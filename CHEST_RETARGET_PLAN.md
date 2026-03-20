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
✅ Phase 4 — Blender preview scene created for offline visual inspection
✅ Phase 5, Step 1 — Runtime matrix dump instrumented and captured (`chest_runtime_matrices.json`)
✅ Phase 5, Step 2 — **Validated simulator built using BakeMesh ground truth** — user confirmed Blender output exactly matches in-game appearance
✅ Phase 5, Step 2b — **Full character visualization scene completed** — Charred (body+sinew+skull+eyes) and Player (1010v naked body) side by side, sinew/eyes bind-pose remapped, skeleton rotations fixed
⏳ Phase 5, Step 3 — One-bone experiments (NEXT)

## Blender Preview Scene

**File:** `extracted_assets/knightchest_charred_preview.blend`

A persistent Blender scene built via BlenderMCP that allows visual inspection and posing of the chest armor without launching the game:

- **`KnightChest`** mesh — 5,865 vertices / 5,246 faces, bronze/metallic material, loaded from `knightchest_mesh_data.json` in player skeleton T-pose
- **`CharredArmature`** — 54-bone armature with bones positioned at their vertex-weight centroids (derived directly from bone weight data), ensuring each bone sits at the correct influence location within the mesh
- **Vertex weights** — all 53 bone vertex groups assigned from game data with up to 4 influences per vertex
- **Armature modifier** active — enter Pose Mode (`Ctrl+Tab`), select a bone, press `R` to rotate; the mesh deforms in real time

**How bone positions were derived:** Rather than fighting the 5.66× Unity world-scale factor embedded in the bind pose matrices, bone positions were computed as the weighted centroid of all vertices each bone influences. This correctly places each bone inside the region it controls.

**Coordinate system:** Mesh vertices loaded with X negated (`-v[0], v[1], v[2]`) to convert Unity left-handed Z-up to Blender right-handed Z-up.

## v13 Attempts (Failed) — Lessons Learned

Two attempts were made to improve the v12 skinny arms by recomputing bind poses from `charred_skeleton.json` world transforms:

**v13 (full recompute):** Computed `BP[i] = inverse(bone_world[i]) @ meshRoot` for all bones. Failed catastrophically in-game — long shards stretching into the sky. Root cause: bone world matrices built from `charred_skeleton.json` did NOT include the ~5.66× parent game object scale. This caused translation values in the bind poses to be ~100× too large.

**v13_fixed (rotation-only hybrid):** Used v13's 3×3 rotation-scale submatrix with original player translations (near-zero). Looked improved in the Blender simulator but failed in-game with stretched shoulders and distorted arms/fingers. Root cause: the Blender simulator was never validated against known in-game results, so "looks better in Blender" was meaningless.

**Key lesson (from external review):** The simulator must reproduce the known v12 in-game appearance BEFORE it can be trusted to evaluate new candidates. Both v13 attempts skipped this validation step.

## Phase 5 — Runtime Matrix Dump & Validated Simulator

> **Model recommendation:** Use **Opus** (`/model opus`) for this phase — the matrix math and validation logic require careful reasoning.

### Step 1 — ✅ Instrument the game to dump runtime matrices

**Completed.** Added `DumpRuntimeMatrices()` to `CharredWarriorPatches.cs`. Fires automatically on first chest armor apply, writes `chest_runtime_matrices.json` to `BepInEx/plugins/`. Copy saved at `extracted_assets/chest_runtime_matrices.json`.

Add a one-time dump (triggered by F9 or a debug key) that logs the following for the chest SkinnedMeshRenderer after armor is attached:

- `smr.bones[]` array: exact count, exact order, exact names
- For each bone: `bone.localToWorldMatrix` (full Matrix4x4, not pos/rot decomposition)
- `smr.transform.localToWorldMatrix` (the renderer's own transform)
- `smr.rootBone.name` and `smr.rootBone.localToWorldMatrix`
- `mesh.bindposes[]` array: exact count, exact order
- Verify: `bones.Length == bindposes.Length` and bone names match bind pose order

Output: JSON file to `BepInEx/plugins/` or game log.

### Step 2 — ✅ Build validated simulator

**Completed.** Validated ground truth captured via `SkinnedMeshRenderer.BakeMesh()`.

**Key discoveries during validation:**
- `knightchest_mesh_data.json` (5865 verts) is NOT the armor mesh — it's the player **body** mesh from the SouthsilArmor bundle
- The real armor prefab mesh (`Padded_Cuirrass`) has only **1152 vertices** (thin arm guards + chest plate)
- Vanilla's `AttachArmor` combines body + armor into `Body(Clone)` (5865 verts, 10 submeshes) — this is what Unity renders
- Python skinning math was verified to match Unity's C# output within float32 precision (max error 0.0006)
- Manual skinning with extracted matrices produced chaotic results because the body mesh vertices were authored for Charred skeleton bind poses, not v12 retargeted bind poses
- **Solution: `BakeMesh()`** captures Unity's own rendered vertex positions directly, bypassing all matrix math

**Ground truth files:**
- `extracted_assets/chest_baked_mesh.json` — BakeMesh output (5865 verts in SMR local space), convert to Blender with `(-x, -z, y)`
- `extracted_assets/chest_runtime_matrices.json` — bone L2W, v12 bind poses, prefab armor mesh (1152 verts with vertices/triangles/bone weights/bind poses)
- `extracted_assets/v12_armor_simulator.blend` — Blender scene with baked mesh visualization

**Validation result:** User confirmed BakeMesh visualization in Blender exactly matches in-game appearance — armored torso with sword-wielding arm raised, good torso shape, thin arms.

### Step 3 — Bind pose experiments (NEXT)

**Workflow for testing bind pose changes:**
1. Modify bind pose(s) in `s_chestRetargetedBPs` in `CharredWarriorPatches.cs`
2. `dotnet build` → press F10 in-game → triggers BakeMesh dump
3. Copy `chest_baked_mesh.json` to `extracted_assets/`
4. Visualize in Blender via BlenderMCP — convert vertices with `(-x, -z, y)`
5. Compare against previous BakeMesh ground truth

**Key insight from Step 2:** The combined `Body(Clone)` mesh (5865 verts) contains both Charred body vertices and armor vertices, all skinned with the same v12 bind poses. The body arm vertices were authored for the Charred skeleton's original bind poses — the v12 retargeted arm BPs cause the body's arms to appear thin/twisted. The torso BPs work well because Player and Charred torso orientations are similar.

**Blender comparison scene:** `extracted_assets/v12_armor_simulator.blend`
- Left object (`BakedMesh_GroundTruth`): current v12 in-game appearance captured via BakeMesh (animated pose)
- Right object (`Reference_RestPose`): rest-pose body+armor mesh from `knightchest_mesh_data.json` showing target arm proportions
- The reference clearly shows fuller, properly proportioned arms vs v12's thin/distorted arms

**Possible approaches:**
- Blend v12 arm BPs with Charred body arm BPs to find a compromise that works for both body and armor vertices
- Prevent vanilla mesh combining so body keeps Charred BPs and armor keeps v12 BPs separately
- Accept thin arms and focus on reducing twist artifacts

**Iteration workflow:**
1. Modify bind poses in `s_chestRetargetedBPs` dictionary in `CharredWarriorPatches.cs`
2. `dotnet build` → press F10 in-game → captures `chest_baked_mesh.json` via BakeMesh
3. Copy JSON to `extracted_assets/`, load in Blender, compare against reference rest-pose mesh
4. Repeat until arms match reference proportions

### Step 4 — Apply proven improvements

Only apply bind pose changes that have been verified to match between simulator and in-game.

## Future Improvements

- Arm scale adjustment: Consider applying targeted scale corrections to arm/hand bones to reduce "skinny" appearance while preserving position/orientation
- Arm twist mitigation: Explore per-bone rotation blending to reduce twisted appearance in fingers/wrists
- Validation: Full in-game animation testing across all combat moves
- Blender scene improvement: ✅ Charred body + sinew + skull + eyes and Player naked body now in `v12_armor_simulator.blend`

---

## Verification

1. After Phase 1: confirm `padded_cuirrass_data.json` and `charred_skeleton.json` exist and contain the expected bone names and matrix data
2. After Phase 2: open `padded_cuirrass_retargeted_bindposes.json` and verify it contains 53 entries with valid 4×4 matrix data
3. After Phase 3: `dotnet build` → launch Valheim → spawn `charred_melee` → press F9 → chest armor appears at torso height, arm guards on arms, gauntlets on hands, all stay coherent through combat animation
