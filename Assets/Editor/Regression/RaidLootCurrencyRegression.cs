// =============================================================================
// RaidLootCurrencyRegression [raid-loot-currency]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Markers: RAID_LOOT_CURRENCY_OK / _FAIL.
//
// WO-1374. Pins the half of the raid reward table that is correct under BOTH
// sides of the blocked troop-cost fork: RAIDS PAY WOOD AND IRON, on the
// north-star map's performance ladder, and THEY STILL PAY NO GOLD.
//
// Spec: docs/PROGRAM_RAID_ECONOMY_2026-09-04.md sections 1 and 12.
//
// -----------------------------------------------------------------------------
// PROVEN RED FIRST - what each case asserts and what the PREVIOUS code returned.
// -----------------------------------------------------------------------------
// Before this work RaidScoring.ComputeLoot ended with
//     return new ResourceCost(food: food, crystals: crystals);
// so wood and iron were STRUCTURALLY ZERO on every path. Every case in group (A)
// therefore fails against that build by construction, not by coincidence:
//   A1 expects 1800 wood at 3 stars      -> old code returned 0.
//   A2 expects a non-zero payout on a LOSS -> old code returned 0.
//   A5 expects the ladder to be MONOTONIC over five rungs -> old code returned a
//      flat 0 at every rung, which is not monotonic-increasing and reds.
// Group (C) is the mirror image and had to be RED-checked the other way: it
// asserts the crystals/food arithmetic is UNCHANGED, so it would have gone red if
// this ticket had "tidied" those two axes onto the new ladder while it was in
// there. That is the failure it exists to catch.
//
// -----------------------------------------------------------------------------
// (!) THE GOLD FENCE IS GONE. THE FORK IT GUARDED WAS CLOSED (commit 281902df0).
// -----------------------------------------------------------------------------
// This header used to explain that case (D) failed the build the moment a raid
// paid a single coin, because WO-1372 (troops cost TIME) and the map (troops cost
// 1,650 GOLD) contradicted each other. They no longer do: troops COST GOLD, they
// ALSO take time, and a SECOND gold spend hires mercenaries to skip the clock.
// Case (D) is therefore INVERTED, not deleted - it now fails if a raid pays NO
// gold, because a zero there is the map's one explicitly named missing arrow
// being deleted again: "You currently have Gold -> troops but not troops ->
// raids -> gold. That arrow has to exist."
//
// The per-camp gold table, the crystal cut and the two multiplier exclusions are
// pinned by RaidGoldArrowRegression [raid-gold-arrow], which is where the NEW
// behaviour is asserted. This suite keeps the wood/iron ladder it was written for.
//
// Zero scene, zero save, zero network, zero PlayMode: ComputeLoot and
// RaidLootTunables.FractionFrom are pure statics, which is why they were written
// that way.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Core.Ops;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pins the WO-1374 raid payout: wood + iron on the map's ladder, crystals and
    /// food untouched, gold still zero. Returns true (summary) / false (detail).
    /// Never throws.
    /// </summary>
    public static class RaidLootCurrencyRegression
    {
        // ---- The map's numbers, as LITERALS. Never RemoteTunables.Int(...) - an
        // oracle that reads the value it is checking certifies nothing.
        private const int MapWoodBase = 1800;
        private const int MapIronBase = 1100;
        private const int MapFailPct = 18;
        private const int MapOneStarPct = 50;
        private const int MapTwoStarPct = 75;
        private const int MapThreeStarPct = 100;
        private const int MapPerfectPct = 110;
        private const int MapStarterArmySize = 3;
        // The map's Camp I gold target, sized at 125-140% of that camp's designed
        // 1,650-gold army replacement cost. A literal, never RemoteTunables.Int.
        private const int MapCoinsCamp1 = 2200;

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RAID LOOT CURRENCY (WO-1374, map sections 1 + 12) ---");

            // =================================================================
            //  (A) WOOD AND IRON ARE PAID, ON THE LADDER.
            // =================================================================

            // A perfect run: 3 stars AND a total razing -> the top rung, 110% of base.
            var perfect = RaidScoring.ComputeLoot(3, 1f, 25, 60, 10, 20, 1f, MapWoodBase, MapIronBase);
            int expectPerfectWood = Mathf.RoundToInt(MapWoodBase * (MapPerfectPct / 100f));
            int expectPerfectIron = Mathf.RoundToInt(MapIronBase * (MapPerfectPct / 100f));
            log.AppendLine("  perfect(3*,100%) -> " + perfect.Wood + "w " + perfect.Iron + "i");
            if (perfect.Wood != expectPerfectWood)
                failures.Add("[A1] a perfect raid paid " + perfect.Wood + " wood, expected " +
                             expectPerfectWood + " (base " + MapWoodBase + " x " + MapPerfectPct + "%)");
            if (perfect.Iron != expectPerfectIron)
                failures.Add("[A1] a perfect raid paid " + perfect.Iron + " iron, expected " +
                             expectPerfectIron);

            // A 3-star clear that did NOT raze everything is the 100% rung: the base itself.
            var threeStar = RaidScoring.ComputeLoot(3, 0.80f, 25, 60, 10, 20, 1f, MapWoodBase, MapIronBase);
            if (threeStar.Wood != MapWoodBase)
                failures.Add("[A1] a 3-star (80% razed) raid paid " + threeStar.Wood + " wood, expected the " +
                             "base " + MapWoodBase + " - the 3-star rung is 100% and IS what the base means");
            if (threeStar.Iron != MapIronBase)
                failures.Add("[A1] a 3-star (80% razed) raid paid " + threeStar.Iron + " iron, expected " + MapIronBase);

            // ⛔ A LOSS STILL PAYS. The map: "A failed attack still pays 15-20%. That is
            // deliberate - it keeps a loss from being a dead end." A zero here is not a
            // rounding detail, it is the retention rule being deleted.
            var lost = RaidScoring.ComputeLoot(0, 0.10f, 25, 60, 10, 20, 1f, MapWoodBase, MapIronBase);
            log.AppendLine("  failed attack -> " + lost.Wood + "w " + lost.Iron + "i");
            if (lost.Wood <= 0)
                failures.Add("[A2] a FAILED attack paid 0 wood. The map rules that a loss pays 15-20% " +
                             "on purpose, so that losing is not a dead end. Do not 'fix' this to zero.");
            if (lost.Iron <= 0)
                failures.Add("[A2] a FAILED attack paid 0 iron - see [A2] above.");
            int expectFailWood = Mathf.RoundToInt(MapWoodBase * (MapFailPct / 100f));
            if (lost.Wood != expectFailWood)
                failures.Add("[A2] a failed attack paid " + lost.Wood + " wood, expected " + expectFailWood +
                             " (" + MapFailPct + "% of " + MapWoodBase + ")");

            // A3 - the camp difficulty multiplier reaches the new axes too, or the
            // selection card's "x1.5 Loot" is a lie on two of the five resources.
            var hard = RaidScoring.ComputeLoot(3, 1f, 25, 60, 10, 20, 1.5f, MapWoodBase, MapIronBase);
            if (hard.Wood <= perfect.Wood)
                failures.Add("[A3] the x1.5 camp multiplier did not scale wood (x1 " + perfect.Wood +
                             " vs x1.5 " + hard.Wood + ") - the card advertises a bonus the payout withholds");
            if (hard.Iron <= perfect.Iron)
                failures.Add("[A3] the x1.5 camp multiplier did not scale iron (x1 " + perfect.Iron +
                             " vs x1.5 " + hard.Iron + ")");

            // A4 - BACKWARD COMPATIBILITY. The pre-WO-1374 call shape (no wood/iron bases)
            // must pay exactly what it always paid: nothing on those two axes. This is what
            // lets the two EditMode tests and RaidScoringRegression keep asserting the old
            // contract truthfully instead of being quietly re-pointed.
            var legacy = RaidScoring.ComputeLoot(3, 1f, 40, 60, 15, 20);
            if (legacy.Wood != 0 || legacy.Iron != 0)
                failures.Add("[A4] the 6-argument ComputeLoot shape paid " + legacy.Wood + "w/" + legacy.Iron +
                             "i - it must stay a food-and-crystals-only payout so existing callers are unchanged");

            // A5 - THE LADDER IS MONOTONIC ACROSS ALL FIVE RUNGS. This is the property that
            // makes "getting better at raiding has an economic payoff" true; a flat or
            // inverted rung is the defect and it is invisible in any single case.
            int wFail = RaidScoring.ComputeLoot(0, 0.3f, 0, 0, 0, 0, 1f, MapWoodBase, 0).Wood;
            int wOne = RaidScoring.ComputeLoot(1, 0.5f, 0, 0, 0, 0, 1f, MapWoodBase, 0).Wood;
            int wTwo = RaidScoring.ComputeLoot(2, 0.7f, 0, 0, 0, 0, 1f, MapWoodBase, 0).Wood;
            int wThree = RaidScoring.ComputeLoot(3, 0.8f, 0, 0, 0, 0, 1f, MapWoodBase, 0).Wood;
            int wPerf = RaidScoring.ComputeLoot(3, 1.0f, 0, 0, 0, 0, 1f, MapWoodBase, 0).Wood;
            log.AppendLine("  ladder wood: fail " + wFail + " < 1* " + wOne + " < 2* " + wTwo +
                           " < 3* " + wThree + " < perfect " + wPerf);
            if (!(wFail < wOne && wOne < wTwo && wTwo < wThree && wThree < wPerf))
                failures.Add("[A5] the performance ladder is not strictly increasing: fail " + wFail +
                             " / 1* " + wOne + " / 2* " + wTwo + " / 3* " + wThree + " / perfect " + wPerf +
                             ". Without this, raiding better pays no better and the whole programme is inert.");

            // A6 - the PURE ladder function, driven directly with the map's rungs so the
            // table is asserted independently of the knobs the game happens to resolve.
            AssertFraction(failures, "fail", 0, 0.0f, MapFailPct / 100f);
            AssertFraction(failures, "1 star", 1, 0.5f, MapOneStarPct / 100f);
            AssertFraction(failures, "2 stars", 2, 0.7f, MapTwoStarPct / 100f);
            AssertFraction(failures, "3 stars, partial razing", 3, 0.9f, MapThreeStarPct / 100f);
            AssertFraction(failures, "3 stars, total razing", 3, 1.0f, MapPerfectPct / 100f);
            // Destruction must NOT move a sub-3-star rung: the ladder is by RESULT, and a
            // second continuous axis smuggled in here is how a 2-star run starts paying
            // like a 3-star one.
            AssertFraction(failures, "2 stars at 100% razed is still the 2-star rung", 2, 1.0f, MapTwoStarPct / 100f);
            // Out-of-range stars clamp rather than throw or index off the end.
            AssertFraction(failures, "stars clamped low", -5, 0.5f, MapFailPct / 100f);
            AssertFraction(failures, "stars clamped high", 99, 1.0f, MapPerfectPct / 100f);

            // =================================================================
            //  (B) THE KNOB DEFAULTS ARE THE MAP'S NUMBERS.
            // =================================================================
            // Asserted against LITERALS above, not against RemoteTunables' own constants:
            // this is the statement that the shipping build pays what the owner asked for,
            // and it must be able to disagree with the code.
            AssertDefault(failures, RemoteTunables.KeyRaidLootWoodBase, MapWoodBase);
            AssertDefault(failures, RemoteTunables.KeyRaidLootIronBase, MapIronBase);
            AssertDefault(failures, RemoteTunables.KeyRaidLootFailPct, MapFailPct);
            AssertDefault(failures, RemoteTunables.KeyRaidLootOneStarPct, MapOneStarPct);
            AssertDefault(failures, RemoteTunables.KeyRaidLootTwoStarPct, MapTwoStarPct);
            AssertDefault(failures, RemoteTunables.KeyRaidLootThreeStarPct, MapThreeStarPct);
            AssertDefault(failures, RemoteTunables.KeyRaidLootPerfectPct, MapPerfectPct);
            AssertDefault(failures, RemoteTunables.KeyRaidStarterArmySize, MapStarterArmySize);

            // The map is explicit that the failed-attack rung sits in a BAND, not on a
            // point, so the oracle checks the band as well as the shipped value - a future
            // retune to 15 or 20 stays green, a retune to 0 or 60 does not.
            if (MapFailPct < 15 || MapFailPct > 20)
                failures.Add("[B] the failed-attack rung is " + MapFailPct + "%, outside the map's stated 15-20% band");

            // =================================================================
            //  (C) THE CRYSTAL/FOOD SHAPE IS UNCHANGED (crystals lost only the multiplier).
            // =================================================================
            // The original SHAPE, re-derived here independently. If someone folds these
            // two axes onto the new ladder, this reds.
            //
            // (!) CORRECTED 2026-09-04. Crystals no longer ride the camp rewardMultiplier
            // - the map rules them out of it, because a harder camp must pay more gold,
            // wood and iron, never more timer compression. AssertLegacyAxes reflects that;
            // FOOD still carries the multiplier and is still checked with it.
            AssertLegacyAxes(failures, 0, 0f, 25, 60, 10, 20, 1f);
            AssertLegacyAxes(failures, 1, 0.5f, 25, 60, 10, 20, 1f);
            AssertLegacyAxes(failures, 3, 1f, 25, 60, 10, 20, 1f);
            AssertLegacyAxes(failures, 2, 0.75f, 40, 60, 15, 20, 1.5f);
            // And the presence of a wood/iron base must not perturb them either.
            var withBases = RaidScoring.ComputeLoot(2, 0.75f, 25, 60, 10, 20, 1f, MapWoodBase, MapIronBase);
            var withoutBases = RaidScoring.ComputeLoot(2, 0.75f, 25, 60, 10, 20, 1f, 0, 0);
            if (withBases.Crystals != withoutBases.Crystals || withBases.Stone != withoutBases.Stone)
                failures.Add("[C] adding a wood/iron base changed the crystals/food payout (" +
                             withoutBases.Crystals + "c/" + withoutBases.Stone + "f -> " +
                             withBases.Crystals + "c/" + withBases.Stone + "f) - the two axes must be independent");

            // =================================================================
            //  (D) THE ARROW. GOLD IS PAID, ON THE SAME LADDER.
            // =================================================================
            // (!) THIS CASE IS INVERTED, NOT DELETED. It used to fail the moment a raid
            // paid a single coin, because WO-1374 was blocked on the troop-cost fork.
            // That fork was CLOSED at commit 281902df0 - troops cost gold AND time, and a
            // second gold spend hires mercenaries. So the failure mode worth guarding is
            // now the opposite one: a build that silently stops paying gold has deleted
            // the map's one explicitly named missing arrow, and nothing on screen says so.
            for (int stars = 0; stars <= 3; stars++)
            {
                for (int d = 0; d <= 10; d++)
                {
                    float destruction = d / 10f;
                    var any = RaidScoring.ComputeLoot(stars, destruction, 20, 60, 2, 20, 2.2f,
                                                      MapWoodBase, MapIronBase, MapCoinsCamp1);
                    float ladder = RaidLootTunables.FractionFrom(stars, destruction,
                        MapFailPct, MapOneStarPct, MapTwoStarPct, MapThreeStarPct, MapPerfectPct);
                    int want = Mathf.RoundToInt(MapCoinsCamp1 * ladder);
                    if (any.Coins != want)
                    {
                        failures.Add("[D] a raid paid " + any.Coins + " GOLD at stars=" + stars +
                                     " destruction=" + (d * 10) + "%, expected " + want + " (base " +
                                     MapCoinsCamp1 + " x the " + (ladder * 100f).ToString("0.#") +
                                     "% ladder rung, and NOT the x2.2 camp multiplier). Gold is the " +
                                     "map's one explicitly named missing arrow: troops -> raids -> gold.");
                        break;
                    }
                }
                if (failures.Count > 0 && failures[failures.Count - 1].StartsWith("[D]")) break;
            }

            // (D1) The pre-existing 9-argument shape still pays NO gold, so every caller
            // that has not opted in is byte-unchanged.
            var noCoins = RaidScoring.ComputeLoot(3, 1f, 20, 60, 2, 20, 1f, MapWoodBase, MapIronBase);
            if (noCoins.Coins != 0)
                failures.Add("[D1] the 9-argument ComputeLoot shape paid " + noCoins.Coins + " gold - " +
                             "gold must be opt-in through the trailing coinsBase parameter, or every " +
                             "legacy caller silently starts minting currency");

            // (D2) SOURCE LINT, on comment-stripped code: the live scorer must actually
            // read the per-camp gold table off the tunable rail. The numeric sweep above
            // proves the pure function; this proves the SCENE path is wired to it rather
            // than to a literal nobody can tune.
            string scoringCode = ReadStripped("RaidScoring.cs");
            if (scoringCode == null)
            {
                failures.Add("[D2] RaidScoring.cs not found under Assets/_Modules - the source lint cannot run, " +
                             "and a lint that silently skips is worse than none");
            }
            else
            {
                if (!scoringCode.Contains("coins:"))
                    failures.Add("[D2] RaidScoring.cs live code never assigns Coins - the gold arrow is not " +
                                 "wired into the payout the victory screen grants");
                if (!scoringCode.Contains("CoinsBaseFor"))
                    failures.Add("[D2] RaidScoring.cs live code never calls RaidLootTunables.CoinsBaseFor - " +
                                 "the PER-CAMP gold base is not being resolved, so every camp would pay the " +
                                 "same and the map's escalation ladder is inert");
                if (!scoringCode.Contains("RaidLootTunables"))
                    failures.Add("[D2] RaidScoring.cs live code never reads RaidLootTunables - the wood/iron " +
                                 "bases are not on the remote rail, so the reward curve cannot be tuned by feel");
            }

            if (failures.Count == 0)
            {
                reason = "RAID LOOT CURRENCY OK - a raid pays WOOD and IRON on the map's five-rung " +
                         "performance ladder (fail " + MapFailPct + "% / 1* " + MapOneStarPct + "% / 2* " +
                         MapTwoStarPct + "% / 3* " + MapThreeStarPct + "% / perfect " + MapPerfectPct +
                         "%) off bases " + MapWoodBase + "w/" + MapIronBase + "i, a LOSS still pays rather " +
                         "than dead-ending, the camp multiplier reaches both new axes, the crystals/food " +
                         "shape is unchanged, the legacy 6-argument call shape still pays neither, " +
                         "all 8 knob defaults are the owner's numbers, and GOLD IS PAID on the same ladder " +
                         "across all 44 star/destruction combinations swept while never riding the camp " +
                         "multiplier - the map's missing arrow, asserted";
                Debug.Log(log.ToString() + "RAID_LOOT_CURRENCY_OK");
                return true;
            }

            reason = "raid-loot-currency: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "RAID_LOOT_CURRENCY_FAIL: " + reason);
            return false;
        }

        // =====================================================================

        private static void AssertFraction(List<string> failures, string label, int stars,
                                           float destruction, float expected)
        {
            float actual = RaidLootTunables.FractionFrom(stars, destruction,
                MapFailPct, MapOneStarPct, MapTwoStarPct, MapThreeStarPct, MapPerfectPct);
            if (Mathf.Abs(actual - expected) > 0.0001f)
                failures.Add("[A6] ladder rung '" + label + "' (stars=" + stars + ", destruction=" +
                             destruction.ToString("0.00") + ") returned " + actual.ToString("0.###") +
                             ", expected " + expected.ToString("0.###"));
        }

        private static void AssertDefault(List<string> failures, string key, int expected)
        {
            var spec = RemoteTunables.SpecFor(key);
            if (spec == null)
            {
                failures.Add("[B] knob '" + key + "' is not in RemoteTunables.Registry - it has no default, " +
                             "the server refuses every write to it, and the console cannot show it");
                return;
            }
            if (spec.Default != expected)
                failures.Add("[B] knob '" + key + "' ships at " + spec.Default + ", but the north-star map " +
                             "says " + expected + ". One of the two is wrong and neither may be assumed.");
        }

        /// <summary>
        /// Re-derives the ORIGINAL crystals/food formula independently and compares. Any
        /// drift means the two pre-existing axes were changed by a ticket that had no
        /// mandate to change them.
        /// </summary>
        private static void AssertLegacyAxes(List<string> failures, int stars, float destruction,
                                             int crystalsBase, int foodBase, int crystalsPerStar,
                                             int foodPerStar, float mult)
        {
            var got = RaidScoring.ComputeLoot(stars, destruction, crystalsBase, foodBase,
                                              crystalsPerStar, foodPerStar, mult, MapWoodBase, MapIronBase);
            float frac = Mathf.Clamp01(destruction);
            int st = Mathf.Clamp(stars, 0, 3);
            // Crystals: the same shape, WITHOUT the camp multiplier (map section 1).
            int wantCrystals = Mathf.RoundToInt(crystalsBase * frac + crystalsPerStar * st);
            int wantFood = Mathf.RoundToInt((foodBase * frac + foodPerStar * st) * mult);
            if (got.Crystals != wantCrystals)
                failures.Add("[C] crystals changed shape at stars=" + stars + " d=" + destruction.ToString("0.00") +
                             ": got " + got.Crystals + ", base*destruction + perStar*stars (NO camp " +
                             "multiplier - crystals are timer compression) gives " + wantCrystals);
            if (got.Stone != wantFood)
                failures.Add("[C] food changed shape at stars=" + stars + " d=" + destruction.ToString("0.00") +
                             ": got " + got.Stone + ", the original formula gives " + wantFood);
        }

        /// <summary>Reads a file under Assets/_Modules with comments blanked, or null.</summary>
        internal static string ReadStripped(string fileName)
        {
            string raw = ReadFirstUnderModules(fileName);
            return raw == null ? null : StripComments(raw);
        }

        /// <summary>Reads the first file with this name under Assets/_Modules, or null.</summary>
        internal static string ReadFirstUnderModules(string fileName)
        {
            try
            {
                string dir = Path.Combine(Application.dataPath, "_Modules");
                if (!Directory.Exists(dir)) return null;
                var hits = Directory.GetFiles(dir, fileName, SearchOption.AllDirectories);
                if (hits == null || hits.Length == 0) return null;
                return File.ReadAllText(hits[0]);
            }
            catch { return null; }
        }

        /// <summary>
        /// Blanks // and /* */ comments so a lint tests CODE, not prose. Replaces each
        /// comment with a space rather than deleting it, so a token cannot be forged by
        /// two identifiers meeting across a stripped comment. Same trade
        /// RaidScoringRegression.StripComments makes, and made for the same reason: on its
        /// first run a lint like this failed against a doc comment that QUOTED the thing it
        /// forbids, punishing the author for documenting the trap.
        /// </summary>
        internal static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src ?? string.Empty;
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                if (i + 1 < src.Length && src[i] == '/' && src[i + 1] == '*')
                {
                    int end = src.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    if (end < 0) { sb.Append(' '); break; }
                    sb.Append(' ');
                    i = end + 1;
                    continue;
                }
                if (i + 1 < src.Length && src[i] == '/' && src[i + 1] == '/')
                {
                    int nl = src.IndexOf('\n', i);
                    if (nl < 0) { sb.Append(' '); break; }
                    sb.Append(' ');
                    i = nl - 1;
                    continue;
                }
                sb.Append(src[i]);
            }
            return sb.ToString();
        }
    }
}
