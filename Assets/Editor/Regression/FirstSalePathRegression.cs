// =============================================================================
// FirstSalePathRegression — WO-1801 [first-sale-path]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor.Regression
//   public static bool Run(out string reason)   -- NEVER throws
//   markers: FIRST_SALE_PATH_OK (Debug.Log) / FIRST_SALE_PATH_FAIL (LogError)
//
// Owner, 2026-09-16, verbatim: "yeah cause i just want a first sale you know".
//
// WHAT THIS PINS, and why each case exists rather than being a comment:
//
//  A [barracks-goal]  The $1.99 FIRST BUY completes its one nameable goal. Before
//     WO-1801 it granted wood 600 / iron 300 against a Barracks that costs wood 600
//     + iron 320 - the pack was the Barracks build MINUS 20 IRON, so the basket was
//     shaped like a goal it could not finish. A pack that misses its own promise by a
//     rounding error is unbuyable twice: once because it does not work, and once
//     because the player finds out after paying.
//
//  B [cap-fit]  ...and it still FITS a brand-new save. Paid grants BYPASS the town
//     bank cap (EconomyService.GrantPurchased -> BankGrantKind.PurchasedOrPromised),
//     so an oversized basket lands in full and then the player's ordinary faucet
//     clamps to zero until they spend back under cap - the buyer is PUNISHED by the
//     purchase. Six of the nine shelf rows overflow the base cap today (WO-1798 s1d);
//     the FIRST buy, aimed at a save with 0 wood and 0 iron, must not.
//
//  C [dominance]  ...and it makes NO impulse rung pointless. A first buy that grants at
//     least as much as a single-resource rung in every lane, at the same price or less,
//     strictly dominates it. test/purchases.quote.test.js "no impulse rung is strictly
//     dominated" is the AUTHORITY on the shipped rule; this case pins it HERE, next to the
//     goal it is in tension with, so a seat raising iron to "give more headroom" learns why
//     the ceiling exists before the JS suite tells them. Every rung, every lane and every
//     price comes from the data - no SKU name and no anchor is hardcoded, because the first
//     cut did exactly that and the hollow-pass scanner caught it standing down twice.
//
//  D [door-no-nag]  The door offers at most ONCE per structure per session, and a
//     resolve with nothing to offer is silent.
//
//  E [door-no-invented-discount]  The client never prints a discount percentage. The
//     2000 bps shortfall grant is issued SERVER-SIDE at the till and the public price
//     list deliberately excludes it (api/purchases/quote.js), so a client-side
//     "20% off today" would state something this build cannot prove and would be flatly
//     wrong for a player who already spent their 7-day window (CLAUDE.md s11B).
//
//  F [door-instrumented]  Both funnel events have exactly one emit site each.
//
// EVERY NUMBER IS READ FROM DATA. The Barracks cost comes from structures-catalog.json
// and the caps from storage-caps.json; not one literal cost or cap appears below. A
// hand-typed expectation here would be the duplicated state CLAUDE.md s2/s5/s8 records.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Commerce;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class FirstSalePathRegression
    {
        private const string PacksRelPath = "Data/Canonical/packs.json";
        private const string StructuresRelPath = "Data/Canonical/structures-catalog.json";
        private const string CapsRelPath = "Data/Canonical/storage-caps.json";

        /// <summary>The structure the first-buy basket is shaped to complete (owner: the muster yard).</summary>
        private const string GoalStructureId = "barracks";

        /// <summary>
        /// The lanes a paid grant can land in. Crystals and coins are UNCAPPED BY DESIGN
        /// (storage-caps.json own _note + TownBankCapacity.UncappableResources), so the cap case
        /// skips them rather than inventing a ceiling for them.
        /// </summary>
        private static readonly string[] CappedLanes = { "wood", "iron", "stone" };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== FirstSalePathRegression [first-sale-path] (WO-1801: the $1.99 first buy " +
                           "completes the Barracks, fits the bank, and is OFFERED at the moment of need) ===");

            try
            {
                var grant = ReadFirstBuyGrant(failures, log, out string firstBuySku, out double firstBuyUsd);
                var cost = ReadGoalCost(failures, log);
                var caps = ReadBaseCaps(failures, log);

                if (grant != null && cost != null) CaseGoalIsCompleted(grant, cost, firstBuySku, failures, log);
                if (grant != null && caps != null) CaseFitsTheBank(grant, caps, firstBuySku, failures, log);
                if (grant != null) CaseDoesNotDominateAnImpulseRung(firstBuySku, firstBuyUsd, failures, log);

                CaseDoorOffersOncePerStructure(failures, log);
                CaseDoorInventsNoDiscount(failures, log);
                CaseDoorIsInstrumented(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[first-sale-path] THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "FIRST SALE PATH OK - the $1.99 first-buy grant covers the '" + GoalStructureId +
                         "' build cost in every lane, every granted lane still fits the bare base bank cap " +
                         "of a brand-new save, it makes no impulse rung pointless at its own price, " +
                         "the build-mode door offers at most once per structure per session, " +
                         "the client invents no discount percentage, and both funnel events have exactly " +
                         "one emit site.";
                Debug.Log("FIRST_SALE_PATH_OK\n" + log);
                return true;
            }
            reason = "first-sale-path: " + failures.Count + " failure(s): " + string.Join(" | ", failures);
            Debug.LogError("FIRST_SALE_PATH_FAIL: " + failures.Count + " failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }

        // ── Data reads (never a literal expectation) ─────────────────────────

        /// <summary>
        /// The first-buy pack's economy grant, resolved through the SAME authority the runtime door
        /// uses (<see cref="FirstBuyOffer.Resolve"/>, i.e. the authored FIRST BUY badge) so this
        /// suite can never pass on a pack the game would not offer.
        /// </summary>
        private static Dictionary<string, int> ReadFirstBuyGrant(List<string> failures, StringBuilder log,
                                                                 out string sku, out double usd)
        {
            sku = null; usd = 0d;
            var pack = FirstBuyOffer.Resolve();
            if (pack == null)
            {
                failures.Add("[barracks-goal] FirstBuyOffer.Resolve() returned null - no storeVisible pack " +
                             "carries the '" + FirstBuyOffer.FirstBuyBadge + "' badge with a price, so the " +
                             "first-buy door can never be offered and nothing below is verifiable.");
                return null;
            }
            sku = pack.Sku;
            usd = pack.Pricing != null ? pack.Pricing.Usd : 0d;
            var e = pack.Contents != null ? pack.Contents.Economy : null;
            if (e == null)
            {
                failures.Add("[barracks-goal] first-buy pack '" + sku + "' has NO economy bag - it grants nothing.");
                return null;
            }
            var grant = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "wood", e.Wood }, { "iron", e.Iron }, { "stone", e.Stone },
                { "crystals", e.Crystals }, { "coins", e.Coins },
            };
            log.AppendLine("  first buy: '" + sku + "' " + pack.UsdReference + " grants wood " + e.Wood +
                           " / iron " + e.Iron + " / stone " + e.Stone + " / crystals " + e.Crystals);
            return grant;
        }

        /// <summary>The goal structure's BUILD cost. ⚠ It lives in structures-catalog.json
        /// (<c>repo.cost</c>) - building-tiers.json holds the UPGRADE ladder, which is a different
        /// number entirely (T1 is already several times the build).</summary>
        private static Dictionary<string, int> ReadGoalCost(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(StructuresRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[barracks-goal] " + StructuresRelPath + " unreadable - the goal cost is unknown.");
                return null;
            }
            JToken root;
            try { root = JToken.Parse(json); }
            catch (Exception ex)
            {
                failures.Add("[barracks-goal] " + StructuresRelPath + " parse error: " + ex.Message);
                return null;
            }
            foreach (var row in FindRows(root))
            {
                if (!(row is JObject o)) continue;
                if (!string.Equals(o["id"]?.ToString(), GoalStructureId, StringComparison.OrdinalIgnoreCase)) continue;
                var c = o["repo"]?["cost"] as JObject;
                if (c == null)
                {
                    failures.Add("[barracks-goal] '" + GoalStructureId + "' has no repo.cost in " + StructuresRelPath);
                    return null;
                }
                var cost = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "wood", c["wood"]?.Value<int>() ?? 0 },
                    { "iron", c["iron"]?.Value<int>() ?? 0 },
                    // 'food' is the retired authoring name for STONE (EconomyService.ResourceCost
                    // still carries the FormerlySerializedAs), so read both and take the larger.
                    { "stone", Math.Max(c["stone"]?.Value<int>() ?? 0, c["food"]?.Value<int>() ?? 0) },
                    { "crystals", c["crystals"]?.Value<int>() ?? 0 },
                };
                log.AppendLine("  goal '" + GoalStructureId + "' build cost (structures-catalog repo.cost): wood " +
                               cost["wood"] + " / iron " + cost["iron"] + " / stone " + cost["stone"] +
                               " / crystals " + cost["crystals"]);
                return cost;
            }
            failures.Add("[barracks-goal] no '" + GoalStructureId + "' row in " + StructuresRelPath);
            return null;
        }

        /// <summary>The non-building BASE STORE every save holds before any container exists.</summary>
        private static Dictionary<string, int> ReadBaseCaps(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(CapsRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[cap-fit] " + CapsRelPath + " unreadable - the base cap is unknown.");
                return null;
            }
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex)
            {
                failures.Add("[cap-fit] " + CapsRelPath + " parse error: " + ex.Message);
                return null;
            }
            var bag = root["baseCap"] as JObject;
            if (bag == null)
            {
                failures.Add("[cap-fit] " + CapsRelPath + " has no baseCap object.");
                return null;
            }
            var caps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var lane in CappedLanes) caps[lane] = bag[lane]?.Value<int>() ?? 0;
            log.AppendLine("  base bank cap (storage-caps baseCap): wood " + caps["wood"] + " / iron " +
                           caps["iron"] + " / stone " + caps["stone"] + " (crystals + coins uncapped by design)");
            return caps;
        }

        /// <summary>structures-catalog.json is an array of rows, possibly under a named property.</summary>
        private static IEnumerable<JToken> FindRows(JToken root)
        {
            if (root is JArray direct) { foreach (var t in direct) yield return t; yield break; }
            if (root is JObject o)
                foreach (var prop in o.Properties())
                    if (prop.Value is JArray arr)
                        foreach (var t in arr) yield return t;
        }

        // ── Cases ────────────────────────────────────────────────────────────

        private static void CaseGoalIsCompleted(Dictionary<string, int> grant, Dictionary<string, int> cost,
                                                string sku, List<string> failures, StringBuilder log)
        {
            // ⛔ NO VACUOUS PASS. Every assertion below is inside `if (kv.Value > 0)`, so a goal whose
            // cost read as all-zero would assert NOTHING and still report green - the scanner's arm D.
            // A free Barracks is a data defect, not a reason to stand down, so say so and fail.
            int priced = 0;
            foreach (var kv in cost) if (kv.Value > 0) priced++;
            if (priced == 0)
                failures.Add("[barracks-goal] the '" + GoalStructureId + "' build cost read as ZERO in every lane " +
                             "from " + StructuresRelPath + " - there is no goal left to complete, so this case " +
                             "would prove nothing. Check repo.cost on that row.");

            bool ok = true;
            foreach (var kv in cost)
            {
                if (kv.Value <= 0) continue;
                int granted = grant.TryGetValue(kv.Key, out int g) ? g : 0;
                if (granted < kv.Value)
                {
                    ok = false;
                    failures.Add("[barracks-goal] '" + sku + "' grants " + granted + " " + kv.Key +
                                 " but the '" + GoalStructureId + "' build costs " + kv.Value +
                                 " - short by " + (kv.Value - granted) + ". The first buy must COMPLETE the one " +
                                 "goal its basket is shaped like; missing it by a rounding error is the exact " +
                                 "defect WO-1801 was opened to fix (the old basket missed the iron by 20).");
                }
            }
            if (ok) log.AppendLine("  [barracks-goal] PASS - every priced lane of the goal is covered with headroom.");
        }

        private static void CaseFitsTheBank(Dictionary<string, int> grant, Dictionary<string, int> caps,
                                            string sku, List<string> failures, StringBuilder log)
        {
            // ⛔ NO VACUOUS PASS (scanner arm D): every comparison below needs BOTH a granted lane and
            // an authored cap, so a zeroed grant or a capless file would assert nothing and read green.
            int comparable = 0;
            foreach (var lane in CappedLanes)
                if ((grant.TryGetValue(lane, out int gg) ? gg : 0) > 0 &&
                    (caps.TryGetValue(lane, out int cc) ? cc : 0) > 0) comparable++;
            if (comparable == 0)
                failures.Add("[cap-fit] no lane could be compared: the first-buy grant has no positive " +
                             "wood/iron/stone amount, or " + CapsRelPath + " authors no baseCap for them. Either " +
                             "way the cap rule is unproven, which must never read as a pass.");

            bool ok = true;
            foreach (var lane in CappedLanes)
            {
                int granted = grant.TryGetValue(lane, out int g) ? g : 0;
                int cap = caps.TryGetValue(lane, out int c) ? c : 0;
                if (granted <= 0 || cap <= 0) continue;
                if (granted > cap)
                {
                    ok = false;
                    failures.Add("[cap-fit] '" + sku + "' grants " + granted + " " + lane + " but the BASE bank " +
                                 "cap is " + cap + ". A paid grant bypasses the cap, so the overflow lands and " +
                                 "then the player's ordinary " + lane + " income clamps to zero until they spend " +
                                 "back under it - the FIRST purchase would punish the buyer. Size the first rung " +
                                 "to the bare base cap of a brand-new save (0 wood, 0 iron).");
                }
            }
            if (ok) log.AppendLine("  [cap-fit] PASS - every granted lane fits the bare base cap.");
        }

        /// <summary>
        /// [dominance] The first buy must not make ANY impulse rung pointless — it must not grant
        /// &gt;= that rung's whole basket while costing the same or less.
        ///
        /// <para>⚠ THIS CASE WAS REWRITTEN AFTER THE HOLLOW-PASS SCANNER CAUGHT IT (2026-09-16,
        /// arms A + B). The first cut hung the whole check on ONE named SKU and stood down in two
        /// places — "'impulse-iron-small' is not in the catalogue" and "it sits at a different
        /// anchor" — each of which logged a SKIP to the reader and reported a PASS to the caller.
        /// That is the exact crime the scanner exists to stop: the case that guards the constraint
        /// would have gone quiet the moment the constraint became interesting (a renamed or
        /// repriced rung). There is now NO stand-down: the rung set, the price comparison and the
        /// resource keys are all derived from the data, an absent fixture FAILS by name, and the
        /// "nothing was judged" outcome is itself a named failure.</para>
        ///
        /// <para>It is deliberately STRONGER than, and never in conflict with,
        /// <c>test/purchases.quote.test.js</c>'s same-anchor rule: same-or-cheaper is a superset of
        /// same-price, and the JS suite remains the authority on the shipped rule.</para>
        /// </summary>
        private static void CaseDoesNotDominateAnImpulseRung(string firstBuySku, double firstBuyUsd,
                                                            List<string> failures, StringBuilder log)
        {
            const string Tag = "[dominance] ";
            string json = DeNelle.Core.CanonicalJson.Read(PacksRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add(Tag + PacksRelPath + " unreadable (CanonicalJson.Read returned empty) - the " +
                             "dominance rule cannot be checked at all.");
                return;
            }
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex) { failures.Add(Tag + PacksRelPath + " parse error: " + ex.Message); return; }

            var packs = root["packs"] as JArray;
            if (packs == null || packs.Count == 0)
            {
                failures.Add(Tag + PacksRelPath + " has no packs[] rows - the fixture this case reads is " +
                             "MISSING, so it proves nothing about the ladder.");
                return;
            }

            // The key surface comes from the DATA, exactly as the JS suite derives it: a
            // hand-maintained lane list fails OPEN by checking one fewer key (that is how the
            // food -> stone rename slipped through once already).
            var keys = new List<string>();
            JObject firstBuyRow = null;
            var rungs = new List<JObject>();
            foreach (var t in packs)
            {
                if (!(t is JObject o)) continue;
                var econ = o["contents"]?["economy"] as JObject;
                if (econ != null)
                    foreach (var prop in econ.Properties())
                        if (!keys.Contains(prop.Name)) keys.Add(prop.Name);
                if (string.Equals(o["sku"]?.ToString(), firstBuySku, StringComparison.Ordinal)) firstBuyRow = o;
                if (o["impulse"]?.Value<bool>() == true) rungs.Add(o);
            }

            if (firstBuyRow == null)
            {
                failures.Add(Tag + "the first-buy sku '" + firstBuySku + "' resolved from the catalog is not a " +
                             "row in " + PacksRelPath + " - the two readers disagree about the same file.");
                return;
            }
            if (rungs.Count == 0)
            {
                failures.Add(Tag + PacksRelPath + " authors NO impulse rows (impulse:true) - the ladder this " +
                             "case compares against is MISSING. ImpulsePackRegression requires the full " +
                             "small/medium/large family per resource, so this is a data defect, not a shrug.");
                return;
            }

            int judged = 0;
            foreach (var rung in rungs)
            {
                string rungSku = rung["sku"]?.ToString() ?? "<unnamed>";
                double rungUsd = rung["pricing"]?["usd"]?.Value<double>() ?? -1d;
                // A rung that costs LESS than the first buy cannot be dominated by it on price, so
                // it is out of scope for this rule - and `judged` below refuses to let an all-out-of-
                // scope ladder read as a pass.
                if (rungUsd < firstBuyUsd - 0.0001d)
                {
                    log.AppendLine("  " + Tag + "not judged: '" + rungSku + "' at $" + rungUsd +
                                   " is cheaper than the first buy at $" + firstBuyUsd + ".");
                    continue;
                }
                judged++;

                bool coversEverything = true, beatsSomething = false;
                var beaten = new List<string>();
                foreach (var key in keys)
                {
                    int mine = firstBuyRow["contents"]?["economy"]?[key]?.Value<int>() ?? 0;
                    int theirs = rung["contents"]?["economy"]?[key]?.Value<int>() ?? 0;
                    if (mine < theirs) { coversEverything = false; break; }
                    if (mine > theirs) { beatsSomething = true; beaten.Add(key + " " + mine + ">" + theirs); }
                }

                if (coversEverything && beatsSomething)
                    failures.Add(Tag + "'" + firstBuySku + "' ($" + firstBuyUsd + ") grants at least as much as " +
                                 "'" + rungSku + "' ($" + rungUsd + ") in EVERY lane and more in [" +
                                 string.Join(",", beaten.ToArray()) + "], so it STRICTLY DOMINATES that rung - " +
                                 "the rung is pointless at its price. test/purchases.quote.test.js 'no impulse " +
                                 "rung is strictly dominated' fails on exactly this (it is the authority). The " +
                                 "first buy only has to COMPLETE ITS GOAL; it must not beat a single-resource " +
                                 "rung at that rung's own job.");
            }

            if (judged == 0)
                failures.Add(Tag + "every one of the " + rungs.Count + " impulse rungs is CHEAPER than the " +
                             "first buy at $" + firstBuyUsd + ", so nothing was judged. The FIRST BUY is the " +
                             "bottom of the ladder by design - if it is now the dearest $ rung, the ladder has " +
                             "been re-shaped and this case is no longer measuring the shelf it was written for.");
            else
                log.AppendLine("  " + Tag + "PASS - " + judged + " of " + rungs.Count + " impulse rung(s) judged " +
                               "at or above the first buy's $" + firstBuyUsd + " anchor over " + keys.Count +
                               " data-derived lanes; none is strictly dominated.");
        }

        private static void CaseDoorOffersOncePerStructure(List<string> failures, StringBuilder log)
        {
            FirstBuyDoorModel.ResetSessionLatch();
            const string id = "first-sale-path-probe";

            if (FirstBuyDoorModel.WasOfferedThisSession(id))
                failures.Add("[door-no-nag] the session latch reports '" + id + "' as already offered " +
                             "immediately after ResetSessionLatch() - the latch does not clear.");

            FirstBuyDoorModel.MarkOfferedThisSession(id);
            if (!FirstBuyDoorModel.WasOfferedThisSession(id))
                failures.Add("[door-no-nag] MarkOfferedThisSession did not take - the door would re-offer on " +
                             "every reopen of the same structure, which is the nag the rule forbids.");

            // The gate itself: a latched structure can never resolve to a shown door, whichever
            // earlier gate happens to fire first in this headless context.
            var latched = FirstBuyDoorModel.Resolve(new DeNelle.Core.Catalog.CatalogEntry { id = id }, default);
            if (latched.Show)
                failures.Add("[door-no-nag] Resolve() still returns Show=true for a structure already offered " +
                             "this session (reason: " + latched.Reason + ").");

            // And nothing to offer is SILENT, never a half-built row.
            var empty = FirstBuyDoorModel.Resolve(null, default);
            if (empty.Show || !string.IsNullOrEmpty(empty.Caption))
                failures.Add("[door-no-nag] Resolve(null) produced a door/caption - a resolve with no entry must " +
                             "be silent.");

            FirstBuyDoorModel.ResetSessionLatch();
            if (FirstBuyDoorModel.WasOfferedThisSession(id))
                failures.Add("[door-no-nag] ResetSessionLatch() left the latch set.");

            log.AppendLine("  [door-no-nag] latch drive complete (set -> refuses -> reset); silent on a null entry.");
        }

        private static void CaseDoorInventsNoDiscount(List<string> failures, StringBuilder log)
        {
            string src = ReadSource("_Modules/Village/BuildMode/FirstBuyDoorModel.cs", failures, "[door-no-invented-discount]");
            // ReadSource was handed `failures` and has ALREADY recorded the miss by name one statement
            // above, so this is a second report being suppressed, not a silent stand-down.
            if (src == null) return;
            string code = StripComments(src);
            if (code.IndexOf("% off", StringComparison.Ordinal) >= 0 ||
                code.IndexOf("20%", StringComparison.Ordinal) >= 0)
                failures.Add("[door-no-invented-discount] FirstBuyDoorModel composes a discount percentage. The " +
                             "shortfall discount is issued SERVER-SIDE at the till, once per wallet per 7 days, " +
                             "and the public price list deliberately excludes it (api/purchases/quote.js), so the " +
                             "client cannot know it when the door is painted - printing one states an unprovable " +
                             "claim and is simply WRONG for a player who already spent the window (CLAUDE.md s11B).");
            else
                log.AppendLine("  [door-no-invented-discount] PASS - the door prints the authored USD only.");
        }

        private static void CaseDoorIsInstrumented(List<string> failures, StringBuilder log)
        {
            string src = ReadSource("_Modules/Village/BuildMode/FirstBuyDoorModel.cs", failures, "[door-instrumented]");
            // Same as above: the producer (ReadSource) already recorded the miss into `failures`.
            if (src == null) return;
            string code = StripComments(src);
            foreach (var call in new[] { "EventTracker.Track(EventShown", "EventTracker.Track(EventTapped" })
            {
                int count = 0, at = 0;
                while ((at = code.IndexOf(call, at, StringComparison.Ordinal)) >= 0) { count++; at += call.Length; }
                if (count != 1)
                    failures.Add("[door-instrumented] '" + call + "' has " + count + " emit site(s); exactly ONE is " +
                                 "required, so a funnel count can never be double-fired from a second surface " +
                                 "(the WO-1388 store-funnel discipline).");
            }
            if (code.IndexOf("shortfall_offer_shown", StringComparison.Ordinal) < 0 ||
                code.IndexOf("shortfall_offer_tapped", StringComparison.Ordinal) < 0)
                failures.Add("[door-instrumented] the two event NAMES are no longer declared here - the funnel " +
                             "would lose the only measurement of whether the door works.");
            log.AppendLine("  [door-instrumented] event names declared; one emit site each.");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string ReadSource(string relToAssets, List<string> failures, string tag)
        {
            string path = Path.Combine(Application.dataPath, relToAssets);
            if (!File.Exists(path))
            {
                failures.Add(tag + " source not found: " + relToAssets);
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex) { failures.Add(tag + " unreadable: " + ex.Message); return null; }
        }

        /// <summary>Comments stripped so a rule NAMED in prose is never mistaken for code; string
        /// literals are left intact so a real one cannot hide in one.</summary>
        private static string StripComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            bool block = false, line = false;
            for (int i = 0; i < src.Length; i++)
            {
                if (line) { if (src[i] == '\n') { line = false; sb.Append('\n'); } continue; }
                if (block) { if (src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/') { block = false; i++; } continue; }
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/') { line = true; continue; }
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*') { block = true; i++; continue; }
                sb.Append(src[i]);
            }
            return sb.ToString();
        }
    }
}
