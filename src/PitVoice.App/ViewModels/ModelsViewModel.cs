using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>
/// A model row (spec: Models in the window; design D10 of simplify-window-screens): the language name, "● используется"
/// for the model of the commands language, the size of a model that is not downloaded, the progress "N из M МБ" while
/// downloading, and icon buttons. The install date, the id and the license are not shown: they are in "About".
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

    public string LanguageName => _languageName;

    public ModelState State => Item.State;

    public bool IsInUse => Item.State == ModelState.InUse;

    /// <summary>True in the group "Установлены"; downloading and failed models stay in "Можно скачать".</summary>
    public bool IsInstalled => Item.State is ModelState.Installed or ModelState.InUse;

    /// <summary>"45.1 МБ" for a model that is not downloaded, else empty.</summary>
    public string SizeText => Item.State == ModelState.NotInstalled ? Loc.F("ModelSize", Texts.Megabytes(Item.Entry.SizeBytes)) : string.Empty;

    /// <summary>"12.3 из 45.1 МБ" while downloading, else empty.</summary>
    public string ProgressText => Item.State == ModelState.Downloading && Item.Progress is { } progress
        ? Loc.F("ModelBytes", Texts.Megabytes(progress.BytesReceived), Texts.Megabytes(progress.TotalBytes ?? Item.Entry.SizeBytes))
        : string.Empty;

    public double Percent => Item.Progress?.Percent ?? 0;

    public bool IsDownloading => Item.State == ModelState.Downloading;

    public bool CanDownload => Item.State == ModelState.NotInstalled;

    public bool CanDelete => IsInstalled;

    public string Error => Item.Error is { } error ? Loc.F("ModelError", error) : string.Empty;

    public IAsyncRelayCommand DownloadCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    public void Update(ModelItem next)
    {
        Item = next;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// The two groups of the "Models" page over one list of rows: "Установлены" and "Можно скачать". A row moves between them
/// when its state changes without being created again; an empty group is hidden with its title.
/// </summary>
internal sealed partial class ModelGroups : ObservableObject
{
    public ModelGroups(ObservableCollection<ModelRow> rows)
    {
        Installed = new ListCollectionView(rows) { Filter = r => ((ModelRow)r).IsInstalled };
        Available = new ListCollectionView(rows) { Filter = r => !((ModelRow)r).IsInstalled };
        Refresh();
    }

    public ListCollectionView Installed { get; }

    public ListCollectionView Available { get; }

    [ObservableProperty]
    public partial bool HasInstalled { get; set; }

    [ObservableProperty]
    public partial bool HasAvailable { get; set; }

    /// <summary>Sorts the rows into the groups again after their states changed.</summary>
    public void Refresh()
    {
        Installed.Refresh();
        Available.Refresh();
        HasInstalled = !Installed.IsEmpty;
        HasAvailable = !Available.IsEmpty;
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
        Groups = new ModelGroups(Rows);
        services.Models.Changed += () => Ui.Post(Refresh);
        services.Host.SettingsChanged += _ => Ui.Post(Refresh);
        Loc.Instance.LanguageChanged += Refresh;
        Refresh();
    }

    public ObservableCollection<ModelRow> Rows { get; } = [];

    /// <summary>"Установлены" and "Можно скачать" over <see cref="Rows"/>.</summary>
    public ModelGroups Groups { get; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

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
        }
        else
        {
            Rows.Clear();
            foreach (var item in items)
            {
                Rows.Add(new ModelRow(item, LanguageName(item.Entry.Language), DownloadAsync, Cancel, DeleteAsync));
            }
        }

        Groups.Refresh();
    }
}
