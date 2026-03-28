using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

    // Player body mesh cache — populated on first Player Awake, used by body swap layer
    private static Mesh? _cachedPlayerBodyMesh;
    private static Material? _cachedPlayerBodyMaterial;
    private static string[]? _cachedPlayerBoneNames;

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

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (!marker.ChestSwapped)
            marker.OriginalChestItem = name;

        // Body swap layer (volumetric deforming flesh under armor)
        if (Plugin.EnableBodySwap?.Value == true && !marker.BodySwapApplied)
        {
            HideBodyVisuals(__instance, true);
            __instance.StartCoroutine(ApplyBodySwapLayer(__instance));
        }

        // Approach A: bind pose retargeting armor on top
        var target = Plugin.CharredWarriorChestName?.Value ?? "";
        if (string.IsNullOrEmpty(target)) return;

        // When ShowVanillaChest is off (default), replace name with custom piece.
        // When on, vanilla attaches as normal and the coroutine adds custom on top.
        if (Plugin.ShowVanillaChest?.Value != true)
            name = target;

        marker.ChestSwapped = true;
        if (Plugin.EnableBodySwap?.Value != true)
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

        if (Plugin.ShowVanillaShoulders?.Value != true)
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
            { "LeftArm", M4(0.024250146f, 0.17487538f, 0.0023985063f, -9.69768371e-05f, -0.17793842f, 0.025146089f, -0.034354255f, -0.0019564675f, -0.033606939f, 0.0022502947f, 0.17571479f, -0.0026339616f, 0f, 0f, 0f, 1f) },
            { "LeftForeArm", M4(-0.00057045865f, 0.17645763f, 0.0061586001f, -0.00038173064f, -0.18273112f, -0.000812474f, 0.0063531785f, -0.0041889511f, 0.0062443661f, -0.0062203747f, 0.17880593f, 0.00067522092f, 0f, 0f, 0f, 1f) },
            { "LeftHand", M4(6.55785826e-08f, 0.17656973f, -1.52193795e-08f, -0.00036235285f, -0.18172002f, 6.74914276e-08f, 3.80372853e-08f, -0.0056970548f, 3.76966725e-08f, 1.55230211e-08f, 0.18009271f, 0.0027630306f, 0f, 0f, 0f, 1f) },
            { "LeftHandThumb1", M4(0.074347734f, -0.14232317f, 0.078112155f, 0.0036671846f, -0.1132546f, -0.10842414f, -0.08975596f, -0.0053203483f, 0.11791539f, -0.012063769f, -0.13421349f, 0.0014560665f, 0f, 0f, 0f, 1f) },
            { "LeftHandThumb2", M4(-0.012305028f, -0.17801072f, 0.004632486f, -0.00079314352f, -0.14639778f, 0.0073270742f, -0.10731211f, -0.0070286631f, 0.10477435f, -0.010981752f, -0.14368558f, 0.00068826781f, 0f, 0f, 0f, 1f) },
            { "LeftHandThumb3", M4(-0.04324811f, -0.16717359f, -0.037171923f, -0.0029086627f, -0.14160326f, 0.058964789f, -0.10043319f, -0.0069412268f, 0.10461677f, 0.0050712447f, -0.14452451f, 0.00058932119f, 0f, 0f, 0f, 1f) },
            { "LeftHandIndex1", M4(-0.0082563302f, 0.1766838f, -0.0009707617f, -0.00030751523f, -0.18072601f, -0.0085512986f, -0.019306546f, -0.0068369918f, -0.01907541f, 8.9484769e-05f, 0.17852274f, 0.0028388144f, 0f, 0f, 0f, 1f) },
            { "LeftHandIndex2", M4(-0.0072025768f, 0.17686857f, 8.28909979e-05f, -0.00015452754f, -0.16805011f, -0.006809487f, -0.072501257f, -0.0078965295f, -0.070510112f, -0.0029480874f, 0.16371173f, 0.00095095596f, 0f, 0f, 0f, 1f) },
            { "LeftHandIndex3", M4(-0.013618456f, 0.17628019f, -0.00072854856f, -0.00036453773f, -0.16511661f, -0.013080101f, -0.078415327f, -0.0081955921f, -0.076156326f, -0.0052170665f, 0.16123012f, 0.00086553191f, 0f, 0f, 0f, 1f) },
            { "LeftHandMiddle1", M4(-0.0026410464f, 0.1766423f, 0.002161992f, -0.00032507791f, -0.1803118f, -0.0024835304f, -0.017352201f, -0.0068208384f, -0.017258003f, -0.0024572585f, 0.17968465f, 0.0029976515f, 0f, 0f, 0f, 1f) },
            { "LeftHandMiddle2", M4(-0.0044262167f, 0.17662156f, 0.0008321061f, -0.00037054991f, -0.16762388f, -0.0038500775f, -0.074429296f, -0.0080104452f, -0.072319306f, -0.0025803088f, 0.16300537f, 0.00087592134f, 0f, 0f, 0f, 1f) },
            { "LeftHandMiddle3", M4(-0.0021188059f, 0.17665973f, -4.46057675e-05f, -0.00026440184f, -0.14917473f, -0.0018161994f, -0.10706435f, -0.0083609074f, -0.10388473f, -0.0012093894f, 0.14476502f, -0.00057865866f, 0f, 0f, 0f, 1f) },
            { "LeftHandRing1", M4(0.0098039629f, 0.17628418f, 0.0031726027f, -0.00014738111f, -0.18078335f, 0.010232026f, -0.0098818252f, -0.0067333318f, -0.0099991839f, -0.0026860589f, 0.18014909f, 0.0033256742f, 0f, 0f, 0f, 1f) },
            { "LeftHandRing2", M4(-0.0027523492f, 0.17654963f, -0.0004309236f, -0.00067449454f, -0.16664098f, -0.0027853961f, -0.076825894f, -0.0080195637f, -0.074676f, -0.00076874119f, 0.16200556f, 0.00080529379f, 0f, 0f, 0f, 1f) },
            { "LeftHandRing3", M4(0.0028925859f, 0.17653233f, 0.0029324754f, -0.00037629579f, -0.1555482f, 0.0041949847f, -0.099102065f, -0.008404701f, -0.095396467f, -0.00092349458f, 0.14969285f, -0.00010511739f, 0f, 0f, 0f, 1f) },
            { "LeftHandPinky1", M4(0.035787612f, 0.17275807f, -0.0078353668f, 0.00026281169f, -0.16754767f, 0.031763859f, -0.064919427f, -0.0074048648f, -0.06103202f, 0.020236159f, 0.16741596f, 0.0010454286f, 0f, 0f, 0f, 1f) },
            { "LeftHandPinky2", M4(0.029762145f, 0.17366868f, -0.011461794f, -6.31821385e-05f, -0.13099302f, 0.014010907f, -0.12784889f, -0.0076549165f, -0.12127108f, 0.029194247f, 0.12745284f, -0.0018712904f, 0f, 0f, 0f, 1f) },
            { "LeftHandPinky3", M4(0.020490598f, 0.17495801f, -0.01273613f, -0.00044604548f, -0.11825319f, 0.0036113921f, -0.14064206f, -0.0076466873f, -0.13476367f, 0.024076648f, 0.1139288f, -0.0025854786f, 0f, 0f, 0f, 1f) },
            { "RightShoulder", M4(0.066344686f, -0.16355032f, -0.0052403905f, -0.00080215203f, 0.16359012f, 0.066412307f, -0.001774186f, 7.9966383e-05f, 0.0034389591f, -0.0039852182f, 0.18549581f, -0.0035244275f, 0f, 0f, 0f, 1f) },
            { "RightArm", M4(0.024250142f, -0.17487535f, -0.0023985079f, 0.00010409208f, 0.1779384f, 0.025146089f, -0.034354255f, -0.0019042552f, 0.033606946f, 0.0022502933f, 0.17571482f, -0.0026240991f, 0f, 0f, 0f, 1f) },
            { "RightForeArm", M4(-0.0005704583f, -0.17645754f, -0.0061585959f, 0.00038156321f, 0.18273106f, -0.00081247359f, 0.0063531771f, -0.0041353344f, -0.0062443647f, -0.0062203696f, 0.17880587f, 0.00067339151f, 0f, 0f, 0f, 1f) },
            { "RightHand", M4(3.58111443e-08f, -0.17656966f, 1.34637874e-08f, 0.00036235395f, 0.18172002f, 3.68557167e-08f, 4.1434685e-08f, -0.0056437403f, -4.10636538e-08f, 1.37324161e-08f, 0.18009272f, 0.002763032f, 0f, 0f, 0f, 1f) },
            { "RightHandThumb1", M4(0.07434772f, 0.14232312f, -0.07811211f, -0.0036453705f, 0.11325458f, -0.1084241f, -0.089755923f, -0.0052871197f, -0.11791536f, -0.012063761f, -0.13421349f, 0.0014214657f, 0f, 0f, 0f, 1f) },
            { "RightHandThumb2", M4(-0.012305055f, 0.1780107f, -0.0046324888f, 0.00078953372f, 0.14639777f, 0.0073270751f, -0.10731211f, -0.0069857109f, -0.10477435f, -0.010981763f, -0.14368555f, 0.00065752532f, 0f, 0f, 0f, 1f) },
            { "RightHandThumb3", M4(-0.043248143f, 0.16717364f, 0.037171938f, 0.0028959776f, 0.14160331f, 0.058964826f, -0.10043322f, -0.0068996875f, -0.10461679f, 0.0050712368f, -0.14452451f, 0.00055863056f, 0f, 0f, 0f, 1f) },
            { "RightHandIndex1", M4(-0.0082563302f, -0.17668377f, 0.00097075157f, 0.00030509208f, 0.18072605f, -0.0085512996f, -0.019306546f, -0.0067839636f, 0.019075405f, 8.94762561e-05f, 0.17852272f, 0.0028444186f, 0f, 0f, 0f, 1f) },
            { "RightHandIndex2", M4(-0.0072025815f, -0.17686857f, -8.29164856e-05f, 0.00015241407f, 0.16805011f, -0.006809481f, -0.072501265f, -0.0078472272f, 0.070510112f, -0.0029481126f, 0.16371171f, 0.00097164774f, 0f, 0f, 0f, 1f) },
            { "RightHandIndex3", M4(-0.013618466f, -0.17628019f, 0.00072851352f, 0.00036054195f, 0.16511658f, -0.013080095f, -0.078415327f, -0.0081471521f, 0.076156348f, -0.0052171052f, 0.16123013f, 0.00088787492f, 0f, 0f, 0f, 1f) },
            { "RightHandMiddle1", M4(-0.0026410455f, -0.17664221f, -0.0021619999f, 0.00032430165f, 0.1803118f, -0.00248353f, -0.017352195f, -0.0067679347f, 0.017257996f, -0.002457266f, 0.17968461f, 0.0030027193f, 0f, 0f, 0f, 1f) },
            { "RightHandMiddle2", M4(-0.0044262516f, -0.17662153f, -0.00083213206f, 0.0003692513f, 0.16762388f, -0.003850102f, -0.074429289f, -0.007961262f, 0.072319306f, -0.0025803414f, 0.16300538f, 0.00089715037f, 0f, 0f, 0f, 1f) },
            { "RightHandMiddle3", M4(-0.0021188415f, -0.17665966f, 4.45768746e-05f, 0.00026378126f, 0.14917469f, -0.0018162128f, -0.10706428f, -0.0083171399f, 0.10388473f, -0.0012094314f, 0.14476505f, -0.00054817437f, 0f, 0f, 0f, 1f) },
            { "RightHandRing1", M4(0.009803962f, -0.17628415f, -0.0031726004f, 0.00015025711f, 0.18078339f, 0.010232029f, -0.0098818243f, -0.0066802888f, 0.0099991793f, -0.0026860568f, 0.18014906f, 0.0033286193f, 0f, 0f, 0f, 1f) },
            { "RightHandRing2", M4(-0.0027523621f, -0.17654966f, 0.00043088559f, 0.00067368639f, 0.16664092f, -0.0027853895f, -0.076825887f, -0.0079706647f, 0.074676074f, -0.00076878385f, 0.16200568f, 0.00082721532f, 0f, 0f, 0f, 1f) },
            { "RightHandRing3", M4(0.0028926211f, -0.17653233f, -0.0029324503f, 0.00037714344f, 0.15554824f, 0.0041950014f, -0.099102117f, -0.0083590671f, 0.095396444f, -0.00092344754f, 0.14969279f, -7.71245468e-05f, 0f, 0f, 0f, 1f) },
            { "RightHandPinky1", M4(0.035787586f, -0.17275794f, 0.0078353835f, -0.00025230978f, 0.1675476f, 0.03176384f, -0.06491942f, -0.0073557002f, 0.061032012f, 0.020236176f, 0.16741592f, 0.0010633451f, 0f, 0f, 0f, 1f) },
            { "RightHandPinky2", M4(0.029762199f, -0.17366864f, 0.011461799f, 7.19137461e-05f, 0.13099304f, 0.01401094f, -0.12784891f, -0.0076164822f, 0.12127109f, 0.029194308f, 0.12745281f, -0.0018356947f, 0f, 0f, 0f, 1f) },
            { "RightHandPinky3", M4(0.020490626f, -0.17495795f, 0.012736145f, 0.00045205702f, 0.11825313f, 0.0036113937f, -0.14064202f, -0.0076119942f, 0.1347637f, 0.0240767f, 0.1139288f, -0.0025459349f, 0f, 0f, 0f, 1f) },
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
    private static SkinnedMeshRenderer? _lastChestSMR;

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

                // --- Clone mesh and compute bind poses ---
                bool isChest = (instanceField == FChestItemInstances);
                bool useTrimmedMesh = isChest && (Plugin.TrimChestArms?.Value ?? true);

                Mesh newMesh;
                if (useTrimmedMesh)
                {
                    // Use pre-built torso-only mesh (arm tris already removed).
                    // The SouthsilArmor knightchest mesh is isReadable=false, blocking
                    // all triangle read/write APIs, so we load a fully readable mesh
                    // built offline from the extracted asset data.
                    var trimmed = LoadTrimmedChestMesh();
                    newMesh = trimmed != null
                        ? UObject.Instantiate(trimmed)
                        : UObject.Instantiate(smr.sharedMesh); // fallback: untrimmed
                }
                else
                {
                    var prefabMesh = GetPrefabArmorMesh(armorItemName, smr.name);
                    var baseMesh = prefabMesh ?? smr.sharedMesh;
                    newMesh = UObject.Instantiate(baseMesh);
                }

                var originalBPs = newMesh.bindposes;
                var newBPs = new Matrix4x4[originalBPs.Length];

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

                // --- Stash chest SMR reference for on-demand runtime matrix dump ---
                if (isChest)
                    _lastChestSMR = smr;

                if (missingCount > 0)
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] Armor '{armorItemName}' SMR '{smr.name}': {missingCount}/{prefabBoneNames.Length} bones missing. autoScale={autoCorrection:F4}, userScale={userScale:F2}");
                else
                    Plugin.Log?.LogInfo($"[Ashlands Reborn] Armor bind-pose remap (runtime): {smr.name} on {vis.gameObject.name} ({prefabBoneNames.Length} BPs, autoScale={autoCorrection:F4}, userScale={userScale:F2})");
            }

            marker.RemappedInstances.Add(armorGo.GetInstanceID());
        }
    }

    // =====================================================================
    // Arm triangle trimming — removes arm/hand geometry from chest armor
    // =====================================================================

    /// Cached torso-only mesh loaded from embedded resource (arm triangles pre-removed).
    private static Mesh? s_trimmedChestMesh;

    /// <summary>
    /// Loads the pre-trimmed knightchest mesh from an embedded zlib-compressed binary resource.
    /// The SouthsilArmor mesh is not CPU-readable at runtime (isReadable=false), which blocks
    /// all triangle read/write APIs. This mesh was built offline from the extracted asset data
    /// with arm triangles already removed and unreferenced vertices stripped.
    /// </summary>
    private static Mesh? LoadTrimmedChestMesh()
    {
        if (s_trimmedChestMesh != null) return s_trimmedChestMesh;

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("AshlandsReborn.knightchest_trimmed.bin");
        if (stream == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] knightchest_trimmed.bin embedded resource not found. Arm trim disabled.");
            return null;
        }

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            compressed = ms.ToArray();
        }

        // Decompress: Python zlib.compress wraps raw deflate with 2-byte header + 4-byte Adler32 footer.
        byte[] raw;
        using (var inMs = new MemoryStream(compressed, 2, compressed.Length - 6))
        using (var deflate = new DeflateStream(inMs, CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            deflate.CopyTo(outMs);
            raw = outMs.ToArray();
        }

        int offset = 0;
        int ReadInt() { var v = BitConverter.ToInt32(raw, offset); offset += 4; return v; }
        float ReadFloat() { var v = BitConverter.ToSingle(raw, offset); offset += 4; return v; }

        int vertCount = ReadInt();
        int triIndexCount = ReadInt();
        int boneCount = ReadInt();

        var vertices = new Vector3[vertCount];
        for (int i = 0; i < vertCount; i++)
            vertices[i] = new Vector3(ReadFloat(), ReadFloat(), ReadFloat());

        var normals = new Vector3[vertCount];
        for (int i = 0; i < vertCount; i++)
            normals[i] = new Vector3(ReadFloat(), ReadFloat(), ReadFloat());

        var uvs = new Vector2[vertCount];
        for (int i = 0; i < vertCount; i++)
            uvs[i] = new Vector2(ReadFloat(), ReadFloat());

        var boneWeights = new BoneWeight[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            boneWeights[i] = new BoneWeight
            {
                boneIndex0 = ReadInt(), boneIndex1 = ReadInt(),
                boneIndex2 = ReadInt(), boneIndex3 = ReadInt(),
                weight0 = ReadFloat(), weight1 = ReadFloat(),
                weight2 = ReadFloat(), weight3 = ReadFloat(),
            };
        }

        var triangles = new int[triIndexCount];
        for (int i = 0; i < triIndexCount; i++)
            triangles[i] = BitConverter.ToUInt16(raw, offset + i * 2);
        offset += triIndexCount * 2;

        int bpCount = ReadInt();
        var bindPoses = new Matrix4x4[bpCount];
        for (int i = 0; i < bpCount; i++)
        {
            var m = new Matrix4x4();
            for (int j = 0; j < 16; j++)
                m[j] = ReadFloat();
            bindPoses[i] = m;
        }

        var mesh = new Mesh();
        mesh.name = "KnightChest_TorsoOnly";
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.boneWeights = boneWeights;
        mesh.triangles = triangles;
        mesh.bindposes = bindPoses;
        mesh.RecalculateBounds();

        s_trimmedChestMesh = mesh;
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Loaded trimmed chest mesh: {vertCount} verts, {triIndexCount / 3} tris, {bpCount} bones");
        return mesh;
    }



    // =====================================================================
    // Hybrid body swap layer — player mesh underneath Approach A armor
    // =====================================================================

    private static System.Collections.IEnumerator ApplyBodySwapLayer(VisEquipment vis)
    {
        // 5-frame settle (much shorter than armor — body just needs skeleton to be live)
        for (int i = 0; i < 5; i++) yield return null;

        if (vis == null || !ShouldSwap()) yield break;

        var marker = vis.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null || marker.BodySwapApplied) yield break;

        if (_cachedPlayerBodyMesh == null || _cachedPlayerBoneNames == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] BodySwap: player body mesh not cached yet — skipping");
            yield break;
        }

        var bodySMR = vis.m_bodyModel;
        if (bodySMR == null) yield break;

        var bodyRoot = bodySMR.rootBone ?? bodySMR.transform;
        var charBoneMap = BuildCharredBoneMap(vis);

        // Clone the cached player mesh so we can modify it independently per creature
        var clonedMesh = UObject.Instantiate(_cachedPlayerBodyMesh);
        clonedMesh.name = "BodySwapLayer";

        // Map player bone names → Charred bone Transforms (1:1 Mixamo names)
        var boneCount = _cachedPlayerBoneNames.Length;
        var bones = new Transform[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            if (!charBoneMap.TryGetValue(_cachedPlayerBoneNames[i], out var t))
                t = bodyRoot;
            bones[i] = t;
        }

        // Create GO parented to vis.transform (same level as armor GOs)
        var go = new GameObject("BodySwapLayer");
        go.transform.SetParent(vis.transform, false);
        go.transform.localPosition = new Vector3(0f, Plugin.BodySwapYOffset?.Value ?? 0f, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * (Plugin.BodySwapScale?.Value ?? 1f);

        // Create SMR — keep the player's original bind poses; GPU skinning deforms via Charred bones
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = clonedMesh;
        smr.bones = bones;
        smr.rootBone = bodyRoot;

        // Material: clone player material and tint it
        var mat = _cachedPlayerBodyMaterial != null
            ? UObject.Instantiate(_cachedPlayerBodyMaterial)
            : new Material(Shader.Find("Standard"));
        mat.color = new Color(
            Plugin.BodySwapColorR?.Value ?? 0.15f,
            Plugin.BodySwapColorG?.Value ?? 0.1f,
            Plugin.BodySwapColorB?.Value ?? 0.05f,
            1f);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(
                Plugin.BodySwapEmissionR?.Value ?? 0.8f,
                Plugin.BodySwapEmissionG?.Value ?? 0.2f,
                Plugin.BodySwapEmissionB?.Value ?? 0f,
                1f));
        }
        smr.material = mat;

        marker.BodySwapApplied = true;
        marker.SyncedObjects.Add(go);

        Plugin.Log?.LogInfo($"[Ashlands Reborn] BodySwap applied to {vis.gameObject.name}: {clonedMesh.vertexCount} verts, {boneCount} bones");
    }

    /// <summary>
    /// Phase 5: Dumps raw Unity runtime matrices for the chest SMR.
    /// Writes JSON to BepInEx/plugins/chest_runtime_matrices.json.
    /// Triggered by F9 press (with 2-frame delay for animation update).
    /// </summary>
    private static void DumpRuntimeMatrices(SkinnedMeshRenderer smr, string[] boneNames)
    {
        try
        {

            var bones = smr.bones;
            var mesh = smr.sharedMesh;
            var bindPoses = mesh.bindposes;

            // Sanity check
            if (bones.Length != bindPoses.Length)
                Plugin.Log?.LogWarning($"[RuntimeDump] bones.Length ({bones.Length}) != bindposes.Length ({bindPoses.Length})!");

            // Try to get the prefab mesh for comparison
            var prefabMesh = GetPrefabArmorMesh("ArmorPaddedCuirass", smr.name);
            Plugin.Log?.LogInfo($"[RuntimeDump] SMR mesh: name='{mesh.name}', vertexCount={mesh.vertexCount}, isReadable={mesh.isReadable}, subMeshCount={mesh.subMeshCount}");
            if (prefabMesh != null)
                Plugin.Log?.LogInfo($"[RuntimeDump] Prefab mesh: name='{prefabMesh.name}', vertexCount={prefabMesh.vertexCount}, isReadable={prefabMesh.isReadable}");
            else
                Plugin.Log?.LogInfo("[RuntimeDump] Prefab mesh: null (GetPrefabArmorMesh returned null)");

            var sb = new StringBuilder();
            sb.AppendLine("{");

            // Mesh metadata
            sb.AppendLine($"  \"mesh_name\": \"{mesh.name}\",");
            sb.AppendLine($"  \"mesh_vertex_count\": {mesh.vertexCount},");
            sb.AppendLine($"  \"mesh_is_readable\": {(mesh.isReadable ? "true" : "false")},");
            if (prefabMesh != null)
            {
                sb.AppendLine($"  \"prefab_mesh_name\": \"{prefabMesh.name}\",");
                sb.AppendLine($"  \"prefab_mesh_vertex_count\": {prefabMesh.vertexCount},");
                sb.AppendLine($"  \"prefab_mesh_is_readable\": {(prefabMesh.isReadable ? "true" : "false")},");
            }

            // Dump the PREFAB mesh (actual armor geometry, readable) — full vertices, triangles, bone weights
            if (prefabMesh != null && prefabMesh.isReadable)
            {
                var pVerts = prefabMesh.vertices;
                var pTris = prefabMesh.triangles;
                var pBoneNames = new string[0];
                // Get prefab bone names from prefab SMR
                {
                    var pPrefab = ObjectDB.instance?.GetItemPrefab("ArmorPaddedCuirass");
                    if (pPrefab != null)
                    {
                        foreach (Transform child in pPrefab.transform)
                        {
                            if (!child.gameObject.name.StartsWith("attach_skin", StringComparison.OrdinalIgnoreCase)) continue;
                            foreach (var pSmr in child.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            {
                                if (pSmr.sharedMesh == prefabMesh)
                                {
                                    pBoneNames = new string[pSmr.bones.Length];
                                    for (int i = 0; i < pSmr.bones.Length; i++)
                                        pBoneNames[i] = pSmr.bones[i] != null ? pSmr.bones[i].name : "null";
                                    break;
                                }
                            }
                        }
                    }
                }

                sb.AppendLine("  \"prefab_armor_mesh\": {");
                sb.AppendLine($"    \"vertex_count\": {pVerts.Length},");
                sb.AppendLine($"    \"triangle_count\": {pTris.Length / 3},");
                sb.AppendLine($"    \"bone_count\": {pBoneNames.Length},");

                // Bone names
                sb.Append("    \"bone_names\": [");
                for (int i = 0; i < pBoneNames.Length; i++)
                {
                    sb.Append($"\"{pBoneNames[i]}\"");
                    if (i < pBoneNames.Length - 1) sb.Append(",");
                }
                sb.AppendLine("],");

                // Vertices
                sb.AppendLine("    \"vertices\": [");
                for (int i = 0; i < pVerts.Length; i++)
                {
                    sb.Append($"      [{pVerts[i].x:G9},{pVerts[i].y:G9},{pVerts[i].z:G9}]");
                    sb.AppendLine(i < pVerts.Length - 1 ? "," : "");
                }
                sb.AppendLine("    ],");

                // Triangles (as triplets)
                sb.AppendLine("    \"triangles\": [");
                for (int i = 0; i < pTris.Length; i += 3)
                {
                    sb.Append($"      [{pTris[i]},{pTris[i+1]},{pTris[i+2]}]");
                    sb.AppendLine(i + 3 < pTris.Length ? "," : "");
                }
                sb.AppendLine("    ],");

                // Bone weights
                var pWeights = prefabMesh.boneWeights;
                sb.AppendLine("    \"boneWeights\": [");
                for (int i = 0; i < pWeights.Length; i++)
                {
                    var w = pWeights[i];
                    var w0s = w.weight0.ToString("G9"); var w1s = w.weight1.ToString("G9");
                    var w2s = w.weight2.ToString("G9"); var w3s = w.weight3.ToString("G9");
                    sb.Append($"      {{\"b0\":{w.boneIndex0},\"w0\":{w0s},\"b1\":{w.boneIndex1},\"w1\":{w1s},\"b2\":{w.boneIndex2},\"w2\":{w2s},\"b3\":{w.boneIndex3},\"w3\":{w3s}}}");
                    sb.AppendLine(i < pWeights.Length - 1 ? "," : "");
                }
                sb.AppendLine("    ],");

                // Bind poses from prefab mesh (original player bind poses)
                var pBPs = prefabMesh.bindposes;
                sb.AppendLine("    \"bindposes\": [");
                for (int i = 0; i < pBPs.Length; i++)
                {
                    sb.Append($"      {M4ToJson(pBPs[i])}");
                    sb.AppendLine(i < pBPs.Length - 1 ? "," : "");
                }
                sb.AppendLine("    ]");

                sb.AppendLine("  },");
            }

            // SMR transform
            sb.AppendLine("  \"smr_transform\": {");
            sb.AppendLine($"    \"name\": \"{smr.transform.name}\",");
            sb.AppendLine($"    \"localToWorldMatrix\": {M4ToJson(smr.transform.localToWorldMatrix)}");
            sb.AppendLine("  },");

            // Root bone
            sb.AppendLine("  \"rootBone\": {");
            sb.AppendLine($"    \"name\": \"{(smr.rootBone != null ? smr.rootBone.name : "null")}\",");
            sb.AppendLine($"    \"localToWorldMatrix\": {(smr.rootBone != null ? M4ToJson(smr.rootBone.localToWorldMatrix) : "null")}");
            sb.AppendLine("  },");

            // Bone count / bind pose count
            sb.AppendLine($"  \"bone_count\": {bones.Length},");
            sb.AppendLine($"  \"bindpose_count\": {bindPoses.Length},");

            // Bones array: name, localToWorldMatrix
            sb.AppendLine("  \"bones\": [");
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                var name = bone != null ? bone.name : "null";
                var boneNameFromPrefab = i < boneNames.Length ? boneNames[i] : "?";
                sb.Append("    { ");
                sb.Append($"\"index\": {i}, ");
                sb.Append($"\"name\": \"{name}\", ");
                sb.Append($"\"prefab_name\": \"{boneNameFromPrefab}\", ");
                sb.Append($"\"localToWorldMatrix\": {(bone != null ? M4ToJson(bone.localToWorldMatrix) : "null")}");
                sb.Append(i < bones.Length - 1 ? " },\n" : " }\n");
            }
            sb.AppendLine("  ],");

            // Bind poses array (from mesh — these are the v12 values we set)
            sb.AppendLine("  \"bindposes\": [");
            for (int i = 0; i < bindPoses.Length; i++)
            {
                var boneNameFromPrefab = i < boneNames.Length ? boneNames[i] : "?";
                sb.Append("    { ");
                sb.Append($"\"index\": {i}, ");
                sb.Append($"\"bone_name\": \"{boneNameFromPrefab}\", ");
                sb.Append($"\"matrix\": {M4ToJson(bindPoses[i])}");
                sb.Append(i < bindPoses.Length - 1 ? " },\n" : " }\n");
            }
            sb.AppendLine("  ],");

            // Dump v12 hardcoded bind poses (from s_chestRetargetedBPs) keyed by bone name
            sb.AppendLine("  \"v12_bindposes\": [");
            for (int i = 0; i < bones.Length; i++)
            {
                var bn = i < boneNames.Length ? boneNames[i] : "?";
                var hasV12 = s_chestRetargetedBPs.TryGetValue(bn, out var v12bp);
                sb.Append("    { ");
                sb.Append($"\"index\": {i}, ");
                sb.Append($"\"bone_name\": \"{bn}\", ");
                sb.Append($"\"has_v12\": {(hasV12 ? "true" : "false")}, ");
                sb.Append($"\"matrix\": {(hasV12 ? M4ToJson(v12bp) : (i < bindPoses.Length ? M4ToJson(bindPoses[i]) : "null"))}");
                sb.Append(i < bones.Length - 1 ? " },\n" : " }\n");
            }
            sb.AppendLine("  ],");

            // Manual skinning test using hardcoded vertex data from knightchest_mesh_data.json
            // Tests with BOTH mesh bind poses (Body(Clone) — likely wrong) and v12 hardcoded bind poses
            {
                // Build v12 bind pose array indexed by bone position (matching bones[] order)
                var v12BPs = new Matrix4x4[bones.Length];
                int v12Hits = 0;
                for (int i = 0; i < bones.Length; i++)
                {
                    var bn = i < boneNames.Length ? boneNames[i] : "";
                    if (s_chestRetargetedBPs.TryGetValue(bn, out var v12bp))
                    {
                        v12BPs[i] = v12bp;
                        v12Hits++;
                    }
                    else
                    {
                        v12BPs[i] = i < bindPoses.Length ? bindPoses[i] : Matrix4x4.identity;
                    }
                }
                sb.AppendLine($"  \"v12_bp_matched\": {v12Hits},");
                sb.AppendLine($"  \"v12_bp_total\": {s_chestRetargetedBPs.Count},");

                // Check if mesh bind poses match v12 values (to detect Body(Clone) overwrite)
                int bpMatch = 0, bpMismatch = 0;
                for (int i = 0; i < Math.Min(bindPoses.Length, v12BPs.Length); i++)
                {
                    bool match = true;
                    for (int r = 0; r < 4 && match; r++)
                        for (int c = 0; c < 4 && match; c++)
                            if (Math.Abs(bindPoses[i][r, c] - v12BPs[i][r, c]) > 1e-5f)
                                match = false;
                    if (match) bpMatch++; else bpMismatch++;
                }
                sb.AppendLine($"  \"mesh_vs_v12_match\": {bpMatch},");
                sb.AppendLine($"  \"mesh_vs_v12_mismatch\": {bpMismatch},");

                var testVerts = new[] {
                    new { pos = new Vector3(0.108580366f, 0.150748387f, 1.20352566f),
                          bi = new[]{1,49,2,0}, wt = new[]{0.892056406f, 0.0624691807f, 0.044204291f, 0.00127011503f} },
                    new { pos = new Vector3(0.167304620f, 0.089224882f, 1.19044089f),
                          bi = new[]{1,2,49,0}, wt = new[]{0.893103480f, 0.053666711f, 0.053229801f, 0f} },
                    new { pos = new Vector3(-0.0423966870f, 0.0548427030f, 1.21651220f),
                          bi = new[]{8,7,3,2}, wt = new[]{0.502074480f, 0.294174800f, 0.161498070f, 0.042252656f} },
                };
                sb.AppendLine("  \"skinning_test\": [");
                for (int ti = 0; ti < testVerts.Length; ti++)
                {
                    var tv = testVerts[ti];
                    // Skin with mesh bind poses (what mesh.bindposes says — may be Body(Clone))
                    Vector3 skinnedMesh = Vector3.zero;
                    for (int j = 0; j < 4; j++)
                    {
                        if (tv.wt[j] <= 0 || tv.bi[j] >= bones.Length || tv.bi[j] >= bindPoses.Length) continue;
                        var skinMat = bones[tv.bi[j]].localToWorldMatrix * bindPoses[tv.bi[j]];
                        skinnedMesh += tv.wt[j] * (Vector3)skinMat.MultiplyPoint3x4(tv.pos);
                    }
                    // Skin with v12 hardcoded bind poses (what we actually set in RemapArmorBones)
                    Vector3 skinnedV12 = Vector3.zero;
                    for (int j = 0; j < 4; j++)
                    {
                        if (tv.wt[j] <= 0 || tv.bi[j] >= bones.Length) continue;
                        var skinMat = bones[tv.bi[j]].localToWorldMatrix * v12BPs[tv.bi[j]];
                        skinnedV12 += tv.wt[j] * (Vector3)skinMat.MultiplyPoint3x4(tv.pos);
                    }
                    sb.Append($"    {{ \"vertex\": {ti}, ");
                    sb.Append($"\"original\": [{tv.pos.x:G9},{tv.pos.y:G9},{tv.pos.z:G9}], ");
                    sb.Append($"\"skinned_mesh_bp\": [{skinnedMesh.x:G9},{skinnedMesh.y:G9},{skinnedMesh.z:G9}], ");
                    sb.Append($"\"skinned_v12_bp\": [{skinnedV12.x:G9},{skinnedV12.y:G9},{skinnedV12.z:G9}]");
                    sb.Append(ti < testVerts.Length - 1 ? " },\n" : " }\n");
                }
                sb.AppendLine("  ]");
            }

            sb.AppendLine("}");

            // Write to BepInEx/plugins/ directory (always writable)
            var pluginsDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".";
            var outPath = Path.Combine(pluginsDir, "chest_runtime_matrices.json");
            File.WriteAllText(outPath, sb.ToString());
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Runtime matrix dump written to: {outPath}");
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Dumped {bones.Length} bones, {bindPoses.Length} bind poses");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Ashlands Reborn] Runtime matrix dump failed: {ex}");
        }
    }

    private static string M4ToJson(Matrix4x4 m)
    {
        // Row-major: m.mRC where R=row, C=col — matches Unity's Matrix4x4 layout
        return $"[{m.m00:G9},{m.m01:G9},{m.m02:G9},{m.m03:G9}," +
               $"{m.m10:G9},{m.m11:G9},{m.m12:G9},{m.m13:G9}," +
               $"{m.m20:G9},{m.m21:G9},{m.m22:G9},{m.m23:G9}," +
               $"{m.m30:G9},{m.m31:G9},{m.m32:G9},{m.m33:G9}]";
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
        marker.BodySwapApplied = false;
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

    /// <summary>
    /// Finds the Padded_Cuirrass SMR on any live Charred_Melee and dumps its matrices.
    /// Called synchronously BEFORE refresh.
    /// </summary>
    internal static void DumpChestMatricesNow()
    {
        // Find any Charred_Melee with our armor attached
        var humanoids = UObject.FindObjectsByType<Humanoid>(FindObjectsSortMode.None);
        foreach (var humanoid in humanoids)
        {
            if (!IsCharredMelee(humanoid.gameObject)) continue;
            var smrs = humanoid.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpSearch: Charred '{humanoid.gameObject.name}' has {smrs.Length} SMRs");
            foreach (var smr in smrs)
            {
                if (smr == null) continue;
                var meshName = smr.sharedMesh != null ? smr.sharedMesh.name : "null";
                Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpSearch:   SMR '{smr.name}', mesh='{meshName}', bones={smr.bones.Length}");
                if (smr.sharedMesh == null) continue;
                if (!smr.name.Equals("Padded_Cuirrass", StringComparison.OrdinalIgnoreCase)) continue;

                Plugin.Log?.LogInfo($"[Ashlands Reborn] Found chest SMR: '{smr.name}', mesh='{meshName}', bones={smr.bones.Length}");
                var bones = smr.bones;
                var boneNames = new string[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    boneNames[i] = bones[i] != null ? bones[i].name : "null";

                // Try BakeMesh to get the actual rendered vertex positions
                try
                {
                    var bakedMesh = new Mesh();
                    smr.BakeMesh(bakedMesh);
                    var bakedVerts = bakedMesh.vertices;
                    var bakedTris = bakedMesh.triangles;
                    Plugin.Log?.LogInfo($"[Ashlands Reborn] BakeMesh succeeded: {bakedVerts.Length} verts, {bakedTris.Length/3} tris");

                    // Write baked mesh to separate JSON
                    var pluginsDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".";
                    var bakeSb = new StringBuilder();
                    bakeSb.AppendLine("{");
                    bakeSb.AppendLine($"  \"vertex_count\": {bakedVerts.Length},");
                    bakeSb.AppendLine($"  \"triangle_count\": {bakedTris.Length / 3},");
                    bakeSb.AppendLine("  \"vertices\": [");
                    for (int i = 0; i < bakedVerts.Length; i++)
                    {
                        bakeSb.Append($"    [{bakedVerts[i].x:G9},{bakedVerts[i].y:G9},{bakedVerts[i].z:G9}]");
                        bakeSb.AppendLine(i < bakedVerts.Length - 1 ? "," : "");
                    }
                    bakeSb.AppendLine("  ],");
                    bakeSb.AppendLine("  \"triangles\": [");
                    for (int i = 0; i < bakedTris.Length; i += 3)
                    {
                        bakeSb.Append($"    [{bakedTris[i]},{bakedTris[i+1]},{bakedTris[i+2]}]");
                        bakeSb.AppendLine(i + 3 < bakedTris.Length ? "," : "");
                    }
                    bakeSb.AppendLine("  ]");
                    bakeSb.AppendLine("}");
                    var bakePath = Path.Combine(pluginsDir, "chest_baked_mesh.json");
                    File.WriteAllText(bakePath, bakeSb.ToString());
                    Plugin.Log?.LogInfo($"[Ashlands Reborn] Baked mesh written to: {bakePath}");
                    UObject.Destroy(bakedMesh);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] BakeMesh failed: {ex.Message}");
                }

                DumpRuntimeMatrices(smr, boneNames);
                DumpBodyCloneMeshData(smr);
                return;
            }
        }
        Plugin.Log?.LogWarning("[Ashlands Reborn] No Padded_Cuirrass SMR found on any Charred_Melee for matrix dump.");
    }

    /// <summary>
    /// Dumps Body(Clone) mesh vertices and bone weights to bodyclone_mesh_data.json.
    /// The Body(Clone) mesh is marked non-readable at runtime, so this tries multiple
    /// approaches: Instantiate (which may copy GPU data back), direct access, etc.
    /// </summary>
    private static void DumpBodyCloneMeshData(SkinnedMeshRenderer smr)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null)
        {
            Plugin.Log?.LogWarning("[BodyCloneDump] sharedMesh is null, skipping.");
            return;
        }

        var pluginsDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".";
        Plugin.Log?.LogInfo($"[BodyCloneDump] Mesh: '{mesh.name}', verts={mesh.vertexCount}, readable={mesh.isReadable}, subMeshCount={mesh.subMeshCount}");

        Vector3[]? vertices = null;
        BoneWeight[]? boneWeights = null;
        int[]? triangles = null;
        string readMethod = "none";

        // --- Method 1: Direct access (works if mesh happens to be readable) ---
        if (mesh.isReadable)
        {
            try
            {
                vertices = mesh.vertices;
                boneWeights = mesh.boneWeights;
                triangles = mesh.triangles;
                readMethod = "direct";
                Plugin.Log?.LogInfo($"[BodyCloneDump] Direct read succeeded: {vertices.Length} verts, {boneWeights.Length} weights");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[BodyCloneDump] Direct read failed despite isReadable=true: {ex.Message}");
            }
        }

        // --- Method 2: Instantiate creates a clone that may be readable ---
        if (vertices == null)
        {
            try
            {
                var clone = UObject.Instantiate(mesh);
                Plugin.Log?.LogInfo($"[BodyCloneDump] Instantiate clone: readable={clone.isReadable}, verts={clone.vertexCount}");

                if (clone.isReadable)
                {
                    vertices = clone.vertices;
                    boneWeights = clone.boneWeights;
                    triangles = clone.triangles;
                    readMethod = "instantiate_clone";
                    Plugin.Log?.LogInfo($"[BodyCloneDump] Clone read succeeded: {vertices.Length} verts, {boneWeights.Length} weights");
                }
                else
                {
                    // Even if isReadable is false, try accessing — the clone may have CPU data
                    try
                    {
                        vertices = clone.vertices;
                        boneWeights = clone.boneWeights;
                        triangles = clone.triangles;
                        readMethod = "instantiate_clone_forced";
                        Plugin.Log?.LogInfo($"[BodyCloneDump] Clone forced read succeeded: {vertices.Length} verts, {boneWeights.Length} weights");
                    }
                    catch (Exception ex2)
                    {
                        Plugin.Log?.LogInfo($"[BodyCloneDump] Clone forced read failed: {ex2.Message}");
                    }
                }

                UObject.Destroy(clone);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[BodyCloneDump] Instantiate failed: {ex.Message}");
            }
        }

        // --- Method 3: BakeMesh for vertex positions + try to get bone weights separately ---
        if (vertices == null)
        {
            try
            {
                var bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);
                vertices = bakedMesh.vertices;
                triangles = bakedMesh.triangles;
                readMethod = "bakemesh_verts_only";
                Plugin.Log?.LogInfo($"[BodyCloneDump] BakeMesh fallback: {vertices.Length} verts (NO bone weights — these are baked world-space positions, not input vertices)");
                UObject.Destroy(bakedMesh);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[BodyCloneDump] BakeMesh fallback failed: {ex.Message}");
            }
        }

        if (vertices == null)
        {
            Plugin.Log?.LogWarning("[BodyCloneDump] All read methods failed. No data to dump.");
            return;
        }

        // --- Serialize to JSON ---
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"mesh_name\": \"{mesh.name}\",");
        sb.AppendLine($"  \"vertex_count\": {vertices.Length},");
        sb.AppendLine($"  \"is_readable\": {(mesh.isReadable ? "true" : "false")},");
        sb.AppendLine($"  \"read_method\": \"{readMethod}\",");
        sb.AppendLine($"  \"has_bone_weights\": {(boneWeights != null ? "true" : "false")},");
        sb.AppendLine($"  \"submesh_count\": {mesh.subMeshCount},");

        // Bone names from the SMR
        var bones = smr.bones;
        sb.Append("  \"bone_names\": [");
        for (int i = 0; i < bones.Length; i++)
        {
            sb.Append($"\"{(bones[i] != null ? bones[i].name : "null")}\"");
            if (i < bones.Length - 1) sb.Append(",");
        }
        sb.AppendLine("],");

        // Vertices
        sb.AppendLine("  \"vertices\": [");
        for (int i = 0; i < vertices.Length; i++)
        {
            sb.Append($"    [{vertices[i].x:G9},{vertices[i].y:G9},{vertices[i].z:G9}]");
            sb.AppendLine(i < vertices.Length - 1 ? "," : "");
        }
        sb.AppendLine("  ],");

        // Triangles
        if (triangles != null)
        {
            sb.AppendLine("  \"triangles\": [");
            for (int i = 0; i < triangles.Length; i += 3)
            {
                sb.Append($"    [{triangles[i]},{triangles[i + 1]},{triangles[i + 2]}]");
                sb.AppendLine(i + 3 < triangles.Length ? "," : "");
            }
            sb.AppendLine("  ],");
        }

        // Bone weights
        if (boneWeights != null)
        {
            sb.AppendLine("  \"bone_weights\": [");
            for (int i = 0; i < boneWeights.Length; i++)
            {
                var w = boneWeights[i];
                sb.Append($"    {{\"b0\":{w.boneIndex0},\"w0\":{w.weight0:G9},\"b1\":{w.boneIndex1},\"w1\":{w.weight1:G9},\"b2\":{w.boneIndex2},\"w2\":{w.weight2:G9},\"b3\":{w.boneIndex3},\"w3\":{w.weight3:G9}}}");
                sb.AppendLine(i < boneWeights.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");
        }

        // Bind poses from the runtime mesh
        try
        {
            var bindPoses = mesh.bindposes;
            sb.AppendLine($"  \"bindpose_count\": {bindPoses.Length},");
            sb.AppendLine("  \"bindposes\": [");
            for (int i = 0; i < bindPoses.Length; i++)
            {
                var bname = i < bones.Length && bones[i] != null ? bones[i].name : $"bone_{i}";
                sb.Append($"    {{\"index\": {i}, \"bone_name\": \"{bname}\", \"matrix\": {M4ToJson(bindPoses[i])}}}");
                sb.AppendLine(i < bindPoses.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ]");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[BodyCloneDump] Could not read bindposes: {ex.Message}");
            // Remove trailing comma from previous section
            sb.AppendLine("  \"bindposes_error\": \"" + ex.Message.Replace("\"", "'") + "\"");
        }

        sb.AppendLine("}");

        var outPath = Path.Combine(pluginsDir, "bodyclone_mesh_data.json");
        File.WriteAllText(outPath, sb.ToString());
        Plugin.Log?.LogInfo($"[BodyCloneDump] Written to: {outPath} ({vertices.Length} verts, weights={boneWeights != null}, method={readMethod})");
    }

    // -------------------------------------------------------------------------
    // Extraction #2: Player body mesh + Charred sinew positioning
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dumps player body mesh data and charred sinew positioning.
    /// Called from Plugin.cs on F11 press.
    /// </summary>
    internal static void DumpPlayerAndSinewData()
    {
        var pluginsDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".";
        DumpPlayerBodyMesh(pluginsDir);
        DumpRestPosePlayerMeshes(pluginsDir);
        DumpCharredSinewData(pluginsDir);
    }

    /// <summary>
    /// Extracts all player armor SMRs in rest/bind pose (A-pose) for Blender B2 workflow.
    /// Output: player_restpose_&lt;smrname&gt;.json per SMR.
    /// </summary>
    private static void DumpRestPosePlayerMeshes(string pluginsDir)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] DumpRestPose: No local player found.");
            return;
        }

        var vis = player.GetComponent<VisEquipment>();
        if (vis == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] DumpRestPose: No VisEquipment.");
            return;
        }

        Plugin.Log?.LogInfo("[Ashlands Reborn] === Rest-pose extraction starting ===");

        // Dump the bare body
        if (vis.m_bodyModel?.sharedMesh != null)
            DumpRestPoseSMR(vis.m_bodyModel, pluginsDir);

        // Dump every armor SMR
        var allSmrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int dumped = 0;
        foreach (var smr in allSmrs)
        {
            if (smr.sharedMesh == null || smr == vis.m_bodyModel) continue;
            DumpRestPoseSMR(smr, pluginsDir);
            dumped++;
        }

        Plugin.Log?.LogInfo($"[Ashlands Reborn] === Rest-pose extraction complete: {dumped + 1} SMRs dumped ===");
    }

    /// <summary>
    /// Extracts the local player's body mesh: vertices, triangles, normals,
    /// bone weights, bone names, bind poses, and BakeMesh verification.
    /// Output: player_body_runtime.json
    /// </summary>
    private static void DumpPlayerBodyMesh(string pluginsDir)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] DumpPlayerBody: No local player found.");
            return;
        }

        var vis = player.GetComponent<VisEquipment>();
        if (vis == null || vis.m_bodyModel == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] DumpPlayerBody: No VisEquipment or m_bodyModel.");
            return;
        }

        // Always dump the bare body (m_bodyModel) first — it renders continuously and provides
        // the skin visible at neck, wrists, ankles, etc. between armor pieces.
        if (vis.m_bodyModel?.sharedMesh != null)
            DumpSinglePlayerSMR(vis.m_bodyModel, pluginsDir);

        // Dump every armor SMR (each armor slot creates its own combined body+piece SMR).
        var allSmrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int dumped = 0;
        foreach (var smr in allSmrs)
        {
            if (smr.sharedMesh == null || smr == vis.m_bodyModel) continue;
            DumpSinglePlayerSMR(smr, pluginsDir);
            dumped++;
        }
        if (dumped == 0)
            Plugin.Log?.LogWarning("[Ashlands Reborn] DumpPlayerBody: no armor SMRs found (is armor equipped?).");

        // Also dump rigid MeshRenderers (helms attach this way — parented to head bone, not skinned).
        // Vertices are transformed to world space so they sit correctly relative to the baked body.
        var allMrs = player.GetComponentsInChildren<MeshRenderer>(true);
        Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpPlayerBody: found {allMrs.Length} MeshRenderers on player");
        foreach (var mr in allMrs)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh;
            var t = mr.transform;
            var wm = t.localToWorldMatrix;
            Plugin.Log?.LogInfo($"[Ashlands Reborn]   MeshRenderer '{mr.name}': verts={mesh.vertexCount}, readable={mesh.isReadable}, parent='{t.parent?.name}'");
            Plugin.Log?.LogInfo($"[Ashlands Reborn]     worldPos=({wm.m03:F4},{wm.m13:F4},{wm.m23:F4}) scale=({t.lossyScale.x:F4},{t.lossyScale.y:F4},{t.lossyScale.z:F4})");
            Plugin.Log?.LogInfo($"[Ashlands Reborn]     L2W={M4ToJson(wm)}");
            if (!mesh.isReadable) continue;
            DumpRigidMeshRenderer(mr, mf, pluginsDir);
        }
    }

    /// <summary>
    /// Dumps a rigid MeshRenderer (e.g. helm) with vertices transformed to world space.
    /// </summary>
    private static void DumpRigidMeshRenderer(MeshRenderer mr, MeshFilter mf, string pluginsDir)
    {
        var mesh = mf.sharedMesh;
        var l2w = mr.transform.localToWorldMatrix;
        var localVerts = mesh.vertices;
        var localNormals = mesh.normals;
        var tris = mesh.triangles;

        Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpPlayerBody: baking rigid MeshRenderer '{mr.name}' ({localVerts.Length} verts, parent='{mr.transform.parent?.name}')");

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"smr_name\": \"{mr.name}\",");
        sb.AppendLine($"  \"baked_vertex_count\": {localVerts.Length},");

        sb.AppendLine("  \"baked_vertices\": [");
        for (int i = 0; i < localVerts.Length; i++)
        {
            var wp = l2w.MultiplyPoint3x4(localVerts[i]);
            sb.Append($"    [{wp.x:G9},{wp.y:G9},{wp.z:G9}]");
            sb.AppendLine(i < localVerts.Length - 1 ? "," : "");
        }
        sb.AppendLine("  ],");

        sb.AppendLine("  \"baked_normals\": [");
        for (int i = 0; i < localNormals.Length; i++)
        {
            var wn = l2w.MultiplyVector(localNormals[i]).normalized;
            sb.Append($"    [{wn.x:G9},{wn.y:G9},{wn.z:G9}]");
            sb.AppendLine(i < localNormals.Length - 1 ? "," : "");
        }
        sb.AppendLine("  ],");

        sb.AppendLine($"  \"baked_triangle_count\": {tris.Length / 3},");
        sb.AppendLine("  \"baked_triangles\": [");
        for (int i = 0; i < tris.Length; i += 3)
        {
            sb.Append($"    [{tris[i]},{tris[i+1]},{tris[i+2]}]");
            sb.AppendLine(i < tris.Length - 3 ? "," : "");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        var safeName = System.Text.RegularExpressions.Regex.Replace(mr.name, @"[^\w\-]", "_");
        var outPath = Path.Combine(pluginsDir, $"player_body_{safeName}.json");
        File.WriteAllText(outPath, sb.ToString());
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Rigid mesh dump written to: {outPath}");
    }

    /// <summary>
    /// BakeMesh-dumps a single player armor SMR to player_body_&lt;smrname&gt;.json.
    /// Only baked geometry (verts, normals, tris) is captured — sufficient for Blender import.
    /// </summary>
    private static void DumpSinglePlayerSMR(SkinnedMeshRenderer smr, string pluginsDir)
    {
        Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpPlayerBody: baking SMR '{smr.name}' ({smr.sharedMesh.vertexCount} verts, {smr.sharedMesh.subMeshCount} submeshes)");
        try
        {
            var bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            var bakedVerts   = bakedMesh.vertices;
            var bakedNormals = bakedMesh.normals;
            var bakedTris    = bakedMesh.triangles;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"smr_name\": \"{smr.name}\",");
            sb.AppendLine($"  \"smr_l2w\": {M4ToJson(smr.transform.localToWorldMatrix)},");
            sb.AppendLine($"  \"baked_vertex_count\": {bakedVerts.Length},");

            sb.AppendLine("  \"baked_vertices\": [");
            for (int i = 0; i < bakedVerts.Length; i++)
            {
                sb.Append($"    [{bakedVerts[i].x:G9},{bakedVerts[i].y:G9},{bakedVerts[i].z:G9}]");
                sb.AppendLine(i < bakedVerts.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"baked_normals\": [");
            for (int i = 0; i < bakedNormals.Length; i++)
            {
                sb.Append($"    [{bakedNormals[i].x:G9},{bakedNormals[i].y:G9},{bakedNormals[i].z:G9}]");
                sb.AppendLine(i < bakedNormals.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            sb.AppendLine($"  \"baked_triangle_count\": {bakedTris.Length / 3},");
            sb.AppendLine("  \"baked_triangles\": [");
            for (int i = 0; i < bakedTris.Length; i += 3)
            {
                sb.Append($"    [{bakedTris[i]},{bakedTris[i+1]},{bakedTris[i+2]}]");
                sb.AppendLine(i < bakedTris.Length - 3 ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            UObject.Destroy(bakedMesh);

            var safeName = System.Text.RegularExpressions.Regex.Replace(smr.name, @"[^\w\-]", "_");
            var outPath = Path.Combine(pluginsDir, $"player_body_{safeName}.json");
            File.WriteAllText(outPath, sb.ToString());
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Player body mesh dump written to: {outPath}");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Ashlands Reborn] DumpSinglePlayerSMR '{smr.name}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Dumps a single player armor SMR in rest/bind pose (A-pose) by temporarily resetting
    /// all bones to their bind-pose transforms before calling BakeMesh.
    /// Output includes UVs — suitable for Blender import + rigging workflow.
    /// Output: player_restpose_&lt;smrname&gt;.json
    /// </summary>
    private static void DumpRestPoseSMR(SkinnedMeshRenderer smr, string pluginsDir)
    {
        Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpRestPose: baking SMR '{smr.name}' in bind pose ({smr.sharedMesh.vertexCount} verts)");
        try
        {
            var bones = smr.bones;
            var bindPoses = smr.sharedMesh.bindposes;

            // Save original bone local transforms
            var savedLocal = new (Vector3 pos, Quaternion rot)[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                savedLocal[i] = (bones[i].localPosition, bones[i].localRotation);
            }

            // Reset all bone local rotations to identity → produces A/T-pose for humanoid rigs.
            // This avoids fighting the character's 100× scale hierarchy that makes
            // exact bind-pose matrix reconstruction produce wrong-scale output.
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].localRotation = Quaternion.identity;
            }

            // BakeMesh in rest pose
            var bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            var verts   = bakedMesh.vertices;
            var normals = bakedMesh.normals;
            var tris    = bakedMesh.triangles;
            var uvs     = bakedMesh.uv;   // UV channel 0

            Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpRestPose: baked {verts.Length} verts, {tris.Length/3} tris, {uvs.Length} uvs");

            // Restore original bone transforms
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                bones[i].localPosition = savedLocal[i].pos;
                bones[i].localRotation = savedLocal[i].rot;
            }

            // Serialize JSON
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"smr_name\": \"{smr.name}\",");
            sb.AppendLine($"  \"pose\": \"rest_bind\",");
            sb.AppendLine($"  \"smr_l2w\": {M4ToJson(smr.transform.localToWorldMatrix)},");
            sb.AppendLine($"  \"vertex_count\": {verts.Length},");

            // Bone names (for later rigging reference)
            sb.Append("  \"bone_names\": [");
            for (int i = 0; i < bones.Length; i++)
            {
                sb.Append($"\"{(bones[i] != null ? bones[i].name : "null")}\"");
                if (i < bones.Length - 1) sb.Append(",");
            }
            sb.AppendLine("],");

            // Bind poses
            sb.AppendLine("  \"bind_poses\": [");
            for (int i = 0; i < bindPoses.Length; i++)
            {
                sb.Append($"    {M4ToJson(bindPoses[i])}");
                sb.AppendLine(i < bindPoses.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            // Vertices
            sb.AppendLine("  \"vertices\": [");
            for (int i = 0; i < verts.Length; i++)
            {
                sb.Append($"    [{verts[i].x:G9},{verts[i].y:G9},{verts[i].z:G9}]");
                sb.AppendLine(i < verts.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            // Normals
            sb.AppendLine("  \"normals\": [");
            for (int i = 0; i < normals.Length; i++)
            {
                sb.Append($"    [{normals[i].x:G9},{normals[i].y:G9},{normals[i].z:G9}]");
                sb.AppendLine(i < normals.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            // UVs
            sb.AppendLine("  \"uvs\": [");
            for (int i = 0; i < uvs.Length; i++)
            {
                sb.Append($"    [{uvs[i].x:G9},{uvs[i].y:G9}]");
                sb.AppendLine(i < uvs.Length - 1 ? "," : "");
            }
            sb.AppendLine("  ],");

            // Triangles
            sb.AppendLine($"  \"triangle_count\": {tris.Length / 3},");
            sb.AppendLine("  \"triangles\": [");
            for (int i = 0; i < tris.Length; i += 3)
            {
                sb.Append($"    [{tris[i]},{tris[i+1]},{tris[i+2]}]");
                sb.AppendLine(i < tris.Length - 3 ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            UObject.Destroy(bakedMesh);

            var safeName = System.Text.RegularExpressions.Regex.Replace(smr.name, @"[^\w\-]", "_");
            var outPath = Path.Combine(pluginsDir, $"player_restpose_{safeName}.json");
            File.WriteAllText(outPath, sb.ToString());
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Rest-pose dump written to: {outPath}");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Ashlands Reborn] DumpRestPoseSMR '{smr.name}' failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Extracts Charred_Melee sinew mesh positioning data: transform hierarchy,
    /// bone data, bind poses, and BakeMesh for both Body and Sinew SMRs.
    /// Output: charred_sinew_data.json
    /// </summary>
    private static void DumpCharredSinewData(string pluginsDir)
    {
        var humanoids = UObject.FindObjectsByType<Humanoid>(FindObjectsSortMode.None);
        Humanoid? charred = null;
        foreach (var h in humanoids)
        {
            if (IsCharredMelee(h.gameObject)) { charred = h; break; }
        }
        if (charred == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] DumpSinew: No Charred_Melee found in scene.");
            return;
        }

        var allSMRs = charred.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer? sinewSMR = null;
        SkinnedMeshRenderer? bodySMR = null;
        SkinnedMeshRenderer? skullSMR = null;
        SkinnedMeshRenderer? eyesSMR = null;

        foreach (var smr in allSMRs)
        {
            if (smr == null) continue;
            if (IsUnderAttachSkin(smr.transform)) continue; // skip armor SMRs
            var n = smr.name;
            if (n.Equals("Sinew", StringComparison.OrdinalIgnoreCase)) sinewSMR = smr;
            else if (n.Equals("Body", StringComparison.OrdinalIgnoreCase)) bodySMR = smr;
            else if (n.Equals("Skull", StringComparison.OrdinalIgnoreCase)) skullSMR = smr;
            else if (n.Equals("Eyes", StringComparison.OrdinalIgnoreCase)) eyesSMR = smr;
        }

        if (sinewSMR == null)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] DumpSinew: No 'Sinew' SMR found on Charred_Melee.");
            return;
        }

        Plugin.Log?.LogInfo($"[Ashlands Reborn] DumpSinew: Found Sinew SMR, bones={sinewSMR.bones.Length}");

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"source\": \"Charred_Melee runtime sinew + body comparison\",");

        // Dump each native SMR's transform + mesh data
        string[] partNames = { "Body", "Sinew", "Skull", "Eyes" };
        SkinnedMeshRenderer?[] partSMRs = { bodySMR, sinewSMR, skullSMR, eyesSMR };

        sb.AppendLine("  \"parts\": [");
        for (int p = 0; p < partNames.Length; p++)
        {
            var smr = partSMRs[p];
            if (smr == null) continue;

            sb.AppendLine("    {");
            sb.AppendLine($"      \"name\": \"{partNames[p]}\",");

            // Transform hierarchy
            sb.AppendLine("      \"transform\": {");
            sb.AppendLine($"        \"gameobject\": \"{smr.gameObject.name}\",");
            sb.AppendLine($"        \"parent\": \"{smr.transform.parent?.name ?? "null"}\",");
            sb.AppendLine($"        \"localPosition\": [{smr.transform.localPosition.x:G9},{smr.transform.localPosition.y:G9},{smr.transform.localPosition.z:G9}],");
            sb.AppendLine($"        \"localRotation\": [{smr.transform.localRotation.x:G9},{smr.transform.localRotation.y:G9},{smr.transform.localRotation.z:G9},{smr.transform.localRotation.w:G9}],");
            sb.AppendLine($"        \"localScale\": [{smr.transform.localScale.x:G9},{smr.transform.localScale.y:G9},{smr.transform.localScale.z:G9}],");
            sb.AppendLine($"        \"worldPosition\": [{smr.transform.position.x:G9},{smr.transform.position.y:G9},{smr.transform.position.z:G9}],");
            sb.AppendLine($"        \"localToWorldMatrix\": {M4ToJson(smr.transform.localToWorldMatrix)}");
            sb.AppendLine("      },");

            // Parent chain (up to 6 levels)
            sb.Append("      \"parent_chain\": [");
            var t = smr.transform.parent;
            for (int d = 0; d < 6 && t != null; d++)
            {
                sb.Append($"\"{t.name}\"");
                t = t.parent;
                if (t != null && d < 5) sb.Append(",");
            }
            sb.AppendLine("],");

            // SMR specifics
            sb.AppendLine($"      \"rootBone\": \"{smr.rootBone?.name ?? "null"}\",");

            var mesh = smr.sharedMesh;
            if (mesh != null)
            {
                sb.AppendLine($"      \"mesh_name\": \"{mesh.name}\",");
                sb.AppendLine($"      \"vertex_count\": {mesh.vertexCount},");
                sb.AppendLine($"      \"mesh_is_readable\": {(mesh.isReadable ? "true" : "false")},");

                var bones = smr.bones;
                var bps = mesh.bindposes;

                // Bone names
                sb.Append("      \"bone_names\": [");
                for (int i = 0; i < bones.Length; i++)
                {
                    sb.Append($"\"{(bones[i] != null ? bones[i].name : "null")}\"");
                    if (i < bones.Length - 1) sb.Append(",");
                }
                sb.AppendLine("],");

                // Bind poses
                sb.AppendLine("      \"bind_poses\": [");
                for (int i = 0; i < bps.Length; i++)
                {
                    sb.Append($"        {{\"index\":{i},\"bone\":\"{(i < bones.Length && bones[i] != null ? bones[i].name : "?")}\",\"matrix\":{M4ToJson(bps[i])}}}");
                    sb.AppendLine(i < bps.Length - 1 ? "," : "");
                }
                sb.AppendLine("      ],");

                // Bone L2W (only first 10 for brevity on Body, all for Sinew)
                int boneLimit = partNames[p] == "Sinew" ? bones.Length : Math.Min(bones.Length, 10);
                sb.AppendLine($"      \"bone_local_to_world_count\": {bones.Length},");
                sb.AppendLine("      \"bone_local_to_world\": [");
                for (int i = 0; i < boneLimit; i++)
                {
                    if (bones[i] != null)
                        sb.Append($"        {{\"name\":\"{bones[i].name}\",\"matrix\":{M4ToJson(bones[i].localToWorldMatrix)}}}");
                    else
                        sb.Append("        {\"name\":\"null\",\"matrix\":null}");
                    sb.AppendLine(i < boneLimit - 1 ? "," : "");
                }
                sb.AppendLine("      ],");

                // Mesh data if readable (vertices, triangles, bone weights)
                if (mesh.isReadable)
                {
                    var verts = mesh.vertices;
                    sb.AppendLine("      \"vertices\": [");
                    for (int i = 0; i < verts.Length; i++)
                    {
                        sb.Append($"        [{verts[i].x:G9},{verts[i].y:G9},{verts[i].z:G9}]");
                        sb.AppendLine(i < verts.Length - 1 ? "," : "");
                    }
                    sb.AppendLine("      ],");

                    var tris = mesh.triangles;
                    sb.AppendLine("      \"triangles\": [");
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        sb.Append($"        [{tris[i]},{tris[i + 1]},{tris[i + 2]}]");
                        sb.AppendLine(i + 3 < tris.Length ? "," : "");
                    }
                    sb.AppendLine("      ],");

                    var boneWeights = mesh.boneWeights;
                    if (boneWeights != null && boneWeights.Length > 0)
                    {
                        sb.AppendLine("      \"bone_weights\": [");
                        for (int i = 0; i < boneWeights.Length; i++)
                        {
                            var bw = boneWeights[i];
                            sb.Append($"        {{\"b0\":{bw.boneIndex0},\"w0\":{bw.weight0:G9},\"b1\":{bw.boneIndex1},\"w1\":{bw.weight1:G9},\"b2\":{bw.boneIndex2},\"w2\":{bw.weight2:G9},\"b3\":{bw.boneIndex3},\"w3\":{bw.weight3:G9}}}");
                            sb.AppendLine(i < boneWeights.Length - 1 ? "," : "");
                        }
                        sb.AppendLine("      ],");
                    }
                }
            }

            // BakeMesh
            try
            {
                var bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);
                var bakedVerts = bakedMesh.vertices;
                sb.AppendLine($"      \"baked_vertex_count\": {bakedVerts.Length},");
                sb.AppendLine("      \"baked_vertices\": [");
                for (int i = 0; i < bakedVerts.Length; i++)
                {
                    sb.Append($"        [{bakedVerts[i].x:G9},{bakedVerts[i].y:G9},{bakedVerts[i].z:G9}]");
                    sb.AppendLine(i < bakedVerts.Length - 1 ? "," : "");
                }
                sb.AppendLine("      ]");
                UObject.Destroy(bakedMesh);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"      \"baked_error\": \"{ex.Message.Replace("\"", "'")}\"");
            }

            sb.Append("    }");
            sb.AppendLine(p < partNames.Length - 1 ? "," : "");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        var outPath = Path.Combine(pluginsDir, "charred_sinew_data.json");
        File.WriteAllText(outPath, sb.ToString());
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred sinew data written to: {outPath}");
    }

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

                if (marker != null) { marker.OriginalChestItem = ""; marker.ChestSwapped = false; marker.BodySwapApplied = false; }
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

        // Cache the player body mesh the first time a Player awakens
        if (_cachedPlayerBodyMesh == null && __instance is Player player)
        {
            var vis = player.GetComponent<VisEquipment>();
            if (vis?.m_bodyModel?.sharedMesh != null)
            {
                _cachedPlayerBodyMesh = UObject.Instantiate(vis.m_bodyModel.sharedMesh);
                _cachedPlayerBodyMesh.name = "PlayerBodyCached";
                _cachedPlayerBodyMaterial = vis.m_bodyModel.sharedMaterial;
                var playerBones = vis.m_bodyModel.bones;
                _cachedPlayerBoneNames = new string[playerBones.Length];
                for (int i = 0; i < playerBones.Length; i++)
                    _cachedPlayerBoneNames[i] = playerBones[i]?.name ?? "";
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Cached player body mesh: {_cachedPlayerBodyMesh.vertexCount} verts, {playerBones.Length} bones");
            }
        }

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
    public bool BodySwapApplied;
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
