# 3D Asset Correction Workflow (AI-Assisted)

This plan outlines the process of extracting, visualizing, and correcting the Charred Warrior armor issues using 3D tools and AI guidance. This bypasses the difficulty of "blind" matrix math in C#.

## Phase 1: Asset Extraction
The first step is getting the raw game files into a format that 3D software can read.

1.  **Tool**: [AssetStudio](https://github.com/Perfare/AssetStudio) or [UABE (Unity Assets Bundle Extractor)](https://github.com/nesrak1/UABE).
2.  **Target Files**:
    *   `Charred_Melee` prefab (The monster skeleton).
    *   `Player` prefab (The skeleton the armor was designed for).
    *   `knightchest` (The SouthsilArmor asset).
3.  **Action**: Export these as `.fbx` or `.obj` files.
    > [!TIP]
    > Export with "Include Hierarchy" to preserve the bone names (`Hips`, `Spine`, etc.) needed for mapping.

## Phase 2: Environment Setup
Set up a desktop environment where an AI can "see" your work.

1.  **Software**: [Blender](https://www.blender.org/) (Free, open-source).
2.  **AI Assistant**: Use a Vision-enabled AI (like Claude or GPT-4o) alongside your Blender window.
3.  **Visual Support**: Use a screen-sharing tool or take frequent screenshots of:
    *   The **Outliner** (list of bones).
    *   The **Properties Panel** (Rotation/Location/Scale).
    *   The **Viewport** (how the armor looks on the skeleton).

## Phase 3: Auto-Rigging (The "No-Modeling" Solution)
Instead of manual weight painting, use automated tools to map the armor to the monster.

1.  **Option A: Mixamo (Easiest)**
    *   Upload the `Charred_Melee` skeleton and the `knightchest` mesh to [Adobe Mixamo](https://www.mixamo.com/).
    *   The AI will auto-rig the mesh. Download the resulting `.fbx`.
2.  **Option B: Reallusion AccuRIG (Most Precise)**
    *   Download [AccuRIG](https://www.reallusion.com/smart-bones/accurig.html) (Free).
    *   Place marker dots on the mesh's joints (Knees, Hips, Shoulders).
    *   The tool will generate a perfectly weighted rig for the monster.

## Phase 4: Diagnostic Comparison
Compare the "working" auto-rigged model against your C# logic to find the missing numbers.

1.  **Coordinate Check**: In Blender, click the Hips bone of the auto-rigged armor. Look at the `X, Y, Z` coordinates in the transform panel.
2.  **Bind-Pose Logic**: If the Hips are at `Y=1.2` in Blender but your code thinks they are at `Y=0`, you have found your logic error.
3.  **AI Guidance**: Ask the AI: *"I have the armor in Blender. The Hips are at (0, 1.25, 0) but my game code puts them at 0. How do I translate this Blender offset into a Unity Matrix4x4 shift?"*

## Phase 5: Re-Implementation
Once you have the visual "Truth" from Blender:

1.  Update the `CharredWarriorPatches.cs` bind-pose math with the offsets discovered.
2.  **OR**: Save the corrected armor as a new Unity AssetBundle and load that instead of trying to fix the vanilla assets at runtime.

---

### Why this works for beginners:
*   **No Sculpting**: You aren't changing the art, just moving bones.
*   **Visual Feedback**: You see the "stretching" fix in real-time before writing a single line of code.
*   **AI Pair Programming**: I can give you the exact Blender hotkeys (`G` for grab, `R` for rotate, `S` for scale) once I see where the model is "broken" via a screenshot.
