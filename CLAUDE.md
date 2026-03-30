# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Ashlands Reborn** is a BepInEx plugin for Valheim (Steam App 892970) that visually transforms the Ashlands biome into a Meadows-like aesthetic without changing gameplay. It uses Harmony patches to override weather, terrain vertex colors, tree spawning, and creature visuals at runtime.

- Plugin GUID: `com.ashlandsreborn.weather`
- Target: .NET 4.7.2, `valheim.exe` process

**Required mods** (install via r2modman into the "Ashlands Reborn" profile):
- `denikson-BepInExPack_Valheim` v5.4.2333+ — mod loader
- `Azumatt-Official_BepInEx_ConfigurationManager` v18.4.1+ — in-game config UI (F1)
- `southsil-SouthsilArmor` v3.1.8+ — custom armor sets used by CharredWarrior patches

**First-run note for new user accounts**: If SouthsilArmor items are missing in-game and the log shows a `NullReferenceException` in `Localization.SetLanguageFromLocale`, the account has no saved language preference. Fix: launch Valheim via Steam (no mods), go to Options → Language, set English, and exit. Then launch via r2modman normally.

## Build & Launch

### One-command dev cycle

```powershell
.\dev.ps1
```

`dev.ps1` (repo root) does everything in one step:
1. Runs `dotnet build` — compiles and deploys `AshlandsReborn.dll` to the r2modman profile
2. Copies Doorstop files (`winhttp.dll`, `doorstop_config.ini`) from the profile to the game directory
3. Creates a directory junction `<game dir>/BepInEx/` → profile's `BepInEx/` (backed up to `BepInEx_vanilla/` on first run)
4. Launches Valheim via `steam://rungameid/892970` — **must go through Steam** so Steamworks initializes correctly. Launching `valheim.exe` directly causes a Steamworks init failure and FejdStartup never loads.

With `DevAutoLoad = true` in the config (section "Dev Automation"), the game also auto-navigates menus and loads directly into the configured character/world.

**Must run with `-ExecutionPolicy Bypass`** — the script is blocked by default PowerShell execution policy:
```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1
```

### Autonomous dev cycle (Claude-driven)

Claude can build, launch, and evaluate results without user intervention:

1. **Build + launch**: `powershell -ExecutionPolicy Bypass -File dev.ps1`
2. **Poll for world load**: Watch `%APPDATA%\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\LogOutput.log` for `"starting game"` — appears within ~10s of game start when DevAutoLoad is enabled
3. **Wait ~15s** for the world to fully render after "starting game" appears
4. **Take screenshot**: Focus the Valheim window and use Alt+PrintScreen → save clipboard to PNG:
   ```powershell
   Add-Type -AssemblyName System.Windows.Forms, System.Drawing
   $proc = Get-Process valheim
   [Win32]::SetForegroundWindow($proc.MainWindowHandle)
   Start-Sleep 2
   [System.Windows.Forms.SendKeys]::SendWait('%{PRTSC}')
   Start-Sleep 1
   [System.Windows.Forms.Clipboard]::GetImage().Save('path\shot.png', [System.Drawing.Imaging.ImageFormat]::Png)
   ```
5. **Read log** for `[Ashlands Reborn]` lines confirming patches applied
6. **Read screenshot** with the Read tool to visually evaluate results

**Key paths:**
- BepInEx log: `C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\LogOutput.log`
- Valheim screenshots: `C:\Users\Dev\AppData\LocalLow\IronGate\Valheim\screenshots\`
- Config: `C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\com.ashlandsreborn.weather.cfg`

**Why the junction is needed:** BepInEx resolves plugin and config paths relative to the exe directory, not the working directory. Without the junction, BepInEx loads from the game's own `BepInEx/` folder (which has no plugins). The junction makes BepInEx see the profile's plugins transparently.

### Manual build only

```bash
cd AshlandsReborn
dotnet build
```

After a successful build, the `.csproj`:
1. **Copies `AshlandsReborn.dll`** to the r2modman profile — but only if the mod is marked `enabled: true` in `mods.yml`. If it's toggled off in r2modman, the build skips the copy and prints "Deploy skipped".
2. **Produces `bin/Debug/AshlandsReborn.zip`** — a Thunderstore-format package (requires `AshlandsReborn/icon.png` to exist, which it does).

The deploy target reads `mods.yml` by the mod's full name `Dan Moore-Ashlands Reborn` (author + mod name from manifest). If this string ever changes, update the PowerShell snippet in the `CopyToProfile` target accordingly.

**One-time r2modman import** (already done): profile → ⋮ → Import local mod → select the zip. After import, the toggle in r2modman's mod list controls deployment.

Override the profile path at build time:
```bash
dotnet build -p:ProfilePluginsPath="C:\path\to\profile\BepInEx\plugins"
```

If game references are missing, run `CopyRefs.ps1` from the repo root to populate `AshlandsReborn/Lib/` with the required DLLs from your Valheim install.

There are no automated tests — verify changes by running the game.

## Architecture

All plugin logic is structured as Harmony patches. `Plugin.cs` is the entry point: it binds 50+ `ConfigEntry<T>` fields (all `public static`), then calls `Harmony.PatchAll()`. Each feature lives in its own patch file under `AshlandsReborn/Patches/`.

### Patch files and what they do

| File | Patches | Purpose |
|---|---|---|
| `EnvManPatches.cs` | `EnvMan.Update` | Forces "Clear" environment when player is in Ashlands biome |
| `HeightmapPatches.cs` | Heightmap build/rebuild | Rewrites terrain vertex colors; detects lava via vegetation mask threshold |
| `TreePatches.cs` | `ClutterSystem`, zone generation | Replaces Ashlands tree spawns with Beech/Oak at configurable density and ratio |
| `ClutterSystemPatches.cs` | `ClutterSystem.Awake` | Minor grass clutter patch |
| `ValkyriePatches.cs` | Creature spawn | Swaps Fallen Valkyrie prefab with Valkyrie mesh/animations |
| `CharredWarriorPatches.cs` | Creature spawn | Equips armor/sword on Charred Melee; computes bind-pose transforms for skeletal rigging |
| `DevAutoLoadPatches.cs` | None (no Harmony patches) | State machine called from `Plugin.Update()` that auto-navigates FejdStartup menus on startup |

**Note on `DevAutoLoadPatches.cs`:** This file has no `[HarmonyPatch]` attributes. Harmony-patching `FejdStartup.Start()` (a coroutine) and `FejdStartup.Update()` (not defined as an override) fails silently. Instead it exposes a `Tick()` method called from `Plugin.Update()` each frame, checking `FejdStartup.instance` directly.

### Config → feature guard pattern

Every feature checks `Plugin.MasterSwitch` plus its own toggle before acting:
```csharp
public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
```

All `ConfigEntry` properties are `public static` so patch classes read them directly from `Plugin.*` without needing an instance.

### Charred Warrior armor (most complex)

`CharredWarriorPatches.cs` (~1500 lines) is the most involved file. It:
1. Clones armor item prefabs from `ObjectDB`
2. Computes bind-pose bone transforms from the creature skeleton
3. Attaches `SkinnedMeshRenderer` components with correct bone arrays and bind poses
4. Applies per-piece scale, rotation, and offset config values at attach time

**Chest armor Blender retargeting** (see `CHEST_RETARGET_PLAN.md`). Seven programmatic bind-pose approaches were exhausted; the fix required Blender-computed bind poses due to ~177° arm bone orientation mismatch between the Charred and Player skeletons.

**Hybrid approach (current implementation):**

The final design combines two layers to work around the ~177° arm bone orientation mismatch:

1. **Body swap layer** (`EnableBodySwap = true`, default): The player body mesh (cached from the local Player's `VisEquipment.m_bodyModel` on first Awake) is placed on the Charred skeleton with the player's original bind poses intact. Because both skeletons share Mixamo bone names, GPU skinning deforms it via Charred bones, giving volumetric deforming arms. Color/emission/scale/offset are configurable.

2. **Approach A armor on top** (unchanged): SouthsilArmor pieces attached via Blender-retargeted bind poses. Torso/legs/helm/cape look great. Arm geometry from the chest armor is hidden via `TrimChestArms = true` (default) by truncating `subMeshCount` from 10 to 7 on the cloned mesh (submeshes 7-9 are 100% arm/hand geometry). This modifies only the submesh descriptor table, not vertex/index buffers, bypassing the `isReadable=false` constraint. The body swap arms show through instead. Correct textures are preserved because the original Unity mesh is used (`UObject.Instantiate` of prefab mesh) rather than a rebuilt binary.

**SouthsilArmor mesh `isReadable=false` constraint**: All SouthsilArmor meshes have `isReadable=false` baked into the asset bundle. This blocks `SetTriangles`, `GetTriangles`, `GetVertices`, and all other mesh data APIs at runtime — even on `UObject.Instantiate()` clones. There is no public Unity API to flip this flag at runtime, and we cannot change import settings on a third-party mod's pre-built bundles. `Mesh.AcquireReadOnlyMeshData()` (Unity 2020.1+) can bypass `isReadable` for reading, but writing requires building a new mesh from scratch.

**Key config toggles:**
- `EnableBodySwap` (bool, default true) — adds the player body mesh layer
- `TrimChestArms` (bool, default true) — hides arm submeshes (7-9) via subMeshCount truncation
- `ShowVanillaChest / ShowVanillaShoulders` (bool, default false) — overlay vanilla pieces for comparison
- `BodySwapColorR/G/B`, `BodySwapEmissionR/G/B` — material color/emission of the body layer
- `BodySwapScale`, `BodySwapYOffset` — size and vertical position of the body layer

**MasterSwitch toggle revert/refresh cycle:**

`RevertAllCharredWarriors()` (OFF) must call Valheim's `Set*Item()` methods (not just set fields via reflection) so that Valheim updates its internal ZDO hashes. Without this, `RefreshCharredWarriors()` (ON) fails because Valheim sees the ZDO hash already matches the target item and skips instance creation. After the `Set*Item()` calls, leftover instances are explicitly destroyed as a safety net via `DestroyAndClearField`/`DestroyListInstances`.

**`m_current*ItemHash` must also be reset to 0 after revert and before refresh:** `DestroyAndClearField`/`DestroyListInstances` destroy visual GameObjects but do NOT reset `VisEquipment`'s internal `m_currentHelmetItemHash`, `m_currentChestItemHash`, `m_currentLegItemHash`, `m_currentShoulderItemHash` fields. `Set*Equipped(hash)` returns false immediately when its slot's hash matches — so if the hash was never cleared, the destroyed instances are never recreated. Fix: after destroying instances in `RevertAllCharredWarriors()` and before calling `Set*Item()` in `RefreshCharredWarriors()`, set all four fields to `0` via reflection (`FCurrentHelmetItemHash?.SetValue(vis, 0)` etc.). These fields are declared as `private static readonly FieldInfo?` alongside the other VisEquipment field accessors.

**Helmet scale/rotation must be absolute, not additive:** `ScaleHelmetAfterAttach` sets `localScale`, `localRotation`, and `localPosition` to absolute values (not `*=` or `+=`). The prefab's original scale is cached in a static `_cachedHelmetPrefabScale` field (not on the marker, which is destroyed during revert). Config scale is applied as `_cachedHelmetPrefabScale * configScale`.

## Key Config Entries (runtime-tweakable via F1 in-game with ConfigurationManager)

| Config key | Default hotkey | Effect |
|---|---|---|
| `MasterSwitch` | Backspace | Toggle all features |
| `TerrainRefreshKey` | F7 | Force terrain vertex color rewrite |
| `TreeRefreshKey` | F8 | Respawn tree replacements |
| `CharredWarriorRefreshKey` | F10 | Dump chest matrices + re-apply Charred Warrior armor |
| `DataDumpKey` | F11 | Dump player body mesh + charred sinew positioning data |

### Dev Automation config (section "Dev Automation")

| Config key | Default | Effect |
|---|---|---|
| `DevAutoLoad` | false | Auto-navigate menus and load into world on startup |
| `DevAutoLoadCharacter` | "Dove" | Character name to select |
| `DevAutoLoadWorld` | "Reborn" | World name to select |

## Asset Extraction Scripts

`scripts/` contains Python utilities used during development (not part of the plugin):

- `extract_terrain_textures.py` — extracts terrain texture arrays from Valheim asset bundles using `UnityPy`
- `extract_and_decompile_shader.py` — decompresses and disassembles HLSL shaders from asset bundles

Install dependencies with `pip install UnityPy` before running these.

## Reference Materials

- `ASHLANDS_REBORN_PLAN.md` — original phase 1/2 design plan
- `CHEST_RETARGET_PLAN.md` — Blender retargeting plan for chest armor (current WIP, supersedes CHEST_DEBUG_NOTES.md)
- `SHADER_SLICE_MAPPING.md` — terrain texture array slice documentation
- `VALKYRIE_RETARGET_PLAN.md` — creature animation retargeting strategy