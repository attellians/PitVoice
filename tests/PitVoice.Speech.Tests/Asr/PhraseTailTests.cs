using PitVoice.Core.Audio;
using PitVoice.Speech.Asr;
using Xunit;

namespace PitVoice.Speech.Tests.Asr;

/// <summary>Design D1 of recover-dropped-phrase-tail: speech after the last recognized word.</summary>
public sealed class PhraseTailTests
{
    /// <summary>Amplitude of the phrase tone: RMS about −9 dBFS.</summary>
    private const double Loud = 0.5;

    private static double Below(double db) => Loud * Math.Pow(10, -db / 20);

    private static short[] Tone(double seconds, double amplitude) =>
        Enumerable.Range(0, (int)(seconds * AudioBuffer.SampleRate))
            .Select(i => (short)(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * 440 * i / AudioBuffer.SampleRate)))
            .ToArray();

    private static short[] Silence(double seconds) => new short[(int)(seconds * AudioBuffer.SampleRate)];

    private static short[] Join(params short[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Silence_after_the_phrase_is_no_tail()
    {
        var samples = Join(Tone(1.0, Loud), Silence(1.0));

        Assert.Equal(0.0, PhraseTail.SpeechAfter(samples, 1.0));
        Assert.False(PhraseTail.IsDropped(samples, 1.0));
    }

    [Fact]
    public void Quiet_speech_after_the_phrase_is_a_tail()
    {
        // "на слики": half a second 15–20 dB below the start of the phrase.
        var samples = Join(Tone(1.0, Loud), Tone(0.5, Below(18)), Silence(0.3));

        Assert.Equal(0.4, PhraseTail.SpeechAfter(samples, 1.0), precision: 6);
        Assert.True(PhraseTail.IsDropped(samples, 1.0));
    }

    [Fact]
    public void Short_click_is_no_tail()
    {
        var samples = Join(Tone(1.0, Loud), Silence(0.3), Tone(0.04, Loud), Silence(0.5));

        Assert.Equal(0.04, PhraseTail.SpeechAfter(samples, 1.0), precision: 6);
        Assert.False(PhraseTail.IsDropped(samples, 1.0));
    }

    [Fact]
    public void Sound_more_than_25_dB_below_the_phrase_is_no_tail()
    {
        var samples = Join(Tone(1.0, Loud), Tone(0.5, Below(30)), Silence(0.3));

        Assert.Equal(0.0, PhraseTail.SpeechAfter(samples, 1.0));
        Assert.False(PhraseTail.IsDropped(samples, 1.0));
    }

    [Fact]
    public void Recording_below_the_noise_floor_is_no_tail()
    {
        // The whole recording is quieter than −55 dBFS: the tail is close to the loudest frame, but it is noise.
        var samples = Join(Tone(1.0, Below(55)), Tone(0.5, Below(60)), Silence(0.3));

        Assert.Equal(0.0, PhraseTail.SpeechAfter(samples, 1.0));
        Assert.False(PhraseTail.IsDropped(samples, 1.0));
    }

    [Fact]
    public void Word_ending_at_the_end_of_the_buffer_is_no_tail()
    {
        var samples = Tone(1.0, Loud);

        Assert.Equal(0.0, PhraseTail.SpeechAfter(samples, 1.0));
        Assert.Equal(0.0, PhraseTail.SpeechAfter(samples, 0.95));
        Assert.False(PhraseTail.IsDropped(samples, 1.0));
    }

    [Fact]
    public void Fade_of_the_last_word_is_skipped()
    {
        // Vosk ends the word before its energy; 0.1 s after the end is not counted.
        var samples = Join(Tone(1.08, Loud), Silence(1.0));

        Assert.Equal(0.0, PhraseTail.SpeechAfter(samples, 1.0));
    }
}
