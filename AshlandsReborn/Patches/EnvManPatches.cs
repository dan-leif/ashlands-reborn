using System;
using System.Linq;
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
    private static readonly Type? EnvManType = Type.GetType("EnvMan, Assembly-CSharp") ?? Type.GetType("EnvMan, assembly_valheim");
    private const string TargetEnv = "Clear"; // Meadows-like: clear sky, no cinder rain, no lava fog

    private static bool _wasInAshlands;

    static MethodBase? TargetMethod()
    {
        if (EnvManType == null) return null;
        // EnvMan is a MonoBehaviour - patch Update() which runs every frame
        return AccessTools.Method(EnvManType, "Update");
    }

    /// <summary>
    /// Get current environment name from EnvMan via reflection.
    /// </summary>
    private static string GetCurrentEnvName()
    {
        try
        {
            var envMan = EnvMan.instance;
            if (envMan == null) return "(null)";

            var t = envMan.GetType();
            var f = AccessTools.Field(t, "m_currentEnv") ?? AccessTools.Field(t, "m_env");
            if (f != null) return f.GetValue(envMan)?.ToString() ?? "(null)";

            var p = AccessTools.Property(t, "CurrentEnv") ?? AccessTools.Property(t, "currentEnv");
            if (p != null) return p.GetValue(envMan)?.ToString() ?? "(null)";

            return "(unknown)";
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug("GetCurrentEnvName error: " + ex.Message);
            return "(error)";
        }
    }

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

            // Try common method names: SetEnv, SetEnvironment, SetCurrentEnvironment, etc.
            var envType = envMan.GetType();
            var setEnv = AccessTools.Method(envType, "SetEnv")
                ?? AccessTools.Method(envType, "SetEnvironment")
                ?? AccessTools.Method(envType, "SetCurrentEnvironment");
            if (setEnv != null)
            {
                setEnv.Invoke(envMan, new object[] { TargetEnv });
            }
            else
            {
                Plugin.Log?.LogWarning("EnvMan: could not find SetEnv/SetEnvironment/SetCurrentEnvironment. EnvMan methods: " + string.Join(", ", envType.GetMethods().Select(m => m.Name).Distinct().Take(15)));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug($"ForceMeadowsEnvironment error: {ex.Message}");
        }
    }

    /// <summary>
    /// Postfix on EnvMan.Update - logs Ashlands enter/exit (Step 1), then applies weather override when enabled.
    /// </summary>
    [HarmonyPostfix]
    private static void Update_Postfix()
    {
        var player = Player.m_localPlayer;
        var worldGen = WorldGenerator.instance;
        if (player == null || worldGen == null) return;

        var pos = player.transform.position;
        Heightmap.Biome biome;
        try
        {
            biome = worldGen.GetBiome(pos);
        }
        catch
        {
            return;
        }

        var biomeName = biome.ToString();
        var inAshlands = false;
        try
        {
            var ashlands = (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), "Ashlands", true);
            inAshlands = biome == ashlands;
        }
        catch (ArgumentException) { }

        var envName = GetCurrentEnvName();

        // Step 1: Ashlands transition logging
        if (Plugin.LogAshlandsTransitions?.Value == true && inAshlands != _wasInAshlands)
        {
            var msg = inAshlands ? "Entered" : "Exited";
            Plugin.Log?.LogInfo($"[Ashlands Reborn] {msg} Ashlands | biome: {biomeName} | env: {envName}");
        }

        _wasInAshlands = inAshlands;

        // Weather override
        if (Plugin.EnableWeatherOverride?.Value == true && inAshlands)
        {
            ForceMeadowsEnvironment();
        }
    }
}
