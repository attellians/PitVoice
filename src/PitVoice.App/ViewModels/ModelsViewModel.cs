using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>
/// A model row with its buttons (spec: Models in the window; design D6): the language code in a circle of the state's
/// hue, the language name, the state in a word, then size, install date, id and license in small grey, and the
/// progress "N из M МБ" while downloading.
/// </summary>
internal sealed partial class ModelRow : ObservableObject
{
    private readonly string _languageName;

    public ModelRow(ModelItem item, string languageName, Func<string, Task> download, Action<string> cancel, Func<string, Task> delete)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        _languageName = languageName;
        DownloadCommand = new AsyncRelayCommand(() => download(Item.Entry.Id));
        CancelCommand = new RelayCommand(() => cancel(Item.Entry.Id));
        DeleteCommand = new AsyncRelayCommand(() => delete(Item.Entry.Id));
    }

    public ModelItem Item { get; private set; }

    public string Id => Item.Entry.Id;

    /// <summary>"RU", "EN": the first two letters of the language code.</summary>
    public string Code => Item.Entry.Language.Length >= 2 ? Item.Entry.Language[..2].ToUpperInvariant() : Item.Entry.Language.ToUpperInvariant();

    public string LanguageName => _languageName;

    public ModelState State => Item.State;

    /// <summary>"используется", "скачивается", "установлена", "не скачана".</summary>
    public string StateText => Loc.T($"model.{Item.State}");

    public bool IsInUse => Item.State == ModelState.InUse;

    public bool IsNotInstalled => Item.State == ModelState.NotInstalled;

    /// <summary>"45,1 МБ · установлена 19.10.2026 · vosk-model-small-ru-0.22 · лицензия Apache-2.0".</summary>
    public string Details
    {
        get
        {
            var parts = new List<string> { Loc.F("ModelSize", Texts.Megabytes(Item.Entry.SizeBytes)) };
            if (Item.InstalledAt is { } installed)
            {
                parts.Add(Loc.F("ModelInstalledOn", FormatDate(installed)));
            }

            parts.Add(Item.Entry.Id);
            parts.Add(Loc.F("ModelLicense", Item.Entry.License));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>"12,3 из 45,1 МБ" while downloading, else empty.</summary>
    public string ProgressText => Item.State == ModelState.Downloading && Item.Progress is { } progress
        ? Loc.F("ModelBytes", Texts.Megabytes(progress.BytesReceived), Texts.Megabytes(progress.TotalBytes ?? Item.Entry.SizeBytes))
        : string.Empty;

    public double Percent => Item.Progress?.Percent ?? 0;

    public bool IsDownloading => Item.State == ModelState.Downloading;

    public bool CanDownload => Item.State == ModelState.NotInstalled;

    public bool CanDelete => Item.State is ModelState.Installed or ModelState.InUse;

    public string Error => Item.Error is { } error ? Loc.F("ModelError", error) : string.Empty;

    public IAsyncRelayCommand DownloadCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>The date in the interface language: "19.10.2026" in Russian, "10/19/2026" in English.</summary>
    public static string FormatDate(DateTimeOffset date) =>
        date.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo(Loc.Instance.Language == "ru" ? "ru-RU" : "en-US"));

    public void Update(ModelItem next)
    {
        Item = next;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>"Models" (spec: Models in the window, Delete model from the window).</summary>
internal sealed partial class ModelsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Func<string, Task<bool>> _confirmDelete;

    public ModelsViewModel(AppServices services, Func<string, Task<bool>> confirmDelete)
    {
        _services = services;
        _confirmDelete = confirmDelete;
        services.Models.Changed += () => Ui.Post(Refresh);
        services.Host.SettingsChanged += _ => Ui.Post(Refresh);
        Loc.Instance.LanguageChanged += Refresh;
        Refresh();
    }

    public ObservableCollection<ModelRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>The subtitle: recognition works offline, the model of the commands language is needed.</summary>
    [ObservableProperty]
    public partial string LanguageTitle { get; set; } = string.Empty;

    /// <summary>Name of a pack language, e.g. "Русский".</summary>
    public string LanguageName(string code) => _services.Host.Languages.FirstOrDefault(l => l.Code == code)?.Name ?? code;

    public Task DownloadAsync(string id)
    {
        Message = string.Empty;
        return _services.Models.DownloadAsync(id);
    }

    public void Cancel(string id) => _services.Models.Cancel(id);

    public async Task DeleteAsync(string id)
    {
        if (!await _confirmDelete(id))
        {
            return;
        }

        var result = await _services.Models.DeleteAsync(id);
        Message = result.Deleted ? string.Empty : Loc.F("DeleteFailed", result.Error);
    }

    public void Refresh()
    {
        var language = _services.Host.Settings.Language;
        LanguageTitle = Loc.F("ModelsIntro", LanguageName(language));
        if (_services.Models.ManifestError is { } manifestError)
        {
            Message = Loc.F("ManifestFailed", manifestError);
        }

        var items = _services.Models.All();
        if (Rows.Select(r => r.Id).SequenceEqual(items.Select(i => i.Entry.Id)))
        {
            for (var i = 0; i < items.Count; i++)
            {
                Rows[i].Update(items[i]);
            }

            return;
        }

        Rows.Clear();
        foreach (var item in items)
        {
            Rows.Add(new ModelRow(item, LanguageName(item.Entry.Language), DownloadAsync, Cancel, DeleteAsync));
        }
    }
}
