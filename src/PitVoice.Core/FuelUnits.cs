namespace PitVoice.Core;

/// <summary>Canonical fuel unit ids. Language packs map their synonyms onto these.</summary>
public static class FuelUnits
{
    public const string Liters = "liters";
    public const string Gallons = "gallons";

    public static IReadOnlyList<string> All { get; } = [Liters, Gallons];

    public static bool IsKnown(string unit) => unit is Liters or Gallons;
}
