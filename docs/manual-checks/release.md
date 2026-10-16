# Manual check: release, installation, updates and uninstall

The first public release and what a user does with it: install on a clean Windows, first start, the model mirror, the
CLI next to the app, reinstall, update from one release to the next, the update switch, uninstall. Everything runs in
**Windows Sandbox** (clean Windows without .NET and without a PitVoice profile at every start); the update check needs
two published releases, N and N+1.

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen.

Windows Sandbox: "Turn Windows features on or off" → Windows Sandbox (once, needs a restart). A configuration without
network is a `.wsb` file:

```xml
<Configuration>
  <Networking>Disable</Networking>
</Configuration>
```

## 0. Before the repository goes public

| # | Check | Expected | Result |
|---|---|---|---|
| 0.1 | `git log --all --name-only --pretty=format: \| sort -u` filtered by `*.wav`, `samples.yaml`, `spike/`, `settings.json`, `.env`, `*.pfx`, `*.pem`, `*.key` | no voice recordings of the user, no spike files, no settings | 2026-10-16: only the three generated cues (`src/PitVoice.Speech/Cues/*.wav`) and `tests/PitVoice.Speech.Tests/Fixtures/en-change-all-tires-add-thirty-liters.wav` (origin to be confirmed by the user); no `samples.yaml`, `spike/`, `settings.json`, `.env` or keys ever committed |
| 0.2 | `git log --all -p` searched for `token`, `password`, `apikey`, `api_key`, `secret` in added lines | nothing but code identifiers | 2026-10-16: only `TokenQuery` (a WinAPI constant) besides `CancellationToken` and `GITHUB_TOKEN` |
| 0.3 | authors of all commits | the user's own name and address | 2026-10-16: "Artem" and "Artem Lebedko", one address |
| 0.4 | telemetry recordings `tests/PitVoice.IRacing.Tests/Recordings` | no user names, CustID or team names | 2026-10-16: no `UserName`, `CustID`, `TeamName` in the history of the directory |
| 0.5 | the user makes the repository public; the repository page opens in a private browser window | the page opens without signing in | ok (2026-10-16): public, opens in a browser without a GitHub session; the default branch `main` holds only README, the code is in `development` |

## 1. Mirror and the first release

| # | Action | Expected | Result |
|---|---|---|---|
| 1.1 | Actions → Mirror models → Run workflow | the run is green; the pre-release `models` holds `vosk-model-small-ru-0.22.zip` and `vosk-model-small-en-us-0.15.zip` | ok (2026-10-16): run 1 green, pre-release "Speech models (mirror)" with vosk-model-small-ru-0.22.zip (46236750 bytes) and vosk-model-small-en-us-0.15.zip (41205931 bytes); `releases/latest` answers 404, so `models` is not the latest release |
| 1.2 | open both `mirror` addresses of `models/manifest.json` in a private browser window, then `Get-FileHash -Algorithm SHA256` | both download without signing in, the sums match the manifest | ok (2026-10-16): both downloaded without a GitHub session, sizes and sha256 match the manifest |
| 1.3 | run Mirror models again | green, both files reported as already in the release, nothing replaced | |
| 1.4 | release N by the steps of README "Releasing" | the release `vN` has `PitVoice-win-Setup.exe`, `PitVoice-N-full.nupkg`, `releases.win.json`; the size of the installer is printed in the Pack job | |
| 1.5 | the "Releases" link on the repository page | "Latest" is `vN`, not `models` | |
| 1.6 | tag `vX` on a commit whose `VERSION` is not X, push it | the release fails in "Tag matches VERSION" with both versions; no release `vX`; then delete the tag | |

## 2. Installation in Windows Sandbox

| # | Action | Expected | Result |
|---|---|---|---|
| 2.1 | in the sandbox: download the installer from `releases/latest`, run it | SmartScreen "Windows protected your PC" → More info → Run anyway; no administrator prompt; the app starts | |
| 2.2 | 2.1 | Start menu and desktop have a PitVoice shortcut | |
| 2.3 | 2.1 | the home page shows the setup checklist with "download the model"; the tray icon is in the "no model" state | |
| 2.4 | 2.1, "About" | version N, "License: MIT", the components with versions, "Check for updates" (not the text of a developer build) | |
| 2.5 | 2.1, `cmd`: `cd %LocalAppData%\PitVoice\current` and `pitvoice-cli diagnostics collect` | prints the path of the archive; no .NET installed in the sandbox | |
| 2.6 | sandbox without network (`.wsb` above), the installer copied in | installation succeeds, the app starts; "Models" reports a connection error only when a download is started | |
| 2.7 | sandbox with network: `notepad C:\Windows\System32\drivers\etc\hosts` as administrator inside the sandbox, add `0.0.0.0 alphacephei.com`; download the model in "Models" | the model downloads from the `models` release, passes the check and is installed | |
| 2.8 | the app is running; run the same installer again | the installer closes the app, reinstalls and starts it; settings and the model stay | |

## 3. Update from N to N+1

| # | Action | Expected | Result |
|---|---|---|---|
| 3.1 | in the sandbox with N installed, a model and a changed setting; publish N+1 | the release `vN+1` also has a delta package from N | |
| 3.2 | restart PitVoice in the sandbox | the home page shows "Version N+1 is available" with "Update and restart" and "What's new" (opens the release page) | |
| 3.3 | "Update and restart" | progress in the block, then the app closes and starts again as N+1 with the same settings and model | |
| 3.4 | 3.3, the log in `%LocalAppData%\PitVoice\logs` | "Automatic update check: version N+1 found", "Update N+1 download started", "… downloaded, installing and restarting", then "PitVoice N+1 started" | |
| 3.5 | install N in a new sandbox, turn "Check for updates" off in Settings, restart the app | no update block; the log has "Automatic update check off" and no check line | |
| 3.6 | 3.5, "About → Check for updates" | shows N+1 and the block appears on the home page | |

## 4. Uninstall

| # | Action | Expected | Result |
|---|---|---|---|
| 4.1 | the app is running with a model, settings, a log and a diagnostics archive; Settings → Apps → PitVoice → Uninstall | the app closes, the tray icon disappears | |
| 4.2 | 4.1 | no `%LocalAppData%\PitVoice`, no `%AppData%\PitVoice`, no shortcuts, PitVoice not in the list of apps | |
