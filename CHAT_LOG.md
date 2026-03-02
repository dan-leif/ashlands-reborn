# Ashlands Reborn - Development Status

## Current State (2026-03-02)

### What Works
- **Legs (knightlegs)**: Essentially perfect. Uses charred body bind poses with auto-scale correction (`charredBP * scaleMat`).
- **Cape (ss_storrcape)**: Correct size and shape. Same approach as legs.
- **Sword (Krom)**: Working. Scale-adjusted rigid attachment.
- **Helmet**: Working (unconditional swap with refresh logic).

### Chest Armor - Partially Working (knightchest)
- **Shape**: Well-formed and correctly shaped using the **runtime bind-pose approach** (`bp = bone.worldToLocal * smr.localToWorld * scaleAndShift`).
- **Position**: Floating at knee level instead of the torso. The Hips-centering shift (`Translate(-hipsMeshPos)`) was added but hasn't fully resolved the vertical offset.
- **Animation**: Does not correctly track the warrior's skeleton. Arms don't follow warrior movements (sword holding, combat). Breathing motion is exaggerated. The chest follows the warrior's movement loosely like a ragdoll.

### Why Two Different Bind-Pose Approaches
- **Legs/Cape**: The charred body's rest-pose bind poses (`charredBP * scaleMat`) work perfectly. The bone orientation differences between player and charred skeletons are manageable for lower-body bones.
- **Chest**: The same rest-pose approach causes "explosions" (spirals of long triangles). The diagnostic dump shows ~177-degree rotation deltas on arm/shoulder bones between player and charred bind poses. The runtime approach avoids this by using actual bone transforms at the current animation frame, producing correct mesh shape.

### Key Diagnostic Findings
- Player and Charred share the same bone names but have significantly different bone orientations at rest, especially for upper-body bones (LeftShoulder: 176.3 deg delta, LeftArm: 176.5 deg, etc.)
- Lower-body bones also have ~180 deg deltas but legs still work with charred BPs (likely because the rotation is a clean 180-flip that preserves shape)
- Upper-body rotations are not clean flips (e.g., 176.3 deg, not 180 deg), causing mesh distortion
- The Charred skeleton has an extra `Root` bone between `Armature` and `Hips`
- Armature has localScale=(100,100,100) with character scale 0.9, giving worldScale=90
- The armor SMR has localRot=(270,0,0) and localScale=(100,100,100)

### Approaches Tried and Failed for Chest
1. **charredBP * scaleMat** (same as legs): Exploded/spirals
2. **Scale correction on fallback BPs + all-SMR collection**: Worse (legs/cape also broke)
3. **Scale-only approach**: Broke everything
4. **Hybrid (charred translation+scale, player rotation)**: Still exploded
5. **Rest-pose BPs + coordinate-space correction** (`bodyBP * bodySMR.W2L * armorSMR.L2W * scale`): Swirling armor
6. **Hips-anchored BPs** (`bodyBP * hipsBP^-1 * Hips.W2L * armorSMR.L2W * scale`): Still spirals
7. **Runtime BPs** (`bone.W2L * smr.L2W * scale`): Well-formed but at knee level, poor animation tracking <-- CURRENT for chest
8. **Runtime BPs + Hips shift**: Same as #7 but with Hips centering attempt

### Next Steps for Chest
- The runtime approach gives correct shape. The remaining problems are position (vertical offset) and animation tracking.
- Position: The mesh centers on the SMR origin (feet/Armature level). Need to shift it up to the torso. The Hips-centering shift was a first attempt but the offset may need to account for the coordinate space transformation (the SMR has a 270-degree X rotation and 100x scale).
- Animation: At the captured frame, animation deltas are zero by definition. As bones animate away from the captured pose, the deltas should accumulate. The poor tracking may be because the Hips shift places vertices at the wrong relative positions to bones, or because the captured frame's bone orientations differ enough from subsequent frames.
- Alternative: Consider a rigid-attach approach for the chest (bake mesh, parent to Spine2) since knight armor is rigid plate anyway.

### File Overview
- `AshlandsReborn/Patches/CharredWarriorPatches.cs`: All armor/helmet/sword swap logic, bind-pose computation, diagnostic dump
- `AshlandsReborn/Plugin.cs`: BepInEx plugin entry, config entries, refresh/revert commands
- `BIND-POSE DIAGNOSTIC DUMP.txt`: Per-bone comparison of player vs charred bind poses, skeleton hierarchy, vanilla AttachArmor state
- `ARMOR BONE MAPPING DUMP.txt`: Per-SMR bone mapping between prefab and charred skeleton
