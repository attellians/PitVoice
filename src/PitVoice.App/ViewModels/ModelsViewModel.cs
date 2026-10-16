using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>A model row with its buttons.</summary>
internal sealed partial class ModelRow(ModelsViewModel owner, ModelItem item) : ObservableObject
{
    public ModelItem Item { get; private set; } = item;

    public string Id => Item.Entry.Id;

    public string Details => Loc.F("ModelDetails", owner.LanguageName(Item.Entry.Language), Texts.Megabytes(Item.Entry.SizeBytes), Item.Entry.License);

    public string State => Item.State == ModelState.Downloading && Item.Progress is { } progress
        ? Loc.F("ModelProgress", progress.Percent is { } p ? ((int)p).ToString(System.Globalization.CultureInfo.InvariantCulture) : "…",
            Texts.Megabytes(progress.BytesReceived), Texts.Megabytes(progress.TotalBytes ?? Item.Entry.SizeBytes))
        : Loc.T($"model.{Item.State}");

    public double Percent => Item.Progress?.Percent ?? 0;

    public bool IsDownloading => Item.State == ModelState.Downloading;

    public bool CanDownload => Item.State == ModelState.NotInstalled;

    public bool CanDelete => Item.State is ModelState.Installed or ModelState.InUse;

    public string Error => Item.Error is { } error ? Loc.F("ModelError", error) : string.Empty;

    public IAsyncRelayCommand DownloadCommand { get; } = new AsyncRelayCommand(() => owner.DownloadAsync(item.Entry.Id));

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => owner.Cancel(item.Entry.Id));

    public IAsyncRelayCommand DeleteCommand { get; } = new AsyncRelayCommand(() => owner.DeleteAsync(item.Entry.Id));

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
            Rows.Add(new ModelRow(this, item));
        }
    }
}
