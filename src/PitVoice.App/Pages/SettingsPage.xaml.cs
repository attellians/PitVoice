using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>A drag of the threshold slider is saved when the mouse is released, without waiting for the delay.</summary>
    private void OnThresholdReleased(object sender, MouseButtonEventArgs e) => _model.Threshold.Commit();
}
