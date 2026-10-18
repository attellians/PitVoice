using System.Globalization;

namespace PitVoice.Core.Pit;

/// <summary>Bits of the <c>PitSvFlags</c> telemetry variable (<c>irsdk_PitSvFlags</c>).</summary>
[Flags]
public enum PitServiceFlags
{
    None = 0,
    LFTireChange = 0x01,
    RFTireChange = 0x02,
    LRTireChange = 0x04,
    RRTireChange = 0x08,
    FuelFill = 0x10,
    WindshieldTearoff = 0x20,
    FastRepair = 0x40,
}

/// <summary>What is selected in the black box, as the telemetry reports it.</summary>
/// <param name="Flags">Raw <c>PitSvFlags</c>; bits unknown to this version are kept as they are.</param>
/// <param name="FuelToAdd">Requested fuel in liters (<c>PitSvFuel</c>).</param>
/// <param name="FuelLevel">Fuel in the tank in liters (<c>FuelLevel</c>).</param>
public sealed record PitServiceState(PitServiceFlags Flags, double FuelToAdd, double FuelLevel)
{
    public static PitServiceState Empty { get; } = new(PitServiceFlags.None, 0, 0);

    /// <summary>
    /// Index of the compound selected for the pit stop (<c>PitSvTireCompound</c>); null when the game has no such variable.
    /// </summary>
    public int? TireCompound { get; init; }

    public Wheels Wheels => (Wheels)((int)Flags & (int)Wheels.All);

    public bool FuelFill => Flags.HasFlag(PitServiceFlags.FuelFill);

    public bool Tearoff => Flags.HasFlag(PitServiceFlags.WindshieldTearoff);

    public bool FastRepair => Flags.HasFlag(PitServiceFlags.FastRepair);

    /// <summary>True when no tire, fuel, tear-off or fast repair is selected.</summary>
    public bool NothingSelected =>
        (Flags & (PitServiceFlags.FuelFill | PitServiceFlags.WindshieldTearoff | PitServiceFlags.FastRepair)) == 0
        && Wheels == Wheels.None;

    public PitServiceState WithWheels(Wheels wheels) =>
        this with { Flags = (PitServiceFlags)(((int)Flags & ~(int)Wheels.All) | (int)wheels) };

    public PitServiceState With(PitServiceFlags flag, bool on) =>
        this with { Flags = on ? Flags | flag : Flags & ~flag };

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"wheels={Wheels.Describe()} fuel={(FuelFill ? "on" : "off")} {FuelToAdd:0.#} l, tearoff={(Tearoff ? "on" : "off")}, fast repair={(FastRepair ? "on" : "off")}, tank {FuelLevel:0.#} l{(TireCompound is { } compound ? $", compound {compound}" : string.Empty)}");
}
