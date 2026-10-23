# Morgen skins (Fable Bunny `Ape` / `Svalt` modes)

Runtime files go in `<plugin folder>/MorgenSkins/` next to `AshlandsReborn.dll`:
`assets/*` plus, for Svalt, `monstrum.unity3d` (Monstrum's asset bundle, carved out of
`Monstrum.dll` with `scripts/carve.py`; not stored here, it's 121 MB and Therzie's).

Ape pipeline (2026-10-23), run from a scratch dir laid out like the original session:
1. `source/gor_4.png`: SD-Turbo concept (local, `C:\DEV\ai3d\gen_image.py`).
2. `source/gor4.glb`: Hunyuan3D-2mini shape (`C:\DEV\ai3d\gen_shape.py`).
3. `scripts/morgen_export.py`: Morgen mesh, bind poses and bone order from vanilla bundle `c4210710`.
4. `scripts/fit.py` + `source/spec_gor4.json`: move the Morgen joints onto the ape's anatomy (fit.json/fit.npz).
5. `scripts/r_skin.py` (Blender 5.0, headless): bone-heat weights, UV unwrap, baked fur textures (`source/variants.json`), export.
6. `scripts/build_assets.py`: writes `ape.bin` (mesh + weights + fitted bind poses + local offsets) and the Svalt living-face textures.
Runtime: `AshlandsReborn/Patches/FableMorgenSkins.cs`.
