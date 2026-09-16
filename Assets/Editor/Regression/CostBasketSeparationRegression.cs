// =============================================================================
// CostBasketSeparationRegression [cost-basket] -- WO-947: cost baskets separate
// by the structure's NATURE.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only; references DeNelle.Core).
// Contract mirrors the other Run(out reason) oracles:
//   public static bool Run(out string reason)   -- NEVER throws
//   markers: COST_BASKET_OK (Debug.Log) / COST_BASKET_FAIL (LogError)
//
// THE RULING (owner, 2026-08-10, verbatim intent -- WO-947 section 1):
//   "The lens... is what are we building? If we are building regular structures,
//    then it makes sense that they only cost... iron and wood. However, if it's
//    magical based or ethereal based, then yes, it's crystal... Let's make a
//    separation. So it doesn't touch all three."
// Operationalized:
//   * REGULAR structures cost wood + iron (+/- food where already used). NEVER crystals.
//   * MAGICAL / ETHEREAL structures are CRYSTALS + IRON -- never wood.
//   * INVARIANT: no row's build cost or upgrade step holds wood AND iron AND crystals.
//
// THE PINS ARE ANSWERED (owner, 2026-08-14); catalog v18 applies the first four and v19 the last:
//   pin 1 "Crystals and Iron"        -> the magical pairing is crystals + iron.
//   pin 2 "yes AoE healing"          -> healing IS magical: the healing rows become crystals +
//                                       iron (+ their existing food). NOTE: one of the two rows
//                                       this pin was spent on -- the Healer Tower -- was RETIRED
//                                       from the catalog at v20 (WO-990, owner 2026-08-14: "i do
//                                       not know what the town healer is" -> "retire"), because it
//                                       was in NO build category and no player could ever build it.
//                                       'healing_caravan' is the surviving, reachable healing row.
//   pin 3 "Crafting (can enbue       -> the jeweler is a CRAFTING shop, therefore REGULAR
//         preciouus sstones future      today. The owner flagged a FUTURE release may let it
//         release)"                     imbue precious stones -- a re-classification THEN.
//   pin 4 "thats a baliista          -> tower_ballista is MECHANICAL, therefore REGULAR.
//         mechanical"                   The DATA reading beat the ID reading.
//   pin 5 "cathedral of magic  -> 'arcane-tower' ("Cathedral of Magic") is MAGICAL. It is the
//         is where all magic      ENGINE of magical progression, not a vendor that deals in
//         upgrades anre and       magic. Applied in catalog v19.
//         can unlock new
//         teirs of spells"
// So MagicalIds holds THREE magical ids and PendingPins is EMPTY. (It held FOUR until catalog
// v20 retired the never-buildable Healer Tower row -- WO-990. The pin is still spent and still
// binding; there is simply one fewer row for it to apply to.)
//
// WHY THE PENDING-PIN MECHANISM STAYS even at zero entries: when a row's side is
// an open OWNER question, guessing it is exactly the inference-fix CLAUDE.md
// section 12 forbids. Such a row is carried here as a DATED, CITED exemption
// instead of silently passing or silently failing -- and the list SELF-CLEANS,
// because case 3 FAILS if a listed row has stopped violating, so a pin that lands
// and gets applied forces the exemption to be deleted in the same change. It can
// only shrink. It is NOT a mute button for a gate failure.
//
// THE LAST PIN IS SPENT (owner, 2026-08-14): 'arcane-tower' ("Cathedral of Magic")
// was the one row left unclassified. It is MAGICAL -- crystals + iron -- and catalog
// v19 folds its wood:60 1:1 into CRYSTALS (basket total 120 unchanged).
// This OVERRULES the earlier reading that put it on the REGULAR side by the jeweler
// analogy ("a shop that DEALS IN magic is still a shop"), which cited behaviorId
// 'GameplayBuilding' and the row's _heightNote ("despite the id this is not a tower --
// it is the town's one civic landmark"). THE OWNER'S DISTINCTION, recorded here
// because the surface evidence genuinely points both ways and the next reader will
// hit the same fork: the jeweler SELLS things that happen to be precious; the
// Cathedral is WHERE MAGIC UPGRADES LIVE AND WHERE NEW SPELL TIERS UNLOCK -- the
// ENGINE of magical progression, not a vendor. 'behaviorId: GameplayBuilding'
// describes its BEHAVIOUR (it is not a firing tower), NOT its cost class.
// WO-947 is now FULLY APPLIED: no open classification questions remain.
//
// Cases:
//   1 [invariant]   no entry's build cost / upgrade step holds wood AND iron AND
//                   crystals -- except the dated pending-pin ids.
//   2 [regular]     crystals appear in NO entry outside MagicalIds -- except the
//                   dated pending-pin ids. Also FAILS a row whose authored basket
//                   is all-zero, because BuildModeController.CostFor's back-compat
//                   fallback then charges pure `buildCost` CRYSTALS -- a regular
//                   structure priced in crystals through the back door.
//   3 [pins]        every pending-pin id still EXISTS and still VIOLATES (else the
//                   exemption is stale and is hiding the next regression), and the
//                   pins are logged distinguishably for the owner call.
//   7 [repair-carve-out] the WO-947 REPAIR EXCEPTION, fenced: the crystals-per-iron
//                   rate is authored on 'repair_default' and NO other row, at the RULED
//                   number; the base repair price still emits zero crystals; an
//                   unauthored slot is not convertible. Red-proved against four
//                   deliberately-broken rows before the live pass is believed.
//   4 [applied]     every conversion already made (v17: tower_siege_tower; v18:
//                   tower_ballista, jeweler, tower_arcane_spire,
//                   healing_caravan; v19: arcane-tower) stays on its ruled side -- regular rows carry
//                   zero crystals, magical rows carry zero wood at EVERY rung and
//                   non-zero crystals AT THEIR TOP TIER (re-pointed from
//                   every-rung by OWNER RULING 2026-08-26 / WO-1217 -- see the
//                   in-method block; the pairing rule was NOT weakened) -- and each
//                   basket TOTAL matches the pinned Totals[]. A revert or a
//                   stealth re-balance both trip here.
//   8 [capture-basket] WO-1478 -- the SCREENSHOT HARNESS may not author a cost either.
//                   No SetPlacingLabel( call in Assets/Editor/UICaptureLaunch.cs passes a
//                   string literal containing a DIGIT. The ghost pill had a wood+iron+crystals
//                   basket hardcoded into it -- one the catalog has never held (the
//                   authored Arcane Spire row is iron 360) whose all-three shape case 1
//                   forbids -- and because it lived in EDITOR code, every case above was
//                   blind to it. It photographed a price the game cannot charge and both
//                   independent UI reviewers quoted the fiction back as evidence. Red-proved
//                   against an inline broken fixture before the live pass is believed.
//
// DELIBERATELY NOT DUPLICATED HERE: the structures-catalog dual-copy byte-equal
// check (BuildEconomyRegression.CheckDualCopy) and cost-slot sanity / tier
// monotonicity (BuildEconomyRegression checks 3 + 4). This oracle owns ONE thing:
// basket COMPOSITION.
//
// Reads the AUTHORED basket (repo.cost / repo.upgradeCost), NOT
// BuildModeController.CostFor -- CostFor folds in the buildCost-crystals
// back-compat fallback and the tower softcap multiplier, neither of which is a
// statement about what the row is MADE of. Composition is an authoring question.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "cost-basket suite", () => { if (!DeNelle.Editor.Regression.CostBasketSeparationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cost-basket] " + r); });
//
// Standalone: run-unity-method DeNelle.Editor.Regression.CostBasketSeparationRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.Catalog;
// NOTE: NO 'using DeNelle.Village' here on purpose -- that namespace also declares a
// ResourceCost (the EconomyService struct) and this file uses the Core.Catalog one bare
// in four places. The repair carve-out's pricing authority is spelled out in full instead.
using WallRepair = DeNelle.Village.WallRepairController;

namespace DeNelle.Editor.Regression
{
    public static class CostBasketSeparationRegression
    {
        private const string CatalogRelPath = "Data/Canonical/structures-catalog.json";

        /// <summary>
        /// The PINNED magical/ethereal set -- rows allowed to carry crystals.
        /// POPULATED 2026-08-14 by the OWNER's answers to WO-947 section 4:
        ///   pin 1 verbatim "Crystals and Iron"  -> the magical basket is crystals + iron, never wood.
        ///   pin 2 verbatim "yes AoE healing"    -> healing IS magical. This pin covered TWO rows; only
        ///                   'healing_caravan' remains, because catalog v20 RETIRED the never-buildable
        ///                   Healer Tower row (WO-990, owner ruling 2026-08-14). Do not re-add it here.
        ///   pin 5 verbatim "cathedral of magic is where all magic upgrades anre and can unlock new
        ///                   teirs of spells"    -> 'arcane-tower' is the ENGINE of magical
        ///                   progression, not a vendor that deals in magic. Applied catalog v19.
        /// Adding an id here is an OWNER ruling, never an agent's inference.
        /// </summary>
        private static readonly HashSet<string> MagicalIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "tower_arcane_spire",   // element Aether / behaviorId ArcaneTower / projectileStyle "spell"
                "healing_caravan",      // owner 2026-08-14 pin 2: "yes AoE healing" -- healing is magical.
                                        // The pin's OTHER row (the Healer Tower) was retired from the
                                        // catalog at v20 by WO-990; this is the surviving healing row.
                "arcane-tower",         // owner 2026-08-14 pin 5: "cathedral of magic is where all magic
                                        // upgrades anre and can unlock new teirs of spells" -- the ENGINE of
                                        // magical progression. behaviorId 'GameplayBuilding' is its BEHAVIOUR
                                        // (not a firing tower), NOT its cost class. Overrules the jeweler-analogy
                                        // reading; see the header. Applied catalog v19.
            };

        /// <summary>
        /// Rows still on their PRE-RULING basket because their classification is an
        /// OPEN OWNER PIN. **EMPTY as of 2026-08-14** -- the owner answered all four
        /// WO-947 section 4 pins plus the section 6 id-vs-data pin plus the final
        /// 'arcane-tower' pin, and catalog v18 + v19
        /// apply every one of them, so there is nothing left to excuse. The
        /// mechanism stays (case 3 fails if a listed row has stopped violating) so a
        /// FUTURE pin can be carried the same dated, cited way instead of an agent
        /// guessing a side -- the inference-fix CLAUDE.md section 12 forbids.
        /// </summary>
        private static readonly Dictionary<string, string> PendingPins =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // (empty -- every WO-947 pin was answered by the owner on 2026-08-14 and applied in
                //  structures-catalog.json v18 + v19. Do NOT re-add a row here to dodge a gate failure;
                //  an entry here is an OWNER question on record, not a mute button.)
            };

        /// <summary>
        /// Every row WO-947 has actually been converted (catalog v17 + v18 + v19). Each records the
        /// SIDE it was ruled onto and the basket TOTALS (build, then each upgrade step) that the
        /// conversion deliberately PRESERVED -- both folds were 1:1, so first-cost feel did not
        /// move. A revert, a dropped fold, or a stealth re-balance all trip case 4.
        /// </summary>
        private sealed class AppliedRow
        {
            public bool Magical;       // true -> crystals + iron, wood must be 0; false -> wood + iron, crystals must be 0
            public int[] Totals;       // build cost, then one per upgrade step
            public string Why;         // the owner's own words + the catalog evidence
        }

        // ⚠ TOTALS RE-BASELINED 2026-08-21 BY THE ECONOMY SINK PASS (owner ruling: a committed
        // daily player should need 8-12 weeks to exhaust content, and the LEVER IS THE SINKS --
        // the 5-wood-per-5-seconds faucet is deliberately untouched). Every Totals[] below was
        // multiplied by that pass's per-ladder factor (build x4, upgrade->L2 x10, upgrade->L3 x14;
        // the three storage rows use their own decreasing ladder). READ THE 'Why' PROSE BELOW WITH
        // THAT IN MIND: sentences like "basket total (120) is unchanged" describe the WO-947
        // 1:1 wood->crystal FOLD that produced the row's SHAPE, and that fold is still exactly
        // what it says -- but the ABSOLUTE number quoted is the pre-rescale one. The fold is the
        // load-bearing claim (which resources, in what ratio); the absolute total is not, and is
        // now carried ONLY by the Totals[] arrays here, which are the single authority.
        // WHAT THIS CASE STILL PINS, undiminished: a rescale must move a row's baskets TOGETHER
        // and must never move a resource ACROSS the regular/magical line. Scaling is allowed;
        // silently re-basketing under cover of a scale is what this case catches.
        private static readonly Dictionary<string, AppliedRow> AppliedRows =
            new Dictionary<string, AppliedRow>(StringComparer.OrdinalIgnoreCase)
            {
                { "tower_siege_tower", new AppliedRow {
                    // ⚠ STEP 1 MOVED 3240 -> 1620 by WO-1217 Slice A (2026-08-26): the FIRST
                    // upgrade step is now a flat 1.5x the build cost on every ladder
                    // (1080 x 1.5 = 1620). Build and step 2 are untouched. Composition never
                    // moved -- still wood + iron, crystals 0.
                    Magical = false, Totals = new[] { 1080, 1620, 9450 },
                    Why = "REGULAR on the catalog's own evidence (displayName 'Sky Ballista (Anti-Air)', " +
                          "'Wall-mounted spear thrower', element None, projectileStyle 'bolt', _heightCadence " +
                          "SIEGE ENGINE group). v17, 2026-08-14. Crystals folded 1:1 into IRON. " +
                          "TOTALS RE-BASELINED 2026-08-21: this was the ONE row the economy sink pass's " +
                          "re-baseline of this dictionary MISSED, and it read 270/324/675 while every other " +
                          "row here had already been multiplied. PROOF it is this array that was stale and " +
                          "NOT a double-scaled catalog: at HEAD the row authored wood 160 + iron 110 = 270, " +
                          "192+132 = 324, 400+275 = 675; the working tree authors 640+440 = 1080, " +
                          "1920+1320 = 3240, 5600+3850 = 9450 -- exactly 270x4, 324x10, 675x14, ONE " +
                          "application of the pass's per-ladder factors (a double application would read " +
                          "4320 on the build basket). Composition never moved: still wood + iron, crystals 0." } },
                { "tower_ballista", new AppliedRow {
                    // ⚠ STEP 1 MOVED 1920 -> 960 by WO-1217 Slice A (2026-08-26): flat 1.5x
                    // the build cost on the first upgrade step (640 x 1.5 = 960). Build and
                    // step 2 untouched; still wood + iron, crystals 0.
                    Magical = false, Totals = new[] { 640, 960, 5600 },
                    Why = "REGULAR by OWNER ruling 2026-08-14, verbatim: \"thats a baliista mechanical\". The " +
                          "'wizard' in the id is stale naming; the row's data (displayName 'Ballista', element " +
                          "None, projectileStyle 'bolt', owner rename 2026-07-08) is what was ruled on. v18. " +
                          "Crystals (70/84/175) folded 1:1 into IRON." } },
                { "jeweler", new AppliedRow {
                    Magical = false, Totals = new[] { 480 },
                    Why = "REGULAR by OWNER ruling 2026-08-14, verbatim: \"Crafting (can enbue preciouus sstones " +
                          "future release)\" -- it is a crafting shop, it trades in gems rather than being built " +
                          "of magic. The owner flagged a FUTURE release may let it imbue precious stones; that is " +
                          "a re-classification THEN, not now. v18. Crystals (30) folded 1:1 into IRON." } },
                { "tower_arcane_spire", new AppliedRow {
                    // ⛔ TOTALS RE-RULED 2026-08-24 by the owner: crystals 200/400/800 (was
                    // 200/1500/4370), so these totals move 660/1980/5770 -> 360/880/2200.
                    // Her reasoning, verbatim: "to get 5 of those towers would take a year".
                    //
                    // ⭐ THIS SUITE DID ITS JOB and the note is worth keeping: it caught the change
                    // in one run and said exactly the right thing — "a different total is a balance
                    // change riding on a composition ruling; if it is intended, update AppliedRows
                    // with the owner's new numbers." It is intended, and these are her numbers.
                    //
                    // ⚠ WHAT DID NOT CHANGE, AND WHY IT MATTERS: the WO-947 pin-1 PAIRING
                    // ("Crystals and Iron") is untouched, and so is the 1:1 wood fold that produced
                    // it. This edit moves MAGNITUDE only. The suite exists to stop a balance change
                    // from silently re-opening a COMPOSITION ruling — the composition is unchanged,
                    // which is why updating the totals is the correct response rather than a bypass.
                    //
                    // ⚠ IRON IS DELIBERATELY UNCHANGED (160/480/1400). At L3 iron is now the larger
                    // constraint — crystals gate ENTRY, iron gates SCALE — which is a consequence of
                    // the owner's crystal-only ruling, flagged rather than quietly rebalanced.
                    //
                    // ⛔ RE-RULED AGAIN 2026-08-26 (WO-1217, catalog v38): ONLY TIER 3 COSTS
                    // CRYSTALS. The build basket's 200 crystals and the L1->L2 step's 400 were
                    // folded 1:1 into IRON, so those two rungs are now crystal-FREE
                    // (build w0 f0 i360 c0; step1 w0 f0 i540 c0; step2 w0 f0 i1400 c800).
                    // Step 1 also took Slice A's flat 1.5x-of-build rule (360 x 1.5 = 540),
                    // which is why this total moves 880 -> 540 rather than staying at 600.
                    // Totals therefore go 360/880/2200 -> 360/540/2200.
                    // This is what re-pointed case 4's magical crystal assertion from
                    // every-basket to top-tier -- see the ⛔ block in CaseAppliedRowStaysConverted.
                    // The WO-947 PAIRING rule is untouched: wood is still 0 at every rung.
                    Magical = true, Totals = new[] { 360, 540, 2200 },
                    Why = "MAGICAL (element 'Aether', behaviorId 'ArcaneTower', projectileStyle 'spell'); the " +
                          "PAIRING came from OWNER pin 1, verbatim: \"Crystals and Iron\". v18. Wood (40/48/100) " +
                          "folded 1:1 into CRYSTALS, the crystal-BASED side of the ruling. v37 (2026-08-24): " +
                          "crystal MAGNITUDES re-ruled to 200/400/800 by the owner; pairing and fold unchanged. " +
                          "v38 (2026-08-26, WO-1217): ONLY TIER 3 costs crystals -- build + L2 crystals folded " +
                          "1:1 into IRON, so this row is crystal-gated at the TOP of its ladder rather than at " +
                          "every rung. Pairing (no wood) unchanged." } },
                // NOTE: the Healer Tower row was converted here at v18 under the same pin-2 ruling and
                // then RETIRED WHOLESALE at catalog v20 (WO-990, owner 2026-08-14). Its AppliedRow was
                // removed with it -- case 4 FAILS on an entry whose row is missing, so a retirement must
                // delete both halves. Do not restore either.
                { "healing_caravan", new AppliedRow {
                    Magical = true, Totals = new[] { 1400 },
                    Why = "MAGICAL by OWNER ruling 2026-08-14, verbatim: \"yes AoE healing\" -- healing IS magical. " +
                          "Basket is crystals + iron + food. v18. Wood (150) folded 1:1 into CRYSTALS." } },
                { "arcane-tower", new AppliedRow {
                    Magical = true, Totals = new[] { 480 },
                    Why = "MAGICAL by OWNER ruling 2026-08-14, verbatim: \"cathedral of magic is where all magic " +
                          "upgrades anre and can unlock new teirs of spells\" -- it is the ENGINE of magical " +
                          "progression, not a vendor that deals in magic (the jeweler-analogy reading, which " +
                          "cited behaviorId 'GameplayBuilding' and the row's civic-landmark _heightNote, is " +
                          "OVERRULED: behaviorId describes BEHAVIOUR, not cost class). v19. Wood (60) folded " +
                          "1:1 into CRYSTALS, so the basket is crystals 60 + iron 60, total 120 unchanged. " +
                          "No upgrade ladder on this row (singleton, maxLevel unset), so ONE basket." } },
            };

        [Serializable]
        private sealed class StructuresFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("COST_BASKET_OK - " + reason);
            else Debug.LogError("COST_BASKET_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== CostBasketSeparationRegression [cost-basket] (WO-947: regular = wood+iron, magical = crystal-based) ===");

            try
            {
                var entries = ParseCatalog(failures, log);
                if (entries != null)
                {
                    CaseInvariantAndRegular(entries, failures, log);
                    CasePendingPinsStillStand(entries, failures, log);
                    CaseAppliedRowStaysConverted(entries, failures, log);
                    CaseRepairCrystalCarveOut(entries, failures, log);
                }
                // WIDENED 2026-08-21 (owner ruling): "a rule that inspects one file while three
                // files author costs is not a rule". Two WO-947 violations survived for months
                // purely because this oracle stopped at structures-catalog.json.
                CaseBuildingTiersBaskets(failures, log);
                CaseCsAuthoredBaskets(failures, log);
                // WIDENED 2026-09-06 (WO-1478): the capture harness is a fourth cost-authoring
                // file, and it is EDITOR code, so neither the data cases nor the _Modules lint
                // could see the fabricated basket it painted into every ghost-pill PNG.
                CaseCaptureHarnessLabels(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[cost-basket] CostBasketSeparationRegression THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "COST BASKET OK - no structure row mixes wood + iron + crystals and no row outside the " +
                         "pinned magical set (" + MagicalIds.Count + " row(s)) carries crystals; " +
                         PendingPins.Count + " dated WO-947 pending-pin row(s) remain (0 = every owner pin " +
                         "answered and applied); all " + AppliedRows.Count + " converted row(s) stay on their " +
                         "ruled side with their basket totals intact. WIDENED 2026-08-21: " +
                         "building-tiers.json tier baskets carry no crystals on regular buildings " +
                         "(and no wood on the magical one), and no .cs under Assets/_Modules builds " +
                         "a cost basket naming wood + iron + crystals together. WIDENED 2026-08-27 (PROD-014 slice d): " +
                         "the crystals-for-repair carve-out is authored on '" + RepairRateRowId + "' ONLY at " +
                         "the ruled " + RuledCrystalsPerIron + " crystals/iron, the base repair price still " +
                         "emits zero crystals, and an unauthored slot is NOT CONVERTIBLE rather than free. " +
                         "WIDENED 2026-09-06 (WO-1478): no SetPlacingLabel call in the capture harness " +
                         "(" + CaptureHarnessRelPath + ") hands the ghost pill a hardcoded cost literal, " +
                         "red-proved against an inline broken fixture first.";
                Debug.Log("COST_BASKET_OK\n" + log);
                return true;
            }
            reason = "cost-basket: " + failures.Count + " failure(s): " + string.Join(" | ", failures);
            Debug.LogError("COST_BASKET_FAIL: " + failures.Count + " failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }

        // =====================================================================
        //  CASE 5 [tiers-basket] -- building-tiers.json authors a SECOND cost basket
        //  (costWood / costFood / costCrystal per tier) and this oracle never looked at
        //  it. Five REGULAR buildings (farm, lumbermill, forge, armorer, barracks) were
        //  charging CRYSTALS in plain sight the whole time.
        //
        //  Same law, same sides: REGULAR carries no crystals; the MAGICAL row carries no
        //  wood. 'arcane-tower' is MAGICAL by the owner's spent pin 5 ("cathedral of magic
        //  is where all magic upgrades anre and can unlock new teirs of spells") -- that
        //  classification was already ruled for structures-catalog and is simply APPLIED
        //  here to the same building in a second file. This file has no iron column, so a
        //  regular basket here reads wood + food.
        // =====================================================================
        private const string BuildingTiersRelPath = "Data/Canonical/building-tiers.json";

        private static void CaseBuildingTiersBaskets(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(BuildingTiersRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[tiers-basket] " + BuildingTiersRelPath + " unreadable - a whole cost-authoring " +
                             "file would go unchecked, which is exactly how the crystals-on-regular rows survived");
                return;
            }

            JToken root;
            try { root = JToken.Parse(json); }
            catch (Exception ex)
            {
                failures.Add("[tiers-basket] building-tiers.json failed to parse: " + ex.Message);
                return;
            }

            if (!(root["buildings"] is JArray buildings) || buildings.Count == 0)
            {
                failures.Add("[tiers-basket] building-tiers.json deserialized to 0 buildings");
                return;
            }

            int clean = 0;
            foreach (var b in buildings)
            {
                string id = (string)b["id"] ?? "?";
                bool magical = MagicalIds.Contains(id);
                if (!(b["tiers"] is JArray tiers)) continue;

                foreach (var t in tiers)
                {
                    string where = "'" + id + "' tier " + (t["tier"] ?? "?");
                    int wood    = (int?)t["costWood"]    ?? 0;
                    int crystal = (int?)t["costCrystal"] ?? 0;

                    if (magical)
                    {
                        if (wood != 0)
                            failures.Add("[tiers-basket] " + where + " charges " + wood + " wood, but '" + id +
                                         "' is MAGICAL (owner pin 5) and a magical basket is crystal-BASED, never " +
                                         "wood. Fold the wood into crystals the way catalog v19 did for the same " +
                                         "building in structures-catalog.json.");
                        if (crystal <= 0 && wood == 0)
                            failures.Add("[tiers-basket] " + where + " charges NO crystals - a magical row under " +
                                         "WO-947 is crystal-BASED.");
                    }
                    else
                    {
                        if (crystal != 0)
                            failures.Add("[tiers-basket] " + where + " charges " + crystal + " crystals, but '" + id +
                                         "' is a REGULAR building and WO-947 reserves crystals for MAGICAL ones. " +
                                         "This is the violation class that survived for months because the basket " +
                                         "oracle only read structures-catalog.json. Fold the crystals into wood/food.");
                    }
                    clean++;
                }
            }
            log.AppendLine("  [tiers-basket] building-tiers.json -> " + buildings.Count + " building(s), " +
                           clean + " tier basket(s) checked");
        }

        // =====================================================================
        //  CASE 6 [cs-basket] -- COSTS AUTHORED IN C#, WHICH NO DATA ORACLE CAN SEE.
        //
        //  Tower.TryUpgrade charged ONE authored int as wood AND iron AND crystals
        //  simultaneously -- a textbook breach of the invariant -- and BuildMenuVM held a
        //  SECOND copy of the same expression for the price it displayed. Neither is in
        //  any JSON, so every data-driven check in this file was blind to both.
        //
        //  This case is a SOURCE LINT. It reads .cs as TEXT because there is no other way
        //  to see a cost that never becomes data -- the ruling's own instruction: "if a
        //  cost is authored in .cs rather than data, the oracle must catch that too, by
        //  lint if it cannot be read as data."
        //
        //  ⚠ IT MUST DISTINGUISH **AUTHORING** A BASKET FROM **RELAYING** ONE, and getting
        //  this wrong is what a naive version of this lint does. Simply flagging any
        //  expression that NAMES wood, iron and crystals together produces 10+ false
        //  positives on completely correct code -- EconomyService's own constructor
        //  (wood, food, iron, crystals, coins), BuildModeController re-wrapping (c.wood,
        //  c.stone, c.iron, c.crystals), the crafting services forwarding
        //  (recipe.Cost?.Wood, ... .Iron, ... .Crystals). Those COPY a basket that was
        //  authored elsewhere; they decide nothing. A gate that cries wolf on correct code
        //  gets muted, and a muted gate protects nothing -- so the lint targets the two
        //  shapes that actually DECIDE a three-resource basket:
        //
        //    (a) SAME-EXPRESSION: wood, iron and crystals all assigned the identical
        //        non-zero expression. This is Tower.cs's exact bug
        //        (wood: cost, iron: cost, crystals: cost) and BuildMenuVM's copy of it
        //        (wood = each, iron = each, crystals = each). A relay never does this,
        //        because a relay reads three DIFFERENT source fields.
        //    (b) ALL-LITERAL: wood, iron and crystals all assigned non-zero numeric
        //        literals -- a basket hand-written in code that should have been data.
        //
        //  Scope: Assets/_Modules (runtime code). Comments are stripped before matching so
        //  the historical note in Tower.cs -- which QUOTES the old bad line on purpose --
        //  does not trip the gate it documents.
        // =====================================================================
        // NOTE the alternation covers the ALIASES as well as the type names. BuildMenuVM's copy
        // of the bug was written as `new CoreCost { ... }` -- a `using CoreCost = DeNelle.Core.
        // Catalog.ResourceCost;` alias -- so a pattern matching only "ResourceCost" walks straight
        // past the second of the two sites this case exists to catch.
        private static readonly Regex CostCtorRx = new Regex(
            @"new\s+\w*(?:ResourceCost|CoreCost)\s*(?:\(|\{)(?<body>[^;)}]*)",
            RegexOptions.Compiled);

        /// <summary>Value assigned to <paramref name="field"/> inside a basket body, or null.</summary>
        private static string AssignedValue(string body, string field)
        {
            var m = Regex.Match(body, @"\b" + field + @"s?\s*[:=]\s*(?<v>[^,]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value.Trim() : null;
        }

        private static bool IsNonZeroLiteral(string v)
        {
            return v != null && Regex.IsMatch(v, @"^\d+$") && v.TrimStart('0').Length > 0;
        }

        private static void CaseCsAuthoredBaskets(List<string> failures, StringBuilder log)
        {
            string root = Path.Combine(Application.dataPath, "_Modules");
            if (!Directory.Exists(root))
            {
                failures.Add("[cs-basket] " + root + " not found - the C#-authored-cost lint could not run, so a " +
                             "three-resource basket in code would pass unseen");
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                failures.Add("[cs-basket] could not enumerate .cs under _Modules: " + ex.Message);
                return;
            }

            int scanned = 0, sites = 0;
            foreach (string f in files)
            {
                string src;
                try { src = File.ReadAllText(f); }
                catch { continue; }
                scanned++;

                string code = StripComments(src);
                foreach (Match m in CostCtorRx.Matches(code))
                {
                    string body = m.Groups["body"].Value;
                    if (string.IsNullOrEmpty(body)) continue;
                    sites++;

                    string vWood    = AssignedValue(body, "wood");
                    string vIron    = AssignedValue(body, "iron");
                    string vCrystal = AssignedValue(body, "crystal");
                    if (vWood == null || vIron == null || vCrystal == null) continue;

                    // (a) the same non-zero expression driving all three slots.
                    bool sameExpr = vWood == vIron && vIron == vCrystal
                                    && vWood != "0" && vWood.Length > 0;
                    // (b) three hand-written non-zero literals.
                    bool allLiteral = IsNonZeroLiteral(vWood) && IsNonZeroLiteral(vIron) && IsNonZeroLiteral(vCrystal);

                    if (sameExpr || allLiteral)
                    {
                        string rel = f.Replace(Application.dataPath, "Assets").Replace('\\', '/');
                        failures.Add("[cs-basket] " + rel + " AUTHORS a cost basket charging wood AND iron AND " +
                                     "crystals together: \"" + Collapse(body) + "\"" +
                                     (sameExpr ? " (one expression, '" + vWood + "', driving all three slots -- " +
                                                 "Tower.TryUpgrade's exact bug)"
                                               : " (three hand-written literals)") +
                                     ". WO-947's invariant is that NO basket holds all three. A cost authored in " +
                                     "C# is invisible to every data oracle, which is why this went unseen for " +
                                     "months. Split by the structure's NATURE: regular = wood + iron, magical = " +
                                     "crystals + iron.");
                    }
                }
            }
            log.AppendLine("  [cs-basket] linted " + scanned + " .cs file(s) under Assets/_Modules, " +
                           sites + " cost-basket construction site(s)");
        }

        /// <summary>Blank out // and /* */ comments so documented history cannot trip the lint.</summary>
        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            src = Regex.Replace(src, @"//[^\n]*", " ");
            return src;
        }

        private static string Collapse(string s)
        {
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 120 ? s.Substring(0, 120) + "..." : s;
        }

        // =====================================================================
        //  CASE 8 [capture-basket] -- WO-1478: THE SCREENSHOT HARNESS AUTHORED A COST.
        //
        //  Assets/Editor/UICaptureLaunch.cs handed the build-mode ghost pill a hand-written
        //  wood + iron + crystals cost string. Three separate things were wrong with it and only
        //  the third is obvious:
        //    (a) the catalog has NEVER held that basket -- the authored Arcane Spire row is
        //        iron 360, nothing else;
        //    (b) its SHAPE -- wood AND iron AND crystals in one basket -- is precisely what
        //        case 1 forbids for a catalog row, so the harness was painting a basket the
        //        game is structurally incapable of charging;
        //    (c) it lived in EDITOR code, so every case above was blind to it. The data cases
        //        read JSON; the [cs-basket] lint scopes Assets/_Modules. Nothing looked here.
        //
        //  The cost of that blind spot was not cosmetic. The fabricated line went into
        //  BuildGhostChips_*.png, and BOTH independent UI reviewers (docs/qa/UI_REVIEW_
        //  2026-09-05) quoted it back as evidence of what the player sees, which put it into
        //  two work orders and a runtime docstring. A capture that paints numbers the game
        //  does not use is not a capture of the game -- it manufactures consensus around a
        //  number nobody ever measured, which is the exact failure CLAUDE.md section 11B exists to stop.
        //
        //  THE DISCRIMINATOR IS A DIGIT IN A STRING LITERAL, and it is chosen deliberately.
        //  Flagging any literal argument would forbid the NAME argument too, which is a
        //  legitimate label (and "Arcane Spire" holds no digit). A cost, a duration or a
        //  count cannot be written without one. So: a SetPlacingLabel call whose arguments
        //  contain a string literal carrying a digit is authoring a NUMBER into a capture,
        //  and the harness must read that number from the live catalog instead.
        //
        //  Comments are stripped first, so the historical note in UICaptureLaunch.cs that
        //  explains this very bug cannot trip the gate that documents it.
        // =====================================================================
        private const string CaptureHarnessRelPath = "Assets/Editor/UICaptureLaunch.cs";

        private static readonly Regex PlacingLabelRx = new Regex(
            @"SetPlacingLabel\s*\((?<args>[^;]*?)\)\s*;", RegexOptions.Compiled);

        private static readonly Regex StringLiteralRx = new Regex(
            @"@?""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);

        /// <summary>
        /// Every SetPlacingLabel site in <paramref name="code"/> that hands the pill a string
        /// literal containing a digit, collapsed to one line each. Comments are stripped first.
        /// Factored out so the case can RED-PROVE itself against an inline broken fixture --
        /// a lint that has never been seen to fire is a lint nobody has tested.
        /// </summary>
        internal static List<string> FindHardcodedPlacingBaskets(string code)
        {
            var hits = new List<string>();
            if (string.IsNullOrEmpty(code)) return hits;

            string stripped = StripComments(code);
            foreach (Match m in PlacingLabelRx.Matches(stripped))
            {
                string args = m.Groups["args"].Value;
                if (string.IsNullOrEmpty(args)) continue;
                foreach (Match lit in StringLiteralRx.Matches(args))
                {
                    if (!Regex.IsMatch(lit.Value, @"\d")) continue;
                    hits.Add(Collapse(args));
                    break;   // one finding per call site, however many literals it holds
                }
            }
            return hits;
        }

        private static void CaseCaptureHarnessLabels(List<string> failures, StringBuilder log)
        {
            // ---- RED PROOF: the detector must SEE the shape it exists to catch. -------------
            const string redFixture =
                "void F() { hud.SetPlacingLabel(\"Ghost Row\", \"12 wood, 34 iron, 56 crystals\"); }";
            int redHits = FindHardcodedPlacingBaskets(redFixture).Count;
            if (redHits != 1)
            {
                failures.Add("[capture-basket] RED PROOF FAILED: a deliberately-broken fixture with a hardcoded " +
                             "cost literal produced " + redHits + " finding(s), expected exactly 1. The detector " +
                             "is not detecting, so its pass on the real harness proves nothing.");
                return;
            }

            // ---- GREEN PROOF: the corrected shape must NOT fire, or the gate cries wolf. ----
            const string greenFixture =
                "void F() { hud.SetPlacingLabel(entry.displayName, " +
                "StructureCardVM.PlacementSummaryFor(entry).CostWords); }";
            int greenHits = FindHardcodedPlacingBaskets(greenFixture).Count;
            if (greenHits != 0)
            {
                failures.Add("[capture-basket] GREEN PROOF FAILED: the CORRECT shape (label composed from the live " +
                             "catalog row) produced " + greenHits + " finding(s), expected 0. A lint that flags " +
                             "correct code gets muted, and a muted gate protects nothing.");
                return;
            }

            // ---- COMMENT PROOF: the in-code history of this bug must not trip its own gate. -
            const string commentFixture =
                "// SetPlacingLabel(\"Ghost Row\", \"12 wood, 34 iron, 56 crystals\") was the bug.\n" +
                "void F() { hud.SetPlacingLabel(entry.displayName, words); }";
            int commentHits = FindHardcodedPlacingBaskets(commentFixture).Count;
            if (commentHits != 0)
            {
                failures.Add("[capture-basket] COMMENT PROOF FAILED: a COMMENTED quotation of the old bad call " +
                             "produced " + commentHits + " finding(s), expected 0 -- documented history would be " +
                             "unwritable without disabling the gate.");
                return;
            }

            // ---- The live harness. ----------------------------------------------------------
            string path = Path.Combine(Application.dataPath, "Editor", "UICaptureLaunch.cs");
            if (!File.Exists(path))
            {
                failures.Add("[capture-basket] " + CaptureHarnessRelPath + " not found at " + path + " -- the " +
                             "capture-harness lint could not run, so a hardcoded cost painted into every UI PNG " +
                             "would pass unseen. If the harness moved, MOVE THIS PATH; do not delete the case.");
                return;
            }

            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("[capture-basket] could not read " + CaptureHarnessRelPath + ": " + ex.Message);
                return;
            }

            var hits = FindHardcodedPlacingBaskets(src);
            foreach (string hit in hits)
            {
                failures.Add("[capture-basket] " + CaptureHarnessRelPath + " hands the build-mode ghost pill a " +
                             "HARDCODED string literal carrying a number: \"" + hit + "\". A capture that paints " +
                             "numbers the game does not use is not a capture of the game -- WO-1478's fabricated " +
                             "\"wood + iron + crystals\" basket reached two work orders and two independent UI " +
                             "reviews before anyone checked it against the catalog. Compose the label from the " +
                             "live row instead: CatalogRegistry.Get(id) -> StructureCardVM.PlacementSummaryFor(" +
                             "entry).CostWords (or CostFormat over BuildModeController.SoftcappedCostFor).");
            }

            log.AppendLine("  [capture-basket] red/green/comment proofs passed; linted " + CaptureHarnessRelPath +
                           " -- " + hits.Count + " hardcoded SetPlacingLabel literal(s)");
        }

        // =====================================================================
        //  Parse -- the SAME settings CatalogBootstrap.LoadFromJson uses.
        // =====================================================================
        private static List<CatalogEntry> ParseCatalog(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(CatalogRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[cost-basket] " + CatalogRelPath + " unreadable (CanonicalJson.Read returned empty) - " +
                             "no basket is verifiable at all");
                return null;
            }
            StructuresFile file;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = { new StringEnumConverter() },
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                file = JsonConvert.DeserializeObject<StructuresFile>(json, settings);
            }
            catch (Exception ex)
            {
                failures.Add("[cost-basket] structures-catalog.json failed to parse: " + ex.Message);
                return null;
            }
            if (file == null || file.Entries == null || file.Entries.Count == 0)
            {
                failures.Add("[cost-basket] structures-catalog.json deserialized to 0 entries");
                return null;
            }
            log.AppendLine("  structures-catalog.json v" + file.Version + " -> " + file.Entries.Count + " row(s)");
            return file.Entries;
        }

        // =====================================================================
        //  CASES 1 + 2 -- the invariant, and crystals-only-where-pinned.
        // =====================================================================
        private static void CaseInvariantAndRegular(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            int clean = 0;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || e.repo == null) continue;
                bool pending = PendingPins.ContainsKey(e.id);
                bool magical = MagicalIds.Contains(e.id);
                bool dirty = false;

                // Every authored basket on the row: the build cost + each upgrade step.
                // ResourceCost is a STRUCT (Core/Catalog/RepoProps.cs) - never null; the
                // array itself is the only nullable part.
                var baskets = new List<KeyValuePair<string, ResourceCost>>
                {
                    new KeyValuePair<string, ResourceCost>("build cost", e.repo.cost),
                };
                if (e.repo.upgradeCost != null)
                    for (int i = 0; i < e.repo.upgradeCost.Length; i++)
                        baskets.Add(new KeyValuePair<string, ResourceCost>("upgrade L" + (i + 1) + "->L" + (i + 2),
                                                                           e.repo.upgradeCost[i]));

                foreach (var b in baskets)
                {
                    var c = b.Value;
                    string where = "'" + e.id + "' " + b.Key + " (w" + c.wood + " f" + c.stone + " i" + c.iron + " c" + c.crystals + ")";

                    // -- CASE 1 [invariant] -- never all three.
                    if (c.wood > 0 && c.iron > 0 && c.crystals > 0)
                    {
                        dirty = true;
                        if (!pending)
                            failures.Add("[invariant] " + where + " holds wood AND iron AND crystals - the WO-947 " +
                                         "owner ruling (2026-08-10) forbids a basket that touches all three. Pick a " +
                                         "side: regular -> wood + iron, magical/ethereal -> crystal-based.");
                    }

                    // -- CASE 2 [regular] -- crystals only on the pinned magical set.
                    if (c.crystals > 0 && !magical)
                    {
                        dirty = true;
                        if (!pending)
                            failures.Add("[regular] " + where + " charges crystals but '" + e.id + "' is not on the " +
                                         "pinned MAGICAL set - WO-947: regular structures cost wood + iron (+/- food) " +
                                         "and NEVER crystals. If this row IS magical/ethereal it needs an OWNER pin " +
                                         "(WO-947 section 4), not a code-side classification.");
                    }
                }

                // -- CASE 2b -- the back-door crystal charge. An all-zero authored basket
                // makes BuildModeController.CostFor fall through to charging `buildCost`
                // in CRYSTALS, which prices a regular structure in crystals invisibly.
                bool authoredZero = e.repo.cost.IsZero;
                bool affordGated = e.repo.placement == null || e.repo.placement.checkAffordable;
                if (authoredZero && affordGated && e.repo.buildCost > 0 && !magical)
                {
                    dirty = true;
                    if (!pending)
                        failures.Add("[regular] '" + e.id + "' authors NO repo.cost (all-zero) but buildCost " +
                                     e.repo.buildCost + " - CostFor's back-compat fallback charges that in pure " +
                                     "CRYSTALS, so this regular structure is crystal-priced through the back door " +
                                     "(WO-947). Author a wood + iron basket.");
                }

                if (!dirty && !pending) clean++;
            }
            log.AppendLine("  [invariant]+[regular] " + clean + " row(s) conform outright; " +
                           PendingPins.Count + " carried as dated WO-947 pending pins; " +
                           MagicalIds.Count + " row(s) on the pinned MAGICAL set");
        }

        // =====================================================================
        //  CASE 3 [pins] -- the exemption list may only shrink, and every pin is
        //  surfaced distinguishably (no silent failures, CLAUDE.md section 12).
        // =====================================================================
        private static void CasePendingPinsStillStand(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            if (PendingPins.Count == 0)
            {
                log.AppendLine("  [pins] NONE OPEN - every WO-947 classification pin was answered by the owner on " +
                               "2026-08-14 and applied in structures-catalog.json v18 + v19. The exemption list is empty, " +
                               "so no row is excused from the ruling.");
                return;
            }

            var byId = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                if (e != null && !string.IsNullOrEmpty(e.id) && !byId.ContainsKey(e.id)) byId[e.id] = e;

            foreach (var pin in PendingPins)
            {
                CatalogEntry e;
                if (!byId.TryGetValue(pin.Key, out e) || e == null || e.repo == null)
                {
                    failures.Add("[pins] WO-947 pending-pin id '" + pin.Key + "' is no longer a catalog row - the " +
                                 "exemption is stale and would silently excuse whatever takes its place. Delete it " +
                                 "from PendingPins (or restore the row).");
                    continue;
                }

                bool stillViolates = Violates(e.repo.cost);
                if (e.repo.upgradeCost != null)
                    foreach (var step in e.repo.upgradeCost)
                        if (Violates(step)) stillViolates = true;

                if (!stillViolates)
                {
                    failures.Add("[pins] WO-947 pending-pin id '" + pin.Key + "' NO LONGER violates the cost-basket " +
                                 "ruling - its pin has evidently been answered and applied. REMOVE it from " +
                                 "PendingPins in the same change so the exemption cannot hide the next regression " +
                                 "(and add it to MagicalIds if the owner pinned it magical).");
                }
                else
                {
                    log.AppendLine("  [pins] OPEN - '" + pin.Key + "' still on its pre-ruling basket. " + pin.Value);
                }
            }
        }

        /// <summary>Does this basket breach the ruling for a row with no owner pin?</summary>
        private static bool Violates(ResourceCost c)
        {
            return c.crystals > 0;   // crystals outside the pinned magical set, incl. the all-three case
        }

        // =====================================================================
        //  CASE 4 [applied] -- the one conversion WO-947 has actually made.
        // =====================================================================
        private static void CaseAppliedRowStaysConverted(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            var byId = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                if (e != null && !string.IsNullOrEmpty(e.id) && !byId.ContainsKey(e.id)) byId[e.id] = e;

            foreach (var kv in AppliedRows)
            {
                string id = kv.Key;
                AppliedRow spec = kv.Value;

                CatalogEntry row;
                if (!byId.TryGetValue(id, out row) || row == null || row.repo == null)
                {
                    failures.Add("[applied] '" + id + "' is missing from the catalog - a row WO-947 has been " +
                                 "applied to cannot be verified. " + spec.Why);
                    continue;
                }

                var baskets = new List<ResourceCost> { row.repo.cost };
                if (row.repo.upgradeCost != null)
                    foreach (var step in row.repo.upgradeCost) baskets.Add(step);

                if (baskets.Count != spec.Totals.Length)
                {
                    failures.Add("[applied] '" + id + "' authors " + baskets.Count + " basket(s), expected " +
                                 spec.Totals.Length + " (build + upgrade steps) - the WO-947 conversion was " +
                                 "authored against that ladder shape. " + spec.Why);
                    continue;
                }

                for (int i = 0; i < baskets.Count; i++)
                {
                    var c = baskets[i];
                    string which = i == 0 ? "build cost" : "upgrade step " + i;

                    if (spec.Magical)
                    {
                        // Magical basket = CRYSTALS + IRON (owner pin 1, 2026-08-14). Wood is out.
                        // ⚠ THE PAIRING HALF OF WO-947 IS UNTOUCHED BY THE 2026-08-26 RULING BELOW.
                        // No wood may ever enter a magical row, at ANY rung. That is still checked
                        // on EVERY basket, exactly as it was.
                        if (c.wood != 0)
                            failures.Add("[applied] '" + id + "' " + which + " charges " + c.wood + " wood - WO-947 " +
                                         "converted this row to MAGICAL (crystals + iron, NEVER wood). It has been " +
                                         "reverted or re-authored. " + spec.Why);
                    }
                    else
                    {
                        // Regular basket = WOOD + IRON (+/- food). Crystals are out.
                        if (c.crystals != 0)
                            failures.Add("[applied] '" + id + "' " + which + " charges " + c.crystals + " crystals - " +
                                         "WO-947 converted this row to REGULAR (wood + iron, NEVER crystals). It has " +
                                         "been reverted. " + spec.Why);
                    }

                    int total = c.wood + c.stone + c.iron + c.crystals;
                    if (total != spec.Totals[i])
                        failures.Add("[applied] '" + id + "' " + which + " totals " + total + ", expected " +
                                     spec.Totals[i] + " - every WO-947 conversion folded the dropped resource 1:1 " +
                                     "into the surviving side specifically so first-cost FEEL was unchanged. A " +
                                     "different total is a balance change riding on a composition ruling; if it is " +
                                     "intended, update AppliedRows with the owner's new numbers. " + spec.Why);
                }

                // =============================================================
                // ⛔ RE-POINTED BY OWNER RULING 2026-08-26 (WO-1217). READ THIS BEFORE
                //    TOUCHING IT -- it was moved by a RULING, not weakened to fit a change.
                //
                // WHAT THE RULE WAS (2026-08-14 -> 2026-08-26): a MAGICAL row had to carry
                //   crystals > 0 in EVERY authored basket -- build cost AND every upgrade
                //   step. That assertion lived inside the per-basket loop above.
                //
                // WHAT IT IS NOW: a MAGICAL row must carry crystals > 0 AT ITS TOP TIER --
                //   the LAST authored basket. It is still crystal-BASED; the crystal gate
                //   simply sits at the final rung instead of every rung.
                //
                // WHY IT CHANGED: the owner ruled on 2026-08-26 that on
                //   'tower_arcane_spire' ONLY tier 3 costs crystals -- the build cost and
                //   the L1->L2 step are now crystal-FREE, their crystals folded 1:1 into
                //   IRON. That is a deliberate progression shape (iron gets you started,
                //   crystals gate the top of the ladder), not a drift. The old
                //   every-basket assertion is therefore FALSE AS WRITTEN and must be
                //   re-pointed at the rung the ruling actually protects.
                //
                // WHAT IS *NOT* WEAKENED -- canon law here is "pin the exception, never
                // soften the rule":
                //   * WO-947 pin 1's PAIRING rule is fully intact and still enforced on
                //     EVERY basket, above: NO WOOD may enter a magical row at any rung.
                //   * A magical row that carries ZERO crystals ANYWHERE still FAILS here.
                //     "Crystal-based" is not optional; only its POSITION on the ladder moved.
                //   * Case 2 [regular] is untouched: crystals still appear on NO row outside
                //     the pinned MagicalIds set.
                //   * The frozen Totals[] still pin every rung, so the crystals that left
                //     build/L2 had to land in iron 1:1 or case 4's total check trips.
                // A future single-basket magical row (no upgrade ladder) is unaffected: its
                // build cost IS its top tier.
                // =============================================================
                if (spec.Magical && baskets.Count > 0)
                {
                    var top = baskets[baskets.Count - 1];
                    string topWhich = baskets.Count == 1 ? "build cost" : "upgrade step " + (baskets.Count - 1);
                    if (top.crystals <= 0)
                        failures.Add("[applied] '" + id + "' TOP TIER (" + topWhich + ") charges NO crystals - a " +
                                     "magical row under WO-947 is crystal-BASED, and the owner's 2026-08-26 ruling " +
                                     "moved that requirement to the FINAL rung, it did not remove it. Lower rungs " +
                                     "may be crystal-free (iron gets you started, crystals gate the top); the top " +
                                     "rung may not. " + spec.Why);
                }

                log.AppendLine("  [applied] '" + id + "' converted " + (spec.Magical ? "MAGICAL (crystals+iron)" : "REGULAR (wood+iron)") +
                               ", totals " + string.Join("/", Array.ConvertAll(spec.Totals, t => t.ToString())) + " preserved");
            }
        }

        // =====================================================================
        //  CASE 7 [repair-carve-out] -- PROD-014 slice (d). THE ONE PLACE CRYSTALS
        //  MAY TOUCH A REGULAR STRUCTURE, AND THE FENCE AROUND IT.
        // ---------------------------------------------------------------------
        //  OWNER RULING 2026-08-24 AMENDS WO-947, it does not bypass it. The ruling now
        //  reads: regular structures are BUILT and UPGRADED with wood + iron, magical
        //  ones with crystals, and REPAIR may be paid in CRYSTALS for anything.
        //  OWNER RULING 2026-08-26 sets the number: 1.0 CRYSTAL PER IRON, chosen 60%
        //  above the measured 0.625 natural-exchange floor (the $1.99 impulse rung) so a
        //  player who HAS iron still spends iron.
        //
        //  THE EXCEPTION IS THE POINT; THE ENFORCEMENT STAYS. The WO says this suite must
        //  ENCODE the exception explicitly, and warns what happens if it is instead
        //  loosened or deleted: the separation stops being enforced at all and the next
        //  accidental crystal cost lands silently. So this case does not relax cases 1-4
        //  by one row -- repair_default carries crystals 0 in its basket and is still
        //  judged by them exactly like every other regular row. It adds a FENCE around
        //  the new field:
        //
        //    a  repair_default authors repo.repairCrystalsPer.perIron == the RULED rate.
        //    b  NO OTHER ROW authors a rate. The carve-out is a property of the repair
        //       ECONOMY, and the moment it can be authored per-structure it is a crystal
        //       cost on a regular building wearing a different name.
        //    c  the repair pricing row itself carries NO crystal build cost.
        //    d  BEHAVIOUR: WallRepairController.CostForFraction emits crystals = 0 at
        //       every damage fraction, even from a build cost that carries crystals. The
        //       base price stays in kind; crystals are only ever an opt-in top-up.
        //    e  BEHAVIOUR: the conversion prices ONLY the slots that have an authored
        //       rate, and reports the rest NOT CONVERTIBLE -- a rate of 0 must never read
        //       as "free", which is the free-repair exploit MaterialsZero was fixed for.
        //
        //  RED-FIRST (WO-1138 / PROD-008). Rules a-c are a PURE predicate, run first over
        //  four rows whose defects are authored on purpose. If any of those stays silent
        //  the live pass proves nothing and this case fails on that instead.
        // =====================================================================

        /// <summary>The ONE row allowed to author a crystals-for-repair rate.</summary>
        private const string RepairRateRowId = "repair_default";

        /// <summary>OWNER RULING 2026-08-26. Changing this number takes another ruling, not an edit.</summary>
        private const float RuledCrystalsPerIron = 1.0f;

        private static void CaseRepairCrystalCarveOut(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            // --- RED FIRST: the predicate must SEE each defect --------------------
            var redIds   = new[] { RepairRateRowId, RepairRateRowId, "tower_ballista", RepairRateRowId };
            var redRates = new[]
            {
                default(RepairCrystalRate),
                new RepairCrystalRate { perIron = 0.5f },
                new RepairCrystalRate { perIron = RuledCrystalsPerIron },
                new RepairCrystalRate { perIron = RuledCrystalsPerIron },
            };
            var redCrystals = new[] { 0, 0, 0, 30 };
            var redWhat = new[]
            {
                "the carve-out DELETED (no rate authored at all)",
                "a rate BELOW the natural-exchange floor (crystals cheaper than earning the iron)",
                "the rate LEAKING onto an ordinary structure row",
                "a crystal BUILD cost on the repair pricing row",
            };
            for (int i = 0; i < redIds.Length; i++)
            {
                var probe = new List<string>();
                InspectRepairRateRow(redIds[i], redRates[i], redCrystals[i], probe);
                if (probe.Count == 0)
                {
                    failures.Add("[repair-carve-out] RED PROOF FAILED: " + redWhat[i] + " produced NO finding. " +
                                 "The fence around the WO-947 repair exception cannot see its own defect, so a " +
                                 "clean pass below is worth nothing.");
                    return;
                }
                log.AppendLine("  [repair-carve-out] red-proof " + (i + 1) + ": " + redWhat[i] + " -> " + probe[0]);
            }
            // ...and stay silent on the correct shape, or it would be suppressed within a week.
            var quiet = new List<string>();
            InspectRepairRateRow(RepairRateRowId, new RepairCrystalRate { perIron = RuledCrystalsPerIron }, 0, quiet);
            InspectRepairRateRow("tower_ballista", default(RepairCrystalRate), 0, quiet);
            if (quiet.Count != 0)
            {
                failures.Add("[repair-carve-out] the predicate fires on the CORRECT shape (" +
                             string.Join(" | ", quiet) + ") - a rule that reddens healthy data gets muted, not fixed.");
                return;
            }

            // --- a / b / c: the LIVE catalog --------------------------------------
            bool sawRateRow = false;
            foreach (var e in entries)
            {
                if (e == null || e.repo == null) continue;
                if (string.Equals(e.id, RepairRateRowId, StringComparison.OrdinalIgnoreCase)) sawRateRow = true;
                InspectRepairRateRow(e.id, e.repo.repairCrystalsPer, e.repo.cost.crystals, failures);
            }
            if (!sawRateRow)
                failures.Add("[repair-carve-out] the '" + RepairRateRowId + "' row is MISSING from the catalog. " +
                             "It is where the crystals-for-repair rate is authored AND the fallback price for " +
                             "every structure with no cost row of its own - losing it silently disables both.");

            // --- d: repair pricing never emits crystals ----------------------------
            var withCrystals = new DeNelle.Core.Catalog.ResourceCost { wood = 120, stone = 0, iron = 60, crystals = 99 };
            var fractions = new[] { 0.25f, 0.5f, 1f };
            for (int i = 0; i < fractions.Length; i++)
            {
                var priced = WallRepair.CostForFraction(fractions[i], withCrystals);
                if (priced.crystals != 0)
                {
                    failures.Add("[repair-carve-out] WallRepairController.CostForFraction(" + fractions[i] +
                                 ") emitted " + priced.crystals + " crystals into the BASE repair price. The " +
                                 "2026-08-24 amendment is an opt-in TOP-UP, not a crystal slot: the moment the " +
                                 "base price carries crystals, every regular structure has a crystal cost and " +
                                 "WO-947 is gone.");
                    break;
                }
            }

            // --- e: the conversion prices only what has an authored rate -----------
            var ironShort = new DeNelle.Core.Catalog.ResourceCost { iron = 115 };
            var ruled = new RepairCrystalRate { perIron = RuledCrystalsPerIron };
            var price = WallRepair.CrystalPriceFor(ironShort, ruled, out bool ironOk);
            if (!ironOk || price.crystals != 115)
                failures.Add("[repair-carve-out] a 115-iron shortfall priced " + price.crystals + " crystals " +
                             "(convertible=" + ironOk + ") at the ruled " + RuledCrystalsPerIron + "/iron. The " +
                             "owner's own worked example is 115 iron short = 115 crystals.");
            if (price.wood != 0 || price.stone != 0 || price.iron != 0)
                failures.Add("[repair-carve-out] the crystal top-up came back carrying materials (" +
                             price.wood + "w/" + price.stone + "f/" + price.iron + "i). It must be crystals ONLY - " +
                             "the in-kind part of the price is the part the wallet already covers.");

            var woodShort = new DeNelle.Core.Catalog.ResourceCost { wood = 10 };
            var noRate = WallRepair.CrystalPriceFor(woodShort, ruled, out bool woodOk);
            if (woodOk)
                failures.Add("[repair-carve-out] a WOOD shortfall reported CONVERTIBLE with no perWood rate " +
                             "authored, priced at " + noRate.crystals + " crystals. An unauthored rate means NOT " +
                             "CONVERTIBLE, never free - the owner ruled one number (iron) and inventing the " +
                             "others is economy policy.");

            log.AppendLine("  [repair-carve-out] rate authored on '" + RepairRateRowId + "' ONLY, at " +
                           RuledCrystalsPerIron + " crystals/iron (floor " +
                           WallRepair.NaturalExchangeFloorCrystalsPerIron + "); base repair price " +
                           "still emits zero crystals; unauthored slots are not convertible.");
        }

        /// <summary>
        /// The pure fence, over ONE row's authored data. Kept parameterised (rather than reading
        /// the entry) so the red-proof above can hand it defects that do not exist in the catalog.
        /// </summary>
        private static void InspectRepairRateRow(string id, RepairCrystalRate rate, int costCrystals, List<string> into)
        {
            bool isRateRow = string.Equals(id, RepairRateRowId, StringComparison.OrdinalIgnoreCase);

            if (!isRateRow)
            {
                if (!rate.IsZero)
                    into.Add("[repair-carve-out] row '" + id + "' authors a repairCrystalsPer rate. ONLY '" +
                             RepairRateRowId + "' may: the crystals-for-repair exception is a property of the " +
                             "repair ECONOMY, and a per-structure rate is a crystal price on a regular building " +
                             "under another name. Delete it, or take an owner ruling.");
                return;
            }

            if (rate.perIron <= 0f)
                into.Add("[repair-carve-out] '" + RepairRateRowId + "' authors NO crystals-per-iron rate. The " +
                         "owner ruled 1.0 on 2026-08-26; with the rate gone, a refused repair has no crystal " +
                         "option at all and PROD-014 slice (d) is silently un-shipped.");
            else if (rate.perIron != RuledCrystalsPerIron)
                into.Add("[repair-carve-out] crystals-per-iron is " + rate.perIron + ", not the RULED " +
                         RuledCrystalsPerIron + " (owner 2026-08-26). This number is a ruling, not a tuning knob" +
                         (rate.perIron <= WallRepair.NaturalExchangeFloorCrystalsPerIron
                            ? " - and it is at or BELOW the measured natural-exchange floor (" +
                              WallRepair.NaturalExchangeFloorCrystalsPerIron + "), which makes crystals " +
                              "the CHEAP way to repair and retires iron's sink"
                            : "") + ".");

            if (costCrystals != 0)
                into.Add("[repair-carve-out] '" + RepairRateRowId + "' authors a crystal BUILD cost (" +
                         costCrystals + "). It is the fallback price for every structure with no cost row of its " +
                         "own, so a crystal slot here charges crystals to repair ordinary buildings - the exact " +
                         "leak the carve-out is fenced against.");
        }
    }
}
