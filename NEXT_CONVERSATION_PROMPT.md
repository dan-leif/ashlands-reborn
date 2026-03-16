# Handoff — Ashlands Reborn, Chest Armor Phase 5 Step 3: Blender Skinning Simulator

## Goal
Fix the skinny/distorted arms on Charred warrior chest armor (Padded_Cuirrass). Build a Blender skinning simulator to iterate arm bind poses without loading the game.

## Current status: BLOCKED on coordinate space mismatch

### What works
- **Skinning math verified**: `skinned_world = sum(w * bone_L2W * v12_BP * v)` matches Unity's skinning test exactly (3 vertices, <0.0001 error)
- **Matrix parsing**: Unity row-major 16-element arrays → Blender `Matrix((row0,row1,row2,row3))` — NO transpose needed
- **BakeMesh→Blender conversion**: negate X, subtract centroid to center at origin (verified exact match across all 5865 vertices)
- **Blender scene** (`extracted_assets/v12_armor_simulator.blend`) has orange (GT) and green (reference) meshes

### What doesn't work
- **Cannot replicate BakeMesh output** from `knightchest_mesh_data.json` vertices + v12 bind poses + bone L2W
- `inv(SMR_L2W) * skinned_world` gives vertex range [-0.24, 0.36] but BakeMesh vertices span [0, 2]
- Affine least-squares solve has 0.2-0.6 residual errors — the mapping is NOT a simple transform
- **Root cause**: `knightchest_mesh_data.json` vertices are in a DIFFERENT coordinate space than the runtime `Body(Clone)` mesh. Body(Clone) is `mesh_is_readable: false` so its actual vertices were never dumped.

## What to do next: Fix the data gap

### Option A (recommended): Dump Body(Clone) vertices from Unity
Add code in `CharredWarriorPatches.cs` DumpRuntimeMatrices to make the mesh readable and dump its vertices:
```csharp
// Before BakeMesh, make mesh readable and dump raw vertices
var rtMesh = smr.sharedMesh;
if (!rtMesh.isReadable)
{
    // Create a readable copy
    var readable = new Mesh();
    readable.indexFormat = rtMesh.indexFormat;
    // Use Graphics.CopyTexture or mesh copy approach
}
// Or simpler: use mesh.vertices which might work even if !isReadable in some Unity versions
```
Then add the raw Body(Clone) vertices + bone weights to `chest_runtime_matrices.json`.

### Option B: Skip simulator, iterate via Unity
Modify bind poses in C# → rebuild → F10 BakeMesh → import to Blender → compare. Slower but guaranteed.

### Option C: Use prefab mesh (1152 verts) with corrected bind poses
The prefab Padded_Cuirrass mesh (1152 verts) IS readable and has its own bind poses (scale ~1.0). The v12 bind poses have scale ~0.176. If we can find the transform between these coordinate spaces, we can build a simulator with just the armor portion. This would actually be preferable since we only care about fixing the armor, not the body.

## Key arm bones to experiment with
`LeftShoulder`, `LeftArm`, `LeftForeArm`, `RightShoulder`, `RightArm`, `RightForeArm` (and hand bones)

## Key files
- `extracted_assets/chest_runtime_matrices.json` — bone L2W, bind poses, prefab mesh (1152v), bone weights
- `extracted_assets/chest_baked_mesh.json` — BakeMesh ground truth (5865v combined body+armor)
- `extracted_assets/knightchest_mesh_data.json` — 5865v mesh data (WRONG coordinate space for simulator)
- `extracted_assets/v12_armor_simulator.blend` — Blender comparison scene
- `AshlandsReborn/Patches/CharredWarriorPatches.cs:293` — `s_chestRetargetedBPs` dictionary (53 bones)
- `CHEST_RETARGET_PLAN.md` — full retargeting plan

## Important notes
- Unity matrices are row-major. JSON arrays are 16 elements in row-major order. In Blender: `Matrix(((arr[0:4]),(arr[4:8]),(arr[8:12]),(arr[12:16])))` — NO transpose.
- BakeMesh returns vertices in mesh-local space with skinning applied, but the exact transform from `sum(w*L2W*BP*v)` world space back to this local space is NOT simply `inv(SMR_L2W)`.
- Use **Opus** model for this work (3D matrix math benefits from precise reasoning).
- The user prefers iterating in Blender over loading the game. Only deploy to Unity for final validation.
