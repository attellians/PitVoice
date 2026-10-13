using NAudio.Wave;
using PitVoice.Core.Audio;
using PitVoice.Speech.Audio;
using Xunit;
using AudioBuffer = PitVoice.Core.Audio.AudioBuffer;

namespace PitVoice.Speech.Tests.Audio;

public sealed class AudioRecorderTests
{
    private static readonly WaveFormat DeviceFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    [Fact]
    public void Capture_StartStop_OpensOnlyWhileRecordingAndConverts()
    {
        var backend = new FakeBackend(new AudioDevice("Headset", true));
        using var recorder = new AudioRecorder(backend);

        var start = recorder.Start("Headset");
        Assert.True(start.Started);
        Assert.Equal("Headset", start.DeviceName);
        Assert.True(backend.Stream!.IsOpen);

        backend.Stream.Push(Signals.Sine(DeviceFormat, 440, TimeSpan.FromSeconds(2)));
        var result = recorder.Stop();

        Assert.Null(result.Error);
        Assert.InRange(result.Buffer.Duration, TimeSpan.FromMilliseconds(1950), TimeSpan.FromMilliseconds(2050));
        Assert.InRange(result.Buffer.Samples.Count, 31200, 32800);
        Assert.False(backend.Stream.IsOpen);
        Assert.True(backend.Stream.Disposed);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void Capture_StopWithoutStart_NothingAndNoError()
    {
        using var recorder = new AudioRecorder(new FakeBackend(new AudioDevice("Mic", true)));

        var result = recorder.Stop();

        Assert.Null(result.Error);
        Assert.True(result.Buffer.IsEmpty);
    }

    [Fact]
    public void Capture_StopBeforeFirstChunk_EmptyBuffer()
    {
        var backend = new FakeBackend(new AudioDevice("Mic", true));
        using var recorder = new AudioRecorder(backend);

        recorder.Start();
        var result = recorder.Stop();

        Assert.True(result.Buffer.IsEmpty);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Capture_SelectedDeviceMissing_DefaultWithWarning()
    {
        var backend = new FakeBackend(new AudioDevice("USB Mic", false), new AudioDevice("Realtek", true));
        using var recorder = new AudioRecorder(backend);

        var start = recorder.Start("Old Headset");

        Assert.True(start.Started);
        Assert.Equal("Realtek", start.DeviceName);
        Assert.Contains("Old Headset", start.Warning, StringComparison.Ordinal);
        recorder.Stop();
    }

    [Fact]
    public void Capture_NamedDevice_IsUsedEvenIfNotDefault()
    {
        var backend = new FakeBackend(new AudioDevice("USB Mic", false), new AudioDevice("Realtek", true));
        using var recorder = new AudioRecorder(backend);

        var start = recorder.Start("USB Mic");

        Assert.Equal("USB Mic", start.DeviceName);
        Assert.Null(start.Warning);
        recorder.Stop();
    }

    [Fact]
    public void Capture_NoDevices_NoMicrophoneError()
    {
        using var recorder = new AudioRecorder(new FakeBackend());

        var start = recorder.Start();

        Assert.False(start.Started);
        Assert.Contains("no microphone", start.Error, StringComparison.Ordinal);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void Capture_DeviceCannotOpen_ErrorWithNameAndNothingOpen()
    {
        var backend = new FakeBackend(new AudioDevice("Broken", true)) { OpenError = new InvalidOperationException("format not supported") };
        using var recorder = new AudioRecorder(backend);

        var start = recorder.Start();

        Assert.False(start.Started);
        Assert.Contains("Broken", start.Error, StringComparison.Ordinal);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void Capture_UnsupportedDeviceFormat_ErrorAndStreamDisposed()
    {
        var backend = new FakeBackend(new AudioDevice("Odd", true)) { Format = WaveFormat.CreateALawFormat(8000, 1) };
        using var recorder = new AudioRecorder(backend);

        var start = recorder.Start();

        Assert.False(start.Started);
        Assert.Contains("Odd", start.Error, StringComparison.Ordinal);
        Assert.True(backend.Stream!.Disposed);
    }

    [Fact]
    public void Capture_DeviceDisappears_PartialBufferAndError()
    {
        var backend = new FakeBackend(new AudioDevice("Headset", true));
        using var recorder = new AudioRecorder(backend);
        recorder.Start();
        backend.Stream!.Push(Signals.Sine(DeviceFormat, 440, TimeSpan.FromSeconds(1)));

        backend.Stream.Fail(new IOException("device removed"));
        var result = recorder.Stop();

        Assert.Contains("disconnected", result.Error, StringComparison.Ordinal);
        Assert.InRange(result.Buffer.Duration, TimeSpan.FromMilliseconds(950), TimeSpan.FromMilliseconds(1050));
        Assert.True(backend.Stream.Disposed);

        // The recorder keeps working after the failure.
        Assert.True(recorder.Start().Started);
        Assert.Null(recorder.Stop().Error);
    }

    [Fact]
    public void Capture_TwelveSeconds_TruncatedToEight()
    {
        var backend = new FakeBackend(new AudioDevice("Headset", true));
        using var recorder = new AudioRecorder(backend);
        recorder.Start();
        var second = Signals.Sine(DeviceFormat, 440, TimeSpan.FromSeconds(1));

        for (var i = 0; i < 12; i++)
        {
            backend.Stream!.Push(second);
        }

        var result = recorder.Stop();

        Assert.True(result.Buffer.Truncated);
        Assert.Equal(AudioBuffer.MaxDuration, result.Buffer.Duration);
    }

    [Fact]
    public void Capture_SecondStartWhileRecording_Fails()
    {
        var backend = new FakeBackend(new AudioDevice("Headset", true));
        using var recorder = new AudioRecorder(backend);
        recorder.Start();

        Assert.False(recorder.Start().Started);
        recorder.Stop();
    }

    private sealed class FakeBackend(params AudioDevice[] devices) : ICaptureBackend
    {
        public FakeStream? Stream { get; private set; }

        public Exception? OpenError { get; init; }

        public WaveFormat Format { get; init; } = DeviceFormat;

        public IReadOnlyList<AudioDevice> GetInputDevices() => devices;

        public ICaptureStream Open(AudioDevice device)
        {
            if (OpenError is not null)
            {
                throw OpenError;
            }

            Stream = new FakeStream(Format);
            return Stream;
        }
    }

    private sealed class FakeStream(WaveFormat format) : ICaptureStream
    {
        public event CaptureDataHandler? DataAvailable;

        public event Action<Exception?>? Stopped;

        public WaveFormat Format => format;

        public bool IsOpen { get; private set; }

        public bool Disposed { get; private set; }

        public void Start() => IsOpen = true;

        public void Stop()
        {
            if (IsOpen)
            {
                IsOpen = false;
                Stopped?.Invoke(null);
            }
        }

        public void Push(byte[] data)
        {
            // Deliver in 10 ms chunks like a real device.
            var chunk = format.AverageBytesPerSecond / 100;
            for (var offset = 0; offset < data.Length; offset += chunk)
            {
                DataAvailable?.Invoke(data.AsSpan(offset, Math.Min(chunk, data.Length - offset)));
            }
        }

        public void Fail(Exception error)
        {
            IsOpen = false;
            Stopped?.Invoke(error);
        }

        public void Dispose()
        {
            IsOpen = false;
            Disposed = true;
        }
    }
}
