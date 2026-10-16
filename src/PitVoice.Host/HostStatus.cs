using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;

namespace PitVoice.Host;

/// <summary>State of the tray icon (FR-APP-2), by priority: no model, error, no game, ready.</summary>
public enum HostState
{
    Ready,
    NoGame,
    Error,
    NoModel,
}

/// <summary>Why the host is not ready; the window and the tray tooltip turn it into text.</summary>
public enum StatusReason
{
    None,

    /// <summary>The host is still loading the pack and the model at start.</summary>
    Starting,

    /// <summary>The model of the language is not installed; <see cref="HostStatus.Detail"/> is its id.</summary>
    ModelNotInstalled,

    /// <summary>The model is being loaded (start, language switch, download finished).</summary>
    ModelLoading,

    /// <summary>The model is installed but did not load; the detail is the error.</summary>
    ModelFailed,

    /// <summary>The language pack did not load; the detail is the error.</summary>
    PackFailed,

    PttNotAssigned,

    /// <summary>The bound game device is not connected; the detail is its name.</summary>
    PttDeviceDisconnected,

    /// <summary>The PTT source could not start (no SDL, hook failed); the detail is the error.</summary>
    PttSourceFailed,

    GameNotRunning,
}

/// <summary>What the host knows; <see cref="HostStatus.Evaluate"/> turns it into the icon state.</summary>
/// <param name="PackError">Why the pack of the language did not load, or null.</param>
/// <param name="ModelId">Model of the language, or null without a pack.</param>
/// <param name="ModelInstalled">True when the model is installed.</param>
/// <param name="ModelLoading">True while the model loads.</param>
/// <param name="ModelError">Why an installed model did not load, or null.</param>
/// <param name="Binding">PTT binding, or null.</param>
/// <param name="PttDeviceConnected">For a game device binding: whether the device is connected.</param>
/// <param name="PttSourceError">Why the PTT source did not start, or null.</param>
/// <param name="Game">Connection to iRacing.</param>
/// <param name="GameElevated">True when iRacing runs as administrator and PitVoice does not.</param>
/// <param name="Starting">True until the first session is built.</param>
public sealed record HostFacts(
    string? PackError,
    string? ModelId,
    bool ModelInstalled,
    bool ModelLoading,
    string? ModelError,
    PttBinding? Binding,
    bool PttDeviceConnected,
    string? PttSourceError,
    ConnectionState Game,
    bool GameElevated,
    bool Starting)
{
    public static HostFacts Initial { get; } = new(null, null, false, false, null, null, false, null, ConnectionState.NotRunning, false, true);
}

/// <summary>State of the icon with the reason, plus what the main page shows about the game and the button.</summary>
/// <param name="State">Icon state.</param>
/// <param name="Reason">Why, when not ready.</param>
/// <param name="Detail">Model id, device name or error text for the reason.</param>
/// <param name="Facts">Everything the state was computed from.</param>
public sealed record HostStatus(HostState State, StatusReason Reason, string? Detail, HostFacts Facts)
{
    /// <summary>Priority from the spec (Tray status): no model > error > no connection > ready.</summary>
    public static HostStatus Evaluate(HostFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Starting)
        {
            return new HostStatus(HostState.NoModel, StatusReason.Starting, null, facts);
        }

        if (facts.PackError is null && facts.ModelId is { } model)
        {
            if (!facts.ModelInstalled)
            {
                return new HostStatus(HostState.NoModel, StatusReason.ModelNotInstalled, model, facts);
            }

            if (facts.ModelLoading)
            {
                return new HostStatus(HostState.NoModel, StatusReason.ModelLoading, model, facts);
            }

            if (facts.ModelError is { } error)
            {
                return new HostStatus(HostState.NoModel, StatusReason.ModelFailed, error, facts);
            }
        }

        if (facts.PackError is { } packError)
        {
            return new HostStatus(HostState.Error, StatusReason.PackFailed, packError, facts);
        }

        if (facts.Binding is not { } binding)
        {
            return new HostStatus(HostState.Error, StatusReason.PttNotAssigned, null, facts);
        }

        if (facts.PttSourceError is { } sourceError)
        {
            return new HostStatus(HostState.Error, StatusReason.PttSourceFailed, sourceError, facts);
        }

        if (binding.Kind == PttDeviceKind.Joystick && !facts.PttDeviceConnected)
        {
            return new HostStatus(HostState.Error, StatusReason.PttDeviceDisconnected, binding.Name, facts);
        }

        return facts.Game == ConnectionState.NotRunning
            ? new HostStatus(HostState.NoGame, StatusReason.GameNotRunning, null, facts)
            : new HostStatus(HostState.Ready, StatusReason.None, null, facts);
    }
}
