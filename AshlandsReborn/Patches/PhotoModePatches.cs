using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev-only autonomous verification harness for Charred_Melee visual changes. Spawns a
/// Charred_Melee near the player, takes over the camera to frame it from four angles plus
/// an animation-proof pair 1s apart, writes screenshots + a DONE marker, then despawns it.
/// No window focus or manual input required - safe to drive from a background build/test
/// loop. Triggered by Plugin.PhotoModeKey (default F6) or once automatically ~10s after
/// world load if Plugin.PhotoModeAuto is set. Output: "&lt;plugin dir&gt;\AR_PhotoMode\".
///
/// The t0/t1 pair specifically guards against the historical false-positive failure mode
/// where a frozen/T-posed mesh was mistaken for a working animated one: if the two frames
/// are pixel-identical, the subject isn't actually animating.
/// </summary>
[HarmonyPatch]
internal static class PhotoModePatches
{
    private const string CharredMeleePrefab = "Charred_Melee";
    private static readonly int[] Yaws = { 0, 90, 180, 270 };
    private static readonly int[] CloseYaws = { 0, 45, 90, 180, 270 };

    private static bool _running;
    private static bool _cameraOverrideActive;
    private static Vector3 _desiredCamPos;
    private static Quaternion _desiredCamRot;

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

        if (Input.GetKeyDown(Plugin.PhotoModeKey?.Value ?? KeyCode.F6) && Time.time - _lastKeyPressTime >= 2f)
        {
            _lastKeyPressTime = Time.time;
            TryStart("hotkey");
        }

        // The M4 lifecycle self-test owns the session when enabled - don't also run the
        // photo shoot (both teleport the player and spawn warriors on the same island).
        if (!_autoFired && (Plugin.PhotoModeAuto?.Value ?? false) && !(Plugin.PhotoModeM4Test?.Value ?? false)
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
            Plugin.Log?.LogWarning($"[AR PhotoMode] Already running, ignoring trigger: {trigger}");
            return;
        }
        Plugin.Log?.LogInfo($"[AR PhotoMode] Starting ({trigger})");
        Plugin.Instance.StartCoroutine(CaptureRoutine());
    }

    private static IEnumerator CaptureRoutine()
    {
        _running = true;
        try
        {
            Application.runInBackground = true;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                Plugin.Log?.LogError("[AR PhotoMode] No local player - aborting");
                yield break;
            }

            var prefab = ZNetScene.instance?.GetPrefab(CharredMeleePrefab);
            if (prefab == null)
            {
                Plugin.Log?.LogError("[AR PhotoMode] Charred_Melee prefab not found - aborting");
                yield break;
            }

            yield return TeleportToIslandRoutine(player);

            var dist = Plugin.PhotoModeSpawnDistance?.Value ?? 5f;
            var spawnPos = player.transform.position + player.transform.forward * dist + Vector3.up * 0.3f;
            var toPlayer = player.transform.position - spawnPos;
            toPlayer.y = 0f;
            var spawnRot = toPlayer.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toPlayer.normalized)
                : Quaternion.identity;

            var go = UObject.Instantiate(prefab, spawnPos, spawnRot);
            Plugin.Log?.LogInfo($"[AR PhotoMode] Spawned {CharredMeleePrefab} at {spawnPos}");

            // Let the creature settle: skeleton/animator init, and any existing Awake-hook
            // coroutines (legacy sword/armor swap, or the Fable puppet build) to finish.
            yield return new WaitForSeconds(3f);

            if (go == null)
            {
                Plugin.Log?.LogError("[AR PhotoMode] Spawned warrior was destroyed before capture - aborting");
                yield break;
            }

            var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "AR_PhotoMode");
            Directory.CreateDirectory(dir);

            _cameraOverrideActive = true;
            var shotPaths = new List<string>();

            foreach (var yaw in Yaws)
            {
                if (go == null) break;
                AimCameraAt(go, yaw);
                yield return new WaitForSeconds(0.3f); // let the camera settle into place & render
                var path = Path.Combine(dir, $"shot_{yaw}.png");
                ScreenCapture.CaptureScreenshot(path);
                shotPaths.Add(path);
                for (var f = 0; f < 5; f++) yield return null; // let the async capture finish writing
            }

            // Head/shoulder close-ups: helmet fit + idle sword-on-shoulder rest are invisible
            // at full-body framing (the M3 "helmet sane" false pass came from exactly that).
            foreach (var yaw in CloseYaws)
            {
                if (go == null) break;
                AimCameraAt(go, yaw, closeUp: true);
                yield return new WaitForSeconds(0.3f);
                var path = Path.Combine(dir, $"close_{yaw}.png");
                ScreenCapture.CaptureScreenshot(path);
                shotPaths.Add(path);
                for (var f = 0; f < 5; f++) yield return null;
            }

            // Animation-proof pair: same angle, 1s apart. Screenshots alone are a weak signal
            // here (a held guard/idle stance can look identical to a truly frozen T-pose to
            // the eye) - so we also read the Animator directly for an objective signal that
            // doesn't depend on visually comparing two images.
            var animator = go != null ? go.GetComponentInChildren<Animator>() : null;
            string animLine = "Animator check: unavailable (no Animator found or warrior destroyed early)";
            AnimatorStateInfo s0 = default;
            Vector3 pos0 = default;
            var haveT0Sample = false;

            if (go != null)
            {
                AimCameraAt(go, 0);
                yield return new WaitForSeconds(0.3f);

                if (animator != null)
                {
                    s0 = animator.GetCurrentAnimatorStateInfo(0);
                    pos0 = go.transform.position;
                    haveT0Sample = true;
                }

                var t0Path = Path.Combine(dir, "shot_t0.png");
                ScreenCapture.CaptureScreenshot(t0Path);
                shotPaths.Add(t0Path);
                for (var f = 0; f < 5; f++) yield return null;

                yield return new WaitForSeconds(1.0f);
            }

            if (go != null)
            {
                AimCameraAt(go, 0);
                yield return new WaitForSeconds(0.3f);

                if (animator != null && haveT0Sample)
                {
                    var s1 = animator.GetCurrentAnimatorStateInfo(0);
                    var pos1 = go.transform.position;
                    var sameState = s0.fullPathHash == s1.fullPathHash;
                    var animAdvanced = sameState && Mathf.Abs(s1.normalizedTime - s0.normalizedTime) > 0.001f;
                    var posDelta = Vector3.Distance(pos0, pos1);
                    animLine = $"Animator check: stateHash t0={s0.fullPathHash} t1={s1.fullPathHash} " +
                               $"normalizedTime t0={s0.normalizedTime:F4} t1={s1.normalizedTime:F4} " +
                               $"posDelta={posDelta:F4} -> {(animAdvanced || !sameState ? "ANIMATING" : "STATIC")}";
                }

                var t1Path = Path.Combine(dir, "shot_t1.png");
                ScreenCapture.CaptureScreenshot(t1Path);
                shotPaths.Add(t1Path);
                for (var f = 0; f < 5; f++) yield return null;
            }

            _cameraOverrideActive = false;

            if (go != null)
                ZNetScene.instance?.Destroy(go);

            for (var f = 0; f < 10; f++) yield return null;

            Plugin.Log?.LogInfo($"[AR PhotoMode] {animLine}");

            var donePath = Path.Combine(dir, "DONE.txt");
            File.WriteAllLines(
                donePath,
                new[] { $"Completed {DateTime.Now:O}", animLine }.Concat(shotPaths).ToArray());

            Plugin.Log?.LogInfo($"[AR PhotoMode] DONE {shotPaths.Count} shots -> {dir}");
        }
        finally
        {
            _cameraOverrideActive = false;
            _running = false;
        }
    }

    /// <summary>
    /// Teleport to the test island (flat ocean platform, clutter-free backgrounds).
    /// Force-killed dev sessions skip the world save, so the player's logout point can
    /// regress to world spawn between runs - never rely on where the player happens
    /// to be standing. Shared by the photo shoot and the M4 lifecycle self-test.
    /// </summary>
    internal static IEnumerator TeleportToIslandRoutine(Player player)
    {
        var islandPos = ParseIslandPos(Plugin.PhotoModeIslandPos?.Value);
        if (islandPos == null || Vector3.Distance(player.transform.position, islandPos.Value) <= 15f)
            yield break;

        Plugin.Log?.LogInfo($"[AR PhotoMode] Teleporting player to test island {islandPos.Value}");
        // TeleportTo refuses during its ~2s post-load cooldown - retry until accepted.
        var accepted = false;
        for (var attempt = 0; attempt < 20 && !accepted; attempt++)
        {
            accepted = player.TeleportTo(islandPos.Value, player.transform.rotation, distantTeleport: true);
            if (!accepted) yield return new WaitForSeconds(0.5f);
        }
        var timeout = Time.time + 30f;
        while (player.IsTeleporting() && Time.time < timeout) yield return null;
        if (!accepted || player.IsTeleporting())
            Plugin.Log?.LogWarning("[AR PhotoMode] Island teleport incomplete - capturing at current position");
        yield return new WaitForSeconds(3f); // let terrain/objects around the island settle
    }

    // Camera-override access for the M4 lifecycle self-test (reuses this class's
    // GameCamera.LateUpdate prefix).
    internal static void SetCameraOverride(Vector3 pos, Quaternion rot)
    {
        _desiredCamPos = pos;
        _desiredCamRot = rot;
        _cameraOverrideActive = true;
    }

    internal static void EnableCameraOverride() => _cameraOverrideActive = true;

    internal static void ClearCameraOverride() => _cameraOverrideActive = false;

    private static Vector3? ParseIslandPos(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw!.Split(',');
        if (parts.Length != 3) return null;
        if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) return null;
        if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) return null;
        if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) return null;
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Computes a scale-agnostic framing (renderer-bounds based) so the same code frames
    /// both the current oversized hodgepodge warrior and the eventual Fable puppet correctly
    /// regardless of their differing heights. With closeUp=true, frames the head/shoulder
    /// region instead: focus on the puppet's helmet joint when a Fable marker is present
    /// (the union bounds include the raised sword and over-read the head height), else the
    /// top quarter of the bounds.
    /// </summary>
    internal static void AimCameraAt(GameObject go, int yaw, bool closeUp = false)
    {
        Bounds? bounds = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (bounds == null) bounds = r.bounds;
            else
            {
                var b = bounds.Value;
                b.Encapsulate(r.bounds);
                bounds = b;
            }
        }

        var center = bounds?.center ?? go.transform.position + Vector3.up;
        var size = bounds?.size ?? Vector3.one * 2f;

        float camDistance;
        if (closeUp)
        {
            var marker = go.GetComponent<AshlandsRebornFableWarrior>();
            var helmJoint = marker != null && marker.PuppetVis != null ? marker.PuppetVis.m_helmet : null;
            if (helmJoint != null)
                center = helmJoint.position - Vector3.up * 0.1f * Mathf.Max(1f, helmJoint.position.y - go.transform.position.y);
            else
                center = center + Vector3.up * (size.y * 0.25f);
            var headHeight = Mathf.Max(center.y - go.transform.position.y, 1f);
            camDistance = Mathf.Clamp(headHeight * 0.6f, 1.2f, 5f);
        }
        else
        {
            camDistance = Mathf.Clamp(size.magnitude * 1.3f, 3f, 20f);
        }

        var rot = Quaternion.Euler(0f, yaw, 0f);
        var camPos = center + rot * new Vector3(0f, 0f, -camDistance)
                     + (closeUp ? Vector3.zero : Vector3.up * (size.y * 0.15f));

        _desiredCamPos = camPos;
        _desiredCamRot = Quaternion.LookRotation((center - camPos).normalized, Vector3.up);
    }

    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    [HarmonyPrefix]
    private static bool GameCamera_LateUpdate_Prefix(GameCamera __instance)
    {
        if (!_cameraOverrideActive) return true;
        __instance.transform.SetPositionAndRotation(_desiredCamPos, _desiredCamRot);
        return false;
    }
}
