using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Parsing;

public class NormalizerTests
{
    [Fact]
    public void Lower_case_punctuation_and_fillers()
    {
        var tokens = TestPacks.Ru.Normalizer.Normalize("Ну, залей, пожалуйста, тридцать литров!");

        Assert.Equal(["залей", "#30", "<liters>"], tokens.Select(t => t.ToString()));
    }

    [Fact]
    public void Yo_is_replaced_by_ye()
    {
        var tokens = TestPacks.Ru.Normalizer.Normalize("сорви плёнку ещё");

        Assert.Equal(["сорви", "пленку", "еще"], tokens.Select(t => t.Text));
    }

    [Fact]
    public void Only_fillers_leave_nothing()
    {
        Assert.Empty(TestPacks.Ru.Normalizer.Normalize("ну пожалуйста"));
        Assert.Empty(TestPacks.Ru.Normalizer.Normalize("..."));
    }

    [Fact]
    public void Number_words_are_folded_into_one_token()
    {
        var tokens = TestPacks.Ru.Normalizer.Normalize("залей сто двадцать пять литров");

        var number = Assert.IsType<NumberToken>(tokens[1]);
        Assert.Equal(125, number.Value);
        Assert.Equal("сто двадцать пять", number.Text);
        Assert.Equal(3, tokens.Count);
    }

    [Fact]
    public void Digits_are_numbers_too()
    {
        var tokens = TestPacks.Ru.Normalizer.Normalize("залей 40 литров");

        Assert.Equal(40, Assert.IsType<NumberToken>(tokens[1]).Value);
    }

    [Fact]
    public void Hyphenated_english_number_is_split_and_folded()
    {
        var tokens = TestPacks.En.Normalizer.Normalize("add thirty-five liters");

        Assert.Equal(35, Assert.IsType<NumberToken>(tokens[1]).Value);
        Assert.Equal("liters", Assert.IsType<UnitToken>(tokens[2]).Unit);
    }

    [Fact]
    public void Unit_synonyms_resolve_to_canonical_units()
    {
        Assert.Equal("liters", Assert.IsType<UnitToken>(TestPacks.Ru.Normalizer.Normalize("л")[0]).Unit);
        Assert.Equal("gallons", Assert.IsType<UnitToken>(TestPacks.Ru.Normalizer.Normalize("галлона")[0]).Unit);
        Assert.Equal("liters", Assert.IsType<UnitToken>(TestPacks.En.Normalizer.Normalize("litres")[0]).Unit);
    }

    [Fact]
    public void Word_confidence_is_preserved_and_numbers_take_the_minimum()
    {
        var phrase = new RecognizedPhrase(
        [
            new RecognizedWord("залей", 0.9),
            new RecognizedWord("двадцать", 0.8),
            new RecognizedWord("пять", 0.3),
            new RecognizedWord("литров"),
        ]);

        var tokens = TestPacks.Ru.Normalizer.Normalize(phrase);

        Assert.Equal(0.9, tokens[0].Confidence);
        Assert.Equal(0.3, tokens[1].Confidence);
        Assert.Equal(1.0, tokens[2].Confidence);
    }

    [Fact]
    public void Out_of_range_number_stays_a_word()
    {
        var tokens = TestPacks.Ru.Normalizer.Normalize("залей тысячу литров");

        Assert.IsType<WordToken>(tokens[1]);
        Assert.Equal("тысячу", tokens[1].Text);
    }

    [Fact]
    public void Split_words_handles_apostrophes_and_hyphens()
    {
        Assert.Equal(["don't", "tear", "off"], Normalizer.SplitWords("Don't tear-off"));
    }
}
