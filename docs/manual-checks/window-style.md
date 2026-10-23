# Manual check: the look of the window (`refresh-window-visual-style`)

The restyled window on a Release build (spec `window-visual-style`, TZ FR-APP-12 and the updated FR-APP-6, FR-APP-7,
FR-APP-8): both themes and a Windows theme switch on the fly, both interface languages, every state of the home page,
downloading and deleting a model, the reference in ru and en, long device names, the minimum width, two DPI scales and
the idle CPU against 0.1.3. Items that need the game use iRacing **Test Drive**.

Build once from the repository root in PowerShell (exit PitVoice from the tray menu first, the build replaces its files):

```
dotnet build PitVoice.sln -c Release -nodeReuse:false
dotnet build-server shutdown
Set-Alias pitvoice "$PWD\src\PitVoice.App\bin\Release\net10.0-windows\PitVoice.exe"
```

**Working profile.** Point settings, models and own words at the manual-run folders (`spike/` is ignored by git) and
start the app from the same PowerShell window, so it inherits the variables:

```
$env:PITVOICE_SETTINGS = "$PWD\spike\c1\settings.json"
$env:PITVOICE_MODELS_DIR = "$PWD\spike\c1\models"
$env:PITVOICE_USER_LANGUAGES = "$PWD\spike\c1\languages"
```

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. A remark about the
look is either fixed or written down as a decision in the Result column. Defaults: theme "as in Windows", interface
language Russian, window 1000 × 720.

**Who checks what** (to shorten the run, 2026-10-19). The user checks only what needs a voice, the hardware or a Windows
setting: 1.3 (Windows theme on the fly), 1.5 (Windows accent), 3.3 (rest), 3.4 (phrases), 3.6 (unplugged wheel), 4.4 (no
network), 7.2 (display scale 150 %). Everything else is checked by driving the Release build with screenshots and the
settings file; such results say "checked by screenshot".

## 1. Themes

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | Windows in the dark theme, settings theme "as in Windows" | open the window, look at the menu | six items with six hues: Home blue, Settings grey, Overlay lilac (readable on its tint), Models green, Commands peach, About teal; icons on a soft tint, no white icons on solid fills; the active item has a background and no coloured bar on its left, the others none | ok (2026-10-19, Release, checked by screenshot): six hues, icons on tints, the active item has a background and no bar |
| 1.2 | 1.1 | Settings → Theme → Light | the whole window switches at once: hues darker, tints lighter, text readable on every tint, cards white-ish with no coloured frames; then set Dark and back to "as in Windows" | ok (2026-10-19, Release, checked by screenshot): every page in the light theme readable; dark → light → dark switches the hue set each time (a bug of the second switch was fixed on the way) |
| 1.3 | theme "as in Windows" | Windows Settings → Personalization → Colors → switch the Windows mode light ↔ dark while the PitVoice window is open | the window follows within a second without a restart: hues, tints, cards and the status card change together, no grey patches; switch back | ok (2026-10-19, Release, user) |
| 1.4 | any theme | look at every page | cards of one tone, 12 px corners, rows inside a card divided by thin lines, no card inside a card | ok (2026-10-19, Release, checked by screenshot) |
| 1.5 | Windows accent colour set to something other than blue (e.g. peach) | look at the primary buttons, the switch on Overlay and the sliders, in both themes | all blue, nothing in the Windows accent colour | ok (2026-10-19, Release, user) |
| 1.6 | any | press the menu button left of the app icon in the title bar, twice | the menu collapses to the section icons and expands again; the window still moves by dragging the title bar; after a restart the menu is expanded | ok (2026-10-19, Release, checked by screenshot): collapses to icons and back; dragging the title bar next to the button moved the window by 80 × 50 px; expanded after a restart |

## 2. Interface language

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | Russian | open every page | all new texts are Russian: status card, tiles, counter, "Язык команд: Русский", group descriptions, "Слова группы", model rows ("используется", "установлена 19.10.2026"), settings groups "Язык и вид" … "Диагностика" | ok (2026-10-19, Release, checked by screenshot) |
| 2.2 | Settings → Interface language → English | open every page | the same texts in English, dates as "10/19/2026", "Words of the group", no Russian left; the window keeps its layout (no row wraps onto two lines at 1000 px); set Russian back | ok (2026-10-19, Release, checked by screenshot): all texts English, date "10/15/2026", no wrapped rows at 1000 px |

## 3. Home: states

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | model installed, wheel button assigned and connected, iRacing not running | open Home | status card "Нет связи с iRacing" without the green mark and the hint to start iRacing; tiles: microphone with a green dot, "SIMAGIC GT Neo, кнопка 1 · удержание" with a green dot "подключена", iRacing "Не запущен" in grey with the hint; no counter, "Фраз пока нет…" | ok (2026-10-19, checked by screenshot): "Нет связи с iRacing" without the mark (Debug); on Release with the wheel unplugged the error wins, the iRacing tile "Не запущен" grey with the hint |
| 3.2 | 3.1 | start iRacing Test Drive, stay in the menu, then get in the car | the status card turns "Всё готово" with the green mark within 5 s of the tray icon; the iRacing tile shows "Запущен, вы не в машине" then "Вы в машине" with a green dot | ok (2026-10-19, Release, user in Test Drive): "Всё готово", the iRacing tile "Вы в машине" with a green dot |
| 3.3 | in the car | say "залей тридцать литров", then something that is not a command, then leave the car and say "залей тридцать литров" | three rows in "Последние фразы": applied (outcome and response in grey), "не понял" in amber with the reason as a second line, "вы не в машине" in red; no icons or coloured chips in the rows; the counter in the title reads "3 фразы, 1 применено" | ok (2026-10-19, Release, user): applied phrases and the counter "4 фразы, 4 применено" seen earlier; "не понял" amber and "вы не в машине" red checked by the user |
| 3.4 | 3.3 | say 20 more phrases (any), then look at the list and the counter | 20 rows, the oldest gone; the counter counts all 23 | ok (2026-10-19, Release, user) |
| 3.5 | Models → delete the model of the commands language | open Home | status card "Нет модели" without the mark and the hint to download it on the Models page; the tray icon says "no model" too; download it again | ok (2026-10-19, Release, checked by screenshot): after delete "Нет модели" with the hint naming vosk-model-small-ru-0.22; downloaded again from the window |
| 3.6 | Settings → assign a key, then unplug the wheel; or just unplug the wheel with the wheel button assigned | open Home | status card "Ошибка — устройство кнопки «…» не подключено" with the hint; the "Кнопка голоса" tile shows only the short red "не подключено" and the "Назначить" link (no second hint line, the hint is in the status card); the link opens Settings | ok (2026-10-19, Release, checked by screenshot; the wheel was unplugged by the user): error card with the device name, red "не подключено" and "Назначить", which opens Settings |
| 3.7 | settings file without a PTT binding (set `"binding": null`, restart) | open Home | "Кнопка голоса" tile "Кнопка не выбрана" with the red "не назначена" and the "Назначить" link; the status card says the button is not assigned; assign the button again | ok (2026-10-19, Release, checked by screenshot): "Кнопка не выбрана", red "не назначена", "Назначить" → Settings with "Назначить кнопку" |
| 3.8 | Settings → Microphone → a device, then unplug it | open Home | the tile shows the device name and the red "не найден" with the hint that the Windows default is used | ok (2026-10-19, Release, checked by screenshot): device name, red "не найден", hint about the Windows default |
| 3.9 | Settings → Microphone → "Системное по умолчанию" | open Home | the tile shows "Как в Windows (‹device name›)" with a green dot | ok (2026-10-19, Release, checked by screenshot): "Как в Windows (Microphone (fifine Microphone))" with a green dot |
| 3.10 | exit from the tray, start again | open Home | the counter is hidden, the list says "Фраз пока нет…" | ok (2026-10-19, Release): counter hidden and "Фраз пока нет…" after every restart |

## 4. Models

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 4.1 | en model not downloaded | open Models | the subtitle says recognition works offline and names the commands language; row "RU · Русский · используется" with a green dot, size, install date, id and license in small grey, "Удалить" grey; row "EN · English · не скачана" with a dashed circle, no date, and the single blue "Скачать" | ok (2026-10-19, Release, checked by screenshot) |
| 4.2 | 4.1 | "Скачать" for English | a thin progress bar with "N из M МБ" and "Отмена"; then "установлена" in grey with the install date of today, "Удалить"; the ru row stays "используется" | ok (2026-10-19, Release, checked by screenshot): bar with "9.5 из 39.3 МБ" and "Отмена"; then "установлена" grey with today's date and "Удалить"; ru stays "используется" |
| 4.3 | 4.2 | "Скачать" again after a delete, press "Отмена" in the middle | the row goes back to "не скачана", no error | ok (2026-10-19, Release): cancelled at once, the row back to "не скачана" without an error, no partial files |
| 4.4 | network off (Wi-Fi off) | "Скачать" | the row shows the error in red and "Скачать" again; nothing partial left in the models folder | ok (2026-10-19, Release, user) |

## 5. Commands

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | Russian | open Commands | "Язык команд: Русский"; "Свои слова" first with its icon, the state in one line, the two buttons on the right, the path under it; then Fuel, Tires, Windshield, Repair, Reset cards with icons in their hues (fuel peach, tires blue, windshield teal, repair red, reset grey), a description and example chips; every "Слова группы" collapsed | ok (2026-10-19, Release, checked by screenshot) |
| 5.2 | 5.1 | expand "Слова группы" of Fuel, go to Home and back | the block is collapsed again; the other groups never opened | ok (2026-10-19, Release, checked by screenshot) |
| 5.3 | **replaced by phrase-builder.md 3.3 (2026-10-23)** own words: `lexicon: { fuel: { action: [плесни] } }` in `spike\c1\languages\ru.yaml`, "Перечитать" | expand "Слова группы" of Fuel | "плесни" among the actions with the "своё" mark; "плесни" is not in the chips | ok (2026-10-19, Release, checked by screenshot): "плесни" with "своё" in the block, not in the chips |
| 5.4 | **replaced by phrase-builder.md 5.3 (2026-10-23)** put an unknown class into the file, "Перечитать" | look at "Свои слова" | the error with the file name and line in red under the path; the built-in words still work | ok (2026-10-19, Release, checked by screenshot): "fuel.pour" → state "в файле ошибки…" and the file:line error in red; built-in cards stay |
| 5.5 | Settings → Commands language → English | open Commands | "Commands language: English", chips from the English pack ("add ‹number› ‹unit›"), words "add, fill…" in the block; set Russian back | ok (2026-10-19, Release, checked by screenshot): "Язык команд: English", chips and words from the English pack |

## 6. Settings, overlay, about

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 6.1 | any | open Settings | five cards "Язык и вид", "Кнопка push-to-talk", "Звук", "Распознавание", "Диагностика"; every setting is a row "icon — label — control on the right"; rows divided by lines; the PTT row shows the device and button with the green "подключена" (or red "не подключено") and "Назначить другую" at the right edge, the mode on the row below; the volume slider with "Прослушать" and the threshold box at the right edge; "Собрать диагностику" and "Открыть папку логов" one under the other | ok (2026-10-19, Release, checked by screenshot) |
| 6.2 | 6.1 | change the volume to 40 %, the unit to gallons, the threshold to 0.7 | each change is in `settings.json` at once; a threshold of 1.5 shows the range error in red under the row and keeps the old value; set the values back | ok (2026-10-19, Release): volume 40, gallons and 0.7 saved to the file at once; an invalid threshold shows "Введите число от 0 до 1." under the row and keeps 0.6; values set back |
| 6.3 | a microphone with a long name (or rename one in Windows) | open Settings | the name is cut with an ellipsis, the list stays on the right, the row does not wrap | ok (2026-10-19, Release, checked by screenshot): "Системное по умолчанию (Microphone (fifine…" cut with an ellipsis, list on the right, also at 760 px |
| 6.4 | any | open Overlay | the "Показывать оверлей" row with the switch on the right and no note under it, then "Внешний вид" and "Место на экране" cards with rows, every control on the right edge; "Переместить" and "Сбросить положение" one under the other; "Переместить" turns into "Готово" and the indicator can be dragged as before; "Сбросить положение" works | ok (2026-10-19, Release, checked by screenshot): switch on the right, no note; "Переместить" shows the indicator with the dashed frame and "Готово", the page button turns into "Готово" and back |
| 6.5 | any | open About | the version card, the models card with licenses and links, the components as chips with a visible fill and a thin outline; the GitHub link opens the browser | ok (2026-10-19, Release): the GitHub link opened attellians/PitVoice in Chrome |

## 7. Window size and DPI

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 7.1 | any | shrink the window to its minimum width (760 px) and open every page | cards narrow, tiles share the width, the status title wraps, long rows cut with an ellipsis; a vertical scroll bar where needed, never a horizontal one | ok after a fix (2026-10-19, Release, checked by screenshot): the "Кнопка голоса" tile title was clipped at 760 px, now wraps; other pages narrow, ellipsis on long values, no horizontal scroll |
| 7.2 | Windows display scale 150 % (sign out / in if Windows asks) | open every page at 1000 × 720 and at 760 px | the same layout, icons and tints crisp, nothing clipped; set 100 % back | ok (2026-10-19, Release, user) |

## 8. Idle CPU

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 8.1 | Release build, window open on Home, iRacing not running | Task Manager → Details, watch PitVoice for a minute | CPU below 1 % and not above the 0.1.3 reading (0.52 % on 2026-10-18); no periodic spikes from the window | ok (2026-10-19, Release 0.1.3+, iRacing not running): 0.01 % over 60 s on 16 logical cores (0.1.3: 0.52 %), working set 354 MB |
| 8.2 | 8.1 | close the window to the tray, wait a minute | CPU the same or lower | ok (2026-10-19, Release): 0.01 % over 60 s with the window closed to the tray |
