using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev-only autonomous verification harness for the terrain transition styles. Teleports
/// the player to the known green/ash/lava boundary test spot (TerrainPhotoPos), captures
/// a vanilla ground-truth set (terrain override disabled), then for each
/// TerrainTransitionStyle: sets the config value (whose SettingChanged fires
/// ForceTerrainRefresh), waits for the rebuild to drain, runs LAVACHECK (no
/// functionally-lava vertex may render non-ash) + GRASSCHECK (no grass at/above the ash
/// hold), and captures top-down + oblique + lava-edge close-up screenshots. Output:
/// "&lt;plugin dir&gt;\AR_TerrainPhoto\" + a DONE.txt marker (check results first, then
/// shot paths) + an "[AR TerrainPhoto] DONE" log line for the outer
/// build/launch/evaluate loop.
///
/// No Harmony patches - driven by Tick() from Plugin.Update() (LifecycleTestPatches
/// pattern) and reuses PhotoModePatches' GameCamera override + teleport machinery.
/// Triggered by Plugin.TerrainPhotoKey (default F7) or once automatically ~10s after
/// world load if Plugin.TerrainPhotoAuto is set.
/// </summary>
internal static class TerrainPhotoPatches
{
    private static readonly string[] Styles = { "Legacy", "MudBlend", "AshBlend", "GrassToLava", "DebugGradient" };
    private const float RebuildWatchRadius = 160f;

    private static bool _running;
    private static bool _autoFired;
    private static float _worldLoadedAt = -1f;
    private static float _lastKeyPressTime;

    /// <summary>Called from Plugin.Update() every frame.</summary>
    internal static void Tick()
    {
        if (Player.m_localPlayer == null)
        {
            _worldLoadedAt = -1f;
            _autoFired = false;
            return;
        }

        if (_worldLoadedAt < 0f)
            _worldLoadedAt = Time.time;

        if (Input.GetKeyDown(Plugin.TerrainPhotoKey?.Value ?? KeyCode.F7) && Time.time - _lastKeyPressTime >= 2f)
        {
            _lastKeyPressTime = Time.time;
            TryStart("hotkey");
        }

        // The creature photo shoot / M4 self-test own the session when enabled (they
        // teleport the player to the test island; we teleport to the lava boundary).
        if (!_autoFired && (Plugin.TerrainPhotoAuto?.Value ?? false)
            && !(Plugin.PhotoModeAuto?.Value ?? false) && !(Plugin.PhotoModeM4Test?.Value ?? false)
            && Time.time - _worldLoadedAt >= 10f)
        {
            _autoFired = true;
            TryStart("auto");
        }
    }

    private static void TryStart(string trigger)
    {
        if (_running)
        {
            Plugin.Log?.LogWarning($"[AR TerrainPhoto] Already running, ignoring trigger: {trigger}");
            return;
        }
        Plugin.Log?.LogInfo($"[AR TerrainPhoto] Starting ({trigger})");
        Plugin.Instance.StartCoroutine(CaptureRoutine());
    }

    private static IEnumerator CaptureRoutine()
    {
        _running = true;
        var originalStyle = Plugin.TerrainTransitionStyle?.Value ?? "MudBlend";
        var originalOverride = Plugin.EnableTerrainOverride?.Value ?? true;
        try
        {
            Application.runInBackground = true;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                Plugin.Log?.LogError("[AR TerrainPhoto] No local player - aborting");
                yield break;
            }
            if (!Plugin.IsTerrainOverrideActive)
            {
                Plugin.Log?.LogError("[AR TerrainPhoto] Terrain override inactive (MasterSwitch/EnableTerrainOverride) - aborting");
                yield break;
            }

            var target = PhotoModePatches.ParsePos(Plugin.TerrainPhotoPos?.Value);
            if (target != null)
                yield return PhotoModePatches.TeleportRoutine(player, target.Value);
            var pos = target ?? player.transform.position;

            // Wait for the ground around the spot to exist, then for rebuilds to drain.
            var timeout = Time.time + 30f;
            while (Heightmap.FindHeightmap(pos) == null && Time.time < timeout) yield return null;
            timeout = Time.time + 15f;
            while (Heightmap.HaveQueuedRebuild(pos, RebuildWatchRadius) && Time.time < timeout) yield return null;
            yield return new WaitForSeconds(2f);

            // Anchor the camera on the real terrain height at the spot.
            var ground = pos;
            if (Heightmap.GetHeight(pos, out var h)) ground.y = h;

            var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "AR_TerrainPhoto");
            Directory.CreateDirectory(dir);
            var shotPaths = new List<string>();
            var checkLines = new List<string>();
            long totalViolations = 0;

            // Vanilla ground truth: the visible lava edge exactly where the game puts it.
            // Every styled capture is judged against this set.
            Plugin.Log?.LogInfo("[AR TerrainPhoto] Style Vanilla (override disabled)");
            Plugin.EnableTerrainOverride!.Value = false;
            EnvManPatches.ForceTerrainRefresh(force: true);
            yield return WaitForRebuild(pos);
            yield return CaptureSet("Vanilla", ground, dir, shotPaths);
            Plugin.EnableTerrainOverride.Value = true;

            HeightmapPatches.LavaCheckArmed = true;
            foreach (var style in Styles)
            {
                Plugin.Log?.LogInfo($"[AR TerrainPhoto] Style {style}");
                HeightmapPatches.ResetLavaCheck();
                if (Plugin.TerrainTransitionStyle!.Value != style)
                    Plugin.TerrainTransitionStyle.Value = style; // SettingChanged fires ForceTerrainRefresh
                else
                    EnvManPatches.ForceTerrainRefresh(force: true);
                yield return WaitForRebuild(pos);

                var lavaVerts = HeightmapPatches.LavaCheckLavaVertices;
                var lavaViol = HeightmapPatches.LavaCheckViolations;
                string lavaLine;
                if (style == "DebugGradient")
                {
                    lavaLine = $"LAVACHECK {style} EXEMPT (calibration strips paint over lava by design; lavaVerts={lavaVerts} nonAsh={lavaViol})";
                }
                else if (style == "Legacy" && lavaViol > 0)
                {
                    // Not aggregated: Legacy is the byte-identical revert contract, so its
                    // stride-2 subsampling misses (a lethal vertex whose stride-neighbor
                    // sample is <= 0.1) are reported but not fixable.
                    lavaLine = $"LAVACHECK Legacy CONTRACT n={lavaViol} (lavaVerts={lavaVerts}) - pre-existing stride-2 sampling artifact in the revert path";
                }
                else
                {
                    totalViolations += lavaViol;
                    lavaLine = $"LAVACHECK {style} {(lavaViol == 0 ? "PASS" : $"FAIL n={lavaViol}")} (lavaVerts={lavaVerts})"
                        + (lavaVerts == 0 ? " WARN no lava vertices in refresh radius - check TerrainPhotoPos" : "");
                }
                checkLines.Add(lavaLine);
                Plugin.Log?.LogInfo($"[AR TerrainPhoto] {lavaLine}");

                var (samples, grassViol) = GrassCheck(pos, style);
                var grassLine = $"GRASSCHECK {style} {(grassViol == 0 ? "PASS" : $"FAIL n={grassViol}")} (samples={samples})";
                checkLines.Add(grassLine);
                Plugin.Log?.LogInfo($"[AR TerrainPhoto] {grassLine}");

                yield return CaptureSet(style, ground, dir, shotPaths);
            }
            HeightmapPatches.LavaCheckArmed = false;

            checkLines.Insert(0, totalViolations == 0 ? "LAVACHECK PASS" : $"LAVACHECK FAIL n={totalViolations}");

            File.WriteAllLines(
                Path.Combine(dir, "DONE.txt"),
                new[] { $"Completed {DateTime.Now:O}" }.Concat(checkLines).Concat(shotPaths).ToArray());
            Plugin.Log?.LogInfo($"[AR TerrainPhoto] DONE {shotPaths.Count} shots -> {dir}");
        }
        finally
        {
            HeightmapPatches.LavaCheckArmed = false;
            if (Plugin.EnableTerrainOverride != null && Plugin.EnableTerrainOverride.Value != originalOverride)
                Plugin.EnableTerrainOverride.Value = originalOverride;
            // Restoring the style fires one more refresh - intended, so a hotkey run
            // leaves the world rendering the user's configured style.
            if (Plugin.TerrainTransitionStyle != null && Plugin.TerrainTransitionStyle.Value != originalStyle)
                Plugin.TerrainTransitionStyle.Value = originalStyle;
            PhotoModePatches.ClearCameraOverride();
            _running = false;
        }
    }

    private static IEnumerator WaitForRebuild(Vector3 pos)
    {
        yield return new WaitForSeconds(0.5f);
        var timeout = Time.time + 10f;
        while (Heightmap.HaveQueuedRebuild(pos, RebuildWatchRadius) && Time.time < timeout) yield return null;
        yield return new WaitForSeconds(1.5f); // clutter + render settle
    }

    private static IEnumerator CaptureSet(string label, Vector3 ground, string dir, List<string> shotPaths)
    {
        // Top-down: the angle that exposed the grid/yellow-line artifacts, and the one
        // whose lava-pool contours must coincide with the Vanilla reference.
        PhotoModePatches.SetCameraOverride(
            ground + Vector3.up * 45f,
            Quaternion.LookRotation(Vector3.down, Vector3.forward));
        yield return new WaitForSeconds(0.3f);
        var topPath = Path.Combine(dir, $"terrain_{label}_top.png");
        ScreenCapture.CaptureScreenshot(topPath);
        shotPaths.Add(topPath);
        for (var f = 0; f < 5; f++) yield return null; // let the async capture finish writing

        // Oblique: how the fade reads at play-time camera angles.
        var camPos = ground + new Vector3(0f, 18f, -30f);
        PhotoModePatches.SetCameraOverride(
            camPos,
            Quaternion.LookRotation((ground - camPos).normalized, Vector3.up));
        yield return new WaitForSeconds(0.3f);
        var obliquePath = Path.Combine(dir, $"terrain_{label}_oblique.png");
        ScreenCapture.CaptureScreenshot(obliquePath);
        shotPaths.Add(obliquePath);
        for (var f = 0; f < 5; f++) yield return null;

        // Lava-edge close-up: frame the rim of the lava pool ~15m south of the test spot
        // so the shader's glowing rim + crack rivulets are comparable across styles at
        // fragment scale.
        var closePos = ground + new Vector3(0f, 5f, 0f);
        var closeAim = ground + new Vector3(0f, -1.5f, -16f);
        PhotoModePatches.SetCameraOverride(
            closePos,
            Quaternion.LookRotation((closeAim - closePos).normalized, Vector3.up));
        yield return new WaitForSeconds(0.3f);
        var closePath = Path.Combine(dir, $"terrain_{label}_close.png");
        ScreenCapture.CaptureScreenshot(closePath);
        shotPaths.Add(closePath);
        for (var f = 0; f < 5; f++) yield return null;
    }

    /// <summary>Samples a world-space grid around the test spot; every point at/above the
    /// ash hold must refuse grass. Legacy uses its own contract rule (mask &gt; 0.11
    /// excludes grass), the styled paths use the shared AllowGrassAt.</summary>
    private static (int samples, int violations) GrassCheck(Vector3 center, string style)
    {
        var hold = TerrainTransition.AshHold;
        int samples = 0, violations = 0;
        for (var dz = -60f; dz <= 60f; dz += 2f)
        {
            for (var dx = -60f; dx <= 60f; dx += 2f)
            {
                var p = center + new Vector3(dx, 0f, dz);
                var hmap = Heightmap.FindHeightmap(p);
                if (hmap == null) continue;
                var raw = hmap.GetVegetationMask(p);
                if (raw < hold) continue;
                samples++;
                var allowed = style == "Legacy"
                    ? raw <= 0.11f
                    : TerrainTransition.AllowGrassAt(hmap, p);
                if (allowed) violations++;
            }
        }
        return (samples, violations);
    }
}
