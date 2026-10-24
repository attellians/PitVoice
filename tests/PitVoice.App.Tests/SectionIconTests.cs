using System.IO;
using System.Windows;
using PitVoice.App.Controls;
using Wpf.Ui.Controls;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The section icon (design D2 of refresh-window-visual-style; spec: Section icons).</summary>
public sealed class SectionIconTests
{
    [Fact]
    public void Without_a_symbol_the_first_letter_of_the_title_is_drawn() => Sta.Run(() =>
    {
        var icon = new SectionIcon { Section = Section.Commands, Fallback = "топливо" };

        Assert.True(icon.ShowsLetter);
        Assert.Equal("Т", icon.Letter);

        icon.Symbol = SymbolRegular.Home24;
        Assert.False(icon.ShowsLetter);
        Assert.Equal(string.Empty, icon.Letter);
    });

    [Theory]
    [InlineData(28)]
    [InlineData(32)]
    [InlineData(44)]
    public void Size_gives_a_square_tint(double size) => Sta.Run(() =>
    {
        var icon = new SectionIcon { Symbol = SymbolRegular.Mic24, Size = size };
        icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        icon.Arrange(new Rect(icon.DesiredSize));

        Assert.Equal(size, icon.ActualWidth);
        Assert.Equal(size, icon.ActualHeight);
        Assert.Equal(size >= 40 ? 10 : 8, icon.CornerRadius.TopLeft);
    });

    /// <summary>A symbol name in XAML is parsed at run time: a missing one would break the page only when it opens.</summary>
    [Fact]
    public void Every_symbol_used_in_xaml_exists()
    {
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(source.FullName, "src", "PitVoice.App")))
        {
            source = source.Parent ?? throw new DirectoryNotFoundException("src/PitVoice.App not found");
        }

        var used = Directory.EnumerateFiles(Path.Combine(source.FullName, "src", "PitVoice.App"), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "Symbol=\"([A-Za-z0-9]+)\"").Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Gauge24", used);
        Assert.DoesNotContain(used, name => !Enum.TryParse<SymbolRegular>(name, out _));
    }

    [Fact]
    public void Section_picks_its_hue_and_tint_keys()
    {
        Assert.Equal("SectionModelsBrush", SectionIcon.BrushKey(Section.Models));
        Assert.Equal("SectionModelsTintBrush", SectionIcon.TintKey(Section.Models));
        foreach (var section in Enum.GetValues<Section>())
        {
            Assert.StartsWith("Section", SectionIcon.BrushKey(section), StringComparison.Ordinal);
        }
    }
}
