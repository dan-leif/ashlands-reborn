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
    public static ConfigEntry<bool> EnableDevMode { get; private set; } = null!;

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
    public static ConfigEntry<float> LegacySmoothLineGrass { get; private set; } = null!;
    public static ConfigEntry<float> LegacySmoothLineAsh { get; private set; } = null!;
    public static ConfigEntry<float> LegacySmoothLineMud { get; private set; } = null!;
    public static ConfigEntry<float> LegacySmoothLineKhaki { get; private set; } = null!;
    public static ConfigEntry<bool> LegacySmoothDebugRamp { get; private set; } = null!;
    public static ConfigEntry<bool> TerrainArrayUncompressed { get; private set; } = null!;

    // --- Trees ---
    public static ConfigEntry<bool> EnableTreeReplacement { get; private set; } = null!;
    public static ConfigEntry<int> AshlandsTreeDensity { get; private set; } = null!;
    public static ConfigEntry<KeyCode> TreeRefreshKey { get; private set; } = null!;

    // --- Valkyrie ---
    public static ConfigEntry<string> EnableValkyrieSwap { get; private set; } = null!;
    public static ConfigEntry<KeyCode> ValkyrieRefreshKey { get; private set; } = null!;

    // --- Fable Warrior ---
    // Disabled | ClonePlayer | CustomEquipment (parsed via FableWarriorPatches.WarriorMode).
    // Disabled builds no warrior puppet; this key is also the warrior's on/off switch.
    public static ConfigEntry<string> EnableFableWarrior { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorScale { get; private set; } = null!;
    public static ConfigEntry<string> FableWarriorSex { get; private set; } = null!;
    public static ConfigEntry<string> FableWarriorHelmet { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorHelmetScale { get; private set; } = null!;
    public static ConfigEntry<string> FableWarriorChest { get; private set; } = null!;
    public static ConfigEntry<string> FableWarriorLegs { get; private set; } = null!;
    public static ConfigEntry<string> FableWarriorShoulders { get; private set; } = null!;
    public static ConfigEntry<string> FableWarriorWeapon { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorWeaponScale { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorWeaponGripRotX { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorWeaponGripRotY { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorWeaponGripRotZ { get; private set; } = null!;
    public static ConfigEntry<bool> FableWarriorEyeGlow { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorEyeOffsetX { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorEyeOffsetY { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorEyeOffsetZ { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorEyeSeparation { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorEyeScale { get; private set; } = null!;
    public static ConfigEntry<float> FableWarriorEyeIntensity { get; private set; } = null!;
    // Archer/Twitcher/Mage mirror the Fable Warrior tri-state (Disabled/ClonePlayer/CustomEquipment).
    public static ConfigEntry<string> EnableFableArcher { get; private set; } = null!;
    public static ConfigEntry<float> FableArcherScale { get; private set; } = null!;
    public static ConfigEntry<string> FableArcherSex { get; private set; } = null!;
    public static ConfigEntry<string> FableArcherHelmet { get; private set; } = null!;
    public static ConfigEntry<float> FableArcherHelmetScale { get; private set; } = null!;
    public static ConfigEntry<string> FableArcherChest { get; private set; } = null!;
    public static ConfigEntry<string> FableArcherLegs { get; private set; } = null!;
    public static ConfigEntry<string> FableArcherShoulders { get; private set; } = null!;
    public static ConfigEntry<string> FableArcherWeapon { get; private set; } = null!;
    public static ConfigEntry<float> FableArcherWeaponScale { get; private set; } = null!;
    public static ConfigEntry<string> EnableFableTwitcher { get; private set; } = null!;
    public static ConfigEntry<float> FableTwitcherScale { get; private set; } = null!;
    public static ConfigEntry<string> FableTwitcherSex { get; private set; } = null!;
    public static ConfigEntry<string> FableTwitcherHelmet { get; private set; } = null!;
    public static ConfigEntry<float> FableTwitcherHelmetScale { get; private set; } = null!;
    public static ConfigEntry<string> FableTwitcherChest { get; private set; } = null!;
    public static ConfigEntry<string> FableTwitcherLegs { get; private set; } = null!;
    public static ConfigEntry<string> FableTwitcherShoulders { get; private set; } = null!;
    public static ConfigEntry<string> FableTwitcherWeapon { get; private set; } = null!;
    public static ConfigEntry<float> FableTwitcherWeaponScale { get; private set; } = null!;
    public static ConfigEntry<string> EnableFableMage { get; private set; } = null!;
    public static ConfigEntry<float> FableMageScale { get; private set; } = null!;
    public static ConfigEntry<string> FableMageSex { get; private set; } = null!;
    public static ConfigEntry<string> FableMageHelmet { get; private set; } = null!;
    public static ConfigEntry<float> FableMageHelmetScale { get; private set; } = null!;
    public static ConfigEntry<string> FableMageChest { get; private set; } = null!;
    public static ConfigEntry<string> FableMageLegs { get; private set; } = null!;
    public static ConfigEntry<string> FableMageShoulders { get; private set; } = null!;
    public static ConfigEntry<string> FableMageWeapon { get; private set; } = null!;
    public static ConfigEntry<float> FableMageWeaponScale { get; private set; } = null!;
    public static ConfigEntry<string> FableMageTextureCopyFrom { get; private set; } = null!;
    public static ConfigEntry<string> FableMageTextureCopyTo { get; private set; } = null!;
    public static ConfigEntry<bool> FableMageStaffHideFlare { get; private set; } = null!;
    public static ConfigEntry<bool> FableMageStaffHideSmoke { get; private set; } = null!;
    public static ConfigEntry<bool> FableMageStaffHideFlakes { get; private set; } = null!;
    public static ConfigEntry<bool> FableMageStaffGreenFx { get; private set; } = null!;
    // Staff orientation, per source family (player Staff* / DvergerStaff* / charred_magestaff*).
    // These ARE the whole orientation - there is no hidden baked layer underneath. All-zero =
    // the staff's raw attach orientation.
    public static ConfigEntry<float> FableMagePlayerStaffRotX { get; private set; } = null!;
    public static ConfigEntry<float> FableMagePlayerStaffRotY { get; private set; } = null!;
    public static ConfigEntry<float> FableMagePlayerStaffRotZ { get; private set; } = null!;
    public static ConfigEntry<float> FableMagePlayerStaffOffsetX { get; private set; } = null!;
    public static ConfigEntry<float> FableMagePlayerStaffOffsetY { get; private set; } = null!;
    public static ConfigEntry<float> FableMagePlayerStaffOffsetZ { get; private set; } = null!;
    public static ConfigEntry<float> FableMageDvergerStaffRotX { get; private set; } = null!;
    public static ConfigEntry<float> FableMageDvergerStaffRotY { get; private set; } = null!;
    public static ConfigEntry<float> FableMageDvergerStaffRotZ { get; private set; } = null!;
    public static ConfigEntry<float> FableMageDvergerStaffOffsetX { get; private set; } = null!;
    public static ConfigEntry<float> FableMageDvergerStaffOffsetY { get; private set; } = null!;
    public static ConfigEntry<float> FableMageDvergerStaffOffsetZ { get; private set; } = null!;
    public static ConfigEntry<float> FableMageCharredStaffRotX { get; private set; } = null!;
    public static ConfigEntry<float> FableMageCharredStaffRotY { get; private set; } = null!;
    public static ConfigEntry<float> FableMageCharredStaffRotZ { get; private set; } = null!;
    public static ConfigEntry<float> FableMageCharredStaffOffsetX { get; private set; } = null!;
    public static ConfigEntry<float> FableMageCharredStaffOffsetY { get; private set; } = null!;
    public static ConfigEntry<float> FableMageCharredStaffOffsetZ { get; private set; } = null!;

    // --- Fable Race ---
    // Global body mode for ALL Fable Charred puppets (Warrior/Archer/Twitcher/Mage; NOT the
    // Fable Bunny). Vanilla | ClonePlayer | CustomRace. Vanilla is a master-off that folds
    // into IsFablePuppetActive and overrides every class's EnableFable[Class].
    public static ConfigEntry<string> FableRaceMode { get; private set; } = null!;
    public static ConfigEntry<string> FableRaceHair { get; private set; } = null!;
    public static ConfigEntry<string> FableRaceBeard { get; private set; } = null!;
    public static ConfigEntry<float> FableRaceSkinTone { get; private set; } = null!;
    public static ConfigEntry<bool> FableRacePureBlackSkin { get; private set; } = null!;
    public static ConfigEntry<float> FableRaceHairTone { get; private set; } = null!;
    public static ConfigEntry<float> FableRaceBlondness { get; private set; } = null!;

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
    public static ConfigEntry<bool> MageWeaponTest { get; private set; } = null!;
    public static ConfigEntry<string> MageWeaponTestList { get; private set; } = null!;
    public static ConfigEntry<bool> MageWeaponRefCapture { get; private set; } = null!;
    public static ConfigEntry<string> MageWeaponRotSweep { get; private set; } = null!;
    public static ConfigEntry<string> PhotoModeIslandPos { get; private set; } = null!;
    public static ConfigEntry<bool> TerrainPhotoAuto { get; private set; } = null!;
    public static ConfigEntry<KeyCode> TerrainPhotoKey { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoPos { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoRefPos { get; private set; } = null!;
    public static ConfigEntry<bool> TerrainPhotoRefCapture { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoProbeSpecs { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoProbeAshBrightness { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoProbeLegacySpecs { get; private set; } = null!;
    public static ConfigEntry<string> TerrainPhotoProbeLineMixes { get; private set; } = null!;

    public static bool IsWeatherOverrideActive => MasterSwitch?.Value == true && EnableWeatherOverride?.Value == true;
    public static bool IsForceNoonActive => MasterSwitch?.Value == true && ForceNoon?.Value == true;
    public static bool IsTerrainOverrideActive => MasterSwitch?.Value == true && EnableTerrainOverride?.Value == true;
    // Global gate for the Fable puppet system: master switch AND FableRaceMode != Vanilla.
    // FableRaceMode = "Vanilla" is a global master-off that renders all Charred natively,
    // overriding every class's EnableFable[Class]. Per-creature enablement otherwise lives in
    // FableWarriorPatches' profile table (EnableFableWarrior != Disabled for the warrior,
    // the other classes' EnableFable[Class] enums).
    public static bool IsFablePuppetActive =>
        MasterSwitch?.Value == true &&
        !string.Equals(FableRaceMode?.Value, "Vanilla", System.StringComparison.OrdinalIgnoreCase);
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
            "Master toggle for all mod features except EnableDevMode."
        );

        MasterSwitchKey = Config.Bind(
            "General",
            "MasterSwitchKey",
            KeyCode.Backslash,
            "Hotkey to toggle MasterSwitch and immediately revert or apply all visual changes."
        );

        EnableDevMode = Config.Bind(
            "General",
            "EnableDevMode",
            true,
            "When loading a world, run devcommands, god, debugmode and nocost for easier testing."
        );

        // --- Weather ---
        EnableWeatherOverride = Config.Bind(
            "Weather",
            "EnableWeatherOverride",
            true,
            "Override the environment to Meadows-like (clear sky, no cinder rain, no lava fog) in every biome, not just the Ashlands."
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
            "LegacySmooth",
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

        LegacySmoothLineGrass = Config.Bind(
            "Terrain",
            "LegacySmoothLineGrass",
            0.65f,
            new ConfigDescription(
                "LegacySmooth only: weight of the meadows grass texture (slice 0) in the line-slice mix. " +
                "The swapped-in LegacySmoothSwapSlices content is a weighted average of grass/ash/mud/khaki " +
                "(weights normalized by their sum; all zero = pure grass): pure grass reads slightly MORE " +
                "saturated than the grass-diluted-with-ash ground the old plains line inhabits, so diluting " +
                "the line texture the same way makes it vanish. Live rebuild.",
                new AcceptableValueRange<float>(0f, 1f)));

        LegacySmoothLineAsh = Config.Bind(
            "Terrain",
            "LegacySmoothLineAsh",
            0.25f,
            new ConfigDescription(
                "LegacySmooth only: weight of the light ash texture (slice 13 - the tonal-family pick, not " +
                "near-black slice 7) in the line-slice mix. See LegacySmoothLineGrass. Live rebuild.",
                new AcceptableValueRange<float>(0f, 1f)));

        LegacySmoothLineMud = Config.Bind(
            "Terrain",
            "LegacySmoothLineMud",
            0.05f,
            new ConfigDescription(
                "LegacySmooth only: weight of the swamp mud texture (slice 3) in the line-slice mix. " +
                "See LegacySmoothLineGrass. Live rebuild.",
                new AcceptableValueRange<float>(0f, 1f)));

        LegacySmoothLineKhaki = Config.Bind(
            "Terrain",
            "LegacySmoothLineKhaki",
            0.05f,
            new ConfigDescription(
                "LegacySmooth only: weight of the plains khaki texture (slice 8 - a little of the original " +
                "overlay back) in the line-slice mix. See LegacySmoothLineGrass. Live rebuild.",
                new AcceptableValueRange<float>(0f, 1f)));

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

        // --- Fable Warrior ---
        EnableFableWarrior = Config.Bind(
            "Fable Warrior",
            "EnableFableWarrior",
            "CustomEquipment",
            new ConfigDescription(
                "How the Charred_Melee (warrior) is dressed (also the warrior's on/off switch):\n" +
                "Disabled = no puppet, the Charred renders 100% vanilla.\n" +
                "ClonePlayer = copy the player's body + armor AND the player's equipped weapon " +
                "(attached even if it's not a warrior-style weapon).\n" +
                "CustomEquipment = copy the player's body only (no player armor/weapon) and equip the " +
                "FableWarrior Helmet/Chest/Legs/Shoulders/Weapon item IDs from this section.",
                new AcceptableValueList<string>("Disabled", "ClonePlayer", "CustomEquipment")));

        FableWarriorScale = Config.Bind(
            "Fable Warrior",
            "FableWarriorScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Warrior puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableWarriorSex = Config.Bind(
            "Fable Warrior",
            "FableWarriorSex",
            "Male",
            new ConfigDescription(
                "CustomRace only: body sex (model) for the Fable Warrior. Beards only render on Male.",
                new AcceptableValueList<string>("Male", "Female")));

        FableWarriorHelmet = Config.Bind(
            "Fable Warrior",
            "FableWarriorHelmet",
            "knighthelm",
            "CustomEquipment only: item prefab name equipped in the warrior's helmet slot " +
            "(empty = bare head). Must exist in ObjectDB.");

        FableWarriorHelmetScale = Config.Bind(
            "Fable Warrior",
            "FableWarriorHelmetScale",
            1.0f,
            new ConfigDescription(
                "Fine-tune multiplier on the warrior puppet's helmet size. Rigid-attach helmets are first " +
                "normalized to scale with the puppet rig (vanilla attach cancels the rig scale, which " +
                "left player-sized helmets perched on the oversized head); this multiplies on top. " +
                "1.0 = exact fit at puppet scale. Warrior-only (other charred classes normalize at 1.0).",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableWarriorChest = Config.Bind(
            "Fable Warrior",
            "FableWarriorChest",
            "knightchest",
            "CustomEquipment only: item prefab name equipped in the warrior's chest slot " +
            "(empty = bare chest). Must exist in ObjectDB.");

        FableWarriorLegs = Config.Bind(
            "Fable Warrior",
            "FableWarriorLegs",
            "knightlegs",
            "CustomEquipment only: item prefab name equipped in the warrior's legs slot " +
            "(empty = bare legs). Must exist in ObjectDB.");

        FableWarriorShoulders = Config.Bind(
            "Fable Warrior",
            "FableWarriorShoulders",
            "",
            "CustomEquipment only: item prefab name equipped in the warrior's shoulder/cape slot " +
            "(empty = no cape/shoulders). Must exist in ObjectDB.");

        FableWarriorWeapon = Config.Bind(
            "Fable Warrior",
            "FableWarriorWeapon",
            "THSwordKrom",
            "CustomEquipment only: item prefab name equipped in the warrior's right hand " +
            "(empty = bare hand). Default THSwordKrom (Krom). Must exist in ObjectDB.");

        FableWarriorWeaponScale = Config.Bind(
            "Fable Warrior",
            "FableWarriorWeaponScale",
            1.16f,
            new ConfigDescription(
                "CustomEquipment only: scale factor for the warrior's configured weapon. 1.0 = vanilla size. " +
                "1.16 = 16% larger (matches the original Krom sizing). ClonePlayer weapons keep natural size.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        FableWarriorWeaponGripRotX = Config.Bind(
            "Fable Warrior",
            "FableWarriorWeaponGripRotX",
            -17.74648f,
            new ConfigDescription(
                "CustomEquipment only: rotation (degrees) of the warrior's weapon around the hand-attach " +
                "local X axis. Tunes the idle sword-on-shoulder rest so the blade lies beside the trapezius " +
                "instead of through it. Applied on top of the vanilla attach orientation. " +
                "(Grip tuning is a work in progress.)",
                new AcceptableValueRange<float>(-180f, 180f)));

        FableWarriorWeaponGripRotY = Config.Bind(
            "Fable Warrior",
            "FableWarriorWeaponGripRotY",
            15.21127f,
            new ConfigDescription(
                "CustomEquipment only: rotation (degrees) of the warrior's weapon around the hand-attach local Y axis.",
                new AcceptableValueRange<float>(-180f, 180f)));

        FableWarriorWeaponGripRotZ = Config.Bind(
            "Fable Warrior",
            "FableWarriorWeaponGripRotZ",
            -121.6901f,
            new ConfigDescription(
                "CustomEquipment only: rotation (degrees) of the warrior's weapon around the hand-attach local Z axis.",
                new AcceptableValueRange<float>(-180f, 180f)));

        FableWarriorEyeGlow = Config.Bind(
            "Fable Warrior",
            "FableWarriorEyeGlow",
            true,
            "Give the Fable Warrior the vanilla Charred eye glow, recolored white: the Charred's own " +
            "EyeGlow particle systems are cloned onto the puppet's head at the position set by the " +
            "FableWarriorEye* settings below.");

        FableWarriorEyeOffsetX = Config.Bind(
            "Fable Warrior",
            "FableWarriorEyeOffsetX",
            0.005f,
            new ConfigDescription(
                "Sideways offset of the eye-glow pair from the head bone, in meters (face's right " +
                "direction; negative = left). Applies to both eyes together - use FableWarriorEyeSeparation " +
                "to spread them apart.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        FableWarriorEyeOffsetY = Config.Bind(
            "Fable Warrior",
            "FableWarriorEyeOffsetY",
            0.15f,
            new ConfigDescription(
                "Vertical offset of the eye-glow pair from the head bone, in meters (up positive).",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        FableWarriorEyeOffsetZ = Config.Bind(
            "Fable Warrior",
            "FableWarriorEyeOffsetZ",
            0.22f,
            new ConfigDescription(
                "Forward/back offset of the eye-glow pair from the head bone, in meters (toward the face " +
                "positive). Push forward until the glow clears the face instead of being buried in the skull.",
                new AcceptableValueRange<float>(-0.5f, 0.5f)));

        FableWarriorEyeSeparation = Config.Bind(
            "Fable Warrior",
            "FableWarriorEyeSeparation",
            0.1f,
            new ConfigDescription(
                "Distance between the two eye glows, in meters.",
                new AcceptableValueRange<float>(0f, 0.4f)));

        FableWarriorEyeScale = Config.Bind(
            "Fable Warrior",
            "FableWarriorEyeScale",
            2.0f,
            new ConfigDescription(
                "Size multiplier for each eye glow. 1.0 = the vanilla Charred glow size.",
                new AcceptableValueRange<float>(0.1f, 3f)));

        FableWarriorEyeIntensity = Config.Bind(
            "Fable Warrior",
            "FableWarriorEyeIntensity",
            5.0f,
            new ConfigDescription(
                "Brightness of the white eye glow (material tint alpha; 0 = invisible, 5 = max).",
                new AcceptableValueRange<float>(0f, 5f)));

        // --- Fable Archer ---
        EnableFableArcher = Config.Bind(
            "Fable Archer",
            "EnableFableArcher",
            "CustomEquipment",
            new ConfigDescription(
                "How the Charred_Archer is dressed (also the archer's on/off switch):\n" +
                "Disabled = no puppet, the Charred renders 100% vanilla.\n" +
                "ClonePlayer = copy the player's body + armor AND the player's equipped weapon.\n" +
                "CustomEquipment = copy the player's body only and equip the FableArcher " +
                "Helmet/Chest/Legs/Shoulders/Weapon item IDs from this section.",
                new AcceptableValueList<string>("Disabled", "ClonePlayer", "CustomEquipment")));

        FableArcherScale = Config.Bind(
            "Fable Archer",
            "FableArcherScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Archer puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableArcherSex = Config.Bind(
            "Fable Archer",
            "FableArcherSex",
            "Female",
            new ConfigDescription(
                "CustomRace only: body sex (model) for the Fable Archer. Beards only render on Male.",
                new AcceptableValueList<string>("Male", "Female")));

        FableArcherHelmet = Config.Bind(
            "Fable Archer",
            "FableArcherHelmet",
            "norahhelmalt",
            "CustomEquipment only: item prefab name equipped in the archer's helmet slot " +
            "(empty = bare head). Must exist in ObjectDB.");

        FableArcherHelmetScale = Config.Bind(
            "Fable Archer",
            "FableArcherHelmetScale",
            1.0f,
            new ConfigDescription(
                "Fine-tune multiplier on the archer puppet's helmet size (on top of the rig normalization). " +
                "1.0 = exact fit at puppet scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableArcherChest = Config.Bind(
            "Fable Archer",
            "FableArcherChest",
            "norahchest",
            "CustomEquipment only: item prefab name equipped in the archer's chest slot " +
            "(empty = bare chest). Must exist in ObjectDB.");

        FableArcherLegs = Config.Bind(
            "Fable Archer",
            "FableArcherLegs",
            "norahlegs",
            "CustomEquipment only: item prefab name equipped in the archer's legs slot " +
            "(empty = bare legs). Must exist in ObjectDB.");

        FableArcherShoulders = Config.Bind(
            "Fable Archer",
            "FableArcherShoulders",
            "",
            "CustomEquipment only: item prefab name equipped in the archer's shoulder/cape slot " +
            "(empty = none). Must exist in ObjectDB.");

        FableArcherWeapon = Config.Bind(
            "Fable Archer",
            "FableArcherWeapon",
            "BowAshlands",
            "CustomEquipment only: item prefab name the Fable Archer carries in its LEFT hand " +
            "(empty = empty hand). Default BowAshlands. Must exist in ObjectDB.");

        FableArcherWeaponScale = Config.Bind(
            "Fable Archer",
            "FableArcherWeaponScale",
            1.3f,
            new ConfigDescription(
                "CustomEquipment only: multiplier on the archer's weapon size, applied after the weapon is " +
                "normalized to scale with the puppet rig. 1.0 = fits like it fits the player.",
                new AcceptableValueRange<float>(0.25f, 4.0f)));

        // --- Fable Twitcher ---
        EnableFableTwitcher = Config.Bind(
            "Fable Twitcher",
            "EnableFableTwitcher",
            "CustomEquipment",
            new ConfigDescription(
                "How the Charred_Twitcher (and Charred_Twitcher_Summoned) is dressed (also its on/off switch):\n" +
                "Disabled = no puppet, the Charred renders 100% vanilla.\n" +
                "ClonePlayer = copy the player's body + armor AND the player's equipped weapon.\n" +
                "CustomEquipment = copy the player's body only and equip the FableTwitcher " +
                "Helmet/Chest/Legs/Shoulders/Weapon item IDs from this section.",
                new AcceptableValueList<string>("Disabled", "ClonePlayer", "CustomEquipment")));

        FableTwitcherScale = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Twitcher puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableTwitcherSex = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherSex",
            "Male",
            new ConfigDescription(
                "CustomRace only: body sex (model) for the Fable Twitcher. Beards only render on Male.",
                new AcceptableValueList<string>("Male", "Female")));

        FableTwitcherHelmet = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherHelmet",
            "HelmetFenring",
            "CustomEquipment only: item prefab name equipped in the twitcher's helmet slot " +
            "(empty = bare head). Must exist in ObjectDB.");

        FableTwitcherHelmetScale = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherHelmetScale",
            1.0f,
            new ConfigDescription(
                "Fine-tune multiplier on the twitcher puppet's helmet size (on top of the rig normalization). " +
                "1.0 = exact fit at puppet scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableTwitcherChest = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherChest",
            "ArmorFenringChest",
            "CustomEquipment only: item prefab name equipped in the twitcher's chest slot " +
            "(empty = bare chest). Must exist in ObjectDB.");

        FableTwitcherLegs = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherLegs",
            "ArmorFenringLegs",
            "CustomEquipment only: item prefab name equipped in the twitcher's legs slot " +
            "(empty = bare legs). Must exist in ObjectDB.");

        FableTwitcherShoulders = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherShoulders",
            "",
            "CustomEquipment only: item prefab name equipped in the twitcher's shoulder/cape slot " +
            "(empty = none). Must exist in ObjectDB.");

        FableTwitcherWeapon = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherWeapon",
            "FistFenrirClaw",
            "CustomEquipment only: item prefab name the Fable Twitcher carries in its RIGHT hand " +
            "(empty = bare hands). Default FistFenrirClaw. Must exist in ObjectDB.");

        FableTwitcherWeaponScale = Config.Bind(
            "Fable Twitcher",
            "FableTwitcherWeaponScale",
            1.0f,
            new ConfigDescription(
                "CustomEquipment only: multiplier on the twitcher's weapon size, applied after the weapon is " +
                "normalized to scale with the puppet rig. 1.0 = fits like it fits the player.",
                new AcceptableValueRange<float>(0.25f, 4.0f)));

        // --- Fable Mage ---
        EnableFableMage = Config.Bind(
            "Fable Mage",
            "EnableFableMage",
            "CustomEquipment",
            new ConfigDescription(
                "How the Charred_Mage is dressed (also the mage's on/off switch):\n" +
                "Disabled = no puppet, the Charred renders 100% vanilla.\n" +
                "ClonePlayer = copy the player's body + armor AND the player's equipped weapon.\n" +
                "CustomEquipment = copy the player's body only and equip the FableMage " +
                "Helmet/Chest/Legs/Shoulders/Weapon item IDs from this section.",
                new AcceptableValueList<string>("Disabled", "ClonePlayer", "CustomEquipment")));

        FableMageScale = Config.Bind(
            "Fable Mage",
            "FableMageScale",
            1.0f,
            new ConfigDescription(
                "Multiplier on the auto-computed height-match scale for the Fable Mage puppet. 1.0 = auto scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableMageSex = Config.Bind(
            "Fable Mage",
            "FableMageSex",
            "Male",
            new ConfigDescription(
                "CustomRace only: body sex (model) for the Fable Mage. Beards only render on Male.",
                new AcceptableValueList<string>("Male", "Female")));

        FableMageHelmet = Config.Bind(
            "Fable Mage",
            "FableMageHelmet",
            "chiefhelmdeer",
            "CustomEquipment only: item prefab name equipped in the mage's helmet slot " +
            "(empty = bare head). Must exist in ObjectDB.");

        FableMageHelmetScale = Config.Bind(
            "Fable Mage",
            "FableMageHelmetScale",
            1.0f,
            new ConfigDescription(
                "Fine-tune multiplier on the mage puppet's helmet size (on top of the rig normalization). " +
                "1.0 = exact fit at puppet scale.",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        FableMageChest = Config.Bind(
            "Fable Mage",
            "FableMageChest",
            "frostmagechest",
            "CustomEquipment only: item prefab name equipped in the mage's chest slot " +
            "(empty = bare chest). Must exist in ObjectDB.");

        FableMageLegs = Config.Bind(
            "Fable Mage",
            "FableMageLegs",
            "frostmagelegs",
            "CustomEquipment only: item prefab name equipped in the mage's legs slot " +
            "(empty = bare legs). Must exist in ObjectDB.");

        FableMageShoulders = Config.Bind(
            "Fable Mage",
            "FableMageShoulders",
            "",
            "CustomEquipment only: item prefab name equipped in the mage's shoulder/cape slot " +
            "(empty = none). Must exist in ObjectDB.");

        FableMageWeapon = Config.Bind(
            "Fable Mage",
            "FableMageWeapon",
            "DvergerStaffSupport",
            new ConfigDescription(
                "CustomEquipment only: the staff the Fable Mage carries in its RIGHT hand " +
                "(None = empty hand). Default DvergerStaffSupport.\n" +
                "Player staffs (Staff*) mount via their normal attach point; creature staffs " +
                "(DvergerStaff*, charred_magestaff_fire) store their held mesh under an " +
                "'attach_r.hand' child the game normally ignores - this mod mounts that child too. " +
                "How the staff sits in the hand is set entirely by the per-source orientation knobs " +
                "below (FableMagePlayerStaff* / FableMageDvergerStaff* / FableMageCharredStaff*), " +
                "each covering one staff family.\n" +
                "(DvergerStaffNova/Blocker have no held mesh at all and the Bog Witch staff is baked " +
                "into her body mesh - none of them can be equipped, which is why they're not listed.)",
                new AcceptableValueList<string>(
                    "StaffIceShards", "StaffFireball", "StaffShield", "StaffSkeleton", "StaffRedTroll",
                    "StaffGreenRoots", "StaffLightning", "StaffClusterbomb",
                    "DvergerStaffFire", "DvergerStaffIce", "DvergerStaffSupport", "DvergerStaffHeal",
                    "charred_magestaff_fire", "None")));

        FableMageWeaponScale = Config.Bind(
            "Fable Mage",
            "FableMageWeaponScale",
            0.75f,
            new ConfigDescription(
                "CustomEquipment only: multiplier on the mage's weapon size, applied after the weapon is " +
                "normalized to scale with the puppet rig. 1.0 = fits like it fits the player.",
                new AcceptableValueRange<float>(0.25f, 4.0f)));

        FableMageTextureCopyFrom = Config.Bind(
            "Fable Mage",
            "FableMageTextureCopyFrom",
            "New Material 5",
            "SOURCE material/texture for the mage armor re-texture: on the mage puppet's equipped " +
            "armor pieces, every material slot matching FableMageTextureCopyTo is replaced by the " +
            "material matching this name (matches the material name or its main texture name). " +
            "Empty = feature off. Default 'New Material 5' = frostmagechest's bronze shoulder metal. " +
            "The default pairing re-textures frostmagechest's ice right gauntlet to bronze. " +
            "Unmatched names log the armor's full material/texture catalog to the BepInEx log.");

        FableMageTextureCopyTo = Config.Bind(
            "Fable Mage",
            "FableMageTextureCopyTo",
            "FrostMage",
            "TARGET material/texture to be replaced (matched by material name or main texture name) " +
            "on the mage puppet's equipped armor. Empty = feature off. Default 'FrostMage' = " +
            "frostmagechest's ice material (the right gauntlet). Only the puppet is affected - " +
            "the same armor worn by the player keeps its vanilla look.");

        FableMageStaffHideFlare = Config.Bind(
            "Fable Mage",
            "FableMageStaffHideFlare",
            true,
            "Hide the bright flare glow at the mage staff's head (particle children named '*flare*', " +
            "e.g. DvergerStaffSupport's orb flare). Applies live.");

        FableMageStaffHideSmoke = Config.Bind(
            "Fable Mage",
            "FableMageStaffHideSmoke",
            true,
            "Hide the misty smoke emanating from the mage staff's head (particle children named " +
            "'*smoke*', e.g. DvergerStaffSupport's smoke_expl). Applies live.");

        FableMageStaffHideFlakes = Config.Bind(
            "Fable Mage",
            "FableMageStaffHideFlakes",
            true,
            "Hide the square particles swirling off the mage staff's head (particle children named " +
            "'*flakes*', e.g. DvergerStaffSupport's purple/white pixel flakes). Applies live.");

        FableMageStaffGreenFx = Config.Bind(
            "Fable Mage",
            "FableMageStaffGreenFx",
            true,
            "Add the vanilla charred mage staff's green orbiting particle effect " +
            "(charred_magestaff_fire's 'Orbiting Bits') to the mage staff's head - on " +
            "DvergerStaffSupport it rides the green orb. Unaffected by the FableMageStaffHide* " +
            "toggles. Applies live.");

        // Orientation knobs, one group per staff source family. Each group affects ONLY its family
        // and IS the complete orientation (no hidden built-in layer): 0 on every axis = the staff's
        // raw attach orientation. Defaults are neutral except the Dvergr family, whose values are
        // the user's approved in-game tuning (MAGE_STAFF_PLAN.md "v2").
        string RotHelp(string family, string prefix, string axis) =>
            $"Rotation (deg, {axis}) of {family} staffs ({prefix}) in the Fable Mage's hand, hand-attach " +
            "local frame, applied after the attach's equipoffset; 0 on every axis = the staff's raw " +
            "attach orientation. Applies live.";
        string OffHelp(string family, string prefix, string axis) =>
            $"Grip position offset (~m, {axis}) of {family} staffs ({prefix}) in the Fable Mage's hand, " +
            "hand-attach local frame, applied after the attach's equipoffset - shifts where along the " +
            "shaft the hand grips. 0 on every axis = the staff's raw attach position. Applies live.";

        FableMagePlayerStaffRotX = BindMageKnob("FableMagePlayerStaffRotX", 0f, -180f, 180f, RotHelp("player", "Staff*", "X"));
        FableMagePlayerStaffRotY = BindMageKnob("FableMagePlayerStaffRotY", 0f, -180f, 180f, RotHelp("player", "Staff*", "Y"));
        FableMagePlayerStaffRotZ = BindMageKnob("FableMagePlayerStaffRotZ", 0f, -180f, 180f, RotHelp("player", "Staff*", "Z"));
        FableMagePlayerStaffOffsetX = BindMageKnob("FableMagePlayerStaffOffsetX", 0f, -0.5f, 0.5f, OffHelp("player", "Staff*", "X"));
        FableMagePlayerStaffOffsetY = BindMageKnob("FableMagePlayerStaffOffsetY", 0f, -0.5f, 0.5f, OffHelp("player", "Staff*", "Y"));
        FableMagePlayerStaffOffsetZ = BindMageKnob("FableMagePlayerStaffOffsetZ", 0f, -0.5f, 0.5f, OffHelp("player", "Staff*", "Z"));

        FableMageDvergerStaffRotX = BindMageKnob("FableMageDvergerStaffRotX", 0f, -180f, 180f, RotHelp("Dvergr", "DvergerStaff*", "X"));
        FableMageDvergerStaffRotY = BindMageKnob("FableMageDvergerStaffRotY", -95f, -180f, 180f, RotHelp("Dvergr", "DvergerStaff*", "Y"));
        FableMageDvergerStaffRotZ = BindMageKnob("FableMageDvergerStaffRotZ", 0f, -180f, 180f, RotHelp("Dvergr", "DvergerStaff*", "Z"));
        FableMageDvergerStaffOffsetX = BindMageKnob("FableMageDvergerStaffOffsetX", -0.075f, -0.5f, 0.5f, OffHelp("Dvergr", "DvergerStaff*", "X"));
        FableMageDvergerStaffOffsetY = BindMageKnob("FableMageDvergerStaffOffsetY", -0.1f, -0.5f, 0.5f, OffHelp("Dvergr", "DvergerStaff*", "Y"));
        FableMageDvergerStaffOffsetZ = BindMageKnob("FableMageDvergerStaffOffsetZ", 0f, -0.5f, 0.5f, OffHelp("Dvergr", "DvergerStaff*", "Z"));

        FableMageCharredStaffRotX = BindMageKnob("FableMageCharredStaffRotX", 0f, -180f, 180f, RotHelp("Charred", "charred_magestaff*", "X"));
        FableMageCharredStaffRotY = BindMageKnob("FableMageCharredStaffRotY", 0f, -180f, 180f, RotHelp("Charred", "charred_magestaff*", "Y"));
        FableMageCharredStaffRotZ = BindMageKnob("FableMageCharredStaffRotZ", 0f, -180f, 180f, RotHelp("Charred", "charred_magestaff*", "Z"));
        FableMageCharredStaffOffsetX = BindMageKnob("FableMageCharredStaffOffsetX", 0f, -0.5f, 0.5f, OffHelp("Charred", "charred_magestaff*", "X"));
        FableMageCharredStaffOffsetY = BindMageKnob("FableMageCharredStaffOffsetY", 0f, -0.5f, 0.5f, OffHelp("Charred", "charred_magestaff*", "Y"));
        FableMageCharredStaffOffsetZ = BindMageKnob("FableMageCharredStaffOffsetZ", 0f, -0.5f, 0.5f, OffHelp("Charred", "charred_magestaff*", "Z"));

        ConfigEntry<float> BindMageKnob(string key, float def, float min, float max, string help) =>
            Config.Bind("Fable Mage", key, def, new ConfigDescription(help, new AcceptableValueRange<float>(min, max)));

        // --- Fable Race ---
        // Fixed body appearance shared by all four Fable Charred puppets (not the Fable Bunny).
        FableRaceMode = Config.Bind(
            "Fable Race",
            "FableRaceMode",
            "CustomRace",
            new ConfigDescription(
                "Body treatment for ALL Fable Charred puppets (Warrior/Archer/Twitcher/Mage):\n" +
                "Vanilla = no puppets at all, every Charred renders 100% native (overrides the " +
                "per-class EnableFable[Class] settings).\n" +
                "ClonePlayer = puppet bodies copy the local player's sex/skin/hair/beard (legacy).\n" +
                "CustomRace = puppet bodies use the Fable Race settings below.\n" +
                "The class's own EnableFable[Class] still chooses whether it gets a puppet and its " +
                "armor/weapon; this key only drives the body.",
                new AcceptableValueList<string>("Vanilla", "ClonePlayer", "CustomRace")));

        FableRaceHair = Config.Bind(
            "Fable Race",
            "FableRaceHair",
            "Hair5",
            new ConfigDescription(
                "CustomRace only: hair style item. HairNone = bald. Hair5/Hair8 are short styles.",
                new AcceptableValueList<string>(
                    "HairNone", "Hair1", "Hair2", "Hair3", "Hair4", "Hair5", "Hair6", "Hair7",
                    "Hair8", "Hair9", "Hair10", "Hair11", "Hair12", "Hair13", "Hair14", "Hair15",
                    "Hair16", "Hair17", "Hair18", "Hair19", "Hair20", "Hair21", "Hair22", "Hair23")));

        FableRaceBeard = Config.Bind(
            "Fable Race",
            "FableRaceBeard",
            "BeardNone",
            new ConfigDescription(
                "CustomRace only: beard item. BeardNone = clean-shaven. Only renders when Sex = Male.",
                new AcceptableValueList<string>(
                    "BeardNone", "Beard1", "Beard2", "Beard3", "Beard4", "Beard5", "Beard6",
                    "Beard7", "Beard8", "Beard9", "Beard10", "Beard11", "Beard12", "Beard13",
                    "Beard14", "Beard15", "Beard16")));

        FableRaceSkinTone = Config.Bind(
            "Fable Race",
            "FableRaceSkinTone",
            1.0f,
            new ConfigDescription(
                "CustomRace only: skin tone. 0 = lightest, 1 = darkest. The vanilla gamut stops " +
                "around 70% grey; for a true-black skin use FableRacePureBlackSkin.",
                new AcceptableValueRange<float>(0f, 1f)));

        FableRacePureBlackSkin = Config.Bind(
            "Fable Race",
            "FableRacePureBlackSkin",
            true,
            "CustomRace only: force the skin to pure black (0,0,0), darker than the FableRaceSkinTone " +
            "slider can reach. Overrides FableRaceSkinTone when on. The eyes keep their own color and " +
            "the body is still lit by the scene (highlights/shadows read the form) - only the skin " +
            "albedo goes black.");

        FableRaceHairTone = Config.Bind(
            "Fable Race",
            "FableRaceHairTone",
            1.0f,
            new ConfigDescription(
                "CustomRace only: hair color tone along the gradient. 0 = lightest, 1 = darkest.",
                new AcceptableValueRange<float>(0f, 1f)));

        FableRaceBlondness = Config.Bind(
            "Fable Race",
            "FableRaceBlondness",
            0.0f,
            new ConfigDescription(
                "CustomRace only: hair brightness/blondness multiplier. 0 = darkest, 1 = brightest/most blonde.",
                new AcceptableValueRange<float>(0f, 1f)));

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

        MageWeaponTest = Config.Bind(
            "Dev Automation",
            "MageWeaponTest",
            false,
            "Dev: once ~10s after world load, dump the ObjectDB staff catalog, spawn a Charred_Mage, and " +
            "cycle MageWeaponTestList through FableMageWeapon, capturing a full-body + close-up screenshot " +
            "per staff into <plugin dir>\\AR_MageWeapon\\ (+ STAFFS.txt catalog + DONE.txt).");

        MageWeaponTestList = Config.Bind(
            "Dev Automation",
            "MageWeaponTestList",
            "StaffIceShards,StaffFireball,StaffShield,StaffSkeleton,StaffRedTroll,StaffGreenRoots," +
            "StaffLightning,StaffClusterbomb,DvergerStaffFire,DvergerStaffIce,DvergerStaffSupport," +
            "DvergerStaffHeal,charred_magestaff_fire",
            "Comma-separated staff/weapon prefab IDs the MageWeaponTest harness cycles through the Fable " +
            "Mage's hand. Must stay a subset of the FableMageWeapon dropdown list (values outside the " +
            "list are clamped to its first entry when set).");

        MageWeaponRefCapture = Config.Bind(
            "Dev Automation",
            "MageWeaponRefCapture",
            false,
            "Dev: prepend a reference phase to the MageWeaponTest run - ground-truth shots for judging " +
            "the puppet's staff placement: the PLAYER equipping each player staff in MageWeaponTestList " +
            "(ref_player_*.png), vanilla DvergerMageFire/Ice/Support spawns holding their own staffs " +
            "(ref_DvergerMage*.png), and a vanilla puppet-disabled Charred_Mage (ref_Charred_Mage_*.png). " +
            "Same framing as the puppet shots so they compare side-by-side.");

        MageWeaponRotSweep = Config.Bind(
            "Dev Automation",
            "MageWeaponRotSweep",
            "",
            "Dev: rotation/offset sweep for the MageWeaponTest run. Entries 'rx,ry,rz[,ox,oy,oz]' " +
            "separated by '|' (e.g. \"0,0,0|0,90,0|0,180,0|90,0,0\"). When non-empty, each staff in " +
            "MageWeaponTestList is captured once per entry with the orientation knobs of THAT staff's " +
            "own source family (FableMagePlayerStaff*/FableMageDvergerStaff*/FableMageCharredStaff*) " +
            "set to it (filenames {staff}_sweep_{i}_{yaw}.png); all 18 knobs are restored afterwards. " +
            "Values are ABSOLUTE orientations, not deltas - there is no baked base underneath. " +
            "Shrink MageWeaponTestList to the staff under tuning - every entry costs a ~4s rebuild.");

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

        TerrainPhotoProbeLineMixes = Config.Bind(
            "Dev Automation",
            "TerrainPhotoProbeLineMixes",
            "",
            "Semicolon-separated LegacySmooth line-mix tuples 'grass,ash,mud,khaki' (e.g. " +
            "'1,0,0,0;0.65,0.25,0.05,0.05'). When non-empty, the terrain photo run appends a LegacySmooth " +
            "capture set per tuple (the LegacySmoothLine* sliders are set, terrain refreshed, then " +
            "restored). Include '1,0,0,0' as the pure-grass reference for the difference-based line mask.");

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
            // WarriorKromScale is the only key that survived the legacy Charred Warrior
            // removal (now under "Fable Warrior"). Carry over any value a user had saved
            // under the old "Charred Warrior" / "Warrior General" sections.
            MigrateKey("Charred Warrior", "CharredWarriorKromScale", FableWarriorWeaponScale);
            MigrateKey("Warrior General", "WarriorKromScale", FableWarriorWeaponScale);
            // Dead config sections linger in saved cfg files because BepInEx keeps unbound keys
            // as "orphaned entries" that Config.Remove/ContainsKey do NOT touch (which is also
            // why the DataDumpKey and Valkyrie "Creatures"->"Valkyrie" migrations above could
            // never actually delete the old keys). Reach the orphan store via reflection.
            System.Collections.IDictionary? orphanedEntries = null;
            try
            {
                var prop = typeof(ConfigFile).GetProperty("OrphanedEntries",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                orphanedEntries = prop?.GetValue(Config) as System.Collections.IDictionary;
            }
            catch { /* internals unavailable on this BepInEx build - orphans stay (invisible in F1) */ }

            // Remove ONLY orphaned (unbound) entries in a section. Live/bound keys are never in
            // the orphan store, so this can never delete a key an active feature still uses -
            // safe even for mixed sections. In particular the live Valkyrie keys are bound under
            // the "Valkyrie" section, so purging "Creatures" leaves them completely untouched.
            void PurgeOrphanedSection(string section)
            {
                if (orphanedEntries == null) return;
                var dead = orphanedEntries.Keys.Cast<ConfigDefinition>()
                    .Where(d => d.Section == section).ToList();
                foreach (var def in dead)
                    orphanedEntries.Remove(def);
            }
            // Remove ONE orphaned key by exact (section, key) - used to clean renamed keys out of a
            // section that still has live keys (e.g. "Fable Warrior"), where PurgeOrphanedSection
            // would be too broad.
            void PurgeOrphanedKey(string section, string key)
            {
                if (orphanedEntries == null) return;
                var dead = orphanedEntries.Keys.Cast<ConfigDefinition>()
                    .Where(d => d.Section == section && d.Key == key).ToList();
                foreach (var def in dead)
                    orphanedEntries.Remove(def);
            }
            // Read a raw orphaned value by (section, key). Orphaned (unbound) keys are invisible to
            // Config.ContainsKey/Config[def], so MigrateKey can't reach them - go through the store.
            string? ReadOrphanRaw(string section, string key)
            {
                if (orphanedEntries == null) return null;
                var def = new ConfigDefinition(section, key);
                return orphanedEntries.Contains(def) ? orphanedEntries[def] as string : null;
            }
            // The four legacy Warrior sections (all their binds were deleted this release) plus
            // two ancient dead sections from long-superseded versions: "Charred Warrior" (fully
            // dead) and "Creatures" (orphaned CharredWarrior/BodySwap leftovers + stale Valkyrie
            // duplicates already migrated to "Valkyrie"). WarriorKromScale was migrated above.
            PurgeOrphanedSection("Warrior General");
            PurgeOrphanedSection("Warrior Body");
            PurgeOrphanedSection("Warrior Player Armor");
            PurgeOrphanedSection("Warrior Vanilla Armor");
            PurgeOrphanedSection("Charred Warrior");
            PurgeOrphanedSection("Creatures");

            // --- Fable Warrior config restructure (this release) ---
            // Warrior mode key: original ClonePlayerToWarrior (bool) -> interim FableWarriorSwitch
            // (enum, Vanilla/ClonePlayer/CustomEquipment) -> current EnableFableWarrior (enum,
            // Vanilla renamed to Disabled). FableHelmetScale dropped; WarriorKromScale ->
            // FableWarriorWeaponScale; FableKromGrip* -> FableWarriorWeaponGrip*; FableHelmetYOffset
            // dropped. Old keys are unbound this session (orphaned) - carry values over, then purge.
            void CarryFloat(string section, string oldKey, ConfigEntry<float> target)
            {
                var raw = ReadOrphanRaw(section, oldKey)?.Trim();
                if (string.IsNullOrEmpty(raw)) return;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    target.Value = v;
            }
            void CarryString(string section, string oldKey, ConfigEntry<string> target)
            {
                var raw = ReadOrphanRaw(section, oldKey);
                if (!string.IsNullOrEmpty(raw)) target.Value = raw!;
            }
            // Warrior: mode source #1 (oldest) ClonePlayerToWarrior bool (true -> ClonePlayer, false -> Disabled).
            var oldClone = ReadOrphanRaw("Fable Warrior", "ClonePlayerToWarrior")?.Trim();
            if (!string.IsNullOrEmpty(oldClone) && bool.TryParse(oldClone, out var wasClone))
                EnableFableWarrior.Value = wasClone ? "ClonePlayer" : "Disabled";
            // Warrior: mode source #2 (interim, wins over #1) FableWarriorSwitch enum (Vanilla -> Disabled).
            var oldSwitch = ReadOrphanRaw("Fable Warrior", "FableWarriorSwitch")?.Trim();
            if (!string.IsNullOrEmpty(oldSwitch))
                EnableFableWarrior.Value = string.Equals(oldSwitch, "Vanilla", StringComparison.OrdinalIgnoreCase)
                    ? "Disabled" : oldSwitch;
            // NOTE: the old shared FableHelmetScale is deliberately NOT carried into
            // FableWarriorHelmetScale - the warrior helmet scale defaults to 1.0 (the legacy value
            // scaled ALL classes' helmets; it should not silently become the warrior's tuning).
            CarryFloat("Fable Warrior", "WarriorKromScale", FableWarriorWeaponScale);
            CarryFloat("Fable Warrior", "FableKromGripRotX", FableWarriorWeaponGripRotX);
            CarryFloat("Fable Warrior", "FableKromGripRotY", FableWarriorWeaponGripRotY);
            CarryFloat("Fable Warrior", "FableKromGripRotZ", FableWarriorWeaponGripRotZ);
            foreach (var oldKey in new[] {
                "FableWarriorSwitch", "ClonePlayerToWarrior", "FableHelmetScale", "FableHelmetYOffset",
                "WarriorKromScale", "FableKromGripRotX", "FableKromGripRotY", "FableKromGripRotZ",
                "FableKromGripOffX", "FableKromGripOffY", "FableKromGripOffZ",
                // Removed this release: the weapon grip position offsets (rotation-only grip now).
                "FableWarriorWeaponGripOffX", "FableWarriorWeaponGripOffY", "FableWarriorWeaponGripOffZ",
                // Obsolete dev knob (retarget now always copies Charred bone orientations directly).
                "FableWarriorRetargetSource" })
                PurgeOrphanedKey("Fable Warrior", oldKey);

            // --- Fable Archer/Twitcher/Mage restructure (this release) ---
            // ClonePlayerTo[Class] (bool) -> EnableFable[Class] (enum, true -> ClonePlayer, false ->
            // Disabled). Archer/Mage weapon: FableArcherBow/FableMageStaff -> Fable[Class]Weapon,
            // FableArcher/MageBow/StaffScale -> Fable[Class]WeaponScale. New keys (helmet/chest/legs/
            // shoulders/helmet-scale, + Twitcher weapon/scale) have no old value to carry.
            void MigrateClassMode(string section, string oldBoolKey, ConfigEntry<string> mode)
            {
                var raw = ReadOrphanRaw(section, oldBoolKey)?.Trim();
                if (!string.IsNullOrEmpty(raw) && bool.TryParse(raw, out var on))
                    mode.Value = on ? "ClonePlayer" : "Disabled";
            }
            MigrateClassMode("Fable Archer", "ClonePlayerToArcher", EnableFableArcher);
            CarryString("Fable Archer", "FableArcherBow", FableArcherWeapon);
            CarryFloat("Fable Archer", "FableArcherBowScale", FableArcherWeaponScale);
            MigrateClassMode("Fable Twitcher", "ClonePlayerToTwitcher", EnableFableTwitcher);
            MigrateClassMode("Fable Mage", "ClonePlayerToMage", EnableFableMage);
            CarryString("Fable Mage", "FableMageStaff", FableMageWeapon);
            CarryFloat("Fable Mage", "FableMageStaffScale", FableMageWeaponScale);
            foreach (var (section, key) in new[] {
                ("Fable Archer", "ClonePlayerToArcher"), ("Fable Archer", "FableArcherBow"),
                ("Fable Archer", "FableArcherBowScale"),
                ("Fable Twitcher", "ClonePlayerToTwitcher"),
                ("Fable Mage", "ClonePlayerToMage"), ("Fable Mage", "FableMageStaff"),
                ("Fable Mage", "FableMageStaffScale") })
                PurgeOrphanedKey(section, key);

            // The six global staff knobs were split into per-source families (this release), and the
            // hardcoded orientation table they composed with is gone. Deliberately NOT carried over:
            // they tuned a baked base that no longer exists, and the one tuning the user approved
            // (the Dvergr family) is now folded into FableMageDvergerStaff*'s DEFAULTS.
            foreach (var oldKey in new[] {
                "FableMageWeaponRotX", "FableMageWeaponRotY", "FableMageWeaponRotZ",
                "FableMageWeaponOffsetX", "FableMageWeaponOffsetY", "FableMageWeaponOffsetZ" })
                PurgeOrphanedKey("Fable Mage", oldKey);

            // FableMageStaffHideGlow was split into HideFlare + HideSmoke (this release, one
            // release after it was added): it covered both the flare and smoke_expl particle
            // children, so its value carries into both new keys.
            var oldHideGlow = ReadOrphanRaw("Fable Mage", "FableMageStaffHideGlow")?.Trim();
            if (!string.IsNullOrEmpty(oldHideGlow) && bool.TryParse(oldHideGlow, out var hideGlowOn))
            {
                FableMageStaffHideFlare.Value = hideGlowOn;
                FableMageStaffHideSmoke.Value = hideGlowOn;
            }
            PurgeOrphanedKey("Fable Mage", "FableMageStaffHideGlow");

            // FableRaceSex (global) was split into per-class Fable[Class]Sex (this release). No
            // carry: the per-class defaults (Archer = Female, others = Male) are intentional.
            PurgeOrphanedKey("Fable Race", "FableRaceSex");

            // EnableDevCommandsAndGodMode -> EnableDevMode (this release; the dev commands it runs
            // now also include debugmode and nocost). Unbound this session, so read it as an orphan.
            var oldDevMode = ReadOrphanRaw("General", "EnableDevCommandsAndGodMode")?.Trim();
            if (!string.IsNullOrEmpty(oldDevMode) && bool.TryParse(oldDevMode, out var devModeOn))
                EnableDevMode.Value = devModeOn;
            PurgeOrphanedKey("General", "EnableDevCommandsAndGodMode");

            Config.Save();

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

        // Every Fable Bunny config applies instantly (OnFableBunnyChanged rebuilds swapped
        // Morgens, re-reading all bunny config).
        EnableFableBunny.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyDonor.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyStarLook.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyLashStyle.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyMode.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyHeight.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyScale.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyYOffset.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyPounceAmplitude.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyMoveAnimSpeed.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyRollStyle.SettingChanged += (_, _) => OnFableBunnyChanged();
        FableBunnyHideRagdoll.SettingChanged += (_, _) => OnFableBunnyChanged();

        // Every Fable Warrior/Archer/Twitcher/Mage config applies instantly
        // (OnFableWarriorModeChanged rebuilds the puppets, re-reading all config getters).
        // This replaces the removed F10 manual-refresh hotkey.
        EnableFableWarrior.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorHelmet.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorHelmetScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorChest.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorLegs.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorShoulders.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorWeapon.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorWeaponScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorWeaponGripRotX.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorWeaponGripRotY.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorWeaponGripRotZ.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorEyeGlow.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        // Eye tuning sliders adjust the live clones in place (no puppet rebuild - a rebuild
        // restarts the glow particle systems, which refill over seconds and blink out while
        // dragging sliders in the pause menu).
        FableWarriorEyeOffsetX.SettingChanged += (_, _) => Patches.FableWarriorPatches.UpdateFableEyes();
        FableWarriorEyeOffsetY.SettingChanged += (_, _) => Patches.FableWarriorPatches.UpdateFableEyes();
        FableWarriorEyeOffsetZ.SettingChanged += (_, _) => Patches.FableWarriorPatches.UpdateFableEyes();
        FableWarriorEyeSeparation.SettingChanged += (_, _) => Patches.FableWarriorPatches.UpdateFableEyes();
        FableWarriorEyeScale.SettingChanged += (_, _) => Patches.FableWarriorPatches.UpdateFableEyes();
        FableWarriorEyeIntensity.SettingChanged += (_, _) => Patches.FableWarriorPatches.UpdateFableEyes();
        EnableFableArcher.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherHelmet.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherHelmetScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherChest.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherLegs.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherShoulders.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherWeapon.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherWeaponScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        EnableFableTwitcher.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherHelmet.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherHelmetScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherChest.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherLegs.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherShoulders.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherWeapon.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherWeaponScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        EnableFableMage.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageHelmet.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageHelmetScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageChest.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageLegs.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageShoulders.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageWeapon.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageWeaponScale.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageTextureCopyFrom.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageTextureCopyTo.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageStaffHideFlare.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageStaffHideSmoke.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageStaffHideFlakes.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageStaffGreenFx.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        foreach (var knob in new[] {
                     FableMagePlayerStaffRotX, FableMagePlayerStaffRotY, FableMagePlayerStaffRotZ,
                     FableMagePlayerStaffOffsetX, FableMagePlayerStaffOffsetY, FableMagePlayerStaffOffsetZ,
                     FableMageDvergerStaffRotX, FableMageDvergerStaffRotY, FableMageDvergerStaffRotZ,
                     FableMageDvergerStaffOffsetX, FableMageDvergerStaffOffsetY, FableMageDvergerStaffOffsetZ,
                     FableMageCharredStaffRotX, FableMageCharredStaffRotY, FableMageCharredStaffRotZ,
                     FableMageCharredStaffOffsetX, FableMageCharredStaffOffsetY, FableMageCharredStaffOffsetZ })
            knob.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        // Fable Race (global body mode) - also rebuilds all puppets live (Vanilla reverts them).
        FableRaceMode.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableWarriorSex.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableArcherSex.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableTwitcherSex.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableMageSex.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableRaceHair.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableRaceBeard.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableRaceSkinTone.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableRacePureBlackSkin.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableRaceHairTone.SettingChanged += (_, _) => OnFableWarriorModeChanged();
        FableRaceBlondness.SettingChanged += (_, _) => OnFableWarriorModeChanged();

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
        LegacySmoothLineGrass.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        LegacySmoothLineAsh.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        LegacySmoothLineMud.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        LegacySmoothLineKhaki.SettingChanged += (_, _) => OnBandArrayConfigChanged();
        LegacySmoothDebugRamp.SettingChanged += (_, _) => OnTerrainTransitionChanged();
        TerrainArrayUncompressed.SettingChanged += (_, _) => OnBandArrayConfigChanged();

        try
        {
            Harmony.PatchAll(typeof(Plugin).Assembly);

            // Apply patches explicitly in case PatchAll missed them (assembly resolution)
            ApplyTerrainPatches();
            ApplyTreePatches();
            Patches.FableBunnyPatches.ApplyBunnyPatches(Harmony);

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Mod: {(MasterSwitch.Value ? "ON" : "OFF")}, Weather: {(EnableWeatherOverride.Value ? "ON" : "OFF")}, Terrain: {(EnableTerrainOverride.Value ? "ON" : "OFF")}, Trees: {(EnableTreeReplacement.Value ? "ON" : "OFF")}, Valkyrie: {EnableValkyrieSwap.Value}, FableWarrior: {EnableFableWarrior.Value}");
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
    private static float _lastBracerScaleUpdateTime;

    private void Update()
    {
        Patches.DevAutoLoadPatches.Tick();
        Patches.PhotoModePatches.Tick();
        Patches.LifecycleTestPatches.Tick();
        Patches.TerrainPhotoPatches.Tick();
        Patches.MageWeaponTestPatches.Tick();
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
            // Fable creature config changes apply instantly via SettingChanged handlers
            // (see OnFableWarriorModeChanged / OnFableBunnyChanged), so there is no manual
            // refresh hotkey. The periodic tick keeps the Fable puppets/bunnies in sync.
            if (Time.time - _lastBracerScaleUpdateTime >= 0.2f)
            {
                _lastBracerScaleUpdateTime = Time.time;
                Patches.FableWarriorPatches.PeriodicUpdate();
                Patches.FableBunnyPatches.PeriodicUpdate();
            }
        }

        if (!EnableDevMode.Value) return;

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
            Invoke(nameof(RunDevModeCommands), 1f);
            Log.LogInfo("[Ashlands Reborn] Ran devcommands; god, debugmode, nocost in 1s");
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
            Patches.FableWarriorPatches.RevertAll();
            Patches.FableBunnyPatches.RevertAll();
            Log.LogInfo("[Ashlands Reborn] Master switch OFF - all overrides reverted");
        }
    }

    // god/debugmode/nocost are all gated behind devcommands, which is run a second earlier.
    private void RunDevModeCommands()
    {
        if (Console.instance == null || Player.m_localPlayer == null) return;

        foreach (var cmd in new[] { "god", "debugmode", "nocost" })
            Console.instance.TryRunCommand(cmd);
        Log.LogInfo("[Ashlands Reborn] Ran god, debugmode, nocost");
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

    // Any Fable Warrior/Archer/Twitcher/Mage config change (enable, per-creature clone
    // toggle, scale/grip/weapon knobs) applies instantly: rebuild the puppets when the
    // system is active, otherwise revert them so the charred creatures return to vanilla.
    // RefreshAll reverts + rebuilds each puppet, re-reading every config getter.
    private void OnFableWarriorModeChanged()
    {
        if (!MasterSwitch.Value || Player.m_localPlayer == null) return;

        if (IsFablePuppetActive)
            Patches.FableWarriorPatches.RefreshAll();
        else
            Patches.FableWarriorPatches.RevertAll();
    }

    private void OnDestroy()
    {
        Harmony.UnpatchSelf();
        Config.Save();
    }
}
