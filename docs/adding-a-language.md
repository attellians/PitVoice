# Adding a language

A language is one YAML file in `languages/<code>.yaml`. The parser, the response builder and the recognizer
vocabulary are all driven by that file. Code changes are needed only for two language-specific pieces: the number
parser and the plural rules (see the end of this document).

Validate your work at any time:

```bash
dotnet run --project tools/PitVoice.Cli -- validate
dotnet run --project tools/PitVoice.Cli -- parse --lang <code>        # interactive check
```

CI runs the same validation and the `tests:` section of every pack, so a pack that fails validation or whose
test phrase regresses fails the build.

With the pack's `asr_model` installed (`models download <id>`), `validate` also checks every pack word against the
model vocabulary. A word the model does not know can never be recognized by voice: replace it with a form the model
has (small Vosk models often lack rare imperatives; an infinitive such as `заправить` usually exists) or drop the
pattern. Patterns of intents that change the pit stop (fuel, tires, windshield, repair, `pit.clear`) need at least
two words: a single word such as "перед" or "front" is too easily heard in unrelated speech, and validation rejects
it. Only `status.fuel` and `assistant.cancel` may have one-word patterns. Write `е` instead of `ё` as usual; the recognizer grammar switches to the `ё` spelling when the model needs it.

## File layout

```yaml
language: ru                       # language code; must have a number parser in code
name: Русский                      # display name
version: 1
asr_model: vosk-model-small-ru-0.22    # model id from the models manifest (used from stage 2 on)
default_voice: piper-ru-dmitri-medium  # voice id (used from stage 4 on)
plural_rules: ru                   # plural rule set in code; defaults to `language`

units:                             # only `liters` and `gallons` are known
  liters:
    synonyms: [литр, литра, литров, литры, л]      # single words that mean this unit in speech
    forms: { one: литр, few: литра, many: литров }  # one form per plural category of `plural_rules`
  gallons:
    synonyms: [галлон, галлона, галлонов]
    forms: { one: галлон, few: галлона, many: галлонов }

conjunctions: [и, а также, плюс]   # split compound phrases; multi-word entries are allowed
fillers: [пожалуйста, ну]          # removed before matching; must not appear in patterns
negations: [не, без]               # fuzzy matching never drops, inserts or replaces these

intents:
  fuel.add:
    patterns:
      - "залей {amount} {unit?}"
      - "{amount} {unit} топлива"
    response: "заливаем {amount} {unit}"
  tires.none:
    patterns: ["не меняй шины", "без шин"]
    response: "шины не меняем"
  # ... one entry per intent of the catalog

responses:
  not_understood: "не понял, повтори"   # required
  not_connected: "нет связи с игрой"    # required
  not_in_car: "ты не в машине"          # required
  apply_failed: "команда не применилась" # required
  not_supported: "пока не умею"         # required
  cancelled: "отменил"

tests:
  - say: "залей тридцать пять литров"
    expect: [{ intent: fuel.add, amount: 35, unit: liters }]
  - say: "залей двадцать"
    expect: [{ intent: fuel.add, amount: 20 }]    # unit not checked: it comes from settings
  - say: "меняй шины и не меняй шины"
    expect: reject
```

## Intents

The catalog of intents is fixed in code (`IntentCatalog`, TZ §5). A pack cannot invent intents. A pack may leave
an intent out: the language then loads with a warning and phrases for that intent are rejected. The shipped `ru`
and `en` packs describe every intent and the pack tests enforce that.

| Intent | Meaning | Slots |
|---|---|---|
| `fuel.add` | add fuel; 0 removes refueling | `{amount}` required, `{unit}` or `{unit?}` |
| `fuel.none` | do not refuel | none |
| `tires.all` | change all four tires | none |
| `tires.left`, `tires.right` | change the left / right side | none |
| `tires.front`, `tires.rear` | change the front / rear axle | none |
| `tires.lf`, `tires.rf`, `tires.lr`, `tires.rr` | change one wheel: front left, front right, rear left, rear right | none |
| `tires.none` | change no tires (removes all four) | none |
| `tires.none.left`, `tires.none.right` | do not change the left / right side | none |
| `tires.none.front`, `tires.none.rear` | do not change the front / rear axle | none |
| `tires.none.lf`, `tires.none.rf`, `tires.none.lr`, `tires.none.rr` | do not change one wheel | none |
| `windshield.tearoff`, `windshield.none` | tear-off / no tear-off | none |
| `repair.fast`, `repair.none` | fast repair / no fast repair | none |
| `pit.clear` | clear every pit command; cannot be combined | none |
| `status.fuel` | fuel status; answered with `not_supported` until stage 8 | none |
| `assistant.cancel` | cancel, nothing is sent; cannot be combined | none |

Tire commands add wheels to what is already selected in the black box; the `tires.none.*` intents remove wheels. One
phrase cannot both add and remove the same wheel ("меняй левые и не меняй переднее левое" is rejected), while
different wheels combine ("меняй правые и не меняй левые"). Spell out every wheel and side the way drivers say it:
the recognizer only hears words that appear in some pattern.

## Patterns

A pattern is a sequence of words and slots separated by spaces. Everything the normalizer does to a spoken phrase
is also done to pattern words, so you can write them naturally:

- Case and punctuation do not matter; `ё` is the same as `е`.
- Number words inside a pattern become a number literal: `все четыре` matches "все четыре" and "все 4".
- Unit synonyms inside a pattern become a unit literal.
- Hyphens split words: `tear-off` is the same as `tear off`. Write `tearoff` as a separate pattern if you want the
  one-word variant too.

Slots:

| Slot | Matches |
|---|---|
| `{amount}` | one number 0..999 (words or digits) |
| `{unit}` | one unit synonym, required |
| `{unit?}` | one unit synonym or nothing; the default unit from settings is used when absent |

Alternatives are separate patterns; there is no `(a|b)` syntax. Three to six patterns per intent is a good range.

Matching rules you should keep in mind while writing patterns:

- The whole phrase is matched first. Only if nothing matches exactly is it split by conjunctions.
- Exact matches win. Among candidates with the same distance the longer pattern wins, so `не меняй шины`
  beats `меняй шины`.
- A phrase may differ from a pattern by one word (extra, missing or replaced) only when the pattern has three
  or more tokens. Slots and negation words are never part of such a difference, and a number or unit token is
  never dropped.
- If two different intents match equally well with a one-word difference, the phrase is rejected as ambiguous.
  Add an exact pattern for the phrase you want to the right intent.
- A pattern that normalizes to the same tokens as a pattern of another intent is a validation error.
- Filler words inside a pattern are a validation error: they would never be present at match time.

## Responses

`response` is the confirmation text of the intent. `{amount}` is replaced by digits and `{unit}` by the unit form
that agrees with the number according to `plural_rules`. Only `fuel.add` may use these placeholders. Responses of a
compound command are joined with commas in the order of speech. `responses.not_understood` is spoken for any
rejected phrase and is required.

The other required responses are spoken by execution outcome; a pack without any of them fails validation and the
language cannot be selected:

| Key | Spoken when |
|---|---|
| `not_connected` | the game is not running, nothing was sent |
| `not_in_car` | the game runs but the driver is not in the car, nothing was sent |
| `apply_failed` | the game did not confirm all commands within 1.5 s (not applied or partially applied) |
| `not_supported` | the phrase asks for something this version cannot do yet (`status.fuel`) |

`cancelled` is optional and not used by the code yet.

## Tests

Every intent of the pack must be expected by at least one test, and the pack should also contain negative tests:
foreign phrases, conflicts, numbers out of range. `expect` is either `reject` or a list of commands in the order
they are spoken. In an expected command only `intent` is required; `amount` and `unit` are compared only when
given. Tests run with the default settings: unit `liters`, confidence threshold 0.6.

Recommended checklist per intent: two or more positive phrases, including at least one with an extra word that
must still match (fuzzy) and, for negated intents, a phrase that must not be mistaken for the positive one.

## What needs code

Two pieces live in `src/PitVoice.Core` and are registered in `Languages/LanguageSupport.cs`:

1. **Number parser** (`Numbers/<Xx>NumberParser.cs`, implements `INumberParser`): turns a run of number words into
   an integer 0..999 and reports how many words it consumed. It must read greedily and stop at the first word
   that does not continue the number. It also exposes every word it understands through `Words`, which feeds the
   recognizer vocabulary. See `RuNumberParser` (gender forms) and `EnNumberParser` ("a hundred", "and").
2. **Plural rules** (`Plural/<Xx>PluralRules.cs`, implements `IPluralRules`): maps a number to a CLDR category and
   declares which categories the language produces. A unit in the pack must define a form for each of them.

Add table-driven tests for both in `tests/PitVoice.Core.Tests`, then write the pack. The `PitVoice.LanguagePacks.Tests`
project picks up any new `languages/*.yaml` automatically.
