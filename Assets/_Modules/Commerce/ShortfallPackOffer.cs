// =============================================================================
// ShortfallPackOffer — the WO-1037 shortfall → impulse-pack resolver.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// ONE job: given "the player is N short of <resource>", name the SMALLEST
// sufficient single-resource impulse pack — or nothing at all. It reads
// PackCatalog (packs.json), and it does not grant, charge, or route anything.
//
// ⛔ THIS TYPE NEVER TOUCHES THE MONEY PATH. It resolves a PackDef and returns
//    it. ApplyPackContents is unreachable from here — deliberately, per WO-1037
//    §2 and the WO-931 defect it exists to prevent (a stub surface that shipped
//    with a tappable Buy button routed at a free-granting stub wallet). The three
//    payment refusals (FeatureFlags.RealmStorePurchase off; WalletService.Pay /
//    PayFlat refusing unconditionally; SolanaWalletProvider blocking Mainnet) are
//    upstream of any purchase and are NOT this type's to soften.
//
// THE DESIGN RULES IT ENCODES (WO-1037 §1 + WO-947 §12c) — each is a line of code
// here, not a comment somewhere else:
//   * Only against a REAL shortfall — Resolve refuses a missing<=0 ask.
//   * The SMALLEST SUFFICIENT size. No upsell at the shortfall moment: the ladder
//     is walked small -> medium -> large and STOPS at the first pack that covers
//     the gap. The larger rungs are never surfaced when a smaller one suffices.
//   * Exactly ONE economy key. A pack whose contents carry a second non-zero key
//     is REJECTED at resolve time (FlowTrace.Fail + skipped), not merely expected
//     to be authored right — a multi-resource bundle re-mixes the WO-947 cost
//     baskets through the back door, so the runtime refuses to offer one even if
//     packs.json is edited to contain it.
//   * Resources only. A pack carrying cosmetics or convenience items is likewise
//     rejected: money buys the INPUT, never the outcome (WO-947 §12c guardrail 3).
//
// WHY THE GUARDRAILS ARE ENFORCED HERE AND NOT ONLY IN THE ORACLE: the oracle
// runs at gate time on the tree; this runs against whatever packs.json the player's
// build actually loaded. Both are wanted. The oracle catches the authoring mistake
// before it ships; this catches a data file that reached a device anyway.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Wallet
{
    /// <summary>
    /// The resolved answer to one shortfall. A struct with <see cref="HasOffer"/> false is the
    /// normal, expected outcome (affordable upgrade, unknown resource, no sized pack) — callers
    /// render nothing and the harvest path stands alone.
    /// </summary>
    public readonly struct ShortfallOffer
    {
        /// <summary>The pack to name, or null when there is nothing honest to offer.</summary>
        public readonly PackDef Pack;
        /// <summary>Normalised economy key the player is short of ("wood"/"iron"/"stone"/"crystals").</summary>
        public readonly string ResourceKey;
        /// <summary>Player-facing resource word as the panel already spells it ("Wood").</summary>
        public readonly string ResourceLabel;
        /// <summary>How many units short the player is.</summary>
        public readonly int Missing;
        /// <summary>True when <see cref="Pack"/>'s amount actually closes <see cref="Missing"/>.</summary>
        public readonly bool CoversShortfall;

        public ShortfallOffer(PackDef pack, string resourceKey, string resourceLabel, int missing, bool covers)
        {
            Pack = pack; ResourceKey = resourceKey; ResourceLabel = resourceLabel;
            Missing = missing; CoversShortfall = covers;
        }

        /// <summary>True when there is a pack to name.</summary>
        public bool HasOffer => Pack != null;

        /// <summary>Units of <see cref="ResourceKey"/> the offered pack grants (0 with no offer).</summary>
        public int Amount => Pack != null ? Pack.ImpulseAmount : 0;

        /// <summary>
        /// True only when the purchase rail is OPEN. It is <b>false on every build today</b>
        /// (<c>FeatureFlags.RealmStorePurchase</c> declares <c>defaultOn: false</c> and WO-931's three
        /// preconditions are unmet), so the surface renders the offer as INFORMATION with no tappable
        /// buy. ⚠ Do not invert this to "show the button and let Purchase() refuse" — WO-931 is exactly
        /// that mistake: a tappable Buy over a refusing rail is what shipped and what the owner flips
        /// this flag to undo, after a device wallet test.
        /// </summary>
        public bool Purchasable => HasOffer && DeNelle.Core.FeatureFlags.RealmStorePurchase;

        /// <summary>The USD reference price string ("$1.99"); empty with no offer.</summary>
        public string PriceLabel => Pack != null ? Pack.UsdReference : string.Empty;
    }

    /// <summary>
    /// Resolves the one relevant single-resource impulse pack for a shortfall. Read-only over
    /// PackCatalog; never grants, never charges, never routes to a purchase.
    /// </summary>
    public static class ShortfallPackOffer
    {
        /// <summary>The size ladder, SMALLEST FIRST. Resolve stops at the first sufficient rung.</summary>
        private static readonly string[] SizeOrder = { "small", "medium", "large" };

        /// <summary>
        /// The four harvestable economy keys money may buy (WO-1037 §3 option (b) / WO-947 §12b).
        /// Anything outside this set resolves NO offer — notably "Magic", which the upgrade panel
        /// prints as a cost label but which is not a harvestable resource and has no pack family.
        /// </summary>
        private static readonly Dictionary<string, string> LabelToKey =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "wood",     "wood"     },
                { "iron",     "iron"     },
                { "stone",    "stone"    },
                { "crystals", "crystals" },
                { "crystal",  "crystals" },   // singular spelling seen in some cost labels
            };

        /// <summary>
        /// The smallest impulse pack that covers <paramref name="missing"/> units of
        /// <paramref name="resourceLabel"/>. Returns an empty offer when the ask is not a genuine
        /// shortfall, the resource has no pack family, or no valid pack exists.
        /// </summary>
        /// <param name="resourceLabel">The panel's own resource word ("Wood", "Iron", "Stone", "Crystals").</param>
        /// <param name="missing">Units still needed. &lt;= 0 means "affordable" and resolves nothing.</param>
        public static ShortfallOffer Resolve(string resourceLabel, int missing)
        {
            var empty = new ShortfallOffer(null, null, resourceLabel, missing, false);

            // GUARDRAIL: only against a REAL shortfall (WO-1037 §1, WO-947 §12c.4). An affordable
            // upgrade must never see an offer — that is the line between a remedy and a storefront.
            if (missing <= 0) return empty;
            if (string.IsNullOrEmpty(resourceLabel)) return empty;

            string key;
            if (!LabelToKey.TryGetValue(resourceLabel.Trim(), out key))
            {
                // Not a failure — "Magic" and any future non-harvestable cost land here by design.
                FlowTrace.Once("Store", "shortfall-no-family:" + resourceLabel,
                    "ShortfallPackOffer: resource '" + resourceLabel +
                    "' has no impulse-pack family (only wood/iron/stone/crystals are purchasable, " +
                    "WO-947 §12b) - no offer surfaced. This is expected, not a defect.");
                return empty;
            }

            ShortfallOffer resolved = empty;
            Guard.Try("Store", "resolve shortfall pack for " + key + " x" + missing, () =>
            {
                PackDef best = null;         // the smallest sufficient rung
                PackDef largest = null;      // fallback when nothing covers the gap
                int largestAmount = 0;

                for (int i = 0; i < SizeOrder.Length; i++)
                {
                    var pack = FindValid(key, SizeOrder[i]);
                    if (pack == null) continue;

                    int amount = pack.ImpulseAmount;
                    if (amount <= 0) continue;

                    if (amount > largestAmount) { largest = pack; largestAmount = amount; }

                    // SMALLEST SUFFICIENT — stop here. Walking on to compare a bigger rung is
                    // literally the upsell WO-1037 §1 forbids at this moment.
                    if (best == null && amount >= missing) best = pack;
                }

                if (best != null)
                {
                    resolved = new ShortfallOffer(best, key, resourceLabel, missing, true);
                    FlowTrace.Step("Store", "ShortfallPackOffer: short " + missing + " " + key +
                        " -> smallest sufficient '" + best.Sku + "' (" + best.ImpulseAmount + " " + key +
                        ", " + best.UsdReference + "). Purchase rail " +
                        (DeNelle.Core.FeatureFlags.RealmStorePurchase ? "OPEN" : "CLOSED (display only)") + ".");
                    return;
                }

                if (largest != null)
                {
                    // The gap is bigger than the largest rung. Offering the top pack is not an upsell
                    // (there is nothing above it to climb to) but it does NOT close the gap, and
                    // CoversShortfall=false is how the surface knows never to claim that it does.
                    resolved = new ShortfallOffer(largest, key, resourceLabel, missing, false);
                    FlowTrace.Step("Store", "ShortfallPackOffer: short " + missing + " " + key +
                        " EXCEEDS the largest pack ('" + largest.Sku + "', " + largestAmount + ") - " +
                        "offering it with CoversShortfall=false so no copy claims it closes the gap.");
                    return;
                }

                FlowTrace.Warn("Store", "ShortfallPackOffer: no valid impulse pack for '" + key +
                    "' in packs.json - no offer surfaced. Expected 3 rungs (small/medium/large); " +
                    "check the [impulse-pack] oracle.");
            });

            return resolved;
        }

        /// <summary>
        /// Finds the pack for one (resource, size) rung and REJECTS it unless it still honours the
        /// WO-947 §12c guardrails. Every rejection is a FlowTrace.Fail: the data has drifted from a
        /// binding ruling, and a silent skip would let the surface quietly offer the wrong thing.
        /// </summary>
        private static PackDef FindValid(string key, string size)
        {
            var packs = PackCatalog.Packs;
            if (packs == null) return null;

            for (int i = 0; i < packs.Count; i++)
            {
                var p = packs[i];
                if (p == null || !p.Impulse) continue;
                if (!string.Equals(p.ImpulseResource, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(p.ImpulseSize, size, StringComparison.OrdinalIgnoreCase)) continue;

                if (!IsSingleKeyResourceOnly(p, key)) return null;
                return p;
            }
            return null;
        }

        /// <summary>
        /// WO-947 §12c guardrails 1 + 3, enforced on the live data: exactly ONE non-zero economy key,
        /// and it is the tagged one; no cosmetics; no convenience items. Returns false + Fails loudly
        /// on any breach so a mis-authored SKU is never OFFERED, only logged.
        /// </summary>
        private static bool IsSingleKeyResourceOnly(PackDef p, string key)
        {
            var c = p.Contents;
            var e = c != null ? c.Economy : null;
            if (e == null)
            {
                FlowTrace.Fail("Store", "ShortfallPackOffer: impulse pack '" + p.Sku +
                    "' has NO economy bag - it can grant nothing. REJECTED (not offered).");
                return false;
            }

            var nonZero = new List<string>();
            if (e.Wood     > 0) nonZero.Add("wood");
            if (e.Iron     > 0) nonZero.Add("iron");
            if (e.Stone     > 0) nonZero.Add("stone");
            if (e.Crystals > 0) nonZero.Add("crystals");
            if (e.Coins    > 0) nonZero.Add("coins");

            if (nonZero.Count != 1 || !string.Equals(nonZero[0], key, StringComparison.OrdinalIgnoreCase))
            {
                FlowTrace.Fail("Store", "ShortfallPackOffer: impulse pack '" + p.Sku + "' grants [" +
                    string.Join(",", nonZero.ToArray()) + "] but must grant EXACTLY ONE key ('" + key +
                    "'). WO-947 §12c guardrail 1: a multi-resource impulse bundle re-mixes the cost " +
                    "baskets through the back door and is FORBIDDEN. REJECTED (not offered).");
                return false;
            }

            if (c.Cosmetics != null && c.Cosmetics.Count > 0)
            {
                FlowTrace.Fail("Store", "ShortfallPackOffer: impulse pack '" + p.Sku + "' carries " +
                    c.Cosmetics.Count + " cosmetic(s). WO-947 §12c guardrail 3: impulse packs grant " +
                    "RESOURCES ONLY. REJECTED (not offered).");
                return false;
            }

            if (c.Convenience != null && c.Convenience.Count > 0)
            {
                FlowTrace.Fail("Store", "ShortfallPackOffer: impulse pack '" + p.Sku + "' carries " +
                    c.Convenience.Count + " convenience item(s) - money would be buying TIME/OUTCOME, " +
                    "not the input. WO-947 §12c guardrail 3 + §12d (selling outcomes is explicitly " +
                    "NOT ruled). REJECTED (not offered).");
                return false;
            }

            return true;
        }
    }
}
