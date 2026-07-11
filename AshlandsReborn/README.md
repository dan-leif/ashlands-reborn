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
- **TerrainTransitionStyle** (default: MudBlend) - How green terrain fades into ash/lava. `MudBlend` = grass -> scorched mud -> ash -> lava with organic noisy edges. `GrassToLava` = grass runs almost to the lava rivers with a tight mud/ash rim. `Legacy` = the original binary stamp (blocky edges + yellow fringe). `DebugGradient` = dev calibration strips. Changing it live-rebuilds nearby terrain.
- **TransitionNoiseScale / TransitionNoiseStrength** (defaults: 0.08 / 0.08) - Frequency and amplitude of the edge-breakup noise on the transition contours.
- **TransitionBlurRadius** (default: 2) - Lava-mask blur in vertices; smooths the banding.
- **TransitionAshHold** (default: 0.2) - Lava-mask level at/above which terrain always renders as full vanilla ash (evaluated on the raw mask), keeping the glowing lava rim/cracks and the deadly lava boundary exactly vanilla. Lower = more vanilla ash retained around lava.
- **TransitionFadeWidth** (default: 0.15) - Width of the MudBlend green -> mud -> ash fade band below the ash hold; smaller = narrower mud band.
- **EnableDevCommandsAndGodMode** (default: true) - When loading a world, run devcommands and god for easier testing.

Use **ConfigurationManager** (F1 in-game) to toggle these at runtime without restarting.

Config file: `BepInEx/config/com.ashlandsreborn.weather.cfg`

## Known Limitations

- **Distant terrain:** Far-away (distant-LOD) chunks render uniformly green without lava detail; detail appears as chunks stream in.
- The historical blocky transitions + yellow seam of the original override survive only in `TerrainTransitionStyle = Legacy`, kept as a revert option; the default MudBlend style replaces them with an organic fade.

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
