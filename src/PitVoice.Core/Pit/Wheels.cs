namespace PitVoice.Core.Pit;

/// <summary>Set of wheels. The bits match the tire bits of <see cref="PitServiceFlags"/>.</summary>
[Flags]
public enum Wheels
{
    None = 0,
    LF = 0x01,
    RF = 0x02,
    LR = 0x04,
    RR = 0x08,
    Left = LF | LR,
    Right = RF | RR,
    Front = LF | RF,
    Rear = LR | RR,
    All = LF | RF | LR | RR,
}

public static class WheelsExtensions
{
    /// <summary>Single wheels in SDK order: LF, RF, LR, RR.</summary>
    public static IReadOnlyList<Wheels> Each { get; } = [Wheels.LF, Wheels.RF, Wheels.LR, Wheels.RR];

    /// <summary>The single wheels contained in the set, in SDK order.</summary>
    public static IEnumerable<Wheels> Split(this Wheels wheels) => Each.Where(w => (wheels & w) != 0);

    /// <summary>The pit command that selects one wheel.</summary>
    public static PitCommand ToPitCommand(this Wheels wheel) => wheel switch
    {
        Wheels.LF => PitCommand.LF,
        Wheels.RF => PitCommand.RF,
        Wheels.LR => PitCommand.LR,
        Wheels.RR => PitCommand.RR,
        // Without the value: a stage failure reaches the log, and slots of a phrase must not (logging spec).
        _ => throw new ArgumentOutOfRangeException(nameof(wheel), "not a single wheel"),
    };

    /// <summary>Compact text such as <c>LF+LR</c> or <c>none</c>.</summary>
    public static string Describe(this Wheels wheels) =>
        wheels == Wheels.None ? "none" : string.Join('+', wheels.Split().Select(w => w.ToString()));
}
