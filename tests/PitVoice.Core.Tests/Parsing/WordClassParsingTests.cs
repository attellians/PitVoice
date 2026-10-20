using PitVoice.Core.Asr;
using PitVoice.Core.Intents;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Parsing;

/// <summary>
/// Scenarios of the command-parsing and language-packs deltas of refine-command-phrases: word classes, free word
/// order, several targets, a lost conjunction, the elided tire action, integers only and the built-in vocabularies.
/// </summary>
public class WordClassParsingTests
{
    private static ParseResult Parse(string language, string phrase) => new CommandParser(TestPacks.Get(language)).Parse(phrase);

    private static string[] Intents(string language, string phrase)
    {
        var result = Parse(language, phrase);
        var accepted = Assert.IsType<ParseResult.Accepted>(result);
        return accepted.Commands.Select(c => c.Intent.Value).ToArray();
    }

    private static void Rejected(string language, string phrase, RejectReason reason = RejectReason.NotUnderstood)
    {
        var result = Parse(language, phrase);
        var rejected = Assert.IsType<ParseResult.Rejected>(result);
        Assert.Equal(reason, rejected.Reason);
    }

    // --- Word-class matching

    [Fact]
    public void Free_word_order()
    {
        Assert.Equal(["tires.all"], Intents("ru", "шины меняй"));
        Assert.Equal(["tires.all"], Intents("ru", "меняй шины"));
    }

    [Theory]
    [InlineData("не меняй шины")]
    [InlineData("шины не меняем")]
    [InlineData("без шин")]
    public void Negation_gives_removal(string phrase) => Assert.Equal(["tires.none"], Intents("ru", phrase));

    [Theory]
    [InlineData("топливо не надо", "fuel.none")]
    [InlineData("без ремонта", "repair.none")]
    public void Negation_without_verb(string phrase, string intent) => Assert.Equal([intent], Intents("ru", phrase));

    [Fact]
    public void Fuzzy_match_within_one_word()
    {
        var command = Assert.Single(((ParseResult.Accepted)Parse("ru", "меняй все эти шины")).Commands);
        Assert.Equal(IntentCatalog.TiresAll, command.Intent);
        Assert.True(command.Confidence < 1.0);
    }

    [Theory]
    [InlineData("меняй шины эти")]          // two words and one unknown is fine (three tokens)
    public void One_unknown_word_needs_three_tokens(string phrase) => Assert.Equal(["tires.all"], Intents("ru", phrase));

    [Theory]
    [InlineData("меняй эти")]
    [InlineData("меняй мои эти шины")]
    public void Unknown_words_beyond_the_allowance_reject(string phrase) => Rejected("ru", phrase);

    [Theory]
    [InlineData("смени плёнку", "windshield.tearoff")]
    [InlineData("смени передние", "tires.front")]
    public void Shared_word_resolved_by_context(string phrase, string intent) => Assert.Equal([intent], Intents("ru", phrase));

    [Fact]
    public void Ambiguous_piece_is_rejected() => Rejected("ru", "сбрось все шины");

    [Fact]
    public void Ambiguous_reading_of_multi_word_targets_is_rejected() => Rejected("en", "left front right");

    [Fact]
    public void Too_distant_phrase() => Rejected("ru", "какая сегодня погода");

    [Fact]
    public void Keep_without_negation_means_nothing() => Rejected("ru", "трогай шины");

    // --- No single-word patterns for pit changes

    [Theory]
    [InlineData("ru", "передние")]
    [InlineData("ru", "почини")]
    [InlineData("ru", "отмена")]
    [InlineData("ru", "стоп")]
    [InlineData("ru", "сбрось")]
    [InlineData("en", "fronts")]
    [InlineData("en", "tear")]
    public void Single_word_is_rejected(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Two_words_are_enough() => Assert.Equal(["repair.none"], Intents("ru", "не чини"));

    // --- Numbers: integers only

    [Theory]
    [InlineData("ru", "залей тридцать точка два")]
    [InlineData("ru", "залей 30.2 литра")]
    [InlineData("ru", "залей тридцать и два литра")]
    [InlineData("en", "add thirty point two")]
    public void Fraction_rejected(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Two_numbers_in_one_part_are_rejected() => Rejected("ru", "залей двадцать литров тридцать");

    // --- Several targets per action

    [Theory]
    [InlineData("меняй левые и передние шины", new[] { "tires.left", "tires.front" })]
    [InlineData("меняй переднее левое заднее правое", new[] { "tires.lf", "tires.rr" })]
    [InlineData("не меняй переднее левое заднее правое", new[] { "tires.none.lf", "tires.none.rr" })]
    public void Several_targets_per_action(string phrase, string[] intents) => Assert.Equal(intents, Intents("ru", phrase));

    [Fact]
    public void Leftover_part_of_a_target_is_rejected() => Rejected("ru", "меняй правую");

    // --- Compound commands

    [Theory]
    [InlineData("ru", "меняй шины залей двадцать пять литров", new[] { "tires.all", "fuel.add" })]
    [InlineData("ru", "залей десять литров меняй правую сторону и сорви плёнку", new[] { "fuel.add", "tires.right", "windshield.tearoff" })]
    [InlineData("en", "change tires add twenty five liters", new[] { "tires.all", "fuel.add" })]
    public void Conjunction_lost_by_the_recognizer(string language, string phrase, string[] intents) =>
        Assert.Equal(intents, Intents(language, phrase));

    [Theory]
    [InlineData("меняй шины и спой песню")]
    [InlineData("меняй шины спой песню")]
    public void One_part_not_recognized(string phrase) => Rejected("ru", phrase);

    // --- Elided tire action

    [Theory]
    [InlineData("ru", "меняй левые и передние", new[] { "tires.left", "tires.front" })]
    [InlineData("ru", "не меняй левые и переднее правое", new[] { "tires.none.left", "tires.none.rf" })]
    [InlineData("ru", "меняй переднее левое и заднее правое и заднее левое", new[] { "tires.lf", "tires.rr", "tires.lr" })]
    [InlineData("ru", "меняй правые и левые не меняем", new[] { "tires.right", "tires.none.left" })]
    [InlineData("ru", "не меняй левые и задние шины", new[] { "tires.none.left", "tires.none.rear" })]
    [InlineData("en", "change front left and rear right", new[] { "tires.lf", "tires.rr" })]
    public void Elided_tire_action(string language, string phrase, string[] intents) => Assert.Equal(intents, Intents(language, phrase));

    [Theory]
    [InlineData("залей двадцать и передние")]
    [InlineData("меняй левые и стекло")]
    public void Elided_action_only_for_tire_targets(string phrase) => Rejected("ru", phrase);

    // --- Reject words (design D12)

    [Theory]
    [InlineData("ru", "залей тридцать точка два")]
    [InlineData("en", "change to intermediates")]
    public void Reject_word_rejects_the_whole_phrase(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Same_command_without_the_reject_word_still_works() =>
        Assert.Equal(["tires.all"], Intents("ru", "меняем резину"));

    // --- Word forms and repeated actions (design D13)

    [Theory]
    [InlineData("меняй переднее", new[] { "tires.front" })]                       // "передние" heard as "переднее"
    [InlineData("не трогай заднее", new[] { "tires.none.rear" })]
    [InlineData("правая сторона", new[] { "tires.right" })]
    public void Swapped_forms_are_one_word(string phrase, string[] intents) => Assert.Equal(intents, Intents("ru", phrase));

    [Theory]
    [InlineData("ru", "меняй левые передние")]               // the side and the axle, or the wheel in the wrong forms
    [InlineData("ru", "смени переднее правая")]              // "сменим переднее правое" heard in the wrong forms
    [InlineData("ru", "меняй передние левое заднее правое")]
    [InlineData("ru", "не меняй задние левые")]
    [InlineData("en", "change left fronts")]
    public void Side_and_axle_in_a_row_are_rejected(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Wheel_named_as_written_still_works()
    {
        Assert.Equal(["tires.lf", "tires.rr"], Intents("ru", "меняй переднее левое заднее правое"));
        Assert.Equal(["tires.lf"], Intents("en", "change left front"));
    }

    [Theory]
    [InlineData("ru", "меняй шины меняй шины")]               // "меняй шины и не меняй шины" with "и не" lost
    [InlineData("ru", "меняй правые меняй левое")]
    [InlineData("en", "change tires change tires")]
    public void Same_action_twice_without_a_conjunction_is_rejected(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Same_action_twice_with_a_conjunction_still_works() =>
        Assert.Equal(["tires.left", "tires.right"], Intents("ru", "меняй левые и меняй правые"));

    [Theory]
    [InlineData("ru", "залей тридцать точка два")]
    [InlineData("ru", "кто впереди меня")]
    [InlineData("en", "add thirty point five liters")]
    [InlineData("en", "who is ahead")]
    public void Words_the_recognizer_would_drop_reject_the_phrase(string language, string phrase) => Rejected(language, phrase);

    // --- Conflicts

    [Fact]
    public void Cancel_cannot_be_combined_because_it_is_no_command() => Rejected("ru", "отмена и залей двадцать");

    [Fact]
    public void Clear_cannot_be_combined() =>
        Rejected("ru", "отмени всё, залей десять литров и меняй правую сторону", RejectReason.Conflict);

    // --- Language packs: built-in vocabularies

    [Fact]
    public void Same_rules_for_both_languages()
    {
        Assert.Equal(["tires.right"], Intents("ru", "поменяй правые шины"));
        Assert.Equal(["tires.right"], Intents("en", "swap right tires"));
    }

    [Fact]
    public void Multi_word_entry()
    {
        var command = Assert.Single(((ParseResult.Accepted)Parse("en", "give me twenty liters")).Commands);
        Assert.Equal(IntentCatalog.FuelAdd, command.Intent);
        Assert.Equal(20, command.Amount);
        Assert.Equal(FuelUnits.Liters, command.Unit);
    }

    [Fact]
    public void Words_of_one_class_only() => Rejected("ru", "топливо шины");

    [Fact]
    public void New_tire_verb_with_every_target()
    {
        Assert.Equal(["tires.front"], Intents("ru", "заменяем передние"));
        Assert.Equal(["tires.rr"], Intents("ru", "заменяем заднее правое"));
        Assert.Equal(["tires.all"], Intents("ru", "заменяем все шины"));
    }

    [Fact]
    public void New_fuel_verb()
    {
        var command = Assert.Single(((ParseResult.Accepted)Parse("ru", "долей двадцать литров")).Commands);
        Assert.Equal(IntentCatalog.FuelAdd, command.Intent);
        Assert.Equal(20, command.Amount);
    }

    [Fact]
    public void Removal_verb() => Assert.Equal(["tires.none.rear"], Intents("ru", "задние не заменяем"));

    [Fact]
    public void Verb_not_in_the_pack() => Rejected("ru", "плесни тридцать литров");

    [Theory]
    [InlineData("ru", "меняй переднее левое", "tires.lf")]
    [InlineData("ru", "замени левое переднее", "tires.lf")]
    [InlineData("ru", "левые меняй", "tires.left")]
    [InlineData("ru", "задние шины", "tires.rear")]
    [InlineData("en", "left front", "tires.lf")]
    [InlineData("en", "no left side tires", "tires.none.left")]
    public void Individual_wheel_commands(string language, string phrase, string intent) =>
        Assert.Equal([intent], Intents(language, phrase));

    [Theory]
    [InlineData("ru", "тир офф")]
    [InlineData("en", "fix it")]
    public void Removed_noise_words_are_rejected(string language, string phrase) => Rejected(language, phrase);

    [Fact]
    public void Kept_word_of_the_same_command() => Assert.Equal(["windshield.tearoff"], Intents("ru", "сорви плёнку"));

    [Fact]
    public void Words_of_removed_phrases_leave_the_vocabulary()
    {
        var vocabulary = VocabularyBuilder.Build(TestPacks.Ru);
        Assert.DoesNotContain("тир", vocabulary);
        Assert.DoesNotContain("офф", vocabulary);
        Assert.DoesNotContain("горючего", vocabulary);
        Assert.DoesNotContain("бензина", vocabulary);
        Assert.DoesNotContain("ничего", vocabulary);
        Assert.DoesNotContain("gas", VocabularyBuilder.Build(TestPacks.En));
        Assert.DoesNotContain("it", VocabularyBuilder.Build(TestPacks.En));
    }

    [Fact]
    public void Excluded_forms_stay_out_of_the_vocabulary()
    {
        // "ставь" and its forms sound like "оставь" (keep), "бензин" is a question, "отбой" means "over and out".
        var vocabulary = VocabularyBuilder.Build(TestPacks.Ru);
        foreach (var word in new[] { "ставь", "ставим", "поставим", "бензин", "отбой" })
        {
            Assert.DoesNotContain(word, vocabulary);
        }
    }

    [Fact]
    public void Built_in_packs_have_no_voice_field()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var text = File.ReadAllText(Path.Combine(TestPacks.Directory, $"{language}.yaml"));
            Assert.DoesNotContain("default_voice", text, StringComparison.Ordinal);
            Assert.Empty(TestPacks.Get(language).MissingIntents);
        }
    }

    // --- Determinism

    [Fact]
    public void Repeated_parse_of_new_phrases_gives_identical_results()
    {
        var parser = new CommandParser(TestPacks.Ru);
        string[] phrases =
        [
            "меняй левые передние",
            "меняй правые меняй левое",
            "меняй левые и передние",
            "залей десять литров меняй правую сторону и сорви плёнку",
            "сбрось все шины",
            "смени плёнку",
        ];

        foreach (var phrase in phrases)
        {
            var first = parser.Parse(phrase);
            for (var i = 0; i < 100; i++)
            {
                Assert.Equal(first.ToString(), parser.Parse(phrase).ToString());
            }
        }
    }
}
