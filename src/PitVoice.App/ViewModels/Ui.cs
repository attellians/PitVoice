using System.Windows;

namespace PitVoice.App.ViewModels;

/// <summary>Host events come on background threads; view models update on the UI thread.</summary>
internal static class Ui
{
    public static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
