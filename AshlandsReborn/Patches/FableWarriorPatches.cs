using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// <summary>
/// Fable Warrior: replaces the legacy Charred Warrior body/armor hodgepodge
/// (<see cref="CharredWarriorPatches"/>, gated off via ShouldSwap when
/// Plugin.EnableFableWarrior is true) with a scaled Player-rig "puppet" driven by the
/// Charred's own animation.
///
/// The core idea that makes this work where 10+ prior mesh-on-Charred-skeleton attempts
/// failed: the player body mesh NEVER leaves the skeleton it was authored for. We instantiate
/// a stripped, visual-only Player prefab as a child of the warrior, hide the Charred's own
/// renderers, scale the puppet to Charred size, and every LateUpdate retarget the Charred
/// bones' rotations onto the matching puppet bones (both rigs use Mixamo names) via a
/// deviation-from-rest transfer: measure each Charred bone's rotation delta from its own rest
/// pose and apply that delta to the puppet bone relative to ITS rest pose. This preserves the
/// human rest posture (a direct orientation copy was tried and distorts the whole body,
/// because the rigs' rest orientations/roll only coincide approximately outside the arms).
/// The arm chain is the exception: its rest poses differ by a large constant (~48/59.5 deg),
/// so those bones get a computed rest-pose alignment baked into their offsets (EnsureOffsets).
///
/// M2 scope: puppet BODY only (naked player body), no equipment. M3 adds the equipment clone,
/// Charred attach suppression, and the Krom sword.
/// </summary>
[HarmonyPatch]
internal static class FableWarriorPatches
{
    private const string CharredMeleePrefab = "Charred_Melee";

    // The Charred's capsule collider spans ~ground-to-neck and under-reads the visible body
    // height by ~30% (it misses the head). Calibrated so a standard warrior's puppet matches
    // the Charred's visible silhouette (~2.97m) rather than the capsule height (~2.29m).
    private const float CapsuleToBodyHeight = 1.3f;

    // Per-bone rest-pose offsets, computed once from the two prefabs, keyed by Mixamo bone name.
    // c0Inv = inverse of the Charred bone's rest rotation (relative to the Charred Visual node).
    // p0Eff = the Player bone's rest rotation (relative to the Player Visual node); for the arm
    //         chain it is pre-multiplied by a constant swing that aligns the player's rest arm
    //         segment direction with the Charred's (see AlignedBoneChild in EnsureOffsets).
    private static Dictionary<string, (Quaternion c0Inv, Quaternion p0Eff)>? _offsets;
    private static bool _offsetsFailed;

    // Arm-chain bones whose rest segment direction differs between the two rigs by a large
    // constant (measured live: upper arm ~48.0 deg, forearm ~59.5 deg — the inherent
    // human-vs-Charred rest arm pose difference). Value = the single child bone that defines
    // each bone's segment direction. All other bones' rests already agree closely enough that
    // the plain deviation transfer passes the body rubric.
    private static readonly Dictionary<string, string> AlignedBoneChild = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftShoulder"] = "LeftArm",
        ["RightShoulder"] = "RightArm",
        ["LeftArm"] = "LeftForeArm",
        ["RightArm"] = "RightForeArm",
        ["LeftForeArm"] = "LeftHand",
        ["RightForeArm"] = "RightHand",
    };

    // Rest-direction mismatches below this are noise (and the deviation transfer already
    // handles them fine) - skip alignment to avoid perturbing bones that don't need it.
    private const float AlignSkipDegrees = 5f;

    private static readonly List<AshlandsRebornFableWarrior> Registry = new();

    // Session-static dummy ZNetView so the puppet's VisEquipment runs in local (no-ZDO) mode.
    private static ZNetView? _dummyNView;

    // ---- registry ----------------------------------------------------------------------

    internal static void Register(AshlandsRebornFableWarrior m)
    {
        if (!Registry.Contains(m)) Registry.Add(m);
    }

    internal static void Unregister(AshlandsRebornFableWarrior m)
    {
        Registry.Remove(m);
    }

    // ---- spawn hook --------------------------------------------------------------------

    [HarmonyPatch(typeof(Humanoid), "Awake")]
    [HarmonyPostfix]
    private static void Humanoid_Awake_Postfix(Humanoid __instance)
    {
        if (!Plugin.IsFablePuppetActive) return;
        if (GetPrefabName(__instance.gameObject) != CharredMeleePrefab) return;
        if (__instance.GetComponent<AshlandsRebornFableWarrior>() != null) return;

        var marker = __instance.gameObject.AddComponent<AshlandsRebornFableWarrior>();
        __instance.StartCoroutine(BuildAfterSettle(__instance, marker));
    }

    private static IEnumerator BuildAfterSettle(Humanoid humanoid, AshlandsRebornFableWarrior marker)
    {
        // Let the skeleton, scale (world level / star), and first animation frame settle.
        for (var i = 0; i < 10; i++) yield return null;
        if (humanoid == null || marker == null) yield break;
        if (!Plugin.IsFablePuppetActive) yield break;

        var vis = humanoid.GetComponent<VisEquipment>();
        if (vis == null || vis.m_bodyModel == null) yield break;

        try
        {
            BuildPuppet(humanoid, vis, marker);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Fable Warrior] BuildPuppet failed: {ex}");
        }
    }

    // ---- puppet construction -----------------------------------------------------------

    private static void BuildPuppet(Humanoid humanoid, VisEquipment charredVis, AshlandsRebornFableWarrior marker)
    {
        if (!EnsureOffsets())
        {
            Plugin.Log?.LogError("[Fable Warrior] Bone offsets unavailable - cannot build puppet.");
            return;
        }

        var charredVisual = charredVis.transform.Find("Visual") ?? charredVis.transform;
        marker.CharredVisual = charredVisual;

        // Target height for scaling: use the Charred's CAPSULE COLLIDER (pose-independent).
        // Live renderer-bounds are unreliable - a warrior mid-lunge or with its sword raised
        // reports a wildly inflated height and yields an oversized puppet. The capsule height x
        // world scale is a stable proxy for the creature's standing height and folds in
        // star/world-level scaling automatically via lossyScale.
        var charredRenderers = charredVis.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer)
            .ToList();
        var capsule = humanoid.GetComponent<CapsuleCollider>();
        var targetHeight = capsule != null
            ? Mathf.Max(capsule.height, capsule.radius * 2f) * Mathf.Abs(humanoid.transform.lossyScale.y) * CapsuleToBodyHeight
            : UnionHeight(charredVis.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).Cast<Renderer>());

        // Hide the Charred's own body/armor renderers (record for revert).
        foreach (var r in charredRenderers)
        {
            marker.HiddenRenderers.Add(r);
            marker.HiddenRendererStates.Add(r.enabled);
            r.enabled = false;
        }

        // Keep the Charred Animator running while its renderers are hidden, else the retarget
        // SOURCE freezes (Valkyrie precedent).
        marker.CharredAnimator = humanoid.GetComponentInChildren<Animator>();
        if (marker.CharredAnimator != null)
        {
            marker.OriginalCulling = marker.CharredAnimator.cullingMode;
            marker.CharredAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // --- instantiate + strip the Player prefab while inactive (no gameplay Awake runs) ---
        var playerPrefab = Game.instance != null ? Game.instance.m_playerPrefab : null;
        if (playerPrefab == null) playerPrefab = ZNetScene.instance?.GetPrefab("Player");
        if (playerPrefab == null)
        {
            Plugin.Log?.LogError("[Fable Warrior] Player prefab not found - aborting puppet build.");
            return;
        }

        var holder = new GameObject("AR_PuppetBuildHolder");
        holder.SetActive(false);
        var puppet = UObject.Instantiate(playerPrefab, holder.transform);
        puppet.name = "AR_FablePuppet";

        StripPuppet(puppet);

        var puppetVis = puppet.GetComponent<VisEquipment>();
        if (puppetVis == null)
        {
            Plugin.Log?.LogError("[Fable Warrior] Puppet has no VisEquipment - aborting.");
            UObject.Destroy(holder);
            return;
        }
        puppetVis.m_clothColliders = Array.Empty<CapsuleCollider>();
        puppetVis.m_nViewOverride = GetDummyNView();

        // Reparent to the live (active) Charred Visual node -> puppet activates -> only
        // VisEquipment.Awake runs, and it takes m_nViewOverride (checked before GetComponent).
        puppet.transform.SetParent(charredVisual, worldPositionStays: false);
        puppet.transform.localPosition = Vector3.zero;
        puppet.transform.localRotation = Quaternion.identity;
        puppet.transform.localScale = Vector3.one;
        puppet.SetActive(true);
        UObject.Destroy(holder);

        marker.Puppet = puppet;
        marker.PuppetVis = puppetVis;
        marker.PuppetVisual = puppet.transform.Find("Visual") ?? puppet.transform;

        // Drive the base body model + colors once now (no equipment in M2).
        try { puppetVis.CustomUpdate(0f, 0f); } catch { /* non-fatal */ }

        // Keep the puppet body visible even when off-screen (cheap insurance vs LOD/culling).
        foreach (var smr in puppet.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;

        // Measure the puppet's height NOW, in its bind pose (before we drive it), so the
        // measurement is pose-independent - it is always the same player rig at localScale=1
        // under the inherited Visual scale.
        var puppetHeight = UnionHeight(new[] { (Renderer)puppetVis.m_bodyModel });
        var autoScale = 1f;
        if (puppetHeight > 0.001f && targetHeight > 0.001f)
            autoScale = targetHeight / puppetHeight;
        marker.BuiltAutoScale = autoScale;
        var finalScale = autoScale * (Plugin.FableWarriorScale?.Value ?? 1f);
        puppet.transform.localScale = Vector3.one * finalScale;

        // Build bone pairs and drive one frame so the puppet is posed (no T-pose flash).
        BuildBonePairs(charredVis, puppetVis, marker);
        Drive(marker);

        marker.Built = true;
        Plugin.Log?.LogInfo(
            $"[Fable Warrior] Puppet built on {humanoid.gameObject.name}: " +
            $"pairs={marker.Pairs?.Length ?? 0}, targetH={targetHeight:F2}, puppetH={puppetHeight:F2}, " +
            $"autoScale={autoScale:F3}, finalScale={finalScale:F3}");
        Plugin.Log?.LogInfo(
            $"[Fable Warrior] Frame ref: charredVisualLocalRot={charredVisual.localRotation.eulerAngles}, " +
            $"puppetVisualLocalRot={marker.PuppetVisual.localRotation.eulerAngles}");
    }

    /// <summary>
    /// Destroy every gameplay MonoBehaviour on the puppet (keeping VisEquipment), plus the
    /// Rigidbody, all Colliders, and Cloth. Runs while the puppet is inactive so no Awake or
    /// OnDestroy side effects fire. Multi-pass to satisfy RequireComponent dependency chains.
    /// </summary>
    private static void StripPuppet(GameObject puppet)
    {
        // Animator: keep the component (bones stay poseable) but disable it so Unity never
        // drives the puppet skeleton - only our retarget does.
        foreach (var anim in puppet.GetComponentsInChildren<Animator>(true))
            anim.enabled = false;

        for (var pass = 0; pass < 5; pass++)
        {
            var behaviours = puppet.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(mb => mb != null && !(mb is VisEquipment))
                .ToList();
            if (behaviours.Count == 0) break;

            var destroyedAny = false;
            foreach (var mb in behaviours)
            {
                try
                {
                    UObject.DestroyImmediate(mb);
                    destroyedAny = true;
                }
                catch
                {
                    // RequireComponent dependency still present; retry next pass.
                }
            }
            if (!destroyedAny) break;
        }

        var survivors = puppet.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(mb => mb != null && !(mb is VisEquipment))
            .Select(mb => mb.GetType().Name)
            .Distinct()
            .ToList();
        if (survivors.Count > 0)
            Plugin.Log?.LogWarning($"[Fable Warrior] Puppet strip survivors: {string.Join(", ", survivors)}");

        foreach (var col in puppet.GetComponentsInChildren<Collider>(true))
            TryDestroyImmediate(col);
        foreach (var rb in puppet.GetComponentsInChildren<Rigidbody>(true))
            TryDestroyImmediate(rb);
    }

    private static void TryDestroyImmediate(UObject o)
    {
        try { UObject.DestroyImmediate(o); } catch { /* dependency or already gone */ }
    }

    private static ZNetView GetDummyNView()
    {
        if (_dummyNView != null) return _dummyNView;
        var go = new GameObject("AR_DummyNView");
        go.SetActive(false); // ZNetView.Awake never runs -> GetZDO() stays null forever
        UObject.DontDestroyOnLoad(go);
        _dummyNView = go.AddComponent<ZNetView>();
        return _dummyNView;
    }

    // ---- bone retarget -----------------------------------------------------------------

    private static bool EnsureOffsets()
    {
        if (_offsets != null) return true;
        if (_offsetsFailed) return false;

        var charredPrefab = ZNetScene.instance?.GetPrefab(CharredMeleePrefab);
        var playerPrefab = Game.instance != null ? Game.instance.m_playerPrefab : ZNetScene.instance?.GetPrefab("Player");
        if (charredPrefab == null || playerPrefab == null)
        {
            _offsetsFailed = true;
            return false;
        }

        var charredVisual = charredPrefab.transform.Find("Visual") ?? charredPrefab.transform;
        var playerVisual = playerPrefab.transform.Find("Visual") ?? playerPrefab.transform;

        var charredBones = CollectBones(charredVisual);
        var playerBones = CollectBones(playerVisual);

        var cInv = Quaternion.Inverse(charredVisual.rotation);
        var pInv = Quaternion.Inverse(playerVisual.rotation);

        var dict = new Dictionary<string, (Quaternion, Quaternion)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in charredBones)
        {
            if (!playerBones.TryGetValue(kv.Key, out var pBone)) continue;
            var c0 = cInv * kv.Value.rotation;      // Charred rest rot in Charred-Visual frame
            var p0 = pInv * pBone.rotation;         // Player  rest rot in Player-Visual  frame

            // Arm chain: pre-swing the player's rest so its arm segment points where the
            // Charred's rest segment points; the per-frame deviation then tracks the Charred
            // arm exactly instead of carrying the rest difference as a constant error.
            var p0Eff = p0;
            if (AlignedBoneChild.TryGetValue(kv.Key, out var childName)
                && charredBones.TryGetValue(childName, out var cChild)
                && playerBones.TryGetValue(childName, out var pChild))
            {
                var cDir = cInv * (cChild.position - kv.Value.position);
                var pDir = pInv * (pChild.position - pBone.position);
                if (cDir.sqrMagnitude > 1e-8f && pDir.sqrMagnitude > 1e-8f)
                {
                    var angle = Vector3.Angle(pDir, cDir);
                    if (angle >= AlignSkipDegrees)
                        p0Eff = Quaternion.FromToRotation(pDir.normalized, cDir.normalized) * p0;
                    Plugin.Log?.LogInfo(
                        $"[Fable Warrior] Rest-align {kv.Key}: {angle:F1} deg" +
                        (angle >= AlignSkipDegrees ? "" : " (below threshold, skipped)"));
                }
            }

            dict[kv.Key] = (Quaternion.Inverse(c0), p0Eff);
        }

        _offsets = dict;
        Plugin.Log?.LogInfo($"[Fable Warrior] Bone offsets built: {dict.Count} shared bones " +
                            $"(charred={charredBones.Count}, player={playerBones.Count}).");
        return true;
    }

    private static Dictionary<string, Transform> CollectBones(Transform root)
    {
        var map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        void Walk(Transform t)
        {
            if (!map.ContainsKey(t.name)) map[t.name] = t;
            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
        }
        Walk(root);
        return map;
    }

    private static void BuildBonePairs(VisEquipment charredVis, VisEquipment puppetVis, AshlandsRebornFableWarrior marker)
    {
        var charredMap = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in charredVis.m_bodyModel.bones)
            if (b != null && !charredMap.ContainsKey(b.name)) charredMap[b.name] = b;
        foreach (var kv in CollectBones(marker.CharredVisual!))
            if (!charredMap.ContainsKey(kv.Key)) charredMap[kv.Key] = kv.Value;

        var puppetMap = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in puppetVis.m_bodyModel.bones)
            if (b != null && !puppetMap.ContainsKey(b.name)) puppetMap[b.name] = b;
        foreach (var kv in CollectBones(marker.PuppetVisual!))
            if (!puppetMap.ContainsKey(kv.Key)) puppetMap[kv.Key] = kv.Value;

        // Pair every bone with a prefab-derived rest offset that exists in BOTH live rigs.
        var pairs = new List<BonePair>();
        foreach (var kv in _offsets!)
        {
            if (!charredMap.TryGetValue(kv.Key, out var c)) continue;
            if (!puppetMap.TryGetValue(kv.Key, out var p)) continue;
            pairs.Add(new BonePair
            {
                C = c,
                P = p,
                C0Inv = kv.Value.c0Inv,
                P0Eff = kv.Value.p0Eff,
                Depth = Depth(p, marker.PuppetVisual!),
            });
        }
        // Parents before children so world-rotation writes aren't disturbed by later parent writes.
        pairs.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        marker.Pairs = pairs.ToArray();
    }

    private static int Depth(Transform t, Transform stop)
    {
        var d = 0;
        var cur = t;
        while (cur != null && cur != stop) { d++; cur = cur.parent; }
        return d;
    }

    /// <summary>
    /// Deviation-from-rest rotation retarget. For each bone: measure how far the Charred bone
    /// has rotated from its rest pose (in the Charred Visual frame), then apply that same delta
    /// to the puppet bone relative to ITS rest pose (in the puppet Visual frame). Because the
    /// delta is relative to each skeleton's own rest, rest-orientation mismatches cancel and
    /// the human rest posture is preserved. The arm chain's rest poses differ by a large
    /// constant, so their P0Eff carries a baked-in alignment swing (see EnsureOffsets) - which
    /// makes this algebraically equivalent to a direct orientation copy with a per-bone basis
    /// correction. Rotation-only: the puppet keeps its own bone lengths and proportions.
    /// </summary>
    private static void Drive(AshlandsRebornFableWarrior marker)
    {
        if (marker.Pairs == null || marker.CharredVisual == null || marker.PuppetVisual == null) return;

        var crInv = Quaternion.Inverse(marker.CharredVisual.rotation);
        var pr = marker.PuppetVisual.rotation;

        var pairs = marker.Pairs;
        for (var i = 0; i < pairs.Length; i++)
        {
            var bp = pairs[i];
            if (bp.C == null || bp.P == null) continue;
            var cLive = crInv * bp.C.rotation;        // Charred bone rot in Charred-Visual frame
            var delta = cLive * bp.C0Inv;             // delta from Charred rest
            bp.P.rotation = pr * delta * bp.P0Eff;    // apply to puppet from its (aligned) rest
        }
    }

    // ---- lifecycle ---------------------------------------------------------------------

    [HarmonyPatch(typeof(MonoUpdaters), "LateUpdate")]
    [HarmonyPostfix]
    private static void MonoUpdaters_LateUpdate_Postfix()
    {
        if (Registry.Count == 0) return;
        for (var i = 0; i < Registry.Count; i++)
        {
            var m = Registry[i];
            if (m != null && m.Built) Drive(m);
        }
    }

    internal static void RefreshAll()
    {
        RevertAll();
        if (!Plugin.IsFablePuppetActive) return;

        foreach (var humanoid in UObject.FindObjectsByType<Humanoid>(FindObjectsSortMode.None))
        {
            if (GetPrefabName(humanoid.gameObject) != CharredMeleePrefab) continue;
            if (humanoid.GetComponent<AshlandsRebornFableWarrior>() != null) continue;
            var vis = humanoid.GetComponent<VisEquipment>();
            if (vis == null || vis.m_bodyModel == null) continue;
            var marker = humanoid.gameObject.AddComponent<AshlandsRebornFableWarrior>();
            humanoid.StartCoroutine(BuildAfterSettle(humanoid, marker));
        }
    }

    internal static void RevertAll()
    {
        foreach (var m in UObject.FindObjectsByType<AshlandsRebornFableWarrior>(FindObjectsSortMode.None))
            m.RevertAndDestroy();
    }

    internal static void PeriodicUpdate()
    {
        // M2: nothing yet. M3 adds pending-equip apply + live armor-change resync here.
    }

    // ---- helpers -----------------------------------------------------------------------

    private static float UnionHeight(IEnumerable<Renderer> renderers)
    {
        var has = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has ? b.size.y : 0f;
    }

    private static string GetPrefabName(GameObject go)
    {
        var name = go.name;
        var idx = name.IndexOf('(');
        return idx >= 0 ? name.Substring(0, idx).Trim() : name;
    }

    internal struct BonePair
    {
        public Transform C;
        public Transform P;
        public Quaternion C0Inv;
        public Quaternion P0Eff;
        public int Depth;
    }
}

/// <summary>Per-warrior state for the Fable Warrior puppet. Lives on the Charred_Melee root.</summary>
internal class AshlandsRebornFableWarrior : MonoBehaviour
{
    public GameObject? Puppet;
    public VisEquipment? PuppetVis;
    public Transform? CharredVisual;
    public Transform? PuppetVisual;
    public FableWarriorPatches.BonePair[]? Pairs;
    public float BuiltAutoScale;
    public Animator? CharredAnimator;
    public AnimatorCullingMode OriginalCulling;
    public readonly List<Renderer> HiddenRenderers = new();
    public readonly List<bool> HiddenRendererStates = new();
    public bool Built;

    private void OnEnable() => FableWarriorPatches.Register(this);
    private void OnDisable() => FableWarriorPatches.Unregister(this);
    private void OnDestroy() => FableWarriorPatches.Unregister(this);

    public void RevertAndDestroy()
    {
        try
        {
            if (Puppet != null) UnityEngine.Object.Destroy(Puppet);

            for (var i = 0; i < HiddenRenderers.Count; i++)
            {
                var r = HiddenRenderers[i];
                if (r != null) r.enabled = i < HiddenRendererStates.Count ? HiddenRendererStates[i] : true;
            }

            if (CharredAnimator != null)
                CharredAnimator.cullingMode = OriginalCulling;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Fable Warrior] Revert warning: {ex.Message}");
        }
        finally
        {
            UnityEngine.Object.Destroy(this);
        }
    }
}
