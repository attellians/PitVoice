using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Asr;

public sealed class SpeechRecognizerTests
{
    private static readonly AudioBuffer OneSecond = new(new short[AudioBuffer.SampleRate]);

    [Fact]
    public void Asr_EmptyBuffer_EmptyResultWithoutEngineCall()
    {
        var engine = new FakeEngine("залей тридцать литров");

        var result = new SpeechRecognizer(engine).Recognize(AudioBuffer.Empty);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsEmpty);
        Assert.Equal(0, engine.Calls);
        Assert.Equal(RejectReason.Empty, Assert.IsType<ParseResult.Rejected>(new CommandParser(TestPacks.Ru).Parse(result.Phrase)).Reason);
    }

    [Fact]
    public void Asr_FakeEngine_TextReachesParser()
    {
        var engine = new FakeEngine("меняй шины и залей двадцать пять литров");

        var result = new SpeechRecognizer(engine).Recognize(OneSecond);
        var parsed = new CommandParser(TestPacks.Ru).Parse(result.Phrase);

        Assert.Equal(1, engine.Calls);
        Assert.Equal(TimeSpan.FromSeconds(1), result.AudioDuration);
        var accepted = Assert.IsType<ParseResult.Accepted>(parsed);
        Assert.Equal(["tires.all", "fuel.add"], accepted.Commands.Select(c => c.Intent.Value));
        Assert.Equal(25, accepted.Commands[1].Amount);
    }

    [Theory]
    [InlineData("ru", "залей [unk] литров")]
    [InlineData("ru", "[unk] меняй шины")]
    [InlineData("en", "add [unk] liters")]
    public void Asr_UnknownWord_ZeroConfidenceAndRejected(string language, string text)
    {
        var engine = new FakeEngine(text);

        var result = new SpeechRecognizer(engine).Recognize(OneSecond);

        var unknown = Assert.Single(result.Phrase.Words, w => w.Text == SpeechRecognizer.UnknownWord);
        Assert.Equal(0.0, unknown.Confidence);
        Assert.False(new CommandParser(TestPacks.Get(language)).Parse(result.Phrase).IsAccepted);
    }

    [Fact]
    public void Asr_LowWordConfidence_RejectedAsLowConfidence()
    {
        var engine = new FakeEngine(new RecognizedPhrase([new("залей", 0.95), new("тридцать", 0.4), new("литров", 0.9)]));

        var result = new SpeechRecognizer(engine).Recognize(OneSecond);
        var parsed = new CommandParser(TestPacks.Ru).Parse(result.Phrase, new ParseOptions { ConfidenceThreshold = 0.6 });

        Assert.Equal(RejectReason.LowConfidence, Assert.IsType<ParseResult.Rejected>(parsed).Reason);
    }

    [Fact]
    public void Asr_EngineThrows_ErrorResultAndNextRequestWorks()
    {
        var engine = new FakeEngine("меняй шины") { ThrowOnce = new InvalidOperationException("native crash") };
        var recognizer = new SpeechRecognizer(engine);

        var failed = recognizer.Recognize(OneSecond);
        var next = recognizer.Recognize(OneSecond);

        Assert.False(failed.IsSuccess);
        Assert.Contains("native crash", failed.Error, StringComparison.Ordinal);
        Assert.True(failed.IsEmpty);
        Assert.True(next.IsSuccess);
        Assert.Equal("меняй шины", next.Phrase.Text);
    }

    [Fact]
    public void Asr_EngineNotLoaded_ErrorWithoutCall()
    {
        var engine = new FakeEngine("меняй шины") { IsLoaded = false };

        var result = new SpeechRecognizer(engine).Recognize(OneSecond);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public void Asr_AudioBuffer_DurationFromSamples()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), new AudioBuffer(new short[32000]).Duration);
        Assert.Equal(TimeSpan.Zero, AudioBuffer.Empty.Duration);
        Assert.Equal(8 * 16000, AudioBuffer.MaxSamples);
    }

    private sealed class FakeEngine(RecognizedPhrase phrase) : IAsrEngine
    {
        public FakeEngine(string text)
            : this(RecognizedPhrase.FromText(text))
        {
        }

        public int Calls { get; private set; }

        public Exception? ThrowOnce { get; set; }

        public bool IsLoaded { get; set; } = true;

        public string? Language => "ru";

        public Task<AsrLoadResult> LoadAsync(LanguagePack pack, string? modelPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(AsrLoadResult.Loaded);

        public RecognizedPhrase Recognize(AudioBuffer buffer)
        {
            Calls++;
            if (ThrowOnce is { } error)
            {
                ThrowOnce = null;
                throw error;
            }

            return phrase;
        }

        public void Unload()
        {
        }
    }
}
