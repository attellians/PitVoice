using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PitVoice.Core.Settings;
using Wpf.Ui.Appearance;

namespace PitVoice.App;

/// <summary>
/// Applies the theme from the settings: the Windows one (followed when it changes), always light or always dark. The
/// section hue set (<c>Themes/Style.Dark.xaml</c> / <c>Style.Light.xaml</c>, design D1 of refresh-window-visual-style)
/// is swapped with the WPF-UI theme, also when Windows switches its theme while "as in Windows" is chosen.
/// </summary>
internal static class Themes
{
    private const string DarkSet = "Themes/Style.Dark.xaml";
    private const string LightSet = "Themes/Style.Light.xaml";

    private static Window? _watched;
    private static bool _following;

    public static void Apply(AppTheme theme, Window? window)
    {
        Follow();

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

        ApplyLook(ApplicationThemeManager.GetAppTheme());
    }

    /// <summary>
    /// The accent of the window: always this blue, never the Windows accent (user decision 2026-10-19), so the one primary
    /// button of a page is blue in both themes. WPF-UI derives the lighter and darker shades per theme from it.
    /// </summary>
    public static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>The hue set and the accent of <paramref name="theme"/>; WPF-UI resets the accent with every theme it applies.</summary>
    private static void ApplyLook(ApplicationTheme theme)
    {
        ApplySet(theme);
        ApplyAccent(theme);

        // The system theme watcher re-applies the Windows accent after it raises Changed: set the blue again once it is done.
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplyAccent(ApplicationThemeManager.GetAppTheme()));
    }

    /// <summary>
    /// WPF-UI puts the accent into the application resources and resolves some brushes once (the primary button, the
    /// slider thumb): its own blue first, then the blue brushes of the hue set at the root, which wins over every merged
    /// dictionary and is read by later pages too.
    /// </summary>
    private static void ApplyAccent(ApplicationTheme theme)
    {
        ApplicationAccentColorManager.Apply(Accent, theme, systemGlassColor: false, systemAccentColor: false);
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        var set = LoadSet(theme);
        foreach (var key in set.Keys.OfType<string>().Where(IsAccentKey))
        {
            resources[key] = set[key];
        }
    }

    /// <summary>Keys of the hue set that override WPF-UI accent resources rather than add our own.</summary>
    private static bool IsAccentKey(string key) =>
        AccentPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));

    private static readonly string[] AccentPrefixes = ["Accent", "Slider", "ToggleSwitch", "ProgressBar", "NavigationView"];

    /// <summary>The hue set of <paramref name="theme"/> as a fresh dictionary (for tests and for the swap).</summary>
    public static ResourceDictionary LoadSet(ApplicationTheme theme) =>
        (ResourceDictionary)Application.LoadComponent(new Uri("/PitVoice;component/" + SetFile(theme), UriKind.Relative));

    private static string SetFile(ApplicationTheme theme) => theme == ApplicationTheme.Light ? LightSet : DarkSet;

    /// <summary>Replaces the hue set among the application resources with the one of <paramref name="theme"/>.</summary>
    private static void ApplySet(ApplicationTheme theme)
    {
        if (Application.Current?.Resources.MergedDictionaries is not { } merged)
        {
            return;
        }

        var wanted = SetFile(theme);
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is null || !(source.EndsWith(DarkSet, StringComparison.OrdinalIgnoreCase) || source.EndsWith(LightSet, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!source.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
            {
                // A dictionary keeps its Source only when it is loaded by Source, and the next swap finds the set by it.
                merged[i] = new ResourceDictionary { Source = new Uri("pack://application:,,,/PitVoice;component/" + wanted, UriKind.Absolute) };
            }

            return;
        }
    }

    /// <summary>The system theme watcher applies the WPF-UI theme itself; the hue set follows through this event.</summary>
    private static void Follow()
    {
        if (_following)
        {
            return;
        }

        _following = true;
        ApplicationThemeManager.Changed += (theme, _) => ApplyLook(theme);
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
