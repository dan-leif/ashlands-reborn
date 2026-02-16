# Game References

This folder should contain DLLs from your Valheim+BepInEx installation for building when Valheim is not at the default Steam path.

## Setup

Run from the AshlandsReborn folder:

```powershell
.\CopyRefs.ps1 -GamePath "C:\path\to\Valheim"
```

Replace with your actual Valheim install path (e.g. from r2modman profile or Steam).

## Required files

- BepInEx\core\BepInEx.dll
- valheim_Data\Managed\UnityEngine.dll
- valheim_Data\Managed\UnityEngine.CoreModule.dll
- valheim_Data\Managed\Assembly-CSharp.dll
- valheim_Data\Managed\assembly_valheim.dll
