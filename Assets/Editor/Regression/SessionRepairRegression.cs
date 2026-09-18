#if UNITY_EDITOR
// =============================================================================
// SessionRepairRegression — WO-1856. Pins the always-reachable safety-net
// Settings door (Part A) and the client-side "repair my session" action
// (Part B) against exactly the class of bug that minted this ticket:
// HudContextEvaluator wrongly reporting Battle while battleLock/pursuit/modal
// are all false (the captured WO-1855 shape).
// -----------------------------------------------------------------------------
// Standalone: run-unity-method -Method DeNelle.Editor.Regression.SessionRepairRegression.RunAll
// Registered in DataRegression.RunAll as the "session-repair suite".
//
// ⚠ DEVIATION FROM THE WO'S ACCEPTANCE CRITERION #3, SURFACED FOR LEAD RULING
// (CLAUDE.md §11B.B — never silently reinterpret a written acceptance line):
// the WO text reads "run while BattleLock is artificially held by a test
// holder, releases it." BattleLock exposes NO force-unregister/override API,
// by deliberate design: BattleSessionEnd.cs's own header states it "does NOT
// force BattleLock false", and BattleQuiescenceRegression.LiveChaseIsNotSuppressed
// (Assets/Editor/Regression/BattleQuiescenceRegression.cs:441-459) PINS that a
// still-chasing pursuer re-raises the lock on its next aggro tick — i.e. this
// codebase already regression-enforces that nothing may force the lock false
// out from under a live probe. An arbitrary `() => true` test probe therefore
// CANNOT be "released" by anything short of unregistering it, which would be
// destructive in production (real battle owners register once at boot and
// never re-register). RepairClearsPursuitStuckLock below proves the case the
// repair action ACTUALLY targets and CAN safely clear: the real captured shape
// (WO-1855/WO-1233/WO-1603) where the lock is held by a STALE pursuit pulse,
// not a live battle owner. DoorSurvivesForcedBattleLock separately proves the
// Part A door does not care what BattleLock reports at all. If the lead wants
// the literal "any test holder" case, that requires either a new force-clear
// API on BattleLock (out of this ticket's silo — BattleLock.cs is explicitly
// off-limits per the WO-1856 dispatch) or a ruling that the WO's own wording
// is superseded by BattleSessionEnd's "never force false" law.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Combat;
using DeNelle.Core.HudModel;
using DeNelle.Core.UI;
using DeNelle.HUD.Kit;

namespace DeNelle.Editor.Regression
{
    public static class SessionRepairRegression
    {
        private const string HudKitSrc = "Assets/_Modules/HUD/Kit/HudKitController.cs";
        private const string RepairSrc = "Assets/_Modules/Core/UI/SessionRepairService.cs";

        public static bool Run(out string result)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            try
            {
                DoorBuiltUnconditionallyFirst(failures, log);
                DoorYieldsOnlyToLiveDialogue(failures, log);
                DoorSurvivesForcedBattleLock(failures, log);
                RepairNeverTouchesPersistence(failures, log);
                RepairClearsPursuitStuckLock(failures, log);
                RepairClosesStuckPanel(failures, log);
                RepairDoesNotSuppressLiveChase(failures, log);
            }
            catch (Exception ex)
            {
                // A thrown setup/assert (a missing file, a renamed signature) is itself a FAIL —
                // never a silently-skipped suite (CLAUDE.md §12 "no silent failures").
                result = "SESSION_REPAIR_SUITE_FAIL (threw): " + ex.Message;
                return false;
            }

            if (failures.Count > 0)
            {
                result = "SESSION_REPAIR_SUITE_FAIL (" + failures.Count + "): " + string.Join(" | ", failures);
                return false;
            }
            result = "WO-1856: safety-net Settings door is unconditional/context-blind and yields only " +
                     "to a live dialogue; the repair action clears a stale pursuit-held battle-lock and a " +
                     "stuck panel without suppressing a real fight, and never references persistence.\n" + log;
            return true;
        }

        // =====================================================================
        //  PART A — the door itself
        // =====================================================================

        /// <summary>Source-lint: BuildSafetyNetSettingsDoor is the FIRST statement of
        /// BuildWidgets(), in its own Guard.Try, before `Transform pool = transform` and before
        /// any dock builder call — so a later throw in BuildWidgets() cannot prevent it (Guard.Try
        /// does not roll back GameObjects already constructed; VillageHudController.cs:109 wraps
        /// the WHOLE BuildWidgets() call in an outer Guard.Try that would otherwise silently eat
        /// the door along with everything else).</summary>
        private static void DoorBuiltUnconditionallyFirst(List<string> failures, StringBuilder log)
        {
            string src = File.ReadAllText(HudKitSrc);
            string method = SliceMethod(src, "private void BuildWidgets()", out int methodStart);

            int guardCall = method.IndexOf(
                "Guard.Try(\"Settings\", \"build the always-reachable safety-net Settings door\"",
                StringComparison.Ordinal);
            if (guardCall < 0)
            {
                failures.Add("[door-first] BuildWidgets() no longer opens with the WO-1856 safety-net " +
                             "Guard.Try call — the safety net can silently stop being built.");
                return;
            }

            int poolLine = method.IndexOf("Transform pool = transform;", StringComparison.Ordinal);
            if (poolLine >= 0 && guardCall > poolLine)
                failures.Add("[door-first] the safety-net door build no longer runs BEFORE " +
                             "'Transform pool = transform;' — it must be the opening statement, not one " +
                             "reached after other setup that could throw first.");

            int firstDockCall = FirstOf(method,
                "BuildAdaptivePeacefulDock(pool)", "BuildAdaptiveCombatDock(pool)", "BuildAdaptiveOutsideDock(pool)");
            if (firstDockCall >= 0 && guardCall > firstDockCall)
                failures.Add("[door-first] the safety-net door build runs AFTER a dock builder — exactly " +
                             "the ordering that would let a broken dock builder's throw prevent the door " +
                             "(the WO-1856 captured failure mode) from ever being built.");

            if (guardCall >= 0 && (poolLine < 0 || guardCall < poolLine) && (firstDockCall < 0 || guardCall < firstDockCall))
                log.AppendLine("  [door-first] BuildSafetyNetSettingsDoor runs first, in its own Guard.Try, " +
                                "before pool setup and before every dock builder.");

            // The builder body itself must not read any context/battle/panel signal — it must be
            // able to run correctly no matter what those report, which is the whole point.
            string builder = SliceMethod(src, "private void BuildSafetyNetSettingsDoor()", out _);
            string[] forbidden = { "HudContextEvaluator", "BattleLock", "PanelManager.AnyOpen",
                                    "_evaluator.Posture", "ApplyPosture(" };
            foreach (var token in forbidden)
            {
                if (builder.IndexOf(token, StringComparison.Ordinal) >= 0)
                    failures.Add("[door-first] BuildSafetyNetSettingsDoor() reads '" + token + "' — it must " +
                                 "be unconditional and context-blind, exactly like the bug class it is the " +
                                 "safety net for.");
            }
        }

        /// <summary>Source-lint: the door's ONE yield reads DialogueGateState.PanelVisible only —
        /// never PanelManager.AnyOpen (that would recreate the lockout Part B exists to fix) and
        /// never a truce flag alone (a HIDDEN dialogue has nothing on screen to fight, per the
        /// WO-1714 HiddenByTruce contract).</summary>
        private static void DoorYieldsOnlyToLiveDialogue(List<string> failures, StringBuilder log)
        {
            string src = File.ReadAllText(HudKitSrc);
            string tick = SliceMethod(src, "private void TickSafetyGearDialogueYield()", out _);

            if (tick.IndexOf("DialogueGateState.PanelVisible", StringComparison.Ordinal) < 0)
            {
                failures.Add("[door-yield] TickSafetyGearDialogueYield no longer reads " +
                             "DialogueGateState.PanelVisible — the door's yield condition is gone.");
                return;
            }
            if (tick.IndexOf("PanelManager.AnyOpen", StringComparison.Ordinal) >= 0)
                failures.Add("[door-yield] the yield now also reads PanelManager.AnyOpen — that would hide " +
                             "the door behind exactly the kind of stuck panel WO-1856 Part B exists to clear, " +
                             "recreating the lockout under a different name.");
            log.AppendLine("  [door-yield] yields only to DialogueGateState.PanelVisible (a genuinely " +
                            "on-screen dialogue), not to any panel/context/battle signal.");
        }

        /// <summary>Live build: constructs ONLY the safety-net door (no HudAreasHost, no
        /// PostureEvaluator — the same lightweight probe shape HudActionBarRegression uses for the
        /// docks) while BattleLock is forced to report an active battle by a test probe, reproducing
        /// the WO-1855/WO-1736 shape (a misclassified Battle context) at the Core layer this Editor
        /// assembly can reach (DeNelle.Editor cannot see DeNelle.Village's HudContextEvaluator
        /// directly, so BattleLock is the correct, general stand-in: it is one of the three
        /// signals — battleLock/pursuit/modal — the captured incident named, and the door
        /// consults NONE of them, so forcing any one of them true and proving the door still
        /// builds is sufficient proof it survives the whole class).</summary>
        private static void DoorSurvivesForcedBattleLock(List<string> failures, StringBuilder log)
        {
            GameObject probeGo = null;
            Func<bool> stuckBattle = () => true;
            BattleLock.RegisterProbe(stuckBattle);
            try
            {
                if (!BattleLock.IsInBattle())
                {
                    failures.Add("[door-survives-battle] setup failed - BattleLock.IsInBattle() reads " +
                                 "false with a forced-true probe registered.");
                    return;
                }

                probeGo = new GameObject("WO1856_SafetyDoorProbe", typeof(RectTransform));
                var kit = probeGo.AddComponent<HudKitController>();
                GameObject door = kit.BuildSafetyNetSettingsDoorProbe();

                if (door == null)
                {
                    failures.Add("[door-survives-battle] BuildSafetyNetSettingsDoorProbe built NO door " +
                                 "while BattleLock reported an active battle — exactly the WO-1856 lockout.");
                    return;
                }
                if (!door.activeInHierarchy)
                    failures.Add("[door-survives-battle] the door GameObject exists but is not active.");
                var canvas = door.GetComponent<Canvas>();
                if (canvas == null)
                    failures.Add("[door-survives-battle] the door has no Canvas component.");

                var button = kit.SafetyGearButtonProbe;
                if (button == null)
                {
                    failures.Add("[door-survives-battle] the door built no tappable Button.");
                    return;
                }
                if (!button.interactable || !button.gameObject.activeInHierarchy)
                    failures.Add("[door-survives-battle] the door's button is present but not tappable " +
                                 "(interactable=" + button.interactable + ", active=" +
                                 button.gameObject.activeInHierarchy + ") while BattleLock reports an " +
                                 "active battle.");
                else
                    log.AppendLine("  [door-survives-battle] the safety-net door built and stayed tappable " +
                                    "with BattleLock.IsInBattle()=true (the WO-1855/1736 class of bug).");
            }
            finally
            {
                BattleLock.UnregisterProbe(stuckBattle);
                if (probeGo != null) UnityEngine.Object.DestroyImmediate(probeGo);
            }
        }

        // =====================================================================
        //  PART B — the repair action
        // =====================================================================

        /// <summary>Source-lint (code only, comments stripped — the header above deliberately
        /// NAMES GameStateService/PersistenceBridge as documentation of what this file must never
        /// call, which is exactly why the comment lines are stripped first, mirroring
        /// PublicNavigationRetirementRegression.AssertAbsentInCode).</summary>
        private static void RepairNeverTouchesPersistence(List<string> failures, StringBuilder log)
        {
            if (!File.Exists(RepairSrc))
            {
                failures.Add("[repair-no-persistence] " + RepairSrc + " is missing.");
                return;
            }
            string code = StripLineComments(File.ReadAllText(RepairSrc));
            string[] forbidden = { "GameStateService", "PersistenceBridge", ".Save(", "SyncToBackend" };
            foreach (var token in forbidden)
            {
                if (code.IndexOf(token, StringComparison.Ordinal) >= 0)
                    failures.Add("[repair-no-persistence] " + RepairSrc + " names '" + token + "' in CODE " +
                                 "(not a comment) — the repair action must never reach the save file or the " +
                                 "backend (WO-1856 Part B non-scope).");
            }
            if (failures.Count == 0)
                log.AppendLine("  [repair-no-persistence] SessionRepairService.cs's code names no " +
                                "persistence/backend type or method.");
        }

        /// <summary>Behavioural: reproduces the REAL captured shape (WO-1855/1233/1603 — a pursuit
        /// pulse the battle opened, never closed) and proves SessionRepairService.Repair() clears
        /// it through BattleSessionEnd.Release, the identical call BattleQuiescenceGate's own
        /// self-heal makes. Mirrors BattleQuiescenceRegression.SessionEndReleasesTheLock's shape.</summary>
        private static void RepairClearsPursuitStuckLock(List<string> failures, StringBuilder log)
        {
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                PostureSignals.ReportPursuit(185600);
                if (!BattleLock.IsInBattle())
                {
                    failures.Add("[repair-pursuit] setup failed - a live pursuit pulse did not raise " +
                                 "BattleLock.IsInBattle(); PursuitBattleProbe's contract may have changed.");
                    return;
                }

                var result = SessionRepairService.Repair();

                if (BattleLock.IsInBattle())
                    failures.Add("[repair-pursuit] the stale pursuit pulse survived the repair - " +
                                 "BattleLock.IsInBattle() still reads true after Repair().");
                else if (!result.BattleLockCleared)
                    failures.Add("[repair-pursuit] the lock cleared but SessionRepairResult.BattleLockCleared " +
                                 "is false - the confirmation copy would under-report what happened.");
                else
                    log.AppendLine("  [repair-pursuit] Repair() cleared a stale pursuit-held battle-lock and " +
                                    "reported it (Summarize: \"" + result.Summarize() + "\").");
            }
            finally
            {
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
            }
        }

        /// <summary>Behavioural: PanelManager reports a panel open; Repair() force-closes it through
        /// the panel's OWN close action (never by zeroing the record blind), exactly the WO-1337
        /// heal discipline this codebase already established.</summary>
        private static void RepairClosesStuckPanel(List<string> failures, StringBuilder log)
        {
            bool closed = false;
            var handle = PanelManager.RegisterBattleAllowed(
                "wo1856-suite-stuck-panel", () => closed = true, () => true);
            PanelManager.NotifyOpened(handle);
            try
            {
                if (!PanelManager.AnyOpen)
                {
                    failures.Add("[repair-panel] setup failed - NotifyOpened did not record the handle as open.");
                    return;
                }

                var result = SessionRepairService.Repair();

                if (PanelManager.AnyOpen)
                    failures.Add("[repair-panel] a panel PanelManager reported open survived Repair().");
                else if (!closed)
                    failures.Add("[repair-panel] the record cleared but the panel's OWN Close action never " +
                                 "ran - Repair() zeroed the arbiter instead of closing the panel properly.");
                else if (result.ClosedPanelName != "wo1856-suite-stuck-panel")
                    failures.Add("[repair-panel] the panel closed but SessionRepairResult.ClosedPanelName " +
                                 "did not name it (\"" + result.ClosedPanelName + "\") - the confirmation " +
                                 "copy would under-report what happened.");
                else
                    log.AppendLine("  [repair-panel] Repair() closed the stuck panel through its own Close " +
                                    "action and named it in the result.");
            }
            finally
            {
                PanelManager.CloseAll();
            }
        }

        /// <summary>The law this repair tool must never break (BattleSessionEnd.cs's own header,
        /// and BattleQuiescenceRegression.LiveChaseIsNotSuppressed): a chaser that is STILL chasing
        /// re-raises the lock on its very next aggro tick. If Repair() ever became a suppression
        /// instead of a clear, this is the case that would catch it.</summary>
        private static void RepairDoesNotSuppressLiveChase(List<string> failures, StringBuilder log)
        {
            Func<bool> pursuitProbe = () => PostureSignals.PursuitActive;
            PostureSignals.ClearPursuits();
            BattleLock.RegisterProbe(pursuitProbe);
            try
            {
                SessionRepairService.Repair();
                PostureSignals.ReportPursuit(185601);   // a chaser is STILL chasing, right now

                if (!BattleLock.IsInBattle())
                    failures.Add("[repair-live-chase] a live pursuer re-reported immediately AFTER Repair() " +
                                 "and the lock did NOT come back - the repair has become a suppression, which " +
                                 "would unblock town panels during a real chase.");
                else
                    log.AppendLine("  [repair-live-chase] a still-chasing pursuer correctly re-raises the " +
                                    "lock right after a repair - the tool clears stale state only, never a " +
                                    "real fight.");
            }
            finally
            {
                BattleLock.UnregisterProbe(pursuitProbe);
                PostureSignals.ClearPursuits();
            }
        }

        // =====================================================================
        //  Shared source-text helpers (mirrors PublicNavigationRetirementRegression)
        // =====================================================================

        private static int FirstOf(string haystack, params string[] needles)
        {
            int best = -1;
            foreach (var n in needles)
            {
                int at = haystack.IndexOf(n, StringComparison.Ordinal);
                if (at >= 0 && (best < 0 || at < best)) best = at;
            }
            return best;
        }

        /// <summary>The text of a method from its signature line to the next `private`/`public`
        /// member declaration at the same indent, or end of file. Good enough for the well-formed
        /// methods this suite reads (same tolerance as SliceJourneyCase's sibling below).</summary>
        private static string SliceMethod(string src, string signature, out int start)
        {
            int a = src.IndexOf(signature, StringComparison.Ordinal);
            if (a < 0)
                throw new InvalidOperationException("signature not found: " + signature);
            start = a;
            int bodyStart = src.IndexOf('{', a);
            if (bodyStart < 0) return src.Substring(a);
            int depth = 0;
            int i = bodyStart;
            for (; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }
            return src.Substring(a, i - a);
        }

        /// <summary>Everything after a `//` on each line is dropped — identical tolerance to
        /// PublicNavigationRetirementRegression.StripLineComments.</summary>
        private static string StripLineComments(string source)
        {
            var sb = new StringBuilder(source.Length);
            foreach (string raw in source.Split('\n'))
            {
                string line = raw;
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                if (slash >= 0) line = line.Substring(0, slash);
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }
    }
}
#endif
