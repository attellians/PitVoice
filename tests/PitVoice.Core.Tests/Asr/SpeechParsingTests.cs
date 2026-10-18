using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Asr;

/// <summary>Design D13: a word unsure only of its form gets a second look with the recognizer's alternatives.</summary>
public sealed class SpeechParsingTests
{
    private static readonly AudioBuffer OneSecond = new(new short[AudioBuffer.SampleRate]);

    private static RecognizedPhrase Phrase(params (string Word, double Confidence)[] words) =>
        new(words.Select(w => new RecognizedWord(w.Word, w.Confidence)).ToArray());

    private static SpeechParse Parse(FormsEngine engine, RecognizedPhrase phrase) =>
        SpeechParsing.Parse(new CommandParser(TestPacks.Ru), new SpeechRecognizer(engine), OneSecond, phrase);

    [Fact]
    public void Word_unsure_of_its_form_gets_a_second_look()
    {
        var engine = new FormsEngine { Joined = Phrase(("меняй", 1.0), ("левые", 0.98)) };

        var result = Parse(engine, Phrase(("меняй", 1.0), ("левые", 0.55)));

        Assert.Equal(1, engine.Calls);
        Assert.True(result.FormsJoined);
        Assert.Equal(IntentCatalog.TiresLeft, Assert.Single(Assert.IsType<ParseResult.Accepted>(result.Result).Commands).Intent);
    }

    [Fact]
    public void Low_confidence_of_a_word_without_forms_keeps_the_rejection()
    {
        var engine = new FormsEngine { Joined = Phrase(("залей", 1.0), ("тридцать", 1.0)) };

        var result = Parse(engine, Phrase(("залей", 1.0), ("тридцать", 0.4)));

        Assert.Equal(0, engine.Calls);
        Assert.False(result.FormsJoined);
        Assert.Equal(RejectReason.LowConfidence, Assert.IsType<ParseResult.Rejected>(result.Result).Reason);
    }

    [Fact]
    public void Accepted_phrase_needs_no_second_look()
    {
        var engine = new FormsEngine();

        var result = Parse(engine, Phrase(("меняй", 1.0), ("левые", 0.9)));

        Assert.Equal(0, engine.Calls);
        Assert.True(result.Result.IsAccepted);
    }

    [Fact]
    public void Other_rejections_need_no_second_look()
    {
        var engine = new FormsEngine();

        var result = Parse(engine, Phrase(("левые", 0.5)));

        Assert.Equal(0, engine.Calls);
        Assert.Equal(RejectReason.NotUnderstood, Assert.IsType<ParseResult.Rejected>(result.Result).Reason);
    }

    [Fact]
    public void Second_look_that_stays_unsure_keeps_the_rejection()
    {
        // The alternatives were split between "левые" and "правые": the word itself is unsure.
        var engine = new FormsEngine { Joined = Phrase(("меняй", 1.0), ("левые", 0.55)) };

        var result = Parse(engine, Phrase(("меняй", 1.0), ("левые", 0.55)));

        Assert.True(result.FormsJoined);
        Assert.Equal(RejectReason.LowConfidence, Assert.IsType<ParseResult.Rejected>(result.Result).Reason);
    }

    [Fact]
    public void Engine_without_alternatives_or_failing_keeps_the_first_result()
    {
        var phrase = Phrase(("меняй", 1.0), ("левые", 0.55));

        Assert.False(Parse(new FormsEngine { Joined = null }, phrase).FormsJoined);
        Assert.False(Parse(new FormsEngine { Throw = true }, phrase).FormsJoined);
        Assert.Equal(RejectReason.LowConfidence, Assert.IsType<ParseResult.Rejected>(Parse(new FormsEngine { Throw = true }, phrase).Result).Reason);
    }

    [Fact]
    public void Parser_reads_forms_as_their_word()
    {
        var parser = new CommandParser(TestPacks.Ru);

        Assert.Equal("левые", parser.ReadAs("Левое"));
        Assert.Equal("передние", parser.ReadAs("переднее"));
        Assert.Equal("шины", parser.ReadAs("шины"));
        Assert.True(parser.HasForms("левые"));
        Assert.True(parser.HasForms("правая"));
        Assert.False(parser.HasForms("шины"));
        Assert.False(new CommandParser(TestPacks.En).HasForms("left"));
    }

    private sealed class FormsEngine : IAsrEngine
    {
        public RecognizedPhrase? Joined { get; init; }

        public bool Throw { get; init; }

        public int Calls { get; private set; }

        public bool IsLoaded => true;

        public string? Language => "ru";

        public Task<AsrLoadResult> LoadAsync(LanguagePack pack, string? modelPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(AsrLoadResult.Loaded);

        public RecognizedPhrase Recognize(AudioBuffer buffer) => RecognizedPhrase.Empty;

        public RecognizedPhrase? RecognizeForms(AudioBuffer buffer, RecognizedPhrase phrase, Func<string, string> readAs)
        {
            Calls++;
            return Throw ? throw new InvalidOperationException("broken") : Joined;
        }

        public void Unload()
        {
        }
    }
}
