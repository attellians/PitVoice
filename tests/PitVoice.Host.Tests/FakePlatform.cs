using System.Collections.Concurrent;
using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Cues;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Core.Tests.Pipeline;
using PitVoice.Core.Tests.Pit;
using PitVoice.Input;
using PitVoice.IRacing.Diagnostics;

namespace PitVoice.Host.Tests;

/// <summary>Devices, recognizer and game for <see cref="AppHost"/> tests.</summary>
internal sealed class FakePlatform : IHostPlatform
{
    public FakePlatform(TimeProvider time)
    {
        Game = new FakeTelemetrySource(time);
        Sink = new RecordingPitCommandSink(Game);
    }

    public FakeCapture PipelineCapture { get; } = new();

    public IAudioCapture Capture => PipelineCapture;

    /// <summary>Recognizer script shared by every engine the host creates.</summary>
    public ScriptedAsrEngine Script { get; } = new();

    public ConcurrentQueue<FakeEngine> Engines { get; } = new();

    /// <summary>When set, every engine load waits for it.</summary>
    public TaskCompletionSource? LoadGate { get; set; }

    public string? LoadError { get; set; }

    public ConcurrentQueue<(string? Device, int Volume, FakeCuePlayer Player)> CuePlayers { get; } = new();

    public FakeCuePlayer LastCues => CuePlayers.Last().Player;

    public ConcurrentQueue<FakePttSource> Sources { get; } = new();

    public FakePttSource LastSource => Sources.Last();

    /// <summary>Game devices connected now: reported by every new source at start.</summary>
    public List<(string Guid, string Name)> ConnectedDevices { get; } = [];

    public string? SourceError { get; set; }

    public List<AudioDevice> Microphones { get; } = [new("Desk Mic", true)];

    public List<AudioDevice> Outputs { get; } = [new("Speakers", true)];

    public FakeTelemetrySource Game { get; }

    public ITelemetrySource Telemetry => Game;

    public IPitCommandSink Sink { get; }

    public IExecutionLog ExecutionLog { get; set; } = new NullLog();

    public GameElevation Elevation { get; set; } = GameElevation.NotElevated;

    public IReadOnlyList<AudioDevice> GetInputDevices() => Microphones;

    public IReadOnlyList<AudioDevice> GetOutputDevices() => Outputs;

    /// <summary>When set, the game device list cannot be read.</summary>
    public string? GameDevicesError { get; set; }

    public IReadOnlyList<GameDevice> GetGameDevices(out string? error)
    {
        error = GameDevicesError;
        return error is null ? [.. ConnectedDevices.Select(d => new GameDevice(d.Guid, d.Name))] : [];
    }

    public IAudioCuePlayer CreateCuePlayer(string? deviceName, int volume)
    {
        var player = new FakeCuePlayer();
        CuePlayers.Enqueue((deviceName, volume, player));
        return player;
    }

    public IAsrEngine CreateEngine()
    {
        var engine = new FakeEngine(this);
        Engines.Enqueue(engine);
        return engine;
    }

    public IPttSource CreatePttSource(PttBinding binding)
    {
        var source = new FakePttSource(this, binding.Kind == PttDeviceKind.Joystick ? binding.Key.Device : null);
        Sources.Enqueue(source);
        return source;
    }

    public IPttSource CreateAssignmentSource()
    {
        var source = new FakePttSource(this, onlyDevice: null) { IsAssignment = true };
        Sources.Enqueue(source);
        return source;
    }

    public GameElevation CheckElevation() => Elevation;

    private sealed class NullLog : IExecutionLog
    {
        public void ApplyFailed(ApplyFailureReport report)
        {
        }

        public void Warning(string message)
        {
        }
    }
}

/// <summary>Microphone that remembers which device each recording asked for.</summary>
internal sealed class FakeCapture : IAudioCapture
{
    private readonly ConcurrentQueue<string?> _devices = new();

    public bool IsRecording { get; private set; }

    public string? StartError { get; set; }

    public IReadOnlyList<string?> Devices => [.. _devices];

    public int Stops { get; private set; }

    public CaptureStart Start(string? deviceName = null)
    {
        _devices.Enqueue(deviceName);
        if (StartError is not null)
        {
            return CaptureStart.Failed(StartError);
        }

        IsRecording = true;
        return CaptureStart.Ok(deviceName ?? "Desk Mic");
    }

    public CaptureResult Stop()
    {
        Stops++;
        IsRecording = false;
        return new CaptureResult(new AudioBuffer(new short[AudioBuffer.SampleRate]), null);
    }
}

/// <summary>Engine whose loading the test controls; recognition comes from <see cref="FakePlatform.Script"/>.</summary>
internal sealed class FakeEngine(FakePlatform platform) : IAsrEngine, IDisposable
{
    public bool IsLoaded { get; private set; }

    public string? Language { get; private set; }

    public bool Disposed { get; private set; }

    public async Task<AsrLoadResult> LoadAsync(LanguagePack pack, string? modelPath, CancellationToken cancellationToken = default)
    {
        if (platform.LoadGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (platform.LoadError is { } error)
        {
            return AsrLoadResult.Failed(error);
        }

        IsLoaded = true;
        Language = pack.Language;
        return AsrLoadResult.Loaded;
    }

    public RecognizedPhrase Recognize(AudioBuffer buffer) => platform.Script.Recognize(buffer);

    public void Unload()
    {
        IsLoaded = false;
        Language = null;
    }

    public void Dispose()
    {
        Unload();
        Disposed = true;
    }
}

/// <summary>PTT source the test presses; game devices of the platform are reported as connected at start.</summary>
internal sealed class FakePttSource(FakePlatform platform, string? onlyDevice) : IPttSource
{
    public event Action<PttInput>? Input;

    public event Action<PttDeviceChange>? DeviceChanged;

    public bool IsAssignment { get; init; }

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public string? Start()
    {
        Started = true;
        if (platform.SourceError is { } error)
        {
            return error;
        }

        foreach (var (guid, name) in platform.ConnectedDevices)
        {
            if (onlyDevice is null || string.Equals(onlyDevice, guid, StringComparison.OrdinalIgnoreCase))
            {
                DeviceChanged?.Invoke(new PttDeviceChange(guid, name, true));
            }
        }

        return null;
    }

    public void Press(PttKey key, string name = "F13") => Input?.Invoke(new PttInput(key, true, name));

    public void Release(PttKey key, string name = "F13") => Input?.Invoke(new PttInput(key, false, name));

    public void Device(string guid, string name, bool connected) => DeviceChanged?.Invoke(new PttDeviceChange(guid, name, connected));

    public void Dispose() => Disposed = true;
}

/// <summary>Settings in memory; can refuse to save like a read-only file.</summary>
internal sealed class MemorySettingsStore(AppSettings initial) : ISettingsStore
{
    private readonly ConcurrentQueue<AppSettings> _saved = new();

    public string Path => "memory://settings.json";

    public bool ReadOnly { get; set; }

    public AppSettings Current { get; private set; } = initial;

    public IReadOnlyList<AppSettings> Saved => [.. _saved];

    public SettingsLoadResult Load() => new(Current, [], null);

    public void Save(AppSettings settings)
    {
        if (ReadOnly)
        {
            throw new UnauthorizedAccessException("Access to the path 'settings.json' is denied.");
        }

        Current = settings;
        _saved.Enqueue(settings);
    }
}
