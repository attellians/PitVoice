using System.Buffers.Binary;
using NAudio.Dsp;
using NAudio.Wave;
using AudioBuffer = PitVoice.Core.Audio.AudioBuffer;

namespace PitVoice.Speech.Audio;

/// <summary>
/// Converts a stream of audio in any PCM (8/16/24/32-bit) or IEEE float (32/64-bit) format, any rate and channel count,
/// into 16 kHz mono 16-bit samples (FR-AUD-2, design D2). Channels are averaged, the rate is changed with the managed WDL
/// resampler. Input may arrive in chunks of any size; the same path serves microphones and files.
/// </summary>
internal sealed class Pcm16Converter
{
    private const int PaddingMilliseconds = 100;

    private readonly WaveFormat _format;
    private readonly SampleKind _kind;
    private readonly int _bytesPerSample;
    private readonly int _channels;
    private readonly int _blockAlign;
    private readonly int _maxSamples;
    private readonly WdlResampler? _resampler;
    private readonly List<short> _output = [];
    private readonly byte[] _pending;
    private int _pendingCount;
    private long _inputFrames;
    private float[] _mono = [];
    private float[] _resampled = [];

    /// <param name="format">Format of the incoming bytes.</param>
    /// <param name="maxSamples">Output cap; input beyond it is dropped and <see cref="Truncated"/> is set.</param>
    public Pcm16Converter(WaveFormat format, int maxSamples = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (Describe(format) is { } problem)
        {
            throw new NotSupportedException(problem);
        }

        _format = format;
        _kind = KindOf(format);
        _bytesPerSample = format.BitsPerSample / 8;
        _channels = format.Channels;
        _blockAlign = _bytesPerSample * _channels;
        _maxSamples = maxSamples;
        _pending = new byte[_blockAlign];

        if (format.SampleRate != AudioBuffer.SampleRate)
        {
            _resampler = new WdlResampler();
            _resampler.SetMode(true, 2, false);
            _resampler.SetFilterParms();
            _resampler.SetFeedMode(true);
            _resampler.SetRates(format.SampleRate, AudioBuffer.SampleRate);
        }
    }

    public WaveFormat SourceFormat => _format;

    /// <summary>True once input beyond the cap has been dropped.</summary>
    public bool Truncated { get; private set; }

    public int SampleCount => _output.Count;

    /// <summary>Why <paramref name="format"/> cannot be converted, or null when it can.</summary>
    public static string? Describe(WaveFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.Channels < 1 || format.SampleRate < 1)
        {
            return $"invalid format: {format.Channels} channel(s), {format.SampleRate} Hz";
        }

        return KindOf(format) switch
        {
            SampleKind.Pcm when format.BitsPerSample is 8 or 16 or 24 or 32 => null,
            SampleKind.Float when format.BitsPerSample is 32 or 64 => null,
            SampleKind.Pcm or SampleKind.Float => $"unsupported sample size: {format.BitsPerSample} bit {format.Encoding}",
            _ => $"unsupported encoding {format.Encoding} (compressed audio is not supported; use PCM or float WAV)",
        };
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        // Complete a frame split between two chunks.
        if (_pendingCount > 0)
        {
            var take = Math.Min(_blockAlign - _pendingCount, data.Length);
            data[..take].CopyTo(_pending.AsSpan(_pendingCount));
            _pendingCount += take;
            data = data[take..];
            if (_pendingCount < _blockAlign)
            {
                return;
            }

            ConvertFrames(_pending);
            _pendingCount = 0;
        }

        var whole = data.Length - (data.Length % _blockAlign);
        if (whole > 0)
        {
            ConvertFrames(data[..whole]);
        }

        var rest = data[whole..];
        rest.CopyTo(_pending);
        _pendingCount = rest.Length;
    }

    /// <summary>Flushes the resampler and returns the converted audio, cut to the exact expected length.</summary>
    public AudioBuffer Complete()
    {
        if (_resampler is not null)
        {
            var padding = new float[_format.SampleRate * PaddingMilliseconds / 1000];
            Resample(padding);
        }

        var expected = (long)Math.Round(_inputFrames * (double)AudioBuffer.SampleRate / _format.SampleRate);
        var length = (int)Math.Min(Math.Min(expected, _output.Count), _maxSamples);
        return new AudioBuffer(_output.GetRange(0, length).ToArray(), Truncated);
    }

    private void ConvertFrames(ReadOnlySpan<byte> frames)
    {
        if (Truncated)
        {
            return;
        }

        var count = frames.Length / _blockAlign;
        var allowedFrames = (long)Math.Ceiling((double)_maxSamples * _format.SampleRate / AudioBuffer.SampleRate) - _inputFrames;
        if (count > allowedFrames)
        {
            Truncated = true;
            count = (int)Math.Max(0, allowedFrames);
        }

        if (count == 0)
        {
            return;
        }

        if (_mono.Length < count)
        {
            _mono = new float[count];
        }

        for (var frame = 0; frame < count; frame++)
        {
            var sum = 0.0f;
            var offset = frame * _blockAlign;
            for (var channel = 0; channel < _channels; channel++)
            {
                sum += ReadSample(frames.Slice(offset + (channel * _bytesPerSample), _bytesPerSample));
            }

            _mono[frame] = sum / _channels;
        }

        _inputFrames += count;
        if (_resampler is null)
        {
            Append(_mono.AsSpan(0, count));
        }
        else
        {
            Resample(_mono.AsSpan(0, count));
        }
    }

    private void Resample(ReadOnlySpan<float> input)
    {
        var resampler = _resampler!;
        var wanted = resampler.ResamplePrepare(input.Length, 1, out var inBuffer);
        var given = Math.Min(wanted, input.Length);
        input[..given].CopyTo(inBuffer);

        var capacity = (int)((long)given * AudioBuffer.SampleRate / _format.SampleRate) + 64;
        if (_resampled.Length < capacity)
        {
            _resampled = new float[capacity];
        }

        var produced = resampler.ResampleOut(_resampled, given, capacity, 1);
        Append(_resampled.AsSpan(0, produced));
        if (given < input.Length)
        {
            Resample(input[given..]);
        }
    }

    private void Append(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _output.Add((short)Math.Clamp(MathF.Round(sample * 32768f), short.MinValue, short.MaxValue));
        }
    }

    private float ReadSample(ReadOnlySpan<byte> bytes) => (_kind, _bytesPerSample) switch
    {
        (SampleKind.Pcm, 1) => (bytes[0] - 128) / 128f,
        (SampleKind.Pcm, 2) => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
        (SampleKind.Pcm, 3) => ((bytes[2] << 24) | (bytes[1] << 16) | (bytes[0] << 8)) / 2147483648f,
        (SampleKind.Pcm, 4) => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f,
        (SampleKind.Float, 4) => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        (SampleKind.Float, 8) => (float)BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        _ => throw new InvalidOperationException("unreachable: format validated in constructor"),
    };

    private static SampleKind KindOf(WaveFormat format) => format.Encoding switch
    {
        WaveFormatEncoding.Pcm => SampleKind.Pcm,
        WaveFormatEncoding.IeeeFloat => SampleKind.Float,
        WaveFormatEncoding.Extensible when format is WaveFormatExtensible extensible =>
            extensible.SubFormat == AudioSubtypePcm ? SampleKind.Pcm
            : extensible.SubFormat == AudioSubtypeFloat ? SampleKind.Float
            : SampleKind.Other,
        _ => SampleKind.Other,
    };

    // KSDATAFORMAT_SUBTYPE_PCM and KSDATAFORMAT_SUBTYPE_IEEE_FLOAT.
    private static readonly Guid AudioSubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid AudioSubtypeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private enum SampleKind
    {
        Pcm,
        Float,
        Other,
    }
}
