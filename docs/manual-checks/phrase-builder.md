# Manual check: own phrases (`replace-additions-with-phrase-builder`)

The page "Commands" → "Own phrases" on a Release build (spec `phrase-builder`, the updated `language-packs`,
`command-parsing` and `command-reference`; TZ FR-LANG-5, FR-APP-8): opening and going back, rows of every command,
errors of rows, the check field, save and cancel, the leave dialog (menu, back arrow, tray "Exit"), the page without a
model and an additions file the page cannot show. Items that need the game use iRacing **Test Drive**.

Build once from the repository root in PowerShell (exit PitVoice from the tray menu first, the build replaces its files):

```
dotnet build PitVoice.sln -c Release -nodeReuse:false
dotnet build-server shutdown
Set-Alias pitvoice "$PWD\src\PitVoice.App\bin\Release\net10.0-windows\PitVoice.exe"
```

**Working profile.** Point settings, models and own phrases at the manual-run folders (`spike/` is ignored by git) and
start the app from the same PowerShell window, so it inherits the variables. Start with no file in
`spike\c1\languages`:

```
$env:PITVOICE_SETTINGS = "$PWD\spike\c1\settings.json"
$env:PITVOICE_MODELS_DIR = "$PWD\spike\c1\models"
$env:PITVOICE_USER_LANGUAGES = "$PWD\spike\c1\languages"
$additions = "$env:PITVOICE_USER_LANGUAGES\ru.yaml"
```

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. Defaults: commands
and interface language Russian, the ru model installed, window 1000 × 720.

**Who checks what.** The user checks only what needs a voice or the game: 3.4 and 3.5. Everything else is checked by
driving the Release build with screenshots and the additions file; such results say "checked by screenshot".

**The phrases.** «давай на мокрые» from the spec already works with the built-in words («давай» is a filler), so the page
shows "already works" for it and does not save it. The checks use «идет дождь» (Дождевые), which only an own phrase
gives, and «начнем заново» (Отмена всего).

## 1. Opening the page

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | no additions file | open "Commands" | the first card "Свои фразы": icon, "Своих фраз нет." without a green dot, "Настроить" on the right; no path, no "Открыть файл", no "Перечитать" | ok (2026-10-23, Release, checked by screenshot) |
| 1.2 | 1.1 | "Настроить" | the page "Команды / Свои фразы" with the back arrow, "Язык команд: Русский" under it; the menu item "Команды" stays selected; "Своих фраз пока нет.", "Добавить фразу"; the check card; "Отменить", "Сохранить" (disabled), no unsaved mark | ok (2026-10-23, Release, checked by screenshot) |
| 1.3 | 1.2 | the back arrow; then "Настроить" and the word "Команды" of the path | both go back to "Commands"; no dialog; no file is created in `spike\c1\languages` | |
| 1.4 | 1.2 | the window at its minimum width 760 px, then the light theme, then English interface | the rows and both cards fit without horizontal scrolling; the columns of a row shrink, the phrase is not cut; light theme readable; English texts ("Own phrases", "Set up", "Check", "Save", "Cancel") | ok (2026-10-23, Release, checked by screenshot): at 760 px a row goes to two lines (phrase, then command and variant), nothing cut; light theme readable; English texts |

## 2. Rows and their check

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | 1.2 | "Добавить фразу" | a row: an empty phrase box with focus, "Замена резины", "Все", the delete button; under it "Напишите фразу."; the unsaved mark appears | |
| 2.2 | 2.1 | open the command list, then the variant list for each command | commands "Заправка", "Замена резины", "Ремонт", "Чистка стекла", "Отмена всего"; Заправка: "Залить (сколько — скажу)", "Без топлива"; Замена резины: Все, Передние, Задние, Левые, Правые, four wheels, Сухие, Дождевые, Не менять; Ремонт, Чистка стекла, Отмена всего: the variant list is disabled and empty | |
| 2.3 | 2.1 | "Замена резины" — "Передние", then the command "Заправка" | the variant becomes "Залить (сколько — скажу)" | |
| 2.4 | 2.1 | type «почисти» with "Чистка стекла" | under the row: "Нужно хотя бы два слова: одно слово не меняет пит-стоп." | ok (2026-10-23, Release, checked by screenshot) |
| 2.5 | 2.1 | type «шины и стекло» with "Замена резины" — "Все" | "Во фразе не должно быть «и»: …" | ok (2026-10-23, Release, checked by screenshot) |
| 2.6 | 2.1 | type «не меняй резину» with "Все" | "Эта фраза уже означает «Шины не менять»." | ok (2026-10-23, Release, checked by screenshot) |
| 2.7 | 2.1 | type «меняй все шины» with "Все" | a warning (not an error): "Фраза уже работает, сохранять её не нужно." | ok (2026-10-23, Release, checked by screenshot) |
| 2.8 | 2.1 | type a made-up word, e.g. «шмыгздрязь резину» | "«шмыгздрязь» нет в модели распознавания, голосом фраза не сработает." | |
| 2.9 | two rows | «идет дождь» — "Дождевые" and «идет дождь» — "Сухие" | under the second row: "Фраза уже назначена другой команде: «Дождевые шины»." | |
| 2.10 | a row | the delete button | the row is gone; with no saved rows left the unsaved mark goes away | |

## 3. Check, save and cancel

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | a new row «плесни» — "Заправка" — "Залить (сколько — скажу)" | type «плесни двадцать литров» in "Проверка", "Проверить" (or Enter) | "заливаем 20 литров" with a green dot; nothing in "Последние фразы" on the home page; `$additions` does not exist | |
| 3.2 | 3.1 | check «кто впереди меня» | "не понял" without a dot | |
| 3.3 | 3.1, a second row «идет дождь» — "Дождевые", a third «меняй все шины» — "Все" | "Сохранить" | the unsaved mark goes away; the third row is gone; `Get-Content $additions` shows the header comment, `плесни` and `идет дождь`; "Commands": "2 фразы работают." with a green dot; Fuel words: "плесни" with "своё"; Tires words: "Свои фразы: «идет дождь» — Дождевые шины" with "своё"; neither in the chips | |
| 3.4 | 3.3, iRacing Test Drive | say «плесни двадцать литров» | 20 l added, the success cue (user) | ok (2026-10-23, Release, user): works |
| 3.5 | 3.3, Test Drive | say «идёт дождь» | wet tires set in the pit menu, the success cue (user) | ok (2026-10-23, Release, user): works |
| 3.6 | 3.3 | delete «плесни», add «начнем заново» — "Отмена всего", "Отменить" | the rows are «плесни», «идет дождь» again; no unsaved mark | |
| 3.7 | 3.3 | add «почисти» — "Чистка стекла", "Сохранить" | nothing is written (`$additions` unchanged), focus in the «почисти» box, "Исправьте ошибки в строках, потом сохраните." | |
| 3.8 | 3.3, a new row without errors | make the file read-only (`Set-ItemProperty $additions IsReadOnly $true`), "Сохранить"; then make it writable again | "Фразы не сохранились: …" on the page; the unsaved mark stays; the saved phrases keep working | |

## 4. Leaving with unsaved changes

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 4.1 | an unsaved row without errors | the menu item "Настройки" | the dialog "Сохранить свои фразы?" with "Сохранить", "Не сохранять", "Остаться"; the menu item "Команды" stays selected | ok (2026-10-23, Release, checked by screenshot): the dialog shows, "Команды" stays selected |
| 4.2 | 4.1 | "Остаться" | the page with the same draft; "Команды" selected | |
| 4.3 | 4.1 | "Сохранить" | the phrase is saved, "Настройки" opens | ok (2026-10-23, Release): saved, "Настройки" opened |
| 4.4 | an unsaved change | the back arrow, "Не сохранять" | "Commands" opens; "Настроить" shows the saved rows | |
| 4.5 | an unsaved row with an error | a menu item, "Сохранить" | the page stays, focus on the row with the error | |
| 4.6 | an unsaved change | close the window with the cross, open it from the tray | no dialog; the page and the draft are there | ok (2026-10-23, Release, checked by screenshot): the window hid, the process kept running |
| 4.7 | 4.6, the window hidden | tray menu "Выход" | the window shows itself with the dialog; "Остаться" keeps the app running; "Выход" again and "Не сохранять" exits; the file is unchanged | ok (2026-10-23, Release, checked by screenshot): the window showed with the dialog; "Остаться" kept the app and the draft; "Не сохранять" exited, the file unchanged |

## 5. No model, a file the page cannot show

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | `$env:PITVOICE_MODELS_DIR` at an empty folder, a saved row | open "Свои фразы" | the warning about the speech model on top; the rows can be edited; "Проверить" and "Сохранить" disabled, "Отменить" enabled | |
| 5.2 | the models folder back, the app just started (the model loads for a few seconds) | open "Свои фразы" at once and change a row while the model loads | the warning shows while loading, then goes away; the rows are checked, "Сохранить" is enabled, the draft stays | |
| 5.3 | `Set-Content $additions "lexicon:`n  tires: { targets: { front: [перёд] } }" -Encoding utf8`, restart | "Commands", then "Настроить" | the card: "Прежние свои слова не применены…"; the page: the message about the old words with "Начать заново", an empty list; «меняй перёд» is not understood | |
| 5.4 | 5.3 | "Начать заново", the back arrow, "Не сохранять", "Настроить" again | the file is unchanged; the message is shown again | |
| 5.5 | 5.3 | "Начать заново", add «идет дождь» — "Дождевые", "Сохранить" | the file has only the new phrase; the message is gone and does not come back on the next visit | |
| 5.6 | `Set-Content $additions "lexicon:`n  fuel: { action: [плесни] }" -Encoding utf8`, restart | open "Свои фразы" | no message; the row «плесни» — "Заправка" — "Залить (сколько — скажу)" | |
