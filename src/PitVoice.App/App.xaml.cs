using System.Windows;
using System.Windows.Threading;
using PitVoice.App.Localization;
using PitVoice.App.Overlay;
using PitVoice.App.Tray;
using PitVoice.App.ViewModels;
using PitVoice.Host;
using PitVoice.Host.Diagnostics;
using PitVoice.Input;
using Serilog.Core;

namespace PitVoice.App;

/// <summary>
/// Start (design decisions 4, 6): one app per user, one pipeline host per user, the icon in the tray and the window. "Exit" in the tray menu stops the host within 3 s and ends the process.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private Logger? _log;
    private CrashReporter? _crashes;
    private AppServices? _services;
    private TrayIcon? _tray;
    private OverlayController? _overlay;
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

        // Crash handling comes before the host, so a failure while it starts is reported too (crash-reports spec).
        _log = DiagnosticLog.Create();
        _crashes = new CrashReporter(_log);
        _crashes.FatalReported += _ => HideTray();
        _crashes.Install();
        DispatcherUnhandledException += OnWindowThreadCrash;

        _services = new AppServices(_log, _crashes);
        if (await _services.Host.StartAsync() == HostStartResult.AlreadyRunning)
        {
            ShowMessage("ConsoleHostRunning");
            await _services.DisposeAsync();
            _log.Dispose();
            _instance.Dispose();
            Shutdown();
            return;
        }

        ApplyLook(_services.Host.Settings);
        _services.Host.SettingsChanged += settings => Ui.Post(() => ApplyLook(settings));

        _overlay = _services.StartIndicator();
        var indicator = _services.IndicatorControl!;
        _tray = new TrayIcon(ShowWindow, () => indicator.Moving = true, () => _ = ExitAsync());
        _tray.Show(_services.Host.Status);
        _tray.ShowMoveIndicator(indicator.Settings.Enabled);
        _services.Host.StatusChanged += status => Ui.Post(() => _tray?.Show(status));
        indicator.Changed += () => _tray?.ShowMoveIndicator(indicator.Settings.Enabled);
        _instance.Listen(() => Dispatcher.Invoke(ShowWindow));

        ShowWindow();
        TestCrash.Schedule(_log, action => Dispatcher.BeginInvoke(action));
    }

    /// <summary>
    /// After an exception in the window thread the state of the window and the host is unknown, so the app writes the
    /// report, removes the icon (<see cref="CrashReporter.FatalReported"/>) and ends (design decision 3); the next start
    /// is a normal one.
    /// </summary>
    private void OnWindowThreadCrash(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _crashes?.Report(e.Exception, "window thread", fatal: true);
        Environment.Exit(2);
    }

    /// <summary>Without it a dead icon stays in the tray until the mouse passes over it.</summary>
    private void HideTray()
    {
        try
        {
            if (Dispatcher.CheckAccess())
            {
                _tray?.Dispose();
            }
            else
            {
                Dispatcher.Invoke(() => _tray?.Dispose(), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(1));
            }
        }
#pragma warning disable CA1031 // The process ends anyway.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
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

        // Unsaved own phrases: the window shows itself with the leave dialog, "Остаться" cancels the exit (spec: Exit from the tray).
        if (_window is not null && !await _window.ConfirmLeaveAsync())
        {
            return;
        }

        _exiting = true;
        _tray?.Dispose();
        _overlay?.Dispose();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        _log?.Dispose();
        _instance?.Dispose();
        Shutdown();
    }

    private static void ShowMessage(string key) =>
        MessageBox.Show(Loc.T(key), "PitVoice", MessageBoxButton.OK, MessageBoxImage.Information);
}
