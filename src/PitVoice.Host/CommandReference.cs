using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;

namespace PitVoice.Host;

/// <summary>Group of the command reference (spec: Command reference).</summary>
public enum CommandGroup
{
    Fuel,
    Tires,
    Windshield,
    Repair,
    Reset,
}

/// <summary>A word of the reference and whether it comes from the user's additions ("своё").</summary>
public sealed record ReferenceWord(string Text, bool IsUserAddition);

/// <summary>The words of one class of a group: actions, objects, a tire target, negations, ...</summary>
/// <param name="Class">The class; null for the negations, which the whole pack shares.</param>
/// <param name="Words">Words in pack order, built-in first.</param>
public sealed record ReferenceClass(WordClass? Class, IReadOnlyList<ReferenceWord> Words);

/// <summary>A command with 1–2 example phrases; <c>{amount}</c> and <c>{unit}</c> stand for the slots.</summary>
public sealed record ReferenceCommand(IntentId Intent, IReadOnlyList<string> Examples);

/// <summary>A group of the reference.</summary>
/// <param name="Group">The group.</param>
/// <param name="Classes">Words of the group by class, then the negations when the group has removal commands.</param>
/// <param name="Commands">Commands of the group available in the pack.</param>
/// <param name="ElidedAction">
/// For tires: a phrase that does not repeat the action after a conjunction ("меняй левые и передние"); otherwise null.
/// </param>
public sealed record ReferenceGroup(
    CommandGroup Group,
    IReadOnlyList<ReferenceClass> Classes,
    IReadOnlyList<ReferenceCommand> Commands,
    string? ElidedAction);

/// <summary>
/// The "Commands" page, built from the loaded pack with the user's additions, so it always matches what is really
/// understood (design D8): words of every class and 1–2 examples per command. Every example is checked by the parser, so
/// a shared word never produces a misleading one. Intents the pack cannot produce are left out.
/// </summary>
public static class CommandReference
{
    private const int ExampleAmount = 20;

    public static IReadOnlyList<ReferenceGroup> Build(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var parser = new CommandParser(pack);
        var groups = new List<ReferenceGroup>();
        foreach (var group in Enum.GetValues<CommandGroup>())
        {
            var commands = pack.Intents
                .Where(id => GroupOf(id) == group)
                .Select(id => new ReferenceCommand(id, Examples(pack, parser, id)))
                .ToArray();
            if (commands.Length == 0)
            {
                continue;
            }

            var lexiconGroup = LexiconGroupOf(group);
            var classes = LexiconSchema.AllClasses
                .Where(c => c.Group == lexiconGroup && pack.Lexicon.Has(c))
                .Select(c => new ReferenceClass(c, pack.Lexicon.Of(c).Select(e => new ReferenceWord(e.Text, e.IsUserAddition)).ToArray()))
                .ToList();
            if (commands.Any(c => IntentCatalog.Get(c.Intent).IsNegation))
            {
                // The set keeps no file order: short words ("не", "no") first, as they are said most.
                classes.Add(new ReferenceClass(null, pack.Negations.OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).Select(n => new ReferenceWord(n, false)).ToArray()));
            }

            var elided = group == CommandGroup.Tires ? ElidedActionExample(pack, parser) : null;
            groups.Add(new ReferenceGroup(group, classes, commands, elided));
        }

        return groups;
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
            _ => throw new ArgumentOutOfRangeException(nameof(intent), "The intent has no command group."),
        };
    }

    private static LexiconGroup LexiconGroupOf(CommandGroup group) => group switch
    {
        CommandGroup.Fuel => LexiconGroup.Fuel,
        CommandGroup.Tires => LexiconGroup.Tires,
        CommandGroup.Windshield => LexiconGroup.Windshield,
        CommandGroup.Repair => LexiconGroup.Repair,
        _ => LexiconGroup.Clear,
    };

    private const int MaxExamples = 2;

    /// <summary>
    /// The first self-test phrases of the pack that give only this command: the pack author wrote them, so they read
    /// naturally ("без топлива", not a phrase glued from the first word of every class). <c>fuel.add</c> starts with
    /// "‹action› ‹number› ‹unit›" built from the lexicon, so the slots are shown. A phrase built from the lexicon is also the
    /// fallback for a pack without such tests. Every example is checked by the parser.
    /// </summary>
    private static IReadOnlyList<string> Examples(LanguagePack pack, CommandParser parser, IntentId intent)
    {
        var examples = new List<string>();
        if (intent == IntentCatalog.FuelAdd && FirstGenerated(pack, parser, intent) is { } withSlots)
        {
            examples.Add(withSlots);
        }

        foreach (var test in pack.Tests)
        {
            if (examples.Count == MaxExamples)
            {
                break;
            }

            if (test.Expect is [var only] && only.Intent == intent && !examples.Contains(test.Say) && Gives(parser, test.Say, intent))
            {
                examples.Add(test.Say);
            }
        }

        if (examples.Count == 0 && FirstGenerated(pack, parser, intent) is { } generated)
        {
            examples.Add(generated);
        }

        return examples;
    }

    private static string? FirstGenerated(LanguagePack pack, CommandParser parser, IntentId intent) =>
        Generated(pack, intent).FirstOrDefault(candidate => Gives(
            parser,
            candidate.Replace("{amount}", ExampleAmount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace(" {unit}", string.Empty, StringComparison.Ordinal),
            intent));

    private static bool Gives(CommandParser parser, string phrase, IntentId intent) =>
        parser.Parse(phrase) is ParseResult.Accepted { Commands: [var only] } && only.Intent == intent;

    private static IEnumerable<string> Generated(LanguagePack pack, IntentId intent)
    {
        string? First(LexiconGroup group, WordRole role, TireTarget? target = null) =>
            pack.Lexicon.Of(new WordClass(group, role, target)).FirstOrDefault(e => !e.IsUserAddition)?.Text;

        var negation = pack.Negations.OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).FirstOrDefault();
        var definition = IntentCatalog.Get(intent);
        var group = definition.ExclusionGroup switch
        {
            IntentCatalog.FuelGroup => LexiconGroup.Fuel,
            IntentCatalog.TiresGroup => LexiconGroup.Tires,
            IntentCatalog.WindshieldGroup => LexiconGroup.Windshield,
            IntentCatalog.RepairGroup => LexiconGroup.Repair,
            _ => LexiconGroup.Clear,
        };
        var action = First(group, WordRole.Action);
        var obj = First(group, WordRole.Object);

        IEnumerable<string?[]> Candidates()
        {
            if (intent == IntentCatalog.FuelAdd)
            {
                yield return [action, "{amount}", "{unit}"];
                yield return ["{amount}", "{unit}", obj];
                yield break;
            }

            if (definition.Compound is { } compound)
            {
                var compoundWord = pack.Lexicon.Of(new WordClass(LexiconGroup.Tires, WordRole.Compound, Compound: compound))
                    .FirstOrDefault(e => !e.IsUserAddition)?.Text;
                yield return [action, First(LexiconGroup.Tires, WordRole.Into), compoundWord];
                yield return [compoundWord, obj];
                yield break;
            }

            if (group == LexiconGroup.Tires)
            {
                var target = Enum.GetValues<TireTarget>().First(t => WordRules.TireIntent(t, definition.IsNegation) == intent);
                var targetWord = First(LexiconGroup.Tires, WordRole.Target, target);
                var prefix = definition.IsNegation ? negation : null;
                yield return [prefix, action, targetWord];
                yield return [prefix, targetWord, obj];
                if (target == TireTarget.All)
                {
                    yield return [prefix, action, obj];
                }

                yield break;
            }

            if (definition.IsNegation)
            {
                yield return [negation, action, obj];
                yield return [negation, First(group, WordRole.Keep), obj];
                yield return [negation, obj];
                yield return [negation, action];
                yield break;
            }

            yield return [action, obj];
            yield return [First(group, WordRole.Fast), obj];
            yield return [action];
        }

        foreach (var words in Candidates())
        {
            if (words.All(w => w is not null))
            {
                yield return string.Join(' ', words);
            }
        }
    }

    /// <summary>"меняй левые и передние": the action is said once for two targets.</summary>
    private static string? ElidedActionExample(LanguagePack pack, CommandParser parser)
    {
        var action = pack.Lexicon.Of(new WordClass(LexiconGroup.Tires, WordRole.Action)).FirstOrDefault(e => !e.IsUserAddition)?.Text;
        var first = pack.Lexicon.Of(new WordClass(LexiconGroup.Tires, WordRole.Target, TireTarget.Left)).FirstOrDefault()?.Text;
        var second = pack.Lexicon.Of(new WordClass(LexiconGroup.Tires, WordRole.Target, TireTarget.Front)).FirstOrDefault()?.Text;
        var conjunction = pack.Conjunctions.Where(c => c.Count == 1).Select(c => c[0]).OrderBy(c => c.Length).ThenBy(c => c, StringComparer.Ordinal).FirstOrDefault();
        if (action is null || first is null || second is null || conjunction is null)
        {
            return null;
        }

        var phrase = $"{action} {first} {conjunction} {second}";
        return parser.Parse(phrase) is ParseResult.Accepted { Commands.Count: 2 } ? phrase : null;
    }
}
