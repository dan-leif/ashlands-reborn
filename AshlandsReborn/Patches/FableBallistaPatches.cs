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
/// Fable Ballista: replaces the Skugg's flesh-ballista visuals with the player Ballista
/// buildable (piece_turret). The "Skugg" is NOT a creature - it is the Charred Ballista
/// piece (prefab piece_Charred_Balista, display token piece_charredballista) found in
/// Ashlands fortresses, driven by the same vanilla Turret component as the player Ballista.
/// Visual-only: the Skugg's Turret keeps aiming/shooting and its WearNTear keeps taking
/// damage - only the look changes.
///
/// UnityPy recon (bundle c4210710): both prefabs share an IDENTICAL visual hierarchy -
/// New/Base, New/NeckRotation, New/BodyRotation/{Body, Body_Unarmed, Bolt*, Mag, Eye} -
/// the Skugg just adds its own Skug_Missile bolt child and has an empty NeckRotation.
/// That makes the swap a pure mirror job:
///
///  - The Skugg's renderers are hidden (forceRenderingOff + invisible materials, the
///    bunny's two-layer animation/code-proof hide). GameObject ACTIVE states are left
///    alone, so the Skugg's own Turret keeps toggling its (hidden) armed/unarmed bodies
///    and bolt visuals - those toggles are our mirror source.
///  - A stripped piece_turret clone sits on a pivot at the Skugg root. Every LateUpdate:
///    the donor's BodyRotation/NeckRotation copy the Skugg's live local rotations (real
///    aim tracking), and the armed/unarmed/bolt children mirror the Skugg's active states
///    (real reload/ammo reads). Skugg-only bolt visuals (Skug_Missile) fall back to the
///    donor's first bolt child.
///  - Turret.ShootProjectile postfix arms a procedural recoil (pitch kick + pushback) on
///    the pivot so firing reads even at a glance.
///
/// All hooks are applied manually with null-guards (ApplyBallistaPatches) so a game-update
/// rename degrades to a logged warning instead of breaking PatchAll.
/// </summary>
[HarmonyPatch]
internal static class FableBallistaPatches
{
    private const string PivotName = "AR_FableBallistaPivot";
    private const string DefaultSourcePrefab = "piece_Charred_Balista";

    private static readonly List<AshlandsRebornFableBallista> Registry = new();
    private static int _swapLogCount;
    private static bool _donorInventoryLogged;
    private static bool _sourceProbeDone;

    // ---- registry ----------------------------------------------------------------------

    internal static void Register(AshlandsRebornFableBallista m)
    {
        if (!Registry.Contains(m)) Registry.Add(m);
    }

    internal static void Unregister(AshlandsRebornFableBallista m)
    {
        Registry.Remove(m);
    }

    private static string SourcePrefabName
    {
        get
        {
            var s = Plugin.FableBallistaSourcePrefab?.Value?.Trim();
            if (string.IsNullOrEmpty(s)) return DefaultSourcePrefab;
            // Accept the display name as an alias - users know this thing as "the Skugg".
            if (string.Equals(s, "Skugg", StringComparison.OrdinalIgnoreCase)) return DefaultSourcePrefab;
            return s!;
        }
    }

    private static string DonorPrefabName => Plugin.FableBallistaDonorPrefab?.Value?.Trim() is { Length: > 0 } s ? s : "piece_turret";

    // ---- manual patch application (called from Plugin.Awake after PatchAll) --------------

    internal static void ApplyBallistaPatches(Harmony harmony)
    {
        var applied = new List<string>();

        TryPatch(harmony, applied, "Turret.Awake",
            ResolveMethod(typeof(Turret), "Awake"),
            postfix: AccessTools.Method(typeof(FableBallistaPatches), nameof(Turret_Awake_Postfix)));

        TryPatch(harmony, applied, "Turret.ShootProjectile",
            ResolveMethod(typeof(Turret), "ShootProjectile"),
            postfix: AccessTools.Method(typeof(FableBallistaPatches), nameof(ShootProjectile_Postfix)));

        Plugin.Log?.LogInfo($"[Fable Ballista] Patches applied: {string.Join(", ", applied)}");
    }

    private static MethodInfo? ResolveMethod(Type type, string name)
    {
        try
        {
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == name);
        }
        catch
        {
            return null;
        }
    }

    private static void TryPatch(Harmony harmony, List<string> applied, string label, MethodInfo? target, MethodInfo? postfix)
    {
        if (target == null || postfix == null)
        {
            Plugin.Log?.LogWarning($"[Fable Ballista] Patch target not found: {label} - feature degraded");
            return;
        }
        try
        {
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            applied.Add(label);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Fable Ballista] Patch failed for {label}: {ex.Message}");
        }
    }

    // ---- spawn hook ----------------------------------------------------------------------

    private static void Turret_Awake_Postfix(Turret __instance)
    {
        try
        {
            TryBuild(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Fable Ballista] Awake hook failed: {ex}");
        }
    }

    internal static void TryBuild(Turret turret)
    {
        if (!Plugin.IsFableBallistaActive) return;
        if (turret == null) return;
        if (!string.Equals(GetPrefabName(turret.gameObject), SourcePrefabName, StringComparison.OrdinalIgnoreCase)) return;
        if (turret.GetComponent<AshlandsRebornFableBallista>() != null) return;

        var marker = turret.gameObject.AddComponent<AshlandsRebornFableBallista>();
        marker.Source = turret;
        turret.StartCoroutine(BuildAfterSettle(turret, marker));
    }

    private static IEnumerator BuildAfterSettle(Turret turret, AshlandsRebornFableBallista marker)
    {
        // Let the piece finish initializing (ZNetView/WearNTear state) before measuring.
        for (var i = 0; i < 5; i++) yield return null;
        if (turret == null || marker == null) yield break;
        if (!Plugin.IsFableBallistaActive) yield break;

        try
        {
            BuildBallista(turret, marker);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Fable Ballista] BuildBallista failed: {ex}");
        }
    }

    // ---- construction ----------------------------------------------------------------------

    private static void BuildBallista(Turret source, AshlandsRebornFableBallista marker)
    {
        // Measure the Skugg's visual height BEFORE hiding (bounds of live renderers); the
        // donor is scaled so its own visual height matches.
        var sourceHeight = UnionHeight(source.GetComponentsInChildren<Renderer>()
            .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r.enabled));

        // Capture the Skugg's live driver nodes off its own Turret component (public fields):
        // BodyRotation/NeckRotation get rotated by UpdateTurretRotation, Body/Body_Unarmed
        // get toggled by UpdateReloadState, bolt visuals by UpdateVisualBolt. All of those
        // keep running on the hidden Skugg - they are the mirror source.
        marker.SrcBodyRot = source.m_turretBody != null ? source.m_turretBody.transform : null;
        marker.SrcNeckRot = source.m_turretNeck != null ? source.m_turretNeck.transform : null;

        // Hide ALL Skugg renderers, recorded for revert. Two layers of defence: the flag is
        // code-proof (nothing in Turret touches it) and the invisible material wins even if
        // something re-enables a renderer.
        foreach (var r in source.GetComponentsInChildren<Renderer>(true))
        {
            marker.HiddenRenderers.Add(r);
            marker.HiddenMaterials.Add(r.sharedMaterials);
            r.forceRenderingOff = true;
            r.sharedMaterials = FableBunnyPatches.InvisibleMaterials(r.sharedMaterials.Length);
        }

        // Pivot at the Skugg root (identity - the piece doesn't move; the pivot exists for
        // recoil, YOffset, and free cleanup on revert).
        var pivot = new GameObject(PivotName);
        pivot.transform.SetParent(source.transform, worldPositionStays: false);
        pivot.transform.localPosition = Vector3.zero;
        pivot.transform.localRotation = Quaternion.identity;
        marker.Pivot = pivot.transform;
        marker.TargetHeight = sourceHeight;

        if (!BuildDonorVisual(marker, source))
        {
            Plugin.Log?.LogError($"[Fable Ballista] No donor visual could be built (donor='{DonorPrefabName}') - reverting.");
            marker.RevertAndDestroy();
            return;
        }

        marker.Built = true;

        if (_swapLogCount++ < 8)
            Plugin.Log?.LogInfo(
                $"[Fable Ballista] Swapped {SourcePrefabName} -> {DonorPrefabName}: " +
                $"srcH={sourceHeight:F2}, rawH={marker.RawHeight:F2}, " +
                $"scale={(marker.Visual != null ? marker.Visual.transform.localScale.y : 0f):F3}, " +
                $"mirrors={marker.MirrorSrc.Count}, aim={(marker.SrcBodyRot != null && marker.DstBodyRot != null ? "body" : "-")}" +
                $"{(marker.SrcNeckRot != null && marker.DstNeckRot != null ? "+neck" : "")}");
    }

    private static bool BuildDonorVisual(AshlandsRebornFableBallista marker, Turret source)
    {
        var prefab = ZNetScene.instance?.GetPrefab(DonorPrefabName);
        if (prefab == null || marker.Pivot == null)
        {
            if (prefab == null)
                Plugin.Log?.LogWarning($"[Fable Ballista] Donor prefab '{DonorPrefabName}' not found in ZNetScene");
            return false;
        }

        // Inactive-holder instantiate: no gameplay Awake runs (ZNetView/Piece/WearNTear/Turret
        // never register a ZDO or start their state machines), then strip to visuals.
        var holder = new GameObject("AR_BallistaBuildHolder");
        holder.SetActive(false);
        var clone = UObject.Instantiate(prefab, holder.transform);
        clone.name = $"AR_FableBallista_{DonorPrefabName}";

        // Capture the donor's driver/toggle nodes off its own Turret component BEFORE the
        // strip (the Transform/GameObject refs survive component destruction), and force
        // the states its stripped Turret/WearNTear would otherwise manage.
        GameObject? dstArmed = null, dstUnarmed = null;
        var donorTurret = clone.GetComponentInChildren<Turret>(true);
        if (donorTurret != null)
        {
            marker.DstBodyRot = donorTurret.m_turretBody != null ? donorTurret.m_turretBody.transform : null;
            marker.DstNeckRot = donorTurret.m_turretNeck != null ? donorTurret.m_turretNeck.transform : null;
            dstArmed = donorTurret.m_turretBodyArmed;
            dstUnarmed = donorTurret.m_turretBodyUnarmed;
            if (donorTurret.m_marker != null) donorTurret.m_marker.gameObject.SetActive(false);
            if (donorTurret.m_allowedAmmo != null)
                foreach (var ammo in donorTurret.m_allowedAmmo)
                    if (ammo.m_visual != null) ammo.m_visual.SetActive(false);
        }
        else
        {
            Plugin.Log?.LogWarning($"[Fable Ballista] Donor '{DonorPrefabName}' has no Turret component - no aim mirror");
        }
        var wnt = clone.GetComponentInChildren<WearNTear>(true);
        if (wnt != null)
        {
            if (wnt.m_worn != null && wnt.m_worn != wnt.m_new) wnt.m_worn.SetActive(false);
            if (wnt.m_broken != null && wnt.m_broken != wnt.m_new) wnt.m_broken.SetActive(false);
            if (wnt.m_new != null) wnt.m_new.SetActive(true);
        }

        FableBunnyPatches.StripToVisual(clone);

        // One-time recon: reveals prefab-authored child states the forcing above missed.
        if (!_donorInventoryLogged)
        {
            _donorInventoryLogged = true;
            var inventory = clone.GetComponentsInChildren<Renderer>(true)
                .Select(r => $"{r.name}({(r.gameObject.activeSelf ? "on" : "off")})")
                .ToList();
            Plugin.Log?.LogInfo($"[Fable Ballista] donor renderers: {string.Join(", ", inventory)}");
        }

        clone.transform.SetParent(marker.Pivot, worldPositionStays: false);
        clone.transform.localPosition = Vector3.up * (Plugin.FableBallistaYOffset?.Value ?? 0f);
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        clone.SetActive(true);
        UObject.Destroy(holder);

        // Height-match: both pieces put their origin at the build point, so no ground
        // alignment is needed - just scale the donor to the Skugg's visual height.
        var rawHeight = UnionHeight(clone.GetComponentsInChildren<Renderer>()
            .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r.enabled));
        if (rawHeight <= 0.01f)
        {
            Plugin.Log?.LogWarning($"[Fable Ballista] Donor '{DonorPrefabName}' has no measurable renderers");
            UObject.Destroy(clone);
            return false;
        }
        marker.RawHeight = rawHeight;
        var autoScale = marker.TargetHeight > 0.01f ? marker.TargetHeight / rawHeight : 1f;
        clone.transform.localScale = Vector3.one * (autoScale * (Plugin.FableBallistaScale?.Value ?? 1f));

        marker.Visual = clone;
        BuildMirrorPairs(marker, source, dstArmed, dstUnarmed);
        return true;
    }

    /// <summary>
    /// Pair every toggleable Skugg visual with its donor counterpart so Drive can mirror
    /// active states: armed/unarmed bodies from the Turret fields, bolt/Mag visuals by
    /// name under BodyRotation. Skugg-only children (Skug_Missile) fall back to the donor's
    /// armed-state pairing partner's first bolt sibling so a loaded Skugg always shows a
    /// loaded donor bolt. Multiple sources may map to one destination (OR'd in Drive).
    /// </summary>
    private static void BuildMirrorPairs(AshlandsRebornFableBallista marker, Turret source,
        GameObject? dstArmed, GameObject? dstUnarmed)
    {
        void AddPair(GameObject? src, GameObject? dst)
        {
            if (src == null || dst == null) return;
            marker.MirrorSrc.Add(src);
            marker.MirrorDst.Add(dst);
        }

        AddPair(source.m_turretBodyArmed, dstArmed);
        AddPair(source.m_turretBodyUnarmed, dstUnarmed);

        if (marker.SrcBodyRot == null || marker.DstBodyRot == null) return;

        // Donor bolt fallback for Skugg-only visuals: prefer "Bolt Black Metal", else the
        // first renderer-bearing BodyRotation child that isn't the armed/unarmed body.
        GameObject? fallback = null;
        foreach (Transform child in marker.DstBodyRot)
        {
            if (child.gameObject == dstArmed || child.gameObject == dstUnarmed) continue;
            if (child.GetComponent<Renderer>() == null) continue;
            if (fallback == null) fallback = child.gameObject;
            if (child.name.IndexOf("Black Metal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fallback = child.gameObject;
                break;
            }
        }

        foreach (Transform child in marker.SrcBodyRot)
        {
            var go = child.gameObject;
            if (go == source.m_turretBodyArmed || go == source.m_turretBodyUnarmed) continue;
            if (child.GetComponent<Renderer>() == null) continue;
            var dst = marker.DstBodyRot.Find(child.name);
            AddPair(go, dst != null ? dst.gameObject : fallback);
        }
    }

    // ---- fire hook -----------------------------------------------------------------------

    private static void ShootProjectile_Postfix(Turret __instance)
    {
        if (!Plugin.IsFableBallistaActive || __instance == null) return;
        var marker = __instance.GetComponent<AshlandsRebornFableBallista>();
        if (marker == null || !marker.Built) return;

        marker.RecoilDuration = 0.4f;
        marker.RecoilEnd = Time.time + marker.RecoilDuration;
        marker.RecoilAmplitude = Plugin.FableBallistaRecoilAmplitude?.Value ?? 1f;
    }

    // ---- per-frame driver ----------------------------------------------------------------

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

    private static readonly Dictionary<GameObject, bool> MirrorStates = new();

    private static void Drive(AshlandsRebornFableBallista marker)
    {
        if (marker.Source == null || marker.Pivot == null) return;

        // Aim mirror: the hierarchies are structurally identical and the donor pivot is
        // aligned with the Skugg root, so a straight localRotation copy of the rotation
        // nodes reproduces the live aim exactly.
        if (marker.SrcBodyRot != null && marker.DstBodyRot != null)
            marker.DstBodyRot.localRotation = marker.SrcBodyRot.localRotation;
        if (marker.SrcNeckRot != null && marker.DstNeckRot != null)
            marker.DstNeckRot.localRotation = marker.SrcNeckRot.localRotation;

        // State mirror: OR every source's activeSelf into its destination (several Skugg
        // bolt visuals can share one donor bolt via the fallback pairing).
        if (marker.MirrorSrc.Count > 0)
        {
            MirrorStates.Clear();
            for (var i = 0; i < marker.MirrorSrc.Count; i++)
            {
                var src = marker.MirrorSrc[i];
                var dst = marker.MirrorDst[i];
                if (src == null || dst == null) continue;
                MirrorStates.TryGetValue(dst, out var on);
                MirrorStates[dst] = on || src.activeSelf;
            }
            foreach (var kv in MirrorStates)
                if (kv.Key.activeSelf != kv.Value)
                    kv.Key.SetActive(kv.Value);
        }

        // Fire recoil: pitch kick-up + backward push, sharp at the start, damped settle.
        var pitch = 0f;
        var push = 0f;
        if (marker.RecoilEnd > Time.time && marker.RecoilDuration > 0.01f)
        {
            var u = 1f - (marker.RecoilEnd - Time.time) / marker.RecoilDuration;
            var envelope = Mathf.Sin(Mathf.PI * u) * (1f - u);
            pitch = marker.RecoilAmplitude * 10f * envelope;
            push = marker.RecoilAmplitude * 0.06f * Mathf.Max(marker.TargetHeight, 1f) * envelope;
        }
        if (pitch != 0f || marker.Pivot.localRotation != Quaternion.identity)
            marker.Pivot.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
        if (push != 0f || marker.Pivot.localPosition != Vector3.zero)
            marker.Pivot.localPosition = Vector3.back * push;
    }

    // ---- lifecycle -----------------------------------------------------------------------

    internal static void RefreshAll()
    {
        RevertAll();
        if (!Plugin.IsFableBallistaActive) return;
        Plugin.Instance.StartCoroutine(RebuildAfterRevert());
    }

    /// <summary>One-frame wait after RevertAll: marker Destroy is deferred, and a same-frame
    /// rescan still sees the stale markers and skips every Skugg (bunny/warrior gotcha).</summary>
    private static IEnumerator RebuildAfterRevert()
    {
        yield return null;
        if (!Plugin.IsFableBallistaActive) yield break;

        foreach (var turret in UObject.FindObjectsByType<Turret>(FindObjectsSortMode.None))
        {
            if (turret == null) continue;
            TryBuild(turret);
        }
    }

    internal static void RevertAll()
    {
        foreach (var m in UObject.FindObjectsByType<AshlandsRebornFableBallista>(FindObjectsSortMode.None))
            m.RevertAndDestroy();
    }

    private static float _periodicTimer;

    /// <summary>
    /// Called from Plugin's 0.2s block; runs every ~2s. Re-hides re-enabled Skugg renderers,
    /// re-applies the visual scale when the config changed (makes the F1 slider live), and
    /// runs the one-time source-prefab probe.
    /// </summary>
    internal static void PeriodicUpdate()
    {
        if (!Plugin.IsFableBallistaActive) return;
        _periodicTimer += 0.2f;
        if (_periodicTimer < 2f) return;
        _periodicTimer = 0f;

        ProbeSourcePrefab();

        for (var i = 0; i < Registry.Count; i++)
        {
            var m = Registry[i];
            if (m == null || !m.Built) continue;

            for (var r = 0; r < m.HiddenRenderers.Count; r++)
                if (m.HiddenRenderers[r] != null && !m.HiddenRenderers[r].forceRenderingOff)
                    m.HiddenRenderers[r].forceRenderingOff = true;

            if (m.Visual != null && m.RawHeight > 0.01f && m.TargetHeight > 0.01f)
            {
                var desired = m.TargetHeight / m.RawHeight * (Plugin.FableBallistaScale?.Value ?? 1f);
                var current = m.Visual.transform.localScale.y;
                if (current > 1e-5f && Mathf.Abs(desired - current) / current > 0.02f)
                    m.Visual.transform.localScale = Vector3.one * desired;
            }
        }
    }

    /// <summary>One-time recon: if the configured source prefab name misses, log every
    /// ZNetScene candidate so the cfg string can be fixed without a rebuild.</summary>
    private static void ProbeSourcePrefab()
    {
        if (_sourceProbeDone || ZNetScene.instance == null) return;
        _sourceProbeDone = true;

        try
        {
            var prefab = ZNetScene.instance.GetPrefab(SourcePrefabName);
            if (prefab != null)
            {
                Plugin.Log?.LogInfo(
                    $"[Fable Ballista] source prefab '{SourcePrefabName}' FOUND " +
                    $"(Turret={prefab.GetComponent<Turret>() != null})");
            }
            else
            {
                var candidates = ZNetScene.instance.m_prefabs
                    .Where(p => p != null)
                    .Select(p => p.name)
                    .Where(n => n.IndexOf("skug", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.IndexOf("balista", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.IndexOf("ballista", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                Plugin.Log?.LogWarning(
                    $"[Fable Ballista] source prefab '{SourcePrefabName}' NOT FOUND; " +
                    $"candidates: {(candidates.Count > 0 ? string.Join(", ", candidates) : "(none)")}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Fable Ballista] prefab probe failed: {ex.Message}");
        }
    }

    // ---- helpers -----------------------------------------------------------------------

    private static Bounds? UnionBounds(IEnumerable<Renderer> renderers)
    {
        var has = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return has ? b : (Bounds?)null;
    }

    private static float UnionHeight(IEnumerable<Renderer> renderers)
        => UnionBounds(renderers)?.size.y ?? 0f;

    private static string GetPrefabName(GameObject go)
    {
        var name = go.name;
        var idx = name.IndexOf('(');
        return idx >= 0 ? name.Substring(0, idx).Trim() : name;
    }
}

/// <summary>Per-Skugg state for the Fable Ballista swap. Lives on the Skugg piece root.</summary>
internal class AshlandsRebornFableBallista : MonoBehaviour
{
    public Turret? Source;
    public Transform? Pivot;
    public GameObject? Visual;
    // Live driver nodes: Src* on the hidden Skugg (kept running by its own Turret),
    // Dst* on the stripped donor clone (captured before the strip).
    public Transform? SrcBodyRot;
    public Transform? SrcNeckRot;
    public Transform? DstBodyRot;
    public Transform? DstNeckRot;
    // Active-state mirror pairs (parallel lists; several srcs may share a dst - OR'd).
    public readonly List<GameObject> MirrorSrc = new();
    public readonly List<GameObject> MirrorDst = new();
    // Hidden via forceRenderingOff + invisible material swap; revert clears the flag and
    // restores the cached material arrays.
    public readonly List<Renderer> HiddenRenderers = new();
    public readonly List<Material[]> HiddenMaterials = new();
    public float TargetHeight;
    public float RawHeight;
    public float RecoilEnd;
    public float RecoilDuration;
    public float RecoilAmplitude;
    public bool Built;

    private void OnEnable() => FableBallistaPatches.Register(this);
    private void OnDisable() => FableBallistaPatches.Unregister(this);
    private void OnDestroy() => FableBallistaPatches.Unregister(this);

    public void RevertAndDestroy()
    {
        try
        {
            if (Pivot != null) UnityEngine.Object.Destroy(Pivot.gameObject);

            for (var i = 0; i < HiddenRenderers.Count; i++)
            {
                var r = HiddenRenderers[i];
                if (r == null) continue;
                r.forceRenderingOff = false;
                if (i < HiddenMaterials.Count && HiddenMaterials[i] != null)
                    r.sharedMaterials = HiddenMaterials[i];
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Fable Ballista] Revert warning: {ex.Message}");
        }
        finally
        {
            UnityEngine.Object.Destroy(this);
        }
    }
}
