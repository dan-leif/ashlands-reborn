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

    /// <summary>Legacy's look (binary Meadows/full-AshLands stamp, no fade band) but
    /// thresholded on the styled path's blurred + skirted + Perlin-jittered field, so the
    /// contour wanders as a smooth organic line instead of 90/45-degree lattice rectangles.
    /// The thin (~1 triangle) yellow interpolation fringe along the line is accepted - it is
    /// the GPU crossing the (t,0,0,t) diagonal, inherent to any binary stamp.</summary>
    LegacySmooth,

    /// <summary>Grass fades into scorched mud (Swamp channel), then mud fades into ash,
    /// with blurred mask + noise-jittered band edges for organic contours.</summary>
    MudBlend,

    /// <summary>MudBlend's exact fade geometry, but the chunk material's diffuse texture
    /// array is cloned with the swamp/mud slice overwritten by an ash-family slice, so the
    /// same vertex colors render green fading directly into ash - no mud terrain, and no
    /// yellow either (alpha still stays 0 until red saturates). See TERRAIN_NO_MUD_PLAN.md
    /// for why a direct green-to-ash vertex fade is impossible in color space alone.</summary>
    AshBlend,

    /// <summary>The "dug rock strip" transition (TERRAIN_TRANSITION_V4_PLAN.md): GrassToLava's
    /// tight-rim band geometry, but the rim renders as gray rock instead of mud - the chunk's
    /// diffuse array is cloned with the swamp slice overwritten per RockBlendSwapSlices
    /// (default the base-rock-scales slice 5, matching the user's pickaxe-dug reference strip
    /// at (149,-9600)).</summary>
    RockBlend,

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
    // LegacySmooth: grass stops this far (mask units) short of the binary stamp line.
    private const float LegacySmoothGrassMargin = 0.02f;

    internal static float AshHold => Mathf.Clamp(Plugin.TransitionAshHold?.Value ?? 0.2f, 0.05f, 0.55f);
    private static float FadeWidth => Mathf.Clamp(Plugin.TransitionFadeWidth?.Value ?? 0.15f, 0.05f, 0.5f);

    // Ash-skirt spatial widths: how many meters the full band fade spans around a lava
    // feature regardless of how sharp the paint-mask cliff is. Mask-value bands alone
    // collapse to under one triangle at 1-vertex-wide lava channels (the blur dilutes a
    // lone 0.7 cell to ~0.03), re-showing lattice stair-steps (run-2 finding).
    private const float MudSkirtMeters = 4f;
    private const float LavaSkirtMeters = 3.5f;

    /// <summary>Styles that use GrassToLava's tight lava-rim band geometry (RockBlend is the
    /// same rim rendered as rock; its dev toggle widens it to MudBlend's band for comparison).</summary>
    private static bool UsesLavaRim(TransitionStyle style) =>
        style == TransitionStyle.GrassToLava
        || (style == TransitionStyle.RockBlend && !(Plugin.RockBlendWideBand?.Value ?? false));

    private static float BandWidth(TransitionStyle style) =>
        UsesLavaRim(style) ? LavaRimR : FadeWidth;

    internal static float SkirtDecayPerMeter(TransitionStyle style) =>
        BandWidth(style) / (UsesLavaRim(style) ? LavaSkirtMeters : MudSkirtMeters);

    /// <summary>LegacySmooth's stamp threshold sits a ~2.5-cell guard margin BELOW the ash
    /// hold. Run-1 finding: stamping at the hold itself exposes the raw AshHold gate as 1m
    /// lattice stair-steps wherever a raw-mask cliff (thin lava channel) pokes gated
    /// vertices outside the smooth field's contour - the blur dilutes the field below the
    /// hold right where raw is at/above it. Band styles hide the gate because their A-ramp
    /// reaches 255 exactly at the hold; a binary stamp has no partial alpha to hide behind,
    /// so its region must strictly CONTAIN the gate region: the blurred skirt droops at
    /// most ~2 cells x decay below the hold over any raw>=hold vertex (worst case: isolated
    /// single-cell feature under a radius-2 blur), hence the margin. Net effect at defaults
    /// is a threshold of ~0.106 - almost exactly Legacy's own 0.1 stamp level. The hold/2
    /// floor keeps extreme knob combos (min hold + max fade width) from stamping ash on
    /// half the map.</summary>
    internal static float LegacySmoothThreshold =>
        Mathf.Max(AshHold - 2.5f * SkirtDecayPerMeter(TransitionStyle.LegacySmooth), AshHold * 0.5f);

    internal static int SkirtRadiusCells(TransitionStyle style) =>
        Mathf.CeilToInt(UsesLavaRim(style) ? LavaSkirtMeters : MudSkirtMeters) + 1;

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

    /// <summary>Second, higher-frequency jitter octave (4x base scale, 3/4 amplitude),
    /// LegacySmooth only. A binary stamp renders lattice-quantized edges by construction;
    /// the base ~12m-wavelength noise wanders the contour but leaves long straight
    /// vertex-runs intact at 1m scale (v4 run-2 finding, worst along the skirt's
    /// distance-field contours near thin lava channels). The short octave breaks those
    /// runs into irregular single-vertex wander that reads as texture, not staircase.</summary>
    internal static float Jitter2(float wx, float wz)
    {
        var strength = Plugin.TransitionNoiseStrength?.Value ?? 0.08f;
        if (strength <= 0f) return 0f;
        var scale = (Plugin.TransitionNoiseScale?.Value ?? 0.08f) * 4f;
        return (Mathf.PerlinNoise(wx * scale + 20000f, wz * scale + 20000f) - 0.5f) * strength * 0.75f;
    }

    /// <summary>Maximum magnitude of Jitter + Jitter2, for positive-biasing.</summary>
    private static float JitterAmplitude => (Plugin.TransitionNoiseStrength?.Value ?? 0.08f) * 0.875f;

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

        var jf = UsesLavaRim(style) ? LavaJitterFactor : 1f;
        var jitter = Jitter(wx, wz) * jf;
        var m = Mathf.Max(mask + jitter, skirt + jitter * 0.5f);

        // LegacySmooth: the same binary stamp as Legacy, but on the smooth field at a
        // guard-margin threshold (see LegacySmoothThreshold) so the stamp region strictly
        // contains the raw AshHold gate region - no exposed 1m gate lattice. Two-octave
        // jitter (see Jitter2) breaks the residual 1m quantization into irregular wander;
        // the skirt term's jitter is positive-BIASED (never below the un-jittered skirt)
        // because the gate-cover guarantee needs the blurred skirt alone to reach t over
        // every raw>=hold vertex. The un-jittered last condition keeps extreme knob combos
        // (low hold + high noise strength) from stamping stray ash speckles far from any
        // lava; near the contour the smooth field is ~t, far above t/2.
        if (style == TransitionStyle.LegacySmooth)
        {
            var t = LegacySmoothThreshold;
            var j2 = jitter + Jitter2(wx, wz);
            var ms = Mathf.Max(mask + j2, skirt + (j2 + JitterAmplitude) * 0.5f);
            return ms >= t && Mathf.Max(mask, skirt) >= t * 0.5f
                ? new Color32(255, 0, 0, 255)
                : new Color32(0, 0, 0, 0);
        }

        if (UsesLavaRim(style))
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

        if (style == TransitionStyle.LegacySmooth)
        {
            // Mirror the binary stamp: same field, same two-octave jitter, same
            // guard-margin threshold, stopping a small margin short of the line so
            // grass never pokes through the ash edge.
            return mask + Jitter(point.x, point.z) + Jitter2(point.x, point.z)
                < LegacySmoothThreshold - LegacySmoothGrassMargin;
        }
        if (UsesLavaRim(style))
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
    /// restores it so raw texture-layer colors are observable. AshBlend's tint is
    /// configurable (a lighter variation color brightens the slice-15 overlay in the
    /// full-ash zone toward the band tone - v4 idea 3); every applied value is tracked
    /// so RestoreVanillaMaterial recognizes styled materials even after knob changes.
    /// </summary>
    internal static void ApplyVariationCol(Material mat)
    {
        if (mat == null || !mat.HasProperty(ShaderAshlandsVariationCol)) return;
        _vanillaVariationCol ??= mat.GetColor(ShaderAshlandsVariationCol);

        var target = Current switch
        {
            TransitionStyle.DebugGradient => _vanillaVariationCol.Value,
            TransitionStyle.AshBlend => Plugin.AshBlendVariationColor?.Value ?? OliveTint,
            _ => OliveTint,
        };
        if (Current != TransitionStyle.DebugGradient)
            AppliedVariationCols.Add(target);
        mat.SetColor(ShaderAshlandsVariationCol, target);
    }

    private static readonly HashSet<Color> AppliedVariationCols = new() { OliveTint };

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

    // --- Band diffuse-array patch (AshBlend / RockBlend) ---
    //
    // The terrain shader picks _DiffuseArrayTex slices purely from vertex color; the
    // swamp/mud overlay is slice 3, albedo-only (recon: SHADER_SLICE_MAPPING.md, asm
    // lines 382-397), so a cloned array with another slice copied over slice 3 turns
    // MudBlend's mud band into a different-material fade. AshBlend defaults to slice 13
    // (the lighter ash-pair texture), NOT slice 7 (main ash): the near-black slice 7
    // renders the fade band so much darker than the pale full-ash hold zone that the
    // binary AshHold gate reads as high-contrast 1m stair-steps wherever a raw-mask
    // cliff pokes gated vertices into the band (v3 run-1 finding); slice 13 puts band
    // and hold in the same tonal family and the gate disappears into the ash mottling.
    // RockBlend defaults to slice 5 (base rock scales - the user's dug-strip reference).
    //
    // Two clone flavors (v4, TERRAIN_TRANSITION_V4_PLAN.md idea 3):
    //  - Compressed: byte-identical BC7 slice copies via Graphics.CopyTexture (GPU-side,
    //    ignores isReadable). Used whenever every tone knob is neutral - the v3 path.
    //  - Uncompressed graded: any single existing slice is darker and flatter than the
    //    full-ash composite (7+13 per-pixel blend + slice-15 variation overlay + detail
    //    normal), so the band texture is synthesized instead: each BC7 slice is GPU
    //    decoded (CopyTexture -> Texture2D -> Blit -> ReadPixels; sRGB RT keeps the
    //    round trip byte-faithful), the band slice is graded in byte space (brightness x
    //    tint, optional grass-slice mix), and the whole clone is rebuilt as RGBA32
    //    (CopyTexture cannot mix BC7 and RGBA32 in one array). ~4 MB VRAM, one-time.
    //
    // Clones are cached per parameter descriptor and shared by every chunk (neighboring
    // chunks agree - no seams); the vanilla array reference is kept for revert.
    private static readonly int ShaderDiffuseArray = Shader.PropertyToID("_DiffuseArrayTex");
    private static Texture2DArray? _vanillaDiffuseArray;
    private static bool _patchFailedLogged;
    private static int _restoreLogCount;
    private static readonly Dictionary<string, Texture2DArray> PatchedArrayCache = new();
    private static readonly HashSet<Texture2DArray> AllPatchedArrays = new();

    /// <summary>Per-style band-array parameters: which slices are swapped and how the
    /// swapped-in band texture is graded. Neutral grading routes to the byte-identical
    /// compressed clone path.</summary>
    private readonly struct BandArrayParams
    {
        public readonly string Swaps;
        public readonly float Brightness;
        public readonly Color Tint;
        public readonly float GrassMix;
        public readonly string ConfigName;

        public BandArrayParams(string swaps, float brightness, Color tint, float grassMix, string configName)
        {
            Swaps = swaps;
            Brightness = brightness;
            Tint = tint;
            GrassMix = grassMix;
            ConfigName = configName;
        }

        public bool GradingNeutral =>
            Mathf.Approximately(Brightness, 1f) && Tint == Color.white && GrassMix <= 0f;

        public string CacheKey(bool uncompressed) =>
            $"{Swaps}|b{Brightness:F3}|t{Tint.r:F3},{Tint.g:F3},{Tint.b:F3}|m{GrassMix:F3}|{(uncompressed ? "u" : "c")}";
    }

    private static BandArrayParams? CurrentBandParams(TransitionStyle style) => style switch
    {
        TransitionStyle.AshBlend => new BandArrayParams(
            Plugin.AshBlendSwapSlices?.Value ?? "3:13",
            Plugin.AshBlendBandBrightness?.Value ?? 1f,
            Plugin.AshBlendBandTint?.Value ?? Color.white,
            Plugin.AshBlendBandMix?.Value ?? 0f,
            "AshBlendSwapSlices"),
        TransitionStyle.RockBlend => new BandArrayParams(
            Plugin.RockBlendSwapSlices?.Value ?? "3:5",
            Plugin.RockBlendBandBrightness?.Value ?? 1f,
            Color.white,
            0f,
            "RockBlendSwapSlices"),
        _ => null,
    };

    /// <summary>Drops the cached patched arrays so the next rebuild reconstructs them
    /// from the current swap/tone config values. Old clones are intentionally not
    /// destroyed - distant, not-yet-rebuilt chunks may still render them (they stay
    /// tracked in AllPatchedArrays for RestoreVanillaArray); a dev tuning change leaks
    /// one 1-4 MB GPU texture per change until session end, which is acceptable.</summary>
    internal static void InvalidatePatchedArray()
    {
        PatchedArrayCache.Clear();
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
            && AppliedVariationCols.Contains(mat.GetColor(ShaderAshlandsVariationCol)))
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

        var bandParams = CurrentBandParams(Current);
        if (bandParams.HasValue)
        {
            var patched = GetOrBuildPatchedArray(bandParams.Value);
            if (patched != null && current != patched)
                mat.SetTexture(ShaderDiffuseArray, patched);
        }
        else if (current != _vanillaDiffuseArray)
        {
            mat.SetTexture(ShaderDiffuseArray, _vanillaDiffuseArray);
        }
    }

    private static Texture2DArray? GetOrBuildPatchedArray(BandArrayParams p)
    {
        var src = _vanillaDiffuseArray;
        if (src == null) return null;

        var uncompressed = !p.GradingNeutral || (Plugin.TerrainArrayUncompressed?.Value ?? false);
        var key = p.CacheKey(uncompressed);
        if (PatchedArrayCache.TryGetValue(key, out var cached) && cached != null) return cached;

        var swaps = ParseSwaps(p.Swaps, src.depth, p.ConfigName);
        try
        {
            var clone = uncompressed
                ? BuildGradedClone(src, swaps, p.Brightness, p.Tint, p.GrassMix)
                : BuildCompressedClone(src, swaps);
            PatchedArrayCache[key] = clone;
            AllPatchedArrays.Add(clone);
            Plugin.Log?.LogInfo($"[Ashlands Reborn] Terrain band array built ({key}) "
                + $"{src.width}x{src.height}x{src.depth} {(uncompressed ? "RGBA32 graded" : src.graphicsFormat.ToString())}");
            return clone;
        }
        catch (Exception e)
        {
            if (!_patchFailedLogged)
            {
                _patchFailedLogged = true;
                Plugin.Log?.LogError($"[Ashlands Reborn] Terrain band array build failed ({key}). {e}");
            }
            if (uncompressed)
            {
                // Fail soft in stages: an ungraded compressed swap still beats no swap.
                try
                {
                    var clone = BuildCompressedClone(src, swaps);
                    PatchedArrayCache[key] = clone;
                    AllPatchedArrays.Add(clone);
                    Plugin.Log?.LogWarning($"[Ashlands Reborn] Terrain band array: graded build failed, using ungraded compressed clone for ({key})");
                    return clone;
                }
                catch
                {
                    // fall through to vanilla
                }
            }
            return null; // style then renders exactly like MudBlend (vanilla array)
        }
    }

    private static List<KeyValuePair<int, int>> ParseSwaps(string spec, int depth, string configName)
    {
        var swaps = new List<KeyValuePair<int, int>>();
        foreach (var pair in spec.Split(','))
        {
            if (string.IsNullOrWhiteSpace(pair)) continue;
            var parts = pair.Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), out var dst)
                || !int.TryParse(parts[1].Trim(), out var srcSlice)
                || dst < 0 || dst >= depth || srcSlice < 0 || srcSlice >= depth)
            {
                Plugin.Log?.LogWarning($"[Ashlands Reborn] {configName}: ignoring bad pair '{pair}'");
                continue;
            }
            swaps.Add(new KeyValuePair<int, int>(dst, srcSlice));
        }
        return swaps;
    }

    /// <summary>v3 path, byte-identical slices: same format as vanilla, GPU-side copies only.</summary>
    private static Texture2DArray BuildCompressedClone(Texture2DArray src, List<KeyValuePair<int, int>> swaps)
    {
        // Recon: 256x256 x16, BC7 sRGB, single mip. Built generically anyway so a
        // game update changing the format keeps working (CopyTexture only needs
        // src/dst to match, which a same-format clone guarantees).
        var flags = src.mipmapCount > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None;
        var clone = new Texture2DArray(src.width, src.height, src.depth, src.graphicsFormat, flags, src.mipmapCount)
        {
            name = src.name + "_ARBand",
            filterMode = src.filterMode,
            wrapMode = src.wrapMode,
            anisoLevel = src.anisoLevel,
        };
        for (var slice = 0; slice < src.depth; slice++)
            for (var mip = 0; mip < src.mipmapCount; mip++)
                Graphics.CopyTexture(src, slice, mip, clone, slice, mip);

        foreach (var pair in swaps)
            for (var mip = 0; mip < src.mipmapCount; mip++)
                Graphics.CopyTexture(src, pair.Value, mip, clone, pair.Key, mip);
        return clone;
    }

    /// <summary>Uncompressed rebuild with the swapped-in band slice graded in byte space
    /// (matching how the PIL tone target is measured). All 16 slices are GPU-decoded
    /// because CopyTexture cannot mix BC7 and RGBA32 within one array.</summary>
    private static Texture2DArray BuildGradedClone(Texture2DArray src, List<KeyValuePair<int, int>> swaps,
        float brightness, Color tint, float grassMix)
    {
        var mips = src.mipmapCount > 1;
        var clone = new Texture2DArray(src.width, src.height, src.depth, TextureFormat.RGBA32, mips, linear: false)
        {
            name = src.name + "_ARBandGraded",
            filterMode = src.filterMode,
            wrapMode = src.wrapMode,
            anisoLevel = src.anisoLevel,
        };

        var pixels = new Color32[src.depth][];
        for (var slice = 0; slice < src.depth; slice++)
            pixels[slice] = DecodeSlice(src, slice);

        foreach (var pair in swaps)
        {
            var graded = (Color32[])pixels[pair.Value].Clone();
            GradeBandPixels(graded, grassMix > 0f ? pixels[0] : null, brightness, tint, grassMix);
            pixels[pair.Key] = graded;
        }

        for (var slice = 0; slice < src.depth; slice++)
            clone.SetPixels32(pixels[slice], slice, 0);
        clone.Apply(updateMipmaps: mips, makeNoLongerReadable: true);
        return clone;
    }

    /// <summary>BC7 -> readable RGBA32 bytes: CopyTexture the slice into a same-format
    /// Texture2D (GPU-side, ignores isReadable), let the GPU decompress it through an
    /// sRGB Blit, and read the pixels back. sRGB RT + sRGB texture keeps the round trip
    /// byte-faithful in the linear-color-space player.</summary>
    private static Color32[] DecodeSlice(Texture2DArray src, int slice)
    {
        var compressed = new Texture2D(src.width, src.height, src.graphicsFormat, TextureCreationFlags.None);
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prevActive = RenderTexture.active;
        try
        {
            Graphics.CopyTexture(src, slice, 0, compressed, 0, 0);
            Graphics.Blit(compressed, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, linear: false);
            readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            readable.Apply(false);
            var result = readable.GetPixels32();
            UnityEngine.Object.Destroy(readable);
            return result;
        }
        finally
        {
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(compressed);
        }
    }

    /// <summary>Byte-space grading: optional grass-slice mix ("singed grass" bridge tonally
    /// between the two sides), then brightness x tint. Alpha is left untouched.</summary>
    private static void GradeBandPixels(Color32[] band, Color32[]? grass, float brightness, Color tint, float grassMix)
    {
        for (var i = 0; i < band.Length; i++)
        {
            float r = band[i].r, g = band[i].g, b = band[i].b;
            if (grass != null && i < grass.Length)
            {
                r = Mathf.Lerp(r, grass[i].r, grassMix);
                g = Mathf.Lerp(g, grass[i].g, grassMix);
                b = Mathf.Lerp(b, grass[i].b, grassMix);
            }
            band[i] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r * brightness * tint.r), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g * brightness * tint.g), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b * brightness * tint.b), 0, 255),
                band[i].a);
        }
    }
}
