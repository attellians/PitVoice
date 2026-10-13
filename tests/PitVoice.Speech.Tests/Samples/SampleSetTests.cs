using System.Globalization;
using NAudio.Wave;
using PitVoice.Core.Asr;
using PitVoice.Core.Parsing;
using PitVoice.Speech.Asr;
using PitVoice.Speech.Audio;
using Xunit;
using Xunit.Abstractions;

namespace PitVoice.Speech.Tests.Samples;

/// <summary>
/// Acceptance set (design D9): recorded commands run through the real engine and parser. Fails on any false command
/// (a different command than expected, or a command where a rejection was expected) and when fewer than 90% of the
/// records are correct.
/// </summary>
public sealed class SampleSetTests(ITestOutputHelper output)
{
    private const double MinCorrectShare = 0.9;
    private const int MinSamplesPerLanguage = 30;
    private const long MaxSetBytes = 15L * 1024 * 1024;

    [SampleTheory]
    [InlineData("ru")]
    [InlineData("en")]
    public async Task Samples_RecognizedAndParsed(string language)
    {
        var samples = SampleSet.Load(language);
        var pack = TestEnvironment.Pack(language);
        using var engine = new VoskAsrEngine();
        var load = await engine.LoadAsync(pack, TestEnvironment.RequireModel(language));
        Assert.True(load.IsLoaded, load.Error);
        var recognizer = new SpeechRecognizer(engine);
        var parser = new CommandParser(pack);

        var correct = 0;
        var missed = new List<string>();
        var wrong = new List<string>();
        var times = new List<double>();
        foreach (var sample in samples)
        {
            var result = recognizer.Recognize(WavFile.Load(Path.Combine(SampleSet.Directory(language), sample.File)));
            Assert.True(result.IsSuccess, $"{sample.File}: {result.Error}");
            times.Add(result.RecognitionTime.TotalMilliseconds);

            var parsed = parser.Parse(result.Phrase);
            var line = $"{sample.File}: said \"{sample.Say}\", heard \"{result.Phrase.Text}\", got {parsed}";
            switch (parsed, sample.Expect)
            {
                case (ParseResult.Rejected, null):
                case (ParseResult.Accepted accepted, { } expect) when SampleSet.Matches(expect, accepted.Commands):
                    correct++;
                    break;
                case (ParseResult.Rejected, _):
                    missed.Add(line);
                    break;
                default:
                    wrong.Add(line);
                    break;
            }
        }

        times.Sort();
        var median = times.Count == 0 ? 0 : times[times.Count / 2];
        output.WriteLine($"[{language}] {samples.Count} samples: correct {correct}, rejected {missed.Count}, false {wrong.Count}; median recognition {median.ToString("0", CultureInfo.InvariantCulture)} ms");
        foreach (var line in missed)
        {
            output.WriteLine($"  rejected: {line}");
        }

        foreach (var line in wrong)
        {
            output.WriteLine($"  FALSE:    {line}");
        }

        Assert.True(wrong.Count == 0, $"false commands:\n{string.Join('\n', wrong)}");
        Assert.True(
            correct >= MinCorrectShare * samples.Count,
            $"only {correct} of {samples.Count} samples correct (need {MinCorrectShare:P0}):\n{string.Join('\n', missed)}");
    }

    [SampleTheory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Samples_SetIsComplete(string language)
    {
        var samples = SampleSet.Load(language);
        var directory = SampleSet.Directory(language);

        Assert.True(samples.Count >= MinSamplesPerLanguage, $"{samples.Count} samples, need at least {MinSamplesPerLanguage}");
        Assert.True(samples.Count(s => s.Expect is null) >= 5, "need at least 5 samples that must be rejected");
        foreach (var sample in samples)
        {
            using var reader = new WaveFileReader(Path.Combine(directory, sample.File));
            Assert.True(
                reader.WaveFormat is { SampleRate: 16000, Channels: 1, BitsPerSample: 16, Encoding: WaveFormatEncoding.Pcm },
                $"{sample.File}: {reader.WaveFormat}, expected 16 kHz mono 16-bit PCM");
        }

        var total = new DirectoryInfo(Path.Combine(directory, "..")).EnumerateFiles("*.wav", SearchOption.AllDirectories).Sum(f => f.Length);
        Assert.True(total <= MaxSetBytes, $"sample set is {total / 1024 / 1024} MB, limit {MaxSetBytes / 1024 / 1024} MB");
    }
}

/// <summary>Skips until the language has recorded samples, and when its model is missing (unless models are required).</summary>
internal sealed class SampleTheoryAttribute : TheoryAttribute
{
    public SampleTheoryAttribute()
    {
        var missing = new[] { "ru", "en" }.Where(l => !SampleSet.Exists(l)).ToArray();
        if (missing.Length > 0)
        {
            Skip = $"no recorded samples for {string.Join(", ", missing)} yet: record them with 'pitvoice-cli listen --save-wav' into tests/PitVoice.Speech.Tests/Samples/<lang>/ and describe them in {SampleSet.FileName}";
            return;
        }

        Skip = TestEnvironment.SkipReason("ru") ?? TestEnvironment.SkipReason("en");
    }
}
