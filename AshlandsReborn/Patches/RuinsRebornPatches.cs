using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Ashlands Rebuilt: GAMEPLAY changes (the project's 2026-10-25 ethos shift).
///
/// 1. Purge - charred spawners, gravestones and the Morgen are removed from the world:
///    banned networked prefabs are deactivated in location blueprints before a zone generates,
///    and any banned object that still awakens (already saved in the world, event spawns) is
///    deleted next frame. Morgen entries in the biome spawn lists are disabled. Permanent.
///
/// 2. Restoration - every ruin is restored "as designed", REVERSIBLY:
///    The ruins are modular towers whose storeys/wall fragments/arches are each a RandomSpawn
///    roll. The vanilla ruin is left exactly as generated; the restoration is the set of
///    structural pieces that roll on when every structural roll is forced to succeed but that
///    vanilla's own roll (same seed, same Random sequence) left out, plus the symmetry
///    completions from <see cref="RuinsBlueprints"/>. Those pieces are spawned as normal
///    networked objects TAGGED with <see cref="RestoreTag"/>; they are shown while
///    <see cref="IsRebuildActive"/> (MasterSwitch + RebuildRuins) and deactivated otherwise,
///    and vanilla pieces' damage visuals (worn/broken meshes) read as new while restored.
///    So the MasterSwitch flips every ruin between ruined and restored instantly.
///    - New zones: SpawnLocation postfix adds the restoration right after vanilla's spawn.
///    - Existing zones: a ruin's LocationProxy loading without our stamp gets its restoration
///      added once (stamp = version on the proxy ZDO; bump <see cref="RestoreVersion"/> to
///      add newly authored pieces everywhere - older tagged pieces are cleared first).
///    - Camp ruins (AshlandRuins/FortressRuins, DungeonGenerator rooms): same per room.
///    Proxies/generators stamped with <see cref="KeepRuinedKey"/> are never restored (the
///    memorial park's "before" exhibits).
/// </summary>
internal static class RuinsRebornPatches
{
    internal const string Tag = "[Ruins Reborn]";
    internal const int RestoreVersion = 3;
    internal static readonly int RestoredKey = "ar_ruins_restored".GetStableHashCode();
    internal static readonly int RestoreTag = "ar_restore_piece".GetStableHashCode();
    internal static readonly int KeepRuinedKey = "ar_keep_ruined".GetStableHashCode();

    internal static readonly HashSet<string> RuinLocations = new(StringComparer.Ordinal)
    {
        "CharredRuins1", "CharredRuins2", "CharredRuins3", "CharredRuins4",
        "AshlandRuins", "FortressRuins",
        "CharredTowerRuins1", "CharredTowerRuins1_dvergr", "CharredTowerRuins2", "CharredTowerRuins3",
    };

    internal static readonly HashSet<string> SpawnerAndGravePrefabs = new(StringComparer.Ordinal)
    {
        "Spawner_CharredStone", "Spawner_CharredStone_Elite", "Spawner_CharredStone_event", "Spawner_CharredCross",
        "GraveStone_CharredTwitcherNest", "GraveStone_Broken_CharredTwitcherNest",
        "GraveStone_Elite_CharredTwitcherNest", "GraveStone_Elite_Broken_CharredTwitcherNest",
        "GraveStone_Broken_World", "GraveStone_CharredFaderLocation",
    };

    internal static readonly HashSet<string> MorgenPrefabs = new(StringComparer.Ordinal)
    {
        "Morgen", "Morgen_NonSleeping", "Spawner_Morgen", "Spawner_Morgen_wakeup",
    };

    /// <summary>Harness override: null = follow config, true/false = force restoration on/off.</summary>
    internal static bool? RebuildOverride;
    /// <summary>Set while the park spawns a "before" exhibit: no restoration, stamp keep-ruined.</summary>
    internal static bool SuppressRestore;
    /// <summary>Dev harness: when non-null, every camp room placed is recorded here (name, root pos, rot).</summary>
    internal static List<(string name, Vector3 pos, Quaternion rot)>? RoomLog;

    internal static bool IsRebuildActive => RebuildOverride ?? (Plugin.MasterSwitch?.Value == true && Plugin.RuinsRebuild?.Value == true);
    internal static bool IsSpawnerPurgeActive => Plugin.MasterSwitch?.Value == true && Plugin.RemoveSpawnersAndGraves?.Value == true;
    internal static bool IsMorgenPurgeActive => Plugin.MasterSwitch?.Value == true && Plugin.RemoveMorgen?.Value == true;

    internal static bool IsBanned(string prefab) =>
        (IsSpawnerPurgeActive && SpawnerAndGravePrefabs.Contains(prefab)) ||
        (IsMorgenPurgeActive && MorgenPrefabs.Contains(prefab));

    private static readonly Dictionary<string, bool> StructuralCache = new(StringComparer.Ordinal);

    /// <summary>Ruin building pieces: Ashlands stone prefabs with a WearNTear (walls, floors,
    /// pillars, arches, stairs). Pots, vines, chests and creatures are not structural.</summary>
    internal static bool IsStructural(string prefab)
    {
        if (StructuralCache.TryGetValue(prefab, out var v)) return v;
        v = false;
        if (prefab.StartsWith("Ashland", StringComparison.Ordinal) && ZNetScene.instance != null)
        {
            var p = ZNetScene.instance.GetPrefab(prefab);
            v = p != null && p.GetComponent<WearNTear>() != null;
        }
        StructuralCache[prefab] = v;
        return v;
    }

    internal static ZoneSystem.ZoneLocation? FindLocation(int hash)
    {
        foreach (var l in ZoneSystem.instance.m_locations)
            if (l?.m_prefab != null && l.m_prefab.IsValid && l.Hash == hash) return l;
        return null;
    }

    internal static ZoneSystem.ZoneLocation? FindLocation(string name) => FindLocation(name.GetStableHashCode());

    internal static string PrefabName(GameObject go) => RuinsReconPatches.BaseName(go.name);

    // ================================================================== blueprint roll replay

    /// <summary>Deactivate park-banned children of a location/room asset for the duration of a spawn.</summary>
    private static List<GameObject> DeactivateBanned(GameObject asset)
    {
        var list = new List<GameObject>();
        foreach (var nv in asset.GetComponentsInChildren<ZNetView>(true))
        {
            if (nv.gameObject == asset || !nv.gameObject.activeSelf) continue;
            // The purge no longer strips blueprints (it hides at runtime, reversibly). Only park exhibits
            // drop creatures and creature spawners: it's a memorial, not a fortress.
            bool ban = RuinsParkPatches.ParkSpawning &&
                (nv.GetComponent<CreatureSpawner>() || nv.GetComponent<SpawnArea>() || nv.GetComponent<Character>() || nv.GetComponent<TriggerSpawner>()
                 || SpawnerAndGravePrefabs.Contains(PrefabName(nv.gameObject)) || MorgenPrefabs.Contains(PrefabName(nv.gameObject)));
            if (ban) { nv.gameObject.SetActive(false); list.Add(nv.gameObject); }
        }
        return list;
    }

    private static bool SubtreeHasStructure(GameObject go)
    {
        foreach (var nv in go.GetComponentsInChildren<ZNetView>(true))
            if (IsStructural(PrefabName(nv.gameObject))) return true;
        return false;
    }

    /// <summary>One replay of vanilla's roll sequence: which structural net children end up active.</summary>
    private static HashSet<ZNetView> Roll(ZNetView[] netViews, RandomSpawn[] rss, int seed, Func<RandomSpawn, Vector3> rsWorldPos,
        Location? loc, DungeonGenerator? dg, bool forced, string key = "")
    {
        bool Relevant(string n) => IsStructural(n) || RuinsBlueprints.GetSwap(key, n) != null;
        bool SubtreeRelevant(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<ZNetView>(true)) if (Relevant(PrefabName(c.gameObject))) return true;
            return false;
        }
        var forcedList = new List<(RandomSpawn, float)>();
        if (forced)
            foreach (var rs in rss)
                if (rs.m_chanceToSpawn < 100f && SubtreeRelevant(rs.gameObject)) { forcedList.Add((rs, rs.m_chanceToSpawn)); rs.m_chanceToSpawn = 100f; }
        var result = new HashSet<ZNetView>();
        var state = UnityEngine.Random.state;
        try
        {
            foreach (var rs in rss) rs.Prepare();
            UnityEngine.Random.InitState(seed);
            foreach (var rs in rss) rs.Randomize(rsWorldPos(rs), loc, dg);
            foreach (var nv in netViews)
                if (nv.gameObject.activeSelf && Relevant(PrefabName(nv.gameObject))) result.Add(nv);
        }
        finally
        {
            UnityEngine.Random.state = state;
            foreach (var rs in rss) rs.Reset();
            foreach (var nv in netViews) nv.gameObject.SetActive(true);
            foreach (var (rs, c) in forcedList) rs.m_chanceToSpawn = c;
        }
        return result;
    }

    internal static GameObject? SpawnTagged(GameObject prefabOrChild, Vector3 pos, Quaternion rot, float scale = 1f)
    {
        var old = WearNTear.m_randomInitialDamage;
        WearNTear.m_randomInitialDamage = false;
        try
        {
            var go = UnityEngine.Object.Instantiate(prefabOrChild, pos, rot);
            var nv = go.GetComponent<ZNetView>();
            if (Mathf.Abs(scale - 1f) > 0.001f && nv != null) nv.SetLocalScale(Vector3.one * scale);
            var zdo = nv?.GetZDO();
            if (zdo != null)
            {
                zdo.Set(RestoreTag, 1);
                zdo.Set(ZDOVars.s_preSnow, true);
                Track(nv!, (int)VisKind.Restore); // Awake ran inside Instantiate, before the tag existed
            }
            return go;
        }
        finally { WearNTear.m_randomInitialDamage = old; }
    }

    /// <summary>
    /// The restoration of one blueprint (location or camp room), given vanilla's roll and the forced roll:
    /// - pieces only in the forced roll are added (tagged), unless they sit in a door slot;
    /// - swap sources (broken floor, charred tree) are added as their swap target instead, and a vanilla
    ///   swap source is marked <see cref="SwappedKey"/> (hidden while restored) with its target added;
    /// - vanilla pieces in a door slot are marked swapped (hidden while restored), nothing replaces them;
    /// - floors over a stairwell keep their broken (open) version.
    /// </summary>
    private static int RestoreCore(string key, HashSet<ZNetView> vanilla, HashSet<ZNetView> full,
        Func<Transform, Vector3> local, Func<Transform, Vector3> world, Func<Transform, Quaternion> worldRot, Vector3 searchCenter)
    {
        var doors = RuinsBlueprints.GetDoors(key);
        bool IsDoor(Transform t)
        {
            var l = local(t);
            foreach (var d in doors) if ((d - l).sqrMagnitude < 0.35f * 0.35f) return true;
            return false;
        }
        // stairs in the restored building, for the stairwell guard
        var stairs = full.Where(nv => PrefabName(nv.gameObject).Contains("Stair")).Select(nv => local(nv.transform)).ToList();
        bool OverStairwell(Transform t, string name)
        {
            if (!name.Contains("Floor")) return false;
            float half = name.Contains("6x6") ? 3.1f : name.Contains("1point5") ? 0.85f : 1.6f;
            var l = local(t);
            foreach (var st in stairs)
                if (Mathf.Abs(st.x - l.x) < half && Mathf.Abs(st.z - l.z) < half && st.y > l.y - 4.5f && st.y < l.y + 0.5f) return true;
            return false;
        }
        var placed = new HashSet<(string, int, int, int)>();
        bool Claim(string name, Vector3 w) =>
            placed.Add((name, Mathf.RoundToInt(w.x * 5f), Mathf.RoundToInt(w.y * 5f), Mathf.RoundToInt(w.z * 5f)));
        foreach (var nv in vanilla) Claim(PrefabName(nv.gameObject), world(nv.transform)); // intact vanilla pieces count

        List<ZDO>? zdos = null;
        int n = 0, swapped = 0, doorHidden = 0;
        foreach (var nv in full)
        {
            var name = PrefabName(nv.gameObject);
            var t = nv.transform;
            bool door = IsDoor(t);
            var swap = RuinsBlueprints.GetSwap(key, name);
            if (swap != null && OverStairwell(t, name)) swap = null;
            bool isVanilla = vanilla.Contains(nv);
            if (isVanilla && (door || swap != null))
            {
                // hide the vanilla piece while restored
                zdos ??= SectorZdos(searchCenter);
                var w = world(t);
                var h = name.GetStableHashCode();
                foreach (var z in zdos)
                    if (z.IsValid() && z.GetPrefab() == h && (z.GetPosition() - w).sqrMagnitude < 0.09f && z.GetLong(ZDOVars.s_creator) == 0)
                    {
                        z.Set(SwappedKey, 1);
                        var inst = ZNetScene.instance.FindInstance(z);
                        if (inst != null) Track(inst, (int)VisKind.Swapped);
                        if (door) doorHidden++; else swapped++;
                    }
            }
            if (door) continue;
            if (isVanilla && swap == null) continue;
            if (!IsStructural(name) && swap == null) continue; // a rolled-on swap source whose swap is guarded off
            if (swap != null)
            {
                var pf = ZNetScene.instance.GetPrefab(swap.Value.to);
                if (pf == null || !Claim(swap.Value.to, world(t))) continue;
                SpawnTagged(pf, world(t), worldRot(t), swap.Value.scale);
            }
            else SpawnTagged(nv.gameObject, world(t), worldRot(t));
            n++;
        }
        if (swapped + doorHidden > 0) Plugin.Log.LogInfo($"{Tag} {key}: {swapped} vanilla pieces swapped, {doorHidden} opened as doors");
        return n;
    }

    private static List<ZDO> SectorZdos(Vector3 c)
    {
        var l = new List<ZDO>();
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(c), new SimulationDistance(1, 1, classic: true), l);
        return l;
    }

    /// <summary>Spawn the restoration of one location instance (vanilla roll vs forced roll diff + symmetry).</summary>
    internal static int AddLocationRestoration(ZoneSystem.ZoneLocation location, int seed, Vector3 pos, Quaternion rot)
    {
        location.m_prefab.Load();
        var asset = location.m_prefab.Asset;
        var root = asset.transform;
        var oldPos = root.position;
        var oldRot = root.rotation;
        root.position = Vector3.zero;
        root.rotation = Quaternion.identity;
        var banned = DeactivateBanned(asset);
        int n = 0;
        try
        {
            var netViews = Utils.GetEnabledComponentsInChildren<ZNetView>(asset);
            var rss = Utils.GetEnabledComponentsInChildren<RandomSpawn>(asset);
            var loc = asset.GetComponent<Location>();
            Vector3 RsPos(RandomSpawn rs) => pos + rot * rs.transform.position;
            var key = location.m_prefab.Name;
            var vanilla = Roll(netViews, rss, seed, RsPos, loc, null, forced: false, key);
            var full = Roll(netViews, rss, seed, RsPos, loc, null, forced: true, key);
            n = RestoreCore(key, vanilla, full, t => t.position, t => pos + rot * t.position, t => rot * t.rotation, pos);
        }
        finally
        {
            foreach (var go in banned) if (go) go.SetActive(true);
            root.position = oldPos;
            root.rotation = oldRot;
            location.m_prefab.Release();
        }
        n += RuinsBlueprints.SpawnAdditions(location.m_prefab.Name, pos, rot);
        return n;
    }

    // ================================================================== new-zone spawn hooks

    private static int _fullSpawnDepth;

    [HarmonyPatch(typeof(ZoneSystem), "SpawnLocation")]
    private static class SpawnLocationPatch
    {
        private static void Prefix(ZoneSystem.ZoneLocation location, ZoneSystem.SpawnMode mode, out List<GameObject>? __state)
        {
            __state = null;
            try
            {
                if (Plugin.MasterSwitch?.Value != true && !SuppressRestore && RebuildOverride == null) return;
                if (location == null || (location.m_biome & Heightmap.Biome.AshLands) == 0) return;
                location.m_prefab.Load();
                if (location.m_prefab.Asset == null) { location.m_prefab.Release(); return; }
                __state = DeactivateBanned(location.m_prefab.Asset);
                if (mode == ZoneSystem.SpawnMode.Full) _fullSpawnDepth++;
            }
            catch (Exception e) { Plugin.Log.LogError($"{Tag} SpawnLocation prefix: {e}"); }
        }

        private static void Postfix(ZoneSystem.ZoneLocation location, int seed, Vector3 pos, Quaternion rot,
            ZoneSystem.SpawnMode mode, List<GameObject>? __state)
        {
            if (__state == null) return;
            try
            {
                foreach (var go in __state) if (go) go.SetActive(true);
                if (mode == ZoneSystem.SpawnMode.Full)
                {
                    _fullSpawnDepth--;
                    if (IsRebuildActive && !SuppressRestore && RuinLocations.Contains(location.m_prefab.Name))
                    {
                        int n = AddLocationRestoration(location, seed, pos, rot);
                        Plugin.Log.LogInfo($"{Tag} restored new {location.m_prefab.Name} at {pos}: +{n} pieces");
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogError($"{Tag} SpawnLocation postfix: {e}"); }
            finally { location.m_prefab.Release(); }
        }
    }

    /// <summary>Stamp proxies created by a full spawn: restored now, or kept ruined (park exhibits).</summary>
    [HarmonyPatch(typeof(LocationProxy), nameof(LocationProxy.SetLocation))]
    private static class SetLocationPatch
    {
        private static void Postfix(LocationProxy __instance, string location)
        {
            if (_fullSpawnDepth <= 0 || !RuinLocations.Contains(location)) return;
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return;
            if (SuppressRestore) zdo.Set(KeepRuinedKey, 1);
            else if (IsRebuildActive) zdo.Set(RestoredKey, RestoreVersion);
        }
    }

    // ================================================================== existing-world restoration

    private struct PendingRestore
    {
        public ZNetView Owner;      // LocationProxy or DungeonGenerator net view
        public float Due;
        public List<(DungeonDB.RoomData data, Vector3 pos, Quaternion rot)>? Rooms;
    }

    private static readonly List<PendingRestore> Pending = new();

    private static bool NeedsRestore(ZDO? zdo) =>
        zdo != null && zdo.GetInt(KeepRuinedKey) == 0 && zdo.GetInt(RestoredKey) < RestoreVersion;

    [HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
    private static class ProxySpawnPatch
    {
        private static void Postfix(LocationProxy __instance, bool __result)
        {
            if (!__result) return;
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null && zdo.GetInt(KeepRuinedKey) == 1)
            {
                // a kept-ruined exhibit: its pieces keep their damage visuals even while restored
                var c = __instance.transform.position;
                if (!KeptRuinAreas.Any(a => (a.center - c).sqrMagnitude < 1f))
                {
                    KeptRuinAreas.Add((c, 45f));
                    RefreshDamageVisuals();
                }
                return;
            }
            QueueProxy(__instance);
        }
    }

    private static void QueueProxy(LocationProxy proxy)
    {
        if (!IsRebuildActive || ZNet.instance == null || !ZNet.instance.IsServer()) return;
        var nv = proxy.GetComponent<ZNetView>();
        var zdo = nv?.GetZDO();
        if (!NeedsRestore(zdo)) return;
        var loc = FindLocation(zdo!.GetInt(ZDOVars.s_location));
        if (loc == null || !RuinLocations.Contains(loc.m_prefab.Name)) return;
        Pending.Add(new PendingRestore { Owner = nv!, Due = Time.time + 2f });
    }

    // ---- camp ruins (DungeonGenerator)

    /// <summary>AshlandRuins / FortressRuins are "camp" dungeon generators that scatter house
    /// rooms; each room prefab carries its own RandomSpawn rolls.</summary>
    internal static bool IsRuinDG(DungeonGenerator dg) =>
        dg != null && (dg.name.StartsWith("DG_AshlandRuins", StringComparison.Ordinal) || dg.name.StartsWith("DG_FortressRuins", StringComparison.Ordinal));

    [HarmonyPatch(typeof(DungeonGenerator), "PlaceRoom",
        new[] { typeof(DungeonDB.RoomData), typeof(Vector3), typeof(Quaternion), typeof(RoomConnection), typeof(ZoneSystem.SpawnMode) })]
    private static class PlaceRoomPatch
    {
        private static void Prefix(DungeonGenerator __instance, DungeonDB.RoomData roomData, out List<GameObject>? __state)
        {
            __state = null;
            if (!IsRuinDG(__instance)) return;
            roomData.m_prefab.Load();
            __state = DeactivateBanned(roomData.m_prefab.Asset);
        }

        private static void Postfix(DungeonGenerator __instance, DungeonDB.RoomData roomData, Vector3 pos, Quaternion rot,
            ZoneSystem.SpawnMode mode, List<GameObject>? __state)
        {
            if (__state == null) return;
            try
            {
                foreach (var go in __state) if (go) go.SetActive(true);
                RoomLog?.Add((roomData.m_prefab.Name, pos, rot));
                if (mode == ZoneSystem.SpawnMode.Full && IsRebuildActive && !SuppressRestore)
                {
                    // The generator places its next room from the global Random sequence; the restoration's
                    // spawns (Awake etc.) must not consume it, or a restored camp lays out differently.
                    var st = UnityEngine.Random.state;
                    try { AddRoomRestoration(__instance, roomData, pos, rot); }
                    finally { UnityEngine.Random.state = st; }
                }
            }
            catch (Exception e) { Plugin.Log.LogError($"{Tag} PlaceRoom postfix: {e}"); }
            finally { roomData.m_prefab.Release(); }
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.Generate), new[] { typeof(int), typeof(ZoneSystem.SpawnMode) })]
    private static class DGGeneratePatch
    {
        private static void Postfix(DungeonGenerator __instance, ZoneSystem.SpawnMode mode)
        {
            if (mode != ZoneSystem.SpawnMode.Full || !IsRuinDG(__instance)) return;
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null) return;
            if (SuppressRestore) zdo.Set(KeepRuinedKey, 1);
            else if (IsRebuildActive) zdo.Set(RestoredKey, RestoreVersion);
        }
    }

    /// <summary>Park / harness: while set, a camp ruin's room layout depends on its seed alone. Vanilla also
    /// rejects room spots by ground slope, altitude and the zone's bounds, so two camps with the same seed
    /// on different plots still came out different (park run: 8 vs 12 rooms).</summary>
    internal static bool CampLayoutSeedOnly;

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.Generate), new[] { typeof(int), typeof(ZoneSystem.SpawnMode) })]
    private static class DGSeedOnlyPatch
    {
        private static void Prefix(DungeonGenerator __instance)
        {
            if (!CampLayoutSeedOnly || !IsRuinDG(__instance)) return;
            __instance.m_maxTilt = 90f;        // cos(90) = 0: any ground normal passes
            __instance.m_minAltitude = -1e6f;  // any altitude passes
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), "IsInsideDungeon")]
    private static class DGInsidePatch
    {
        private static bool Prefix(DungeonGenerator __instance, ref bool __result)
        {
            if (!CampLayoutSeedOnly || !IsRuinDG(__instance)) return true;
            __result = true;
            return false;
        }
    }

    /// <summary>Existing camps: capture the saved room list when the generator re-places its rooms.</summary>
    [HarmonyPatch(typeof(DungeonGenerator), "Spawn")]
    private static class DGSpawnPatch
    {
        private static void Prefix(DungeonGenerator __instance)
        {
            try
            {
                if (!IsRebuildActive || !IsRuinDG(__instance) || ZNet.instance == null || !ZNet.instance.IsServer()) return;
                var nv = __instance.GetComponent<ZNetView>();
                if (!NeedsRestore(nv?.GetZDO())) return;
                var rooms = CaptureRooms(__instance);
                if (rooms != null) Pending.Add(new PendingRestore { Owner = nv!, Due = Time.time + 2f, Rooms = rooms });
            }
            catch (Exception e) { Plugin.Log.LogError($"{Tag} DG spawn capture: {e}"); }
        }
    }

    private static readonly Dictionary<DungeonGenerator, List<(DungeonDB.RoomData, Vector3, Quaternion)>> RoomCache = new();

    private static List<(DungeonDB.RoomData data, Vector3 pos, Quaternion rot)>? CaptureRooms(DungeonGenerator dg)
    {
        var arr = Traverse.Create(dg).Field("m_loadedRooms").GetValue() as Array;
        if (arr == null) return null;
        var rooms = new List<(DungeonDB.RoomData, Vector3, Quaternion)>();
        foreach (var o in arr)
        {
            var t = Traverse.Create(o);
            rooms.Add((t.Field("m_roomData").GetValue<DungeonDB.RoomData>(), t.Field("m_position").GetValue<Vector3>(), t.Field("m_rotation").GetValue<Quaternion>()));
        }
        RoomCache[dg] = rooms;
        return rooms;
    }

    internal static int AddRoomRestoration(DungeonGenerator dg, DungeonDB.RoomData data, Vector3 pos, Quaternion rot)
    {
        data.m_prefab.Load();
        var asset = data.m_prefab.Asset;
        var banned = DeactivateBanned(asset);
        int n = 0;
        try
        {
            var room = asset.GetComponent<Room>();
            var rPos = room.transform.position;
            var rInv = Quaternion.Inverse(room.transform.rotation);
            Vector3 W(Transform t) => pos + rot * (rInv * (t.position - rPos));
            var netViews = Utils.GetEnabledComponentsInChildren<ZNetView>(asset);
            var rss = Utils.GetEnabledComponentsInChildren<RandomSpawn>(asset);
            var v = dg.m_useCustomInteriorTransform ? pos - dg.transform.position : pos;
            int seed = (int)v.x * 4271 + (int)v.y * 9187 + (int)v.z * 2134;
            if (dg.m_addBaseSeedToRandomSpawn) seed += dg.GetSeed();
            var key = "room:" + data.m_prefab.Name;
            var vanilla = Roll(netViews, rss, seed, rs => W(rs.transform), null, dg, forced: false, key);
            var full = Roll(netViews, rss, seed, rs => W(rs.transform), null, dg, forced: true, key);
            n = RestoreCore(key, vanilla, full, t => rInv * (t.position - rPos), W, t => rot * (rInv * t.rotation), pos);
        }
        finally
        {
            foreach (var go in banned) if (go) go.SetActive(true);
            data.m_prefab.Release();
        }
        return n;
    }

    /// <summary>Delete previously added restoration pieces near a ruin (version upgrade).</summary>
    private static int ClearRestoration(Vector3 center, float radius)
    {
        var zdos = new List<ZDO>();
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(center), new SimulationDistance(1, 1, classic: true), zdos);
        int n = 0;
        foreach (var z in zdos)
            if (z.IsValid() && z.GetInt(RestoreTag) == 1 && (z.GetPosition() - center).sqrMagnitude < radius * radius) { DeleteZdo(z); n++; }
        return n;
    }

    private static void RunPending(PendingRestore p)
    {
        if (p.Owner == null || !p.Owner.IsValid()) return;
        var zdo = p.Owner.GetZDO();
        if (!NeedsRestore(zdo)) return;
        int cleared = zdo.GetInt(RestoredKey) > 0 ? ClearRestoration(p.Owner.transform.position, 40f) : 0;
        int n = 0;
        string what;
        if (p.Rooms != null)
        {
            var dg = p.Owner.GetComponent<DungeonGenerator>();
            foreach (var (data, pos, rot) in p.Rooms) n += AddRoomRestoration(dg, data, pos, rot);
            what = dg.name;
        }
        else
        {
            var loc = FindLocation(zdo.GetInt(ZDOVars.s_location))!;
            n = AddLocationRestoration(loc, zdo.GetInt(ZDOVars.s_seed), p.Owner.transform.position, p.Owner.transform.rotation);
            what = loc.m_prefab.Name;
        }
        zdo.Set(RestoredKey, RestoreVersion);
        Plugin.Log.LogInfo($"{Tag} restored existing {what} at {p.Owner.transform.position}: +{n} pieces (cleared {cleared} old)");
    }

    // ================================================================== show / hide (MasterSwitch)

    internal static void DeleteZdo(ZDO z)
    {
        var inst = ZNetScene.instance.FindInstance(z);
        if (!z.IsOwner()) z.SetOwner(ZDOMan.GetSessionID());
        if (inst != null)
        {
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            ZNetScene.instance.Destroy(inst.gameObject);
        }
        else ZDOMan.instance.DestroyZDO(z);
    }

    /// <summary>MasterSwitch / RebuildRuins changed: flip every loaded ruin between ruined and restored.</summary>
    internal static void OnRestoreToggled()
    {
        bool show = IsRebuildActive;
        int hidden = ApplyVisibility();
        RefreshDamageVisuals();
        if (show)
        {
            foreach (var proxy in UnityEngine.Object.FindObjectsByType<LocationProxy>(FindObjectsSortMode.None)) QueueProxy(proxy);
            foreach (var dg in UnityEngine.Object.FindObjectsByType<DungeonGenerator>(FindObjectsSortMode.None))
            {
                var nv = dg.GetComponent<ZNetView>();
                if (IsRuinDG(dg) && NeedsRestore(nv?.GetZDO()) && RoomCache.TryGetValue(dg, out var rooms))
                    Pending.Add(new PendingRestore { Owner = nv!, Due = Time.time + 0.5f, Rooms = rooms });
            }
        }
        Plugin.Log.LogInfo($"{Tag} ruins {(show ? "RESTORED" : "RUINED")} ({Tracked.Count} tracked objects, {hidden} hidden)");
    }

    private static readonly System.Reflection.MethodInfo? UpdateVisualMethod = AccessTools.Method(typeof(WearNTear), "UpdateVisual");

    private static void RefreshDamageVisuals()
    {
        foreach (var w in WearNTear.GetAllInstances())
            if (w && IsRuinPiece(w)) UpdateVisualMethod?.Invoke(w, new object[] { false });
    }

    /// <summary>A world-generated ruin piece (not player-built).</summary>
    private static bool IsRuinPiece(WearNTear w)
    {
        var nv = w.GetComponent<ZNetView>();
        var zdo = nv?.GetZDO();
        return zdo != null && zdo.GetLong(ZDOVars.s_creator) == 0 && IsStructural(PrefabName(w.gameObject));
    }

    /// <summary>Restored ruins read as new: the worn/broken damage meshes only show while ruined.</summary>
    [HarmonyPatch(typeof(WearNTear), "SetHealthVisual")]
    private static class HealthVisualPatch
    {
        private static void Prefix(WearNTear __instance, ref float health)
        {
            if (health < 1f && IsRebuildActive && IsRuinPiece(__instance) && !InKeptRuin(__instance.transform.position)) health = 1f;
        }
    }

    /// <summary>Park "before" exhibits keep their damage visuals.</summary>
    internal static readonly List<(Vector3 center, float radius)> KeptRuinAreas = new();
    private static bool InKeptRuin(Vector3 p)
    {
        foreach (var (c, r) in KeptRuinAreas) if ((p - c).sqrMagnitude < r * r) return true;
        return false;
    }

    // ================================================================== visibility (purge, restoration, swaps)

    /// <summary>Vanilla pieces replaced by a swap (broken floor -> intact floor, charred tree -> living
    /// tree, doorway walls): shown only while ruined.</summary>
    internal static readonly int SwappedKey = "ar_swapped".GetStableHashCode();

    private enum VisKind { Restore, Swapped, Purge }
    private static readonly Dictionary<ZNetView, VisKind> Tracked = new();
    private static readonly List<ZNetView> VisQueue = new();

    private static bool ShouldShow(ZNetView nv, VisKind k) => k switch
    {
        VisKind.Restore => IsRebuildActive,
        VisKind.Swapped => !IsRebuildActive,
        _ => !IsBanned(PrefabName(nv.gameObject)),
    };

    internal static void Track(ZNetView nv, int kind)
    {
        Tracked[nv] = (VisKind)kind;
        VisQueue.Add(nv);
    }

    [HarmonyPatch(typeof(ZNetView), "Awake")]
    private static class NetViewAwakePatch
    {
        private static void Postfix(ZNetView __instance)
        {
            var zdo = __instance.GetZDO();
            if (zdo == null) return;
            if (zdo.GetInt(RestoreTag) == 1) Track(__instance, (int)VisKind.Restore);
            else if (zdo.GetInt(SwappedKey) == 1) Track(__instance, (int)VisKind.Swapped);
            else
            {
                var name = PrefabName(__instance.gameObject);
                if (SpawnerAndGravePrefabs.Contains(name) || MorgenPrefabs.Contains(name)) Track(__instance, (int)VisKind.Purge);
            }
        }
    }

    private static int ApplyVisibility()
    {
        int hidden = 0;
        var dead = new List<ZNetView>();
        foreach (var kv in Tracked)
        {
            if (kv.Key == null) { dead.Add(kv.Key!); continue; }
            var show = ShouldShow(kv.Key, kv.Value);
            if (kv.Key.gameObject.activeSelf != show) kv.Key.gameObject.SetActive(show);
            if (!show) hidden++;
        }
        foreach (var d in dead) Tracked.Remove(d);
        return hidden;
    }

    private static readonly Dictionary<SpawnSystem.SpawnData, bool> SpawnDataOriginal = new();

    private static void ApplySpawnListEdits()
    {
        foreach (var list in Resources.FindObjectsOfTypeAll<SpawnSystemList>())
            foreach (var s in list.m_spawners)
            {
                if (s?.m_prefab == null || !MorgenPrefabs.Contains(s.m_prefab.name)) continue;
                if (!SpawnDataOriginal.ContainsKey(s)) SpawnDataOriginal[s] = s.m_enabled;
                s.m_enabled = !IsMorgenPurgeActive && SpawnDataOriginal[s];
            }
    }

    /// <summary>MasterSwitch / RemoveSpawnersAndGraves / RemoveMorgen changed: hide or reveal the purged
    /// objects (nothing is ever deleted) and re-enable or disable Morgen biome spawns.</summary>
    internal static void OnPurgeConfigChanged()
    {
        ApplySpawnListEdits();
        int hidden = ApplyVisibility();
        Plugin.Log.LogInfo($"{Tag} purge {(IsSpawnerPurgeActive ? "spawners/graves hidden" : "spawners/graves shown")}, " +
                           $"{(IsMorgenPurgeActive ? "Morgen hidden" : "Morgen shown")} ({hidden} objects hidden)");
    }

    private static ZNetScene? _spawnListsAppliedFor;
    private static int _purged;

    /// <summary>Called from Plugin.Update() every frame.</summary>
    internal static void Tick()
    {
        if (ZNetScene.instance == null) { Tracked.Clear(); VisQueue.Clear(); return; }
        if (_spawnListsAppliedFor != ZNetScene.instance)
        {
            _spawnListsAppliedFor = ZNetScene.instance;
            ApplySpawnListEdits();
        }

        if (VisQueue.Count > 0)
        {
            foreach (var nv in VisQueue)
                if (nv && Tracked.TryGetValue(nv, out var k))
                {
                    var show = ShouldShow(nv, k);
                    if (nv.gameObject.activeSelf != show) nv.gameObject.SetActive(show);
                }
            VisQueue.Clear();
        }

        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            var p = Pending[i];
            if (Time.time < p.Due) continue;
            Pending.RemoveAt(i);
            if (!IsRebuildActive) continue;
            try { RunPending(p); }
            catch (Exception e) { Plugin.Log.LogError($"{Tag} restore failed: {e}"); }
        }
    }
}
