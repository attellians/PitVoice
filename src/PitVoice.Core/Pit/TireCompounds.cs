namespace PitVoice.Core.Pit;

/// <summary>Kind of tire compound a command asks for (design D1, D6).</summary>
public enum TireCompoundKind
{
    Dry,
    Wet,
}

/// <summary>One compound of the car's tire list (session info <c>DriverInfo.DriverTires</c>).</summary>
/// <param name="Index">Number of the compound, the parameter of <see cref="PitCommand.TC"/>.</param>
/// <param name="Type">Type as the game writes it: "Dry", "Wet", "Hard", ...</param>
public sealed record TireCompound(int Index, string Type)
{
    /// <summary>Wet when the type has "wet" or "rain" in any case; every other compound is dry (design D6).</summary>
    public TireCompoundKind Kind =>
        Type.Contains("wet", StringComparison.OrdinalIgnoreCase) || Type.Contains("rain", StringComparison.OrdinalIgnoreCase)
            ? TireCompoundKind.Wet
            : TireCompoundKind.Dry;

    public override string ToString() => $"{Index} {Type}";
}

/// <summary>Why a compound command cannot be carried out on this car.</summary>
public enum CompoundProblem
{
    /// <summary>The car has no dry compound, or the game gave no tire list.</summary>
    NoDry,

    /// <summary>The car has no wet compound, or the game gave no tire list.</summary>
    NoWet,

    /// <summary>The car has several dry compounds; which one is meant is not guessed.</summary>
    SeveralDry,
}

/// <summary>Picks the compound of the car's list for a compound command (design D6).</summary>
public static class TireCompoundSelector
{
    /// <summary>
    /// The index to send for <paramref name="kind"/>: the first wet compound, or the only dry one. Null with a problem
    /// when the list is unknown, has no such compound or has several dry ones.
    /// </summary>
    public static int? Select(IReadOnlyList<TireCompound>? compounds, TireCompoundKind kind, out CompoundProblem? problem)
    {
        var missing = kind == TireCompoundKind.Wet ? CompoundProblem.NoWet : CompoundProblem.NoDry;
        var matching = compounds?.Where(c => c.Kind == kind).ToArray() ?? [];
        if (matching.Length == 0)
        {
            problem = missing;
            return null;
        }

        if (kind == TireCompoundKind.Dry && matching.Length > 1)
        {
            problem = CompoundProblem.SeveralDry;
            return null;
        }

        problem = null;
        return matching[0].Index;
    }
}
