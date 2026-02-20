using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Replaces dead Ashlands tree visuals with living Meadows trees (Beech/Oak)
/// while preserving original drop tables so Ashlands resources still drop.
/// </summary>
[HarmonyPatch]
internal static class TreePatches
{
    private static readonly HashSet<string> AshlandsTreeNames = new();
    private static readonly Dictionary<string, string> ReplacementMap = new();
    private static readonly Dictionary<string, GameObject> MeadowsPrefabs = new();

    private static ZoneSystem? _lastZoneSystem;
    private static bool _initDone;
    private static bool _hasReplacements;
    private static int _swapLogCount;

    private static void EnsureInitialized()
    {
        var zs = ZoneSystem.instance;
        if (zs != _lastZoneSystem)
        {
            _initDone = false;
            _lastZoneSystem = zs;
            AshlandsTreeNames.Clear();
            ReplacementMap.Clear();
            MeadowsPrefabs.Clear();
            _swapLogCount = 0;
        }

        if (_initDone) return;
        _initDone = true;
        _hasReplacements = false;

        var zns = ZNetScene.instance;
        if (zns == null || zs == null) { _initDone = false; return; }

        foreach (var name in new[] { "Beech1", "Beech_small1", "Beech_small2", "Oak1" })
        {
            var prefab = zns.GetPrefab(name);
            if (prefab != null)
                MeadowsPrefabs[name] = prefab;
        }

        if (MeadowsPrefabs.Count == 0)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] Tree replacement: no Meadows tree prefabs found");
            return;
        }

        foreach (var veg in zs.m_vegetation)
        {
            if ((veg.m_biome & Heightmap.Biome.AshLands) == 0) continue;
            if (veg.m_prefab == null) continue;

            var tb = veg.m_prefab.GetComponent<TreeBase>();
            if (tb == null) continue;

            var prefabName = veg.m_prefab.name;
            if (AshlandsTreeNames.Contains(prefabName)) continue;
            AshlandsTreeNames.Add(prefabName);

            string replacement;
            if (tb.m_health >= 80f && MeadowsPrefabs.ContainsKey("Oak1"))
                replacement = "Oak1";
            else if (tb.m_health <= 20f && MeadowsPrefabs.ContainsKey("Beech_small1"))
                replacement = "Beech_small1";
            else
                replacement = MeadowsPrefabs.ContainsKey("Beech1") ? "Beech1" : "Oak1";

            ReplacementMap[prefabName] = replacement;
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Ashlands tree: {prefabName} (HP:{tb.m_health}, tier:{tb.m_minToolTier}) -> {replacement}");
        }

        _hasReplacements = AshlandsTreeNames.Count > 0;
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Tree replacement: {AshlandsTreeNames.Count} Ashlands tree types, {MeadowsPrefabs.Count} Meadows prefabs cached");
    }

    [HarmonyPatch(typeof(TreeBase), "Awake")]
    [HarmonyPostfix]
    private static void TreeBase_Awake_Postfix(TreeBase __instance)
    {
        if (!(Plugin.Enabled?.Value ?? false)) return;
        if (!(Plugin.EnableTreeReplacement?.Value ?? true)) return;

        EnsureInitialized();
        if (!_hasReplacements) return;

        var go = __instance.gameObject;
        if (go.GetComponent<AshlandsRebornSwapped>() != null) return;

        var prefabName = GetPrefabName(go);
        if (!ReplacementMap.TryGetValue(prefabName, out var replacementName)) return;
        if (!MeadowsPrefabs.TryGetValue(replacementName, out var replacementPrefab)) return;

        var replacementTB = replacementPrefab.GetComponent<TreeBase>();
        if (replacementTB?.m_trunk == null) return;

        // Hide original trunk renderers (keep the GameObject for reference)
        if (__instance.m_trunk != null)
        {
            foreach (var r in __instance.m_trunk.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }

        // Clone the replacement tree's trunk visual hierarchy
        var newTrunk = Object.Instantiate(replacementTB.m_trunk, __instance.transform);
        newTrunk.name = "AshlandsReborn_Trunk";

        if (__instance.m_trunk != null)
        {
            newTrunk.transform.localPosition = __instance.m_trunk.transform.localPosition;
            newTrunk.transform.localRotation = __instance.m_trunk.transform.localRotation;
        }
        else
        {
            newTrunk.transform.localPosition = Vector3.zero;
            newTrunk.transform.localRotation = Quaternion.identity;
        }

        // Redirect TreeBase to our replacement trunk so chopping/destruction works correctly.
        // Drops (m_dropWhenDestroyed) remain the original Ashlands resources.
        __instance.m_trunk = newTrunk;

        go.AddComponent<AshlandsRebornSwapped>();

        if (_swapLogCount++ < 15)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Swapped tree visual: {prefabName} -> {replacementName}");
    }

    private static string GetPrefabName(GameObject go)
    {
        var name = go.name;
        var idx = name.IndexOf('(');
        return idx >= 0 ? name.Substring(0, idx).Trim() : name;
    }
}

/// <summary>Marker component to prevent double-swapping a tree.</summary>
internal class AshlandsRebornSwapped : MonoBehaviour { }
