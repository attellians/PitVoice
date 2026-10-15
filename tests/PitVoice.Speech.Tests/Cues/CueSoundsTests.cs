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
}
