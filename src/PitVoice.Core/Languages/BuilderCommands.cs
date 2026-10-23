using PitVoice.Core.Intents;

namespace PitVoice.Core.Languages;

/// <summary>Command of a row of the "Own phrases" page.</summary>
public enum BuilderCommand
{
    Fuel,
    Tires,
    Repair,
    Windshield,
    Clear,
}

/// <summary>What the phrase of a row is.</summary>
public enum BuilderKind
{
    /// <summary>A word of <c>fuel.action</c>: it goes with every amount and unit of the built-in pack.</summary>
    Verb,

    /// <summary>A ready phrase of two or more words that gives one command.</summary>
    Phrase,
}

/// <summary>One choice of the page: a command and its variant.</summary>
/// <param name="Command">Command of the row.</param>
/// <param name="Variant">Variant key, e.g. <c>tires.wet</c>; null for a command without variants.</param>
/// <param name="Kind">Verb or ready phrase.</param>
/// <param name="Intent">The intent the row gives; <c>fuel.add</c> for the verb.</param>
public sealed record BuilderOption(BuilderCommand Command, string? Variant, BuilderKind Kind, IntentId Intent);

/// <summary>
/// The commands of the "Own phrases" page (design D2), in page order. The loader accepts only these intents for ready
/// phrases, and the page builds its lists from them.
/// </summary>
public static class BuilderCommands
{
    /// <summary>The fuel verb: a word of <c>fuel.action</c>.</summary>
    public static BuilderOption FuelVerb { get; } = new(BuilderCommand.Fuel, "fuel.verb", BuilderKind.Verb, IntentCatalog.FuelAdd);

    public static IReadOnlyList<BuilderOption> All { get; } =
    [
        FuelVerb,
        Phrase(BuilderCommand.Fuel, IntentCatalog.FuelNone),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresAll),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresFront),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresRear),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresLeft),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresRight),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresLF),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresRF),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresLR),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresRR),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresDry),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresWet),
        Phrase(BuilderCommand.Tires, IntentCatalog.TiresNone),
        new(BuilderCommand.Repair, null, BuilderKind.Phrase, IntentCatalog.RepairFast),
        new(BuilderCommand.Windshield, null, BuilderKind.Phrase, IntentCatalog.WindshieldTearoff),
        new(BuilderCommand.Clear, null, BuilderKind.Phrase, IntentCatalog.PitClear),
    ];

    /// <summary>Commands in page order.</summary>
    public static IReadOnlyList<BuilderCommand> Commands { get; } = All.Select(o => o.Command).Distinct().ToArray();

    /// <summary>Choices of <paramref name="command"/> in page order; one choice with a null variant for a command without variants.</summary>
    public static IReadOnlyList<BuilderOption> Of(BuilderCommand command) => All.Where(o => o.Command == command).ToArray();

    /// <summary>The ready phrase choice that gives <paramref name="intent"/>, or null when the page has none.</summary>
    public static BuilderOption? PhraseOf(IntentId intent) => All.FirstOrDefault(o => o.Kind == BuilderKind.Phrase && o.Intent == intent);

    private static BuilderOption Phrase(BuilderCommand command, IntentId intent) => new(command, intent.Value, BuilderKind.Phrase, intent);
}
