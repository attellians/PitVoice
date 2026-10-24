using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace PitVoice.App.Overlay;

/// <summary>
/// The window of the indicator (design D3): no frame, transparent, topmost, no taskbar button, never activated, and
/// clicks pass through it into the game unless move mode is on. Placed by its owner in physical pixels.
/// </summary>
internal class OverlayWindow : Window
{
    private bool _clickThrough = true;

    public OverlayWindow()
    {
        Title = "PitVoice";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Off the screen until the owner places it.
        Left = -32000;
        Top = -32000;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            OverlayNative.SetOverlayStyles(Handle, _clickThrough);
            OverlayNative.ClearBackdrop(Handle);
            HwndSource.FromHwnd(Handle)?.AddHook(Hook);
        };
        ApplicationThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ApplicationThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>WPF-UI puts its backdrop on every window again when the theme changes.</summary>
    private void OnThemeChanged(ApplicationTheme theme, Color accent) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (Handle != 0)
            {
                OverlayNative.ClearBackdrop(Handle);
            }
        });

    public nint Handle { get; private set; }

    /// <summary>Raised after the window moved to a monitor with another scale, so the owner can place it again.</summary>
    public event Action? ScaleChanged;

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            if (Handle != 0)
            {
                OverlayNative.SetOverlayStyles(Handle, value);
            }
        }
    }

    /// <summary>WPF-UI paints the background of every window of the app when it applies a theme; this one stays transparent.</summary>
    static OverlayWindow()
    {
        BackgroundProperty.OverrideMetadata(
            typeof(OverlayWindow),
            new FrameworkPropertyMetadata(Brushes.Transparent, null, (_, _) => Brushes.Transparent));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ScaleChanged?.Invoke();
    }

    private static nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // A click in move mode must not take the focus from the game.
        if (message == OverlayNative.WmMouseActivate)
        {
            handled = true;
            return OverlayNative.MaNoActivate;
        }

        return 0;
    }
}

/// <summary>The indicator itself: the window with <see cref="IndicatorBadge"/>.</summary>
internal sealed class IndicatorWindow : OverlayWindow
{
    public IndicatorWindow()
    {
        Content = Badge;
    }

    public IndicatorBadge Badge { get; } = new();
}

/// <summary>
/// An empty window that stays off the screen. WPF advances animation clocks only while some window renders, so the badge
/// of the headset, which is in no window, animates while this one is shown (add-vr-overlay task 1.3).
/// </summary>
internal sealed class ClockWindow : OverlayWindow
{
    public ClockWindow()
    {
        SizeToContent = SizeToContent.Manual;
        Width = 1;
        Height = 1;
    }
}

/// <summary>The "Done" button of move mode, a small window under the icon (design D5).</summary>
internal sealed class DoneWindow : OverlayWindow
{
    public DoneWindow(Action done)
    {
        ClickThrough = false;
        Button.Click += (_, _) => done();
        Content = Button;
    }

    public Wpf.Ui.Controls.Button Button { get; } = new()
    {
        Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
        Padding = new Thickness(18, 6, 18, 6),
        Margin = new Thickness(2),
    };
}
