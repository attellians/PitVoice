using System.Globalization;
using System.Runtime.Versioning;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Host;
using PitVoice.Input;
using PitVoice.Models;

namespace PitVoice.Cli;

/// <summary>
/// <c>run</c>: the console pipeline host. It is the same <see cref="AppHost"/> as the tray app, with events printed until
/// Ctrl+C; it refuses to start while the app or another <c>run</c> works (spec: Single pipeline host).
/// </summary>
internal static class HostRunner
{
    public const string KeyboardElevationHint =
        "iRacing runs as administrator while PitVoice does not: Windows hides keys pressed in the game window from the " +
        "keyboard hook and blocks pit commands. Start both normally (recommended); a wheel button PTT is not affected by the first part.";

    public const string AlreadyRunning =
        "PitVoice is already running (the tray app or another 'run'): one press would be recorded and executed twice. Close it first.";

    public static async Task<int> RunAsync(string? settingsPath, string packsDirectory, ModelStore models, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("run needs Windows (microphone, PTT and iRacing).");
            return 1;
        }

        var store = Settings.Open(settingsPath);
        using var platform = new WindowsHostPlatform(GameRunner.Game.ConsoleLog);
        var host = new AppHost(new AppHostOptions { PacksDirectory = packsDirectory, Models = models }, store, platform);
        try
        {
            return await HostAsync(host, platform, store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> HostAsync(AppHost host, WindowsHostPlatform platform, ISettingsStore store, CancellationToken cancellationToken)
    {
        host.SubscriberFailed += e => Console.Error.WriteLine($"event subscriber failed: {e.Message}");
        host.Events.Subscribe(ConsoleEvents.Print);
        host.PttDeviceChanged += change => Console.WriteLine(change.Connected
            ? $"{DateTime.Now:HH:mm:ss.fff}  PTT device connected: {change.Name}"
            : $"{DateTime.Now:HH:mm:ss.fff}  PTT device disconnected: {change.Name}; waiting for it to come back");

        if (await host.StartAsync(cancellationToken).ConfigureAwait(false) == HostStartResult.AlreadyRunning)
        {
            Console.Error.WriteLine(AlreadyRunning);
            return 1;
        }

        Settings.Print(store, host.SettingsWarnings);
        if (host.Additions is { } additions)
        {
            Packs.Print(additions, Console.Error);
        }

        var status = host.Status;
        var settings = host.Settings;
        switch (status.Reason)
        {
            case StatusReason.PackFailed:
                Console.Error.WriteLine(status.Detail);
                return 1;
            case StatusReason.ModelNotInstalled:
                Console.Error.WriteLine($"model not installed: {status.Detail}");
                Console.Error.WriteLine($"Download it with: pitvoice-cli models download {status.Detail}");
                return 1;
            case StatusReason.ModelFailed:
                Console.Error.WriteLine(status.Detail);
                return 1;
        }

        Console.WriteLine($"language:   {settings.Language}, model {status.Facts.ModelId}");
        Console.WriteLine($"PTT:        {settings.PttBinding?.Describe() ?? "not assigned"}, mode {settings.PttMode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"microphone: {settings.Microphone ?? DefaultName(platform.GetInputDevices())}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"cues:       {settings.OutputDevice ?? DefaultName(platform.GetOutputDevices())}, volume {settings.CueVolume}%"));
        Console.WriteLine($"game:       {GameRunner.Describe(status.Facts.Game)}");
        if (status.Facts.GameElevated)
        {
            Console.Error.WriteLine($"warning: {KeyboardElevationHint}");
        }

        foreach (var word in host.MissingAdditionWords)
        {
            Console.Error.WriteLine($"warning: word '{word}' of your additions is not in the model and cannot be recognized by voice");
        }

        if (settings.PttBinding is not { } binding)
        {
            Console.Error.WriteLine("PTT button is not assigned. Assign it with: pitvoice-cli ptt bind");
        }
        else if (host.Status.Reason == StatusReason.PttSourceFailed)
        {
            Console.Error.WriteLine($"error: {host.Status.Detail}");
        }
        else if (binding.Kind == PttDeviceKind.Joystick)
        {
            await Task.Delay(AppHost.DeviceCheckDelay + AppHost.DeviceCheckDelay, cancellationToken).ConfigureAwait(false);
            if (host.Status.Reason == StatusReason.PttDeviceDisconnected)
            {
                Console.Error.WriteLine($"PTT device '{binding.Name}' is not connected; waiting for it.");
            }
        }

        Console.WriteLine("Ready. Ctrl+C stops.");
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("Stopped.");
        return 0;
    }

    private static string DefaultName(IReadOnlyList<Core.Audio.AudioDevice> devices) =>
        devices.FirstOrDefault(d => d.IsDefault)?.Name is { } name ? $"{name} (default)" : "system default (none found)";
}

/// <summary>Opening and loading the settings file with the console messages every command shares.</summary>
internal static class Settings
{
    public static JsonSettingsStore Open(string? path) => new(path, SystemLanguage.Detect());

    public static AppSettings Load(ISettingsStore store)
    {
        var result = store.Load();
        Print(store, result.Warnings);
        return result.Settings;
    }

    public static void Print(ISettingsStore store, IReadOnlyList<string> warnings)
    {
        Console.WriteLine($"settings:   {store.Path}{(File.Exists(store.Path) ? string.Empty : " (not created yet, defaults)")}");
        foreach (var warning in warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }
    }
}
