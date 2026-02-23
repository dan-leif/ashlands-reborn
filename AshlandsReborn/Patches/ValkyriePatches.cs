using System.Collections.Generic;
using System.Text;
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
    private static bool _boneDumpDone;

    /// <summary>
    /// Maps intro Valkyrie bone names to Fallen Valkyrie bone names.
    /// Derived from bone dump: intro has 62 bones, fallen has 68.
    /// 41 match exactly; 21 need mapping (renames, wing detail → spine0, shoulder feathers → shoulder).
    /// </summary>
    private static readonly Dictionary<string, string> IntroToFallenBoneMap = new()
    {
        { "pelvis", "hip" },
        { "tail", "tail0" },
        { "tail.001", "tail1" },
        { "tail.002", "tail2" },
        { "l_shouldergeathers", "l_shoulder" },
        { "r_shouldergeathers", "r_shoulder" },
        { "spine0.001", "spine0" },
        { "spine0.002", "spine0" },
        { "spine0.003", "spine0" },
        { "spine0.004", "spine0" },
        { "spine0.005", "spine0" },
        { "spine0.006", "spine0" },
        { "spine0.007", "spine0" },
        { "spine0.008", "spine0" },
        { "spine0.009", "spine0" },
        { "spine0.010", "spine0" },
        { "spine0.011", "spine0" },
        { "spine0.012", "spine0" },
        { "spine0.013", "spine0" },
        { "spine0.014", "spine0" },
        { "spine0.015", "spine0" },
    };

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
        var mode = Plugin.EnableValkyrieSwap?.Value ?? "Enabled";
        if (mode == "Disabled") return "Disabled";
        if (mode == "UseIntroVisualsAndAnimations") return "UseIntroVisualsAndAnimations";
        return "Enabled";
    }

    internal static void TrySwapValkyrieVisual(Humanoid humanoid)
    {
        if (!(Plugin.MasterSwitch?.Value ?? false)) return;

        var mode = GetValkyrieSwapMode();
        if (mode == "Disabled") return;

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

        if (!_boneDumpDone)
        {
            _boneDumpDone = true;
            DumpBoneNames(fallenVisual, _valkyrieVisualTemplate!.transform);
        }

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
        Transform? smrParent = null;
        int referenceLayer = 0;
        if (fallenSMRs.Length > 0)
        {
            smrParent = fallenSMRs[0].transform.parent;
            referenceLayer = fallenSMRs[0].gameObject.layer;
        }

        // Build a map from fallen bone name to its static bind pose matrix.
        // These come from the mesh asset and represent the true rest pose,
        // unlike live bone transforms which reflect whatever animation frame is playing.
        var fallenBPMap = new Dictionary<string, Matrix4x4>();
        if (fallenSMRs.Length > 0)
        {
            var fbones = fallenSMRs[0].bones;
            var fbps = fallenSMRs[0].sharedMesh.bindposes;
            for (int j = 0; j < fbones.Length && j < fbps.Length; j++)
                if (fbones[j] != null)
                    fallenBPMap[fbones[j].name] = fbps[j];
        }

        var originalRenderers = fallenVisual.GetComponentsInChildren<Renderer>(true);

        var created = new List<GameObject>();
        var anySMRFailed = false;

        foreach (var valkyrieSMR in valkyrieVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = valkyrieSMR.bones;
            var newBones = new Transform[bones.Length];
            var missing = 0;
            var missingNames = new List<string>();
            for (var i = 0; i < bones.Length; i++)
            {
                var resolved = TryResolveBone(bones[i].name, boneMap);
                if (resolved != null)
                    newBones[i] = resolved;
                else
                {
                    missing++;
                    if (missingNames.Count < 5)
                        missingNames.Add(bones[i].name);
                }
            }

            if (missing > 0)
            {
                anySMRFailed = true;
                if (_swapLogCount < 10)
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] Valkyrie animated swap: {missing}/{bones.Length} bones not found for {valkyrieSMR.name} (e.g. {string.Join(", ", missingNames)}) - falling back to UseIntroVisualsAndAnimations");
                break;
            }

            // Clone the intro mesh and replace bind poses with the Fallen rig's
            // static bind poses. This ensures the mesh deforms correctly with the
            // Fallen Valkyrie's Animator regardless of the current animation frame.
            var origMesh = valkyrieSMR.sharedMesh;
            var newMesh = Object.Instantiate(origMesh);
            var origBindPoses = origMesh.bindposes;
            var newBindPoses = new Matrix4x4[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                if (newBones[i] != null && fallenBPMap.TryGetValue(newBones[i].name, out var bp))
                    newBindPoses[i] = bp;
                else
                    newBindPoses[i] = origBindPoses[i];
            }
            newMesh.bindposes = newBindPoses;

            var parent = smrParent != null ? smrParent : fallenVisual;
            var newGo = new GameObject("AshlandsReborn_ValkyrieMesh");
            newGo.layer = referenceLayer;
            newGo.transform.SetParent(parent);
            newGo.transform.localPosition = Vector3.zero;
            newGo.transform.localRotation = Quaternion.identity;
            newGo.transform.localScale = Vector3.one;

            var newSMR = newGo.AddComponent<SkinnedMeshRenderer>();
            newSMR.sharedMesh = newMesh;
            newSMR.sharedMaterials = valkyrieSMR.sharedMaterials;
            newSMR.localBounds = valkyrieSMR.localBounds;
            newSMR.updateWhenOffscreen = true;
            newSMR.bones = newBones;

            if (valkyrieSMR.rootBone != null)
            {
                var rootResolved = TryResolveBone(valkyrieSMR.rootBone.name, boneMap);
                if (rootResolved != null)
                    newSMR.rootBone = rootResolved;
            }

            if (fallenSMRs.Length > 0)
            {
                newSMR.shadowCastingMode = fallenSMRs[0].shadowCastingMode;
                newSMR.receiveShadows = fallenSMRs[0].receiveShadows;
                newSMR.lightProbeUsage = fallenSMRs[0].lightProbeUsage;
                if (fallenSMRs[0].probeAnchor != null)
                    newSMR.probeAnchor = fallenSMRs[0].probeAnchor;
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

        foreach (var r in originalRenderers)
            r.enabled = false;

        // Force the Animator to keep updating bone transforms even though the
        // original renderers are disabled. Without this, the Animator culls bone
        // updates when it sees no visible renderers, freezing the mesh in T-pose.
        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (_swapLogCount < 3)
                Plugin.Log?.LogInfo($"[Ashlands Reborn] Valkyrie animator culling set to AlwaysAnimate (was {animator.cullingMode})");
        }

        if (_swapLogCount < 3)
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Valkyrie animated swap: created {created.Count} SMRs, layer={referenceLayer}, parent={smrParent?.name ?? "null"}");

        var marker = go.AddComponent<AshlandsRebornValkyrieSwapped>();
        marker.OriginalVisual = fallenVisual.gameObject;
        marker.IsRawSwap = false;
        marker.CreatedSwapObjects = created;
    }

    private static Transform? TryResolveBone(string valkyrieBoneName, Dictionary<string, Transform> boneMap)
    {
        if (boneMap.TryGetValue(valkyrieBoneName, out var t))
            return t;
        if (IntroToFallenBoneMap.TryGetValue(valkyrieBoneName, out var fallenName) && boneMap.TryGetValue(fallenName, out t))
            return t;
        return null;
    }

    private static void DumpBoneNames(Transform fallenVisual, Transform valkyrieVisual)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== VALKYRIE BONE DUMP ===");

        sb.AppendLine();
        sb.AppendLine("--- Fallen Valkyrie Hierarchy ---");
        DumpHierarchy(fallenVisual, sb, 0);

        sb.AppendLine();
        sb.AppendLine("--- Fallen Valkyrie SMR Bones ---");
        foreach (var smr in fallenVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            sb.AppendLine($"  SMR: {smr.name} ({smr.bones.Length} bones, rootBone: {smr.rootBone?.name ?? "null"})");
            for (var i = 0; i < smr.bones.Length; i++)
                sb.AppendLine($"    [{i}] {smr.bones[i]?.name ?? "NULL"}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Intro Valkyrie Hierarchy ---");
        DumpHierarchy(valkyrieVisual, sb, 0);

        sb.AppendLine();
        sb.AppendLine("--- Intro Valkyrie SMR Bones ---");
        foreach (var smr in valkyrieVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            sb.AppendLine($"  SMR: {smr.name} ({smr.bones.Length} bones, rootBone: {smr.rootBone?.name ?? "null"})");
            for (var i = 0; i < smr.bones.Length; i++)
                sb.AppendLine($"    [{i}] {smr.bones[i]?.name ?? "NULL"}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Fallen Valkyrie Materials ---");
        foreach (var smr in fallenVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            sb.AppendLine($"  SMR: {smr.name}");
            foreach (var mat in smr.sharedMaterials)
                sb.AppendLine($"    Material: {mat?.name ?? "null"}, Shader: {mat?.shader?.name ?? "null"}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Intro Valkyrie Materials ---");
        foreach (var smr in valkyrieVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            sb.AppendLine($"  SMR: {smr.name}");
            foreach (var mat in smr.sharedMaterials)
                sb.AppendLine($"    Material: {mat?.name ?? "null"}, Shader: {mat?.shader?.name ?? "null"}");
        }

        sb.AppendLine("=== END BONE DUMP ===");
        Plugin.Log?.LogInfo(sb.ToString());
    }

    private static void DumpHierarchy(Transform t, StringBuilder sb, int depth)
    {
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name}");
        for (var i = 0; i < t.childCount; i++)
            DumpHierarchy(t.GetChild(i), sb, depth + 1);
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

    internal static void RevertAllValkyries()
    {
        var humanoids = Object.FindObjectsOfType<Humanoid>();
        var count = 0;
        foreach (var h in humanoids)
        {
            if (GetPrefabName(h.gameObject) != FallenValkyriePrefab) continue;
            RevertValkyrieSwap(h);
            count++;
        }
        Plugin.Log?.LogInfo($"[Ashlands Reborn] Valkyrie revert: {count} Fallen Valkyries");
    }

    internal static void RefreshValkyries()
    {
        if (!(Plugin.MasterSwitch?.Value ?? false)) return;

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
