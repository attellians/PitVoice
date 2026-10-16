using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PitVoice.App.Localization;

/// <summary>
/// Interface strings (design decision 7): <c>ui/&lt;language&gt;.json</c> embedded in the app, key → text. XAML binds to the
/// indexer through <see cref="TrExtension"/>; switching the language raises <c>Item[]</c> and every bound string is
/// redrawn without a restart. A key missing in the language falls back to English, a key missing in English shows the
/// key itself, so the window never fails on a missing string.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Fallback = "en";

    private const string ResourcePrefix = "PitVoice.App.ui.";

    private readonly IReadOnlyDictionary<string, string> _english;
    private IReadOnlyDictionary<string, string> _strings;

    private Loc()
    {
        _english = LoadDictionary(Fallback) ?? new Dictionary<string, string>();
        _strings = _english;
        Language = Fallback;
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after <see cref="SetLanguage"/> changed the language.</summary>
    public event Action? LanguageChanged;

    /// <summary>Language of the interface strings; a language without a dictionary uses English.</summary>
    public string Language { get; private set; }

    public string this[string key] =>
        _strings.TryGetValue(key, out var text) ? text
        : _english.TryGetValue(key, out var english) ? english
        : key;

    /// <summary>Interface languages that have a dictionary.</summary>
    public static IReadOnlyList<string> Languages { get; } = typeof(Loc).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
        .Select(n => n[ResourcePrefix.Length..^".json".Length])
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static string T(string key) => Instance[key];

    /// <summary>The string of <paramref name="key"/> with <c>{0}</c>, <c>{1}</c>… filled in.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Instance[key], args);

    public void SetLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        var strings = LoadDictionary(language);
        var effective = strings is null ? Fallback : language;
        if (effective == Language)
        {
            return;
        }

        _strings = strings ?? _english;
        Language = effective;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    /// <summary>The dictionary of <paramref name="language"/>, or null when the app has none.</summary>
    internal static IReadOnlyDictionary<string, string>? LoadDictionary(string language)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream(ResourcePrefix + language + ".json");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? [];
    }
}
