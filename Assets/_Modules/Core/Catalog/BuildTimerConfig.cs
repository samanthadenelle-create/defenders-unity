// =============================================================================
// BuildTimerConfig — the tunable knobs for build/upgrade timers + ad-skip (WO-172).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Catalog
//
// CoC-style time-sink tuning, kept OUT of logic (WO-172 constraint: "all durations
// in a tunable SO/constants, never hard-coded"). One asset drives:
//   • the HYBRID duration curve — short early (snappy onboarding), scaling
//     super-linearly to hours for high tiers (the endgame drag) via DurationForTier;
//   • the rewarded-ad skip chunk + daily cap (the opt-in monetization lever);
//   • the premium instant-finish crystal price;
//   • the free build-slot count (concurrency / scarcity → sellable slots later).
//
// WO-855 Phase 4 (2026-08-03) -- the curve is now actually REACHABLE. The placement
// path used to pass a hard-coded literal tier 0, so every structure built in exactly
// baseBuildSeconds (15s) and tierGrowth was dead tuning. The tier is now derived from
// the structure's authored cost basket (TierForCost), and the defaults were retuned to
// the WO-855 sec.4.6 mobile bands -- snappy early, hours-long endgame:
//   base 45s | tierGrowth 3.2 | upgradeMultiplier 1.25 | freeBuildSlots 2
//   tier ladder: 45s | 2.4m | 7.7m | 24.6m | 1.3h | 4.2h
// First-time discoveries receive the separate 15s grace below. Repeat builds and
// advanced upgrades pay the real curve, preserving early momentum without letting
// the construction catalog evaporate in one sitting.
// There is NO Resources/Economy/BuildTimerConfig.asset in the tree -- these C# defaults
// ARE the live numbers. Author an asset only to override them.
//
// Lives in DeNelle.Core (pure data, no Village ref) so both the Village
// BuildTimerService AND a future Core-side replay/validation can read the same
// numbers. BuildTimerService resolves an instance via Resources.Load (path below)
// and falls back to code defaults if the asset is absent — so the system works with
// zero scene/asset authoring and the owner can drop an authored asset to retune.
// =============================================================================

using System;
using UnityEngine;

namespace DeNelle.Core.Catalog
{
    /// <summary>
    /// Tunable durations / ad-skip / instant-finish / build-slot knobs for the
    /// WO-172 construction-timer system. Author one asset to retune; code defaults
    /// stand if none exists.
    /// </summary>
    [CreateAssetMenu(menuName = "Defenders/Economy/Build Timer Config", fileName = "BuildTimerConfig")]
    public sealed class BuildTimerConfig : ScriptableObject
    {
        /// <summary>Resources path BuildTimerService loads by default (no extension).</summary>
        public const string ResourcesPath = "Economy/BuildTimerConfig";

        [Header("Hybrid duration curve (tier 0 = first build)")]
        [Tooltip("Base seconds for a tier-0 build — keep onboarding snappy (seconds–minutes).")]
        [Min(0f)] public float baseBuildSeconds = 45f;

        [Tooltip("Per-tier multiplier — durations scale super-linearly: tierSeconds = base * pow(growth, tier). " +
                 ">1 makes high tiers the hours-long endgame drag that drives ad-watches/spend.")]
        [Min(1f)] public float tierGrowth = 3.2f;

        [Tooltip("Hard ceiling on any single job's duration (seconds). Default 24h — no multi-day lockouts.")]
        [Min(0f)] public float maxDurationSeconds = 24f * 3600f;

        [Tooltip("Upgrades multiply the same tier curve by this (upgrades a touch longer than a fresh build of the same tier).")]
        [Min(0f)] public float upgradeMultiplier = 1.25f;

        [Header("Cost -> tier bands (WO-855 Phase 4)")]
        [Tooltip("Ascending resource-basket thresholds. A structure whose authored cost basket reaches " +
                 "thresholds[i] builds at tier i+1. Below thresholds[0] = tier 0 (the snappy early game).")]
        // RE-CALIBRATED 2026-08-21 for the economy sink pass, which is the event the previous
        // revision of this comment named in advance ("Bands 4-5 are HEADROOM ... what turns the
        // ladder into the hours-long endgame as the Phase 2/3 cost retune pushes late rows up.
        // Re-check this table whenever catalog costs move."). The costs moved; this is the re-check.
        //
        // The live basket spread went from 5..440 to 20..20,640, so the five old bands are a
        // straight x4 rescale of the OWNER-CALIBRATED originals (140/260/420 -> 600/1200/2400),
        // which preserves the hard constraint they encoded: band 0 still swallows EVERY founding
        // piece, collector and wall (deco_torch 20, wall_wood 80, repair 180, collector_farm 320,
        // mill/lumbermill 360, wall_stone 360, gate_stone 440, market/workshop 400), so the first
        // ten minutes of a new save are still never gated. TWO NEW BANDS (11000, 17000) extend the
        // ladder to tier 7, which is where the 24h maxDurationSeconds clamp finally bites.
        //
        // THE LADDER, at base 45s / growth 3.2 / upgrade x1.25:
        //   T0 45s | T1 2.4m | T2 7.7m | T3 24.6m | T4 1.3h | T5 4.2h | T6 13.4h | T7 24h (clamped)
        // Only the maxed storage ladder (lumberyard/foundry/silo top steps, 16k-20k baskets)
        // reaches T6-T7 -- the endgame drag lands on the endgame, not on walls.
        //
        // ⛔ WHAT THIS TABLE CANNOT DO, measured 2026-08-21 -- READ BEFORE RETUNING IT AGAIN:
        // it cannot deliver the owner's 8-12 week pacing target, and NO setting of these numbers
        // can. A realistic committed town is ~196 build/upgrade actions; at 2 concurrent builders
        // even the MAXIMUM possible ladder (every single action clamped to 24h, walls included)
        // tops out near 98 days, and every playable curve lands at 4-7 DAYS. The binding limit is
        // CONTENT VOLUME, not the multipliers -- 196 actions is simply not 8 weeks of game at any
        // tolerable per-action wait. Pushing these bands down to chase the target only makes early
        // walls take hours; it does not move the total. The gap has to be closed with more content
        // or a repeatable endgame loop, which is an OWNER decision, not a tuning one.
        public float[] tierCostThresholds = { 600f, 1200f, 2400f, 4200f, 7000f, 11000f, 17000f };

        // OWNER RULING 2026-08-06. Crystals END a wait (immediate finish); an ad only
        // DENTS it. Two different products, not one product at two speeds.
        //
        // THE CAP IS A CONVERSION TRIGGER, NOT A LIMIT ON REVENUE - that is the whole
        // point and it is easy to get backwards. The retention-first 2026-08-21 pass limits
        // this to three voluntary watches per four hours: enough to rescue a play session,
        // not enough to turn construction into an ad playlist or pressure a purchase.
        //
        // The numbers land on purpose: a 20-minute troop clears in 2 watches (feels free),
        // A ten-minute chunk clears a short wait and dents a long one. Late progression
        // remains paced by planning, offline time and optional crystal use.
        [Header("Rewarded-ad skip (opt-in, store-build only)")]
        [Tooltip("Seconds knocked off the remaining timer per rewarded-ad watch.")]
        [Min(0f)] public float adSkipSeconds = 10f * 60f;

        // ROLLING WINDOW, not a calendar day. A day-reset punishes an evening player who
        // spent their allowance that morning; rolling always offers a way back in.
        //
        // WARNING FOR WHOEVER WIRES THIS: the persisted ledger is DAY-SHAPED
        // (SaveSchema AdSkipsUsedToday + AdSkipDayKey, since v13/WO-172) and CANNOT express
        // a rolling window. Implementing this needs the timestamps of recent watches (or a
        // decaying counter), i.e. a schema addition - not a config tweak. Reusing the day
        // fields would silently ship day-reset behaviour and quietly lose the ruling.
        [Tooltip("Max rewarded-ad skips allowed within the rolling window below. 0 = unlimited.")]
        [Min(0)] public int adSkipsPerWindow = 3;

        [Tooltip("Length of the ROLLING window the skip allowance is counted over. Default 4 hours.")]
        [Min(0f)] public float adSkipWindowSeconds = 4f * 60f * 60f;

        [Header("Premium instant-finish (convenience IAP, not power)")]
        [Tooltip("Aether-crystal price to instantly finish a job, scaled by remaining minutes. " +
                 "0 disables the paid skip (ad-skip still works).")]
        [Min(0)] public int instantFinishCrystalsPerMinute = 1;

        [Tooltip("Minimum crystal price for any instant-finish (so near-done jobs still cost something).")]
        [Min(0)] public int instantFinishMinCrystals = 10;

        // ---------------------------------------------------------------------
        //  HIRE REINFORCEMENTS -- WO-1372 Lane D. GOLD buys TROOP TIME.
        //  Owner ruling 2026-09-04, verbatim: "gold buys hire mercenaries instead
        //  of waiting on time." Creative canon (CREATIVE_CANON_ELARION §6): the
        //  mercenaries ARE the same unit; the button reads HIRE REINFORCEMENTS.
        //
        //  ONE CURVE, TWO KNOB PAIRS. A TrainTroop job is priced by SkipPrice(...)
        //  below with THESE knobs; every other kind keeps the crystal pair above.
        //  HireReinforcementsRegression case 2 fails the moment the two prices
        //  diverge under identical knobs, so the maths cannot fork.
        //
        //  THE NUMBERS (defaults, not rulings - the ruling fixed the VERB and the
        //  CURRENCY, not a figure). Against the live troops.json (buildSeconds
        //  45..270, costGold 205..1500) and the anchored e=0.75 curve, 20/min with
        //  a 50 floor prices a fresh Footman (45s) at ~99 gold, a 2-minute train
        //  at ~208 and a 4.5-minute one at ~380 - a second spend of roughly a
        //  fifth to two-thirds of the troop's own price, so "hire" reads as an
        //  impatience tax and never as a cheaper troop. Canon's illustrative
        //  "HIRE REINFORCEMENTS - 300 Gold" lands near a 3-minute wait.
        //
        //  NOT on the remote-tunable rail, deliberately: neither
        //  instantFinishCrystalsPerMinute nor instantFinishMinCrystals is on it
        //  (RemoteTunables.cs carries no BuildTimerConfig knob at all), and the
        //  owner's mirroring rule is "do what the crystal pair does". Author the
        //  Resources asset to retune, exactly as for the crystal pair.
        // ---------------------------------------------------------------------
        [Header("Hire reinforcements (gold buys TROOP time, WO-1372 Lane D)")]
        [Tooltip("GOLD price per remaining minute to hire reinforcements on a TRAINING job - the same " +
                 "anchored curve as the crystal instant-finish, in gold. 0 disables the gold hire " +
                 "(crystal skips on the other channels are unaffected).")]
        [Min(0)] public int hireReinforcementsGoldPerMinute = 20;

        [Tooltip("Minimum gold price for any hire (so a nearly-trained unit still costs something).")]
        [Min(0)] public int hireReinforcementsMinGold = 50;

        // ---------------------------------------------------------------------
        //  CONVEX SKIP CURVE -- OWNER RULING 2026-08-21.
        //
        //  THE PROBLEM IT FIXES, measured: under the old LINEAR price (crystals =
        //  minutes) the whole early ladder skipped for 3-31 crystals. That is
        //  effectively free, so the paid skip stopped being a choice and became a
        //  reflex -- and it cancelled the very tier ladder this file had just
        //  extended. A timer nobody feels is not a pacing device.
        //
        //  THE SHAPE: price = crystalsPerMinute x anchorMinutes x (minutes/anchorMinutes)^e
        //  anchored at the 24h maxDurationSeconds clamp, so:
        //    * e = 1 reproduces TODAY'S LINEAR PRICE EXACTLY (the change is opt-in
        //      by exponent, and the old behaviour is still reachable and testable);
        //    * at the anchor the price is UNCHANGED at crystalsPerMinute x 1440,
        //      so instantFinishCrystalsPerMinute keeps its exact current meaning
        //      ("crystals for a full-length skip, per minute") and the ceiling the
        //      owner called sane does not move;
        //    * below the anchor, e < 1 lifts short skips toward the curve.
        //  Effect at e = 0.75: T0 3->10, T2 8->29, T3 25->68, T4 79->163, T5 252->390,
        //  T6 806->932 (+16%), T7 1440 UNCHANGED.
        //
        //  ⚠ A NAMING NOTE, because the word matters when someone retunes this later:
        //  the ruling says "convex", and the behaviour it describes -- short waits
        //  cheap-but-not-trivial, long waits unchanged -- is what is implemented here.
        //  Strictly, the TOTAL-price curve that produces that behaviour is SUB-LINEAR
        //  (concave, e < 1); it is the price PER MINUTE that is convex and falling, so
        //  a short skip pays a premium RATE (13.3 cr/min at T0 vs 1.0 at T7). A
        //  genuinely convex TOTAL curve (e > 1) would do the OPPOSITE of the ruling --
        //  it would drive short skips toward zero, which is the exact defect being
        //  fixed. Hence the exponent is clamped to (0, 1] and the oracle FAILS at
        //  e >= 1. Do not "correct" this to e > 1 on the strength of the word alone.
        //
        //  ONE WALLET, DELIBERATELY (owner ruling, same day): skips and the crystal-
        //  priced content ladders share one currency, so skipping a timer really does
        //  defund the Cathedral. That tension is the product, not a bug to design out
        //  -- the curve exists to make it read as a decision rather than a reflex. A
        //  separate skip currency was explicitly REJECTED, on the same day the store
        //  pass removed glimmer for not being real. Do not reintroduce one here.
        [Tooltip("Exponent of the instant-finish price curve, anchored at the maxDurationSeconds " +
                 "clamp. 1 = the old linear price. Below 1 makes SHORT skips cost relatively more " +
                 "while leaving a full-length skip's price untouched. Must stay in (0, 1].")]
        [Range(0.1f, 1f)] public float instantFinishCurveExponent = 0.75f;

        // Originally 5 seconds. The retention-first 2026-08-21 pass keeps the first-build
        // grace but raises it to 15 seconds so discovery stays quick without feeling disposable.
        //
        // WHAT THE CATALOG ACTUALLY SAYS (checked before implementing, not assumed): NOTHING in the
        // game is free. All 29 costed structures-catalog entries have a non-zero basket -- the
        // cheapest is deco_torch at 5 wood, then wall_wood at 20. A literal "if cost == 0" rule
        // would fire on ZERO structures. So the rule is keyed to what she actually meant: the FIRST
        // time the player places a given structure, which the v36 ever-built ledger already tracks.
        //
        // This is deliberately NOT the same idea as BuildModeController's note that "a freebie does
        // not make a build instant" (it keys the tier off CostFor, not EffectiveCostFor). That guard
        // is about DISCOUNTS not shortening timers, and it stands. This is about ONBOARDING PACE:
        // the first of anything is snappy, every one after it pays the real curve.
        [Header("First-build grace (onboarding pace)")]
        [Tooltip("Seconds for the FIRST build of a structure id the player has never built before. " +
                 "0 disables the grace (every build pays the normal tier curve).")]
        [Min(0f)] public float firstBuildSeconds = 15f;

        [Header("Build slots (concurrency / scarcity)")]
        [Tooltip("How many jobs may run at once for free. CoC-style scarcity — extra slots are a future unlock/purchase.")]
        [Min(1)] public int freeBuildSlots = 2;

        [Tooltip("Seconds granted by the one-time temporary Builder taste. Default: 24 hours. " +
                 "The grant is non-stacking; an active window cannot be extended or overlapped.")]
        [Min(0f)] public float temporaryBuilderSeconds = 24f * 60f * 60f;

        // WO-1388 - the PACK-sold temporary crew is a DIFFERENT PRODUCT from the one-time
        // 24 h taste above: it is repeatable (every purchase is a window), it is 6 h, and a
        // second purchase inside a running window is deferred behind it, never burned.
        // Do NOT repurpose temporaryBuilderSeconds for it - the crystal/free taste path reads that.
        // The live value rides the tunables rail as RemoteTunables.KeyEconomyPackTemporaryBuilderSeconds;
        // ConvenienceRedeemer resolves the knob and falls back to THIS field only when the knob
        // is at its shipping default, so the two cannot disagree in a way that matters.
        [Tooltip("Seconds of extra Builder crew granted by ONE 'temporary-builder' pack token (WO-1388). " +
                 "Default: 6 hours. Repeatable per purchase; a purchase inside a running window is deferred.")]
        [Min(0f)] public float packTemporaryBuilderSeconds = 6f * 60f * 60f;

        // ─────────────────────────────────────────────────────────────────────
        //  WO-911 (M1) — QUEUE DEPTH. A DIFFERENT AXIS FROM freeBuildSlots.
        //  -------------------------------------------------------------------
        //  Owner: "max of five things in the queue, maybe upgradable later"
        //  (ruled Q4: 5 TOTAL PER LINE — per channel, NOT global).
        //
        //  ⚠ DEPTH is how many items may be LINED UP on one channel (active +
        //  pending). CONCURRENCY (freeBuildSlots, above) is how many run AT ONCE.
        //  DO NOT implement the cap of 5 by raising freeBuildSlots: that would
        //  hand the player five simultaneous builders, collapse the CoC scarcity
        //  the timer economy rests on, and delete the waiting pain the crystal
        //  Finish-Now sink exists to monetize. freeBuildSlots is also oracle-
        //  pinned at 2 (BuildEconomyRegression). See WO-911 section 2d.
        //
        //  Authored as DATA so "upgradable later" is a data change, not a code
        //  change. The per-player lever on top of this is the Echo-gated
        //  purchased slot (BuildTimerService.TryBuySlot, ruling Q6), which
        //  raises DEPTH and CONCURRENCY together via ChannelState.BoughtSlots.
        // ─────────────────────────────────────────────────────────────────────
        [Header("Queue depth (WO-911 — line length, NOT concurrency)")]
        [Tooltip("Max items lined up on ONE channel (active + pending). Owner ruling Q4: 5 per line. 0 = uncapped.")]
        [Min(0)] public int queueDepthPerLine = 5;

        [Header("Extra queue slot (Echo-gated crystal sink, WO-911 Q6/Q7)")]
        [Tooltip("Crystals for the FIRST purchased slot on a channel. Each further slot on that channel costs this x (1 + slots already bought).")]
        [Min(0)] public int extraSlotBaseCrystals = 250;

        [Tooltip("Echoes the player must own BEFORE the first extra slot may be bought. Ruling Q6: 'each Echo above 2'.")]
        [Min(0)] public int extraSlotEchoFloor = 2;

        // ─────────────────────────────────────────────────────────────────────
        //  BUILDING-PERK RESEARCH — the WC3 timed-research curve (owner ruling
        //  2026-08-07: "building perk research must be TIME-BASED, like WC3").
        //  -------------------------------------------------------------------
        //  WHAT THE DATA ACTUALLY CARRIES (checked at source before authoring a
        //  number, not assumed): BuildingPerkDef has id/name/effect/goldCost/
        //  iconId/isSignature/modifiers and NOTHING ELSE - there is no
        //  researchSeconds field in building-tiers.json, in either copy
        //  (Assets/Resources/... and Assets/StreamingAssets/...). BuildingTierDef
        //  carries no duration either. So the ONLY per-perk signal that already
        //  exists is goldCost, and the curve is derived from it here rather than
        //  hard-coded at the call site (WO-172 constraint: "all durations in a
        //  tunable SO/constants, never hard-coded").
        //
        //  Authoring a researchSeconds field per perk is the RIGHT long-term
        //  home and is deliberately NOT done here: it is a content pass across
        //  16 perks x 2 json copies plus a catalog-shape change, i.e. an owner
        //  decision, not a side effect of making research timed. When it lands,
        //  BuildingPerkService.ResearchSeconds prefers the authored value and
        //  this curve becomes the fallback - one call site to change.
        //
        //  THE BAND THIS PRODUCES against the LIVE catalog (goldCost 250..2000):
        //    250g -> 3m 30s | 300g -> 4m | 600g -> 7m | 800g -> 9m
        //    1200g -> 13m   | 1600g -> 17m | 2000g -> 21m
        //  Early perks are a coffee break; the tier-3 signature capstones are a
        //  real session-length wait, which is what makes the crystal Finish-Now
        //  sink (1 crystal/minute) and the 10-minute ad chunk meaningful here.
        //  Deliberately SHORTER than a building upgrade of comparable price:
        //  research is a parallel Research-channel line the player runs
        //  alongside their builders, not the main-line time sink.
        // ─────────────────────────────────────────────────────────────────────
        [Header("Building-perk research (WC3 timed research)")]
        [Tooltip("Flat floor added to every building-perk research, in seconds. Guarantees research is " +
                 "never felt as instant even for the cheapest perk. 0 = no floor.")]
        [Min(0f)] public float researchBaseSeconds = 60f;

        [Tooltip("Seconds of research time per 1 gold of the perk's authored goldCost. The whole per-perk " +
                 "curve: seconds = researchBaseSeconds + goldCost * this, clamped to maxDurationSeconds.")]
        [Min(0f)] public float researchSecondsPerGold = 0.6f;

        /// <summary>
        /// Hybrid curve: duration (seconds) for a <paramref name="tier"/> job of
        /// <paramref name="type"/>. Super-linear via <see cref="tierGrowth"/>,
        /// clamped to <see cref="maxDurationSeconds"/>. Tier is the TARGET level
        /// (0 = first build / first upgrade step).
        /// </summary>
        public float DurationSecondsForTier(int tier, BuildJobKind type)
        {
            if (tier < 0) tier = 0;
            float seconds = baseBuildSeconds * Mathf.Pow(Mathf.Max(1f, tierGrowth), tier);
            if (type == BuildJobKind.Upgrade) seconds *= Mathf.Max(0f, upgradeMultiplier);
            return Mathf.Min(seconds, Mathf.Max(0f, maxDurationSeconds));
        }

        /// <summary>
        /// WO-855 sec.4 resource BASKET -- the one weighted scalar the work order defines for
        /// comparing costs across the four axes: <c>wood + 1.5*iron + 1.0*food + 2.0*crystals</c>.
        /// Pure; used as the structure's economic WEIGHT when deriving its build tier.
        /// </summary>
        public static float CostBasket(ResourceCost cost)
            => cost.wood + 1.5f * cost.iron + 1.0f * cost.stone + 2.0f * cost.crystals;

        /// <summary>
        /// WO-855 Phase 4 -- the BUILD TIER for a structure of <paramref name="cost"/>.
        /// -----------------------------------------------------------------------------
        /// Before WO-855 the placement path passed a hard-coded literal 0 here, so every
        /// structure in the game built in exactly <see cref="baseBuildSeconds"/> and the whole
        /// <see cref="tierGrowth"/> curve was unreachable dead tuning. There is NO
        /// <c>repo.buildSeconds</c> / <c>repo.tier</c> field on RepoProps (checked at source --
        /// adding one is forbidden by WO-855's "check before adding fields"), so the tier is
        /// derived from the structure's own AUTHORED COST BASKET: the cheap founding shed is
        /// tier 0 (snappy), the expensive endgame building is a high tier (the hours-long drag),
        /// and the whole ladder re-tunes itself for free when the Phase 2/3 JSON cost pass lands.
        /// Returns 0..<c>tierCostThresholds.Length</c>; a null/empty threshold table degrades to
        /// tier 0 (the pre-WO-855 behaviour) rather than throwing.
        /// </summary>
        public int TierForCost(ResourceCost cost)
        {
            var bands = tierCostThresholds;
            if (bands == null || bands.Length == 0) return 0;
            float basket = CostBasket(cost);
            int tier = 0;
            for (int i = 0; i < bands.Length; i++)
                if (basket >= bands[i]) tier = i + 1;
                else break;                       // ascending table -- first miss ends the climb
            return tier;
        }

        /// <summary>
        /// The highest tier <see cref="TierForCost"/> can ever return (= the threshold count).
        /// The oracle asserts the <see cref="maxDurationSeconds"/> clamp holds HERE, at the top
        /// of the REACHABLE ladder, not at an arbitrary tier number.
        /// </summary>
        public int MaxReachableTier => tierCostThresholds != null ? tierCostThresholds.Length : 0;

        /// <summary>
        /// Wall-clock seconds one building-perk research takes, derived from the perk's authored
        /// <c>goldCost</c> (the only per-perk signal building-tiers.json carries — see the block
        /// comment above). <c>researchBaseSeconds + goldCost * researchSecondsPerGold</c>, clamped
        /// to <see cref="maxDurationSeconds"/>. A negative/zero gold cost still pays the base floor,
        /// so a free perk is a short wait rather than an instant grant.
        /// </summary>
        public float ResearchSecondsForGold(int goldCost)
        {
            float seconds = Mathf.Max(0f, researchBaseSeconds)
                          + Mathf.Max(0, goldCost) * Mathf.Max(0f, researchSecondsPerGold);
            return Mathf.Min(seconds, Mathf.Max(0f, maxDurationSeconds));
        }

        /// <summary>
        /// Crystal price to instant-finish a job with <paramref name="remainingSeconds"/> left.
        /// Prices the WAIT along the convex skip curve documented at
        /// <see cref="instantFinishCurveExponent"/> — it never decides how LONG anything takes.
        /// <see cref="TierForCost"/> remains the single duration authority; this reads the
        /// remaining time it produced and charges for cutting it short.
        /// </summary>
        public int InstantFinishPrice(double remainingSeconds)
            => SkipPrice(remainingSeconds, instantFinishCrystalsPerMinute, instantFinishMinCrystals);

        /// <summary>
        /// WO-1372 Lane D — GOLD price to hire reinforcements on a TRAINING job with
        /// <paramref name="remainingSeconds"/> left. The SAME curve as <see cref="InstantFinishPrice"/>
        /// (one <see cref="SkipPrice"/>, two knob pairs): with identical knobs the two prices are
        /// identical, which HireReinforcementsRegression case 2 pins. Floored at
        /// <see cref="hireReinforcementsMinGold"/>; 0 when <see cref="hireReinforcementsGoldPerMinute"/>
        /// is 0 (the gold hire disabled).
        /// </summary>
        public int HireReinforcementsPrice(double remainingSeconds)
            => SkipPrice(remainingSeconds, hireReinforcementsGoldPerMinute, hireReinforcementsMinGold);

        /// <summary>
        /// THE ONE SKIP CURVE. <see cref="InstantFinishPrice"/> (crystals, every non-Train kind) and
        /// <see cref="HireReinforcementsPrice"/> (gold, TrainTroop) are this function with their own
        /// knob pair — never a copy of the maths. Shape and exponent are documented at
        /// <see cref="instantFinishCurveExponent"/>.
        /// </summary>
        private int SkipPrice(double remainingSeconds, int perMinute, int minimum)
        {
            if (perMinute <= 0) return 0;   // paid skip disabled
            double minutes = Mathf.Max(0f, (float)remainingSeconds) / 60.0;
            if (minutes <= 0.0) return minimum;

            // Anchor at the 24h clamp so a full-length skip keeps its old linear price and
            // perMinute keeps its old meaning ("price for a full-length skip, per minute").
            // Guarded so a zeroed or negative maxDurationSeconds degrades to the linear price
            // instead of dividing by zero / NaN-ing the store: a broken config must never make
            // a skip FREE.
            double anchorMinutes = Mathf.Max(1f, maxDurationSeconds) / 60.0;
            double e = Mathf.Clamp(instantFinishCurveExponent, 0.1f, 1f);

            double price = perMinute * anchorMinutes
                         * Math.Pow(minutes / anchorMinutes, e);
            if (double.IsNaN(price) || double.IsInfinity(price))
                price = minutes * perMinute;   // linear fallback, never free

            return Mathf.Max(Mathf.CeilToInt((float)price), minimum);
        }

        // Code-default fallback so the system runs with no authored asset.
        private static BuildTimerConfig s_default;

        /// <summary>A code-built default config instance (used when no asset is authored).</summary>
        public static BuildTimerConfig CreateDefault()
        {
            if (s_default != null) return s_default;
            s_default = CreateInstance<BuildTimerConfig>();
            s_default.name = "BuildTimerConfig (code default)";
            return s_default;
        }
    }

    /// <summary>
    /// Duration-curve flavour. Mirrors <see cref="DeNelle.Core.State.BuildJobType"/>
    /// but kept local so this pure-data config has no dependency direction concern.
    /// </summary>
    public enum BuildJobKind
    {
        Build = 0,
        Upgrade = 1,
    }
}
