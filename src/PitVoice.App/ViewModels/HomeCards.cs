using PitVoice.App.Controls;
using PitVoice.App.Localization;
using PitVoice.Core.Audio;
using PitVoice.Core.Pit;
using PitVoice.Core.Ptt;
using PitVoice.Core.Settings;
using PitVoice.Host;
using Wpf.Ui.Controls;

namespace PitVoice.App.ViewModels;

/// <summary>How a tile or a word of state is colored: grey, green ("works"), amber or red (a problem).</summary>
internal enum StateTone
{
    Neutral,
    Ok,
    Warn,
    Problem,
}

/// <summary>Where the button of the status card leads.</summary>
internal enum CardAction
{
    None,
    OpenModels,
    OpenSettings,
}

/// <summary>The status card of the home page, one line (spec: Home status card).</summary>
/// <param name="Title">"Всё готово", "Нет модели", "Кнопка голоса не подключена", "Загрузка…".</param>
/// <param name="ToolTip">What to do next: the former hint line.</param>
/// <param name="IsOk">True only when the host is ready: the green mark.</param>
/// <param name="Section">Hue of the icon.</param>
/// <param name="Symbol">Icon of the card.</param>
/// <param name="Action">Where the button leads; <see cref="CardAction.None"/> without a button.</param>
/// <param name="ActionText">"Скачать", "Назначить", ...; empty without a button.</param>
internal sealed record StatusCard(string Title, string ToolTip, bool IsOk, Section Section, SymbolRegular Symbol, CardAction Action, string ActionText)
{
    public bool HasAction => Action != CardAction.None;
}

/// <summary>A check tile of the home page, one line without a title (spec: Home check tiles).</summary>
/// <param name="Value">Device name, binding or the game state: "fifine Microphone", "iRacing не запущен".</param>
/// <param name="Tone">Color of the state dot.</param>
/// <param name="ToolTip">The state in words, so it is not only a color: "Микрофон: подключён".</param>
/// <param name="IsAction">True when the whole tile is the "Назначить кнопку" button to the settings.</param>
/// <param name="DotInText">True for a dot before the text, false for a dot in the corner of the icon.</param>
/// <param name="Section">Hue of the icon.</param>
/// <param name="Symbol">Icon of the tile.</param>
internal sealed record Tile(string Value, StateTone Tone, string ToolTip, bool IsAction, bool DotInText, Section Section, SymbolRegular Symbol)
{
    /// <summary>Name for a screen reader: the value and the state in words.</summary>
    public string AccessibleName => $"{Value}. {ToolTip}";
}

/// <summary>
/// Texts of the home page built from what the host knows (design D4, D5 of simplify-window-screens): pure functions, so
/// the tests check every host state without a window.
/// </summary>
internal static class HomeCards
{
    /// <summary>The status card; its title follows the tray icon state.</summary>
    public static StatusCard Status(HostStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.State == HostState.Ready)
        {
            return new StatusCard(Loc.T("home.Ready"), Loc.T("hint.Ready"), true, Section.Models, SymbolRegular.CheckmarkCircle24, CardAction.None, string.Empty);
        }

        var (action, actionKey) = status.Reason switch
        {
            StatusReason.ModelNotInstalled => (CardAction.OpenModels, "DownloadButton"),
            StatusReason.ModelFailed => (CardAction.OpenModels, "NavModels"),
            StatusReason.PttNotAssigned or StatusReason.PttDeviceDisconnected => (CardAction.OpenSettings, "AssignAction"),
            StatusReason.PttSourceFailed => (CardAction.OpenSettings, "NavSettings"),
            _ => (CardAction.None, null),
        };
        var (section, symbol) = status switch
        {
            { Reason: StatusReason.Starting or StatusReason.ModelLoading } => (Section.Settings, SymbolRegular.Timer24),
            { State: HostState.NoModel } => (Section.Commands, SymbolRegular.ArrowDownload24),
            { State: HostState.NoGame } => (Section.Settings, SymbolRegular.Flag24),
            _ => (Section.Alert, SymbolRegular.ErrorCircle24),
        };
        return new StatusCard(Loc.T($"home.{status.Reason}"), Hint(status), false, section, symbol, action,
            actionKey is null ? string.Empty : Loc.T(actionKey));
    }

    /// <summary>The microphone: the chosen device, or for "как в Windows" the current default one, by name only.</summary>
    public static Tile Microphone(AppSettings settings, IReadOnlyList<AudioDevice> present)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(present);
        if (settings.Microphone is not { } chosen)
        {
            var fallback = present.FirstOrDefault(d => d.IsDefault)?.Name ?? present.FirstOrDefault()?.Name;
            return fallback is null
                ? Mic(Loc.T("MicNoneValue"), StateTone.Problem, Loc.T("MicNone"))
                : Mic(fallback, StateTone.Ok, Loc.T("MicPresent"), Loc.T("MicDefaultTip"));
        }

        return present.Any(d => d.Name == chosen)
            ? Mic(chosen, StateTone.Ok, Loc.T("MicPresent"))
            : Mic(chosen, StateTone.Problem, Loc.T("MicMissing"), Loc.T("MicMissingHint"));
    }

    /// <summary>The voice button: device and button or key without the mode; a problem makes the tile "Назначить кнопку".</summary>
    public static Tile Ptt(HostStatus status, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.PttBinding is not { } binding)
        {
            return Assign(Tip("TilePtt", Loc.T("PttNotAssigned"), Loc.T("hint.PttNotAssigned")));
        }

        if (status.Reason == StatusReason.PttDeviceDisconnected)
        {
            return Assign(Tip("TilePtt", $"{Loc.T("PttDisconnectedShort")} — {Binding(binding)}", Loc.F("hint.PttDeviceDisconnected", binding.Name)));
        }

        return binding.Kind == PttDeviceKind.Joystick
            ? new Tile(Loc.F("PttJoystickTile", binding.Name, binding.Key.Code), StateTone.Ok, Tip("TilePtt", Loc.T("PttConnected")), false, false, Section.Overlay, SymbolRegular.Games24)
            : new Tile(Loc.F("PttKeyboard", binding.Name), StateTone.Ok, Tip("TilePtt", Loc.T("PttKeyboardReady")), false, false, Section.Overlay, SymbolRegular.Keyboard24);
    }

    /// <summary>iRacing: "iRacing не запущен", "iRacing запущен", "iRacing: в машине".</summary>
    public static Tile Game(ConnectionState game)
    {
        var (tone, tip) = game switch
        {
            ConnectionState.NotRunning => (StateTone.Neutral, Loc.T("GameNotRunningHint")),
            ConnectionState.Connected => (StateTone.Neutral, Loc.T("GameConnectedHint")),
            _ => (StateTone.Ok, Loc.T("GameInCarState")),
        };
        return new Tile(Texts.Game(game), tone, tip, false, true, Section.Commands, SymbolRegular.Flag24);
    }

    /// <summary>"SIMAGIC GT Neo, кнопка 1" or "клавиша F13", in the interface language.</summary>
    public static string Binding(PttBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Kind == PttDeviceKind.Joystick
            ? Loc.F("PttJoystick", binding.Name, binding.Key.Code)
            : Loc.F("PttKeyboard", binding.Name);
    }

    private static Tile Mic(string value, StateTone tone, string state, params string[] more) =>
        new(value, tone, Tip("TileMicrophone", state, more), false, false, Section.Home, SymbolRegular.Mic24);

    private static Tile Assign(string tip) =>
        new(Loc.T("AssignButton"), StateTone.Problem, tip, true, false, Section.Overlay, SymbolRegular.Games24);

    /// <summary>"Микрофон: не найден" and more lines under it.</summary>
    private static string Tip(string titleKey, string state, params string[] more) =>
        string.Join('\n', [$"{Loc.T(titleKey)}: {state}", .. more]);

    private static string Hint(HostStatus status) => Loc.F($"hint.{status.Reason}", status.Detail);
}
