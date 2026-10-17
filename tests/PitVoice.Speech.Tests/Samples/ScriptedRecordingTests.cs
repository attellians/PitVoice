using NAudio.Wave;
using PitVoice.Core;
using AudioBuffer = PitVoice.Core.Audio.AudioBuffer;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Speech.Samples;
using Xunit;

namespace PitVoice.Speech.Tests.Samples;

/// <summary>Spec: Scripted sample recording — the logic of <c>listen --script</c> with a fake microphone and console.</summary>
public sealed class ScriptedRecordingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pitvoice-script-" + Guid.NewGuid().ToString("N"));
    private int _recordings;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static readonly IReadOnlyList<SampleEntry> Script =
    [
        new(null, "меняй шины и залей двадцать пять литров", [new(IntentCatalog.TiresAll, null, null), new(IntentCatalog.FuelAdd, 25, FuelUnits.Liters)]),
        new(null, "какая сегодня погода", null),
        new(null, "сорви плёнку", [new(IntentCatalog.WindshieldTearoff, null, null)]),
    ];

    private ScriptedRecordingResult Run(int from, params ScriptKey[] keys)
    {
        var console = new FakeConsole(keys);
        var recording = new ScriptedRecording(console, () =>
        {
            _recordings++;
            return new AudioBuffer(new short[AudioBuffer.SampleRate / 2]);
        }, _ => "heard something");
        return recording.Run(Script, _directory, from);
    }

    private string SamplesFile => Path.Combine(_directory, SampleFile.FileName);

    [Fact]
    public void Take_saved_as_wav_and_described_in_samples_yaml()
    {
        var result = Run(1, ScriptKey.Enter, ScriptKey.Enter, ScriptKey.Quit);

        Assert.Equal(["take-001.wav"], result.Saved);
        Assert.True(result.Quit);
        using (var reader = new WaveFileReader(Path.Combine(_directory, "take-001.wav")))
        {
            Assert.Equal(16000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        }

        var entry = Assert.Single(SampleFile.Load(SamplesFile, requireFile: true));
        Assert.Equal("take-001.wav", entry.File);
        Assert.Equal(Script[0].Say, entry.Say);
        Assert.Equal(Script[0].Expect, entry.Expect);
    }

    [Fact]
    public void Retake_and_skip_save_nothing_unconfirmed()
    {
        // Phrase 1: record, re-record, save. Phrase 2: record, skip. Phrase 3: skip before recording.
        var result = Run(1, ScriptKey.Enter, ScriptKey.Retake, ScriptKey.Enter, ScriptKey.Enter, ScriptKey.Skip, ScriptKey.Skip);

        Assert.Equal(["take-001.wav"], result.Saved);
        Assert.Equal([2, 3], result.Skipped);
        Assert.Equal(3, _recordings);
        Assert.Equal(["take-001.wav"], Directory.GetFiles(_directory, "*.wav").Select(Path.GetFileName));
        Assert.Single(SampleFile.Load(SamplesFile, requireFile: true));
    }

    [Fact]
    public void Resume_from_a_number_keeps_existing_takes()
    {
        Run(1, ScriptKey.Enter, ScriptKey.Enter, ScriptKey.Enter, ScriptKey.Enter, ScriptKey.Quit);
        var before = File.ReadAllBytes(Path.Combine(_directory, "take-001.wav"));
        var samplesBefore = File.ReadAllText(SamplesFile);

        var result = Run(3, ScriptKey.Enter, ScriptKey.Enter);

        Assert.Equal(["take-003.wav"], result.Saved);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_directory, "take-001.wav")));
        Assert.StartsWith(samplesBefore, File.ReadAllText(SamplesFile), StringComparison.Ordinal);
        Assert.Equal(["take-001.wav", "take-002.wav", "take-003.wav"], SampleFile.Load(SamplesFile, requireFile: true).Select(e => e.File));
    }

    [Fact]
    public void Existing_take_is_never_overwritten()
    {
        Run(1, ScriptKey.Enter, ScriptKey.Enter, ScriptKey.Quit);

        var result = Run(1, ScriptKey.Quit);

        Assert.Empty(result.Saved);
        Assert.Contains(1, result.Skipped);
        Assert.Single(SampleFile.Load(SamplesFile, requireFile: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_save_key_is_an_error_before_anything_opens(string? directory)
    {
        var problem = ScriptedRecording.CheckArguments("script.ru.yaml", directory, 1, 10);

        Assert.NotNull(problem);
        Assert.Contains("--save-wav", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void From_out_of_range_is_an_error() =>
        Assert.NotNull(ScriptedRecording.CheckArguments("script.ru.yaml", _directory, 11, 10));

    [Fact]
    public void Script_with_an_unknown_intent_names_the_file_and_line()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "script.ru.yaml");
        File.WriteAllText(path, "samples:\n  - say: \"меняй шины\"\n    expect: [{ intent: tires.all }]\n  - say: \"протри стекло\"\n    expect: [{ intent: windshield.tearoff }]\n");
        var minimal = LanguagePackLoader.LoadFile(Path.Combine(TestEnvironment.BaseDirectory, "languages", "ru.yaml")).Pack!;

        Assert.Empty(SampleFile.CheckIntents(SampleFile.Load(path, requireFile: false), minimal, path));

        File.WriteAllText(path, "samples:\n  - say: \"переверни шины\"\n    expect: [{ intent: tires.flip }]\n");
        var error = Assert.Throws<SampleFileException>(() => SampleFile.Load(path, requireFile: false));
        Assert.Contains($"{path}:2", error.Message, StringComparison.Ordinal);
        Assert.Contains("tires.flip", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_script_file_is_an_error() =>
        Assert.Throws<SampleFileException>(() => SampleFile.Load(Path.Combine(_directory, "none.yaml"), requireFile: false));

    private sealed class FakeConsole(IEnumerable<ScriptKey> keys) : IScriptConsole
    {
        private readonly Queue<ScriptKey> _keys = new(keys);

        public ScriptKey ReadKey() => _keys.Count > 0 ? _keys.Dequeue() : ScriptKey.Quit;

        public void WriteLine(string text)
        {
        }
    }
}
