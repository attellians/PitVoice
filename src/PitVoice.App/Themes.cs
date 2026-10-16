using System.Windows;
using PitVoice.Core.Settings;
using Wpf.Ui.Appearance;

namespace PitVoice.App;

/// <summary>Applies the theme from the settings: the Windows one (followed when it changes), always light or always dark.</summary>
internal static class Themes
{
    private static Window? _watched;

    public static void Apply(AppTheme theme, Window? window)
    {
        // WPF-UI refuses to watch or unwatch a window that is not loaded yet.
        if (_watched is { IsLoaded: true } watched)
        {
            SystemThemeWatcher.UnWatch(watched);
        }

        _watched = null;
        switch (theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                if (window is not null)
                {
                    Watch(window);
                }

                break;
        }
    }

    private static void Watch(Window window)
    {
        _watched = window;
        if (window.IsLoaded)
        {
            SystemThemeWatcher.Watch(window);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            window.Loaded -= OnLoaded;
            if (ReferenceEquals(_watched, window))
            {
                SystemThemeWatcher.Watch(window);
            }
        }

        window.Loaded += OnLoaded;
    }
}
