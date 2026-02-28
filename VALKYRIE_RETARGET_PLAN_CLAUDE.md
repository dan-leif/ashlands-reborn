# Valkyrie Mesh Retarget Plan (Claude's Version)

Goal: Make the intro Valkyrie appearance play Fallen Valkyrie combat animations.

---

## Recommended Approach: Start Simple, Escalate If Needed

### Step 1: Dump Bone Names (AI does code, you run game)

Add debug logging to dump both rigs' bone names/hierarchy to the BepInEx log.

- **AI does:** Write the logging code.
- **You do:** Run the game, spawn a Fallen Valkyrie, paste the log output.
- **Output:** We see exact bone names for both rigs and can decide which path to take.

### Step 2: Choose a Path Based on Results

---

## Path A: Material/Texture Swap (Zero External Tools)

**When to use:** If the Fallen Valkyrie mesh looks acceptable when given clean textures (most of the "decay" is texture, not geometry).

**How it works:** Copy the intro Valkyrie's materials onto the Fallen Valkyrie's existing SkinnedMeshRenderers at runtime. No bone mapping needed; the Fallen mesh, rig, and animations stay untouched.

- **AI does:** Write all the mod code.
- **You do:** Test in-game, decide if it looks good enough.
- **Pros:** No external tools. Combat animations work perfectly. Easy to maintain across Valheim updates.
- **Cons:** Mesh geometry (torn wings, missing feathers, etc.) won't change. Only colors/surfaces change.

---

## Path B: Code-Side Bone Mapping (No External Tools)

**When to use:** If the bone dump from Step 1 shows the rigs overlap well (e.g. same names with prefixes, or only a few missing bones).

**How it works:** Build a bone name mapping table in code. For missing bones (intro Valkyrie is simpler), map to the nearest parent bone in the Fallen rig. Copy intro mesh + materials onto the Fallen skeleton using the mapping.

- **AI does:** Write the mapping table and all code based on your bone dump.
- **You do:** Run the game and paste logs.
- **Pros:** No external tools. Intro Valkyrie mesh with combat animations.
- **Cons:** If meshes are fundamentally different shapes, deformation may look wrong even with perfect bone mapping.

---

## Path C: Blender Python Script (Requires Blender + AssetRipper)

**When to use:** If Paths A and B don't look good enough and you want the exact intro Valkyrie mesh properly retargeted.

**Tools to install:**
- [AssetRipper](https://github.com/AssetRipper/AssetRipper/releases) (extract game assets)
- [Blender](https://www.blender.org/download/) (free 3D editor)
- [ValheimExportHelper](https://github.com/heinermann/ValheimExportHelper) (correct FBX export)

**How it works:**
1. Extract both Valkyrie FBX files with AssetRipper.
2. Import both into Blender.
3. Run a Python script (written by AI) that automates: bone mapping, weight transfer, cleanup, FBX export.
4. Build an asset bundle in Unity (2019.4.x).
5. AI updates mod to load the bundle.

- **AI does:** Write the Blender Python script. Write the Unity build instructions. Write the mod code to load the bundle.
- **You do:** Install tools, run AssetRipper, import into Blender, run script, build bundle in Unity.
- **Pros:** Exact intro Valkyrie mesh, properly deforming with combat animations.
- **Cons:** Requires installing AssetRipper, Blender, Unity. Must redo if Valheim updates the Fallen Valkyrie model.

---

## Path D: Full Manual Retarget (Original Plan)

**When to use:** If the Blender script (Path C) doesn't produce good results and manual weight painting is needed.

See `VALKYRIE_RETARGET_PLAN.md` for full details. This is the most effort but gives full control.

- **AI does:** Phase 4 (mod code).
- **You do:** Phases 1-3 (extract, retarget in Blender, build asset bundle).

---

## Summary

| Path | External Tools | AI Help | Quality | Effort |
|------|---------------|---------|---------|--------|
| A: Material swap | None | Full | Good (if decay is mostly texture) | Low |
| B: Code bone mapping | None | Full | Good (if rigs overlap) | Low |
| C: Blender script | AssetRipper + Blender + Unity | Mostly AI | Best | Medium |
| D: Manual retarget | AssetRipper + Blender + Unity | Code only | Best | High |

---

## Next Action

Start with **Step 1** (bone name dump). Then decide between Paths A-D based on what the rigs look like.
