using PitVoice.App.Overlay;
using PitVoice.App.Overlay.Vr;
using PitVoice.Core.Settings;
using PitVoice.Core.Vr;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>Frames of the indicator for the headset (add-vr-overlay D5; tasks 1.3, 4.2).</summary>
public sealed class VrIndicatorTests
{
    private static readonly MonitorArea Main = new(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1.0, Primary: true);

    private static readonly IndicatorView Idle = new(IndicatorVisual.Idle, Visible: true, Moving: false);

    [Fact]
    public void Appearing_sends_place_frame_and_visible_and_a_state_change_sends_a_frame() => Sta.Run(() =>
    {
        var link = new Link();
        using var vr = new VrIndicator(link, () => [Main]);

        vr.Update(Idle, OverlaySettings.Default, "ru", Word);

        Assert.Collection(
            link.Messages,
            m => Assert.Equal(new VrVisible(false), m),
            m => Assert.IsType<VrPlace>(m),
            m => Assert.IsType<VrFrame>(m),
            m => Assert.Equal(new VrVisible(true), m));
        link.Messages.Clear();

        vr.Update(Idle with { Visual = IndicatorVisual.Done }, OverlaySettings.Default, "ru", Word);

        Assert.IsType<VrFrame>(Assert.Single(link.Messages));
    });

    /// <summary>Task 1.3: the clocks of a badge outside any window tick, the spinner turns between frames.</summary>
    [Fact]
    public void Animations_outside_a_window_tick_and_frames_follow_them() => Sta.Run(() =>
    {
        var link = new Link();
        using var vr = new VrIndicator(link, () => [Main]);
        vr.Update(Idle with { Visual = IndicatorVisual.Recognizing }, OverlaySettings.Default, "ru", Word);
        link.Messages.Clear();

        Sta.Pump(TimeSpan.FromMilliseconds(500));

        var frames = link.Messages.OfType<VrFrame>().ToList();
        Assert.InRange(frames.Count, 8, 20); // 30 a second, with slack for a busy machine
        var distinct = frames.Select(f => Convert.ToHexString(f.Rgba)).Distinct().Count();
        Assert.True(distinct >= frames.Count / 2, $"the spinner turns between frames: {distinct} different of {frames.Count}");
    });

    [Fact]
    public void Idle_after_the_transition_sends_no_frames() => Sta.Run(() =>
    {
        var link = new Link();
        using var vr = new VrIndicator(link, () => [Main]);
        vr.Update(Idle with { Visual = IndicatorVisual.Done }, OverlaySettings.Default, "ru", Word);
        vr.Update(Idle, OverlaySettings.Default, "ru", Word);
        Sta.Pump(TimeSpan.FromMilliseconds(600)); // the transition to idle ends
        link.Messages.Clear();

        Sta.Pump(TimeSpan.FromSeconds(1));

        Assert.Empty(link.Messages);
    });

    /// <summary>Spec: Kind and size follow the overlay settings.</summary>
    [Fact]
    public void Size_and_kind_change_the_frame_and_the_place() => Sta.Run(() =>
    {
        var link = new Link();
        using var vr = new VrIndicator(link, () => [Main]);
        vr.Update(Idle, OverlaySettings.Default, "ru", Word);
        var small = vr.FrameSize;
        var place = link.Messages.OfType<VrPlace>().Single();

        link.Messages.Clear();
        vr.Update(Idle, OverlaySettings.Default with { Scale = 150 }, "ru", Word);

        Assert.InRange(vr.FrameSize.Width, (small.Width * 3 / 2) - 2, (small.Width * 3 / 2) + 2);
        Assert.InRange(vr.FrameSize.Height, (small.Height * 3 / 2) - 2, (small.Height * 3 / 2) + 2);
        Assert.Equal(place.Width * 1.5f, link.Messages.OfType<VrPlace>().Single().Width, 3);

        vr.Update(Idle, OverlaySettings.Default with { Scale = 150, Kind = IndicatorKind.B }, "ru", Word);
        Assert.True(vr.FrameSize.Width > small.Width * 3 / 2 + 50);
    });

    [Fact]
    public void Hiding_sends_visible_false_once_and_stops_the_frames() => Sta.Run(() =>
    {
        var link = new Link();
        using var vr = new VrIndicator(link, () => [Main]);
        vr.Update(Idle with { Visual = IndicatorVisual.Listening }, OverlaySettings.Default, "ru", Word);
        link.Messages.Clear();

        vr.Update(Idle with { Visible = false }, OverlaySettings.Default, "ru", Word);
        vr.Update(Idle with { Visible = false }, OverlaySettings.Default, "ru", Word);
        Sta.Pump(TimeSpan.FromMilliseconds(300));

        Assert.Equal([new VrVisible(false)], link.Messages);
    });

    [Fact]
    public void Dragging_on_the_monitor_moves_the_place() => Sta.Run(() =>
    {
        var link = new Link();
        using var vr = new VrIndicator(link, () => [Main]);
        vr.Update(Idle with { Moving = true }, OverlaySettings.Default, "ru", Word);
        link.Messages.Clear();

        vr.MoveTo(new ScreenPoint(30, 1410), Main);

        var place = Assert.IsType<VrPlace>(Assert.Single(link.Messages));
        Assert.True(place.Yaw < -20 && place.Pitch < -14);
    });

    [Fact]
    public void Premultiplied_pixels_become_straight_rgba()
    {
        byte[] bgra = [0, 0, 0, 0, 40, 80, 120, 128, 255, 255, 255, 255];

        Assert.Equal([0, 0, 0, 0, 239, 159, 80, 128, 255, 255, 255, 255], VrIndicator.ToRgba(bgra));
    }

    private static string Word(IndicatorVisual visual) => visual == IndicatorVisual.Idle ? string.Empty : visual.ToString();

    private sealed class Link : IVrLink
    {
        public List<VrMessage> Messages { get; } = [];

        public void Send(VrMessage message) => Messages.Add(message);
    }
}
