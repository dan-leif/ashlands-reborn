# Fable Mage Staff Plan — dropdown, per-staff orientation, reference-verified in-game loop

## Recovery anchor (read this first if resuming a fresh session)

This plan doubles as the resume document if a session dies mid-work. **M0 copies this file
into the repo as `C:\DEV\ashlands-reborn\MAGE_STAFF_PLAN.md` and commits it.** From then on,
ALL progress state lives in that repo copy: milestone checkboxes, the per-staff status table,
and recorded decisions (lamp-staff identity, dropped staffs, baked orientation values). Update
it after every game-run iteration and commit at least at each milestone boundary. A fresh
session resumes from `MAGE_STAFF_PLAN.md` + `git log` alone — do not rely on chat history.

Resume procedure: read `MAGE_STAFF_PLAN.md`, find the first unchecked box / first non-PASS
row in the status table, and continue from there. The autonomous dev-cycle procedure is in
CLAUDE.md ("Autonomous dev cycle"); the harness-specific outer loop is repeated below.

## Context

`FableMageWeapon` (the staff the Fable Mage puppet carries) is a free-text config today. The
user wants: (1) a dropdown of all 8 player staffs + the creature staffs DvergerStaffFire/Ice/
Support, charred_magestaff_fire, and **`DvergerStaffHeal` — user-identified as the Dvergr
support mage's lamp-looking staff** (DvergerStaffSupport is the green orb; both stay in the
list). `DvergerStaffBlocker` and `DvergerStaffNova` are try-and-see: user says they may not
render at all — if so, remove them; (2) every dropdown staff verified in-game to sit naturally in the
mage's hand, judged against references — the PLAYER holding each player staff, and the vanilla
creature (DvergerMageFire/Ice/Support, vanilla Charred_Mage) holding each creature staff;
(3) orientation tweak configs (by staff type as needed) to fix misoriented creature staffs,
iterating until all look natural. **Bog Witch staff is explicitly OUT of scope** (baked
SkinnedMesh on the creature, not an item — user dropped it).

Recon is DONE (do not redo): all facts below were verified against the code and the existing
`AR_MageWeapon\STAFFS.txt` catalog dump.

## Verified facts (from completed recon — trust these)

- `FableMageWeapon` bind: [Plugin.cs:805-822], ConfigEntry<string>, section "Fable Mage",
  default `StaffIceShards`, free text. `FableMageWeaponScale` at :824-831.
  `MageWeaponTestList` at :1081-1086 (section "Dev Automation", CSV).
- Dropdown idiom (repo-wide): `ConfigDescription(help, new AcceptableValueList<string>(...))`
  — templates: `TerrainTransitionStyle` Plugin.cs:240, `EnableFableMage` :750,
  `FableRaceHair` :849-858 (long list).
- SettingChanged wiring: every Fable Mage key → `OnFableWarriorModeChanged()`
  (Plugin.cs:1497-1505 → :1820-1828 → `FableWarriorPatches.RefreshAll()`, live puppet
  rebuild). Warrior grip-rot knobs subscribe the same way at :1476-1478.
- Mage profile: FableWarriorPatches.cs:142-159 — `RightItem = FableMageWeapon`, no
  `WeaponGrip` (grip block is Warrior-only).
- **Orientation injection point**: `FixupPuppetAttaches` right-hand block,
  FableWarriorPatches.cs:715-734. Warrior branch does
  `t.localRotation *= Quaternion.Euler(gripRotXYZ)` (post-multiply, hand-attach LOCAL frame);
  the `else` branch (mage today, :727-731) does scale-only via `JointLossyRatio`. The
  `LastFixedRightItem` guard means fixup runs once per instance; every SettingChanged →
  RefreshAll → fresh instance, so live tuning works.
- `EnsureCreatureWeaponAttached` :787-829: creature-staff fallback; attach children
  {attach, attach_r.hand, attach_l.hand, attach_skin}; mounts at localPosition zero +
  optional `equipoffset` child (pos += / rot *=); writes `m_rightItemInstance` so the
  instance flows through the fixup block above.
- Harness `MageWeaponTestPatches.cs` (267 lines): `MageWeaponTest` fires Run() ~10s after
  world load; output dir `<plugin assembly dir>\AR_MageWeapon\` (=
  `...\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\plugins\Dan Moore-Ashlands Reborn\AshlandsReborn\AR_MageWeapon\`;
  the Steam-path twin in DONE.txt is the same files via the junction); dumps STAFFS.txt
  catalog; forces MasterSwitch + CustomRace + EnableFableMage=CustomEquipment ON; teleports
  via `PhotoModePatches.TeleportToIslandRoutine` + `ForceClearSkyRoutine`; spawns ONE
  hardcoded `Charred_Mage` 5m ahead, frozen (MonsterAI off, kinematic RB, re-pinned);
  cycles `FableMageWeapon.Value` per staff with a **4s rebuild wait**; captures
  `{staff}_full_{yaw}.png` at yaws {90,180,270} via `AimClose` (camera aims torso↔puppet
  right hand, dist 4.2); writes DONE.txt. It cannot currently capture any references.
- Staff catalog (STAFFS.txt): ObjectDB staff items = 8 player staffs;
  DvergerStaffFire/Ice/Support/Heal/Nova/Blocker; charred_magestaff_fire/summon;
  GoblinShaman_Staff_*. ZNetScene creatures = DvergerMage, DvergerMageFire, DvergerMageIce,
  DvergerMageSupport, Charred_Mage. **Lamp staff = DvergerStaffHeal (user-confirmed;
  supersedes earlier recon speculation about Blocker/Nova/baked-mesh).**
- BepInEx clamps `ConfigEntry<string>.Value` **on set as well as on file read**: a value not
  in the AcceptableValueList silently becomes element [0]. Two consequences: (a) list the
  current default `StaffIceShards` FIRST so pre-existing free-text cfg values degrade sanely;
  (b) the harness sets `FableMageWeapon.Value` programmatically, so `MageWeaponTestList`
  must always be a subset of the dropdown list or those cycles silently test element [0].

## Design decisions

### D1 — Dropdown membership (16 entries)
`StaffIceShards` (first = clamp fallback), `StaffFireball`, `StaffShield`, `StaffSkeleton`,
`StaffRedTroll`, `StaffGreenRoots`, `StaffLightning`, `StaffClusterbomb`,
`DvergerStaffFire`, `DvergerStaffIce`, `DvergerStaffSupport`, `DvergerStaffHeal`,
`DvergerStaffNova`, `DvergerStaffBlocker`, `charred_magestaff_fire`, `None`.

- `None` sentinel replaces free-text "" (AcceptableValueList can't hold ""); the mage
  profile's `RightItem` func maps `"None"` → `""`.
- **Lamp staff = `DvergerStaffHeal` (user-confirmed, no in-game identification needed).**
  **Must-pass set** = 8 player staffs + DvergerStaffFire/Ice/Support + DvergerStaffHeal +
  charred_magestaff_fire. `DvergerStaffBlocker` and `DvergerStaffNova` are try-and-see (user:
  they may not render at all) — if a staff doesn't render or can't be made to look right
  cheaply, DROP it from the list in M4.
- OUT: `charred_magestaff_summon`, GoblinShaman staffs (not requested), Bog Witch (user cut).
- Rewrite the help text: drop the "other staffs work as free text" paragraph (they no longer
  can — the list clamps).

### D2 — Orientation: hardcoded per-staff defaults + one global knob set (hybrid)
Applied in the mage's (non-grip) right-hand branch of `FixupPuppetAttaches` (:727-731),
gated by a new per-profile flag (e.g. `StaffOrientation`, true only for Mage) so
Archer/Twitcher are untouched:

- **Layer 1 — `StaffOrientationDefaults`**: a static table in FableWarriorPatches.cs mapping
  staff prefab name → (rot Euler, pos offset), lookup = exact name, then prefix fallback
  (`DvergerStaff*`, `charred_magestaff*`), absent = identity. Ships the out-of-box look;
  filled during M3 tuning. Player staffs expected to need NO entry (their `attach` child is
  authored for the player rig, which the puppet is).
- **Layer 2 — user knobs**, section "Fable Mage", all live-rebuild via
  `OnFableWarriorModeChanged`:
  - `FableMageWeaponRotX/Y/Z` (float 0, range -180..180) — extra rotation (deg) post-multiplied
    after the built-in default, hand-attach local frame (same convention as
    `FableWarriorWeaponGripRotX/Y/Z`).
  - `FableMageWeaponOffsetX/Y/Z` (float 0, range -0.5..0.5) — extra grip position offset (m),
    local frame (creature staffs mount at position zero; grip point may sit elsewhere along
    the shaft — near-certain for a lamp-on-a-pole).
- Application order (fixed contract — baked values are meaningless if it changes):
  `t.localRotation *= Quaternion.Euler(defaultRot) * Quaternion.Euler(knobRot);`
  `t.localPosition += defaultPos + knobPos;` — after the equipoffset contribution from
  `EnsureCreatureWeaponAttached`, before/alongside the existing scale line.
- Tuning loop: knobs tune whichever staff is selected → winning values get baked into the
  table → knobs return to 0. Keeps config surface at 6 floats instead of 6×15.

### D3 — Harness extension (same entry point, new sub-flags in "Dev Automation")
- `MageWeaponRefCapture` (bool, false): prepend a reference phase to the MageWeaponTest run.
  **Must run BEFORE the force-puppet-on block** (it toggles EnableFableMage):
  1. **Player refs** (8 player staffs only — creature staffs would show an empty player hand,
     same attach_r.hand problem): resolve ItemDrop from ObjectDB → add to
     `player.GetInventory()` → `EquipItem` → wait ~1s for VisEquipment → capture
     `ref_player_{staff}_{yaw}.png` (AimClose-style framing on the PLAYER, hand target =
     player VisEquipment.m_rightHand, camera orbits yaws {90,180,270}) → unequip + remove item.
     If EquipItem refuses, log + skip, don't abort.
  2. **Vanilla Dvergr refs**: spawn `DvergerMageFire`, `DvergerMageIce`, `DvergerMageSupport`
     one at a time, same freeze recipe (MonsterAI off, kinematic, re-pin), wait ~3s for their
     staff attach, capture `ref_{creature}_{yaw}.png`, destroy. Destroy any `DvergerMistile*`
     near the spawn first (Support spawns orbiting Mistiles that photobomb).
     **`ref_DvergerMageSupport_*` is the vanilla-carry reference for DvergerStaffHeal (lamp)
     and DvergerStaffSupport (green orb).**
  3. **Vanilla Charred_Mage ref**: snapshot `EnableFableMage`, set `"Disabled"`, spawn +
     freeze + capture `ref_Charred_Mage_{yaw}.png`, destroy, restore (try/finally; the
     force-on block that follows overwrites it anyway).
- `MageWeaponRotSweep` (string, ""): `rx,ry,rz[,ox,oy,oz]` entries separated by `|`. When
  non-empty, each staff in the test list is captured once per entry: harness writes the six
  knob configs, waits 4s for the rebuild, captures `{staff}_sweep_{i}_{yaw}.png`. Knobs
  restored to pre-run values afterwards. Sweep runs shrink `MageWeaponTestList` to the staff
  under tuning (6 entries × 15 staffs × 4s ≈ 6 min of dead wait otherwise).
- `MageWeaponTestList` default expands to all 15 real staffs (everything except None).
- Run() deletes its own stale `AR_MageWeapon\DONE.txt` at start.

### Outer loop per game-run iteration (from CLAUDE.md, harness-specific)
```
Stop-Process valheim → delete BepInEx log → delete AR_MageWeapon\DONE.txt
→ powershell -ExecutionPolicy Bypass -File dev.ps1
→ poll log for "starting game", then for "[AR MageWeapon] DONE" (timeout ~5 min)
→ kill valheim immediately → read PNGs from AR_MageWeapon\ → evaluate → update plan file
```
Config file to edit between runs:
`C:\Users\Dev\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Ashlands Reborn\BepInEx\config\com.ashlandsreborn.weather.cfg`
(set `MageWeaponTest=true`, `DevAutoLoad=true`, plus per-run flags; knob values can also be
set here directly between launches instead of sweeping).

## Milestones

### M0 — Anchor the plan (no code)
- [ ] Copy this plan to `C:\DEV\ashlands-reborn\MAGE_STAFF_PLAN.md`
- [ ] Confirm clean `git status` on master
- [ ] Commit: `Plan: Fable Mage staff dropdown + orientation tuning (MAGE_STAFF_PLAN.md)`

### M1 — Harness extension + reference gallery + lamp identification — DONE (2026-07-14)
- [x] `MageWeaponTestPatches.cs`: reference phase per D3 (player refs → Dvergr refs →
      vanilla Charred_Mage with snapshot/restore), stale-DONE self-delete
- [x] `Plugin.cs`: bind `MageWeaponRefCapture`; expand `MageWeaponTestList` default
- [x] Build + full run (`MageWeaponTest=true`, `MageWeaponRefCapture=true`)
- [x] All reference shots + puppet baseline shots captured and readable (81 shots)
- [x] **DECISION**: Blocker: NO — "no recognized attach child - cannot mount it" → DROPPED.
      Nova: NO — same warning → DROPPED. (Log-confirmed; their `_full_*` shots show an empty
      hand.) Both stay in the harness gallery as evidence; remove from dropdown in M4.
- [x] Curate `screenshots/fable-mage-staffs/refs/` (16) + `.../baseline/` (22)
- [x] Per-staff status table initialized from baseline vs reference comparison
- [x] Commit

**M1 findings (baseline vs reference):**
- Player staffs (all 8, verified via StaffIceShards): player carries VERTICAL head-up; the
  puppet holds them HORIZONTAL like a lance (head forward, waist height) — the Charred idle
  animation orients the hand joint differently than the player idle, so even player staffs
  need a corrective rotation (~90° pitch family).
- DvergerStaff Fire/Heal (Ice/Support assumed same family, verify post-fix): vanilla Dvergr
  grips mid-shaft, head pointing UP-FORWARD ~35°; puppet has them INVERTED (head at knees,
  butt-cap over the shoulder) → ~180° flip + angle match.
- Lamp confirmed visually: ref_DvergerMageSupport shows the lantern staff = DvergerStaffHeal.
- charred_magestaff_fire: vanilla Charred Warlock grips mid-shaft with the claw head hanging
  DOWN near the ground, pale butt up past the shoulder; puppet grips at the claw head with
  the butt hanging down → flip + likely grip position offset along the shaft.

### M2 — Config surface: dropdown + orientation knobs (behavior-neutral at knob=0)
- [ ] `Plugin.cs:805-822`: FableMageWeapon → ConfigDescription + AcceptableValueList per D1;
      rewritten help
- [ ] `Plugin.cs`: bind `FableMageWeaponRotX/Y/Z` + `FableMageWeaponOffsetX/Y/Z` (D2) and
      `MageWeaponRotSweep` (D3); subscribe all 6 knobs in the SettingChanged block (~:1505)
- [ ] `FableWarriorPatches.cs`: mage `RightItem` maps None→""; per-profile `StaffOrientation`
      flag (Mage only); empty `StaffOrientationDefaults` table + prefix fallback; apply
      defaults+knobs in the :727-731 branch per D2's fixed order
- [ ] `MageWeaponTestPatches.cs`: rot-sweep mode
- [ ] Build clean; harness run with knobs at 0, refs off → shots match M1 baseline (no
      regression); F1 shows the dropdown
- [ ] Commit

### M3 — Per-staff tuning loop (bulk of the work; several runs)

**User config change before M3 (2026-07-14)**: the user emptied the Fable Mage armor slots
(FableMageHelmet/Chest/Legs/Shoulders = "") and set a plain default race (HairNone/BeardNone,
light skin) so the grip is readable — the bulky antlered/frostmage look hid the hands. M1
`screenshots/fable-mage-staffs/baseline/` shots show the OLD armored look; the defect
analysis is unaffected (rig-level). M3 sweep shots + the M4 final gallery use the bare look.
Do not "fix" the empty armor slots — they're intentional.
Per staff, must-pass set first: compare baseline vs reference from multiple yaws → if off,
find rot/offset via `MageWeaponRotSweep` (coarse `0,0,0|0,90,0|0,180,0|0,270,0|90,0,0|-90,0,0`,
then refine) or direct cfg edits between launches → bake winners into
`StaffOrientationDefaults` → rebuild → re-run with knobs at 0 → PASS.
- [x] All must-pass staffs PASS (13/13); Blocker/Nova DROPPED
- [x] Commit at milestone end

**Per-staff status table** (legend: PENDING / BASELINED / TUNING(values inline) / PASS / DROPPED)

| Staff | Must-pass | Reference | Status | Baked rot | Baked pos |
|---|---|---|---|---|---|
| StaffIceShards | yes | ref_player | PASS (vertical head-up, matches player) | prefix "Staff" (90,0,0) | 0 |
| StaffFireball | yes | ref_player | PASS | prefix "Staff" (90,0,0) | 0 |
| StaffShield | yes | ref_player | PASS (short staff, orb at hip = player carry) | prefix "Staff" (90,0,0) | 0 |
| StaffSkeleton | yes | ref_player | PASS (skull at hand height = player carry) | prefix "Staff" (90,0,0) | 0 |
| StaffRedTroll | yes | ref_player | PASS | prefix "Staff" (90,0,0) | 0 |
| StaffGreenRoots | yes | ref_player | PASS | prefix "Staff" (90,0,0) | 0 |
| StaffLightning | yes | ref_player | PASS | prefix "Staff" (90,0,0) | 0 |
| StaffClusterbomb | yes | ref_player | PASS | prefix "Staff" (90,0,0) | 0 |
| DvergerStaffFire | yes | ref_DvergerMageFire | PASS (head up-forward ~40°, mid-shaft grip) | prefix "DvergerStaff" (0,130,0) | 0 |
| DvergerStaffIce | yes | ref_DvergerMageIce | PASS | prefix "DvergerStaff" (0,130,0) | 0 |
| DvergerStaffSupport (green orb) | yes | ref_DvergerMageSupport | PASS | prefix "DvergerStaff" (0,130,0) | 0 |
| DvergerStaffHeal (lamp) | yes | ref_DvergerMageSupport | PASS (lantern up-forward, glowing) | prefix "DvergerStaff" (0,130,0) | 0 |
| charred_magestaff_fire | yes | ref_Charred_Mage | PASS (head-down mid-shaft like vanilla; claw tip grazes flat ground in idle — accepted, see note) | prefix "charred_magestaff" (0,75,0) | 0 |
| DvergerStaffBlocker | no | — | DROPPED (no attach child, cannot mount) | — | — |
| DvergerStaffNova | no | — | DROPPED (no attach child, cannot mount) | — | — |

**M3 notes (2026-07-14):**
- Axis recon (sweep run 1): player staffs pitch about hand-local X (X+90 = vertical head-up);
  Dverger + charred staffs pitch about Y with OPPOSITE signs per family (Dverger head up at
  +130, charred head down-forward at +75); Dverger staffs spin in place about X (their shaft
  IS the hand-local X axis).
- charred graze: probed grip offsets ±0.15 on every axis (offset sweep run) — the shaft is
  not joint-axis-aligned, so every single-axis offset visibly disconnects the hand from the
  shaft, which reads worse than the tine graze. Vanilla also hangs the claw near the ground.
  Accepted with no offset; `FableMageWeaponOffsetX/Y/Z` knobs remain for user tweaks.
- Do NOT re-derive: rotations compose as localRotation *= Euler(def)*Euler(knob) AFTER
  equipoffset; a knob sweep therefore probes RELATIVE to the baked default.

### M4 — Finalize: dropdown trim, docs, gallery
- [ ] Remove DROPPED staffs from the AcceptableValueList (and MageWeaponTestList default)
- [ ] Final full harness run at pure defaults (knobs 0, refs off) → curate
      `screenshots/fable-mage-staffs/final/`
- [ ] CLAUDE.md updates: Fable Mage config rows (dropdown semantics + 6 knobs), Dev
      Automation rows (`MageWeaponRefCapture`, `MageWeaponRotSweep`, new TestList default),
      MageWeaponTestPatches description line
- [ ] Mark this plan COMPLETE at top of MAGE_STAFF_PLAN.md; final commit

## Files to modify
- `AshlandsReborn/Plugin.cs` — binds :805-831 area + :1081-1086; SettingChanged block :1497-1505
- `AshlandsReborn/Patches/FableWarriorPatches.cs` — profile table :142-159; fixup branch
  :715-734; near `EnsureCreatureWeaponAttached` for the defaults table
- `AshlandsReborn/Patches/MageWeaponTestPatches.cs` — reference phase, sweep mode, DONE cleanup
- `CLAUDE.md`, `screenshots/fable-mage-staffs/` — M4
- Reused as-is: `PhotoModePatches` helpers (TeleportToIslandRoutine, ForceClearSkyRoutine,
  camera override), `EnsureCreatureWeaponAttached`, `RefreshAll` live-rebuild chain

## Risks / gotchas
- **Clamp-on-set**: keep `MageWeaponTestList` ⊆ dropdown members at all times, or cycles
  silently test `StaffIceShards` (element [0]).
- **LastFixedRightItem guard**: never mutate the live weapon transform from the harness;
  orientation flows only through config change → RefreshAll → fresh instance.
- **4s rebuild wait** per staff swap is load-bearing (RevertAll → 1-frame wait →
  BuildAfterSettle → 10-frame fixup delay); don't shorten it.
- **State restoration**: wrap the vanilla-Charred_Mage EnableFableMage toggle in try/finally.
  The harness force-writing MasterSwitch/CustomRace/CustomEquipment into the cfg is existing,
  accepted dev-profile behavior.
- **Stale DONE markers**: delete BepInEx log AND AR_MageWeapon\DONE.txt before every run.
- **DvergerMageSupport Mistiles** photobomb — destroy nearby `DvergerMistile*` before capture.
- **Blocker/Nova may not render** (user warning): their prefabs may lack a usable attach
  child or visible mesh. Non-rendering = DROPPED in M1's decision box, removed in M4.
- **equipoffset composition order** is a fixed contract (D2); changing it invalidates all
  baked values.
