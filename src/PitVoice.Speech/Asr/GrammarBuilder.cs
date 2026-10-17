using System.Text.Encodings.Web;
using System.Text.Json;
using PitVoice.Core.Asr;
using PitVoice.Core.Languages;

namespace PitVoice.Speech.Asr;

/// <summary>How a pack vocabulary fits a model vocabulary.</summary>
/// <param name="Substituted">Pack word → model spelling (only words that differ, e.g. <c>еще</c> → <c>ещё</c>).</param>
/// <param name="Missing">Pack words the model does not know in any spelling; they cannot be recognized.</param>
public sealed record VocabularyReport(IReadOnlyDictionary<string, string> Substituted, IReadOnlyList<string> Missing);

/// <summary>
/// Builds the Vosk grammar from a language pack: every vocabulary word as its own entry, plus the unknown-word marker
/// (hypothesis A of the stage 2 spike). The output is sorted and duplicate-free, so one pack always gives the
/// same grammar (FR-ASR-2).
/// </summary>
public static class GrammarBuilder
{
    /// <param name="pack">Language pack.</param>
    /// <param name="isKnownWord">
    /// Model vocabulary lookup. When given, every word is written the way the model spells it (<c>еще</c> → <c>ещё</c>)
    /// and words the model does not know are left out.
    /// </param>
    public static IReadOnlyList<string> Build(LanguagePack pack, Func<string, bool>? isKnownWord = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var entries = new SortedSet<string>(VocabularyBuilder.Build(pack), StringComparer.Ordinal);

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
    /// Words of the user's additions that the model does not know (FR-LANG-5): phrases with them work as text but
    /// cannot be recognized by voice, so the user gets a warning naming them.
    /// </summary>
    public static IReadOnlyList<string> MissingAdditionWords(LanguagePack pack, VocabularyReport report)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(report);
        var missing = new HashSet<string>(report.Missing, StringComparer.Ordinal);
        return VocabularyBuilder.BuildUserAdditionWords(pack).Where(missing.Contains).ToArray();
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
}
