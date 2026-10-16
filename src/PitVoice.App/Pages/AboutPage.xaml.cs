using System.Windows.Controls;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

internal partial class AboutPage : Page
{
    public AboutPage(AboutViewModel model)
    {
        DataContext = model;
        InitializeComponent();
    }
}
