using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PitVoice.App.ViewModels;

namespace PitVoice.App.Pages;

/// <summary>"Свои фразы" inside "Commands" (spec: Phrase builder page).</summary>
internal partial class PhrasesPage : Page
{
    private readonly Action _back;

    /// <param name="model">The page model; a new one for every visit (design D8).</param>
    /// <param name="back">Goes back to "Commands"; it passes the window's leave check.</param>
    public PhrasesPage(PhrasesViewModel model, Action back)
    {
        _back = back;
        DataContext = model;
        InitializeComponent();
        model.FocusRequested += Focus;
    }

    public PhrasesViewModel Model => (PhrasesViewModel)DataContext;

    private void OnBack(object sender, RoutedEventArgs e) => _back();

    private void OnCheckKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Model.CheckCommand.CanExecute(null))
        {
            Model.CheckCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Focuses the phrase box of <paramref name="row"/> once its container exists (spec: Save with an error).</summary>
    private void Focus(OwnPhraseRow row) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        if (RowList.ItemContainerGenerator.ContainerFromItem(row) is ContentPresenter container
            && container.ContentTemplate?.FindName("PhraseBox", container) is FrameworkElement box)
        {
            box.BringIntoView();
            box.Focus();
            Keyboard.Focus(box);
        }
    });
}
