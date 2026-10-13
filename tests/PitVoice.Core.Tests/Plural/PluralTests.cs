using PitVoice.Core.Plural;
using Xunit;

namespace PitVoice.Core.Tests.Plural;

public class PluralTests
{
    [Theory]
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Few)]
    [InlineData(5, PluralCategory.Many)]
    [InlineData(11, PluralCategory.Many)]
    [InlineData(21, PluralCategory.One)]
    [InlineData(22, PluralCategory.Few)]
    [InlineData(25, PluralCategory.Many)]
    [InlineData(111, PluralCategory.Many)]
    [InlineData(0, PluralCategory.Many)]
    [InlineData(12, PluralCategory.Many)]
    [InlineData(14, PluralCategory.Many)]
    [InlineData(101, PluralCategory.One)]
    [InlineData(104, PluralCategory.Few)]
    public void Russian(int number, PluralCategory expected)
    {
        Assert.Equal(expected, RuPluralRules.Instance.Select(number));
    }

    [Theory]
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Other)]
    [InlineData(5, PluralCategory.Other)]
    [InlineData(11, PluralCategory.Other)]
    [InlineData(21, PluralCategory.Other)]
    [InlineData(0, PluralCategory.Other)]
    public void English(int number, PluralCategory expected)
    {
        Assert.Equal(expected, EnPluralRules.Instance.Select(number));
    }

    [Fact]
    public void Rules_declare_the_categories_they_produce()
    {
        Assert.Equal([PluralCategory.One, PluralCategory.Few, PluralCategory.Many], RuPluralRules.Instance.Categories);
        Assert.Equal([PluralCategory.One, PluralCategory.Other], EnPluralRules.Instance.Categories);
    }
}
