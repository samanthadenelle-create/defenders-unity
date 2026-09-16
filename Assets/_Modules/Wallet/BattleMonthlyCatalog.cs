// =============================================================================
// BattleMonthlyCatalog - typed model + loader for battle_monthly.json
// (WORK_ORDER_battle_and_monthly_packs, sections 4.1 / 4.2).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// WHY THIS IS A SEPARATE FILE FROM PackCatalog AND NOT A packs.json BLOCK.
// PackDef describes ONE purchasable bag of goods. A season is a TIERED TRACK the
// player climbs by playing, and a monthly card is a THIRTY-CLAIM POOL. Neither
// shape fits PackDef, and bolting them into packs.json would put two schemas
// behind one loader that a dozen oracles already walk. So: a sibling canonical
// file, read the same WebGL-safe way (CanonicalJson.Read -> Resources first,
// StreamingAssets fallback), with its own typed hydrate. packs.json is untouched.
//
// =============================================================================
//  THE FIREWALL IS ENFORCED HERE, AT LOAD, AND IT IS THE POINT OF THIS FILE.
// -----------------------------------------------------------------------------
// The covenant (docs/monetization-v2-spec.md section 2) says convenience and
// beauty, NEVER combat power. This catalogue makes that structural in three ways,
// and each one is a DIFFERENT axis - do not merge them:
//
//   1. LEGALITY.     A grant kind outside {economy, convenience_token,
//                    cosmetic_sku, skr, bundle} is REJECTED. There is no `combat`
//                    kind and adding one is not a data edit, it is a code edit
//                    that this file refuses. A convenience kind outside
//                    PackCatalog's sanctioned allowlist is likewise rejected -
//                    that is the same firewall PackCatalog.EnforceCovenant runs,
//                    asked rather than re-listed, so the two can never drift.
//
//   2. REDEEMABILITY. LEGAL IS NOT REDEEMABLE. A convenience kind can be
//                    perfectly sanctioned and still be vapor because nothing in
//                    the shipped build spends it. PackCatalog.IsRedeemableConvenience
//                    is the live statement about THIS build; this catalogue asks
//                    it, so the day a redeemer ships, both surfaces update
//                    together.
//
//   3. DELIVERABILITY. The mirror problem, and the one the 2026-08-21
//                    re-verification of the WO exposed: not "what may this grant"
//                    but "what can this grant actually DELIVER today".
//                      * cosmetic_sku -> gated on CosmeticsDeliverable (false:
//                        the render seam landed but there is NO cosmetic art in
//                        the tree, so every equipped cosmetic lands on a
//                        preview-tint fallback).
//                      * skr          -> gated on SkrLedgerAvailable, which is
//                        RESOLVED BY REFLECTION rather than asserted, because
//                        ISkrLedger does not exist anywhere in the tree today.
//                    A rewards programme whose rewards do not exist is worse than
//                    a bad pack: a pack disappoints once, a season disappoints for
//                    a month. So a reward that cannot be delivered is DROPPED at
//                    load and self-reports; it is never quietly granted as a flag.
//
// A dropped grant is a Guard-pattern skip: it Fails loudly through FlowTrace and
// the surrounding tier/day survives. Nothing here throws - a bad row must never
// take the season down with it.
//
// ASCII-only strings (TMP renders non-ASCII as tofu). Null-safe throughout.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Wallet
{
    /// <summary>
    /// The sanctioned reward kinds. <b>There is deliberately no Combat member and never will be</b> -
    /// section 2.3 of the WO bans revives, mid-battle heals, stat boosts, passives, level/cap raises
    /// and tier sales outright, and an enum with no way to spell them is a stronger guarantee than a
    /// validator that hopes to catch them.
    /// </summary>
    public enum RewardKind
    {
        /// <summary>Unparseable / unsanctioned. Always dropped, never granted.</summary>
        Unknown = 0,
        /// <summary>Soft economy: wood / iron / food / crystals / coins. Out-of-combat only.</summary>
        Economy = 1,
        /// <summary>A ConvenienceItemDef.Kind token. Time-saving only, out-of-combat only.</summary>
        ConvenienceToken = 2,
        /// <summary>A cosmetic SKU. Gated on <see cref="BattleMonthlyCatalog.CosmeticsDeliverable"/>.</summary>
        CosmeticSku = 3,
        /// <summary>SKR credit. Gated on <see cref="BattleMonthlyCatalog.SkrLedgerAvailable"/>.</summary>
        Skr = 4,
        /// <summary>Several grants at once. Nests; never self-nests deeper than the depth cap.</summary>
        Bundle = 5,
    }

    /// <summary>
    /// The soft-economy payload of a grant.
    /// <para>The payload deliberately contains only live economy ledgers.</para>
    /// </summary>
    [Serializable]
    public sealed class RewardEconomy
    {
        [JsonProperty("wood")]     public int Wood;
        [JsonProperty("iron")]     public int Iron;
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("stone")]    public int Stone;
        [JsonProperty("food")] private int LegacyFood { set { if (Stone == 0) Stone = value; } }
        [JsonProperty("crystals")] public int Crystals;
        [JsonProperty("coins")]    public int Coins;

        /// <summary>True when this payload would move nothing.</summary>
        public bool IsEmpty => Wood <= 0 && Iron <= 0 && Stone <= 0 && Crystals <= 0 && Coins <= 0;
    }

    /// <summary>One convenience token grant: a sanctioned kind and a count.</summary>
    [Serializable]
    public sealed class RewardConvenience
    {
        [JsonProperty("kind")]  public string Kind;
        [JsonProperty("count")] public int Count;
    }

    /// <summary>
    /// One reward. The recursive shape both families converge on, so a new reward is a JSON row and
    /// never a <c>switch</c> on a SKU name.
    /// </summary>
    [Serializable]
    public sealed class RewardGrant
    {
        [JsonProperty("kind")]        public string KindRaw;
        [JsonProperty("economy")]     public RewardEconomy Economy;
        [JsonProperty("convenience")] public RewardConvenience Convenience;
        [JsonProperty("cosmeticSku")] public string CosmeticSku;
        [JsonProperty("skr")]         public double Skr;
        [JsonProperty("bundle")]      public List<RewardGrant> Bundle;

        /// <summary>The parsed kind. <see cref="RewardKind.Unknown"/> for anything unsanctioned.</summary>
        public RewardKind Kind => ParseKind(KindRaw);

        /// <summary>Maps the authored string onto the enum. Hyphen and underscore spellings compare equal.</summary>
        public static RewardKind ParseKind(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return RewardKind.Unknown;
            switch (raw.Trim().ToLowerInvariant().Replace('-', '_'))
            {
                case "economy":           return RewardKind.Economy;
                case "convenience_token": return RewardKind.ConvenienceToken;
                case "cosmetic_sku":      return RewardKind.CosmeticSku;
                case "skr":               return RewardKind.Skr;
                case "bundle":            return RewardKind.Bundle;
                default:                  return RewardKind.Unknown;
            }
        }

        /// <summary>A short, player-safe one-line summary. ASCII only.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case RewardKind.Economy:
                    return DescribeEconomy(Economy);
                case RewardKind.ConvenienceToken:
                    return Convenience != null
                        ? Convenience.Count + "x " + PrettyKind(Convenience.Kind)
                        : string.Empty;
                case RewardKind.CosmeticSku:
                    return CosmeticSku ?? string.Empty;
                case RewardKind.Skr:
                    return Skr.ToString("0.##") + " SKR";
                case RewardKind.Bundle:
                {
                    if (Bundle == null || Bundle.Count == 0) return string.Empty;
                    var parts = new List<string>();
                    for (int i = 0; i < Bundle.Count; i++)
                    {
                        string s = Bundle[i] != null ? Bundle[i].Describe() : string.Empty;
                        if (!string.IsNullOrEmpty(s)) parts.Add(s);
                    }
                    return string.Join("  ", parts.ToArray());
                }
                default:
                    return string.Empty;
            }
        }

        private static string DescribeEconomy(RewardEconomy e)
        {
            if (e == null) return string.Empty;
            var parts = new List<string>();
            if (e.Wood > 0)     parts.Add(e.Wood + " Wood");
            if (e.Iron > 0)     parts.Add(e.Iron + " Iron");
            if (e.Stone > 0)     parts.Add(e.Stone + " Stone");
            if (e.Crystals > 0) parts.Add(e.Crystals + " Crystals");
            if (e.Coins > 0)    parts.Add(e.Coins + " Coins");
            return string.Join("  ", parts.ToArray());
        }

        private static string PrettyKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "Token";
            return kind.Replace('-', ' ').Replace('_', ' ');
        }
    }

    /// <summary>One rung of the season track: the free reward, the premium reward, the XP gate.</summary>
    [Serializable]
    public sealed class BattlePassTier
    {
        [JsonProperty("tier")]       public int Tier;
        [JsonProperty("xpRequired")] public int XpRequired;
        [JsonProperty("free")]       public RewardGrant Free;
        [JsonProperty("premium")]    public RewardGrant Premium;
        [JsonProperty("isCapstone")] public bool IsCapstone;
    }

    /// <summary>The XP-from-PLAY rules. Nothing here is ever purchasable (WO section 6 invariant 2).</summary>
    [Serializable]
    public sealed class BattlePassXpRules
    {
        [JsonProperty("perWin")]          public int PerWin = 100;
        [JsonProperty("perLoss")]         public int PerLoss = 25;
        [JsonProperty("perStreakStep")]   public int PerStreakStep = 10;
        [JsonProperty("streakStepCap")]   public int StreakStepCap = 10;
        [JsonProperty("perfectBonus")]    public int PerfectBonus;
        [JsonProperty("dailySoftCap")]    public int DailySoftCap = 1500;
        [JsonProperty("softCapTaperPct")] public double SoftCapTaperPct = 0.5d;
    }

    /// <summary>A season: a calendar month of tiers, climbed by playing.</summary>
    [Serializable]
    public sealed class BattlePassSeason
    {
        [JsonProperty("seasonId")]       public string SeasonId;
        [JsonProperty("name")]           public string Name;
        [JsonProperty("tagline")]        public string Tagline;
        [JsonProperty("startUtc")]       public string StartUtc;
        [JsonProperty("endUtc")]         public string EndUtc;
        [JsonProperty("lengthDays")]     public int LengthDays = 30;
        [JsonProperty("premiumPassSku")] public string PremiumPassSku;
        [JsonProperty("xp")]             public BattlePassXpRules Xp = new BattlePassXpRules();
        [JsonProperty("tiers")]          public List<BattlePassTier> Tiers = new List<BattlePassTier>();

        /// <summary>
        /// True when a premium lane can actually be BOUGHT: the season names a SKU and that SKU
        /// resolves to a real pack. False today, deliberately - see the authored
        /// <c>_premiumPassSkuUnauthored</c> note in battle_monthly.json.
        /// <para>The Season Track screen asks this before it draws a purchase control. A Buy button
        /// that cannot complete is the WO-1118 vapor rule wearing a CTA.</para>
        /// </summary>
        public bool HasPurchasablePremiumLane =>
            !string.IsNullOrEmpty(PremiumPassSku) && PackCatalog.Find(PremiumPassSku) != null;

        /// <summary>
        /// The XP gate for a tier, SCALED to the month actually being played.
        /// <para>Owner ruling Q1 made seasons calendar-month, which means a 28-day February and a
        /// 31-day March award the same ~30 tiers over different windows - about a 10 percent drift
        /// in required XP per day. Rather than re-author the curve twelve times (twelve copies of
        /// one decision is how this repo builds its worst drift bugs), the curve is authored ONCE
        /// against <see cref="LengthDays"/> and derived here. Every month is equally completable.</para>
        /// </summary>
        public int XpRequiredScaled(BattlePassTier tier, int actualDays)
        {
            if (tier == null) return int.MaxValue;
            if (LengthDays <= 0 || actualDays <= 0 || actualDays == LengthDays) return tier.XpRequired;
            double scaled = (double)tier.XpRequired * actualDays / LengthDays;
            return (int)Math.Ceiling(scaled);
        }

        /// <summary>Days in the calendar month a UTC instant falls in - the live season length (ruling Q1).</summary>
        public static int DaysInSeasonMonth(DateTime utc) =>
            DateTime.DaysInMonth(utc.Year, utc.Month);

        /// <summary>First instant of the calendar month containing <paramref name="utc"/>.</summary>
        public static DateTime SeasonStart(DateTime utc) =>
            new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Whole days left in the current calendar month, floored at 0. Used by the header line.
        /// <para>It is a COUNT OF DAYS, never a ticking clock: section 3.2 forbids manufactured
        /// urgency, and nothing in a season is lost at close (earned rewards are kept forever).</para>
        /// </summary>
        public static int DaysRemainingInSeason(DateTime utc)
        {
            var end = SeasonStart(utc).AddMonths(1);
            int days = (int)Math.Ceiling((end - utc).TotalDays);
            return days < 0 ? 0 : days;
        }
    }

    /// <summary>One day of a monthly card's table.</summary>
    [Serializable]
    public sealed class MonthlyDailyDrip
    {
        [JsonProperty("day")]       public int Day;
        [JsonProperty("grant")]     public RewardGrant Grant;
        [JsonProperty("highlight")] public bool Highlight;
    }

    /// <summary>
    /// A monthly card: pay once, claim a table of daily drips.
    /// <para><b>Pool model</b> (owner default Q2): <see cref="DurationDays"/> counts CLAIMS, not
    /// calendar days. A missed day is never lost. Nothing expires, so nothing on the screen
    /// counts down.</para>
    /// </summary>
    [Serializable]
    public sealed class MonthlyCard
    {
        [JsonProperty("sku")]               public string Sku;
        [JsonProperty("tier")]              public int Tier;
        [JsonProperty("name")]              public string Name;
        [JsonProperty("tagline")]           public string Tagline;
        [JsonProperty("pricing")]           public PackPricing Pricing = new PackPricing();
        [JsonProperty("durationDays")]      public int DurationDays = 30;
        [JsonProperty("exclusiveCosmetic")] public string ExclusiveCosmetic;
        [JsonProperty("claimModel")]        public string ClaimModel = "pool";
        [JsonProperty("stackable")]         public bool Stackable = true;
        [JsonProperty("dailyTable")]        public List<MonthlyDailyDrip> DailyTable = new List<MonthlyDailyDrip>();

        /// <summary>True for the generous, non-predatory model. Anything else is the calendar model.</summary>
        public bool IsPoolModel =>
            string.Equals((ClaimModel ?? "pool").Trim(), "pool", StringComparison.OrdinalIgnoreCase);

        /// <summary>The drip for a 1-based day, or null.</summary>
        public MonthlyDailyDrip Day(int day)
        {
            if (DailyTable == null) return null;
            for (int i = 0; i < DailyTable.Count; i++)
                if (DailyTable[i] != null && DailyTable[i].Day == day) return DailyTable[i];
            return null;
        }
    }

    /// <summary>The parsed battle_monthly.json root.</summary>
    [Serializable]
    public sealed class BattleMonthlyData
    {
        [JsonProperty("version")]           public int Version;
        [JsonProperty("battlePassSeasons")] public List<BattlePassSeason> Seasons = new List<BattlePassSeason>();
        [JsonProperty("monthlyCards")]      public List<MonthlyCard> Cards = new List<MonthlyCard>();
    }

    /// <summary>
    /// Static surface over battle_monthly.json: loads, hydrates, and enforces the firewall at load.
    /// </summary>
    public static class BattleMonthlyCatalog
    {
        private const string StreamingRelativePath = "Data/Canonical/battle_monthly.json";

        /// <summary>How deep a bundle may nest before it is refused as malformed.</summary>
        private const int MaxBundleDepth = 4;

        private static BattleMonthlyData _data;
        private static int _droppedGrants;

        /// <summary>Every authored season (usually one live one).</summary>
        public static IReadOnlyList<BattlePassSeason> Seasons { get { EnsureLoaded(); return _data.Seasons; } }

        /// <summary>Every authored monthly card, in tier order as authored.</summary>
        public static IReadOnlyList<MonthlyCard> Cards { get { EnsureLoaded(); return _data.Cards; } }

        /// <summary>How many grants the firewall dropped on the last load. 0 on a clean file.</summary>
        public static int DroppedGrants { get { EnsureLoaded(); return _droppedGrants; } }

        /// <summary>The season to render. First authored season; null when the file is absent.</summary>
        public static BattlePassSeason ActiveSeason
        {
            get
            {
                EnsureLoaded();
                return _data.Seasons != null && _data.Seasons.Count > 0 ? _data.Seasons[0] : null;
            }
        }

        /// <summary>Looks up a monthly card by SKU. Null when absent.</summary>
        public static MonthlyCard FindCard(string sku)
        {
            if (string.IsNullOrEmpty(sku)) return null;
            EnsureLoaded();
            if (_data.Cards == null) return null;
            for (int i = 0; i < _data.Cards.Count; i++)
                if (_data.Cards[i] != null && string.Equals(_data.Cards[i].Sku, sku, StringComparison.Ordinal))
                    return _data.Cards[i];
            return null;
        }

        /// <summary>Forces a re-read (tests / the regression oracle).</summary>
        public static void Reload() { _data = null; EnsureLoaded(); }

        // =====================================================================
        //  DELIVERABILITY - the two gates that decide what may be AUTHORED today
        // =====================================================================

        /// <summary>
        /// True when an equipped cosmetic actually CHANGES WHAT THE PLAYER SEES in this build.
        ///
        /// <para><b>FALSE TODAY, AND THAT IS A MEASUREMENT, NOT AN OPINION.</b> The render seam
        /// itself landed on 2026-08-21 - <c>CosmeticApplier</c> is a real self-binding applier now,
        /// reached from HeroBodySwapper and HeroArmorVisual, and pinned by
        /// <c>CosmeticApplyRegression</c>. What is missing is the ART: there is no cosmetic art in
        /// the tree, so every equipped cosmetic lands on the applier's preview-tint fallback and
        /// logs a Warn naming the Resources path that would have replaced it.</para>
        ///
        /// <para>A battle pass whose premium lane pays out preview tints for thirty days is the
        /// exact vapor WO-1118 exists to refuse - worse than a pack, because a pack disappoints
        /// once. So while this is false, an authored <c>cosmetic_sku</c> grant is DROPPED at load
        /// and FAILS the build gate.</para>
        ///
        /// <para>&#9888; WHY THIS IS A CONSTANT AND NOT A PROBE. There is no honest runtime question
        /// to ask: the applier resolves art lazily per cosmetic id and falls back silently by
        /// design, so "does art exist" has no single answer a loader can read. A named constant
        /// with one owner is checkable and a fuzzy probe is not. <b>Flip it in the SAME commit that
        /// lands the first real cosmetic art, and author the cosmetic reward lines in that same
        /// change.</b> Flipping it without art re-creates precisely the lie it exists to stop.</para>
        /// </summary>
        public const bool CosmeticsDeliverable = false;

        private static bool _skrResolved;
        private static bool _skrAvailable;

        /// <summary>
        /// True when an <c>skr</c> grant has somewhere to land.
        ///
        /// <para><b>FALSE TODAY.</b> The WO spends <c>ISkrLedger.Credit</c> in its interpreter and
        /// its acceptance criteria, and promises V1 works via a <c>LocalSkrLedger</c>. Neither type
        /// exists anywhere in the tree - the only occurrence of the name is a doc comment in
        /// <c>IPiPlatform.cs</c> describing the pattern. What DOES exist is a data catalogue with
        /// <c>costSkr</c> rows, <c>CurrencyKind.Skr</c> on the wallet rail, and
        /// <c>FeatureFlags.SkrPreview</c> at <c>defaultOn:false</c>.</para>
        ///
        /// <para>Unlike <see cref="CosmeticsDeliverable"/> this one is RESOLVED, not asserted: a
        /// ledger is a TYPE, and a type either exists in the loaded assemblies or it does not. So
        /// the day someone writes it, this returns true on its own and no one has to remember to
        /// flip anything. That is the better shape wherever the question has a real answer.</para>
        /// </summary>
        public static bool SkrLedgerAvailable
        {
            get
            {
                if (_skrResolved) return _skrAvailable;
                _skrResolved = true;
                _skrAvailable = false;
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm == null) continue;
                        if (asm.GetType("DeNelle.Core.Economy.ISkrLedger", false) != null ||
                            asm.GetType("DeNelle.Core.Economy.LocalSkrLedger", false) != null ||
                            asm.GetType("DeNelle.Wallet.ISkrLedger", false) != null ||
                            asm.GetType("DeNelle.Wallet.LocalSkrLedger", false) != null)
                        {
                            _skrAvailable = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // No silent catch (CLAUDE.md section 12): say why the answer is "no".
                    FlowTrace.Fail("BattlePass",
                        "SkrLedgerAvailable probe THREW (" + ex.GetType().Name + ": " + ex.Message +
                        ") - treating SKR as UNAVAILABLE, so no skr reward can be granted.");
                }
                return _skrAvailable;
            }
        }

        /// <summary>Test hook - drops the memoised SKR probe so a new assembly is re-scanned.</summary>
        public static void ResetSkrProbe() { _skrResolved = false; _skrAvailable = false; }

        // =====================================================================
        //  Loading + the firewall
        // =====================================================================

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            _data = LoadData();
            _droppedGrants = 0;
            EnforceFirewall(_data);
            if (_droppedGrants > 0)
                FlowTrace.Fail("BattlePass",
                    "BattleMonthlyCatalog: " + _droppedGrants + " reward grant(s) were DROPPED by the " +
                    "firewall. A dropped grant is never silently paid out as a flag - see the Fail lines " +
                    "above for which rows and why.");
            else
                FlowTrace.Step("BattlePass",
                    "BattleMonthlyCatalog loaded: " + (_data.Seasons != null ? _data.Seasons.Count : 0) +
                    " season(s), " + (_data.Cards != null ? _data.Cards.Count : 0) +
                    " monthly card(s), 0 grants dropped.");
        }

        private static BattleMonthlyData LoadData()
        {
            try
            {
                string json = CanonicalJson.Read(StreamingRelativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = JsonConvert.DeserializeObject<BattleMonthlyData>(json);
                    if (parsed != null) return parsed;
                    FlowTrace.Fail("BattlePass", "battle_monthly.json parsed to null - season + cards unavailable.");
                }
                else
                {
                    FlowTrace.Fail("BattlePass",
                        "battle_monthly.json not found (Resources or StreamingAssets) - the Season Track and " +
                        "Monthly Ledger screens will render their empty states rather than a blank panel.");
                }
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass",
                    "battle_monthly.json read/parse THREW: " + ex.GetType().Name + ": " + ex.Message);
            }
            return new BattleMonthlyData();
        }

        /// <summary>
        /// Walks every authored grant and NULLS any that the covenant, the redeemer set or the
        /// deliverability gates refuse. Never throws; a bad row costs its own reward and nothing else.
        /// </summary>
        private static void EnforceFirewall(BattleMonthlyData data)
        {
            if (data == null) return;

            if (data.Seasons != null)
                foreach (var season in data.Seasons)
                {
                    if (season == null || season.Tiers == null) continue;
                    foreach (var tier in season.Tiers)
                    {
                        if (tier == null) continue;
                        string where = "season '" + (season.SeasonId ?? "?") + "' tier " + tier.Tier;
                        tier.Free    = Sanitize(tier.Free,    where + " free", 0);
                        tier.Premium = Sanitize(tier.Premium, where + " premium", 0);
                    }
                }

            if (data.Cards != null)
                foreach (var card in data.Cards)
                {
                    if (card == null) continue;

                    // The month-exclusive cosmetic is the same deliverability question as any other
                    // cosmetic reward, asked about the headline of the card rather than a tier.
                    if (!string.IsNullOrEmpty(card.ExclusiveCosmetic) && !CosmeticsDeliverable)
                    {
                        FlowTrace.Fail("MonthlyCard",
                            "card '" + card.Sku + "' names an exclusiveCosmetic ('" + card.ExclusiveCosmetic +
                            "') but no cosmetic art exists in this build, so it would land on the applier's " +
                            "preview-tint fallback. CLEARED - the card must not be sold on a cosmetic it " +
                            "cannot show.");
                        card.ExclusiveCosmetic = null;
                        _droppedGrants++;
                    }

                    if (card.DailyTable == null) continue;
                    foreach (var drip in card.DailyTable)
                    {
                        if (drip == null) continue;
                        drip.Grant = Sanitize(drip.Grant, "card '" + card.Sku + "' day " + drip.Day, 0);
                    }
                }
        }

        /// <summary>
        /// Returns the grant if every gate accepts it, otherwise null (and says why). Recurses into
        /// bundles, dropping only the offending leaf where the rest of the bundle is sound.
        /// </summary>
        private static RewardGrant Sanitize(RewardGrant grant, string where, int depth)
        {
            if (grant == null) return null;

            if (depth > MaxBundleDepth)
            {
                FlowTrace.Fail("BattlePass", where + ": bundle nests deeper than " + MaxBundleDepth +
                                             " - refused as malformed.");
                _droppedGrants++;
                return null;
            }

            switch (grant.Kind)
            {
                case RewardKind.Economy:
                    if (grant.Economy == null || grant.Economy.IsEmpty)
                    {
                        FlowTrace.Warn("BattlePass", where + ": economy grant carries no amount - dropped " +
                                                     "(an empty reward reads to the player as a broken one).");
                        _droppedGrants++;
                        return null;
                    }
                    return grant;

                case RewardKind.ConvenienceToken:
                {
                    string kind = grant.Convenience != null ? grant.Convenience.Kind : null;
                    int count = grant.Convenience != null ? grant.Convenience.Count : 0;

                    if (string.IsNullOrEmpty(kind) || count <= 0)
                    {
                        FlowTrace.Fail("BattlePass", where + ": convenience grant has no kind or a non-positive " +
                                                     "count - dropped.");
                        _droppedGrants++;
                        return null;
                    }

                    // AXIS 2: legal is not redeemable. PackCatalog owns the live statement about
                    // this build; asking it (rather than re-listing kinds here) means the day a
                    // redeemer ships, this surface updates itself.
                    if (!PackCatalog.IsRedeemableConvenience(kind))
                    {
                        FlowTrace.Fail("BattlePass", where + ": convenience kind '" + kind + "' has NO redeemer in " +
                                                     "this build - nothing in the shipped game spends it, so the " +
                                                     "token would accumulate unread. Dropped.");
                        _droppedGrants++;
                        return null;
                    }
                    return grant;
                }

                case RewardKind.CosmeticSku:
                    if (string.IsNullOrEmpty(grant.CosmeticSku))
                    {
                        FlowTrace.Fail("BattlePass", where + ": cosmetic grant names no SKU - dropped.");
                        _droppedGrants++;
                        return null;
                    }
                    if (!CosmeticsDeliverable)
                    {
                        FlowTrace.Fail("BattlePass", where + ": cosmetic '" + grant.CosmeticSku + "' cannot be " +
                                                     "DELIVERED in this build (no cosmetic art in the tree - the " +
                                                     "applier would fall back to a preview tint). Dropped. Author " +
                                                     "cosmetic rewards in the same change that lands the art.");
                        _droppedGrants++;
                        return null;
                    }
                    return grant;

                case RewardKind.Skr:
                    if (grant.Skr <= 0d)
                    {
                        FlowTrace.Fail("BattlePass", where + ": skr grant is non-positive - dropped.");
                        _droppedGrants++;
                        return null;
                    }
                    if (!SkrLedgerAvailable)
                    {
                        FlowTrace.Fail("BattlePass", where + ": an skr reward is authored but there is NO SKR " +
                                                     "LEDGER in this build (ISkrLedger / LocalSkrLedger do not " +
                                                     "exist), so the credit has nowhere to land. Dropped.");
                        _droppedGrants++;
                        return null;
                    }
                    return grant;

                case RewardKind.Bundle:
                {
                    if (grant.Bundle == null || grant.Bundle.Count == 0)
                    {
                        FlowTrace.Warn("BattlePass", where + ": empty bundle - dropped.");
                        _droppedGrants++;
                        return null;
                    }
                    var kept = new List<RewardGrant>();
                    for (int i = 0; i < grant.Bundle.Count; i++)
                    {
                        var child = Sanitize(grant.Bundle[i], where + " [bundle " + i + "]", depth + 1);
                        if (child != null) kept.Add(child);
                    }
                    if (kept.Count == 0)
                    {
                        FlowTrace.Fail("BattlePass", where + ": every member of the bundle was refused - the whole " +
                                                     "reward is dropped.");
                        return null;
                    }
                    grant.Bundle = kept;
                    return grant;
                }

                default:
                    // AXIS 1: legality. This is the branch a `combat` kind lands in, and it is why
                    // adding one is not a data edit.
                    FlowTrace.Fail("BattlePass", where + ": reward kind '" + (grant.KindRaw ?? "<null>") +
                                                 "' is NOT a sanctioned kind (economy | convenience_token | " +
                                                 "cosmetic_sku | skr | bundle). There is no combat kind and there " +
                                                 "never will be - covenant section 2. REJECTED + dropped.");
                    _droppedGrants++;
                    return null;
            }
        }
    }
}
