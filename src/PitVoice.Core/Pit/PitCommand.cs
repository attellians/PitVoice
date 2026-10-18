namespace PitVoice.Core.Pit;

/// <summary>
/// Pit commands of the iRacing SDK (<c>irsdk_PitCommandMode</c> in <c>irsdk_defines.h</c>). The numeric values are the
/// codes sent to the game and must not change.
/// </summary>
public enum PitCommand
{
    /// <summary>Clear all pit checkboxes.</summary>
    Clear = 0,

    /// <summary>Clean the windshield using one tear-off.</summary>
    WS = 1,

    /// <summary>Add fuel; the parameter is liters, 0 keeps the current amount.</summary>
    Fuel = 2,

    /// <summary>Change the left front tire; the parameter is pressure in kPa, 0 keeps the current pressure.</summary>
    LF = 3,

    /// <summary>Change the right front tire.</summary>
    RF = 4,

    /// <summary>Change the left rear tire.</summary>
    LR = 5,

    /// <summary>Change the right rear tire.</summary>
    RR = 6,

    /// <summary>Clear the tire checkboxes.</summary>
    ClearTires = 7,

    /// <summary>Request a fast repair.</summary>
    FR = 8,

    /// <summary>Uncheck the windshield tear-off.</summary>
    ClearWS = 9,

    /// <summary>Uncheck the fast repair.</summary>
    ClearFR = 10,

    /// <summary>Uncheck adding fuel.</summary>
    ClearFuel = 11,

    /// <summary>Select a tire compound; the parameter is the index of the compound in the car's tire list.</summary>
    TC = 12,
}

/// <summary>One command as it is sent to the game.</summary>
/// <param name="Command">SDK pit command.</param>
/// <param name="Parameter">Liters for <see cref="PitCommand.Fuel"/>, kPa for wheels (0 = keep), the compound index for <see cref="PitCommand.TC"/>, otherwise 0.</param>
public readonly record struct SentCommand(PitCommand Command, int Parameter)
{
    public override string ToString() => Parameter == 0 ? Command.ToString() : $"{Command}({Parameter})";
}
