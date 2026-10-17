# Acceptance sample set (local only)

Recorded voice commands are personal data, so they are not committed: `*.wav` and `samples.yaml` in this directory
are ignored by git. The sample-set tests are skipped until both `ru/` and `en/` have a set.

The phrases to record are committed: `script.ru.yaml` and `script.en.yaml` (the format of `samples.yaml` without
`file`). They cover every command of the pack, the verbs, both orders of the wheel names, free word order, several
targets, the verb said once after "и"/"and", a lost conjunction, a conflict, a fraction and unrelated phrases that
must be rejected. `dotnet test tests/PitVoice.Speech.Tests --filter Script` checks that every phrase parses as text
into its `expect`, so a take can only fail on recognition.

## Recording

Move an old set out of the way first (the set of 2026-10-14 goes to `spike\samples-2026-10-14\`). Then record with the
microphone you race with and the game sound in your headphones, in short batches:

```bash
dotnet run --project tools/PitVoice.Cli -- listen --lang ru --mic "<name from 'devices'>" --script tests/PitVoice.Speech.Tests/Samples/script.ru.yaml --save-wav tests/PitVoice.Speech.Tests/Samples/ru
```

For every phrase: Enter starts the recording, Enter stops it; the recognized words and the parse result are printed.
Then Enter saves `take-NNN.wav` (NNN is the number of the phrase in the list) and appends its entry to `samples.yaml`,
R records again, S skips the phrase, Q quits. Continue a session with `--from <number>`: takes already saved are
never overwritten. Without `--save-wav` the command stops before the microphone opens. Audio is written only this way,
to the directory given on the command line.

`samples.yaml` then looks like this (written by the command, same `expect` format as the `tests:` of a pack):

```yaml
samples:
  - file: take-001.wav
    say: "залей тридцать литров"
    expect: [{ intent: fuel.add, amount: 30, unit: liters }]
  - file: take-052.wav
    say: "кто впереди меня"
    expect: reject
```

At least 50 takes per language, 8 or more of them phrases that must be rejected. Run:

```bash
dotnet test tests/PitVoice.Speech.Tests --filter Samples
```

The test needs 90% correct takes and no false command (a different command than expected, or a command where a
rejection was expected).
