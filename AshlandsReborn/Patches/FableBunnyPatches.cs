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
///  - Swipes: the Morgen's arm swings have no visible correlate on the donor. The striking
///    SIDE is measured (root-relative hand-bone travel during the wind-up, cached per anim
///    name), the donor sits up on its haunches (vanilla "alert"-style state, FableBunnySitUp),
///    and FableBunnySwipeStyle picks the strike visual: Paws = procedural front-leg swing on
///    the matching side; Wisps = the striking-side wisp orb telegraphs at the hidden hand and
///    lashes along the real swing; Comets = the original both-orb flare + streaking trails.
///    FableBunnyWispOrbit gates the constant idle orbit (off = orbs fade in only for strikes).
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
    // "idle" float: selects between the idle blend-tree variants (hare recon: Idle /
    // Idle Alerted / Idle Eating live in a blend tree, NOT as CrossFade-able states).
    private static readonly int IdleHash = Animator.StringToHash("idle");

    // Humanoid.m_currentAttack / Attack.m_attackAnimation via reflection - accessibility has
    // shifted between game versions; a null just degrades attack classification to "unknown".
    private static readonly FieldInfo? FCurrentAttack = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
    private static readonly FieldInfo? FAttackAnimation = AccessTools.Field(typeof(Attack), "m_attackAnimation");

    internal enum AttackClass { Lash, Smash, Bite, Roll, Unknown }

    /// <summary>Which look replaces the Morgen (FableBunnyMode - the user's rotate knob).</summary>
    internal enum BunnyMode { Bunny, LightElemental, LightningElemental }

    internal static BunnyMode CurrentMode()
    {
        var s = Plugin.FableBunnyMode?.Value;
        if (string.Equals(s, "LightElemental", StringComparison.OrdinalIgnoreCase)) return BunnyMode.LightElemental;
        if (string.Equals(s, "LightningElemental", StringComparison.OrdinalIgnoreCase)) return BunnyMode.LightningElemental;
        return BunnyMode.Bunny;
    }

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
        marker.Mode = CurrentMode();

        // The hidden Morgen bones keep animating hitbox-accurately (AlwaysAnimate) - hands
        // anchor the wisp-lash tracking; Chest + limb chains anchor the elemental modes.
        FindMorgenBones(source, marker);

        if (marker.Mode == BunnyMode.Bunny)
        {
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
            // Orbs exist when a wisp strike style needs them OR the idle orbit is wanted
            // (orbit + Paws strikes is a legal combination).
            if (SwipeUsesWisps() || WispOrbitOn()) BuildWispOrbs(marker);
        }
        else
        {
            BuildElemental(marker);
        }

        marker.Built = true;

        if (_swapLogCount++ < 8)
            Plugin.Log?.LogInfo(
                $"[Fable Bunny] Swapped Morgen -> {marker.Mode}: targetH={targetHeight:F2}, " +
                $"star={starScale:F2}, orbs={marker.Orbs.Count}, bolts={marker.Bolts.Count}, " +
                $"starLook={Plugin.FableBunnyStarLook?.Value ?? 0}");
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

        // M5 CurlAndRoll: insert a spin node at the donor's visual CENTER (tumbling about
        // the feet would swing the body through the ground). Identity when unused, so the
        // default HopHigher path is untouched.
        if (isPrimary && groundedBounds.HasValue)
        {
            var spin = new GameObject("AR_RollSpin");
            spin.transform.SetParent(marker.Pivot, worldPositionStays: false);
            spin.transform.localPosition = marker.Pivot!.InverseTransformPoint(groundedBounds.Value.center);
            clone.transform.SetParent(spin.transform, worldPositionStays: true);
            marker.SpinNode = spin.transform;
        }

        // M5 EarWhip: cache the hare's ear bones (recon names). Post-Animator rotation
        // writes in Drive turn them into a readable lash at giant scale.
        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
            if (t.name is "Ear.l" or "Ear.r" or "ear1.l" or "ear1.r")
                marker.EarBones.Add(t);

        // v3 Paws swipe style: cache the donor's front-leg chains for the procedural strike.
        if (isPrimary) ResolvePawChains(clone, marker);

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

            var elemental = marker.Mode != BunnyMode.Bunny;
            switch (cls)
            {
                case AttackClass.Roll:
                    marker.Rolling = true;
                    marker.RollStartTime = Time.time;
                    marker.NextRollJumpTime = Time.time; // first hop fires immediately in Drive
                    break;
                case AttackClass.Bite when !elemental:
                    StartPounce(marker, duration: 0.5f, amplitude: 1.2f); // signed off - don't touch
                    break;
                case AttackClass.Smash when !elemental:
                    StartPounce(marker, duration: 0.8f, amplitude: 1.5f);
                    break;
                case AttackClass.Bite:
                case AttackClass.Smash:
                    // Elemental read: core flash spike; the Light elemental adds a light beam
                    // along the attack direction (its projected bite/slam).
                    marker.CoreFlashEnd = Time.time + 0.6f;
                    if (marker.Mode == BunnyMode.LightElemental)
                    {
                        marker.BeamDir = __instance.transform.forward;
                        marker.BeamEnd = Time.time + 0.8f;
                    }
                    break;
                case AttackClass.Lash when !elemental:
                {
                    // v3 swipes: measure/recall the striking side, sit up on the haunches for
                    // the wind-up, then let the configured style carry the strike read. The
                    // body pounce only fills in when nothing else reads: toned down when a
                    // strike visual exists, full-size for the bare Off style (old default-case
                    // behavior for LashStyle=Off).
                    BeginSwipeSide(marker, animName);
                    var seated = StartSitUp(marker);
                    var style = CurrentSwipeStyle();
                    var strikeReads = (marker.Orbs.Count > 0 && SwipeUsesWisps())
                        || (EarWhipOn() && marker.EarBones.Count > 0)
                        || (style == SwipeStyle.Paws && (marker.PawBonesL.Count > 0 || marker.PawBonesR.Count > 0));
                    if (!seated)
                        StartPounce(marker, duration: 0.45f, amplitude: strikeReads ? 0.4f : 1.0f);
                    StartLash(marker);
                    break;
                }
                case AttackClass.Lash:
                    marker.CoreFlashEnd = Time.time + 0.3f;
                    StartLash(marker);
                    break;
                default:
                    if (!elemental) StartPounce(marker, duration: 0.45f, amplitude: 1.0f);
                    else marker.CoreFlashEnd = Time.time + 0.4f;
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

    // ---- swipe side detection (v3) --------------------------------------------------------
    // attack_swipe_1..4 don't encode which arm swings, so the side is MEASURED: root-relative
    // travel of the hidden Morgen hand bones during the wind-up decides it, and the result is
    // cached per anim name (plus seeded from recon runs) so the telegraph points at the right
    // side from the very first swing of a known anim.

    private static readonly Dictionary<string, int> SwipeSideByAnim = new(StringComparer.OrdinalIgnoreCase)
    {
        // Seeded from run-2 recon (Morgen clip chronology + travel verdicts agree):
        // swipe_1 = Swipe L only, swipe_2 = Swipe R only, swipe_3 = L-then-R combo,
        // swipe_4 = R-then-L combo. Values are the OPENING side (what the wind-up
        // telegraphs); the live clip follow switches sides mid-combo.
        { "attack_swipe_1", -1 },
        { "attack_swipe_2", 1 },
        { "attack_swipe_3", -1 },
        { "attack_swipe_4", 1 },
    };
    private static readonly HashSet<string> SideLogged = new(StringComparer.OrdinalIgnoreCase);

    private static void BeginSwipeSide(AshlandsRebornFableBunny marker, string? animName)
    {
        marker.PendingSideAnim = animName;
        marker.LashSide = animName != null && SwipeSideByAnim.TryGetValue(animName, out var s) ? s : 0;
        marker.SideCommitted = marker.LashSide != 0;
        marker.SideFinalized = false;
        marker.SideCached = false;
        marker.HandTravelL = 0f;
        marker.HandTravelR = 0f;
        var root = marker.Source != null ? marker.Source.transform : null;
        if (root == null) return;
        if (marker.HandL != null) marker.HandPrevL = root.InverseTransformPoint(marker.HandL.position);
        if (marker.HandR != null) marker.HandPrevR = root.InverseTransformPoint(marker.HandR.position);
    }

    /// <summary>Turn accumulated L/R hand travel into a side: -1/L, +1/R, 0 = both-arms
    /// swing (near-identical travel) or no hand bones.</summary>
    private static int SideFromTravel(float dL, float dR)
        => dL + dR < 0.05f || Mathf.Abs(dL - dR) < 0.15f * Mathf.Max(dL, dR)
            ? 0
            : (dL > dR ? -1 : 1);

    /// <summary>Authoritative live side: the Morgen's swipe CLIP names end in L/R
    /// ("Attack Swipe R" - run-2 recon), and combo swipes (attack_swipe_4 plays R then L)
    /// switch clips mid-attack, so this is read every frame while lashing. 0 = the current
    /// clip doesn't name a side (fall back to travel measurement).</summary>
    private static int SideFromClip(AshlandsRebornFableBunny marker)
    {
        var anim = marker.SourceAnimator;
        if (anim == null || !anim.isActiveAndEnabled) return 0;
        var clips = anim.GetCurrentAnimatorClipInfo(0);
        var clip = clips.Length > 0 ? clips[0].clip : null;
        if (clip == null) return 0;
        var n = clip.name.ToLowerInvariant();
        if (!n.Contains("swipe") && !n.Contains("attack")) return 0;
        if (n.EndsWith(" l") || n.EndsWith("_l") || n.EndsWith(".l")) return -1;
        if (n.EndsWith(" r") || n.EndsWith("_r") || n.EndsWith(".r")) return 1;
        return 0;
    }

    private static void UpdateSwipeSide(AshlandsRebornFableBunny marker)
    {
        if (marker.SideFinalized || marker.LashState == LashState.Orbit || marker.Source == null) return;

        // Clip names win whenever they speak: they are hitbox-authoritative and they track
        // combo swipes that change arms mid-attack. The first clip verdict of the attack is
        // cached so the NEXT wind-up of this anim telegraphs the correct opening side.
        var clipSide = SideFromClip(marker);
        if (clipSide != 0)
        {
            marker.LashSide = clipSide;
            marker.SideCommitted = true;
            var clipAnim = marker.PendingSideAnim;
            if (clipAnim != null && !marker.SideCached)
            {
                marker.SideCached = true;
                SwipeSideByAnim[clipAnim] = clipSide;
                if (SideLogged.Add(clipAnim))
                    Plugin.Log?.LogInfo($"[AR Bunny] swipe side (clip) '{clipAnim}' opens " +
                        $"{(clipSide < 0 ? "LEFT" : "RIGHT")}");
            }
        }

        var root = marker.Source.transform;

        // The whole swing is measured (even when the cache pre-committed the side): the
        // full-swing travel is the better estimate, and it's what gets cached for the NEXT
        // telegraph of this anim.
        if (marker.LashState != LashState.Return)
        {
            if (marker.HandL != null)
            {
                var p = root.InverseTransformPoint(marker.HandL.position);
                marker.HandTravelL += (p - marker.HandPrevL).magnitude;
                marker.HandPrevL = p;
            }
            if (marker.HandR != null)
            {
                var p = root.InverseTransformPoint(marker.HandR.position);
                marker.HandTravelR += (p - marker.HandPrevR).magnitude;
                marker.HandPrevR = p;
            }
        }

        var dL = marker.HandTravelL;
        var dR = marker.HandTravelR;

        // Live commit at the end of the wind-up window - the strike visuals need a side now.
        if (!marker.SideCommitted && Time.time - marker.LashPhaseStart >= 0.25f)
        {
            marker.SideCommitted = true;
            marker.LashSide = SideFromTravel(dL, dR);
        }

        // Finalize once the swing settles: cache the full-swing travel verdict for this anim
        // name - unless a clip verdict already claimed the cache (it's authoritative).
        if (marker.LashState != LashState.Return) return;
        marker.SideFinalized = true;
        var anim = marker.PendingSideAnim;
        if (anim == null || marker.SideCached) return;
        var full = SideFromTravel(dL, dR);
        SwipeSideByAnim[anim] = full;
        if (SideLogged.Add(anim))
            Plugin.Log?.LogInfo($"[AR Bunny] swipe side detect '{anim}' (full swing): " +
                $"L={dL:F2} R={dR:F2} -> {(full < 0 ? "LEFT" : full > 0 ? "RIGHT" : "BOTH")}");
    }

    // ---- sit-up on the haunches (v3) ------------------------------------------------------

    private static readonly string[] SitStateCandidates = { "alert", "sit", "sit_up", "idle_alert", "scared" };

    private static BunnyProxy? ActiveProxy(AshlandsRebornFableBunny marker)
        => marker.ActiveProxyIndex >= 0 && marker.ActiveProxyIndex < marker.Proxies.Count
            ? marker.Proxies[marker.ActiveProxyIndex]
            : null;

    /// <summary>Cross-fade the donor into its sit/alert animator state for the swipe wind-up
    /// (the hare's vanilla "sit up on the haunches" pose). Returns true when a body pose
    /// engaged (state or the procedural rear-up fallback) so the caller can skip the pounce -
    /// the two would fight over the pivot pitch.</summary>
    private static bool StartSitUp(AshlandsRebornFableBunny marker)
    {
        if (Plugin.FableBunnySitUp?.Value != true || marker.Mode != BunnyMode.Bunny) return false;
        var proxy = ActiveProxy(marker);
        var anim = proxy?.Animator;
        if (anim != null && anim.isActiveAndEnabled && proxy!.SitStateHash == 0)
            ResolveSitState(proxy, anim);
        if (anim == null || !anim.isActiveAndEnabled
            || (proxy!.SitStateHash <= 0 && !proxy.ParamHashes.Contains(IdleHash)))
        {
            marker.SitProceduralUntil = Time.time + 0.6f; // no usable state/param: rear-up
            return true;
        }
        if (proxy.SitStateHash > 0)
        {
            // A real sit/alert STATE exists - cross-fade in and record where to return.
            if (!marker.SitEntered)
                marker.SitReturnStateHash = anim.GetCurrentAnimatorStateInfo(0).shortNameHash;
            marker.SitUsesParam = false;
            anim.CrossFadeInFixedTime(proxy.SitStateHash, 0.12f, 0);
        }
        else
        {
            // Hare path: the alert pose is an idle blend-tree variant selected by the "idle"
            // float. UpdateSitUp forces the param each frame; speed=0 keeps the controller
            // in the idle region of the movement tree.
            marker.SitUsesParam = true;
        }
        marker.SitEntered = true;
        marker.SitClipLogged = false;
        marker.SitUntil = Time.time + 2.5f; // safety cap; the lash Return normally ends it
        return true;
    }

    /// <summary>Probe the donor controller for a usable sit/alert STATE (HasState works on
    /// runtime Animators where state-machine introspection doesn't). Config value first, then
    /// the recon candidates; -1 = none, use the procedural rear-up.</summary>
    private static void ResolveSitState(BunnyProxy proxy, Animator anim)
    {
        var configured = Plugin.FableBunnySitState?.Value?.Trim();
        var names = string.IsNullOrEmpty(configured)
            ? SitStateCandidates
            : new[] { configured! }.Concat(SitStateCandidates).ToArray();
        foreach (var name in names)
        {
            var hash = Animator.StringToHash(name);
            if (hash == 0 || hash == -1 || !anim.HasState(0, hash)) continue;
            proxy.SitStateHash = hash;
            Plugin.Log?.LogInfo($"[AR Bunny] sit-up state: '{name}' on donor '{proxy.DonorName}'");
            return;
        }
        proxy.SitStateHash = -1;
        Plugin.Log?.LogInfo($"[AR Bunny] no sit-up state on donor '{proxy.DonorName}' " +
            $"(probed: {string.Join(", ", names.Distinct())}) - " +
            (proxy.ParamHashes.Contains(IdleHash)
                ? "using the 'idle' blend-tree param path"
                : "using the procedural rear-up"));
    }

    /// <summary>Hold the sit pose while the lash wind-up/strike runs, steer the controller
    /// back if a param-driven transition pulls it out early, and cross-fade back to the
    /// recorded state once the lash settles (locomotion params take over from there).</summary>
    private static void UpdateSitUp(AshlandsRebornFableBunny marker, Animator? anim)
    {
        if (!marker.SitEntered) return;
        if (anim == null || !anim.isActiveAndEnabled) { marker.SitEntered = false; return; }
        var done = marker.LashState == LashState.Return || marker.LashState == LashState.Orbit
                   || Time.time > marker.SitUntil;
        if (done)
        {
            if (marker.SitUsesParam) anim.SetFloat(IdleHash, 0f); // back to the plain idle
            else if (marker.SitReturnStateHash != 0)
                anim.CrossFadeInFixedTime(marker.SitReturnStateHash, 0.25f, 0);
            marker.SitEntered = false;
            return;
        }
        if (marker.SitUsesParam)
        {
            // Direct (undamped) write: the pose must land within the wind-up window.
            anim.SetFloat(IdleHash, Plugin.FableBunnySitIdleValue?.Value ?? 1f);
            // One-shot blend evidence per sit: which clips (and weights) actually play at
            // this idle value - names the blend-tree order from evidence, not guesses.
            if (!marker.SitClipLogged && Time.time - marker.AttackStartTime > 0.5f)
            {
                marker.SitClipLogged = true;
                var infos = anim.GetCurrentAnimatorClipInfo(0);
                Plugin.Log?.LogInfo("[AR Bunny] sit blend @idle=" +
                    $"{Plugin.FableBunnySitIdleValue?.Value ?? 1f:F1}: " +
                    string.Join(", ", infos.Select(ci =>
                        $"{(ci.clip != null ? ci.clip.name : "?")}:{ci.weight:F2}")));
            }
            return;
        }
        // Hold the pose: steer the controller back if a param-driven transition pulled it out.
        var sitHash = ActiveProxy(marker)?.SitStateHash ?? 0;
        if (sitHash > 0
            && anim.GetCurrentAnimatorStateInfo(0).shortNameHash != sitHash
            && anim.GetNextAnimatorStateInfo(0).shortNameHash != sitHash)
            anim.CrossFadeInFixedTime(sitHash, 0.12f, 0);
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

        // Sit-up fallback for donors without a sit/alert state: rear the pivot back through
        // the wind-up (negative pitch = nose up; the pounce's lunge uses positive).
        if (marker.SitProceduralUntil > Time.time)
        {
            var w = 1f - Mathf.Clamp01((marker.SitProceduralUntil - Time.time) / 0.6f);
            pitch -= 18f * Mathf.Sin(Mathf.PI * w);
        }

        // Roll presentation "HopHigher": repeating airborne bounce arcs with a landing squash,
        // plus the donor's real jump trigger on each arc - destructive bounding instead of
        // the v1 flat jog.
        var bounceY = 0f;
        if (marker.Rolling && marker.Mode == BunnyMode.Bunny && IsHopHigherRoll())
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

        // M5 "CurlAndRoll": tumble the donor about its center while rolling, spin rate
        // matched to ground speed over the body radius (a rolling ball, not a spinning
        // sprite). The yaw stabilizer already faces travel, so local X is the tumble axis.
        if (marker.SpinNode != null)
        {
            if (marker.Rolling && marker.Mode == BunnyMode.Bunny && !IsHopHigherRoll())
            {
                var spinSpeed = Mathf.Clamp(
                    planarVel.magnitude / (0.35f * marker.TargetHeight) * Mathf.Rad2Deg, 180f, 720f);
                marker.RollSpinAngle = (marker.RollSpinAngle + spinSpeed * dt) % 360f;
                marker.SpinNode.localRotation = Quaternion.AngleAxis(marker.RollSpinAngle, Vector3.right);
            }
            else if (marker.RollSpinAngle != 0f || marker.SpinNode.localRotation != Quaternion.identity)
            {
                marker.RollSpinAngle = 0f;
                marker.SpinNode.localRotation = Quaternion.Slerp(
                    marker.SpinNode.localRotation, Quaternion.identity, 1f - Mathf.Exp(-10f * dt));
            }
        }

        UpdateSwipeSide(marker);
        UpdateOrbs(marker, source, dt);
        UpdateElemental(marker, source, dt);
        UpdateSitUp(marker, anim);

        if (anim == null || !anim.isActiveAndEnabled) return;

        // Locomotion sync on the active proxy: same params vanilla ZSyncAnimation writes.
        // Move-anim speed sync fixes the v1 "moonwalking": at ~3x scale the authored run
        // cycle paddles faster than the ground covered, so the animation slows toward the
        // configured factor as locomotion speed rises. Idle fidgets stay full-rate.
        var speed = planarVel.magnitude;
        if (marker.Rolling) speed = Mathf.Max(speed, 5f);
        // Seated: zero the locomotion input so a param-driven move transition doesn't yank
        // the controller out of the sit pose mid-wind-up (the Morgen may still be drifting).
        if (marker.SitEntered) speed = 0f;
        anim.speed = Mathf.Lerp(1f, Plugin.FableBunnyMoveAnimSpeed?.Value ?? 0.55f, Mathf.Clamp01(speed / 4f));
        if (proxy!.ParamHashes.Contains(ForwardSpeedHash))
        {
            // Seated writes are UNDAMPED: the 0.2s damp needs ~0.5s to actually reach 0,
            // which outlasts the whole 0.25s wind-up (run-2 shots caught the donor still
            // mid-locomotion-crouch instead of sitting).
            if (marker.SitEntered) anim.SetFloat(ForwardSpeedHash, 0f);
            else anim.SetFloat(ForwardSpeedHash, speed, 0.2f, dt);
        }
        if (proxy.ParamHashes.Contains(TurnSpeedHash) && dt > 0.0001f)
        {
            var turn = Mathf.DeltaAngle(marker.PrevYaw, marker.LastYaw) * Mathf.Deg2Rad / dt;
            anim.SetFloat(TurnSpeedHash, turn, 0.2f, dt);
        }
        if (proxy.ParamHashes.Contains(OnGroundHash))
            anim.SetBool(OnGroundHash, true);
        marker.PrevYaw = marker.LastYaw;

        // Post-Animator bone overrides (this postfix runs after the donor Animator wrote the
        // frame's pose, so the writes stick for rendering - the same ordering trick
        // FableWarriorPatches.Drive relies on): M5 ear whip + v3 paw strike.
        ApplyEarWhip(marker);
        ApplyPawStrike(marker);
    }

    /// <summary>Wind-up -> strike -> settle rotation layered onto the donor's striking-side
    /// front-leg chain during swipes (SwipeStyle Paws). Same post-Animator trick as the ear
    /// whip; composes with the sit-up pose, which lifts the forepaws off the ground. Axis is
    /// the ear whip's calibrated guess (local X); tune here if shots show sideways swings.</summary>
    private static void ApplyPawStrike(AshlandsRebornFableBunny marker)
    {
        if (marker.Mode != BunnyMode.Bunny || CurrentSwipeStyle() != SwipeStyle.Paws) return;
        if (marker.PawBonesL.Count == 0 && marker.PawBonesR.Count == 0) return;
        float angle;
        switch (marker.LashState)
        {
            case LashState.Flare: // wind-up: the paw draws back/up
                angle = -40f * Mathf.Clamp01((Time.time - marker.LashPhaseStart) / 0.25f);
                break;
            case LashState.Track: // strike: fast forward-down swipe with decaying follow-through
                var t = Time.time - marker.LashPhaseStart - 0.25f;
                angle = Mathf.Lerp(-40f, 55f, Mathf.Clamp01(t * 8f))
                        + 10f * Mathf.Sin(t * 16f) * Mathf.Exp(-t * 3f);
                break;
            case LashState.Return: // settle back to the authored pose
                angle = 55f * (1f - Mathf.Clamp01((Time.time - marker.LashPhaseStart) / 0.4f));
                break;
            default:
                return; // idle: leave the authored leg animation alone
        }
        if (marker.LashSide <= 0) SwingChain(marker.PawBonesL, angle);
        if (marker.LashSide >= 0) SwingChain(marker.PawBonesR, angle);
    }

    /// <summary>Distribute a swing angle across a leg chain, shoulder-heavy, so the limb arcs
    /// instead of hinging at one joint.</summary>
    private static void SwingChain(List<Transform> bones, float angle)
    {
        if (bones.Count == 0) return;
        for (var i = 0; i < bones.Count; i++)
        {
            var b = bones[i];
            if (b == null) continue;
            var w = bones.Count == 1 ? 1f
                : bones.Count == 2 ? (i == 0 ? 0.6f : 0.4f)
                : (i == 0 ? 0.5f : i == 1 ? 0.3f : 0.2f);
            b.localRotation *= Quaternion.AngleAxis(angle * w, Vector3.right);
        }
    }

    // Hare rig (recon 2026-07-19): Shoulder.l -> FrontUpperLeg.l -> FrontLowerLeg.l (+.r).
    // "target" is excluded: FrontLegTarget.l/r are IK stubs parented at the Armature root -
    // rotating them does nothing and their shallow depth would hijack the chain sort.
    private static readonly string[] PawNameFragments =
        { "shoulder", "frontupperleg", "frontlowerleg", "frontleg", "front_leg", "upperarm", "forearm", "paw", "hand" };

    /// <summary>Find the donor's front-leg bone chains by loose name match (the hare rig's
    /// exact names come from recon; other donors vary). Parent-most 3 bones per side carry
    /// the swing. Resolved names are logged so a rig rename is diagnosable from the log.</summary>
    private static void ResolvePawChains(GameObject clone, AshlandsRebornFableBunny marker)
    {
        marker.PawBonesL.Clear();
        marker.PawBonesR.Clear();
        foreach (var t in clone.GetComponentsInChildren<Transform>(true))
        {
            var n = t.name.ToLowerInvariant();
            var left = n.EndsWith(".l") || n.EndsWith("_l");
            var right = n.EndsWith(".r") || n.EndsWith("_r");
            if (!left && !right) continue;
            if (n.Contains("target")) continue;
            if (!PawNameFragments.Any(f => n.Contains(f))) continue;
            (left ? marker.PawBonesL : marker.PawBonesR).Add(t);
        }
        SortAndTrimChain(marker.PawBonesL);
        SortAndTrimChain(marker.PawBonesR);
        if (marker.PawBonesL.Count == 0 && marker.PawBonesR.Count == 0)
            Plugin.Log?.LogInfo("[AR Bunny] no front-leg bones matched on the donor - " +
                "Paws swipe style degrades to sit-up + pounce only");
        else
            Plugin.Log?.LogInfo("[AR Bunny] paw chains: " +
                $"L=[{string.Join(",", marker.PawBonesL.Select(b => b.name))}] " +
                $"R=[{string.Join(",", marker.PawBonesR.Select(b => b.name))}]");
    }

    private static void SortAndTrimChain(List<Transform> bones)
    {
        bones.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
        if (bones.Count > 3) bones.RemoveRange(3, bones.Count - 3);
    }

    private static int Depth(Transform t)
    {
        var d = 0;
        while (t.parent != null) { d++; t = t.parent; }
        return d;
    }

    /// <summary>Wind-up -> snap -> settle rotation layered onto the hare's ear bones during
    /// lash attacks. Post-multiplies the Animator's live localRotation each frame (no
    /// accumulation: the Animator rewrites the pose before we run). Axis is a calibrated
    /// guess (local X pitch); if screenshots show sideways ears, this is the line to tune.</summary>
    private static void ApplyEarWhip(AshlandsRebornFableBunny marker)
    {
        if (marker.EarBones.Count == 0 || !EarWhipOn()) return;
        float angle;
        switch (marker.LashState)
        {
            case LashState.Flare: // wind-up: ears sweep back
                angle = -45f * Mathf.Clamp01((Time.time - marker.LashPhaseStart) / 0.25f);
                break;
            case LashState.Track: // snap forward with a fast oscillating whip
                var t = Time.time - marker.LashPhaseStart - 0.25f;
                angle = 80f * Mathf.Clamp01(t * 8f) + 15f * Mathf.Sin(t * 18f);
                break;
            case LashState.Return: // settle back to the authored pose
                angle = 80f * (1f - Mathf.Clamp01((Time.time - marker.LashPhaseStart) / 0.4f));
                break;
            default:
                return; // idle: leave the authored ear animation alone
        }
        foreach (var e in marker.EarBones)
            if (e != null) e.localRotation *= Quaternion.AngleAxis(angle, Vector3.right);
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

    internal enum SwipeStyle { Paws, Wisps, Comets, Off }

    internal static SwipeStyle CurrentSwipeStyle()
    {
        var s = Plugin.FableBunnySwipeStyle?.Value;
        if (string.Equals(s, "Wisps", StringComparison.OrdinalIgnoreCase)) return SwipeStyle.Wisps;
        if (string.Equals(s, "Comets", StringComparison.OrdinalIgnoreCase)) return SwipeStyle.Comets;
        if (string.Equals(s, "Off", StringComparison.OrdinalIgnoreCase)) return SwipeStyle.Off;
        return SwipeStyle.Paws;
    }

    internal static bool SwipeUsesWisps()
        => CurrentSwipeStyle() is SwipeStyle.Wisps or SwipeStyle.Comets;

    private static bool EarWhipOn() => Plugin.FableBunnyEarWhip?.Value == true;

    internal static bool WispOrbitOn() => Plugin.FableBunnyWispOrbit?.Value != false;

    private static bool WindupIsFlare()
        => string.Equals(Plugin.FableBunnyWindupStyle?.Value, "Flare", StringComparison.OrdinalIgnoreCase);

    private static Shader? FindOrbShader()
    {
        foreach (var name in new[] { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Sprites/Default", "Standard" })
        {
            var s = Shader.Find(name);
            if (s != null) return s;
        }
        return null;
    }

    private static void BuildWispOrbs(AshlandsRebornFableBunny marker, Color? colorOverride = null,
        float sizeMul = 1f, bool proceduralOnly = false)
    {
        if (marker.Pivot == null) return;
        var h = marker.TargetHeight; // all orb dimensions scale off the configured height
        var col = colorOverride ?? WispColor;

        for (var i = 0; i < 2; i++)
        {
            var orb = new GameObject($"AR_WispOrb_{(i == 0 ? "l" : "r")}");
            orb.transform.SetParent(marker.Pivot, worldPositionStays: false);

            // Preferred visual: a stripped clone of a real wisp prefab (probed - M2 recon may
            // improve the candidate list); guaranteed fallback: a procedural additive orb.
            // Elementals force procedural: the demister ball's fixed wisp-green fights their
            // palette. Either way the orb gets a point light and a trail.
            if (proceduralOnly || TryBuildWispVisual(orb.transform, 0.22f * h * sizeMul) == null)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                TryDestroyImmediate(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(orb.transform, worldPositionStays: false);
                sphere.transform.localScale = Vector3.one * (0.18f * h * sizeMul);
                var shader = FindOrbShader();
                if (shader != null)
                {
                    var mat = new Material(shader) { color = col };
                    if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", col);
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", col * 2f);
                    }
                    sphere.GetComponent<Renderer>().material = mat;
                }
            }

            // Idle stays subtle (the first shoot bathed the whole donor in cyan); the lash
            // phases brighten the light and enable the trail in UpdateOrbs.
            var light = orb.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = col;
            light.intensity = 1f;
            light.range = 1.5f * h;
            light.shadows = LightShadows.None;

            var trail = orb.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.startWidth = 0.09f * h * sizeMul;
            trail.endWidth = 0f;
            trail.numCapVertices = 4;
            trail.emitting = false; // orbit leaves no trail; lash phases switch it on
            var trailShader = FindOrbShader();
            if (trailShader != null)
                trail.material = new Material(trailShader) { color = col };
            trail.startColor = col;
            trail.endColor = new Color(col.r, col.g, col.b, 0f);

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
    internal static void StripToVisual(GameObject clone)
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

    private enum StrikeSide { None, Left, Right, Both }

    private static void UpdateOrbs(AshlandsRebornFableBunny marker, Character source, float dt)
    {
        // NOTE: runs even with zero orbs - the lash state machine below is the shared attack
        // phase clock (ear whip, paw strike, and sit-up release all key off Flare/Track/Return).

        // Lash phases: Flare (0.25s wind-up) -> Track (follow the hidden hands until the
        // attack ends; same InAttack grace as the roll) -> Return (0.4s) -> Orbit.
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

        if (marker.Orbs.Count == 0) return;

        var h = marker.TargetHeight;
        var bunny = marker.Mode == BunnyMode.Bunny;
        var style = CurrentSwipeStyle();
        // Which orbs join the lash: elementals keep the original both-orb brightness layering
        // (their orbs ARE the hands); bunny Comets = both; bunny Wisps = the striking side
        // (side 0 = both-arms swing / not yet committed); Paws/Off = orbs never lash.
        var strikers = !bunny || style == SwipeStyle.Comets ? StrikeSide.Both
            : style != SwipeStyle.Wisps ? StrikeSide.None
            : marker.LashSide < 0 ? StrikeSide.Left
            : marker.LashSide > 0 ? StrikeSide.Right
            : StrikeSide.Both;

        foreach (var orb in marker.Orbs)
        {
            if (orb.Root == null) continue;
            var isLeft = orb.Hand != null && marker.HandL != null ? orb.Hand == marker.HandL : orb.Phase < 1f;
            var participating = strikers == StrikeSide.Both
                || (strikers == StrikeSide.Left && isLeft)
                || (strikers == StrikeSide.Right && !isLeft);
            var lashing = participating && marker.LashState != LashState.Orbit;
            var state = lashing ? marker.LashState : LashState.Orbit;

            Vector3 target;
            float lerpRate, lightIntensity;
            switch (state)
            {
                case LashState.Flare:
                    if (bunny && style == SwipeStyle.Wisps && !WindupIsFlare() && orb.Hand != null)
                    {
                        // Telegraph wind-up: glow up AT the wound-back striking hand so the
                        // player reads where the hit will come from, and when.
                        target = orb.Hand.position;
                        lerpRate = 16f;
                        lightIntensity = 6f;
                    }
                    else
                    {
                        // The original flare burst (Comets always; Wisps via WindupStyle=Flare).
                        var center = marker.Pivot!.position + Vector3.up * (0.5f * h);
                        var outward = orb.Root.position - center;
                        outward.y *= 0.3f;
                        if (outward.sqrMagnitude < 0.01f)
                            outward = marker.Pivot.right * (orb.Phase > 1f ? -1f : 1f);
                        target = center + outward.normalized * (1.9f * h);
                        lerpRate = 14f;
                        lightIntensity = 5f;
                    }
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
                    // Elemental hand orbs sit AT the hands at all times ("orbs in place of
                    // the morgen's hands"); bunny wisps orbit until a lash starts.
                    if (marker.AlwaysTrackHands && orb.Hand != null)
                    {
                        target = orb.Hand.position;
                        lerpRate = 22f;
                        lightIntensity = 1.6f;
                    }
                    else
                    {
                        target = OrbitTarget(marker, orb);
                        lerpRate = 6f;
                        lightIntensity = 1f;
                    }
                    break;
            }

            // Idle visibility (v3): with the orbit toggled off, bunny orbs exist only while
            // they participate in a lash - fade in at the telegraph, fade out after Return.
            var visible = !bunny || WispOrbitOn() || lashing;
            var wasHidden = orb.Visible < 0.05f;
            orb.Visible = Mathf.Lerp(orb.Visible, visible ? 1f : 0f,
                1f - Mathf.Exp((visible ? -12f : -5f) * dt));
            if (visible && wasHidden)
            {
                // Waking from hidden: snap to the target so the orb doesn't streak in from
                // its stale parked position, and drop any recorded trail.
                orb.Root.position = target;
                if (orb.Trail != null) orb.Trail.Clear();
            }
            orb.Root.localScale = Vector3.one * Mathf.Max(0.0001f, orb.Visible);

            orb.Root.position = Vector3.Lerp(orb.Root.position, target, 1f - Mathf.Exp(-lerpRate * dt));
            if (orb.Light != null)
                orb.Light.intensity = Mathf.Lerp(orb.Light.intensity, lightIntensity * orb.Visible,
                    1f - Mathf.Exp(-8f * dt));

            if (orb.Trail != null)
            {
                // Comets = the original streaking trails through every lash phase. Wisps keep
                // only a short whisker during the strike itself - the long comet streamers
                // were the look the user rejected.
                bool emit;
                float trailTime;
                if (!bunny || style == SwipeStyle.Comets) { emit = lashing; trailTime = 0.4f; }
                else if (style == SwipeStyle.Wisps)
                {
                    emit = lashing && marker.LashState == LashState.Track;
                    trailTime = 0.15f;
                }
                else { emit = false; trailTime = 0.4f; }
                if (orb.Trail.emitting != emit) orb.Trail.emitting = emit;
                if (Mathf.Abs(orb.Trail.time - trailTime) > 0.001f) orb.Trail.time = trailTime;
            }
        }
    }

    // ---- elemental modes (v2 M3 light / M4 lightning) -----------------------------------
    // Both replace the donor with FX anchored to the hidden Morgen bones: the skeleton keeps
    // animating (AlwaysAnimate), so a core at Chest bobs/lunges with the REAL animation and
    // limb-following bolts lash/stomp/roll exactly where the hitboxes are. Everything is
    // procedural (primitives + additive materials + Lights + LineRenderers) so no prefab
    // pick is load-bearing; the M2 FX-catalog dump exists to upgrade visuals later, not to
    // gate. All objects live under the pivot, so RevertAndDestroy cleans them up for free.

    private static readonly Color LightCoreColor = new(1f, 0.95f, 0.78f, 1f);
    private static readonly Color LightningColor = new(0.62f, 0.8f, 1f, 1f);

    /// <summary>Resolve the Morgen bones every mode anchors to. Names verified by v1 recon;
    /// a missing name shortens/skips that chain (logged) but never breaks the build.</summary>
    private static void FindMorgenBones(Character source, AshlandsRebornFableBunny marker)
    {
        var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (var t in source.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);

        byName.TryGetValue("Hand.l", out var handL); marker.HandL = handL;
        byName.TryGetValue("Hand.r", out var handR); marker.HandR = handR;
        byName.TryGetValue("Chest", out var chest); marker.Chest = chest;
        if (marker.HandL == null || marker.HandR == null)
            Plugin.Log?.LogWarning("[AR Bunny] Morgen hand bones not found - wisp lash degrades to orbit-only");

        marker.LimbChains.Clear();
        foreach (var side in new[] { "l", "r" })
        {
            AddChain(byName, marker, isArm: true, $"Shoulder.{side}", $"UpperArm.{side}", $"LowerArm.{side}", $"Hand.{side}");
            AddChain(byName, marker, isArm: false, $"UpperLeg.{side}", $"LowerLeg.{side}", $"Foot.{side}");
        }
    }

    private static void AddChain(Dictionary<string, Transform> byName, AshlandsRebornFableBunny marker,
        bool isArm, params string[] names)
    {
        var bones = names
            .Select(n => byName.TryGetValue(n, out var t) ? t : null)
            .Where(t => t != null)
            .Cast<Transform>()
            .ToArray();
        if (bones.Length >= 2) marker.LimbChains.Add(new LimbChain { Bones = bones, IsArm = isArm });
        else Plugin.Log?.LogWarning($"[AR Bunny] limb chain unresolved: {string.Join("/", names)}");
    }

    private static void BuildElemental(AshlandsRebornFableBunny marker)
    {
        if (marker.Pivot == null) return;
        var h = marker.TargetHeight;
        var light = marker.Mode == BunnyMode.LightElemental;
        var col = light ? LightCoreColor : LightningColor;
        var shader = FindOrbShader();

        // Core orb: tracks the Chest bone in UpdateElemental (world-space writes; the pivot
        // parent exists only so revert cleanup gets it).
        var core = new GameObject("AR_ElemCore");
        core.transform.SetParent(marker.Pivot, worldPositionStays: false);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        TryDestroyImmediate(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(core.transform, worldPositionStays: false);
        if (shader != null)
        {
            var mat = new Material(shader) { color = col };
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", col);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", col * 3f);
            }
            sphere.GetComponent<Renderer>().material = mat;
        }
        marker.CoreBaseScale = (light ? 0.55f : 0.4f) * h;
        core.transform.localScale = Vector3.one * marker.CoreBaseScale; // core root carries the pulse

        var pl = core.AddComponent<Light>();
        pl.type = LightType.Point;
        pl.color = col;
        pl.range = 4f * h;
        pl.intensity = light ? 5f : 3.5f;
        // The Light elemental's signature is the STARK SHADOWS it casts while pulsing.
        pl.shadows = light ? LightShadows.Soft : LightShadows.None;
        marker.CoreOrb = core.transform;
        marker.CoreLight = pl;
        core.transform.position = marker.Source != null
            ? marker.Source.transform.position + Vector3.up * (0.6f * h)
            : marker.Pivot.position;

        // Hand orbs: the M1 wisp machinery with tracking ALWAYS on - "orbs in place of the
        // Morgen's hands". Lash flare/track/return still layers brightness on attack swings.
        marker.AlwaysTrackHands = true;
        BuildWispOrbs(marker, col, sizeMul: light ? 0.8f : 0.5f, proceduralOnly: true);

        if (light)
        {
            // Beam for bite/slam. Named AR_WispOrb_beam so PhotoModePatches' framing
            // exclusion (name-prefix match) ignores it - a 20m beam would wreck auto-framing.
            var beamGo = new GameObject("AR_WispOrb_beam");
            beamGo.transform.SetParent(marker.Pivot, worldPositionStays: false);
            var lr = beamGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.22f * h;
            lr.endWidth = 0.04f * h;
            lr.numCapVertices = 4;
            if (shader != null) lr.material = new Material(shader) { color = col };
            lr.startColor = col;
            lr.endColor = new Color(col.r, col.g, col.b, 0.15f);
            lr.enabled = false;
            marker.Beam = lr;
        }
        else
        {
            // Lightning limbs: one jagged polyline per limb chain (bone points + jittered
            // midpoints). If no arm chain resolved, degrade to straight Chest->Hand bolts.
            var chains = new List<LimbChain>(marker.LimbChains);
            if (!chains.Any(c => c.IsArm) && marker.Chest != null)
            {
                if (marker.HandL != null) chains.Add(new LimbChain { Bones = new[] { marker.Chest, marker.HandL }, IsArm = true });
                if (marker.HandR != null) chains.Add(new LimbChain { Bones = new[] { marker.Chest, marker.HandR }, IsArm = true });
            }
            foreach (var chain in chains)
            {
                var go = new GameObject($"AR_ElemBolt_{(chain.IsArm ? "arm" : "leg")}");
                go.transform.SetParent(marker.Pivot, worldPositionStays: false);
                var lr = go.AddComponent<LineRenderer>();
                var points = chain.Bones.Length * 2 - 1;
                lr.positionCount = points;
                lr.startWidth = 0.09f * h;
                lr.endWidth = 0.02f * h;
                if (shader != null) lr.material = new Material(shader) { color = col };
                lr.startColor = col;
                lr.endColor = col;
                marker.Bolts.Add(new LimbBolt
                {
                    Line = lr,
                    Bones = chain.Bones,
                    IsArm = chain.IsArm,
                    Jitter = new Vector3[points],
                });
            }
            if (marker.Bolts.Count == 0)
                Plugin.Log?.LogWarning("[AR Bunny] lightning elemental: no limb chains resolved - core only");
        }
    }

    private static void UpdateElemental(AshlandsRebornFableBunny marker, Character source, float dt)
    {
        if (marker.Mode == BunnyMode.Bunny || marker.CoreOrb == null) return;
        var h = marker.TargetHeight;
        var chestPos = marker.Chest != null
            ? marker.Chest.position
            : source.transform.position + Vector3.up * (0.6f * h);
        var flash = marker.CoreFlashEnd > Time.time
            ? Mathf.Clamp01((marker.CoreFlashEnd - Time.time) / 0.5f)
            : 0f;

        if (marker.Mode == BunnyMode.LightElemental)
        {
            // Occasional blinding spikes on top of the steady expand/contract pulse.
            if (Time.time >= marker.NextFlareTime)
            {
                marker.NextFlareTime = Time.time + UnityEngine.Random.Range(4f, 9f);
                marker.CoreFlashEnd = Mathf.Max(marker.CoreFlashEnd, Time.time + 0.35f);
            }
            var pulse = 1f + 0.25f * Mathf.Sin(Time.time * 3.1f);

            // Roll = the orb IS the marble: drop to ground level and bounce along the real
            // (invisible) roll trajectory; otherwise ride the animated Chest bone.
            Vector3 target;
            if (marker.Rolling)
            {
                var bounce = Mathf.Abs(Mathf.Sin(Mathf.PI * (Time.time - marker.RollStartTime) / RollHopPeriod));
                target = source.transform.position + Vector3.up * (0.5f * marker.CoreBaseScale + 0.8f * h * bounce);
            }
            else target = chestPos;
            marker.CoreOrb.position = Vector3.Lerp(marker.CoreOrb.position, target, 1f - Mathf.Exp(-16f * dt));

            marker.CoreOrb.localScale = Vector3.one * (marker.CoreBaseScale * (pulse + 0.9f * flash));
            if (marker.CoreLight != null)
                marker.CoreLight.intensity = 5f * pulse + 22f * flash;

            if (marker.Beam != null)
            {
                var beamOn = marker.BeamEnd > Time.time;
                if (marker.Beam.enabled != beamOn) marker.Beam.enabled = beamOn;
                if (beamOn)
                {
                    marker.Beam.SetPosition(0, marker.CoreOrb.position);
                    marker.Beam.SetPosition(1, marker.CoreOrb.position + marker.BeamDir.normalized * (5f * h));
                }
            }
        }
        else
        {
            marker.CoreOrb.position = Vector3.Lerp(marker.CoreOrb.position, chestPos, 1f - Mathf.Exp(-20f * dt));
            // Crackle: flicker the light every frame, re-roll the bolt jitter every ~0.05s.
            if (marker.CoreLight != null)
                marker.CoreLight.intensity = 3.5f + UnityEngine.Random.Range(-1.2f, 1.6f) + 14f * flash;
            marker.CoreOrb.localScale = Vector3.one *
                (marker.CoreBaseScale * (1f + 0.08f * Mathf.Sin(Time.time * 17f) + 0.5f * flash));

            var reroll = Time.time >= marker.NextJitterTime;
            if (reroll) marker.NextJitterTime = Time.time + 0.05f;
            var lashing = marker.LashState == LashState.Flare || marker.LashState == LashState.Track;
            var amp = (marker.Rolling ? 0.16f : 0.07f) * h;
            foreach (var bolt in marker.Bolts)
            {
                if (bolt.Line == null || bolt.Bones.Length < 2) continue;
                var boltAmp = bolt.IsArm && lashing ? amp * 1.8f : amp;
                if (reroll)
                    for (var j = 0; j < bolt.Jitter.Length; j++)
                        bolt.Jitter[j] = j % 2 == 1 ? UnityEngine.Random.insideUnitSphere : Vector3.zero;
                for (var j = 0; j < bolt.Jitter.Length; j++)
                {
                    var basePos = j % 2 == 0
                        ? bolt.Bones[j / 2].position
                        : (bolt.Bones[j / 2].position + bolt.Bones[j / 2 + 1].position) * 0.5f;
                    bolt.Line.SetPosition(j, basePos + bolt.Jitter[j] * boltAmp);
                }
                var w = (bolt.IsArm && lashing ? 0.18f : 0.09f) * h * (1f + flash);
                if (Mathf.Abs(bolt.Line.startWidth - w) > 0.001f)
                {
                    bolt.Line.startWidth = w;
                    bolt.Line.endWidth = w * 0.25f;
                }
            }
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
                // M2 FX catwalk (log form): catalog elemental-relevant vfx prefabs + boss
                // attack EffectLists so elemental visual upgrades can be picked from evidence.
                DumpFxCatalog();
                foreach (var bossName in new[] { "Eikthyr", "GoblinKing" })
                {
                    var boss = ZNetScene.instance.GetPrefab(bossName);
                    if (boss != null) DumpEffectLists(boss);
                }
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
        // the Ashlands fallback position has rocks/ruins that block the camera). Clear sky +
        // forced midday: the v3 run 1 landed at in-game night and the shots were unreadable.
        yield return PhotoModePatches.TeleportToIslandRoutine(player);
        yield return PhotoModePatches.ForceClearSkyRoutine();
        SetDebugDaylight(true);

        var pos = FindSolidSpawn(player);

        // Make sure a live Hare exists for the animator dump (ReconTick needs a Character
        // instance; the donor clone is stripped and invisible to the scan). The dump's
        // HasState probes are what name the sit-up state.
        var harePrefab = ZNetScene.instance?.GetPrefab("Hare");
        if (harePrefab != null && !ReconDumped.Contains("Hare"))
        {
            var hare = UObject.Instantiate(harePrefab, pos + Vector3.up * 0.3f, Quaternion.identity);
            FreezeAI(hare);
            yield return new WaitForSeconds(1.5f); // ReconTick scans every 0.5s
            if (hare != null) ZNetScene.instance?.Destroy(hare);
        }

        var go = UObject.Instantiate(prefab, pos, Quaternion.identity);
        Plugin.Log?.LogInfo("[AR BunnyRecon] observation Morgen spawned");
        // The Morgen's ground-rise + sleep window runs ~14s from spawn; shooting inside it
        // catches the (suppressed) rise fx and a still-buried donor. Wait it out.
        yield return new WaitForSeconds(16f);

        var hum = go != null ? go.GetComponent<Humanoid>() : null;
        var marker = go != null ? go.GetComponent<AshlandsRebornFableBunny>() : null;
        Check(marker != null && marker.Built, "swap built on observation Morgen");
        Check(marker != null && (!(SwipeUsesWisps() || WispOrbitOn()) || marker.Orbs.Count == 2), "wisp orbs built");

        PhotoModePatches.EnableCameraOverride();

        // Forced attacks, one per attack item, two screenshots each (early + late pose) -
        // cycled across all three FableBunnyMode looks. Each mode switch fires the
        // SettingChanged -> RefreshAll path, exactly what the user's F1 rotate knob does.
        var items = hum != null ? hum.GetInventory()?.GetAllItems() : null;
        foreach (var mode in new[] { "Bunny", "LightElemental", "LightningElemental" })
        {
            if (go == null) break;
            Plugin.FableBunnyMode.Value = mode;
            yield return new WaitForSeconds(3f);
            var mMode = go != null ? go.GetComponent<AshlandsRebornFableBunny>() : null;
            Check(mMode != null && mMode.Built, $"{mode}: swap rebuilt after mode switch");
            if (go == null) break;
            go.transform.position = pos;
            PhotoModePatches.AimCameraAt(go, 60);
            yield return new WaitForSeconds(0.3f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_{mode}_idle.png"));
            yield return new WaitForSeconds(0.5f);

            if (hum == null || items == null) continue;
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
                Plugin.Log?.LogInfo($"[AR BunnyRecon] forced attack '{anim}' ({mode}) -> {ok}");
                if (!ok)
                {
                    yield return new WaitForSeconds(2f);
                    continue;
                }
                yield return new WaitForSeconds(0.35f);
                if (go == null) break;
                PhotoModePatches.AimCameraAt(go, 45);
                yield return new WaitForSeconds(0.15f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_{mode}_atk_{anim}_a.png"));
                yield return new WaitForSeconds(0.6f);
                if (go == null) break;
                PhotoModePatches.AimCameraAt(go, 45);
                yield return new WaitForSeconds(0.1f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_{mode}_atk_{anim}_b.png"));
                var tEnd = Time.time + 6f;
                while (go != null && hum != null && hum.InAttack() && Time.time < tEnd) yield return null;
                yield return new WaitForSeconds(1f);
            }
        }
        Plugin.FableBunnyMode.Value = "Bunny";
        yield return new WaitForSeconds(2f);

        // v3 swipe styles: force one swipe per style (plus an orbit-off wisp strike) and
        // capture wind-up / strike / settle frames. The "[AR Bunny] swipe side detect" log
        // lines from the mode loop above double as the seed data for SwipeSideByAnim.
        if (go != null && hum != null && items != null)
        {
            ItemDrop.ItemData? swipeItem = null;
            foreach (var it in items)
            {
                var a = it?.m_shared?.m_attack != null
                    ? FAttackAnimation?.GetValue(it.m_shared.m_attack) as string
                    : null;
                if (a != null && ClassifyAttack(a) == AttackClass.Lash) { swipeItem = it; break; }
            }
            if (swipeItem == null)
                Plugin.Log?.LogWarning("[AR Bunny] style pass: no swipe attack item found");
            else
            {
                var origStyle = Plugin.FableBunnySwipeStyle.Value;
                var origOrbit = Plugin.FableBunnyWispOrbit.Value;
                var origSitIdle = Plugin.FableBunnySitIdleValue.Value;
                // The two Paws passes double as the sit-pose sweep: idle=1 vs idle=2 name
                // the blend-tree slot that is the upright alert pose (with the blend log).
                foreach (var (styleName, orbit, sitIdle) in new[]
                             { ("Paws", true, 1f), ("Paws", true, 2f), ("Wisps", true, 1f),
                               ("Wisps", false, 1f), ("Comets", true, 1f) })
                {
                    if (go == null) break;
                    Plugin.FableBunnySwipeStyle.Value = styleName;
                    Plugin.FableBunnyWispOrbit.Value = orbit;
                    Plugin.FableBunnySitIdleValue.Value = sitIdle;
                    yield return new WaitForSeconds(2.5f); // SettingChanged -> RefreshAll rebuild
                    if (go == null) break;
                    go.transform.position = pos;
                    hum.EquipItem(swipeItem);
                    var okStyle = hum.StartAttack(player, false);
                    Plugin.Log?.LogInfo($"[AR Bunny] style pass '{styleName}' orbit={orbit} sitIdle={sitIdle} -> {okStyle}");
                    if (!okStyle) { yield return new WaitForSeconds(2f); continue; }
                    var tag = $"{styleName}{(orbit ? "" : "_noorbit")}_i{sitIdle:F0}";
                    yield return new WaitForSeconds(0.2f);
                    PhotoModePatches.AimCameraAt(go, 45);
                    yield return new WaitForSeconds(0.1f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_swipe_{tag}_windup.png"));
                    yield return new WaitForSeconds(0.45f);
                    if (go == null) break;
                    PhotoModePatches.AimCameraAt(go, 45);
                    yield return new WaitForSeconds(0.05f);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_swipe_{tag}_strike.png"));
                    yield return new WaitForSeconds(0.6f);
                    if (go == null) break;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"Morgen_swipe_{tag}_settle.png"));
                    var tStyleEnd = Time.time + 6f;
                    while (go != null && hum != null && hum.InAttack() && Time.time < tStyleEnd) yield return null;
                    yield return new WaitForSeconds(0.8f);
                }
                Plugin.FableBunnySwipeStyle.Value = origStyle;
                Plugin.FableBunnyWispOrbit.Value = origOrbit;
                Plugin.FableBunnySitIdleValue.Value = origSitIdle;
                yield return new WaitForSeconds(2f);
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
        SetDebugDaylight(false);
        Plugin.Log?.LogInfo($"[AR BunnyRecon] OBSERVATION DONE pass={pass} fail={fail}");
    }

    /// <summary>Pin the world clock to midday for the capture window (the same EnvMan debug
    /// fields the vanilla console time tools drive), reflection-safe against renames.</summary>
    private static void SetDebugDaylight(bool on)
    {
        try
        {
            var em = EnvMan.instance;
            if (em == null) return;
            var fFlag = AccessTools.Field(typeof(EnvMan), "m_debugTimeOfDay");
            var fTime = AccessTools.Field(typeof(EnvMan), "m_debugTime");
            if (fFlag == null || fTime == null)
            {
                Plugin.Log?.LogWarning("[AR BunnyRecon] EnvMan debug-time fields not found - captures keep world time");
                return;
            }
            fFlag.SetValue(em, on);
            if (on) fTime.SetValue(em, 0.5f); // midday
            Plugin.Log?.LogInfo($"[AR BunnyRecon] debug daylight {(on ? "ON (midday)" : "off")}");
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[AR BunnyRecon] daylight force error: {ex.Message}");
        }
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

    private static readonly string[] FxNameFilters =
    {
        "wisp", "demister", "torch", "dverger", "dvergr", "lighthouse", "lightning", "thunder",
        "himmin", "iolite", "gem", "eikthyr", "yagluth", "goblinking", "mistile", "staff",
        "orb", "spark", "flare",
    };

    /// <summary>M2 FX catalog: one compact line per ZNetScene prefab matching the elemental
    /// shopping-list filters - component counts + shader names, so elemental visual upgrades
    /// can be picked from the log instead of guessed.</summary>
    private static void DumpFxCatalog()
    {
        try
        {
            var scene = ZNetScene.instance;
            var prefabs = AccessTools.Field(typeof(ZNetScene), "m_prefabs")?.GetValue(scene) as List<GameObject>;
            if (prefabs == null)
            {
                Plugin.Log?.LogWarning("[AR BunnyRecon] ZNetScene.m_prefabs not readable - no FX catalog");
                return;
            }
            var sb = new StringBuilder(32768);
            sb.AppendLine($"[AR BunnyRecon] FX catalog ({prefabs.Count} prefabs scanned):");
            foreach (var p in prefabs)
            {
                if (p == null) continue;
                var n = p.name.ToLowerInvariant();
                if (!FxNameFilters.Any(f => n.Contains(f))) continue;
                var shaders = p.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials ?? Array.Empty<Material>())
                    .Where(m => m != null && m.shader != null)
                    .Select(m => m.shader.name)
                    .Distinct()
                    .Take(3);
                sb.AppendLine(
                    $"  {p.name}: L={p.GetComponentsInChildren<Light>(true).Length}" +
                    $" PS={p.GetComponentsInChildren<ParticleSystem>(true).Length}" +
                    $" LR={p.GetComponentsInChildren<LineRenderer>(true).Length}" +
                    $" TR={p.GetComponentsInChildren<TrailRenderer>(true).Length}" +
                    $" sh=[{string.Join(";", shaders)}]");
            }
            Plugin.Log?.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[AR BunnyRecon] fx catalog error: {ex.Message}");
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
            sb.AppendLine($"[AR Bunny] {prefab.name} EffectList inventory:");
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
                sb.AppendLine($"  controller={rac.name} layers={anim.layerCount}");
                // HasState probes: state names aren't enumerable at runtime, but layer-0
                // states usually share their clip's name - a TRUE here is a CrossFade-able
                // state (this is how the sit-up state gets named from evidence).
                foreach (var clip in rac.animationClips.Where(cl => cl != null).Select(cl => cl.name).Distinct().OrderBy(n => n))
                    sb.AppendLine($"  clip {clip} state?={anim.HasState(0, Animator.StringToHash(clip))}");
                foreach (var cand in new[] { "alert", "sit", "sit_up", "idle_alert", "scared", "idle2", "listen", "dig" })
                    if (anim.HasState(0, Animator.StringToHash(cand)))
                        sb.AppendLine($"  sit-candidate state MATCH: {cand}");
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
        // v3 sit-up: resolved sit/alert state hash (0 = not probed yet, -1 = none exists).
        public int SitStateHash;
    }

    /// <summary>A resolved Morgen limb bone chain (root -> extremity).</summary>
    internal sealed class LimbChain
    {
        public Transform[] Bones = Array.Empty<Transform>();
        public bool IsArm;
    }

    /// <summary>One lightning limb: a jagged LineRenderer polyline over a hidden Morgen bone
    /// chain (bone points + jittered midpoints, re-rolled every ~0.05s).</summary>
    internal sealed class LimbBolt
    {
        public LineRenderer? Line;
        public Transform[] Bones = Array.Empty<Transform>();
        public Vector3[] Jitter = Array.Empty<Vector3>();
        public bool IsArm;
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
        // v3 idle visibility weight (0..1): fades the orb out when the idle orbit is off.
        public float Visible = 1f;
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

    // Elemental modes (v2 M3/M4): core orb + beam + lightning limb bolts, all bone-anchored.
    public FableBunnyPatches.BunnyMode Mode;
    public Transform? Chest;
    public readonly List<FableBunnyPatches.LimbChain> LimbChains = new();
    public Transform? CoreOrb;
    public Light? CoreLight;
    public float CoreBaseScale;
    public float CoreFlashEnd;
    public float NextFlareTime;
    public float NextJitterTime;
    public LineRenderer? Beam;
    public Vector3 BeamDir = Vector3.forward;
    public float BeamEnd;
    public bool AlwaysTrackHands;
    public readonly List<FableBunnyPatches.LimbBolt> Bolts = new();

    // M5 procedural bone layer: ear-whip bones + the CurlAndRoll center-tumble node.
    public readonly List<Transform> EarBones = new();
    public Transform? SpinNode;
    public float RollSpinAngle;

    // v3 swipe upgrades: measured striking side, sit-up state, paw-strike chains.
    public int LashSide;                 // -1 left, +1 right, 0 both/unknown
    public bool SideCommitted;
    public bool SideFinalized;
    public bool SideCached;
    public string? PendingSideAnim;
    public float HandTravelL;
    public float HandTravelR;
    public Vector3 HandPrevL;
    public Vector3 HandPrevR;
    public bool SitEntered;
    public bool SitUsesParam;
    public bool SitClipLogged;
    public float SitUntil;
    public int SitReturnStateHash;
    public float SitProceduralUntil;
    public readonly List<Transform> PawBonesL = new();
    public readonly List<Transform> PawBonesR = new();

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
