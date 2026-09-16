// =============================================================================
// KillGrantShortfallReasonRegression -- WO-1590. The kill-grant materials warn must
// NAME the cause it was handed, and a full bank must not read as a broken faucet.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Contract: public static bool Run(out string reason). Registered in DataRegression.RunAll
// as [kill-grant-shortfall-reason]. Marker: rolls into REGRESSION_OK <n>/<n> suites.
//
// WHAT WENT WRONG (owner's Seeker session, dg_sunken_vault, 2026-09-07)
//   Every dungeon kill printed, at Warn:
//     [Flow:Reward] KILL GRANT SHORTFALL (materials) id=hollow-warrior askedWood=14
//     bankedWood=14 askedIron=14 bankedIron=14 askedStone=14 bankedStone=0 - a material
//     grant did not land in full (missing EconomyService/GameState, or the town bank cap
//     clamped that axis ...)
//   One sentence, two guessed causes, three materials -- and the bank had ALREADY answered
//   it on the adjacent line, unthrottled:
//     [Flow:Bank] BANK FULL [Grant] Stone: requested 14, banked 0, LOST 14 (wallet
//     34000/34000). Build or upgrade a Stoneyard, or spend stone.
//     [Flow:Bank] MaxOf(stone) = base 2000 + containers 32000 across 1 built container(s)
//   A full Stoneyard. WO-837 / WO-901 working exactly as ruled. The DEFECT was the warn
//   re-guessing instead of reading the applied basket EconomyService.Grant hands back --
//   and it cost WO-1590 its first three hypotheses (zero cap on a fresh town, a retired
//   Food-key migration, a missing Stone column), every one of which the log had already
//   ruled out.
//
// WHAT THIS SUITE PINS
//   [cap-reason]        a clamped axis is narrated as the CAP, and the retired
//                       "missing EconomyService/GameState" guess never appears for it.
//   [swallowed-reason]  applied == asked but banked < asked -- the ONE case that still
//                       earns the missing-service wording -- is narrated as such and is
//                       NOT confused with a full bank.
//   [no-grant-reason]   the -1 sentinel (no grant attempted) is its own sentence.
//   [full-axes-named]   materials that landed in full are named, so the line can never be
//                       misread as "all three failed" -- the exact misreading that happened.
//   [stone-axis-clamp]  the ticket's two outcomes on the CLAMP AUTHORITY, on the Stone
//                       axis specifically, needing no wallet -- so this pin can never
//                       quietly become a stand-down: room banks the ask in full, the
//                       ceiling banks 0 and reports the whole loss, and MaxOf(Stone) is
//                       asserted non-zero (the ticket's own first hypothesis).
//   [uncapped-banks-full] MEASURED: with headroom, a Stone (Food axis) grant through the
//                       real EconomyService banks in full and the applied basket equals
//                       the ask. This is the ticket's "an uncapped store banks 8/8".
//   [capped-banks-zero] MEASURED: with the wallet parked at MaxOf(Food), the same grant
//                       applies 0, moves the wallet 0, and the composed sentence names the
//                       cap. This is the ticket's "a capped store reports the cap reason".
//   [guess-string-retired] source lint: Enemy.cs routes the warn through
//                       Enemy.DescribeMaterialShortfall and no longer carries the old
//                       blanket guess string.
//
// WHY THE WORDING IS TESTED AT ALL, NOT JUST THE MECHANICS
//   The mechanics were never broken -- ClampGrant returned the right number and Grant
//   returned the right basket the whole time. What was broken was the SENTENCE, and a
//   sentence that misdiagnoses itself costs a debugging session, which is precisely what
//   it cost here. So the oracle is the composed string.
//
// STAND-DOWN TAXONOMY (docs/HANDOVER.md). The pure cases need no fixture and always run.
//   The two MEASURED cases need a wallet this environment can move: with no GameStateService
//   they carry a VISIBLE RegressionOutcome.PartialSkip rather than a silent green. A cap
//   that resolves to 0 or int.MaxValue on the Food axis is NOT a stand-down -- it is a FAIL,
//   because TownBankCapacity Law 2 says a cap can never resolve to zero and the whole
//   incident is about that axis.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class KillGrantShortfallReasonRegression
    {
        /// <summary>The retired blanket guess. Its presence in the materials warn is the defect.</summary>
        private const string RetiredGuess = "missing EconomyService/GameState";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            try
            {
                CheckComposedReasons(failures, notes);
                CheckStoneAxisClamp(failures, notes);
                CheckMeasuredStoneGrant(failures, notes);
                CheckSourceRouting(failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add("[kill-grant-shortfall-reason] threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = string.Join(" | ", failures);
                return false;
            }
            reason = "kill-grant shortfall: a clamped axis names the bank cap, a swallowed write names the "
                   + "service, full axes are named as full, the Stone axis banks the ask with room and 0 at "
                   + "its ceiling, and Enemy.cs routes the warn through the composer"
                   + (notes.Count > 0 ? " -- " + string.Join("; ", notes) : "");
            return true;
        }

        // =====================================================================
        //  [cap-reason] / [swallowed-reason] / [no-grant-reason] / [full-axes-named]
        //  PURE -- the composer is a static on Enemy precisely so the death path's
        //  wording is drivable without a death.
        // =====================================================================
        private static void CheckComposedReasons(List<string> failures, List<string> notes)
        {
            // The owner's exact frame: Wood and Iron landed, Stone was clamped to 0 by a full bank.
            string capped = Enemy.DescribeMaterialShortfall(
                "hollow-warrior",
                askedWood: 14, bankedWood: 14, appliedWood: 14,
                askedIron: 14, bankedIron: 14, appliedIron: 14,
                askedStone: 14, bankedStone: 0, appliedStone: 0);

            if (capped.IndexOf(RetiredGuess, StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("[cap-reason] a CLAMPED Stone axis is still narrated with the retired guess \""
                           + RetiredGuess + "\" -- that sentence sent WO-1590 after three causes the "
                           + "[Flow:Bank] BANK FULL line had already ruled out");
            if (capped.IndexOf("BANK FULL", StringComparison.Ordinal) < 0)
                failures.Add("[cap-reason] the composed warn does not say BANK FULL for an axis whose applied "
                           + "amount was below the ask: " + capped);
            if (capped.IndexOf("Stone", StringComparison.Ordinal) < 0)
                failures.Add("[cap-reason] the composed warn does not NAME Stone as the clamped material: " + capped);
            if (capped.IndexOf("Flow:Bank", StringComparison.Ordinal) < 0)
                failures.Add("[cap-reason] the composed warn does not point at the [Flow:Bank] line that carries "
                           + "the wallet/ceiling and the container to upgrade -- the reader is left to find it");

            // [full-axes-named] -- the misreading that actually happened: three materials on one
            // line, one sentence, so it read as though all three had failed.
            if (capped.IndexOf("Wood: banked 14/14 in full", StringComparison.Ordinal) < 0)
                failures.Add("[full-axes-named] Wood banked in full but the warn does not say so: " + capped);
            if (capped.IndexOf("Iron: banked 14/14 in full", StringComparison.Ordinal) < 0)
                failures.Add("[full-axes-named] Iron banked in full but the warn does not say so: " + capped);

            // The line must still carry the raw numbers -- the triage above was done off them.
            if (capped.IndexOf("askedStone=14", StringComparison.Ordinal) < 0
                || capped.IndexOf("bankedStone=0", StringComparison.Ordinal) < 0)
                failures.Add("[cap-reason] the asked/banked figures were dropped from the warn; the reason is an "
                           + "ADDITION to the evidence, never a replacement for it: " + capped);

            // [swallowed-reason] -- the economy service applied it in full and it still did not
            // reach the wallet. THIS is the missing-service case, and it must not read as a cap.
            string swallowed = Enemy.DescribeMaterialShortfall(
                "hollow-walker",
                askedWood: 6, bankedWood: 6, appliedWood: 6,
                askedIron: 6, bankedIron: 6, appliedIron: 6,
                askedStone: 6, bankedStone: 0, appliedStone: 6);

            if (swallowed.IndexOf("BANK FULL", StringComparison.Ordinal) >= 0)
                failures.Add("[swallowed-reason] a grant the economy service applied IN FULL was narrated as a "
                           + "full bank -- the two causes have opposite fixes: " + swallowed);
            if (swallowed.IndexOf("swallowed", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add("[swallowed-reason] applied 6 but banked 0 is not narrated as a swallowed write: " + swallowed);
            if (swallowed.IndexOf("GameStateService", StringComparison.Ordinal) < 0)
                failures.Add("[swallowed-reason] the one case that IS a missing-service failure does not name "
                           + "GameStateService as a candidate: " + swallowed);

            // [no-grant-reason] -- the -1 sentinel. Never silently folded into "applied 0".
            string none = Enemy.DescribeMaterialShortfall(
                "hollow-brute",
                askedWood: 40, bankedWood: 0, appliedWood: -1,
                askedIron: 40, bankedIron: 0, appliedIron: -1,
                askedStone: 40, bankedStone: 0, appliedStone: -1);

            if (none.IndexOf("BANK FULL", StringComparison.Ordinal) >= 0)
                failures.Add("[no-grant-reason] a kill where NO grant was attempted was narrated as a full bank: " + none);
            if (none.IndexOf("no grant was attempted", StringComparison.Ordinal) < 0)
                failures.Add("[no-grant-reason] the -1 sentinel is not narrated as \"no grant was attempted\": " + none);

            // Nothing may ever be silent. A material with no clause is the ambiguity this replaced.
            foreach (string mat in new[] { "Wood", "Iron", "Stone" })
            {
                if (capped.IndexOf(mat + ":", StringComparison.Ordinal) < 0)
                    failures.Add("[full-axes-named] " + mat + " has no clause at all in the composed warn: " + capped);
            }

            notes.Add("composed reasons asserted for cap / swallowed / no-grant");
        }

        // =====================================================================
        //  [stone-axis-clamp] -- the ticket's two outcomes, on the CLAMP AUTHORITY.
        //  This case needs no wallet and therefore NEVER stands down: whatever
        //  environment the suite runs in, "a store with room banks the ask in full"
        //  and "a store at its ceiling banks zero" are both asserted on the Stone
        //  axis specifically. The wallet-measured pair below is the stronger proof
        //  when a GameStateService exists; this is the floor under it, so the pin
        //  the ticket asked for can never quietly become a skip.
        // =====================================================================
        private static void CheckStoneAxisClamp(List<string> failures, List<string> notes)
        {
            const BankResource Stone = BankResource.Stone;   // WO-1212: Stone rides the Food axis
            const int Ask = 8;                              // the ticket's own number

            if (!TownBankCapacity.IsCapped(Stone))
            {
                failures.Add("[stone-axis-clamp] FIXTURE ABSENT: IsCapped(Food/Stone) is false. The axis this whole "
                           + "incident is about is no longer storage-capped, so a green here would assert that the "
                           + "cap behaves correctly when there is no cap left to behave.");
                return;
            }

            int max = TownBankCapacity.MaxOf(Stone);
            if (max <= 0 || max == int.MaxValue)
            {
                failures.Add($"[stone-axis-clamp] MaxOf(Stone) resolved to {max}. TownBankCapacity Law 2 says a cap can "
                           + "NEVER resolve to zero (BaseCapOf floors every answer at AbsoluteMinBaseCap), and "
                           + "IsCapped(Stone) is true -- the two authorities disagree. This is also the ticket's own "
                           + "first hypothesis (\"the Stone cap is 0 without a Quarry\"), so it is asserted, not assumed.");
                return;
            }

            // A store with room: the ask arrives in full and NOTHING is reported lost.
            int withRoom = TownBankCapacity.ClampGrant(Stone, 0, Ask, "kill-grant-oracle", out int lostWithRoom);
            if (withRoom != Ask || lostWithRoom != 0)
                failures.Add($"[stone-axis-clamp] an empty Stone store banked {withRoom} of {Ask} (lost {lostWithRoom}) "
                           + $"against a ceiling of {max} -- an earned kill grant into a store with room must arrive whole");

            // A store at its ceiling: zero banks, the whole ask is reported lost. This is the
            // owner's device state (wallet 34000/34000) and it is the CAP working, not a defect.
            int atCap = TownBankCapacity.ClampGrant(Stone, max, Ask, "kill-grant-oracle", out int lostAtCap);
            if (atCap != 0)
                failures.Add($"[stone-axis-clamp] a Stone store at its ceiling ({max}/{max}) banked {atCap} of {Ask} "
                           + "(expected 0) -- the bank accepted above its own cap");
            if (lostAtCap != Ask)
                failures.Add($"[stone-axis-clamp] at the ceiling the clamp reported {lostAtCap} lost of {Ask} -- the "
                           + "loss must be reported in full or the player's resources vanish silently (WO-901 §5)");

            // And the sentence the player's log gets from exactly those numbers.
            string sentence = Enemy.DescribeMaterialShortfall("oracle",
                0, 0, -1, 0, 0, -1, Ask, 0, atCap);
            if (sentence.IndexOf("BANK FULL", StringComparison.Ordinal) < 0)
                failures.Add("[stone-axis-clamp] the clamp authority's own numbers do not compose a BANK FULL reason: " + sentence);
            if (sentence.IndexOf(RetiredGuess, StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("[stone-axis-clamp] the clamp authority's own numbers still compose the retired guess \""
                           + RetiredGuess + "\": " + sentence);

            notes.Add($"Stone axis clamp asserted against a live ceiling of {max}");
        }

        // =====================================================================
        //  [uncapped-banks-full] / [capped-banks-zero]
        //  MEASURED through the real EconomyService on the Stone (Food) axis --
        //  the applied basket AND the wallet delta, never the clamp's return value.
        // =====================================================================
        private static void CheckMeasuredStoneGrant(List<string> failures, List<string> notes)
        {
            const BankResource Stone = BankResource.Stone;   // WO-1212: Stone rides the Food axis
            const int Ask = 8;                              // the ticket's own number

            var gs = GameStateService.Instance;
            var state = gs != null ? gs.State : null;
            if (state == null)
            {
                notes.Add(RegressionOutcome.PartialSkip(
                    "[kill-grant-shortfall-reason] measured Stone grant cases (uncapped-banks-full / capped-banks-zero)",
                    "no GameStateService is installed in this environment, so there is no Food-axis wallet to read a "
                    + "delta out of. EconomyService.Food reads through GameState and returns 0 without it, which would "
                    + "make every assertion below vacuously true. The composed-reason and source-routing cases still ran."));
                return;
            }

            if (!TownBankCapacity.IsCapped(Stone))
            {
                failures.Add("[capped-banks-zero] FIXTURE ABSENT: IsCapped(Food/Stone) is false, so the axis this whole "
                           + "incident is about is no longer storage-capped. A green here would assert the cap behaves "
                           + "when there is no cap.");
                return;
            }

            int max = TownBankCapacity.MaxOf(Stone);
            if (max <= 0 || max == int.MaxValue)
            {
                failures.Add($"[capped-banks-zero] MaxOf(Stone) resolved to {max} -- TownBankCapacity Law 2 says a cap "
                           + "can NEVER resolve to zero (BaseCapOf floors it), and IsCapped(Stone) is true, so the two "
                           + "authorities disagree.");
                return;
            }

            ResourceBalance prior = state.Resources;
            GameObject host = null;
            try
            {
                // Prefer the live singleton: a second EconomyService walks into its own Awake
                // duplicate-guard and the oracle would be driving a doomed object.
                var econ = EconomyService.Instance;
                if (econ == null)
                {
                    host = new GameObject("WO1590 kill-grant stone oracle");
                    host.hideFlags = HideFlags.HideAndDontSave;
                    econ = host.AddComponent<EconomyService>();
                }

                // --- [uncapped-banks-full] : plenty of headroom -----------------------------
                if (!TrySetFood(state, 0, out string seedWhy))
                {
                    failures.Add("[uncapped-banks-full] FIXTURE DID NOT SET UP: could not seed Stone to 0 -- " + seedWhy
                               + ". Both measured cases would have reported on a state that never existed.");
                    return;
                }

                int beforeRoom = econ.Stone;
                var appliedRoom = econ.Grant(new ResourceCost(wood: 0, stone: Ask, iron: 0));
                int afterRoom = econ.Stone;

                if (appliedRoom.Stone != Ask)
                    failures.Add($"[uncapped-banks-full] with {max} headroom the applied basket reports {appliedRoom.Stone} "
                               + $"of {Ask} Stone -- an earned grant into an empty store must arrive in full");
                if (afterRoom - beforeRoom != Ask)
                    failures.Add($"[uncapped-banks-full] the WALLET moved {afterRoom - beforeRoom} for a {Ask} Stone grant "
                               + $"({beforeRoom} -> {afterRoom}) -- the applied basket and the wallet disagree");

                // With nothing lost there must be no shortfall sentence at all to compose.
                if (afterRoom - beforeRoom >= Ask && appliedRoom.Stone >= Ask)
                {
                    string clean = Enemy.DescribeMaterialShortfall("oracle",
                        0, 0, -1, 0, 0, -1, Ask, afterRoom - beforeRoom, appliedRoom.Stone);
                    if (clean.IndexOf("BANK FULL", StringComparison.Ordinal) >= 0)
                        failures.Add("[uncapped-banks-full] a grant that banked in full still composes a BANK FULL "
                                   + "sentence -- a false cap alarm is as misleading as a missed one: " + clean);
                }

                // --- [capped-banks-zero] : wallet parked exactly at the cap ------------------
                if (!TrySetFood(state, max, out string capWhy))
                {
                    failures.Add($"[capped-banks-zero] FIXTURE DID NOT SET UP: could not seed Stone to its cap of {max} -- "
                               + capWhy + ". The case would have reported on a state that never existed.");
                    return;
                }

                int beforeFull = econ.Stone;
                var appliedFull = econ.Grant(new ResourceCost(wood: 0, stone: Ask, iron: 0));
                int afterFull = econ.Stone;

                if (appliedFull.Stone != 0)
                    failures.Add($"[capped-banks-zero] at the cap ({beforeFull}/{max}) the applied basket reports "
                               + $"{appliedFull.Stone} Stone banked of {Ask} (expected 0)");
                if (afterFull != beforeFull)
                    failures.Add($"[capped-banks-zero] at the cap the wallet moved {afterFull - beforeFull} "
                               + $"({beforeFull} -> {afterFull}) -- the bank accepted above its own ceiling");

                // THE POINT OF THE TICKET: those real numbers must compose the CAP sentence, not the guess.
                string composed = Enemy.DescribeMaterialShortfall("oracle",
                    0, 0, -1, 0, 0, -1, Ask, afterFull - beforeFull, appliedFull.Stone);
                if (composed.IndexOf("BANK FULL", StringComparison.Ordinal) < 0)
                    failures.Add("[capped-banks-zero] the MEASURED cap outcome does not compose a BANK FULL reason: " + composed);
                if (composed.IndexOf(RetiredGuess, StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[capped-banks-zero] the MEASURED cap outcome still composes the retired guess \""
                               + RetiredGuess + "\": " + composed);

                notes.Add($"measured on the Food/Stone axis against a live cap of {max}");
            }
            finally
            {
                // Never leave the owner's save moved by a test.
                state.Resources = prior;
                if (gs != null) gs.Save();
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>Seed the Food (Stone) balance directly on the wallet struct and read it back.
        /// Returns false WITH A REASON when the write did not take -- a fixture that silently
        /// failed to set up is how a suite reports on a state that never existed.</summary>
        private static bool TrySetFood(GameState state, int value, out string why)
        {
            why = null;
            var r = state.Resources;
            r.Stone = Mathf.Max(0, value);
            state.Resources = r;
            int readBack = TownBankCapacity.CurrentOf(BankResource.Stone);
            if (readBack != Mathf.Max(0, value))
            {
                why = $"wrote {value} to GameState.Resources.Stone and TownBankCapacity.CurrentOf(Food) read back {readBack}";
                return false;
            }
            return true;
        }

        // =====================================================================
        //  [guess-string-retired] -- the warn is actually routed through the composer.
        //  A source lint, because the death path cannot be driven from EditMode: the
        //  composer could be perfect and still not be the thing the player's log gets.
        // =====================================================================
        private static void CheckSourceRouting(List<string> failures, List<string> notes)
        {
            string path = Path.Combine(Application.dataPath, "_Modules/Village/Enemies/Enemy.cs");
            if (!File.Exists(path))
            {
                failures.Add("[guess-string-retired] Assets/_Modules/Village/Enemies/Enemy.cs is MISSING -- the kill "
                           + "grant warn cannot be verified to route through the composer");
                return;
            }

            string src = File.ReadAllText(path);

            // At least TWO occurrences: the declaration, and the death path's call. Testing for
            // one would be satisfied by the declaration alone -- i.e. by a composer that is
            // perfectly worded, fully tested, and dead code that the player's log never sees.
            int composerRefs = 0;
            for (int i = src.IndexOf("DescribeMaterialShortfall(", StringComparison.Ordinal); i >= 0;
                 i = src.IndexOf("DescribeMaterialShortfall(", i + 1, StringComparison.Ordinal))
                composerRefs++;
            if (composerRefs < 2)
                failures.Add($"[guess-string-retired] Enemy.cs names DescribeMaterialShortfall {composerRefs} time(s) "
                           + "(expected the declaration plus at least one call site) -- the composed reason is dead "
                           + "code and the log the owner actually reads is unchanged");

            // The warn header must be authored in exactly ONE place -- the composer. A second
            // occurrence means the death path re-inlined its own wording beside the composed one,
            // and the two would drift: the duplicated-state failure this repo keeps paying for.
            int occurrences = 0;
            for (int i = src.IndexOf("KILL GRANT SHORTFALL (materials)", StringComparison.Ordinal); i >= 0;
                 i = src.IndexOf("KILL GRANT SHORTFALL (materials)", i + 1, StringComparison.Ordinal))
                occurrences++;
            if (occurrences != 1)
                failures.Add($"[guess-string-retired] the literal \"KILL GRANT SHORTFALL (materials)\" appears "
                           + $"{occurrences} time(s) in Enemy.cs (expected exactly 1, inside DescribeMaterialShortfall) "
                           + "-- 0 means the warn lost its header, >1 means a second author of the same sentence");

            foreach (string line in src.Split('\n'))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("///", StringComparison.Ordinal))
                    continue;
                if (line.IndexOf(RetiredGuess, StringComparison.Ordinal) >= 0)
                {
                    failures.Add("[guess-string-retired] the retired blanket guess \"" + RetiredGuess + "\" is back in "
                               + "live Enemy.cs code (not a comment). It narrates a full bank as a broken service.");
                    break;
                }
            }

            notes.Add("Enemy.cs routes the materials warn through the composer");
        }
    }
}
