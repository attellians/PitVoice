namespace PitVoice.Core.Audio;

/// <summary>
/// Recorded speech in the format the recognizer expects (FR-AUD-2): 16 kHz, mono, 16-bit PCM. Lives in memory only.
/// </summary>
public sealed class AudioBuffer
{
    public const int SampleRate = 16000;

    /// <summary>Longest recording kept (FR-AUD-4); anything after it is dropped.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(8);

    public static int MaxSamples => (int)(MaxDuration.Ticks * SampleRate / TimeSpan.TicksPerSecond);

    public AudioBuffer(short[] samples, bool truncated = false)
    {
        ArgumentNullException.ThrowIfNull(samples);
        Samples = samples;
        Truncated = truncated;
    }

    public static AudioBuffer Empty { get; } = new([]);

    /// <summary>Mono 16-bit samples at <see cref="SampleRate"/>.</summary>
    public IReadOnlyList<short> Samples { get; }

    /// <summary>True when the recording was cut to <see cref="MaxDuration"/>.</summary>
    public bool Truncated { get; }

    public bool IsEmpty => Samples.Count == 0;

    public TimeSpan Duration => TimeSpan.FromTicks(Samples.Count * TimeSpan.TicksPerSecond / SampleRate);

    /// <summary>The samples as an array without copying when possible.</summary>
    public short[] ToArray() => Samples as short[] ?? [.. Samples];
}
