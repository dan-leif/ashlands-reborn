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
    internal static Plugin Instance { get; private set; } = null!;

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

    // --- Warrior General ---
    public static ConfigEntry<bool> EnableWarriorSwap { get; private set; } = null!;
    public static ConfigEntry<KeyCode> WarriorRefreshKey { get; private set; } = null!;
    public static ConfigEntry<float> WarriorKromScale { get; private set; } = null!;

    // --- Warrior Body ---
    public static ConfigEntry<bool> EnableWarriorBodySwap { get; private set; } = null!;
    public static ConfigEntry<float> WarriorBodySwapScale { get; private set; } = null!;
    public static ConfigEntry<float> WarriorBodySwapThickness { get; private set; } = null!;
    public static ConfigEntry<string> WarriorBodySwapTextureSubmesh { get; private set; } = null!;
    public static ConfigEntry<bool> WarriorBodySwapHideHead { get; private set; } = null!;
    public static ConfigEntry<float> WarriorBodySwapHeadCutoffY { get; private set; } = null!;
    public static ConfigEntry<bool> WarriorChestGlow { get; private set; } = null!;
    public static ConfigEntry<string> WarriorEyeGlowColor { get; private set; } = null!;
    public static ConfigEntry<float> WarriorEyeGlowIntensity { get; private set; } = null!;
    public static ConfigEntry<float> WarriorEyeGlowOffsetX { get; private set; } = null!;
    public static ConfigEntry<float> WarriorEyeGlowOffsetY { get; private set; } = null!;
    public static ConfigEntry<float> WarriorEyeGlowOffsetZ { get; private set; } = null!;

    // --- Warrior Player Armor ---
    public static ConfigEntry<bool> EnableWarriorPlayerArmor { get; private set; } = null!;
    public static ConfigEntry<string> WarriorChestName { get; private set; } = null!;
    public static ConfigEntry<float> WarriorChestScale { get; private set; } = null!;
    public static ConfigEntry<bool> WarriorChestCollapseArmBones { get; private set; } = null!;
    public static ConfigEntry<bool> WarriorChestCollapseForeArmBones { get; private set; } = null!;
    public static ConfigEntry<string> WarriorChestSubmeshesHidden { get; private set; } = null!;
    public static ConfigEntry<bool> WarriorChestTrimArms { get; private set; } = null!;
    public static ConfigEntry<string> WarriorLegsName { get; private set; } = null!;
    public static ConfigEntry<float> WarriorLegsScale { get; private set; } = null!;
    public static ConfigEntry<string> WarriorHelmetName { get; private set; } = null!;
    public static ConfigEntry<float> WarriorHelmetScale { get; private set; } = null!;
    public static ConfigEntry<float> WarriorHelmetYaw { get; private set; } = null!;
    public static ConfigEntry<float> WarriorHelmetYOffset { get; private set; } = null!;
    public static ConfigEntry<float> WarriorHelmetZOffset { get; private set; } = null!;

    // --- Warrior Vanilla Armor ---
    public static ConfigEntry<bool> ShowWarriorVanillaHelmet { get; private set; } = null!;
    public static ConfigEntry<bool> ShowWarriorVanillaBodyArmor { get; private set; } = null!;
    public static ConfigEntry<string> WarriorVanillaVisibleSubmeshes { get; private set; } = null!;
    public static ConfigEntry<string> WarriorVanillaCollapseBones { get; private set; } = null!;
    public static ConfigEntry<string> WarriorVanillaScaleBones { get; private set; } = null!;
    public static ConfigEntry<KeyCode> WarriorVanillaDumpSubmeshesKey { get; private set; } = null!;

    // --- Fable Warrior ---
    public static ConfigEntry<bool> EnableFableWarrior { get; private set; } = null!;
    public static ConfigEntry<bool> ClonePlayerToWarrior { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorScale { get; private set; } = null!;
    public static ConfigEntry<float> FableHelmetScale { get; private set; } = null!;
    public static ConfigEntry<float> FableHelmetYOffset { get; private set; } = null!;
    public static ConfigEntry<float> FableKromGripRotX { get; private set; } = null!;
    public static ConfigEntry<float> FableKromGripRotY { get; private set; } = null!;
    public static ConfigEntry<float> FableKromGripRotZ { get; private set; } = null!;
    public static ConfigEntry<float> FableKromGripOffX { get; private set; } = null!;
    public static ConfigEntry<float> FableKromGripOffY { get; private set; } = null!;
    public static ConfigEntry<float> FableKromGripOffZ { get; private set; } = null!;

    // --- Dev Automation ---
    public static ConfigEntry<bool> DevAutoLoad { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadCharacter { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadWorld { get; private set; } = null!;
    public static ConfigEntry<KeyCode> PhotoModeKey { get; private set; } = null!;
    public static ConfigEntry<bool> PhotoModeAuto { get; private set; } = null!;
    public static ConfigEntry<bool> PhotoModeM4Test { get; private set; } = null!;
    public static ConfigEntry<float> PhotoModeSpawnDistance { get; private set; } = null!;
    public static ConfigEntry<string> PhotoModeIslandPos { get; private set; } = null!;

    public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
    public static bool IsForceNoonActive => MasterSwitch?.Value == true && ForceNoon?.Value == true;
    public static bool IsTerrainOverrideActive => MasterSwitch?.Value == true && EnableTerrainOverride?.Value == true;
    public static bool IsFablePuppetActive =>
        MasterSwitch?.Value == true && EnableFableWarrior?.Value == true && ClonePlayerToWarrior?.Value == true;

    private static readonly Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Instance = this;
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

        // --- Warrior General ---
        EnableWarriorSwap = Config.Bind(
            "Warrior General",
            "EnableWarriorSwap",
            true,
            "Master toggle for all Charred_Melee visual changes (body, sword, and armor). When off, all warrior modifications are disabled.");

        WarriorRefreshKey = Config.Bind(
            "Warrior General",
            "WarriorRefreshKey",
            KeyCode.F10,
            "Re-apply warrior sword and armor swap to nearby instances without teleporting.");

        WarriorKromScale = Config.Bind(
            "Warrior General",
            "WarriorKromScale",
            1.16f,
            new ConfigDescription(
                "Scale factor for Krom sword when swapped onto Charred Warriors. 1.0 = vanilla size. 1.16 = 16% larger (matches original sword). 1.18 = 18% larger.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        // --- Warrior Body ---
        EnableWarriorBodySwap = Config.Bind(
            "Warrior Body",
            "EnableWarriorBodySwap",
            true,
            "Toggle between default skeleton body and custom player body/eyes as specified in this section.");

        WarriorChestGlow = Config.Bind(
            "Warrior Body",
            "WarriorChestGlow",
            false,
            "Show the vanilla Charred chest ember/glow particle effect on the warrior's chest.");

        WarriorBodySwapScale = Config.Bind(
            "Warrior Body",
            "WarriorBodySwapScale",
            1.0f,
            new ConfigDescription(
                "Uniform scale multiplier for the body swap mesh.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        WarriorBodySwapThickness = Config.Bind(
            "Warrior Body",
            "WarriorBodySwapThickness",
            1.25f,
            new ConfigDescription(
                "Radial thickness of the body swap layer (XZ scale on torso/arms/legs). "
                + "1.0 = original player proportions; >1 = more muscular. Does not affect height.",
                new AcceptableValueRange<float>(0.7f, 2.0f)));

        WarriorBodySwapTextureSubmesh = Config.Bind(
            "Warrior Body",
            "WarriorBodySwapTextureSubmesh",
            "3",
            new ConfigDescription(
                "Pick a chest armor submesh (0–9) whose material is cloned onto the body swap layer. 'Off' uses a plain dark color.",
                new AcceptableValueList<string>("Off", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9")));

        WarriorBodySwapHideHead = Config.Bind(
            "Warrior Body",
            "WarriorBodySwapHideHead",
            true,
            "Hide the player head in the body swap layer (head shows through the helmet visor otherwise).");

        WarriorBodySwapHeadCutoffY = Config.Bind(
            "Warrior Body",
            "WarriorBodySwapHeadCutoffY",
            0.0f,
            new ConfigDescription(
                "Vertical Y offset of the head-hide bone wrapper in the body swap layer. " +
                "Increase to hide more (push cutoff down into neck); decrease to expose more neck.",
                new AcceptableValueRange<float>(-0.2f, 0.2f)));

        WarriorEyeGlowColor = Config.Bind(
            "Warrior Body",
            "WarriorEyeGlowColor",
            "White",
            new ConfigDescription(
                "Emission color preset for the Charred Melee eye glow.",
                new AcceptableValueList<string>("Blue", "Cyan", "Green", "Red", "White", "Orange")));

        WarriorEyeGlowIntensity = Config.Bind(
            "Warrior Body",
            "WarriorEyeGlowIntensity",
            2.0f,
            new ConfigDescription(
                "Brightness multiplier for the eye glow emission (0 = off, 5 = very bright).",
                new AcceptableValueRange<float>(0f, 5f)));

        WarriorEyeGlowOffsetX = Config.Bind(
            "Warrior Body",
            "WarriorEyeGlowOffsetX",
            0.0f,
            new ConfigDescription(
                "Horizontal offset for the eye glow particles. Positive pushes eyes apart, negative pushes them together.",
                new AcceptableValueRange<float>(-2f, 2f)));

        WarriorEyeGlowOffsetY = Config.Bind(
            "Warrior Body",
            "WarriorEyeGlowOffsetY",
            0.0f,
            new ConfigDescription(
                "Vertical offset for the eye glow particles. Positive moves eyes up, negative moves them down.",
                new AcceptableValueRange<float>(-2f, 2f)));

        WarriorEyeGlowOffsetZ = Config.Bind(
            "Warrior Body",
            "WarriorEyeGlowOffsetZ",
            0.04f,
            new ConfigDescription(
                "Forward/back offset for the eye glow particles. Positive moves eyes forward, negative moves them back.",
                new AcceptableValueRange<float>(-2f, 2f)));

        // --- Warrior Player Armor ---
        EnableWarriorPlayerArmor = Config.Bind(
            "Warrior Player Armor",
            "EnableWarriorPlayerArmor",
            true,
            "Enabled: apply player armor pieces and settings to charred warrior as specified in this section. Disabled: do not apply player armor to the warrior.");

        WarriorChestName = Config.Bind(
            "Warrior Player Armor",
            "WarriorChestName",
            "knightchest",
            "The chest armor to swap onto Charred Warriors. Requires SouthsilArmor mod for 'knightchest'. Try 'ArmorIronChest' to test with vanilla armor. Leave empty to disable.");

        WarriorChestScale = Config.Bind(
            "Warrior Player Armor",
            "WarriorChestScale",
            1.3f,
            new ConfigDescription(
                "Scale factor for chest armor on Charred Warriors. 1.0 = player size. Adjusts bind poses so the skinned mesh renders larger/smaller relative to the skeleton.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        WarriorChestCollapseArmBones = Config.Bind(
            "Warrior Player Armor",
            "WarriorChestCollapseArmBones",
            true,
            "Collapse the LeftArm/RightArm bones to zero scale on the chest SMR only. Hides the upper-arm portion of submesh 0 (which can't be hidden via submesh tricks because torso and arm geometry share the submesh) while keeping torso vertices anchored to spine bones intact.");

        WarriorChestCollapseForeArmBones = Config.Bind(
            "Warrior Player Armor",
            "WarriorChestCollapseForeArmBones",
            false,
            "Also collapse LeftForeArm/RightForeArm on the chest SMR. Enable if any submesh-0 geometry runs past the elbow.");

        WarriorChestSubmeshesHidden = Config.Bind(
            "Warrior Player Armor",
            "WarriorChestSubmeshesHidden",
            "5",
            "Comma-separated list of chest submesh indices to hide via invisible material (e.g. \"5\" or \"5,6\"). Empty string disables all extra hiding.");

        WarriorChestTrimArms = Config.Bind(
            "Warrior Player Armor",
            "WarriorChestTrimArms",
            true,
            "Remove arm/hand triangles from the chest armor mesh, leaving only the torso plate.");

        WarriorLegsName = Config.Bind(
            "Warrior Player Armor",
            "WarriorLegsName",
            "knightlegs",
            "The legs armor to swap onto Charred Warriors. Requires SouthsilArmor mod for 'knightlegs'. Leave empty to disable.");

        WarriorLegsScale = Config.Bind(
            "Warrior Player Armor",
            "WarriorLegsScale",
            1.0f,
            new ConfigDescription(
                "Scale factor for leg armor on Charred Warriors. 1.0 = player size.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        WarriorHelmetName = Config.Bind(
            "Warrior Player Armor",
            "WarriorHelmetName",
            "knighthelm",
            "The helmet to swap onto Charred Warriors. HelmetDrake is vanilla, knighthelm requires SouthsilArmor mod. Leave empty to disable.");

        WarriorHelmetScale = Config.Bind(
            "Warrior Player Armor",
            "WarriorHelmetScale",
            1.1f,
            new ConfigDescription(
                "Scale factor for the helmet when swapped onto Charred Warriors. 1.0 = vanilla size.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        WarriorHelmetYaw = Config.Bind(
            "Warrior Player Armor",
            "WarriorHelmetYaw",
            270f,
            new ConfigDescription(
                "Y-axis rotation for the helmet on Charred Warriors. 0 = default HelmetDrake orientation. -90 = facing forward.",
                new AcceptableValueRange<float>(-360f, 360f)));

        WarriorHelmetYOffset = Config.Bind(
            "Warrior Player Armor",
            "WarriorHelmetYOffset",
            0.05f,
            new ConfigDescription(
                "Vertical height offset for the helmet on Charred Warriors. Positive = move up. Adjust so the helmet sits flush on the skull.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        WarriorHelmetZOffset = Config.Bind(
            "Warrior Player Armor",
            "WarriorHelmetZOffset",
            0.05f,
            new ConfigDescription(
                "Forward/back offset for the helmet on Charred Warriors in world space. Positive = forward (toward face). Adjust to prevent skull clipping through front.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        // --- Warrior Vanilla Armor ---
        ShowWarriorVanillaHelmet = Config.Bind(
            "Warrior Vanilla Armor",
            "ShowWarriorVanillaHelmet",
            false,
            "Enabled: apply the vanilla Charred_Helmet to every Charred Warrior. " +
            "Disabled: remove the vanilla Charred_Helmet from every Charred Warrior. " +
            "Only takes effect when MasterSwitch is enabled — otherwise each warrior keeps whatever it rolled at spawn.");

        ShowWarriorVanillaBodyArmor = Config.Bind(
            "Warrior Vanilla Armor",
            "ShowWarriorVanillaBodyArmor",
            false,
            "Enabled: apply all body armor pieces to every Charred Warrior, including those on the legs, hips, chest, shoulders, and arms. " +
            "(All vanilla body armor lives in one mesh: Charred_Breastplate / 'ChestPiece'.) " +
            "Disabled: remove all vanilla body armor pieces from every Charred Warrior. " +
            "Does NOT control the custom player-armor swap — see EnableWarriorPlayerArmor in 'Warrior Player Armor'. " +
            "Only takes effect when MasterSwitch is enabled.");

        WarriorVanillaVisibleSubmeshes = Config.Bind(
            "Warrior Vanilla Armor",
            "WarriorVanillaVisibleSubmeshes",
            "",
            "Comma-separated submesh indices of the vanilla Charred_Breastplate ('ChestPiece') mesh to KEEP visible. " +
            "All other submeshes are hidden via an invisible material. Empty = all visible (no masking). " +
            "Example: '3,4' to show only submeshes 3 and 4. " +
            "Press WarriorVanillaDumpSubmeshesKey (F12) in-game to log the submesh layout to BepInEx/LogOutput.log. " +
            "Only effective when ShowWarriorVanillaBodyArmor=true and MasterSwitch=true.");

        WarriorVanillaCollapseBones = Config.Bind(
            "Warrior Vanilla Armor",
            "WarriorVanillaCollapseBones",
            "",
            "Comma-separated bone names to collapse (zero-scale) on the vanilla Charred_Breastplate, hiding any geometry weighted to them. " +
            "Examples: 'LeftShoulder,RightShoulder' to remove pauldrons; 'LeftUpLeg,RightUpLeg' to remove leg armor; 'Spine,Spine1,Spine2' to remove torso plate. " +
            "WARNING: avoid 'RightArm' and 'RightHandThumb1' — the vanilla mesh has mis-weighted vertices on those bones (101 verts and 119 verts respectively) and collapsing them distorts the right bracer. " +
            "Only effective when ShowWarriorVanillaBodyArmor=true and MasterSwitch=true.");

        WarriorVanillaScaleBones = Config.Bind(
            "Warrior Vanilla Armor",
            "WarriorVanillaScaleBones",
            "",
            "Comma-separated 'BoneName:scale' pairs to scale (rather than fully hide) geometry on the vanilla Charred_Breastplate. " +
            "Example: 'LeftShoulder:0.5,RightShoulder:0.5' halves the pauldrons. " +
            "Bones listed in WarriorVanillaCollapseBones take precedence (collapsed to zero) over scale entries for the same bone. " +
            "Only effective when ShowWarriorVanillaBodyArmor=true and MasterSwitch=true.");

        WarriorVanillaDumpSubmeshesKey = Config.Bind(
            "Warrior Vanilla Armor",
            "WarriorVanillaDumpSubmeshesKey",
            KeyCode.F12,
            "Press this key in-game to log a per-submesh dump of the live vanilla Charred_Breastplate (mesh, materials, triangle ranges, dominant bone per submesh). " +
            "Use the dump to decide which indices to put in WarriorVanillaVisibleSubmeshes.");

        // --- Fable Warrior ---
        EnableFableWarrior = Config.Bind(
            "Fable Warrior",
            "EnableFableWarrior",
            true,
            "Master toggle for the Fable Warrior feature. When true, overrides and bypasses ALL legacy " +
            "Charred Warrior modifications (body swap layer, SouthsilArmor attach, vanilla breastplate mods, " +
            "bracers, old sword-swap path) in favor of the settings in this section.");

        ClonePlayerToWarrior = Config.Bind(
            "Fable Warrior",
            "ClonePlayerToWarrior",
            true,
            "Build a player-rig puppet (the local player's body + current armor, scaled up) on every " +
            "Charred_Melee, driven by the warrior's own animation. Requires EnableFableWarrior.");

        FableWarriorScale = Config.Bind(
            "Fable Warrior",
            "FableWarriorScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Warrior puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableHelmetScale = Config.Bind(
            "Fable Warrior",
            "FableHelmetScale",
            1.0f,
            new ConfigDescription(
                "Fine-tune multiplier on the puppet's helmet size. Rigid-attach helmets are first " +
                "normalized to scale with the puppet rig (vanilla attach cancels the rig scale, which " +
                "left player-sized helmets perched on the oversized head); this multiplies on top. " +
                "1.0 = exact player fit at puppet scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableHelmetYOffset = Config.Bind(
            "Fable Warrior",
            "FableHelmetYOffset",
            0.0f,
            new ConfigDescription(
                "Vertical offset (meters, helmet-joint local - scales with the rig) applied to the " +
                "puppet's helmet instance after the scale normalization.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        FableKromGripRotX = Config.Bind(
            "Fable Warrior",
            "FableKromGripRotX",
            12.0f,
            new ConfigDescription(
                "Rotation (degrees) of the puppet's Krom sword around the hand-attach local X axis. " +
                "Tunes the idle sword-on-shoulder rest so the blade lies beside the trapezius instead " +
                "of through it (12 = calibrated M3.1 value). Applied on top of the vanilla attach orientation.",
                new AcceptableValueRange<float>(-180f, 180f)));

        FableKromGripRotY = Config.Bind(
            "Fable Warrior",
            "FableKromGripRotY",
            0.0f,
            new ConfigDescription(
                "Rotation (degrees) of the puppet's Krom sword around the hand-attach local Y axis.",
                new AcceptableValueRange<float>(-180f, 180f)));

        FableKromGripRotZ = Config.Bind(
            "Fable Warrior",
            "FableKromGripRotZ",
            0.0f,
            new ConfigDescription(
                "Rotation (degrees) of the puppet's Krom sword around the hand-attach local Z axis.",
                new AcceptableValueRange<float>(-180f, 180f)));

        FableKromGripOffX = Config.Bind(
            "Fable Warrior",
            "FableKromGripOffX",
            0.0f,
            new ConfigDescription(
                "Position offset (meters, hand-attach local) of the puppet's Krom sword grip, X axis.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        FableKromGripOffY = Config.Bind(
            "Fable Warrior",
            "FableKromGripOffY",
            0.0f,
            new ConfigDescription(
                "Position offset (meters, hand-attach local) of the puppet's Krom sword grip, Y axis.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        FableKromGripOffZ = Config.Bind(
            "Fable Warrior",
            "FableKromGripOffZ",
            0.0f,
            new ConfigDescription(
                "Position offset (meters, hand-attach local) of the puppet's Krom sword grip, Z axis.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

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

        PhotoModeKey = Config.Bind(
            "Dev Automation",
            "PhotoModeKey",
            KeyCode.F6,
            "Hotkey to run the Fable Warrior verification harness: spawns a Charred_Melee near the player, " +
            "orbits the camera around it, captures screenshots, then despawns it. Output goes to " +
            "<plugin dir>\\AR_PhotoMode\\.");

        PhotoModeAuto = Config.Bind(
            "Dev Automation",
            "PhotoModeAuto",
            false,
            "Automatically run the photo-mode verification harness once, ~10s after entering the world.");

        PhotoModeM4Test = Config.Bind(
            "Dev Automation",
            "PhotoModeM4Test",
            false,
            "Run the M4 lifecycle self-test once ~10s after world load: spawns 3 warriors (one 2-star, " +
            "one leveled up post-build), asserts puppet builds, master-switch toggle cleanliness x3, " +
            "F10 rebuild, <=2.5s armor sync, and star scaling, then despawns them and writes " +
            "M4_RESULTS.txt + screenshots to AR_PhotoMode. Suppresses the PhotoModeAuto shoot.");

        PhotoModeSpawnDistance = Config.Bind(
            "Dev Automation",
            "PhotoModeSpawnDistance",
            5.0f,
            new ConfigDescription(
                "Distance in front of the player to spawn the photo-mode Charred_Melee.",
                new AcceptableValueRange<float>(2f, 15f)));

        PhotoModeIslandPos = Config.Bind(
            "Dev Automation",
            "PhotoModeIslandPos",
            "2736,40,2580",
            "Test island 'x,y,z': flat player-built ocean platform with clutter-free backgrounds. " +
            "The photo harness teleports the player here before each session (force-killed dev runs " +
            "don't save the logout point, so the player can regress to world spawn between runs). " +
            "Empty string disables the teleport.");

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

            // Migrate "Charred Warrior" section keys to new section names
            MigrateKey("Charred Warrior", "EnableCharredWarriorSwap", EnableWarriorSwap);
            MigrateKey("Charred Warrior", "CharredWarriorRefreshKey", WarriorRefreshKey);
            MigrateKey("Charred Warrior", "CharredWarriorKromScale", WarriorKromScale);
            MigrateKey("Charred Warrior", "EnableBodySwap", EnableWarriorBodySwap);
            MigrateKey("Charred Warrior", "BodySwapScale", WarriorBodySwapScale);
            MigrateKey("Charred Warrior", "BodySwapThickness", WarriorBodySwapThickness);
            MigrateKey("Charred Warrior", "BodySwapChestTextureSubmesh", WarriorBodySwapTextureSubmesh);
            MigrateKey("Charred Warrior", "BodySwapHideHead", WarriorBodySwapHideHead);
            MigrateKey("Charred Warrior", "BodySwapHeadCutoffY", WarriorBodySwapHeadCutoffY);
            MigrateKey("Charred Warrior", "EyeGlowColor", WarriorEyeGlowColor);
            MigrateKey("Charred Warrior", "EyeGlowIntensity", WarriorEyeGlowIntensity);
            MigrateKey("Charred Warrior", "EyeGlowOffsetX", WarriorEyeGlowOffsetX);
            MigrateKey("Charred Warrior", "EyeGlowOffsetY", WarriorEyeGlowOffsetY);
            MigrateKey("Charred Warrior", "EyeGlowOffsetZ", WarriorEyeGlowOffsetZ);
            MigrateKey("Charred Warrior", "CharredWarriorChestName", WarriorChestName);
            MigrateKey("Charred Warrior", "CharredWarriorChestScale", WarriorChestScale);
            MigrateKey("Charred Warrior", "ChestCollapseArmBones", WarriorChestCollapseArmBones);
            MigrateKey("Charred Warrior", "ChestCollapseForeArmBones", WarriorChestCollapseForeArmBones);
            MigrateKey("Charred Warrior", "ChestSubmeshesHidden", WarriorChestSubmeshesHidden);
            MigrateKey("Charred Warrior", "TrimChestArms", WarriorChestTrimArms);
            MigrateKey("Charred Warrior", "CharredWarriorLegsName", WarriorLegsName);
            MigrateKey("Charred Warrior", "CharredWarriorLegsScale", WarriorLegsScale);
            MigrateKey("Charred Warrior", "CharredWarriorHelmetName", WarriorHelmetName);
            MigrateKey("Charred Warrior", "CharredWarriorHelmetScale", WarriorHelmetScale);
            MigrateKey("Charred Warrior", "CharredWarriorHelmetYaw", WarriorHelmetYaw);
            MigrateKey("Charred Warrior", "CharredWarriorHelmetYOffset", WarriorHelmetYOffset);
            MigrateKey("Charred Warrior", "CharredWarriorHelmetZOffset", WarriorHelmetZOffset);
            // DataDumpKey removed — just clean it up
            var defDataDump = new ConfigDefinition("Charred Warrior", "DataDumpKey");
            if (Config.ContainsKey(defDataDump)) Config.Remove(defDataDump);

            // Removed config keys from the previous "Warrior Vanilla Armor" iteration —
            // delete from saved cfg files so they don't linger in ConfigurationManager.
            foreach (var dead in new[]
                     {
                         "ForceWarriorVanillaArmor", "ForceWarriorVanillaArmorAll",
                         "ShowWarriorVanillaChest", "ShowWarriorVanillaShoulders",
                         "ShowWarriorVanillaBracers", "WarriorVanillaBracersScale",
                     })
            {
                var def = new ConfigDefinition("Warrior Vanilla Armor", dead);
                if (Config.ContainsKey(def)) Config.Remove(def);
            }

            // Removed: the Fable Warrior retarget now always copies Charred bone orientations
            // directly (no rest-pose "source" choice), so this dev knob is obsolete.
            var defRetarget = new ConfigDefinition("Fable Warrior", "FableWarriorRetargetSource");
            if (Config.ContainsKey(defRetarget)) Config.Remove(defRetarget);
        }
        catch
        {
            // Non-fatal
        }

        Config.Save();
        Config.SaveOnConfigSet = true;

        EnableFableWarrior.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        ClonePlayerToWarrior.SettingChanged += (_, _) => OnFableWarriorModeChanged();

        try
        {
            Harmony.PatchAll(typeof(Plugin).Assembly);

            // Apply patches explicitly in case PatchAll missed them (assembly resolution)
            ApplyTerrainPatches();
            ApplyTreePatches();

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Mod: {(MasterSwitch.Value ? "ON" : "OFF")}, Weather: {(EnableWeatherOverride.Value ? "ON" : "OFF")}, Terrain: {(EnableTerrainOverride.Value ? "ON" : "OFF")}, Trees: {(EnableTreeReplacement.Value ? "ON" : "OFF")}, Valkyrie: {EnableValkyrieSwap.Value}, WarriorSwap: {(EnableWarriorSwap.Value ? "ON" : "OFF")}");
        }
        catch (Exception ex)
        {
            Log.LogError("Failed to apply Harmony patches: " + ex.Message);
        }
    }

    private void MigrateKey<T>(string oldSection, string oldKey, ConfigEntry<T> target)
    {
        var def = new ConfigDefinition(oldSection, oldKey);
        if (!Config.ContainsKey(def)) return;
        try
        {
            var raw = Config[def].BoxedValue?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                var converted = (T)System.ComponentModel.TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(raw)!;
                target.Value = converted;
            }
        }
        catch { }
        Config.Remove(def);
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
    private static float _lastBracerScaleUpdateTime;

    private void Update()
    {
        Patches.DevAutoLoadPatches.Tick();
        Patches.PhotoModePatches.Tick();
        Patches.LifecycleTestPatches.Tick();

        var inWorld = Player.m_localPlayer != null;
        if (inWorld)
        {
            if (Input.GetKeyDown(MasterSwitchKey?.Value ?? KeyCode.F6) && Time.time - _lastMasterSwitchToggleTime >= 1f)
            {
                _lastMasterSwitchToggleTime = Time.time;
                ApplyMasterSwitch(!MasterSwitch.Value);
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
            if (Input.GetKeyDown(WarriorRefreshKey?.Value ?? KeyCode.F10) && Time.time - _lastCharredRefreshTime >= 1f)
            {
                _lastCharredRefreshTime = Time.time;
                if (IsFablePuppetActive)
                {
                    Patches.FableWarriorPatches.RefreshAll();
                    Log.LogInfo("[Ashlands Reborn] Fable Warrior refresh triggered");
                }
                else
                {
                    // Dump BEFORE refresh — _lastChestSMR is still valid
                    Patches.CharredWarriorPatches.DumpChestMatricesNow();
                    Patches.CharredWarriorPatches.RefreshCharredWarriors();
                    Log.LogInfo("[Ashlands Reborn] Warrior matrix dump + refresh triggered");
                }
            }
            if (Input.GetKeyDown(WarriorVanillaDumpSubmeshesKey?.Value ?? KeyCode.F12))
            {
                Patches.CharredWarriorPatches.DumpVanillaBreastplateSubmeshes();
            }

            if (Time.time - _lastBracerScaleUpdateTime >= 0.2f)
            {
                _lastBracerScaleUpdateTime = Time.time;
                if (!IsFablePuppetActive)
                {
                    Patches.CharredWarriorPatches.UpdateBracerScales();
                    Patches.CharredWarriorPatches.UpdateChestSubmeshesHidden();
                    Patches.CharredWarriorPatches.UpdateBodySwapThickness();
                    Patches.CharredWarriorPatches.UpdateVanillaBreastplateMods();
                }
                Patches.FableWarriorPatches.PeriodicUpdate();
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

    /// <summary>
    /// The master-switch toggle body, shared by the hotkey handler and the M4 lifecycle
    /// self-test so the test exercises exactly the shipping code path.
    /// </summary>
    internal void ApplyMasterSwitch(bool on)
    {
        MasterSwitch.Value = on;
        if (on)
        {
            Patches.EnvManPatches.ForceTerrainRefresh(force: true);
            Patches.TreePatches.RefreshTrees();
            Patches.ValkyriePatches.RefreshValkyries();
            Patches.CharredWarriorPatches.RefreshCharredWarriors();
            Patches.FableWarriorPatches.RefreshAll();
            Log.LogInfo("[Ashlands Reborn] Master switch ON - all overrides applied");
        }
        else
        {
            Patches.EnvManPatches.ClearForceEnvironment();
            Patches.EnvManPatches.ForceTerrainRefresh(force: true);
            Patches.TreePatches.RevertAllTrees();
            Patches.ValkyriePatches.RevertAllValkyries();
            Patches.CharredWarriorPatches.RevertAllCharredWarriors();
            Patches.FableWarriorPatches.RevertAll();
            Log.LogInfo("[Ashlands Reborn] Master switch OFF - all overrides reverted");
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

    // Mode transitions between the legacy Charred Warrior swap and the Fable Warrior puppet
    // must clean up the mode being left before applying the mode being entered - otherwise
    // stale attachments/instances from one system can linger under the other.
    private void OnFableWarriorModeChanged()
    {
        if (!MasterSwitch.Value || Player.m_localPlayer == null) return;

        if (IsFablePuppetActive)
        {
            Patches.CharredWarriorPatches.RevertAllCharredWarriors();
            Patches.FableWarriorPatches.RefreshAll();
        }
        else
        {
            Patches.FableWarriorPatches.RevertAll();
            Patches.CharredWarriorPatches.RefreshCharredWarriors();
        }
    }

    private void OnDestroy()
    {
        Harmony.UnpatchSelf();
        Config.Save();
    }
}
