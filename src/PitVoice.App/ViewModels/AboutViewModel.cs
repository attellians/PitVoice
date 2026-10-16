using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Host.Updates;
using PitVoice.Models;

namespace PitVoice.App.ViewModels;

/// <summary>A third-party component or a model with its license.</summary>
internal sealed record LicensedItem(string Name, string Version, string License, string Url);

/// <summary>
/// "About" (spec: About section): version, license, repository, the notices file, components and models with their
/// licenses, and the update check button (or why there is none in a build that was not installed).
/// </summary>
internal sealed partial class AboutViewModel : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/attellians/PitVoice";

    private readonly AppServices _services;

    public AboutViewModel(AppServices services)
    {
        _services = services;
        Version = CurrentVersion;
        Components = LoadComponents();
        Models = services.Models.Manifest?.Entries
            .Where(e => e.Type == ModelType.Asr)
            .Select(e => new LicensedItem(e.Id, string.Empty, e.License, e.Source))
            .ToArray() ?? [];
        IsInstalled = services.Updates.IsInstalled;
        NoticesUrl = NoticesAddress(Version, IsInstalled);
        services.Updates.Changed += _ => Ui.Post(RefreshUpdate);
        Loc.Instance.LanguageChanged += RefreshUpdate;
        RefreshUpdate();
    }

    /// <summary>PitVoice version without the commit hash.</summary>
    public static string CurrentVersion { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3)
        ?? "?";

    public string Version { get; }

    public IReadOnlyList<LicensedItem> Components { get; }

    public IReadOnlyList<LicensedItem> Models { get; }

    public static string Repository => RepositoryUrl;

    /// <summary>The notices file of this version; a build that was not installed has no tag, so it gets the main branch (design decision 9).</summary>
    public string NoticesUrl { get; }

    /// <summary>False for a build that was not installed: no button, a text instead (spec: Developer build).</summary>
    public bool IsInstalled { get; }

    public bool IsNotInstalled => !IsInstalled;

    /// <summary>The address to copy when no browser opened (spec: Browser cannot be opened).</summary>
    [ObservableProperty]
    public partial string CopyAddress { get; set; } = string.Empty;

    /// <summary>Result of the last check: the latest, the version found or why it failed.</summary>
    [ObservableProperty]
    public partial string UpdateResult { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    public partial bool Checking { get; set; }

    internal static string NoticesAddress(string version, bool installed) =>
        $"{RepositoryUrl}/blob/{(installed ? "v" + version : "main")}/THIRD-PARTY-NOTICES.md";

    [RelayCommand]
    private void Open(string url) => CopyAddress = Links.TryOpen(url) ? string.Empty : url;

    private bool CanCheckUpdates() => !Checking && IsInstalled;

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync() => await _services.Updates.CheckNowAsync();

    private void RefreshUpdate()
    {
        var state = _services.Updates.State;
        Checking = state.Status is UpdateStatus.Checking or UpdateStatus.Downloading;
        UpdateResult = state switch
        {
            { Status: UpdateStatus.Checking } => Loc.T("UpdateChecking"),
            { Status: UpdateStatus.UpToDate } => Loc.T("UpdateLatest"),
            { Status: UpdateStatus.Failed, Error: var error } => Loc.F("UpdateCheckFailed", error),
            { Update: { } update } => Loc.F("UpdateFound", update.Version),
            _ => string.Empty,
        };
    }

    internal static IReadOnlyList<LicensedItem> LoadComponents()
    {
        using var stream = typeof(AboutViewModel).Assembly.GetManifestResourceStream("PitVoice.App.ThirdParty.json");
        if (stream is null)
        {
            return [];
        }

        return JsonSerializer.Deserialize<LicensedItem[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
