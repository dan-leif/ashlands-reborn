# Terrain Transition v3 — "AshBlend": green → ash with NO swamp/mud terrain

Follow-up to terrain transition v2 (commits `154df06`..`90dc6ec`). Read the "Terrain
transition styles" section of CLAUDE.md first; this plan assumes all of it (AshHold
invariant, ash skirt, LAVACHECK/GRASSCHECK harness, kill-valheim-immediately rule).

## User review of v2 (2026-07-11)

v2 verdict: safety + edge quality goals all met. But **four visible terrains
(grass + swamp-mud + ash + lava) read as clutter**. The user likes MudBlend best and
wants it "with no mud at all": a new option fading green directly into ash, with
everything else retained:

- No yellow (Plains) line
- No 90/45-degree lattice edges
- Lava edge exactly where vanilla puts it (AshHold invariant + LAVACHECK stay)
- Vanilla lava edge quality: fissures/cracks + soft glowing areas
- Grass, stopping believably before the ash
- The four existing styles stay available in-game, byte-identical, for comparison
  (default stays MudBlend until the user picks)

## Why a direct green→ash vertex fade cannot work (do not re-attempt)

Recon-verified shader weight model (v1/v2 DebugGradient strips + Legacy history):
Plains(yellow) weight ≈ (1−R)·A. A straight vertex-color ramp (t·255,0,0,t·255) passes
through mid-R/mid-A → up to ~25% Plains weight mid-fade. That IS the Legacy yellow
fringe. Avoiding yellow requires A ≈ 0 until R is saturated — i.e. the fade must cross
the (255,0,0,0) "swamp corner" of color space, which renders the scorched-mud texture.
**In vertex-color space alone, "no mud AND no yellow" is impossible.** The fix must
change what the swamp corner *renders as*, not the path through it.

## Approach A (primary) — swamp-slice → ash-slice material patch ("AshBlend" style)

All biome textures are slices of ONE `_DiffuseArrayTex` Texture2DArray on the heightmap
material (see SHADER_SLICE_MAPPING.md; there is a matching normal array, t15). The
shader selects slices purely from vertex color. So: build a **cloned diffuse array whose
swamp slice(s) are replaced by the ash slice(s)**, and assign it to `m_materialInstance`
of Ashlands-converted chunks when the new style is active. Then the entire MudBlend band
machinery is reused unchanged — the R-ramp now renders green→ash directly:

- green → (R,0,0,0) renders ASH (patched slice) → (255,0,0,A) ash→ash (A only ramps the
  glow in) → full AshLands at the hold. Alpha stays 0 until R=255 → no yellow, ever.
- Visible terrains: grass + ash + lava. Glow/fissures untouched (AshHold + full ash near
  lava, same as v2). A pleasant side effect: the glow now fades IN over the A-ramp
  instead of the crimson bleed MudBlend shows at mid-alpha.

### Implementation sketch

1. **New style** `AshBlend` in the `TransitionStyle` enum + `TerrainTransitionStyle`
   AcceptableValueList + harness `Styles` array. Band geometry, skirt, grass rule,
   jitter: identical to MudBlend (share the code path; only the material patch differs).
   Existing styles untouched — Legacy stays byte-identical (revert contract).
2. **Slice recon**: identify which slice index(es) the swamp channel (R=1, A=0) samples
   in this material. SHADER_SLICE_MAPPING.md lists candidates (biome-layer slices 1, 3,
   8; base 5/14; ash block is 7+13, variation 15). Two recon routes:
   a. `scripts/extract_terrain_textures.py` (UnityPy) — dump slices, find the one that
      looks like the in-game scorched mud from the v2 close-ups.
   b. Empirical: clone array, overwrite ONE candidate slice with slice 7 (main ash),
      rebuild, photograph the AshBlend R-ramp zone; iterate until the mud is gone.
      The photo harness makes each probe one run (~4 min). A dev-only config string
      (e.g. `AshBlendSwapSlices`, CSV, default set by recon) makes probing config-only.
      NOTE: a dead config key `AshlandsTextureSlices` existed once — do NOT reuse dead
      key names from the Plugin.cs deletion list (~line 999).
3. **Runtime clone**: `Graphics.CopyTexture(srcArray, srcSlice, dstArray, dstSlice)` is
   GPU-side and ignores `isReadable`. Create `new Texture2DArray(w,h,depth,format,mips)`
   matching the original, CopyTexture every slice, then CopyTexture ash slice(s) over
   the swamp slice(s). Build ONCE per session (static cache; also cache the vanilla
   array reference for revert), assign the same clone to every chunk's material
   instance → chunks agree → no seams. Do the same for the normal array (t15) so mud
   normals don't show under ash albedo; if the normal swap visibly changes nothing,
   keep it anyway for correctness.
4. **Hook**: extend `TerrainTransition.ApplyVariationCol(mat)` (already called per-chunk
   per-rebuild from `RebuildRenderMesh_Postfix` with the material instance) into an
   `ApplyStyleMaterial(mat)` that also assigns the patched/vanilla array per the active
   style. AshBlend → patched; every other style → vanilla array (styles must not leak
   into each other during F1/harness cycling — the harness swaps styles live).
5. **Revert/lifecycle**: MasterSwitch off → vanilla array (ForceTerrainRefresh already
   rebuilds; ApplyStyleMaterial runs on rebuild — verify the OFF path restores, since
   the postfix early-returns when the override is inactive; if so, restore the array in
   the same place the variation color is restored, or accept that vanilla capture +
   Legacy/MudBlend/GrassToLava paths always assign the vanilla array first).
6. **DebugGradient stays on the VANILLA array** (it calibrates vanilla behavior).
   AshBlend's own top/close shots are the no-mud proof — mud is obvious at a glance.

### Risks / unknowns (verify empirically, in this order)

- Which slice(s) are the swamp albedo in this material (recon step 2).
- Per-layer UV scale: the ash-in-swamp-slot may tile at the swamp layer's UV scale →
  visible scale mismatch against real ash areas. Judge from the close shots; if bad,
  try copying a downscaled/upscaled mip or accept and document.
- The swamp layer might also drive non-albedo response (moisture/gloss) baked in the
  shader path, leaving a sheen difference. Judge visually.
- Compressed formats are fine for CopyTexture (same array → same format), but
  `new Texture2DArray` with a compressed GraphicsFormat needs the right ctor overload
  (`TextureCreationFlags.MipChain` etc.). If construction fails, fall back to
  RenderTexture blit per slice, or Approach B.

## Approach B (fallback) — overlapped ramps, "least-mud" tuning

If (and only if) A's material patch is infeasible: overlap the ramps so swamp weight
never dominates. R over [H−W, H−0.4W], A over [H−0.55W, H] (15% overlap; tune). Swamp
weight peaks ≈ where A starts while R≈0.85·255 → Plains weight ≤ (1−0.85)·A (faint), mud
never renders pure — reads as "scorched/dirty grass" rather than a mud terrain band.
Calibrate first with a new DebugGradient strip: (R,0,0,A) with the overlapped schedule
across one strip. Accept only if BOTH the yellow tinge and the mud read are invisible at
the close-shot distance; otherwise ship A or B2.

## Approach B2 (fallback) — thin charred seam

Keep MudBlend geometry but shrink the R-ramp to ~1.5 vertices at the skirt decay (the
minimum before lattice steps return — v2 finding says ≥1.5–2 vertices). The mud stops
reading as "swamp terrain" and becomes a ~1.5m dark charred line between grass and ash —
physically plausible burnt edge. Zero new mechanisms; weakest fix (mud still technically
present up close), but a safe shippable option if A and B both fail.

## Harness & verification (unchanged protocol)

- Add `AshBlend` to `TerrainPhotoPatches.Styles` (18 shots/run). LAVACHECK + GRASSCHECK
  must PASS for it (they will — band machinery is MudBlend's; the material patch cannot
  affect vertex colors).
- Evaluation vs `terrain_Vanilla_*`: (a) lava edge position identical; (b) fissures +
  soft glow present, no clipping; (c) checks PASS; (d) no yellow; (e) no lattice steps;
  (f) organic fade; (g) no chunk seams; **(h) zero swamp/mud texture anywhere; (i) ash
  region texture reads consistent (no tiling-scale mismatch between patched swamp-slot
  ash and real ash).**
- Outer loop per CLAUDE.md: `dev.ps1` → poll `[AR TerrainPhoto] DONE` → **kill valheim
  immediately** (also on timeout; never leave it running while analyzing) → read PNGs →
  iterate. Magnified crops (PIL, NEAREST) for lattice/tiling checks — they caught what
  full frames hid in v2.
- Final deliverable: labeled 2x2 composites (Vanilla ground truth / MudBlend / AshBlend
  top + lava-edge close; add GrassToLava if room), images LAST in the report, LAVACHECK
  lines quoted. Refresh `screenshots/terrain-transition/` and commit the composites.

## Docs & wrap-up

- Commit + push after every meaningful step (plain `git push` to master).
- Update CLAUDE.md "Terrain transition styles" (AshBlend mechanism, slice recon results,
  weight-model impossibility note so nobody re-attempts the direct fade) and
  AshlandsReborn/README.md config list.
- Update memory `project-terrain-transition-status` (v3 status + findings).
- Default style stays MudBlend until the user picks AshBlend in-game.

## Files to touch

- `AshlandsReborn/Patches/TerrainTransition.cs` — enum + AshBlend banding reuse +
  `ApplyStyleMaterial` (variation col + array assignment)
- `AshlandsReborn/Patches/HeightmapPatches.cs` — pass style through to the material hook
  (already calls ApplyVariationCol in RebuildRenderMesh_Postfix)
- `AshlandsReborn/Plugin.cs` — AcceptableValueList + (dev) `AshBlendSwapSlices` config +
  SettingChanged wiring
- `AshlandsReborn/Patches/TerrainPhotoPatches.cs` — Styles array + composite inputs
- `scripts/extract_terrain_textures.py` — slice recon (run offline, no game needed)
