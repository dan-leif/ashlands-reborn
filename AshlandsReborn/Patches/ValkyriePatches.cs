using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Replaces Fallen Valkyrie visuals with the intro Valkyrie's appearance.
/// UseIntroVisualsOnly: mesh + materials only, keeps Fallen combat animations.
/// UseIntroVisualsAndAnimations: full Valkyrie visual + Animator (no combat animations).
/// </summary>
[HarmonyPatch]
internal static class ValkyriePatches
{
    private const string FallenValkyriePrefab = "FallenValkyrie";
    private const string IntroValkyriePrefab = "Valkyrie";
    private const string RawVisualName = "AshlandsReborn_ValkyrieVisual";

    private static GameObject? _valkyrieVisualTemplate;
    private static int _swapLogCount;

    private static void EnsureValkyrieTemplate()
    {
        if (_valkyrieVisualTemplate != null) return;

        var zns = ZNetScene.instance;
        if (zns == null) return;

        var valkyriePrefab = zns.GetPrefab(IntroValkyriePrefab);
        if (valkyriePrefab == null)
        {
            Plugin.Log?.LogWarning($"[Ashlands Reborn] Valkyrie swap: prefab '{IntroValkyriePrefab}' not found");
            return;
        }

        var valkyrieRoot = valkyriePrefab.transform;
        if (valkyrieRoot.childCount < 2)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] Valkyrie swap: Valkyrie prefab has no Visual child");
            return;
        }

        var visual = valkyrieRoot.GetChild(1);
        if (visual == null) return;

        _valkyrieVisualTemplate = visual.gameObject;
        Plugin.Log?.LogInfo("[Ashlands Reborn] Valkyrie swap: intro Valkyrie template cached");
    }

    private static string GetPrefabName(GameObject go)
    {
        var name = go.name;
        var idx = name.IndexOf('(');
        return idx >= 0 ? name.Substring(0, idx).Trim() : name;
    }

    private static string GetValkyrieSwapMode()
    {
        var mode = Plugin.EnableValkyrieSwap?.Value ?? "UseIntroVisualsOnly";
        if (mode == "Disable") return "Disable";
        if (mode == "UseIntroVisualsAndAnimations") return "UseIntroVisualsAndAnimations";
        return "UseIntroVisualsOnly";
    }

    internal static void TrySwapValkyrieVisual(Humanoid humanoid)
    {
        if (!(Plugin.Enabled?.Value ?? false)) return;

        var mode = GetValkyrieSwapMode();
        if (mode == "Disable") return;

        var go = humanoid.gameObject;
        if (GetPrefabName(go) != FallenValkyriePrefab) return;

        if (go.GetComponent<AshlandsRebornValkyrieSwapped>() != null) return;

        EnsureValkyrieTemplate();
        if (_valkyrieVisualTemplate == null) return;

        var fallenRoot = go.transform;
        if (fallenRoot.childCount < 2)
        {
            Plugin.Log?.LogWarning("[Ashlands Reborn] Valkyrie swap: Fallen Valkyrie has no Visual child");
            return;
        }

        var fallenVisual = fallenRoot.GetChild(1);
        if (fallenVisual == null) return;

        if (mode == "UseIntroVisualsAndAnimations")
            ApplyRawSwap(go, fallenVisual);
        else
            ApplyAnimatedSwap(go, fallenVisual);

        if (_swapLogCount++ < 5)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Valkyrie swap: {mode}");
    }

    private static void ApplyRawSwap(GameObject go, Transform fallenVisual)
    {
        foreach (var r in fallenVisual.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        var newVisual = Object.Instantiate(_valkyrieVisualTemplate!, go.transform);
        newVisual.name = RawVisualName;
        newVisual.transform.SetSiblingIndex(1);
        newVisual.transform.localPosition = fallenVisual.localPosition;
        newVisual.transform.localRotation = fallenVisual.localRotation * Quaternion.Euler(0f, 180f, 0f);
        newVisual.transform.localScale = fallenVisual.localScale;
        newVisual.SetActive(true);

        var marker = go.AddComponent<AshlandsRebornValkyrieSwapped>();
        marker.OriginalVisual = fallenVisual.gameObject;
        marker.IsRawSwap = true;
    }

    private static void ApplyAnimatedSwap(GameObject go, Transform fallenVisual)
    {
        var valkyrieVisual = _valkyrieVisualTemplate!.transform;
        var boneMap = BuildBoneMap(fallenVisual);

        var fallenSMRs = fallenVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Transform? armatureRoot = null;
        if (fallenSMRs.Length > 0 && fallenSMRs[0].transform.parent != null)
            armatureRoot = fallenSMRs[0].transform.parent;

        var created = new List<GameObject>();
        var anySMRFailed = false;

        foreach (var valkyrieSMR in valkyrieVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = valkyrieSMR.bones;
            var newBones = new Transform[bones.Length];
            var missing = 0;
            for (var i = 0; i < bones.Length; i++)
            {
                var resolved = TryResolveBone(bones[i].name, boneMap);
                if (resolved != null)
                    newBones[i] = resolved;
                else
                    missing++;
            }

            if (missing > 0)
            {
                anySMRFailed = true;
                if (_swapLogCount < 10)
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] Valkyrie animated swap: {missing}/{bones.Length} bones not found for {valkyrieSMR.name} - rigs differ, falling back to UseIntroVisualsAndAnimations");
                break;
            }

            var parent = armatureRoot != null ? armatureRoot : fallenVisual;
            var newGo = new GameObject("AshlandsReborn_ValkyrieMesh");
            newGo.transform.SetParent(parent);
            newGo.transform.localPosition = Vector3.zero;
            newGo.transform.localRotation = Quaternion.identity;
            newGo.transform.localScale = Vector3.one;

            var newSMR = newGo.AddComponent<SkinnedMeshRenderer>();
            newSMR.sharedMesh = valkyrieSMR.sharedMesh;
            newSMR.sharedMaterials = valkyrieSMR.sharedMaterials;
            newSMR.localBounds = valkyrieSMR.localBounds;
            newSMR.bones = newBones;
            if (valkyrieSMR.rootBone != null)
            {
                var rootResolved = TryResolveBone(valkyrieSMR.rootBone.name, boneMap);
                if (rootResolved != null)
                    newSMR.rootBone = rootResolved;
            }

            created.Add(newGo);
        }

        if (anySMRFailed)
        {
            foreach (var obj in created)
                Object.Destroy(obj);
            if (_swapLogCount < 5)
                Plugin.Log?.LogInfo("[Ashlands Reborn] Valkyrie: UseIntroVisualsOnly failed (rig mismatch), using UseIntroVisualsAndAnimations instead");
            ApplyRawSwap(go, fallenVisual);
            return;
        }

        foreach (var r in fallenVisual.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        var marker = go.AddComponent<AshlandsRebornValkyrieSwapped>();
        marker.OriginalVisual = fallenVisual.gameObject;
        marker.IsRawSwap = false;
        marker.CreatedSwapObjects = created;
    }

    private static Transform? TryResolveBone(string valkyrieBoneName, Dictionary<string, Transform> boneMap)
    {
        if (boneMap.TryGetValue(valkyrieBoneName, out var t))
            return t;
        var baseName = valkyrieBoneName;
        var colon = baseName.LastIndexOf(':');
        if (colon >= 0)
            baseName = baseName.Substring(colon + 1);
        var slash = baseName.LastIndexOf('/');
        if (slash >= 0)
            baseName = baseName.Substring(slash + 1);
        if (baseName != valkyrieBoneName && boneMap.TryGetValue(baseName, out t))
            return t;
        foreach (var kv in boneMap)
        {
            if (kv.Key.EndsWith(baseName) || baseName.EndsWith(kv.Key))
                return kv.Value;
        }
        return null;
    }

    private static Dictionary<string, Transform> BuildBoneMap(Transform root)
    {
        var map = new Dictionary<string, Transform>();
        void Collect(Transform t)
        {
            map[t.name] = t;
            for (var i = 0; i < t.childCount; i++)
                Collect(t.GetChild(i));
        }
        Collect(root);
        return map;
    }

    internal static void RevertValkyrieSwap(Humanoid humanoid)
    {
        var marker = humanoid.GetComponent<AshlandsRebornValkyrieSwapped>();
        if (marker == null) return;

        var go = humanoid.gameObject;

        if (marker.IsRawSwap)
        {
            for (var i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i);
                if (child.name == RawVisualName)
                {
                    Object.Destroy(child.gameObject);
                    break;
                }
            }
        }
        else if (marker.CreatedSwapObjects != null)
        {
            foreach (var obj in marker.CreatedSwapObjects)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
        }

        if (marker.OriginalVisual != null)
        {
            foreach (var r in marker.OriginalVisual.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
        }

        Object.Destroy(marker);
    }

    internal static void RefreshValkyries()
    {
        if (!(Plugin.Enabled?.Value ?? false)) return;

        var humanoids = Object.FindObjectsOfType<Humanoid>();
        var count = 0;
        foreach (var h in humanoids)
        {
            if (GetPrefabName(h.gameObject) != FallenValkyriePrefab) continue;
            RevertValkyrieSwap(h);
            count++;
        }

        foreach (var h in humanoids)
        {
            if (GetPrefabName(h.gameObject) != FallenValkyriePrefab) continue;
            TrySwapValkyrieVisual(h);
        }

        Plugin.Log?.LogInfo($"[Ashlands Reborn] Valkyrie refresh: {count} Fallen Valkyries");
    }

    [HarmonyPatch(typeof(Humanoid), "Awake")]
    [HarmonyPostfix]
    private static void Humanoid_Awake_Postfix(Humanoid __instance)
    {
        TrySwapValkyrieVisual(__instance);
    }
}

/// <summary>Marker: Fallen Valkyrie had its visual swapped. Stores revert data.</summary>
internal class AshlandsRebornValkyrieSwapped : MonoBehaviour
{
    public GameObject? OriginalVisual;
    public bool IsRawSwap;
    public List<GameObject>? CreatedSwapObjects;
}
