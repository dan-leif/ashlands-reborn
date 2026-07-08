using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// <summary>
/// Fable Bunny: replaces the Morgen's bone-and-sinew visuals with a giant, self-animating
/// donor creature (default: Hare). Unlike the Fable Warrior puppet (bone retargeting, needs
/// shared Mixamo names) and the Valkyrie bind-pose swap (needs structurally compatible rigs),
/// the Morgen's blob rig shares nothing with any pleasant donor - so the donor keeps its OWN
/// Animator and we sync it to the Morgen's actual behavior instead:
///
///  - The Morgen's renderers are hidden and its Animator forced to AlwaysAnimate so its
///    animation-event-driven attack timing keeps working invisibly (gameplay untouched).
///  - A stripped donor clone (Animator kept ENABLED - the deliberate difference from
///    FableWarriorPatches.StripPuppet) sits on an upright pivot under the Morgen root.
///  - Locomotion: every LateUpdate the Morgen's planar velocity feeds the donor's
///    "forward_speed"/"turn_speed" animator params (the same params vanilla ZSyncAnimation
///    writes), so the donor idles/hops/runs exactly when the Morgen does.
///  - Attacks: a Humanoid.StartAttack postfix classifies the Morgen attack by animation name
///    and arms a procedural "pounce" (squash-stretch + lunge pitch) applied to the pivot
///    ABOVE the donor's Animator, composing with the hop cycle instead of fighting it.
///  - Roll: the pivot's yaw-only stabilizer cancels any tumble and forward_speed is floored,
///    so the ball attack reads as the donor bounding at rolling speed.
///  - Hybrid mode ("LoxBiteRoll"): a second Lox proxy is built alongside the primary donor
///    and swapped in for bite/roll attacks (the Lox has a real bite animation).
///  - Death: Ragdoll.Awake hides the Morgen ragdoll's renderers (vanilla FX/drops untouched).
///
/// All spawn/attack/ragdoll hooks are applied manually with null-guards (ApplyBunnyPatches)
/// so a game-update rename degrades to a logged warning instead of breaking PatchAll.
/// </summary>
[HarmonyPatch]
internal static class FableBunnyPatches
{
    private const string MorgenPrefab = "Morgen";
    private const string LoxPrefab = "Lox";
    private const string PivotName = "AR_FableBunnyPivot";

    private static readonly List<AshlandsRebornFableBunny> Registry = new();
    private static readonly Dictionary<string, GameObject?> DonorTemplates = new(StringComparer.OrdinalIgnoreCase);
    private static int _swapLogCount;
    private static int _attackLogCount;
    private static bool _ragdollLogDone;

    private static readonly int ForwardSpeedHash = Animator.StringToHash("forward_speed");
    private static readonly int TurnSpeedHash = Animator.StringToHash("turn_speed");
    private static readonly int OnGroundHash = Animator.StringToHash("onGround");

    // Humanoid.m_currentAttack / Attack.m_attackAnimation via reflection - accessibility has
    // shifted between game versions; a null just degrades attack classification to "unknown".
    private static readonly FieldInfo? FCurrentAttack = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
    private static readonly FieldInfo? FAttackAnimation = AccessTools.Field(typeof(Attack), "m_attackAnimation");

    internal enum AttackClass { Lash, Smash, Bite, Roll, Unknown }

    // ---- registry ----------------------------------------------------------------------

    internal static int RegistryCount => Registry.Count;

    internal static void Register(AshlandsRebornFableBunny m)
    {
        if (!Registry.Contains(m)) Registry.Add(m);
    }

    internal static void Unregister(AshlandsRebornFableBunny m)
    {
        Registry.Remove(m);
    }

    // ---- manual patch application (called from Plugin.Awake after PatchAll) --------------

    internal static void ApplyBunnyPatches(Harmony harmony)
    {
        var applied = new List<string>();

        TryPatch(harmony, applied, "Character.Awake",
            ResolveMethod(typeof(Character), "Awake"),
            postfix: AccessTools.Method(typeof(FableBunnyPatches), nameof(Character_Awake_Postfix)));

        var startAttack = ResolveMethod(typeof(Humanoid), "StartAttack") ?? ResolveMethod(typeof(Character), "StartAttack");
        TryPatch(harmony, applied, "StartAttack", startAttack,
            postfix: AccessTools.Method(typeof(FableBunnyPatches), nameof(StartAttack_Postfix)));

        TryPatch(harmony, applied, "Ragdoll.Awake",
            ResolveMethod(typeof(Ragdoll), "Awake"),
            postfix: AccessTools.Method(typeof(FableBunnyPatches), nameof(Ragdoll_Awake_Postfix)));

        Plugin.Log?.LogInfo($"[Fable Bunny] Patches applied: {string.Join(", ", applied)}");
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
            Plugin.Log?.LogWarning($"[Fable Bunny] Patch target not found: {label} - feature degraded");
            return;
        }
        try
        {
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            applied.Add(label);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Fable Bunny] Patch failed for {label}: {ex.Message}");
        }
    }

    // ---- spawn hook ----------------------------------------------------------------------

    private static void Character_Awake_Postfix(Character __instance)
    {
        try
        {
            TryBuild(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Fable Bunny] Awake hook failed: {ex}");
        }
    }

    internal static void TryBuild(Character character)
    {
        if (!Plugin.IsFableBunnyActive) return;
        if (character == null) return;
        if (!string.Equals(GetPrefabName(character.gameObject), MorgenPrefab, StringComparison.OrdinalIgnoreCase)) return;
        if (character.GetComponent<AshlandsRebornFableBunny>() != null) return;

        var marker = character.gameObject.AddComponent<AshlandsRebornFableBunny>();
        marker.Source = character;
        character.StartCoroutine(BuildAfterSettle(character, marker));
    }

    private static IEnumerator BuildAfterSettle(Character character, AshlandsRebornFableBunny marker)
    {
        // Let the skeleton, star scale, and first animation frame settle (warrior pattern).
        for (var i = 0; i < 10; i++) yield return null;
        if (character == null || marker == null) yield break;
        if (!Plugin.IsFableBunnyActive) yield break;

        try
        {
            BuildBunny(character, marker);
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogError($"[Fable Bunny] BuildBunny failed: {ex}");
        }
    }

    // ---- construction ----------------------------------------------------------------------

    private static void BuildBunny(Character source, AshlandsRebornFableBunny marker)
    {
        var sourceVisual = source.transform.Find("Visual") ?? source.transform;
        marker.SourceVisual = sourceVisual;

        // Target height is an ABSOLUTE config value (x star scale). The Morgen's live render
        // bounds are pose-inflated (recon measured 9.4m mid-animation vs a ~2.2m capsule),
        // so measuring it would produce a wildly oversized donor.
        var starScale = GetStarScale(source);
        var targetHeight = (Plugin.FableBunnyHeight?.Value ?? 4f) * starScale;

        // Hide ALL Morgen renderers (body + gore particles), recorded for revert.
        foreach (var r in source.GetComponentsInChildren<Renderer>(true))
        {
            marker.HiddenRenderers.Add(r);
            marker.HiddenRendererStates.Add(r.enabled);
            r.enabled = false;
        }

        // Keep the hidden Morgen animating - its animation events drive attack hitboxes/timing
        // (Valkyrie precedent: a renderer-less Animator gets culled and freezes).
        marker.SourceAnimator = source.GetComponentInChildren<Animator>();
        if (marker.SourceAnimator != null)
        {
            marker.OriginalCulling = marker.SourceAnimator.cullingMode;
            marker.SourceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        // Upright pivot at the Morgen's feet: proxies live under it; the per-frame stabilizer
        // writes a yaw-only world rotation so roll/tumble on the source never tilts the donor.
        var pivot = new GameObject(PivotName);
        pivot.transform.SetParent(source.transform, worldPositionStays: false);
        pivot.transform.localPosition = Vector3.zero;
        pivot.transform.localRotation = Quaternion.identity;
        marker.Pivot = pivot.transform;

        marker.TargetHeight = targetHeight;

        // Primary donor + optional Lox proxy for the hybrid bite/roll presentation.
        var primaryDonor = Plugin.FableBunnyDonor?.Value?.Trim();
        if (string.IsNullOrEmpty(primaryDonor)) primaryDonor = "Hare";
        BuildProxy(marker, primaryDonor!, isPrimary: true);
        if (IsHybridMode() && !string.Equals(primaryDonor, LoxPrefab, StringComparison.OrdinalIgnoreCase))
            BuildProxy(marker, LoxPrefab, isPrimary: false);

        if (marker.Proxies.Count == 0)
        {
            Plugin.Log?.LogError($"[Fable Bunny] No donor proxy could be built (donor='{primaryDonor}') - reverting.");
            marker.RevertAndDestroy();
            return;
        }

        ShowProxy(marker, 0);
        marker.Built = true;

        if (_swapLogCount++ < 5)
            Plugin.Log?.LogInfo(
                $"[Fable Bunny] Swapped Morgen -> {primaryDonor}: targetH={targetHeight:F2}, " +
                $"star={starScale:F2}, proxies={marker.Proxies.Count}, hybrid={IsHybridMode()}");
    }

    private static bool IsHybridMode()
        => string.Equals(Plugin.FableBunnyHybridMode?.Value, "LoxBiteRoll", StringComparison.OrdinalIgnoreCase);

    private static GameObject? EnsureDonorTemplate(string donorName)
    {
        if (DonorTemplates.TryGetValue(donorName, out var cached)) return cached;
        var prefab = ZNetScene.instance?.GetPrefab(donorName);
        if (prefab == null)
            Plugin.Log?.LogWarning($"[Fable Bunny] Donor prefab '{donorName}' not found in ZNetScene");
        DonorTemplates[donorName] = prefab;
        return prefab;
    }

    private static void BuildProxy(AshlandsRebornFableBunny marker, string donorName, bool isPrimary)
    {
        var prefab = EnsureDonorTemplate(donorName);
        if (prefab == null || marker.Pivot == null) return;

        // Inactive-holder instantiate (warrior pattern): no gameplay Awake runs, then strip
        // everything except the visuals and the Animator.
        var holder = new GameObject("AR_BunnyBuildHolder");
        holder.SetActive(false);
        var clone = UObject.Instantiate(prefab, holder.transform);
        clone.name = $"AR_FableBunny_{donorName}";

        StripDonor(clone);

        clone.transform.SetParent(marker.Pivot, worldPositionStays: false);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        clone.SetActive(true);
        UObject.Destroy(holder);

        var animator = clone.GetComponentInChildren<Animator>(true);
        var paramHashes = new HashSet<int>();
        if (animator != null)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = false; // event receivers were stripped; silence the warnings
            foreach (var p in animator.parameters)
                paramHashes.Add(p.nameHash);
        }
        else
        {
            Plugin.Log?.LogWarning($"[Fable Bunny] Donor '{donorName}' has no Animator - it will stand frozen");
        }

        foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;

        // A wild Lox never shows its saddle, but the prefab ships the mesh - keep it off.
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            if (r.name.IndexOf("sadel", StringComparison.OrdinalIgnoreCase) >= 0
                || r.name.IndexOf("saddle", StringComparison.OrdinalIgnoreCase) >= 0)
                r.enabled = false;

        // Raw donor height, measured in the first active frame (default pose, Animator not
        // yet stepped, localScale=1). PeriodicUpdate recomputes the scale from this so the
        // height/scale F1 sliders and late star-ups apply live.
        var rawHeight = UnionHeight(clone.GetComponentsInChildren<Renderer>(true)
            .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r.enabled));
        var autoScale = rawHeight > 0.01f ? marker.TargetHeight / rawHeight : 1f;
        var configScale = GetDonorConfigScale(donorName);
        clone.transform.localScale = Vector3.one * (autoScale * configScale);

        // Ground align: creature origins sit at the feet, but the donor's visual can hang
        // below/above its origin - align the scaled bounds' bottom to the Morgen root.
        var groundedBounds = UnionBounds(clone.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer));
        if (groundedBounds.HasValue)
        {
            var footError = marker.Source != null
                ? marker.Source.transform.position.y - groundedBounds.Value.min.y
                : 0f;
            clone.transform.localPosition += Vector3.up * (footError + (Plugin.FableBunnyYOffset?.Value ?? 0f));
        }

        marker.Proxies.Add(new BunnyProxy
        {
            DonorName = donorName,
            Root = clone,
            Animator = animator,
            ParamHashes = paramHashes,
            RawHeight = rawHeight,
            IsPrimary = isPrimary,
        });
        clone.SetActive(false); // ShowProxy decides visibility once all proxies exist
    }

    private static float GetDonorConfigScale(string donorName)
        => string.Equals(donorName, LoxPrefab, StringComparison.OrdinalIgnoreCase)
            ? Plugin.FableBunnyLoxScale?.Value ?? 1f
            : Plugin.FableBunnyScale?.Value ?? 1f;

    /// <summary>
    /// Strip the donor clone to visuals + Animator while inactive. Differs from the warrior's
    /// StripPuppet in exactly one way: the Animator stays ENABLED (the donor self-animates;
    /// the puppet's bones are driven manually). Everything gameplay-shaped goes: all
    /// MonoBehaviours (ZNetView/ZSyncAnimation/Character/AI/CharacterDrop/Tameable/...),
    /// colliders, rigidbodies, cloth.
    /// </summary>
    private static void StripDonor(GameObject clone)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            // Character last: half the creature scripts RequireComponent it, and destroying
            // it first spams "Can't remove Character..." Unity errors before the retry pass.
            var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(mb => mb != null)
                .OrderBy(mb => mb is Character ? 1 : 0)
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

        var survivors = clone.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(mb => mb != null)
            .Select(mb => mb.GetType().Name)
            .Distinct()
            .ToList();
        if (survivors.Count > 0)
            Plugin.Log?.LogWarning($"[Fable Bunny] Donor strip survivors: {string.Join(", ", survivors)}");

        // Cloth lives in an unreferenced Unity module - match it by type name.
        foreach (var comp in clone.GetComponentsInChildren<Component>(true))
            if (comp != null && comp.GetType().Name == "Cloth") TryDestroyImmediate(comp);
        foreach (var c in clone.GetComponentsInChildren<Collider>(true)) TryDestroyImmediate(c);
        foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) TryDestroyImmediate(rb);
    }

    private static void TryDestroyImmediate(UObject o)
    {
        try { UObject.DestroyImmediate(o); } catch { /* dependency or already gone */ }
    }

    private static void ShowProxy(AshlandsRebornFableBunny marker, int index)
    {
        for (var i = 0; i < marker.Proxies.Count; i++)
        {
            var p = marker.Proxies[i];
            if (p.Root != null && p.Root.activeSelf != (i == index))
                p.Root.SetActive(i == index);
        }
        marker.ActiveProxyIndex = index;
    }

    private static int FindProxyIndex(AshlandsRebornFableBunny marker, bool primary)
    {
        for (var i = 0; i < marker.Proxies.Count; i++)
            if (marker.Proxies[i].IsPrimary == primary)
                return i;
        return 0;
    }

    // ---- attack hook -----------------------------------------------------------------------

    private static void StartAttack_Postfix(Character __instance, bool __result)
    {
        if (!__result || !Plugin.IsFableBunnyActive || __instance == null) return;
        var marker = __instance.GetComponent<AshlandsRebornFableBunny>();
        if (marker == null || !marker.Built) return;

        try
        {
            var animName = GetCurrentAttackAnimation(__instance);
            var cls = ClassifyAttack(animName);
            marker.AttackStartTime = Time.time;
            if (_attackLogCount++ < 40)
                Plugin.Log?.LogInfo($"[AR Bunny] attack start: anim='{animName ?? "?"}' class={cls}");

            switch (cls)
            {
                case AttackClass.Roll:
                    marker.Rolling = true;
                    if (IsHybridMode() && marker.Proxies.Count > 1)
                        ShowProxy(marker, FindProxyIndex(marker, primary: false));
                    break;
                case AttackClass.Bite:
                    StartPounce(marker, duration: 0.5f, amplitude: 1.2f);
                    if (IsHybridMode() && marker.Proxies.Count > 1)
                    {
                        ShowProxy(marker, FindProxyIndex(marker, primary: false));
                        marker.HybridSwapped = true;
                        TriggerDonorAttack(marker);
                    }
                    break;
                case AttackClass.Smash:
                    StartPounce(marker, duration: 0.8f, amplitude: 1.5f);
                    break;
                default:
                    StartPounce(marker, duration: 0.45f, amplitude: 1.0f);
                    break;
            }
            if (cls == AttackClass.Roll) marker.HybridSwapped = IsHybridMode() && marker.Proxies.Count > 1;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[AR Bunny] attack hook error: {ex.Message}");
        }
    }

    private static string? GetCurrentAttackAnimation(Character character)
    {
        if (FCurrentAttack == null || FAttackAnimation == null) return null;
        var attack = FCurrentAttack.GetValue(character);
        return attack == null ? null : FAttackAnimation.GetValue(attack) as string;
    }

    internal static AttackClass ClassifyAttack(string? animName)
    {
        if (string.IsNullOrEmpty(animName)) return AttackClass.Unknown;
        var n = animName!.ToLowerInvariant();
        if (n.Contains("roll")) return AttackClass.Roll;
        if (n.Contains("bite") || n.Contains("eat") || n.Contains("maw")) return AttackClass.Bite;
        if (n.Contains("smash") || n.Contains("slam") || n.Contains("double")) return AttackClass.Smash;
        return AttackClass.Lash;
    }

    private static void StartPounce(AshlandsRebornFableBunny marker, float duration, float amplitude)
    {
        marker.PounceDuration = duration;
        marker.PounceEnd = Time.time + duration;
        marker.PounceAmplitude = amplitude * (Plugin.FableBunnyPounceAmplitude?.Value ?? 1f);
    }

    /// <summary>Fire the Lox proxy's native attack: its controller keeps vanilla trigger params
    /// (normally set via ZSyncAnimation). Config names the trigger; missing param is a no-op.</summary>
    private static void TriggerDonorAttack(AshlandsRebornFableBunny marker)
    {
        var idx = FindProxyIndex(marker, primary: false);
        if (idx >= marker.Proxies.Count) return;
        var proxy = marker.Proxies[idx];
        if (proxy.Animator == null) return;
        var trigger = Plugin.FableBunnyLoxAttackTrigger?.Value?.Trim();
        if (string.IsNullOrEmpty(trigger)) return;
        var hash = Animator.StringToHash(trigger);
        if (proxy.ParamHashes.Contains(hash))
            proxy.Animator.SetTrigger(hash);
    }

    // ---- ragdoll hook ------------------------------------------------------------------------

    private static void Ragdoll_Awake_Postfix(Ragdoll __instance)
    {
        if (!Plugin.IsFableBunnyActive || Plugin.FableBunnyHideRagdoll?.Value != true) return;
        var name = GetPrefabName(__instance.gameObject);
        if (name.IndexOf("morgen", StringComparison.OrdinalIgnoreCase) < 0) return;

        foreach (var r in __instance.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;
        if (!_ragdollLogDone)
        {
            _ragdollLogDone = true;
            Plugin.Log?.LogInfo($"[AR Bunny] Morgen ragdoll hidden: {name}");
        }
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

    private static void Drive(AshlandsRebornFableBunny marker)
    {
        var source = marker.Source;
        if (source == null || marker.Pivot == null) return;

        var dt = Time.deltaTime;

        // Roll/hybrid end detection: the attack is over once the Morgen leaves InAttack.
        // The grace period matters: InAttack() stays false for a few frames right after
        // StartAttack (until the animator enters the attack state), and without it the
        // hybrid Lox swapped in and instantly reverted before ever rendering.
        if ((marker.Rolling || marker.HybridSwapped)
            && Time.time - marker.AttackStartTime > 0.6f && !source.InAttack())
        {
            marker.Rolling = false;
            if (marker.HybridSwapped)
            {
                marker.HybridSwapped = false;
                ShowProxy(marker, FindProxyIndex(marker, primary: true));
            }
        }

        // Upright stabilizer: yaw-only world rotation for the pivot. During the roll the
        // source root/visual can tumble arbitrarily; fall back to the velocity direction,
        // then to the last known yaw.
        var fwd = source.transform.forward;
        var planarFwd = new Vector3(fwd.x, 0f, fwd.z);
        var vel = source.GetVelocity();
        var planarVel = new Vector3(vel.x, 0f, vel.z);
        if (planarFwd.sqrMagnitude > 0.01f)
            marker.LastYaw = Mathf.Atan2(planarFwd.x, planarFwd.z) * Mathf.Rad2Deg;
        else if (planarVel.sqrMagnitude > 0.25f)
            marker.LastYaw = Mathf.Atan2(planarVel.x, planarVel.z) * Mathf.Rad2Deg;

        // Pounce envelope: squash (first half) then stretch (second half), plus a forward
        // lunge pitch peaking mid-attack. Applied to the pivot so it composes with whatever
        // the donor's own Animator is doing.
        var pitch = 0f;
        var scaleY = 1f;
        if (marker.PounceEnd > Time.time && marker.PounceDuration > 0.01f)
        {
            var u = 1f - (marker.PounceEnd - Time.time) / marker.PounceDuration;
            var s = -Mathf.Sin(2f * Mathf.PI * u) * (1f - 0.4f * u);
            scaleY = 1f + marker.PounceAmplitude * 0.22f * s;
            pitch = marker.PounceAmplitude * 14f * Mathf.Sin(Mathf.PI * u);
        }
        var scaleXZ = 1f + 0.5f * (1f - scaleY); // rough volume preservation
        marker.Pivot.rotation = Quaternion.Euler(0f, marker.LastYaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
        marker.Pivot.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);

        // Locomotion sync on the active proxy: same params vanilla ZSyncAnimation writes.
        if (marker.ActiveProxyIndex < 0 || marker.ActiveProxyIndex >= marker.Proxies.Count) return;
        var proxy = marker.Proxies[marker.ActiveProxyIndex];
        var anim = proxy.Animator;
        if (anim == null || !anim.isActiveAndEnabled) return;

        var speed = planarVel.magnitude;
        if (marker.Rolling) speed = Mathf.Max(speed, 5f);
        if (proxy.ParamHashes.Contains(ForwardSpeedHash))
            anim.SetFloat(ForwardSpeedHash, speed, 0.2f, dt);
        if (proxy.ParamHashes.Contains(TurnSpeedHash) && dt > 0.0001f)
        {
            var turn = Mathf.DeltaAngle(marker.PrevYaw, marker.LastYaw) * Mathf.Deg2Rad / dt;
            anim.SetFloat(TurnSpeedHash, turn, 0.2f, dt);
        }
        if (proxy.ParamHashes.Contains(OnGroundHash))
            anim.SetBool(OnGroundHash, true);
        marker.PrevYaw = marker.LastYaw;
    }

    // ---- lifecycle -----------------------------------------------------------------------

    internal static void RefreshAll()
    {
        RevertAll();
        if (!Plugin.IsFableBunnyActive) return;
        Plugin.Instance.StartCoroutine(RebuildAfterRevert());
    }

    /// <summary>One-frame wait after RevertAll: marker Destroy is deferred, and a same-frame
    /// rescan still sees the stale markers and skips every Morgen (warrior F10 gotcha).</summary>
    private static IEnumerator RebuildAfterRevert()
    {
        yield return null;
        if (!Plugin.IsFableBunnyActive) yield break;

        foreach (var character in UObject.FindObjectsByType<Character>(FindObjectsSortMode.None))
        {
            if (character == null) continue;
            TryBuild(character);
        }
    }

    internal static void RevertAll()
    {
        foreach (var m in UObject.FindObjectsByType<AshlandsRebornFableBunny>(FindObjectsSortMode.None))
            m.RevertAndDestroy();
    }

    private static float _periodicTimer;

    /// <summary>
    /// Called from Plugin's 0.2s block; runs every ~2s. Re-applies proxy scale when the star
    /// level or the scale configs changed (makes the F1 sliders live), and re-hides any Morgen
    /// renderers a late initializer re-enabled.
    /// </summary>
    internal static void PeriodicUpdate()
    {
        if (!Plugin.IsFableBunnyActive) return;
        _periodicTimer += 0.2f;
        if (_periodicTimer < 2f) return;
        _periodicTimer = 0f;

        for (var i = 0; i < Registry.Count; i++)
        {
            var m = Registry[i];
            if (m == null || !m.Built || m.Source == null) continue;

            for (var r = 0; r < m.HiddenRenderers.Count; r++)
                if (m.HiddenRenderers[r] != null && m.HiddenRenderers[r].enabled)
                    m.HiddenRenderers[r].enabled = false;

            var star = GetStarScale(m.Source);
            foreach (var p in m.Proxies)
            {
                if (p.Root == null || p.RawHeight <= 0.01f) continue;
                var desired = (Plugin.FableBunnyHeight?.Value ?? 4f) * star / p.RawHeight
                              * GetDonorConfigScale(p.DonorName);
                var current = p.Root.transform.localScale.y;
                if (current > 1e-5f && Mathf.Abs(desired - current) / current > 0.02f)
                    p.Root.transform.localScale = Vector3.one * desired;
            }
        }
    }

    // ---- helpers -----------------------------------------------------------------------

    private static float GetStarScale(Character character)
    {
        var level = character.GetLevel();
        if (level <= 1) return 1f;
        var fx = character.GetComponentInChildren<LevelEffects>(true);
        if (fx == null || fx.m_levelSetups == null || fx.m_levelSetups.Count < level - 1) return 1f;
        return Mathf.Max(0.01f, fx.m_levelSetups[level - 2].m_scale);
    }

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

    // =====================================================================================
    // Recon (dev-only, FableBunnyReconDump): dumps Morgen/Hare/Lox rig facts to the log so
    // classification tables, scales, and animator params can be corrected from evidence.
    // =====================================================================================

    private static readonly string[] ReconTargets = { MorgenPrefab, "Hare", LoxPrefab };
    private static readonly HashSet<string> ReconDumped = new(StringComparer.OrdinalIgnoreCase);
    private static float _reconNextScan;
    private static int _morgenSamples;
    private static bool _reconMiscDone;
    private static float _reconWorldLoadedAt = -1f;
    private static bool _obsFired;

    /// <summary>Called from Plugin.Update() every frame (DevAutoLoadPatches pattern).</summary>
    internal static void ReconTick()
    {
        if (Plugin.FableBunnyReconDump?.Value != true) return;
        if (Player.m_localPlayer == null || ZNetScene.instance == null)
        {
            _reconWorldLoadedAt = -1f;
            return;
        }
        if (_reconWorldLoadedAt < 0f) _reconWorldLoadedAt = Time.time;
        if (Time.time < _reconNextScan) return;
        _reconNextScan = Time.time + 0.5f;

        try
        {
            if (!_reconMiscDone)
            {
                _reconMiscDone = true;
                var trophy = ZNetScene.instance.GetPrefab("TrophyHare");
                Plugin.Log?.LogInfo($"[AR BunnyRecon] TrophyHare prefab: {(trophy != null ? "FOUND" : "not found")}");
            }

            if (!_obsFired && Time.time - _reconWorldLoadedAt > 90f)
            {
                _obsFired = true;
                Plugin.Instance.StartCoroutine(ObservationRoutine());
            }

            foreach (var c in UObject.FindObjectsByType<Character>(FindObjectsSortMode.None))
            {
                if (c == null) continue;
                var prefabName = GetPrefabName(c.gameObject);
                if (!ReconTargets.Contains(prefabName, StringComparer.OrdinalIgnoreCase)) continue;

                if (!ReconDumped.Contains(prefabName))
                {
                    ReconDumped.Add(prefabName);
                    DumpCreature(c, prefabName);
                }

                if (string.Equals(prefabName, MorgenPrefab, StringComparison.OrdinalIgnoreCase) && _morgenSamples < 240)
                {
                    _morgenSamples++;
                    SampleMorgen(c);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[AR BunnyRecon] tick error: {ex.Message}");
        }
    }

    /// <summary>
    /// Recon phase 2, fired once ~90s after world load (after the PhotoMode shoot): a full
    /// autonomous verification pass. The AI alerts-but-never-attacks on the player-built test
    /// platform (big-monster pathfinding), so real attacks are forced through the production
    /// pipeline - equip each attack item and call StartAttack, exactly what MonsterAI.DoAttack
    /// does - while the hijacked camera captures each one. Then a real death (no bone corpse
    /// check) and a MasterSwitch/RefreshAll lifecycle assert pass. Results: [AR BunnyRecon]
    /// PASS/FAIL lines + "OBSERVATION DONE pass=X fail=Y", PNGs in AR_PhotoMode.
    /// </summary>
    private static IEnumerator ObservationRoutine()
    {
        var pass = 0;
        var fail = 0;
        void Check(bool ok, string what)
        {
            if (ok) { pass++; Plugin.Log?.LogInfo($"[AR BunnyRecon] PASS {what}"); }
            else { fail++; Plugin.Log?.LogWarning($"[AR BunnyRecon] FAIL {what}"); }
        }

        var player = Player.m_localPlayer;
        var prefab = ZNetScene.instance?.GetPrefab(MorgenPrefab);
        if (player == null || prefab == null) yield break;
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "AR_PhotoMode");
        System.IO.Directory.CreateDirectory(dir);

        var pos = player.transform.position + player.transform.forward * 4f + Vector3.up * 0.3f;
        var go = UObject.Instantiate(prefab, pos, Quaternion.identity);
        Plugin.Log?.LogInfo("[AR BunnyRecon] observation Morgen spawned");
        yield return new WaitForSeconds(4f); // swap build + rise settle

        var hum = go != null ? go.GetComponent<Humanoid>() : null;
        var marker = go != null ? go.GetComponent<AshlandsRebornFableBunny>() : null;
        Check(marker != null && marker.Built, "swap built on observation Morgen");

        PhotoModePatches.EnableCameraOverride();

        // Forced attacks, one per attack item, two screenshots each (early + late pose).
        var items = hum != null ? hum.GetInventory()?.GetAllItems() : null;
        if (hum != null && items != null)
        {
            for (var i = 0; i < items.Count && go != null; i++)
            {
                var item = items[i];
                hum.EquipItem(item);
                var anim = item.m_shared.m_attack != null
                    ? FAttackAnimation?.GetValue(item.m_shared.m_attack) as string ?? "unknown"
                    : "unknown";
                var ok = hum.StartAttack(player, false);
                Plugin.Log?.LogInfo($"[AR BunnyRecon] forced attack '{anim}' -> {ok}");
                if (!ok)
                {
                    yield return new WaitForSeconds(2f);
                    continue;
                }
                yield return new WaitForSeconds(0.35f);
                if (go == null) break;
                PhotoModePatches.AimCameraAt(go, 45);
                yield return new WaitForSeconds(0.15f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_atk_{anim}_a.png"));
                yield return new WaitForSeconds(0.6f);
                if (go == null) break;
                PhotoModePatches.AimCameraAt(go, 45);
                yield return new WaitForSeconds(0.1f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_atk_{anim}_b.png"));
                var tEnd = Time.time + 6f;
                while (go != null && hum != null && hum.InAttack() && Time.time < tEnd) yield return null;
                yield return new WaitForSeconds(1f);
            }
        }

        // Death: a real kill - vanilla fx must play with no bone corpse left behind.
        if (go != null)
        {
            var ch = go.GetComponent<Character>();
            PhotoModePatches.AimCameraAt(go, 90);
            ch.SetHealth(0f);
            yield return new WaitForSeconds(1.5f);
            Check(ch == null || ch.IsDead(), "death: Morgen died cleanly");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "Morgen_death_aftermath.png"));
            yield return new WaitForSeconds(1f);
        }

        // Lifecycle: fresh spawn, MasterSwitch OFF -> vanilla Morgen back, ON -> rebuilt,
        // RefreshAll (the F10 path) -> rebuilt again.
        var go2 = UObject.Instantiate(prefab, pos, Quaternion.identity);
        yield return new WaitForSeconds(4f);
        var m2 = go2 != null ? go2.GetComponent<AshlandsRebornFableBunny>() : null;
        Check(m2 != null && m2.Built, "lifecycle: swap built on fresh Morgen");
        var srcRenderer = m2 != null && m2.HiddenRenderers.Count > 0 ? m2.HiddenRenderers[0] : null;

        Plugin.Instance.ApplyMasterSwitch(false);
        yield return new WaitForSeconds(1f);
        Check(go2 != null && go2.GetComponent<AshlandsRebornFableBunny>() == null, "lifecycle: marker gone after master OFF");
        Check(srcRenderer != null && srcRenderer.enabled, "lifecycle: Morgen renderers restored after master OFF");
        Check(go2 != null && go2.transform.Find(PivotName) == null, "lifecycle: pivot destroyed after master OFF");
        if (go2 != null)
        {
            PhotoModePatches.AimCameraAt(go2, 90);
            yield return new WaitForSeconds(0.2f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "Morgen_master_off.png"));
        }

        Plugin.Instance.ApplyMasterSwitch(true);
        yield return new WaitForSeconds(2f);
        var m3 = go2 != null ? go2.GetComponent<AshlandsRebornFableBunny>() : null;
        Check(m3 != null && m3.Built, "lifecycle: rebuilt after master ON");
        Check(srcRenderer != null && !srcRenderer.enabled, "lifecycle: Morgen renderers hidden after master ON");
        if (go2 != null)
        {
            PhotoModePatches.AimCameraAt(go2, 90);
            yield return new WaitForSeconds(0.2f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "Morgen_master_on.png"));
        }

        RefreshAll();
        yield return new WaitForSeconds(2f);
        var m4 = go2 != null ? go2.GetComponent<AshlandsRebornFableBunny>() : null;
        Check(m4 != null && m4.Built, "lifecycle: rebuilt after RefreshAll (F10 path)");
        if (go2 != null) ZNetScene.instance?.Destroy(go2);

        PhotoModePatches.ClearCameraOverride();
        Plugin.Log?.LogInfo($"[AR BunnyRecon] OBSERVATION DONE pass={pass} fail={fail}");
    }

    private static void SampleMorgen(Character c)
    {
        var anim = c.GetComponentInChildren<Animator>();
        var state = anim != null ? anim.GetCurrentAnimatorStateInfo(0) : default;
        var clips = anim != null ? anim.GetCurrentAnimatorClipInfo(0) : Array.Empty<AnimatorClipInfo>();
        var clipName = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "?";
        var visual = c.transform.Find("Visual");
        var vel = c.GetVelocity();
        var attackAnim = c.InAttack() ? GetCurrentAttackAnimation(c) : null;
        var ai = c.GetComponent<BaseAI>();
        Plugin.Log?.LogInfo(
            $"[AR BunnyRecon] Morgen sample t={Time.time:F1} clip={clipName} nt={state.normalizedTime:F2} " +
            $"inAttack={c.InAttack()} attackAnim={attackAnim ?? "-"} alerted={(ai != null && ai.IsAlerted())} " +
            $"vel={new Vector3(vel.x, 0, vel.z).magnitude:F2} " +
            $"rootEuler={c.transform.eulerAngles:F0} visualEuler={(visual != null ? visual.eulerAngles : Vector3.zero):F0}");
    }

    private static void DumpCreature(Character c, string prefabName)
    {
        var sb = new StringBuilder(8192);
        sb.AppendLine($"=== [AR BunnyRecon] DUMP {prefabName} ===");
        sb.AppendLine($"type={c.GetType().FullName} isHumanoid={c is Humanoid}");
        sb.AppendLine($"speeds: walk={c.m_walkSpeed:F2} run={c.m_runSpeed:F2} speed={c.m_speed:F2}");
        sb.AppendLine($"lossyScale={c.transform.lossyScale:F3} level={c.GetLevel()}");

        var capsule = c.GetComponent<CapsuleCollider>();
        if (capsule != null)
            sb.AppendLine($"capsule: height={capsule.height:F2} radius={capsule.radius:F2} center={capsule.center:F2}");
        var bodyBounds = UnionBounds(c.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer));
        if (bodyBounds.HasValue)
            sb.AppendLine($"bodyBounds: size={bodyBounds.Value.size:F2} min.y={bodyBounds.Value.min.y:F2} rootY={c.transform.position.y:F2}");

        sb.AppendLine("--- hierarchy (name [components]) ---");
        DumpHierarchy(c.transform, sb, 0);

        var anim = c.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            sb.AppendLine($"--- animator @ {GetPath(anim.transform, c.transform)} speed={anim.speed} rootMotion={anim.applyRootMotion} ---");
            foreach (var p in anim.parameters)
                sb.AppendLine($"  param {p.name} : {p.type} (default f={p.defaultFloat} b={p.defaultBool})");
            var rac = anim.runtimeAnimatorController;
            if (rac != null)
            {
                sb.AppendLine($"  controller={rac.name}");
                foreach (var clip in rac.animationClips.Where(cl => cl != null).Select(cl => cl.name).Distinct().OrderBy(n => n))
                    sb.AppendLine($"  clip {clip}");
            }
        }
        else sb.AppendLine("--- NO ANIMATOR ---");

        if (c is Humanoid humanoid && humanoid.m_defaultItems != null)
        {
            sb.AppendLine("--- default items (attacks) ---");
            foreach (var item in humanoid.m_defaultItems)
            {
                if (item == null) continue;
                var drop = item.GetComponent<ItemDrop>();
                var shared = drop != null ? drop.m_itemData?.m_shared : null;
                if (shared == null) { sb.AppendLine($"  {item.name}: no ItemDrop"); continue; }
                var prim = shared.m_attack != null ? FAttackAnimation?.GetValue(shared.m_attack) as string : null;
                var sec = shared.m_secondaryAttack != null ? FAttackAnimation?.GetValue(shared.m_secondaryAttack) as string : null;
                sb.AppendLine($"  {item.name}: name='{shared.m_name}' attackAnim='{prim ?? "-"}' secondaryAnim='{sec ?? "-"}'");
            }
        }

        if (c.m_deathEffects?.m_effectPrefabs != null)
        {
            sb.AppendLine("--- death effects ---");
            foreach (var e in c.m_deathEffects.m_effectPrefabs)
                if (e?.m_prefab != null) sb.AppendLine($"  {e.m_prefab.name}");
        }

        if (string.Equals(prefabName, "Hare", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("--- hare meshes (chimera recon) ---");
            foreach (var smr in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh != null)
                    sb.AppendLine($"  SMR {smr.name}: mesh={mesh.name} subMeshes={mesh.subMeshCount} verts={mesh.vertexCount} isReadable={mesh.isReadable}");
            }
        }

        sb.AppendLine($"=== [AR BunnyRecon] END {prefabName} ===");
        Plugin.Log?.LogInfo(sb.ToString());
    }

    private static void DumpHierarchy(Transform t, StringBuilder sb, int depth)
    {
        var comps = t.GetComponents<Component>()
            .Where(x => x != null && !(x is Transform))
            .Select(x => x.GetType().Name)
            .ToList();
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(comps.Count > 0 ? " [" + string.Join(",", comps) + "]" : "")}");
        for (var i = 0; i < t.childCount; i++)
            DumpHierarchy(t.GetChild(i), sb, depth + 1);
    }

    private static string GetPath(Transform t, Transform root)
    {
        var parts = new List<string>();
        var cur = t;
        while (cur != null && cur != root) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    internal sealed class BunnyProxy
    {
        public string DonorName = "";
        public GameObject? Root;
        public Animator? Animator;
        public HashSet<int> ParamHashes = new();
        public float RawHeight = 1f;
        public bool IsPrimary;
    }
}

/// <summary>Per-Morgen state for the Fable Bunny swap. Lives on the Morgen root.</summary>
internal class AshlandsRebornFableBunny : MonoBehaviour
{
    public Character? Source;
    public Transform? SourceVisual;
    public Transform? Pivot;
    public Animator? SourceAnimator;
    public AnimatorCullingMode OriginalCulling;
    public readonly List<Renderer> HiddenRenderers = new();
    public readonly List<bool> HiddenRendererStates = new();
    public readonly List<FableBunnyPatches.BunnyProxy> Proxies = new();
    public int ActiveProxyIndex = -1;
    public float TargetHeight;
    public float LastYaw;
    public float PrevYaw;
    public float PounceEnd;
    public float PounceDuration;
    public float PounceAmplitude;
    public float AttackStartTime;
    public bool Rolling;
    public bool HybridSwapped;
    public bool Built;

    private void OnEnable() => FableBunnyPatches.Register(this);
    private void OnDisable() => FableBunnyPatches.Unregister(this);
    private void OnDestroy() => FableBunnyPatches.Unregister(this);

    public void RevertAndDestroy()
    {
        try
        {
            if (Pivot != null) UnityEngine.Object.Destroy(Pivot.gameObject);

            for (var i = 0; i < HiddenRenderers.Count; i++)
            {
                var r = HiddenRenderers[i];
                if (r != null) r.enabled = i < HiddenRendererStates.Count ? HiddenRendererStates[i] : true;
            }

            if (SourceAnimator != null)
                SourceAnimator.cullingMode = OriginalCulling;
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[Fable Bunny] Revert warning: {ex.Message}");
        }
        finally
        {
            UnityEngine.Object.Destroy(this);
        }
    }
}
