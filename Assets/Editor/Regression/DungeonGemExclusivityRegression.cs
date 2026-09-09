// =============================================================================
// DungeonGemExclusivityRegression — the oracle for WO-1041 / WO-1042.
// -----------------------------------------------------------------------------
// Marker: DUNGEON_GEM_EXCLUSIVITY_OK / DUNGEON_GEM_EXCLUSIVITY_FAIL
// Wire (DataRegression.RunAll):
//   Guard.Try("Regression", "dungeon-gem-exclusivity suite", () => {
//       if (!DungeonGemExclusivityRegression.Run(out var r)) failures.Add(r);
//       else log.AppendLine("[dungeon-gem-exclusivity] " + r); });
//
// WHY THIS EXISTS: WO-1041's whole thesis is that the dungeon is worth descending into
// because it pays what you CANNOT GET ANYWHERE ELSE. That invariant is not self-enforcing
// — it dies quietly, one convenient data edit at a time, and nobody notices until the
// pillar is pointless. It had ALREADY died once before this ticket: the `jeweler` vendor
// sold all three recipe gems over the counter for gold (VendorStockResolver's gem band).
//
// It pins, in order:
//   1. EXCLUSIVITY   — no dungeon-exclusive id in any purchasable/grantable catalog.
//   2. VENDOR        — no vendor shelf resolves one either (the leak that actually shipped).
//   3. NO PAID SKIP  — the polish job cannot be bought to completion, by any path.
//   4. ALWAYS PAYS   — every outcome row pays a real gem; no empty/"nothing" outcome.
//   5. ODDS ONLY     — the grade moves probabilities and NOTHING else (no per-grade
//                      duration, no per-grade tier set, no per-grade count).
//   6. ONE TABLE     — free / ad / paid share the identical per-roll odds (the fairness
//                      property the design rests on), and the DISCLOSED odds are derived
//                      from the roll table rather than authored a second time.
//   7. LOOP CLOSES   — the gems produced are exactly the ids jeweler-recipes.json consumes.
//   8. RATES         — runs-per-ring at every grade stays inside a playable band.
//
// ⚠ SOURCE-LINT DISCIPLINE: every source match below runs on text with COMMENTS AND STRING
// LITERALS STRIPPED. Several oracles in this project have matched their own prose and
// reported a false pass; this file talks about "TryInstantFinish" and "loot box" constantly,
// so unstripped matching here would be guaranteed to lie.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.Jobs;
using DeNelle.Village.Crafting;

namespace DeNelle.Editor
{
    public static class DungeonGemExclusivityRegression
    {
        // Catalogs that can put an item into a player's hands for money or as a payout.
        private static readonly string[] PurchasableCatalogs =
        {
            "Resources/Data/Canonical/packs.json",
            "StreamingAssets/Data/Canonical/packs.json",
            "StreamingAssets/Data/Canonical/skr_store.json",
            "StreamingAssets/Data/Canonical/skr_staking.json",
            "StreamingAssets/Data/Canonical/battle_monthly_packs.sample.json",
            "Resources/Data/Canonical/cosmetics.json",
            "Resources/Data/Canonical/stake-rewards.json",
            "Resources/Data/Canonical/quests.json",
            "StreamingAssets/Data/Canonical/quests.json",
            "Resources/Data/Canonical/daily-quests.json",
            "StreamingAssets/Data/Canonical/daily-quests.json",
        };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();

            try
            {
                CheckCatalogExclusivity(failures, log);
                CheckVendorShelves(failures, log);
                CheckNoPaidSkip(failures, log);
                CheckAlwaysPays(failures, log);
                CheckOddsOnly(failures, log);
                CheckSingleTableAndDerivedDisclosure(failures, log);
                CheckLoopCloses(failures, log);
                CheckRunsPerRing(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("suite THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            return Finish(failures, log, out reason);
        }

        // ── 1. No dungeon-exclusive id in any purchasable/grantable catalog ───

        private static void CheckCatalogExclusivity(List<string> failures, StringBuilder log)
        {
            int scanned = 0;
            foreach (var rel in PurchasableCatalogs)
            {
                string path = Path.Combine(Application.dataPath, rel);
                if (!File.Exists(path)) continue;      // sample/optional files may be absent
                scanned++;
                string text = File.ReadAllText(path);
                foreach (var id in DungeonExclusiveItems.All)
                {
                    if (text.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    failures.Add(
                        $"EXCLUSIVITY BROKEN: '{id}' appears in {rel}. Dungeon-exclusive items must " +
                        "never be purchasable or grantable outside a delve - the moment a gem can be " +
                        "bought, the dungeon stops justifying itself and WO-1041's whole thesis is void.");
                }
            }

            if (scanned == 0)
            {
                // A premise failure, not a vacuous pass: an oracle that scanned nothing must say so.
                failures.Add("PREMISE FAILED: none of the purchasable catalogs were found on disk - " +
                             "this suite proved nothing. Check the paths in PurchasableCatalogs.");
                return;
            }
            log.Append($"exclusivity: {DungeonExclusiveItems.All.Count} id(s) absent from {scanned} catalog(s); ");
        }

        // ── 2. No vendor shelf resolves one (the leak that actually shipped) ──

        private static void CheckVendorShelves(List<string> failures, StringBuilder log)
        {
            string vendorsPath = Path.Combine(Application.dataPath, "Resources/Data/Canonical/vendors.json");
            if (!File.Exists(vendorsPath))
            {
                failures.Add("PREMISE FAILED: vendors.json not found - the vendor leak cannot be checked.");
                return;
            }

            // The DATA half is deliberately NOT asserted (the `jeweler` vendor may legitimately keep a
            // "gem" category for a future NON-exclusive gem). The MECHANISM half is what matters: the
            // resolver must never emit a dungeon-exclusive id, whatever the shelf asks for.
            int leaks = 0;
            foreach (var context in new[] { "jeweler", "market", "forge", "armorer" })
            {
                IReadOnlyList<DeNelle.Village.Hero.VendorWare> wares = null;
                try { wares = DeNelle.Village.Hero.VendorStockResolver.Resolve(context, "knight", 99); }
                catch (Exception ex)
                {
                    failures.Add($"vendor resolve '{context}' THREW: {ex.Message}");
                    continue;
                }
                if (wares == null) continue;
                foreach (var w in wares)
                {
                    if (!DungeonExclusiveItems.Contains(w.Id)) continue;
                    leaks++;
                    failures.Add(
                        $"VENDOR LEAK: vendor '{context}' stocks dungeon-exclusive '{w.Id}'. This is the " +
                        "exact defect WO-1041 found shipped (the Jeweler sold all three recipe gems for " +
                        "gold), which let a player buy the whole ring chain without ever descending.");
                }
            }
            if (leaks == 0) log.Append("vendors: no exclusive id on any shelf; ");
        }

        // ── 3. The polish job cannot be BOUGHT to completion, by any path ─────

        private static void CheckNoPaidSkip(List<string> failures, StringBuilder log)
        {
            // (a) The policy itself.
            if (JobRushPolicy.AllowsPaidInstantFinish(JobKind.JewelPolish))
            {
                failures.Add(
                    "LOOT BOX REINSTATED: JobRushPolicy now permits a PAID instant finish of " +
                    "JobKind.JewelPolish. A paid instant resolve of a RANDOM outcome is mechanically a " +
                    "loot box and is regulated in several jurisdictions in the shipping plan (owner " +
                    "ruling 2026-08-16, 'explicitly exclude this'). Buying an ATTEMPT is allowed; " +
                    "buying the RESOLUTION is not. Revert this.");
            }

            // (b) Deterministic kinds must be UNAFFECTED - the ruling is scoped, and over-applying it
            //     would silently remove a sanctioned revenue path.
            foreach (var kind in new[] { JobKind.Build, JobKind.Upgrade, JobKind.TrainTroop,
                                         JobKind.BuildingResearch, JobKind.WallUpgrade })
            {
                if (!JobRushPolicy.AllowsPaidInstantFinish(kind))
                    failures.Add($"OVER-APPLIED: JobRushPolicy now blocks paid finish on deterministic " +
                                 $"kind {kind}. The exclusion covers RANDOM outcomes only.");
            }

            // (c) Every paid-finish entry point must still CONSULT the policy. A future seat that adds
            //     a generic Finish Now row must hit a wall, not a gap - so the gates cannot be quietly
            //     deleted while the policy stays green.
            string src = ReadStripped("_Modules/Village/Buildings/BuildTimerService.cs", failures);
            if (src != null)
            {
                foreach (var site in new[] { "TryInstantFinish", "InstantFinishPrice", "CompleteAnyJob" })
                {
                    int at = src.IndexOf(site, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        failures.Add($"PREMISE FAILED: '{site}' no longer exists in BuildTimerService - " +
                                     "this suite can no longer prove the paid-finish gates are wired.");
                        continue;
                    }
                }
                int policyRefs = CountOccurrences(src, "JobRushPolicy");
                if (policyRefs < 3)
                    failures.Add($"GATE REMOVED: BuildTimerService references JobRushPolicy only " +
                                 $"{policyRefs} time(s); all three paid-finish sites (InstantFinishPrice, " +
                                 "TryInstantFinish, CompleteAnyJob) must consult it. A missing gate is " +
                                 "how the exclusion silently becomes a gap.");
            }

            if (failures.Count == 0) log.Append("paid-skip: excluded at policy + 3 gates; ");
        }

        // ── 4. Every completed run pays something ─────────────────────────────

        private static void CheckAlwaysPays(List<string> failures, StringBuilder log)
        {
            for (int score = 0; score <= DungeonRunGrade.MaxStars; score++)
            {
                var row = JewelPolishCatalog.RowFor(score);
                if (row == null || row.Weights == null || row.Weights.Count == 0)
                {
                    failures.Add($"NO PAYOUT: polish score {score} has no outcome row. Every completed " +
                                 "run must pay something (WO-1041 section 3 / WO-1040 section 3b trap 3) - " +
                                 "a grade gate would mean the median player never sees the reward that " +
                                 "justifies the dungeon, and locks out exactly the players who are dying.");
                    continue;
                }
                float sum = 0f;
                foreach (var w in row.Weights)
                {
                    if (w == null) continue;
                    if (string.IsNullOrEmpty(w.Id))
                        failures.Add($"NO PAYOUT: score {score} contains an EMPTY outcome id - a " +
                                     "'nothing' result. Every row must pay a real gem.");
                    if (w.Weight > 0f) sum += w.Weight;
                }
                if (sum <= 0f)
                    failures.Add($"NO PAYOUT: score {score} has zero total weight.");
            }

            // A FIRST polish must never shatter - that stone is the run's guaranteed payout.
            string svc = ReadStripped("_Modules/Village/Crafting/JewelPolishService.cs", failures);
            if (svc != null && svc.IndexOf("isRePolish &&", StringComparison.Ordinal) < 0)
                failures.Add("SHATTER UNGATED: the shatter roll is no longer conditioned on isRePolish. " +
                             "A first polish must never destroy the stone - it is the run's guaranteed " +
                             "payout, and shattering it would break 'every completed run pays'.");

            log.Append($"always-pays: {DungeonRunGrade.MaxStars + 1} rows all pay; ");
        }

        // ── 5. The grade shapes ODDS ONLY ─────────────────────────────────────

        private static void CheckOddsOnly(List<string> failures, StringBuilder log)
        {
            // (a) Duration is a single scalar, not a per-score curve.
            string json = ReadCanonical("jewel-polish.json", failures);
            if (json == null) return;
            if (json.IndexOf("\"secondsByScore\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                json.IndexOf("\"durationByScore\"", StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("AXIS STACKING: jewel-polish.json authors a per-score DURATION. The grade " +
                             "must shape the ODDS and nothing else (WO-1042 section 5(2)) - stacking " +
                             "every axis on one input makes a good run trivialise the system and a bad " +
                             "run feel worthless. Hold time constant so the player can plan.");

            // (b) Every score row offers the SAME tier set - only the weights differ. A row that adds
            //     or drops a gem id would be the grade changing the TIER SET, not the odds.
            List<string> baseline = null;
            for (int score = 0; score <= DungeonRunGrade.MaxStars; score++)
            {
                var row = JewelPolishCatalog.RowFor(score);
                if (row == null || row.Weights == null) continue;
                var ids = new List<string>();
                foreach (var w in row.Weights) if (w != null) ids.Add(w.Id);
                ids.Sort(StringComparer.Ordinal);
                if (baseline == null) { baseline = ids; continue; }
                if (string.Join(",", baseline) != string.Join(",", ids))
                    failures.Add($"AXIS STACKING: score {score} offers a DIFFERENT set of gems " +
                                 $"[{string.Join(",", ids)}] than the baseline [{string.Join(",", baseline)}]. " +
                                 "The grade may move probabilities, never the tier set itself.");
            }

            // (c) The odds must actually MOVE, or the grade is decorative.
            var lo = ChanceOf(DungeonExclusiveItems.HeartstoneCrystalId, 0);
            var hi = ChanceOf(DungeonExclusiveItems.HeartstoneCrystalId, DungeonRunGrade.MaxStars);
            if (hi <= lo)
                failures.Add($"GRADE INERT: heartstone odds do not improve with grade " +
                             $"(score 0 = {lo:P1}, score {DungeonRunGrade.MaxStars} = {hi:P1}). " +
                             "Mastery must pay, or the rubric is decoration.");

            log.Append($"odds-only: tier set constant, heartstone {lo:P0}->{hi:P0}; ");
        }

        // ── 6. ONE table for every path; disclosure DERIVED from it ───────────

        private static void CheckSingleTableAndDerivedDisclosure(List<string> failures, StringBuilder log)
        {
            string json = ReadCanonical("jewel-polish.json", failures);
            if (json != null)
            {
                // Money buys ATTEMPTS, never better odds (owner ruling 2026-08-16). A second,
                // purchaser-weighted table is the one thing that would break that.
                foreach (var banned in new[] { "\"paidOutcomes\"", "\"premiumOutcomes\"",
                                               "\"purchasedOutcomes\"", "\"adOutcomes\"" })
                {
                    if (json.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0)
                        failures.Add($"FAIRNESS BROKEN: jewel-polish.json authors {banned} - a second " +
                                     "odds table. Free, ad-funded and paid rolls MUST share the identical " +
                                     "per-roll table; money buys ATTEMPTS, never better odds. This is the " +
                                     "property the whole design rests on.");
                }
            }

            // The DISCLOSED odds must be computed from the SAME table the roll uses. A drift here is
            // not merely a bug - it is misrepresentation of a random outcome, which is exactly what the
            // store disclosure regimes police.
            for (int score = 0; score <= DungeonRunGrade.MaxStars; score++)
            {
                foreach (bool isRe in new[] { false, true })
                {
                    var odds = JewelPolishService.DescribeOdds(score, isRe);
                    if (odds == null || odds.Count == 0)
                    {
                        failures.Add($"DISCLOSURE MISSING: no odds described for score {score} " +
                                     $"(rePolish={isRe}). The confirmation screen would show nothing.");
                        continue;
                    }
                    float sum = 0f;
                    bool hasShatter = false;
                    foreach (var o in odds) { sum += o.Chance; if (o.IsShatter) hasShatter = true; }
                    if (Mathf.Abs(sum - 1f) > 0.001f)
                        failures.Add($"DISCLOSURE WRONG: score {score} (rePolish={isRe}) odds sum to " +
                                     $"{sum:F3}, not 1.000. Displayed percentages must be the real ones.");
                    if (isRe && !hasShatter)
                        failures.Add($"DISCLOSURE INCOMPLETE: score {score} re-polish odds omit the " +
                                     "SHATTER line. The player must be told the stone can be destroyed " +
                                     "before confirming.");
                    if (!isRe && hasShatter)
                        failures.Add($"WRONG DISCLOSURE: a FIRST polish discloses a shatter chance, but " +
                                     "a first polish can never shatter.");
                }
            }

            // ⛔ STAKING GRANTS ATTEMPTS, NEVER ODDS (owner ruling 2026-08-16). The owner's first
            // proposal was +5% odds for SKR stakers; it would have broken the identical-table
            // property, and she replaced it with extra ATTEMPTS. So the roll and the disclosed odds
            // must never consult the bonus provider - a staker's roll is exactly as likely as a free
            // player's. This is a SOURCE assertion because it is a structural guarantee: the numbers
            // cannot differ if the code cannot see the provider.
            string svc = ReadStripped("_Modules/Village/Crafting/JewelPolishService.cs", failures);
            if (svc != null)
            {
                foreach (var seam in new[] { "RollOutcome", "DescribeOdds" })
                {
                    int at = svc.IndexOf(seam, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        failures.Add($"PREMISE FAILED: '{seam}' not found in JewelPolishService - the " +
                                     "fairness assertion can no longer be made.");
                        continue;
                    }
                }
                // Attempt/cap authorization legitimately consults the provider earlier in the
                // service. Only the outcome/disclosure surface must be structurally blind to it.
                int outcomeAt = svc.IndexOf("public static string RollOutcome", StringComparison.Ordinal);
                string outcomeAndDisclosure = outcomeAt >= 0 ? svc.Substring(outcomeAt) : svc;
                foreach (var banned in new[] { "PolishBonuses", "IPolishBonusProvider" })
                {
                    if (outcomeAndDisclosure.IndexOf(banned, StringComparison.Ordinal) < 0) continue;
                    failures.Add(
                        $"FAIRNESS BROKEN: JewelPolishService outcome/disclosure references {banned}. The bonus provider " +
                        "grants ATTEMPTS ONLY and must never be visible to the roll or to the disclosed " +
                        "odds - a staker's roll must be exactly as likely as a free player's. The owner " +
                        "explicitly replaced a proposed +5% staker odds bonus with extra attempts for " +
                        "precisely this reason. Move the bonus back to the attempt/cap path.");
                }
            }

            // The provider interface itself must stay attempt-shaped. An odds-shaped member on it is
            // how the invariant would erode without any single call site looking wrong.
            string prov = ReadStripped("_Modules/Core/Catalog/PolishBonusProvider.cs", failures);
            if (prov != null)
            {
                foreach (var banned in new[] { "OddsBonus", "LuckBonus", "WeightBonus",
                                               "TierBonus", "ChanceBonus", "OddsMultiplier" })
                {
                    if (prov.IndexOf(banned, StringComparison.Ordinal) >= 0)
                        failures.Add($"FAIRNESS BROKEN: IPolishBonusProvider exposes '{banned}' - an " +
                                     "ODDS-shaped grant. The seam may grant attempts and roll-cap only.");
                }
            }

            log.Append("one-table: no paid tier, no staking odds, disclosure derived + sums to 1; ");
        }

        // ── 7. The loop closes: outputs are the ids the recipes consume ───────

        private static void CheckLoopCloses(List<string> failures, StringBuilder log)
        {
            string recipes = ReadCanonical("jeweler-recipes.json", failures);
            if (recipes == null) return;

            foreach (var id in DungeonExclusiveItems.RefinedGems)
            {
                if (recipes.IndexOf(id, StringComparison.Ordinal) < 0)
                    failures.Add($"LOOP DEAD-ENDS: polish can output '{id}', but jeweler-recipes.json " +
                                 "does not consume it. The chain would break one step from the finish.");
            }

            // And every gem the polish can produce must be a gem the recipes actually want.
            for (int score = 0; score <= DungeonRunGrade.MaxStars; score++)
            {
                var row = JewelPolishCatalog.RowFor(score);
                if (row == null || row.Weights == null) continue;
                foreach (var w in row.Weights)
                {
                    if (w == null || string.IsNullOrEmpty(w.Id)) continue;
                    if (recipes.IndexOf(w.Id, StringComparison.Ordinal) < 0)
                        failures.Add($"ORPHAN OUTPUT: score {score} can produce '{w.Id}', which no " +
                                     "jeweler recipe consumes - the player would bank a dead item.");
                }
            }
            log.Append("loop: outputs match recipe inputs; ");
        }

        // ── 8. Rates sized against the REAL recipe gem counts ─────────────────

        private static void CheckRunsPerRing(List<string> failures, StringBuilder log)
        {
            // The ring chain ring_iron -> steadfast -> embercoil -> heartward, read from
            // jeweler-recipes.json at source: ember x4, aether x3, heartstone x1.
            var need = new Dictionary<string, int>
            {
                { DungeonExclusiveItems.EmberCrystalId, 4 },
                { DungeonExclusiveItems.AetherShardId, 3 },
                { DungeonExclusiveItems.HeartstoneCrystalId, 1 },
            };

            // One rough stone per completed run, one gem per stone, so runs == gems.
            const float WorstAcceptableRuns = 30f;   // WO-1041 section 3: "40 runs reads as no reward at all"

            for (int score = 0; score <= DungeonRunGrade.MaxStars; score++)
            {
                float binding = 0f;
                string bindingId = "";
                foreach (var kv in need)
                {
                    float p = ChanceOf(kv.Key, score);
                    if (p <= 0f)
                    {
                        failures.Add($"UNREACHABLE: '{kv.Key}' has zero chance at score {score}, so the " +
                                     "ring chain can never complete at that grade.");
                        continue;
                    }
                    float runs = kv.Value / p;
                    if (runs > binding) { binding = runs; bindingId = kv.Key; }
                }
                if (binding > WorstAcceptableRuns)
                    failures.Add($"RATE TOO THIN: at score {score} the full ring chain needs ~{binding:F1} " +
                                 $"runs (bound by '{bindingId}'). WO-1041 section 3 is explicit that a rate " +
                                 "needing dozens of runs reads as no reward at all - retune " +
                                 "jewel-polish.json against the real recipe gem counts.");
                log.Append($"runs@{score}={binding:F1} ");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static float ChanceOf(string gemId, int score)
        {
            foreach (var o in JewelPolishService.DescribeOdds(score, false))
                if (string.Equals(o.Id, gemId, StringComparison.Ordinal)) return o.Chance;
            return 0f;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        private static string ReadCanonical(string fileName, List<string> failures)
        {
            string path = Path.Combine(Application.dataPath, "Resources/Data/Canonical/" + fileName);
            if (!File.Exists(path))
            {
                failures.Add($"PREMISE FAILED: {fileName} not found at {path}.");
                return null;
            }
            return File.ReadAllText(path);
        }

        /// <summary>
        /// Read a source file with COMMENTS AND STRING LITERALS STRIPPED. Mandatory for every source
        /// match in this suite: the file it most often reads (BuildTimerService) and this suite itself
        /// discuss "TryInstantFinish" and "JobRushPolicy" in prose constantly, so unstripped matching
        /// would report a pass off a comment. Several oracles in this project have already done that.
        /// </summary>
        private static string ReadStripped(string relToAssets, List<string> failures)
        {
            string path = Path.Combine(Application.dataPath, relToAssets);
            if (!File.Exists(path))
            {
                failures.Add($"PREMISE FAILED: source not found at {path}.");
                return null;
            }
            return StripCommentsAndStrings(File.ReadAllText(path));
        }

        /// <summary>
        /// Blank out //, /* */, "...", @"..." and '.' so a regex/IndexOf sees CODE only. Replaces
        /// stripped spans with spaces so offsets and line numbers still line up.
        /// </summary>
        private static string StripCommentsAndStrings(string src)
        {
            if (string.IsNullOrEmpty(src)) return "";
            var sb = new StringBuilder(src.Length);
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];

                // line comment
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }
                // block comment
                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    sb.Append("  "); i += 2;
                    while (i < n && !(src[i] == '*' && i + 1 < n && src[i + 1] == '/'))
                    { sb.Append(src[i] == '\n' ? '\n' : ' '); i++; }
                    if (i < n) { sb.Append("  "); i += 2; }
                    continue;
                }
                // verbatim string
                if (c == '@' && i + 1 < n && src[i + 1] == '"')
                {
                    sb.Append("  "); i += 2;
                    while (i < n)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < n && src[i + 1] == '"') { sb.Append("  "); i += 2; continue; }
                            sb.Append(' '); i++; break;
                        }
                        sb.Append(src[i] == '\n' ? '\n' : ' '); i++;
                    }
                    continue;
                }
                // regular string
                if (c == '"')
                {
                    sb.Append(' '); i++;
                    while (i < n)
                    {
                        if (src[i] == '\\' && i + 1 < n) { sb.Append("  "); i += 2; continue; }
                        if (src[i] == '"') { sb.Append(' '); i++; break; }
                        sb.Append(src[i] == '\n' ? '\n' : ' '); i++;
                    }
                    continue;
                }
                // char literal
                if (c == '\'')
                {
                    sb.Append(' '); i++;
                    while (i < n)
                    {
                        if (src[i] == '\\' && i + 1 < n) { sb.Append("  "); i += 2; continue; }
                        if (src[i] == '\'') { sb.Append(' '); i++; break; }
                        sb.Append(' '); i++;
                    }
                    continue;
                }

                sb.Append(c); i++;
            }
            return sb.ToString();
        }

        private static bool Finish(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = log.ToString().TrimEnd();
                Debug.Log("DUNGEON_GEM_EXCLUSIVITY_OK " + reason);
                return true;
            }
            var sb = new StringBuilder();
            sb.Append("DUNGEON_GEM_EXCLUSIVITY_FAIL: ").Append(failures.Count).Append(" failure(s):");
            foreach (var f in failures) sb.Append("\n  - ").Append(f);
            reason = sb.ToString();
            Debug.LogError(reason);
            return false;
        }
    }
}
