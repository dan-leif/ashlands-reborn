# Game References

This folder holds the game + BepInEx DLLs the project compiles against when the Valheim
install is NOT modded (no `<game>\BepInEx\core\BepInEx.dll`). `AshlandsReborn.csproj`
prefers the live game folder and falls back to this folder automatically.

The DLLs are gitignored (game assets). `FROZEN_FROM.txt` records which Steam build they
came from. See `RESUME.md` at the repo root before overwriting them.

## Setup

Run from the AshlandsReborn folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\CopyRefs.ps1
```

Options: `-GamePath "C:\path\to\Valheim"`, `-BepInExCorePath "C:\path\to\BepInEx\core"`
(the latter defaults to the game dir's `BepInEx\core`, else the r2modman
"Ashlands Reborn" profile's).

## Files

Required: `BepInEx.dll`, `0Harmony.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`,
`Assembly-CSharp.dll`, `assembly_valheim.dll`.

Optional (referenced only when present): `BepInEx.Harmony.dll`, `BepInEx.Unity.Mono.dll`,
`UnityEngine.InputLegacyModule.dll`, `UnityEngine.PhysicsModule.dll`,
`UnityEngine.AnimationModule.dll`, `UnityEngine.ParticleSystemModule.dll`,
`UnityEngine.ScreenCaptureModule.dll`.
