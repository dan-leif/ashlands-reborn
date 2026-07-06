namespace AshlandsReborn.Patches;

/// <summary>
/// Fable Warrior: replaces the legacy Charred Warrior body/armor hodgepodge
/// (<see cref="CharredWarriorPatches"/>, gated off via ShouldSwap when
/// Plugin.EnableFableWarrior is true) with a scaled player-rig puppet driven by the
/// Charred's own animation. See the design plan for the full approach.
///
/// M0 status: config gating only. These entry points are stubs; puppet construction,
/// equipment cloning, and the bone-retarget driver land in M2/M3.
/// </summary>
internal static class FableWarriorPatches
{
    /// <summary>Rebuild puppets on all Charred_Melee instances. No-op until M2.</summary>
    internal static void RefreshAll()
    {
    }

    /// <summary>Destroy all puppets and restore Charred visuals. No-op until M2.</summary>
    internal static void RevertAll()
    {
    }

    /// <summary>Pending-equip apply + live armor-change resync, called from Plugin's 0.2s tick. No-op until M2/M3.</summary>
    internal static void PeriodicUpdate()
    {
    }
}
