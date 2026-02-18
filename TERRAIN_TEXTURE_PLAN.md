# Terrain Texture Replacement Plan

## Progress So Far

### Terrain Override (working)
- **GetBiomeColor** prefix: Ashlands → Meadows vertex color for non-lava vertices
- **RebuildRenderMesh** prefix/postfix: corner override, lava vertex restore, `_AshlandsVariationCol` tint
- **FindBiomeClutter** postfix: Meadows grass placement in Ashlands
- **OnEnable** postfix: neighbor Poke when chunks load
- **ClutterSystem.GetGroundInfo** postfix: grass placement, lava exclusion via LavaEdgeThreshold

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

## Next Step: Replace Gray Stone With Grass

### 1. Identify which terraintile is the gray stone
- Compare `terraintile_0.png` … `terraintile_15.png` visually
- Match against in-game ashlands terrain appearance
- Document the tile index for the ash/gray texture

### 2. Replace with grass
- Create a grass-colored variant (based on Meadows grass tile or `grass_terrain_color.png`)
- Apply via one of:
  - **CustomTextures mod**: Place replacement PNG in `BepInEx/plugins/CustomTextures` with correct naming (e.g. `terrain_<id>_terraintile_N.png` or per CustomTextures docs)
  - **Texture loading in our mod**: Swap the texture at runtime when terrain material is set up (would require identifying the material/texture binding)
- Keep original as backup; test in-game

### 3. Validate
- Load Ashlands, confirm gray terrain appears green/grass
- Verify lava still looks correct
- Check for seams or visual artifacts at boundaries
