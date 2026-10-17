using System.Collections.Concurrent;
using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pipeline;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Responses;
using PitVoice.Core.Tests.Pit;
using Xunit;

namespace PitVoice.Core.Tests.Pipeline;

public sealed class VoicePipelineTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly ManualTimeProvider _time = new();
    private readonly FakeTelemetrySource _game;
    private readonly RecordingPitCommandSink _sink;
    private readonly FakeAudioCapture _capture = new();
    private readonly ScriptedAsrEngine _engine = new();
    private readonly FakeCuePlayer _cues = new();
    private readonly EventBus _bus = new();
    private readonly ConcurrentQueue<PipelineEvent> _events = new();
    private readonly SemaphoreSlim _eventArrived = new(0);
    private ITelemetrySource? _telemetryOverride;
    private VoicePipeline? _pipeline;

    public VoicePipelineTests()
    {
        _game = new FakeTelemetrySource(_time);
        _sink = new RecordingPitCommandSink(_game);
        _bus.Subscribe(e =>
        {
            _events.Enqueue(e);
            _eventArrived.Release();
        });
    }

    private VoicePipeline Pipeline => _pipeline ??= new VoicePipeline(new VoicePipelineServices(
        _capture,
        null,
        new SpeechRecognizer(_engine),
        new CommandParser(TestPacks.Ru),
        new ResponseBuilder(TestPacks.Ru),
        ParseOptions.Default,
        new PitExecutor(_telemetryOverride ?? _game, _sink, _time),
        _cues,
        _bus,
        _time));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_pipeline is not null)
        {
            await _pipeline.DisposeAsync();
        }

        _bus.Dispose();
        _eventArrived.Dispose();
    }

    private void Press() => Pipeline.OnTransition(new PttTransition.Started());

    private void Release() => Pipeline.OnTransition(new PttTransition.Stopped(RecordingStopReason.Released));

    private void Say(string text, SemaphoreSlim? gate = null)
    {
        _engine.Say(text, gate);
        Press();
        Release();
    }

    private IReadOnlyList<PipelineEvent> EventsOf(int phraseId) => [.. _events.Where(e => e.PhraseId == phraseId)];

    private async Task<T> WaitFor<T>(Func<T, bool>? match = null)
        where T : PipelineEvent
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (true)
        {
            if (_events.OfType<T>().FirstOrDefault(e => match?.Invoke(e) ?? true) is { } found)
            {
                return found;
            }

            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !await _eventArrived.WaitAsync(left))
            {
                throw new TimeoutException($"no {typeof(T).Name}; got: {string.Join(", ", _events.Select(e => e.GetType().Name))}");
            }
        }
    }

    private Task<ResultSignaled> Result(int phraseId) => WaitFor<ResultSignaled>(r => r.PhraseId == phraseId);

    // --- End-to-end processing

    [Fact]
    public async Task PitStopFromTheWheel_AppliedWithSuccessCue()
    {
        Say("меняй шины и залей двадцать пять литров");

        var result = await Result(1);

        Assert.Equal(Cue.Success, result.Cue);
        Assert.False(result.Cancelled);
        Assert.Equal("меняем все шины, заливаем 25 литров", result.ResponseText);
        Assert.Equal(Wheels.All, _game.Pit.Wheels);
        Assert.Equal(25, _game.Pit.FuelToAdd);
        Assert.Equal([Cue.Start, Cue.Success], _cues.Played);
    }

    [Fact]
    public async Task GameNotRunning_NothingSent_ErrorCue()
    {
        _game.State = ConnectionState.NotRunning;

        Say("залей тридцать литров");

        var result = await Result(1);
        Assert.Equal(Cue.Error, result.Cue);
        Assert.Equal(TestPacks.Ru.Responses[ResponseBuilder.NotConnectedKey], result.ResponseText);
        Assert.Empty(_sink.Sent);
        Assert.Equal(ExecutionOutcome.NotConnected, (await WaitFor<ApplyFailed>()).Outcome);
    }

    [Fact]
    public async Task Silence_NotUnderstood_ErrorCue()
    {
        _capture.Recordings.Enqueue(AudioBuffer.Empty);
        Press();
        Release();

        var result = await Result(1);
        Assert.Equal(Cue.Error, result.Cue);
        Assert.Equal(TestPacks.Ru.Responses[ResponseBuilder.NotUnderstoodKey], result.ResponseText);
        Assert.Equal(0, _engine.Calls);
        Assert.Empty(_sink.Sent);
        Assert.Equal(RejectReason.Empty, (await WaitFor<CommandsRejected>()).Reason);
    }

    [Fact]
    public async Task LowConfidence_NothingSent_ErrorCue()
    {
        _engine.Say(new RecognizedPhrase([new RecognizedWord("залей", 0.3), new RecognizedWord("тридцать", 0.9), new RecognizedWord("литров", 0.9)]));
        Press();
        Release();

        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Equal(RejectReason.LowConfidence, (await WaitFor<CommandsRejected>()).Reason);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task CancelPhrase_IsRejected_ErrorCue_NothingSent()
    {
        Say("отмена");

        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Empty(_sink.Sent);
    }

    [Fact]
    public async Task PartiallyApplied_ErrorCue()
    {
        _game.Ignored.Add(PitCommand.Fuel);

        Say("меняй шины и залей двадцать пять литров");

        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Equal(ExecutionOutcome.PartiallyApplied, (await WaitFor<ApplyFailed>()).Outcome);
    }

    [Fact]
    public async Task StartCue_IsTrimmedFromTheRecording()
    {
        Say("залей тридцать литров");

        var captured = await WaitFor<AudioCaptured>();
        Assert.Equal(TimeSpan.FromSeconds(1) - FakeCuePlayer.StartDuration - VoicePipeline.StartCueMargin, captured.Duration);
        Assert.Equal(FakeAudioCapture.DeviceName, captured.DeviceName);
    }

    [Fact]
    public async Task MutedCues_NoTrim_EventsStillPublished()
    {
        _cues.Muted = true;

        Say("залей тридцать литров");

        var result = await Result(1);
        Assert.False(result.Cancelled);
        Assert.Equal(TimeSpan.FromSeconds(1), (await WaitFor<AudioCaptured>()).Duration);
        Assert.Empty(_cues.Played);
    }

    [Fact]
    public async Task SlowConfirmation_CueAfterOutcome_LatencyShowsIt()
    {
        _game.Delay = TimeSpan.FromSeconds(1.2);

        Say("залей тридцать литров");

        var result = await Result(1);
        Assert.Equal(Cue.Success, result.Cue);
        Assert.True(result.LatencyFromStop >= TimeSpan.FromSeconds(1.2), $"latency {result.LatencyFromStop}");
        Assert.DoesNotContain(_events, e => e is PipelineFailed);
    }

    // --- Pipeline events

    [Fact]
    public async Task Events_OfAppliedPhrase_InStageOrder()
    {
        Say("залей тридцать литров");
        await Result(1);

        var events = EventsOf(1);
        Assert.Equal(
            [typeof(PttPressed), typeof(PttReleased), typeof(AudioCaptured), typeof(SpeechRecognized), typeof(CommandsParsed), typeof(CommandsApplied), typeof(ResultSignaled)],
            events.Select(e => e.GetType()));
        Assert.Equal("залей тридцать литров", Assert.IsType<SpeechRecognized>(events[3]).Text);
        Assert.False(Assert.IsType<PttReleased>(events[1]).Ignored);
    }

    [Fact]
    public async Task Events_OfRejectedPhrase_NoApplication()
    {
        Say("спой песню");
        await Result(1);

        var types = EventsOf(1).Select(e => e.GetType()).ToList();
        Assert.Equal(types.IndexOf(typeof(SpeechRecognized)) + 1, types.IndexOf(typeof(CommandsRejected)));
        Assert.DoesNotContain(typeof(CommandsParsed), types);
        Assert.DoesNotContain(typeof(CommandsApplied), types);
        Assert.DoesNotContain(typeof(ApplyFailed), types);
    }

    [Fact]
    public async Task IgnoredShortPress_OnlyPressAndIgnoredRelease()
    {
        Press();
        Pipeline.OnTransition(new PttTransition.Discarded());
        Say("залей тридцать литров"); // phrase 2 proves the pipeline went on
        await Result(2);

        var events = EventsOf(1);
        Assert.Equal([typeof(PttPressed), typeof(PttReleased)], events.Select(e => e.GetType()));
        Assert.True(((PttReleased)events[1]).Ignored);
        Assert.Equal(1, _engine.Calls);
        Assert.Equal(2, _capture.Stops);
    }

    [Fact]
    public async Task ShortPressThroughStateMachine_NoRecognitionNoResultCue()
    {
        var clock = new Ptt.SteppedTimeProvider();
        var key = PttKey.KeyboardKey(0x7C);
        using var machine = new PttStateMachine(new PttBinding(key, "F13"), PttMode.Hold, clock);
        machine.Transition += Pipeline.OnTransition;

        machine.OnInput(new PttInput(key, true, "F13"));
        clock.Advance(TimeSpan.FromMilliseconds(150));
        machine.OnInput(new PttInput(key, false, "F13"));

        var released = await WaitFor<PttReleased>();
        Assert.True(released.Ignored);
        Assert.Equal([Cue.Start], _cues.Played);
        Assert.Equal(0, _engine.Calls);
    }

    // --- Press during processing, cue cancellation, sequential execution

    [Fact]
    public async Task PressDuringRecognition_PreviousCueCancelled_PreviousStillExecuted()
    {
        using var gate = new SemaphoreSlim(0);
        Say("меняй шины", gate);
        Press(); // phrase 2 starts while phrase 1 is being recognized
        gate.Release();
        _engine.Say("залей двадцать литров");
        Release();

        var first = await Result(1);
        var second = await Result(2);
        Assert.True(first.Cancelled);
        Assert.False(second.Cancelled);
        Assert.Equal(Wheels.All, _game.Pit.Wheels); // phrase 1 reached the game
        Assert.Equal(20, _game.Pit.FuelToAdd);
        Assert.Equal([Cue.Start, Cue.Start, Cue.Success], _cues.Played);
    }

    [Fact]
    public async Task PressWhileResultCuePlays_CueCutAndStartPlays_SelectionStays()
    {
        Say("меняй шины");
        await Result(1);

        Press();

        Assert.Equal(["stop", "play Start", "play Success", "stop", "play Start"], _cues.Log);
        Assert.Equal(Wheels.All, _game.Pit.Wheels);
    }

    [Fact]
    public async Task ShortPressDuringProcessing_PreviousCueCancelled_NothingRecognized()
    {
        using var gate = new SemaphoreSlim(0);
        Say("меняй шины", gate);
        Press();
        Pipeline.OnTransition(new PttTransition.Discarded());
        gate.Release();

        Assert.True((await Result(1)).Cancelled);
        Assert.Equal([Cue.Start, Cue.Start], _cues.Played);
        Assert.Equal(1, _engine.Calls);
    }

    [Fact]
    public async Task FirstPhraseNotApplied_SecondExecutesNormally_OnlySecondCue()
    {
        using var gate = new SemaphoreSlim(0);
        _game.Ignored.Add(PitCommand.LF);
        _game.Ignored.Add(PitCommand.RF);
        _game.Ignored.Add(PitCommand.LR);
        _game.Ignored.Add(PitCommand.RR);
        Say("меняй шины", gate);
        Press();
        _engine.Say("залей двадцать литров");
        Release();
        gate.Release();

        Assert.True((await Result(1)).Cancelled);
        Assert.Equal(ExecutionOutcome.NotApplied, (await WaitFor<ApplyFailed>(e => e.PhraseId == 1)).Outcome);
        var second = await Result(2);
        Assert.Equal(Cue.Success, second.Cue);
        Assert.Equal([Cue.Start, Cue.Start, Cue.Success], _cues.Played);
    }

    [Fact]
    public async Task Phrases_AreExecutedOneAtATime_InRecordingOrder()
    {
        _game.Delay = TimeSpan.FromMilliseconds(500);
        using var gate = new SemaphoreSlim(0);
        Say("меняй шины", gate);
        Say("залей двадцать литров");
        gate.Release();

        await Result(2);
        var all = _events.ToList();
        var firstOutcome = all.FindIndex(e => e is CommandsApplied { PhraseId: 1 });
        var secondRecognized = all.FindIndex(e => e is SpeechRecognized { PhraseId: 2 });
        Assert.True(firstOutcome >= 0 && firstOutcome < secondRecognized, "phrase 2 was processed before phrase 1 finished");
        Assert.Equal(
            new[] { PitCommand.LF, PitCommand.RF, PitCommand.LR, PitCommand.RR }.Order(),
            _sink.Sent.Take(4).Select(s => s.Command).Order());
        Assert.Equal(PitCommand.Fuel, _sink.Sent[^1].Command);
    }

    // --- Stage failures

    [Fact]
    public async Task MicrophoneMissing_CaptureFailure_ErrorCue()
    {
        _capture.StartError = "no microphone";
        Press();
        Release();

        var failed = await WaitFor<PipelineFailed>();
        Assert.Equal(PipelineStage.Capture, failed.Stage);
        Assert.Equal("no microphone", failed.Error);
        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Equal(0, _engine.Calls);
    }

    [Fact]
    public async Task RecognitionThrows_ErrorCue_NextPhraseApplied()
    {
        _engine.Throw("engine crashed");
        Press();
        Release();
        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Equal(PipelineStage.Recognition, (await WaitFor<PipelineFailed>()).Stage);
        Assert.Empty(_sink.Sent);

        Say("залей тридцать литров");

        Assert.Equal(Cue.Success, (await Result(2)).Cue);
        Assert.Equal(30, _game.Pit.FuelToAdd);
    }

    [Fact]
    public async Task ModelNotLoaded_RecognitionFailure()
    {
        _engine.IsLoaded = false;

        Say("залей тридцать литров");

        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Contains("not loaded", (await WaitFor<PipelineFailed>()).Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParsingThrows_ErrorCue()
    {
        _engine.Say(new RecognizedPhrase([new RecognizedWord(null!)]));
        Press();
        Release();

        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Equal(PipelineStage.Parsing, (await WaitFor<PipelineFailed>()).Stage);
    }

    [Fact]
    public async Task ExecutionThrows_ErrorCue_HostGoesOn()
    {
        _telemetryOverride = new BrokenTelemetrySource();

        Say("залей тридцать литров");
        Say("отмена"); // no longer a command: the next phrase is still handled and rejected

        Assert.Equal(Cue.Error, (await Result(1)).Cue);
        Assert.Equal(PipelineStage.Execution, (await WaitFor<PipelineFailed>()).Stage);
        Assert.Equal(Cue.Error, (await Result(2)).Cue);
    }

    [Fact]
    public async Task CuePlaybackFails_OnlyReported_CommandsStayApplied()
    {
        _cues.Error = "no output device";

        Say("меняй шины");

        var result = await Result(1);
        Assert.Equal(Cue.Success, result.Cue);
        Assert.Equal(Wheels.All, _game.Pit.Wheels);
        var failures = await WaitFor<PipelineFailed>(f => f.PhraseId == 1 && f.Stage == PipelineStage.Cue);
        Assert.Equal("no output device", failures.Error);
    }

    // --- Subscriber isolation

    [Fact]
    public async Task FailingAndSlowSubscribers_DoNotDelayTheCueOrOthers()
    {
        using var slowRelease = new ManualResetEventSlim();
        _bus.Subscribe(_ => throw new InvalidOperationException("subscriber broke"));
        _bus.Subscribe(_ => slowRelease.Wait(TimeSpan.FromSeconds(2)));

        Say("меняй шины");

        Assert.Equal(Cue.Success, (await Result(1)).Cue);
        Assert.Equal([Cue.Start, Cue.Success], _cues.Played);
        slowRelease.Set();
    }
}
