using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace AshlandsReborn.Patches;

/// <summary>
/// How the terrain override fades green Meadows terrain into ash/lava.
/// </summary>
internal enum TransitionStyle
{
    /// <summary>Original behavior, byte-identical: binary AshLands stamp at mask > 0.1 on a
    /// stride-2 subsampled grid. Kept as the user's revert path.</summary>
    Legacy,

    /// <summary>Grass fades into scorched mud (Swamp channel), then mud fades into ash,
    /// with blurred mask + noise-jittered band edges for organic contours.</summary>
    MudBlend,

    /// <summary>MudBlend's exact fade geometry, but the chunk material's diffuse texture
    /// array is cloned with the swamp/mud slice overwritten by an ash-family slice, so the
    /// same vertex colors render green fading directly into ash - no mud terrain, and no
    /// yellow either (alpha still stays 0 until red saturates). See TERRAIN_NO_MUD_PLAN.md
    /// for why a direct green-to-ash vertex fade is impossible in color space alone.</summary>
    AshBlend,

    /// <summary>Green runs almost to the molten lava; only a tight mud/ash rim at the edge.</summary>
    GrassToLava,

    /// <summary>Dev calibration: paints known vertex-color ramps in strips so a single
    /// screenshot reveals what each interpolation path actually renders as.</summary>
    DebugGradient,
}

/// <summary>
/// Style engine for the Ashlands->Meadows terrain transition.
///
/// The terrain shader reads vertex colors as biome texture selectors: Meadows (0,0,0,0),
/// Swamp (255,0,0,0), Plains (0,0,0,255), AshLands (255,0,0,255) - ALPHA is the Plains
/// (yellow) selector. Legacy stamps the full AshLands color directly against pure Meadows
/// vertices, so GPU interpolation across boundary triangles sweeps through mid-alpha
/// (a yellow Plains fringe), and its binary per-vertex threshold snaps contours to the
/// 1m vertex lattice (the 90/45-degree stair-steps).
///
/// The styled paths avoid both: the fade routes through the Swamp red channel first
/// (alpha stays 0 until red is saturated, so Plains cannot activate in the fade band),
/// the lava mask is box-blurred before banding, and band thresholds are jittered with
/// deterministic world-space Perlin noise so contours wander organically and agree
/// across chunk borders.
/// </summary>
internal static class TerrainTransition
{
    private static readonly int ShaderAshlandsVariationCol = Shader.PropertyToID("_AshlandsVariationCol");
    private static readonly Color OliveTint = new(0.4f, 0.5f, 0.3f, 1f);
    private static Color? _vanillaVariationCol;

    // Band edges are anchored to the ash hold H (TransitionAshHold) so bands live
    // entirely below it: MudBlend ramps the red channel (grass -> scorched mud) over
    // [H-W, H-W/2] and alpha (mud -> ash) over [H-W/2, H] with W = TransitionFadeWidth;
    // GrassToLava uses a fixed tight rim (R over [H-0.06, H-0.035], A over [H-0.035, H],
    // jitter halved). EVERY ramp segment must span >= ~1.5-2 vertices at the skirt's
    // decay rate - a sub-vertex ramp renders as 1m lattice steps (run-3/4 findings: first
    // the A-ramp painted maroon steps, then the 0.9m R-ramp stepped the mud->green edge).
    // The A-ramp ends exactly at H with 255, so band output meets the raw-mask hold rule
    // (EvaluateColor) with no seam at the hold contour. Grass cutoffs sit at a fixed
    // fraction of the R-ramp.
    private const float MudGrassFraction = 0.48f;
    private const float LavaRimR = 0.06f, LavaRimAWidth = 0.035f;
    private const float LavaGrassFraction = 0.2f;
    private const float LavaJitterFactor = 0.5f;

    internal static float AshHold => Mathf.Clamp(Plugin.TransitionAshHold?.Value ?? 0.2f, 0.05f, 0.55f);
    private static float FadeWidth => Mathf.Clamp(Plugin.TransitionFadeWidth?.Value ?? 0.15f, 0.05f, 0.5f);

    // Ash-skirt spatial widths: how many meters the full band fade spans around a lava
    // feature regardless of how sharp the paint-mask cliff is. Mask-value bands alone
    // collapse to under one triangle at 1-vertex-wide lava channels (the blur dilutes a
    // lone 0.7 cell to ~0.03), re-showing lattice stair-steps (run-2 finding).
    private const float MudSkirtMeters = 4f;
    private const float LavaSkirtMeters = 3.5f;

    private static float BandWidth(TransitionStyle style) =>
        style == TransitionStyle.GrassToLava ? LavaRimR : FadeWidth;

    internal static float SkirtDecayPerMeter(TransitionStyle style) =>
        BandWidth(style) / (style == TransitionStyle.GrassToLava ? LavaSkirtMeters : MudSkirtMeters);

    internal static int SkirtRadiusCells(TransitionStyle style) =>
        Mathf.CeilToInt(style == TransitionStyle.GrassToLava ? LavaSkirtMeters : MudSkirtMeters) + 1;

    /// <summary>
    /// Grayscale dilation of the hold-capped raw mask with linear per-meter decay
    /// (separable max-plus passes; Manhattan falloff). The result is >= min(raw, hold)
    /// everywhere and projects the hold outward so the ash-to-green fade always has
    /// spatial width; feeding it into the band via max() preserves the AshHold invariant.
    /// Pure grid math on the padded cross-chunk grid, so neighboring chunks agree.
    /// </summary>
    internal static float[] BuildSkirt(float[] rawGrid, int gridSide, float hold, float decayPerCell, int radius)
    {
        var skirt = new float[rawGrid.Length];
        for (var i = 0; i < rawGrid.Length; i++)
            skirt[i] = Math.Min(rawGrid[i], hold);

        var tmp = (float[])skirt.Clone();
        for (var r = 0; r < gridSide; r++)
        {
            for (var c = 0; c < gridSide; c++)
            {
                var best = tmp[r * gridSide + c];
                for (var k = 1; k <= radius; k++)
                {
                    var drop = decayPerCell * k;
                    if (c - k >= 0) best = Math.Max(best, tmp[r * gridSide + c - k] - drop);
                    if (c + k < gridSide) best = Math.Max(best, tmp[r * gridSide + c + k] - drop);
                }
                skirt[r * gridSide + c] = best;
            }
        }
        for (var c = 0; c < gridSide; c++)
        {
            for (var r = 0; r < gridSide; r++)
            {
                var best = skirt[r * gridSide + c];
                for (var k = 1; k <= radius; k++)
                {
                    var drop = decayPerCell * k;
                    if (r - k >= 0) best = Math.Max(best, skirt[(r - k) * gridSide + c] - drop);
                    if (r + k < gridSide) best = Math.Max(best, skirt[(r + k) * gridSide + c] - drop);
                }
                tmp[r * gridSide + c] = best;
            }
        }
        return tmp;
    }

    internal static TransitionStyle Current
    {
        get
        {
            var raw = Plugin.TerrainTransitionStyle?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return TransitionStyle.Legacy;
            return Enum.TryParse<TransitionStyle>(raw!.Trim(), true, out var style)
                ? style
                : TransitionStyle.Legacy;
        }
    }

    /// <summary>
    /// Builds a (side + 2*pad)^2 grid of vegetation-mask samples covering the chunk's
    /// vertex lattice plus <paramref name="pad"/> rings, so a blur kernel of radius
    /// pad-1 never reads unpadded cells. Ring cells sample the owning neighbor chunk
    /// (blur kernels then agree across chunk borders - no seams); when the neighbor
    /// isn't loaded yet we fall back to this chunk's clamped edge sample, which
    /// self-heals via the existing Heightmap_OnEnable_Postfix neighbor poke.
    /// </summary>
    internal static float[] BuildMaskGrid(Heightmap hmap, List<Vector3> vertices, int side, int pad)
    {
        var gridSide = side + 2 * pad;
        var grid = new float[gridSide * gridSide];

        var origin = hmap.transform.TransformPoint(vertices[0]);
        // Zone heightmaps are axis-aligned with uniform lattice spacing; derive the step
        // from the mesh rather than assuming 1m. Only x/z matter for the mask lookup.
        var stepX = vertices.Count > 1
            ? hmap.transform.TransformPoint(vertices[1]) - origin
            : Vector3.right;
        var stepZ = vertices.Count > side
            ? hmap.transform.TransformPoint(vertices[side]) - origin
            : Vector3.forward;
        stepX.y = 0f;
        stepZ.y = 0f;

        Heightmap? ringCache = null;
        for (var gr = 0; gr < gridSide; gr++)
        {
            for (var gc = 0; gc < gridSide; gc++)
            {
                var col = gc - pad;
                var row = gr - pad;
                var world = origin + stepX * col + stepZ * row;

                float v;
                if (col >= 0 && col < side && row >= 0 && row < side)
                {
                    v = hmap.GetVegetationMask(world);
                }
                else
                {
                    var owner = ringCache != null && ringCache.IsPointInside(world)
                        ? ringCache
                        : Heightmap.FindHeightmap(world);
                    if (owner != null && owner != hmap) ringCache = owner;
                    v = owner != null ? owner.GetVegetationMask(world) : hmap.GetVegetationMask(world);
                }
                grid[gr * gridSide + gc] = v;
            }
        }
        return grid;
    }

    /// <summary>Separable box blur, in place. Ring cells near the grid edge pick up
    /// clamped bias, but with pad = radius + 1 that bias never propagates into the
    /// interior side x side cells actually used for coloring.</summary>
    internal static void Smooth(float[] grid, int gridSide, int radius)
    {
        if (radius <= 0) return;
        var tmp = new float[grid.Length];
        var window = 2 * radius + 1;

        for (var r = 0; r < gridSide; r++)
        {
            for (var c = 0; c < gridSide; c++)
            {
                var sum = 0f;
                for (var k = -radius; k <= radius; k++)
                    sum += grid[r * gridSide + Mathf.Clamp(c + k, 0, gridSide - 1)];
                tmp[r * gridSide + c] = sum / window;
            }
        }
        for (var r = 0; r < gridSide; r++)
        {
            for (var c = 0; c < gridSide; c++)
            {
                var sum = 0f;
                for (var k = -radius; k <= radius; k++)
                    sum += tmp[Mathf.Clamp(r + k, 0, gridSide - 1) * gridSide + c];
                grid[r * gridSide + c] = sum / window;
            }
        }
    }

    /// <summary>
    /// Deterministic world-space threshold jitter so band contours wander organically.
    /// Pure world coords means adjacent chunks agree exactly (no seams). The +10000
    /// domain offset keeps Perlin inputs positive across the whole +-10500 world
    /// (Mathf.PerlinNoise mirrors at negative coordinates).
    /// </summary>
    internal static float Jitter(float wx, float wz)
    {
        var strength = Plugin.TransitionNoiseStrength?.Value ?? 0.08f;
        if (strength <= 0f) return 0f;
        var scale = Plugin.TransitionNoiseScale?.Value ?? 0.08f;
        return (Mathf.PerlinNoise(wx * scale + 10000f, wz * scale + 10000f) - 0.5f) * strength;
    }

    internal static Color32 EvaluateColor(TransitionStyle style, float rawMask, float mask,
        float skirt, float wx, float wz, int col, int row, int side)
    {
        if (style == TransitionStyle.DebugGradient)
            return DebugColor(col, row, side);

        // AshHold invariant: any vertex whose RAW (unblurred, unjittered) mask is
        // at/above the hold renders full vanilla ash, so the shader keeps drawing its
        // lava rim/cracks with the visible edge exactly where the ground turns deadly
        // (raw >= 0.6 is Heightmap.IsLava; the hold caps at 0.55) - noise can never
        // expose lava. Below the hold the band input is max(blurred + jittered, skirt +
        // half jitter): the skirt ramps up to exactly the hold at the contour, so the
        // binary gate has no jump, and raw itself must NOT feed the band - its per-vertex
        // lattice grain painted 1m color steps across the whole mid-band (run-3 finding).
        var hold = AshHold;
        if (rawMask >= hold) return new Color32(255, 0, 0, 255);

        var jf = style == TransitionStyle.GrassToLava ? LavaJitterFactor : 1f;
        var jitter = Jitter(wx, wz) * jf;
        var m = Mathf.Max(mask + jitter, skirt + jitter * 0.5f);

        if (style == TransitionStyle.GrassToLava)
            return BandColor(m, hold - LavaRimR, hold - LavaRimAWidth, hold - LavaRimAWidth, hold);

        var w = FadeWidth;
        return BandColor(m, hold - w, hold - w / 2f, hold - w / 2f, hold);
    }

    private static Color32 BandColor(float m, float rStart, float rEnd, float aStart, float aEnd)
    {
        var r = (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 255f, Mathf.InverseLerp(rStart, rEnd, m)));
        var a = (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 255f, Mathf.InverseLerp(aStart, aEnd, m)));
        return new Color32(r, 0, 0, a);
    }

    /// <summary>
    /// Seven constant-z calibration strips per chunk, ramping west->east. The AshHold
    /// rule is deliberately NOT applied to this style: strips 3-5 must paint non-ash over
    /// the real lava pool to answer how the shader's rim/cracks degrade off full ash.
    ///   strip 0: (R,0,0,0)     - pure Swamp ramp (what does the mud fade band look like?)
    ///   strip 1: (0,0,0,A)     - pure alpha ramp (does mid-alpha alone render yellow Plains?)
    ///   strip 2: (255,0,0,A)   - the MudBlend hypothesis band (does saturated R suppress it?)
    ///   strip 3: (0,0,0,0)     - all Meadows, even over lava (does lava glow without ash color?)
    ///   strip 4: (255,0,0,128) - constant half-ash over lava (do rim/cracks render dimmed?)
    ///   strip 5: (255,0,0,0)   - constant swamp over lava (do rim/cracks render over mud?)
    ///   strip 6: (0,G,0,0)     - Mountain gray-rock ramp (StoneAsh style candidate)
    /// </summary>
    private static Color32 DebugColor(int col, int row, int side)
    {
        var strip = Mathf.Clamp(row * 7 / side, 0, 6);
        var ramp = (byte)Mathf.RoundToInt(255f * col / Math.Max(1, side - 1));
        return strip switch
        {
            0 => new Color32(ramp, 0, 0, 0),
            1 => new Color32(0, 0, 0, ramp),
            2 => new Color32(255, 0, 0, ramp),
            3 => new Color32(0, 0, 0, 0),
            4 => new Color32(255, 0, 0, 128),
            5 => new Color32(255, 0, 0, 0),
            _ => new Color32(0, ramp, 0, 0),
        };
    }

    /// <summary>Grass placement rule shared with ClutterSystemPatches for the styled
    /// paths: grass only where the green band clearly dominates, using a skirt-mirrored
    /// mask + the SAME jitter so the grass boundary wanders with the terrain bands. The
    /// raw hold guard is belt+suspenders on top of the band cutoffs (which move with the
    /// hold): grass can never place on ground the hold renders as vanilla ash.</summary>
    internal static bool AllowGrassAt(Heightmap hmap, Vector3 point)
    {
        var style = Current;
        var hold = AshHold;
        if (hmap.GetVegetationMask(point) >= hold) return false;

        // Mirror the terrain's ash skirt with point samples: nearby lava projects
        // outward with the same per-meter decay, so grass stops mid-fade instead of
        // sprouting on the gray skirt around thin lava features.
        var mask = SkirtedMask(hmap, point, hold, SkirtDecayPerMeter(style));

        if (style == TransitionStyle.GrassToLava)
        {
            var cutoff = hold - LavaRimR + (LavaRimR - LavaRimAWidth) * LavaGrassFraction;
            return mask + Jitter(point.x, point.z) * LavaJitterFactor < cutoff;
        }
        var w = FadeWidth;
        var mudCutoff = hold - w + w / 2f * MudGrassFraction;
        return mask + Jitter(point.x, point.z) < mudCutoff;
    }

    private static readonly Vector3[] SkirtProbeOffsets =
    {
        new(2f, 0f, 0f), new(-2f, 0f, 0f), new(0f, 0f, 2f), new(0f, 0f, -2f),
        new(4f, 0f, 0f), new(-4f, 0f, 0f), new(0f, 0f, 4f), new(0f, 0f, -4f),
    };

    private static float SkirtedMask(Heightmap hmap, Vector3 point, float hold, float decayPerMeter)
    {
        var best = hmap.GetVegetationMask(point);
        foreach (var off in SkirtProbeOffsets)
        {
            var q = point + off;
            var owner = hmap.IsPointInside(q) ? hmap : Heightmap.FindHeightmap(q);
            if (owner == null) continue;
            var projected = Math.Min(owner.GetVegetationMask(q), hold) - decayPerMeter * off.magnitude;
            if (projected > best) best = projected;
        }
        return best;
    }

    /// <summary>
    /// Per-style _AshlandsVariationCol. The vanilla value is cached from the first
    /// material instance touched this session (instances are fresh clones of the shared
    /// material, so the first touch still holds the vanilla color) - DebugGradient
    /// restores it so raw texture-layer colors are observable.
    /// </summary>
    internal static void ApplyVariationCol(Material mat)
    {
        if (mat == null || !mat.HasProperty(ShaderAshlandsVariationCol)) return;
        _vanillaVariationCol ??= mat.GetColor(ShaderAshlandsVariationCol);

        mat.SetColor(ShaderAshlandsVariationCol,
            Current == TransitionStyle.DebugGradient ? _vanillaVariationCol.Value : OliveTint);
    }

    /// <summary>Per-style material state, applied per chunk per rebuild: the variation
    /// tint plus, for AshBlend, the patched diffuse texture array (every other style must
    /// get the vanilla array back - the F1 dropdown and the photo harness swap styles
    /// live, so styles must not leak into each other).</summary>
    internal static void ApplyStyleMaterial(Material mat)
    {
        if (mat == null) return;
        ApplyVariationCol(mat);
        ApplyDiffuseArray(mat);
    }

    // --- AshBlend diffuse-array patch ---
    //
    // The terrain shader picks _DiffuseArrayTex slices purely from vertex color; the
    // swamp/mud overlay is slice 3, albedo-only (recon: SHADER_SLICE_MAPPING.md, asm
    // lines 382-397), so a cloned array with an ash slice copied over slice 3 turns
    // MudBlend's mud band into a direct green->ash fade. The default source is slice 13
    // (the lighter ash-pair texture), NOT slice 7 (main ash): the near-black slice 7
    // renders the fade band so much darker than the pale full-ash hold zone that the
    // binary AshHold gate reads as high-contrast 1m stair-steps wherever a raw-mask
    // cliff pokes gated vertices into the band (v3 run-1 finding); slice 13 puts band
    // and hold in the same tonal family and the gate disappears into the ash mottling.
    // Graphics.CopyTexture is GPU-side and ignores isReadable; the clone is built once
    // per session (all chunks share it, so neighboring chunks agree - no seams) and
    // cached along with the vanilla array reference for revert.
    private static readonly int ShaderDiffuseArray = Shader.PropertyToID("_DiffuseArrayTex");
    private static Texture2DArray? _vanillaDiffuseArray;
    private static Texture2DArray? _patchedDiffuseArray;
    private static string? _patchedSpec;
    private static bool _patchFailedLogged;
    private static int _restoreLogCount;
    private static readonly HashSet<Texture2DArray> AllPatchedArrays = new();

    /// <summary>Drops the cached patched array so the next AshBlend rebuild reconstructs
    /// it from the current AshBlendSwapSlices value. The old clone is intentionally not
    /// destroyed - distant, not-yet-rebuilt chunks may still render it (it stays tracked
    /// in AllPatchedArrays for RestoreVanillaArray); a dev tuning change leaks one 1 MB
    /// GPU texture per change until session end, which is acceptable.</summary>
    internal static void InvalidatePatchedArray()
    {
        _patchedDiffuseArray = null;
        _patchedSpec = null;
        _patchFailedLogged = false;
    }

    /// <summary>Restore path for chunks rebuilt while the override is inactive
    /// (MasterSwitch off / the harness's vanilla ground-truth pass): if the material
    /// still references a patched array or carries the styled olive variation tint,
    /// put the vanilla state back. Both restores are no-ops on materials this mod
    /// never touched (array must be a tracked patched clone; tint must equal the
    /// exact OliveTint we set).</summary>
    internal static void RestoreVanillaMaterial(Material mat)
    {
        if (mat == null) return;
        RestoreVanillaArray(mat);

        if (_vanillaVariationCol.HasValue && mat.HasProperty(ShaderAshlandsVariationCol)
            && mat.GetColor(ShaderAshlandsVariationCol) == OliveTint)
            mat.SetColor(ShaderAshlandsVariationCol, _vanillaVariationCol.Value);
    }

    private static void RestoreVanillaArray(Material mat)
    {
        if (_vanillaDiffuseArray == null || AllPatchedArrays.Count == 0) return;
        if (!mat.HasProperty(ShaderDiffuseArray)) return;
        if (mat.GetTexture(ShaderDiffuseArray) is Texture2DArray current && AllPatchedArrays.Contains(current))
        {
            mat.SetTexture(ShaderDiffuseArray, _vanillaDiffuseArray);
            if (_restoreLogCount++ < 3)
                Plugin.Log?.LogInfo("[Ashlands Reborn] AshBlend: restored vanilla diffuse array on an override-off chunk rebuild");
        }
    }

    private static void ApplyDiffuseArray(Material mat)
    {
        if (!mat.HasProperty(ShaderDiffuseArray)) return;
        if (mat.GetTexture(ShaderDiffuseArray) is not Texture2DArray current) return;

        // Material instances are fresh clones of the shared Heightmap material, so the
        // first instance touched this session still references the vanilla array.
        if (_vanillaDiffuseArray == null)
        {
            if (AllPatchedArrays.Contains(current)) return; // unreachable before capture; belt+suspenders
            _vanillaDiffuseArray = current;
        }

        if (Current == TransitionStyle.AshBlend)
        {
            var patched = GetOrBuildPatchedArray();
            if (patched != null && current != patched)
                mat.SetTexture(ShaderDiffuseArray, patched);
        }
        else if (current != _vanillaDiffuseArray)
        {
            mat.SetTexture(ShaderDiffuseArray, _vanillaDiffuseArray);
        }
    }

    private static Texture2DArray? GetOrBuildPatchedArray()
    {
        var spec = Plugin.AshBlendSwapSlices?.Value ?? "3:13";
        if (_patchedDiffuseArray != null && _patchedSpec == spec) return _patchedDiffuseArray;
        var src = _vanillaDiffuseArray;
        if (src == null) return null;

        try
        {
            // Recon: 256x256 x16, BC7 sRGB, single mip. Built generically anyway so a
            // game update changing the format keeps working (CopyTexture only needs
            // src/dst to match, which a same-format clone guarantees).
            var flags = src.mipmapCount > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None;
            var clone = new Texture2DArray(src.width, src.height, src.depth, src.graphicsFormat, flags, src.mipmapCount)
            {
                name = src.name + "_AshBlend",
                filterMode = src.filterMode,
                wrapMode = src.wrapMode,
                anisoLevel = src.anisoLevel,
            };
            for (var slice = 0; slice < src.depth; slice++)
                for (var mip = 0; mip < src.mipmapCount; mip++)
                    Graphics.CopyTexture(src, slice, mip, clone, slice, mip);

            foreach (var pair in spec.Split(','))
            {
                var parts = pair.Split(':');
                if (parts.Length != 2
                    || !int.TryParse(parts[0].Trim(), out var dst)
                    || !int.TryParse(parts[1].Trim(), out var srcSlice)
                    || dst < 0 || dst >= src.depth || srcSlice < 0 || srcSlice >= src.depth)
                {
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] AshBlendSwapSlices: ignoring bad pair '{pair}'");
                    continue;
                }
                for (var mip = 0; mip < src.mipmapCount; mip++)
                    Graphics.CopyTexture(src, srcSlice, mip, clone, dst, mip);
            }

            _patchedDiffuseArray = clone;
            _patchedSpec = spec;
            AllPatchedArrays.Add(clone);
            Plugin.Log?.LogInfo($"[Ashlands Reborn] AshBlend: built patched diffuse array ({spec}) "
                + $"{src.width}x{src.height}x{src.depth} {src.graphicsFormat}");
            return clone;
        }
        catch (Exception e)
        {
            // Fail soft: AshBlend then renders exactly like MudBlend (vanilla array).
            if (!_patchFailedLogged)
            {
                _patchFailedLogged = true;
                Plugin.Log?.LogError($"[Ashlands Reborn] AshBlend: patched array build failed - falling back to vanilla array. {e}");
            }
            return null;
        }
    }
}
