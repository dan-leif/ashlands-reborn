using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    internal static void Reload() => _data = null;

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
