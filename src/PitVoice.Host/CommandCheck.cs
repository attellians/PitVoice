using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Responses;

namespace PitVoice.Host;

/// <summary>What a check found (spec: Text check, Microphone check). Nothing of it reaches the game.</summary>
/// <param name="Text">Typed or recognized text.</param>
/// <param name="Words">Words with recognition confidence (null confidence for typed text).</param>
/// <param name="Parse">Parse result; null when the check failed before parsing.</param>
/// <param name="Response">Response the pack would give: the applied response or the rejection response.</param>
/// <param name="Error">Why the check failed (no pack, microphone unavailable, recognition failed), or null.</param>
public sealed record CheckResult(string Text, IReadOnlyList<RecognizedWord> Words, ParseResult? Parse, string Response, string? Error)
{
    public bool Accepted => Parse is ParseResult.Accepted;

    public IReadOnlyList<ParsedCommand> Commands => Parse is ParseResult.Accepted accepted ? accepted.Commands : [];

    /// <summary>Lowest confidence of the commands of an accepted phrase.</summary>
    public double? Confidence => Commands.Count > 0 ? Commands.Min(c => c.Confidence) : null;

    /// <summary>The recording held no speech.</summary>
    public bool NoSpeech => Error is null && Words.Count == 0;

    public static CheckResult Failed(string error) => new(string.Empty, [], null, string.Empty, error);
}

/// <summary>State of the microphone check.</summary>
public enum MicCheckState
{
    Idle,
    Recording,
    Recognizing,
}

/// <summary>
/// The "Check" page (design decision 10): a typed phrase is parsed with the current pack, unit and threshold; a spoken
/// one is recorded from the chosen microphone (click to start, click again or <see cref="AutoStop"/> to stop) and
/// recognized with the host's recognizer. The response is what the pack would say if the commands were applied; the
/// game is never touched. A PTT press during the recording drops the check (the pipeline needs the microphone). The
/// first finished check marks the setup checklist item done.
/// </summary>
public sealed class CommandCheck : IDisposable
{
    public static readonly TimeSpan AutoStop = TimeSpan.FromSeconds(8);

    private readonly AppHost _host;
    private readonly Lock _lock = new();
    private IAudioCapture? _capture;
    private TaskCompletionSource<CheckResult?>? _recording;
    private ITimer? _autoStop;
    private MicCheckState _state;

    public CommandCheck(AppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _host.RecordingStarted += Cancel;
    }

    /// <summary>Raised on any thread when <see cref="State"/> changes.</summary>
    public event Action<MicCheckState>? StateChanged;

    public MicCheckState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>The microphone check needs a loaded model (spec: without a model the button is unavailable).</summary>
    public bool MicrophoneAvailable => _host.Engine is not null;

    public CheckResult CheckText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return CheckResult.Failed("empty phrase");
        }

        if (_host.Pack is not { } pack)
        {
            return CheckResult.Failed(_host.Status.Detail ?? "the language pack is not loaded");
        }

        var result = Evaluate(pack, RecognizedPhrase.FromText(text.Trim()));
        MarkDone();
        return result;
    }

    /// <summary>
    /// Starts recording. The task completes with the result when <see cref="StopMicrophone"/> is called or after
    /// <see cref="AutoStop"/>, with an error result when recording cannot start, and with null when a PTT press or
    /// <see cref="Cancel"/> drops the check.
    /// </summary>
    public Task<CheckResult?> StartMicrophone()
    {
        TaskCompletionSource<CheckResult?> recording;
        lock (_lock)
        {
            if (_recording is not null)
            {
                return _recording.Task;
            }

            if (!MicrophoneAvailable)
            {
                return Task.FromResult<CheckResult?>(CheckResult.Failed("no speech recognition model is loaded"));
            }

            _capture ??= _host.Platform.CreateCheckCapture();
            CaptureStart start;
            try
            {
                start = _capture.Start(_host.Settings.Microphone);
            }
#pragma warning disable CA1031 // A device failure is shown, the app keeps running.
            catch (Exception e)
#pragma warning restore CA1031
            {
                start = CaptureStart.Failed(e.Message);
            }

            if (!start.Started)
            {
                return Task.FromResult<CheckResult?>(CheckResult.Failed(start.Error ?? "the microphone could not be opened"));
            }

            recording = new TaskCompletionSource<CheckResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _recording = recording;
            _autoStop = _host.Time.CreateTimer(_ => StopMicrophone(), null, AutoStop, Timeout.InfiniteTimeSpan);
            _state = MicCheckState.Recording;
        }

        StateChanged?.Invoke(MicCheckState.Recording);
        return recording.Task;
    }

    /// <summary>Stops the recording and recognizes it; the result arrives through the task of <see cref="StartMicrophone"/>.</summary>
    public void StopMicrophone()
    {
        TaskCompletionSource<CheckResult?> recording;
        CaptureResult captured;
        lock (_lock)
        {
            if (_recording is null || _state != MicCheckState.Recording)
            {
                return;
            }

            recording = _recording;
            _autoStop?.Dispose();
            _autoStop = null;
            captured = _capture!.Stop();
            _state = MicCheckState.Recognizing;
        }

        StateChanged?.Invoke(MicCheckState.Recognizing);
        _ = Task.Run(() => Finish(recording, Recognize(captured)));
    }

    /// <summary>Drops a running microphone check without a result.</summary>
    public void Cancel()
    {
        TaskCompletionSource<CheckResult?>? recording;
        lock (_lock)
        {
            recording = _recording;
            if (recording is null)
            {
                return;
            }

            _autoStop?.Dispose();
            _autoStop = null;
            if (_state == MicCheckState.Recording)
            {
                _capture!.Stop();
            }

            _recording = null;
            _state = MicCheckState.Idle;
        }

        recording.TrySetResult(null);
        StateChanged?.Invoke(MicCheckState.Idle);
    }

    public void Dispose()
    {
        _host.RecordingStarted -= Cancel;
        Cancel();
        (_capture as IDisposable)?.Dispose();
    }

    private CheckResult Recognize(CaptureResult captured)
    {
        if (_host.Engine is not { } engine || _host.Pack is not { } pack)
        {
            return CheckResult.Failed("no speech recognition model is loaded");
        }

        var recognition = new SpeechRecognizer(engine).Recognize(captured.Buffer);
        if (!recognition.IsSuccess)
        {
            return CheckResult.Failed(recognition.Error!);
        }

        if (recognition.IsEmpty)
        {
            return new CheckResult(string.Empty, [], null, string.Empty, null);
        }

        return Evaluate(pack, recognition.Phrase);
    }

    private void Finish(TaskCompletionSource<CheckResult?> recording, CheckResult result)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_recording, recording))
            {
                return; // cancelled meanwhile
            }

            _recording = null;
            _state = MicCheckState.Idle;
        }

        if (result.Error is null)
        {
            MarkDone();
        }

        recording.TrySetResult(result);
        StateChanged?.Invoke(MicCheckState.Idle);
    }

    private CheckResult Evaluate(LanguagePack pack, RecognizedPhrase phrase)
    {
        var parse = new CommandParser(pack).Parse(phrase, _host.Settings.ToParseOptions());
        var responses = new ResponseBuilder(pack);
        var response = parse is ParseResult.Accepted
            ? responses.Build(parse, ExecutionResult.Of(ExecutionOutcome.Applied))
            : responses.Build(parse);
        return new CheckResult(phrase.Text, phrase.Words, parse, response, null);
    }

    private void MarkDone()
    {
        if (!_host.Settings.SetupCheckDone)
        {
            _host.Update(s => s with { SetupCheckDone = true });
        }
    }
}
