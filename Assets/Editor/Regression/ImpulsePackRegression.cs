// =============================================================================
// ImpulsePackRegression [impulse-pack] -- WO-1037: single-resource impulse packs,
// legalised by the WO-947 section 12 amendment (the PURCHASE boundary is not the
// COST boundary).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only; references DeNelle.Core +
// DeNelle.Wallet). Contract mirrors the other Run(out reason) oracles:
//   public static bool Run(out string reason)   -- NEVER throws
//   markers: IMPULSE_PACK_OK (Debug.Log) / IMPULSE_PACK_FAIL (LogError)
//
// THE RULING (owner, 2026-08-16, WO-1037 section 3 option (b), verbatim):
//   "we should have small instant packs" / "small wood only" / "or medium wood" /
//   "or large wood" / "same with all types for impulse small purchases"
//
// THE FOUR GUARDRAILS (WO-947 section 12c) -- one case each, because each of them
// is a rule a future authoring edit could break silently and none of them is
// visible in a diff of a JSON blob:
//   1. EXACTLY ONE economy key per SKU. A multi-resource impulse bundle re-mixes
//      the WO-947 cost baskets through the back door and is FORBIDDEN -- it would
//      be a genuine section 2 violation wearing a store item's clothes.
//   2. $5 CEILING, impulse tiers (owner standing pricing ruling, memory
//      solana-store-early-access-pack-pricing).
//   3. RESOURCES ONLY -- never a structure, never a level, never a queue
//      completion. Money buys the INPUT, never the OUTCOME (WO-947 section 12d
//      explicitly does NOT rule that selling time/outcomes is allowed).
//   4. The offer only appears against a REAL shortfall -- asserted here on the
//      RESOLVER, which is the thing that decides whether an offer exists at all.
//
// WHY CASE 6 EXERCISES THE REAL RESOLVER RATHER THAN RE-READING THE JSON: cases
// 1-5 prove the DATA is legal. Only case 6 proves the code that CHOOSES from that
// data honours "smallest sufficient, no upsell at the shortfall moment" (WO-1037
// section 1) and refuses an affordable ask. A data-only oracle would pass with a
// resolver that always returned the LARGE pack -- which is the exact turn from
// helpful to extractive the WO was written to prevent.
//
// DELIBERATELY NOT ASSERTED HERE: that a purchase cannot complete. That is
// WalletProviderSelectionRegression's job and it already owns it (the
// FeatureFlags.RealmStorePurchase defaultOn:false pin + the Pay/PayFlat refusals
// + the Mainnet block). Duplicating it would create a second authority on the
// payment rails -- the drift bug this project keeps hitting. What IS asserted
// here is case 7: that the shortfall SURFACE has no route into the grant path.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "impulse-pack suite", () => { if (!DeNelle.Editor.Regression.ImpulsePackRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[impulse-pack] " + r); });
//
// Standalone: run-unity-method DeNelle.Editor.Regression.ImpulsePackRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Wallet;

namespace DeNelle.Editor.Regression
{
    public static class ImpulsePackRegression
    {
        private const string PacksRelPath = "Data/Canonical/packs.json";

        /// <summary>The owner's standing price ceiling. $5 is the CEILING, not a target.</summary>
        private const double PriceCeilingUsd = 5.0d;

        /// <summary>The four harvestable resource KEYS money may buy (WO-1037 section 3 / WO-947 12b).</summary>
        private static readonly string[] ResourceKeys = { "wood", "iron", "stone", "crystals" };

        /// <summary>The size ladder, smallest first. Every resource must author all three.</summary>
        private static readonly string[] Sizes = { "small", "medium", "large" };

        /// <summary>
        /// The ONLY keys an impulse pack object may carry. An UNKNOWN key is a FAILURE, not a
        /// shrug: this is how "grant a finished upgrade" would arrive -- as a new field nobody
        /// reviewed, on a SKU that already passes every value-based check. An allowlist catches
        /// the field that has not been invented yet; a denylist of bad words never could.
        /// </summary>
        private static readonly HashSet<string> AllowedPackKeys =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "sku", "tier", "name", "tagline", "theme", "founderOnly",
                "impulse", "impulseResource", "impulseSize",
                "pricing", "contents", "legacySkus",

                // ── REVIEWED 2026-08-21, owner shelf ruling ──────────────────────────
                // Added in the same change that put them in the data, which is what this
                // allowlist exists to force. All three are PRESENTATION keys: they decide
                // WHERE a pack is shown, never WHAT it grants, so none of them can be the
                // "grant a finished upgrade" field this gate was built to catch. The value
                // checks above ([single-key], [ceiling], [resources-only] contents scan) are
                // untouched and still bind.
                //   shelfCurated  - owner ruling 2026-08-21 ("one impulse tier per resource"):
                //                   exactly three impulse SKUs are browsable shelf rows; the
                //                   other nine stay shortfall-only. PackStore reads this INSTEAD
                //                   of a hardcoded SKU list, so the shelf decision lives in data.
                //                   ⚠ [not-on-shelf] below still pins that non-curated impulse
                //                   SKUs are skipped by the card loop - the wall WO-947 refused
                //                   is still structurally prevented.
                //   storeVisible  - the WO-1118 honesty flag: hides a SKU whose advertised
                //                   contents have no redeemer yet (a pack that cannot deliver
                //                   must not be sellable). Hiding can only ever REMOVE an offer.
                //   storeSection  - which shelf section renders the card. Pure layout.
                "shelfCurated", "storeVisible", "storeSection",
                //   _shelfNote    - a leading-underscore DOCUMENTATION key (why this row is
                //                   curated/hidden). Underscore-prefixed keys are inert notes,
                //                   read by nobody at runtime; it grants nothing and cannot.
                "_shelfNote",

                // ── REVIEWED 2026-08-21, WO-1050 "The Night Market" ──────────────────
                // Added in the SAME change that put them in the data - which is the whole
                // point of this allowlist: a new key does not reach the shelf until a human
                // has said what it does. All four are PRESENTATION keys. Each one decides
                // WHERE or HOW a pack is SHOWN and NONE of them can change WHAT IT GRANTS,
                // so none can be the "grant a finished upgrade" field this gate exists to
                // catch. The value checks above ([single-key], [ceiling], [resources-only]
                // contents scan) are untouched and still bind on every impulse row.
                //   band       - free|gap|basket|patronage. The shelf GROUPING key, replacing
                //                storeSection's mood names. The three curated impulse rows
                //                carry band:"gap"; the other nine are still skipped by the
                //                card loop entirely ([not-on-shelf] below still pins that).
                //                Pure layout: it moves a card between headings.
                //   orbTint    - hex tint of the card gem. DECORATION ONLY, and deliberately
                //                so: the owner is red/green colourblind, so band identity is
                //                carried by the mark + the text eyebrow + the greyscale step
                //                ([band-greyscale] asserts that), never by a hue. A tint can
                //                make a card prettier; it cannot make it mean anything.
                //   compareTo  - the SKU the spotlight comparison line is drawn against. It
                //                selects an EXISTING row to do arithmetic against and grants
                //                nothing; [compare-arithmetic] proves the arithmetic is real
                //                or the line is absent.
                //   anchorOnly - renders fully priced with NO Buy control ever built. It can
                //                only ever REMOVE the ability to buy, which is the safe
                //                direction. NO ROW CARRIES IT TODAY (see the packs.json
                //                _schemaNotes) - the key is admitted here so the mechanism and
                //                its review land together rather than the day someone needs it.
                "band", "orbTint", "compareTo", "anchorOnly",
            };

        private static readonly HashSet<string> AllowedContentsKeys =
            new HashSet<string>(StringComparer.Ordinal) { "cosmetics", "economy", "convenience" };

        // `skrFlat` reviewed and admitted 2026-09-16 (WO-1815, owner: "change the store to SKR only and
        // set to flat amounts") - the authored flat SKR rung the server prices from via the generated
        // mirror; it grants nothing, so the resources-only covenant this list guards is untouched.
        private static readonly HashSet<string> AllowedPricingKeys =
            new HashSet<string>(StringComparer.Ordinal) { "usd", "usdc", "sol", "skr", "skrFlat" };

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("IMPULSE_PACK_OK - " + reason);
            else Debug.LogError("IMPULSE_PACK_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== ImpulsePackRegression [impulse-pack] (WO-1037: one resource, one SKU, $5 ceiling) ===");

            try
            {
                CaseDualCopy(failures, log);
                CaseClientBindsEveryEconomyKey(failures, log);

                var impulse = ParseImpulsePacks(failures, log);
                if (impulse != null)
                {
                    CaseSingleEconomyKey(impulse, failures, log);
                    CasePriceCeiling(impulse, failures, log);
                    CaseResourcesOnly(impulse, failures, log);
                    CaseFamilyIsComplete(impulse, failures, log);
                }

                CaseResolverPicksSmallestSufficient(failures, log);
                CaseSurfaceHasNoGrantRoute(failures, log);
                CaseNotOnTheShelf(failures, log);

                // ── WO-1050 The Night Market ──────────────────────────────────────
                CaseAnchorBuildsNoBuy(failures, log);
                CaseBandIsKnown(failures, log);
                CaseCompareIsArithmetic(failures, log);
                CaseVisibleIsGrantable(failures, log);
                CaseBandGreyscale(failures, log);

                // ── WO-1339 the catch-up rail is the Gap band, and it must repaint ──
                CaseGapBandReachesTheShelf(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[impulse-pack] ImpulsePackRegression THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "IMPULSE PACK OK - " + (ResourceKeys.Length * Sizes.Length) + " single-resource SKUs, each " +
                         "granting EXACTLY ONE economy key, none above the $" + PriceCeilingUsd.ToString("0.00") +
                         " ceiling, none granting a structure/level/queue completion; the dual JSON copies are " +
                         "byte-identical; and the resolver returns the SMALLEST SUFFICIENT pack (and nothing at " +
                         "all when the upgrade is affordable). NIGHT MARKET (WO-1050): every browsable row " +
                         "carries an authored band, the free band is priceless, every comparison resolves to real " +
                         "arithmetic over two real SKUs, every advertised line is grantable today, an anchorOnly " +
                         "row provably builds NO Buy control, and the four band lights step apart in rec.709 " +
                         "greyscale with a word on every band.";
                Debug.Log("IMPULSE_PACK_OK\n" + log);
                return true;
            }
            reason = "impulse-pack: " + failures.Count + " failure(s): " + string.Join(" | ", failures);
            Debug.LogError("IMPULSE_PACK_FAIL: " + failures.Count + " failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }

        // Newtonsoft ignores unknown JSON members by default. Compare the raw
        // authored economy-key union with PackEconomy's JsonProperty bindings so
        // a rename cannot silently deserialize paid value to zero.
        private static void CaseClientBindsEveryEconomyKey(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(PacksRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[client-bindings] " + PacksRelPath +
                    " unreadable; client binding coverage cannot be proven");
                return;
            }
            var root = JObject.Parse(json);
            var authored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in (JArray)root["packs"])
            {
                var economy = row["contents"] != null ? row["contents"]["economy"] as JObject : null;
                if (economy == null) continue;
                foreach (var property in economy.Properties()) authored.Add(property.Name);
            }

            var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in typeof(PackEconomy).GetFields())
            {
                var attrs = field.GetCustomAttributes(typeof(JsonPropertyAttribute), false);
                if (attrs.Length == 1) bound.Add(((JsonPropertyAttribute)attrs[0]).PropertyName);
            }
            foreach (string key in authored)
                if (!bound.Contains(key))
                    failures.Add("[client-bindings] packs.json authors economy key '" + key +
                                 "' but PackEconomy has no JsonProperty binding; Newtonsoft would silently " +
                                 "drop paid value and grant zero");
            log.AppendLine("  [client-bindings] " + authored.Count +
                           " authored economy key(s) all bind in PackEconomy");
        }

        // =====================================================================
        //  CASE 5 [dual-copy] -- Resources and StreamingAssets must be identical.
        //  Runs FIRST: if the two copies differ, every other case is measuring a
        //  file the shipped build may never load.
        // =====================================================================
        private static void CaseDualCopy(List<string> failures, StringBuilder log)
        {
            string res = Application.dataPath + "/Resources/" + PacksRelPath;
            string sa  = Application.dataPath + "/StreamingAssets/" + PacksRelPath;
            bool hasRes = File.Exists(res), hasSa = File.Exists(sa);
            if (!hasRes || !hasSa)
            {
                failures.Add("[dual-copy] packs.json missing " + (hasRes ? "" : "the Resources copy ") +
                             (hasSa ? "" : "the StreamingAssets copy") +
                             " - PackCatalog reads Resources first and falls back to StreamingAssets, so one " +
                             "missing copy silently changes what a WebGL/desktop build loads.");
                return;
            }
            byte[] a = File.ReadAllBytes(res), b = File.ReadAllBytes(sa);
            bool equal = a.Length == b.Length;
            if (equal)
                for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { equal = false; break; }

            if (!equal)
                failures.Add("[dual-copy] packs.json Resources and StreamingAssets copies DIVERGED (" +
                             a.Length + " vs " + b.Length + " bytes). The editor/Resources path and the " +
                             "StreamingAssets path would sell DIFFERENT packs - and the price/amount a player " +
                             "sees would not be the one they are charged for.");
            else
                log.AppendLine("  [dual-copy] packs.json byte-identical across both copies (" + a.Length + " bytes)");
        }

        // =====================================================================
        //  Parse -- the RAW JObject list of impulse packs. Raw, not the typed
        //  PackDef, because cases 1 and 3 must see keys PackDef does not model:
        //  a field the loader silently ignores is exactly how an unreviewed grant
        //  would arrive, and a typed read would make it invisible.
        // =====================================================================
        private static List<JObject> ParseImpulsePacks(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(PacksRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[impulse-pack] " + PacksRelPath + " unreadable (CanonicalJson.Read returned empty) - " +
                             "no SKU is verifiable at all");
                return null;
            }

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex)
            {
                failures.Add("[impulse-pack] packs.json failed to parse: " + ex.Message);
                return null;
            }

            var arr = root["packs"] as JArray;
            if (arr == null || arr.Count == 0)
            {
                failures.Add("[impulse-pack] packs.json holds no 'packs' array");
                return null;
            }

            var impulse = new List<JObject>();
            foreach (var el in arr)
            {
                var o = el as JObject;
                if (o == null) continue;
                var flag = o["impulse"];
                if (flag != null && flag.Type == JTokenType.Boolean && flag.Value<bool>()) impulse.Add(o);
            }

            log.AppendLine("  packs.json -> " + arr.Count + " pack(s), " + impulse.Count + " tagged impulse:true");
            if (impulse.Count == 0)
                failures.Add("[impulse-pack] NO pack carries impulse:true. The WO-1037 SKUs are gone, or the " +
                             "family tag was renamed - ShortfallPackOffer resolves on that flag, so the shortfall " +
                             "offer would silently never appear.");
            return impulse;
        }

        // =====================================================================
        //  CASE 1 [single-key] -- WO-947 section 12c guardrail 1. THE load-bearing one.
        // =====================================================================
        private static void CaseSingleEconomyKey(List<JObject> impulse, List<string> failures, StringBuilder log)
        {
            foreach (var p in impulse)
            {
                string sku = (string)p["sku"] ?? "<no sku>";
                string tagged = ((string)p["impulseResource"] ?? "").Trim().ToLowerInvariant();

                var econ = p["contents"] != null ? p["contents"]["economy"] as JObject : null;
                if (econ == null)
                {
                    failures.Add("[single-key] '" + sku + "' has no contents.economy - it can grant nothing, " +
                                 "so a player paying for it receives nothing.");
                    continue;
                }

                var nonZero = new List<string>();
                foreach (var prop in econ.Properties())
                {
                    long v = 0;
                    try { v = prop.Value.Type == JTokenType.Integer ? prop.Value.Value<long>() : 0; }
                    catch { v = 0; }
                    if (v != 0) nonZero.Add(prop.Name + "=" + v);
                }

                if (nonZero.Count != 1)
                {
                    failures.Add("[single-key] '" + sku + "' grants " + nonZero.Count + " economy key(s) [" +
                                 string.Join(",", nonZero.ToArray()) + "] but MUST grant EXACTLY ONE. " +
                                 "WO-947 section 12c guardrail 1: a multi-resource impulse bundle re-mixes the " +
                                 "cost baskets through the back door and IS FORBIDDEN - that is a genuine " +
                                 "section 2 violation wearing a store item's clothes.");
                    continue;
                }

                string key = nonZero[0].Substring(0, nonZero[0].IndexOf('='));
                if (!string.Equals(key, tagged, StringComparison.OrdinalIgnoreCase))
                    failures.Add("[single-key] '" + sku + "' is tagged impulseResource='" + tagged +
                                 "' but grants '" + key + "'. ShortfallPackOffer resolves on the TAG, so this " +
                                 "SKU would be offered against a " + tagged + " shortfall and pay out " + key +
                                 " - the player is sold the wrong resource at the exact moment they are blocked.");

                if (Array.IndexOf(ResourceKeys, key.ToLowerInvariant()) < 0)
                    failures.Add("[single-key] '" + sku + "' grants '" + key + "', which is not one of the four " +
                                 "harvestable resources money may buy (wood/iron/food/crystals, WO-947 section 12b). " +
                                 "Selling glimmer or coins through the shortfall surface is a different ruling and " +
                                 "has not been made.");
            }
            log.AppendLine("  [single-key] " + impulse.Count + " SKU(s) checked for exactly-one-economy-key");
        }

        // =====================================================================
        //  CASE 2 [ceiling] -- $5 is the CEILING, and every rail must be payable.
        // =====================================================================
        private static void CasePriceCeiling(List<JObject> impulse, List<string> failures, StringBuilder log)
        {
            double maxSeen = 0d;
            foreach (var p in impulse)
            {
                string sku = (string)p["sku"] ?? "<no sku>";
                var pricing = p["pricing"] as JObject;
                if (pricing == null)
                {
                    failures.Add("[ceiling] '" + sku + "' has no pricing block.");
                    continue;
                }

                foreach (var rail in AllowedPricingKeys)
                {
                    var tok = pricing[rail];
                    double v = tok != null ? tok.Value<double>() : 0d;
                    if (v <= 0d)
                        failures.Add("[ceiling] '" + sku + "' has no positive '" + rail + "' price - the pack " +
                                     "is unpayable on that rail (PackCatalogTest requires all four).");
                }

                double usd  = pricing["usd"]  != null ? pricing["usd"].Value<double>()  : 0d;
                double usdc = pricing["usdc"] != null ? pricing["usdc"].Value<double>() : 0d;
                if (usd > maxSeen) maxSeen = usd;

                if (usd > PriceCeilingUsd + 0.0001d)
                    failures.Add("[ceiling] '" + sku + "' is priced $" + usd.ToString("0.00") + ", above the $" +
                                 PriceCeilingUsd.ToString("0.00") + " CEILING. Owner standing ruling (memory " +
                                 "solana-store-early-access-pack-pricing, restated in WO-947 section 12c guardrail 2): " +
                                 "impulse packs are cheap - $2 and $5 tiers, $5 max. Raising the ceiling is an " +
                                 "OWNER ruling with a new date, never an authoring edit.");

                if (Math.Abs(usd - usdc) > 0.0001d)
                    failures.Add("[ceiling] '" + sku + "' quotes usd $" + usd.ToString("0.00") + " but usdc " +
                                 usdc.ToString("0.00") + ". USDC is dollar-pegged and every other pack in this " +
                                 "file quotes them equal; a gap means the displayed reference price is not the " +
                                 "price charged on the default rail.");
            }
            log.AppendLine("  [ceiling] " + impulse.Count + " SKU(s) priced at or under $" +
                           PriceCeilingUsd.ToString("0.00") + " (dearest: $" + maxSeen.ToString("0.00") + ")");
        }

        // =====================================================================
        //  CASE 3 [resources-only] -- no structures, no levels, no queue
        //  completions, no cosmetics, no convenience, and NO UNKNOWN KEYS.
        // =====================================================================
        private static void CaseResourcesOnly(List<JObject> impulse, List<string> failures, StringBuilder log)
        {
            foreach (var p in impulse)
            {
                string sku = (string)p["sku"] ?? "<no sku>";

                foreach (var prop in p.Properties())
                    if (!AllowedPackKeys.Contains(prop.Name))
                        failures.Add("[resources-only] '" + sku + "' carries the UNREVIEWED key '" + prop.Name +
                                     "'. An impulse pack may only hold [" + string.Join(",", Join(AllowedPackKeys)) +
                                     "]. WO-947 section 12c guardrail 3: packs grant RESOURCES, never structures, " +
                                     "never levels, never queue completions - and section 12d says plainly that " +
                                     "selling time or outcomes is NOT ruled and needs a new owner decision. If this " +
                                     "key is legitimate, add it here in the same change that adds it to the data, so " +
                                     "the review happens.");

                var contents = p["contents"] as JObject;
                if (contents == null)
                {
                    failures.Add("[resources-only] '" + sku + "' has no contents block.");
                    continue;
                }

                foreach (var prop in contents.Properties())
                    if (!AllowedContentsKeys.Contains(prop.Name))
                        failures.Add("[resources-only] '" + sku + "' contents carries the UNREVIEWED key '" +
                                     prop.Name + "' (allowed: cosmetics, economy, convenience).");

                var cos = contents["cosmetics"] as JArray;
                if (cos != null && cos.Count > 0)
                    failures.Add("[resources-only] '" + sku + "' grants " + cos.Count + " cosmetic(s). An impulse " +
                                 "pack is a shortfall remedy, not a bundle - it grants its one resource and nothing " +
                                 "else. Cosmetics belong in the ladder/seasonal packs (tiers 1-13).");

                var conv = contents["convenience"] as JArray;
                if (conv != null && conv.Count > 0)
                {
                    var kinds = new List<string>();
                    foreach (var c in conv)
                    {
                        var co = c as JObject;
                        kinds.Add(co != null ? ((string)co["kind"] ?? "?") : "?");
                    }
                    failures.Add("[resources-only] '" + sku + "' grants convenience item(s) [" +
                                 string.Join(",", kinds.ToArray()) + "]. Those are TIME and OUTCOME " +
                                 "(instant-build IS a queue completion). WO-947 section 12d: selling time or " +
                                 "outcomes directly touches the crystal SINKS and is explicitly NOT ruled here - " +
                                 "it needs a NEW owner ruling with a new date, not an extension of this one.");
                }

                var pricing = p["pricing"] as JObject;
                if (pricing != null)
                    foreach (var prop in pricing.Properties())
                        if (!AllowedPricingKeys.Contains(prop.Name))
                            failures.Add("[resources-only] '" + sku + "' pricing carries the UNREVIEWED key '" +
                                         prop.Name + "' (allowed: " +
                                         string.Join(", ", AllowedPricingKeys) + ").");
            }
            log.AppendLine("  [resources-only] " + impulse.Count + " SKU(s): no cosmetics, no convenience, " +
                           "no unreviewed keys anywhere in the object");
        }

        private static string[] Join(HashSet<string> set)
        {
            var a = new string[set.Count];
            set.CopyTo(a);
            Array.Sort(a, StringComparer.Ordinal);
            return a;
        }

        // =====================================================================
        //  CASE 4 [family] -- all 4 x 3 rungs exist, tiers are unique, and the
        //  ladder actually climbs. A ladder whose medium grants less than its
        //  small is a shop that punishes paying more.
        // =====================================================================
        private static void CaseFamilyIsComplete(List<JObject> impulse, List<string> failures, StringBuilder log)
        {
            var seenTiers = new Dictionary<int, string>();
            foreach (var p in impulse)
            {
                string sku = (string)p["sku"] ?? "<no sku>";
                var tierTok = p["tier"];
                if (tierTok == null) { failures.Add("[family] '" + sku + "' has no tier (the PackCatalog.FindByTier key)."); continue; }
                int tier = tierTok.Value<int>();
                string other;
                if (seenTiers.TryGetValue(tier, out other))
                    failures.Add("[family] tier " + tier + " is claimed by BOTH '" + other + "' and '" + sku +
                                 "'. tier is a UNIQUE lookup key (PackCatalog.FindByTier returns the FIRST match), " +
                                 "so one of these packs is unreachable by tier.");
                else seenTiers[tier] = sku;
            }

            foreach (string res in ResourceKeys)
            {
                int prevAmount = 0;
                double prevUsd = 0d;
                for (int s = 0; s < Sizes.Length; s++)
                {
                    string size = Sizes[s];
                    JObject found = null;
                    foreach (var p in impulse)
                    {
                        if (string.Equals((string)p["impulseResource"], res, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((string)p["impulseSize"], size, StringComparison.OrdinalIgnoreCase))
                        { found = p; break; }
                    }

                    if (found == null)
                    {
                        failures.Add("[family] no '" + size + "' pack for '" + res + "'. The owner ruled a full " +
                                     "small/medium/large ladder for EVERY harvestable type (2026-08-16: \"same with " +
                                     "all types for impulse small purchases\"); a missing rung means the shortfall " +
                                     "surface silently offers a bigger pack than the player needs, which is the " +
                                     "upsell WO-1037 section 1 forbids.");
                        continue;
                    }

                    // Null-safe on purpose: a missing contents/economy is already a [single-key] /
                    // [resources-only] failure with a far better message, and letting an NRE escape
                    // here would abort the WHOLE oracle into one "THREW" line — losing every other
                    // finding in the same run.
                    var contents = found["contents"] as JObject;
                    var econ = contents != null ? contents["economy"] as JObject : null;
                    int amount = econ != null && econ[res] != null ? econ[res].Value<int>() : 0;
                    double usd = found["pricing"] != null && found["pricing"]["usd"] != null
                               ? found["pricing"]["usd"].Value<double>() : 0d;
                    string sku = (string)found["sku"];

                    if (amount <= prevAmount)
                        failures.Add("[family] '" + sku + "' grants " + amount + " " + res + ", not more than the " +
                                     "rung below it (" + prevAmount + "). The size ladder must climb or the " +
                                     "smallest-sufficient resolver picks a pack that is not actually smaller.");
                    if (usd < prevUsd)
                        failures.Add("[family] '" + sku + "' is priced $" + usd.ToString("0.00") + ", CHEAPER than " +
                                     "the rung below it ($" + prevUsd.ToString("0.00") + ") while granting more.");

                    prevAmount = amount;
                    prevUsd = usd;
                }
            }
            log.AppendLine("  [family] " + ResourceKeys.Length + " resource families x " + Sizes.Length +
                           " sizes present, tiers unique, amounts and prices climb");
        }

        // =====================================================================
        //  CASE 6 [resolver] -- the REAL ShortfallPackOffer, exercised.
        //  This is the case that proves the DESIGN, not just the data.
        // =====================================================================
        private static void CaseResolverPicksSmallestSufficient(List<string> failures, StringBuilder log)
        {
            PackCatalog.Reload();

            // 6a -- an AFFORDABLE upgrade must resolve NOTHING. WO-1037 section 1 + section 5:
            // "it NEVER appears when the upgrade is affordable". A storefront that shows up when
            // nothing is wrong is the difference between a remedy and a billboard.
            foreach (int ask in new[] { 0, -1, -5000 })
            {
                var none = ShortfallPackOffer.Resolve("Wood", ask);
                if (none.HasOffer)
                    failures.Add("[resolver] Resolve(\"Wood\", " + ask + ") returned an offer ('" + none.Pack.Sku +
                                 "'). A non-positive shortfall means the player can already afford it, and the " +
                                 "offer must NEVER surface then (WO-1037 section 1/section 5).");
            }

            // 6b -- SMALLEST SUFFICIENT. Walk each family: an ask of exactly the small pack's
            // amount must resolve SMALL, one unit more must resolve MEDIUM, and so on. Any drift
            // toward a larger rung is the upsell-at-the-shortfall-moment the WO forbids.
            foreach (string res in ResourceKeys)
            {
                string label = char.ToUpperInvariant(res[0]) + res.Substring(1);
                var rungs = new List<PackDef>();
                foreach (string size in Sizes)
                {
                    PackDef found = null;
                    foreach (var p in PackCatalog.Packs)
                        if (p != null && p.Impulse &&
                            string.Equals(p.ImpulseResource, res, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(p.ImpulseSize, size, StringComparison.OrdinalIgnoreCase))
                        { found = p; break; }
                    if (found != null) rungs.Add(found);
                }
                if (rungs.Count == 0) continue;   // already reported by [family]

                for (int i = 0; i < rungs.Count; i++)
                {
                    int amount = rungs[i].ImpulseAmount;

                    // exactly the rung's amount -> that rung
                    var exact = ShortfallPackOffer.Resolve(label, amount);
                    if (!exact.HasOffer || exact.Pack.Sku != rungs[i].Sku)
                        failures.Add("[resolver] short " + amount + " " + res + " should resolve '" + rungs[i].Sku +
                                     "' (which grants exactly that) but resolved '" +
                                     (exact.HasOffer ? exact.Pack.Sku : "<nothing>") + "'.");
                    else if (!exact.CoversShortfall)
                        failures.Add("[resolver] '" + exact.Pack.Sku + "' grants " + amount + " against a " + amount +
                                     " shortfall but reports CoversShortfall=false - the copy would decline to say " +
                                     "it closes a gap it does close.");

                    // one below the rung -> still that rung (never the one above)
                    if (amount > 1)
                    {
                        var under = ShortfallPackOffer.Resolve(label, amount - 1);
                        if (!under.HasOffer || under.Pack.Sku != rungs[i].Sku)
                            failures.Add("[resolver] short " + (amount - 1) + " " + res + " should resolve the " +
                                         "SMALLEST SUFFICIENT pack '" + rungs[i].Sku + "' but resolved '" +
                                         (under.HasOffer ? under.Pack.Sku : "<nothing>") + "'. WO-1037 section 1: " +
                                         "one offer, the smallest sufficient one - upselling at the shortfall " +
                                         "moment is the turn from helpful to extractive.");
                    }
                }

                // 6c -- beyond the LARGEST rung: still offer the top pack, but it must NOT claim to
                // cover the gap. A pack that cannot close the gap it is offered against is worse
                // than no pack (WO-1037 section 3) -- so the surface has to be able to tell.
                int biggest = rungs[rungs.Count - 1].ImpulseAmount;
                var over = ShortfallPackOffer.Resolve(label, biggest + 1);
                if (over.HasOffer && over.CoversShortfall)
                    failures.Add("[resolver] short " + (biggest + 1) + " " + res + " resolved '" + over.Pack.Sku +
                                 "' with CoversShortfall=TRUE, but the largest pack only grants " + biggest +
                                 ". The surface would tell the player this pack closes their gap when it does not.");
            }

            // 6d -- a resource with no family (the cost panel prints "Magic") must resolve nothing
            // rather than falling through to some other family's pack.
            foreach (string bogus in new[] { "Magic", "Glimmer", "Coins", "", "Gold" })
            {
                var none = ShortfallPackOffer.Resolve(bogus, 500);
                if (none.HasOffer)
                    failures.Add("[resolver] Resolve(\"" + bogus + "\", 500) resolved '" + none.Pack.Sku +
                                 "'. Only wood/iron/food/crystals are purchasable (WO-947 section 12b); every " +
                                 "other cost label must resolve NO offer, not the nearest pack.");
            }

            log.AppendLine("  [resolver] smallest-sufficient honoured on all " + ResourceKeys.Length +
                           " families; affordable asks and non-harvestable labels resolve no offer");
        }

        // =====================================================================
        //  CASE 7 [no-grant-route] -- the WO-931 class. The shortfall surface must
        //  not be able to reach the entitlement grant. Asserted as a SOURCE fact
        //  because that is what the rule is: not "the call currently fails" but
        //  "the call does not exist".
        // =====================================================================
        private static void CaseSurfaceHasNoGrantRoute(List<string> failures, StringBuilder log)
        {
            var files = new Dictionary<string, string>
            {
                // WO-1282 - moved to the rail-neutral DeNelle.Commerce assembly. Same file, same
                // guardrails; only its assembly changed (DeNelle.Village had to stop referencing
                // DeNelle.Wallet so a Google Play artifact can exclude the Solana rail).
                { "ShortfallPackOffer.cs", Application.dataPath + "/_Modules/Commerce/ShortfallPackOffer.cs" },
                { "BuildingUpgradePanelMvvm.cs", Application.dataPath +
                  "/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs" },
                { "BuildingUpgradeVM.cs", Application.dataPath +
                  "/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs" },
            };

            // Each banned token is a DIFFERENT way the surface could start granting. Naming them
            // individually means the failure message says which door was opened.
            var banned = new Dictionary<string, string>
            {
                { "ApplyPackContents", "the entitlement grant itself - WO-931's defect was reaching this for ZERO payment" },
                { "GrantSpendablePurchased", "the paid-resource seam - the surface must not grant directly either" },
                { "GrantSpendableUncapped", "the DEV uncapped grant - a shortfall surface calling this is a free-money button" },
                { ".Purchase(", "PackStore.Purchase - the surface must not initiate a purchase while the rail is shut" },
            };

            foreach (var kv in files)
            {
                if (!File.Exists(kv.Value))
                {
                    failures.Add("[no-grant-route] " + kv.Key + " not found at " + kv.Value +
                                 " - the WO-1037 surface cannot be verified as grant-free.");
                    continue;
                }
                string src = File.ReadAllText(kv.Value);
                foreach (var b in banned)
                {
                    // Skip the comment lines that NAME the ban (they are the documentation of it).
                    if (!ContainsOutsideComments(src, b.Key)) continue;
                    failures.Add("[no-grant-route] " + kv.Key + " now references '" + b.Key + "' in CODE (" +
                                 b.Value + "). WO-1037 section 2: the shortfall surface may DISPLAY an offer and " +
                                 "nothing else; it must not reach a grant or a purchase. Opening this route needs " +
                                 "WO-931's three preconditions met and an owner decision, not an edit here.");
                }
            }
            log.AppendLine("  [no-grant-route] the shortfall VM + View + resolver reference no grant/purchase call");
        }

        // =====================================================================
        //  CASE 8 [not-on-shelf] -- WO-947 section 12c guardrail 4: the offer appears against a
        //  REAL SHORTFALL. It is a remedy, not a storefront. Twelve resource-for-cash rows on a
        //  browsable shelf IS the storefront the ruling is not, so PackStore must skip them.
        //  Asserted on the source because the alternative -- instantiating the store MonoBehaviour
        //  headlessly -- would prove far less for far more machinery.
        // =====================================================================
        private static void CaseNotOnTheShelf(List<string> failures, StringBuilder log)
        {
            string path = Application.dataPath + "/_Modules/Wallet/PackStore.cs";
            if (!File.Exists(path))
            {
                failures.Add("[not-on-shelf] PackStore.cs not found at " + path +
                             " - cannot verify the impulse SKUs stay off the browsable shelf.");
                return;
            }
            string src = File.ReadAllText(path);
            if (!ContainsOutsideComments(src, "pack.Impulse"))
                failures.Add("[not-on-shelf] PackStore.cs no longer skips 'pack.Impulse' in its card loop, so all " +
                             (ResourceKeys.Length * Sizes.Length) + " single-resource SKUs now render as browsable " +
                             "storefront rows. WO-947 section 12c guardrail 4: these appear against a REAL " +
                             "SHORTFALL and nowhere else - out of that context they are exactly the resource-for-cash " +
                             "storefront the ruling is not. Reach them through ShortfallPackOffer.");
            else
                log.AppendLine("  [not-on-shelf] PackStore's card loop still skips impulse SKUs (shortfall-only)");
        }

        // =====================================================================
        //  CASE 9 [anchor-no-buy] -- WO-1050. A row flagged anchorOnly must produce
        //  NO Buy control, on EITHER side of the RealmStorePurchase flag.
        //
        //  WHY IT IS ASSERTED ON THE SOURCE AND NOT ON A RENDER: the rule is not
        //  "the button currently happens to be absent", it is "the code path that
        //  builds a button is UNREACHABLE for an anchor row". That is a structural
        //  claim, and the structure is what is checked -- the anchorOnly branch must
        //  RETURN before the CTA builder can reach any BuildObsidianButton call. A
        //  headless render would prove today's data (no row carries the flag, so it
        //  would pass vacuously and prove nothing at all).
        // =====================================================================
        private static void CaseAnchorBuildsNoBuy(List<string> failures, StringBuilder log)
        {
            string path = Application.dataPath + "/_Modules/Wallet/PackStore.cs";
            if (!File.Exists(path))
            {
                failures.Add("[anchor-no-buy] PackStore.cs not found at " + path +
                             " - cannot verify that an anchor row builds no Buy control.");
                return;
            }
            string src = File.ReadAllText(path);

            if (!ContainsOutsideComments(src, "pack.AnchorOnly"))
            {
                failures.Add("[anchor-no-buy] PackStore.cs no longer tests 'pack.AnchorOnly' in CODE. A row " +
                             "authored anchorOnly:true would then render a TAPPABLE Buy over a rail that " +
                             "refuses - which is the WO-931 defect exactly. The flag must remove the button, " +
                             "not merely label it.");
                return;
            }

            // The branch must return before any button is built. Scope to the CTA BUILDER — the one
            // method that can construct a Buy control. Scanning the whole file would find the first
            // AnchorOnly read anywhere (the card's state WORD, which correctly returns a string) and
            // pass on an occurrence that was never the risk.
            // RE-POINTED 2026-08-22: the CTA builder was renamed BuildSpotlightCta -> BuildCommerce by
            // the UI-001 composition pass (it moved into the right-hand commerce column). The
            // oracle asserted a NAME, so a legitimate rename read as "the anchor guard is gone".
            // Fourth stale oracle ADDRESS found on 2026-08-22 - see also [suite-count],
            // contract.lamports, and ProveGeometryMoves. When a suite fails on code you believe
            // is correct, check what it is POINTED AT before changing the product.
            int cta = src.IndexOf("private void BuildCommerce", StringComparison.Ordinal);
            if (cta < 0)
            {
                failures.Add("[anchor-no-buy] PackStore.BuildCommerce (the CTA builder) not found. If the CTA builder was " +
                             "renamed, RE-POINT THIS ORACLE IN THE SAME CHANGE - otherwise the anchor rule " +
                             "silently stops being checked, which is worse than never having had it.");
                return;
            }
            int guard = IndexOfOutsideComments(src.Substring(cta), "pack.AnchorOnly");
            if (guard < 0)
            {
                failures.Add("[anchor-no-buy] PackStore.BuildCommerce does not test 'pack.AnchorOnly'. The CTA " +
                             "builder is the ONLY place a Buy control is constructed, so an anchor row would get one.");
                return;
            }
            guard += cta;
            int nextReturn = src.IndexOf("return;", guard, StringComparison.Ordinal);
            int nextButton = src.IndexOf("BuildObsidianButton", guard, StringComparison.Ordinal);
            if (nextReturn < 0 || (nextButton >= 0 && nextButton < nextReturn))
                failures.Add("[anchor-no-buy] in PackStore.cs the 'pack.AnchorOnly' branch reaches a " +
                             "BuildObsidianButton call before it returns. An anchor row must build NO Buy " +
                             "control AT ALL - showing a price with no button is the entire mechanism, and it " +
                             "is what lets the row anchor while shipping zero vapor.");
            else
                log.AppendLine("  [anchor-no-buy] the anchorOnly branch returns before any button is built");

            // Data side: an anchor row is still a normal, fully-priced row. If one is ever authored,
            // it must carry a real price on every rail or it renders an anchor of nothing.
            var packs = AllPacks(failures);
            if (packs == null) return;
            int anchors = 0;
            foreach (var p in packs)
            {
                var tok = p["anchorOnly"];
                if (tok == null || tok.Type != JTokenType.Boolean || !tok.Value<bool>()) continue;
                anchors++;
                string sku = (string)p["sku"] ?? "<no sku>";
                var pricing = p["pricing"] as JObject;
                double usd = pricing != null && pricing["usd"] != null ? pricing["usd"].Value<double>() : 0d;
                if (usd <= 0d)
                    failures.Add("[anchor-no-buy] '" + sku + "' is anchorOnly with no positive usd price. An " +
                                 "anchor works by SHOWING a price it will not take; with no price it is just a " +
                                 "locked card.");
            }
            log.AppendLine("  [anchor-no-buy] " + anchors + " anchorOnly row(s) authored " +
                           "(0 is expected today - WO-1121 made the top rungs buyable behind the wallet rule)");
        }

        // =====================================================================
        //  CASE 10 [band] -- every browsable row lands in one of the four bands,
        //  and lands there BY AUTHORING rather than by falling through the legacy
        //  storeSection fallback. The fallback exists so an un-migrated row is not
        //  lost; a shelf that RELIES on it is a shelf whose grouping is an accident.
        // =====================================================================
        private static void CaseBandIsKnown(List<string> failures, StringBuilder log)
        {
            PackCatalog.Reload();
            int checkedRows = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !pack.StoreVisible) continue;
                if (pack.Impulse && !pack.ShelfCurated) continue;   // shortfall-only; never on the shelf
                checkedRows++;

                if (!PackCatalog.HasAuthoredBand(pack))
                    failures.Add("[band] browsable row '" + pack.Sku + "' has no authored `band` (it reads '" +
                                 (pack.Band ?? "<absent>") + "') and is only on the shelf because BandOf fell " +
                                 "back to storeSection='" + (pack.StoreSection ?? "<absent>") + "'. The fallback " +
                                 "is a safety net for un-migrated rows, not the shelf's grouping rule - author " +
                                 "the band.");
            }

            // The Free band must never contain something that costs money. It is the one band whose
            // meaning is absolute, and its colour is reserved to it for the same reason.
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !pack.StoreVisible) continue;
                if (PackCatalog.BandOf(pack) != StoreBand.Free) continue;
                double usd = pack.Pricing != null ? pack.Pricing.Usd : 0d;
                if (usd > 0d)
                    failures.Add("[band] '" + pack.Sku + "' is in the FREE band at $" + usd.ToString("0.00") +
                                 ". Free means free. A priced row in that band makes the one promise on this " +
                                 "screen that carries no asterisk into a lie.");
            }
            log.AppendLine("  [band] " + checkedRows + " browsable row(s) carry an authored band; the free band is priceless");
        }

        // =====================================================================
        //  CASE 11 [compare-arithmetic] -- the comparison line is arithmetic over
        //  two REAL SKUs, or it is absent. No adjectives, no invented value index.
        // =====================================================================
        private static void CaseCompareIsArithmetic(List<string> failures, StringBuilder log)
        {
            PackCatalog.Reload();
            int lines = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || string.IsNullOrEmpty(pack.CompareTo)) continue;

                var other = PackCatalog.Find(pack.CompareTo);
                if (other == null)
                {
                    failures.Add("[compare-arithmetic] '" + pack.Sku + "' compares against '" + pack.CompareTo +
                                 "', which is not a SKU in this catalogue. The store would either draw nothing " +
                                 "(silently losing an authored line) or - worse, if someone 'fixes' it by " +
                                 "guessing - compare against the wrong pack.");
                    continue;
                }
                if (ReferenceEquals(other, pack))
                {
                    failures.Add("[compare-arithmetic] '" + pack.Sku + "' compares against itself. Every ratio " +
                                 "would be 1x, which is a sentence that says nothing while looking like a claim.");
                    continue;
                }

                bool sharesKey = false;
                foreach (string key in PackCatalog.LedgerEconomyKeys)
                    if (pack.EconomyAmount(key) > 0 && other.EconomyAmount(key) > 0) { sharesKey = true; break; }

                if (!sharesKey)
                    failures.Add("[compare-arithmetic] '" + pack.Sku + "' and '" + other.Sku + "' share NO economy " +
                                 "key, so there is no arithmetic to do and the line must be absent. Authoring a " +
                                 "compareTo that cannot resolve is how an adjective gets written into the gap " +
                                 "later - point it at a row that grants the same good, or remove it.");

                double a = pack.Pricing != null ? pack.Pricing.Usd : 0d;
                double b = other.Pricing != null ? other.Pricing.Usd : 0d;
                if (a <= 0d || b <= 0d)
                    failures.Add("[compare-arithmetic] '" + pack.Sku + "' vs '" + other.Sku + "': one of them has " +
                                 "no positive usd price, so the price ratio is undefined (or a divide by zero).");
                lines++;
            }
            log.AppendLine("  [compare-arithmetic] " + lines + " authored comparison(s), each resolvable to real ratios");
        }

        // =====================================================================
        //  CASE 12 [visible-grantable] -- the WO-1118 honest-shelf rule, now
        //  enforced per row on the WHOLE browsable shelf rather than only on the
        //  ladder. A visible card may only advertise lines that are grantable today.
        // =====================================================================
        private static void CaseVisibleIsGrantable(List<string> failures, StringBuilder log)
        {
            PackCatalog.Reload();
            int rows = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || !pack.StoreVisible) continue;
                if (pack.Impulse && !pack.ShelfCurated) continue;
                rows++;

                // 1. Convenience it advertises must have a redeemer IN THIS BUILD.
                var conv = pack.Contents != null ? pack.Contents.Convenience : null;
                if (conv != null)
                {
                    foreach (var item in conv)
                    {
                        if (item == null || item.Count <= 0 || string.IsNullOrEmpty(item.Kind)) continue;
                        if (!PackCatalog.IsRedeemableConvenience(item.Kind))
                            failures.Add("[visible-grantable] browsable row '" + pack.Sku + "' carries convenience '" +
                                         item.Kind + "', which NOTHING in this build spends (PackCatalog." +
                                         "IsRedeemableConvenience). It is granted into GearInventory and read by " +
                                         "nobody. WO-1118: a pack that cannot deliver a line it advertises must " +
                                         "not be sellable. Ship the redeemer, or strip the line.");
                    }
                }

                // 2. It must actually give SOMETHING the ledger can draw.
                bool grantsSomething = false;
                foreach (string key in PackCatalog.LedgerEconomyKeys)
                    if (pack.EconomyAmount(key) > 0) { grantsSomething = true; break; }
                if (!grantsSomething && (conv == null || conv.Count == 0) &&
                    (pack.Contents == null || pack.Contents.Cosmetics == null || pack.Contents.Cosmetics.Count == 0))
                    failures.Add("[visible-grantable] browsable row '" + pack.Sku + "' grants NOTHING at all. The " +
                                 "spotlight ledger would draw an empty box beside a real price.");

                // 3. ⛔ NO GLIMMER ON A PACK (owner ruling 2026-08-21). Its only sink is cosmetics and
                //    no CosmeticApplier runs, so it is a line on a paid card that buys nothing visible.
                if (pack.EconomyAmount("glimmer") > 0)
                    failures.Add("[visible-grantable] browsable row '" + pack.Sku + "' carries a glimmer amount. " +
                                 "The owner stripped glimmer from every pack on 2026-08-21 (\"its nothing real\"); " +
                                 "the ledger would draw a bar for a currency the player cannot spend on anything " +
                                 "that renders.");
            }
            log.AppendLine("  [visible-grantable] " + rows + " browsable row(s): every advertised line is grantable today");
        }

        // =====================================================================
        //  CASE 13 [band-greyscale] -- THE COLOURBLIND GATE.
        //
        //  The owner is RED/GREEN COLOURBLIND. The house rule is that colour is
        //  never the SOLE carrier of meaning, and a rule nothing checks is a rule
        //  that quietly stops being true. So: the four band lights must STEP APART
        //  in rec.709 greyscale, and every band must additionally carry a TEXT
        //  EYEBROW. Strip every hue from the shelf and the bands are still ordered
        //  and still named -- which is the acceptance criterion, not a preference.
        //
        //  ⛔ THIS IS ALSO WHY NOBODY ASKS HER TO APPROVE A PALETTE (memory
        //  `owner-colorblind-delegate-visual-creative`). The gate runs without her.
        // =====================================================================
        // =====================================================================
        //  CASE 13 [gap-reaches-shelf] -- WO-1339. THE "CLOSE THE GAP" BAND MUST
        //  ACTUALLY REACH A PLAYER, AND ITS PRICES MUST REPAINT.
        // ---------------------------------------------------------------------
        //  THE DEFECT THIS PINS, twice reported by the owner's EYES and by nothing
        //  else (WO-1335 "UNAVAILABLE", WO-1339 "back to not showing"):
        //
        //  In the landscape composition PackStore.Render() SKIPS StoreBand.Gap
        //  (`if (_utilityContent != null && band == StoreBand.Gap) continue;`) because
        //  those three rows live in the right-hand catch-up rail instead. That rail
        //  was built ONCE in EnsureBuilt and NOTHING rebuilt it. EnsureBuilt runs
        //  BEFORE the first quote list arrives, so each row was stamped with
        //  AmountLabel's no-quote WORDS ("Price unavailable", rendered "UNAVAILABLE")
        //  and kept them for the whole session, beside packs showing real prices.
        //
        //  ⛔ ASSERTED ON THE SOURCE, NOT ON A RENDER, and deliberately: the claim is
        //  STRUCTURAL -- "the band the shelf walk skips is rebuilt by the same
        //  Render() that repaints the bands it does not skip". A headless render
        //  proves only what one pass happened to draw with one set of quotes.
        //
        //  ⛔ AND IT IS PINNED AS A PAIR. The `continue` and the rebuild are two
        //  halves of one decision: whoever deletes the rail must delete the skip in
        //  the same change, or the Gap band is drawn NOWHERE. Checking either alone
        //  is how this came back.
        // =====================================================================
        private static void CaseGapBandReachesTheShelf(List<string> failures, StringBuilder log)
        {
            // ── (a) DATA: the band is not empty. ────────────────────────────────
            PackCatalog.Reload();
            int gapRows = 0, curated = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null) continue;
                if (pack.Impulse && pack.StoreVisible && pack.ShelfCurated)
                {
                    curated++;
                    if (PackCatalog.BandOf(pack) != StoreBand.Gap)
                        failures.Add("[gap-reaches-shelf] '" + pack.Sku + "' is storeVisible+shelfCurated (an " +
                                     "opted-in shelf row) but BandOf says " + PackCatalog.BandOf(pack) + ", not " +
                                     "Gap. The landscape rail draws the Gap band ONLY, so this row would be " +
                                     "curated onto a shelf and then rendered nowhere.");
                    if (!PackCatalog.IsOnBrowsableShelf(pack))
                        failures.Add("[gap-reaches-shelf] '" + pack.Sku + "' is storeVisible+shelfCurated but " +
                                     "IsOnBrowsableShelf refuses it. The curation flag and the shelf filter are " +
                                     "the same decision and cannot disagree.");
                }
                if (PackCatalog.IsOnBrowsableShelf(pack) && PackCatalog.BandOf(pack) == StoreBand.Gap) gapRows++;
            }
            if (gapRows == 0)
                failures.Add("[gap-reaches-shelf] the Gap band has ZERO browsable rows. 'Close the Gap' would " +
                             "render as nothing at all - the exact silent-empty-band state the owner had to " +
                             "report by eye twice. One impulse tier per resource is the owner's 2026-08-21 " +
                             "ruling; the shelf must carry it.");
            log.AppendLine("  [gap-reaches-shelf] " + gapRows + " browsable Gap row(s), " + curated +
                           " storeVisible+shelfCurated impulse row(s)");

            // ── (b) SOURCE: the rail is the band, and Render() repaints it. ────
            string path = Application.dataPath + "/_Modules/Wallet/PackStore.cs";
            if (!File.Exists(path))
            {
                failures.Add("[gap-reaches-shelf] PackStore.cs not found at " + path +
                             " - cannot verify the catch-up rail is repainted.");
                return;
            }
            string src = File.ReadAllText(path);

            bool skipsGapOnShelf = ContainsOutsideComments(src, "band == StoreBand.Gap) continue");
            bool hasRail = ContainsOutsideComments(src, "BuildLandscapeGapOffers()");
            bool rebuilds = ContainsOutsideComments(src, "RebuildLandscapeGapOffers()");

            if (skipsGapOnShelf && !rebuilds)
                failures.Add("[gap-reaches-shelf] PackStore.Render() SKIPS StoreBand.Gap on the shelf (the " +
                             "landscape rail owns it) but nothing calls RebuildLandscapeGapOffers(). The rail is " +
                             "built once in EnsureBuilt, BEFORE the first quote arrives, so every catch-up offer " +
                             "keeps AmountLabel's no-quote words ('Price unavailable' -> 'UNAVAILABLE') for the " +
                             "whole session while every other band repaints. That is WO-1335 and WO-1339, and it " +
                             "is invisible to every other gate.");
            if (rebuilds && !hasRail)
                failures.Add("[gap-reaches-shelf] RebuildLandscapeGapOffers() is called but BuildLandscapeGapOffers() " +
                             "is gone. RE-POINT THIS ORACLE IN THE SAME CHANGE - a renamed builder must never " +
                             "read as a passing rail.");

            // The rebuild must be reached from Render(), not from EnsureBuilt only: EnsureBuilt runs
            // once per surface, and a quote that lands afterwards would never be drawn.
            int render = src.IndexOf("public void Render()", StringComparison.Ordinal);
            if (render < 0)
                failures.Add("[gap-reaches-shelf] PackStore.Render() not found. If it was renamed, RE-POINT THIS " +
                             "ORACLE IN THE SAME CHANGE - otherwise the repaint rule silently stops being checked.");
            else if (skipsGapOnShelf && IndexOfOutsideComments(src.Substring(render), "RebuildLandscapeGapOffers()") < 0)
                failures.Add("[gap-reaches-shelf] RebuildLandscapeGapOffers() exists but is not reached from " +
                             "Render(). Only Render() runs again when RefreshQuotedPrices returns; a rebuild wired " +
                             "anywhere else repaints the rail exactly as never as before.");

            // ⛔ AND A QUOTE FAILURE MUST DEGRADE TO WORDS. An empty rail explains nothing and
            // cannot be seen by a gate; a heading plus a readable sentence can. Never colour alone -
            // the owner is red/green colourblind.
            int builder = src.IndexOf("private void BuildLandscapeGapOffers()", StringComparison.Ordinal);
            if (builder >= 0)
            {
                string body = src.Substring(builder, Math.Min(2600, src.Length - builder));
                if (IndexOfOutsideComments(body, "rows.Count == 0") >= 0 &&
                    IndexOfOutsideComments(body, "Unavailable right now") < 0)
                    failures.Add("[gap-reaches-shelf] BuildLandscapeGapOffers bails on an empty catalogue without " +
                                 "drawing anything. A band that silently vanishes is the failure mode of BOTH " +
                                 "reports - it must draw its heading and say, in WORDS, that the offers are " +
                                 "unavailable right now.");
            }
            log.AppendLine("  [gap-reaches-shelf] the shelf skip and the rail rebuild are paired, and " +
                           "Render() repaints the rail when a quote returns");
        }

        private static void CaseBandGreyscale(List<string> failures, StringBuilder log)
        {
            var bands = new[] { StoreBand.Free, StoreBand.Gap, StoreBand.Basket, StoreBand.Patronage };
            var lumas = new float[bands.Length];
            for (int i = 0; i < bands.Length; i++)
                lumas[i] = NightMarketPalette.Luma255(NightMarketPalette.For(bands[i]));

            for (int i = 0; i < bands.Length; i++)
            {
                for (int j = i + 1; j < bands.Length; j++)
                {
                    float step = Math.Abs(lumas[i] - lumas[j]);
                    if (step < NightMarketPalette.MinGreyscaleStep)
                        failures.Add("[band-greyscale] the " + bands[i] + " and " + bands[j] + " lights are only " +
                                     step.ToString("0.0") + "/255 apart in rec.709 greyscale (floor is " +
                                     NightMarketPalette.MinGreyscaleStep.ToString("0") + "). On a monochrome read " +
                                     "- which is how the owner reads this build - those two bands become the same " +
                                     "band. Re-pick a VALUE, not a hue.");
                }
            }

            // Every band also carries a word. A mark and a colour without a word is still colour-only
            // to anyone who cannot separate the two hues involved.
            foreach (var band in bands)
            {
                string eyebrow = EyebrowFor(band);
                if (string.IsNullOrEmpty(eyebrow) || eyebrow.StartsWith("[[missing:", StringComparison.Ordinal))
                    failures.Add("[band-greyscale] the " + band + " band has no eyebrow sentence (canon-strings " +
                                 "returned '" + eyebrow + "'). Without the word, band identity is carried by the " +
                                 "colour alone, which is the one thing this screen may never do.");
            }

            log.AppendLine("  [band-greyscale] four band lights step >= " +
                           NightMarketPalette.MinGreyscaleStep.ToString("0") + "/255 apart (" +
                           string.Join(", ", Array.ConvertAll(lumas, v => v.ToString("0"))) +
                           ") and every band carries a word");
        }

        private static string EyebrowFor(StoreBand band)
        {
            switch (band)
            {
                case StoreBand.Free:      return StoreStrings.Get(StoreStrings.KeyBandFree);
                case StoreBand.Gap:       return StoreStrings.Get(StoreStrings.KeyBandGap);
                case StoreBand.Patronage: return StoreStrings.Get(StoreStrings.KeyBandPatronage);
                default:                  return StoreStrings.Get(StoreStrings.KeyBandBasket);
            }
        }

        /// <summary>Every pack object in packs.json, raw. Null (with a failure logged) when unreadable.</summary>
        private static List<JObject> AllPacks(List<string> failures)
        {
            string json = DeNelle.Core.CanonicalJson.Read(PacksRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add("[night-market] " + PacksRelPath + " unreadable - no row is verifiable.");
                return null;
            }
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception ex)
            {
                failures.Add("[night-market] packs.json failed to parse: " + ex.Message);
                return null;
            }
            var arr = root["packs"] as JArray;
            if (arr == null) { failures.Add("[night-market] packs.json holds no 'packs' array"); return null; }
            var list = new List<JObject>();
            foreach (var el in arr) { var o = el as JObject; if (o != null) list.Add(o); }
            return list;
        }

        /// <summary>First index of <paramref name="needle"/> on a non-comment line, or -1.</summary>
        private static int IndexOfOutsideComments(string src, string needle)
        {
            int pos = 0;
            while (true)
            {
                int idx = src.IndexOf(needle, pos, StringComparison.Ordinal);
                if (idx < 0) return -1;
                int lineStart = src.LastIndexOf('\n', Math.Max(0, idx - 1)) + 1;
                string before = src.Substring(lineStart, idx - lineStart);
                if (before.IndexOf("//", StringComparison.Ordinal) < 0 &&
                    !before.TrimStart().StartsWith("*", StringComparison.Ordinal))
                    return idx;
                pos = idx + needle.Length;
            }
        }

        /// <summary>
        /// True when <paramref name="needle"/> appears on a line that is not a <c>//</c> comment.
        /// The bans above are DOCUMENTED in comments in exactly these files, so a naive Contains
        /// would fail the gate on its own explanation of why it passes.
        /// </summary>
        private static bool ContainsOutsideComments(string src, string needle)
        {
            var lines = src.Split('\n');
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd('\r');
                int idx = line.IndexOf(needle, StringComparison.Ordinal);
                if (idx < 0) continue;
                string before = line.Substring(0, idx);
                int comment = before.IndexOf("//", StringComparison.Ordinal);
                int star = before.IndexOf("///", StringComparison.Ordinal);
                if (comment >= 0 || star >= 0) continue;      // inside a trailing/whole-line comment
                if (line.TrimStart().StartsWith("*", StringComparison.Ordinal)) continue;  // block-comment body
                return true;
            }
            return false;
        }
    }
}
