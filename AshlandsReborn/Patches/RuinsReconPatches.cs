using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev recon (config <c>RuinsReconDump</c>): once after world load, writes every Ashlands location
/// blueprint (full child hierarchy, transforms relative to the location root, components, meshes),
/// the Ashlands biome spawn list, every ZNetScene prefab that spawns creatures or looks like a
/// grave/spawner/ruin piece, and all Ashlands location instances in this world to
/// <c>&lt;plugin dir&gt;\AR_Ruins\recon.tsv</c>. Analysis happens offline (scripts/ruins/).
/// </summary>
internal static class RuinsReconPatches
{
    private const string Tag = "[AR RuinsRecon]";
    private static bool _fired;
    private static float _worldLoadedAt = -1f;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    internal static void Tick()
    {
        if (Player.m_localPlayer == null) { _worldLoadedAt = -1f; _fired = false; return; }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;
        if (_fired || Time.time - _worldLoadedAt < 5f) return;
        if (!(Plugin.RuinsReconDump?.Value ?? false)) return;
        _fired = true;
        try { Dump(); }
        catch (Exception e) { Plugin.Log.LogError($"{Tag} failed: {e}"); }
    }

    internal static string OutDir()
    {
        var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "AR_Ruins");
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static string BaseName(string n)
    {
        n = n.Replace("(Clone)", "").Trim();
        // strip Unity duplicate suffix " (12)"
        int p = n.LastIndexOf(" (", StringComparison.Ordinal);
        if (p > 0 && n.EndsWith(")") && int.TryParse(n.Substring(p + 2, n.Length - p - 3), out _)) n = n.Substring(0, p);
        return n.Trim();
    }

    private static string V(Vector3 v) => string.Format(Inv, "{0:0.###},{1:0.###},{2:0.###}", v.x, v.y, v.z);
    private static string Q(Quaternion q) => string.Format(Inv, "{0:0.####},{1:0.####},{2:0.####},{3:0.####}", q.x, q.y, q.z, q.w);

    private static string Comps(GameObject go)
    {
        var s = string.Join(",", go.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
        var rs = go.GetComponent<RandomSpawn>();
        if (rs) s += $"|rs={rs.m_chanceToSpawn.ToString(Inv)};off={(rs.m_OffObject ? rs.m_OffObject.name : "")};lava={rs.m_notInLava};biome={(int)rs.m_requireBiome};elev={rs.m_minElevation}..{rs.m_maxElevation}";
        var ro = go.GetComponent<RandomObject>();
        if (ro) s += "|ro=" + string.Join(";", ro.m_objects.Where(o => o?.m_object).Select(o => o.m_object.name + ":" + o.m_weight.ToString(Inv)));
        return s;
    }

    private static string Meshes(GameObject go)
    {
        var names = new List<string>();
        var mf = go.GetComponent<MeshFilter>();
        if (mf && mf.sharedMesh) names.Add(mf.sharedMesh.name);
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr && smr.sharedMesh) names.Add("skin:" + smr.sharedMesh.name);
        return string.Join(",", names);
    }

    private static void Dump()
    {
        var sw = new StringBuilder();
        var zs = ZoneSystem.instance;
        var ashLocs = zs.m_locations.Where(l => l != null && (l.m_biome & Heightmap.Biome.AshLands) != 0).ToList();
        Plugin.Log.LogInfo($"{Tag} {ashLocs.Count} Ashlands locations");

        foreach (var loc in ashLocs)
        {
            sw.Append("LOC\t").Append(loc.m_prefab.Name).Append('\t').Append(loc.m_name).Append('\t')
              .Append((int)loc.m_biome).Append('\t').Append(loc.m_quantity).Append('\t').Append(loc.m_enable)
              .Append('\t').Append(loc.m_interiorRadius.ToString(Inv)).Append('\t').Append(loc.m_exteriorRadius.ToString(Inv))
              .Append('\t').Append(loc.m_group).Append('\n');
            try
            {
                loc.m_prefab.Load();
                var root = loc.m_prefab.Asset;
                if (root == null) { sw.Append("ERR\tnull asset\n"); continue; }
                var locComp = root.GetComponent<Location>();
                if (locComp) sw.Append("LOCINFO\t").Append(loc.m_prefab.Name).Append("\trandomDamage=").Append(locComp.m_applyRandomDamage)
                    .Append("\tclearArea=").Append(locComp.m_clearArea).Append("\texteriorR=").Append(locComp.m_exteriorRadius.ToString(Inv)).Append('\n');
                DumpNode(sw, loc.m_prefab.Name, root.transform, root.transform, "", 0);
                foreach (var dg in root.GetComponentsInChildren<DungeonGenerator>(true))
                {
                    sw.Append("DG\t").Append(loc.m_prefab.Name).Append('\t').Append(dg.name).Append("\talgo=").Append(dg.m_algorithm)
                      .Append(";themes=").Append(dg.m_themes).Append(";rooms=").Append(dg.m_minRooms).Append("..").Append(dg.m_maxRooms)
                      .Append(";radius=").Append(dg.m_campRadiusMin.ToString(Inv)).Append("..").Append(dg.m_campRadiusMax.ToString(Inv))
                      .Append(";perimeter=").Append(dg.m_perimeterSections).Append(";grid=").Append(dg.m_gridSize).Append('x').Append(dg.m_tileWidth.ToString(Inv))
                      .Append(";spawnChance=").Append(dg.m_spawnChance.ToString(Inv)).Append('\n');
                    foreach (var rd in DungeonDB.GetRooms())
                    {
                        if ((rd.m_theme & dg.m_themes) == 0) continue;
                        rd.m_prefab.Load();
                        var ra = rd.m_prefab.Asset;
                        var room = ra ? ra.GetComponent<Room>() : null;
                        sw.Append("ROOM\t").Append(loc.m_prefab.Name).Append('\t').Append(rd.m_prefab.Name).Append("\tenabled=").Append(rd.m_enabled);
                        if (room) sw.Append(";weight=").Append(room.m_weight.ToString(Inv)).Append(";perimeter=").Append(room.m_perimeter)
                            .Append(";faceCenter=").Append(room.m_faceCenter).Append(";size=").Append(room.m_size);
                        sw.Append('\n');
                        if (ra) DumpNode(sw, "room:" + rd.m_prefab.Name, ra.transform, ra.transform, "", 0);
                        rd.m_prefab.Release();
                    }
                }
                loc.m_prefab.Release();
            }
            catch (Exception e) { sw.Append("ERR\t").Append(e.Message).Append('\n'); }
        }

        // Ashlands spawn list
        foreach (var list in Resources.FindObjectsOfTypeAll<SpawnSystemList>())
            foreach (var s in list.m_spawners)
                if (s != null && (s.m_biome & Heightmap.Biome.AshLands) != 0)
                    sw.Append("SPAWN\t").Append(list.name).Append('\t').Append(s.m_name).Append('\t')
                      .Append(s.m_prefab ? s.m_prefab.name : "null").Append('\t').Append(s.m_enabled).Append('\t')
                      .Append(s.m_maxSpawned).Append('\t').Append((int)s.m_biome).Append('\n');

        // ZNetScene prefab catalog (all of them, with mesh bounds for buildable pieces)
        foreach (var p in ZNetScene.instance.m_prefabs)
        {
            if (!p) continue;
            sw.Append("PREFAB\t").Append(p.name).Append('\t').Append(Comps(p)).Append('\t');
            var wnt = p.GetComponent<WearNTear>();
            if (wnt) sw.Append("hp=").Append(wnt.m_health.ToString(Inv)).Append(";mat=").Append(wnt.m_materialType);
            var sa = p.GetComponent<SpawnArea>();
            if (sa) sw.Append("spawns=").Append(string.Join("|", sa.m_prefabs.Where(d => d?.m_prefab).Select(d => d.m_prefab.name)));
            var cs = p.GetComponent<CreatureSpawner>();
            if (cs) sw.Append("creature=").Append(cs.m_creaturePrefab ? cs.m_creaturePrefab.name : "null");
            sw.Append('\t');
            var rends = p.GetComponentsInChildren<MeshFilter>(true);
            if (rends.Length > 0 && rends.Length < 40)
            {
                var b = new Bounds();
                bool init = false;
                foreach (var mf in rends)
                {
                    if (!mf.sharedMesh) continue;
                    var mb = mf.sharedMesh.bounds;
                    // transform bounds corners into prefab-root space
                    var m = p.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var w = m.MultiplyPoint3x4(c);
                        if (!init) { b = new Bounds(w, Vector3.zero); init = true; } else b.Encapsulate(w);
                    }
                }
                if (init) sw.Append("bmin=").Append(V(b.min)).Append(";bmax=").Append(V(b.max));
            }
            sw.Append('\t').Append(string.Join(",", rends.Where(r => r.sharedMesh).Select(r => r.sharedMesh.name).Distinct().Take(12)));
            sw.Append('\n');
        }

        // Location instances in this world
        foreach (var inst in zs.GetLocationList())
        {
            if (inst.m_location == null || (inst.m_location.m_biome & Heightmap.Biome.AshLands) == 0) continue;
            sw.Append("INST\t").Append(inst.m_location.m_prefab.Name).Append('\t').Append(V(inst.m_position)).Append('\t').Append(inst.m_placed).Append('\n');
        }

        var path = Path.Combine(OutDir(), "recon.tsv");
        File.WriteAllText(path, sw.ToString());
        Plugin.Log.LogInfo($"{Tag} DONE -> {path}");
    }

    private static void DumpNode(StringBuilder sw, string loc, Transform root, Transform t, string path, int depth)
    {
        var go = t.gameObject;
        var nv = go.GetComponent<ZNetView>();
        var rel = root.worldToLocalMatrix * t.localToWorldMatrix;
        var pos = (Vector3)rel.GetColumn(3);
        var rot = Quaternion.Inverse(root.rotation) * t.rotation;
        var scl = t.lossyScale;
        sw.Append("NODE\t").Append(loc).Append('\t').Append(depth).Append('\t').Append(path).Append('\t')
          .Append(BaseName(go.name)).Append('\t').Append(go.activeSelf ? 1 : 0).Append('\t').Append(nv ? 1 : 0).Append('\t')
          .Append(V(pos)).Append('\t').Append(Q(rot)).Append('\t').Append(V(scl)).Append('\t')
          .Append(Comps(go)).Append('\t').Append(Meshes(go)).Append('\n');
        if (nv && depth > 0) return; // networked prefab instance: its own prefab, don't recurse
        var childPath = depth == 0 ? "" : (path.Length == 0 ? BaseName(go.name) : path + "/" + BaseName(go.name));
        for (int i = 0; i < t.childCount; i++)
            DumpNode(sw, loc, root, t.GetChild(i), childPath, depth + 1);
    }
}
