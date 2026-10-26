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
/// The Ember Memorial: a walkable park showing every ruin type twice - as the cataclysm left it
/// (kept ruined forever) and as it was rebuilt (restoration toggles with the MasterSwitch).
///
/// Layout (the Ember Walk): a paved avenue from an entrance gate to the Garden of Embers. Each
/// station straddles the trail - the ruined exhibit on the walker's left, the restored one on the
/// right - with a pole sign naming the internal location id at each, a story runestone read on
/// the approach, and blue memorial torches. Stations are placed greedily on the flattest dry
/// ground ahead (WorldGenerator search), so the avenue winds with the land. Interlude runestones between stations tell the story of the
/// cataclysm and the rebuilding. Ever-burning torches line the trail, meadow trees the inside.
///
/// Everything is ordinary world data (location proxies, pieces, terrain edits), tagged with
/// <see cref="ParkTag"/> so a rebuild can clear the old park first. Runestones are real
/// Runestone_Ashlands locations whose proxy ZDO carries our inscription (<see cref="RuneTextPatch"/>).
///
/// Dev automation: <c>RuinsParkBuild</c> (search/build, writes <c>RuinsParkPos</c>, switches
/// itself off), <c>RuinsParkTour</c> (per-station FPS measurement + screenshots, log
/// "[AR Park] FPS ..." and "[AR Park] TOUR DONE"). <c>RuinsParkGap</c> spreads the stations out.
/// </summary>
internal static class RuinsParkPatches
{
    private const string Tag = "[AR Park]";
    internal static readonly int ParkTag = "ar_park".GetStableHashCode();
    internal static readonly int RuneTopicKey = "ar_rune_topic".GetStableHashCode();
    internal static readonly int RuneTextKey = "ar_rune_text".GetStableHashCode();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>True while the park spawns exhibits: creatures and creature spawners are stripped.</summary>
    internal static bool ParkSpawning;

    private sealed class Exhibit
    {
        public string Id = "";
        public float R;          // footprint radius (m)
        public string Title = "";
        public string Lore = "";
        public int Seed;
    }

    // Narrative order: humble arbors first, the great cathedral last.
    private static readonly Exhibit[] Exhibits =
    {
        new() { Id = "CharredTowerRuins2", R = 13, Seed = 1102, Title = "The Arbor of the Old Tree",
            Lore = "Before the fire, every village grew around a tree. Twisted stone arches were raised to cradle it, so that the roots and the house would grow as one. When the sky burned, the arches cracked, but the tree held them still. We rebuilt the arbor around the same roots." },
        new() { Id = "CharredTowerRuins3", R = 13, Seed = 1103, Title = "The Ring of Pillars",
            Lore = "A gathering ring where the elders spoke. Here the first warning of the burning sky was read aloud, and here the last of the elders fell silent. Its pillars stand again, a circle unbroken." },
        new() { Id = "CharredRuins4", R = 11, Seed = 1104, Title = "The Watchtower",
            Lore = "A slender watchtower, the eyes of the coast. Its keepers lit the first beacon when the ash began to fall. The beacon was never answered. Rebuilt, it watches over a quieter sea." },
        new() { Id = "CharredRuins3", R = 11, Seed = 1105, Title = "The Bell Tower",
            Lore = "Five storeys of stone and a bell that rang the hours. When the cataclysm came, it rang until the tower fell around it. Its storeys are whole again; listen for it at dusk." },
        new() { Id = "CharredRuins2", R = 16, Seed = 1106, Title = "The Chapel of Spires",
            Lore = "A chapel crowned with a forest of stone spires. The Charred made it their roost in the long ash; their banners hung from its broken windows. Every spire has been raised again to the sky that once betrayed it." },
        new() { Id = "CharredTowerRuins1", R = 22, Seed = 1107, Title = "The Red-Roofed Hall",
            Lore = "A great hall of red tile, where a hundred families shared their bread. The roof fell in a single night. We laid every tile again by hand, and the hall is full once more." },
        new() { Id = "CharredTowerRuins1_dvergr", R = 22, Seed = 1108, Title = "The Hall of the Dvergr Guests",
            Lore = "The same hall, built a second time in the dwarven quarter. Long after the fall, the Dvergr camped among its broken walls and kept its hearth warm. This restoration is our thanks to them." },
        new() { Id = "FortressRuins", R = 35, Seed = 1109, Title = "The Fortress Quarter",
            Lore = "Houses that grew in the shadow of the fortress walls. When the fortress turned against its people, these homes were the first to burn. Their courtyards bloom again." },
        new() { Id = "AshlandRuins", R = 35, Seed = 1110, Title = "The Twin Towers of the Market Town",
            Lore = "A market town ringed by stone houses, its twin towers seen from three valleys away. In the long ash they were the tallest graves in the land. Now they are the tallest homes." },
        new() { Id = "CharredRuins1", R = 30, Seed = 1111, Title = "The Cathedral of the Ember Throne",
            Lore = "The heart of the old civilization: a cathedral-palace of a hundred spires, where the Ember Throne once stood. It was the last place to fall and the last to rise. Behold it whole. Remember what was lost, so that it is never lost again." },
    };

    private static readonly (string topic, string text)[] Interludes =
    {
        ("I. The Burning Sky", "It began with a light in the south, brighter than the sun. Then the sky caught fire and rained ash for a hundred days. Stone that had stood a thousand winters cracked like river ice."),
        ("II. The Rising of the Charred", "The fallen did not rest. From graves of black stone they rose, charred and silent, and the living fled north across the sea. For an age, only the dead walked these halls."),
        ("III. The Long Ash", "Lava ran where rivers had run. The spawning stones pulsed in the dark. Those who returned found only ruin, and named this land for its ashes."),
        ("IV. The Breaking of the Stones", "At last the heroes came. They shattered the spawning stones and swept away the graves, and the Charred walked no more. The bone-wretches of the pits were driven out, never to return."),
        ("V. The Rebuilding", "Then the children of the survivors came home. Stone by stone, from the old plans carved in the foundations, they raised every hall exactly as it had been. This walk is their promise: we remember the dark times, and we build."),
    };

    private const string EntranceTopic = "The Ember Memorial";
    private const string EntranceText =
        "Traveller, you stand at the gate of the Ember Memorial.\n\n" +
        "Here the houses of the Ashlands people are shown twice: on your left as the cataclysm left them, on your right as their children raised them again. " +
        "Follow the path from the smallest arbor to the great cathedral, and on to the Garden of Embers.\n\n" +
        "Walk slowly. Read the stones. Remember the dark times, so that they never return.";
    private const string GardenTopic = "The Garden of Embers";
    private const string GardenText =
        "Where the first ember fell, a meadow grows. Every tree in this garden was planted for a hall that was rebuilt. " +
        "When all the halls of the Ashlands stand again, the garden will be complete.";

    // ================================================================== layout (the Ember Walk)

    private sealed class Station
    {
        public int Index;            // -1 = entrance, Exhibits.Length = garden
        public Exhibit? Ex;
        public Vector3 P;            // trail point (y unset until built)
        public Vector3 D;            // walking direction arriving here
        public Vector3 Right => Vector3.Cross(Vector3.up, D);
        public (string, string)? Interlude;  // stone on the trail leading INTO this station
    }

    private static float Gap => Mathf.Max(10f, Plugin.RuinsParkGap?.Value ?? 40f);
    private const float EntranceR = 12f, GardenR = 20f;

    /// <summary>Plot centers: ruined on the walker's left, restored on the right.</summary>
    private static (Vector3 ruined, Vector3 restored) PairCenters(Station s) =>
        (s.P - s.Right * (s.Ex!.R + 7f), s.P + s.Right * (s.Ex.R + 7f));

    private static float StationR(Station s) => s.Ex?.R ?? (s.Index < 0 ? EntranceR : GardenR);

    private static readonly int[] InterludeBefore = { 2, 4, 6, 8, 9 };

    private static List<Station>? ParseLayout(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw!.Contains(";")) return null;
        var pts = new List<Vector3>();
        foreach (var part in raw.Split(';'))
        {
            var f = part.Split(',');
            if (f.Length != 2) return null;
            pts.Add(new Vector3(float.Parse(f[0], Inv), 0f, float.Parse(f[1], Inv)));
        }
        if (pts.Count != Exhibits.Length + 2) return null;
        return BuildStations(pts);
    }

    private static List<Station> BuildStations(List<Vector3> pts)
    {
        var list = new List<Station>();
        for (int i = 0; i < pts.Count; i++)
        {
            var d = i == 0 ? (pts[1] - pts[0]) : (pts[i] - pts[i - 1]);
            d.y = 0; d.Normalize();
            var s = new Station { Index = i - 1, P = pts[i], D = d };
            if (i >= 1 && i <= Exhibits.Length)
            {
                s.Ex = Exhibits[i - 1];
                int k = Array.IndexOf(InterludeBefore, i - 1);
                if (k >= 0) s.Interlude = Interludes[k];
            }
            list.Add(s);
        }
        return list;
    }

    private static string FormatLayout(List<Station> st) =>
        string.Join(";", st.Select(s => string.Format(Inv, "{0:0.0},{1:0.0}", s.P.x, s.P.z)));

    private static List<Station>? CurrentLayout() => ParseLayout(Plugin.RuinsParkPos?.Value);

    // ================================================================== triggers

    private static bool _running, _firedBuild, _firedTour, _firedPortal;
    private static float _worldLoadedAt = -1f;

    internal static void Tick()
    {
        if (Player.m_localPlayer == null) { _worldLoadedAt = -1f; _firedBuild = _firedTour = _firedPortal = false; return; }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;
        if (_running || Time.time - _worldLoadedAt < 10f) return;
        if (!_firedBuild && (Plugin.RuinsParkBuild?.Value ?? false))
        {
            _firedBuild = true;
            Plugin.Instance.StartCoroutine(Guard(BuildRoutine()));
        }
        else if (!_firedPortal && (Plugin.RuinsParkPortal?.Value ?? false))
        {
            _firedPortal = true;
            Plugin.Instance.StartCoroutine(Guard(PortalRoutine()));
        }
        else if (!_firedTour && (Plugin.RuinsParkTour?.Value ?? false))
        {
            _firedTour = true;
            Plugin.Instance.StartCoroutine(Guard(TourRoutine()));
        }
    }

    private static IEnumerator Guard(IEnumerator inner)
    {
        _running = true;
        Application.runInBackground = true;
        Player.m_localPlayer?.SetGodMode(true);
        while (true)
        {
            object? cur;
            try
            {
                if (!inner.MoveNext()) break;
                cur = inner.Current;
            }
            catch (Exception e) { Plugin.Log.LogError($"{Tag} failed: {e}"); break; }
            yield return cur;
        }
        ParkSpawning = false;
        RuinsRebornPatches.SuppressRestore = false;
        PhotoModePatches.ClearCameraOverride();
        FableBunnyPatches.SetDebugDaylight(false);
        if (Hud.instance && Hud.instance.m_rootObject) Hud.instance.m_rootObject.SetActive(true);
        _running = false;
    }

    // ================================================================== site search (greedy flat-ground chain)

    private static float Relief(WorldGenerator wg, Vector3 c, float r, out float lo)
    {
        lo = float.MaxValue; float hi = float.MinValue;
        for (float dx = -r; dx <= r; dx += 7f)
            for (float dz = -r; dz <= r; dz += 7f)
            {
                if (dx * dx + dz * dz > r * r) continue;
                var h = wg.GetHeight(c.x + dx, c.z + dz);
                lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
            }
        return hi - lo;
    }

    private static bool LandOk(WorldGenerator wg, Vector3 p)
    {
        var h = wg.GetHeight(p.x, p.z);
        var b = wg.GetBiome(p.x, p.z);
        return h >= 31.5f && h <= 120f && (b == Heightmap.Biome.Meadows || b == Heightmap.Biome.Plains || b == Heightmap.Biome.BlackForest);
    }

    /// <summary>Walk a chain of stations from a start, each placed on the flattest dry ground ahead.</summary>
    private static List<Station>? TryChain(WorldGenerator wg, Vector3 start, Vector3 dir, List<Vector3> avoid, out float worst)
    {
        worst = 0f;
        if (!LandOk(wg, start) || Relief(wg, start, EntranceR, out var lo0) > 8f || lo0 < 31.5f) return null;
        var pts = new List<Vector3> { start };
        var plots = new List<(Vector3 c, float r)> { (start, EntranceR) };
        var prevP = start; var prevR = EntranceR; var d = dir;
        for (int i = 0; i <= Exhibits.Length; i++)
        {
            bool garden = i == Exhibits.Length;
            float r = garden ? GardenR : Exhibits[i].R;
            float baseDist = prevR + r + Gap + (garden ? 0f : 8f);
            float bestScore = float.MaxValue; Vector3 bestP = default, bestD = default; float bestRel = 0f;
            foreach (var ang in new[] { 0f, -20f, 20f, -40f, 40f, -60f, 60f })
                foreach (var extra in new[] { 0f, 20f })
                {
                    var nd = Quaternion.Euler(0, ang, 0) * d;
                    var P = prevP + nd * (baseDist + extra);
                    if (!LandOk(wg, P) || !LandOk(wg, (P + prevP) * 0.5f)) continue;
                    var right = Vector3.Cross(Vector3.up, nd);
                    var cand = garden ? new[] { (P, r) } : new[] { (P - right * (r + 7f), r + 5f), (P + right * (r + 7f), r + 5f) };
                    float rel = 0f; bool ok = true;
                    foreach (var (c, cr) in cand)
                    {
                        foreach (var (pc, pr) in plots)
                            if ((pc - c).sqrMagnitude < (pr + cr + 10f) * (pr + cr + 10f)) { ok = false; break; }
                        if (!ok) break;
                        foreach (var a in avoid)
                            if ((a - c).sqrMagnitude < (cr + 40f) * (cr + 40f)) { ok = false; break; }
                        if (!ok) break;
                        var rr = Relief(wg, c, cr, out var lo);
                        if (lo < 31.5f) { ok = false; break; }
                        rel = Mathf.Max(rel, rr);
                    }
                    if (!ok) continue;
                    var score = rel * rel + Mathf.Abs(ang) * 0.05f + extra * 0.1f;
                    if (score < bestScore) { bestScore = score; bestP = P; bestD = nd; bestRel = rel; }
                }
            if (bestScore == float.MaxValue) return null;
            worst = Mathf.Max(worst, bestRel);
            if (worst > 12f) return null;
            pts.Add(bestP);
            var br = Vector3.Cross(Vector3.up, bestD);
            if (garden) plots.Add((bestP, r));
            else { plots.Add((bestP - br * (r + 7f), r + 5f)); plots.Add((bestP + br * (r + 7f), r + 5f)); }
            prevP = bestP; prevR = r; d = bestD;
        }
        return BuildStations(pts);
    }

    private static IEnumerator SearchRoutine(Action<List<Station>?> done)
    {
        var wg = WorldGenerator.instance;
        var avoid = ZoneSystem.instance.GetLocationList()
            .Where(l => l.m_placed || l.m_location == null || l.m_location.m_unique || l.m_location.m_prioritized)
            .Select(l => l.m_position).ToList();
        Plugin.Log.LogInfo($"{Tag} searching for the Ember Walk (gap {Gap} m)");
        List<Station>? best = null; float bestWorst = float.MaxValue; int tested = 0;
        for (float r = 700f; r <= 6000f && bestWorst > 7f; r += 250f)
        {
            int n = Mathf.CeilToInt(2f * Mathf.PI * r / 250f);
            for (int k = 0; k < n && bestWorst > 7f; k++)
            {
                var a = 2f * Mathf.PI * k / n;
                var start = new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a));
                foreach (var dang in new[] { 0f, 90f, 180f, 270f })
                {
                    var dir = Quaternion.Euler(0, dang, 0) * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    tested++;
                    var chain = TryChain(wg, start, dir, avoid, out var worst);
                    if (chain != null && worst < bestWorst) { best = chain; bestWorst = worst; }
                }
                if (k % 3 == 0) yield return null;
            }
        }
        Plugin.Log.LogInfo($"{Tag} search tested {tested} chains, best worst-plot relief {bestWorst:0.0} m, start {(best != null ? best[0].P.ToString() : "none")}");
        if (best != null)
            foreach (var s in best)
                DropPlannedLocations(s.P, StationR(s) * 2f + 30f);
        done(best);
    }

    /// <summary>Remove not-yet-generated location instances from the park footprint so they never spawn into it.</summary>
    private static void DropPlannedLocations(Vector3 c, float r)
    {
        var dict = Traverse.Create(ZoneSystem.instance).Field("m_locationInstances").GetValue() as System.Collections.IDictionary;
        if (dict == null) return;
        var drop = new List<object>();
        foreach (System.Collections.DictionaryEntry e in dict)
        {
            var inst = (ZoneSystem.LocationInstance)e.Value;
            if (inst.m_placed) continue;
            var p = inst.m_position;
            if ((p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z) < r * r) drop.Add(e.Key);
        }
        foreach (var k in drop)
        {
            var inst = (ZoneSystem.LocationInstance)dict[k];
            Plugin.Log.LogInfo($"{Tag} dropping planned location {inst.m_location?.m_prefab.Name} at {inst.m_position} (inside the park)");
            dict.Remove(k);
        }
    }

    // ================================================================== build

    private static IEnumerator BuildRoutine()
    {
        var player = Player.m_localPlayer;
        var old = Plugin.RuinsParkPos?.Value ?? "";
        var stations = Plugin.RuinsParkReuseLayout?.Value == true ? CurrentLayout() : null;

        // Clear the previous park: every tagged object, and its terrain edits (reset to generated)
        int cleared = ClearPark();
        int terr = 0;
        var oldLayout = ParseLayout(old);
        if (oldLayout != null) foreach (var s in oldLayout) terr += ResetTerrain(s.P, StationR(s) * 2f + 30f);
        else if (PhotoModePatches.ParsePos(old) is Vector3 oldCenter) terr += ResetTerrain(oldCenter, 340f); // v1 circle park
        Plugin.Log.LogInfo($"{Tag} cleared {cleared} park objects, reset {terr} terrain compilers");

        if (stations == null)
        {
            yield return SearchRoutine(v => stations = v);
            if (stations == null) { Plugin.Log.LogError($"{Tag} no site found"); yield break; }
        }
        Plugin.RuinsParkPos!.Value = FormatLayout(stations);
        Plugin.Log.LogInfo($"{Tag} BUILD layout {Plugin.RuinsParkPos.Value}");

        var spawn = AccessTools.Method(typeof(ZoneSystem), "SpawnLocation");
        Station? prev = null;
        foreach (var s in stations)
        {
            yield return PhotoModePatches.TeleportRoutine(player, Ground(s.P) + Vector3.up * 2f);
            yield return WaitAreaReady(s.P, StationR(s) * 2f + 15f);
            if (prev != null) yield return TrailSegment(prev, s, stations);
            if (s.Index < 0) yield return BuildEntrance(spawn, s);
            else if (s.Ex == null) yield return BuildGarden(spawn, s);
            else yield return BuildStation(spawn, s);
            prev = s;
        }
        Plugin.RuinsParkBuild!.Value = false;
        yield return PortalRoutine();
        var signHash = "sign".GetStableHashCode();
        var signs = AllZdos()?.Values.Count(z => z != null && z.IsValid() && z.GetPrefab() == signHash && z.GetInt(ParkTag) == 1) ?? 0;
        Plugin.Log.LogInfo($"{Tag} SIGNCHECK {signs} park signs standing (expected {Exhibits.Length * 2 + 3})");
        ZNet.instance?.Save(true);
        Plugin.Log.LogInfo($"{Tag} BUILD DONE entrance={Ground(stations[0].P)} stations={stations.Count}");
    }

    private static IEnumerator BuildStation(System.Reflection.MethodInfo spawn, Station s)
    {
        var ex = s.Ex!;
        var (ruinedC, restoredC) = PairCenters(s);
        var right = s.Right;
        ClearObjects(s.P, ex.R * 2f + 18f); // the whole station footprint: open lawns around both halls
        foreach (var (c, ruined) in new[] { (ruinedC, true), (restoredC, false) })
        {
            ClearObjects(c, ex.R + 6f);
            var floorY = MedianGround(c, ex.R + 4f);
            Level(c, ex.R + 6f, floorY, paint: false);
            Blend(c, ex.R + 16f);
            yield return new WaitForSeconds(1f);
            var pos = new Vector3(c.x, floorY, c.z);
            // both face the trail
            var rot = Quaternion.LookRotation(ruined ? right : -right);
            SpawnExhibit(spawn, ex, pos, rot, ruined);
            if (ruined) RuinsRebornPatches.KeptRuinAreas.Add((pos, ex.R + 10f));
            yield return new WaitForSeconds(1.5f);
        }
        // id signs at the trail edge, each facing the trail from its own side
        PlaceSign(s.P - right * 3.6f, right, $"{ex.Id}\n<size=70%>as the cataclysm left it</size>");
        PlaceSign(s.P + right * 3.6f, -right, $"{ex.Id}\n<size=70%>restored</size>");
        // the story stone, read while approaching, flanked by blue memorial torches
        var stoneP = s.P - s.D * 7f + right * 3.8f;
        PlaceRunestone(spawn, stoneP, Quaternion.LookRotation(-s.D), ex.Title, ex.Lore + $"\n\n<size=70%>({ex.Id})</size>");
        PlaceTorch(stoneP - s.D * 2.2f, "piece_groundtorch_blue");
        PlaceTorch(stoneP + s.D * 2.2f, "piece_groundtorch_blue");
        PlaceMisc("piece_bench01", s.P - s.D * 7f - right * 3.6f, Quaternion.LookRotation(right));
        Plugin.Log.LogInfo($"{Tag} station {s.Index} {ex.Id} built at {Ground(s.P)}");
    }

    private static IEnumerator BuildEntrance(System.Reflection.MethodInfo spawn, Station s)
    {
        var right = s.Right;
        ClearObjects(s.P, EntranceR + 10f);
        var y = MedianGround(s.P, 8f);
        Level(s.P, EntranceR, y, paint: true);
        Blend(s.P, EntranceR + 10f);
        yield return new WaitForSeconds(1f);
        // the gate spans the trail: two pillars, and two half arches meeting at the keystone
        var gate = new Vector3(s.P.x, y, s.P.z) + s.D * 5f;
        var fwd = Quaternion.LookRotation(s.D);
        PlaceMisc("Ashlands_Pillar4", gate - right * 4.15f, fwd);
        PlaceMisc("Ashlands_Pillar4", gate + right * 4.15f, fwd);
        // Arch2's pivot is its keystone end (local x = 0, high); it spans 4.15 m down along +x onto a pillar
        PlaceMisc("Ashlands_Arch2", gate + Vector3.up * 4f, Quaternion.LookRotation(s.D));   // +x = right
        PlaceMisc("Ashlands_Arch2", gate + Vector3.up * 4f, Quaternion.LookRotation(-s.D));  // +x = left
        PlaceMisc("piece_banner05", gate - right * 6.3f, fwd);
        PlaceMisc("piece_banner05", gate + right * 6.3f, fwd);
        PlaceTorch(gate - right * 2.4f - s.D * 1.5f, "piece_groundtorch_blue");
        PlaceTorch(gate + right * 2.4f - s.D * 1.5f, "piece_groundtorch_blue");
        PlaceRunestone(spawn, s.P - right * 6f - s.D * 2f, Quaternion.LookRotation(right), EntranceTopic, EntranceText);
        PlaceSign(s.P + right * 5f - s.D * 2f, -right, "THE EMBER WALK\n<size=60%>remember the dark times</size>");
        PlaceMisc("piece_bench01", s.P + right * 6f + s.D * 1f, Quaternion.LookRotation(-right));
        Plugin.Log.LogInfo($"{Tag} entrance built at {Ground(s.P)}");
    }

    private static IEnumerator BuildGarden(System.Reflection.MethodInfo spawn, Station s)
    {
        ClearObjects(s.P, GardenR + 14f); // a clearing in the woods
        var y = MedianGround(s.P, 10f);
        Level(s.P, 10f, y, paint: true);
        Blend(s.P, 22f);
        yield return new WaitForSeconds(1f);
        PlaceRunestone(spawn, s.P, Quaternion.LookRotation(-s.D), GardenTopic, GardenText);
        for (int i = 0; i < 6; i++)
        {
            var a = i * Mathf.PI / 3f;
            PlaceTorch(s.P + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6f, i % 2 == 0 ? "piece_groundtorch_blue" : "piece_groundtorch");
        }
        // one tree per restored hall
        string[] trees = { "Oak1", "Beech1", "Birch1", "Birch2", "Beech1", "Oak1", "Birch1", "Beech1", "Oak1", "Birch2" };
        for (int i = 0; i < Exhibits.Length; i++)
        {
            var a = 2f * Mathf.PI * i / Exhibits.Length;
            var p = s.P + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 16f;
            if (Vector3.Dot((p - s.P).normalized, -s.D) > 0.9f) continue; // keep the way in open
            PlaceMisc(trees[i % trees.Length], p, Quaternion.Euler(0, i * 37f, 0));
        }
        Plugin.Log.LogInfo($"{Tag} garden built at {Ground(s.P)}");
    }

    /// <summary>Pave the trail from one station to the next; torches alternate sides, trees stand back
    /// from the trail, and an interlude stone sits halfway when the next station has one.</summary>
    private static IEnumerator TrailSegment(Station a, Station b, List<Station> all)
    {
        var from = a.P; var to = b.P;
        var d = to - from; d.y = 0; var len = d.magnitude; d /= len;
        var right = Vector3.Cross(Vector3.up, d);
        var rnd = new System.Random((int)(from.x * 7 + from.z));
        int n = 0;
        for (float t = 0; t <= len; t += 2f, n++)
        {
            var p = from + d * t;
            if (Heightmap.FindHeightmap(p) == null) continue;
            ClearObjects(p, 6f);
            Paint(p, 2.1f);
            if (n % 8 == 4 && t > 12f && t < len - 12f) PlaceTorch(p + right * ((n / 8) % 2 == 0 ? 3.3f : -3.3f), "piece_groundtorch");
            if (n % 11 == 6 && t > 15f && t < len - 15f)
            {
                var side = rnd.Next(2) == 0 ? -1f : 1f;
                var tp = p + right * side * (9f + (float)rnd.NextDouble() * 8f);
                bool inPlot = all.Any(s => s.Ex != null &&
                    ((PairCenters(s).ruined - tp).sqrMagnitude < (s.Ex.R + 9f) * (s.Ex.R + 9f) ||
                     (PairCenters(s).restored - tp).sqrMagnitude < (s.Ex.R + 9f) * (s.Ex.R + 9f)));
                if (!inPlot)
                {
                    string[] trees = { "Beech1", "Birch1", "Oak1", "Birch2", "Beech_small1" };
                    PlaceMisc(trees[rnd.Next(trees.Length)], tp, Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0));
                }
            }
            if (n % 25 == 0) yield return null;
        }
        if (b.Interlude.HasValue)
        {
            var spawn = AccessTools.Method(typeof(ZoneSystem), "SpawnLocation");
            var mid = from + d * (len * 0.5f) - right * 4f;
            PlaceRunestone(spawn, mid, Quaternion.LookRotation(right), b.Interlude.Value.Item1, b.Interlude.Value.Item2);
            PlaceTorch(mid - d * 2.2f, "piece_groundtorch_green");
            PlaceTorch(mid + d * 2.2f, "piece_groundtorch_green");
        }
    }

    internal const string PortalTag = "EmberWalk";

    /// <summary>A linked portal pair (tag "EmberWalk"): one beside the world spawn's start temple,
    /// one at the walk entrance facing the gate; plus map pins for the portal, entrance, every
    /// station (internal id) and the garden. Saves world + player profile.</summary>
    private static IEnumerator PortalRoutine()
    {
        var player = Player.m_localPlayer;
        var stations = CurrentLayout();
        if (stations == null) { Plugin.Log.LogError($"{Tag} no park layout - build first"); yield break; }
        var dict = AllZdos();
        var portalHash = "portal_wood".GetStableHashCode();
        if (dict != null)
            foreach (var z in dict.Values.Where(z => z != null && z.IsValid() && z.GetPrefab() == portalHash && z.GetString(ZDOVars.s_tag) == PortalTag).ToList())
                RuinsRebornPatches.DeleteZdo(z);

        // world start: the StartTemple location (where new characters spawn)
        var temple = ZoneSystem.instance.GetLocationList().FirstOrDefault(l => l.m_location?.m_prefab.Name == "StartTemple").m_position;
        var startDir = new Vector3(stations[0].P.x - temple.x, 0, stations[0].P.z - temple.z).normalized; // toward the walk
        var startP = temple + startDir * 16f;
        yield return PhotoModePatches.TeleportRoutine(player, Ground(startP) + Vector3.up * 2f);
        yield return WaitAreaReady(startP, 20f);
        ClearObjects(startP, 3.5f);
        var a = PlacePortal(startP, Quaternion.LookRotation(-startDir)); // step out facing the temple
        PlaceSign(startP + Vector3.Cross(Vector3.up, startDir) * 2.6f, -startDir, "THE EMBER WALK\n<size=60%>through the portal</size>");

        // walk entrance: on the plaza behind the gate, exits facing the gate
        var s0 = stations[0];
        var walkP = s0.P - s0.D * 7f;
        yield return PhotoModePatches.TeleportRoutine(player, Ground(walkP) + Vector3.up * 2f);
        yield return WaitAreaReady(walkP, 20f);
        ClearObjects(walkP, 3.5f);
        Paint(walkP, 3f);
        var b = PlacePortal(walkP, Quaternion.LookRotation(s0.D));
        PlaceSign(walkP + s0.Right * 2.6f, s0.D, "WORLD START\n<size=60%>through the portal</size>");

        // map pins (player profile)
        RemoveMapPins(new[] { Ground(startP), Ground(walkP) }.Concat(stations.Select(st => Ground(st.P))).ToList());
        AddMapPin(Ground(startP), "Portal: Ember Walk");
        AddMapPin(Ground(walkP), "Ember Walk entrance");
        foreach (var st in stations.Skip(1))
            AddMapPin(Ground(st.P), st.Ex != null ? $"{st.Index + 1}. {st.Ex.Id}" : "Garden of Embers");
        ZNet.instance?.Save(true);
        try { Game.instance?.SavePlayerProfile(false); } catch (Exception e) { Plugin.Log.LogWarning($"{Tag} profile save: {e.Message}"); }
        Plugin.RuinsParkPortal!.Value = false;

        // verify: the pair must be connected in the world data (ZDOMan links same-tag portals)
        yield return new WaitForSeconds(6f);
        var pair = AllZdos()?.Values.Where(z => z != null && z.IsValid() && z.GetPrefab() == portalHash && z.GetString(ZDOVars.s_tag) == PortalTag).ToList() ?? new List<ZDO>();
        int linked = pair.Count(z => !z.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal).IsNone());

        // photos of both ends (the game runs fullscreen; desktop capture is black)
        var dir = Path.Combine(RuinsReconPatches.OutDir(), "park");
        Directory.CreateDirectory(dir);
        FableBunnyPatches.SetDebugDaylight(true);
        if (Hud.instance && Hud.instance.m_rootObject) Hud.instance.m_rootObject.SetActive(false);
        foreach (var (p, fwd, name) in new[] { (startP, startDir, "portal_start"), (walkP, s0.D, "portal_walk") })
        {
            yield return PhotoModePatches.TeleportRoutine(player, Ground(p - fwd * 6f) + Vector3.up * 2f);
            yield return new WaitForSeconds(2f);
            var g = Ground(p) + Vector3.up * 1.5f;
            var right = Vector3.Cross(Vector3.up, fwd);
            var cam = g - fwd * 9f + right * 4f + Vector3.up * 2.5f;
            PhotoModePatches.SetCameraOverride(cam, Quaternion.LookRotation(g - cam));
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return new WaitForSeconds(0.8f);
        }
        PhotoModePatches.ClearCameraOverride();
        // end where a visitor starts: next to the world-start portal
        yield return PhotoModePatches.TeleportRoutine(player, Ground(startP - startDir * 4f) + Vector3.up * 2f);
        Plugin.Log.LogInfo($"{Tag} PORTAL DONE start={Ground(startP)} walk={Ground(walkP)} portals={pair.Count} linked={linked}");
    }

    private static GameObject? PlacePortal(Vector3 p, Quaternion rot)
    {
        var go = PlaceMisc("portal_wood", p, rot);
        go?.GetComponent<ZNetView>()?.GetZDO()?.Set(ZDOVars.s_tag, PortalTag);
        return go;
    }

    private static void RemoveMapPins(List<Vector3> at)
    {
        if (Minimap.instance == null) return;
        foreach (var p in at) while (Minimap.instance.RemovePin(p, 3f)) { }
    }

    private static void AddMapPin(Vector3 pos, string name)
    {
        try
        {
            // reflection: AddPin's trailing PlatformUserID parameter lives in an assembly we don't reference
            var add = AccessTools.Method(typeof(Minimap), "AddPin");
            var ps = add.GetParameters();
            var args = new object?[ps.Length];
            args[0] = pos; args[1] = Minimap.PinType.Icon3; args[2] = name; args[3] = true; args[4] = false;
            for (int i = 5; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
            add.Invoke(Minimap.instance, args);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"{Tag} map pin: {e.Message}"); }
    }

    // ================================================================== placement helpers

    private static Vector3 Ground(Vector3 p)
    {
        var y = ZoneSystem.instance.GetGroundHeight(new Vector3(p.x, 0, p.z));
        if (y < 1f) y = WorldGenerator.instance.GetHeight(p.x, p.z);
        return new Vector3(p.x, y, p.z);
    }

    private static float MedianGround(Vector3 c, float r)
    {
        var hs = new List<float>();
        for (float dx = -r; dx <= r; dx += 2f)
            for (float dz = -r; dz <= r; dz += 2f)
                if (dx * dx + dz * dz <= r * r) hs.Add(Ground(c + new Vector3(dx, 0, dz)).y);
        hs.Sort();
        return hs.Count > 0 ? hs[hs.Count / 2] : Ground(c).y;
    }

    private static GameObject? Tagged(GameObject? go)
    {
        go?.GetComponent<ZNetView>()?.GetZDO()?.Set(ParkTag, 1);
        var w = go ? go!.GetComponent<WearNTear>() : null;
        if (w) w!.m_noSupportWear = false; // park furniture (signs on poles, arches) never collapses
        return go;
    }

    /// <summary>Park pieces ignore structural support on every load too (the flag isn't saved).</summary>
    [HarmonyPatch(typeof(WearNTear), "Awake")]
    private static class ParkSupportPatch
    {
        private static void Postfix(WearNTear __instance)
        {
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (zdo != null && zdo.GetInt(ParkTag) == 1) __instance.m_noSupportWear = false;
        }
    }

    internal static GameObject? PlaceMisc(string prefab, Vector3 p, Quaternion rot)
    {
        var pf = ZNetScene.instance.GetPrefab(prefab);
        if (pf == null) { Plugin.Log.LogWarning($"{Tag} missing prefab {prefab}"); return null; }
        var g = Ground(p);
        if (p.y > g.y + 0.5f) g = p; // explicit heights (arches on pillars) win
        return Tagged(UnityEngine.Object.Instantiate(pf, g, rot));
    }

    private static void PlaceTorch(Vector3 p, string prefab)
    {
        var go = PlaceMisc(prefab, p, Quaternion.identity);
        var zdo = go?.GetComponent<ZNetView>()?.GetZDO();
        zdo?.Set(ZDOVars.s_fuel, 1e7f); // effectively ever-burning
    }

    /// <summary>A sign on a pole; its text faces <paramref name="facing"/> (toward the reader).</summary>
    private static void PlaceSign(Vector3 p, Vector3 facing, string text)
    {
        var ground = Ground(p);
        Paint(ground, 1.4f); // no tall grass in front of the text
        PlaceMisc("wood_pole2", ground, Quaternion.LookRotation(facing));
        var signPf = ZNetScene.instance.GetPrefab("sign");
        if (signPf == null) return;
        var go = Tagged(UnityEngine.Object.Instantiate(signPf, ground + Vector3.up * 1.6f + facing * 0.12f, Quaternion.LookRotation(facing)));
        var sign = go?.GetComponent<Sign>();
        if (sign != null) sign.SetText(text);
    }

    private static void PlaceRunestone(System.Reflection.MethodInfo spawn, Vector3 p, Quaternion rot, string topic, string text)
    {
        var loc = RuinsRebornPatches.FindLocation("Runestone_Ashlands");
        if (loc == null) { Plugin.Log.LogWarning($"{Tag} Runestone_Ashlands missing"); return; }
        var g = Ground(p);
        Paint(g, 2.4f);
        var before = new HashSet<ZDOID>(Near(g, 3f).Select(z => z.m_uid));
        spawn.Invoke(ZoneSystem.instance, new object[] { loc, 7, g, rot, ZoneSystem.SpawnMode.Full, new List<GameObject>(), false });
        var proxyHash = ZoneSystem.instance.m_locationProxyPrefab.name.GetStableHashCode();
        foreach (var z in Near(g, 3f))
        {
            if (before.Contains(z.m_uid)) continue;
            z.Set(ParkTag, 1);
            if (z.GetPrefab() == proxyHash)
            {
                z.Set(RuneTopicKey, topic);
                z.Set(RuneTextKey, text);
            }
        }
    }

    private static void SpawnExhibit(System.Reflection.MethodInfo spawn, Exhibit ex, Vector3 pos, Quaternion rot, bool ruined)
    {
        var loc = RuinsRebornPatches.FindLocation(ex.Id);
        if (loc == null) { Plugin.Log.LogWarning($"{Tag} unknown location {ex.Id}"); return; }
        var before = new HashSet<ZDOID>(Near(pos, ex.R + 30f).Select(z => z.m_uid));
        loc.m_prefab.Load();
        var lc = loc.m_prefab.Asset.GetComponent<Location>();
        var oldBiome = lc ? lc.m_biome : Heightmap.Biome.None;
        if (lc) lc.m_biome = Heightmap.Biome.AshLands; // biome-gated rolls behave as in the Ashlands
        ParkSpawning = true;
        RuinsRebornPatches.SuppressRestore = ruined;
        var oldOverride = RuinsRebornPatches.RebuildOverride;
        if (!ruined) RuinsRebornPatches.RebuildOverride = true; // always author the restoration (it still toggles)
        try
        {
            spawn.Invoke(ZoneSystem.instance, new object[] { loc, ex.Seed, pos, rot, ZoneSystem.SpawnMode.Full, new List<GameObject>(), false });
        }
        catch (Exception e) { Plugin.Log.LogError($"{Tag} spawn {ex.Id}: {e}"); }
        finally
        {
            RuinsRebornPatches.RebuildOverride = oldOverride;
            RuinsRebornPatches.SuppressRestore = false;
            ParkSpawning = false;
            if (lc) lc.m_biome = oldBiome;
            loc.m_prefab.Release();
        }
        int n = 0;
        foreach (var z in Near(pos, ex.R + 30f)) if (!before.Contains(z.m_uid)) { z.Set(ParkTag, 1); n++; }
        RuinsRebornPatches.OnRestoreToggled(); // apply the current MasterSwitch state to the new restoration
        Plugin.Log.LogInfo($"{Tag} exhibit {ex.Id} {(ruined ? "ruined" : "restored")} at {pos}: {n} objects");
    }

    private static List<ZDO> Near(Vector3 pos, float r)
    {
        var l = new List<ZDO>();
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), new SimulationDistance(1, 1, classic: true), l);
        return l.Where(z => z.IsValid() && (z.GetPosition() - pos).sqrMagnitude < r * r).ToList();
    }

    // ---- terrain

    private static System.Reflection.MethodInfo? _doOp;

    private static void TerrainOpAt(Vector3 c, TerrainOp.Settings st, float reach)
    {
        _doOp ??= AccessTools.Method(typeof(TerrainComp), "DoOperation");
        var hmaps = new List<Heightmap>();
        Heightmap.FindHeightmap(c, reach, hmaps);
        foreach (var hm in hmaps)
        {
            var tc = hm.GetAndCreateTerrainCompiler();
            var nv = tc.GetComponent<ZNetView>();
            if (nv != null && nv.IsValid() && !nv.IsOwner()) nv.ClaimOwnership();
            _doOp!.Invoke(tc, new object[] { c, Vector3.zero, st });
        }
    }

    private static void Level(Vector3 c, float r, float y, bool paint)
    {
        TerrainOpAt(new Vector3(c.x, y, c.z), new TerrainOp.Settings
        {
            m_level = true, m_levelRadius = r, m_square = false, m_raise = false,
            m_smooth = true, m_smoothRadius = r + 6f, m_smoothPower = 3f,
            m_paintCleared = paint, m_paintType = TerrainModifier.PaintType.Paved, m_paintRadius = r, m_paintStrength = 1f, m_paintExp = 0.05f,
        }, r + 8f);
    }

    /// <summary>Blend a leveled area into the surroundings: a few smoothing passes over a wider ring.</summary>
    private static void Blend(Vector3 c, float r)
    {
        var st = new TerrainOp.Settings
        {
            m_level = false, m_raise = false, m_square = false, m_paintCleared = false,
            m_smooth = true, m_smoothRadius = r, m_smoothPower = 2f,
        };
        for (int i = 0; i < 3; i++) TerrainOpAt(Ground(c), st, r + 2f);
    }

    private static void Paint(Vector3 c, float r)
    {
        var g = Ground(c);
        TerrainOpAt(g, new TerrainOp.Settings
        {
            m_level = false, m_raise = false, m_smooth = false, m_square = false,
            m_paintCleared = true, m_paintType = TerrainModifier.PaintType.Paved, m_paintRadius = r, m_paintStrength = 1f, m_paintExp = 0.5f,
        }, r + 2f);
    }

    private static int ClearObjects(Vector3 center, float radius)
    {
        int n = 0;
        foreach (var nv in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
        {
            if (nv == null || !nv.IsValid()) continue;
            var go = nv.gameObject;
            var p = go.transform.position;
            if ((p.x - center.x) * (p.x - center.x) + (p.z - center.z) * (p.z - center.z) > radius * radius) continue;
            if (go.name.StartsWith("_") || go.GetComponent<Character>() || go.GetComponent<TerrainComp>() || go.GetComponent<RandomFlyingBird>()) continue;
            var zdo = nv.GetZDO();
            if (zdo.GetInt(ParkTag) == 1 || zdo.GetLong(ZDOVars.s_creator) != 0) continue;
            if (go.GetComponent<LocationProxy>()) continue;
            nv.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
            n++;
        }
        return n;
    }

    private static Dictionary<ZDOID, ZDO>? AllZdos() =>
        Traverse.Create(ZDOMan.instance).Field("m_objectsByID").GetValue<Dictionary<ZDOID, ZDO>>();

    /// <summary>Delete every park-tagged ZDO in the world (loaded or not) - for rebuilds.</summary>
    private static int ClearPark()
    {
        var dict = AllZdos();
        if (dict == null) return 0;
        var victims = dict.Values.Where(z => z != null && z.IsValid() && z.GetInt(ParkTag) == 1).ToList();
        foreach (var z in victims) RuinsRebornPatches.DeleteZdo(z);
        return victims.Count;
    }

    /// <summary>Reset terrain edits near an old park site: delete the zones' terrain compilers so the
    /// ground returns to its generated shape (only park edits ever touched these zones).</summary>
    private static int ResetTerrain(Vector3 c, float r)
    {
        var dict = AllZdos();
        if (dict == null) return 0;
        var hash = "_TerrainCompiler".GetStableHashCode();
        var victims = dict.Values.Where(z => z != null && z.IsValid() && z.GetPrefab() == hash &&
            (z.GetPosition().x - c.x) * (z.GetPosition().x - c.x) + (z.GetPosition().z - c.z) * (z.GetPosition().z - c.z) < (r + 46f) * (r + 46f)).ToList();
        foreach (var z in victims) RuinsRebornPatches.DeleteZdo(z);
        return victims.Count;
    }

    private static IEnumerator WaitAreaReady(Vector3 c, float r)
    {
        var timeout = Time.time + 60f;
        var probes = new[] { Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right }.Select(d => c + d * r).ToArray();
        while (Time.time < timeout && !probes.All(p => ZNetScene.instance.IsAreaReady(p) && Heightmap.FindHeightmap(p) != null))
            yield return new WaitForSeconds(0.5f);
        yield return new WaitForSeconds(3f);
    }

    // ================================================================== FPS tour

    private static IEnumerator MeasureFps(string label, List<string> results, Action<float> avgOut)
    {
        int frames = 0; float t0 = Time.realtimeSinceStartup; float worst = 0f;
        while (Time.realtimeSinceStartup - t0 < 5f) { worst = Mathf.Max(worst, Time.unscaledDeltaTime); frames++; yield return null; }
        var avg = frames / (Time.realtimeSinceStartup - t0);
        var dict = ZNetScene.instance ? Traverse.Create(ZNetScene.instance).Field("m_instances").GetValue() as System.Collections.IDictionary : null;
        var line = $"FPS {label}: avg {avg:0.0} worstFrame {1f / Mathf.Max(worst, 1e-4f):0.0} loadedObjects {dict?.Count ?? -1}";
        results.Add(line);
        Plugin.Log.LogInfo($"{Tag} {line}");
        avgOut(avg);
    }

    private static IEnumerator TourRoutine()
    {
        var player = Player.m_localPlayer;
        var stations = CurrentLayout();
        if (stations == null) { Plugin.Log.LogError($"{Tag} no park layout - build first"); yield break; }
        FableBunnyPatches.SetDebugDaylight(true);
        var dir = Path.Combine(RuinsReconPatches.OutDir(), "park");
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        yield return PhotoModePatches.TeleportRoutine(player, Ground(stations[0].P) + Vector3.up * 2f);
        yield return PhotoModePatches.ForceClearSkyRoutine();
        if (Hud.instance && Hud.instance.m_rootObject) Hud.instance.m_rootObject.SetActive(false);
        var results = new List<string>();
        float worstAvg = float.MaxValue, baseline = 0f;

        // baseline: same camera style over plain countryside 450 m behind the entrance
        var bp = stations[0].P - stations[0].D * 450f;
        yield return PhotoModePatches.TeleportRoutine(player, Ground(bp) + Vector3.up * 2f);
        yield return WaitAreaReady(bp, 40f);
        var bcam = Ground(bp) + Vector3.up * 14f;
        PhotoModePatches.SetCameraOverride(bcam, Quaternion.LookRotation(Ground(bp + stations[0].D * 40f) + Vector3.up * 6f - bcam));
        yield return new WaitForSeconds(4f);
        yield return MeasureFps("baseline (countryside, no park)", results, v => baseline = v);
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, "baseline.png"));
        yield return new WaitForSeconds(0.8f);

        foreach (var s in stations)
        {
            var r = StationR(s);
            yield return PhotoModePatches.TeleportRoutine(player, Ground(s.P - s.D * 4f) + Vector3.up * 2f);
            yield return WaitAreaReady(s.P, r * 2f + 10f);
            // camera behind and above the walker, looking down the avenue between the two buildings
            var back = r * 1.3f + 14f;
            var cam = Ground(s.P - s.D * back) + Vector3.up * (r * 0.55f + 7f);
            var look = Ground(s.P) + Vector3.up * (r * 0.35f + 2f);
            PhotoModePatches.SetCameraOverride(cam, Quaternion.LookRotation(look - cam));
            yield return new WaitForSeconds(4f);
            var id = s.Ex?.Id ?? (s.Index < 0 ? "Entrance" : "Garden");
            float avg = 0f;
            yield return MeasureFps($"station {s.Index + 1:00} {id}", results, v => avg = v);
            worstAvg = Mathf.Min(worstAvg, avg);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"station_{s.Index + 1:00}_{id}.png"));
            yield return new WaitForSeconds(0.8f);
            if (s.Index == 0)
            {
                // readability check: the id sign of the first ruined exhibit, as a walker sees it
                var signP = Ground(s.P - s.Right * 3.6f) + Vector3.up * 1.6f;
                var camS = signP + s.Right * 2.6f - s.D * 1.2f + Vector3.up * 0.6f;
                PhotoModePatches.SetCameraOverride(camS, Quaternion.LookRotation(signP - camS));
                yield return new WaitForSeconds(1.5f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "sign_closeup.png"));
                yield return new WaitForSeconds(0.8f);
            }
            if (s.Ex != null)
            {
                // a close look at each building from the trail
                foreach (var (c, tagName) in new[] { (PairCenters(s).ruined, "ruined"), (PairCenters(s).restored, "restored") })
                {
                    var g = Ground(c);
                    var away = (g - s.P); away.y = 0; away.Normalize();
                    var cam2 = Ground(s.P - away * (r * 0.6f)) + Vector3.up * (r * 0.4f + 4f);
                    var look2 = g + Vector3.up * (r * 0.5f);
                    PhotoModePatches.SetCameraOverride(cam2, Quaternion.LookRotation(look2 - cam2));
                    yield return new WaitForSeconds(1.5f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"station_{s.Index + 1:00}_{id}_{tagName}.png"));
                    yield return new WaitForSeconds(0.8f);
                }
            }
        }
        // overview from high above the middle of the walk
        var mid = stations[stations.Count / 2].P;
        var span = stations.Max(s => (s.P - mid).magnitude);
        var top = Ground(mid) + Vector3.up * 70f - stations[0].D * 150f;
        PhotoModePatches.SetCameraOverride(top, Quaternion.LookRotation(Ground(mid) - top));
        yield return new WaitForSeconds(5f);
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, "overview.png"));
        yield return new WaitForSeconds(1f);
        File.WriteAllLines(Path.Combine(dir, "FPS.txt"), results);
        Plugin.RuinsParkTour!.Value = false;
        Plugin.Log.LogInfo($"{Tag} TOUR DONE baseline={baseline:0.0} worstStation={worstAvg:0.0} ratio={(baseline > 0 ? worstAvg / baseline : 0):0.00} min-acceptable={Plugin.RuinsParkMinFps?.Value ?? 0f}");
    }

    // ================================================================== runestone inscriptions

    private static (string topic, string text)? Inscription(Component c)
    {
        var proxy = c.GetComponentInParent<LocationProxy>();
        var zdo = proxy ? proxy.GetComponent<ZNetView>()?.GetZDO() : null;
        if (zdo == null) return null;
        var topic = zdo.GetString(RuneTopicKey);
        if (string.IsNullOrEmpty(topic)) return null;
        return (topic, zdo.GetString(RuneTextKey));
    }

    [HarmonyPatch(typeof(RuneStone), nameof(RuneStone.Interact))]
    private static class RuneTextPatch
    {
        private static bool Prefix(RuneStone __instance, bool hold, ref bool __result)
        {
            var ins = Inscription(__instance);
            if (ins == null) return true;
            __result = false;
            if (!hold) TextViewer.instance.ShowText(TextViewer.Style.Rune, ins.Value.topic, ins.Value.text, autoHide: true);
            return false;
        }
    }

    [HarmonyPatch(typeof(RuneStone), nameof(RuneStone.GetHoverText))]
    private static class RuneHoverPatch
    {
        private static bool Prefix(RuneStone __instance, ref string __result)
        {
            var ins = Inscription(__instance);
            if (ins == null) return true;
            __result = ins.Value.topic + "\n[<color=yellow><b>E</b></color>] Read the stone";
            return false;
        }
    }

    // ================================================================== no random mobs in the park

    [HarmonyPatch(typeof(SpawnSystem), "IsSpawnPointGood")]
    private static class NoSpawnPatch
    {
        private static string? _raw;
        private static List<Vector3> _pts = new();
        private static void Postfix(Vector3 spawnPoint, ref bool __result)
        {
            if (!__result) return;
            var raw = Plugin.RuinsParkPos?.Value;
            if (raw != _raw) { _raw = raw; _pts = ParseLayout(raw)?.Select(s => s.P).ToList() ?? new List<Vector3>(); }
            foreach (var p in _pts)
                if ((spawnPoint.x - p.x) * (spawnPoint.x - p.x) + (spawnPoint.z - p.z) * (spawnPoint.z - p.z) < 140f * 140f) { __result = false; return; }
        }
    }
}
