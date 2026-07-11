using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Harmony patches to override Ashlands terrain and grass to Meadows-like (green ground, green grass).
/// </summary>
internal static class HeightmapPatches
{
    private static int _terrainOverrideLogCount;
    private static int _neighborPokeLogCount;
    private static int _lavaCheckLogCount;
    private static readonly Dictionary<Heightmap, Heightmap.Biome[]> SavedCornerBiomes = new();

    private const float NeighborPokeRadius = 80f;

    // LAVACHECK (TERRAIN_LAVA_EDGE_PLAN.md): counts vertices that are functionally lava
    // (mirrors Heightmap.IsLava exactly: non-biome-edge AshLands chunk + raw point-sampled
    // mask >= 0.6) but whose final vertex color is not full AshLands - i.e. lethal ground
    // rendering as something safe-looking. The AshHold invariant makes this impossible by
    // construction for the styled paths; the counters prove it per run. ApplyStyled always
    // counts (its raw grid is free); ApplyLegacy samples per-vertex only while the photo
    // harness arms the check.
    internal static bool LavaCheckArmed;
    internal static long LavaCheckLavaVertices;
    internal static long LavaCheckViolations;

    internal static void ResetLavaCheck()
    {
        LavaCheckLavaVertices = 0;
        LavaCheckViolations = 0;
    }

    /// <summary>IsLava can only ever be true on a non-biome-edge pure-AshLands chunk
    /// (Heightmap.IsLava checks GetBiome + IsBiomeEdge before the mask). Corners are
    /// restored before the recolor passes run, so this is accurate there.</summary>
    private static bool ChunkCanBeLava(Heightmap hmap)
    {
        if (hmap.IsBiomeEdge()) return false;
        var corners = AccessTools.Field(typeof(Heightmap), "m_cornerBiomes").GetValue(hmap) as Heightmap.Biome[];
        return corners != null && corners[0] == Heightmap.Biome.AshLands;
    }

    /// <summary>
    /// When Ashlands biome is requested for terrain coloring, use Meadows instead.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiomeColor))]
    [HarmonyPrefix]
    private static void GetBiomeColor_Prefix(ref Heightmap.Biome biome)
    {
        if (!Plugin.IsTerrainOverrideActive) return;
        if (biome != Heightmap.Biome.AshLands) return;
        biome = Heightmap.Biome.Meadows;
    }

    /// <summary>
    /// Before mesh build: force all corner biomes to Meadows so no Plains/Ashlands blend is interpolated.
    /// Saves and restores m_cornerBiomes so gameplay (IsLava, GetBiome) is unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
    [HarmonyPrefix]
    private static void RebuildRenderMesh_Prefix(Heightmap __instance)
    {
        if (!Plugin.IsTerrainOverrideActive) return;
        if (!HasAshLands(__instance)) return;

        var corners = AccessTools.Field(typeof(Heightmap), "m_cornerBiomes").GetValue(__instance) as Heightmap.Biome[];
        if (corners == null) return;
        SavedCornerBiomes[__instance] = (Heightmap.Biome[])corners.Clone();
        var meadows = Heightmap.Biome.Meadows;
        AccessTools.Field(typeof(Heightmap), "m_cornerBiomes").SetValue(__instance, new[] { meadows, meadows, meadows, meadows });
    }

    /// <summary>
    /// After mesh build: restore m_cornerBiomes, then re-color the transition per the active
    /// TerrainTransitionStyle (Legacy = original binary lava stamp) and apply the per-style
    /// _AshlandsVariationCol material tint.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
    [HarmonyPostfix]
    private static void RebuildRenderMesh_Postfix(Heightmap __instance)
    {
        if (!Plugin.IsTerrainOverrideActive) return;
        // Prefix sets corners to Meadows, so HasAshLands would be false here. Check if we saved corners instead.
        if (!SavedCornerBiomes.ContainsKey(__instance)) return;

        // Restore m_cornerBiomes so gameplay (IsLava, GetBiome) is correct
        if (SavedCornerBiomes.TryGetValue(__instance, out var saved))
        {
            AccessTools.Field(typeof(Heightmap), "m_cornerBiomes").SetValue(__instance, saved);
            SavedCornerBiomes.Remove(__instance);
        }

        var mf = __instance.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null) return;
        var colors = new List<Color32>();
        var vertices = new List<Vector3>();
        mf.mesh.GetColors(colors);
        mf.mesh.GetVertices(vertices);
        if (colors.Count == 0 || colors.Count != vertices.Count) return;

        var w = (int)Mathf.Sqrt(vertices.Count) - 1;
        if (w < 1) w = 64;
        var side = w + 1;

        var style = TerrainTransition.Current;
        if (style == TransitionStyle.Legacy)
            ApplyLegacy(__instance, mf, colors, vertices, side);
        else
            ApplyStyled(__instance, mf, colors, vertices, side, style);

        // Per-style Ashlands material tint (Legacy keeps the original olive green that
        // reduced the yellow seams; DebugGradient restores vanilla).
        var mat = AccessTools.Field(typeof(Heightmap), "m_materialInstance").GetValue(__instance) as Material;
        if (mat != null)
            TerrainTransition.ApplyVariationCol(mat);
    }

    /// <summary>Original binary lava stamp, byte-identical - the user's revert path.
    /// See TerrainTransition for why it produces the yellow fringe + stair-step contours.</summary>
    private static void ApplyLegacy(Heightmap __instance, MeshFilter mf, List<Color32> colors, List<Vector3> vertices, int side)
    {
        var lavaThreshold = 0.1f;
        var sampleStride = 2;

        float[]? sampled = null;
        int sampleSide = 0;
        if (sampleStride > 1)
        {
            sampleSide = (side - 1) / sampleStride + 1;
            sampled = new float[sampleSide * sampleSide];
            for (var sj = 0; sj < sampleSide; sj++)
            for (var si = 0; si < sampleSide; si++)
            {
                var row = Math.Min(sj * sampleStride, side - 1);
                var col = Math.Min(si * sampleStride, side - 1);
                var idx = row * side + col;
                var worldPos = __instance.transform.TransformPoint(vertices[idx]);
                sampled[sj * sampleSide + si] = __instance.GetVegetationMask(worldPos);
            }
        }

        var lavaRestored = 0;
        var lavaCheck = LavaCheckArmed && ChunkCanBeLava(__instance);
        var lavaVerts = 0;
        var violations = 0;
        for (var i = 0; i < colors.Count; i++)
        {
            float mask;
            if (sampled != null)
            {
                var col = i % side;
                var row = i / side;
                var si = Math.Min(col / sampleStride, sampleSide - 1);
                var sj = Math.Min(row / sampleStride, sampleSide - 1);
                mask = sampled[sj * sampleSide + si];
            }
            else
            {
                var worldPos = __instance.transform.TransformPoint(vertices[i]);
                mask = __instance.GetVegetationMask(worldPos);
            }
            if (mask > lavaThreshold)
            {
                colors[i] = new Color32(255, 0, 0, 255);
                lavaRestored++;
            }

            // Read-only instrumentation; the colors written above stay byte-identical.
            if (lavaCheck)
            {
                var rawMask = __instance.GetVegetationMask(__instance.transform.TransformPoint(vertices[i]));
                if (rawMask >= 0.6f)
                {
                    lavaVerts++;
                    var c = colors[i];
                    if (c.r != 255 || c.g != 0 || c.b != 0 || c.a != 255) violations++;
                }
            }
        }
        if (lavaCheck)
        {
            LavaCheckLavaVertices += lavaVerts;
            LavaCheckViolations += violations;
            if (violations > 0 && _lavaCheckLogCount++ < 20)
                Plugin.Log?.LogWarning($"[AR LavaCheck] chunk ({__instance.transform.position.x:F0}, {__instance.transform.position.z:F0}) style=Legacy violations={violations}");
        }
        if (lavaRestored > 0)
        {
            mf.mesh.SetColors(colors);
            if (_terrainOverrideLogCount++ < 5)
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Terrain override: lava restore for {lavaRestored} vertices at ({__instance.transform.position.x:F0}, {__instance.transform.position.z:F0})");
        }
    }

    /// <summary>Styled transition: blurred lava-mask grid (with cross-chunk padding) fed
    /// through the style's band ramp; the pre-blur raw grid feeds the AshHold invariant
    /// and LAVACHECK. Overwriting every vertex is safe because the prefix already forced
    /// the whole chunk to uniform Meadows output.</summary>
    private static void ApplyStyled(Heightmap __instance, MeshFilter mf, List<Color32> colors, List<Vector3> vertices, int side, TransitionStyle style)
    {
        var radius = Mathf.Clamp(Plugin.TransitionBlurRadius?.Value ?? 2, 0, 4);
        var pad = radius + 1;
        var grid = TerrainTransition.BuildMaskGrid(__instance, vertices, side, pad);
        var gridSide = side + 2 * pad;
        var raw = (float[])grid.Clone();
        TerrainTransition.Smooth(grid, gridSide, radius);

        var canBeLava = ChunkCanBeLava(__instance);
        var lavaVerts = 0;
        var violations = 0;
        for (var i = 0; i < colors.Count; i++)
        {
            var col = Math.Min(i % side, side - 1);
            var row = Math.Min(i / side, side - 1);
            var wp = __instance.transform.TransformPoint(vertices[i]);
            var gi = (row + pad) * gridSide + col + pad;
            var c = TerrainTransition.EvaluateColor(style, raw[gi], grid[gi], wp.x, wp.z, col, row, side);
            colors[i] = c;
            if (canBeLava && raw[gi] >= 0.6f)
            {
                lavaVerts++;
                if (c.r != 255 || c.g != 0 || c.b != 0 || c.a != 255) violations++;
            }
        }
        mf.mesh.SetColors(colors);

        LavaCheckLavaVertices += lavaVerts;
        LavaCheckViolations += violations;
        // DebugGradient paints over lava by design (calibration strips) - exempt from the warning.
        if (violations > 0 && style != TransitionStyle.DebugGradient && _lavaCheckLogCount++ < 20)
            Plugin.Log?.LogWarning($"[AR LavaCheck] chunk ({__instance.transform.position.x:F0}, {__instance.transform.position.z:F0}) style={style} violations={violations}");

        if (_terrainOverrideLogCount++ < 5)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Terrain override ({style}): styled {colors.Count} vertices at ({__instance.transform.position.x:F0}, {__instance.transform.position.z:F0})");
    }

    private static bool HasAshLands(Heightmap hmap)
    {
        var corners = AccessTools.Field(typeof(Heightmap), "m_cornerBiomes").GetValue(hmap) as Heightmap.Biome[];
        if (corners == null) return false;
        var ashLands = Heightmap.Biome.AshLands;
        foreach (var c in corners)
            if ((c & ashLands) != 0) return true;
        return false;
    }

    /// <summary>
    /// When a heightmap loads in Ashlands, Poke it and its neighbors so both sides of zone boundaries get converted.
    /// Addresses timing: chunks that load after the initial Poke would otherwise stay unconverted and show seams.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), "OnEnable")]
    [HarmonyPostfix]
    private static void Heightmap_OnEnable_Postfix(Heightmap __instance)
    {
        if (!Plugin.IsTerrainOverrideActive) return;
        var player = Player.m_localPlayer;
        if (player == null) return;

        var worldGen = WorldGenerator.instance;
        if (worldGen == null) return;

        Heightmap.Biome biome;
        try
        {
            biome = worldGen.GetBiome(player.transform.position);
        }
        catch
        {
            return;
        }

        var ashLands = Heightmap.Biome.AshLands;
        if (biome != ashLands) return;

        var center = __instance.transform.position;
        var list = new List<Heightmap>();
        Heightmap.FindHeightmap(center, NeighborPokeRadius, list);

        var buildDataField = AccessTools.Field(typeof(Heightmap), "m_buildData");
        var poked = 0;
        foreach (var hmap in list)
        {
            if (!HasAshLands(hmap)) continue;
            buildDataField?.SetValue(hmap, null);
            hmap.Poke(delayed: true);
            poked++;
        }
        if (poked > 0)
        {
            if (ClutterSystem.instance != null)
                ClutterSystem.instance.ResetGrass(center, NeighborPokeRadius);
            if (_neighborPokeLogCount++ < 5)
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Neighbor Poke: regenerating {poked} heightmaps at ({center.x:F0}, {center.z:F0})");
        }
    }

    /// <summary>
    /// When clutter system queries biome for grass placement, return Meadows for Ashlands so Meadows grass spawns.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.FindBiomeClutter))]
    [HarmonyPostfix]
    private static void FindBiomeClutter_Postfix(ref Heightmap.Biome __result)
    {
        if (!Plugin.IsTerrainOverrideActive) return;
        if (__result != Heightmap.Biome.AshLands) return;
        __result = Heightmap.Biome.Meadows;
    }
}
