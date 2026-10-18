# Manual check: release, installation and uninstall

The first public release and what a user does with it: install on a clean Windows, first start, the model mirror, the
CLI next to the app, reinstall, uninstall. Updates from inside the app were removed from this stage (2026-10-17); a newer version is installed over the old one with its installer. Everything runs in
**Windows Sandbox** (clean Windows without .NET and without a PitVoice profile at every start).

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
| 0.1 | `git log --all --name-only --pretty=format: \| sort -u` filtered by `*.wav`, `samples.yaml`, `spike/`, `settings.json`, `.env`, `*.pfx`, `*.pem`, `*.key` | no voice recordings of the user, no spike files, no settings | 2026-10-16: only the three generated cues (`src/PitVoice.Speech/Cues/*.wav`) and `tests/PitVoice.Speech.Tests/Fixtures/en-change-all-tires-add-thirty-liters.wav` (synthesized speech, confirmed by the user); no `samples.yaml`, `spike/`, `settings.json`, `.env` or keys ever committed |
| 0.2 | `git log --all -p` searched for `token`, `password`, `apikey`, `api_key`, `secret` in added lines | nothing but code identifiers | 2026-10-16: only `TokenQuery` (a WinAPI constant) besides `CancellationToken` and `GITHUB_TOKEN` |
| 0.3 | authors of all commits | the user's own name and address | 2026-10-16: "Artem" and "Artem Lebedko", one address |
| 0.4 | telemetry recordings `tests/PitVoice.IRacing.Tests/Recordings` | no user names, CustID or team names | 2026-10-16: no `UserName`, `CustID`, `TeamName` in the history of the directory |
| 0.5 | the user makes the repository public; the repository page opens in a private browser window | the page opens without signing in | ok (2026-10-16): public, opens in a browser without a GitHub session; the default branch `main` holds only README, the code is in `development` |

## 1. Mirror and the first release

| # | Action | Expected | Result |
|---|---|---|---|
| 1.1 | Actions → Mirror models → Run workflow | the run is green; the pre-release `models` holds `vosk-model-small-ru-0.22.zip` and `vosk-model-small-en-us-0.15.zip` | ok (2026-10-16): run 1 green, pre-release "Speech models (mirror)" with vosk-model-small-ru-0.22.zip (46236750 bytes) and vosk-model-small-en-us-0.15.zip (41205931 bytes); `releases/latest` answers 404, so `models` is not the latest release |
| 1.2 | open both `mirror` addresses of `models/manifest.json` in a private browser window, then `Get-FileHash -Algorithm SHA256` | both download without signing in, the sums match the manifest | ok (2026-10-16): both downloaded without a GitHub session, sizes and sha256 match the manifest |
| 1.3 | run Mirror models again | green, both files reported as already in the release, nothing replaced || ok (2026-10-17): Mirror models run 3 green; both archives in `models` still date from run 2 (2026-10-16 19:34 UTC), nothing replaced |
| 1.4 | release N by the steps of docs/development.md "Releasing" | the release `vN` has `PitVoice-win-Setup.exe`, `PitVoice-N-full.nupkg`, `releases.win.json`; the size of the installer is printed in the Pack job | mismatch, fixed (2026-10-16): v0.0.15 (package id `PitVoice`) was released by the README steps (Release run 1 green, Setup 85.8 MB), but its installer, run on the developer machine, said "PitVoice is already installed" over `%LocalAppData%\PitVoice` that held only models, logs and diagnostics, and its "Repair" deletes that directory whole. The program moved to `%LocalAppData%\PitVoiceApp` (package id `PitVoiceApp`), release v0.0.15 and its tag were deleted; recheck with the next release: ok (2026-10-17): v0.0.19 (package id `PitVoiceApp`), tag "First public release"; Release run 3 on v0.0.18 failed in the Ubuntu tests (a race in the no-trace test, fixed) and published nothing, so v0.0.18 was dropped; Release run 4 green; assets PitVoice-win-Setup.exe 85.8 MB, PitVoiceApp-0.0.19-full.nupkg 78.7 MB, releases.win.json, RELEASES, no portable zip |
| 1.5 | the "Releases" link on the repository page | "Latest" is `vN`, not `models` | ok for v0.0.15 (2026-10-16): `releases/latest` was v0.0.15, not `models`; recheck with the next release: ok (2026-10-17): `releases/latest` is v0.0.19 and `releases/latest/download/PitVoice-win-Setup.exe` answers 200 |
| 1.6 | tag `vX` on a commit whose `VERSION` is not X, push it | the release fails in "Tag matches VERSION" with both versions; no release `vX`; then delete the tag | ok (2026-10-16): v0.0.99 on a7f6972 (VERSION 0.0.16): Release run 2 failed in "Tag matches VERSION" with "tag v0.0.99 does not match VERSION 0.0.16 of the tagged commit", CI and pack skipped, no release v0.0.99, latest still v0.0.15; tag deleted |

## 2. Installation in Windows Sandbox

| # | Action | Expected | Result |
|---|---|---|---|
| 2.1 | in the sandbox: download the installer from `releases/latest`, run it | SmartScreen "Windows protected your PC" → More info → Run anyway; no administrator prompt; the app starts || ok (2026-10-17, the user): installed v0.0.19 on a clean system without administrator rights, the app started |
| 2.2 | 2.1 | Start menu and desktop have a PitVoice shortcut || ok (2026-10-17, the user, v0.0.25) |
| 2.3 | 2.1 | the home page shows the setup checklist with "download the model"; the tray icon is in the "no model" state || ok (2026-10-17, the user, v0.0.25) |
| 2.4 | 2.1, "About" | version N, "License: MIT", the components with versions, a link to THIRD-PARTY-NOTICES.md of this version || ok (2026-10-17, the user, v0.0.25): version 0.0.25, MIT, components with versions, notices link; no update button |
| 2.5 | 2.1, `cmd`: `cd %LocalAppData%\PitVoiceApp\current` and `pitvoice-cli diagnostics collect` | prints the path of the archive; no .NET installed in the sandbox || ok (2026-10-17, the user, v0.0.25): `pitvoice-cli diagnostics collect` from `%LocalAppData%\PitVoiceApp\current` printed the archive path |
| 2.6 | sandbox without network (`.wsb` above), the installer copied in | installation succeeds, the app starts; "Models" reports a connection error only when a download is started || ok (2026-10-17, the user, network cable unplugged): installed and started; "Models" showed nothing until "Download", then "no connection: primary https://alphacephei.com/... No such host is known; mirror https://github.com/.../releases/download/models/... No such host is known" under the model (both sources named; the text is technical, but correct) |
| 2.7 | sandbox with network: `notepad C:\Windows\System32\drivers\etc\hosts` as administrator inside the sandbox, add `0.0.0.0 alphacephei.com`; download the model in "Models" | the model downloads from the `models` release, passes the check and is installed || ok (2026-10-17), on the developer machine instead of the sandbox: blocking alphacephei.com in hosts did not work (DNS goes through Mihomo on the router) and the Mihomo REJECT rule was not reliable, so a copy of the installed v0.0.19 got a manifest whose primary address of vosk-model-small-en-us-0.15 is https://primary.invalid/...; `pitvoice-cli models download` printed "primary source failed (...), switching to mirror https://github.com/attellians/PitVoice/releases/download/models/vosk-model-small-en-us-0.15.zip", downloaded 39.3 MB, checked sha256 and installed; `models verify` OK. Note: after Mihomo stopped resolving release-assets.githubusercontent.com for a while, Windows kept the negative answer until `ipconfig /flushdns` |
| 2.8 | the app is running; run the same installer again, "Repair" | the dialog "PitVoice is already installed" (Velopack asks before reinstalling); after "Repair" the installer closes the app, reinstalls and starts it; settings and the model stay | ok (2026-10-17), on the developer machine with v0.0.19 (uninstalled, installed again, then this): the dialog shows "Installed at: %LocalAppData%PitVoiceApp", not the data directory; after "Repair" the app runs from `PitVoiceApprrent`, the log has "PitVoice 0.0.19 started", the same settings (PTT SIMAGIC GT Neo, button 1), "Model vosk-model-small-ru-0.22 loaded" and the first real check "Automatic update check: 0.0.19 is the latest" |
| 2.9 | Windows outside the sandbox where PitVoice was never installed but `%LocalAppData%\PitVoice` has models and logs of a build from source (the developer machine); run the installer | no "already installed" question; the app starts and sees the downloaded model; `%LocalAppData%\PitVoice` keeps its files || ok (2026-10-17, the user, v0.0.25): no "already installed" question over the data of a build from source; the app saw the downloaded model, `%LocalAppData%\PitVoice` kept its files |

## 3. Update from N to N+1

Removed from this stage on 2026-10-17 together with the update check in the app; it comes back with that feature. Seen before the removal: the installed v0.0.19 checked GitHub at start ("Automatic update check: 0.0.19 is the latest"), the release v0.0.22 got a delta package from 0.0.19 (0.1 MB), and the manual check without API quota showed "Could not check: … 403 (rate limit exceeded)" with the button available again.

## 4. Uninstall

| # | Action | Expected | Result |
|---|---|---|---|
| 4.1 | the app is running with a model, settings, a log and a diagnostics archive; Settings → Apps → PitVoice → Uninstall | the app closes, the tray icon disappears || ok (2026-10-17, the user, on the developer machine): uninstall showed progress, then "Uninstall Complete" with the system sound |
| 4.2 | 4.1 | no `%LocalAppData%\PitVoiceApp`, no `%LocalAppData%\PitVoice`, no `%AppData%\PitVoice`, no shortcuts, PitVoice not in the list of apps || ok (2026-10-17): checked after 4.1 on the developer machine: no `%LocalAppData%\PitVoiceApp`, `%LocalAppData%\PitVoice`, `%AppData%\PitVoice`, no desktop or Start menu shortcut, no PitVoice entry in the uninstall list |

## 5. Alpha and stable

Branches and tag forms of docs/development.md "Releasing" (change `add-alpha-prereleases`, 2026-10-17): an alpha `vX.Y.Z-alpha` from
`development`, a stable `vX.Y.Z` from `main` after a fast-forward to `development`, and only the installer in a release.

| # | Action | Expected | Result |
|---|---|---|---|
| 5.1 | alpha by docs/development.md "Releasing → Alpha": tag `vX.Y.Z-alpha` on a commit of `development` | the Release run is green; the pre-release "PitVoice X.Y.Z-alpha" has only `PitVoice-win-Setup.exe` (and the source archives GitHub adds); `releases/latest` is still the last stable release | ok (2026-10-17): `v0.0.27-alpha` on f3d1c12 (`development`) by the README steps; Release run 39033286562 green (verify, CI on both OSes, pack; "Remove old alphas" had nothing to delete); pre-release "PitVoice 0.0.27-alpha" holds only `PitVoice-win-Setup.exe` (85.8 MB); `releases/latest` is still v0.0.25 |
| 5.2 | install the alpha of 5.1 over the installed stable version ("Repair"), open "About" | version `X.Y.Z-alpha`; the THIRD-PARTY-NOTICES.md link opens the file of the tag `vX.Y.Z-alpha`; the log has "PitVoice X.Y.Z-alpha started"; settings and models stay | ok (2026-10-17, the user): 0.0.27-alpha installed over 0.0.25 with "Repair"; "About" shows "PitVoice 0.0.27-alpha", the notices link works, settings and the ru model stay; the log has "PitVoice 0.0.27-alpha started on Microsoft Windows 10.0.26200 (X64), .NET 10.0.13". The user found "About" too noisy (versions and licenses of every component): a separate change |
| 5.3 | stable by docs/development.md "Releasing → Stable": `main` fast-forwarded to `development`, tag `vX.Y.Z` | the Release run is green; the release is the latest, its only file is `PitVoice-win-Setup.exe` (and the source archives); the installer goes over the alpha of 5.2 with "Repair", settings and models stay, "About" shows `X.Y.Z` | GitHub part ok (2026-10-17): `git switch main`, `git merge --ff-only development` (6aa7867..54c6846), `git push origin main`, tag `v0.0.29` on 54c6846 (VERSION 0.0.29); Release run 39035438229 green; release "PitVoice 0.0.29" is the latest, its only file `PitVoice-win-Setup.exe` (85.8 MB); `releases/latest/download/PitVoice-win-Setup.exe` answers. Install over the alpha ok (the user): v0.0.29 over 0.0.28-alpha with "Repair", "About" shows 0.0.29, settings and the model stay |
| 5.4 | tag `vX.Y.Z` on a commit that is only in `development`, push it | the run fails in "Tag form, VERSION and branch" with "a stable release comes only from main" and the hint; CI and pack skipped, no release; then delete the tag | ok (2026-10-17): `v0.0.29` on 54c6846 while it was only in `development`: Release run 39035309434 failed in "Tag form, VERSION and branch" with "tag v0.0.29: a stable release comes only from main, and the tagged commit is not in main; release it as v0.0.29-alpha from development, or move main to it (git merge --ff-only development) and tag again"; no release (404); tag deleted |
| 5.5 | tag `vX.Y.Z-beta`, push it | the run fails in "Tag form, VERSION and branch" with the allowed forms `vX.Y.Z` and `vX.Y.Z-alpha`; no release; then delete the tag | ok (2026-10-17): `v0.0.29-beta` on 54c6846: Release run 39035309521 failed in "Tag form, VERSION and branch" with "tag v0.0.29-beta is not a release tag; the allowed forms are vX.Y.Z (stable, from main) and vX.Y.Z-alpha (pre-release, from development)"; no release (404); tag deleted |
| 5.6 | the sixth alpha (when there are 5 already) | the oldest alpha and its tag are gone, 5 stay; stable releases and `models` are in place | not checked (2026-10-17): only 2 alphas so far; the selection is covered by the "Test old alpha cleanup" step in CI, and both alpha runs logged no deletion |
| 5.7 | install the next alpha, open "About" in Russian and in English, click the repository link | the components are one column of names without versions, licenses or addresses; no THIRD-PARTY-NOTICES.md link; the models heading is "Модели распознавания речи" / "Speech recognition models"; the repository opens in the browser | Russian ok (2026-10-17, the user, v0.0.28-alpha installed over 0.0.27-alpha): "PitVoice 0.0.28-alpha", "Лицензия: MIT.", the repository link, no notices link; "Модели распознавания речи" with the two Vosk models, Apache-2.0 and their address; "Сторонние компоненты" as names only (.NET Runtime, CommunityToolkit.Mvvm, H.NotifyIcon, Kaldi, libvosk, NAudio, OpenBLAS, ppy.SDL3-CS, …). Release run 39034727558 green, the pre-release holds only PitVoice-win-Setup.exe; the cleanup removed nothing (2 alphas). English ok (the user): "Speech recognition models", components as names only; the repository link opens in the browser |
