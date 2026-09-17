// =============================================================================
// StoreSkrFlatLadderRegression — WO-1815 [skr-flat-ladder]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor.Regression
//   public static bool Run(out string reason)   -- NEVER throws
//   markers: SKR_FLAT_LADDER_OK (Debug.Log) / SKR_FLAT_LADDER_FAIL (LogError)
//
// Owner, 2026-09-16, verbatim: "change the store to SKR only and set to flat
// amounts" / "what if we drop the conversion and only do straight SKR for SKR
// build 100 200 300 500 9999" / "if we just list the SKR does it feel more
// impulse less cost" / "put big sales signs with x% off!!!!".
//
// WHAT EACH CASE EXISTS FOR, and why it is not a comment:
//
//  A [ladder]        Every PRICED pack authors pricing.skrFlat, and every authored
//     value sits on the owner's ladder. The two promo rows (no pricing.usd, never
//     quotable) must author NONE - a flat price on a row nothing can buy is a number
//     with no meaning, and the server would try to quote it.
//
//  B [anchor-cohort] Packs that share a pricing.usd anchor share an skrFlat. This is
//     not tidiness: test/purchases.quote.test.js judges "no impulse rung is strictly
//     dominated by another purchasable pack AT THE SAME USD ANCHOR", and WO-1801 spent
//     a real design constraint (iron 360, not 400) satisfying it. Two packs on one USD
//     anchor at two SKR prices silently changes what that test is measuring.
//
//  C [twins]         The StreamingAssets copy authors the identical flat amount for
//     every sku. The byte-identity of the two files is BuyGateAndPriceLadderRegression's
//     [dual-copy] case; this one asks the narrower question in the language of the field,
//     so a future partial patch names the sku that diverged rather than a byte offset.
//
//  D [server-mirror] ⛔ THE CASE THAT MAKES THIS FIELD SAFE TO RENDER AT ALL. The shelf
//     may print an authored flat amount (PackStore.SkrFlatShelfPrices) only because the
//     SERVER prices from the SAME authored row: packs.json reaches
//     api/_lib/sku-catalog.generated.json through the VERBATIM tools/gen-sku-catalog.mjs
//     copy. If those two files ever hold different flat amounts, the store shows one
//     number and charges another - the WO-1158 paid-but-not-granted family, which is
//     exactly what PurchaseQuoteService.cs:6-31 exists to prevent. Two copies of one
//     CONSTANT are admissible; two copies that drift are not. The remedy is one command:
//     node tools/gen-sku-catalog.mjs
//
//  E [skr-only]      The Solana shelf quotes ONE currency. Asserted on the live label
//     formatters (no '$' in what the SKR rail prints) AND on PackStore's own source for
//     the two switches, because a switch's value is a constant and the file is its
//     authority. ⛔ The USD code is NOT required to be absent - it is required to be
//     unreached. Stripping it is forbidden (CLAUDE.md §12); showing it is what the owner
//     ruled out.
//
//  F [sale-30]       A 30% sale renders the sign, the struck ORIGINAL SKR and the
//     discounted SKR - over the live types, from real wire JSON, not a source grep.
//
//  G [fail-closed]   No sale, no authored flat, or a served figure that is NOT lower
//     than the anchor ⇒ NO strike. The last one is the one that matters: until the
//     server prices flat (WO-1815 §6) the served figure is rate-derived and can sit
//     ABOVE the ladder rung, and striking then would cross out the CHEAPER number and
//     advertise a discount upwards.
//
//  H [rounding]      ceil(flat * (10000 - bps) / 10000) to a whole SKR, the same
//     direction as the shipped quoteAmount (api/_lib/purchase-catalog.js:281, owner
//     ruling 2026-08-23). ⛔ ONE IMPLEMENTATION: the headless capture's stub calls
//     DiscountedFlatSkr below, so the PNG cannot show a figure the rule would not
//     produce. A second copy of the rule in the harness is how a screenshot starts
//     lying.
//
//  I [corpus]        Anti-vacuity floor on the pack count, so a catalogue that failed
//     to load cannot pass every case above by having nothing to check.
//
// EVERY authored number is READ. The only literals here are the owner's own ladder and
// the bps of the deal she asked for.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class StoreSkrFlatLadderRegression
    {
        private const string Tag = "[skr-flat-ladder] ";

        private const string CanonicalPacks = "Resources/Data/Canonical/packs.json";
        private const string StreamingPacks = "StreamingAssets/Data/Canonical/packs.json";
        /// <summary>The generated server mirror. Relative to the REPO root, not to Assets/.</summary>
        private const string ServerMirror = "api/_lib/sku-catalog.generated.json";

        private const string PackStoreSource = "_Modules/Wallet/PackStore.cs";

        /// <summary>
        /// The owner's ladder, verbatim ("100 200 300 500 9999"), plus the 1000 rung WO-1815 §3 added
        /// because her five rungs do not cover the shelf's six USD bands. ⚠ 1000 is a PROPOSAL she can
        /// re-rule; if she does, this array and packs.json move in the SAME change.
        /// </summary>
        private static readonly double[] Ladder = { 100d, 200d, 300d, 500d, 1000d, 9999d };

        /// <summary>The deal the owner asked for: "can we run a 30% deal?".</summary>
        private const int SaleBps = 3000;

        /// <summary>Fewer packs than this and every case below would pass vacuously.</summary>
        private const int PackCorpusFloor = 25;

        /// <summary>
        /// THE rounding rule, in one place. <c>ceil</c> to a whole SKR, the same direction as the
        /// shipped USD path (<c>api/_lib/purchase-catalog.js:281</c>) and for the same reason recorded
        /// there: it favours us, and that is the owner's 2026-08-23 ruling carried forward rather than
        /// re-decided. Returns 0 for an unusable input, never a guess.
        /// <para>⛔ The CLIENT never calls this on the money path - it prints the server's transported
        /// figure. It exists for the oracle below and for the headless capture's stub row, so the
        /// screenshot and the rule cannot disagree.</para>
        /// </summary>
        public static double DiscountedFlatSkr(double flatSkr, int bps)
        {
            if (!(flatSkr > 0d)) return 0d;
            if (bps <= 0 || bps >= 10000) return Math.Ceiling(flatSkr);
            return Math.Ceiling(flatSkr * (10000 - bps) / 10000d);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== StoreSkrFlatLadderRegression [skr-flat-ladder] (WO-1815: the store is " +
                           "SKR-only at flat amounts, and a 30% sale shows the sign + both figures) ===");

            try
            {
                var canonical = ReadPackFlats(CanonicalPacks, failures, log);
                if (canonical != null)
                {
                    CaseLadder(canonical, failures, log);
                    CaseAnchorCohort(canonical, failures, log);
                    CaseTwins(canonical, failures, log);
                    CaseServerMirror(canonical, failures, log);
                }
                CaseSkrOnly(failures, log);
                CaseSaleRendersBothFigures(failures, log);
                CaseFailClosed(failures, log);
                CaseRounding(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + "THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "SKR FLAT LADDER OK - every priced pack authors a flat SKR amount on the owner's " +
                         "ladder, packs sharing a USD anchor share a rung, the StreamingAssets twin and the " +
                         "generated server mirror author the identical amount for every sku, the Solana " +
                         "shelf quotes SKR and no dollars (with the USD path compiled but unreached), a " +
                         "3000-bps sale renders \"30% OFF\" with the struck original SKR and the discounted " +
                         "SKR, and every fail-closed branch draws nothing.";
                Debug.Log("SKR_FLAT_LADDER_OK\n" + log);
                return true;
            }
            reason = "skr-flat-ladder: " + failures.Count + " failure(s): " + string.Join(" | ", failures);
            Debug.LogError("SKR_FLAT_LADDER_FAIL: " + failures.Count + " failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }

        // =====================================================================
        //  Data reads
        // =====================================================================

        private sealed class PackFlat
        {
            public string Sku;
            public double Usd;
            public bool HasFlat;
            public double Flat;
        }

        /// <summary>Reads sku / pricing.usd / pricing.skrFlat out of one packs.json copy.</summary>
        private static List<PackFlat> ReadPackFlats(string relToAssets, List<string> failures, StringBuilder log)
        {
            string text = ReadAssetsFile(relToAssets, failures);
            if (text == null) return null;
            JObject doc;
            try { doc = JObject.Parse(text); }
            catch (Exception ex)
            {
                failures.Add(Tag + relToAssets + " does not parse: " + ex.Message);
                return null;
            }
            var rows = doc["packs"] as JArray;
            if (rows == null || rows.Count == 0)
            {
                failures.Add(Tag + relToAssets + " has no packs[] rows.");
                return null;
            }
            var list = new List<PackFlat>(rows.Count);
            foreach (var tok in rows)
            {
                if (!(tok is JObject p)) continue;
                var pricing = p["pricing"] as JObject;
                var flatTok = pricing != null ? pricing["skrFlat"] : null;
                list.Add(new PackFlat
                {
                    Sku = (string)p["sku"],
                    Usd = pricing != null && pricing["usd"] != null ? (double)pricing["usd"] : 0d,
                    HasFlat = flatTok != null,
                    Flat = flatTok != null ? (double)flatTok : 0d,
                });
            }
            if (list.Count < PackCorpusFloor)
            {
                // CASE I [corpus] - stated as a failure, not a skip: a collapsed catalogue would make
                // every question below pass by having nothing to answer.
                failures.Add(Tag + "[corpus] only " + list.Count + " packs were read from " + relToAssets +
                             " (floor " + PackCorpusFloor + ", measured 29 on 2026-09-16). The catalogue did " +
                             "not load and every case below would pass vacuously. FAIL, not a skip.");
                return null;
            }
            log.AppendLine("  [corpus] " + list.Count + " packs read from " + relToAssets);
            return list;
        }

        // =====================================================================
        //  CASE A [ladder]
        // =====================================================================
        private static void CaseLadder(List<PackFlat> packs, List<string> failures, StringBuilder log)
        {
            int priced = 0, promo = 0;
            foreach (var p in packs)
            {
                bool sellable = p.Usd > 0d;
                if (!sellable)
                {
                    promo++;
                    if (p.HasFlat)
                        failures.Add(Tag + "[ladder] '" + p.Sku + "' authors no pricing.usd (it is promo-only " +
                                     "and never quotable) yet carries skrFlat " + Fmt(p.Flat) + ". A flat price " +
                                     "on a row nothing can buy is a number with no meaning, and the server " +
                                     "would try to quote it.");
                    continue;
                }
                priced++;
                if (!p.HasFlat)
                {
                    failures.Add(Tag + "[ladder] priced pack '" + p.Sku + "' ($" + Fmt(p.Usd) + ") authors NO " +
                                 "pricing.skrFlat. Under the SKR-only shelf that row has no figure to print " +
                                 "and the server has no flat amount to quote.");
                    continue;
                }
                if (Array.IndexOf(Ladder, p.Flat) < 0)
                    failures.Add(Tag + "[ladder] '" + p.Sku + "' authors skrFlat " + Fmt(p.Flat) + ", which is " +
                                 "not on the owner's ladder (" + string.Join("/", Array.ConvertAll(Ladder, Fmt)) +
                                 "). Her ruling was verbatim: \"100 200 300 500 9999\".");
            }
            log.AppendLine("  [ladder] " + priced + " priced packs on the ladder, " + promo +
                           " promo-only rows correctly unpriced");
        }

        // =====================================================================
        //  CASE B [anchor-cohort]
        // =====================================================================
        private static void CaseAnchorCohort(List<PackFlat> packs, List<string> failures, StringBuilder log)
        {
            var byAnchor = new Dictionary<double, List<PackFlat>>();
            foreach (var p in packs)
            {
                if (p.Usd <= 0d || !p.HasFlat) continue;
                if (!byAnchor.TryGetValue(p.Usd, out var bucket))
                    byAnchor[p.Usd] = bucket = new List<PackFlat>();
                bucket.Add(p);
            }
            foreach (var kv in byAnchor)
            {
                double first = kv.Value[0].Flat;
                foreach (var p in kv.Value)
                {
                    if (p.Flat == first) continue;
                    failures.Add(Tag + "[anchor-cohort] the $" + Fmt(kv.Key) + " anchor carries TWO flat " +
                                 "prices: '" + kv.Value[0].Sku + "' at " + Fmt(first) + " SKR and '" + p.Sku +
                                 "' at " + Fmt(p.Flat) + " SKR. test/purchases.quote.test.js judges " +
                                 "domination AT A SHARED USD ANCHOR, so two SKR prices on one anchor change " +
                                 "what that test measures without saying so.");
                }
                log.AppendLine("  [anchor-cohort] $" + Fmt(kv.Key) + " -> " + Fmt(first) + " SKR (" +
                               kv.Value.Count + " pack(s))");
            }
        }

        // =====================================================================
        //  CASE C [twins]
        // =====================================================================
        private static void CaseTwins(List<PackFlat> canonical, List<string> failures, StringBuilder log)
        {
            var twin = ReadPackFlats(StreamingPacks, failures, log);
            if (twin == null) return;
            var bySku = new Dictionary<string, PackFlat>(StringComparer.Ordinal);
            foreach (var p in twin) if (!string.IsNullOrEmpty(p.Sku)) bySku[p.Sku] = p;
            int matched = 0;
            foreach (var p in canonical)
            {
                if (string.IsNullOrEmpty(p.Sku)) continue;
                if (!bySku.TryGetValue(p.Sku, out var t))
                {
                    failures.Add(Tag + "[twins] '" + p.Sku + "' exists in the Resources copy and NOT in the " +
                                 "StreamingAssets copy.");
                    continue;
                }
                if (t.HasFlat != p.HasFlat || t.Flat != p.Flat)
                    failures.Add(Tag + "[twins] '" + p.Sku + "' authors skrFlat " +
                                 (p.HasFlat ? Fmt(p.Flat) : "<absent>") + " in Resources and " +
                                 (t.HasFlat ? Fmt(t.Flat) : "<absent>") + " in StreamingAssets.");
                else matched++;
            }
            log.AppendLine("  [twins] " + matched + "/" + canonical.Count + " skus agree on skrFlat across both copies");
        }

        // =====================================================================
        //  CASE D [server-mirror]
        // =====================================================================
        private static void CaseServerMirror(List<PackFlat> canonical, List<string> failures, StringBuilder log)
        {
            string path = RepoPath(ServerMirror);
            if (!File.Exists(path))
            {
                failures.Add(Tag + "[server-mirror] " + ServerMirror + " does not exist. It is the file the " +
                             "SERVER prices from, and the shelf is allowed to print an authored flat amount " +
                             "ONLY because the two are the same row. Remedy: node tools/gen-sku-catalog.mjs");
                return;
            }
            JObject doc;
            try { doc = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                failures.Add(Tag + "[server-mirror] " + ServerMirror + " does not parse: " + ex.Message);
                return;
            }
            var rows = doc["packs"] as JArray;
            if (rows == null || rows.Count == 0)
            {
                failures.Add(Tag + "[server-mirror] " + ServerMirror + " has no packs[] rows.");
                return;
            }
            var mirror = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var tok in rows)
            {
                if (!(tok is JObject p)) continue;
                string sku = (string)p["sku"];
                if (string.IsNullOrEmpty(sku)) continue;
                var pricing = p["pricing"] as JObject;
                mirror[sku] = pricing != null ? pricing["skrFlat"] : null;
            }
            int agreed = 0, missing = 0;
            foreach (var p in canonical)
            {
                if (string.IsNullOrEmpty(p.Sku)) continue;
                if (!mirror.TryGetValue(p.Sku, out var tok))
                {
                    failures.Add(Tag + "[server-mirror] '" + p.Sku + "' is absent from " + ServerMirror +
                                 " - the mirror is stale. Remedy: node tools/gen-sku-catalog.mjs");
                    continue;
                }
                bool has = tok != null;
                double flat = has ? (double)tok : 0d;
                if (has != p.HasFlat || flat != p.Flat)
                {
                    missing++;
                    failures.Add(Tag + "[server-mirror] '" + p.Sku + "' authors skrFlat " +
                                 (p.HasFlat ? Fmt(p.Flat) : "<absent>") + " in packs.json and " +
                                 (has ? Fmt(flat) : "<absent>") + " in " + ServerMirror + ". The shelf would " +
                                 "print one number and the till would charge another (the WO-1158 family). " +
                                 "Remedy: node tools/gen-sku-catalog.mjs - never hand-edit the mirror.");
                }
                else agreed++;
            }
            log.AppendLine("  [server-mirror] " + agreed + " skus agree with " + ServerMirror +
                           (missing > 0 ? ", " + missing + " DIVERGED" : ", none diverged"));
        }

        // =====================================================================
        //  CASE E [skr-only]
        // =====================================================================
        private static void CaseSkrOnly(List<string> failures, StringBuilder log)
        {
            // E1 - the LIVE label formatters print SKR and no dollars.
            var served = Deserialize("{\"sku\":\"x\",\"amountBaseUnits\":\"210000000\",\"decimals\":6," +
                                     "\"usdAnchor\":4.99}", failures);
            if (served != null)
            {
                if (served.SkrEffectiveLabel != "210 SKR")
                    failures.Add(Tag + "[skr-only] a 210-SKR quote labels itself \"" + served.SkrEffectiveLabel +
                                 "\", not \"210 SKR\".");
                if (served.SkrEffectiveLabel.IndexOf('$') >= 0)
                    failures.Add(Tag + "[skr-only] the SKR label carries a '$'.");
            }

            // E2/E3 - the two switches and the ONE place each is honoured. A switch's value is a
            // constant, so the file is its authority; the ORDER is the guarantee that the USD line is
            // unreachable on the Solana rail rather than merely discouraged.
            string src = ReadAssetsFile(PackStoreSource, failures);
            if (src == null) return;
            string code = StripComments(src);

            RequireOrdered(code,
                "private static readonly bool ShowUsdAlongsideSkr = false;",
                null, null,
                Tag + "[skr-only] PackStore no longer declares ShowUsdAlongsideSkr = false. The owner ruled " +
                "the store SKR-only on 2026-09-16; if the dollars are wanted back that is a ruling, not an " +
                "edit.", failures);

            RequireOrdered(code,
                "private string StorePriceMinor(PackDef pack)",
                "if (!ShowUsdAlongsideSkr) return string.Empty;",
                "return pack != null ? pack.UsdApprox() : string.Empty;",
                Tag + "[skr-only] StorePriceMinor's SKR-only stand-down is missing, or it no longer sits " +
                "BEFORE the UsdApprox return. Both halves matter: the USD path must stay COMPILED " +
                "(CLAUDE.md §12 forbids stripping it, and StorePiSkinCurrencyRegression pins that literal) " +
                "and it must be UNREACHED on the Solana rail.", failures);

            RequireOrdered(code,
                "string rateLine = quote.Pinned",
                "quote.IsFlatPriced",
                "per SKR",
                Tag + "[skr-only] the confirm line's rate clause is no longer gated on IsFlatPriced. A flat " +
                "quote carries no rate at all, so the ungated form prints \"at $0.00000000 per SKR ()\" - a " +
                "fabricated rate on the one screen where the player is deciding.", failures);

            if (code.IndexOf("private static readonly bool SkrFlatShelfPrices", StringComparison.Ordinal) < 0)
                failures.Add(Tag + "[skr-only] PackStore no longer declares SkrFlatShelfPrices - the switch " +
                             "that decides whether an AUTHORED amount may fill a gap the server left. Its " +
                             "doc comment is the record of why that is admissible at all.");
            log.AppendLine("  [skr-only] the SKR label carries no '$'; both switches declared; the USD path " +
                           "is compiled and unreached on the Solana rail");
        }

        // =====================================================================
        //  CASE F [sale-30]
        // =====================================================================
        private static void CaseSaleRendersBothFigures(List<string> failures, StringBuilder log)
        {
            const double Flat = 300d;
            double effective = DiscountedFlatSkr(Flat, SaleBps);          // 210
            long baseUnits = (long)effective * 1000000L;                  // 6 dp on the SKR rail
            var sale = Deserialize("{\"sku\":\"starters-hand\",\"amountBaseUnits\":\"" +
                                   baseUnits.ToString(CultureInfo.InvariantCulture) + "\",\"decimals\":6," +
                                   "\"usdAnchor\":4.99,\"saleBps\":" +
                                   SaleBps.ToString(CultureInfo.InvariantCulture) + "}", failures);
            if (sale == null) return;

            if (!sale.IsOnSale)
                failures.Add(Tag + "[sale-30] saleBps " + SaleBps + " does not read as a sale.");
            if (sale.SaleBadgeText != "30% OFF")
                failures.Add(Tag + "[sale-30] the sign reads \"" + sale.SaleBadgeText + "\", not \"30% OFF\".");
            if (!sale.HasStruckSkr(Flat))
                failures.Add(Tag + "[sale-30] a sale whose served figure (" + Fmt(effective) + " SKR) is lower " +
                             "than the authored rung (" + Fmt(Flat) + " SKR) draws NO strike.");
            if (sale.SkrAnchorLabel(Flat) != "300 SKR")
                failures.Add(Tag + "[sale-30] the struck anchor reads \"" + sale.SkrAnchorLabel(Flat) +
                             "\", not \"300 SKR\".");
            if (sale.SkrEffectiveLabel != "210 SKR")
                failures.Add(Tag + "[sale-30] the discounted figure reads \"" + sale.SkrEffectiveLabel +
                             "\", not \"210 SKR\".");
            string struck = DeNelle.Wallet.StorePackCard.Strike(sale.SkrAnchorLabel(Flat));
            if (struck != "<s>300 SKR</s>")
                failures.Add(Tag + "[sale-30] the strike markup is \"" + struck + "\" - TMP renders <s>...</s>.");
            if (sale.SkrAnchorLabel(Flat).IndexOf('$') >= 0 || sale.SkrEffectiveLabel.IndexOf('$') >= 0)
                failures.Add(Tag + "[sale-30] a sale figure carries a '$' on the SKR-only shelf.");
            log.AppendLine("  [sale-30] " + SaleBps + " bps on a " + Fmt(Flat) + "-SKR rung -> sign \"" +
                           sale.SaleBadgeText + "\", struck " + sale.SkrAnchorLabel(Flat) + ", charged " +
                           sale.SkrEffectiveLabel);
        }

        // =====================================================================
        //  CASE G [fail-closed]
        // =====================================================================
        private static void CaseFailClosed(List<string> failures, StringBuilder log)
        {
            // No sale at all.
            var plain = Deserialize("{\"sku\":\"x\",\"amountBaseUnits\":\"210000000\",\"decimals\":6}", failures);
            if (plain != null && plain.HasStruckSkr(300d))
                failures.Add(Tag + "[fail-closed] a quote with no saleBps wants its SKR anchor struck.");

            // No authored flat amount to strike.
            var noFlat = Deserialize("{\"sku\":\"x\",\"amountBaseUnits\":\"210000000\",\"decimals\":6," +
                                     "\"saleBps\":3000}", failures);
            if (noFlat != null)
            {
                if (noFlat.HasStruckSkr(0d))
                    failures.Add(Tag + "[fail-closed] a sale with NO authored flat amount struck something.");
                if (noFlat.SkrAnchorLabel(0d).Length != 0)
                    failures.Add(Tag + "[fail-closed] the struck label is non-empty with no anchor to strike.");
            }

            // ⛔ THE ONE THAT MATTERS: the served figure is NOT lower than the rung. This is the live
            // state until WO-1815 §6 lands server-side (a rate-derived quote at ~431 SKR against a
            // 300-SKR rung), and a strike here would advertise a discount UPWARDS.
            var notFlatYet = Deserialize("{\"sku\":\"x\",\"amountBaseUnits\":\"431000000\",\"decimals\":6," +
                                         "\"saleBps\":3000}", failures);
            if (notFlatYet != null && notFlatYet.HasStruckSkr(300d))
                failures.Add(Tag + "[fail-closed] a served figure of 431 SKR struck a 300-SKR anchor - the " +
                             "strike crossed out the CHEAPER number.");

            // A flat quote carries no rate; a rate-derived one does. The confirm line reads this.
            var flatQuote = Deserialize("{\"sku\":\"x\",\"amountBaseUnits\":\"210000000\",\"decimals\":6}", failures);
            if (flatQuote != null && !flatQuote.IsFlatPriced)
                failures.Add(Tag + "[fail-closed] a quote carrying no rate does not read as flat-priced.");
            var rated = Deserialize("{\"sku\":\"x\",\"amountBaseUnits\":\"431000000\",\"decimals\":6," +
                                    "\"rate\":0.01745041}", failures);
            if (rated != null && rated.IsFlatPriced)
                failures.Add(Tag + "[fail-closed] a rate-derived quote reads as flat-priced, so the confirm " +
                             "line would withhold the rate that priced the charge.");
            log.AppendLine("  [fail-closed] no sale / no anchor / served-not-lower all draw nothing; " +
                           "flat-vs-rated is read off the transported rate");
        }

        // =====================================================================
        //  CASE H [rounding]
        // =====================================================================
        private static void CaseRounding(List<string> failures, StringBuilder log)
        {
            // ceil, to a whole SKR, at every rung the owner named. The expectations are the rule applied
            // by hand ONCE, here, where a reader can check them against the sentence above.
            var expected = new Dictionary<double, double>
            {
                { 100d, 70d }, { 200d, 140d }, { 300d, 210d },
                { 500d, 350d }, { 1000d, 700d }, { 9999d, 7000d },
            };
            foreach (var kv in expected)
            {
                double got = DiscountedFlatSkr(kv.Key, SaleBps);
                if (got != kv.Value)
                    failures.Add(Tag + "[rounding] " + SaleBps + " bps off " + Fmt(kv.Key) + " SKR gives " +
                                 Fmt(got) + ", not " + Fmt(kv.Value) + " (ceil to a whole SKR).");
                if (got != Math.Ceiling(got))
                    failures.Add(Tag + "[rounding] " + Fmt(got) + " is not a whole SKR.");
            }
            if (DiscountedFlatSkr(0d, SaleBps) != 0d)
                failures.Add(Tag + "[rounding] an unpriced row produced a discounted figure.");
            if (DiscountedFlatSkr(300d, 0) != 300d || DiscountedFlatSkr(300d, 10000) != 300d)
                failures.Add(Tag + "[rounding] an unusable bps did not fall back to the full rung.");
            log.AppendLine("  [rounding] ceil(flat * (10000 - " + SaleBps + ") / 10000) at every rung: " +
                           "100->70, 200->140, 300->210, 500->350, 1000->700, 9999->7000");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static DeNelle.Wallet.PurchaseQuote Deserialize(string json, List<string> failures)
        {
            try { return JsonConvert.DeserializeObject<DeNelle.Wallet.PurchaseQuote>(json); }
            catch (Exception ex)
            {
                failures.Add(Tag + "wire row would not parse (" + ex.GetType().Name + "): " + json);
                return null;
            }
        }

        private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private static string ReadAssetsFile(string relToAssets, List<string> failures)
        {
            string path = Path.Combine(Application.dataPath, relToAssets);
            if (!File.Exists(path))
            {
                failures.Add(Tag + "file not found: " + relToAssets);
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add(Tag + relToAssets + " unreadable: " + ex.Message);
                return null;
            }
        }

        /// <summary>Repo-root-relative path. ⛔ Derived from Application.dataPath, never a drive letter:
        /// the repo root is machine-dependent (CLAUDE.md §0).</summary>
        private static string RepoPath(string relToRepoRoot)
        {
            var assets = new DirectoryInfo(Application.dataPath);
            string root = assets.Parent != null ? assets.Parent.FullName : Application.dataPath;
            return Path.Combine(root, relToRepoRoot.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Each non-null fragment must appear, in this order. ⚠ Distance is NOT asserted: WO-1800's
        /// RESULT records a proximity assertion that false-positived because comment-stripping collapsed
        /// the gap between two tokens. Order is a real textual fact; nearness is not a control-flow one.
        /// </summary>
        private static void RequireOrdered(string code, string a, string b, string c, string why,
                                          List<string> failures)
        {
            int i = code.IndexOf(a, StringComparison.Ordinal);
            if (i < 0) { failures.Add(why + " (missing: \"" + a + "\")"); return; }
            if (b != null)
            {
                int j = code.IndexOf(b, i, StringComparison.Ordinal);
                if (j < 0) { failures.Add(why + " (missing after the first: \"" + b + "\")"); return; }
                i = j;
            }
            if (c != null && code.IndexOf(c, i, StringComparison.Ordinal) < 0)
                failures.Add(why + " (missing after the second: \"" + c + "\")");
        }

        /// <summary>Comments stripped so a rule NAMED in prose is never mistaken for code; string
        /// literals are LEFT INTACT, because two of the assertions above are about literals.</summary>
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
