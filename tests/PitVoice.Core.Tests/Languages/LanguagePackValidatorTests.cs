using System.Text.RegularExpressions;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Languages;

public class LanguagePackValidatorTests
{
    private static LanguagePackLoadResult LoadFixture(string name) =>
        LanguagePackLoader.LoadFile(Path.Combine(TestPacks.FixturesDirectory, name));

    private static string FixtureText(string name) => File.ReadAllText(Path.Combine(TestPacks.FixturesDirectory, name));

    private static int FixtureLine(string name, string contains)
    {
        var lines = File.ReadAllLines(Path.Combine(TestPacks.FixturesDirectory, name));
        var index = Array.FindIndex(lines, l => l.Contains(contains, StringComparison.Ordinal));
        Assert.True(index >= 0, $"fixture {name} has no line containing '{contains}'");
        return index + 1;
    }

    /// <summary>The minimal pack with <paramref name="lexicon"/> lines appended to its lexicon.</summary>
    private static LanguagePackLoadResult MinimalWith(string lexicon, string responses = "", string tests = "")
    {
        var yaml = FixtureText("valid-minimal.yaml");
        yaml = Regex.Replace(yaml, "^responses:\r?\n", lexicon + "responses:\n" + responses, RegexOptions.Multiline);
        yaml += tests;
        return LanguagePackLoader.LoadText(yaml, "inline.yaml");
    }

    [Fact]
    public void Valid_pack_loads_with_its_intents_and_a_warning_about_missing_ones()
    {
        var result = LoadFixture("valid-minimal.yaml");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.Empty(result.Errors);
        var pack = result.Pack!;
        Assert.Equal("ru", pack.Language);
        Assert.Equal([IntentCatalog.FuelAdd, IntentCatalog.TiresAll], pack.Intents);

        // Spec: Catalog intent missing from pack — the pack has no windshield words, which is a warning.
        Assert.Contains(IntentCatalog.WindshieldTearoff, pack.MissingIntents);
        var warning = Assert.Single(result.Warnings, w => w.Message.Contains("incomplete", StringComparison.Ordinal));
        Assert.Contains("windshield.tearoff", warning.Message, StringComparison.Ordinal);
        Assert.Contains("windshield.none", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Phrases_for_a_missing_intent_are_rejected()
    {
        var pack = LoadFixture("valid-minimal.yaml").Pack!;
        var result = new CommandParser(pack).Parse("протри стекло");
        Assert.Equal(RejectReason.NotUnderstood, Assert.IsType<ParseResult.Rejected>(result).Reason);
    }

    [Fact]
    public void Leftover_voice_field_names_the_file_and_the_field()
    {
        var result = LoadFixture("voice-field.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("voice-field.yaml", error.File, StringComparison.Ordinal);
        Assert.Contains("'default_voice'", error.Message, StringComparison.Ordinal);
        Assert.Equal(FixtureLine("voice-field.yaml", "default_voice"), error.Line);
    }

    [Fact]
    public void Old_format_with_intents_is_not_loaded()
    {
        var result = LoadFixture("old-intents.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'intents'", error.Message, StringComparison.Ordinal);
        Assert.Contains("lexicon", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_word_class_names_file_line_and_class()
    {
        var result = LoadFixture("unknown-class.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'tire_flip'", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("unknown-class.yaml", error.File, StringComparison.Ordinal);
        Assert.Equal(FixtureLine("unknown-class.yaml", "tire_flip"), error.Line);
    }

    [Fact]
    public void Unknown_role_of_a_group_is_an_error()
    {
        var result = MinimalWith("  repair: { action: [почини], slow: [медленный] }\n");

        var error = Assert.Single(result.Errors, e => e.Message.Contains("repair.slow", StringComparison.Ordinal));
        Assert.Equal("lexicon/repair/slow", error.Path);
        Assert.Contains("action, object, fast", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_tire_target_names_file_line_and_target()
    {
        var result = LoadFixture("unknown-target.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'middle'", error.Message, StringComparison.Ordinal);
        Assert.Equal(FixtureLine("unknown-target.yaml", "middle"), error.Line);
    }

    [Fact]
    public void Unknown_response_key_names_file_line_and_key()
    {
        var result = LoadFixture("unknown-intent.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'tires.flip'", error.Message, StringComparison.Ordinal);
        Assert.Equal(FixtureLine("unknown-intent.yaml", "tires.flip"), error.Line);
    }

    [Fact]
    public void Unknown_slot_names_the_response_and_the_slot()
    {
        var result = LoadFixture("unknown-slot.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("{liters}", error.Message, StringComparison.Ordinal);
        Assert.Contains("'fuel.add'", error.Message, StringComparison.Ordinal);
        Assert.Equal("responses/fuel.add", error.Path);
    }

    [Fact]
    public void Missing_response_of_an_available_intent_is_an_error()
    {
        var result = LoadFixture("no-response.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'tires.all' has no response", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_outcome_response_is_an_error()
    {
        var result = LoadFixture("no-outcome-response.yaml");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("no-outcome-response.yaml", error.File, StringComparison.Ordinal);
        Assert.Contains("'responses.apply_failed' is required", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not_supported")]
    [InlineData("cancelled")]
    public void Leftover_response_of_a_removed_intent_is_a_warning(string key)
    {
        var result = MinimalWith(string.Empty, $"  {key}: \"old\"\n");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        var warning = Assert.Single(result.Warnings, w => w.Path == $"responses/{key}");
        Assert.Contains("is not used", warning.Message, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("lexicon/fuel/action/1", "has a slot")]
    [InlineData("lexicon/fuel/action/2", "has digits")]
    [InlineData("lexicon/fuel/action/3", "filler 'пожалуйста'")]
    [InlineData("lexicon/tires/action/1", "'не' is in 'negations'")]
    [InlineData("lexicon/tires/object/1", "conjunction 'и'")]
    [InlineData("lexicon/tires/object/2", "unit word 'литров'")]
    public void Bad_words_are_errors_with_their_place(string path, string message)
    {
        var result = LoadFixture("bad-words.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors, e => e.Path == path);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.Line);
    }

    [Fact]
    public void Same_word_in_action_and_keep_of_a_group_is_an_error()
    {
        var result = LoadFixture("bad-words.yaml");

        Assert.Single(result.Errors, e => e.Message.Contains("'трогай' is in both 'tires.action' and 'tires.keep'", StringComparison.Ordinal));
    }

    [Fact]
    public void Reject_word_that_is_a_command_word_is_an_error()
    {
        var yaml = FixtureText("valid-minimal.yaml").Replace("fillers: [пожалуйста]", "fillers: [пожалуйста]\nreject_words: [на, шины]", StringComparison.Ordinal);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains("reject word 'шины' is also in 'tires.object'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_words_load_into_the_pack_and_the_vocabulary()
    {
        var yaml = FixtureText("valid-minimal.yaml").Replace("fillers: [пожалуйста]", "fillers: [пожалуйста]\nreject_words: [на, Мокрые]", StringComparison.Ordinal);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.Equal(["мокрые", "на"], result.Pack!.RejectWords.Order(StringComparer.Ordinal));
        Assert.Contains("на", PitVoice.Core.Asr.VocabularyBuilder.Build(result.Pack));
    }

    [Fact]
    public void Words_shared_by_groups_are_allowed()
    {
        var result = MinimalWith(
            "  windshield: { action: [меняй], object: [стекло] }\n",
            "  windshield.tearoff: \"протираем\"\n",
            "  - say: \"меняй стекло\"\n    expect: [{ intent: windshield.tearoff }]\n");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        var parser = new CommandParser(result.Pack!);
        Assert.Equal(IntentCatalog.TiresAll, Assert.Single(((ParseResult.Accepted)parser.Parse("меняй шины")).Commands).Intent);
    }

    [Fact]
    public void Repeated_word_in_a_class_is_a_warning()
    {
        var result = MinimalWith("  repair: { action: [почини, почини], object: [машину] }\n", "  repair.fast: \"чиним\"\n",
            "  - say: \"почини машину\"\n    expect: [{ intent: repair.fast }]\n");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.Single(result.Warnings, w => w.Message.Contains("repeated", StringComparison.Ordinal));
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
        Assert.Contains(messages, m => m.StartsWith("lexicon/tires/flip:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("responses/fuel.add:", StringComparison.Ordinal) && m.Contains("{laps}", StringComparison.Ordinal));
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
    public void Entries_are_matched_by_normalized_tokens()
    {
        var pack = TestPacks.Ru;
        var allFour = Assert.Single(pack.Lexicon.Of(new WordClass(LexiconGroup.Tires, WordRole.Target, TireTarget.All)), e => e.Text == "все четыре");
        Assert.Equal(["все", "#4"], allFour.Keys);

        var film = Assert.Single(pack.Lexicon.Of(new WordClass(LexiconGroup.Windshield, WordRole.Object)), e => e.Keys[0] == "пленку");
        Assert.Equal("пленку", film.Text);
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

    [Fact]
    public void Example_pack_of_the_format_document_is_valid_and_passes_its_tests()
    {
        // The fixture is the first YAML block of docs/adding-a-language.md; keep them equal.
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "PitVoice.sln")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repository root not found");
        }

        var document = File.ReadAllText(Path.Combine(root, "docs", "adding-a-language.md")).ReplaceLineEndings("\n");
        var fixture = FixtureText("docs-example.yaml").ReplaceLineEndings("\n");
        Assert.Contains("```yaml\n" + fixture + "```", document, StringComparison.Ordinal);

        var result = LoadFixture("docs-example.yaml");
        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.Empty(LanguagePackTestRunner.Run(result.Pack!));
        Assert.Single(result.Warnings, w => w.Message.Contains("incomplete", StringComparison.Ordinal));
    }
}
