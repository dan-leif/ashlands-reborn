# Ashlands Reborn - Weather Override

BepInEx plugin that overrides Ashlands environment to Meadows-like weather (clear sky, no cinder rain, no lava fog) when the player is in the Ashlands biome.

## Requirements

- Valheim (with Ashlands update)
- BepInExPack Valheim 5.4.2333+

## Installation

1. Ensure BepInEx is installed (via r2modman or manually).
2. Copy `AshlandsReborn.dll` to `BepInEx/plugins/AshlandsReborn/`.

## Configuration

- **EnableWeatherOverride** (default: true) - Toggle the Ashlands weather override.

Config file: `BepInEx/config/com.ashlandsreborn.weather.cfg`

## Building

```powershell
cd AshlandsReborn
dotnet build
```

If Valheim is not at the default Steam path:
```powershell
dotnet build -p:GamePath="C:\path\to\Valheim"
```

Or run `.\CopyRefs.ps1 -GamePath "C:\path\to\Valheim"` to copy game assemblies to `Lib/`, then build.

## Testing

1. Launch Valheim with BepInEx.
2. F5 → `devcommands` → `debugmode`
3. Teleport to Ashlands (southern edge of map).
4. Verify: clear sky instead of cinder rain.
