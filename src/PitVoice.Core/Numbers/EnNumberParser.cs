using System.Collections.Frozen;

namespace PitVoice.Core.Numbers;

/// <summary>
/// English cardinal numbers 0..999: "thirty five" (hyphens are split by the normalizer), "a hundred",
/// "one hundred and five", "two hundred twenty". Input is expected to be lower-case.
/// </summary>
public sealed class EnNumberParser : INumberParser
{
    private const string Hundred = "hundred";
    private const string Article = "a";
    private const string And = "and";

    private static readonly FrozenDictionary<string, int> Units = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["zero"] = 0,
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int> Teens = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["ten"] = 10,
        ["eleven"] = 11,
        ["twelve"] = 12,
        ["thirteen"] = 13,
        ["fourteen"] = 14,
        ["fifteen"] = 15,
        ["sixteen"] = 16,
        ["seventeen"] = 17,
        ["eighteen"] = 18,
        ["nineteen"] = 19,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int> Tens = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["twenty"] = 20,
        ["thirty"] = 30,
        ["forty"] = 40,
        ["fifty"] = 50,
        ["sixty"] = 60,
        ["seventy"] = 70,
        ["eighty"] = 80,
        ["ninety"] = 90,
    }.ToFrozenDictionary();

    // Declared after the tables: static initializers run in textual order and the constructor reads them.
    public static EnNumberParser Instance { get; } = new();

    private EnNumberParser()
    {
        Words = Units.Keys.Concat(Teens.Keys).Concat(Tens.Keys).Concat([Hundred, Article, And])
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyCollection<string> Words { get; }

    public bool TryParse(ReadOnlySpan<string> words, out int value, out int consumed)
    {
        value = 0;
        consumed = 0;
        if (words.IsEmpty)
        {
            return false;
        }

        if (DigitToken.TryParse(words[0], out value))
        {
            consumed = 1;
            return true;
        }

        // Grammar: [ (a | unit) hundred [and] ] ( teens | [tens] [units] )
        var i = 0;
        var hasHundreds = false;
        if (i + 1 < words.Length && words[i + 1] == Hundred)
        {
            if (words[i] == Article)
            {
                value = 100;
                i += 2;
                hasHundreds = true;
            }
            else if (Units.TryGetValue(words[i], out var h) && h != 0)
            {
                value = h * 100;
                i += 2;
                hasHundreds = true;
            }
        }

        if (!hasHundreds && words[i] == Hundred)
        {
            value = 100;
            i++;
            hasHundreds = true;
        }

        if (hasHundreds && i + 1 < words.Length && words[i] == And && StartsRemainder(words[i + 1]))
        {
            i++; // "and" is only swallowed when a real remainder follows
        }

        if (i < words.Length && Teens.TryGetValue(words[i], out var teen))
        {
            value += teen;
            i++;
        }
        else
        {
            if (i < words.Length && Tens.TryGetValue(words[i], out var tens))
            {
                value += tens;
                i++;
            }

            if (i < words.Length && Units.TryGetValue(words[i], out var unit) && (i == 0 || unit != 0))
            {
                value += unit;
                i++;
            }
        }

        consumed = i;
        return consumed > 0;
    }

    private static bool StartsRemainder(string word) =>
        Teens.ContainsKey(word) || Tens.ContainsKey(word) || (Units.TryGetValue(word, out var u) && u != 0);
}
