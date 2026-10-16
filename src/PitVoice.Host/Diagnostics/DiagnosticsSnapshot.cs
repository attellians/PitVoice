using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Core.Pit;
using PitVoice.Core.Settings;
using PitVoice.Input;
using PitVoice.Models;

namespace PitVoice.Host.Diagnostics;

/// <summary>A device list for the summary, or why it could not be read.</summary>
public sealed record DeviceList<T>(IReadOnlyList<T> Items, string? Error)
{
    public static DeviceList<T> Read(Func<IReadOnlyList<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            return new DeviceList<T>(read(), null);
        }
#pragma warning disable CA1031 // Spec: Device enumeration fails — the error goes to the summary instead of the list.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return new DeviceList<T>([], e.Message);
        }
    }
}

/// <summary>
/// What the caller knows for the summary (design decision 6). The recent phrases of the window are not part of it.
/// </summary>
public sealed record DiagnosticsSnapshot
{
    /// <summary>Said when the snapshot comes from <c>pitvoice-cli diagnostics collect</c>.</summary>
    public const string NoHostInProcess = "the host is not running in this process";

    /// <summary>State like the tray icon's, or <see cref="NoHostInProcess"/>.</summary>
    public required string Host { get; init; }

    public required string CommandLanguage { get; init; }

    public required string InterfaceLanguage { get; init; }

    /// <summary>Model of the command language, or null when no pack names one.</summary>
    public string? ModelId { get; init; }

    public bool ModelInstalled { get; init; }

    public required ConnectionState Game { get; init; }

    /// <summary>The PTT binding as text, or null when not assigned.</summary>
    public string? Ptt { get; init; }

    public required DeviceList<AudioDevice> Microphones { get; init; }

    public required DeviceList<AudioDevice> Outputs { get; init; }

    public required DeviceList<GameDevice> GameDevices { get; init; }

    /// <summary>The running host of the tray app.</summary>
    public static DiagnosticsSnapshot FromHost(AppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var status = host.Status;
        var settings = host.Settings;
        return new DiagnosticsSnapshot
        {
            Host = Describe(status),
            CommandLanguage = settings.Language,
            InterfaceLanguage = settings.EffectiveInterfaceLanguage,
            ModelId = status.Facts.ModelId,
            ModelInstalled = status.Facts.ModelInstalled,
            Game = status.Facts.Game,
            Ptt = settings.PttBinding?.Describe(),
            Microphones = DeviceList<AudioDevice>.Read(host.Platform.GetInputDevices),
            Outputs = DeviceList<AudioDevice>.Read(host.Platform.GetOutputDevices),
            GameDevices = ReadGameDevices(host.Platform),
        };
    }

    /// <summary>No host in this process (the CLI): the settings file, the packs, the model store and the game as they are.</summary>
    public static DiagnosticsSnapshot WithoutHost(AppSettings settings, string packsDirectory, ModelStore models, ITelemetrySource telemetry, IHostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(platform);
        var modelId = LanguagePackLoader.LoadDirectory(packsDirectory)
            .FirstOrDefault(r => r.Pack?.Language == settings.Language)?.Pack?.AsrModel;
        ConnectionState game;
        try
        {
            game = telemetry.Current.State;
        }
#pragma warning disable CA1031 // The summary must be written whatever the game does.
        catch (Exception)
#pragma warning restore CA1031
        {
            game = ConnectionState.NotRunning;
        }

        return new DiagnosticsSnapshot
        {
            Host = NoHostInProcess,
            CommandLanguage = settings.Language,
            InterfaceLanguage = settings.EffectiveInterfaceLanguage,
            ModelId = modelId,
            ModelInstalled = modelId is not null && models.GetInstalledPath(modelId) is not null,
            Game = game,
            Ptt = settings.PttBinding?.Describe(),
            Microphones = DeviceList<AudioDevice>.Read(platform.GetInputDevices),
            Outputs = DeviceList<AudioDevice>.Read(platform.GetOutputDevices),
            GameDevices = ReadGameDevices(platform),
        };
    }

    public static string Describe(HostStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var text = status.State.ToString();
        if (status.Reason != StatusReason.None)
        {
            text += $": {status.Reason}";
        }

        return status.Detail is { Length: > 0 } detail ? $"{text} ({detail})" : text;
    }

    private static DeviceList<GameDevice> ReadGameDevices(IHostPlatform platform)
    {
        var result = DeviceList<GameDevice>.Read(() =>
        {
            var devices = platform.GetGameDevices(out var error);
            return error is null ? devices : throw new InvalidOperationException(error);
        });
        return result;
    }
}
