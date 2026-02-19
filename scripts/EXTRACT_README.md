# Extracting Valheim Terrain Textures

The ash/gray terrain color comes from textures in Valheim's asset bundles.

## Asset locations (from manifest_extended)

| Asset | Bundle ID | Path |
|-------|-----------|------|
| **terrain_d_array** (full diffuse atlas) | c4210710 | Assets/world/terrain/terrain_d_array.texture2darray |
| terrain_d.png | c4210710 | Assets/world/terrain/terrain_d.png |
| terraintile_0.png .. terraintile_15.png | c4210710 | Assets/world/terrain/array generation/terraintile_N.png |

Bundles path: `Steam/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/Bundles/`

## Option 1: Python script (UnityPy)

```powershell
pip install UnityPy
python scripts/extract_terrain_textures.py
```

Output goes to `extracted_textures/`. The script loads bundle `c4210710` and exports terrain Texture2D and Texture2DArray slices as PNG.

If UnityPy fails (Valheim's format may be custom), use Option 2.

## Option 2: AssetRipper (GUI)

1. Download [AssetRipper](https://github.com/AssetRipper/AssetRipper/releases)
2. File → Load Folder → select `valheim_Data` folder
3. In the Project Explorer, search for "terrain" or "terraintile"
4. Right-click textures → Export

## Option 3: Asset Studio

1. Download [Asset Studio](https://github.com/Perfare/AssetStudio/releases)
2. File → Load folder → `valheim_Data/StreamingAssets/SoftRef/Bundles`
3. Or load individual bundle file `c4210710` (no extension)
4. Filter by Texture2D, find terrain_d or terraintile_*
5. Export as PNG

---

## Extracting and Disassembling the Heightmap Shader

The terrain slice selection (vertex color → texture array index) lives in the `Custom/Heightmap` shader. Full HLSL decompilation fails (DXDecompiler NullRef on Unity shaders), but assembly disassembly succeeds.

```powershell
pip install UnityPy lz4
python scripts/extract_and_decompile_shader.py
```

- Loads bundle `c4210710`, decompresses LZ4 blob, extracts 256 DXBC shader blobs
- Clones/builds [DXDecompiler](https://github.com/spacehamster/DXDecompiler) if needed
- Disassembles to HLSL assembly (`.asm`) via `dxc -a` style output

**Output:** `extracted_shaders/Heightmap_*.dxbc` and `Heightmap_*.asm`

**Main terrain pixel shaders** (use texture2darray t14/t15 for _DiffuseArrayTex): `Heightmap_064`–`095`, `Heightmap_176`–`191`. See [`SHADER_SLICE_MAPPING.md`](../SHADER_SLICE_MAPPING.md) in project root for the full Meadows/Ashlands slice mapping.
