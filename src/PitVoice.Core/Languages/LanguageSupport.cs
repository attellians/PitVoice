using System.Collections.Frozen;
using PitVoice.Core.Numbers;
using PitVoice.Core.Plural;

namespace PitVoice.Core.Languages;

/// <summary>
/// The only language-specific code: a number parser and plural rules per language code. Adding a language means
/// adding a YAML pack plus, if needed, an entry here.
/// </summary>
public static class LanguageSupport
{
    private static readonly FrozenDictionary<string, INumberParser> NumberParsers =
        new Dictionary<string, INumberParser>(StringComparer.Ordinal)
        {
            ["ru"] = RuNumberParser.Instance,
            ["en"] = EnNumberParser.Instance,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, IPluralRules> PluralRulesById =
        new Dictionary<string, IPluralRules>(StringComparer.Ordinal)
        {
            ["ru"] = RuPluralRules.Instance,
            ["en"] = EnPluralRules.Instance,
        }.ToFrozenDictionary();

    public static IReadOnlyCollection<string> LanguagesWithNumbers => NumberParsers.Keys;

    public static IReadOnlyCollection<string> PluralRuleSets => PluralRulesById.Keys;

    public static bool TryGetNumberParser(string language, out INumberParser parser) =>
        NumberParsers.TryGetValue(language, out parser!);

    public static bool TryGetPluralRules(string id, out IPluralRules rules) =>
        PluralRulesById.TryGetValue(id, out rules!);
}
