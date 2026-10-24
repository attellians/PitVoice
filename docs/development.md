# Developing PitVoice

Building, testing, the console tools, the settings file, logs and releasing. What PitVoice is and how to install it: [README](../README.md) ([English](../README.en.md)).

## Repository layout

```
src/PitVoice.Core/                 intents, language packs, numbers, normalization, parsing, PTT state machine, pipeline,
                                   event bus, settings, ASR/audio/cue interfaces (no platform deps)
src/PitVoice.Models/               model manifest, download with sha256 check, per-user model store
src/PitVoice.Speech/               microphone capture and cue playback (NAudio), WAV loading, resampling, Vosk recognizer
src/PitVoice.Input/                PTT sources: game device buttons (SDL3) and a low-level keyboard hook
src/PitVoice.IRacing/              iRacing SDK: telemetry, pit commands, telemetry recording and replay
src/PitVoice.Host/                 the pipeline host shared by the app and `run`: composition from settings, live apply,
                                   tray status, recent phrases, models, PTT assignment, one host per user,
                                   log, crash reports and the diagnostics archive
src/PitVoice.App/                  the tray app (WPF, WPF-UI): window, pages, tray icon, interface strings ru/en
src/PitVoice.VrOverlay/            the VR helper: the indicator in a SteamVR headset through an OpenVR overlay, fed by the app
third_party/openvr/                openvr_api.dll of the pinned OpenVR SDK with its license and hash
tools/PitVoice.Cli/                pitvoice-cli: run, ptt, cues, diagnostics, parse, validate, models, devices, recognize, listen, status, send, record
tools/cue-gen/                     generates the three built-in cue sounds
tools/icon-gen/                    renders the app icon (.ico) from the same vector sign as the tray icon
tools/notices-gen/                 generates THIRD-PARTY-NOTICES.md and the "About" list; components.json, license texts
tests/PitVoice.Core.Tests/         unit tests for the core
tests/PitVoice.Input.Tests/        language detection, smoke tests of the PTT sources (Windows)
tests/PitVoice.LanguagePacks.Tests/ data-driven tests from the `tests:` section of every language pack
tests/PitVoice.Models.Tests/       manifest, license allowlist, store and downloader tests
tests/PitVoice.Speech.Tests/       audio conversion, capture logic, grammar, Vosk engine, recorded sample set
tests/PitVoice.Host.Tests/         the app logic on fakes: live apply, status, journal, reference, models, assignment, diagnostics
tests/PitVoice.App.Tests/          interface dictionaries and the components list (Windows only)
tests/PitVoice.VrOverlay.Tests/    the VR protocol, the helper loop on a fake OpenVR, the overlay pose
tests/PitVoice.NoticesGen.Tests/   the notices generator on prepared package files: listing, forbidden licenses
languages/                         ru.yaml, en.yaml
models/                            manifest.json: downloadable models with URL, size, sha256 and license
docs/                              development.md (this file), adding-a-language.md, manual-checks/
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

Commands are built from words, not fixed phrases, and the word order is free. A few examples of what is understood:

- "залей тридцать литров", "долей двадцать", "тридцать литров топлива"; "add thirty liters", "give me twenty"
- "меняй шины", "шины меняй", "замени левое переднее", "задние шины"; "change tires", "left front", "swap rears"
- several targets and the verb said once: "меняй переднее левое заднее правое", "меняй левые и передние", "не меняй левые и переднее правое"; "change front left and rear right" (a side and an axle need "и": "меняй левые передние" is rejected, it could be the front left wheel)
- removal: "не меняй шины", "без шин", "стекло не трогай", "без ремонта"; "no tires", "keep fronts", "leave the rears"
- tire compound, always on all four tires: "меняем резину на дождевые", "на сухие", "дождевые шины"; "change tires to wets", "change to slicks" (a car with no such compound, or with several dry ones, gets an error cue and nothing is sent; "меняй левые на дождевые" is rejected)
- compound commands, also when the recognizer lost the "и": "меняй шины залей двадцать пять литров"

One word never changes the pit stop, fuel amounts are whole numbers only ("тридцать точка два" is rejected, nothing is rounded), and a phrase that can be read two ways is rejected. The full word lists are in `languages/*.yaml` and on the "Commands" page.

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

When the primary source of a model fails, answers nothing for 20 s or stops sending for 30 s, the download goes to its `mirror`: a copy in the pre-release [`models`](https://github.com/attellians/PitVoice/releases/tag/models) of this repository, under the same file name. A test fails for a manifest entry without such a mirror. After adding a model to the manifest, run the **Mirror models** workflow by hand (Actions → Mirror models → Run workflow): it creates the `models` release if needed, downloads every archive that is not there yet, checks its sha256 against the manifest and uploads it. Copies already in the release are never replaced; a checksum mismatch fails the run with the model id. Being a pre-release, `models` is never taken for an app update and never becomes the "Latest" release.

## Speech from a file or the microphone

Nothing is executed: these commands print the recognized words with per-word confidence, the recognition time and the same parse block as `parse`.

```bash
dotnet run --project tools/PitVoice.Cli -- devices                                   # microphones, the default one is marked
dotnet run --project tools/PitVoice.Cli -- recognize --lang en --wav tests/PitVoice.Speech.Tests/Fixtures/en-change-all-tires-add-thirty-liters.wav
dotnet run --project tools/PitVoice.Cli -- listen --lang ru                          # Enter starts, Enter stops (8 s max)
dotnet run --project tools/PitVoice.Cli -- listen --lang ru --mic "Microphone (USB Audio)" --save-wav samples/ru/take.wav
```

The recognizer grammar is checked against the model vocabulary when the model loads: a pack word the model spells with `ё` (`еще` → `ещё`) is used in the model spelling, and a word the model does not know at all is left out, because it can never be recognized. `validate` reports such words as warnings, with the word classes that use them, for every installed model (`--models-dir` works here too); without an installed model the check is skipped.

Audio stays in memory. `listen` writes a file only with `--save-wav`; later takes of the same session get `-2`, `-3`, ... suffixes. `listen --script <list> --save-wav <directory>` records the acceptance set phrase by phrase from a list ([tests/PitVoice.Speech.Tests/Samples/README.md](../tests/PitVoice.Speech.Tests/Samples/README.md)).

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

Every setting is saved at once and applies from the next PTT press, without a restart; a phrase in flight finishes
with the old settings. Switching the language switches the interface, the commands and the speech model. The home
page shows the state with its reason (no model, no PTT button, no game), the button and the last 20 phrases of this
run (kept in memory only) with the recognized text, the outcome and the response, so a command is checked by saying
it. "Commands" lists the words of every command with example phrases, including your own phrases, which are set on its
page "Own phrases" (see [docs/adding-a-language.md](adding-a-language.md#own-words-user-additions)).

The icon shows one state: no model, error (button not assigned or unplugged, pack failed), no connection to iRacing,
ready; the tooltip names the reason. Closing the window keeps PitVoice running; "Exit" in the icon menu stops it.
A second start opens the window of the running app. The app and `pitvoice-cli run` never run at the same time: one
press would otherwise be executed twice.

### Indicator above the game

A small icon above iRacing shows that PitVoice is listening (blue, pulsing), recognizing (dark blue, spinning) and how
the phrase ended: done (green), didn't get it (amber), no game (grey), error (red); the result stays for 2.5 s and the
icon goes back to a dim microphone. Kind "Icon and word" adds a short word to the right of the icon; the icon
never moves and keeps room for the word from the right edge of the screen. Clicks pass through the indicator into the game and it never takes the focus. It is shown only while you
are in the car: in the menus, replays and without the game it stays hidden and a phrase gives only its cue. Move it
with "Move" on the "Overlay" page of the window (its settings are there too) or "Move indicator" in the icon menu: drag it with the mouse and press "Done" under it; the
place is kept per monitor and falls back to the top right corner of the main monitor when that monitor is gone.
Windows shows it over iRacing in **windowed and borderless** mode only; in exclusive fullscreen it is not visible.
`pitvoice-cli run` has no indicator.

### Indicator in the VR headset

"VR mode" ("Settings" → "Language and look", `overlay.vr`) shows the same indicator in a SteamVR headset too. The app
never touches OpenVR itself: while the mode and "Show the overlay" are on, it looks for the `vrserver` process every
2 s and only then starts `PitVoice.VrOverlay.exe` next to it, with redirected standard streams and inside a job object
that ends it with the app. The helper loads `openvr_api.dll` (OpenVR SDK 2.5.1 in `third_party/openvr`), creates an
overlay fixed to the head with a Direct3D 11 texture on the GPU of the SteamVR compositor (`SetOverlayRaw` blinked at
30 frames a second), and takes length-prefixed binary messages on its input (`Frame` RGBA pictures, `Visible`,
`Place` angles, `Ping`); it answers `ready`, `pong`, `error <code>` and `quit` on its output
(`src/PitVoice.Core/Vr/VrProtocol.cs`). The app renders the frames with a second `IndicatorBadge` outside any window,
30 per second while an animation runs and none when idle; a crash, a hang (no `pong` for 5 s) or an error of the helper
is retried after 2 s, three within 2 minutes give "VR error" until SteamVR restarts. The helper is only next to the app in
a published folder, not in `bin\`: publish the app and `src/PitVoice.VrOverlay` into one folder to try it.

`PitVoice.VrOverlay.exe --demo` shows a green test badge in the top right of the view until Enter, without the app.

#### Building a VR stand

There is no headset to test with, so SteamVR runs with its null driver: a virtual headset, the compositor and the
overlays work, and the compositor shows the picture of both eyes in a desktop window titled **Headset Window**
(2160 × 1200); it plays the role of VR View. Checked 2026-10-24 with SteamVR 2.18.4.

1. Install **SteamVR** from Steam (Library → Tools). Start and close it once, so its config files exist.
2. Close SteamVR, keep a copy of `Steam\config\steamvr.vrsettings`, then add to it (merge with the sections that are
   already there):

   ```json
   "steamvr": {
     "requireHmd": false,
     "forcedDriver": "null",
     "activateMultipleDrivers": true
   },
   "driver_null": {
     "enable": true
   },
   "power": {
     "pauseCompositorOnStandby": false,
     "turnOffScreensTimeout": 3600
   }
   ```

   and set `"enable": true` in `Steam\steamapps\common\SteamVR\drivers\null\resources\settings\default.vrsettings`
   (section `driver_null`). The virtual headset has no proximity sensor, so without the `power` lines SteamVR goes to
   standby after a short while and the headset window stays black.
3. Start SteamVR (`SteamVR\bin\win64\vrstartup.exe`): "Headset ready", and the headset window shows the SteamVR scene
   with the dashboard. The first start may open Room Setup; finish or close it.
4. Check the helper alone: `publish\PitVoice.VrOverlay.exe --demo` — a green badge appears at the top right of the view
   in both eyes. The headset window is a Direct3D window: `PrintWindow` gives black, so capture it from the screen while
   it is on top.

Remove the lines (or put the copy back) to use a real headset again.

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
| success (two rising tones) | the phrase was applied in the game |
| error (two low falling tones) | anything else: not understood, game not running, not in the car, not (fully) applied, a failed stage |

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
  "interfaceLanguage": null,
  "theme": "system",
  "overlay": { "enabled": true, "kind": "A", "scale": 100, "opacity": 90, "position": null, "vr": false }
}
```

- `language`: `ru` or `en`; by default the language of Windows.
- `ptt.binding`: a game device button (`kind: joystick`, `guid`, `button` from 1) or a key (`kind: keyboard`, `vk` virtual-key code); `null` until assigned. `ptt.mode`: `hold` or `toggle`.
- `microphone`, `outputDevice`: device names as `devices` prints them; `null` is the system default. A missing device falls back to the default with a warning.
- `cueVolume`: 0 to 100; 0 mutes the cues. `defaultUnit`: `liters` or `gallons`. `threshold`: confidence threshold 0 to 1.
- `interfaceLanguage`: `ru` or `en` for the window and the tray menu; `null` follows `language`. `theme`: `system`, `light` or `dark`.
- `overlay`: the indicator above the game. `kind`: `A` (icon) or `B` (icon and word); `scale`: 75 to 200 (%); `opacity`: 30 to 100 (%), the idle icon gets 45 % of it; `position`: center of the icon in physical pixels (`{ "x": …, "y": … }`), `null` is the top right corner of the main monitor; `vr`: "VR mode", the indicator in a SteamVR headset too (only with `enabled`). Out-of-range size and opacity are clamped with a warning, an unknown kind becomes `A`, a `vr` that is not `true`/`false` becomes `false`.

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

Work goes into the `development` branch; `main` holds only what was released as stable and moves only by fast-forward to `development`, never by its own commits. A release is one tag; GitHub Actions does the rest (`.github/workflows/release.yml`). The form of the tag decides what is published:

| Tag | Tagged commit must be in | Published as |
|---|---|---|
| `vX.Y.Z` | `main` | a stable release, the latest release of the project |
| `vX.Y.Z-alpha` | `development` | a pre-release, never the latest release; the last 5 are kept, older ones are deleted with their tags |

Both kinds go through the same steps: the workflow checks the form of the tag, that `X.Y.Z` matches `VERSION` in the tagged commit (the suffix takes no part), that the commit is in the branch of its kind and that the tag is not released yet, runs the full CI, publishes the app and the CLI self-contained for win-x64 with the version of the tag (an alpha shows `X.Y.Z-alpha` in "About" and in the log), checks the notices, packs the installer (Velopack, package id `PitVoiceApp`, so the program goes into `%LocalAppData%\PitVoiceApp` and never shares a directory with the data in `%LocalAppData%\PitVoice`), fails if it is above 90 MB, and publishes the release with `PitVoice-win-Setup.exe` as its only file. The update packages are not published until updates from the app come back.

**Alpha** (any commit of `development`):

1. Commit everything that goes into the build in `development` (the pre-commit hook bumps `VERSION`) and push it: `git push origin development`.
2. Read the version of that commit: `git show HEAD:VERSION`.
3. Tag that commit with the notes as the tag message: `git tag -a v<VERSION>-alpha -m "<what changed>"`.
4. Push the tag: `git push origin v<VERSION>-alpha`.

**Stable** (the head of `development`, usually after its alpha was tried):

1. Move `main` to `development`, without a new commit:
   `git switch main`, `git merge --ff-only development`, `git push origin main`, `git switch development`.
2. Read the version: `git show main:VERSION`.
3. Tag that commit: `git tag -a v<VERSION> -m "<what changed>" main`.
4. Push the tag: `git push origin v<VERSION>`.

If `git merge --ff-only` refuses, something was committed to `main` directly: move it into `development` (`git switch development`, `git cherry-pick <commit>`), push, and start the stable steps again.

When a release fails in "Tag form, VERSION and branch", nothing is published; delete the tag (`git tag -d <tag>` and `git push origin :refs/tags/<tag>`) and then:

- **not a release tag** (`-beta`, `-rc1`, `-alpha.1`, `-ALPHA`): only `vX.Y.Z` and `vX.Y.Z-alpha` are allowed; tag again in one of these forms.
- **does not match VERSION** (for example the tag went on a later commit whose hook already bumped the version): the message names both versions; tag the commit whose `VERSION` is the one to release.
- **a stable release comes only from main**: the commit is only in `development`; release it as `v<VERSION>-alpha`, or move `main` to it as above and tag again.
- **a pre-release comes only from development**: tag a commit of `development`.

A tag of a version that is already released fails with "version already released" and leaves that release alone; to rebuild the same commit, delete its release and tag on GitHub and push the tag again. Nothing reaches users before the release is published; to withdraw a release, delete it and its tag on GitHub. Deleting a release whose tag is `models` removes the model mirror: run **Mirror models** again then. The cleanup of old alphas never touches stable releases or `models`; when it cannot delete one, the run shows a warning with the tag, and the new pre-release stays published.

## Versioning

The project version lives in the `VERSION` file in `MAJOR.MINOR.PATCH` form (for example `1.3.15`), each segment in the range 0..99. It starts at `0.0.1`.

- **PATCH** is bumped automatically by the `.githooks/pre-commit` hook on every commit.
- When a segment would exceed 99 it rolls over: `0.0.99` -> `0.1.0`, `0.99.99` -> `1.0.0`. Nothing is done by hand.
- Skip the bump for one commit if ever needed: `SKIP_VERSION_BUMP=1 git commit ...`

After cloning the repository, enable the hook once:

```bash
git config core.hooksPath .githooks
```
