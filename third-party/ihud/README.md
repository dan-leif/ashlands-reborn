# iHUD (third-party, kept for safekeeping)

iHUD (Immersive HUD) by MMaxG, MIT license (`src/LICENSE`).
Upstream: https://github.com/MMaxG/valheim-ihud-mod, commit `ea5a69f` (2026-10-18). Also Nexus mod 3867.
It is not on Thunderstore, so it was built locally and imported into the r2modman
"Ashlands Reborn" profile as a local mod. That gives it its own on/off switch.

| File | What it is |
|---|---|
| `MMaxG-iHUD-1.1.0.zip` | The Thunderstore-style package that was imported into r2modman |
| `src/iHUD.cs` | Upstream source, unchanged (the whole mod is this one file) |
| `iHUD.csproj` | Local build file. Upstream's csproj needs the BepInEx NuGet feed and a `D:\` game path, so this one references the profile's BepInEx/Harmony and the live game DLLs instead |
| `manifest.json`, `icon.png` | Package metadata (icon is a placeholder made locally) |

Built and verified on Valheim 1.0.20 (buildid 25909357) with BepInExPack 5.4.2351
(upstream says it was tested on 1.0.15). It doesn't patch anything Ashlands Reborn patches.

## Rebuild

```bash
dotnet build -c Release third-party/ihud/iHUD.csproj
```

Output: `third-party/ihud/bin/Release/netstandard2.1/iHUD.dll`.
Override paths with `-p:ValheimPath=...` / `-p:BepInExCore=...` if needed.

- **Quick test:** copy the DLL over the profile's `BepInEx\plugins\MMaxG-iHUD\iHUD\iHUD.dll`.
  The r2modman switch keeps working.
- **New package:** bump `version_number` in `manifest.json`, then zip `manifest.json`,
  `icon.png`, `README.md`, `src/LICENSE` (as `LICENSE`) and the DLL (as `plugins/iHUD/iHUD.dll`).
  Use forward-slash entry paths: Windows PowerShell 5.1's `Compress-Archive` writes backslashes.
  Then in r2modman: Settings → Import local mod → select the zip.

Config lives in the profile at `BepInEx\config\iHUD.cfg` (tune in-game with F1).
