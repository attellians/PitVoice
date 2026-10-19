using PitVoice.App.Controls;
using PitVoice.App.Localization;
using PitVoice.Host;
using Wpf.Ui.Controls;

namespace PitVoice.App.ViewModels;

internal sealed record WordLine(string Text, bool IsOwn);

internal sealed record ClassEntry(string Name, IReadOnlyList<WordLine> Words);

internal sealed record CommandEntry(string Name, IReadOnlyList<string> Examples)
{
    public string ExamplesText => string.Join("   ", Examples.Select(e => $"«{e}»"));
}

/// <summary>A group of the reference as a card (spec: Command reference; design D5).</summary>
/// <param name="Name">"Топливо".</param>
/// <param name="Section">Hue of the icon.</param>
/// <param name="Symbol">Icon of the group.</param>
/// <param name="Description">One sentence about what the group does.</param>
/// <param name="Examples">Up to <see cref="CommandCards.MaxExamples"/> example phrases shown as chips.</param>
/// <param name="Classes">Words of the group by class, in the collapsed "Слова группы" block.</param>
/// <param name="Commands">Every command of the group with its 1–2 examples, in the same block.</param>
/// <param name="ElidedAction">The note about the action said once for two targets, or empty.</param>
internal sealed record GroupEntry(
    string Name,
    Section Section,
    SymbolRegular Symbol,
    string Description,
    IReadOnlyList<string> Examples,
    IReadOnlyList<ClassEntry> Classes,
    IReadOnlyList<CommandEntry> Commands,
    string ElidedAction);

/// <summary>Turns the reference of the host into the cards of the "Commands" page; pure, so the tests need no window.</summary>
internal static class CommandCards
{
    public const int MaxExamples = 6;

    public static IReadOnlyList<GroupEntry> Build(IReadOnlyList<ReferenceGroup> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.Select(Card).ToArray();
    }

    public static GroupEntry Card(ReferenceGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new GroupEntry(
            Texts.Group(group.Group),
            SectionOf(group.Group),
            SymbolOf(group.Group),
            Loc.T($"groupHint.{group.Group}"),
            Examples(group),
            group.Classes.Select(c => new ClassEntry(
                Texts.WordClass(c.Class),
                c.Words.Select(w => new WordLine(w.Text, w.IsUserAddition)).ToArray())).ToArray(),
            group.Commands.Select(c => new CommandEntry(
                Texts.Intent(c.Intent),
                c.Examples.Select(Texts.Pattern).ToArray())).ToArray(),
            group.ElidedAction is { } elided ? Loc.F("ElidedAction", elided) : string.Empty);
    }

    /// <summary>
    /// Up to <see cref="MaxExamples"/> example phrases of the group without repeats, slots in words. The first example of
    /// each command comes first, the commands of <see cref="Preferred"/> ahead of the rest, so the tires card shows all
    /// four, a side, an axle, a wheel, a compound and a removal instead of six ways to say "all tires".
    /// </summary>
    public static IReadOnlyList<string> Examples(ReferenceGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var preferred = Preferred(group.Group);
        var commands = group.Commands
            .OrderBy(c => preferred.IndexOf(c.Intent.Value) is var i and >= 0 ? i : preferred.Count + group.Commands.ToList().IndexOf(c))
            .ToArray();
        var examples = new List<string>();
        foreach (var example in commands.Select(c => c.Examples.FirstOrDefault()).Concat(commands.SelectMany(c => c.Examples.Skip(1))))
        {
            if (example is null)
            {
                continue;
            }

            var text = Texts.Pattern(example);
            if (!examples.Contains(text))
            {
                examples.Add(text);
            }

            if (examples.Count == MaxExamples)
            {
                break;
            }
        }

        return examples;
    }

    /// <summary>Intents whose examples go first on the card, by group.</summary>
    private static List<string> Preferred(CommandGroup group) => group switch
    {
        CommandGroup.Fuel => ["fuel.add", "fuel.none"],
        CommandGroup.Tires => ["tires.all", "tires.left", "tires.front", "tires.lf", "tires.wet", "tires.none"],
        CommandGroup.Windshield => ["windshield.tearoff", "windshield.none"],
        CommandGroup.Repair => ["repair.fast", "repair.none"],
        _ => [],
    };

    /// <summary>Fuel — peach, tires — blue, windshield — teal, repair — muted red, reset — grey (spec: Section icons).</summary>
    public static Section SectionOf(CommandGroup group) => group switch
    {
        CommandGroup.Fuel => Section.Commands,
        CommandGroup.Tires => Section.Home,
        CommandGroup.Windshield => Section.About,
        CommandGroup.Repair => Section.Alert,
        _ => Section.Settings,
    };

    public static SymbolRegular SymbolOf(CommandGroup group) => group switch
    {
        CommandGroup.Fuel => SymbolRegular.GasPump24,
        CommandGroup.Tires => SymbolRegular.Circle24,
        CommandGroup.Windshield => SymbolRegular.Window24,
        CommandGroup.Repair => SymbolRegular.Wrench24,
        _ => SymbolRegular.ArrowReset24,
    };
}
