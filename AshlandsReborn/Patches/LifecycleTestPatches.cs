using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AshlandsReborn.Patches;

/// <summary>
/// M4 lifecycle self-test for the Fable Warrior puppet. Dev-only, fully autonomous:
/// enabled via Plugin.PhotoModeM4Test, fires once ~10s after world load. Teleports to the
/// test island, spawns three Charred_Melee (one 2-star at spawn, one leveled up after its
/// puppet is built), then asserts:
///   - all three puppets build (multi-warrior support),
///   - the 2-star puppet is proportionally larger (star scaling at spawn),
///   - a post-build level-up is absorbed by the periodic scale-drift check,
///   - master-switch OFF leaves zero markers/puppets and re-shows the charred (x3 cycles),
///   - master-switch ON rebuilds all puppets (x3 cycles),
///   - RefreshAll (the F10 path) rebuilds cleanly,
///   - a player helmet change syncs to every puppet within one 2s sync tick.
/// Each assertion logs "[AR M4] CHECK name: PASS/FAIL (detail)"; the run ends with
/// "[AR M4] DONE pass=X fail=Y" plus M4_RESULTS.txt and screenshots in AR_PhotoMode\,
/// which the outer autonomous loop polls and reads.
/// </summary>
internal static class LifecycleTestPatches
{
    private const string CharredMeleePrefab = "Charred_Melee";
    private const string TestHelmet = "HelmetBronze";

    private static bool _running;
    private static bool _autoFired;
    private static float _worldLoadedAt = -1f;

    private static readonly FieldInfo? FHelmetItemName = AccessTools.Field(typeof(VisEquipment), "m_helmetItem");

    private static int _pass;
    private static int _fail;
    private static readonly List<string> Results = new();

    /// <summary>Called from Plugin.Update() every frame.</summary>
    internal static void Tick()
    {
        if (Player.m_localPlayer == null)
        {
            _worldLoadedAt = -1f;
            return;
        }
        if (_worldLoadedAt < 0f) _worldLoadedAt = Time.time;

        if (_autoFired || !(Plugin.PhotoModeM4Test?.Value ?? false)) return;
        if (Time.time - _worldLoadedAt < 10f) return;

        _autoFired = true;
        Plugin.Log?.LogInfo("[AR M4] Starting lifecycle self-test");
        Plugin.Instance.StartCoroutine(RunRoutine());
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) _pass++; else _fail++;
        var line = $"[AR M4] CHECK {name}: {(ok ? "PASS" : "FAIL")} ({detail})";
        Results.Add(line);
        Plugin.Log?.LogInfo(line);
    }

    private static AshlandsRebornFableWarrior[] Markers()
        => UObject.FindObjectsByType<AshlandsRebornFableWarrior>(FindObjectsSortMode.None);

    private static float PuppetLossyY(GameObject warrior)
    {
        var m = warrior != null ? warrior.GetComponent<AshlandsRebornFableWarrior>() : null;
        return m != null && m.Puppet != null ? m.Puppet.transform.lossyScale.y : 0f;
    }

    private static IEnumerator RunRoutine()
    {
        if (_running) yield break;
        _running = true;
        _pass = 0;
        _fail = 0;
        Results.Clear();

        var spawned = new List<GameObject>();
        var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".", "AR_PhotoMode");
        var player = Player.m_localPlayer;
        ItemDrop.ItemData? bronzeItem = null;
        ItemDrop.ItemData? origHelmData = null;
        var helmetRestored = true;

        try
        {
            Application.runInBackground = true;
            var prefab = ZNetScene.instance?.GetPrefab(CharredMeleePrefab);
            if (player == null || prefab == null || !Plugin.IsFablePuppetActive)
            {
                Check("prereq", false, $"player={(player != null)} prefab={(prefab != null)} fableActive={Plugin.IsFablePuppetActive}");
                yield break;
            }
            Directory.CreateDirectory(dir);

            yield return PhotoModePatches.TeleportToIslandRoutine(player);

            // --- spawn 3 warriors: [0] 1-star, [1] 2-star at spawn, [2] leveled up later ---
            var basePos = player.transform.position + player.transform.forward * 7f;
            var right = player.transform.right;
            var faceBack = Quaternion.LookRotation(-player.transform.forward);
            foreach (var off in new[] { -2.5f, 0f, 2.5f })
                spawned.Add(UObject.Instantiate(prefab, basePos + right * off + Vector3.up * 0.3f, faceBack));
            yield return null; // let Awake/Start (incl. LevelEffects delegate wiring) run
            spawned[1].GetComponent<Character>()?.SetLevel(3); // 2-star
            Plugin.Log?.LogInfo("[AR M4] Spawned 3 warriors (middle one 2-star)");

            yield return new WaitForSeconds(4f);

            var markers = Markers();
            Check("build_3", markers.Length == 3 && markers.All(m => m.Built && m.Puppet != null),
                $"markers={markers.Length} built={markers.Count(m => m.Built)}");

            var oneStar = PuppetLossyY(spawned[0]);
            var twoStar = PuppetLossyY(spawned[1]);
            Check("star2_spawn_scale", twoStar > oneStar * 1.05f, $"1star={oneStar:F3} 2star={twoStar:F3}");

            // --- late level-up: the periodic drift absorber must grow the built puppet ---
            var lateBefore = PuppetLossyY(spawned[2]);
            spawned[2].GetComponent<Character>()?.SetLevel(3);
            yield return new WaitForSeconds(3f);
            var lateAfter = PuppetLossyY(spawned[2]);
            Check("star2_late_absorb", lateAfter > lateBefore * 1.05f, $"before={lateBefore:F3} after={lateAfter:F3}");

            yield return CaptureGroup(spawned, dir, "m4_group_stars.png");

            // --- master-switch toggle x3: OFF must leave zero traces, ON must rebuild ---
            for (var i = 1; i <= 3; i++)
            {
                Plugin.Instance.ApplyMasterSwitch(false);
                yield return new WaitForSeconds(0.7f); // marker/puppet Destroy is end-of-frame
                var leftover = Markers().Length;
                var puppetLeft = GameObject.Find("AR_FablePuppet") != null;
                var charredVisible = spawned[0] != null
                    && spawned[0].GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.enabled);
                Check($"toggle{i}_off_clean",
                    leftover == 0 && !puppetLeft && FableWarriorPatches.RegistryCount == 0 && charredVisible,
                    $"markers={leftover} puppetFound={puppetLeft} registry={FableWarriorPatches.RegistryCount} charredVisible={charredVisible}");

                Plugin.Instance.ApplyMasterSwitch(true);
                yield return new WaitForSeconds(2.5f);
                var ms = Markers();
                Check($"toggle{i}_on_rebuild", ms.Length == 3 && ms.All(m => m.Built),
                    $"markers={ms.Length} built={ms.Count(m => m.Built)}");
            }

            // --- F10 path: RefreshAll tears down and rebuilds ---
            FableWarriorPatches.RefreshAll();
            yield return new WaitForSeconds(2.5f);
            var after = Markers();
            Check("f10_rebuild", after.Length == 3 && after.All(m => m.Built),
                $"markers={after.Length} built={after.Count(m => m.Built)}");

            // --- live armor sync: a REAL equip change (Humanoid path, not just VisEquipment
            // fields - ApplyAppearance clones from the Humanoid's equipment) must reach every
            // puppet within one 2s sync tick ---
            origHelmData = AccessTools.Field(typeof(Humanoid), "m_helmetItem")?.GetValue(player) as ItemDrop.ItemData;
            bronzeItem = player.GetInventory().AddItem(TestHelmet, 1, 1, 0, 0L, "");
            var equipped = bronzeItem != null && player.EquipItem(bronzeItem, triggerEquipEffects: false);
            helmetRestored = false;
            yield return new WaitForSeconds(2.5f);
            var puppets = Markers();
            var synced = equipped && puppets.Length == 3 && puppets.All(m => m.PuppetVis != null
                && (FHelmetItemName?.GetValue(m.PuppetVis) as string) == TestHelmet);
            Check("armor_sync_2s", synced, $"equipped={equipped} puppets={puppets.Length} allBronze={synced}");

            yield return CaptureGroup(spawned, dir, "m4_group_bronzehelm.png");

            if (spawned[1] != null)
            {
                PhotoModePatches.EnableCameraOverride();
                PhotoModePatches.AimCameraAt(spawned[1], 180, closeUp: true);
                yield return new WaitForSeconds(0.3f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "m4_2star_close.png"));
                for (var f = 0; f < 5; f++) yield return null;
                PhotoModePatches.ClearCameraOverride();
            }

            // restore the player's real helmet and let the puppets sync back
            RestoreHelmet(player, ref bronzeItem, origHelmData);
            helmetRestored = true;
            yield return new WaitForSeconds(2.5f);
        }
        finally
        {
            PhotoModePatches.ClearCameraOverride();
            if (!helmetRestored)
            {
                try { RestoreHelmet(player, ref bronzeItem, origHelmData); } catch { /* best effort */ }
            }
            foreach (var go in spawned)
                if (go != null) ZNetScene.instance?.Destroy(go);

            var summary = $"[AR M4] DONE pass={_pass} fail={_fail} -> {Path.Combine(dir, "M4_RESULTS.txt")}";
            Results.Add(summary);
            try { File.WriteAllLines(Path.Combine(dir, "M4_RESULTS.txt"), Results); } catch { /* non-fatal */ }
            Plugin.Log?.LogInfo(summary);
            _running = false;
        }
    }

    private static void RestoreHelmet(Player? player, ref ItemDrop.ItemData? bronzeItem, ItemDrop.ItemData? origHelmData)
    {
        if (player == null) return;
        if (bronzeItem != null)
        {
            player.UnequipItem(bronzeItem, triggerEquipEffects: false);
            player.GetInventory().RemoveItem(bronzeItem);
            bronzeItem = null;
        }
        if (origHelmData != null)
            player.EquipItem(origHelmData, triggerEquipEffects: false);
    }

    private static IEnumerator CaptureGroup(List<GameObject> gos, string dir, string file)
    {
        Bounds? b = null;
        foreach (var go in gos.Where(g => g != null))
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds;
                else
                {
                    var bb = b.Value;
                    bb.Encapsulate(r.bounds);
                    b = bb;
                }
            }
        }
        if (b == null) yield break;

        var center = b.Value.center;
        var toPlayer = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position - center : Vector3.back;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) toPlayer = Vector3.back;
        var dist = Mathf.Clamp(b.Value.size.magnitude * 1.1f, 4f, 25f);
        var camPos = center + toPlayer.normalized * dist + Vector3.up * (b.Value.size.y * 0.2f);
        PhotoModePatches.SetCameraOverride(camPos, Quaternion.LookRotation((center - camPos).normalized, Vector3.up));
        yield return new WaitForSeconds(0.3f);
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, file));
        for (var f = 0; f < 5; f++) yield return null;
        PhotoModePatches.ClearCameraOverride();
    }
}
