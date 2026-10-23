using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// <summary>
/// Fable Bunny "skin" modes (FableBunnyMode = Ape / Svalt). They reuse the bunny's hide /
/// pivot / revert / lifecycle machinery and replace only the build + per-frame drive:
///
///  - Ape: a custom mesh (MorgenSkins/ape.bin, made offline: AI shape -> fitted onto the
///    Morgen's 56-bone skeleton -> Blender bone-heat weights). The mesh is bound to an
///    ape-proportioned COPY of the Morgen skeleton under the pivot. Every frame the copy takes
///    the hidden Morgen's live bone rotations, while bone offsets come from the ape fit - so the
///    ape plays every native Morgen clip (bite, swipes, rolls, slam, rise) with ape proportions,
///    and the real Morgen bones (hitbox timing) are never touched.
///  - Svalt: Therzie's Monstrum "BossSvalt_TW" loaded from an asset-bundle file (dev test:
///    MorgenSkins/monstrum.unity3d), stripped to visuals, re-textured (living bark face instead
///    of bone), tail/jaw hidden, upper body bulked. Its own Animator runs; locomotion params are
///    copied from the Morgen and each Morgen attack CLIP fires the matching Svalt trigger.
/// </summary>
internal static class FableMorgenSkins
{
    internal static bool IsSkin(FableBunnyPatches.BunnyMode m)
        => m == FableBunnyPatches.BunnyMode.Ape || m == FableBunnyPatches.BunnyMode.Svalt;

    internal static string AssetsDir =>
        Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "MorgenSkins");

    internal sealed class SkinState
    {
        public Transform? Root;
        // Ape
        public readonly List<(Transform src, Transform dst)> Ancestors = new();
        public Transform[] SrcBones = Array.Empty<Transform>();
        public Transform[] DstBones = Array.Empty<Transform>();
        public Vector3[] FitLP = Array.Empty<Vector3>();
        public Vector3[] RestLP = Array.Empty<Vector3>();
        public bool[] HasLP = Array.Empty<bool>();
        public int RootIndex = -1;
        public float ApeScale = 1f;
        public int SpineIndex = -1, ArmLIndex = -1, ArmRIndex = -1;
        public float TorsoPitch;
        // Svalt
        public GameObject? Donor;
        public Animator? DonorAnim;
        public HashSet<string> DonorParams = new();
        public HashSet<string> SrcParams = new();
        public readonly List<(Transform t, Vector3 baseScale, float factor)> ScaleBones = new();
        public string LastClip = "";
        public float LastAttackTime;
        public bool Scaled;
        public int Frames;
        public float Yaw;
    }

    // ---- asset loading ------------------------------------------------------------------

    private sealed class ApeData
    {
        public Mesh Mesh = null!;
        public string[] Bones = Array.Empty<string>();
        public Vector3[] FitLP = Array.Empty<Vector3>();
        public Vector3[] RestLP = Array.Empty<Vector3>();
        public bool[] Has = Array.Empty<bool>();
    }

    private static ApeData? _ape;
    private static bool _apeTried;
    private static readonly Dictionary<string, Texture2D?> Textures = new(StringComparer.OrdinalIgnoreCase);
    private static AssetBundle? _svaltBundle;
    private static GameObject? _svaltPrefab;
    private static bool _svaltTried;

    private static ApeData? LoadApe()
    {
        if (_apeTried) return _ape;
        _apeTried = true;
        var path = Path.Combine(AssetsDir, "ape.bin");
        try
        {
            if (!File.Exists(path)) { Plugin.Log?.LogWarning($"[Morgen Skin] ape mesh missing: {path}"); return null; }
            using var br = new BinaryReader(File.OpenRead(path));
            var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != "ARMS") { Plugin.Log?.LogWarning($"[Morgen Skin] bad ape.bin magic '{magic}'"); return null; }
            br.ReadInt32(); // version
            var nv = br.ReadInt32(); var ni = br.ReadInt32(); var nb = br.ReadInt32();
            var names = new string[nb];
            for (var i = 0; i < nb; i++) names[i] = Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));
            Vector3[] V3(int n) { var a = new Vector3[n]; for (var i = 0; i < n; i++) a[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); return a; }
            var verts = V3(nv); var norms = V3(nv);
            var uv = new Vector2[nv]; for (var i = 0; i < nv; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            var tris = new int[ni]; for (var i = 0; i < ni; i++) tris[i] = br.ReadInt32();
            var bi = new int[nv * 4]; for (var i = 0; i < bi.Length; i++) bi[i] = br.ReadInt32();
            var bw = new float[nv * 4]; for (var i = 0; i < bw.Length; i++) bw[i] = br.ReadSingle();
            var bp = new Matrix4x4[nb];
            for (var b = 0; b < nb; b++)
            {
                var m = new Matrix4x4();
                for (var r = 0; r < 4; r++) for (var c = 0; c < 4; c++) m[r, c] = br.ReadSingle();
                bp[b] = m;
            }
            var fit = V3(nb); var rest = V3(nb);
            var has = new bool[nb]; for (var i = 0; i < nb; i++) has[i] = br.ReadInt32() != 0;

            var mesh = new Mesh { name = "AR_ApeSkin" };
            if (nv > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uv; mesh.triangles = tris;
            var weights = new BoneWeight[nv];
            for (var i = 0; i < nv; i++)
                weights[i] = new BoneWeight
                {
                    boneIndex0 = bi[i * 4], boneIndex1 = bi[i * 4 + 1], boneIndex2 = bi[i * 4 + 2], boneIndex3 = bi[i * 4 + 3],
                    weight0 = bw[i * 4], weight1 = bw[i * 4 + 1], weight2 = bw[i * 4 + 2], weight3 = bw[i * 4 + 3],
                };
            mesh.boneWeights = weights; mesh.bindposes = bp; mesh.RecalculateBounds();
            _ape = new ApeData { Mesh = mesh, Bones = names, FitLP = fit, RestLP = rest, Has = has };
            Plugin.Log?.LogInfo($"[Morgen Skin] ape mesh loaded: {nv} verts, {ni / 3} tris, {nb} bones");
        }
        catch (Exception ex) { Plugin.Log?.LogError($"[Morgen Skin] ape.bin load failed: {ex}"); }
        return _ape;
    }

    private static Texture2D? LoadTex(string file)
    {
        if (Textures.TryGetValue(file, out var t)) return t;
        Texture2D? tex = null;
        try
        {
            var path = Path.Combine(AssetsDir, file);
            if (File.Exists(path))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "AR_" + file };
                // ImageConversion lives in a netstandard2.1 module this net472 project can't
                // reference at compile time; it is always loaded in-game, so bind by reflection.
                var ic = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var load = ic?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (load == null) { Plugin.Log?.LogWarning("[Morgen Skin] ImageConversion.LoadImage unavailable"); return null; }
                load.Invoke(null, new object[] { tex, File.ReadAllBytes(path) });
                tex.wrapMode = TextureWrapMode.Clamp;
            }
            else Plugin.Log?.LogWarning($"[Morgen Skin] texture missing: {path}");
        }
        catch (Exception ex) { Plugin.Log?.LogWarning($"[Morgen Skin] texture load failed {file}: {ex.Message}"); }
        Textures[file] = tex;
        return tex;
    }

    private static GameObject? LoadSvalt()
    {
        if (_svaltTried) return _svaltPrefab;
        _svaltTried = true;
        var path = Plugin.FableBunnySvaltBundle?.Value?.Trim();
        if (string.IsNullOrEmpty(path)) path = Path.Combine(AssetsDir, "monstrum.unity3d");
        try
        {
            if (!File.Exists(path)) { Plugin.Log?.LogWarning($"[Morgen Skin] Svalt bundle missing: {path}"); return null; }
            var t0 = Time.realtimeSinceStartup;
            _svaltBundle = AssetBundle.LoadFromFile(path);
            _svaltPrefab = _svaltBundle != null ? _svaltBundle.LoadAsset<GameObject>("BossSvalt_TW") : null;
            Plugin.Log?.LogInfo($"[Morgen Skin] Svalt bundle loaded in {Time.realtimeSinceStartup - t0:F1}s: prefab={(_svaltPrefab != null ? "FOUND" : "missing")}");
        }
        catch (Exception ex) { Plugin.Log?.LogError($"[Morgen Skin] Svalt bundle load failed: {ex}"); }
        return _svaltPrefab;
    }

    // ---- build ------------------------------------------------------------------------------

    internal static bool Build(AshlandsRebornFableBunny marker, Character source)
    {
        var state = new SkinState();
        marker.Skin = state;
        var root = new GameObject("AR_SkinRoot");
        root.transform.SetParent(marker.Pivot, false);
        state.Root = root.transform;
        return marker.Mode == FableBunnyPatches.BunnyMode.Ape ? BuildApe(marker, source, state) : BuildSvalt(marker, source, state);
    }

    private static bool BuildApe(AshlandsRebornFableBunny marker, Character source, SkinState st)
    {
        var data = LoadApe();
        if (data == null) return false;

        SkinnedMeshRenderer? smr = null;
        foreach (var r in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.bones != null && r.bones.Length == data.Bones.Length && (smr == null || (r.sharedMesh?.vertexCount ?? 0) > (smr.sharedMesh?.vertexCount ?? 0)))
                smr = r;
        if (smr == null) { Plugin.Log?.LogWarning("[Morgen Skin] no 56-bone Morgen SMR found"); return false; }

        // Bone order: Morgen SMR order == ape.bin order (both exported from the same SMR); verify by name.
        var byName = new Dictionary<string, Transform>();
        foreach (var b in smr.bones) if (b != null) byName[b.name] = b;
        var src = new Transform[data.Bones.Length];
        for (var i = 0; i < src.Length; i++)
            if (!byName.TryGetValue(data.Bones[i], out src[i]!)) { Plugin.Log?.LogWarning($"[Morgen Skin] bone '{data.Bones[i]}' not on Morgen"); return false; }

        var boneSet = new HashSet<Transform>(src);
        var mirrors = new Dictionary<Transform, Transform>();
        Transform Mirror(Transform t)
        {
            if (t == source.transform) return st.Root!;
            if (mirrors.TryGetValue(t, out var m)) return m;
            var parent = Mirror(t.parent);
            var go = new GameObject("AR_" + t.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = t.localPosition; go.transform.localRotation = t.localRotation; go.transform.localScale = t.localScale;
            mirrors[t] = go.transform;
            if (!boneSet.Contains(t)) st.Ancestors.Add((t, go.transform));
            return go.transform;
        }
        var dst = new Transform[src.Length];
        for (var i = 0; i < src.Length; i++) dst[i] = Mirror(src[i]);
        st.SrcBones = src; st.DstBones = dst;
        st.FitLP = data.FitLP; st.RestLP = data.RestLP; st.HasLP = data.Has;
        st.RootIndex = Array.IndexOf(data.Bones, "Root");
        st.SpineIndex = Array.IndexOf(data.Bones, "Spine");
        st.ArmLIndex = Array.IndexOf(data.Bones, "UpperArm.l");
        st.ArmRIndex = Array.IndexOf(data.Bones, "UpperArm.r");
        // The ape was fitted 60 units tall vs the Morgen's ~50: fold that in so 1.0 = Morgen height.
        st.ApeScale = (Plugin.FableBunnyApeScale?.Value ?? 1f) * (50f / 60f);
        st.TorsoPitch = Plugin.FableBunnyApeTorsoPitch?.Value ?? 45f;

        var skinGo = new GameObject("AR_ApeSkin");
        skinGo.transform.SetParent(Mirror(smr.transform.parent), false);
        skinGo.transform.localPosition = smr.transform.localPosition;
        skinGo.transform.localRotation = smr.transform.localRotation;
        skinGo.transform.localScale = smr.transform.localScale;
        var skin = skinGo.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = data.Mesh;
        skin.bones = dst;
        skin.rootBone = st.RootIndex >= 0 ? dst[st.RootIndex] : dst[0];
        skin.updateWhenOffscreen = true;
        skin.quality = SkinQuality.Bone4;

        // Material: the Morgen's own (Valheim creature shader, lighting/fog/wet consistent),
        // re-textured. The Morgen normal/metal maps use the Morgen's UVs - drop them.
        var hiddenIdx = marker.HiddenRenderers.IndexOf(smr);
        var orig = hiddenIdx >= 0 && hiddenIdx < marker.HiddenMaterials.Count ? marker.HiddenMaterials[hiddenIdx] : null;
        var mat = orig != null && orig.Length > 0 && orig[0] != null ? new Material(orig[0]) : new Material(Shader.Find("Standard"));
        var fur = (Plugin.FableBunnyApeFur?.Value ?? "Silverback").ToLowerInvariant();
        var tex = LoadTex($"ape_{fur}.png");
        if (tex != null) mat.mainTexture = tex;
        foreach (var p in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap" })
            if (mat.HasProperty(p)) mat.SetTexture(p, null);
        mat.DisableKeyword("_NORMALMAP");
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        // The Morgen material carries its own emission; on the ape it washed the fur out to pastel (run 3).
        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
        mat.DisableKeyword("_EMISSION");
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
        mat.name = "AR_ApeFur_" + fur;
        skin.sharedMaterial = mat;

        Plugin.Log?.LogInfo($"[Morgen Skin] ape built: fur={fur} scale={st.ApeScale:F2} shader='{mat.shader?.name}' bones={dst.Length} ancestors={st.Ancestors.Count}");
        return true;
    }

    private static bool BuildSvalt(AshlandsRebornFableBunny marker, Character source, SkinState st)
    {
        var prefab = LoadSvalt();
        if (prefab == null) return false;

        var holder = new GameObject("AR_SvaltHolder");
        holder.SetActive(false);
        holder.transform.SetParent(st.Root, false);
        var clone = UObject.Instantiate(prefab, holder.transform, false);
        clone.name = "AR_Svalt";
        FableBunnyPatches.StripToVisual(clone);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        st.Donor = clone;

        var palette = (Plugin.FableBunnySvaltPalette?.Value ?? "Original").ToLowerInvariant();
        var living = Plugin.FableBunnySvaltLivingFace?.Value != false;
        var bodyTex = LoadTex($"svalt_{palette}_{(living ? "living" : "raw")}.png");
        var hideJaw = Plugin.FableBunnySvaltHideJaw?.Value != false;
        var autumn = palette == "autumn";

        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials; // instanced: never touches the bundle's shared materials
            foreach (var m in mats)
            {
                if (m == null) continue;
                var sh = m.shader != null ? Shader.Find(m.shader.name) : null; // re-resolve to the game's copy
                if (sh != null) m.shader = sh;
                if (m.name.IndexOf("BossSvalt_mat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (bodyTex != null) m.mainTexture = bodyTex;
                    if (m.HasProperty("_DetailAlbedoMap")) m.SetTexture("_DetailAlbedoMap", null);
                    m.DisableKeyword("_DETAIL_MULX2");
                    if (autumn && m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.05f) * 1.6f);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", autumn ? new Color(0.85f, 0.8f, 0.75f) : new Color(0.75f, 0.75f, 0.75f));
                }
                else if (m.name.IndexOf("BossSvalt_hair", StringComparison.OrdinalIgnoreCase) >= 0 && m.HasProperty("_Color"))
                    m.SetColor("_Color", autumn ? new Color(0.55f, 0.24f, 0.07f) : new Color(0.33f, 0.16f, 0.13f));
            }
            r.materials = mats;
            var n = r.gameObject.name.ToLowerInvariant();
            if (hideJaw && (n.Contains("mandibulla") || n.Contains("tongue") || n.Contains("poison")))
            {
                r.enabled = false;
                r.forceRenderingOff = true;
            }
        }

        // Bone layer: tail collapsed, upper body bulked (applied after the Animator every frame).
        var bulk = Plugin.FableBunnySvaltBulk?.Value ?? 1.25f;
        void AddScale(string bone, float f)
        {
            var t = FindDeep(clone.transform, bone);
            if (t != null) st.ScaleBones.Add((t, t.localScale, f));
        }
        if (Plugin.FableBunnySvaltHideTail?.Value != false) AddScale("Tail1_M", 0.001f);
        if (bulk > 1.001f)
        {
            AddScale("ChestMid_M", bulk);
            AddScale("Mid3_M", Mathf.Sqrt(bulk));
            AddScale("Hip_L", Mathf.Sqrt(bulk));
            AddScale("Hip_R", Mathf.Sqrt(bulk));
        }

        // Drop the prefab's smoke/poison particle FX (run 2: a dark smoke cloud smeared the
        // body); keep the eye "flames".
        foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true))
            if (ps != null && ps.gameObject.name.IndexOf("flames", StringComparison.OrdinalIgnoreCase) < 0)
                ps.gameObject.SetActive(false);

        st.DonorAnim = clone.GetComponentInChildren<Animator>(true);
        holder.SetActive(true);
        if (st.DonorAnim != null)
        {
            st.DonorAnim.enabled = true;
            st.DonorAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var p in st.DonorAnim.parameters) st.DonorParams.Add(p.name);
        }
        if (marker.SourceAnimator != null)
            foreach (var p in marker.SourceAnimator.parameters) st.SrcParams.Add(p.name);

        Plugin.Log?.LogInfo($"[Morgen Skin] Svalt built: palette={palette} living={living} hideJaw={hideJaw} bulk={bulk:F2} " +
                            $"scaleBones={st.ScaleBones.Count} donorParams={st.DonorParams.Count} srcParams=[{string.Join(",", st.SrcParams)}]");
        return true;
    }

    private static Transform? FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    // ---- per-frame drive ----------------------------------------------------------------------

    internal static void Drive(AshlandsRebornFableBunny marker)
    {
        var st = marker.Skin;
        var source = marker.Source;
        if (st == null || source == null || marker.Pivot == null) return;
        try
        {
            if (marker.Mode == FableBunnyPatches.BunnyMode.Ape) DriveApe(st, source);
            else DriveSvalt(marker, st, source);
        }
        catch (Exception ex)
        {
            if (_driveErrors++ < 5) Plugin.Log?.LogWarning($"[Morgen Skin] drive error: {ex.Message}");
        }
    }

    private static int _driveErrors;
    private const float HipDeltaScale = 0.5f;

    private static void DriveApe(SkinState st, Character source)
    {
        DriveApeBones(st);
        // Gorilla stance: the Morgen moves with its body horizontal, which on the short-legged
        // ape read as crawling face-down (run 4). Tilt the torso up at the spine, then restore the
        // upper arms' animated world orientation so the knuckles still reach for the ground.
        if (st.TorsoPitch != 0f && st.SpineIndex >= 0)
        {
            var spine = st.DstBones[st.SpineIndex];
            Quaternion? armL = st.ArmLIndex >= 0 ? st.DstBones[st.ArmLIndex].rotation : null;
            Quaternion? armR = st.ArmRIndex >= 0 ? st.DstBones[st.ArmRIndex].rotation : null;
            spine.rotation = Quaternion.AngleAxis(-st.TorsoPitch, source.transform.right) * spine.rotation;
            if (armL.HasValue) st.DstBones[st.ArmLIndex].rotation = armL.Value;
            if (armR.HasValue) st.DstBones[st.ArmRIndex].rotation = armR.Value;
        }
    }

    private static void DriveApeBones(SkinState st)
    {
        foreach (var (src, dst) in st.Ancestors)
        {
            if (src == null || dst == null) continue;
            dst.localPosition = src.localPosition; dst.localRotation = src.localRotation; dst.localScale = src.localScale;
        }
        for (var i = 0; i < st.SrcBones.Length; i++)
        {
            var s = st.SrcBones[i]; var d = st.DstBones[i];
            if (s == null || d == null) continue;
            d.localRotation = s.localRotation;
            d.localPosition = st.HasLP[i] ? st.FitLP[i] + (s.localPosition - st.RestLP[i]) * HipDeltaScale : s.localPosition;
            d.localScale = i == st.RootIndex ? s.localScale * st.ApeScale : s.localScale;
        }
    }

    private static readonly (string clip, string trigger)[] ClipTriggers =
    {
        ("Swipe L", "attackslap0"), ("Swipe R", "attackslap1"), ("Bite", "attackbite"),
        ("BodySlam", "attackantler"), ("Bash", "attackbitegrab"), ("Taunt", "taunt"), ("Stagger", "stagger"),
    };

    private static void DriveSvalt(AshlandsRebornFableBunny marker, SkinState st, Character source)
    {
        var pivot = marker.Pivot!;
        st.Frames++;

        // Facing: Morgen facing, except while rolling (it often rolls sideways) -> travel direction.
        var vel = source.GetVelocity(); vel.y = 0f;
        var rolling = marker.Rolling && Time.time - marker.RollStartTime < 3f;
        var yaw = source.transform.eulerAngles.y;
        if (rolling && vel.sqrMagnitude > 4f) yaw = Quaternion.LookRotation(vel).eulerAngles.y;
        st.Yaw = Mathf.LerpAngle(st.Yaw == 0f ? yaw : st.Yaw, yaw, 1f - Mathf.Exp(-12f * Time.deltaTime));
        pivot.rotation = Quaternion.Euler(0f, st.Yaw, 0f);
        pivot.localPosition = new Vector3(0f, Plugin.FableBunnyYOffset?.Value ?? 0f, 0f);

        // Height-match once the donor has animated a few frames (bounds are live then).
        if (!st.Scaled && st.Frames > 3 && st.Donor != null && st.Root != null)
        {
            var b = default(Bounds); var has = false;
            foreach (var r in st.Donor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!r.enabled || r.forceRenderingOff) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            var h = has ? b.size.y / Mathf.Max(0.001f, st.Root.lossyScale.y) : 0f;
            if (h > 0.1f)
            {
                var target = (Plugin.FableBunnySvaltHeight?.Value ?? 3.6f);
                st.Root.localScale = Vector3.one * (target / h);
                st.Scaled = true;
                Plugin.Log?.LogInfo($"[Morgen Skin] Svalt height-match: raw={h:F2}m target={target:F2}m scale={target / h:F2}");
            }
        }

        var src = marker.SourceAnimator;
        var dst = st.DonorAnim;
        if (src != null && dst != null)
        {
            foreach (var p in new[] { "forward_speed", "sideway_speed", "turn_speed" })
                if (st.SrcParams.Contains(p) && st.DonorParams.Contains(p)) dst.SetFloat(p, src.GetFloat(p));
            if (rolling && st.DonorParams.Contains("forward_speed"))
                dst.SetFloat("forward_speed", Mathf.Max(dst.GetFloat("forward_speed"), vel.magnitude));

            // The slam never surfaces as its own layer-0 clip (run 3 clip log), so key it off the
            // Morgen's current attack animation name instead.
            var atk = marker.LastAttackAnim;
            if (marker.AttackStartTime != st.LastAttackTime)
            {
                st.LastAttackTime = marker.AttackStartTime;
                if (atk != null && atk.IndexOf("slam", StringComparison.OrdinalIgnoreCase) >= 0 && st.DonorParams.Contains("attackantler"))
                {
                    dst.SetTrigger("attackantler");
                    if (_trigLog++ < 40) Plugin.Log?.LogInfo($"[Morgen Skin] Morgen attack '{atk}' -> Svalt 'attackantler'");
                }
            }

            var info = src.GetCurrentAnimatorClipInfo(0);
            var clip = info.Length > 0 && info[0].clip != null ? info[0].clip.name : "";
            if (clip != st.LastClip)
            {
                st.LastClip = clip;
                if (SeenClips.Add(clip)) Plugin.Log?.LogInfo($"[Morgen Skin] Morgen clip seen: '{clip}'");
                foreach (var (key, trig) in ClipTriggers)
                {
                    if (clip.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (st.DonorParams.Contains(trig)) dst.SetTrigger(trig);
                    if (_trigLog++ < 40) Plugin.Log?.LogInfo($"[Morgen Skin] Morgen clip '{clip}' -> Svalt '{trig}'");
                    break;
                }
            }
        }

        foreach (var (t, baseScale, f) in st.ScaleBones)
            if (t != null) t.localScale = baseScale * f;
    }

    private static int _trigLog;
    private static readonly System.Reflection.FieldInfo? FCurAttack = HarmonyLib.AccessTools.Field(typeof(Humanoid), "m_currentAttack");
    private static readonly System.Reflection.FieldInfo? FAtkAnim = HarmonyLib.AccessTools.Field(typeof(Attack), "m_attackAnimation");

    private static string? CurrentAttackAnim(Character c)
    {
        if (c is not Humanoid h || FCurAttack == null || FAtkAnim == null) return null;
        var a = FCurAttack.GetValue(h);
        return a != null && h.InAttack() ? FAtkAnim.GetValue(a) as string : null;
    }
    private static readonly HashSet<string> SeenClips = new();

    // =====================================================================================
    // Dev self-test (FableBunnySkinTest): spawn a Morgen on the test island, cycle the skin
    // variants through the live config path, force every attack, capture screenshots.
    // =====================================================================================

    private static float _worldLoadedAt = -1f;
    private static bool _testFired;

    internal static void TestTick()
    {
        if (Plugin.FableBunnySkinTest?.Value != true) return;
        if (Player.m_localPlayer == null || ZNetScene.instance == null) { _worldLoadedAt = -1f; return; }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;
        if (_testFired || Time.time - _worldLoadedAt < 20f) return;
        _testFired = true;
        Plugin.Instance.StartCoroutine(TestRoutine());
    }

    private static IEnumerator TestRoutine()
    {
        var player = Player.m_localPlayer;
        var prefab = ZNetScene.instance?.GetPrefab("Morgen");
        if (player == null || prefab == null) yield break;
        var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "AR_SkinTest");
        Directory.CreateDirectory(dir);
        Plugin.Log?.LogInfo("[AR SkinTest] start");

        yield return PhotoModePatches.TeleportToIslandRoutine(player);
        yield return PhotoModePatches.ForceClearSkyRoutine();
        FableBunnyPatches.SetDebugDaylight(true);
        var pos = player.transform.position + player.transform.forward * 5f;
        if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 10f)) pos = hit.point + Vector3.up * 0.3f;

        var origMode = Plugin.FableBunnyMode.Value;
        var origFur = Plugin.FableBunnyApeFur.Value;
        var origApeScale = Plugin.FableBunnyApeScale.Value;
        var origPitch = Plugin.FableBunnyApeTorsoPitch.Value;
        var origPal = Plugin.FableBunnySvaltPalette.Value;
        var origBulk = Plugin.FableBunnySvaltBulk.Value;
        var origLiving = Plugin.FableBunnySvaltLivingFace.Value;
        var origTail = Plugin.FableBunnySvaltHideTail.Value;
        var origJaw = Plugin.FableBunnySvaltHideJaw.Value;

        var variants = (Plugin.FableBunnySkinTestVariants?.Value ?? "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

        // Awake variant + live AI: a frozen Morgen never leaves its buried spawn sleep, and
        // a sleeping Humanoid refuses StartAttack (run 1: every forced attack -> False).
        var awake = ZNetScene.instance?.GetPrefab("Morgen_NonSleeping");
        var go = UObject.Instantiate(awake ?? prefab, pos, Quaternion.LookRotation(player.transform.position - pos));
        Plugin.Log?.LogInfo($"[AR SkinTest] spawned {(awake != null ? "Morgen_NonSleeping" : "Morgen (sleeping variant)")}");
        yield return new WaitForSeconds(awake != null ? 5f : 16f);
        // Freeze AI once awake (run 2: the live AI wandered and rolls left the platform), and
        // keep the player alive - forced attacks still target and hit the player.
        if (go != null) foreach (var ai in go.GetComponentsInChildren<BaseAI>()) ai.enabled = false;
        var wasGod = player.InGodMode();
        player.SetGodMode(true);
        var hum = go != null ? go.GetComponent<Humanoid>() : null;
        var items = hum != null ? hum.GetInventory()?.GetAllItems() : null;
        PhotoModePatches.EnableCameraOverride();

        foreach (var v in variants)
        {
            if (go == null) break;
            // Variant syntax: Ape:<fur>[:<scale>] | Svalt:<palette>:<bulk>[:raw]
            var parts = v.Split(':');
            Plugin.FableBunnySvaltLivingFace.Value = true;
            Plugin.FableBunnySvaltHideTail.Value = true;
            Plugin.FableBunnySvaltHideJaw.Value = true;
            if (parts[0].Equals("Ape", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.FableBunnyApeFur.Value = parts.Length > 1 ? parts[1] : "Silverback";
                Plugin.FableBunnyApeScale.Value = parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 1f;
                Plugin.FableBunnyApeTorsoPitch.Value = parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pt) ? pt : 45f;
                Plugin.FableBunnyMode.Value = "Ape";
            }
            else
            {
                Plugin.FableBunnySvaltPalette.Value = parts.Length > 1 ? parts[1] : "Original";
                Plugin.FableBunnySvaltBulk.Value = parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bk) ? bk : 1.25f;
                if (parts.Length > 3 && parts[3] == "raw")
                {
                    Plugin.FableBunnySvaltLivingFace.Value = false;
                    Plugin.FableBunnySvaltHideTail.Value = false;
                    Plugin.FableBunnySvaltHideJaw.Value = false;
                }
                Plugin.FableBunnyMode.Value = "Svalt";
            }
            Plugin.Instance.StartCoroutine(Nudge()); // force a rebuild even if the mode string was unchanged
            yield return new WaitForSeconds(4f);
            if (go == null) break;
            var tag = v.Replace(':', '_').Replace('.', 'p');
            go.transform.position = pos;
            var m = go.GetComponent<AshlandsRebornFableBunny>();
            Plugin.Log?.LogInfo($"[AR SkinTest] variant {v}: built={(m != null && m.Built)} mode={(m != null ? m.Mode.ToString() : "-")}");

            foreach (var (label, yawOff) in new[] { ("front34", 35), ("side", 90), ("back34", 150) })
            {
                if (go == null) break;
                go.transform.position = pos;
                PhotoModePatches.AimCameraAt(go, (int)go.transform.eulerAngles.y + 180 + yawOff);
                yield return new WaitForSeconds(0.35f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{tag}_idle_{label}.png"));
                yield return new WaitForSeconds(0.25f);
            }
            if (go != null)
            {
                AimFace(go);
                yield return new WaitForSeconds(0.35f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{tag}_idle_face.png"));
                yield return new WaitForSeconds(0.25f);
                PhotoModePatches.EnableCameraOverride();
            }

            if (hum == null || items == null) continue;
            for (var i = 0; i < items.Count && go != null; i++)
            {
                go.transform.position = pos;
                go.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(player.transform.position - pos, Vector3.up));
                var body = go.GetComponent<Rigidbody>();
                if (body != null) body.linearVelocity = Vector3.zero;
                yield return new WaitForSeconds(0.3f);
                if (go == null) break;
                var item = items[i];
                hum.EquipItem(item);
                var anim = "atk" + i;
                try
                {
                    if (item.m_shared.m_attack != null)
                        anim = HarmonyLib.AccessTools.Field(typeof(Attack), "m_attackAnimation")?.GetValue(item.m_shared.m_attack) as string ?? anim;
                }
                catch { }
                var ok = hum.StartAttack(player, false);
                Plugin.Log?.LogInfo($"[AR SkinTest] {v} forced '{anim}' -> {ok}");
                if (!ok) { yield return new WaitForSeconds(1.5f); continue; }
                foreach (var (suffix, wait) in new[] { ("a", 0.3f), ("b", 0.4f) })
                {
                    yield return new WaitForSeconds(wait);
                    if (go == null) break;
                    PhotoModePatches.AimCameraAt(go, (int)go.transform.eulerAngles.y + 180 + 40);
                    yield return new WaitForSeconds(0.05f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{tag}_{anim}_{suffix}.png"));
                }
                var tEnd = Time.time + 6f;
                while (go != null && hum != null && hum.InAttack() && Time.time < tEnd) yield return null;
                yield return new WaitForSeconds(0.8f);
            }
        }

        if (go != null) ZNetScene.instance?.Destroy(go);
        player.SetGodMode(wasGod);
        Plugin.FableBunnyApeFur.Value = origFur;
        Plugin.FableBunnyApeScale.Value = origApeScale;
        Plugin.FableBunnyApeTorsoPitch.Value = origPitch;
        Plugin.FableBunnySvaltPalette.Value = origPal;
        Plugin.FableBunnySvaltBulk.Value = origBulk;
        Plugin.FableBunnySvaltLivingFace.Value = origLiving;
        Plugin.FableBunnySvaltHideTail.Value = origTail;
        Plugin.FableBunnySvaltHideJaw.Value = origJaw;
        Plugin.FableBunnyMode.Value = origMode;
        PhotoModePatches.ClearCameraOverride();
        FableBunnyPatches.SetDebugDaylight(false);
        File.WriteAllText(Path.Combine(dir, "DONE.txt"), DateTime.Now.ToString("s"));
        Plugin.Log?.LogInfo("[AR SkinTest] DONE");
    }

    /// <summary>Head-and-shoulders framing from the front-left, ~upper quarter of the visible
    /// body (the shared close-up framing zoomed into the antlers/tail on these big creatures).</summary>
    private static void AimFace(GameObject go)
    {
        var b = default(Bounds); var has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r.forceRenderingOff || r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return;
        var focus = new Vector3(go.transform.position.x, b.min.y + b.size.y * 0.72f, go.transform.position.z)
                    + go.transform.forward * (b.extents.z * 0.3f);
        var dirv = Quaternion.Euler(0f, 25f, 0f) * go.transform.forward;
        var camPos = focus + dirv * Mathf.Clamp(b.size.y * 1.8f, 4f, 10f) + Vector3.up * 0.4f;
        PhotoModePatches.SetCameraOverride(camPos, Quaternion.LookRotation(focus - camPos, Vector3.up));
    }

    private static IEnumerator Nudge()
    {
        yield return null;
        FableBunnyPatches.RefreshAll();
    }
}
