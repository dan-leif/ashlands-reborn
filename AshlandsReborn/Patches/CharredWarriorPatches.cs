using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// Visual transformation for Charred_Melee (the Ashlands greatsword enemy).
/// Phase 1 (sword): prefixes VisEquipment.SetRightItem to replace charred_greatsword_* with THSwordKrom.
/// Behavior (attacks, damage, AI) is untouched throughout.
/// Phase 0 dump: fires once per session on first Charred_Melee spawn.
/// </summary>
[HarmonyPatch]
internal static class CharredWarriorPatches
{
    private const string CharredMeleePrefab = "Charred_Melee";
    private const string KromPrefabName     = "THSwordKrom";
    private const string CharredSwordPrefix = "charred_greatsword";
    private const string HelmetDrakeName    = "HelmetDrake";
    private const string CharredHelmetName  = "Charred_Helmet";



    // Reflection cache — private VisEquipment fields
    private static readonly FieldInfo? FRightItem =
        typeof(VisEquipment).GetField("m_rightItem", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FRightItemInstance =
        typeof(VisEquipment).GetField("m_rightItemInstance", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FHelmetItem =
        typeof(VisEquipment).GetField("m_helmetItem", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FHelmetItemInstance =
        typeof(VisEquipment).GetField("m_helmetItemInstance", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FHelmetTransform =
        typeof(VisEquipment).GetField("m_helmet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo? FChestItem =
        typeof(VisEquipment).GetField("m_chestItem", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FLegItem =
        typeof(VisEquipment).GetField("m_legItem", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FShoulderItem =
        typeof(VisEquipment).GetField("m_shoulderItem", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FChestItemInstances =
        typeof(VisEquipment).GetField("m_chestItemInstances", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FLegItemInstances =
        typeof(VisEquipment).GetField("m_legItemInstances", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FShoulderItemInstances =
        typeof(VisEquipment).GetField("m_shoulderItemInstances", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool _suppressSwordSwap;
    private static int  _swapLogCount;
    private static bool _dumpDone;

    // Swap active when master switch + EnableCharredWarriorSwap are on
    private static bool ShouldSwap() =>
        (Plugin.MasterSwitch?.Value ?? false) &&
        (Plugin.EnableCharredWarriorSwap?.Value ?? false);

    private static readonly Dictionary<string, Transform> _boneCache = new(StringComparer.OrdinalIgnoreCase);

    private static string GetPrefabName(GameObject go)
    {
        var name = go.name;
        var idx = name.IndexOf('(');
        return idx >= 0 ? name.Substring(0, idx).Trim() : name;
    }

    private static bool IsCharredMelee(GameObject go) =>
        GetPrefabName(go) == CharredMeleePrefab;

    private static Transform? FindInChildren(Transform parent, string name, Transform? skip = null)
    {
        if (parent == skip) return null;
        if (parent.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var found = FindInChildren(parent.GetChild(i), name, skip);
            if (found != null) return found;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Phase 1: Sword swap — prefix on VisEquipment.SetRightItem
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetRightItem))]
    [HarmonyPrefix]
    private static void SetRightItem_Prefix(VisEquipment __instance, ref string name)
    {
        if (_suppressSwordSwap) return;
        if (!ShouldSwap()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        if (!name.StartsWith(CharredSwordPrefix, StringComparison.OrdinalIgnoreCase)) return;

        // Store original name for revert (only on first swap)
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (string.IsNullOrEmpty(marker.OriginalRightItem))
            marker.OriginalRightItem = name;

        name = KromPrefabName;

        if (_swapLogCount++ < 5)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred_Melee sword: '{marker.OriginalRightItem}' \u2192 '{KromPrefabName}'");
    }

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetRightItem))]
    [HarmonyPostfix]
    private static void SetRightItem_Postfix(VisEquipment __instance)
    {
        if (!ShouldSwap()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;

        var curItem = FRightItem?.GetValue(__instance) as string ?? "";
        if (!string.Equals(curItem, KromPrefabName, StringComparison.OrdinalIgnoreCase)) return;

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        // Skip if we've already scaled this exact instance.
        // NOTE: do NOT check marker.KromScaled — that flag doesn't survive the prefix resetting it.
        if (marker?.LastScaledKromInstance != null) return;
        __instance.StartCoroutine(ScaleKromAfterAttach(__instance, marker));
    }

    private static System.Collections.IEnumerator ScaleKromAfterAttach(VisEquipment vis, AshlandsRebornCharredSwapped? marker)
    {
        // Wait several frames for the character skeleton, scale, and first animation frame to settle.
        // Initial spawn (especially console spawn) can have 'garbage' bone positions for the first few frames.
        for (int i = 0; i < 10; i++) yield return null;

        if (vis == null || !ShouldSwap()) yield break;

        var weaponGo = FRightItemInstance?.GetValue(vis) as GameObject;

        // Guard: skip if this is the same instance we already scaled (prevents stacking during combat).
        if (weaponGo == null || (marker != null && ReferenceEquals(weaponGo, marker.LastScaledKromInstance)))
            yield break;

        var scale = Plugin.CharredWarriorKromScale?.Value ?? 1.16f;
        weaponGo.transform.localScale *= scale;
        if (marker != null)
        {
            marker.KromScaled = true;
            marker.LastScaledKromInstance = weaponGo;
        }
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Krom sword adjusted: scale={scale}");
    }

    // -------------------------------------------------------------------------
    // Phase 1.5: Helmet swap — prefix on VisEquipment.SetHelmetItem
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetHelmetItem))]
    [HarmonyPrefix]
    private static void SetHelmetItem_Prefix(VisEquipment __instance, ref string name)
    {
        if (_suppressSwordSwap) return;
        if (!ShouldSwap()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;

        var targetHelmet = Plugin.CharredWarriorHelmetName?.Value ?? HelmetDrakeName;
        if (string.IsNullOrEmpty(targetHelmet)) return;

        // Already the target — nothing to do
        if (string.Equals(name, targetHelmet, StringComparison.OrdinalIgnoreCase)) return;

        // Ensure the attachment point exists BEFORE the game calls AttachItem.
        EnsureHelmetTransform(__instance);

        // Safety check: verify prefab exists in ZNetScene
        if (targetHelmet != HelmetDrakeName && ZNetScene.instance != null)
        {
            if (ZNetScene.instance.GetPrefab(targetHelmet) == null)
            {
                if (_swapLogCount < 5)
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] Configured helmet '{targetHelmet}' not found. Falling back to '{HelmetDrakeName}'.");
                targetHelmet = HelmetDrakeName;
            }
        }

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (!marker.HelmetSwapped)
            marker.OriginalHelmetItem = name;

        name = targetHelmet;
        marker.HelmetSwapped = true;

        if (_swapLogCount++ < 10)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred_Melee helmet: '{marker.OriginalHelmetItem}' \u2192 '{name}'");
    }

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetHelmetItem))]
    [HarmonyPostfix]
    private static void SetHelmetItem_Postfix(VisEquipment __instance)
    {
        if (!ShouldSwap()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;

        var curItem = FHelmetItem?.GetValue(__instance) as string ?? "";
        var targetHelmet = Plugin.CharredWarriorHelmetName?.Value ?? HelmetDrakeName;
        // Check if current matches target (or fallback)
        if (!string.Equals(curItem, targetHelmet, StringComparison.OrdinalIgnoreCase) && 
            !string.Equals(curItem, HelmetDrakeName, StringComparison.OrdinalIgnoreCase)) return;

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        // Skip if we've already scaled this exact instance.
        if (marker?.LastScaledHelmetInstance != null) return;

        // Visual fix: Hide the helmet immediately. 
        // We'll show it again in the coroutine after it's been correctly oriented/positioned.
        var helmetInstance = FHelmetItemInstance?.GetValue(__instance) as GameObject;
        if (helmetInstance != null)
        {
            foreach (var r in helmetInstance.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }

        __instance.StartCoroutine(ScaleHelmetAfterAttach(__instance, marker));
    }

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetChestItem))]
    [HarmonyPrefix]
    private static void SetChestItem_Prefix(VisEquipment __instance, ref string name)
    {
        if (!ShouldSwap() || !IsCharredMelee(__instance.gameObject)) return;
        var target = Plugin.CharredWarriorChestName?.Value ?? "";
        if (string.IsNullOrEmpty(target)) return;

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (!marker.ChestSwapped)
            marker.OriginalChestItem = name;

        name = target;
        marker.ChestSwapped = true;
        HideBodyVisuals(__instance, true);
        __instance.StartCoroutine(RemapArmorBones(__instance, FChestItemInstances, target, Plugin.CharredWarriorChestScale?.Value ?? 1f));
    }

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetLegItem))]
    [HarmonyPrefix]
    private static void SetLegItem_Prefix(VisEquipment __instance, ref string name)
    {
        if (!ShouldSwap() || !IsCharredMelee(__instance.gameObject)) return;
        var target = Plugin.CharredWarriorLegsName?.Value ?? "";
        if (string.IsNullOrEmpty(target)) return;

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (!marker.LegsSwapped)
            marker.OriginalLegItem = name;

        name = target;
        marker.LegsSwapped = true;
        HideBodyVisuals(__instance, true);
        __instance.StartCoroutine(RemapArmorBones(__instance, FLegItemInstances, target, Plugin.CharredWarriorLegsScale?.Value ?? 1f));
    }

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetShoulderItem))]
    [HarmonyPrefix]
    private static void SetShoulderItem_Prefix(VisEquipment __instance, ref string name)
    {
        if (!ShouldSwap() || !IsCharredMelee(__instance.gameObject)) return;
        var target = Plugin.CharredWarriorShoulderName?.Value ?? "";
        if (string.IsNullOrEmpty(target)) return;

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (!marker.ShoulderSwapped)
            marker.OriginalShoulderItem = name;

        name = target;
        marker.ShoulderSwapped = true;
        __instance.StartCoroutine(RemapArmorBones(__instance, FShoulderItemInstances, target, Plugin.CharredWarriorCapeScale?.Value ?? 1f));
    }

    /// <summary>
    /// Maps player skeleton bone names to Charred_Melee equivalents where they differ.
    /// Populated from diagnostic bone dumps. Empty entries mean names match directly.
    /// </summary>
    private static readonly Dictionary<string, string> PlayerToCharredBoneMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Will be populated after first diagnostic dump shows mismatched names.
        // Example: { "PlayerBoneName", "CharredBoneName" }
    };

    // Blender-retargeted bind poses for Padded_Cuirrass → Charred_Melee skeleton.
    // Computed by mapping Player bone rest transforms to Charred bone orientations,
    // preserving original mesh vertices and bone weights.
    private static readonly Dictionary<string, Matrix4x4> s_chestRetargetedBPs = new()
    {
            { "Hips", M4(0.17656492f, 0f, 0f, 2.590326e-05f, 0f, 0f, 0.18557829f, 0.0042964648f, 0f, -0.17656492f, 0f, -0.00017281967f, 0f, 0f, 0f, 1f) },
            { "Spine", M4(0.17656492f, -4.1997971e-08f, 3.023124e-09f, 2.5903264e-05f, 0f, 0.01206405f, 0.18514462f, 0.0026450527f, -4.2096364e-08f, -0.17615232f, 0.012679905f, 7.9258389e-06f, 0f, 0f, 0f, 1f) },
            { "Spine1", M4(0.17656493f, -4.1608317e-08f, 6.7178609e-09f, 2.5903269e-05f, 0f, 0.026808234f, 0.18342678f, 8.7906454e-05f, -4.2096378e-08f, -0.17451793f, 0.028176755f, 1.5359392e-05f, 0f, 0f, 0f, 1f) },
            { "Spine2", M4(0.17656493f, 0f, 0f, 2.5903264e-05f, 0f, -0.0036778736f, 0.18553805f, -0.0019825485f, 0f, -0.17652665f, -0.003865618f, 0.00036255465f, 0f, 0f, 0f, 1f) },
            { "Neck", M4(0.17564972f, -0.017837185f, -0.002150676f, 0.00010027509f, -3.4483894e-10f, -0.020122949f, 0.18436913f, -0.0039352402f, -0.017954167f, -0.17450526f, -0.021040557f, 0.00072627614f, 0f, 0f, 0f, 1f) },
            { "Head", M4(-3.5057676e-07f, 0.17656493f, -1.1496297e-08f, -7.130405e-05f, 1.1568933e-10f, 5.8197727e-09f, 0.18557833f, -0.0058074393f, 0.17656495f, 3.4858417e-07f, -7.9499612e-10f, 2.5903119e-05f, 0f, 0f, 0f, 1f) },
            { "Jaw", M4(0.17656498f, 6.4622901e-07f, -1.7941648e-08f, 2.5904677e-05f, 5.0939047e-07f, -0.1458036f, -0.10466507f, 0.0031492882f, -3.4766151e-07f, 0.099581555f, -0.15324666f, 0.0050407141f, 0f, 0f, 0f, 1f) },
            { "LeftShoulder", M4(0.066344686f, 0.16355032f, 0.0052403905f, 0.00082161842f, -0.16359012f, 0.066412307f, -0.001774186f, 3.1966854e-05f, -0.0034389591f, -0.0039852182f, 0.18549581f, -0.0035254369f, 0f, 0f, 0f, 1f) },
            { "LeftArm", M4(0.024250135f, 0.1748753f, 0.002520991f, 9.8171739e-05f, -0.17171815f, 0.024267055f, -0.03484574f, -0.0024704614f, -0.033165704f, 0.0022206921f, 0.18225999f, -0.0040087467f, 0f, 0f, 0f, 1f) },
            { "LeftForeArm", M4(-0.00057046168f, 0.17645662f, 0.0064729946f, -0.00095511787f, -0.17645651f, -0.00078461698f, 0.0064482028f, -0.0071129361f, 0.0061586127f, -0.0061350148f, 0.1853532f, -0.0024496831f, 0f, 0f, 0f, 1f) },
            { "LeftHand", M4(3.2271139e-08f, 0.17656499f, 3.5949125e-08f, -0.00082033611f, -0.17656481f, -9.0442442e-10f, 4.6590685e-08f, -0.010955139f, 3.7481751e-08f, -4.2096197e-08f, 0.18557826f, -0.0028637734f, 0f, 0f, 0f, 1f) },
            { "LeftHandThumb1", M4(0.073515519f, -0.1407301f, 0.081180602f, 0.0037553059f, -0.11068653f, -0.10596561f, -0.09219873f, -0.0056371391f, 0.11627159f, -0.011895564f, -0.13909842f, 0.0096249823f, 0f, 0f, 0f, 1f) },
            { "LeftHandThumb2", M4(-0.012171932f, -0.17608528f, 0.0048162774f, -0.001031668f, -0.14228833f, 0.0071214614f, -0.10962415f, -0.0081178313f, 0.1038316f, -0.010882924f, -0.14966168f, 0.008928421f, 0f, 0f, 0f, 1f) },
            { "LeftHandThumb3", M4(-0.043231487f, -0.16710931f, -0.039054457f, -0.0027365782f, -0.13636716f, 0.056784555f, -0.10165676f, -0.0084604248f, 0.10348991f, 0.0050166319f, -0.15026616f, 0.0089219566f, 0f, 0f, 0f, 1f) },
            { "LeftHandIndex1", M4(-0.0082416739f, 0.17636988f, -0.0010184553f, -0.0009125974f, -0.1753719f, -0.0082980152f, -0.019690963f, -0.012129877f, -0.018759422f, 8.7940331e-05f, 0.18452783f, -0.0043633468f, 0f, 0f, 0f, 1f) },
            { "LeftHandIndex2", M4(-0.0071842424f, 0.17641874f, 8.695919e-05f, -0.00085246185f, -0.16200855f, -0.0065646833f, -0.073462836f, -0.010762778f, -0.06983383f, -0.0029198865f, 0.17041859f, -0.0079007633f, 0f, 0f, 0f, 1f) },
            { "LeftHandIndex3", M4(-0.013599807f, 0.17603897f, -0.00076463324f, -0.0013239705f, -0.15908593f, -0.01260238f, -0.079408132f, -0.010789008f, -0.075378165f, -0.0051638461f, 0.16772914f, -0.0082748225f, 0f, 0f, 0f, 1f) },
            { "LeftHandMiddle1", M4(-0.0026394085f, 0.17653205f, 0.0022709887f, -0.00093558047f, -0.17573635f, -0.0024205528f, -0.017775225f, -0.012239078f, -0.016879125f, -0.0024033736f, 0.18471108f, -0.0042110141f, 0f, 0f, 0f, 1f) },
            { "LeftHandMiddle2", M4(-0.0044233711f, 0.17650764f, 0.00087409234f, -0.0010465565f, -0.16133648f, -0.0037056797f, -0.075294577f, -0.010847722f, -0.071596839f, -0.0025546267f, 0.16961506f, -0.0080616372f, 0f, 0f, 0f, 1f) },
            { "LeftHandMiddle3", M4(-0.0021175449f, 0.1765523f, -4.6805817e-05f, -0.00085547025f, -0.14343689f, -0.0017463545f, -0.10820156f, -0.0093878359f, -0.10293932f, -0.0011984868f, 0.15077038f, -0.010192378f, 0f, 0f, 0f, 1f) },
            { "LeftHandRing1", M4(0.0098028062f, 0.17626409f, 0.0033342249f, -0.00043389233f, -0.17602077f, 0.0099624209f, -0.010112656f, -0.012251636f, -0.0097840922f, -0.0026283397f, 0.18527253f, -0.0035381767f, 0f, 0f, 0f, 1f) },
            { "LeftHandRing2", M4(-0.0027522412f, 0.17654303f, -0.00045283281f, -0.0012962845f, -0.16032645f, -0.0026798442f, -0.077688016f, -0.010524345f, -0.073912099f, -0.00076096703f, 0.16853391f, -0.0079857297f, 0f, 0f, 0f, 1f) },
            { "LeftHandRing3", M4(0.0028923689f, 0.17651697f, 0.003081925f, -0.00092439773f, -0.14887173f, 0.004014953f, -0.099690311f, -0.0097348848f, -0.094889283f, -0.0009186145f, 0.15649804f, -0.0093821976f, 0f, 0f, 0f, 1f) },
            { "LeftHandPinky1", M4(0.035780501f, 0.17272399f, -0.0082337121f, 0.00129515f, -0.16212445f, 0.030735716f, -0.066024899f, -0.010419197f, -0.060087882f, 0.01992308f, 0.17324032f, -0.0067569087f, 0f, 0f, 0f, 1f) },
            { "LeftHandPinky2", M4(0.029760849f, 0.17366096f, -0.012046325f, 0.00091455504f, -0.12598887f, 0.013475729f, -0.12924208f, -0.0072450498f, -0.12006763f, 0.028904548f, 0.1326298f, -0.010510717f, 0f, 0f, 0f, 1f) },
            { "LeftHandPinky3", M4(0.020484952f, 0.17490983f, -0.013382586f, 0.00025712716f, -0.11360734f, 0.0034695293f, -0.14201418f, -0.0063659111f, -0.13359977f, 0.023868695f, 0.11871053f, -0.011263974f, 0f, 0f, 0f, 1f) },
            { "RightShoulder", M4(0.066344686f, -0.16355032f, -0.0052403905f, -0.00080215203f, 0.16359012f, 0.066412307f, -0.001774186f, 7.9966383e-05f, 0.0034389591f, -0.0039852182f, 0.18549581f, -0.0035244275f, 0f, 0f, 0f, 1f) },
            { "RightArm", M4(0.024250131f, -0.17487529f, -0.0025209901f, -9.1056376e-05f, 0.1717182f, 0.024267059f, -0.034845747f, -0.0024200773f, 0.033165712f, 0.0022206916f, 0.18226005f, -0.0039990172f, 0f, 0f, 0f, 1f) },
            { "RightForeArm", M4(-0.00057047518f, -0.1764566f, -0.006472995f, 0.00095495093f, 0.17645657f, -0.00078461098f, 0.0064482079f, -0.0070611644f, -0.0061586211f, -0.0061350148f, 0.18535328f, -0.0024514964f, 0f, 0f, 0f, 1f) },
            { "RightHand", M4(1.8848834e-08f, -0.17656496f, -3.5749299e-08f, 0.00082033616f, 0.17656495f, 5.1181517e-09f, 4.9584251e-08f, -0.01090334f, -4.272842e-08f, -4.339886e-08f, 0.18557829f, -0.002863779f, 0f, 0f, 0f, 1f) },
            { "RightHandThumb1", M4(0.073515616f, 0.14073013f, -0.08118064f, -0.0037337372f, 0.11068659f, -0.10596561f, -0.09219873f, -0.0056046615f, -0.11627166f, -0.011895563f, -0.1390985f, 0.0095908782f, 0f, 0f, 0f, 1f) },
            { "RightHandThumb2", M4(-0.012171896f, 0.17608528f, -0.0048163021f, 0.0010280946f, 0.14228842f, 0.007121494f, -0.10962419f, -0.008076082f, -0.10383168f, -0.010882926f, -0.14966173f, 0.0088979648f, 0f, 0f, 0f, 1f) },
            { "RightHandThumb3", M4(-0.043231472f, 0.16710936f, 0.039054461f, 0.0027238908f, 0.13636731f, 0.056784604f, -0.10165687f, -0.0084204171f, -0.10349001f, 0.0050166356f, -0.15026626f, 0.0088916058f, 0f, 0f, 0f, 1f) },
            { "RightHandIndex1", M4(-0.0082416944f, -0.17636986f, 0.0010184556f, 0.00091017969f, 0.17537203f, -0.0082980087f, -0.019690964f, -0.012078424f, 0.018759435f, 8.7939647e-05f, 0.18452789f, -0.004357852f, 0f, 0f, 0f, 1f) },
            { "RightHandIndex2", M4(-0.0071842624f, -0.17641875f, -8.6958928e-05f, 0.00085035467f, 0.16200867f, -0.0065646763f, -0.073462866f, -0.010715244f, 0.069833875f, -0.0029198844f, 0.17041865f, -0.0078802835f, 0f, 0f, 0f, 1f) },
            { "RightHandIndex3", M4(-0.013599831f, -0.17603895f, 0.00076463242f, 0.0013199811f, 0.159086f, -0.012602371f, -0.079408146f, -0.010742332f, 0.075378209f, -0.0051638447f, 0.16772915f, -0.0082527176f, 0f, 0f, 0f, 1f) },
            { "RightHandMiddle1", M4(-0.0026394241f, -0.17653205f, -0.0022709896f, 0.00093480636f, 0.17573649f, -0.0024205465f, -0.017775226f, -0.012187522f, 0.016879132f, -0.0024033741f, 0.18471113f, -0.0042060697f, 0f, 0f, 0f, 1f) },
            { "RightHandMiddle2", M4(-0.0044233883f, -0.17650761f, -0.00087409298f, 0.0010452592f, 0.1613366f, -0.0037056734f, -0.075294584f, -0.010800388f, 0.071596876f, -0.002554625f, 0.16961507f, -0.0080406396f, 0f, 0f, 0f, 1f) },
            { "RightHandMiddle3", M4(-0.0021175607f, -0.17655228f, 4.6804398e-05f, 0.00085484958f, 0.14343703f, -0.0017463488f, -0.10820161f, -0.0093457513f, 0.1029394f, -0.0011984844f, 0.15077044f, -0.010162189f, 0f, 0f, 0f, 1f) },
            { "RightHandRing1", M4(0.0098027997f, -0.17626408f, -0.0033342254f, 0.00043676817f, 0.17602089f, 0.0099624237f, -0.010112655f, -0.012199995f, 0.0097840922f, -0.0026283404f, 0.18527254f, -0.0035353131f, 0f, 0f, 0f, 1f) },
            { "RightHandRing2", M4(-0.0027522561f, -0.17654303f, 0.00045283412f, 0.0012954772f, 0.16032659f, -0.0026798386f, -0.077688046f, -0.010477309f, 0.073912159f, -0.00076096546f, 0.16853392f, -0.0079640513f, 0f, 0f, 0f, 1f) },
            { "RightHandRing3", M4(0.0028923585f, -0.17651697f, -0.0030819247f, 0.00092524657f, 0.14887187f, 0.0040149591f, -0.099690348f, -0.0096912095f, 0.094889365f, -0.00091861218f, 0.15649809f, -0.0093543632f, 0f, 0f, 0f, 1f) },
            { "RightHandPinky1", M4(0.035780516f, -0.17272398f, 0.0082337148f, -0.0012846532f, 0.16212457f, 0.030735718f, -0.066024922f, -0.010371632f, 0.06008793f, 0.01992308f, 0.17324035f, -0.0067392872f, 0f, 0f, 0f, 1f) },
            { "RightHandPinky2", M4(0.029760856f, -0.17366093f, 0.012046328f, -0.00090582378f, 0.12598895f, 0.01347573f, -0.12924209f, -0.0072080814f, 0.12006772f, 0.028904552f, 0.13262984f, -0.0104755f, 0f, 0f, 0f, 1f) },
            { "RightHandPinky3", M4(0.020484958f, -0.17490982f, 0.013382588f, -0.0002511178f, 0.11360737f, 0.0034695286f, -0.14201418f, -0.0063325693f, 0.13359986f, 0.023868695f, 0.11871056f, -0.011224785f, 0f, 0f, 0f, 1f) },
            { "LeftUpLeg", M4(0.17656492f, 0f, 0f, 0.001924803f, 0f, 0.0005986357f, -0.18557723f, -0.004383733f, 0f, 0.1765639f, 0.00062919513f, 0.00023020229f, 0f, 0f, 0f, 1f) },
            { "LeftLeg", M4(0.17656353f, 0.00052702741f, -0.00048770101f, 0.0018949402f, -0.00052702759f, 0.022361932f, -0.1840831f, -0.011462824f, -0.00046401389f, 0.17514233f, 0.023504941f, 0.001650367f, 0f, 0f, 0f, 1f) },
            { "LeftFoot", M4(0.17656495f, 3.6438184e-08f, -8.0360408e-08f, 0.001942436f, 0f, -0.15940769f, -0.079799101f, -0.0069944705f, -8.4921567e-08f, 0.075923331f, -0.16754524f, -0.015982036f, 0f, 0f, 0f, 1f) },
            { "LeftToeBase", M4(0.17656495f, 0f, 0f, 0.0019424437f, 0f, -0.17656492f, -5.6498466e-05f, -0.0012867933f, -2.1791109e-10f, 5.3764914e-05f, -0.18557832f, -0.01831203f, 0f, 0f, 0f, 1f) },
            { "RightUpLeg", M4(0.17656492f, 0f, 0f, -0.0018729967f, 0f, 0.0005986357f, -0.18557723f, -0.004383733f, 0f, 0.1765639f, 0.00062919513f, 0.00023020229f, 0f, 0f, 0f, 1f) },
            { "RightLeg", M4(0.17656353f, -0.00052702741f, 0.00048770101f, -0.0018431342f, 0.00052702759f, 0.022361932f, -0.1840831f, -0.01146267f, 0.00046401389f, 0.17514233f, 0.023504941f, 0.001650503f, 0f, 0f, 0f, 1f) },
            { "RightFoot", M4(0.17656492f, -3.6363947e-08f, 8.0397555e-08f, -0.0018906289f, 1.0236612e-10f, -0.15940769f, -0.079799108f, -0.0069944705f, 8.4895696e-08f, 0.075923324f, -0.16754524f, -0.015982036f, 0f, 0f, 0f, 1f) },
            { "RightToeBase", M4(0.17656492f, 0f, 0f, -0.001890637f, 0f, -0.1765649f, -5.6509529e-05f, -0.0012867943f, 2.333001e-10f, 5.3775435e-05f, -0.18557832f, -0.018312031f, 0f, 0f, 0f, 1f) },
    };

    private static Matrix4x4 M4(
        float m00, float m01, float m02, float m03,
        float m10, float m11, float m12, float m13,
        float m20, float m21, float m22, float m23,
        float m30, float m31, float m32, float m33)
    {
        return new Matrix4x4(
            new Vector4(m00, m10, m20, m30),
            new Vector4(m01, m11, m21, m31),
            new Vector4(m02, m12, m22, m32),
            new Vector4(m03, m13, m23, m33));
    }

    private static bool _armorBoneDumpDone;
    private static bool _bindPoseDiagDone;

    private static Dictionary<string, Transform> BuildCharredBoneMap(VisEquipment vis)
    {
        var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        if (vis.m_bodyModel != null)
        {
            foreach (var bone in vis.m_bodyModel.bones)
                if (bone != null) map[bone.name] = bone;
        }
        void Collect(Transform t)
        {
            if (!map.ContainsKey(t.name)) map[t.name] = t;
            for (var i = 0; i < t.childCount; i++)
                Collect(t.GetChild(i));
        }
        Collect(vis.transform);
        return map;
    }

    /// <summary>
    /// Reads the original bone names from the armor prefab's attach_skin SMRs.
    /// These are the bone names the mesh was authored for (player skeleton), before
    /// vanilla AttachArmor overwrites them with the character's m_bodyModel.bones.
    /// Returns a dict keyed by SMR name -> ordered bone name array.
    /// </summary>
    private static Dictionary<string, string[]>? GetPrefabArmorBoneInfo(string armorItemName)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(armorItemName);
        if (prefab == null) return null;

        var result = new Dictionary<string, string[]>();
        for (var i = 0; i < prefab.transform.childCount; i++)
        {
            var child = prefab.transform.GetChild(i);
            if (!child.gameObject.name.StartsWith("attach_skin", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var smr in child.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var boneNames = new string[smr.bones.Length];
                for (var b = 0; b < smr.bones.Length; b++)
                    boneNames[b] = smr.bones[b] != null ? smr.bones[b].name : "";
                result[smr.name] = boneNames;
            }
        }
        return result.Count > 0 ? result : null;
    }

    private static void DumpArmorBoneMapping(string armorItemName, Dictionary<string, string[]> prefabBoneInfo,
                                              Dictionary<string, Transform> charBoneMap)
    {
        if (_armorBoneDumpDone) return;
        _armorBoneDumpDone = true;

        var sb = new StringBuilder();
        sb.AppendLine("=== ARMOR BONE MAPPING DUMP ===");
        sb.AppendLine($"Armor: '{armorItemName}'");

        sb.AppendLine();
        sb.AppendLine("--- Charred_Melee available bones ---");
        foreach (var kvp in charBoneMap)
            sb.AppendLine($"  '{kvp.Key}'");

        foreach (var kvp in prefabBoneInfo)
        {
            sb.AppendLine();
            sb.AppendLine($"--- Armor SMR '{kvp.Key}' expected bones ({kvp.Value.Length}) ---");
            var missing = 0;
            for (var i = 0; i < kvp.Value.Length; i++)
            {
                var boneName = kvp.Value[i];
                var found = charBoneMap.ContainsKey(boneName);
                string? alt = null;
                var mapped = !found && PlayerToCharredBoneMap.TryGetValue(boneName, out alt) && charBoneMap.ContainsKey(alt!);
                var status = found ? "OK" : mapped ? $"MAPPED->{alt}" : "MISSING";
                sb.AppendLine($"  [{i}] '{boneName}' -> {status}");
                if (!found && !mapped) missing++;
            }
            sb.AppendLine($"  Total: {kvp.Value.Length}, Missing: {missing}");
        }

        sb.AppendLine("=== END ARMOR BONE MAPPING DUMP ===");
        Plugin.Log?.LogInfo(sb.ToString());
    }

    private static readonly string[] DiagBones =
    {
        "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head",
        "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
        "RightShoulder", "RightArm", "RightForeArm", "RightHand",
        "LeftUpLeg", "LeftLeg", "LeftFoot",
        "RightUpLeg", "RightLeg", "RightFoot"
    };

    private static void DumpBindPoseDiagnostic(
        VisEquipment vis,
        string armorItemName,
        Dictionary<string, Matrix4x4>? playerBPMap,
        Dictionary<string, Matrix4x4> charredBPMap,
        Dictionary<string, Transform> charBoneMap,
        SkinnedMeshRenderer armorSMR)
    {
        if (_bindPoseDiagDone) return;
        _bindPoseDiagDone = true;

        var sb = new StringBuilder();
        sb.AppendLine("=== BIND-POSE DIAGNOSTIC DUMP ===");
        sb.AppendLine($"Armor: '{armorItemName}', SMR: '{armorSMR.name}'");
        sb.AppendLine($"Charred m_bodyModel: '{vis.m_bodyModel?.name ?? "NULL"}', bones={vis.m_bodyModel?.bones.Length ?? 0}, rootBone='{vis.m_bodyModel?.rootBone?.name ?? "NULL"}'");

        // --- Phase 1: Per-bone bind-pose comparison ---
        sb.AppendLine();
        sb.AppendLine("--- Per-bone bind-pose comparison (Player prefab vs Charred body) ---");
        sb.AppendLine("Format: bone | playerPos | charredPos | posDelta | playerRotEuler | charredRotEuler | rotDelta(deg) | playerScale | charredScale");

        foreach (var boneName in DiagBones)
        {
            Matrix4x4 pBP = Matrix4x4.identity, cBP = Matrix4x4.identity;
            bool hasPlayer = playerBPMap != null && playerBPMap.TryGetValue(boneName, out pBP);
            bool hasCharred = charredBPMap.TryGetValue(boneName, out cBP);

            if (!hasPlayer && !hasCharred)
            {
                sb.AppendLine($"  {boneName}: MISSING from both");
                continue;
            }

            var pPos = hasPlayer ? (Vector3)pBP.inverse.GetColumn(3) : Vector3.zero;
            var cPos = hasCharred ? (Vector3)cBP.inverse.GetColumn(3) : Vector3.zero;
            var pRot = hasPlayer ? pBP.rotation.eulerAngles : Vector3.zero;
            var cRot = hasCharred ? cBP.rotation.eulerAngles : Vector3.zero;
            var pScale = hasPlayer ? pBP.lossyScale : Vector3.one;
            var cScale = hasCharred ? cBP.lossyScale : Vector3.one;

            float posDelta = (hasPlayer && hasCharred) ? Vector3.Distance(pPos, cPos) : -1;
            float rotDelta = (hasPlayer && hasCharred) ? Quaternion.Angle(pBP.rotation, cBP.rotation) : -1;

            sb.AppendLine($"  {boneName}:");
            if (hasPlayer)
                sb.AppendLine($"    Player:  pos=({pPos.x:F5},{pPos.y:F5},{pPos.z:F5})  rot=({pRot.x:F1},{pRot.y:F1},{pRot.z:F1})  scale=({pScale.x:F4},{pScale.y:F4},{pScale.z:F4})");
            else
                sb.AppendLine($"    Player:  MISSING");
            if (hasCharred)
                sb.AppendLine($"    Charred: pos=({cPos.x:F5},{cPos.y:F5},{cPos.z:F5})  rot=({cRot.x:F1},{cRot.y:F1},{cRot.z:F1})  scale=({cScale.x:F4},{cScale.y:F4},{cScale.z:F4})");
            else
                sb.AppendLine($"    Charred: MISSING");
            if (hasPlayer && hasCharred)
                sb.AppendLine($"    Delta:   posDist={posDelta:F5}  rotAngle={rotDelta:F1}deg");
        }

        // --- Phase 2: Charred skeleton hierarchy ---
        sb.AppendLine();
        sb.AppendLine("--- Charred skeleton hierarchy (parent chain + local transforms) ---");

        // Root transform chain first
        sb.AppendLine("  Transform chain from root:");
        var rootChain = new List<Transform>();
        var cursor = vis.transform;
        while (cursor != null) { rootChain.Add(cursor); cursor = cursor.parent; }
        rootChain.Reverse();
        foreach (var t in rootChain)
            sb.AppendLine($"    '{t.name}' localPos={t.localPosition} localRot={t.localEulerAngles} localScale={t.localScale}");

        // Armature/Root chain
        var armature = FindInChildren(vis.transform, "Armature");
        if (armature != null)
        {
            sb.AppendLine($"  Armature -> root chain:");
            var ac = armature;
            for (int depth = 0; depth < 5 && ac != null; depth++)
            {
                sb.AppendLine($"    '{ac.name}' localPos={ac.localPosition} localRot={ac.localEulerAngles} localScale={ac.localScale} worldScale={ac.lossyScale}");
                ac = ac.childCount > 0 ? ac.GetChild(0) : null;
            }
        }

        // Per diagnostic bone: parent chain + local transform
        sb.AppendLine("  Per-bone parent chains:");
        foreach (var boneName in DiagBones)
        {
            if (!charBoneMap.TryGetValue(boneName, out var bone)) continue;
            var chain = new List<string>();
            var p = bone;
            while (p != null && p != vis.transform)
            {
                chain.Add(p.name);
                p = p.parent;
            }
            chain.Reverse();
            sb.AppendLine($"    {boneName}: chain=[{string.Join(" > ", chain)}]  localPos={bone.localPosition}  localRot={bone.localEulerAngles}  localScale={bone.localScale}");
        }

        // --- Phase 4: Vanilla AttachArmor state (BEFORE our remap) ---
        sb.AppendLine();
        sb.AppendLine("--- Vanilla AttachArmor state (armor SMR as vanilla left it) ---");
        sb.AppendLine($"  SMR '{armorSMR.name}': bones={armorSMR.bones.Length}, rootBone='{armorSMR.rootBone?.name ?? "NULL"}'");
        sb.AppendLine($"  SMR transform: localPos={armorSMR.transform.localPosition}, localRot={armorSMR.transform.localEulerAngles}, localScale={armorSMR.transform.localScale}");
        sb.AppendLine($"  SMR localToWorld row0=({armorSMR.transform.localToWorldMatrix.GetRow(0)})");
        sb.AppendLine($"  SMR localToWorld row1=({armorSMR.transform.localToWorldMatrix.GetRow(1)})");
        sb.AppendLine($"  SMR localToWorld row2=({armorSMR.transform.localToWorldMatrix.GetRow(2)})");
        sb.AppendLine($"  SMR localToWorld row3=({armorSMR.transform.localToWorldMatrix.GetRow(3)})");

        sb.AppendLine($"  Vanilla bone array ({armorSMR.bones.Length} entries):");
        for (int i = 0; i < armorSMR.bones.Length && i < 53; i++)
        {
            var b = armorSMR.bones[i];
            sb.AppendLine($"    [{i}] '{b?.name ?? "NULL"}'  worldPos={b?.position.ToString() ?? "N/A"}");
        }

        var vanillaBPs = armorSMR.sharedMesh?.bindposes;
        if (vanillaBPs != null)
        {
            sb.AppendLine($"  Vanilla mesh bind poses ({vanillaBPs.Length} entries, showing diag bones):");
            for (int i = 0; i < armorSMR.bones.Length && i < vanillaBPs.Length; i++)
            {
                var b = armorSMR.bones[i];
                if (b == null) continue;
                bool isDiag = Array.IndexOf(DiagBones, b.name) >= 0;
                if (!isDiag) continue;
                var bp = vanillaBPs[i];
                var pos = (Vector3)bp.inverse.GetColumn(3);
                var rot = bp.rotation.eulerAngles;
                sb.AppendLine($"    [{i}] '{b.name}' pos=({pos.x:F5},{pos.y:F5},{pos.z:F5}) rot=({rot.x:F1},{rot.y:F1},{rot.z:F1})");
            }
        }

        sb.AppendLine("=== END BIND-POSE DIAGNOSTIC DUMP ===");
        Plugin.Log?.LogInfo(sb.ToString());
    }

    /// <summary>
    /// Returns the original (prefab) mesh for a given armor item's SMR by name.
    /// Used to avoid scale stacking when cloning meshes for bind-pose scaling.
    /// </summary>
    private static Mesh? GetPrefabArmorMesh(string armorItemName, string smrName)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(armorItemName);
        if (prefab == null) return null;

        Mesh? fallback = null;
        for (var i = 0; i < prefab.transform.childCount; i++)
        {
            var child = prefab.transform.GetChild(i);
            if (!child.gameObject.name.StartsWith("attach_skin", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var smr in child.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (string.Equals(smr.name, smrName, StringComparison.OrdinalIgnoreCase))
                    return smr.sharedMesh;
                fallback ??= smr.sharedMesh;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Returns bone-name -> bind-pose matrix from the armor prefab's original SMR.
    /// These are the PLAYER's bind poses before vanilla overwrites the bone array.
    /// </summary>
    private static Dictionary<string, Matrix4x4>? GetPrefabArmorBindPoses(string armorItemName, string smrName)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(armorItemName);
        if (prefab == null) return null;

        SkinnedMeshRenderer? target = null;
        SkinnedMeshRenderer? fallback = null;

        for (var i = 0; i < prefab.transform.childCount; i++)
        {
            var child = prefab.transform.GetChild(i);
            if (!child.gameObject.name.StartsWith("attach_skin", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var smr in child.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (string.Equals(smr.name, smrName, StringComparison.OrdinalIgnoreCase))
                { target = smr; break; }
                fallback ??= smr;
            }
            if (target != null) break;
        }

        var use = target ?? fallback;
        if (use == null) return null;

        var bones = use.bones;
        var bps = use.sharedMesh.bindposes;
        var map = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bones.Length && i < bps.Length; i++)
            if (bones[i] != null) map[bones[i].name] = bps[i];
        return map.Count > 0 ? map : null;
    }

    /// <summary>
    /// Returns bone-name -> bind-pose matrix from the Charred's body model.
    /// These encode how the Charred's mesh was authored relative to its skeleton.
    /// </summary>
    private static Dictionary<string, Matrix4x4> BuildCharredBindPoseMap(VisEquipment vis)
    {
        var map = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);

        // Collect from m_bodyModel first (primary source)
        if (vis.m_bodyModel != null && vis.m_bodyModel.sharedMesh != null)
        {
            var bones = vis.m_bodyModel.bones;
            var bps = vis.m_bodyModel.sharedMesh.bindposes;
            for (int i = 0; i < bones.Length && i < bps.Length; i++)
                if (bones[i] != null) map[bones[i].name] = bps[i];
        }

        // Collect from native Charred SMRs only (Eyes, Sinew, Skull) — NOT from armor
        // SMRs (under attach_skin nodes) which may have player or already-remapped bind poses.
        foreach (var smr in vis.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr == vis.m_bodyModel || smr.sharedMesh == null) continue;
            if (IsUnderAttachSkin(smr.transform)) continue;
            var bones = smr.bones;
            var bps = smr.sharedMesh.bindposes;
            for (int i = 0; i < bones.Length && i < bps.Length; i++)
                if (bones[i] != null && !map.ContainsKey(bones[i].name))
                    map[bones[i].name] = bps[i];
        }

        return map;
    }

    private static bool IsUnderAttachSkin(Transform t)
    {
        var parent = t.parent;
        while (parent != null)
        {
            if (parent.name.StartsWith("attach_skin", StringComparison.OrdinalIgnoreCase) ||
                parent.name.StartsWith("attach(", StringComparison.OrdinalIgnoreCase))
                return true;
            parent = parent.parent;
        }
        return false;
    }

    /// <summary>
    /// Computes the scale correction factor needed when replacing player bind poses
    /// with Charred bind poses. The Charred may be modeled at a different internal
    /// scale (e.g. 0.1x with a 10x root transform to compensate). Player mesh
    /// vertices are at the player's internal scale and must be shrunk to match.
    ///
    /// Returns charredDist / playerDist — post-multiply onto bind poses:
    ///   correctedBP = charredBP * Scale(correction)
    /// </summary>
    private static float ComputeBindPoseScaleCorrection(
        Dictionary<string, Matrix4x4> playerBPMap,
        Dictionary<string, Matrix4x4> charredBPMap)
    {
        if (!playerBPMap.TryGetValue("Hips", out var pHipsBP) ||
            !playerBPMap.TryGetValue("Head", out var pHeadBP) ||
            !charredBPMap.TryGetValue("Hips", out var cHipsBP) ||
            !charredBPMap.TryGetValue("Head", out var cHeadBP))
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] Could not find Hips/Head in both skeletons for auto-scale. Using 1.0.");
            return 1f;
        }

        Vector3 pHipsPos = pHipsBP.inverse.GetColumn(3);
        Vector3 pHeadPos = pHeadBP.inverse.GetColumn(3);
        Vector3 cHipsPos = cHipsBP.inverse.GetColumn(3);
        Vector3 cHeadPos = cHeadBP.inverse.GetColumn(3);

        float playerDist = Vector3.Distance(pHipsPos, pHeadPos);
        float charredDist = Vector3.Distance(cHipsPos, cHeadPos);

        if (playerDist < 0.001f || charredDist < 0.001f)
        {
            Plugin.Log?.LogWarning($"[Ashlands Reborn] Degenerate spine distance: player={playerDist:F4} charred={charredDist:F4}. Using 1.0.");
            return 1f;
        }

        float correction = charredDist / playerDist;
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Auto-scale correction: playerSpine={playerDist:F4}, charredSpine={charredDist:F4}, ratio={correction:F4}");
        return correction;
    }

    private static System.Collections.IEnumerator RemapArmorBones(
        VisEquipment vis, FieldInfo? instanceField, string armorItemName, float userScale)
    {
        var marker = vis.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null) yield break;

        // Yield one frame so vanilla UpdateEquipmentVisuals can process the item
        // change (destroy old instances, create new ones via AttachArmor). Without
        // this, the coroutine picks up stale instances from before the swap.
        yield return null;

        // Wait for new armor instances to appear
        List<GameObject>? instances = null;
        for (int i = 0; i < 30; i++)
        {
            if (vis == null) yield break;
            instances = instanceField?.GetValue(vis) as List<GameObject>;
            if (instances != null && instances.Count > 0) break;
            yield return null;
        }

        if (instances == null || instances.Count == 0 || vis == null) yield break;

        var bodySMR = vis.m_bodyModel;
        if (bodySMR == null) yield break;

        var bodyRoot = bodySMR.rootBone;

        // Name -> Transform map for the Charred's full hierarchy
        var charBoneMap = BuildCharredBoneMap(vis);

        // Bone-name -> bone-name array per SMR (for logging / bone-array ordering)
        var prefabBoneInfo = GetPrefabArmorBoneInfo(armorItemName);
        if (prefabBoneInfo == null)
        {
            Plugin.Log?.LogWarning($"[Ashlands Reborn] Could not read bone info from armor prefab '{armorItemName}'. Armor remap skipped.");
            yield break;
        }

        DumpArmorBoneMapping(armorItemName, prefabBoneInfo, charBoneMap);

        // Charred bind poses from all SMRs (keyed by bone name)
        var charredBPMap = BuildCharredBindPoseMap(vis);
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred bind-pose map: {charredBPMap.Count} bones from all SMRs for '{armorItemName}'");

        // --- Diagnostic dump (fires once) for chest debugging ---
        if (!_bindPoseDiagDone)
        {
            var playerBPMapDiag = GetPrefabArmorBindPoses(armorItemName, "");
            SkinnedMeshRenderer? firstSMR = null;
            foreach (var go in instances)
            {
                if (go == null) continue;
                firstSMR = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (firstSMR != null) break;
            }
            if (firstSMR != null)
                DumpBindPoseDiagnostic(vis, armorItemName, playerBPMapDiag, charredBPMap, charBoneMap, firstSMR);
        }

        var shoulderRot = Plugin.CharredWarriorShoulderRotation?.Value ?? 0f;

        foreach (var armorGo in instances)
        {
            if (armorGo == null) continue;
            if (marker.RemappedInstances.Contains(armorGo.GetInstanceID())) continue;

            // --- Hide LowerCloth (belt/loincloth) on ANY renderer type ---
            foreach (var renderer in armorGo.GetComponentsInChildren<Renderer>(true))
            {
                if (string.Equals(renderer.gameObject.name, "LowerCloth", StringComparison.OrdinalIgnoreCase))
                    renderer.enabled = false;
            }

            var smrs = armorGo.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr == null) continue;

                if (string.Equals(smr.gameObject.name, "LowerCloth", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[]? prefabBoneNames = null;
                if (prefabBoneInfo.TryGetValue(smr.name, out var names))
                    prefabBoneNames = names;
                else if (prefabBoneInfo.Count == 1)
                {
                    foreach (var v in prefabBoneInfo.Values) { prefabBoneNames = v; break; }
                }

                if (prefabBoneNames == null || prefabBoneNames.Length == 0)
                {
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] No prefab bone info for SMR '{smr.name}' in '{armorItemName}'. Skipping.");
                    continue;
                }

                // --- Player's original bind poses for scale computation ---
                var playerBPMap = GetPrefabArmorBindPoses(armorItemName, smr.name);

                // --- Auto-compute scale correction ---
                float autoCorrection = 1f;
                if (playerBPMap != null)
                    autoCorrection = ComputeBindPoseScaleCorrection(playerBPMap, charredBPMap);
                float totalScale = autoCorrection * userScale;
                var scaleMat = Matrix4x4.Scale(Vector3.one * totalScale);

                // --- Build bone array FIRST (needed for runtime bind pose computation) ---
                var newBones = new Transform[prefabBoneNames.Length];
                var missingCount = 0;
                for (int i = 0; i < prefabBoneNames.Length; i++)
                {
                    var boneName = prefabBoneNames[i];
                    if (charBoneMap.TryGetValue(boneName, out var t))
                        newBones[i] = t;
                    else if (PlayerToCharredBoneMap.TryGetValue(boneName, out var mapped) && charBoneMap.TryGetValue(mapped, out t))
                        newBones[i] = t;
                    else
                    {
                        newBones[i] = bodyRoot!;
                        missingCount++;
                    }
                }

                // --- Clone mesh from prefab (prevents stacking) and compute bind poses ---
                var prefabMesh = GetPrefabArmorMesh(armorItemName, smr.name);
                var baseMesh = prefabMesh ?? smr.sharedMesh;
                var newMesh = UObject.Instantiate(baseMesh);
                var originalBPs = newMesh.bindposes;
                var newBPs = new Matrix4x4[originalBPs.Length];

                bool isChest = (instanceField == FChestItemInstances);

                if (isChest)
                {
                    // Use Blender-retargeted bind poses — precomputed to correctly map
                    // Padded_Cuirrass vertices to the Charred_Melee skeleton's rest pose.
                    // These account for the ~177° arm bone orientation mismatch between
                    // Player and Charred skeletons that programmatic approaches couldn't fix.
                    for (int i = 0; i < prefabBoneNames.Length && i < originalBPs.Length; i++)
                    {
                        var boneName = prefabBoneNames[i];
                        newBPs[i] = s_chestRetargetedBPs.TryGetValue(boneName, out var bp)
                            ? bp
                            : originalBPs[i];
                    }
                    for (int i = prefabBoneNames.Length; i < originalBPs.Length; i++)
                        newBPs[i] = originalBPs[i];
                }
                else
                {
                    // Charred body bind poses + scale for legs/cape (proven working).
                    for (int i = 0; i < prefabBoneNames.Length && i < originalBPs.Length; i++)
                    {
                        var boneName = prefabBoneNames[i];
                        Matrix4x4 cBP;
                        if (charredBPMap.TryGetValue(boneName, out cBP) ||
                            (PlayerToCharredBoneMap.TryGetValue(boneName, out var mapped) &&
                             charredBPMap.TryGetValue(mapped, out cBP)))
                        {
                            newBPs[i] = cBP * scaleMat;
                        }
                        else
                        {
                            newBPs[i] = originalBPs[i] * scaleMat;
                        }
                    }
                    for (int i = prefabBoneNames.Length; i < originalBPs.Length; i++)
                        newBPs[i] = originalBPs[i] * scaleMat;
                }

                newMesh.bindposes = newBPs;

                smr.sharedMesh = newMesh;

                // --- SHOULDER ROTATION WRAPPERS ---
                if (Math.Abs(shoulderRot) > 0.01f)
                {
                    for (int i = 0; i < prefabBoneNames.Length; i++)
                    {
                        var bn = prefabBoneNames[i];
                        if (string.Equals(bn, "LeftShoulder", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(bn, "RightShoulder", StringComparison.OrdinalIgnoreCase))
                        {
                            var wrapper = new GameObject($"ShoulderAdjust_{bn}");
                            wrapper.transform.SetParent(newBones[i], false);
                            wrapper.transform.localRotation = Quaternion.Euler(0, 0, shoulderRot);
                            newBones[i] = wrapper.transform;
                            marker.SyncedObjects.Add(wrapper);
                        }
                    }
                }

                smr.bones = newBones;
                if (bodyRoot != null)
                    smr.rootBone = bodyRoot;

                if (missingCount > 0)
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] Armor '{armorItemName}' SMR '{smr.name}': {missingCount}/{prefabBoneNames.Length} bones missing. autoScale={autoCorrection:F4}, userScale={userScale:F2}");
                else
                    Plugin.Log?.LogInfo($"[Ashlands Reborn] Armor bind-pose remap (runtime): {smr.name} on {vis.gameObject.name} ({prefabBoneNames.Length} BPs, autoScale={autoCorrection:F4}, userScale={userScale:F2})");
            }

            marker.RemappedInstances.Add(armorGo.GetInstanceID());
        }
    }

    private static void CleanupSyncedArmor(VisEquipment vis)
    {
        var marker = vis.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null) return;
        
        foreach (var obj in marker.SyncedObjects)
        {
            if (obj != null) UObject.Destroy(obj);
        }
        marker.SyncedObjects.Clear();
        marker.RemappedInstances.Clear();
    }

    private static void HideBodyVisuals(VisEquipment vis, bool hide)
    {
        // Charred_Melee is composed of several meshes.
        // According to logs: "Body", "Eyes", "Sinew", "Skull"
        string[] bodyParts = { "Body", "Eyes", "Sinew", "Skull", "Charred_Melee_Vis" };
        
        foreach (var partName in bodyParts)
        {
            var part = FindInChildren(vis.transform, partName);
            if (part != null)
            {
                var renderer = part.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = !hide;
            }
        }
    }

    private static System.Collections.IEnumerator ScaleHelmetAfterAttach(VisEquipment vis, AshlandsRebornCharredSwapped? marker)
    {
        // Wait 20 frames for the character skeleton, scale, and first animation frame to settle.
        // Initial spawn (especially console spawn) can have 'garbage' bone positions for the first few frames.
        for (int i = 0; i < 20; i++) yield return null;

        if (vis == null || !ShouldSwap()) yield break;

        // Ensure we have the attachment point. 
        if (vis.m_helmet == null) EnsureHelmetTransform(vis);
        var head = vis.m_helmet;

        var helmetGo = FHelmetItemInstance?.GetValue(vis) as GameObject;

        // --- RESCUE AND RE-EQUIP ---
        // If the helmet is missing but we now have a valid Head bone, the initial attach likely failed.
        // We trigger a re-equip now that the character is settled.
        // Root cause of the loop: vanilla SetHelmetItem skips re-attach when m_helmetItem already
        // equals the target name. Clearing the field first forces vanilla to see it as a new item.
        if (helmetGo == null && head != null && marker != null && !string.IsNullOrEmpty(marker.OriginalHelmetItem))
        {
            if (marker.HelmetRescueCount >= 3)
            {
                Plugin.Log?.LogWarning($"[Ashlands Reborn] Helmet rescue gave up after 3 attempts on {vis.gameObject.name}.");
                yield break;
            }
            marker.HelmetRescueCount++;
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Helmet rescue #{marker.HelmetRescueCount} on {vis.gameObject.name}.");
            FHelmetItem?.SetValue(vis, "");   // force vanilla to treat it as a new item
            marker.HelmetSwapped = false;     // allow prefix to re-record original
            vis.SetHelmetItem(marker.OriginalHelmetItem);
            yield break; // The new call will start its own coroutine
        }

        // Guard: skip if this is the same instance we already scaled (prevents stacking during combat).
        if (helmetGo == null || (marker != null && ReferenceEquals(helmetGo, marker.LastScaledHelmetInstance)))
        {
            if (helmetGo == null)
            {
                Plugin.Log?.LogWarning($"[Ashlands Reborn] Drake Helmet GO not found after 20 frames on {vis.gameObject.name}. m_helmet={(head?.name ?? "NULL")}");
            }
            yield break;
        }

        if (helmetGo != null)
        {
            // Ensure parenting is correct (fixes orphaned-at-origin issue if m_helmet changed)
            if (head != null && helmetGo.transform.parent != head)
            {
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Re-parenting Drake Helmet to Head bone for {vis.gameObject.name}");
                helmetGo.transform.SetParent(head);
                helmetGo.transform.localPosition = Vector3.zero;
                helmetGo.transform.localRotation = Quaternion.identity;
            }

            var scale = Plugin.CharredWarriorHelmetScale?.Value ?? 1.1f;
            var yOffset = Plugin.CharredWarriorHelmetYOffset?.Value ?? 0.05f;

            var posBefore = helmetGo.transform.position;

            helmetGo.transform.localScale *= scale;

            // Apply rotation and offsets adding to current state
            // Rotate around Y axis — configurable via CharredWarriorHelmetYaw
            var yaw = Plugin.CharredWarriorHelmetYaw?.Value ?? -90f;
            helmetGo.transform.localRotation *= Quaternion.Euler(0f, yaw, 0f);
            
            // Calculate actual face "forward" in world space now that we've rotated it locally.
            // vis.transform.forward might be pointing in a default direction during initialization.
            var faceForward = helmetGo.transform.forward;

            // Lift in world space
            helmetGo.transform.Translate(0f, yOffset, 0f, Space.World);
            // Move forward/back relative to where the face is actually pointing
            var zOffset = Plugin.CharredWarriorHelmetZOffset?.Value ?? 0.05f;
            helmetGo.transform.Translate(faceForward * zOffset, Space.World);

            // Important: Show the helmet now that it's correctly placed
            foreach (var r in helmetGo.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;

            Plugin.Log?.LogInfo($"[Ashlands Reborn] Drake Helmet adjusted and shown: rootForward={vis.transform.forward}, faceForward={faceForward}, yOff={yOffset}, zOff={zOffset}");

            if (marker != null)
            {
                marker.HelmetScaled = true;
                marker.LastScaledHelmetInstance = helmetGo;
            }
        }
        else
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] Drake Helmet GO not found after attach — cannot adjust position.");
        }
    }




    // -------------------------------------------------------------------------
    // Revert: restore original charred sword on all live instances
    // -------------------------------------------------------------------------

    internal static void RevertAllCharredWarriors()
    {
        var markers = UObject.FindObjectsByType<AshlandsRebornCharredSwapped>(FindObjectsSortMode.None);

        _suppressSwordSwap = true;
        try
        {
            foreach (var marker in markers)
            {
                if (marker == null) continue;

                var vis = marker.GetComponent<VisEquipment>();
                if (vis != null)
                {
                    CleanupSyncedArmor(vis);
                    if (!string.IsNullOrEmpty(marker.OriginalRightItem))
                        vis.SetRightItem(marker.OriginalRightItem);
                    
                    if (marker.HelmetSwapped)
                    {
                        FHelmetItem?.SetValue(vis, "_revert");
                        vis.SetHelmetItem(marker.OriginalHelmetItem);
                    }

                    if (marker.ChestSwapped)
                    {
                        FChestItem?.SetValue(vis, "_revert");
                        vis.SetChestItem(marker.OriginalChestItem);
                    }

                    if (marker.LegsSwapped)
                    {
                        FLegItem?.SetValue(vis, "_revert");
                        vis.SetLegItem(marker.OriginalLegItem);
                    }

                    if (marker.ShoulderSwapped)
                    {
                        FShoulderItem?.SetValue(vis, "_revert");
                        vis.SetShoulderItem(marker.OriginalShoulderItem, 0);
                    }

                    HideBodyVisuals(vis, false);
                }
                
                UObject.Destroy(marker);
            }
        }
        finally
        {
            _suppressSwordSwap = false;
        }

        Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred revert: {markers.Length} instance(s)");
    }

    // -------------------------------------------------------------------------
    // Refresh: re-apply swap to all live Charred_Melee instances
    // -------------------------------------------------------------------------

    internal static void RefreshCharredWarriors()
    {
        if (!ShouldSwap()) return;

        var humanoids = UObject.FindObjectsByType<Humanoid>(FindObjectsSortMode.None);
        var count = 0;

        foreach (var humanoid in humanoids)
        {
            if (!IsCharredMelee(humanoid.gameObject)) continue;

            var vis = humanoid.GetComponent<VisEquipment>();
            if (vis == null) continue;

            var marker = humanoid.GetComponent<AshlandsRebornCharredSwapped>();
            if (marker != null) CleanupSyncedArmor(vis);

            // --- Sword refresh ---
            marker = humanoid.GetComponent<AshlandsRebornCharredSwapped>();
            var triggerSword = marker?.OriginalRightItem ?? "";
            if (string.IsNullOrEmpty(triggerSword))
            {
                var cur = FRightItem?.GetValue(vis) as string ?? "";
                if (cur.StartsWith(CharredSwordPrefix, StringComparison.OrdinalIgnoreCase))
                    triggerSword = cur;
            }
            if (!string.IsNullOrEmpty(triggerSword))
            {
                if (marker != null) marker.OriginalRightItem = "";
                FRightItem?.SetValue(vis, "");
                vis.SetRightItem(triggerSword);
            }

            // --- Helmet refresh ---
            var helmetTarget = Plugin.CharredWarriorHelmetName?.Value ?? HelmetDrakeName;
            if (!string.IsNullOrEmpty(helmetTarget))
            {
                var triggerHelmet = marker?.OriginalHelmetItem ?? "";
                if (string.IsNullOrEmpty(triggerHelmet))
                    triggerHelmet = FHelmetItem?.GetValue(vis) as string ?? "";
                if (string.IsNullOrEmpty(triggerHelmet)) triggerHelmet = "_none";

                if (marker != null) { marker.OriginalHelmetItem = ""; marker.HelmetSwapped = false; }
                FHelmetItem?.SetValue(vis, "");
                vis.SetHelmetItem(triggerHelmet);
            }

            // --- Chest refresh ---
            var chestTarget = Plugin.CharredWarriorChestName?.Value ?? "";
            if (!string.IsNullOrEmpty(chestTarget))
            {
                var triggerChest = marker?.OriginalChestItem ?? "";
                if (string.IsNullOrEmpty(triggerChest))
                    triggerChest = FChestItem?.GetValue(vis) as string ?? "";
                if (string.IsNullOrEmpty(triggerChest)) triggerChest = "_none";

                if (marker != null) { marker.OriginalChestItem = ""; marker.ChestSwapped = false; }
                FChestItem?.SetValue(vis, "");
                vis.SetChestItem(triggerChest);
            }

            // --- Legs refresh ---
            var legsTarget = Plugin.CharredWarriorLegsName?.Value ?? "";
            if (!string.IsNullOrEmpty(legsTarget))
            {
                var triggerLegs = marker?.OriginalLegItem ?? "";
                if (string.IsNullOrEmpty(triggerLegs))
                    triggerLegs = FLegItem?.GetValue(vis) as string ?? "";
                if (string.IsNullOrEmpty(triggerLegs)) triggerLegs = "_none";

                if (marker != null) { marker.OriginalLegItem = ""; marker.LegsSwapped = false; }
                FLegItem?.SetValue(vis, "");
                vis.SetLegItem(triggerLegs);
            }

            // --- Shoulder refresh ---
            var shoulderTarget = Plugin.CharredWarriorShoulderName?.Value ?? "";
            if (!string.IsNullOrEmpty(shoulderTarget))
            {
                var triggerShoulder = marker?.OriginalShoulderItem ?? "";
                if (string.IsNullOrEmpty(triggerShoulder))
                    triggerShoulder = FShoulderItem?.GetValue(vis) as string ?? "";
                if (string.IsNullOrEmpty(triggerShoulder)) triggerShoulder = "_none";

                if (marker != null) { marker.OriginalShoulderItem = ""; marker.ShoulderSwapped = false; }
                FShoulderItem?.SetValue(vis, "");
                vis.SetShoulderItem(triggerShoulder, 0);
            }

            count++;
        }

        Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred refresh: {count} instance(s)");
    }

    // -------------------------------------------------------------------------
    // Phase 0: One-time discovery dump (fires once per session on first spawn)
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(Humanoid), "Awake")]
    [HarmonyPostfix]
    private static void Humanoid_Awake_Postfix(Humanoid __instance)
    {
        var prefabName = GetPrefabName(__instance.gameObject);

        if (prefabName == CharredMeleePrefab)
        {
            var vis = __instance.GetComponent<VisEquipment>();
            if (vis?.m_bodyModel != null)
            {
                var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                             ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();

                EnsureHelmetTransform(vis);
            }

            if (ShouldSwap())
                __instance.StartCoroutine(ForceEquipAfterSpawn(__instance));
        }

        // Discovery dump fires only once per session
        if (_dumpDone) return;
        if (prefabName.IndexOf("charred", StringComparison.OrdinalIgnoreCase) < 0) return;

        _dumpDone = true;
        DumpCharredWarrior(__instance, prefabName);
    }

    /// <summary>
    /// Waits for a Charred_Melee to finish its initial equip phase, then force-equips
    /// any missing armor slots. This ensures ALL Charred get the custom armor, not
    /// just those that randomly spawned with equipment in those slots.
    /// </summary>
    private static System.Collections.IEnumerator ForceEquipAfterSpawn(Humanoid humanoid)
    {
        // Wait for random equip to settle (game needs several frames after Awake)
        for (int i = 0; i < 10; i++) yield return null;

        if (humanoid == null || !ShouldSwap()) yield break;

        var vis = humanoid.GetComponent<VisEquipment>();
        if (vis == null) yield break;

        var marker = vis.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? vis.gameObject.AddComponent<AshlandsRebornCharredSwapped>();

        // Helmet
        var helmetTarget = Plugin.CharredWarriorHelmetName?.Value ?? HelmetDrakeName;
        if (!string.IsNullOrEmpty(helmetTarget) && !marker.HelmetSwapped)
        {
            var curHelmet = FHelmetItem?.GetValue(vis) as string ?? "";
            if (!string.Equals(curHelmet, helmetTarget, StringComparison.OrdinalIgnoreCase))
            {
                FHelmetItem?.SetValue(vis, "");
                vis.SetHelmetItem(curHelmet);
            }
        }

        // Chest
        var chestTarget = Plugin.CharredWarriorChestName?.Value ?? "";
        if (!string.IsNullOrEmpty(chestTarget) && !marker.ChestSwapped)
        {
            var curChest = FChestItem?.GetValue(vis) as string ?? "";
            if (!string.Equals(curChest, chestTarget, StringComparison.OrdinalIgnoreCase))
            {
                FChestItem?.SetValue(vis, "");
                vis.SetChestItem(curChest);
            }
        }

        // Legs
        var legsTarget = Plugin.CharredWarriorLegsName?.Value ?? "";
        if (!string.IsNullOrEmpty(legsTarget) && !marker.LegsSwapped)
        {
            var curLegs = FLegItem?.GetValue(vis) as string ?? "";
            if (!string.Equals(curLegs, legsTarget, StringComparison.OrdinalIgnoreCase))
            {
                FLegItem?.SetValue(vis, "");
                vis.SetLegItem(curLegs);
            }
        }

        // Shoulder/Cape
        var shoulderTarget = Plugin.CharredWarriorShoulderName?.Value ?? "";
        if (!string.IsNullOrEmpty(shoulderTarget) && !marker.ShoulderSwapped)
        {
            var curShoulder = FShoulderItem?.GetValue(vis) as string ?? "";
            if (!string.Equals(curShoulder, shoulderTarget, StringComparison.OrdinalIgnoreCase))
            {
                FShoulderItem?.SetValue(vis, "");
                vis.SetShoulderItem(curShoulder, 0);
            }
        }
    }

    private static void FindGameObjectByPrefabName(Transform root, string prefabName, List<GameObject> results)
    {
        if (GetPrefabName(root.gameObject).Equals(prefabName, StringComparison.OrdinalIgnoreCase))
            results.Add(root.gameObject);

        for (var i = 0; i < root.childCount; i++)
            FindGameObjectByPrefabName(root.GetChild(i), prefabName, results);
    }

    private static void EnsureHelmetTransform(VisEquipment vis)
    {
        // Charred_Melee has no m_helmet transform set in its prefab — rigid-attach helmets
        // like HelmetDrake need it so AttachItem can parent them correctly.
        if (vis.m_helmet != null) return;

        // 1. Recursive search in hierarchy
        var head = FindInChildren(vis.transform, "Head");
        
        // 2. Fallback: Search in m_bodyModel bones if available
        if (head == null && vis.m_bodyModel != null)
        {
            foreach (var bone in vis.m_bodyModel.bones)
            {
                if (bone != null && string.Equals(bone.name, "Head", StringComparison.OrdinalIgnoreCase))
                {
                    head = bone;
                    break;
                }
            }
        }

        if (head != null)
        {
            vis.m_helmet = head;
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Assigned Head bone as m_helmet for {vis.gameObject.name}");
        }
        else
        {
            if (_swapLogCount < 20)
                Plugin.Log?.LogWarning($"[Ashlands Reborn] Could not find Head bone for {vis.gameObject.name}");
        }
    }

    private static void DumpCharredWarrior(Humanoid humanoid, string prefabName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CHARRED WARRIOR DUMP ===");
        sb.AppendLine($"Prefab name: '{prefabName}'");
        sb.AppendLine($"Position: {humanoid.transform.position}");

        sb.AppendLine();
        sb.AppendLine("--- m_defaultItems (always equipped) ---");
        DumpGameObjectArray(humanoid.m_defaultItems, sb);

        sb.AppendLine("--- m_randomWeapon (one chosen randomly) ---");
        DumpGameObjectArray(humanoid.m_randomWeapon, sb);

        sb.AppendLine("--- m_randomArmor (one chosen randomly) ---");
        DumpGameObjectArray(humanoid.m_randomArmor, sb);

        sb.AppendLine("--- m_randomShield (one chosen randomly) ---");
        DumpGameObjectArray(humanoid.m_randomShield, sb);

        if (humanoid.m_randomItems != null)
        {
            sb.AppendLine("--- m_randomItems (each has a chance) ---");
            foreach (var ri in humanoid.m_randomItems)
                sb.AppendLine($"  {ri.m_prefab?.name ?? "null"} ({ri.m_chance * 100:F0}%)");
        }

        if (humanoid.m_randomSets != null)
        {
            sb.AppendLine("--- m_randomSets ---");
            foreach (var set in humanoid.m_randomSets)
            {
                sb.AppendLine($"  Set '{set.m_name}':");
                DumpGameObjectArray(set.m_items, sb, indent: "    ");
            }
        }

        var vis = humanoid.GetComponent<VisEquipment>();
        if (vis != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- VisEquipment Slots (at Awake time) ---");
            foreach (var field in new[] { "m_rightItem", "m_leftItem", "m_chestItem", "m_legItem",
                                          "m_helmetItem", "m_shoulderItem", "m_utilityItem" })
            {
                try
                {
                    var f = typeof(VisEquipment).GetField(field,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var val = f?.GetValue(vis) as string;
                    sb.AppendLine($"  {field}: '{val ?? "(empty)"}'");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  {field}: ERROR ({ex.Message})");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- Hierarchy (depth ≤ 6) ---");
        DumpHierarchy(humanoid.transform, sb, 0, maxDepth: 6);

        sb.AppendLine();
        sb.AppendLine("--- SkinnedMeshRenderers ---");
        foreach (var smr in humanoid.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            sb.AppendLine($"  SMR: '{smr.name}'  bones={smr.bones.Length}  rootBone={smr.rootBone?.name ?? "null"}");
            sb.AppendLine($"    localBounds center={smr.localBounds.center}  size={smr.localBounds.size}");
            for (var i = 0; i < smr.bones.Length; i++)
                sb.AppendLine($"    [{i}] {smr.bones[i]?.name ?? "NULL"}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Renderers & Materials ---");
        foreach (var r in humanoid.GetComponentsInChildren<Renderer>(true))
        {
            sb.AppendLine($"  {r.GetType().Name}: '{r.name}'");
            foreach (var mat in r.sharedMaterials)
                sb.AppendLine($"    Material: '{mat?.name ?? "null"}'  Shader: '{mat?.shader?.name ?? "null"}'");
        }

        sb.AppendLine();
        sb.AppendLine("=== END CHARRED WARRIOR DUMP ===");
        Plugin.Log?.LogInfo(sb.ToString());
    }

    private static void DumpGameObjectArray(GameObject[] arr, StringBuilder sb, string indent = "  ")
    {
        if (arr == null || arr.Length == 0) { sb.AppendLine($"{indent}(empty)"); return; }
        foreach (var go in arr)
            sb.AppendLine($"{indent}{go?.name ?? "null"}");
    }

    private static void DumpHierarchy(Transform t, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name}");
        for (var i = 0; i < t.childCount; i++)
            DumpHierarchy(t.GetChild(i), sb, depth + 1, maxDepth);
    }
}

/// <summary>
/// Marker on Charred_Melee — stores the original right-hand weapon name for revert.
/// </summary>
internal class AshlandsRebornCharredSwapped : MonoBehaviour
{
    public string OriginalRightItem = "";
    public string OriginalHelmetItem = "";
    public string OriginalChestItem = "";
    public string OriginalLegItem = "";
    public string OriginalShoulderItem = "";
    public bool HelmetSwapped;
    public bool ChestSwapped;
    public bool LegsSwapped;
    public bool ShoulderSwapped;
    public List<int> RemappedInstances = new();
    public List<GameObject> SyncedObjects = new();

    /// <summary>True when we've scaled the Krom weapon (avoids re-scaling every frame).</summary>
    public bool KromScaled;

    /// <summary>Last Krom weapon instance we scaled. Guards against repeated SetRightItem calls during combat.</summary>
    public GameObject? LastScaledKromInstance;

    /// <summary>True when we've scaled the helmet (avoids re-scaling every frame).</summary>
    public bool HelmetScaled;

    /// <summary>Counts rescue re-equip attempts; capped to prevent infinite loops.</summary>
    public int HelmetRescueCount;

    /// <summary>
    /// The last helmet instance GameObject we applied transforms to.
    /// The coroutine compares the current instance against this to prevent
    /// re-applying scale/rotation when SetHelmetItem is called repeatedly (e.g. during combat).
    /// </summary>
    public GameObject? LastScaledHelmetInstance;
}
