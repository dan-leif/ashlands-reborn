# Terrain Texture Replacement Plan

## Progress So Far

### Terrain Override (working)
- **GetBiomeColor** prefix: Ashlands → Meadows vertex color for non-lava vertices
- **RebuildRenderMesh** prefix/postfix: corner override, lava vertex restore, `_AshlandsVariationCol` tint
- **FindBiomeClutter** postfix: Meadows grass placement in Ashlands
- **OnEnable** postfix: neighbor Poke when chunks load
- **ClutterSystem.GetGroundInfo** postfix: grass placement, lava exclusion via LavaGrassThreshold

### Known Limitation
- **Yellow seam at 64x64 zone boundaries** – accepted; vertex color, shader, neighbor-poke approaches did not resolve it.

### Texture Extraction (done)
- Python 3.12 + UnityPy installed
- Extracted 24 terrain textures to `extracted_textures/`:
  - `terrain_d.png` – full terrain diffuse atlas
  - `terraintile_0.png` … `terraintile_15.png` – individual tiles (one is ash/gray)
  - `terraintile_n_0`, `terraintile_n_1`, `terraintile_n_2` – normals
  - `TerrainVarietyNoise.png`, `grass_terrain_color.png`, etc.
- Script: `scripts/extract_terrain_textures.py`
- Asset source: bundle `c4210710`, path `Assets/world/terrain/`

---

## Terraintile_7 → Grass Swap (reverted)

- **terraintile_7** = ash/gray stone (identified by user)
- **terraintile_0** = grass (Meadows)
- **Attempted**: Create modified `_DiffuseArrayTex` with slice 7 replaced by slice 0 via Graphics.CopyTexture
- **Result**: Terrain turned bright white (likely format mismatch or mipmap handling). Reverted.
- **Future**: Use CustomTextures mod with scene dump to identify correct replacement naming; or investigate original texture format/mipmaps before retrying runtime swap
