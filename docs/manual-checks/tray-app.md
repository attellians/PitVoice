# Manual check: the tray app

Scenario С1 (first setup) on a clean profile, the tray icon and its states, one instance and one pipeline host,
language switch and idle CPU. Items that need the game use iRacing **Test Drive**.

Build once from the repository root in PowerShell:

```
dotnet build PitVoice.sln -c Release
Set-Alias pitvoice "$PWD\src\PitVoice.App\bin\Release\net10.0-windows\PitVoice.exe"
Set-Alias pitvoice-cli "$PWD\tools\PitVoice.Cli\bin\Release\net10.0\pitvoice-cli.exe"
```

**Clean profile.** Point settings, models and own phrases at empty folders so your real ones stay untouched (`spike/` is
ignored by git). Start the app from the same PowerShell window, so it inherits the variables:

```
$env:PITVOICE_SETTINGS = "$PWD\spike\c1\settings.json"
$env:PITVOICE_MODELS_DIR = "$PWD\spike\c1\models"
$env:PITVOICE_USER_LANGUAGES = "$PWD\spike\c1\languages"
```

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. A mismatch becomes a
fix with a test, a change to `design.md` or a new task. PitVoice and iRacing run **without** administrator rights unless
an item says otherwise.

## 1. С1: first setup on a clean profile

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | clean profile, no folders yet | `pitvoice` | the icon appears in the notification area with the orange "no model" mark; the window opens; the interface language is the Windows language (ru for Russian Windows, else en) | ok (2026-10-15): icon with the orange mark, window opened, English (Windows UI language detects as en) |
| 1.2 | 1.1 | look at "Home" | "What is left to set up" lists three items: model, button, check; state "no model" with the model id | ok: three items; "No model — speech model vosk-model-small-en-us-0.15 is not downloaded" |
| 1.3 | 1.2 | click the model item | "Models" opens with the models of the language, size in MB, license Apache-2.0, "not downloaded" | ok: vosk-model-small-en-us-0.15, 39.3 MB, license Apache-2.0, not downloaded |
| 1.4 | 1.3 | "Download" | progress in % and MB moves, the window stays responsive; at the end the model is "in use" and the icon leaves "no model" without a restart | ok: progress moved, window responsive, "in use" at the end. First run: the app crashed on the next icon change (H.NotifyIcon disposes the previous Icon, cached icons were reused) — fixed (a fresh Icon per change), rechecked ok |
| 1.5 | download running | close the window with ×, wait, open it from the icon | the download continued, the current progress is shown | ok: after the fix, download continued while the window was closed, progress shown on reopening; delete with confirmation gave "no model" |
| 1.6 | network cable out / Wi-Fi off, model of the other language | "Download" | "no connection" error, "Download" is back, no `*.zip.part` and no model folder left in `spike\c1\models` | not checked: wired Ethernet, the user chose not to unplug it; covered by test Download_without_connection_shows_the_error_and_leaves_nothing |
| 1.7 | 1.4 done | "Home" → button item → "Assign button", press a wheel button | the device name and the button number shown, the item disappears from the checklist, `settings.json` has the binding | ok (2026-10-15): "Assigned: SIMAGIC GT Neo, button 1", saved |
| 1.8 | 1.7 | "Assign button", wait 15 s | "No button was pressed…", the old binding stays | ok: "No button was pressed: the button is not assigned, the old one stays." |
| 1.9 | 1.7 | "Assign button", press the current PTT button | no start cue, no recording; the button stays assigned | ok: no start cue, the button stayed |
| 1.10 | 1.7 | "Check": type «меняй шины и залей двадцать пять литров» (en: "change all tires and add twenty five liters"), "Check" | commands "All tires" and "Add fuel 25 liters", confidence, response; the checklist block is gone; nothing changes in the iRacing black box | ok (en): All tires, Add fuel 25 liters, confidence 1.00, "changing all tires, adding 25 liters" |
| 1.11 | 1.10 | "Check": «какая сегодня погода» | rejected, response «не понял, повтори» | ok (en): "Rejected: no command matched", response "sorry, say again" |
| 1.12 | 1.10 | empty field | "Check" is disabled | ok |
| 1.13 | model loaded | "Check with the microphone", say «залей тридцать литров», click again | the text with a confidence per word and the command "Add fuel 30" | ok after two fixes: (1) the button was disabled while recording (async command blocked a second click), so it waited 8 s; (2) the UI froze 1–2 s on Stop (the device was closed on the UI thread). Result: "add thirty litres", add 1.00 thirty 1.00 litres 0.63, Add fuel 30 liters |
| 1.14 | 1.13 | start the microphone check and stay silent until it stops by itself (8 s) | "(no speech)" | ok |
| 1.15 | 1.13 | start the microphone check, press the PTT button | the check stops without a result ("PTT was pressed"), the PTT phrase is processed as usual | ok |
| 1.16 | everything set, app restarted | `pitvoice` | the checklist block is not shown | not checked explicitly; covered by test Setup_complete_stays_complete_after_a_restart |

## 2. Settings and live apply

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | in the car in Test Drive | volume 40%, "Listen" | three cues, quieter; `settings.json` has `"cueVolume": 40` only after the slider stopped | ok (2026-10-15) |
| 2.2 | — | volume 0%, "Listen" | "The cues are off" | ok |
| 2.3 | headphones connected and not the default | choose the headphones for the cues, "Listen", unplug them, open the list | cues in the headphones; after unplugging the list marks them "not connected" and a PTT phrase plays its cues on the default device | not checked: no second real output device (as in stage 4) |
| 2.4 | window open | plug in a USB microphone, open the microphone list | the new microphone is in the list without a restart | not checked: no second microphone |
| 2.5 | in the car | choose another microphone, say a phrase with the PTT | the phrase is recorded from the new microphone (see "Home": the phrase is understood; or speak into the old one and see it fail) | ok: with Steam Streaming Microphone the phrase was "no speech"; back on the system default phrases work |
| 2.6 | hold mode, in the car | hold the PTT and switch the mode to "Toggle" while holding, release | the recording stops on the release (old mode); the next press works as toggle | ok |
| 2.7 | — | threshold 1.5, Enter | "Enter a number from 0 to 1", the file keeps the old threshold | ok: the field keeps 1.5 with "Enter a number from 0 to 1", the file keeps 0.6 |
| 2.8 | `settings.json` read-only (Properties → Read-only) | unit → gallons, then «залей десять» on "Check" | "Settings were not saved…"; the check shows gallons | not checked: the user chose to skip it; covered by test Read_only_settings_file_reports_the_error_and_the_change_still_applies |
| 2.9 | ru and en models installed, language ru | language → English | the window switches to English at once; a few seconds later «change all tires» works with the PTT; during loading a PTT press gives the error cue and "Home" shows "speech model is loading" | not checked: the user skipped it; covered by test Press_while_the_model_loads_gets_the_error_cue_with_model_loading |
| 2.10 | en model deleted, language ru | language → English | icon "no model"; Settings offers "Download" next to the language | not checked: the user skipped it; covered by test Switch_to_a_language_without_model_gives_no_model_and_the_error_cue |
| 2.11 | language of the commands ru | interface language → English | the window and the tray menu switch to English at once; «залей тридцать литров» still works with the PTT | ok (2026-10-15) |
| 2.12 | 2.11 | interface language → "Same as the commands" | the window is Russian again | ok |
| 2.13 | Windows theme light (or dark) | theme → Dark, then Light, then "As in Windows"; restart the app with Dark chosen | the window changes at once each time; Dark stays after the restart; "As in Windows" follows a change of the Windows theme | ok (first build crashed at start: WPF-UI refuses to unwatch a window that is not loaded yet — fixed) |
| 2.14 | — | "Models" | models of both languages with the language name, size and license; the one of the commands language is "in use" | ok. Found meanwhile: the mouse wheel did not scroll the pages (a page ScrollViewer nested in the one of NavigationView swallowed the wheel) — the page ScrollViewer removed; rechecked ok |

## 3. Tray, states, one instance

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | everything set, iRacing in the car | hover the icon | no mark, tooltip «PitVoice: готово» / "PitVoice: ready" | ok (2026-10-16): no mark, ready |
| 3.2 | 3.1 | close iRacing | within 5 s grey mark, tooltip "no connection to iRacing" | ok |
| 3.3 | PTT on the wheel | unplug the rim, wait 5 s; plug it back | red mark, tooltip names the device; after plugging back the previous state without any action | mismatch, then not rechecked: after "Assign button" the rim reconnect was not seen (the error stayed); without an assignment in the same run it recovered (ok). Cause: SDL was shut down and started again inside one process. Fixed (one SDL instance per process, see design); the recheck after an assignment was skipped by the user as an unusual case |
| 3.4 | window open, PTT assigned, in the car | close the window with × | the icon stays; a PTT phrase is still recorded and executed | not checked: the user skipped it |
| 3.5 | 3.4 | double click the icon; then "Open" in its menu | the window shows on top of other windows | not checked: the user skipped it |
| 3.6 | window closed | say three phrases, open the window | all three on "Home" with time, text, outcome and response | not checked: the user skipped it |
| 3.7 | app in the tray | start `pitvoice` again | the window of the first instance opens, no second icon, the second process ends | ok (2026-10-16) |
| 3.8 | app in the tray | `pitvoice-cli run` | exit code ≠ 0, "PitVoice is already running…"; `pitvoice-cli parse --lang ru "залей тридцать"` works | ok (2026-10-16): `run` exits with "PitVoice is already running…"; `parse --lang ru "залей тридцать"` gives fuel.add amount=30 while the app runs |
| 3.9 | app closed, `pitvoice-cli run` running | `pitvoice` | message that the console host is running, no icon | ok: message "The PitVoice console host (pitvoice-cli run) is running…" (Windows language en), no icon |
| 3.10 | a phrase is being executed | "Exit" in the icon menu | the app ends within 3 s, the icon disappears; commands already sent stay | not checked: the user skipped it; stop within 3 s is bounded in code (AppServices.StopTimeout) |
| 3.11 | download running | "Exit" | the app ends, no partial files in the models folder | not checked; covered by test Stopping_the_host_cancels_downloads_and_removes_partial_files |
| 3.12 | app killed in Task Manager | `pitvoice` | starts normally | ok |
| 3.13 | iRacing run as administrator, PitVoice not | open the window | warning about administrator rights on "Home" | not checked: not reproducible, the sim runs non-elevated (as in stages 3 and 4) |

## 4. Autostart

Removed from the stage by the user (2026-10-16): PitVoice is started by hand, nothing is written to `Run`.

## 5. Commands and own phrases

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | language ru | "Commands" | groups Fuel, Tires, …; "Add fuel" has «залей ‹число› ‹единица›» | ok (2026-10-16): groups Топливо, Шины…; «залей ‹число› ‹единица›» |
| 5.2 | 5.1 | language → English | English phrases, e.g. "add ‹number› ‹unit›" | ok (checked as 2.11 with the interface in English) |
| 5.3 | **replaced by phrase-builder.md 1.1–1.3 (2026-10-23)** no additions file | "Open file" | `spike\c1\languages\ru.yaml` is created with a format comment and opens in the editor | ok: ru.yaml created with the format comment and opened |
| 5.4 | **replaced by phrase-builder.md 3.1–3.3 (2026-10-23)** 5.3 | add `language: ru` and `intents: { fuel.add: { patterns: ["плесни {amount} {unit?}"] } }`, save, "Reload" | «плесни ‹число› ‹единица›» marked "own"; «плесни тридцать литров» on "Check" gives Add fuel 30 | ok: «плесни ‹число› ‹единица›» marked own; «плесни тридцать литров» understood |
| 5.5 | **replaced by phrase-builder.md 5.3 (2026-10-23)** 5.4 | add `tires.flip:` with a pattern, "Reload" | the error names the file, the line and `tires.flip`; built-in phrases are shown and work | ok: "…\ru.yaml:6: error: intents/tires.flip/patterns: unknown intent 'tires.flip'…", built-in phrases shown |

## 6. About, icon, idle CPU

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 6.1 | — | "About" | version from `VERSION`, the license line, the GitHub link, Vosk models with Apache-2.0, components with licenses | ok: PitVoice 0.0.10, license "not chosen yet", GitHub link, both Vosk models Apache-2.0, nine components |
| 6.2 | — | click the GitHub link | opens in the default browser | ok |
| 6.3 | — | look at the tray, the taskbar and `PitVoice.exe` in Explorer (large icons) | the PitVoice sign everywhere, sharp at every size | ok: the sign in the tray and the taskbar |
| 6.4 | PTT on the wheel, window closed, game not running | Task Manager → Details, add the "CPU time" column; note it, wait 60 s, note again | under 1% of a core above what the PTT device itself costs (stage 4: base about 0.65% with the rim bound) | ok: 0.81% of one core over 60 s with the window in the tray, PTT on the GT Neo rim (the rim alone ~0.4%), game not running; working set 390 MB (TZ §12: ≤ 450 MB) |

## 7. Console host and CLI after the move to PitVoice.Host

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 7.1 | app closed, PTT assigned, in the car | `pitvoice-cli run` | language, PTT, devices and game state printed as before; a phrase with the PTT is executed | ok (2026-10-16): output as in stage 4; phrases executed through the tray app in the same session |
| 7.2 | — | `pitvoice-cli ptt bind`, press a wheel button | works as in stage 4: device and button printed, saved | ok |
