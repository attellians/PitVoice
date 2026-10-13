using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using PitVoice.Core.Responses;
using Xunit;

namespace PitVoice.Core.Tests.Responses;

public class ResponseBuilderTests
{
    [Theory]
    [InlineData(21, "21 литр")]
    [InlineData(22, "22 литра")]
    [InlineData(25, "25 литров")]
    [InlineData(1, "1 литр")]
    [InlineData(11, "11 литров")]
    [InlineData(111, "111 литров")]
    public void Russian_plural_forms(int amount, string expected)
    {
        var builder = new ResponseBuilder(TestPacks.Ru);
        var text = builder.Build(new ParsedCommand(IntentCatalog.FuelAdd, amount, FuelUnits.Liters, 1.0));

        Assert.Equal($"заливаем {expected}", text);
    }

    [Theory]
    [InlineData(1, "1 gallon")]
    [InlineData(30, "30 gallons")]
    [InlineData(0, "0 gallons")]
    public void English_plural_forms(int amount, string expected)
    {
        var builder = new ResponseBuilder(TestPacks.En);
        var text = builder.Build(new ParsedCommand(IntentCatalog.FuelAdd, amount, FuelUnits.Gallons, 1.0));

        Assert.Equal($"adding {expected}", text);
    }

    [Fact]
    public void Compound_responses_are_joined_in_order()
    {
        var parser = new CommandParser(TestPacks.Ru);
        var result = parser.Parse("меняй левые и залей десять литров");

        var text = new ResponseBuilder(TestPacks.Ru).Build(result);

        Assert.Equal("меняем левые, заливаем 10 литров", text);
    }

    [Fact]
    public void Rejection_yields_not_understood()
    {
        var parser = new CommandParser(TestPacks.Ru);
        var builder = new ResponseBuilder(TestPacks.Ru);

        Assert.Equal("не понял, повтори", builder.Build(parser.Parse("какая сегодня погода")));
        Assert.Equal("не понял, повтори", builder.Build(parser.Parse("меняй шины и не меняй шины")));
        Assert.Equal("sorry, say again", new ResponseBuilder(TestPacks.En).Build(new ParseResult.Rejected(RejectReason.Empty, "")));
    }

    [Fact]
    public void Intent_without_slots_uses_its_plain_response()
    {
        var builder = new ResponseBuilder(TestPacks.En);
        Assert.Equal("changing all tires", builder.Build(new ParsedCommand(IntentCatalog.TiresAll, null, null, 1.0)));
    }
}
