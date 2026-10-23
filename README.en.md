<p align="center">
  <img width="128" src=".github/assets/logo.png" alt="PitVoice logo">
</p>
<h1 align="center">PitVoice</h1>
<p align="center">
  <strong>Voice pit commands for iRacing.</strong> Hold a wheel button, say "change tires and add thirty liters", and
  PitVoice sets up the pit stop in the game; a short sound confirms the result. Free and open source, for Windows.
</p>

<p align="center">
  <a href="https://github.com/attellians/PitVoice/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/attellians/PitVoice"></a>
  <a href="https://github.com/attellians/PitVoice/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/attellians/PitVoice/total"></a>
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue"></a>
  <img alt="Windows 10 | 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white">
</p>

<p align="center">
  <a href="https://github.com/attellians/PitVoice/releases/latest"><b>Download</b></a>
  ·
  <a href="#features">Features</a>
  ·
  <a href="#what-you-can-say">Commands</a>
  ·
  <a href="#faq">FAQ</a>
  ·
  <a href="https://github.com/attellians/PitVoice/issues">Report a bug</a>
  ·
  <a href="README.md">Русский</a>
</p>

> [!WARNING]
> The only official builds are on this repository's [Releases](https://github.com/attellians/PitVoice/releases) page.

<p align="center">
  <img width="760" src=".github/assets/window-home.png" alt="PitVoice home page: all set, microphone, button and iRacing connected, recent phrases">
  <br>
  <sub>The interface is in Russian here; English is chosen in the settings.</sub>
</p>

## Why

Setting up a pit stop in iRacing means looking away to the black box or the keyboard while you drive. With PitVoice
you press a button on the wheel and say what the stop should do. Speech is recognized on your PC without the internet,
the commands go to the game through the official iRacing SDK, and the app checks in the telemetry that the game took
them.

## Features

- **A wheel button or a key.** Any button of a game device (wheel, button box) or a keyboard key. Two modes: **hold**
  (hold, speak, release) and **toggle** (press, speak, press again).
- **Russian and English.** The language of the commands and of the interface are chosen separately.
- **Offline recognition.** The speech model (Vosk) is downloaded once, when you ask, and runs locally. No telemetry,
  no cloud.
- **Several commands in one phrase.** "Add ten liters, change right side and tear off". The phrase is applied in full or
  not at all.
- **Checked in the telemetry.** "Success" sounds only when the game really took the commands. When you are not in the
  car, there is no game or something did not apply, you hear "error".
- **Three short sounds** instead of a voice: recording started, success, error.
- **Indicator above the game.** A small icon in the car shows "listening", "recognizing" and how the phrase ended.
  Clicks pass through it into the game. Kind, size, opacity and place are set on the "Overlay" page.
- **Own phrases.** Give a pit command your own phrase: "Commands" → "Own phrases" → "Set up". The phrase is checked
  at once, before it is saved, and the reference shows it marked "own".
- **Recent phrases** on the home page: what was heard, how it ended and why.
- **Lives in the tray**, needs no administrator rights and never touches the game process.

## What you can say

Word order is free; after "and" the action need not be repeated. The full word list is on the "Commands" page of the
app.

| To do | For example |
|---|---|
| Fuel | "add thirty-five liters", "add twenty", "no fuel" |
| All tires | "change tires", "new tires" |
| Some tires | "change left side", "change front left and rear right", "change left side and fronts" |
| Keep tires | "keep left side and rear right" |
| Tire compound | "change to slicks", "change to wet tires" |
| Windshield | "tear off" |
| Fast repair | "fast repair" |
| Clear everything | "clear all" |
| Together | "change tires and add twenty five liters" |

In Russian: «залей тридцать литров», «залить двадцать», «меняй левые», «свежую резину», «замена на сухие», «на слики»,
«почистить стекло», «быстрый ремонт», «отмени всё», «снимай всё».

<p align="center">
  <img width="760" src=".github/assets/window-commands.png" alt="The Commands page: group cards with example phrases">
</p>

## Install

| You need | |
|---|---|
| System | Windows 10 or 11, 64-bit |
| Game | iRacing; windowed or borderless mode for the indicator |
| Microphone | any, a headset is best |
| Disk | ~90 MB installer and ~45 MB speech model per language |

1. Download `PitVoice-win-Setup.exe` from the [latest release](https://github.com/attellians/PitVoice/releases/latest)
   and run it. It installs into your profile (`%LocalAppData%\PitVoiceApp`), needs no administrator rights and adds
   shortcuts to the Start menu and the desktop.
2. Open **"Models"** and download the speech model of your language. That is the only thing PitVoice downloads, and
   only when you press the button.
3. In **"Settings"** press **"Assign button"**, then the button on your wheel.
4. Get in the car in iRacing, hold the button and say a command. The home page shows what was heard and how it ended.

> [!IMPORTANT]
> **The installer is not signed yet**, so Windows SmartScreen may say "Windows protected your PC". Click
> **More info**, then **Run anyway**.

**New versions.** The app does not look for updates and never goes online on its own. Download the installer of the
new version from the [releases](https://github.com/attellians/PitVoice/releases) and run it; it says PitVoice is
already installed: press **Repair**. Settings, models and your own phrases stay.

**Alpha builds.** Work in progress is published as pre-releases `PitVoice X.Y.Z-alpha` that install the same way, over
the stable version. To go back, run the installer of a stable version.

**Uninstall.** "Settings → Apps → Installed apps → PitVoice → Uninstall" removes the program and all its data: models,
logs, diagnostics, settings and your own phrases.

## FAQ

<details>
<summary><b>Can I get banned?</b></summary>
<br/>
PitVoice does not inject into the game and does not change its files. It reads the game state through the official
iRacing SDK and sends pit commands with the SDK messages made for that. It is the same as setting up the pit stop in the
game yourself.
</details>

<details>
<summary><b>Does it need the internet? What does it send?</b></summary>
<br/>
Only to download a speech model, when you press the button on the "Models" page. Nothing else goes online: no
telemetry, no analytics, no update checks. Your voice is never saved to disk, and recognized phrases are not written to
the log.
</details>

<details>
<summary><b>Commands do not reach the game, or the key does not work in the game</b></summary>
<br/>
Most likely iRacing runs as administrator and PitVoice does not. Windows then blocks the messages to the game and the
keys pressed in its window. Start both normally. A wheel button is more reliable than a keyboard key.
</details>

<details>
<summary><b>PitVoice does not understand me well</b></summary>
<br/>
Speak after the start sound and release the button when you are done. The home page shows exactly what was heard. If
you call a command your own way, add your own phrase: "Commands" → "Own phrases" → "Set up". A rejected phrase sends
nothing to the game: asking again is better than guessing.
</details>

<details>
<summary><b>I do not see the indicator above the game</b></summary>
<br/>
The indicator is shown only while you are in the car. In exclusive fullscreen Windows shows no windows over the game:
switch iRacing to borderless or windowed mode. Turn the indicator off or move it on the "Overlay" page or with "Move
indicator" in the tray icon menu.
</details>

<details>
<summary><b>Where does PitVoice keep its data?</b></summary>
<br/>
The program: <code>%LocalAppData%\PitVoiceApp</code>; models, logs and diagnostics archives: <code>%LocalAppData%\PitVoice</code>;
settings and your own phrases: <code>%AppData%\PitVoice</code>.
</details>

<details>
<summary><b>Why are there GPL files in the installer?</b></summary>
<br/>
The Vosk recognizer for Windows is built with MinGW-w64 and comes with <code>libstdc++-6.dll</code> and
<code>libgcc_s_seh-1.dll</code>. They are under GPL-3.0 with the GCC Runtime Library Exception, which allows shipping them
with programs under any license. PitVoice itself is MIT. Every component and its license: <a href="THIRD-PARTY-NOTICES.md">THIRD-PARTY-NOTICES.md</a>.
</details>

## Feedback

Found a bug or have an idea? Open an [issue](https://github.com/attellians/PitVoice/issues). If the app crashed, attach
the diagnostics archive: "Settings" → "Collect diagnostics".

Building from source and everything for developers: [docs/development.md](docs/development.md); adding a language:
[docs/adding-a-language.md](docs/adding-a-language.md).

## License

[MIT](LICENSE). Third-party components: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

PitVoice is not affiliated with iRacing.com Motorsport Simulations. iRacing is a trademark of its owner.
