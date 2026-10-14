namespace PitVoice.Core.Pit;

/// <summary>What the log gets when a phrase was not or only partially applied.</summary>
/// <param name="Phrase">The phrase as it was recognized or typed.</param>
/// <param name="Outcome"><see cref="ExecutionOutcome.NotApplied"/> or <see cref="ExecutionOutcome.PartiallyApplied"/>.</param>
/// <param name="Sent">Commands sent to the game.</param>
/// <param name="Expected">Expected states of the steps that were not confirmed.</param>
/// <param name="Actual">Last black box state seen, <c>null</c> when the connection was lost.</param>
/// <param name="NothingConfirmed">True when not a single step was confirmed (a hint that commands do not arrive).</param>
public sealed record ApplyFailureReport(
    string Phrase,
    ExecutionOutcome Outcome,
    IReadOnlyList<SentCommand> Sent,
    IReadOnlyList<string> Expected,
    PitServiceState? Actual,
    bool NothingConfirmed)
{
    public override string ToString() =>
        $"pit commands {(Outcome == ExecutionOutcome.PartiallyApplied ? "partially applied" : "not applied")}: " +
        $"phrase \"{Phrase}\"; sent [{string.Join(", ", Sent)}]; expected [{string.Join("; ", Expected)}]; " +
        $"actual [{Actual?.ToString() ?? "no data (connection lost)"}]";
}

/// <summary>
/// Log sink of the execution layer. Core has no logging dependency; the host adapts this to its logger.
/// </summary>
public interface IExecutionLog
{
    void ApplyFailed(ApplyFailureReport report);

    void Warning(string message);
}

/// <summary>Log that drops everything.</summary>
public sealed class NullExecutionLog : IExecutionLog
{
    public static NullExecutionLog Instance { get; } = new();

    public void ApplyFailed(ApplyFailureReport report)
    {
    }

    public void Warning(string message)
    {
    }
}
