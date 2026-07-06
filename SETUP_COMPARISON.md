# Ashlands Reborn - Admin vs Dev User Setup Comparison

## Summary of Findings

The Admin and Dev users have **different mod configurations** in their r2modman profiles, but both setups are actually **working correctly** at the BepInEx level. The visual difference in r2modman is due to a **missing mod in the Dev profile**.

---

## Key Differences

### Admin User Setup ✅
**Mod Manager Profile: "Ashlands Reborn"**
- **3 mods installed and ENABLED:**
  1. `denikson-BepInExPack_Valheim` v5.4.2333
  2. `Azumatt-Official_BepInEx_ConfigurationManager` v18.4.1 ⭐
  3. `southsil-SouthsilArmor` v3.1.8

**Status:** All 3 mods visible in r2modman UI, all with valid icons, no errors

---

### Dev User Setup ⚠️
**Mod Manager Profile: "Ashlands Reborn"**
- **2 mods installed and ENABLED:**
  1. `denikson-BepInExPack_Valheim` v5.4.2333
  2. `southsil-SouthsilArmor` v3.1.8

**Status:** Missing `Official_BepInEx_ConfigurationManager` (the mod with the broken icon in your screenshot)

**Note:** Dev user also has a backup profile "Ashlands Reborn.backup" from an earlier attempt

---

## Root Cause: The Missing Mod

### What's Missing?
The **Official_BepInEx ConfigurationManager** mod is:
- ✅ Present in Admin's `mods.yml`
- ✅ Has valid files in Admin's plugin directory
- ❌ **Missing from Dev's `mods.yml`**
- ❌ Files exist in Dev's directory but are NOT registered in the profile

### File Locations

**Admin's Configuration:**
```
C:\Users\danjo\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\mods.yml
```
- Contains 3 mod entries (including ConfigurationManager)

**Dev's Configuration:**
```
C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\mods.yml
```
- Contains only 2 mod entries (ConfigurationManager missing)

**Dev's Plugin Files:**
```
C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\Azumatt-Official_BepInEx_ConfigurationManager\
```
- Files exist: `ConfigurationManager.dll`, icon, manifest, README
- But the mod is NOT listed in `mods.yml`

---

## The Broken Icon Issue Explained

In your r2modman screenshot, the mod with the "broken icon" showing in Dev's setup is r2modman displaying the Configuration Manager entry **without proper metadata** because it's not properly registered in the `mods.yml` file.

---

## Solution: Add the Missing Mod to Dev's Profile

### Option 1: Manual Fix (Recommended)
Edit `C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\mods.yml`

Add this entry after the BepInExPack line (before SouthsilArmor):

```yaml
- manifestVersion: 1
  name: Azumatt-Official_BepInEx_ConfigurationManager
  authorName: Azumatt
  websiteUrl: >-
    https://thunderstore.io/c/valheim/p/Azumatt/Official_BepInEx_ConfigurationManager/
  displayName: Official_BepInEx_ConfigurationManager
  description: Mod to assist with configuration of BepInEx mods
  gameVersion: '0'
  networkMode: both
  packageType: other
  installMode: managed
  installedAtTime: 1771301810943
  loaders: []
  dependencies:
    - denikson-BepInExPack_Valheim-5.4.2202
  incompatibilities: []
  optionalDependencies: []
  versionNumber:
    major: 18
    minor: 4
    patch: 1
  enabled: true
```

### Option 2: Use r2modman UI
1. In r2modman, go to Dev's "Ashlands Reborn" profile
2. Click "Install from code" or search the mod store for "Official_BepInEx_ConfigurationManager"
3. Install the mod by Azumatt
4. Ensure it's enabled

### Option 3: Copy Admin's Profile
Since both users share the same Valheim game installation:
```powershell
# Backup Dev's current profile
Copy-Item "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\mods.yml" `
          "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\mods.yml.backup"

# Copy Admin's mods.yml
Copy-Item "C:\Users\danjo\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\mods.yml" `
          "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\mods.yml"
```

---

## Important Note: Shared Game Installation

Both users are running Valheim from:
```
C:\Program Files (x86)\Steam\steamapps\common\Valheim
```

This is a **shared installation**, but each user has their own:
- r2modman profile configuration
- BepInEx cache and config files (user-specific in their AppData)

However, the **actual plugin DLLs in the shared game directory** are what all users see.

Current plugins in shared directory:
- ✅ AshlandsReborn.dll
- ❌ ConfigurationManager.dll (NOT in shared directory)
- ❌ SouthsilArmor (NOT in shared directory)

This is expected - the plugins are managed per-user in their AppData, not in the shared Steam folder.

---

## Verification Steps

After applying the fix:

1. **Dev user launches r2modman**
   - Should now show 3 mods (matching Admin)
   - ConfigurationManager icon should display correctly

2. **Dev user launches Valheim**
   - Should load with proper BepInEx initialization
   - Should see the Configuration Manager in-game (F1 key typically)

3. **Check the logs**
   - `C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\LogOutput.log`
   - Should show "3 plugins to load" (matching the Admin log)
