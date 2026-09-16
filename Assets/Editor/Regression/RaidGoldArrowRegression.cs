// =============================================================================
// RaidGoldArrowRegression [raid-gold-arrow]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Markers: RAID_GOLD_ARROW_OK / _FAIL.
//
// Pins the north-star map's ONE explicitly named missing arrow:
//
//     "You currently have Gold -> troops but not troops -> raids -> gold.
//      That arrow has to exist."
//         - docs/PROGRAM_RAID_ECONOMY_2026-09-04.md section 1
//
// and the two exclusions and one CUT that ship with it:
//
//   * GOLD is paid, off a PER-CAMP base, on the SAME five-rung performance ladder
//     as wood and iron, and it does NOT ride the camp's rewardMultiplier.
//   * CRYSTALS come DOWN - the one reward in the map's table that decreases -
//     and they do not ride the camp multiplier either.
//   * WOOD, IRON and FOOD keep the multiplier exactly as they had it, so the
//     selection card's "x1.5 Loot" stays honest for them.
//
// -----------------------------------------------------------------------------
// PROVEN RED FIRST - stated per case, against the ACTUAL previous tree.
// -----------------------------------------------------------------------------
// Two different kinds of red, and the difference is honest rather than cosmetic:
//
//   (i) CASES THAT CANNOT COMPILE against the pre-change tree, which IS their red.
//       Before this work RaidScoring.ComputeLoot had NINE parameters and there was
//       no coinsBase; RaidLootTunables had no CoinsBaseFor, no CoinsBaseCamp*, no
//       CrystalsBase; RemoteTunables had no raid.lootCoinsBase* keys. Groups (A),
//       (B) and (E) below name symbols that did not exist, so against that tree
//       this file does not build. What each asserts once it does compile is
//       written out beside it.
//
//  (ii) ONE CASE THAT COMPILES AGAINST THE OLD TREE AND FAILS ON THE NUMBERS.
//       Case [C1] calls the pre-existing SEVEN-argument shape
//       ComputeLoot(3, 1f, 25, 60, 10, 20, 1.5f) and asserts the camp multiplier
//       does NOT reach crystals. The old code returned
//       (25*1 + 10*3) * 1.5 = 82.5 -> 83; this build returns 55 (25 + 30, no
//       multiplier). That case is red against the previous tree by arithmetic,
//       not by construction, and it is the one that proves the exclusion is real
//       rather than an accident of the new parameter.
//
// -----------------------------------------------------------------------------
// (!) WHY GOLD IS FOUR KNOBS AND NOT ONE BASE TIMES THE CAMP MULTIPLIER.
// -----------------------------------------------------------------------------
// The map publishes a DESIGNED gold target per camp, each sized at 125-140% of
// that camp's EXPECTED army replacement cost (never the player's actual army -
// "that could be gamed"):
//     Camp I 2,200 (army 1,650) - Camp II 3,100 (2,300)
//     Camp III 4,500 (3,300)    - Iron Bastion 6,500 (4,800)
// x1.5 of 2,200 is 3,300, and her Camp II number is 3,100. x2.2 is 4,840, and her
// Camp III number is 4,500. There is NO single base and multiplier that pays all
// four published numbers, so the escalation lives in the knob VALUES and the
// multiplier is excluded. Case [B3] asserts exactly that, because it is the one
// design decision in this lane that a future seat is most likely to "simplify".
//
// -----------------------------------------------------------------------------
// (!) THE OPEN QUESTION THIS SUITE DELIBERATELY DOES NOT SETTLE.
// -----------------------------------------------------------------------------
// Map section 1's table is headed "perfect 3 stars / 100%" and gives 1,800 wood /
// 2,200 gold, while the ladder in the same section lists "3 stars = 100%" AND
// "3 stars + 100% destruction = 110%". Those cannot both be true of 1,800.
// RaidLootTunables took the reading that 1,800 is the BASE and the 3-star rung is
// 100% of it, so a total razing pays 1,980; this suite applies the SAME reading to
// gold (2,200 = base, perfect = 2,420) for consistency, and says so out loud here
// rather than burying it. If the owner meant the other reading it is two Command
// Center rows and no rebuild - and then these expectations move with the knobs,
// because every one of them is derived from a literal in this file.
//
// Zero scene, zero save, zero network, zero PlayMode.
// ASCII only. Never throws.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Core.Ops;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pins the raid GOLD payout (the map's missing arrow), its per-camp table, the
    /// crystal cut, and the two camp-multiplier exclusions. Returns true (summary) /
    /// false (detail).
    /// </summary>
    public static class RaidGoldArrowRegression
    {
        // ---- The map's numbers, as LITERALS. Never RemoteTunables.Int(...) - an
        // oracle that reads the value it is checking certifies nothing.
        private const int MapCoinsCamp1 = 2200;
        private const int MapCoinsCamp2 = 3100;
        private const int MapCoinsCamp3 = 4500;
        private const int MapCoinsBastion = 6500;
        private const int MapCrystalsBase = 20;
        private const int MapCrystalsPerStar = 2;

        // The map's crystal BAND at a perfect clear: "Crystals 55 -> 20-30".
        private const int CrystalBandLow = 20;
        private const int CrystalBandHigh = 30;

        // What this build used to pay at a perfect clear, kept as a literal so the
        // failure message can say what the regression would be a return TO.
        private const int OldPerfectCrystals = 55;

        // The ladder rungs, restated as literals for the same reason.
        private const int MapFailPct = 18;
        private const int MapThreeStarPct = 100;
        private const int MapPerfectPct = 110;

        // The LIVE config ids of the three raid camps that exist on disk, read out of
        // Assets/Resources/Data/Canonical/scene-configs.json (verified 2026-09-04).
        // Ids are live data and are matched, never renamed.
        private const string IdCamp1 = "raider_camp_small";
        private const string IdCamp2 = "fortified_garrison";
        private const string IdCamp3 = "mage_enclave";
        private const string IdBastion = "iron_bastion";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RAID GOLD ARROW (map section 1: troops -> raids -> gold) ---");

            Case_A_GoldIsPaidOnTheLadder(failures, log);
            Case_B_PerCampTable(failures, log);
            Case_C_CrystalsComeDownAndLoseTheMultiplier(failures, log);
            Case_D_AcceptanceRow(failures, log);
            Case_E_KnobDefaults(failures);

            if (failures.Count == 0)
            {
                reason = "RAID GOLD ARROW OK - a raid PAYS GOLD on the map's five-rung ladder off a " +
                         "PER-CAMP base (" + MapCoinsCamp1 + "/" + MapCoinsCamp2 + "/" + MapCoinsCamp3 +
                         "/" + MapCoinsBastion + " by config id, unknown ids falling back to Camp I " +
                         "rather than to zero), gold and crystals both stay OFF the camp multiplier " +
                         "while wood/iron/food stay ON it, crystals at a perfect clear land inside the " +
                         "map's " + CrystalBandLow + "-" + CrystalBandHigh + " band (down from " +
                         OldPerfectCrystals + "), a LOSS still pays " + MapFailPct + "% of gold as well " +
                         "as wood and iron, and all 6 new knob defaults are the owner's numbers";
                Debug.Log(log.ToString() + "RAID_GOLD_ARROW_OK");
                return true;
            }

            reason = "raid-gold-arrow: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "RAID_GOLD_ARROW_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  (A) GOLD IS PAID, AND IT RIDES THE SAME LADDER AS WOOD AND IRON.
        // =====================================================================
        /// <summary>
        /// RED against the previous tree by NON-COMPILE: ComputeLoot had no coinsBase
        /// parameter, so gold was structurally zero on every path. Once it compiles, this
        /// asserts the arrow exists at all and that it is a LADDER, not a flat payout.
        /// </summary>
        private static void Case_A_GoldIsPaidOnTheLadder(List<string> failures, StringBuilder log)
        {
            int gThree = Coins(3, 0.80f, 1f, MapCoinsCamp1);
            int gPerfect = Coins(3, 1.00f, 1f, MapCoinsCamp1);
            int gFail = Coins(0, 0.10f, 1f, MapCoinsCamp1);
            log.AppendLine("  gold: fail " + gFail + " / 3 stars " + gThree + " / perfect " + gPerfect);

            if (gThree <= 0)
                failures.Add("[A1] a 3-star raid paid " + gThree + " GOLD. The whole point of this lane is " +
                             "the map's named missing arrow - troops -> raids -> gold. A zero here IS the " +
                             "defect, restored.");

            int wantThree = Mathf.RoundToInt(MapCoinsCamp1 * (MapThreeStarPct / 100f));
            if (gThree != wantThree)
                failures.Add("[A1] a 3-star raid paid " + gThree + " gold, expected " + wantThree +
                             " - the 3-star rung is " + MapThreeStarPct + "% and IS what the base means");

            int wantPerfect = Mathf.RoundToInt(MapCoinsCamp1 * (MapPerfectPct / 100f));
            if (gPerfect != wantPerfect)
                failures.Add("[A2] a perfect raid (3 stars AND 100% razed) paid " + gPerfect + " gold, " +
                             "expected " + wantPerfect + " (" + MapPerfectPct + "% of " + MapCoinsCamp1 +
                             "). The top rung is the only one that pays above the base, and it is what " +
                             "makes mastery worth more than victory.");

            // A LOSS STILL PAYS GOLD. Same ruling as wood and iron: "A failed attack still
            // pays 15-20%. That is deliberate - it keeps a loss from being a dead end."
            int wantFail = Mathf.RoundToInt(MapCoinsCamp1 * (MapFailPct / 100f));
            if (gFail != wantFail)
                failures.Add("[A3] a FAILED attack paid " + gFail + " gold, expected " + wantFail + " (" +
                             MapFailPct + "% of " + MapCoinsCamp1 + "). Do not 'fix' the zero-star rung " +
                             "to zero - a loss that funds nothing is a dead end, and the map rules it out.");

            // Strictly increasing across all five rungs, or "getting better at raiding has an
            // economic payoff" is not true of gold even though it is true of wood.
            int l0 = Coins(0, 0.3f, 1f, MapCoinsCamp1);
            int l1 = Coins(1, 0.5f, 1f, MapCoinsCamp1);
            int l2 = Coins(2, 0.7f, 1f, MapCoinsCamp1);
            int l3 = Coins(3, 0.8f, 1f, MapCoinsCamp1);
            int l4 = Coins(3, 1.0f, 1f, MapCoinsCamp1);
            if (!(l0 < l1 && l1 < l2 && l2 < l3 && l3 < l4))
                failures.Add("[A4] the GOLD ladder is not strictly increasing: " + l0 + " / " + l1 + " / " +
                             l2 + " / " + l3 + " / " + l4 + ". Raiding better must pay better in gold too, " +
                             "or the arrow exists without the incentive that makes it matter.");

            // A base of 0 must pay nothing, so the knob can genuinely turn the arrow off.
            if (Coins(3, 1f, 1f, 0) != 0)
                failures.Add("[A5] a coinsBase of 0 still paid gold - the knob cannot turn the payout off, " +
                             "so there is no way back to the previous behaviour");
        }

        // =====================================================================
        //  (B) THE PER-CAMP TABLE, AND THE MULTIPLIER EXCLUSION.
        // =====================================================================
        /// <summary>
        /// RED against the previous tree by NON-COMPILE: RaidLootTunables.CoinsBaseFor did
        /// not exist. Once it compiles, this asserts each live camp id resolves to ITS
        /// designed target, an unknown id falls back to Camp I rather than to zero, and the
        /// camp difficulty multiplier never touches gold.
        /// </summary>
        private static void Case_B_PerCampTable(List<string> failures, StringBuilder log)
        {
            AssertCampBase(failures, log, IdCamp1, MapCoinsCamp1);
            AssertCampBase(failures, log, IdCamp2, MapCoinsCamp2);
            AssertCampBase(failures, log, IdCamp3, MapCoinsCamp3);
            AssertCampBase(failures, log, IdBastion, MapCoinsBastion);

            // Case matters nowhere: the id arrives from a hand-authored JSON catalog.
            if (RaidLootTunables.CoinsBaseFor(IdCamp2.ToUpperInvariant()) != MapCoinsCamp2)
                failures.Add("[B1] the per-camp gold table is case-SENSITIVE - a hand-authored config id " +
                             "with different casing would silently drop that camp to the Camp I payout");

            // B2 - AN UNKNOWN ID FALLS BACK TO CAMP I, NEVER TO ZERO. A zero fallback would
            // silently delete the gold arrow for a whole camp with nothing on screen saying
            // so, which is the exact class of invisible failure canon section 12 forbids.
            int unknown = RaidLootTunables.CoinsBaseFor("no_such_camp_id");
            if (unknown != MapCoinsCamp1)
                failures.Add("[B2] an UNKNOWN raid config id resolved to " + unknown + " gold, expected the " +
                             "Camp I base " + MapCoinsCamp1 + ". Falling back to zero deletes the arrow for " +
                             "that camp invisibly; falling back to the top rung overpays it.");
            int empty = RaidLootTunables.CoinsBaseFor(null);
            if (empty != MapCoinsCamp1)
                failures.Add("[B2] a NULL raid config id resolved to " + empty + " gold, expected the Camp I " +
                             "base " + MapCoinsCamp1 + " - see [B2] above");

            // B3 - GOLD DOES NOT RIDE THE CAMP MULTIPLIER, and this is the design decision a
            // future seat is most likely to "simplify" away. If it did, Camp II would pay
            // 3,300 rather than the owner's designed 3,100 and every published number above
            // Camp I would be wrong.
            int flat = Coins(3, 1f, 1f, MapCoinsCamp1);
            int hard = Coins(3, 1f, 1.5f, MapCoinsCamp1);
            int extreme = Coins(3, 1f, 2.2f, MapCoinsCamp1);
            log.AppendLine("  gold vs camp multiplier: x1 " + flat + " / x1.5 " + hard + " / x2.2 " + extreme);
            if (hard != flat || extreme != flat)
                failures.Add("[B3] the camp rewardMultiplier reached GOLD (x1 " + flat + " / x1.5 " + hard +
                             " / x2.2 " + extreme + "). The map publishes a DESIGNED gold target per camp - " +
                             "x1.5 of " + MapCoinsCamp1 + " is " + Mathf.RoundToInt(MapCoinsCamp1 * 1.5f) +
                             ", and her Camp II number is " + MapCoinsCamp2 + ". Multiplying on top makes " +
                             "every tier above the first unpayable. The escalation belongs in the knob values.");

            // B4 - but WOOD and IRON still DO ride it, or the selection card's "x1.5 Loot"
            // becomes a lie on the two axes it was true of before this lane.
            var flatLoot = RaidScoring.ComputeLoot(3, 1f, MapCrystalsBase, 60, MapCrystalsPerStar, 20,
                                                   1f, 1800, 1100, MapCoinsCamp1);
            var hardLoot = RaidScoring.ComputeLoot(3, 1f, MapCrystalsBase, 60, MapCrystalsPerStar, 20,
                                                   1.5f, 1800, 1100, MapCoinsCamp1);
            if (hardLoot.Wood <= flatLoot.Wood || hardLoot.Iron <= flatLoot.Iron)
                failures.Add("[B4] the x1.5 camp multiplier stopped scaling WOOD/IRON (" + flatLoot.Wood +
                             "w/" + flatLoot.Iron + "i -> " + hardLoot.Wood + "w/" + hardLoot.Iron +
                             "i). Excluding gold and crystals must not quietly exclude the two axes that " +
                             "were always meant to carry it.");
            if (hardLoot.Stone <= flatLoot.Stone)
                failures.Add("[B4] the x1.5 camp multiplier stopped scaling FOOD (" + flatLoot.Stone + " -> " +
                             hardLoot.Stone + ") - food was never part of this lane's exclusions");
        }

        // =====================================================================
        //  (C) CRYSTALS COME DOWN, AND OFF THE MULTIPLIER.
        // =====================================================================
        /// <summary>
        /// [C1] is the one case in this file that COMPILES against the previous tree and
        /// fails on the arithmetic: the old code returned (25 + 10*3) * 1.5 = 83 for a
        /// perfect x1.5 clear, this build returns 55. [C2]/[C3] are non-compile reds
        /// (RaidLootTunables.CrystalsBase did not exist).
        /// </summary>
        private static void Case_C_CrystalsComeDownAndLoseTheMultiplier(List<string> failures, StringBuilder log)
        {
            // C1 - THE MULTIPLIER EXCLUSION, in the pre-existing 7-argument call shape.
            // Old tree: (25*1 + 10*3) * 1.5 = 82.5 -> 83. This build: 25 + 30 = 55.
            var legacyFlat = RaidScoring.ComputeLoot(3, 1f, 25, 60, 10, 20, 1f);
            var legacyHard = RaidScoring.ComputeLoot(3, 1f, 25, 60, 10, 20, 1.5f);
            log.AppendLine("  crystals vs camp multiplier (legacy shape): x1 " + legacyFlat.Crystals +
                           " / x1.5 " + legacyHard.Crystals);
            if (legacyHard.Crystals != legacyFlat.Crystals)
                failures.Add("[C1] the camp rewardMultiplier reached CRYSTALS (x1 " + legacyFlat.Crystals +
                             " / x1.5 " + legacyHard.Crystals + "). The map: 'Crystals are timer " +
                             "compression. If raids dump huge amounts of crystals, you accidentally " +
                             "accelerate the already-too-short progression curve.' An escalating camp must " +
                             "raise gold, wood and iron - never instant-finish.");
            // ...while FOOD in that same legacy shape still carries it.
            if (legacyHard.Stone <= legacyFlat.Stone)
                failures.Add("[C1] the camp multiplier stopped scaling FOOD in the legacy call shape (" +
                             legacyFlat.Stone + " -> " + legacyHard.Stone + ")");

            // C2 - THE CUT ITSELF. A perfect clear must land inside the map's 20-30 band.
            int perfectCrystals = RaidScoring.ComputeLoot(
                3, 1f, RaidLootTunables.CrystalsBase, 60, RaidLootTunables.CrystalsPerStar, 20,
                1f, 1800, 1100, MapCoinsCamp1).Crystals;
            log.AppendLine("  crystals at a perfect clear: " + perfectCrystals +
                           " (band " + CrystalBandLow + "-" + CrystalBandHigh + ", was " +
                           OldPerfectCrystals + ")");
            if (perfectCrystals < CrystalBandLow || perfectCrystals > CrystalBandHigh)
                failures.Add("[C2] a perfect clear pays " + perfectCrystals + " crystals, outside the map's " +
                             CrystalBandLow + "-" + CrystalBandHigh + " band. This build used to pay " +
                             OldPerfectCrystals + ", and crystals are the ONE number in the reward table " +
                             "that DECREASES - they buy instant-finish, so paying them out of raids " +
                             "shortens the whole build tree by the back door.");

            // C3 - and a LOSS with nothing razed still pays no crystals and no food, which is
            // the pre-existing contract RaidScoringRegression and the EditMode tests pin.
            var lost = RaidScoring.ComputeLoot(0, 0f, RaidLootTunables.CrystalsBase, 60,
                                               RaidLootTunables.CrystalsPerStar, 20,
                                               1f, 1800, 1100, MapCoinsCamp1);
            if (lost.Crystals != 0 || lost.Stone != 0)
                failures.Add("[C3] a total failure paid " + lost.Crystals + " crystals / " + lost.Stone +
                             " food - both must stay zero on a raid that razed nothing");
            if (lost.Coins <= 0 || lost.Wood <= 0 || lost.Iron <= 0)
                failures.Add("[C3] a total failure paid " + lost.Coins + " gold / " + lost.Wood + " wood / " +
                             lost.Iron + " iron - all three ride the fail rung and must pay " + MapFailPct +
                             "%, so that losing is not a dead end");
        }

        // =====================================================================
        //  (D) THE ACCEPTANCE ROW, WRITTEN OUT AS ONE ASSERTION.
        // =====================================================================
        /// <summary>
        /// The lane's acceptance criterion, in one place, at the resolved KNOB values so it
        /// measures what a device would actually pay rather than what a literal says.
        /// NON-COMPILE red against the previous tree (no CoinsBaseFor, no coinsBase).
        /// </summary>
        private static void Case_D_AcceptanceRow(List<string> failures, StringBuilder log)
        {
            int coinsBase = RaidLootTunables.CoinsBaseFor(IdCamp1);
            // The 3-star rung is 100%, so this row is the BASE reading of map section 1 -
            // the ambiguity flagged in this file's header. A total razing pays 110% of it.
            var threeStar = RaidScoring.ComputeLoot(3, 0.80f,
                RaidLootTunables.CrystalsBase, 60, RaidLootTunables.CrystalsPerStar, 20,
                1f, RaidLootTunables.WoodBase, RaidLootTunables.IronBase, coinsBase);
            log.AppendLine("  acceptance (Camp I, 3 stars): " + threeStar.Coins + "g " + threeStar.Wood +
                           "w " + threeStar.Iron + "i " + threeStar.Crystals + "c " + threeStar.Stone + "f");

            if (threeStar.Coins != MapCoinsCamp1)
                failures.Add("[D] a 3-star Camp I clear paid " + threeStar.Coins + " gold, expected " +
                             MapCoinsCamp1 + " - the map's Camp I target, sized at 125-140% of that " +
                             "camp's designed 1,650-gold army");
            if (threeStar.Wood != 1800)
                failures.Add("[D] a 3-star Camp I clear paid " + threeStar.Wood + " wood, expected 1800");
            if (threeStar.Iron != 1100)
                failures.Add("[D] a 3-star Camp I clear paid " + threeStar.Iron + " iron, expected 1100");
            if (threeStar.Crystals < CrystalBandLow || threeStar.Crystals > CrystalBandHigh)
                failures.Add("[D] a 3-star Camp I clear paid " + threeStar.Crystals + " crystals, outside " +
                             CrystalBandLow + "-" + CrystalBandHigh);
        }

        // =====================================================================
        //  (E) THE KNOB DEFAULTS ARE THE OWNER'S NUMBERS.
        // =====================================================================
        /// <summary>
        /// NON-COMPILE red against the previous tree: none of these keys existed. Asserted
        /// against LITERALS, never against RemoteTunables' own constants - an oracle that
        /// reads the value it is checking certifies nothing.
        /// </summary>
        private static void Case_E_KnobDefaults(List<string> failures)
        {
            AssertDefault(failures, RemoteTunables.KeyRaidLootCoinsBaseCamp1, MapCoinsCamp1);
            AssertDefault(failures, RemoteTunables.KeyRaidLootCoinsBaseCamp2, MapCoinsCamp2);
            AssertDefault(failures, RemoteTunables.KeyRaidLootCoinsBaseCamp3, MapCoinsCamp3);
            AssertDefault(failures, RemoteTunables.KeyRaidLootCoinsBaseBastion, MapCoinsBastion);
            AssertDefault(failures, RemoteTunables.KeyRaidLootCrystalsBase, MapCrystalsBase);
            AssertDefault(failures, RemoteTunables.KeyRaidLootCrystalsPerStar, MapCrystalsPerStar);

            // The map's per-camp gold is sized at 125-140% of a DESIGNED army cost. Assert the
            // RATIO band as well as the values, so a retune that keeps the shape stays green
            // and one that breaks the relationship does not.
            AssertReplacementBand(failures, "Camp I", MapCoinsCamp1, 1650);
            AssertReplacementBand(failures, "Camp II", MapCoinsCamp2, 2300);
            AssertReplacementBand(failures, "Camp III", MapCoinsCamp3, 3300);
            AssertReplacementBand(failures, "Iron Bastion", MapCoinsBastion, 4800);

            // And the tiers must actually escalate, or "unlock a harder raid" pays no better.
            if (!(MapCoinsCamp1 < MapCoinsCamp2 && MapCoinsCamp2 < MapCoinsCamp3 &&
                  MapCoinsCamp3 < MapCoinsBastion))
                failures.Add("[E] the per-camp gold targets do not escalate: " + MapCoinsCamp1 + " / " +
                             MapCoinsCamp2 + " / " + MapCoinsCamp3 + " / " + MapCoinsBastion);
        }

        // =====================================================================

        /// <summary>Gold paid by one result, with everything else zeroed out.</summary>
        private static int Coins(int stars, float destruction, float mult, int coinsBase)
            => RaidScoring.ComputeLoot(stars, destruction, 0, 0, 0, 0, mult, 0, 0, coinsBase).Coins;

        private static void AssertCampBase(List<string> failures, StringBuilder log,
                                           string configId, int expected)
        {
            int got = RaidLootTunables.CoinsBaseFor(configId);
            log.AppendLine("  camp '" + configId + "' -> " + got + " gold base");
            if (got != expected)
                failures.Add("[B] raid config id '" + configId + "' resolved to a gold base of " + got +
                             ", expected the map's designed target " + expected);
        }

        private static void AssertDefault(List<string> failures, string key, int expected)
        {
            var spec = RemoteTunables.SpecFor(key);
            if (spec == null)
            {
                failures.Add("[E] knob '" + key + "' is not in RemoteTunables.Registry - it has no default, " +
                             "the server refuses every write to it, and the console cannot show it");
                return;
            }
            if (spec.Default != expected)
                failures.Add("[E] knob '" + key + "' ships at " + spec.Default + ", but the north-star map " +
                             "says " + expected + ". One of the two is wrong and neither may be assumed.");
        }

        private static void AssertReplacementBand(List<string> failures, string label,
                                                  int gold, int designedArmyCost)
        {
            float pct = gold * 100f / designedArmyCost;
            if (pct < 125f || pct > 140f)
                failures.Add("[E] " + label + " pays " + gold + " gold against a designed " +
                             designedArmyCost + "-gold army = " + pct.ToString("0.#") + "%, outside the " +
                             "map's stated 125-140% band. Below 125 a win does not fund the next raid; " +
                             "above 140 the army stops being a real cost.");
        }
    }
}
