using PitVoice.Core.Languages;

namespace PitVoice.Core.Asr;

/// <summary>
/// Collects every word that can occur in a command of a language: words of every lexicon entry, unit synonyms,
/// conjunctions, fillers, negations, reject words and all number words the language's parser understands. The output is sorted and
/// duplicate-free, so it is stable between runs (FR-ASR-2).
/// </summary>
public static class VocabularyBuilder
{
    public static IReadOnlyList<string> Build(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in pack.Lexicon.Entries)
        {
            words.UnionWith(entry.Words);
        }

        foreach (var unit in pack.Units.Values)
        {
            words.UnionWith(unit.Synonyms);
        }

        foreach (var conjunction in pack.Conjunctions)
        {
            words.UnionWith(conjunction);
        }

        words.UnionWith(pack.Fillers);
        words.UnionWith(pack.Negations);
        words.UnionWith(pack.RejectWords);
        words.UnionWith(pack.NumberParser.Words);

        words.RemoveWhere(string.IsNullOrWhiteSpace);
        return words.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Words that only the user's additions bring into the vocabulary (FR-LANG-5), sorted.</summary>
    public static IReadOnlyList<string> BuildUserAdditionWords(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var builtIn = pack.Lexicon.Entries.Where(e => !e.IsUserAddition).SelectMany(e => e.Words).ToHashSet(StringComparer.Ordinal);
        var added = new SortedSet<string>(pack.UserEntries.SelectMany(e => e.Words), StringComparer.Ordinal);
        added.ExceptWith(builtIn);
        added.ExceptWith(pack.NumberParser.Words);
        return [.. added];
    }

    /// <summary>
    /// Where a vocabulary word is used, for reports about words the model does not know: lexicon classes
    /// (<c>tires.targets.lf</c>), or the other word lists.
    /// </summary>
    public static string Usage(LanguagePack pack, string word)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var classes = pack.Lexicon.Entries
            .Where(e => e.Words.Contains(word))
            .Select(e => e.IsUserAddition ? $"{e.Class} (own)" : e.Class.Name)
            .Distinct()
            .ToArray();
        if (classes.Length > 0)
        {
            return string.Join(", ", classes);
        }

        if (pack.Units.Values.Any(u => u.Synonyms.Contains(word)))
        {
            return "unit synonyms";
        }

        if (pack.NumberParser.Words.Contains(word))
        {
            return "number words";
        }

        if (pack.Conjunctions.Any(c => c.Contains(word)))
        {
            return "conjunctions";
        }

        return pack.Fillers.Contains(word) ? "fillers"
            : pack.Negations.Contains(word) ? "negations"
            : pack.RejectWords.Contains(word) ? "reject words"
            : "vocabulary";
    }
}
