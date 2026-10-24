using PitVoice.Core.Cues;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;

namespace PitVoice.Core.Events;

/// <summary>
/// Something that happened to one phrase on its way from the PTT press to the result cue (TZ §7). Events of one phrase
/// share <see cref="PhraseId"/> and are published in stage order.
/// </summary>
/// <param name="PhraseId">Number of the phrase in this run, starting at 1.</param>
/// <param name="Timestamp">When the event happened.</param>
public abstract record PipelineEvent(int PhraseId, DateTimeOffset Timestamp);

/// <summary>Recording started.</summary>
public sealed record PttPressed(int PhraseId, DateTimeOffset Timestamp) : PipelineEvent(PhraseId, Timestamp);

/// <summary>Recording stopped. An ignored release (too short in hold mode) ends the phrase without recognition.</summary>
/// <param name="PhraseId">Phrase id.</param>
/// <param name="Timestamp">Time.</param>
/// <param name="Ignored">True when the press was too short and the recording was dropped.</param>
/// <param name="Reason">Why the recording stopped; null for an ignored press.</param>
public sealed record PttReleased(int PhraseId, DateTimeOffset Timestamp, bool Ignored, RecordingStopReason? Reason)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>Audio handed to recognition.</summary>
public sealed record AudioCaptured(int PhraseId, DateTimeOffset Timestamp, TimeSpan Duration, bool Truncated, string? DeviceName)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>Recognizer output with per-word confidence; empty on silence.</summary>
public sealed record SpeechRecognized(int PhraseId, DateTimeOffset Timestamp, RecognizedPhrase Phrase, TimeSpan RecognitionTime)
    : PipelineEvent(PhraseId, Timestamp)
{
    public string Text => Phrase.Text;
}

/// <summary>The phrase was understood.</summary>
public sealed record CommandsParsed(int PhraseId, DateTimeOffset Timestamp, IReadOnlyList<ParsedCommand> Commands)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>The phrase was rejected by the parser; nothing reaches the game.</summary>
public sealed record CommandsRejected(int PhraseId, DateTimeOffset Timestamp, RejectReason Reason, string Details)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>Execution ended with <see cref="ExecutionOutcome.Applied"/>.</summary>
public sealed record CommandsApplied(int PhraseId, DateTimeOffset Timestamp, IReadOnlyList<SentCommand> Sent)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>Execution ended with any other outcome.</summary>
public sealed record ApplyFailed(int PhraseId, DateTimeOffset Timestamp, ExecutionOutcome Outcome, IReadOnlyList<ParsedCommand> Unconfirmed)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>The result cue of the phrase played or was cancelled by a newer press.</summary>
/// <param name="PhraseId">Phrase id.</param>
/// <param name="Timestamp">Time.</param>
/// <param name="Cue">Cue chosen for the outcome.</param>
/// <param name="ResponseText">Text response of the language pack for the outcome.</param>
/// <param name="Cancelled">True when a newer press cancelled the cue, so it did not play.</param>
/// <param name="LatencyFromStop">From the end of the recording to the cue.</param>
/// <param name="ShortText">Short responses of the applied commands ("все шины · +25 л"); empty when not applied.</param>
public sealed record ResultSignaled(
    int PhraseId, DateTimeOffset Timestamp, Cue Cue, string ResponseText, bool Cancelled, TimeSpan LatencyFromStop, string ShortText = "")
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>A stage failed. The phrase ends with the error cue (unless the cue itself failed); the host keeps running.</summary>
public sealed record PipelineFailed(int PhraseId, DateTimeOffset Timestamp, PipelineStage Stage, string Error)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>Something worth telling the user that does not stop the phrase, e.g. a missing device replaced by the default.</summary>
public sealed record PipelineWarning(int PhraseId, DateTimeOffset Timestamp, PipelineStage Stage, string Message)
    : PipelineEvent(PhraseId, Timestamp);

/// <summary>Stage of the pipeline, for failures and warnings.</summary>
public enum PipelineStage
{
    Capture,
    Recognition,
    Parsing,
    Execution,
    Cue,
}
