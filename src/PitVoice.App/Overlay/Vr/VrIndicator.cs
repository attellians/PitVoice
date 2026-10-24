using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PitVoice.Core.Settings;
using PitVoice.Core.Vr;
using PitVoice.Host;

namespace PitVoice.App.Overlay.Vr;

/// <summary>Where the indicator in the headset goes: the VR helper, through its supervisor.</summary>
internal interface IVrLink
{
    /// <summary>Takes a message on the UI thread and never waits for the helper.</summary>
    void Send(VrMessage message);
}

/// <summary>
/// Pictures of the indicator for the headset (add-vr-overlay D5): a second <see cref="IndicatorBadge"/> outside any window
/// gets the same states and settings as the one on the monitor, and <see cref="RenderTargetBitmap"/> takes frames of it at
/// <see cref="PixelsPerUnit"/> pixels per device-independent unit. A frame goes out on every change and, while an
/// animation runs, 30 times a second; the idle icon sends none. Clocks of an element outside every window do not tick,
/// so a <see cref="ClockWindow"/> off the screen is shown while the animations run (task 1.3). Runs on the UI thread.
/// </summary>
internal sealed class VrIndicator : IDisposable
{
    /// <summary>Frame density at 100 %: the indicator size scales the picture itself.</summary>
    public const double PixelsPerUnit = 2;

    public static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1 / 30.0);

    private readonly IVrLink _link;
    private readonly Func<IReadOnlyList<MonitorArea>> _monitors;
    private readonly IndicatorBadge _badge = new();
    private readonly Border _root = new();
    private readonly DispatcherTimer _timer;
    private ClockWindow? _clock;
    private OverlaySettings? _settings;
    private string _language = string.Empty;
    private double _wordRoom;
    private VrPlace? _place;
    private bool _visible;
    private bool _disposed;

    /// <param name="link">Where the messages go.</param>
    /// <param name="monitors">The monitors now, for the place (D7).</param>
    public VrIndicator(IVrLink link, Func<IReadOnlyList<MonitorArea>> monitors)
    {
        _link = link;
        _monitors = monitors;
        _root.Child = _badge;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameInterval };
        _timer.Tick += (_, _) => Animate();
        _link.Send(new VrVisible(false));
    }

    /// <summary>Frames sent so far.</summary>
    public int Frames { get; private set; }

    /// <summary>Size of the last frame in pixels.</summary>
    public (int Width, int Height) FrameSize { get; private set; }

    /// <summary>Shows a state with the settings of the indicator; <paramref name="word"/> picks the word of kind B (D9 of add-overlay-indicator).</summary>
    public void Update(IndicatorView view, OverlaySettings settings, string language, Func<IndicatorVisual, string> word)
    {
        if (_disposed)
        {
            return;
        }

        if (!view.Visible)
        {
            if (_visible)
            {
                _visible = false;
                StopAnimating();
                _badge.StopAll();
                _link.Send(new VrVisible(false));
            }

            return;
        }

        var appearing = !_visible;
        if (appearing || settings != _settings || language != _language)
        {
            var longest = Enum.GetValues<IndicatorVisual>().Max(v => IndicatorBadge.MeasureWord(word(v), 1));
            _wordRoom = IndicatorBadge.WordRoomFor(settings.Kind, longest);
            _badge.Arrange(settings.Kind, longest);
            _badge.Scale = settings.Scale / 100.0;
            _badge.SetOpacity(settings.Opacity / 100.0, animate: !appearing);
            if (_monitors() is { Count: > 0 } monitors)
            {
                SendPlace(VrPlacement.Resolve(settings.Position, monitors, settings.Scale, _wordRoom));
            }

            _settings = settings;
            _language = language;
        }

        _badge.ShowFrame = view.Moving;
        _badge.Show(view.Visual, word(view.Visual), animate: !appearing);
        SendFrame();
        if (appearing)
        {
            _visible = true;
            _link.Send(new VrVisible(true));
        }

        if (_badge.RunningAnimations > 0)
        {
            _clock ??= new ClockWindow();
            _clock.Show();
            _timer.Start();
        }
    }

    /// <summary>The icon is being dragged on the monitor: the indicator in the headset follows (spec: Moving with the mouse).</summary>
    public void MoveTo(ScreenPoint center, MonitorArea monitor)
    {
        if (!_disposed && _visible && _settings is { } settings)
        {
            SendPlace(VrPlacement.Place(center, monitor, settings.Scale, _wordRoom));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAnimating();
        _badge.StopAll();
        _clock?.Close();
    }

    /// <summary>Straight colors from premultiplied BGRA, as SetOverlayRaw takes them.</summary>
    public static byte[] ToRgba(byte[] premultipliedBgra)
    {
        var rgba = new byte[premultipliedBgra.Length];
        for (var i = 0; i < premultipliedBgra.Length; i += 4)
        {
            var a = premultipliedBgra[i + 3];
            if (a == 0)
            {
                continue;
            }

            rgba[i] = Unpremultiply(premultipliedBgra[i + 2], a);
            rgba[i + 1] = Unpremultiply(premultipliedBgra[i + 1], a);
            rgba[i + 2] = Unpremultiply(premultipliedBgra[i], a);
            rgba[i + 3] = a;
        }

        return rgba;
    }

    private static byte Unpremultiply(byte value, byte alpha) => (byte)Math.Min(255, ((value * 255) + (alpha / 2)) / alpha);

    private void Animate()
    {
        if (_disposed || !_visible)
        {
            StopAnimating();
            return;
        }

        SendFrame();

        // The frame after the last animation ended shows the final values; then silence until the next change.
        if (_badge.RunningAnimations == 0)
        {
            StopAnimating();
        }
    }

    private void StopAnimating()
    {
        _timer.Stop();
        _clock?.Hide();
    }

    private void SendPlace(VrPlace place)
    {
        if (place != _place)
        {
            _place = place;
            _link.Send(place);
        }
    }

    private void SendFrame()
    {
        _root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _root.Arrange(new Rect(_root.DesiredSize));
        _root.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(_root.DesiredSize.Width * PixelsPerUnit));
        var height = Math.Max(1, (int)Math.Ceiling(_root.DesiredSize.Height * PixelsPerUnit));
        var bitmap = new RenderTargetBitmap(width, height, 96 * PixelsPerUnit, 96 * PixelsPerUnit, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        Frames++;
        FrameSize = (width, height);
        _link.Send(new VrFrame(width, height, ToRgba(pixels)));
    }
}
