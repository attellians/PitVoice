using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;

namespace PitVoice.Core.Asr;

/// <summary>
/// Collects every word that can occur in a command of a language: template words, unit synonyms, conjunctions,
/// fillers, negations and all number words the language's parser understands. The output is sorted and
/// duplicate-free, so it is stable between runs (FR-ASR-2).
/// </summary>
public static class VocabularyBuilder
{
    public static IReadOnlyList<string> Build(LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var words = new HashSet<string>(StringComparer.Ordinal);

        foreach (var template in pack.Templates)
        {
            foreach (var token in template.Tokens)
            {
                if (token is WordLiteral word)
                {
                    words.Add(word.Text);
                }
            }
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
        words.UnionWith(pack.NumberParser.Words);

        words.RemoveWhere(string.IsNullOrWhiteSpace);
        return words.Order(StringComparer.Ordinal).ToArray();
    }
}
