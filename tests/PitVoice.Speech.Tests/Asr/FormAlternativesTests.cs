using PitVoice.Core.Asr;
using PitVoice.Core.Parsing;
using PitVoice.Speech.Asr;
using Xunit;

namespace PitVoice.Speech.Tests.Asr;

/// <summary>Design D13: the share of the alternatives that read as the same word becomes its confidence.</summary>
public sealed class FormAlternativesTests
{
    private static string ReadAs(string word) => word switch
    {
        "левое" or "левая" => "левые",
        "переднее" => "передние",
        _ => word,
    };

    private static RecognizedPhrase Phrase(params (string Word, double Confidence)[] words) =>
        new(words.Select(w => new RecognizedWord(w.Word, w.Confidence)).ToArray());

    [Fact]
    public void Alternatives_that_differ_only_by_form_add_up()
    {
        // take-014 of the ru set: "меняй левые" 106.63, "меняй левое" 106.22.
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":106.630508,"result":[{"word":"меняй"},{"word":"левые"}],"text":"меняй левые"},
              {"confidence":106.222733,"result":[{"word":"меняй"},{"word":"левое"}],"text":"меняй левое"}]}
            """);

        var joined = VoskAsrEngine.JoinForms(Phrase(("меняй", 1.0), ("левые", 0.60)), alternatives, ReadAs);

        Assert.Equal(1.0, joined.Words[1].Confidence!.Value, precision: 6);
        Assert.Equal(1.0, joined.Words[0].Confidence);
    }

    [Fact]
    public void Alternative_with_a_different_word_does_not_add()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":10.0,"result":[{"word":"меняй"},{"word":"левые"}]},
              {"confidence":10.0,"result":[{"word":"меняй"},{"word":"правые"}]}]}
            """);

        var joined = VoskAsrEngine.JoinForms(Phrase(("меняй", 1.0), ("левые", 0.5)), alternatives, ReadAs);

        Assert.Equal(0.5, joined.Words[1].Confidence!.Value, precision: 6);
    }

    [Fact]
    public void Alternative_with_another_number_of_words_does_not_add()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":10.0,"result":[{"word":"меняй"},{"word":"левые"}]},
              {"confidence":10.0,"result":[{"word":"меняй"},{"word":"левое"},{"word":"переднее"}]}]}
            """);

        var joined = VoskAsrEngine.JoinForms(Phrase(("меняй", 1.0), ("левые", 0.5)), alternatives, ReadAs);

        Assert.Equal(0.5, joined.Words[1].Confidence!.Value, precision: 6);
    }

    [Fact]
    public void Confidence_is_never_lowered_and_unknown_words_stay_zero()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":10.0,"result":[{"word":"[unk]"},{"word":"шины"}]},
              {"confidence":12.0,"result":[{"word":"меняй"},{"word":"резину"}]}]}
            """);

        var joined = VoskAsrEngine.JoinForms(Phrase((SpeechRecognizer.UnknownWord, 0.0), ("шины", 0.9)), alternatives, ReadAs);

        Assert.Equal(0.0, joined.Words[0].Confidence);
        Assert.Equal(0.9, joined.Words[1].Confidence);
    }

    [Fact]
    public void Result_without_alternatives_keeps_the_phrase()
    {
        var phrase = Phrase(("меняй", 1.0), ("левые", 0.5));

        Assert.Empty(VoskAsrEngine.ParseAlternatives("""{"text":"меняй левые"}"""));
        Assert.Same(phrase, VoskAsrEngine.JoinForms(phrase, [], ReadAs));
    }
}
