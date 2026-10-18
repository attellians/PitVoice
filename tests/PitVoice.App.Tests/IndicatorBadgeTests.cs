using System.Windows;
using PitVoice.App.Overlay;
using PitVoice.Core.Settings;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>Animations of the indicator (design D4; spec: Animated transitions).</summary>
public sealed class IndicatorBadgeTests
{
    [Theory]
    [InlineData(IndicatorKind.A)]
    [InlineData(IndicatorKind.B)]
    public void Idle_runs_no_animations_after_a_phrase(IndicatorKind kind) => Sta.Run(() =>
    {
        var window = new IndicatorWindow();
        window.Badge.Arrange(kind, labelMax: 120);
        window.Show();
        try
        {
            var badge = window.Badge;
            badge.Show(IndicatorVisual.Listening, "Listening", animate: true);
            Assert.True(badge.RunningEndless > 0); // the pulse
            Sta.Pump(TimeSpan.FromMilliseconds(100));

            badge.Show(IndicatorVisual.Recognizing, "Recognizing", animate: true);
            Assert.True(badge.RunningEndless > 0); // the spin
            Sta.Pump(TimeSpan.FromMilliseconds(100));

            badge.Show(IndicatorVisual.Done, "Done", animate: true);
            Assert.Equal(0, badge.RunningEndless);
            Sta.Pump(TimeSpan.FromMilliseconds(100));

            badge.Show(IndicatorVisual.Idle, string.Empty, animate: true);
            Assert.Equal(0, badge.RunningEndless);
            Assert.True(badge.RunningAnimations > 0); // the transition to idle itself

            Sta.Pump(TimeSpan.FromSeconds(1.5));
            Assert.Equal(0, badge.RunningAnimations);
            Assert.Equal(OverlaySettings.DefaultOpacity / 100.0 * OverlaySettings.IdleOpacityShare, badge.Opacity, precision: 3);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Press_interrupts_the_result_at_once() => Sta.Run(() =>
    {
        var badge = new IndicatorBadge();
        badge.Show(IndicatorVisual.Error, "Error", animate: true);

        badge.Show(IndicatorVisual.Listening, "Listening", animate: true);

        Assert.Equal(IndicatorVisual.Listening, badge.Visual);
        Assert.True(badge.RunningEndless > 0);
    });

    [Fact]
    public void Stop_all_ends_every_animation_and_the_next_show_restarts_the_pulse() => Sta.Run(() =>
    {
        var badge = new IndicatorBadge();
        badge.Show(IndicatorVisual.Listening, "Listening", animate: true);

        badge.StopAll();
        Assert.Equal(0, badge.RunningAnimations);

        badge.Show(IndicatorVisual.Listening, "Listening", animate: false);
        Assert.True(badge.RunningEndless > 0);
    });

    [Fact]
    public void Kind_A_window_is_only_the_icon_and_kind_B_has_room_for_the_word() => Sta.Run(() =>
    {
        var badge = new IndicatorBadge();

        badge.Arrange(IndicatorKind.A, labelMax: 120);
        Assert.Equal((OverlayLayout.Padding * 2) + OverlayLayout.IconSize, badge.ContentWidth);
        Assert.Equal(0, badge.WordRoom);

        badge.Arrange(IndicatorKind.B, labelMax: 120);
        Assert.Equal((OverlayLayout.Padding * 2) + OverlayLayout.IconSize + badge.WordRoom, badge.ContentWidth);
        Assert.True(badge.WordRoom > 120);
        Assert.Equal(HorizontalAlignment.Left, ((FrameworkElement)badge.Children[1]).HorizontalAlignment);
    });
}
