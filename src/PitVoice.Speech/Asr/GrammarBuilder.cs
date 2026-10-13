using System.Text.Encodings.Web;
using System.Text.Json;
using PitVoice.Core.Asr;
using PitVoice.Core.Languages;
using PitVoice.Core.Numbers;
using PitVoice.Core.Parsing;

namespace PitVoice.Speech.Asr;

/// <summary>Shape of the recognizer grammar (design D4; chosen by the spike, TZ §14).</summary>
public enum GrammarMode
{
    /// <summary>Hypothesis A: every vocabulary word of the pack as its own entry, plus the unknown-word marker.</summary>
    Words,

    /// <summary>Hypothesis B: whole template phrases with numbers 0–999 and unit words expanded.</summary>
    Phrases,
}

/// <summary>How a pack vocabulary fits a model vocabulary.</summary>
/// <param name="Substituted">Pack word → model spelling (only words that differ, e.g. <c>еще</c> → <c>ещё</c>).</param>
/// <param name="Missing">Pack words the model does not know in any spelling; they cannot be recognized.</param>
public sealed record VocabularyReport(IReadOnlyDictionary<string, string> Substituted, IReadOnlyList<string> Missing);

/// <summary>
/// Builds the Vosk grammar from a language pack. The output is sorted and duplicate-free, so one pack always gives the
/// same grammar (FR-ASR-2).
/// </summary>
public static class GrammarBuilder
{
    public const GrammarMode DefaultMode = GrammarMode.Words;

    /// <param name="pack">Language pack.</param>
    /// <param name="mode">Grammar shape.</param>
    /// <param name="isKnownWord">
    /// Model vocabulary lookup. When given, every word is written the way the model spells it (<c>еще</c> → <c>ещё</c>)
    /// and words the model does not know are left out; phrases containing them are dropped.
    /// </param>
    public static IReadOnlyList<string> Build(LanguagePack pack, GrammarMode mode = DefaultMode, Func<string, bool>? isKnownWord = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var entries = mode switch
        {
            GrammarMode.Words => new SortedSet<string>(VocabularyBuilder.Build(pack), StringComparer.Ordinal),
            GrammarMode.Phrases => BuildPhrases(pack),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

        if (isKnownWord is not null)
        {
            entries = MatchVocabulary(entries, CheckVocabulary(pack, isKnownWord));
        }

        entries.Add(SpeechRecognizer.UnknownWord);
        return [.. entries];
    }

    /// <summary>Compares the pack vocabulary with a model vocabulary: which words need another spelling, which are absent.</summary>
    public static VocabularyReport CheckVocabulary(LanguagePack pack, Func<string, bool> isKnownWord)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(isKnownWord);
        var substituted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var word in VocabularyBuilder.Build(pack))
        {
            switch (ModelSpelling(word, isKnownWord))
            {
                case null:
                    missing.Add(word);
                    break;
                case var spelled when spelled != word:
                    substituted[word] = spelled;
                    break;
            }
        }

        return new VocabularyReport(substituted, missing);
    }

    /// <summary>
    /// The word as the model knows it: itself, or a variant with some <c>е</c> replaced by <c>ё</c> (packs are written
    /// with <c>е</c> because normalization folds <c>ё</c>). Null when the model knows no spelling.
    /// </summary>
    internal static string? ModelSpelling(string word, Func<string, bool> isKnownWord)
    {
        if (isKnownWord(word))
        {
            return word;
        }

        var positions = Enumerable.Range(0, word.Length).Where(i => word[i] == 'е').Take(MaxYoPositions).ToArray();

        // Fewer replacements first: "ещё" before an unlikely "ёщё"-style spelling.
        var subsets = Enumerable.Range(1, (1 << positions.Length) - 1).OrderBy(m => int.PopCount(m)).ThenBy(m => m);
        foreach (var mask in subsets)
        {
            var chars = word.ToCharArray();
            for (var bit = 0; bit < positions.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    chars[positions[bit]] = 'ё';
                }
            }

            var variant = new string(chars);
            if (isKnownWord(variant))
            {
                return variant;
            }
        }

        return null;
    }

    private const int MaxYoPositions = 4;

    private static SortedSet<string> MatchVocabulary(SortedSet<string> entries, VocabularyReport report)
    {
        var missing = new HashSet<string>(report.Missing, StringComparer.Ordinal);
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var words = entry.Split(' ');
            if (words.Any(missing.Contains))
            {
                continue;
            }

            result.Add(string.Join(' ', words.Select(w => report.Substituted.GetValueOrDefault(w, w))));
        }

        return result;
    }

    /// <summary>
    /// The grammar as the JSON array Vosk expects. Vosk does not decode <c>\uXXXX</c> escapes, so Cyrillic letters and
    /// apostrophes must stay literal.
    /// </summary>
    public static string ToJson(IReadOnlyList<string> grammar) => JsonSerializer.Serialize(grammar, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static SortedSet<string> BuildPhrases(LanguagePack pack)
    {
        var spellings = NumberSpellings.Build(pack.NumberParser);
        var allUnits = pack.Units.Values.SelectMany(u => u.Synonyms).Distinct(StringComparer.Ordinal).ToArray();
        var phrases = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var template in pack.Templates)
        {
            var parts = new List<IReadOnlyList<string>>(template.Tokens.Count);
            foreach (var token in template.Tokens)
            {
                parts.Add(token switch
                {
                    WordLiteral word => [word.Text],
                    NumberLiteral number => spellings.TryGetValue(number.Value, out var spelled) ? spelled : [],
                    UnitLiteral unit => pack.Units.TryGetValue(unit.Unit, out var definition) ? definition.Synonyms : [],
                    AmountSlot => spellings.Values.SelectMany(s => s).ToArray(),
                    UnitSlot { Optional: true } => [string.Empty, .. allUnits],
                    UnitSlot => allUnits,
                    _ => throw new InvalidOperationException($"unknown template token {token}"),
                });
            }

            Expand(parts, 0, [], phrases);
        }

        // Compound commands are sequences of phrases joined by conjunctions; Vosk lets entries follow each other.
        foreach (var conjunction in pack.Conjunctions)
        {
            phrases.Add(string.Join(' ', conjunction));
        }

        return phrases;
    }

    private static void Expand(List<IReadOnlyList<string>> parts, int index, List<string> prefix, SortedSet<string> output)
    {
        if (index == parts.Count)
        {
            var phrase = string.Join(' ', prefix.Where(p => p.Length > 0));
            if (phrase.Length > 0)
            {
                output.Add(phrase);
            }

            return;
        }

        foreach (var option in parts[index])
        {
            prefix.Add(option);
            Expand(parts, index + 1, prefix, output);
            prefix.RemoveAt(prefix.Count - 1);
        }
    }
}

/// <summary>
/// Every way to say each number 0–999 that a language's parser accepts, found by searching word sequences the parser
/// reads completely. Language-agnostic: it only uses <see cref="INumberParser"/>.
/// </summary>
internal static class NumberSpellings
{
    private const int MaxWords = 6;

    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Build(INumberParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        var words = parser.Words.Order(StringComparer.Ordinal).ToArray();
        var connectors = words.Where(w => !TryParseAll(parser, [w], out _)).ToArray();
        var found = new SortedDictionary<int, SortedSet<string>>();
        var frontier = new List<string[]>();

        foreach (var word in words)
        {
            if (TryParseAll(parser, [word], out var value))
            {
                Add(found, value, [word]);
                frontier.Add([word]);
            }
        }

        for (var length = 2; length <= MaxWords && frontier.Count > 0; length++)
        {
            var next = new List<string[]>();
            foreach (var sequence in frontier)
            {
                foreach (var word in words)
                {
                    string[] extended = [.. sequence, word];
                    if (TryParseAll(parser, extended, out var value))
                    {
                        if (Add(found, value, extended))
                        {
                            next.Add(extended);
                        }

                        continue;
                    }

                    // A connector ("and") only makes sense with a number word after it.
                    if (length < MaxWords && Array.IndexOf(connectors, word) >= 0)
                    {
                        foreach (var after in words)
                        {
                            string[] joined = [.. extended, after];
                            if (TryParseAll(parser, joined, out var joinedValue) && Add(found, joinedValue, joined))
                            {
                                next.Add(joined);
                            }
                        }
                    }
                }
            }

            frontier = next;
        }

        return found.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value]);
    }

    private static bool Add(SortedDictionary<int, SortedSet<string>> found, int value, string[] words)
    {
        if (value is < INumberParser.MinValue or > INumberParser.MaxValue)
        {
            return false;
        }

        if (!found.TryGetValue(value, out var set))
        {
            set = new SortedSet<string>(StringComparer.Ordinal);
            found[value] = set;
        }

        return set.Add(string.Join(' ', words));
    }

    private static bool TryParseAll(INumberParser parser, string[] words, out int value) =>
        parser.TryParse(words, out value, out var consumed) && consumed == words.Length;
}
