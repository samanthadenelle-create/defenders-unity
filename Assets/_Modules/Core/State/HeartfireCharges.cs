// =============================================================================
// HeartfireCharges - HEARTFIRE, the Heart's ability to sustain an expedition
// beyond its own reach. WO-1379. THE PURE HALF: the pool, the regen arithmetic
// and the words. No clock, no save, no scene, no UnityEngine.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.State
//
// Canon: docs/CREATIVE_CANON_ELARION_2026-09-04.md section 4. That file rules the
// fiction and the copy; this one only implements it. Do NOT restate the canon
// here and do NOT invent a name it does not carry.
//
// =============================================================================
//  (!) HEARTFIRE IS A CHARGE, NOT A CURRENCY. THIS IS THE WHOLE DESIGN.
// =============================================================================
// It is never earned, traded, stored, gifted or bought. There is NO wallet row,
// NO ResourceType member, NO storage cap, NO vendor and NO price anywhere. The
// economy map's "do not add another currency" ruling is NOT violated by this file
// and must not be read as licence to add one.
//
//   IF THE IMPLEMENTATION EVER GROWS A BALANCE, IT IS WRONG.
//
// The distinction is mechanical, not decorative: a currency has a SOURCE the
// player can influence (produce it, buy it, loot more of it) and a SINK that
// competes with other sinks. Nothing else can consume Heartfire, and nothing the
// player SPENDS makes it arrive faster. That is why it is safe, and it is what
// HeartfireRegression's currency-lint exists to keep true.
//
// =============================================================================
//  (!) TWO SOURCES, AS OF 2026-09-10. THE SECOND IS RULED, CAPPED AND NAMED.
// =============================================================================
// This header said "exactly one source - the passage of time" from WO-1379 until
// 2026-09-10, when the owner ruled (Q-HEARTFIRE, WO-1678/HEART-005):
//
//     "allow a second source for stakers - the single-source lint is re-pointed
//      to permit exactly Heartfire Spark."
//
// So the sentence above is CORRECTED rather than quietly left to rot, and the
// lint moved in the same change (CLAUDE.md section 15 - the doc and the code
// travel together). The sources are now, exhaustively:
//
//     1. TIME.  Regenerate(). The pool climbs one charge per rekindle interval.
//     2. SPARK. Spark(), below. A Heartbound Echo Event lights a charge.
//
// ⛔ AND THE SECOND ONE IS STILL NOT A CURRENCY, WHICH IS THE WHOLE REASON IT WAS
// ALLOWED. Read the three properties, because a future faucet has to keep all
// three or it is a different decision:
//
//   * IT IS CAPPED BY THE SAME CEILING AS TIME. Spark() clamps at maxCharges and
//     grants NOTHING to a full pool. A staker cannot hold more Heartfire than an
//     idle player who slept - only reach the ceiling sooner.
//   * IT CANNOT BE BOUGHT. No price, no pack, no vendor, no wallet row. It arrives
//     with an event the SERVER decided; the player cannot ask for one, and the
//     event table carries no purchase of any kind.
//   * IT IS NOT A BALANCE. There is still no amount, no storage cap, no
//     ResourceType member and no enum row. A second faucet on a capped pool is
//     still a pool.
//
// IF THE IMPLEMENTATION EVER GROWS A BALANCE, IT IS WRONG. That line is unchanged
// and unconditional; a second SOURCE is not a balance.
//
// ⚠ AND THE OPEN QUESTION IS NOW ANSWERED IN ONE DIRECTION ONLY. The "Heartfire is
// full" return door recorded as deliberately-not-built in WelcomeBackDoorsVM is a
// SEPARATE unruled question about a door, not about a faucet. This ruling does not
// answer it, and a lane must not read it as licence to build that door.
//
// "RAID ORDERS" IS DEAD (canon section 4): the player is the ruler and nobody
// issues them orders. But "MARCH" SURVIVES AS THE VERB - you spend Heartfire, you
// march. "MARCH AGAIN" is a valid button. "MARCHES" is never a noun for the pool;
// that was the FIRST-PASS name the owner superseded (canon section 2), and
// implementing a superseded name is a defect, not a preference.
//
// =============================================================================
//  WHY THE MATH IS PURE AND LIVES HERE
// =============================================================================
// Regeneration is LAZY: nothing ticks. The pool carries a count and the instant
// the last charge landed, and a read reconciles the two against "now". That makes
// the entire behaviour a total function of (charges, lastRegenUnixMs, now), which
// is exactly what an oracle can drive with no save, no clock and no PlayMode -
// the RaidCooldownService.DurationForDifficulty precedent, same reasoning.
//
// (!) THE CLOCK IS NOT READ HERE, AND THAT IS THE POINT. "now" is a PARAMETER.
// The one caller that supplies it is DeNelle.Village.World.Camps.HeartfireService,
// which reads the SERVER-ANCHORED seam TimeSource.NowUnixMs() and never
// DateTime.UtcNow. A charge pool stamped off the device clock is refilled in ten
// seconds by anyone who opens Settings > Date & Time, which makes the whole gate
// optional. DeNelle.Core cannot see TimeSource (it lives in DeNelle.Village), and
// that asymmetry is load-bearing rather than inconvenient: it makes reading the
// wrong clock from this file impossible.
//
// BALANCE VALUES ARE TUNABLES, never literals (standing rule 2026-09-02). Both
// numbers ride the EXISTING RemoteTunables rail with the shipped values as their
// DEFAULTS, so a retune is a database flip and not a rebuild.
//
// ASCII only - every string below reaches a mobile font atlas.
// =============================================================================

using System;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;

namespace DeNelle.Core.State
{
    /// <summary>
    /// HEARTFIRE: a stacking pool of expedition charges. Pure arithmetic plus the
    /// player-facing words. Never a currency (see the file header).
    /// </summary>
    public static class HeartfireCharges
    {
        /// <summary>FlowTrace system tag for the whole Heartfire lane.</summary>
        public const string Sys = "Heartfire";

        // =====================================================================
        //  BALANCE - owner-ruled, and authored on the tunables rail
        // =====================================================================

        /// <summary>
        /// SHIPPED default pool ceiling: 3 (canon section 4 - "Three charges ...
        /// stacks to three, so sleeping or working is not punished").
        /// <para>(!) ALIAS, NOT A COPY. The literal lives in
        /// <see cref="RemoteTunables.RaidHeartfireMaxChargesDefault"/> because that is the
        /// one file a tunable default may live in AND the only one the Command Center's
        /// manifest generator can parse a default out of. This const exists so consumers
        /// read a Heartfire word rather than a knob name; the compiler keeps them equal, so
        /// the pair can never drift the way a hand-copied number would.</para>
        /// </summary>
        public const int MaxChargesDefault = RemoteTunables.RaidHeartfireMaxChargesDefault;

        /// <summary>
        /// SHIPPED default rekindle interval: 4 h in seconds (canon section 4 - "One
        /// rekindles every four hours"). ALIAS of
        /// <see cref="RemoteTunables.RaidHeartfireRegenSecondsDefault"/>, see
        /// <see cref="MaxChargesDefault"/> for why that direction.
        /// <para>(!) It is deliberately EQUAL to the shortest authored per-camp cooldown
        /// (scene-configs.json raider_camp_small = 14400 s), which is what keeps the
        /// three-gate stack honest: a rekindled charge always has at least one camp
        /// that has finished recovering to spend it on. HeartfireRegression pins that
        /// relation, so shortening one without the other goes red.</para>
        /// </summary>
        public const int RegenSecondsDefault = RemoteTunables.RaidHeartfireRegenSecondsDefault;

        /// <summary>Hard sanity bound on the tunable - a table typo must never hand out
        /// an unbounded pool, and must never zero the gate either.</summary>
        public const int MaxChargesFloor = 1;
        /// <summary>Hard sanity bound on the tunable (see <see cref="MaxChargesFloor"/>).</summary>
        public const int MaxChargesCeiling = 9;
        /// <summary>Hard sanity floor on the rekindle interval, in seconds. A zero or
        /// negative interval would make the pool infinite, which is the one failure
        /// this clamp exists to make impossible.</summary>
        public const int RegenSecondsFloor = 60;

        /// <summary>The LIVE pool ceiling (tunable, clamped). Read it; never re-type 3.</summary>
        public static int MaxCharges
        {
            get
            {
                int v = RemoteTunables.Int(RemoteTunables.KeyRaidHeartfireMaxCharges);
                if (v < MaxChargesFloor || v > MaxChargesCeiling)
                {
                    FlowTrace.Throttle(Sys, "badmax", 60f,
                        "heartfire max charges tunable resolved " + v + ", outside " + MaxChargesFloor +
                        ".." + MaxChargesCeiling + " - clamping. The pool is never unbounded and never zero.");
                    v = v < MaxChargesFloor ? MaxChargesFloor : MaxChargesCeiling;
                }
                return v;
            }
        }

        /// <summary>The LIVE rekindle interval in seconds (tunable, clamped).</summary>
        public static double RegenSeconds
        {
            get
            {
                int v = RemoteTunables.Int(RemoteTunables.KeyRaidHeartfireRegenSeconds);
                if (v < RegenSecondsFloor)
                {
                    FlowTrace.Throttle(Sys, "badregen", 60f,
                        "heartfire regen tunable resolved " + v + "s, below the " + RegenSecondsFloor +
                        "s floor - clamping. A non-positive interval would make Heartfire infinite.");
                    v = RegenSecondsFloor;
                }
                return v;
            }
        }

        // =====================================================================
        //  THE POOL
        // =====================================================================

        /// <summary>
        /// A Heartfire pool at rest: how many charges are lit, and the instant the
        /// accrual window last advanced (unix-ms, from the server-anchored seam).
        /// Dumb data - it judges nothing, exactly like RaidCooldownRecord.
        /// </summary>
        [Serializable]
        public struct Pool
        {
            /// <summary>Charges currently lit. Never below 0, never above <see cref="MaxCharges"/>.</summary>
            public int Charges;

            /// <summary>
            /// Unix-ms the accrual window last advanced. NOT "when the player last
            /// raided": it advances by exactly one interval per charge granted, so the
            /// remainder of a partial interval is CARRIED rather than discarded. A
            /// player who checks in every 3h59m must still gain a charge on the hour.
            /// </summary>
            public double LastRegenUnixMs;

            /// <summary>
            /// True when the clock was server-anchored the last time this pool was
            /// written. PURELY DIAGNOSTIC - nothing branches on it and nothing may
            /// start to. A cold launch is ALWAYS unanchored, so the moment this flag
            /// slows or refuses a rekindle it taxes every honest offline player on
            /// every launch (the WO-1128 rule: refuse server-side, never punish
            /// client-side).
            /// </summary>
            public bool ServerAnchored;

            public Pool(int charges, double lastRegenUnixMs, bool serverAnchored)
            {
                Charges = charges;
                LastRegenUnixMs = lastRegenUnixMs;
                ServerAnchored = serverAnchored;
            }
        }

        /// <summary>
        /// A brand-new pool: FULL, stamped at <paramref name="nowUnixMs"/>. A player who
        /// has never raided is not made to wait twelve hours for the feature to exist,
        /// and a reinstall grants at most the same three charges an idle day would have.
        /// </summary>
        public static Pool NewFull(double nowUnixMs) => new Pool(MaxCharges, nowUnixMs, false);

        // =====================================================================
        //  THE ARITHMETIC - one total function, no side effects
        // =====================================================================

        /// <summary>
        /// Reconcile <paramref name="pool"/> against <paramref name="nowUnixMs"/> and return
        /// the pool as it stands. PURE: no clock, no save, no logging side effect on the
        /// caller's state - which is what lets an oracle assert the whole regen table with
        /// nothing loaded.
        ///
        /// <para>THE RULES, and each one is a case in HeartfireRegression:</para>
        /// <list type="bullet">
        /// <item>A pool already at the ceiling does not accrue, and its stamp is MOVED UP to
        /// now - otherwise a player who was full for two days would bank a hidden backlog
        /// and refill instantly after spending, which is a stacking pool with no ceiling.</item>
        /// <item>Charges accrue in WHOLE intervals; the remainder is carried in the stamp.</item>
        /// <item>Accrual is CLAMPED at the ceiling, and the stamp is set to now on the clamp,
        /// for the same reason as the first rule.</item>
        /// <item>A BACKWARDS clock RE-STAMPS to now (refuse, don't punish - the
        /// RaidCooldownService.RemainingSeconds precedent). The player waits at most one
        /// full interval, never longer, and never less: a rolled-back clock can therefore
        /// never manufacture a charge.</item>
        /// </list>
        /// </summary>
        /// <param name="pool">The pool as last written.</param>
        /// <param name="nowUnixMs">"Now", supplied by the caller from the server-anchored seam.</param>
        /// <param name="maxCharges">Pool ceiling (pass <see cref="MaxCharges"/> in production).</param>
        /// <param name="regenSeconds">Rekindle interval (pass <see cref="RegenSeconds"/>).</param>
        /// <param name="granted">How many charges this reconcile added. 0 is the common case.</param>
        /// <returns>The reconciled pool. Never mutates the input.</returns>
        public static Pool Regenerate(Pool pool, double nowUnixMs, int maxCharges,
                                      double regenSeconds, out int granted)
        {
            granted = 0;

            if (maxCharges < 1) maxCharges = 1;
            if (regenSeconds <= 0d || double.IsNaN(regenSeconds)) regenSeconds = RegenSecondsDefault;

            var next = pool;
            if (next.Charges < 0) next.Charges = 0;
            if (next.Charges > maxCharges) next.Charges = maxCharges;

            // An unstamped pool (a fresh install, or a record that lost its stamp) is
            // anchored at now rather than at the epoch - an epoch stamp would resolve to
            // ~57 years of accrual and hand out a full pool via the accrual path, which
            // would look identical to a working regen and hide a real bug forever.
            if (next.LastRegenUnixMs <= 0d || double.IsNaN(next.LastRegenUnixMs))
            {
                next.LastRegenUnixMs = nowUnixMs;
                return next;
            }

            if (next.Charges >= maxCharges)
            {
                // Full: no accrual, and no hidden backlog (see the doc comment).
                next.LastRegenUnixMs = nowUnixMs;
                return next;
            }

            double elapsedSeconds = (nowUnixMs - next.LastRegenUnixMs) / 1000d;

            if (elapsedSeconds < 0d)
            {
                // REFUSE, DON'T PUNISH. The clock moved backwards since the stamp, which
                // would otherwise read as a rekindle that keeps receding. Re-stamp to now:
                // at most one full interval of waiting, never more, and never less - so a
                // backwards clock can never shorten the wait or conjure a charge.
                next.LastRegenUnixMs = nowUnixMs;
                return next;
            }

            if (elapsedSeconds < regenSeconds) return next;   // the common path: nothing to do

            int earned = (int)Math.Floor(elapsedSeconds / regenSeconds);
            int room = maxCharges - next.Charges;
            granted = earned < room ? earned : room;
            next.Charges += granted;

            next.LastRegenUnixMs = next.Charges >= maxCharges
                ? nowUnixMs                                            // clamped: no backlog
                : next.LastRegenUnixMs + granted * regenSeconds * 1000d;  // carry the remainder

            return next;
        }

        /// <summary>
        /// Seconds until the next charge lights, given an ALREADY-reconciled pool.
        /// 0 when the pool is full (nothing is pending) - callers render "Heartfire is
        /// full" for that case rather than a countdown to nothing.
        /// </summary>
        public static double SecondsToNextCharge(Pool pool, double nowUnixMs, int maxCharges,
                                                 double regenSeconds)
        {
            if (maxCharges < 1) maxCharges = 1;
            if (regenSeconds <= 0d || double.IsNaN(regenSeconds)) regenSeconds = RegenSecondsDefault;
            if (pool.Charges >= maxCharges) return 0d;

            double elapsedSeconds = (nowUnixMs - pool.LastRegenUnixMs) / 1000d;
            if (elapsedSeconds < 0d) elapsedSeconds = 0d;          // backwards clock: a full wait
            double remaining = regenSeconds - elapsedSeconds;
            return remaining > 0d ? remaining : 0d;
        }

        /// <summary>
        /// Spend one charge. Returns false (and leaves the pool untouched) when the pool is
        /// empty - the ONE refusal, and the caller must say the Heart's sentence, never a
        /// timer's. PURE: the caller persists and traces.
        /// </summary>
        public static bool TrySpend(Pool pool, out Pool spent)
        {
            spent = pool;
            if (pool.Charges <= 0) return false;
            spent.Charges = pool.Charges - 1;
            // The stamp is NOT touched. Spending must not restart the accrual window, or a
            // player who marches the instant a charge lands would silently lose the partial
            // progress toward the next one - a punishment nobody authored.
            return true;
        }

        /// <summary>
        /// THE RULED SECOND SOURCE (owner, 2026-09-10, Q-HEARTFIRE): light charges from a
        /// Heartbound Echo Event. PURE - the caller persists, publishes and traces, exactly
        /// like <see cref="TrySpend"/>.
        ///
        /// <para>⛔ CLAMPED AT THE SAME CEILING AS TIME, and that clamp is the reason this
        /// source was allowed at all. A full pool grants ZERO and reports it: a staker can
        /// reach the ceiling sooner, never hold more than an idle player who slept.</para>
        ///
        /// <para>⛔ THE STAMP IS NOT TOUCHED, and this direction matters more than it does
        /// for spending. Moving it FORWARD would push the next rekindle away, so a spark
        /// would silently cost time and the reward would be partly an illusion. Leaving it
        /// alone means a spark is purely additive, which is what the reveal card promises.
        /// (A pool taken to the ceiling by a spark still stops accruing on the next
        /// Regenerate, which re-stamps a full pool by its own rule - so no hidden backlog
        /// is banked either.)</para>
        ///
        /// <para>⛔ NOT IDEMPOTENT. Each call that returns a positive number lights charges.
        /// Once-only belongs to the caller's claim ledger, never to this arithmetic.</para>
        /// </summary>
        /// <param name="pool">The pool as last written.</param>
        /// <param name="charges">How many to light. Non-positive is refused, not silently zero-ed.</param>
        /// <param name="maxCharges">Pool ceiling (pass <see cref="MaxCharges"/> in production).</param>
        /// <param name="sparked">The pool afterwards. Equals the input when nothing was lit.</param>
        /// <returns>How many charges were actually lit: 0 at the ceiling, or on a bad request.</returns>
        public static int Spark(Pool pool, int charges, int maxCharges, out Pool sparked)
        {
            sparked = pool;
            if (charges <= 0) return 0;
            if (maxCharges < 1) maxCharges = 1;

            int current = pool.Charges < 0 ? 0 : pool.Charges;
            if (current >= maxCharges) return 0;

            int room = maxCharges - current;
            int lit = charges < room ? charges : room;
            sparked.Charges = current + lit;
            return lit;
        }

        // =====================================================================
        //  THE WORDS - canon section 4. Copy lives here for the same reason
        //  PostureSignals.RaidLockCopy does: ONE owner, so every surface and the
        //  oracle read identical sentences.
        // =====================================================================

        /// <summary>The pool's proper name. Never "Raid Orders", never "Marches".</summary>
        public const string Name = "Heartfire";

        /// <summary>
        /// The legacy ASCII rendering retained for logs and traces. Player-facing surfaces bind
        /// <see cref="FlameStates"/> to the owner-selected flame sprite instead of painting these
        /// brackets. ASCII remains useful in evidence where image state cannot be serialized.
        /// </summary>
        public static string FlameRow(int charges, int maxCharges)
        {
            bool[] states = FlameStates(charges, maxCharges);
            var sb = new System.Text.StringBuilder(states.Length * 4);
            for (int i = 0; i < states.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(states[i] ? "[*]" : "[ ]");
            }
            return sb.ToString();
        }

        /// <summary>
        /// One state per charge slot: true is lit, false is spent. Inputs clamp to a non-empty
        /// pool and to its ceiling so presentation code cannot build a ragged or surplus row.
        /// </summary>
        public static bool[] FlameStates(int charges, int maxCharges)
        {
            if (maxCharges < 1) maxCharges = 1;
            if (charges < 0) charges = 0;
            if (charges > maxCharges) charges = maxCharges;

            var states = new bool[maxCharges];
            for (int i = 0; i < states.Length; i++) states[i] = i < charges;
            return states;
        }

        /// <summary>"Heartfire 2/3" - the count in words, for a badge that must fit.</summary>
        public static string CountLabel(int charges, int maxCharges) =>
            Name + " " + charges + "/" + maxCharges;

        // ---------------------------------------------------------------------
        //  WO-1415 - WHAT A CHARGE BUYS. The owner's sentence, and the plate form.
        // ---------------------------------------------------------------------
        // The felt-test that opened WO-1415: "Heartfire is full, i dont understand as a
        // new player what to do with that. No one in game has introduced me to heartfire."
        // The plate reported a STATE with no consequence attached, and Heartfire is the ONE
        // gate on whether the player may raid at all (WO-1379). So the consequence is now
        // said out loud, and it is said in exactly ONE place - here - because the guide
        // entry, the introduction beat and the plate must never drift apart.

        /// <summary>
        /// THE OWNER'S SENTENCE (ruling 2026-09-05, WO-1415): what one charge buys, in her
        /// words. The guide entry and the introduction dialogue carry it VERBATIM, and
        /// HeartfireRegression asserts both files still do - a reworded copy in a JSON file
        /// is the drift this const exists to make impossible.
        /// <para>(!) The PLATE deliberately does NOT use the sentence. That row is a single
        /// FITTED line inside the WO-1384 Heart plate, and a sentence ellipsises there; the
        /// owner ruled the parenthetical form for the plate instead (see
        /// <see cref="PlateLabel"/>). Same fact, two lengths, one owner.</para>
        /// </summary>
        public const string SpendSentence = "each one sends you on a raid";

        /// <summary>The plate's consequence tag - the short form of
        /// <see cref="SpendSentence"/>, ruled by the owner for the fitted row.</summary>
        public const string SpendTag = "(raids)";

        /// <summary>
        /// THE PLATE'S FIRST ROW, minus the marks: "Heartfire 3/3 (raids)" (owner ruling
        /// 2026-09-05). It names the count AND what a charge is for, at a width that seats
        /// at the row's font floor - which "Heartfire - each one sends you on a raid" does
        /// not.
        /// </summary>
        public static string PlateLabel(int charges, int maxCharges) =>
            HeartHudText.HeartfirePlate.Resolve(new HeartfireCountArguments(charges, maxCharges));

        /// <summary>
        /// THE PLATE'S SECOND ROW: "next in 3h 12m" while a charge is pending, and EMPTY on
        /// a full pool. Empty rather than "Heartfire is full" because a state word with no
        /// consequence is the exact thing WO-1415 was raised about; the first row already
        /// says 3/3, which is the same fact without the dead end.
        /// </summary>
        public static string PlateRekindle(int charges, int maxCharges, double secondsToNext)
        {
            if (maxCharges < 1) maxCharges = 1;
            if (charges >= maxCharges) return string.Empty;
            if (double.IsNaN(secondsToNext) || secondsToNext < 0d) secondsToNext = 0d;
            long totalMinutes = (long)Math.Ceiling(secondsToNext / 60d);
            if (totalMinutes < 1) totalMinutes = 1;
            long hours = totalMinutes / 60;
            long minutes = totalMinutes % 60;
            return hours > 0
                ? HeartHudText.HeartfireNextHoursMinutes.Resolve(
                    new HeartfireHoursMinutesArguments(hours, minutes))
                : HeartHudText.HeartfireNextMinutes.Resolve(new HeartfireMinutesArguments(minutes));
        }

        /// <summary>Localized one-line fallback when the plate cannot create its second row.</summary>
        public static string PlateCombined(string plate, string rekindle) =>
            string.IsNullOrEmpty(rekindle)
                ? plate
                : HeartHudText.HeartfireCombined.Resolve(
                    new HeartfireCombinedArguments(plate, rekindle));

        /// <summary>
        /// A COARSE wait, for a row read at a glance: "3h 12m", "12m". Distinct from
        /// <see cref="Clock"/> (h:mm:ss) on purpose - the refusal toast names a precise wait
        /// because a refused player is deciding whether to stay, while the plate is ambient.
        /// <para>Rounds UP to the minute and NEVER renders "0m": a live wait shown as zero
        /// reads as a broken button (the same rule <see cref="Clock"/> carries).</para>
        /// </summary>
        public static string ShortWait(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0d) seconds = 0d;
            long totalMinutes = (long)Math.Ceiling(seconds / 60d);
            if (totalMinutes < 1) totalMinutes = 1;
            long h = totalMinutes / 60;
            long m = totalMinutes % 60;
            return h > 0 ? h + "h " + m + "m" : m + "m";
        }

        /// <summary>
        /// The COMPOSED one-line form: "Heartfire is full" / "Heartfire rekindles in
        /// 3:42:18".
        /// <para>(!) WO-1415 MOVED THE PLATE OFF THIS. The Heart plate now paints
        /// <see cref="PlateLabel"/> on its marks row and <see cref="PlateRekindle"/> on the
        /// row beneath, because the owner ruled the plate must name what a charge BUYS and
        /// not only its state. This composer is retained as the single-line form for any
        /// surface that has one row rather than two - HudKitController's own fallback branch
        /// (a factory failure that leaves the second label null) still composes the ruled
        /// pair with " - " rather than calling this, so the ruled string survives there
        /// too.</para>
        /// </summary>
        public static string RekindleLine(int charges, int maxCharges, double secondsToNext)
        {
            if (charges >= maxCharges) return Name + " is full";
            return Name + " rekindles in " + Clock(secondsToNext);
        }

        /// <summary>
        /// THE REFUSAL. The whole point of the rename lives in this sentence: not "you may
        /// not raid because TIMER", but the Heart is not ready to send you back yet
        /// (canon section 4). It always names the wait, because a player told "no" with no
        /// "when" cannot act on it (the RaidCooldownService.BlockedMessage precedent).
        /// </summary>
        public static string BlockedMessage(double secondsToNext) =>
            "The Heart is not ready to send you back yet. " + Name + " rekindles in " + Clock(secondsToNext) + ".";

        /// <summary>h:mm:ss (or m:ss under an hour). ASCII, zero-padded, no localisation.</summary>
        public static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0d) seconds = 0d;
            long total = (long)Math.Ceiling(seconds);
            long h = total / 3600;
            long m = (total % 3600) / 60;
            long s = total % 60;
            return h > 0
                ? h + ":" + m.ToString("00") + ":" + s.ToString("00")
                : m + ":" + s.ToString("00");
        }
    }
}
