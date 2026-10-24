using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>
/// A line of the recent phrases: the text, a chip with the short outcome colored by <see cref="Tone"/> and the time;
/// the full response, the reason and the time with seconds are in <see cref="ToolTip"/> (spec: Recent phrases).
/// </summary>
/// <param name="Time">"22:34".</param>
/// <param name="Text">Recognized text.</param>
/// <param name="Chip">Short responses of an applied phrase ("все шины · +25 л") or the outcome in a word ("не понял").</param>
/// <param name="Tone">Color of the chip.</param>
/// <param name="ToolTip">Full response, reason and time with seconds, one per line.</param>
/// <param name="IsDim">True for a phrase that was not understood: its text is muted.</param>
internal sealed record PhraseRow(string Time, string Text, string Chip, StateTone Tone, string ToolTip, bool IsDim);

/// <summary>
/// "Home": the status card, the three check tiles, the elevation hint and the recent phrases
/// (spec: Home status card, Home check tiles, Recent phrases, Elevation hint).
/// </summary>
internal sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _openSettings;
    private readonly Action _openModels;
    private CardAction _statusAction;

    public HomeViewModel(AppServices services, Action openSettings, Action openModels)
    {
        _services = services;
        _openSettings = openSettings;
        _openModels = openModels;
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
    public partial StatusCard? Status { get; set; }

    [ObservableProperty]
    public partial Tile? Microphone { get; set; }

    [ObservableProperty]
    public partial Tile? Ptt { get; set; }

    [ObservableProperty]
    public partial Tile? Game { get; set; }

    [ObservableProperty]
    public partial bool ElevationHintVisible { get; set; }

    [ObservableProperty]
    public partial bool NoPhrases { get; set; } = true;

    /// <summary>Reads the status, the settings and the microphones again (status or settings changed, page shown).</summary>
    public void Refresh()
    {
        var status = _services.Host.Status;
        var settings = _services.Host.Settings;
        Status = HomeCards.Status(status);
        _statusAction = Status.Action;
        Microphone = HomeCards.Microphone(settings, _services.Platform.GetInputDevices());
        Ptt = HomeCards.Ptt(status, settings);
        Game = HomeCards.Game(status.Facts.Game);
        ElevationHintVisible = status.Facts.GameElevated;
    }

    /// <summary>The button of the status card opens the section where the problem is solved (spec: Home status card).</summary>
    [RelayCommand]
    private void StatusAction()
    {
        switch (_statusAction)
        {
            case CardAction.OpenModels:
                _openModels();
                break;
            case CardAction.OpenSettings:
                _openSettings();
                break;
        }
    }

    /// <summary>The voice button tile with a problem opens the settings (spec: Button not assigned).</summary>
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
        foreach (var entry in _services.Host.Journal.Entries)
        {
            Phrases.Add(Row(entry));
        }

        NoPhrases = Phrases.Count == 0;
    }

    internal static PhraseRow Row(PhraseEntry entry)
    {
        var reason = entry.Outcome switch
        {
            PhraseOutcome.Failed => $"{Texts.Stage(entry.FailedStage!.Value)}: {entry.Error}",
            PhraseOutcome.NotUnderstood when entry.Rejected is { } rejected => Texts.Reject(rejected),
            PhraseOutcome.Unavailable when entry.Unavailable is { } unavailable =>
                Loc.F($"reason.{unavailable}", entry.Error),
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
            PhraseOutcome.Applied => StateTone.Ok,
            PhraseOutcome.InProgress => StateTone.Neutral,
            PhraseOutcome.NotUnderstood or PhraseOutcome.PartiallyApplied => StateTone.Warn,
            _ => StateTone.Problem,
        };
        var chip = entry is { Outcome: PhraseOutcome.Applied, Short.Length: > 0 } ? entry.Short : Texts.Chip(entry.Outcome);
        var at = entry.PressedAt.ToLocalTime();
        var tip = string.Join('\n', new[] { entry.Response, reason, at.ToString("HH:mm:ss", CultureInfo.InvariantCulture) }.Where(l => l.Length > 0));
        return new PhraseRow(at.ToString("HH:mm", CultureInfo.InvariantCulture), text, chip, tone, tip, entry.Outcome == PhraseOutcome.NotUnderstood);
    }
}
