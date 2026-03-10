# Chest Armor Retarget — Step-by-Step Walkthrough

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

### ☐ Step 7 — Add mesh geometry to the dump (NEXT: requires one more game run)
The proper Blender retargeting requires mesh vertex data. I'll add temporary mesh extraction to the dumper:
- Re-add `DumpArmorMeshData()` to CharredWarriorPatches.cs
- Extracts vertex positions and triangle indices from the knightchest SMR
- Writes `extracted_assets/knightchest_mesh_data.json`
- **Then**: You run the game once more (same as before — load save, spawn Charred, press F9, close)
- **Checkpoint**: `knightchest_mesh_data.json` exists ✓

### ☐ Step 8 — Remove mesh dumper (cleanup)
Delete the temporary mesh extraction code after Step 7 completes.

### ☐ Step 9 — Run Blender retargeting via Blender script
Execute Python script in Blender (via Script Editor or automated MCP):
1. Load knightchest vertex/triangle data
2. Load knightchest bind poses and bone weights
3. Reconstruct Player armature with correct bone positions (from bind pose inverses)
4. Create weighted mesh parented to Player armature
5. Reconstruct Charred armature with correct hierarchy
6. Apply Blender's retargeting (Data Transfer modifier + weight recalc)
7. Bake new bind poses for Charred skeleton
8. Export → `extracted_assets/knightchest_retargeted_bindposes.json`
- **Checkpoint**: JSON contains ~56 bone entries with valid 4×4 matrix data ✓

### ☐ Step 8 — I integrate the retargeted bind poses into the plugin
Modify `AshlandsReborn/Patches/CharredWarriorPatches.cs`:
1. Add `private static readonly Dictionary<string, Matrix4x4> s_chestRetargetedBPs` with the 56 matrices
2. Replace the `isChest` v7 branch in `RemapArmorBones` with a lookup into that dictionary
- **Checkpoint**: `dotnet build` succeeds ✓

### ☐ Step 9 — You test in-game
1. Launch via r2modman
2. Spawn `charred_melee`
3. Press F9 (CharredWarriorRefreshKey)
4. Verify: chest armor sits at torso height, arm guards on arms, stays coherent through combat animation
- **Checkpoint**: Armor renders correctly in all animations ✓

---

## Files Modified / Generated

| File | Who | Step | Action |
|------|-----|------|--------|
| `AshlandsReborn/Patches/CharredWarriorPatches.cs` | Claude | 2, 4, 8 | Add/remove dumper; replace isChest branch |
| `extracted_assets/knightchest_data.json` | Plugin (runtime) | 3 | Generated by in-game dump |
| `extracted_assets/charred_skeleton.json` | Plugin (runtime) | 3 | Generated by in-game dump |
| `extracted_assets/knightchest_retargeted_bindposes.json` | Claude via BlenderMCP | 7 | Generated by Blender |
