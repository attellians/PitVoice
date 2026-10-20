using PitVoice.Core.Asr;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Parsing;

/// <summary>
/// Scenarios of the command-parsing and language-packs deltas of add-tire-compound: compound commands, the transition
/// word without a compound, conflicts of compounds and the built-in compound words.
/// </summary>
public class TireCompoundParsingTests
{
    private static ParseResult Parse(string language, string phrase) => new CommandParser(TestPacks.Get(language)).Parse(phrase);

    private static string[] Intents(string language, string phrase)
    {
        var accepted = Assert.IsType<ParseResult.Accepted>(Parse(language, phrase));
        return accepted.Commands.Select(c => c.Intent.Value).ToArray();
    }

    private static void Rejected(string language, string phrase, RejectReason reason = RejectReason.NotUnderstood)
    {
        var rejected = Assert.IsType<ParseResult.Rejected>(Parse(language, phrase));
        Assert.Equal(reason, rejected.Reason);
    }

    // --- Tire compound commands

    [Theory]
    [InlineData("меняем резину на дождевые")]
    [InlineData("дождевые шины")]
    [InlineData("на дождевые")]
    [InlineData("меняй все на мокрые")]
    [InlineData("меняем резину на дождевую")]               // a form alone is its word
    public void Change_to_wets(string phrase) => Assert.Equal(["tires.wet"], Intents("ru", phrase));

    [Theory]
    [InlineData("ru", "меняй шины на сухие")]
    [InlineData("en", "change to slicks")]
    public void Change_to_dry(string language, string phrase) => Assert.Equal(["tires.dry"], Intents(language, phrase));

    [Fact]
    public void Compound_with_fuel()
    {
        var commands = Assert.IsType<ParseResult.Accepted>(Parse("ru", "меняй на дождевые и залей двадцать литров")).Commands;
        Assert.Equal(["tires.wet", "fuel.add"], commands.Select(c => c.Intent.Value));
        Assert.Equal(20, commands[1].Amount);
        Assert.Equal(FuelUnits.Liters, commands[1].Unit);
    }

    [Theory]
    [InlineData("ru", "дождевые")]
    [InlineData("en", "wets")]
    public void Compound_word_alone(string language, string phrase) => Rejected(language, phrase);

    [Theory]
    [InlineData("ru", "меняй левые на дождевые")]
    [InlineData("ru", "меняй переднее левое на сухие")]
    [InlineData("en", "change left to wets")]
    public void Compound_on_part_of_the_wheels(string language, string phrase) => Rejected(language, phrase);

    [Theory]
    [InlineData("ru", "не меняй на дождевые")]
    [InlineData("ru", "дождевые не трогай")]
    [InlineData("en", "no wet tires")]
    public void Negated_compound(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Two_compounds_in_one_piece() => Rejected("ru", "меняй дождевые сухие");

    [Fact]
    public void Compound_with_a_number() => Rejected("ru", "меняй на дождевые двадцать");

    [Fact]
    public void Unsupported_compound() => Rejected("en", "change to intermediates");

    [Fact]
    public void Tire_action_carries_over_after_the_conjunction() =>
        Assert.Equal(["tires.left", "tires.wet"], Intents("ru", "меняй левые и на дождевые"));

    // --- Transition word without a compound

    [Theory]
    [InlineData("ru", "меняем резину на")]                 // the recognizer lost the compound ("слики" is heard as "слик")
    [InlineData("ru", "залей двадцать на и меняй шины")]
    [InlineData("en", "change tires to")]
    public void Transition_word_without_a_compound(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Transition_word_with_a_compound() => Assert.Equal(["tires.dry"], Intents("ru", "меняем резину на сухие"));

    // --- Homophone of the transition word (design D10)

    [Fact]
    public void To_heard_as_two()
    {
        var phrase = new RecognizedPhrase([new RecognizedWord("change", 1.0), new RecognizedWord("two", 0.5), new RecognizedWord("slicks", 1.0)]);

        var command = Assert.Single(Assert.IsType<ParseResult.Accepted>(new CommandParser(TestPacks.En).Parse(phrase)).Commands);

        Assert.Equal("tires.dry", command.Intent.Value);
        Assert.Equal(1.0, command.Confidence);
    }

    [Fact]
    public void Homophone_with_a_part_of_the_wheels() => Rejected("en", "change left two wets");

    [Fact]
    public void Homophone_elsewhere_stays_a_number()
    {
        var command = Assert.Single(Assert.IsType<ParseResult.Accepted>(Parse("en", "add two liters")).Commands);
        Assert.Equal(2, command.Amount);
        Assert.Equal(FuelUnits.Liters, command.Unit);
    }

    [Fact]
    public void Multi_word_number_is_no_homophone() => Rejected("en", "change twenty two slicks");

    [Fact]
    public void English_homophone() => Assert.Contains("two", TestPacks.En.IntoHomophones);

    // --- Conflicts

    [Fact]
    public void Two_compounds() => Rejected("ru", "меняй на дождевые и на сухие", RejectReason.Conflict);

    [Fact]
    public void Compound_with_a_removal() => Rejected("ru", "меняй резину на дождевые и не меняй левые", RejectReason.Conflict);

    [Fact]
    public void Compound_with_a_tire_addition() =>
        Assert.Equal(["tires.wet", "tires.left"], Intents("ru", "меняй на дождевые и меняй левые"));

    [Fact]
    public void Same_compound_twice_collapses() =>
        Assert.Equal(["tires.wet"], Intents("ru", "меняй на дождевые и на мокрые"));

    // --- Language packs

    [Fact]
    public void Same_compound_rules_for_both_languages()
    {
        Assert.Equal(["tires.wet"], Intents("ru", "меняем резину на дождевые"));
        Assert.Equal(["tires.wet"], Intents("en", "change tires to wets"));
    }

    [Fact]
    public void Compound_words_are_command_words()
    {
        foreach (var word in new[] { "на", "дождевые", "сухие" })
        {
            Assert.DoesNotContain(word, TestPacks.Ru.RejectWords);
        }

        foreach (var word in new[] { "wet", "dry", "slicks" })
        {
            Assert.DoesNotContain(word, TestPacks.En.RejectWords);
        }

        Assert.Contains("intermediates", TestPacks.En.RejectWords);
    }

    [Fact]
    public void Russian_compound_words()
    {
        var vocabulary = VocabularyBuilder.Build(TestPacks.Ru);
        Assert.Contains("сухие", vocabulary);
        Assert.Contains("слик", vocabulary);
        Assert.Contains("дождевые", vocabulary);
        Assert.Contains("мокрые", vocabulary);
        Assert.Contains("на", vocabulary);
    }

    [Fact]
    public void English_compound_words()
    {
        var vocabulary = VocabularyBuilder.Build(TestPacks.En);
        foreach (var word in new[] { "dry", "slicks", "wet", "wets", "rain", "to" })
        {
            Assert.Contains(word, vocabulary);
        }
    }

    [Fact]
    public void Word_missing_from_the_model_is_not_in_the_pack()
    {
        // "слики" is not in vosk-model-small-ru-0.22, only "слик"; the check against the model itself runs in the speech tests.
        Assert.DoesNotContain("слики", VocabularyBuilder.Build(TestPacks.Ru));
    }

    [Theory]
    [InlineData("на слик")]
    [InlineData("меняем резину на слик")]
    [InlineData("меняем резину на слик и")]                // "слики" heard as "слик и": the dangling conjunction is dropped
    public void Slick_in_the_model_form(string phrase) => Assert.Equal(["tires.dry"], Intents("ru", phrase));

    [Theory]
    [InlineData("слик")]
    [InlineData("меняй левые на слик")]
    public void Slick_alone_or_on_part_of_the_tires(string phrase) => Rejected("ru", phrase);

    [Fact]
    public void Both_packs_have_compound_commands()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            Assert.Contains(PitVoice.Core.Intents.IntentCatalog.TiresDry, TestPacks.Get(language).Intents);
            Assert.Contains(PitVoice.Core.Intents.IntentCatalog.TiresWet, TestPacks.Get(language).Intents);
        }
    }
}
