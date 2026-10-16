using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Cues;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.IRacing.Diagnostics;

namespace PitVoice.Host;

/// <summary>
/// Devices, the recognizer and the game as the host sees them. The Windows implementation is
/// <see cref="WindowsHostPlatform"/>; tests pass fakes. Objects the host creates through it and that implement
/// <see cref="IDisposable"/> are disposed by the host.
/// </summary>
public interface IHostPlatform
{
    /// <summary>Microphone recording for the pipeline (the device is chosen per recording).</summary>
    IAudioCapture Capture { get; }

    /// <summary>A separate recording for the microphone check, so it never meets the pipeline's recording.</summary>
    IAudioCapture CreateCheckCapture();

    IReadOnlyList<AudioDevice> GetInputDevices();

    IReadOnlyList<AudioDevice> GetOutputDevices();

    IAudioCuePlayer CreateCuePlayer(string? deviceName, int volume);

    IAsrEngine CreateEngine();

    /// <summary>Source that listens only to the bound device (or the keyboard), to keep idle CPU low (TZ §12).</summary>
    IPttSource CreatePttSource(PttBinding binding);

    /// <summary>Every game device and the keyboard, for button assignment.</summary>
    IPttSource CreateAssignmentSource();

    ITelemetrySource Telemetry { get; }

    IPitCommandSink Sink { get; }

    IExecutionLog ExecutionLog { get; }

    /// <summary>Whether the game runs as administrator while PitVoice does not.</summary>
    GameElevation CheckElevation();
}
