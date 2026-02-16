# Ashlands Reborn - Weather Override

BepInEx plugin that overrides Ashlands environment to Meadows-like weather (clear sky, no cinder rain, no lava fog) when the player is in the Ashlands biome.

## Requirements

- Valheim (with Ashlands update)
- BepInExPack Valheim 5.4.2333+

## Installation

**Using r2modman:** Settings → Locations → Browse Data Folder, then copy `AshlandsReborn.dll` to `profiles/[YourProfile]/BepInEx/plugins/AshlandsReborn/`. Or use "Import local mod" and select the folder containing the DLL.

**Manual:** Copy `AshlandsReborn.dll` to your game's `BepInEx/plugins/AshlandsReborn/` folder.

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
