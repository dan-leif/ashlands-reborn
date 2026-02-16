using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Harmony patches to override Ashlands environment to Meadows-like (clear sky, no cinder rain, no lava fog).
/// </summary>
[HarmonyPatch]
internal static class EnvManPatches
{
    private static Type? EnvManType => Type.GetType("EnvMan, Assembly-CSharp");

    static MethodBase? TargetMethod() => EnvManType == null ? null : AccessTools.Method(EnvManType, "UpdateEnv");
    private const string TargetEnv = "Clear"; // Meadows-like: clear sky, no cinder rain, no lava fog

    /// <summary>
    /// Check if the local player is currently in the Ashlands biome.
    /// </summary>
    private static bool IsPlayerInAshlands()
    {
        try
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;

            var pos = player.transform.position;
            var worldGen = WorldGenerator.instance;
            if (worldGen == null) return false;

            var biome = worldGen.GetBiome(pos);
            try
            {
                var ashlands = (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), "Ashlands", true);
                return biome == ashlands;
            }
            catch (ArgumentException)
            {
                return false; // Ashlands not in enum (older game version)
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug($"IsPlayerInAshlands error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Force EnvMan to use the target environment (Clear/Meadows-like).
    /// </summary>
    private static void ForceMeadowsEnvironment()
    {
        try
        {
            var envMan = EnvMan.instance;
            if (envMan == null) return;

            // Try common method names: SetEnv, SetEnvironment, etc.
            var setEnv = AccessTools.Method(envMan.GetType(), "SetEnv")
                ?? AccessTools.Method(envMan.GetType(), "SetEnvironment");
            setEnv?.Invoke(envMan, new object[] { TargetEnv });
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug($"ForceMeadowsEnvironment error: {ex.Message}");
        }
    }

    /// <summary>
    /// Postfix on EnvMan.UpdateEnv - after the game updates environment,
    /// override to Meadows if we're in Ashlands and config is enabled.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch("UpdateEnv")]
    private static void UpdateEnv_Postfix()
    {
        if (Plugin.EnableWeatherOverride?.Value != true) return;
        if (!IsPlayerInAshlands()) return;

        ForceMeadowsEnvironment();
    }
}
