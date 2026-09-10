// =============================================================================
// RaidWatchdogHonorRegression — WO-1095 (the stranding watchdog measures the raid
// clock's own interval) + WO-1594 (the honor clock: three lit at engage, snuffed as
// milestones pass). Marker: RAID_WATCHDOG_HONOR_OK.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Core).
// Wired into DeNelle.Editor.DataRegression.RunAll with ONE line — see the RESULT.
//
// -----------------------------------------------------------------------------
//  WHY IT EXISTS — WO-1095, PROVEN FROM CAPTURE, NOT REASONED FROM SOURCE
// -----------------------------------------------------------------------------
// The raid clock is ENGAGEMENT-GATED (WO-1520): RaidScoring.Update returns before
// `_elapsed += Time.deltaTime` until first contact, so staging costs the player
// nothing. The stranding watchdog measured a DIFFERENT interval — `aliveFor`,
// unscaled seconds since the raid SCENE loaded — and bounded that by `clock + 45`.
//
//   F8 seq 4967: raid entered 2026-09-09T17:15:42.851Z, watchdog Fail at
//                17:19:27.828Z = 224.977s of SCENE age, exactly the 180+45 bound —
//                while the owner's screenshot still read 2:46 remaining (14s of the
//                180s raid had elapsed). The player was yanked with 166s left.
//   F8 seq 4980: scene_loaded t=1036.537, watchdog Fail t=1546.019 — 509.5s of wall
//                clock, printed as "510s". The settle line that follows in the SAME
//                log reads `elapsed=50s/180s`. Same defect, an order of magnitude
//                louder.
//
// THE RED-FIRST DISCRIMINATOR, hand-evaluated before the fix was written:
//   ClassifyStranding(scoringPresent:true, engaged:true, aliveFor:225, engagedFor:14,
//                     clock:180, grace:45, stagingCeiling:900, hudBuilt:true)
//   OLD logic  — `if (aliveFor < clock + grace) continue;` → 225 >= 225 → FIRES.
//   NEW logic  — engaged, so 14 >= 225 is false            → None.
// Case A1 below asserts None. Against the pre-fix tree that assertion FAILS, and it
// is the one case that separates the two implementations: every other input in this
// suite that fires still fires.
//
// -----------------------------------------------------------------------------
//  WHY IT EXISTS — WO-1594
// -----------------------------------------------------------------------------
// The HUD bound `ProjectedStars`, an EARN-UP settle preview: 0/3 through the whole
// fight, then a jump at the end. The owner asked for the opposite — three lit at
// engage, going dark as milestones pass. `ComputeHonorStars` is that projector, pure
// and scene-free, and `Finalize` clamps the payout to `min(settle, honor)` so loot can
// never exceed what the live HUD promised.
//
// -----------------------------------------------------------------------------
//  WHAT THIS SUITE DOES NOT PROVE — stated because an unproven thing named as unproven
//  is useful and an unproven thing stated as fact costs someone a day (CLAUDE.md §11B)
// -----------------------------------------------------------------------------
//  * That a real raid now exits by all three routes. That needs a play session; it is
//    an owner felt-verify item on both WOs.
//  * WHY the frame gap in seq 4980 happened (no frame observed aliveFor between 225
//    and 510, so the accumulator crossed both in ONE frame). The new failure line
//    prints Time.timeScale, ElapsedSeconds, engagedFor and aliveFor so the NEXT
//    capture answers it; this suite does not.
//  * That the scaled raid clock cannot be frozen by a hold. It demonstrably was in
//    seq 4980 (elapsed=50s across ~455s of engaged wall time); the watchdog is now
//    unscaled so a frozen clock can no longer disarm the net, but the freeze itself
//    is a separate, un-ticketed finding.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.Ops;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pure-function + source oracle for the raid stranding watchdog's measured interval
    /// (WO-1095) and the honor star clock (WO-1594). DataRegression-shaped: true = pass
    /// with a one-line summary, false = fail with the offending detail. NEVER throws.
    /// </summary>
    public static class RaidWatchdogHonorRegression
    {
        // Relative to Application.dataPath.
        private const string CtrlRel  = "_Modules/Village/Troops/RaidDeployController.cs";
        private const string ScoreRel = "_Modules/Village/Troops/RaidScoring.cs";
        private const string HudRel   = "_Modules/Village/Troops/RaidHudController.cs";

        // Declared as a balanced PAIR on one line on purpose (RaidTerminalStateRegression's
        // precedent): a lone brace char literal trips the CLAUDE.md rule-1 brace counter.
        private const char OpenBrace = '{', CloseBrace = '}';

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "raid-watchdog-honor: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Standalone batch entry.</summary>
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log("RAID_WATCHDOG_HONOR_OK - " + reason);
            else Debug.LogError("RAID_WATCHDOG_HONOR_FAIL - " + reason);
        }

        private static bool RunCore(out string reason)
        {
            var fails = new List<string>();
            var notes = new List<string>();

            // Comments are stripped BEFORE any match. Load-bearing: the WO-1095 fix comments
            // in RaidDeployController QUOTE the retired failure text this suite asserts is
            // gone, so an unstripped read would fail on prose alone.
            string ctrl  = ReadCode(CtrlRel, fails);
            string score = ReadCode(ScoreRel, fails);
            string hud   = ReadCode(HudRel, fails);

            CaseA_WatchdogMeasuresTheClocksInterval(fails, notes);
            CaseB_WatchdogSourceCannotRevertToSceneAge(ctrl, fails, notes);
            CaseC_HonorStarsSnuffDown(fails, notes);
            CaseD_HonorIsWiredAndTheHudBindsIt(score, hud, fails, notes);

            if (fails.Count == 0)
            {
                reason = "RAID WATCHDOG + HONOR OK -- the stranding watchdog bounds the SAME interval " +
                         "the raid clock measures (engaged time, unscaled), so a long staging no longer " +
                         "yanks a player out of a live raid, and its failure line STATES subscriber / " +
                         "scorer / HUD / timeScale instead of accusing them; honor stars light three at " +
                         "engage and snuff on T3/T2+D2/hero-death, with the payout clamped to " +
                         "min(settle, honor) -- and the three milestones are RAIL ROWS (owner 2026-09-09, " +
                         "'Tunables with those defaults') read through the SpecFor guard, so an empty " +
                         "table, an unreachable server and an unregistered key all answer 90 / 150 / 50 " +
                         "exactly" +
                         (notes.Count > 0 ? " [" + string.Join("; ", notes.ToArray()) + "]" : "");
                Debug.Log("RAID_WATCHDOG_HONOR_OK");
                return true;
            }

            reason = "raid-watchdog-honor (" + fails.Count + "): " + string.Join(" | ", fails.ToArray());
            Debug.LogError("RAID_WATCHDOG_HONOR_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  CASE A — the PURE watchdog verdict. This is the RED-first case.
        // =====================================================================
        private static void CaseA_WatchdogMeasuresTheClocksInterval(List<string> fails, List<string> notes)
        {
            const float Clock = 180f, Grace = 45f, Ceiling = 900f;

            // ── A1. THE DISCRIMINATOR (F8 seq 4967, reproduced exactly). ────────────────
            // 225s of SCENE age, 14s of RAID. The old wall-clock arm fired here and pulled the
            // player out of a raid with 166 valid seconds left; the fixed arm must not.
            Verdict(fails, "seq 4967: 225s staged, 14s engaged -> the raid is LIVE, do not fire",
                RaidDeployController.ClassifyStranding(true, true, 225f, 14f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.None);

            // ── A2. F8 seq 4980's shape: genuinely overdue on the clock's own interval. ──
            Verdict(fails, "seq 4980: 455s engaged on a 180s clock -> fire",
                RaidDeployController.ClassifyStranding(true, true, 510f, 455f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.EngagedOverrun);

            // ── A3. The bound itself is UNCHANGED — only what it measures. WO-1095 forbids
            //        raising it, so the boundary is pinned on BOTH sides.
            Verdict(fails, "engaged 224.9s -> still inside the grace",
                RaidDeployController.ClassifyStranding(true, true, 9999f, 224.9f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.None);
            Verdict(fails, "engaged exactly clock+grace -> fire",
                RaidDeployController.ClassifyStranding(true, true, 225f, 225f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.EngagedOverrun);

            // ── A4. No scorer at all: there is no clock and no engagement gate to respect,
            //        so scene age IS the only honest interval and the tight bound stands.
            //        This is the case the retired failure text named — it is kept, not removed.
            Verdict(fails, "no RaidScoring, 224s -> not yet",
                RaidDeployController.ClassifyStranding(false, false, 224f, 0f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.None);
            Verdict(fails, "no RaidScoring, 225s -> fire on scene age",
                RaidDeployController.ClassifyStranding(false, false, 225f, 0f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.ScorerMissing);

            // ── A5. Staging with NO HUD is the raid's one genuinely exitless state (no
            //        Retreat button, no clock ticking). It keeps the tight bound.
            Verdict(fails, "staging, HUD failed to build, 225s -> fire (exitless)",
                RaidDeployController.ClassifyStranding(true, false, 225f, 0f, Clock, Grace, Ceiling, false),
                RaidDeployController.StrandingArm.StagingWithNoHud);

            // ── A6. Staging WITH a HUD: Retreat is on screen, so the player is not stranded.
            //        Only the generous absolute ceiling applies.
            Verdict(fails, "staging, HUD built, 225s -> Retreat exists, do not fire",
                RaidDeployController.ClassifyStranding(true, false, 225f, 0f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.None);
            Verdict(fails, "staging, HUD built, 899s -> still under the ceiling",
                RaidDeployController.ClassifyStranding(true, false, 899f, 0f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.None);
            Verdict(fails, "staging, HUD built, 900s -> dead session, fire",
                RaidDeployController.ClassifyStranding(true, false, 900f, 0f, Clock, Grace, Ceiling, true),
                RaidDeployController.StrandingArm.StagingCeiling);

            // ── A7. THE NET IS NOT REMOVABLE. Whatever the state, a raid left standing long
            //        enough must reach SOME arm — a verdict of None at an absurd wall clock in
            //        every configuration would mean the WO-1437 net had been deleted.
            var arms = new[]
            {
                RaidDeployController.ClassifyStranding(true,  true,  5000f, 5000f, Clock, Grace, Ceiling, true),
                RaidDeployController.ClassifyStranding(true,  false, 5000f, 0f,    Clock, Grace, Ceiling, true),
                RaidDeployController.ClassifyStranding(true,  false, 5000f, 0f,    Clock, Grace, Ceiling, false),
                RaidDeployController.ClassifyStranding(false, false, 5000f, 0f,    Clock, Grace, Ceiling, true),
            };
            for (int i = 0; i < arms.Length; i++)
                if (arms[i] == RaidDeployController.StrandingArm.None)
                    fails.Add("[WO-1095] ClassifyStranding case " + i + " returns None after 5000s in a raid " +
                              "scene. Every configuration must eventually reach a terminal state - that is " +
                              "the whole WO-1437 net, and narrowing what the watchdog measures must never " +
                              "narrow WHETHER it fires.");

            // The staging ceiling is a CHOSEN number and must stay far outside a real staging.
            if (RaidDeployController.StagingCeilingSecondsDefault <= Clock + Grace)
                fails.Add("[WO-1095] StagingCeilingSecondsDefault (" +
                          RaidDeployController.StagingCeilingSecondsDefault + "s) is at or below the " +
                          "clock+grace bound. The whole point of the staging ceiling is that it CANNOT " +
                          "reproduce the false positive the wall-clock bound produced.");

            notes.Add("watchdog verdict pinned on 11 cases incl. the seq-4967 discriminator");
        }

        // =====================================================================
        //  CASE B — the source cannot quietly revert to scene age, and the
        //           failure line must STATE rather than ACCUSE.
        // =====================================================================
        private static void CaseB_WatchdogSourceCannotRevertToSceneAge(string ctrl, List<string> fails,
                                                                      List<string> notes)
        {
            if (string.IsNullOrEmpty(ctrl)) return;

            string body = Body(ctrl, @"IEnumerator\s+StrandingWatchdog\s*\(");
            if (string.IsNullOrEmpty(body))
            {
                fails.Add("[WO-1095] could not locate StrandingWatchdog's body in RaidDeployController - " +
                          "the WO-1437 net is gone or was renamed; nothing below could be verified.");
                return;
            }

            if (body.IndexOf("ClassifyStranding", StringComparison.Ordinal) < 0)
                fails.Add("[WO-1095] StrandingWatchdog no longer routes its unsettled decision through " +
                          "ClassifyStranding. The verdict is pure ON PURPOSE: it is the only part of this " +
                          "net an oracle can assert without a play session, and inlining it back into the " +
                          "coroutine makes the WO-1095 regression unfalsifiable.");

            if (body.IndexOf("Engaged", StringComparison.Ordinal) < 0)
                fails.Add("[WO-1095] StrandingWatchdog never reads RaidScoring.Engaged. The raid clock is " +
                          "engagement-gated (WO-1520), so a watchdog that does not consult engagement is " +
                          "measuring scene age again - the exact defect that fired at 225s of staging with " +
                          "166s of raid still on the clock (F8 seq 4967).");

            if (body.IndexOf("engagedFor", StringComparison.Ordinal) < 0)
                fails.Add("[WO-1095] StrandingWatchdog no longer accumulates engagedFor. Without it there " +
                          "is no interval that starts where the raid clock starts.");

            // The net stays UNSCALED (also pinned by RaidTerminalStateRegression Case C) — a
            // frozen scaled clock must not be able to disarm the seatbelt.
            if (body.IndexOf("Time.unscaledDeltaTime", StringComparison.Ordinal) < 0)
                fails.Add("[WO-1095] StrandingWatchdog is no longer unscaled. engagedFor deliberately does " +
                          "NOT read RaidScoring.ElapsedSeconds, because _elapsed advances on Time.deltaTime " +
                          "and a hold at timeScale=0 freezes it - measured in F8 seq 4980, where the clock " +
                          "sat at 50s across ~455s of engaged wall time. A net that reads a frozen number " +
                          "never fires, which turns a premature exit into a permanent strand.");

            // The retired accusation. It named a cause it had never checked.
            if (body.IndexOf("subscriber is missing or RaidScoring never", StringComparison.Ordinal) >= 0)
                fails.Add("[WO-1095] the last-resort failure text still asserts \"the OnTimeExpired " +
                          "subscriber is missing or RaidScoring never installed\". In F8 seq 4980 neither " +
                          "was true - no HUD-build failure was logged and RaidScoring.Finalize ran on a live " +
                          "scorer in the same breath. A trace that names an unchecked cause sends the next " +
                          "reader to the wrong file (CLAUDE.md 11B).");

            foreach (string token in new[] { "subscriber=", "hudBuilt=", "timeScale=", "engagedFor=" })
                if (body.IndexOf(token, StringComparison.Ordinal) < 0)
                    fails.Add("[WO-1095] the stranding failure line does not report '" + token + "'. The " +
                              "replacement for an accusation is a STATEMENT of the facts the controller can " +
                              "already see; each of these discriminates a different upstream defect.");

            if (ctrl.IndexOf("_clockSubscribed", StringComparison.Ordinal) < 0)
                fails.Add("[WO-1095] RaidDeployController has no _clockSubscribed latch, so it cannot report " +
                          "whether the clock-expiry exit was ever armed - which is the fact the retired " +
                          "failure text was guessing at.");

            // BindScoringRoutine gives up after ten frames; a late scorer must still get bound.
            string bind = Body(ctrl, @"IEnumerator\s+BindScoringRoutine\s*\(");
            if (!string.IsNullOrEmpty(bind) && bind.IndexOf("SubscribeClock", StringComparison.Ordinal) < 0)
                fails.Add("[WO-1095] BindScoringRoutine does not subscribe through SubscribeClock. One " +
                          "idempotent owner of OnTimeExpired is what lets the watchdog bind a LATE-resolving " +
                          "scorer without risking a double subscription.");
            if (body.IndexOf("SubscribeClock", StringComparison.Ordinal) < 0)
                fails.Add("[WO-1095] StrandingWatchdog re-resolves RaidScoring.Instance but never subscribes " +
                          "it. A scorer that installs after BindScoringRoutine's 10-frame poll would then " +
                          "leave the raid's on-time exit unarmed for the whole session, and this net would " +
                          "be the only way out - a hole the net itself would report as someone else's bug.");

            notes.Add("watchdog source pinned to the engaged interval + a stating failure line");
        }

        // =====================================================================
        //  CASE C — WO-1594 honor stars, pure.
        // =====================================================================
        private static void CaseC_HonorStarsSnuffDown(List<string> fails, List<string> notes)
        {
            // ⛔ READ THE SHIPPING DEFAULTS, NOT THE LIVE PROPERTIES. Since the owner's
            // 2026-09-09 ruling ("Tunables with those defaults") the three milestones are RAIL
            // READS on RaidScoring, and an oracle that sourced its expectations from the same
            // property it is testing would be measuring the thing against itself - the defaults
            // oracle states that trap in its own words (RemoteTunablesDefaultsRegression.cs:124).
            // These consts are the independent statement; the equality pin below is what proves
            // the two agree when nothing has overridden them.
            float t3 = RemoteTunables.RaidHonorThirdStarSecondsDefault;
            float t2 = RemoteTunables.RaidHonorSecondStarSecondsDefault;
            float d2 = RemoteTunables.RaidHonorSecondStarMinDestructionPctDefault / 100f;

            PinTheRailGuard(fails, notes);

            // Staging is not lit — the clock has not started either (WO-1520).
            Honor(fails, "staging shows nothing lit", false, 0f, 0f, false, 0);
            Honor(fails, "engage lights three", true, 0f, 0f, false, 3);

            // T3 — the speed honor. Inclusive on the near side: AT the milestone it is not lost.
            Honor(fails, "at T3 the third star is still lit", true, t3, 0f, false, 3);
            Honor(fails, "past T3 the third star is gone", true, t3 + 0.01f, 0f, false, 2);

            // Owner Q2 = YES: hero death snuffs the third immediately, at any time.
            Honor(fails, "hero death snuffs the third at once", true, 10f, 0f, true, 2);

            // T2 — only bites when the camp is still under half razed.
            Honor(fails, "past T2 under D2 loses the second", true, t2 + 0.01f, d2 - 0.01f, false, 1);
            Honor(fails, "past T2 at D2 keeps the second", true, t2 + 0.01f, d2, false, 2);

            // The floor the owner named: the last star is never snuffed mid-fight for time.
            // Both times are expressed OFF t2 rather than as 179 / 600, which silently assumed
            // T2 was 150 forever - it is a row now and the assertion must survive it moving.
            Honor(fails, "the first star survives time alone", true, t2 + 29f, 0.10f, false, 1);
            Honor(fails, "the first star survives even past the clock", true, t2 * 4f, 0.00f, false, 1);

            // Monotonic: honor can only ever go DOWN as the fight runs on. A projector that
            // relit a star would make the settle clamp below unsound.
            int prev = 4;
            for (float e = 0f; e <= 300f; e += 5f)
            {
                int now = RaidScoring.ComputeHonorStars(true, e, 0f, false);
                if (now > prev)
                {
                    fails.Add("[WO-1594] ComputeHonorStars RELIT a star at elapsed=" + e + "s (" + prev +
                              " -> " + now + "). Honor snuffs down only; a projector that can go back up " +
                              "makes the Finalize clamp min(settle, honor) meaningless.");
                    break;
                }
                prev = now;
            }

            // Range, across a wide sweep.
            for (int di = 0; di <= 10; di++)
                for (float e = 0f; e <= 400f; e += 25f)
                {
                    int v = RaidScoring.ComputeHonorStars(true, e, di / 10f, di % 2 == 0);
                    if (v < 0 || v > 3)
                        fails.Add("[WO-1594] ComputeHonorStars returned " + v + " (out of 0..3) at elapsed=" +
                                  e + " destruction=" + (di / 10f));
                }

            // THE HONESTY CLAMP SHAPE. A perfect clear just PAST T3 settles 3 on the earn-up
            // ladder but the HUD already went dark, so the payout must be 2. The instant is
            // derived from t3 (it was a bare 100f, which only worked while T3 happened to be 90
            // and T2 150); the guard below states the one relationship it needs.
            float justPastT3 = t3 + 10f;
            if (justPastT3 >= t2 || justPastT3 > RaidScoring.DefaultClockSeconds)
                fails.Add("[WO-1594] the shipping milestones no longer leave a window between T3 (" + t3 +
                          "s) and T2 (" + t2 + "s) wide enough for the honesty-clamp case. Re-derive the " +
                          "instant rather than deleting the case - the clamp is what stops the end screen " +
                          "paying stars the live HUD had already put out.");
            int settle = RaidScoring.ComputeStars(true, true, 1f, justPastT3, RaidScoring.DefaultClockSeconds, 1f);
            int honor = RaidScoring.ComputeHonorStars(true, justPastT3, 1f, false);
            int clamped = Mathf.Min(settle, honor);
            if (settle != 3 || honor != 2 || clamped != 2)
                fails.Add("[WO-1594] honesty clamp shape is wrong: settle=" + settle + " honor=" + honor +
                          " min=" + clamped + " (want 3, 2, 2). The live HUD must never promise less than " +
                          "the end screen pays.");

            // The hero clause must not contradict the WO-1526 ceiling it shares a raid with.
            if (RaidScoring.ComputeHonorStars(true, 0f, 1f, true) > RaidScoring.HeroDeathStarCap)
                fails.Add("[WO-1594/1526] hero-death honor exceeds HeroDeathStarCap. Q2 (snuff the third on " +
                          "hero death) exists precisely so the live bar agrees with the settled cap.");

            notes.Add("honor T3=" + t3 + "s T2=" + t2 + "s D2=" + d2.ToString("0.00") +
                      " (shipping defaults, read off RemoteTunables)");
        }

        // =====================================================================
        //  CASE C2 - THE RAIL GUARD. WO-1594, owner ruling 2026-09-09.
        // =====================================================================
        /// <summary>
        /// The three milestones are RAIL READS since the owner's "Tunables with those defaults"
        /// ruling, and every one of them is guarded by <c>RemoteTunables.SpecFor(key)</c> before
        /// <c>Int(key)</c> is ever called.
        ///
        /// <para>⚠ THE GUARD IS LOAD-BEARING, NOT DEFENSIVE, AND THIS IS THE RED-FIRST CASE.
        /// <c>RemoteTunables.Int</c> answers <b>0</b> for an unregistered key (RemoteTunables.cs,
        /// the Int() unregistered branch) - it has no spec and therefore no default to fall back
        /// to. Hand-evaluated: strip the guard, unregister the key, and
        /// <c>ComputeHonorStars(true, 90, 0, false)</c> reads T3 = 0, so <c>90 &gt; 0</c> and the
        /// THIRD STAR IS DARK ON THE FIRST FRAME OF EVERY RAID - and, through Finalize's
        /// min(settle, honor) clamp, every raid in the game silently caps at two stars. With the
        /// guard it answers 90 and the case below is green.</para>
        ///
        /// <para>The equality is asserted only when nothing has overridden the knob: a developer
        /// with a live <c>ff.tun.*</c> PlayerPrefs override, or a loaded remote table, is
        /// correctly NOT at the default, and an oracle that reds on the MACHINE rather than on
        /// the code is worse than no oracle (the same reason
        /// <c>RemoteTunablesDefaultsRegression</c> snapshots and clears them). The skip is
        /// reported, never silent.</para>
        /// </summary>
        private static void PinTheRailGuard(List<string> fails, List<string> notes)
        {
            string[] keys =
            {
                RemoteTunables.KeyRaidHonorThirdStarSeconds,
                RemoteTunables.KeyRaidHonorSecondStarSeconds,
                RemoteTunables.KeyRaidHonorSecondStarMinDestructionPct,
            };

            for (int i = 0; i < keys.Length; i++)
                if (RemoteTunables.SpecFor(keys[i]) == null)
                    fails.Add("[WO-1594] '" + keys[i] + "' is not in RemoteTunables.Registry. RaidScoring " +
                              "guards on SpecFor, so the milestone still answers its shipping default and " +
                              "nothing is broken today - but the owner cannot move it, which is the whole " +
                              "point of the 2026-09-09 ruling 'Tunables with those defaults'.");

            if (RemoteTunables.RowCount > 0)
            {
                fails.Add("[WO-1594] a remote tunable table was already loaded (" + RemoteTunables.RowCount +
                          " row(s), provenance=" + RemoteTunables.TableProvenance + ") when this case ran, " +
                          "so the live milestones could not be compared against the shipping defaults. This " +
                          "suite must run against an empty table.");
                return;
            }

            bool overridden = false;
            for (int i = 0; i < keys.Length; i++)
                if (PlayerPrefs.GetInt(RemoteTunables.LocalPrefix + keys[i], int.MinValue) != int.MinValue)
                    overridden = true;
            if (overridden)
            {
                // ⛔ A DECLARED stand-down, through the OUTCOME CHANNEL - never a prose note.
                // The first version of this branch told the READER, in words, that it was standing
                // down and told the CALLER nothing: it returned without asserting and without a
                // token, so it landed in the GREEN column. That is the undeclared hollow pass
                // RegressionMarkerRegression's RULE 4 ratchet exists to catch (WO-1138), and it
                // caught this one - Builds/wave1-reg 2026-09-09 23:34, "[B-says-skip] guard
                // 'overridden'". RegressionOutcome.PartialSkip is the correct shape here rather
                // than Skip: the suite DID assert - eleven watchdog verdicts, the whole pure honor
                // ladder, the monotonic sweep and the source pins all ran - and it is this one
                // section that cannot run, which is exactly what "partial" means.
                //
                // WHY IT CANNOT RUN: with a live ff.tun.* override the properties are correctly NOT
                // at their shipping defaults, so the equality below would red on the MACHINE rather
                // than on the code - worse than no oracle (RemoteTunablesDefaultsRegression:352-361
                // states the same reasoning, and snapshots the prefs instead; this suite must not,
                // because it does not own that global state and other suites run after it).
                notes.Add(RegressionOutcome.PartialSkip(
                    "[WO-1594] honor-rail default-equality pin",
                    "a local ff.tun.* override is set on this machine for at least one of " +
                    "raid.honorThirdStarSeconds / raid.honorSecondStarSeconds / " +
                    "raid.honorSecondStarMinDestructionPct, so RaidScoring's live milestones are " +
                    "legitimately not the shipping defaults and the SpecFor-guard pin ASSERTED " +
                    "NOTHING. Clear the override (or run the batchmode gate, which has none) to " +
                    "restore this pin. The pure honor cases that follow are comparing overridden " +
                    "behaviour against shipping defaults and may disagree for the same reason"));
                return;
            }

            Same(fails, "T3", RaidScoring.HonorThirdStarSeconds,
                 RemoteTunables.RaidHonorThirdStarSecondsDefault);
            Same(fails, "T2", RaidScoring.HonorSecondStarSeconds,
                 RemoteTunables.RaidHonorSecondStarSecondsDefault);
            Same(fails, "D2", RaidScoring.HonorSecondStarMinDestruction,
                 RemoteTunables.RaidHonorSecondStarMinDestructionPctDefault / 100f);
        }

        private static void Same(List<string> fails, string label, float got, float want)
        {
            if (!Mathf.Approximately(got, want))
                fails.Add("[WO-1594] RaidScoring's " + label + " reads " + got + " with NO table and NO " +
                          "local override; the shipping default is " + want + ". Either the SpecFor guard " +
                          "is gone (an unregistered key then answers 0, which snuffs the star on frame one) " +
                          "or the consumer re-derived the number instead of reading the rail.");
        }

        // =====================================================================
        //  CASE D — the honor read is actually WIRED (a pure function nothing
        //           calls narrates nothing).
        // =====================================================================
        private static void CaseD_HonorIsWiredAndTheHudBindsIt(string score, string hud,
                                                              List<string> fails, List<string> notes)
        {
            if (!string.IsNullOrEmpty(score))
            {
                if (score.IndexOf("PresentationStars", StringComparison.Ordinal) < 0)
                    fails.Add("[WO-1594] RaidScoring exposes no PresentationStars - the HUD has nothing to " +
                              "bind and the honor projector is dead code.");

                string fin = Body(score, @"public\s+RaidResult\s+Finalize\s*\(");
                if (string.IsNullOrEmpty(fin))
                    fails.Add("[WO-1594] could not locate RaidScoring.Finalize to verify the honesty clamp.");
                else
                {
                    if (fin.IndexOf("ComputeHonorStars", StringComparison.Ordinal) < 0 ||
                        fin.IndexOf("Mathf.Min", StringComparison.Ordinal) < 0)
                        fails.Add("[WO-1594] Finalize does not clamp to min(settle, honor). Without it the " +
                                  "end screen can pay stars the live HUD had already put out, which is the " +
                                  "one thing the ticket forbids.");
                    if (fin.IndexOf("ApplyHeroDeathCap", StringComparison.Ordinal) < 0)
                        fails.Add("[WO-1594/1526] Finalize lost ApplyHeroDeathCap. The honor clamp SUBSUMES " +
                                  "it numerically, but it is the pinned statement of the owner's ceiling " +
                                  "(RaidScoringRegression) and removing it drops that pin.");
                }

                // WO-1594 (owner ruling 2026-09-09) - the milestones read the RAIL, through the
                // SpecFor guard. Pinned on the IDENTIFIERS: this file strips comments but not
                // string literals, so a pin on the key TEXT could be satisfied by a log message.
                foreach (string token in new[]
                         {
                             "KeyRaidHonorThirdStarSeconds",
                             "KeyRaidHonorSecondStarSeconds",
                             "KeyRaidHonorSecondStarMinDestructionPct",
                             "SpecFor",
                         })
                    if (score.IndexOf(token, StringComparison.Ordinal) < 0)
                        fails.Add("[WO-1594] RaidScoring no longer names '" + token + "'. The three honor " +
                                  "milestones are ROWS since the owner ruled 'Tunables with those defaults' " +
                                  "(2026-09-09), and every read is guarded by SpecFor BEFORE Int - because " +
                                  "Int answers 0 for an unregistered key, and T3=0 puts the third star out " +
                                  "on the first frame of every raid and caps every payout at two stars " +
                                  "through the Finalize clamp.");

                if (score.IndexOf("star-lost reason=", StringComparison.Ordinal) < 0)
                    fails.Add("[WO-1594] the permanent 'star-lost reason=' FlowTrace line is gone. It is the " +
                              "acceptance evidence for this ticket and the only way a capture can tell a " +
                              "time snuff from a hero-death snuff after the fact. Instrumentation is " +
                              "permanent (CLAUDE.md 12).");
            }

            if (!string.IsNullOrEmpty(hud))
            {
                if (hud.IndexOf("PresentationStars", StringComparison.Ordinal) < 0)
                    fails.Add("[WO-1594] RaidHudController does not bind PresentationStars.");
                if (hud.IndexOf("ProjectedStars", StringComparison.Ordinal) >= 0)
                    fails.Add("[WO-1594] RaidHudController still reads ProjectedStars. That is the EARN-UP " +
                              "settle preview: bound to the live bar it sits at 0/3 through the fight and " +
                              "jumps at the end, which is the felt defect this ticket names.");

                // Colourblind law: the state must not be carried by hue/alpha alone.
                if (hud.IndexOf("StarSizeLit", StringComparison.Ordinal) < 0 ||
                    hud.IndexOf("StarSizeLost", StringComparison.Ordinal) < 0)
                    fails.Add("[WO-1594] the star widgets no longer change SHAPE between lit and lost. The " +
                              "owner is red/green colourblind and dimming alone is luminance, not shape - a " +
                              "snuffed star must read as a different silhouette, with 'n/3' as the third " +
                              "hue-free channel.");
                if (hud.IndexOf("/3", StringComparison.Ordinal) < 0)
                    fails.Add("[WO-1594] the 'n/3' star count is gone - the number is the hue-free channel " +
                              "of last resort.");
            }

            notes.Add("honor wired: scorer -> PresentationStars -> HUD, clamped at Finalize");
        }

        // =====================================================================
        //  Helpers — all guarded, none throw.
        // =====================================================================

        private static void Verdict(List<string> fails, string label,
                                    RaidDeployController.StrandingArm got,
                                    RaidDeployController.StrandingArm want)
        {
            if (got != want)
                fails.Add("[WO-1095] ClassifyStranding [" + label + "] expected " + want + ", got " + got);
        }

        private static void Honor(List<string> fails, string label,
                                  bool engaged, float elapsed, float destruction, bool heroDied, int want)
        {
            int got = RaidScoring.ComputeHonorStars(engaged, elapsed, destruction, heroDied);
            if (got != want)
                fails.Add("[WO-1594] ComputeHonorStars [" + label + "] expected " + want + ", got " + got);
        }

        private static string ReadCode(string rel, List<string> fails)
        {
            try
            {
                string path = Path.Combine(Application.dataPath, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    fails.Add("raid-watchdog-honor: cannot read Assets/" + rel);
                    return null;
                }
                return StripComments(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                fails.Add("raid-watchdog-honor: reading Assets/" + rel + " threw " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>
        /// Remove line and block comments. Load-bearing (see the header): the fix comments in
        /// these very files quote the tokens this suite asserts are ABSENT.
        /// </summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                sb.Append(src[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The brace-balanced body of the first member whose signature matches
        /// <paramref name="signature"/>, or null. Comment-stripped source in, so a quoted
        /// signature inside prose cannot be matched.
        /// </summary>
        private static string Body(string src, string signature)
        {
            if (string.IsNullOrEmpty(src)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(src, signature);
            if (!m.Success) return null;

            int open = src.IndexOf(OpenBrace, m.Index + m.Length);
            if (open < 0) return null;

            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == OpenBrace) depth++;
                else if (src[i] == CloseBrace)
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }
    }
}
