using System.Windows.Controls;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

/// <summary>"Overlay": the indicator above the game (overlay-indicator spec: Indicator settings, Moving the indicator).</summary>
internal partial class OverlayPage : Page
{
    public OverlayPage(IndicatorSettingsViewModel model)
    {
        DataContext = model;
        InitializeComponent();
    }
}
