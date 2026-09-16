// =============================================================================
// EchoBonusCalculator -- the shared Echo specialization MATH (WO-738 SERVICE lane,
// WO-830 affinity match + pair synergies + hidden tri-synergy).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// PURE STATIC, null/absent-safe. The ONE place the specialization curve lives so
// EchoService (harvest income + dump split + silo cadence) and the roster/picker UI
// all read the SAME numbers -- no per-system math scattered. Reads:
//   - EchoAssignments (LaneOf / ResourceTokenOf / LevelOf per owned echo index)
//   - EchoBalanceCatalog (the owner-tunable knobs: MaxLevel, PreferredLaneMatchBonus,
//     BaseContributionPerEcho, SixSetBonusGlobalHarvest, PerLevelBonus, BaseRateFor,
//     CrossBonuses, HiddenTriSynergyBonus)
//   - EchoRosterCatalog (the fixed identity table: PreferredLane, Affinity, Id)
//
// WO-830 MATCH LAW: the "preferred match" bonus fires when the echo's ASSIGNED harvest
// resource equals its AFFINITY (player-picked; affinity is a bonus, never a lock).
// PAIR SYNERGIES (disclosed): a crossBonuses pair "runs" when BOTH members are owned
// AND harvest-assigned to their own affinity resources; each running pair adds its
// `bonus` to the global harvest spec sum. HIDDEN TRI-SYNERGY (UNDISCLOSED): when ALL
// pairs run at once, `hiddenTriSynergyBonus` is added to the APPLIED path ONLY --
// it must NEVER appear in ReadoutFor / any displayed "+%" (the whole point).
// The per-echo ReadoutFor.BonusPct stays base+match+level (disclosed per-echo terms);
// the pair bonus is disclosed through SynergyFor (its own card line), applied ONCE
// per pair globally so no per-echo number double-counts it.
//
// SAFETY: no GameState / no service => neutral (multipliers 1.0, weights fall back to
// even Wood/Iron/Food). Nothing here mutates state; Recompute() is the ONLY writer and
// it only pushes into the Core EchoLaneBonuses holder (Village writes, hosts read).
//
// COMPOSITION LAW (no double-count): the WO-709 count-quadratic spine stays owned by
// EchoService.GlobalHarvestMultiplier (== EchoCount). AggregateHarvestMultiplier FOLDS
// that spine in ONCE and layers the specialization factor on top, so EchoService
// multiplies by AggregateHarvestMultiplier() INSTEAD OF GlobalHarvestMultiplier (never
// both). See EchoService.RatePerSecond.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>A small per-echo specialization readout for the roster/picker UI (WO-738).</summary>
    public struct EchoBonusReadout
    {
        /// <summary>The functional lane this echo is assigned to.</summary>
        public LaneType Lane;
        /// <summary>The echo's level (1..MaxLevel).</summary>
        public int Level;
        /// <summary>This echo's additive bonus to its assigned lane, as a PERCENT (e.g. 18f == +18%).
        /// Idle contributes nothing (0). Composed of the base-to-all floor + the affinity match
        /// bonus (when the assigned resource matches) + the per-level increment. Deliberately
        /// EXCLUDES the pair synergies (disclosed separately via SynergyFor, applied once per
        /// pair) and the hidden tri-synergy (never displayed anywhere -- WO-830 Sec.3d).</summary>
        public float BonusPct;
        /// <summary>True when this echo's assigned harvest resource matches its affinity
        /// (earns the match bonus). For legacy non-harvest lanes: lane == PreferredLane.</summary>
        public bool PreferredMatch;
    }

    /// <summary>WO-830: one echo's pair-synergy status for the card UI (DISCLOSED). The hidden
    /// tri-synergy is deliberately NOT represented here or anywhere player-facing.</summary>
    public struct EchoSynergyReadout
    {
        /// <summary>True when this echo is a member of a defined synergy pair.</summary>
        public bool HasPair;
        /// <summary>The pair's display name ("Provisions"/"Forge"/"Fortune"). "" when none.</summary>
        public string PairName;
        /// <summary>The partner echo's display name. "" when none.</summary>
        public string PartnerName;
        /// <summary>The partner's affinity resource label ("Food") -- the hint for activating.</summary>
        public string PartnerResourceLabel;
        /// <summary>True when the pair is RUNNING (both owned + both on their affinity resources).</summary>
        public bool Active;
        /// <summary>The pair's disclosed bonus as a PERCENT (e.g. 10f == +10%).</summary>
        public float BonusPct;
    }

    /// <summary>
    /// The shared Echo specialization math (WO-738/830). Pure functions over the persisted
    /// assignment + the tunable balance + the fixed roster identity. See file header for
    /// the composition law (spine folded in ONCE by AggregateHarvestMultiplier).
    /// </summary>
    public static class EchoBonusCalculator
    {
        // WO-1474: the three per-hour rates USED to be private consts here (3600 / 900 / 4),
        // which put them out of reach of the WO-1331 remote-retune seam. They now live in
        // echoes-balance.json (EchoBalanceCatalog.CommonResourcePerHour / GoldPerHour /
        // CrystalPerHour), authored at the SAME values, so the split is unchanged by the move.
        // Hidden tri-synergy activation edge (trace on transition, not per frame --
        // AggregateHarvestMultiplier runs every tick). Internal-only observability.
        private static bool s_triWasActive;

        // =====================================================================
        //  HARVEST -- the applied total multiplier (folds the count spine).
        // =====================================================================

        /// <summary>
        /// The TOTAL multiplier applied to harvest income -- the value EchoService.RatePerSecond
        /// multiplies by IN PLACE OF GlobalHarvestMultiplier (do not apply both).
        ///
        /// FORMULA (owner-tunable via echoes-balance.json):
        ///   AggregateHarvestMultiplier
        ///     = EchoCount                                            // the WO-709 count-quadratic SPINE
        ///       x ( 1
        ///           + Sum over echoes ASSIGNED to Harvest of
        ///               [ BaseContributionPerEcho                    // "no echo wasted" floor
        ///                 + (assigned resource == affinity ? PreferredLaneMatchBonus : 0)  // WO-830 match
        ///                 + PerLevelBonus * (level - 1) ]            // level growth
        ///           + Sum of RUNNING pair-synergy bonuses            // WO-830 disclosed pairs
        ///           + (all 6 owned ? SixSetBonusGlobalHarvest : 0)   // the 6-of-6 set bonus
        ///           + (ALL pairs running ? HiddenTriSynergyBonus : 0) ) // WO-830 HIDDEN (applied only)
        ///
        /// Neutral (1.0) when no GameState/service exists. The hidden tri term exists ONLY here
        /// (the applied path); every displayed number excludes it (WO-830 Sec.3d hard rule).
        /// </summary>
        public static float AggregateHarvestMultiplier()
        {
            int count = OwnedCount();
            if (count <= 0) return 1f;   // no service -> neutral

            float specSum = DisclosedHarvestBonusFraction(count);

            // WO-830 Sec.3d: the UNDISCLOSED tri-synergy -- applied, never displayed.
            bool triActive = HiddenTriSynergyActive(count);
            if (triActive) specSum += EchoBalanceCatalog.HiddenTriSynergyBonus;
            if (triActive != s_triWasActive)
            {
                s_triWasActive = triActive;
                // Internal-only observability (headless verify) -- no player-facing surface.
                FlowTrace.Step("Echo", triActive
                    ? $"hidden tri-synergy ACTIVE (+{EchoBalanceCatalog.HiddenTriSynergyBonus:0.###} applied, undisclosed)"
                    : "hidden tri-synergy inactive");
            }

            return count * (1f + specSum);
        }

        /// <summary>The additive harvest bonus safe to show to players, as a percent.
        /// Includes specialization, running pairs, and the six-Echo set; deliberately
        /// excludes the hidden tri-synergy that exists only in the applied aggregate.</summary>
        public static float DisclosedHarvestBonusPercent()
        {
            int count = OwnedCount();
            float percent = count > 0 ? DisclosedHarvestBonusFraction(count) * 100f : 0f;
            FlowTrace.Once("Echo", "disclosed-harvest-bonus-readout",
                "EchoBonusCalculator supplied the additive player readout; hidden tri-synergy excluded.");
            return Mathf.Max(0f, percent);
        }

        private static float DisclosedHarvestBonusFraction(int count)
        {
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                if (LaneTypeOf(EchoAssignments.LaneOf(i)) != LaneType.Harvest) continue;
                sum += LaneContribution(i, LaneType.Harvest);
            }

            sum += PairBonusSum(count);
            if (AllOwned(count)) sum += EchoBalanceCatalog.SixSetBonusGlobalHarvest;
            return sum;
        }

        /// <summary>
        /// WO-830: per-TARGET split WEIGHTS for DumpSilos across all five harvest targets
        /// (Wood/Iron/Food/Gold/Crystals). Each Harvest-assigned echo contributes
        ///
        ///   HarvestRatePerHour(assignedTarget, level) x EchoBalanceCatalog.BaseRateFor(echoId)
        ///
        /// to its ASSIGNED resource (player-picked -- EchoAssignments.ResourceTokenOf; a v33
        /// generic "harvest" token defaults to the echo's affinity on read). Non-Harvest echoes
        /// contribute nothing.
        ///
        /// WO-1474 corrected this header AND the code it described. The old text claimed the
        /// weight was "BaseRateFor(id) * level"; the code multiplied NEITHER factor in -- it
        /// returned the raw rate-class per-hour number, so the authored perEchoBaseRate rows
        /// were dead and the level term came only from HarvestRatePerHour's own +10%/level.
        /// BaseRateFor is now wired in (it is the AUTHORITY for the per-echo weight; the rate
        /// classes are the AUTHORITY for the per-resource cadence), which is what makes the
        /// WO-830 Sec.3b crystals monetization guard actually run.
        ///
        /// There is deliberately NO even-split fallback when no echo is assigned (the old
        /// header claimed one): every weight stays 0 and callers must handle a zero total --
        /// Gold/Crystals must never flow without an explicit assignment.
        /// </summary>
        public static Dictionary<HarvestTarget, float> HarvestTargetWeights()
        {
            var weights = new Dictionary<HarvestTarget, float>
            {
                { HarvestTarget.Wood, 0f },
                { HarvestTarget.Iron, 0f },
                { HarvestTarget.Stone, 0f },
                { HarvestTarget.Gold, 0f },
                { HarvestTarget.Crystals, 0f },
            };

            int count = OwnedCount();
            for (int i = 0; i < count; i++)
            {
                if (LaneTypeOf(EchoAssignments.LaneOf(i)) != LaneType.Harvest) continue;
                if (!EchoAssignments.TryTargetOf(i, out var target)) continue;

                var entry = EchoRosterCatalog.ByIndex(i);
                if (entry == null) continue;

                int level = EchoAssignments.LevelOf(i);
                // WO-1474: the authored per-echo weight finally applies. `entry` was resolved
                // and then thrown away here before today, which is exactly why it was dead.
                float w = HarvestRatePerHour(target, level)
                          * Mathf.Max(0f, EchoBalanceCatalog.BaseRateFor(entry.Id));
                if (w <= 0f) continue;

                weights[target] += w;
            }

            return weights;
        }

        /// <summary>Actual workforce production per hour. Ordinary materials use the
        /// player-readable 5-per-5-seconds cadence. Gold is slower; crystals are an
        /// intentionally tiny final-Echo drip fixed at 1 every 15 minutes.</summary>
        public static float HarvestRatePerHour(HarvestTarget target, int level)
        {
            int maxLevel = Mathf.Max(1, EchoBalanceCatalog.MaxLevel);
            int clamped = Mathf.Clamp(level, 1, maxLevel);
            float progress = maxLevel <= 1 ? 0f : (clamped - 1f) / (maxLevel - 1f);
            switch (target)
            {
                case HarvestTarget.Crystals:
                    return EchoBalanceCatalog.CrystalPerHour;
                case HarvestTarget.Gold:
                    return EchoBalanceCatalog.GoldPerHour * (1f + 0.10f * (clamped - 1));
                default:
                    return EchoBalanceCatalog.CommonResourcePerHour * (1f + 0.10f * (clamped - 1));
            }
        }

        public static float TotalHarvestRatePerHour()
        {
            var weights = HarvestTargetWeights();
            float total = 0f;
            foreach (float rate in weights.Values) total += Mathf.Max(0f, rate);
            return total;
        }

        // =====================================================================
        //  WO-830 pair synergies (disclosed) + hidden tri (applied-only).
        // =====================================================================

        /// <summary>True when the crossBonuses pair at <paramref name="def"/> is RUNNING:
        /// both member echoes owned AND harvest-assigned to their own affinity resources.</summary>
        private static bool PairActive(EchoCrossBonusDef def, int ownedCount)
        {
            return MemberActive(def != null ? def.A : null, ownedCount)
                && MemberActive(def != null ? def.B : null, ownedCount);
        }

        /// <summary>One pair member's activation: owned + Harvest lane + assigned resource == affinity.</summary>
        private static bool MemberActive(string echoId, int ownedCount)
        {
            if (string.IsNullOrEmpty(echoId)) return false;
            var entry = FindEntry(echoId);
            if (entry == null) return false;
            int index = entry.Order - 1;
            if (index < 0 || index >= ownedCount) return false;
            if (LaneTypeOf(EchoAssignments.LaneOf(index)) != LaneType.Harvest) return false;
            return EchoAssignments.ResourceTokenOf(index) == EchoRosterCatalog.TargetToken(entry.Affinity);
        }

        /// <summary>Sum of the DISCLOSED bonuses of all running pairs (additive spec-sum terms).</summary>
        private static float PairBonusSum(int ownedCount)
        {
            var defs = EchoBalanceCatalog.CrossBonuses;
            if (defs == null || defs.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < defs.Count; i++)
                if (PairActive(defs[i], ownedCount)) sum += Mathf.Max(0f, defs[i].Bonus);
            return sum;
        }

        /// <summary>WO-830 Sec.3d: true when EVERY defined pair is running at once (the secret
        /// condition). False when no pairs are defined. Internal -- never surfaced to a player.</summary>
        private static bool HiddenTriSynergyActive(int ownedCount)
        {
            var defs = EchoBalanceCatalog.CrossBonuses;
            if (defs == null || defs.Count == 0) return false;
            for (int i = 0; i < defs.Count; i++)
                if (!PairActive(defs[i], ownedCount)) return false;
            return true;
        }

        /// <summary>WO-830: the DISCLOSED pair-synergy status for one echo (card UI line).
        /// Carries the pair name, the partner + the partner's affinity resource (the hint),
        /// active state, and the disclosed bonus %. Never mentions the tri-synergy.</summary>
        public static EchoSynergyReadout SynergyFor(int echoIndex)
        {
            var ro = new EchoSynergyReadout
            {
                HasPair = false, PairName = "", PartnerName = "", PartnerResourceLabel = "",
                Active = false, BonusPct = 0f,
            };

            var entry = EchoRosterCatalog.ByIndex(echoIndex);
            if (entry == null) return ro;

            var defs = EchoBalanceCatalog.CrossBonuses;
            if (defs == null) return ro;

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null) continue;
                string partnerId = null;
                if (def.A == entry.Id) partnerId = def.B;
                else if (def.B == entry.Id) partnerId = def.A;
                if (partnerId == null) continue;

                var partner = FindEntry(partnerId);
                ro.HasPair = true;
                ro.PairName = def.Name ?? "";
                ro.PartnerName = partner != null ? partner.DisplayName : partnerId;
                ro.PartnerResourceLabel = partner != null ? EchoRosterCatalog.TargetLabel(partner.Affinity) : "";
                ro.Active = PairActive(def, OwnedCount());
                ro.BonusPct = Mathf.Max(0f, def.Bonus) * 100f;
                return ro;
            }
            return ro;
        }

        // =====================================================================
        //  WO-811 REPAIR -- the rate the repair consumer reads (single math source).
        // =====================================================================

        /// <summary>
        /// WO-811 rate, WO-1108 PASSIVE: structure-FRACTIONS of repair work per second across
        /// EVERY OWNED Echo (the value EchoRepairService accrues its work budget at -- this
        /// method is the ONE home of the repair rate math, per the single-math-source law
        /// this file exists for).
        ///
        /// WO-1108 (owner: "the number of pets that we have just passively takes towards
        /// healing"): repair is NO LONGER an assignable lane. The lane filter is GONE -- the
        /// roster COUNT drives mending, so an Echo repairs and harvests at the same time and
        /// no assignment can turn repair off. A stored legacy "repair:N" token read-migrates
        /// to the Echo's affinity harvest resource (EchoAssignments.NormalizeToken), so it
        /// neither disappears from this sum nor zeroes that Echo's yield.
        ///
        /// Per OWNED echo:
        ///   EchoBalanceCatalog.RepairFractionPerHour x (1 + LaneContribution)
        /// where LaneContribution is the SAME shared term every lane uses --
        /// BaseContributionPerEcho + PerLevelBonus x (level - 1). Level scaling therefore
        /// rides the one owner-tuned curve (WO-1108 D2 default: count x LEVEL, not count
        /// alone -- count-only would make Echo levels worthless for repair), and there is
        /// deliberately NO affinity match term: "Repairs" was REMOVED as an affinity
        /// (WO-830 owner ruling 2026-08-02 -- Maren harvests Crystals), no roster entry
        /// prefers the Repair lane, so PreferredMatches can never fire here.
        ///
        /// WO-1108 D3: because this now sums the WHOLE roster, `repairFractionPerHour` was
        /// re-tuned DOWN (2.0 -> 0.35) and MOVED INTO echoes-balance.json so the aggregate at
        /// a full 6-Echo roster lands near the old single-assigned-Echo felt rate instead of
        /// 6x-ing it. See the json _authoringNotes for the arithmetic.
        ///
        /// 0 when no Echo is owned / no GameState (=> the consumer accrues nothing -- the
        /// honest zero, never fake work).
        /// </summary>
        public static float RepairFractionsPerSecond()
        {
            int count = OwnedCount();
            if (count <= 0) return 0f;

            float perHour = 0f;
            for (int i = 0; i < count; i++)
            {
                perHour += Mathf.Max(0f, EchoBalanceCatalog.RepairFractionPerHour)
                         * (1f + LaneContribution(i, LaneType.Repair));
            }
            return perHour / 3600f;
        }

        // =====================================================================
        //  NON-HARVEST lanes -- passive multipliers stored on EchoLaneBonuses.
        // =====================================================================

        /// <summary>
        /// The passive multiplier for a Crafting/Defense/Exploration lane (WO-738):
        ///   1 + Sum over echoes ASSIGNED to that lane of
        ///       [ BaseContributionPerEcho + (preferred match ? PreferredLaneMatchBonus : 0)
        ///         + PerLevelBonus * (level - 1) ].
        /// Harvest/Idle return 1.0 here (Harvest is handled by AggregateHarvestMultiplier; Idle is
        /// a no-op). WO-830 note: these lanes are no longer pickable (Harvest-only picker), but any
        /// legacy-stored token still computes so an old save never silently loses a bonus.
        /// </summary>
        public static float LaneMultiplier(LaneType lane)
        {
            if (lane == LaneType.Idle) return 1f;

            int count = OwnedCount();
            if (count <= 0) return 1f;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                if (LaneTypeOf(EchoAssignments.LaneOf(i)) != lane) continue;
                sum += LaneContribution(i, lane);
            }
            return 1f + sum;
        }

        // =====================================================================
        //  UI readout.
        // =====================================================================

        /// <summary>A per-echo specialization readout (lane, level, bonus %, matched) for the
        /// roster/picker UI. BonusPct = base + match + level ONLY (see the struct doc --
        /// pair synergies disclose via <see cref="SynergyFor"/>; the hidden tri never shows).</summary>
        public static EchoBonusReadout ReadoutFor(int echoIndex)
        {
            var lane = LaneTypeOf(EchoAssignments.LaneOf(echoIndex));
            int level = EchoAssignments.LevelOf(echoIndex);
            bool match = PreferredMatches(echoIndex, lane);
            float bonus = lane == LaneType.Idle ? 0f : LaneContribution(echoIndex, lane);
            return new EchoBonusReadout
            {
                Lane = lane,
                Level = level,
                BonusPct = bonus * 100f,   // fraction -> percent for display ("+18%")
                PreferredMatch = match,
            };
        }

        // =====================================================================
        //  Recompute -- the ONLY writer; pushes passive lane mults into Core.
        // =====================================================================

        /// <summary>
        /// Recompute the three passive lane multipliers + the harvest total and push them into the
        /// Core <see cref="EchoLaneBonuses"/> holder (Village writes, hosts read). Idempotent -- safe
        /// to call on every assignment/count change event. HarvestBonusMult mirrors the applied
        /// AggregateHarvestMultiplier so a host reading the contract sees the same number EchoService
        /// applies (EchoService itself reads AggregateHarvestMultiplier() LIVE -- no double-apply).
        /// The mirror carries the hidden tri term (it is the APPLIED value); no UI reads it
        /// (verified: write-only stub + regression), so nothing player-facing discloses. [Flow:Echo].
        /// </summary>
        public static void Recompute()
        {
            EchoLaneBonuses.Reset();

            float harvest = AggregateHarvestMultiplier();
            float crafting = LaneMultiplier(LaneType.Crafting);
            float defense = LaneMultiplier(LaneType.Defense);
            float exploration = LaneMultiplier(LaneType.Exploration);

            EchoLaneBonuses.HarvestBonusMult = harvest;
            EchoLaneBonuses.CraftingMult = crafting;
            EchoLaneBonuses.DefenseMult = defense;
            EchoLaneBonuses.ExplorationMult = exploration;

            FlowTrace.Once("Echo", "bonus-recompute-first",
                "EchoBonusCalculator.Recompute: first pass -- passive lane multipliers populated onto EchoLaneBonuses (hosts read when they land).");
            FlowTrace.Step("Echo",
                $"Recompute: harvestx{harvest:0.###} (applied), craftx{crafting:0.###}, " +
                $"defx{defense:0.###}, explorationx{exploration:0.###} (owned {OwnedCount()}).");
        }

        // =====================================================================
        //  Internals.
        // =====================================================================

        /// <summary>The additive specialization contribution (FRACTION) of one echo to a given lane:
        /// BaseContributionPerEcho + (affinity/preferred match ? PreferredLaneMatchBonus : 0)
        /// + PerLevelBonus*(level-1). Pair + tri terms live in AggregateHarvestMultiplier only.</summary>
        private static float LaneContribution(int echoIndex, LaneType lane)
        {
            int level = EchoAssignments.LevelOf(echoIndex);
            float c = EchoBalanceCatalog.BaseContributionPerEcho;
            if (PreferredMatches(echoIndex, lane)) c += EchoBalanceCatalog.PreferredLaneMatchBonus;
            c += EchoBalanceCatalog.PerLevelBonus * Mathf.Max(0, level - 1);
            return c;
        }

        /// <summary>WO-830 match law: on the HARVEST lane the match is the echo's ASSIGNED
        /// resource equaling its AFFINITY (player pick lands on the calling). On a legacy
        /// non-harvest lane the pre-830 rule survives (lane == PreferredLane) so old stored
        /// tokens keep their exact bonus.</summary>
        private static bool PreferredMatches(int echoIndex, LaneType lane)
        {
            if (lane == LaneType.Idle) return false;
            var entry = EchoRosterCatalog.ByIndex(echoIndex);
            if (entry == null) return false;
            if (lane == LaneType.Harvest)
            {
                if (entry.PreferredLane != LaneType.Harvest) return false;
                return EchoAssignments.ResourceTokenOf(echoIndex) == EchoRosterCatalog.TargetToken(entry.Affinity);
            }
            return entry.PreferredLane == lane;
        }

        /// <summary>Find a roster entry by id (null when absent).</summary>
        private static EchoRosterEntry FindEntry(string echoId)
        {
            var all = EchoRosterCatalog.All;
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Id == echoId) return all[i];
            return null;
        }

        /// <summary>Owned echo count. 0 when no GameState/service exists (=> neutral bonuses).</summary>
        private static int OwnedCount()
        {
            if (EchoService.Instance != null) return EchoService.Instance.EchoCount;
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null) return 0;
            return Mathf.Max(1, svc.State.EchoCount);
        }

        /// <summary>True when the full canonical roster is owned (the 6-of-6 set bonus condition).</summary>
        private static bool AllOwned(int count)
        {
            return count >= EchoRosterCatalog.Count;
        }

        /// <summary>Map an EchoAssignments lane token to the LaneType enum (unknown/legacy -> Idle-safe).</summary>
        private static LaneType LaneTypeOf(string laneToken)
        {
            if (string.IsNullOrEmpty(laneToken)) return LaneType.Idle;
            switch (laneToken)
            {
                case EchoAssignments.LaneHarvest:     return LaneType.Harvest;
                case EchoAssignments.LaneCrafting:    return LaneType.Crafting;
                case EchoAssignments.LaneDefense:     return LaneType.Defense;
                case EchoAssignments.LaneExploration: return LaneType.Exploration;
                // WO-1108: "repair" is NO LONGER a storable lane -- NormalizeToken
                // read-migrates it to the Harvest lane, so no case can appear here.
                default:                              return LaneType.Idle;
            }
        }
    }
}
