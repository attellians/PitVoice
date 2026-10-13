using NAudio.Wave;

namespace PitVoice.Speech.Tests.Audio;

/// <summary>Synthetic test audio.</summary>
internal static class Signals
{
    /// <summary>Interleaved sine in <paramref name="format"/> (every channel carries the same tone).</summary>
    public static byte[] Sine(WaveFormat format, double frequency, TimeSpan duration, double amplitude = 0.5)
    {
        var frames = (int)(format.SampleRate * duration.TotalSeconds);
        var bytesPerSample = format.BitsPerSample / 8;
        var data = new byte[frames * format.Channels * bytesPerSample];
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        var offset = 0;
        for (var i = 0; i < frames; i++)
        {
            var value = amplitude * Math.Sin(2 * Math.PI * frequency * i / format.SampleRate);
            for (var c = 0; c < format.Channels; c++)
            {
                WriteSample(data.AsSpan(offset, bytesPerSample), value, isFloat);
                offset += bytesPerSample;
            }
        }

        return data;
    }

    public static string WriteWav(string path, WaveFormat format, byte[] data)
    {
        using var writer = new WaveFileWriter(path, format);
        writer.Write(data, 0, data.Length);
        return path;
    }

    /// <summary>Tone frequency estimated from zero crossings.</summary>
    public static double EstimateFrequency(IReadOnlyList<short> samples, int sampleRate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            if ((samples[i - 1] < 0 && samples[i] >= 0) || (samples[i - 1] >= 0 && samples[i] < 0))
            {
                crossings++;
            }
        }

        return crossings / 2.0 / ((double)samples.Count / sampleRate);
    }

    private static void WriteSample(Span<byte> target, double value, bool isFloat)
    {
        switch (target.Length, isFloat)
        {
            case (4, true):
                BitConverter.TryWriteBytes(target, (float)value);
                break;
            case (2, false):
                BitConverter.TryWriteBytes(target, (short)(value * short.MaxValue));
                break;
            case (3, false):
                var v24 = (int)(value * 8388607);
                target[0] = (byte)v24;
                target[1] = (byte)(v24 >> 8);
                target[2] = (byte)(v24 >> 16);
                break;
            case (1, false):
                target[0] = (byte)(128 + (value * 127));
                break;
            default:
                throw new NotSupportedException($"{target.Length * 8} bit");
        }
    }
}

/// <summary>A temporary directory removed after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pitvoice-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
