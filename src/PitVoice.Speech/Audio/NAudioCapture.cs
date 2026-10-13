using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PitVoice.Core.Audio;

namespace PitVoice.Speech.Audio;

/// <summary>
/// Microphone recording on Windows through WASAPI shared mode: the device is opened in its native mix format and the
/// audio is converted inside the application (design D2). Open only between <see cref="Start"/> and <see cref="Stop"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NAudioCapture : IAudioCapture, IAudioDeviceCatalog, IDisposable
{
    private readonly AudioRecorder _recorder = new(new WasapiBackend());

    public bool IsRecording => _recorder.IsRecording;

    public IReadOnlyList<AudioDevice> GetInputDevices() => _recorder.GetInputDevices();

    public CaptureStart Start(string? deviceName = null) => _recorder.Start(deviceName);

    public CaptureResult Stop() => _recorder.Stop();

    public void Dispose() => _recorder.Dispose();

    [SupportedOSPlatform("windows")]
    private sealed class WasapiBackend : ICaptureBackend
    {
        public IReadOnlyList<AudioDevice> GetInputDevices()
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            if (enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out var defaultDevice))
            {
                using (defaultDevice)
                {
                    defaultId = defaultDevice.ID;
                }
            }

            var result = new List<AudioDevice>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    result.Add(new AudioDevice(device.FriendlyName, string.Equals(device.ID, defaultId, StringComparison.Ordinal)));
                }
            }

            return result;
        }

        public ICaptureStream Open(AudioDevice device)
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var candidate in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (string.Equals(candidate.FriendlyName, device.Name, StringComparison.Ordinal))
                {
                    return new WasapiStream(candidate);
                }

                candidate.Dispose();
            }

            throw new InvalidOperationException($"device '{device.Name}' is no longer available");
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class WasapiStream : ICaptureStream
    {
        private readonly MMDevice _device;
        private readonly WasapiRecorder _recorder;
        private byte[] _silence = [];

        public WasapiStream(MMDevice device)
        {
            _device = device;
            try
            {
                _recorder = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().Build();
            }
            catch
            {
                device.Dispose();
                throw;
            }

            _recorder.DataAvailable += OnData;
            _recorder.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        }

        public event CaptureDataHandler? DataAvailable;

        public event Action<Exception?>? Stopped;

        public WaveFormat Format => _recorder.WaveFormat;

        public void Start() => _recorder.StartRecording();

        public void Stop() => _recorder.StopRecording();

        public void Dispose()
        {
            _recorder.Dispose();
            _device.Dispose();
        }

        private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            if ((flags & AudioClientBufferFlags.Silent) == 0)
            {
                DataAvailable?.Invoke(buffer);
                return;
            }

            // The driver marks the packet as silence; its bytes must be treated as zeros.
            if (_silence.Length < buffer.Length)
            {
                _silence = new byte[buffer.Length];
            }

            DataAvailable?.Invoke(_silence.AsSpan(0, buffer.Length));
        }
    }
}
