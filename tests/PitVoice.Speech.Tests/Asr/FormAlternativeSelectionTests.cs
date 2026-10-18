using PitVoice.Core.Asr;
using PitVoice.Core.Parsing;
using PitVoice.Speech.Asr;
using Xunit;

namespace PitVoice.Speech.Tests.Asr;

/// <summary>Design D2 of read-wheel-names-from-alternatives: only alternatives of forms close to the best are taken.</summary>
public sealed class FormAlternativeSelectionTests
{
    private static string ReadAs(string word) => word switch
    {
        "левое" or "левая" => "левые",
        "правое" or "правая" => "правые",
        "переднее" => "передние",
        _ => word,
    };

    private static RecognizedPhrase Phrase(params string[] words) =>
        new(words.Select(w => new RecognizedWord(w, 0.9)).ToArray());

    private static string Text(RecognizedPhrase phrase) => string.Join(' ', phrase.Words.Select(w => w.Text));

    [Fact]
    public void Alternative_of_forms_close_to_the_best_is_taken()
    {
        // The wheel name is second, 0.4 below (0.37 and 0.40 for take-022 and take-038 of the ru set).
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":120.4,"result":[{"word":"меняй"},{"word":"передние"},{"word":"левое"}]},
              {"confidence":120.0,"result":[{"word":"меняй"},{"word":"переднее"},{"word":"левое"}]}]}
            """);

        var selected = VoskAsrEngine.SelectFormAlternatives(Phrase("меняй", "передние", "левое"), alternatives, ReadAs);

        var alternative = Assert.Single(selected);
        Assert.Equal("меняй переднее левое", Text(alternative));
        Assert.All(alternative.Words, w => Assert.Equal(1.0, w.Confidence!.Value, precision: 6));
    }

    [Fact]
    public void Alternative_with_another_word_is_left_out()
    {
        // take-027: "и" added, "меня" instead of "меняй".
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":100.0,"result":[{"word":"меняй"},{"word":"левые"},{"word":"переднее"}]},
              {"confidence":99.9,"result":[{"word":"меня"},{"word":"левые"},{"word":"переднее"}]},
              {"confidence":99.2,"result":[{"word":"меняй"},{"word":"левые"},{"word":"и"},{"word":"переднее"}]}]}
            """);

        Assert.Empty(VoskAsrEngine.SelectFormAlternatives(Phrase("меняй", "левые", "переднее"), alternatives, ReadAs));
    }

    [Fact]
    public void Alternative_with_another_side_is_left_out()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":100.0,"result":[{"word":"смени"},{"word":"переднее"},{"word":"правая"}]},
              {"confidence":99.9,"result":[{"word":"смени"},{"word":"переднее"},{"word":"левое"}]}]}
            """);

        Assert.Empty(VoskAsrEngine.SelectFormAlternatives(Phrase("смени", "переднее", "правая"), alternatives, ReadAs));
    }

    [Fact]
    public void Alternative_with_another_number_of_words_is_left_out()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":100.0,"result":[{"word":"меняй"},{"word":"передние"},{"word":"левое"}]},
              {"confidence":99.9,"result":[{"word":"меняй"},{"word":"переднее"}]}]}
            """);

        Assert.Empty(VoskAsrEngine.SelectFormAlternatives(Phrase("меняй", "передние", "левое"), alternatives, ReadAs));
    }

    [Fact]
    public void Alternative_far_below_the_best_is_left_out()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":100.0,"result":[{"word":"меняй"},{"word":"передние"},{"word":"левое"}]},
              {"confidence":99.4,"result":[{"word":"меняй"},{"word":"переднее"},{"word":"левое"}]}]}
            """);

        Assert.Empty(VoskAsrEngine.SelectFormAlternatives(Phrase("меняй", "передние", "левое"), alternatives, ReadAs));
    }

    [Fact]
    public void Wheel_name_as_far_below_as_a_misheard_side_and_axle_is_left_out()
    {
        // take-025 of the ru set: said "меняй левые передние" (a side and an axle), the wheel name is 0.62 below.
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":100.0,"result":[{"word":"меняй"},{"word":"левые"},{"word":"переднее"}]},
              {"confidence":99.6,"result":[{"word":"меняй"},{"word":"левые"},{"word":"передние"}]},
              {"confidence":99.38,"result":[{"word":"меняй"},{"word":"левое"},{"word":"переднее"}]}]}
            """);

        var selected = VoskAsrEngine.SelectFormAlternatives(Phrase("меняй", "левые", "переднее"), alternatives, ReadAs);

        Assert.Equal(["меняй левые передние"], selected.Select(Text));
    }

    [Fact]
    public void The_phrase_itself_and_repeats_are_not_alternatives()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":100.0,"result":[{"word":"заднее"},{"word":"правая"}]},
              {"confidence":99.8,"result":[{"word":"заднее"},{"word":"правое"}]},
              {"confidence":99.7,"result":[{"word":"заднее"},{"word":"правое"}]},
              {"confidence":99.6,"result":[{"word":"заднее"},{"word":"правые"}]}]}
            """);

        var selected = VoskAsrEngine.SelectFormAlternatives(Phrase("заднее", "правая"), alternatives, ReadAs);

        Assert.Equal(["заднее правое", "заднее правые"], selected.Select(Text));
    }

    [Fact]
    public void Unknown_word_stays_unsure()
    {
        var alternatives = VoskAsrEngine.ParseAlternatives("""
            {"alternatives":[
              {"confidence":100.0,"result":[{"word":"[unk]"},{"word":"левая"}]},
              {"confidence":99.8,"result":[{"word":"[unk]"},{"word":"левое"}]}]}
            """);

        var alternative = Assert.Single(VoskAsrEngine.SelectFormAlternatives(Phrase(SpeechRecognizer.UnknownWord, "левая"), alternatives, ReadAs));

        Assert.Equal(0.0, alternative.Words[0].Confidence);
    }

    [Fact]
    public void Result_without_alternatives_gives_nothing()
    {
        Assert.Empty(VoskAsrEngine.SelectFormAlternatives(Phrase("меняй", "левое"), [], ReadAs));
    }
}
