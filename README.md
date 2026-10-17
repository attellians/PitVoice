# PitVoice

PitVoice is a free, open-source Windows tray utility that gives iRacing drivers voice control over pit commands. Hold a wheel button, say "change tires and add thirty liters", release: the phrase is recognized locally, parsed into deterministic pit commands, sent to iRacing through the official SDK and confirmed by a short sound cue. Russian and English are supported from the start; a new language is a single YAML pack plus models. No cloud, no telemetry.

## Install

1. Download `PitVoice-win-Setup.exe` from the [latest release](https://github.com/attellians/PitVoice/releases/latest) (Windows 10/11 x64).
2. Run it. It needs no administrator rights and nothing from the network: PitVoice and the .NET runtime it needs go into your profile (`%LocalAppData%\PitVoiceApp`; your models, logs and diagnostics live apart, in `%LocalAppData%\PitVoice`), shortcuts appear in the Start menu and on the desktop, and the app starts.
   The installer is not signed yet, so Windows SmartScreen may say "Windows protected your PC": click **More info → Run anyway**.
3. First setup: the home page lists what is left. Download the speech model of your language on the "Models" page, assign the push-to-talk button in "Settings", and try a phrase on the "Check" page. The models are not in the installer; they are downloaded only when you ask for it.

**New versions.** The app does not look for updates yet and never goes online on its own: model downloads are the only network traffic, and only when you ask. To update, download the installer of the newer version from the [releases](https://github.com/attellians/PitVoice/releases) and run it; it says "PitVoice is already installed", press **Repair**. Settings, models and phrase additions stay.

**Uninstall.** "Settings → Apps → Installed apps → PitVoice → Uninstall" closes the app and removes everything: the program (`%LocalAppData%\PitVoiceApp`), the shortcuts, `%LocalAppData%\PitVoice` (models, logs, crash reports, diagnostics archives) and `%AppData%\PitVoice` (settings, phrase additions).

**About the GCC runtime.** The speech recognizer (Vosk) for Windows is built with MinGW-w64, so it comes with `libstdc++-6.dll` and `libgcc_s_seh-1.dll`. They are under GPL-3.0 with the GCC Runtime Library Exception, which allows shipping them with programs under any license; PitVoice itself stays MIT. Every component and its license are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

PitVoice is licensed under the [MIT License](LICENSE). Everything below is about building it from source.

## Repository layout

```
src/PitVoice.Core/                 intents, language packs, numbers, normalization, parsing, PTT state machine, pipeline,
                                   event bus, settings, ASR/audio/cue interfaces (no platform deps)
src/PitVoice.Models/               model manifest, download with sha256 check, per-user model store
src/PitVoice.Speech/               microphone capture and cue playback (NAudio), WAV loading, resampling, Vosk recognizer
src/PitVoice.Input/                PTT sources: game device buttons (SDL3) and a low-level keyboard hook
src/PitVoice.IRacing/              iRacing SDK: telemetry, pit commands, telemetry recording and replay
src/PitVoice.Host/                 the pipeline host shared by the app and `run`: composition from settings, live apply,
                                   tray status, recent phrases, command check, models, PTT assignment, one host per user,
                                   log, crash reports and the diagnostics archive
src/PitVoice.App/                  the tray app (WPF, WPF-UI): window, pages, tray icon, interface strings ru/en
tools/PitVoice.Cli/                pitvoice-cli: run, ptt, cues, diagnostics, parse, validate, models, devices, recognize, listen, status, send, record
tools/cue-gen/                     generates the three built-in cue sounds
tools/icon-gen/                    renders the app icon (.ico) from the same vector sign as the tray icon
tools/notices-gen/                 generates THIRD-PARTY-NOTICES.md and the "About" list; components.json, license texts
tests/PitVoice.Core.Tests/         unit tests for the core
tests/PitVoice.Input.Tests/        language detection, smoke tests of the PTT sources (Windows)
tests/PitVoice.LanguagePacks.Tests/ data-driven tests from the `tests:` section of every language pack
tests/PitVoice.Models.Tests/       manifest, license allowlist, store and downloader tests
tests/PitVoice.Speech.Tests/       audio conversion, capture logic, grammar, Vosk engine, recorded sample set
tests/PitVoice.Host.Tests/         the app logic on fakes: live apply, status, journal, check, models, assignment, diagnostics
tests/PitVoice.App.Tests/          interface dictionaries and the components list (Windows only)
tests/PitVoice.NoticesGen.Tests/   the notices generator on prepared package files: listing, forbidden licenses
languages/                         ru.yaml, en.yaml
models/                            manifest.json: downloadable models with URL, size, sha256 and license
docs/                              adding-a-language.md
```

## Build and test

```bash
dotnet build PitVoice.sln
dotnet test PitVoice.sln
```

Try a command without a microphone or the game:

```bash
dotnet run --project tools/PitVoice.Cli -- parse --lang ru "меняй шины и залей двадцать пять литров"
dotnet run --project tools/PitVoice.Cli -- parse --lang en          # REPL
dotnet run --project tools/PitVoice.Cli -- validate                 # checks every pack and runs its tests
```

## Speech models

Recognition runs locally with [Vosk](https://alphacephei.com/vosk/) small models (Apache 2.0). Models are not part of the repository or the build: they are listed in `models/manifest.json` with URL, size, sha256 and license, and downloaded only when you ask for it. The archive is checked against the manifest before it is unpacked; a cancelled or failed download leaves nothing behind.

Models live in `%LocalAppData%\PitVoice\models\<id>\`. Override the location with `--models-dir <path>` or the `PITVOICE_MODELS_DIR` environment variable (tests and CI use it).

```bash
dotnet run --project tools/PitVoice.Cli -- models list                               # id, language, size, license, status
dotnet run --project tools/PitVoice.Cli -- models download vosk-model-small-ru-0.22  # Ctrl+C cancels and cleans up
dotnet run --project tools/PitVoice.Cli -- models verify vosk-model-small-ru-0.22
dotnet run --project tools/PitVoice.Cli -- models delete vosk-model-small-ru-0.22
```

### Model mirror

When the primary source of a model does not answer, the download goes to its `mirror`: a copy in the pre-release [`models`](https://github.com/attellians/PitVoice/releases/tag/models) of this repository, under the same file name. A test fails for a manifest entry without such a mirror. After adding a model to the manifest, run the **Mirror models** workflow by hand (Actions → Mirror models → Run workflow): it creates the `models` release if needed, downloads every archive that is not there yet, checks its sha256 against the manifest and uploads it. Copies already in the release are never replaced; a checksum mismatch fails the run with the model id. Being a pre-release, `models` is never taken for an app update and never becomes the "Latest" release.

## Speech from a file or the microphone

Nothing is executed: these commands print the recognized words with per-word confidence, the recognition time and the same parse block as `parse`.

```bash
dotnet run --project tools/PitVoice.Cli -- devices                                   # microphones, the default one is marked
dotnet run --project tools/PitVoice.Cli -- recognize --lang en --wav tests/PitVoice.Speech.Tests/Fixtures/en-change-all-tires-add-thirty-liters.wav
dotnet run --project tools/PitVoice.Cli -- listen --lang ru                          # Enter starts, Enter stops (8 s max)
dotnet run --project tools/PitVoice.Cli -- listen --lang ru --mic "Microphone (USB Audio)" --save-wav samples/ru/take.wav
```

The recognizer grammar is checked against the model vocabulary when the model loads: a pack word the model spells with `ё` (`еще` → `ещё`) is used in the model spelling, and a word the model does not know at all is left out, because it can never be recognized. `validate` reports such words as warnings, with the templates that use them, for every installed model (`--models-dir` works here too); without an installed model the check is skipped.

Audio stays in memory. `listen` writes a file only with `--save-wav`; later takes of the same session get `-2`, `-3`, ... suffixes. `--grammar phrases` switches the recognizer grammar from the vocabulary word list to whole template phrases.

Tests that need a real model are skipped when it is not installed and say which one to download. Set `PITVOICE_REQUIRE_MODELS=1` to turn a missing model into a failure (CI does).

### If speech recognition does not work

- `model not installed: <id>`: run `models download <id>`, or point `--models-dir` / `PITVOICE_MODELS_DIR` at the directory you downloaded into.
- `model directory corrupted`: an interrupted copy or a damaged disk. Run `models delete <id>` and download again.
- `speech recognition library failed to load`: the native `libvosk.dll` next to the executable could not be loaded. Install the latest [Microsoft Visual C++ Redistributable (x64)](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist), make sure `libvosk.dll`, `libgcc_s_seh-1.dll`, `libstdc++-6.dll` and `libwinpthread-1.dll` were not removed by an antivirus, and use a 64-bit Windows.
- `no microphone`: Windows sees no active input device. Check *Settings → System → Sound → Input* and the microphone privacy setting for desktop apps.

## The tray app

```bash
dotnet run --project src/PitVoice.App    # opens the window; the icon stays in the notification area
```

The window walks through the first setup: a "What is left to set up" block on the home page leads to the model
download, the PTT button assignment and a phrase check. Every setting is saved at once and applies from the next PTT
press, without a restart; a phrase in flight finishes with the old settings. Switching the language switches the
interface, the commands and the speech model. The home page shows the game and button state and the last 20 phrases
of this run (kept in memory only). "Check" parses a typed or spoken phrase without touching the game; "Commands" lists
the phrases of the language, including your own (`%AppData%\PitVoice\languages\<code>.yaml`, see
[docs/adding-a-language.md](docs/adding-a-language.md#own-phrases-user-additions)).

The icon shows one state: no model, error (button not assigned or unplugged, pack failed), no connection to iRacing,
ready; the tooltip names the reason. Closing the window keeps PitVoice running; "Exit" in the icon menu stops it.
A second start opens the window of the running app. The app and `pitvoice-cli run` never run at the same time: one
press would otherwise be executed twice.

## Push-to-talk: from the wheel button to the game

`run` is the whole pipeline without a window: PTT button → microphone → recognition → parsing → iRacing → sound cue. It takes everything from the settings file, so set it up once:

```bash
dotnet run --project tools/PitVoice.Cli -- models download vosk-model-small-ru-0.22   # the model of your language
dotnet run --project tools/PitVoice.Cli -- devices                                   # microphones, output devices, game devices
dotnet run --project tools/PitVoice.Cli -- ptt bind                                  # press the wheel button (or a key) to use as PTT
dotnet run --project tools/PitVoice.Cli -- cues play                                 # hear the three cues on the output device
dotnet run --project tools/PitVoice.Cli -- run                                       # Ctrl+C stops
```

`run` prints the language, the PTT button, the devices and the game state, then one line per pipeline event: recording, recognized words with confidence, parsed or rejected commands, the outcome in the game, the cue with the time from the end of the recording, and the text response of the language pack. Without the model of the language it exits with the `models download` command to run; without a PTT button it says to run `ptt bind`.

The PTT works while iRacing has the focus. In **hold** mode (default) the recording lasts while the button is held; presses shorter than 250 ms are ignored. In **toggle** mode the first press starts and the second stops (a second press within 300 ms is ignored as a bounce); without a second press the recording stops by itself after 8 s. A press while the previous phrase is still being processed cuts its result cue and starts a new recording; commands already sent to the game stay. A wheel button is stored by the device model id and button number, so it survives reconnecting the wheel and a different device order. `ptt watch` shows raw presses of every game device and key, also while another window has the focus.

If iRacing runs as administrator and PitVoice does not, Windows blocks pit commands and hides keys pressed in the game window from the keyboard hook; `run` warns about it. Start both normally.

### Cues

There is no voice. Three short built-in sounds tell what happened:

| Cue | When |
| --- | --- |
| start (short rising tone) | the recording started |
| success (two rising tones) | the phrase was applied in the game, or it was "cancel" |
| error (two low falling tones) | anything else: not understood, game not running, not in the car, not (fully) applied, not supported yet, a failed stage |

The reason of an error is in the `run` output. `cues play [start|success|error|all] [--volume 0-100] [--device <name>]` plays them without the game or a microphone. The sounds are generated by `tools/cue-gen` (`dotnet run --project tools/cue-gen` rewrites `src/PitVoice.Speech/Cues/*.wav` byte for byte).

### Settings file

`%AppData%\PitVoice\settings.json`; `--settings <path>` or the `PITVOICE_SETTINGS` environment variable point elsewhere. The tray app and `ptt bind` write it; `run` reads it when it starts:

```json
{
  "version": 1,
  "language": "ru",
  "ptt": {
    "binding": { "kind": "joystick", "guid": "0300bee1703600000508000000000000", "button": 5, "name": "SIMAGIC GT Neo" },
    "mode": "hold"
  },
  "microphone": null,
  "outputDevice": null,
  "cueVolume": 100,
  "defaultUnit": "liters",
  "threshold": 0.6,
  "setupCheckDone": false,
  "interfaceLanguage": null,
  "theme": "system"
}
```

- `language`: `ru` or `en`; by default the language of Windows.
- `ptt.binding`: a game device button (`kind: joystick`, `guid`, `button` from 1) or a key (`kind: keyboard`, `vk` virtual-key code); `null` until assigned. `ptt.mode`: `hold` or `toggle`.
- `microphone`, `outputDevice`: device names as `devices` prints them; `null` is the system default. A missing device falls back to the default with a warning.
- `cueVolume`: 0 to 100; 0 mutes the cues. `defaultUnit`: `liters` or `gallons`. `threshold`: confidence threshold 0 to 1.
- `setupCheckDone`: set by the app after the first phrase check; hides the last item of the setup checklist.
- `interfaceLanguage`: `ru` or `en` for the window and the tray menu; `null` follows `language`. `theme`: `system`, `light` or `dark`.

A missing field gets its default; an invalid value gets its default (the volume is clamped to 0..100) with a warning. A file that is not valid JSON is renamed to `settings.json.bak` and the defaults are used.

## Logs and diagnostics

The tray app and `run` write a log of what the app does to `%LocalAppData%\PitVoice\logs\pitvoice-<date>.log`: start and
stop, settings, the pack, additions and model, the game and its administrator rights, the PTT device, stage failures and
"not (fully) applied" outcomes without the commands. Nothing of what you say is written anywhere: no audio, no
recognized text, no record per phrase. A new file starts every day and at 10 MB; the last 7 are kept. Other CLI
commands write no log.

An unhandled error writes `crash-<date-time>.log` next to the log (the last 10 are kept) and ends the process; the next
start of the app offers to collect diagnostics on the home page. `PITVOICE_TEST_CRASH=ui|background|task` forces such a
crash 5 s after start, for checking it (`ui` only in the app).

"Collect diagnostics" in the app settings, or the command below, writes
`%LocalAppData%\PitVoice\diagnostics\pitvoice-diagnostics-<date-time>.zip` (the last 5 are kept): a summary of the
system, devices and state, the logs and crash reports, the settings file and your additions. The path of your Windows
profile is replaced with `%USERPROFILE%` in every file. Nothing is sent anywhere; attach the archive yourself.

```bash
dotnet run --project tools/PitVoice.Cli -- diagnostics collect                      # prints the path of the archive
```

## Third-party notices

`THIRD-PARTY-NOTICES.md` and `src/PitVoice.App/ThirdParty.json` (the list in "About") are generated, never edited by hand. The generator reads the restored packages of the app and the CLI (with every transitive one), `tools/notices-gen/components.json` (native libraries, the .NET runtime, packages without a license expression) and `models/manifest.json`, and appends the full text of every license from `tools/notices-gen/licenses/`.

```bash
dotnet run --project tools/notices-gen               # regenerate both files after adding, removing or updating a package
dotnet run --project tools/notices-gen -- --check    # what CI runs: fails when the files are out of date
```

Regenerate and commit both files whenever a package of the app or of any project it references changes, or a native component changes in `components.json`. Generation fails, writing nothing, when a package has no license expression and no entry in `components.json`, or when a license may not ship: GPL, LGPL, AGPL and non-commercial licenses are forbidden, and the message names the package and the chain it comes through. The one exception, the GCC runtime under the GCC Runtime Library Exception, is allowed only for the entries described in `components.json`.

## Releasing

A release is one tag; GitHub Actions does the rest (`.github/workflows/release.yml`): it checks that the tag matches `VERSION` in the tagged commit and that the version is not released yet, runs the full CI, publishes the app and the CLI self-contained for win-x64, checks the notices, packs the installer with the full and delta update packages (Velopack, package id `PitVoiceApp`, so the program goes into `%LocalAppData%\PitVoiceApp` and never shares a directory with the data in `%LocalAppData%\PitVoice`), uploads it as `PitVoice-win-Setup.exe` without the portable zip, fails if the installer is above 90 MB, and publishes the release.

1. Commit everything that goes into the release (the pre-commit hook bumps `VERSION`) and push the branch.
2. Read the version of that commit: `git show HEAD:VERSION`.
3. Tag that commit with the release notes as the tag message: `git tag -a v<VERSION> -m "<what changed>"`.
4. Push the tag: `git push origin v<VERSION>`.

If the tag does not match `VERSION` (for example it went on a later commit whose hook already bumped the version), the release fails and names both versions: delete the tag (`git tag -d v<X>` and `git push origin :refs/tags/v<X>`) and tag the right commit. A tag of a version that is already released fails with "version already released" and leaves that release alone; release the next version instead. Nothing reaches users before the release is published; to withdraw a release, delete it and its tag on GitHub. Deleting a release whose tag is `models` removes the model mirror: run **Mirror models** again then.

## Versioning

The project version lives in the `VERSION` file in `MAJOR.MINOR.PATCH` form (for example `1.3.15`), each segment in the range 0..99. It starts at `0.0.1`.

- **PATCH** is bumped automatically by the `.githooks/pre-commit` hook on every commit.
- When a segment would exceed 99 it rolls over: `0.0.99` -> `0.1.0`, `0.99.99` -> `1.0.0`. Nothing is done by hand.
- Skip the bump for one commit if ever needed: `SKIP_VERSION_BUMP=1 git commit ...`

After cloning the repository, enable the hook once:

```bash
git config core.hooksPath .githooks
```
