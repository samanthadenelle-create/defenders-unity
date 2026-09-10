// =============================================================================
// RaidClaimService — the persisted "which raid bases has the player CLAIMED?" set.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// THE GAP THIS CLOSES (core-loop payoff, WO-441 Phase A/C spine): clearing a raid
// garrison (RaidGarrisonSpawner.OnCleared) had NO subscriber and NO record of the
// win — so victory -> CLAIM -> "this base is now mine" was missing and a cleared
// raid soft-locked. This static persists the claimed-raid set so the win READS as
// the player's: the OuterWorld outpost / re-entry can reflect claimed state, and a
// claimed scene flips ownership ENEMY -> PLAYER (the inverse of
// RaidGarrisonSpawner's SceneOwnership.SetEnemyOwned(true)).
//
// PERSISTENCE: PlayerPrefs, exactly mirroring the established ClaimableCamp
// convention (dotr-camp-claimed-<id>) and the WO-441 spec's named key
// (dotr-raid-owner-<id>). This is the SAME additive pattern the camp loop already
// ships — NO SaveSchema migration (the versioned save layer is risk-gated; camp/
// raid ownership has always lived in PlayerPrefs). A later WO can fold the set into
// SaveSchema v24 (OwnedOutposts) for cloud sync; until then this is local-first and
// correct.
//
// configId is the scene-config id the raid base was generated from (the baked scene
// is RaidBase_<configId>; RaidGarrisonSpawner carries the STORED id). ASCII-only.
// Canon: the village is Elarion (never Avalon).
// =============================================================================

using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.Ops;

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// The persisted set of CLAIMED raid bases (PlayerPrefs <c>dotr-raid-owner-&lt;configId&gt;</c>).
    /// Written on raid victory by <see cref="RaidVictoryController"/> and
    /// <see cref="Village2RaidController"/>; READ by those same victory paths BEFORE they
    /// claim, to decide whether this clear is the FIRST one (full payoff) or a REPEAT
    /// (diminished payoff, see <see cref="ScaleLootForClear"/>).
    ///
    /// <para>WHAT THIS SUMMARY USED TO CLAIM, AND WHY THE LINE IS WRITTEN THIS WAY NOW
    /// (defect sweep 2026-08-15): it said the set was "read by re-entry / world-state so a
    /// cleared-and-claimed base reads as the player's". Nothing read it. <c>IsClaimed</c> had
    /// no caller outside <c>MarkClaimed</c>'s own re-claim guard and <c>ClearClaim</c> had
    /// ZERO callers repo-wide, so the whole service was write-only: every raid payout was
    /// computed as though the base had never been taken, which made an Extreme base
    /// (rewardMultiplier 2.2) an infinitely repeatable full payout. A comment asserting a
    /// read that does not exist is worse than no comment - it is why the hole survived
    /// review. Only claim a reader here that you can point at by name.</para>
    /// </summary>
    public static class RaidClaimService
    {
        // +configId -> "1" once the player has cleared + claimed that raid base.
        private const string PrefOwnerKey = "dotr-raid-owner-";

        // +configId -> the UTC day key ("yyyy-MM-dd") on which this camp last PAID CRYSTALS.
        // WO-1134. A SEPARATE key from PrefOwnerKey on purpose, and the separation is the
        // whole safety property: PrefOwnerKey is a ONE-TIME, never-expiring flag that also
        // gates the next-companion unlock (RaidVictoryController :~480,
        // OutpostVictoryController :~193). Day-scoping THAT key would re-grant a companion
        // every single day, forever. So the daily axis gets its own key and touches nothing
        // the one-time axis owns.
        private const string PrefCrystalDayKey = "dotr-raid-crystalday-";

        // +resource word ("wood"/"iron"/"stone") -> the units this town is holding in the RAID
        // CACHE because the bank had no room for them. WO-1461, owner ruling 2026-09-06 20:33:
        // "Never destroy raid loot because storage is full. Put overflow into a temporary Raid
        // Cache with a modest cap."
        //
        // A THIRD prefix, and the separation is again the safety property. PrefOwnerKey is a
        // one-time per-camp flag and PrefCrystalDayKey is a per-camp day stamp; the cache is
        // neither. It is the TOWN'S, not the camp's - the player claims it from a town surface
        // after they upgrade or spend, so keying it per camp would strand a haul behind a camp
        // the player never revisits. One store, per resource, town-wide.
        private const string PrefCacheKey = "dotr-raid-cache-";

        // =====================================================================
        //  THE FIRST-CLEAR GATE  (economy-safe half; the curve is an OWNER call)
        // =====================================================================

        /// <summary>
        /// What fraction of the settled loot a REPEAT clear pays, as a 0..1 fraction.
        ///
        /// <para>⛔ THIS IS NO LONGER A <c>const</c>, AND THAT IS THE POINT (WO-1461). It read
        /// <c>0.25f</c> from 2026-08-15 until 2026-09-09 while the owner's ruling of
        /// 2026-09-06 20:33 said, verbatim: <i>"100% first clear after cooldown, 60% repeat
        /// clear during the same cycle, then reset to 100% when the camp's cooldown expires."</i>
        /// A compiled-in constant is why the two could disagree for three days and why the fix
        /// needed a rebuild. It is now a knob on the RemoteTunables rail
        /// (<c>raid.lootRepeatClearPct</c>, shipping default 60), so the next retune is a flag
        /// flip - the same rail RaidLootTunables already reads every other raid reward off.</para>
        ///
        /// <para>THE NAME AND THE TYPE ARE DELIBERATELY UNCHANGED. Both existing readers -
        /// <c>RaidVictoryController.ApplyFirstClearGate</c> (which interpolates it
        /// <c>:0.##</c>) and <c>RaidSelectionVM.ClearedWord</c> (which multiplies it by 100) -
        /// compile untouched against a static float property, so this lane re-authors a number
        /// without editing a file another lane owns.</para>
        ///
        /// <para>Crystals are on a SEPARATE axis (the once-per-UTC-day stamp, see
        /// <see cref="CrystalsPaidToday"/>) and are not governed by this multiplier.</para>
        /// </summary>
        public static float RepeatClearLootMultiplier => RepeatClearPct / 100f;

        /// <summary>
        /// The repeat-clear share as an INTEGER PERCENT, read off the rail and clamped
        /// 0..100 loudly-once (the <c>RaidLootTunables.ClampAndReport</c> contract, copied in
        /// shape rather than reached for across a file this lane does not own).
        ///
        /// <para>The upper clamp is 100, not 1000: a repeat clear that paid MORE than a first
        /// clear would invert the whole first-clear gate, so a typo in the operator console
        /// must degrade to "pays full", never to "pays double".</para>
        /// </summary>
        public static int RepeatClearPct
        {
            get
            {
                int raw = RemoteTunables.Int(RemoteTunables.KeyRaidLootRepeatClearPct);
                int clamped = Mathf.Clamp(raw, 0, 100);
                if (clamped != raw)
                    FlowTrace.Once("Raid", "raid-repeatclear-clamp",
                        "repeat-clear knob '" + RemoteTunables.KeyRaidLootRepeatClearPct +
                        "' resolved to " + raw + ", outside 0..100 - CLAMPED to " + clamped +
                        ". A repeat clear may never pay more than a first clear.");
                return clamped;
            }
        }

        /// <summary>
        /// Is a clear of <paramref name="configId"/> happening INSIDE the camp's current
        /// cooldown cycle - i.e. does the repeat multiplier apply to it?
        ///
        /// <para>⛔ THIS IS NOT <see cref="IsClaimed"/>, AND THE DIFFERENCE IS THE OWNER'S
        /// RULING. <c>IsClaimed</c> is PERMANENT and never expires (WO-1134 records why: it
        /// also gates the next-companion unlock, so day-scoping it would re-grant a companion
        /// forever). Read alone it says "every clear after the first pays the reduced rate,
        /// for the life of the save" - which is the 0.25 behaviour the player met. Her ruling
        /// resets to 100% "when the camp's cooldown expires", so the cycle question is
        /// <c>RaidCooldownService</c>'s, and this is the AND of the two: a prior clear is on
        /// record AND that clear's cooldown window is still running.</para>
        ///
        /// <para><c>RaidCooldownService</c> was RETIRED AS A GATE by WO-1379 (Heartfire is the
        /// one door), but its STAMP survives on every clear and its header says so in as many
        /// words. This is a READ of that surviving record, not a second entry gate: nothing
        /// here refuses a raid, paints "Recovering", or touches
        /// <c>RaidSelectionScreen</c> - HeartfireRegression PIN F reds that file, not this one.</para>
        ///
        /// <para>A camp with no cooldown record answers FALSE (first clear, full pay), which
        /// is also the correct answer for a save that predates the stamp.</para>
        /// </summary>
        public static bool IsRepeatClearInCycle(string configId)
        {
            if (string.IsNullOrEmpty(configId)) return false;
            bool claimed = IsClaimed(configId);
            // SHORT-CIRCUIT ON PURPOSE, and not merely for speed: an UNCLAIMED camp has no
            // cycle to be inside, and reading the cooldown record on one costs a
            // "no GameState available" Warn from RaidCooldownService.RemainingSeconds on every
            // headless frame - noise that would bury the raid trace this line exists to annotate.
            bool inCycle = claimed && RaidCooldownService.IsOnCooldown(configId);
            bool repeat = claimed && inCycle;
            FlowTrace.Step("Raid",
                "RaidClaimService.IsRepeatClearInCycle('" + configId + "') claimed=" + claimed +
                " insideCooldownCycle=" + (claimed ? inCycle.ToString() : "(not read - unclaimed)") +
                " -> " + (repeat
                    ? "REPEAT: this clear pays " + RepeatClearPct + "% of ordinary loot."
                    : "FULL: this clear pays 100% of ordinary loot (owner ruling 2026-09-06 - the " +
                      "share resets to 100% once the camp's cooldown expires)."));
            return repeat;
        }

        // =====================================================================
        //  THE RAID CACHE  (WO-1461, owner ruling 2026-09-06 20:33)
        // =====================================================================

        /// <summary>
        /// The per-resource ceiling on the Raid Cache, read off the rail
        /// (<c>raid.cacheCapPerResource</c>) and clamped 0..1000000.
        ///
        /// <para>⚠ THE VALUE IS NOT OWNER-RULED AND THIS FILE DOES NOT PRETEND IT IS. She said
        /// "a modest cap" and gave no number. The shipping default is 1800 - EXACTLY one
        /// perfect Camp I wood haul (<c>raid.lootWoodBase</c>) - chosen so the cache holds AT
        /// MOST one raid's worth of any one resource and therefore cannot become a second,
        /// larger bank. It is registered as a knob precisely so her number replaces it with a
        /// flag flip and no rebuild. Flagged in WO-1461's RESULT as unruled.</para>
        ///
        /// <para>Why a cap at all, in her own framing: an unbounded cache removes the upgrade
        /// pressure the cache exists to create (WO-1461 section 4). Above the cap the units
        /// are still refused - but they are refused by a stated ceiling the player can raise,
        /// not deleted by a silent one.</para>
        /// </summary>
        public static int CacheCapPerResource
        {
            get
            {
                int raw = RemoteTunables.Int(RemoteTunables.KeyRaidCacheCapPerResource);
                int clamped = Mathf.Clamp(raw, 0, 1000000);
                if (clamped != raw)
                    FlowTrace.Once("Raid", "raid-cachecap-clamp",
                        "raid cache knob '" + RemoteTunables.KeyRaidCacheCapPerResource +
                        "' resolved to " + raw + ", outside 0..1000000 - CLAMPED to " + clamped + ".");
                return clamped;
            }
        }

        /// <summary>
        /// Where one axis of a settled payout actually GOES. Three numbers that must always
        /// sum to the amount asked for, which is the whole law this ticket exists to restore:
        /// <c>Banked + Cached + Refused == amount</c>.
        ///
        /// <para><see cref="Refused"/> is deliberately not called "lost". Under WO-1434's
        /// retention law the bank REFUSES units, it does not destroy them; the only way a unit
        /// leaves the world here is by exceeding BOTH the bank's headroom and the cache's
        /// stated ceiling, and the player can raise the second one.</para>
        /// </summary>
        public struct SpoilsDestiny
        {
            /// <summary>Units the bank has room for right now.</summary>
            public int Banked;
            /// <summary>Units the Raid Cache holds instead, up to its remaining room.</summary>
            public int Cached;
            /// <summary>Units above BOTH ceilings. Zero whenever the cache has room.</summary>
            public int Refused;
        }

        /// <summary>
        /// Split ONE axis of a payout across the bank and the cache. Pure, static, no
        /// PlayerPrefs and no scene - so the oracle asserts the law with nothing loaded, and
        /// so the deploy card and the settle path can compute the SAME split from the same
        /// method rather than each deriving one.
        ///
        /// <para>Negative inputs are treated as zero; an uncapped resource (crystals, coins)
        /// is expressed by passing <c>int.MaxValue</c> as <paramref name="bankRoom"/>, which
        /// banks the whole amount and caches nothing.</para>
        /// </summary>
        public static SpoilsDestiny SplitAxis(int amount, int bankRoom, int cacheRoom)
        {
            var d = new SpoilsDestiny();
            if (amount <= 0) return d;
            if (bankRoom < 0) bankRoom = 0;
            if (cacheRoom < 0) cacheRoom = 0;

            d.Banked = bankRoom >= amount ? amount : bankRoom;
            int over = amount - d.Banked;
            d.Cached = cacheRoom >= over ? over : cacheRoom;
            d.Refused = over - d.Cached;
            return d;
        }

        /// <summary>Units currently held in the Raid Cache for one resource.</summary>
        public static int Cached(BankResource r)
        {
            int held = PlayerPrefs.GetInt(PrefCacheKey + TownBankCapacity.WordOf(r), 0);
            return held < 0 ? 0 : held;
        }

        /// <summary>Room left in the Raid Cache for one resource (cap minus what it holds).</summary>
        public static int CacheRoomFor(BankResource r)
        {
            int room = CacheCapPerResource - Cached(r);
            return room < 0 ? 0 : room;
        }

        /// <summary>
        /// RETAIN the part of a settled raid payout the bank refused, instead of burning it.
        ///
        /// <para>Call this AFTER the grant, handing in what was ASKED FOR and what the wallet
        /// MEASURED as credited - <c>RaidVictoryController.GrantLoot</c> already computes both
        /// (<c>loot</c> and its measured <c>_credited</c> basket). The shortfall per axis is
        /// the amount the bank refused; each axis is pushed into the cache up to its remaining
        /// room and the remainder is reported by name.</para>
        ///
        /// <para>ONLY THE CAPPED AXES ARE CACHED. <c>TownBankCapacity.IsCapped</c> is false for
        /// crystals and coins by owner ruling (WO-901 section 6, Law 1) - they are never
        /// clamped, so a shortfall on those axes is not an overflow and caching it would
        /// invent a second wallet for a currency that has no ceiling.</para>
        ///
        /// <para>Returns what was retained, so the caller can put the number on the victory
        /// screen. Idempotent it is NOT - call it exactly once per settle.</para>
        /// </summary>
        public static ResourceCost RetainOverflow(string configId, ResourceCost requested, ResourceCost credited)
        {
            int rw = RetainAxis(configId, BankResource.Wood, requested.Wood, credited.Wood);
            int ri = RetainAxis(configId, BankResource.Iron, requested.Iron, credited.Iron);
            int rf = RetainAxis(configId, BankResource.Food, requested.Food, credited.Food);

            var retained = new ResourceCost(wood: rw, food: rf, iron: ri);
            if (rw > 0 || ri > 0 || rf > 0)
            {
                PlayerPrefs.Save();
                FlowTrace.Step("Raid",
                    "RAID CACHE: the bank refused part of '" + (configId ?? "(none)") + "' payout and it was " +
                    "RETAINED, not burned - wood " + rw + " / iron " + ri + " / stone " + rf +
                    ". Cache now holds wood " + Cached(BankResource.Wood) + " / iron " +
                    Cached(BankResource.Iron) + " / stone " + Cached(BankResource.Food) +
                    " against a per-resource cap of " + CacheCapPerResource +
                    ". Upgrade or spend, then claim it.");
            }
            else
            {
                FlowTrace.Step("Raid",
                    "RAID CACHE: nothing to retain for '" + (configId ?? "(none)") + "' - the bank took the " +
                    "whole payout on every capped axis.");
            }
            return retained;
        }

        private static int RetainAxis(string configId, BankResource r, int requested, int credited)
        {
            if (!TownBankCapacity.IsCapped(r)) return 0;
            if (requested <= 0) return 0;
            if (credited < 0) credited = 0;

            int refusedByBank = requested - credited;
            if (refusedByBank <= 0) return 0;

            int room = CacheRoomFor(r);
            int retained = room >= refusedByBank ? refusedByBank : room;
            int stillRefused = refusedByBank - retained;

            if (retained > 0)
                PlayerPrefs.SetInt(PrefCacheKey + TownBankCapacity.WordOf(r), Cached(r) + retained);

            if (stillRefused > 0)
                FlowTrace.Warn("Raid",
                    "RAID CACHE FULL for " + TownBankCapacity.WordOf(r) + " on '" + (configId ?? "(none)") +
                    "': the bank refused " + refusedByBank + ", the cache took " + retained +
                    " and " + stillRefused + " is above BOTH ceilings. This is the ONLY path on which a raid " +
                    "unit leaves the world, and it is bounded by a cap the player can raise (knob '" +
                    RemoteTunables.KeyRaidCacheCapPerResource + "' = " + CacheCapPerResource +
                    "), never by a silent burn.");

            return retained;
        }

        /// <summary>
        /// CONSUME units out of the Raid Cache - the claim door's half. The caller grants them
        /// to the wallet FIRST and hands back what the wallet actually took, exactly as
        /// <c>EchoService.DumpSilos</c> settles the APPLIED basket rather than the requested
        /// one (WO-1392): a cache decremented by what was asked for, against a bank that was
        /// still full, would burn the haul a second time on the way out.
        /// </summary>
        public static void ConsumeCached(BankResource r, int applied)
        {
            if (applied <= 0) return;
            int held = Cached(r);
            int take = applied > held ? held : applied;
            PlayerPrefs.SetInt(PrefCacheKey + TownBankCapacity.WordOf(r), held - take);
            PlayerPrefs.Save();
            FlowTrace.Step("Raid",
                "RAID CACHE: claimed " + take + " " + TownBankCapacity.WordOf(r) +
                " (asked " + applied + ", held " + held + ") - " + (held - take) + " still held.");
        }

        /// <summary>
        /// Test/dev hook: set one axis of the Raid Cache directly. Exercised by
        /// <c>SpoilsAreBankableRegression</c>, which round-trips every cache axis on a scratch
        /// value and restores the pref. (The claim set went write-only for months because its
        /// reset hook had zero callers - an unexercised hook proves nothing.)
        /// </summary>
        public static void SetCached(BankResource r, int units)
        {
            if (units < 0) units = 0;
            PlayerPrefs.SetInt(PrefCacheKey + TownBankCapacity.WordOf(r), units);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Scales a settled raid payout by the raid loot gates. TWO INDEPENDENT AXES, and they
        /// are deliberately separate parameters rather than one overloaded flag:
        ///
        /// <list type="bullet">
        /// <item><description><paramref name="isRepeatClear"/> — the ORDINARY-RESOURCE axis.
        /// A first clear pays wood/food/iron/coins in full; a re-clear of an already-claimed
        /// base pays <see cref="RepeatClearLootMultiplier"/> of them (rounded DOWN, so the gate
        /// can never round a repeat back up to a full unit).</description></item>
        /// <item><description><paramref name="crystalsAlreadyPaidToday"/> — the CRYSTAL axis
        /// (WO-1134, owner ruling). Crystals are paid on the FIRST clear of each UTC DAY and
        /// zero for every further clear that day. They reset the next day even on a base that
        /// has been claimed for months.</description></item>
        /// </list>
        ///
        /// <para>⛔ DO NOT COLLAPSE THESE INTO ONE FLAG. They answer different questions and
        /// they cross: the second clear of day one is <c>repeat + paid</c> (reduced resources,
        /// no crystals), while the first clear of day two is <c>repeat + NOT paid</c> — reduced
        /// resources but FULL crystals. One boolean cannot express that, and the previous
        /// signature (which hardcoded <c>crystals: 0</c> on any repeat) got the day-two case
        /// silently wrong.</para>
        ///
        /// <para>Pure + static: no PlayerPrefs, no scene, no singleton - so a regression can
        /// assert the gate's arithmetic with nothing loaded. The CALLER decides which case it
        /// is, because both persisted flags are flipped during victory handling: read
        /// <see cref="IsClaimed"/> and <see cref="CrystalsPaidToday"/> BEFORE <c>MarkClaimed</c>
        /// / <c>MarkCrystalsPaid</c> or every clear reads as a repeat that has already paid.</para>
        /// </summary>
        public static ResourceCost ScaleLootForClear(ResourceCost loot, bool isRepeatClear, bool crystalsAlreadyPaidToday)
        {
            // The crystal axis is resolved first and independently of the resource axis.
            int crystals = crystalsAlreadyPaidToday ? 0 : loot.Crystals;

            if (!isRepeatClear)
            {
                if (crystals == loot.Crystals) return loot;
                return new ResourceCost(
                    wood: loot.Wood, food: loot.Food, iron: loot.Iron,
                    crystals: crystals, coins: loot.Coins);
            }

            float m = RepeatClearLootMultiplier;
            if (m >= 1f) m = 1f;        // defensive: a mis-set knob must never PAY MORE than the first clear
            if (m < 0f)  m = 0f;

            // Re-clears remain useful for army practice and food recovery. Crystals are NOT
            // scaled by m — they are all-or-nothing on the day stamp, because a fractional
            // premium payout is exactly the kind of number that quietly becomes a faucet.
            return new ResourceCost(
                wood:     Mathf.FloorToInt(loot.Wood     * m),
                food:     Mathf.FloorToInt(loot.Food     * m),
                iron:     Mathf.FloorToInt(loot.Iron     * m),
                crystals: crystals,
                coins:    Mathf.FloorToInt(loot.Coins    * m));
        }

        // =====================================================================
        //  THE CRYSTAL DAY-STAMP  (WO-1134, owner ruling)
        // =====================================================================

        /// <summary>
        /// True if this camp has ALREADY paid its crystals during the current UTC day, so this
        /// clear must pay zero crystals. False on the first clear of a new day — including on a
        /// base claimed long ago, which is the whole point of the ruling.
        ///
        /// <para>WHY A DAY STAMP AND NOT THE CLAIM FLAG: crystals were the one unbounded faucet
        /// in the game, and the cooldown alone bounded them at ~2 clears/day. Under this stamp
        /// the SECOND clear of a day pays none, so the DAY — not the cooldown — is now the
        /// crystal bound.</para>
        ///
        /// <para>⚠ CORRECTED 2026-09-04 (WO-1374): this used to say
        /// "RaidScoring.ComputeLoot pays FOOD and CRYSTALS only". It no longer does — that
        /// method now also pays WOOD and IRON off the north-star map's performance ladder.
        /// The day-stamp reasoning above is UNCHANGED and still correct, because the stamp
        /// governs the crystal axis alone; only the parenthetical fact had gone stale. The
        /// wood/iron axis is bounded by the repeat-clear multiplier below, not by this
        /// stamp. GOLD is still not paid at all, by WO-1374's fence.</para>
        ///
        /// <para>Read this BEFORE <see cref="MarkCrystalsPaid"/>, exactly as
        /// <see cref="IsClaimed"/> is read before <c>MarkClaimed</c>: the stamp is written
        /// during victory handling, so a read taken afterwards always says "already paid".</para>
        ///
        /// <para>PlayerPrefs, not the save file — same local-first convention as the claim set
        /// above, so this needs NO SaveSchema bump (a schema bump is an owner decision).</para>
        /// </summary>
        public static bool CrystalsPaidToday(string configId)
        {
            if (string.IsNullOrEmpty(configId)) return false;
            string stamped = PlayerPrefs.GetString(PrefCrystalDayKey + configId, string.Empty);
            return !string.IsNullOrEmpty(stamped) && stamped == UtcDay.Key();
        }

        /// <summary>
        /// Stamp this camp as having PAID CRYSTALS today (UTC). Idempotent within a day, and
        /// self-expiring: tomorrow's <see cref="CrystalsPaidToday"/> compares against a
        /// different key and reports false, so nothing ever has to clean this up.
        ///
        /// <para>Call this AFTER the loot has been granted — a stamp written before a grant
        /// that then throws would burn the player's crystal day for nothing.</para>
        /// </summary>
        public static void MarkCrystalsPaid(string configId)
        {
            if (string.IsNullOrEmpty(configId))
            {
                FlowTrace.Warn("Raid", "RaidClaimService.MarkCrystalsPaid: empty configId - crystal day NOT stamped.");
                return;
            }
            string day = UtcDay.Key();
            PlayerPrefs.SetString(PrefCrystalDayKey + configId, day);
            PlayerPrefs.Save();
            FlowTrace.Step("Raid", $"RaidClaimService: crystal day-stamp SET for '{configId}' = {day} " +
                                   $"(persisted dotr-raid-crystalday-{configId}). Further clears today pay 0 crystals.");
        }

        /// <summary>
        /// Test/dev hook: drop the crystal day-stamp so the camp can pay crystals again today.
        /// Exercised by <c>RaidRepeatClearRegression</c>, which round-trips the stamp on a
        /// scratch id and restores the pref. (The claim set went write-only for months because
        /// its reset hook had zero callers - an unexercised hook proves nothing.)
        /// </summary>
        public static void ClearCrystalDayStamp(string configId)
        {
            if (string.IsNullOrEmpty(configId)) return;
            PlayerPrefs.DeleteKey(PrefCrystalDayKey + configId);
            PlayerPrefs.Save();
            FlowTrace.Step("Raid", $"RaidClaimService: crystal day-stamp on '{configId}' CLEARED.");
        }

        /// <summary>True once the player has claimed the raid base with this config id.</summary>
        public static bool IsClaimed(string configId)
        {
            if (string.IsNullOrEmpty(configId)) return false;
            return PlayerPrefs.GetString(PrefOwnerKey + configId, null) == "1";
        }

        /// <summary>
        /// Mark the raid base <paramref name="configId"/> CLAIMED (player-owned), persist,
        /// and report whether this was a NEW claim (true) or a no-op re-claim (false). The
        /// caller uses the "new" signal to decide whether to grant the one-time payoff
        /// (the next-companion unlock) so a re-cleared base never double-grants. Idempotent.
        ///
        /// <para>The RESOURCE payout is gated separately, and deliberately so: it is decided
        /// from an <see cref="IsClaimed"/> read taken BEFORE this call (see
        /// RaidVictoryController.HandleVictory), because by the time this returns the flag has
        /// already flipped. Do not re-derive "was this a repeat" from IsClaimed afterwards.</para>
        /// </summary>
        public static bool MarkClaimed(string configId)
        {
            if (string.IsNullOrEmpty(configId))
            {
                FlowTrace.Warn("Raid", "RaidClaimService.MarkClaimed: empty configId - not claimed.");
                return false;
            }
            if (IsClaimed(configId))
            {
                FlowTrace.Step("Raid", $"RaidClaimService: '{configId}' already claimed - no-op (no re-grant).");
                return false;
            }
            PlayerPrefs.SetString(PrefOwnerKey + configId, "1");
            PlayerPrefs.Save();
            FlowTrace.Step("Raid", $"RaidClaimService: '{configId}' CLAIMED -> player-owned (persisted dotr-raid-owner-{configId}).");
            return true;
        }

        /// <summary>
        /// Test/dev hook: drop the claim on a raid base (so it can be re-raided).
        /// Called by <c>RaidRepeatClearRegression</c>, which round-trips
        /// MarkClaimed -> IsClaimed -> ClearClaim on a scratch id and restores the pref.
        /// (It had ZERO callers before that suite, which is how the write-only claim set
        /// went unnoticed - an unexercised hook proves nothing.)
        /// </summary>
        public static void ClearClaim(string configId)
        {
            if (string.IsNullOrEmpty(configId)) return;
            PlayerPrefs.DeleteKey(PrefOwnerKey + configId);
            PlayerPrefs.Save();
            FlowTrace.Step("Raid", $"RaidClaimService: claim on '{configId}' CLEARED - the base is raidable again.");
        }
    }
}
