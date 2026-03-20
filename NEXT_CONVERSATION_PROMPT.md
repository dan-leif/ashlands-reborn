# Handoff — Ashlands Reborn: Chest Armor Phase 5 Step 3

## Goal
Begin one-bone bind pose experiments to fix the thin/distorted arms on Padded_Cuirrass chest armor worn by Charred warriors.

## Current status: Blender visualization scene complete, ready for experiments

### Blender scene: `extracted_assets/v12_armor_simulator.blend`

Side-by-side character visualization with all meshes correctly positioned:

| Object | Type | Verts/Bones | Parent | Location | Status |
|--------|------|-------------|--------|----------|--------|
| Charred_Skeleton | ARMATURE | 70 bones | None | X=-3 | OK |
| Charred_Body | MESH | 2786v | Charred_Skeleton | 0,0,0 | OK |
| Charred_Skull | MESH | 530v | Charred_Skeleton | 0,0,0 | OK — bind-pose remapped |
| Charred_Sinew | MESH | 488v | Charred_Skeleton | 0,0,0 | OK — bind-pose remapped |
| Charred_Eyes | MESH | 12v | Charred_Skeleton | 0,0,0 | OK — bind-pose remapped |
| Player_Skeleton | ARMATURE | 57 bones | None | X=3 | OK — rotation [90°,0,0] |
| Player_Body | MESH | 1010v | Player_Skeleton | 0,0,0 | OK — runtime naked body |
| BakedMesh_GroundTruth | MESH | 5865v | None | X=-1 | Hidden — Phase 5 reference |
| Reference_RestPose | MESH | 5865v | None | X=1 | Hidden — Phase 5 reference |
| CharredArmature | ARMATURE | 54 bones | None | 0,0,0 | Hidden — legacy armor sim |
| KnightChest | MESH | 5865v | CharredArmature | 0,0,0 | Hidden — legacy armor sim |
| V12_Simulated | MESH | 5865v | None | X=-2.5 | Hidden |
| V12_Simulator | MESH | 5865v | None | 0,0,0 | Hidden |
| V13_Fixed | MESH | 5865v | None | X=2.5 | Hidden |
| Sun | LIGHT | — | None | 2,-2,5 | Hidden |

### What was completed this session
- **Runtime data extraction (F11 key):** Added `DumpPlayerAndSinewData()` to `CharredWarriorPatches.cs`
  - `player_body_runtime.json` — 1010v naked player body (vertices, triangles, normals, bone weights, bind poses, BakeMesh)
  - `charred_sinew_data.json` — All 4 Charred mesh parts (Body, Sinew, Skull, Eyes) with transforms, bind poses, BakeMesh
- **Player_Body replaced:** 1387v clothed bundle mesh → 1010v runtime naked body with 53 bone vertex groups
- **Sinew/Eyes repositioned:** Per-vertex bind-pose remap (`body_bp⁻¹ × sinew_bp`) fixed positioning to match in-game BakeMesh
- **Charred_Skeleton rotation fixed** by user in Blender

### Key discovery: Sinew scale factor
The Sinew and Eyes meshes have `localScale=[128.6, 128.6, 128.6]` vs Body's `[100, 100, 100]`. Their bind poses include a 1.286× scale factor. The bind-pose remap formula correctly accounts for this difference.

## Next step: Phase 5 Step 3 — Bind pose experiments

**Iteration workflow:**
1. Modify bind pose(s) in `s_chestRetargetedBPs` in `CharredWarriorPatches.cs`
2. `dotnet build` → press F10 in-game → captures `chest_baked_mesh.json` via BakeMesh
3. Copy JSON to `extracted_assets/`, load in Blender, compare against reference
4. Repeat until arms match reference proportions

**Possible approaches (from CHEST_RETARGET_PLAN.md):**
- Blend v12 arm BPs with Charred body arm BPs to find a compromise
- Prevent vanilla mesh combining so body keeps Charred BPs and armor keeps v12 BPs separately
- Accept thin arms and focus on reducing twist artifacts

## Key files

| File | Purpose |
|------|---------|
| `AshlandsReborn/Patches/CharredWarriorPatches.cs` | Plugin code — `s_chestRetargetedBPs` dictionary, dump methods |
| `AshlandsReborn/Plugin.cs` | Key bindings — F10 (chest dump+refresh), F11 (player+sinew dump) |
| `extracted_assets/v12_armor_simulator.blend` | Blender visualization scene |
| `extracted_assets/player_body_runtime.json` | 1010v naked player body mesh data |
| `extracted_assets/charred_sinew_data.json` | Charred mesh transforms and positioning data |
| `extracted_assets/chest_baked_mesh.json` | BakeMesh ground truth (v12 in-game appearance) |
| `extracted_assets/chest_runtime_matrices.json` | Runtime bone L2W, bind poses, prefab mesh |
| `CHEST_RETARGET_PLAN.md` | Full retargeting plan and history |

## Technical notes

- **F10:** Dumps `chest_baked_mesh.json` + `chest_runtime_matrices.json`, then refreshes Charred Warriors
- **F11:** Dumps `player_body_runtime.json` + `charred_sinew_data.json`
- **Coordinate conversions:**
  - Player body (runtime→Blender): `(-x*100, z*100, -y*100)` — accounts for mesh localRotation(-90°X) and localScale(100)
  - Charred meshes (bundle→Blender): `(-x, y, z) * scale_factor` with armature at origin
  - BakeMesh→Blender: `(-x, -z, y)` per CHEST_RETARGET_PLAN.md
- **Bind-pose remap:** `v_new = body_bp⁻¹ × other_bp × v_old` — used for Skull, Sinew, Eyes
- **Unity matrices:** Row-major 16-element arrays → Blender `Matrix(((arr[0:4]),...))` — NO transpose
- Use **Opus** model for 3D matrix math work
