namespace PitVoice.Core.Parsing;

/// <summary>User settings that influence parsing.</summary>
public sealed record ParseOptions
{
    public const double DefaultConfidenceThreshold = 0.6;

    public static ParseOptions Default { get; } = new();

    /// <summary>A phrase is rejected if any part's confidence is below this value.</summary>
    public double ConfidenceThreshold { get; init; } = DefaultConfidenceThreshold;

    /// <summary>Unit used when the phrase does not name one (<see cref="FuelUnits"/>).</summary>
    public string DefaultUnit { get; init; } = FuelUnits.Liters;
}
