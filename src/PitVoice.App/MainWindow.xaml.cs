using System.ComponentModel;
using System.Windows;
using PitVoice.App.Localization;
using PitVoice.App.Pages;
using PitVoice.App.ViewModels;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace PitVoice.App;

/// <summary>
/// The window: navigation on the left, pages on the right. Closing it only hides it, the pipeline keeps working
/// (spec: Close window keeps running); <see cref="AllowClose"/> is set on exit.
/// </summary>
internal partial class MainWindow : FluentWindow
{
    private readonly AppServices _services;
    private readonly PageProvider _pages;
    private object? _current;
    private bool _leaving;
    private bool _asking;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        _pages = new PageProvider(services, this);
        RootNavigation.SetPageProviderService(_pages);
        RootNavigation.Navigating += OnNavigating;
        RootNavigation.Navigated += (_, e) => _current = e.Page;
        Loaded += (_, _) => RootNavigation.Navigate(typeof(HomePage));
        Activated += (_, _) =>
        {
            _services.Host.RefreshElevation();
            _pages.Settings?.Refresh();
        };
    }

    public bool AllowClose { get; set; }

    /// <summary>The model of the current page when it may hold unsaved changes.</summary>
    private ILeaveGuard? CurrentGuard => (_current as FrameworkElement)?.DataContext as ILeaveGuard;

    /// <summary>Shows the window on top of the others (spec: Open from tray).</summary>
    public void ShowOnTop()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>The button left of the app icon collapses the menu to its icons and opens it again.</summary>
    private void OnPaneToggle(object sender, RoutedEventArgs e) => RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;

    /// <summary>Opens the "Settings" page (the "Assign" link of the home page).</summary>
    public void OpenSettings() => RootNavigation.Navigate(typeof(SettingsPage));

    /// <summary>Opens the "Models" page (a link of the settings page).</summary>
    public void OpenModels() => RootNavigation.Navigate(typeof(ModelsPage));

    /// <summary>Opens "Свои фразы" inside "Commands": the menu item stays selected (design D8).</summary>
    public void OpenPhrases() => RootNavigation.NavigateWithHierarchy(typeof(PhrasesPage));

    /// <summary>The back arrow of "Свои фразы"; it passes the leave check like any other navigation.</summary>
    public void BackToCommands() => RootNavigation.Navigate(typeof(CommandsPage));

    /// <summary>
    /// Asks about unsaved changes of the current page before the app exits (spec: Exit from the tray): the window shows
    /// itself with the leave dialog. False when the user stays.
    /// </summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (CurrentGuard is not { HasUnsavedChanges: true } guard)
        {
            return true;
        }

        if (_asking)
        {
            return false;
        }

        ShowOnTop();
        return await LeaveFlow.ConfirmAsync(guard, AskLeaveAsync);
    }

    /// <summary>
    /// Leaving a page with unsaved changes by the menu or the back arrow: the navigation is cancelled, the dialog asks,
    /// and the navigation is repeated only after a successful save or "Не сохранять" (design D8).
    /// </summary>
    private void OnNavigating(NavigationView sender, NavigatingCancelEventArgs e)
    {
        if (_leaving || ReferenceEquals(e.Page, _current) || CurrentGuard is not { HasUnsavedChanges: true } guard)
        {
            return;
        }

        e.Cancel = true;
        if (!_asking && e.Page is { } target)
        {
            _ = LeaveAsync(guard, target.GetType());
        }
    }

    private async Task LeaveAsync(ILeaveGuard guard, Type target)
    {
        if (!await LeaveFlow.ConfirmAsync(guard, AskLeaveAsync))
        {
            return;
        }

        _leaving = true;
        try
        {
            RootNavigation.Navigate(target);
        }
        finally
        {
            _leaving = false;
        }
    }

    private async Task<LeaveChoice> AskLeaveAsync()
    {
        _asking = true;
        try
        {
            var dialog = new ContentDialog(RootDialogHost)
            {
                Title = Loc.T("LeaveTitle"),
                Content = Loc.T("LeaveText"),
                PrimaryButtonText = Loc.T("LeaveSave"),
                SecondaryButtonText = Loc.T("LeaveDiscard"),
                CloseButtonText = Loc.T("LeaveStay"),
                DefaultButton = ContentDialogButton.Primary,
            };
            return await dialog.ShowAsync() switch
            {
                ContentDialogResult.Primary => LeaveChoice.Save,
                ContentDialogResult.Secondary => LeaveChoice.Discard,
                _ => LeaveChoice.Stay,
            };
        }
        finally
        {
            _asking = false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private async Task<bool> ConfirmDeleteAsync(string id)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("DeleteTitle"),
            Content = Loc.F("DeleteQuestion", id),
            PrimaryButtonText = Loc.T("DeleteButton"),
            CloseButtonText = Loc.T("CancelButton"),
            Owner = this,
        };
        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    /// <summary>Pages are created once, on first use, and keep their state while the window is hidden.</summary>
    private sealed class PageProvider(AppServices services, MainWindow window) : INavigationViewPageProvider
    {
        private readonly Dictionary<Type, object> _pages = [];

        public SettingsViewModel? Settings { get; private set; }

        public object? GetPage(Type pageType)
        {
            if (_pages.TryGetValue(pageType, out var page))
            {
                return page;
            }

            page = pageType.Name switch
            {
                nameof(HomePage) => new HomePage(new HomeViewModel(services, window.OpenSettings)),
                nameof(SettingsPage) => new SettingsPage(Settings = new SettingsViewModel(services, window.OpenModels)),
                nameof(OverlayPage) when services.IndicatorControl is { } indicator => new OverlayPage(new IndicatorSettingsViewModel(indicator)),
                nameof(ModelsPage) =>new ModelsPage(new ModelsViewModel(services, window.ConfirmDeleteAsync)),
                nameof(CommandsPage) => new CommandsPage(new CommandsViewModel(services, window.OpenPhrases)),

                // A new draft on every visit: a cached page would show a stale one (design D8).
                nameof(PhrasesPage) => new PhrasesPage(new PhrasesViewModel(new HostPhrases(services.Host)), window.BackToCommands),
                nameof(AboutPage) => new AboutPage(new AboutViewModel(services)),
                _ => null,
            };
            if (page is not null and not PhrasesPage)
            {
                _pages[pageType] = page;
            }

            return page;
        }
    }
}
