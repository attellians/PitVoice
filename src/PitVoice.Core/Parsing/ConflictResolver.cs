using PitVoice.Core.Intents;
using PitVoice.Core.Pit;

namespace PitVoice.Core.Parsing;

/// <summary>Outcome of conflict resolution.</summary>
/// <param name="Commands">Collapsed command list, or <c>null</c> on conflict.</param>
/// <param name="Conflict">Human-readable description of the conflict, or <c>null</c>.</param>
public sealed record ConflictResolution(IReadOnlyList<ParsedCommand>? Commands, string? Conflict)
{
    public bool HasConflict => Conflict is not null;
}

/// <summary>
/// Applies FR-PARSE-7: mutually exclusive intents reject the phrase (for tires: adding and removing a common wheel),
/// standalone intents cannot be combined,
/// duplicate commands collapse, and for <c>fuel.add</c> the last one wins.
/// </summary>
public static class ConflictResolver
{
    public static ConflictResolution Resolve(IReadOnlyList<ParsedCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count > 1)
        {
            foreach (var command in commands)
            {
                if (IntentCatalog.Get(command.Intent).IsStandalone)
                {
                    return new ConflictResolution(null, $"'{command.Intent}' cannot be combined with other commands");
                }
            }

            var byGroup = commands
                .Select(c => IntentCatalog.Get(c.Intent))
                .Where(d => d.ExclusionGroup is not null)
                .GroupBy(d => d.ExclusionGroup!, StringComparer.Ordinal);

            foreach (var group in byGroup)
            {
                if (group.Key == IntentCatalog.TiresGroup)
                {
                    // Tires conflict only when the same wheel is both added and removed.
                    var adds = group.Where(d => d.AddsWheels != Wheels.None).ToList();
                    var removes = group.Where(d => d.RemovesWheels != Wheels.None).ToList();
                    foreach (var add in adds)
                    {
                        var clash = removes.FirstOrDefault(r => (add.AddsWheels & r.RemovesWheels) != 0);
                        if (clash is not null)
                        {
                            return new ConflictResolution(null, $"'{add.Id}' conflicts with '{clash.Id}'");
                        }
                    }

                    continue;
                }

                var negation = group.FirstOrDefault(d => d.IsNegation);
                var positive = group.FirstOrDefault(d => !d.IsNegation);
                if (negation is not null && positive is not null)
                {
                    return new ConflictResolution(null, $"'{positive.Id}' conflicts with '{negation.Id}'");
                }
            }
        }

        var result = new List<ParsedCommand>(commands.Count);
        var lastFuelAdd = commands.LastOrDefault(c => c.Intent == IntentCatalog.FuelAdd);
        var seen = new HashSet<IntentId>();
        foreach (var command in commands)
        {
            if (command.Intent == IntentCatalog.FuelAdd)
            {
                if (!ReferenceEquals(command, lastFuelAdd))
                {
                    continue;
                }
            }
            else if (!seen.Add(command.Intent))
            {
                continue;
            }

            result.Add(command);
        }

        return new ConflictResolution(result, null);
    }
}
