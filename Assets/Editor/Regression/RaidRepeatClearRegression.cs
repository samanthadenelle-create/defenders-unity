// =============================================================================
// RaidRepeatClearRegression [raid-repeat-clear]  --  markers RAID_REPEAT_CLEAR_OK / _FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Edit mode, no PlayMode: half BEHAVIOURAL
// (it calls the real DeNelle.Village statics) and half SOURCE-LINT (it reads the
// raid runtime files with comments AND string literals stripped, so a symbol named
// only inside a comment or a log message can never satisfy a pin).
// Registered in DataRegression.RunAll.  NEVER throws.
//
// Pins the two raid-economy defects found in the 2026-08-15 sweep. Both were WRITE-
// ONLY / UNCHECKED paths that paid out, and both were covered by a comment claiming
// the opposite - which is why neither showed up in review:
//
//   PIN A  A REPEAT CLEAR CANNOT PAY FULL LOOT AGAIN.
//          RaidClaimService was write-only: IsClaimed had no caller outside
//          MarkClaimed's own re-claim guard, ClearClaim had ZERO callers, and
//          RaidVictoryController never gated the payout on newClaim. So re-entering
//          an already-claimed base and razing it again paid the FULL settled loot,
//          every time, forever - on the Extreme tier (rewardMultiplier 2.2) an
//          unbounded resource faucet. Asserts the gate ARITHMETIC (pure static), the
//          claim set's ROUND TRIP (PlayerPrefs, on a scratch id), and the CALL ORDER
//          in both victory controllers - the read must precede the claim that flips it.
//
//          WO-1134 EXTENDS PIN A WITH THE CRYSTAL DAY-STAMP. Crystals are now paid on
//          the FIRST clear of each UTC DAY rather than only on the clear that claims
//          the base, so the DAY - not the raid cooldown - is what bounds the game's one
//          unbounded faucet. That is a SECOND, INDEPENDENT axis and the pin asserts it
//          as a 3-case matrix, because the case that matters is the CROSS: an
//          already-claimed base on a NEW day pays reduced ordinary resources AND full
//          crystals. It also pins that the day stamp lives on its OWN PlayerPrefs key -
//          folding it into the ownership key would day-scope the ONE-TIME companion
//          unlock and re-grant a companion every day, forever.
//
//   PIN B  AN OFF-NAVMESH DEPLOY DOES NOT PRODUCE A COUNTED SURVIVOR.
//          RaidDeployController raycast the deploy tap against ALL layers and never
//          tested walkability; TroopFactory's SamplePosition then failed and SPAWNED
//          ANYWAY behind a Debug.LogWarning (invisible to F8). The inert body never
//          fights, never dies, and counts as a SURVIVOR at reconcile - lifting
//          SurvivalPct past the 70% axis and BUYING 3-star clears (and, since victory
//          pays veterancy at 3 stars, promoting the warband with it). Asserts the
//          arithmetic of that exploit against the real RaidScoring.ComputeStars (so
//          the stake is measured, not asserted) and pins the INPUT-side refusal that
//          now makes it unreachable: a NavMesh test, before the spawn, that returns,
//          with a player-visible tell and a FlowTrace line.
//
// Standalone:
//   -Method DeNelle.Editor.Regression.RaidRepeatClearRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    public static class RaidRepeatClearRegression
    {
        // Relative to Application.dataPath.
        private const string ClaimRel   = "_Modules/Village/World/Camps/RaidClaimService.cs";
        private const string VictoryRel = "_Modules/Village/World/Camps/RaidVictoryController.cs";
        private const string V2Rel      = "_Modules/Village/World/Camps/Village2RaidController.cs";
        private const string DeployRel  = "_Modules/Village/Troops/RaidDeployController.cs";
        private const string FactoryRel = "_Modules/Village/Troops/TroopFactory.cs";

        // A scratch claim id no scene-config will ever use, so the round trip cannot
        // disturb a real save. Cleared on every exit path, including the throwing one.
        private const string ScratchId = "zz-regression-scratch-raid-claim";

        // Declared as a balanced PAIR on one line on purpose (RegressionMarkerRegression's
        // precedent): a lone brace char literal trips the CLAUDE.md rule-1 brace counter.
        private const char OpenBrace = '{', CloseBrace = '}';

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "raid-repeat-clear: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                // The scratch claim + the scratch crystal day-stamp never survive this suite,
                // however it exits.
                try { RaidClaimService.ClearClaim(ScratchId); }
                catch (Exception ex) { Debug.LogWarning("raid-repeat-clear: scratch cleanup failed: " + ex.Message); }
                try { RaidClaimService.ClearCrystalDayStamp(ScratchId); }
                catch (Exception ex) { Debug.LogWarning("raid-repeat-clear: scratch day-stamp cleanup failed: " + ex.Message); }
            }
        }

        /// <summary>Standalone batch entry.</summary>
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("RAID_REPEAT_CLEAR_OK - " + reason);
            else Debug.LogError("RAID_REPEAT_CLEAR_FAIL - " + reason);
        }

        private static bool RunCore(out string reason)
        {
            var fails = new List<string>();

            string claim   = ReadCode(ClaimRel,   fails);
            string victory = ReadCode(VictoryRel, fails);
            string v2      = ReadCode(V2Rel,      fails);
            string deploy  = ReadCode(DeployRel,  fails);
            string factory = ReadCode(FactoryRel, fails);

            CheckRepeatClearGate(claim, victory, v2, fails);
            CheckOffMeshDeployRefused(deploy, factory, fails);

            if (fails.Count == 0)
            {
                Debug.Log("RAID_REPEAT_CLEAR_OK");
                reason = "RAID REPEAT CLEAR OK -- a re-clear of a claimed base pays x" +
                         RaidClaimService.RepeatClearLootMultiplier.ToString("0.##") +
                         " of the settled ORDINARY loot (gate arithmetic + PlayerPrefs round trip verified " +
                         "live), CRYSTALS are paid only on the FIRST clear of each UTC day on an independent " +
                         "stamp that never touches the one-time claim/companion flag (3-case matrix verified: " +
                         "first-today full / repeat-today zero / first-of-a-new-day full again), " +
                         "both victory controllers read the claim BEFORE they write it, and an off-NavMesh " +
                         "deploy tap is refused at the input with a player tell + a FlowTrace line, so no " +
                         "inert body can reach the survivor ledger";
                return true;
            }

            reason = "raid-repeat-clear (" + fails.Count + "): " + string.Join(" | ", fails.ToArray());
            Debug.LogError("RAID_REPEAT_CLEAR_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  PIN A - a repeat clear cannot pay full loot again
        // =====================================================================

        private static void CheckRepeatClearGate(string claim, string victory, string v2, List<string> fails)
        {
            // --- A1: the knob exists and can only ever REDUCE a repeat payout ---
            float mult = RaidClaimService.RepeatClearLootMultiplier;
            if (!(mult < 1f))
                fails.Add("RaidClaimService.RepeatClearLootMultiplier is " + mult.ToString("0.###") +
                          " - a repeat clear would pay the FULL settled loot again (or more). The whole " +
                          "point of the first-clear gate is that a claimed base cannot be farmed for its " +
                          "full payout; an Extreme base carries rewardMultiplier 2.2");
            if (mult < 0f)
                fails.Add("RaidClaimService.RepeatClearLootMultiplier is NEGATIVE (" + mult.ToString("0.###") +
                          ") - a repeat clear would compute a negative payout");

            // --- A2: the gate ARITHMETIC, run for real against the pure static ---
            var full = new ResourceCost(wood: 400, food: 600, iron: 300, crystals: 500, coins: 200);

            var first = RaidClaimService.ScaleLootForClear(full, false, false);
            if (first.Wood != full.Wood || first.Food != full.Food || first.Iron != full.Iron
                || first.Crystals != full.Crystals || first.Coins != full.Coins)
                fails.Add("ScaleLootForClear(loot, isRepeatClear:false, crystalsAlreadyPaidToday:false) altered " +
                          "a FIRST clear's payout (crystals " + first.Crystals + " of " + full.Crystals +
                          ") - the gate must be invisible on the clear that claims the base");

            var repeat = RaidClaimService.ScaleLootForClear(full, true, true);
            if (repeat.Crystals >= full.Crystals || repeat.Food >= full.Food || repeat.Wood >= full.Wood
                || repeat.Iron >= full.Iron || repeat.Coins >= full.Coins)
                fails.Add("ScaleLootForClear(loot, isRepeatClear:true, crystalsAlreadyPaidToday:true) did NOT " +
                          "reduce the payout (crystals " + repeat.Crystals + " of " + full.Crystals + ", food " +
                          repeat.Food + " of " + full.Food + ") - re-clearing a claimed base still pays what the " +
                          "first clear paid, which is the infinite-faucet defect this pin exists to stop");

            // The absolute floor, independent of whatever curve the owner later sets: a repeat
            // may never out-earn the first clear on ANY axis.
            if (repeat.Wood > full.Wood || repeat.Food > full.Food || repeat.Iron > full.Iron
                || repeat.Crystals > full.Crystals || repeat.Coins > full.Coins)
                fails.Add("a REPEAT clear out-earns the FIRST clear on at least one axis - the multiplier " +
                          "knob has been set above 1 and the defensive clamp in ScaleLootForClear is gone");

            // Zero in, zero out - a scaled nothing must never conjure a payout.
            var nothing = RaidClaimService.ScaleLootForClear(default(ResourceCost), true, true);
            if (!nothing.IsZero)
                fails.Add("ScaleLootForClear(zero loot, repeat) returned a NON-ZERO payout - the gate is " +
                          "inventing resources out of an empty result");

            // ------------------------------------------------------------------
            //  A2b (WO-1134) - THE CRYSTAL DAY-STAMP IS A SECOND, INDEPENDENT AXIS
            // ------------------------------------------------------------------
            // The three cases the owner ruling actually describes, asserted as a MATRIX,
            // because the interesting one is the CROSS: a base claimed months ago
            // (repeatClear TRUE) whose crystal day has rolled over (paid FALSE) must pay
            // reduced ordinary resources AND full crystals. A single overloaded flag cannot
            // express that case, which is exactly what the old two-arg signature got wrong.
            int expectRepeatWood = Mathf.FloorToInt(full.Wood * mult);
            int expectRepeatFood = Mathf.FloorToInt(full.Food * mult);

            // Case 1 - FIRST clear of the day (and of the base): everything in full.
            var dayCase1 = RaidClaimService.ScaleLootForClear(full, false, false);
            if (dayCase1.Crystals != full.Crystals)
                fails.Add("crystal day-stamp case 1 (first clear today, crystals unpaid): paid " +
                          dayCase1.Crystals + " crystals of " + full.Crystals + " - the first clear of a " +
                          "UTC day must pay its crystals IN FULL");

            // Case 2 - REPEAT clear the SAME day: crystals zero, ordinary resources still x0.25.
            var dayCase2 = RaidClaimService.ScaleLootForClear(full, true, true);
            if (dayCase2.Crystals != 0)
                fails.Add("crystal day-stamp case 2 (repeat clear, same UTC day): paid " + dayCase2.Crystals +
                          " crystals - a camp that has already paid crystals today must pay ZERO. Crystals " +
                          "are the one unbounded faucet in the game and the day stamp is what bounds them");
            if (dayCase2.Wood != expectRepeatWood || dayCase2.Food != expectRepeatFood)
                fails.Add("crystal day-stamp case 2 (repeat clear, same UTC day): ordinary resources came " +
                          "back wood " + dayCase2.Wood + "/food " + dayCase2.Food + ", expected " +
                          expectRepeatWood + "/" + expectRepeatFood + " - the crystal axis has been allowed " +
                          "to disturb the x" + mult.ToString("0.##") + " repeat-clear scaling it must not touch");

            // Case 3 - the CROSS. First clear of a NEW day on an already-claimed base:
            // ordinary resources still reduced, crystals FULL again.
            var dayCase3 = RaidClaimService.ScaleLootForClear(full, true, false);
            if (dayCase3.Crystals != full.Crystals)
                fails.Add("crystal day-stamp case 3 (first clear of a NEW UTC day on a base claimed earlier): " +
                          "paid " + dayCase3.Crystals + " crystals of " + full.Crystals + " - crystals RESET " +
                          "daily under the owner ruling, so a long-claimed camp pays them again tomorrow. This " +
                          "is the case the old two-arg signature got silently wrong by hardcoding crystals:0 " +
                          "on every repeat");
            if (dayCase3.Wood != expectRepeatWood || dayCase3.Food != expectRepeatFood)
                fails.Add("crystal day-stamp case 3: ordinary resources came back wood " + dayCase3.Wood +
                          "/food " + dayCase3.Food + ", expected " + expectRepeatWood + "/" + expectRepeatFood +
                          " - a new crystal day must NOT restore the full ordinary payout; the two axes are " +
                          "independent and repeatClear still governs wood/food/iron/coins");

            // The remaining corner: unpaid crystals on a repeat must never come back SCALED.
            // A fractional premium payout is how a bounded faucet quietly becomes an unbounded one.
            if (dayCase3.Crystals != 0 && dayCase3.Crystals != full.Crystals)
                fails.Add("crystal day-stamp: crystals were SCALED by the repeat multiplier (" +
                          dayCase3.Crystals + " of " + full.Crystals + ") - crystals are all-or-nothing on " +
                          "the day stamp, never a fraction");

            // ------------------------------------------------------------------
            //  A2c - the day stamp actually ROUND TRIPS through PlayerPrefs
            // ------------------------------------------------------------------
            RaidClaimService.ClearCrystalDayStamp(ScratchId);
            if (RaidClaimService.CrystalsPaidToday(ScratchId))
                fails.Add("RaidClaimService.CrystalsPaidToday reported TRUE on a camp with no stamp - every " +
                          "raid would pay zero crystals forever");

            RaidClaimService.MarkCrystalsPaid(ScratchId);
            if (!RaidClaimService.CrystalsPaidToday(ScratchId))
                fails.Add("RaidClaimService.CrystalsPaidToday returned FALSE immediately after MarkCrystalsPaid " +
                          "- the day stamp does not persist, so every clear of the day would pay full crystals " +
                          "and the cooldown would again be the only bound");

            RaidClaimService.ClearCrystalDayStamp(ScratchId);
            if (RaidClaimService.CrystalsPaidToday(ScratchId))
                fails.Add("RaidClaimService.ClearCrystalDayStamp left the stamp in place - the dev/test reset " +
                          "hook does not work");

            // The stamp must NOT be the claim flag wearing a different name: claiming a base
            // must never imply its crystals were paid, and vice versa. This separation is what
            // stops a day-scoped flag from re-granting the ONE-TIME companion unlock every day.
            RaidClaimService.ClearClaim(ScratchId);
            RaidClaimService.MarkCrystalsPaid(ScratchId);
            if (RaidClaimService.IsClaimed(ScratchId))
                fails.Add("MarkCrystalsPaid also flipped the CLAIM flag - the daily crystal axis has been " +
                          "wired into the one-time ownership key, which gates the next-companion unlock. " +
                          "Day-scoping that key re-grants a companion every day, forever");
            RaidClaimService.ClearCrystalDayStamp(ScratchId);
            RaidClaimService.MarkClaimed(ScratchId);
            if (RaidClaimService.CrystalsPaidToday(ScratchId))
                fails.Add("MarkClaimed also stamped the CRYSTAL DAY - claiming a base would consume its " +
                          "crystal payout for the day, so the very clear that claims it pays none");
            RaidClaimService.ClearClaim(ScratchId);

            // --- A3: the claim set actually round-trips (it was write-only before) ---
            RaidClaimService.ClearClaim(ScratchId);
            if (RaidClaimService.IsClaimed(ScratchId))
                fails.Add("RaidClaimService.ClearClaim did not drop the claim on '" + ScratchId +
                          "' - IsClaimed still reports it owned, so no base can ever be reset");

            bool firstMark = RaidClaimService.MarkClaimed(ScratchId);
            if (!firstMark)
                fails.Add("RaidClaimService.MarkClaimed returned false on a FIRST claim - the caller's " +
                          "newClaim signal is inverted, so the one-time payoff would never be granted");
            if (!RaidClaimService.IsClaimed(ScratchId))
                fails.Add("RaidClaimService.IsClaimed returned false immediately after MarkClaimed - the " +
                          "claim does not persist, so EVERY clear reads as a first clear and pays in full");

            bool secondMark = RaidClaimService.MarkClaimed(ScratchId);
            if (secondMark)
                fails.Add("RaidClaimService.MarkClaimed returned TRUE on a re-claim of an already-claimed " +
                          "base - the re-claim guard is gone and a repeat clear would re-grant the one-time payoff");

            RaidClaimService.ClearClaim(ScratchId);
            if (RaidClaimService.IsClaimed(ScratchId))
                fails.Add("RaidClaimService.ClearClaim left the claim in place after a real claim - the " +
                          "dev/test reset hook does not work");

            // --- A4: the SOURCE wiring - order of the read against the write ---
            if (claim.IndexOf("ScaleLootForClear", StringComparison.Ordinal) < 0)
                fails.Add("RaidClaimService no longer exposes ScaleLootForClear - the first-clear gate's " +
                          "single arithmetic authority is gone");
            if (claim.IndexOf("CrystalsPaidToday", StringComparison.Ordinal) < 0)
                fails.Add("RaidClaimService no longer exposes CrystalsPaidToday - the WO-1134 crystal " +
                          "day-stamp is gone and crystals are unbounded within a day again");
            if (claim.IndexOf("MarkCrystalsPaid", StringComparison.Ordinal) < 0)
                fails.Add("RaidClaimService no longer exposes MarkCrystalsPaid - nothing records that a camp " +
                          "paid its crystals today, so every clear of the day pays them again");
            // Pinned on the IDENTIFIER, never on the key string: ReadCode strips string
            // literals, so a pin on "dotr-raid-crystalday-" could never match anything.
            if (claim.IndexOf("PrefCrystalDayKey", StringComparison.Ordinal) < 0)
                fails.Add("RaidClaimService no longer declares PrefCrystalDayKey - the crystal day-stamp has " +
                          "lost its OWN PlayerPrefs key. If it has been folded into PrefOwnerKey then the " +
                          "ONE-TIME companion unlock is now day-scoped and re-grants a companion every day, " +
                          "forever (RaidVictoryController + OutpostVictoryController both read that flag)");

            string handleVictory = Body(victory, @"void\s+HandleVictory\s*\([^)]*\)");
            if (string.IsNullOrEmpty(handleVictory))
                fails.Add("could not locate RaidVictoryController.HandleVictory's body - the loot gate " +
                          "wiring cannot be verified");
            else
            {
                // Re-pointed 2026-09-09 by the lead WITH the owner's WO-1461 ruling (60% repeat clear inside
                // the same cycle): the read is now IsRepeatClearInCycle, which wraps IsClaimed AND the cooldown
                // stamp (RaidClaimService.cs ~:151). It must still precede ClaimBase (flips the flag) and now
                // also BeginAfterClear (stamps the cooldown) - either one first would read every clear as a repeat.
                int iRead  = handleVictory.IndexOf("RaidClaimService.IsRepeatClearInCycle(", StringComparison.Ordinal);
                int iClaim = handleVictory.IndexOf("ClaimBase(", StringComparison.Ordinal);
                int iStamp = handleVictory.IndexOf("BeginAfterClear(", StringComparison.Ordinal);
                int iGate  = handleVictory.IndexOf("ApplyFirstClearGate(", StringComparison.Ordinal);
                int iGrant = handleVictory.IndexOf("GrantLoot(", StringComparison.Ordinal);

                if (iRead < 0)
                    fails.Add("RaidVictoryController.HandleVictory no longer calls RaidClaimService.IsRepeatClearInCycle " +
                              "- nothing tells a first clear from a same-cycle repeat, so every clear pays in full again");
                if (iRead >= 0 && iStamp >= 0 && iRead > iStamp)
                    fails.Add("RaidVictoryController.HandleVictory reads IsRepeatClearInCycle (at " + iRead + ") AFTER " +
                              "BeginAfterClear (at " + iStamp + "), which stamps the cooldown the predicate reads - so " +
                              "every clear would read as a repeat. Read before you stamp");
                if (iGate < 0)
                    fails.Add("RaidVictoryController.HandleVictory no longer calls ApplyFirstClearGate - the " +
                              "settled loot reaches GrantLoot ungated, which is the infinite-faucet defect");
                if (iGrant < 0)
                    fails.Add("RaidVictoryController.HandleVictory no longer calls GrantLoot - the win pays nothing");
                if (iRead >= 0 && iClaim >= 0 && iRead > iClaim)
                    fails.Add("RaidVictoryController.HandleVictory reads IsClaimed (at " + iRead + ") AFTER " +
                              "ClaimBase (at " + iClaim + "), which is the call that FLIPS the flag - so every " +
                              "clear would read as a repeat and no raid would ever pay. Read before you claim");
                if (iGate >= 0 && iGrant >= 0 && iGate > iGrant)
                    fails.Add("RaidVictoryController.HandleVictory grants the loot (at " + iGrant + ") BEFORE " +
                              "applying the first-clear gate (at " + iGate + ") - the full payout has already " +
                              "landed in the wallet by the time it is scaled");

                // --- WO-1134: the crystal day-stamp's own read-before-write + write-after-grant ---
                int iDayRead  = handleVictory.IndexOf("RaidClaimService.CrystalsPaidToday(", StringComparison.Ordinal);
                int iDayWrite = handleVictory.IndexOf("RaidClaimService.MarkCrystalsPaid(", StringComparison.Ordinal);

                if (iDayRead < 0)
                    fails.Add("RaidVictoryController.HandleVictory no longer reads " +
                              "RaidClaimService.CrystalsPaidToday - nothing tells the first clear of a UTC day " +
                              "from the second, so every clear of the day pays full crystals again");
                if (iDayWrite < 0)
                    fails.Add("RaidVictoryController.HandleVictory no longer calls " +
                              "RaidClaimService.MarkCrystalsPaid - the day is never stamped, so the gate reads " +
                              "'unpaid' forever and bounds nothing");
                if (iDayRead >= 0 && iDayWrite >= 0 && iDayRead > iDayWrite)
                    fails.Add("RaidVictoryController.HandleVictory reads CrystalsPaidToday (at " + iDayRead +
                              ") AFTER MarkCrystalsPaid (at " + iDayWrite + "), which is the call that WRITES " +
                              "the stamp - so every clear would read as already-paid and no raid would ever pay " +
                              "crystals. Read before you stamp, exactly as with the claim flag");
                if (iDayWrite >= 0 && iGrant >= 0 && iDayWrite < iGrant)
                    fails.Add("RaidVictoryController.HandleVictory stamps the crystal day (at " + iDayWrite +
                              ") BEFORE GrantLoot (at " + iGrant + ") - a grant that then throws would have " +
                              "burned the player's one crystal clear of the day for nothing");
            }

            string handleCleared = Body(v2, @"void\s+HandleCleared\s*\([^)]*\)");
            if (string.IsNullOrEmpty(handleCleared))
                fails.Add("could not locate Village2RaidController.HandleCleared's body");
            else
            {
                // Re-pointed 2026-09-09 by the lead WITH the owner's WO-1461 ruling (60% same-cycle
                // repeat clear): the village raid path now reads IsRepeatClearInCycle, like HandleVictory,
                // and must read it BEFORE BeginAfterClear stamps the window it consults.
                int iRead  = handleCleared.IndexOf("RaidClaimService.IsRepeatClearInCycle(", StringComparison.Ordinal);
                int iClaim = handleCleared.IndexOf("ClaimBase(", StringComparison.Ordinal);
                int iStamp = handleCleared.IndexOf("BeginAfterClear(", StringComparison.Ordinal);
                if (iRead < 0)
                    fails.Add("Village2RaidController.HandleCleared does not read RaidClaimService.IsRepeatClearInCycle - " +
                              "its own header comment claims this controller both writes AND reads the claim key; " +
                              "a comment asserting a read that does not exist is how the write-only claim set survived review");
                if (iRead >= 0 && iStamp >= 0 && iRead > iStamp)
                    fails.Add("Village2RaidController.HandleCleared reads IsRepeatClearInCycle (at " + iRead + ") AFTER " +
                              "BeginAfterClear (at " + iStamp + ") stamps the cooldown it consults - every clear would read as a repeat");
                if (iRead >= 0 && iClaim >= 0 && iRead > iClaim)
                    fails.Add("Village2RaidController.HandleCleared reads IsClaimed (at " + iRead + ") after " +
                              "ClaimBase (at " + iClaim + ") flips it - the read can only ever answer 'repeat'");
            }
        }

        // =====================================================================
        //  PIN B - an off-NavMesh deploy does not produce a counted survivor
        // =====================================================================

        private static void CheckOffMeshDeployRefused(string deploy, string factory, List<string> fails)
        {
            // --- B1: MEASURE the stake against the real scorer, so this pin states a
            // number rather than an opinion. Three real troops, two standing = 66%
            // survival, one axis short of the 70% high-survival gate = 2 stars. Add ONE
            // inert off-mesh body (alive by definition - it never fought) and the same
            // raid reads 75% and scores 3, which is the veterancy-paying tier.
            const float clock = 180f, elapsed = 120f;
            int honest  = RaidScoring.ComputeStars(true, true, 1f, elapsed, clock, 2f / 3f);
            int phantom = RaidScoring.ComputeStars(true, true, 1f, elapsed, clock, 3f / 4f);
            if (!(honest < phantom))
                fails.Add("the survivor-inflation exploit no longer reproduces in RaidScoring.ComputeStars " +
                          "(2/3 survival scored " + honest + ", 3/4 scored " + phantom + ") - the star ladder " +
                          "changed under this pin. Re-derive the stake before relaxing the deploy gate; the " +
                          "input-side refusal below is what keeps a phantom survivor out of the numerator");
            if (phantom < 3)
                fails.Add("a 3/4-survival clear inside the clock no longer reaches 3 stars (scored " + phantom +
                          ") - the veterancy tier the phantom survivor was buying has moved");

            // --- B2: the INPUT-side refusal, in order, before any spawn ---
            string tap = Body(deploy, @"void\s+HandleDeployTap\s*\([^)]*\)");
            if (string.IsNullOrEmpty(tap))
            {
                fails.Add("could not locate RaidDeployController.HandleDeployTap's body - the NavMesh deploy " +
                          "gate cannot be verified");
            }
            else
            {
                int iSample = tap.IndexOf("NavMesh.SamplePosition(", StringComparison.Ordinal);
                int iSpawn  = tap.IndexOf("SpawnFromArmy(", StringComparison.Ordinal);
                int iLedger = tap.IndexOf("_deployed.Add(", StringComparison.Ordinal);

                if (iSample < 0)
                    fails.Add("RaidDeployController.HandleDeployTap performs NO NavMesh.SamplePosition test on " +
                              "the tap. RaycastGround falls back to ALL layers, so a tap on scenery or " +
                              "out-of-bounds terrain resolves a hit and drops an INERT troop that counts as a " +
                              "survivor at reconcile - free 3-star clears. The file header claims 'deploy " +
                              "anywhere on the NavMesh'; without this test that is a claim, not a rule");
                if (iSpawn < 0)
                    fails.Add("RaidDeployController.HandleDeployTap no longer spawns through " +
                              "TroopDeployer.SpawnFromArmy - the canonical deploy path has been forked");
                if (iSample >= 0 && iSpawn >= 0 && iSample > iSpawn)
                    fails.Add("RaidDeployController.HandleDeployTap samples the NavMesh (at " + iSample +
                              ") AFTER it has already spawned the troop (at " + iSpawn + ") - the body exists " +
                              "before the check can refuse it, so the check protects nothing");
                if (iSample >= 0 && iLedger >= 0 && iSample > iLedger)
                    fails.Add("RaidDeployController.HandleDeployTap adds to the deployed ledger (at " + iLedger +
                              ") before the NavMesh test (at " + iSample + ") - an off-mesh body would still " +
                              "reach the survivor reconcile");

                // The refusal must be a REFUSAL: it must return, tell the player, and trace.
                if (iSample >= 0 && iSpawn > iSample)
                {
                    string guard = tap.Substring(iSample, iSpawn - iSample);
                    if (guard.IndexOf("return", StringComparison.Ordinal) < 0)
                        fails.Add("the NavMesh test in HandleDeployTap does not RETURN on failure - it " +
                                  "observes the off-mesh tap and then spawns anyway, which is precisely what " +
                                  "TroopFactory was already doing");
                    if (guard.IndexOf("SetStatus(", StringComparison.Ordinal) < 0)
                        fails.Add("the NavMesh test in HandleDeployTap gives the player NO visible tell on " +
                                  "refusal - a tap that silently does nothing reads as a frozen game, which is " +
                                  "why 'just drop it anyway' looked like the safer behaviour in the first place");
                    if (guard.IndexOf("FlowTrace.", StringComparison.Ordinal) < 0)
                        fails.Add("the NavMesh test in HandleDeployTap emits no FlowTrace line - F8 would not " +
                                  "see the refusal, repeating the Debug.LogWarning mistake this pin closes " +
                                  "(CLAUDE.md sec.12)");
                }
            }

            // --- B3: the factory's off-mesh branch is F8-visible and shares one radius ---
            if (factory.IndexOf("NavSampleRadius", StringComparison.Ordinal) < 0)
                fails.Add("TroopFactory no longer declares NavSampleRadius - the deploy gate and the spawn " +
                          "snap would drift apart, so the tap could accept a point the factory then cannot place");

            string build = Body(factory, @"TroopController\s+Build\s*\([^)]*\)");
            if (string.IsNullOrEmpty(build))
                fails.Add("could not locate TroopFactory.Build's body - its off-mesh reporting cannot be verified");
            else
            {
                int iSample = build.IndexOf("NavMesh.SamplePosition(", StringComparison.Ordinal);
                if (iSample < 0)
                    fails.Add("TroopFactory.Build no longer snaps the spawn with NavMesh.SamplePosition");
                if (build.IndexOf("FlowTrace.", StringComparison.Ordinal) < 0)
                    fails.Add("TroopFactory.Build's off-mesh branch reports through no FlowTrace call. It was a " +
                              "bare Debug.LogWarning, which the F8 break-capture harness does not see - so the " +
                              "single most consequential spawn failure in the raid loop produced NO evidence " +
                              "and the inert-survivor defect went months without a capture (CLAUDE.md sec.12)");
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>
        /// The brace-matched body of the first method whose signature matches, from the
        /// signature's opening brace to its balanced close. Brace-matched rather than
        /// indentation-matched so a nested block cannot end the extraction early.
        /// </summary>
        private static string Body(string code, string signaturePattern)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            var m = Regex.Match(code, signaturePattern);
            if (!m.Success) return string.Empty;
            int open = code.IndexOf(OpenBrace, m.Index + m.Length);
            if (open < 0) return string.Empty;
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == OpenBrace) depth++;
                else if (code[i] == CloseBrace)
                {
                    depth--;
                    if (depth == 0) return code.Substring(open, i - open + 1);
                }
            }
            return string.Empty;
        }

        /// <summary>Reads a file under Assets/ as CODE ONLY; records a failure if missing.</summary>
        private static string ReadCode(string rel, List<string> fails)
        {
            string path = Path.Combine(Application.dataPath, rel);
            if (!File.Exists(path))
            {
                fails.Add("raid runtime file missing: " + rel);
                return string.Empty;
            }
            try { return StripCommentsAndLiterals(File.ReadAllText(path)); }
            catch (IOException ex)
            {
                fails.Add("could not read " + rel + ": " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Strips // line comments, block comments AND the CONTENTS of string literals,
        /// preserving offsets/line structure well enough for ordering comparisons.
        ///
        /// <para>Literals are emptied, not just comments, because these files DOCUMENT the
        /// very symbols this suite looks for - the deploy refusal's own FlowTrace message
        /// names SurvivalPct and the reconcile, and RaidClaimService's summary names
        /// IsClaimed and ScaleLootForClear. Matching raw text would let a log message or a
        /// comment satisfy a pin that no call site actually meets, which is a hollow pass
        /// wearing a green marker.</para>
        /// </summary>
        private static string StripCommentsAndLiterals(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';

                // Line comment -> keep the newline only.
                if (c == '/' && n == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                // Block comment -> keep any newlines inside it.
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;   // land on the '/' (the for-loop's i++ steps past it)
                    continue;
                }
                // Char literal -> emptied.
                if (c == '\'')
                {
                    i++;
                    while (i < src.Length && src[i] != '\'')
                    {
                        if (src[i] == '\\') i++;
                        i++;
                    }
                    sb.Append("''");
                    continue;
                }
                // Verbatim string -> emptied ("" is an escaped quote inside one).
                if (c == '@' && n == '"')
                {
                    i += 2;
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '"') { i += 2; continue; }
                            break;
                        }
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    sb.Append("\"\"");
                    continue;
                }
                // Regular (and interpolated) string -> emptied. An interpolated hole may
                // hold a real call, but a hole is not a call SITE for these pins, and
                // dropping it is the conservative direction: a pin can only go red.
                if (c == '"')
                {
                    i++;
                    while (i < src.Length && src[i] != '"')
                    {
                        if (src[i] == '\\') i++;
                        i++;
                    }
                    sb.Append("\"\"");
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
