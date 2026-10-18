# Manual check: indicator above the game (stage 8, `add-overlay-indicator`)

The indicator of the tray app in iRacing **borderless** mode on a Release build (spec `overlay-indicator`, TZ §4.10).
Items that need the game use iRacing **Test Drive**.

Build once from the repository root in PowerShell (exit PitVoice from the tray menu first, the build replaces its files):

```
dotnet build PitVoice.sln -c Release -nodeReuse:false
dotnet build-server shutdown
Set-Alias pitvoice "$PWD\src\PitVoice.App\bin\Release\net10.0-windows\PitVoice.exe"
```

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. Defaults: kind A,
size 100 %, opacity 90 %, default place (top right corner of the main monitor).

## 1. States in the game

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | iRacing not running, `pitvoice` started, window closed | look at the top right corner | no indicator | ok (2026-10-18, Release 0.1.1) |
| 1.2 | 1.1 | say "залей тридцать литров" | indicator stays hidden (the indicator is shown only in the car, user decision 2026-10-18); the error cue plays | ok (2026-10-18, 0.1.1); first run showed a grey rectangle behind the indicator — WPF-UI put its Mica backdrop on every window of the app; the indicator windows now clear the DWM backdrop (checked before that decision, when it showed the grey plug) |
| 1.3 | start iRacing Test Drive, stay in the garage menu, then get in the car | wait in the menu; get in the car | hidden in the menu; dim mic icon appears within 5 s in the car | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 1.4 | in the car | say "залей тридцать литров" | listening → recognizing → green check with the success cue at the same moment, short pop; 2.5 s later fades to the dim mic | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 1.5 | in the car | say something that is not a command ("какая погода") | amber question mark with a shake, error cue | ok (2026-10-18, 0.1.1); the spinner shows only for a moment (recognition takes ~50 ms), accepted by the user |
| 1.6 | in the garage menu (not in the car) | say "залей тридцать литров" | indicator stays hidden, the error cue plays. Before the decision the phrase was shown here — reported by the user as a bug | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 1.7 | in the car | press and release PTT quickly (< 250 ms) | blue for a moment, back to idle, no spinner, no result | ok (2026-10-18, 0.1.1) |
| 1.8 | settings: PTT mode "toggle" | press once, say a command, wait 8 s without a second press | after the auto stop: spinner, then the result as after a second press; set mode back to "hold" | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 1.9 | in the car | say a command and press PTT again while the spinner turns | blue "listening" at once; the result of the first phrase is not shown | ok (2026-10-18, Release 0.1.1, Test Drive) |

## 2. Game focus and clicks

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | move the indicator over a button of the iRacing menu (section 4), "Done" | click that place | iRacing gets the click | ok (2026-10-18, 0.1.1): checked in the car (the indicator is hidden in the menus since the in-car decision) — a click on the indicator reached the game, PitVoice was not activated |
| 2.2 | iRacing has the focus, in the car | say a command, then press keys of the game (e.g. the pit black box key) | the game keeps the focus and gets the keys during and after the states | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 2.3 | iRacing has the focus | Alt+Tab to another window and back to iRacing, then say a command | the indicator is shown above iRacing again | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 2.4 | iRacing in **exclusive fullscreen** (if the setting is available) | say a command | indicator not visible, the phrase is executed and the cue plays; the "Overlay" page says it is visible only in windowed and borderless mode | ok (2026-10-18, Release 0.1.1, Test Drive) |

## 3. Kind, size, opacity, visibility

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | "Overlay" page → Kind "Icon and word", indicator at the default place | say a command | the word ("Слушаю", "Распознаю", "Готово"…) appears to the **right** of the icon, the icon stays in place, the pill fits on the screen; in idle only the icon | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 3.2 | 3.1, interface language English | say something that is not a command | "Didn't get it" | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 3.3 | 3.1, "Move", drag the icon to the right edge | release, "Done", say a command | the icon stops short of the edge, the longest word fits to its right | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 3.4 | indicator visible | Size 150 %, then 75 %, then 100 % | changes at once, the icon center stays in place | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 3.5 | indicator visible | Opacity 30 %, then 100 %, then 90 % | changes at once; idle icon dimmer than the active states | ok (2026-10-18, Release 0.1.1, Test Drive) |
| 3.6 | indicator showing the spinner | turn "Show the indicator" off | hides at once, the phrase still ends with its cue; "Move", "Reset position" and the tray item "Move indicator" are hidden; turn it on again | ok (2026-10-18, Release 0.1.1, Test Drive) |

## 4. Moving

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 4.1 | iRacing not running | "Overlay" page → "Move" | indicator shown with a dashed frame, "Done" under it with its center under the icon center; the button reads "Done" | ok (2026-10-18, Release 0.1.1) |
| 4.2 | 4.1 | drag the icon to the bottom left corner and beyond the edge | it stops at the edge, the whole icon stays on the screen; "Done" follows (above the icon at the bottom edge) | ok (2026-10-18, Release 0.1.1) |
| 4.3 | 4.2 | release, press "Done", exit from the tray, start `pitvoice`, then "Move" again | after the start the indicator is in the bottom left corner | ok (2026-10-18, Release 0.1.1; three 2560×1440 monitors) |
| 4.4 | window closed | tray menu → "Move indicator" | move mode on, the window does not open; "Done" ends it | ok (2026-10-18, Release 0.1.1) |
| 4.5 | move mode on, in the car | say a command | the states are shown and the indicator can still be dragged | ok (2026-10-18, Release 0.1.1; three 2560×1440 monitors) |
| 4.6 | sizes 75 % and 200 %, kinds A and B | "Move" | "Done" stays centered under the icon | ok (2026-10-18, Release 0.1.1) |
| 4.7 | "Reset position" | press it | indicator jumps to the top right corner of the main monitor | ok (2026-10-18, Release 0.1.1) |
| 4.8 | `settings.json` read-only (Properties → Read-only) | move the indicator and release | it stays at the new place; the Indicator block shows the save error; clear Read-only afterwards | ok (2026-10-18, Release 0.1.1) |

## 5. Monitors and DPI (if there is a second monitor)

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | two monitors with different scales (e.g. 100 % and 150 %) | move the main window between them | the window is sharp on both, not blurry | ok (2026-10-18, Release 0.1.1; three 2560×1440 monitors) |
| 5.2 | 5.1 | move the indicator to the second monitor, "Done" | sharp, the size matches the scale of that monitor | ok (2026-10-18, Release 0.1.1; three 2560×1440 monitors) |
| 5.3 | 5.2 | exit, disconnect the second monitor, start `pitvoice`, "Move" | indicator at the top right corner of the main monitor; `settings.json` still has the old `overlay.position` | not checked: the monitor was not disconnected (the app kept running, the saved position stayed 1930,66). The fallback to the default place is covered by `OverlayLayoutTests.Saved_place_on_a_removed_monitor_gives_the_default_place` |

## 6. Resources

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 6.1 | `pitvoice` Release, PTT on the wheel, window closed, iRacing Test Drive in the car, indicator idle and visible | `TotalProcessorTime` of `PitVoice.exe` over 5 minutes | < 1 % of one core on average (0.1.0: 0.52 % without the game) | ok (2026-10-18, Release 0.1.1, three monitors, Test Drive in the car, indicator idle and visible): `TotalProcessorTime` over 300 s — 0.76 % of one core with iRacing running and the GT Neo rim bound (0.1.0: 0.52 % without the game, not directly comparable) |
| 6.2 | 6.1, after one phrase | "Working set" of `PitVoice.exe` | ≤ 450 MB (0.1.0: 386 MB) | ok (2026-10-18, 0.1.1): after one phrase working set 392 MB, peak 403 MB, private bytes 862 MB (0.1.0: 386 MB, private 972 MB) |
