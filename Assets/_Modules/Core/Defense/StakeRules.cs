// =============================================================================
// StakeRules -- THE LOSS-STAKES RULING (WO-1026, owner ruling of 2026-08-27).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Defense
//
// THE RULING, IN ONE LINE:
//        BANK THEFT REPLACES COLLECTOR LOOTING. A SIEGE BILLS ONCE PER ATTACK.
//
//    A siege takes exactly three things and nothing else:
//        1. structural damage
//        2. a repair bill  (WallRepairController, ceil(buildCost x damageFraction))
//        3. theft of a PERCENTAGE of UNPROTECTED bank resources, under a
//           PROTECTED FLOOR and a PER-ATTACK CAP
//
//        LOOTABLE      Wood, Iron, Stone, Coins
//        UNTOUCHABLE   Crystals, SKR, purchased goods, equipped gear
//
// ============================================================================
//  ! "STONE" IS THE BALANCE INTERNALLY NAMED `Food`. THIS IS THE TRAP.
// ----------------------------------------------------------------------------
//    Owner verbatim: "food was depreicated and is stone."
//    BankResource has NO Stone member -- it is Wood, Iron, Food, Crystals, Coins.
//    The HUD labels GameState.Resources.Stone as "Stone", TownBankCapacity.WordOf
//    (BankResource.Stone) literally returns "stone", and WO-1212 confirmed that slot
//    is the LIVE authority (the field actually NAMED Stone was dead code and has
//    been retired). So BankResource.Stone IS Stone.
//    DO NOT rename it -- it is a live SAVE AND WIRE key. DO NOT add a Stone member.
//    DO NOT read the name and conclude Stone is unimplemented, or that Food is some
//    SEPARATE lootable resource: that misreading is exactly how a siege would either
//    take the wrong balance or take one balance twice.
//    "Gold" in the ruling is BankResource.Coins (GameState.Resources.Coins).
// ============================================================================
//
// ============================================================================
//  ! WHAT THIS FILE LOOKED LIKE BEFORE, AND WHY THE HISTORY IS WRITTEN DOWN
// ----------------------------------------------------------------------------
//  The stakes ruling has now moved THREE times, and every seat after this one will
//  find superseded prose somewhere in the tree. The order of events, so nobody
//  mistakes a mid-exchange snapshot for the ruling:
//
//   * 2026-08-21 -- flat 15%-of-banked theft with a 20%-of-capacity floor.
//     SUPERSEDED. Its two numbers belong to a deleted system and MUST NOT be reused
//     as defaults for this one.
//   * 2026-08-22 (WO-1139) -- "COLLECTOR LOOTING ONLY. NO BANK THEFT." Shipped, and
//     it DELETED the bank arithmetic from this file on purpose.
//     SUPERSEDED 2026-08-27.
//   * 2026-08-27 (LIVE) -- BANK THEFT REPLACES COLLECTOR LOOTING. Collector looting
//     is REMOVED (ResourceCollector no longer takes anything when it breaks), so the
//     double-charge the 08-22 ruling feared is closed BY REMOVAL rather than by
//     abstinence: there is now exactly ONE theft in the game and it is this one.
//
//  WO-1139's oracle (SiegeLossStakesRegression) was RE-POINTED to this rule, never
//  deleted -- a green oracle going red on a ruling change is the oracle doing its job.
// ============================================================================
//
//  ONE BILL, BY CONSTRUCTION -- read this before adding any second take.
//    The 08-22 header was right about the failure mode and it is worth keeping: two
//    authorities for one concept is how a player gets charged twice for one siege.
//    The answer is not "be careful", it is that a collector no longer removes anything
//    from its own pending (see ResourceCollector.OnSiegeDestroyed) and this file is
//    the only place a siege loss is computed. If a second pool is ever added, the
//    first one must be REMOVED in the same change.
//
//  CRYSTALS ARE NEVER TAKEN -- AND THE EXEMPTION IS STRUCTURAL, NOT REMEMBERED.
//    A player cannot distinguish HARVESTED crystals from PURCHASED ones -- same
//    wallet -- so any crystal loss reads as losing bought currency, which turns a
//    gameplay loss into a refund request and a one-star review on a LIVE published
//    title. IsLootable is the single gate, Add is the only writer, and Add routes a
//    non-lootable bucket to nowhere. There is no expression in this file in which a
//    crystal could be taken, and SiegeUntouchableRegression fails the gate if that
//    ever stops being true.
//
//  WHAT IS NEVER LOST, and is therefore ABSENT from this file entirely:
//    building downgrades, destroyed permanent progress, stars, cleared-camp progress,
//    SKR, purchased goods, equipped gear. There is no code path here that could
//    express any of them.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Economy;

namespace DeNelle.Core.Defense
{
    /// <summary>
    /// One resource's standing at the moment a siege settles: what the bank holds, and what it
    /// could hold. Passed IN rather than read from the wallet so the arithmetic below stays a pure
    /// function -- the oracle can drive it against a hand-authored table with no scene, no save
    /// service and no economy service, which is what makes the numbers provable.
    /// </summary>
    public struct BankStanding
    {
        /// <summary>Which bucket. Only <see cref="StakeRules.IsLootable"/> buckets can produce a take.</summary>
        public BankResource Resource;

        /// <summary>What the bank currently holds. Negative is treated as zero.</summary>
        public int Banked;

        /// <summary>
        /// The town-bank ceiling for this resource (<c>TownBankCapacity.MaxOf</c>).
        /// <para><see cref="StakeRules.UncappedCapacity"/> (int.MaxValue) for a resource that has no
        /// ceiling by design -- Coins. Zero or negative means the capacity could not be read, and a
        /// capped resource with an UNKNOWN capacity loses NOTHING: failing open, in the player's
        /// favour, is the only safe direction when the number that defines the floor is missing.</para>
        /// </summary>
        public int Capacity;
    }

    /// <summary>
    /// The loss-stakes ruling as pure arithmetic: which buckets a siege may take from, how much
    /// of each is protected, how much one attack may ever carry off, and the one writer that fills
    /// the ledger. <see cref="DeNelle.Village.DefenseReportBuilder"/> is the only production caller.
    /// </summary>
    public static class StakeRules
    {
        /// <summary>
        /// The id stamped on every ledger this ruling produces. An old report keeps the id of the
        /// ruling that WROTE it (<see cref="StakesLedger.InterimRuleId"/> for pre-stakes records,
        /// <see cref="CollectorLootRuleId"/> for WO-1139 ones), so a report is always
        /// self-describing about which ruling produced its numbers and a newer build can never
        /// mis-read an older record as "they lost nothing that day".
        /// </summary>
        public const string RuleId = "stakes.banktheft.floorcap.wo1026";

        /// <summary>
        /// The SUPERSEDED WO-1139 rule id (collector looting only, no bank theft). Records written
        /// between 2026-08-22 and 2026-08-27 carry it. Kept as a named constant so a reader can
        /// recognise one rather than guess, and so a test can assert the ids are DISTINCT -- if the
        /// two ever collapsed into one string, an old report would start claiming it was written
        /// under a ruling that did not exist yet.
        /// </summary>
        public const string CollectorLootRuleId = "stakes.collectorloot.wo1139";

        /// <summary>The capacity value that means "this resource has no ceiling by design"
        /// (Coins -- TownBankCapacity's UncappableResources, owner ruling 2026-08-04).</summary>
        public const int UncappedCapacity = int.MaxValue;

        // =====================================================================
        //  WHICH BUCKETS A SIEGE MAY EVER TAKE FROM
        // =====================================================================

        /// <summary>
        /// THE UNTOUCHABLE LIST, as one expression.
        ///
        /// <para>LOOTABLE: Wood, Iron, Food (which IS Stone -- see the file header) and Coins
        /// (which IS Gold). These are EARNED balances.</para>
        ///
        /// <para>NEVER LOOTABLE: Crystals, on any outcome, from any source, at any percentage,
        /// under any cap. SKR, purchased goods and equipped gear are not even bank buckets -- they
        /// have no representation anywhere in this file, which is the strongest form of the
        /// exemption available.</para>
        ///
        /// <para>This is ONE expression rather than a habit at N call sites precisely so it cannot
        /// be forgotten at the N+1th.</para>
        /// </summary>
        public static bool IsLootable(BankResource resource)
        {
            return resource == BankResource.Wood
                || resource == BankResource.Iron
                || resource == BankResource.Stone     // "Stone" player-facing -- live save key
                || resource == BankResource.Coins;   // "Gold" player-facing
        }

        // =====================================================================
        //  THE TWO BOUNDS -- pure functions, hand-checkable
        // =====================================================================

        /// <summary>
        /// THE PROTECTED FLOOR: the amount of <paramref name="standing"/> that can never be taken,
        /// however many sieges land. A player sitting at or below the floor loses NOTHING, so the
        /// mechanic never kicks a player who is already down.
        ///
        /// <para>Capped resources (wood / iron / stone) use a FRACTION OF CAPACITY, because
        /// containers climb to six levels (2000 -> 34000) and a flat floor would mean two different
        /// games at the two ends of that curve. Coins are uncapped by design, so they use the flat
        /// authored floor instead.</para>
        ///
        /// <para>An UNKNOWN capacity on a capped resource returns <see cref="int.MaxValue"/> -- a
        /// floor above any conceivable balance, so nothing can be taken. Failing open is the only
        /// safe direction when the number that defines the floor could not be read.</para>
        ///
        /// <para>OWNER-PENDING: the fractions come from <see cref="SiegeStakesBalance"/> and are
        /// provisional placeholders awaiting a ruling.</para>
        /// </summary>
        public static int ProtectedFloor(BankStanding standing)
        {
            if (!IsLootable(standing.Resource)) return int.MaxValue;   // nothing to floor: nothing is takeable

            if (IsUncapped(standing)) return SiegeStakesBalance.CoinsProtectedFloor;

            if (standing.Capacity <= 0) return int.MaxValue;           // unknown ceiling -> take nothing

            double floor = standing.Capacity * (double)SiegeStakesBalance.ProtectedFloorFractionOfCapacity;
            return RoundHalfUp(floor);
        }

        /// <summary>
        /// THE PER-ATTACK CAP: the most a SINGLE attack may ever carry off, whatever the balance.
        /// This is what stops a very full store turning one bad night into a catastrophe, and it is
        /// the second bound the 2026-08-26 ruling requires.
        ///
        /// <para>Capped resources use a fraction of capacity; coins use the flat authored cap.
        /// An unknown capacity returns 0 -- no cap headroom, so no take.</para>
        ///
        /// <para>OWNER-PENDING: see <see cref="SiegeStakesBalance"/>.</para>
        /// </summary>
        public static int CapPerAttack(BankStanding standing)
        {
            if (!IsLootable(standing.Resource)) return 0;

            if (IsUncapped(standing)) return SiegeStakesBalance.CoinsPerAttackCap;

            if (standing.Capacity <= 0) return 0;                      // unknown ceiling -> take nothing

            double cap = standing.Capacity * (double)SiegeStakesBalance.PerAttackCapFractionOfCapacity;
            return RoundHalfUp(cap);
        }

        /// <summary>
        /// The steal fraction for an outcome.
        ///
        /// <para>A HELD defence takes NOTHING, and that is STRUCTURAL rather than a tuning knob --
        /// there is deliberately no authored field for it. If holding the line could still cost the
        /// player resources, the report's whole "your east wall fell first" story has nothing riding
        /// on it and the mechanic stops teaching anything.</para>
        ///
        /// <para>A BREACH costs less than an OVERRUN so that partial success is worth something.
        /// OWNER-PENDING: both fractions are provisional (see <see cref="SiegeStakesBalance"/>).</para>
        /// </summary>
        public static float StealFractionFor(DefenseOutcome outcome)
        {
            switch (outcome)
            {
                case DefenseOutcome.Breached: return SiegeStakesBalance.BreachedStealFraction;
                case DefenseOutcome.Overrun:  return SiegeStakesBalance.OverrunStealFraction;
                default:                      return 0f;   // Held -- structural, never a knob
            }
        }

        /// <summary>
        /// THE ONE PIECE OF THEFT ARITHMETIC IN THE GAME.
        ///
        /// <code>
        ///   unprotected = max(0, banked - ProtectedFloor)
        ///   raw         = floor(unprotected * StealFractionFor(outcome))
        ///   take        = min(raw, CapPerAttack, unprotected)
        /// </code>
        ///
        /// <para>Rounds DOWN, in the player's favour, at every step. Returns 0 for a non-lootable
        /// bucket, a held defence, a balance at or under the floor, or a capacity that could not be
        /// read -- and it can never return more than exists.</para>
        /// </summary>
        public static int TakeFrom(BankStanding standing, DefenseOutcome outcome)
        {
            if (!IsLootable(standing.Resource)) return 0;

            int banked = standing.Banked > 0 ? standing.Banked : 0;
            if (banked <= 0) return 0;

            float fraction = StealFractionFor(outcome);
            if (fraction <= 0f) return 0;

            int floor = ProtectedFloor(standing);
            if (floor >= banked) return 0;              // at or under the floor: untouchable

            int unprotected = banked - floor;
            int raw = (int)System.Math.Floor(unprotected * (double)fraction);
            if (raw <= 0) return 0;

            int cap = CapPerAttack(standing);
            int take = raw < cap ? raw : cap;
            if (take > unprotected) take = unprotected;
            return take > 0 ? take : 0;
        }

        /// <summary>
        /// Builds the ledger for one siege from the bank's standing at settle time. The ONLY
        /// producer of a non-empty stakes ledger.
        ///
        /// <para>It COMPUTES, it does not TAKE. <c>DefenseReportBuilder.ApplyStakes</c> performs the
        /// single debit, of exactly these numbers -- so what the player is TOLD they lost and what
        /// the wallet ACTUALLY lost are one value read from one place, never two computations that
        /// happen to agree today.</para>
        /// </summary>
        public static StakesLedger Build(DefenseOutcome outcome, IList<BankStanding> standings)
        {
            var ledger = Empty();
            if (standings == null) return ledger;

            for (int i = 0; i < standings.Count; i++)
            {
                var s = standings[i];
                int take = TakeFrom(s, outcome);
                if (take > 0) Add(ledger, s.Resource, take);
            }

            return ledger;
        }

        // =====================================================================
        //  THE ONE WRITER
        // =====================================================================

        /// <summary>
        /// THE ONLY WRITER of a stakes bucket. Adds <paramref name="amount"/> of
        /// <paramref name="resource"/> to <paramref name="ledger"/>, and DROPS it when the bucket
        /// is not lootable.
        ///
        /// <para>Negative and zero amounts are ignored: a "loss" that gives resources back is not
        /// a stake, it is a bug, and it must never be able to reach a ledger the player reads.</para>
        /// </summary>
        /// <returns>True when the amount was actually recorded.</returns>
        public static bool Add(StakesLedger ledger, BankResource resource, int amount)
        {
            if (ledger == null || amount <= 0) return false;
            if (!IsLootable(resource)) return false;   // crystals: no bucket, no expression

            switch (resource)
            {
                case BankResource.Wood:  ledger.Wood += amount;  return true;
                case BankResource.Iron:  ledger.Iron += amount;  return true;
                case BankResource.Stone:  ledger.Stone += amount;  return true;   // "Stone"
                case BankResource.Coins: ledger.Coins += amount; return true;   // "Gold"
                default: return false;
            }
        }

        /// <summary>
        /// A well-formed, all-zero ledger stamped with THIS ruling. The honest answer for a defence
        /// that held -- and the fallback direction when the report cannot be built at all: an
        /// all-zero ledger says "nothing was taken", which is safe to be wrong about in a way that
        /// an invented loss never is.
        /// </summary>
        public static StakesLedger Empty()
        {
            return new StakesLedger { StakesRuleId = RuleId };
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool IsUncapped(BankStanding standing)
        {
            return standing.Capacity == UncappedCapacity || !TownBankCapacity.IsCapped(standing.Resource);
        }

        /// <summary>Rounds a non-negative double half-up to an int, saturating rather than
        /// overflowing (an uncapped capacity is int.MaxValue and must not wrap).</summary>
        private static int RoundHalfUp(double value)
        {
            if (value <= 0d) return 0;
            if (value >= int.MaxValue) return int.MaxValue;
            return (int)System.Math.Floor(value + 0.5d);
        }
    }
}
