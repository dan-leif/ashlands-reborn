# Chest Armor Retarget — Step-by-Step Walkthrough

## Model Recommendation

| Work type | Model |
|-----------|-------|
| C# plugin code (dumper, integration, builds) | Sonnet 4.6 (default) |
| Blender Python retargeting script, bind pose math, bone transform reasoning | **Opus 4.6** — switch with `/model claude-opus-4-6` before Step 9 |

---

## Quick Reference

| Step | Who | What |
|------|-----|------|
| 1 | **You** | Install Blender |
| 2 | **Me** | Add runtime data dumper to plugin |
| 3 | **You** | Run the game once to generate JSON files |
| 4 | **Me** | Remove the runtime dumper |
| 5 | **You** | Install BlenderMCP addon in Blender (I walk you through it) |
| 6 | **Me** | Configure Claude Code MCP for Blender |
| 7 | **Me** | Run fully automated Blender retargeting via MCP |
| 8 | **Me** | Integrate retargeted bind poses into plugin |
| 9 | **You** | Test in-game |

---

## Background / Why This Approach

`knightchest` is the **item name** (ObjectDB lookup). `Padded_Cuirrass` is the **SMR name** — the SkinnedMeshRenderer component *inside* the knightchest prefab. The plan refers to "Padded_Cuirrass" as the mesh/SMR name, not a different armor piece. No confusion — you're already using the right armor.

`knightchest` is a mod asset (SouthsilArmor), so it's not in Valheim's own bundle files. Instead of hunting for the mod's bundle file (which might be embedded in the DLL), we dump the bind pose data at runtime directly from the game — the plugin already reads it, we just need to write it to disk.

Only bones with vertex weights matter for a chest piece (~13 torso/arm/hand bones out of 56 total). Blender outputs all 56; extra ones are harmless.

---

## Steps

### ☑ Step 1 — You install Blender
- Download from **https://www.blender.org/download/** (latest stable)
- Run the Windows installer with default settings
- Launch Blender once to confirm it opens, then close it
- **Checkpoint**: Blender opens ✓

### ☑ Step 2 — I add a runtime data dumper to the plugin
I add a temporary `DumpChestRetargetData()` method to `CharredWarriorPatches.cs` that fires once when the armor loads. It writes:
- `extracted_assets/knightchest_data.json` — bone names + all 4×4 bind pose matrices from the knightchest SMR
- `extracted_assets/charred_skeleton.json` — Charred_Melee bone hierarchy with local pos/rot/scale

### ☑ Step 3 — You run the game once to generate the JSON files
1. I run `dotnet build`
2. You launch Valheim via r2modman → load a save → spawn a Charred Melee → press F9
3. Close the game
- **Checkpoint**: Both JSON files exist in `extracted_assets/` ✓

### ☑ Step 4 — I remove the runtime dumper
Temporary diagnostic code cleaned out of `CharredWarriorPatches.cs`.

### ☑ Step 5 — You install the BlenderMCP addon in Blender
I'll walk you through each sub-step when we get here:
1. Download the BlenderMCP `.py` addon (I'll give you the file/link)
2. Blender → Edit → Preferences → Add-ons → Install → select file → enable it
3. In viewport, press **N** → BlenderMCP tab → click **Start MCP Server**
- **Checkpoint**: Panel shows "Server running on port 9876" ✓

### ☑ Step 6 — I configure the MCP server in Claude Code
I update the Claude Code MCP config and tell you to restart it.
- **Checkpoint**: `blender` appears in Claude Code's MCP server list ✓

### ☑ Step 7 — Add mesh geometry to the dump & run Blender retargeting
- ✅ Added `DumpArmorMeshData()` to extract vertex positions and triangle indices
- ✅ Generated `extracted_assets/knightchest_mesh_data.json`
- ✅ Ran Blender retargeting script via manual Python execution
- ✅ Two retargeting iterations completed:
  - **v11** (first run): Perfect torso with correct shape and animation
  - **v10** (second run with per-bone hybrid): Better arm positioning (but arms too skinny)
- ✅ Exported bind poses to `extracted_assets/knightchest_retargeted_bindposes.json`
- **Checkpoint**: Both JSON files contain valid 4×4 matrix data ✓

### ☑ Step 8 — Remove mesh dumper (cleanup)
- ✅ Deleted temporary mesh extraction code after retargeting completed

### ☑ Step 9 — Integrate retargeted bind poses into the plugin
Modified `AshlandsReborn/Patches/CharredWarriorPatches.cs`:
- ✅ Added `private static readonly Dictionary<string, Matrix4x4> s_chestRetargetedBPs` with 51 bone entries
- ✅ Replaced the `isChest` v7 branch in `RemapArmorBones` with dictionary lookup
- ✅ Implemented v12 hybrid approach: v11 torso/shoulder/leg values + v10 arm/hand/finger values
- ✅ `dotnet build` succeeds, no errors ✓

### ☑ Step 10 — In-game validation
- ✅ Verified in-game: chest armor sits at correct torso height
- ✅ Arm guards positioned on arms (correct spatial location)
- ✅ Armor animates correctly through breathing and combat
- ✅ Torso is coherent and well-shaped
- ⚠️ **Known limitation**: Arms are somewhat skinny and have minor twist artifacts in fingers/wrists
- **Checkpoint**: Armor renders correctly in all animations with good torso and correct arm positioning ✓

---

## Files Modified / Generated

| File | Status | Step | Notes |
|------|--------|------|-------|
| `AshlandsReborn/Patches/CharredWarriorPatches.cs` | ✅ Final | 2, 4, 8, 9 | Added/removed dumper code; integrated v12 hybrid dictionary |
| `extracted_assets/knightchest_data.json` | ✅ Generated | 3 | Runtime dump of armor bind poses and bone weights |
| `extracted_assets/charred_skeleton.json` | ✅ Generated | 3 | Runtime dump of Charred skeleton hierarchy |
| `extracted_assets/knightchest_mesh_data.json` | ✅ Generated | 7 | Runtime dump of armor mesh vertices and triangles (for retargeting input) |
| `extracted_assets/knightchest_retargeted_bindposes.json` | ✅ Generated | 9 | Blender-computed retargeted bind poses (v11 torso + v10 arms hybrid) |

## Final Implementation Summary

**v12 Hybrid Approach:**
- Torso, shoulders, legs: Bind poses from v11 (first Blender retargeting run)
- Arms, hands, fingers: Bind poses from v10 (per-bone positional hybrid from second run)
- Result: Good torso with correct shape and animation, arms positioned correctly at spatial location (though somewhat skinny)

**Build Status:** ✅ `dotnet build` succeeds, deployed to r2modman
**In-Game Status:** ✅ Armor renders and animates correctly on charred_melee

**Known Limitations:**
- Arm geometry is narrower than ideal (skinny appearance)
- Minor twist artifacts in fingers and wrists
- Potential for future improvement via targeted scale adjustments
