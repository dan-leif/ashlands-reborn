using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// <summary>
/// Dev-only autonomous harness for iterating on <see cref="Plugin.FableMageWeapon"/>. It:
///   1. dumps every ObjectDB item whose name hints at a staff/wand (so the exact internal IDs of
///      creature-only staffs can be discovered), plus a per-candidate resolution report
///      (ObjectDB item? ZNetScene prefab? has an "attach" child?);
///   2. spawns a single Charred_Mage, then cycles <see cref="Plugin.MageWeaponTestList"/>, setting
///      FableMageWeapon to each ID (which live-rebuilds the mage puppet) and capturing a full-body
///      + close-up (right-hand) screenshot per weapon.
///
/// Output: "&lt;plugin dir&gt;\AR_MageWeapon\" - one PNG pair per weapon + DONE.txt + STAFFS.txt.
/// Triggered once ~10s after world load when <see cref="Plugin.MageWeaponTest"/> is true.
/// Reuses PhotoModePatches' teleport / clear-sky / camera-override / framing helpers.
/// </summary>
internal static class MageWeaponTestPatches
{
    private const string MagePrefab = "Charred_Mage";
    private static readonly int[] Yaws = { 90, 180, 270 };

    private static bool _running;
    private static bool _autoFired;
    private static float _worldLoadedAt = -1f;

    internal static void Tick()
    {
        if (Player.m_localPlayer == null)
        {
            _worldLoadedAt = -1f;
            _autoFired = false;
            return;
        }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;

        if (!_autoFired && (Plugin.MageWeaponTest?.Value ?? false) && Time.time - _worldLoadedAt >= 10f)
        {
            _autoFired = true;
            if (_running) return;
            Plugin.Log?.LogInfo("[AR MageWeapon] Starting (auto)");
            Plugin.Instance.StartCoroutine(Run());
        }
    }

    private static IEnumerator Run()
    {
        _running = true;
        try
        {
            Application.runInBackground = true;
            var player = Player.m_localPlayer;
            if (player == null) { Plugin.Log?.LogError("[AR MageWeapon] No local player"); yield break; }

            var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "AR_MageWeapon");
            Directory.CreateDirectory(dir);

            var weapons = (Plugin.MageWeaponTestList?.Value ?? "StaffIceShards")
                .Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            DumpStaffCatalog(dir, weapons);

            // Force the mage puppet ON so the harness works regardless of the saved config.
            if (Plugin.MasterSwitch != null) Plugin.MasterSwitch.Value = true;
            if (Plugin.FableRaceMode != null &&
                string.Equals(Plugin.FableRaceMode.Value, "Vanilla", StringComparison.OrdinalIgnoreCase))
                Plugin.FableRaceMode.Value = "CustomRace";
            if (Plugin.EnableFableMage != null) Plugin.EnableFableMage.Value = "CustomEquipment";

            yield return PhotoModePatches.TeleportToIslandRoutine(player);
            yield return PhotoModePatches.ForceClearSkyRoutine();

            // Spawn one mage; keep it still so it stays framed and doesn't cast/wander.
            var prefab = ZNetScene.instance?.GetPrefab(MagePrefab);
            if (prefab == null) { Plugin.Log?.LogError($"[AR MageWeapon] {MagePrefab} prefab not found"); yield break; }

            var dist = 5f;
            var spawnPos = player.transform.position + player.transform.forward * dist + Vector3.up * 0.3f;
            var toPlayer = player.transform.position - spawnPos; toPlayer.y = 0f;
            var spawnRot = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer.normalized) : Quaternion.identity;
            var go = UObject.Instantiate(prefab, spawnPos, spawnRot);
            Plugin.Log?.LogInfo($"[AR MageWeapon] Spawned {MagePrefab} at {spawnPos}");
            yield return new WaitForSeconds(3f);
            if (go == null) { Plugin.Log?.LogError("[AR MageWeapon] mage destroyed before capture"); yield break; }

            // Freeze it hard: disable AI, make the body kinematic, and re-pin position/rotation each
            // capture (disabling MonsterAI alone still let it slide off the platform over the cycle).
            var ai = go.GetComponent<MonsterAI>();
            if (ai != null) ai.enabled = false;
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.velocity = Vector3.zero; rb.isKinematic = true; }

            var shotPaths = new List<string>();
            foreach (var weapon in weapons)
            {
                if (go == null) break;
                Plugin.Log?.LogInfo($"[AR MageWeapon] === weapon: {weapon} ===");
                if (Plugin.FableMageWeapon != null) Plugin.FableMageWeapon.Value = weapon; // live rebuild
                // Rebuild chain: RevertAll -> 1 frame -> BuildAfterSettle (10f) -> ApplyAppearance
                //   -> FixupPuppetAttaches (10f wait) -> creature-weapon fallback. Give it margin.
                yield return new WaitForSeconds(4f);
                if (go == null) break;
                go.transform.SetPositionAndRotation(spawnPos, spawnRot); // undo any drift from the rebuild

                PhotoModePatches.EnableCameraOverride();
                // 90/270 = both sides (staff-in-hand reads best), 180 = front. Fixed close distance
                // (particle FX on the staffs inflate renderer bounds, so bounds-based framing pulls
                // the camera too far to judge the staff).
                foreach (var yaw in Yaws)
                {
                    if (go == null) break;
                    go.transform.SetPositionAndRotation(spawnPos, spawnRot);
                    AimClose(go, yaw);
                    yield return new WaitForSeconds(0.3f);
                    var p = Path.Combine(dir, $"{Sanitize(weapon)}_full_{yaw}.png");
                    ScreenCapture.CaptureScreenshot(p);
                    shotPaths.Add(p);
                    for (var f = 0; f < 5; f++) yield return null;
                }
                PhotoModePatches.ClearCameraOverride();
            }

            if (go != null) ZNetScene.instance?.Destroy(go);

            File.WriteAllLines(Path.Combine(dir, "DONE.txt"),
                new[] { $"Completed {DateTime.Now:O}", $"weapons: {string.Join(", ", weapons)}" }
                    .Concat(shotPaths).ToArray());
            Plugin.Log?.LogInfo($"[AR MageWeapon] DONE {shotPaths.Count} shots -> {dir}");
        }
        finally
        {
            PhotoModePatches.ClearCameraOverride();
            _running = false;
        }
    }

    /// <summary>Dump every ObjectDB item whose name hints at a staff/wand, matching ZNetScene
    /// prefabs, a per-candidate resolution report, and the child hierarchy (with renderer/mesh/
    /// particle/light markers) of each candidate prefab, to STAFFS.txt and the log.</summary>
    private static void DumpStaffCatalog(string dir, List<string> candidates)
    {
        var lines = new List<string>();
        try
        {
            var odb = ObjectDB.instance;
            string[] hints = { "staff", "wand", "dverg", "witch", "lantern", "lamp", "magestaff", "scepter" };
            if (odb != null)
            {
                lines.Add($"=== ObjectDB items matching staff-like hints ({odb.m_items.Count} items total) ===");
                foreach (var it in odb.m_items)
                {
                    if (it == null) continue;
                    var n = it.name;
                    if (hints.Any(h => n.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                        lines.Add($"  ObjectDB: {n}");
                }
            }
            else lines.Add("ObjectDB.instance is null");

            // ZNetScene has non-item prefabs too (creatures, world objects) - the Dvergr "lamp"
            // support staff and Bog Witch staff may live here rather than as ObjectDB items.
            var zns = ZNetScene.instance;
            if (zns != null)
            {
                lines.Add("");
                lines.Add("=== ZNetScene prefabs matching staff-like hints ===");
                foreach (var p in zns.m_prefabs)
                {
                    if (p == null) continue;
                    var n = p.name;
                    if (hints.Any(h => n.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                        lines.Add($"  ZNetScene: {n}");
                }
            }

            lines.Add("");
            lines.Add("=== Candidate resolution report + hierarchy ===");
            foreach (var c in candidates)
            {
                var inOdb = odb != null && odb.GetItemPrefab(c) != null;
                var zn = ZNetScene.instance?.GetPrefab(c);
                var hasAttach = false;
                if (zn != null)
                    for (var i = 0; i < zn.transform.childCount; i++)
                        if (zn.transform.GetChild(i).name == "attach") { hasAttach = true; break; }
                lines.Add($"  {c}: ObjectDB={inOdb} ZNetScene={(zn != null)} attachChild={hasAttach}");
                if (zn != null) DumpHierarchy(zn.transform, "      ", lines, 0);
            }

            // Creature prefabs whose held staff is baked into the rig (no standalone item):
            // find the mesh nodes so the lamp/bog-witch staffs can be located.
            lines.Add("");
            lines.Add("=== Creature staff-mesh search (baked-in weapons) ===");
            foreach (var cn in new[] { "DvergerMage", "DvergerMageSupport", "DvergerMageFire", "BogWitch", "BogWitchKvastur" })
            {
                var cp = ZNetScene.instance?.GetPrefab(cn);
                lines.Add($"  --- {cn} (found={cp != null}) ---");
                if (cp != null) SearchStaffNodes(cp.transform, "", lines);
            }
        }
        catch (Exception ex) { lines.Add($"dump error: {ex.Message}"); }

        File.WriteAllLines(Path.Combine(dir, "STAFFS.txt"), lines);
        foreach (var l in lines) Plugin.Log?.LogInfo($"[AR MageWeapon] {l}");
    }

    private static void DumpHierarchy(Transform t, string indent, List<string> lines, int depth)
    {
        if (depth > 5) return;
        var comps = new List<string>();
        if (t.GetComponent<SkinnedMeshRenderer>() != null) comps.Add("SkinnedMesh");
        if (t.GetComponent<MeshRenderer>() != null) comps.Add("MeshRenderer");
        if (t.GetComponent<MeshFilter>() != null) comps.Add("MeshFilter");
        if (t.GetComponent<ParticleSystem>() != null) comps.Add("Particles");
        if (t.GetComponent<Light>() != null) comps.Add("Light");
        var tag = comps.Count > 0 ? $"  [{string.Join(",", comps)}]" : "";
        lines.Add($"{indent}{t.name}{tag}");
        for (var i = 0; i < t.childCount; i++)
            DumpHierarchy(t.GetChild(i), indent + "  ", lines, depth + 1);
    }

    /// <summary>Walk a creature's full hierarchy (any depth) and print the path of every node that
    /// names a staff/lamp/weapon/attach point or carries a mesh, so a baked-in held staff can be
    /// found by transform path.</summary>
    private static void SearchStaffNodes(Transform t, string path, List<string> lines)
    {
        var here = path.Length == 0 ? t.name : path + "/" + t.name;
        var n = t.name.ToLowerInvariant();
        var nameHit = n.Contains("staff") || n.Contains("lamp") || n.Contains("lantern") ||
                      n.Contains("weapon") || n.Contains("attach") || n.Contains("kolv");
        var smr = t.GetComponent<SkinnedMeshRenderer>();
        var mr = t.GetComponent<MeshRenderer>();
        var hasMesh = smr != null || mr != null;
        if (nameHit || hasMesh)
        {
            var tag = smr != null ? "  [SkinnedMesh]" : mr != null ? "  [MeshRenderer]" : "";
            lines.Add($"      {here}{tag}");
        }
        for (var i = 0; i < t.childCount; i++)
            SearchStaffNodes(t.GetChild(i), here, lines);
    }

    /// <summary>Frame the mage's upper body at a fixed close distance from a given yaw, focusing on
    /// the right-hand staff. Uses the puppet's right-hand joint as the look target when available so
    /// the staff is centered; falls back to chest height.</summary>
    private static void AimClose(GameObject go, int yaw)
    {
        var marker = go.GetComponent<AshlandsRebornFableWarrior>();
        var hand = marker != null && marker.PuppetVis != null ? marker.PuppetVis.m_rightHand : null;
        // Aim between the torso and the hand so both the mage and the staff are in frame.
        var body = go.transform.position + Vector3.up * 1.7f;
        var center = hand != null ? Vector3.Lerp(body, hand.position, 0.5f) : body;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        const float dist = 4.2f;
        var camPos = center + rot * new Vector3(0f, 0.3f, -dist);
        PhotoModePatches.SetCameraOverride(camPos, Quaternion.LookRotation((center - camPos).normalized, Vector3.up));
    }

    private static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
