using PitVoice.Core.Ptt;
using SDL;

namespace PitVoice.Input;

/// <summary>A game device as SDL sees it.</summary>
/// <param name="Guid">Device model GUID; the same for every unit of the model and stable across ports and reboots.</param>
/// <param name="Name">Device name, for messages.</param>
public sealed record GameDevice(string Guid, string Name);

/// <summary>
/// Buttons of wheels, button boxes and joysticks through SDL3 (design decision 5). A dedicated thread enables background
/// joystick events (the game keeps the focus), opens every device as it is added and drains the SDL event queue every
/// <see cref="PollInterval"/>. Without a window <c>SDL_WaitEvent</c> does not block: it polls about a thousand times a
/// second (2.4% of a core measured), so the thread sleeps on a kernel wait between polls instead (TZ §12, idle CPU).
/// SDL's own Ctrl+C handler is switched off so the host keeps receiving Ctrl+C. Buttons are reported with the device
/// model GUID and a number starting at 1, as Windows shows them. Devices present at start are reported as connected.
/// Events are raised on the SDL thread.
/// </summary>
public sealed unsafe class SdlJoystickSource : IPttSource
{
    /// <summary>How often the event queue is drained; adds at most this much to the PTT reaction.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private const string BackgroundEventsHint = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";
    private const string NoSignalHandlersHint = "SDL_NO_SIGNAL_HANDLERS";
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<SDL_JoystickID, OpenDevice> _devices = [];
    private readonly ManualResetEventSlim _started = new();
    private readonly ManualResetEvent _stop = new(false);
    private readonly string? _onlyDevice;
    private Thread? _thread;
    private string? _startError;

    /// <param name="onlyDevice">
    /// When set, only devices with this model GUID are opened. An open device costs CPU even when nobody presses
    /// anything (a wheel base sends ~1000 reports a second), so the host opens only the bound one.
    /// </param>
    public SdlJoystickSource(string? onlyDevice = null)
    {
        _onlyDevice = onlyDevice;
    }

    public event Action<PttInput>? Input;

    public event Action<PttDeviceChange>? DeviceChanged;

    public string? Start()
    {
        if (_thread is not null)
        {
            return _startError;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "PitVoice SDL joysticks" };
        _thread.Start();
        _started.Wait();
        return _startError;
    }

    public void Dispose()
    {
        if (_thread is { IsAlive: true } thread)
        {
            _stop.Set();
            thread.Join(StopTimeout);
        }

        _started.Dispose();
        _stop.Dispose();
    }

    private static void SetHints()
    {
        SDL3.SDL_SetHint(BackgroundEventsHint, "1");
        SDL3.SDL_SetHint(NoSignalHandlersHint, "1");
    }

    /// <summary>Game devices connected right now.</summary>
    public static IReadOnlyList<GameDevice> ListDevices(out string? error)
    {
        error = null;
        SetHints();
        if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_JOYSTICK))
        {
            error = SDL3.SDL_GetError();
            return [];
        }

        try
        {
            int count;
            var ids = SDL3.SDL_GetJoysticks(&count);
            if (ids is null)
            {
                return [];
            }

            var result = new List<GameDevice>(count);
            for (var i = 0; i < count; i++)
            {
                result.Add(new GameDevice(GuidToString(SDL3.SDL_GetJoystickGUIDForID(ids[i])), SDL3.SDL_GetJoystickNameForID(ids[i]) ?? "game device"));
            }

            SDL3.SDL_free(ids);
            return result;
        }
        finally
        {
            SDL3.SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_JOYSTICK);
            SDL3.SDL_Quit();
        }
    }

    private void Run()
    {
        SetHints();
        if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_JOYSTICK))
        {
            _startError = $"game devices are not available: {SDL3.SDL_GetError()}";
            _started.Set();
            return;
        }

        _started.Set();
        try
        {
            SDL_Event e;
            do
            {
                while (SDL3.SDL_PollEvent(&e))
                {
                    Handle(e);
                }
            }
            while (!_stop.WaitOne(PollInterval));
        }
        finally
        {
            foreach (var device in _devices.Values)
            {
                SDL3.SDL_CloseJoystick(device.Joystick);
            }

            _devices.Clear();
            SDL3.SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_JOYSTICK);
            SDL3.SDL_Quit();
        }
    }

    private void Handle(SDL_Event e)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
                Open(e.jdevice.which);
                break;
            case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
                Close(e.jdevice.which);
                break;
            case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_UP:
                if (_devices.TryGetValue(e.jbutton.which, out var device))
                {
                    Input?.Invoke(new PttInput(PttKey.JoystickButton(device.Guid, e.jbutton.button + 1), e.jbutton.down, device.Name));
                }

                break;
        }
    }

    private void Open(SDL_JoystickID id)
    {
        if (_devices.ContainsKey(id)
            || (_onlyDevice is not null && !string.Equals(GuidToString(SDL3.SDL_GetJoystickGUIDForID(id)), _onlyDevice, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var handle = SDL3.SDL_OpenJoystick(id);
        if (handle is null)
        {
            return;
        }

        var device = new OpenDevice((nint)handle, GuidToString(SDL3.SDL_GetJoystickGUID(handle)), SDL3.SDL_GetJoystickName(handle) ?? "game device");
        _devices[id] = device;
        DeviceChanged?.Invoke(new PttDeviceChange(device.Guid, device.Name, Connected: true));
    }

    private void Close(SDL_JoystickID id)
    {
        if (!_devices.Remove(id, out var device))
        {
            return;
        }

        SDL3.SDL_CloseJoystick(device.Joystick);
        DeviceChanged?.Invoke(new PttDeviceChange(device.Guid, device.Name, Connected: false));
    }

    private static string GuidToString(SDL_GUID guid)
    {
        const int size = 33;
        var buffer = stackalloc byte[size];
        SDL3.SDL_GUIDToString(guid, buffer, size);
        return new string((sbyte*)buffer);
    }

    private sealed record OpenDevice(nint Handle, string Guid, string Name)
    {
        public SDL_Joystick* Joystick => (SDL_Joystick*)Handle;
    }
}
