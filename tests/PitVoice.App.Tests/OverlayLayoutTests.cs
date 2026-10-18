using PitVoice.App.Overlay;
using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>Where the indicator and "Done" go (design D5, D6; spec: Moving the indicator, Indicator kinds).</summary>
public sealed class OverlayLayoutTests
{
    // Main monitor 1920×1080 at 100 % with the taskbar at the bottom, second one on the left 2560×1440 at 150 %.
    private static readonly MonitorArea Main = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.0, Primary: true);
    private static readonly MonitorArea Left = new(new PixelRect(-2560, 0, 0, 1440), new PixelRect(-2560, 0, 0, 1392), 1.5, Primary: false);
    private static readonly IReadOnlyList<MonitorArea> Both = [Left, Main];

    [Fact]
    public void Default_place_is_the_top_right_corner_of_the_main_work_area()
    {
        var (center, monitor) = OverlayLayout.Resolve(null, Both, 100);

        Assert.Same(Main, monitor);
        Assert.Equal(new ScreenPoint(1920 - 24 - 22, 24 + 22), center);
    }

    [Fact]
    public void Saved_place_on_a_present_monitor_is_used()
    {
        var (center, monitor) = OverlayLayout.Resolve(new ScreenPoint(-2000, 1300), Both, 100);

        Assert.Same(Left, monitor);
        Assert.Equal(new ScreenPoint(-2000, 1300), center);
    }

    /// <summary>Spec: Monitor removed — the default place is used; the caller does not save it.</summary>
    [Fact]
    public void Saved_place_on_a_removed_monitor_gives_the_default_place()
    {
        var (center, monitor) = OverlayLayout.Resolve(new ScreenPoint(-2000, 1300), [Main], 100);

        Assert.Same(Main, monitor);
        Assert.Equal(OverlayLayout.Default(Main, 100), center);
    }

    [Fact]
    public void Saved_place_is_kept_on_its_monitor_when_the_icon_grew()
    {
        var (center, _) = OverlayLayout.Resolve(new ScreenPoint(1915, 5), [Main], 200);

        Assert.Equal(new ScreenPoint(1920 - 44, 44), center);
    }

    /// <summary>Spec: Dragged off the screen — the whole icon stays on the monitor.</summary>
    [Theory]
    [InlineData(5000, 500, 1920 - 22, 500)]
    [InlineData(-10, -10, 22, 22)]
    [InlineData(900, 2000, 900, 1080 - 22)]
    public void Clamp_keeps_the_icon_on_the_monitor(int x, int y, int expectedX, int expectedY)
    {
        Assert.Equal(new ScreenPoint(expectedX, expectedY), OverlayLayout.Clamp(new ScreenPoint(x, y), Main, 100));
    }

    [Fact]
    public void Clamp_uses_the_scale_of_the_monitor()
    {
        // 44 × 150 % size × 150 % monitor = 99 px, radius 50.
        Assert.Equal(new ScreenPoint(-2560 + 50, 50), OverlayLayout.Clamp(new ScreenPoint(-3000, -3000), Left, 150));
    }

    [Fact]
    public void Nearest_monitor_is_used_for_a_point_between_monitors()
    {
        Assert.Same(Main, OverlayLayout.NearestMonitor(new ScreenPoint(2100, 500), Both));
        Assert.Same(Left, OverlayLayout.NearestMonitor(new ScreenPoint(-1000, 1500), Both));
    }

    /// <summary>Spec: Kind B near the right edge — the icon stops left of the edge so the longest word fits to its right.</summary>
    [Fact]
    public void Kind_B_keeps_room_for_the_word_at_the_right_edge()
    {
        // 120 units of word room at 100 % on a 100 % monitor.
        Assert.Equal(new ScreenPoint(1920 - 22 - 120, 500), OverlayLayout.Clamp(new ScreenPoint(1900, 500), Main, 100, wordRoom: 120));
        Assert.Equal(new ScreenPoint(300, 500), OverlayLayout.Clamp(new ScreenPoint(300, 500), Main, 100, wordRoom: 120));

        // 150 % size on the 150 % monitor: 120 × 2.25 = 270 px.
        Assert.Equal(-50 - 270, OverlayLayout.Clamp(new ScreenPoint(-10, 500), Left, 150, wordRoom: 120).X);
    }

    [Fact]
    public void Kind_B_default_place_leaves_room_for_the_word()
    {
        Assert.Equal(new ScreenPoint(1920 - 24 - 22 - 120, 24 + 22), OverlayLayout.Default(Main, 100, wordRoom: 120));
        Assert.Equal(OverlayLayout.Default(Main, 100, wordRoom: 120), OverlayLayout.Resolve(null, Both, 100, wordRoom: 120).Center);
    }

    [Fact]
    public void Icon_center_is_at_the_left_of_the_window()
    {
        Assert.Equal(36.0, OverlayLayout.IconCenterInWindow);
    }

    /// <summary>Spec: Done button centered — at any size and kind its horizontal center is the icon center.</summary>
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public void Done_button_is_centered_under_the_icon(int scale)
    {
        foreach (var center in new[] { new ScreenPoint(1850, 70), new ScreenPoint(300, 400) })
        {
            // The kind changes the window, not the anchor: the button depends only on the icon center.
            var rect = OverlayLayout.DoneButton(center, scale, Main, width: 91, height: 32);

            Assert.True(Math.Abs(((rect.Left + rect.Right) / 2.0) - center.X) <= 0.5);
            Assert.Equal((int)Math.Round(center.Y + (OverlayLayout.IconPixels(scale, 1) / 2) + OverlayLayout.DoneGap), rect.Top);
        }
    }

    [Fact]
    public void Done_button_goes_above_the_icon_at_the_bottom_edge()
    {
        var rect = OverlayLayout.DoneButton(new ScreenPoint(500, 1058), 100, Main, width: 90, height: 32);

        Assert.True(rect.Bottom <= 1058 - 22);
        Assert.Equal(500, (rect.Left + rect.Right) / 2);
    }

    [Fact]
    public void Done_button_scales_with_the_monitor()
    {
        var rect = OverlayLayout.DoneButton(new ScreenPoint(-1000, 300), 100, Left, width: 135, height: 48);

        Assert.Equal((int)Math.Round(300 + 33 + 15.0), rect.Top);
        Assert.True(Math.Abs(((rect.Left + rect.Right) / 2.0) - -1000) <= 0.5);
    }
}
