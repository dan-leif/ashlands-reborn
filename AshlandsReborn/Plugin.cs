using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInProcess("valheim.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    public static ConfigEntry<bool> Enabled { get; private set; } = null!;

    public static ConfigEntry<bool> EnableWeatherOverride { get; private set; } = null!;

    public static ConfigEntry<bool> EnableTerrainOverride { get; private set; } = null!;

    public static ConfigEntry<bool> LogAshlandsTransitions { get; private set; } = null!;

    public static ConfigEntry<float> LavaTerrainThreshold { get; private set; } = null!;

    public static ConfigEntry<float> LavaGrassThreshold { get; private set; } = null!;

    public static ConfigEntry<float> TerrainRefreshInterval { get; private set; } = null!;

    public static ConfigEntry<int> TerrainSampleStride { get; private set; } = null!;

    public static ConfigEntry<float> TerrainRegenRadius { get; private set; } = null!;

    public static ConfigEntry<KeyCode> TerrainRefreshKey { get; private set; } = null!;

    public static ConfigEntry<bool> EnableTreeReplacement { get; private set; } = null!;

    public static ConfigEntry<int> AshlandsTreeDensity { get; private set; } = null!;

    public static ConfigEntry<int> BeechOakRatio { get; private set; } = null!;

    public static ConfigEntry<KeyCode> TreeRefreshKey { get; private set; } = null!;

    public static ConfigEntry<bool> EnableDevCommandsAndGodMode { get; private set; } = null!;

    /// <summary>True when the mod is enabled and weather override is on.</summary>
    public static bool IsWeatherOverrideActive => Enabled?.Value == true && EnableWeatherOverride?.Value == true;

    /// <summary>True when the mod is enabled and terrain override is on.</summary>
    public static bool IsTerrainOverrideActive => Enabled?.Value == true && EnableTerrainOverride?.Value == true;

    private static readonly Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Log = Logger;

        Config.SaveOnConfigSet = false;

        Enabled = Config.Bind(
            "General",
            "Enabled",
            false,
            "Master toggle: turn the entire mod on or off. When off, Ashlands uses default weather and terrain. Devcommands and god still run if enabled."
        );

        EnableWeatherOverride = Config.Bind(
            "General",
            "EnableWeatherOverride",
            true,
            "When in Ashlands, override the environment to Meadows-like (clear sky, no cinder rain, no lava fog)."
        );

        EnableTerrainOverride = Config.Bind(
            "General",
            "EnableTerrainOverride",
            true,
            "When in Ashlands, override terrain and grass to Meadows-like (green ground, green grass)."
        );

        LogAshlandsTransitions = Config.Bind(
            "General",
            "LogAshlandsTransitions",
            true,
            "Log biome and environment name when entering or exiting Ashlands (Step 1 diagnostic)."
        );

        EnableTreeReplacement = Config.Bind(
            "Trees",
            "EnableTreeReplacement",
            true,
            "Replace dead Ashlands trees with living Meadows trees (Beech and Oak) while keeping Ashlands resource drops."
        );

        AshlandsTreeDensity = Config.Bind(
            "Trees",
            "AshlandsTreeDensity",
            20,
            "Percent of scorched trees to transform into living Oak/Beech. 0 = no trees visible. 100 = normal Ashlands count."
        );

        BeechOakRatio = Config.Bind(
            "Trees",
            "BeechOakRatio",
            100,
            "Oak vs Beech mix. 0 = all Beech. 100 = all Oak. In-between = mixed."
        );

        TreeRefreshKey = Config.Bind(
            "Trees",
            "TreeRefreshKey",
            KeyCode.F8,
            "Key to re-apply tree config to currently loaded trees without teleporting."
        );

        EnableDevCommandsAndGodMode = Config.Bind(
            "General",
            "EnableDevCommandsAndGodMode",
            true,
            "When loading a world, run devcommands and god for easier testing. Default true."
        );

        LavaTerrainThreshold = Config.Bind(
            "Terrain",
            "LavaTerrainThreshold",
            0.1f,
            "Points with vegetation mask above this are treated as lava for terrain vertex color (shows lava texture). Lower = wider lava area. Default 0.1."
        );

        LavaGrassThreshold = Config.Bind(
            "Terrain",
            "LavaGrassThreshold",
            0.15f,
            "Points with vegetation mask above this are excluded from grass placement (no grass on lava edges). Lower = wider exclusion zone. Default 0.15."
        );

        TerrainRefreshInterval = Config.Bind(
            "Terrain",
            "TerrainRefreshInterval",
            0f,
            "Seconds between terrain refreshes while in Ashlands. 0 = disable (no periodic refresh, less stutter). 60 = refresh every minute to catch new terrain as you move."
        );

        TerrainSampleStride = Config.Bind(
            "Terrain",
            "TerrainSampleStride",
            2,
            "Sample every Nth vertex for lava detection. 1 = all (quality, slow). 2 = half (default). 4 = quarter (fast)."
        );

        TerrainRegenRadius = Config.Bind(
            "Terrain",
            "TerrainRegenRadius",
            128f,
            "Radius in meters to regenerate when entering Ashlands. Lower = less stutter on enter."
        );

        TerrainRefreshKey = Config.Bind(
            "Terrain",
            "TerrainRefreshKey",
            KeyCode.F7,
            "Key to re-apply terrain with current TerrainSampleStride and TerrainRegenRadius."
        );

        // Migrate LavaEdgeThreshold to LavaTerrainThreshold and LavaGrassThreshold
        try
        {
            var defOld = new ConfigDefinition("Terrain", "LavaEdgeThreshold");
            if (Config.ContainsKey(defOld))
            {
                var val = Config[defOld].BoxedValue;
                if (val is float f)
                {
                    LavaTerrainThreshold.Value = f;
                    LavaGrassThreshold.Value = f;
                }
                Config.Remove(defOld);
            }
        }
        catch
        {
            // Non-fatal
        }

        // Remove obsolete config keys
        try
        {
            var def1 = new ConfigDefinition("Terrain", "AshlandsTextureSlices");
            var def2 = new ConfigDefinition("Terrain", "SliceProbeIndex");
            var def3 = new ConfigDefinition("Terrain", "LavaTransitionRange");
            var def4 = new ConfigDefinition("Terrain", "LavaAlphaOffset");
            var def5 = new ConfigDefinition("Terrain", "MeadowsBaseRed");
            var def6 = new ConfigDefinition("Terrain", "MeadowsBaseAlpha");
            var def7 = new ConfigDefinition("Terrain", "EnableBoundaryOverlay");
            var def8 = new ConfigDefinition("Terrain", "OverlayWidth");
            if (Config.ContainsKey(def1)) { Config.Remove(def1); }
            if (Config.ContainsKey(def2)) { Config.Remove(def2); }
            if (Config.ContainsKey(def3)) { Config.Remove(def3); }
            if (Config.ContainsKey(def4)) { Config.Remove(def4); }
            if (Config.ContainsKey(def5)) { Config.Remove(def5); }
            if (Config.ContainsKey(def6)) { Config.Remove(def6); }
            if (Config.ContainsKey(def7)) { Config.Remove(def7); }
            if (Config.ContainsKey(def8)) { Config.Remove(def8); }
        }
        catch
        {
            // Non-fatal; user can delete config manually if slice options persist
        }

        Config.Save();
        Config.SaveOnConfigSet = true;

        try
        {
            Harmony.PatchAll(typeof(Plugin).Assembly);

            // Apply patches explicitly in case PatchAll missed them (assembly resolution)
            ApplyTerrainPatches();
            ApplyTreePatches();

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Mod: {(Enabled.Value ? "ON" : "OFF")}, Weather: {(EnableWeatherOverride.Value ? "ON" : "OFF")}, Terrain: {(EnableTerrainOverride.Value ? "ON" : "OFF")}, Trees: {(EnableTreeReplacement.Value ? "ON" : "OFF")}");
        }
        catch (Exception ex)
        {
            Log.LogError("Failed to apply Harmony patches: " + ex.Message);
        }
    }

    private void ApplyTerrainPatches()
    {
        var logged = new System.Collections.Generic.List<string>();

        // Resolve types from loaded assemblies (game may load assembly_valheim differently)
        var asmSharp = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name?.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) == true);
        var asmValheim = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name?.Equals("assembly_valheim", StringComparison.OrdinalIgnoreCase) == true);

        var heightmapType = asmValheim?.GetType("Heightmap") ?? asmSharp?.GetType("Heightmap");
        if (heightmapType == null)
        {
            Log.LogWarning("[Ashlands Reborn] Terrain: Heightmap type not found in game assemblies");
            return;
        }

        // Patch Heightmap.GetBiomeColor (public static)
        var biomeEnum = heightmapType.GetNestedType("Biome", System.Reflection.BindingFlags.Public) ?? typeof(Heightmap.Biome);
        var getBiomeColor = heightmapType.GetMethod("GetBiomeColor", new[] { biomeEnum });
        if (getBiomeColor != null)
        {
            var prefix = typeof(Patches.HeightmapPatches).GetMethod("GetBiomeColor_Prefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (prefix != null)
            {
                Harmony.Patch(getBiomeColor, prefix: new HarmonyMethod(prefix));
                logged.Add("GetBiomeColor");
            }
        }

        // Patch Heightmap.RebuildRenderMesh (private) - corner override, vertex colors
        var rebuildMesh = heightmapType.GetMethod("RebuildRenderMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (rebuildMesh != null)
        {
            var prefix = typeof(Patches.HeightmapPatches).GetMethod("RebuildRenderMesh_Prefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var postfix = typeof(Patches.HeightmapPatches).GetMethod("RebuildRenderMesh_Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (prefix != null && postfix != null)
            {
                Harmony.Patch(rebuildMesh, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                logged.Add("RebuildRenderMesh");
            }
        }

        // Patch Heightmap.OnEnable - Poke this heightmap and neighbors when loading in Ashlands
        var onEnable = heightmapType.GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (onEnable != null)
        {
            var onEnablePostfix = typeof(Patches.HeightmapPatches).GetMethod("Heightmap_OnEnable_Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (onEnablePostfix != null)
            {
                Harmony.Patch(onEnable, postfix: new HarmonyMethod(onEnablePostfix));
                logged.Add("Heightmap.OnEnable");
            }
        }

        // Patch Heightmap.FindBiomeClutter - use Meadows for grass type selection in Ashlands
        var findBiomeClutter = heightmapType.GetMethod("FindBiomeClutter", new[] { typeof(UnityEngine.Vector3) });
        if (findBiomeClutter != null)
        {
            var findClutterPostfix = typeof(Patches.HeightmapPatches).GetMethod("FindBiomeClutter_Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (findClutterPostfix != null)
            {
                Harmony.Patch(findBiomeClutter, postfix: new HarmonyMethod(findClutterPostfix));
                logged.Add("FindBiomeClutter");
            }
        }

        // Patch ClutterSystem.GetGroundInfo - treat Ashlands as Meadows for grass placement
        var clutterSystemType = asmValheim?.GetType("ClutterSystem") ?? asmSharp?.GetType("ClutterSystem");
        if (clutterSystemType != null)
        {
            var getGroundInfo = clutterSystemType.GetMethod("GetGroundInfo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (getGroundInfo != null)
            {
                var groundInfoPostfix = typeof(Patches.ClutterSystemPatches).GetMethod("GetGroundInfo_Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (groundInfoPostfix != null)
                {
                    Harmony.Patch(getGroundInfo, postfix: new HarmonyMethod(groundInfoPostfix));
                    logged.Add("ClutterSystem.GetGroundInfo");
                }
            }
        }

        if (logged.Count > 0)
            Log.LogInfo($"[Ashlands Reborn] Terrain patches applied: {string.Join(", ", logged)} (Heightmap from {heightmapType.Assembly.GetName().Name})");
    }

    private void ApplyTreePatches()
    {
        var asmSharp = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name?.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) == true);
        var asmValheim = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name?.Equals("assembly_valheim", StringComparison.OrdinalIgnoreCase) == true);

        var treeBaseType = asmValheim?.GetType("TreeBase") ?? asmSharp?.GetType("TreeBase");
        if (treeBaseType == null)
        {
            Log.LogWarning("[Ashlands Reborn] Trees: TreeBase type not found");
            return;
        }

        var awake = treeBaseType.GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (awake != null)
        {
            var postfix = typeof(Patches.TreePatches).GetMethod("TreeBase_Awake_Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (postfix != null)
            {
                Harmony.Patch(awake, postfix: new HarmonyMethod(postfix));
                Log.LogInfo("[Ashlands Reborn] Tree patch applied: TreeBase.Awake");
            }
        }
        else
        {
            Log.LogWarning("[Ashlands Reborn] Trees: TreeBase.Awake not found - tree replacement won't work");
        }
    }

    private static bool _devCommandsRunThisSession;
    private static float _lastTreeRefreshTime;
    private static float _lastTerrainRefreshTime;

    private void Update()
    {
        var inWorld = Player.m_localPlayer != null;
        if (inWorld)
        {
            if (Input.GetKeyDown(TerrainRefreshKey?.Value ?? KeyCode.F7) && Time.time - _lastTerrainRefreshTime >= 1f)
            {
                _lastTerrainRefreshTime = Time.time;
                Patches.EnvManPatches.ForceTerrainRefresh();
            }
            if ((Plugin.EnableTreeReplacement?.Value ?? false) && Input.GetKeyDown(TreeRefreshKey?.Value ?? KeyCode.F8) && Time.time - _lastTreeRefreshTime >= 1f)
            {
                _lastTreeRefreshTime = Time.time;
                Patches.TreePatches.RefreshTrees();
                Log.LogInfo("[Ashlands Reborn] Tree refresh triggered");
            }
        }

        if (!EnableDevCommandsAndGodMode.Value) return;

        if (!inWorld)
        {
            _devCommandsRunThisSession = false;
            return;
        }
        if (_devCommandsRunThisSession) return;

        _devCommandsRunThisSession = true;
        if (Console.instance != null)
        {
            Console.instance.TryRunCommand("devcommands");
            Invoke(nameof(RunGodCommand), 1f);
            Log.LogInfo("[Ashlands Reborn] Ran devcommands, god in 1s");
        }
    }

    private void RunGodCommand()
    {
        if (Console.instance != null && Player.m_localPlayer != null)
        {
            Console.instance.TryRunCommand("god");
            Log.LogInfo("[Ashlands Reborn] Ran god");
        }
    }

    private void OnDestroy()
    {
        Harmony.UnpatchSelf();
        Config.Save();
    }
}
