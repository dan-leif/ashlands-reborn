using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Symmetry completions for rebuilt ruins: extra structural pieces per location, in
/// location-local space, computed offline by <c>scripts/ruins/analyze.py</c> (symmetry analysis
/// of each blueprint with every structural roll forced on). Data = embedded
/// <c>Data/ruins_additions.tsv</c>; a dev override at <c>AR_Ruins/additions.tsv</c> next to the
/// DLL wins when present (fast iteration without a rebuild).
/// Line format: <c>location \t prefab \t x,y,z \t qx,qy,qz,qw</c>.
/// </summary>
internal static class RuinsBlueprints
{
    private struct Piece
    {
        public string Prefab;
        public Vector3 Pos;
        public Quaternion Rot;
    }

    private static Dictionary<string, List<Piece>>? _data;

    internal static void Reload() { _data = null; _swaps = null; _doors = null; }

    /// <summary>Embedded data file, or its dev override in AR_Ruins\ (same name without "ruins_").</summary>
    private static string? ReadData(string resource, string overrideName)
    {
        try
        {
            var over = Path.Combine(RuinsReconPatches.OutDir(), overrideName);
            if (File.Exists(over)) return File.ReadAllText(over);
            using var s = typeof(RuinsBlueprints).Assembly.GetManifestResourceStream("AshlandsReborn.Data." + resource);
            if (s == null) return null;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{RuinsRebornPatches.Tag} {resource} load: {e.Message}"); return null; }
    }

    // ---- swaps: "location (or *) \t from prefab \t to prefab \t scale"
    private static Dictionary<string, Dictionary<string, (string to, float scale)>>? _swaps;

    /// <summary>What a vanilla piece becomes when its ruin is restored (intact floor, living tree), or null.</summary>
    internal static (string to, float scale)? GetSwap(string key, string prefab)
    {
        if (_swaps == null)
        {
            _swaps = new Dictionary<string, Dictionary<string, (string, float)>>(StringComparer.Ordinal);
            var text = ReadData("ruins_swaps.tsv", "swaps.tsv") ?? "";
            foreach (var line in text.Split('\n'))
            {
                var f = line.Trim().Split('\t');
                if (f.Length < 3 || f[0].StartsWith("#")) continue;
                var scale = f.Length > 3 && float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) ? sc : 1f;
                if (!_swaps.TryGetValue(f[0], out var d)) _swaps[f[0]] = d = new Dictionary<string, (string, float)>(StringComparer.Ordinal);
                d[f[1]] = (f[2], scale);
            }
            Plugin.Log.LogInfo($"{RuinsRebornPatches.Tag} swaps loaded: {_swaps.Values.Sum(d => d.Count)} rules");
        }
        if (_swaps.TryGetValue(key, out var own) && own.TryGetValue(prefab, out var v)) return v;
        if (_swaps.TryGetValue("*", out var any) && any.TryGetValue(prefab, out var v2)) return v2;
        return null;
    }

    // ---- doors: "location (or room:Name) \t x,y,z" pivots of wall groups left open in the restored building
    private static Dictionary<string, List<Vector3>>? _doors;
    private static readonly List<Vector3> NoDoors = new();

    internal static List<Vector3> GetDoors(string key)
    {
        if (_doors == null)
        {
            _doors = new Dictionary<string, List<Vector3>>(StringComparer.Ordinal);
            var text = ReadData("ruins_doors.tsv", "doors.tsv") ?? "";
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in text.Split('\n'))
            {
                var f = line.Trim().Split('\t');
                if (f.Length < 2 || f[0].StartsWith("#")) continue;
                var p = f[1].Split(',');
                if (p.Length != 3) continue;
                if (!_doors.TryGetValue(f[0], out var l)) _doors[f[0]] = l = new List<Vector3>();
                l.Add(new Vector3(float.Parse(p[0], inv), float.Parse(p[1], inv), float.Parse(p[2], inv)));
            }
            Plugin.Log.LogInfo($"{RuinsRebornPatches.Tag} doors loaded: {_doors.Values.Sum(l => l.Count)} in {_doors.Count} buildings");
        }
        return _doors.TryGetValue(key, out var d) ? d : NoDoors;
    }

    private static Dictionary<string, List<Piece>> Data
    {
        get
        {
            if (_data != null) return _data;
            _data = new Dictionary<string, List<Piece>>(StringComparer.Ordinal);
            string? text = null;
            try
            {
                var over = Path.Combine(RuinsReconPatches.OutDir(), "additions.tsv");
                if (File.Exists(over)) text = File.ReadAllText(over);
                else
                {
                    using var s = typeof(RuinsBlueprints).Assembly.GetManifestResourceStream("AshlandsReborn.Data.ruins_additions.tsv");
                    if (s != null) using (var r = new StreamReader(s)) text = r.ReadToEnd();
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"{RuinsRebornPatches.Tag} additions load: {e.Message}"); }
            if (text == null) return _data;
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in text.Split('\n'))
            {
                var f = line.Trim().Split('\t');
                if (f.Length < 4 || f[0].StartsWith("#")) continue;
                var p = f[2].Split(',');
                var q = f[3].Split(',');
                if (p.Length != 3 || q.Length != 4) continue;
                if (!_data.TryGetValue(f[0], out var list)) _data[f[0]] = list = new List<Piece>();
                list.Add(new Piece
                {
                    Prefab = f[1],
                    Pos = new Vector3(float.Parse(p[0], inv), float.Parse(p[1], inv), float.Parse(p[2], inv)),
                    Rot = new Quaternion(float.Parse(q[0], inv), float.Parse(q[1], inv), float.Parse(q[2], inv), float.Parse(q[3], inv)),
                });
            }
            int n = 0;
            foreach (var l in _data.Values) n += l.Count;
            Plugin.Log.LogInfo($"{RuinsRebornPatches.Tag} symmetry additions loaded: {n} pieces in {_data.Count} locations");
            return _data;
        }
    }

    /// <summary>Spawn the location's symmetry completions (networked, persistent). Returns count.</summary>
    internal static int SpawnAdditions(string location, Vector3 pos, Quaternion rot)
    {
        if (Plugin.RuinsSymmetry?.Value != true) return 0;
        if (!Data.TryGetValue(location, out var list)) return 0;
        int n = 0;
        foreach (var piece in list)
        {
            var prefab = ZNetScene.instance.GetPrefab(piece.Prefab);
            if (prefab == null) continue;
            if (RuinsRebornPatches.SpawnTagged(prefab, pos + rot * piece.Pos, rot * piece.Rot) != null) n++;
        }
        return n;
    }
}
