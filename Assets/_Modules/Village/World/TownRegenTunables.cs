// =============================================================================
// TownRegenTunables - the ONE reader of the WO-1773 town-regen COMBAT GATE knobs,
// and the owner of their clamps.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Rail: DeNelle.Core.Ops.RemoteTunables - reused end to end, not re-invented.
// This file is modelled deliberately on RaidDifficultyTunables (WO-1763): the same
// ResolveFrom(...)-takes-the-knob-values shape so an oracle can drive every branch
// with nothing loaded and no row written, the same ClampAndReport, and the same
// "identity short-circuits" rule so an empty table is today's behaviour exactly.
//
// -----------------------------------------------------------------------------
// WHY THIS EXISTS (the owner-visible symptom, then the arithmetic).
// -----------------------------------------------------------------------------
// An external tester relayed by the owner on 2026-09-16: "at the level he is
// nothing can damage him he can just stand there". WO-1773 measured the hero plate
// across 146 one-second frames and 80 consecutive tenth-second frames of a wave-176
// run: the red bar's right edge never moved, with a Cave Troll in melee contact.
//
// DEAD STEP A, named by that ticket's arithmetic: SafeZoneRecovery restores a
// FRACTION OF MaxHp PER SECOND while the hero stands anywhere inside the town ring,
// and it has no combat check of any kind - no wave test, no recent-damage test. The
// whole walled town is inside that ring. Because the regen scales with MaxHp while
// incoming contact damage is capped by WaveScalingCurve's clamp, the gap WIDENS with
// every piece of gear. It can never be outgrown, at any level, on any wave.
//
// That is why this is the PREREQUISITE and not one option among several: a roster
// ten times heavier still cannot move a bar that is refilling faster than it drains.
//
// -----------------------------------------------------------------------------
// NO NUMBER FROM SafeZoneRecovery IS RESTATED ANYWHERE IN THIS FILE.
// -----------------------------------------------------------------------------
// The base regen FRACTION arrives as a PARAMETER. A baseline copied into a comment
// is the duplicated state CLAUDE.md sections 2 / 5 / 8 / 16 each record a scar from,
// and WO-1763 had to delete a stale "rewardMultiplier of 2.8" sentence from two
// files whose JSON had moved on beneath them. Read the fraction at its const.
//
// -----------------------------------------------------------------------------
// THE TWO AXES, AND WHY BOTH.
// -----------------------------------------------------------------------------
//   town.regenSuppressSecondsAfterHit   The OUT-OF-COMBAT rule. Regen is suppressed
//       entirely for N seconds after the hero last actually LOST HP. 0 = OFF, which
//       is today. This is the shape WO-1773 section 6 recommends, and the reason is
//       that it preserves BOTH things the regen exists for with no carve-out: the
//       between-waves top-up (the hero stops being hit, the timer runs out, HP comes
//       back) and the FTUE 1-HP floor recovery (nothing is hitting the hero there at
//       all, so the timer is never armed).
//
//   town.regenPctDuringWave             A PERCENT of the normal rate while a wave is
//       ACTIVE. 100 = today, bit for bit: this file short-circuits at 100 and hands
//       back the supplied fraction itself rather than fraction*100/100f, so "no row
//       => today's town, byte for byte" is a provable statement and not a
//       float-rounding argument. 0 zeroes regen for the whole wave.
//
// They COMPOSE, and the order is a ruling, not an accident: the recent-damage
// suppression is tested FIRST and wins. A hero being hit right now is in combat
// whatever the wave phase says - the raid/dungeon/overworld case reaches this same
// Update with no WaveManager at all.
//
// -----------------------------------------------------------------------------
// THE FTUE CARVE-OUT IS EXPLICIT, AND THAT IS DELIBERATE.
// -----------------------------------------------------------------------------
// SafeZoneRecovery's own header records that the tick regen exists BECAUSE of a real
// bug: the FTUE teaching wave floors the hero at 1 HP (HeroHealth's newHp<=0 guard
// under TutorialFlow.HostilesSuppressedForTutorial) and OnSceneLoaded never re-fires,
// so without the tick there is NO recovery path at all and the player is stuck at 1
// HP forever. Shape (ii) preserves that naturally - but "naturally" is an inference
// about what is hitting the hero during a scripted beat, and an inference is not a
// proof (CLAUDE.md section 11B). So the same predicate HeroHealth's 1-HP floor uses
// is checked here and returns FULL rate unconditionally. One line, and the worst
// case it prevents is a new player permanently stuck at 1 HP.
//
// Pure and static: no scene, no save, no network, no PlayerPrefs, no Time.time. The
// caller supplies "seconds since the hero lost HP" and "is a wave live", so an
// oracle asserts the whole table with nothing loaded.
//
// (!) THE CALLER CACHES. RemoteTunables.Int does a spec lookup AND a
// PlayerPrefs.GetInt per call; SafeZoneRecovery.Update runs every frame the hero is
// below full, so it holds the two ints behind a refresh interval rather than reading
// the rail per frame. Resolve() is the convenience door for a non-frame caller.
//
// ASCII only. FlowTrace tag "SafeZone". Never stripped (CLAUDE.md section 12).
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;

namespace DeNelle.Village
{
    /// <summary>
    /// Resolves and clamps the town-regen combat gate (how long after a hit regen stays off,
    /// and what share of the normal rate runs while a wave is live). Pure static; with no row,
    /// no network and no parse it answers the supplied fraction UNCHANGED - today's behaviour.
    /// </summary>
    public static class TownRegenTunables
    {
        /// <summary>Lowest suppression window. 0 is OFF and is the shipping default.</summary>
        public const int MinSuppressSeconds = 0;

        /// <summary>
        /// Highest suppression window. Two minutes: beyond this a hero who took one hit during a
        /// long wave would effectively never regen in town again, which is a different design
        /// (no town recovery at all) rather than an out-of-combat rule.
        /// </summary>
        public const int MaxSuppressSeconds = 120;

        /// <summary>Lowest in-wave share. 0 means no regen at all while a wave is live.</summary>
        public const int MinDuringWavePct = 0;

        /// <summary>
        /// Highest in-wave share. 100 is IDENTITY and is also the ceiling on purpose: this knob
        /// exists to make in-wave regen weaker, never stronger. A value above 100 would make
        /// standing in a wave better than standing between waves, which is the opposite of the
        /// defect the ticket was opened for.
        /// </summary>
        public const int MaxDuringWavePct = 100;

        /// <summary>The percent at which this file hands back the supplied fraction untouched.</summary>
        public const int IdentityDuringWavePct = RemoteTunables.TownRegenDuringWavePctIdentity;

        /// <summary>Reason label: nothing is gating the regen - the hero is out of combat.</summary>
        public const string ReasonOutOfCombat = "out-of-combat";

        /// <summary>Reason label: the hero lost HP inside the suppression window.</summary>
        public const string ReasonRecentDamage = "recent-damage";

        /// <summary>Reason label: a wave is live and the in-wave percent is below 100.</summary>
        public const string ReasonInWave = "in-wave";

        /// <summary>Reason label: the first-time tutorial is live, so the gate never applies.</summary>
        public const string ReasonFtue = "ftue-carve-out";

        /// <summary>
        /// The EFFECTIVE regen fraction for this frame, plus the reason, so the one
        /// <c>[Flow:SafeZone]</c> line can say WHY it did or did not heal.
        /// </summary>
        public struct Resolved
        {
            /// <summary>The fraction-of-MaxHp-per-second SafeZoneRecovery should actually apply. 0 = none.</summary>
            public float RateFraction;

            /// <summary>True when the gate zeroed the regen this frame.</summary>
            public bool Suppressed;

            /// <summary>One of the four Reason* labels. Human-readable, never parsed.</summary>
            public string Reason;

            /// <summary>The post-clamp suppression window actually in force, for the trace line.</summary>
            public int SuppressSeconds;

            /// <summary>The post-clamp in-wave percent actually in force, for the trace line.</summary>
            public int DuringWavePct;

            /// <summary>True when NEITHER axis changed anything, i.e. this is today's regen.</summary>
            public bool IsIdentity => !Suppressed && Reason == ReasonOutOfCombat;
        }

        /// <summary>
        /// Read both knobs off the rail and resolve. The convenience door for a caller that is NOT
        /// on a per-frame path; a frame caller reads the two ints on its own refresh interval and
        /// calls <see cref="ResolveFrom"/>, because <c>RemoteTunables.Int</c> costs a spec lookup
        /// and a <c>PlayerPrefs.GetInt</c> every time.
        /// </summary>
        public static Resolved Resolve(float baseFraction, bool waveActive,
                                       float secondsSinceHpLost, bool tutorialActive)
            => ResolveFrom(baseFraction, waveActive, secondsSinceHpLost, tutorialActive,
                           RemoteTunables.Int(RemoteTunables.KeyTownRegenSuppressSecondsAfterHit),
                           RemoteTunables.Int(RemoteTunables.KeyTownRegenPctDuringWave));

        /// <summary>
        /// The pure arithmetic, with the knob values SUPPLIED - the
        /// <see cref="RaidDifficultyTunables.ResolveFrom"/> precedent, so an oracle drives every
        /// branch of the gate with nothing loaded and no row written.
        ///
        /// <para>Clamping happens HERE and the caller receives the CLAMPED result, so there is
        /// exactly one answer to "what does town.regenSuppressSecondsAfterHit = 999999 do".</para>
        /// </summary>
        /// <param name="baseFraction">SafeZoneRecovery's own per-second fraction of MaxHp.</param>
        /// <param name="waveActive">TRUE only when a town wave is genuinely live.</param>
        /// <param name="secondsSinceHpLost">
        /// Seconds since the hero last actually LOST HP. <see cref="float.PositiveInfinity"/> when
        /// the hero has never been hit this session - NOT 0, which would read as "hit this frame".
        /// </param>
        /// <param name="tutorialActive">TutorialFlow.HostilesSuppressedForTutorial, the FTUE carve-out.</param>
        public static Resolved ResolveFrom(float baseFraction, bool waveActive,
                                           float secondsSinceHpLost, bool tutorialActive,
                                           int rawSuppressSeconds, int rawDuringWavePct)
        {
            int suppress = ClampAndReport(RemoteTunables.KeyTownRegenSuppressSecondsAfterHit,
                                          rawSuppressSeconds, MinSuppressSeconds, MaxSuppressSeconds,
                                          "suppression window (seconds)");
            int pct = ClampAndReport(RemoteTunables.KeyTownRegenPctDuringWave,
                                     rawDuringWavePct, MinDuringWavePct, MaxDuringWavePct,
                                     "in-wave percent");

            var r = new Resolved { SuppressSeconds = suppress, DuringWavePct = pct };

            // THE FTUE CARVE-OUT, FIRST AND UNCONDITIONAL. SafeZoneRecovery's header records that
            // the tick regen exists because the teaching wave floors the hero at 1 HP with no other
            // recovery path. Never gate that, whatever the rows say.
            if (tutorialActive)
            {
                r.RateFraction = baseFraction;
                r.Suppressed = false;
                r.Reason = ReasonFtue;
                return r;
            }

            // RECENT DAMAGE WINS OVER THE WAVE PHASE, and that ordering is the ruling. A hero being
            // hit right now is in combat whatever a wave phase says - and this same Update runs in
            // scenes that have no WaveManager at all.
            if (suppress > 0 && secondsSinceHpLost < suppress)
            {
                r.RateFraction = 0f;
                r.Suppressed = true;
                r.Reason = ReasonRecentDamage;
                return r;
            }

            if (waveActive && pct != IdentityDuringWavePct)
            {
                r.RateFraction = baseFraction * (pct / 100f);
                r.Suppressed = r.RateFraction <= 0f;
                r.Reason = ReasonInWave;
                return r;
            }

            // THE BIT-IDENTITY BRANCH. Handing back the supplied fraction itself rather than
            // fraction * 100 / 100f is what makes "no row => today's town, byte for byte" provable
            // instead of a float-rounding argument.
            r.RateFraction = baseFraction;
            r.Suppressed = false;
            r.Reason = ReasonOutOfCombat;
            return r;
        }

        // =====================================================================
        //  Clamps. Loud, once per key per process, never silent.
        // =====================================================================

        private static int ClampAndReport(string key, int raw, int min, int max, string what)
        {
            int clamped = Mathf.Clamp(raw, min, max);
            if (clamped == raw) return clamped;

            // Once per key per value per process: this is read on a frame path, and a per-frame
            // Warn would bury the town-regen line it is meant to annotate (CLAUDE.md section 12,
            // the logcat-ring lesson).
            FlowTrace.Once("SafeZone", "townregen-clamp-" + (key ?? what) + "-" + raw,
                "town regen " + what + " '" + (key ?? "(supplied directly)") + "' resolved to " +
                raw + ", outside " + min + ".." + max + " - CLAMPED to " + clamped + ". The regen " +
                "below uses the CLAMPED value. " +
                (max == MaxDuringWavePct && min == MinDuringWavePct
                    ? "100 is identity - the full rate. Above 100 is refused on purpose: this knob " +
                      "exists to make in-wave regen weaker, never stronger."
                    : "0 is identity - no suppression, which is what ships."));
            return clamped;
        }
    }
}
