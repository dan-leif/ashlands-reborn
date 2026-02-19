# Heightmap Shader Slice Mapping (from Heightmap_064.asm)

Analysis of how Valheim's terrain shader selects `_DiffuseArrayTex` slices from vertex color (biome) and slope.

## Biome Vertex Colors (from GetBiomeColor)

| Biome       | Color32 (R,G,B,A)     | Normalized v5  |
|-------------|-----------------------|----------------|
| Meadows     | (0,0,0,0)             | (0,0,0,0)      |
| Swamp       | (255,0,0,0)           | (1,0,0,0)      |
| Mountain    | (0,255,0,0)           | (0,1,0,0)      |
| BlackForest | (0,0,255,0)           | (0,0,1,0)      |
| Plains      | (0,0,0,255)           | (0,0,0,1)      |
| **AshLands**| **(255,0,0,255)**     | **(1,0,0,1)**  |
| DeepNorth   | (0,255,0,0)           | (0,1,0,0)      |
| Mistlands   | (0,0,255,255)         | (0,0,1,1)      |

## Slice Indices Used in Shader (t14 = _DiffuseArrayTex)

| Slice | Context / Trigger |
|-------|-------------------|
| **1** | Biome layer (r5.x > 0.4 after smoothstep) |
| **2** | Normal map array (t15) – shared base |
| **3** | Biome layer (r6.y > 0.4) |
| **4** | Steep slope (v3.z < 0.999), blended with 14 |
| **5** | Base terrain (flat/angled), blended with 14 by r2.w |
| **6** | Secondary terrain layer |
| **7** | Ashlands/Variation block (lines 378–379), with slice 13 |
| **8** | Biome layer (r5.x path) |
| **9** | Distance/LOD blend |
| **10** | With slice 3 for blend |
| **11** | When r2.w > 0 (steep/angled blend factor) |
| **12** | Snow/frost layer |
| **13** | Ashlands block (paired with 7) |
| **14** | Base terrain (paired with 5); steep (paired with 4) |
| **15** | Ashlands variation/color block (lines 449–458) |

## Slope Logic

- **Flat (v3.z ≈ 1)**: Uses base blend slice 5 ↔ 14.
- **Steep (v3.z < 0.999)**: Uses slices 4 ↔ 14 (cliff).
- **Angled**: Blends between flat and steep.
- Thresholds: ~0.8–0.85 (angled), ~0.7 (steep cliff).

## Ashlands-Specific Paths

1. **Lines 378–379**: `mov r1.zw, l(0,0,7,13)` – samples **7** and **13**, blended.
2. **Lines 449–458**: Samples slice **15** for variation.
3. **Base path**: Uses 5 and 14 like other biomes; vertex color `v5` drives `r2.w` and `r5.x/y/z` to select additional layers.

## Recommendations for Ashlands Reborn

To fully replace Ashlands terrain with grass (Meadows), swap these slices with grass (e.g. slice 0):

- **7** – main ash texture (already in config as default)
- **13** – paired with 7 in ash block
- **4** – steep Ashlands cliff
- **14** – used in base and steep blends for Ashlands

Test config: `AshlandsTextureSlices = "4,7,13,14"`.

If white/gray patches remain, also try adding: **1**, **3**, **8**, **11**, **15** (other biome/variation layers that can activate with R=1,A=1).
