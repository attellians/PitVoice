namespace PitVoice.Core.Plural;

/// <summary>CLDR cardinal rules for English: one (1), other (everything else).</summary>
public sealed class EnPluralRules : IPluralRules
{
    public static EnPluralRules Instance { get; } = new();

    private EnPluralRules()
    {
    }

    public IReadOnlyList<PluralCategory> Categories { get; } = [PluralCategory.One, PluralCategory.Other];

    public PluralCategory Select(int number) => Math.Abs(number) == 1 ? PluralCategory.One : PluralCategory.Other;
}
