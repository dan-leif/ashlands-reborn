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
        if (_suppressSwordSwap) return; // Share the suppression flag
        if (!ShouldSwap()) return;
        if (!IsCharredMelee(__instance.gameObject)) return;

        if (_swapLogCount < 20)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] SetHelmetItem_Prefix for {__instance.gameObject.name}: '{name}'");
        
        // Only swap if it's the charred helmet
        if (!string.Equals(name, CharredHelmetName, StringComparison.OrdinalIgnoreCase)) return;

        // Ensure the attachment point exists BEFORE the game calls AttachItem in the postfix flow.
        // On initial spawn, SetHelmetItem is called during initialization, often BEFORE 
        // our Awake postfix has had a chance to run.
        EnsureHelmetTransform(__instance);

        // Store original name for revert (only on first swap)
        var marker = __instance.GetComponent<AshlandsRebornCharredSwapped>()
                     ?? __instance.gameObject.AddComponent<AshlandsRebornCharredSwapped>();
        if (string.IsNullOrEmpty(marker.OriginalHelmetItem))
            marker.OriginalHelmetItem = name;

        name = HelmetDrakeName;

        if (_swapLogCount < 10)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Charred_Melee helmet: '{marker.OriginalHelmetItem}' \u2192 '{HelmetDrakeName}'");

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

                EnsureHelmetTransform(vis);
            }
        }

        // Discovery dump fires only once per session
        if (_dumpDone) return;
        if (prefabName.IndexOf("charred", StringComparison.OrdinalIgnoreCase) < 0) return;

        _dumpDone = true;
        DumpCharredWarrior(__instance, prefabName);
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
