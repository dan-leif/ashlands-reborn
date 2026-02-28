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

    private static bool _suppressSwordSwap;
    private static int  _swapLogCount;
    private static bool _dumpDone;

    // Sword swap active when master switch + EnableCharredWarriorSwap are on
    private static bool ShouldSwap() =>
        (Plugin.MasterSwitch?.Value ?? false) &&
        (Plugin.EnableCharredWarriorSwap?.Value ?? false);

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
    // Phase 1.5: Helmet swap — prefix on VisEquipment.SetHelmetItem
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetHelmetItem))]
    [HarmonyPrefix]
    private static void SetHelmetItem_Prefix(VisEquipment __instance, ref string name)
    {
        if (_suppressSwordSwap) return; // Share the suppression flag
        if (!ShouldSwap()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;
        
        // Only swap if it's the charred helmet
        if (!string.Equals(name, CharredHelmetName, StringComparison.OrdinalIgnoreCase)) return;

        // Store original name for revert (only on first swap)
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (string.IsNullOrEmpty(marker.OriginalHelmetItem))
            marker.OriginalHelmetItem = name;

        name = HelmetDrakeName;

        if (_swapLogCount < 10)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred_Melee helmet: '{marker.OriginalHelmetItem}' → '{HelmetDrakeName}'");

        marker.HelmetScaled = false;

        // Diagnostic: check m_helmet transform
        var helmetTransform = FHelmetTransform?.GetValue(__instance) as Transform;
        Plugin.Log?.LogInfo($"[Ashlands Reborn] DIAG Helmet prefix: m_helmet transform={(helmetTransform?.name ?? "NULL")}, helmetTransformIsNull={helmetTransform == null}");
    }

    [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetHelmetItem))]
    [HarmonyPostfix]
    private static void SetHelmetItem_Postfix(VisEquipment __instance)
    {
        if (!ShouldSwap()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;

        var curItem = FHelmetItem?.GetValue(__instance) as string ?? "";
        if (!string.Equals(curItem, HelmetDrakeName, StringComparison.OrdinalIgnoreCase)) return;

        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>();
        if (marker != null && marker.HelmetScaled) return;

        // Diagnostic: log helmet state after SetHelmetItem
        var helmetTransform = FHelmetTransform?.GetValue(__instance) as Transform;
        var helmetInstance = FHelmetItemInstance?.GetValue(__instance) as GameObject;
        Plugin.Log?.LogInfo($"[Ashlands Reborn] DIAG Helmet postfix: curItem='{curItem}', m_helmet={(helmetTransform?.name ?? "NULL")}, m_helmetItemInstance={(helmetInstance?.name ?? "NULL")}");

        __instance.StartCoroutine(ScaleHelmetAfterAttach(__instance, marker));
    }

    private static System.Collections.IEnumerator ScaleHelmetAfterAttach(VisEquipment vis, AshlandsRebornCharredSwapped? marker)
    {
        yield return null;
        yield return null;

        if (vis == null || !ShouldSwap()) yield break;

        var helmetGo = FHelmetItemInstance?.GetValue(vis) as GameObject;
        if (helmetGo == null)
        {
            // Valheim usually attaches helmets to the Head bone
            var head = FindInChildren(vis.transform, "Head");
            if (head != null && head.childCount > 0)
            {
                 // Try to find the instantiated helmet under the head
                 for (var i = 0; i < head.childCount; i++)
                 {
                     var child = head.GetChild(i).gameObject;
                     if (GetPrefabName(child).Equals(HelmetDrakeName, StringComparison.OrdinalIgnoreCase))
                     {
                         helmetGo = child;
                         break;
                     }
                 }
            }
        }

        if (helmetGo != null)
        {
            var scale = Plugin.CharredWarriorHelmetScale?.Value ?? 1.05f;
            var yOffset = Plugin.CharredWarriorHelmetYOffset?.Value ?? 0.15f;

            var posBefore = helmetGo.transform.position;

            helmetGo.transform.localScale *= scale;
            // Rotate around Y axis — configurable via CharredWarriorHelmetYaw
            var yaw = Plugin.CharredWarriorHelmetYaw?.Value ?? -90f;
            helmetGo.transform.localRotation *= Quaternion.Euler(0f, yaw, 0f);
            // Lift in world space (avoids bone-local scale distortion)
            helmetGo.transform.Translate(0f, yOffset, 0f, Space.World);
            // Move forward/back relative to the warrior's facing direction
            var zOffset = Plugin.CharredWarriorHelmetZOffset?.Value ?? 0.05f;
            helmetGo.transform.Translate(vis.transform.forward * zOffset, Space.World);

            Plugin.Log?.LogInfo($"[Ashlands Reborn] DIAG Helmet pos: before={posBefore}, after={helmetGo.transform.position}, yOffset={yOffset}");

            if (marker != null) marker.HelmetScaled = true;
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Drake Helmet adjusted: scale={scale}, yOffset={yOffset}");
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
                    if (!string.IsNullOrEmpty(marker.OriginalRightItem))
                        vis.SetRightItem(marker.OriginalRightItem);
                    
                    if (!string.IsNullOrEmpty(marker.OriginalHelmetItem))
                        vis.SetHelmetItem(marker.OriginalHelmetItem);
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
            var triggerHelmet = marker?.OriginalHelmetItem ?? "";
            if (string.IsNullOrEmpty(triggerHelmet))
            {
                var cur = FHelmetItem?.GetValue(vis) as string ?? "";
                if (string.Equals(cur, CharredHelmetName, StringComparison.OrdinalIgnoreCase))
                    triggerHelmet = cur;
            }
            if (!string.IsNullOrEmpty(triggerHelmet))
            {
                if (marker != null) marker.OriginalHelmetItem = "";
                FHelmetItem?.SetValue(vis, "");
                vis.SetHelmetItem(triggerHelmet);
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

                // Charred_Melee has no m_helmet transform set — rigid-attach helmets
                // like HelmetDrake need it so AttachItem can parent them correctly.
                // Without this, SetParent(null) sends the helmet to the world origin.
                if (vis.m_helmet == null)
                {
                    var head = FindInChildren(vis.transform, "Head");
                    if (head != null)
                    {
                        vis.m_helmet = head;
                        Plugin.Log?.LogInfo("[Ashlands Reborn] Assigned Head bone as m_helmet for Charred_Melee");
                    }
                }
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
/// Marker on Charred_Melee — stores the original right-hand weapon name for revert.
/// </summary>
internal class AshlandsRebornCharredSwapped : MonoBehaviour
{
    public string OriginalRightItem = "";
    public string OriginalHelmetItem = "";

    /// <summary>True when we've scaled the Krom weapon (avoids re-scaling every frame).</summary>
    public bool KromScaled;

    /// <summary>True when we've scaled the helmet (avoids re-scaling every frame).</summary>
    public bool HelmetScaled;
}
