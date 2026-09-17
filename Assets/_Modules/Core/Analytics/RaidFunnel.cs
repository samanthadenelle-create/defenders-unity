// =============================================================================
// RaidFunnel - THE SIX EVENTS the raid programme is measured by (WO-1374,
// docs/PROGRAM_RAID_ECONOMY_2026-09-04.md section 11).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Analytics
//
// Owner, verbatim (the map's section 11):
//   "Don't start with DAU or retention percentages. Watch one simple funnel:
//    Barracks unlocked -> Army trained -> First raid attempted -> First raid won
//    -> Raid reward spent -> SECOND RAID ATTEMPTED WITHIN 24H"
//   "That last one is the gold nugget. If someone raids once and chooses to raid
//    again, your loop is beginning to work. If they don't, more tutorial text
//    probably isn't the answer."
//
// -----------------------------------------------------------------------------
// (!) THIS IS NOT A TELEMETRY PATH. IT IS A CALLER OF THE ONE WE ALREADY HAVE.
// -----------------------------------------------------------------------------
// Every emission below goes through DeNelle.Core.Analytics.EventTracker.Track,
// which batches, persists offline, retries and circuit-breaks into
// POST /api/events/track -> Neon `analytics_events`. The work order forbids a
// second rail in capitals and it is worth saying why: a parallel POST would have
// its own queue, its own outage behaviour and its own idea of playerId, so the
// funnel's six steps would be measured on a different reliability curve from
// every other event in the game - and the FIRST time the numbers disagreed,
// nobody would be able to say which half was lying.
//
// -----------------------------------------------------------------------------
// EVERY STEP IS A "FIRST", AND THE FIRSTNESS IS PERSISTED.
// -----------------------------------------------------------------------------
// A funnel counts PLAYERS reaching a step, not events. Firing "first raid won"
// on every win would make step 4 outnumber step 3 and the funnel would read as
// a >100% conversion, which is how a metric quietly stops being used. So each
// step latches in PlayerPrefs, once per install, and a re-fire is a logged no-op.
//
// PlayerPrefs (never the save file) is deliberate: this is INSTRUMENTATION about
// the install, not game state. It must not ride the save schema (no version bump,
// no migration, nothing for a save loader to get wrong), and a player who resets
// their save has still, factually, already learned the loop once.
//
// -----------------------------------------------------------------------------
// THE 24-HOUR STEP IS COMPUTED HERE, NOT ON THE SERVER.
// -----------------------------------------------------------------------------
// The first raid attempt stamps a wall-clock ms; the next DISTINCT attempt
// compares against it. It is emitted as its own named event rather than left for
// a SQL window over step 3, because the map calls it "the gold nugget" and a
// nugget that requires someone to write the right query is a nugget nobody digs.
// The raw timestamps ride along as properties anyway, so the server can always
// re-derive it and disagree out loud.
//
// (!) A CLOCK THE PLAYER OWNS. Device time can move backwards or jump; the
// window therefore accepts only a non-negative elapsed value below the bound and
// SAYS SO in the trace when it rejects one, rather than silently counting or
// silently dropping it.
//
// -----------------------------------------------------------------------------
// (!) WO-1794 - STEPS 1 AND 2 ARE GRANT-FIRED FOR EVERY FOUNDING PLAYER.
// -----------------------------------------------------------------------------
// Read the row before you read the count. On 2026-09-16 every id that reached
// founding emitted raid_funnel_barracks_unlocked AND raid_funnel_army_trained in
// the SAME SECOND as founding_path_selected, because the founding template hands
// the player a Barracks and StarterArmyGrant hands them ten troops. The metrics
// line "barracks unlocked 8 / army trained 8" therefore reads as "8 players built
// a barracks and trained an army" and MEANS "8 grants fired". The same day's
// raid_funnel_first_raid_attempted was ZERO.
//
// The six wire names are owned by WO-1374 and do not change (renaming one makes it
// read as zero forever, and re-latching one re-fires it for every install). So the
// separation is a PROPERTY: every step that a grant produced carries
// `granted = true`, and an earned one carries `granted = false`. A query that wants
// the honest on-ramp filters on it; a dashboard that sums the name is unaffected.
//
// `granted` is an EXPLICIT ARGUMENT at every call site, never inferred from the
// `source` string. Inferring it would mean a new grant path only has to invent a
// source spelling to report itself as an earned player action - which is precisely
// the failure being fixed here, one layer down.
//
// Steps 3-6 are genuine player actions (SceneRouter.GoRaid, RaidVictoryController,
// the two spend surfaces) and carry no such flag, because there is no grant that
// can reach them.
//
// ASCII only. FlowTrace tag "Funnel" - never stripped (CLAUDE.md section 12).
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Analytics
{
    /// <summary>
    /// The six raid-loop funnel steps (map section 11), emitted ONCE per install
    /// through the existing <see cref="EventTracker"/> rail. Every method is
    /// null-safe, never throws, and is a logged no-op after its step has latched.
    /// </summary>
    public static class RaidFunnel
    {
        /// <summary>FlowTrace system tag for the whole funnel.</summary>
        public const string Sys = "Funnel";

        // ── THE SIX WIRE NAMES. snake_case, EventTracker's convention. ────────
        // Prefixed `raid_funnel_` so the six can be pulled out of analytics_events
        // with one LIKE and can never be confused with an ordinary gameplay event.

        /// <summary>Step 1 - a Barracks finished, so raids are reachable at all.</summary>
        public const string EventBarracksUnlocked = "raid_funnel_barracks_unlocked";

        /// <summary>Step 2 - a troop entered the roster (trained OR granted).</summary>
        public const string EventArmyTrained = "raid_funnel_army_trained";

        /// <summary>Step 3 - the player launched a raid.</summary>
        public const string EventFirstRaidAttempted = "raid_funnel_first_raid_attempted";

        /// <summary>Step 4 - the player won a raid.</summary>
        public const string EventFirstRaidWon = "raid_funnel_first_raid_won";

        /// <summary>Step 5 - the player spent resources after that win.</summary>
        public const string EventRaidRewardSpent = "raid_funnel_raid_reward_spent";

        /// <summary>Step 6 - THE GOLD NUGGET. A second raid launched inside 24h.</summary>
        public const string EventSecondRaidWithin24h = "raid_funnel_second_raid_within_24h";

        // ── Latches. One key per step; value "1" once fired. ──────────────────
        private const string PrefPrefix = "dotr-funnel-";
        private const string KeyBarracks = PrefPrefix + "barracks";
        private const string KeyArmy = PrefPrefix + "army";
        private const string KeyAttempt = PrefPrefix + "attempt";
        private const string KeyWon = PrefPrefix + "won";
        private const string KeySpent = PrefPrefix + "spent";
        private const string KeySecond = PrefPrefix + "second";

        /// <summary>Unix ms of the FIRST raid attempt. 0 until step 3 fires.</summary>
        private const string KeyFirstAttemptMs = PrefPrefix + "first-attempt-ms";

        /// <summary>
        /// "1" between a raid win and the next spend. Step 5 is "reward spent", so a
        /// spend BEFORE any raid win is not it - the arm is what makes the ordering
        /// part of the measurement instead of a coincidence.
        /// </summary>
        private const string KeyRewardArmed = PrefPrefix + "reward-armed";

        /// <summary>The map's window for step 6, in milliseconds. Twenty-four hours.</summary>
        public const long SecondRaidWindowMs = 24L * 60L * 60L * 1000L;

        /// <summary>
        /// Every PlayerPrefs key this class owns, in funnel order. Exposed so an ORACLE can
        /// snapshot, clear and restore them around a run: a suite that drives the funnel
        /// would otherwise latch a step on the build machine and quietly make itself
        /// un-rerunnable - and a test that only passes the first time is not a test.
        /// (Same reason RemoteTunablesDefaultsRegression snapshots the ff.tun.* overrides.)
        /// </summary>
        public static readonly string[] PrefKeys =
        {
            KeyBarracks, KeyArmy, KeyAttempt, KeyWon, KeySpent, KeySecond,
            KeyFirstAttemptMs, KeyRewardArmed,
        };

        /// <summary>The six wire names, in funnel order. Read by the oracle; also the list
        /// an analytics query should pull. Order is the funnel's order, not alphabetical.</summary>
        public static readonly string[] EventNames =
        {
            EventBarracksUnlocked, EventArmyTrained, EventFirstRaidAttempted,
            EventFirstRaidWon, EventRaidRewardSpent, EventSecondRaidWithin24h,
        };

        // =====================================================================
        //  PURE - the step-6 decision, testable with no PlayerPrefs and no clock
        // =====================================================================

        /// <summary>
        /// True when a second attempt at <paramref name="nowMs"/> falls inside the
        /// 24h window opened by a first attempt at <paramref name="firstAttemptMs"/>.
        ///
        /// <para>Refuses three cases on purpose, all of which a real device produces:
        /// no first attempt recorded (0 or less), a NEGATIVE elapsed (the player moved
        /// the clock backwards, or a timezone/DST write landed between the two reads),
        /// and anything at or beyond the bound. The window is INCLUSIVE of 0 and
        /// EXCLUSIVE of the bound, so a re-raid in the same second counts and one at
        /// exactly 24h does not.</para>
        ///
        /// <para>Static + pure so an oracle can assert the whole table with nothing
        /// loaded - no scene, no save, no network.</para>
        /// </summary>
        public static bool IsWithinSecondRaidWindow(long firstAttemptMs, long nowMs)
        {
            if (firstAttemptMs <= 0L) return false;
            long elapsed = nowMs - firstAttemptMs;
            if (elapsed < 0L) return false;
            return elapsed < SecondRaidWindowMs;
        }

        // =====================================================================
        //  STEP 1 - Barracks unlocked
        // =====================================================================

        /// <summary>
        /// The player has a Barracks, so the raid door is reachable. Called from the
        /// ONE place that learns it (the Village-side starter-army bridge), never from
        /// a screen - a UI that happens to be open is not evidence of a game state.
        /// </summary>
        /// <param name="source">Which seam observed it (trace + property only).</param>
        /// <param name="granted">
        /// WO-1794 - TRUE when the Barracks arrived from a GRANT (the founding template's
        /// starter settlement), FALSE when the player built one. Required, not defaulted:
        /// the caller is the only thing that knows, and a default would let the next
        /// grant path report itself as a player action by saying nothing.
        /// </param>
        public static void BarracksUnlocked(string source, bool granted)
        {
            FireOnce(KeyBarracks, EventBarracksUnlocked, new { source = Safe(source), granted });
        }

        // =====================================================================
        //  STEP 2 - Army trained
        // =====================================================================

        /// <summary>
        /// A troop entered the roster. Fired from BarracksProgression.GrantTrainedTroop,
        /// the SINGLE owner of "a troop joins the army" - both the timed Train job's
        /// completion effect and the free starter grant pass through it, so the step
        /// cannot be reached by one path and missed by the other.
        /// </summary>
        /// <param name="granted">
        /// WO-1794 - TRUE when the troop was GRANTED (the free starter squad), FALSE when
        /// the player paid for and waited out a Train job. Required for the reason
        /// <see cref="BarracksUnlocked"/> gives.
        /// </param>
        public static void ArmyTrained(string troopId, int rosterCount, string source, bool granted)
        {
            FireOnce(KeyArmy, EventArmyTrained, new
            {
                troopId = Safe(troopId),
                rosterCount,
                source = Safe(source),
                granted,
            });
        }

        // =====================================================================
        //  STEP 3 + STEP 6 - raid attempted (and the gold nugget)
        // =====================================================================

        /// <summary>
        /// A raid was LAUNCHED. Called from SceneRouter.GoRaid - the shared contract
        /// every raid entry already funnels through - so "attempted" means the player
        /// actually committed to the raid scene, not that they opened a list and
        /// backed out.
        ///
        /// <para>Owns BOTH step 3 and step 6: the first call stamps the window open,
        /// and the first DISTINCT later call decides the nugget. Keeping them in one
        /// method is the point - two methods would need two call sites to agree about
        /// what "second" means, and they would eventually stop agreeing.</para>
        /// </summary>
        public static void RaidAttempted(string sceneName)
        {
            long now = NowMs();
            bool wasFirst = !Latched(KeyAttempt);

            FireOnce(KeyAttempt, EventFirstRaidAttempted, new
            {
                scene = Safe(sceneName),
                attemptMs = now,
            });

            if (wasFirst)
            {
                // Stamp the window AFTER the step-3 emission so the two can never
                // disagree about which attempt opened it.
                SetLong(KeyFirstAttemptMs, now);
                FlowTrace.Step(Sys, "step 3 FIRST RAID ATTEMPTED (scene='" + Safe(sceneName) +
                                    "') - 24h window for the second-raid nugget opens now.");
                return;
            }

            if (Latched(KeySecond)) return;   // nugget already answered for this install

            long first = GetLong(KeyFirstAttemptMs);
            if (first <= 0L)
            {
                // Step 3 latched on an older build that never stamped the ms, so the
                // window is unknowable. Say so rather than guessing in either direction:
                // an invented "yes" inflates the one number the programme is judged on.
                FlowTrace.Warn(Sys, "step 6 UNDECIDABLE - a raid was attempted before but no " +
                                    "first-attempt timestamp was recorded, so the 24h window cannot " +
                                    "be evaluated. Not emitting " + EventSecondRaidWithin24h + ".");
                return;
            }

            long elapsed = now - first;
            if (!IsWithinSecondRaidWindow(first, now))
            {
                FlowTrace.Step(Sys, "step 6 NOT met - second raid attempted " + elapsed +
                                    "ms after the first (window " + SecondRaidWindowMs + "ms). " +
                                    (elapsed < 0L
                                        ? "Elapsed is NEGATIVE - the device clock moved backwards; " +
                                          "counting it would be a fabricated conversion."
                                        : "The player came back, just not inside a day."));
                return;
            }

            FireOnce(KeySecond, EventSecondRaidWithin24h, new
            {
                scene = Safe(sceneName),
                firstAttemptMs = first,
                secondAttemptMs = now,
                elapsedMs = elapsed,
            });
            FlowTrace.Step(Sys, "step 6 THE GOLD NUGGET - a second raid was attempted " + elapsed +
                                "ms after the first. The loop is beginning to work.");
        }

        // =====================================================================
        //  STEP 4 - raid won
        // =====================================================================

        /// <summary>
        /// A raid was WON. Fired from the victory controller's single handled path.
        /// Also ARMS step 5: a spend only counts as "raid reward spent" once a reward
        /// has actually been won.
        /// </summary>
        public static void RaidWon(string configId, int stars)
        {
            FireOnce(KeyWon, EventFirstRaidWon, new
            {
                configId = Safe(configId),
                stars,
            });

            // Arm even when step 4 had already latched: a returning player who wins
            // again should still be able to complete step 5 if they never did.
            if (!Latched(KeySpent) && !Latched(KeyRewardArmed))
            {
                SetLatch(KeyRewardArmed);
                FlowTrace.Step(Sys, "step 5 ARMED - the next resource spend counts as the raid " +
                                    "reward being put to work.");
            }
        }

        // =====================================================================
        //  STEP 5 - raid reward spent
        // =====================================================================

        /// <summary>
        /// The player spent resources while step 5 was armed. Called from the two
        /// real spend surfaces (EconomyService.TrySpend and the progression
        /// ResourceLedger). Cheap and silent until armed - an unarmed call reads a
        /// single PlayerPrefs int and returns, so it is safe on a hot path.
        /// </summary>
        /// <param name="surface">Which spend seam (trace + property only).</param>
        public static void RewardSpent(string surface)
        {
            if (!Latched(KeyRewardArmed)) return;   // not armed: no raid has been won yet
            if (Latched(KeySpent)) { ClearLatch(KeyRewardArmed); return; }

            ClearLatch(KeyRewardArmed);
            FireOnce(KeySpent, EventRaidRewardSpent, new { surface = Safe(surface) });
            FlowTrace.Step(Sys, "step 5 RAID REWARD SPENT via '" + Safe(surface) +
                                "' - the reward re-entered the loop.");
        }

        // =====================================================================
        //  Plumbing. Never throws; every failure is LOUD (CLAUDE.md section 12).
        // =====================================================================

        /// <summary>
        /// WO-1802 - TRUE once this install has LAUNCHED a raid (step 3's latch). Exposed so a
        /// surface that wants to point a new player at the raid door can stop the moment they
        /// have used it, reading THE SAME latch that decides whether
        /// <see cref="EventFirstRaidAttempted"/> is emitted.
        ///
        /// ⚠ NOT GameState.EverCompletedRaid, and the difference is the measurement. That flag
        /// means FINISHED; this means ATTEMPTED, which is the step the live funnel is missing
        /// (zero emissions on 2026-09-16 against four players who reached the starter army).
        /// A player who launched a raid and died has found the door and must not be nagged.
        ///
        /// Per-INSTALL, like every other step here, for the reason this file's header gives: a
        /// player who wipes their save has still factually learned the loop once.
        /// </summary>
        public static bool FirstRaidAttempted => Latched(KeyAttempt);

        /// <summary>True when <paramref name="key"/>'s step has already been emitted.</summary>
        public static bool Latched(string key)
        {
            try { return PlayerPrefs.GetInt(key, 0) == 1; }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "PlayerPrefs read of '" + key + "' threw (" +
                                    ex.GetType().Name + ": " + ex.Message +
                                    ") - treating the step as NOT yet fired.");
                return false;
            }
        }

        private static void SetLatch(string key)
        {
            Guard.Try(Sys, "latch " + key, () =>
            {
                PlayerPrefs.SetInt(key, 1);
                PlayerPrefs.Save();
            });
        }

        private static void ClearLatch(string key)
        {
            Guard.Try(Sys, "clear latch " + key, () =>
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            });
        }

        private static long GetLong(string key)
        {
            try
            {
                string raw = PlayerPrefs.GetString(key, "0");
                return long.TryParse(raw, out long v) ? v : 0L;
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(Sys, "PlayerPrefs read of '" + key + "' threw (" +
                                    ex.GetType().Name + ") - answering 0.");
                return 0L;
            }
        }

        private static void SetLong(string key, long value)
        {
            Guard.Try(Sys, "stamp " + key, () =>
            {
                // Stored as a STRING: PlayerPrefs has no long, and an int overflows a
                // unix-ms value in 1970 + 24 days.
                PlayerPrefs.SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                PlayerPrefs.Save();
            });
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string Safe(string s) => string.IsNullOrEmpty(s) ? "unknown" : s;

        /// <summary>
        /// Emits <paramref name="eventName"/> exactly once per install, latching
        /// <paramref name="key"/>. The latch is written BEFORE the Track call so a
        /// throw inside the tracker cannot produce a duplicate on the next call - a
        /// missing event is a measurable hole, a duplicate is a silently wrong ratio.
        /// </summary>
        private static void FireOnce(string key, string eventName, object properties)
        {
            if (Latched(key))
            {
                FlowTrace.Once(Sys, "dup-" + eventName,
                    "funnel step '" + eventName + "' already recorded for this install - " +
                    "ignoring the repeat (a funnel counts players, not events).");
                return;
            }

            SetLatch(key);
            Guard.Try(Sys, "track " + eventName,
                () => EventTracker.Track(eventName, properties));
            FlowTrace.Step(Sys, "funnel step EMITTED: " + eventName);
        }
    }
}
