namespace PitVoice.Core.Parsing;

/// <summary>A normalized token of the input phrase.</summary>
/// <param name="Text">Normalized source text (for diagnostics and conjunction matching).</param>
/// <param name="Confidence">Recognition confidence 0..1 (1.0 when unknown).</param>
public abstract record Token(string Text, double Confidence)
{
    /// <summary>True when a filler right after this token was removed ("не надо"): the token was not the last word.</summary>
    public bool FillerAfter { get; init; }

    public override string ToString() => Text;
}

/// <summary>A plain word.</summary>
public sealed record WordToken(string Text, double Confidence) : Token(Text, Confidence)
{
    public override string ToString() => Text;
}

/// <summary>A number folded from one or more number words or a digit string.</summary>
public sealed record NumberToken(int Value, string Text, double Confidence) : Token(Text, Confidence)
{
    public override string ToString() => $"#{Value}";
}

/// <summary>A unit synonym resolved to its canonical unit id.</summary>
public sealed record UnitToken(string Unit, string Text, double Confidence) : Token(Text, Confidence)
{
    public override string ToString() => $"<{Unit}>";
}
