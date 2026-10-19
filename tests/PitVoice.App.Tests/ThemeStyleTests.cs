using System.Windows.Media;
using Wpf.Ui.Appearance;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The section hue sets of both themes (design D1 of refresh-window-visual-style; spec: Section colors).</summary>
public sealed class ThemeStyleTests
{
    [Fact]
    public void Both_sets_have_the_same_keys_and_every_section_brush_has_a_tint() => Sta.Run(() =>
    {
        var dark = Themes.LoadSet(ApplicationTheme.Dark);
        var light = Themes.LoadSet(ApplicationTheme.Light);
        var darkKeys = dark.Keys.Cast<string>().Order(StringComparer.Ordinal).ToArray();
        var lightKeys = light.Keys.Cast<string>().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(darkKeys, lightKeys);
        var sections = darkKeys.Where(k => k.StartsWith("Section", StringComparison.Ordinal) && !k.EndsWith("TintBrush", StringComparison.Ordinal)).ToArray();
        Assert.Equal(7, sections.Length); // six sections and the alert hue
        foreach (var key in sections)
        {
            Assert.EndsWith("Brush", key, StringComparison.Ordinal);
            var tint = key[..^"Brush".Length] + "TintBrush";
            Assert.Contains(tint, darkKeys);
            foreach (var set in new[] { dark, light })
            {
                var brush = Assert.IsType<SolidColorBrush>(set[key]);
                var tintBrush = Assert.IsType<SolidColorBrush>(set[tint]);
                Assert.Equal(255, brush.Color.A);
                Assert.InRange(tintBrush.Color.A, 25, 51); // 10–20 % alpha
            }
        }

        foreach (var state in new[] { "StateOkBrush", "StateWarnBrush", "StateErrorBrush", "DividerBrush" })
        {
            Assert.IsType<SolidColorBrush>(dark[state]);
            Assert.IsType<SolidColorBrush>(light[state]);
        }
    });

    [Fact]
    public void Light_hues_are_darker_than_dark_ones() => Sta.Run(() =>
    {
        var dark = Themes.LoadSet(ApplicationTheme.Dark);
        var light = Themes.LoadSet(ApplicationTheme.Light);
        foreach (var key in dark.Keys.Cast<string>().Where(k => k.StartsWith("Section", StringComparison.Ordinal) && !k.EndsWith("TintBrush", StringComparison.Ordinal)))
        {
            var d = ((SolidColorBrush)dark[key]).Color;
            var l = ((SolidColorBrush)light[key]).Color;
            Assert.True(Luma(l) < Luma(d), $"{key}: light {l} is not darker than dark {d}");
        }
    });

    private static double Luma(Color c) => (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B);
}
