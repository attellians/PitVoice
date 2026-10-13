using NAudio.Wave;
using AudioBuffer = PitVoice.Core.Audio.AudioBuffer;

namespace PitVoice.Speech.Audio;

/// <summary>A file could not be loaded as audio; the message names the file and the reason.</summary>
public sealed class WavLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads .wav files of any PCM/float format into an <see cref="AudioBuffer"/> through the same conversion as the
/// microphone, and writes buffers back only to an explicitly given path (FR-AUD-5 "in memory only").
/// </summary>
public static class WavFile
{
    public static AudioBuffer Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new WavLoadException($"{path}: file not found");
        }

        try
        {
            using var reader = new WaveFileReader(path);
            if (Pcm16Converter.Describe(reader.WaveFormat) is { } problem)
            {
                throw new WavLoadException($"{path}: {problem}");
            }

            var converter = new Pcm16Converter(reader.WaveFormat);
            var chunk = new byte[reader.WaveFormat.AverageBytesPerSecond];
            int read;
            while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
            {
                converter.Write(chunk.AsSpan(0, read));
            }

            return converter.Complete();
        }
        catch (Exception e) when (e is FormatException or EndOfStreamException or InvalidDataException or ArgumentException)
        {
            throw new WavLoadException($"{path}: not a valid WAV file: {e.Message}", e);
        }
    }

    /// <summary>Format of every file written by <see cref="Save"/>: 16 kHz mono 16-bit PCM.</summary>
    public static WaveFormat Format { get; } = new(AudioBuffer.SampleRate, 16, 1);

    public static void Save(AudioBuffer buffer, string path)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new WaveFileWriter(path, Format);
        var samples = buffer.ToArray();
        writer.WriteSamples(samples, 0, samples.Length);
    }

    /// <summary>Saves only when the user asked for it; returns whether a file was written.</summary>
    public static bool SaveIfRequested(AudioBuffer buffer, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        Save(buffer, path);
        return true;
    }
}
