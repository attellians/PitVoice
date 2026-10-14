using System.Globalization;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Pit;

/// <summary>
/// Commands to send for one part of a phrase and the telemetry state that confirms them.
/// </summary>
/// <param name="Sources">Parsed commands this step carries out (all tire commands of a phrase share one step).</param>
/// <param name="Commands">SDK commands, in sending order.</param>
/// <param name="Expected">Human-readable expected state, for logs.</param>
/// <param name="IsConfirmed">True when a telemetry state shows the step applied.</param>
public sealed record PlannedStep(
    IReadOnlyList<ParsedCommand> Sources,
    IReadOnlyList<SentCommand> Commands,
    string Expected,
    Func<PitServiceState, bool> IsConfirmed);

/// <summary>What to send for a phrase and how to confirm it.</summary>
/// <param name="Steps">Steps in the order the phrase named them.</param>
/// <param name="Skipped">Commands that send nothing (<c>status.fuel</c>, <c>assistant.cancel</c>).</param>
public sealed record PitPlan(IReadOnlyList<PlannedStep> Steps, IReadOnlyList<ParsedCommand> Skipped)
{
    public IEnumerable<SentCommand> Commands => Steps.SelectMany(s => s.Commands);
}

/// <summary>
/// Turns parsed commands into SDK pit commands with expected states (design decisions 4, 6, 7). Pure: the current
/// black box state comes from the caller.
/// </summary>
public static class PitPlanner
{
    public const double LitersPerGallon = 3.78541;

    /// <summary>Tolerance for comparing the requested fuel with the telemetry value (a float in liters).</summary>
    public const double FuelTolerance = 0.5;

    public static PitPlan Plan(IReadOnlyList<ParsedCommand> commands, PitServiceState current)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(current);

        var steps = new List<PlannedStep>();
        var skipped = new List<ParsedCommand>();
        var tires = commands.Where(c => IntentCatalog.Get(c.Intent).ExclusionGroup == IntentCatalog.TiresGroup).ToList();
        var tiresPlanned = false;

        foreach (var command in commands)
        {
            var definition = IntentCatalog.Get(command.Intent);
            if (!definition.ChangesPitSetup)
            {
                skipped.Add(command);
                continue;
            }

            if (definition.ExclusionGroup == IntentCatalog.TiresGroup)
            {
                if (!tiresPlanned)
                {
                    steps.Add(PlanTires(tires, current));
                    tiresPlanned = true;
                }

                continue;
            }

            steps.Add(PlanOther(command));
        }

        return new PitPlan(steps, skipped);
    }

    /// <summary>Liters to send for a <c>fuel.add</c> amount: gallons are converted and rounded to whole liters.</summary>
    public static int ToLiters(int amount, string? unit) =>
        unit == FuelUnits.Gallons
            ? (int)Math.Round(amount * LitersPerGallon, MidpointRounding.AwayFromZero)
            : amount;

    private static PlannedStep PlanTires(IReadOnlyList<ParsedCommand> tires, PitServiceState current)
    {
        var adds = Wheels.None;
        var removes = Wheels.None;
        foreach (var command in tires)
        {
            var definition = IntentCatalog.Get(command.Intent);
            adds |= definition.AddsWheels;
            removes |= definition.RemovesWheels;
        }

        if (removes == Wheels.None)
        {
            // LF/RF/LR/RR select a wheel without touching the others; 0 keeps the current pressure.
            var sent = adds.Split().Select(w => new SentCommand(w.ToPitCommand(), 0)).ToList();
            return new PlannedStep(tires, sent, $"wheels include {adds.Describe()}", s => (s.Wheels & adds) == adds);
        }

        var target = (current.Wheels | adds) & ~removes;
        var commands = new List<SentCommand> { new(PitCommand.ClearTires, 0) };
        commands.AddRange(target.Split().Select(w => new SentCommand(w.ToPitCommand(), 0)));
        return new PlannedStep(tires, commands, $"wheels are {target.Describe()}", s => s.Wheels == target);
    }

    private static PlannedStep PlanOther(ParsedCommand command)
    {
        IReadOnlyList<ParsedCommand> sources = [command];
        var intent = command.Intent;

        if (intent == IntentCatalog.FuelAdd)
        {
            var liters = ToLiters(command.Amount ?? 0, command.Unit);
            if (liters == 0)
            {
                // Fuel with parameter 0 means "keep the current amount" in the SDK, so zero liters clears refueling.
                return Clear(sources, PitCommand.ClearFuel, PitServiceFlags.FuelFill, "fuel off");
            }

            return new PlannedStep(
                sources,
                [new SentCommand(PitCommand.Fuel, liters)],
                string.Create(CultureInfo.InvariantCulture, $"fuel on, 0 < amount <= {liters} l"),
                s => s.FuelFill && s.FuelToAdd > 0 && s.FuelToAdd <= liters + FuelTolerance);
        }

        if (intent == IntentCatalog.FuelNone)
        {
            return Clear(sources, PitCommand.ClearFuel, PitServiceFlags.FuelFill, "fuel off");
        }

        if (intent == IntentCatalog.WindshieldTearoff)
        {
            return Set(sources, PitCommand.WS, PitServiceFlags.WindshieldTearoff, "tearoff on");
        }

        if (intent == IntentCatalog.WindshieldNone)
        {
            return Clear(sources, PitCommand.ClearWS, PitServiceFlags.WindshieldTearoff, "tearoff off");
        }

        if (intent == IntentCatalog.RepairFast)
        {
            return Set(sources, PitCommand.FR, PitServiceFlags.FastRepair, "fast repair on");
        }

        if (intent == IntentCatalog.RepairNone)
        {
            return Clear(sources, PitCommand.ClearFR, PitServiceFlags.FastRepair, "fast repair off");
        }

        if (intent == IntentCatalog.PitClear)
        {
            return new PlannedStep(sources, [new SentCommand(PitCommand.Clear, 0)], "nothing selected", s => s.NothingSelected);
        }

        throw new InvalidOperationException($"No pit command for intent '{intent}'.");
    }

    private static PlannedStep Set(IReadOnlyList<ParsedCommand> sources, PitCommand command, PitServiceFlags flag, string expected) =>
        new(sources, [new SentCommand(command, 0)], expected, s => s.Flags.HasFlag(flag));

    private static PlannedStep Clear(IReadOnlyList<ParsedCommand> sources, PitCommand command, PitServiceFlags flag, string expected) =>
        new(sources, [new SentCommand(command, 0)], expected, s => !s.Flags.HasFlag(flag));
}
