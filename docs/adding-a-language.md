# Adding a language

A language is one YAML file in `languages/<code>.yaml`. The file lists the **words** of the language by class: which
words are fuel verbs, which name the tires, which name the front left wheel, and so on. Which command comes out of
which classes is decided by **rules in code**, the same for every language. So a new verb is one more word in a list,
and it works with every amount and every wheel at once. Code changes are needed only for two language-specific
pieces: the number parser and the plural rules (see the end of this document).

Validate your work at any time:

```bash
dotnet run --project tools/PitVoice.Cli -- validate
dotnet run --project tools/PitVoice.Cli -- parse --lang <code>        # interactive check
```

CI runs the same validation and the `tests:` section of every pack, so a pack that fails validation or whose
test phrase regresses fails the build.

With the pack's `asr_model` installed (`models download <id>`), `validate` also checks every word against the model
vocabulary and names the class it is used in. A word the model does not know can never be recognized by voice: replace
it with a form the model has (small Vosk models often lack rare imperatives; an infinitive such as `заправить` usually
exists) or drop it. Write `е` instead of `ё` as usual; the recognizer grammar switches to the `ё` spelling when the
model needs it. Every word of the pack goes into the recognizer grammar, and every word there is one more thing the
recognizer can mistake a sound for: list the words drivers really say, not every synonym of the dictionary.

## File layout

This is a complete, valid pack (it is also a test fixture, `tests/PitVoice.Core.Tests/Fixtures/docs-example.yaml`):

```yaml
language: ru                       # language code; must have a number parser in code
name: Пример
version: 2
asr_model: vosk-model-small-ru-0.22    # model id from the models manifest
plural_rules: ru                   # plural rule set in code; defaults to `language`

units:                             # only `liters` and `gallons` are known
  liters:
    synonyms: [литр, литра, литров]                 # single words that mean this unit in speech
    forms: { one: литр, few: литра, many: литров }  # one form per plural category of `plural_rules`
    short: л                                        # abbreviation for short responses

conjunctions: [и, а также]         # split compound phrases; multi-word entries are allowed
fillers: [пожалуйста, ну]          # removed before parsing; must not appear in the lexicon
negations: [не, без]               # turn a command into its removal
reject_words: [на]                 # reject the whole phrase ("меняем резину на слики")

lexicon:
  fuel:
    action: [залей, долей]
    object: [топливо, топлива]
  tires:
    action: [меняй, замени]
    object: [шины, шин]
    keep: [трогай]                 # only with a negation: "не трогай шины"
    targets:
      all: [все]
      front: [передние]
      lf: [переднее левое, левое переднее]

responses:
  fuel.add: "заливаем {amount} {unit}"
  fuel.none: "без топлива"
  tires.all: "меняем все шины"
  tires.front: "меняем передние"
  tires.lf: "меняем переднее левое"
  tires.none: "шины не меняем"
  tires.none.front: "передние не меняем"
  tires.none.lf: "переднее левое не меняем"
  not_understood: "не понял, повтори"    # required
  not_connected: "нет связи с игрой"     # required
  not_in_car: "ты не в машине"           # required
  apply_failed: "команда не применилась" # required

short_responses:                   # result chip in the window, one per command of `responses`
  fuel.add: "+{amount} {unit}"
  fuel.none: "без топлива"
  tires.all: "все шины"
  tires.front: "передние"
  tires.lf: "переднее левое"
  tires.none: "без шин"
  tires.none.front: "без передних"
  tires.none.lf: "без переднего левого"

tests:
  - say: "залей тридцать пять литров"
    expect: [{ intent: fuel.add, amount: 35, unit: liters }]
  - say: "долей двадцать"
    expect: [{ intent: fuel.add, amount: 20 }]    # unit not checked: it comes from settings
  - say: "без топлива"
    expect: [{ intent: fuel.none }]
  - say: "меняй шины"
    expect: [{ intent: tires.all }]
  - say: "замени передние"
    expect: [{ intent: tires.front }]
  - say: "левое переднее"
    expect: [{ intent: tires.lf }]
  - say: "не трогай шины"
    expect: [{ intent: tires.none }]
  - say: "передние не меняй"
    expect: [{ intent: tires.none.front }]
  - say: "не меняй переднее левое"
    expect: [{ intent: tires.none.lf }]
  - say: "меняй шины и не меняй шины"
    expect: reject
```

The pack has no windshield, repair and clear words, so it loads with a warning that it is incomplete, and phrases about
the windshield are rejected in this language. The shipped `ru` and `en` packs have every command.

## Word classes

| Group | Classes | Example words (ru / en) |
|---|---|---|
| `fuel` | `action`, `object` | залей, долей / add, give me; топливо / fuel |
| `tires` | `action`, `object`, `keep`, `targets`, `compounds`, `into` | меняй, замени / change, swap; шины / tires; трогай; дождевые / wets; на / to |
| `windshield` | `action`, `object`, `keep` | сорви, протри / tear off, clean; плёнку / windshield |
| `repair` | `action`, `object`, `fast` | почини / fix; машину, ремонт / car, repair; быстрый / fast |
| `clear` | `action`, `object` | сбрось, отмени, отменить, удали, сотри, отмена; снимай, снять (shared with the windshield) / clear, reset; всё, все команды / all, everything |

Tire `targets` are `all`, `left`, `right`, `front`, `rear` (sides and axles) and `lf`, `rf`, `lr`, `rr` (front left,
front right, rear left, rear right). A pack may leave a group, a class or a target out; the commands that need it
are then not available in this language, and validation says which.

Tire `compounds` are `dry` and `wet`; another name (`intermediate`) is an error with the file and the line. `into` is
the word that leads to a compound ("на" of "меняем резину на дождевые", "to" of "change tires to wets"). Without
compound words the pack has no `tires.dry` and `tires.wet`, and validation warns that it is incomplete.

```yaml
  tires:
    compounds:
      dry: [сухие, слик]
      wet: [дождевые, мокрые]
    into: [на]
```

A compound word must be in the model too. `vosk-model-small-ru-0.22` knows "слик" but not "слики": the recognizer
hears "на слики" as "на слик" or "на слик и" (the dangling conjunction is dropped), so the pack lists "слик" only.

`into_homophones` (optional, top level) lists words that sound like the `into` word: the English pack has
`into_homophones: [two]`, because the recognizer cannot tell "to" from "two" and hears "change to slicks" as "change two
slicks". In a piece with a compound such a one-word number right before the compound word is read as `into`, and its
recognition confidence does not count; anywhere else it stays a number ("add two liters"). A homophone must be one
word, the pack must have `tires.into`, and a homophone cannot be a lexicon word, a conjunction, a filler, a negation or
a reject word.

An entry is one word or a fixed sequence of words: "переднее левое", "give me", "tear off". List every form drivers
use as its own entry ("меняй", "меняем", "поменяй"): there are no stems or patterns, so "мен" never matches "меньше".
Write both orders of a wheel name ("переднее левое", "левое переднее"). An entry is normalized like a spoken phrase:
case, punctuation and `ё` do not matter, and a number word inside it ("все четыре") matches the spoken number too.

Which forms to list: the shipped `ru` pack gives every action its imperative ("залей"), its we-form ("заливаем",
"добавим") and its infinitive ("залить"), and a noun of the action where drivers say one ("замена", "смена",
"дозаправку"; "новые" and "свежие" work as a tire action too: "свежую резину"). A form goes in only if the model has
it (`validate` warns about the others: "заправь", "переобуй", "доливай" and "слики" are not in
`vosk-model-small-ru-0.22`), if it does not sound like a word of the opposite meaning ("ставь" is close to "оставь"),
and if it is no noise word that drivers use in questions ("бензин", "горючего"). Mixing up two forms of one action is
safe: "залить" for "залей" gives the same command.

The same word may be in several groups: "смени" changes tires and the windshield film, "все" is a tire target and
what "сбрось всё" clears, "repair" is the action and the object of repair. The parser picks the group from the other
words of the command, and rejects the phrase when both readings give a command ("сбрось все шины"). "сними",
"снимай", "снимаем" and "снять" are windshield and clear verbs: "снимай плёнку" tears off the film, "снимай всё"
clears the pit stop, "снимай все шины" is rejected, and "снимаем" alone is no command.

Validation errors, each with the file and line:

- a group, class or target the format does not have;
- an entry with a slot (`{amount}`) or digits: numbers and units are recognized by themselves;
- an entry that contains a filler (it is removed before parsing), a conjunction (it splits the phrase first), a unit
  word or a negation, or that is a number alone;
- the same word in `action` and `keep` of one group: "не трогай" keeps, so "трогай" alone cannot be a verb.

A word repeated in one class is skipped with a warning.

## Rules

The rules are in code (`WordRules`) and the same for every language. Word order inside a command is free: "меняй
шины" and "шины меняй" are the same.

| Command | Words it needs |
|---|---|
| `fuel.add` | a number and a fuel `action` or `object`; the unit is optional (default from settings) |
| `fuel.none` | a negation and a fuel `action` or `object`, no number |
| `tires.<target>` | targets with a tire `action` or `object` ("меняй передние", "задние шины"), or one target of two or more words alone ("переднее левое"); `action` + `object` without a target is `tires.all` |
| `tires.none.<target>` | a negation and targets ("не меняй левые", "leave the rears"); a negation and the tire `object` without a target is `tires.none` |
| `tires.dry`, `tires.wet` | a compound with a tire `action`, `object`, `into` or the target `all` ("меняем резину на дождевые", "дождевые шины", "на сухие"); selects the compound and all four tires |
| `windshield.tearoff` | a windshield `action` and `object`, or an `action` of two or more words ("tear off") |
| `windshield.none` | a negation and a windshield `action`, `keep` or `object` |
| `repair.fast` | a repair `object` with an `action` or `fast` ("почини машину", "быстрый ремонт") |
| `repair.none` | a negation and a repair `action` or `object` |
| `pit.clear` | a clear `action` and `object`; cannot be combined with other commands |

More rules worth knowing:

- **Two words at least.** Every rule needs two words, so one word never changes the pit stop, whatever the lexicon
  and the user's additions contain. ("кто впереди меня" was once heard as "перед" and changed the front tires.)
- **Several targets per action.** "меняй переднее левое заднее правое" gives two wheels, "меняй левые и передние"
  the left side and the front axle. Multi-word targets are read by the longest match; a part of one ("меняй правую")
  and a phrase that can be cut into targets in two ways ("left front right") are rejected. A side and an axle in a row
  without "и" ("меняй левые передние") are rejected too: it may be the front left wheel heard in the wrong forms.
- **Word forms.** `word_forms` lists forms the recognizer swaps freely ("передние" and "переднее"). A form said alone
  is its word ("меняй переднее" is the front axle), but a wheel name matches only as written ("переднее левое"). When
  a phrase is rejected for low confidence because of such a word, the recognizer is asked once more with its list of
  alternatives: if they differ only by the forms of that word, the word counts as certain.
- **One action, once.** The same action twice without a conjunction ("меняй правые меняй левые") is rejected: that is
  what is left when the recognizer lost "и не".
- **The tire action after a conjunction may be left out.** "меняй левые и передние", "не меняй левые и переднее
  правое": a part with only tire targets (and maybe the tire object) takes the action of the tire command before it,
  along a chain of parts. This works only for tires: "залей двадцать и передние" is rejected.
- **A lost conjunction.** "меняй шины залей двадцать пять литров" is cut into parts by the actions of different
  groups, using the cut with the fewest parts; when two cuts are equally good, the phrase is rejected.
- **Whole numbers only.** The game takes whole liters; a fraction ("тридцать точка два", "30.2", "тридцать и два
  литра") is rejected and never rounded. Two separate numbers in one command are rejected.
- **One unknown word.** A part of three or more words may contain one word that is in no entry ("меняй все эти шины"),
  with match quality 0.75; a word that is only a piece of a longer entry does not count as unknown, and an unknown word
  next to a number rejects the part ("add one thousand liters" is not 1 liter).
- **A compound is for all four tires.** The game sets a compound for the whole car, so a compound with another target
  ("меняй левые на дождевые"), with a negation or `keep` ("не меняй на дождевые"), with a second compound or with a
  number is rejected. One compound word alone ("дождевые") is not a command.
- **`into` needs a compound.** A piece with `into` and no compound ("меняем резину на") rejects the whole phrase: the
  recognizer may drop a compound word it did not catch, and the phrase would change all tires on the old compound.
  When it drops "на" too ("меняем резину на слики" heard as "меняем резину"), the rule cannot see it: say "на слики"
  without "меняем резину".
- Conflicts: adding and removing the same wheel, a command and its removal, `pit.clear` with anything else, the two
  compounds, a compound with any tire removal ("меняй на дождевые и не меняй левые"). A compound with a tire addition
  is no conflict: the wheels merge. Repeated commands collapse; of several `fuel.add` the last one wins.

## Reject words

`reject_words` lists words that reject the whole phrase: what drivers say when they ask for something PitVoice cannot
do, such as intermediate tires, and words the recognizer would otherwise drop without a trace: "тридцать точка два"
would be heard as "тридцать два", "кто впереди меня" as "передние меняй".

```yaml
reject_words: [точка, кто, впереди]
```

The recognizer only writes words of the pack, and a phrase tail without such words is dropped without a trace:
"кто впереди меня" would be heard as some command words with full confidence. With the words in `reject_words` the
recognizer hears them, and the phrase is rejected. (A lost compound after "на" is caught by `into`: "на" without a
compound rejects the phrase.) Reject words go into the recognizer grammar, so list words the model knows (`validate`
warns about the others) and that do not sound like command words: short words such as "on" are heard as "one". A
reject word must not be a word of any command (a lexicon entry, a compound, `into`, a conjunction, a negation, a
filler, a unit or a number word); validation reports it with the class.

## Responses

`responses` holds the confirmation text of every command the pack can give, by intent id, and the outcome texts.
`{amount}` is replaced by digits and `{unit}` by the unit form that agrees with the number according to
`plural_rules`; only `fuel.add` may use them. Responses of a compound command are joined with commas in the order of
speech. A command the lexicon can give without a response is a validation error; a response key that is neither an
intent nor an outcome is an error too.

| Key | Used when |
|---|---|
| `not_understood` | the phrase was rejected |
| `not_connected` | the game is not running, nothing was sent |
| `not_in_car` | the game runs but the driver is not in the car, nothing was sent |
| `apply_failed` | the game did not confirm all commands within 1.5 s (not applied or partially applied) |
| `no_dry_tires`, `no_wet_tires`, `several_dry_tires` | the car has no compound of that kind, or several dry ones; nothing was sent. Required when the pack has compound words |

`not_supported` and `cancelled` of format 1 are not used any more and give a warning.

## Short responses

`short_responses` holds a short text of every command the pack can give, by intent id: "Recent phrases" in the window
shows it in the result chip of an applied phrase. The short responses of a compound command are joined with " · " in
the order of speech ("все шины · +25 л"); keep each one to a word or two. `{amount}` is replaced by digits and `{unit}`
by the unit's `short` abbreviation, the same for every number ("+25 л", "+12 gal"). "Add zero" shows the short response
of `fuel.none`, as the full response does. A command the lexicon can give without a short response, a unit without
`short`, a key that is not an intent and a placeholder the intent does not provide are validation errors.

## Tests

Every command the pack can give must be expected by at least one test (the shipped packs have two or more), and the
pack should also contain negative tests: foreign phrases, single words, conflicts, fractions. `expect` is either
`reject` or a list of commands in the order they are spoken. In an expected command only `intent` is required;
`amount` and `unit` are compared only when given. Tests run with the default settings: unit `liters`, confidence
threshold 0.6.

## Format 1

Format 1 had phrase templates per intent (`intents.<id>.patterns`) and the field `default_voice`. A pack with either
does not load: the error names the field. Rewrite the templates as words of their classes.

## Own words (user additions)

A user adds own phrases on the window's page "Commands" → "Own phrases" → "Set up". Each row is a phrase, a command
and its variant: a fuel verb ("Refuel" → "Add fuel (I say how much)") or a ready phrase for one of the commands of the
page (no fuel, every tire change of the reference, dry and wet compounds, no tire change, repair, windshield, clear
all). The page checks every row as it is typed, with the same check as below, and parses a typed phrase with the
unsaved rows in its "Check" field. Nothing applies until "Save".

The page keeps the rows in `%AppData%\PitVoice\languages\<code>.yaml`. The format is internal: the application writes
the file (into a temporary file next to it, then replaces it) and nobody is expected to edit it. The directory can be
changed with `PITVOICE_USER_LANGUAGES`, which tests and tools such as `pitvoice-cli` use:

```yaml
# Managed by PitVoice: Commands → Own phrases. Changes made here by hand may be lost.
language: ru
lexicon:
  fuel: { action: ["плесни"] }
phrases:
  - { text: "идет дождь", intent: tires.wet }
```

"плесни тридцать литров" then works like "залей тридцать литров": a fuel verb is a word of `fuel.action` and goes with
every amount and unit of the pack. A ready phrase gives its command when a piece of the phrase (between conjunctions,
without fillers) is exactly the phrase, so "идет дождь и залей двадцать литров" works too; a piece with another word is
parsed by word classes as usual. The confidence of the piece and the conflict rules apply to it like to any part.

A fuel verb is checked by the word rules above and must give "‹verb› twenty liters" as a fuel amount; a negation is an
error that names both classes. A ready phrase needs two words or more without fillers, no digits, braces, conjunctions
or reject words, and a command the pack has. A phrase the built-in words read as another command (or as two) is an
error that names it; a phrase that already gives the same command, and a repeat, are skipped with a warning. Any error
leaves the whole file out and the built-in pack is used. A file the page cannot show is not applied either: invalid
YAML, the old `intents:` section, words of any class other than `fuel.action`, or a command the page does not have.
The page then shows an empty list and offers "Start over"; the file stays as it is until a save replaces it. A file
for a language without a built-in pack (e.g. `de.yaml`) is ignored with a warning.

Fuel verbs and the words of ready phrases go into the recognizer vocabulary. On the page a word the model does not
know is an error of its row; when a file is loaded it is a warning: the phrase still works when typed
(`pitvoice-cli parse`), but cannot be recognized by voice.

`pitvoice-cli parse`, `listen`, `recognize`, `send` and `run` load the additions and print their problems;
`validate` checks the additions files too, when they exist, and says when the page cannot open one.

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
