// =============================================================================
// HeartfireService - HEARTFIRE, the runtime half. WO-1379.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// The pure pool arithmetic and every player-facing word live in
// DeNelle.Core.State.HeartfireCharges. THIS file owns the three things that
// cannot be pure: the CLOCK, the PERSISTENCE, and the PUBLISH to the HUD.
// Read that file's header first - it is where the design is argued, including
// the one rule that outranks everything here:
//
//   (!) HEARTFIRE IS A CHARGE, NOT A CURRENCY. No wallet row, no ResourceType
//       member, no storage cap, no vendor, no price. If it ever grows a balance,
//       it is wrong.
//
// =============================================================================
//  THE ONE ENTRY GATE - and it is deliberately GLOBAL, not per camp
// =============================================================================
// Two pacing gates stand between the player and a raid, each with its own true
// reason, and they are siblings rather than duplicates:
//
//   RaidClaimService.CrystalsPaidToday  "have I already been paid TODAY?"   (loot)
//   HeartfireService (this file)        "can the HEART sustain a march?"    (entry)
//
// (!) THE PER-CAMP WALL IS RETIRED AS A GATE (WO-1379, owner ruling 2026-09-04,
//     landed at the door 2026-09-05: "Heartfire replaces the camp wall").
// RaidCooldownService used to be the third row here ("may I raid THIS CAMP again
// yet?"). It is now a RECORD, not a gate: BeginAfterClear still stamps every clear
// (save evidence for SaveMigrator v41 / the first-raid soft gate), but
// RaidSelectionScreen.OnCardTapped consults ONLY HasCharge below, and
// HeartfireRegression PIN F reds that file if a RaidCooldownService reference comes
// back. One gate on WHEN you may raid - two lockouts "reads as a bug".
//
// WHY ONE GATE IS SAFE (worked through in the WO, not assumed): re-clearing the
// easiest camp is not optimal, because camps carry an escalating rewardMultiplier
// (1.0 / 1.5 / 2.2), so the best use of a charge is the hardest camp you can beat.
//
// (!) THE CRITERION THAT KEPT THE OLD STACK HONEST is now structural:
//     A PLAYER HOLDING HEARTFIRE ALWAYS HAS SOMEWHERE TO SPEND IT
// - with no per-camp door, every unlocked camp is somewhere. HeartfireRegression
// PIN E still pins the rekindle interval <= the SHORTEST authored raidCooldownSeconds
// (14400 s) as a tripwire on the RETAINED field: WO-1379 forbids retuning it, and a
// superseded number that drifts is how the next seat reads a value that means
// nothing.
//
// (!) DO NOT SHORTEN raidCooldownSeconds. That file's own authoring note explains at
// length why those hours were never the lever: crystals buy instant-finish on the
// Obsidian queue, so a shorter camp cooldown defunds the timer ladder the whole game
// is paced by. Retired as a gate; NOT retuned.
//
// =============================================================================
//  (!) CLOCK DISCIPLINE - READ BEFORE TOUCHING ANY TIME LINE
// =============================================================================
// EVERY "now" here is TimeSource.NowUnixMs(). NEVER DateTime.UtcNow, never
// DateTimeOffset.UtcNow, never Time.time. TimeSource is server-anchored when a
// handshake has happened this process (ServerClock anchors to a MONOTONIC
// Stopwatch, so a wall-clock edit cannot move it). A charge pool stamped off the
// device clock is refilled in ten seconds by anyone who opens Settings > Date &
// Time, which makes the gate optional. The same rule, the same words and the same
// reasons as RaidCooldownService's header - because it is the same clock.
//
// UNANCHORED IS NOT PUNISHED. A cold launch is ALWAYS unanchored, so refusing to
// rekindle without an anchor would tax every honest offline player on every
// launch (WO-1128: refuse server-side, never punish client-side). We record the
// anchor state and branch on nothing.
//
// A BACKWARDS CLOCK RE-STAMPS to now (HeartfireCharges.Regenerate): at most one
// full interval of waiting, never more and never less - so rolling the phone
// clock back can neither shorten the wait nor conjure a charge.
//
// =============================================================================
//  PERSISTENCE - PlayerPrefs, and this is FLAGGED, not hidden
// =============================================================================
// The pool rides PlayerPrefs, exactly like its sibling gate
// RaidClaimService.CrystalsPaidToday (RaidClaimService.cs:162-189, "PlayerPrefs,
// not the save file - same local-first convention as the claim set"). That keeps
// this lane file-disjoint from GameState/SaveSchema, which WO-1379 did not scope
// to it and which four other lanes are editing concurrently.
//
// (!) THE HONEST COST, stated so the next seat does not have to discover it:
// RaidCooldownRecord's header argues the OTHER way for the per-camp cooldown - a
// window that survives a reinstall but not a cloud restore is worse than one that
// survives neither. The same argument applies here, so folding this pool into the
// save (one additive nullable field, default-on-read, NO schema bump - the
// raidCooldowns precedent) is a genuine follow-up and NOT a refactor for its own
// sake. It is a lead/owner call, not this lane's, and it is written into the
// WO-1379 report rather than silently decided here. Until then: a reinstall grants
// a full pool, which is at most what one idle day would have granted anyway.
//
// ASCII-only. Canon: the village is Elarion (never Avalon).
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.HudModel;
using DeNelle.Core.State;

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// The live Heartfire pool: lazily regenerated off the server-anchored clock,
    /// spent on raid entry, and published to the HUD. One owner, one seam.
    /// </summary>
    public static class HeartfireService
    {
        /// <summary>FlowTrace system tag. Shared with the Core half so one grep finds both.</summary>
        public const string Sys = HeartfireCharges.Sys;

        // PlayerPrefs keys. Prefixed "dotr-" like every other raid key so a stray
        // PlayerPrefs.DeleteAll in a tool cannot be mistaken for a targeted clear.
        private const string PrefCharges = "dotr-heartfire-charges";
        private const string PrefStamp   = "dotr-heartfire-stamp-ms";
        private const string PrefAnchored = "dotr-heartfire-anchored";

        // =====================================================================
        //  Reading
        // =====================================================================

        /// <summary>
        /// The pool as it stands RIGHT NOW: read, reconcile against the clock, write
        /// back if anything moved, publish if the count moved.
        ///
        /// <para>SIDE EFFECT BY DESIGN, exactly like RaidCooldownService.RemainingSeconds:
        /// regeneration IS the read. A pure query would either have to tick (nothing does)
        /// or re-derive the same repair on every frame without ever storing it.</para>
        /// </summary>
        public static HeartfireCharges.Pool Current()
        {
            double now = TimeSource.NowUnixMs();
            var before = Load(now, out bool wasSeeded);

            int max = HeartfireCharges.MaxCharges;
            double regen = HeartfireCharges.RegenSeconds;

            var after = HeartfireCharges.Regenerate(before, now, max, regen, out int granted);

            if (granted > 0)
                FlowTrace.Step(Sys, "Heartfire REKINDLED +" + granted + " -> " + after.Charges + "/" + max +
                                    " (interval " + regen.ToString("F0") + "s, serverAnchored=" +
                                    TimeSource.IsServerAnchored + "). The Heart can sustain another march.");

            // (!) WRITE ONLY WHEN SOMETHING REAL MOVED. Regenerate deliberately advances the
            // stamp on a FULL pool (that is how the no-hidden-backlog rule works), so a naive
            // "the stamp moved -> persist" test would flush PlayerPrefs EVERY TICK for the
            // whole time a player is sitting at three charges - a disk write per second, on a
            // phone, forever. The in-memory pool still carries the advanced stamp, and the one
            // moment it has to be durable - a spend - persists it through Store(spent) in
            // TrySpend. So the invariant holds and the write does not.
            bool countChanged = after.Charges != before.Charges;
            bool stampMovedWhileNotFull = after.Charges < max &&
                                          Math.Abs(after.LastRegenUnixMs - before.LastRegenUnixMs) > 0.5d;
            if (wasSeeded || granted > 0 || countChanged || stampMovedWhileNotFull)
                Store(after);

            Publish(after, now, max, regen);
            return after;
        }

        /// <summary>Charges lit right now (0..<see cref="Max"/>).</summary>
        public static int Charges => Current().Charges;

        /// <summary>The live pool ceiling. Never re-type 3 - read it.</summary>
        public static int Max => HeartfireCharges.MaxCharges;

        /// <summary>True when the Heart can sustain a march right now.</summary>
        public static bool HasCharge => Current().Charges > 0;

        /// <summary>Seconds until the next charge lights; 0 when the pool is full.</summary>
        public static double SecondsToNextCharge()
        {
            double now = TimeSource.NowUnixMs();
            var pool = Current();
            return HeartfireCharges.SecondsToNextCharge(pool, now, HeartfireCharges.MaxCharges,
                                                        HeartfireCharges.RegenSeconds);
        }

        /// <summary>
        /// THE REFUSAL SENTENCE for a march attempted with an empty pool. Always the
        /// Heart's words and always with the wait named - never "you may not raid because
        /// TIMER" (canon section 4).
        /// </summary>
        public static string BlockedMessage() =>
            HeartfireCharges.BlockedMessage(SecondsToNextCharge());

        // =====================================================================
        //  Spending
        // =====================================================================

        /// <summary>
        /// Spend one Heartfire to march. Returns false and changes NOTHING when the pool
        /// is empty; the caller must then show <see cref="BlockedMessage"/> rather than
        /// letting the march proceed silently.
        ///
        /// <para>NOT IDEMPOTENT, by definition - each call that returns true consumes a
        /// charge. Call it ONCE per march, at the entry seam, never in a repaint.</para>
        /// </summary>
        /// <param name="reason">What is being marched on (a camp config id), for the trace.</param>
        public static bool TrySpend(string reason)
        {
            var pool = Current();
            int max = HeartfireCharges.MaxCharges;

            if (!HeartfireCharges.TrySpend(pool, out var spent))
            {
                FlowTrace.Step(Sys, "Heartfire SPEND REFUSED for '" + (reason ?? "unknown") +
                                    "' - the pool is empty (0/" + max + "). Next rekindle in " +
                                    HeartfireCharges.Clock(SecondsToNextCharge()) + ". The march does not " +
                                    "start; the caller shows the Heart's sentence, never a bare timer.");
                return false;
            }

            Store(spent);
            double now = TimeSource.NowUnixMs();
            Publish(spent, now, max, HeartfireCharges.RegenSeconds);

            FlowTrace.Step(Sys, "Heartfire SPENT on '" + (reason ?? "unknown") + "' -> " + spent.Charges +
                                "/" + max + " remaining (serverAnchored=" + TimeSource.IsServerAnchored +
                                "). The accrual stamp is deliberately untouched, so partial progress " +
                                "toward the next charge is not lost by marching the moment one lands.");
            return true;
        }

        /// <summary>
        /// Light Heartfire from a Heartbound Echo Event - THE RULED SECOND SOURCE
        /// (owner, 2026-09-10, Q-HEARTFIRE: "allow a second source for stakers ... the
        /// single-source lint is re-pointed to permit exactly Heartfire Spark").
        ///
        /// <para>⛔ IT IS CAPPED BY THE SAME CEILING AS TIME. A full pool gains NOTHING and
        /// returns 0. A staker reaches the ceiling sooner; they never hold more than an
        /// idle player who slept. That clamp is why the second source was allowed, so it
        /// is not a detail a later edit may relax.</para>
        ///
        /// <para>⛔ NOTHING IS BOUGHT HERE. There is no price, no pack and no wallet row on
        /// this path: the event that calls it was decided by the server, and the player
        /// cannot ask for one. Heartfire is still a CHARGE, not a currency.</para>
        ///
        /// <para>NOT IDEMPOTENT - each call that returns a positive number lights charges.
        /// Once-only belongs to the caller's claim ledger (HeartboundEventInbox plus the
        /// backend's own), never to this seam.</para>
        /// </summary>
        /// <param name="charges">How many to light, from the authored event table.</param>
        /// <param name="reason">The event id, for the trace.</param>
        /// <returns>How many were actually lit; 0 when the pool was already full.</returns>
        public static int TryGrantSpark(int charges, string reason)
        {
            var pool = Current();
            int max = HeartfireCharges.MaxCharges;

            int lit = HeartfireCharges.Spark(pool, charges, max, out var sparked);
            if (lit <= 0)
            {
                FlowTrace.Step(Sys, "Heartfire SPARK from '" + (reason ?? "unknown") + "' lit nothing - " +
                                    "the pool is already at its ceiling (" + pool.Charges + "/" + max +
                                    "). The second source is capped by the SAME ceiling as time; it can " +
                                    "never carry a player past it.");
                return 0;
            }

            Store(sparked);
            double now = TimeSource.NowUnixMs();
            Publish(sparked, now, max, HeartfireCharges.RegenSeconds);

            FlowTrace.Step(Sys, "Heartfire SPARK from '" + (reason ?? "unknown") + "' lit " + lit +
                                " -> " + sparked.Charges + "/" + max + ". The accrual stamp is untouched, " +
                                "so a spark is purely additive and never pushes the next rekindle away.");
            return lit;
        }

        /// <summary>
        /// Test/dev hook: put the pool at an exact state. Exercised by HeartfireRegression -
        /// an unexercised hook proves nothing (the RaidClaimService.ClearClaim lesson).
        /// Never called by gameplay.
        /// </summary>
        public static void DebugSet(int charges, double lastRegenUnixMs)
        {
            int max = HeartfireCharges.MaxCharges;
            if (charges < 0) charges = 0;
            if (charges > max) charges = max;
            var pool = new HeartfireCharges.Pool(charges, lastRegenUnixMs, TimeSource.IsServerAnchored);
            Store(pool);
            Publish(pool, TimeSource.NowUnixMs(), max, HeartfireCharges.RegenSeconds);
            FlowTrace.Step(Sys, "Heartfire DEBUG-SET to " + charges + "/" + max + " stamped " +
                                lastRegenUnixMs.ToString("F0") + " (test hook).");
        }

        /// <summary>Test/dev hook: forget the pool entirely, so the next read re-seeds it full.</summary>
        public static void DebugClear()
        {
            Guard.Try(Sys, "clear heartfire prefs", () =>
            {
                PlayerPrefs.DeleteKey(PrefCharges);
                PlayerPrefs.DeleteKey(PrefStamp);
                PlayerPrefs.DeleteKey(PrefAnchored);
                PlayerPrefs.Save();
            });
            FlowTrace.Step(Sys, "Heartfire pool CLEARED (test hook) - the next read re-seeds a full pool.");
        }

        // =====================================================================
        //  THE PUBLISHER - so the town HUD is never stale
        // =====================================================================
        // WITHOUT THIS THE FLAMES ARE A LIE. Nothing else in town calls into this
        // service: the spend seam only fires on raid entry, so a HUD that painted only
        // on a publish would sit on PostureSignals' pre-publish defaults for the whole
        // session and cheerfully show three lit flames to a player holding none. That is
        // exactly the class of defect PostureSignals' own header records (the one-shot
        // TalkHudBridge hook that stopped pushing after a scene swap), so it is closed
        // the same way: a producer that cannot go quiet.
        //
        // ONE SECOND is the cadence because the rekindle line is a countdown a player
        // reads; the read itself is a PlayerPrefs lookup plus arithmetic, and Current()
        // only writes when something real moved (see the note there). Self-installing +
        // DontDestroyOnLoad, so it survives every scene swap and cannot be duplicated.

        private sealed class HeartfirePublisher : MonoBehaviour
        {
            private float _next;

            private void Update()
            {
                // Unscaled: a paused / time-scaled game must not stall a real-world clock.
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 1f;
                Guard.Try(Sys, "publish heartfire", () => { Current(); });
            }
        }

        private static HeartfirePublisher s_publisher;

        /// <summary>Install the once-a-second publisher. Idempotent, and safe to call from
        /// anywhere - it is armed automatically at load.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void EnsurePublisher()
        {
            if (s_publisher != null) return;
            bool ok = Guard.Try(Sys, "install heartfire publisher", () =>
            {
                var go = new GameObject("HeartfirePublisher");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                s_publisher = go.AddComponent<HeartfirePublisher>();
            });
            if (!ok)
            {
                // Guard logged the throw; say what it COSTS, because a silent miss here
                // shows the player a full Heart they do not have.
                FlowTrace.Warn(Sys, "the Heartfire publisher did not install - the town HUD will show " +
                                    "whatever PostureSignals last held, which on a fresh process is a " +
                                    "FULL pool. The gate itself still works (every read reconciles); " +
                                    "only the display goes stale.");
                return;
            }
            FlowTrace.Step(Sys, "Heartfire publisher installed (1 Hz, unscaled) - the town HUD repaints " +
                                "the flames and the rekindle line without any surface polling the save.");
        }

        // =====================================================================
        //  Internals - one place that touches storage, one that touches the HUD
        // =====================================================================

        /// <summary>
        /// The stored pool, or a freshly seeded FULL one when nothing is stored.
        /// A player who has never marched is not made to wait twelve hours for the
        /// feature to exist.
        /// </summary>
        private static HeartfireCharges.Pool Load(double now, out bool seeded)
        {
            bool present = false;
            var pool = new HeartfireCharges.Pool(0, 0d, false);
            bool ok = Guard.Try(Sys, "read heartfire pool", () =>
            {
                present = PlayerPrefs.HasKey(PrefCharges) && PlayerPrefs.HasKey(PrefStamp);
                if (!present) return;
                pool = new HeartfireCharges.Pool(
                    PlayerPrefs.GetInt(PrefCharges, HeartfireCharges.MaxCharges),
                    // PlayerPrefs has no double. unix-ms exceeds float precision by a wide
                    // margin, so the stamp round-trips as a STRING and is parsed invariantly.
                    ParseStamp(PlayerPrefs.GetString(PrefStamp, string.Empty), now),
                    PlayerPrefs.GetInt(PrefAnchored, 0) != 0);
            });

            if (!ok)
            {
                // Guard already logged the throw. Say what we are doing ABOUT it, because
                // silently seeding a full pool is indistinguishable from a healthy read.
                FlowTrace.Warn(Sys, "Heartfire pool could not be read - seeding a FULL pool for this " +
                                    "session. That is the FORGIVING direction on purpose (a storage " +
                                    "fault must not lock a paying player out of the game), but nothing " +
                                    "is being enforced while this holds.");
                seeded = true;
                return HeartfireCharges.NewFull(now);
            }

            if (!present)
            {
                seeded = true;
                FlowTrace.Step(Sys, "no Heartfire pool stored - seeding FULL (" + HeartfireCharges.MaxCharges +
                                    "). A new save is not made to wait for the feature to exist.");
                return HeartfireCharges.NewFull(now);
            }

            seeded = false;
            return pool;
        }

        private static double ParseStamp(string raw, double fallbackNow)
        {
            if (string.IsNullOrEmpty(raw)) return fallbackNow;
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v) &&
                !double.IsNaN(v) && v > 0d)
                return v;

            FlowTrace.Warn(Sys, "stored Heartfire stamp '" + raw + "' did not parse - treating it as NOW, " +
                                "which costs the player at most one interval of accrual and can never " +
                                "hand out a free charge.");
            return fallbackNow;
        }

        private static void Store(HeartfireCharges.Pool pool)
        {
            pool.ServerAnchored = TimeSource.IsServerAnchored;   // recorded only, never branched on
            Guard.Try(Sys, "persist heartfire pool", () =>
            {
                PlayerPrefs.SetInt(PrefCharges, pool.Charges);
                PlayerPrefs.SetString(PrefStamp,
                    pool.LastRegenUnixMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                PlayerPrefs.SetInt(PrefAnchored, pool.ServerAnchored ? 1 : 0);
                PlayerPrefs.Save();
            });
        }

        /// <summary>
        /// Push the count onto the EXISTING posture rail, beside RaidCapable - the
        /// SetRaidCapable mirror pattern. DeNelle.HUD may reference DeNelle.Core ONLY
        /// (CLAUDE.md section 5), so a Core static is the one legal way for a Village
        /// fact to reach the town HUD, and a static cannot go stale across a scene swap.
        /// </summary>
        private static void Publish(HeartfireCharges.Pool pool, double now, int max, double regen)
        {
            double next = HeartfireCharges.SecondsToNextCharge(pool, now, max, regen);
            PostureSignals.SetHeartfire(pool.Charges, max, next);
        }
    }
}
