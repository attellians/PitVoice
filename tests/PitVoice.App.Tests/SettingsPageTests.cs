using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The restyled settings page binds every setting it bound before (design D7; spec: Same settings after the restyle).</summary>
public sealed partial class SettingsPageTests
{
    /// <summary>What SettingsPage.xaml bound before refresh-window-visual-style.</summary>
    private static readonly string[] Before =
    [
        "SaveError",
        "Languages", "Language", "ModelMissing", "OpenModelsCommand",
        "InterfaceLanguages", "InterfaceLanguage",
        "Themes", "Theme",
        "PttText", "AssignCommand", "AssignMessage",
        "Modes", "Mode",
        "Microphones", "Microphone",
        "Outputs", "Output",
        "Volume", "PreviewCommand", "PreviewMessage",
        "Units", "Unit",
        "ThresholdText", "ApplyThresholdCommand", "ThresholdError",
        "Diagnostics.CollectCommand", "Diagnostics.OpenLogsCommand", "Diagnostics.Message",
    ];

    [Fact]
    public void Every_setting_bound_before_the_restyle_is_still_bound()
    {
        var xaml = File.ReadAllText(Path.Combine(SourceDirectory(), "Pages", "SettingsPage.xaml"));
        var bound = BindingPath().Matches(xaml).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Before, p => !bound.Contains(p));
    }

    [Fact]
    public void Settings_rows_are_cards_with_list_rows()
    {
        var xaml = File.ReadAllText(Path.Combine(SourceDirectory(), "Pages", "SettingsPage.xaml"));

        Assert.Equal(5, Regex.Matches(xaml, "Style=\"\\{StaticResource Card\\}\"").Count); // Язык и вид, PTT, Звук, Распознавание, Диагностика
        Assert.True(Regex.Matches(xaml, "Style=\"\\{StaticResource ListRow(First)?\\}\"").Count >= 10);
        Assert.DoesNotContain("ui:Card", xaml, StringComparison.Ordinal);
    }

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

    [GeneratedRegex(@"\{Binding\s+([A-Za-z0-9_.]+)")]
    private static partial Regex BindingPath();
}
