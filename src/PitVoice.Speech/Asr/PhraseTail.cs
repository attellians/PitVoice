using PitVoice.Core.Audio;

namespace PitVoice.Speech.Asr;

/// <summary>
/// Speech after the last recognized word (FR-ASR-8, design D1 of recover-dropped-phrase-tail). The grammar loop can
/// explain a quiet tail ("на слики" after "меняем резину") with silence; the energy after the last word shows it.
/// </summary>
internal static class PhraseTail
{
    /// <summary>Frame length for the level, in samples (20 ms).</summary>
    internal const int FrameSamples = AudioBuffer.SampleRate / 50;

    /// <summary>A frame is speech when it is no more than this below the loudest frame of the recording.</summary>
    internal const double BelowLoudestDb = 25.0;

    /// <summary>A frame is speech only when louder than this, so a very quiet recording does not count its noise.</summary>
    internal const double FloorDbfs = -55.0;

    /// <summary>Frames are counted from the end of the last word plus this, past the fade Vosk cuts off early.</summary>
    internal const double OffsetSeconds = 0.1;

    /// <summary>The tail is there when its speech frames add up to at least this.</summary>
    internal const double MinSpeechSeconds = 0.1;

    /// <summary>Seconds of speech frames starting no earlier than <paramref name="lastWordEnd"/> + <see cref="OffsetSeconds"/>.</summary>
    internal static double SpeechAfter(IReadOnlyList<short> samples, double lastWordEnd)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var frames = samples.Count / FrameSamples;
        if (frames == 0)
        {
            return 0.0;
        }

        var levels = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            levels[f] = FrameDbfs(samples, f * FrameSamples);
        }

        var threshold = Math.Max(levels.Max() - BelowLoudestDb, FloorDbfs);
        var first = (int)Math.Ceiling((lastWordEnd + OffsetSeconds) * AudioBuffer.SampleRate / FrameSamples);
        var speech = 0;
        for (var f = Math.Max(first, 0); f < frames; f++)
        {
            if (levels[f] >= threshold && levels[f] > FloorDbfs)
            {
                speech++;
            }
        }

        return speech * (double)FrameSamples / AudioBuffer.SampleRate;
    }

    /// <summary>True when the recording has a tail of speech after the last word worth recognizing on its own.</summary>
    internal static bool IsDropped(IReadOnlyList<short> samples, double lastWordEnd) =>
        SpeechAfter(samples, lastWordEnd) >= MinSpeechSeconds - 1e-9;

    private static double FrameDbfs(IReadOnlyList<short> samples, int start)
    {
        var sum = 0.0;
        for (var i = start; i < start + FrameSamples; i++)
        {
            var value = samples[i] / 32768.0;
            sum += value * value;
        }

        var rms = Math.Sqrt(sum / FrameSamples);
        return rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity;
    }
}
