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
    private static readonly Dictionary<Heightmap, Heightmap.Biome[]> SavedCornerBiomes = new();
    private static readonly int ShaderAshlandsVariationCol = Shader.PropertyToID("_AshlandsVariationCol");

    private const float NeighborPokeRadius = 80f;

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
    /// After mesh build: restore m_cornerBiomes, restore lava vertex colors, override _AshlandsVariationCol to Meadows green.
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

        var lavaThreshold = Mathf.Max(0.001f, Plugin.LavaTerrainThreshold?.Value ?? 0.01f);
        var transitionRange = Plugin.LavaTransitionRange?.Value ?? 0.15f;
        var lavaRestored = 0;
        for (var i = 0; i < colors.Count; i++)
        {
            var worldPos = __instance.transform.TransformPoint(vertices[i]);
            var mask = __instance.GetVegetationMask(worldPos);
            if (mask <= lavaThreshold) continue;

            var depth = mask - lavaThreshold;
            if (transitionRange > 0.001f && depth < transitionRange)
            {
                var t = Mathf.Clamp01(depth / transitionRange);
                colors[i] = new Color32((byte)(t * 255f), 0, 0, 0);
            }
            else
            {
                colors[i] = new Color32(255, 0, 0, 255);
            }
            lavaRestored++;
        }
        if (lavaRestored > 0)
        {
            mf.mesh.SetColors(colors);
            if (_terrainOverrideLogCount++ < 5)
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Terrain override: lava restore for {lavaRestored} vertices at ({__instance.transform.position.x:F0}, {__instance.transform.position.z:F0})");
        }

        // Override Ashlands material tint to Meadows green to reduce yellow seams
        var mat = AccessTools.Field(typeof(Heightmap), "m_materialInstance").GetValue(__instance) as Material;
        if (mat != null && mat.HasProperty(ShaderAshlandsVariationCol))
            mat.SetColor(ShaderAshlandsVariationCol, new Color(0.4f, 0.5f, 0.3f, 1f));
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
