// =============================================================================
// SessionRepairService — WO-1856 Part B. The client-side, in-memory "repair my
// session" action reachable through the WO-1856 Part A safety-net Settings door.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.UI
//
// THE PROBLEM THIS CLOSES (minted live during the WO-1855 investigation, owner
// verbatim: "i think this does show that we need a disection tool, if a plyer
// is stuck. THey will not want to wait for a build to be fixed."): whatever the
// NEXT HUD-context misclassification turns out to be (WO-1855's parked-countdown
// bug was one instance; there will be others), a stuck player needs a way to
// unstick the SCREEN without waiting for a patch.
//
// SCOPE, DELIBERATELY NARROW (WO-1856's own non-scope section):
//   - Clears the Core-level signal a stuck battle/HUD state hangs off: the
//     pursuit-pulse ring, via BattleSessionEnd.Release — the EXACT SAME call
//     BattleQuiescenceGate's own self-heal already makes (see BattleSessionEnd.
//     cs's header). It never forces BattleLock false and never unregisters a
//     battle probe: BattleSessionEnd.cs's own header states it "does NOT force
//     BattleLock false", and BattleQuiescenceRegression.LiveChaseIsNotSuppressed
//     (Assets/Editor/Regression/BattleQuiescenceRegression.cs:441-459) pins that
//     a still-chasing pursuer re-raises the lock on its very next aggro tick —
//     which is the correct, safe shape for an emergency repair tool: it can only
//     clear a STALE signal, never suppress a real one.
//   - Force-closes whatever panel PanelManager still believes is open.
//   - Does NOT re-derive HudContextEvaluator or force a dock rebuild directly —
//     it CANNOT: HudContextEvaluator is `internal sealed` in DeNelle.Village
//     (Assets/_Modules/Village/HUD/HudContextEvaluator.cs:69) and this file's
//     assembly (DeNelle.Core) cannot reference DeNelle.Village at all (CLAUDE.md
//     §5 — Village depends on Core, never the reverse). It doesn't need to:
//     HudContextEvaluator already polls every 0.20s (HudProducer base, pollInterval
//     0.20f) and PostureEvaluator (DeNelle.HUD) every 0.15s
//     (PostureEvaluator.PollInterval), and BOTH read the exact signals this
//     service clears (BattleLock.IsInBattle / PostureSignals.PursuitActive /
//     PanelManager.AnyOpen). So the next natural poll pair (<= 0.35s after this
//     call) re-derives Town/Overworld on its own and HudKitController.ApplyPosture
//     rebuilds the dock without this file doing anything HUD-specific at all.
//   - NEVER touches GameStateService, PersistenceBridge, or anything that reaches
//     the save file or the backend. Pinned by
//     Assets/Editor/Regression/SessionRepairRegression.cs (source-lint, the
//     PublicNavigationRetirementRegression pattern) AND true by construction:
//     this file's body below names no such type/method.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.UI
{
    /// <summary>Result of one <see cref="SessionRepairService.Repair"/> run — everything the
    /// WO-1856 confirmation copy needs, in plain data so the caller (SettingsController) never
    /// has to re-read BattleLock/PanelManager itself.</summary>
    public sealed class SessionRepairResult
    {
        /// <summary>BattleLock.DescribeHolders() before the repair ran.</summary>
        public string BattleLockHoldersBefore;
        /// <summary>BattleLock.DescribeHolders() after the repair ran.</summary>
        public string BattleLockHoldersAfter;
        /// <summary>True only when the lock was HELD before and reads clear after. A holder that
        /// survives (a genuinely live chase re-raising it) is correctly NOT counted as cleared —
        /// see the file header on why the repair must never suppress a real fight.</summary>
        public bool BattleLockCleared;
        /// <summary>The panel PanelManager reported open and this repair closed, or null.</summary>
        public string ClosedPanelName;

        /// <summary>True when the repair actually changed anything (the "nothing was stuck" copy
        /// branch below reads this).</summary>
        public bool AnythingCleared => BattleLockCleared || ClosedPanelName != null;

        /// <summary>
        /// The one-line player-facing result (WO-1856 copy rules: plain language, "stuck screen
        /// state"/"session", NEVER "save" — this tool never touches the save).
        /// </summary>
        public string Summarize()
        {
            if (!AnythingCleared)
                return "Nothing was stuck - if you're still seeing a problem, this isn't it.";
            var parts = new List<string>(2);
            if (BattleLockCleared) parts.Add("1 stuck battle lock");
            if (ClosedPanelName != null) parts.Add("1 stuck panel");
            var sb = new StringBuilder("Cleared: ");
            sb.Append(string.Join(", ", parts));
            sb.Append('.');
            return sb.ToString();
        }
    }

    /// <summary>
    /// WO-1856 Part B — the "repair my session" action behind the safety-net Settings door.
    /// Client-side, in-memory only (see file header for the exact, deliberately narrow scope).
    /// </summary>
    public static class SessionRepairService
    {
        private const string Sys = "Settings";

        /// <summary>Run the repair now. Safe to call repeatedly; a no-op run (nothing was
        /// actually stuck) is a normal, expected result, not a failure.</summary>
        public static SessionRepairResult Repair()
        {
            var result = new SessionRepairResult
            {
                BattleLockHoldersBefore = BattleLock.DescribeHolders()
            };

            // The SAME call BattleQuiescenceGate's own self-heal makes (see BattleSessionEnd.cs
            // header) — clears the pursuit-pulse ring and runs every registered session-end
            // unwind. Guarded: one faulty unwind must never abort the rest of the repair.
            Guard.Try(Sys, "session repair: release battle session (pursuit ring + registered unwinds)",
                () => BattleSessionEnd.Release("session repair"));

            result.BattleLockHoldersAfter = BattleLock.DescribeHolders();
            result.BattleLockCleared = result.BattleLockHoldersBefore != "none" &&
                                        result.BattleLockHoldersAfter == "none";

            string openPanel = PanelManager.OpenPanelName;
            if (openPanel != null)
            {
                Guard.Try(Sys, "session repair: close stuck panel '" + openPanel + "'", PanelManager.CloseOpen);
                result.ClosedPanelName = openPanel;
            }

            FlowTrace.Step(Sys,
                "session repair run - battle-lock before=[" + result.BattleLockHoldersBefore +
                "] after=[" + result.BattleLockHoldersAfter + "] (cleared=" + result.BattleLockCleared +
                "), panel closed=" + (result.ClosedPanelName ?? "<none>") + ". This call touches no " +
                "dock/context/save state directly - HudContextEvaluator/PostureEvaluator re-derive the " +
                "HUD context on their own next poll (<=0.35s combined) from the signals just cleared.");

            return result;
        }
    }
}
