// =============================================================================
// CaptureStrandExitRegression — WO-1778: a 3-star capture can never strand the
// player on the victory screen. Marker: CAPTURE_STRAND_EXIT_OK.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Core).
// Wired into DeNelle.Editor.DataRegression.RunAll with ONE line.
//
// -----------------------------------------------------------------------------
//  WHY IT EXISTS — PROVEN FROM CAPTURE, NOT REASONED FROM SOURCE (CLAUDE.md §11B/§12)
// -----------------------------------------------------------------------------
// The precondition fired in the owner's own 2026-09-16 device run
// (Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt):
//
//   [Flow:Raid] Precombat capture census failed: Captured structure lacks a baked
//               stable identity: Wall_Outer_SS_3
//
// She settled at 2 stars, so capture was never attempted and the strand was never
// reached. A 3-star clear on that same build would have hit it:
//
//   1. RaidVictoryController.Start swallows the census throw -> _captureCensus == null
//   2. TryCommitCapturedTown then refuses FOREVER ("precombat census is missing")
//   3. CanEnterCapturedTown toasted ownedTown.captureRetry and returned false
//   4. ReturnHome returned early on that false — all three "independent" routes home
//      funnel through it, so they were three copies of ONE refusal
//   5. EndStateView.FirePrimary returned before Destroy(gameObject), so the
//      anti-softlock guard fired once into the same refusal and could not clear the
//      screen either
//
// And the escape hatch the source promised — RetryCaptureAfterDismissal — had ZERO
// callers; _waitingForCapture was never set true.
//
// -----------------------------------------------------------------------------
//  THE RED-FIRST DISCRIMINATOR (Case A), hand-evaluated before the fix was written
// -----------------------------------------------------------------------------
// A REAL RaidVictoryController component, _captureRequired = true, _captureCensus =
// null, _victoryStars = 3 — i.e. a 3-star final victory whose census is missing — and
// CanEnterCapturedTown() invoked twice:
//   OLD body: false, false   (no bound, no other exit — the strand)
//   NEW body: false, TRUE    (one real retry, then the forced castle route)
// Case A asserts the second call returns true AND that it cleared _captureRequired, so
// ReturnHome's own branch routes to GoCastle. Against the pre-fix tree that assertion
// FAILS. This is a behavioural probe of the shipping type, not a source-text lint.
//
// -----------------------------------------------------------------------------
//  WHAT THIS SUITE DOES NOT PROVE — an unproven thing named as unproven is useful,
//  an unproven thing stated as fact costs someone a day (CLAUDE.md §11B)
// -----------------------------------------------------------------------------
//  * That a real 3-star Bastion clear now lands in Main_Castle_Overworld. EditMode
//    cannot load a scene mid-suite, so Case A proves the DECISION (gate true +
//    _captureRequired cleared) and Case B proves ReturnHome has exactly one branch on
//    it. The device capture is WO-1778 acceptance item 2, an owner felt-verify.
//  * That the census identity defect itself is fixed — that is WO-1767's lane. This
//    suite deliberately holds the census NULL, because the promise under test is
//    "whatever refuses, the player still gets out".
//  * That the toast is legible on the Seeker. Copy/layout is the WO-1783 lane.
// =============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Behavioural probe (Case A) + source oracles (Cases B-F) for WO-1778's one law:
    /// every route off the victory screen, and off the owned-town surfaces beside it,
    /// ends somewhere. DataRegression-shaped: true = pass with a one-line summary,
    /// false = fail with the offending detail. NEVER throws.
    /// </summary>
    public static class CaptureStrandExitRegression
    {
        // Relative to Application.dataPath.
        private const string VictoryRel = "_Modules/Village/World/Camps/RaidVictoryController.cs";
        private const string CensusRel  = "_Modules/Village/World/Camps/RaidCaptureCensus.cs";
        private const string PanelRel   = "_Modules/Village/World/Camps/OwnedTownPanel.cs";
        private const string RouterRel  = "_Modules/Core/SceneRouter.cs";
        private const string ProgRel    = "_Modules/Core/State/OwnedBaseProgression.cs";
        private const string EndStateRel = "_Modules/Village/UI/EndState/EndStateView.cs";

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = "capture-strand-exit suite THREW: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            var notes = new StringBuilder();

            // ---- CASE A — the behavioural probe (the red-first discriminator) ------
            if (!CaseA_BoundedGateForcesTheRoute(out string caseA)) { reason = caseA; return false; }
            notes.Append(caseA);

            // ---- CASE B — ReturnHome still owns exactly one branch on the flag -----
            string victory = ReadSource(VictoryRel);
            if (victory == null) { reason = "capture-strand-exit: could not read " + VictoryRel; return false; }

            if (!victory.Contains("if (_captureRequired) SceneRouter.GoOwnedTown();") ||
                !victory.Contains("else SceneRouter.GoCastle();"))
            {
                reason = "capture-strand-exit CASE B: ReturnHome no longer routes on _captureRequired " +
                         "(GoOwnedTown / GoCastle). Case A's proof — the gate clears the flag — only " +
                         "reaches the castle through that branch.";
                return false;
            }
            notes.Append("; B ReturnHome branches GoOwnedTown/GoCastle on _captureRequired");

            // ---- CASE C — the dead retry is GONE from the CODE, not merely unreferenced ----
            // `victory` is comment-stripped (see ReadSource), so the RCA paragraph in
            // RaidVictoryController's own doc comment — which NAMES both of these to record why
            // they were deleted — cannot trip this. That false FAIL is exactly what this suite
            // reported on its first combined-tree run, and "fixing" it by deleting the explanation
            // would have destroyed the canon record instead of the dormant code.
            if (victory.Contains("RetryCaptureAfterDismissal") || victory.Contains("_waitingForCapture"))
            {
                reason = "capture-strand-exit CASE C: RetryCaptureAfterDismissal / _waitingForCapture is " +
                         "back in RaidVictoryController AS CODE (this match is comment-stripped, so it is " +
                         "not the doc comment that names them). It had ZERO callers and was never set true — " +
                         "a promised retry that does not run is what made three routes home one refusal. " +
                         "Wire it — _waitingForCapture actually set true on a live path and the coroutine " +
                         "actually started — or leave it deleted. Never re-add it dormant.";
                return false;
            }
            notes.Append("; C dead retry + _waitingForCapture stay deleted");

            // ---- CASE D — a refused gate can still be dismissed by the guard -------
            string endState = ReadSource(EndStateRel);
            if (endState == null) { reason = "capture-strand-exit: could not read " + EndStateRel; return false; }

            if (!endState.Contains("private void FirePrimary(bool forceDismissIfGateRefuses)") ||
                !endState.Contains("FirePrimary(forceDismissIfGateRefuses: last)") ||
                !endState.Contains("GateRefusalWindowsBeforeForcedDismiss"))
            {
                reason = "capture-strand-exit CASE D: EndStateView's anti-softlock guard no longer forces a " +
                         "dismissal past a refusing primary gate. FirePrimary returning before " +
                         "Destroy(gameObject) on a refusal is exactly what left a victory screen whose " +
                         "only CTA was a no-op.";
                return false;
            }
            notes.Append("; D guard forces dismissal past a refusing gate");

            // ---- CASE E — the owned-town surfaces refuse OUT LOUD, with a route ----
            string panel = ReadSource(PanelRel);
            string router = ReadSource(RouterRel);
            if (panel == null || router == null)
            { reason = "capture-strand-exit: could not read " + PanelRel + " / " + RouterRel; return false; }

            int busy = panel.IndexOf("OwnedTownDesignService.IsBusy || OwnedTownConstructionService.IsBusy", StringComparison.Ordinal);
            int castle = panel.IndexOf("Add(column, \"castle\", SceneRouter.GoCastle)", StringComparison.Ordinal);
            if (busy < 0 || castle < 0 || castle < busy)
            {
                reason = "capture-strand-exit CASE E1: OwnedTownPanel.Show must build the castle route INSIDE " +
                         "or BEFORE the busy early-out (the panel is built withClose:false, so a busy save " +
                         "otherwise leaves the player with no exit at all).";
                return false;
            }

            int refusal = router.IndexOf("Personal town entry refused", StringComparison.Ordinal);
            int toast = router.IndexOf("ElarionUiKit.ShowToast", StringComparison.Ordinal);
            if (refusal < 0 || toast < 0 || toast < refusal || toast - refusal > 600)
            {
                reason = "capture-strand-exit CASE E2: SceneRouter.GoOwnedTown refuses without surfacing a " +
                         "toast. A refusal the PLAYER triggered (the practice panel's single button) that " +
                         "only writes a FlowTrace.Warn is a button that does nothing, with no feedback.";
                return false;
            }
            notes.Append("; E busy panel keeps its castle route + GoOwnedTown toasts its refusal");

            // ---- CASE F — the two silent contracts now speak (WO-1778 §4.5) --------
            string census = ReadSource(CensusRel);
            string prog = ReadSource(ProgRel);
            if (census == null || prog == null)
            { reason = "capture-strand-exit: could not read " + CensusRel + " / " + ProgRel; return false; }

            int censusTraces = Count(census, "FlowTrace.");
            int progTraces = Count(prog, "FlowTrace.");
            if (censusTraces == 0 || progTraces == 0)
            {
                reason = $"capture-strand-exit CASE F: instrumentation regressed to silence — " +
                         $"RaidCaptureCensus has {censusTraces} FlowTrace call(s), OwnedBaseProgression has " +
                         $"{progTraces}. Both carried ZERO before WO-1778, which is why the contract's own " +
                         "refusal reasons were invisible except where a caller happened to echo them. " +
                         "NEVER STRIP FLOWTRACE (CLAUDE.md §12).";
                return false;
            }
            if (!census.Contains("TryParkForLaterClaim"))
            {
                reason = "capture-strand-exit CASE F2: RaidCaptureCensus.TryParkForLaterClaim is gone. The " +
                         "forced exit defers the town by parking its receipt; without it a refusal destroys " +
                         "the capture instead of postponing it.";
                return false;
            }
            notes.Append($"; F census={censusTraces} progression={progTraces} FlowTrace call(s), receipt park present");

            reason = "CAPTURE_STRAND_EXIT_OK " + notes.ToString().TrimStart(';', ' ');
            return true;
        }

        /// <summary>
        /// CASE A — the real component, the real method, the real refusal. Builds a
        /// RaidVictoryController on a throwaway GameObject, sets the exact state the owner's run
        /// produced (3-star final victory, capture required, census NULL) and asks the gate twice.
        ///
        /// <para>The FlowTrace categories are muted for the duration: the forced-exit line is a
        /// deliberate <c>Fail</c> (LogError) so it reaches break-log.jsonl on a device, and an
        /// editor suite must not print an error it is asserting the PRESENCE of. Restored with
        /// <c>AllOn</c> in the finally — never left muted.</para>
        /// </summary>
        private static bool CaseA_BoundedGateForcesTheRoute(out string note)
        {
            note = null;
            GameObject host = null;
            try
            {
                DeNelle.Core.Diagnostics.FlowTrace.Mute("Raid", "OwnedBase");

                host = new GameObject("WO1778_CaptureStrandProbe");
                host.hideFlags = HideFlags.HideAndDontSave;
                var ctrl = host.AddComponent<RaidVictoryController>();
                var type = typeof(RaidVictoryController);

                var requiredField = type.GetField("_captureRequired", Hidden);
                var censusField = type.GetField("_captureCensus", Hidden);
                var starsField = type.GetField("_victoryStars", Hidden);
                var gate = type.GetMethod("CanEnterCapturedTown", Hidden);
                var boundField = type.GetField("CaptureRefusalsBeforeForcedExit",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                if (requiredField == null || censusField == null || starsField == null || gate == null || boundField == null)
                {
                    note = "capture-strand-exit CASE A: RaidVictoryController's capture seam was renamed — " +
                           "expected _captureRequired / _captureCensus / _victoryStars / CanEnterCapturedTown / " +
                           "CaptureRefusalsBeforeForcedExit. The probe cannot prove the exit, so it fails rather " +
                           "than passing blind.";
                    return false;
                }

                int bound = (int)boundField.GetValue(null);
                if (bound < 1)
                {
                    note = $"capture-strand-exit CASE A: CaptureRefusalsBeforeForcedExit={bound}. A bound below 1 " +
                           "means the player is never offered even one honest retry.";
                    return false;
                }

                requiredField.SetValue(ctrl, true);
                censusField.SetValue(ctrl, null);      // the owner's run: the census throw was swallowed
                starsField.SetValue(ctrl, 3);         // a 3-star final clear — capture IS owed

                // The refusals BEFORE the bound must hold the screen up (a real retry).
                for (int call = 1; call < bound; call++)
                {
                    if ((bool)gate.Invoke(ctrl, null))
                    {
                        note = $"capture-strand-exit CASE A: the gate PASSED on refusal {call}/{bound} with a null " +
                               "census — a capture that cannot commit must not report success, or the player " +
                               "lands in an owned town that was never saved.";
                        return false;
                    }
                    if (!(bool)requiredField.GetValue(ctrl))
                    {
                        note = $"capture-strand-exit CASE A: _captureRequired was cleared on refusal {call}/{bound}, " +
                               "before the bound. The first refusals are meant to be real retries.";
                        return false;
                    }
                }

                // The refusal AT the bound must FORCE the route home.
                if (!(bool)gate.Invoke(ctrl, null))
                {
                    note = $"capture-strand-exit CASE A (THE DEFECT): refusal {bound}/{bound} still returned false, " +
                           "so ReturnHome returns early and every route off the victory screen refuses forever. " +
                           "This is the pre-WO-1778 behaviour and the exact strand the owner's 2026-09-16 run was " +
                           "one star away from hitting.";
                    return false;
                }
                if ((bool)requiredField.GetValue(ctrl))
                {
                    note = "capture-strand-exit CASE A: the gate passed but _captureRequired is still true, so " +
                           "ReturnHome would route to GoOwnedTown — a town that was never committed. The forced " +
                           "exit must clear the flag so the route is the CASTLE.";
                    return false;
                }

                note = $"A gate refused {bound - 1}x then FORCED the castle route (census null, 3 stars, " +
                       $"_captureRequired cleared)";
                return true;
            }
            finally
            {
                DeNelle.Core.Diagnostics.FlowTrace.AllOn();
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Read one source file as CODE — comments stripped, string literals preserved.
        ///
        /// <para>⛔ WHY STRIPPED, and it bit THIS suite on its first combined-tree run. Case C
        /// asserts <c>RetryCaptureAfterDismissal</c> / <c>_waitingForCapture</c> are GONE, and the
        /// WO-1778 fix deliberately leaves an RCA paragraph in RaidVictoryController's own doc
        /// comment NAMING both of them (that paragraph is the canon record of why they were
        /// deleted, CLAUDE.md §15). Reading raw text, the suite matched its own prose and reported
        /// the dormant retry as "back" while the code was demonstrably absent — a false FAIL that
        /// would have been "fixed" by deleting the explanation. RaidWatchdogHonorRegression set
        /// this precedent for the same reason: "comment-stripped source in, so a quoted signature
        /// inside prose cannot be matched".</para>
        ///
        /// <para>String literals are KEPT — Case E matches <c>"Personal town entry refused"</c> and
        /// <c>Add(column, "castle", ...)</c>, which are literals in live code — and the scanner is
        /// literal-AWARE so a <c>//</c> inside a string (a URL) cannot swallow the rest of a line.
        /// That is the same trap CLAUDE.md §1 documents for the brace gate.</para>
        /// </summary>
        private static string ReadSource(string relative)
        {
            string path = Path.Combine(Application.dataPath, relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? StripComments(File.ReadAllText(path)) : null;
        }

        /// <summary>
        /// Comment stripper that walks string/char literals rather than pretending they are code.
        /// Blanks each comment to a newline so every offset comparison in this suite (Case E reads
        /// "is the castle route built BEFORE the busy early-out") keeps its ordering.
        /// </summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);

            for (int i = 0; i < src.Length; i++)
            {
                char ch = src[i];

                // ---- line comment ------------------------------------------------
                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }

                // ---- block comment -----------------------------------------------
                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    continue;
                }

                // ---- verbatim string: only "" escapes, newlines are legal ---------
                if (ch == '@' && i + 1 < src.Length && src[i + 1] == '"')
                {
                    sb.Append(src[i]).Append(src[i + 1]);
                    i += 2;
                    while (i < src.Length)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < src.Length && src[i + 1] == '"') { sb.Append('"').Append('"'); i += 2; continue; }
                            sb.Append('"');
                            break;
                        }
                        sb.Append(src[i]);
                        i++;
                    }
                    continue;
                }

                // ---- regular string / char literal: backslash escapes -------------
                if (ch == '"' || ch == '\'')
                {
                    char quote = ch;
                    sb.Append(ch);
                    i++;
                    while (i < src.Length && src[i] != quote)
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(src[i]).Append(src[i + 1]); i += 2; continue; }
                        if (src[i] == '\n') break;              // unterminated — do not run away
                        sb.Append(src[i]);
                        i++;
                    }
                    if (i < src.Length) sb.Append(src[i]);
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        private static int Count(string src, string needle)
        {
            int n = 0, i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
