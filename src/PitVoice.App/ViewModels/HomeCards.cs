using System.Globalization;
using PitVoice.App.Controls;
using PitVoice.App.Localization;
using PitVoice.Core.Audio;
using PitVoice.Core.Pit;
using PitVoice.Core.Plural;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Host;
using Wpf.Ui.Controls;

namespace PitVoice.App.ViewModels;

/// <summary>How a tile or a word of state is colored: grey, green ("works") or red (a problem).</summary>
internal enum StateTone
{
    Neutral,
    Ok,
    Warn,
    Problem,
}

/// <summary>The status card of the home page (spec: Home status card).</summary>
/// <param name="Title">"Всё готово", "Нет модели", "Ошибка — …", "Загрузка…".</param>
/// <param name="Hint">What to do next.</param>
/// <param name="IsOk">True only when the host is ready: the green mark.</param>
/// <param name="Section">Hue of the icon.</param>
/// <param name="Symbol">Icon of the card.</param>
internal sealed record StatusCard(string Title, string Hint, bool IsOk, Section Section, SymbolRegular Symbol);

/// <summary>A check tile of the home page (spec: Home check tiles).</summary>
/// <param name="Title">"Микрофон", "Кнопка PTT", "iRacing".</param>
/// <param name="Value">Device name, binding or the game state.</param>
/// <param name="StateText">The state in a word ("подключена", "не найден"); empty when the value says it all.</param>
/// <param name="Tone">Color of the state word.</param>
/// <param name="Detail">A second line in grey: mode, hint; empty when there is none.</param>
/// <param name="ShowAssign">True to show the "Назначить" link to the settings.</param>
/// <param name="Section">Hue of the icon.</param>
/// <param name="Symbol">Icon of the tile.</param>
internal sealed record Tile(string Title, string Value, string StateText, StateTone Tone, string Detail, bool ShowAssign, Section Section, SymbolRegular Symbol);

/// <summary>
/// Texts of the home page built from what the host knows (design D4 of refresh-window-visual-style): pure functions, so
/// the tests check every host state without a window.
/// </summary>
internal static class HomeCards
{
    /// <summary>The status card; its title follows the tray icon state (<see cref="Texts.State"/>).</summary>
    public static StatusCard Status(HostStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var reason = Texts.Reason(status);
        return status switch
        {
            { State: HostState.Ready } => new StatusCard(Loc.T("HomeReady"), Loc.T("hint.Ready"), true, Section.Models, SymbolRegular.CheckmarkCircle24),
            { Reason: StatusReason.Starting or StatusReason.ModelLoading } => new StatusCard(Loc.T("HomeLoading"), Hint(status), false, Section.Settings, SymbolRegular.Timer24),
            { State: HostState.NoModel } => new StatusCard(Capital(Texts.State(status.State)), Hint(status), false, Section.Commands, SymbolRegular.ArrowDownload24),
            { State: HostState.NoGame } => new StatusCard(Capital(Texts.State(status.State)), Hint(status), false, Section.Settings, SymbolRegular.Flag24),
            _ => new StatusCard($"{Capital(Texts.State(status.State))} — {reason}", Hint(status), false, Section.Alert, SymbolRegular.ErrorCircle24),
        };
    }

    /// <summary>"Микрофон": the chosen device and whether it is in the system; "как в Windows" names the default one.</summary>
    public static Tile Microphone(AppSettings settings, IReadOnlyList<AudioDevice> present)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(present);
        var title = Loc.T("TileMicrophone");
        if (settings.Microphone is not { } chosen)
        {
            var fallback = present.FirstOrDefault(d => d.IsDefault)?.Name ?? present.FirstOrDefault()?.Name;
            return fallback is null
                ? new Tile(title, Loc.T("MicDefault"), Loc.T("MicNone"), StateTone.Problem, string.Empty, false, Section.Home, SymbolRegular.Mic24)
                : new Tile(title, Loc.F("MicDefaultIs", fallback), Loc.T("MicPresent"), StateTone.Ok, string.Empty, false, Section.Home, SymbolRegular.Mic24);
        }

        return present.Any(d => d.Name == chosen)
            ? new Tile(title, chosen, Loc.T("MicPresent"), StateTone.Ok, string.Empty, false, Section.Home, SymbolRegular.Mic24)
            : new Tile(title, chosen, Loc.T("MicMissing"), StateTone.Problem, Loc.T("MicMissingHint"), false, Section.Home, SymbolRegular.Mic24);
    }

    /// <summary>"Кнопка PTT": device and button or key, the mode, and whether the device is connected.</summary>
    public static Tile Ptt(HostStatus status, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(settings);
        var title = Loc.T("TilePtt");
        if (settings.PttBinding is not { } binding)
        {
            return new Tile(title, Loc.T("PttNone"), Loc.T("PttNotAssigned"), StateTone.Problem, string.Empty, true, Section.Overlay, SymbolRegular.Games24);
        }

        var mode = Loc.T($"modeShort.{settings.PttMode}");
        var value = $"{Binding(binding)} · {mode}";
        return status.Reason == StatusReason.PttDeviceDisconnected
            ? new Tile(title, value, Loc.T("PttDisconnectedShort"), StateTone.Problem, string.Empty, true, Section.Overlay, SymbolRegular.Games24)
            : binding.Kind == PttDeviceKind.Joystick
                ? new Tile(title, value, Loc.T("PttConnected"), StateTone.Ok, string.Empty, false, Section.Overlay, SymbolRegular.Games24)
                : new Tile(title, value, Loc.T("PttKeyboardReady"), StateTone.Ok, string.Empty, false, Section.Overlay, SymbolRegular.Keyboard24);
    }

    /// <summary>"iRacing": not running, running (not in the car), in the car.</summary>
    public static Tile Game(ConnectionState game)
    {
        var title = Loc.T("TileGame");
        var value = Capital(Texts.Game(game));
        return game switch
        {
            ConnectionState.NotRunning => new Tile(title, value, string.Empty, StateTone.Neutral, Loc.T("GameNotRunningHint"), false, Section.Commands, SymbolRegular.Flag24),
            ConnectionState.Connected => new Tile(title, value, string.Empty, StateTone.Neutral, Loc.T("GameConnectedHint"), false, Section.Commands, SymbolRegular.Flag24),
            _ => new Tile(title, value, Loc.T("GameInCarState"), StateTone.Ok, string.Empty, false, Section.Commands, SymbolRegular.Flag24),
        };
    }

    /// <summary>"25 фраз, 23 применено"; empty without phrases (spec: Phrase counter).</summary>
    public static string Counter(int total, int applied)
    {
        if (total == 0)
        {
            return string.Empty;
        }

        IPluralRules rules = Loc.Instance.Language == "ru" ? RuPluralRules.Instance : EnPluralRules.Instance;
        var phrases = Loc.F($"phrases.{rules.Select(total)}", total);
        return Loc.F("PhraseCounter", phrases, applied);
    }

    /// <summary>"SIMAGIC GT Neo, кнопка 1" or "клавиша F13", in the interface language.</summary>
    public static string Binding(PttBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Kind == PttDeviceKind.Joystick
            ? Loc.F("PttJoystick", binding.Name, binding.Key.Code)
            : Loc.F("PttKeyboard", binding.Name);
    }

    private static string Hint(HostStatus status) => Loc.F($"hint.{status.Reason}", status.Detail);

    private static string Capital(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
