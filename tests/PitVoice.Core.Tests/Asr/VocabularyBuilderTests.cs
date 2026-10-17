using PitVoice.Core.Asr;
using Xunit;

namespace PitVoice.Core.Tests.Asr;

public class VocabularyBuilderTests
{
    [Fact]
    public void Russian_vocabulary_contains_number_words_and_no_slots()
    {
        var words = VocabularyBuilder.Build(TestPacks.Ru);

        Assert.Contains("тридцать", words);
        Assert.Contains("пять", words);
        Assert.Contains("одна", words);
        Assert.Contains("две", words);
        Assert.Contains("сто", words);
        Assert.DoesNotContain(words, w => w.Contains('{') || w.Contains('}'));
        Assert.DoesNotContain("amount", words);
        Assert.DoesNotContain("unit", words);
    }

    [Fact]
    public void Vocabulary_contains_template_words_units_conjunctions_fillers_and_negations()
    {
        var words = VocabularyBuilder.Build(TestPacks.Ru);

        Assert.Contains("залей", words);
        Assert.Contains("литров", words);
        Assert.Contains("л", words);
        Assert.Contains("также", words);
        Assert.Contains("пожалуйста", words);
        Assert.Contains("не", words);
        Assert.Contains("пленку", words); // ё → е
        Assert.DoesNotContain("плёнку", words);
    }

    [Fact]
    public void English_vocabulary_contains_hundred_and_article()
    {
        var words = VocabularyBuilder.Build(TestPacks.En);

        Assert.Contains("hundred", words);
        Assert.Contains("a", words);
        Assert.Contains("litres", words);
        Assert.Contains("don't", words);
    }

    [Fact]
    public void Vocabulary_is_sorted_unique_and_deterministic()
    {
        var first = VocabularyBuilder.Build(TestPacks.Ru);
        var second = VocabularyBuilder.Build(TestPacks.Ru);

        Assert.Equal(first, second);
        Assert.Equal(first.Order(StringComparer.Ordinal), first);
        Assert.Equal(first.Distinct().Count(), first.Count);
        Assert.DoesNotContain(first, string.IsNullOrWhiteSpace);
        Assert.DoesNotContain(first, w => w.Contains(' '));
    }

    [Fact]
    public void Vocabulary_contains_every_verb_form_and_words_of_multi_word_entries()
    {
        var words = VocabularyBuilder.Build(TestPacks.Ru);

        Assert.Contains("заменяем", words);
        Assert.Contains("поменяй", words);
        Assert.Contains("заправить", words);
        Assert.Contains("долей", words);
        Assert.Contains("переднее", words);
        Assert.Contains("сторону", words);
        Assert.Contains("me", VocabularyBuilder.Build(TestPacks.En));
    }

    [Fact]
    public void Usage_names_the_classes_of_a_word()
    {
        Assert.Equal("tires.action, windshield.action", VocabularyBuilder.Usage(TestPacks.Ru, "смени"));
        Assert.Equal("tires.targets.lf, tires.targets.lr", VocabularyBuilder.Usage(TestPacks.Ru, "левое"));
        Assert.Equal("negations", VocabularyBuilder.Usage(TestPacks.Ru, "не"));
    }
}
