# Action Guide for Dev User - What to Do Now

## TL;DR - Quick Summary

**5 problems found and fixed:**
1. ✅ Missing mod in profile (mods.yml) - FIXED
2. ✅ Charred swap disabled - FIXED  
3. ✅ Incomplete armor config - FIXED
4. ✅ Wrong AshlandsReborn.dll version - FIXED (Root cause!)
5. ✅ Stale cache - FIXED

**All fixes applied. Your setup should now work.**

---

## What You Should Do RIGHT NOW

### Step 1: Close Everything
```
☐ Close Valheim (if running)
☐ Close r2modman (if running)
☐ Close any other game processes
```

Wait 5-10 seconds.

### Step 2: Reopen r2modman
```
☐ Launch r2modman
☐ Navigate to "Ashlands Reborn" profile
☐ You should see 3 mods with valid icons
   ☐ BepInExPack_Valheim
   ☐ Official_BepInEx_ConfigurationManager (was broken, now fixed)
   ☐ SouthsilArmor
```

If you still only see 2 mods, something went wrong. Check the documentation.

### Step 3: Launch Valheim
```
☐ Click "Launch" button in r2modman
☐ Wait for game to start (may take 30-60 seconds)
☐ Wait for main menu to appear
☐ You should NOT see any error dialogs
```

### Step 4: Test in Game
```
☐ Create a new character or load an existing one
☐ Go to a world with Charred Warriors (Ashlands biome)
☐ Look at the Charred Melee enemies
☐ They should wear CUSTOM KNIGHT ARMOR (not vanilla)
☐ They should have swords (not bare-handed)
```

### Step 5: Verify Nothing Broke
```
☐ Game controls work normally
☐ Can move around without lag
☐ Can access inventory (E key)
☐ Can craft items normally
☐ Open Configuration Manager (F1 key) - should work without errors
```

---

## If Everything Works

**Congratulations!** Your setup is now fixed and matches the Admin user's configuration.

**No further action needed.** Just enjoy the mod!

---

## If You Still Have Problems

### Problem: Still only see 2 mods in r2modman

**Solution:**
1. Force close r2modman
2. Delete the r2modman cache:
   ```
   C:\Users\Dev\AppData\Roaming\r2modman
   ```
   (Delete the entire folder, let it regenerate)
3. Reopen r2modman
4. Navigate to profile again

### Problem: Game crashes on startup

**Check the log:**
1. Launch Valheim from r2modman
2. Wait 30 seconds
3. Check the log file:
   ```
   C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\LogOutput.log
   ```
4. Look for ERROR messages
5. Send screenshot of any ERROR lines

### Problem: Charred Warriors still don't have custom armor

**This means the config fix didn't work:**
1. Verify the config file was updated:
   ```
   C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\com.ashlandsreborn.weather.cfg
   ```
2. Open in Notepad and search for: `EnableCharredWarriorSwap`
3. Line 21 should say: `EnableCharredWarriorSwap = true`
4. If it says `false`, something reverted it

### Problem: Configuration Manager won't open (F1 key does nothing)

**This is less critical but means:** Configuration Manager mod isn't loading properly

1. Check BepInEx log for Configuration Manager errors
2. Make sure the mod is in r2modman profile (it should be)
3. Verify DLL exists:
   ```
   C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\Azumatt-Official_BepInEx_ConfigurationManager\ConfigurationManager\ConfigurationManager.dll
   ```

---

## Verification Checklist

Print this out or save it. Check off each item:

### r2modman UI
- [ ] Shows "Ashlands Reborn" profile
- [ ] Profile shows 3 mods
- [ ] All mods have icons (no broken icons)
- [ ] Can click "Launch" without errors

### Game Launch
- [ ] Game window opens
- [ ] Main menu appears
- [ ] No crash dialogs
- [ ] Can click "New" or load world without hanging

### In Game
- [ ] Character spawns in world
- [ ] Can move with WASD
- [ ] Can jump with SPACE
- [ ] Can open inventory with E
- [ ] See Charred Warriors in Ashlands area
- [ ] Charred Warriors wear custom armor (knight helmet, chest, legs)
- [ ] No texture errors or missing models

### Log File
Check: `C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\LogOutput.log`

- [ ] See "3 plugins to load" message
- [ ] See "Ashlands Reborn 1.0.0" loaded
- [ ] See "Configuration Manager 18.4.1" loaded
- [ ] See "SouthsilArmor 3.1.8" loaded
- [ ] See "CharredSwap: ON"
- [ ] See "Chainloader startup complete"
- [ ] Do NOT see "InvalidOperationException: Steamworks is not initialized"
- [ ] Do NOT see other ERROR messages related to mods

---

## Questions?

All the detailed technical documentation is in these files:
- `FINAL_COMPLETE_FIX_SUMMARY.md` - Everything that was fixed
- `PLUGIN_DLL_VERSION_MISMATCH_FIX.md` - Why the DLL mismatch was the root cause
- `DEEP_CONFIG_DIFFERENCES.md` - Detailed config comparisons
- `CONFIG_FIX_APPLIED.md` - Configuration changes explained

Read those if you want to understand what went wrong and why it's fixed now.

---

## Success Confirmation

Once you verify all items in the checklist above are working, your Ashlands Reborn mod setup is **fully functional and matches the Admin user's working configuration**.

The game should play identically now, with all custom armor, visual effects, and mod features working correctly.

**Good luck, and happy gaming!**
