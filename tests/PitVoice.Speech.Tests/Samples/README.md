# Acceptance sample set (local only)

Recorded voice commands are personal data, so they are not committed: `*.wav` and `samples.yaml` in this directory
are ignored by git. The sample-set tests are skipped until both `ru/` and `en/` have a set.

Record a set with the microphone and the game sound in your headphones:

```bash
dotnet run --project tools/PitVoice.Cli -- listen --lang ru --mic "<name from 'devices'>" --save-wav tests/PitVoice.Speech.Tests/Samples/ru/take.wav
```

Then describe every take in `<lang>/samples.yaml` (same `expect` format as the `tests:` section of a pack):

```yaml
samples:
  - file: take.wav
    say: "меняй шины и залей двадцать пять литров"
    expect: [{ intent: tires.all }, { intent: fuel.add, amount: 25, unit: liters }]
  - file: take-2.wav
    say: "какая сегодня погода"
    expect: reject
```

At least 30 takes per language, 5 or more of them unrelated phrases that must be rejected. Run:

```bash
dotnet test tests/PitVoice.Speech.Tests --filter Samples
```
