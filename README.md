# PitVoice

PitVoice is a free, open-source Windows tray utility that gives iRacing drivers voice control over pit commands. Hold a wheel button, say "change tires and add thirty liters", release: the phrase is recognized locally, parsed into deterministic pit commands, sent to iRacing through the official SDK and confirmed by a short sound cue. Russian and English are supported from the start; a new language is a single YAML pack plus models. No cloud, no telemetry.

## Repository layout

```
src/PitVoice.Core/                 intents, language packs, numbers, normalization, parsing, PTT state machine, pipeline,
                                   event bus, settings, ASR/audio/cue interfaces (no platform deps)
src/PitVoice.Models/               model manifest, download with sha256 check, per-user model store
src/PitVoice.Speech/               microphone capture and cue playback (NAudio), WAV loading, resampling, Vosk recognizer
src/PitVoice.Input/                PTT sources: game device buttons (SDL3) and a low-level keyboard hook
src/PitVoice.IRacing/              iRacing SDK: telemetry, pit commands, telemetry recording and replay
tools/PitVoice.Cli/                pitvoice-cli: run, ptt, cues, parse, validate, models, devices, recognize, listen, status, send, record
tools/cue-gen/                     generates the three built-in cue sounds
tests/PitVoice.Core.Tests/         unit tests for the core
tests/PitVoice.Input.Tests/        language detection, smoke tests of the PTT sources (Windows)
tests/PitVoice.LanguagePacks.Tests/ data-driven tests from the `tests:` section of every language pack
tests/PitVoice.Models.Tests/       manifest, license allowlist, store and downloader tests
tests/PitVoice.Speech.Tests/       audio conversion, capture logic, grammar, Vosk engine, recorded sample set
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

`%AppData%\PitVoice\settings.json`; `--settings <path>` or the `PITVOICE_SETTINGS` environment variable point elsewhere. `ptt bind` writes it; everything else is edited by hand and read when `run` starts:

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
  "threshold": 0.6
}
```

- `language`: `ru` or `en`; by default the language of Windows.
- `ptt.binding`: a game device button (`kind: joystick`, `guid`, `button` from 1) or a key (`kind: keyboard`, `vk` virtual-key code); `null` until assigned. `ptt.mode`: `hold` or `toggle`.
- `microphone`, `outputDevice`: device names as `devices` prints them; `null` is the system default. A missing device falls back to the default with a warning.
- `cueVolume`: 0 to 100; 0 mutes the cues. `defaultUnit`: `liters` or `gallons`. `threshold`: confidence threshold 0 to 1.

A missing field gets its default; an invalid value gets its default (the volume is clamped to 0..100) with a warning. A file that is not valid JSON is renamed to `settings.json.bak` and the defaults are used.

## Versioning

The project version lives in the `VERSION` file in `MAJOR.MINOR.PATCH` form (for example `1.3.15`), each segment in the range 0..99. It starts at `0.0.1`.

- **PATCH** is bumped automatically by the `.githooks/pre-commit` hook on every commit.
- When a segment would exceed 99 it rolls over: `0.0.99` -> `0.1.0`, `0.99.99` -> `1.0.0`. Nothing is done by hand.
- Skip the bump for one commit if ever needed: `SKIP_VERSION_BUMP=1 git commit ...`

After cloning the repository, enable the hook once:

```bash
git config core.hooksPath .githooks
```
