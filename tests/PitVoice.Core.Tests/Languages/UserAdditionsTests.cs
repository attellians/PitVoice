using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using Xunit;

namespace PitVoice.Core.Tests.Languages;

/// <summary>User pack additions (FR-LANG-5): reading the file, checking it and merging it into the built-in pack.</summary>
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
    public void Own_phrase_works_and_built_in_patterns_still_work()
    {
        var result = ApplyRu("""
            language: ru
            intents:
              fuel.add:
                patterns: ["плесни {amount} {unit?}"]
            """);

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        Assert.Empty(result.Issues);
        var command = Single(result.Pack, "плесни тридцать литров");
        Assert.Equal(IntentCatalog.FuelAdd, command.Intent);
        Assert.Equal(30, command.Amount);
        Assert.Equal("liters", command.Unit);

        var builtIn = Single(result.Pack, "залей двадцать литров");
        Assert.Equal(IntentCatalog.FuelAdd, builtIn.Intent);
        Assert.Equal(20, builtIn.Amount);
    }

    [Fact]
    public void Added_templates_are_marked_and_the_pack_names_the_file()
    {
        var result = ApplyRu("""
            language: ru
            intents:
              fuel.add:
                patterns: ["плесни {amount} {unit?}"]
            """);

        var templates = result.Pack.Intents[IntentCatalog.FuelAdd].Templates;
        var own = Assert.Single(templates, t => t.IsUserAddition);
        Assert.Equal("плесни {amount} {unit?}", own.Source);
        Assert.Contains(templates, t => !t.IsUserAddition);
        Assert.Equal(result.File, result.Pack.AdditionsFile);
        Assert.Null(TestPacks.Ru.AdditionsFile);
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
            # Your own phrases for the Russian pack.
            # language: ru
            """);

        Assert.True(result.Applied);
        Assert.Empty(result.Issues);
        Assert.DoesNotContain(result.Pack.Templates, t => t.IsUserAddition);
    }

    [Fact]
    public void Malformed_file_keeps_the_built_in_pack_and_names_the_file_and_line()
    {
        var path = Write("ru", """
            language: ru
            intents:
              fuel.add:
                patterns: ["плесни {amount} {unit?}"
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
    public void Unknown_intent_rejects_the_whole_file_with_file_line_and_intent()
    {
        var result = ApplyRu("""
            language: ru
            intents:
              fuel.add:
                patterns: ["плесни {amount} {unit?}"]
              tires.flip:
                patterns: ["переверни шины"]
            """);

        Assert.False(result.Applied);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors);
        Assert.Equal(result.File, error.File);
        Assert.Equal(6, error.Line);
        Assert.Contains("tires.flip", error.Message, StringComparison.Ordinal);
        Assert.Contains("tires.flip", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_slot_rejects_the_whole_file_and_names_pattern_and_slot()
    {
        var result = ApplyRu("""
            language: ru
            intents:
              fuel.add:
                patterns:
                  - "плесни {amount} {unit?}"
                  - "залей {liters}"
            """);

        Assert.False(result.Applied);
        Assert.Same(TestPacks.Ru, result.Pack);
        var error = Assert.Single(result.Errors, e => e.Message.Contains("unknown slot {liters}", StringComparison.Ordinal));
        Assert.Equal(6, error.Line);
        Assert.Contains("залей {liters}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_of_a_built_in_pattern_is_skipped_with_a_warning_and_the_rest_applies()
    {
        var result = ApplyRu("""
            language: ru
            intents:
              fuel.add:
                patterns:
                  - "залей {amount} {unit?}"
                  - "плесни {amount} {unit?}"
            """);

        Assert.True(result.Applied, string.Join('\n', result.Issues));
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("залей {amount} {unit?}", warning.Message, StringComparison.Ordinal);
        Assert.Equal(5, warning.Line);
        Assert.Single(result.Pack.Templates, t => t.IsUserAddition);
    }

    [Fact]
    public void Single_word_pattern_of_a_pit_command_is_an_error()
    {
        var result = ApplyRu("""
            language: ru
            intents:
              tires.all:
                patterns: ["резина"]
            """);

        Assert.False(result.Applied);
        var error = Assert.Single(result.Errors);
        Assert.Contains("single word", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("language: ru\nintents:\n  fuel.add:\n    response: \"льём\"\n", "response")]
    [InlineData("language: ru\nconjunctions: [плюс]\nintents: {}\n", "conjunctions")]
    [InlineData("language: ru\nintents:\n  fuel.add:\n    patterns: \"плесни {amount}\"\n", "list")]
    [InlineData("intents:\n  fuel.add:\n    patterns: [\"плесни {amount}\"]\n", "'language' is required")]
    public void Only_language_and_patterns_are_allowed(string yaml, string expected)
    {
        var result = ApplyRu(yaml);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void File_for_another_language_is_not_applied()
    {
        var result = ApplyRu("""
            language: en
            intents:
              fuel.add:
                patterns: ["pour {amount} {unit?}"]
            """);

        Assert.False(result.Applied);
        Assert.Contains("'en'", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Additions_for_a_language_without_pack_are_ignored_with_a_warning()
    {
        var path = Write("de", """
            language: de
            intents:
              fuel.add:
                patterns: ["tanke {amount} {unit?}"]
            """);
        Write("ru", "language: ru\nintents: {}\n");

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
