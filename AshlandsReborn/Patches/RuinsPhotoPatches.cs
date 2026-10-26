using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev harnesses on the flat test arena (<c>PhotoModeIslandPos</c>), run once ~10 s after world load,
/// in this order, each switching its own flag off when done:
/// - <c>RuinsPurgeTest</c>: plants a spawner, a grave and a Morgen, flips the purge configs and the
///   MasterSwitch, asserts each hides/shows with the toggles and the ZDO count never changes.
/// - <c>RuinsSeedSweep</c>: spawns AshlandRuins with forced DungeonGenerator seeds and logs the room
///   list + tower count (<c>CharredRuins_new01</c>) per seed.
/// - <c>RuinsTreeTrial</c>: a restored CharredTowerRuins2 with each <c>RuinsTreeTrialList</c> tree at the
///   arbor's tree slot, shot from fixed angles.
/// - <c>RuinsDoorShots</c>: ground-level shots of every door slot (ruins_doors.tsv), restored + ruined,
///   with a DOORCHECK line (no active structural piece left at a restored door pivot).
/// - <c>RuinsPhotoAuto</c>: each <c>RuinsPhotoLocations</c> location, orbit + top-down shots per variant.
/// Output: <c>AR_Ruins\photos\</c> (+ subfolders), "[AR RuinsPhoto] DONE".
/// </summary>
internal static class RuinsPhotoPatches
{
    private const string Tag = "[AR RuinsPhoto]";
    private static bool _running, _fired;
    private static float _worldLoadedAt = -1f;

    private static bool Any =>
        (Plugin.RuinsPhotoAuto?.Value ?? false) || (Plugin.RuinsSeedSweep?.Value ?? false) || (Plugin.RuinsTreeTrial?.Value ?? false)
        || (Plugin.RuinsPurgeTest?.Value ?? false) || (Plugin.RuinsDoorShots?.Value ?? false);

    internal static void Tick()
    {
        if (Player.m_localPlayer == null) { _worldLoadedAt = -1f; _fired = false; return; }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;
        if (_fired || _running || Time.time - _worldLoadedAt < 10f) return;
        if (!Any) return;
        _fired = true;
        Plugin.Instance.StartCoroutine(Run());
    }

    private static Vector3 _center;
    private static string _dir = "";
    private static readonly List<string> Lines = new();

    private static void Note(string l) { Lines.Add(l); Plugin.Log.LogInfo($"{Tag} {l}"); }

    private static IEnumerator Run()
    {
        _running = true;
        var player = Player.m_localPlayer;
        var hud = Hud.instance;
        Lines.Clear();
        try
        {
            Application.runInBackground = true;
            _center = PhotoModePatches.ParsePos(Plugin.PhotoModeIslandPos?.Value) ?? player.transform.position;
            // Stand off to the side so the player never ends up inside a tower.
            yield return PhotoModePatches.TeleportRoutine(player, _center + new Vector3(0f, 0f, -45f));
            FableBunnyPatches.SetDebugDaylight(true);
            yield return PhotoModePatches.ForceClearSkyRoutine();
            player.SetGodMode(true);
            if (hud && hud.m_rootObject) hud.m_rootObject.SetActive(false);
            _dir = Path.Combine(RuinsReconPatches.OutDir(), "photos");
            Directory.CreateDirectory(_dir);

            if (Plugin.RuinsPurgeTest?.Value ?? false) { yield return Guard(PurgeTest(), "purge test"); Plugin.RuinsPurgeTest!.Value = false; }
            if (Plugin.RuinsSeedSweep?.Value ?? false) { yield return Guard(SeedSweep(), "seed sweep"); Plugin.RuinsSeedSweep!.Value = false; }
            if (Plugin.RuinsTreeTrial?.Value ?? false) { yield return Guard(TreeTrial(), "tree trial"); Plugin.RuinsTreeTrial!.Value = false; }
            if (Plugin.RuinsDoorShots?.Value ?? false) { yield return Guard(DoorShots(), "door shots"); Plugin.RuinsDoorShots!.Value = false; }
            if (Plugin.RuinsPhotoAuto?.Value ?? false) yield return Guard(OrbitShots(), "orbit shots");

            RuinsRebornPatches.RebuildOverride = null;
            RuinsRebornPatches.OnRestoreToggled();
            File.WriteAllLines(Path.Combine(_dir, "DONE.txt"), Lines);
            Plugin.Log.LogInfo($"{Tag} DONE -> {_dir}");
        }
        finally
        {
            RuinsRebornPatches.RebuildOverride = null;
            RuinsRebornPatches.RoomLog = null;
            PhotoModePatches.ClearCameraOverride();
            FableBunnyPatches.SetDebugDaylight(false);
            if (hud && hud.m_rootObject) hud.m_rootObject.SetActive(true);
            _running = false;
        }
    }

    /// <summary>Run a sub-routine; an exception ends that mode only (logged), not the whole harness.</summary>
    private static IEnumerator Guard(IEnumerator inner, string what)
    {
        while (true)
        {
            object? cur;
            try
            {
                if (!inner.MoveNext()) yield break;
                cur = inner.Current;
            }
            catch (Exception e) { Plugin.Log.LogError($"{Tag} {what} failed: {e}"); yield break; }
            yield return cur;
        }
    }

    // ================================================================== shared helpers

    /// <summary>Spawn a location at <paramref name="pos"/> (restored when <paramref name="rebuilt"/>), return the new ZDOs.</summary>
    private static IEnumerator SpawnLoc(string name, Vector3 pos, bool rebuilt, int forceSeed, Action<List<ZDO>> done)
    {
        var loc = RuinsRebornPatches.FindLocation(name);
        if (loc == null) { Plugin.Log.LogWarning($"{Tag} unknown location {name}"); done(new List<ZDO>()); yield break; }
        var spawn = AccessTools.Method(typeof(ZoneSystem), "SpawnLocation");
        RuinsRebornPatches.RebuildOverride = rebuilt;
        pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        var before = new HashSet<ZDOID>(SectorZdos(pos).Select(z => z.m_uid));
        // Location.m_biome is computed from the world on first spawn; the arena isn't Ashlands,
        // and biome-gated RandomSpawns would all roll off there.
        loc.m_prefab.Load();
        var lc = loc.m_prefab.Asset.GetComponent<Location>();
        var oldBiome = lc ? lc.m_biome : Heightmap.Biome.None;
        if (lc) lc.m_biome = Heightmap.Biome.AshLands;
        if (forceSeed != int.MinValue) { DungeonGenerator.m_forceSeed = forceSeed; RuinsRebornPatches.CampLayoutSeedOnly = true; }
        try
        {
            spawn.Invoke(ZoneSystem.instance, new object[] { loc, 4242, pos, Quaternion.identity, ZoneSystem.SpawnMode.Full, new List<GameObject>(), false });
        }
        catch (Exception e) { Plugin.Log.LogError($"{Tag} spawn {name}: {e}"); }
        finally
        {
            DungeonGenerator.m_forceSeed = int.MinValue;
            RuinsRebornPatches.CampLayoutSeedOnly = false;
            if (lc) lc.m_biome = oldBiome;
            loc.m_prefab.Release();
        }
        yield return new WaitForSeconds(3f);
        done(SectorZdos(pos).Where(z => !before.Contains(z.m_uid)).ToList());
    }

    private static IEnumerator DeleteAll(List<ZDO> created)
    {
        foreach (var z in created) if (z.IsValid()) RuinsRebornPatches.DeleteZdo(z);
        yield return new WaitForSeconds(2f);
    }

    private static Bounds StructureBounds(List<ZDO> created, Vector3 pos)
    {
        Bounds? bb = null;
        foreach (var z in created)
        {
            var inst = ZNetScene.instance.FindInstance(z);
            if (inst == null || !RuinsRebornPatches.IsStructural(RuinsRebornPatches.PrefabName(inst.gameObject))) continue;
            foreach (var rend in inst.GetComponentsInChildren<Renderer>())
            {
                if (!rend.enabled || rend is ParticleSystemRenderer) continue;
                if (bb == null) bb = rend.bounds; else { var b = bb.Value; b.Encapsulate(rend.bounds); bb = b; }
            }
        }
        return bb ?? new Bounds(pos + Vector3.up * 8f, Vector3.one * 24f);
    }

    private static IEnumerator Shot(Vector3 cam, Vector3 look, string file)
    {
        PhotoModePatches.SetCameraOverride(cam, Quaternion.LookRotation(look - cam));
        yield return new WaitForSeconds(1.2f);
        ScreenCapture.CaptureScreenshot(Path.Combine(_dir, file));
        yield return new WaitForSeconds(0.6f);
    }

    private static void SetRebuilt(bool on)
    {
        RuinsRebornPatches.RebuildOverride = on;
        RuinsRebornPatches.OnRestoreToggled();
    }

    // ================================================================== orbit shots (RuinsPhotoAuto)

    private static IEnumerator OrbitShots()
    {
        var locs = (Plugin.RuinsPhotoLocations?.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var variants = (Plugin.RuinsPhotoVariants?.Value ?? "rebuilt,vanilla").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        foreach (var name in locs)
        {
            // Spawn once, restored; then flip the toggle to shoot the ruined state of the SAME instance.
            List<ZDO> created = new();
            yield return SpawnLoc(name, _center, true, int.MinValue, c => created = c);
            var counts = created.GroupBy(z => ZNetScene.instance.GetPrefab(z.GetPrefab())?.name ?? z.GetPrefab().ToString())
                .OrderByDescending(g => g.Count()).Select(g => $"{g.Key}x{g.Count()}");
            int tagged = created.Count(z => z.GetInt(RuinsRebornPatches.RestoreTag) == 1);
            Note($"{name}: {created.Count} objects ({tagged} restoration): {string.Join(" ", counts)}");
            var bounds = StructureBounds(created, _center);
            var r = Mathf.Max(bounds.extents.magnitude, 10f);
            var focus = bounds.center;
            foreach (var variant in variants)
            {
                SetRebuilt(variant == "rebuilt");
                yield return new WaitForSeconds(1f);
                foreach (var yaw in new[] { 30, 120, 210, 300 })
                {
                    var camPos = focus + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, r * 0.75f, -r * 1.9f);
                    yield return Shot(camPos, focus, $"{name}_{variant}_y{yaw}.png");
                }
                PhotoModePatches.SetCameraOverride(focus + Vector3.up * (r * 2.6f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                yield return new WaitForSeconds(1.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"{name}_{variant}_top.png"));
                yield return new WaitForSeconds(0.6f);
            }
            yield return DeleteAll(created);
        }
    }

    // ================================================================== purge toggle test

    private static int ZdoCount() =>
        (Traverse.Create(ZDOMan.instance).Field("m_objectsByID").GetValue() as System.Collections.IDictionary)?.Count ?? -1;

    private static IEnumerator PurgeTest()
    {
        var oldSp = Plugin.RemoveSpawnersAndGraves.Value;
        var oldMo = Plugin.RemoveMorgen.Value;
        var oldMaster = Plugin.MasterSwitch.Value;
        var plants = new List<(string name, ZNetView nv, ZDO zdo, bool morgen)>();
        var offsets = new[] { new Vector3(-6f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(7f, 0f, 0f) };
        var names = new[] { "Spawner_CharredStone", "GraveStone_CharredTwitcherNest", "Morgen" };
        int pass = 0, fail = 0;
        try
        {
            Plugin.RemoveSpawnersAndGraves.Value = true;
            Plugin.RemoveMorgen.Value = true;
            Plugin.MasterSwitch.Value = true;
            yield return new WaitForSeconds(2f);
            for (int i = 0; i < names.Length; i++)
            {
                var pf = ZNetScene.instance.GetPrefab(names[i]);
                if (pf == null) { Note($"PURGECHECK prefab {names[i]} missing"); fail++; continue; }
                var p = _center + offsets[i];
                p.y = ZoneSystem.instance.GetGroundHeight(p);
                var go = UnityEngine.Object.Instantiate(pf, p, Quaternion.Euler(0f, 180f, 0f));
                var nv = go.GetComponent<ZNetView>();
                plants.Add((names[i], nv, nv.GetZDO(), i == 2));
            }
            var cam = _center + new Vector3(0f, 6f, -16f);
            var look = _center + Vector3.up * 1.5f;
            yield return new WaitForSeconds(2f);

            IEnumerator Check(string label, bool spShown, bool moShown)
            {
                yield return new WaitForSeconds(2f);
                int zdos = ZdoCount();
                foreach (var pl in plants)
                {
                    bool want = pl.morgen ? moShown : spShown;
                    bool alive = pl.zdo != null && pl.zdo.IsValid() && pl.nv != null;
                    bool shown = alive && pl.nv!.gameObject.activeSelf;
                    bool ok = alive && shown == want;
                    if (ok) pass++; else fail++;
                    Note($"PURGECHECK {(ok ? "PASS" : "FAIL")} [{label}] {pl.name}: zdo {(alive ? "alive" : "GONE")}, {(shown ? "shown" : "hidden")} (want {(want ? "shown" : "hidden")})");
                }
                Note($"PURGECHECK [{label}] zdoCount={zdos}");
                yield return Shot(cam, look, $"purge_{label}.png");
            }

            int z0 = ZdoCount();
            yield return Check("1_purge_on", false, false);
            Plugin.RemoveSpawnersAndGraves.Value = false;
            yield return Check("2_spawners_off", true, false);
            Plugin.RemoveMorgen.Value = false;
            yield return Check("3_both_off", true, true);
            Plugin.RemoveSpawnersAndGraves.Value = true;
            Plugin.RemoveMorgen.Value = true;
            yield return Check("4_both_on_again", false, false);
            Plugin.MasterSwitch.Value = false;
            yield return new WaitForSeconds(3f);
            yield return Check("5_master_off", true, true);
            Plugin.MasterSwitch.Value = true;
            yield return new WaitForSeconds(3f);
            yield return Check("6_master_on", false, false);
            int z1 = ZdoCount();
            // Nothing may be deleted. The count may RISE: a revealed spawner spawns creatures while shown.
            bool same = z1 >= z0;
            if (same) pass++; else fail++;
            Note($"PURGECHECK {(same ? "PASS" : "FAIL")} zdo count before={z0} after={z1} (no deletions; rises = spawns while revealed)");
        }
        finally
        {
            Plugin.RemoveSpawnersAndGraves.Value = oldSp;
            Plugin.RemoveMorgen.Value = oldMo;
            Plugin.MasterSwitch.Value = oldMaster;
        }
        Note($"PURGECHECK DONE pass={pass} fail={fail}");
        foreach (var pl in plants) if (pl.zdo != null && pl.zdo.IsValid()) RuinsRebornPatches.DeleteZdo(pl.zdo);
        yield return new WaitForSeconds(2f);
    }

    // ================================================================== AshlandRuins seed sweep

    private static IEnumerator SeedSweep()
    {
        var range = (Plugin.RuinsSeedSweepRange?.Value ?? "1-16").Split('-');
        int a = int.Parse(range[0].Trim(), CultureInfo.InvariantCulture);
        int b = range.Length > 1 ? int.Parse(range[1].Trim(), CultureInfo.InvariantCulture) : a;
        var best = new List<int>();
        for (int seed = a; seed <= b; seed++)
        {
            RuinsRebornPatches.RoomLog = new List<(string, Vector3, Quaternion)>();
            List<ZDO> created = new();
            yield return SpawnLoc("AshlandRuins", _center, false, seed, c => created = c);
            var rooms = RuinsRebornPatches.RoomLog ?? new();
            RuinsRebornPatches.RoomLog = null;
            int towers = rooms.Count(r => r.name == "CharredRuins_new01");
            var list = rooms.GroupBy(r => r.name).Select(g => $"{g.Key}x{g.Count()}");
            Note($"SEED {seed}: towers={towers} rooms={rooms.Count} {string.Join(" ", list)}");
            if (towers == 2) best.Add(seed);
            if (towers >= 2 && best.Count <= 3)
            {
                var bounds = StructureBounds(created, _center);
                var r = Mathf.Max(bounds.extents.magnitude, 12f);
                yield return Shot(bounds.center + new Vector3(0f, r * 0.8f, -r * 1.8f), bounds.center, $"seed_{seed}_t{towers}.png");
            }
            yield return DeleteAll(created);
        }
        Note($"SEED two-tower seeds: {(best.Count > 0 ? string.Join(",", best) : "none")}");
    }

    // ================================================================== arbor tree trial

    internal static readonly Vector3 ArborTreeSlot = new(-3f, -0.5f, -0.75f);

    private static IEnumerator TreeTrial()
    {
        List<ZDO> created = new();
        yield return SpawnLoc("CharredTowerRuins2", _center, true, int.MinValue, c => created = c);
        var pos = _center; pos.y = ZoneSystem.instance.GetGroundHeight(_center);
        var slot = pos + ArborTreeSlot;
        // remove whatever tree vanilla / the swap put in the slot
        foreach (var z in created)
            if (z.IsValid() && (z.GetPosition() - slot).sqrMagnitude < 2f * 2f)
            {
                var n = ZNetScene.instance.GetPrefab(z.GetPrefab())?.name ?? "";
                if (n.Contains("Tree") || n.StartsWith("Oak") || n.StartsWith("Beech") || n.StartsWith("Birch"))
                { Note($"TREE removed slot occupant {n}"); RuinsRebornPatches.DeleteZdo(z); }
            }
        yield return new WaitForSeconds(1f);
        var bounds = StructureBounds(created, pos);
        var focus = bounds.center;
        var r = Mathf.Max(bounds.extents.magnitude, 10f);
        var list = (Plugin.RuinsTreeTrialList?.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
        foreach (var entry in list)
        {
            var parts = entry.Split(':');
            var name = parts[0];
            var scale = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sc) ? sc : 1f;
            var pf = ZNetScene.instance.GetPrefab(name);
            if (pf == null) { Note($"TREE {name} missing"); continue; }
            var go = UnityEngine.Object.Instantiate(pf, slot, Quaternion.identity);
            var nv = go.GetComponent<ZNetView>();
            if (nv != null && Mathf.Abs(scale - 1f) > 0.001f) nv.SetLocalScale(Vector3.one * scale);
            yield return new WaitForSeconds(1.5f);
            float h = 0f;
            foreach (var rend in go.GetComponentsInChildren<Renderer>()) if (rend.enabled && !(rend is ParticleSystemRenderer)) h = Mathf.Max(h, rend.bounds.max.y - slot.y);
            Note($"TREE {name}:{scale} height={h:0.0}m arbor top={bounds.max.y - slot.y:0.0}m");
            var label = $"{name}_{scale.ToString("0.##", CultureInfo.InvariantCulture)}";
            yield return Shot(focus + Quaternion.Euler(0f, 200f, 0f) * new Vector3(0f, r * 0.5f, -r * 1.7f), focus, $"tree_{label}_oblique.png");
            var walker = pos + new Vector3(0f, 1.7f, -r * 1.6f);
            walker.y = ZoneSystem.instance.GetGroundHeight(walker) + 1.7f;
            yield return Shot(walker, focus + Vector3.up * 2f, $"tree_{label}_walker.png");
            if (nv != null && nv.GetZDO() != null) RuinsRebornPatches.DeleteZdo(nv.GetZDO()); else UnityEngine.Object.Destroy(go);
            yield return new WaitForSeconds(1f);
        }
        yield return DeleteAll(created);
    }

    // ================================================================== door shots

    private static IEnumerator DoorShots()
    {
        var locs = new[] { "CharredRuins2", "CharredRuins3", "CharredRuins4", "CharredTowerRuins1", "AshlandRuins", "FortressRuins" };
        foreach (var name in locs)
        {
            RuinsRebornPatches.RoomLog = new List<(string, Vector3, Quaternion)>();
            List<ZDO> created = new();
            yield return SpawnLoc(name, _center, true, int.MinValue, c => created = c);
            var rooms = RuinsRebornPatches.RoomLog ?? new();
            RuinsRebornPatches.RoomLog = null;
            var pos = _center; pos.y = ZoneSystem.instance.GetGroundHeight(_center);
            var doors = new List<(string key, Vector3 world, Vector3 outward)>();
            foreach (var d in RuinsBlueprints.GetDoors(name))
                doors.Add((name, pos + d, Flat(d)));
            int perRoom = 0;
            foreach (var room in rooms)
            {
                var ds = RuinsBlueprints.GetDoors("room:" + room.name);
                if (ds.Count == 0 || perRoom >= 2) continue;
                perRoom++;
                foreach (var d in ds) doors.Add(("room:" + room.name, room.pos + room.rot * d, room.rot * Flat(d)));
            }
            if (doors.Count == 0) Note($"DOORCHECK {name}: no door slots in this instance");
            int i = 0;
            foreach (var (key, w, outward) in doors)
            {
                i++;
                var cam = w + outward * 7.5f;
                cam.y = Mathf.Max(ZoneSystem.instance.GetGroundHeight(cam) + 1.7f, w.y + 1.2f);
                var look = w + Vector3.up * 1.4f;
                foreach (var restored in new[] { true, false })
                {
                    SetRebuilt(restored);
                    yield return new WaitForSeconds(1f);
                    if (restored)
                    {
                        var blockers = ActiveStructuralNear(w, 0.4f);
                        Note($"DOORCHECK {(blockers.Count == 0 ? "PASS" : "FAIL")} {name} {key} door {i} at {w}: {(blockers.Count == 0 ? "open" : "blocked by " + string.Join(",", blockers))}");
                    }
                    yield return Shot(cam, look, $"door_{name}_{i}_{(restored ? "restored" : "ruined")}.png");
                }
            }
            yield return DeleteAll(created);
        }
    }

    private static Vector3 Flat(Vector3 d)
    {
        d.y = 0f;
        return d.sqrMagnitude < 1e-4f ? Vector3.forward : d.normalized;
    }

    private static List<string> ActiveStructuralNear(Vector3 w, float r)
    {
        var l = new List<string>();
        foreach (var z in SectorZdos(w))
        {
            if ((z.GetPosition() - w).sqrMagnitude > r * r) continue;
            var inst = ZNetScene.instance.FindInstance(z);
            if (inst == null || !inst.gameObject.activeInHierarchy) continue;
            var n = RuinsRebornPatches.PrefabName(inst.gameObject);
            if (RuinsRebornPatches.IsStructural(n) && !n.Contains("Floor")) l.Add(n);
        }
        return l;
    }

    private static List<ZDO> SectorZdos(Vector3 pos)
    {
        var l = new List<ZDO>();
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), new SimulationDistance(1, 1, classic: true), l);
        return l.Where(z => z.IsValid() && (z.GetPosition() - pos).sqrMagnitude < 60f * 60f).ToList();
    }
}
