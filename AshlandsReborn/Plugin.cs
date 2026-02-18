using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

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
            true,
            "Master toggle: turn the entire mod on or off. When off, Ashlands uses default weather and terrain."
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

        Config.Save();
        Config.SaveOnConfigSet = true;

        try
        {
            Harmony.PatchAll(typeof(Plugin).Assembly);

            // Apply terrain patches explicitly in case PatchAll missed them (assembly resolution)
            ApplyTerrainPatches();

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Mod: {(Enabled.Value ? "ON" : "OFF")}, Weather: {(EnableWeatherOverride.Value ? "ON" : "OFF")}, Terrain: {(EnableTerrainOverride.Value ? "ON" : "OFF")}, logging: {(LogAshlandsTransitions.Value ? "ON" : "OFF")}");
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

        // Patch Heightmap.RebuildRenderMesh (private) - replaces vertex colors only, preserves mask (lava)
        var rebuildMesh = heightmapType.GetMethod("RebuildRenderMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (rebuildMesh != null)
        {
            var postfix = typeof(Patches.HeightmapPatches).GetMethod("RebuildRenderMesh_Postfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (postfix != null)
            {
                Harmony.Patch(rebuildMesh, postfix: new HarmonyMethod(postfix));
                logged.Add("RebuildRenderMesh");
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

    private void OnDestroy()
    {
        Harmony.UnpatchSelf();
        Config.Save();
    }
}
