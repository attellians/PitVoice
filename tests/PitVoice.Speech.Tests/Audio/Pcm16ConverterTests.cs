using NAudio.Wave;
using PitVoice.Speech.Audio;
using Xunit;
using AudioBuffer = PitVoice.Core.Audio.AudioBuffer;

namespace PitVoice.Speech.Tests.Audio;

public sealed class Pcm16ConverterTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void Resampler_48kStereoFloat_To16kMono()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var converter = new Pcm16Converter(format);

        converter.Write(Signals.Sine(format, 440, TimeSpan.FromSeconds(2)));
        var buffer = converter.Complete();

        AssertDuration(TimeSpan.FromSeconds(2), buffer.Duration);
        Assert.InRange(buffer.Samples.Count, 32000 - 800, 32000 + 800);
        Assert.InRange(Signals.EstimateFrequency(buffer.Samples, AudioBuffer.SampleRate), 430, 450);
        Assert.False(buffer.Truncated);
    }

    [Fact]
    public void Resampler_ChunkedInput_SameAsWhole()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var data = Signals.Sine(format, 300, TimeSpan.FromSeconds(1));
        var whole = new Pcm16Converter(format);
        whole.Write(data);
        var chunked = new Pcm16Converter(format);

        // Odd chunk sizes split frames between writes.
        for (var offset = 0; offset < data.Length; offset += 1237)
        {
            chunked.Write(data.AsSpan(offset, Math.Min(1237, data.Length - offset)));
        }

        Assert.Equal(whole.Complete().Samples, chunked.Complete().Samples);
    }

    [Theory]
    [InlineData(16000, 16, 1)]
    [InlineData(44100, 16, 2)]
    [InlineData(8000, 16, 1)]
    [InlineData(48000, 24, 2)]
    [InlineData(22050, 8, 1)]
    public void Resampler_PcmFormats_KeepDurationAndTone(int rate, int bits, int channels)
    {
        var format = new WaveFormat(rate, bits, channels);
        var converter = new Pcm16Converter(format);

        converter.Write(Signals.Sine(format, 500, TimeSpan.FromSeconds(1.5)));
        var buffer = converter.Complete();

        AssertDuration(TimeSpan.FromSeconds(1.5), buffer.Duration);
        Assert.InRange(Signals.EstimateFrequency(buffer.Samples, AudioBuffer.SampleRate), 490, 510);
    }

    [Fact]
    public void Resampler_Native16kMono_IsUnchanged()
    {
        var format = new WaveFormat(16000, 16, 1);
        var data = Signals.Sine(format, 440, TimeSpan.FromSeconds(0.5));
        var converter = new Pcm16Converter(format);

        converter.Write(data);
        var buffer = converter.Complete();

        var expected = new short[data.Length / 2];
        Buffer.BlockCopy(data, 0, expected, 0, data.Length);
        Assert.Equal(expected, buffer.Samples);
    }

    [Fact]
    public void Resampler_Limit_TruncatesAndFlags()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var converter = new Pcm16Converter(format, AudioBuffer.MaxSamples);

        for (var second = 0; second < 12; second++)
        {
            converter.Write(Signals.Sine(format, 440, TimeSpan.FromSeconds(1)));
        }

        var buffer = converter.Complete();

        Assert.True(buffer.Truncated);
        Assert.Equal(AudioBuffer.MaxSamples, buffer.Samples.Count);
        Assert.Equal(AudioBuffer.MaxDuration, buffer.Duration);
    }

    [Fact]
    public void Resampler_NoInput_EmptyBuffer()
    {
        var buffer = new Pcm16Converter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).Complete();

        Assert.True(buffer.IsEmpty);
        Assert.False(buffer.Truncated);
    }

    [Fact]
    public void Resampler_CompressedFormat_Rejected()
    {
        var format = WaveFormat.CreateALawFormat(8000, 1);

        Assert.NotNull(Pcm16Converter.Describe(format));
        Assert.Throws<NotSupportedException>(() => new Pcm16Converter(format));
    }

    private static void AssertDuration(TimeSpan expected, TimeSpan actual) =>
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
}
