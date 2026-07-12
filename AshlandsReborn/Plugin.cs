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
    public static ConfigEntry<string> TerrainTransitionStyle { get; private set; } = null!;
    public static ConfigEntry<float> TransitionNoiseScale { get; private set; } = null!;
    public static ConfigEntry<float> TransitionNoiseStrength { get; private set; } = null!;
    public static ConfigEntry<int> TransitionBlurRadius { get; private set; } = null!;
    public static ConfigEntry<float> TransitionAshHold { get; private set; } = null!;
    public static ConfigEntry<float> TransitionFadeWidth { get; private set; } = null!;
    public static ConfigEntry<string> AshBlendSwapSlices { get; private set; } = null!;
    public static ConfigEntry<float> AshBlendBandBrightness { get; private set; } = null!;
    public static ConfigEntry<Color> AshBlendBandTint { get; private set; } = null!;
    public static ConfigEntry<float> AshBlendBandMix { get; private set; } = null!;
    public static ConfigEntry<Color> AshBlendVariationColor { get; private set; } = null!;
    public static ConfigEntry<string> RockBlendSwapSlices { get; private set; } = null!;
    public static ConfigEntry<float> RockBlendBandBrightness { get; private set; } = null!;
    public static ConfigEntry<bool> RockBlendWideBand { get; private set; } = null!;
    public static ConfigEntry<string> LegacySmoothSwapSlices { get; private set; } = null!;
    public static ConfigEntry<bool> LegacySmoothDebugRamp { get; private set; } = null!;
    public static ConfigEntry<bool> TerrainArrayUncompressed { get; private set; } = null!;

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
    public static ConfigEntry<bool> ClonePlayerToArcher { get; private set; } = null!;
    public static ConfigEntry<float> FableArcherScale { get; private set; } = null!;
    public static ConfigEntry<string> FableArcherBow { get; private set; } = null!;
    public static ConfigEntry<float> FableArcherBowScale { get; private set; } = null!;
    public static ConfigEntry<bool> ClonePlayerToTwitcher { get; private set; } = null!;
    public static ConfigEntry<float> FableTwitcherScale { get; private set; } = null!;
    public static ConfigEntry<bool> ClonePlayerToMage { get; private set; } = null!;
    public static ConfigEntry<float> FableMageScale { get; private set; } = null!;
    public static ConfigEntry<string> FableMageStaff { get; private set; } = null!;
    public static ConfigEntry<float> FableMageStaffScale { get; private set; } = null!;

    // --- Fable Bunny ---
    public static ConfigEntry<bool> EnableFableBunny { get; private set; } = null!;
    public static ConfigEntry<string> FableBunnyDonor { get; private set; } = null!;
    public static ConfigEntry<float> FableBunnyHeight { get; private set; } = null!;
    public static ConfigEntry<float> FableBunnyScale { get; private set; } = null!;
    public static ConfigEntry<float> FableBunnyYOffset { get; private set; } = null!;
    public static ConfigEntry<float> FableBunnyPounceAmplitude { get; private set; } = null!;
    public static ConfigEntry<bool> FableBunnyHideRagdoll { get; private set; } = null!;
    public static ConfigEntry<int> FableBunnyStarLook { get; private set; } = null!;
    public static ConfigEntry<float> FableBunnyMoveAnimSpeed { get; private set; } = null!;
    public static ConfigEntry<string> FableBunnyLashStyle { get; private set; } = null!;
    public static ConfigEntry<string> FableBunnyRollStyle { get; private set; } = null!;
    public static ConfigEntry<string> FableBunnyMode { get; private set; } = null!;

    // --- Dev Automation ---
    public static ConfigEntry<bool> DevAutoLoad { get; private set; } = null!;
    public static ConfigEntry<bool> FableBunnyReconDump { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadCharacter { get; private set; } = null!;
    public static ConfigEntry<string> DevAutoLoadWorld { get; private set; } = null!;
    public static ConfigEntry<KeyCode> PhotoModeKey { get; private set; } = null!;
    public static ConfigEntry<bool> PhotoModeAuto { get; private set; } = null!;
    public static ConfigEntry<bool> PhotoModeM4Test { get; private set; } = null!;
    public static ConfigEntry<string> PhotoModePrefabs { get; private set; } = null!;
    public static ConfigEntry<float> PhotoModeSpawnDistance { get; private set; } = null!;
    public static ConfigEntry<string> PhotoModeIslandPos { get; private set; } = null!;
    public static ConfigEntry<bool> TerrainPhotoAuto { get; private set; } = null!;
    public static ConfigEntry<KeyCode> TerrainPhotoKey { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoPos { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoRefPos { get; private set; } = null!;
    public static ConfigEntry<bool> TerrainPhotoRefCapture { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoProbeSpecs { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoProbeAshBrightness { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoProbeLegacySpecs { get; private set; } = null!;

    public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
    public static bool IsForceNoonActive => MasterSwitch?.Value == true && ForceNoon?.Value == true;
    public static bool IsTerrainOverrideActive => MasterSwitch?.Value == true && EnableTerrainOverride?.Value == true;
    // Global gate for the Fable puppet system (matches CharredWarriorPatches.ShouldSwap's
    // legacy bypass). Per-creature enablement lives in FableWarriorPatches' profile table
    // (ClonePlayerToWarrior/Archer/Twitcher/Mage).
    public static bool IsFablePuppetActive =>
        MasterSwitch?.Value == true && EnableFableWarrior?.Value == true;
    // Global gate for the Fable Bunny (Morgen -> donor creature) swap.
    public static bool IsFableBunnyActive =>
        MasterSwitch?.Value == true && EnableFableBunny?.Value == true;

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

        TerrainTransitionStyle = Config.Bind(
            "Terrain",
            "TerrainTransitionStyle",
            "MudBlend",
            new ConfigDescription(
                "How green terrain transitions into ash/lava. Legacy = original binary stamp (exact previous " +
                "behavior). LegacySmooth = green blends straight into ash across one smooth organic fade (no " +
                "mud stage; a soft warm mid-fade tinge replaces Legacy's hard yellow fringe). MudBlend = " +
                "grass -> scorched mud -> ash -> lava multi-band fade with organic noisy " +
                "edges. AshBlend = MudBlend's fade geometry but the mud renders as ash (green fades directly " +
                "into ash - no swamp texture, no yellow). RockBlend = GrassToLava's tight rim rendered as gray " +
                "rock (the dug-rock-strip look). GrassToLava = grass runs almost to the lava rivers with a " +
                "tight mud/ash rim. DebugGradient = dev calibration strips. Changing this live-rebuilds " +
                "nearby terrain.",
                new AcceptableValueList<string>("Legacy", "LegacySmooth", "MudBlend", "AshBlend", "RockBlend", "GrassToLava", "DebugGradient")));

        TransitionNoiseScale = Config.Bind(
            "Terrain",
            "TransitionNoiseScale",
            0.08f,
            new ConfigDescription(
                "World-space frequency of the transition edge-breakup noise (lower = larger wobbles). Ignored by Legacy.",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        TransitionNoiseStrength = Config.Bind(
            "Terrain",
            "TransitionNoiseStrength",
            0.08f,
            new ConfigDescription(
                "How far transition band thresholds are jittered (lava-mask units). 0 = perfectly smooth contour " +
                "lines. Ignored by Legacy.",
                new AcceptableValueRange<float>(0f, 0.3f)));

        TransitionBlurRadius = Config.Bind(
            "Terrain",
            "TransitionBlurRadius",
            2,
            new ConfigDescription(
                "Vertices of box-blur applied to the lava mask before banding (kills the stair-step grid look). " +
                "Ignored by Legacy.",
                new AcceptableValueRange<int>(0, 4)));

        TransitionAshHold = Config.Bind(
            "Terrain",
            "TransitionAshHold",
            0.2f,
            new ConfigDescription(
                "Lava-mask level at/above which terrain always renders as full vanilla ash, evaluated on the RAW " +
                "unblurred mask, so the shader's glowing lava rim/cracks and the deadly lava boundary stay exactly " +
                "vanilla (lava is lethal above 0.6). Lower = more vanilla ash retained around lava. Ignored by " +
                "Legacy and DebugGradient.",
                new AcceptableValueRange<float>(0.05f, 0.55f)));

        TransitionFadeWidth = Config.Bind(
            "Terrain",
            "TransitionFadeWidth",
            0.15f,
            new ConfigDescription(
                "Width (lava-mask units) of the MudBlend green -> mud -> ash fade band below the ash hold. " +
                "Smaller = narrower mud band. Ignored by the other styles.",
                new AcceptableValueRange<float>(0.05f, 0.5f)));

        AshBlendSwapSlices = Config.Bind(
            "Terrain",
            "AshBlendSwapSlices",
            "3:13",
            "AshBlend only (dev tuning): comma-separated dstSlice:srcSlice pairs copied inside the cloned " +
            "terrain diffuse texture array, so the listed layers render as another layer's texture. Default " +
            "3:13 = the swamp/mud overlay (slice 3) renders as the lighter ash-pair texture (slice 13), which " +
            "blends smoothly into the pale full-ash zone at the lava rim; 3:7 (main ash) is darker/dramatic " +
            "but shows the binary ash-hold contour as high-contrast 1m steps. Recon in SHADER_SLICE_MAPPING.md. " +
            "Changing this rebuilds the patched array and refreshes nearby terrain."
        );

        AshBlendBandBrightness = Config.Bind(
            "Terrain",
            "AshBlendBandBrightness",
            1.43f,
            new ConfigDescription(
                "AshBlend only: brightness multiplier applied to the swapped-in band texture (byte space). " +
                "The full-ash zone renders a per-pixel 7+13 blend plus the variation overlay, so any single " +
                "stock slice reads darker than it - grade the band up until it tonally matches the adjacent " +
                "full-ash ground. The default pairs with the default AshBlendBandTint for an effective " +
                "(1.23, 1.30, 1.43) per-channel lift, measured within ~1% mean luminance of the adjacent " +
                "full-ash zone (v4 run-1 calibration). 1.0 with a white tint = untouched slice " +
                "(byte-identical compressed clone path). Live rebuild.",
                new AcceptableValueRange<float>(0.25f, 2.5f)));

        AshBlendBandTint = Config.Bind(
            "Terrain",
            "AshBlendBandTint",
            new Color(0.86f, 0.91f, 1f),
            "AshBlend only: color multiplier applied to the band texture after AshBlendBandBrightness " +
            "(white = no tint). The default cools the band - at matched brightness the raw slice reads " +
            "warmer (R +7%, B -11%) than the blue-gray full-ash zone (v4 run-1 measurement). Components " +
            "must stay <= 1 (Color configs clamp on save); put any overall lift into " +
            "AshBlendBandBrightness instead. Live rebuild.");

        AshBlendBandMix = Config.Bind(
            "Terrain",
            "AshBlendBandMix",
            0.0f,
            new ConfigDescription(
                "AshBlend only: fraction of the grass slice (0) blended into the band texture before grading - " +
                "a 'singed grass' bridge that is by construction tonally between the two sides. 0 = pure ash " +
                "band. Live rebuild.",
                new AcceptableValueRange<float>(0f, 1f)));

        AshBlendVariationColor = Config.Bind(
            "Terrain",
            "AshBlendVariationColor",
            new Color(0.4f, 0.5f, 0.3f, 1f),
            "AshBlend only: _AshlandsVariationCol tint for the slice-15 variation overlay in the full-ash " +
            "zone. The default olive matches every other styled path; a lighter color brightens the ash side " +
            "toward the band tone. Live rebuild.");

        RockBlendSwapSlices = Config.Bind(
            "Terrain",
            "RockBlendSwapSlices",
            "3:5",
            "RockBlend only: comma-separated dstSlice:srcSlice pairs for the cloned terrain diffuse array. " +
            "Default 3:5 = the swamp/mud overlay renders as the base-rock-scales texture, matching the " +
            "pickaxe-dug rock strip the transition imitates. 3:4 / 3:14 = cliff textures, 3:12 = lighter " +
            "rubble. Live rebuild.");

        RockBlendBandBrightness = Config.Bind(
            "Terrain",
            "RockBlendBandBrightness",
            1.0f,
            new ConfigDescription(
                "RockBlend only: brightness multiplier for the rock band texture (see AshBlendBandBrightness). " +
                "1.0 = untouched slice. Live rebuild.",
                new AcceptableValueRange<float>(0.25f, 2.5f)));

        RockBlendWideBand = Config.Bind(
            "Terrain",
            "RockBlendWideBand",
            false,
            "RockBlend only (dev comparison): use MudBlend's wide TransitionFadeWidth band geometry instead " +
            "of GrassToLava's tight rim. Live rebuild.");

        LegacySmoothSwapSlices = Config.Bind(
            "Terrain",
            "LegacySmoothSwapSlices",
            "8:0",
            "LegacySmooth only: comma-separated dstSlice:srcSlice pairs for the cloned terrain diffuse " +
            "array. The mid-fade of the green->ash diagonal carries partial Plains weight, which renders " +
            "the khaki slice 8 as a yellow line floating inside otherwise-green ground; the default '8:0' " +
            "overwrites it with the meadows grass texture that surrounds it. '8:0,3:0' also renders the " +
            "weak swamp-mud overlay (same mid-fade window) as grass - visually near-identical, but slice 3 " +
            "doubles as the hoe-path texture, so paths would render grassy. Empty = vanilla array (the " +
            "yellow line returns). Live rebuild.");

        TerrainArrayUncompressed = Config.Bind(
            "Terrain",
            "TerrainArrayUncompressed",
            false,
            "Dev: force the uncompressed (RGBA32, GPU-decoded) patched-array build even when all tone knobs " +
            "are neutral, to verify the decode round trip is byte-faithful against the compressed clone. " +
            "Non-neutral tone knobs always use the uncompressed path regardless.");

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

        // --- Fable Archer ---
        ClonePlayerToArcher = Config.Bind(
            "Fable Archer",
            "ClonePlayerToArcher",
            true,
            "Build a player-rig puppet on every Charred_Archer, driven by the archer's own animation, " +
            "carrying FableArcherBow in its left hand. Requires EnableFableWarrior (Fable Warrior section).");

        FableArcherScale = Config.Bind(
            "Fable Archer",
            "FableArcherScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Archer puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableArcherBow = Config.Bind(
            "Fable Archer",
            "FableArcherBow",
            "BowAshlands",
            "Item prefab name of the bow the Fable Archer carries (left hand). Must exist in ObjectDB.");

        FableArcherBowScale = Config.Bind(
            "Fable Archer",
            "FableArcherBowScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the archer's bow size, applied after the bow is normalized to scale with " +
                "the puppet rig (1.0 = the bow fits the puppet's hands/draw exactly like it fits the player).",
                new AcceptableValueRange<float>(0.25f, 4.0f)));

        // --- Fable Twitcher ---
        ClonePlayerToTwitcher = Config.Bind(
            "Fable Twitcher",
            "ClonePlayerToTwitcher",
            true,
            "Build a player-rig puppet on every Charred_Twitcher (and Charred_Twitcher_Summoned), driven " +
            "by the twitcher's own animation. Bare hands - no weapon. Requires EnableFableWarrior.");

        FableTwitcherScale = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Twitcher puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        // --- Fable Mage ---
        ClonePlayerToMage = Config.Bind(
            "Fable Mage",
            "ClonePlayerToMage",
            true,
            "Build a player-rig puppet on every Charred_Mage, driven by the mage's own animation, " +
            "carrying FableMageStaff in its right hand. Requires EnableFableWarrior.");

        FableMageScale = Config.Bind(
            "Fable Mage",
            "FableMageScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Mage puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableMageStaff = Config.Bind(
            "Fable Mage",
            "FableMageStaff",
            "StaffFireball",
            "Item prefab name of the staff the Fable Mage carries (right hand). Must exist in ObjectDB.");

        FableMageStaffScale = Config.Bind(
            "Fable Mage",
            "FableMageStaffScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the mage's staff size, applied after the staff is normalized to scale with " +
                "the puppet rig.",
                new AcceptableValueRange<float>(0.25f, 4.0f)));

        // --- Fable Bunny ---
        EnableFableBunny = Config.Bind(
            "Fable Bunny",
            "EnableFableBunny",
            true,
            "Replace the Morgen's bone-horror visuals with a giant, self-animating donor creature " +
            "(FableBunnyDonor, default Hare). Gameplay (attacks, hitboxes, drops, AI) is untouched - " +
            "only the look changes.");

        FableBunnyDonor = Config.Bind(
            "Fable Bunny",
            "FableBunnyDonor",
            "Hare",
            "Creature prefab whose visuals stand in for the Morgen. Quadruped/beast prefabs work best " +
            "(Hare, Lox, Wolf, Deer). Changing this live rebuilds all swapped Morgens.");

        FableBunnyHeight = Config.Bind(
            "Fable Bunny",
            "FableBunnyHeight",
            4.0f,
            new ConfigDescription(
                "Target standing height (meters) for the donor visual, before star scaling. An absolute " +
                "target is used because the Morgen's live render bounds are pose-inflated (recon measured " +
                "9.4m mid-animation) and would produce an absurdly huge donor.",
                new AcceptableValueRange<float>(1.5f, 10f)));

        FableBunnyScale = Config.Bind(
            "Fable Bunny",
            "FableBunnyScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the FableBunnyHeight-derived scale for the primary donor. 1.0 = exact height match.",
                new AcceptableValueRange<float>(0.25f, 3.0f)));

        FableBunnyYOffset = Config.Bind(
            "Fable Bunny",
            "FableBunnyYOffset",
            0.0f,
            new ConfigDescription(
                "Vertical offset (meters) for the donor visual after ground alignment.",
                new AcceptableValueRange<float>(-2f, 2f)));

        FableBunnyPounceAmplitude = Config.Bind(
            "Fable Bunny",
            "FableBunnyPounceAmplitude",
            1.0f,
            new ConfigDescription(
                "Strength of the procedural attack pounce (squash-stretch + lunge pitch). 0 disables it.",
                new AcceptableValueRange<float>(0f, 3f)));

        FableBunnyHideRagdoll = Config.Bind(
            "Fable Bunny",
            "FableBunnyHideRagdoll",
            true,
            "Hide the Morgen ragdoll's renderers on death so the bone corpse never shows. Vanilla death " +
            "effects, despawn timing, and drops are untouched.");

        FableBunnyStarLook = Config.Bind(
            "Fable Bunny",
            "FableBunnyStarLook",
            0,
            new ConfigDescription(
                "Apply the donor creature's star-level tint regardless of the Morgen's real level " +
                "(0 = base look, 1 = 1-star, 2 = 2-star). Purely visual, for comparing looks - real " +
                "star scaling stays independent. Changing this live rebuilds swapped Morgens.",
                new AcceptableValueRange<int>(0, 2)));

        FableBunnyMoveAnimSpeed = Config.Bind(
            "Fable Bunny",
            "FableBunnyMoveAnimSpeed",
            0.55f,
            new ConfigDescription(
                "Animator speed while the donor is moving (1 = authored speed). At giant scale the " +
                "authored run cycle paddles faster than the ground actually covered ('moonwalking'); " +
                "slowing the animation as locomotion speed rises fixes the read. Idle animations always " +
                "play at full speed.",
                new AcceptableValueRange<float>(0.2f, 1.5f)));

        FableBunnyLashStyle = Config.Bind(
            "Fable Bunny",
            "FableBunnyLashStyle",
            "Wisps",
            new ConfigDescription(
                "How the Morgen's arm-swipe attacks read on the donor (they are invisible otherwise - " +
                "the donor has no matching limbs). Wisps = two wisp orbs orbit the donor and lash along " +
                "the hidden hand bones during swipes. EarWhip = the donor's ears whip at the targets " +
                "(procedural bone layer). Both = wisps + ears. Off = v1 body-pounce only.",
                new AcceptableValueList<string>("Wisps", "EarWhip", "Both", "Off")));

        FableBunnyRollStyle = Config.Bind(
            "Fable Bunny",
            "FableBunnyRollStyle",
            "HopHigher",
            new ConfigDescription(
                "Roll-attack presentation. HopHigher = destructive bounding: the donor's real jump " +
                "animation fires on every arc with an airborne bounce and landing squash. CurlAndRoll = " +
                "curl up and tumble (procedural bone layer).",
                new AcceptableValueList<string>("HopHigher", "CurlAndRoll")));

        FableBunnyMode = Config.Bind(
            "Fable Bunny",
            "FableBunnyMode",
            "Bunny",
            new ConfigDescription(
                "Which look replaces the Morgen - THE in-game rotate knob for comparing v2 looks. " +
                "Bunny = giant donor creature (FableBunnyDonor). LightElemental = blinding pulsing " +
                "orb with hand orbs, stark shadows, light beams on bite/slam, and a marble roll. " +
                "LightningElemental = crackling ball lightning whose limbs are jagged bolts " +
                "following the hidden Morgen skeleton. Changing this live rebuilds all swapped Morgens.",
                new AcceptableValueList<string>("Bunny", "LightElemental", "LightningElemental")));

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

        PhotoModePrefabs = Config.Bind(
            "Dev Automation",
            "PhotoModePrefabs",
            "Charred_Melee",
            "Comma-separated creature prefab names the photo harness shoots per session " +
            "(e.g. \"Charred_Melee,Charred_Archer,Charred_Twitcher,Charred_Mage\"). Each gets its own " +
            "spawn/orbit/close-up/animation-pair pass with filenames prefixed by the prefab name.");

        PhotoModeSpawnDistance = Config.Bind(
            "Dev Automation",
            "PhotoModeSpawnDistance",
            5.0f,
            new ConfigDescription(
                "Distance in front of the player to spawn the photo-mode Charred_Melee.",
                new AcceptableValueRange<float>(2f, 15f)));

        FableBunnyReconDump = Config.Bind(
            "Dev Automation",
            "FableBunnyReconDump",
            false,
            "Dev: dump rig/animator/attack recon for Morgen, Hare, and Lox instances to the log " +
            "([AR BunnyRecon] lines), plus live Morgen behavior samples. Spawn the creatures via " +
            "PhotoModePrefabs=\"Morgen,Hare,Lox\".");

        PhotoModeIslandPos = Config.Bind(
            "Dev Automation",
            "PhotoModeIslandPos",
            "2736,40,2580",
            "Test island 'x,y,z': flat player-built ocean platform with clutter-free backgrounds. " +
            "The photo harness teleports the player here before each session (force-killed dev runs " +
            "don't save the logout point, so the player can regress to world spawn between runs). " +
            "Empty string disables the teleport.");

        TerrainPhotoAuto = Config.Bind(
            "Dev Automation",
            "TerrainPhotoAuto",
            false,
            "Run the terrain transition photo harness once ~10s after world load: teleports the player to " +
            "TerrainPhotoPos, cycles every TerrainTransitionStyle with a terrain refresh per style, and " +
            "captures top-down + oblique screenshots into <plugin dir>\\AR_TerrainPhoto\\. Suppressed while " +
            "PhotoModeAuto or PhotoModeM4Test own the session.");

        TerrainPhotoKey = Config.Bind(
            "Dev Automation",
            "TerrainPhotoKey",
            KeyCode.F7,
            "Hotkey to run the terrain transition photo harness on demand.");

        TerrainPhotoPos = Config.Bind(
            "Dev Automation",
            "TerrainPhotoPos",
            "129,30,-9671",
            "'x,y,z' of the terrain transition test spot (a known green/ash/lava boundary with the historical " +
            "grid + yellow-line artifacts). Empty string disables the teleport.");

        TerrainPhotoRefPos = Config.Bind(
            "Dev Automation",
            "TerrainPhotoRefPos",
            "149,30,-9600",
            "'x,y,z' of the pickaxe-dug rock strip used as RockBlend's visual reference (persists in the " +
            "world save). Used by TerrainPhotoRefCapture.");

        TerrainPhotoRefCapture = Config.Bind(
            "Dev Automation",
            "TerrainPhotoRefCapture",
            false,
            "During the next terrain photo run, first visit TerrainPhotoRefPos and capture the dug-rock " +
            "reference (Vanilla + GrassToLava sets) plus an [AR TerrainPhoto] REFGRID dump of veg mask, " +
            "paint-mask RGBA, mesh normal Y and vertex color over the strip (slope-path vs paint-channel recon).");

        TerrainPhotoProbeSpecs = Config.Bind(
            "Dev Automation",
            "TerrainPhotoProbeSpecs",
            "",
            "Semicolon-separated RockBlendSwapSlices values (e.g. '3:5;3:4;3:14;3:12'). When non-empty, the " +
            "terrain photo run appends a RockBlend capture set per spec (LAVACHECK/GRASSCHECK unaffected - " +
            "slice swaps do not change vertex colors), plus one RockBlendWideBand variant of the first spec.");

        TerrainPhotoProbeAshBrightness = Config.Bind(
            "Dev Automation",
            "TerrainPhotoProbeAshBrightness",
            "",
            "Comma-separated AshBlendBandBrightness values (e.g. '1.0,1.3,1.6'). When non-empty, the terrain " +
            "photo run appends an AshBlend capture set per value for band-tone calibration.");

        TerrainPhotoProbeLegacySpecs = Config.Bind(
            "Dev Automation",
            "TerrainPhotoProbeLegacySpecs",
            "",
            "Semicolon-separated LegacySmoothSwapSlices values (use 'none' for the vanilla-array baseline). " +
            "When non-empty, the terrain photo run first captures a LegacySmoothDebugRamp diagnostic set, " +
            "then a LegacySmooth capture set per spec.");

        LegacySmoothDebugRamp = Config.Bind(
            "Dev Automation",
            "LegacySmoothDebugRamp",
            false,
            "Dev diagnostic: LegacySmooth paints every Ashlands chunk with the raw (b,0,0,b) green->ash " +
            "diagonal ramp west->east, ignoring the lava mask, so a top-down shot shows exactly which part " +
            "of the diagonal renders the Plains line. BYPASSES the ash-hold safety gate - lethal ground " +
            "will look green while this is on. Live rebuild.");

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

            // Removed: the Fable Bunny hybrid Lox mode was dropped after user review (the swap
            // read as janky, and a giant lox is an established creature - too plain).
            foreach (var dead in new[] { "FableBunnyHybridMode", "FableBunnyLoxScale", "FableBunnyLoxAttackTrigger" })
            {
                var def = new ConfigDefinition("Fable Bunny", dead);
                if (Config.ContainsKey(def)) Config.Remove(def);
            }
        }
        catch
        {
            // Non-fatal
        }

        Config.Save();
        Config.SaveOnConfigSet = true;

        EnableFableBunny.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyDonor.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyStarLook.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyLashStyle.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyMode.SettingChanged += (_, _) => OnFableBunnyChanged();

        EnableFableWarrior.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        ClonePlayerToWarrior.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        ClonePlayerToArcher.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        ClonePlayerToTwitcher.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        ClonePlayerToMage.SettingChanged += (_, _) => OnFableWarriorModeChanged();

        TerrainTransitionStyle.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        TransitionNoiseScale.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        TransitionNoiseStrength.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        TransitionBlurRadius.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        TransitionAshHold.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        TransitionFadeWidth.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        AshBlendSwapSlices.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        AshBlendBandBrightness.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        AshBlendBandTint.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        AshBlendBandMix.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        AshBlendVariationColor.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        RockBlendSwapSlices.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        RockBlendBandBrightness.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        RockBlendWideBand.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        LegacySmoothSwapSlices.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        LegacySmoothDebugRamp.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        TerrainArrayUncompressed.SettingChanged += (_, _) => OnBandArrayConfigChanged();

        try
        {
            Harmony.PatchAll(typeof(Plugin).Assembly);

            // Apply patches explicitly in case PatchAll missed them (assembly resolution)
            ApplyTerrainPatches();
            ApplyTreePatches();
            Patches.FableBunnyPatches.ApplyBunnyPatches(Harmony);

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
        Patches.TerrainPhotoPatches.Tick();
        Patches.FableBunnyPatches.ReconTick();

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
                if (IsFableBunnyActive)
                {
                    Patches.FableBunnyPatches.RefreshAll();
                    Log.LogInfo("[Ashlands Reborn] Fable Bunny refresh triggered");
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
                Patches.FableBunnyPatches.PeriodicUpdate();
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
            Patches.FableBunnyPatches.RefreshAll();
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
            Patches.FableBunnyPatches.RevertAll();
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

    // Live config toggles for the Fable Bunny (enable, donor, star look, lash style) rebuild
    // every swapped Morgen; RefreshAll itself reverts-only when the gate is off.
    private void OnFableBunnyChanged()
    {
        if (!MasterSwitch.Value || Player.m_localPlayer == null) return;
        Patches.FableBunnyPatches.RefreshAll();
    }

    private void OnTerrainTransitionChanged()
    {
        if (!MasterSwitch.Value || Player.m_localPlayer == null) return;
        Patches.EnvManPatches.ForceTerrainRefresh(force: true);
    }

    // Swap/tone knobs bake into the cached patched diffuse arrays, so those must be
    // invalidated (cache-cleared, old clones stay tracked for restore) before the refresh.
    private void OnBandArrayConfigChanged()
    {
        Patches.TerrainTransition.InvalidatePatchedArray();
        OnTerrainTransitionChanged();
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
