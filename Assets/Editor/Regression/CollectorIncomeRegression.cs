// =============================================================================
// CollectorIncomeRegression [collector-income]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers: COLLECTOR_INCOME_OK / COLLECTOR_INCOME_FAIL.
//
// ⚠ WHAT THIS SUITE ACTUALLY DOES (WO-1494, 2026-09-06 - verified by reading every case body,
// not by trusting this header):
//   SOURCE LINT - cases 1-12 and 14-16. They Regex the harvester/collector/view .cs and parse
//               the canonical JSON catalogs. They prove a call site, a guard or an authored
//               number is present, absent or ordered correctly; they CANNOT see runtime state.
//   HYBRID     - case 13 [configure-rekeys] ONLY: the lint half above MeasureConfigureRekey,
//               plus a real measurement that constructs a live ResourceCollector, calls
//               Configure and reads the real ResourceCollectorRegistry back.
//   Where a case description below says a defect was "measured" on a date, that is the HISTORY
//   of the headless/device capture that FOUND it - not a claim that this suite re-measures it.
//
// THE DEFECT CLASS THIS PINS (measured 2026-08-04): PHANTOM COLLECTOR INCOME.
//
// ResourceBuildingHarvester.Update iterated all three
// ResourceBuildingProgression.OrderedIds (farm / lumbermill / forge)
// UNCONDITIONALLY. Its only guard was `GetLevel(id) < 1`, and
// ResourceBuildingState.GetLevel is `PlayerPrefs.GetInt(key, 1)` clamped to >= 1
// (ResourceBuildingState.cs:63-69) - it DEFAULTS TO 1 and never asks whether the
// building exists, so `level < 1` is unreachable. An EMPTY town therefore earned
// farm + lumbermill + forge income from t=0; and because no ResourceCollector was
// registered for a building that was never placed, the income fell through to a
// direct-grant fallback that banked it straight into the wallet - UNCAPPED,
// AUTO-BANKED, bypassing the capped pending pool, the manual Collect tap and the
// siege-loot risk that are the entire point of the WO-663 CoC collector spine.
// Post-WO-855 that free baseline is ~720 wood + 936 food + 432 iron per hour for a
// town with nothing in it, and PLACING the collector made income strictly WORSE.
//
// The fix gates the per-id tick on the building actually having been built, proven
// by the persisted WO-834 ledger GameState.EverBuiltStructureIds (save v36) or by a
// live registered ResourceCollector, and DELETES the direct-grant fallback so the
// capped pending pool is the only payout path.
//
// WO-859 / WO-900 (2026-08-04) extended this suite from six cases to thirteen. The new half
// covers the SECOND defect the first fix missed and the loop built on top of it:
//
//   7  [offline-capped]         away accrual shares the ONLINE rate authority
//                               (EffectiveYieldPerTick), carries an int-overflow guard, and is
//                               bounded by CAPACITY - the cap actually binds at the authored
//                               numbers (an hour fits, thirty days does not).
//   8  [stamp-advances-at-cap]  THE HIGHEST-RISK ONE. The last-accrual stamp write must sit
//                               OUTSIDE Accrue's `if (_pending > before)` block, or it freezes
//                               when the pool caps and the next Collect refills instantly from a
//                               frozen backlog - the capacity cap would then bound nothing.
//   9  [capacity-hours-stable]  capacity is HOURS: the flat `1 + 0.5*(level-1)` multiplier is
//                               gone, harvestRate is excluded from the capacity basis, and every
//                               collector still holds ~8h of level-1 production.
//   10 [fallback-gated]         P0. EnsureFallbackCollector must consult the ever-built ledger.
//                               A live collector opens MayHarvest unconditionally, so an
//                               unconditional DDOL fallback re-opened the phantom-income gate for
//                               a BLANK TOWN and paid full town income during DUNGEON runs.
//   11 [no-crystal-faucet]      no collector routes at Crystals (uncapped premium currency).
//   12 [tell-wired]             CollectorStackView.Attach actually has a caller, the tell stays
//                               event-driven, the FULL toast is coalesced, and the copy never
//                               says "Storage" (that word is the town bank's).
//   13 [configure-rekeys]       SOURCE LINT + MEASURED (WO-1494). AddComponent fires OnEnable before
//                               Configure, so every collector registered under the default id "farm";
//                               Configure must re-key the registry. The DEFECT was measured headless
//                               2026-08-04. The lint half greps Configure, so a DELETED call site is
//                               caught; the measured half drives a real ResourceCollector and reads
//                               the real ResourceCollectorRegistry, so a call site that is PRESENT
//                               AND WRONG is caught too.
//
// THE ORIGINAL SIX CASES:
//
//   1 [gate-live]      Source: the harvester consults the existence rule BEFORE the
//                      level read, and reads the ever-built ledger. FAILS on the
//                      pre-fix tree (there was no gate at all).
//
//   2 [no-direct-grant] Source: the uncapped auto-banking payout is GONE - no
//                      EconomyService.Grant / ResourceLedger.Credit anywhere in the
//                      harvester, exactly one collector.Accrue payout, and the
//                      no-collector branch traces + withholds instead of paying.
//                      FAILS on the pre-fix tree.
//
//   3 [empty-town-zero] Behavioral truth table on the PURE rule
//                      ResourceBuildingHarvester.MayHarvest: an empty town (nothing
//                      ever built, no live collector) earns ZERO for every id; an
//                      unrelated ledger and a null ledger earn zero too.
//
//   4 [built-earns]    A built farm earns farm income and ONLY farm income (no bleed
//                      to lumbermill/forge), the match is case-insensitive (the
//                      MarkEverBuilt convention), and a live standing collector
//                      produces even with an empty ledger.
//
//   5 [catalog-map]    Data: each progression id resolves to EXACTLY ONE Collector
//                      catalog row via repo.collectorBuildingId, and the BARE id
//                      (lumbermill / forge also exist as GameplayBuilding storefront
//                      rows) is NOT a Collector row - so building the Forge
//                      STOREFRONT can never open the forge COLLECTOR's harvest gate.
//
//   6 [founding-flow]  THE SOFT-LOCK GUARD. Starting wood and iron are 0
//                      (StartingBudget), so the free-first-build flags ARE the
//                      starting budget. Every mandatory founding placement must be
//                      free (FoundingKit or a non-tower lane-3 first-of-each-id
//                      freebie), the founding collector must be a real Collector row
//                      whose placement OPENS the gate, and a freshly placed
//                      (level-1) collector must be able to accrue - ResourceCollector
//                      .IsActive must NOT require level > 1, or the zero-seed
//                      founding bootstrap dead-locks the moment the direct-grant
//                      fallback is removed.
//
// Standalone: run-unity-method DeNelle.Editor.Regression.CollectorIncomeRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Village.Buildings.Progression;

namespace DeNelle.Editor.Regression
{
    public static class CollectorIncomeRegression
    {
        private const string HarvesterSrc = "Assets/_Modules/Village/Buildings/Progression/ResourceBuildingHarvester.cs";
        private const string CollectorSrc = "Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs";
        private const string BuildModeSrc = "Assets/_Modules/Village/BuildMode/BuildModeController.cs";
        private const string BootstrapSrc = "Assets/_Modules/Village/Buildings/Progression/ResourceCollectorBootstrap.cs";
        private const string ViewSrc = "Assets/_Modules/Village/Buildings/Progression/CollectorStackView.cs";
        private const string FactorySrc = "Assets/_Modules/Village/Catalog/StructureFactory.cs";
        private const string StructuresRes = "Assets/Resources/Data/Canonical/structures-catalog.json";
        private const string StructuresSA = "Assets/StreamingAssets/Data/Canonical/structures-catalog.json";
        private const string StepsRes = "Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json";
        // WO-1416 [quarry-pays-stone] targets - the three surfaces that must agree.
        private const string RoleSrc = "Assets/_Modules/Core/Catalog/StructureRole.cs";
        private const string InteractableSrc = "Assets/_Modules/Village/Buildings/BuildingInteractable.cs";
        private const string VendorNpcSrc = "Assets/_Modules/Village/NPCs/CastleVendorNpcInjector.cs";

        // =====================================================================

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("COLLECTOR_INCOME_OK - " + reason);
            else Debug.LogError("COLLECTOR_INCOME_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            Case(failures, "gate-live", () => Case1_GateLive(failures, notes));
            Case(failures, "no-direct-grant", () => Case2_NoDirectGrant(failures, notes));
            Case(failures, "empty-town-zero", () => Case3_EmptyTownZero(failures, notes));
            Case(failures, "built-earns", () => Case4_BuiltEarns(failures, notes));
            Case(failures, "catalog-map", () => Case5_CatalogMap(failures, notes));
            Case(failures, "founding-flow", () => Case6_FoundingFlow(failures, notes));
            Case(failures, "offline-capped", () => Case7_OfflineCapped(failures, notes));
            Case(failures, "stamp-advances-at-cap", () => Case8_StampAdvancesAtCap(failures, notes));
            Case(failures, "capacity-hours-stable", () => Case9_CapacityHoursStable(failures, notes));
            Case(failures, "fallback-gated", () => Case10_FallbackGated(failures, notes));
            Case(failures, "no-crystal-faucet", () => Case11_NoCrystalFaucet(failures, notes));
            Case(failures, "tell-wired", () => Case12_TellWired(failures, notes));
            Case(failures, "configure-rekeys", () => Case13_ConfigureRekeys(failures, notes));
            Case(failures, "popup-and-result-agree", () => Case14_PopupAndResultAgree(failures, notes));
            Case(failures, "overflow-stays-pending", () => Case15_OverflowStaysPending(failures, notes));
            Case(failures, "quarry-pays-stone", () => Case16_QuarryPaysStone(failures, notes));

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count > 0)
            {
                reason = "collector-income FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
                return false;
            }

            reason = "COLLECTOR INCOME OK - an empty town earns ZERO from every resource building, " +
                     "the existence gate runs before the level read and reads the WO-834 ever-built " +
                     "ledger, the uncapped auto-banking direct grant is gone (the capped pending pool " +
                     "is the only payout), a built farm earns farm income only, each progression id maps " +
                     "to exactly one Collector catalog row, and the zero-seed founding sequence still " +
                     "bootstraps (every demanded placement is free and a level-1 collector accrues). " +
                     "AND (WO-859/WO-900): the DDOL fallback collector is gated on the ever-built ledger so it " +
                     "can no longer back-door the existence gate, away accrual shares the online rate authority " +
                     "and is bounded by capacity, the last-accrual stamp advances even AT CAP (no frozen " +
                     "backlog), capacity is expressed in HOURS so fill time is level- and echo-invariant at " +
                     "~8h, no collector opens a crystal faucet, Configure re-keys the registry (MEASURED " +
                     "on a live ResourceCollector against the real ResourceCollectorRegistry, WO-1494), and the " +
                     "collector FULL tell is wired with a coalesced toast. AND (WO-1416): the Quarry " +
                     "pays STONE in all three of its answers - the role word, the harvest tick and the " +
                     "offline/welcome-back summary - off ONE producer" + noteStr;
            return true;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1 - the existence gate is live, and it runs BEFORE the level read
        // =====================================================================

        private static void Case1_GateLive(List<string> failures, List<string> notes)
        {
            string raw = ReadText(HarvesterSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            int update = code.IndexOf("private void Update", StringComparison.Ordinal);
            if (update < 0)
            {
                failures.Add("[gate-live] ResourceBuildingHarvester has no Update() - the harvest tick moved; re-point this oracle");
                return;
            }
            string body = code.Substring(update);

            int gate = body.IndexOf("MayHarvest", StringComparison.Ordinal);
            int levelRead = body.IndexOf("ResourceBuildingState.GetLevel", StringComparison.Ordinal);

            if (gate < 0)
            {
                failures.Add("[gate-live] the harvest tick does NOT consult the existence rule (MayHarvest) - every id " +
                             "ticks unconditionally again, so an EMPTY town earns free income from t=0 (the phantom-income defect)");
                return;
            }
            if (levelRead >= 0 && gate > levelRead)
                failures.Add("[gate-live] the existence gate runs AFTER ResourceBuildingState.GetLevel - GetLevel DEFAULTS TO 1 " +
                             "and can never report 'does not exist', so the level read must not be the first guard");

            if (code.IndexOf("EverBuilt", StringComparison.Ordinal) < 0)
                failures.Add("[gate-live] the harvester no longer reads the WO-834 EverBuiltStructureIds ledger - the persisted " +
                             "proof that a building was actually placed (save v36) is the whole basis of the gate");

            // The gate must also stop banking ELAPSED time for a building that does not
            // exist, or a town founded an hour in pays out an instant phantom backlog.
            if (!Regex.IsMatch(body, @"_elapsed\s*\[\s*i\s*\]\s*=\s*0f"))
                notes.Add("the closed-gate branch does not visibly zero _elapsed[i]; a never-built id may bank elapsed time");

            // Instrumentation (CLAUDE.md sec.12): a capture must name which ids ticked and
            // which were skipped for not existing.
            if (body.IndexOf("FlowTrace", StringComparison.Ordinal) < 0)
                failures.Add("[gate-live] the harvest tick writes no FlowTrace line - a capture cannot show which ids ticked " +
                             "and which were skipped for not existing (CLAUDE.md sec.12)");
        }

        // =====================================================================
        //  CASE 2 - the uncapped auto-banking direct grant is GONE
        // =====================================================================

        private static void Case2_NoDirectGrant(List<string> failures, List<string> notes)
        {
            string raw = ReadText(HarvesterSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            if (code.IndexOf("EconomyService", StringComparison.Ordinal) >= 0)
                failures.Add("[no-direct-grant] the harvester references EconomyService again - the direct grant banked harvest " +
                             "income straight into the wallet, UNCAPPED and with no Collect tap, which is the second half of the " +
                             "phantom-income defect (and it also paid full town income while the player was off in a dungeon)");

            if (Regex.IsMatch(code, @"ResourceLedger\s*\.\s*Credit\s*\("))
                failures.Add("[no-direct-grant] the harvester calls ResourceLedger.Credit again - harvest income must land in the " +
                             "collector's capped pending pool, never straight in the wallet");

            var accrue = Regex.Matches(code, @"\.\s*Accrue\s*\(");
            if (accrue.Count == 0)
                failures.Add("[no-direct-grant] the harvester never calls Accrue - the capped pending pool is the ONLY payout path; " +
                             "with it gone a built collector earns nothing at all");
            else if (accrue.Count > 1)
                failures.Add("[no-direct-grant] the harvester calls Accrue " + accrue.Count + " times in one tick - income would " +
                             "DOUBLE-COUNT for a building whose ResourceCollector is registered");

            // The withheld branch must be audible, not a silent skip.
            if (code.IndexOf("no-live-collector", StringComparison.Ordinal) < 0)
                notes.Add("the built-but-no-live-collector branch does not carry the 'no-live-collector' trace key; a wiring bug " +
                          "there would be invisible in a capture");
        }

        // =====================================================================
        //  CASE 3 - an empty town earns ZERO (the headline behavioral assertion)
        // =====================================================================

        private static void Case3_EmptyTownZero(List<string> failures, List<string> notes)
        {
            var empty = new List<string>();
            var unrelated = new List<string> { "workshop", "barracks", "pet-house", "lumberyard" };

            foreach (var id in ResourceBuildingProgression.OrderedIds)
            {
                var cat = ResourceBuildingHarvester.CatalogIdsForBuilding(id);
                if (cat == null || cat.Count == 0)
                {
                    failures.Add("[empty-town-zero] CatalogIdsForBuilding('" + id + "') resolved NOTHING - the gate would have " +
                                 "no id to test against the ledger");
                    continue;
                }

                if (ResourceBuildingHarvester.MayHarvest(cat, empty, false))
                    failures.Add("[empty-town-zero] '" + id + "' MAY HARVEST on an EMPTY town (nothing ever built, no live " +
                                 "collector) - that is the phantom-income defect: a town with nothing in it earning free, " +
                                 "uncapped, auto-banked income from t=0");

                if (ResourceBuildingHarvester.MayHarvest(cat, unrelated, false))
                    failures.Add("[empty-town-zero] '" + id + "' MAY HARVEST off a ledger holding only unrelated ids [" +
                                 string.Join(",", unrelated) + "] - building a workshop must not switch on the farm");

                if (ResourceBuildingHarvester.MayHarvest(cat, null, false))
                    failures.Add("[empty-town-zero] '" + id + "' MAY HARVEST off a NULL ledger - an absent/legacy list must read " +
                                 "as 'nothing built', never as 'everything built'");
            }
        }

        // =====================================================================
        //  CASE 4 - a built farm earns farm income (and nothing else does)
        // =====================================================================

        private static void Case4_BuiltEarns(List<string> failures, List<string> notes)
        {
            var farmIds = ResourceBuildingHarvester.CatalogIdsForBuilding(ResourceBuildingProgression.FarmId);
            var millIds = ResourceBuildingHarvester.CatalogIdsForBuilding(ResourceBuildingProgression.LumbermillId);
            var forgeIds = ResourceBuildingHarvester.CatalogIdsForBuilding(ResourceBuildingProgression.ForgeId);
            if (farmIds == null || farmIds.Count == 0)
            {
                failures.Add("[built-earns] the farm resolves to no collector catalog id - the gate can never open for it");
                return;
            }

            var builtFarm = new List<string>(farmIds);

            if (!ResourceBuildingHarvester.MayHarvest(farmIds, builtFarm, false))
                failures.Add("[built-earns] a town with a BUILT farm (its id in EverBuiltStructureIds) may NOT harvest - the gate " +
                             "is too tight and the player's own building earns nothing");

            if (ResourceBuildingHarvester.MayHarvest(millIds, builtFarm, false))
                failures.Add("[built-earns] building the FARM also switched on the LUMBERMILL - the gate must be per-id");
            if (ResourceBuildingHarvester.MayHarvest(forgeIds, builtFarm, false))
                failures.Add("[built-earns] building the FARM also switched on the FORGE - the gate must be per-id");

            // MarkEverBuilt records OrdinalIgnoreCase (GameState.cs:538-547); the gate must agree.
            var upper = new List<string>();
            foreach (var f in builtFarm) upper.Add(f.ToUpperInvariant());
            if (!ResourceBuildingHarvester.MayHarvest(farmIds, upper, false))
                failures.Add("[built-earns] the ledger match is case-SENSITIVE - GameState.MarkEverBuilt/HasEverBuilt are " +
                             "OrdinalIgnoreCase, so a case-variant id must still read as built");

            // A LIVE standing collector is the strongest existence proof there is: it exists
            // right now. It must produce even before/without a ledger entry (a migrated save
            // replays its BaseLayout collectors, and the ledger seed is a separate leg).
            if (!ResourceBuildingHarvester.MayHarvest(farmIds, new List<string>(), true))
                failures.Add("[built-earns] a LIVE registered ResourceCollector does not open the gate - a collector standing in " +
                             "the world is proof the building exists and must always be allowed to accrue");

            // No double-count: the payout routes to the collector and RETURNS. Proven at
            // source in case 2 (exactly one Accrue, no Grant/Credit after it); asserted here
            // as the rule half - a live collector never ALSO opens a second wallet path,
            // because the wallet path no longer exists.
            string raw = ReadText(HarvesterSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);
            int accrueAt = code.IndexOf(".Accrue(", StringComparison.Ordinal);
            if (accrueAt >= 0)
            {
                string after = code.Substring(accrueAt);
                if (Regex.IsMatch(after, @"\.\s*Grant\s*\(") || Regex.IsMatch(after, @"Credit\s*\("))
                    failures.Add("[built-earns] a wallet grant follows the collector Accrue in the same tick - a registered " +
                                 "collector would be paid TWICE (pending pool AND wallet)");
            }
        }

        // =====================================================================
        //  CASE 5 - the id map is unambiguous, and the storefront is not the collector
        // =====================================================================

        private static void Case5_CatalogMap(List<string> failures, List<string> notes)
        {
            string raw = ReadText(StructuresRes, failures);
            if (raw == null) return;

            string sa = File.Exists(StructuresSA) ? File.ReadAllText(StructuresSA) : null;
            if (sa != null && !string.Equals(raw, sa, StringComparison.Ordinal))
                notes.Add("structures-catalog.json Resources and StreamingAssets copies differ (tracked by the catalog suites)");

            var entries = JObject.Parse(raw)["entries"] as JArray;
            if (entries == null)
            {
                failures.Add("[catalog-map] structures-catalog.json has no 'entries' array");
                return;
            }

            var typeById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var collectorsFor = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                string id = (string)e["id"];
                if (string.IsNullOrEmpty(id)) continue;
                string type = (string)e["type"] ?? "";
                typeById[id] = type;
                if (!string.Equals(type, "Collector", StringComparison.OrdinalIgnoreCase)) continue;

                var repo = e["repo"];
                string bid = repo != null ? (string)repo["collectorBuildingId"] : null;
                if (string.IsNullOrEmpty(bid)) bid = id;
                if (!collectorsFor.TryGetValue(bid, out var list))
                {
                    list = new List<string>();
                    collectorsFor[bid] = list;
                }
                list.Add(id);
            }

            foreach (var id in ResourceBuildingProgression.OrderedIds)
            {
                if (!collectorsFor.TryGetValue(id, out var rows) || rows.Count == 0)
                {
                    failures.Add("[catalog-map] progression building '" + id + "' has NO Collector row in structures-catalog.json " +
                                 "(no repo.collectorBuildingId points at it) - it can never be built, so it must never earn");
                    continue;
                }
                if (rows.Count > 1)
                    failures.Add("[catalog-map] progression building '" + id + "' maps to " + rows.Count + " Collector rows (" +
                                 string.Join(",", rows) + ") - the existence gate cannot say which one proves it was built");

                // The BARE id must not itself be a Collector row: 'lumbermill' and 'forge'
                // are ALSO GameplayBuilding storefront rows, and accepting the bare id would
                // let building the storefront open the collector's harvest gate.
                if (typeById.TryGetValue(id, out var bareType) &&
                    string.Equals(bareType, "Collector", StringComparison.OrdinalIgnoreCase))
                    notes.Add("bare id '" + id + "' is itself a Collector row; the gate's bare-id exclusion is now moot for it");
            }
        }

        // =====================================================================
        //  CASE 6 - the zero-seed founding sequence still bootstraps (soft-lock guard)
        // =====================================================================

        private static void Case6_FoundingFlow(List<string> failures, List<string> notes)
        {
            // (a) The freebies ARE the starting budget - there is no resource seed to fall
            //     back on, so anything the FTUE demands must be placeable for nothing.
            if (DeNelle.Core.State.StartingBudget.StrategicWood != 0 ||
                DeNelle.Core.State.StartingBudget.StrategicIron != 0)
                notes.Add("StartingBudget is no longer 0/0 (wood " + DeNelle.Core.State.StartingBudget.StrategicWood +
                          ", iron " + DeNelle.Core.State.StartingBudget.StrategicIron + ") - the founding freebies are no " +
                          "longer the only starting budget; re-read this case's premise");

            // (b) A freshly PLACED collector is level 1. With the direct-grant fallback gone,
            //     the pending pool is the only income path, so a level-1 collector MUST be
            //     able to accrue or a zero-seed founding can never earn its first resource.
            string collectorRaw = ReadText(CollectorSrc, failures);
            if (collectorRaw != null)
            {
                string collectorCode = StripComments(collectorRaw);
                var isActive = Regex.Match(collectorCode, @"bool\s+IsActive\s*=>([^;]*);");
                if (!isActive.Success)
                    failures.Add("[founding-flow] ResourceCollector.IsActive not found - the accrual gate moved; re-point this oracle");
                else if (isActive.Groups[1].Value.IndexOf("GetLevel", StringComparison.Ordinal) >= 0)
                    failures.Add("[founding-flow] ResourceCollector.IsActive still gates accrual on the building LEVEL (" +
                                 isActive.Groups[1].Value.Trim() + ") - levels start at 1, so every freshly placed collector " +
                                 "would accrue ZERO until a paid upgrade the player cannot afford. With the phantom direct " +
                                 "grant removed this dead-locks the zero-seed founding bootstrap (owner 2026-07-13: LEVEL 1 PRODUCES)");
            }

            // (c) Every mandatory founding placement is FREE: either an explicit FoundingKit
            //     id, or a NON-TOWER catalog row (BuildModeController lane 3 - the first
            //     placement of each distinct non-tower id is free).
            string bmRaw = ReadText(BuildModeSrc, failures);
            string stepsRaw = ReadText(StepsRes, failures);
            string catRaw = ReadText(StructuresRes, failures);
            if (bmRaw == null || stepsRaw == null || catRaw == null) return;

            string bmCode = StripComments(bmRaw);
            int kit = bmCode.IndexOf("HashSet<string> FoundingKit", StringComparison.Ordinal);
            string kitBlock = "";
            if (kit < 0)
                notes.Add("BuildModeController.FoundingKit not found - founding affordability is verified only through the " +
                          "non-tower lane-3 freebie");
            else
            {
                int open = bmCode.IndexOf('{', kit);
                int close = open >= 0 ? bmCode.IndexOf('}', open) : -1;
                if (open >= 0 && close > open) kitBlock = bmCode.Substring(open, close - open);
            }

            var typeById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var catEntries = JObject.Parse(catRaw)["entries"] as JArray;
            if (catEntries != null)
                foreach (var e in catEntries)
                {
                    string id = (string)e["id"];
                    if (!string.IsNullOrEmpty(id)) typeById[id] = (string)e["type"] ?? "";
                }

            var steps = JObject.Parse(stepsRaw)["steps"] as JArray;
            if (steps == null)
            {
                failures.Add("[founding-flow] tutorial-steps.json has no 'steps' array");
                return;
            }

            const string Prefix = "build.structure_placed:";
            int walked = 0;
            foreach (var s in steps)
            {
                bool contextual = string.Equals((string)s["flowId"], "contextual", StringComparison.OrdinalIgnoreCase);
                if (contextual) continue;
                string signal = s["completion"] != null ? (string)s["completion"]["signal"] : null;
                if (string.IsNullOrEmpty(signal) || !signal.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string wantId = signal.Substring(Prefix.Length);
                if (string.IsNullOrEmpty(wantId)) continue;
                walked++;

                bool inKit = kitBlock.IndexOf("\"" + wantId + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
                typeById.TryGetValue(wantId, out var type);
                bool nonTower = !string.Equals(type, "Tower", StringComparison.OrdinalIgnoreCase);
                if (!inKit && !nonTower)
                    failures.Add("[founding-flow] founding step awaits placement of '" + wantId + "' (catalog type '" + type +
                                 "') which is neither in FoundingKit nor a non-tower lane-3 freebie - with wood/iron seeded at 0 " +
                                 "the player cannot afford the thing the FTUE forces them to place (soft-lock)");

                // And the demanded placement must actually TURN INCOME ON: for a collector
                // id, committing it writes that id into EverBuiltStructureIds
                // (BuildModeController.Place -> MarkEverBuilt), which is exactly what the
                // gate reads. Prove the round trip for every collector the FTUE demands.
                foreach (var bid in ResourceBuildingProgression.OrderedIds)
                {
                    var cat = ResourceBuildingHarvester.CatalogIdsForBuilding(bid);
                    if (cat == null || !cat.Contains(wantId)) continue;
                    if (!ResourceBuildingHarvester.MayHarvest(cat, new List<string> { wantId }, false))
                        failures.Add("[founding-flow] the FTUE forces the player to place '" + wantId + "' but committing it does " +
                                     "NOT open the harvest gate for '" + bid + "' - the founding collector would stand there " +
                                     "earning nothing and a zero-seed save could never buy anything");
                }
            }

            if (walked == 0)
                failures.Add("[founding-flow] no mandatory 'build.structure_placed:*' step found in tutorial-steps.json - the " +
                             "founding sequence this case walks is gone; re-point the oracle");
            else
                notes.Add("walked " + walked + " mandatory founding placement step(s)");
        }

        // =====================================================================
        //  CASE 7 - away accrual is bounded by CAPACITY, and shares the online rate
        // =====================================================================

        private static void Case7_OfflineCapped(List<string> failures, List<string> notes)
        {
            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            // (a) The away path must NOT re-implement the multiplier stack. It has to call the
            //     one shared authority the ONLINE tick uses, or the two silently diverge the
            //     first time either is retuned and the player earns a different rate for being
            //     away than for standing there.
            if (code.IndexOf("EffectiveYieldPerTick", StringComparison.Ordinal) < 0)
                failures.Add("[offline-capped] the collector's away catch-up does not call " +
                             "ResourceBuildingHarvester.EffectiveYieldPerTick - the offline path is re-deriving the yield " +
                             "multiplier stack itself, which is exactly how the away rate drifts away from the online rate");
            if (Regex.IsMatch(code, @"GlobalHarvestMultiplier"))
                failures.Add("[offline-capped] ResourceCollector reads GlobalHarvestMultiplier directly - the echo term " +
                             "must come through the shared harvester helper so rate and capacity can never disagree");

            // (b) The int-overflow guard exists and is a CLAMP, not a balance cap.
            if (code.IndexOf("MaxAwaySeconds", StringComparison.Ordinal) < 0)
                failures.Add("[offline-capped] no MaxAwaySeconds overflow guard - a tampered/rolled-forward clock could " +
                             "overflow the int handed to Accrue");

            // (c) Accrue still CLAMPS to Capacity. This is what makes the away window bounded:
            //     8h, 3 days and 3 weeks must all yield exactly the pool, with no second
            //     time-based cap anywhere.
            if (!Regex.IsMatch(code, @"Math\s*\.\s*Min\s*\(\s*cap"))
                failures.Add("[offline-capped] Accrue no longer clamps pending to the capacity - the per-collector capacity " +
                             "IS the offline cap, so without this clamp an away window is unbounded");

            // (d) The cap must actually BIND at the authored numbers: an hour of away must fit
            //     inside the pool while 30 days must not. If both fell on one side of the cap
            //     the dial would be doing nothing.
            var caps = CatalogCapacities(failures);
            if (caps == null) return;
            foreach (var id in ResourceBuildingProgression.OrderedIds)
            {
                if (!caps.TryGetValue(id, out double cap) || cap <= 0.0) continue;
                double perHour = BaselineYieldPerHour(id);
                if (perHour <= 0.0) continue;

                if (perHour * 1.0 > cap)
                    failures.Add("[offline-capped] '" + id + "' fills its whole pool in under an hour (" +
                                 perHour.ToString("0") + "/h vs cap " + cap.ToString("0") + ") - the away window is so " +
                                 "short the collector is capped before the player could plausibly return");
                if (perHour * 24.0 * 30.0 <= cap)
                    failures.Add("[offline-capped] '" + id + "' does NOT reach its cap in 30 days (" +
                                 perHour.ToString("0") + "/h vs cap " + cap.ToString("0") + ") - capacity is not bounding " +
                                 "the away window at all, so offline income is effectively unlimited");
            }
        }

        // =====================================================================
        //  CASE 8 - THE HIGHEST-RISK REGRESSION: the stamp advances even AT CAP
        // =====================================================================
        //
        //  If the last-accrual stamp only moved when pending actually GREW, it would FREEZE the
        //  moment a collector caps. The player then taps Collect and the very next catch-up
        //  re-pays the entire frozen backlog instantly - so the capacity cap would bound nothing
        //  and the away window would be unlimited. The stamp write must therefore sit OUTSIDE
        //  the `if (_pending > before)` block. This case asserts exactly that, at source.

        private static void Case8_StampAdvancesAtCap(List<string> failures, List<string> notes)
        {
            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            int accrueAt = code.IndexOf("public void Accrue(", StringComparison.Ordinal);
            if (accrueAt < 0)
            {
                failures.Add("[stamp-advances-at-cap] ResourceCollector.Accrue not found - the accrual seam moved; " +
                             "re-point this oracle (do NOT delete it: it guards the frozen-backlog defect)");
                return;
            }

            // Bound the search to Accrue's own body (up to the next member declaration).
            int endAt = code.IndexOf("public int Collect(", accrueAt, StringComparison.Ordinal);
            string body = endAt > accrueAt ? code.Substring(accrueAt, endAt - accrueAt) : code.Substring(accrueAt);

            var stamp = Regex.Match(body, @"_lastAccrualMs\s*=\s*TimeSource\s*\.\s*NowUnixMs\s*\(\s*\)");
            if (!stamp.Success)
            {
                failures.Add("[stamp-advances-at-cap] Accrue never advances the last-accrual stamp - every online tick " +
                             "would leave the stamp stale, so a relaunch would re-pay time the player was already paid for");
                return;
            }

            var guard = Regex.Match(body, @"if\s*\(\s*_pending\s*>\s*before\s*\)");
            if (!guard.Success)
            {
                notes.Add("Accrue's `if (_pending > before)` block is gone; the stamp-placement assert below is now " +
                          "vacuous - re-point this oracle to whatever replaced it");
                return;
            }

            if (stamp.Index > guard.Index)
                failures.Add("[stamp-advances-at-cap] the last-accrual stamp is written AFTER/INSIDE the " +
                             "`if (_pending > before)` block. At capacity Accrue adds nothing, that branch does not run, " +
                             "and the stamp FREEZES - so the instant the player taps Collect the next catch-up refills the " +
                             "pool from a frozen backlog and the capacity cap bounds NOTHING. The write must stay OUTSIDE " +
                             "the block (WO-859 sec.4, the highest-risk line in the change)");

            // The at-cap branch must also PERSIST the advanced stamp, or a relaunch reads the
            // pre-cap value off disk and the freeze comes back through the save file.
            if (!Regex.IsMatch(body, @"else\s*\{[^}]*SaveState\s*\(", RegexOptions.Singleline))
                failures.Add("[stamp-advances-at-cap] the at-cap branch of Accrue does not SaveState - the stamp advances " +
                             "in memory but never reaches disk, so a relaunch restores the frozen backlog anyway");
        }

        // =====================================================================
        //  CASE 9 - capacity is HOURS: fill time is level- and echo-invariant
        // =====================================================================

        private static void Case9_CapacityHoursStable(List<string> failures, List<string> notes)
        {
            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            // (a) The flat level multiplier must be GONE. It is what made the curve run backwards:
            //     capacity x3 from L1->L5 against throughput x5.6, so upgrading a collector
            //     SHORTENED how long it ran unattended.
            if (Regex.IsMatch(code, @"1\.0\s*\+\s*0\.5\s*\*\s*System\.Math\.Max\s*\(\s*0\s*,\s*level"))
                failures.Add("[capacity-hours-stable] ComputeCapacity still scales the authored capacity by the FLAT " +
                             "`1 + 0.5*(level-1)` multiplier - capacity then grows x3 across the ladder while throughput " +
                             "grows x5.6, so UPGRADING A COLLECTOR MAKES IT FILL SOONER (the curve runs backwards)");
            if (code.IndexOf("ThroughputScale", StringComparison.Ordinal) < 0)
                failures.Add("[capacity-hours-stable] ComputeCapacity does not use a throughput-proportional scale - " +
                             "hours-to-full will drift with level and echo count instead of staying constant");

            // (b) Capacity must NOT fold in the harvestRate talent. Deliberate, and it mirrors the
            //     already-shipped identical ruling on the Echo silo: capacity is collectorCap's
            //     seam, not harvestRate's. Two capacity systems, one rule.
            var scale = Regex.Match(code, @"ThroughputScale\s*\(\s*\)\s*\{(.*?)\n\s{8}\}", RegexOptions.Singleline);
            if (scale.Success && scale.Groups[1].Value.IndexOf("harvestRate", StringComparison.Ordinal) >= 0)
                failures.Add("[capacity-hours-stable] the capacity scale folds in the harvestRate talent - capacity is " +
                             "collectorCap's seam, not harvestRate's (EchoService.SiloCapacity applies the same rule); " +
                             "including it makes the two capacity systems disagree");

            // (c) DATA: the authored capacity must still mean ~8 hours of level-1 production for
            //     every collector. This is what catches a retune that silently changes the loop.
            var caps = CatalogCapacities(failures);
            if (caps == null) return;

            const double TargetHours = 8.0;
            foreach (var id in ResourceBuildingProgression.OrderedIds)
            {
                if (!caps.TryGetValue(id, out double cap) || cap <= 0.0)
                {
                    failures.Add("[capacity-hours-stable] '" + id + "' authors no repo.capacity - it would fall back to " +
                                 "the legacy ~2h formula and stop obeying the hours dial");
                    continue;
                }
                double perHour = BaselineYieldPerHour(id);
                if (perHour <= 0.0)
                {
                    failures.Add("[capacity-hours-stable] '" + id + "' has no level-1 production rate - hours-to-full is " +
                                 "undefined");
                    continue;
                }
                double hours = cap / perHour;
                if (Math.Abs(hours - TargetHours) > TargetHours * 0.05)
                    failures.Add("[capacity-hours-stable] '" + id + "' holds " + hours.ToString("0.00") + "h of level-1 " +
                                 "production (cap " + cap.ToString("0") + " / " + perHour.ToString("0") + " per hour), " +
                                 "outside 5% of the authored " + TargetHours.ToString("0") + "h target - the collect loop's " +
                                 "cadence moved; retune repo.capacity or update this target deliberately");
                else
                    notes.Add(id + " holds " + hours.ToString("0.00") + "h at L1");

                // (d) Fill time must be level-INVARIANT. Replicates the documented scale basis
                //     (rate ratio) so a future change that reintroduces a constant multiplier is
                //     caught numerically as well as at source.
                var def = ResourceBuildingProgression.Find(id);
                if (def == null) continue;
                foreach (int level in new[] { 3, 5 })
                {
                    var lv = def.LevelDef(level);
                    if (lv == null || lv.HarvestInterval <= 0f) continue;
                    double ratio = (lv.YieldPerTick * Math.Max(0f, lv.YieldSizeMultiplier) * (3600.0 / lv.HarvestInterval))
                                   / perHour;
                    if (ratio <= 0.0) continue;
                    double hoursAtLevel = (cap * ratio) / (perHour * ratio);
                    if (Math.Abs(hoursAtLevel - hours) > hours * 0.05)
                        failures.Add("[capacity-hours-stable] '" + id + "' fills in " + hoursAtLevel.ToString("0.00") +
                                     "h at level " + level + " versus " + hours.ToString("0.00") + "h at level 1 - " +
                                     "hours-to-full must not move with the upgrade ladder");
                }
            }
        }

        // =====================================================================
        //  CASE 10 - the fallback collector cannot back-door the existence gate
        // =====================================================================

        private static void Case10_FallbackGated(List<string> failures, List<string> notes)
        {
            string raw = ReadText(BootstrapSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            int fn = code.IndexOf("EnsureFallbackCollector(string", StringComparison.Ordinal);
            if (fn < 0)
            {
                failures.Add("[fallback-gated] ResourceCollectorBootstrap.EnsureFallbackCollector not found - re-point " +
                             "this oracle; it guards the back door that let a BLANK TOWN earn again");
                return;
            }
            string body = code.Substring(fn);

            if (body.IndexOf("HasEverBuilt", StringComparison.Ordinal) < 0)
                failures.Add("[fallback-gated] EnsureFallbackCollector creates a live ResourceCollector WITHOUT consulting " +
                             "the WO-834 ever-built ledger. MayHarvest returns true the instant a live collector exists, so " +
                             "an unconditional fallback re-opens the phantom-income gate for a town with nothing in it - and " +
                             "accrues full town income while the player is off in a DUNGEON");

            if (code.IndexOf("CatalogIdsForBuilding", StringComparison.Ordinal) < 0)
                failures.Add("[fallback-gated] the fallback gate does not resolve ids through " +
                             "ResourceBuildingHarvester.CatalogIdsForBuilding - it must use the SAME resolution the harvest " +
                             "gate uses, or the two can disagree about what 'built' means");
        }

        // =====================================================================
        //  CASE 11 - no collector opens a CRYSTAL faucet (owner ruling, uncapped premium)
        // =====================================================================

        private static void Case11_NoCrystalFaucet(List<string> failures, List<string> notes)
        {
            foreach (var id in ResourceBuildingProgression.OrderedIds)
            {
                var def = ResourceBuildingProgression.Find(id);
                if (def == null) continue;
                if (def.Yields == HarvestResource.Crystals)
                    failures.Add("[no-crystal-faucet] progression building '" + id + "' YIELDS Crystals - crystals are the " +
                                 "UNCAPPED premium currency (owner ruling 2026-08-04, CoC gems precedent); routing a " +
                                 "collector at them creates an uncapped, offline-accruing premium faucet");
            }

            string raw = ReadText(StructuresRes, failures);
            if (raw == null) return;
            var entries = JObject.Parse(raw)["entries"] as JArray;
            if (entries == null) return;

            foreach (var e in entries)
            {
                string type = (string)e["type"] ?? "";
                if (!string.Equals(type, "Collector", StringComparison.OrdinalIgnoreCase)) continue;
                var repo = e["repo"];
                string storageRes = repo != null ? (string)repo["storageResource"] : null;
                if (!string.IsNullOrEmpty(storageRes) &&
                    storageRes.IndexOf("crystal", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[no-crystal-faucet] Collector row '" + (string)e["id"] + "' routes at a crystal resource " +
                                 "(storageResource='" + storageRes + "') - collectors yield Food/Wood/Iron only");
            }
        }

        // =====================================================================
        //  CASE 12 - the "I am full" tell is actually WIRED (WO-900)
        // =====================================================================
        //
        //  CollectorStackView is a complete 437-line CoC fill tell that sat with ZERO CALLERS
        //  since it was written: a collector capping showed the player nothing at all and the
        //  wallet number simply stopped moving. This case is the one that would have caught it.

        private static void Case12_TellWired(List<string> failures, List<string> notes)
        {
            string raw = ReadText(FactorySrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            int at = code.IndexOf("case \"ResourceCollector\"", StringComparison.Ordinal);
            if (at < 0)
            {
                failures.Add("[tell-wired] StructureFactory has no \"ResourceCollector\" behavior case - re-point this oracle");
                return;
            }
            int end = code.IndexOf("case \"CrystalMine\"", at, StringComparison.Ordinal);
            string body = end > at ? code.Substring(at, end - at) : code.Substring(at);

            if (body.IndexOf("CollectorStackView.Attach", StringComparison.Ordinal) < 0)
                failures.Add("[tell-wired] placing a collector does NOT attach CollectorStackView - the entire fill tell " +
                             "(fill bar, amber near-full band, N/20 readout, the \"!\" bang, the glint and the full toast) " +
                             "is built but dead, so the player gets no signal at all that a collector has stopped earning");

            // The tell must remain event-driven off StepChanged, never a per-frame model poll.
            string viewRaw = ReadText(ViewSrc, failures);
            if (viewRaw != null)
            {
                string viewCode = StripComments(viewRaw);
                if (viewCode.IndexOf("StepChanged", StringComparison.Ordinal) < 0)
                    failures.Add("[tell-wired] CollectorStackView no longer subscribes to StepChanged - the view must be " +
                                 "event-driven off the model's single re-render signal, never poll it per frame");

                // Toast spam: three collectors capping in one frame must produce ONE toast.
                if (!Regex.IsMatch(viewCode, @"static[^\n]*s_pendingFullNames"))
                    failures.Add("[tell-wired] the FULL toast is not coalesced - ShowFullToast fires per collector, so " +
                                 "three collectors filling together throw three stacked toasts at the player");

                // WO-900 sec.4 copy law: "Storage" is the town BANK's word (WO-857). The player
                // must never be shown two different notions of "full".
                if (viewCode.IndexOf("Storage", StringComparison.Ordinal) >= 0)
                    failures.Add("[tell-wired] CollectorStackView copy uses the word 'Storage' - that word belongs to the " +
                                 "town bank (WO-857); collector copy says 'full ... collect it'");
            }
        }

        // =====================================================================
        //  CASE 13 - Configure RE-KEYS the registry
        //  (source lint + a live registry measurement; WO-1494)
        // =====================================================================
        //
        //  THE DEFECT was MEASURED, not theorised. Headless blank-town capture, three consecutive lines:
        //      [Flow:Harvest] register id=farm pending=1088/2000   (x3, one per collector)
        //      [Flow:Harvest] existence gate CLOSED for 'lumbermill' (liveCollector=no, ...)
        //      [Flow:Harvest] accrue-pending building=forge pending=87/600   (rising in ~12s
        //                     = the FARM's yield 13 x HpFraction, NOT the forge's 6)
        //  AddComponent on an ACTIVE GameObject runs Awake+OnEnable synchronously - i.e. BEFORE
        //  Configure - so every collector registered under the serialized default id "farm".
        //  Result: lumbermill/forge income was silently WITHHELD by the no-live-collector branch,
        //  and farm income was paid into the wrong pool and banked as the wrong RESOURCE.

        private static void Case13_ConfigureRekeys(List<string> failures, List<string> notes)
        {
            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);

            var cfg = Regex.Match(code, @"public\s+void\s+Configure\s*\([^)]*\)\s*\{(.*?)\n\s{8}\}",
                                  RegexOptions.Singleline);
            if (!cfg.Success)
            {
                failures.Add("[configure-rekeys] ResourceCollector.Configure not found - re-point this oracle");
                return;
            }
            string body = cfg.Groups[1].Value;

            bool unreg = body.IndexOf("ResourceCollectorRegistry.Unregister", StringComparison.Ordinal) >= 0;
            bool reg = body.IndexOf("ResourceCollectorRegistry.Register", StringComparison.Ordinal) >= 0;
            if (!unreg || !reg)
                failures.Add("[configure-rekeys] Configure does not re-key the collector registry (Unregister then " +
                             "Register). AddComponent fires OnEnable - and therefore Register - BEFORE Configure runs, so " +
                             "every collector registers under the serialized default id 'farm': the lumbermill and forge " +
                             "then have NO live collector and their income is silently withheld, while the farm's tick is " +
                             "paid into whichever collector registered last and banked as the WRONG RESOURCE. Measured " +
                             "headless 2026-08-04 ('register id=farm' x3)");

            // Order matters: the stale key can only be dropped while BuildingId still returns it.
            int unregAt = body.IndexOf("ResourceCollectorRegistry.Unregister", StringComparison.Ordinal);
            int assignAt = Regex.Match(body, @"_buildingId\s*=\s*buildingId").Index;
            if (unreg && assignAt > 0 && unregAt > assignAt)
                failures.Add("[configure-rekeys] Configure unregisters AFTER assigning _buildingId - the registry removes " +
                             "by CURRENT id, so the stale entry under the old key is orphaned forever and the bug survives");

            // The source lint above survives on purpose (WO-1494 sec.3): it catches a DELETED call
            // site. The half below is the one that catches a call site that is present and WRONG.
            MeasureConfigureRekey(failures, notes);
        }

        /// <summary>
        /// WO-1494 - the MEASURED half of [configure-rekeys]. Everything above this point is Regex
        /// over ResourceCollector.cs; this drives the real component and reads the real registry.
        ///
        /// <para>⚠ WHY OnEnable IS STOOD IN FOR EXPLICITLY: <see cref="ResourceCollector"/> carries
        /// no <c>[ExecuteAlways]</c> / <c>[ExecuteInEditMode]</c> (verified at source 2026-09-06,
        /// ResourceCollector.cs:19 declares only <c>[DisallowMultipleComponent]</c>), so inside an
        /// edit-mode batch method <c>AddComponent</c> does NOT fire Awake/OnEnable and the collector
        /// would never register under its serialized default. A case relying on that would go green
        /// on a broken tree - Unregister would find nothing and Register would succeed with no
        /// re-key proven. So the OnEnable step is reproduced through the SAME public registry call
        /// OnEnable makes, and only Configure's own behaviour is then under test.</para>
        ///
        /// <para>The probe id is synthetic, never a real progression id: <see cref="ResourceCollector"/>
        /// LoadState reads PlayerPrefs under `prefix + id`, and no real key may be touched by a suite.</para>
        /// </summary>
        private static void MeasureConfigureRekey(List<string> failures, List<string> notes)
        {
            const string probeId = "wo1494-rekey-probe";
            string defaultId = ResourceBuildingProgression.FarmId;

            // A batch session may have a live collector registered; put the registry back exactly.
            ResourceCollector priorDefault = ResourceCollectorRegistry.Get(defaultId);
            ResourceCollector priorProbe = ResourceCollectorRegistry.Get(probeId);

            GameObject go = null;
            ResourceCollector probe = null;
            try
            {
                go = new GameObject("WO1494_ConfigureRekeyProbe") { hideFlags = HideFlags.HideAndDontSave };
                probe = go.AddComponent<ResourceCollector>();

                if (!string.Equals(probe.BuildingId, defaultId, StringComparison.Ordinal))
                {
                    failures.Add("[configure-rekeys] MEASURED: a fresh ResourceCollector's serialized default id is '" +
                                 probe.BuildingId + "', not '" + defaultId + "' - the whole defect is that the default " +
                                 "is what OnEnable registers under, so re-point this oracle deliberately, do not " +
                                 "assume the new default is harmless");
                    return;
                }

                ResourceCollectorRegistry.Register(probe);   // stands in for OnEnable (see remarks)
                if (ResourceCollectorRegistry.Get(defaultId) != probe)
                {
                    failures.Add("[configure-rekeys] MEASURED: could not establish the pre-condition (collector " +
                                 "registered under the default id '" + defaultId + "') - ResourceCollectorRegistry." +
                                 "Register did not take. The measurement below would be vacuous, so it is withheld " +
                                 "rather than reported green");
                    return;
                }

                probe.Configure(probeId);

                if (!string.Equals(probe.BuildingId, probeId, StringComparison.Ordinal))
                    failures.Add("[configure-rekeys] MEASURED: Configure('" + probeId + "') left BuildingId at '" +
                                 probe.BuildingId + "' - the collector never took its real id");
                if (ResourceCollectorRegistry.Get(probeId) != probe)
                    failures.Add("[configure-rekeys] MEASURED: after Configure the registry does NOT return this " +
                                 "collector under its real id '" + probeId + "'. That is the 2026-08-04 defect exactly: " +
                                 "the lumbermill and the forge have no live collector, so their income is silently " +
                                 "withheld by the no-live-collector branch");
                if (ResourceCollectorRegistry.Get(defaultId) == probe)
                    failures.Add("[configure-rekeys] MEASURED: after Configure the STALE '" + defaultId + "' entry still " +
                                 "points at this collector. The registry removes by CURRENT id, so an orphaned default " +
                                 "entry is permanent - the farm's tick is then paid into whichever collector registered " +
                                 "last and banked as the WRONG RESOURCE");
                else
                    notes.Add("configure-rekeys MEASURED: live collector re-keyed '" + defaultId + "' -> '" + probeId +
                              "' and the stale default entry is gone");
            }
            catch (Exception ex)
            {
                failures.Add("[configure-rekeys] MEASURED half THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // Unregister EXPLICITLY: without [ExecuteAlways] OnDisable may not fire on
                // DestroyImmediate, and a leaked entry in this static registry would poison every
                // suite that runs after this one. Unregister removes by CURRENT id, so this drops
                // whichever key the probe ended up holding.
                //
                // ⚠ KNOWN LIMIT OF THIS CLEANUP, stated rather than papered over: in the DEFECT
                // case (a stale default-id entry still pointing at the probe) this cannot remove
                // BOTH keys - ResourceCollectorRegistry exposes no remove-by-id, only
                // Unregister(collector), which removes by collector.BuildingId and so drops just
                // the probe's CURRENT key. The priorDefault re-Register below overwrites the stale
                // '<defaultId>' slot when a real collector was there; when it was empty, a dead
                // entry can survive this method. That only happens on an ALREADY-RED run, so it
                // cannot manufacture a false green - but a later suite in the same batch may see it.
                if (probe != null) ResourceCollectorRegistry.Unregister(probe);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                if (priorDefault != null) ResourceCollectorRegistry.Register(priorDefault);
                if (priorProbe != null) ResourceCollectorRegistry.Register(priorProbe);
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>repo.capacity per progression building id, read from the canonical catalog.</summary>
        // =====================================================================
        //  CASE 14 - [popup-and-result-agree] (WO-1392): ONE producer, one number per resource
        // =====================================================================
        //
        //  Measured 2026-09-04 23:41 (build 355952): the welcome-back popup said "WOOD WAITING
        //  +672"; one tap later the harvest result said "Collected 1979 of 2393". The popup summed
        //  Floor(PendingAmount) in its own loop; the modal's "of N" was the Echo silo's clamp
        //  (the only warn scope in the tree). Both surfaces now consume
        //  ResourceCollectorService.PendingByResource(); this case drives the PURE halves with
        //  fixture samples and asserts the popup row and the result row carry the SAME number.
        //  RED before WO-1392: AggregatePending / LinesFrom / BuildCollectorRows did not exist,
        //  and the service's Collect() reported nothing per resource at all.

        private static void Case14_PopupAndResultAgree(List<string> failures, List<string> notes)
        {
            var samples = new List<KeyValuePair<HarvestResource, double>>
            {
                new KeyValuePair<HarvestResource, double>(HarvestResource.Wood, 500.9),
                new KeyValuePair<HarvestResource, double>(HarvestResource.Wood, 172.7),
                new KeyValuePair<HarvestResource, double>(HarvestResource.Iron, 403.2),
                new KeyValuePair<HarvestResource, double>(HarvestResource.Stone, 874.99),
                new KeyValuePair<HarvestResource, double>(HarvestResource.Crystals, 0.4),
            };
            var agg = ResourceCollectorService.AggregatePending(samples);
            if (agg == null || agg.Count != 3)
            {
                failures.Add("[popup-and-result-agree] AggregatePending returned " + (agg == null ? "null" : agg.Count.ToString()) +
                             " line(s) for wood/wood/iron/stone/crystals(0.4) - expected 3 (a sub-unit crystal pending is not a row)");
                return;
            }
            // Rail order and PER-COLLECTOR floors (500 + 172 = 672, never floor(673.6) = 673 - the
            // sum of the rows must be exactly what a tap can bank).
            if (agg[0].Resource != HarvestResource.Wood || agg[0].Pending != 672 || agg[0].Collectors != 2)
                failures.Add($"[popup-and-result-agree] line 0 = {agg[0].Resource}/{agg[0].Pending}/{agg[0].Collectors}; expected Wood/672/2 (per-collector floor)");
            if (agg[1].Resource != HarvestResource.Iron || agg[1].Pending != 403)
                failures.Add($"[popup-and-result-agree] line 1 = {agg[1].Resource}/{agg[1].Pending}; expected Iron/403");
            if (agg[2].Resource != HarvestResource.Stone || agg[2].Pending != 874)
                failures.Add($"[popup-and-result-agree] line 2 = {agg[2].Resource}/{agg[2].Pending}; expected Food(Stone)/874 - rail order Wood/Iron/Stone");

            // The popup's rows, from the same lines.
            var popup = DeNelle.Village.OfflineHarvestService.LinesFrom(agg);
            if (popup == null || popup.Count != 3)
            {
                failures.Add("[popup-and-result-agree] LinesFrom did not yield one popup row per aggregate line");
                return;
            }
            if (popup[0].Resource != "Wood" || popup[2].Resource != "Stone")
                failures.Add($"[popup-and-result-agree] popup words = '{popup[0].Resource}'/'{popup[2].Resource}'; expected the canon LabelFor words Wood / Stone");

            // The result's rows: wood partly fit (258 of 672), iron fit entirely, stone banked nothing.
            var bankedBy = new Dictionary<HarvestResource, int>
                { { HarvestResource.Wood, 258 }, { HarvestResource.Iron, 403 }, { HarvestResource.Stone, 0 } };
            var store = new Dictionary<HarvestResource, int>
                { { HarvestResource.Wood, 3742 }, { HarvestResource.Iron, 100 }, { HarvestResource.Stone, 3000 } };
            var rows = ResourceCollectorService.BuildCollectorRows(agg, bankedBy, store);
            if (rows == null || rows.Count != 2)
            {
                failures.Add("[popup-and-result-agree] BuildCollectorRows returned " + (rows == null ? "null" : rows.Count.ToString()) +
                             " row(s); expected 2 (wood + stone - iron fit entirely and must NOT be scolded)");
                return;
            }
            foreach (var row in rows)
            {
                DeNelle.Village.OfflineHarvestResult.OfflineCollectorLine match = null;
                foreach (var p in popup) if (p.Resource == row.ResourceName) { match = p; break; }
                if (match == null)
                    failures.Add($"[popup-and-result-agree] result row '{row.ResourceName}' has no popup row with the same word");
                else if (match.Pending != row.Requested)
                    failures.Add($"[popup-and-result-agree] {row.ResourceName}: popup says +{match.Pending}, result says 'of {row.Requested}' - " +
                                 "two numbers for one resource on one tap, the WO-1392 defect");
                if (row.Source != DeNelle.Core.UI.HarvestOverflowModal.CollectorSource)
                    failures.Add($"[popup-and-result-agree] collector row Source = '{row.Source}', expected HarvestOverflowModal.CollectorSource " +
                                 "- without it the modal reads the row as a burned loss");
            }
            if (rows[0].ResourceName != "Wood" || rows[0].Granted != 258 || rows[0].Lost != 414 || rows[0].Current != 3742)
                failures.Add($"[popup-and-result-agree] wood row = granted {rows[0].Granted} / waiting {rows[0].Lost} / store {rows[0].Current}; expected 258 / 414 / 3742");
            if (rows[1].ResourceName != "Stone" || rows[1].Granted != 0 || rows[1].Lost != 874)
                failures.Add($"[popup-and-result-agree] stone row = {rows[1].ResourceName} granted {rows[1].Granted} / waiting {rows[1].Lost}; expected Stone 0 / 874");

            // At SOURCE: both live paths consume the one producer, and the collect path snapshots
            // it BEFORE the first Collect so 'of N' is the pre-tap number the popup showed.
            string svc = ReadText("Assets/_Modules/Village/Buildings/Progression/ResourceCollectorService.cs", failures);
            string ohs = ReadText("Assets/_Modules/Village/Harvest/OfflineHarvestService.cs", failures);
            if (svc != null)
            {
                string code = StripComments(svc);
                int snap = code.IndexOf("var before = PendingByResource();", StringComparison.Ordinal);
                int collect = code.IndexOf(".Collect(", StringComparison.Ordinal);
                if (snap < 0 || collect < 0 || snap > collect)
                    failures.Add("[popup-and-result-agree] CollectAll does not snapshot PendingByResource() BEFORE the first Collect - " +
                                 "the result's 'of N' would be read after the pools drained");
                if (code.IndexOf("HarvestOverflowModal.BeginBatch(", StringComparison.Ordinal) < 0)
                    failures.Add("[popup-and-result-agree] CollectAll does not open a HarvestOverflowModal batch - the collector rows and " +
                                 "the silo dump's rows would fight for one modal and the second would close the first");
                if (code.IndexOf("BuildCollectorRows(before, bankedBy, storeBefore)", StringComparison.Ordinal) < 0)
                    failures.Add("[popup-and-result-agree] CollectAll does not build its result rows from the snapshot");
            }
            if (ohs != null)
            {
                string code = StripComments(ohs);
                if (code.IndexOf("ResourceCollectorService.PendingByResource()", StringComparison.Ordinal) < 0)
                    failures.Add("[popup-and-result-agree] OfflineHarvestService.AttachPendingCollectors does not read PendingByResource()");
                if (code.IndexOf("Floor(c.PendingAmount)", StringComparison.Ordinal) >= 0)
                    failures.Add("[popup-and-result-agree] OfflineHarvestService floors collector pending in its own loop - the second producer is back");
            }
        }

        // =====================================================================
        //  CASE 15 - [overflow-stays-pending] (WO-1392): a collect NEVER burns
        // =====================================================================
        //
        //  ResourceCollector.Collect granted floor(pending) through GrantSpendable - which CLAMPS
        //  at the town bank cap and RETURNS the applied basket - and then did `_pending -= amount`
        //  with the REQUEST, discarding every refused unit. It now drains the pool by what BANKED
        //  (SettleCollect, pure) and leaves the remainder pending. RED before WO-1392: SettleCollect
        //  did not exist and the source carried `_pending -= amount`.

        private static void Case15_OverflowStaysPending(List<string> failures, List<string> notes)
        {
            double after = ResourceCollector.SettleCollect(672.9, 258, out int left);
            if (Math.Abs(after - 414.9) > 1e-6 || left != 414)
                failures.Add($"[overflow-stays-pending] SettleCollect(672.9, banked 258) -> pending {after:0.###} / left {left}; expected 414.9 / 414 " +
                             "(the pool drains by what BANKED, never by what was asked)");
            after = ResourceCollector.SettleCollect(672.0, 672, out left);
            if (Math.Abs(after) > 1e-9 || left != 0)
                failures.Add($"[overflow-stays-pending] a full bank of 672 left {after:0.###}/{left} pending; expected 0");
            after = ResourceCollector.SettleCollect(10.5, 999, out left);
            if (Math.Abs(after - 0.5) > 1e-6 || left != 0)
                failures.Add($"[overflow-stays-pending] an over-reported bank (999 of 10.5) drained to {after:0.###}; expected 0.5 (clamped to the request)");
            after = ResourceCollector.SettleCollect(10.0, -5, out left);
            if (Math.Abs(after - 10.0) > 1e-9 || left != 10)
                failures.Add($"[overflow-stays-pending] a negative bank moved the pool to {after:0.###}; expected 10 untouched");
            after = ResourceCollector.SettleCollect(4000.0, 0, out left);
            if (Math.Abs(after - 4000.0) > 1e-9 || left != 4000)
                failures.Add($"[overflow-stays-pending] a bank-full collect (0 banked) drained the pool to {after:0.###}; expected 4000 still waiting");

            string raw = ReadText(CollectorSrc, failures);
            if (raw == null) return;
            string code = StripComments(raw);
            if (Regex.IsMatch(code, @"_pending\s*-=\s*amount"))
                failures.Add("[overflow-stays-pending] ResourceCollector.Collect still does `_pending -= amount` - it drains the REQUEST, " +
                             "so every unit the town bank cap refused is burned (the owner's 414 wood, 2026-09-04)");
            if (code.IndexOf("SettleCollect(_pending, banked", StringComparison.Ordinal) < 0)
                failures.Add("[overflow-stays-pending] Collect does not settle the pool through SettleCollect(_pending, banked, ...)");
            if (!Regex.IsMatch(code, @"GrantSpendable\(wood:\s*amount\)\s*\.Wood") ||
                !Regex.IsMatch(code, @"GrantSpendable\(iron:\s*amount\)\s*\.Iron") ||
                !Regex.IsMatch(code, @"GrantSpendable\(stone:\s*amount\)\s*\.Stone"))
                failures.Add("[overflow-stays-pending] Collect does not read the APPLIED basket back from GrantSpendable for wood/iron/food - " +
                             "it is trusting its own request local, which is how a silent loss hides");
        }

        private static Dictionary<string, double> CatalogCapacities(List<string> failures)
        {
            string raw = ReadText(StructuresRes, failures);
            if (raw == null) return null;
            var entries = JObject.Parse(raw)["entries"] as JArray;
            if (entries == null)
            {
                failures.Add("[capacity] structures-catalog.json has no 'entries' array");
                return null;
            }

            var caps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                string type = (string)e["type"] ?? "";
                if (!string.Equals(type, "Collector", StringComparison.OrdinalIgnoreCase)) continue;
                var repo = e["repo"];
                if (repo == null) continue;
                string bid = (string)repo["collectorBuildingId"];
                if (string.IsNullOrEmpty(bid)) bid = (string)e["id"];
                double cap = repo["capacity"] != null ? (double)repo["capacity"] : 0.0;
                if (!string.IsNullOrEmpty(bid)) caps[bid] = cap;
            }
            return caps;
        }

        /// <summary>
        /// Units per hour a building produces at LEVEL 1 with one echo and no perks - the fixed
        /// reference repo.capacity is authored against, so `capacity / this` is hours-to-full.
        /// </summary>
        private static double BaselineYieldPerHour(string buildingId)
        {
            var def = ResourceBuildingProgression.Find(buildingId);
            var l1 = def?.LevelDef(1);
            if (l1 == null || l1.HarvestInterval <= 0f) return 0.0;
            return l1.YieldPerTick * Math.Max(0f, l1.YieldSizeMultiplier) * (3600.0 / l1.HarvestInterval);
        }

        // =====================================================================
        //  CASE 16 - WO-1416: the QUARRY pays STONE, and all three answers come
        //  from the SAME producer.
        // ---------------------------------------------------------------------
        //  MEASURED DEFECT (owner device 2026-09-05, WO-1416): one building, three
        //  words. The catalog row `collector_farm` already read displayName "Quarry",
        //  description "Extracts Stone", role "stone_producer" - but
        //    * the only C# role constant said StructureRole.FoodProducer =
        //      "food_producer", a role NO row claims, so StructureRoles resolved NULL
        //      and each caller fell back to a literal: BuildingInteractable printed the
        //      generic "Building" and CastleVendorNpcInjector printed the RETIRED proper
        //      noun "Farm"; and
        //    * the harvest tick printed the RAW ENUM (`{def.Yields}` -> "Food") while the
        //      welcome-back popup put the SAME value through
        //      ResourceBuildingProgression.LabelFor and printed "Stone".
        //  Owner rulings this pins: "quarry pays stone" and "the farm was retired
        //  nothiong uses food, was replaced by stone which actually has uses".
        //
        //  STOP - THE RESOURCE ALWAYS HAD EXACTLY ONE PRODUCER and still does:
        //  ResourceBuildingProgression.Find(id).Yields. The tick, ResourceCollector
        //  .ResolveResource and ResourceCollectorService.PendingByResource all read it;
        //  nothing re-derives it. What diverged was the LABEL STEP and the ROLE NAME, so
        //  this case pins the three READ SITES, not a new map.
        //
        //  STOP - AND IT PINS WHAT MUST NOT MOVE: the id `collector_farm` and the enum member
        //  HarvestResource.Stone are LIVE PERSISTED KEYS. Food IS the Stone wallet slot
        //  (LabelFor). A HarvestResource.Stone site in C# is a STONE site - never "fix" it.
        //
        //  NAMED MUTATION (the one-line proof this case discriminates): in
        //  ResourceBuildingHarvester.cs revert the HELD trace to `{def.Yields}` - check
        //  [tick-word] fails. Equally: put StructureRole.FoodProducer back at
        //  BuildingInteractable's BuildingType.Farm arm - check [role-word] fails.
        // =====================================================================

        private static void Case16_QuarryPaysStone(List<string> failures, List<string> notes)
        {
            const string Tag = "[quarry-pays-stone] ";

            // -- 1. the role constant names the role the CATALOG actually authors -------
            string roleRaw = ReadText(RoleSrc, failures);
            if (roleRaw != null)
            {
                string role = StripComments(roleRaw);
                if (!role.Contains("StoneProducer = \"stone_producer\""))
                    failures.Add(Tag + "StructureRole has no StoneProducer = \"stone_producer\" constant - " +
                                 "the collector_farm row authors role 'stone_producer', so without this constant " +
                                 "every caller names a role no row claims and silently resolves to null");
                if (Regex.IsMatch(role, @"FoodProducer\s*="))
                    failures.Add(Tag + "StructureRole still declares FoodProducer - no catalog row claims " +
                                 "'food_producer' (the Quarry authors 'stone_producer'), and a named role nothing " +
                                 "claims is the exact trap WO-1416 removed: it resolves to null in silence and the " +
                                 "player-facing word falls back to a literal");
            }

            // -- 2. the ROLE WORD: the building's own tile ------------------------------
            string interRaw = ReadText(InteractableSrc, failures);
            if (interRaw != null)
            {
                string inter = StripComments(interRaw);
                // Anchor on the WORD switch (LabelFor). The file carries three other
                // BuildingType.Farm arms (structure id, panel, progression id) and none of them
                // names a role - matching the wrong one would make this check vacuous.
                int labelSwitch = inter.IndexOf("LabelFor(BuildingType t)", StringComparison.Ordinal);
                if (labelSwitch < 0)
                {
                    failures.Add(Tag + "BuildingInteractable has no LabelFor(BuildingType t) - the tile's " +
                                 "word switch moved; re-point this oracle");
                    labelSwitch = 0;
                }
                var m = Regex.Match(inter.Substring(labelSwitch), @"BuildingType\.Farm\s*=>[^,\r\n]*");
                if (!m.Success)
                    failures.Add(Tag + "BuildingInteractable has no BuildingType.Farm arm in LabelFor - " +
                                 "the tile's word moved; re-point this oracle");
                else
                {
                    if (m.Value.Contains("FoodProducer"))
                        failures.Add(Tag + "BuildingInteractable's BuildingType.Farm arm still asks for " +
                                     "StructureRole.FoodProducer - that role is unclaimed, so the Quarry tile reads " +
                                     "the generic \"Building\" fallback (owner ruling: quarry pays stone)");
                    if (!m.Value.Contains("StoneProducer"))
                        failures.Add(Tag + "BuildingInteractable's BuildingType.Farm arm does not read " +
                                     "StructureRole.StoneProducer - the word must come from the row that claims the " +
                                     "role ('Quarry'), never from a literal typed into that file");
                }
            }

            // -- 3. the NPC label: no surface may still say "Farm" ---------------------
            string vendorRaw = ReadText(VendorNpcSrc, failures);
            if (vendorRaw != null)
            {
                string vendor = StripComments(vendorRaw);
                if (vendor.Contains("\"Farm\""))
                    failures.Add(Tag + "CastleVendorNpcInjector still carries the player-facing literal \"Farm\" - " +
                                 "the farm is RETIRED (owner 2026-09-05); the Quarry's NPC label falls back to that " +
                                 "word whenever the catalog has not loaded");
                if (!vendor.Contains("StructureRole.StoneProducer"))
                    failures.Add(Tag + "CastleVendorNpcInjector's windmill/farm vendor does not read " +
                                 "StructureRole.StoneProducer - its label resolves to null and prints its literal");
            }

            // -- 4. the HARVEST TICK: the WORD, not the raw enum -----------------------
            string harvRaw = ReadText(HarvesterSrc, failures);
            if (harvRaw != null)
            {
                string harv = StripComments(harvRaw);
                if (Regex.IsMatch(harv, @"\{\s*def\.Yields\s*\}"))
                    failures.Add(Tag + "the harvest tick still interpolates the RAW enum {def.Yields} - it " +
                                 "ToString()s the frozen persisted slot name (\"Food\") while every other surface " +
                                 "puts the SAME value through ResourceBuildingProgression.LabelFor and says " +
                                 "\"Stone\". That skipped label step is the divergence WO-1416 closed");
                if (!harv.Contains("LabelFor(def.Yields)"))
                    failures.Add(Tag + "the harvest tick does not word its payout through " +
                                 "ResourceBuildingProgression.LabelFor(def.Yields) - the tick and the welcome-back " +
                                 "popup must read the one producer through the one label");
                if (!harv.Contains("yield map:"))
                    failures.Add(Tag + "the harvester emits no '[Flow:Harvest] yield map: <catalogId> -> <word> " +
                                 "(role=<word>)' line - a capture cannot prove the three answers agree " +
                                 "(CLAUDE.md sec.12: instrument, do not guess)");
            }

            // -- 5. DATA SANITY (green today; keeps the pin honest if the row moves) ----
            var farmDef = ResourceBuildingProgression.Find(ResourceBuildingProgression.FarmId);
            if (farmDef == null)
                failures.Add(Tag + "ResourceBuildingProgression has no '" + ResourceBuildingProgression.FarmId +
                             "' building - the one resource producer lost the Quarry's row");
            else
            {
                string word = ResourceBuildingProgression.LabelFor(farmDef.Yields);
                if (!string.Equals(word, "Stone", StringComparison.Ordinal))
                    failures.Add(Tag + "the ONE producer words '" + ResourceBuildingProgression.FarmId +
                                 "' as '" + word + "', not 'Stone' - owner ruling 2026-09-05 'quarry pays stone'. " +
                                 "(HarvestResource.Stone is the FROZEN persisted Stone slot; fix LabelFor or the " +
                                 "row's Yields, never the enum member - it is a live save key)");
            }

            foreach (string path in new[] { StructuresRes, StructuresSA })
            {
                string raw = ReadText(path, failures);
                if (raw == null) continue;
                var entries = JObject.Parse(raw)["entries"] as JArray;
                if (entries == null)
                {
                    failures.Add(Tag + path + " has no 'entries' array");
                    continue;
                }
                JToken row = null;
                foreach (var e in entries)
                    if (string.Equals((string)e["id"], "collector_farm", StringComparison.Ordinal)) { row = e; break; }
                if (row == null)
                {
                    failures.Add(Tag + path + " has no 'collector_farm' row - that id is a LIVE SAVE KEY " +
                                 "(everBuiltStructureIds / BaseLayout / bakedTwins) and must never be renamed");
                    continue;
                }
                if (!string.Equals((string)row["role"], "stone_producer", StringComparison.OrdinalIgnoreCase))
                    failures.Add(Tag + path + ": collector_farm authors role '" + ((string)row["role"] ?? "<none>") +
                                 "', not 'stone_producer' - the role word, the tile and the NPC all resolve through it");
                if (!string.Equals((string)row["displayName"], "Quarry", StringComparison.Ordinal))
                    failures.Add(Tag + path + ": collector_farm displayName is '" +
                                 ((string)row["displayName"] ?? "<none>") + "', not 'Quarry'");
                string desc = (string)row["description"] ?? "";
                if (desc.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add(Tag + path + ": collector_farm description does not name Stone ('" + desc + "')");
                if (row["_quarryNote"] == null)
                    notes.Add("collector_farm carries no _quarryNote in " + path +
                              " - the 2026-09-05 ruling is not recorded where the next seat reads it");
            }
        }

        private static string ReadText(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("[source] missing file: " + path);
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("[source] could not read " + path + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Strips // and block comments so a lint can never be satisfied by prose.</summary>
        /// <summary>
        /// Strips comments, keeping string literals (the "Farm" check below needs them).
        ///
        /// ⛔ SINGLE PASS, IN LEXICAL ORDER - and that is the whole point of this rewrite
        /// (2026-09-05, WO-1416). The regex pair this replaced ran the BLOCK pattern FIRST:
        ///     Regex.Replace(src, @"/\*.*?\*\/", " ", Singleline)   // then //...
        /// so a `//` LINE comment containing the two characters `/` `*` opened a block that ran
        /// to the next `*` `/` anywhere in the file and DELETED everything between them.
        /// `CastleVendorNpcInjector.cs:92` is exactly that - a line comment ending
        /// `Resources/Portraits/*):` - and the next `*/` is `/*wander*/` at :1080, so ~988 lines
        /// vanished, including the very `StructureRole.StoneProducer` this suite then reported as
        /// missing. The code was right and the oracle was wrong: a FALSE RED, which is as
        /// expensive as a false green because it sends the next seat to fix working code.
        /// The same shape red the WallAdjacency wiring suite earlier the same day
        /// (a comment carrying `logs/device/*.log`).
        ///
        /// A char-by-char walk cannot have that bug: whichever of `//` or `/*` is reached first
        /// consumes its own comment and nothing else. `SourceLint.StripCommentsAndLiterals` is
        /// the same shape and would be the shared home for this, but it also empties literals,
        /// which this suite's "Farm" assertion depends on.
        /// </summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new System.Text.StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';
                if (c == '/' && n == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    i += 2;
                    while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
