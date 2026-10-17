using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Parsing;

public class CommandParserTests
{
    private static ParseResult Parse(string language, string phrase, ParseOptions? options = null) =>
        new CommandParser(TestPacks.Get(language)).Parse(phrase, options);

    private static IReadOnlyList<ParsedCommand> Accepted(ParseResult result)
    {
        var accepted = Assert.IsType<ParseResult.Accepted>(result);
        return accepted.Commands;
    }

    private static ParseResult.Rejected Rejected(ParseResult result, RejectReason reason)
    {
        var rejected = Assert.IsType<ParseResult.Rejected>(result);
        Assert.Equal(reason, rejected.Reason);
        return rejected;
    }

    // --- Text normalization

    [Fact]
    public void Punctuation_and_fillers_removed()
    {
        var command = Assert.Single(Accepted(Parse("ru", "Ну, залей, пожалуйста, тридцать литров!")));
        Assert.Equal(IntentCatalog.FuelAdd, command.Intent);
        Assert.Equal(30, command.Amount);
        Assert.Equal(FuelUnits.Liters, command.Unit);
    }

    [Fact]
    public void Only_fillers_is_not_understood()
    {
        Rejected(Parse("ru", "ну пожалуйста"), RejectReason.NotUnderstood);
    }

    [Fact]
    public void Empty_input_is_empty()
    {
        Rejected(new CommandParser(TestPacks.Ru).Parse(RecognizedPhrase.Empty), RejectReason.Empty);
        Rejected(Parse("ru", "   "), RejectReason.Empty);
    }

    // --- Spoken numbers

    [Theory]
    [InlineData("ru", "залей сто двадцать пять литров", 125, FuelUnits.Liters)]
    [InlineData("ru", "залей двадцать один литр", 21, FuelUnits.Liters)]
    [InlineData("ru", "залей двадцать одна литр", 21, FuelUnits.Liters)]
    [InlineData("ru", "залей 40 литров", 40, FuelUnits.Liters)]
    [InlineData("en", "add thirty-five liters", 35, FuelUnits.Liters)]
    [InlineData("en", "add a hundred gallons", 100, FuelUnits.Gallons)]
    [InlineData("en", "put in one hundred and five liters", 105, FuelUnits.Liters)]
    public void Numbers_in_commands(string language, string phrase, int amount, string unit)
    {
        var command = Assert.Single(Accepted(Parse(language, phrase)));
        Assert.Equal(IntentCatalog.FuelAdd, command.Intent);
        Assert.Equal(amount, command.Amount);
        Assert.Equal(unit, command.Unit);
    }

    [Theory]
    [InlineData("ru", "залей тысячу литров")]
    [InlineData("ru", "залей 1000 литров")]
    [InlineData("en", "add a thousand liters")]
    [InlineData("en", "add one thousand liters")]
    [InlineData("ru", "залей двадцать тысяч литров")]
    public void Number_out_of_range_is_rejected(string language, string phrase)
    {
        Rejected(Parse(language, phrase), RejectReason.NotUnderstood);
    }

    // --- Word-class matching

    [Fact]
    public void Exact_match_has_confidence_one()
    {
        var command = Assert.Single(Accepted(Parse("ru", "добавь двадцать литров топлива")));
        Assert.Equal(1.0, command.Confidence);
    }

    [Fact]
    public void Optional_unit_defaults_from_options()
    {
        var gallons = new ParseOptions { DefaultUnit = FuelUnits.Gallons };
        var command = Assert.Single(Accepted(Parse("ru", "залей десять", gallons)));
        Assert.Equal(10, command.Amount);
        Assert.Equal(FuelUnits.Gallons, command.Unit);

        var liters = Assert.Single(Accepted(Parse("ru", "залей десять")));
        Assert.Equal(FuelUnits.Liters, liters.Unit);
    }

    [Fact]
    public void Negation_wins_by_specificity()
    {
        var command = Assert.Single(Accepted(Parse("ru", "не меняй шины")));
        Assert.Equal(IntentCatalog.TiresNone, command.Intent);
    }

    [Fact]
    public void Fuzzy_match_lowers_confidence()
    {
        var command = Assert.Single(Accepted(Parse("ru", "меняй все эти шины")));
        Assert.Equal(IntentCatalog.TiresAll, command.Intent);
        Assert.Equal(CommandParser.FuzzyQuality, command.Confidence);
    }

    [Fact]
    public void Too_distant_phrase_is_not_understood()
    {
        Rejected(Parse("ru", "какая сегодня погода"), RejectReason.NotUnderstood);
    }

    // --- Compound commands

    [Theory]
    [InlineData("ru", "меняй шины залей двадцать пять литров")]
    [InlineData("en", "change all tires add twenty five liters")]
    public void Compound_without_conjunction_splits_into_exact_parts(string language, string phrase)
    {
        var commands = Accepted(Parse(language, phrase));

        Assert.Equal([IntentCatalog.TiresAll, IntentCatalog.FuelAdd], commands.Select(c => c.Intent));
        Assert.Equal(25, commands[1].Amount);
        Assert.All(commands, c => Assert.Equal(1.0, c.Confidence));
    }

    [Fact]
    public void Compound_without_conjunction_needs_every_word_matched()
    {
        Rejected(Parse("ru", "меняй шины спой песню"), RejectReason.NotUnderstood);
    }

    [Fact]
    public void Compound_without_conjunction_inside_conjunction_part()
    {
        var commands = Accepted(Parse("ru", "меняй шины залей двадцать и протри стекло"));

        Assert.Equal([IntentCatalog.TiresAll, IntentCatalog.FuelAdd, IntentCatalog.WindshieldTearoff], commands.Select(c => c.Intent));
    }

    [Theory]
    [InlineData("ru", "перед")]
    [InlineData("ru", "стекло")]
    [InlineData("en", "front")]
    [InlineData("en", "reset")]
    public void Single_word_never_changes_the_pit_stop(string language, string phrase)
    {
        Assert.False(Parse(language, phrase).IsAccepted);
    }

    [Fact]
    public void Two_commands_keep_their_order()
    {
        var commands = Accepted(Parse("ru", "меняй шины и залей двадцать пять литров"));

        Assert.Equal(2, commands.Count);
        Assert.Equal(IntentCatalog.TiresAll, commands[0].Intent);
        Assert.Equal(IntentCatalog.FuelAdd, commands[1].Intent);
        Assert.Equal(25, commands[1].Amount);
        Assert.Equal(FuelUnits.Liters, commands[1].Unit);
    }

    [Fact]
    public void Multi_word_conjunction_is_tried_first()
    {
        var commands = Accepted(Parse("ru", "залей тридцать литров а также протри стекло"));

        Assert.Equal([IntentCatalog.FuelAdd, IntentCatalog.WindshieldTearoff], commands.Select(c => c.Intent));

        var en = Accepted(Parse("en", "change tires and then add fifty"));
        Assert.Equal([IntentCatalog.TiresAll, IntentCatalog.FuelAdd], en.Select(c => c.Intent));
    }

    [Fact]
    public void Three_commands()
    {
        var commands = Accepted(Parse("ru", "меняй шины плюс быстрый ремонт потом залей пятьдесят"));

        Assert.Equal([IntentCatalog.TiresAll, IntentCatalog.RepairFast, IntentCatalog.FuelAdd], commands.Select(c => c.Intent));
        Assert.Equal(50, commands[2].Amount);
    }

    [Fact]
    public void Conjunction_inside_a_number_does_not_split()
    {
        // "and" inside a spoken number is part of the number, not a conjunction.
        var whole = Assert.Single(Accepted(Parse("en", "fuel a hundred and five liters")));
        Assert.Equal(105, whole.Amount);
    }

    [Fact]
    public void One_unrecognized_part_rejects_the_whole_phrase()
    {
        var rejected = Rejected(Parse("ru", "меняй шины и спой песню"), RejectReason.NotUnderstood);
        Assert.Contains("спой песню", rejected.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_by_conjunctions_drops_empty_parts()
    {
        var parser = new CommandParser(TestPacks.Ru);
        var parts = parser.SplitByConjunctions(TestPacks.Ru.Normalizer.Normalize("и меняй шины и и залей десять и"));

        Assert.Equal(2, parts.Count);
        Assert.Equal(["меняй", "шины"], parts[0].Select(t => t.Text));
    }

    // --- Conflict resolution

    [Theory]
    [InlineData("ru", "меняй шины и не меняй шины")]
    [InlineData("ru", "залей двадцать и без топлива")]
    [InlineData("ru", "протри стекло и стекло не трогай")]
    [InlineData("ru", "быстрый ремонт и без ремонта")]
    [InlineData("ru", "меняй левые и не меняй шины")]
    [InlineData("en", "change tires and no tires")]
    public void Contradiction_is_a_conflict(string language, string phrase)
    {
        Rejected(Parse(language, phrase), RejectReason.Conflict);
    }

    [Fact]
    public void Duplicates_collapse()
    {
        var command = Assert.Single(Accepted(Parse("ru", "меняй шины и меняй шины")));
        Assert.Equal(IntentCatalog.TiresAll, command.Intent);
    }

    [Fact]
    public void Last_fuel_wins()
    {
        var command = Assert.Single(Accepted(Parse("ru", "залей двадцать и залей тридцать")));
        Assert.Equal(IntentCatalog.FuelAdd, command.Intent);
        Assert.Equal(30, command.Amount);
    }

    [Theory]
    [InlineData("ru", "сбрось все и меняй шины")]
    [InlineData("en", "change tires and clear all")]
    public void Standalone_intents_cannot_be_combined(string language, string phrase)
    {
        Rejected(Parse(language, phrase), RejectReason.Conflict);
    }

    [Fact]
    public void Non_conflicting_tire_commands_are_kept()
    {
        var commands = Accepted(Parse("ru", "меняй левые и меняй правые"));
        Assert.Equal([IntentCatalog.TiresLeft, IntentCatalog.TiresRight], commands.Select(c => c.Intent));
    }

    [Theory]
    [InlineData("ru", "меняй левые и не меняй переднее левое")]
    [InlineData("ru", "меняй переднее правое и не меняй правые")]
    [InlineData("en", "change left tires and no front left")]
    public void Same_wheel_added_and_removed_is_a_conflict(string language, string phrase)
    {
        Rejected(Parse(language, phrase), RejectReason.Conflict);
    }

    [Theory]
    [InlineData("ru", "меняй правые и не меняй левые", "tires.right", "tires.none.left")]
    [InlineData("ru", "меняй шины и не меняй переднее левое", null, null)]
    [InlineData("en", "change right tires and no left side tires", "tires.right", "tires.none.left")]
    public void Different_wheels_added_and_removed_are_kept(string language, string phrase, string? first, string? second)
    {
        var result = Parse(language, phrase);
        if (first is null)
        {
            // tires.all adds every wheel, so removing any wheel in the same phrase conflicts.
            Rejected(result, RejectReason.Conflict);
            return;
        }

        Assert.Equal([first, second], Accepted(result).Select(c => c.Intent.Value));
    }

    [Fact]
    public void Overlapping_tire_additions_do_not_conflict()
    {
        var resolution = ConflictResolver.Resolve(
        [
            new ParsedCommand(IntentCatalog.TiresLeft, null, null, 1.0),
            new ParsedCommand(IntentCatalog.TiresFront, null, null, 1.0),
        ]);

        Assert.False(resolution.HasConflict);
        Assert.Equal([IntentCatalog.TiresLeft, IntentCatalog.TiresFront], resolution.Commands!.Select(c => c.Intent));
        Assert.Equal(
            [IntentCatalog.TiresLeft, IntentCatalog.TiresFront],
            Accepted(Parse("ru", "меняй левые и меняй передние")).Select(c => c.Intent));
    }

    [Fact]
    public void Removing_the_same_wheel_twice_does_not_conflict()
    {
        var resolution = ConflictResolver.Resolve(
        [
            new ParsedCommand(IntentCatalog.TiresNoneLeft, null, null, 1.0),
            new ParsedCommand(IntentCatalog.TiresNoneLF, null, null, 1.0),
        ]);

        Assert.False(resolution.HasConflict);
    }

    [Fact]
    public void Resolver_keeps_the_last_fuel_add_in_place_and_first_of_others()
    {
        var commands = new[]
        {
            new ParsedCommand(IntentCatalog.FuelAdd, 20, FuelUnits.Liters, 1.0),
            new ParsedCommand(IntentCatalog.TiresAll, null, null, 1.0),
            new ParsedCommand(IntentCatalog.FuelAdd, 30, FuelUnits.Liters, 1.0),
            new ParsedCommand(IntentCatalog.TiresAll, null, null, 0.75),
        };

        var resolution = ConflictResolver.Resolve(commands);

        Assert.False(resolution.HasConflict);
        Assert.Equal(2, resolution.Commands!.Count);
        Assert.Equal(IntentCatalog.TiresAll, resolution.Commands[0].Intent);
        Assert.Equal(1.0, resolution.Commands[0].Confidence);
        Assert.Equal(30, resolution.Commands[1].Amount);
    }

    // --- Confidence threshold

    [Fact]
    public void Low_recognition_confidence_rejects()
    {
        var phrase = new RecognizedPhrase(
        [
            new RecognizedWord("залей", 0.95),
            new RecognizedWord("тридцать", 0.3),
            new RecognizedWord("литров", 0.9),
        ]);

        var rejected = Rejected(new CommandParser(TestPacks.Ru).Parse(phrase), RejectReason.LowConfidence);
        Assert.Contains("fuel.add", rejected.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_confidence_counts_as_one()
    {
        var command = Assert.Single(Accepted(Parse("ru", "залей тридцать литров")));
        Assert.Equal(1.0, command.Confidence);
    }

    [Fact]
    public void Confidence_is_match_quality_times_minimum_word_confidence()
    {
        var phrase = new RecognizedPhrase(
        [
            new RecognizedWord("меняй", 0.9),
            new RecognizedWord("все", 0.8),
            new RecognizedWord("эти", 0.95),
            new RecognizedWord("шины", 1.0),
        ]);

        var command = Assert.Single(Accepted(new CommandParser(TestPacks.Ru).Parse(phrase)));
        Assert.Equal(CommandParser.FuzzyQuality * 0.8, command.Confidence, precision: 10);
    }

    [Fact]
    public void Threshold_is_configurable_and_applies_to_every_part()
    {
        var phrase = new RecognizedPhrase(
        [
            new RecognizedWord("меняй", 1.0),
            new RecognizedWord("шины", 1.0),
            new RecognizedWord("и", 1.0),
            new RecognizedWord("залей", 0.7),
            new RecognizedWord("двадцать", 1.0),
        ]);
        var parser = new CommandParser(TestPacks.Ru);

        Assert.IsType<ParseResult.Accepted>(parser.Parse(phrase));
        Rejected(parser.Parse(phrase, new ParseOptions { ConfidenceThreshold = 0.8 }), RejectReason.LowConfidence);
    }

    // --- Determinism

    [Fact]
    public void Repeated_parse_gives_identical_results()
    {
        var parser = new CommandParser(TestPacks.Ru);
        var phrases = new[]
        {
            "меняй шины и залей двадцать пять литров",
            "меняй все эти шины",
            "меняй шины и спой песню",
            "залей двадцать и залей тридцать",
        };

        foreach (var phrase in phrases)
        {
            var first = parser.Parse(phrase);
            for (var i = 0; i < 100; i++)
            {
                var again = parser.Parse(phrase);
                Assert.Equal(first, again);
                Assert.Equal(first.ToString(), again.ToString());
            }
        }
    }

    // --- Spec scenario from the text command tool

    [Fact]
    public void Interactive_check_scenario()
    {
        var commands = Accepted(Parse("ru", "меняй левые и залей десять литров"));

        Assert.Equal(2, commands.Count);
        Assert.Equal("tires.left (1.00)", commands[0].ToString());
        Assert.Equal("fuel.add amount=10 unit=liters (1.00)", commands[1].ToString());
    }
}
