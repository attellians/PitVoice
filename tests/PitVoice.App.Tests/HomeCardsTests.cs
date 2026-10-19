using PitVoice.App.Controls;
using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Core.Audio;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Host;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The status card, the check tiles and the counter of the home page (design D4; spec: Home status card, Home check tiles, Phrase counter).</summary>
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

    private readonly string _language = Loc.Instance.Language;

    public HomeCardsTests() => Loc.Instance.SetLanguage("ru");

    public void Dispose() => Loc.Instance.SetLanguage(_language);

    private static HostStatus Status(HostFacts facts) => HostStatus.Evaluate(facts);

    [Fact]
    public void Ready_card_has_the_green_mark_and_the_hint()
    {
        var card = HomeCards.Status(Status(ReadyFacts with { Game = ConnectionState.InCar }));

        Assert.Equal("Всё готово", card.Title);
        Assert.True(card.IsOk);
        Assert.Equal("Зажмите кнопку и скажите команду.", card.Hint);
    }

    [Fact]
    public void No_model_card_sends_to_the_models_page_without_the_mark()
    {
        var card = HomeCards.Status(Status(ReadyFacts with { ModelInstalled = false }));

        Assert.Equal("Нет модели", card.Title);
        Assert.False(card.IsOk);
        Assert.Contains("«Модели»", card.Hint, StringComparison.Ordinal);
        Assert.Contains(Model, card.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnected_device_is_an_error_with_the_device_name()
    {
        var card = HomeCards.Status(Status(ReadyFacts with { PttDeviceConnected = false }));

        Assert.Equal("Ошибка — устройство кнопки «SIMAGIC GT Neo» не подключено", card.Title);
        Assert.False(card.IsOk);
        Assert.Equal(Section.Alert, card.Section);
    }

    [Fact]
    public void Starting_shows_loading()
    {
        Assert.Equal("Загрузка…", HomeCards.Status(Status(HostFacts.Initial)).Title);
        Assert.Equal("Загрузка…", HomeCards.Status(Status(ReadyFacts with { ModelLoading = true })).Title);
    }

    [Fact]
    public void No_game_is_not_an_error()
    {
        var card = HomeCards.Status(Status(ReadyFacts));

        Assert.Equal("Нет связи с iRacing", card.Title);
        Assert.False(card.IsOk);
        Assert.NotEqual(Section.Alert, card.Section);
    }

    [Fact]
    public void Microphone_present_has_a_green_dot()
    {
        var tile = HomeCards.Microphone(Settings, [new AudioDevice("Desk Mic", true), new AudioDevice("fifine Microphone", false)]);

        Assert.Equal("fifine Microphone", tile.Value);
        Assert.Equal(StateTone.Ok, tile.Tone);
    }

    [Fact]
    public void Microphone_missing_is_red()
    {
        var tile = HomeCards.Microphone(Settings, [new AudioDevice("Desk Mic", true)]);

        Assert.Equal("fifine Microphone", tile.Value);
        Assert.Equal("не найден", tile.StateText);
        Assert.Equal(StateTone.Problem, tile.Tone);
    }

    [Fact]
    public void Default_microphone_names_the_current_default_device()
    {
        var tile = HomeCards.Microphone(Settings with { Microphone = null }, [new AudioDevice("Desk Mic", true), new AudioDevice("Other", false)]);

        Assert.Equal("Как в Windows (Desk Mic)", tile.Value);
        Assert.Equal(StateTone.Ok, tile.Tone);
    }

    [Fact]
    public void Ptt_tile_shows_device_button_mode_and_connection()
    {
        var tile = HomeCards.Ptt(Status(ReadyFacts), Settings);

        Assert.Equal("SIMAGIC GT Neo, кнопка 1 · удержание", tile.Value);
        Assert.Equal("подключена", tile.StateText);
        Assert.Equal(StateTone.Ok, tile.Tone);
        Assert.False(tile.ShowAssign);
    }

    [Fact]
    public void Ptt_tile_shows_the_disconnected_device_in_red()
    {
        var tile = HomeCards.Ptt(Status(ReadyFacts with { PttDeviceConnected = false }), Settings);

        Assert.Equal("не подключено", tile.StateText);
        Assert.Equal(StateTone.Problem, tile.Tone);
        Assert.True(tile.ShowAssign);
    }

    [Fact]
    public void Ptt_tile_without_a_button_offers_to_assign()
    {
        var tile = HomeCards.Ptt(Status(ReadyFacts with { Binding = null }), Settings with { PttBinding = null });

        Assert.Equal("не назначена", tile.StateText);
        Assert.Equal(StateTone.Problem, tile.Tone);
        Assert.True(tile.ShowAssign);
    }

    [Fact]
    public void Keyboard_binding_is_shown_as_a_key()
    {
        var tile = HomeCards.Ptt(Status(ReadyFacts with { Binding = Key }), Settings with { PttBinding = Key, PttMode = PttMode.Toggle });

        Assert.Equal("клавиша F13 · рация", tile.Value);
        Assert.Equal(StateTone.Ok, tile.Tone);
    }

    [Fact]
    public void Game_tile_states()
    {
        var notRunning = HomeCards.Game(ConnectionState.NotRunning);
        Assert.Equal("Не запущен", notRunning.Value);
        Assert.Equal(StateTone.Neutral, notRunning.Tone);
        Assert.NotEmpty(notRunning.Detail);

        Assert.Equal(StateTone.Neutral, HomeCards.Game(ConnectionState.Connected).Tone);
        Assert.Equal(StateTone.Ok, HomeCards.Game(ConnectionState.InCar).Tone);
    }

    [Fact]
    public void Counter_counts_in_words_and_hides_without_phrases()
    {
        Assert.Equal(string.Empty, HomeCards.Counter(0, 0));
        Assert.Equal("25 фраз, 23 применено", HomeCards.Counter(25, 23));
        Assert.Equal("1 фраза, 1 применено", HomeCards.Counter(1, 1));
        Assert.Equal("3 фразы, 0 применено", HomeCards.Counter(3, 0));

        Loc.Instance.SetLanguage("en");
        Assert.Equal("1 phrase, 1 applied", HomeCards.Counter(1, 1));
        Assert.Equal("25 phrases, 23 applied", HomeCards.Counter(25, 23));
    }

    [Fact]
    public void Phrase_rows_color_only_the_outcome()
    {
        var at = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var applied = HomeViewModel.Row(new PhraseEntry(1, at, "залей тридцать", PhraseOutcome.Applied, "заливаем 30 литров", null, null, null, null));
        var notUnderstood = HomeViewModel.Row(new PhraseEntry(2, at, "залей", PhraseOutcome.NotUnderstood, "не понял, повтори", Core.Parsing.RejectReason.LowConfidence, null, null, null));
        var failed = HomeViewModel.Row(new PhraseEntry(3, at, "залей тридцать", PhraseOutcome.NotConnected, "нет связи с игрой", null, null, null, null));

        Assert.Equal(StateTone.Neutral, applied.Tone);
        Assert.Equal(StateTone.Warn, notUnderstood.Tone);
        Assert.Equal(StateTone.Problem, failed.Tone);
        Assert.Equal("не понял", notUnderstood.Outcome);
        Assert.True(notUnderstood.HasDetail);
    }
}
