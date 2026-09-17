// =============================================================================
// PackCatalog — typed model + loader for the canonical packs.json (spec Part 4)
// -----------------------------------------------------------------------------
// C# port of the React PackDef interface (src/content/packs.ts — monetization-
// v2-spec §8.4). The five packs are CONTENT, not code: PackCatalog reads
// Assets/StreamingAssets/Data/Canonical/packs.json at load time and hydrates
// typed PackDef records. Mirrors the Theme.cs pattern (canonical JSON under
// StreamingAssets, read via Application.streamingAssetsPath, parsed by
// Newtonsoft.Json) — see unity-decisions.md, 2026-05-18.
//
// The pack store (PackStore.cs) reads PackDefs from here; it never types pack
// names, prices or contents inline (spec Part 4 — canon strings flow from JSON).
// =============================================================================
//
// =============================================================================
//  WO-1282 - ASSEMBLY: DeNelle.Commerce.   NAMESPACE: DeNelle.Wallet (DELIBERATE).
// -----------------------------------------------------------------------------
// This file MOVED out of DeNelle.Wallet so that DeNelle.Village can stop referencing
// the Solana rail and a Google Play artifact can exclude DeNelle.Wallet whole
// (GooglePlayPackagingGate.AssertSourceIsolation). Commerce is rail-neutral by
// construction: it references DeNelle.Core and NOTHING else, and it may NEVER
// reference DeNelle.Wallet or DeNelle.Web3. Wallet references Commerce, one way.
//
// ⛔ THE NAMESPACE STAYED `DeNelle.Wallet` ON PURPOSE - IT IS A LIVE RUNTIME CONTRACT,
//    NOT A LEFTOVER. Assets/_Modules/Core/Promo/PromoCodeService.cs resolves
//    "DeNelle.Wallet.PackContents" as a STRING LITERAL by reflection, walking every
//    loaded assembly (TryApplyInlinePack). Renaming the namespace compiles perfectly
//    clean and turns promo-code redemption into a silent runtime no-op - the exact
//    compiler-invisible landmine WO-1282 was written to avoid. A namespace that a
//    string resolves at runtime is an interface, and interfaces do not get renamed
//    for tidiness. (Same reason PackDef.LegacySkus can never be pruned and PackEconomy
//    keeps the field name `Stone` for the authored key `stone`.)
//
//    C# namespaces and assemblies are orthogonal; the ASSEMBLY is what the Play build
//    excludes, and the assembly is DeNelle.Commerce. Nothing about the exclusion needs
//    the namespace to change.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Wallet
{
    /// <summary>
    /// The four shelf bands of The Night Market (WO-1050), in their FIXED render order.
    /// <para>⛔ THE ORDER IS NOT A PREFERENCE AND IS NOT SORTED AT RENDER TIME — it is the enum
    /// order, and the shelf walks the enum. <see cref="Free"/> is first because nothing is asked for
    /// before something is given; <see cref="Patronage"/> is last so it is the read on the way out.
    /// Renumbering these values re-orders the store.</para>
    /// </summary>
    public enum StoreBand
    {
        /// <summary>Costs nothing, ever. Never rendered in a colour that also appears on a priced row.</summary>
        Free = 0,
        /// <summary>One resource, nothing else — the curated single-resource impulse rows.</summary>
        Gap = 1,
        /// <summary>Baskets: everything at once.</summary>
        Basket = 2,
        /// <summary>Status, never power.</summary>
        Patronage = 3,
    }

    /// <summary>Per-currency price of a pack — mirrors PackDef.pricing (§8.4).</summary>
    [Serializable]
    public sealed class PackPricing
    {
        /// <summary>The Stripe / USD reference price. Display only — Stripe is web-only.</summary>
        [JsonProperty("usd")] public double Usd;
        /// <summary>USDC wallet-rail amount.</summary>
        [JsonProperty("usdc")] public double Usdc;
        /// <summary>Native SOL wallet-rail amount.</summary>
        [JsonProperty("sol")] public double Sol;
        /// <summary>SKR (Solana Seeker token) wallet-rail amount.</summary>
        [JsonProperty("skr")] public double Skr;

        /// <summary>
        /// The FLAT, market-independent SKR amount for this pack — the owner's 100 / 200 / 300 / 500 /
        /// 9999 ladder (WO-1815, 2026-09-16: <i>"change the store to SKR only and set to flat amounts"</i>).
        /// 0 on a pack that is not sold (the two promo rows).
        ///
        /// <para>⛔ THIS IS AN AUTHORING FIELD, NOT A CLIENT PRICE, AND THE DIFFERENCE IS THE WHOLE
        /// REASON <see cref="Skr"/> ABOVE IS DEAD DATA. The server is the price authority
        /// (PurchaseQuoteService.cs's header records why: a client-resolved price and a server-checked
        /// one cannot both be right, and /verify runs AFTER the transfer settles, so the failure is
        /// paid-and-not-granted). This field reaches the server as authored, through the VERBATIM
        /// <c>tools/gen-sku-catalog.mjs</c> copy into <c>api/_lib/sku-catalog.generated.json</c>, and the
        /// server prices from it — one authoring surface, no second table.</para>
        ///
        /// <para>The client may use it for exactly three things, none of which is "the price": the
        /// STRUCK anchor on a sale card (legitimate because a flat amount is a CONSTANT, not a
        /// market-derived figure, and only when the served figure is strictly lower), the ladder oracle
        /// in <c>StoreSkrFlatLadderRegression</c>, and the headless capture's stub row.</para>
        /// </summary>
        [JsonProperty("skrFlat")] public double SkrFlat;
    }

    /// <summary>One in-game currency top-up — mirrors PackDef.contents.economy (§5.2).</summary>
    [Serializable]
    public sealed class PackEconomy
    {
        /// <summary>Crystals — the primary build currency.</summary>
        [JsonProperty("crystals")] public int Crystals;
        /// <summary>Stone. The C# field retains its historic name because this is the reused
        /// economy/save slot; the authored and player-facing JSON key is stone.</summary>
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("stone")] public int Stone;
        [JsonProperty("food")] private int LegacyFood { set { if (Stone == 0) Stone = value; } }
        /// <summary>Coins (Gold).</summary>
        [JsonProperty("coins")] public int Coins;
        /// <summary>Wood - build resource (additive; absent in older packs.json rows = 0, no migration break). Granted via EconomyService.GrantSpendable (ECON-01).</summary>
        [JsonProperty("wood")] public int Wood;
        /// <summary>Iron - build resource (additive; absent = 0). Granted via EconomyService.GrantSpendable (ECON-01).</summary>
        [JsonProperty("iron")] public int Iron;
    }

    /// <summary>
    /// A timed multiplier attached to a convenience item (WO economy_store_packs §2c).
    /// Null on every non-timed kind, and absent from every pack authored before it existed —
    /// additive, so nothing migrates. Wall-clock based by design (the consumer stores an
    /// <c>endsAtUtc</c>), so a boost keeps ticking while the player is offline.
    /// </summary>
    [Serializable]
    public sealed class BoostSpec
    {
        /// <summary>Rate multiplier, e.g. 2.0 for 2x.</summary>
        [JsonProperty("multiplier")] public double Multiplier;
        /// <summary>How long the boost runs, in hours.</summary>
        [JsonProperty("durationHours")] public double DurationHours;
        /// <summary>Which channel it applies to: "all" | "wood" | "iron" | "food" | "crystals".</summary>
        [JsonProperty("appliesTo")] public string AppliesTo;
        /// <summary>Re-buy behaviour while one is already running: "extend" | "refresh" | "reject" | "queue".</summary>
        [JsonProperty("stack")] public string Stack;
    }

    /// <summary>
    /// One convenience-power item — TIME-SAVING only, never combat-power
    /// (the bent covenant, monetization-v2-spec §5.3).
    /// </summary>
    [Serializable]
    public sealed class ConvenienceItemDef
    {
        /// <summary>The item kind: instant-build / instant-repair / harvest-auto-collect / xp-weekend.</summary>
        [JsonProperty("kind")] public string Kind;
        /// <summary>How many of this item the pack grants.</summary>
        [JsonProperty("count")] public int Count;
        /// <summary>Player-facing description of the item.</summary>
        [JsonProperty("description")] public string Description;
        /// <summary>Timed-multiplier spec for boost kinds; null for simple count-only kinds.</summary>
        [JsonProperty("boost")] public BoostSpec Boost;
    }

    /// <summary>The contents bag of a pack — cosmetics + economy + convenience (§5).</summary>
    [Serializable]
    public sealed class PackContents
    {
        /// <summary>Cosmetic SKUs granted by the pack (1–5 depending on tier).</summary>
        [JsonProperty("cosmetics")] public List<string> Cosmetics = new List<string>();
        /// <summary>The in-game currency top-up.</summary>
        [JsonProperty("economy")] public PackEconomy Economy = new PackEconomy();
        /// <summary>The convenience-power items.</summary>
        [JsonProperty("convenience")] public List<ConvenienceItemDef> Convenience = new List<ConvenienceItemDef>();
    }

    /// <summary>
    /// One purchasable pack — the C# port of React's PackDef (monetization-v2-spec
    /// §8.4). Hydrated from packs.json; never constructed inline by game code.
    /// </summary>
    [Serializable]
    public sealed class PackDef
    {
        /// <summary>Stable SKU (e.g. "hearth-spark"). The entitlement key.</summary>
        [JsonProperty("sku")] public string Sku;
        /// <summary>Pricing tier 1–5 (Hearth Spark → Founder's Vow).</summary>
        [JsonProperty("tier")] public int Tier;
        /// <summary>Canon pack name — verbatim, never paraphrased.</summary>
        [JsonProperty("name")] public string Name;
        /// <summary>Narrative-bible-voice tagline shown on the pack card / detail page.</summary>
        [JsonProperty("tagline")] public string Tagline;
        /// <summary>The pricing-ladder theme description (§4 table).</summary>
        [JsonProperty("theme")] public string Theme;
        /// <summary>True for the launch-window-only Founder's Vow (§4).</summary>
        [JsonProperty("founderOnly")] public bool FounderOnly;
        /// <summary>Whether this SKU appears on the browsable Realm Store shelf.</summary>
        [JsonProperty("storeVisible")] public bool StoreVisible = true;

        /// <summary>
        /// Owner ruling 2026-08-21 ("Middle — one impulse tier per resource"): opts a SINGLE
        /// <see cref="Impulse"/> SKU per resource onto the browsable shelf. Every other impulse SKU
        /// stays shortfall-only. Absent = false, so the other nine rows need no edit.
        /// <para>This is DATA, not a SKU list in PackStore.cs, deliberately: which three tiers are
        /// curated is a merchandising decision that will be re-ruled, and a hardcoded list in the
        /// render loop is the thing that goes stale silently. The render loop asks the row.</para>
        /// </summary>
        [JsonProperty("shelfCurated")] public bool ShelfCurated;

        /// <summary>
        /// Retired SKU ids that still resolve to THIS pack (WO-1118 rename migration).
        /// <para>⚠ LOAD-BEARING. <c>sku</c> is a live save key: PackStoreVM.RecordOwned writes it
        /// into <c>GameState.OwnedItemIds</c> and IsOwned reads it back with an ORDINAL
        /// <c>List.Contains</c>. Renaming a SKU with money already spent against the old id
        /// therefore ORPHANS that entitlement — the player silently un-owns what they bought.
        /// Listing the old id here keeps it resolving, forever, with no save rewrite and no schema
        /// bump. NEVER delete an entry from this array; a retired id must stay redeemable for as
        /// long as any save can carry it.</para>
        /// </summary>
        [JsonProperty("legacySkus")] public List<string> LegacySkus = new List<string>();

        /// <summary>True when <paramref name="sku"/> is this pack's current id OR one of its retired ids.</summary>
        public bool MatchesSku(string sku)
        {
            if (string.IsNullOrEmpty(sku)) return false;
            if (string.Equals(Sku, sku, StringComparison.Ordinal)) return true;
            if (LegacySkus == null) return false;
            for (int i = 0; i < LegacySkus.Count; i++)
                if (string.Equals(LegacySkus[i], sku, StringComparison.Ordinal)) return true;
            return false;
        }
        /// <summary>Retention-oriented shelf section: featured / essentials / style / support.</summary>
        /// <remarks>
        /// ⛔ NOT DELETED BY WO-1050, deliberately. <see cref="Band"/> supersedes it as the GROUPING
        /// key, but this field is still the FALLBACK a row without a band resolves through
        /// (<see cref="PackCatalog.BandOf"/>), so deleting it would silently drop any un-migrated row
        /// into the wrong band. Two fields over one decision is duplicated state ONLY when both are
        /// authoritative; here one is authored and the other is a documented fallback.
        /// </remarks>
        [JsonProperty("storeSection")] public string StoreSection = "essentials";

        // ── WO-1050 "The Night Market" PRESENTATION fields (additive; absent = null/false) ──
        // ⚠ EVERY FIELD IN THIS BLOCK DECIDES WHERE OR HOW A PACK IS SHOWN, AND NONE OF THEM CAN
        // CHANGE WHAT IT GRANTS. That distinction is why ImpulsePackRegression's AllowedPackKeys
        // gate accepts them (they cannot be the "grant a finished upgrade" field it exists to catch)
        // and it is the line to hold if this block ever grows: a key that touches contents, pricing
        // or entitlement does NOT belong here.

        /// <summary>
        /// The shelf band this pack lives in: <c>free</c> | <c>gap</c> | <c>basket</c> |
        /// <c>patronage</c>. Replaces <see cref="StoreSection"/>'s mood names as the grouping key.
        /// Absent → resolved from <see cref="StoreSection"/> by <see cref="PackCatalog.BandOf"/>.
        /// </summary>
        [JsonProperty("band")] public string Band;

        /// <summary>
        /// Hex tint (<c>#RRGGBB</c>) for the card's gem. Authored in DATA so a merchandising change
        /// is a data edit, never a code edit. Absent → the band's own light is used.
        /// <para>⚠ DECORATION ONLY. The owner is red/green colourblind: band identity is carried by
        /// the 3 px mark, the text eyebrow and the step in greyscale value, NEVER by this tint. A
        /// card whose meaning depends on its orb colour is a defect.</para>
        /// </summary>
        [JsonProperty("orbTint")] public string OrbTint;

        /// <summary>
        /// The SKU this pack's spotlight comparison line is drawn against. Absent → no line.
        /// <para>The line is PURE ARITHMETIC over two real SKUs (a goods ratio and a price ratio) or
        /// it is not drawn at all — no adjectives, no invented "value index". If the named SKU does
        /// not exist, or the two rows share no economy key, the line is ABSENT.</para>
        /// </summary>
        [JsonProperty("compareTo")] public string CompareTo;

        /// <summary>
        /// A price ANCHOR: the card renders fully priced and <b>no Buy control is ever built</b>, on
        /// either side of the purchase flag. It cannot be bought, so it cannot disappoint.
        /// <para>⚠ NO ROW CARRIES THIS TODAY (WO-1050). The mechanism ships ahead of its data on
        /// purpose: WO-1121 is the SAME-DAY owner ruling that un-hid the $9.99/$19.99/$49.99 ladder
        /// and made those rows buyable behind <c>PurchaseGate</c>'s wallet rule, so flagging any of
        /// them <c>anchorOnly</c> now would walk that ruling back by an authoring edit. Setting this
        /// on a row is an OWNER call.</para>
        /// </summary>
        [JsonProperty("anchorOnly")] public bool AnchorOnly;

        /// <summary>
        /// How much of one economy key this pack grants. Read straight off the contents bag — the
        /// SAME bag <c>PackStoreVM.ApplyPackContents</c> pays out — so the ledger and the comparison
        /// line can never advertise a figure the grant seam does not deliver.
        /// </summary>
        public int EconomyAmount(string key)
        {
            var e = Contents != null ? Contents.Economy : null;
            if (e == null || string.IsNullOrEmpty(key)) return 0;
            switch (key.Trim().ToLowerInvariant())
            {
                case "wood":     return e.Wood;
                case "iron":     return e.Iron;
                case "crystals": return e.Crystals;
                case "stone":    return e.Stone;
                case "food":     return e.Stone;
                case "coins":    return e.Coins;
                default:         return 0;
            }
        }
        /// <summary>Short, honest card badge such as "BEST START" or "EXPEDITION".</summary>
        [JsonProperty("storeBadge")] public string StoreBadge;
        /// <summary>Per-currency pricing.</summary>
        [JsonProperty("pricing")] public PackPricing Pricing = new PackPricing();
        /// <summary>The contents bag.</summary>
        [JsonProperty("contents")] public PackContents Contents = new PackContents();
        /// <summary>The single cosmetic SKU exclusive to this pack (§5.1).</summary>
        [JsonProperty("packExclusiveCosmetic")] public string PackExclusiveCosmetic;

        // ── WO-1037 single-resource impulse family (additive; absent = false/null) ──
        // These three fields are the MACHINE-READABLE family tag ShortfallPackOffer resolves on.
        // They are deliberately data, not a name-prefix convention: matching on "impulse-" in the
        // SKU string would silently mis-classify the day someone renames a SKU, and the shortfall
        // surface would then offer a 4-key bundle against a one-resource gap. Older packs omit all
        // three (JSON absent -> false/null), so nothing migrates.

        /// <summary>True for a WO-1037 single-resource impulse SKU (exactly ONE economy key).</summary>
        [JsonProperty("impulse")] public bool Impulse;
        /// <summary>The ONE economy key this impulse pack grants: "wood" / "iron" / "stone" / "crystals".</summary>
        [JsonProperty("impulseResource")] public string ImpulseResource;
        /// <summary>"small" / "medium" / "large" — the size rung inside its resource family.</summary>
        [JsonProperty("impulseSize")] public string ImpulseSize;

        /// <summary>
        /// How much of <see cref="ImpulseResource"/> this pack grants (0 when it is not an impulse
        /// pack, or when the tagged resource key carries no amount). Read straight off the contents
        /// bag rather than from a second authored number, so the advertised figure and the granted
        /// figure cannot drift apart.
        /// </summary>
        public int ImpulseAmount
        {
            get
            {
                var e = Contents != null ? Contents.Economy : null;
                if (e == null || string.IsNullOrEmpty(ImpulseResource)) return 0;
                switch (ImpulseResource.Trim().ToLowerInvariant())
                {
                    case "wood":     return e.Wood;
                    case "iron":     return e.Iron;
                    case "stone":    return e.Stone;
                    case "food":     return e.Stone;
                    case "crystals": return e.Crystals;
                    default:         return 0;
                }
            }
        }

        /// <summary>The USD reference price string, e.g. <c>"$4.99"</c>.</summary>
        /// <remarks>Rail-free on purpose: it reads the AUTHORED usd anchor and nothing else, so it
        /// survives in a build that carries no wallet at all. The rate-derived twin lives with the
        /// rail - see the pointer below.</remarks>
        public string UsdReference => Pricing != null ? $"${Pricing.Usd:0.00}" : "$0.00";

        // =====================================================================
        //  WO-1282 - THE RAIL-PRICED MEMBERS ARE NOT HERE ANY MORE, AND THAT IS THE POINT.
        // ---------------------------------------------------------------------
        //  AmountFor(CurrencyKind) / AmountLabel(CurrencyKind) / UsdApprox() moved VERBATIM to
        //  DeNelle.Wallet.SolanaPackPricing (Assets/_Modules/Wallet/SolanaPackPricing.cs) as
        //  EXTENSION METHODS on this type. They are still called as `pack.AmountFor(...)` from any
        //  file that is `namespace DeNelle.Wallet` - nothing about the call sites changed.
        //
        //  WHY: CurrencyKind IS the Solana rail (Sol/Usdc/Skr, WalletService.cs) and
        //  PurchaseQuoteService/PurchaseGate/MainnetCanaryCatalog are all rail-bound. A Google Play
        //  artifact excludes DeNelle.Wallet entirely (GooglePlayPackagingGate), so a PackDef that
        //  NAMES CurrencyKind cannot compile there. The DATA ships everywhere; the PRICE IN A
        //  TOKEN ships only with the rail that can charge it.
        //
        //  DO NOT re-add a CurrencyKind member to this file. The compiler will let you - Commerce
        //  does not reference Wallet, so it will simply fail to resolve the name and you will be
        //  tempted to "fix" it by adding the reference. Adding it re-breaks the Play artifact,
        //  silently, and the gate that catches it runs at BUILD time, not here.
        // =====================================================================
    }

    /// <summary>The parsed packs.json root.</summary>
    [Serializable]
    public sealed class PackCatalogData
    {
        [JsonProperty("version")] public int Version;
        /// <summary>The permanent UI disclaimer for wallet-rail purchases (§4.1).</summary>
        [JsonProperty("currencyDisclaimer")] public string CurrencyDisclaimer;
        [JsonProperty("packs")] public List<PackDef> Packs = new List<PackDef>();
    }

    /// <summary>
    /// Static surface over the canonical packs.json — loads + caches the five
    /// PackDefs, exposes lookups by SKU / tier. The Theme.cs loading pattern.
    /// </summary>
    public static class PackCatalog
    {
        /// <summary>StreamingAssets-relative path to the canonical pack data.</summary>
        private const string StreamingRelativePath = "Data/Canonical/packs.json";

        /// <summary>
        /// WO-1253. Store SKU that grants +1 concurrent Builder. Ownership of this id in
        /// <c>GameState.OwnedItemIds</c> IS the entitlement; it is not a save flag and it does
        /// not raise queue depth.
        /// </summary>
        public const string PermanentBuilderSku = "permanent-builder";

        /// <summary>
        /// Convenience kind advertised on <see cref="PermanentBuilderSku"/>. Redeemed by
        /// <c>BuildTimerService.SlotCount</c> reading SKU ownership, never a GearInventory count
        /// (re-settle must stay idempotent).
        /// </summary>
        public const string PermanentBuilderKind = "permanent-builder";

        private static PackCatalogData _data;

        /// <summary>All five packs, ordered by tier (Hearth Spark → Founder's Vow).</summary>
        public static IReadOnlyList<PackDef> Packs
        {
            get { EnsureLoaded(); return _data.Packs; }
        }

        /// <summary>The permanent wallet-rail purchase disclaimer (§4.1).</summary>
        public static string CurrencyDisclaimer
        {
            // ⚠ THE FALLBACK IS A SECOND COPY OF AN AUTHORED SENTENCE, so it moves WITH the data or it
            // rots. Re-worded with packs.json's own `currencyDisclaimer` on 2026-09-16 (WO-1815): the old
            // "Token price moves with the market." reads as a disclaimer about the PRICE, which under a
            // flat SKR ladder no longer moves - what moves is the token's value behind it.
            //
            // ⛔ WO-1832 (2026-09-17) — THE SPELLING IS PER-ARTIFACT AND THAT IS NOT COSMETIC.
            // IL2CPP writes EVERY string literal into global-metadata.dat whether its branch can run
            // or not, so this one sentence was the SOLE live `skr` hit in the 2026-09-17 rejected Play
            // AAB — MEASURED at offset 1,114,669 of
            // base/assets/bin/Data/Managed/Metadata/global-metadata.dat, reading
            // "...Priced in Pi when you tap Buy.Priced in SKR - token value moves.Primary Touch...".
            // The gate's binary matcher fires on it (trailing boundary is the space, printable run 34)
            // and rejects the artifact with PLAY_ARTIFACT_DIRTY token:skr. WO-1815's reword was
            // correct for the shelf and simply did not know the sentence ships as metadata.
            //
            // SAFE IN BOTH DIRECTIONS, proven rather than assumed: every consumer of this property is
            // in DeNelle.Wallet — PackStore.cs:1559, StoreLegalFooter.cs:104 and :126 — and
            // Assets/_Modules/Wallet/DeNelle.Wallet.asmdef carries "!GOOGLE_PLAY", so NOTHING in a
            // Play player reads this string. The Play arm is therefore unobservable in the shipped
            // game; it is kept NON-EMPTY because PackCatalogTest.cs:193 asserts non-empty and an
            // editor compile can carry -ExtraScriptingDefines GOOGLE_PLAY (FeatureFlags.cs:1651).
            // The dApp Store / Seeker build is UNCHANGED — it takes the #else arm verbatim.
            // Pinned two-sided by PlayMetadataIdentifierRegression's LiteralFreeUnderPlay.
#if GOOGLE_PLAY
            get { EnsureLoaded(); return _data.CurrencyDisclaimer ?? "Priced by the store at checkout."; }
#else
            get { EnsureLoaded(); return _data.CurrencyDisclaimer ?? "Priced in SKR - token value moves."; }
#endif
        }

        /// <summary>
        /// Looks up a pack by its SKU. Returns null when not found.
        /// <para>Resolves RETIRED ids too (<see cref="PackDef.LegacySkus"/>), so a save, a promo
        /// code (<c>reward_pack_sku</c> → RedeemCodePanel) or a dev grant written against an old id
        /// still lands on the renamed pack. Current ids are matched FIRST across the whole
        /// catalogue before any legacy id is considered, so a live SKU can never be shadowed by
        /// another pack's retired alias.</para>
        /// </summary>
        public static PackDef Find(string sku)
        {
            if (string.IsNullOrEmpty(sku)) return null;
            EnsureLoaded();
            foreach (var pack in _data.Packs)
                if (pack != null && pack.Sku == sku) return pack;
            foreach (var pack in _data.Packs)
                if (pack != null && pack.MatchesSku(sku)) return pack;
            return null;
        }

        /// <summary>
        /// Maps a possibly-retired SKU onto the id the catalogue uses TODAY. Returns the input
        /// unchanged when it is already current or resolves to nothing — callers can apply it
        /// unconditionally. Used by ownership checks so a pre-rename entitlement still reads owned.
        /// </summary>
        public static string ResolveCurrentSku(string sku)
        {
            var pack = Find(sku);
            return pack != null && !string.IsNullOrEmpty(pack.Sku) ? pack.Sku : sku;
        }

        /// <summary>
        /// Every id under which this pack may appear in a save: its current SKU plus every retired
        /// one. An ownership check must test them ALL — a player who bought the pack before the
        /// rename carries only the old string.
        /// </summary>
        public static IEnumerable<string> OwnershipKeysFor(string sku)
        {
            var pack = Find(sku);
            if (pack == null) { yield return sku; yield break; }
            if (!string.IsNullOrEmpty(pack.Sku)) yield return pack.Sku;
            if (pack.LegacySkus == null) yield break;
            foreach (var legacy in pack.LegacySkus)
                if (!string.IsNullOrEmpty(legacy)) yield return legacy;
        }

        /// <summary>True when <paramref name="sku"/> is the permanent-builder pack or a retired alias of it.</summary>
        public static bool IsPermanentBuilderSku(string sku)
        {
            if (string.IsNullOrEmpty(sku)) return false;
            foreach (var key in OwnershipKeysFor(PermanentBuilderSku))
                if (string.Equals(key, sku, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>True when <paramref name="kind"/> is the permanent-builder convenience kind (hyphen/underscore spellings compare equal).</summary>
        public static bool IsPermanentBuilderKind(string kind)
        {
            return NormalizeKind(kind) == "permanent_builder";
        }

        /// <summary>
        /// True when the owned-id list carries the permanent-builder entitlement.
        /// Derived from SKU ownership, never from a GearInventory stack, so a repeated settle
        /// cannot grant a second crew.
        /// </summary>
        public static bool OwnsPermanentBuilder(IList<string> ownedItemIds)
        {
            if (ownedItemIds == null) return false;
            foreach (var key in OwnershipKeysFor(PermanentBuilderSku))
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (ownedItemIds.Contains(key)) return true;
            }
            return false;
        }

        // =====================================================================
        //  WO-1050 — band resolution + the ledger key list
        // =====================================================================

        /// <summary>
        /// Every economy key <c>PackStoreVM.ApplyPackContents</c> actually pays out, in grant order.
        /// <para>⛔ THIS LIST IS THE HONESTY CONTRACT OF THE SPOTLIGHT LEDGER. The ledger draws one
        /// bar per key here that the pack carries a non-zero amount of, so a good that IS granted can
        /// never be un-drawn, and a good that is NOT granted can never appear. If the grant seam ever
        /// learns a new currency, add it here in the SAME commit — the same rule that governs
        /// <c>RedeemableConvenienceKinds</c> above.</para>
        /// </summary>
        public static readonly string[] LedgerEconomyKeys =
            { "wood", "iron", "crystals", "stone", "coins" };

        /// <summary>
        /// The band a pack renders in. Reads the authored <c>band</c> first and falls back to the
        /// legacy <c>storeSection</c> mapping, so a row that predates WO-1050 still lands somewhere
        /// sane instead of vanishing off the shelf.
        /// </summary>
        public static StoreBand BandOf(PackDef pack)
        {
            if (pack == null) return StoreBand.Basket;

            switch ((pack.Band ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "free":      return StoreBand.Free;
                case "gap":       return StoreBand.Gap;
                case "basket":    return StoreBand.Basket;
                case "patronage": return StoreBand.Patronage;
            }

            // Fallback: the retired mood-named sections. "support" was where the two top rungs of
            // the ladder already lived, so it maps to Patronage; everything else is a basket.
            // An impulse row that somehow lost its band is a Gap row by construction — it grants
            // exactly one resource, which is the definition of the band.
            if (pack.Impulse) return StoreBand.Gap;
            return string.Equals(pack.StoreSection, "support", StringComparison.OrdinalIgnoreCase)
                ? StoreBand.Patronage
                : StoreBand.Basket;
        }

        /// <summary>
        /// True when the Night Market shelf will actually BUILD a card for this pack.
        /// <para>This is the PackStore.PacksInBand filter, lifted here so the grant-path
        /// oracle (WO-1246) and the render loop cannot disagree about what "visible" means.
        /// Impulse SKUs that are not <see cref="PackDef.ShelfCurated"/> stay shortfall-only
        /// even when <see cref="PackDef.StoreVisible"/> defaults true (JSON-omitted).</para>
        /// </summary>
        public static bool IsOnBrowsableShelf(PackDef pack)
        {
            if (pack == null || !pack.StoreVisible) return false;
            if (pack.Impulse && !pack.ShelfCurated) return false;
            return true;
        }

        /// <summary>True when the row carries an explicitly authored, recognised band.</summary>
        public static bool HasAuthoredBand(PackDef pack)
        {
            if (pack == null) return false;
            switch ((pack.Band ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "free": case "gap": case "basket": case "patronage": return true;
                default: return false;
            }
        }

        /// <summary>Looks up a pack by its tier (1–5). Returns null when not found.</summary>
        public static PackDef FindByTier(int tier)
        {
            EnsureLoaded();
            foreach (var pack in _data.Packs)
                if (pack != null && pack.Tier == tier) return pack;
            return null;
        }

        /// <summary>Forces a re-read of packs.json (used by tests / the Monday sync).</summary>
        public static void Reload()
        {
            _data = null;
            EnsureLoaded();
        }

        // =====================================================================
        //  Loading
        // =====================================================================

        // =====================================================================
        //  Covenant firewall — the RUNTIME chokepoint (LB-5 / C-COV / C-KIND)
        // ---------------------------------------------------------------------
        //  ConvenienceItemDef.Kind is an unvalidated string; the covenant (§5.3:
        //  TIME-SAVING only, never combat power) was comment-only. This is the
        //  canonical allowlist of sanctioned convenience kinds — the SAME set the
        //  editor MonetizationCovenantRegression gate derives from skr_staking.json
        //  convenienceAllowList + packs.json _schemaNotes + the economy-pack
        //  extension set. Any convenience def whose Kind is NOT here is REJECTED at
        //  load (FlowTrace.Fail + skipped — the Guard pattern; a bad def never grants
        //  power, it is simply dropped from the pack). Stored normalised (lower,
        //  '-'/' ' -> '_') so hyphen/underscore spellings compare equal.
        private static readonly HashSet<string> ConvenienceAllowList = new HashSet<string>
        {
            // PackDef documented set (packs.json _schemaNotes.convenience)
            "instant_build", "instant_repair", "harvest_auto_collect", "xp_weekend",
            "lantern_oil_2x_expedition", "lantern_oil_3x_expedition",
            // economy-pack extension set (WO economy_store_packs _schemaExtensions)
            "harvest_boost", "instant_fill_storage", "workforce_slot",
            "storage_tier_jump", "offline_window_extension",
            // WO-1253: +1 concurrent Builder. Time-saving (crew at once), never combat power.
            "permanent_builder",
            // WO-1388: the same +1 crew for a 6 h window (Builder's Hour). Time, never power.
            "temporary_builder",
            // skr_staking.json convenienceAllowList (loyalty convenience bumps)
            "echo_storage_slot", "passive_accrual_hours",
        };

        private static string NormalizeKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        }

        // =====================================================================
        //  Shelf honesty — REDEEMABLE-TODAY convenience (WO-1118 §0 / §2.3)
        // ---------------------------------------------------------------------
        //  ⚠ THIS IS A DIFFERENT AXIS FROM ConvenienceAllowList ABOVE. Do not merge them.
        //    * ConvenienceAllowList answers "is this kind LEGAL under the covenant?" — it is the
        //      pay-to-win firewall, and every sanctioned kind belongs in it forever.
        //    * This set answers "does anything in the shipped game actually SPEND this token?" —
        //      it is a statement about the CURRENT build, and it shrinks/grows as redeemers land.
        //  A kind can be perfectly legal and still be vapor. Live redeemers:
        //    * lantern-oil-2x/3x-expedition — Lantern.cs GearInventory consume per expedition
        //    * permanent-builder -- WO-1253, BuildTimerService.SlotCount reads SKU ownership in OwnedItemIds
        //    * instant-build / instant-repair / harvest-auto-collect / xp-weekend — WO-1246,
        //      ConvenienceRedeemer.cs (Village/Monetization)
        //  Still vapor (no GearInventory consumer): harvest_boost as a PACK TOKEN
        //  (HarvestBoostService is crystal/ad), instant_fill_storage, workforce_slot,
        //  storage_tier_jump, offline_window_extension, echo_storage_slot, passive_accrual_hours.
        //  ⛔ WHEN YOU SHIP A REDEEMER, ADD ITS KIND HERE IN THE SAME COMMIT. Adding the kind here
        //  without a consumer re-creates the exact lie this set exists to stop.
        private static readonly HashSet<string> RedeemableConvenienceKinds = new HashSet<string>
        {
            // Lantern.cs (Dungeons) — consumed per expedition.
            "lantern_oil_2x_expedition", "lantern_oil_3x_expedition",
            // WO-1253 — BuildTimerService.SlotCount reads SKU ownership (OwnedItemIds),
            // not a GearInventory count. Re-settle is therefore idempotent.
            "permanent_builder",
            // WO-1246 — ConvenienceRedeemer.cs (Village/Monetization).
            "instant_build", "instant_repair", "harvest_auto_collect", "xp_weekend",
            // WO-1388 - ConvenienceRedeemer.TryRedeemTemporaryBuilder ->
            // BuildTimerService.TryGrantTemporaryBuilder(seconds, reclaimAfterExpiry: true).
            "temporary_builder",
        };

        /// <summary>
        /// True when a convenience kind has a live redeemer in THIS build — i.e. the token the
        /// player pays for is actually spendable. The store must not advertise a kind for which
        /// this is false (WO-1118 §2.3). Hyphen and underscore spellings compare equal.
        /// </summary>
        public static bool IsRedeemableConvenience(string kind)
        {
            var norm = NormalizeKind(kind);
            return norm.Length > 0 && RedeemableConvenienceKinds.Contains(norm);
        }

        /// <summary>Drops any convenience item whose Kind is outside the sanctioned
        /// allowlist (combat/stat/RNG smuggled in as "convenience"). Logs the breach
        /// via FlowTrace.Fail and removes the def so it can never grant power.</summary>
        private static void EnforceCovenant(PackCatalogData data)
        {
            if (data == null || data.Packs == null) return;
            foreach (var pack in data.Packs)
            {
                var conv = pack?.Contents?.Convenience;
                if (conv == null) continue;
                for (int i = conv.Count - 1; i >= 0; i--)
                {
                    var item = conv[i];
                    string norm = item != null ? NormalizeKind(item.Kind) : string.Empty;
                    if (string.IsNullOrEmpty(norm) || !ConvenienceAllowList.Contains(norm))
                    {
                        FlowTrace.Fail("Covenant",
                            $"PackCatalog: pack '{pack?.Sku}' convenience kind '{item?.Kind}' is NOT sanctioned " +
                            "(covenant §5.3 — time-saving only, never combat power) — REJECTED + skipped.");
                        conv.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// WO-1282 seam. A build-gated, RAIL-SIDE pack contributed on top of packs.json - today the
        /// one and only user is <c>DeNelle.Wallet.MainnetCanaryCatalog</c> under
        /// <c>#if MAINNET_CANARY_TEST</c>, which registers itself at BeforeSceneLoad and calls
        /// <see cref="Reload"/> so ordering cannot matter.
        /// <para>⚠ NULL IS THE NORMAL, CORRECT ANSWER AND IT IS NOT A FAILURE. Every ordinary build
        /// - including every Play build, which has no DeNelle.Wallet assembly at all - leaves this
        /// unregistered and ships exactly the authored catalogue. That is why this hook does NOT
        /// warn when it is unset: the silent-failure risk the WO-1282 correction block names runs
        /// the other way (a hook whose ABSENCE hides a feature). Here the absence IS the feature.
        /// The registration itself is FlowTrace'd at the Wallet end, so a canary build that fails
        /// to register says so on the one build where that would be wrong.</para>
        /// <para>⛔ This is deliberately a <c>Func</c>, not a reference to the canary type. Naming
        /// <c>MainnetCanaryCatalog</c> here would be Commerce -&gt; Wallet, the forbidden direction,
        /// and would put the owner-only real-money SKU's type name in the Play artifact.</para>
        /// </summary>
        public static Func<PackDef> BuildGatedPackProvider;

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            _data = LoadCatalog();

            // Isolated from canonical production data. The compile symbol is forwarded only to an
            // owner sideload; clean builds cannot load, find, render, or grant this SKU. WO-1282
            // moved the #if to the Wallet side (see BuildGatedPackProvider) because this file no
            // longer lives in an assembly that can name the canary catalogue.
            var gated = BuildGatedPackProvider != null ? BuildGatedPackProvider() : null;
            if (gated != null)
            {
                _data.Packs.Add(gated);
                FlowTrace.Warn("Covenant", "PackCatalog: a BUILD-GATED pack '" + gated.Sku +
                    "' was added on top of packs.json. This must NEVER appear in a shipped build.");
            }

            EnforceCovenant(_data);
        }

        private static PackCatalogData LoadCatalog()
        {
            // WebGL-safe load via CanonicalJson: Resources.Load first (works in a
            // browser, where File.ReadAllText(streamingAssetsPath) THROWS), then a
            // StreamingAssets fallback on desktop/editor. packs.json is mirrored into
            // Assets/Resources/Data/Canonical/ so the Resources path resolves.
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(StreamingRelativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = JsonConvert.DeserializeObject<PackCatalogData>(json);
                    if (parsed != null && parsed.Packs != null)
                        return parsed;
                    Debug.LogError("[PackCatalog] packs.json parsed empty.");
                }
                else
                {
                    Debug.LogError("[PackCatalog] packs.json not found (Resources or StreamingAssets).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackCatalog] Failed to read packs.json: {ex.Message}");
            }

            Debug.LogError("[PackCatalog] packs.json could not be loaded — using an empty catalog.");
            return new PackCatalogData { Packs = new List<PackDef>() };
        }
    }
}
