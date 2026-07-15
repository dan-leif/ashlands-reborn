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

**Step 4 — Wait 15s for world render, then force a clear sky**
After "starting game" appears, sleep 15 seconds for terrain, trees, and creatures to fully
load. **Then force clear weather before any screenshot** — overcast/rain wrecks the lighting
(e.g. dark, hard-to-read subjects). Bake this into *every* test:
- The autonomous harnesses do it in code: `PhotoModePatches.ForceClearSkyRoutine()` runs
  `EnvMan.SetForceEnvironment("Clear")` and waits for the sky to actually clear before
  capturing. Reuse that helper in any new harness.
- For manual/live screenshots (the Alt+PrintScreen path below), open the console (F5) and run
  `env clear`, then **wait ~15–20 s for the sky to visibly clear** — the fog/sky/sun transition
  lerps in over several seconds; capturing immediately still shows the old weather.

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
| `EnvManPatches.cs` | `EnvMan.Update` | Forces "Clear" environment in every biome (the terrain-regen half of the postfix stays Ashlands-only) |
| `HeightmapPatches.cs` | Heightmap build/rebuild | Rewrites terrain vertex colors per the active `TerrainTransitionStyle`; detects lava via vegetation mask (see "Terrain transition styles") |
| `TerrainTransition.cs` | None (style engine) | Band ramps, mask blur, Perlin edge jitter, grass rule, and per-style material tint shared by Heightmap/Clutter patches |
| `TreePatches.cs` | `ClutterSystem`, zone generation | Replaces Ashlands tree spawns with Beech/Oak at configurable density and ratio |
| `ClutterSystemPatches.cs` | `ClutterSystem.GetGroundInfo` | Meadows grass on green terrain; excluded from lava/mud per the active transition style |
| `TerrainPhotoPatches.cs` | None (no Harmony patches) | Dev harness: teleports to the transition test spot, cycles every transition style, captures top-down/oblique/close shots per style |
| `ValkyriePatches.cs` | Creature spawn | Swaps Fallen Valkyrie prefab with Valkyrie mesh/animations |
| `FableWarriorPatches.cs` | `Humanoid.Awake`, `MonoUpdaters.LateUpdate`, `VisEquipment.Set*Equipped` | Charred Warrior system: scaled Player-rig puppet dressed via native VisEquipment, driven by the Charred's animation (see below) |
| `FableBunnyPatches.cs` | `Character.Awake`, `Humanoid.StartAttack`, `Ragdoll.Awake` (all manually applied with null-guards), `MonoUpdaters.LateUpdate` | Fable Bunny: replaces the Morgen's visuals with a giant self-animating Hare (+ optional hybrid Lox for bite/roll). See "Fable Bunny" below |
| `PhotoModePatches.cs` | `GameCamera.LateUpdate` (prefix) | Dev verification harness: spawns a warrior, orbits the camera, captures full-body + close-up screenshots autonomously |
| `LifecycleTestPatches.cs` | None (no Harmony patches) | Dev M4 self-test: spawns 3 warriors (incl. 2★), asserts toggle/refresh/sync/scale lifecycle invariants |
| `MageWeaponTestPatches.cs` | None (no Harmony patches) | Dev harness (`MageWeaponTest`): dumps the ObjectDB/ZNetScene staff catalog + candidate prefab hierarchies, then spawns a pinned Charred_Mage and cycles `MageWeaponTestList` through `FableMageWeapon`, screenshotting each staff in-hand. `MageWeaponRefCapture` prepends vanilla-carry references (player equipping each player staff; DvergerMageFire/Ice/Support; puppet-disabled Charred_Mage); `MageWeaponRotSweep` captures each staff once per rot/offset knob combination (tuning mode). Output: `AR_MageWeapon\` |
| `DevAutoLoadPatches.cs` | None (no Harmony patches) | State machine called from `Plugin.Update()` that auto-navigates FejdStartup menus on startup |

**Note on `DevAutoLoadPatches.cs`:** This file has no `[HarmonyPatch]` attributes. Harmony-patching `FejdStartup.Start()` (a coroutine) and `FejdStartup.Update()` (not defined as an override) fails silently. Instead it exposes a `Tick()` method called from `Plugin.Update()` each frame, checking `FejdStartup.instance` directly.

### Terrain transition styles (green → ash/lava fade)

The terrain shader reads vertex colors as biome texture selectors: Meadows `(0,0,0,0)`,
Swamp `(255,0,0,0)`, Plains `(0,0,0,255)`, AshLands `(255,0,0,255)` — **alpha is the
Plains (yellow) selector** (recon-verified in-game via the `DebugGradient` calibration
style: a pure-alpha ramp renders green→yellow; an alpha ramp with R=255 held renders
mud→ash with no yellow). This is why the original override (now `Legacy`) showed a yellow
fringe: it stamped full AshLands color directly against Meadows vertices, and GPU
interpolation swept through mid-alpha. Its binary per-vertex mask threshold also snapped
contours to the 1m lattice (90°/45° stair-steps).

`TerrainTransition.cs` fixes both for the styled paths (`TerrainTransitionStyle` config,
F1 dropdown, live rebuild via `SettingChanged` → `ForceTerrainRefresh`):
- Fade routes through the Swamp red channel: green → `(R,0,0,0)` mud ramp → `(255,0,0,A)`
  ash ramp → full AshLands. Alpha never rises until R is saturated, so Plains can't activate.
- Lava mask is box-blurred on a padded grid (`BuildMaskGrid` samples neighbor chunks via
  `Heightmap.FindHeightmap` so kernels agree across chunk borders — no seams), and band
  thresholds get world-space Perlin jitter (deterministic, chunk-agnostic) for organic edges.
- **Lava glow requires the AshLands vertex color** (DebugGradient strip proofs: Meadows
  color over molten lava kills the glow; constant half-ash `(255,0,0,128)` renders it dim
  crimson — the glow scales with ash weight, so only full ash matches vanilla).
- **AshHold invariant (v2, safety)**: `Heightmap.IsLava` = raw point-sampled paint-mask
  alpha > 0.6 on a non-biome-edge AshLands chunk. Every styled vertex with raw mask ≥
  `TransitionAshHold` (default 0.2, slider 0.05–0.55 < 0.6) renders full vanilla ash via a
  binary gate on the RAW mask, so lethal ground can never look safe and the shader's
  glowing rim/cracks stay exactly where vanilla puts them. LAVACHECK (in
  `HeightmapPatches`, armed by the photo harness) counts violations per rebuild — must be
  0. Do NOT feed raw into the band ramps: its lattice grain paints 1m color steps.
- **Ash skirt (v2, band shaping)**: below the hold, band input =
  `max(blurred+jitter, blurred-skirt + jitter/2)`. The skirt is a distance-decayed
  grayscale dilation (separable max-plus) of the hold-capped raw mask, box-blurred to
  round the hold region's lattice-blocky level-set corners. It guarantees the fade spans
  ~4 m (MudBlend) / ~3.5 m (GrassToLava) around every lava feature — mask-value bands
  alone collapse to <1 m at sharp mask cliffs (thin lava channels) and re-show stair
  steps. Every ramp segment must span ≥ ~1.5–2 vertices at the skirt decay rate.
  `AllowGrassAt` mirrors the skirt with 8 point probes so grass stops mid-fade.
- Bands are anchored to the hold H: MudBlend R-ramp `[H−W, H−W/2]`, A-ramp `[H−W/2, H]`
  (W = `TransitionFadeWidth`, default 0.15); GrassToLava fixed rim R `[H−0.06, H−0.035]`,
  A `[H−0.035, H]`, jitter halved. A-ramps end at H with 255 → no seam at the gate.
- `Legacy` is byte-identical to the pre-style behavior (stamp at 0.1, grass rule 0.11) —
  the user's revert contract. Do not "fix" its threshold mismatch. Known contract
  artifact: its stride-2 subsampling misses ~1 lethal vertex at the test spot (raw ≥ 0.6
  whose stride neighbor sample is ≤ 0.1 renders green); LAVACHECK reports it as
  `CONTRACT`, excluded from the aggregate. The styled paths are strictly safer.
- Styles: `LegacySmooth` (default since v4.3 — user verdict on v4.2: "by far the best
  version"; see below), `MudBlend` (scorched-mud fade; the pre-v4.3 default), `AshBlend`
  (v3: MudBlend with NO mud — see below), `RockBlend` (v4: GrassToLava's tight rim
  rendered as gray rock — see below), `GrassToLava` (green close to the lava rivers,
  tight rim), `DebugGradient` (7 dev calibration strips; EXEMPT from the hold — strips must
  paint over lava; G-ramp strip renders yellow-green, not clean gray, so a StoneAsh style
  was evaluated and rejected). Knobs: `TransitionAshHold`, `TransitionFadeWidth`,
  `TransitionNoiseScale/Strength`, `TransitionBlurRadius` — all live-refresh via F1.
- **LegacySmooth (v4.1–4.2, TERRAIN_TRANSITION_V4_PLAN.md)**: Legacy's green↔full-ash
  look (no mud stage) with MudBlend's curve quality: MudBlend's exact fade geometry —
  same `[H−W, H]` anchors, same blurred+skirt+jitter field, same grass rule (shared code
  branch) — but R and A ramp TOGETHER along the `(t,0,0,t)` diagonal, i.e.
  `BandColor(m, H−W, H, H−W, H)`; the A-ramp reaching 255 at H hides the AshHold gate
  like every band style. Hard-won negative results (v4 runs 1–4, do NOT retry): a binary
  per-vertex stamp, at ANY threshold, on ANY smooth field, with ANY jitter, renders
  lattice-quantized 90°/45° edges — stamping at the hold additionally exposes the raw
  gate's own lattice at mask cliffs; a narrow (~2-cell) AA ramp fails the same way
  because the full green/ash contrast compresses into ~1 vertex. Curves require the
  fade to span the whole band width (~4 vertices), which is why the final design is
  just MudBlend's geometry with a diagonal color path.
- **LegacySmooth plains-line hiding (v4.2)**: the diagonal's mid-fade carries partial
  Plains weight (`1 − max(t, 1−t)`, peak 0.5), which renders the khaki slice-8 overlay
  as a yellow line floating INSIDE otherwise-green ground — green on BOTH sides, because
  the perceptual green/ash crossover sits near the ash end of the ramp (empirically
  charted with `LegacySmoothDebugRamp`, which paints the raw diagonal across each chunk;
  it bypasses the hold gate, dev only). Since the line is bounded by meadows, it is
  hidden UNDER meadows: `LegacySmoothSwapSlices` (default `8:0`) gives LegacySmooth its
  own patched diffuse array with grass copied over the plains slice, so the plains
  overlay renders as the grass that surrounds it. `8:0,3:0` also swaps the weak swamp-mud
  overlay (same mid-fade window) — visually indistinguishable in probes, but slice 3
  doubles as the hoe-path texture, so the targeted default preserves path visibility.
  Empty spec = vanilla array = the yellow line returns.
- **LegacySmooth line-mix sliders (v4.3, TERRAIN_TRANSITION_V43_PLAN.md)**: pure grass
  in the swapped slice still traced the old line as a subtle SATURATION bump — the plains
  overlay peaks mid-fade, where the base blend is grass diluted with ash (duller/grayer
  than pure slice 0). `LegacySmoothLineGrass/Ash/Mud/Khaki` (defaults 0.65/0.25/0.05/0.05,
  normalized by sum, live rebuild) bake a grass(0)/light-ash(13)/mud(3)/khaki(8) weighted
  average — RGB and alpha alike — into every `LegacySmoothSwapSlices` dst slice via the
  uncompressed graded-clone path; effectively-pure-grass weights keep the v4.2
  byte-identical compressed fast path, and only then; cache keys grow an `|lm` segment
  only when a mix is active (AshBlend/RockBlend keys untouched). Run-1 sweep measurements
  (difference-based line mask vs the pure-grass reference; `TerrainPhotoProbeLineMixes`
  probe): ash weight trades the top-down green-excess line (+6.7 pure → +1.9 at 0.45 ash)
  against a DARK line at oblique play angles (mean-RGB delta −1.2 pure → −7.2 at 0.45).
  The 0.65/0.25 default sits in the sweet spot (top dGE +3.4, oblique dRGB −4.9); nudge
  toward 0.75/0.15 if the line reads dark in-game, toward 0.55/0.35 if still too green.
  Sweep composites: `screenshots/terrain-transition/compare_legacysmooth_line_mix_*`.
- **AshBlend (v3, TERRAIN_NO_MUD_PLAN.md)**: green fades directly into ash; identical to
  MudBlend in band/skirt/grass code (only the material differs). Do NOT re-attempt a
  direct green→ash fade in vertex-color space: biome weights are Chebyshev distances
  from the corner colors (asm recon in SHADER_SLICE_MAPPING.md), so every path from
  Meadows to full AshLands crosses either Plains yellow (the `(t,0,0,t)` diagonal peaks
  at 0.5 Plains weight) or the swamp corner `(255,0,0,0)` = mud. The fix changes what
  the swamp corner renders AS: the swamp overlay is `_DiffuseArrayTex` slice 3,
  albedo-only (asm lines 382–397; never samples the normal array), so
  `ApplyStyleMaterial` (called per chunk per rebuild from `RebuildRenderMesh_Postfix`)
  assigns Ashlands chunks a session-cached cloned array (`Graphics.CopyTexture` —
  GPU-side, ignores isReadable; BC7 sRGB 256×256×16, single mip; one shared clone → no
  seams) with slice 13 copied over slice 3. **Source = slice 13 (light ash pair), NOT 7
  (main ash)**: near-black 7 renders the fade band so much darker than the pale hold
  zone that the binary AshHold gate reads as high-contrast 1m stair-steps wherever a
  raw-mask cliff pokes gated vertices into the band (v3 run-1 finding); 13 puts band and
  hold in one tonal family and the gate vanishes into the ash mottling.
  `AshBlendSwapSlices` (default "3:13", dst:src CSV, live rebuild) makes slice probing
  config-only. All other styles and the override-off rebuild path (`RestoreVanillaArray`
  in the postfix's early return) restore the vanilla array, so live style cycling can't
  leak; DebugGradient calibrates vanilla so it stays on the vanilla array by design.
  Slice 3 doubles as the paint-mask hoe-path texture → player paths on Ashlands chunks
  render ash-toned under AshBlend (accepted as thematic).

**Autonomous verification**: `TerrainPhotoAuto` (or F7) teleports to `TerrainPhotoPos`
(default "129,30,-9671", the historical problem spot), captures a vanilla ground-truth
set (override disabled), then cycles all 7 styles with a terrain refresh each, running
LAVACHECK + GRASSCHECK per style and capturing top-down + oblique + lava-edge close shots
into `AR_TerrainPhoto\` (checks + shot paths in DONE.txt, `[AR TerrainPhoto] DONE` in the
log). v4 extras (all off by default): `TerrainPhotoRefCapture` visits the pickaxe-dug
rock strip at `TerrainPhotoRefPos` (149,30,-9600) first — vanilla + GrassToLava sets plus
an `[AR TerrainPhoto] REFGRID` dump (veg mask, paint RGBA, mesh normal Y, vertex color;
v4 recon: dug paint RGB is all zero and normal Y drops to 0.63–0.98, so the dug-rock look
is slope-path geometry, not a paint channel); `TerrainPhotoProbeSpecs` appends RockBlend
capture sets per swap spec (+ one wide-band variant); `TerrainPhotoProbeAshBrightness`
appends AshBlend sets per brightness value (band-tone calibration measures these).
Outer loop: kill valheim → **delete the BepInEx log** (stale DONE markers from the
previous run otherwise satisfy the poll instantly) → `dev.ps1` → poll for "starting
game", then for the DONE marker → **kill the game immediately** → read PNGs.
Gallery (v4-refreshed): `screenshots/terrain-transition/`.

### Config → feature guard pattern

Every feature checks `Plugin.MasterSwitch` plus its own toggle before acting:
```csharp
public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
```

All `ConfigEntry` properties are `public static` so patch classes read them directly from `Plugin.*` without needing an instance.

### Fable Warrior puppet (CURRENT Charred Warrior system — covers all four charred creatures)

`FableWarriorPatches.cs` is the Charred Warrior visual system (the old legacy bind-pose
armor swap has been removed). Core idea: player-authored meshes never leave the skeleton
they were authored for. A stripped, visual-only Player prefab ("puppet") is instantiated
as a child of the creature's `Visual` node, the Charred's own renderers are hidden, and
every `MonoUpdaters.LateUpdate` the Charred bones' rotations are retargeted onto the
matching puppet bones (shared Mixamo names) via deviation-from-rest transfer, with a
computed rest-pose alignment baked in for the 6 arm-chain bones (their rest poses differ
by a ~28/48/59.5° constant). Globally active when `MasterSwitch` is on AND
`FableRaceMode != "Vanilla"` (`Plugin.IsFablePuppetActive`); per-creature enablement is the
warrior's `EnableFableWarrior` (Disabled = off) and the other classes' `EnableFable[Class]`
enums. The puppet **body** is driven by the global `FableRaceMode` (Fable Race section):
`Vanilla` = master-off (all Charred native, overrides `EnableFable[Class]`), `ClonePlayer` =
copy the local player's sex/skin/hair/beard (legacy), `CustomRace` (default) = a fixed race
from the Fable Race config, applied in `ApplyAppearance` right after the player clone via
`VisEquipment.SetModel/SetHairItem/SetBeardItem/SetSkinColor/SetHairColor`.

**Per-creature profile table** (`FableWarriorPatches.Profiles`): the same machinery covers
`Charred_Melee` (warrior, weapon right hand), `Charred_Archer` (weapon LEFT hand — bow),
`Charred_Twitcher`/`Charred_Twitcher_Summoned` (weapon right hand), and `Charred_Mage`
(weapon right hand — staff). **All four classes now share the same tri-state config system**
(see "Fable modes" below): each has its own `EnableFable[Class]` dropdown, body/helmet/weapon
scales, and CustomEquipment armor/weapon item IDs in its own config section. Only the Warrior
has the extra weapon-grip knobs. Retarget offsets are computed and cached PER charred prefab
(the variants share bone names, and empirically the same rest poses, but this is not assumed).

**Fable modes** (`EnableFable[Class]`, parsed by `FableWarriorPatches.ParseMode()`; per-class
for Warrior/Archer/Twitcher/Mage): `Disabled` = profile disabled, no puppet, 100% vanilla
Charred; `ClonePlayer` = clone the player's body + armor AND the player's real equipped weapon
(attached even if it's the "wrong" style; kept in sync via the resync signature),
rig-normalized at natural size; `CustomEquipment` (default) = clone the player's BODY only,
then override the armor slots + weapon with the `Fable[Class] Helmet/Chest/Legs/Shoulders/
Weapon` item IDs (empty = bare slot). Defaults: Warrior = Knight set + Krom; Archer = Knight +
BowAshlands; Mage = `chiefhelmdeer` + `frostmagechest`/`frostmagelegs` + `StaffIceShards`;
Twitcher = Fenris + FistFenrirClaw (shoulders empty for all). The mode is routed through
per-profile `Func<>`s on `CreatureProfile`
(`OverrideArmor`, `Helmet/Chest/Leg/ShoulderItem`, `KeepClonedHands`, `WeaponGrip`,
`HelmetScale`, `RightItem`/`LeftItem` for the weapon hand). Only the Warrior sets `WeaponGrip`
(+ its grip config); the other three rig-normalize their weapon like the old bow/staff.

Key mechanics (details in the file's doc comments):
- **Inactive strip**: the puppet is instantiated under an inactive holder so no gameplay
  `Awake` runs; all MonoBehaviours except `VisEquipment` are destroyed (multi-pass for
  RequireComponent chains), plus Rigidbody/Colliders; the Animator is kept but disabled.
- **No-ZDO VisEquipment**: `m_nViewOverride` is set to a session-static ZNetView on an
  inactive GameObject (its `GetZDO()` stays null), so all `Set*Item` calls run in local mode.
- **Appearance**: `Humanoid.SetupVisEquipment` (reflection) clones the local player's full
  gear/beard/hair/skin onto the puppet; then per the profile the hands (and, for warrior
  CustomEquipment, the armor slots) are overridden by name via the vanilla public
  `VisEquipment.Set{Helmet,Chest,Leg,Shoulder,Right,Left}Item` API (item IDs resolve against
  ObjectDB — no manual prefab lookup). A ~2s signature diff in `PeriodicUpdate` re-clones on
  real player equip changes; the signature now includes hand/back items so a warrior
  ClonePlayer puppet tracks the player's weapon.
- **Rigid-attach scale gotcha**: vanilla `AttachItem` parents rigid attaches (helmet, sword)
  with `worldPositionStays=true`, which back-compensates `localScale` by the joint's
  `lossyScale` — on the ~1.4× scaled puppet rig, helmets rendered player-sized ("too small,
  perched on the crown"). `FixupPuppetAttaches` re-scales the helmet instance by the
  puppet-vs-prefab helmet-joint lossy ratio × the profile's `HelmetScale()` (warrior =
  `FableWarriorHelmetScale`, other classes = 1.0 — the helmet scale is now Warrior-scoped, no
  Y-offset). The warrior's CustomEquipment weapon keeps `FableWarriorWeaponScale` sizing plus
  `FableWarriorWeaponGripRot*` grip tuning (rotation-only; calibrated so the resting blade lies
  on the shoulder, not through the trapezius); ClonePlayer weapons rig-normalize at natural
  size. Skinned attaches (`attach_skin` armor) are immune — bones + bind poses drive them.
- **Creature-weapon attach fallback** (`EnsureCreatureWeaponAttached`, called from
  `FixupPuppetAttaches`): vanilla `VisEquipment.AttachItem` only mounts a child literally named
  `attach` (or `attach_skin`). Staffs wielded by creatures (Dvergr, etc.) put their held mesh
  under an `attach_r.hand` / `attach_l.hand` child instead, so vanilla leaves the hand empty even
  though the item IS in ObjectDB (`m_*ItemInstance` stays null). When that instance field is empty
  and the configured weapon name resolves (ObjectDB → ZNetScene fallback), we instantiate the
  first recognized attach child (`attach`, `attach_r.hand`, `attach_l.hand`, `attach_skin`) onto
  the hand joint (colliders off, transform reset, equipoffset applied) and write it back into
  `m_*ItemInstance` so it flows through the normal scale/cleanup path. This is what lets
  `FableMageWeapon` accept creature-only staffs (`DvergerStaffFire/Ice/Support/Heal`,
  `charred_magestaff_fire`, …). Standalone bake-free limitation: the Bog Witch's staff is a
  SkinnedMeshRenderer baked into the creature (path `BogWitch/BogWitch/staff`), not an item, so it
  can't be equipped this way; `DvergerStaffNova`/`DvergerStaffBlocker` have no recognized attach
  child at all (nothing to mount — excluded from the dropdown).
- **Mage staff orientation** (`StaffOrientationDefaults` in `FableWarriorPatches.cs`): the mage's
  (non-grip) right-hand fixup applies a built-in per-staff-family rotation after the rig-normalize
  scale, because the Charred idle pose holds the hand joint so staffs read wrong otherwise:
  `Staff*` (player) X+90 = vertical head-up matching the player's own carry; `DvergerStaff*`
  Y+130 = head up-forward ~40° matching the Dvergr mages; `charred_magestaff*` Y+75 = head-down
  mid-shaft matching the vanilla Charred Warlock (claw hangs at the ground — vanilla-like).
  Composition contract: `localRotation *= Euler(default) * Euler(knob)`, `localPosition +=
  defaultPos + knobPos`, applied AFTER the attach's equipoffset; the knobs are the
  `FableMageWeaponRotX/Y/Z` / `OffsetX/Y/Z` configs (live rebuild). Sweep-verified per staff
  against vanilla-carry references; galleries in `screenshots/fable-mage-staffs/`
  (refs/ = ground truth, final/ = shipped defaults). Grip position offsets for the charred staff
  were probed (±0.15 every axis) and rejected — the shaft is not joint-axis-aligned, so
  single-axis offsets visibly disconnect the hand from the shaft.
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

### Fable Bunny (Morgen → giant Hare)

`FableBunnyPatches.cs` replaces the Morgen's bone-and-sinew visuals with a giant,
self-animating donor creature (default Hare) without touching gameplay. The Morgen's rig
shares no bone names with any pleasant donor, so unlike the Fable puppet (bone retarget)
the donor keeps its OWN Animator and is state-synced to the Morgen instead:

- **Swap**: `Character.Awake` postfix (Morgen IS a `Humanoid`, but the hook is
  Character-level) → hide all Morgen renderers, force its Animator `AlwaysAnimate`
  (animation events drive attack hitboxes — never disable it), build a stripped donor
  clone (inactive-holder pattern; Animator kept ENABLED — the one difference from
  `StripPuppet`) on an upright pivot under the Morgen root.
- **Scale**: absolute `FableBunnyHeight` (default 4m) × star scale ÷ donor raw bounds
  height. Do NOT use the Morgen's live render bounds — they are pose-inflated (measured
  9.4m mid-animation vs its 2.2m capsule).
- **Locomotion**: every `MonoUpdaters.LateUpdate`, Morgen planar velocity → donor's
  `forward_speed`/`turn_speed` animator params (the same ones ZSyncAnimation writes; both
  hare_animator and lox_animator have them). Pivot gets a yaw-only world rotation each
  frame (the roll's tumble lives entirely in hidden bones — root/Visual stay upright,
  recon-verified).
- **Attacks**: `Humanoid.StartAttack` postfix classifies by attack anim name
  (recon-verified names: `attack_bite`, `roll_left/right`, `attack_swipe_1..4`,
  `attack_slam`) → procedural pounce (squash-stretch + lunge pitch on the pivot, composes
  with the donor's Animator). Roll floors `forward_speed` (the bunny bounds at up to
  19 m/s real roll velocity). Hybrid mode swaps in a second Lox proxy for bite/roll and
  fires its real `attack_bite` trigger. **Swap-back gotcha**: `InAttack()` is false for a
  few frames after `StartAttack` — the swap-back check needs its 0.6s grace period or the
  Lox reverts before rendering a single frame.
- **Death**: Morgen has NO ragdoll (recon: death effects are fx+sfx only), so the corpse
  concern is moot; `Ragdoll.Awake` postfix still hides any morgen-named ragdoll as
  insurance behind `FableBunnyHideRagdoll`.
- **Lifecycle**: marker `AshlandsRebornFableBunny`, `RevertAll`/`RefreshAll` (one-frame
  wait after revert, same as warrior), wired into `ApplyMasterSwitch` and `SettingChanged`
  for every Fable Bunny config key (all changes rebuild live via F1).

**Autonomous verification**: `FableBunnyReconDump=true` (Dev Automation) dumps rig/
animator/attack recon for Morgen/Hare/Lox instances (`[AR BunnyRecon]` lines), then ~90s
after world load runs a full self-test: spawns a Morgen, forces all 8 attacks through
`Humanoid.StartAttack` (the AI alerts but never commits to attacks on the player-built
test platform, so forcing is required), captures camera-tracked screenshots per attack,
kills it, and asserts the MasterSwitch/RefreshAll lifecycle — `[AR BunnyRecon]
OBSERVATION DONE pass=X fail=Y` + PNGs in `AR_PhotoMode\`. Review gallery:
`screenshots/fable-bunny/`.

**v2 elemental modes (M3/M4, commit `64a049e`)**: `FableBunnyMode` (Bunny / LightElemental /
LightningElemental — F1 dropdown, live rebuild) is the single rotate knob. Both elementals
skip the donor entirely and anchor procedural FX to the hidden, still-animating Morgen bones
(`FindMorgenBones`: Chest, Hand.l/r, and the 4 dot-suffixed limb chains). LightElemental:
blinding pulsing core orb rides `Chest` (soft-shadow point light, random flare spikes), hand
orbs always-track the hands (M1 wisp machinery + `AlwaysTrackHands`), LineRenderer beam on
bite/slam, roll = the core drops to ground level and bounces as a marble. LightningElemental:
flickering core + jagged LineRenderer bolts over each limb chain (bone points + midpoints
re-jittered ~0.05s); arm bolts thicken/brighten during swipes, jitter doubles while rolling.
All FX live under the pivot (revert cleanup free) and are purely procedural — no prefab pick
is load-bearing. The light beam is named `AR_WispOrb_beam` deliberately: PhotoModePatches'
framing exclusion matches the `AR_WispOrb` prefix, and a 20m beam would wreck auto-framing.
M2 FX recon = `DumpFxCatalog` under `FableBunnyReconDump` (ZNetScene component/shader catalog
+ Eikthyr/GoblinKing EffectList dumps) for future visual upgrades — good candidates logged:
`fx_Lightning`, `fx_chainlightning_hit/spread`, `fx_DvergerMage_Mistile_*`. The self-test
cycles all three modes through the forced-attack gallery; v2 galleries live under
`screenshots/fable-bunny/v2/<mode>/`.

### Fable Bunny config (section "Fable Bunny")

v2 (commit `d126117`) dropped the hybrid Lox mode (user review: janky swap, too plain) and
removed its keys (`FableBunnyHybridMode`, `FableBunnyLoxScale`, `FableBunnyLoxAttackTrigger`).

| Config key | Default | Effect |
|---|---|---|
| `EnableFableBunny` | true | Swap Morgen visuals for the donor creature |
| `FableBunnyMode` | "Bunny" | THE rotate knob: Bunny / LightElemental / LightningElemental (live rebuild) |
| `FableBunnyDonor` | "Hare" | Donor prefab (Hare, Lox, Wolf, Deer...); live-rebuilds on change (Bunny mode) |
| `FableBunnyHeight` | 4.0 | Target donor height in meters (× star scale) |
| `FableBunnyScale` | 1.0 | Multiplier on the height-derived scale |
| `FableBunnyYOffset` | 0 | Vertical offset after ground alignment |
| `FableBunnyPounceAmplitude` | 1.0 | Strength of the procedural attack pounce (0 = off) |
| `FableBunnyStarLook` | 0 | Apply donor's 1★/2★ LevelEffects tint regardless of real level (0=base; rebuilds live) |
| `FableBunnyMoveAnimSpeed` | 0.55 | Animator speed while moving ("moonwalk" fix); idle always full rate |
| `FableBunnyLashStyle` | "Wisps" | Swipe read: wisp orbs orbit + lash along hidden `Hand.l/r` (EarWhip/Both arrive with M5) |
| `FableBunnyRollStyle` | "HopHigher" | Roll read: face travel dir + real jump trigger + bounce arcs (CurlAndRoll arrives with M5) |
| `FableBunnyHideRagdoll` | true | Hide any morgen-named ragdoll renderers (insurance) |

**v2 hide/fx machinery gotchas** (all recon-verified, expensive to re-derive):
- The Morgen wake-up "rise" clip ANIMATES its renderers back on every frame; hiding uses
  `forceRenderingOff` + an invisible-material swap (`InvisibleMaterials`), both
  clip-proof. Plain `renderer.enabled=false` LOSES to the Animator.
- Morgen bone-gore effects are separate spawned objects, suppressed by name in an
  `EffectList.Create` postfix (`fx_morgen_roll`, `fx_morgen_death`, plus
  `fx_Abomination_arise*` when near a swapped Morgen). `MonsterAI.m_wakeupEffects` =
  `fx_Abomination_arise_end` (name shared with Swamp Abomination - proximity-gated).
- Wisp orb visual = stripped `demister_ball` clone (probe list + procedural fallback);
  orbs/trails are excluded from `PhotoModePatches.AimCameraAt` framing bounds.
- Self-test additions: island teleport + solid-ground spawn probe + per-attack
  re-anchoring (rolls carry the Morgen off the platform), star-look cycling captures,
  `FreezeAI` for still tint shots, frustum-scan + EffectList-inventory recon dumps.
- Star-look capture bug: RESOLVED (user-verified in-game 2026-07-11). `FableBunnyStarLook`
  rotates perfectly in-app (the 2★ white hare looks notably good); the empty star-look
  GALLERY shots were a capture-harness artifact only. No code fix needed — don't reopen
  unless in-game behavior regresses.

## Key Config Entries (runtime-tweakable via F1 in-game with ConfigurationManager)

| Config key | Default hotkey | Effect |
|---|---|---|
| `MasterSwitch` | Backspace | Toggle all features |
| `TerrainPhotoKey` | F7 | Run the terrain transition photo harness on demand |
| `TreeRefreshKey` | F8 | Respawn tree replacements |
| `ValkyrieRefreshKey` | F9 | Re-apply the Valkyrie swap to nearby Fallen Valkyries |

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
| `MageWeaponTest` | false | Run the mage-weapon harness once after world load: dumps the staff catalog, spawns a Charred_Mage, cycles `MageWeaponTestList` through `FableMageWeapon`, shoots each in-hand into `AR_MageWeapon\` |
| `MageWeaponTestList` | (13 staffs) | CSV of staff prefab IDs the mage-weapon harness cycles. Must stay a subset of the `FableMageWeapon` dropdown (BepInEx clamps out-of-list values to the list's FIRST entry on set) |
| `MageWeaponRefCapture` | false | Prepend a reference phase to the harness run: player equipping each player staff (`ref_player_*`), vanilla DvergerMageFire/Ice/Support (`ref_DvergerMage*`), puppet-disabled Charred_Mage (`ref_Charred_Mage_*`) — same framing as the puppet shots |
| `MageWeaponRotSweep` | "" | `rx,ry,rz[,ox,oy,oz]` entries separated by `\|`: capture each staff once per entry with the FableMageWeaponRot/Offset knobs set to it (knobs restored after). Sweeps are RELATIVE to the baked per-staff defaults |
| `PhotoModeIslandPos` | "2736,40,2580" | Test island teleport target; empty disables |
| `TerrainPhotoAuto` | false | Run the terrain photo harness once after world load (suppressed by PhotoModeAuto/M4Test) |
| `TerrainPhotoKey` | F7 | Run the terrain photo harness on demand |
| `TerrainPhotoPos` | "129,30,-9671" | Transition test spot (historical grid/yellow-line area); empty disables teleport |

### Fable Warrior config (section "Fable Warrior" + per-creature sections)

| Config key | Default | Effect |
|---|---|---|
| `EnableFableWarrior` | "CustomEquipment" | Warrior mode dropdown AND on/off switch: `Disabled` (no puppet) / `ClonePlayer` (player body+armor+weapon) / `CustomEquipment` (player body + configured armor/weapon) |
| `FableWarriorScale` | 1.0 | Multiplier on the auto-computed height-match scale |
| `FableWarriorHelmet` / `Chest` / `Legs` / `Shoulders` | "knighthelm" / "knightchest" / "knightlegs" / "" | CustomEquipment armor slot item IDs (empty = bare); Knight IDs are SouthsilArmor |
| `FableWarriorHelmetScale` | 1.0 | Fine-tune the (already scale-normalized) puppet helmet; each class has its own (`Fable[Class]HelmetScale`) |
| `FableWarriorWeapon` | "THSwordKrom" | CustomEquipment right-hand weapon item ID (empty = bare hand); default Krom |
| `FableWarriorWeaponScale` | 1.16 | CustomEquipment weapon size (ClonePlayer weapons keep natural size) |
| `FableWarriorWeaponGripRotX/Y/Z` | -17.75 / 15.21 / -121.69 | CustomEquipment weapon grip rotation (deg, hand-attach frame); calibrates the shoulder rest (grip tuning WIP) |

Every key in the Fable Warrior/Archer/Twitcher/Mage sections applies **instantly** via
`SettingChanged` (rebuilds the affected puppets live) — there is no manual refresh hotkey.
Old keys auto-migrate on first load and are purged from the cfg orphan store:
`ClonePlayerTo[Class]` bool → `EnableFable[Class]` enum (`true`→ClonePlayer, `false`→Disabled);
`FableArcherBow`/`FableMageStaff` → `Fable[Class]Weapon`; `FableArcherBowScale`/
`FableMageStaffScale` → `Fable[Class]WeaponScale`; plus the Warrior's earlier renames
(`FableWarriorSwitch`, `FableHelmetScale`, `WarriorKromScale`, `FableKromGrip*`).

Sections **"Fable Archer" / "Fable Twitcher" / "Fable Mage"** mirror the Warrior's keys
(`EnableFable[Class]` tri-state, `Fable[Class]Scale`, `Fable[Class]Sex`, `Fable[Class]Helmet/
HelmetScale/Chest/Legs/Shoulders`, `Fable[Class]Weapon`, `Fable[Class]WeaponScale`) — but with
**no grip knobs** (Warrior-only). CustomEquipment defaults: Archer =
`norahhelmalt`/`norahchest`/`norahlegs` + `BowAshlands` (LEFT hand, `WeaponScale` 1.3); Mage =
`chiefhelmdeer`/`frostmagechest`/`frostmagelegs` + `StaffIceShards` (right hand); Twitcher =
`HelmetFenring`/`ArmorFenringChest`/`ArmorFenringLegs` + `FistFenrirClaw` (right hand).
Shoulders empty for all. **`Fable[Class]Sex`** (Male/Female, CustomRace only) is per-class —
default **Female for the Archer**, Male for the others — driving `VisEquipment.SetModel(0/1)`.

**`FableMageWeapon` is a dropdown** (AcceptableValueList, F1-selectable): the 8 player staffs
(`StaffIceShards` first = clamp fallback, `StaffFireball`, `StaffShield`, `StaffSkeleton`,
`StaffRedTroll`, `StaffGreenRoots`, `StaffLightning`, `StaffClusterbomb`), the creature staffs
`DvergerStaffFire/Ice/Support/Heal` (`Heal` = the support mage's lamp staff, `Support` = the
green orb) + `charred_magestaff_fire` (via the attach fallback above), and `None` = bare hand
(the profile maps it to ""). Every entry ships with a baked orientation so it sits naturally in
the mage's hand (see "Mage staff orientation" above); `FableMageWeaponRotX/Y/Z` and
`FableMageWeaponOffsetX/Y/Z` (section "Fable Mage", live rebuild) fine-tune on top. BepInEx
clamps any out-of-list value to the FIRST list entry both on file read and on programmatic set.

### Fable Race config (section "Fable Race")

A single global section that defines the **body** of all four Fable Charred puppets (NOT the
Fable Bunny). Overrides the per-puppet player-clone in `ApplyAppearance`.

| Config key | Default | Effect |
|---|---|---|
| `FableRaceMode` | "CustomRace" | `Vanilla` = no puppets, all Charred native (overrides every `EnableFable[Class]`; folded into `IsFablePuppetActive`) / `ClonePlayer` = bodies copy the player (legacy) / `CustomRace` = bodies use the settings below |
| _(sex is per-class)_ | — | Body sex lives in each class section as `Fable[Class]Sex` (Male/Female → `SetModel(0/1)`; beards render only on Male). Default Female for Archer, Male for others |
| `FableRaceHair` | "Hair5" | hair item (`HairNone`, `Hair1`..`Hair23`); `Hair5`/`Hair8` are short |
| `FableRaceBeard` | "BeardNone" | beard item (`BeardNone`, `Beard1`..`Beard16`) |
| `FableRaceSkinTone` | 1.0 | 0 = lightest, 1 = darkest; Lerp between hardcoded skin endpoints (vanilla gamut bottoms out ~70% grey) |
| `FableRacePureBlackSkin` | false | Force skin to pure black (0,0,0), darker than the slider reaches; overrides `FableRaceSkinTone`. Eyes keep their color; the lit shader still shows form (specular/rim/shadow) at true black |
| `FableRaceHairTone` | 1.0 | 0 = lightest, 1 = darkest; Lerp along the hair gradient |
| `FableRaceBlondness` | 0.0 | 0 = darkest, 1 = brightest; brightness multiplier on the toned hair |

Skin/hair Vector3s come from `ComputeRaceSkinColor`/`ComputeRaceHairColor` in
`FableWarriorPatches.cs` (hardcoded endpoint constants approximating Valheim's char-creation
gradients — the game's real endpoints are serialized on the FejdStartup prefab, not in code).
Every key live-rebuilds all puppets via `SettingChanged` → `OnFableWarriorModeChanged`.

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