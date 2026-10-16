using System.Windows.Controls;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

internal partial class CommandsPage : Page
{
    public CommandsPage(CommandsViewModel model)
    {
        DataContext = model;
        InitializeComponent();
        Loaded += (_, _) => model.Refresh();
    }
}
