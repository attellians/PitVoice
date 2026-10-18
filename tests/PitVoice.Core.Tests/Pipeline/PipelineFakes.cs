using System.Collections.Concurrent;
using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Cues;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;

namespace PitVoice.Core.Tests.Pipeline;

/// <summary>Microphone that returns one second of audio per recording unless told otherwise.</summary>
internal sealed class FakeAudioCapture : IAudioCapture
{
    public const string DeviceName = "Test Mic";

    public bool IsRecording { get; private set; }

    /// <summary>When set, <see cref="Start"/> fails with this error (no microphone).</summary>
    public string? StartError { get; set; }

    /// <summary>Audio returned by the next stops, in order; one second of silence when empty.</summary>
    public Queue<AudioBuffer> Recordings { get; } = new();

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public CaptureStart Start(string? deviceName = null)
    {
        Starts++;
        if (StartError is not null)
        {
            return CaptureStart.Failed(StartError);
        }

        IsRecording = true;
        return CaptureStart.Ok(DeviceName);
    }

    public CaptureResult Stop()
    {
        Stops++;
        IsRecording = false;
        var buffer = Recordings.Count > 0 ? Recordings.Dequeue() : new AudioBuffer(new short[AudioBuffer.SampleRate]);
        return new CaptureResult(buffer, null);
    }
}

/// <summary>Recognizer that returns queued phrases in order; a phrase can be held until the test releases it.</summary>
internal sealed class ScriptedAsrEngine : IAsrEngine
{
    private readonly ConcurrentQueue<Step> _steps = new();

    public bool IsLoaded { get; set; } = true;

    public string? Language => "ru";

    public int Calls => _calls;

    private int _calls;

    public void Say(string text, SemaphoreSlim? gate = null) =>
        _steps.Enqueue(new Step(RecognizedPhrase.FromText(text), null, gate));

    public void Say(RecognizedPhrase phrase) => _steps.Enqueue(new Step(phrase, null, null));

    public void Throw(string message) => _steps.Enqueue(new Step(null, new InvalidOperationException(message), null));

    public Task<AsrLoadResult> LoadAsync(LanguagePack pack, string? modelPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(AsrLoadResult.Loaded);

    public RecognizedPhrase Recognize(AudioBuffer buffer)
    {
        Interlocked.Increment(ref _calls);
        if (!_steps.TryDequeue(out var step))
        {
            throw new InvalidOperationException("no scripted phrase left");
        }

        if (step.Gate is not null && !step.Gate.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("the test never released the phrase");
        }

        return step.Error is not null ? throw step.Error : step.Phrase!;
    }

    /// <summary>What <see cref="RecognizeFormAlternatives"/> returns for every phrase.</summary>
    public IReadOnlyList<RecognizedPhrase> FormAlternatives { get; set; } = [];

    public IReadOnlyList<RecognizedPhrase> RecognizeFormAlternatives(AudioBuffer buffer, RecognizedPhrase phrase, Func<string, string> readAs) =>
        FormAlternatives;

    public void Unload()
    {
    }

    private sealed record Step(RecognizedPhrase? Phrase, Exception? Error, SemaphoreSlim? Gate);
}

/// <summary>Remembers every play and stop.</summary>
internal sealed class FakeCuePlayer : IAudioCuePlayer
{
    public static readonly TimeSpan StartDuration = TimeSpan.FromMilliseconds(120);

    private readonly ConcurrentQueue<string> _log = new();

    public IReadOnlyList<string> Log => [.. _log];

    public IReadOnlyList<Cue> Played => [.. _log.Where(l => l.StartsWith("play ", StringComparison.Ordinal)).Select(l => Enum.Parse<Cue>(l[5..]))];

    public bool Muted { get; set; }

    public string? Error { get; set; }

    public CuePlayback Play(Cue cue)
    {
        if (Error is not null)
        {
            return CuePlayback.Failed(Error);
        }

        if (Muted)
        {
            return CuePlayback.Muted;
        }

        _log.Enqueue($"play {cue}");
        return CuePlayback.Ok("Test Speakers");
    }

    public void Stop() => _log.Enqueue("stop");

    public TimeSpan GetDuration(Cue cue) => cue == Cue.Start ? StartDuration : TimeSpan.FromMilliseconds(300);
}

/// <summary>Telemetry that fails on every read, for an execution stage that throws.</summary>
internal sealed class BrokenTelemetrySource : ITelemetrySource
{
    public TelemetrySnapshot Current => throw new IOException("shared memory is gone");
}
