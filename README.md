# PitVoice

PitVoice is a free, open-source Windows tray utility that gives iRacing drivers voice control over pit commands. Hold a wheel button, say "change tires and add thirty liters", release: the phrase is recognized locally, parsed into deterministic pit commands, sent to iRacing through the official SDK and confirmed by voice. Russian and English are supported from the start; a new language is a single YAML pack plus models. No cloud, no telemetry.

## Repository layout

```
src/PitVoice.Core/                 intents, language packs, numbers, normalization, parsing (no platform deps)
tools/PitVoice.Cli/                pitvoice-cli: parse a text command, validate language packs
tests/PitVoice.Core.Tests/         unit tests for the core
tests/PitVoice.LanguagePacks.Tests/ data-driven tests from the `tests:` section of every language pack
languages/                         ru.yaml, en.yaml
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

## Versioning

The project version lives in the `VERSION` file in `MAJOR.MINOR.PATCH` form (for example `1.3.15`), each segment in the range 0..99. It starts at `0.0.1`.

- **PATCH** is bumped automatically by the `.githooks/pre-commit` hook on every commit.
- When a segment would exceed 99 it rolls over: `0.0.99` -> `0.1.0`, `0.99.99` -> `1.0.0`. Nothing is done by hand.
- Skip the bump for one commit if ever needed: `SKIP_VERSION_BUMP=1 git commit ...`

After cloning the repository, enable the hook once:

```bash
git config core.hooksPath .githooks
```
