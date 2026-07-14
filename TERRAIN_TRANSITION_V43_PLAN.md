# Terrain Transition v4.3 — dull the LegacySmooth line + promote LegacySmooth to primary

Follow-up to v4.2 (TERRAIN_TRANSITION_V4_PLAN.md, commits `be991a7`..`496dcd8`). Read
CLAUDE.md "Terrain transition styles" first — especially the LegacySmooth (v4.1–4.2)
blocks; this plan assumes all of it (band machinery, patched-array specs, graded clone,
photo harness, kill-valheim-immediately + delete-log-before-relaunch rules).

## User verdict on v4.2 (2026-07-12)

LegacySmooth with `LegacySmoothSwapSlices=8:0` is "by far the best version" — **promote
it to primary**: change the code default `TerrainTransitionStyle` from "MudBlend" to
"LegacySmooth". This supersedes the old "default stays MudBlend until the user picks"
note — the user has picked, and has already set the live cfg to LegacySmooth themselves.

## Repo state as of 2026-07-13 (read before assuming anything)

Commits `5f3d8a7`..`77c72c8` (after the last terrain commit `496dcd8`) are creature-side
work (Fable Race/Warrior/Mage; legacy CharredWarriorPatches deleted; Plugin.cs heavily
reorganized). Verified: the four terrain files (TerrainTransition, TerrainPhotoPatches,
HeightmapPatches, ClutterSystemPatches) are untouched, every terrain/harness config bind
survived, and the PhotoModePatches helpers the terrain harness uses (TeleportRoutine,
SetCameraOverride, ClearCameraOverride, ParsePos) still exist. PhotoModePatches gained
ForceClearSkyRoutine — terrain shots don't need it (the weather override already forces
Clear in Ashlands). Live cfg is idle: TerrainPhotoAuto=false, probe keys empty,
TerrainTransitionStyle=LegacySmooth, LegacySmoothSwapSlices=8:0, DevAutoLoad=true.

## The remaining imperfection

Reference screenshot: user-provided, taken top-down at **(86, -9528)** (any green/ash
boundary shows it; the standard test spot works too). The old plains line now renders
green (goal achieved) but a **slightly more INTENSE green** than the transition zone it
inhabits: the plains overlay paints the PURE grass texture (slice 0, up to ~half overlay
strength) over ground whose base blend at that point is grass diluted with ash — duller
and grayer. Pure saturated grass over desaturated grass-ash = a subtle saturation bump
tracing the old line. The fix: the swapped-in slice-8 content should be a MIX matching
the local blend, not pure grass.

## Design: line-mix sliders

New Terrain-section configs (all live rebuild via `OnBandArrayConfigChanged` — they bake
into the patched array, so invalidate + refresh; sliders must appear in F1):

| Config | Default | Meaning |
|---|---|---|
| `LegacySmoothLineGrass` | 0.65 | Weight of meadows grass (slice 0) in the line texture |
| `LegacySmoothLineAsh`   | 0.25 | Weight of light ash (slice 13 — NOT near-black 7, v3 finding) |
| `LegacySmoothLineMud`   | 0.05 | Weight of swamp mud (slice 3) |
| `LegacySmoothLineKhaki` | 0.05 | Weight of plains khaki (slice 8 — a little of the original back) |

- Weights are normalized by their sum in code (sum 0 → pure grass). Ranges 0–1,
  `AcceptableValueRange<float>(0f, 1f)`.
- Content is built in the existing uncompressed graded-clone path (`BuildGradedClone`
  family): decoded byte-space weighted average of the four slices, **RGB and alpha
  alike** (slice alpha modulates overlay strength — if mixing alpha visibly weakens the
  overlay, retry keeping slice 0's alpha; note which in the commit).
- The mix content is written to EVERY dst slice in `LegacySmoothSwapSlices` (default
  "8:0"; the pair's src is what the pure-grass fast path uses). **Fast path**: if the
  sliders are effectively (1,0,0,0) after normalization, keep the v4.2 byte-identical
  compressed copy behavior.
- Cache key must include the four weights. AshBlend/RockBlend params and cache keys
  unchanged.

## Autonomous iteration protocol

1. Harness probe: `TerrainPhotoProbeLineMixes` (Dev Automation, semicolon-separated
   `grass,ash,mud,khaki` tuples, e.g. `1,0,0,0;0.65,0.25,0.05,0.05;0.55,0.35,0.05,0.05`).
   Each entry sets the sliders, refreshes, captures a LegacySmooth set. Restore originals
   after. Always include `1,0,0,0` (pure grass) as the reference capture.
2. **Line mask via differencing**: line pixels = pixels that change between the pure-grass
   capture and a candidate capture at the same pose (only slice-8 overlay pixels change).
   Filter animation noise as in v4: drop glow (R>B+70), keep grayish/greenish, restrict to
   below-horizon rows. Neighbors = dilated ring around the line mask minus the mask.
3. **Metric**: on the CANDIDATE shot, compare line pixels vs ring pixels: green excess
   `G−(R+B)/2` and mean RGB. Target: deltas within ~3–5 (8-bit) AND no visible line in
   4x NEAREST zoom crops of top + oblique. The user says they'll be impressed if we can
   even see it — the eyeball check at high zoom is the real bar.
4. Iterate mixes (2–4 harness runs expected). The line lives closer to the meadow end,
   so expect the winner to stay grass-dominant. If a uniform mix can't null both the
   bright and dark flanks (the base blend varies along the band), bias toward nulling
   the MEADOW-side flank — that is where the user noticed it.
5. Set winning defaults in code AND in the live cfg (sed — Config.Bind won't update
   existing cfg entries).

Outer loop per CLAUDE.md: kill valheim → delete the BepInEx log → `dev.ps1` → poll
"starting game" then `[AR TerrainPhoto] DONE` → kill immediately → analyze PNGs.
Commit+push after each meaningful step.

## Constraints

- Legacy, MudBlend, GrassToLava, DebugGradient stay byte-identical. AshBlend and
  RockBlend rendering unchanged (shared machinery — verify their cache keys/params
  don't shift).
- LAVACHECK/GRASSCHECK must PASS for every styled path each run (vertex colors don't
  change in this work — any failure means something else broke).
- Don't touch paint masks; don't change Legacy's numbers.

## Wrap-up

- Gallery: refresh `screenshots/terrain-transition/` LegacySmooth shots + a labeled
  before/after line-zoom composite (pure grass vs tuned mix); compare_top/oblique/
  lavaedge composites regenerate automatically via the scratchpad script (rebuild it
  if the scratchpad was cleaned: labeled 5-style grid from AR_TerrainPhoto shots).
- Docs: CLAUDE.md LegacySmooth v4.2 block gains the mix-slider paragraph + the new
  default-style note; AshlandsReborn/README.md config list; memory
  `project-terrain-transition-status`.
- End state: TerrainPhotoAuto=false, all probe configs empty, default style
  LegacySmooth (code + cfg), game killed.

## Launch prompt (paste into a fresh session)

```
Implement TERRAIN_TRANSITION_V43_PLAN.md (repo root). It extends the terrain
transition v4/v4.2 work documented in CLAUDE.md's "Terrain transition styles"
section — read that section first; the plan assumes it. Hard rules: kill valheim
immediately whenever the [AR TerrainPhoto] DONE marker appears or a run times out;
delete the BepInEx log before each relaunch; commit+push after each meaningful step;
Legacy, MudBlend, GrassToLava, and DebugGradient stay byte-identical; AshBlend and
RockBlend rendering unchanged. Deliverables: (a) TerrainTransitionStyle default
flipped to LegacySmooth in code and live cfg, (b) LegacySmoothLineGrass/Ash/Mud/Khaki
sliders (F1, live rebuild) controlling the mix baked into the swapped line slice,
(c) iterate the mix until the line is invisible in 4x zoom crops (difference-based
line mask + green-excess metric per the plan; target deltas < ~3-5 8-bit), then set
the winning mix as the default in code and cfg. Report with labeled before/after
zoom composites (pure-grass line vs tuned mix, top + oblique) posted last.
```
