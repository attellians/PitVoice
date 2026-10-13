using System.Collections.Frozen;

namespace PitVoice.Core.Numbers;

/// <summary>
/// Russian cardinal numbers 0..999 in the nominative case, with gender variants for one and two
/// (один/одна/одно, два/две). Input is expected to be lower-case with <c>ё</c> already replaced by <c>е</c>.
/// </summary>
public sealed class RuNumberParser : INumberParser
{
    private static readonly FrozenDictionary<string, int> Units = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["ноль"] = 0,
        ["один"] = 1, ["одна"] = 1, ["одно"] = 1,
        ["два"] = 2, ["две"] = 2,
        ["три"] = 3,
        ["четыре"] = 4,
        ["пять"] = 5,
        ["шесть"] = 6,
        ["семь"] = 7,
        ["восемь"] = 8,
        ["девять"] = 9,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int> Teens = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["десять"] = 10,
        ["одиннадцать"] = 11,
        ["двенадцать"] = 12,
        ["тринадцать"] = 13,
        ["четырнадцать"] = 14,
        ["пятнадцать"] = 15,
        ["шестнадцать"] = 16,
        ["семнадцать"] = 17,
        ["восемнадцать"] = 18,
        ["девятнадцать"] = 19,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int> Tens = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["двадцать"] = 20,
        ["тридцать"] = 30,
        ["сорок"] = 40,
        ["пятьдесят"] = 50,
        ["шестьдесят"] = 60,
        ["семьдесят"] = 70,
        ["восемьдесят"] = 80,
        ["девяносто"] = 90,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, int> Hundreds = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["сто"] = 100,
        ["двести"] = 200,
        ["триста"] = 300,
        ["четыреста"] = 400,
        ["пятьсот"] = 500,
        ["шестьсот"] = 600,
        ["семьсот"] = 700,
        ["восемьсот"] = 800,
        ["девятьсот"] = 900,
    }.ToFrozenDictionary();

    // Declared after the tables: static initializers run in textual order and the constructor reads them.
    public static RuNumberParser Instance { get; } = new();

    private RuNumberParser()
    {
        Words = Units.Keys.Concat(Teens.Keys).Concat(Tens.Keys).Concat(Hundreds.Keys)
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

        // Grammar: [hundreds] ( teens | [tens] [units] ). "ноль" only stands alone.
        var i = 0;
        if (Hundreds.TryGetValue(words[i], out var hundreds))
        {
            value += hundreds;
            i++;
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
}
