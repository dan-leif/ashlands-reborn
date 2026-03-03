# Ashlands Reborn - Development Status

## Current State (2026-03-02)

### What Works

- **Legs (knightlegs)**: Essentially perfect. Uses charred body bind poses with auto-scale correction (`charredBP * scaleMat`).
- **Cape (ss_storrcape)**: Correct size and shape. Same approach as legs.
- **Sword (Krom)**: Working. Scale-adjusted rigid attachment.
- **Helmet**: Working (unconditional swap with refresh logic).

### Chest Armor - Still Broken (knightchest)

- **Shape**: Well-formed (runtime bind-pose approach preserves mesh shape).
- **Position**: Floating **below** foot level — top of mesh at foot height, bottom several feet underground. Worse than the original "chest-formed-at-feet" state (where top was at knee, bottom at foot).
- **Culling**: The mesh was being frustum-culled (bounds = 0.08×0.03×0.05 in SMR local space = degenerate). Added `updateWhenOffscreen = true` as a diagnostic workaround. This is not a fix — just defeats culling to expose the real position problem.
- **Animation**: Still does not correctly track skeleton (pre-existing issue, not addressed yet).

### Diagnostic Findings (this session)

All candidate shift sources return ~zero in mesh/bind-pose space:
- `hipsMeshPosPlayer` (player prefab BP inverse.pos) = `(0, 0, -0.02)` ≈ zero
- `hipsBPMeshPos` (Charred BP inverse.pos for Hips) = `(0, 0, 0.01)` ≈ zero
- `spine2BPMeshPos` (Charred BP inverse.pos Spine2) = `(0, 0, 0.02)` ≈ zero
- `hipsLocalPos` (InverseTransformPoint of Hips world pos) = `(0, 0, 0.01)` ≈ zero

This means: both the player and Charred skeletons have Hips at the mesh origin in bind-pose space. No useful vertical offset can be extracted from bind poses alone.

Key transform values:
- SMR: `localPos=(0,0,0)`, `localRot=(270,0,0)`, `localScale=(100,100,100)`
- `autoScale=0.2346` (charredSpine=0.0101 / playerSpine=0.0431)
- `smrL2W.pos` (SMR world origin) ≈ 1.26 world units below the Hips bone world pos
- Mesh bounds after clone: `center=(0,0,0)`, `size=(0.08, 0.03, 0.05)` — degenerate (in 100x local space)
- `RecalculateBounds()` on the cloned mesh failed ("not allowed") — mesh may still be marked non-writable

### Bind-Pose Formula (current)

```
BP[i] = bone[i].worldToLocalMatrix * smrL2W * scaleMat * Translate(-hipsBPMeshPos)
```
where `hipsBPMeshPos ≈ 0`, so effectively:
```
BP[i] = bone[i].worldToLocalMatrix * smrL2W * scaleMat
```

The Hips newBP matrix:
```
row0=(0.23, 0.00, 0.00, 0.00)
row1=(0.00, 0.00, 0.23, -0.02)
row2=(0.00,-0.23, 0.00, 0.00)
inverse.pos=(0.00, 0.00, 0.07)
```
Rows 1 and 2 are swapped/negated — the SMR's 270° X rotation is baked into every bind pose, rotating the deformation axes 90° from what animation expects.

### Approaches Tried for Chest Position (this session)

1. **v0 "chest-formed-at-feet"**: `Translate(-hipsMeshPos)` where `hipsMeshPos` from player prefab BP ≈ 0. Mesh top at knee, bottom at foot.
2. **v1 "chest disappeared"**: World-space shift inserted *after* `smrL2W`. All bones got the same constant BP → mesh collapsed to a point.
3. **v2**: `InverseTransformPoint(hipsWorldPos)` → also ≈ zero (SMR origin is already near Hips in world space).
4. **v3 (current)**: `charredBPMap["Hips"].inverse` → also ≈ zero. Same result as v0. Added `updateWhenOffscreen=true` and `RecalculateBounds()`. Mesh now visible (culling defeated) but below ground.

### Root Cause Hypothesis

The knight chest mesh vertices are authored with Y=0 at feet and Y increasing upward. After the bind-pose chain (which includes the SMR's 270° X rotation baked in), the mesh ends up **inverted below the origin**. The 270° X rotation swaps Y↔Z and negates one axis. A vertex at mesh-local `(0, +H, 0)` (upward) becomes `(0, 0, -H)` after the rotation — pointing into the ground in world space.

### Next Step

The 270° X rotation on the SMR is the likely root cause of both the vertical inversion and the axis-swapped bone deformation. The bind-pose formula needs to either:
- **Factor out the SMR rotation** so vertices are oriented correctly before bone transforms are applied, or
- **Apply a compensating rotation** to the shift or to the mesh itself

Specifically: compute the shift in the SMR's *rotated* local space (post-rotation, pre-scale) rather than raw local space. The correct mesh-space upward direction is `smr.transform.up` in world space — use that to construct the vertical shift.

### Key Diagnostic Data Still Needed

- Actual vertex Y-range of the knight chest mesh (to know how far up to shift)
- Whether `RecalculateBounds()` can be called on a writable cloned mesh (the error suggests the clone is still read-only — may need `mesh.MarkDynamic()` first or use `Mesh.Instantiate`)

### Why Two Different Bind-Pose Approaches

- **Legs/Cape**: The charred body's rest-pose bind poses (`charredBP * scaleMat`) work perfectly. The bone orientation differences between player and charred skeletons are manageable for lower-body bones.
- **Chest**: The same rest-pose approach causes "explosions" (spirals of long triangles). The diagnostic dump shows ~177-degree rotation deltas on arm/shoulder bones between player and charred bind poses. The runtime approach avoids this by using actual bone transforms at the current animation frame, producing correct mesh shape.

### Key Diagnostic Findings (earlier)

- Player and Charred share the same bone names but have significantly different bone orientations at rest, especially for upper-body bones (LeftShoulder: 176.3 deg delta, LeftArm: 176.5 deg, etc.)
- Lower-body bones also have ~180 deg deltas but legs still work with charred BPs (likely because the rotation is a clean 180-flip that preserves shape)
- Upper-body rotations are not clean flips (e.g., 176.3 deg, not 180 deg), causing mesh distortion
- The Charred skeleton has an extra `Root` bone between `Armature` and `Hips`
- Armature has localScale=(100,100,100) with character scale 0.9, giving worldScale=90
- The armor SMR has localRot=(270,0,0) and localScale=(100,100,100)

### Approaches Tried and Failed for Chest (earlier sessions)

1. **charredBP * scaleMat** (same as legs): Exploded/spirals
2. **Scale correction on fallback BPs + all-SMR collection**: Worse (legs/cape also broke)
3. **Scale-only approach**: Broke everything
4. **Hybrid (charred translation+scale, player rotation)**: Still exploded
5. **Rest-pose BPs + coordinate-space correction** (`bodyBP * bodySMR.W2L * armorSMR.L2W * scale`): Swirling armor
6. **Hips-anchored BPs** (`bodyBP * hipsBP^-1 * Hips.W2L * armorSMR.L2W * scale`): Still spirals
7. **Runtime BPs** (`bone.W2L * smr.L2W * scale`): Well-formed but at knee level, poor animation tracking ← "chest-formed-at-feet" baseline
8. **Runtime BPs + Hips shift (player BP space)**: Same as #7, shift ≈ 0

### File Overview

- `AshlandsReborn/Patches/CharredWarriorPatches.cs`: All armor/helmet/sword swap logic, bind-pose computation, diagnostic dump
- `AshlandsReborn/Plugin.cs`: BepInEx plugin entry, config entries, refresh/revert commands
- `BIND-POSE DIAGNOSTIC DUMP.txt`: Per-bone comparison of player vs charred bind poses, skeleton hierarchy, vanilla AttachArmor state
- `ARMOR BONE MAPPING DUMP.txt`: Per-bone bone mapping between prefab and charred skeleton
