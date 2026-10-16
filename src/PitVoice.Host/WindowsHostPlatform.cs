using System.Runtime.Versioning;
using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Cues;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Input;
using PitVoice.IRacing.Commands;
using PitVoice.IRacing.Diagnostics;
using PitVoice.IRacing.Telemetry;
using PitVoice.Speech.Asr;
using PitVoice.Speech.Audio;
using PitVoice.Speech.Cues;

namespace PitVoice.Host;

/// <summary>The real devices: NAudio, Vosk, SDL3 and the keyboard hook, iRacing shared memory and broadcast messages.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHostPlatform : IHostPlatform, IDisposable
{
    private readonly NAudioCapture _capture = new();
    private readonly IRacingTelemetrySource _telemetry = new();
    private readonly WindowsGameElevationProbe _elevation = new();
    private readonly Lazy<NAudioCuePlayer> _outputs = new(() => new NAudioCuePlayer(null, 0));

    /// <param name="log">Where execution problems go (the console for <c>run</c>); the elevation hint is added to it.</param>
    public WindowsHostPlatform(IExecutionLog? log = null)
    {
        ExecutionLog = new ElevationHintLog(log ?? NullExecutionLog.Instance, _elevation);
    }

    public IAudioCapture Capture => _capture;

    public ITelemetrySource Telemetry => _telemetry;

    public IPitCommandSink Sink { get; } = new BroadcastPitCommandSink();

    public IExecutionLog ExecutionLog { get; }

    public IAudioCapture CreateCheckCapture() => new NAudioCapture();

    public IReadOnlyList<AudioDevice> GetInputDevices() => _capture.GetInputDevices();

    public IReadOnlyList<AudioDevice> GetOutputDevices() => _outputs.Value.GetOutputDevices();

    public IAudioCuePlayer CreateCuePlayer(string? deviceName, int volume) => new NAudioCuePlayer(deviceName, volume);

    public IAsrEngine CreateEngine() => new VoskAsrEngine(GrammarBuilder.DefaultMode);

    public IPttSource CreatePttSource(PttBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Kind == PttDeviceKind.Joystick ? new SdlJoystickSource(binding.Key.Device) : new KeyboardHookSource();
    }

    public IPttSource CreateAssignmentSource() => new AllPttSources();

    public GameElevation CheckElevation() => _elevation.Check();

    public void Dispose()
    {
        _capture.Dispose();
        _telemetry.Dispose();
        if (_outputs.IsValueCreated)
        {
            _outputs.Value.Dispose();
        }
    }

    private sealed class NullExecutionLog : IExecutionLog
    {
        public static NullExecutionLog Instance { get; } = new();

        public void ApplyFailed(ApplyFailureReport report)
        {
        }

        public void Warning(string message)
        {
        }
    }
}
