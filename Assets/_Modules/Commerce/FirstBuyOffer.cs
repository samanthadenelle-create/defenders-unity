// =============================================================================
// FirstBuyOffer — WO-1801: which pack is the FIRST BUY, and does it close THIS gap?
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Commerce   Namespace: DeNelle.Commerce   (STATIC, read-only)
//
// Owner, 2026-09-16, verbatim: "yeah cause i just want a first sale you know".
// Measured the same day (production analytics_events, 30 days): store_opened 59 by
// 3 ids; pack_tapped 54 by 2 ids, last on 09-10; checkout_started 9; purchase_completed
// 3 by ONE id (the owner's own, Aug 25) — against ~25 distinct players a day. So the
// funnel does not leak at the shelf: almost nobody OPENS the store at all.
//
// ⛔ ONE JOB: name the first-buy pack from the DATA, and answer whether its grant
//    covers a specific resource gap. It resolves a PackDef and returns it. It never
//    grants, never charges, never routes, never opens a panel — the same boundary
//    ShortfallPackOffer draws, and for the same WO-931 reason (a stub surface with a
//    tappable Buy over a free-granting wallet is what shipped once already).
//
// ⛔ WHY IT IS NOT ShortfallPackOffer. That resolver is the WO-1037 impulse ladder and
//    it REJECTS, by design, any pack with more than one economy key or any convenience
//    item (ShortfallPackOffer.IsSingleKeyResourceOnly — WO-947 §12c guardrails 1 + 3).
//    The first-buy pack is deliberately the opposite shape: a multi-lane BASKET plus a
//    temporary-builder charge, legal because the PURCHASE boundary is not the COST
//    boundary (packs.json:15, WO-947 §12 amendment). Widening that resolver to admit it
//    would re-open the cost baskets through the back door, so this is a SEPARATE
//    resolver with its own, narrower promise. Neither file changes the other.
//
// ⛔ AND THE SKU IS NOT A CONSTANT HERE. It is resolved from packs.json by the authored
//    merchandising flags (storeVisible + storeBadge). A hardcoded "builders-hour" would
//    be a second copy of a decision the data already makes — the duplicated-state
//    failure CLAUDE.md §2/§5/§8/§16 each record a scar from — and it would go stale the
//    first time the owner moves the badge.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Wallet;   // PackDef / PackCatalog declare namespace DeNelle.Wallet inside this assembly (PackCatalog.cs:46). Gate CS0246 fix, lead 2026-09-16.

namespace DeNelle.Commerce
{
    /// <summary>
    /// The resolved answer to "is there a first-buy pack that closes this gap?". A struct with
    /// <see cref="HasOffer"/> false is the normal, expected outcome — callers render nothing.
    /// </summary>
    public readonly struct FirstBuyOfferResult
    {
        /// <summary>The pack to name, or null when there is nothing honest to offer.</summary>
        public readonly PackDef Pack;
        /// <summary>The worst-short resource, spelled as the caller spells it ("Iron"); may be null.</summary>
        public readonly string WorstLabel;
        /// <summary>How many units of <see cref="WorstLabel"/> the player is short.</summary>
        public readonly int WorstMissing;

        public FirstBuyOfferResult(PackDef pack, string worstLabel, int worstMissing)
        {
            Pack = pack; WorstLabel = worstLabel; WorstMissing = worstMissing;
        }

        /// <summary>True when there is a pack to name.</summary>
        public bool HasOffer => Pack != null;
        /// <summary>The pack SKU, or empty.</summary>
        public string Sku => Pack != null ? Pack.Sku : string.Empty;
        /// <summary>The AUTHORED USD reference ("$1.99"). Never a discounted figure — see the
        /// class remark on <see cref="FirstBuyOffer"/>: the client cannot know a discount before
        /// the till issues the quote, so it never prints one.</summary>
        public string PriceLabel => Pack != null ? Pack.UsdReference : string.Empty;
    }

    /// <summary>
    /// Resolves the first-buy pack and tests whether its grant covers a gap. Read-only over
    /// <see cref="PackCatalog"/>.
    ///
    /// <para>⛔ NO DISCOUNT IS EVER QUOTED FROM HERE, and that is a correctness rule, not caution.
    /// The 2000 bps shortfall discount is issued SERVER-SIDE at the till, once per wallet per 7
    /// days (api/purchases/quote.js, reason hint <c>repair_shortfall</c>), and the public LIST
    /// quote deliberately excludes it (quote.js:308 — "the till adds the shortfall if this player
    /// is owed one"). A client that printed "20% off today" before the quote existed would be
    /// stating something it cannot prove, and would be WRONG for any player who already spent the
    /// window. The door prints the authored USD; the confirm line renders the server's own
    /// discountLabel once the quote comes back.</para>
    /// </summary>
    public static class FirstBuyOffer
    {
        private const string TraceSystem = "Store";

        /// <summary>
        /// The authored merchandising badge that marks the first-buy rung (packs.json
        /// <c>storeBadge</c>). The BADGE is the authority, not a SKU list.
        /// </summary>
        public const string FirstBuyBadge = "FIRST BUY";

        /// <summary>
        /// The first-buy pack, or null. A shelf row (<c>storeVisible</c>) carrying the
        /// <see cref="FirstBuyBadge"/> badge and a real price; the CHEAPEST such row wins so a
        /// second badged row can never silently promote a dearer pack to "the first buy".
        /// </summary>
        public static PackDef Resolve()
        {
            PackDef best = null;
            Guard.Try(TraceSystem, "resolve the first-buy pack", () =>
            {
                var packs = PackCatalog.Packs;
                if (packs == null) return;
                for (int i = 0; i < packs.Count; i++)
                {
                    var p = packs[i];
                    if (p == null || !p.StoreVisible) continue;
                    if (p.Pricing == null || p.Pricing.Usd <= 0d) continue;   // never a promo row
                    if (!string.Equals(p.StoreBadge, FirstBuyBadge, StringComparison.OrdinalIgnoreCase)) continue;
                    if (best == null || p.Pricing.Usd < best.Pricing.Usd) best = p;
                }
            });

            if (best == null)
            {
                FlowTrace.Once(TraceSystem, "first-buy-unresolved",
                    "FirstBuyOffer.Resolve: no storeVisible pack carries the '" + FirstBuyBadge +
                    "' badge with a price > 0 in packs.json - no first-buy door can be offered. " +
                    "Expected exactly one (the $1.99 micro). Check the storeBadge field.");
            }
            return best;
        }

        /// <summary>
        /// Does <paramref name="pack"/>'s economy grant cover EVERY missing lane? The all-lanes
        /// rule is the honesty rule: a door that closes three of four gaps sends the player to a
        /// till and leaves them still blocked, which is worse than no door
        /// (<c>ShortfallOffer.CoversShortfall</c> exists for the same reason).
        /// </summary>
        public static bool Covers(PackDef pack, int missingWood, int missingIron,
                                  int missingStone, int missingCrystals)
        {
            var e = pack != null && pack.Contents != null ? pack.Contents.Economy : null;
            if (e == null) return false;
            if (missingWood <= 0 && missingIron <= 0 && missingStone <= 0 && missingCrystals <= 0)
                return false;                                   // not a real shortfall
            return e.Wood >= missingWood && e.Iron >= missingIron
                && e.Stone >= missingStone && e.Crystals >= missingCrystals;
        }

        /// <summary>
        /// TRUE when this save already carries a PAID pack entitlement — the "they have bought
        /// something" test, so the first-buy door is never shown to a player who has already had
        /// their first sale.
        ///
        /// <para>⛔ PROMO ROWS DO NOT COUNT. <c>welcome-500</c>/<c>welcome-100</c> are redeemable
        /// codes whose sku lands in OwnedItemIds exactly like a purchase; counting one would hide
        /// the door from a player who has never paid anything. Only rows with
        /// <c>pricing.usd &gt; 0</c> are probed.</para>
        ///
        /// <para>⛔ FAIL CLOSED ON "CANNOT TELL". <see cref="PackGrantBridge.TryIsOwned"/>
        /// distinguishes not-owned from unknown; an unknown answer (no local entitlement writer -
        /// the Google Play artifact, which compiles DeNelle.Wallet out) returns TRUE here, so the
        /// door stays shut rather than being offered to someone who may already own it.</para>
        /// </summary>
        public static bool HasPaidEntitlement()
        {
            bool result = true;   // the fail-closed default; only a complete probe can clear it
            Guard.Try(TraceSystem, "probe for an existing paid pack entitlement", () =>
            {
                var packs = PackCatalog.Packs;
                if (packs == null) return;
                var probed = new List<string>();
                bool any = false;
                for (int i = 0; i < packs.Count; i++)
                {
                    var p = packs[i];
                    if (p == null || string.IsNullOrEmpty(p.Sku)) continue;
                    if (p.Pricing == null || p.Pricing.Usd <= 0d) continue;
                    bool owned;
                    if (!PackGrantBridge.TryIsOwned(p.Sku, out owned))
                    {
                        FlowTrace.Once(TraceSystem, "first-buy-ownership-unknown",
                            "FirstBuyOffer.HasPaidEntitlement: ownership of '" + p.Sku + "' is UNKNOWN " +
                            "(no local entitlement writer registered - expected on a Google Play artifact). " +
                            "FAILING CLOSED: no first-buy door is offered on this build.");
                        return;                                  // result stays true
                    }
                    any = true;
                    if (owned) { probed.Add(p.Sku); }
                }
                if (!any) return;                                 // nothing probeable - stay closed
                result = probed.Count > 0;
                if (result)
                    FlowTrace.Step(TraceSystem, "FirstBuyOffer: this save already owns paid pack(s) [" +
                        string.Join(",", probed.ToArray()) + "] - the first-buy door stays shut.");
            });
            return result;
        }
    }
}
