# Plan: Chest Armor Swap — Blender Retargeting

## Context

Seven programmatic approaches (v0–v7) all failed to correctly retarget `Padded_Cuirrass` to the Charred warrior. Root cause:

- The armor is a **full-body rig with 53 bones** (all spine, arm, hand, finger, leg, foot, toe, neck, head, jaw bones)
- `BuildCharredBindPoseMap` reads bind poses **directly from the Charred body mesh's baked `.sharedMesh.bindposes`** — these are authored for the Charred body mesh geometry, not for the player armor geometry
- The Charred arm/shoulder bones have **~177° different rest-pose orientations** from the Player skeleton
- There is no single rotation correction, scale correction, or formula that can reconcile this — each arm bone requires a different per-bone correction based on its geometry in the Charred mesh vs. the player armor mesh
- The correct fix requires **recomputing all 53 bind pose matrices** such that each maps player armor vertices to the corresponding Charred bone's local space at the Charred skeleton's rest pose — this is exactly what Blender's retargeting tools do

**Why no Unity Editor is needed:** We do not need an asset bundle. Bind pose matrices are just `Matrix4x4` data. We extract the correct bind poses from Blender after retargeting, then **hardcode them as a `static readonly` array** in the plugin. The bone weights (vertex→bone assignments) in the original mesh are preserved unchanged. The SMR's `localRot=(270,0,0)` does not affect skinned vertex rendering (Unity applies `bone.L2W * bindpose * vertex` directly to world space, bypassing the SMR transform).

---

## Three-Phase Plan

### Phase 1 — Write Extraction Scripts (I implement)

Write two new Python scripts in the repo root:

**`extract_chest_armor_data.py`** — extracts from SouthsilArmor:
- Bone names array (ordered, matches bind pose index)
- All 53 bind pose matrices (as flat float arrays)
- Bone weight data (boneIndex + boneWeight per vertex, for reference/verification)
- Output: `extracted_assets/padded_cuirrass_data.json`

**`extract_charred_skeleton.py`** — extracts from Valheim `c4210710` bundle:
- Charred_Melee bone hierarchy (parent→child names)
- Rest-pose local transforms per bone (position, rotation as quaternion, scale)
- Output: `extracted_assets/charred_skeleton.json`

Both follow the existing UnityPy pattern established in the other 15 scripts.

---

### Phase 2 — Blender Retargeting via BlenderMCP (Claude-automated, use **Opus 4.6**)

> Switch model before starting this phase: `/model claude-opus-4-6`
> The bone transform math and coordinate space reasoning in this phase benefit significantly from Opus.
> Switch back to Sonnet for Phase 3: `/model claude-sonnet-4-6`


**BlenderMCP** lets Claude control Blender directly through an MCP server, automating the entire Phase 2 without any manual Blender work.

#### Setup (one-time, if not already done)

1. Install the Blender addon: in Blender → Edit → Preferences → Add-ons → Install → select the BlenderMCP `.py` addon file. Enable it.
2. In Blender, open the BlenderMCP panel (N-panel → BlenderMCP tab) and click **Start MCP Server** (runs on port 9876 by default).
3. Add to Claude Code's MCP config (`~/.claude/claude_desktop_config.json` or via `claude mcp add`):
   ```json
   {
     "mcpServers": {
       "blender": {
         "command": "uvx",
         "args": ["blender-mcp"]
       }
     }
   }
   ```
4. Restart Claude Code. Confirm `blender` appears in the MCP server list.

#### Automated steps Claude will execute via BlenderMCP

Once connected, Claude will run these steps through the MCP:

1. **Clear scene** — delete default cube/light/camera
2. **Reconstruct Player armature** from `padded_cuirrass_data.json` bind poses:
   - Invert each BP matrix → bone rest-pose world transform
   - Create Armature `Player_Armature` with 53 named bones at those transforms
3. **Create armor mesh** from extracted vertex/triangle data, assign bone weights from the JSON, parent to `Player_Armature` with the original bind poses → armor appears in correct T-pose shape
4. **Reconstruct Charred armature** from `charred_skeleton.json` rest-pose transforms:
   - Create Armature `Charred_Armature` with matching bone names and local transforms
5. **Retarget**: re-parent the armor mesh to `Charred_Armature`, apply the Data Transfer modifier to recompute weights relative to the Charred skeleton's geometry, then apply the armature modifier to bake new bind poses
6. **Extract and save bind poses** — run via BlenderMCP Python execution:
   ```python
   import bpy, json, pathlib
   obj = bpy.data.objects["Padded_Cuirrass"]
   arm = obj.find_armature()
   mwi = obj.matrix_world.inverted()
   result = {}
   for bone in arm.data.bones:
       bm = arm.matrix_world @ bone.matrix_local
       bp = (mwi @ bm).inverted()
       result[bone.name] = [list(bp[r]) for r in range(4)]
   out = pathlib.Path(r"C:\DEV\ashlands-reborn\extracted_assets\padded_cuirrass_retargeted_bindposes.json")
   out.write_text(json.dumps(result, indent=2))
   print(f"Wrote {len(result)} bind poses to {out}")
   ```

#### Fallback: manual Blender steps (if BlenderMCP unavailable)

Run the same logic above manually in Blender's Python console / Scripting workspace using the JSON files as input. Save output to `extracted_assets/padded_cuirrass_retargeted_bindposes.json`.

---

### Phase 3 — Plugin Integration (I implement, after Phase 2)

Once the JSON is ready, I will:

1. **Parse the JSON into C# constants** — generate a `private static readonly Dictionary<string, Matrix4x4> s_chestRetargetedBPs` populated from the 53 exported matrices

2. **Replace the `isChest` branch** in `RemapArmorBones` with:
```csharp
if (isChest)
{
    // Use Blender-retargeted bind poses — precomputed to correctly map
    // Padded_Cuirrass vertices to the Charred_Melee skeleton's rest pose.
    for (int i = 0; i < prefabBoneNames.Length && i < originalBPs.Length; i++)
    {
        var boneName = prefabBoneNames[i];
        newBPs[i] = s_chestRetargetedBPs.TryGetValue(boneName, out var bp)
            ? bp
            : originalBPs[i];
        // newBones[i] already points to the correct Charred bone (set above)
    }
    for (int i = prefabBoneNames.Length; i < originalBPs.Length; i++)
        newBPs[i] = originalBPs[i];
}
```
   Note: no `scaleMat` needed — the Blender bind poses are computed at the Charred skeleton's actual scale

3. `dotnet build` and verify in-game

---

## Files to Create / Modify

| File | Action |
|------|--------|
| `extract_chest_armor_data.py` | Create (new extraction script) |
| `extract_charred_skeleton.py` | Create (new extraction script) |
| `extracted_assets/padded_cuirrass_retargeted_bindposes.json` | Created by user after Blender work |
| `AshlandsReborn/Patches/CharredWarriorPatches.cs` | Modify isChest branch + add static BP dict |

## Current State of `isChest` Branch

✅ **COMPLETED** — Phase 3 implemented. The `isChest` branch now uses dictionary-based bind pose lookup:

```csharp
private static readonly Dictionary<string, Matrix4x4> s_chestRetargetedBPs = new()
{
    { "Hips", M4(...) },
    { "Spine", M4(...) },
    // ... 51 more bone entries
};

if (isChest)
{
    for (int i = 0; i < prefabBoneNames.Length && i < originalBPs.Length; i++)
    {
        var boneName = prefabBoneNames[i];
        newBPs[i] = s_chestRetargetedBPs.TryGetValue(boneName, out var bp)
            ? bp
            : originalBPs[i];
    }
    for (int i = prefabBoneNames.Length; i < originalBPs.Length; i++)
        newBPs[i] = originalBPs[i];
}
```

**Current result (v12 hybrid):** Good torso and arms positioned correctly. Arms are somewhat skinny and twisted, but at proper spatial location and animate correctly. Torso is coherent through breathing and combat animations.

**Hybrid approach:** Combines best torso values from v11 (first Blender retargeting run) with arm bone values from v10 (per-bone positional hybrid from second Blender run).

## Session Status

✅ Phase 1 — Extraction scripts completed
✅ Phase 2 — Blender retargeting completed (two iterations)
✅ Phase 3 — Plugin integration completed (v12 hybrid deployed)

## Future Improvements

- Arm scale adjustment: Consider applying targeted scale corrections to arm/hand bones to reduce "skinny" appearance while preserving position/orientation
- Arm twist mitigation: Explore per-bone rotation blending to reduce twisted appearance in fingers/wrists
- Validation: Full in-game animation testing across all combat moves

---

## Verification

1. After Phase 1: confirm `padded_cuirrass_data.json` and `charred_skeleton.json` exist and contain the expected bone names and matrix data
2. After Phase 2: open `padded_cuirrass_retargeted_bindposes.json` and verify it contains 53 entries with valid 4×4 matrix data
3. After Phase 3: `dotnet build` → launch Valheim → spawn `charred_melee` → press F9 → chest armor appears at torso height, arm guards on arms, gauntlets on hands, all stay coherent through combat animation
