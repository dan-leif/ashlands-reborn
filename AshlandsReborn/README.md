# Ashlands Reborn - Weather Override

BepInEx plugin that overrides Ashlands environment to Meadows-like weather (clear sky, no cinder rain, no lava fog) when the player is in the Ashlands biome.

## Requirements

- Valheim (with Ashlands update)
- BepInExPack Valheim 5.4.2333+

## Installation

**Using r2modman:** Settings → Locations → Browse Data Folder, then copy `AshlandsReborn.dll` to `profiles/[YourProfile]/BepInEx/plugins/AshlandsReborn/`. Or use "Import local mod" and select the folder containing the DLL.

**Manual:** Copy `AshlandsReborn.dll` to your game's `BepInEx/plugins/AshlandsReborn/` folder.

## Configuration

- **MasterSwitch** (default: true) - Master toggle: turn the entire mod on or off. When off, Ashlands uses default weather and terrain.
- **EnableWeatherOverride** (default: true) - Override Ashlands weather to Meadows-like (clear sky, no cinder rain, no lava fog).
- **EnableTerrainOverride** (default: true) - Override Ashlands terrain and grass to Meadows-like (green ground, green grass).
- **TerrainTransitionStyle** (default: MudBlend) - How green terrain fades into ash/lava. `MudBlend` = grass -> scorched mud -> ash -> lava with organic noisy edges. `AshBlend` = the same fade but with no mud at all: grass fades directly into ash (the mod re-textures the mud layer as tone-graded ash on Ashlands chunks). `RockBlend` = a tight gray-rock rim between grass and lava, imitating the look of pickaxe-dug ground at the lava's edge. `GrassToLava` = grass runs almost to the lava rivers with a tight mud/ash rim. `Legacy` = the original binary stamp (blocky edges + yellow fringe). `LegacySmooth` = green blends straight into ash across one smooth curved fade, no mud stage (the yellow plains tinge the blend would produce mid-fade is re-textured as meadows grass - see LegacySmoothSwapSlices). `DebugGradient` = dev calibration strips. Changing it live-rebuilds nearby terrain.
- **TransitionNoiseScale / TransitionNoiseStrength** (defaults: 0.08 / 0.08) - Frequency and amplitude of the edge-breakup noise on the transition contours.
- **TransitionBlurRadius** (default: 2) - Lava-mask blur in vertices; smooths the banding.
- **TransitionAshHold** (default: 0.2) - Lava-mask level at/above which terrain always renders as full vanilla ash (evaluated on the raw mask), keeping the glowing lava rim/cracks and the deadly lava boundary exactly vanilla. Lower = more vanilla ash retained around lava.
- **TransitionFadeWidth** (default: 0.15) - Width of the MudBlend/AshBlend fade band below the ash hold; smaller = narrower fade band.
- **AshBlendSwapSlices** (default: 3:13) - AshBlend dev tuning: which terrain texture-array slices get overwritten by which (dst:src pairs). 3:13 renders the mud layer as the lighter ash-pair texture; 3:7 (main ash) is darker but shows the ash-hold contour as hard steps.
- **AshBlendBandBrightness / AshBlendBandTint / AshBlendBandMix** (defaults: 1.43 / #DBE8FF / 0) - AshBlend band-tone grading: the swapped-in band texture is brightened, tinted, and optionally mixed with the grass texture so the fade band tonally matches the adjacent full-ash ground (a stock slice alone is much darker than the ash zone's multi-layer composite). Defaults are calibrated to within ~1% mean luminance. Tint components must stay <= 1; put overall lift into the brightness.
- **AshBlendVariationColor** (default: olive #66804C) - The variation-overlay tint on the full-ash zone under AshBlend; lighter = paler ash side.
- **RockBlendSwapSlices / RockBlendBandBrightness** (defaults: 3:5 / 1.0) - RockBlend's rock texture pick (3:5 = scaly base rock, 3:12 = cobblestone) and optional tone grading.
- **LegacySmoothSwapSlices** (default: 8:0) - Hides the yellow plains line that LegacySmooth's green->ash blend would otherwise draw mid-fade, by rendering the plains texture layer as meadows grass on Ashlands chunks. Empty = vanilla textures (the line returns); '8:0,3:0' also re-textures the weak mud overlay (but makes hoe paths render grassy).
- **EnableDevCommandsAndGodMode** (default: true) - When loading a world, run devcommands and god for easier testing.

Use **ConfigurationManager** (F1 in-game) to toggle these at runtime without restarting.

Config file: `BepInEx/config/com.ashlandsreborn.weather.cfg`

## Known Limitations

- **Distant terrain:** Far-away (distant-LOD) chunks render uniformly green without lava detail; detail appears as chunks stream in.
- The historical blocky transitions + yellow seam of the original override survive only in `TerrainTransitionStyle = Legacy`, kept as a revert option; the default MudBlend style replaces them with an organic fade. `LegacySmooth` keeps Legacy's direct green-to-ash look but with MudBlend's curved contours; a soft warm tinge mid-fade is what remains of Legacy's yellow fringe.

## Building

```powershell
cd AshlandsReborn
dotnet build
```

If Valheim is not at the default Steam path:
```powershell
dotnet build -p:GamePath="C:\path\to\Valheim"
```

The DLL is copied to both the game's `BepInEx/plugins/AshlandsReborn/` and the r2modman "Ashlands Reborn" profile (`.../profiles/Ashlands Reborn/BepInEx/plugins/AshlandsReborn/`). For a different profile:
```powershell
dotnet build -p:ProfilePluginsPath="C:\path\to\profiles\MyProfile\BepInEx\plugins"
```

Or run `.\CopyRefs.ps1 -GamePath "C:\path\to\Valheim"` to copy game assemblies to `Lib/`, then build.

## Testing

1. Launch Valheim with BepInEx.
2. F5 → `devcommands` → `debugmode`
3. Teleport to Ashlands (southern edge of map).
4. Verify: clear sky instead of cinder rain.
