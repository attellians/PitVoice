using System.Windows.Controls;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

internal partial class HomePage : Page
{
    public HomePage(HomeViewModel model)
    {
        DataContext = model;
        InitializeComponent();
    }
}
