using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Harmony patches so Meadows grass can place on Ashlands terrain when terrain override is enabled.
/// </summary>
internal static class ClutterSystemPatches
{
    /// <summary>
    /// When clutter system checks biome for grass placement, treat Ashlands as Meadows so Meadows grass can place.
    /// Exclude lava and lava transition areas so grass does not spawn on lava edges.
    /// </summary>
    [HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GetGroundInfo))]
    [HarmonyPostfix]
    private static void GetGroundInfo_Postfix(ref Vector3 point, ref Heightmap hmap, ref Heightmap.Biome biome)
    {
        if (!Plugin.IsTerrainOverrideActive) return;
        if (biome != Heightmap.Biome.AshLands) return;

        var lavaThreshold = Mathf.Max(0.01f, Plugin.LavaEdgeThreshold?.Value ?? 0.05f);
        if (hmap != null && hmap.GetVegetationMask(point) > lavaThreshold)
        {
            biome = Heightmap.Biome.None;
            return;
        }

        biome = Heightmap.Biome.Meadows;
    }
}
