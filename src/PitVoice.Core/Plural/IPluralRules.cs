namespace PitVoice.Core.Plural;

/// <summary>Plural category selection for one language (CLDR cardinal rules).</summary>
public interface IPluralRules
{
    /// <summary>Categories this language actually produces; a unit must define a form for each of them.</summary>
    IReadOnlyList<PluralCategory> Categories { get; }

    PluralCategory Select(int number);
}
