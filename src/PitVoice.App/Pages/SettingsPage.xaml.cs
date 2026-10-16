using System.Windows;
using System.Windows.Controls;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

internal partial class SettingsPage : Page
{
    private readonly SettingsViewModel _model;

    public SettingsPage(SettingsViewModel model)
    {
        _model = model;
        DataContext = model;
        InitializeComponent();
        Loaded += (_, _) => model.Refresh();
    }

    private void OnDevicesOpened(object? sender, EventArgs e) => _model.Refresh();

    private void OnThresholdLostFocus(object sender, RoutedEventArgs e) => _model.ApplyThresholdCommand.Execute(null);
}
