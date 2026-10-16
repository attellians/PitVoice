using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Core.Ptt;
using PitVoice.Host;
using PitVoice.Host.Updates;

namespace PitVoice.App.ViewModels;

/// <summary>An item of "What is left to set up" with the page it leads to.</summary>
internal sealed record ChecklistEntry(string Text, IRelayCommand Open);

/// <summary>A line of the recent phrases.</summary>
internal sealed record PhraseRow(string Time, string Text, string Outcome, string Response, string Detail, bool IsSuccess);

/// <summary>
/// "Home": the setup checklist, the state of the game and the button, the elevation hint and the recent phrases
/// (spec: Setup checklist, Recent phrases, Elevation hint).
/// </summary>
internal sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<SetupItem> _open;

    public HomeViewModel(AppServices services, Action<SetupItem> open)
    {
        _services = services;
        _open = open;
        services.Host.StatusChanged += _ => Ui.Post(Refresh);
        services.Host.SettingsChanged += _ => Ui.Post(Refresh);
        services.Host.Journal.Changed += () => Ui.Post(RefreshPhrases);
        Loc.Instance.LanguageChanged += () =>
        {
            Refresh();
            RefreshPhrases();
        };
        services.Updates.Changed += _ => Ui.Post(RefreshUpdate);
        Loc.Instance.LanguageChanged += RefreshUpdate;
        Refresh();
        RefreshPhrases();
        RefreshUpdate();
        CrashOfferVisible = services.Crashes.PendingFatal() is not null;
    }

    /// <summary>"Version X is available" (app-updates spec): shown while an update is found, downloading or failed to download.</summary>
    [ObservableProperty]
    public partial bool UpdateVisible { get; set; }

    [ObservableProperty]
    public partial string UpdateTitle { get; set; } = string.Empty;

    /// <summary>What the button does, the download progress or why the download failed.</summary>
    [ObservableProperty]
    public partial string UpdateMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateNowCommand))]
    public partial bool UpdateDownloading { get; set; }

    [ObservableProperty]
    public partial int UpdatePercent { get; set; }

    /// <summary>The release page address to copy when no browser opened.</summary>
    [ObservableProperty]
    public partial string ReleaseCopyAddress { get; set; } = string.Empty;

    /// <summary>"PitVoice crashed last time" (spec: Offer to collect diagnostics after a crash).</summary>
    [ObservableProperty]
    public partial bool CrashOfferVisible { get; set; }

    /// <summary>Why collecting from the block failed, or empty.</summary>
    [ObservableProperty]
    public partial string CollectError { get; set; } = string.Empty;

    public ObservableCollection<ChecklistEntry> Checklist { get; } = [];

    public ObservableCollection<PhraseRow> Phrases { get; } = [];

    [ObservableProperty]
    public partial bool ChecklistVisible { get; set; }

    /// <summary>"Error — PTT button is not assigned".</summary>
    [ObservableProperty]
    public partial string StatusLine { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PttText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ElevationHintVisible { get; set; }

    [ObservableProperty]
    public partial bool NoPhrases { get; set; } = true;

    private void Refresh()
    {
        var status = _services.Host.Status;
        var settings = _services.Host.Settings;

        Checklist.Clear();
        foreach (var item in SetupChecklist.Pending(status, settings))
        {
            Checklist.Add(new ChecklistEntry(Loc.T($"setup.{item}"), new RelayCommand(() => _open(item))));
        }

        ChecklistVisible = Checklist.Count > 0;
        var state = Texts.State(status.State);
        var reason = Texts.Reason(status);
        state = char.ToUpper(state[0], CultureInfo.InvariantCulture) + state[1..];
        StatusLine = reason.Length == 0 ? state : $"{state} — {reason}";
        GameText = Texts.Game(status.Facts.Game);
        PttText = settings.PttBinding is not { } binding ? Loc.T("PttNotAssigned")
            : binding.Kind == PttDeviceKind.Joystick && status.Reason == StatusReason.PttDeviceDisconnected
                ? Loc.F("PttDisconnected", binding.Describe())
                : binding.Describe();
        ElevationHintVisible = status.Facts.GameElevated;
    }

    /// <summary>Either button hides the block until the next fatal crash.</summary>
    [RelayCommand]
    private void DismissCrash()
    {
        _services.Crashes.Acknowledge();
        CrashOfferVisible = false;
    }

    [RelayCommand]
    private async Task CollectAfterCrashAsync()
    {
        DismissCrash();
        CollectError = await _services.Diagnostics.CollectAsync() is { } error ? Loc.F("DiagCollectFailed", error) : string.Empty;
    }

    private bool CanUpdateNow() => !UpdateDownloading;

    /// <summary>Downloads, then the app stops and the new version starts; a failed download offers the update again.</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateNow))]
    private Task UpdateNowAsync() => _services.Updates.UpdateAndRestartAsync();

    [RelayCommand]
    private void OpenRelease()
    {
        var url = _services.Updates.State.Update?.ReleaseUrl;
        if (url is not null)
        {
            ReleaseCopyAddress = Links.TryOpen(url) ? string.Empty : url;
        }
    }

    private void RefreshUpdate()
    {
        var state = _services.Updates.State;
        UpdateVisible = state is { Update: not null, Status: UpdateStatus.Available or UpdateStatus.Downloading or UpdateStatus.Checking or UpdateStatus.Failed };
        if (state.Update is not { } update)
        {
            return;
        }

        UpdateTitle = Loc.F("UpdateAvailableTitle", update.Version);
        UpdateDownloading = state.Status == UpdateStatus.Downloading;
        UpdatePercent = state.Percent;
        UpdateMessage = state switch
        {
            { Status: UpdateStatus.Downloading } => Loc.F("UpdateDownloading", state.Percent),
            { Status: UpdateStatus.Available, Error: { } error } => Loc.F("UpdateDownloadFailed", error),
            _ => Loc.T("UpdateAvailableMessage"),
        };
    }

    private void RefreshPhrases()
    {
        Phrases.Clear();
        foreach (var entry in _services.Host.Journal.Entries)
        {
            Phrases.Add(Row(entry));
        }

        NoPhrases = Phrases.Count == 0;
    }

    private static PhraseRow Row(PhraseEntry entry)
    {
        var detail = entry.Outcome switch
        {
            PhraseOutcome.Failed => $"{Texts.Stage(entry.FailedStage!.Value)}: {entry.Error}",
            PhraseOutcome.NotUnderstood when entry.Rejected is { } rejected => Texts.Reject(rejected),
            PhraseOutcome.Unavailable when entry.Unavailable is { } reason =>
                Loc.F($"reason.{reason}", entry.Error),
            _ => entry.Error is { } cueError ? $"{Texts.Stage(Core.Events.PipelineStage.Cue)}: {cueError}" : string.Empty,
        };
        var text = entry.Text switch
        {
            null => "…",
            "" => Loc.T("NoSpeech"),
            var t => t,
        };
        return new PhraseRow(
            entry.PressedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            text,
            Texts.Outcome(entry.Outcome),
            entry.Response,
            detail,
            entry.Outcome == PhraseOutcome.Applied);
    }
}
