namespace PitVoice.Core.Plural;

/// <summary>CLDR cardinal rules for Russian: one (1, 21, 101), few (2..4, 22..24), many (0, 5..20, 11..14, 25..30).</summary>
public sealed class RuPluralRules : IPluralRules
{
    public static RuPluralRules Instance { get; } = new();

    private RuPluralRules()
    {
    }

    public IReadOnlyList<PluralCategory> Categories { get; } = [PluralCategory.One, PluralCategory.Few, PluralCategory.Many];

    public PluralCategory Select(int number)
    {
        var n = Math.Abs(number);
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11)
        {
            return PluralCategory.One;
        }

        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
        {
            return PluralCategory.Few;
        }

        return PluralCategory.Many;
    }
}
