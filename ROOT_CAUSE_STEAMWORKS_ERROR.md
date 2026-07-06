# Root Cause Analysis: Steamworks Initialization Error

## The Real Problem

After reviewing the latest BepInEx log from the Dev user, the true root cause has been identified:

**The Steamworks library is not being initialized before ItemManager tries to use it.**

### Evidence from Log

**Lines 37-54:** The Steamworks error occurs during ItemManager plugin initialization:
```
[Error  : Unity Log] InvalidOperationException: Steamworks is not initialized.
  ItemManagerModTemplate.ItemManagerModTemplatePlugin.Awake ()
    → Localization.Initialize()
      → PlatformPrefs.GetString()
        → Steamworks.SteamUtils.IsSteamRunningOnSteamDeck()
```

**But then lines 57-60 show:** Steam IS actually initialized later:
```
[Info   : Unity Log] 03/05/2026 23:35:53: Using environment steamid 892970
[Info   : Unity Log] 03/05/2026 23:35:53: Using steam APPID:892970
```

**This is the timing issue:** Steam gets initialized AFTER ItemManager tries to use it.

### Item Loading Failures

**Lines 507-515:** Failed to find item prefabs:
- `norahlegs`, `norahhelm`, `norahchest`
- `norahh elmalt`, `ss_ashcape`

**Lines 835-922:** Hundreds of "Missing" item errors because ItemManager couldn't initialize properly.

**Lines 1380-1383:** Ashlands Reborn can't remap armor bones because the armor items don't exist:
```
Could not read bone info from armor prefab 'knightchest'. Armor remap skipped.
Could not read bone info from armor prefab 'knightlegs'. Armor remap skipped.
```

**Lines 1387-1451:** All SouthsilArmor items missing:
- bearchest, bearlegs, bearhelm
- skogchest, skoglegs, skoghelm
- ss_korokchest, ss_koroklegs, ss_korokhelm
- valkchest, valklegs, valkhelm
- And dozens more...

### Why This Happens

The plugin loading order is:
1. AshlandsReborn loads
2. Configuration Manager loads
3. **SouthsilArmor (ItemManager) tries to initialize** ← ERROR occurs here
4. Later, Valheim's main game code initializes Steam

**The problem:** ItemManager tries to use Steamworks before Valheim has properly initialized the Steamworks context.

### Why Admin Works But Dev Doesn't

**Admin user's log does NOT show the Steamworks error** in the same place. This suggests:
1. **Different plugin load timing** - Admin's plugins might load in a different order
2. **Different Steam initialization** - Admin's Steam context might be ready sooner
3. **Permission differences** - Dev user might not have proper Steam API access
4. **Account initialization** - Dev user's Steam profile might not be fully loaded

### What Copying the DLL Didn't Fix

We copied Admin's working AshlandsReborn.dll (90 KB) to Dev, but the Steamworks error persists. This proves the issue is NOT:
- ✗ The AshlandsReborn.dll version
- ✗ The SouthsilArmor configuration files
- ✗ Cache issues

The issue IS likely:
- ✓ Steam context initialization timing specific to Dev user
- ✓ How the Dev user account initializes with Steam API
- ✓ Potential permission or platform-specific Steam initialization

---

## The Real Fix Required

This is **NOT a mod configuration issue**. This is a **Steam API/user profile initialization issue**.

### Possible Solutions:

1. **Verify Dev User's Steam Account:**
   - Make sure Dev user is logged in to Steam
   - Make sure Dev user account owns/has access to Valheim
   - Make sure Dev user can launch Valheim independently (not via mod manager)

2. **Check for Hardware/Platform Differences:**
   - Is Dev running on a different machine?
   - Different Windows version?
   - Different Steam client version?

3. **Force Steam Initialization Before Mods:**
   - This would require modifying how BepInEx loads plugins
   - Or waiting for SouthsilArmor mod author to add better Steamworks initialization handling

4. **Temporary Workaround:**
   - Launch Valheim vanilla first (without mods)
   - This fully initializes Steam context
   - Then use mod manager to launch with mods
   - Steam context might persist for subsequent launches

---

## What This Means

The Steamworks initialization error is a **timing bug** where ItemManager tries to use Steam APIs that aren't ready yet on the Dev user's system specifically.

**This is not solvable by:**
- Copying files from Admin
- Updating configurations
- Clearing caches
- Reinstalling mods

**This would require:**
- Debugging the Steam initialization sequence
- Potentially modifying BepInEx load order
- Or having the SouthsilArmor mod author fix ItemManager's initialization sequence
- Or investigating why Dev user's Steam context initializes later than Admin's

---

## Recommendation

The Dev user should:

1. **Launch Valheim vanilla first** (without mods) to pre-initialize Steam
2. **Then use r2modman to launch** with mods active
3. This gives Steam time to fully initialize before ItemManager tries to use it

Alternatively:
- Contact the SouthsilArmor mod author about the Steamworks initialization timing
- Or investigate if there's a difference in how Dev user's Steam account is configured vs Admin

---

## Summary

- ✓ Configuration files fixed
- ✓ Plugin DLLs matched  
- ✗ Steamworks error STILL OCCURS

**Conclusion:** The issue is not with the mods themselves, but with how the Dev user's system initializes Steam. This is a deeper platform/account configuration issue that requires investigating the Dev user's Steam setup or attempting the vanilla-first launch workaround.
