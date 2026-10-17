using PitVoice.Core.Intents;
using PitVoice.Core.Languages;

namespace PitVoice.Core.Parsing;

/// <summary>A command a part of a phrase gives before the default unit and confidence are applied.</summary>
internal readonly record struct RuleCommand(IntentId Intent, int? Amount, string? Unit);

/// <summary>Words of one part of a phrase sorted by role; every word class comes from one group.</summary>
internal sealed class PartRoles
{
    public required LexiconGroup Group { get; init; }

    public int Actions { get; init; }

    /// <summary>True when an action entry has two or more words ("tear off").</summary>
    public bool MultiWordAction { get; init; }

    public int Objects { get; init; }

    public int Keeps { get; init; }

    public int Fasts { get; init; }

    /// <summary>Tire targets in spoken order with the number of words of each entry.</summary>
    public IReadOnlyList<(TireTarget Target, int Words)> Targets { get; init; } = [];

    public int Negations { get; init; }

    public IReadOnlyList<int> Amounts { get; init; } = [];

    public IReadOnlyList<string> Units { get; init; } = [];
}

/// <summary>
/// The rules that turn word classes into intents (design D1). They are the same for every language: a pack only lists the
/// words of each class. Every rule needs at least two words, so one word never changes the pit stop.
/// </summary>
public static class WordRules
{
    private static readonly Dictionary<TireTarget, (IntentId Add, IntentId Remove)> TireIntents = new()
    {
        [TireTarget.All] = (IntentCatalog.TiresAll, IntentCatalog.TiresNone),
        [TireTarget.Left] = (IntentCatalog.TiresLeft, IntentCatalog.TiresNoneLeft),
        [TireTarget.Right] = (IntentCatalog.TiresRight, IntentCatalog.TiresNoneRight),
        [TireTarget.Front] = (IntentCatalog.TiresFront, IntentCatalog.TiresNoneFront),
        [TireTarget.Rear] = (IntentCatalog.TiresRear, IntentCatalog.TiresNoneRear),
        [TireTarget.LF] = (IntentCatalog.TiresLF, IntentCatalog.TiresNoneLF),
        [TireTarget.RF] = (IntentCatalog.TiresRF, IntentCatalog.TiresNoneRF),
        [TireTarget.LR] = (IntentCatalog.TiresLR, IntentCatalog.TiresNoneLR),
        [TireTarget.RR] = (IntentCatalog.TiresRR, IntentCatalog.TiresNoneRR),
    };

    /// <summary>The intent that adds (or removes) the tires of <paramref name="target"/>.</summary>
    public static IntentId TireIntent(TireTarget target, bool remove) =>
        remove ? TireIntents[target].Remove : TireIntents[target].Add;

    /// <summary>
    /// Commands of a part, or null when no rule fits. Tire targets give one command each, in spoken order.
    /// </summary>
    internal static IReadOnlyList<RuleCommand>? Apply(PartRoles roles)
    {
        var negated = roles.Negations > 0;
        if (roles.Group != LexiconGroup.Fuel && (roles.Amounts.Count > 0 || roles.Units.Count > 0))
        {
            return null;
        }

        switch (roles.Group)
        {
            case LexiconGroup.Fuel:
                if (roles.Actions + roles.Objects == 0 || roles.Units.Count > 1)
                {
                    return null;
                }

                if (negated)
                {
                    return roles.Amounts.Count == 0 && roles.Units.Count == 0 ? [new(IntentCatalog.FuelNone, null, null)] : null;
                }

                // Two separate numbers are a fraction ("тридцать точка два") or a mistake: never guess.
                return roles.Amounts.Count == 1
                    ? [new(IntentCatalog.FuelAdd, roles.Amounts[0], roles.Units.Count == 1 ? roles.Units[0] : null)]
                    : null;

            case LexiconGroup.Tires:
                if (negated)
                {
                    if (roles.Targets.Count > 0)
                    {
                        return roles.Targets.Select(t => new RuleCommand(TireIntent(t.Target, remove: true), null, null)).ToArray();
                    }

                    return roles.Objects > 0 ? [new(IntentCatalog.TiresNone, null, null)] : null;
                }

                if (roles.Keeps > 0)
                {
                    return null;
                }

                if (roles.Targets.Count > 0)
                {
                    var named = roles.Actions > 0 || roles.Objects > 0 || roles.Targets is [{ Words: >= 2 }];
                    return named
                        ? roles.Targets.Select(t => new RuleCommand(TireIntent(t.Target, remove: false), null, null)).ToArray()
                        : null;
                }

                return roles.Actions > 0 && roles.Objects > 0 ? [new(IntentCatalog.TiresAll, null, null)] : null;

            case LexiconGroup.Windshield:
                if (negated)
                {
                    return roles.Actions + roles.Keeps + roles.Objects > 0 ? [new(IntentCatalog.WindshieldNone, null, null)] : null;
                }

                return roles.Keeps == 0 && roles.Actions > 0 && (roles.Objects > 0 || roles.MultiWordAction)
                    ? [new(IntentCatalog.WindshieldTearoff, null, null)]
                    : null;

            case LexiconGroup.Repair:
                if (negated)
                {
                    return roles.Actions + roles.Objects > 0 ? [new(IntentCatalog.RepairNone, null, null)] : null;
                }

                return roles.Objects > 0 && roles.Actions + roles.Fasts > 0 ? [new(IntentCatalog.RepairFast, null, null)] : null;

            case LexiconGroup.Clear:
                return !negated && roles.Actions > 0 && roles.Objects > 0 ? [new(IntentCatalog.PitClear, null, null)] : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// True when the lexicon has the classes the rule of <paramref name="intent"/> needs (spec: Pack completeness).
    /// </summary>
    internal static bool IsAvailable(IntentId intent, Lexicon lexicon, bool hasNegations)
    {
        bool Has(LexiconGroup group, WordRole role) => lexicon.Has(new WordClass(group, role));

        var definition = IntentCatalog.Get(intent);
        if (definition.IsNegation && !hasNegations)
        {
            return false;
        }

        switch (definition.ExclusionGroup)
        {
            case IntentCatalog.FuelGroup:
                return Has(LexiconGroup.Fuel, WordRole.Action) || Has(LexiconGroup.Fuel, WordRole.Object);

            case IntentCatalog.TiresGroup:
                var target = TireIntents.Single(p => p.Value.Add == intent || p.Value.Remove == intent).Key;
                var targetClass = new WordClass(LexiconGroup.Tires, WordRole.Target, target);
                var action = Has(LexiconGroup.Tires, WordRole.Action);
                var obj = Has(LexiconGroup.Tires, WordRole.Object);
                if (definition.IsNegation)
                {
                    return lexicon.Has(targetClass) || (target == TireTarget.All && obj);
                }

                return (lexicon.Has(targetClass) && (action || obj || lexicon.HasMultiWord(targetClass)))
                    || (target == TireTarget.All && action && obj);

            case IntentCatalog.WindshieldGroup:
                var wsAction = Has(LexiconGroup.Windshield, WordRole.Action);
                return definition.IsNegation
                    ? wsAction || Has(LexiconGroup.Windshield, WordRole.Keep) || Has(LexiconGroup.Windshield, WordRole.Object)
                    : wsAction && (Has(LexiconGroup.Windshield, WordRole.Object)
                        || lexicon.HasMultiWord(new WordClass(LexiconGroup.Windshield, WordRole.Action)));

            case IntentCatalog.RepairGroup:
                var repairObject = Has(LexiconGroup.Repair, WordRole.Object);
                return definition.IsNegation
                    ? Has(LexiconGroup.Repair, WordRole.Action) || repairObject
                    : repairObject && (Has(LexiconGroup.Repair, WordRole.Action) || Has(LexiconGroup.Repair, WordRole.Fast));

            default:
                return intent == IntentCatalog.PitClear
                    && Has(LexiconGroup.Clear, WordRole.Action) && Has(LexiconGroup.Clear, WordRole.Object);
        }
    }
}
