using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev harness (config <c>RuinsPhotoAuto</c>): teleports to the flat test arena, and for each
/// location in <c>RuinsPhotoLocations</c> spawns it at the arena center (vanilla and/or rebuilt,
/// same seed), captures 4 oblique orbit shots + 1 top-down shot, then deletes everything it
/// spawned. Output: <c>AR_Ruins\photos\&lt;loc&gt;_&lt;variant&gt;_&lt;view&gt;.png</c>, "[AR RuinsPhoto] DONE".
/// </summary>
internal static class RuinsPhotoPatches
{
    private const string Tag = "[AR RuinsPhoto]";
    private static bool _running, _fired;
    private static float _worldLoadedAt = -1f;

    internal static void Tick()
    {
        if (Player.m_localPlayer == null) { _worldLoadedAt = -1f; _fired = false; return; }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;
        if (_fired || _running || Time.time - _worldLoadedAt < 10f) return;
        if (!(Plugin.RuinsPhotoAuto?.Value ?? false)) return;
        _fired = true;
        Plugin.Instance.StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        _running = true;
        var player = Player.m_localPlayer;
        var hud = Hud.instance;
        try
        {
            Application.runInBackground = true;
            var center = PhotoModePatches.ParsePos(Plugin.PhotoModeIslandPos?.Value) ?? player.transform.position;
            // Stand off to the side so the player never ends up inside a tower.
            yield return PhotoModePatches.TeleportRoutine(player, center + new Vector3(0f, 0f, -45f));
            FableBunnyPatches.SetDebugDaylight(true);
            yield return PhotoModePatches.ForceClearSkyRoutine();
            player.SetGodMode(true);
            if (hud && hud.m_rootObject) hud.m_rootObject.SetActive(false);

            var dir = Path.Combine(RuinsReconPatches.OutDir(), "photos");
            Directory.CreateDirectory(dir);
            var locs = (Plugin.RuinsPhotoLocations?.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var variants = (Plugin.RuinsPhotoVariants?.Value ?? "rebuilt,vanilla").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var spawn = AccessTools.Method(typeof(ZoneSystem), "SpawnLocation");
            var lines = new List<string>();

            foreach (var name in locs)
            {
                var loc = RuinsRebornPatches.FindLocation(name);
                if (loc == null) { Plugin.Log.LogWarning($"{Tag} unknown location {name}"); continue; }
                {
                    // Spawn once, restored; then flip the toggle to shoot the ruined state of the SAME instance.
                    RuinsRebornPatches.RebuildOverride = true;
                    var pos = center;
                    pos.y = ZoneSystem.instance.GetGroundHeight(center);
                    var before = new HashSet<ZDOID>(SectorZdos(pos).Select(z => z.m_uid));

                    // Location.m_biome is computed from the world on first spawn; the arena isn't Ashlands,
                    // and biome-gated RandomSpawns would all roll off there.
                    loc.m_prefab.Load();
                    var lc = loc.m_prefab.Asset.GetComponent<Location>();
                    var oldBiome = lc ? lc.m_biome : Heightmap.Biome.None;
                    if (lc) lc.m_biome = Heightmap.Biome.AshLands;
                    try
                    {
                        spawn.Invoke(ZoneSystem.instance, new object[] { loc, 4242, pos, Quaternion.identity, ZoneSystem.SpawnMode.Full, new List<GameObject>(), false });
                    }
                    catch (Exception e) { Plugin.Log.LogError($"{Tag} spawn {name}: {e}"); }
                    finally
                    {
                        if (lc) lc.m_biome = oldBiome;
                        loc.m_prefab.Release();
                    }
                    yield return new WaitForSeconds(3f);

                    var created = SectorZdos(pos).Where(z => !before.Contains(z.m_uid)).ToList();
                    var counts = created.GroupBy(z => ZNetScene.instance.GetPrefab(z.GetPrefab())?.name ?? z.GetPrefab().ToString())
                        .OrderByDescending(g => g.Count()).Select(g => $"{g.Key}x{g.Count()}");
                    int tagged = created.Count(z => z.GetInt(RuinsRebornPatches.RestoreTag) == 1);
                    lines.Add($"{name}: {created.Count} objects ({tagged} restoration): {string.Join(" ", counts)}");

                    // Frame the structure's real bounds (towers are much taller than the location radius).
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
                    var bounds = bb ?? new Bounds(pos + Vector3.up * 8f, Vector3.one * 24f);
                    var r = Mathf.Max(bounds.extents.magnitude, 10f);
                    var focus = bounds.center;
                    lines.Add($"  bounds center={bounds.center} size={bounds.size}");
                    foreach (var variant in variants)
                    {
                    RuinsRebornPatches.RebuildOverride = variant == "rebuilt";
                    RuinsRebornPatches.OnRestoreToggled();
                    yield return new WaitForSeconds(1f);
                    foreach (var yaw in new[] { 30, 120, 210, 300 })
                    {
                        var q = Quaternion.Euler(0f, yaw, 0f);
                        var camPos = focus + q * new Vector3(0f, r * 0.75f, -r * 1.9f);
                        PhotoModePatches.SetCameraOverride(camPos, Quaternion.LookRotation(focus - camPos));
                        yield return new WaitForSeconds(1.2f);
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{name}_{variant}_y{yaw}.png"));
                        yield return new WaitForSeconds(0.6f);
                    }
                    var top = focus + Vector3.up * (r * 2.6f);
                    PhotoModePatches.SetCameraOverride(top, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    yield return new WaitForSeconds(1.2f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{name}_{variant}_top.png"));
                    yield return new WaitForSeconds(0.6f);
                    }

                    foreach (var z in created) if (z.IsValid()) RuinsRebornPatches.DeleteZdo(z);
                    yield return new WaitForSeconds(2f);
                }
            }
            RuinsRebornPatches.RebuildOverride = null;
            File.WriteAllLines(Path.Combine(dir, "DONE.txt"), lines);
            foreach (var l in lines) Plugin.Log.LogInfo($"{Tag} {l}");
            Plugin.Log.LogInfo($"{Tag} DONE -> {dir}");
        }
        finally
        {
            RuinsRebornPatches.RebuildOverride = null;
            PhotoModePatches.ClearCameraOverride();
            FableBunnyPatches.SetDebugDaylight(false);
            if (hud && hud.m_rootObject) hud.m_rootObject.SetActive(true);
            _running = false;
        }
    }

    private static List<ZDO> SectorZdos(Vector3 pos)
    {
        var l = new List<ZDO>();
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(pos), new SimulationDistance(1, 1, classic: true), l);
        return l.Where(z => z.IsValid() && (z.GetPosition() - pos).sqrMagnitude < 60f * 60f).ToList();
    }
}
