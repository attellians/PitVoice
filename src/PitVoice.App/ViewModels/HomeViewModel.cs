using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Controls;
using PitVoice.App.Localization;
using PitVoice.Host;
using Wpf.Ui.Controls;

namespace PitVoice.App.ViewModels;

/// <summary>A line of the recent phrases; <see cref="Tone"/> colors only the outcome word (spec: No colored phrase markers).</summary>
internal sealed record PhraseRow(string Time, string Text, string Outcome, string Response, string Detail, StateTone Tone)
{
    public bool HasResponse => Response.Length > 0;

    public bool HasDetail => Detail.Length > 0;
}

/// <summary>
/// "Home": the status card, the three check tiles, the phrase counter, the elevation hint and the recent phrases
/// (spec: Home status card, Home check tiles, Phrase counter, Recent phrases, Elevation hint).
/// </summary>
internal sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _openSettings;

    public HomeViewModel(AppServices services, Action openSettings)
    {
        _services = services;
        _openSettings = openSettings;
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
        CrashOfferVisible = services.Crashes.PendingFatal() is not null;
    }

    /// <summary>"PitVoice crashed last time" (spec: Offer to collect diagnostics after a crash).</summary>
    [ObservableProperty]
    public partial bool CrashOfferVisible { get; set; }

    /// <summary>Why collecting from the block failed, or empty.</summary>
    [ObservableProperty]
    public partial string CollectError { get; set; } = string.Empty;

    public ObservableCollection<PhraseRow> Phrases { get; } = [];

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool StatusOk { get; set; }

    [ObservableProperty]
    public partial Section StatusSection { get; set; }

    [ObservableProperty]
    public partial SymbolRegular StatusSymbol { get; set; }

    [ObservableProperty]
    public partial Tile? Microphone { get; set; }

    [ObservableProperty]
    public partial Tile? Ptt { get; set; }

    [ObservableProperty]
    public partial Tile? Game { get; set; }

    /// <summary>"25 фраз, 23 применено"; empty (hidden) without phrases.</summary>
    [ObservableProperty]
    public partial string Counter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ElevationHintVisible { get; set; }

    [ObservableProperty]
    public partial bool NoPhrases { get; set; } = true;

    /// <summary>Reads the status, the settings and the microphones again (status or settings changed, page shown).</summary>
    public void Refresh()
    {
        var status = _services.Host.Status;
        var settings = _services.Host.Settings;
        var card = HomeCards.Status(status);
        StatusTitle = card.Title;
        StatusHint = card.Hint;
        StatusOk = card.IsOk;
        StatusSection = card.Section;
        StatusSymbol = card.Symbol;
        Microphone = HomeCards.Microphone(settings, _services.Platform.GetInputDevices());
        Ptt = HomeCards.Ptt(status, settings);
        Game = HomeCards.Game(status.Facts.Game);
        ElevationHintVisible = status.Facts.GameElevated;
    }

    /// <summary>"Назначить" on the PTT tile opens the settings (spec: Button not assigned).</summary>
    [RelayCommand]
    private void OpenSettings() => _openSettings();

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

    private void RefreshPhrases()
    {
        Phrases.Clear();
        var journal = _services.Host.Journal;
        foreach (var entry in journal.Entries)
        {
            Phrases.Add(Row(entry));
        }

        NoPhrases = Phrases.Count == 0;
        Counter = HomeCards.Counter(journal.TotalCount, journal.AppliedCount);
    }

    internal static PhraseRow Row(PhraseEntry entry)
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
        var tone = entry.Outcome switch
        {
            PhraseOutcome.Applied or PhraseOutcome.InProgress => StateTone.Neutral,
            PhraseOutcome.NotUnderstood => StateTone.Warn,
            _ => StateTone.Problem,
        };
        return new PhraseRow(
            entry.PressedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            text,
            Texts.Outcome(entry.Outcome),
            entry.Response,
            detail,
            tone);
    }
}
