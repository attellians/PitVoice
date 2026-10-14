using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Languages;

public class LanguagePackValidatorTests
{
    private static LanguagePackLoadResult LoadFixture(string name) =>
        LanguagePackLoader.LoadFile(Path.Combine(TestPacks.FixturesDirectory, name));

    private static string FixtureLine(string name, string contains)
    {
        var lines = File.ReadAllLines(Path.Combine(TestPacks.FixturesDirectory, name));
        var index = Array.FindIndex(lines, l => l.Contains(contains, StringComparison.Ordinal));
        Assert.True(index >= 0, $"fixture {name} has no line containing '{contains}'");
        return (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Valid_pack_loads_with_its_intents_and_a_warning_about_missing_ones()
    {
        var result = LoadFixture("valid-minimal.yaml");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.Empty(result.Errors);
        var pack = result.Pack!;
        Assert.Equal("ru", pack.Language);
        Assert.Equal(2, pack.Intents.Count);
        Assert.Contains(IntentCatalog.FuelAdd, pack.Intents.Keys);
        Assert.Contains(IntentCatalog.TiresAll, pack.Intents.Keys);

        // Catalog intents absent from the pack are warnings, not errors (FR-LANG-4).
        Assert.Contains(IntentCatalog.WindshieldNone, pack.MissingIntents);
        Assert.Contains(result.Warnings, w => w.Message.Contains("windshield.none", StringComparison.Ordinal));
    }

    [Fact]
    public void Phrases_for_a_missing_intent_are_rejected()
    {
        var pack = LoadFixture("valid-minimal.yaml").Pack!;
        var result = new CommandParser(pack).Parse("стекло не трогай");
        Assert.IsType<ParseResult.Rejected>(result);
        Assert.Equal(RejectReason.NotUnderstood, ((ParseResult.Rejected)result).Reason);
    }

    [Fact]
    public void Unknown_intent_names_file_intent_and_line()
    {
        var result = LoadFixture("unknown-intent.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors, e => e.Message.Contains("tires.flip", StringComparison.Ordinal));
        Assert.Contains("unknown intent", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("unknown-intent.yaml", error.File, StringComparison.Ordinal);
        Assert.Equal(FixtureLine("unknown-intent.yaml", "tires.flip:"), error.Line?.ToString());
    }

    [Fact]
    public void Unknown_slot_names_the_pattern_and_the_slot()
    {
        var result = LoadFixture("unknown-slot.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("unknown slot {liters}", error.Message, StringComparison.Ordinal);
        Assert.Contains("залей {amount} {liters}", error.Message, StringComparison.Ordinal);
        Assert.Equal("intents/fuel.add/patterns/1", error.Path);
        Assert.Equal(FixtureLine("unknown-slot.yaml", "{liters}"), error.Line?.ToString());
    }

    [Fact]
    public void Empty_pattern_list_is_an_error()
    {
        var result = LoadFixture("empty-patterns.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("no patterns", error.Message, StringComparison.Ordinal);
        Assert.Equal("intents/tires.all/patterns", error.Path);
    }

    [Fact]
    public void Missing_response_is_an_error()
    {
        var result = LoadFixture("no-response.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("no response", error.Message, StringComparison.Ordinal);
        Assert.Equal("intents/tires.all/response", error.Path);
    }

    [Fact]
    public void Missing_outcome_response_is_an_error()
    {
        var result = LoadFixture("no-outcome-response.yaml");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("no-outcome-response.yaml", error.File, StringComparison.Ordinal);
        Assert.Contains("'responses.not_supported' is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Intent_without_test_phrase_is_an_error()
    {
        var result = LoadFixture("intent-without-test.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("repair.fast", error.Message, StringComparison.Ordinal);
        Assert.Contains("not covered by any test", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unit_without_forms_is_an_error()
    {
        var result = LoadFixture("unit-without-forms.yaml");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Path == "units/liters/forms" && e.Message.Contains("no plural forms", StringComparison.Ordinal));
        // Partial forms are also an error: every category of the plural rules needs one.
        Assert.Contains(result.Errors, e => e.Path == "units/gallons/forms" && e.Message.Contains("'few'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Path == "units/gallons/forms" && e.Message.Contains("'many'", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_pattern_across_intents_is_an_error()
    {
        var result = LoadFixture("duplicate-pattern.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors, e => e.Message.Contains("duplicates", StringComparison.Ordinal));
        Assert.Equal("intents/tires.none/patterns/0", error.Path);
    }

    [Fact]
    public void All_errors_are_reported_at_once()
    {
        var result = LoadFixture("many-errors.yaml");

        Assert.False(result.IsSuccess);
        var messages = result.Errors.Select(e => e.Path + ": " + e.Message).ToArray();
        Assert.Contains(messages, m => m.StartsWith("language:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("name:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("units:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("intents/tires.all/response:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("intents/fuel.add/response:", StringComparison.Ordinal) && m.Contains("{laps}", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("responses:", StringComparison.Ordinal) && m.Contains("not_understood", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("tests/0:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("tests/1/expect:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("tests/2/expect:", StringComparison.Ordinal) && m.Contains("windshield.none", StringComparison.Ordinal));
        Assert.True(result.Errors.Count() >= 9, string.Join('\n', messages));
    }

    [Fact]
    public void Malformed_yaml_names_file_and_line_and_does_not_break_other_packs()
    {
        var results = LanguagePackLoader.LoadDirectory(TestPacks.FixturesDirectory);

        var malformed = Assert.Single(results, r => r.File.EndsWith("malformed.yaml", StringComparison.Ordinal));
        Assert.False(malformed.IsSuccess);
        var error = Assert.Single(malformed.Errors);
        Assert.Contains("invalid YAML", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.Line);
        Assert.InRange(error.Line!.Value, 4, 7);

        var valid = Assert.Single(results, r => r.File.EndsWith("valid-minimal.yaml", StringComparison.Ordinal));
        Assert.True(valid.IsSuccess);
    }

    [Fact]
    public void Templates_are_compiled_with_literal_numbers_and_units()
    {
        var pack = TestPacks.Ru;
        var allFour = pack.Intents[IntentCatalog.TiresAll].Templates.Single(t => t.Source == "все четыре");
        Assert.Equal([new WordLiteral("все"), new NumberLiteral(4)], allFour.Tokens);

        var fuel = pack.Intents[IntentCatalog.FuelAdd].Templates.Single(t => t.Source == "залей {amount} {unit?}");
        Assert.Equal([new WordLiteral("залей"), AmountSlot.Instance, UnitSlot.OptionalSlot], fuel.Tokens);
    }

    [Fact]
    public void Filler_inside_a_pattern_is_an_error()
    {
        const string yaml = """
            language: ru
            name: Паразит
            units:
              liters:
                synonyms: [литр]
                forms: { one: литр, few: литра, many: литров }
            fillers: [давай]
            intents:
              tires.all:
                patterns: ["давай меняй шины"]
                response: "меняем"
            responses:
              not_understood: "не понял"
              not_connected: "нет связи"
              not_in_car: "не в машине"
              apply_failed: "не применилось"
              not_supported: "не умею"
            tests:
              - say: "меняй шины"
                expect: [{ intent: tires.all }]
            """;

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");
        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'давай' is a filler", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_packs_load_without_errors_or_warnings()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var result = LanguagePackLoader.LoadFile(Path.Combine(TestPacks.Directory, $"{language}.yaml"));
            Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
            Assert.Empty(result.Issues);
        }
    }
}
