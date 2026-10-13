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
}
