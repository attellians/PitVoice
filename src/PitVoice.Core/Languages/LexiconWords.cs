using PitVoice.Core.Parsing;

namespace PitVoice.Core.Languages;

/// <summary>
/// Word rules shared by packs and user additions (design D3): an entry is one or more plain words; it must not contain
/// slots, digits, fillers, negations, conjunctions or unit words, and a number alone is not an entry. Words shared by
/// several groups are allowed; ambiguity is left to the parser, which rejects it.
/// </summary>
internal sealed class LexiconWords
{
    private readonly Normalizer _normalizer;
    private readonly IReadOnlyList<IReadOnlyList<string>> _conjunctions;
    private readonly IReadOnlySet<string> _negations;

    public LexiconWords(Normalizer normalizer, IReadOnlyList<IReadOnlyList<string>> conjunctions, IReadOnlySet<string> negations)
    {
        _normalizer = normalizer;
        _conjunctions = conjunctions;
        _negations = negations;
    }

    /// <summary>Checks one raw entry of <paramref name="wordClass"/>; returns null and reports through <paramref name="error"/> when it is invalid.</summary>
    public LexiconEntry? Check(WordClass wordClass, string? raw, bool isUserAddition, Action<string> error)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            error($"empty entry in '{wordClass}'");
            return null;
        }

        if (text.Contains('{') || text.Contains('}'))
        {
            error($"'{text}' in '{wordClass}' has a slot; classes hold plain words, numbers and units are recognized by themselves");
            return null;
        }

        if (text.Any(char.IsDigit))
        {
            error($"'{text}' in '{wordClass}' has digits; write words only");
            return null;
        }

        var tokens = _normalizer.Tokenize(Normalizer.SplitWords(text).Select(w => new RecognizedWord(w)).ToArray(), removeFillers: false);
        if (tokens.Count == 0)
        {
            error($"'{text}' in '{wordClass}' has no words");
            return null;
        }

        var valid = true;
        foreach (var token in tokens)
        {
            switch (token)
            {
                case UnitToken unit:
                    error($"'{text}' in '{wordClass}' contains the unit word '{unit.Text}'; units are recognized by themselves");
                    valid = false;
                    break;
                case WordToken word when _normalizer.Rules.Fillers.Contains(word.Text):
                    error($"'{text}' in '{wordClass}' contains the filler '{word.Text}', which is removed from the phrase before parsing");
                    valid = false;
                    break;
                case WordToken word when _negations.Contains(word.Text):
                    error($"'{word.Text}' is in 'negations' and cannot be in '{wordClass}': it would turn the command into its opposite");
                    valid = false;
                    break;
            }
        }

        if (tokens is [NumberToken])
        {
            error($"'{text}' in '{wordClass}' is a number; a number alone is the fuel amount");
            valid = false;
        }

        var words = tokens.SelectMany(t => t.Text.Split(' ')).ToArray();
        foreach (var conjunction in _conjunctions)
        {
            if (ContainsSequence(words, conjunction))
            {
                error($"'{text}' in '{wordClass}' contains the conjunction '{string.Join(' ', conjunction)}', which splits the phrase before parsing");
                valid = false;
                break;
            }
        }

        return valid
            ? new LexiconEntry(wordClass, string.Join(' ', words), tokens.Select(Lexicon.KeyOf).ToArray(), isUserAddition)
            : null;
    }

    /// <summary>
    /// Words in both <c>action</c> and <c>keep</c> of one group: "не трогай" means keep, "трогай" alone means nothing, so a
    /// word cannot be both.
    /// </summary>
    public static IEnumerable<(LexiconEntry Action, LexiconEntry Keep)> Opposites(IEnumerable<LexiconEntry> entries)
    {
        var list = entries.ToArray();
        foreach (var keep in list.Where(e => e.Class.Role == WordRole.Keep))
        {
            foreach (var action in list.Where(e => e.Class.Role == WordRole.Action && e.Class.Group == keep.Class.Group && e.Text == keep.Text))
            {
                yield return (action, keep);
            }
        }
    }

    private static bool ContainsSequence(IReadOnlyList<string> words, IReadOnlyList<string> sequence)
    {
        for (var start = 0; start + sequence.Count <= words.Count; start++)
        {
            var match = true;
            for (var k = 0; k < sequence.Count; k++)
            {
                if (words[start + k] != sequence[k])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
