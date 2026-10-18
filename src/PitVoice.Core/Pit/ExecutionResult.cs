using PitVoice.Core.Parsing;

namespace PitVoice.Core.Pit;

/// <summary>How the execution of one phrase ended. Exactly one per phrase.</summary>
public enum ExecutionOutcome
{
    /// <summary>Every command was confirmed by telemetry.</summary>
    Applied,

    /// <summary>Some commands were confirmed, some were not.</summary>
    PartiallyApplied,

    /// <summary>Nothing was confirmed in time, or the connection was lost while waiting.</summary>
    NotApplied,

    /// <summary>The game is not running; nothing was sent.</summary>
    NotConnected,

    /// <summary>The game runs but the driver is not in the car; nothing was sent.</summary>
    NotInCar,

    /// <summary>
    /// The phrase asks for a tire compound the car does not have (or several dry ones); nothing of the phrase was sent
    /// (design D7). <see cref="ExecutionResult.CompoundProblem"/> says why.
    /// </summary>
    CompoundUnavailable,
}

/// <summary>Result of executing an accepted phrase. Expected race situations are values here, not exceptions.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Sent">Commands sent to the game, in order.</param>
/// <param name="Unconfirmed">Parsed commands whose effect the telemetry did not show.</param>
public sealed record ExecutionResult(
    ExecutionOutcome Outcome,
    IReadOnlyList<SentCommand> Sent,
    IReadOnlyList<ParsedCommand> Unconfirmed)
{
    /// <summary>Why the compound is not available, for <see cref="ExecutionOutcome.CompoundUnavailable"/>.</summary>
    public CompoundProblem? CompoundProblem { get; init; }

    public static ExecutionResult Of(ExecutionOutcome outcome) => new(outcome, [], []);

    public override string ToString() =>
        $"{Outcome}; sent: [{string.Join(", ", Sent)}]; unconfirmed: [{string.Join(", ", Unconfirmed.Select(c => c.Intent))}]";
}
