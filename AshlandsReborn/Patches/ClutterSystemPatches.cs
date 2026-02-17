using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Harmony patches so Meadows grass can place on Ashlands terrain when terrain override is enabled.
/// </summary>
internal static class ClutterSystemPatches
{
    private const float LavaThreshold = 0.5f;

    /// <summary>
    /// When clutter system checks biome for grass placement, treat Ashlands as Meadows so Meadows grass can place.
    /// Exclude lava areas so grass does not spawn on lava.
    /// </summary>
    [HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GetGroundInfo))]
    [HarmonyPostfix]
    private static void GetGroundInfo_Postfix(ref Vector3 point, ref Heightmap hmap, ref Heightmap.Biome biome)
    {
        if (Plugin.EnableTerrainOverride?.Value != true) return;
        if (biome != Heightmap.Biome.AshLands) return;

        if (hmap != null && hmap.GetVegetationMask(point) > LavaThreshold)
        {
            biome = Heightmap.Biome.None;
            return;
        }

        biome = Heightmap.Biome.Meadows;
    }
}
