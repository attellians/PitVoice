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

    /// <summary>
    /// How long the expected state must hold to count as confirmed. In iRacing a fast repair request without fast repairs
    /// left shows the flag for one or two telemetry frames (~30 ms) before the game drops it.
    /// </summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(100);

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
        var before = _telemetry.Current;
        switch (before.State)
        {
            case ConnectionState.NotRunning:
                return ExecutionResult.Of(ExecutionOutcome.NotConnected);
            case ConnectionState.Connected:
                return ExecutionResult.Of(ExecutionOutcome.NotInCar);
        }

        var plan = PitPlanner.Plan(commands, before.Pit ?? PitServiceState.Empty, before.FuelCapacity);
        var sent = new List<SentCommand>();
        foreach (var command in plan.Commands)
        {
            _sink.Send(command.Command, command.Parameter);
            sent.Add(command);
        }

        // A step is confirmed only while its state has held for SettleTime: the game may show a flag for a frame or
        // two and then drop it (fast repair with none left), which must not count as applied.
        var holdingSince = new TimeSpan?[plan.Steps.Count];
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
            var elapsed = _time.GetElapsedTime(started);
            for (var i = 0; i < confirmed.Length; i++)
            {
                if (plan.Steps[i].IsConfirmed(actual))
                {
                    holdingSince[i] ??= elapsed;
                }
                else
                {
                    holdingSince[i] = null;
                }

                confirmed[i] = holdingSince[i] is { } since && elapsed - since >= SettleTime;
            }

            if (confirmed.All(c => c) || elapsed >= ConfirmationTimeout)
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
