using System;
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

    public static ConfigEntry<bool> EnableWeatherOverride { get; private set; } = null!;

    public static ConfigEntry<bool> LogAshlandsTransitions { get; private set; } = null!;

    private static readonly Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Log = Logger;

        Config.SaveOnConfigSet = false;

        EnableWeatherOverride = Config.Bind(
            "General",
            "EnableWeatherOverride",
            true,
            "When in Ashlands, override the environment to Meadows-like (clear sky, no cinder rain, no lava fog)."
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
            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Weather override: {(EnableWeatherOverride.Value ? "ON" : "OFF")}, biome logging: {(LogAshlandsTransitions.Value ? "ON" : "OFF")}");
        }
        catch (Exception ex)
        {
            Log.LogError("Failed to apply Harmony patches: " + ex.Message);
        }
    }

    private void OnDestroy()
    {
        Harmony.UnpatchSelf();
        Config.Save();
    }
}
