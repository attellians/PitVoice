using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Languages;

/// <summary>User pack additions (FR-LANG-5, design D7): words added to classes of the built-in lexicon.</summary>
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

    [Fact]
    public void Own_word_works_with_every_amount_and_built_in_words_still_work()
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
    public void Own_tire_target_works_with_every_tire_verb()
    {
        var result = ApplyRu("""
            language: ru
            lexicon:
              tires: { targets: { front: [перёд] } }
            """);

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        Assert.Equal(IntentCatalog.TiresFront, Single(result.Pack, "заменяем перёд").Intent);
        Assert.Equal(IntentCatalog.TiresNoneFront, Single(result.Pack, "перёд не меняй").Intent);
    }

    [Fact]
    public void One_own_word_never_changes_the_pit_stop_alone()
    {
        var result = ApplyRu("lexicon:\n  tires: { targets: { front: [перёд] } }\n");

        Assert.True(result.Applied);
        Assert.False(new CommandParser(result.Pack).Parse("перёд").IsAccepted);
    }

    [Fact]
    public void Added_words_are_marked_and_the_pack_names_the_file()
    {
        var result = ApplyRu("lexicon:\n  fuel: { action: [плесни] }\n");

        var own = Assert.Single(result.Pack.UserEntries);
        Assert.Equal("плесни", own.Text);
        Assert.Equal(new WordClass(LexiconGroup.Fuel, WordRole.Action), own.Class);
        Assert.Contains(result.Pack.Lexicon.Entries, e => !e.IsUserAddition && e.Text == "залей");
        Assert.Equal(result.File, result.Pack.AdditionsFile);
        Assert.Null(TestPacks.Ru.AdditionsFile);
        Assert.Equal(TestPacks.Ru.Intents, result.Pack.Intents);
    }

    [Fact]
    public void No_file_keeps_the_built_in_pack()
    {
        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Exists);
        Assert.False(result.Applied);
        Assert.Same(TestPacks.Ru, result.Pack);
        Assert.Empty(result.Issues);
        Assert.Equal(Path.Combine(_directory, "ru.yaml"), result.File);
    }

    [Fact]
    public void Template_with_comments_only_adds_nothing()
    {
        var result = ApplyRu("""
            # Your own words for the Russian pack.
            # lexicon:
            """);

        Assert.True(result.Applied);
        Assert.Empty(result.Issues);
        Assert.Empty(result.Pack.UserEntries);
    }

    [Fact]
    public void Created_template_loads_without_errors()
    {
        var path = UserAdditionsLoader.CreateTemplate(_directory, "ru");

        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.Equal(path, result.File);
        Assert.True(result.Applied, string.Join('\n', result.Issues));
        Assert.Empty(result.Issues);
        Assert.Contains("lexicon:", File.ReadAllText(path), StringComparison.Ordinal);
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
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.NotNull(error.Line);
        Assert.Contains("invalid YAML", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_class_rejects_the_whole_file_with_file_line_and_class()
    {
        var path = Write("ru", """
            lexicon:
              fuel: { action: [плесни] }
              tire_flip: { action: [переверни] }
            """);

        var result = UserAdditions.Apply(TestPacks.Ru, _directory);

        Assert.False(result.Applied);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.Equal(3, error.Line);
        Assert.Contains("'tire_flip'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lexicon:\n  tires: { flip: [переверни] }\n", "'tires.flip'")]
    [InlineData("lexicon:\n  tires: { targets: { middle: [середина] } }\n", "'middle'")]
    [InlineData("lexicon:\n  fuel: { fast: [быстро] }\n", "'fuel.fast'")]
    public void Unknown_role_or_target_rejects_the_file(string yaml, string expected)
    {
        var result = ApplyRu(yaml);

        Assert.False(result.Applied);
        Assert.Contains(expected, Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Old_additions_format_is_not_applied_and_explains_the_new_one()
    {
        var result = ApplyRu("""
            language: ru
            intents:
              fuel.add:
                patterns: ["плесни {amount} {unit?}"]
            """);

        Assert.False(result.Applied);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'intents'", error.Message, StringComparison.Ordinal);
        Assert.Contains("classes of 'lexicon'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Word_with_a_slot_rejects_the_whole_file_and_names_word_and_class()
    {
        var result = ApplyRu("lexicon:\n  fuel: { action: [плесни, \"залей {amount}\"] }\n");

        Assert.False(result.Applied);
        var error = Assert.Single(result.Errors);
        Assert.Contains("'залей {amount}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'fuel.action'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_of_a_built_in_word_is_skipped_with_a_warning_and_the_rest_applies()
    {
        var result = ApplyRu("lexicon:\n  fuel: { action: [залей, плесни, плесни] }\n");

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        Assert.Equal(2, result.Warnings.Count());
        Assert.Contains(result.Warnings, w => w.Message.Contains("'залей' is already built into 'fuel.action'", StringComparison.Ordinal));
        Assert.Single(result.Pack.UserEntries);
    }

    [Theory]
    [InlineData("lexicon:\n  tires: { action: [не] }\n", "'не' is in 'negations'", "'tires.action'")]
    [InlineData("lexicon:\n  tires: { action: [трогай] }\n", "'tires.action'", "'tires.keep'")]
    public void Word_with_opposite_meaning_rejects_the_file_and_names_both_classes(string yaml, string first, string second)
    {
        var result = ApplyRu(yaml);

        Assert.False(result.Applied);
        var error = Assert.Single(result.Errors);
        Assert.Contains(first, error.Message, StringComparison.Ordinal);
        Assert.Contains(second, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("conjunctions: [плюс]\nlexicon: {}\n", "conjunctions")]
    [InlineData("lexicon:\n  fuel: { action: \"плесни\" }\n", "list")]
    [InlineData("lexicon: [плесни]\n", "map groups")]
    public void Only_language_and_lexicon_are_allowed(string yaml, string expected)
    {
        var result = ApplyRu(yaml);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void File_for_another_language_is_not_applied()
    {
        var result = ApplyRu("language: en\nlexicon:\n  fuel: { action: [pour] }\n");

        Assert.False(result.Applied);
        Assert.Contains("'en'", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
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
}
