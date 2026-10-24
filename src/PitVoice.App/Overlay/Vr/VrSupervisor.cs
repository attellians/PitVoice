using System.Globalization;
using PitVoice.Core.Vr;

namespace PitVoice.App.Overlay.Vr;

/// <summary>State of the indicator in the headset, shown under the "VR mode" switch (add-vr-overlay D8).</summary>
internal enum VrState
{
    /// <summary>"VR mode" is off.</summary>
    Off,

    /// <summary>"VR mode" is on, "Show the overlay" is off.</summary>
    OverlayOff,

    /// <summary>SteamVR is not running, or the helper waits to be started again.</summary>
    WaitingSteamVr,

    /// <summary>The helper is started and has not said "ready" yet.</summary>
    Starting,

    /// <summary>The overlay exists in SteamVR.</summary>
    InHeadset,

    /// <summary>Three failures within 2 minutes; no more attempts until SteamVR restarts or the mode is switched.</summary>
    Failed,
}

/// <summary>Whether SteamVR runs: the id of the <c>vrserver</c> process, or null.</summary>
internal interface ISteamVrProbe
{
    int? ServerProcessId();
}

/// <summary>A running VR helper with redirected standard streams; disposing closes its input.</summary>
internal interface IVrHelperProcess : IDisposable
{
    /// <summary>Queues a message for the helper; never waits for it.</summary>
    void Send(VrMessage message);

    /// <summary>Kills the helper at once.</summary>
    void Kill();
}

internal interface IVrHelperLauncher
{
    /// <summary>Starts the helper; throws when it cannot be started.</summary>
    /// <param name="line">Gets every line of its standard output, on a background thread.</param>
    /// <param name="exited">Gets its exit code once, on a background thread, after the last line.</param>
    IVrHelperProcess Start(Action<string> line, Action<int> exited);
}

/// <summary>
/// Starts the VR helper only while SteamVR runs and watches it (add-vr-overlay D3, D4, D8): <c>vrserver</c> is looked for
/// every 2 s; "ready" within 10 s and "pong" within 5 s of the pings sent every second are expected. A failure kills the
/// helper and tries again after 2 s; three failures within 2 minutes stop the attempts until <c>vrserver</c> gets a new
/// process id. The latest place, frame and visibility are kept and sent again to every new helper.
/// </summary>
internal sealed class VrSupervisor : IVrLink, IDisposable
{
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan PongTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(2);
    public const int MaxFailures = 3;

    /// <summary>Time the helper gets to exit by itself after an "error" line.</summary>
    private static readonly TimeSpan ErrorGrace = TimeSpan.FromSeconds(1);

    private readonly IVrHelperLauncher _launcher;
    private readonly ISteamVrProbe _probe;
    private readonly TimeProvider _time;
    private readonly Action<string> _log;
    private readonly Lock _gate = new();
    private readonly List<DateTimeOffset> _failures = [];
    private ITimer? _probeTimer;
    private ITimer? _pingTimer;
    private Session? _session;
    private VrState _state = VrState.WaitingSteamVr;
    private DateTimeOffset _retryAt;
    private int? _failedPid;
    private int? _lastPid;
    private VrPlace? _place;
    private VrFrame? _frame;
    private VrVisible? _visible;
    private bool _disposed;

    /// <param name="launcher">Starts the helper.</param>
    /// <param name="probe">Looks for SteamVR.</param>
    /// <param name="time">Timers and the clock.</param>
    /// <param name="log">Writes a line to the application log; never gets phrase contents.</param>
    public VrSupervisor(IVrHelperLauncher launcher, ISteamVrProbe probe, TimeProvider time, Action<string> log)
    {
        _launcher = launcher;
        _probe = probe;
        _time = time;
        _log = log;
    }

    /// <summary>Raised on a background thread or the caller's thread when <see cref="State"/> changes.</summary>
    public event Action<VrState>? StateChanged;

    public VrState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Looks for SteamVR at once and then every <see cref="ProbeInterval"/>.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _probeTimer ??= _time.CreateTimer(_ => Probe(), null, TimeSpan.Zero, ProbeInterval);
            _pingTimer ??= _time.CreateTimer(_ => Tick(), null, PingInterval, PingInterval);
        }
    }

    public void Send(VrMessage message)
    {
        lock (_gate)
        {
            switch (message)
            {
                case VrPlace place:
                    _place = place;
                    break;
                case VrFrame frame:
                    _frame = frame;
                    break;
                case VrVisible visible:
                    _visible = visible;
                    break;
            }

            if (_state == VrState.InHeadset)
            {
                _session?.Process.Send(message);
            }
        }
    }

    /// <summary>Stops looking and ends the helper; closing its input lets it remove the overlay itself.</summary>
    public void Dispose()
    {
        Session? session;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _probeTimer?.Dispose();
            _pingTimer?.Dispose();
            session = _session;
            _session = null;
        }

        session?.Process.Dispose();
    }

    private void Probe()
    {
        Action? raise = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var pid = _probe.ServerProcessId();
            _lastPid = pid;
            if (_state == VrState.Failed)
            {
                if (pid is null || pid == _failedPid)
                {
                    return;
                }

                _log(string.Create(CultureInfo.InvariantCulture, $"vr: SteamVR restarted (vrserver {pid}), trying again"));
                _failures.Clear();
                _failedPid = null;
                raise = SetState(VrState.WaitingSteamVr);
            }

            if (_state == VrState.WaitingSteamVr && pid is not null && _time.GetUtcNow() >= _retryAt)
            {
                raise += Launch();
            }
        }

        raise?.Invoke();
    }

    private void Tick()
    {
        Action? raise = null;
        lock (_gate)
        {
            if (_disposed || _session is not { } session)
            {
                return;
            }

            var now = _time.GetUtcNow();
            if (session.Error is not null && now - session.ErrorAt >= ErrorGrace)
            {
                raise = Fail(session, "said error " + session.Error);
            }
            else if (_state == VrState.Starting && now - session.StartedAt >= ReadyTimeout)
            {
                raise = Fail(session, "no ready within 10 s");
            }
            else if (_state == VrState.InHeadset && now - session.LastPong >= PongTimeout)
            {
                raise = Fail(session, "no pong for 5 s");
            }
            else if (_state == VrState.InHeadset)
            {
                session.Process.Send(new VrPing());
            }
        }

        raise?.Invoke();
    }

    /// <summary>Called under the lock.</summary>
    private Action? Launch()
    {
        // The callbacks wait for the lock, so they see the session filled in.
        var session = new Session(_time.GetUtcNow());
        try
        {
            session.Process = _launcher.Start(line => OnLine(session, line), code => OnExited(session, code));
        }
#pragma warning disable CA1031 // Any start failure is a failure of the helper, not of the app (D8).
        catch (Exception e)
#pragma warning restore CA1031
        {
            return Fail(null, "could not start the helper: " + e.Message);
        }

        _session = session;
        return SetState(VrState.Starting);
    }

    private void OnLine(Session session, string line)
    {
        Action? raise = null;
        lock (_gate)
        {
            if (_disposed || session != _session)
            {
                return;
            }

            var reply = VrReply.Parse(line);
            switch (reply.Kind)
            {
                case VrReplyKind.Ready when _state == VrState.Starting:
                    session.LastPong = _time.GetUtcNow();
                    foreach (var message in new VrMessage?[] { _place, _frame, _visible })
                    {
                        if (message is not null)
                        {
                            session.Process.Send(message);
                        }
                    }

                    raise = SetState(VrState.InHeadset);
                    break;
                case VrReplyKind.Pong:
                    session.LastPong = _time.GetUtcNow();
                    break;
                case VrReplyKind.Error:
                    session.Error ??= reply.Code;
                    session.ErrorAt = _time.GetUtcNow();
                    break;
                case VrReplyKind.Quit:
                    session.Quitting = true;
                    break;
            }
        }

        raise?.Invoke();
    }

    private void OnExited(Session session, int code)
    {
        Action? raise = null;
        lock (_gate)
        {
            if (_disposed || session != _session)
            {
                return;
            }

            // After "quit" SteamVR may end the helper itself before it exits (D4): still the end of SteamVR, not a failure.
            if (code == VrExitCode.SteamVrClosed || session.Quitting)
            {
                _session = null;
                session.Process.Dispose();
                _log("vr: SteamVR closed, waiting for its next start");
                raise = SetState(VrState.WaitingSteamVr);
            }
            else
            {
                raise = Fail(session, string.Create(CultureInfo.InvariantCulture, $"exited with code {code}"));
            }
        }

        raise?.Invoke();
    }

    /// <summary>Called under the lock: logs, ends the helper, counts the failure and decides what comes next.</summary>
    private Action? Fail(Session? session, string reason)
    {
        var now = _time.GetUtcNow();
        if (session is not null)
        {
            _session = null;
            var error = session.Error is null ? string.Empty : ", error " + session.Error;
            _log(string.Create(CultureInfo.InvariantCulture, $"vr: helper failed: {reason}{error}, ran {(now - session.StartedAt).TotalSeconds:0.0} s"));
            session.Process.Kill();
            session.Process.Dispose();
        }
        else
        {
            _log("vr: " + reason);
        }

        _failures.RemoveAll(t => now - t >= FailureWindow);
        _failures.Add(now);
        if (_failures.Count >= MaxFailures)
        {
            _failedPid = _lastPid;
            _log(string.Create(CultureInfo.InvariantCulture, $"vr: {_failures.Count} failures within 2 minutes, no more attempts until SteamVR restarts"));
            return SetState(VrState.Failed);
        }

        _retryAt = now + RetryDelay;
        _probeTimer?.Change(RetryDelay, ProbeInterval);
        return SetState(VrState.WaitingSteamVr);
    }

    /// <summary>Called under the lock; the returned action raises the event outside of it.</summary>
    private Action? SetState(VrState state)
    {
        if (state == _state)
        {
            return null;
        }

        _state = state;
        var handler = StateChanged;
        return handler is null ? null : () => handler(state);
    }

    private sealed class Session(DateTimeOffset startedAt)
    {
        public IVrHelperProcess Process { get; set; } = null!;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public DateTimeOffset LastPong { get; set; } = startedAt;

        public string? Error { get; set; }

        public DateTimeOffset ErrorAt { get; set; }

        public bool Quitting { get; set; }
    }
}
