using NAudio.Wave;
using PitVoice.Core.Audio;
using AudioBuffer = PitVoice.Core.Audio.AudioBuffer;

namespace PitVoice.Speech.Audio;

/// <summary>Receives one chunk of captured audio; the span is valid only during the call.</summary>
internal delegate void CaptureDataHandler(ReadOnlySpan<byte> data);

/// <summary>An open input device delivering audio chunks in its native format.</summary>
internal interface ICaptureStream : IDisposable
{
    WaveFormat Format { get; }

    /// <summary>Raised on the capture thread for every chunk.</summary>
    event CaptureDataHandler? DataAvailable;

    /// <summary>Raised once when the stream ends; the exception is set when the device failed or disappeared.</summary>
    event Action<Exception?>? Stopped;

    void Start();

    void Stop();
}

/// <summary>Platform side of recording: lists devices and opens one by name.</summary>
internal interface ICaptureBackend
{
    IReadOnlyList<AudioDevice> GetInputDevices();

    /// <summary>Opens a device. Throws when the device cannot be opened.</summary>
    ICaptureStream Open(AudioDevice device);
}

/// <summary>
/// Push-to-talk recorder (design D3): <see cref="Start"/> opens the device, chunks are converted to 16 kHz mono 16-bit
/// as they arrive and kept in memory up to <see cref="AudioBuffer.MaxDuration"/>, <see cref="Stop"/> closes the device.
/// Device problems are reported in results, never thrown.
/// </summary>
internal sealed class AudioRecorder : IAudioCapture, IAudioDeviceCatalog, IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly ICaptureBackend _backend;
    private readonly Lock _lock = new();
    private ICaptureStream? _stream;
    private Pcm16Converter? _converter;
    private ManualResetEventSlim? _stopped;
    private Exception? _failure;
    private string? _deviceName;

    public AudioRecorder(ICaptureBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _stream is not null;
            }
        }
    }

    public IReadOnlyList<AudioDevice> GetInputDevices() => _backend.GetInputDevices();

    public CaptureStart Start(string? deviceName = null)
    {
        lock (_lock)
        {
            if (_stream is not null)
            {
                return CaptureStart.Failed("recording is already running", _deviceName);
            }
        }

        IReadOnlyList<AudioDevice> devices;
        try
        {
            devices = _backend.GetInputDevices();
        }
#pragma warning disable CA1031 // Device enumeration errors are reported, the app keeps running.
        catch (Exception e)
#pragma warning restore CA1031
        {
            return CaptureStart.Failed($"cannot list input devices: {e.Message}");
        }

        if (devices.Count == 0)
        {
            return CaptureStart.Failed("no microphone: the system has no audio input devices");
        }

        string? warning = null;
        var device = deviceName is null ? null : devices.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.Ordinal));
        if (device is null)
        {
            if (deviceName is not null)
            {
                warning = $"microphone '{deviceName}' not found, using the system default";
            }

            device = devices.FirstOrDefault(d => d.IsDefault) ?? devices[0];
        }

        ICaptureStream? stream = null;
        try
        {
            stream = _backend.Open(device);
            var converter = new Pcm16Converter(stream.Format, AudioBuffer.MaxSamples);
            var stopped = new ManualResetEventSlim(false);
            lock (_lock)
            {
                _stream = stream;
                _converter = converter;
                _stopped = stopped;
                _failure = null;
                _deviceName = device.Name;
            }

            stream.DataAvailable += OnData;
            stream.Stopped += OnStopped;
            stream.Start();
            return CaptureStart.Ok(device.Name, warning);
        }
#pragma warning disable CA1031 // Any failure to open the device is reported; the device must not stay open.
        catch (Exception e)
#pragma warning restore CA1031
        {
            lock (_lock)
            {
                _stream = null;
                _converter = null;
                _stopped?.Dispose();
                _stopped = null;
            }

            stream?.Dispose();
            return CaptureStart.Failed($"cannot open microphone '{device.Name}': {e.Message}", device.Name);
        }
    }

    public CaptureResult Stop()
    {
        ICaptureStream? stream;
        ManualResetEventSlim? stopped;
        lock (_lock)
        {
            stream = _stream;
            stopped = _stopped;
            if (stream is null)
            {
                return CaptureResult.Nothing;
            }
        }

        try
        {
            stream.Stop();
            stopped!.Wait(StopTimeout);
        }
#pragma warning disable CA1031 // A device that fails while stopping still yields what was recorded.
        catch (Exception e)
#pragma warning restore CA1031
        {
            lock (_lock)
            {
                _failure ??= e;
            }
        }
        finally
        {
            stream.DataAvailable -= OnData;
            stream.Stopped -= OnStopped;
            stream.Dispose();
        }

        lock (_lock)
        {
            var buffer = _converter!.Complete();
            var error = _failure is null ? null : $"microphone '{_deviceName}' disconnected or failed: {_failure.Message}";
            _stream = null;
            _converter = null;
            _stopped = null;
            _failure = null;
            stopped!.Dispose();
            return new CaptureResult(buffer, error);
        }
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            Stop();
        }
    }

    private void OnData(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            _converter?.Write(data);
        }
    }

    private void OnStopped(Exception? error)
    {
        lock (_lock)
        {
            if (error is not null)
            {
                _failure ??= error;
            }

            _stopped?.Set();
        }
    }
}
