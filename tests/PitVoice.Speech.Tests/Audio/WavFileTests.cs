using NAudio.Wave;
using PitVoice.Speech.Audio;
using Xunit;
using AudioBuffer = PitVoice.Core.Audio.AudioBuffer;

namespace PitVoice.Speech.Tests.Audio;

public sealed class WavFileTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(50);

    [Fact]
    public void Wav_44kStereo_LoadsAs16kMono()
    {
        using var temp = new TempDirectory();
        var format = new WaveFormat(44100, 16, 2);
        var path = Signals.WriteWav(temp.File("stereo.wav"), format, Signals.Sine(format, 440, TimeSpan.FromSeconds(3)));

        var buffer = WavFile.Load(path);

        Assert.InRange(buffer.Duration, TimeSpan.FromSeconds(3) - Tolerance, TimeSpan.FromSeconds(3) + Tolerance);
        Assert.InRange(Signals.EstimateFrequency(buffer.Samples, AudioBuffer.SampleRate), 430, 450);
    }

    [Fact]
    public void Wav_8kMono_LoadsAs16kMono()
    {
        using var temp = new TempDirectory();
        var format = new WaveFormat(8000, 16, 1);
        var path = Signals.WriteWav(temp.File("narrow.wav"), format, Signals.Sine(format, 300, TimeSpan.FromSeconds(2)));

        var buffer = WavFile.Load(path);

        Assert.InRange(buffer.Samples.Count, 32000 - 800, 32000 + 800);
        Assert.InRange(Signals.EstimateFrequency(buffer.Samples, AudioBuffer.SampleRate), 290, 310);
    }

    [Fact]
    public void Wav_FloatFile_Loads()
    {
        using var temp = new TempDirectory();
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        var path = Signals.WriteWav(temp.File("float.wav"), format, Signals.Sine(format, 440, TimeSpan.FromSeconds(1)));

        Assert.InRange(WavFile.Load(path).Duration, TimeSpan.FromSeconds(1) - Tolerance, TimeSpan.FromSeconds(1) + Tolerance);
    }

    [Fact]
    public void Wav_TextFileWithWavExtension_ErrorNamesFile()
    {
        using var temp = new TempDirectory();
        var path = temp.File("notes.wav");
        File.WriteAllText(path, "this is not audio, just text pretending to be a wav file");

        var error = Assert.Throws<WavLoadException>(() => WavFile.Load(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wav_CompressedCodec_ErrorNamesFileAndEncoding()
    {
        using var temp = new TempDirectory();
        var format = WaveFormat.CreateALawFormat(8000, 1);
        var path = Signals.WriteWav(temp.File("alaw.wav"), format, new byte[8000]);

        var error = Assert.Throws<WavLoadException>(() => WavFile.Load(path));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.Contains("ALaw", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wav_MissingFile_ErrorNamesFile()
    {
        var error = Assert.Throws<WavLoadException>(() => WavFile.Load("no-such-file.wav"));

        Assert.Contains("no-such-file.wav", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wav_SaveWithoutPath_WritesNothing()
    {
        using var temp = new TempDirectory();
        var buffer = new AudioBuffer([1, 2, 3]);

        Assert.False(WavFile.SaveIfRequested(buffer, null));
        Assert.False(WavFile.SaveIfRequested(buffer, " "));

        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public void Wav_SaveWithPath_RoundTripsIdentically()
    {
        using var temp = new TempDirectory();
        var samples = Enumerable.Range(0, 16000).Select(i => (short)((i * 37 % 2000) - 1000)).ToArray();
        var path = temp.File(Path.Combine("sub", "take.wav"));

        Assert.True(WavFile.SaveIfRequested(new AudioBuffer(samples), path));

        using (var reader = new WaveFileReader(path))
        {
            Assert.Equal(16000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        }

        Assert.Equal(samples, WavFile.Load(path).Samples);
        Assert.Single(Directory.EnumerateFiles(temp.Path, "*", SearchOption.AllDirectories));
    }
}
