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

        Config.Save();
        Config.SaveOnConfigSet = true;

        Harmony.PatchAll(typeof(Plugin).Assembly);

        Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Weather override: {(EnableWeatherOverride.Value ? "ON" : "OFF")}");
    }

    private void OnDestroy()
    {
        Harmony.UnpatchSelf();
        Config.Save();
    }
}
