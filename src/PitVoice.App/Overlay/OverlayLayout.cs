using PitVoice.Core.Settings;

namespace PitVoice.App.Overlay;

/// <summary>A rectangle in physical pixels of the virtual screen; <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public double CenterX => (Left + Right) / 2.0;

    public bool Contains(ScreenPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
}

/// <summary>A monitor: its whole area, the area without the taskbar, and its scale (DPI / 96).</summary>
internal sealed record MonitorArea(PixelRect Bounds, PixelRect WorkArea, double Scale, bool Primary);

/// <summary>
/// Where the indicator and its "Done" button go (design D5, D6). Everything is in physical pixels; the anchor is the
/// center of the icon, so the kind, the size and the width of the word never move the icon. The word of kind B always
/// grows to the right of the icon (user decision 2026-10-18).
/// </summary>
internal static class OverlayLayout
{
    /// <summary>Diameter of the icon at 100 %, in device-independent units.</summary>
    public const double IconSize = 44;

    /// <summary>Space around the icon inside the window, for the pulse ring and the shake.</summary>
    public const double Padding = 14;

    /// <summary>Distance of the default place from the top right corner of the work area, in pixels.</summary>
    public const int DefaultMargin = 24;

    /// <summary>Gap between the icon and the "Done" button, in device-independent units.</summary>
    public const double DoneGap = 10;

    /// <summary>Diameter of the icon on a monitor, in pixels.</summary>
    public static double IconPixels(int scale, double monitorScale) => IconSize * scale / 100.0 * monitorScale;

    /// <summary>The monitor whose area holds the point, or null (MonitorFromPoint with MONITOR_DEFAULTTONULL).</summary>
    public static MonitorArea? MonitorOf(ScreenPoint point, IReadOnlyList<MonitorArea> monitors) =>
        monitors.FirstOrDefault(m => m.Bounds.Contains(point));

    /// <summary>The monitor that holds the point or, failing that, the one nearest to it.</summary>
    public static MonitorArea NearestMonitor(ScreenPoint point, IReadOnlyList<MonitorArea> monitors) =>
        MonitorOf(point, monitors) ?? monitors.MinBy(m => Distance(point, m.Bounds)) ?? throw new InvalidOperationException("no monitors");

    public static MonitorArea Primary(IReadOnlyList<MonitorArea> monitors) =>
        monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault() ?? throw new InvalidOperationException("no monitors");

    /// <summary>Room the word of kind B takes to the right of the icon, in pixels.</summary>
    public static int WordPixels(double wordRoom, int scale, double monitorScale) =>
        (int)Math.Ceiling(wordRoom * scale / 100.0 * monitorScale);

    /// <summary>
    /// Default place: the top right corner of the work area of the main monitor; with kind B the icon stands left of it by
    /// the room of the longest word.
    /// </summary>
    /// <param name="primary">Main monitor.</param>
    /// <param name="scale">Size of the indicator in percent.</param>
    /// <param name="wordRoom">Room of the longest word to the right of the icon in device-independent units; 0 for kind A.</param>
    public static ScreenPoint Default(MonitorArea primary, int scale, double wordRoom = 0)
    {
        var radius = (int)Math.Ceiling(IconPixels(scale, primary.Scale) / 2);
        var word = WordPixels(wordRoom, scale, primary.Scale);
        return new ScreenPoint(primary.WorkArea.Right - DefaultMargin - radius - word, primary.WorkArea.Top + DefaultMargin + radius);
    }

    /// <summary>
    /// Where the icon is shown: the saved place when a monitor still has it (kept on that monitor), otherwise the default
    /// place. The saved value itself is not changed (spec: Monitor removed).
    /// </summary>
    public static (ScreenPoint Center, MonitorArea Monitor) Resolve(ScreenPoint? saved, IReadOnlyList<MonitorArea> monitors, int scale, double wordRoom = 0)
    {
        if (saved is { } point && MonitorOf(point, monitors) is { } monitor)
        {
            return (Clamp(point, monitor, scale, wordRoom), monitor);
        }

        var primary = Primary(monitors);
        return (Default(primary, scale, wordRoom), primary);
    }

    /// <summary>
    /// Keeps the whole icon on the monitor (spec: Dragged off the screen) and, with kind B, the longest word to the right
    /// of it too, so the word never goes off the screen.
    /// </summary>
    public static ScreenPoint Clamp(ScreenPoint center, MonitorArea monitor, int scale, double wordRoom = 0)
    {
        var radius = (int)Math.Ceiling(IconPixels(scale, monitor.Scale) / 2);
        var word = WordPixels(wordRoom, scale, monitor.Scale);
        var bounds = monitor.Bounds;
        var x = Math.Clamp(center.X, bounds.Left + radius, Math.Max(bounds.Left + radius, bounds.Right - radius - word));
        var y = Math.Clamp(center.Y, bounds.Top + radius, Math.Max(bounds.Top + radius, bounds.Bottom - radius));
        return new ScreenPoint(x, y);
    }

    /// <summary>
    /// The "Done" button of move mode: its horizontal center is the center of the icon (user decision), under the icon,
    /// or above it when there is no room below.
    /// </summary>
    /// <param name="center">Center of the icon.</param>
    /// <param name="scale">Size of the indicator in percent.</param>
    /// <param name="monitor">Monitor of the icon.</param>
    /// <param name="width">Width of the button window in pixels.</param>
    /// <param name="height">Height of the button window in pixels.</param>
    public static PixelRect DoneButton(ScreenPoint center, int scale, MonitorArea monitor, int width, int height)
    {
        var radius = IconPixels(scale, monitor.Scale) / 2;
        var gap = DoneGap * monitor.Scale;
        var left = (int)Math.Round(center.X - (width / 2.0));
        var below = (int)Math.Round(center.Y + radius + gap);
        var top = below + height <= monitor.Bounds.Bottom ? below : (int)Math.Round(center.Y - radius - gap - height);
        return new PixelRect(left, top, left + width, top + height);
    }

    /// <summary>Center of the icon inside the window, in device-independent units before scaling: the icon is always at the left.</summary>
    public static double IconCenterInWindow => Padding + (IconSize / 2);

    private static double Distance(ScreenPoint point, PixelRect rect)
    {
        var dx = Math.Max(Math.Max(rect.Left - point.X, 0), point.X - (rect.Right - 1));
        var dy = Math.Max(Math.Max(rect.Top - point.Y, 0), point.Y - (rect.Bottom - 1));
        return Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
    }
}
