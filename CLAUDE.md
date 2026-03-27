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

## Build

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

The game's own `BepInEx\plugins\` folder is intentionally left untouched so vanilla Valheim can run without mods.

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

**Two approaches are in flight — Approach A (current, kept as fallback) and Approach B (next implementation):**

**Approach A — Bind pose retargeting (v12 hybrid, current code):**
- ✅ Phase 1–4: Extraction, Blender retargeting, plugin integration, preview scene
- ✅ Phase 5, Steps 1-2b: Runtime matrix dump, validated BakeMesh simulator, full character visualization scene (`v12_armor_simulator.blend`)
- Result: Good torso, arms are thin/twisted but correctly positioned. Kept as fallback via `UseCustomKnightBodyMesh = false`.

**Approach B — Custom mesh directly rigged to Charred skeleton (planned next):**
- Sidesteps the ~177° arm bone orientation mismatch entirely by rigging a new mesh natively to the Charred skeleton rather than retargeting from the Player skeleton.
- Replaces the **full warrior visual** (hides Charred body/sinew/skull SMRs, adds single custom human+armor combined mesh).
- No Unity Editor needed — mesh data (vertices, normals, UVs, triangles, bone weights, bind poses) serialized as JSON from Blender and reconstructed as a Unity `Mesh` at runtime.
- Toggled via new `UseCustomKnightBodyMesh` config flag (default `false` keeps Approach A active).
- Armor set TBD — user will test SouthsilArmor sets in-game; materials pulled from whatever is equipped at runtime.
- See plan file for full implementation steps.

## Key Config Entries (runtime-tweakable via F1 in-game with ConfigurationManager)

| Config key | Default hotkey | Effect |
|---|---|---|
| `MasterSwitch` | F6 | Toggle all features |
| `TerrainRefreshKey` | F7 | Force terrain vertex color rewrite |
| `TreeRefreshKey` | F8 | Respawn tree replacements |
| `CharredWarriorRefreshKey` | F10 | Dump chest matrices + re-apply Charred Warrior armor |
| `DataDumpKey` | F11 | Dump player body mesh + charred sinew positioning data |

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