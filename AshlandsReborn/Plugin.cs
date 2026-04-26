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

    // --- General ---
    public static ConfigEntry<bool> MasterSwitch { get; private set; } = null!;
    public static ConfigEntry<KeyCode> MasterSwitchKey { get; private set; } = null!;
    public static ConfigEntry<bool> EnableDevCommandsAndGodMode { get; private set; } = null!;

    // --- Weather ---
    public static ConfigEntry<bool> EnableWeatherOverride { get; private set; } = null!;

    // --- Terrain ---
    public static ConfigEntry<bool> EnableTerrainOverride { get; private set; } = null!;
    public static ConfigEntry<float> LavaGrassThreshold { get; private set; } = null!;
    public static ConfigEntry<float> LavaTerrainThreshold { get; private set; } = null!;
    public static ConfigEntry<float> TerrainRefreshInterval { get; private set; } = null!;
    public static ConfigEntry<KeyCode> TerrainRefreshKey { get; private set; } = null!;
    public static ConfigEntry<float> TerrainRegenRadius { get; private set; } = null!;
    public static ConfigEntry<int> TerrainSampleStride { get; private set; } = null!;

    // --- Trees ---
    public static ConfigEntry<bool> EnableTreeReplacement { get; private set; } = null!;
    public static ConfigEntry<int> AshlandsTreeDensity { get; private set; } = null!;
    public static ConfigEntry<int> BeechOakRatio { get; private set; } = null!;
    public static ConfigEntry<KeyCode> TreeRefreshKey { get; private set; } = null!;

    // --- Creatures ---
    public static ConfigEntry<string> EnableValkyrieSwap { get; private set; } = null!;
    public static ConfigEntry<KeyCode> ValkyrieRefreshKey { get; private set; } = null!;

    // --- Charred Warrior ---
    public static ConfigEntry<bool> EnableCharredWarriorSwap { get; private set; } = null!;
    public static ConfigEntry<string> CharredWarriorHelmetName { get; private set; } = null!;
    public static ConfigEntry<string> CharredWarriorChestName { get; private set; } = null!;
    public static ConfigEntry<string> CharredWarriorLegsName { get; private set; } = null!;
    public static ConfigEntry<string> CharredWarriorShoulderName { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorKromScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorChestScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorLegsScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorCapeScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorShoulderRotation { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetYOffset { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetYaw { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetZOffset { get; private set; } = null!;
    public static ConfigEntry<KeyCode> CharredWarriorRefreshKey { get; private set; } = null!;
    public static ConfigEntry<KeyCode> DataDumpKey { get; private set; } = null!;
    public static ConfigEntry<bool> EnableBodySwap { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapColorR { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapColorG { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapColorB { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapEmissionR { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapEmissionG { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapEmissionB { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapYOffset { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapScale { get; private set; } = null!;
    public static ConfigEntry<bool> BodySwapHideHead { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapHeadCutoffY { get; private set; } = null!;
    public static ConfigEntry<bool> TrimChestArms { get; private set; } = null!;
    public static ConfigEntry<bool> ShowVanillaChest { get; private set; } = null!;
    public static ConfigEntry<bool> ShowVanillaShoulders { get; private set; } = null!;
    public static ConfigEntry<bool> ShowVanillaBracers { get; private set; } = null!;

    // --- Dev Automation ---
    public static ConfigEntry<bool> DevAutoLoad { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadCharacter { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadWorld { get; private set; } = null!;

    public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
    public static bool IsTerrainOverrideActive => MasterSwitch?.Value == true && EnableTerrainOverride?.Value == true;

    private static readonly Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Log = Logger;

        Config.SaveOnConfigSet = false;

        // --- General ---
        MasterSwitch = Config.Bind(
            "General",
            "MasterSwitch",
            true,
            "Master toggle for all mod features except DevCommandsAndGodMode."
        );

        MasterSwitchKey = Config.Bind(
            "General",
            "MasterSwitchKey",
            KeyCode.Backslash,
            "Hotkey to toggle MasterSwitch and immediately revert or apply all visual changes."
        );

        EnableDevCommandsAndGodMode = Config.Bind(
            "General",
            "EnableDevCommandsAndGodMode",
            true,
            "When loading a world, run devcommands and god for easier testing."
        );

        // --- Weather ---
        EnableWeatherOverride = Config.Bind(
            "Weather",
            "EnableWeatherOverride",
            true,
            "When in Ashlands, override the environment to Meadows-like (clear sky, no cinder rain, no lava fog)."
        );

        // --- Terrain ---
        EnableTerrainOverride = Config.Bind(
            "Terrain",
            "EnableTerrainOverride",
            true,
            "When in Ashlands, override terrain and grass to Meadows-like (green ground, green grass)."
        );

        LavaGrassThreshold = Config.Bind(
            "Terrain",
            "LavaGrassThreshold",
            0.11f,
            "Points with vegetation mask above this are excluded from grass placement (no grass on lava edges). Lower = wider exclusion zone. Default 0.15."
        );

        LavaTerrainThreshold = Config.Bind(
            "Terrain",
            "LavaTerrainThreshold",
            0.1f,
            "Points with vegetation mask above this are treated as lava for terrain vertex color (shows lava texture). Lower = wider lava area. Default 0.1."
        );

        TerrainRefreshInterval = Config.Bind(
            "Terrain",
            "TerrainRefreshInterval",
            0f,
            "Seconds between terrain refreshes while in Ashlands. 0 = disable (no periodic refresh, less stutter). 60 = refresh every minute."
        );

        TerrainRefreshKey = Config.Bind(
            "Terrain",
            "TerrainRefreshKey",
            KeyCode.F7,
            "Key to re-apply terrain with current TerrainSampleStride and TerrainRegenRadius."
        );

        TerrainRegenRadius = Config.Bind(
            "Terrain",
            "TerrainRegenRadius",
            128f,
            "Radius in meters to regenerate when entering Ashlands. Lower = less stutter on enter."
        );

        TerrainSampleStride = Config.Bind(
            "Terrain",
            "TerrainSampleStride",
            2,
            "Sample every Nth vertex for lava detection. 1 = all (quality, slow). 2 = half (default). 4 = quarter (fast)."
        );

        // --- Trees ---
        EnableTreeReplacement = Config.Bind(
            "Trees",
            "EnableTreeReplacement",
            true,
            "Replace dead Ashlands trees with living Meadows trees (Beech and Oak) while keeping Ashlands resource drops."
        );

        AshlandsTreeDensity = Config.Bind(
            "Trees",
            "AshlandsTreeDensity",
            50,
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

        // --- Creatures ---
        EnableValkyrieSwap = Config.Bind(
            "Creatures",
            "EnableValkyrieSwap",
            "Enabled",
            new ConfigDescription(
                "Enabled = mesh + materials only, keeps Fallen combat animations. UseIntroVisualsAndAnimations = full Valkyrie visual + Animator. Disabled = no swap.",
                new AcceptableValueList<string>("Enabled", "UseIntroVisualsAndAnimations", "Disabled")));

        ValkyrieRefreshKey = Config.Bind(
            "Creatures",
            "ValkyrieRefreshKey",
            KeyCode.F9,
            "Re-apply Valkyrie swap to nearby Fallen Valkyries without teleporting.");

        EnableCharredWarriorSwap = Config.Bind(
            "Creatures",
            "EnableCharredWarriorSwap",
            true,
            "Master toggle for all Charred_Melee visual changes (sword and armor). No behavior change.");

        CharredWarriorHelmetName = Config.Bind(
            "Creatures",
            "CharredWarriorHelmetName",
            "knighthelm",
            new ConfigDescription(
                "The helmet to swap onto Charred Warriors. HelmetDrake is vanilla, knighthelm requires SouthsilArmor mod.",
                new AcceptableValueList<string>("HelmetDrake", "knighthelm")));

        CharredWarriorChestName = Config.Bind(
            "Creatures",
            "CharredWarriorChestName",
            "knightchest",
            "The chest armor to swap onto Charred Warriors. Requires SouthsilArmor mod for 'knightchest'. Try 'ArmorIronChest' to test with vanilla armor. Leave empty to disable.");

        CharredWarriorLegsName = Config.Bind(
            "Creatures",
            "CharredWarriorLegsName",
            "knightlegs",
            "The legs armor to swap onto Charred Warriors. Requires SouthsilArmor mod for 'knightlegs'. Leave empty to disable.");

        CharredWarriorShoulderName = Config.Bind(
            "Creatures",
            "CharredWarriorShoulderName",
            "",
            "The cape/shoulder to swap onto Charred Warriors. Requires SouthsilArmor mod for 'ss_storrcape'. Leave empty to disable.");

        CharredWarriorKromScale = Config.Bind(
            "Creatures",
            "CharredWarriorKromScale",
            1.16f,
            new ConfigDescription(
                "Scale factor for Krom sword when swapped onto Charred Warriors. 1.0 = vanilla size. 1.16 = 16% larger (matches original sword). 1.18 = 18% larger.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorChestScale = Config.Bind(
            "Creatures",
            "CharredWarriorChestScale",
            1.3f,
            new ConfigDescription(
                "Scale factor for chest armor on Charred Warriors. 1.0 = player size. Adjusts bind poses so the skinned mesh renders larger/smaller relative to the skeleton.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorLegsScale = Config.Bind(
            "Creatures",
            "CharredWarriorLegsScale",
            1.0f,
            new ConfigDescription(
                "Scale factor for leg armor on Charred Warriors. 1.0 = player size.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorCapeScale = Config.Bind(
            "Creatures",
            "CharredWarriorCapeScale",
            1.0f,
            new ConfigDescription(
                "Scale factor for cape/shoulder on Charred Warriors. 1.0 = player size.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorShoulderRotation = Config.Bind(
            "Creatures",
            "CharredWarriorShoulderRotation",
            0f,
            new ConfigDescription(
                "Z-axis rotation in degrees for shoulder bone adjustment. 0 = no adjustment (bind-pose replacement handles orientation). Try 180 to flip if pauldrons appear upside-down.",
                new AcceptableValueRange<float>(-360f, 360f)));

        CharredWarriorHelmetScale = Config.Bind(
            "Creatures",
            "CharredWarriorHelmetScale",
            1.1f,
            new ConfigDescription(
                "Scale factor for Drake Helmet when swapped onto Charred Warriors. 1.0 = vanilla size. 1.05 = 5% larger (slightly better fit).",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorHelmetYOffset = Config.Bind(
            "Creatures",
            "CharredWarriorHelmetYOffset",
            0.05f,
            new ConfigDescription(
                "Vertical height offset for Drake Helmet on Charred Warriors. Positive = move up. Adjust so the helmet sits flush on the skull.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        CharredWarriorHelmetYaw = Config.Bind(
            "Creatures",
            "CharredWarriorHelmetYaw",
            270f,
            new ConfigDescription(
                "Y-axis rotation for Drake Helmet on Charred Warriors. 0 = default HelmetDrake orientation. -90 = facing forward.",
                new AcceptableValueRange<float>(-360f, 360f)));

        CharredWarriorHelmetZOffset = Config.Bind(
            "Creatures",
            "CharredWarriorHelmetZOffset",
            0.05f,
            new ConfigDescription(
                "Forward/back offset for Drake Helmet on Charred Warriors in world space. Positive = forward (toward face). Adjust to prevent skull clipping through front.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));


        CharredWarriorRefreshKey = Config.Bind(
            "Creatures",
            "CharredWarriorRefreshKey",
            KeyCode.F10,
            "Re-apply Charred Warrior sword and armor swap to nearby instances without teleporting.");

        DataDumpKey = Config.Bind(
            "Creatures",
            "DataDumpKey",
            KeyCode.F11,
            "Dump player body mesh + charred sinew positioning data to BepInEx/plugins/.");

        EnableBodySwap = Config.Bind(
            "Creatures",
            "EnableBodySwap",
            true,
            "Adds a player body mesh underneath the Charred Warrior armor to provide volumetric deforming limbs.");

        BodySwapColorR = Config.Bind(
            "Creatures",
            "BodySwapColorR",
            0.15f,
            new ConfigDescription(
                "Body swap material color — Red channel (0–1).",
                new AcceptableValueRange<float>(0f, 1f)));

        BodySwapColorG = Config.Bind(
            "Creatures",
            "BodySwapColorG",
            0.1f,
            new ConfigDescription(
                "Body swap material color — Green channel (0–1).",
                new AcceptableValueRange<float>(0f, 1f)));

        BodySwapColorB = Config.Bind(
            "Creatures",
            "BodySwapColorB",
            0.05f,
            new ConfigDescription(
                "Body swap material color — Blue channel (0–1).",
                new AcceptableValueRange<float>(0f, 1f)));

        BodySwapEmissionR = Config.Bind(
            "Creatures",
            "BodySwapEmissionR",
            0.8f,
            new ConfigDescription(
                "Body swap eye/glow emission — Red channel (0–1).",
                new AcceptableValueRange<float>(0f, 1f)));

        BodySwapEmissionG = Config.Bind(
            "Creatures",
            "BodySwapEmissionG",
            0.2f,
            new ConfigDescription(
                "Body swap eye/glow emission — Green channel (0–1).",
                new AcceptableValueRange<float>(0f, 1f)));

        BodySwapEmissionB = Config.Bind(
            "Creatures",
            "BodySwapEmissionB",
            0.0f,
            new ConfigDescription(
                "Body swap eye/glow emission — Blue channel (0–1).",
                new AcceptableValueRange<float>(0f, 1f)));

        BodySwapYOffset = Config.Bind(
            "Creatures",
            "BodySwapYOffset",
            0.0f,
            new ConfigDescription(
                "Vertical offset for the body swap mesh.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        BodySwapScale = Config.Bind(
            "Creatures",
            "BodySwapScale",
            1.0f,
            new ConfigDescription(
                "Uniform scale multiplier for the body swap mesh.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        BodySwapHideHead = Config.Bind(
            "Creatures",
            "BodySwapHideHead",
            true,
            "Hide the player head in the body swap layer (head shows through the helmet visor otherwise).");

        BodySwapHeadCutoffY = Config.Bind(
            "Creatures",
            "BodySwapHeadCutoffY",
            0.0f,
            new ConfigDescription(
                "Vertical Y offset of the head-hide bone wrapper in the body swap layer. " +
                "Increase to hide more (push cutoff down into neck); decrease to expose more neck.",
                new AcceptableValueRange<float>(-0.2f, 0.2f)));

        TrimChestArms = Config.Bind(
            "Creatures",
            "TrimChestArms",
            true,
            "Remove arm/hand triangles from the chest armor mesh, leaving only the torso plate.");

        ShowVanillaChest = Config.Bind(
            "Creatures",
            "ShowVanillaChest",
            false,
            "Also show the vanilla chest piece alongside the custom one (for comparison).");

        ShowVanillaShoulders = Config.Bind(
            "Creatures",
            "ShowVanillaShoulders",
            false,
            "Also show the vanilla shoulder piece alongside the custom one (for comparison).");

        ShowVanillaBracers = Config.Bind(
            "Creatures",
            "ShowVanillaBracers",
            true,
            "Also show the vanilla utility/bracer piece alongside the custom one (for comparison).");

        DevAutoLoad = Config.Bind(
            "Dev Automation",
            "DevAutoLoad",
            false,
            "Automatically navigate menus and load into a world on game start. Set character/world names below.");

        DevAutoLoadCharacter = Config.Bind(
            "Dev Automation",
            "DevAutoLoadCharacter",
            "Dove",
            "Character name to auto-select when DevAutoLoad is true.");

        DevAutoLoadWorld = Config.Bind(
            "Dev Automation",
            "DevAutoLoadWorld",
            "Reborn",
            "World name to auto-select when DevAutoLoad is true.");

        // Migrate renamed/moved config keys
        try
        {
            var defOldEnabled = new ConfigDefinition("General", "Enabled");
            if (Config.ContainsKey(defOldEnabled))
            {
                var raw = Config[defOldEnabled].BoxedValue?.ToString()?.Trim();
                if (bool.TryParse(raw, out var b))
                    MasterSwitch.Value = b;
                Config.Remove(defOldEnabled);
            }

            var defOldWeather = new ConfigDefinition("General", "EnableWeatherOverride");
            if (Config.ContainsKey(defOldWeather))
            {
                var raw = Config[defOldWeather].BoxedValue?.ToString()?.Trim();
                if (bool.TryParse(raw, out var b))
                    EnableWeatherOverride.Value = b;
                Config.Remove(defOldWeather);
            }

            var defOldTerrain = new ConfigDefinition("General", "EnableTerrainOverride");
            if (Config.ContainsKey(defOldTerrain))
            {
                var raw = Config[defOldTerrain].BoxedValue?.ToString()?.Trim();
                if (bool.TryParse(raw, out var b))
                    EnableTerrainOverride.Value = b;
                Config.Remove(defOldTerrain);
            }

            var defLog = new ConfigDefinition("General", "LogAshlandsTransitions");
            if (Config.ContainsKey(defLog))
                Config.Remove(defLog);

            var valkVal = EnableValkyrieSwap.Value;
            if (valkVal == "UseIntroVisualsOnly")
                EnableValkyrieSwap.Value = "Enabled";
            else if (valkVal == "Disable")
                EnableValkyrieSwap.Value = "Disabled";
        }
        catch
        {
            // Non-fatal
        }

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
            var defValkyrieOld = new ConfigDefinition("Creatures", "EnableValkyrieVisualSwap");
            if (Config.ContainsKey(defValkyrieOld))
            {
                var oldVal = Config[defValkyrieOld].BoxedValue;
                if (oldVal is bool b && b)
                    EnableValkyrieSwap.Value = "Enabled";
                Config.Remove(defValkyrieOld);
            }

            foreach (var name in new[] { "AshlandsTextureSlices", "SliceProbeIndex", "LavaTransitionRange",
                "LavaAlphaOffset", "MeadowsBaseRed", "MeadowsBaseAlpha", "EnableBoundaryOverlay", "OverlayWidth" })
            {
                var def = new ConfigDefinition("Terrain", name);
                if (Config.ContainsKey(def))
                    Config.Remove(def);
            }
        }
        catch
        {
            // Non-fatal
        }

        Config.Save();
        Config.SaveOnConfigSet = true;

        try
        {
            Harmony.PatchAll(typeof(Plugin).Assembly);

            // Apply patches explicitly in case PatchAll missed them (assembly resolution)
            ApplyTerrainPatches();
            ApplyTreePatches();

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Mod: {(MasterSwitch.Value ? "ON" : "OFF")}, Weather: {(EnableWeatherOverride.Value ? "ON" : "OFF")}, Terrain: {(EnableTerrainOverride.Value ? "ON" : "OFF")}, Trees: {(EnableTreeReplacement.Value ? "ON" : "OFF")}, Valkyrie: {EnableValkyrieSwap.Value}, CharredSwap: {(EnableCharredWarriorSwap.Value ? "ON" : "OFF")}");
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
    private static float _lastMasterSwitchToggleTime;
    private static float _lastTreeRefreshTime;
    private static float _lastTerrainRefreshTime;
    private static float _lastValkyrieRefreshTime;
    private static float _lastCharredRefreshTime;
    private static float _lastDataDumpTime;

    private void Update()
    {
        Patches.DevAutoLoadPatches.Tick();

        var inWorld = Player.m_localPlayer != null;
        if (inWorld)
        {
            if (Input.GetKeyDown(MasterSwitchKey?.Value ?? KeyCode.F6) && Time.time - _lastMasterSwitchToggleTime >= 1f)
            {
                _lastMasterSwitchToggleTime = Time.time;
                MasterSwitch.Value = !MasterSwitch.Value;
                if (MasterSwitch.Value)
                {
                    Patches.EnvManPatches.ForceTerrainRefresh(force: true);
                    Patches.TreePatches.RefreshTrees();
                    Patches.ValkyriePatches.RefreshValkyries();
                    Patches.CharredWarriorPatches.RefreshCharredWarriors();
                    Log.LogInfo("[Ashlands Reborn] Master switch ON - all overrides applied");
                }
                else
                {
                    Patches.EnvManPatches.ClearForceEnvironment();
                    Patches.EnvManPatches.ForceTerrainRefresh(force: true);
                    Patches.TreePatches.RevertAllTrees();
                    Patches.ValkyriePatches.RevertAllValkyries();
                    Patches.CharredWarriorPatches.RevertAllCharredWarriors();
                    Log.LogInfo("[Ashlands Reborn] Master switch OFF - all overrides reverted");
                }
            }
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
            if (Input.GetKeyDown(ValkyrieRefreshKey?.Value ?? KeyCode.F9) && Time.time - _lastValkyrieRefreshTime >= 1f)
            {
                _lastValkyrieRefreshTime = Time.time;
                Patches.ValkyriePatches.RefreshValkyries();
                Log.LogInfo("[Ashlands Reborn] Valkyrie refresh triggered");
            }
            if (Input.GetKeyDown(CharredWarriorRefreshKey?.Value ?? KeyCode.F10) && Time.time - _lastCharredRefreshTime >= 1f)
            {
                _lastCharredRefreshTime = Time.time;
                // Dump BEFORE refresh — _lastChestSMR is still valid
                Patches.CharredWarriorPatches.DumpChestMatricesNow();
                Patches.CharredWarriorPatches.RefreshCharredWarriors();
                Log.LogInfo("[Ashlands Reborn] Charred Warrior matrix dump + refresh triggered");
            }
            if (Input.GetKeyDown(DataDumpKey?.Value ?? KeyCode.F11) && Time.time - _lastDataDumpTime >= 1f)
            {
                _lastDataDumpTime = Time.time;
                Patches.CharredWarriorPatches.DumpPlayerAndSinewData();
                Log.LogInfo("[Ashlands Reborn] Player body + sinew data dump triggered");
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
