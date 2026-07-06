# Critical Configuration Differences: Admin vs Dev

## Summary
The Admin and Dev users have **significantly different mod configurations** that affect **gameplay features**. The Dev user's setup is broken not because of missing mods, but because of **incorrect settings in the Ashlands Reborn configuration file**.

---

## 1. Main Issue: Ashlands Reborn Settings

### File Affected
```
BepInEx/config/com.ashlandsreborn.weather.cfg
```

### Critical Differences in Charred Warrior Configuration

| Setting | Admin | Dev | Impact |
|---------|-------|-----|--------|
| **EnableCharredWarriorSwap** | `true` ✅ | `false` ❌ | BREAKS charred warrior visuals |
| **CharredWarriorHelmetName** | `knighthelm` | `HelmetDrake` | Admin uses custom SouthsilArmor; Dev uses vanilla |
| **CharredWarriorChestName** | `knightchest` | `knightchest` | Same (custom armor) |
| **CharredWarriorLegsName** | `knightlegs` | `knightlegs` | Same (custom armor) |
| **CharredWarriorShoulderName** | ` ` (empty) | `ss_storrcape` | Admin disabled; Dev trying to use custom |

### What This Means
The **Dev user has Charred Warrior visual swaps disabled** (`EnableCharredWarriorSwap = false`), which means:
- Charred Melee enemies won't display custom armor
- Charred Melee enemies won't have custom sword swaps
- Visual effects are disabled (no Krom sword, no knight armor)
- **This is likely why the Dev user's game "doesn't work"** - visual features are broken

---

## 2. SouthsilArmor Configuration Differences

### File Affected
```
BepInEx/config/southsil.SouthsilArmor.cfg
```

**Size Difference:**
- Admin: **574,306 bytes** - Full configuration with all armor items
- Dev: **3,610 bytes** - Minimal configuration

**Content Difference:**
- Admin: Contains configurations for **ALL** armor items (Abomination Helm, etc.)
- Dev: Contains configurations for only **a few items** (Neck-King Crown, etc.)

This suggests:
1. Dev's config was generated more recently with a trimmed-down item list
2. Admin's config was generated earlier with a fuller item set
3. The armor mod may not be functioning at full capacity for Dev

---

## 3. Log File Errors

### Dev User's BepInEx Log (Lines 37-54)

**CRITICAL ERROR:**
```
[Error  : Unity Log] InvalidOperationException: Steamworks is not initialized.
Stack trace:
  Steamworks.InteropHelp.TestIfAvailableClient ()
  Steamworks.SteamUtils.IsSteamRunningOnSteamDeck ()
  PlatformPrefs.MigratePlatformKeyIfNeeded ()
  ItemManagerModTemplate.ItemManagerModTemplatePlugin.Awake ()
```

This error **does NOT appear in the Admin's log**. It indicates:
- SouthsilArmor's ItemManager is having trouble initializing Steam context
- This could cause items to not load properly
- This could be why armor customization is broken for Dev

### Admin's Log (Line 19)
```
[Info   :Ashlands Reborn] Ashlands Reborn v1.0.0 loaded. Mod: ON, Weather: ON, Terrain: ON, Trees: ON, Valkyrie: Enabled, CharredSwap: ON
```

### Dev's Log (Line 18)
```
[Info   :Ashlands Reborn] Ashlands Reborn v1.0.0 loaded. Mod: ON, Weather: ON, Terrain: ON, Trees: ON, Valkyrie: Enabled, CharredSwap: OFF
```

**The logs confirm**: Charred swap is OFF for Dev.

---

## Why Dev's Game is Broken

The Dev user's setup is broken due to **THREE combined issues**:

1. **Charred Warrior Swap Disabled** - The primary Ashlands Reborn visual feature is turned off
2. **Incomplete SouthsilArmor Config** - Armor mod not fully configured (174KB difference!)
3. **Steamworks Initialization Error** - SouthsilArmor having trouble with Steam context, preventing proper armor loading

---

## Solution

Copy the Admin's configuration files to the Dev user:

```powershell
# Backup Dev's current configs
Copy-Item "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\com.ashlandsreborn.weather.cfg" `
          "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\com.ashlandsreborn.weather.cfg.backup"

Copy-Item "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\southsil.SouthsilArmor.cfg" `
          "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\southsil.SouthsilArmor.cfg.backup"

# Copy Admin's working configs to Dev
Copy-Item "C:\Users\danjo\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\com.ashlandsreborn.weather.cfg" `
          "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\"

Copy-Item "C:\Users\danjo\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\southsil.SouthsilArmor.cfg" `
          "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\"
```

Then:
1. Close r2modman
2. Close Valheim if running
3. Delete the r2modman cache for this profile
4. Reopen r2modman and launch Valheim

---

## Key Takeaway

**The mods were installed correctly, but the configuration was wrong.** This is a common issue with shared game installations where different users run different mod configurations. The Admin user's settings were more complete and had the features enabled, while the Dev user's settings had critical features disabled or incompletely configured.
