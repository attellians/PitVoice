# Manual check: push-to-talk, cues and the pipeline

Run with iRacing **Test Drive** where an item needs the game; the rest works without it.

Build the CLI once and use the executable from the build output. From the repository root in PowerShell:

```
dotnet build tools/PitVoice.Cli -c Release
Set-Alias pitvoice-cli "$PWD\tools\PitVoice.Cli\bin\Release\net10.0\pitvoice-cli.exe"
```

The alias lives only in that PowerShell window. Use a separate settings file for the check so your own settings stay
untouched: `$env:PITVOICE_SETTINGS = "$PWD\spike\settings-check.json"` (`spike/` is ignored by git). Keep a telemetry
recording running in a second terminal while items touch the game (`pitvoice-cli record --out spike/ptt-check-<date>.jsonl`).

`run` prints one line per pipeline event with the phrase id (`#3`). The latency of the result cue is printed as
`cue <name> <N> ms after the recording ended`; items 8.x collect it.

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. A mismatch becomes a
fix with a test, a change to `design.md` or a new task.

Both PitVoice and iRacing run **without** administrator rights unless an item says otherwise.

## 1. Devices and cues

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | wheel, speakers and headphones connected | `devices` | microphones, output devices and game devices listed; the default microphone and output device are marked | ok (2026-10-15): 3 microphones, 3 outputs, SIMAGIC P1000, GT Neo, Alpha Ultimate with guids; defaults marked |
| 1.2 | — | `cues play` | start, success, error play in this order; device and volume printed; the three are easy to tell apart by ear | ok: start 110 ms, success 285 ms, error 380 ms on 'Speakers (USB HIFI AUDIO)'; told apart by ear during the game run |
| 1.3 | — | `cues play beep` | exit code ≠ 0, the valid names `start`, `success`, `error`, `all` are listed | ok: exit 1, `Must be one of: start, success, error, all` |
| 1.4 | — | `cues play all --volume 30` | same cues, clearly quieter | not checked: skipped in this session; volume scaling is covered by unit test Volume_ScalesSamples |
| 1.5 | — | `cues play all --volume 0` | nothing plays, "muted" printed, exit code 0 | ok: `muted`, exit 0 |
| 1.6 | headphones are not the system default | `cues play all --device "<headphones name from devices>"` | cues in the headphones, not in the speakers | not checked: no second real output device (only USB HIFI speakers and Steam virtual devices); device choice by name seen in 1.7 |
| 1.7 | — | `cues play start --device "Nope"` | warning naming `Nope`, cue on the system default device | ok: `output device 'Nope Speakers' not found, using the default 'Speakers (USB HIFI AUDIO)'` |
| 1.8 | `"cueVolume": 150` in the settings file | `cues play start` | warning "out of 0..100", played at 100% | not checked: skipped in this session; clamping with a warning is covered by unit test OutOfRangeVolume_IsClamped_WithWarning |

## 2. Assigning the PTT

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | no settings file | `ptt bind`, press a wheel rim button | device name and button number printed, `saved to …`; the file has `kind: joystick`, the device `guid` and the button | ok (2026-10-15): `PTT: SIMAGIC GT Neo, button 1`, file written with guid `0300bee1…` and `button: 1` |
| 2.2 | 2.1 done | `ptt bind`, press a key (e.g. F13 or a key not used by the game) | key name printed, the file has `kind: keyboard` and `vk` | ok: left Shift → `PTT: key Shift`, file has `kind: keyboard`, `vk: 160` (VK_LSHIFT) |
| 2.3 | PTT assigned to a key | `ptt bind`, wait 15 s | "No button pressed: PTT not assigned", the old key stays in the file | ok (with the rim button as the old binding): `No button pressed: PTT not assigned. It stays: SIMAGIC GT Neo, button 1`, file unchanged |
| 2.4 | PTT assigned | `ptt bind`, press Esc | "Cancelled", the binding in the file does not change | ok: `Cancelled, PTT unchanged: SIMAGIC GT Neo, button 1` |
| 2.5 | Notepad has the focus | `ptt watch`, type in Notepad | every key shows as down/up in `ptt watch` **and** the characters appear in Notepad | ok (2026-10-15): keys seen in `ptt watch`, characters typed in Notepad |
| 2.6 | another window has the focus | `ptt watch`, press wheel buttons | presses show with the device name, button number and guid | ok (2026-10-15): `down`/`up` of `SIMAGIC GT Neo, button 1` with guid while another window had the focus |
| 2.7 | `ptt watch` running | unplug the wheel rim / base, plug it back | `disconnected` and `connected` lines with the device name | not checked in `ptt watch`; the same SDL connect and disconnect events were seen in `run` (6.1, 6.2) |

## 3. Host start and stop

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | `--models-dir` pointing to an empty folder | `run --models-dir spike/no-models` | exit code ≠ 0, the `models download <id>` command to run | ok (2026-10-15): `model not installed`, hint printed, exit 1 |
| 3.2 | no PTT assigned | `run` | language, PTT `not assigned`, devices, game state; hint `ptt bind`; keeps running | ok (2026-10-15) |
| 3.3 | 3.2 running | Ctrl+C | `Stopped.`, exit code 0 | ok (2026-10-15): `Stopped.` printed |
| 3.4 | PTT on a wheel button, wheel connected | `run` | `PTT device connected: <name>` at start, `Ready.` | ok (2026-10-15) |
| 3.5 | PTT on a wheel button, wheel unplugged before start | `run` | `PTT device '<name>' is not connected; waiting for it.`; plugging it in prints `PTT device connected` and the button works | not checked: skipped in this session; reconnect while running works (6.1, 6.2) |
| 3.6 | iRacing run as administrator, PitVoice not | `run` | warning that keys in the game window are hidden from the hook and pit commands are blocked | not checked: not reproducible, the sim runs non-elevated even when the iRacing UI is started as administrator (same as stage 3 check 6.2) |

## 4. Hold mode (default)

`run` with PTT on a wheel button, iRacing Test Drive, driver in the car.

| # | Action | Expected | Result |
|---|---|---|---|
| 4.1 | hold, say «залей тридцать литров», release | start cue on press; success cue; 30 l in the black box; response in the console | ok (2026-10-15): «меняй шины залей двадцать пять литров» → LF, RF, LR, RR, Fuel(25), success cue 262 ms after the end of the recording; «залей десять литров» → Fuel(10), 201 ms |
| 4.2 | tap the button (< 250 ms) | start cue, then `released: too short, ignored`; no recognition, no result cue | ok: `released: too short, ignored`, no recognition |
| 4.3 | hold 12 s while speaking | recording stops on release, `audio: 8.00 s (cut to 8 s)` | ok (left Shift): held 13 s, stopped only on release, `audio: 7.86 s (cut to 8 s)` |
| 4.4 | hold 1 s without speaking | error cue, response `not_understood`, nothing sent | ok: empty recognition, `rejected: Empty`, error cue |
| 4.5 | PTT on a keyboard key, iRacing has the focus | hold the key, speak, release | the phrase is processed like 4.1 | ok: left Shift with iRacing focused, «залей пятнадцать литров» → Fuel(15), success cue 231 ms |
| 4.6 | hold the key 3 s (auto-repeat) | one `recording...`, one stop on release | ok: held 2.6 s, one recording and one stop, auto-repeat ignored |

## 5. Toggle mode

Set `"mode": "toggle"` in the settings file and restart `run`.

| # | Action | Expected | Result |
|---|---|---|---|
| 5.1 | press, say «меняй шины», press again after ~2 s | start cue; `stopped: second press`; success cue | ok: `stopped: second press`, tires applied, success cue 199 ms |
| 5.2 | press, release quickly | recording keeps going (release does nothing) | ok: no release ever stopped a recording in toggle mode |
| 5.3 | press twice within 300 ms (double tap), speak, press again | the double tap does not stop it; the later press stops it | not reproduced: a deliberate double tap by hand took 0.58 s, so the second press correctly stopped the recording; the 300 ms guard is for contact bounce. Covered by unit tests (Toggle_BounceWithinDebounce_IsIgnored) |
| 5.4 | press, say a phrase, wait | `stopped: 8 s without a second press` 8 s after the press; result cue | ok: `stopped: 8 s without a second press` exactly 8.0 s after the press, 7.83 s of audio recognized, success cue 279 ms |
| 5.5 | right after 5.4's auto stop, press again | a new recording starts (start cue) | ok: the next press started a new recording, stopped by a second press |

## 6. Device reconnect

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 6.1 | `run`, PTT on the rim | unplug the rim | `PTT device disconnected: <name>`, host keeps running | ok: `PTT device disconnected: SIMAGIC GT Neo; waiting for it to come back`, host kept running |
| 6.2 | 6.1 | plug the rim back, press PTT | `PTT device connected`, the button records again without restarting | ok: `PTT device connected` 6 s later, the next phrase recorded and applied without restart |
| 6.3 | hold mode, recording running | unplug the rim while holding | `stopped: PTT device disconnected`, the recording goes to recognition | ok: the recording stopped and went to recognition; the reason shown is `button released`, because SDL reports the held button as released just before the device removal |
| 6.4 | reboot or move the wheel to another USB port | `run`, press PTT | works without `ptt bind` | not checked: needs a reboot or a different USB port; device order does not matter by design (binding by model GUID, unit test Binding_MatchesGuidCaseInsensitively_RegardlessOfDeviceOrder) |

## 7. Main scenarios S2–S5 and pressing during processing

Driver in the car in Test Drive unless said otherwise.

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 7.1 | — | «меняй шины и залей двадцать пять литров» | 4 tires and 25 l; success cue; «меняем все шины, заливаем 25 литров» | ok (see 4.1) |
| 7.2 | iRacing not started | «залей тридцать литров» | error cue; response `not_connected`; nothing sent | ok (accepted): not seen in this run's output; the not_connected path is covered by unit test GameNotRunning_NothingSent_ErrorCue and by stage 3 check 1.2 (`send` without the game) |
| 7.3 | in the garage | «меняй шины» | error cue; response `not_in_car` | ok: `outcome: not in the car`, response «ты не в машине», error cue, nothing sent |
| 7.4 | — | «отмена» | success cue, nothing sent | ok: sent nothing, success cue 40 ms after the recording |
| 7.5 | — | an unrelated phrase («спой песню») | error cue; `rejected` in the console | ok: «спой песню» has no words in the grammar → empty recognition, `rejected: Empty`, error cue |
| 7.6 | — | say a phrase and press PTT again right after releasing (while it is recognized) | the first phrase reaches its outcome in the game; its cue does **not** play; only the start cue of the new press | not checked: not reproducible by hand, a phrase is processed in ~170 ms, faster than a second press; covered by unit tests (VoicePipelineTests: PressDuringRecognition…) |
| 7.7 | — | «меняй шины», then press PTT while the success cue plays | the cue is cut, start cue plays; tires stay selected | partly: the press came 35 ms after the 285 ms cue ended, so nothing was left to cut; tires stayed selected. Cut covered by unit tests (PressWhileResultCuePlays…) |
| 7.8 | — | two phrases quickly one after another | both executed in order; only the second result cue plays; each has its own outcome line | ok: «меняй шины» and «залей двадцать литров» pressed 0.3 s apart, both applied in order with their own outcome |
| 7.9 | cues from the **speakers** next to the microphone | press and say «залей тридцать литров» right after the start cue | recognized without extra words at the beginning | ok: 34 phrases with cues from the speakers, no extra words at the start of any recognition |
| 7.10 | cues in **headphones** | same as 7.9 | same | not checked: no headphones as a separate output device (see 1.6) |

## 8. Latency: end of recording → result cue

Ten phrases in Test Drive, driver in the car, hold mode. Copy the `cue … ms after the recording ended` number. Target: at
most 1000 ms for a phrase of about 2 s confirmed by telemetry within 300 ms (TZ §7); more is noted, not an error.

| # | Phrase | Outcome | Latency, ms |
|---|---|---|---|
| 8.1 | «меняй шины залей двадцать пять литров» | applied | 262 |
| 8.2 | «залей десять литров» | applied | 201 |
| 8.3 | «меняй переднее левое» | applied | 209 |
| 8.4 | «не меняем резину» | applied | 184 |
| 8.5 | «меняем левую сторону» | applied | 194 |
| 8.6 | «залей пятнадцать литров» | applied | 201 |
| 8.7 | «залей тридцать два литра» | applied | 200 |
| 8.8 | «не протирать стекло» | applied | 191 |
| 8.9 | «заправлять ноль литров» | applied (ClearFuel) | 197 |
| 8.10 | «меняем правую сторону» | applied | 174 |

Result (2026-10-15, Test Drive, driver in the car, mic fifine, cues from the speakers): 173–262 ms for applied phrases over 17 phrases, 29–71 ms for rejected ones; target met. No false execution in 34 phrases. Rejections came from the language pack, not the pipeline: «переднее/заднее левое/правое» often below the 0.6 threshold (0.43–0.59; the known левое/левые confusion, stage 7a), and phrases outside the templates («меняем правое переднее», «меняем заднее», «меняем правое», «все колесо», «очисти»).

## 9. Robustness and idle

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 9.1 | `run` with PTT on a keyboard key | leave it running 30 min (with or without the game), then use the key | the key still records (Windows did not remove the hook) | not checked: no 30 min run in this session; the hook stayed alive for the whole ~20 min session (2.5, 4.5) |
| 9.2 | `run` with PTT on a wheel button, no game | 30 min, then press | still records | not checked: no 30 min run in this session |
| 9.3 | `run`, no presses, no game | CPU of `pitvoice-cli` over 60 s (`(Get-Process pitvoice-cli).TotalProcessorTime` before and after) | below 1% of one core | ok (2026-10-15): PTT assigned with `ptt bind` to SIMAGIC GT Neo rim button 1, 60 s: 0.65%; without PTT 0.00%. `run` opens only the bound device. Binding to the Alpha Ultimate base itself would cost ~1.5% (the base sends ~1000 reports a second); rim buttons come from the GT Neo device, so this does not happen in practice |
| 9.4 | `run`, microphone unplugged | press and release PTT | `error at capture`, error cue; after plugging it back the next press works | ok (accepted): with fifine unplugged Windows made the USB HIFI microphone the default and `run` recorded from it (microphone not set = system default); the "no microphone at all" case is not reproducible on this machine (covered by unit test MicrophoneMissing_CaptureFailure_ErrorCue). After plugging fifine back the next recordings came from it |

## 10. Microphone (deferred from the speech recognition stage, task 8.3)

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 10.1 | a microphone whose mix format is not 16 kHz (e.g. 48 kHz stereo) | `listen --lang ru`, say a phrase | recognized as usual | not checked as a separate step; every run used the fifine and USB HIFI microphones through WASAPI shared mode in their own mix format and was recognized |
| 10.2 | `listen` recording | unplug the microphone during the recording | the audio up to that moment is kept, an error is printed, `listen` keeps working | not checked: skipped in this session |
| 10.3 | `listen` | record 12 s | `recording cut to 8 s` | not checked with `listen`; the same capture code cut a 13 s recording to 8 s in `run` (4.3) |
| 10.4 | `"microphone": "Nope"` in the settings file | `run`, press PTT | `warning (capture)` naming the device, the default microphone is used | not checked: skipped in this session |
