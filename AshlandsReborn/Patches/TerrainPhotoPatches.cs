using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev-only autonomous verification harness for the terrain transition styles. Teleports
/// the player to the known green/ash/lava boundary test spot (TerrainPhotoPos), then for
/// each TerrainTransitionStyle: sets the config value (whose SettingChanged fires
/// ForceTerrainRefresh), waits for the rebuild to drain, and captures a top-down + an
/// oblique screenshot. Output: "&lt;plugin dir&gt;\AR_TerrainPhoto\" + a DONE.txt marker +
/// an "[AR TerrainPhoto] DONE" log line for the outer build/launch/evaluate loop.
///
/// No Harmony patches - driven by Tick() from Plugin.Update() (LifecycleTestPatches
/// pattern) and reuses PhotoModePatches' GameCamera override + teleport machinery.
/// Triggered by Plugin.TerrainPhotoKey (default F7) or once automatically ~10s after
/// world load if Plugin.TerrainPhotoAuto is set.
/// </summary>
internal static class TerrainPhotoPatches
{
    private static readonly string[] Styles = { "Legacy", "MudBlend", "GrassToLava", "DebugGradient" };
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

            foreach (var style in Styles)
            {
                Plugin.Log?.LogInfo($"[AR TerrainPhoto] Style {style}");
                Plugin.TerrainTransitionStyle!.Value = style; // SettingChanged fires ForceTerrainRefresh
                yield return new WaitForSeconds(0.5f);
                timeout = Time.time + 10f;
                while (Heightmap.HaveQueuedRebuild(pos, RebuildWatchRadius) && Time.time < timeout) yield return null;
                yield return new WaitForSeconds(1.5f); // clutter + render settle

                // Top-down: the angle that exposed the grid/yellow-line artifacts.
                PhotoModePatches.SetCameraOverride(
                    ground + Vector3.up * 45f,
                    Quaternion.LookRotation(Vector3.down, Vector3.forward));
                yield return new WaitForSeconds(0.3f);
                var topPath = Path.Combine(dir, $"terrain_{style}_top.png");
                ScreenCapture.CaptureScreenshot(topPath);
                shotPaths.Add(topPath);
                for (var f = 0; f < 5; f++) yield return null; // let the async capture finish writing

                // Oblique: how the fade reads at play-time camera angles.
                var camPos = ground + new Vector3(0f, 18f, -30f);
                PhotoModePatches.SetCameraOverride(
                    camPos,
                    Quaternion.LookRotation((ground - camPos).normalized, Vector3.up));
                yield return new WaitForSeconds(0.3f);
                var obliquePath = Path.Combine(dir, $"terrain_{style}_oblique.png");
                ScreenCapture.CaptureScreenshot(obliquePath);
                shotPaths.Add(obliquePath);
                for (var f = 0; f < 5; f++) yield return null;

                // Ground-level close-up looking south across the transition band (the
                // historical yellow fringe was most visible at walking distance).
                var closePos = ground + new Vector3(0f, 2.5f, 8f);
                var closeAim = ground + new Vector3(0f, 0f, -12f);
                PhotoModePatches.SetCameraOverride(
                    closePos,
                    Quaternion.LookRotation((closeAim - closePos).normalized, Vector3.up));
                yield return new WaitForSeconds(0.3f);
                var closePath = Path.Combine(dir, $"terrain_{style}_close.png");
                ScreenCapture.CaptureScreenshot(closePath);
                shotPaths.Add(closePath);
                for (var f = 0; f < 5; f++) yield return null;
            }

            File.WriteAllLines(
                Path.Combine(dir, "DONE.txt"),
                new[] { $"Completed {DateTime.Now:O}" }.Concat(shotPaths).ToArray());
            Plugin.Log?.LogInfo($"[AR TerrainPhoto] DONE {shotPaths.Count} shots -> {dir}");
        }
        finally
        {
            // Restoring the style fires one more refresh - intended, so a hotkey run
            // leaves the world rendering the user's configured style.
            if (Plugin.TerrainTransitionStyle != null && Plugin.TerrainTransitionStyle.Value != originalStyle)
                Plugin.TerrainTransitionStyle.Value = originalStyle;
            PhotoModePatches.ClearCameraOverride();
            _running = false;
        }
    }
}
