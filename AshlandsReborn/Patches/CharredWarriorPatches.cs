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

    private static bool _suppressSwordSwap;
    private static int  _swapLogCount;
    private static bool _dumpDone;
    private static bool _meshDumpDone;

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
    private static void SetChestEquipped_Postfix(VisEquipment __instance)
    {
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null || marker.TposeBoneBindposes.Count == 0) return;
        var instances = FChestInstances?.GetValue(__instance) as List<GameObject>;
        FixArmorBindposes(instances, marker);
    }

    [HarmonyPatch(typeof(VisEquipment), "SetLegEquipped")]
    [HarmonyPostfix]
    private static void SetLegEquipped_Postfix(VisEquipment __instance)
    {
        if (!ShouldApplyArmor()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker == null || marker.TposeBoneBindposes.Count == 0) return;
        var instances = FLegInstances?.GetValue(__instance) as List<GameObject>;
        FixArmorBindposes(instances, marker);
    }

    [HarmonyPatch(typeof(VisEquipment), "SetHelmetEquipped")]
    [HarmonyPostfix]
    private static void SetHelmetEquipped_Postfix(VisEquipment __instance)
    {
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
                    // Restore original sword (suppressed so prefix won't re-swap)
                    if (!string.IsNullOrEmpty(marker.OriginalRightItem))
                        vis.SetRightItem(marker.OriginalRightItem);

                    // Invalidate armor current-hash fields so UpdateEquipmentVisuals
                    // re-evaluates immediately and shows the original items next frame
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

            // --- Sword refresh ---
            // Prefer the stored original; fall back to the current m_rightItem if it's a charred sword.
            var marker = humanoid.GetComponent<AshlandsRebornCharredSwapped>();
            var triggerName = marker?.OriginalRightItem ?? "";
            if (string.IsNullOrEmpty(triggerName))
            {
                var cur = FRightItem?.GetValue(vis) as string ?? "";
                if (cur.StartsWith(CharredSwordPrefix, StringComparison.OrdinalIgnoreCase))
                    triggerName = cur;
            }
            if (!string.IsNullOrEmpty(triggerName))
            {
                // Remove old marker, clear guard, re-trigger — prefix swaps to Krom
                if (marker != null) UObject.Destroy(marker);
                FRightItem?.SetValue(vis, "");
                vis.SetRightItem(triggerName);
            }

            // --- Armor refresh ---
            // Invalidate current-hash fields so UpdateEquipmentVisuals re-evaluates
            // immediately and the armor prefix applies on the next frame
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

        // Mesh comparison dump: fires once when a Charred spawns and the local player exists
        if (!_meshDumpDone && prefabName == CharredMeleePrefab && Player.m_localPlayer != null)
        {
            _meshDumpDone = true;
            DumpMeshComparison(__instance, Player.m_localPlayer);
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

    // -------------------------------------------------------------------------
    // Mesh comparison dump: player vs Charred body mesh, bones, height ratios
    // -------------------------------------------------------------------------

    private static void DumpMeshComparison(Humanoid charred, Player player)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MESH COMPARISON DUMP (Charred vs Player) ===");

        var charredVis = charred.GetComponent<VisEquipment>();
        var playerVis = player.GetComponent<VisEquipment>();

        if (charredVis?.m_bodyModel == null)
        {
            sb.AppendLine("ERROR: Charred VisEquipment or m_bodyModel is null");
            Plugin.Log?.LogInfo(sb.ToString());
            return;
        }
        if (playerVis?.m_bodyModel == null)
        {
            sb.AppendLine("ERROR: Player VisEquipment or m_bodyModel is null");
            Plugin.Log?.LogInfo(sb.ToString());
            return;
        }

        var cBody = charredVis.m_bodyModel;
        var pBody = playerVis.m_bodyModel;

        // --- Mesh info ---
        sb.AppendLine();
        sb.AppendLine("--- Mesh Info ---");
        DumpMeshInfo(sb, "Player", pBody);
        DumpMeshInfo(sb, "Charred", cBody);

        // --- Player models array ---
        sb.AppendLine();
        sb.AppendLine($"Player m_models.Length: {playerVis.m_models?.Length ?? 0}");
        if (playerVis.m_models != null)
        {
            for (var i = 0; i < playerVis.m_models.Length; i++)
            {
                var m = playerVis.m_models[i];
                sb.AppendLine($"  [{i}] mesh={m.m_mesh?.name ?? "null"} verts={m.m_mesh?.vertexCount ?? 0} " +
                              $"mat={m.m_baseMaterial?.name ?? "null"} shader={m.m_baseMaterial?.shader?.name ?? "null"}");
            }
        }

        // --- Bone counts and name match ---
        sb.AppendLine();
        sb.AppendLine("--- Bone Comparison ---");
        sb.AppendLine($"Player bones: {pBody.bones.Length}   Charred bones: {cBody.bones.Length}");
        sb.AppendLine($"Player rootBone: {pBody.rootBone?.name ?? "null"}   Charred rootBone: {cBody.rootBone?.name ?? "null"}");

        var maxBones = Math.Max(pBody.bones.Length, cBody.bones.Length);
        var mismatches = 0;
        for (var i = 0; i < maxBones; i++)
        {
            var pName = i < pBody.bones.Length ? pBody.bones[i]?.name ?? "NULL" : "(missing)";
            var cName = i < cBody.bones.Length ? cBody.bones[i]?.name ?? "NULL" : "(missing)";
            if (pName != cName)
            {
                sb.AppendLine($"  MISMATCH [{i}]: player='{pName}'  charred='{cName}'");
                mismatches++;
            }
        }
        sb.AppendLine(mismatches == 0 ? "  All bone names match!" : $"  {mismatches} mismatch(es)");

        // --- Key bone world positions ---
        sb.AppendLine();
        sb.AppendLine("--- Key Bone World Positions ---");
        var keyBones = new[] { "Root", "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head",
                               "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
                               "RightShoulder", "RightArm", "RightForeArm", "RightHand",
                               "LeftUpLeg", "LeftLeg", "LeftFoot",
                               "RightUpLeg", "RightLeg", "RightFoot" };

        var pBoneMap = new Dictionary<string, Transform>();
        var cBoneMap = new Dictionary<string, Transform>();
        foreach (var b in pBody.bones) if (b != null) pBoneMap[b.name] = b;
        foreach (var b in cBody.bones) if (b != null) cBoneMap[b.name] = b;

        sb.AppendLine($"  {"Bone",-20} {"Player pos",-36} {"Charred pos",-36} {"Dist ratio",10}");
        foreach (var boneName in keyBones)
        {
            var hasP = pBoneMap.TryGetValue(boneName, out var pBone);
            var hasC = cBoneMap.TryGetValue(boneName, out var cBone);
            if (!hasP || !hasC)
            {
                sb.AppendLine($"  {boneName,-20} {(hasP ? pBone!.position.ToString("F4") : "MISSING"),-36} " +
                              $"{(hasC ? cBone!.position.ToString("F4") : "MISSING"),-36}");
                continue;
            }
            sb.AppendLine($"  {boneName,-20} {pBone!.position.ToString("F4"),-36} {cBone!.position.ToString("F4"),-36}");
        }

        // --- Height and proportion ratios ---
        sb.AppendLine();
        sb.AppendLine("--- Proportion Ratios (Charred / Player) ---");

        DumpSegmentRatio(sb, "Height (Head-Foot)", pBoneMap, cBoneMap, "Head", "LeftFoot");
        DumpSegmentRatio(sb, "Torso (Hips-Head)", pBoneMap, cBoneMap, "Hips", "Head");
        DumpSegmentRatio(sb, "Leg (Hips-Foot)", pBoneMap, cBoneMap, "Hips", "LeftFoot");
        DumpSegmentRatio(sb, "Upper leg (Hips-Knee)", pBoneMap, cBoneMap, "LeftUpLeg", "LeftLeg");
        DumpSegmentRatio(sb, "Lower leg (Knee-Foot)", pBoneMap, cBoneMap, "LeftLeg", "LeftFoot");
        DumpSegmentRatio(sb, "Arm (Shoulder-Hand)", pBoneMap, cBoneMap, "LeftShoulder", "LeftHand");
        DumpSegmentRatio(sb, "Upper arm (Shoulder-Elbow)", pBoneMap, cBoneMap, "LeftArm", "LeftForeArm");
        DumpSegmentRatio(sb, "Forearm (Elbow-Hand)", pBoneMap, cBoneMap, "LeftForeArm", "LeftHand");
        DumpSegmentRatio(sb, "Shoulder width (L-R)", pBoneMap, cBoneMap, "LeftShoulder", "RightShoulder");
        DumpSegmentRatio(sb, "Hip width (L-R)", pBoneMap, cBoneMap, "LeftUpLeg", "RightUpLeg");

        // --- Y-axis height comparison (most reliable for uniform scale factor) ---
        sb.AppendLine();
        sb.AppendLine("--- Y-Axis Heights (local to Root) ---");
        if (pBoneMap.TryGetValue("Root", out var pRoot) && cBoneMap.TryGetValue("Root", out var cRoot))
        {
            foreach (var boneName in new[] { "Hips", "Spine2", "Head", "LeftFoot", "LeftHand" })
            {
                if (pBoneMap.TryGetValue(boneName, out var pb) && cBoneMap.TryGetValue(boneName, out var cb))
                {
                    var pY = pb.position.y - pRoot.position.y;
                    var cY = cb.position.y - cRoot.position.y;
                    var ratio = Math.Abs(pY) > 0.001f ? cY / pY : 0f;
                    sb.AppendLine($"  {boneName,-20} player={pY,8:F4}  charred={cY,8:F4}  ratio={ratio,6:F3}");
                }
            }
        }

        // --- Bindpose sample (first 5 bones) ---
        sb.AppendLine();
        sb.AppendLine("--- Bindpose Sample (first 5 bones from player body mesh) ---");
        var pBindposes = pBody.sharedMesh?.bindposes;
        if (pBindposes != null)
        {
            for (var i = 0; i < Math.Min(5, pBindposes.Length); i++)
            {
                var bp = pBindposes[i];
                var boneName = i < pBody.bones.Length ? pBody.bones[i]?.name ?? "?" : "?";
                sb.AppendLine($"  [{i}] {boneName}: row0={bp.GetRow(0):F4}  row1={bp.GetRow(1):F4}  row2={bp.GetRow(2):F4}  row3={bp.GetRow(3):F4}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("--- Bindpose Sample (first 5 bones from charred body mesh) ---");
        var cBindposes = cBody.sharedMesh?.bindposes;
        if (cBindposes != null)
        {
            for (var i = 0; i < Math.Min(5, cBindposes.Length); i++)
            {
                var bp = cBindposes[i];
                var boneName = i < cBody.bones.Length ? cBody.bones[i]?.name ?? "?" : "?";
                sb.AppendLine($"  [{i}] {boneName}: row0={bp.GetRow(0):F4}  row1={bp.GetRow(1):F4}  row2={bp.GetRow(2):F4}  row3={bp.GetRow(3):F4}");
            }
        }

        // --- Charred extra SMRs ---
        sb.AppendLine();
        sb.AppendLine("--- Charred Extra SMRs (Eyes, Sinew, Skull) ---");
        foreach (var smr in charred.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == cBody) continue;
            sb.AppendLine($"  '{smr.name}' verts={smr.sharedMesh?.vertexCount ?? 0} active={smr.gameObject.activeSelf} " +
                          $"mat={smr.sharedMaterial?.name ?? "null"} shader={smr.sharedMaterial?.shader?.name ?? "null"}");
        }

        // --- Charred non-SMR renderers (particle effects for eye glow, chest glow, etc.) ---
        sb.AppendLine();
        sb.AppendLine("--- Charred Non-SMR Renderers ---");
        foreach (var r in charred.GetComponentsInChildren<Renderer>(true))
        {
            if (r is SkinnedMeshRenderer) continue;
            sb.AppendLine($"  {r.GetType().Name}: '{r.name}' active={r.gameObject.activeSelf} enabled={r.enabled}");
        }

        sb.AppendLine();
        sb.AppendLine("=== END MESH COMPARISON DUMP ===");
        Plugin.Log?.LogInfo(sb.ToString());
    }

    private static void DumpMeshInfo(StringBuilder sb, string label, SkinnedMeshRenderer body)
    {
        var mesh = body.sharedMesh;
        sb.AppendLine($"  {label}:");
        sb.AppendLine($"    mesh name='{mesh?.name ?? "null"}'  verts={mesh?.vertexCount ?? 0}  submeshes={mesh?.subMeshCount ?? 0}");
        sb.AppendLine($"    bounds center={mesh?.bounds.center}  size={mesh?.bounds.size}");
        sb.AppendLine($"    material='{body.sharedMaterial?.name ?? "null"}'  shader='{body.sharedMaterial?.shader?.name ?? "null"}'");
        if (body.sharedMaterials != null && body.sharedMaterials.Length > 1)
        {
            for (var i = 1; i < body.sharedMaterials.Length; i++)
                sb.AppendLine($"    material[{i}]='{body.sharedMaterials[i]?.name ?? "null"}'  shader='{body.sharedMaterials[i]?.shader?.name ?? "null"}'");
        }
    }

    private static void DumpSegmentRatio(StringBuilder sb, string label,
        Dictionary<string, Transform> pMap, Dictionary<string, Transform> cMap,
        string boneA, string boneB)
    {
        if (!pMap.TryGetValue(boneA, out var pa) || !pMap.TryGetValue(boneB, out var pb)) return;
        if (!cMap.TryGetValue(boneA, out var ca) || !cMap.TryGetValue(boneB, out var cb)) return;

        var pDist = Vector3.Distance(pa.position, pb.position);
        var cDist = Vector3.Distance(ca.position, cb.position);
        var ratio = pDist > 0.001f ? cDist / pDist : 0f;

        sb.AppendLine($"  {label,-30} player={pDist,8:F4}  charred={cDist,8:F4}  ratio={ratio,6:F3}");
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

    /// <summary>
    /// Per-bone bindpose computed at Awake T-pose time:
    ///   bone.worldToLocalMatrix * meshRoot.localToWorldMatrix
    /// This ratio is world-position-invariant and is used to replace the
    /// player-baked bindposes in any armor SMR attached to this creature.
    /// </summary>
    public Dictionary<string, Matrix4x4> TposeBoneBindposes = new();
}
