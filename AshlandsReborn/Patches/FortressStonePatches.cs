using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Fortress Stone Recolor: recolors the dark-gray Ashlands stone toward a "thriving
/// beautiful castle" look. Three independent material families, each with its own config
/// section: Fortress (the charred fortress piece set), Ruins (world-gen Ashlands ruins
/// stone), and Grausten (player-built stone, default off = vanilla).
///
/// No Harmony patches (DevAutoLoadPatches precedent): the target materials are SHARED
/// Material assets - Fortresswall1 alone covers every fortress wall/floor/gate/stair piece
/// including all _frac fractured variants - so mutating the shared material recolors every
/// placed instance instantly, destruction states included. Lightening = driving the HDR
/// _Color multiplier above its vanilla value (both the Standard shader on the fortress mats
/// and Custom/Piece on ruins/grausten multiply albedo by _Color).
///
/// The fortress family additionally owns the glowing red cracks (_EmissionColor on
/// Fortresswall1/Fortresswall_Big_mat; _EmissionMap FortressWall1_e): a style dropdown
/// defaults them to warm ember gold. Glow is only ever written to materials whose VANILLA
/// emission is non-black, so ruins/grausten can never gain a glow.
///
/// Apply discipline (TerrainTransition's revert contract): resolve once per session from
/// ZNetScene's prefab list, cache each matched material's original _Color/_EmissionColor,
/// and only ever write to cached materials - ApplyAll always computes from the cached
/// originals (idempotent, no compounding), RestoreAll puts every original back.
/// </summary>
internal static class FortressStonePatches
{
    private sealed class StoneFamily
    {
        public string Name = "";
        public Func<bool> Enabled = () => false;
        public Func<float> Brightness = () => 1f;
        public Func<float> TintR = () => 1f;
        public Func<float> TintG = () => 1f;
        public Func<float> TintB = () => 1f;
        public Func<string> MaterialsCsv = () => "";
        public bool HasGlow;
        public readonly List<Material> Materials = new();
        public int PrefabCount;
    }

    private struct OriginalState
    {
        public Color Color;
        public bool HasColor;
        public Color Emission;
        public bool HasEmission;
    }

    private static readonly StoneFamily[] Families =
    {
        new()
        {
            Name = "fortress",
            Enabled = () => Plugin.FortressStoneEnable?.Value == true,
            Brightness = () => Plugin.FortressStoneBrightness?.Value ?? 1f,
            TintR = () => Plugin.FortressStoneTintR?.Value ?? 1f,
            TintG = () => Plugin.FortressStoneTintG?.Value ?? 1f,
            TintB = () => Plugin.FortressStoneTintB?.Value ?? 1f,
            MaterialsCsv = () => Plugin.FortressStoneMaterials?.Value ?? "",
            HasGlow = true,
        },
        new()
        {
            Name = "ruins",
            Enabled = () => Plugin.RuinsStoneEnable?.Value == true,
            Brightness = () => Plugin.RuinsStoneBrightness?.Value ?? 1f,
            TintR = () => Plugin.RuinsStoneTintR?.Value ?? 1f,
            TintG = () => Plugin.RuinsStoneTintG?.Value ?? 1f,
            TintB = () => Plugin.RuinsStoneTintB?.Value ?? 1f,
            MaterialsCsv = () => Plugin.RuinsStoneMaterials?.Value ?? "",
            HasGlow = false,
        },
        new()
        {
            Name = "grausten",
            Enabled = () => Plugin.GraustenStoneEnable?.Value == true,
            Brightness = () => Plugin.GraustenStoneBrightness?.Value ?? 1f,
            TintR = () => Plugin.GraustenStoneTintR?.Value ?? 1f,
            TintG = () => Plugin.GraustenStoneTintG?.Value ?? 1f,
            TintB = () => Plugin.GraustenStoneTintB?.Value ?? 1f,
            MaterialsCsv = () => Plugin.GraustenStoneMaterials?.Value ?? "",
            HasGlow = false,
        },
    };

    /// <summary>Built-in warm ember gold for the fortress crack glow (user-approved default).</summary>
    private static readonly Color EmberGlow = new(1.0f, 0.55f, 0.18f);

    // Original _Color/_EmissionColor per resolved material. Apply/restore operate exclusively
    // on materials present here - never blind writes, so other mods' materials stay untouched.
    private static readonly Dictionary<Material, OriginalState> Originals = new();

    private static bool _resolved;
    // The ZNetScene we resolved against: a world reload creates a fresh instance (and may
    // have freed/reloaded the material assets), so a mismatch re-arms the resolve.
    private static ZNetScene? _resolvedScene;
    private static bool _catalogDumped;
    private static float _periodicTimer;

    // ---- resolve -------------------------------------------------------------------------

    /// <summary>
    /// Scan every ZNetScene prefab's renderers and match shared-material base names against
    /// each family's CSV (Unity's " (Instance)"/" (Clone)" suffixes stripped). Dedupes by
    /// Material reference and caches originals. Safe to call again after ClearResolved().
    /// </summary>
    private static void ResolveMaterials()
    {
        if (ZNetScene.instance == null) return;
        _resolved = true;
        _resolvedScene = ZNetScene.instance;

        var entryLists = new List<string>[Families.Length];
        var matchedEntries = new HashSet<string>[Families.Length];
        var prefabSets = new HashSet<GameObject>[Families.Length];
        for (var f = 0; f < Families.Length; f++)
        {
            Families[f].Materials.Clear();
            Families[f].PrefabCount = 0;
            entryLists[f] = ParseCsv(Families[f].MaterialsCsv());
            matchedEntries[f] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            prefabSets[f] = new HashSet<GameObject>();
        }

        // Near-miss catalog for the no-match dump: any stone-ish material name seen anywhere.
        var nearMisses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null) continue;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    var baseName = BaseName(mat.name);

                    if (baseName.IndexOf("fortress", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        baseName.IndexOf("grausten", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        baseName.IndexOf("ashland", StringComparison.OrdinalIgnoreCase) >= 0)
                        nearMisses.Add(baseName);

                    for (var f = 0; f < Families.Length; f++)
                    {
                        var hit = false;
                        foreach (var entry in entryLists[f])
                        {
                            if (!string.Equals(baseName, entry, StringComparison.OrdinalIgnoreCase)) continue;
                            matchedEntries[f].Add(entry);
                            hit = true;
                        }
                        if (!hit) continue;

                        prefabSets[f].Add(prefab);
                        if (!Families[f].Materials.Contains(mat))
                        {
                            Families[f].Materials.Add(mat);
                            CacheOriginal(mat);
                        }
                    }
                }
            }
        }

        var summary = new StringBuilder("[Fortress Stone] resolved:");
        for (var f = 0; f < Families.Length; f++)
        {
            Families[f].PrefabCount = prefabSets[f].Count;
            summary.Append($" {Families[f].Name}={Families[f].Materials.Count} mats ({Families[f].PrefabCount} prefabs)");
            if (f < Families.Length - 1) summary.Append(',');
        }
        Plugin.Log?.LogInfo(summary.ToString());

        for (var f = 0; f < Families.Length; f++)
        {
            foreach (var entry in entryLists[f])
            {
                if (matchedEntries[f].Contains(entry)) continue;
                Plugin.Log?.LogWarning(
                    $"[Fortress Stone] {Families[f].Name} CSV entry '{entry}' matched nothing");
                if (!_catalogDumped)
                {
                    _catalogDumped = true;
                    Plugin.Log?.LogWarning(
                        "[Fortress Stone] near-miss material catalog: " +
                        (nearMisses.Count > 0 ? string.Join(", ", nearMisses) : "(none)"));
                }
            }
        }
    }

    private static void CacheOriginal(Material mat)
    {
        if (Originals.ContainsKey(mat)) return;
        var state = new OriginalState();
        if (mat.HasProperty("_Color"))
        {
            state.HasColor = true;
            state.Color = mat.GetColor("_Color");
        }
        if (mat.HasProperty("_EmissionColor"))
        {
            state.HasEmission = true;
            state.Emission = mat.GetColor("_EmissionColor");
        }
        Originals[mat] = state;
    }

    private static void ClearResolved()
    {
        _resolved = false;
        _resolvedScene = null;
        Originals.Clear();
        foreach (var family in Families)
        {
            family.Materials.Clear();
            family.PrefabCount = 0;
        }
    }

    // ---- apply / restore -----------------------------------------------------------------

    /// <summary>
    /// Idempotent: every write is computed from the cached original, never from the
    /// material's current value. Disabled families (or MasterSwitch off) get their
    /// originals restored, so cycling a toggle can't leak.
    /// </summary>
    internal static void ApplyAll()
    {
        if (!_resolved) return;
        var master = Plugin.MasterSwitch?.Value == true;

        foreach (var family in Families)
        {
            var active = master && family.Enabled();
            var brightness = family.Brightness();
            var tint = new Vector3(family.TintR(), family.TintG(), family.TintB());
            var applied = 0;

            foreach (var mat in family.Materials)
            {
                if (mat == null || !Originals.TryGetValue(mat, out var orig)) continue;

                if (!active)
                {
                    RestoreMaterial(mat, orig);
                    continue;
                }

                if (orig.HasColor)
                {
                    var c = orig.Color;
                    // HDR multiply on RGB only - alpha stays vanilla.
                    mat.SetColor("_Color", new Color(
                        c.r * tint.x * brightness,
                        c.g * tint.y * brightness,
                        c.b * tint.z * brightness,
                        c.a));
                }

                // Glow: fortress-only, and only on materials that ship a non-black vanilla
                // emission (the red cracks) - never sets emission where vanilla has none.
                if (family.HasGlow && orig.HasEmission && orig.Emission.maxColorComponent > 0.001f)
                    mat.SetColor("_EmissionColor", ResolveGlowColor(orig.Emission));

                applied++;
            }

            if (active && applied > 0)
            {
                Plugin.Log?.LogInfo(
                    $"[Fortress Stone] applied {family.Name}: {applied} mats, brightness={brightness:0.###} " +
                    $"tint=({tint.x:0.###},{tint.y:0.###},{tint.z:0.###})" +
                    (family.HasGlow ? $" glow={Plugin.FortressStoneGlowStyle?.Value}" : ""));
            }
        }
    }

    private static Color ResolveGlowColor(Color vanilla)
    {
        var style = Plugin.FortressStoneGlowStyle?.Value ?? "Ember";
        if (string.Equals(style, "Vanilla", StringComparison.OrdinalIgnoreCase)) return vanilla;
        if (string.Equals(style, "Off", StringComparison.OrdinalIgnoreCase)) return Color.black;
        if (string.Equals(style, "Custom", StringComparison.OrdinalIgnoreCase))
            return new Color(
                Plugin.FortressStoneGlowR?.Value ?? EmberGlow.r,
                Plugin.FortressStoneGlowG?.Value ?? EmberGlow.g,
                Plugin.FortressStoneGlowB?.Value ?? EmberGlow.b);
        return EmberGlow;
    }

    /// <summary>Restore every cached material to its vanilla colors (master-off revert path).</summary>
    internal static void RestoreAll()
    {
        foreach (var kvp in Originals)
        {
            if (kvp.Key == null) continue;
            RestoreMaterial(kvp.Key, kvp.Value);
        }
    }

    private static void RestoreMaterial(Material mat, OriginalState orig)
    {
        if (orig.HasColor) mat.SetColor("_Color", orig.Color);
        if (orig.HasEmission) mat.SetColor("_EmissionColor", orig.Emission);
    }

    // ---- lifecycle -------------------------------------------------------------------------

    /// <summary>
    /// Called by every Fortress Stone SettingChanged. A CSV change restores the old set,
    /// clears the resolve state, and re-resolves immediately; every change re-applies.
    /// Changes are instant - shared materials need no rebuild.
    /// </summary>
    internal static void OnConfigChanged(bool csvChanged = false)
    {
        if (csvChanged)
        {
            RestoreAll();
            ClearResolved();
            if (ZNetScene.instance != null) ResolveMaterials();
        }
        ApplyAll();
    }

    /// <summary>
    /// Called from Plugin's 0.2s tick; runs every ~2s. Resolves once per session when
    /// ZNetScene appears, and re-arms if it goes away (world unload can free the material
    /// assets, so the cache is dropped rather than restored).
    /// </summary>
    internal static void PeriodicUpdate()
    {
        _periodicTimer += 0.2f;
        if (_periodicTimer < 2f) return;
        _periodicTimer = 0f;

        if (ZNetScene.instance == null)
        {
            if (_resolved) ClearResolved();
            return;
        }

        if (_resolved && !ReferenceEquals(_resolvedScene, ZNetScene.instance)) ClearResolved();
        if (_resolved) return;
        ResolveMaterials();
        ApplyAll();
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static List<string> ParseCsv(string csv)
    {
        var list = new List<string>();
        foreach (var part in (csv ?? "").Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) list.Add(trimmed);
        }
        return list;
    }

    /// <summary>Strip Unity's " (Instance)"/" (Clone)" suffixes (FableWarriorPatches idiom).</summary>
    private static string BaseName(string name)
    {
        var trimmed = name;
        while (trimmed.EndsWith(" (Instance)", StringComparison.Ordinal) ||
               trimmed.EndsWith(" (Clone)", StringComparison.Ordinal) ||
               trimmed.EndsWith("(Clone)", StringComparison.Ordinal))
        {
            trimmed = trimmed.EndsWith(" (Instance)", StringComparison.Ordinal)
                ? trimmed.Substring(0, trimmed.Length - " (Instance)".Length)
                : trimmed.Substring(0, trimmed.LastIndexOf("(Clone)", StringComparison.Ordinal)).TrimEnd();
        }
        return trimmed.Trim();
    }
}
