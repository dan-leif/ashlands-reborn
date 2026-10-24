using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev-only builder for the photo harnesses' test arena: a flat, indestructible TERRAIN island
/// in open ocean (the old player-built stone platform got smashed by rolling Morgens).
///
/// Runs from Plugin.Update() via <see cref="Tick"/>; the one Harmony patch is <see cref="NoSpawnPatch"/>
/// (blocks random mob spawns around the arena).
/// - <c>TestArenaBuild</c>: once after world load, pick a site (<c>TestArenaPos</c>, or search the
///   world generator for a small Meadows island ringed by open ocean), teleport there, delete every
///   networked object (trees, rocks, pickables, logs...) over the arena, level the terrain flat at
///   one height and paint it, save the world, then write the center into <c>TestArenaPos</c> and
///   <c>PhotoModeIslandPos</c> and switch itself off. Everything is vanilla world data
///   (TerrainComp ZDOs + destroyed ZDOs), so it persists in the world save.
/// - <c>TestArenaVerify</c>: once after world load, teleport via the photo harness's own
///   TeleportToIslandRoutine, log a flatness check and capture overview shots (no changes).
///
/// Terrain limit: vanilla clamps every terrain edit to +-8 m of the generated height
/// (TerrainComp.LevelTerrain / ApplyToHeightmap), which is why the search needs a natural island
/// whose ground already sits within ~7.5 m of the target height everywhere inside the arena.
/// Output: "&lt;plugin dir&gt;\AR_Arena\" shots + "[AR Arena] DONE" in the log.
/// </summary>
internal static class TestArenaPatches
{
    private const string Tag = "[AR Arena]";
    private const float MaxEdit = 7.5f;         // vanilla clamp is 8 m; keep a margin
    private const float MinTarget = 32f;        // water level is 30 - keep the floor clear of waves
    private const float MaxTarget = 34f;

    private static bool _running;
    private static bool _fired;
    private static float _worldLoadedAt = -1f;

    /// <summary>Called from Plugin.Update() every frame.</summary>
    internal static void Tick()
    {
        if (Player.m_localPlayer == null)
        {
            _worldLoadedAt = -1f;
            _fired = false;
            return;
        }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;
        if (_fired || _running || Time.time - _worldLoadedAt < 8f) return;

        var build = Plugin.TestArenaBuild?.Value ?? false;
        var verify = Plugin.TestArenaVerify?.Value ?? false;
        if (!build && !verify) return;
        _fired = true;
        Plugin.Instance.StartCoroutine(Run(build));
    }

    private static IEnumerator Run(bool build)
    {
        _running = true;
        Application.runInBackground = true;
        try
        {
            var player = Player.m_localPlayer;
            if (player == null) yield break;
            var radius = Mathf.Clamp(Plugin.TestArenaRadius?.Value ?? 36f, 10f, 100f);

            Vector3 center;
            if (build)
            {
                Vector3? site = PhotoModePatches.ParsePos(Plugin.TestArenaPos?.Value);
                if (site == null)
                {
                    Vector3? found = null;
                    yield return SearchRoutine(radius, p => found = p);
                    site = found;
                }
                if (site == null)
                {
                    Plugin.Log?.LogError($"{Tag} No arena site found - DONE (failed)");
                    yield break;
                }
                center = site.Value;
                yield return PhotoModePatches.TeleportRoutine(player, center + Vector3.up * 6f);
                yield return WaitAreaReady(center, radius);
                yield return BuildRoutine(center, radius);
                Plugin.TestArenaBuild!.Value = false;
            }
            else
            {
                var pos = PhotoModePatches.ParsePos(Plugin.PhotoModeIslandPos?.Value);
                if (pos == null)
                {
                    Plugin.Log?.LogError($"{Tag} PhotoModeIslandPos is empty - nothing to verify");
                    yield break;
                }
                // Exercise the exact teleport path the photo harness uses.
                yield return PhotoModePatches.TeleportToIslandRoutine(player);
                center = pos.Value;
                yield return WaitAreaReady(center, radius);
                Plugin.TestArenaVerify!.Value = false;
            }

            var level = LogFlatness(center, radius, out var arenaY);
            Plugin.Log?.LogInfo($"{Tag} Player at {player.transform.position} (arena floor y={arenaY:0.00})");
            yield return CaptureShots(player, new Vector3(center.x, arenaY, center.z), radius, build ? "build" : "verify");
            Plugin.Log?.LogInfo($"{Tag} DONE ({(build ? "build" : "verify")}) center={Fmt(center)} flat={level}");
        }
        finally
        {
            PhotoModePatches.ClearCameraOverride();
            FableBunnyPatches.SetDebugDaylight(false);
            _running = false;
        }
    }

    // ---------------------------------------------------------------- site search

    private struct Candidate
    {
        public float X, Z, Target, Score;
    }

    /// <summary>Two-stage scan of WorldGenerator heights: a 16 m world grid for candidates, then a
    /// 2 m refine of the best ones. A site qualifies when every point inside the arena can be
    /// leveled to one height (32-34 m) within the +-8 m edit clamp, its surroundings out to ~200 m
    /// are open water, the biome is Meadows (calm spawns), and no location (dungeon, altar,
    /// trader...) is nearby.</summary>
    private static IEnumerator SearchRoutine(float radius, Action<Vector3?> done)
    {
        var wg = WorldGenerator.instance;
        if (wg == null) { done(null); yield break; }
        var water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
        var meadowsOnly = string.Equals(Plugin.TestArenaBiome?.Value, "Meadows", StringComparison.OrdinalIgnoreCase);
        var biome = meadowsOnly ? "Meadows" : "calm-biome";

        const float step = 16f;
        const float extent = 9600f;
        var n = (int)(2f * extent / step) + 1;
        var heights = new float[n * n];
        var sw = Stopwatch.StartNew();
        var frame = Stopwatch.StartNew();
        Plugin.Log?.LogInfo($"{Tag} Searching for a {biome} site (radius {radius:0} m, {n}x{n} samples)...");
        for (var j = 0; j < n; j++)
        {
            var z = -extent + j * step;
            for (var i = 0; i < n; i++)
            {
                var x = -extent + i * step;
                heights[j * n + i] = x * x + z * z > 9800f * 9800f ? -100f : wg.GetHeight(x, z);
            }
            if (frame.ElapsedMilliseconds > 30) { yield return null; frame.Restart(); }
        }
        Plugin.Log?.LogInfo($"{Tag} Height grid done in {sw.ElapsedMilliseconds / 1000f:0.0}s");

        // Stage 1: coarse candidates.
        var innerOffsets = Offsets(step, 0f, radius);
        var ringOffsets = Offsets(step, radius + 30f, radius + 170f);
        var ringReach = (int)Mathf.Ceil((radius + 170f) / step);
        var coarse = new List<Candidate>();
        var inner = new List<float>();
        for (var j = ringReach; j < n - ringReach; j++)
        {
            for (var i = ringReach; i < n - ringReach; i++)
            {
                var hc = heights[j * n + i];
                if (hc < MinTarget - MaxEdit || hc > MaxTarget + MaxEdit) continue;

                inner.Clear();
                var ok = true;
                foreach (var (di, dj) in innerOffsets)
                {
                    var h = heights[(j + dj) * n + i + di];
                    if (h < MinTarget - MaxEdit || h > MaxTarget + MaxEdit) { ok = false; break; }
                    inner.Add(h);
                }
                if (!ok) continue;
                var target = Mathf.Clamp(Median(inner), MinTarget, MaxTarget);
                if (inner.Any(h => Mathf.Abs(h - target) > MaxEdit)) continue;

                var wet = 0;
                foreach (var (di, dj) in ringOffsets)
                    if (heights[(j + dj) * n + i + di] < water - 0.5f) wet++;
                if (wet < ringOffsets.Count * 0.95f) continue;

                if (!BiomeAllowed(wg.GetBiome(-extent + i * step, -extent + j * step), meadowsOnly)) continue;

                coarse.Add(new Candidate
                {
                    X = -extent + i * step,
                    Z = -extent + j * step,
                    Target = target,
                    Score = inner.Average(h => Mathf.Abs(h - target)),
                });
            }
            if (frame.ElapsedMilliseconds > 30) { yield return null; frame.Restart(); }
        }
        Plugin.Log?.LogInfo($"{Tag} {coarse.Count} coarse candidates");

        // Stage 2: refine the best (one per ~300 m cluster) at 2 m resolution.
        var picked = new List<Candidate>();
        foreach (var c in coarse.OrderBy(c => c.Score))
        {
            if (picked.Count >= 40) break;
            if (picked.Any(p => Sq(p.X - c.X) + Sq(p.Z - c.Z) < 300f * 300f)) continue;
            picked.Add(c);
        }

        var locations = ZoneSystem.instance?.m_locationInstances?.Values.Select(l => l.m_position).ToList()
                        ?? new List<Vector3>();
        Candidate? best = null;
        var rejects = new Dictionary<string, int>();
        foreach (var c in picked)
        {
            // Small local re-centering search (the coarse grid is 16 m).
            for (var ox = -8f; ox <= 8f; ox += 4f)
            for (var oz = -8f; oz <= 8f; oz += 4f)
            {
                var r = Refine(wg, meadowsOnly, c.X + ox, c.Z + oz, radius, water, locations, out var why);
                if (r == null) { rejects[why] = rejects.TryGetValue(why, out var k) ? k + 1 : 1; continue; }
                if (best == null || r.Value.Score < best.Value.Score) best = r;
            }
            if (frame.ElapsedMilliseconds > 30) { yield return null; frame.Restart(); }
        }

        if (best == null)
        {
            Plugin.Log?.LogWarning($"{Tag} No candidate survived the refine pass (" +
                                   string.Join(", ", rejects.Select(kv => $"{kv.Key}={kv.Value}")) + ")");
            done(null);
            yield break;
        }
        var b = best.Value;
        Plugin.Log?.LogInfo($"{Tag} Picked site {b.X:0},{b.Target:0.0},{b.Z:0} (mean edit {b.Score:0.00} m, " +
                            $"biome {wg.GetBiome(b.X, b.Z)}, dist from center {Mathf.Sqrt(b.X * b.X + b.Z * b.Z):0} m) " +
                            $"after {sw.ElapsedMilliseconds / 1000f:0.0}s");
        done(new Vector3(b.X, b.Target, b.Z));
    }

    private static Candidate? Refine(WorldGenerator wg, bool meadowsOnly, float x, float z, float radius, float water,
        List<Vector3> locations, out string why)
    {
        why = "";
        // Center plus 8 points at half and full radius: the whole disc must be an allowed biome.
        if (!BiomeAllowed(wg.GetBiome(x, z), meadowsOnly)) { why = "biome"; return null; }
        for (var a = 0; a < 8; a++)
        {
            var ang = a * Mathf.PI / 4f;
            var rr = (a % 2 == 0 ? 0.95f : 0.5f) * radius;
            if (!BiomeAllowed(wg.GetBiome(x + Mathf.Cos(ang) * rr, z + Mathf.Sin(ang) * rr), meadowsOnly)) { why = "biome"; return null; }
        }
        if (locations.Any(l => Sq(l.x - x) + Sq(l.z - z) < 250f * 250f)) { why = "location"; return null; }

        var inner = new List<float>();
        for (var dz = -radius - 2f; dz <= radius + 2f; dz += 3f)
        for (var dx = -radius - 2f; dx <= radius + 2f; dx += 3f)
        {
            if (dx * dx + dz * dz > Sq(radius + 2f)) continue;
            var h = wg.GetHeight(x + dx, z + dz);
            if (h < MinTarget - MaxEdit || h > MaxTarget + MaxEdit) { why = "height"; return null; }
            inner.Add(h);
        }
        var target = Mathf.Clamp(Median(inner), MinTarget, MaxTarget);
        if (inner.Any(h => Mathf.Abs(h - target) > MaxEdit)) { why = "edit"; return null; }

        int wet = 0, total = 0;
        for (var dz = -radius - 170f; dz <= radius + 170f; dz += 6f)
        for (var dx = -radius - 170f; dx <= radius + 170f; dx += 6f)
        {
            var d2 = dx * dx + dz * dz;
            if (d2 < Sq(radius + 25f) || d2 > Sq(radius + 170f)) continue;
            total++;
            if (wg.GetHeight(x + dx, z + dz) < water - 0.5f) wet++;
        }
        if (wet < total * 0.97f) { why = "ring"; return null; }

        return new Candidate { X = x, Z = z, Target = target, Score = inner.Average(h => Mathf.Abs(h - target)) };
    }

    /// <summary>Calm biomes only: no snow/cold (Mountain, DeepNorth), mist (Mistlands), lava (AshLands) or
    /// swamp. "Ocean" shows up on shallow shelves at biome edges. Mob spawns are blocked separately
    /// (<see cref="NoSpawnPatch"/>), so the biome is a looks choice, not a safety one.</summary>
    private static bool BiomeAllowed(Heightmap.Biome b, bool meadowsOnly) =>
        meadowsOnly
            ? b == Heightmap.Biome.Meadows
            : b is Heightmap.Biome.Meadows or Heightmap.Biome.BlackForest or Heightmap.Biome.Plains or Heightmap.Biome.Ocean;

    private static List<(int, int)> Offsets(float step, float rMin, float rMax)
    {
        var list = new List<(int, int)>();
        var reach = (int)Mathf.Ceil(rMax / step);
        for (var dj = -reach; dj <= reach; dj++)
        for (var di = -reach; di <= reach; di++)
        {
            var d = Mathf.Sqrt(di * di + dj * dj) * step;
            if (d >= rMin && d <= rMax) list.Add((di, dj));
        }
        return list;
    }

    // ---------------------------------------------------------------- build

    private static IEnumerator WaitAreaReady(Vector3 center, float radius)
    {
        var timeout = Time.time + 60f;
        var probes = new[] { Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right }
            .Select(d => center + d * (radius + 4f)).ToArray();
        while (Time.time < timeout && !probes.All(p => ZNetScene.instance.IsAreaReady(p) && Heightmap.FindHeightmap(p) != null))
            yield return new WaitForSeconds(0.5f);
        yield return new WaitForSeconds(3f); // let zone objects finish spawning
    }

    private static IEnumerator BuildRoutine(Vector3 center, float radius)
    {
        var target = center.y;
        Plugin.Log?.LogInfo($"{Tag} Building arena at {Fmt(center)} radius {radius:0} m");

        // 1. Delete every networked object over the arena (trees, stumps, rocks, logs, pickables,
        //    bushes, item drops). Keeps creatures/players and system objects (_ZoneCtrl,
        //    _TerrainCompiler). Two passes: some destructibles drop children (logs) when removed.
        for (var pass = 0; pass < 2; pass++)
        {
            var removed = ClearObjects(center, radius + 4f);
            Plugin.Log?.LogInfo($"{Tag} Clear pass {pass + 1}: removed {removed} objects");
            yield return new WaitForSeconds(1f);
        }

        // 2. Level + paint via the vanilla TerrainComp pipeline. TerrainOp's own RPC path only
        //    accepts registered TerrainOp prefabs (settings serialize as a prefab hash), so we call
        //    the owner-side DoOperation directly with custom settings; it saves into each zone's
        //    TerrainComp ZDO exactly like a hoe stroke.
        var doOp = AccessTools.Method(typeof(TerrainComp), "DoOperation");
        if (doOp == null) { Plugin.Log?.LogError($"{Tag} TerrainComp.DoOperation not found"); yield break; }
        var paint = (Plugin.TestArenaPaint?.Value ?? "Paved").Trim();
        var settings = new TerrainOp.Settings
        {
            m_level = true,
            m_levelRadius = radius,
            m_square = false,
            m_raise = false,
            m_smooth = false,
            m_paintCleared = !paint.Equals("None", StringComparison.OrdinalIgnoreCase),
            m_paintType = paint.Equals("Dirt", StringComparison.OrdinalIgnoreCase)
                ? TerrainModifier.PaintType.Dirt
                : TerrainModifier.PaintType.Paved,
            m_paintRadius = radius,
            m_paintStrength = 1f,
            m_paintExp = 0.05f,
        };

        var hmaps = new List<Heightmap>();
        Heightmap.FindHeightmap(center, radius + 2f, hmaps);
        foreach (var hm in hmaps)
        {
            var tc = hm.GetAndCreateTerrainCompiler();
            var nview = tc.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            doOp.Invoke(tc, new object[] { new Vector3(center.x, target, center.z), Vector3.zero, settings });
        }
        Plugin.Log?.LogInfo($"{Tag} Leveled {hmaps.Count} heightmaps to y={target:0.00} (paint={paint})");
        yield return new WaitForSeconds(2f); // Poke(1) rebuilds the meshes on a later frame

        // Leveling can expose objects that were buried; one more sweep.
        var late = ClearObjects(center, radius + 4f);
        if (late > 0) Plugin.Log?.LogInfo($"{Tag} Post-level clear: removed {late} objects");

        // 3. Persist: force-killed dev sessions never save, so write the world now.
        ZNet.instance?.Save(true);
        Plugin.Log?.LogInfo($"{Tag} World saved");

        var posStr = Fmt(new Vector3(center.x, target + 1f, center.z));
        Plugin.TestArenaPos!.Value = Fmt(center);
        Plugin.PhotoModeIslandPos.Value = posStr;
        Plugin.Log?.LogInfo($"{Tag} TestArenaPos={Plugin.TestArenaPos.Value}, PhotoModeIslandPos={posStr}");
    }

    private static int ClearObjects(Vector3 center, float radius)
    {
        var player = Player.m_localPlayer;
        var removed = 0;
        foreach (var nview in UnityEngine.Object.FindObjectsOfType<ZNetView>())
        {
            if (nview == null || !nview.IsValid()) continue;
            var go = nview.gameObject;
            var p = go.transform.position;
            if (Sq(p.x - center.x) + Sq(p.z - center.z) > radius * radius) continue;
            if (go.name.StartsWith("_")) continue; // _ZoneCtrl, _TerrainCompiler, ...
            if (go.GetComponent<Character>() != null || go.GetComponent<TerrainComp>() != null) continue;
            if (go.GetComponent<RandomFlyingBird>() != null) continue; // ambient seagulls fly over
            if (player != null && go == player.gameObject) continue;
            nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
            removed++;
        }
        return removed;
    }

    // ---------------------------------------------------------------- verify + shots

    /// <summary>Sample the live terrain on a 2 m grid; returns a summary and the floor height.</summary>
    private static string LogFlatness(Vector3 center, float radius, out float floorY)
    {
        var hs = new List<float>();
        for (var dz = -radius + 1f; dz <= radius - 1f; dz += 2f)
        for (var dx = -radius + 1f; dx <= radius - 1f; dx += 2f)
        {
            if (dx * dx + dz * dz > Sq(radius - 1f)) continue;
            if (Heightmap.GetHeight(new Vector3(center.x + dx, 0f, center.z + dz), out var h)) hs.Add(h);
        }
        if (hs.Count == 0) { floorY = center.y; return "no samples"; }
        floorY = Median(hs);
        var y = floorY;
        var off = hs.Count(h => Mathf.Abs(h - y) > 0.1f);
        var maxDev = hs.Max(h => Mathf.Abs(h - y));
        var summary = $"{hs.Count} samples, median y={floorY:0.00}, max dev {maxDev:0.00} m, {off} off-level (>0.1 m)";
        Plugin.Log?.LogInfo($"{Tag} FLATCHECK {(off == 0 ? "PASS" : "FAIL")} {summary}");

        var objs = UnityEngine.Object.FindObjectsOfType<ZNetView>()
            .Where(v => v != null && v.IsValid() && !v.gameObject.name.StartsWith("_")
                        && v.GetComponent<Character>() == null && v.GetComponent<RandomFlyingBird>() == null
                        && Sq(v.transform.position.x - center.x) + Sq(v.transform.position.z - center.z) < radius * radius)
            .Select(v => v.gameObject.name).ToList();
        Plugin.Log?.LogInfo($"{Tag} OBJCHECK {(objs.Count == 0 ? "PASS" : "FAIL")} {objs.Count} non-creature objects on the arena" +
                            (objs.Count > 0 ? ": " + string.Join(",", objs.Take(20)) : ""));
        return off == 0 && objs.Count == 0 ? "PASS" : "FAIL";
    }

    private static IEnumerator CaptureShots(Player player, Vector3 floor, float radius, string label)
    {
        FableBunnyPatches.SetDebugDaylight(true);
        yield return PhotoModePatches.ForceClearSkyRoutine();

        var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "AR_Arena");
        Directory.CreateDirectory(dir);

        var shots = new (string name, Vector3 pos)[]
        {
            ("top", floor + new Vector3(0f, radius * 2.6f, -1f)),
            ("oblique_s", floor + new Vector3(0f, radius * 0.9f, -radius * 2.2f)),
            ("oblique_e", floor + new Vector3(radius * 2.2f, radius * 0.9f, 0f)),
            ("ground", floor + new Vector3(0f, 1.8f, -radius + 3f)),
        };
        foreach (var (name, pos) in shots)
        {
            var look = name == "ground" ? floor + new Vector3(0f, 1.5f, 0f) : floor;
            PhotoModePatches.SetCameraOverride(pos, Quaternion.LookRotation(look - pos));
            yield return new WaitForSeconds(1.5f);
            var path = Path.Combine(dir, $"arena_{label}_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Plugin.Log?.LogInfo($"{Tag} Shot {path}");
            for (var f = 0; f < 5; f++) yield return null;
        }
        PhotoModePatches.ClearCameraOverride();
    }

    // ---------------------------------------------------------------- mob spawn block

    /// <summary>Random mob spawns kept wandering onto the arena. Every SpawnSystem spawn point goes
    /// through IsSpawnPointGood; reject points within (arena radius + 120 m) of PhotoModeIslandPos
    /// while TestArenaNoSpawns is on. 120 m covers the vanilla 40-80 m spawn ring around a player
    /// standing at the arena edge. Mod-side only: nothing is written to the world.</summary>
    [HarmonyPatch(typeof(SpawnSystem), "IsSpawnPointGood")]
    internal static class NoSpawnPatch
    {
        private static string? _cachedRaw;
        private static Vector3? _cachedPos;

        private static void Postfix(Vector3 spawnPoint, ref bool __result)
        {
            if (!__result || !(Plugin.TestArenaNoSpawns?.Value ?? false)) return;
            var raw = Plugin.PhotoModeIslandPos?.Value;
            if (raw != _cachedRaw) { _cachedRaw = raw; _cachedPos = PhotoModePatches.ParsePos(raw); }
            if (_cachedPos == null) return;
            var block = (Plugin.TestArenaRadius?.Value ?? 36f) + 120f;
            if (Sq(spawnPoint.x - _cachedPos.Value.x) + Sq(spawnPoint.z - _cachedPos.Value.z) < block * block)
                __result = false;
        }
    }

    // ---------------------------------------------------------------- helpers

    private static float Sq(float v) => v * v;

    private static float Median(List<float> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private static string Fmt(Vector3 v) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0.0},{2:0}", v.x, v.y, v.z);
}
