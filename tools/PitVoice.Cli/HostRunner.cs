using System.Globalization;
using System.Runtime.Versioning;
using PitVoice.Core.Asr;
using PitVoice.Core.Events;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pipeline;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Responses;
using PitVoice.Core.Settings;
using PitVoice.Input;
using PitVoice.IRacing.Diagnostics;
using PitVoice.Models;
using PitVoice.Speech.Asr;
using PitVoice.Speech.Audio;
using PitVoice.Speech.Cues;

namespace PitVoice.Cli;

/// <summary>
/// <c>run</c>: the whole pipeline without a window (design decision 11). Settings come from the settings file; events are
/// printed until Ctrl+C. Outside a recording the microphone is closed and nothing polls.
/// </summary>
internal static class HostRunner
{
    public const string KeyboardElevationHint =
        "iRacing runs as administrator while PitVoice does not: Windows hides keys pressed in the game window from the " +
        "keyboard hook and blocks pit commands. Start both normally (recommended); a wheel button PTT is not affected by the first part.";

    private static readonly TimeSpan DeviceCheckDelay = TimeSpan.FromMilliseconds(500);

    public static async Task<int> RunAsync(string? settingsPath, string packsDirectory, ModelStore models, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("run needs Windows (microphone, PTT and iRacing).");
            return 1;
        }

        var settingsStore = Settings.Open(settingsPath);
        var settings = Settings.Load(settingsStore);

        var pack = Packs.LoadLanguage(packsDirectory, settings.Language, Console.Error);
        if (pack?.AsrModel is not { } modelId)
        {
            if (pack is not null)
            {
                Console.Error.WriteLine($"Language pack '{pack.Language}' names no asr_model.");
            }

            return 1;
        }

        using var engine = new VoskAsrEngine(GrammarBuilder.DefaultMode);
        var loaded = await engine.LoadAsync(pack, models.GetInstalledPath(modelId), cancellationToken).ConfigureAwait(false);
        if (!loaded.IsLoaded)
        {
            Console.Error.WriteLine(loaded.Error);
            if (!models.IsInstalled(modelId))
            {
                Console.Error.WriteLine($"Download it with: pitvoice-cli models download {modelId}");
            }

            return 1;
        }

        return await HostAsync(settings, pack.Language, modelId, engine, new CommandParser(pack), new ResponseBuilder(pack), cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> HostAsync(
        AppSettings settings,
        string language,
        string modelId,
        VoskAsrEngine engine,
        CommandParser parser,
        ResponseBuilder responses,
        CancellationToken cancellationToken)
    {
        using var capture = new NAudioCapture();
        using var cues = new NAudioCuePlayer(settings.OutputDevice, settings.CueVolume);
        using var telemetry = GameRunner.Game.OpenTelemetry();
        using var bus = new EventBus(e => Console.Error.WriteLine($"event subscriber failed: {e.Message}"));
        bus.Subscribe(ConsoleEvents.Print);

        Console.WriteLine($"language:   {language}, model {modelId}");
        Console.WriteLine($"PTT:        {settings.PttBinding?.Describe() ?? "not assigned"}, mode {settings.PttMode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"microphone: {settings.Microphone ?? DefaultName(capture.GetInputDevices())}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"cues:       {settings.OutputDevice ?? DefaultName(cues.GetOutputDevices())}, volume {settings.CueVolume}%"));
        Console.WriteLine($"game:       {GameRunner.Describe(telemetry.Source.Current.State)}");
        if (new WindowsGameElevationProbe().Check() == GameElevation.Elevated)
        {
            Console.Error.WriteLine($"warning: {KeyboardElevationHint}");
        }

        var executor = new PitExecutor(telemetry.Source, GameRunner.Game.OpenSink(), TimeProvider.System, GameRunner.Game.Log());
        var pipeline = new VoicePipeline(new VoicePipelineServices(
            capture,
            settings.Microphone,
            new SpeechRecognizer(engine),
            parser,
            responses,
            settings.ToParseOptions(),
            executor,
            cues,
            bus,
            TimeProvider.System));

        IPttSource? source = null;
        PttStateMachine? machine = null;
        try
        {
            if (settings.PttBinding is { } binding)
            {
                (source, machine) = StartPtt(binding, settings.PttMode, pipeline);
            }
            else
            {
                Console.Error.WriteLine("PTT button is not assigned. Assign it with: pitvoice-cli ptt bind");
            }

            Console.WriteLine("Ready. Ctrl+C stops.");
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            source?.Dispose();
            machine?.Dispose();
            await pipeline.DisposeAsync().ConfigureAwait(false);
            await bus.DrainAsync().ConfigureAwait(false);
        }

        Console.WriteLine("Stopped.");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static (IPttSource Source, PttStateMachine Machine) StartPtt(PttBinding binding, PttMode mode, VoicePipeline pipeline)
    {
        IPttSource source = binding.Kind == PttDeviceKind.Joystick ? new SdlJoystickSource(binding.Key.Device) : new KeyboardHookSource();
        var machine = new PttStateMachine(binding, mode);
        machine.Transition += pipeline.OnTransition;
        var deviceSeen = false;
        source.Input += machine.OnInput;
        source.DeviceChanged += change =>
        {
            machine.OnDeviceChanged(change);
            if (binding.IsDevice(change.Device))
            {
                deviceSeen |= change.Connected;
                Console.WriteLine(change.Connected
                    ? $"{DateTime.Now:HH:mm:ss.fff}  PTT device connected: {change.Name}"
                    : $"{DateTime.Now:HH:mm:ss.fff}  PTT device disconnected: {change.Name}; waiting for it to come back");
            }
        };

        if (source.Start() is { } error)
        {
            Console.Error.WriteLine($"error: {error}");
            return (source, machine);
        }

        if (binding.Kind == PttDeviceKind.Joystick)
        {
            Thread.Sleep(DeviceCheckDelay);
            if (!Volatile.Read(ref deviceSeen))
            {
                Console.Error.WriteLine($"PTT device '{binding.Name}' is not connected; waiting for it.");
            }
        }

        return (source, machine);
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
        Console.WriteLine($"settings:   {store.Path}{(File.Exists(store.Path) ? string.Empty : " (not created yet, defaults)")}");
        foreach (var warning in result.Warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        return result.Settings;
    }
}
