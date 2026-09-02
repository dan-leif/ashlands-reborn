# Resuming Ashlands Reborn

On 2026-09-01 the local Valheim install was returned to pure vanilla (`revert-vanilla.ps1`)
ahead of a game update. This file is the way back.

## What was preserved, and where

| Thing | Where | Notes |
|---|---|---|
| Source, docs, screenshot galleries | this repo, pushed to `origin/master` | |
| Reference assemblies the code compiles against | `AshlandsReborn\Lib\*.dll` (gitignored, on disk) | Frozen from Steam **buildid 21981559** (see `Lib\FROZEN_FROM.txt`). The csproj falls back to them automatically when the game folder has no `BepInEx\core`. `dotnet build` works with the game vanilla. |
| Your tuned in-game config + exact mod versions | `profile-snapshot\` (committed) | `com.ashlandsreborn.weather.cfg`, `mods.yml`, `BepInEx.cfg`, `STEAM_BUILD.txt` |
| The r2modman profile "Ashlands Reborn" | `%APPDATA%\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn` | Untouched by the revert: deployed plugin dll, configs, BepInExPack, ConfigurationManager, SouthsilArmor |
| Last built Thunderstore package, old game-dir BepInEx leftovers, cfg copies | `C:\DEV\ashlands-reborn-archive\<date>\` | Not in git |

What the revert removed from `C:\Program Files (x86)\Steam\steamapps\common\Valheim`:
the `BepInEx` junction (into the profile), `winhttp.dll`, `doorstop_config.ini`,
`.doorstop_version`, `doorstop_libs\`, `start_*_bepinex.sh`, and the misnamed
`BepInEx_vanilla\` (an old manual BepInEx install, archived).

## Resume on the SAME game build (21981559)

```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1
```

That is all. `dev.ps1` rebuilds, re-copies `winhttp.dll` + `doorstop_config.ini` from the
profile into the game dir, recreates the `BepInEx` junction, and launches through Steam.

## Resume on a NEWER game build

1. In r2modman, update `denikson-BepInExPack_Valheim` and `southsil-SouthsilArmor` in the
   "Ashlands Reborn" profile. Keep `Dan Moore-Ashlands Reborn` enabled (the build deploys
   into it).
2. Try `dev.ps1` as is first. The frozen `Lib\` is only used when the game folder is
   unmodded, so once the junction exists the build compiles against the LIVE game
   assemblies again. Compile errors = renamed/removed game APIs.
3. Only when ready to port, refresh the frozen refs (this overwrites `Lib\`):
   ```powershell
   powershell -ExecutionPolicy Bypass -File AshlandsReborn\CopyRefs.ps1
   ```
4. Expect runtime breakage in name-based lookups even if it compiles. Every patched method
   and prefab/material name is listed per feature in `CLAUDE.md`; the ballista and fortress
   stone features already log near-miss catalogs when a configured name matches nothing.
   The photo harnesses (`PhotoModeAuto`, `TerrainPhotoAuto`, `FableBunnyReconDump`) are the
   fastest way to find what regressed.

## If the r2modman profile is gone

1. Create a profile named `Ashlands Reborn`, install BepInExPack, ConfigurationManager,
   SouthsilArmor (versions in `profile-snapshot\mods.yml`).
2. Import the mod: profile → ⋮ → Import local mod → `AshlandsReborn\bin\Debug\AshlandsReborn.zip`
   (rebuild first, or use the archived copy).
3. Copy `profile-snapshot\com.ashlandsreborn.weather.cfg` into the profile's
   `BepInEx\config\`.
4. Run `dev.ps1`.

## Gotchas

- **Junction removal**: `<game>\BepInEx` is a junction into the profile. Remove it only with
  `cmd /c rmdir`. `Remove-Item -Recurse` follows the link and deletes the profile's
  plugins and configs. `revert-vanilla.ps1` guards against this.
- **Steam verify does not remove extra files**, which is why the revert deletes the loader
  files explicitly.
- **Launching modded through r2modman** copies doorstop files back into the game dir; use
  plain Steam to stay vanilla.
- **DevAutoLoad** in the cfg expects character "Dove" and world "Reborn".
