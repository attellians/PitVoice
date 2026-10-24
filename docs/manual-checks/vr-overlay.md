# Manual check: indicator in the VR headset (`add-vr-overlay`)

"VR mode" on a published Release build (spec `vr-overlay-indicator`, the updated `app-settings`; TZ FR-OVL-8): the
mode off, waiting for SteamVR, the indicator in the headset, moving it with the mouse, SteamVR closed, helper crashes,
the failed state and its end, exit, idle CPU. There is no VR hardware: everything runs on the stand without a headset
(SteamVR with the null driver, "Building a VR stand" in [development.md](../development.md)), the picture of the headset
is the **VR View** window of SteamVR.

Publish once from the repository root in PowerShell (exit PitVoice from the tray menu first). The VR helper is only next
to the app in a published folder, like in the installer:

```
foreach ($p in "src/PitVoice.App/PitVoice.App.csproj", "src/PitVoice.VrOverlay/PitVoice.VrOverlay.csproj") {
  dotnet publish $p -c Release -r win-x64 --self-contained -o publish -nodeReuse:false
}
dotnet build-server shutdown
Set-Alias pitvoice "$PWD\publish\PitVoice.exe"
$env:PITVOICE_SETTINGS = "$PWD\spike\c1\settings.json"
$env:PITVOICE_MODELS_DIR = "$PWD\spike\c1\models"
```

Mark every item **ok**, **mismatch** or **not checked** (with the reason) and write what was seen. The state of the mode
is the line under the "VR mode" switch ("Settings" → "Language and look"); the log is
`%LocalAppData%\PitVoice\logs`. Processes: `Get-Process PitVoice*, vrserver -ErrorAction SilentlyContinue`.

**Who checks what.** The user checks only phrases spoken into the microphone (3.3) and, when iRacing runs on the stand,
the indicator in the car (3.4); everything else is checked by driving the build with screenshots of the window and of VR
View.

## 1. Mode off

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 1.1 | "VR mode" off, SteamVR running | start PitVoice, open "Overlay" → "Move" | no `PitVoice.VrOverlay` process; `openvr_api.dll` is not among the modules of `PitVoice` (`(Get-Process PitVoice).Modules`); nothing in VR View; the line says "выключен" | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): no helper, `openvr_api.dll` not loaded in `PitVoice`, nothing in the headset window in move mode, "выключен" |
| 1.2 | 1.1 | "Show the overlay" off, then "VR mode" on | the line says "индикатор выключен в разделе «Оверлей»"; no helper process | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): "индикатор выключен в разделе «Оверлей»", no helper |

## 2. Waiting for SteamVR

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 2.1 | SteamVR not running | turn "VR mode" on | at once "ждёт SteamVR" gray | ok (2026-10-24, Release publish 1.0.2, null driver, D3D11 helper): "VR mode" off and on with SteamVR closed: "ждёт SteamVR" 0.1 s after the switch, no helper |
| 2.2 | 2.1 | wait 10 minutes | SteamVR does not start, no helper process, the line still "ждёт SteamVR" | ok (2026-10-24, Release publish 1.0.2, null driver, D3D11 helper): 600 s sampled every 0.25 s: always "ждёт SteamVR", no helper, no `vrserver` |

## 3. In the headset

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 3.1 | 2.1 | start SteamVR | within 5 s "в шлеме" green; one `PitVoice.VrOverlay` process | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): SteamVR already running, "Show the overlay" turned on: "в шлеме" after 0.3 s, one helper, OpenVR still not in `PitVoice`; from 2.2 (1.0.2): `vrserver` 0.3 s after `vrstartup`, "подключение…", "в шлеме" 2.6 s after `vrserver` appeared |
| 3.2 | 3.1 | "Overlay" → "Move" | VR View shows the indicator with the dashed frame in the top right of the view, the monitor shows it too | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): the dim microphone in the dashed frame at the top right of the view in both eyes |
| 3.3 | 3.2, model and PTT set | say a phrase (user) | VR View: "listening", "recognizing", the result with its color, at the same time as on the monitor | ok (2026-10-24, Release publish 1.0.2, null driver, user by voice, headset window): the states go together with the monitor; no blinking after the helper moved to a D3D11 texture (with `SetOverlayRaw` it blinked while PTT was held, see design D5) |
| 3.4 | iRacing runs through SteamVR on the stand | get in the car, say a phrase (user) | the dim idle icon in the headset within 5 s; the phrase as in 3.3; leaving the car hides it. Without iRacing on the stand: not checked, left to the first VR pilot | not checked: iRacing does not run on the stand without a headset; left to the first VR pilot |
| 3.5 | 3.2 | drag the indicator on the monitor to the bottom left, "Done" | VR View: the indicator follows during the drag and stays at the bottom left of the view; after a restart of PitVoice it is there again | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): while the mouse button was still down the indicator in the headset already stood at the new place; the position was saved on release. after a restart of PitVoice (1.0.2) the frame stood at the bottom left of the view again, the file kept 74, 1370 |
| 3.6 | 3.2 | "Kind" B, "Size" 150 %, "Opacity" 50 % | VR View: the pill with the word, 1.5 times bigger, more transparent; the word stays inside the view at the right edge | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot), idle only: the frame of kind B, 1.5 times bigger, more transparent; the word of kind B during a phrase seen by the user, inside the view |
| 3.7 | 3.2 | interface language English | the word of kind B in VR View is English | ok (2026-10-24, Release publish 1.0.2, null driver, user by voice): the word is English |

## 4. SteamVR closed and helper failures

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 4.1 | 3.1 | close SteamVR | SteamVR closes without waiting for PitVoice; the line "ждёт SteamVR"; no helper process; PitVoice keeps working; the log has "SteamVR closed" and no failure | ok (2026-10-24, Release publish 1.0.2, null driver, D3D11 helper, user closed SteamVR from its menu): SteamVR closed at once; the helper was gone and the line said "ждёт SteamVR" 0.8 s before `vrserver` ended (the helper left on `VREvent_Quit`); log `vr: SteamVR closed, waiting for its next start`, no failure; PitVoice kept running |
| 4.2 | 3.2 | `Stop-Process -Name PitVoice.VrOverlay -Force` | within 5 s the indicator is back in VR View, the line "в шлеме"; the log has one "helper failed" line with the exit code | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): "в шлеме" again after 2.4 s ("ждёт SteamVR" → "подключение…" → "в шлеме"); log: `helper failed: exited with code -1, ran 108.1 s` (Stop-Process ends with -1) |
| 4.3 | 3.1 | kill the helper three times in a row, each time as soon as it is back | "ошибка VR" red; no new helper during 1 minute; three "helper failed" lines in the log | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): the third failure within 2 minutes (one of them from 4.2) gave "ошибка VR"; no helper during 1 minute; three `helper failed` lines and `3 failures within 2 minutes` |
| 4.4 | 4.3 | restart SteamVR | "в шлеме" again | ok (2026-10-24, Release publish 1.0.2, null driver, D3D11 helper): from "ошибка VR" SteamVR ended and started again: "в шлеме" 2.6 s after the start; log `SteamVR restarted (vrserver 29560), trying again` |
| 4.5 | 4.3 | instead of 4.4: "VR mode" off and on | "в шлеме" again | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): from "ошибка VR": off → "выключен", on → "в шлеме" after 0.3 s |
| 4.6 | 3.1 | "VR mode" off | within 2 s the indicator is gone from VR View and no helper process is left; the monitor indicator stays | ok (2026-10-24, Release publish 1.0.1, null driver, checked by screenshot): no helper after 0.1 s, the indicator is gone from the headset window, "выключен" |

## 5. Exit and load

| # | Setup | Action | Expected | Result |
|---|---|---|---|---|
| 5.1 | 3.2 | "Exit" in the tray menu | the indicator is gone from VR View; no `PitVoice` or `PitVoice.VrOverlay` process; SteamVR keeps running | ok (2026-10-24, Release publish 1.0.2, null driver, D3D11 helper, user pressed Exit): no `PitVoice` and no `PitVoice.VrOverlay` process, no PitVoice overlay in `vrcmd --overlays`, nothing in the headset window, SteamVR kept running |
| 5.2 | 3.1, idle icon visible (move mode off, in the car or "Move" with no phrase) | measure 5 minutes: `Get-Process PitVoice, PitVoice.VrOverlay` CPU time before and after | together below 1 % of one core on average (below 3 s of CPU time in 5 minutes) | ok (2026-10-24, Release publish 1.0.2, null driver, D3D11 helper): "в шлеме", move mode with the idle icon, 300 s: 2.45 s of CPU for `PitVoice` and `PitVoice.VrOverlay` together, 0.82 % of one core |

**Build, date:** Release publish of `development` (VERSION 1.0.2, add-vr-overlay uncommitted, helper with the D3D11 texture), SteamVR 2.18.4 with the null driver, 2026-10-24. All items ok except 3.4 (not checked: no iRacing on the stand).
