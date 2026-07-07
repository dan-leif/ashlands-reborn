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

Claude can build, launch, evaluate results, and report back without user intervention. Full procedure:

**Step 1 — Kill any running instance**
```powershell
Stop-Process -Name valheim -Force -ErrorAction SilentlyContinue
```
Wait ~3 seconds before relaunching.

**Step 2 — Build + launch**
```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1
```

**Step 3 — Poll for world load**
Watch the BepInEx log for `"starting game"` — appears within ~20s of game start when DevAutoLoad is enabled. Poll every 2s, timeout after 3 minutes.
```bash
LOG="C:/Users/Dev/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/Ashlands Reborn/BepInEx/LogOutput.log"
for i in $(seq 1 90); do
  grep -q "starting game" "$LOG" && echo "FOUND" && break
  sleep 2
done
```

**Step 4 — Wait 15s for world render**
After "starting game" appears, sleep 15 seconds for terrain, trees, and creatures to fully load.

**Step 5 — Take screenshot**
Focus the Valheim window, send Alt+PrintScreen, save clipboard to PNG. Use `SW_RESTORE` (9) before `SetForegroundWindow` so the window is not minimized:
```powershell
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@
$proc = Get-Process valheim
[Win32]::ShowWindow($proc.MainWindowHandle, 9)   # SW_RESTORE
Start-Sleep -Milliseconds 500
[Win32]::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Seconds 2
[System.Windows.Forms.SendKeys]::SendWait('%{PRTSC}')
Start-Sleep -Seconds 1
[System.Windows.Forms.Clipboard]::GetImage().Save('C:\Users\Dev\AppData\LocalLow\IronGate\Valheim\screenshots\claude_shot.png', [System.Drawing.Imaging.ImageFormat]::Png)
```

**Step 6 — Analyze the screenshot**
Read the PNG with the Read tool and visually inspect it. A **good screenshot** shows:
- Game world in the background (terrain, trees, sky, creatures)
- Player character visible or at least the world loaded around them
- No loading screens, main menus, or black frames

A **bad screenshot** may show:
- Camera pointing straight up at the sky (spawn animation still in progress — wait longer)
- Black screen (window not focused or still loading)
- Main menu / character select screen (DevAutoLoad didn't fire — check log for errors)
- Partial UI only (window minimized when captured)

**If the screenshot is bad**: diagnose from the log, adjust wait time or retry. Common fixes:
- Camera pointing up → add 10–15s more wait and retake
- Black screen → verify Valheim window is foregrounded (`SetForegroundWindow` returned `True`), retry
- Still on menu → check log for DevAutoLoad errors; the character/world name may not match

Retry the screenshot (Steps 5–6) without relaunching the game until you get a good one.

**Step 7 — Read the log**
```bash
tail -60 "$LOG" | grep -E "(Ashlands Reborn|Warning|Error)"
```
Check for `[Ashlands Reborn]` lines confirming each patch applied (terrain, weather, trees, BodySwap, armor bind-pose, helmet).

**Step 8 — Kill the game and report**
```powershell
Stop-Process -Name valheim -Force -ErrorAction SilentlyContinue
```
Report log findings to the user, then **post the screenshot last** so it remains visible at the bottom of the chat.

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
| `CharredWarriorPatches.cs` | Creature spawn | LEGACY (bypassed when `EnableFableWarrior=true`): equips armor/sword on Charred Melee via bind-pose math |
| `FableWarriorPatches.cs` | `Humanoid.Awake`, `MonoUpdaters.LateUpdate`, `VisEquipment.Set*Equipped` | CURRENT Charred Warrior system: scaled Player-rig puppet dressed via native VisEquipment, driven by the Charred's animation (see below) |
| `PhotoModePatches.cs` | `GameCamera.LateUpdate` (prefix) | Dev verification harness: spawns a warrior, orbits the camera, captures full-body + close-up screenshots autonomously |
| `LifecycleTestPatches.cs` | None (no Harmony patches) | Dev M4 self-test: spawns 3 warriors (incl. 2★), asserts toggle/refresh/sync/scale lifecycle invariants |
| `DevAutoLoadPatches.cs` | None (no Harmony patches) | State machine called from `Plugin.Update()` that auto-navigates FejdStartup menus on startup |

**Note on `DevAutoLoadPatches.cs`:** This file has no `[HarmonyPatch]` attributes. Harmony-patching `FejdStartup.Start()` (a coroutine) and `FejdStartup.Update()` (not defined as an override) fails silently. Instead it exposes a `Tick()` method called from `Plugin.Update()` each frame, checking `FejdStartup.instance` directly.

### Config → feature guard pattern

Every feature checks `Plugin.MasterSwitch` plus its own toggle before acting:
```csharp
public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
```

All `ConfigEntry` properties are `public static` so patch classes read them directly from `Plugin.*` without needing an instance.

### Fable Warrior puppet (CURRENT Charred Warrior system — covers all four charred creatures)

`FableWarriorPatches.cs` replaces the legacy hodgepodge below. Core idea: player-authored
meshes never leave the skeleton they were authored for. A stripped, visual-only Player
prefab ("puppet") is instantiated as a child of the creature's `Visual` node, the
Charred's own renderers are hidden, and every `MonoUpdaters.LateUpdate` the Charred bones'
rotations are retargeted onto the matching puppet bones (shared Mixamo names) via
deviation-from-rest transfer, with a computed rest-pose alignment baked in for the 6
arm-chain bones (their rest poses differ by a ~28/48/59.5° constant). Globally active when
`MasterSwitch && EnableFableWarrior` (`Plugin.IsFablePuppetActive`); the entire legacy
`CharredWarriorPatches` path is bypassed via its `ShouldSwap()` guard.

**Per-creature profile table** (`FableWarriorPatches.Profiles`): the same machinery covers
`Charred_Melee` (Krom sword, right hand, legacy sizing + grip configs),
`Charred_Archer` (`FableArcherBow` bow, left hand, rig-normalized sizing),
`Charred_Twitcher`/`Charred_Twitcher_Summoned` (bare hands), and
`Charred_Mage` (`FableMageStaff` staff, right hand, rig-normalized). Each profile has its
own enable toggle (`ClonePlayerTo*`), body-scale multiplier, and weapon-scale config in its
own config section. Retarget offsets are computed and cached PER charred prefab (the
variants share bone names, and empirically the same rest poses, but this is not assumed).

Key mechanics (details in the file's doc comments):
- **Inactive strip**: the puppet is instantiated under an inactive holder so no gameplay
  `Awake` runs; all MonoBehaviours except `VisEquipment` are destroyed (multi-pass for
  RequireComponent chains), plus Rigidbody/Colliders; the Animator is kept but disabled.
- **No-ZDO VisEquipment**: `m_nViewOverride` is set to a session-static ZNetView on an
  inactive GameObject (its `GetZDO()` stays null), so all `Set*Item` calls run in local mode.
- **Appearance**: `Humanoid.SetupVisEquipment` (reflection) clones the local player's full
  gear/beard/hair/skin onto the puppet; Krom sword forced into the right hand; a ~2s
  signature diff in `PeriodicUpdate` re-clones on real player equip changes.
- **Rigid-attach scale gotcha**: vanilla `AttachItem` parents rigid attaches (helmet, sword)
  with `worldPositionStays=true`, which back-compensates `localScale` by the joint's
  `lossyScale` — on the ~1.4× scaled puppet rig, helmets rendered player-sized ("too small,
  perched on the crown"). `FixupPuppetAttaches` re-scales the helmet instance by the
  puppet-vs-prefab helmet-joint lossy ratio (`FableHelmetScale`/`FableHelmetYOffset` on
  top); the Krom keeps `WarriorKromScale` sizing plus `FableKromGripRot*/Off*` grip tuning
  (RotX=12 calibrated so the resting blade lies on the shoulder, not through the trapezius).
  Skinned attaches (`attach_skin` armor) are immune — bones + bind poses drive them.
- **Charred suppression**: pure-skip prefixes on the 7 private `VisEquipment.Set*Equipped`
  methods, gated on the `AshlandsRebornFableWarrior` marker; glow FX (EyeGlow ×2,
  chestglow) disabled; charred Animator set to `AlwaysAnimate` so the hidden source keeps
  animating.
- **Scale**: height target = capsule height × root lossyScale × 1.17 (`CapsuleToBodyHeight`)
  × star scale (from `LevelEffects.m_levelSetups`); late star-ups are absorbed by
  `AbsorbScaleDrift` in `PeriodicUpdate`.
- **Refresh gotcha**: `RefreshAll()` must wait one frame after `RevertAll()` before
  rescanning (marker `Destroy` is deferred; a same-frame scan sees stale markers and skips
  every warrior).

**Autonomous verification**: `PhotoModePatches` (config `PhotoModeAuto` or F6) teleports the
player to the test island (`PhotoModeIslandPos`), spawns a warrior, captures 4 full-body +
5 close-up angles + a t0/t1 animation-proof pair (plus an objective
`Animator.GetCurrentAnimatorStateInfo` log line), writes `AR_PhotoMode\DONE.txt`.
`LifecycleTestPatches` (config `PhotoModeM4Test`) asserts the M4 lifecycle DoD and writes
`M4_RESULTS.txt`. Outer loop: kill valheim → `dev.ps1` → poll the BepInEx log for
`[AR PhotoMode] DONE` / `[AR M4] DONE` → read the PNGs/results → iterate.

### Charred Warrior armor (LEGACY — active only when `EnableFableWarrior=false`)

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
| `PhotoModeKey` | F6 | Run the photo harness on demand |
| `PhotoModeAuto` | false | Run the photo harness once, ~10s after world load |
| `PhotoModeM4Test` | false | Run the M4 lifecycle self-test once after world load (suppresses the auto photo shoot) |
| `PhotoModePrefabs` | "Charred_Melee" | CSV of creature prefabs the harness shoots per session (filenames prefixed per prefab) |
| `PhotoModeSpawnDistance` | 5 | Distance in front of the player to spawn the test warrior |
| `PhotoModeIslandPos` | "2736,40,2580" | Test island teleport target; empty disables |

### Fable Warrior config (section "Fable Warrior" + per-creature sections)

| Config key | Default | Effect |
|---|---|---|
| `EnableFableWarrior` | true | Global gate: bypass ALL legacy warrior mods in favor of the puppet system (all creatures) |
| `ClonePlayerToWarrior` | true | Build the player puppet on every Charred_Melee |
| `FableWarriorScale` | 1.0 | Multiplier on the auto-computed height-match scale |
| `FableHelmetScale` / `FableHelmetYOffset` | 1.0 / 0 | Fine-tune the (already scale-normalized) puppet helmet (all creatures) |
| `FableKromGripRotX/Y/Z` | 12 / 0 / 0 | Krom grip rotation (deg, hand-attach frame); RotX=12 calibrates the shoulder rest |
| `FableKromGripOffX/Y/Z` | 0 | Krom grip position offset (m, hand-attach frame) |

Sections "Fable Archer" (`ClonePlayerToArcher`, `FableArcherScale`, `FableArcherBow` =
"BowAshlands", `FableArcherBowScale`), "Fable Twitcher" (`ClonePlayerToTwitcher`,
`FableTwitcherScale` — no weapon), and "Fable Mage" (`ClonePlayerToMage`, `FableMageScale`,
`FableMageStaff` = "StaffFireball", `FableMageStaffScale`) mirror the warrior keys.

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