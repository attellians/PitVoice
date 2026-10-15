using System.Globalization;

namespace PitVoice.Core.Ptt;

/// <summary>What kind of control the PTT is bound to.</summary>
public enum PttDeviceKind
{
    /// <summary>A button of a game device (wheel, button box, joystick).</summary>
    Joystick,

    /// <summary>A keyboard key.</summary>
    Keyboard,
}

/// <summary>How the PTT button controls the recording (FR-PTT-7).</summary>
public enum PttMode
{
    /// <summary>Recording lasts while the button is held (default).</summary>
    Hold,

    /// <summary>First press starts, second press stops ("walkie-talkie").</summary>
    Toggle,
}

/// <summary>
/// Identifies one physical button or key independently of device order (FR-PTT-6).
/// </summary>
/// <param name="Kind">Joystick button or keyboard key.</param>
/// <param name="Device">Device model GUID for a joystick; empty for the keyboard.</param>
/// <param name="Code">Button number starting at 1 (as Windows shows it) or the virtual-key code.</param>
public readonly record struct PttKey(PttDeviceKind Kind, string Device, int Code)
{
    public static PttKey JoystickButton(string guid, int button) => new(PttDeviceKind.Joystick, guid, button);

    public static PttKey KeyboardKey(int virtualKey) => new(PttDeviceKind.Keyboard, string.Empty, virtualKey);

    public override string ToString() => Kind == PttDeviceKind.Joystick
        ? string.Create(CultureInfo.InvariantCulture, $"{Device} button {Code}")
        : string.Create(CultureInfo.InvariantCulture, $"vk 0x{Code:X2}");
}

/// <summary>A raw edge from a PTT source: a button or key went down or up.</summary>
/// <param name="Key">The control.</param>
/// <param name="Down">True on press, false on release.</param>
/// <param name="Name">For messages: device name for a joystick, key name for the keyboard.</param>
public sealed record PttInput(PttKey Key, bool Down, string Name)
{
    /// <summary>For messages, e.g. "Fanatec Wheel, button 5" or "key F13".</summary>
    public string Describe() => PttBinding.Describe(Key, Name);
}

/// <summary>A game device appeared or disappeared.</summary>
/// <param name="Device">Device model GUID.</param>
/// <param name="Name">Device name, for messages.</param>
/// <param name="Connected">True when connected, false when removed.</param>
public sealed record PttDeviceChange(string Device, string Name, bool Connected);

/// <summary>
/// The control bound to PTT. Stored by device model GUID and button number (joystick) or virtual-key code (keyboard);
/// <see cref="Name"/> is only for messages.
/// </summary>
public sealed record PttBinding(PttKey Key, string Name)
{
    public PttDeviceKind Kind => Key.Kind;

    public static PttBinding From(PttInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new PttBinding(input.Key, input.Name);
    }

    public bool Matches(PttKey key) => Key.Kind == key.Kind && Key.Code == key.Code
        && string.Equals(Key.Device, key.Device, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="device"/> is the device of a joystick binding.</summary>
    public bool IsDevice(string device) => Key.Kind == PttDeviceKind.Joystick
        && string.Equals(Key.Device, device, StringComparison.OrdinalIgnoreCase);

    public string Describe() => Describe(Key, Name);

    public override string ToString() => Describe();

    internal static string Describe(PttKey key, string name) => key.Kind == PttDeviceKind.Joystick
        ? string.Create(CultureInfo.InvariantCulture, $"{name}, button {key.Code}")
        : $"key {name}";
}

/// <summary>
/// A source of PTT edges: game device buttons or the keyboard. Sources only report edges; timing rules live in
/// <see cref="PttStateMachine"/> (design decision 4). Events are raised on the source's own thread, one at a time.
/// </summary>
public interface IPttSource : IDisposable
{
    /// <summary>A button or key went down or up.</summary>
    event Action<PttInput>? Input;

    /// <summary>A game device was connected or removed. Devices present at start are reported as connected.</summary>
    event Action<PttDeviceChange>? DeviceChanged;

    /// <summary>Starts listening. Returns an error message when the source cannot work, otherwise null.</summary>
    string? Start();
}
