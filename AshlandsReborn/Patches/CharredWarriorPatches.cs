using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// <summary>
/// Visual transformation for Charred_Melee (the Ashlands greatsword enemy).
/// Phase 1 (sword): prefixes VisEquipment.SetRightItem to replace charred_greatsword_* with THSwordKrom.
/// Phase 3 (armor): prefixes the private SetChestEquipped/SetLegEquipped/SetHelmetEquipped methods to
///   inject VanillaMetal armor hashes at the visual-instantiation level, bypassing ZDO timing entirely.
/// Behavior (attacks, damage, AI) is untouched throughout.
/// Phase 0 dump: fires once per session on first Charred_Melee spawn.
/// </summary>
[HarmonyPatch]
internal static class CharredWarriorPatches
{
    private const string CharredMeleePrefab = "Charred_Melee";
    private const string KromPrefabName     = "THSwordKrom";
    private const string CharredSwordPrefix = "charred_greatsword";

    // VanillaMetal armor prefab names
    private const string ArmorChest  = "ArmorIronChest";
    private const string ArmorHelmet = "HelmetFlametal";
    private const string ArmorLegs   = "ArmorMageLegs_Ashlands";

    // Pre-compute stable hashes using Valheim's algorithm (same as string.GetStableHashCode()
    // in assembly_valheim, which is inaccessible from external assemblies).
    private static readonly int HashChest  = StableHash(ArmorChest);
    private static readonly int HashHelmet = StableHash(ArmorHelmet);
    private static readonly int HashLegs   = StableHash(ArmorLegs);

    // Valheim's stable string hash — matches the internal GetStableHashCode() extension method.
    private static int StableHash(string str)
    {
        int hash1 = 5381;
        int hash2 = hash1;
        for (int i = 0; i < str.Length; i += 2)
        {
            hash1 = ((hash1 << 5) + hash1) ^ str[i];
            if (i + 1 < str.Length)
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
        }
        return hash1 + hash2 * 1566083941;
    }

    // Reflection cache — private VisEquipment fields
    private static readonly FieldInfo? FRightItem =
        typeof(VisEquipment).GetField("m_rightItem", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FCurrentChestHash =
        typeof(VisEquipment).GetField("m_currentChestItemHash", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FCurrentLegHash =
        typeof(VisEquipment).GetField("m_currentLegItemHash", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FCurrentHelmetHash =
        typeof(VisEquipment).GetField("m_currentHelmetItemHash", BindingFlags.Instance | BindingFlags.NonPublic);

    // Armor instance lists — read after AttachArmor to locate newly-created SMRs
    private static readonly FieldInfo? FChestInstances =
        typeof(VisEquipment).GetField("m_chestItemInstances", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FLegInstances =
        typeof(VisEquipment).GetField("m_legItemInstances", BindingFlags.Instance | BindingFlags.NonPublic);
    // Helmet is a single GameObject (not a list), attached via AttachItem to the m_helmet bone
    private static readonly FieldInfo? FHelmetInstance =
        typeof(VisEquipment).GetField("m_helmetItemInstance", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? FRightItemInstance =
        typeof(VisEquipment).GetField("m_rightItemInstance", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool _suppressSwordSwap;
    private static int  _swapLogCount;
    private static bool _dumpDone;

    // Sword swap active when master switch + EnableCharredWarriorSwap are on
    private static bool ShouldSwap() =>
        (Plugin.MasterSwitch?.Value ?? false) &&
        (Plugin.EnableCharredWarriorSwap?.Value ?? false);

    // Armor swap active when sword swap is on + armor mode is VanillaMetal
    private static bool ShouldApplyArmor() =>
        ShouldSwap() &&
        string.Equals(Plugin.EnableCharredWarriorArmorSwap?.Value, "VanillaMetal",
            StringComparison.OrdinalIgnoreCase);

    private static string GetPrefabName(GameObject go)
    {
        var name = go.name;
        var idx = name.IndexOf('(');
        return idx >= 0 ? name.Substring(0, idx).Trim() : name;
    }

    private static bool IsCharredMelee(GameObject go) =>
        GetPrefabName(go) == CharredMeleePrefab;

    private static Transform? FindInChildren(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var found = FindInChildren(parent.GetChild(i), name);
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
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred_Melee sword: '{marker.OriginalRightItem}' → '{KromPrefabName}'");

        marker.KromScaled = false;
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
        if (marker != null && marker.KromScaled) return;

        __instance.StartCoroutine(ScaleKromAfterAttach(__instance, marker));
    }

    private static System.Collections.IEnumerator ScaleKromAfterAttach(VisEquipment vis, AshlandsRebornCharredSwapped? marker)
    {
        yield return null;
        yield return null;

        if (vis == null || !ShouldSwap()) yield break;

        var weaponGo = FRightItemInstance?.GetValue(vis) as GameObject;
        if (weaponGo == null)
        {
            var attachR = FindInChildren(vis.transform, "Attach.r");
            if (attachR != null && attachR.childCount > 0)
                weaponGo = attachR.GetChild(0).gameObject;
            if (weaponGo == null)
            {
                var rightHand = FindInChildren(vis.transform, "RightHand");
                if (rightHand != null && rightHand.childCount > 0)
                    weaponGo = rightHand.GetChild(0).gameObject;
            }
        }

        if (weaponGo != null)
        {
            var scale = Plugin.CharredWarriorKromScale?.Value ?? 1.16f;
            weaponGo.transform.localScale *= scale;
            if (marker != null) marker.KromScaled = true;
            if (_swapLogCount <= 5)
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Krom scaled by {scale}x for Charred");
        }
    }

    // -------------------------------------------------------------------------
    // Phase 3: Armor swap — prefix the private hash-to-visual methods
    // These fire inside UpdateEquipmentVisuals every time the visual is evaluated,
    // so no per-spawn force-set is needed. Revert/refresh happen automatically
    // on the next UpdateEquipmentVisuals frame when ShouldApplyArmor() changes.
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(VisEquipment), "SetChestEquipped")]
    [HarmonyPrefix]
    private static void SetChestEquipped_Prefix(VisEquipment __instance, ref int hash)
    {
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        hash = HashChest;
    }

    [HarmonyPatch(typeof(VisEquipment), "SetLegEquipped")]
    [HarmonyPrefix]
    private static void SetLegEquipped_Prefix(VisEquipment __instance, ref int hash)
    {
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        hash = HashLegs;
    }

    [HarmonyPatch(typeof(VisEquipment), "SetHelmetEquipped")]
    [HarmonyPrefix]
    private static void SetHelmetEquipped_Prefix(VisEquipment __instance, ref int hash)
    {
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        hash = HashHelmet;
    }

    // -------------------------------------------------------------------------
    // Phase 3b: Bindpose fix — postfix on SetChestEquipped / SetLegEquipped /
    // SetHelmetEquipped.  After AttachArmor instantiates the armor GameObjects
    // and assigns m_bodyModel.bones, the mesh bindposes are still baked for the
    // player skeleton.  We clone each mesh and substitute the Charred-specific
    // T-pose bindposes so the armor deforms correctly for this creature.
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(VisEquipment), "SetChestEquipped")]
    [HarmonyPostfix]
    private static void SetChestEquipped_Postfix(VisEquipment __instance, bool __result)
    {
        if (!__result) return;
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null || marker.TposeBoneBindposes.Count == 0) return;
        var instances = FChestInstances?.GetValue(__instance) as List<GameObject>;
        FixArmorBindposes(instances, marker);
    }

    [HarmonyPatch(typeof(VisEquipment), "SetLegEquipped")]
    [HarmonyPostfix]
    private static void SetLegEquipped_Postfix(VisEquipment __instance, bool __result)
    {
        if (!__result) return;
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null || marker.TposeBoneBindposes.Count == 0) return;
        var instances = FLegInstances?.GetValue(__instance) as List<GameObject>;
        FixArmorBindposes(instances, marker);
    }

    [HarmonyPatch(typeof(VisEquipment), "SetHelmetEquipped")]
    [HarmonyPostfix]
    private static void SetHelmetEquipped_Postfix(VisEquipment __instance, bool __result)
    {
        if (!__result) return;
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null || marker.TposeBoneBindposes.Count == 0) return;
        var helmetGo = FHelmetInstance?.GetValue(__instance) as GameObject;
        if (helmetGo != null)
            FixArmorBindposes(new List<GameObject> { helmetGo }, marker);
    }

    /// <summary>
    /// KNOWN ISSUE: This approach produces severe mesh spike artifacts in-game.
    /// The T-pose capture at Humanoid.Awake may not actually catch the skeleton in
    /// its bind/rest pose (Animator may have already run), causing the recomputed
    /// bindposes to be wrong. Needs investigation before VanillaMetal mode is usable.
    /// Config default is "Default" until resolved.
    ///
    /// For each SkinnedMeshRenderer in the supplied GameObjects, clones the shared mesh
    /// and replaces its bindposes with the Charred-specific T-pose values stored on the
    /// marker, so the armor deforms correctly against this creature's skeleton.
    /// </summary>
    private static void FixArmorBindposes(List<GameObject>? instances, AshlandsRebornCharredSwapped marker)
    {
        if (instances == null) return;

        var fixed_ = 0;
        foreach (var go in instances)
        {
            if (go == null) continue;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.bones == null || smr.bones.Length == 0 || smr.sharedMesh == null) continue;

                var originalBindposes = smr.sharedMesh.bindposes;
                var newBindposes = new Matrix4x4[smr.bones.Length];
                for (var i = 0; i < smr.bones.Length; i++)
                {
                    if (smr.bones[i] != null &&
                        marker.TposeBoneBindposes.TryGetValue(smr.bones[i].name, out var stored))
                        newBindposes[i] = stored;
                    else
                        newBindposes[i] = i < originalBindposes.Length ? originalBindposes[i] : Matrix4x4.identity;
                }

                // Clone the mesh to avoid modifying the shared player asset
                var clonedMesh = UObject.Instantiate(smr.sharedMesh);
                clonedMesh.bindposes = newBindposes;
                smr.sharedMesh = clonedMesh;
                fixed_++;
            }
        }

        if (fixed_ > 0)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred armor bindpose fix: {fixed_} SMR(s) rebound");
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
                    if (!string.IsNullOrEmpty(marker.OriginalRightItem))
                        vis.SetRightItem(marker.OriginalRightItem);
                    InvalidateArmorHashes(vis);
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

            // --- Sword refresh ---
            marker = humanoid.GetComponent<AshlandsRebornCharredSwapped>();
            var triggerName = marker?.OriginalRightItem ?? "";
            if (string.IsNullOrEmpty(triggerName))
            {
                var cur = FRightItem?.GetValue(vis) as string ?? "";
                if (cur.StartsWith(CharredSwordPrefix, StringComparison.OrdinalIgnoreCase))
                    triggerName = cur;
            }
            if (!string.IsNullOrEmpty(triggerName))
            {
                if (marker != null) marker.OriginalRightItem = "";
                FRightItem?.SetValue(vis, "");
                vis.SetRightItem(triggerName);
            }

            // --- Armor refresh ---
            InvalidateArmorHashes(vis);

            count++;
        }

        Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred refresh: {count} instance(s)");
    }

    // Invalidates the cached visual hash for all three armor slots on a VisEquipment,
    // forcing UpdateEquipmentVisuals to re-evaluate them on the next frame.
    private static void InvalidateArmorHashes(VisEquipment vis)
    {
        const int invalid = -1;
        FCurrentChestHash?.SetValue(vis, invalid);
        FCurrentLegHash?.SetValue(vis, invalid);
        FCurrentHelmetHash?.SetValue(vis, invalid);
    }

    // -------------------------------------------------------------------------
    // Phase 0: One-time discovery dump (fires once per session on first spawn)
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(Humanoid), "Awake")]
    [HarmonyPostfix]
    private static void Humanoid_Awake_Postfix(Humanoid __instance)
    {
        var prefabName = GetPrefabName(__instance.gameObject);

        // Capture T-pose bone bindposes for every Charred_Melee that spawns.
        // At Awake time no animation frames have run, so the skeleton is still in its
        // bind/rest pose.  We pre-compute the world-position-invariant ratio
        //   bone.worldToLocalMatrix * meshRoot.localToWorldMatrix
        // once here and use it later to fix armor SMR bindposes in FixArmorBindposes().
        if (prefabName == CharredMeleePrefab)
        {
            var vis = __instance.GetComponent<VisEquipment>();
            if (vis?.m_bodyModel != null)
            {
                var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                             ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();

                var meshRoot = vis.m_bodyModel.transform.parent.localToWorldMatrix;
                foreach (var bone in vis.m_bodyModel.bones)
                {
                    if (bone != null)
                        marker.TposeBoneBindposes[bone.name] = bone.worldToLocalMatrix * meshRoot;
                }

                if (marker.TposeBoneBindposes.Count > 0)
                    Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred T-pose captured: {marker.TposeBoneBindposes.Count} bones");
            }
        }

        // Discovery dump fires only once per session
        if (_dumpDone) return;
        if (prefabName.IndexOf("charred", StringComparison.OrdinalIgnoreCase) < 0) return;

        _dumpDone = true;
        DumpCharredWarrior(__instance, prefabName);
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
/// Marker on Charred_Melee — stores the original right-hand weapon name for revert,
/// and T-pose bindpose matrices (one per bone name) for armor mesh fixup.
/// </summary>
internal class AshlandsRebornCharredSwapped : MonoBehaviour
{
    public string OriginalRightItem = "";

    /// <summary>True when we've scaled the Krom weapon (avoids re-scaling every frame).</summary>
    public bool KromScaled;

    /// <summary>
    /// Per-bone bindpose computed at Awake T-pose time:
    ///   bone.worldToLocalMatrix * meshRoot.localToWorldMatrix
    /// This ratio is world-position-invariant and is used to replace the
    /// player-baked bindposes in any armor SMR attached to this creature.
    /// </summary>
    public Dictionary<string, Matrix4x4> TposeBoneBindposes = new();
}
