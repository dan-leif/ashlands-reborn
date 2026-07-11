# Terrain Transition v2 — Restore the Vanilla Lava Edge

Follow-up to the terrain transition styles (commits `9a58926`, `fc5ee67`). Read the
"Terrain transition styles" section of CLAUDE.md first; this plan assumes it.

## User review of v1 (2026-07-11)

Verdict: yellow line GONE, angular grid edges GONE — but the new styles broke the lava
boundary:

1. **Visual lava edge moved (SAFETY BUG)**: In vanilla/Legacy, the visible lava edge sits
   exactly where the ground becomes functionally deadly (player sinks + ignites). In
   MudBlend/GrassToLava, terrain that is functionally lava renders as mud or even grass —
   lethal ground that looks safe.
2. **Vanilla's gentle lava→ash transition destroyed**: vanilla shows a softly glowing rim
   with tiny rivulets/cracks of lava extending into the ash. MudBlend/GrassToLava clip
   this to a sharp, sometimes squared-off edge.
3. **MudBlend reads as visual clutter**: the wide brown swamp-mud band is an unrelated
   terrain type; user wants an additional option that avoids bulk foreign terrain.
4. Deliverable: automated comparison run; user picks in-game from a set of solutions.

## Root cause (verified in code, no guessing needed)

- Gameplay lethality: `Heightmap.IsLava` = **RAW point-sampled** `m_paintMask` alpha
  (`GetVegetationMask`) `> 0.6` (Decompiled/assembly_valheim/Heightmap.cs:902-913).
- v1 styles color vertices from a **box-blurred (radius 2) + Perlin-jittered** copy of
  that mask, with full-ash starting only at m=0.45 (MudBlend) / 0.58 (GrassToLava)
  (`TerrainTransition.cs` band consts). At a sharp lava-pool edge, blur pulls a raw-0.7
  (deadly) vertex down to ~0.45 → renders as mud/grass. That is finding #1.
- The glowing rim + lava cracks are rendered **per-fragment by the terrain shader** from
  `_ClearedMaskTex` (the paint mask, bound in `Heightmap.Awake`), but ONLY where the
  AshLands vertex-color layer is active (proven by DebugGradient strip 4: Meadows color
  over molten lava kills the glow). Vanilla keeps full ash color everywhere, so the
  shader draws its transition over the whole mask ramp. v1 bands remove ash weight below
  mask 0.45/0.58 — exactly the crack zone — clipping the effect at 1m vertex resolution.
  That is finding #2 (sharp/squared edge).
- Legacy is immune because it stamps full ash at raw mask > 0.1: everything near lava is
  ash-colored and the shader owns the transition — "same as vanilla" per user.

## Fix design

### A. AshHold invariant (all styled paths; Legacy untouched — it is the revert contract)

New rule evaluated on the **raw, unblurred, unjittered** mask, before any band logic:

```
rawMask >= TransitionAshHold  =>  vertex color = (255,0,0,255), no exceptions
```

- New config `TransitionAshHold` (float slider, section "Terrain", default 0.35, range
  0.05–0.55; name avoids the dead-key deletion list in Plugin.cs — do NOT reuse
  `LavaEdgeThreshold`/`LavaTerrainThreshold` etc.). Description: mask level at/above
  which terrain always renders as vanilla ash so the shader's lava rim/cracks and the
  deadly boundary stay exactly vanilla. Lower = more vanilla ash retained around lava.
- Because raw >= 0.6 is the lethal zone and hold <= 0.55 < 0.6, **no functionally-lava
  vertex can ever render non-ash**, independent of blur radius and noise settings.
- `SettingChanged` on it → `ForceTerrainRefresh(force:true)` (wire like the existing
  terrain knobs) so the user can slide it live in F1 and watch the ash halo grow/shrink.
- Keep blur+jitter for the bands BELOW the hold (they killed the grid/yellow artifacts —
  don't regress that). Implementation: `ApplyStyled` keeps a raw copy of the mask grid
  before `Smooth()` (copy the interior before blurring, or build raw + blurred arrays).

### B. Re-anchor all bands to the hold (bands live entirely below it)

Parametrize band edges on H = TransitionAshHold instead of absolute consts:

- **MudBlend v2**: green below H−W; R-ramp (mud) over [H−W, H−W/3]; A-ramp (mud→ash)
  over [H−W/3, H]; full ash ≥ H. W = new config `TransitionFadeWidth` (default 0.25,
  range 0.05–0.5, slider) so the user can shrink the mud band (finding #3 partial fix).
- **GrassToLava v2**: same shape with a fixed tight rim: R-ramp [H−0.06, H−0.02],
  A-ramp [H−0.02, H]. Jitter still halved. Green runs to the edge of the vanilla ash
  halo; the shader's crack zone sits inside full ash and renders untouched.
- Band continuity at the hold contour: the A-ramp ENDS exactly at H with value 255, so
  band output ≈ (255,0,0,255) as m→H — no seam where the raw-hold rule takes over.
- Grass rule (`AllowGrassAt`): must also consult the raw mask — never allow grass where
  raw ≥ H (belt+suspenders on top of the band cutoffs, which move with H/W).

### C. Calibrate the hold + test "cracks over partial ash" (finding #2, empirically)

Extend `DebugGradient` (it already proved the alpha/Plains and glow facts):

- Keep strips 1-4. Add strip variants that cross the REAL lava pool at the test spot:
  a constant `(255,0,0,128)` half-ash patch and a constant `(255,0,0,0)` swamp patch
  over the lava area, to answer: do the rim/cracks render (dimmed?) over partial ash /
  over swamp? If they degrade gracefully, a future style could fade the crack zone into
  green directly ("gentle lava-meadow transition" the user floated). If they clip, the
  hold approach is the right and only answer — document the result in CLAUDE.md either way.
- Also add a G-channel ramp strip `(0,G,0,0)` (Mountain = gray rock): if green→gray reads
  cleanly AND gray→ash (one-vertex jump at a jittered contour) has no fringe, promote a
  new style `StoneAsh` — green → gray rock → ash — as the "no unrelated terrain" option
  (gray reads as ash-family, unlike brown swamp mud). Only build the style if the strips
  look clean; otherwise GrassToLava v2's hairline rim IS the finding-#3 answer.

### D. Harness upgrades (finding #4 — automated comparison)

`TerrainPhotoPatches.CaptureRoutine`:

1. **Vanilla reference capture**: before the style loop, set
   `Plugin.EnableTerrainOverride.Value = false` → `ForceTerrainRefresh(force:true)` →
   wait rebuild → capture `terrain_Vanilla_{top,oblique,close}.png` → re-enable. This is
   the ground truth for "lava edge in the exact vanilla place". (Restore in `finally`.)
2. **Lava-edge close-up**: aim the close shot at the lava pool ~15m south of
   `TerrainPhotoPos` (frame the rim, not open grass) so rim/crack fidelity is comparable
   across styles at fragment scale.
3. **LAVACHECK (objective safety test)**: in `ApplyStyled`, count vertices where
   `raw >= 0.6` but the final color != (255,0,0,255); log
   `[AR LavaCheck] chunk (x,z) style=S violations=N`. The harness greps its own run:
   aggregate into DONE.txt as `LAVACHECK PASS` / `LAVACHECK FAIL n=N`. Must be PASS for
   every style before showing the user anything. Also assert the grass rule: sample a
   grid of points with raw >= H and verify `AllowGrassAt` is false; log GRASSCHECK.
4. Keep the outer loop contract (CLAUDE.md): `dev.ps1` → poll for `[AR TerrainPhoto]
   DONE` → **kill valheim immediately** → read PNGs → iterate.

### E. Evaluation criteria per iteration

Compare per style against `terrain_Vanilla_*` and `terrain_Legacy_*` (which match):
(a) visible lava edge in the same place as vanilla (overlay the tops mentally — the
pool contours must coincide); (b) glowing rim + crack rivulets present and gently fading,
no sharp/squared cutoff; (c) LAVACHECK/GRASSCHECK PASS; (d) still no yellow line;
(e) still no grid stair-steps; (f) fade below the hold looks organic; (g) chunk seams
absent. Tune `TransitionAshHold` default (start 0.35; if any crack/rim clipping is
visible vs vanilla, lower toward 0.2) and `TransitionFadeWidth` default, rebuild, rerun.

## Deliverable to the user

- Fixed MudBlend + GrassToLava (+ StoneAsh if the calibration strips justify it) in the
  existing F1 dropdown; `TransitionAshHold` + `TransitionFadeWidth` sliders live-refresh.
- Side-by-side screenshots: Vanilla vs Legacy vs each fixed style (top + lava-edge
  close), images LAST in the report, LAVACHECK results quoted.
- Legacy stays byte-identical. Defaults: keep `MudBlend` unless the comparison clearly
  crowns another style — flag the recommendation to the user either way.

## Hard session rules (user may be asleep)

- **Kill valheim immediately** when the DONE marker appears (or on timeout) — never leave
  it running while analyzing screenshots or editing code.
- **Commit + push after every meaningful step** (implementation compiles, each tuning
  iteration that improves results). Plain `git push` to master.
- Update CLAUDE.md ("Terrain transition styles" section: hold invariant, calibration
  findings, new configs) and AshlandsReborn/README.md config list when done; refresh
  `screenshots/terrain-transition/` with the final gallery.

## Files to touch

- `AshlandsReborn/Patches/TerrainTransition.cs` — hold rule, re-anchored bands, raw+blur
  dual grids, DebugGradient extensions, grass-rule raw check
- `AshlandsReborn/Patches/HeightmapPatches.cs` — `ApplyStyled` passes raw grid, LAVACHECK
- `AshlandsReborn/Patches/TerrainPhotoPatches.cs` — vanilla capture, lava-edge close-up,
  LAVACHECK/GRASSCHECK aggregation into DONE.txt
- `AshlandsReborn/Plugin.cs` — `TransitionAshHold`, `TransitionFadeWidth` configs +
  SettingChanged wiring (names must avoid the dead-key deletion list ~line 933)
