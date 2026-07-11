using System;
using System.Collections.Generic;
using UnityEngine;

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
    // [H-W, H-W/3] and alpha (mud -> ash) over [H-W/3, H] with W = TransitionFadeWidth;
    // GrassToLava uses a fixed tight rim (R over [H-0.06, H-0.02], A over [H-0.02, H],
    // jitter halved). The A-ramp ends exactly at H with 255, so band output meets the
    // raw-mask hold rule (EvaluateColor) with no seam at the hold contour. Grass cutoffs
    // sit at a fixed fraction of the R-ramp (values preserve the v1 cutoff shapes).
    private const float MudGrassFraction = 0.48f;
    private const float LavaRimR = 0.06f, LavaRimA = 0.02f;
    private const float LavaGrassFraction = 0.2f;
    private const float LavaJitterFactor = 0.5f;

    internal static float AshHold => Mathf.Clamp(Plugin.TransitionAshHold?.Value ?? 0.35f, 0.05f, 0.55f);
    private static float FadeWidth => Mathf.Clamp(Plugin.TransitionFadeWidth?.Value ?? 0.25f, 0.05f, 0.5f);

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
        float wx, float wz, int col, int row, int side)
    {
        if (style == TransitionStyle.DebugGradient)
            return DebugColor(col, row, side);

        // AshHold invariant: the band input is max(blurred + jittered, RAW) mask, and the
        // ash ramp ends exactly at the hold with 255 - so any vertex whose raw mask is
        // at/above the hold renders full vanilla ash and the shader keeps drawing its
        // lava rim/cracks with the visible edge exactly where the ground turns deadly
        // (raw >= 0.6 is Heightmap.IsLava; the hold caps at 0.55). Taking the max instead
        // of a binary raw-threshold override keeps the invariant while following the
        // paint mask's own gradient at sharp pool edges - a binary stamp re-introduced
        // the 1m lattice stair-steps the blur exists to kill (run-1 finding).
        var hold = AshHold;
        if (style == TransitionStyle.GrassToLava)
            return BandColor(Mathf.Max(mask + Jitter(wx, wz) * LavaJitterFactor, rawMask),
                hold - LavaRimR, hold - LavaRimA, hold - LavaRimA, hold);

        var w = FadeWidth;
        return BandColor(Mathf.Max(mask + Jitter(wx, wz), rawMask),
            hold - w, hold - w / 3f, hold - w / 3f, hold);
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
    /// paths: grass only where the green band clearly dominates, using the raw mask +
    /// the SAME jitter so the grass boundary wanders with the terrain bands. The raw
    /// hold guard is belt+suspenders on top of the band cutoffs (which move with the
    /// hold): grass can never place on ground the hold renders as vanilla ash.</summary>
    internal static bool AllowGrassAt(Heightmap hmap, Vector3 point)
    {
        var style = Current;
        var mask = hmap.GetVegetationMask(point);
        var hold = AshHold;
        if (mask >= hold) return false;

        if (style == TransitionStyle.GrassToLava)
        {
            var cutoff = hold - LavaRimR + (LavaRimR - LavaRimA) * LavaGrassFraction;
            return mask + Jitter(point.x, point.z) * LavaJitterFactor < cutoff;
        }
        var w = FadeWidth;
        var mudCutoff = hold - w + w * 2f / 3f * MudGrassFraction;
        return mask + Jitter(point.x, point.z) < mudCutoff;
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
}
