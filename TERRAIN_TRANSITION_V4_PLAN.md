# Terrain Transition v4 — RockBlend, LegacySmooth, and band tone calibration

Follow-up to terrain transition v3 (TERRAIN_NO_MUD_PLAN.md, commits `f7e26ac`..`b8ea5f0`).
Read CLAUDE.md "Terrain transition styles" first; this plan assumes all of it (AshHold
invariant, ash skirt, LAVACHECK/GRASSCHECK harness, patched-diffuse-array machinery,
kill-valheim-immediately rule).

## User review of v3 AshBlend (2026-07-12)

The AshBlend band (ash-pair slice 13 in the swamp slot) is **much darker than BOTH the
meadows side and the full-ash side** — it reads worse than MudBlend's swamp band. Why it
can't match by slice-picking alone (recon-backed, Heightmap_064.asm):

- The full-ash zone near lava is NOT one texture. It renders slices **7+13 blended
  per-pixel** (noise-driven), plus the **slice-15 variation overlay** (tinted by
  `_AshlandsVariationCol` — our styled paths set the darkish OliveTint), plus a **detail
  normal + specular response** (t15/t8 sampled in the ash block), plus the glow ramp.
- The band (swamp overlay) is **one raw albedo slice with NO normal sampling** (asm lines
  382–397, albedo-only). Any single existing slice is darker and flatter than the
  composite — slice 13 was already the best single candidate (7 was far worse).

So v4 attacks the problem three ways: a rock band the user already likes the look of
(idea 1), a smoothed Legacy contour (idea 2), and synthesizing/grading the band texture
itself instead of borrowing a stock slice (idea 3).

## Idea 1 — RockBlend: the "dug rock strip" transition

User evidence: digging the GrassToLava mud rim with a pickaxe at **(149, -9600)** turns
it into gray rocky terrain, and meadow → rock → lava reads genuinely good (screenshot
reviewed 2026-07-12: a ~10 m wide gray cobbled/scaly rock strip between bright grass and
glowing lava). The strip resembles diffuse slices **d5 (base rock scales, mean 85,85,82)**
— see `extracted_textures/terrain_d_array_slice_5.png` and the labeled grid in the v3
recon — and/or **d4 (gray cliff)**.

**Why dug ground probably renders rock (verify first):** digging does not repaint the
biome; it jags the surface. The shader's slope path (asm lines 164–247 and 646–701)
blends the cliff slices **4/14** wherever per-pixel up-ness drops below ~0.999, and dug
pits trigger that across most of their surface. If confirmed, the exact dug look is
GEOMETRY-driven and a flat vertex-color band can carry the rock ALBEDO but not the dug
relief/shading. That is fine — the goal is the color transition, not fake pits — but it
sets expectations for the "match the dug area" bar: judge tone match, not bump match.

Steps:
1. **Recon the reference** (one harness visit): teleport to (149, -9600), capture the
   dug strip (vanilla + GrassToLava), and log a small grid dump over it — vertex color,
   veg mask, paint-mask RGBA, and mesh normal Y — to confirm slope-path vs paint-channel.
   (Do NOT write paint masks from the mod — TerrainComp data persists in saves.)
2. **Config-only probes** (no code, works TODAY): style=AshBlend with `AshBlendSwapSlices`
   = `3:5`, then `3:4`, `3:14`, `3:12` — one harness run each, compare the band against
   the dug-strip reference shots. This answers which slice reads closest on flat ground.
3. **Productize as `RockBlend`**: new style = **GrassToLava's tight-rim band geometry**
   (the user's screenshot IS GrassToLava's rim, dug) + a per-style swap spec
   (`RockBlendSwapSlices`, default = winning probe, likely `3:5`). Generalize the
   patched-array machinery from AshBlend-only to a per-style spec map (one cached clone
   per distinct spec; `AllPatchedArrays` + restore paths already handle any tracked
   clone). Also capture a MudBlend-width variant shot for comparison before settling.
4. Grass rule: reuse the GrassToLava cutoffs unchanged (grass close to the rock rim).

## Idea 2 — LegacySmooth: Legacy's look with an organic contour

Keep `Legacy` byte-identical (revert contract). New style `LegacySmooth`: the same
binary Meadows ↔ full-AshLands stamp — accepting the thin yellow interpolation fringe
(one ~1 m triangle wide, the GPU crossing the (t,0,0,t) diagonal) — but thresholded on
the styled path's **blurred + skirt + Perlin-jittered field** instead of the raw
stride-2 mask, so the contour wanders as a smooth organic line instead of 90°/45°
lattice rectangles.

- `EvaluateColor` case: `m >= hold ? (255,0,0,255) : (0,0,0,0)` with
  `m = max(blurred + jitter, skirt + jitter/2)` — the AshHold invariant is automatic
  (raw ≥ hold already gates full ash; LAVACHECK must still pass).
- Grass rule: mirror `AllowGrassAt` with the same cutoff minus a small margin so grass
  stops just short of the line.
- Verify in shots: fringe stays ~1 triangle wide; contour smoothness scales with
  `TransitionBlurRadius`/noise knobs; watch for jitter-created single-vertex ash
  speckles far from lava (if present, clamp jitter for this style or accept if subtle).

## Idea 3 — band tone calibration (fixes the v3 darkness complaint directly)

Stop borrowing stock slices; synthesize the band texture GPU-side:

1. Build the patched array **uncompressed** (`RGBA32`/`R8G8B8A8_SRGB`) instead of BC7:
   per slice, `Graphics.Blit(vanilla slice i → RenderTexture)` then
   `Graphics.CopyTexture(RT → clone slice i)` — GPU decompress, still ignores
   isReadable. ~4 MB VRAM (vs 1 MB BC7), trivial. Mind sRGB: use an sRGB RT format so
   round-tripped slices are byte-faithful; verify by diffing a styled shot against the
   BC7-clone build before changing anything else.
2. The band slice is then a **blit composite**, exposed as live-tunable config:
   - `AshBlendBandBrightness` (multiplier, expect ~1.3–1.6) and optional
     `AshBlendBandTint` — grade slice 13 up toward the pale full-ash zone.
   - Optional `AshBlendBandMix`: blend grass slice 0 into the band ("singed grass"
     bridge) — by construction tonally between the two sides. Judge from close shots.
3. **Objective tone target**: mean RGB of a band crop within ~15% of the adjacent
   full-ash-zone crop in the lava-edge close shot (PIL, same lighting/frame). Tune the
   brightness knob against this + eyeball.
4. Bonus knob (cheap): per-style `_AshlandsVariationCol` for AshBlend — OliveTint darkens
   the slice-15 variation overlay in the full-ash zone; a lighter variation color
   brightens the ash side toward the band, meeting in the middle.
5. Optional: AshBlend band-split knob (fraction of the fade given to the A-ramp) to
   stretch the dark→pale blend over more meters — every ramp segment must keep
   ≥ ~1.5–2 vertices of spatial width at the skirt decay (v2 finding) or lattice steps
   return.

Apply the same grading option to RockBlend's spec if the rock band needs tone help.

## Do NOT re-attempt (hard-won findings)

- Direct green→ash fade in vertex-color space (Chebyshev weights force yellow or mud —
  TERRAIN_NO_MUD_PLAN.md).
- StoneAsh via the Mountain G-ramp (renders yellow-green, DebugGradient-verified,
  user-rejected).
- Feeding the RAW mask into band ramps (1 m lattice grain).
- Changing Legacy's numbers (untouchable revert contract).
- Writing paint masks (persists in world saves).
- Slice 7 as a band texture (v3 run-1: hold gate reads as high-contrast 1 m steps).

## Verification (unchanged protocol)

- Add `LegacySmooth` + `RockBlend` to `TerrainPhotoPatches.Styles`
  (Legacy, LegacySmooth, MudBlend, AshBlend, RockBlend, GrassToLava, DebugGradient →
  24 shots/run incl. Vanilla). LAVACHECK + GRASSCHECK must PASS for every new style.
- Outer loop per CLAUDE.md: kill valheim → `dev.ps1` → poll `[AR TerrainPhoto] DONE` →
  **kill the game immediately** (also on timeout) → read PNGs → iterate. Magnified PIL
  crops (NEAREST) for lattice/tiling/tone checks; numeric band-vs-ash tone diff for
  Idea 3. TerrainPhotoPos must be GREEN ground (v3 run C teleported into open lava —
  photos useless). Also capture the dug-strip reference at (149, -9600) once.
- Commit + push after each meaningful step. Final deliverable: labeled composites
  (Vanilla / MudBlend / tuned AshBlend / RockBlend / LegacySmooth — top + lava-edge
  close), images LAST in the report; refresh `screenshots/terrain-transition/`.
- End state: TerrainPhotoAuto=false, TerrainPhotoPos=129,30,-9671, default style stays
  MudBlend until the user picks.

## Docs & wrap-up

- CLAUDE.md "Terrain transition styles" (new styles + uncompressed-array mechanics),
  AshlandsReborn/README.md config list, memory `project-terrain-transition-status`,
  gallery + composites committed.

## Files to touch

- `AshlandsReborn/Patches/TerrainTransition.cs` — enum members, LegacySmooth evaluate
  case, per-style swap-spec map, uncompressed clone + blit grading, grass-rule cases
- `AshlandsReborn/Patches/HeightmapPatches.cs` — likely no change (hooks are per-style
  agnostic); verify
- `AshlandsReborn/Plugin.cs` — AcceptableValueList, new configs
  (`RockBlendSwapSlices`, `AshBlendBandBrightness`/`Tint`/`Mix`), SettingChanged wiring
- `AshlandsReborn/Patches/TerrainPhotoPatches.cs` — Styles array; dug-strip reference
  capture helper (optional)
- `AshlandsReborn/Patches/ClutterSystemPatches.cs` — only if a new style needs a grass
  rule outside `AllowGrassAt` (unlikely; verify)

## Launch prompt (paste into a fresh session)

```
Implement TERRAIN_TRANSITION_V4_PLAN.md (repo root). It extends the terrain transition
v2/v3 work documented in CLAUDE.md's "Terrain transition styles" section — read that
section first; the plan assumes it. Hard rules: kill valheim immediately whenever the
[AR TerrainPhoto] DONE marker appears or a run times out (never leave it running between
iterations); commit+push after each meaningful step; Legacy, MudBlend, GrassToLava, and
DebugGradient stay byte-identical (AshBlend may gain tone knobs and its default look may
improve per the plan). Iterate until: (a) RockBlend and LegacySmooth pass
LAVACHECK/GRASSCHECK with organic contours (no 90/45 lattice steps; LegacySmooth's
accepted artifact is a ~1-triangle yellow fringe along a smooth line), (b) RockBlend's
band reads like the dug-rock reference at 149,-9600 (capture it for side-by-side), and
(c) AshBlend's band tone lands within ~15% mean-RGB of the adjacent full-ash zone in
the lava-edge close shots. Use magnified PIL crops for lattice/tiling/tone checks.
Report with labeled comparison composites (Vanilla vs MudBlend vs tuned AshBlend vs
RockBlend vs LegacySmooth, top + lava-edge close) posted last.
```
