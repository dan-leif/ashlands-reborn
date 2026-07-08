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
///  - Roll: the pivot's yaw-only stabilizer cancels any tumble; while rolling the donor faces
///    the TRAVEL direction (the Morgen often rolls sideways relative to its facing), and the
///    "HopHigher" presentation fires the donor's real jump trigger on repeating airborne
///    bounce arcs with a landing squash - destructive bounding, not the v1 jog.
///  - Swipes: the Morgen's arm swings have no visible correlate on the donor, so two wisp
///    orbs orbit it and, on swipe attacks, flare outward and lash along the HIDDEN Morgen
///    hand bones (Hand.l/Hand.r keep animating hitbox-accurately) with trails.
///  - Star look: FableBunnyStarLook applies the donor's own LevelEffects tint table (hue/
///    saturation/value/emissive) at build time, independent of the Morgen's real level.
///  - Death: Ragdoll.Awake hides the Morgen ragdoll's renderers (vanilla FX/drops untouched).
///
/// v2 note: v1's hybrid Lox mode (swap a Lox in for bite/roll) was DROPPED after user review -
/// the swap read as janky and a giant lox is an established creature, too plain. The proxy
/// list stays donor-generic but only a single proxy is built now.
///
/// All spawn/attack/ragdoll hooks are applied manually with null-guards (ApplyBunnyPatches)
/// so a game-update rename degrades to a logged warning instead of breaking PatchAll.
/// </summary>
[HarmonyPatch]
internal static class FableBunnyPatches
{
    private const string MorgenPrefab = "Morgen";
    private const string PivotName = "AR_FableBunnyPivot";
    private const float RollHopPeriod = 0.8f;

    private static readonly List<AshlandsRebornFableBunny> Registry = new();
    private static readonly Dictionary<string, GameObject?> DonorTemplates = new(StringComparer.OrdinalIgnoreCase);
    private static int _swapLogCount;
    private static int _attackLogCount;
    private static bool _ragdollLogDone;

    private static readonly int ForwardSpeedHash = Animator.StringToHash("forward_speed");
    private static readonly int TurnSpeedHash = Animator.StringToHash("turn_speed");
    private static readonly int OnGroundHash = Animator.StringToHash("onGround");
    private static readonly int JumpHash = Animator.StringToHash("jump");

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

        var effectCreates = typeof(EffectList)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "Create")
            .ToList();
        Plugin.Log?.LogInfo($"[Fable Bunny] EffectList.Create overloads: {effectCreates.Count} " +
            $"[{string.Join(" | ", effectCreates.Select(m => string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))))}]");
        foreach (var create in effectCreates)
            TryPatch(harmony, applied, $"EffectList.Create({create.GetParameters().Length})", create,
                postfix: AccessTools.Method(typeof(FableBunnyPatches), nameof(EffectList_Create_Postfix)));

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

        // Hide ALL Morgen renderers (body + gore particles), recorded for revert. Two layers
        // of defence, because the Morgen's wake-up "rise" clip re-shows its body every frame
        // through the kept-alive Animator (plain .enabled disables lost that fight):
        //  1. forceRenderingOff - not clip-animatable;
        //  2. materials swapped for an invisible one - nothing can render bone horror with
        //     no visible material even if a rendering flag gets overridden somewhere.
        foreach (var r in source.GetComponentsInChildren<Renderer>(true))
        {
            marker.HiddenRenderers.Add(r);
            marker.HiddenMaterials.Add(r.sharedMaterials);
            r.forceRenderingOff = true;
            r.sharedMaterials = InvisibleMaterials(r.sharedMaterials.Length);
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

        // Single donor proxy (v2: the hybrid Lox mode was dropped after user review).
        var primaryDonor = Plugin.FableBunnyDonor?.Value?.Trim();
        if (string.IsNullOrEmpty(primaryDonor)) primaryDonor = "Hare";
        BuildProxy(marker, primaryDonor!, isPrimary: true);

        if (marker.Proxies.Count == 0)
        {
            Plugin.Log?.LogError($"[Fable Bunny] No donor proxy could be built (donor='{primaryDonor}') - reverting.");
            marker.RevertAndDestroy();
            return;
        }

        ShowProxy(marker, 0);

        // The hidden Morgen hand bones keep animating hitbox-accurately (AlwaysAnimate) -
        // they anchor the wisp-lash tracking during swipe attacks.
        foreach (var t in source.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Hand.l") marker.HandL = t;
            else if (t.name == "Hand.r") marker.HandR = t;
        }
        if (marker.HandL == null || marker.HandR == null)
            Plugin.Log?.LogWarning("[AR Bunny] Morgen hand bones not found - wisp lash degrades to orbit-only");

        if (LashIncludesWisps()) BuildWispOrbs(marker);

        marker.Built = true;

        if (_swapLogCount++ < 5)
            Plugin.Log?.LogInfo(
                $"[Fable Bunny] Swapped Morgen -> {primaryDonor}: targetH={targetHeight:F2}, " +
                $"star={starScale:F2}, orbs={marker.Orbs.Count}, starLook={Plugin.FableBunnyStarLook?.Value ?? 0}");
    }

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
        var configScale = Plugin.FableBunnyScale?.Value ?? 1f;
        clone.transform.localScale = Vector3.one * (autoScale * configScale);

        ApplyStarLook(prefab, clone, donorName);

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

    private static int _starLookLogCount;

    /// <summary>
    /// Apply the donor's own star tint (LevelEffects table) to the freshly built clone,
    /// driven by FableBunnyStarLook instead of the Morgen's real level. Setup data is read
    /// from the PREFAB (the clone's copy is destroyed by StripDonor), and applied manually
    /// rather than via LevelEffects.SetupLevelVisualization: the vanilla method also sets
    /// localScale (which would fight the height-derived scale - star look must stay purely
    /// visual) and its Start() hooks m_onLevelSet on whatever Character it finds above it.
    /// </summary>
    private static void ApplyStarLook(GameObject prefab, GameObject clone, string donorName)
    {
        var look = Plugin.FableBunnyStarLook?.Value ?? 0;
        if (look <= 0) return;

        var fx = prefab.GetComponentInChildren<LevelEffects>(true);
        if (fx == null || fx.m_levelSetups == null || fx.m_levelSetups.Count < look)
        {
            Plugin.Log?.LogWarning($"[AR Bunny] StarLook {look}: donor '{donorName}' has no level setup for it");
            return;
        }

        var setup = fx.m_levelSetups[look - 1];
        if (_starLookLogCount++ < 6)
            Plugin.Log?.LogInfo(
                $"[AR Bunny] StarLook {look} on {donorName}: hue={setup.m_hue:F2} sat={setup.m_saturation:F2} " +
                $"val={setup.m_value:F2} emissive={(setup.m_setEmissiveColor ? setup.m_emissiveColor.ToString() : "no")} " +
                $"enableObj={(setup.m_enableObject != null ? setup.m_enableObject.name : "-")} scale(ignored)={setup.m_scale:F2}");

        foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var mat in smr.materials) // instantiates per-renderer copies, like vanilla
            {
                if (mat == null) continue;
                if (mat.HasProperty("_Hue")) mat.SetFloat("_Hue", setup.m_hue);
                if (mat.HasProperty("_Saturation")) mat.SetFloat("_Saturation", setup.m_saturation);
                if (mat.HasProperty("_Value")) mat.SetFloat("_Value", setup.m_value);
                if (setup.m_setEmissiveColor && mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", setup.m_emissiveColor);
            }
        }

        // m_enableObject references the PREFAB's child - translate by path onto the clone.
        if (setup.m_enableObject != null)
        {
            var path = GetPath(setup.m_enableObject.transform, prefab.transform);
            var target = clone.transform.Find(path);
            if (target != null) target.gameObject.SetActive(true);
            else Plugin.Log?.LogWarning($"[AR Bunny] StarLook enable-object path '{path}' not found on clone");
        }
    }

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
                    marker.RollStartTime = Time.time;
                    marker.NextRollJumpTime = Time.time; // first hop fires immediately in Drive
                    break;
                case AttackClass.Bite:
                    StartPounce(marker, duration: 0.5f, amplitude: 1.2f); // signed off - don't touch
                    break;
                case AttackClass.Smash:
                    StartPounce(marker, duration: 0.8f, amplitude: 1.5f);
                    break;
                case AttackClass.Lash when marker.Orbs.Count > 0:
                    // The wisp orbs carry the read; tone the body pounce down so the lash is
                    // the visible event instead of an unrelated body bob.
                    StartPounce(marker, duration: 0.45f, amplitude: 0.4f);
                    StartLash(marker);
                    break;
                default:
                    StartPounce(marker, duration: 0.45f, amplitude: 1.0f);
                    break;
            }
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

    private static void StartLash(AshlandsRebornFableBunny marker)
    {
        marker.LashState = LashState.Flare;
        marker.LashPhaseStart = Time.time;
    }

    // ---- effect suppression ------------------------------------------------------------------
    // The Morgen's spawn "rise", death burst, and attack gore are SEPARATE spawned effect
    // objects (bone-horror visuals), not children of the character - the renderer hide pass
    // never sees them. The v2.0 shoot caught the rise vfx playing a full bone Morgen for
    // ~14s over the (still underground) donor. While the bunny is active, any effect
    // instance with a morgen-ish name gets its renderers hidden; sounds and timing intact.

    private static int _fxSuppressLogCount;
    private static int _fxNearLogCount;

    private static void EffectList_Create_Postfix(GameObject[] __result)
    {
        if (__result == null || !Plugin.IsFableBunnyActive) return;
        try
        {
            foreach (var fx in __result)
            {
                if (fx == null) continue;
                var name = GetPrefabName(fx);
                var nearMorgen = IsNearSwappedMorgen(fx.transform.position, 8f);
                if (nearMorgen && Plugin.FableBunnyReconDump?.Value == true && _fxNearLogCount++ < 30)
                    Plugin.Log?.LogInfo($"[AR Bunny] fx near swapped Morgen: '{name}'");
                var isMorgenFx = name.IndexOf("morgen", StringComparison.OrdinalIgnoreCase) >= 0;
                // The Morgen's wakeup "rise" is fx_Abomination_arise_end (recon-verified via
                // the EffectList inventory) - a full bone-Morgen emergence. The name is shared
                // with the Swamp Abomination, so only suppress instances spawning ON a swapped
                // Morgen (proximity check against the registry).
                if (!isMorgenFx && name.StartsWith("fx_Abomination_arise", StringComparison.OrdinalIgnoreCase))
                    isMorgenFx = nearMorgen;
                if (!isMorgenFx) continue;
                var hid = 0;
                foreach (var r in fx.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    r.forceRenderingOff = true; // animation-proof, same as the body hide
                    hid++;
                }
                if (hid > 0 && _fxSuppressLogCount++ < 12)
                    Plugin.Log?.LogInfo($"[AR Bunny] suppressed morgen fx visual '{fx.name}' ({hid} renderers)");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[AR Bunny] fx suppression error: {ex.Message}");
        }
    }

    private static Material? _invisibleMat;

    /// <summary>A fully transparent material array (ZWrite off, alpha 0) used to blank out
    /// hidden Morgen renderers - immune to whatever re-enables the renderer itself.</summary>
    internal static Material[] InvisibleMaterials(int count)
    {
        if (_invisibleMat == null)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Additive");
            _invisibleMat = shader != null ? new Material(shader) { color = Color.clear } : new Material(Shader.Find("Standard"));
            _invisibleMat.name = "AR_Invisible";
        }
        var arr = new Material[count];
        for (var i = 0; i < count; i++) arr[i] = _invisibleMat;
        return arr;
    }

    private static bool IsNearSwappedMorgen(Vector3 pos, float radius)
    {
        for (var i = 0; i < Registry.Count; i++)
        {
            var m = Registry[i];
            if (m != null && m.Built && m.Source != null
                && (m.Source.transform.position - pos).sqrMagnitude < radius * radius)
                return true;
        }
        return false;
    }

    // ---- ragdoll hook ------------------------------------------------------------------------

    private static void Ragdoll_Awake_Postfix(Ragdoll __instance)
    {
        if (!Plugin.IsFableBunnyActive || Plugin.FableBunnyHideRagdoll?.Value != true) return;
        var name = GetPrefabName(__instance.gameObject);
        if (name.IndexOf("morgen", StringComparison.OrdinalIgnoreCase) < 0) return;

        foreach (var r in __instance.GetComponentsInChildren<Renderer>(true))
            r.forceRenderingOff = true;
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
        var proxy = marker.ActiveProxyIndex >= 0 && marker.ActiveProxyIndex < marker.Proxies.Count
            ? marker.Proxies[marker.ActiveProxyIndex]
            : null;
        var anim = proxy?.Animator;

        // Roll end detection: the attack is over once the Morgen leaves InAttack. The grace
        // period matters: InAttack() stays false for a few frames right after StartAttack
        // (until the animator enters the attack state), and without it the roll state ends
        // before it visibly starts.
        if (marker.Rolling && Time.time - marker.AttackStartTime > 0.6f && !source.InAttack())
            marker.Rolling = false;

        // Attacks can instantiate NEW renderers under the Morgen (equipping an attack item
        // attaches its visual) that the build-time hide pass never saw - the v2.0 shoot
        // caught vanilla Morgen visuals popping in mid-attack. Rescan every frame while an
        // attack is live (few renderers, few Morgens); PeriodicUpdate covers the idle case.
        if (source.InAttack() || Time.time - marker.AttackStartTime < 1.5f)
            HideNewSourceRenderers(marker);

        // Upright stabilizer: yaw-only world rotation for the pivot. While ROLLING the donor
        // faces the travel direction (smoothed - raw velocity is jittery): the Morgen often
        // rolls sideways relative to its facing, and v1's face-the-source rule left the bunny
        // staring at the player mid-roll. Otherwise face the source's forward, falling back
        // to the velocity direction, then the last known yaw.
        var fwd = source.transform.forward;
        var planarFwd = new Vector3(fwd.x, 0f, fwd.z);
        var vel = source.GetVelocity();
        var planarVel = new Vector3(vel.x, 0f, vel.z);
        if (marker.Rolling && planarVel.sqrMagnitude > 1f)
        {
            var velYaw = Mathf.Atan2(planarVel.x, planarVel.z) * Mathf.Rad2Deg;
            marker.LastYaw = Mathf.LerpAngle(marker.LastYaw, velYaw, 1f - Mathf.Exp(-8f * dt));
        }
        else if (planarFwd.sqrMagnitude > 0.01f)
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

        // Roll presentation "HopHigher": repeating airborne bounce arcs with a landing squash,
        // plus the donor's real jump trigger on each arc - destructive bounding instead of
        // the v1 flat jog.
        var bounceY = 0f;
        if (marker.Rolling && IsHopHigherRoll())
        {
            var hop = Mathf.Abs(Mathf.Sin(Mathf.PI * (Time.time - marker.RollStartTime) / RollHopPeriod));
            bounceY = 0.35f * marker.TargetHeight * hop;
            scaleY *= 0.86f + 0.14f * Mathf.Clamp01(hop * 4f); // squash on each landing
            if (Time.time >= marker.NextRollJumpTime)
            {
                marker.NextRollJumpTime = Time.time + RollHopPeriod;
                if (anim != null && proxy!.ParamHashes.Contains(JumpHash))
                    anim.SetTrigger(JumpHash);
            }
        }

        var scaleXZ = 1f + 0.5f * (1f - scaleY); // rough volume preservation
        marker.Pivot.rotation = Quaternion.Euler(0f, marker.LastYaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
        marker.Pivot.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);
        marker.Pivot.localPosition = Vector3.up * bounceY;

        UpdateOrbs(marker, source, dt);

        if (anim == null || !anim.isActiveAndEnabled) return;

        // Locomotion sync on the active proxy: same params vanilla ZSyncAnimation writes.
        // Move-anim speed sync fixes the v1 "moonwalking": at ~3x scale the authored run
        // cycle paddles faster than the ground covered, so the animation slows toward the
        // configured factor as locomotion speed rises. Idle fidgets stay full-rate.
        var speed = planarVel.magnitude;
        if (marker.Rolling) speed = Mathf.Max(speed, 5f);
        anim.speed = Mathf.Lerp(1f, Plugin.FableBunnyMoveAnimSpeed?.Value ?? 0.55f, Mathf.Clamp01(speed / 4f));
        if (proxy!.ParamHashes.Contains(ForwardSpeedHash))
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

    private static bool IsHopHigherRoll()
        => string.Equals(Plugin.FableBunnyRollStyle?.Value, "HopHigher", StringComparison.OrdinalIgnoreCase);

    private static int _lateHideLogCount;

    /// <summary>Hide any enabled renderer under the Morgen that is not ours (pivot subtree).
    /// Catches visuals instantiated AFTER the build-time hide pass - attack-item attach
    /// visuals and similar late arrivals - and records them for revert.</summary>
    private static void HideNewSourceRenderers(AshlandsRebornFableBunny marker)
    {
        var source = marker.Source;
        if (source == null || marker.Pivot == null) return;

        foreach (var r in source.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.forceRenderingOff) continue;
            // Meshes only: late particles are status vfx (burning/frost) that should stay
            // visible on the donor. The Morgen's own gore particles were caught at build time.
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
            var t = r.transform;
            var ours = false;
            while (t != null && t != source.transform)
            {
                if (t == marker.Pivot) { ours = true; break; }
                t = t.parent;
            }
            if (ours) continue;

            marker.HiddenRenderers.Add(r);
            marker.HiddenMaterials.Add(r.sharedMaterials);
            r.forceRenderingOff = true;
            r.sharedMaterials = InvisibleMaterials(r.sharedMaterials.Length);
            if (_lateHideLogCount++ < 12)
                Plugin.Log?.LogInfo($"[AR Bunny] hid late Morgen renderer '{r.name}'");
        }
    }

    // ---- wisp lash orbs --------------------------------------------------------------------
    // Two orbs orbit the donor at all times; on a swipe (Lash) attack they flare outward for
    // 0.25s, then track the hidden Morgen hand bones for the attack duration (the REAL swing,
    // hitbox-accurate), then return to orbit. This machinery is deliberately reusable: the
    // Light/Lightning elementals (M3/M4) reuse orbit/track with tracking always-on.

    internal enum LashState { Orbit, Flare, Track, Return }

    private static readonly Color WispColor = new(0.45f, 1f, 0.8f, 1f);
    private static readonly string[] WispVisualCandidates = { "demister_ball", "vfx_Demister", "Wisp" };
    private static bool _wispProbeLogged;

    private static bool LashIncludesWisps()
    {
        var s = Plugin.FableBunnyLashStyle?.Value;
        return string.Equals(s, "Wisps", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "Both", StringComparison.OrdinalIgnoreCase);
    }

    private static Shader? FindOrbShader()
    {
        foreach (var name in new[] { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Sprites/Default", "Standard" })
        {
            var s = Shader.Find(name);
            if (s != null) return s;
        }
        return null;
    }

    private static void BuildWispOrbs(AshlandsRebornFableBunny marker)
    {
        if (marker.Pivot == null) return;
        var h = marker.TargetHeight; // all orb dimensions scale off the configured height

        for (var i = 0; i < 2; i++)
        {
            var orb = new GameObject($"AR_WispOrb_{(i == 0 ? "l" : "r")}");
            orb.transform.SetParent(marker.Pivot, worldPositionStays: false);

            // Preferred visual: a stripped clone of a real wisp prefab (probed - M2 recon may
            // improve the candidate list); guaranteed fallback: a procedural additive orb.
            // Either way the orb gets a point light and a trail.
            if (TryBuildWispVisual(orb.transform, 0.22f * h) == null)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                TryDestroyImmediate(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(orb.transform, worldPositionStays: false);
                sphere.transform.localScale = Vector3.one * (0.18f * h);
                var shader = FindOrbShader();
                if (shader != null)
                {
                    var mat = new Material(shader) { color = WispColor };
                    if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", WispColor);
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", WispColor * 2f);
                    }
                    sphere.GetComponent<Renderer>().material = mat;
                }
            }

            // Idle stays subtle (the first shoot bathed the whole donor in cyan); the lash
            // phases brighten the light and enable the trail in UpdateOrbs.
            var light = orb.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = WispColor;
            light.intensity = 1f;
            light.range = 1.5f * h;
            light.shadows = LightShadows.None;

            var trail = orb.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.startWidth = 0.09f * h;
            trail.endWidth = 0f;
            trail.numCapVertices = 4;
            trail.emitting = false; // orbit leaves no trail; lash phases switch it on
            var trailShader = FindOrbShader();
            if (trailShader != null)
                trail.material = new Material(trailShader) { color = WispColor };
            trail.startColor = WispColor;
            trail.endColor = new Color(WispColor.r, WispColor.g, WispColor.b, 0f);

            var data = new WispOrb
            {
                Root = orb.transform,
                Light = light,
                Trail = trail,
                Phase = i == 0 ? 0f : Mathf.PI,
                Hand = i == 0 ? marker.HandL : marker.HandR,
            };
            marker.Orbs.Add(data);

            // Start at the orbit position so the first frame doesn't streak a trail from the feet.
            orb.transform.position = OrbitTarget(marker, data);
            trail.Clear();
        }
    }

    private static GameObject? TryBuildWispVisual(Transform parent, float scale)
    {
        foreach (var name in WispVisualCandidates)
        {
            var prefab = ZNetScene.instance?.GetPrefab(name);
            if (prefab == null) continue;

            var holder = new GameObject("AR_WispBuildHolder");
            holder.SetActive(false);
            var clone = UObject.Instantiate(prefab, holder.transform);
            StripToVisual(clone);
            var hasVisual = clone.GetComponentsInChildren<Renderer>(true).Any(r => r != null)
                || clone.GetComponentsInChildren<ParticleSystem>(true).Any(p => p != null);
            if (!hasVisual)
            {
                UObject.Destroy(holder);
                continue;
            }
            clone.transform.SetParent(parent, worldPositionStays: false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localScale = Vector3.one * scale;
            clone.SetActive(true);
            UObject.Destroy(holder);
            if (!_wispProbeLogged)
            {
                _wispProbeLogged = true;
                Plugin.Log?.LogInfo($"[AR Bunny] wisp orb visual source: '{name}'");
            }
            return clone;
        }
        if (!_wispProbeLogged)
        {
            _wispProbeLogged = true;
            Plugin.Log?.LogInfo(
                $"[AR Bunny] no wisp prefab matched ({string.Join(", ", WispVisualCandidates)}) - using procedural orb");
        }
        return null;
    }

    /// <summary>Strip a decorative clone to pure visuals: all MonoBehaviours, colliders,
    /// rigidbodies, force fields, and audio go; renderers/particles/lights stay. Lighter
    /// cousin of StripDonor for non-creature prefabs (no Animator to preserve).</summary>
    private static void StripToVisual(GameObject clone)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true).Where(mb => mb != null).ToList();
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
        foreach (var c in clone.GetComponentsInChildren<Collider>(true)) TryDestroyImmediate(c);
        foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) TryDestroyImmediate(rb);
        // AudioSource/ParticleSystemForceField live in unreferenced Unity modules - match by name.
        foreach (var comp in clone.GetComponentsInChildren<Component>(true))
            if (comp != null && (comp.GetType().Name == "ParticleSystemForceField" || comp.GetType().Name == "AudioSource"))
                TryDestroyImmediate(comp);
    }

    private static Vector3 OrbitTarget(AshlandsRebornFableBunny marker, WispOrb orb)
    {
        var h = marker.TargetHeight;
        var center = marker.Pivot!.position + Vector3.up * (0.5f * h);
        var ang = Time.time * 1.7f + orb.Phase;
        return center + new Vector3(
            Mathf.Cos(ang) * 0.95f * h,
            Mathf.Sin(Time.time * 2.3f + orb.Phase) * 0.2f * h,
            Mathf.Sin(ang) * 0.95f * h);
    }

    private static void UpdateOrbs(AshlandsRebornFableBunny marker, Character source, float dt)
    {
        if (marker.Orbs.Count == 0) return;

        // Lash phases: Flare (0.25s burst outward) -> Track (follow the hidden hands until
        // the attack ends; same InAttack grace as the roll) -> Return (0.4s) -> Orbit.
        switch (marker.LashState)
        {
            case LashState.Flare when Time.time - marker.LashPhaseStart > 0.25f:
                marker.LashState = LashState.Track;
                break;
            case LashState.Track when Time.time - marker.AttackStartTime > 0.6f && !source.InAttack():
                marker.LashState = LashState.Return;
                marker.LashPhaseStart = Time.time;
                break;
            case LashState.Return when Time.time - marker.LashPhaseStart > 0.4f:
                marker.LashState = LashState.Orbit;
                break;
        }

        var h = marker.TargetHeight;
        foreach (var orb in marker.Orbs)
        {
            if (orb.Root == null) continue;
            Vector3 target;
            float lerpRate, lightIntensity;
            switch (marker.LashState)
            {
                case LashState.Flare:
                    var center = marker.Pivot!.position + Vector3.up * (0.5f * h);
                    var outward = orb.Root.position - center;
                    outward.y *= 0.3f;
                    if (outward.sqrMagnitude < 0.01f)
                        outward = marker.Pivot.right * (orb.Phase > 1f ? -1f : 1f);
                    target = center + outward.normalized * (1.9f * h);
                    lerpRate = 14f;
                    lightIntensity = 5f;
                    break;
                case LashState.Track:
                    target = orb.Hand != null ? orb.Hand.position : OrbitTarget(marker, orb);
                    lerpRate = 22f;
                    lightIntensity = 3.5f;
                    break;
                case LashState.Return:
                    target = OrbitTarget(marker, orb);
                    lerpRate = 8f;
                    lightIntensity = 1.5f;
                    break;
                default:
                    target = OrbitTarget(marker, orb);
                    lerpRate = 6f;
                    lightIntensity = 1f;
                    break;
            }
            orb.Root.position = Vector3.Lerp(orb.Root.position, target, 1f - Mathf.Exp(-lerpRate * dt));
            if (orb.Light != null)
                orb.Light.intensity = Mathf.Lerp(orb.Light.intensity, lightIntensity, 1f - Mathf.Exp(-8f * dt));
            if (orb.Trail != null)
                orb.Trail.emitting = marker.LashState != LashState.Orbit;
        }
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
                if (m.HiddenRenderers[r] != null && !m.HiddenRenderers[r].forceRenderingOff)
                    m.HiddenRenderers[r].forceRenderingOff = true;
            HideNewSourceRenderers(m); // idle-time catch for late-instantiated visuals

            var star = GetStarScale(m.Source);
            foreach (var p in m.Proxies)
            {
                if (p.Root == null || p.RawHeight <= 0.01f) continue;
                var desired = (Plugin.FableBunnyHeight?.Value ?? 4f) * star / p.RawHeight
                              * (Plugin.FableBunnyScale?.Value ?? 1f);
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

    private static readonly string[] ReconTargets = { MorgenPrefab, "Hare", "Lox" };
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

        DumpEffectLists(prefab);

        // Shoot on the test island: flat, clutter-free backgrounds (user note 2026-07-08 -
        // the Ashlands fallback position has rocks/ruins that block the camera).
        yield return PhotoModePatches.TeleportToIslandRoutine(player);

        var pos = FindSolidSpawn(player);
        var go = UObject.Instantiate(prefab, pos, Quaternion.identity);
        Plugin.Log?.LogInfo("[AR BunnyRecon] observation Morgen spawned");
        // The Morgen's ground-rise + sleep window runs ~14s from spawn; shooting inside it
        // catches the (suppressed) rise fx and a still-buried donor. Wait it out.
        yield return new WaitForSeconds(16f);

        var hum = go != null ? go.GetComponent<Humanoid>() : null;
        var marker = go != null ? go.GetComponent<AshlandsRebornFableBunny>() : null;
        Check(marker != null && marker.Built, "swap built on observation Morgen");
        Check(marker != null && (!LashIncludesWisps() || marker.Orbs.Count == 2), "wisp orbs built");

        PhotoModePatches.EnableCameraOverride();

        // Forced attacks, one per attack item, two screenshots each (early + late pose).
        var items = hum != null ? hum.GetInventory()?.GetAllItems() : null;
        if (hum != null && items != null)
        {
            for (var i = 0; i < items.Count && go != null; i++)
            {
                // Re-anchor before each attack: the roll covers ~50m and can carry the
                // Morgen off the island platform into the ocean (user note 2026-07-08).
                go.transform.position = pos;
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

        // Star looks: cycle 0/1/2 on a fresh spawn - each change fires the SettingChanged ->
        // RefreshAll path (the exact mechanism the user drives via F1) and gets a screenshot.
        // AI frozen: an alerted Morgen keeps the donor running (the v2.0 star shots were
        // motion-blurred smears of a fleeing hare); frozen = it idles in place for the
        // tint comparison, and the wake-up rise never plays so no long wait is needed.
        var goStar = UObject.Instantiate(prefab, pos, Quaternion.identity);
        FreezeAI(goStar);
        yield return new WaitForSeconds(4f);
        for (var look = 0; look <= 2; look++)
        {
            if (goStar != null) goStar.transform.position = pos; // it wanders; keep it on the platform
            Plugin.FableBunnyStarLook.Value = look; // no-op rebuild at 0 (already built with it)
            yield return new WaitForSeconds(2.5f);
            var mStar = goStar != null ? goStar.GetComponent<AshlandsRebornFableBunny>() : null;
            Check(mStar != null && mStar.Built, $"starlook {look}: swap built");
            if (mStar != null && goStar != null)
            {
                var proxyRoot = mStar.Proxies.Count > 0 ? mStar.Proxies[0].Root : null;
                var smr = proxyRoot != null ? proxyRoot.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                Plugin.Log?.LogInfo(
                    $"[AR BunnyRecon] starlook {look} dbg: morgenPos={goStar.transform.position:F1} " +
                    $"pivotNull={mStar.Pivot == null} " +
                    $"proxyPos={(proxyRoot != null ? proxyRoot.transform.position.ToString("F1") : "-")} " +
                    $"proxyActive={proxyRoot != null && proxyRoot.activeInHierarchy} " +
                    $"smrVisible={(smr != null ? smr.isVisible.ToString() : "null")} " +
                    $"smrForceOff={(smr != null ? smr.forceRenderingOff.ToString() : "null")} " +
                    $"smrBounds={(smr != null ? smr.bounds.center.ToString("F1") : "-")} orbs={mStar.Orbs.Count}");
            }
            if (goStar != null)
            {
                PhotoModePatches.AimCameraAt(goStar, 60);
                yield return new WaitForSeconds(0.2f);
                LogNearbyRenderers(goStar, 12f, $"starlook {look}"); // after aim: shot's frustum
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_starlook_{look}.png"));
            }
        }
        Plugin.FableBunnyStarLook.Value = 0;
        if (goStar != null) ZNetScene.instance?.Destroy(goStar);
        yield return new WaitForSeconds(1f);

        // Lifecycle: fresh spawn, MasterSwitch OFF -> vanilla Morgen back, ON -> rebuilt,
        // RefreshAll (the F10 path) -> rebuilt again.
        var go2 = UObject.Instantiate(prefab, pos, Quaternion.identity);
        yield return new WaitForSeconds(16f); // rise + sleep window (see above)
        if (go2 != null) go2.transform.position = pos; // keep it on the platform for the toggle shots
        var m2 = go2 != null ? go2.GetComponent<AshlandsRebornFableBunny>() : null;
        Check(m2 != null && m2.Built, "lifecycle: swap built on fresh Morgen");
        var srcRenderer = m2 != null && m2.HiddenRenderers.Count > 0 ? m2.HiddenRenderers[0] : null;

        Plugin.Instance.ApplyMasterSwitch(false);
        yield return new WaitForSeconds(1f);
        Check(go2 != null && go2.GetComponent<AshlandsRebornFableBunny>() == null, "lifecycle: marker gone after master OFF");
        Check(srcRenderer != null && !srcRenderer.forceRenderingOff, "lifecycle: Morgen renderers restored after master OFF");
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
        Check(srcRenderer != null && srcRenderer.forceRenderingOff, "lifecycle: Morgen renderers hidden after master ON");
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

    /// <summary>Pick a test-creature spawn point on SOLID ground near the player. The naive
    /// player.forward*4 points at open ocean when the island teleport leaves the player
    /// facing the water - the spawned Morgen dropped straight into the sea (v2.0 shoot).
    /// Probes 8 directions x 2 distances with a downward raycast and requires ground within
    /// ~1.5m of the player's own height (the platform), falling back to the player's feet.</summary>
    private static void FreezeAI(GameObject? go)
    {
        if (go == null) return;
        foreach (var ai in go.GetComponentsInChildren<BaseAI>())
            ai.enabled = false;
    }

    private static Vector3 FindSolidSpawn(Player player)
    {
        var basePos = player.transform.position;
        foreach (var dist in new[] { 4f, 6f })
        {
            for (var i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0f, 45f * i, 0f) * player.transform.forward;
                var candidate = basePos + dir * dist;
                if (Physics.Raycast(candidate + Vector3.up * 3f, Vector3.down, out var hit, 10f)
                    && Mathf.Abs(hit.point.y - basePos.y) < 1.5f)
                    return hit.point + Vector3.up * 0.3f;
            }
        }
        Plugin.Log?.LogWarning("[AR BunnyRecon] no solid spawn found around player - using player position");
        return basePos + Vector3.up * 0.3f;
    }

    /// <summary>Dev shot-time recon: name every renderer whose bounds sit in the MAIN camera
    /// frustum near the target, with its render flags - identifies mystery visuals (and
    /// whether a "hidden" renderer is still drawing) from evidence, not guesses.</summary>
    private static void LogNearbyRenderers(GameObject target, float radius, string label)
    {
        try
        {
            var cam = Camera.main;
            var planes = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
            var names = new HashSet<string>();
            foreach (var r in UObject.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                if ((r.transform.position - target.transform.position).sqrMagnitude > radius * radius) continue;
                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                names.Add($"{r.transform.root.name}/{r.name}(fOff={r.forceRenderingOff},vis={r.isVisible})");
                if (names.Count >= 15) break;
            }
            Plugin.Log?.LogInfo($"[AR BunnyRecon] {label} in-frustum near target: {string.Join(" | ", names)}");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[AR BunnyRecon] nearby-renderer scan error: {ex.Message}");
        }
    }

    /// <summary>Reflection sweep over every EffectList field on the prefab's components and
    /// its default items' attacks - names the rise/death/gore effect prefabs so the
    /// suppression blocklist rests on evidence instead of guesses.</summary>
    private static void DumpEffectLists(GameObject prefab)
    {
        try
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("[AR Bunny] Morgen EffectList inventory:");
            foreach (var comp in prefab.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                AppendEffectFields(comp, comp.GetType().Name, sb);
            }

            var humanoid = prefab.GetComponent<Humanoid>();
            if (humanoid?.m_defaultItems != null)
            {
                foreach (var item in humanoid.m_defaultItems)
                {
                    var shared = item != null ? item.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                    if (shared == null) continue;
                    AppendEffectFields(shared, $"{item!.name}.shared", sb);
                    if (shared.m_attack != null) AppendEffectFields(shared.m_attack, $"{item.name}.attack", sb);
                    if (shared.m_secondaryAttack != null) AppendEffectFields(shared.m_secondaryAttack, $"{item.name}.secondary", sb);
                }
            }
            Plugin.Log?.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[AR Bunny] fx dump error: {ex.Message}");
        }
    }

    private static void AppendEffectFields(object target, string label, StringBuilder sb)
    {
        foreach (var f in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (f.FieldType != typeof(EffectList)) continue;
            var list = f.GetValue(target) as EffectList;
            var names = list?.m_effectPrefabs?
                .Where(e => e?.m_prefab != null)
                .Select(e => e.m_prefab.name)
                .ToList();
            if (names != null && names.Count > 0)
                sb.AppendLine($"  {label}.{f.Name}: {string.Join(", ", names)}");
        }
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

    /// <summary>One wisp lash orb: procedural or prefab-sourced visual + light + trail,
    /// orbiting the donor and tracking a hidden Morgen hand bone during swipes.</summary>
    internal sealed class WispOrb
    {
        public Transform? Root;
        public Light? Light;
        public TrailRenderer? Trail;
        public float Phase;
        public Transform? Hand;
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
    // Hidden via forceRenderingOff + invisible material swap (both animation-proof);
    // revert clears the flag and restores the cached material arrays.
    public readonly List<Renderer> HiddenRenderers = new();
    public readonly List<Material[]> HiddenMaterials = new();
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
    public float RollStartTime;
    public float NextRollJumpTime;
    public bool Built;

    // Wisp lash (v2): hidden Morgen hand bones + the orbs that lash along them.
    public Transform? HandL;
    public Transform? HandR;
    public readonly List<FableBunnyPatches.WispOrb> Orbs = new();
    public FableBunnyPatches.LashState LashState;
    public float LashPhaseStart;

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
                if (r == null) continue;
                r.forceRenderingOff = false;
                if (i < HiddenMaterials.Count && HiddenMaterials[i] != null)
                    r.sharedMaterials = HiddenMaterials[i];
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
