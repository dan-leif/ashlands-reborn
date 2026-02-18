# Harmony Approach: Ashlands Ground and Grass → Meadows

Investigation of the Valheim terrain system from decompiled `assembly_valheim`.

---

## How Terrain Colors Work

### Ground (terrain mesh)

1. **Heightmap** chunks have vertex colors set in `RebuildRenderMesh()`.
2. Colors come from **`Heightmap.GetBiomeColor(Biome)`** – each biome maps to a `Color32`:
   - **Meadows**: `(0, 0, 0, 0)`
   - **AshLands**: `(255, 0, 0, 255)`
   - Swamp, Mountain, etc. each have different RGBA values.
3. The terrain shader uses these vertex colors to blend textures from the material atlas (grass, dirt, rock, etc.).
4. Ashlands gets gray/ash because its color selects the ash texture; Meadows’ color selects grass.

### Grass (clutter)

1. **ClutterSystem** places grass and vegetation in patches.
2. Biome comes from **`Heightmap.FindBiomeClutter(point)`**.
3. Each clutter type has `m_biome` – it only spawns where the biome matches.
4. Meadows grass has `m_biome = Meadows`; Ashlands has its own clutter (or none).

---

## Patch Targets

### 1. Ground: `Heightmap.GetBiomeColor(Biome biome)` – Prefix

- **Goal:** When biome is AshLands and our terrain override is on, use Meadows color.
- **Method:** Prefix with `ref Heightmap.Biome biome`; when `biome == AshLands` and config enabled, set `biome = Meadows` before the original runs.
- **Effect:** Terrain chunks use Meadows vertex colors, so the shader shows green ground.
- **Usage:** Only `RebuildRenderMesh()` calls this, so it affects terrain visuals only.

### 2. Grass: `Heightmap.FindBiomeClutter(Vector3 point)` – Postfix

- **Goal:** When the real biome is AshLands and override is on, report Meadows so Meadows grass spawns.
- **Method:** Postfix that checks the return value; if AshLands and config enabled, return Meadows instead.
- **Effect:** ClutterSystem treats Ashlands as Meadows and spawns Meadows grass.
- **Usage:** Only used by ClutterSystem for vegetation placement.

---

## Regenerating Existing Chunks

New heightmap chunks pick up our `GetBiomeColor` prefix. Chunks that were already built in Ashlands before the mod (or before the override is enabled) keep old vertex colors.

**Fix:** When entering Ashlands with the terrain override on, call `Heightmap.FindHeightmap(playerPos, 150f)` and then `Poke(delayed: true)` on each heightmap. That triggers `Regenerate()` and `RebuildRenderMesh()` with the patched colors.

---

## Config

Add **`EnableTerrainOverride`** (bool, default true) – separate from `EnableWeatherOverride` so sky and terrain can be toggled independently.

---

## Implementation Summary

| Patch            | Type   | Class    | Method              | When to apply           |
|------------------|--------|----------|---------------------|--------------------------|
| Ground colors    | Prefix | Heightmap| GetBiomeColor(Biome)| biome == AshLands + config |
| Grass placement  | Postfix| Heightmap| FindBiomeClutter    | result == AshLands + config |
| Chunk refresh    | Logic  | EnvMan patch | On enter Ashlands | Poke nearby heightmaps |

---

## Caveats

1. **Lava:** Ashlands lava uses `GetBiome` and `IsLava` for gameplay. We are not patching `GetBiome`, so lava detection stays correct; only visual paths (`GetBiomeColor`, `FindBiomeClutter`) are changed.
2. **Footsteps/sounds:** `GetGroundMaterial` uses `GetBiome` – unchanged, so Ashlands footstep sounds and effects remain.
3. **Performance:** Poking heightmaps on enter can be heavy; limit radius or use a short delay.
