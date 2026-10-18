using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Parsing;

/// <summary>Scenarios of the command-parsing delta of reject-trailing-negation: a negation with nothing after it.</summary>
public class TrailingNegationTests
{
    private static ParseResult Parse(string language, string phrase) => new CommandParser(TestPacks.Get(language)).Parse(phrase);

    private static string[] Intents(string language, string phrase)
    {
        var accepted = Assert.IsType<ParseResult.Accepted>(Parse(language, phrase));
        return accepted.Commands.Select(c => c.Intent.Value).ToArray();
    }

    [Theory]
    [InlineData("en", "right side don't")]
    [InlineData("en", "tires keep")]
    [InlineData("en", "fuel no")]
    [InlineData("ru", "шины не")]
    [InlineData("ru", "залей двадцать и стекло не")]
    [InlineData("ru", "стекло не и залей двадцать")]
    public void Negation_at_the_end_rejects_the_phrase(string language, string phrase)
    {
        var rejected = Assert.IsType<ParseResult.Rejected>(Parse(language, phrase));
        Assert.Equal(RejectReason.NotUnderstood, rejected.Reason);
    }

    [Fact]
    public void Filler_after_the_negation_counts_as_a_word() => Assert.Equal(["fuel.none"], Intents("ru", "топливо не надо"));

    [Theory]
    [InlineData("ru", "шины не меняем")]
    [InlineData("ru", "не меняй шины")]
    [InlineData("en", "don't change tires")]
    [InlineData("en", "keep tires")]
    public void Negation_before_its_words(string language, string phrase) =>
        Assert.Equal(["tires.none"], Intents(language, phrase));

    [Theory]
    [InlineData("en", "add twenty don't change tires")]
    [InlineData("ru", "залей двадцать не меняй шины")]
    public void Lost_conjunction_before_a_negation(string language, string phrase) =>
        Assert.Equal(["fuel.add", "tires.none"], Intents(language, phrase));
}
