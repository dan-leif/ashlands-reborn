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
                    // Runtime bind poses for chest: compute from actual bone transforms.
                    // Rest-pose BPs cause spirals because Charred arm/shoulder rest
                    // orientations differ ~177 deg from runtime orientations.
                    //
                    // History:
                    //   "chest-formed-at-feet" (v0): hipsMeshPos from player prefab BP.
                    //     Shift was ~(0,0,-0.02) — nearly zero — mesh appeared at feet.
                    //   v1 "chest disappeared": world-space shift inserted after smrL2W.
                    //     All bones got the same constant BP, mesh collapsed to a point.
                    //   v2: InverseTransformPoint(hipsWorldPos) → also ~zero because SMR
                    //     origin is already near the Hips bone in world space.
                    //   v3 (current): use charredBPMap["Hips"].inverse to get the Hips
                    //     bone's position in mesh/bind-pose space. This is the offset
                    //     needed to shift vertices so the torso sits at the mesh origin.
                    //
                    // Formula: BP[i] = bone[i].W2L * smrL2W * scaleMat * Translate(-hipsBPMeshPos)

                    var smrL2W = smr.transform.localToWorldMatrix;

                    // --- Diagnostic: compare all candidate shift sources ---
                    Vector3 hipsMeshPosPlayer = Vector3.zero;
                    if (playerBPMap != null && playerBPMap.TryGetValue("Hips", out var hpBPPlayer))
                        hipsMeshPosPlayer = (Vector3)hpBPPlayer.inverse.GetColumn(3);

                    Vector3 hipsBPMeshPos = Vector3.zero; // Hips pos in Charred mesh/bind-pose space
                    if (charredBPMap.TryGetValue("Hips", out var cHipsBP))
                        hipsBPMeshPos = (Vector3)cHipsBP.inverse.GetColumn(3);

                    Transform? hipsBoneTr = null;
                    charBoneMap.TryGetValue("Hips", out hipsBoneTr);
                    Vector3 hipsWorldPos = hipsBoneTr != null ? hipsBoneTr.position : Vector3.zero;
                    Vector3 hipsLocalPos = smr.transform.InverseTransformPoint(hipsWorldPos);

                    // Spine2 bind-pose pos for reference
                    Vector3 spine2BPMeshPos = Vector3.zero;
                    if (charredBPMap.TryGetValue("Spine2", out var cSpine2BP))
                        spine2BPMeshPos = (Vector3)cSpine2BP.inverse.GetColumn(3);

                    Plugin.Log?.LogInfo(
                        $"[Ashlands Reborn] Chest BP diag v3 | SMR='{smr.name}'" +
                        $"\n  hipsMeshPosPlayer  (player prefab BP inverse.pos)   = {hipsMeshPosPlayer}" +
                        $"\n  hipsBPMeshPos      (charred BP inverse.pos for Hips) = {hipsBPMeshPos}" +
                        $"\n  spine2BPMeshPos    (charred BP inverse.pos Spine2)   = {spine2BPMeshPos}" +
                        $"\n  hipsWorldPos       (Hips bone world pos)             = {hipsWorldPos}" +
                        $"\n  hipsLocalPos       (Hips via InverseTransformPoint)  = {hipsLocalPos}" +
                        $"\n  smrL2W.pos         (SMR origin world)                = {(Vector3)smrL2W.GetColumn(3)}" +
                        $"\n  scaleMat           (autoCorr*userScale)              = {totalScale:F4}" +
                        $"\n  SMR localPos={smr.transform.localPosition}  localRot={smr.transform.localEulerAngles}  localScale={smr.transform.localScale}");

                    // Use the Charred skeleton's own Hips bind-pose mesh-space position as the shift.
                    // The Charred body mesh is authored at its own skeleton's scale, so this correctly
                    // encodes how far the Hips bone is from the mesh origin in vertex space.
                    var scaleAndShift = scaleMat * Matrix4x4.Translate(-hipsBPMeshPos);

                    for (int i = 0; i < prefabBoneNames.Length && i < originalBPs.Length; i++)
                        newBPs[i] = newBones[i].worldToLocalMatrix * smrL2W * scaleAndShift;
                    for (int i = prefabBoneNames.Length; i < originalBPs.Length; i++)
                        newBPs[i] = originalBPs[i] * scaleMat;

                    // Log the resulting BP for the Hips bone
                    for (int i = 0; i < prefabBoneNames.Length && i < newBPs.Length; i++)
                    {
                        if (string.Equals(prefabBoneNames[i], "Hips", StringComparison.OrdinalIgnoreCase))
                        {
                            var bp = newBPs[i];
                            Plugin.Log?.LogInfo(
                                $"[Ashlands Reborn] Chest BP diag v3 | Hips newBP[{i}]:" +
                                $"\n  row0={bp.GetRow(0)}" +
                                $"\n  row1={bp.GetRow(1)}" +
                                $"\n  row2={bp.GetRow(2)}" +
                                $"\n  row3={bp.GetRow(3)}" +
                                $"\n  inverse.pos={(Vector3)bp.inverse.GetColumn(3)}");
                            break;
                        }
                    }
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

                if (isChest)
                {
                    // Recalculate mesh bounds so Unity doesn't frustum-cull the chest.
                    // When we replace sharedMesh with a cloned+modified mesh, the bounds
                    // may still reflect the original (pre-remap) vertex positions, causing
                    // the SMR to be culled when the camera isn't aimed at the feet.
                    newMesh.RecalculateBounds();
                    var boundsAfter = newMesh.bounds;
                    Plugin.Log?.LogInfo(
                        $"[Ashlands Reborn] Chest bounds | SMR='{smr.name}'" +
                        $"  newBounds center={boundsAfter.center}  size={boundsAfter.size}");
                }

                smr.sharedMesh = newMesh;

                if (isChest)
                {
                    // Defeat frustum culling while we diagnose position.
                    // This forces the SMR to render regardless of camera angle.
                    smr.updateWhenOffscreen = true;
                }

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
        if (helmetGo == null && head != null && marker != null && !string.IsNullOrEmpty(marker.OriginalHelmetItem))
        {
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Helmet missing on {vis.gameObject.name} but Head bone is ready. Triggering rescue re-equip.");
            _suppressSwordSwap = false; // Ensure we don't block the next call
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

    /// <summary>
    /// The last helmet instance GameObject we applied transforms to.
    /// The coroutine compares the current instance against this to prevent
    /// re-applying scale/rotation when SetHelmetItem is called repeatedly (e.g. during combat).
    /// </summary>
    public GameObject? LastScaledHelmetInstance;
}
