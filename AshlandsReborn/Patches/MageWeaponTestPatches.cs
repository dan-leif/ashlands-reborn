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
            // Stale DONE.txt from a previous run would satisfy the outer loop's poll instantly.
            var doneFile = Path.Combine(dir, "DONE.txt");
            if (File.Exists(doneFile)) File.Delete(doneFile);

            var weapons = (Plugin.MageWeaponTestList?.Value ?? "StaffIceShards")
                .Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            DumpStaffCatalog(dir, weapons);

            var shotPaths = new List<string>();

            yield return PhotoModePatches.TeleportToIslandRoutine(player);
            yield return PhotoModePatches.ForceClearSkyRoutine();

            // Reference phase must run BEFORE the force-puppet-on block below: its vanilla
            // Charred_Mage shot needs EnableFableMage temporarily Disabled.
            if (Plugin.MageWeaponRefCapture?.Value == true)
                yield return CaptureReferences(player, dir, weapons, shotPaths);

            // Force the mage puppet ON so the harness works regardless of the saved config.
            if (Plugin.MasterSwitch != null) Plugin.MasterSwitch.Value = true;
            if (Plugin.FableRaceMode != null &&
                string.Equals(Plugin.FableRaceMode.Value, "Vanilla", StringComparison.OrdinalIgnoreCase))
                Plugin.FableRaceMode.Value = "CustomRace";
            if (Plugin.EnableFableMage != null) Plugin.EnableFableMage.Value = "CustomEquipment";

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

            Freeze(go);

            // Rotation/offset sweep mode: capture each staff once per knob combination instead of
            // once at the saved knob values. Knobs restored in the finally below.
            var sweep = ParseSweep(Plugin.MageWeaponRotSweep?.Value);
            if (sweep.Count > 0) _knobSnapshot = ReadKnobs();

            foreach (var weapon in weapons)
            {
                if (go == null) break;
                Plugin.Log?.LogInfo($"[AR MageWeapon] === weapon: {weapon} ===");
                if (Plugin.FableMageWeapon != null) Plugin.FableMageWeapon.Value = weapon; // live rebuild
                if (sweep.Count == 0)
                {
                    // Rebuild chain: RevertAll -> 1 frame -> BuildAfterSettle (10f) -> ApplyAppearance
                    //   -> FixupPuppetAttaches (10f wait) -> creature-weapon fallback. Give it margin.
                    yield return new WaitForSeconds(4f);
                    if (go == null) break;
                    yield return CaptureSubject(dir, $"{Sanitize(weapon)}_full", go, null, shotPaths, spawnPos, spawnRot);
                }
                else
                {
                    for (var i = 0; i < sweep.Count; i++)
                    {
                        if (go == null) break;
                        Plugin.Log?.LogInfo($"[AR MageWeapon] sweep {i}: [{string.Join(",", sweep[i])}]");
                        SetKnobs(sweep[i]); // each set fires a live rebuild; the wait absorbs all of them
                        yield return new WaitForSeconds(4f);
                        if (go == null) break;
                        yield return CaptureSubject(dir, $"{Sanitize(weapon)}_sweep_{i}", go, null, shotPaths, spawnPos, spawnRot);
                    }
                }
            }

            if (go != null) ZNetScene.instance?.Destroy(go);

            File.WriteAllLines(Path.Combine(dir, "DONE.txt"),
                new[] { $"Completed {DateTime.Now:O}", $"weapons: {string.Join(", ", weapons)}" }
                    .Concat(shotPaths).ToArray());
            Plugin.Log?.LogInfo($"[AR MageWeapon] DONE {shotPaths.Count} shots -> {dir}");
        }
        finally
        {
            if (_knobSnapshot != null) { SetKnobs(_knobSnapshot); _knobSnapshot = null; }
            PhotoModePatches.ClearCameraOverride();
            _running = false;
        }
    }

    private static float[]? _knobSnapshot;

    /// <summary>Parse MageWeaponRotSweep: 'rx,ry,rz[,ox,oy,oz]' entries separated by '|'.</summary>
    private static List<float[]> ParseSweep(string? raw)
    {
        var result = new List<float[]>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var entry in raw!.Split('|'))
        {
            var parts = entry.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (parts.Length != 3 && parts.Length != 6)
            {
                Plugin.Log?.LogWarning($"[AR MageWeapon] sweep entry '{entry}' is not 3 or 6 numbers - skipped");
                continue;
            }
            var vals = new float[6];
            var ok = true;
            for (var i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out vals[i])) { ok = false; break; }
            if (ok) result.Add(vals);
            else Plugin.Log?.LogWarning($"[AR MageWeapon] sweep entry '{entry}' has a non-numeric part - skipped");
        }
        return result;
    }

    private static float[] ReadKnobs() => new[]
    {
        Plugin.FableMageWeaponRotX?.Value ?? 0f, Plugin.FableMageWeaponRotY?.Value ?? 0f,
        Plugin.FableMageWeaponRotZ?.Value ?? 0f, Plugin.FableMageWeaponOffsetX?.Value ?? 0f,
        Plugin.FableMageWeaponOffsetY?.Value ?? 0f, Plugin.FableMageWeaponOffsetZ?.Value ?? 0f,
    };

    private static void SetKnobs(float[] v)
    {
        if (Plugin.FableMageWeaponRotX != null) Plugin.FableMageWeaponRotX.Value = v[0];
        if (Plugin.FableMageWeaponRotY != null) Plugin.FableMageWeaponRotY.Value = v[1];
        if (Plugin.FableMageWeaponRotZ != null) Plugin.FableMageWeaponRotZ.Value = v[2];
        if (Plugin.FableMageWeaponOffsetX != null) Plugin.FableMageWeaponOffsetX.Value = v[3];
        if (Plugin.FableMageWeaponOffsetY != null) Plugin.FableMageWeaponOffsetY.Value = v[4];
        if (Plugin.FableMageWeaponOffsetZ != null) Plugin.FableMageWeaponOffsetZ.Value = v[5];
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

    /// <summary>
    /// Reference phase (MageWeaponRefCapture): ground-truth shots for judging the puppet's staff
    /// placement - the PLAYER equipping each player staff (their "attach" child is authored for the
    /// player rig, which the puppet is), the vanilla Dvergr mages holding their own staffs, and a
    /// vanilla (puppet-disabled) Charred_Mage. Same freeze + AimClose framing as the puppet shots so
    /// everything compares side-by-side. Runs BEFORE the force-puppet-on block because it toggles
    /// EnableFableMage around the vanilla Charred_Mage spawn.
    /// </summary>
    private static IEnumerator CaptureReferences(Player player, string dir, List<string> weapons, List<string> shotPaths)
    {
        Plugin.Log?.LogInfo("[AR MageWeapon] --- reference phase ---");

        // 1) Player holding each player staff. Only names starting with "Staff" (the player set);
        //    creature staffs have no plain "attach" child, so the player's hand would come up empty.
        var prevWeapon = player.GetCurrentWeapon();
        foreach (var weapon in weapons)
        {
            if (!weapon.StartsWith("Staff", StringComparison.OrdinalIgnoreCase)) continue;
            var prefabGo = ObjectDB.instance?.GetItemPrefab(weapon);
            var drop = prefabGo != null ? prefabGo.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                Plugin.Log?.LogWarning($"[AR MageWeapon] ref: {weapon} is not an ObjectDB item - skipped");
                continue;
            }
            var item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefabGo;
            item.m_durability = item.GetMaxDurability();
            if (!player.GetInventory().AddItem(item))
            {
                Plugin.Log?.LogWarning($"[AR MageWeapon] ref: inventory full - {weapon} skipped");
                continue;
            }
            player.EquipItem(item, false);
            yield return new WaitForSeconds(1.5f); // VisEquipment attach + idle pose settle
            var hand = player.GetComponent<VisEquipment>()?.m_rightHand;
            yield return CaptureSubject(dir, $"ref_player_{Sanitize(weapon)}", player.gameObject, hand, shotPaths);
            player.UnequipItem(item, false);
            player.GetInventory().RemoveItem(item);
        }
        if (prevWeapon != null && player.GetInventory().ContainsItem(prevWeapon))
            player.EquipItem(prevWeapon, false);

        // 2) Vanilla Dvergr mages with their native staffs (Fire/Ice; Support carries the
        //    Heal-lamp/Support-orb family).
        foreach (var creature in new[] { "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport" })
            yield return CaptureCreatureRef(player, dir, creature, shotPaths);

        // 3) Vanilla Charred_Mage: puppet disabled around the spawn, restored after (the force-on
        //    block that follows overwrites it anyway - the restore is defensive).
        var mageBefore = Plugin.EnableFableMage?.Value;
        try
        {
            if (Plugin.EnableFableMage != null) Plugin.EnableFableMage.Value = "Disabled";
            yield return CaptureCreatureRef(player, dir, MagePrefab, shotPaths);
        }
        finally
        {
            if (Plugin.EnableFableMage != null && mageBefore != null) Plugin.EnableFableMage.Value = mageBefore;
        }
        Plugin.Log?.LogInfo("[AR MageWeapon] --- reference phase done ---");
    }

    /// <summary>Spawn a vanilla creature 5m ahead, freeze it, and capture the standard yaw set of
    /// it holding its native staff, then despawn it.</summary>
    private static IEnumerator CaptureCreatureRef(Player player, string dir, string prefabName, List<string> shotPaths)
    {
        var prefab = ZNetScene.instance?.GetPrefab(prefabName);
        if (prefab == null) { Plugin.Log?.LogWarning($"[AR MageWeapon] ref: creature {prefabName} not found"); yield break; }

        var spawnPos = player.transform.position + player.transform.forward * 5f + Vector3.up * 0.3f;
        var toPlayer = player.transform.position - spawnPos; toPlayer.y = 0f;
        var spawnRot = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer.normalized) : Quaternion.identity;
        var go = UObject.Instantiate(prefab, spawnPos, spawnRot);
        Plugin.Log?.LogInfo($"[AR MageWeapon] ref: spawned {prefabName}");
        yield return new WaitForSeconds(3f); // default-item VisEquipment attach
        if (go == null) { Plugin.Log?.LogWarning($"[AR MageWeapon] ref: {prefabName} vanished before capture"); yield break; }

        Freeze(go);
        DestroyNearbyMistiles(spawnPos); // DvergerMageSupport's orbiting guards photobomb

        var hand = go.GetComponent<VisEquipment>()?.m_rightHand;
        yield return CaptureSubject(dir, $"ref_{Sanitize(prefabName)}", go, hand, shotPaths, spawnPos, spawnRot);
        if (go != null) ZNetScene.instance?.Destroy(go);
    }

    /// <summary>Capture the standard yaw set of a subject: enable the camera override, optionally
    /// re-pin the subject's position/rotation before each shot (spawned creatures drift), aim via
    /// AimClose, screenshot, and release the override.</summary>
    private static IEnumerator CaptureSubject(string dir, string baseName, GameObject subject, Transform? hand,
        List<string> shotPaths, Vector3? pinPos = null, Quaternion? pinRot = null)
    {
        PhotoModePatches.EnableCameraOverride();
        // 90/270 = both sides (staff-in-hand reads best), 180 = front. Fixed close distance
        // (particle FX on the staffs inflate renderer bounds, so bounds-based framing pulls
        // the camera too far to judge the staff).
        foreach (var yaw in Yaws)
        {
            if (subject == null) break;
            if (pinPos.HasValue)
                subject.transform.SetPositionAndRotation(pinPos.Value, pinRot ?? subject.transform.rotation);
            AimClose(subject, hand, yaw);
            yield return new WaitForSeconds(0.3f);
            var p = Path.Combine(dir, $"{baseName}_{yaw}.png");
            ScreenCapture.CaptureScreenshot(p);
            shotPaths.Add(p);
            for (var f = 0; f < 5; f++) yield return null;
        }
        PhotoModePatches.ClearCameraOverride();
    }

    /// <summary>Freeze a spawned creature hard: disable AI and make the body kinematic. Position/
    /// rotation still get re-pinned per capture (disabling MonsterAI alone let it slide off the
    /// platform over a long cycle).</summary>
    private static void Freeze(GameObject go)
    {
        var ai = go.GetComponent<MonsterAI>();
        if (ai != null) ai.enabled = false;
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) { rb.velocity = Vector3.zero; rb.isKinematic = true; }
    }

    /// <summary>DvergerMageSupport spawns orbiting DvergerMistile guards that photobomb the shot -
    /// despawn any near the capture spot.</summary>
    private static void DestroyNearbyMistiles(Vector3 pos)
    {
        foreach (var view in UObject.FindObjectsOfType<ZNetView>())
        {
            if (view == null) continue;
            if (!view.name.StartsWith("DvergerMistile", StringComparison.OrdinalIgnoreCase)) continue;
            if (Vector3.Distance(view.transform.position, pos) > 40f) continue;
            ZNetScene.instance?.Destroy(view.gameObject);
        }
    }

    /// <summary>Frame the subject's upper body at a fixed close distance from a given yaw, focusing
    /// on the right-hand staff. <paramref name="hand"/> overrides the look target (player/vanilla-
    /// creature refs); when null, the Fable puppet's right-hand joint is used if present; falls back
    /// to chest height.</summary>
    private static void AimClose(GameObject go, Transform? hand, int yaw)
    {
        if (hand == null)
        {
            var marker = go.GetComponent<AshlandsRebornFableWarrior>();
            hand = marker != null && marker.PuppetVis != null ? marker.PuppetVis.m_rightHand : null;
        }
        // Aim between the torso and the hand so both the subject and the staff are in frame.
        var body = go.transform.position + Vector3.up * 1.7f;
        var center = hand != null ? Vector3.Lerp(body, hand.position, 0.5f) : body;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        const float dist = 4.2f;
        var camPos = center + rot * new Vector3(0f, 0.3f, -dist);
        PhotoModePatches.SetCameraOverride(camPos, Quaternion.LookRotation((center - camPos).normalized, Vector3.up));
    }

    private static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
