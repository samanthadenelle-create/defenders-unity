// =============================================================================
// RaidCasualtyPolicy — WO-1810. WHAT A LOST RAID COSTS, as one pure function.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// OWNER RULING 2026-09-16 ~20:05, verbatim:
//   "there is no cost to losing a raid" / "troops return a few injured which has
//    no cost or time" / "loss should lose troops and then rebuild" / "maybe lose
//    100 on fail, lose 60% on retreat" / "but any troop killed is dead so 60% of
//    whats left"
//
// WHAT THE BUILD DID BEFORE THIS FILE EXISTED, measured on the owner's Seeker
// (build 372984, 2026-09-16 19:59:01, logs/device/logcat-2000-tower-inverted.txt):
//     raid-end reconcile - deployed 10, survivors 7, wounded 3 (stars 0, recovery 1200s).
//     TroopRecoveryService: recovery advanced; 3 still wounded.
//     ... and one minute later the army screen read "Army is full. 10/10 slots used".
// Three troops DIED on the field, came home as "wounded", healed for FREE in 20
// minutes, and held their army slots the whole time. That is the defect: the loss
// cost nothing, so the raid loop had no downside and therefore no tension.
//
// THIS FILE IS PURE ON PURPOSE (no MonoBehaviour, no scene, no save, no catalog,
// no UnityEngine.Random): the whole point is that an oracle can assert the cost of
// a raid with nothing loaded, and that two runs of the same raid answer the same.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>
    /// WHICH raid exit settled the army. Declared by the exit itself, never inferred from
    /// the star count — a 0-star VICTORY and a retreat are indistinguishable by stars, and
    /// guessing between them is the difference between "your survivors came home" and
    /// "your whole warband is gone".
    /// </summary>
    public enum RaidExitOutcome
    {
        /// <summary>No exit declared itself. Resolves to <see cref="Failed"/> per the ruling,
        /// and says so in the trace rather than resolving silently.</summary>
        Undeclared = 0,
        /// <summary>The base was cleared (RaidVictoryController).</summary>
        Victory = 1,
        /// <summary>The PLAYER pressed Retreat. Not the clock, not the watchdog, not a death.</summary>
        Retreat = 2,
        /// <summary>Clock expiry, warband wiped, stranding watchdog, hero-death settlement.</summary>
        Failed = 3,
    }

    /// <summary>The settled cost of one raid. Every field is a COUNT, never a rate.</summary>
    public struct RaidCasualtyOutcome
    {
        /// <summary>Deployed bodies that did not survive the field. DEAD, on every outcome.</summary>
        public int Killed;
        /// <summary>Survivors the outcome's loss rate takes on top of <see cref="Killed"/>.</summary>
        public int LostByPolicy;
        /// <summary>Survivors that come home, healthy.</summary>
        public int Returned;
        /// <summary>Total troops removed from the roster: <see cref="Killed"/> + <see cref="LostByPolicy"/>.</summary>
        public int TotalLost;
        /// <summary>The percent actually applied to the survivors (after the outcome map + clamp).</summary>
        public int LossPctApplied;
    }

    /// <summary>
    /// WO-1810 — the ONE place the cost of a raid outcome is computed. Pure + static.
    /// </summary>
    public static class RaidCasualtyPolicy
    {
        // =====================================================================
        //  THE TWO RATES — REMOTE TUNABLE ROWS, not constants
        // =====================================================================
        //  The owner's own words are provisional ("MAYBE lose 100 on fail, lose
        //  60% on retreat"), so these are the definition of numbers that will
        //  move. Read through SpecFor(key) FIRST so an unregistered key answers
        //  the shipping default instead of RemoteTunables.Int's 0-for-unknown —
        //  a 0 here would silently restore the free loss this file exists to end.
        //
        //  ⚠ THESE DEFAULTS ARE THE OWNER'S RULING, NOT TODAY'S BEHAVIOUR. Today
        //  a raid loses 0% of its troops permanently. A row of 0 on both keys
        //  restores the old no-permanent-loss behaviour exactly — except for the
        //  KILLED, who stay dead on every outcome and are deliberately NOT on the
        //  rail ("any troop killed is dead" is a ruling, not a dial).
        // =====================================================================

        /// <summary>Percent of the SURVIVORS a failed raid loses (clock expiry, wipe, watchdog, death settlement).</summary>
        public static int LossPctFail => Resolve(
            DeNelle.Core.Ops.RemoteTunables.KeyRaidLossPctFail,
            DeNelle.Core.Ops.RemoteTunables.RaidLossPctFailDefault);

        /// <summary>Percent of the SURVIVORS the player's own retreat loses.</summary>
        public static int LossPctRetreat => Resolve(
            DeNelle.Core.Ops.RemoteTunables.KeyRaidLossPctRetreat,
            DeNelle.Core.Ops.RemoteTunables.RaidLossPctRetreatDefault);

        private static int Resolve(string key, int shipped)
        {
            int v = shipped;
            DeNelle.Core.Diagnostics.Guard.Try("Raid", "resolve raid casualty rate " + key, () =>
            {
                if (DeNelle.Core.Ops.RemoteTunables.SpecFor(key) != null)
                    v = DeNelle.Core.Ops.RemoteTunables.Int(key);
            });
            return Clamp01Pct(v);
        }

        /// <summary>A loss rate is a percent of a headcount: anything outside 0..100 is a typo, never a policy.</summary>
        public static int Clamp01Pct(int pct) => pct < 0 ? 0 : (pct > 100 ? 100 : pct);

        /// <summary>The loss percent this outcome charges the survivors. Victory charges nothing.</summary>
        public static int LossPctFor(RaidExitOutcome outcome, int failPct, int retreatPct)
        {
            switch (outcome)
            {
                case RaidExitOutcome.Victory: return 0;
                case RaidExitOutcome.Retreat: return Clamp01Pct(retreatPct);
                // Undeclared resolves to the FAIL rate per the ruling (the caller Warns).
                default:                      return Clamp01Pct(failPct);
            }
        }

        /// <summary>
        /// HOW MANY of <paramref name="survivors"/> a <paramref name="pct"/> loss takes.
        ///
        /// <para><b>INTEGER NEAREST, WITH A FLOOR OF ONE.</b> <c>(survivors * pct + 50) / 100</c> —
        /// no floats, no banker's rounding, and deliberately NOT <c>ceil</c>: at 60%,
        /// <c>ceil(2 * 0.6) = 2</c> would take <b>100%</b> of two survivors and
        /// <c>ceil(7 * 0.6) = 5</c> is <b>71%</b>, neither of which is the ruling. The floor of one
        /// is the ruling's "at least one survivor of a non-empty remainder is lost when 60% rounds
        /// below 1", so a lone survivor of a retreat is never a free save.</para>
        ///
        /// <para>At 60% the table reads 1→1, 2→1, 3→2, 5→3, 7→4, 10→6, 0→0. A pct of 0 takes
        /// nothing at all (the floor only applies while the rate is non-zero), and 100 takes
        /// everyone.</para>
        /// </summary>
        public static int LostOf(int survivors, int pct)
        {
            if (survivors <= 0) return 0;
            pct = Clamp01Pct(pct);
            if (pct <= 0) return 0;
            int lost = (survivors * pct + 50) / 100;   // integer NEAREST
            if (lost < 1) lost = 1;                    // a taxed remainder always pays one
            if (lost > survivors) lost = survivors;
            return lost;
        }

        /// <summary>
        /// The settled cost of one raid, from the two counts the deploy ledger already has.
        /// Negative / inconsistent inputs are clamped rather than thrown on: this runs on a raid
        /// exit, and a bad number must never be the reason a player cannot leave a raid.
        /// </summary>
        public static RaidCasualtyOutcome Decide(int deployed, int survivors, RaidExitOutcome outcome,
                                                 int failPct, int retreatPct)
        {
            if (deployed < 0) deployed = 0;
            if (survivors < 0) survivors = 0;
            if (survivors > deployed) survivors = deployed;

            int pct = LossPctFor(outcome, failPct, retreatPct);
            int lost = LostOf(survivors, pct);

            return new RaidCasualtyOutcome
            {
                Killed = deployed - survivors,
                LostByPolicy = lost,
                Returned = survivors - lost,
                TotalLost = (deployed - survivors) + lost,
                LossPctApplied = pct,
            };
        }

        /// <summary>The live-rate overload: the two rows above, resolved once per call.</summary>
        public static RaidCasualtyOutcome Decide(int deployed, int survivors, RaidExitOutcome outcome)
            => Decide(deployed, survivors, outcome, LossPctFail, LossPctRetreat);

        /// <summary>
        /// WHICH survivors the policy takes — <b>ROOKIES FIRST, DETERMINISTICALLY</b>.
        ///
        /// <para>Ordered by <see cref="PlayerTroop.VeterancyRank"/> ascending, then by
        /// <see cref="PlayerTroop.Id"/> ordinal as the tiebreak. Two reasons, both load-bearing:
        /// a veterancy rank is EARNED (granted only by <c>GrantVeterancy</c> on a 3-star clear),
        /// so the policy must not spend it on a coin flip; and an oracle has to get the same answer
        /// twice, which <c>UnityEngine.Random</c> could never give it.</para>
        ///
        /// <para>Takes the survivor ids and the roster, returns the ids to REMOVE (never mutates).
        /// Ids not present in the roster are skipped silently — they are already gone.</para>
        /// </summary>
        public static List<string> PickLostSurvivors(ArmyStorage army, IList<string> survivorIds, int count)
        {
            var picked = new List<string>();
            if (army == null || survivorIds == null || count <= 0) return picked;

            // Resolve survivor ids to the roster entries that still exist, keeping the
            // veterancy each one carries so the sort below can protect it.
            var live = new List<PlayerTroop>();
            for (int i = 0; i < survivorIds.Count; i++)
            {
                string id = survivorIds[i];
                if (string.IsNullOrEmpty(id)) continue;
                var t = Find(army, id);
                if (t != null) live.Add(t);
            }

            live.Sort((a, b) =>
            {
                int byRank = a.VeterancyRank.CompareTo(b.VeterancyRank);
                if (byRank != 0) return byRank;
                return string.CompareOrdinal(a.Id, b.Id);
            });

            for (int i = 0; i < live.Count && picked.Count < count; i++)
                picked.Add(live[i].Id);
            return picked;
        }

        private static PlayerTroop Find(ArmyStorage army, string id)
        {
            if (army == null || army.Owned == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < army.Owned.Count; i++)
            {
                var t = army.Owned[i];
                if (t != null && string.Equals(t.Id, id, System.StringComparison.Ordinal)) return t;
            }
            return null;
        }

        /// <summary>The player-facing word for an outcome, used in the trace and nowhere else.</summary>
        public static string Word(RaidExitOutcome outcome)
        {
            switch (outcome)
            {
                case RaidExitOutcome.Victory:    return "victory";
                case RaidExitOutcome.Retreat:    return "retreat";
                case RaidExitOutcome.Failed:     return "fail";
                default:                         return "fail(undeclared)";
            }
        }
    }
}
