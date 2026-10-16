using System.Windows.Controls;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

internal partial class CheckPage : Page
{
    public CheckPage(CheckViewModel model)
    {
        DataContext = model;
        InitializeComponent();
    }
}
