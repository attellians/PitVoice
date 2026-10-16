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

    /// <summary>Spec: Components match the notices file — the same names, versions and licenses as THIRD-PARTY-NOTICES.md.</summary>
    [Fact]
    public void Components_match_the_notices_file()
    {
        var rows = File.ReadAllLines(Path.Combine(RepositoryRoot(), "THIRD-PARTY-NOTICES.md"))
            .SkipWhile(l => l != "## Components")
            .Skip(4) // blank line, header, separator
            .TakeWhile(l => l.StartsWith('|'))
            .Select(l => l.Split(" | "))
            .Select(cells => (Name: cells[0].TrimStart('|', ' '), Version: cells[1], License: cells[2]))
            .ToArray();

        Assert.NotEmpty(rows);
        Assert.Equal(rows, AboutViewModel.LoadComponents().Select(c => (c.Name, c.Version, c.License)));
    }

    [Fact]
    public void Project_license_is_MIT_in_both_languages()
    {
        foreach (var language in Loc.Languages)
        {
            Assert.Contains("MIT", Loc.LoadDictionary(language)!["ProjectLicense"], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("0.1.3", true, "https://github.com/attellians/PitVoice/blob/v0.1.3/THIRD-PARTY-NOTICES.md")]
    [InlineData("0.1.3", false, "https://github.com/attellians/PitVoice/blob/main/THIRD-PARTY-NOTICES.md")]
    public void Notices_link_points_to_the_tag_of_the_installed_version(string version, bool installed, string expected) =>
        Assert.Equal(expected, AboutViewModel.NoticesAddress(version, installed));

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
