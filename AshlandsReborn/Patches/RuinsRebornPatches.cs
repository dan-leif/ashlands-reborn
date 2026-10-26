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
    internal const int RestoreVersion = 2;
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

    /// <summary>Deactivate banned children of a location/room asset for the duration of a spawn.</summary>
    private static List<GameObject> DeactivateBanned(GameObject asset)
    {
        var list = new List<GameObject>();
        foreach (var nv in asset.GetComponentsInChildren<ZNetView>(true))
        {
            if (nv.gameObject == asset || !nv.gameObject.activeSelf) continue;
            bool ban = IsBanned(PrefabName(nv.gameObject));
            // Park exhibits: no creatures and no creature spawners (it's a memorial, not a fortress)
            if (!ban && RuinsParkPatches.ParkSpawning)
                ban = nv.GetComponent<CreatureSpawner>() || nv.GetComponent<SpawnArea>() || nv.GetComponent<Character>() || nv.GetComponent<TriggerSpawner>();
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
        Location? loc, DungeonGenerator? dg, bool forced)
    {
        var forcedList = new List<(RandomSpawn, float)>();
        if (forced)
            foreach (var rs in rss)
                if (rs.m_chanceToSpawn < 100f && SubtreeHasStructure(rs.gameObject)) { forcedList.Add((rs, rs.m_chanceToSpawn)); rs.m_chanceToSpawn = 100f; }
        var result = new HashSet<ZNetView>();
        var state = UnityEngine.Random.state;
        try
        {
            foreach (var rs in rss) rs.Prepare();
            UnityEngine.Random.InitState(seed);
            foreach (var rs in rss) rs.Randomize(rsWorldPos(rs), loc, dg);
            foreach (var nv in netViews)
                if (nv.gameObject.activeSelf && IsStructural(PrefabName(nv.gameObject))) result.Add(nv);
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

    internal static GameObject? SpawnTagged(GameObject prefabOrChild, Vector3 pos, Quaternion rot)
    {
        var old = WearNTear.m_randomInitialDamage;
        WearNTear.m_randomInitialDamage = false;
        try
        {
            var go = UnityEngine.Object.Instantiate(prefabOrChild, pos, rot);
            var nv = go.GetComponent<ZNetView>();
            var zdo = nv?.GetZDO();
            if (zdo != null)
            {
                zdo.Set(RestoreTag, 1);
                zdo.Set(ZDOVars.s_preSnow, true);
                Tagged.Add(nv!); // Awake ran inside Instantiate, before the tag existed
                if (!IsRebuildActive) HideQueue.Add(nv!);
            }
            return go;
        }
        finally { WearNTear.m_randomInitialDamage = old; }
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
            var vanilla = Roll(netViews, rss, seed, RsPos, loc, null, forced: false);
            var full = Roll(netViews, rss, seed, RsPos, loc, null, forced: true);
            foreach (var nv in full)
            {
                if (vanilla.Contains(nv)) continue;
                SpawnTagged(nv.gameObject, pos + rot * nv.transform.position, rot * nv.transform.rotation);
                n++;
            }
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
                if (mode == ZoneSystem.SpawnMode.Full && IsRebuildActive && !SuppressRestore)
                    AddRoomRestoration(__instance, roomData, pos, rot);
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
            var vanilla = Roll(netViews, rss, seed, rs => W(rs.transform), null, dg, forced: false);
            var full = Roll(netViews, rss, seed, rs => W(rs.transform), null, dg, forced: true);
            foreach (var nv in full)
            {
                if (vanilla.Contains(nv)) continue;
                SpawnTagged(nv.gameObject, W(nv.transform), rot * (rInv * nv.transform.rotation));
                n++;
            }
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

    private static readonly HashSet<ZNetView> Tagged = new();
    private static readonly List<ZNetView> HideQueue = new();

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
        Tagged.RemoveWhere(nv => nv == null);
        foreach (var nv in Tagged) if (nv.gameObject.activeSelf != show) nv.gameObject.SetActive(show);
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
        Plugin.Log.LogInfo($"{Tag} ruins {(show ? "RESTORED" : "RUINED")} ({Tagged.Count} restoration pieces loaded)");
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

    // ================================================================== purge + tag tracking

    private static readonly List<ZNetView> PurgeQueue = new();

    [HarmonyPatch(typeof(ZNetView), "Awake")]
    private static class NetViewAwakePatch
    {
        private static void Postfix(ZNetView __instance)
        {
            var zdo = __instance.GetZDO();
            if (zdo == null) return;
            if (zdo.GetInt(RestoreTag) == 1)
            {
                Tagged.Add(__instance);
                if (!IsRebuildActive) HideQueue.Add(__instance);
                return;
            }
            if (Plugin.MasterSwitch?.Value == true && IsBanned(PrefabName(__instance.gameObject))) PurgeQueue.Add(__instance);
        }
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

    internal static void OnPurgeConfigChanged()
    {
        ApplySpawnListEdits();
        if (IsSpawnerPurgeActive || IsMorgenPurgeActive)
            foreach (var nv in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                if (nv.GetZDO() != null && IsBanned(PrefabName(nv.gameObject))) PurgeQueue.Add(nv);
    }

    private static ZNetScene? _spawnListsAppliedFor;
    private static int _purged;

    /// <summary>Called from Plugin.Update() every frame.</summary>
    internal static void Tick()
    {
        if (ZNetScene.instance == null) { Tagged.Clear(); return; }
        if (_spawnListsAppliedFor != ZNetScene.instance)
        {
            _spawnListsAppliedFor = ZNetScene.instance;
            ApplySpawnListEdits();
        }

        if (HideQueue.Count > 0)
        {
            bool show = IsRebuildActive;
            foreach (var nv in HideQueue) if (nv && nv.gameObject.activeSelf != show) nv.gameObject.SetActive(show);
            HideQueue.Clear();
        }

        if (PurgeQueue.Count > 0)
        {
            foreach (var nv in PurgeQueue)
            {
                if (nv == null || !nv.IsValid()) continue;
                if (!nv.IsOwner()) nv.ClaimOwnership();
                ZNetScene.instance.Destroy(nv.gameObject);
                _purged++;
            }
            PurgeQueue.Clear();
            Plugin.Log.LogInfo($"{Tag} purged total={_purged}");
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
