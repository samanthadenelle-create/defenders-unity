// =============================================================================
// WO1191OverCapIncomeRegression -- the MEASURED oracle for income above the cap.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Contract: public static bool Run(out string reason). Registered in DataRegression.RunAll
// as [over-cap-income]. Marker: the suite rolls into REGRESSION_OK <n>/<n> suites.
//
// THE RULING
//   `FOUNDATIONAL_RULINGS.md` section 7. Read it THERE. It is deliberately not restated,
//   paraphrased or summarised anywhere in this file -- a rule copied into a second place
//   goes stale and the stale copy is what gets believed.
//
// WHY THIS SUITE EXISTS, GIVEN THE MECHANICS WERE ALREADY CORRECT
//   At WO-1191 the behaviour already matched: TownBankCapacity.ClampGrant computes
//   room = max(0, max - current), so an earned grant onto an over-cap balance already
//   returned 0; and TownBankCapacity.IsClampable already exempted a paid grant from the
//   clamp entirely. NOTHING PROVED IT BY MEASUREMENT. Every existing assertion read the
//   RETURN VALUE of the clamp helper, which is the same act as reading the code -- it
//   cannot catch a caller that computes the right number and then banks the wrong one.
//   WO-978 is exactly that failure: four economy callers logged the amount REQUESTED as
//   though it were the amount CREDITED, so every log agreed the player had been paid
//   while the bank took nothing. So every case below reads the WALLET before, performs
//   the real credit through the real EconomyService, reads the WALLET after, and asserts
//   the DELTA.
//
// ** THE FIRST DRAFT OF THIS FILE WAS ITSELF A HOLLOW PASS. READ THIS BEFORE EDITING. **
//   It shipped two IsCapped stand-down guards that added a prose note and bailed out --
//   telling the READER they were standing down and the CALLER nothing, and landing
//   in the green column, which is precisely the class docs/HANDOVER.md "THE LESSON OF THE
//   NIGHT" is about, committed inside the suite meant to prove measured behaviour. It also
//   discarded a TrySpend bool, so a DECLINED fixture spend would have let every downstream
//   case report on a state that never existed. Both were caught by existing ratchets
//   (RegressionMarkerRegression RULE 4, BUILDMENU ECONOMY [tryspend-honoured]). The
//   ratchets were right and were NOT widened. What changed here instead:
//
//   1. THE VEHICLE IS NO LONGER HARDCODED TO WOOD. The suite DISCOVERS a capped resource
//      by enumerating BankResource and asking TownBankCapacity.IsCapped -- which is also a
//      stronger reading of the ticket's rule 1 than the first draft managed, since naming
//      Wood in the test was itself a resource-name list waiting to go stale.
//   2. THE THREE STAND-DOWN OUTCOMES ARE SEPARATED per the docs/HANDOVER.md taxonomy, and
//      each one is justified where it is taken -- see CheckMeasuredDeltas.
//   3. EVERY TrySpend RETURN IS CONSUMED. A fixture whose seeding was declined FAILS the
//      case by name. docs/HANDOVER.md rule 1: a red that describes a plausible product bug
//      is still, first, a claim about the harness -- so the harness must say so itself.
//
// WHAT EACH CASE PINS, AND WHAT BROKEN STATE MAKES IT FAIL
//   (INSTRUMENTATION_STANDARD Sec.1.4b -- an assertion nothing can break is decoration.)
//
//   [paid-overflows]      A purchase onto a full wallet must land in FULL, above the cap.
//                         FAILS IF: IsClampable ever returns true for PurchasedOrPromised;
//                         a pack path is re-routed through the capped Grant; or anyone adds
//                         a post-grant "normalise the wallet to the cap" pass. Any of those
//                         DELETES value the player paid for.
//
//   [earned-adds-zero]    With the wallet ABOVE the cap, an earned credit must move the
//                         wallet by EXACTLY 0.
//                         FAILS IF: room is ever computed without the max(0,...) floor
//                         (a negative room added to a request re-opens partial credit); if a
//                         caller banks its request local instead of the clamp's return (the
//                         WO-978 shape); or if an overflow wallet / escrow is introduced and
//                         starts paying out. Also fails if the grant lands NEGATIVE -- an
//                         over-cap balance must not be drained toward the cap either.
//
//   [spend-restores]      After spending back under the cap, the SAME earned credit must
//                         move the wallet again.
//                         FAILS IF: suppression is ever latched per-resource or per-session
//                         instead of being re-evaluated from the live balance -- a faucet
//                         that stops and never restarts is the "I did the raid and got
//                         nothing" complaint, permanently.
//
//   [uncapped-unaffected] Every resource for which TownBankCapacity.IsCapped() is false must
//                         pay in FULL no matter how far above any number it sits.
//                         FAILS IF: a crystal/coin cap is introduced by implication -- the
//                         contradiction that sent WO-978 back. The set is ENUMERATED from
//                         IsCapped, never from a hardcoded resource-name list, so a resource
//                         added by WO-1163 is covered on the day it lands.
//
//   [over-cap-framed]     The published BankOverflowStatus must distinguish above-the-cap
//                         from merely-full, by the NAMED axis OverCap and with a MEASURED
//                         Current, so presentation can tell a paid overflow from a loss.
//                         FAILS IF: OverCap is dropped, wired to a resource name, wired to a
//                         sourceTag string match, or left true at Current == Max -- at which
//                         point the toast tells a full-bank player that their surplus is
//                         "theirs to spend" when in fact it was just discarded.
//
// SAFETY
//   If a live GameStateService exists (it normally does not in batchmode), the WHOLE wallet
//   is snapshotted before the run and RESTORED in a finally, then persisted once. This suite
//   must never leave the owner's save richer or poorer than it found it.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class WO1191OverCapIncomeRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            var gs = GameStateService.Instance;
            var state = gs != null ? gs.State : null;

            // Snapshot the ENTIRE wallet, not just the axis we expect to use -- the vehicle is
            // discovered at runtime, so we cannot know in advance which field will move.
            int priorWood = state != null ? state.Wood : 0;
            int priorIron = state != null ? state.Iron : 0;
            ResourceBalance priorRes = state != null ? state.Resources : default(ResourceBalance);

            GameObject host = null;
            try
            {
                // Prefer the live service when one exists. Standing up a SECOND EconomyService while
                // a singleton is installed walks straight into its Awake duplicate-guard
                // (EconomyService.cs:248 Destroy(gameObject)), and the oracle would then be driving a
                // doomed object. In batchmode there is normally no instance and we make one.
                var econ = EconomyService.Instance;
                if (econ == null)
                {
                    host = new GameObject("WO1191 over-cap income oracle");
                    host.hideFlags = HideFlags.HideAndDontSave;
                    econ = host.AddComponent<EconomyService>();
                }

                CheckMeasuredDeltas(econ, state, failures, notes);
                CheckUncappedUnaffected(failures, notes);
                CheckOverCapFraming(failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add("[over-cap-income] threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // Never leave the owner's save moved by a test.
                if (state != null)
                {
                    state.Wood = priorWood;
                    state.Iron = priorIron;
                    state.Resources = priorRes;
                    if (gs != null) gs.Save();
                }
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }

            if (failures.Count > 0)
            {
                reason = string.Join(" | ", failures);
                return false;
            }
            reason = "over-cap income: paid overflows, earned adds zero above cap, spending restores it, "
                   + "uncapped resources unaffected, and the over-cap state is framed apart from a full bank"
                   + (notes.Count > 0 ? " -- " + string.Join("; ", notes) : "");
            return true;
        }

        // =====================================================================
        //  [paid-overflows] / [earned-adds-zero] / [spend-restores]
        //  MEASURED through the real EconomyService credit seam. Read the wallet,
        //  credit, read the wallet, assert the DELTA -- never the clamp's return.
        // =====================================================================
        private static void CheckMeasuredDeltas(EconomyService econ, GameState state,
                                                List<string> failures, List<string> notes)
        {
            // --- Vehicle discovery, and the docs/HANDOVER.md taxonomy applied to each outcome ---
            //
            // We need a resource that is (a) CAPPED, and (b) MEASURABLE through EconomyService in
            // whatever environment this suite is running in. Both are discovered, never assumed:
            // (a) from TownBankCapacity.IsCapped, (b) by actually moving the wallet one unit and
            // reading it back. The first draft hardcoded Wood for both, which is how it ended up
            // with a stand-down guard in the first place.
            //
            //   NO CAPPED RESOURCE EXISTS AT ALL   -> FIXTURE-ABSENT -> FAIL, naming it.
            //     Justification: this is not the harness being unable to look. The town bank cap is
            //     the subject of the ruling; if nothing is capped, the feature under test is gone and
            //     a green here would assert that section 7 still holds when it no longer has anything
            //     to hold over. That is the "actively asserts the bug is absent" outcome.
            //
            //   CAPPED RESOURCES EXIST, NONE MEASURABLE -> HARNESS-CAPABILITY-ABSENT -> a VISIBLE
            //     stand-down carrying RegressionOutcome.PartialSkipToken.
            //     Justification: the caps are fine and the product is fine; what is missing is a
            //     wallet this environment can read a delta out of (no GameStateService, and the
            //     EconomyService fallback pool backs only some axes). That is a limitation of where
            //     the test is running, not a defect -- exactly the second bucket. PARTIAL, not whole:
            //     the suite still runs [uncapped-unaffected] and [over-cap-framed], and an honest
            //     partial is not the same event as a whole suite standing down.
            //
            //   (There is no content/art-absent case here. Nothing in this suite depends on art or
            //   on authored content that could legitimately be missing, so bucket 3 does not apply.)
            var capped = new List<BankResource>();
            foreach (BankResource br in Enum.GetValues(typeof(BankResource)))
                if (TownBankCapacity.IsCapped(br)) capped.Add(br);

            if (capped.Count == 0)
            {
                failures.Add("[over-cap-income] FIXTURE ABSENT: NO BankResource reports IsCapped()==true, so there is no "
                           + "town bank cap left for income to sit above. The measured cases assert nothing and this suite "
                           + "must not report green over a missing feature -- see `FOUNDATIONAL_RULINGS.md` section 7.");
                return;
            }

            BankResource r = default;
            bool haveVehicle = false;
            var unmeasurable = new List<string>();
            for (int i = 0; i < capped.Count && !haveVehicle; i++)
            {
                if (IsWalletMeasurable(econ, capped[i])) { r = capped[i]; haveVehicle = true; }
                else unmeasurable.Add(TownBankCapacity.DisplayName(capped[i]));
            }

            if (!haveVehicle)
            {
                string why = "no capped resource has a wallet this environment can read a delta out of (tried: "
                           + string.Join(", ", unmeasurable) + "; no GameStateService, and the EconomyService fallback "
                           + "pool does not back these axes). The clamp authority and the over-cap framing were still "
                           + "asserted below; the MEASURED half was not, and this suite is not claiming otherwise.";
                string section = "[over-cap-income] measured wallet-delta cases "
                               + "(paid-overflows / earned-adds-zero / spend-restores)";
                notes.Add(RegressionOutcome.PartialSkip(section, why));
                return;
            }

            string name = TownBankCapacity.DisplayName(r);
            int max = TownBankCapacity.MaxOf(r);
            if (max <= 0 || max == int.MaxValue)
            {
                failures.Add($"[over-cap-income] MaxOf({name}) resolved to {max} -- unusable as a cap, though IsCapped({name}) is true. "
                           + "The two answers disagree.");
                return;
            }

            // Park the wallet exactly AT the cap so the next credit is the interesting one.
            // A DECLINED or short seed means the state this case claims to test was never reached,
            // so it FAILS BY NAME rather than proceeding as if it were paid.
            if (!TrySetWallet(econ, state, r, max, out string seedWhy))
            {
                failures.Add($"[over-cap-income] FIXTURE DID NOT SET UP: could not seed {name} to its cap of {max} -- {seedWhy}. "
                           + "Every case below would have reported on a state that never existed.");
                return;
            }

            // --- [paid-overflows] ------------------------------------------------------------
            const int purchase = 1500;
            int beforePaid = ReadWallet(econ, r);
            econ.GrantPurchased(CostOf(r, purchase));
            int afterPaid = ReadWallet(econ, r);
            int paidDelta = afterPaid - beforePaid;
            if (paidDelta != purchase)
                failures.Add($"[paid-overflows] a PAID credit of {purchase} {name} onto a full wallet moved it by {paidDelta} "
                           + $"({beforePaid} -> {afterPaid}, cap {max}) -- the player was charged for units that never arrived");
            if (afterPaid <= max)
                failures.Add($"[paid-overflows] after a paid credit the wallet is {afterPaid} against a cap of {max} -- "
                           + "the balance was not allowed above the cap at all");

            // --- [earned-adds-zero] ----------------------------------------------------------
            const int earned = 250;
            int beforeEarned = ReadWallet(econ, r);
            if (beforeEarned <= max)
            {
                failures.Add($"[earned-adds-zero] FIXTURE DID NOT SET UP: {name} is {beforeEarned} against a cap of {max}, "
                           + "not above it, so the over-cap state this case exists to measure was never reached");
            }
            else
            {
                econ.Grant(CostOf(r, earned));
                int afterEarned = ReadWallet(econ, r);
                int earnedDelta = afterEarned - beforeEarned;
                if (earnedDelta != 0)
                    failures.Add($"[earned-adds-zero] with {name} at {beforeEarned} against a cap of {max}, an EARNED credit of "
                               + $"{earned} moved the wallet by {earnedDelta} (expected exactly 0). "
                               + (earnedDelta > 0
                                    ? "Earned income is topping up a balance that is already above capacity."
                                    : "An earned credit DRAINED an over-cap balance -- above the cap is a legitimate state, "
                                    + "and nothing may normalise a wallet down to the cap."));
            }

            // --- [spend-restores] ------------------------------------------------------------
            // Spend back UNDER the cap and prove the same earned credit lands again. The point is
            // that suppression is re-evaluated from the live balance, not latched.
            int target = Mathf.Max(0, max - (earned * 2));
            if (!TrySetWallet(econ, state, r, target, out string spendWhy))
            {
                failures.Add($"[spend-restores] FIXTURE DID NOT SET UP: could not bring {name} back down to {target} "
                           + $"(under the cap of {max}) -- {spendWhy}. The resume this case measures was never attempted.");
                return;
            }

            int beforeResume = ReadWallet(econ, r);
            if (beforeResume >= max)
            {
                failures.Add($"[spend-restores] FIXTURE DID NOT SET UP: after spending, {name} is {beforeResume} and the cap "
                           + $"is {max} -- the wallet never came back under");
                return;
            }

            econ.Grant(CostOf(r, earned));
            int afterResume = ReadWallet(econ, r);
            int resumeDelta = afterResume - beforeResume;
            if (resumeDelta != earned)
                failures.Add($"[spend-restores] back under the cap ({beforeResume}/{max}), an EARNED credit of {earned} moved "
                           + $"the wallet by {resumeDelta} (expected {earned}). "
                           + (resumeDelta == 0
                                ? "The earned faucet did NOT restart -- suppression is latched instead of re-read from the balance."
                                : "The credit landed, but not in full, with headroom to spare."));

            notes.Add($"measured through {name}");
        }

        // =====================================================================
        //  Wallet plumbing. These switches map an axis to ITS wallet field and ITS
        //  cost slot -- an identity mapping, NOT a policy list. No line here decides
        //  whether anything is capped; that answer only ever comes from IsCapped.
        // =====================================================================

        private static int ReadWallet(EconomyService econ, BankResource r)
        {
            switch (r)
            {
                case BankResource.Wood:     return econ.Wood;
                case BankResource.Iron:     return econ.Iron;
                case BankResource.Stone:     return econ.Stone;
                case BankResource.Crystals: return econ.Crystals;
                case BankResource.Coins:    return econ.Coins;
            }
            return 0;
        }

        private static ResourceCost CostOf(BankResource r, int amount)
        {
            switch (r)
            {
                case BankResource.Wood:     return new ResourceCost(amount, 0, 0, 0, 0);
                case BankResource.Iron:     return new ResourceCost(0, 0, amount, 0, 0);
                case BankResource.Stone:     return new ResourceCost(0, amount, 0, 0, 0);
                case BankResource.Crystals: return new ResourceCost(0, 0, 0, amount, 0);
                case BankResource.Coins:    return new ResourceCost(0, 0, 0, 0, amount);
            }
            return new ResourceCost(0, 0, 0, 0, 0);
        }

        /// <summary>
        /// Can this environment actually read a wallet DELTA for this axis? Discovered by MOVING
        /// the wallet one unit and reading it back -- never assumed from a resource name, because
        /// which axes the EconomyService fallback pool backs is a property of that class, not of
        /// this test's opinion. The probe uses the DevHarness kind, which is never clamped, so a
        /// full wallet cannot make a measurable axis look unmeasurable. The stray unit is harmless:
        /// TrySetWallet overwrites to an exact target immediately afterwards and verifies it.
        /// </summary>
        private static bool IsWalletMeasurable(EconomyService econ, BankResource r)
        {
            int before = ReadWallet(econ, r);
            econ.GrantUncapped(CostOf(r, 1));
            return ReadWallet(econ, r) - before == 1;
        }

        /// <summary>
        /// Seed the wallet to an EXACT total through whichever store EconomyService is reading.
        /// Returns FALSE with a reason when the fixture could not be established -- notably when
        /// TrySpend DECLINES. THE BOOL IS NEVER DISCARDED: a caller that proceeds after a
        /// declined spend reports on a state that never existed, which is worse than no test at all.
        /// </summary>
        private static bool TrySetWallet(EconomyService econ, GameState state, BankResource r, int value, out string why)
        {
            why = null;
            int delta = value - ReadWallet(econ, r);
            if (delta > 0)
            {
                econ.GrantUncapped(CostOf(r, delta));   // DevHarness -- never clamped
            }
            else if (delta < 0)
            {
                if (!econ.TrySpend(CostOf(r, -delta)))
                {
                    why = $"EconomyService.TrySpend DECLINED a spend of {-delta} "
                        + $"{TownBankCapacity.DisplayName(r)} from a wallet of {ReadWallet(econ, r)}";
                    return false;
                }
            }

            int actual = ReadWallet(econ, r);
            if (actual != value)
            {
                why = $"the wallet reads {actual} after seeding to {value} "
                    + (state == null
                        ? "(no GameStateService -- the EconomyService fallback pool did not take the write)"
                        : "(a GameStateService is installed -- the write did not reach GameState)");
                return false;
            }
            return true;
        }

        // =====================================================================
        //  [uncapped-unaffected]
        //  ENUMERATED from IsCapped -- never a hardcoded resource-name list, so a
        //  resource added by WO-1163 is covered the day it lands.
        // =====================================================================
        private static void CheckUncappedUnaffected(List<string> failures, List<string> notes)
        {
            int uncappedSeen = 0;
            foreach (BankResource r in Enum.GetValues(typeof(BankResource)))
            {
                if (TownBankCapacity.IsCapped(r)) continue;
                uncappedSeen++;

                // HONEST ABOUT THE MEASUREMENT: this leg drives the cap authority directly rather
                // than the wallet, because the uncapped resources live on GameState.Resources and
                // EconomyService reads 0 for them with no save service -- a wallet delta here would
                // be 0 whether the rule held or not, which is worse than no test. What is asserted
                // is the number the credit seam WOULD bank, taken from the same call the seam makes.
                const int ask = 250000;
                int wallet = 999999999;
                int granted = TownBankCapacity.ClampGrant(r, wallet, ask, "wo1191-uncapped", out int lost);
                if (granted != ask || lost != 0)
                    failures.Add($"[uncapped-unaffected] {TownBankCapacity.DisplayName(r)} is uncapped (IsCapped=false) but a credit of "
                               + $"{ask} onto a wallet of {wallet} banked {granted} losing {lost} -- a cap on an uncapped resource "
                               + "has been introduced by implication");

                if (TownBankCapacity.MaxOf(r) != int.MaxValue)
                    failures.Add($"[uncapped-unaffected] MaxOf({TownBankCapacity.DisplayName(r)}) returned a finite ceiling for a "
                               + "resource IsCapped() says has none -- the two answers disagree");
            }

            if (uncappedSeen == 0)
                failures.Add("[uncapped-unaffected] NO resource reports IsCapped()==false any more -- the uncapped exemption is gone");
            else
                notes.Add($"uncapped resources checked: {uncappedSeen}");
        }

        // =====================================================================
        //  [over-cap-framed]
        //  The published status must separate "above the cap" from "bank full" on a
        //  NAMED axis, with a MEASURED Current -- so the toast can stop calling a paid
        //  overflow a loss. This case needs a CAPPED resource but no measurable wallet
        //  (it drives the clamp authority directly), so its only stand-down condition is
        //  "nothing is capped" -- which is FIXTURE-ABSENT and therefore a FAIL, never a skip.
        // =====================================================================
        private static void CheckOverCapFraming(List<string> failures, List<string> notes)
        {
            BankResource r = default;
            bool found = false;
            foreach (BankResource br in Enum.GetValues(typeof(BankResource)))
                if (TownBankCapacity.IsCapped(br)) { r = br; found = true; break; }

            if (!found)
            {
                failures.Add("[over-cap-framed] FIXTURE ABSENT: no BankResource is capped, so no over-cap status can ever be "
                           + "published and the framing this case pins cannot be exercised");
                return;
            }

            string name = TownBankCapacity.DisplayName(r);
            var seen = new List<BankOverflowStatus>();
            Action<BankOverflowStatus> handler = s => seen.Add(s);
            TownBankCapacity.Overflowed += handler;
            try
            {
                int max = TownBankCapacity.MaxOf(r);

                // (a) EXACTLY at the cap: full, but NOT above it. Existing loss framing must stand.
                TownBankCapacity.ClampGrant(r, max, 400, "wo1191-atcap", out _);
                // (b) ABOVE the cap: the state a purchase creates.
                int over = max + 1500;
                TownBankCapacity.ClampGrant(r, over, 400, "wo1191-overcap", out _);

                if (seen.Count != 2)
                {
                    failures.Add($"[over-cap-framed] the Overflowed event fired {seen.Count} time(s) for 2 suppressed {name} grants -- "
                               + "presentation cannot frame what it is never told about");
                    return;
                }

                var atCap = seen[0];
                var above = seen[1];

                if (atCap.OverCap)
                    failures.Add($"[over-cap-framed] a wallet EXACTLY at the cap ({atCap.Current}/{atCap.Max}) published OverCap=true -- "
                               + "a full-bank player would be told the surplus is theirs to spend when it was in fact discarded");
                if (!above.OverCap)
                    failures.Add($"[over-cap-framed] a wallet ABOVE the cap ({above.Current}/{above.Max}) published OverCap=false -- "
                               + "a player who PAID to get up there is told they lost resources and should build a bigger container");

                if (atCap.Current != max)
                    failures.Add($"[over-cap-framed] published Current={atCap.Current} for a grant weighed against {max} -- "
                               + "the status is not reporting the balance the clamp actually used");
                if (above.Current != over)
                    failures.Add($"[over-cap-framed] published Current={above.Current} for a grant weighed against {over}");
                if (above.Current <= above.Max)
                    failures.Add($"[over-cap-framed] OverCap=true was published with Current {above.Current} <= Max {above.Max}");

                if (above.Granted != 0)
                    failures.Add($"[over-cap-framed] above the cap the status reports Granted={above.Granted} (expected 0)");

                notes.Add($"framing asserted on {name}");
            }
            finally
            {
                TownBankCapacity.Overflowed -= handler;
            }
        }
    }
}
