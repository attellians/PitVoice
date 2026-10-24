using PitVoice.App.Overlay;
using PitVoice.App.Overlay.Vr;
using PitVoice.Core.Settings;
using PitVoice.Core.Vr;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>Where the indicator stands in the headset (add-vr-overlay D7; spec: Place in the field of view).</summary>
public sealed class VrPlacementTests
{
    private static readonly MonitorArea Main = new(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1.0, Primary: true);
    private static readonly MonitorArea Left = new(new PixelRect(-2560, 0, 0, 1440), new PixelRect(-2560, 0, 0, 1392), 1.5, Primary: false);
    private static readonly IReadOnlyList<MonitorArea> Both = [Left, Main];

    /// <summary>Kind A at 100 %: the frame is 72 units wide, its center is the icon center.</summary>
    private static readonly double FrameA = ((OverlayLayout.Padding * 2) + OverlayLayout.IconSize) * VrPlacement.DegreesPerUnit(100);

    [Fact]
    public void Center_of_the_monitor_is_straight_ahead()
    {
        var place = VrPlacement.Place(new ScreenPoint(1280, 720), Main, 100, 0);

        Assert.Equal(0, place.Yaw, 3);
        Assert.Equal(0, place.Pitch, 3);
        Assert.Equal(FrameA, place.Width, 3);
    }

    [Theory]
    [InlineData(0, 0, -1, 1)] // top left
    [InlineData(2559, 0, 1, 1)] // top right
    [InlineData(0, 1439, -1, -1)] // bottom left
    [InlineData(2559, 1439, 1, -1)] // bottom right
    public void Corners_of_the_monitor_are_the_corners_of_the_view_with_the_icon_inside(int x, int y, int right, int up)
    {
        var place = VrPlacement.Place(new ScreenPoint(x, y), Main, 100, 0);
        var radius = VrPlacement.IconDegrees / 2;

        Assert.Equal(right * (VrPlacement.HalfWidth - radius), place.Yaw, 3);
        Assert.Equal(up * (VrPlacement.HalfHeight - radius), place.Pitch, 3);
    }

    /// <summary>Spec: Default place — top right, away from the center of the view.</summary>
    [Fact]
    public void Default_place_is_the_top_right_of_the_view()
    {
        var place = VrPlacement.Resolve(null, Both, 100, 0);

        Assert.InRange(place.Yaw, 20, VrPlacement.HalfWidth);
        Assert.InRange(place.Pitch, 14, VrPlacement.HalfHeight);
    }

    /// <summary>Kind B at the right edge: the longest word stays inside, the icon moves left of the edge.</summary>
    [Fact]
    public void Kind_B_at_the_right_edge_keeps_the_word_inside()
    {
        const double wordRoom = 110;
        var perUnit = VrPlacement.DegreesPerUnit(100);

        var place = VrPlacement.Place(new ScreenPoint(2559, 720), Main, 100, wordRoom);

        var right = place.Yaw + (place.Width / 2) - (OverlayLayout.Padding * perUnit);
        Assert.Equal(VrPlacement.HalfWidth, right, 3);
        Assert.Equal(((OverlayLayout.Padding * 2) + OverlayLayout.IconSize + wordRoom) * perUnit, place.Width, 3);
    }

    [Fact]
    public void Kind_B_grows_to_the_right_and_the_icon_stays()
    {
        var a = VrPlacement.Place(new ScreenPoint(1280, 720), Main, 100, 0);
        var b = VrPlacement.Place(new ScreenPoint(1280, 720), Main, 100, 110);

        Assert.Equal(a.Yaw - (a.Width / 2), b.Yaw - (b.Width / 2), 3);
        Assert.True(b.Width > a.Width);
    }

    [Fact]
    public void Size_200_doubles_the_frame_and_keeps_the_bigger_icon_inside()
    {
        var place = VrPlacement.Place(new ScreenPoint(0, 0), Main, 200, 0);

        Assert.Equal(FrameA * 2, place.Width, 3);
        Assert.Equal(-(VrPlacement.HalfWidth - VrPlacement.IconDegrees), place.Yaw, 3);
        Assert.Equal(VrPlacement.HalfHeight - VrPlacement.IconDegrees, place.Pitch, 3);
    }

    /// <summary>Spec: Saved monitor removed — the default place, like the indicator on the monitor.</summary>
    [Fact]
    public void Saved_place_on_a_removed_monitor_gives_the_default_place()
    {
        var gone = VrPlacement.Resolve(new ScreenPoint(-2000, 1300), [Main], 100, 0);

        Assert.Equal(VrPlacement.Resolve(null, [Main], 100, 0), gone);
        Assert.NotEqual(gone, VrPlacement.Resolve(new ScreenPoint(-2000, 1300), Both, 100, 0));
    }

    [Fact]
    public void Place_on_the_left_monitor_maps_inside_that_monitor()
    {
        var place = VrPlacement.Resolve(new ScreenPoint(-640, 1080), Both, 100, 0);

        Assert.Equal(12.5, place.Yaw, 3); // three quarters across
        Assert.Equal(-9, place.Pitch, 3); // three quarters down
        Assert.IsType<VrPlace>(place);
    }
}
