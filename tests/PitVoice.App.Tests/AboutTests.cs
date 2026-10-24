using System.IO;
using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>"About" lists third-party components from the generated resource (spec: About section).</summary>
public sealed class AboutTests
{
    [Fact]
    public void Components_list_has_names_versions_licenses_and_links()
    {
        var components = AboutViewModel.LoadComponents();

        Assert.Contains(components, c => c.Name == "Vosk" && c.License == "Apache-2.0" && c.Version.Length > 0);
        Assert.Contains(components, c => c.Name == "WPF-UI" && c.License == "MIT");
        Assert.Contains(components, c => c.Name == "Serilog" && c.License == "Apache-2.0");
        Assert.All(components, c => Assert.StartsWith("https://", c.Url, StringComparison.Ordinal));
    }

    /// <summary>Spec: Components match the notices file — "About" shows the same names in the same order as THIRD-PARTY-NOTICES.md.</summary>
    [Fact]
    public void Components_match_the_notices_file()
    {
        var names = File.ReadAllLines(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md"))
            .SkipWhile(l => l != "## Components")
            .Skip(4) // blank line, header, separator
            .TakeWhile(l => l.StartsWith('|'))
            .Select(l => l.Split(" | ")[0].TrimStart('|', ' '))
            .ToArray();

        Assert.NotEmpty(names);
        Assert.Equal(names, AboutViewModel.LoadComponents().Select(c => c.Name));
    }

    /// <summary>Spec: Open about, English interface — "Лицензия MIT" / "MIT License", no colon and no period.</summary>
    [Theory]
    [InlineData("ru", "Лицензия MIT")]
    [InlineData("en", "MIT License")]
    public void Project_license_line(string language, string expected)
    {
        Assert.Equal(expected, Loc.LoadDictionary(language)!["ProjectLicense"]);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PitVoice.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("PitVoice.sln not found above " + AppContext.BaseDirectory);
    }
}
