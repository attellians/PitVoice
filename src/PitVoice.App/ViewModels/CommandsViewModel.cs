using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Core.Languages;
using PitVoice.Core.Plural;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>"Commands" (spec: Command reference, User additions in the reference).</summary>
internal sealed partial class CommandsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _openPhrases;

    /// <param name="services">The app services.</param>
    /// <param name="openPhrases">Opens the "Own phrases" page inside the section.</param>
    public CommandsViewModel(AppServices services, Action openPhrases)
    {
        _services = services;
        _openPhrases = openPhrases;
        services.Host.SessionChanged += () => Ui.Post(Refresh);
        Loc.Instance.LanguageChanged += Refresh;
        Refresh();
    }

    public ObservableCollection<GroupEntry> Groups { get; } = [];

    /// <summary>"Язык команд: Русский" (spec: Language switch).</summary>
    [ObservableProperty]
    public partial string LanguageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PackError { get; set; } = string.Empty;

    /// <summary>"3 свои фразы работают" (spec: Phrases card state).</summary>
    [ObservableProperty]
    public partial string PhrasesState { get; set; } = string.Empty;

    /// <summary>True when own phrases work: the green dot of the "Свои фразы" card.</summary>
    [ObservableProperty]
    public partial bool PhrasesApplied { get; set; }

    /// <summary>False without a pack: then the card and its "Настроить" are hidden (spec: Language pack failed to load).</summary>
    [ObservableProperty]
    public partial bool HasPack { get; set; }

    [RelayCommand]
    private void Configure() => _openPhrases();

    /// <summary>Builds the cards again; the "Слова группы" blocks come back collapsed (spec: Words block state is per group).</summary>
    public void Refresh()
    {
        Groups.Clear();
        var pack = _services.Host.Pack;
        var status = _services.Host.Status;
        var language = _services.Host.Settings.Language;
        LanguageTitle = Loc.F("CommandsLanguage", _services.Host.Languages.FirstOrDefault(l => l.Code == language)?.Name ?? language);
        PackError = pack is null && status.Reason == StatusReason.PackFailed ? Loc.F("reason.PackFailed", status.Detail) : string.Empty;
        HasPack = pack is not null;
        if (pack is null)
        {
            PhrasesState = string.Empty;
            PhrasesApplied = false;
            return;
        }

        foreach (var group in CommandCards.Build(CommandReference.Build(pack)))
        {
            Groups.Add(group);
        }

        (PhrasesState, PhrasesApplied) = CardState(_services.Host.Additions, pack);
    }

    /// <summary>The state line of the "Свои фразы" card and whether its green dot shows (spec: Phrases card state).</summary>
    public static (string Text, bool Applied) CardState(PackAdditionsResult? additions, LanguagePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var count = pack.UserEntries.Count + pack.UserPhrases.Count;
        if (additions is { Exists: true, Applied: false })
        {
            return (Loc.T("PhrasesNotApplied"), false);
        }

        if (count == 0)
        {
            return (Loc.T("PhrasesNone"), false);
        }

        IPluralRules rules = Loc.Instance.Language == "ru" ? RuPluralRules.Instance : EnPluralRules.Instance;
        return (Loc.F($"ownPhrases.{rules.Select(count)}", count), true);
    }
}
