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

    /// <summary>
    /// When Ashlands biome is requested for terrain coloring, use Meadows instead.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiomeColor))]
    [HarmonyPrefix]
    private static void GetBiomeColor_Prefix(ref Heightmap.Biome biome)
    {
        if (Plugin.EnableTerrainOverride?.Value != true) return;
        if (biome != Heightmap.Biome.AshLands) return;
        biome = Heightmap.Biome.Meadows;
    }

    /// <summary>
    /// After mesh is built: GetBiomeColor prefix already made all vertices Meadows (0,0,0,0).
    /// Restore Ashlands color (255,0,0,255) for lava vertices so the lava texture displays.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
    [HarmonyPostfix]
    private static void RebuildRenderMesh_Postfix(Heightmap __instance)
    {
        if (Plugin.EnableTerrainOverride?.Value != true) return;
        if (!HasAshLands(__instance)) return;

        var mf = __instance.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null) return;
        var colors = new List<Color32>();
        var vertices = new List<Vector3>();
        mf.mesh.GetColors(colors);
        mf.mesh.GetVertices(vertices);
        if (colors.Count == 0 || colors.Count != vertices.Count) return;

        const float lavaThreshold = 0.5f;
        var lavaRestored = 0;
        for (var i = 0; i < colors.Count; i++)
        {
            var worldPos = __instance.transform.TransformPoint(vertices[i]);
            if (__instance.GetVegetationMask(worldPos) <= lavaThreshold) continue;

            colors[i] = new Color32(255, 0, 0, 255);
            lavaRestored++;
        }
        if (lavaRestored > 0)
        {
            mf.mesh.SetColors(colors);
            if (_terrainOverrideLogCount++ < 5)
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Terrain override: restored Ashlands vertex color for {lavaRestored} lava vertices at ({__instance.transform.position.x:F0}, {__instance.transform.position.z:F0})");
        }
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
    /// When clutter system queries biome for grass placement, return Meadows for Ashlands so Meadows grass spawns.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.FindBiomeClutter))]
    [HarmonyPostfix]
    private static void FindBiomeClutter_Postfix(ref Heightmap.Biome __result)
    {
        if (Plugin.EnableTerrainOverride?.Value != true) return;
        if (__result != Heightmap.Biome.AshLands) return;
        __result = Heightmap.Biome.Meadows;
    }
}
