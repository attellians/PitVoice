using PitVoice.Core.Asr;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Tests.Languages;
using Xunit;

namespace PitVoice.Core.Tests.Parsing;

/// <summary>
/// Ready phrase match (design D3 of replace-additions-with-phrase-builder). The spec's "давай на мокрые" already works
/// with the built-in words ("давай" is a filler), so the tests use "идет дождь", which only the ready phrase gives.
/// </summary>
public class ReadyPhraseTests
{
    private static readonly LanguagePack Pack = WithPhrase("идет дождь", IntentCatalog.TiresWet);

    private static LanguagePack WithPhrase(string text, IntentId intent)
    {
        var result = UserAdditions.Merge(TestPacks.Ru, UserAdditionsTests.Draft(phrases: [(text, intent)]));
        Assert.True(result.Applied, string.Join('\n', result.Issues));
        return result.Pack;
    }

    private static ParseResult Parse(string text) => new CommandParser(Pack).Parse(text);

    [Fact]
    public void Whole_phrase()
    {
        var accepted = Assert.IsType<ParseResult.Accepted>(Parse("идет дождь"));

        var command = Assert.Single(accepted.Commands);
        Assert.Equal(IntentCatalog.TiresWet, command.Intent);
        Assert.Equal(1.0, command.Confidence);
    }

    [Fact]
    public void Built_in_pack_does_not_understand_the_phrase()
    {
        Assert.False(new CommandParser(TestPacks.Ru).Parse("идет дождь").IsAccepted);
    }

    [Fact]
    public void Ready_phrase_in_a_compound_command()
    {
        var accepted = Assert.IsType<ParseResult.Accepted>(Parse("идет дождь и залей двадцать литров"));

        Assert.Equal([IntentCatalog.TiresWet, IntentCatalog.FuelAdd], accepted.Commands.Select(c => c.Intent));
        Assert.Equal(20, accepted.Commands[1].Amount);
    }

    [Fact]
    public void Filler_inside_a_ready_phrase()
    {
        var accepted = Assert.IsType<ParseResult.Accepted>(Parse("идёт ну дождь пожалуйста"));

        Assert.Equal(IntentCatalog.TiresWet, Assert.Single(accepted.Commands).Intent);
    }

    [Fact]
    public void Extra_word_is_parsed_by_word_classes()
    {
        var rejected = Assert.IsType<ParseResult.Rejected>(Parse("идет дождь передние"));

        Assert.Equal(RejectReason.NotUnderstood, rejected.Reason);
    }

    [Fact]
    public void Missing_word_is_parsed_by_word_classes()
    {
        Assert.False(Parse("дождь").IsAccepted);
    }

    [Fact]
    public void Low_confidence()
    {
        var phrase = new RecognizedPhrase([new RecognizedWord("идет", 0.95), new RecognizedWord("дождь", 0.4)]);

        var rejected = Assert.IsType<ParseResult.Rejected>(new CommandParser(Pack).Parse(phrase, new ParseOptions { ConfidenceThreshold = 0.6 }));

        Assert.Equal(RejectReason.LowConfidence, rejected.Reason);
    }

    [Fact]
    public void Conflict_with_another_part()
    {
        var rejected = Assert.IsType<ParseResult.Rejected>(Parse("идет дождь и сухие шины"));

        Assert.Equal(RejectReason.Conflict, rejected.Reason);
    }

    [Fact]
    public void Tire_targets_after_a_ready_phrase_carry_its_action()
    {
        var pack = WithPhrase("резина под замену", IntentCatalog.TiresLeft);

        var accepted = Assert.IsType<ParseResult.Accepted>(new CommandParser(pack).Parse("резина под замену и правые"));

        Assert.Equal([IntentCatalog.TiresLeft, IntentCatalog.TiresRight], accepted.Commands.Select(c => c.Intent));
    }

    [Fact]
    public void Words_of_ready_phrases_are_in_the_vocabulary()
    {
        var words = VocabularyBuilder.Build(Pack);

        Assert.Contains("идет", words);
        Assert.Contains("дождь", words);
        Assert.Equal(["дождь", "идет"], VocabularyBuilder.BuildUserAdditionWords(Pack));
        Assert.DoesNotContain("дождь", VocabularyBuilder.Build(TestPacks.Ru));
        Assert.Empty(VocabularyBuilder.BuildUserAdditionWords(TestPacks.Ru));
        Assert.Equal(VocabularyBuilder.Build(TestPacks.Ru).Count + 2, words.Count);
    }
}
