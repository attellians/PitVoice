using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Languages;

/// <summary>
/// User pack additions (FR-LANG-5, design D1/D4 of replace-additions-with-phrase-builder): fuel verbs added to
/// <c>fuel.action</c> and ready phrases with one command each.
/// </summary>
public sealed class UserAdditionsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pitvoice-additions-" + Guid.NewGuid().ToString("N"));

    public UserAdditionsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string language, string yaml)
    {
        var path = UserAdditionsLoader.GetFile(_directory, language);
        File.WriteAllText(path, yaml);
        return path;
    }

    private PackAdditionsResult ApplyRu(string yaml)
    {
        Write("ru", yaml);
        return UserAdditions.Apply(TestPacks.Ru, _directory);
    }

    private static ParsedCommand Single(LanguagePack pack, string phrase)
    {
        var result = new CommandParser(pack).Parse(phrase);
        var accepted = Assert.IsType<ParseResult.Accepted>(result);
        return Assert.Single(accepted.Commands);
    }

    // ----- Scenarios of "User pack additions"

    [Fact]
    public void Own_phrase_works()
    {
        var result = ApplyRu("""
            lexicon:
              fuel: { action: [плесни] }
            """);

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        Assert.Empty(result.Issues);
        var command = Single(result.Pack, "плесни тридцать литров");
        Assert.Equal(IntentCatalog.FuelAdd, command.Intent);
        Assert.Equal(30, command.Amount);
        Assert.Equal("liters", command.Unit);

        var builtIn = Single(result.Pack, "залей двадцать литров");
        Assert.Equal(20, builtIn.Amount);
        Assert.Equal(IntentCatalog.FuelNone, Single(result.Pack, "не плесни").Intent);
    }

    [Fact]
    public void Ready_phrase_works()
    {
        var result = ApplyRu("""
            language: ru
            phrases:
              - { text: идет дождь, intent: tires.wet }
            """);

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        Assert.Equal(IntentCatalog.TiresWet, Single(result.Pack, "идёт дождь").Intent);
        Assert.Equal(IntentCatalog.TiresAll, Single(result.Pack, "меняй все шины").Intent);
        var phrase = Assert.Single(result.Pack.UserPhrases);
        Assert.Equal("идет дождь", phrase.Text);
        Assert.Empty(TestPacks.Ru.UserPhrases);
    }

    [Fact]
    public void Unknown_intent_in_additions_rejects_the_whole_file_with_file_line_and_class()
    {
        var path = Write("ru", """
            lexicon:
              fuel: { action: [плесни] }
              tire_flip: { action: [переверни] }
            """);

        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Applied);
        Assert.True(result.NotBuilderFormat);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.Equal(3, error.Line);
        Assert.Contains("'tire_flip'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lexicon:\n  tires: { targets: { front: [перёд] } }\n", "'tires.targets.front'")]
    [InlineData("lexicon:\n  tires: { action: [переобуй] }\n", "'tires.action'")]
    [InlineData("lexicon:\n  tires: { compounds: { wet: [мокрые] } }\n", "'tires.compounds.wet'")]
    [InlineData("lexicon:\n  windshield: { action: [протри] }\n", "'windshield.action'")]
    public void Words_in_a_class_the_builder_does_not_show_reject_the_file(string yaml, string expected)
    {
        var path = Write("ru", yaml);

        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Applied);
        Assert.True(result.NotBuilderFormat);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains("Own phrases", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tires.none.lf")]
    [InlineData("windshield.none")]
    [InlineData("fuel.add")]
    [InlineData("tire.flip")]
    public void Ready_phrase_with_an_unknown_command_rejects_the_file(string intent)
    {
        var path = Write("ru", $"phrases:\n  - {{ text: снимай левое переднее, intent: {intent} }}\n");

        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Applied);
        Assert.True(result.NotBuilderFormat);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.Contains($"'{intent}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Old_additions_format_is_not_applied_and_points_to_the_page()
    {
        var path = Write("ru", """
            language: ru
            intents:
              fuel.add:
                patterns: ["плесни {amount} {unit?}"]
            """);

        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Applied);
        Assert.True(result.NotBuilderFormat);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.Contains("'intents'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Own phrases", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_file_keeps_the_built_in_pack_and_names_the_file_and_line()
    {
        var path = Write("ru", """
            lexicon:
              fuel: { action: [плесни }
            """);

        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Applied);
        Assert.True(result.NotBuilderFormat);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.NotNull(error.Line);
        Assert.Contains("invalid YAML", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Additions_for_a_language_without_pack_are_ignored_with_a_warning()
    {
        var path = Write("de", "lexicon:\n  fuel: { action: [tanke] }\n");
        Write("ru", "lexicon: {}\n");

        var orphans = UserAdditions.FindOrphans(_directory, ["ru", "en"]);

        var warning = Assert.Single(orphans);
        Assert.Equal(IssueSeverity.Warning, warning.Severity);
        Assert.Equal(path, warning.File);
        Assert.Contains("'de'", warning.Message, StringComparison.Ordinal);
    }

    // ----- Scenarios of "User additions validation"

    [Fact]
    public void Unknown_slot_rejects_the_whole_file_and_names_the_word()
    {
        var result = ApplyRu("lexicon:\n  fuel: { action: [плесни, \"залей {amount}\"] }\n");

        Assert.False(result.Applied);
        Assert.False(result.NotBuilderFormat);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'залей {amount}'", error.Message, StringComparison.Ordinal);
        Assert.Equal(AdditionProblem.Symbols, error.Problem);
        Assert.Equal(1, error.Entry);
    }

    [Fact]
    public void Duplicate_of_a_built_in_verb_is_skipped_with_a_warning_and_the_rest_applies()
    {
        var result = ApplyRu("lexicon:\n  fuel: { action: [залей, плесни, плесни] }\n");

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        Assert.Equal(2, result.Warnings.Count());
        Assert.Contains(result.Warnings, w => w.Problem == AdditionProblem.AlreadyWorks && w.Entry == 0
            && w.Message.Contains("'залей' is already built into 'fuel.action'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Problem == AdditionProblem.Repeated && w.Entry == 2);
        Assert.Single(result.Pack.UserEntries);
        Assert.Equal(["плесни"], result.Kept!.Words.Select(w => w.Text));
    }

    [Fact]
    public void Word_with_opposite_meaning_rejects_the_file_and_names_the_word_and_both_classes()
    {
        var result = ApplyRu("lexicon:\n  fuel: { action: [не] }\n");

        Assert.False(result.Applied);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'не' is in 'negations'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'fuel.action'", error.Message, StringComparison.Ordinal);
        Assert.Equal(AdditionProblem.Negation, error.Problem);
        Assert.Equal("не", error.Argument);
    }

    [Theory]
    [InlineData("литров")]
    [InlineData("двадцать")]
    [InlineData("пожалуйста")]
    public void Verb_that_does_not_give_a_fuel_amount_rejects_the_file(string verb)
    {
        var result = ApplyRu($"lexicon:\n  fuel: {{ action: [{verb}] }}\n");

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Entry == 0);
    }

    [Fact]
    public void Verb_is_checked_with_twenty_liters()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(verbs: ["плесни"]));

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        var command = Single(result.Pack, "плесни двадцать литров");
        Assert.Equal((IntentCatalog.FuelAdd, 20, "liters"), (command.Intent, command.Amount, command.Unit));
    }

    [Fact]
    public void One_word_ready_phrase_rejects_the_file()
    {
        var result = ApplyRu("phrases:\n  - { text: почисти, intent: windshield.tearoff }\n");

        Assert.False(result.Applied);
        var error = Assert.Single(result.Errors);
        Assert.Equal(AdditionProblem.TooShort, error.Problem);
        Assert.Contains("shorter than two words", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fillers_do_not_count_as_words_of_a_ready_phrase()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(phrases: [("давай почисти", IntentCatalog.WindshieldTearoff)]));

        Assert.Equal(AdditionProblem.TooShort, Assert.Single(result.Errors).Problem);
    }

    [Fact]
    public void Ready_phrase_that_means_another_command_rejects_the_file()
    {
        var result = ApplyRu("phrases:\n  - { text: не меняй резину, intent: tires.all }\n");

        Assert.False(result.Applied);
        var error = Assert.Single(result.Errors);
        Assert.Equal(AdditionProblem.MeansOther, error.Problem);
        Assert.Equal("tires.none", error.Argument);
        Assert.Contains("'не меняй резину'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'tires.none'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ready_phrase_with_two_built_in_commands_rejects_the_file()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(phrases: [("меняй шины залей двадцать литров", IntentCatalog.TiresAll)]));

        Assert.False(result.Applied);
        Assert.Equal(AdditionProblem.MeansOther, Assert.Single(result.Errors).Problem);
    }

    [Fact]
    public void Ready_phrase_that_already_works_is_skipped_with_a_warning()
    {
        var result = ApplyRu("""
            phrases:
              - { text: меняй все шины, intent: tires.all }
              - { text: идет дождь, intent: tires.wet }
            """);

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(AdditionProblem.AlreadyWorks, warning.Problem);
        Assert.Equal(0, warning.Entry);
        Assert.Contains("already gives", warning.Message, StringComparison.Ordinal);
        Assert.Equal(["идет дождь"], result.Pack.UserPhrases.Select(p => p.Text));
        Assert.Equal(["идет дождь"], result.Kept!.Phrases.Select(p => p.Text));
    }

    [Theory]
    [InlineData("шины и стекло", "и")]
    [InlineData("шины а также стекло", "а также")]
    public void Ready_phrase_with_a_conjunction_rejects_the_file(string text, string conjunction)
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(phrases: [(text, IntentCatalog.TiresAll)]));

        Assert.False(result.Applied);
        var error = Assert.Single(result.Errors);
        Assert.Equal(AdditionProblem.Conjunction, error.Problem);
        Assert.Equal(conjunction, error.Argument);
        Assert.Contains($"'{conjunction}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ready_phrase_with_a_reject_word_rejects_the_file()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(phrases: [("кто впереди", IntentCatalog.PitClear)]));

        var error = Assert.Single(result.Errors);
        Assert.Equal(AdditionProblem.RejectWord, error.Problem);
        Assert.Equal("кто", error.Argument);
    }

    [Fact]
    public void Ready_phrase_with_digits_rejects_the_file()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(phrases: [("пит 2 стоп", IntentCatalog.PitClear)]));

        Assert.Equal(AdditionProblem.Symbols, Assert.Single(result.Errors).Problem);
    }

    [Fact]
    public void Same_phrase_with_two_commands_is_an_error_on_the_second()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(phrases:
        [
            ("идет дождь", IntentCatalog.TiresWet),
            ("идёт дождь", IntentCatalog.TiresDry),
        ]));

        var error = Assert.Single(result.Errors);
        Assert.Equal(AdditionProblem.AssignedElsewhere, error.Problem);
        Assert.Equal(1, error.Entry);
        Assert.Equal("tires.wet", error.Argument);
    }

    [Fact]
    public void Same_phrase_with_the_same_command_is_skipped_with_a_warning()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(phrases:
        [
            ("идет дождь", IntentCatalog.TiresWet),
            ("идет ну дождь", IntentCatalog.TiresWet),
        ]));

        Assert.True(result.Applied);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal((AdditionProblem.Repeated, 1), (warning.Problem!.Value, warning.Entry!.Value));
        Assert.Single(result.Kept!.Phrases);
    }

    [Fact]
    public void Ready_phrase_for_an_intent_the_pack_lacks_is_an_error()
    {
        var pack = LanguagePackLoader.LoadFile(Path.Combine(TestPacks.FixturesDirectory, "valid-minimal.yaml")).Pack!;
        Assert.False(pack.Supports(IntentCatalog.TiresDry));

        var result = UserAdditions.Merge(pack, Draft(language: pack.Language, phrases: [("dry please now", IntentCatalog.TiresDry)]));

        var error = Assert.Single(result.Errors);
        Assert.Equal(AdditionProblem.UnknownCommand, error.Problem);
    }

    [Fact]
    public void Errors_of_entries_name_the_row_and_the_file_errors_do_not()
    {
        var result = UserAdditions.Merge(TestPacks.Ru, Draft(verbs: ["плесни"], phrases: [("почисти", IntentCatalog.WindshieldTearoff)]));

        var error = Assert.Single(result.Errors);
        Assert.Equal(1, error.Entry);
        Assert.False(result.NotBuilderFormat);
    }

    // ----- Files and directories

    [Fact]
    public void No_file_keeps_the_built_in_pack()
    {
        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Exists);
        Assert.False(result.Applied);
        Assert.False(result.NotBuilderFormat);
        Assert.Same(TestPacks.Ru, result.Pack);
        Assert.Empty(result.Issues);
        Assert.Equal(Path.Combine(_directory, "ru.yaml"), result.File);
        Assert.False(File.Exists(result.File));
    }

    [Fact]
    public void Added_entries_are_marked_and_the_pack_names_the_file()
    {
        var result = ApplyRu("lexicon:\n  fuel: { action: [плесни] }\n");

        var own = Assert.Single(result.Pack.UserEntries);
        Assert.Equal("плесни", own.Text);
        Assert.Equal(UserAdditionsLoader.FuelVerbClass, own.Class);
        Assert.Contains(result.Pack.Lexicon.Entries, e => !e.IsUserAddition && e.Text == "залей");
        Assert.Equal(result.File, result.Pack.AdditionsFile);
        Assert.Null(TestPacks.Ru.AdditionsFile);
        Assert.Equal(TestPacks.Ru.Intents, result.Pack.Intents);
    }

    [Theory]
    [InlineData("conjunctions: [плюс]\nlexicon: {}\n", "conjunctions")]
    [InlineData("lexicon:\n  fuel: { action: \"плесни\" }\n", "list")]
    [InlineData("lexicon: [плесни]\n", "map groups")]
    [InlineData("phrases: { text: идет дождь }\n", "list")]
    [InlineData("phrases:\n  - { text: идет дождь }\n", "'text' and 'intent'")]
    [InlineData("phrases:\n  - { text: идет дождь, intent: tires.wet, voice: да }\n", "'voice'")]
    public void Only_language_lexicon_and_phrases_are_allowed(string yaml, string expected)
    {
        var result = ApplyRu(yaml);

        Assert.False(result.Applied);
        Assert.True(result.NotBuilderFormat);
        Assert.Contains(result.Errors, e => e.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void File_for_another_language_is_not_applied()
    {
        var result = ApplyRu("language: en\nlexicon:\n  fuel: { action: [pour] }\n");

        Assert.False(result.Applied);
        Assert.True(result.NotBuilderFormat);
        Assert.Contains("'en'", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Entries_are_numbered_verbs_first_in_file_order()
    {
        var loaded = UserAdditionsLoader.LoadText("""
            phrases:
              - { text: идет дождь, intent: tires.wet }
              - { text: полный сервис, intent: repair.fast }
            lexicon:
              fuel: { action: [плесни, долей] }
            """, "ru.yaml");

        var additions = loaded.Additions!;
        Assert.Equal([0, 1], additions.Words.Select(w => w.Entry));
        Assert.Equal([2, 3], additions.Phrases.Select(p => p.Entry));
        Assert.Equal(4, additions.Count);
    }

    [Fact]
    public void Directory_comes_from_parameter_then_environment_then_app_data()
    {
        Assert.Equal(Path.GetFullPath(_directory), UserAdditionsLoader.ResolveDirectory(_directory));
        var previous = Environment.GetEnvironmentVariable(UserAdditionsLoader.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(UserAdditionsLoader.EnvironmentVariable, _directory);
            Assert.Equal(Path.GetFullPath(_directory), UserAdditionsLoader.ResolveDirectory(null));
            Environment.SetEnvironmentVariable(UserAdditionsLoader.EnvironmentVariable, null);
            Assert.Equal(Path.GetFullPath(UserAdditionsLoader.DefaultDirectory), UserAdditionsLoader.ResolveDirectory(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(UserAdditionsLoader.EnvironmentVariable, previous);
        }
    }

    // ----- Writing (design D1)

    [Fact]
    public void Written_file_reads_back_without_losses()
    {
        var path = UserAdditionsLoader.GetFile(Path.Combine(_directory, "new"), "ru");
        var draft = Draft(verbs: ["плесни", "долей-ка"], phrases:
        [
            ("идёт дождь", IntentCatalog.TiresWet),
            ("скажи \"стоп\": всё", IntentCatalog.PitClear),
        ]);

        Assert.Null(UserAdditionsWriter.TryWrite(path, "ru", draft.Words, draft.Phrases));

        var text = File.ReadAllText(path);
        Assert.StartsWith(UserAdditionsWriter.Header, text, StringComparison.Ordinal);
        var loaded = UserAdditionsLoader.LoadFile(path)!;
        Assert.True(loaded.IsSuccess, string.Join('\n', loaded.Issues));
        Assert.Equal("ru", loaded.Additions!.Language);
        Assert.Equal(draft.Words.Select(w => (w.Class, w.Text)), loaded.Additions.Words.Select(w => (w.Class, w.Text)));
        Assert.Equal(draft.Phrases.Select(p => (p.Text, p.Intent)), loaded.Additions.Phrases.Select(p => (p.Text, p.Intent)));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Busy_file_returns_an_error_and_the_old_file_stays()
    {
        var path = Write("ru", "lexicon:\n  fuel: { action: [плесни] }\n");
        var before = File.ReadAllText(path);

        string? error;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            error = UserAdditionsWriter.TryWrite(path, "ru", [], [new AdditionPhrase("идет дождь", IntentCatalog.TiresWet, null)]);
        }

        Assert.NotNull(error);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    internal static UserPackAdditions Draft(
        string language = "ru",
        IReadOnlyList<string>? verbs = null,
        IReadOnlyList<(string Text, IntentId Intent)>? phrases = null)
    {
        var words = (verbs ?? []).Select((v, i) => new AdditionWord(UserAdditionsLoader.FuelVerbClass, v, null) { Entry = i }).ToArray();
        var list = (phrases ?? []).Select((p, i) => new AdditionPhrase(p.Text, p.Intent, null) { Entry = words.Length + i }).ToArray();
        return new UserPackAdditions("draft", language, words, list);
    }
}
