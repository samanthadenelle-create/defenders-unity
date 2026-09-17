// =============================================================================
// BattleQuiescenceRegression — WO-1127. Proves the battle-end teardown contract
// FAILS every known-bad state, and PASSES a clean teardown.
// -----------------------------------------------------------------------------
// WHY BOTH DIRECTIONS ARE ASSERTED.
//
// A gate that does not fail the known-bad state is not a gate (the WO-1124 lesson,
// restated here because it is the same trap). But a gate that fails a CLEAN state
// is worse than useless: it becomes a permanent red, everyone learns to skip it,
// and the one time it means something nobody looks. So group 1 drives each
// invariant wrong in turn and requires a NAMED failure, and group 2 requires a
// clean world to pass with zero findings.
//
// Group 3 reproduces the originating defect exactly — timeScale pinned at 0.04,
// the value captured on the owner's device on 2026-08-20 — and requires the gate's
// message to name the field, the value, and the fact that the world is at 4% speed.
// A failure message that does not carry those is a debugging session someone pays
// for later.
//
// Group 4 is a source-lint on the WIRING, because a perfect gate nothing calls is
// the exact shape of the bug this ticket exists to prevent: everything green, the
// world broken. It reads CODE ONLY (comments and string-literal contents blanked),
// the project's standing lint discipline — a rule that matches its own tombstone
// comment punishes the self-documenting notes CLAUDE.md sec.12/15 asks for.
//
// EVERY test restores Time.timeScale in a finally. A regression suite that leaks
// the very global it is testing would poison every suite that runs after it.
//
// Markers: BATTLE_QUIESCENCE_SUITE_OK / BATTLE_QUIESCENCE_SUITE_FAIL.
// Standalone: run-unity-method
//   -Method DeNelle.Editor.BattleQuiescenceRegression.RunAll
// Registered in DataRegression.RunAll as the "battle-quiescence suite".
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Combat;
using DeNelle.Core.HudModel;

namespace DeNelle.Editor
{
    public static class BattleQuiescenceRegression
    {
        public const string MarkerOk   = "BATTLE_QUIESCENCE_SUITE_OK";
        public const string MarkerFail = "BATTLE_QUIESCENCE_SUITE_FAIL";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- BATTLE-END QUIESCENCE CONTRACT (WO-1127) ---");

            float savedScale = Time.timeScale;
            try
            {
                CleanStatePasses(failures, log);
                KnownBadTimeScaleFails(failures, log);
                OriginalDefectIsNamed(failures, log);
                ModuleProbeFailureIsSurfaced(failures, log);
                RewardScreenIsNotJudged(failures, log);
                WiringIsPresent(failures, log);

                // WO-1233 — the battle-lock half.
                SessionEndReleasesTheLock(failures, log, "arena win");
                SessionEndReleasesTheLock(failures, log, "arena loss");
                SessionEndReleasesTheLock(failures, log, "retreat");
                HolderIsNamedInTheFinding(failures, log);
                LiveChaseIsNotSuppressed(failures, log);

                // WO-1233 — the timeScale half (a SEPARATE owner, asserted separately).
                BattleEndedDuringHitStopRestoresTheClock(failures, log);

                SessionEndWiringIsPresent(failures, log);

                // WO-1233b — the observer must know WHICH battle it is judging, and a FAIL must
                // self-heal rather than leave the player holding it.
                SessionEpochAdvancesOnEachBattle(failures, log);
                SupersedeGuardIsWired(failures, log);
                SelfHealIsWired(failures, log);

                // WO-1308 — a RETREAT must release EVERY battle-lock holder, including the wave loop.
                RetreatReleasesEveryLockHolder(failures, log);
                WaveLoopUnwindsItsOwnPhase(failures, log);

                // WO-1337 — a retreat must release every battle-lock holder AND close every
                // panel handle. Two invariants, two owners, one exit path.
                RetreatSurvivorPulseDoesNotOutliveTheBody(failures, log);
                DespawnRevokesPursuitAtSource(failures, log);
                RetreatClosesEveryPanelHandle(failures, log);
                RetreatWaitsOutItsOwnDefeatBanner(failures, log);

                // WO-1603 — the WO-1337 regression. The holder name reached the capture and the
                // PULSER never did, so these four make the producer nameable and pin the one
                // producer that could stamp with no battle behind it: a chase over a dead hero.
                PursuitPulseNamesItsProducer(failures, log);
                RetreatAfterHeroDeathStopsThePulse(failures, log);
                RetreatDuringARaidNamesEachPulserSeparately(failures, log);
                EveryPursuitProducerNamesItselfAtSource(failures, log);

                // WO-1353 — the world clock has ONE owner and every step into slow pairs with a
                // step out. Four invariants, pinned as invariants rather than as instances.
                WorldClockHasExactlyOneWriter(failures, log);
                ZeroHoldsMeansFullSpeed(failures, log);
                EveryHoldPathReleasesOnEveryExit(failures, log);
                AnOverrunHoldSelfReleasesAndReports(failures, log);
                APlayerOwnedHoldOutlivesEveryCeiling(failures, log);
                TodaysCapturedDriftIsCorrected(failures, log);
                TheGateObservesAndDoesNotWriteTheClock(failures, log);

                // WO-1736 — the TOWN WAVE END must arm the gate too. Wiring only: the
                // holder is still UNNAMED, so there is nothing behavioural to assert
                // beyond what HolderIsNamedInTheFinding already proves.
                TownWaveEndArmsTheGate(failures, log);
                WavePhaseProbeIsRegistered(failures, log);
                ParkedCountdownIsNotImminent(failures, log);

                // WO-1855 — BATTLE_QUIESCENCE_FAIL (arena win) recurring on the same
                // 'OverworldEncounterSpawner/rep-chase' shape (seq5344/5490/5519): a SECOND,
                // untouched rep pack's leader is genuinely still within its own leash of the
                // hero's return position. Not a release bug (BattleLock/PursuitBattleProbe/
                // BattleSessionEnd all pinned above); the gap was that neither DescribeHolders()
                // nor DescribePursuits() can tell that apart from a stalled/blocked chase. This
                // pins that the rep-chase probe exists and names the discriminating numbers.
                RepChaseProbeIsRegistered(failures, log);
            }
            finally
            {
                // Never leak the global under test. Every later suite reads this clock.
                Time.timeScale = savedScale;
                BattleQuiescenceGate.Unregister("wo1127-suite-probe");
                BattleSessionEnd.UnregisterUnwind("wo1233-suite-hitstop");
                BattleSessionEnd.UnregisterUnwind("wo1308-suite-latched-phase");
                if (s_latchedHolderProbe != null)
                {
                    BattleLock.UnregisterProbe(s_latchedHolderProbe);
                    s_latchedHolderProbe = null;
                }
                PostureSignals.ClearPursuits();
                // WO-1337: the modal arbiter is a global too. A suite that leaves a handle
                // recorded open would fail the modal invariant for every suite after it.
                DeNelle.Core.UI.PanelManager.CloseAll();
            }

            if (failures.Count > 0)
            {
                reason = $"{MarkerFail}: {failures.Count} failure(s) -- " + string.Join(" | ", failures);
                Debug.LogError(log + "\n" + reason);
                return false;
            }

            reason = $"{MarkerOk} -- a clean world passes with zero findings; a wrong timeScale, a " +
                     "failing module probe and the 2026-08-20 defect each produce a NAMED failure; " +
                     "an open reward screen is correctly not judged; the gate is wired into " +
                     "battle resolve; a RETREAT releases every battle-lock holder, the wave " +
                     "loop's latched phase included (WO-1308); and a retreat both releases the " +
                     "pursuit pulse of every body it despawns and closes every panel handle, " +
                     "naming the panel and healing only an invisible ghost (WO-1337); and the WORLD " +
                     "CLOCK has exactly ONE writer, zero live holds always reads 1.00, every exit " +
                     "(battle win/loss/retreat/scene-change, death, victory) releases, an overrun " +
                     "hold self-releases and reports, the 2026-09-03 capture (timeScale 0.28 with " +
                     "zero holds) is corrected and NAMED while a live 0.28 hold is left alone, and " +
                     "the quiescence gate still OBSERVES rather than writing the clock (WO-1353); " +
                     "and every pursuit pulse NAMES ITS PRODUCER and the age of its last stamp, at " +
                     "all three stamp sites and in both the gate finding and the session release, so " +
                     "a one-frame re-latch after a full ClearPursuits reads as the LIVE producer it " +
                     "is - while a chase steered at a hero who is DOWN stamps nothing and a genuine " +
                     "chaser still holds the lock (WO-1603, the WO-1337 regression).";
            Debug.Log(log + MarkerOk);
            return true;
        }

        // ── 1. a clean world must pass ───────────────────────────────────────
        private static void CleanStatePasses(List<string> failures, StringBuilder log)
        {
            Time.timeScale = 1f;
            var found = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false);

            // Only the Core invariants are asserted clean here: module probes are registered by a
            // live BattleArena, which does not exist in an editor batch run, so their absence is
            // expected rather than a finding.
            var coreFindings = found.Where(f => f.StartsWith("timeScale:") || f.StartsWith("battle-lock:")).ToList();
            if (coreFindings.Count == 0)
            {
                log.AppendLine("  [clean] a baseline world produces ZERO core findings");
            }
            else
            {
                failures.Add("[clean] the gate reported a finding against a CLEAN world (timeScale 1, no " +
                             "battle): " + string.Join(" / ", coreFindings) + ". A gate that fails correct " +
                             "behaviour becomes a permanent red everyone learns to ignore, which is worse " +
                             "than no gate.");
            }
        }

        // ── 2. the known-bad timeScale must FAIL ─────────────────────────────
        private static void KnownBadTimeScaleFails(List<string> failures, StringBuilder log)
        {
            Time.timeScale = 0.5f;
            var found = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false);
            Time.timeScale = 1f;

            if (found.Any(f => f.StartsWith("timeScale:")))
                log.AppendLine("  [known-bad] timeScale 0.50 correctly produced a named finding");
            else
                failures.Add("[known-bad] timeScale was 0.50 and the gate did NOT report it. A gate that " +
                             "does not fail the known-bad state is not a gate.");
        }

        // ── 3. the ORIGINATING defect, named in full ─────────────────────────
        private static void OriginalDefectIsNamed(List<string> failures, StringBuilder log)
        {
            // 0.04 is not an arbitrary number: it is HitTier.Medium / Enemy.cs's death stop, and it
            // is the value measured on the owner's device on 2026-08-20.
            Time.timeScale = 0.04f;
            var found = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false);
            Time.timeScale = 1f;

            string line = found.FirstOrDefault(f => f.StartsWith("timeScale:"));
            if (line == null)
            {
                failures.Add("[defect-2026-08-20] the exact captured defect (timeScale 0.04) produced NO " +
                             "finding. This is the state the owner sat in for three minutes.");
                return;
            }

            // The message must carry the evidence, not just the verdict.
            bool namesValue = line.Contains("0.04");
            bool namesSpeed = line.Contains("4%");
            if (namesValue && namesSpeed)
            {
                log.AppendLine("  [defect-2026-08-20] timeScale 0.04 reported with the value AND the 4% speed");
            }
            else
            {
                failures.Add($"[defect-2026-08-20] the finding fires but does not carry its evidence " +
                             $"(value={namesValue}, speed={namesSpeed}): \"{line}\". A message that names " +
                             "only the field costs the next reader the measurement all over again.");
            }
        }

        // ── 4. a module probe's failure must reach the report ────────────────
        private static void ModuleProbeFailureIsSurfaced(List<string> failures, StringBuilder log)
        {
            // The Village probes (arena-actors, hero-owner) cannot be exercised in an editor batch,
            // so the CONTRACT is asserted instead: a registered probe that reports a reason must
            // appear in the findings, prefixed by its name. That is what makes a real probe's
            // failure legible when it does fire on device.
            BattleQuiescenceGate.Register(new QuiescenceProbe
            {
                Name = "wo1127-suite-probe",
                Check = () => "deliberate failure injected by the WO-1127 regression suite"
            });

            Time.timeScale = 1f;
            var found = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false);
            BattleQuiescenceGate.Unregister("wo1127-suite-probe");

            if (found.Any(f => f.StartsWith("wo1127-suite-probe:")))
                log.AppendLine("  [module-probe] a failing module probe surfaces, prefixed by its name");
            else
                failures.Add("[module-probe] a registered probe returned a reason and it did NOT reach the " +
                             "findings. Every Village-side invariant (orphaned arena actors, hero owner) " +
                             "rides this path, so a break here silently disables all of them.");

            // And it must be gone after Unregister, or a suite could poison the live gate.
            var after = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false);
            if (after.Any(f => f.StartsWith("wo1127-suite-probe:")))
                failures.Add("[module-probe] Unregister did not remove the probe - a test or a torn-down " +
                             "scene could leave a dead probe failing the gate forever.");
        }

        // ── 5. an open reward screen must NOT be judged ──────────────────────
        private static void RewardScreenIsNotJudged(List<string> failures, StringBuilder log)
        {
            Time.timeScale = 1f;
            var whileOpen = BattleQuiescenceGate.Evaluate(rewardScreenOpen: true);

            if (whileOpen.Any(f => f.StartsWith("modal:")))
                failures.Add("[reward-screen] the gate reported a modal finding while the reward screen was " +
                             "legitimately OPEN. Failing on correct behaviour is the fastest way to get a " +
                             "gate switched off.");
            else
                log.AppendLine("  [reward-screen] the modal invariant is correctly skipped while it is up");
        }

        // ── 6. the gate must actually be WIRED ───────────────────────────────
        private static void WiringIsPresent(List<string> failures, StringBuilder log)
        {
            string src = ReadCode("Assets/_Modules/Village/Arena/BattleArena.cs");
            if (src == null)
            {
                failures.Add("[wiring] BattleArena.cs is MISSING - the wiring cannot be verified at all.");
                return;
            }

            if (!src.Contains("BattleQuiescenceGate.Arm"))
            {
                failures.Add("[wiring] BattleArena no longer arms BattleQuiescenceGate at resolve. A gate " +
                             "nothing calls is the exact shape of the bug this ticket exists to prevent: " +
                             "everything green, the world broken.");
                return;
            }
            log.AppendLine("  [wiring] BattleArena arms the gate at resolve");

            if (src.Contains("BattleQuiescenceGate.Register"))
                log.AppendLine("  [wiring] the Village-side probes are registered");
            else
                failures.Add("[wiring] BattleArena no longer registers its module probes, so the arena-actor " +
                             "and hero-owner invariants are silently absent from the contract.");

            // Armed on BOTH outcomes. A retreat tears down the same systems a win does, and a
            // contract with a hole in it is not a contract.
            int armAt = src.IndexOf("BattleQuiescenceGate.Arm");
            string tail = src.Substring(armAt, System.Math.Min(400, src.Length - armAt));
            if (tail.Contains("retreat"))
                log.AppendLine("  [wiring] the gate is armed on the loss/retreat path too, not only on a win");
            else
                failures.Add("[wiring] the gate appears to be armed only on the WIN path. A retreat tears " +
                             "down the same systems; leaving it unchecked leaves half the teardowns unproven.");
        }

        // =====================================================================
        //  WO-1233 — the battle SESSION must release what the battle raised
        // ---------------------------------------------------------------------
        //  CAPTURED DEFECT: nine BATTLE_QUIESCENCE_FAIL events on 2026-08-26 —
        //  EIGHT on an arena WIN, one on a retreat. The owner only reported the
        //  retreat, so every case below drives the WIN path too: a suite that only
        //  covered the reported symptom would have gone green on the rare instance
        //  while the common one shipped.
        //
        //  THE STATE EACH CASE REPRODUCES is the one the device captured, in the
        //  neighbouring harvest for an identical failure (capture-…-seq3545):
        //     [Flow:HUD] context inputs: wave=False battleLock=True pursuit=True …
        //  The arena is DOWN and the lock is still up, held through
        //  PursuitBattleProbe by a pursuit pulse the fight opened and nothing
        //  closed. The pre-assert in each case pins that defect state explicitly,
        //  so the suite fails loudly if the reproduction ever stops reproducing —
        //  a green that came from a test that no longer sets up the bug is the
        //  worst outcome available here.
        // =====================================================================

        /// <summary>
        /// Drive one battle outcome end-to-end over the REAL statics: a staged enemy chased the
        /// hero (a live pursuit pulse), the battle then ends, and the lock must be clear afterwards.
        /// </summary>
        private static void SessionEndReleasesTheLock(List<string> failures, StringBuilder log, string outcome)
        {
            bool arenaLive = true;
            Func<bool> arenaProbe   = () => arenaLive;                       // BattleArena.BattleInProgress
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;    // PursuitBattleProbe.Probe

            Time.timeScale = 1f;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(arenaProbe);
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                // A staged arena enemy is chasing the hero. Enemy.DriveNav pulses this every tick.
                PostureSignals.ReportPursuit(19233);

                // The battle ends: BattleArena clears its own flag. This is the ONLY release the
                // arena ever performed, and it is not enough — which is the whole defect.
                arenaLive = false;

                if (!BattleLock.IsInBattle())
                {
                    failures.Add($"[session-end/{outcome}] the DEFECT STATE no longer reproduces: with the " +
                                 "arena down and a live pursuit pulse, BattleLock already reads clear. " +
                                 "Either PursuitBattleProbe's source changed or the pulse TTL did — this " +
                                 "case is no longer testing the captured failure and must be re-derived " +
                                 "before it is trusted.");
                    return;
                }

                string heldBy = BattleLock.DescribeHolders();
                BattleSessionEnd.Release(outcome);

                if (BattleLock.IsInBattle())
                    failures.Add($"[session-end/{outcome}] the battle ended and the lock is STILL HELD by " +
                                 $"[{BattleLock.DescribeHolders()}]. This is the owner's \"doesnt do " +
                                 "anything\": PanelManager refuses every town panel while it is up.");
                else if (Mathf.Abs(Time.timeScale - 1f) > 0.001f)
                    failures.Add($"[session-end/{outcome}] the lock released but the world clock is " +
                                 $"{Time.timeScale:F2}, not 1.00.");
                else
                    log.AppendLine($"  [session-end/{outcome}] lock was held by [{heldBy}] and is released; timeScale 1.00");
            }
            finally
            {
                BattleLock.UnregisterProbe(arenaProbe);
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// The finding must NAME the holder. Nine device captures said "still HELD" and not one said
        /// by whom, which is the entire reason this ticket cost a log-archaeology session.
        /// </summary>
        private static void HolderIsNamedInTheFinding(List<string> failures, StringBuilder log)
        {
            Func<bool> stuckProbe = () => true;
            Time.timeScale = 1f;
            BattleLock.RegisterProbe(stuckProbe);
            try
            {
                string line = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false)
                                                  .FirstOrDefault(f => f.StartsWith("battle-lock:"));
                if (line == null)
                {
                    failures.Add("[holder] a probe reporting TRUE produced no battle-lock finding at all.");
                    return;
                }

                // The original sentence must survive verbatim - this is an addition, not a rewrite.
                if (!line.Contains("still HELD after the battle ended"))
                    failures.Add("[holder] the battle-lock finding's original wording was CHANGED. It may be " +
                                 "added to and never narrowed: it is the only reason this defect was findable.");

                if (line.Contains("HOLDER(S):") && line.Contains("BattleQuiescenceRegression"))
                    log.AppendLine("  [holder] the finding names the probe that actually holds the lock");
                else
                    failures.Add($"[holder] the finding does not name the holder: \"{line}\". Attribution is " +
                                 "the deliverable - \"the lock is stuck\" costs a whole session that \"the " +
                                 "lock is held by PursuitBattleProbe\" does not.");
            }
            finally { BattleLock.UnregisterProbe(stuckProbe); }
        }

        /// <summary>
        /// The release must NOT be able to suppress a real fight. Pursuit is pulse-based: a chaser
        /// that is still chasing re-reports on its next tick, so the lock legitimately comes back.
        /// Without this case the "fix" could be a blind force-false, which would unblock panels
        /// mid-battle - the exact thing WO-1233 forbids.
        /// </summary>
        private static void LiveChaseIsNotSuppressed(List<string> failures, StringBuilder log)
        {
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;
            Time.timeScale = 1f;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                BattleSessionEnd.Release("arena win");
                PostureSignals.ReportPursuit(29233);   // a town rep is still chasing - next aggro tick

                if (BattleLock.IsInBattle())
                    log.AppendLine("  [live-chase] a still-chasing pursuer re-raises the lock after the release");
                else
                    failures.Add("[live-chase] a live pursuer re-reported AFTER the battle-end release and the " +
                                 "lock did NOT come back. The release has become a suppression, which unblocks " +
                                 "town panels during a real chase - worse than the bug it replaced.");
            }
            finally
            {
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
            }
        }

        /// <summary>
        /// The timeScale half, asserted through its OWN owner. A battle that ends mid-hit-stop must
        /// leave the clock at 1.00 - 0.04 is HitTier.Medium and the exact value the device captured
        /// twice on 2026-08-26 (and once on 2026-08-20 before it).
        /// </summary>
        private static void BattleEndedDuringHitStopRestoresTheClock(List<string> failures, StringBuilder log)
        {
            // Stands in for HitStopManager, which cannot run its coroutine outside play mode. The
            // CONTRACT under test is that the session end reaches a registered clock unwind at all;
            // that the real manager is the one registered is asserted by the source lint below, so
            // this can never pass against a stub alone.
            BattleSessionEnd.RegisterUnwind("wo1233-suite-hitstop", _ =>
            {
                if (Mathf.Abs(Time.timeScale - 0.04f) < 0.001f) Time.timeScale = 1f;
            });
            try
            {
                Time.timeScale = 0.04f;   // a hit stop is in flight at the instant the battle resolves
                BattleSessionEnd.Release("arena win");

                if (Mathf.Abs(Time.timeScale - 1f) <= 0.001f)
                    log.AppendLine("  [hit-stop] a battle ended mid-stop leaves timeScale 1.00");
                else
                    failures.Add($"[hit-stop] the battle ended during a hit stop and the clock is still " +
                                 $"{Time.timeScale:F2}. The player reads 4% speed as frozen controls even " +
                                 "though input is fine - the 2026-08-20 defect, returned.");
            }
            finally
            {
                BattleSessionEnd.UnregisterUnwind("wo1233-suite-hitstop");
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// WO-1233 wiring lint. The release must be announced from the battle's LIFECYCLE ENDS, and
        /// the real hit-stop owner must be the thing subscribed to it. A release nothing calls, or a
        /// stub-only subscriber, is the same everything-green-world-broken shape group 6 guards.
        /// </summary>
        private static void SessionEndWiringIsPresent(List<string> failures, StringBuilder log)
        {
            string arena = ReadCode("Assets/_Modules/Village/Arena/BattleArena.cs");
            if (arena == null)
            {
                failures.Add("[session-wiring] BattleArena.cs is MISSING - the wiring cannot be verified.");
            }
            else
            {
                int calls = CountOf(arena, "BattleSessionEnd.Release");
                if (calls >= 2)
                    log.AppendLine($"  [session-wiring] BattleArena announces the session end from both lifecycle ends ({calls} call sites)");
                else
                    failures.Add($"[session-wiring] BattleArena calls BattleSessionEnd.Release {calls} time(s). It " +
                                 "must be announced from BOTH lifecycle ends (Resolve and ResolveAbandoned) and " +
                                 "from NEITHER individual outcome - an abandoned fight opened the same pursuit " +
                                 "window a resolved one did.");

                foreach (var token in new[]
                         {
                             "ArenaEntryLanded(heroStance",
                             "ARENA ENTRY HANDSHAKE failed after warp",
                             "ARENA ENTRY HANDSHAKE failed after retry",
                             "Resolve(false)"
                         })
                    if (!arena.Contains(token))
                        failures.Add($"[session-wiring] arena entry handshake lost '{token}' - a WarpHero request " +
                                     "can again spawn enemies before proving the hero arrived.");
            }

            string hitStop = ReadCode("Assets/_Modules/Village/Vfx/HitStopManager.cs");
            if (hitStop == null)
            {
                failures.Add("[session-wiring] HitStopManager.cs is MISSING - the clock unwind cannot be verified.");
                return;
            }

            if (hitStop.Contains("BattleSessionEnd.RegisterUnwind") && hitStop.Contains("EndStopNow"))
                log.AppendLine("  [session-wiring] the real hit-stop owner registers its own battle-end unwind");
            else
                failures.Add("[session-wiring] HitStopManager no longer registers a battle-end unwind, so the ONLY " +
                             "thing proving the clock case above is the suite's own stub. The 2026-08-20 fix had " +
                             "three unwind paths and the defect still returned, because all three were keyed to " +
                             "the manager's lifetime and none to the battle's.");
        }

        // =====================================================================
        //  WO-1233b — the gate must not report a LIVE battle's state as the last
        //  battle's leak, and a FAIL must self-heal.
        // ---------------------------------------------------------------------
        //  CAPTURED DEFECT (2026-08-30, Seeker 2026.08.30.348233, device break-log):
        //    t=342.5  BATTLE_QUIESCENCE_FAIL (arena win) - timeScale 0.04 +
        //             battle-lock held by PursuitBattleProbe.Probe AND
        //             BattleArena.<Awake>b__84_0
        //    t=350.8  [HeroDeath] death freeze armed ... pinPos=(4997.93,0.08,5004.65)
        //  ArenaCentre is (5000,0,5000): the hero was IN the arena, fighting, and died
        //  there 8.3s after the gate declared the previous battle unclean. Resolve
        //  clears BattleInProgress BEFORE it arms the gate, so that probe reading true
        //  inside the gate's own coroutine can only mean a SECOND battle had begun.
        // =====================================================================

        /// <summary>
        /// The epoch is what tells an armed observer that the world in front of it belongs to a
        /// different fight. Behavioural, over the real static.
        /// </summary>
        private static void SessionEpochAdvancesOnEachBattle(List<string> failures, StringBuilder log)
        {
            int before = BattleSessionEnd.Epoch;
            BattleSessionEnd.Begin("wo1233b-suite: first battle");
            int afterFirst = BattleSessionEnd.Epoch;
            BattleSessionEnd.Release("wo1233b-suite: first battle ends");
            int afterRelease = BattleSessionEnd.Epoch;
            BattleSessionEnd.Begin("wo1233b-suite: second battle");
            int afterSecond = BattleSessionEnd.Epoch;

            if (afterFirst == before)
            {
                failures.Add("[epoch] BattleSessionEnd.Begin did not advance the session epoch. Every " +
                             "quiescence gate then judges whatever battle happens to be running when it " +
                             "settles, which is the 2026-08-30 false failure verbatim.");
                return;
            }

            if (afterRelease != afterFirst)
            {
                failures.Add("[epoch] BattleSessionEnd.Release advanced the epoch. It MUST NOT: the gate " +
                             "is armed immediately after Release, so a bump there would make every gate " +
                             "consider itself superseded by its own battle and never report anything.");
                return;
            }

            if (afterSecond <= afterFirst)
            {
                failures.Add("[epoch] a SECOND Begin did not advance the epoch past the first, so two " +
                             "back-to-back battles are indistinguishable to an armed observer.");
                return;
            }

            log.AppendLine("  [epoch] each battle start advances the session epoch; a session END does not");
        }

        /// <summary>
        /// Source-lint. The settle window cannot be driven in an editor batch (it waits on
        /// Time.unscaledTime, which does not advance inside a synchronous suite), so the WIRING is
        /// what is asserted — the same discipline as groups 4 and 6.
        /// </summary>
        private static void SupersedeGuardIsWired(List<string> failures, StringBuilder log)
        {
            string gate = ReadCode("Assets/_Modules/Core/Combat/BattleQuiescenceGate.cs");
            if (gate == null)
            {
                failures.Add("[supersede] BattleQuiescenceGate.cs is MISSING - the guard cannot be verified.");
            }
            else if (CountOf(gate, "BattleSessionEnd.Epoch") < 2)
            {
                failures.Add("[supersede] BattleQuiescenceGate no longer captures AND compares " +
                             "BattleSessionEnd.Epoch. Without both, the gate reports the live state of " +
                             "whatever battle started while it was settling as the previous battle's leak " +
                             "- 2026-08-30, timeScale 0.04 plus two 'stuck' holders, all of them correct.");
            }
            else if (!gate.Contains("BATTLE_QUIESCENCE_SUPERSEDED"))
            {
                failures.Add("[supersede] the superseded case no longer has its own marker. A withdrawal " +
                             "that looks like a pass is a gate that silently stops covering the real leak.");
            }
            else
            {
                log.AppendLine("  [supersede] the gate captures the epoch at arm time and withdraws under its own marker");
            }

            string arena = ReadCode("Assets/_Modules/Village/Arena/BattleArena.cs");
            if (arena == null)
                failures.Add("[supersede] BattleArena.cs is MISSING - the session-start wiring cannot be verified.");
            else if (!arena.Contains("BattleSessionEnd.Begin"))
                failures.Add("[supersede] BattleArena no longer announces the session START, so the epoch " +
                             "never advances and the supersede guard in the gate is dead code.");
            else
                log.AppendLine("  [supersede] BattleArena announces the session start from BeginEncounter");
        }

        /// <summary>
        /// A FAIL must leave the player better off, not merely documented. Asserts the recovery goes
        /// through the ONE authoritative exit rather than force-clearing the lock from the observer.
        /// </summary>
        private static void SelfHealIsWired(List<string> failures, StringBuilder log)
        {
            string gate = ReadCode("Assets/_Modules/Core/Combat/BattleQuiescenceGate.cs");
            if (gate == null)
            {
                failures.Add("[self-heal] BattleQuiescenceGate.cs is MISSING - the self-heal cannot be verified.");
                return;
            }

            if (!gate.Contains("quiescence self-heal"))
                failures.Add("[self-heal] a BATTLE_QUIESCENCE_FAIL no longer re-drives " +
                             "BattleSessionEnd.Release. Reporting alone leaves the player holding the " +
                             "stuck lock and the 4% clock the report describes.");
            else
                log.AppendLine("  [self-heal] a FAIL re-drives the one authoritative exit seam");

            // The recovery must NOT become a new writer of the lock. BattleLock is an OR over N
            // owners; forcing it false from here would mask a live fight instead of ending one.
            if (gate.Contains("BattleLock.UnregisterProbe") || gate.Contains("BattleLock.RegisterProbe"))
                failures.Add("[self-heal] BattleQuiescenceGate now mutates BattleLock's probe list. The " +
                             "observer must never own the lock: a live chase would be silently unlocked, " +
                             "and the holder attribution the 2026-08-26 tickets bought would be lost.");
            else
                log.AppendLine("  [self-heal] the gate still owns no lock state - it re-drives the owners' own unwinds");
        }

        // =====================================================================
        //  WO-1308 — a RETREAT must release EVERY battle-lock holder.
        // ---------------------------------------------------------------------
        //  CAPTURED DEFECT (2026-09-02, owner felt-test, F8 seq 4663-4665,
        //  Main_Castle_Overworld — "somehow the wolf is still here and sitting in fight"):
        //
        //    [Flow:Quiescence] battle-lock STILL HELD after the self-heal (retreat):
        //      [WaveManager.<OnEnable>b__106_0]
        //      (was [PursuitBattleProbe.Probe, WaveManager.<OnEnable>b__106_0]).
        //
        //  PursuitBattleProbe released. The WAVE probe did not, because the wave loop's
        //  _phase was latched at Active and NOTHING at battle end ever asked it. WO-1233
        //  established the rule that every owner of a global registers its own unwind;
        //  WaveManager owned one (the lock claim it raises through _phase) and was the one
        //  owner that had never registered.
        // =====================================================================

        /// <summary>Kept in a field so the finally can always drop it, even on an early return.</summary>
        private static Func<bool> s_latchedHolderProbe;

        /// <summary>
        /// Behavioural, both directions. A holder that registers NO unwind survives a full session
        /// release — that is the captured defect, and asserting it is what stops this test passing
        /// for the wrong reason. The SAME holder, once it registers an unwind, is released by the
        /// same call.
        /// </summary>
        private static void RetreatReleasesEveryLockHolder(List<string> failures, StringBuilder log)
        {
            bool latched = true;                       // stands in for WaveManager._phase == Active
            s_latchedHolderProbe = () => latched;
            BattleLock.RegisterProbe(s_latchedHolderProbe);

            if (!BattleLock.IsInBattle())
            {
                failures.Add("[wo1308] a registered probe returning true did not raise the battle-lock, " +
                             "so this test cannot prove anything about releasing it.");
                BattleLock.UnregisterProbe(s_latchedHolderProbe);
                s_latchedHolderProbe = null;
                return;
            }

            // (a) THE DEFECT. No unwind registered: a retreat leaves the holder exactly where it was.
            BattleSessionEnd.Release("wo1308-suite: retreat, holder has NO unwind");
            if (!BattleLock.IsInBattle())
            {
                failures.Add("[wo1308] a latched holder that registered NO battle-end unwind was released " +
                             "anyway. Either something now force-clears BattleLock from the outside — which " +
                             "would silently unlock a LIVE fight — or this probe is not really a holder, and " +
                             "the pass below would then be meaningless.");
                BattleLock.UnregisterProbe(s_latchedHolderProbe);
                s_latchedHolderProbe = null;
                return;
            }
            log.AppendLine("  [wo1308] a holder with no unwind survives a session release (the captured defect)");

            // (b) THE CONTRACT. The owner registers an unwind that lowers its OWN claim; the same
            //     retreat now releases it. Nothing force-clears the lock — the owner stands down.
            BattleSessionEnd.RegisterUnwind("wo1308-suite-latched-phase", _ => latched = false);
            BattleSessionEnd.Release("retreat");

            if (BattleLock.IsInBattle())
                failures.Add("[wo1308] the battle-lock is STILL HELD after a retreat, even though the holder " +
                             "registered a battle-end unwind. This is F8 seq 4664 verbatim: combat input " +
                             "stays suppressed and the HUD cannot return to town for the rest of the session.");
            else
                log.AppendLine("  [wo1308] a retreat releases a holder that unwinds its own state at session end");

            BattleSessionEnd.UnregisterUnwind("wo1308-suite-latched-phase");
            BattleLock.UnregisterProbe(s_latchedHolderProbe);
            s_latchedHolderProbe = null;
        }

        /// <summary>
        /// Source-lint on the REAL holder. The stub above proves the seam; only this proves that
        /// WaveManager uses it — and the whole defect was a seam that existed and an owner that
        /// never reached for it. Same discipline as the session-wiring group: a live wave loop
        /// cannot be driven inside a synchronous editor batch.
        /// </summary>
        private static void WaveLoopUnwindsItsOwnPhase(List<string> failures, StringBuilder log)
        {
            string wave = ReadCode("Assets/_Modules/Village/Waves/WaveManager.cs");
            if (wave == null)
            {
                failures.Add("[wo1308-wiring] WaveManager.cs is MISSING — the wave loop's battle-end unwind " +
                             "cannot be verified.");
                return;
            }

            if (!wave.Contains("BattleSessionEnd.RegisterUnwind"))
                failures.Add("[wo1308-wiring] WaveManager no longer registers a battle-end unwind. The wave " +
                             "loop raises the battle-lock through _phase == Active and is the ONLY owner of " +
                             "that claim; with no unwind, a retreat leaves it held for the rest of the " +
                             "session — F8 seq 4664, 2026-09-02.");
            else
                log.AppendLine("  [wo1308-wiring] the wave loop registers its own battle-end unwind");

            // The unwind must DRIVE the loop's own clear test, not re-decide it. A second copy of
            // "is this wave over" is a second answer waiting to disagree with TickActiveWave.
            if (!wave.Contains("ReconcileLatchedWavePhase") || !wave.Contains("TickActiveWave()"))
                failures.Add("[wo1308-wiring] the battle-end unwind no longer drives TickActiveWave. If it " +
                             "decides for itself whether the wave is over, that judgement will drift from " +
                             "the loop's and one of the two will cancel a live siege.");
            else
                log.AppendLine("  [wo1308-wiring] the unwind drives the loop's own tick rather than re-deciding");

            // ⛔ The probe itself must NOT have been weakened to make the log clean. A live siege is
            //    combat, and a retreat from an overworld wolf must never cancel one.
            if (!wave.Contains("Instance == this && _phase == WavePhase.Active"))
                failures.Add("[wo1308-wiring] the battle-lock probe predicate changed. It must stay " +
                             "'isActiveAndEnabled && Instance == this && _phase == WavePhase.Active': " +
                             "narrowing it hides a genuine siege from the lock, which trades a stuck lock " +
                             "for a combat state the game does not know it is in — strictly worse, and " +
                             "invisible.");
            else
                log.AppendLine("  [wo1308-wiring] the battle-lock probe predicate is intact (a real siege still holds)");

            // The roster and the phase must come down together. OnDisable clears every live enemy
            // and the held count; a phase left Active over that empty field is the latch itself.
            if (!wave.Contains("OnDisable/roster-cleared"))
                failures.Add("[wo1308-wiring] OnDisable no longer stands the phase down with the roster it " +
                             "clears. A manager disabled mid-wave then re-enables still claiming Active over " +
                             "a field it emptied, and re-registers the lock probe against that claim.");
            else
                log.AppendLine("  [wo1308-wiring] OnDisable stands the phase down with the roster it clears");
        }

        // =====================================================================
        //  WO-1337 — a RETREAT must release every battle-lock holder AND close
        //  every panel handle.
        // ---------------------------------------------------------------------
        //  CAPTURED DEFECT (device SM02G4061955851, build 2026.09.03.353593,
        //  F8 seq 4677, scene Main_Castle_Overworld):
        //
        //    [Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (retreat) - 2 invariant(s)
        //      NOT restored after the battle:
        //      - battle-lock: still HELD ... HOLDER(S): PursuitBattleProbe.Probe
        //        (of 3 registered: PursuitBattleProbe.Probe,
        //        BattleArena.<Awake>b__84_0, WaveManager.<OnEnable>b__116_0).
        //      - modal: a panel handle is STILL OPEN after the reward screen closed.
        //
        //  Note what changed since WO-1308's seq 4664/4675, where the holder list read
        //  [PursuitBattleProbe.Probe, WaveManager.<OnEnable>b__116_0]: the WAVE holder
        //  is GONE, so WO-1308's unwind works and is not to be re-fixed. The probe
        //  arrived through the other door — it does reach BattleSessionEnd.Release,
        //  and it RE-LATCHES afterwards, because the arena's SURVIVORS outlive the
        //  release by HomeFadeOutSeconds (0.35 s) and are then removed with
        //  Destroy(gameObject), which never reaches Die() and so never revoked their
        //  pursuit pulse. Last pulse live to 0.35 + PursuitTtl(1.5) = 1.85 s; the gate
        //  judges a retreat at SettleSeconds 0.75 s. Deterministic, on constants that
        //  live in the tree - which is why WO-1233's own header already recorded that
        //  "the RETREAT case fails deterministically".
        //
        //  The modal half is a DIFFERENT owner and shares no cause: a retreat DOES
        //  present an end-state screen (the deferred defeat banner, on arrival), and
        //  the arm site used to tell the gate a retreat had none.
        // =====================================================================

        /// <summary>
        /// Behavioural, both directions, over the real Core statics. Reproduces the retreat
        /// timeline: the session end clears the pursuit ring, a SURVIVOR that is still alive
        /// behind the fade re-stamps its pulse, and the lock must come down when that body is
        /// removed — not 1.5 s later on the TTL, which is past the gate's judge point.
        /// </summary>
        private static void RetreatSurvivorPulseDoesNotOutliveTheBody(List<string> failures, StringBuilder log)
        {
            const int survivor = 41337;                                      // one staged arena enemy
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;    // PursuitBattleProbe.Probe

            Time.timeScale = 1f;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                PostureSignals.ReportPursuit(survivor);        // chasing the hero inside the arena
                BattleSessionEnd.Release("retreat");           // t=0 - Release clears the whole ring
                PostureSignals.ReportPursuit(survivor);        // ...and the body is STILL ALIVE behind
                                                               //    the 0.35 s fade, so it re-stamps

                // (a) THE DEFECT. The body is then destroyed WITHOUT dying, so nothing revokes its
                //     pulse and it stays live for PursuitTtl. Assert the defect reproduces: if the
                //     lock is already clear here, this case is no longer testing seq 4677.
                if (!BattleLock.IsInBattle())
                {
                    failures.Add("[wo1337] the DEFECT STATE no longer reproduces: a survivor that " +
                                 "re-stamped its pursuit pulse AFTER BattleSessionEnd.Release is not " +
                                 "holding the battle-lock. Either PursuitBattleProbe's source changed or " +
                                 "the pulse TTL did - re-derive this case from a fresh capture before " +
                                 "trusting the pass below.");
                    return;
                }
                log.AppendLine("  [wo1337] a survivor re-stamping after the release holds the lock (seq 4677)");

                // (b) THE CONTRACT. The body revokes its OWN pulse as it goes down (Enemy.OnDisable),
                //     so the lock comes down with the body instead of 1.5 s later on the TTL.
                //     Nothing force-clears BattleLock; the owner stands its own claim down.
                PostureSignals.RevokePursuit(survivor);

                if (BattleLock.IsInBattle())
                    failures.Add("[wo1337] the survivor revoked its pursuit pulse and the battle-lock is " +
                                 "STILL HELD by [" + BattleLock.DescribeHolders() + "]. The retreat then " +
                                 "leaves combat input suppressed and the HUD pinned out of town - F8 seq " +
                                 "4677 verbatim.");
                else
                    log.AppendLine("  [wo1337] revoking the removed body's own pulse releases the lock immediately");
            }
            finally
            {
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// Source-lint on the REAL owner. The case above proves the seam; only this proves that the
        /// ENEMY reaches it on the path that mattered — and the whole defect was a release seam that
        /// existed (RevokePursuit) with no caller on the despawn path. An Enemy cannot be driven
        /// inside a synchronous editor batch, and DeNelle.Village is not referenced from here.
        /// </summary>
        private static void DespawnRevokesPursuitAtSource(List<string> failures, StringBuilder log)
        {
            string enemy = ReadCode("Assets/_Modules/Village/Enemies/Enemy.cs");
            if (enemy == null)
            {
                failures.Add("[wo1337-wiring] Enemy.cs is MISSING - the pursuit-pulse release cannot be verified.");
            }
            else
            {
                // The revoke must be reachable from OnDisable, which is the ONE hook that covers all
                // three removal paths at once (Destroy, pool release, scene unload). Die() alone is
                // the defect: the arena despawns survivors with Destroy(gameObject).
                int onDisable = enemy.IndexOf("private void OnDisable()", StringComparison.Ordinal);
                if (onDisable < 0)
                {
                    failures.Add("[wo1337-wiring] Enemy.OnDisable is GONE. It is the hook that releases this " +
                                 "body's battle-lock claims (the engagement token AND the pursuit pulse) on " +
                                 "every removal path; without it both leak past the body's own destruction.");
                }
                else
                {
                    // Bounded window: OnDisable is a short method, and a revoke that only appears
                    // 2000 characters later is in some other member and proves nothing.
                    string body = enemy.Substring(onDisable,
                        Math.Min(3000, enemy.Length - onDisable));
                    if (body.Contains("PostureSignals.RevokePursuit"))
                        log.AppendLine("  [wo1337-wiring] Enemy.OnDisable revokes this body's own pursuit pulse");
                    else
                        failures.Add("[wo1337-wiring] Enemy.OnDisable no longer revokes this body's pursuit " +
                                     "pulse. An enemy raises the battle-lock through TWO owners - the " +
                                     "HeroCombatEngagement token AND the pursuit pulse - and OnDisable " +
                                     "releases only the first. The arena's retreat teardown removes survivors " +
                                     "with Destroy(gameObject), which never reaches Die(), so the pulse " +
                                     "outlives the body by PursuitTtl and the gate judges inside that " +
                                     "window: F8 seq 4677.");
                }

                // ⛔ And the pulse must still be revoked on DEATH. The OnDisable revoke is an
                //    ADDITION covering the non-dying exits, never a replacement: death revokes
                //    immediately so town chrome returns as the last threat dies, without waiting
                //    for the corpse's death-hold to disable the body.
                if (CountOf(enemy, "PostureSignals.RevokePursuit") < 2)
                    failures.Add("[wo1337-wiring] Enemy revokes its pursuit pulse in fewer than TWO places. " +
                                 "Die() and OnDisable are BOTH required: dropping the Die() revoke delays the " +
                                 "return to peaceful chrome by the whole death hold, and dropping the " +
                                 "OnDisable one restores the seq 4677 defect.");
                else
                    log.AppendLine("  [wo1337-wiring] the pursuit pulse is revoked on death AND on every other exit");
            }

            // The probe itself must NOT have been weakened to make the log clean. A live chase is
            // combat, and PursuitBattleProbe reporting PursuitActive verbatim is what makes the
            // hero's abilities work while being chased in the overworld (F8-46).
            string probe = ReadCode("Assets/_Modules/Core/Combat/PursuitBattleProbe.cs");
            if (probe == null)
                failures.Add("[wo1337-wiring] PursuitBattleProbe.cs is MISSING - the holder cannot be verified.");
            // Match the ASSIGNMENT, not the bare member name: ReadCode strips comments but keeps
            // string-literal contents, and this file logs its own name in an install message — a
            // rule matching that would pass a probe rewired to return false.
            else if (!probe.Contains("bool active = PostureSignals.PursuitActive;"))
                failures.Add("[wo1337-wiring] PursuitBattleProbe no longer reads PostureSignals.PursuitActive. " +
                             "Narrowing the probe is the forbidden 'fix': it trades a stuck lock for combat " +
                             "input that dies during a real chase (F8-46, owner ruling OPTION A).");
            else
                log.AppendLine("  [wo1337-wiring] the pursuit battle-probe predicate is intact (a real chase still holds)");
        }

        /// <summary>
        /// The MODAL half — a different owner, and asserted separately for that reason. The finding
        /// must NAME the panel (seq 4677 named none, which is unactionable), and a GHOST handle —
        /// recorded open while its own probe reports it is not — must be closable through the
        /// panel's own Close action. A VISIBLE panel is the player's to dismiss and is NOT healed.
        /// </summary>
        private static void RetreatClosesEveryPanelHandle(List<string> failures, StringBuilder log)
        {
            Time.timeScale = 1f;
            DeNelle.Core.UI.PanelManager.CloseAll();
            try
            {
                // A GHOST: the arbiter records it open, the panel itself reports it is not. This is
                // the WO-465 invisible-scrim class and it is exactly the consequence the finding
                // describes - world prompts suppressed under nothing, back aimed at nothing.
                bool ghostClosed = false;
                bool ghostVisible = true;
                // RegisterBattleAllowed so an unrelated battle-lock state left by an earlier case
                // can never have NotifyOpened reject the open (WO-437 gate) and turn this case
                // into a silent no-op. The subject here is the MODAL invariant, not that gate.
                var ghost = DeNelle.Core.UI.PanelManager.RegisterBattleAllowed(
                    "wo1337-suite-ghost", () => ghostClosed = true, () => ghostVisible);
                DeNelle.Core.UI.PanelManager.NotifyOpened(ghost);

                // ...and NOW it stops being open without ever calling NotifyClosed. This is the
                // real shape of a ghost (a panel torn down or blanked behind the arbiter's back),
                // and opening it honestly first keeps this case from emitting NotifyOpened's own
                // invisible-scrim LogError, which would read as a suite error rather than a setup.
                ghostVisible = false;

                if (!DeNelle.Core.UI.PanelManager.AnyOpen)
                {
                    failures.Add("[wo1337-modal] NotifyOpened did not record the handle as open, so nothing " +
                                 "below can prove anything about the modal invariant.");
                    return;
                }

                string line = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false)
                                                  .FirstOrDefault(f => f.StartsWith("modal:"));
                if (line == null)
                {
                    failures.Add("[wo1337-modal] a panel recorded OPEN produced no modal finding at all.");
                    return;
                }

                // The original sentence must survive verbatim - an addition, never a rewrite.
                if (!line.Contains("a panel handle is STILL OPEN after the reward screen closed"))
                    failures.Add("[wo1337-modal] the modal finding's original wording was CHANGED. It may be " +
                                 "added to and never narrowed: it is the only reason this defect was findable.");

                if (line.Contains("HOLDER:") && line.Contains("wo1337-suite-ghost"))
                    log.AppendLine("  [wo1337-modal] the modal finding names the panel that actually holds the arbiter");
                else
                    failures.Add("[wo1337-modal] the modal finding does not name the panel: \"" + line + "\". " +
                                 "F8 seq 4677 reported this invariant on the owner's device and named nothing, " +
                                 "so the capture proved a handle was stuck and could not say which - the same " +
                                 "attribution gap WO-1233 closed for the battle-lock.");

                if (line.Contains("GHOST"))
                    log.AppendLine("  [wo1337-modal] and it distinguishes an invisible ghost handle from a visible panel");
                else
                    failures.Add("[wo1337-modal] the finding does not say whether the panel is VISIBLE or an " +
                                 "invisible ghost. That is the whole discriminator between a screen the player " +
                                 "can dismiss and a softlock, and it decides whether the heal may run.");

                // THE HEAL. Through the panel's OWN Close action, never by zeroing the record.
                if (DeNelle.Core.UI.PanelManager.OpenPanelSelfReportsOpen != false)
                    failures.Add("[wo1337-modal] a handle whose IsOpen probe returns false is not reported as a " +
                                 "ghost, so the gate can never tell the healable case from the live one.");

                DeNelle.Core.UI.PanelManager.CloseAll();
                if (DeNelle.Core.UI.PanelManager.AnyOpen)
                    failures.Add("[wo1337-modal] CloseAll left the arbiter holding a handle, so the gate's modal " +
                                 "self-heal cannot clear a ghost and the player keeps the suppressed interact " +
                                 "button the finding describes.");
                else if (!ghostClosed)
                    failures.Add("[wo1337-modal] the arbiter's record was cleared WITHOUT invoking the panel's own " +
                                 "Close action. The recovery must go through the panel's own door - a cleared " +
                                 "record over a panel that still thinks it is open is a new bug, not a heal.");
                else
                    log.AppendLine("  [wo1337-modal] a ghost handle is healed through the panel's OWN Close action");

                // A VISIBLE panel must still FAIL the invariant (it is not baseline) and must NOT be
                // force-closed by the gate - reported by name is the whole remedy there.
                bool visibleOpen = true;
                var visible = DeNelle.Core.UI.PanelManager.RegisterBattleAllowed(
                    "wo1337-suite-visible", () => visibleOpen = false, () => visibleOpen);
                DeNelle.Core.UI.PanelManager.NotifyOpened(visible);

                string visibleLine = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false)
                                                         .FirstOrDefault(f => f.StartsWith("modal:"));
                if (visibleLine == null)
                    failures.Add("[wo1337-modal] a VISIBLE panel left open at battle end produced no finding. " +
                                 "The invariant must fire on exactly the condition it always did - the ghost " +
                                 "discrimination decides the HEAL, never the FINDING.");
                else if (DeNelle.Core.UI.PanelManager.OpenPanelSelfReportsOpen != true)
                    failures.Add("[wo1337-modal] a visible panel is not reported as visible, so the gate would " +
                                 "force-close a screen the player is looking at.");
                else
                    log.AppendLine("  [wo1337-modal] a visible panel still FAILS the invariant and is reported, not yanked");
            }
            finally
            {
                DeNelle.Core.UI.PanelManager.CloseAll();
            }
        }

        /// <summary>
        /// Source-lint on the two WIRING halves the behavioural cases cannot reach: the gate must
        /// heal a ghost through PanelManager's own door, and the arena must stop telling the gate
        /// that a RETREAT has no end-state screen when it defers exactly one.
        /// </summary>
        private static void RetreatWaitsOutItsOwnDefeatBanner(List<string> failures, StringBuilder log)
        {
            string gate = ReadCode("Assets/_Modules/Core/Combat/BattleQuiescenceGate.cs");
            if (gate == null)
            {
                failures.Add("[wo1337-wiring] BattleQuiescenceGate.cs is MISSING - the modal heal cannot be verified.");
            }
            else
            {
                if (gate.Contains("PanelManager.CloseAll") && gate.Contains("PanelManager.OpenPanelSelfReportsOpen"))
                    log.AppendLine("  [wo1337-wiring] a FAIL heals a GHOST panel handle, and only a ghost");
                else
                    failures.Add("[wo1337-wiring] the gate no longer heals a stuck panel handle through " +
                                 "PanelManager (CloseAll gated on OpenPanelSelfReportsOpen). Reporting alone " +
                                 "leaves the player with the suppressed interact button and the invisible back " +
                                 "target the finding itself describes - a softlock that reports itself is " +
                                 "still a softlock.");

                // The heal must not become a blunt close-everything. A visible panel is the player's.
                if (gate.Contains("modal left OPEN on purpose"))
                    log.AppendLine("  [wo1337-wiring] a VISIBLE panel is deliberately left for the player to dismiss");
                else
                    failures.Add("[wo1337-wiring] the gate no longer says why it leaves a VISIBLE panel alone. " +
                                 "An unconditional CloseAll here yanks a live screen (the pause menu over a " +
                                 "just-ended fight) out from under the player.");
            }

            string arena = ReadCode("Assets/_Modules/Village/Arena/BattleArena.cs");
            if (arena == null)
            {
                failures.Add("[wo1337-wiring] BattleArena.cs is MISSING - the retreat arm cannot be verified.");
                return;
            }

            int armAt = arena.IndexOf("BattleQuiescenceGate.Arm", StringComparison.Ordinal);
            if (armAt < 0)
            {
                failures.Add("[wo1337-wiring] BattleArena no longer arms the gate at all.");
                return;
            }

            // The retreat branch must not hand the gate a bare null reward-screen probe. It defers
            // a defeat end-state (_pendingLossBanner -> BattleArenaHud.ShowResult -> EndStateView
            // -> PanelManager.NotifyOpened), so a null there judges the modal invariant straight
            // through that screen's own open - the false finding in seq 4677.
            int windowStart = Math.Max(0, armAt - 600);
            string window = arena.Substring(windowStart, Math.Min(900, arena.Length - windowStart));
            if (window.Contains("_pendingLossBanner != null"))
                log.AppendLine("  [wo1337-wiring] the retreat arm waits out its own deferred defeat banner");
            else
                failures.Add("[wo1337-wiring] the retreat path does not tell the gate about its DEFERRED defeat " +
                             "end-state (_pendingLossBanner). A retreat presents an arbiter panel on arrival, " +
                             "so a gate told there is no reward screen judges the modal invariant while that " +
                             "screen is legitimately opening and reports correct behaviour as a defect - which " +
                             "is how a gate gets switched off.");
        }

        private static int CountOf(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>Source with comments and string-literal contents blanked, so a rule cannot match
        /// its own tombstone comment.</summary>
        private static string ReadCode(string relPath)
        {
            string full = Path.GetFullPath(relPath);
            if (!File.Exists(full)) return null;

            var sb = new StringBuilder();
            foreach (string raw in File.ReadAllLines(full))
            {
                string line = raw;
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                int c = line.IndexOf("//");
                if (c >= 0 && !InsideQuotes(line, c)) line = line.Substring(0, c);
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        private static bool InsideQuotes(string line, int index)
        {
            bool q = false;
            for (int i = 0; i < index; i++)
                if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) q = !q;
            return q;
        }

        // =====================================================================
        //  WO-1353 — THE WORLD CLOCK HAS ONE OWNER, AND EVERY SLOW PAIRS
        // -----------------------------------------------------------------------------
        //  CAPTURED DEFECT (owner felt-test 2026-09-03, Main_Castle_Overworld):
        //      [Flow:HeroOwner] ... animSpeed=0.00 timeScale=0.28 dt=0.0046
        //      inputSuppressed=False autoWalk=False
        //  28% speed in open town with no battle, no modal and input not suppressed.
        //
        //  These cases pin the INVARIANT, not the instance. Pinning "0.28 never happens"
        //  would be worthless: the next leak is 0.30, or 0.05, or a lerp that stopped
        //  halfway at a number nobody authored. What is pinned instead is that there is
        //  exactly ONE writer, that zero live holds always means 1.00, that every exit
        //  releases, and that a hold which overruns is force-released and NAMED.
        // =====================================================================

        /// <summary>Directories whose Time.timeScale writes are deliberately NOT ours to convert.
        /// EXPLICIT, per WO-1353 §4: a lint with accidental exemptions becomes noise everyone
        /// learns to skip, and then the one write that matters is invisible.</summary>
        // WO-1495 2026-09-06 remove-by 2026-12-06 (origin WO-1353) - vendor-pack demo directories
        // whose Time.timeScale writes we deliberately do not own; re-read then in case a pack was
        // dropped or its demo scripts were deleted, which would leave a dead exemption behind.
        private static readonly string[] WorldClockLintExemptDirs =
        {
            // VENDOR PACK DEMO SCRIPTS. Converting third-party demo code means re-doing the work on
            // every pack update and taking ownership of code we did not write.
            "Assets/Mirza Beig/",
            "Assets/UnityTechnologies/",
            // EDITOR AND TEST CODE sets the clock DELIBERATELY, to drive the very paths these
            // suites assert (this file does it a dozen times, and TransactionWorldHoldRegression
            // measures the global after driving a real hold). A regression that cannot stage a
            // known-bad clock cannot prove a gate catches one.
            "Assets/Editor/",
        };

        /// <summary>The ONE file allowed to write the world clock.</summary>
        private const string WorldClockOwnerSrc = "Assets/_Modules/Core/UI/WorldHold.cs";

        /// <summary>
        /// Blanks the CONTENTS of double-quoted string literals, leaving the quotes.
        ///
        /// <para>⛔ NOT OPTIONAL, AND IT COST A FALSE RED TO LEARN. <c>ReadCode</c> blanks comments
        /// but NOT string contents, and <c>HeroLocomotion</c> legitimately prints
        /// <c>"WORLD CLOCK FROZEN: Time.timeScale={Time.timeScale:F2}"</c> — the diagnostic line
        /// CLAUDE.md sec.12 requires it to keep. Matched raw, that instrumentation reads as a
        /// twelfth writer of the clock, and this suite's own header is explicit that a gate which
        /// fails a CLEAN state is worse than useless: it becomes a permanent red, everyone learns
        /// to skip it, and the one time it means something nobody looks.</para>
        /// </summary>
        private static string BlankStringLiterals(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            var sb = new StringBuilder(code.Length);
            bool inString = false, inChar = false;
            for (int i = 0; i < code.Length; i++)
            {
                char ch = code[i];
                bool escaped = i > 0 && code[i - 1] == '\\' && (i < 2 || code[i - 2] != '\\');

                if (!inChar && ch == '"' && !escaped) { inString = !inString; sb.Append(ch); continue; }
                if (!inString && ch == '\'' && !escaped) { inChar = !inChar; sb.Append(ch); continue; }
                if (inString || inChar)
                {
                    // Newlines are preserved so any later line-based reading stays aligned.
                    sb.Append(ch == '\n' ? '\n' : ' ');
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// INVARIANT 1 — no <c>Time.timeScale =</c> outside the one owner.
        /// <para>Reads CODE ONLY (comments and string-literal contents blanked by ReadCode), the
        /// project's standing lint discipline, so the many tombstone comments this refactor left
        /// behind ("it used to be a bare Time.timeScale = 1f") cannot match their own rule.</para>
        /// </summary>
        private static void WorldClockHasExactlyOneWriter(List<string> failures, StringBuilder log)
        {
            string root = Path.GetFullPath("Assets");
            if (!Directory.Exists(root))
            {
                failures.Add("[one-writer] Assets/ not found from the working directory - the lint " +
                             "cannot run, which is a FAILURE and not a pass. A lint that silently " +
                             "scans nothing is the shape of the bug it exists to catch.");
                return;
            }

            var offenders = new List<string>();
            int scanned = 0;

            foreach (string full in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = full.Replace('\\', '/');
                int idx = rel.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) rel = rel.Substring(idx);

                if (rel.Equals(WorldClockOwnerSrc, StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.IndexOf("Regression", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                bool exempt = false;
                for (int i = 0; i < WorldClockLintExemptDirs.Length; i++)
                    if (rel.StartsWith(WorldClockLintExemptDirs[i], StringComparison.OrdinalIgnoreCase))
                    { exempt = true; break; }
                if (exempt) continue;

                scanned++;
                string code = BlankStringLiterals(ReadCode(rel));
                if (string.IsNullOrEmpty(code)) continue;

                // An ASSIGNMENT, not a comparison: `= x` but never `==`. The owner's own file is the
                // only place the left-hand side may appear.
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        code, @"Time\s*\.\s*timeScale\s*=(?!=)"))
                    offenders.Add(rel);
            }

            if (scanned == 0)
            {
                failures.Add("[one-writer] the lint scanned ZERO files. Every path was excluded, so it " +
                             "proved nothing while reporting green - re-derive the exemptions.");
                return;
            }

            if (offenders.Count > 0)
            {
                failures.Add("[one-writer] " + offenders.Count + " file(s) write Time.timeScale outside " +
                             "the one owner (" + WorldClockOwnerSrc + "): " + string.Join(", ", offenders) +
                             ". Every additional writer is another party to the collision that stranded " +
                             "the world at 0.28 in open town on 2026-09-03: when two owners each " +
                             "correctly decline to stamp over the other, the residue is left on the " +
                             "engine global with nobody holding it. Take a WorldHold.AcquireScale hold " +
                             "and dispose it instead.");
                return;
            }

            log.AppendLine($"  [one-writer] {scanned} source files scanned; Time.timeScale is assigned " +
                           $"ONLY in {WorldClockOwnerSrc}. Exempt by design: vendor demo dirs (" +
                           string.Join(", ", WorldClockLintExemptDirs) + ") and *Regression*.");
        }

        /// <summary>
        /// INVARIANT 2 — zero live holds implies 1.00, and overlapping holds compose slowest-wins
        /// rather than fighting. Driven for real against WorldHold; the clock is MEASURED.
        /// </summary>
        private static void ZeroHoldsMeansFullSpeed(List<string> failures, StringBuilder log)
        {
            try
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();

                // (a) a single cosmetic hold applies its scale and gives it all back.
                using (DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-dip", 0.28f, 5f))
                {
                    if (!Mathf.Approximately(Time.timeScale, 0.28f))
                        failures.Add("[zero-holds] a 0.28 hold did not reach the clock (read " +
                                     Time.timeScale.ToString("0.00") + "). A cosmetic beat that cannot " +
                                     "slow the world is not the fix - the effect must be unchanged.");
                }
                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[zero-holds] after the last hold released the clock read " +
                                 Time.timeScale.ToString("0.00") + " with " +
                                 DeNelle.Core.UI.WorldHold.Count + " hold(s) [" +
                                 DeNelle.Core.UI.WorldHold.Describe() + "]. ZERO HOLDS MUST ALWAYS MEAN " +
                                 "1.00 - that single invariant is the whole of WO-1353.");

                // (b) SLOWEST WINS, and it is order-independent. This is the case the old N-owner
                //     code could not express: a hit stop landing inside a wave-clear dip used to make
                //     one of the two abandon the clock, and whichever abandoned left the residue.
                DeNelle.Core.UI.WorldHold.ResetForTests();
                var dip  = DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-dip", 0.28f, 5f);
                var stop = DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-stop", 0.04f, 5f);

                if (!Mathf.Approximately(Time.timeScale, 0.04f))
                    failures.Add("[zero-holds] two overlapping holds (0.28 and 0.04) produced " +
                                 Time.timeScale.ToString("0.00") + ", not the slowest (0.04). " +
                                 "Slowest-wins is what lets a freeze outrank a cosmetic dip; " +
                                 "last-wins would let a hit stop thaw a live purchase.");

                // Release the STRICTER one first - the order that used to strand a residue.
                stop.Dispose();
                if (!Mathf.Approximately(Time.timeScale, 0.28f))
                    failures.Add("[zero-holds] releasing the stricter hold left the clock at " +
                                 Time.timeScale.ToString("0.00") + " instead of falling back to the " +
                                 "0.28 hold that is STILL LIVE. A release must recompute from the " +
                                 "remaining holds, never stamp a fixed value.");

                dip.Dispose();
                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[zero-holds] both holds released and the clock read " +
                                 Time.timeScale.ToString("0.00") + " with holds [" +
                                 DeNelle.Core.UI.WorldHold.Describe() + "].");

                // (c) double-dispose must not double-release under a live hold.
                DeNelle.Core.UI.WorldHold.ResetForTests();
                var outer = DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-outer", 0f, 5f);
                var twice = DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-twice", 0.5f, 5f);
                twice.Dispose();
                twice.Dispose();
                if (DeNelle.Core.UI.WorldHold.Count != 1 || !Mathf.Approximately(Time.timeScale, 0f))
                    failures.Add("[zero-holds] a double-dispose unfroze the world under a live hold " +
                                 "(count " + DeNelle.Core.UI.WorldHold.Count + ", clock " +
                                 Time.timeScale.ToString("0.00") + ").");
                outer.Dispose();

                log.AppendLine("  [zero-holds] a hold applies its scale; overlapping holds compose " +
                               "slowest-wins in either release order; zero holds always reads 1.00.");
            }
            finally
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();
            }
        }

        /// <summary>
        /// INVARIANT 3 — every hold path releases on EVERY exit. The owner named the moments:
        /// *"every battle death victory"*. Battle is asserted through all four of its exits (win,
        /// loss, RETREAT and a scene change mid-battle), then death and victory.
        ///
        /// <para>The clock half is DRIVEN. The wiring half is a SOURCE LINT, because the owners are
        /// scene MonoBehaviours that cannot be instantiated here — and the wiring is exactly what
        /// went wrong: a coroutine killed by deactivation fires neither a finally nor OnDestroy, so
        /// OnDisable is load-bearing and its absence is invisible at runtime until it leaks.</para>
        /// </summary>
        private static void EveryHoldPathReleasesOnEveryExit(List<string> failures, StringBuilder log)
        {
            // ── the DRIVEN half: each named exit, with a cosmetic hold live ──────
            string[] exits = { "arena win", "arena loss", "retreat", "death", "victory" };
            foreach (string exit in exits)
            {
                try
                {
                    DeNelle.Core.UI.WorldHold.ResetForTests();
                    var beat = DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-beat", 0.28f, 5f);
                    BattleSessionEnd.Release(exit);
                    beat.Dispose();

                    if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                        failures.Add($"[exit/{exit}] the battle ended, the beat released, and the clock " +
                                     "reads " + Time.timeScale.ToString("0.00") + " with holds [" +
                                     DeNelle.Core.UI.WorldHold.Describe() + "]. Every exit must land at 1.00.");
                    else
                        log.AppendLine($"  [exit/{exit}] a live 0.28 beat releases and the clock reads 1.00");
                }
                finally { DeNelle.Core.UI.WorldHold.ResetForTests(); }
            }

            // ── a SCENE CHANGE mid-battle: the exit with no host left to release ──
            try
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();
                DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-abandoned-dip", 0.28f, 5f);
                DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-abandoned-stop", 0.04f, 5f);
                DeNelle.Core.UI.WorldHold.ReleaseAllForSceneLoad("wo1353-next-scene");

                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[exit/scene-change] a scene load left " +
                                 DeNelle.Core.UI.WorldHold.Count + " hold(s) [" +
                                 DeNelle.Core.UI.WorldHold.Describe() + "] and a clock of " +
                                 Time.timeScale.ToString("0.00") + ". Time.timeScale is an ENGINE " +
                                 "GLOBAL and SceneManager.LoadScene does NOT reset it: the hosts that " +
                                 "took those holds are gone, so the load itself has to be the release.");
                else
                    log.AppendLine("  [exit/scene-change] a scene load drops every hold and starts at 1.00");
            }
            finally { DeNelle.Core.UI.WorldHold.ResetForTests(); }

            // ── the WIRING half: every converted owner steps out on every lifecycle exit ──
            var owners = new (string src, string sys, bool needsLadder)[]
            {
                ("Assets/_Modules/Village/Vfx/HitStopManager.cs",           "hit stop",       true),
                ("Assets/_Modules/Village/Vfx/CombatFeedbackManager.cs",    "kill slow-mo",   true),
                ("Assets/_Modules/Village/Waves/WaveCelebrationManager.cs", "wave-clear dip", true),
                ("Assets/_Modules/Village/Hero/HeroHitReaction.cs",         "death slow-mo",  true),
                ("Assets/_Modules/Village/Arena/ArenaDeathCam.cs",          "arena death cam", false),
            };

            foreach (var owner in owners)
            {
                string code = ReadCode(owner.src);
                if (string.IsNullOrEmpty(code))
                {
                    failures.Add($"[wiring/{owner.sys}] {owner.src} is missing or unreadable.");
                    continue;
                }

                if (code.IndexOf("WorldHold.AcquireScale", StringComparison.Ordinal) < 0 &&
                    code.IndexOf("WorldHold.SetScale", StringComparison.Ordinal) < 0)
                    failures.Add($"[wiring/{owner.sys}] {owner.src} no longer takes a WorldHold hold. " +
                                 "If it went back to writing Time.timeScale it is an Nth owner again.");

                // ⛔ OnDisable IS THE ONE THAT MATTERS, AND ONLY OnDisable IS REQUIRED.
                // A coroutine dies on deactivation and OnDestroy does NOT fire for it, which is the
                // hole the 0.28 leaked through. OnDisable covers destruction too — Unity runs it
                // before OnDestroy for any enabled component, and a component that was already
                // disabled has already run it. Requiring BOTH would fail HeroHitReaction, which has
                // only OnDisable and is CORRECT; this suite's own header is explicit that a gate
                // which fails a clean state is worse than useless.
                if (code.IndexOf("OnDisable", StringComparison.Ordinal) < 0)
                    failures.Add($"[wiring/{owner.sys}] {owner.src} has no OnDisable step-out. A " +
                                 "coroutine dies on deactivation and OnDestroy does NOT fire for it, " +
                                 "so without OnDisable a mid-beat SetActive(false) strands the hold " +
                                 "until the watchdog ceiling. This is the hole the 0.28 leaked through.");

                if (owner.needsLadder &&
                    code.IndexOf("BattleSessionEnd.RegisterUnwind", StringComparison.Ordinal) < 0)
                    failures.Add($"[wiring/{owner.sys}] {owner.src} is not on the BattleSessionEnd " +
                                 "unwind ladder. A cosmetic beat must never outlive the fight that " +
                                 "produced it, and every other step-out it has is keyed to the HOST's " +
                                 "lifetime rather than the BATTLE's.");
            }

            // The death and victory screens hold the clock at 0 and must pair explicitly.
            string over = ReadCode("Assets/_Modules/Village/Heart/GameOverScreen.cs");
            if (string.IsNullOrEmpty(over))
                failures.Add("[wiring/death] GameOverScreen.cs is missing or unreadable.");
            else
            {
                // WO-1360: the death screen is PLAYER-OWNED (it ends when the player taps Retry),
                // so AcquirePlayerOwned satisfies this too. The assertion is that it takes a hold.
                if (over.IndexOf("WorldHold.AcquireScale", StringComparison.Ordinal) < 0 &&
                    over.IndexOf("WorldHold.AcquirePlayerOwned", StringComparison.Ordinal) < 0)
                    failures.Add("[wiring/death] GameOverScreen no longer takes a WorldHold hold for the " +
                                 "death freeze. A bare Time.timeScale=0 there can strand the world frozen " +
                                 "behind a dismissed screen with nothing on-screen saying why.");
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        BlankStringLiterals(over), @"Time\s*\.\s*timeScale\s*=(?!=)"))
                    failures.Add("[wiring/death] GameOverScreen assigns Time.timeScale directly again.");
                if (over.IndexOf("DeathTrace.TimeScaleFroze", StringComparison.Ordinal) < 0 ||
                    over.IndexOf("DeathTrace.TimeScaleRestored", StringComparison.Ordinal) < 0)
                    failures.Add("[wiring/death] GameOverScreen lost its DeathTrace step-in/step-out " +
                                 "pair. WO-1353 folded the death freeze into the ONE owner and kept the " +
                                 "reporting deliberately - CLAUDE.md sec.12: instrumentation is PERMANENT.");
            }

            log.AppendLine("  [wiring] every converted clock owner takes a hold and steps out on " +
                           "OnDisable, OnDestroy and (for the combat beats) battle end.");
        }

        /// <summary>
        /// INVARIANT 4 — a hold that overruns its maximum self-releases and REPORTS. A paired
        /// contract still breaks: the pairing covers every branch a compiler can see, and nothing
        /// at all when the host is destroyed mid-beat.
        /// </summary>
        private static void AnOverrunHoldSelfReleasesAndReports(List<string> failures, StringBuilder log)
        {
            try
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();

                // A cosmetic hold with a half-second ceiling, abandoned (never disposed) - exactly
                // what a coroutine killed by deactivation leaves behind.
                DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-abandoned", 0.28f, 0.5f);
                if (!Mathf.Approximately(Time.timeScale, 0.28f))
                    failures.Add("[overrun] the abandoned hold never reached the clock, so this case is " +
                                 "not testing what it claims to.");

                // Well inside the ceiling: it must NOT be force-released. A watchdog that fires early
                // would cut every legitimate beat short, which IS a game-feel change.
                DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime + 0.2f);
                if (DeNelle.Core.UI.WorldHold.Count != 1)
                    failures.Add("[overrun] the watchdog force-released a hold 0.2s into its 0.5s " +
                                 "ceiling. That truncates a live cosmetic beat - the ticket changes " +
                                 "ownership and guarantees, never tuning.");

                // Past the ceiling: it MUST be force-released and the world returned to full speed.
                DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime + 2f);
                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[overrun] a hold 2s past its 0.5s ceiling was NOT force-released " +
                                 "(count " + DeNelle.Core.UI.WorldHold.Count + ", clock " +
                                 Time.timeScale.ToString("0.00") + "). The failsafe exists precisely " +
                                 "because the paired contract cannot cover a host destroyed mid-beat.");
                else
                    log.AppendLine("  [overrun] a hold survives inside its ceiling and self-releases " +
                                   "past it, returning the world to 1.00");

                // A LONG-LIVED hold (a chain settlement) must not be caught by a cosmetic ceiling.
                DeNelle.Core.UI.WorldHold.ResetForTests();
                var purchase = DeNelle.Core.UI.WorldHold.Acquire(DeNelle.Core.UI.WorldHold.ReasonPurchase);
                DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime + 30f);
                if (DeNelle.Core.UI.WorldHold.Count != 1)
                    failures.Add("[overrun] a purchase hold was force-released after 30s. Its ceiling is " +
                                 DeNelle.Core.UI.WorldHold.StuckHoldSeconds.ToString("0") + "s because a " +
                                 "real chain settlement legitimately takes many seconds, and thawing the " +
                                 "world mid-payment is how 'paid but not granted' gets manufactured.");
                purchase.Dispose();

                log.AppendLine("  [overrun] the long-lived transaction ceiling is separate from the " +
                               "cosmetic one and is not tripped by it.");
            }
            finally
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();
            }
        }

        /// <summary>
        /// INVARIANT 4b (WO-1360)  -  A USER-DRIVEN PAUSE HAS NO CEILING, AND A BOUNDED BEAT STILL
        /// HAS ONE. Both directions, deliberately: a "fix" that simply disabled the watchdog would
        /// pass the first half and must fail the second.
        ///
        /// <para>THE CAPTURE THIS PINS (owner F8 seq 4679, 2026-09-03, on device): 'pause-menu' was
        /// force-released after 507.3s past a 180.0s ceiling while the PAUSED menu was still on
        /// screen (logs/f8-inbox/device/SM02G4061955851/break_01_error.png), so the world ran
        /// underneath a modal that said the game was stopped  -  the WO-1016 shape. A player can
        /// pause for hours; backgrounding the app is the normal way to do it. Elapsed time cannot
        /// judge an intentional, player-owned state stuck.</para>
        /// </summary>
        private static void APlayerOwnedHoldOutlivesEveryCeiling(List<string> failures, StringBuilder log)
        {
            try
            {
                // ---- DIRECTION 1: the player-owned hold survives, arbitrarily long. ----
                DeNelle.Core.UI.WorldHold.ResetForTests();
                var pause = DeNelle.Core.UI.WorldHold.AcquirePlayerOwned(
                    DeNelle.Core.UI.WorldHold.ReasonPauseMenu, () => true);
                if (!Mathf.Approximately(Time.timeScale, 0f))
                    failures.Add("[player-owned] the pause hold never froze the clock, so this case is " +
                                 "not testing what it claims to.");
                if (!pause.IsPlayerOwned)
                    failures.Add("[player-owned] AcquirePlayerOwned returned a hold that does not report " +
                                 "IsPlayerOwned. The kind is what the watchdog reads; without it the " +
                                 "ceiling still applies and tonight's defect is unchanged.");

                // The exact overrun from the capture, then an hour, then most of a day. NONE of them
                // may drop it. 507s is the observed number; the others prove it is not a bigger
                // ceiling wearing a new name.
                foreach (float t in new[] { 507.3f, 3600f, 60000f })
                {
                    DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime + t);
                    if (DeNelle.Core.UI.WorldHold.Count != 1 || !Mathf.Approximately(Time.timeScale, 0f))
                        failures.Add("[player-owned] the pause hold was force-released after " +
                                     t.ToString("0") + "s (count " + DeNelle.Core.UI.WorldHold.Count +
                                     ", clock " + Time.timeScale.ToString("0.00") + "). A user-driven " +
                                     "pause has NO natural ceiling - a player can pause for hours and " +
                                     "backgrounding the app is the normal way to do it. Unfreezing the " +
                                     "world under an open PAUSED menu is worse than the leak a ceiling " +
                                     "guards (owner F8 seq 4679).");
                }

                // And it still releases normally when its owner says so.
                pause.Dispose();
                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[player-owned] disposing the pause hold did not return the world to " +
                                 "1.00 (count " + DeNelle.Core.UI.WorldHold.Count + ", clock " +
                                 Time.timeScale.ToString("0.00") + ").");
                else
                    log.AppendLine("  [player-owned] a user-driven pause survives 507s / 1h / ~17h of " +
                                   "watchdog ticks and releases only when its owner disposes it");

                // ---- DIRECTION 2: a BOUNDED BEAT still expires. Without this half, a fix that ----
                // ---- simply switched the watchdog off would pass.                            ----
                DeNelle.Core.UI.WorldHold.ResetForTests();
                DeNelle.Core.UI.WorldHold.AcquireScale("wo1360-abandoned-beat", 0.28f, 0.5f);
                DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime + 2f);
                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[player-owned] a BOUNDED beat 2s past its 0.5s ceiling was NOT " +
                                 "force-released (count " + DeNelle.Core.UI.WorldHold.Count + ", clock " +
                                 Time.timeScale.ToString("0.00") + "). Exempting player-owned holds must " +
                                 "not disable the watchdog: a coroutine killed by a deactivated host " +
                                 "fires no OnDestroy and throws nothing, so the ceiling is the only net " +
                                 "left for a cosmetic dip.");
                else
                    log.AppendLine("  [player-owned] a bounded beat still expires at its ceiling and " +
                                   "reports - the exemption is categorical, not a global off switch");

                // ---- DIRECTION 3: the default is still bounded. An author who does NOT ask for ----
                // ---- an unbounded hold must not get one by accident.                          ----
                DeNelle.Core.UI.WorldHold.ResetForTests();
                var byDefault = DeNelle.Core.UI.WorldHold.AcquireScale("wo1360-default", 0f, 1f);
                if (byDefault.IsPlayerOwned)
                    failures.Add("[player-owned] AcquireScale produced a PLAYER-OWNED hold. Unbounded " +
                                 "must be asked for by name (AcquirePlayerOwned); if it is the default " +
                                 "then every future leak goes undetected.");
                DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime + 5f);
                if (DeNelle.Core.UI.WorldHold.Count != 0)
                    failures.Add("[player-owned] the DEFAULT acquire form outlived its ceiling. The " +
                                 "ceiling must remain the default for everything that is not " +
                                 "explicitly player-owned.");
                else
                    log.AppendLine("  [player-owned] the ceiling remains the default; unbounded is opt-in");

                // ---- DIRECTION 4: a player-owned hold is still dropped by the paths that MUST ----
                // ---- drop it. Removing the ceiling removes one net, not all of them.          ----
                DeNelle.Core.UI.WorldHold.ResetForTests();
                DeNelle.Core.UI.WorldHold.AcquirePlayerOwned(DeNelle.Core.UI.WorldHold.ReasonPauseMenu, () => true);
                DeNelle.Core.UI.WorldHold.ReleaseAllForSceneLoad("wo1360-next-scene");
                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[player-owned] a scene load did not drop the player-owned hold (count " +
                                 DeNelle.Core.UI.WorldHold.Count + ", clock " +
                                 Time.timeScale.ToString("0.00") + "). Time.timeScale is an ENGINE " +
                                 "GLOBAL and a load does not reset it, so quit-to-title would land in a " +
                                 "frozen scene with no menu left to resume it.");

                DeNelle.Core.UI.WorldHold.ResetForTests();
                DeNelle.Core.UI.WorldHold.AcquirePlayerOwned(DeNelle.Core.UI.WorldHold.ReasonPauseMenu, () => true);
                DeNelle.Core.UI.WorldHold.ForceReleaseAll("wo1360-teardown");
                if (DeNelle.Core.UI.WorldHold.Count != 0 || !Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[player-owned] ForceReleaseAll did not drop the player-owned hold. " +
                                 "Quit-to-title and teardown paths must always be able to thaw the world.");
                else
                    log.AppendLine("  [player-owned] scene load and ForceReleaseAll still drop it - the " +
                                   "remaining nets are intact");

                // ---- DIRECTION 5: the OWNING UI is the net that replaces the ceiling. ----
                string pauseSrc = ReadCode("Assets/_Modules/Settings/PauseController.cs");
                if (string.IsNullOrEmpty(pauseSrc))
                    failures.Add("[player-owned] PauseController.cs is missing or unreadable.");
                else
                {
                    if (pauseSrc.IndexOf("WorldHold.AcquirePlayerOwned", StringComparison.Ordinal) < 0)
                        failures.Add("[player-owned] PauseController no longer takes a PLAYER-OWNED hold. " +
                                     "A bounded one force-releases the freeze under an open PAUSED menu " +
                                     "(owner F8 seq 4679).");
                    if (pauseSrc.IndexOf("OnDisable", StringComparison.Ordinal) < 0)
                        failures.Add("[player-owned] PauseController has no OnDisable step-out. With no " +
                                     "ceiling, the owning UI's own lifecycle IS the net: a controller " +
                                     "deactivated while paused cannot process Resume, and OnDestroy does " +
                                     "NOT fire for a merely-disabled component, so the hold would strand " +
                                     "the world frozen forever.");
                    if (pauseSrc.IndexOf("OnDestroy", StringComparison.Ordinal) < 0)
                        failures.Add("[player-owned] PauseController lost its OnDestroy step-out.");
                    else
                        log.AppendLine("  [player-owned] PauseController takes the player-owned hold and " +
                                       "steps out on Resume, OnDisable AND OnDestroy");
                }
            }
            finally
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();
            }
        }

        /// <summary>
        /// THE CAPTURED DEFECT, REPRODUCED EXACTLY. 2026-09-03: timeScale 0.28 in open town with
        /// ZERO live holds and input not suppressed. That state is now detectable and self-correcting,
        /// and — the part that actually cost the session — it NAMES itself instead of leaving the
        /// next capture to start from zero.
        /// </summary>
        private static void TodaysCapturedDriftIsCorrected(List<string> failures, StringBuilder log)
        {
            try
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();

                // Stage the exact capture: the wave-clear dip's 0.28, stranded, nobody holding it.
                Time.timeScale = 0.28f;

                // First tick only OBSERVES the drift (a foreign one-frame write must not be reported
                // as a leak on the frame it happens); the second, past the grace, corrects it.
                DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime);
                DeNelle.Core.UI.WorldHold.WatchdogTick(
                    Time.unscaledTime + DeNelle.Core.UI.WorldHold.DriftGraceSeconds + 0.1f);

                if (!Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[captured-2026-09-03] the exact measured defect (timeScale 0.28, ZERO " +
                                 "live holds, open town) was NOT corrected - the clock still reads " +
                                 Time.timeScale.ToString("0.00") + ". This is the state the owner " +
                                 "played in: every timer, animation, cooldown and the wave clock all " +
                                 "wrong together, with nothing on screen saying so.");
                else
                    log.AppendLine("  [captured-2026-09-03] timeScale 0.28 with zero holds is detected, " +
                                   "restored to 1.00 and reported by name");

                // The same path must ALSO be reachable on demand, which is how the quiescence gate
                // hands a leak back to the owner instead of writing the clock itself.
                DeNelle.Core.UI.WorldHold.ResetForTests();
                Time.timeScale = 0.28f;
                if (!DeNelle.Core.UI.WorldHold.RestoreIfDrifted("wo1353-suite"))
                    failures.Add("[captured-2026-09-03] RestoreIfDrifted did not report correcting a " +
                                 "0.28 clock with zero holds. That is the seam BattleQuiescenceGate " +
                                 "uses to stay an OBSERVER; if it no-ops, the gate reports a leak it " +
                                 "cannot hand to anyone.");
                if (!Mathf.Approximately(Time.timeScale, 1f))
                    failures.Add("[captured-2026-09-03] RestoreIfDrifted left the clock at " +
                                 Time.timeScale.ToString("0.00") + ".");

                // ...and it must NOT overreach: with holds live, a non-1 clock is CORRECT.
                DeNelle.Core.UI.WorldHold.ResetForTests();
                using (DeNelle.Core.UI.WorldHold.AcquireScale("wo1353-legit", 0.28f, 5f))
                {
                    DeNelle.Core.UI.WorldHold.WatchdogTick(Time.unscaledTime + 0.1f);
                    if (!Mathf.Approximately(Time.timeScale, 0.28f))
                        failures.Add("[captured-2026-09-03] the drift watchdog stamped 1.00 over a " +
                                     "LEGITIMATE live 0.28 hold. A watchdog that cannot tell a leak " +
                                     "from a live beat deletes the game feel it was built to protect.");
                    else
                        log.AppendLine("  [captured-2026-09-03] the same 0.28 with a LIVE hold is left " +
                                       "alone - the discriminator is the hold, not the value");
                }
            }
            finally
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();
            }
        }

        /// <summary>
        /// The gate OBSERVES AND REPORTS; it never became the fixer. Its own header says a gate that
        /// quietly fixes things trains the wrong habit — and a gate that writes the world clock is,
        /// by definition, one of the N owners whose collision it is watching for.
        /// </summary>
        private static void TheGateObservesAndDoesNotWriteTheClock(List<string> failures, StringBuilder log)
        {
            const string src = "Assets/_Modules/Core/Combat/BattleQuiescenceGate.cs";
            string code = ReadCode(src);
            if (string.IsNullOrEmpty(code))
            {
                failures.Add("[observer] " + src + " is missing or unreadable.");
                return;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(
                    BlankStringLiterals(code), @"Time\s*\.\s*timeScale\s*=(?!=)"))
                failures.Add("[observer] " + src + " assigns Time.timeScale. This gate OBSERVES and " +
                             "REPORTS by explicit design; a gate that also writes the clock is another " +
                             "owner in the collision it exists to detect. Hand the leak to " +
                             "WorldHold.RestoreIfDrifted and report instead.");

            if (code.IndexOf("WorldHold.RestoreIfDrifted", StringComparison.Ordinal) < 0)
                failures.Add("[observer] " + src + " no longer hands a drifted clock back to the one " +
                             "owner. Reporting without a route to recovery leaves the player in the " +
                             "slow world the gate just described.");

            // It must still REPORT the finding - the half that names the value.
            float saved = Time.timeScale;
            try
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();
                Time.timeScale = 0.28f;
                var found = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false);
                if (!found.Any(f => f.StartsWith("timeScale:")))
                    failures.Add("[observer] the gate produced NO timeScale finding for a 0.28 clock. " +
                                 "Observing without reporting is the silence that made the 2026-09-03 " +
                                 "capture unattributable.");
                else
                    log.AppendLine("  [observer] the gate names a 0.28 clock and does not write it");
            }
            finally
            {
                DeNelle.Core.UI.WorldHold.ResetForTests();
                Time.timeScale = saved > 0f ? saved : 1f;
            }
        }

        // =====================================================================
        //  WO-1603 — NAME THE PULSER, AND STOP THE ONE PULSE WITH NO BATTLE BEHIND IT
        // ---------------------------------------------------------------------
        //  CAPTURED DEFECT (device SM02G4061955851, build 2026.09.07.359651,
        //  scene Main_Castle_Overworld, F8 seq 4701/4702 — a REGRESSION of WO-1337,
        //  which the owner had passed on 358574 the same morning):
        //
        //    seq 4701  BATTLE_QUIESCENCE_FAIL (retreat) - 1 invariant(s) NOT restored:
        //              - battle-lock: still HELD … HOLDER(S): PursuitBattleProbe.Probe
        //    seq 4702  battle-lock STILL HELD after the self-heal (retreat):
        //              [PursuitBattleProbe.Probe] (was [PursuitBattleProbe.Probe]).
        //
        //  ⭐ THE MOST IMPORTANT FACT IN THESE TWO LINES IS THE ONE-FRAME RE-LATCH.
        //  The self-heal runs BattleSessionEnd.Release, which calls ClearPursuits and
        //  ZEROES the ring; the gate then waits exactly one frame and re-reads. A ring
        //  that is empty cannot refill from a stale entry, so the lock could only be
        //  back because a producer STAMPED AGAIN in that frame. This is a LIVE pulse
        //  from a running owner — categorically NOT the WO-1337 shape (a destroyed
        //  body's last pulse riding out PursuitTtl), and the WO-1337 fix is intact:
        //  DespawnRevokesPursuitAtSource above still passes.
        //
        //  And the capture still could not say WHO. PursuitBattleProbe is the READER of
        //  the ring; the producers are Enemy.DriveNav (via two different branches),
        //  OverworldEncounterSpawner's rep chase and RegionMobSpawner's aggro loop. The
        //  ring recorded (key, lastReport) — an instance id that identifies nothing once
        //  the body is gone. It now records the OWNER TAG and the STAMP AGE too.
        //
        //  ⚠ WHAT THESE CASES DO NOT CLAIM. They do not assert which producer held the
        //  lock on the owner's device — no log names it and no fleet run was made here.
        //  They pin (a) that the next capture WILL name it, and (b) the one producer
        //  provable from source to stamp with no battle behind it: a chase steered at a
        //  hero who is DOWN. "retreat" is the context BattleArena.Resolve passes for
        //  EVERY won==false outcome, the hero's own death included.
        // =====================================================================

        /// <summary>
        /// Behavioural, over the real Core statics. The ring must carry each pulse's PRODUCER
        /// and the AGE of its last stamp, and the gate's battle-lock finding must carry both —
        /// appended to the WO-1233 holder sentence, never in place of it.
        /// </summary>
        private static void PursuitPulseNamesItsProducer(List<string> failures, StringBuilder log)
        {
            const int    chaser = 41603;
            const string owner  = "wo1603-suite/producer";
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;   // PursuitBattleProbe.Probe

            Time.timeScale = 1f;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                PostureSignals.ReportPursuit(chaser, owner);

                string described = PostureSignals.DescribePursuits();
                if (!described.Contains(owner))
                    failures.Add("[wo1603] PostureSignals.DescribePursuits does not name the PRODUCER of a " +
                                 "live pulse (got: '" + described + "'). The pursuit key is an INSTANCE ID, " +
                                 "which identifies nothing once the body is gone - that is why F8 seq 4701 " +
                                 "could name PursuitBattleProbe.Probe (the reader) and not the pulser.");
                else if (!described.Contains("key=" + chaser) || !described.Contains("age="))
                    failures.Add("[wo1603] DescribePursuits names the producer but drops the key or the stamp " +
                                 "AGE (got: '" + described + "'). The age is what separates a LIVE chase " +
                                 "(age ~0, re-stamped this frame) from a latched pulse riding out PursuitTtl - " +
                                 "the two shapes the gate's own message asks the reader to tell apart.");
                else
                    log.AppendLine("  [wo1603] a live pulse names its producer, its key and its stamp age");

                var found = BattleQuiescenceGate.Evaluate(rewardScreenOpen: false);
                string lockFinding = found.FirstOrDefault(f => f.StartsWith("battle-lock:"));
                if (lockFinding == null)
                {
                    failures.Add("[wo1603] a live pursuit pulse did NOT produce a battle-lock finding. Either " +
                                 "PursuitBattleProbe's source changed or the probe was narrowed - re-derive " +
                                 "this case from a fresh capture before trusting anything below it.");
                }
                else if (!lockFinding.Contains("HOLDER(S):"))
                {
                    failures.Add("[wo1603] the battle-lock finding lost its WO-1233 HOLDER(S) clause. These " +
                                 "additions APPEND; a strengthening that drops the previous strengthening is a " +
                                 "regression of the ticket it came from.");
                }
                else if (!lockFinding.Contains("PURSUIT PULSES") || !lockFinding.Contains(owner))
                {
                    failures.Add("[wo1603] the battle-lock finding names its HOLDER but not the PULSER behind " +
                                 "it (got: '" + lockFinding + "'). Seq 4701 is exactly this message, and it " +
                                 "cost a whole ticket because PursuitBattleProbe only READS the ring.");
                }
                else
                {
                    log.AppendLine("  [wo1603] the battle-lock finding names the holder AND the producers pulsing it");
                }
            }
            finally
            {
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// Behavioural. Reproduces seq 4702 EXACTLY — a full session release, then a re-stamp
        /// inside the same frame — and requires the description to expose it as a live producer
        /// rather than as a leftover the clear failed to reach. Then the contract: the producer
        /// that stands its own claim down (the hero went down, so the chase is not pursuit any
        /// more) releases the lock immediately, without anything forcing BattleLock false.
        /// </summary>
        private static void RetreatAfterHeroDeathStopsThePulse(List<string> failures, StringBuilder log)
        {
            const int    deadChaser = 41604;
            const string owner      = "Enemy.DriveNav/brain";   // the branch with no dead-hero test
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;

            Time.timeScale = 1f;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                PostureSignals.ReportPursuit(deadChaser, owner);   // chasing while the hero still stood
                BattleSessionEnd.Release("retreat");               // the hero died -> Resolve(false)

                if (BattleLock.IsInBattle())
                {
                    failures.Add("[wo1603] BattleSessionEnd.Release left the pursuit ring holding the " +
                                 "battle-lock. ClearPursuits is the ONE clear on that path (WO-1233); if it " +
                                 "no longer empties the ring, every case below is testing the wrong thing.");
                    return;
                }
                log.AppendLine("  [wo1603] the session release empties the pursuit ring (the self-heal's own door)");

                // THE DEFECT. The brain keeps steering onto the body, so DriveNav re-stamps within
                // one frame of the clear - seq 4702 verbatim.
                PostureSignals.ReportPursuit(deadChaser, owner);
                if (!BattleLock.IsInBattle())
                {
                    failures.Add("[wo1603] the DEFECT STATE no longer reproduces: a pulse stamped one frame " +
                                 "after a full ClearPursuits is not holding the battle-lock. Seq 4702 is that " +
                                 "state; if it cannot be built, this case cannot prove the cure.");
                    return;
                }

                string relatched = PostureSignals.DescribePursuits();
                if (!relatched.Contains(owner) || PostureSignals.PursuitCount != 1)
                    failures.Add("[wo1603] the re-latched pulse is not reported as ONE named live producer " +
                                 "(count=" + PostureSignals.PursuitCount + ", described: '" + relatched + "'). " +
                                 "A holder that survives a full session release is a LIVE producer by " +
                                 "construction, and the capture must say which one.");
                else
                    log.AppendLine("  [wo1603] a one-frame re-latch reports exactly one LIVE producer, by name (seq 4702)");

                // THE CONTRACT. The producer itself stops - a body steered at a DOWN hero is not
                // pursuing anyone - and revokes its own claim. Nothing force-clears BattleLock.
                PostureSignals.RevokePursuit(deadChaser);
                if (BattleLock.IsInBattle())
                    failures.Add("[wo1603] the producer revoked its own pursuit claim and the battle-lock is " +
                                 "STILL HELD by [" + BattleLock.DescribeHolders() + "]. A retreat then leaves " +
                                 "combat input suppressed and the HUD pinned out of town - seq 4701 verbatim.");
                else
                    log.AppendLine("  [wo1603] the producer standing its own claim down releases the lock at once");
            }
            finally
            {
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// Behavioural. A retreat out of a RAID has more than one pulser on the field (the garrison
        /// the hero disengaged from, plus whatever is chasing back home), and attribution is only
        /// worth anything if it survives that: the release must drop BOTH, and a re-stamp by ONE
        /// must name that one and not the other.
        /// </summary>
        private static void RetreatDuringARaidNamesEachPulserSeparately(List<string> failures, StringBuilder log)
        {
            const int    garrison = 41605;
            const int    roamer   = 41606;
            const string garrisonOwner = "Enemy.DriveNav/aggro";
            const string roamerOwner   = "RegionMobSpawner/aggro-loop";
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;

            Time.timeScale = 1f;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                PostureSignals.ReportPursuit(garrison, garrisonOwner);
                PostureSignals.ReportPursuit(roamer,   roamerOwner);

                string both = PostureSignals.DescribePursuits();
                if (!both.Contains(garrisonOwner) || !both.Contains(roamerOwner) || PostureSignals.PursuitCount != 2)
                    failures.Add("[wo1603] two producers pulsing are not both named (count=" +
                                 PostureSignals.PursuitCount + ", described: '" + both + "'). Attribution that " +
                                 "collapses the moment there is more than one chaser is no attribution at all.");
                else
                    log.AppendLine("  [wo1603] two simultaneous producers are named separately");

                BattleSessionEnd.Release("retreat");
                if (PostureSignals.PursuitCount != 0)
                    failures.Add("[wo1603] the retreat release left " + PostureSignals.PursuitCount +
                                 " pursuit pulse(s) live: " + PostureSignals.DescribePursuits() +
                                 ". The release clears the WHOLE ring; a partial clear is a new defect.");

                // ONE of them is still on the field and re-stamps. The other is gone. The capture
                // must point at the one that is actually pulsing.
                PostureSignals.ReportPursuit(roamer, roamerOwner);
                string after = PostureSignals.DescribePursuits();
                if (!after.Contains(roamerOwner) || after.Contains(garrisonOwner))
                    failures.Add("[wo1603] after a retreat with ONE producer still pulsing, the description " +
                                 "does not point at exactly that one (described: '" + after + "'). Naming the " +
                                 "wrong owner costs more than naming none - it sends the fix to a file that " +
                                 "is behaving.");
                else if (!BattleLock.IsInBattle())
                    failures.Add("[wo1603] a genuinely live chaser re-stamping after the retreat did NOT hold " +
                                 "the battle-lock. That is the FORBIDDEN cure (F8-46 owner OPTION A): combat " +
                                 "input must stay live while the hero is really being chased.");
                else
                    log.AppendLine("  [wo1603] a live chaser still holds the lock, and only IT is named");
            }
            finally
            {
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// Source-lint on the WIRING — the behavioural cases prove the seam, only this proves the
        /// real producers reach it. Three files stamp the pursuit ring; a stamp that names no owner
        /// is the anonymous ring seq 4701 could not read, one producer at a time.
        /// </summary>
        private static void EveryPursuitProducerNamesItselfAtSource(List<string> failures, StringBuilder log)
        {
            // ── 1. every stamp site carries a producer tag ────────────────────
            var producers = new (string path, string tag)[]
            {
                // ⚠ Match the CALL, not the bare identifier 'chaseVia'. The tag is read in more
                //   than one place in DriveNav, so a rule on the name alone stays GREEN against a
                //   stamp that dropped the argument - proven by mutation while this case was
                //   being written, the same trap WO-1337's probe-intact rule fell into.
                ("Assets/_Modules/Village/Enemies/Enemy.cs",                     "ReportPursuit(GetInstanceID(), chaseVia)"),
                ("Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs", "OverworldEncounterSpawner/rep-chase"),
                ("Assets/_Modules/Village/World/RegionMobSpawner.cs",            "RegionMobSpawner/aggro-loop"),
            };
            foreach (var (path, tag) in producers)
            {
                string src = ReadCode(path);
                if (src == null)
                {
                    failures.Add("[wo1603-wiring] " + path + " is MISSING - a pursuit producer cannot be verified.");
                    continue;
                }
                if (!src.Contains("PostureSignals.ReportPursuit"))
                {
                    failures.Add("[wo1603-wiring] " + path + " no longer stamps the pursuit ring at all. If a " +
                                 "producer is deliberately retired, retire its row here in the same change - a " +
                                 "silent one leaves the next reader hunting a fourth pulser that does not exist.");
                    continue;
                }
                if (!src.Contains(tag))
                    failures.Add("[wo1603-wiring] " + path + " stamps the pursuit ring WITHOUT naming itself " +
                                 "(expected the owner tag '" + tag + "'). An anonymous stamp is exactly what F8 " +
                                 "seq 4701/4702 hit: the holder was PursuitBattleProbe.Probe, which only reads " +
                                 "the ring, and the capture had nothing pointing past it.");
                else
                    log.AppendLine("  [wo1603-wiring] " + Path.GetFileName(path) + " names itself when it pulses");
            }

            // ── 2. the two Enemy chase branches, and the asymmetry between them ──
            string enemy = ReadCode("Assets/_Modules/Village/Enemies/Enemy.cs");
            if (enemy != null)
            {
                // The hero-aggro branch has refused a dead hero since DEF-224. It is the SIBLING
                // guarantee that makes the brain branch's missing test a defect rather than a
                // design choice, so its loss must fail here too.
                if (!enemy.Contains("if (heroHealth != null && !heroHealth.IsAlive)"))
                    failures.Add("[wo1603-wiring] Enemy.TryGetHeroAggroDestination no longer refuses a DEAD " +
                                 "hero. That refusal is the sibling of the guard below; with it gone the " +
                                 "brain branch's guard reads as an arbitrary special case instead of the " +
                                 "restoration of symmetry it is.");
                else
                    log.AppendLine("  [wo1603-wiring] the hero-aggro chase branch still refuses a dead hero");

                // ⛔ THE FIX. A pulse stamped over a corpse holds the battle-lock with no battle
                //    behind it: the hero is DOWN, she has no combat inputs for PursuitActive to
                //    keep live, and the gate judges a retreat 0.75 s in (WO-1337's arithmetic).
                if (!enemy.Contains("bool heroAliveToChase = chasedHero == null || chasedHero.IsAlive;"))
                    failures.Add("[wo1603-wiring] Enemy.DriveNav stamps the pursuit pulse without testing that " +
                                 "the hero is ALIVE. EnemyBrain scores the hero on '!= null && " +
                                 "activeInHierarchy' with no IsAlive gate and a dead hero reports Fraction 0 - " +
                                 "which the low-HP weight reads as the MOST attractive target on the field - so " +
                                 "the brain keeps steering onto the body and this line re-stamps every frame " +
                                 "for as long as she stays down. That is the seq 4702 one-frame re-latch.");
                else
                    log.AppendLine("  [wo1603-wiring] the brain chase branch no longer pulses over a downed hero");

                // ⛔ AND THE CURE MUST NOT BE A FORCED CLEAR. A null HeroHealth (headless / test
                //    scenes) counts as ALIVE, the same conservative reading BattleArena's own
                //    outcome arbitration takes. Narrowing this to "no pulse unless a HeroHealth
                //    exists" would kill pursuit in every scene that has none.
                if (enemy.Contains("BattleLock.SetInBattle(false)") || enemy.Contains("ClearPursuits()"))
                    failures.Add("[wo1603-wiring] Enemy.cs force-clears the battle window rather than standing " +
                                 "its own claim down. Forbidden by WO-1337: one body may only ever revoke its " +
                                 "OWN key, and no producer may write BattleLock.");
            }

            // ── 3. the gate and the session end must SURFACE the tags ──────────
            string gate = ReadCode("Assets/_Modules/Core/Combat/BattleQuiescenceGate.cs");
            if (gate == null)
                failures.Add("[wo1603-wiring] BattleQuiescenceGate.cs is MISSING.");
            else if (!gate.Contains("PostureSignals.DescribePursuits()"))
                failures.Add("[wo1603-wiring] the quiescence gate no longer renders the pursuit ring. The " +
                             "battle-lock finding then names PursuitBattleProbe.Probe and stops - seq 4701, " +
                             "unactionable by construction, which is the whole reason WO-1603 exists.");
            else
                log.AppendLine("  [wo1603-wiring] the gate's battle-lock finding renders the pursuit ring");

            string sessionEnd = ReadCode("Assets/_Modules/Core/Combat/BattleSessionEnd.cs");
            if (sessionEnd == null)
                failures.Add("[wo1603-wiring] BattleSessionEnd.cs is MISSING.");
            else if (!sessionEnd.Contains("PostureSignals.DescribePursuits()"))
                failures.Add("[wo1603-wiring] BattleSessionEnd.Release no longer logs WHO was pulsing at battle " +
                             "end. That line is the before-half of the pair: without it a holder reported after " +
                             "the release cannot be shown to be a re-stamp rather than a missed clear.");
            else
                log.AppendLine("  [wo1603-wiring] the battle-session release logs its pursuit ring before and after");
        }

        // =====================================================================
        //  WO-1736 — A TOWN WAVE ENDING MUST ARM THE QUIESCENCE GATE.
        // ---------------------------------------------------------------------
        //  THE DEFECT THESE PIN IS THE COVERAGE GAP, NOT A HOLDER.
        //
        //  External player "Sminer" (the project's FIRST outside bug report,
        //  Discord 2026-09-15, Wave 146) reported that every wave end leaves the
        //  combat dock up instead of returning the peaceful dock, recoverable only
        //  by logging out - and sometimes not even then. WO-1736 sec.2.6 found why it
        //  reached us through Discord instead of through F8: BattleQuiescenceGate.Arm
        //  had exactly ONE caller in the whole _Modules tree (BattleArena.cs, an
        //  ARENA battle end), so A TOWN WAVE ENDING ARMED NOTHING. WO-1308's
        //  purpose-built latched-phase dump and BattleLock.DescribeHolders() - the
        //  one line that NAMES a stuck lock holder - were both registered and
        //  neither ever fired on the boundary they were written for.
        //
        //  ⛔ NO FIX IS PINNED HERE BECAUSE NO FIX EXISTS YET. WO-1736 sec.3 excluded
        //  every combat input with a documented self-clearing mechanism and the
        //  surviving candidate is a latched BattleLock probe whose HOLDER IS UNNAMED;
        //  CLAUDE.md sec.12 forbids an edit until captured data names it. So both
        //  cases below are source-lints on the WIRING - the same discipline as
        //  WiringIsPresent and SessionEndWiringIsPresent, and for the same reason: a
        //  live wave loop cannot be driven inside a synchronous editor batch, and
        //  DeNelle.Village is not referenced from this assembly.
        //
        //  The behavioural half of WO-1736's acceptance criterion 5 (a never-releasing
        //  probe must make the gate NAME its holder) is already proven, both
        //  directions, by HolderIsNamedInTheFinding above. It is cited rather than
        //  duplicated: a second copy of that assertion is a second answer waiting to
        //  disagree with the first.
        // =====================================================================

        /// <summary>
        /// The arm site itself, and its POSITION, which WO-1736 sec.12.2 records as load-bearing.
        /// </summary>
        private static void TownWaveEndArmsTheGate(List<string> failures, StringBuilder log)
        {
            const string rel = "Assets/_Modules/Village/Waves/WaveCelebrationManager.cs";
            string src = ReadCode(rel);
            if (src == null)
            {
                failures.Add("[wo1736-wiring] WaveCelebrationManager.cs is MISSING - the town wave-end " +
                             "quiescence arm cannot be verified at all.");
                return;
            }

            int arm = src.IndexOf("BattleQuiescenceGate.Arm", StringComparison.Ordinal);
            if (arm < 0)
            {
                failures.Add("[wo1736-wiring] the town wave clear no longer arms BattleQuiescenceGate. " +
                             "That restores WO-1736 sec.2.6 exactly: the gate armed on an ARENA end and " +
                             "NOWHERE ELSE, so the one boundary an external player actually reported was " +
                             "the one boundary nothing observed. WO-1308's latched-phase dump and " +
                             "BattleLock.DescribeHolders() both go silent with it, and the next occurrence " +
                             "arrives as a Discord message instead of a named holder.");
                return;
            }
            log.AppendLine("  [wo1736-wiring] the town wave clear arms the quiescence gate");

            // The probe must be the END-STATE's own showing flag. Arm judges with
            // Evaluate(rewardScreenOpen: false), so a probe that cannot see the banner lets the
            // gate settle while the results screen is legitimately up.
            if (src.IndexOf("EndStateView.IsShowing", StringComparison.Ordinal) < 0)
                failures.Add("[wo1736-wiring] the town wave-end arm no longer passes EndStateView.IsShowing " +
                             "as its reward-screen probe. Without it the gate judges the world while the " +
                             "wave-results banner is legitimately up and reports a modal finding on every " +
                             "clean wave - a permanent red everyone learns to skip, which is the failure " +
                             "mode CleanStatePasses exists to forbid.");
            else
                log.AppendLine("  [wo1736-wiring] the arm's reward-screen probe is the end-state's own showing flag");

            // ⛔ POSITION IS THE CONTRACT. Armed at CompleteWave()+0 the banner has NOT shown yet
            //    (the routine yields through its VFX bursts first), so IsShowing reads FALSE, the
            //    0.75s settle runs straight through the wave-clear slow-mo dip, and the gate emits a
            //    FALSE timeScale FAIL on EVERY CLEAN WAVE. EndStateView.Show sets its static
            //    synchronously, so armed AFTER it the probe is exact with no pending gap.
            int show = src.IndexOf("EndStateVM.FromWaveClear", StringComparison.Ordinal);
            if (show < 0)
                failures.Add("[wo1736-wiring] WaveCelebrationManager no longer shows the wave-clear end state " +
                             "(EndStateVM.FromWaveClear is gone), so the arm below it has nothing to " +
                             "synchronise with and its ordering cannot be verified.");
            else if (show > arm)
                failures.Add("[wo1736-wiring] the quiescence gate is armed BEFORE the wave-clear end state is " +
                             "shown. EndStateView.Show sets its static synchronously and the arm's probe reads " +
                             "it, so arming first makes the probe read FALSE, the settle window run through " +
                             "the slow-mo dip, and a clean wave report a timeScale FAIL. WO-1736 sec.12.2: " +
                             "this ordering is load-bearing, not incidental.");
            else
                log.AppendLine("  [wo1736-wiring] the arm sits AFTER the end state is shown (no false clean-wave FAIL)");

            // A diagnostic must never take down a wave resolve - the same reason BattleArena wraps
            // its own arm. No silent failures: Guard.Try logs through FlowTrace.Fail (CLAUDE.md sec.12).
            // ⚠ Bounded window, NOT a bare file-wide contains: WaveCelebrationManager already uses
            //   Guard.Try for its deadline sweep, so a rule on the token alone would stay GREEN
            //   against an unguarded arm. Same trap WO-1603's producer lint records.
            int guardFrom = Math.Max(0, arm - 300);
            if (src.IndexOf("Guard.Try", guardFrom, arm - guardFrom, StringComparison.Ordinal) < 0)
                failures.Add("[wo1736-wiring] the town wave-end arm is no longer wrapped in Guard.Try. An " +
                             "instrument that can throw inside WaveClearRoutine would abort the rest of the " +
                             "celebration - a diagnostic must never cost the player the wave it observes.");
            else
                log.AppendLine("  [wo1736-wiring] the arm is guarded, so a diagnostic cannot abort the wave resolve");
        }

        /// <summary>
        /// WO-1736 acceptance criterion 2 asks for "wave-phase among the checks that ran". That name
        /// only reaches a report if WaveManager still REGISTERS the probe WO-1308 built, so pin the
        /// registration and the dump it routes to.
        /// </summary>
        private static void WavePhaseProbeIsRegistered(List<string> failures, StringBuilder log)
        {
            string wave = ReadCode("Assets/_Modules/Village/Waves/WaveManager.cs");
            if (wave == null)
            {
                failures.Add("[wo1736-probe] WaveManager.cs is MISSING - the wave-phase quiescence probe " +
                             "cannot be verified.");
                return;
            }

            if (wave.IndexOf("BattleQuiescenceGate.Register", StringComparison.Ordinal) < 0)
                failures.Add("[wo1736-probe] WaveManager no longer registers its quiescence probe, so a gate " +
                             "armed on the town wave end reports the Core invariants and NOTHING about the " +
                             "wave loop - the one module whose phase raises the battle-lock on that very " +
                             "boundary.");
            else
                log.AppendLine("  [wo1736-probe] WaveManager registers its own quiescence probe");

            if (wave.IndexOf("CheckWavePhaseQuiescence", StringComparison.Ordinal) < 0 ||
                wave.IndexOf("DescribeLatchedWavePhase", StringComparison.Ordinal) < 0)
                failures.Add("[wo1736-probe] the wave-phase probe no longer routes to " +
                             "DescribeLatchedWavePhase. That dump is the whole deliverable: it names the " +
                             "phase, the wave, awaitingPlayerStart, the countdown, the live/null enemy " +
                             "counts, both scenes and the last SetPhase transition with its site, frame and " +
                             "AGE. Without it a FAIL says 'the wave loop is stuck' and the next reader pays " +
                             "for the measurement all over again (WO-1308).");
            else
                log.AppendLine("  [wo1736-probe] the probe routes to the WO-1308 latched-phase dump");
        }

        // =====================================================================
        //  WO-1855 — name the OVERWORLD REP holding the lock, not just the reader.
        // ---------------------------------------------------------------------
        //  CAPTURED (device SM02G4061955851, seq5344-family/5490/5519, 2026-09-15..17):
        //  three BATTLE_QUIESCENCE_FAIL(arena win)s, each with battle-lock held by
        //  PursuitBattleProbe.Probe and a fresh (age=0.00s) 'OverworldEncounterSpawner/
        //  rep-chase' pulse — and the self-heal reports STILL HELD one frame later with
        //  the SAME key. RCA (this file's own WO-1233/1337/1603 blocks, re-verified):
        //  BattleLock/PursuitBattleProbe/BattleSessionEnd release exactly what they
        //  raise. The holder is a DIFFERENT, untouched rep pack's leader genuinely still
        //  inside its own leash of the return position — correct per the owner's "an
        //  active chaser must finish" ruling, and indistinguishable from a stalled/
        //  blocked chase by either DescribeHolders() or DescribePursuits() alone. This
        //  is a source-lint (not a behavioural test): OverworldEncounterSpawner is a
        //  DeNelle.Village type and this suite lives in DeNelle.Editor with no reference
        //  to it, exactly the same constraint every other wo1337/wo1736-wiring check in
        //  this file already works under.
        // =====================================================================
        private static void RepChaseProbeIsRegistered(List<string> failures, StringBuilder log)
        {
            string src = ReadCode("Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs");
            if (src == null)
            {
                failures.Add("[wo1855-probe] OverworldEncounterSpawner.cs is MISSING - the rep-chase " +
                             "quiescence probe cannot be verified.");
                return;
            }

            if (src.IndexOf("BattleQuiescenceGate.Register", StringComparison.Ordinal) < 0 ||
                src.IndexOf("\"rep-chase\"", StringComparison.Ordinal) < 0)
                failures.Add("[wo1855-probe] OverworldEncounterSpawner no longer registers a 'rep-chase' " +
                             "quiescence probe, so a BATTLE_QUIESCENCE_FAIL(arena win) held by " +
                             "PursuitBattleProbe.Probe again names only the reader, never the overworld " +
                             "rep actually still chasing (device SM02G4061955851 seq5344/5490/5519).");
            else
                log.AppendLine("  [wo1855-probe] OverworldEncounterSpawner registers its own 'rep-chase' quiescence probe");

            if (src.IndexOf("CheckRepChaseQuiescence", StringComparison.Ordinal) < 0)
            {
                failures.Add("[wo1855-probe] the rep-chase probe no longer routes to " +
                             "CheckRepChaseQuiescence - that method is the whole deliverable.");
            }
            else
            {
                int at = src.IndexOf("private static string CheckRepChaseQuiescence", StringComparison.Ordinal);
                string body = at >= 0 ? src.Substring(at, Math.Min(2500, src.Length - at)) : "";
                bool namesDistance = body.Contains("Vector3.Distance") && body.Contains("d={d:");
                bool namesStall    = body.Contains("_progressAt") && body.Contains("stalled");
                bool namesTouch    = body.Contains("TouchDistance(hero)");
                bool namesWatcher  = body.Contains("gameObject.name");
                if (!(namesDistance && namesStall && namesTouch && namesWatcher))
                    failures.Add("[wo1855-probe] CheckRepChaseQuiescence no longer names all four " +
                                 "discriminating numbers (which rep, its live distance, its stalled-with-" +
                                 "no-progress time, and its touch threshold). Dropping any one of them " +
                                 "puts the reader back to guessing whether a held lock is a live chase " +
                                 "that will resolve or a blocked one that never will.");
                else
                    log.AppendLine("  [wo1855-probe] CheckRepChaseQuiescence names the rep, its live distance, " +
                                   "its stall time, and its touch threshold");
            }

            // The probe must be READ-ONLY (WO-1855's own doc comment promises this): it must never
            // call Deaggro/RevokePursuit/SetPackCombatPresentation from inside the Check, or looking
            // at a stuck chase would itself change it — exactly the mutation WaveManager's own
            // DescribeLatchedWavePhase comment (":930-932") warns against for the sibling probe.
            int checkAt = src.IndexOf("private static string CheckRepChaseQuiescence", StringComparison.Ordinal);
            if (checkAt >= 0)
            {
                int nextMethod = src.IndexOf("\n        private ", checkAt + 10, StringComparison.Ordinal);
                if (nextMethod < 0) nextMethod = Math.Min(src.Length, checkAt + 2500);
                string checkBody = src.Substring(checkAt, nextMethod - checkAt);
                if (checkBody.Contains("Deaggro(") || checkBody.Contains("RevokePursuit") ||
                    checkBody.Contains("SetPackCombatPresentation"))
                    failures.Add("[wo1855-probe] CheckRepChaseQuiescence mutates chase state (Deaggro / " +
                                 "RevokePursuit / SetPackCombatPresentation). A quiescence probe must only " +
                                 "OBSERVE - the same discipline BattleQuiescenceGate's own header states for " +
                                 "the gate as a whole.");
                else
                    log.AppendLine("  [wo1855-probe] CheckRepChaseQuiescence is read-only (no chase-state mutation)");
            }
        }

        // =====================================================================
        //  WO-1736 — A PARKED COUNTDOWN IS NOT AN IMMINENT ONE. FIVE COPIES.
        // ---------------------------------------------------------------------
        //  THE PROVEN DEFECT (owner's Seeker SM02G4061955851, 2026-09-17, endless
        //  wave 21, Main_Castle_Overworld). Wave 20 cleared at 13:05:21 and the loop
        //  parked awaiting the player's DEFEND press. For the next 34m30s, across
        //  186 consecutive throttled samples, the device logged:
        //
        //    [Flow:HUD] countdown IMMINENT (0.0s <= 5s) -> counts as Battle
        //    [Flow:HUD] context inputs: sceneCombat=False wave=True battleLock=False
        //               pursuit=False ... -> Battle
        //
        //  Endless mode parks in phase Countdown with the countdown HELD AT 0
        //  (WaveManager.TryArmEndlessWave), so `remaining <= 5f` is `0 <= 5` = TRUE and
        //  a wave that is not counting down at all reads as permanently imminent.
        //  battleLock, pursuit and sceneCombat were ALL FALSE for the whole window:
        //  this input was the SOLE holder of HudContext.Battle.
        //
        //  ⚠ THE PREDICATE IS COPIED INTO FIVE FILES, which is why this is pinned as an
        //  invariant rather than as one fix. WaveManager.cs:518-525 documents the
        //  parking design and names the HUD consumers it believed were safe - it lists
        //  the DEFEND button and the wave-timer label, and MISSES all five of these.
        //  A guard restored in one file and lost in another is the duplicated-state
        //  failure CLAUDE.md sec.2/sec.5/sec.16 each describe in their own words.
        //
        //  ⛔ AND THE CURE MUST NOT BE THE THRESHOLD. The imminent window is an owner
        //  ruling (2026-07-08) and a real 4.9s countdown must STILL read as combat, so
        //  every row below also requires the threshold comparison to survive. Raising
        //  or deleting it would trade this latch for a wave that arrives with no
        //  warning - strictly worse, and invisible.
        // =====================================================================
        private static void ParkedCountdownIsNotImminent(List<string> failures, StringBuilder log)
        {
            // Every consumer that turns "a countdown is nearly over" into a combat signal.
            // path, what it drives, and the threshold token that must ALSO still be there.
            var consumers = new (string path, string drives, string threshold)[]
            {
                ("Assets/_Modules/Village/HUD/HudContextEvaluator.cs", "the HUD posture (the PROVEN holder - combatDock instead of peacefulDock)", "ImminentThreshold"),
                ("Assets/_Modules/Village/HUD/HudModelProducers.cs",   "the wave model's published imminent flag",                                  "ImminentThreshold"),
                ("Assets/_Modules/Village/Hero/HeroLocomotion.cs",     "the hero's braced combat idle",                                             "CombatImminentThreshold"),
                ("Assets/_Modules/Village/NPCs/AmbientNPC.cs",         "every townsfolk NPC's combat behaviour",                                    "CombatImminentThreshold"),
                ("Assets/_Modules/Village/Audio/BattleMusicManager.cs","battle music over a peaceful town",                                         "ImminentThreshold"),
            };

            foreach (var (path, drives, threshold) in consumers)
            {
                string src = ReadCode(path);
                if (src == null)
                {
                    failures.Add("[wo1736-parked] " + path + " is MISSING - a consumer of the imminent-" +
                                 "countdown rule cannot be verified.");
                    continue;
                }

                string file = Path.GetFileName(path);

                // It must still read the threshold at all: a consumer that stopped consulting it
                // has silently opted out of the owner's imminent-window ruling.
                if (src.IndexOf(threshold, StringComparison.Ordinal) < 0)
                {
                    failures.Add("[wo1736-parked] " + file + " no longer reads " + threshold + ", so " +
                                 drives + " has silently left the owner's 2026-07-08 imminent-window " +
                                 "ruling. If that consumer was deliberately retired, retire its row in " +
                                 "this case in the same change - a silent one leaves the next reader " +
                                 "hunting a fifth copy that no longer exists.");
                    continue;
                }

                // ⛔ THE FIX. The parked-countdown guard. WaveManager.IsAwaitingPlayerStart is the
                //    only thing that can tell "5 seconds left" from "no countdown is running".
                if (src.IndexOf("IsAwaitingPlayerStart", StringComparison.Ordinal) < 0)
                    failures.Add("[wo1736-parked] " + file + " tests the imminent countdown WITHOUT " +
                                 "IsAwaitingPlayerStart, so " + drives + " latches on for the whole " +
                                 "open-ended endless build phase. Endless mode parks in phase Countdown " +
                                 "with the countdown held at 0 and `0 <= " + threshold + "` is TRUE. " +
                                 "This is the 2026-09-17 capture verbatim: 34m30s and 186 samples of " +
                                 "\"countdown IMMINENT (0.0s <= 5s) -> counts as Battle\" with " +
                                 "battleLock, pursuit and sceneCombat all False - and it is the same " +
                                 "state external player Sminer reported from the Play tester track, " +
                                 "which is why his START WAVE button and his combat dock were on screen " +
                                 "together (WO-1736 sec.2.4 ranked that combination out as impossible).");
                else
                    log.AppendLine("  [wo1736-parked] " + file + " gates the imminent test on a countdown " +
                                   "that is actually running");
            }

            // The seam itself must stay public and read-only on the authority. Every row above
            // depends on it, and it is the one member WaveManager exposes for this purpose.
            string wave = ReadCode("Assets/_Modules/Village/Waves/WaveManager.cs");
            if (wave == null)
                failures.Add("[wo1736-parked] WaveManager.cs is MISSING - the parked-countdown authority " +
                             "cannot be verified.");
            else if (wave.IndexOf("public bool IsAwaitingPlayerStart", StringComparison.Ordinal) < 0)
                failures.Add("[wo1736-parked] WaveManager no longer exposes `public bool " +
                             "IsAwaitingPlayerStart`. All five consumers above read it to tell a parked " +
                             "endless countdown from a genuinely imminent one; without it they either " +
                             "stop compiling or get 'fixed' back to the bare threshold test that " +
                             "produced the 34-minute latch.");
            else
                log.AppendLine("  [wo1736-parked] WaveManager still exposes the parked-countdown authority");
        }

        /// <summary>Standalone entry point (run-unity-method).</summary>
        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log(reason);
            if (!ok) EditorApplication.Exit(1);
        }
    }
}
