# Manual check: refreshed cue sounds

The three cues are regenerated in the "soft sine" preset picked by ear (change `refresh-cue-sounds`): one soft timbre
with a light sub-bass, start is one note, success goes up a fourth (520 → 694 Hz), error goes down a fourth in a lower
register (312 → 234 Hz). Start lasts 150 ms, success and error 400 ms, each cut with a fade-out.

Build the CLI once and use the executable from the build output. From the repository root in PowerShell:

```
dotnet build tools/PitVoice.Cli -c Release
Set-Alias pitvoice-cli "$PWD\tools\PitVoice.Cli\bin\Release\net10.0\pitvoice-cli.exe"
```

Use a separate settings file so your own settings stay untouched: `$env:PITVOICE_SETTINGS = "$PWD\spike\settings-check.json"`.

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was heard. If the cues sound
noticeably drier than the preset in the sound designer, the wet level in `tools/cue-gen/Program.cs` may be tuned within
0.08–0.16 (design, risks) and the cues regenerated.

## 0. Recorded phrase set with the new start trim (task 2.1)

The recordings are made without a cue and run untrimmed in CI, so the pipeline's trim (start cue + 30 ms) was simulated
by dropping the start of every recording (2026-10-19, temporary change in `SampleSetTests`, reverted):

| Trim | Meaning | ru (65) | en (63) |
|---|---|---|---|
| 0 ms | as in CI | 62 correct, 0 false | 60 correct, 0 false |
| 140 ms | old start cue (110 ms) + 30 ms | 59 correct, **2 false** (take-025, take-026) | 60 correct, 0 false |
| 180 ms | new start cue (150 ms) + 30 ms | 62 correct, 0 false | 59 correct, 0 false |

Spread at nearby trims, same recordings: 120 ms — ru 60 (1 false, take-025), en 59; 160 ms — ru 62, en 60, no false;
200 ms — ru 61 (1 false, take-026), en 60. The recognizer reacts to the start offset by ±1–3 takes whatever the cue
sounds like; the en drop by one take at 180 ms is inside that spread (en 59–60 at every trim). Result: accepted, the set
is not worse with the new cue (121 vs 119 correct, false 0 vs 2).

## 1. Listening

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | headphones | `cues play` | start, success, error in this order; durations 150 / 400 / 400 ms printed; soft, no harsh highs, no click at the start or the end | ok (2026-10-19) |
| 1.2 | speakers | `cues play` | same; the low error notes are still clearly heard | ok (2026-10-19) |
| 1.3 | — | `cues play all --volume 50` | same cues, quieter, still easy to tell apart | ok (2026-10-19) |
| 1.4 | — | `cues play all --volume 100` | not louder or harsher than the old cues at 100 % | ok (2026-10-19) |
| 1.5 | PitVoice window, Settings | the cue preview button | the same three cues in the selected output device | ok (2026-10-19) |
| 1.6 | the sound designer open on the "soft sine" preset | play both one after another | the timbre matches the preset; the cut tails do not sound noticeably drier | ok (2026-10-19) |

## 2. Speaking right after the start cue

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | PTT assigned, `run` | press the PTT and start speaking right away, e.g. «меняй все шины» | the start cue sounds, the first word is recognized (`heard` line has it) | ok (2026-10-19) |
| 2.2 | same | a short command starting with a number word, e.g. «тридцать литров» | recognized without losing the first word | ok (2026-10-19) |
