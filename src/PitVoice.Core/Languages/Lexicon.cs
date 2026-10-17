using System.Collections.Frozen;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Languages;

/// <summary>Groups of the pack lexicon (TZ §6, design D1); each group belongs to one kind of pit command.</summary>
public enum LexiconGroup
{
    Fuel,
    Tires,
    Windshield,
    Repair,
    Clear,
}

/// <summary>Roles of words inside a group.</summary>
public enum WordRole
{
    /// <summary>A verb: "залей", "меняй", "change".</summary>
    Action,

    /// <summary>What the command is about: "топливо", "шины", "windshield".</summary>
    Object,

    /// <summary>A verb that only makes sense with a negation: "не трогай".</summary>
    Keep,

    /// <summary>"быстрый": with a repair object it asks for a fast repair.</summary>
    Fast,

    /// <summary>A tire target: a wheel, a side, an axle or all tires (only in <see cref="LexiconGroup.Tires"/>).</summary>
    Target,
}

/// <summary>Tire targets of the lexicon: every wheel, side, axle and all four.</summary>
public enum TireTarget
{
    All,
    Left,
    Right,
    Front,
    Rear,
    LF,
    RF,
    LR,
    RR,
}

/// <summary>A class of words: group, role and, for tire targets, the target.</summary>
public readonly record struct WordClass(LexiconGroup Group, WordRole Role, TireTarget? Target = null) : IComparable<WordClass>
{
    /// <summary>Name as written in the pack: <c>fuel.action</c>, <c>tires.targets.lf</c>.</summary>
    public string Name => Target is { } target
        ? $"{LexiconSchema.Name(Group)}.targets.{LexiconSchema.Name(target)}"
        : $"{LexiconSchema.Name(Group)}.{LexiconSchema.Name(Role)}";

    public int CompareTo(WordClass other)
    {
        var byGroup = Group.CompareTo(other.Group);
        if (byGroup != 0)
        {
            return byGroup;
        }

        var byRole = Role.CompareTo(other.Role);
        return byRole != 0 ? byRole : Nullable.Compare(Target, other.Target);
    }

    public override string ToString() => Name;
}

/// <summary>Names of groups, roles and targets as they are written in YAML, and which roles each group has.</summary>
public static class LexiconSchema
{
    public const string TargetsKey = "targets";

    private static readonly FrozenDictionary<string, LexiconGroup> Groups = new Dictionary<string, LexiconGroup>(StringComparer.Ordinal)
    {
        ["fuel"] = LexiconGroup.Fuel,
        ["tires"] = LexiconGroup.Tires,
        ["windshield"] = LexiconGroup.Windshield,
        ["repair"] = LexiconGroup.Repair,
        ["clear"] = LexiconGroup.Clear,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, WordRole> Roles = new Dictionary<string, WordRole>(StringComparer.Ordinal)
    {
        ["action"] = WordRole.Action,
        ["object"] = WordRole.Object,
        ["keep"] = WordRole.Keep,
        ["fast"] = WordRole.Fast,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, TireTarget> Targets = new Dictionary<string, TireTarget>(StringComparer.Ordinal)
    {
        ["all"] = TireTarget.All,
        ["left"] = TireTarget.Left,
        ["right"] = TireTarget.Right,
        ["front"] = TireTarget.Front,
        ["rear"] = TireTarget.Rear,
        ["lf"] = TireTarget.LF,
        ["rf"] = TireTarget.RF,
        ["lr"] = TireTarget.LR,
        ["rr"] = TireTarget.RR,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<LexiconGroup, WordRole[]> GroupRoles = new Dictionary<LexiconGroup, WordRole[]>
    {
        [LexiconGroup.Fuel] = [WordRole.Action, WordRole.Object],
        [LexiconGroup.Tires] = [WordRole.Action, WordRole.Object, WordRole.Keep, WordRole.Target],
        [LexiconGroup.Windshield] = [WordRole.Action, WordRole.Object, WordRole.Keep],
        [LexiconGroup.Repair] = [WordRole.Action, WordRole.Object, WordRole.Fast],
        [LexiconGroup.Clear] = [WordRole.Action, WordRole.Object],
    }.ToFrozenDictionary();

    /// <summary>Group names in format order.</summary>
    public static IReadOnlyList<string> GroupNames { get; } = ["fuel", "tires", "windshield", "repair", "clear"];

    public static IReadOnlyList<string> TargetNames { get; } = ["all", "left", "right", "front", "rear", "lf", "rf", "lr", "rr"];

    public static bool TryGetGroup(string name, out LexiconGroup group) => Groups.TryGetValue(name, out group);

    public static bool TryGetTarget(string name, out TireTarget target) => Targets.TryGetValue(name, out target);

    /// <summary>The role named <paramref name="name"/> if <paramref name="group"/> has it; <c>targets</c> is not a role name.</summary>
    public static bool TryGetRole(LexiconGroup group, string name, out WordRole role) =>
        Roles.TryGetValue(name, out role) && HasRole(group, role);

    public static bool HasRole(LexiconGroup group, WordRole role) => GroupRoles[group].Contains(role);

    /// <summary>Role names of a group as written in YAML, for error messages.</summary>
    public static IReadOnlyList<string> RoleNames(LexiconGroup group) =>
        GroupRoles[group].Select(r => r == WordRole.Target ? TargetsKey : Name(r)).ToArray();

    public static string Name(LexiconGroup group) => Groups.First(p => p.Value == group).Key;

    public static string Name(WordRole role) => role == WordRole.Target ? TargetsKey : Roles.First(p => p.Value == role).Key;

    public static string Name(TireTarget target) => Targets.First(p => p.Value == target).Key;

    /// <summary>Every class of the format, in format order.</summary>
    public static IReadOnlyList<WordClass> AllClasses { get; } =
        Enum.GetValues<LexiconGroup>()
            .SelectMany(g => GroupRoles[g].SelectMany(r => r == WordRole.Target
                ? Enum.GetValues<TireTarget>().Select(t => new WordClass(g, r, t))
                : [new WordClass(g, r)]))
            .ToArray();
}

/// <summary>One entry of a word class: a word or a fixed sequence of words.</summary>
/// <param name="Class">Class of the entry.</param>
/// <param name="Text">Normalized words joined by a space, e.g. <c>переднее левое</c>.</param>
/// <param name="Keys">
/// What the entry matches in a normalized phrase, one key per token (<see cref="Lexicon.KeyOf"/>): "все четыре" is
/// <c>все</c>, <c>#4</c>.
/// </param>
/// <param name="IsUserAddition">True for a word from the user's additions file (FR-LANG-5).</param>
public sealed record LexiconEntry(WordClass Class, string Text, IReadOnlyList<string> Keys, bool IsUserAddition = false)
{
    /// <summary>Number of words, e.g. 2 for "переднее левое".</summary>
    public int WordCount => Text.Split(' ').Length;

    /// <summary>Words of the entry, as the recognizer must hear them.</summary>
    public IReadOnlyList<string> Words => Text.Split(' ');

    public override string ToString() => $"{Class}: {Text}";
}

/// <summary>
/// The word classes of a pack (TZ §6, design D1). Inside a class the words keep the order of the file, built-in before
/// own, so the reference starts with the words the pack author put first ("залей", not "добавь"); the file fixes the
/// order, so it is deterministic.
/// </summary>
public sealed class Lexicon
{
    private readonly FrozenDictionary<WordClass, LexiconEntry[]> _byClass;

    public Lexicon(IEnumerable<LexiconEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries
            .OrderBy(e => e.Class)
            .ThenBy(e => e.IsUserAddition)
            .ToArray();
        _byClass = Entries.GroupBy(e => e.Class).ToFrozenDictionary(g => g.Key, g => g.ToArray());
        MaxKeys = Entries.Count == 0 ? 0 : Entries.Max(e => e.Keys.Count);
    }

    public static Lexicon Empty { get; } = new([]);

    /// <summary>All entries ordered by class, then built-in before own, then as in the file.</summary>
    public IReadOnlyList<LexiconEntry> Entries { get; }

    /// <summary>Length of the longest entry in tokens.</summary>
    public int MaxKeys { get; }

    public IReadOnlyList<LexiconEntry> Of(WordClass wordClass) => _byClass.TryGetValue(wordClass, out var entries) ? entries : [];

    public bool Has(WordClass wordClass) => _byClass.ContainsKey(wordClass);

    /// <summary>True when the class has an entry of two or more words ("tear off", "переднее левое").</summary>
    public bool HasMultiWord(WordClass wordClass) => Of(wordClass).Any(e => e.WordCount >= 2);

    public bool Contains(WordClass wordClass, string text) => Of(wordClass).Any(e => e.Text == text);

    /// <summary>A copy with <paramref name="extra"/> entries added.</summary>
    public Lexicon With(IEnumerable<LexiconEntry> extra) => new(Entries.Concat(extra));

    /// <summary>The key a token is matched by: the word, <c>#N</c> for a number, <c>&lt;unit&gt;</c> for a unit.</summary>
    public static string KeyOf(Token token) => token switch
    {
        NumberToken number => $"#{number.Value}",
        UnitToken unit => $"<{unit.Unit}>",
        _ => token.Text,
    };
}
