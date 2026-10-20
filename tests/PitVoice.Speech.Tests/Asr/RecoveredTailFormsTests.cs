using PitVoice.Core.Asr;
using PitVoice.Core.Audio;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Speech.Asr;
using Xunit;

namespace PitVoice.Speech.Tests.Asr;

/// <summary>
/// Design D3 of recover-dropped-phrase-tail: the second look at forms (FR-ASR-7) cannot undo the unknown word added for
/// an unrecognized tail, since the alternatives of the whole buffer have no tail and another number of words.
/// </summary>
public sealed class RecoveredTailFormsTests
{
    private static readonly AudioBuffer OneSecond = new(new short[AudioBuffer.SampleRate]);

    private static RecognizedPhrase Phrase(params (string Word, double Confidence)[] words) =>
        new(words.Select(w => new RecognizedWord(w.Word, w.Confidence)).ToArray());

    [Fact]
    public void Alternatives_without_the_tail_change_nothing()
    {
        var parser = new CommandParser(TestEnvironment.Pack("ru"));
        var phrase = Phrase(("меняем", 1.0), ("резину", 0.5), ("[unk]", 0.0));
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":120.0,"result":[{"word":"меняем"},{"word":"резину"}]},
              {"confidence":119.8,"result":[{"word":"меняем"},{"word":"резины"}]}]}
            """);

        var joined = VoskAsrEngine.JoinForms(phrase, alternatives, parser.ReadAs);
        var selected = VoskAsrEngine.SelectFormAlternatives(phrase, alternatives, parser.ReadAs);

        Assert.Equal(phrase.Words, joined.Words);
        Assert.Empty(selected);
    }

    [Fact]
    public void Phrase_with_an_unknown_tail_stays_rejected()
    {
        var pack = TestEnvironment.Pack("ru");
        var engine = new AlternativesEngine("""
            {"alternatives":[
              {"confidence":120.0,"result":[{"word":"меняем"},{"word":"резину"}]},
              {"confidence":119.8,"result":[{"word":"меняем"},{"word":"резины"}]}]}
            """);

        var result = SpeechParsing.Parse(
            new CommandParser(pack), new SpeechRecognizer(engine), OneSecond, Phrase(("меняем", 1.0), ("резину", 1.0), ("[unk]", 0.0)));

        Assert.False(result.FormsJoined);
        Assert.Equal(RejectReason.LowConfidence, Assert.IsType<ParseResult.Rejected>(result.Result).Reason);
    }

    [Fact]
    public void Second_look_at_a_word_with_forms_keeps_the_unknown_tail()
    {
        // "левые" is unsure of its form, so the second look is taken; the unknown word keeps confidence 0.
        var pack = TestEnvironment.Pack("ru");
        var engine = new AlternativesEngine("""
            {"alternatives":[
              {"confidence":120.0,"result":[{"word":"меняй"},{"word":"левые"}]},
              {"confidence":119.8,"result":[{"word":"меняй"},{"word":"левое"}]}]}
            """);

        var result = SpeechParsing.Parse(
            new CommandParser(pack), new SpeechRecognizer(engine), OneSecond, Phrase(("меняй", 1.0), ("левые", 0.5), ("[unk]", 0.0)));

        Assert.Equal(1, engine.FormCalls);
        Assert.Equal(0.0, result.Phrase.Words[2].Confidence);
        Assert.Equal(0.5, result.Phrase.Words[1].Confidence);
        Assert.Equal(RejectReason.LowConfidence, Assert.IsType<ParseResult.Rejected>(result.Result).Reason);
    }

    /// <summary>Answers the second look with the engine's own form logic over fixed alternatives of the whole buffer.</summary>
    private sealed class AlternativesEngine(string alternativesJson) : IAsrEngine
    {
        private readonly IReadOnlyList<(double Score, IReadOnlyList<string> Words)> _alternatives = VoskAsrEngine.ParseAlternatives(alternativesJson);

        public int FormCalls { get; private set; }

        public bool IsLoaded => true;

        public string? Language => "ru";

        public Task<AsrLoadResult> LoadAsync(LanguagePack pack, string? modelPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(AsrLoadResult.Loaded);

        public RecognizedPhrase Recognize(AudioBuffer buffer) => throw new NotSupportedException();

        public RecognizedPhrase? RecognizeForms(AudioBuffer buffer, RecognizedPhrase phrase, Func<string, string> readAs)
        {
            FormCalls++;
            return VoskAsrEngine.JoinForms(phrase, _alternatives, readAs);
        }

        public IReadOnlyList<RecognizedPhrase> RecognizeFormAlternatives(AudioBuffer buffer, RecognizedPhrase phrase, Func<string, string> readAs)
        {
            FormCalls++;
            return VoskAsrEngine.SelectFormAlternatives(phrase, _alternatives, readAs);
        }

        public void Unload()
        {
        }
    }
}
