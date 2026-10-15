using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PitVoice.Core.Audio;
using PitVoice.Core.Cues;
using Cue = PitVoice.Core.Cues.Cue;

namespace PitVoice.Speech.Cues;

/// <summary>
/// Plays the cues through WASAPI shared mode (design decision 3). The sounds are decoded at construction; the output
/// device is opened on the first cue, kept open while cues follow each other and closed after
/// <see cref="IdleClose"/> of silence. A new cue cuts the current one. Volume 0 never opens the device. The device is
/// chosen by name; a missing one falls back to the system default with a warning. Nothing here throws for device or
/// sound problems: they come back in <see cref="CuePlayback"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NAudioCuePlayer : IAudioCuePlayer, IAudioOutputCatalog, IDisposable
{
    public static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(5);
    private const int LatencyMilliseconds = 40;

    private readonly Dictionary<Cue, CueSound> _sounds = [];
    private readonly Dictionary<Cue, string> _broken = [];
    private readonly Dictionary<(Cue, int, int), float[]> _rendered = [];
    private readonly Lock _lock = new();
    private readonly ITimer _idle;
    private Output? _output;

    /// <param name="deviceName">Output device name; null means the system default.</param>
    /// <param name="volume">Volume in percent, clamped to 0..100.</param>
    /// <param name="time">Clock of the idle timer.</param>
    public NAudioCuePlayer(string? deviceName, int volume, TimeProvider? time = null)
    {
        DeviceName = deviceName;
        Volume = Math.Clamp(volume, 0, 100);
        foreach (var cue in Enum.GetValues<Cue>())
        {
            try
            {
                _sounds[cue] = CueSounds.Load(cue);
            }
            catch (InvalidDataException e)
            {
                _broken[cue] = e.Message;
            }
        }

        _idle = (time ?? TimeProvider.System).CreateTimer(_ => CloseIfIdle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public string? DeviceName { get; }

    public int Volume { get; }

    public TimeSpan GetDuration(Cue cue) => _sounds.TryGetValue(cue, out var sound) ? sound.Duration : TimeSpan.Zero;

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        if (enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out var defaultDevice))
        {
            using (defaultDevice)
            {
                defaultId = defaultDevice.ID;
            }
        }

        var result = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioDevice(device.FriendlyName, string.Equals(device.ID, defaultId, StringComparison.Ordinal)));
            }
        }

        return result;
    }

    public CuePlayback Play(Cue cue)
    {
        if (_broken.TryGetValue(cue, out var problem))
        {
            return CuePlayback.Failed(problem);
        }

        if (Volume == 0)
        {
            return CuePlayback.Muted;
        }

        lock (_lock)
        {
            string? warning = null;
            if (_output is null or { Failed: true })
            {
                _output?.Dispose();
                _output = null;
                var opened = Open(out warning, out var error);
                if (opened is null)
                {
                    return CuePlayback.Failed(error!);
                }

                _output = opened;
            }

            var key = (cue, _output.SampleRate, _output.Channels);
            if (!_rendered.TryGetValue(key, out var samples))
            {
                samples = CueSounds.Render(_sounds[cue], Volume, _output.SampleRate, _output.Channels);
                _rendered[key] = samples;
            }

            _output.Stream.Play(samples);
            _idle.Change(_sounds[cue].Duration + IdleClose, Timeout.InfiniteTimeSpan);
            return CuePlayback.Ok(_output.DeviceName, warning);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _output?.Stream.Cut();
        }
    }

    public void Dispose()
    {
        _idle.Dispose();
        lock (_lock)
        {
            _output?.Dispose();
            _output = null;
        }
    }

    private void CloseIfIdle()
    {
        lock (_lock)
        {
            if (_output is { Stream.IsPlaying: false })
            {
                _output.Dispose();
                _output = null;
            }
        }
    }

    private Output? Open(out string? warning, out string? error)
    {
        warning = null;
        error = null;
        MMDevice? device = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!string.IsNullOrWhiteSpace(DeviceName))
            {
                foreach (var candidate in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (device is null && string.Equals(candidate.FriendlyName, DeviceName, StringComparison.Ordinal))
                    {
                        device = candidate;
                    }
                    else
                    {
                        candidate.Dispose();
                    }
                }
            }

            if (device is null)
            {
                if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out device))
                {
                    error = "no output device";
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(DeviceName))
                {
                    warning = $"output device '{DeviceName}' not found, using the default '{device.FriendlyName}'";
                }
            }

            return new Output(device);
        }
#pragma warning disable CA1031 // Any device failure becomes a result; the pipeline must go on.
        catch (Exception e)
#pragma warning restore CA1031
        {
            device?.Dispose();
            error = $"output device could not be opened: {e.Message}";
            return null;
        }
    }

    /// <summary>An open WASAPI output playing <see cref="CueStream"/> in the device mix rate and channel count.</summary>
    private sealed class Output : IDisposable
    {
        private readonly MMDevice _device;
        private readonly WasapiPlayer _player;
        private volatile bool _failed;

        public Output(MMDevice device)
        {
            _device = device;
            DeviceName = device.FriendlyName;
            try
            {
                _player = new WasapiPlayerBuilder()
                    .WithDevice(device)
                    .WithSharedMode()
                    .WithEventSync()
                    .WithLatency(LatencyMilliseconds)
                    .Build();
                var mix = _player.DeviceMixFormat;
                SampleRate = mix.SampleRate;
                Channels = mix.Channels;
                Stream = new CueStream(SampleRate, Channels);
                _player.PlaybackStopped += (_, e) => _failed = e.Exception is not null;
                _player.Init(Stream.ToWaveProvider());
                _player.Play();
            }
            catch
            {
                _player?.Dispose();
                device.Dispose();
                throw;
            }
        }

        public string DeviceName { get; }

        public int SampleRate { get; }

        public int Channels { get; }

        public CueStream Stream { get; } = null!;

        public bool Failed => _failed;

        public void Dispose()
        {
            _player.Dispose();
            _device.Dispose();
        }
    }

    /// <summary>Endless float stream: the current cue, then silence. A new cue replaces the current one at once.</summary>
    private sealed class CueStream(int sampleRate, int channels) : ISampleProvider
    {
        private readonly Lock _lock = new();
        private float[] _samples = [];
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _position < _samples.Length;
                }
            }
        }

        public void Play(float[] samples)
        {
            lock (_lock)
            {
                _samples = samples;
                _position = 0;
            }
        }

        public void Cut()
        {
            lock (_lock)
            {
                _position = _samples.Length;
            }
        }

        public int Read(Span<float> buffer)
        {
            lock (_lock)
            {
                var available = Math.Max(0, Math.Min(buffer.Length, _samples.Length - _position));
                _samples.AsSpan(_position, available).CopyTo(buffer);
                _position += available;
                buffer[available..].Clear();
                return buffer.Length;
            }
        }
    }
}
