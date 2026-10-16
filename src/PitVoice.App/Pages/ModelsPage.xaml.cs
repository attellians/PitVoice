using System.Windows.Controls;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

internal partial class ModelsPage : Page
{
    public ModelsPage(ModelsViewModel model)
    {
        DataContext = model;
        InitializeComponent();
        Loaded += (_, _) => model.Refresh();
    }
}
