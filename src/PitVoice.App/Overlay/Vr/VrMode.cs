using PitVoice.App.ViewModels;
using PitVoice.Core.Settings;

namespace PitVoice.App.Overlay.Vr;

/// <summary>
/// "VR mode" (add-vr-overlay D1, D9): the supervisor exists only while both "VR mode" and "Show the overlay" are on.
/// Without it nothing of VR runs: no SteamVR checks, no helper, no OpenVR library.
/// </summary>
internal sealed class VrMode : IVrStatus, IDisposable
{
    private readonly Func<VrSupervisor> _create;
    private readonly Action<VrSupervisor?> _attached;
    private VrSupervisor? _supervisor;
    private VrState _mode = VrState.Off;
    private bool _disposed;

    /// <param name="create">Makes a supervisor when the mode turns on.</param>
    /// <param name="attached">Gets the new supervisor, or null when it goes away; called on the caller's thread.</param>
    public VrMode(Func<VrSupervisor> create, Action<VrSupervisor?> attached)
    {
        _create = create;
        _attached = attached;
    }

    /// <summary>Raised when <see cref="State"/> may have changed, possibly on a background thread.</summary>
    public event Action? Changed;

    public VrState State => _supervisor?.State ?? _mode;

    /// <summary>Follows the indicator settings at once (spec: VR mode setting); call on the UI thread.</summary>
    public void Apply(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_disposed)
        {
            return;
        }

        var mode = !settings.Vr ? VrState.Off : !settings.Enabled ? VrState.OverlayOff : VrState.WaitingSteamVr;
        var active = mode == VrState.WaitingSteamVr;
        if (active && _supervisor is null)
        {
            _supervisor = _create();
            _supervisor.StateChanged += OnStateChanged;
            _attached(_supervisor);
            _supervisor.Start();
        }
        else if (!active && _supervisor is not null)
        {
            Stop();
        }

        if (mode != _mode)
        {
            _mode = mode;
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void Stop()
    {
        if (_supervisor is not { } supervisor)
        {
            return;
        }

        _supervisor = null;
        supervisor.StateChanged -= OnStateChanged;
        _attached(null);
        supervisor.Dispose();
    }

    private void OnStateChanged(VrState state) => Changed?.Invoke();
}
