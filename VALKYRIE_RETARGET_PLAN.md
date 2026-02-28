# Valkyrie Mesh Retarget Plan

Goal: Make the intro Valkyrie mesh play Fallen Valkyrie combat animations by re-skinning it to the Fallen Valkyrie's rig.

---

## Overview


| Phase | What                                                 | Who          |
| ----- | ---------------------------------------------------- | ------------ |
| 1     | Extract Valkyrie + Fallen Valkyrie from game files   | You (manual) |
| 2     | Retarget Valkyrie mesh to Fallen rig in Blender      | You (manual) |
| 3     | Export retargeted mesh and create Unity asset bundle | You (manual) |
| 4     | Update mod to load bundle and use retargeted mesh    | AI (code)    |


---

## Phase 1: Extract Assets (You)

**Tools to install:**

- [AssetRipper](https://github.com/AssetRipper/AssetRipper/releases) (or [Asset Studio](https://github.com/Perfare/AssetStudio))
- [ValheimExportHelper](https://github.com/heinermann/ValheimExportHelper) (for correct FBX export from Valheim)

**Steps:**

1. Find Valheim install: `Steam/steamapps/common/Valheim`
2. Open `valheim_Data/sharedassets0.assets` (and other sharedassets) in AssetRipper
3. Search for prefabs: `Valkyrie`, `FallenValkyrie`
4. Export both as FBX (use ValheimExportHelper if standard export breaks meshes)
5. Save to a folder, e.g. `c:\DEV\ashlands-reborn\extracted\`

**Output:** `Valkyrie.fbx`, `FallenValkyrie.fbx` (or similar)

---

## Phase 2: Retarget in Blender (You)

**Tool:** [Blender](https://www.blender.org/download/) (free)

**Steps:**

1. Open Blender. Delete default cube.
2. File → Import → FBX. Import `FallenValkyrie.fbx`.
3. Import `Valkyrie.fbx` (as a second import, or in a new scene to compare).
4. Inspect both: select each armature, go to Edit Mode (Tab), see bone names and hierarchy.
5. Goal: Take the Valkyrie **mesh** (not its armature) and skin it to the Fallen Valkyrie **armature**.
6. **Retarget method (pick one):**
  - **A) Transfer Weights:** If both meshes are similar shape, select Valkyrie mesh, then Fallen mesh, then Fallen armature → Weight Paint → Transfer Weights (or use Data Transfer modifier).
  - **B) Manual weight paint:** Select Valkyrie mesh, add Fallen armature as modifier (Armature modifier), then weight-paint vertices to bones.
  - **C) Bone mapping:** If bone names differ but structure is similar, use Blender scripts or addons to map Valkyrie vertex groups to Fallen bones.
7. Export only the retargeted Valkyrie mesh + Fallen armature as FBX. Ensure "Armature" and "Mesh" are selected.

**Output:** `Valkyrie_retargeted.fbx`

**Note:** Phase 2 is the hardest. Blender has a learning curve. YouTube "Blender retarget mesh to different armature" for tutorials.

---

## Phase 3: Create Asset Bundle (You)

**Tool:** Unity (same version as Valheim: 2019.4.x recommended)

**Steps:**

1. Create new Unity project (3D, 2019.4).
2. Import `Valkyrie_retargeted.fbx`.
3. In Project window, select the mesh. In Inspector, ensure it has correct materials/textures (or assign Valkyrie textures if exported separately).
4. Create a prefab: drag the mesh+armature into the scene, then drag from Hierarchy to Project to make a prefab.
5. Assign to Asset Bundle: select prefab, in Inspector bottom panel set "AssetBundle" to `valkyrieretarget` (lowercase).
6. Build: Window → Asset Bundle Browser (or build via script) → Build for your platform (Windows).

**Output:** `valkyrieretarget` (asset bundle file)

---

## Phase 4: Mod Integration (AI)

**What I can do:**

- Add asset bundle loading to the mod (load from `BepInEx/plugins/AshlandsReborn/` or similar)
- Replace the runtime Valkyrie-prefab copy with the retargeted mesh from the bundle
- Wire up SkinnedMeshRenderer with Fallen Valkyrie bones (they will match by name since mesh was built for that rig)
- Keep existing config (UseIntroVisualsOnly, etc.) and fallback logic

**What you provide:**

- The built asset bundle file in the right folder
- Bone names from the Fallen Valkyrie armature (I can add debug logging to dump these if needed)

---

## Checklist

- Phase 1: Valkyrie.fbx, FallenValkyrie.fbx extracted
- Phase 2: Valkyrie mesh retargeted to Fallen armature in Blender
- Phase 3: Asset bundle built in Unity
- Phase 4: Mod updated to load bundle (AI)
- Test in-game: spawn Fallen Valkyrie, UseIntroVisualsOnly, see intro model with combat animations

---

## If You Get Stuck

- **AssetRipper:** Valheim assets can be tricky. ValheimExportHelper or Asset Studio may work better for Valheim specifically.
- **Blender:** The retarget step has many approaches. Share what you see (bone counts, names) and I can suggest a more specific workflow.
- **Unity:** Asset bundles for Valheim mods often need specific build settings; I can provide exact steps when you reach Phase 3.

