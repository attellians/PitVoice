using PitVoice.Core.Cues;
using PitVoice.Speech.Cues;
using Xunit;

namespace PitVoice.Speech.Tests.Cues;

public sealed class CueSoundsTests
{
    public static TheoryData<Cue> AllCues => [.. Enum.GetValues<Cue>()];

    [Theory]
    [MemberData(nameof(AllCues))]
    public void EmbeddedSound_DecodesAndFitsTheLimit(Cue cue)
    {
        var sound = CueSounds.Load(cue);

        Assert.Equal(44100, sound.SampleRate);
        Assert.InRange(sound.Duration, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(400));
        Assert.InRange(sound.Samples.Max(Math.Abs), 0.1f, 1f);
    }

    [Fact]
    public void StartCue_IsAtMost150Milliseconds() =>
        Assert.True(CueSounds.Load(Cue.Start).Duration <= TimeSpan.FromMilliseconds(150));

    [Fact]
    public void Sounds_AreDifferent()
    {
        var sounds = Enum.GetValues<Cue>().Select(CueSounds.Load).ToList();

        for (var i = 0; i < sounds.Count; i++)
        {
            for (var j = i + 1; j < sounds.Count; j++)
            {
                Assert.False(sounds[i].Samples.AsSpan().SequenceEqual(sounds[j].Samples), $"cues {i} and {j} are identical");
            }
        }
    }

    [Fact]
    public void SuccessCue_GoesUpAFourthFrom520Hz()
    {
        var sound = CueSounds.Load(Cue.Success);

        Assert.InRange(PeakHz(sound, 20, 115), 505, 535);
        Assert.InRange(PeakHz(sound, 140, 250), 679, 709);
    }

    [Fact]
    public void ErrorCue_GoesDownAFourthBelowSuccess()
    {
        var error = CueSounds.Load(Cue.Error);
        var first = PeakHz(error, 20, 150);
        var second = PeakHz(error, 180, 300);

        Assert.InRange(first, 297, 327);
        Assert.InRange(second, 219, 249);
        var successFirst = PeakHz(CueSounds.Load(Cue.Success), 20, 115);
        Assert.True(first < successFirst && second < successFirst, $"error notes {first}/{second} Hz, success {successFirst} Hz");
    }

    [Theory]
    [MemberData(nameof(AllCues))]
    public void Sound_EndsWithoutAClick(Cue cue)
    {
        var samples = CueSounds.Load(cue).Samples;
        var peak = samples.Max(Math.Abs);

        foreach (var sample in samples[^10..])
        {
            Assert.True(Math.Abs(sample) < 0.01f * peak, $"tail sample {sample}, peak {peak}");
        }
    }

    [Fact]
    public void BrokenSound_IsInvalidData()
    {
        using var stream = new MemoryStream("RIFF\0\0\0\0WAVEjunk"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => CueSounds.Decode(stream, "broken.wav"));
    }

    [Theory]
    [InlineData(100, 1f)]
    [InlineData(50, 0.5f)]
    [InlineData(0, 0f)]
    [InlineData(150, 1f)]
    [InlineData(-10, 0f)]
    public void Volume_ScalesSamples(int volume, float gain)
    {
        var sound = new CueSound([0.5f, -0.5f, 0.25f, 1f], 4);

        var rendered = CueSounds.Render(sound, volume, 4, 1);

        Assert.Equal(sound.Samples.Select(s => s * gain), rendered);
    }

    [Fact]
    public void Render_ResamplesAndDuplicatesChannels()
    {
        var sound = CueSounds.Load(Cue.Success);

        var rendered = CueSounds.Render(sound, 100, 48000, 2);

        var frames = rendered.Length / 2;
        Assert.Equal((int)((long)sound.Samples.Length * 48000 / 44100), frames);
        for (var i = 0; i < rendered.Length; i += 2)
        {
            Assert.Equal(rendered[i], rendered[i + 1]);
        }
    }

    /// <summary>
    /// Strongest frequency between 200 and 1200 Hz (1 Hz steps) in a Hann-windowed slice of the sound. The band leaves
    /// out the sub-bass, which stays at or below 160 Hz plus its short push.
    /// </summary>
    private static int PeakHz(CueSound sound, int fromMs, int toMs)
    {
        var from = fromMs * sound.SampleRate / 1000;
        var count = (toMs * sound.SampleRate / 1000) - from;
        var best = 0;
        var bestPower = -1.0;
        for (var hz = 200; hz <= 1200; hz++)
        {
            double re = 0, im = 0;
            for (var i = 0; i < count; i++)
            {
                var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (count - 1)));
                var angle = 2 * Math.PI * hz * i / sound.SampleRate;
                var value = sound.Samples[from + i] * window;
                re += value * Math.Cos(angle);
                im -= value * Math.Sin(angle);
            }

            var power = (re * re) + (im * im);
            if (power > bestPower)
            {
                bestPower = power;
                best = hz;
            }
        }

        return best;
    }
}
