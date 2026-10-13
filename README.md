# PitVoice

PitVoice is a free, open-source Windows tray utility that gives iRacing drivers voice control over pit commands. Hold a wheel button, say "change tires and add thirty liters", release: the phrase is recognized locally, parsed into deterministic pit commands, sent to iRacing through the official SDK and confirmed by voice. Russian and English are supported from the start; a new language is a single YAML pack plus models. No cloud, no telemetry.

## Repository layout

```
src/PitVoice.Core/                 intents, language packs, numbers, normalization, parsing, ASR/audio interfaces (no platform deps)
src/PitVoice.Models/               model manifest, download with sha256 check, per-user model store
src/PitVoice.Speech/               microphone capture (NAudio), WAV loading, resampling, Vosk recognizer
tools/PitVoice.Cli/                pitvoice-cli: parse, validate, models, devices, recognize, listen
tests/PitVoice.Core.Tests/         unit tests for the core
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

## Versioning

The project version lives in the `VERSION` file in `MAJOR.MINOR.PATCH` form (for example `1.3.15`), each segment in the range 0..99. It starts at `0.0.1`.

- **PATCH** is bumped automatically by the `.githooks/pre-commit` hook on every commit.
- When a segment would exceed 99 it rolls over: `0.0.99` -> `0.1.0`, `0.99.99` -> `1.0.0`. Nothing is done by hand.
- Skip the bump for one commit if ever needed: `SKIP_VERSION_BUMP=1 git commit ...`

After cloning the repository, enable the hook once:

```bash
git config core.hooksPath .githooks
```
