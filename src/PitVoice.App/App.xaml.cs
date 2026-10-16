using System.Windows;
using PitVoice.App.Localization;
using PitVoice.App.Tray;
using PitVoice.App.ViewModels;
using PitVoice.Host;
using PitVoice.Input;

namespace PitVoice.App;

/// <summary>
/// Start (design decisions 4, 6): one app per user, one pipeline host per user, the icon in the tray and the window. "Exit" in the tray menu stops the host within 3 s and ends the process.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppServices? _services;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;
    private Core.Settings.AppTheme? _theme;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Instance.SetLanguage(SystemLanguage.Detect());

        _instance = SingleInstance.TryStart();
        if (_instance is null)
        {
            if (!SingleInstance.AskFirstToShow())
            {
                ShowMessage("AlreadyRunning");
            }

            Shutdown();
            return;
        }

        _services = new AppServices();
        if (await _services.Host.StartAsync() == HostStartResult.AlreadyRunning)
        {
            ShowMessage("ConsoleHostRunning");
            await _services.DisposeAsync();
            _instance.Dispose();
            Shutdown();
            return;
        }

        ApplyLook(_services.Host.Settings);
        _services.Host.SettingsChanged += settings => Ui.Post(() => ApplyLook(settings));

        _tray = new TrayIcon(ShowWindow, () => _ = ExitAsync());
        _tray.Show(_services.Host.Status);
        _services.Host.StatusChanged += status => Ui.Post(() => _tray?.Show(status));
        _instance.Listen(() => Dispatcher.Invoke(ShowWindow));

        ShowWindow();
    }

    private void ShowWindow()
    {
        if (_services is null || _exiting)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(_services);
            Themes.Apply(_services.Host.Settings.Theme, _window);
        }

        _window.ShowOnTop();
    }

    /// <summary>Interface language and theme follow the settings at once, without a restart.</summary>
    private void ApplyLook(Core.Settings.AppSettings settings)
    {
        Loc.Instance.SetLanguage(settings.EffectiveInterfaceLanguage);
        if (settings.Theme != _theme)
        {
            _theme = settings.Theme;
            Themes.Apply(settings.Theme, _window);
        }
    }

    private async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _tray?.Dispose();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        _instance?.Dispose();
        Shutdown();
    }

    private static void ShowMessage(string key) =>
        MessageBox.Show(Loc.T(key), "PitVoice", MessageBoxButton.OK, MessageBoxImage.Information);
}
