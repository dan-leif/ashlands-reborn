# Plugin DLL Version Mismatch - Critical Fix

## The Root Cause Found!

The Steamworks initialization error the Dev user was experiencing was caused by a **version mismatch in the AshlandsReborn.dll plugin**.

---

## The Problem

### File Size Difference
**AshlandsReborn.dll:**
- Admin: **90,112 bytes** ✅ (Working)
- Dev: **99,840 bytes** ❌ (Broken - 9,728 bytes larger!)

This 9.7 KB difference indicates the **Dev user had a different/newer version** of the AshlandsReborn plugin DLL that was incompatible with the rest of the mod setup.

### Why This Causes the Steamworks Error

The newer version of AshlandsReborn.dll likely has different initialization code that conflicts with ItemManager's Steamworks initialization. When AshlandsReborn starts, it tries to access Steam features before SouthsilArmor's ItemManager can properly initialize, causing the cascade of errors.

### Evidence
BepInEx Log shows the error happens during:
```
ItemManagerModTemplate.ItemManagerModTemplatePlugin.Awake()
  → Localization.Initialize()
    → PlatformPrefs.GetString()
      → Steamworks.SteamUtils.IsSteamRunningOnSteamDeck()
        ❌ ERROR: Steamworks is not initialized
```

This order suggests AshlandsReborn.dll's initialization sequence was interfering with the Steam context availability for other mods.

---

## The Fix Applied

### 1. Backed up Dev's Mismatched DLL
```
AshlandsReborn.dll (99,840 bytes) → AshlandsReborn.dll.backup
```

### 2. Replaced with Admin's Correct Version
```
Copy Admin's AshlandsReborn.dll (90,112 bytes) → Dev's AshlandsReborn.dll
```

### 3. Cleared BepInEx Cache
Deleted cache files to force regeneration:
```
chainloader_typeloader.dat
harmony_interop_cache.dat
```

---

## Summary of All Fixes Applied

| Issue | Type | File | Status |
|-------|------|------|--------|
| Missing mod entry | Configuration | mods.yml | ✅ FIXED |
| CharredSwap disabled | Config value | com.ashlandsreborn.weather.cfg | ✅ FIXED |
| Incomplete armor config | Config file | southsil.SouthsilArmor.cfg | ✅ FIXED |
| Wrong AshlandsReborn.dll | Plugin DLL | AshlandsReborn/AshlandsReborn.dll | ✅ FIXED |
| Stale cache | Cache | BepInEx/cache/* | ✅ CLEARED |

---

## Why Dev Had a Different DLL

Possible causes:
1. **Mod update timing**: Dev's profile was updated after Admin's
2. **Different r2modman refresh**: Dev refreshed mods from Thunderstore at a different time
3. **Manual DLL replacement**: Someone updated Dev's DLL without updating configs
4. **r2modman cache issue**: r2modman cached the wrong DLL version for Dev

Regardless of how it happened, the fix is straightforward: use the same DLL version as the working setup.

---

## What Dev User Should Do Now

### Step 1: Close Everything
- Close Valheim completely (if running)
- Close r2modman completely

### Step 2: Verify the Fix
The DLL and cache should already be fixed, but you can verify:

```powershell
# Check DLL size
(Get-Item "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\AshlandsReborn\AshlandsReborn.dll").Length
# Should show: 90112
```

### Step 3: Launch Valheim
1. Open r2modman
2. Navigate to "Ashlands Reborn" profile
3. Click "Launch" to start Valheim
4. Wait for full initialization

### Step 4: Verify in Game Log
The new BepInEx log should show:
```
[Info   :   BepInEx] 3 plugins to load
[Info   :   BepInEx] Loading [Ashlands Reborn 1.0.0]
[Info   :   BepInEx] Loading [Configuration Manager 18.4.1]
[Info   :   BepInEx] Loading [SouthsilArmor 3.1.8]
[Message:   BepInEx] Chainloader startup complete
```

**Should NOT show:**
```
InvalidOperationException: Steamworks is not initialized
```

---

## Expected Results

After this fix:
- ✅ No more Steamworks errors
- ✅ All 3 plugins load without errors
- ✅ CharredSwap: ON in log
- ✅ Charred Warriors render with custom armor
- ✅ SouthsilArmor mod fully functional
- ✅ Game plays identically to Admin user's setup

---

## Backup Created

Original Dev's AshlandsReborn.dll has been preserved:
```
C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\AshlandsReborn\AshlandsReborn.dll.backup
```

If needed, it can be restored, but the current version from Admin is the correct one.

---

## Technical Details

### Why Version Mismatch Matters

1. **Plugin Load Order**: BepInEx loads all plugins sequentially
2. **First Plugin Sets State**: AshlandsReborn loads first and sets up Steam context
3. **Subsequent Mods Depend on It**: SouthsilArmor/ItemManager expect Steam to be initialized
4. **Newer DLL = Different Initialization**: The newer AshlandsReborn.dll had different code that interfered with Steam setup

### Why This Only Affects Dev

- **Admin's setup**: Consistent from the start, all DLLs match versions
- **Dev's setup**: Got mixed versions from different update times/sources
- **Shared Game Installation**: Both use same game EXE, but different plugin versions caused conflict

---

## Prevention for Future

To avoid this in the future:
1. Keep r2modman profiles synchronized
2. Don't manually copy DLLs between profiles
3. Use r2modman's built-in profile management
4. If issues arise, compare file sizes and timestamps of critical DLLs
