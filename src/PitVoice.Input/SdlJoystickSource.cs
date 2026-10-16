using System.Collections.Concurrent;
using PitVoice.Core.Ptt;
using SDL;

namespace PitVoice.Input;

/// <summary>A game device as SDL sees it.</summary>
/// <param name="Guid">Device model GUID; the same for every unit of the model and stable across ports and reboots.</param>
/// <param name="Name">Device name, for messages.</param>
public sealed record GameDevice(string Guid, string Name);

/// <summary>
/// Buttons of wheels, button boxes and joysticks through SDL3 (design decision 5). Buttons are reported with the device
/// model GUID and a number starting at 1, as Windows shows them. Devices present at start are reported as connected.
/// Events are raised on the SDL thread. All sources of the process share one <see cref="SdlHub"/>: SDL is started once
/// and never shut down, because shutting it down and starting it again in one process loses device hot-plug events.
/// </summary>
public sealed class SdlJoystickSource : IPttSource
{
    /// <summary>How often the event queue is drained; adds at most this much to the PTT reaction.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly string? _onlyDevice;
    private SdlHub.Subscription? _subscription;

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
        if (_subscription is not null)
        {
            return null;
        }

        var (subscription, error) = SdlHub.Subscribe(_onlyDevice, input => Input?.Invoke(input), change => DeviceChanged?.Invoke(change));
        _subscription = subscription;
        return error;
    }

    public void Dispose()
    {
        if (_subscription is { } subscription)
        {
            _subscription = null;
            SdlHub.Unsubscribe(subscription);
        }
    }

    /// <summary>Game devices connected right now.</summary>
    public static IReadOnlyList<GameDevice> ListDevices(out string? error) => SdlHub.ListDevices(out error);
}

/// <summary>
/// The one SDL instance of the process. A background thread starts SDL on first use, enables background joystick events
/// (the game keeps the focus) and switches SDL's own Ctrl+C handler off. It keeps a device open only while a subscriber
/// wants it and drains the event queue every <see cref="SdlJoystickSource.PollInterval"/> while anybody listens. Without a
/// window <c>SDL_WaitEvent</c> does not block: it polls about a thousand times a second (2.4% of a core measured), so the
/// thread sleeps on a kernel wait between polls instead (TZ §12, idle CPU), and without subscribers it does not poll at
/// all. Subscribing and unsubscribing run on that thread and wait for it.
/// </summary>
internal static unsafe class SdlHub
{
    private const string BackgroundEventsHint = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";
    private const string NoSignalHandlersHint = "SDL_NO_SIGNAL_HANDLERS";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);

    private static readonly Lock StartLock = new();
    private static readonly ConcurrentQueue<Action> Commands = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static readonly Dictionary<SDL_JoystickID, Device> Present = [];
    private static readonly List<Subscription> Subscribers = [];
    private static Thread? _thread;
    private static string? _startError;

    public static (Subscription? Subscription, string? Error) Subscribe(string? onlyDevice, Action<PttInput> input, Action<PttDeviceChange> change)
    {
        if (EnsureStarted() is { } error)
        {
            return (null, error);
        }

        var subscription = new Subscription(onlyDevice, input, change);
        Run(() =>
        {
            Pump(); // devices that came while nobody listened
            Subscribers.Add(subscription);
            Reconcile();
            foreach (var device in Present.Values.Where(d => d.Handle != 0 && subscription.Wants(d.Guid)))
            {
                subscription.Change(new PttDeviceChange(device.Guid, device.Name, Connected: true));
            }
        });
        return (subscription, null);
    }

    public static void Unsubscribe(Subscription subscription)
    {
        Run(() =>
        {
            Subscribers.Remove(subscription);
            Reconcile();
        });
    }

    public static IReadOnlyList<GameDevice> ListDevices(out string? error)
    {
        error = EnsureStarted();
        if (error is not null)
        {
            return [];
        }

        IReadOnlyList<GameDevice> result = [];
        Run(() =>
        {
            Pump();
            result = Present.Values.Select(d => new GameDevice(d.Guid, d.Name)).ToArray();
        });
        return result;
    }

    private static string? EnsureStarted()
    {
        lock (StartLock)
        {
            if (_thread is null)
            {
                using var started = new ManualResetEventSlim();
                _thread = new Thread(() => Loop(started)) { IsBackground = true, Name = "PitVoice SDL joysticks" };
                _thread.Start();
                started.Wait();
            }

            return _startError;
        }
    }

    /// <summary>Runs <paramref name="action"/> on the SDL thread and waits for it (inline when already on it).</summary>
    private static void Run(Action action)
    {
        if (Thread.CurrentThread == _thread)
        {
            action();
            return;
        }

        // Not disposed: after a timeout the SDL thread may still set it.
        var done = new ManualResetEventSlim();
        Commands.Enqueue(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.Set();
            }
        });
        Wake.Set();
        done.Wait(CommandTimeout);
    }

    private static void Loop(ManualResetEventSlim started)
    {
        SDL3.SDL_SetHint(BackgroundEventsHint, "1");
        SDL3.SDL_SetHint(NoSignalHandlersHint, "1");
        if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_JOYSTICK))
        {
            _startError = $"game devices are not available: {SDL3.SDL_GetError()}";
            started.Set();
            return;
        }

        started.Set();
        while (true)
        {
            while (Commands.TryDequeue(out var command))
            {
                command();
            }

            if (Subscribers.Count > 0)
            {
                Pump();
                Wake.WaitOne(SdlJoystickSource.PollInterval);
            }
            else
            {
                Wake.WaitOne();
            }
        }
    }

    private static void Pump()
    {
        SDL_Event e;
        while (SDL3.SDL_PollEvent(&e))
        {
            Handle(e);
        }
    }

    private static void Handle(SDL_Event e)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
                Added(e.jdevice.which);
                break;
            case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
                Removed(e.jdevice.which);
                break;
            case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_UP:
                if (Present.TryGetValue(e.jbutton.which, out var device) && device.Handle != 0)
                {
                    var input = new PttInput(PttKey.JoystickButton(device.Guid, e.jbutton.button + 1), e.jbutton.down, device.Name);
                    foreach (var subscriber in Subscribers.Where(s => s.Wants(device.Guid)).ToArray())
                    {
                        subscriber.Input(input);
                    }
                }

                break;
        }
    }

    private static void Added(SDL_JoystickID id)
    {
        if (Present.ContainsKey(id))
        {
            return;
        }

        var guid = GuidToString(SDL3.SDL_GetJoystickGUIDForID(id));
        Present[id] = new Device(guid, SDL3.SDL_GetJoystickNameForID(id) ?? "game device", 0);
        if (Open(id) is { } device)
        {
            Notify(device, connected: true);
        }
    }

    private static void Removed(SDL_JoystickID id)
    {
        if (!Present.Remove(id, out var device))
        {
            return;
        }

        if (device.Handle != 0)
        {
            SDL3.SDL_CloseJoystick((SDL_Joystick*)device.Handle);
            Notify(device, connected: false);
        }
    }

    /// <summary>Opens the devices somebody wants and closes the ones nobody wants any more.</summary>
    private static void Reconcile()
    {
        foreach (var (id, device) in Present.ToArray())
        {
            var wanted = Subscribers.Any(s => s.Wants(device.Guid));
            if (wanted && device.Handle == 0)
            {
                Open(id);
            }
            else if (!wanted && device.Handle != 0)
            {
                SDL3.SDL_CloseJoystick((SDL_Joystick*)device.Handle);
                Present[id] = device with { Handle = 0 };
            }
        }
    }

    private static Device? Open(SDL_JoystickID id)
    {
        var device = Present[id];
        if (device.Handle != 0 || !Subscribers.Any(s => s.Wants(device.Guid)))
        {
            return null;
        }

        var handle = SDL3.SDL_OpenJoystick(id);
        if (handle is null)
        {
            return null;
        }

        device = device with { Handle = (nint)handle, Name = SDL3.SDL_GetJoystickName(handle) ?? device.Name };
        Present[id] = device;
        return device;
    }

    private static void Notify(Device device, bool connected)
    {
        var change = new PttDeviceChange(device.Guid, device.Name, connected);
        foreach (var subscriber in Subscribers.Where(s => s.Wants(device.Guid)).ToArray())
        {
            subscriber.Change(change);
        }
    }

    private static string GuidToString(SDL_GUID guid)
    {
        const int size = 33;
        var buffer = stackalloc byte[size];
        SDL3.SDL_GUIDToString(guid, buffer, size);
        return new string((sbyte*)buffer);
    }

    /// <summary>A listener: all devices, or only those of one model GUID.</summary>
    internal sealed class Subscription(string? onlyDevice, Action<PttInput> input, Action<PttDeviceChange> change)
    {
        public Action<PttInput> Input { get; } = input;

        public Action<PttDeviceChange> Change { get; } = change;

        public bool Wants(string guid) => onlyDevice is null || string.Equals(onlyDevice, guid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A connected device; <see cref="Handle"/> is 0 while it is closed.</summary>
    private sealed record Device(string Guid, string Name, nint Handle);
}
