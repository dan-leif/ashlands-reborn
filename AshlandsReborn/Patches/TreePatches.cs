using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Transforms or hides dead Ashlands tree visuals. Transform = living Oak/Beech with Ashlands drops.
/// Hide = invisible, walk-through (eliminated).
/// </summary>
[HarmonyPatch]
internal static class TreePatches
{
    private static readonly HashSet<string> AshlandsTreeNames = new();
    private static readonly Dictionary<string, GameObject> MeadowsPrefabs = new();

    private static Mesh? _bakedOakMesh;
    private static Material[]? _bakedOakMaterials;
    private static Mesh? _bakedBeechMesh;
    private static Material[]? _bakedBeechMaterials;

    private static ZoneSystem? _lastZoneSystem;
    private static bool _initDone;
    private static bool _hasReplacements;
    private static int _transformLogCount;

    private static void EnsureInitialized()
    {
        var zs = ZoneSystem.instance;
        if (zs != _lastZoneSystem)
        {
            _initDone = false;
            _lastZoneSystem = zs;
            AshlandsTreeNames.Clear();
            MeadowsPrefabs.Clear();
            _bakedOakMesh = null;
            _bakedBeechMesh = null;
            _transformLogCount = 0;
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

        BakeReplacementMeshes();

        foreach (var veg in zs.m_vegetation)
        {
            if ((veg.m_biome & Heightmap.Biome.AshLands) == 0) continue;
            if (veg.m_prefab == null) continue;

            var tb = veg.m_prefab.GetComponent<TreeBase>();
            if (tb == null) continue;

            var prefabName = veg.m_prefab.name;
            if (!AshlandsTreeNames.Contains(prefabName))
                AshlandsTreeNames.Add(prefabName);
        }

        _hasReplacements = AshlandsTreeNames.Count > 0;
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Tree replacement: {AshlandsTreeNames.Count} Ashlands tree types, {MeadowsPrefabs.Count} Meadows prefabs cached");
    }

    private static void BakeReplacementMeshes()
    {
        foreach (var name in new[] { "Oak1", "Beech1" })
        {
            if (!MeadowsPrefabs.TryGetValue(name, out var prefab)) continue;
            var tb = prefab.GetComponent<TreeBase>();
            if (tb?.m_trunk == null) continue;

            var temp = Object.Instantiate(tb.m_trunk);
            var instances = new List<CombineInstance>();
            var materials = new List<Material>();
            var root = temp.transform;

            foreach (var mf in temp.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                var toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (var i = 0; i < mf.sharedMesh.subMeshCount; i++)
                {
                    instances.Add(new CombineInstance
                    {
                        mesh = mf.sharedMesh,
                        subMeshIndex = i,
                        transform = toRoot
                    });
                }
                if (mr.sharedMaterials != null)
                    materials.AddRange(mr.sharedMaterials);
            }

            Object.Destroy(temp);

            if (instances.Count == 0) continue;

            var combined = new Mesh();
            combined.CombineMeshes(instances.ToArray(), false, true);

            if (name == "Oak1")
            {
                _bakedOakMesh = combined;
                _bakedOakMaterials = materials.ToArray();
            }
            else
            {
                _bakedBeechMesh = combined;
                _bakedBeechMaterials = materials.ToArray();
            }
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Baked {name}: {instances.Count} parts, {materials.Count} materials");
        }
    }

    private static GameObject? CreateBakedTrunk(string replacementName, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        Mesh? mesh;
        Material[]? mats;
        if (replacementName == "Oak1")
        {
            mesh = _bakedOakMesh;
            mats = _bakedOakMaterials;
        }
        else
        {
            mesh = _bakedBeechMesh;
            mats = _bakedBeechMaterials;
        }
        if (mesh == null || mats == null || mats.Length == 0) return null;

        var go = new GameObject("AshlandsReborn_Trunk");
        go.transform.SetParent(parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;

        return go;
    }

    private static string PickReplacementType()
    {
        var ratio = Mathf.Clamp(Plugin.BeechOakRatio?.Value ?? 100, 0, 100);
        if (Random.value * 100f < ratio)
            return MeadowsPrefabs.ContainsKey("Oak1") ? "Oak1" : "Beech1";
        return MeadowsPrefabs.ContainsKey("Beech1") ? "Beech1" : "Oak1";
    }

    private static void HideTree(TreeBase tree)
    {
        if (tree.m_trunk == null) return;
        foreach (var r in tree.m_trunk.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
        foreach (var c in tree.m_trunk.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
        tree.gameObject.AddComponent<AshlandsRebornHidden>();
    }

    private static void UnhideTree(TreeBase tree)
    {
        if (tree.m_trunk == null) return;
        foreach (var r in tree.m_trunk.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
        foreach (var c in tree.m_trunk.GetComponentsInChildren<Collider>(true))
            c.enabled = true;
        var hidden = tree.gameObject.GetComponent<AshlandsRebornHidden>();
        if (hidden != null) Object.Destroy(hidden);
    }

    internal static void TryTransformTree(TreeBase tree)
    {
        if (!(Plugin.Enabled?.Value ?? false)) return;
        if (!(Plugin.EnableTreeReplacement?.Value ?? true)) return;

        EnsureInitialized();
        if (!_hasReplacements) return;

        var go = tree.gameObject;
        var prefabName = GetPrefabName(go);
        if (!AshlandsTreeNames.Contains(prefabName)) return;

        var density = Mathf.Clamp(Plugin.AshlandsTreeDensity?.Value ?? 25, 0, 100);
        if (Random.value * 100f >= density)
        {
            HideTree(tree);
            return;
        }

        var replacementName = PickReplacementType();
        var originalTrunk = tree.m_trunk;
        var localPos = originalTrunk != null ? originalTrunk.transform.localPosition : Vector3.zero;
        var localRot = originalTrunk != null ? originalTrunk.transform.localRotation : Quaternion.identity;

        if (originalTrunk != null)
        {
            foreach (var r in originalTrunk.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
        }

        GameObject? newTrunk = CreateBakedTrunk(replacementName, tree.transform, localPos, localRot);
        if (newTrunk == null)
        {
            if (MeadowsPrefabs.TryGetValue(replacementName, out var fallbackPrefab))
            {
                var tb = fallbackPrefab.GetComponent<TreeBase>();
                if (tb?.m_trunk != null)
                    newTrunk = Object.Instantiate(tb.m_trunk, tree.transform);
            }
        }
        if (newTrunk == null) return;

        newTrunk.name = "AshlandsReborn_Trunk";
        newTrunk.transform.localPosition = localPos;
        newTrunk.transform.localRotation = localRot;

        tree.m_trunk = newTrunk;

        var marker = go.AddComponent<AshlandsRebornSwapped>();
        marker.OriginalTrunk = originalTrunk;

        if (_transformLogCount++ < 15)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Transformed tree: {prefabName} -> {replacementName}");
    }

    internal static void RefreshTrees()
    {
        if (!(Plugin.EnableTreeReplacement?.Value ?? true)) return;

        EnsureInitialized();
        if (!_hasReplacements) return;

        var swapped = Object.FindObjectsOfType<AshlandsRebornSwapped>();
        foreach (var marker in swapped)
        {
            var tree = marker.GetComponent<TreeBase>();
            if (tree == null || marker.OriginalTrunk == null) continue;

            var toDestroy = tree.m_trunk;
            tree.m_trunk = marker.OriginalTrunk;
            if (marker.OriginalTrunk != null)
            {
                foreach (var r in marker.OriginalTrunk.GetComponentsInChildren<Renderer>(true))
                    r.enabled = true;
                foreach (var c in marker.OriginalTrunk.GetComponentsInChildren<Collider>(true))
                    c.enabled = true;
            }
            if (toDestroy != null) Object.Destroy(toDestroy);
            Object.Destroy(marker);
        }

        var hidden = Object.FindObjectsOfType<AshlandsRebornHidden>();
        foreach (var marker in hidden)
        {
            var tree = marker.GetComponent<TreeBase>();
            if (tree != null) UnhideTree(tree);
        }

        var allTrees = Object.FindObjectsOfType<TreeBase>();
        _transformLogCount = 0;
        foreach (var tree in allTrees)
        {
            if (AshlandsTreeNames.Contains(GetPrefabName(tree.gameObject)))
                TryTransformTree(tree);
        }

        Plugin.Log?.LogInfo("[Ashlands Reborn] Tree refresh complete");
    }

    [HarmonyPatch(typeof(TreeBase), "Awake")]
    [HarmonyPostfix]
    private static void TreeBase_Awake_Postfix(TreeBase __instance)
    {
        if (__instance.GetComponent<AshlandsRebornSwapped>() != null) return;
        if (__instance.GetComponent<AshlandsRebornHidden>() != null) return;

        TryTransformTree(__instance);
    }

    private static string GetPrefabName(GameObject go)
    {
        var name = go.name;
        var idx = name.IndexOf('(');
        return idx >= 0 ? name.Substring(0, idx).Trim() : name;
    }
}

/// <summary>Marker: tree was transformed to living Oak/Beech. Stores OriginalTrunk for revert.</summary>
internal class AshlandsRebornSwapped : MonoBehaviour
{
    public GameObject? OriginalTrunk;
}

/// <summary>Marker: tree was hidden (eliminated).</summary>
internal class AshlandsRebornHidden : MonoBehaviour { }
