# Dev User Fix Status

## Problem
The Dev user gets errors when launching Valheim via r2modman (Ashlands Reborn profile).
Launching vanilla from Steam works fine — the issue is specific to the r2modman launch path.

**Why r2modman is different:** r2modman launches via:
```
Steam.exe -applaunch 892970 --doorstop-target-assembly "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\core\BepInEx.Preloader.dll"
```
This causes BepInEx's Chainloader to fire all plugin `Awake()` methods **before** Valheim's own `SteamAPI.Init()` runs. SouthsilArmor's `ItemManagerModTemplate.Awake()` calls `Localization.instance`, which triggers Steam-dependent Valheim code too early.

---

## Root Cause: Call Chain

```
ItemManagerModTemplate.Awake()
  -> ItemManager.LocalizeKey.English()
  -> Localization.get_instance()
  -> Localization.Initialize()
  -> Localization..ctor()
  -> Localization.SetStartupLanguage()    <- everything breaks here
  -> Localization.SetLanguageFromLocale() <- NullReferenceException (current error)
  -> PlatformPrefs.GetString()
  -> PlatformPrefs.MigratePlatformKeyIfNeeded()
  -> SteamUtils.IsSteamRunningOnSteamDeck() <- original InvalidOperationException
```

The Admin user never hits this — their Steam process is already warm from prior sessions.

---

## Fixes Applied So Far (all sessions combined)

| # | Issue | Fix | Status |
|---|-------|-----|--------|
| 1 | Dev `mods.yml` missing Configuration Manager | Added entry to Dev's `mods.yml` | ✅ Done |
| 2 | Dev `com.ashlandsreborn.weather.cfg` had `CharredWarriorSwap = false` | Copied Admin's config | ✅ Done |
| 3 | Dev `southsil.SouthsilArmor.cfg` was 3.6 KB (incomplete) vs Admin's 574 KB | Copied Admin's config | ✅ Done |
| 4 | Dev `AshlandsReborn.dll` was wrong version (99,840 bytes vs 90,112) | Replaced with correct build | ✅ Done |
| 5 | Stale BepInEx cache on Dev | Cleared | ✅ Done |
| 6 | `InvalidOperationException: Steamworks is not initialized` | Added `SteamworksInitCompatibility` patch suppressing exception in `MigratePlatformKeyIfNeeded` | ✅ Suppressed |
| 7 | `NullReferenceException` in `Localization.SetLanguageFromLocale` | **Needs fix** — see below | ❌ Pending |

---

## Current Error (last run)

```
[Error  : Unity Log] NullReferenceException: Object reference not set to an instance of an object
Stack trace:
Localization.SetLanguageFromLocale () (at <f9c1e10e233e4d07bcb7e2919c8ed6a6>:0)
Localization.SetStartupLanguage () (at <f9c1e10e233e4d07bcb7e2919c8ed6a6>:0)
Localization..ctor () (at <f9c1e10e233e4d07bcb7e2919c8ed6a6>:0)
Localization.Initialize () (at <f9c1e10e233e4d07bcb7e2919c8ed6a6>:0)
Localization.get_instance () (at <f9c1e10e233e4d07bcb7e2919c8ed6a6>:0)
ItemManager.LocalizeKey.addForLang (System.String lang, System.String value)
ItemManager.LocalizeKey.English (System.String key)
ItemManagerModTemplate.ItemManagerModTemplatePlugin.Awake ()
```

The previous patch suppressed the `InvalidOperationException` inside `MigratePlatformKeyIfNeeded`,
but that left `prefixedKey` as `null`. `SetStartupLanguage` continued and passed the null value
to `SetLanguageFromLocale`, causing a `NullReferenceException`.

---

## Next Fix Required

Move the patch **higher** in the call chain to `Localization.SetStartupLanguage` — patch that
single method with a Harmony Finalizer that swallows both `InvalidOperationException` and
`NullReferenceException`. This covers the entire broken sequence with one patch.

### Changes needed in `SteamworksInitCompatibility.cs`

Change the reflection target from:
```csharp
platformPrefsType.GetMethod("MigratePlatformKeyIfNeeded", ...)
```
To:
```csharp
// Find Localization type across loaded assemblies
localizationType.GetMethod("SetStartupLanguage",
    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
```

And broaden the Finalizer to catch both exception types:
```csharp
static Exception? SuppressSteamworksException(Exception __exception)
{
    if (__exception is InvalidOperationException || __exception is NullReferenceException)
        return null; // suppress — Localization re-initializes correctly once Steam is ready
    return __exception;
}
```

### Steps to complete

1. Update `AshlandsReborn\Patches\SteamworksInitCompatibility.cs` as above
2. `dotnet build AshlandsReborn\AshlandsReborn.csproj -c Debug`
3. Copy `bin\Debug\AshlandsReborn.dll` to both profiles:
   - `C:\Users\danjo\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\AshlandsReborn\`
   - `C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\AshlandsReborn\`
4. Clear Dev BepInEx cache: `...\Ashlands Reborn\BepInEx\cache\*`
5. Launch from Dev r2modman and check log for errors

---

## Current File State

| File | State |
|------|-------|
| `AshlandsReborn\Patches\SteamworksInitCompatibility.cs` | Exists — patches wrong target, needs update |
| `AshlandsReborn\Plugin.cs` | Calls `SteamworksInitCompatibility.ApplyPatches(Harmony)` before `PatchAll` ✅ |
| Dev profile `AshlandsReborn.dll` | 100,864 bytes (current build, 3/6/2026) |
| Admin profile `AshlandsReborn.dll` | 100,864 bytes (current build, 3/6/2026) |
| Dev `mods.yml` | Correct — 3 mods ✅ |
| Dev `com.ashlandsreborn.weather.cfg` | Matches Admin ✅ |
| Dev `southsil.SouthsilArmor.cfg` | Matches Admin (574 KB) ✅ |
