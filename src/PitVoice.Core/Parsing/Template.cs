using PitVoice.Core.Intents;

namespace PitVoice.Core.Parsing;

/// <summary>A token of a compiled template.</summary>
public abstract record TemplateToken
{
    /// <summary>True for <c>{amount}</c>, <c>{unit}</c> and <c>{unit?}</c>. Slots are never touched by fuzzy matching.</summary>
    public abstract bool IsSlot { get; }
}

/// <summary>A literal word that must appear in the phrase.</summary>
public sealed record WordLiteral(string Text) : TemplateToken
{
    public override bool IsSlot => false;

    public override string ToString() => Text;
}

/// <summary>A literal number written in the template as words (e.g. "все четыре" → 4).</summary>
public sealed record NumberLiteral(int Value) : TemplateToken
{
    public override bool IsSlot => false;

    public override string ToString() => $"#{Value}";
}

/// <summary>A literal unit word written in the template (e.g. "литров" → liters).</summary>
public sealed record UnitLiteral(string Unit) : TemplateToken
{
    public override bool IsSlot => false;

    public override string ToString() => $"<{Unit}>";
}

/// <summary><c>{amount}</c>: captures a number.</summary>
public sealed record AmountSlot : TemplateToken
{
    public static AmountSlot Instance { get; } = new();

    public override bool IsSlot => true;

    public override string ToString() => "{amount}";
}

/// <summary><c>{unit}</c> or <c>{unit?}</c>: captures a unit; the optional form may match nothing.</summary>
public sealed record UnitSlot(bool Optional) : TemplateToken
{
    public static UnitSlot Required { get; } = new(false);

    public static UnitSlot OptionalSlot { get; } = new(true);

    public override bool IsSlot => true;

    public override string ToString() => Optional ? "{unit?}" : "{unit}";
}

/// <summary>A compiled pattern of one intent.</summary>
/// <param name="Intent">Intent the template belongs to.</param>
/// <param name="Source">Pattern text as written in the pack.</param>
/// <param name="Tokens">Compiled tokens.</param>
public sealed record Template(IntentId Intent, string Source, IReadOnlyList<TemplateToken> Tokens)
{
    /// <summary>Number of tokens including optional slots.</summary>
    public int Length => Tokens.Count;

    /// <summary>Number of tokens that must be present in the phrase.</summary>
    public int RequiredLength => Tokens.Count(t => t is not UnitSlot { Optional: true });

    public override string ToString() => $"{Intent}: \"{Source}\"";
}
