# Acceptance: MVP v0.1 (TZ §12)

The non-functional requirements of TZ §12 checked on one build before the release v0.1.0: idle CPU, memory, latency,
reliability, rights, installer size and accuracy. Items that need the game use iRacing **Test Drive**.

Build once from the repository root in PowerShell (close PitVoice in the tray first, the build replaces its files):

```
dotnet build PitVoice.sln -c Release -nodeReuse:false
Set-Alias pitvoice "$PWD\src\PitVoice.App\bin\Release\net10.0-windows\PitVoice.exe"
Set-Alias pitvoice-cli "$PWD\tools\PitVoice.Cli\bin\Release\net10.0\pitvoice-cli.exe"
```

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. Earlier measurements
are listed for comparison; the acceptance counts only the build named in the result.

## 1. Accuracy

TZ §12: at least 50 recorded commands per language with the game sound in the headphones; at least 90% correct and at
most 1% false (a different command executed). A rejection ("not understood") is not false.

| # | Action | Expected | Result |
|---|---|---|---|
| 1.1 | `dotnet test tests/PitVoice.Speech.Tests --filter SampleSetTests` (ru set of 2026-10-18, see `Samples/README.md`) | ≥ 50 takes, ≥ 90% correct, 0 false | ok (2026-10-18, 0.0.34): 65 takes, correct 62 (95.4%), rejected 3 (019, 027, 050), false 0; median recognition 50 ms |
| 1.2 | the same, en set | ≥ 50 takes, ≥ 90% correct, 0 false | mismatch (2026-10-18, 0.0.34): 63 takes, correct 59 (93.7%), rejected 2 (021, 046), false 2 (3.2%): take-003 "give me twenty liters" heard "give me twenty five liters" → fuel.add 25 (confidence 1.00); take-015 "right side tires" heard "right side don't" → tires.none.right (0.65). After `reject-trailing-negation` (a negation as the last word rejects the piece): take-015 rejected; 63 takes, correct 59, rejected 3 (015, 021, 046), false 1 (take-003, 1.6%). Recognizer gives no signal against take-003: "five" at 1.00, a single alternative, decoding without the grammar hears "twenty five" too, the word lasts 0.24 s (ru «пять» 0.18–0.21 s). The user listened to the take: "twenty" was slurred, so it was a slip, not a recognition error; the take was recorded again (the old one is kept in `spike\samples-replaced-2026-10-18\en`). ok (2026-10-18, 0.0.35): 63 takes, correct 60 (95.2%), rejected 3 (015, 021, 046), false 0; median recognition 47 ms |

## 2. Idle CPU and memory

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | `pitvoice`, PTT on the wheel, window closed (tray), game not running | Task Manager → Details, "CPU time" of `PitVoice.exe`; note it, wait 60 s, note again | under 1% of one core above what the PTT device costs (GT Neo rim ~0.4%) | ok (2026-10-18, Release 0.0.35, settings `spike\c1`: ru, PTT on GT Neo button 1): `TotalProcessorTime` of `PitVoice.exe` over 60.0 s in the tray — 0.52% of one core, the rim included (earlier: 0.81% on 0.0.11) |
| 2.2 | 2.1, say one phrase so the model is loaded | "Memory (private working set)" and "Working set" of `PitVoice.exe` | working set ≤ 450 MB | ok (2026-10-18, 0.0.35): model loaded at start (439 ms); after one phrase working set 386 MB, peak 397 MB (earlier: 390 MB on 0.0.11). Noted: private bytes (commit) 972 MB, not measured before; TZ §12 counts the working set |

## 3. Latency

TZ §7: end of the recording → start of the result cue ≤ 1000 ms for a phrase of about 2 s confirmed by telemetry. The
host prints `cue <name> <N> ms after the recording ended` for every phrase (`pitvoice-cli run`); the window of the app
does not show it, and the app and `run` cannot run at the same time.

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | Test Drive, driver in the car, `pitvoice-cli run` | ten phrases: fuel, all tires, two wheels, windshield, fast repair, a compound, clear, two combined, one not understood | every applied phrase ≤ 1000 ms; note min–max | ok (2026-10-18, Release 0.0.35, fifine mic, cues from the USB headset): 16 presses. Applied and confirmed by telemetry: fuel 30 (209 ms), all tires (169, 166), two wheels LF+RR (189), windshield (158), wet compound TC(1)+4 wheels (202), clear (180), tires + fuel 20 (212), tires.none (185) — 158–212 ms. Rejected: 41–89 ms (low confidence «залей» 0.41, «сбрось все» 0.53, «не» 0.42; «литр» alone; one empty recording). Fast repair: sent FR, not confirmed (no fast repairs in Test Drive, as in `iracing-pit.md` 5.3–5.5); its error cue was cut by the next press. No false execution. Earlier: 173–262 ms applied, 29–71 ms rejected on 0.0.10 |

## 4. Reliability and rights

| # | What | Expected | Result |
|---|---|---|---|
| 4.1 | a failing stage (capture, recognition, parsing, execution, cue) | error cue, the host goes on, the next press is processed | covered by tests: `VoicePipelineTests` (MicrophoneMissing_CaptureFailure_ErrorCue, RecognitionThrows_ErrorCue_NextPhraseApplied, ParsingThrows_ErrorCue, ExecutionThrows_ErrorCue_HostGoesOn, CuePlaybackFails_…), `SpeechRecognizerTests.Asr_EngineThrows_ErrorResultAndNextRequestWorks`; crash reports and the offer checked in `diagnostics.md` sections 2–3 |
| 4.2 | iRacing as administrator, PitVoice not | the hint about rights | not reproducible on this machine (the sim runs non-elevated even when the iRacing UI is elevated: `iracing-pit.md` 6.2, `ptt-cues.md` 3.6, `tray-app.md` 3.13); covered by tests with a substituted elevation probe |
| 4.3 | install without administrator rights | no elevation prompt | ok: `release.md` 2.1 (v0.0.19 in Windows Sandbox) |

## 5. Installer size

| # | Action | Expected | Result |
|---|---|---|---|
| 5.1 | the release v0.1.0: size of `PitVoice-win-Setup.exe` (the Pack job prints it and fails above the limit) | ≤ 90 MB | earlier: 85.8 MB on v0.0.29 |

## Result

On 0.0.35 (2026-10-18) every measured item passes: accuracy (1.1, 1.2), idle CPU (2.1), memory (2.2), latency (3.1).
4.1–4.3 are covered by tests and earlier checks. Left: 5.1, the size of the installer of the release v0.1.0.
