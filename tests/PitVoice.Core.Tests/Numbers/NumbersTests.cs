using PitVoice.Core.Numbers;
using Xunit;

namespace PitVoice.Core.Tests.Numbers;

public class NumbersTests
{
    [Theory]
    [InlineData("ноль", 0)]
    [InlineData("один", 1)]
    [InlineData("одна", 1)]
    [InlineData("два", 2)]
    [InlineData("две", 2)]
    [InlineData("пять", 5)]
    [InlineData("одиннадцать", 11)]
    [InlineData("девятнадцать", 19)]
    [InlineData("двадцать", 20)]
    [InlineData("двадцать один", 21)]
    [InlineData("двадцать одна", 21)]
    [InlineData("двадцать два", 22)]
    [InlineData("двадцать две", 22)]
    [InlineData("двадцать пять", 25)]
    [InlineData("девяносто девять", 99)]
    [InlineData("сто", 100)]
    [InlineData("сто один", 101)]
    [InlineData("сто одиннадцать", 111)]
    [InlineData("сто двадцать пять", 125)]
    [InlineData("девятьсот девяносто девять", 999)]
    [InlineData("40", 40)]
    [InlineData("0", 0)]
    [InlineData("999", 999)]
    public void Russian_numbers(string words, int expected)
    {
        var parts = words.Split(' ');
        Assert.True(RuNumberParser.Instance.TryParse(parts, out var value, out var consumed));
        Assert.Equal(expected, value);
        Assert.Equal(parts.Length, consumed);
    }

    [Theory]
    [InlineData("zero", 0)]
    [InlineData("one", 1)]
    [InlineData("two", 2)]
    [InlineData("five", 5)]
    [InlineData("eleven", 11)]
    [InlineData("nineteen", 19)]
    [InlineData("twenty", 20)]
    [InlineData("twenty one", 21)]
    [InlineData("twenty two", 22)]
    [InlineData("twenty five", 25)]
    [InlineData("ninety nine", 99)]
    [InlineData("hundred", 100)]
    [InlineData("a hundred", 100)]
    [InlineData("one hundred", 100)]
    [InlineData("one hundred one", 101)]
    [InlineData("one hundred and one", 101)]
    [InlineData("one hundred and five", 105)]
    [InlineData("one hundred eleven", 111)]
    [InlineData("one hundred and twenty five", 125)]
    [InlineData("nine hundred ninety nine", 999)]
    [InlineData("40", 40)]
    public void English_numbers(string words, int expected)
    {
        var parts = words.Split(' ');
        Assert.True(EnNumberParser.Instance.TryParse(parts, out var value, out var consumed));
        Assert.Equal(expected, value);
        Assert.Equal(parts.Length, consumed);
    }

    [Theory]
    [InlineData("тысяча")]
    [InlineData("тысячу")]
    [InlineData("1000")]
    [InlineData("литров")]
    [InlineData("-5")]
    [InlineData("")]
    public void Russian_rejects_non_numbers_and_out_of_range(string word)
    {
        Assert.False(RuNumberParser.Instance.TryParse([word], out _, out var consumed));
        Assert.Equal(0, consumed);
    }

    [Theory]
    [InlineData("thousand")]
    [InlineData("1000")]
    [InlineData("a")]
    [InlineData("and")]
    [InlineData("liters")]
    public void English_rejects_non_numbers_and_out_of_range(string word)
    {
        Assert.False(EnNumberParser.Instance.TryParse([word], out _, out var consumed));
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void Russian_stops_at_the_first_word_that_does_not_continue_the_number()
    {
        Assert.True(RuNumberParser.Instance.TryParse(["двадцать", "пять", "литров"], out var value, out var consumed));
        Assert.Equal(25, value);
        Assert.Equal(2, consumed);

        // Two tens in a row are two numbers, not one.
        Assert.True(RuNumberParser.Instance.TryParse(["двадцать", "тридцать"], out value, out consumed));
        Assert.Equal(20, value);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void English_does_not_swallow_a_conjunction_after_hundred()
    {
        Assert.True(EnNumberParser.Instance.TryParse(["one", "hundred", "and", "change"], out var value, out var consumed));
        Assert.Equal(100, value);
        Assert.Equal(2, consumed);
    }

    [Fact]
    public void English_article_is_a_number_only_before_hundred()
    {
        Assert.False(EnNumberParser.Instance.TryParse(["a", "tear", "off"], out _, out _));
    }

    [Fact]
    public void Parsers_expose_their_words()
    {
        Assert.Contains("тридцать", RuNumberParser.Instance.Words);
        Assert.Contains("одна", RuNumberParser.Instance.Words);
        Assert.Contains("сто", RuNumberParser.Instance.Words);
        Assert.Contains("hundred", EnNumberParser.Instance.Words);
        Assert.Contains("a", EnNumberParser.Instance.Words);
        Assert.DoesNotContain("40", RuNumberParser.Instance.Words);
    }
}
