using PitVoice.App.Controls;
using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Core.Audio;
using PitVoice.Core.Events;
using PitVoice.Core.Parsing;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>
/// The status card, the check tiles and the phrase rows of the home page (design D3–D5 of simplify-window-screens;
/// spec: Home status card, Home check tiles, Recent phrases).
/// </summary>
[Collection("Interface language")]
public sealed class HomeCardsTests : IDisposable
{
    private const string Model = "vosk-model-small-ru-0.22";
    private static readonly PttBinding Wheel = new(PttKey.JoystickButton("030000004f0400000a00000000000000", 1), "SIMAGIC GT Neo");
    private static readonly PttBinding Key = new(PttKey.KeyboardKey(0x7C), "F13");

    private static readonly HostFacts ReadyFacts = HostFacts.Initial with
    {
        Starting = false,
        ModelId = Model,
        ModelInstalled = true,
        Binding = Wheel,
        PttDeviceConnected = true,
        Game = ConnectionState.NotRunning,
    };

    private static readonly AppSettings Settings = AppSettings.Defaults("ru") with { PttBinding = Wheel, Microphone = "fifine Microphone" };

    private static readonly DateTimeOffset At = new(2026, 10, 19, 12, 34, 56, TimeSpan.Zero);

    private readonly string _language = Loc.Instance.Language;

    public HomeCardsTests() => Loc.Instance.SetLanguage("ru");

    public void Dispose() => Loc.Instance.SetLanguage(_language);

    private static HostStatus Status(HostFacts facts) => HostStatus.Evaluate(facts);

    // --- Status card

    [Fact]
    public void Ready_card_has_the_green_mark_no_button_and_the_hint_in_the_tooltip()
    {
        var card = HomeCards.Status(Status(ReadyFacts with { Game = ConnectionState.InCar }));

        Assert.Equal("Всё готово", card.Title);
        Assert.True(card.IsOk);
        Assert.False(card.HasAction);
        Assert.Equal("Зажмите кнопку и скажите команду.", card.ToolTip);
    }

    public static TheoryData<string, StatusReason, string, string, string> Cards => new()
    {
        { "starting", StatusReason.Starting, "Загрузка…", "None", "" },
        { "loading", StatusReason.ModelLoading, "Загрузка…", "None", "" },
        { "no model", StatusReason.ModelNotInstalled, "Нет модели", "OpenModels", "Скачать" },
        { "model failed", StatusReason.ModelFailed, "Модель не загрузилась", "OpenModels", "Модели" },
        { "pack failed", StatusReason.PackFailed, "Языковой пакет повреждён", "None", "" },
        { "no button", StatusReason.PttNotAssigned, "Кнопка голоса не назначена", "OpenSettings", "Назначить" },
        { "disconnected", StatusReason.PttDeviceDisconnected, "Кнопка голоса не подключена", "OpenSettings", "Назначить" },
        { "source failed", StatusReason.PttSourceFailed, "Кнопка голоса не работает", "OpenSettings", "Настройки" },
        { "no game", StatusReason.GameNotRunning, "Ждём iRacing", "None", "" },
    };

    private static HostFacts FactsOf(string state) => state switch
    {
        "starting" => HostFacts.Initial,
        "loading" => ReadyFacts with { ModelLoading = true },
        "no model" => ReadyFacts with { ModelInstalled = false },
        "model failed" => ReadyFacts with { ModelError = "bad model" },
        "pack failed" => ReadyFacts with { PackError = "bad pack", ModelId = null, ModelInstalled = false },
        "no button" => ReadyFacts with { Binding = null },
        "disconnected" => ReadyFacts with { PttDeviceConnected = false },
        "source failed" => ReadyFacts with { PttSourceError = "no SDL" },
        _ => ReadyFacts,
    };

    /// <summary>Design D4: every state gives a short title, its action and the former hint as the tooltip.</summary>
    [Theory]
    [MemberData(nameof(Cards))]
    public void Card_of_every_state(string state, StatusReason reason, string title, string action, string actionText)
    {
        var status = Status(FactsOf(state));
        Assert.Equal(reason, status.Reason);

        var card = HomeCards.Status(status);

        Assert.Equal(title, card.Title);
        Assert.False(card.IsOk);
        Assert.Equal(Enum.Parse<CardAction>(action), card.Action);
        Assert.Equal(actionText, card.ActionText);
        Assert.NotEmpty(card.ToolTip);
        Assert.DoesNotContain("\n", card.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void No_model_card_sends_to_the_models_page()
    {
        var card = HomeCards.Status(Status(ReadyFacts with { ModelInstalled = false }));

        Assert.Equal("Нет модели", card.Title);
        Assert.Equal(CardAction.OpenModels, card.Action);
        Assert.Equal("Скачать", card.ActionText);
        Assert.Contains(Model, card.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnected_button_card_names_the_device_in_the_tooltip()
    {
        var card = HomeCards.Status(Status(ReadyFacts with { PttDeviceConnected = false }));

        Assert.Equal("Кнопка голоса не подключена", card.Title);
        Assert.Equal(CardAction.OpenSettings, card.Action);
        Assert.Contains("SIMAGIC GT Neo", card.ToolTip, StringComparison.Ordinal);
        Assert.Contains("назначьте другую кнопку", card.ToolTip, StringComparison.Ordinal);
        Assert.Equal(Section.Alert, card.Section);
    }

    [Fact]
    public void Waiting_for_the_game_is_grey_without_a_button()
    {
        var card = HomeCards.Status(Status(ReadyFacts));

        Assert.Equal("Ждём iRacing", card.Title);
        Assert.False(card.HasAction);
        Assert.NotEqual(Section.Alert, card.Section);
    }

    [Fact]
    public void Problem_without_an_action_has_the_advice_in_the_tooltip()
    {
        var card = HomeCards.Status(Status(FactsOf("pack failed")));

        Assert.Equal("Языковой пакет повреждён", card.Title);
        Assert.False(card.HasAction);
        Assert.Contains("переустановите PitVoice", card.ToolTip, StringComparison.Ordinal);
    }

    // --- Check tiles

    [Fact]
    public void Everything_present()
    {
        var microphone = HomeCards.Microphone(Settings, [new AudioDevice("Desk Mic", true), new AudioDevice("fifine Microphone", false)]);
        var ptt = HomeCards.Ptt(Status(ReadyFacts), Settings);
        var game = HomeCards.Game(ConnectionState.NotRunning);

        Assert.Equal(("fifine Microphone", StateTone.Ok), (microphone.Value, microphone.Tone));
        Assert.Equal(("SIMAGIC GT Neo · кнопка 1", StateTone.Ok), (ptt.Value, ptt.Tone));
        Assert.Equal(("iRacing не запущен", StateTone.Neutral), (game.Value, game.Tone));
        Assert.All([microphone, ptt, game], tile =>
        {
            Assert.False(tile.IsAction);
            Assert.DoesNotContain("\n", tile.Value, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("удержание", ptt.Value, StringComparison.Ordinal);
        Assert.False(microphone.DotInText);
        Assert.True(game.DotInText);
    }

    [Fact]
    public void Default_microphone_shows_the_device_name_only()
    {
        var tile = HomeCards.Microphone(Settings with { Microphone = null }, [new AudioDevice("Desk Mic", true), new AudioDevice("Other", false)]);

        Assert.Equal("Desk Mic", tile.Value);
        Assert.Equal(StateTone.Ok, tile.Tone);
        Assert.Contains("Микрофон Windows по умолчанию", tile.ToolTip, StringComparison.Ordinal);
        Assert.DoesNotContain("Как в Windows", tile.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Microphone_missing_is_red_and_says_so_in_the_tooltip()
    {
        var tile = HomeCards.Microphone(Settings, [new AudioDevice("Desk Mic", true)]);

        Assert.Equal("fifine Microphone", tile.Value);
        Assert.Equal(StateTone.Problem, tile.Tone);
        Assert.Contains("не найден", tile.ToolTip, StringComparison.Ordinal);
        Assert.Contains("микрофон Windows по умолчанию", tile.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Button_not_assigned_makes_the_tile_a_button_to_the_settings()
    {
        var tile = HomeCards.Ptt(Status(ReadyFacts with { Binding = null }), Settings with { PttBinding = null });

        Assert.Equal("Назначить кнопку", tile.Value);
        Assert.True(tile.IsAction);
        Assert.Equal(StateTone.Problem, tile.Tone);
        Assert.Contains("не назначена", tile.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Button_device_disconnected_makes_the_tile_a_button_and_names_the_device()
    {
        var tile = HomeCards.Ptt(Status(ReadyFacts with { PttDeviceConnected = false }), Settings);

        Assert.Equal("Назначить кнопку", tile.Value);
        Assert.True(tile.IsAction);
        Assert.Contains("не подключено", tile.ToolTip, StringComparison.Ordinal);
        Assert.Contains("SIMAGIC GT Neo", tile.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Keyboard_binding_is_shown_as_a_key_without_the_mode()
    {
        var tile = HomeCards.Ptt(Status(ReadyFacts with { Binding = Key }), Settings with { PttBinding = Key, PttMode = PttMode.Toggle });

        Assert.Equal("клавиша F13", tile.Value);
        Assert.Equal(StateTone.Ok, tile.Tone);
    }

    [Fact]
    public void Game_tile_states()
    {
        Assert.Equal(("iRacing не запущен", StateTone.Neutral), (HomeCards.Game(ConnectionState.NotRunning).Value, HomeCards.Game(ConnectionState.NotRunning).Tone));
        Assert.Equal(("iRacing запущен", StateTone.Neutral), (HomeCards.Game(ConnectionState.Connected).Value, HomeCards.Game(ConnectionState.Connected).Tone));
        Assert.Equal(("iRacing: в машине", StateTone.Ok), (HomeCards.Game(ConnectionState.InCar).Value, HomeCards.Game(ConnectionState.InCar).Tone));
    }

    [Fact]
    public void Long_device_name_stays_one_line()
    {
        var binding = new PttBinding(Wheel.Key, "SIMAGIC GT Neo Formula Wheel Rim With A Very Long Name");
        var tile = HomeCards.Ptt(Status(ReadyFacts with { Binding = binding }), Settings with { PttBinding = binding });

        Assert.Equal("SIMAGIC GT Neo Formula Wheel Rim With A Very Long Name · кнопка 1", tile.Value);
        Assert.DoesNotContain("\n", tile.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Screen_reader_name_has_the_state_in_words()
    {
        var microphone = HomeCards.Microphone(Settings, [new AudioDevice("fifine Microphone", true)]);
        var ptt = HomeCards.Ptt(Status(ReadyFacts with { PttDeviceConnected = false }), Settings);
        var game = HomeCards.Game(ConnectionState.InCar);

        Assert.Contains("подключён", microphone.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("fifine Microphone", microphone.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("не подключено", ptt.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("в машине", game.AccessibleName, StringComparison.Ordinal);
    }

    // --- Recent phrases

    private static PhraseEntry Entry(PhraseOutcome outcome, string response = "", string shortText = "") =>
        new(1, At, "залей тридцать", outcome, response, null, null, null, null) { Short = shortText, Finished = true };

    public static TheoryData<PhraseOutcome, string, string> Chips => new()
    {
        { PhraseOutcome.InProgress, "…", "Neutral" },
        { PhraseOutcome.Applied, "применено", "Ok" },
        { PhraseOutcome.PartiallyApplied, "частично", "Warn" },
        { PhraseOutcome.NotApplied, "не применилось", "Problem" },
        { PhraseOutcome.NotConnected, "нет игры", "Problem" },
        { PhraseOutcome.NotInCar, "не в машине", "Problem" },
        { PhraseOutcome.CompoundUnavailable, "нет состава", "Problem" },
        { PhraseOutcome.NotUnderstood, "не понял", "Warn" },
        { PhraseOutcome.Failed, "сбой", "Problem" },
        { PhraseOutcome.Unavailable, "недоступно", "Problem" },
    };

    [Theory]
    [MemberData(nameof(Chips))]
    public void Chip_word_and_tone_of_every_outcome(PhraseOutcome outcome, string chip, string tone)
    {
        var entry = outcome switch
        {
            PhraseOutcome.Failed => Entry(outcome) with { FailedStage = PipelineStage.Recognition, Error = "engine crashed" },
            PhraseOutcome.Unavailable => Entry(outcome) with { Unavailable = StatusReason.ModelLoading, Error = Model },
            _ => Entry(outcome),
        };

        var row = HomeViewModel.Row(entry);

        Assert.Equal(chip, row.Chip);
        Assert.Equal(Enum.Parse<StateTone>(tone), row.Tone);
        Assert.Equal("залей тридцать", row.Text);
    }

    [Fact]
    public void Applied_phrase_shows_the_short_responses_and_the_full_one_in_the_tooltip()
    {
        var row = HomeViewModel.Row(Entry(PhraseOutcome.Applied, "меняем все шины, заливаем 25 литров", "все шины · +25 л"));

        Assert.Equal("все шины · +25 л", row.Chip);
        Assert.Equal(StateTone.Ok, row.Tone);
        Assert.Equal(At.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), row.Time);
        Assert.Contains("меняем все шины, заливаем 25 литров", row.ToolTip, StringComparison.Ordinal);
        Assert.Contains(At.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), row.ToolTip, StringComparison.Ordinal);
        Assert.False(row.IsDim);
    }

    [Fact]
    public void Not_understood_phrase_is_dim_with_the_reason_only_in_the_tooltip()
    {
        var row = HomeViewModel.Row(Entry(PhraseOutcome.NotUnderstood, "не понял, повтори") with { Rejected = RejectReason.LowConfidence });

        Assert.Equal("не понял", row.Chip);
        Assert.True(row.IsDim);
        Assert.Contains("не понял, повтори", row.ToolTip, StringComparison.Ordinal);
        Assert.Contains(Texts.Reject(RejectReason.LowConfidence), row.ToolTip, StringComparison.Ordinal);
        Assert.DoesNotContain("повтори", row.Chip, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage_failure_has_the_reason_in_the_tooltip()
    {
        var row = HomeViewModel.Row(Entry(PhraseOutcome.Failed) with { FailedStage = PipelineStage.Recognition, Error = "engine crashed" });

        Assert.Equal(StateTone.Problem, row.Tone);
        Assert.Contains("engine crashed", row.ToolTip, StringComparison.Ordinal);
    }
}
