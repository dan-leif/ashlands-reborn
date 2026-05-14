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
    public static ConfigEntry<bool> ForceNoon { get; private set; } = null!;

    // --- Terrain ---
    public static ConfigEntry<bool> EnableTerrainOverride { get; private set; } = null!;

    // --- Trees ---
    public static ConfigEntry<bool> EnableTreeReplacement { get; private set; } = null!;
    public static ConfigEntry<int> AshlandsTreeDensity { get; private set; } = null!;
    public static ConfigEntry<KeyCode> TreeRefreshKey { get; private set; } = null!;

    // --- Valkyrie ---
    public static ConfigEntry<string> EnableValkyrieSwap { get; private set; } = null!;
    public static ConfigEntry<KeyCode> ValkyrieRefreshKey { get; private set; } = null!;

    // --- Charred Warrior ---
    public static ConfigEntry<bool> EnableCharredWarriorSwap { get; private set; } = null!;
    public static ConfigEntry<string> CharredWarriorHelmetName { get; private set; } = null!;
    public static ConfigEntry<string> CharredWarriorChestName { get; private set; } = null!;
    public static ConfigEntry<string> CharredWarriorLegsName { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorKromScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorChestScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorLegsScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetScale { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetYOffset { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetYaw { get; private set; } = null!;
    public static ConfigEntry<float> CharredWarriorHelmetZOffset { get; private set; } = null!;
    public static ConfigEntry<KeyCode> CharredWarriorRefreshKey { get; private set; } = null!;
    public static ConfigEntry<KeyCode> DataDumpKey { get; private set; } = null!;
    public static ConfigEntry<bool> EnableBodySwap { get; private set; } = null!;
    public static ConfigEntry<string> BodySwapChestTextureSubmesh { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapScale { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapThickness { get; private set; } = null!;
    public static ConfigEntry<bool> BodySwapHideHead { get; private set; } = null!;
    public static ConfigEntry<float> BodySwapHeadCutoffY { get; private set; } = null!;
    public static ConfigEntry<bool> TrimChestArms { get; private set; } = null!;
    public static ConfigEntry<bool> ChestCollapseArmBones { get; private set; } = null!;
    public static ConfigEntry<bool> ChestCollapseForeArmBones { get; private set; } = null!;
    public static ConfigEntry<string> ChestSubmeshesHidden { get; private set; } = null!;
    public static ConfigEntry<bool> ShowVanillaChest { get; private set; } = null!;
    public static ConfigEntry<bool> ShowVanillaShoulders { get; private set; } = null!;
    public static ConfigEntry<bool> ShowVanillaBracers { get; private set; } = null!;
    public static ConfigEntry<float> BracerScale { get; private set; } = null!;
    public static ConfigEntry<string> EyeGlowColor { get; private set; } = null!;
    public static ConfigEntry<float> EyeGlowIntensity { get; private set; } = null!;
    public static ConfigEntry<float> EyeGlowOffsetX { get; private set; } = null!;
    public static ConfigEntry<float> EyeGlowOffsetY { get; private set; } = null!;
    public static ConfigEntry<float> EyeGlowOffsetZ { get; private set; } = null!;

    // --- Dev Automation ---
    public static ConfigEntry<bool> DevAutoLoad { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadCharacter { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadWorld { get; private set; } = null!;

    public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
    public static bool IsForceNoonActive => MasterSwitch?.Value == true && ForceNoon?.Value == true;
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

        ForceNoon = Config.Bind(
            "Weather",
            "ForceNoon",
            false,
            "Force the time of day to always be noon. Best lighting for development."
        );

        // --- Terrain ---
        EnableTerrainOverride = Config.Bind(
            "Terrain",
            "EnableTerrainOverride",
            true,
            "When in Ashlands, override terrain and grass to Meadows-like (green ground, green grass)."
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

        TreeRefreshKey = Config.Bind(
            "Trees",
            "TreeRefreshKey",
            KeyCode.F8,
            "Key to re-apply tree config to currently loaded trees without teleporting."
        );

        // --- Valkyrie ---
        EnableValkyrieSwap = Config.Bind(
            "Valkyrie",
            "EnableValkyrieSwap",
            "Enabled",
            new ConfigDescription(
                "Enabled = mesh + materials only, keeps Fallen combat animations. UseIntroVisualsAndAnimations = full Valkyrie visual + Animator. Disabled = no swap.",
                new AcceptableValueList<string>("Enabled", "UseIntroVisualsAndAnimations", "Disabled")));

        ValkyrieRefreshKey = Config.Bind(
            "Valkyrie",
            "ValkyrieRefreshKey",
            KeyCode.F9,
            "Re-apply Valkyrie swap to nearby Fallen Valkyries without teleporting.");

        // --- Charred Warrior ---
        EnableCharredWarriorSwap = Config.Bind(
            "Charred Warrior",
            "EnableCharredWarriorSwap",
            true,
            "Master toggle for all Charred_Melee visual changes (sword and armor). No behavior change.");

        CharredWarriorHelmetName = Config.Bind(
            "Charred Warrior",
            "CharredWarriorHelmetName",
            "knighthelm",
            new ConfigDescription(
                "The helmet to swap onto Charred Warriors. HelmetDrake is vanilla, knighthelm requires SouthsilArmor mod.",
                new AcceptableValueList<string>("HelmetDrake", "knighthelm")));

        CharredWarriorChestName = Config.Bind(
            "Charred Warrior",
            "CharredWarriorChestName",
            "knightchest",
            "The chest armor to swap onto Charred Warriors. Requires SouthsilArmor mod for 'knightchest'. Try 'ArmorIronChest' to test with vanilla armor. Leave empty to disable.");

        CharredWarriorLegsName = Config.Bind(
            "Charred Warrior",
            "CharredWarriorLegsName",
            "knightlegs",
            "The legs armor to swap onto Charred Warriors. Requires SouthsilArmor mod for 'knightlegs'. Leave empty to disable.");

        CharredWarriorKromScale = Config.Bind(
            "Charred Warrior",
            "CharredWarriorKromScale",
            1.16f,
            new ConfigDescription(
                "Scale factor for Krom sword when swapped onto Charred Warriors. 1.0 = vanilla size. 1.16 = 16% larger (matches original sword). 1.18 = 18% larger.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorChestScale = Config.Bind(
            "Charred Warrior",
            "CharredWarriorChestScale",
            1.3f,
            new ConfigDescription(
                "Scale factor for chest armor on Charred Warriors. 1.0 = player size. Adjusts bind poses so the skinned mesh renders larger/smaller relative to the skeleton.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorLegsScale = Config.Bind(
            "Charred Warrior",
            "CharredWarriorLegsScale",
            1.0f,
            new ConfigDescription(
                "Scale factor for leg armor on Charred Warriors. 1.0 = player size.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorHelmetScale = Config.Bind(
            "Charred Warrior",
            "CharredWarriorHelmetScale",
            1.1f,
            new ConfigDescription(
                "Scale factor for Drake Helmet when swapped onto Charred Warriors. 1.0 = vanilla size. 1.05 = 5% larger (slightly better fit).",
                new AcceptableValueRange<float>(0.5f, 2f)));

        CharredWarriorHelmetYOffset = Config.Bind(
            "Charred Warrior",
            "CharredWarriorHelmetYOffset",
            0.05f,
            new ConfigDescription(
                "Vertical height offset for Drake Helmet on Charred Warriors. Positive = move up. Adjust so the helmet sits flush on the skull.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        CharredWarriorHelmetYaw = Config.Bind(
            "Charred Warrior",
            "CharredWarriorHelmetYaw",
            270f,
            new ConfigDescription(
                "Y-axis rotation for Drake Helmet on Charred Warriors. 0 = default HelmetDrake orientation. -90 = facing forward.",
                new AcceptableValueRange<float>(-360f, 360f)));

        CharredWarriorHelmetZOffset = Config.Bind(
            "Charred Warrior",
            "CharredWarriorHelmetZOffset",
            0.05f,
            new ConfigDescription(
                "Forward/back offset for Drake Helmet on Charred Warriors in world space. Positive = forward (toward face). Adjust to prevent skull clipping through front.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));


        CharredWarriorRefreshKey = Config.Bind(
            "Charred Warrior",
            "CharredWarriorRefreshKey",
            KeyCode.F10,
            "Re-apply Charred Warrior sword and armor swap to nearby instances without teleporting.");

        DataDumpKey = Config.Bind(
            "Charred Warrior",
            "DataDumpKey",
            KeyCode.F11,
            "Dump player body mesh + charred sinew positioning data to BepInEx/plugins/.");

        EnableBodySwap = Config.Bind(
            "Charred Warrior",
            "EnableBodySwap",
            true,
            "Adds a player body mesh underneath the Charred Warrior armor to provide volumetric deforming limbs.");

        BodySwapChestTextureSubmesh = Config.Bind(
            "Charred Warrior",
            "BodySwapChestTextureSubmesh",
            "3",
            new ConfigDescription(
                "Pick a chest armor submesh (0–9) whose material is cloned onto the body swap layer. 'Off' uses a plain dark color.",
                new AcceptableValueList<string>("Off", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9")));

        EyeGlowColor = Config.Bind(
            "Charred Warrior",
            "EyeGlowColor",
            "White",
            new ConfigDescription(
                "Emission color preset for the Charred Melee eye glow.",
                new AcceptableValueList<string>("Blue", "Cyan", "Green", "Red", "White", "Orange")));

        EyeGlowIntensity = Config.Bind(
            "Charred Warrior",
            "EyeGlowIntensity",
            2.0f,
            new ConfigDescription(
                "Brightness multiplier for the eye glow emission (0 = off, 5 = very bright).",
                new AcceptableValueRange<float>(0f, 5f)));

        EyeGlowOffsetX = Config.Bind(
            "Charred Warrior",
            "EyeGlowOffsetX",
            0.0f,
            new ConfigDescription(
                "Horizontal offset for the eye glow particles. Positive pushes eyes apart, negative pushes them together.",
                new AcceptableValueRange<float>(-2f, 2f)));

        EyeGlowOffsetY = Config.Bind(
            "Charred Warrior",
            "EyeGlowOffsetY",
            0.0f,
            new ConfigDescription(
                "Vertical offset for the eye glow particles. Positive moves eyes up, negative moves them down.",
                new AcceptableValueRange<float>(-2f, 2f)));

        EyeGlowOffsetZ = Config.Bind(
            "Charred Warrior",
            "EyeGlowOffsetZ",
            0.04f,
            new ConfigDescription(
                "Forward/back offset for the eye glow particles. Positive moves eyes forward, negative moves them back.",
                new AcceptableValueRange<float>(-2f, 2f)));

        BodySwapScale = Config.Bind(
            "Charred Warrior",
            "BodySwapScale",
            1.0f,
            new ConfigDescription(
                "Uniform scale multiplier for the body swap mesh.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        BodySwapThickness = Config.Bind(
            "Charred Warrior",
            "BodySwapThickness",
            1.25f,
            new ConfigDescription(
                "Radial thickness of the body swap layer (XZ scale on torso/arms/legs). "
                + "1.0 = original player proportions; >1 = more muscular. Does not affect height.",
                new AcceptableValueRange<float>(0.7f, 2.0f)));

        BodySwapHideHead = Config.Bind(
            "Charred Warrior",
            "BodySwapHideHead",
            true,
            "Hide the player head in the body swap layer (head shows through the helmet visor otherwise).");

        BodySwapHeadCutoffY = Config.Bind(
            "Charred Warrior",
            "BodySwapHeadCutoffY",
            0.0f,
            new ConfigDescription(
                "Vertical Y offset of the head-hide bone wrapper in the body swap layer. " +
                "Increase to hide more (push cutoff down into neck); decrease to expose more neck.",
                new AcceptableValueRange<float>(-0.2f, 0.2f)));

        TrimChestArms = Config.Bind(
            "Charred Warrior",
            "TrimChestArms",
            true,
            "Remove arm/hand triangles from the chest armor mesh, leaving only the torso plate.");

        ChestCollapseArmBones = Config.Bind(
            "Charred Warrior",
            "ChestCollapseArmBones",
            true,
            "Collapse the LeftArm/RightArm bones to zero scale on the chest SMR only. Hides the upper-arm portion of submesh 0 (which can't be hidden via submesh tricks because torso and arm geometry share the submesh) while keeping torso vertices anchored to spine bones intact.");

        ChestCollapseForeArmBones = Config.Bind(
            "Charred Warrior",
            "ChestCollapseForeArmBones",
            false,
            "Also collapse LeftForeArm/RightForeArm on the chest SMR. Enable if any submesh-0 geometry runs past the elbow.");

        ChestSubmeshesHidden = Config.Bind(
            "Charred Warrior",
            "ChestSubmeshesHidden",
            "5",
            "Comma-separated list of chest submesh indices to hide via invisible material (e.g. \"5\" or \"5,6\"). Empty string disables all extra hiding.");

        ShowVanillaChest = Config.Bind(
            "Charred Warrior",
            "ShowVanillaChest",
            false,
            "Also show the vanilla chest piece alongside the custom one (for comparison).");

        ShowVanillaShoulders = Config.Bind(
            "Charred Warrior",
            "ShowVanillaShoulders",
            false,
            "Also show the vanilla shoulder piece alongside the custom one (for comparison).");

        ShowVanillaBracers = Config.Bind(
            "Charred Warrior",
            "ShowVanillaBracers",
            true,
            "Also show the vanilla utility/bracer piece alongside the custom one (for comparison).");

        BracerScale = Config.Bind(
            "Charred Warrior",
            "BracerScale",
            1.0f,
            new ConfigDescription(
                "Uniform scale of the vanilla bracer overlay. 1.0 = default size.",
                new AcceptableValueRange<float>(0.1f, 3.0f)));

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

        // Migrate EnableValkyrieSwap / ValkyrieRefreshKey from old "Creatures" section to "Valkyrie"
        try
        {
            var defValkOld = new ConfigDefinition("Creatures", "EnableValkyrieSwap");
            if (Config.ContainsKey(defValkOld))
            {
                var raw = Config[defValkOld].BoxedValue?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(raw))
                    EnableValkyrieSwap.Value = raw;
                Config.Remove(defValkOld);
            }
            var defValkKeyOld = new ConfigDefinition("Creatures", "ValkyrieRefreshKey");
            if (Config.ContainsKey(defValkKeyOld))
                Config.Remove(defValkKeyOld);
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
                "LavaAlphaOffset", "MeadowsBaseRed", "MeadowsBaseAlpha", "EnableBoundaryOverlay", "OverlayWidth",
                "LavaEdgeThreshold", "LavaGrassThreshold", "LavaTerrainThreshold", "TerrainRefreshInterval",
                "TerrainRefreshKey", "TerrainRegenRadius", "TerrainSampleStride" })
            {
                var def = new ConfigDefinition("Terrain", name);
                if (Config.ContainsKey(def))
                    Config.Remove(def);
            }

            foreach (var name in new[] { "BeechOakRatio" })
            {
                var def = new ConfigDefinition("Trees", name);
                if (Config.ContainsKey(def))
                    Config.Remove(def);
            }

            foreach (var name in new[] {
                "CharredWarriorShoulderName", "CharredWarriorCapeScale", "CharredWarriorShoulderRotation",
                "BodySwapColorPreset", "BodySwapColorR", "BodySwapColorG", "BodySwapColorB",
                "BodySwapEmissionR", "BodySwapEmissionG", "BodySwapEmissionB",
                "BodySwapUseChestTexture", "BodySwapYOffset", "ShowBodySwapChestGlow" })
            {
                var def = new ConfigDefinition("Creatures", name);
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
    private static float _lastValkyrieRefreshTime;
    private static float _lastCharredRefreshTime;
    private static float _lastDataDumpTime;
    private static float _lastBracerScaleUpdateTime;

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

            if (Time.time - _lastBracerScaleUpdateTime >= 0.2f)
            {
                _lastBracerScaleUpdateTime = Time.time;
                Patches.CharredWarriorPatches.UpdateBracerScales();
                Patches.CharredWarriorPatches.UpdateChestSubmeshesHidden();
                Patches.CharredWarriorPatches.UpdateBodySwapThickness();
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
