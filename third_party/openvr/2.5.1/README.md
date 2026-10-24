# OpenVR SDK 2.5.1

`openvr_api.dll` (Windows x64) from the OpenVR SDK by Valve Corporation, BSD-3-Clause (see `LICENSE`). Only the VR
helper `PitVoice.VrOverlay.exe` loads it, and only in "VR mode" (add-vr-overlay D1, D2).

| File | Source | SHA-256 |
|---|---|---|
| `openvr_api.dll` | https://github.com/ValveSoftware/openvr/blob/v2.5.1/bin/win64/openvr_api.dll | `54ad7fe4cdb4d88fa818dbf8eb3d1f8ca2ae3b34d2ff115f87191d7d88e0a009` |
| `LICENSE` | https://github.com/ValveSoftware/openvr/blob/v2.5.1/LICENSE | |

The helper declares the functions it calls itself (`src/PitVoice.VrOverlay/OpenVrApi.cs`); the slot numbers of the
function tables come from `headers/openvr_capi.h` of the same tag. Moving to another SDK version means checking those
slots and the interface versions `IVRSystem_022` and `IVROverlay_027` again.

Check the file:

```powershell
(Get-FileHash third_party/openvr/2.5.1/openvr_api.dll -Algorithm SHA256).Hash
```
