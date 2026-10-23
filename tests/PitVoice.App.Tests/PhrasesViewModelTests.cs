using System.IO;
using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Core.Intents;
using PitVoice.Core.Languages;
using PitVoice.Core.Parsing;
using PitVoice.Core.Responses;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The "Own phrases" page without a window (design D9, D10; spec: phrase-builder).</summary>
[Collection("Interface language")]
public sealed class PhrasesViewModelTests : IDisposable
{
    private readonly string _language = Loc.Instance.Language;
    private readonly FakePhraseHost _host = new();

    public PhrasesViewModelTests() => Loc.Instance.SetLanguage("ru");

    public void Dispose()
    {
        Loc.Instance.SetLanguage(_language);
        _host.Dispose();
    }

    private static BuilderOption Option(IntentId intent) => BuilderCommands.PhraseOf(intent)!;

    private static CommandChoice Command(BuilderCommand command) => new(command, Texts.Builder(command));

    private PhrasesViewModel Model() => new(_host);

    private static OwnPhraseRow AddRow(PhrasesViewModel model, string text, BuilderCommand command, IntentId? intent = null)
    {
        model.AddRowCommand.Execute(null);
        var row = model.Rows[^1];
        row.Command = Command(command);
        if (intent is { } id)
        {
            row.Variant = row.Variants.Single(v => v.Option.Intent == id);
        }

        row.Text = text;
        return row;
    }

    // ----- Phrase builder page, Phrase rows

    [Fact]
    public void No_phrases_yet()
    {
        var model = Model();

        Assert.Empty(model.Rows);
        Assert.True(model.IsEmpty);
        Assert.False(model.IsChanged);
        Assert.False(model.HasErrors);
        Assert.False(model.OldFile);
        Assert.Equal("Язык команд: Русский", model.LanguageTitle);
    }

    [Fact]
    public void Saved_file_shows_its_rows_verbs_first()
    {
        _host.WriteFile("""
            lexicon:
              fuel: { action: [плесни] }
            phrases:
              - { text: идет дождь, intent: tires.wet }
            """);

        var model = Model();

        Assert.Equal(["плесни", "идет дождь"], model.Rows.Select(r => r.Text));
        Assert.Equal(BuilderCommands.FuelVerb, model.Rows[0].Option);
        Assert.Equal("Залить (сколько — скажу)", model.Rows[0].Variant!.Name);
        Assert.Equal("Дождевые", model.Rows[1].Variant!.Name);
        Assert.False(model.IsChanged);
    }

    [Fact]
    public void Commands_and_their_variants()
    {
        var model = Model();

        Assert.Equal(["Заправка", "Замена резины", "Ремонт", "Чистка стекла", "Отмена всего"], model.Commands.Select(c => c.Name));
        var row = AddRow(model, "идет дождь", BuilderCommand.Fuel);
        Assert.Equal(["Залить (сколько — скажу)", "Без топлива"], row.Variants.Select(v => v.Name));
        row.Command = Command(BuilderCommand.Tires);
        Assert.Equal(
            ["Все", "Передние", "Задние", "Левые", "Правые", "Переднее левое", "Переднее правое", "Заднее левое", "Заднее правое", "Сухие", "Дождевые", "Не менять"],
            row.Variants.Select(v => v.Name));
    }

    [Fact]
    public void Command_without_a_variant()
    {
        var model = Model();

        var row = AddRow(model, "полный сервис", BuilderCommand.Windshield);

        Assert.False(row.HasVariants);
        Assert.Empty(row.Variants);
        Assert.Null(row.Variant);
        Assert.Equal(IntentCatalog.WindshieldTearoff, row.Option.Intent);
    }

    [Fact]
    public void Command_changed_resets_the_variant()
    {
        var model = Model();
        var row = AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresFront);

        row.Command = Command(BuilderCommand.Fuel);

        Assert.Equal(BuilderCommands.FuelVerb, row.Option);
        row.Command = Command(BuilderCommand.Tires);
        Assert.Equal(IntentCatalog.TiresAll, row.Option.Intent);
    }

    [Fact]
    public void Row_can_be_removed()
    {
        _host.WriteFile("phrases:\n  - { text: идет дождь, intent: tires.wet }\n");
        var model = Model();

        model.Rows[0].RemoveCommand.Execute(null);

        Assert.Empty(model.Rows);
        Assert.True(model.IsChanged);
    }

    // ----- Row check while typing, Phrase conflicts

    [Fact]
    public void One_word_is_not_enough()
    {
        var model = Model();

        var row = AddRow(model, "почисти", BuilderCommand.Windshield);

        Assert.Equal("Нужно хотя бы два слова: одно слово не меняет пит-стоп.", row.Error);
        Assert.True(model.HasErrors);
    }

    [Fact]
    public void Word_outside_the_model()
    {
        _host.Unknown.Add("перед");
        var model = Model();

        var row = AddRow(model, "меняй перёд", BuilderCommand.Tires, IntentCatalog.TiresFront);

        Assert.Equal("«перед» нет в модели распознавания, голосом фраза не сработает.", row.Error);
    }

    [Fact]
    public void Conjunction_inside_a_phrase()
    {
        var model = Model();

        var row = AddRow(model, "шины и стекло", BuilderCommand.Tires, IntentCatalog.TiresAll);

        Assert.Equal("Во фразе не должно быть «и»: союз делит фразу на части.", row.Error);
    }

    [Fact]
    public void Built_in_meaning_differs()
    {
        var model = Model();

        var row = AddRow(model, "не меняй резину", BuilderCommand.Tires, IntentCatalog.TiresAll);

        Assert.Equal("Эта фраза уже означает «Шины не менять».", row.Error);
    }

    [Fact]
    public async Task Built_in_meaning_is_the_same_and_the_row_is_not_saved()
    {
        var model = Model();
        var same = AddRow(model, "меняй все шины", BuilderCommand.Tires, IntentCatalog.TiresAll);
        AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresWet);

        Assert.Empty(same.Error);
        Assert.Equal("Фраза уже работает, сохранять её не нужно.", same.Warning);
        Assert.True(await model.SaveAsync());

        Assert.Equal(["идет дождь"], model.Rows.Select(r => r.Text));
        Assert.DoesNotContain("меняй все шины", File.ReadAllText(_host.File), StringComparison.Ordinal);
    }

    [Fact]
    public void Same_phrase_two_commands()
    {
        var model = Model();
        var first = AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresWet);
        var second = AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresDry);

        Assert.Empty(first.Error);
        Assert.Equal("Фраза уже назначена другой команде: «Дождевые шины».", second.Error);
    }

    [Fact]
    public void Fuel_verb_with_a_negation()
    {
        var model = Model();

        var row = AddRow(model, "не", BuilderCommand.Fuel);

        Assert.Equal("«не» — отрицание, глагол заправки не может его содержать.", row.Error);
    }

    // ----- Phrase check field

    [Fact]
    public void Verb_with_an_amount_is_checked_with_the_unsaved_draft()
    {
        var model = Model();
        AddRow(model, "плесни", BuilderCommand.Fuel);

        model.CheckText = "плесни двадцать литров";
        model.CheckCommand.Execute(null);

        Assert.Equal("заливаем 20 литров", model.CheckResult);
        Assert.True(model.CheckUnderstood);
        Assert.True(model.IsChanged);
        Assert.False(File.Exists(_host.File));
    }

    [Fact]
    public void Not_understood()
    {
        var model = Model();

        model.CheckText = "кто впереди меня";
        model.CheckCommand.Execute(null);

        Assert.Equal("не понял", model.CheckResult);
        Assert.False(model.CheckUnderstood);
    }

    // ----- Draft, save and cancel

    [Fact]
    public async Task Save_applies_at_once()
    {
        var model = Model();
        AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresWet);
        Assert.True(model.IsChanged);

        Assert.True(await model.SaveAsync());

        Assert.False(model.IsChanged);
        Assert.Equal(1, _host.Saves);
        Assert.Contains("идет дождь", File.ReadAllText(_host.File), StringComparison.Ordinal);
        Assert.Equal(["идет дождь"], model.Rows.Select(r => r.Text));
    }

    [Fact]
    public void Cancel_returns_to_what_is_saved()
    {
        _host.WriteFile("phrases:\n  - { text: идет дождь, intent: tires.wet }\n");
        var model = Model();
        model.Rows[0].RemoveCommand.Execute(null);
        AddRow(model, "начнем заново", BuilderCommand.Clear);
        Assert.True(model.IsChanged);

        model.DiscardCommand.Execute(null);

        Assert.Equal(["идет дождь"], model.Rows.Select(r => r.Text));
        Assert.False(model.IsChanged);
    }

    [Fact]
    public void A_change_undone_by_hand_is_no_change()
    {
        _host.WriteFile("phrases:\n  - { text: идет дождь, intent: tires.wet }\n");
        var model = Model();

        model.Rows[0].Text = "идет снег";
        Assert.True(model.IsChanged);
        model.Rows[0].Text = "идет дождь";

        Assert.False(model.IsChanged);
    }

    [Fact]
    public async Task Save_with_an_error_focuses_the_row_and_writes_nothing()
    {
        _host.WriteFile("phrases:\n  - { text: идет дождь, intent: tires.wet }\n");
        var before = File.ReadAllText(_host.File);
        var model = Model();
        var broken = AddRow(model, "почисти", BuilderCommand.Windshield);
        OwnPhraseRow? focused = null;
        model.FocusRequested += r => focused = r;

        Assert.False(await model.SaveAsync());

        Assert.Same(broken, focused);
        Assert.Equal(0, _host.Saves);
        Assert.Equal(before, File.ReadAllText(_host.File));
        Assert.True(model.IsChanged);
        Assert.Equal("Исправьте ошибки в строках, потом сохраните.", model.SaveError);
    }

    [Fact]
    public async Task Save_fails_to_write()
    {
        var model = Model();
        AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresWet);
        _host.SaveError = "access denied";

        Assert.False(await model.SaveAsync());

        Assert.Equal("Фразы не сохранились: access denied", model.SaveError);
        Assert.True(model.IsChanged);
        Assert.Single(model.Rows);
    }

    // ----- Builder without a speech model

    [Fact]
    public void Model_missing_disables_check_and_save_but_not_cancel()
    {
        _host.ModelLoaded = false;
        var model = Model();
        AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresWet);
        model.CheckText = "идет дождь";

        Assert.True(model.NoModel);
        Assert.False(model.SaveDraftCommand.CanExecute(null));
        Assert.False(model.CheckCommand.CanExecute(null));
        Assert.True(model.DiscardCommand.CanExecute(null));
    }

    [Fact]
    public void Model_loads_while_the_page_is_open()
    {
        _host.ModelLoaded = false;
        _host.Unknown.Add("дождь");
        var model = Model();
        var row = AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresWet);
        Assert.Empty(row.Error);

        _host.ModelLoaded = true;
        model.Recheck();

        Assert.False(model.NoModel);
        Assert.True(model.SaveDraftCommand.CanExecute(null));
        Assert.Equal("«дождь» нет в модели распознавания, голосом фраза не сработает.", row.Error);
        Assert.Equal(["идет дождь"], model.Rows.Select(r => r.Text));
    }

    // ----- Additions file the builder cannot show

    [Fact]
    public void Old_tire_target_word()
    {
        _host.WriteFile("lexicon:\n  tires: { targets: { front: [перёд] } }\n");

        var model = Model();

        Assert.True(model.OldFile);
        Assert.Empty(model.Rows);
        Assert.False(model.IsChanged);
    }

    [Fact]
    public async Task Start_over_and_save_replaces_the_file()
    {
        _host.WriteFile("lexicon:\n  tires: { targets: { front: [перёд] } }\n");
        var model = Model();

        model.StartOverCommand.Execute(null);
        Assert.False(model.OldFile);
        Assert.True(model.IsChanged);
        AddRow(model, "идет дождь", BuilderCommand.Tires, IntentCatalog.TiresWet);
        Assert.True(await model.SaveAsync());

        Assert.False(model.OldFile);
        Assert.DoesNotContain("перёд", File.ReadAllText(_host.File), StringComparison.Ordinal);
        Assert.False(Model().OldFile);
    }

    [Fact]
    public void Start_over_without_saving_keeps_the_file()
    {
        _host.WriteFile("lexicon:\n  tires: { targets: { front: [перёд] } }\n");
        var before = File.ReadAllText(_host.File);
        var model = Model();

        model.StartOverCommand.Execute(null);
        ((ILeaveGuard)model).Discard();

        Assert.Equal(before, File.ReadAllText(_host.File));
        Assert.True(model.OldFile);
        Assert.True(Model().OldFile);
    }

    [Fact]
    public void Old_fuel_verbs_are_kept()
    {
        _host.WriteFile("lexicon:\n  fuel: { action: [плесни] }\n");

        var model = Model();

        Assert.False(model.OldFile);
        var row = Assert.Single(model.Rows);
        Assert.Equal(("плесни", "Заправка", "Залить (сколько — скажу)"), (row.Text, row.Command.Name, row.Variant!.Name));
    }

    /// <summary>The host over the real ru pack and a temporary additions directory; the model knows every word but <see cref="Unknown"/>.</summary>
    private sealed class FakePhraseHost : IPhraseHost, IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "pitvoice-phrases-" + Guid.NewGuid().ToString("N"));
        private readonly LanguagePack _pack = LanguagePackLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "languages", "ru.yaml")).Pack!;

        public string File => UserAdditionsLoader.GetFile(_directory, "ru");

        public HashSet<string> Unknown { get; } = [];

        public bool ModelLoaded { get; set; } = true;

        public string? SaveError { get; set; }

        public int Saves { get; private set; }

        public string? Language => "ru";

        public string LanguageName => "Русский";

        public event Action? Changed { add { } remove { } }

        public void WriteFile(string yaml)
        {
            Directory.CreateDirectory(_directory);
            System.IO.File.WriteAllText(File, yaml);
        }

        public UserAdditionsLoadResult? LoadSaved() => UserAdditionsLoader.LoadFile(File);

        public DraftCheck? Check(UserPackAdditions draft)
        {
            var merge = UserAdditions.Merge(_pack, draft);
            var issues = merge.Issues.ToList();
            if (ModelLoaded)
            {
                foreach (var (entry, text) in draft.Words.Select(w => (w.Entry, w.Text)).Concat(draft.Phrases.Select(p => (p.Entry, p.Text))))
                {
                    var missing = Normalizer.SplitWords(text).Where(Unknown.Contains).ToArray();
                    if (missing.Length > 0)
                    {
                        issues.Add(new PackIssue(IssueSeverity.Error, "draft", null, string.Empty, "not in the model")
                        {
                            Entry = entry,
                            Problem = AdditionProblem.NotInModel,
                            Argument = string.Join(", ", missing),
                        });
                    }
                }
            }

            return new DraftCheck(merge, issues, ModelLoaded);
        }

        public string? TryPhrase(UserPackAdditions draft, string text)
        {
            var merge = UserAdditions.Merge(_pack, draft);
            var pack = merge.Applied ? merge.Pack : _pack;
            return new CommandParser(pack).Parse(text) is ParseResult.Accepted accepted ? new ResponseBuilder(pack).Build(accepted) : null;
        }

        public Task<string?> SaveAsync(UserPackAdditions draft)
        {
            if (SaveError is { } error)
            {
                return Task.FromResult<string?>(error);
            }

            Saves++;
            var kept = UserAdditions.Merge(_pack, draft).Kept!;
            return Task.FromResult(UserAdditionsWriter.TryWrite(File, "ru", kept.Words, kept.Phrases));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
