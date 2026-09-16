// =============================================================================
// RaidScoringTests (EditMode) — locks the PURE V1 raid star + loot math
// (RaidScoring.ComputeStars / ComputeLoot, WO-771.6 teleport/deploy loop). No
// scene, no GameState, no catalog — just the deterministic-enough formulas.
// =============================================================================

using NUnit.Framework;
using DeNelle.Village;

namespace DeNelle.Tests.EditMode
{
    [TestFixture]
    public class RaidScoringTests
    {
        private const float Clock = 180f;

        // ── Stars from inputs — OWNER LADDER 2026-07-30 (the "premium" two-axis model)
        //    1 = just cleared · 2 = cleared with high survival OR under the clock
        //    3 = cleared with high survival AND under the clock.
        //    Sub-clear credit (>=50% razed = 1 star) is unchanged so a damaging retreat
        //    keeps its loot. `bossDestroyed` floors at 1, not 2: in V1 the boss is part of
        //    the garrison (bossDown == cleared), so its old 2-floor could only fire on a
        //    clear, where it short-circuited the ladder and made EVERY victory 3 stars.

        private const float HiSurv  = 1.00f;   // everyone walked off the field
        private const float LowSurv = 0.20f;   // it cost you

        [Test]
        public void Stars_Retreat_UnderHalf_Razed_IsZero()
        {
            Assert.AreEqual(0, RaidScoring.ComputeStars(false, false, 0.20f, 10f, Clock, HiSurv));
        }

        [Test]
        public void Stars_Retreat_HalfRazed_IsOne()
        {
            Assert.AreEqual(1, RaidScoring.ComputeStars(false, false, 0.50f, 10f, Clock, HiSurv));
        }

        [Test]
        public void Stars_BossDown_Partial_FloorsAtOne()
        {
            Assert.AreEqual(1, RaidScoring.ComputeStars(false, true, 0.60f, 10f, Clock, HiSurv));
        }

        [Test]
        public void Stars_Clear_SlowAndCostly_IsOne()
        {
            Assert.AreEqual(1, RaidScoring.ComputeStars(true, true, 1f, 240f, Clock, LowSurv));
        }

        [Test]
        public void Stars_Clear_UnderClockButCostly_IsTwo()
        {
            Assert.AreEqual(2, RaidScoring.ComputeStars(true, true, 1f, 120f, Clock, LowSurv));
        }

        [Test]
        public void Stars_Clear_SlowButHighSurvival_IsTwo()
        {
            Assert.AreEqual(2, RaidScoring.ComputeStars(true, true, 1f, 240f, Clock, HiSurv));
        }

        [Test]
        public void Stars_Clear_FastAndHighSurvival_IsThree()
        {
            Assert.AreEqual(3, RaidScoring.ComputeStars(true, true, 1f, 120f, Clock, HiSurv));
        }

        [Test]
        public void Stars_SurvivalThreshold_IsInclusive()
        {
            Assert.AreEqual(3, RaidScoring.ComputeStars(true, true, 1f, 120f, Clock,
                                                        RaidScoring.HighSurvivalPct));
            Assert.AreEqual(2, RaidScoring.ComputeStars(true, true, 1f, 120f, Clock,
                                                        RaidScoring.HighSurvivalPct - 0.01f));
        }

        [Test]
        public void Stars_ThreeIsUnreachableWithoutAClear()
        {
            for (int di = 0; di <= 10; di++)
            foreach (var boss in new[] { false, true })
            foreach (var t in new[] { 30f, 180f, 400f })
            foreach (var sv in new[] { 0f, 0.5f, RaidScoring.HighSurvivalPct, 1f })
                Assert.AreNotEqual(3, RaidScoring.ComputeStars(false, boss, di / 10f, t, Clock, sv),
                                   "3 stars must require clearing the base");
        }

        [Test]
        public void Stars_AlwaysWithinRange()
        {
            for (int di = 0; di <= 10; di++)
            {
                float d = di / 10f;
                foreach (var cleared in new[] { false, true })
                foreach (var boss in new[] { false, true })
                foreach (var t in new[] { 30f, 180f, 400f })
                foreach (var sv in new[] { 0f, 0.5f, RaidScoring.HighSurvivalPct, 1f })
                {
                    int s = RaidScoring.ComputeStars(cleared, boss, d, t, Clock, sv);
                    Assert.GreaterOrEqual(s, 0);
                    Assert.LessOrEqual(s, 3);
                }
            }
        }

        // ── Loot scales with stars + destruction ──────────────────────────────

        [Test]
        public void Loot_NothingRaided_PaysNothing()
        {
            var loot = RaidScoring.ComputeLoot(0, 0f, 40, 60, 15, 20);
            Assert.AreEqual(0, loot.Crystals);
            Assert.AreEqual(0, loot.Stone);
        }

        [Test]
        public void Loot_ScalesMonotonically_WithStarsAndDestruction()
        {
            var none = RaidScoring.ComputeLoot(0, 0f, 40, 60, 15, 20);
            var half = RaidScoring.ComputeLoot(1, 0.5f, 40, 60, 15, 20);
            var full = RaidScoring.ComputeLoot(3, 1f, 40, 60, 15, 20);

            Assert.Greater(half.Crystals, none.Crystals);
            Assert.Greater(full.Crystals, half.Crystals);
            Assert.Greater(half.Stone, none.Stone);
            Assert.Greater(full.Stone, half.Stone);
        }

        [Test]
        public void Loot_FullThreeStar_MatchesBasePlusPerStar()
        {
            // 100% destruction => full base; +3x per-star bonus.
            var full = RaidScoring.ComputeLoot(3, 1f, 40, 60, 15, 20);
            Assert.AreEqual(40 + 3 * 15, full.Crystals);   // 85
            Assert.AreEqual(60 + 3 * 20, full.Stone);        // 120
        }

        // ── THE MISSING ARROW: troops -> raids -> GOLD ────────────────────────
        // docs/PROGRAM_RAID_ECONOMY_2026-09-04.md section 1: "You currently have Gold ->
        // troops but not troops -> raids -> gold. That arrow has to exist."
        //
        // RED FIRST, and in two different ways:
        //   Loot_Gold_* cannot COMPILE against the previous tree - ComputeLoot had nine
        //   parameters and no coinsBase, so gold was structurally zero on every path.
        //   Loot_CampMultiplier_DoesNotReachCrystals DOES compile against it and fails on
        //   the numbers: the old code returned (25 + 10*3) * 1.5 = 83, this build 55.

        /// <summary>A 3-star Camp I clear pays the map's 2,200 gold; the 3-star rung is 100%.</summary>
        [Test]
        public void Loot_Gold_ThreeStar_PaysTheCampBase()
        {
            var loot = RaidScoring.ComputeLoot(3, 0.8f, 20, 60, 2, 20, 1f, 1800, 1100, 2200);
            Assert.AreEqual(2200, loot.Coins);
            Assert.AreEqual(1800, loot.Wood);
            Assert.AreEqual(1100, loot.Iron);
        }

        /// <summary>A loss still pays gold - 18% of the base. A dead end is the defect.</summary>
        [Test]
        public void Loot_Gold_FailedAttack_StillPays()
        {
            var loot = RaidScoring.ComputeLoot(0, 0f, 20, 60, 2, 20, 1f, 1800, 1100, 2200);
            Assert.AreEqual(396, loot.Coins);     // 18% of 2200
            Assert.AreEqual(0, loot.Crystals);    // nothing razed
            Assert.AreEqual(0, loot.Stone);
        }

        /// <summary>
        /// Gold carries its escalation in a PER-CAMP base, so the camp difficulty
        /// multiplier must never touch it: x1.5 of 2,200 is 3,300, and the map's Camp II
        /// number is 3,100.
        /// </summary>
        [Test]
        public void Loot_CampMultiplier_DoesNotReachGold()
        {
            var flat = RaidScoring.ComputeLoot(3, 1f, 0, 0, 0, 0, 1f, 0, 0, 2200);
            var hard = RaidScoring.ComputeLoot(3, 1f, 0, 0, 0, 0, 2.2f, 0, 0, 2200);
            Assert.AreEqual(flat.Coins, hard.Coins);
        }

        /// <summary>
        /// "Crystals are timer compression." An escalating camp raises gold, wood and iron -
        /// never instant-finish. Food still carries the multiplier.
        /// </summary>
        [Test]
        public void Loot_CampMultiplier_DoesNotReachCrystals()
        {
            var flat = RaidScoring.ComputeLoot(3, 1f, 25, 60, 10, 20, 1f);
            var hard = RaidScoring.ComputeLoot(3, 1f, 25, 60, 10, 20, 1.5f);
            Assert.AreEqual(flat.Crystals, hard.Crystals);
            Assert.Greater(hard.Stone, flat.Stone);
        }
    }
}
