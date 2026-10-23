using System.IO;
using System.Text.RegularExpressions;
using PitVoice.App.Localization;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>Interface dictionaries (design decision 7, spec: Interface language).</summary>
[Collection("Interface language")]
public sealed partial class LocalizationTests
{
    private static readonly IReadOnlyDictionary<string, string> English = Loc.LoadDictionary("en")!;
    private static readonly IReadOnlyDictionary<string, string> Russian = Loc.LoadDictionary("ru")!;

    [Fact]
    public void Both_languages_have_a_dictionary() => Assert.Equal(["en", "ru"], Loc.Languages);

    [Fact]
    public void Dictionaries_have_the_same_keys()
    {
        Assert.Empty(English.Keys.Except(Russian.Keys));
        Assert.Empty(Russian.Keys.Except(English.Keys));
        Assert.DoesNotContain(Russian, p => string.IsNullOrWhiteSpace(p.Value));
    }

    [Theory]
    [InlineData("AdditionsTitle")]
    [InlineData("AdditionsNone")]
    [InlineData("AdditionsNotApplied")]
    [InlineData("AdditionsApplied")]
    [InlineData("AdditionsMissingWords")]
    [InlineData("OpenFileButton")]
    [InlineData("ReloadButton")]
    [InlineData("OpenFileFailed")]
    public void Keys_of_the_additions_file_are_gone(string key)
    {
        Assert.False(English.ContainsKey(key));
        Assert.False(Russian.ContainsKey(key));
    }

    [Theory]
    [InlineData("PhrasesTitle", "Свои фразы")]
    [InlineData("PhrasesConfigure", "Настроить")]
    [InlineData("PhrasesStartOver", "Начать заново")]
    [InlineData("LeaveSave", "Сохранить")]
    [InlineData("LeaveDiscard", "Не сохранять")]
    [InlineData("LeaveStay", "Остаться")]
    public void Keys_of_the_phrase_builder_are_in_both_languages(string key, string russian)
    {
        Assert.Equal(russian, Russian[key]);
        Assert.False(string.IsNullOrWhiteSpace(English[key]));
    }

    [Fact]
    public void Every_key_used_in_xaml_and_code_is_in_english()
    {
        var source = SourceDirectory();
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.xaml", SearchOption.AllDirectories))
        {
            used.UnionWith(XamlKey().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            used.UnionWith(CodeKey().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value));
        }

        used.UnionWith(Texts.DynamicKeys());

        Assert.True(used.Count > 100, $"only {used.Count} keys found; is the source directory right? {source}");
        Assert.DoesNotContain(used, k => !English.ContainsKey(k));
    }

    [Fact]
    public void Missing_translation_falls_back_to_english_and_missing_key_shows_the_key()
    {
        var loc = Loc.Instance;
        var previous = loc.Language;
        try
        {
            loc.SetLanguage("de");
            Assert.Equal("en", loc.Language);
            Assert.Equal(English["NavHome"], loc["NavHome"]);
            Assert.Equal("NoSuchKey", loc["NoSuchKey"]);

            var changed = new List<string?>();
            loc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            loc.SetLanguage("ru");
            Assert.Equal(Russian["NavHome"], loc["NavHome"]);
            Assert.Contains("Item[]", changed);
        }
        finally
        {
            loc.SetLanguage(previous);
        }
    }

    /// <summary>The app's source directory, found from the test output upwards.</summary>
    private static string SourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "PitVoice.App");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/PitVoice.App not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\{loc:Tr\s+(?:Key=)?([A-Za-z0-9_.]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.(?:T|F)\(""([^""{}]+)""")]
    private static partial Regex CodeKey();
}
