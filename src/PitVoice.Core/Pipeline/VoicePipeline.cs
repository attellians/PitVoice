using System.Threading.Channels;
using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Cues;
using PitVoice.Core.Events;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Responses;

namespace PitVoice.Core.Pipeline;

/// <summary>Everything <see cref="VoicePipeline"/> works with; the host composes it.</summary>
/// <param name="Capture">Microphone recording.</param>
/// <param name="Microphone">Microphone name; null means the system default.</param>
/// <param name="Recognizer">Recognizer with the loaded model of the pack's language.</param>
/// <param name="Parser">Parser of the language pack.</param>
/// <param name="Responses">Text responses of the language pack.</param>
/// <param name="ParseOptions">Default unit and confidence threshold.</param>
/// <param name="Executor">Sends commands to the game and waits for confirmation.</param>
/// <param name="Cues">Cue player.</param>
/// <param name="Events">Event bus.</param>
/// <param name="Time">Clock for timestamps and latency.</param>
public sealed record VoicePipelineServices(
    IAudioCapture Capture,
    string? Microphone,
    SpeechRecognizer Recognizer,
    CommandParser Parser,
    ResponseBuilder Responses,
    ParseOptions ParseOptions,
    PitExecutor Executor,
    IAudioCuePlayer Cues,
    IEventBus Events,
    TimeProvider Time)
{
    /// <summary>Numbers of phrases; a host that rebuilds the pipeline passes one source so numbers never repeat.</summary>
    public PhraseIdSource PhraseIds { get; init; } = new();
}

/// <summary>Hands out phrase numbers starting at 1. Thread-safe.</summary>
public sealed class PhraseIdSource
{
    private int _last;

    public int Next() => Interlocked.Increment(ref _last);
}

/// <summary>
/// The whole way from the PTT button to the result cue (design decision 9). Feed it the transitions of
/// <see cref="PttStateMachine"/>. A start plays the <c>start</c> cue (cutting whatever plays) and opens the microphone; a
/// stop puts the phrase into the single processing queue: recognition, parsing, execution and the result cue, one phrase
/// at a time in recording order. Every press bumps a generation counter; a phrase plays its result cue only when no press
/// happened since its own, otherwise the cue is reported as cancelled. A failing stage publishes
/// <see cref="PipelineFailed"/>, ends the phrase with the <c>error</c> cue and leaves the pipeline working.
/// </summary>
public sealed class VoicePipeline : IAsyncDisposable
{
    /// <summary>Extra audio dropped after the start cue so its tail from the speakers does not reach the recognizer.</summary>
    public static readonly TimeSpan StartCueMargin = TimeSpan.FromMilliseconds(30);

    private readonly VoicePipelineServices _services;
    private readonly Channel<Phrase> _queue = Channel.CreateUnbounded<Phrase>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _lock = new();
    private readonly Task _worker;
    private int _generation;
    private int _pending;
    private Recording? _recording;

    public VoicePipeline(VoicePipelineServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _worker = Task.Run(RunAsync);
    }

    /// <summary>True while a phrase is recorded or waits for its result cue; a host applies new settings only when false.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return _recording is not null || _pending > 0;
            }
        }
    }

    public void OnTransition(PttTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        lock (_lock)
        {
            switch (transition)
            {
                case PttTransition.Started:
                    OnStarted();
                    break;
                case PttTransition.Discarded:
                    OnDiscarded();
                    break;
                case PttTransition.Stopped stopped:
                    OnStopped(stopped.Reason);
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _worker.ConfigureAwait(false);
        lock (_lock)
        {
            if (_recording is { Capture.Started: true })
            {
                _services.Capture.Stop();
            }

            _recording = null;
            _services.Cues.Stop();
        }

        _stopping.Dispose();
    }

    private DateTimeOffset Now => _services.Time.GetUtcNow();

    private void Publish(PipelineEvent pipelineEvent) => _services.Events.Publish(pipelineEvent);

    private void OnStarted()
    {
        if (_recording is not null)
        {
            return;
        }

        _generation++;
        var id = _services.PhraseIds.Next();
        Publish(new PttPressed(id, Now));

        _services.Cues.Stop();
        var cue = Play(id, Cue.Start);
        var trim = cue.Played ? _services.Cues.GetDuration(Cue.Start) + StartCueMargin : TimeSpan.Zero;

        CaptureStart capture;
        try
        {
            capture = _services.Capture.Start(_services.Microphone);
        }
#pragma warning disable CA1031 // A device failure must end this phrase only.
        catch (Exception e)
#pragma warning restore CA1031
        {
            capture = CaptureStart.Failed(e.Message);
        }

        if (capture.Warning is not null)
        {
            Publish(new PipelineWarning(id, Now, PipelineStage.Capture, capture.Warning));
        }

        _recording = new Recording(id, _generation, capture, trim);
    }

    private void OnDiscarded()
    {
        if (_recording is not { } recording)
        {
            return;
        }

        _recording = null;
        Publish(new PttReleased(recording.Id, Now, Ignored: true, Reason: null));
        if (recording.Capture.Started)
        {
            StopCapture(recording.Id);
        }
    }

    private void OnStopped(RecordingStopReason reason)
    {
        if (_recording is not { } recording)
        {
            return;
        }

        _recording = null;
        var stoppedAt = _services.Time.GetTimestamp();
        Publish(new PttReleased(recording.Id, Now, Ignored: false, reason));
        if (!recording.Capture.Started)
        {
            _pending++;
            _queue.Writer.TryWrite(new Phrase(recording.Id, recording.Generation, AudioBuffer.Empty, stoppedAt,
                recording.Capture.Error ?? "the microphone could not be opened"));
            return;
        }

        var buffer = Trim(StopCapture(recording.Id), recording.Trim);
        Publish(new AudioCaptured(recording.Id, Now, buffer.Duration, buffer.Truncated, recording.Capture.DeviceName));
        _pending++;
        _queue.Writer.TryWrite(new Phrase(recording.Id, recording.Generation, buffer, stoppedAt, CaptureError: null));
    }

    private AudioBuffer StopCapture(int id)
    {
        CaptureResult result;
        try
        {
            result = _services.Capture.Stop();
        }
#pragma warning disable CA1031 // A device failure must end this phrase only.
        catch (Exception e)
#pragma warning restore CA1031
        {
            result = new CaptureResult(AudioBuffer.Empty, e.Message);
        }

        if (result.Error is not null)
        {
            Publish(new PipelineWarning(id, Now, PipelineStage.Capture, result.Error));
        }

        return result.Buffer;
    }

    /// <summary>Drops the part of the recording where the start cue sounds (design decision 9).</summary>
    internal static AudioBuffer Trim(AudioBuffer buffer, TimeSpan trim)
    {
        var samples = (int)(trim.Ticks * AudioBuffer.SampleRate / TimeSpan.TicksPerSecond);
        if (samples <= 0 || buffer.IsEmpty)
        {
            return buffer;
        }

        var source = buffer.ToArray();
        return samples >= source.Length
            ? new AudioBuffer([], buffer.Truncated)
            : new AudioBuffer(source[samples..], buffer.Truncated);
    }

    private async Task RunAsync()
    {
        await foreach (var phrase in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (await ProcessAsync(phrase).ConfigureAwait(false) is { } result)
            {
                Signal(phrase, result.Cue, result.Response);
            }

            lock (_lock)
            {
                _pending--;
            }
        }
    }

    /// <summary>Recognition, parsing and execution of one phrase; null when the pipeline is being stopped.</summary>
    private async Task<(Cue Cue, string Response)?> ProcessAsync(Phrase phrase)
    {
        if (phrase.CaptureError is { } captureError)
        {
            return Fail(phrase.Id, PipelineStage.Capture, captureError);
        }

        RecognitionResult recognition;
        try
        {
            recognition = _services.Recognizer.Recognize(phrase.Audio);
        }
#pragma warning disable CA1031 // Any stage failure ends this phrase only; the next press must work.
        catch (Exception e)
        {
            return Fail(phrase.Id, PipelineStage.Recognition, e.Message);
        }

        if (!recognition.IsSuccess)
        {
            return Fail(phrase.Id, PipelineStage.Recognition, recognition.Error!);
        }

        SpeechParse speech;
        try
        {
            // A word unsure only of its form gets a second look with the alternatives (design D13), and a wheel name
            // heard in other forms is read from the alternatives of forms; the event shows the words that were parsed.
            speech = SpeechParsing.Parse(_services.Parser, _services.Recognizer, phrase.Audio, recognition.Phrase, _services.ParseOptions);
        }
        catch (Exception e)
        {
            Publish(new SpeechRecognized(phrase.Id, Now, recognition.Phrase, recognition.RecognitionTime));
            return Fail(phrase.Id, PipelineStage.Parsing, e.Message);
        }

        Publish(new SpeechRecognized(phrase.Id, Now, speech.Phrase, recognition.RecognitionTime));
        var parse = speech.Result;

        if (parse is not ParseResult.Accepted accepted)
        {
            var rejected = (ParseResult.Rejected)parse;
            Publish(new CommandsRejected(phrase.Id, Now, rejected.Reason, rejected.Details));
            return (Cue.Error, _services.Responses.Build(parse));
        }

        Publish(new CommandsParsed(phrase.Id, Now, accepted.Commands));

        ExecutionResult execution;
        string response;
        try
        {
            execution = await _services.Executor.ExecuteAsync(accepted, speech.Phrase.Text,_stopping.Token).ConfigureAwait(false);
            response = _services.Responses.Build(parse, execution);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            return Fail(phrase.Id, PipelineStage.Execution, e.Message);
        }
#pragma warning restore CA1031

        Publish(execution.Outcome == ExecutionOutcome.Applied
            ? new CommandsApplied(phrase.Id, Now, execution.Sent)
            : new ApplyFailed(phrase.Id, Now, execution.Outcome, execution.Unconfirmed));
        return (CueSelector.ForResult(parse, execution), response);
    }

    private (Cue, string) Fail(int id, PipelineStage stage, string error)
    {
        Publish(new PipelineFailed(id, Now, stage, error));
        return (Cue.Error, string.Empty);
    }

    private void Signal(Phrase phrase, Cue cue, string response)
    {
        lock (_lock)
        {
            var latency = _services.Time.GetElapsedTime(phrase.StoppedAt);
            if (phrase.Generation != _generation)
            {
                Publish(new ResultSignaled(phrase.Id, Now, cue, response, Cancelled: true, latency));
                return;
            }

            var playback = PlaySafely(cue);
            Publish(new ResultSignaled(phrase.Id, Now, cue, response, Cancelled: false, latency));
            Report(phrase.Id, playback);
        }
    }

    private CuePlayback Play(int id, Cue cue)
    {
        var playback = PlaySafely(cue);
        Report(id, playback);
        return playback;
    }

    private CuePlayback PlaySafely(Cue cue)
    {
        try
        {
            return _services.Cues.Play(cue);
        }
#pragma warning disable CA1031 // A cue failure is only reported (spec: Cue playback fails).
        catch (Exception e)
#pragma warning restore CA1031
        {
            return CuePlayback.Failed(e.Message);
        }
    }

    private void Report(int id, CuePlayback playback)
    {
        if (playback.Warning is not null)
        {
            Publish(new PipelineWarning(id, Now, PipelineStage.Cue, playback.Warning));
        }

        if (playback.Error is not null)
        {
            Publish(new PipelineFailed(id, Now, PipelineStage.Cue, playback.Error));
        }
    }

    private sealed record Recording(int Id, int Generation, CaptureStart Capture, TimeSpan Trim);

    private sealed record Phrase(int Id, int Generation, AudioBuffer Audio, long StoppedAt, string? CaptureError);
}
