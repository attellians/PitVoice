using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Core.Ptt;
using PitVoice.Host;

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
        Refresh();
        RefreshPhrases();
    }

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
