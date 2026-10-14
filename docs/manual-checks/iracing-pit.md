# Manual check: iRacing pit integration

Run in iRacing **Test Drive** with any car.

`pitvoice-cli` is not installed on `PATH`; build it once and use the executable from the build output. From the
repository root in PowerShell:

```
dotnet build tools/PitVoice.Cli -c Release
Set-Alias pitvoice-cli "$PWD\tools\PitVoice.Cli\bin\Release\net10.0\pitvoice-cli.exe"
```

The alias lives only in that PowerShell window; set it in every terminal you use. Keep a recording running in a
second terminal for the whole session, so every mismatch can be turned into a replay test. `spike/` is ignored by
git, so the raw recording is not committed by accident:

```
pitvoice-cli record --out spike/iracing-check-<date>.jsonl
```

Use `pitvoice-cli status` to look at the state and `pitvoice-cli send --lang ru "<phrase>"` (or `--lang en`) to
execute a phrase. `send` exits with 0 only when the outcome is "applied". Before each tire or fuel item, set the
starting state in the black box by hand or with an earlier item, as given in the **Setup** column.

Mark every item **ok** or **mismatch** and write what was seen. A mismatch becomes a fix of a predicate or a state
rule with a test (task 10.2); characteristic recordings go to `tests/PitVoice.IRacing.Tests/Recordings/` (task 10.3).

Both PitVoice and iRacing run **without** administrator rights unless an item says otherwise.

## 1. Connection states

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | iRacing not started | `status` | `game not running`, exit code 0 | ok |
| 1.2 | iRacing not started | `send --lang ru "залей двадцать литров"` | outcome `no connection`, response «нет связи с игрой», exit code ≠ 0, nothing sent | ok |
| 1.3 | iRacing not started | `send --lang ru "отмена"` | outcome `applied`, response «отменил», exit code 0 | ok |
| 1.4 | iRacing UI (member site / menu), sim not loaded | `status` | `game not running` | ok: only iRacingUI running, sim not loaded |
| 1.5 | Test Drive loaded, in the garage | `status` | `game running, driver not in the car` | ok |
| 1.6 | in the garage | `send --lang ru "меняй шины"` | outcome `not in the car`, response «ты не в машине», nothing sent | ok |
| 1.7 | clicked **Drive**, car on pit road / track | `status` | `driver in the car`, wheels, flags, fuel to add and tank level shown | ok |
| 1.8 | car parked in the pit box | `status` | `driver in the car` (note what `IsOnTrack` gives in the box) | ok: IsOnTrack stays true in the pit box |
| 1.9 | back to the garage (Esc / tow) | `status` | `game running, driver not in the car` | ok |
| 1.10 | `status --watch` running, driver in the car | close iRacing | within 1 s: `game not running`; `--watch` keeps running | partly: state went to not running at 20:33:20; --watch was started after closing, so the 1 s bound was not timed |
| 1.11 | `status --watch` still running | start iRacing and Test Drive again | state comes back without restarting PitVoice | ok: picked up at 20:35:56 without restart |
| 1.12 | Test Drive paused in a frozen state (alt-tab while loading, debugger, etc.) if it can be reproduced | `status` after 1.5 s | `game not running` while the tick does not move; returns when it moves | not checked (could not reproduce) |
| 1.13 | driver in the car; select left tires and 20 l in the black box by hand | `status` | wheels `LF+LR`, fuel `on, 20 l to add` | ok (seen via status after selecting by hand) |
| 1.14 | `record` started before iRacing | start iRacing, select left tires by hand, stop with Ctrl+C | file starts with `NotRunning` lines, then picks up the game; a line after the selection has `pitSvFlags` 5 | ok |

## 2. Tires: accumulating selection

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | no wheel selected | `send "меняй переднее левое"` | only LF selected, outcome `applied` | ok |
| 2.2 | only LF selected | `send "меняй левую сторону"` | LF and LR | not checked |
| 2.3 | no wheel selected | `send "меняй левые и меняй передние"` | LF, LR and RF | not checked in the game; the phrase without the second verb is rejected (see design.md) |
| 2.4 | LF pressure set to 170 kPa by hand | `send "меняй переднее левое"` | LF selected, pressure still 170 kPa | ok: pressures stayed 152 kPa |
| 2.5 | no wheel selected | `send "меняй шины"` | all four | ok |
| 2.6 | no wheel selected | `send --lang en "front left tire"` | only LF | ok |
| 2.7 | only LF selected | `send --lang en "right side tires"` | LF, RF, RR | ok |

## 3. Tires: removing from selection

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | all four selected | `send "не меняй переднее левое"` | RF, LR, RR | ok |
| 3.2 | LF, LR, RF selected | `send "не меняй левые"` | only RF | ok |
| 3.3 | only RR selected | `send "не меняй переднее левое"` | only RR, outcome `applied` | ok |
| 3.4 | LF and RF selected | `send "не меняй шины"` | nothing selected | ok |
| 3.5 | only LF selected | `send "меняй правые и не меняй левые"` | RF and RR | ok |
| 3.6 | all four selected | `send --lang en "no left side tires"` | RF and RR | ok |
| 3.7 | any | `send "меняй левые и не меняй переднее левое"` | rejected as conflict, nothing sent | ok |

## 4. Fuel

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 4.1 | refueling off | `send "залей тридцать литров"` | refueling on, 30 l | ok: telemetry shows 30.000002 l |
| 4.2 | refueling on, 30 l | `send "залей тридцать литров"` | outcome `applied` (already in the requested state) | ok |
| 4.3 | any | `send --lang en "add ten gallons"` | 38 l in the black box, response «adding 10 gallons» | ok: 38 l |
| 4.4 | small tank (e.g. a car with ≤ 110 l); `status` shows the tank capacity | `send "залей двести литров"` | `sent: Fuel(200)`; the black box shows the tank capacity (`min(200, capacity)`); outcome `applied`, response without any capping message | ok: game capped 200 l to 96 l; rechecked with tank capacity from session info (30 → 38 → 96 → 96 → 50 l), recording `fuel-38-to-200-with-capacity` |
| 4.5 | refueling on, 30 l | `send "залей ноль литров"` | refueling off | ok (response fixed to «без топлива») |
| 4.6 | refueling on | `send "без топлива"` | refueling off | ok: flag off, amount kept |

## 5. Other pit commands (TZ §8)

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | nothing selected | `send "протри стекло"` | tear-off on | ok |
| 5.2 | tear-off on | `send "стекло не трогай"` | tear-off off | ok |
| 5.3 | fast repair available | `send "быстрый ремонт"` | fast repair on | not checked: no fast repairs in this session |
| 5.4 | fast repair on | `send "без ремонта"` | fast repair off | not checked |
| 5.5 | nothing selected | `send "протри стекло и быстрый ремонт"` | both on, sent in this order | not checked as one phrase (WS ok in 5.1; no fast repairs left) |
| 5.6 | tires, fuel and tear-off selected | `send "сбрось всё"` | nothing selected | ok |
| 5.7 | no fast repairs left (a session with 0 fast repairs, or after using them) | `send "быстрый ремонт"` | outcome `not applied`, response «команда не применилась», log line with phrase, sent, expected and actual | mismatch → fixed: game flashes the FR flag for ~31 ms; confirmation now requires the state to hold 100 ms |
| 5.8 | fast repairs left = 0 | `send "меняй шины и быстрый ремонт"` | outcome `partially applied`, unconfirmed `repair.fast`, tires stay selected, FR sent once | ok |
| 5.9 | driver in the car | `send "сколько топлива"` | outcome `not supported yet`, response «пока не умею», nothing sent | ok |
| 5.10 | driver in the car | `send "залей двадцать и сколько топлива"` | refueling 20 l, outcome `applied`, response «заливаем 20 литров» | ok |
| 5.11 | driver in the car | `send "меняй левые и залей десять литров"` | LF, LR and 10 l; response «меняем левые, заливаем 10 литров» | ok |
| 5.12 | driver in the car | `send --lang en "change tires and add twenty five liters"` | all four and 25 l; response «changing all tires, adding 25 liters» | ok |

## 6. Connection lost and rights

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 6.1 | driver in the car | run `send "меняй шины"` and close iRacing within a second | outcome `not applied` | not checked (covered by replay of leaving the car) |
| 6.2 | iRacing started **as administrator**, PitVoice not | `send "меняй шины"` | outcome `not applied`; a `warn:` line with the hint about different rights; no elevation prompt | not checked |
| 6.3 | both without administrator rights | `status` | state and black box are read | ok |

## Findings of the run on 2026-10-14

Test Drive, recording `spike/iracing-check-2.jsonl` (5901 records); characteristic pieces are in
`tests/PitVoice.IRacing.Tests/Recordings/` with replay tests.

- The game applies a command within one or two telemetry frames (~30 ms). `ClearTires` followed by wheels may show an
  intermediate frame with no or some wheels selected.
- With no fast repairs left, the game sets the fast repair flag for one or two frames (~31 ms) and then clears it.
  A confirmation that accepted any frame reported "applied"; it now requires the expected state to hold for 100 ms.
- `PitSvFuel` is a float with noise (30.000002 for 30 l); the +0.5 l tolerance covers it.
- Fuel above what fits is capped by the game to the tank capacity, not to the free volume: 200 l → 96 l with 61 l in
  a 96 l tank (`DriverCarFuelMaxLtr: 96.000`, `DriverCarMaxFuelPct: 1.000`). Clearing refueling keeps the last amount.
- A pit stop clears the black box selection after service: accumulated tires apply until the next stop.
- `IsOnTrack` stays true in the pit box.
- Fuel confirmation accepted any amount `0 < amount <= sent`, so a smaller amount left from before (38 l before
  "200 l") counted as confirmed before the command arrived. Fixed: with the tank capacity read from session info the
  expected amount is exactly `min(sent, capacity)`; `status` shows the capacity next to the tank level.

## Notes

- Write down anything unexpected about `PitSvFlags` timing (how fast the flags follow a command) and about
  `IsOnTrack` in the pit box: the confirmation timeout is 1.5 s and "in the car" relies on `IsOnTrack`.
- Keep the recording file of each session; pick 3–5 short characteristic pieces for replay tests: tire selection,
  removing wheels, fuel above the tank size, leaving the car.
