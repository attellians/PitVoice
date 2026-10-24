using PitVoice.Core.Settings;
using PitVoice.Core.Vr;

namespace PitVoice.App.Overlay.Vr;

/// <summary>
/// Where the indicator stands in the headset (add-vr-overlay D7): the place of the icon on its monitor is mapped to the
/// same place in a rectangle of ±<see cref="HalfWidth"/>° × ±<see cref="HalfHeight"/>° around the view, so the left edge
/// of the monitor is the left of the view and so on. The numbers are a starting point for the first VR pilots.
/// </summary>
internal static class VrPlacement
{
    /// <summary>Half the width of the comfortable part of the view, in degrees.</summary>
    public const double HalfWidth = 25;

    /// <summary>Half the height of the comfortable part of the view, in degrees.</summary>
    public const double HalfHeight = 18;

    /// <summary>Width of the icon at 100 %, in degrees.</summary>
    public const double IconDegrees = 2.5;

    /// <summary>The place from the settings: the saved position or, without it or its monitor, the default one.</summary>
    /// <param name="saved">Saved center of the icon.</param>
    /// <param name="monitors">Monitors now.</param>
    /// <param name="scale">Size of the indicator in percent.</param>
    /// <param name="wordRoom">Room of the longest word right of the icon in device-independent units; 0 for kind A.</param>
    public static VrPlace Resolve(ScreenPoint? saved, IReadOnlyList<MonitorArea> monitors, int scale, double wordRoom)
    {
        var (center, monitor) = OverlayLayout.Resolve(saved, monitors, scale, wordRoom);
        return Place(center, monitor, scale, wordRoom);
    }

    /// <summary>
    /// The direction of the center of the frame and its angular width. The frame is the picture of
    /// <see cref="IndicatorBadge"/>: the icon at the left with <see cref="OverlayLayout.Padding"/> around it, the word to
    /// the right. The icon and the longest word stay inside the rectangle, like <see cref="OverlayLayout.Clamp"/> keeps
    /// them on the monitor.
    /// </summary>
    public static VrPlace Place(ScreenPoint center, MonitorArea monitor, int scale, double wordRoom)
    {
        var bounds = monitor.Bounds;
        var rx = (center.X - bounds.Left) / (double)Math.Max(1, bounds.Width);
        var ry = (center.Y - bounds.Top) / (double)Math.Max(1, bounds.Height);
        var perUnit = DegreesPerUnit(scale);
        var radius = IconDegrees / 2 * scale / 100.0;
        var word = wordRoom * perUnit;

        var yaw = Math.Clamp((rx - 0.5) * 2 * HalfWidth, -HalfWidth + radius, Math.Max(-HalfWidth + radius, HalfWidth - radius - word));
        var pitch = Math.Clamp((0.5 - ry) * 2 * HalfHeight, -HalfHeight + radius, HalfHeight - radius);

        // The icon is at the left of the frame and in the middle of its height.
        var frameWidth = (OverlayLayout.Padding * 2) + OverlayLayout.IconSize + wordRoom;
        var toCenter = ((frameWidth / 2) - OverlayLayout.IconCenterInWindow) * perUnit;
        return new VrPlace((float)(yaw + toCenter), (float)pitch, (float)(frameWidth * perUnit));
    }

    /// <summary>Degrees per device-independent unit of the indicator at the given size.</summary>
    public static double DegreesPerUnit(int scale) => IconDegrees / OverlayLayout.IconSize * scale / 100.0;
}
