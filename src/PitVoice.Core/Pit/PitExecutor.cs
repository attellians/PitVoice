using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Pit;

/// <summary>
/// Executes an accepted phrase in the game: connection check, sending, confirmation by target state, one outcome
/// (design decisions 7 and 8). Never retries and never rolls back.
/// </summary>
public sealed class PitExecutor
{
    /// <summary>How long the telemetry may take to show the requested state (FR-IR-5).</summary>
    public static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(1.5);

    /// <summary>Telemetry poll period; the game updates at 60 Hz.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);

    private readonly ITelemetrySource _telemetry;
    private readonly IPitCommandSink _sink;
    private readonly TimeProvider _time;
    private readonly IExecutionLog _log;

    public PitExecutor(ITelemetrySource telemetry, IPitCommandSink sink, TimeProvider? time = null, IExecutionLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(sink);
        _telemetry = telemetry;
        _sink = sink;
        _time = time ?? TimeProvider.System;
        _log = log ?? NullExecutionLog.Instance;
    }

    /// <param name="phrase">Accepted parse result; a rejected phrase never reaches the executor.</param>
    /// <param name="text">The phrase text, for the log.</param>
    /// <param name="cancellationToken">Stops waiting for confirmation.</param>
    public async Task<ExecutionResult> ExecuteAsync(ParseResult.Accepted phrase, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phrase);

        var commands = phrase.Commands;
        if (!commands.Any(c => IntentCatalog.Get(c.Intent).ChangesPitSetup))
        {
            // Cancel and fuel status touch nothing in the game, so the connection does not matter.
            return ExecutionResult.Of(commands.Any(c => c.Intent == IntentCatalog.StatusFuel)
                ? ExecutionOutcome.NotSupported
                : ExecutionOutcome.Applied);
        }

        var before = _telemetry.Current;
        switch (before.State)
        {
            case ConnectionState.NotRunning:
                return ExecutionResult.Of(ExecutionOutcome.NotConnected);
            case ConnectionState.Connected:
                return ExecutionResult.Of(ExecutionOutcome.NotInCar);
        }

        var plan = PitPlanner.Plan(commands, before.Pit ?? PitServiceState.Empty);
        var sent = new List<SentCommand>();
        foreach (var command in plan.Commands)
        {
            _sink.Send(command.Command, command.Parameter);
            sent.Add(command);
        }

        var confirmed = new bool[plan.Steps.Count];
        var started = _time.GetTimestamp();
        PitServiceState? actual = before.Pit;
        var connectionLost = false;
        while (true)
        {
            var snapshot = _telemetry.Current;
            if (snapshot.State != ConnectionState.InCar || snapshot.Pit is null)
            {
                connectionLost = true;
                actual = null;
                break;
            }

            actual = snapshot.Pit;
            for (var i = 0; i < confirmed.Length; i++)
            {
                confirmed[i] |= plan.Steps[i].IsConfirmed(actual);
            }

            if (confirmed.All(c => c) || _time.GetElapsedTime(started) >= ConfirmationTimeout)
            {
                break;
            }

            await Task.Delay(PollInterval, _time, cancellationToken).ConfigureAwait(false);
        }

        var confirmedCount = confirmed.Count(c => c);
        var outcome = connectionLost || confirmedCount == 0
            ? ExecutionOutcome.NotApplied
            : confirmedCount == confirmed.Length ? ExecutionOutcome.Applied : ExecutionOutcome.PartiallyApplied;

        var unconfirmedSteps = plan.Steps.Where((_, i) => outcome == ExecutionOutcome.NotApplied || !confirmed[i]).ToList();
        var result = new ExecutionResult(outcome, sent, unconfirmedSteps.SelectMany(s => s.Sources).ToList());

        if (outcome != ExecutionOutcome.Applied)
        {
            _log.ApplyFailed(new ApplyFailureReport(
                text,
                outcome,
                sent,
                unconfirmedSteps.Select(s => s.Expected).ToList(),
                actual,
                NothingConfirmed: confirmedCount == 0));
        }

        return result;
    }
}
