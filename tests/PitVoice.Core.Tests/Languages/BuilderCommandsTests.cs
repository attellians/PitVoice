using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using Xunit;

namespace PitVoice.Core.Tests.Languages;

/// <summary>The commands of the "Own phrases" page (design D2 of replace-additions-with-phrase-builder).</summary>
public class BuilderCommandsTests
{
    [Fact]
    public void Order_and_contents_match_the_design_table()
    {
        Assert.Equal(
        [
            (BuilderCommand.Fuel, BuilderKind.Verb, "fuel.add"),
            (BuilderCommand.Fuel, BuilderKind.Phrase, "fuel.none"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.all"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.front"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.rear"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.left"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.right"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.lf"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.rf"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.lr"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.rr"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.dry"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.wet"),
            (BuilderCommand.Tires, BuilderKind.Phrase, "tires.none"),
            (BuilderCommand.Repair, BuilderKind.Phrase, "repair.fast"),
            (BuilderCommand.Windshield, BuilderKind.Phrase, "windshield.tearoff"),
            (BuilderCommand.Clear, BuilderKind.Phrase, "pit.clear"),
        ],
        BuilderCommands.All.Select(o => (o.Command, o.Kind, o.Intent.Value)));
    }

    [Fact]
    public void Every_intent_is_in_the_catalog()
    {
        Assert.All(BuilderCommands.All, o => Assert.True(IntentCatalog.Contains(o.Intent), o.Intent.Value));
    }

    [Fact]
    public void Commands_without_variants_have_one_choice_with_no_variant()
    {
        Assert.Equal([BuilderCommand.Fuel, BuilderCommand.Tires, BuilderCommand.Repair, BuilderCommand.Windshield, BuilderCommand.Clear], BuilderCommands.Commands);
        foreach (var command in new[] { BuilderCommand.Repair, BuilderCommand.Windshield, BuilderCommand.Clear })
        {
            Assert.Null(Assert.Single(BuilderCommands.Of(command)).Variant);
        }

        Assert.Equal(BuilderCommands.FuelVerb, BuilderCommands.Of(BuilderCommand.Fuel)[0]);
        Assert.Equal(12, BuilderCommands.Of(BuilderCommand.Tires).Count);
    }

    [Fact]
    public void The_verb_is_not_a_phrase_choice()
    {
        Assert.Null(BuilderCommands.PhraseOf(IntentCatalog.FuelAdd));
        Assert.Null(BuilderCommands.PhraseOf(IntentCatalog.TiresNoneLF));
        Assert.Equal(BuilderCommand.Tires, BuilderCommands.PhraseOf(IntentCatalog.TiresWet)!.Command);
    }
}
