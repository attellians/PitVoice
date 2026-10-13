namespace PitVoice.Core.Audio;

/// <summary>An input device as shown to the user. Devices are identified by name, not by index (FR-AUD-1).</summary>
public sealed record AudioDevice(string Name, bool IsDefault);

/// <summary>Lists microphones.</summary>
public interface IAudioDeviceCatalog
{
    IReadOnlyList<AudioDevice> GetInputDevices();
}

/// <summary>Result of <see cref="IAudioCapture.Start"/>.</summary>
/// <param name="Started">True when the device is open and recording.</param>
/// <param name="DeviceName">Device actually used.</param>
/// <param name="Warning">E.g. the requested device is missing and the default one is used.</param>
/// <param name="Error">Why recording did not start (no microphone, device cannot be opened).</param>
public sealed record CaptureStart(bool Started, string? DeviceName, string? Warning, string? Error)
{
    public static CaptureStart Ok(string deviceName, string? warning = null) => new(true, deviceName, warning, null);

    public static CaptureStart Failed(string error, string? deviceName = null) => new(false, deviceName, null, error);
}

/// <summary>Result of <see cref="IAudioCapture.Stop"/>: what was recorded and, if recording broke off, why.</summary>
public sealed record CaptureResult(AudioBuffer Buffer, string? Error)
{
    public static CaptureResult Nothing { get; } = new(AudioBuffer.Empty, null);
}

/// <summary>
/// Push-to-talk recording (FR-AUD-3): <see cref="Start"/> opens the device, <see cref="Stop"/> closes it and returns the
/// audio. Outside a recording the microphone is closed. Implementations never throw for device problems; they report
/// them in the results.
/// </summary>
public interface IAudioCapture
{
    bool IsRecording { get; }

    /// <param name="deviceName">Device to use; null or missing means the system default.</param>
    CaptureStart Start(string? deviceName = null);

    /// <summary>Stops and closes the device. Without a running recording it returns an empty result.</summary>
    CaptureResult Stop();
}
