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

## v3 recon (2026-07-11, decoded slices + full asm trace for AshBlend)

Ground truth from BC7-decoding `terrain_d_array` out of bundle `c4210710`'s `.resS`
stream (offsets via UnityPy; see `scripts/extract_terrain_textures.py` +
`extracted_textures/terrain_d_array_slice_*.png`):

- `terrain_d_array` (`_DiffuseArrayTex`, t14): 256x256, **16 slices, BC7 sRGB
  (GraphicsFormat 108), mipCount 1**.
- `terrain_n_array` (t15): 256x256, **4 slices only**, BC7 UNorm (109), linear.
- Slice contents: 0=Meadows grass, 1=BlackForest forest floor, **3=Swamp/dirt brown
  (also the paint-mask hoe-path texture, asm line ~702)**, 4=gray cliff, 5=base rock,
  6=molten lava orange, **7=main ash (near-black)**, 8=Plains khaki, 9=beach sand,
  10=cultivated soil, 11=shallow-moss, 12=snow, 13=ash pair, 14=dark cracked rock,
  15=ash variation/glow veins.

Biome weights are **Chebyshev distances** from the corner colors (asm lines 121-139),
e.g. swamp = `1 - max(|R-1|,|G|,|B|,|A|)`, ashlands = `1 - max(|R-1|,|G|,|B|,|A-1|)`;
the Legacy diagonal (t,0,0,t) gives Plains weight `1 - max(t, 1-t)` peaking 0.5 at
mid-fade - the yellow fringe, exactly.

**The swamp overlay (asm lines 382-397) samples t14 slice 3 gated on swamp weight
> 0.4 (smoothstep to 0.6) and is ALBEDO-ONLY** - it writes the albedo register and
never touches the normal path, so an AshBlend-style slice swap needs no normal-array
patch. Slice 3's alpha modulates the overlay strength (`mad_sat` with the weight).

## Recommendations for Ashlands Reborn

To fully replace Ashlands terrain with grass (Meadows), swap these slices with grass (e.g. slice 0):

- **7** – main ash texture (already in config as default)
- **13** – paired with 7 in ash block
- **4** – steep Ashlands cliff
- **14** – used in base and steep blends for Ashlands

Test config: `AshlandsTextureSlices = "4,7,13,14"`.

If white/gray patches remain, also try adding: **1**, **3**, **8**, **11**, **15** (other biome/variation layers that can activate with R=1,A=1).
