using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Responses;
using Xunit;

namespace PitVoice.Core.Tests.Responses;

public class OutcomeResponseTests
{
    private static string Respond(string language, string phrase, ExecutionOutcome outcome)
    {
        var pack = TestPacks.Get(language);
        var parsed = new CommandParser(pack).Parse(phrase);
        return new ResponseBuilder(pack).Build(parsed, ExecutionResult.Of(outcome));
    }

    [Theory]
    [InlineData("ru", "меняй шины и залей двадцать пять литров", ExecutionOutcome.Applied, "меняем все шины, заливаем 25 литров")]
    [InlineData("ru", "меняй шины и залей двадцать пять литров", ExecutionOutcome.PartiallyApplied, "команда не применилась")]
    [InlineData("ru", "быстрый ремонт", ExecutionOutcome.NotApplied, "команда не применилась")]
    [InlineData("ru", "залей двадцать литров", ExecutionOutcome.NotConnected, "нет связи с игрой")]
    [InlineData("ru", "меняй шины", ExecutionOutcome.NotInCar, "ты не в машине")]
    [InlineData("ru", "меняй левые и залей десять литров", ExecutionOutcome.Applied, "меняем левые, заливаем 10 литров")]
    [InlineData("ru", "залей ноль литров", ExecutionOutcome.Applied, "без топлива")]
    [InlineData("ru", "меняй шины и залей ноль", ExecutionOutcome.Applied, "меняем все шины, без топлива")]
    [InlineData("en", "add zero liters", ExecutionOutcome.Applied, "no fuel")]
    [InlineData("en", "change tires and add twenty five liters", ExecutionOutcome.Applied, "changing all tires, adding 25 liters")]
    [InlineData("en", "add ten gallons", ExecutionOutcome.Applied, "adding 10 gallons")]
    [InlineData("en", "change tires and fast repair", ExecutionOutcome.PartiallyApplied, "the command was not applied")]
    [InlineData("en", "fast repair", ExecutionOutcome.NotApplied, "the command was not applied")]
    [InlineData("en", "add twenty liters", ExecutionOutcome.NotConnected, "no connection to the game")]
    [InlineData("en", "change tires", ExecutionOutcome.NotInCar, "you are not in the car")]
    public void Response_by_outcome(string language, string phrase, ExecutionOutcome outcome, string expected)
    {
        Assert.Equal(expected, Respond(language, phrase, outcome));
    }

    [Theory]
    [InlineData("ru", "не понял, повтори")]
    [InlineData("en", "sorry, say again")]
    public void Rejected_phrase_is_not_understood_whatever_the_outcome(string language, string expected)
    {
        Assert.Equal(expected, Respond(language, "какая сегодня погода", ExecutionOutcome.Applied));
    }
}
