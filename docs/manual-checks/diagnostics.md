# Manual check: logs, crash reports and the diagnostics archive

The log of the app and `run`, crash reports with the offer on the home page, "Collect diagnostics" from the window and
the CLI, and that no phrase leaves a trace on disk. Items that need the game use iRacing **Test Drive**.

Build once from the repository root in PowerShell (close PitVoice in the tray first, the build replaces its files):

```
dotnet build PitVoice.sln -c Release -nodeReuse:false
Set-Alias pitvoice "$PWD\src\PitVoice.App\bin\Release\net10.0-windows\PitVoice.exe"
Set-Alias pitvoice-cli "$PWD\tools\PitVoice.Cli\bin\Release\net10.0\pitvoice-cli.exe"
$logs = "$env:LOCALAPPDATA\PitVoice\logs"
```

The checks use the real profile: the log, reports and archives go to `%LocalAppData%\PitVoice\logs` and
`%LocalAppData%\PitVoice\diagnostics`. Mark every item **ok**, **mismatch** or **not checked** (with the reason) and
write what was seen.

## 1. The log of `run` and of the other commands

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | the app is not running | `pitvoice-cli run`, say two phrases (one applied or "not in the car", one not understood), Ctrl+C | the console shows the phrases as before | ok (2026-10-16): two phrases, an empty one and «сорок четыре восемьдесят пять» (not understood), both shown with words and the rejection reason |
| 1.2 | 1.1 | `Get-Content "$logs\pitvoice-$(Get-Date -Format yyyyMMdd).log" -Tail 30` | lines "PitVoice … started on …", "Settings at start: …", "Language pack … loaded", "Model … loaded in … ms", "PTT source started …", "Host stopping", "Host stopped"; no words of the phrases, no line per phrase | ok: start with version 0.0.11 and OS, settings path, settings at start, pack ru, additions applied (0 errors), PTT device connected, PTT source started, model loaded in 435 ms, Host stopping/stopped; not a line between the model and the stop for the two phrases |
| 1.3 | 1.2 | `pitvoice-cli parse --lang ru "меняй шины"`, then the same `Get-Content` | ok: the log stayed 2090 bytes, still one file |

## 2. Crash in `run`

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | the app is not running | `$env:PITVOICE_TEST_CRASH = "background"; pitvoice-cli run; "exit code $LASTEXITCODE"; Remove-Item env:PITVOICE_TEST_CRASH` | about 5 s after "Ready" the process ends with a non-zero code and prints "crash report: …\crash-<time>.log" | ok (2026-10-16): ended about 5 s after "Ready" with code -532462766 (0xE0434352), stderr "PitVoice crashed; crash report: …\logs\crash-20261016-204025-090.log" |
| 2.2 | 2.1 | open the printed report | time, version, os, source "thread … (PitVoice test crash)", `fatal: true`, the exception with its stack | ok: source "thread 13 (PitVoice test crash)", fatal: true, InvalidOperationException with the stack; the log has the WRN "Test crash … in 5 s" and the FTL record with the report name |
| 2.3 | 2.1 | `$env:PITVOICE_TEST_CRASH = "foo"; pitvoice-cli run`, wait 10 s, Ctrl+C, `Remove-Item env:PITVOICE_TEST_CRASH` | works as usual; the log has a warning that `foo` is unknown | ok: still running after 9 s, "[WRN] PITVOICE_TEST_CRASH=foo is unknown and ignored; use ui, background or task", no new crash report (stopped from the task manager instead of Ctrl+C) |

## 3. Crash in the app and the offer after it

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | 2.1 done, the app is not running | `pitvoice` | the home page shows "PitVoice crashed last time" with "Collect diagnostics" and "Close" (the crash of `run` is offered too) | mismatch, fixed (2026-10-16): the block showed after the `run` crash of 2.1, but without buttons (WPF-UI InfoBar does not show its content); the buttons moved to a row under the block, rechecked ok |
| 3.2 | 3.1 | "Close", then "Exit" in the tray menu and `pitvoice` again | no block | ok: "Close" hid the block, `diagnosticsash-seen` holds crash-20261016-204025-090.log; see the next start in the result of this row |
| 3.3 | the app is not running | `$env:PITVOICE_TEST_CRASH = "ui"; pitvoice` (from the same window), wait 10 s | the process ends, the icon disappears from the tray at once (not only after the mouse passes), a new `crash-*.log` with source "window thread" and `fatal: true` | partly (2026-10-16): the process ended 5 s after start, crash-20261016-204432-346.log with source "window thread", fatal: true, the log has "Test crash (ui) in 5 s" and the FTL record; the tray icon was not watched; rechecked with crash-20261016-205117-135.log: the icon was gone from the tray at once |
| 3.4 | 3.3 | `Remove-Item env:PITVOICE_TEST_CRASH; pitvoice` | the app starts normally (no "already running"); the block is on the home page | ok: normal start, the block with both buttons |
| 3.5 | 3.4, the wheel and the headset connected | "Collect diagnostics" in the block | the block disappears; Explorer opens `%LocalAppData%\PitVoice\diagnostics` with the new archive selected | ok: the block disappeared, Explorer showed pitvoice-diagnostics-20261016-204448.zip selected |
| 3.6 | 3.5 | open the archive | `summary.txt`, `logs/` with the log files and both crash reports, `settings/settings.json`, `languages/<code>.yaml` if you have own phrases; the summary lists the wheel among the game devices and the headset among microphones and output devices; no `C:\Users\<name>` anywhere, `%USERPROFILE%` instead | ok: summary.txt, logs/pitvoice-20261016.log, both crash reports, settings/settings.json, languages/ru.yaml; game devices SIMAGIC P1000, GT Neo, Alpha Ultimate with GUIDs; microphones and outputs with the default marked (USB HIFI AUDIO headset among both); no C:SERSARTEM IN ANY FILE, %USERPROFILE% 22 TIMES |
| 3.7 | the app is not running | `$env:PITVOICE_TEST_CRASH = "task"; pitvoice`, wait 10 s, `Remove-Item env:PITVOICE_TEST_CRASH` | the app keeps working; a new `crash-*.log` with `fatal: false`; after "Exit" and a new start there is no block | ok (2026-10-16): still running after 12 s, crash-20261016-205200-420.log with source "unobserved task", fatal: false, ERR in the log (not FTL). With the mark at 205200, a second task crash (205431, fatal: false, newer than the mark) and a new start: no block. On the way: the block of the earlier ui crash was shown until "Close" was pressed in it — "Exit" alone keeps it, as specified |

## 4. Collect from the settings and the CLI

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 4.1 | the app is running | "Settings" → "Diagnostics" | the note says what the log holds and that phrases are not in it; "Collect diagnostics" and "Open logs folder" | ok (2026-10-16): the note and both buttons at the bottom of "Settings"; under them the result of the collect from the home page ("Archive ready: pitvoice-diagnostics-20261016-204448.zip") |
| 4.2 | 4.1 | "Open logs folder" | Explorer opens `%LocalAppData%\PitVoice\logs` | ok (2026-10-16): Explorer in …\AppData\Local\PitVoice\logs with the log and two crash reports |
| 4.3 | 4.1, PTT in hold mode | hold the PTT, say a phrase and press "Collect diagnostics" while the phrase is processed | the result cue sounds as usual; under the button "Archive ready: <name>", Explorer shows it | ok: the start cue and the error cue (iRacing not running) sounded as usual; Explorer showed pitvoice-diagnostics-20261016-204715.zip selected; no line about the phrase in the log |
| 4.4 | the app is running | `pitvoice-cli diagnostics collect` | prints the path of a new archive, no folder opens; the archive has the current log of the running app; the summary says "host: the host is not running in this process" | ok: printed …pitvoice-diagnostics-20261016-204745.zip, no folder opened; the archive has the log the running app holds open (98 lines) and both crash reports |
| 4.5 | 4.4 | collect three more times, count `pitvoice-diagnostics-*.zip` | 5 archives, the oldest removed | ok: 5 archives (204715 … 204749), 203629 and 204448 removed |

## 5. No phrase on disk

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | the app is running, iRacing Test Drive in the car | say several commands: some applied, one not understood, one after "Exit car" | the log has "iRacing: in the car" and similar lines, and not a single line about a phrase | ok (2026-10-16): after the start only "iRacing: running, not in the car" (20:56:33) and "iRacing: in the car" (20:57:13); no line about the phrases, none of their words in the whole log. The log has no return to "not in the car", so the phrase after "Exit car" was said while still in the car or not said |
| 5.2 | 5.1 | "Check": type «залей двадцать литров», then check it by microphone | no new lines in the log | ok: no new lines; the setup check was already done, so no settings record either |
| 5.3 | 5.2 | `Get-ChildItem "$env:LOCALAPPDATA\PitVoice","$env:APPDATA\PitVoice" -Recurse -Include *.wav,*.mp3,*.ogg,*.flac,*.raw` | nothing found | ok: no audio files there, nor in spike\c1 (the settings, models and additions used for the check) |
