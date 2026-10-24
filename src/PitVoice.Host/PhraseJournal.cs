using PitVoice.Core.Events;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;

namespace PitVoice.Host;

/// <summary>How a phrase ended, as the main page shows it (spec: Recent phrases).</summary>
public enum PhraseOutcome
{
    /// <summary>Still recorded or processed.</summary>
    InProgress,

    Applied,

    PartiallyApplied,

    NotApplied,

    NotConnected,

    NotInCar,

    /// <summary>The car has no such tire compound; nothing was sent.</summary>
    CompoundUnavailable,

    /// <summary>The parser rejected the phrase (not understood, conflict, low confidence, silence).</summary>
    NotUnderstood,

    /// <summary>A stage failed; <see cref="PhraseEntry.FailedStage"/> and <see cref="PhraseEntry.Error"/> say which and why.</summary>
    Failed,

    /// <summary>The pipeline was not available (model loading or missing); <see cref="PhraseEntry.Unavailable"/> says why.</summary>
    Unavailable,
}

/// <summary>One line of the recent phrases.</summary>
/// <param name="PhraseId">Phrase number of this run.</param>
/// <param name="PressedAt">When the PTT was pressed.</param>
/// <param name="Text">Recognized text; null before recognition, empty for silence.</param>
/// <param name="Outcome">How the phrase ended.</param>
/// <param name="Response">Text response of the pack; empty when there is none.</param>
/// <param name="Rejected">Why the parser rejected the phrase, for <see cref="PhraseOutcome.NotUnderstood"/>.</param>
/// <param name="FailedStage">Failed stage, for <see cref="PhraseOutcome.Failed"/>.</param>
/// <param name="Error">Error of the failed stage.</param>
/// <param name="Unavailable">Why the pipeline was unavailable, for <see cref="PhraseOutcome.Unavailable"/>.</param>
public sealed record PhraseEntry(
    int PhraseId,
    DateTimeOffset PressedAt,
    string? Text,
    PhraseOutcome Outcome,
    string Response,
    RejectReason? Rejected,
    PipelineStage? FailedStage,
    string? Error,
    StatusReason? Unavailable)
{
    /// <summary>True once the result cue was signaled (or the press was refused).</summary>
    public bool Finished { get; init; }

    /// <summary>Short responses of the applied commands for the result chip ("все шины · +25 л"); empty otherwise.</summary>
    public string Short { get; init; } = string.Empty;
}

/// <summary>
/// The last <see cref="Capacity"/> phrases of this run, built from the pipeline events by phrase id (design decision 11).
/// Ignored short presses are not shown. Kept in memory only; it lives in the host, so it survives session rebuilds and a
/// closed window. Thread-safe; <see cref="Changed"/> is raised on the event bus thread.
/// </summary>
public sealed class PhraseJournal
{
    public const int Capacity = 20;

    private readonly Lock _lock = new();
    private readonly List<PhraseEntry> _entries = [];
    private int _total;
    private int _applied;

    /// <summary>The list changed.</summary>
    public event Action? Changed;

    /// <summary>Phrases of this run that ended (cue signaled or press refused), beyond the <see cref="Capacity"/> shown (spec: Phrase counter).</summary>
    public int TotalCount
    {
        get
        {
            lock (_lock)
            {
                return _total;
            }
        }
    }

    /// <summary>Of <see cref="TotalCount"/>, the phrases whose commands were applied.</summary>
    public int AppliedCount
    {
        get
        {
            lock (_lock)
            {
                return _applied;
            }
        }
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<PhraseEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public void OnEvent(PipelineEvent pipelineEvent)
    {
        ArgumentNullException.ThrowIfNull(pipelineEvent);
        lock (_lock)
        {
            if (!Apply(pipelineEvent))
            {
                return;
            }
        }

        Changed?.Invoke();
    }

    private bool Apply(PipelineEvent e)
    {
        if (e is PttPressed pressed)
        {
            _entries.Insert(0, new PhraseEntry(pressed.PhraseId, pressed.Timestamp, null, PhraseOutcome.InProgress, string.Empty, null, null, null, null));
            if (_entries.Count > Capacity)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }

            return true;
        }

        var index = _entries.FindIndex(x => x.PhraseId == e.PhraseId);
        if (index < 0)
        {
            return false;
        }

        var entry = _entries[index];
        switch (e)
        {
            case PttReleased { Ignored: true }:
                _entries.RemoveAt(index);
                return true;

            case SpeechRecognized speech:
                entry = entry with { Text = speech.Text };
                break;

            case CommandsRejected rejected:
                entry = entry with { Outcome = PhraseOutcome.NotUnderstood, Rejected = rejected.Reason };
                break;

            case CommandsApplied:
                entry = entry with { Outcome = PhraseOutcome.Applied };
                break;

            case ApplyFailed failed:
                entry = entry with { Outcome = Map(failed.Outcome) };
                break;

            case PipelineFailed { Stage: PipelineStage.Cue } failed:
                // The phrase itself went through; only the cue did not sound.
                entry = entry with { FailedStage = failed.Stage, Error = failed.Error };
                break;

            case PipelineFailed failed:
                entry = entry with { Outcome = PhraseOutcome.Failed, FailedStage = failed.Stage, Error = failed.Error };
                break;

            case PttUnavailable unavailable:
                entry = entry with { Outcome = PhraseOutcome.Unavailable, Unavailable = unavailable.Reason, Error = unavailable.Detail, Finished = true };
                Count(entry);
                break;

            case ResultSignaled signaled:
                entry = entry with { Response = signaled.ResponseText, Short = signaled.ShortText, Finished = true };
                Count(entry);
                break;

            default:
                return false;
        }

        _entries[index] = entry;
        return true;
    }

    /// <summary>Counts a finished phrase once; a phrase finishes with one signal or one refusal.</summary>
    private void Count(PhraseEntry entry)
    {
        _total++;
        if (entry.Outcome == PhraseOutcome.Applied)
        {
            _applied++;
        }
    }

    private static PhraseOutcome Map(ExecutionOutcome outcome) => outcome switch
    {
        ExecutionOutcome.Applied => PhraseOutcome.Applied,
        ExecutionOutcome.PartiallyApplied => PhraseOutcome.PartiallyApplied,
        ExecutionOutcome.NotConnected => PhraseOutcome.NotConnected,
        ExecutionOutcome.NotInCar => PhraseOutcome.NotInCar,
        ExecutionOutcome.CompoundUnavailable => PhraseOutcome.CompoundUnavailable,
        _ => PhraseOutcome.NotApplied,
    };
}
