# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Ashlands Reborn** is a BepInEx plugin for Valheim (Steam App 892970) that visually transforms the Ashlands biome into a Meadows-like aesthetic without changing gameplay. It uses Harmony patches to override weather, terrain vertex colors, tree spawning, and creature visuals at runtime.

- Plugin GUID: `com.ashlandsreborn.weather`
- Target: .NET 4.7.2, `valheim.exe` process
- BepInEx dependency: 5.4.2333+

## Build

```bash
cd AshlandsReborn
dotnet build
```

After a successful build, the `.csproj` automatically copies `AshlandsReborn.dll` to:
1. `{GamePath}\BepInEx\plugins\AshlandsReborn\` (detected via Steam registry)
2. `%USERPROFILE%\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\`

Override paths at build time:
```bash
dotnet build -p:GamePath="C:\path\to\Valheim"
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

Current WIP: chest armor bind-pose positioning (see `CHEST_DEBUG_NOTES.md`).

## Key Config Entries (runtime-tweakable via F1 in-game with ConfigurationManager)

| Config key | Default hotkey | Effect |
|---|---|---|
| `MasterSwitch` | F6 | Toggle all features |
| `TerrainRefreshKey` | F7 | Force terrain vertex color rewrite |
| `TreeRefreshKey` | F8 | Respawn tree replacements |
| `CharredWarriorRefreshKey` | F9 | Re-apply Charred Warrior armor |

## Asset Extraction Scripts

`scripts/` contains Python utilities used during development (not part of the plugin):

- `extract_terrain_textures.py` — extracts terrain texture arrays from Valheim asset bundles using `UnityPy`
- `extract_and_decompile_shader.py` — decompresses and disassembles HLSL shaders from asset bundles

Install dependencies with `pip install UnityPy` before running these.

## Reference Materials

- `ASHLANDS_REBORN_PLAN.md` — original phase 1/2 design plan
- `CHEST_DEBUG_NOTES.md` — current debugging status for chest armor bind-pose
- `SHADER_SLICE_MAPPING.md` — terrain texture array slice documentation
- `VALKYRIE_RETARGET_PLAN.md` — creature animation retargeting strategy
