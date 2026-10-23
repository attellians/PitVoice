using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.App.Localization;
using PitVoice.Core.Languages;
using PitVoice.Host;

namespace PitVoice.App.ViewModels;

/// <summary>A page that may hold unsaved changes; the window asks before leaving it (design D8).</summary>
internal interface ILeaveGuard
{
    bool HasUnsavedChanges { get; }

    /// <summary>Saves the changes; false when they could not be saved and the page must stay.</summary>
    Task<bool> SaveAsync();

    /// <summary>Drops the changes and shows what is saved.</summary>
    void Discard();
}

/// <summary>What the "Own phrases" page needs from the host: <see cref="HostPhrases"/> in the app, a fake in tests.</summary>
internal interface IPhraseHost
{
    /// <summary>Code of the commands language whose built-in pack is loaded, or null.</summary>
    string? Language { get; }

    /// <summary>"Русский".</summary>
    string LanguageName { get; }

    /// <summary>The additions file of the language as it is on disk; null when there is none.</summary>
    UserAdditionsLoadResult? LoadSaved();

    DraftCheck? Check(UserPackAdditions draft);

    string? TryPhrase(UserPackAdditions draft, string text);

    Task<string?> SaveAsync(UserPackAdditions draft);

    /// <summary>The session changed: a model loaded or unloaded, the language changed.</summary>
    event Action? Changed;
}

/// <summary>The host of the app behind the "Own phrases" page.</summary>
internal sealed class HostPhrases(AppHost host) : IPhraseHost
{
    public string? Language => host.BuiltInPack?.Language;

    public string LanguageName => host.Languages.FirstOrDefault(l => l.Code == Language)?.Name ?? Language ?? string.Empty;

    public UserAdditionsLoadResult? LoadSaved() =>
        Language is { } language ? UserAdditionsLoader.LoadFile(UserAdditionsLoader.GetFile(host.AdditionsDirectory, language)) : null;

    public DraftCheck? Check(UserPackAdditions draft) => host.CheckDraft(draft);

    public string? TryPhrase(UserPackAdditions draft, string text) => host.TryPhrase(draft, text);

    public Task<string?> SaveAsync(UserPackAdditions draft) => host.SaveAdditionsAsync(draft);

    public event Action? Changed
    {
        add => host.SessionChanged += value;
        remove => host.SessionChanged -= value;
    }
}

/// <summary>A command in the list of a row.</summary>
internal sealed record CommandChoice(BuilderCommand Command, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A variant in the list of a row.</summary>
internal sealed record VariantChoice(BuilderOption Option, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A row of the page: phrase, command and variant, with the message under it (spec: Phrase rows).</summary>
internal sealed partial class OwnPhraseRow : ObservableObject
{
    private readonly Action<OwnPhraseRow> _remove;

    public OwnPhraseRow(string text, BuilderOption option, Action<OwnPhraseRow> remove)
    {
        _remove = remove;
        Text = text;
        Command = new CommandChoice(option.Command, Texts.Builder(option.Command));
        Variant = Variants.FirstOrDefault(v => v.Option == option);
    }

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial CommandChoice Command { get; set; }

    /// <summary>Variants of the command; empty for a command without them, and the list is then disabled.</summary>
    public IReadOnlyList<VariantChoice> Variants { get; private set; } = [];

    public bool HasVariants => Variants.Count > 0;

    [ObservableProperty]
    public partial VariantChoice? Variant { get; set; }

    /// <summary>The choice the row means.</summary>
    public BuilderOption Option => Variant?.Option ?? BuilderCommands.Of(Command.Command)[0];

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Warning { get; set; } = string.Empty;

    [RelayCommand]
    private void Remove() => _remove(this);

    /// <summary>Another command resets the variant to its first one (spec: Command changed).</summary>
    partial void OnCommandChanged(CommandChoice value)
    {
        Variants = BuilderCommands.Of(value.Command)
            .Where(o => o.Variant is not null)
            .Select(o => new VariantChoice(o, Texts.Variant(o)))
            .ToArray();
        OnPropertyChanged(nameof(Variants));
        OnPropertyChanged(nameof(HasVariants));
        Variant = Variants.FirstOrDefault();
    }
}

/// <summary>
/// The "Own phrases" page (design D9): rows of a draft, the saved snapshot they are compared with by value, the check of
/// every row on each change, the check field, save and cancel. The draft goes through the same check as the file.
/// </summary>
internal sealed partial class PhrasesViewModel : ObservableObject, ILeaveGuard
{
    private readonly IPhraseHost _host;
    private IReadOnlyList<(string Text, BuilderOption Option)> _saved = [];
    private bool _startedOver;
    private bool _loading;

    public PhrasesViewModel(IPhraseHost host)
    {
        _host = host;
        Commands = BuilderCommands.Commands.Select(c => new CommandChoice(c, Texts.Builder(c))).ToArray();
        Rows.CollectionChanged += (_, _) => Changed();
        host.Changed += () => Ui.Post(Recheck);
        Load();
    }

    public ObservableCollection<OwnPhraseRow> Rows { get; } = [];

    public IReadOnlyList<CommandChoice> Commands { get; }

    /// <summary>"Язык команд: Русский".</summary>
    public string LanguageTitle => Loc.F("CommandsLanguage", _host.LanguageName);

    /// <summary>True when the draft differs from what is saved (spec: Draft, save and cancel).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand))]
    public partial bool IsChanged { get; set; }

    /// <summary>No speech model: words cannot be checked, so "Проверить" and "Сохранить" are off (spec: Builder without a speech model).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand), nameof(CheckCommand))]
    public partial bool NoModel { get; set; }

    /// <summary>The file on disk cannot be shown here (spec: Additions file the builder cannot show).</summary>
    [ObservableProperty]
    public partial bool OldFile { get; set; }

    [ObservableProperty]
    public partial bool HasErrors { get; set; }

    /// <summary>No rows: the page says there are no own phrases yet (spec: No phrases yet).</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDraftCommand))]
    public partial bool IsSaving { get; set; }

    /// <summary>Why the last save did not happen, or empty.</summary>
    [ObservableProperty]
    public partial string SaveError { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    public partial string CheckText { get; set; } = string.Empty;

    /// <summary>The response of the checked phrase or "не понял"; empty before a check.</summary>
    [ObservableProperty]
    public partial string CheckResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CheckUnderstood { get; set; }

    public bool HasUnsavedChanges => IsChanged;

    /// <summary>Asks the page to focus a row: the first row with an error on a failed save.</summary>
    public event Action<OwnPhraseRow>? FocusRequested;

    [RelayCommand]
    private void AddRow()
    {
        var row = Add(string.Empty, BuilderCommands.All.First(o => o.Command == BuilderCommand.Tires));
        FocusRequested?.Invoke(row);
    }

    /// <summary>"Начать заново": the old file is dropped from the draft; the disk is untouched until a save.</summary>
    [RelayCommand]
    private void StartOver()
    {
        _startedOver = true;
        OldFile = false;
        Changed();
    }

    [RelayCommand]
    private void Discard() => Load();

    void ILeaveGuard.Discard() => Load();

    private bool CanSave() => IsChanged && !NoModel && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveDraftAsync() => SaveAsync();

    private bool CanCheck() => !NoModel && !string.IsNullOrWhiteSpace(CheckText);

    /// <summary>Parses the phrase as the app would after a save; nothing reaches the game (spec: Phrase check field).</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private void Check()
    {
        var response = _host.TryPhrase(Draft(), CheckText);
        CheckUnderstood = response is not null;
        CheckResult = response ?? Loc.T("PhrasesNotUnderstood");
    }

    public async Task<bool> SaveAsync()
    {
        SaveError = string.Empty;
        Recheck();
        if (NoModel)
        {
            return false;
        }

        if (Rows.FirstOrDefault(r => r.Error.Length > 0) is { } broken)
        {
            SaveError = Loc.T("PhrasesHaveErrors");
            FocusRequested?.Invoke(broken);
            return false;
        }

        IsSaving = true;
        try
        {
            if (await _host.SaveAsync(Draft()) is { } error)
            {
                SaveError = Loc.F("PhrasesSaveFailed", error);
                return false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SaveError = Loc.F("PhrasesSaveFailed", e.Message);
            return false;
        }
        finally
        {
            IsSaving = false;
        }

        Load();
        return true;
    }

    /// <summary>The rows as additions; the row index is the entry, so problems come back to their rows.</summary>
    public UserPackAdditions Draft()
    {
        var words = new List<AdditionWord>();
        var phrases = new List<AdditionPhrase>();
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (row.Option.Kind == BuilderKind.Verb)
            {
                words.Add(new AdditionWord(UserAdditionsLoader.FuelVerbClass, row.Text, null) { Entry = i });
            }
            else
            {
                phrases.Add(new AdditionPhrase(row.Text, row.Option.Intent, null) { Entry = i });
            }
        }

        return new UserPackAdditions("draft", _host.Language, words, phrases);
    }

    /// <summary>Checks every row and shows its first error, else its first warning (spec: Row check while typing).</summary>
    public void Recheck()
    {
        if (_loading)
        {
            return;
        }

        var check = _host.Check(Draft());
        NoModel = check is not { ModelLoaded: true };
        var issues = check?.Issues ?? [];
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            var own = issues.Where(p => p.Entry == i).ToArray();
            row.Error = string.IsNullOrWhiteSpace(row.Text)
                ? Loc.T("problem.Empty")
                : own.FirstOrDefault(p => p.Severity == IssueSeverity.Error) is { } error ? Texts.Problem(error) : string.Empty;
            row.Warning = row.Error.Length == 0 && own.FirstOrDefault(p => p.Severity == IssueSeverity.Warning) is { } warning
                ? Texts.Problem(warning)
                : string.Empty;
        }

        HasErrors = Rows.Any(r => r.Error.Length > 0);
    }

    /// <summary>Shows what is saved: the rows of the file, or an empty list and the message for a file the page cannot show.</summary>
    private void Load()
    {
        _loading = true;
        try
        {
            foreach (var row in Rows)
            {
                row.PropertyChanged -= OnRowChanged;
            }

            Rows.Clear();
            _startedOver = false;
            var loaded = _host.LoadSaved();
            OldFile = loaded is { NotBuilderFormat: true }
                || (loaded?.Additions?.Language is { } language && language != _host.Language);
            if (!OldFile && loaded?.Additions is { } additions)
            {
                var entries = additions.Words.Select(w => (w.Entry, w.Text, Option: BuilderCommands.FuelVerb))
                    .Concat(additions.Phrases.Select(p => (p.Entry, p.Text, Option: BuilderCommands.PhraseOf(p.Intent)!)))
                    .OrderBy(e => e.Entry);
                foreach (var (_, text, option) in entries)
                {
                    Add(text, option);
                }
            }

            _saved = Snapshot();
            CheckResult = string.Empty;
            SaveError = string.Empty;
        }
        finally
        {
            _loading = false;
        }

        Changed();
    }

    private OwnPhraseRow Add(string text, BuilderOption option)
    {
        var row = new OwnPhraseRow(text, option, r => Rows.Remove(r));
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
        return row;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OwnPhraseRow.Text) or nameof(OwnPhraseRow.Command) or nameof(OwnPhraseRow.Variant))
        {
            Changed();
        }
    }

    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        IsChanged = _startedOver || !Snapshot().SequenceEqual(_saved);
        IsEmpty = Rows.Count == 0;
        Recheck();
    }

    private (string Text, BuilderOption Option)[] Snapshot() => Rows.Select(r => (r.Text.Trim(), r.Option)).ToArray();
}
