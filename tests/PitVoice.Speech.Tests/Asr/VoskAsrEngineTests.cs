using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Parsing;
using PitVoice.Speech.Asr;
using PitVoice.Speech.Audio;
using Xunit;

namespace PitVoice.Speech.Tests.Asr;

public sealed class VoskAsrEngineTests
{
    private const string EnFixture = "en-change-all-tires-add-thirty-liters.wav";

    [Fact]
    public void Vosk_ParseResult_WordsWithConfidence()
    {
        const string json = """
            {"result":[{"conf":0.97,"end":0.5,"start":0.1,"word":"залей"},{"conf":0.88,"end":1.0,"start":0.5,"word":"тридцать"}],"text":"залей тридцать"}
            """;

        var phrase = VoskAsrEngine.ParseResult(json);

        Assert.Equal([new RecognizedWord("залей", 0.97), new RecognizedWord("тридцать", 0.88)], phrase.Words);
    }

    [Fact]
    public void Vosk_ParseResult_UnknownWordHasZeroConfidence()
    {
        const string json = """{"result":[{"conf":1.0,"word":"add"},{"conf":1.0,"word":"[unk]"},{"conf":1.0,"word":"liters"}],"text":"add [unk] liters"}""";

        var phrase = VoskAsrEngine.ParseResult(json);

        Assert.Equal(0.0, phrase.Words[1].Confidence);
        Assert.False(new Core.Parsing.CommandParser(TestEnvironment.Pack("en")).Parse(phrase).IsAccepted);
    }

    [Fact]
    public void Vosk_ParseResult_SilenceIsEmpty()
    {
        Assert.Empty(VoskAsrEngine.ParseResult("""{"text" : ""}""").Words);
    }

    [Fact]
    public async Task Vosk_ModelNotInstalled_ErrorWithModelId()
    {
        using var engine = new VoskAsrEngine();
        var pack = TestEnvironment.Pack("en");

        var result = await engine.LoadAsync(pack, null);
        var missingDirectory = await engine.LoadAsync(pack, Path.Combine(Path.GetTempPath(), "pitvoice-no-such-model"));

        Assert.False(result.IsLoaded);
        Assert.Equal($"model not installed: {pack.AsrModel}", result.Error);
        Assert.False(missingDirectory.IsLoaded);
        Assert.False(engine.IsLoaded);
    }

    [Fact]
    public async Task Vosk_GarbageDirectory_CorruptedErrorWithPath()
    {
        using var temp = new Audio.TempDirectory();
        File.WriteAllText(temp.File("junk.txt"), "not a model");
        using var engine = new VoskAsrEngine();

        var result = await engine.LoadAsync(TestEnvironment.Pack("en"), temp.Path);

        Assert.False(result.IsLoaded);
        Assert.Contains("corrupted", result.Error, StringComparison.Ordinal);
        Assert.Contains(temp.Path, result.Error, StringComparison.Ordinal);
        Assert.False(engine.IsLoaded);
    }

    [Fact]
    public async Task Vosk_ModelFilesWithGarbageContent_CorruptedError()
    {
        using var temp = new Audio.TempDirectory();
        Directory.CreateDirectory(temp.File("am"));
        Directory.CreateDirectory(temp.File("conf"));
        File.WriteAllText(temp.File(Path.Combine("am", "final.mdl")), "garbage");
        File.WriteAllText(temp.File(Path.Combine("conf", "model.conf")), "garbage");
        using var engine = new VoskAsrEngine();

        var result = await engine.LoadAsync(TestEnvironment.Pack("en"), temp.Path);

        Assert.False(result.IsLoaded);
        Assert.Contains("corrupted", result.Error, StringComparison.Ordinal);
        Assert.False(engine.IsLoaded);
    }

    [Fact]
    public void Vosk_NotLoaded_RecognizerReturnsError()
    {
        using var engine = new VoskAsrEngine();

        var result = new SpeechRecognizer(engine).Recognize(new AudioBuffer(new short[16000]));

        Assert.False(result.IsSuccess);
    }

    [ModelFact("en")]
    public async Task Vosk_RealModel_RecognizesFixtureAndParses()
    {
        using var engine = new VoskAsrEngine();
        var pack = TestEnvironment.Pack("en");
        Assert.True((await engine.LoadAsync(pack, TestEnvironment.RequireModel("en"))).IsLoaded);

        var result = new SpeechRecognizer(engine).Recognize(WavFile.Load(TestEnvironment.Fixture(EnFixture)));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("change all tires and add thirty liters", result.Phrase.Text);
        Assert.All(result.Phrase.Words, w => Assert.InRange(w.Confidence ?? -1, 0.0, 1.0));
        Assert.True(result.RecognitionTime > TimeSpan.Zero);
        var parsed = Assert.IsType<ParseResult.Accepted>(new CommandParser(pack).Parse(result.Phrase));
        Assert.Equal(["tires.all", "fuel.add"], parsed.Commands.Select(c => c.Intent.Value));
        Assert.Equal(30, parsed.Commands[1].Amount);
    }

    [ModelFact("en")]
    public async Task Vosk_ThreeRecognitions_ModelLoadedOnce()
    {
        var loads = 0;
        using var engine = new VoskAsrEngine(path =>
        {
            Interlocked.Increment(ref loads);
            return VoskModelHandle.Load(path);
        });
        var pack = TestEnvironment.Pack("en");
        var modelPath = TestEnvironment.RequireModel("en");
        var buffer = WavFile.Load(TestEnvironment.Fixture(EnFixture));
        var recognizer = new SpeechRecognizer(engine);

        await engine.LoadAsync(pack, modelPath);
        var results = Enumerable.Range(0, 3).Select(_ => recognizer.Recognize(buffer)).ToArray();
        await engine.LoadAsync(pack, modelPath); // same language: no reload

        Assert.Equal(1, loads);
        Assert.All(results, r => Assert.Equal("change all tires and add thirty liters", r.Phrase.Text));
    }

    [ModelFact("en")]
    public async Task Vosk_TwoSecondsOfSilence_EmptyText()
    {
        using var engine = new VoskAsrEngine();
        await engine.LoadAsync(TestEnvironment.Pack("en"), TestEnvironment.RequireModel("en"));

        var result = new SpeechRecognizer(engine).Recognize(new AudioBuffer(new short[2 * AudioBuffer.SampleRate]));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsEmpty);
        Assert.Equal(TimeSpan.FromSeconds(2), result.AudioDuration);
        Assert.Equal(RejectReason.Empty, Assert.IsType<ParseResult.Rejected>(new CommandParser(TestEnvironment.Pack("en")).Parse(result.Phrase)).Reason);
    }

    [ModelFact("ru")]
    public async Task Vosk_RealRuModel_UsesYoSpellingAndReportsMissingWords()
    {
        using var engine = new VoskAsrEngine();
        var pack = TestEnvironment.Pack("ru");

        await engine.LoadAsync(pack, TestEnvironment.RequireModel("ru"));
        var (report, error) = await VoskAsrEngine.CheckVocabularyAsync(pack, TestEnvironment.RequireModel("ru"));

        Assert.NotNull(engine.Vocabulary);
        Assert.Equal("ещё", engine.Vocabulary.Substituted["еще"]);
        Assert.DoesNotContain("еще", engine.Vocabulary.Missing);
        Assert.Null(error);
        Assert.Equal(engine.Vocabulary.Substituted, report!.Substituted);
        Assert.Equal(engine.Vocabulary.Missing, report.Missing);
    }

    [Fact]
    public async Task Vosk_CheckVocabulary_BadModel_Error()
    {
        using var temp = new Audio.TempDirectory();

        var (report, error) = await VoskAsrEngine.CheckVocabularyAsync(TestEnvironment.Pack("ru"), temp.Path);

        Assert.Null(report);
        Assert.Contains("corrupted", error, StringComparison.Ordinal);
    }

    [ModelFact("en")]
    public async Task Vosk_ModelPathWithCyrillic_Loads()
    {
        // Native strings must cross as UTF-8 whatever the Windows ANSI code page is (user names are often Cyrillic).
        using var temp = new Audio.TempDirectory();
        var target = Path.Combine(temp.Path, "модели пилота", "en");
        CopyDirectory(TestEnvironment.RequireModel("en"), target);
        using var engine = new VoskAsrEngine();

        var load = await engine.LoadAsync(TestEnvironment.Pack("en"), target);
        var result = new SpeechRecognizer(engine).Recognize(WavFile.Load(TestEnvironment.Fixture(EnFixture)));

        Assert.True(load.IsLoaded, load.Error);
        Assert.Equal("change all tires and add thirty liters", result.Phrase.Text);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    [ModelFact("ru")]
    public async Task Vosk_LanguageSwitch_Reloads()
    {
        var loads = new List<string>();
        using var engine = new VoskAsrEngine(path =>
        {
            loads.Add(path);
            return VoskModelHandle.Load(path);
        });

        await engine.LoadAsync(TestEnvironment.Pack("ru"), TestEnvironment.RequireModel("ru"));
        Assert.Equal("ru", engine.Language);
        var en = TestEnvironment.ModelPath("en");
        if (en is null)
        {
            return; // switching needs both models; the ru half is still checked
        }

        await engine.LoadAsync(TestEnvironment.Pack("en"), en);

        Assert.Equal("en", engine.Language);
        Assert.Equal(2, loads.Count);
    }
}
