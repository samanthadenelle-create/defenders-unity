// =============================================================================
// TroopRally — the ONE global rally flag for deployed troops (WO-453 Step 4).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The rally verb is a single world point every deployed troop walks to.
// Owner 2026-09-12: nearest exterior walls used to fill the foe slot and starve
// this walk. RallyHoldsMarch now suppresses wall-ring picks until arrival
// (Peel still wins). Once at the flag they stack the most-damaged wall.
//
// Static + nullable so:
//   * the rally state survives across the deploy HUD's lifetime without a wiring
//     ref (every troop reads the same flag, same shape as SceneRouter's statics);
//   * null = "no rally set" (troops idle in place), a set value = the muster point.
//
// RaidDeployController is the ONLY writer (Rally toggle → tap sets Point); it also
// clears Point on scene teardown so a stale rally can't leak into the next raid.
// =============================================================================

using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// The single global rally point for deployed troops. Null when no rally is
    /// active (troops idle); set by <see cref="RaidDeployController"/>'s Rally tap.
    /// Read by <see cref="TroopController"/> in its idle branch (foe-in-range wins).
    /// </summary>
    public static class TroopRally
    {
        /// <summary>The world muster point, or null when no rally is set.</summary>
        public static Vector3? Point;

        /// <summary>Clear the rally (troops return to idle). Called on raid teardown.</summary>
        public static void Clear() => Point = null;
    }
}
