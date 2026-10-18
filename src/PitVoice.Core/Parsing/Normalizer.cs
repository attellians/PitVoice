using System.Globalization;
using System.Text;

namespace PitVoice.Core.Parsing;

/// <summary>
/// Turns recognized words into tokens: lower case, punctuation stripped, <c>ё → е</c>, fillers removed, number words
/// folded into one <see cref="NumberToken"/>, unit synonyms resolved. Per-word confidence is preserved; a folded number
/// takes the minimum confidence of its words.
/// </summary>
public sealed class Normalizer
{
    private readonly NormalizationRules _rules;

    public Normalizer(NormalizationRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    public NormalizationRules Rules => _rules;

    public IReadOnlyList<Token> Normalize(string text) => Normalize(RecognizedPhrase.FromText(text));

    public IReadOnlyList<Token> Normalize(RecognizedPhrase phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        return Tokenize(phrase.Words, removeFillers: true);
    }

    /// <summary>
    /// Full pipeline with optional filler removal. Template compilation keeps fillers so that it can report them.
    /// </summary>
    internal IReadOnlyList<Token> Tokenize(IReadOnlyList<RecognizedWord> words, bool removeFillers)
    {
        var texts = new List<string>();
        var confidences = new List<double>();
        var fillerAfter = new List<bool>();
        foreach (var word in words)
        {
            var confidence = Clamp(word.Confidence);
            foreach (var piece in SplitWords(word.Text))
            {
                if (removeFillers && _rules.Fillers.Contains(piece))
                {
                    if (fillerAfter.Count > 0)
                    {
                        fillerAfter[^1] = true;
                    }

                    continue;
                }

                texts.Add(piece);
                confidences.Add(confidence);
                fillerAfter.Add(false);
            }
        }

        var result = new List<Token>(texts.Count);
        var span = (ReadOnlySpan<string>)texts.ToArray();
        var i = 0;
        while (i < texts.Count)
        {
            if (_rules.NumberParser.TryParse(span[i..], out var value, out var consumed))
            {
                var confidence = 1.0;
                for (var k = i; k < i + consumed; k++)
                {
                    confidence = Math.Min(confidence, confidences[k]);
                }

                result.Add(new NumberToken(value, string.Join(' ', texts.Skip(i).Take(consumed)), confidence)
                {
                    FillerAfter = fillerAfter[i + consumed - 1],
                });
                i += consumed;
                continue;
            }

            if (_rules.UnitSynonyms.TryGetValue(texts[i], out var unit))
            {
                result.Add(new UnitToken(unit, texts[i], confidences[i]) { FillerAfter = fillerAfter[i] });
            }
            else
            {
                result.Add(new WordToken(texts[i], confidences[i]) { FillerAfter = fillerAfter[i] });
            }

            i++;
        }

        return result;
    }

    /// <summary>
    /// Splits raw text into normalized words: lower case, <c>ё → е</c>, every character that is not a letter, digit or
    /// apostrophe acts as a separator (so "thirty-five" becomes two words and "залей," loses the comma).
    /// </summary>
    public static IReadOnlyList<string> SplitWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in text.ToLower(CultureInfo.InvariantCulture))
        {
            var c = raw switch
            {
                'ё' => 'е',
                '’' or '`' => '\'',
                _ => raw,
            };

            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }

    /// <summary>Normalizes a single dictionary word (synonym, filler, conjunction word).</summary>
    public static string NormalizeWord(string word) => string.Join(' ', SplitWords(word));

    private static double Clamp(double? confidence) => confidence is null ? 1.0 : Math.Clamp(confidence.Value, 0.0, 1.0);
}
