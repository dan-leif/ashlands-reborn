using System;
using System.Collections.Generic;
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
    private static bool _wasTerrainOverrideEnabled;
    private static int _terrainRegenRetryFrames;
    private static float _lastTerrainRegenTime;

    private static readonly FieldInfo? FDebugTimeOfDay = EnvManType?.GetField("m_debugTimeOfDay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? FDebugTime = EnvManType?.GetField("m_debugTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? FSmoothDayFraction = EnvManType?.GetField("m_smoothDayFraction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? FSkipTime = EnvManType?.GetField("m_skipTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? FSkipToTime = EnvManType?.GetField("m_skipToTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static bool _forceNoonApplied;
    private static bool _forceNoonLogged;

    private static void ApplyForceNoon()
    {
        try
        {
            var envMan = EnvMan.instance;
            if (envMan == null) return;
            FDebugTimeOfDay?.SetValue(envMan, true);
            FDebugTime?.SetValue(envMan, 0.5f); // 0 = midnight, 0.5 = noon
            FSmoothDayFraction?.SetValue(envMan, 0.5f);
            if (!_forceNoonLogged)
            {
                Plugin.Log?.LogInfo($"[Ashlands Reborn] ForceNoon: ON (FDebugTimeOfDay={FDebugTimeOfDay != null}, FDebugTime={FDebugTime != null}, FSmoothDayFraction={FSmoothDayFraction != null})");
                _forceNoonLogged = true;
            }
            _forceNoonApplied = true;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning("ApplyForceNoon error: " + ex.Message);
        }
    }

    private static void ClearForceNoon()
    {
        if (!_forceNoonApplied) return;
        try
        {
            var envMan = EnvMan.instance;
            if (envMan == null) return;
            FDebugTimeOfDay?.SetValue(envMan, false);
            Plugin.Log?.LogInfo("[Ashlands Reborn] ForceNoon: OFF");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug("ClearForceNoon error: " + ex.Message);
        }
        _forceNoonApplied = false;
        _forceNoonLogged = false;
    }

    static MethodBase? TargetMethod()
    {
        if (EnvManType == null) return null;
        // EnvMan is a MonoBehaviour - patch Update() which runs every frame
        return AccessTools.Method(EnvManType, "Update");
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
    /// Uses SetForceEnvironment() - same API as EnvZone and the env console command.
    /// </summary>
    private static void ForceMeadowsEnvironment()
    {
        try
        {
            var envMan = EnvMan.instance;
            if (envMan == null) return;

            envMan.SetForceEnvironment(TargetEnv);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug("ForceMeadowsEnvironment error: " + ex.Message);
        }
    }

    /// <summary>
    /// Manually trigger terrain regen with current config (radius, etc). Call from Plugin when TerrainRefreshKey pressed.
    /// </summary>
    internal static void ForceTerrainRefresh(bool force = false)
    {
        var player = Player.m_localPlayer;
        var worldGen = WorldGenerator.instance;
        if (player == null || worldGen == null) return;

        var pos = player.transform.position;
        Heightmap.Biome biome;
        try { biome = worldGen.GetBiome(pos); }
        catch { return; }

        try
        {
            var ashlands = (Heightmap.Biome)System.Enum.Parse(typeof(Heightmap.Biome), "Ashlands", true);
            if (biome != ashlands) return;
        }
        catch (ArgumentException) { return; }

        if (!force && !Plugin.IsTerrainOverrideActive) return;

        var list = new List<Heightmap>();
        var radius = Mathf.Max(32f, Plugin.TerrainRegenRadius?.Value ?? 128f);
        Heightmap.FindHeightmap(pos, radius, list);
        if (list.Count == 0) return;

        var buildDataField = AccessTools.Field(typeof(Heightmap), "m_buildData");
        foreach (var hmap in list)
        {
            buildDataField?.SetValue(hmap, null);
            hmap.Poke(delayed: true);
        }
        if (ClutterSystem.instance != null)
            ClutterSystem.instance.ResetGrass(pos, radius);

        Plugin.Log?.LogInfo($"[Ashlands Reborn] Terrain refresh: {list.Count} heightmaps, radius {radius:F0}m");
    }

    /// <summary>
    /// Clear the force override when leaving Ashlands so the game uses the real biome env again.
    /// </summary>
    internal static void ClearForceEnvironment()
    {
        try
        {
            var envMan = EnvMan.instance;
            if (envMan == null) return;

            envMan.SetForceEnvironment("");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogDebug("ClearForceEnvironment error: " + ex.Message);
        }
    }

    /// <summary>
    /// Postfix on EnvMan.Update - applies weather and terrain overrides when enabled.
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

        var inAshlands = false;
        try
        {
            var ashlands = (Heightmap.Biome)Enum.Parse(typeof(Heightmap.Biome), "Ashlands", true);
            inAshlands = biome == ashlands;
        }
        catch (ArgumentException) { }

        // Terrain override: regenerate when entering Ashlands with override on, or when override is toggled on while in Ashlands
        var terrainOverrideOn = Plugin.IsTerrainOverrideActive;
        var overrideJustTurnedOn = terrainOverrideOn && !_wasTerrainOverrideEnabled;
        var justEnteredAshlands = inAshlands && !_wasInAshlands;
        var time = Time.time;
        var timeSinceLastRegen = time - _lastTerrainRegenTime;
        var interval = Plugin.TerrainRefreshInterval?.Value ?? 0f;
        var periodicRefresh = interval > 0 && terrainOverrideOn && inAshlands && timeSinceLastRegen >= interval;
        var shouldRegenTerrain = terrainOverrideOn && inAshlands && (justEnteredAshlands || overrideJustTurnedOn || periodicRefresh);
        _wasTerrainOverrideEnabled = terrainOverrideOn;

        if (shouldRegenTerrain || (terrainOverrideOn && inAshlands && _terrainRegenRetryFrames > 0))
        {
            try
            {
                var list = new List<Heightmap>();
                var radius = Mathf.Max(32f, Plugin.TerrainRegenRadius?.Value ?? 128f);
                Heightmap.FindHeightmap(pos, radius, list);
                if (shouldRegenTerrain && list.Count == 0)
                {
                    // Terrain may not be loaded yet (e.g. after teleport) - retry for a few frames
                    _terrainRegenRetryFrames = 15;
                }
                else if (list.Count > 0)
                {
                    _terrainRegenRetryFrames = 0;
                    var buildDataField = AccessTools.Field(typeof(Heightmap), "m_buildData");
                    foreach (var hmap in list)
                    {
                        buildDataField?.SetValue(hmap, null);
                        hmap.Poke(delayed: true);
                    }
                    if (ClutterSystem.instance != null)
                        ClutterSystem.instance.ResetGrass(pos, radius);
                    _lastTerrainRegenTime = time;
                    Plugin.Log?.LogInfo($"[Ashlands Reborn] Terrain override applied: regenerating {list.Count} heightmaps, resetting grass");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug("Terrain Poke error: " + ex.Message);
                _terrainRegenRetryFrames = 0;
            }

            if (_terrainRegenRetryFrames > 0)
                _terrainRegenRetryFrames--;
        }
        else if (!inAshlands || !terrainOverrideOn)
        {
            _terrainRegenRetryFrames = 0;
        }

        // Weather override: SetForceEnvironment when in Ashlands, clear when exiting
        if (Plugin.IsWeatherOverrideActive)
        {
            if (inAshlands)
                ForceMeadowsEnvironment();
            else if (_wasInAshlands)
                ClearForceEnvironment();
        }
        else
        {
            // Override disabled or mod off - clear any force we set so Ashlands weather returns
            ClearForceEnvironment();
        }

        _wasInAshlands = inAshlands;

        if (Plugin.IsForceNoonActive)
            ApplyForceNoon();
        else
            ClearForceNoon();
    }
}
