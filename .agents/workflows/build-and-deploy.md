---
description: Build and deploy the Ashlands Reborn mod to the game/profile plugins folder.
---

// turbo
1. Run `dotnet build` from the `AshlandsReborn` directory.
   - Note: The `.csproj` is configured to automatically copy the resulting `AshlandsReborn.dll` to both the game's `plugins` folder and the r2modman profile plugins folder after a successful build.
2. Verify that the build succeeded and the file was copied.
