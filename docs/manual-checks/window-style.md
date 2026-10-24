# Manual check: the look of the window (`simplify-window-screens`)

The simplified screens on a Release build (specs `tray-app`, `window-visual-style`, `app-settings`, `overlay-indicator`,
`model-management`, `language-packs`; TZ FR-APP-5…FR-APP-7, FR-APP-9, FR-APP-12, FR-OVL-7, FR-OVL-8): the one-line status
card and tiles of Home, the result chips of the recent phrases, the threshold slider, the "VR mode" row on Overlay, the
two model groups, the "Own phrases" title and the license line — in both themes, both interface languages, at the
minimum width and two DPI scales. Items that need the game use iRacing **Test Drive**. This list replaces the one of
`refresh-window-visual-style` (2026-10-19); the look it checked (hues, cards, icons, theme switch) is unchanged.

Build once from the repository root in PowerShell (exit PitVoice from the tray menu first, the build replaces its files):

```
dotnet build PitVoice.sln -c Release -nodeReuse:false
dotnet build-server shutdown
Set-Alias pitvoice "$PWD\src\PitVoice.App\bin\Release\net10.0-windows\PitVoice.exe"
```

**Working profile.** Point settings, models and own phrases at the manual-run folders (`spike/` is ignored by git) and
start the app from the same PowerShell window, so it inherits the variables:

```
$env:PITVOICE_SETTINGS = "$PWD\spike\c1\settings.json"
$env:PITVOICE_MODELS_DIR = "$PWD\spike\c1\models"
$env:PITVOICE_USER_LANGUAGES = "$PWD\spike\c1\languages"
```

States without hardware come from the settings file: an unknown joystick GUID in `ptt.binding` is a disconnected wheel,
`"binding": null` is no button, a `microphone` name that is not in the system is a missing microphone, an empty
`PITVOICE_MODELS_DIR` folder is no model.

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. Defaults: theme
"as in Windows", interface language Russian, window 1000 × 760.

**Who checks what.** The user checks only what needs a voice, the hardware, the network or a Windows setting: 1.3, 3.2,
3.3, 3.4, 4.2, 4.4, 6.2 (150 %), 7.2. Everything else is checked by driving the Release build through UI Automation with
screenshots and the settings file; such results say "checked by screenshot".

## 1. Themes and language

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | dark theme, Russian | open Home, Settings, Overlay, Models, Commands → Own phrases, About | every page readable; new texts Russian; chips, the red tile and the state dots in their colours; no white icons on solid fills | ok (2026-10-24, Release, checked by screenshot) |
| 1.2 | light theme | the same pages | text and dots readable on light cards; the red tile fill is a soft pink, not a solid red; the download icon blue on a grey button | ok (2026-10-24, Release, checked by screenshot): Home with the red tile, Overlay, Models, Settings, About |
| 1.3 | theme "as in Windows" | switch the Windows mode light ↔ dark with the window open on Home | the card, tiles and chips follow within a second without a restart | not checked: user (Windows setting) |
| 1.4 | English interface | open every page | "All set", "Waiting for iRacing", "Assign button", "Indicator position", "Installed" / "Available to download", "They work offline.", "MIT License"; no Russian left except the language names | ok (2026-10-24, Release, checked by screenshot) |

## 2. Home

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | model installed, wheel button assigned and connected, iRacing not running | open Home | the status card is one line: grey flag, "Ждём iRacing", no button, no second line, the hint in the tooltip; tiles in one row without titles: microphone name with a green dot in the icon corner, "SIMAGIC GT Neo · кнопка 1" with a green dot and no mode, "● iRacing не запущен" with a grey dot before the text; no phrase counter, "Фраз пока нет…" | ok (2026-10-24, Release, checked by screenshot) |
| 2.2 | settings `microphone` of a device that is not there | open Home | the tile shows the name with a red dot, the tooltip says "не найден" and that the Windows default is used | ok (2026-10-24, Release, checked by screenshot): "Ghost USB Mic" with a red dot |
| 2.3 | settings `microphone: null` | open Home | the tile shows only the name of the Windows default device, no "Как в Windows", the tooltip says it is the Windows default | ok (2026-10-24, Release, checked by screenshot): "Microphone (fifine Microphone)"; reader name "… Microphone: connected / The Windows default microphone." |
| 2.4 | wheel disconnected (unknown GUID in the binding) | open Home, click the voice button tile | card "Кнопка голоса не подключена" with "Назначить"; the tile is soft red with "Назначить кнопку" and a chevron, the tooltip names the device; the tile and the card button open Settings | ok (2026-10-24, Release, checked by screenshot): the tile button's reader name names "Fanatec ClubSport Universal Hub V2 Formula Rim, кнопка 3"; invoking it opened Settings |
| 2.5 | `"binding": null` | open Home | card "Кнопка голоса не назначена" with "Назначить", the tile "Назначить кнопку" in red | ok (2026-10-24, Release, checked by screenshot, English: "Voice button is not assigned", "Assign button") |
| 2.6 | empty models folder | open Home, press the card button | card "Нет модели" with the download icon and the blue "Скачать"; the button opens Models | ok (2026-10-24, Release, checked by screenshot) |
| 2.7 | window 760 px wide | open Home | the three tiles stand one under another, each still one line and readable ("iRacing не запущен" whole); at 1000 px they are back in one row | ok after a fix (2026-10-24, Release, checked by screenshot): at 760 px the tiles were cut to "Assign…" and "iRacing i…"; they now stack below 600 px of content |
| 2.8 | any | read the tiles with UI Automation (or Narrator) | every tile's name has the state in words: "… Микрофон: подключён", "… Кнопка голоса: подключена", "iRacing не запущен. …" | ok after a fix (2026-10-24, Release, UI Automation): the name was on a panel without an automation peer and did not reach the reader; now it is on the tile text |

## 3. Recent phrases

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | no phrases yet | open Home | "Фраз пока нет. Зажмите кнопку PTT и скажите команду." and no counter anywhere | ok (2026-10-24, Release, checked by screenshot) |
| 3.2 | in the car | say "меняй шины и залей двадцать пять литров" | one line: the recognized text, a green chip "все шины · +25 л", the time "HH:mm"; the tooltip of the line shows "меняем все шины, заливаем 25 литров" and the time with seconds | not checked: user (voice) |
| 3.3 | in the car | say something that is not a command, then leave the car and say "залей тридцать литров" | a muted text with an amber chip "не понял", no "не понял, повтори" next to it and no second line, the reason only in the tooltip; then a red chip "не в машине" | not checked: user (voice) |
| 3.4 | in the car | say "меняй левые и залей десять литров и протри стекло и быстрый ремонт" | the chip "левые · +10 л · стекло · ремонт" or cut with an ellipsis, the time stays in place, the full response in the tooltip | not checked: user (voice) |

## 4. Models

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 4.1 | ru installed, en not | open Models | subtitle "Работают без интернета."; group "Установлены" with "Русский ● используется" and a grey delete icon; group "Можно скачать" with "English 39.3 МБ" and a blue download icon; no code circle, date, id or license | ok (2026-10-24, Release, checked by screenshot) |
| 4.2 | 4.1 | press the download icon of English, then let it finish | a thin bar, "N из M МБ" and a cancel icon in the row; then the row moves to "Установлены" without "используется"; the "Можно скачать" title disappears | not checked: user (network download) |
| 4.3 | 4.2 | delete English (icon, confirm) | the row moves back to "Можно скачать" | not checked: user, together with 4.2 |
| 4.4 | network off | press the download icon | the error in red in the row and the download icon again | not checked: user (network) |
| 4.5 | any | hover the icon buttons | tooltips "Скачать", "Отмена", "Удалить"; the same names for the screen reader | ok (2026-10-24, Release, UI Automation: names present) |

## 5. Settings and Overlay

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | any | open Settings | "Язык и вид" has three rows — commands language, interface language, theme; no "Режим VR" and no SteamVR hint | ok (2026-10-24, Release, checked by screenshot) |
| 5.2 | threshold 0.6 | look at "Порог уверенности", move the slider to 0.75 | a gauge icon; the label with the explanation under it, wrapping before the slider; the slider and "0.6" on the right; after the move "0.75" and the file gets 0.75 once, after the movement stops | ok (2026-10-24, Release, UI Automation): three steps 0.65 → 0.7 → 0.75, the file still 0.6 right after them, 0.75 after 800 ms |
| 5.3 | `"threshold": 0.2` in the file | open Settings | "0.2" on the right, the thumb at the left edge, the file keeps 0.2 | ok (2026-10-24, Release, checked by screenshot) |
| 5.4 | window 760 px, English | open Settings | the threshold label and the explanation wrap in their column, the slider and the number stay whole on the right | ok after a fix (2026-10-24, Release, checked by screenshot): "Confidence thresho…" was cut, the label now wraps |
| 5.5 | any | open Overlay | top card: "Показывать оверлей" and under it "Режим VR ● выключен" in one line with the switch on the right; "Размер" with the frame-with-arrows icon; "Место на экране" one row "Положение индикатора" with "Перетащите мышью, потом «Готово»", the blue "Переместить" and a grey reset icon (disabled without a moved position) with the tooltip "Сбросить положение"; no paragraph about the tray menu | ok (2026-10-24, Release, checked by screenshot) |
| 5.6 | SteamVR not running | switch "Режим VR" on, then off | "● ждёт SteamVR" appears right after the label at once, the row stays one line, `overlay.vr` is true in the file; off again | ok (2026-10-24, Release, checked by screenshot) |
| 5.7 | 5.5 | "Переместить", drag the indicator, "Готово", then the reset icon | the indicator moves and stays; the reset icon becomes active and puts it back to the top right corner | not checked: the move itself is unchanged (checked in `overlay-indicator.md`); the button state is covered by the model tests |

## 6. Own phrases and About

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 6.1 | display scale 100 % | Commands → "Настроить" | "Команды / Свои фразы": the bottoms of the letters of "Команды", "/" and "Свои фразы" on one line | ok (2026-10-24, Release, checked by screenshot, zoomed) |
| 6.2 | display scale 150 % | the same | the same, one baseline | not checked: user (Windows setting) |
| 6.3 | Russian / English | open About | "Лицензия MIT" / "MIT License" without a colon and a period | ok (2026-10-24, Release, checked by screenshot) |

## 7. Window size, DPI and CPU

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 7.1 | any | 760 px wide, every page | no horizontal scroll; long values cut with an ellipsis; the tiles stacked (2.7) | ok (2026-10-24, Release, checked by screenshot): Home, Settings, Overlay, Models, About, Own phrases |
| 7.2 | Windows display scale 150 % | every page at 1000 × 760 and at 760 px | the same layout, icons and dots crisp, nothing clipped; set 100 % back | not checked: user (Windows setting) |
| 7.3 | Release, window open on Home, iRacing not running | watch the CPU for 30 s | below 1 % | ok (2026-10-24, Release): 0.05 % of 16 cores over 30 s, working set 408 MB |
