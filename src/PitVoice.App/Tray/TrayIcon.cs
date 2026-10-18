using System.IO;
using System.Windows.Controls;
using H.NotifyIcon;
using PitVoice.App.Localization;
using PitVoice.Host;

namespace PitVoice.App.Tray;

/// <summary>
/// The icon in the notification area (design decision 6, spec: Tray presence, Tray status): the state mark, the tooltip
/// "PitVoice: state — reason", a menu with "Open", "Move indicator" and "Exit", and a double click that opens the window. Must be used on
/// the UI thread.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _open;
    private readonly MenuItem _move;
    private readonly MenuItem _exit;
    // H.NotifyIcon disposes the previous icon when a new one is set, so every change gets a fresh Icon from these bytes.
    private readonly Dictionary<HostState, byte[]> _images = [];
    private HostStatus? _status;

    /// <param name="open">Opens the window.</param>
    /// <param name="moveIndicator">Turns on move mode of the indicator; the window stays closed (overlay-indicator spec).</param>
    /// <param name="exit">Ends the app.</param>
    public TrayIcon(Action open, Action moveIndicator, Action exit)
    {
        foreach (var state in Enum.GetValues<HostState>())
        {
            using var stream = new MemoryStream();
            IconArt.WriteIco(stream, state, [16, 20, 24, 32, 40, 48]);
            _images[state] = stream.ToArray();
        }

        _open = new MenuItem { FontWeight = System.Windows.FontWeights.SemiBold };
        _open.Click += (_, _) => open();
        _move = new MenuItem();
        _move.Click += (_, _) => moveIndicator();
        _exit = new MenuItem();
        _exit.Click += (_, _) => exit();
        _icon = new TaskbarIcon
        {
            ContextMenu = new ContextMenu { Items = { _open, _move, new Separator(), _exit } },
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseDoubleClick += (_, _) => open();
        Loc.Instance.LanguageChanged += UpdateTexts;
        UpdateTexts();
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Show(HostStatus status)
    {
        _status = status;
        using (var stream = new MemoryStream(_images[status.State]))
        {
            _icon.Icon = new System.Drawing.Icon(stream);
        }
        _icon.ToolTipText = Texts.Tooltip(status);
    }

    /// <summary>"Move indicator" is hidden while the indicator is turned off.</summary>
    public void ShowMoveIndicator(bool enabled) =>
        _move.Visibility = enabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public void Dispose()
    {
        Loc.Instance.LanguageChanged -= UpdateTexts;
        _icon.Dispose();
    }

    private void UpdateTexts()
    {
        _open.Header = Loc.T("TrayOpen");
        _move.Header = Loc.T("TrayMoveIndicator");
        _exit.Header = Loc.T("TrayExit");
        if (_status is { } status)
        {
            _icon.ToolTipText = Texts.Tooltip(status);
        }
    }
}
