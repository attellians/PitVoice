using PitVoice.Core.Intents;
using PitVoice.Core.Languages;

namespace PitVoice.Host;

/// <summary>Group of the command reference (spec: Command reference).</summary>
public enum CommandGroup
{
    Fuel,
    Tires,
    Windshield,
    Repair,
    Reset,
    Service,
}

/// <summary>A phrase of the reference: the pattern as written and whether it is the user's own.</summary>
public sealed record ReferencePhrase(string Pattern, bool IsUserAddition);

/// <summary>A command with all its phrases in the current language.</summary>
public sealed record ReferenceCommand(IntentId Intent, IReadOnlyList<ReferencePhrase> Phrases);

public sealed record ReferenceGroup(CommandGroup Group, IReadOnlyList<ReferenceCommand> Commands);

/// <summary>
/// The "Commands" page, built from the loaded pack with the user's additions, so it always matches what is really
/// understood. Intents the pack does not describe are left out. The window turns intents, groups and slots into words of
/// the interface language.
/// </summary>
public static class CommandReference
{
    public static IReadOnlyList<ReferenceGroup> Build(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return IntentCatalog.All
            .Where(d => pack.Intents.ContainsKey(d.Id))
            .Select(d => new ReferenceCommand(
                d.Id,
                pack.Intents[d.Id].Templates.Select(t => new ReferencePhrase(t.Source, t.IsUserAddition)).ToArray()))
            .GroupBy(c => GroupOf(c.Intent))
            .OrderBy(g => g.Key)
            .Select(g => new ReferenceGroup(g.Key, g.ToArray()))
            .ToArray();
    }

    public static CommandGroup GroupOf(IntentId intent)
    {
        var prefix = intent.Value.Split('.')[0];
        return prefix switch
        {
            "fuel" => CommandGroup.Fuel,
            "tires" => CommandGroup.Tires,
            "windshield" => CommandGroup.Windshield,
            "repair" => CommandGroup.Repair,
            "pit" => CommandGroup.Reset,
            _ => CommandGroup.Service,
        };
    }
}
