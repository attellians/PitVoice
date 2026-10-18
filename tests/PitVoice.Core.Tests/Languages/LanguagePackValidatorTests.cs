using System.Text.RegularExpressions;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
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

    [Theory]
    [InlineData("word_forms:\n  шины: [шина, шина]\n", "is a form of both")]
    [InlineData("word_forms:\n  шины: [шины]\n", "a form of itself")]
    [InlineData("word_forms:\n  шины: [шина]\n  шина: [шинка]\n", "cannot have forms itself")]
    [InlineData("word_forms:\n  меняй шины: [шина]\n", "single word")]
    public void Bad_word_forms_are_errors(string forms, string message)
    {
        var yaml = FixtureText("valid-minimal.yaml").Replace("fillers: [пожалуйста]", "fillers: [пожалуйста]\n" + forms.TrimEnd('\n'), StringComparison.Ordinal);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains(message, StringComparison.Ordinal) && e.Path.StartsWith("word_forms", StringComparison.Ordinal));
    }

    [Fact]
    public void Word_forms_join_input_and_entries_and_keep_the_written_words()
    {
        var yaml = FixtureText("valid-minimal.yaml").Replace("fillers: [пожалуйста]", "fillers: [пожалуйста]\nword_forms:\n  шины: [шина]", StringComparison.Ordinal);

        var pack = LanguagePackLoader.LoadText(yaml, "inline.yaml").Pack!;

        Assert.Equal(IntentCatalog.TiresAll, Assert.Single(((ParseResult.Accepted)new CommandParser(pack).Parse("меняй шина")).Commands).Intent);
        Assert.Contains("шина", PitVoice.Core.Asr.VocabularyBuilder.Build(pack));
        Assert.Contains(pack.Lexicon.Entries, e => e.Text == "шины");
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

    [Fact]
    public void Compound_classes_load_and_make_the_compound_intents_available()
    {
        var result = LoadFixture("compounds-minimal.yaml");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        var pack = result.Pack!;
        Assert.Contains(IntentCatalog.TiresDry, pack.Intents);
        Assert.Contains(IntentCatalog.TiresWet, pack.Intents);
        Assert.Equal("дождевые", Assert.Single(pack.Lexicon.Of(new WordClass(LexiconGroup.Tires, WordRole.Compound, Compound: TireCompoundKind.Wet))).Text);
        Assert.Equal("на", Assert.Single(pack.Lexicon.Of(new WordClass(LexiconGroup.Tires, WordRole.Into))).Text);
        Assert.Equal("tires.compounds.wet", new WordClass(LexiconGroup.Tires, WordRole.Compound, Compound: TireCompoundKind.Wet).Name);
    }

    [Fact]
    public void Unknown_compound_names_file_line_and_compound()
    {
        var result = LoadFixture("unknown-compound.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("unknown-compound.yaml", error.File, StringComparison.Ordinal);
        Assert.Contains("'intermediate'", error.Message, StringComparison.Ordinal);
        Assert.Contains("dry, wet", error.Message, StringComparison.Ordinal);
        Assert.Equal(FixtureLine("unknown-compound.yaml", "intermediate"), error.Line);
    }

    [Theory]
    [InlineData("дождевые", "tires.compounds.wet")]
    [InlineData("на", "tires.into")]
    public void Reject_word_that_is_a_compound_word_is_an_error(string word, string wordClass)
    {
        var yaml = FixtureText("compounds-minimal.yaml").Replace("fillers: [пожалуйста]", $"fillers: [пожалуйста]\nreject_words: [{word}]", StringComparison.Ordinal);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Contains($"reject word '{word}' is also in '{wordClass}'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no_dry_tires")]
    [InlineData("no_wet_tires")]
    [InlineData("several_dry_tires")]
    public void Missing_compound_response_is_an_error(string key)
    {
        var yaml = Regex.Replace(FixtureText("compounds-minimal.yaml"), $"^  {key}:.*\r?\n", string.Empty, RegexOptions.Multiline);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal("inline.yaml", error.File);
        Assert.Contains($"'responses.{key}' is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_without_compounds_loads_without_compound_intents_and_warns()
    {
        var result = LoadFixture("valid-minimal.yaml");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.DoesNotContain(result.Errors, e => e.Message.Contains("no_wet_tires", StringComparison.Ordinal));
        Assert.Contains(IntentCatalog.TiresDry, result.Pack!.MissingIntents);
        Assert.Contains(IntentCatalog.TiresWet, result.Pack.MissingIntents);
        var warning = Assert.Single(result.Warnings, w => w.Message.Contains("incomplete", StringComparison.Ordinal));
        Assert.Contains("tires.dry", warning.Message, StringComparison.Ordinal);
        Assert.Contains("tires.wet", warning.Message, StringComparison.Ordinal);
        Assert.IsType<ParseResult.Rejected>(new CommandParser(result.Pack).Parse("меняй шины на дождевые"));
    }

    [Fact]
    public void Compound_words_and_the_transition_word_are_in_the_vocabulary()
    {
        var pack = LoadFixture("compounds-minimal.yaml").Pack!;

        var vocabulary = PitVoice.Core.Asr.VocabularyBuilder.Build(pack);

        Assert.Contains("дождевые", vocabulary);
        Assert.Contains("дождевую", vocabulary);
        Assert.Contains("сухие", vocabulary);
        Assert.Contains("на", vocabulary);
        Assert.Equal("tires.into", PitVoice.Core.Asr.VocabularyBuilder.Usage(pack, "на"));
    }

    [Fact]
    public void Homophones_load_into_the_pack()
    {
        var yaml = FixtureText("compounds-minimal.yaml").Replace("fillers: [пожалуйста]", "fillers: [пожалуйста]\ninto_homophones: [два]", StringComparison.Ordinal);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.True(result.IsSuccess, string.Join('\n', result.Issues));
        Assert.Equal(["два"], result.Pack!.IntoHomophones);
    }

    [Fact]
    public void Homophones_without_a_transition_word_are_an_error()
    {
        var yaml = FixtureText("valid-minimal.yaml").Replace("fillers: [пожалуйста]", "fillers: [пожалуйста]\ninto_homophones: [два]", StringComparison.Ordinal);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal("inline.yaml", error.File);
        Assert.Equal("into_homophones", error.Path);
        Assert.Contains("'tires.into'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("шины", "'tires.object'")]
    [InlineData("и", "'conjunctions'")]
    [InlineData("не", "'negations'")]
    public void Homophone_that_is_a_command_word_is_an_error(string word, string where)
    {
        var yaml = FixtureText("compounds-minimal.yaml").Replace("fillers: [пожалуйста]", $"fillers: [пожалуйста]\ninto_homophones: [{word}]", StringComparison.Ordinal);

        var result = LanguagePackLoader.LoadText(yaml, "inline.yaml");

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Message.Contains($"homophone '{word}' is also in {where}", StringComparison.Ordinal));
    }
}
