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

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        _pages = new PageProvider(services, this);
        RootNavigation.SetPageProviderService(_pages);
        Loaded += (_, _) => RootNavigation.Navigate(typeof(HomePage));
        Activated += (_, _) =>
        {
            _services.Host.RefreshElevation();
            _pages.Settings?.Refresh();
        };
    }

    public bool AllowClose { get; set; }

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
                nameof(CommandsPage) => new CommandsPage(new CommandsViewModel(services)),
                nameof(AboutPage) => new AboutPage(new AboutViewModel(services)),
                _ => null,
            };
            if (page is not null)
            {
                _pages[pageType] = page;
            }

            return page;
        }
    }
}
