# Configuration Fix Applied - Dev User

## What Was Fixed

Two critical configuration files have been updated from the Admin user's working setup to the Dev user's profile:

### 1. Ashlands Reborn Configuration
**File:** `BepInEx/config/com.ashlandsreborn.weather.cfg`

**Critical Changes:**
```
Line 21: EnableCharredWarriorSwap = true  ✅ (was: false)
Line 27: CharredWarriorHelmetName = knighthelm  ✅ (was: HelmetDrake)
```

**Impact:** 
- Charred Melee enemies will now render with custom armor
- Krom sword swap will now work
- Visual effects will be enabled

### 2. SouthsilArmor Configuration
**File:** `BepInEx/config/southsil.SouthsilArmor.cfg`

**Changes:**
- Replaced incomplete config (3,610 bytes) with complete config (574,306 bytes)
- Now includes full armor item definitions
- Should resolve the Steamworks initialization error

**Impact:**
- All armor items will now be properly available
- SouthsilArmor mod will load with full functionality

---

## Backups Created

Original Dev configurations have been preserved:

```
C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\
├── com.ashlandsreborn.weather.cfg.backup
└── southsil.SouthsilArmor.cfg.backup
```

---

## What the Dev User Should Do Now

### Step 1: Close Valheim & r2modman
- If Valheim is running, close it
- Close r2modman completely

### Step 2: Clear Cache (Optional but Recommended)
Delete the r2modman cache for this profile to force a fresh load:

```powershell
Remove-Item "C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\cache" -Recurse -Force
```

Or just delete:
- `chainloader_typeloader.dat`
- `harmony_interop_cache.dat`

### Step 3: Reopen r2modman
- Launch r2modman
- Navigate to the "Ashlands Reborn" profile
- Verify that all 3 mods show with valid icons (no more broken icon)

### Step 4: Launch Valheim
- Click "Launch" to start the game
- Wait for the game to fully load

### Step 5: Verify in Game
Once in-game, you should see:
- Custom armor on Charred Melee enemies (knight armor instead of vanilla)
- Krom sword visuals
- Proper mod initialization without Steam errors

---

## Configuration Comparison (After Fix)

| Setting | Admin | Dev (Now Fixed) | Status |
|---------|-------|-----------------|--------|
| **EnableCharredWarriorSwap** | `true` | `true` ✅ | FIXED |
| **CharredWarriorHelmetName** | `knighthelm` | `knighthelm` ✅ | FIXED |
| **SouthsilArmor Config Size** | 574,306 bytes | 574,306 bytes ✅ | FIXED |
| **Mods in Profile** | 3 | 3 ✅ | FIXED |

---

## Expected Result

The Dev user's game should now work **identically to the Admin user's setup**:

✅ All 3 mods loading correctly
✅ Charred Warrior custom visuals enabled
✅ SouthsilArmor full functionality
✅ No Steam initialization errors
✅ Custom armor sets available

---

## Troubleshooting

If issues persist after these changes:

1. **Still seeing errors in BepInEx log:**
   - Run Valheim once to regenerate cache
   - Check the new LogOutput.log for "Chainloader startup complete"
   - Should NOT see "InvalidOperationException: Steamworks is not initialized"

2. **Mods still showing broken icon in r2modman:**
   - Force close r2modman completely
   - Delete: `C:\Users\Dev\AppData\Roaming\r2modman` cache
   - Reopen r2modman

3. **Still having in-game issues:**
   - Check that Valheim version matches (0.221.12)
   - Verify SouthsilArmor mod files exist:
     `BepInEx/plugins/southsil-SouthsilArmor/ItemManagerModTemplate.dll`
   - Check if the mod's config UI is accessible (F1 key)
