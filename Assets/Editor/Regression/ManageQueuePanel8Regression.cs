// =============================================================================
// ManageQueuePanel8Regression [manage-queue-panel8]
// -----------------------------------------------------------------------------
// THE MODEL SIDE OF MOCKUP PANEL 8 (docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png,
// bottom-right, "8. QUEUE (OVERLAY) - Same for all tabs - shows active and queued
// items"). The mockup is the spec (WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md
// section 3.0c, owner ruling 2026-09-06: it is ABSOLUTE and no one asks about it).
//
// What panel 8 draws, read off the PNG this session, not off prose:
//   - THREE TABS across the top of the overlay: "BUILDERS (2/2)", "TRAINING (2/2)",
//     "RESEARCH (2/2)". Each carries a live slots-used / slots-total count.
//   - NUMBERED rows - "1." "2." "3." "4." "5." - each an icon, a target
//     ("Lumber Mill -> Level 3"), and on the ACTIVE row a progress bar with the
//     time remaining beside it ("18m 24s"). The pending rows read "Queued".
//   - A gold "SPEED UP" button on the ACTIVE row carrying a CRYSTAL price (25).
//   - An X to close, top-right.
//
// ⛔ THIS SUITE IS RED ON PURPOSE. It is the ORACLE for that work, written before
// the work, and it must FAIL against the tree as it stands. Measured 2026-09-06
// against HEAD of feat/synty-art-retheme (all four are greps, not inferences):
//
//   RED 1  There is no per-channel tab model at all. The drawer shows ONE channel,
//          chosen from the Manage tab the player happens to be on:
//            ManageScreenPanel.cs:2402  var channel = ManageScreenVM.ChannelOf(_vm.Tab);
//            ManageScreenPanel.cs:2406  AddSectionHeader("IN QUEUE - " + ...ToUpperInvariant());
//          A SECTION HEADER naming one channel is not three tabs, and no code path
//          lets the player reach another channel's queue from inside the overlay.
//   RED 2  `grep -n "ComposeQueueTabs\|BuildQueueTabs" ManageScreenVM.cs
//          ManageScreenPanel.cs` -> NO MATCHES. Nothing composes "(2/2)".
//          (The NUMBERS exist and are already correct - ChannelSummary.Busy /
//          ChannelSummary.Slots, ManageScreenVM.cs:78-81, filled by AddSummary for
//          all three channels at ManageScreenVM.cs:661-663. They are simply not
//          projected into a tab. This is a WIRING gap, not a data gap.)
//   RED 3  `grep -n "OrdinalText" ManageScreenVM.cs ManageScreenPanel.cs` -> NO
//          MATCHES. QueueRowVM (ManageScreenVM.cs:100-180) carries Label, StateText,
//          Progress01, FinishPrice ... and no row number. The mockup's "1." is the
//          reading order of the queue and it is the model's to state, not the view's
//          to count (canon 9: the View invents no copy).
//   RED 4  `grep -n "SPEED UP" ManageScreenVM.cs ManageScreenPanel.cs` -> NO MATCHES.
//          The verb today is composed at ManageScreenVM.cs:808 as
//            FinishVerbText = paysGold ? BuildTimerService.HireReinforcementsVerb : string.Empty,
//          and the empty string makes ManageScreenPanel.cs:4604 fall back to the
//          literal "Finish Now". So the crystal-priced row says "Finish Now" where
//          the mockup says "SPEED UP".
//
// ⭐ THE OTHER HALF OF THIS FILE IS A WALL, AND IT IS GREEN TODAY ON PURPOSE.
// The dangerous way to satisfy panel 8 is to build a SECOND way to spend crystals on
// a timer. This repo documents that duplicated-state failure four times over
// (CLAUDE.md 2 / 5 / 8 / 16), and the queue drawer has already been burned by it
// once (WO-1368: the verbs were "moved" and landed nowhere, and a green suite
// guaranteed they could not come back). So cases 5-7 pin the EXISTING path as the
// only path, and they are expected to PASS both before and after the work:
//   - SPEED UP is the existing Finish CTA wearing a different word. The button
//     invokes ManageScreenVM.FinishNow(channel, jobId) (ManageScreenVM.cs:2343),
//     which calls BuildTimerService.TryInstantFinish(channel, jobId, out failure)
//     (ManageScreenVM.cs:2354 -> BuildTimerService.cs:1337). That is the one paid
//     -finish verb in the game; ObsidianQueueHud.cs:410 calls the same one.
//   - The price is NOT invented here or anywhere. It is
//     svc.InstantFinishPrice(channel, job.StructureId) (ManageScreenVM.cs:766),
//     already carried to the view as QueueRowVM.FinishPrice and worded by
//     DescribeFinishCost into FinishCostText (ManageScreenVM.cs:805-807).
//   - The CURRENCY is the service's decision, asked of BuildTimerService
//     .FinishPaysGold(job.JobKind) (ManageScreenVM.cs:773). ⚠ A TrainTroop job is
//     priced in GOLD and wears the canon HIRE REINFORCEMENTS verb (WO-1372), so the
//     TRAINING tab's active row will NOT read "SPEED UP" with a crystal. That is a
//     recorded divergence from the mockup, not a defect to fix here: changing it is
//     a wallet ruling, and case 6 forbids overwriting the service's answer.
//
// ⛔ AND THE 2026-08-31 RULING IS RESTATED, because turning a section header into
// tabs is exactly the edit that could walk into it. ManageQueueDrawerRegression's
// [rows-not-inline] forbids TWO LITERALS - "AddQueueRow" and
// "AddSectionHeader(\"IN QUEUE - \"" - and ONLY inside the body of
// ManageScreenPanel.RenderList(), the BROWSE catalogue. It says nothing about the
// drawer and nothing about tabs; the drawer is where those very rows are REQUIRED to
// live ([rows-have-a-home]). Case 7 pins the browse list clean so the tab work
// cannot re-seed the overflow the ruling was about.
//
// ⚠ THE HONEST LIMIT, stated the way the sibling suite states its own: DataRegression
// runs in editor batchmode with no play session, so this is a SOURCE SWEEP. It proves
// the build sites exist and are wired to the one path; it cannot prove the overlay
// LOOKS like panel 8. That is the capture loop's job
// (WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md step 4: open the PNGs and look).
//
// Cases:
//   1 [tabs-exist]        the overlay builds three channel tabs, not one section header.
//   2 [tab-counts-real]   the "(n/n)" is Busy/Slots off ChannelSummary, never a literal.
//   3 [rows-numbered]     QueueRowVM carries the model-supplied row number and the
//                         drawer paints it.
//   4 [speedup-verb]      the crystal-priced row's verb is the mockup's SPEED UP.
//   5 [one-rush-path]     WALL: exactly one TryInstantFinish call site, inside FinishNow.
//   6 [price-from-service] WALL: the price and the currency come from the service.
//   7 [rows-not-inline]   WALL: the browse catalogue stays free of queue rows.
//
// Markers: MANAGE_QUEUE_PANEL8_OK / MANAGE_QUEUE_PANEL8_FAIL.
//
// ⛔ NOT REGISTERED IN DataRegression YET, DELIBERATELY. It is RED by design, and
// registering it now would turn REGRESSION_OK red for every concurrent lane that has
// nothing to do with panel 8. The CLI lead registers it in the SAME change that lands
// the model edits, with this one line beside its siblings in DataRegression.cs:
//
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-queue-panel8 suite", () => { if (!DeNelle.Editor.Regression.ManageQueuePanel8Regression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-queue-panel8] " + r); });
//
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace DeNelle.Editor.Regression
{
    /// <summary>Mockup panel 8 (QUEUE overlay), model side: channel tabs with live slot
    /// counts, numbered rows, and SPEED UP bound to the ONE existing paid-finish path.</summary>
    public static class ManageQueuePanel8Regression
    {
        private static readonly string VmPath =
            Path.Combine("Assets", "_Modules", "Village", "UI", "Manage", "ManageScreenVM.cs");
        private static readonly string PanelPath =
            Path.Combine("Assets", "_Modules", "Village", "UI", "Manage", "ManageScreenPanel.cs");

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            string vm = File.Exists(VmPath) ? File.ReadAllText(VmPath) : string.Empty;
            string panel = File.Exists(PanelPath) ? File.ReadAllText(PanelPath) : string.Empty;

            // A missing source is reported as a FAILURE rather than passing vacuously - every
            // assertion below is a Contains() on these strings, and Contains() on "" is a
            // silent pass on all seven cases at once.
            if (vm.Length == 0)
                failures.Add("[tabs-exist] ManageScreenVM.cs is MISSING or empty - nothing below can be trusted");
            if (panel.Length == 0)
                failures.Add("[tabs-exist] ManageScreenPanel.cs is MISSING or empty - nothing below can be trusted");

            if (vm.Length > 0 && panel.Length > 0)
            {
                CaseTabsExist(vm, panel, failures);
                CaseTabCountsReal(vm, failures);
                CaseRowsNumbered(vm, panel, failures);
                CaseSpeedUpVerb(vm, panel, failures);
                CaseOneRushPath(vm, failures);
                CasePriceFromService(vm, failures);
                CaseRowsNotInline(panel, failures);
                CaseVisualTimeline(vm, panel, failures);
            }

            reason = failures.Count == 0
                ? "MANAGE_QUEUE_PANEL8_OK the queue overlay carries three channel tabs counted from the " +
                  "live slot state, numbered rows, and a SPEED UP verb that is the EXISTING " +
                  "TryInstantFinish path priced by InstantFinishPrice, presented as a compact work timeline"
                : "MANAGE_QUEUE_PANEL8_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }

        // ── 1 [tabs-exist] ───────────────────────────────────────────────────────
        // The mockup's overlay is tabbed across ALL THREE channels. Today it shows one
        // channel picked from the Manage tab, so a player upgrading a building cannot see
        // what is training without leaving the overlay, changing tab, and reopening it.
        private static void CaseTabsExist(string vm, string panel, List<string> failures)
        {
            if (!vm.Contains("ComposeQueueTabs("))
                failures.Add("[tabs-exist] ManageScreenVM composes no queue-overlay tab list. Panel 8 draws " +
                             "BUILDERS / TRAINING / RESEARCH as three selectable tabs; the model owns which " +
                             "channels exist and what they are called (canon 9 - the View derives no label " +
                             "from an enum)");
            if (!panel.Contains("BuildQueueTabs("))
                failures.Add("[tabs-exist] the queue drawer builds no tab row. It still leads with the single " +
                             "'IN QUEUE - <CHANNEL>' section header (ManageScreenPanel.cs:2406), which names " +
                             "one channel and offers no route to the other two");

            // The channel the drawer shows must stop being a silent function of the browse tab.
            // ⚠ ManageScreenVM.ChannelOf(_vm.Tab) is still the correct DEFAULT (the overlay should
            // open on the line the player was just looking at) - what is banned is it being the
            // ONLY input, with no selection the player can change.
            if (!vm.Contains("QueueOverlayChannel"))
                failures.Add("[tabs-exist] there is no overlay channel SELECTION on the model. The drawer's " +
                             "channel is derived solely from the browse tab (ManageScreenPanel.cs:2402), so a " +
                             "tab row would have nothing to set. The model owns selection state (canon 2 - it " +
                             "already owns 'last-used tab' the same way)");
        }

        // ── 2 [tab-counts-real] ──────────────────────────────────────────────────
        // "(2/2)" is slots BUSY over slots TOTAL. Both numbers are already computed, per
        // channel, every rebuild: ChannelSummary.Busy / .Slots, filled by AddSummary for
        // Builder, Train and Research. A tab that prints anything else is a second truth.
        private static void CaseTabCountsReal(string vm, List<string> failures)
        {
            // Case 1 already reported the absence; do not double-count it as a second failure.
            if (!vm.Contains("ComposeQueueTabs(")) return;

            // ⚠ ANCHOR ON THE DEFINITION, NOT THE FIRST OCCURRENCE. In this 4000-line file the
            // compose methods are CALLED near Rebuild (~:576-591) and DEFINED ~2700 lines later
            // (ComposeQueueDoor is at :3299). A plain IndexOf would scope from the call site to
            // the next `private` a few lines down - a span containing neither .Busy nor .Slots -
            // and this case would fail AFTER the work landed correctly. A broken oracle is worse
            // than no oracle: it teaches the next seat to delete the pin.
            var def = System.Text.RegularExpressions.Regex.Match(
                vm, @"\bprivate\s+[\w\.<>,\[\]\s]+?ComposeQueueTabs\s*\(");
            if (!def.Success)
            {
                failures.Add("[tab-counts-real] ComposeQueueTabs is CALLED but never DEFINED as a private " +
                             "method on ManageScreenVM - the count cannot be model-owned if the model does " +
                             "not compose it");
                return;
            }
            string body = Body(vm.Substring(def.Index), "ComposeQueueTabs", "\n        private ");
            if (body == null)
            {
                failures.Add("[tab-counts-real] could not locate ComposeQueueTabs' body - a scoped assertion " +
                             "that cannot find its scope is reported as a FAILURE, never passed vacuously");
                return;
            }

            if (!body.Contains(".Busy") || !body.Contains(".Slots"))
                failures.Add("[tab-counts-real] the tab count is not read from ChannelSummary.Busy / .Slots " +
                             "(ManageScreenVM.cs:78-81, filled by AddSummary at :661-663 for all three " +
                             "channels). Any other source is a second count that will drift from the one the " +
                             "three-channel strip already shows");

            if (System.Text.RegularExpressions.Regex.IsMatch(body, "\"\\(\\s*\\d+\\s*/\\s*\\d+\\s*\\)\""))
                failures.Add("[tab-counts-real] a LITERAL '(n/n)' is composed into a tab label. The mockup's " +
                             "2/2 is today's state, not the spec; hardcoding it makes the tab lie the moment " +
                             "the player buys a builder (BuildTimerService.TryBuySlot)");
        }

        // ── 3 [rows-numbered] ────────────────────────────────────────────────────
        private static void CaseRowsNumbered(string vm, string panel, List<string> failures)
        {
            if (!vm.Contains("OrdinalText") || !vm.Contains("PositionText"))
                failures.Add("[rows-numbered] QueueRowVM carries no row number. Panel 8 numbers every row " +
                             "1. 2. 3. 4. 5. - that is the queue's reading order and it is the MODEL's to " +
                             "state. The View counting its own children would be a second ordering that " +
                             "disagrees with the engine the moment a stack is expanded (Q12 children are " +
                             "rows too)");
            else if (!panel.Contains("r.PositionText") || !panel.Contains("QueueTimelineMarker"))
                failures.Add("[rows-numbered] the model publishes its queue position but the drawer does not " +
                             "paint it as a timeline marker - " +
                             "composed-but-unpainted state, which is the exact defect ManageViewContract.cs " +
                             "records twice today (HeaderSubtitle, CountText/CapacityText). Paint it or " +
                             "delete it; do not leave it");

            if (!vm.Contains("r.PositionText = r.Queued ? r.OrdinalText : \"NOW\""))
                failures.Add("[rows-numbered] the timeline marker is not assigned from model-owned queue " +
                             "order. Active work must read NOW and pending work must keep the authoritative " +
                             "number; the View may not recount rendered children");
        }

        // ── 4 [speedup-verb] ─────────────────────────────────────────────────────
        // The verb already flows model -> view: ManageScreenPanel.cs:4604 reads
        // r.FinishVerbText and falls back to "Finish Now" only when it is empty. So this is a
        // one-field model change, and NOT a new button.
        private static void CaseSpeedUpVerb(string vm, string panel, List<string> failures)
        {
            if (!vm.Contains("\"SPEED UP\""))
                failures.Add("[speedup-verb] nothing composes the mockup's SPEED UP verb. The crystal-priced " +
                             "row leaves FinishVerbText empty (ManageScreenVM.cs:808) so the drawer falls back " +
                             "to the literal 'Finish Now' (ManageScreenPanel.cs:4604)");

            if (!panel.Contains("r.FinishVerbText"))
                failures.Add("[speedup-verb] the drawer stopped reading the VM's verb. The word MUST stay the " +
                             "model's: the gold-priced TrainTroop row wears HIRE REINFORCEMENTS (WO-1372) and " +
                             "a view-side literal would overwrite it");

            // ⛔ The verb changes; the WIRING must not. A "SPEED UP" face that calls anything
            // other than the existing command is the second rush path this file exists to prevent.
            if (vm.Contains("\"SPEED UP\"") &&
                !panel.Contains("FinishNow(channel, jobId)"))
                failures.Add("[speedup-verb] SPEED UP exists but the drawer no longer invokes " +
                             "FinishNow(channel, jobId). The button is a RE-WORDING of the existing Finish " +
                             "CTA, not a new one - re-wiring it is how a second crystal sink is born");
        }

        // ── 5 [one-rush-path] — WALL, green today ────────────────────────────────
        private static void CaseOneRushPath(string vm, List<string> failures)
        {
            // The Manage model may reach the paid-finish verb from exactly ONE place.
            // ⚠ COUNT `.TryInstantFinish(` WITH THE DOT, not the bare name. A CALL always has a
            // receiver (`svc.`, `BuildTimerService.Instance.`); the PROSE that discusses it does
            // not - ManageScreenVM.cs:770 says "the wallet TryInstantFinish debits" and would
            // otherwise inflate this count to 2 and fire a false failure. This repo has already
            // shipped a false PASS from the mirror-image mistake
            // (DungeonGemExclusivityRegression.cs:31, :488), and the dot is a cheaper, more
            // provable discriminator than a comment stripper: MEASURED 2026-09-06,
            // `grep -c "\.TryInstantFinish(" ManageScreenVM.cs` = 1.
            int calls = Count(vm, ".TryInstantFinish(");
            if (calls == 0)
                failures.Add("[one-rush-path] ManageScreenVM no longer calls BuildTimerService" +
                             ".TryInstantFinish at all - the paid finish has left the Manage model, which is " +
                             "the WO-1368 shape (a verb that renders and does nothing)");
            else if (calls > 1)
                failures.Add("[one-rush-path] ⛔ " + calls + " TryInstantFinish call sites in ManageScreenVM. " +
                             "SPEED UP must REUSE FinishNow, not add a second paid-finish entry. Two ways to " +
                             "spend crystals on one timer is the duplicated-state failure CLAUDE.md records " +
                             "in 2, 5, 8 and 16");

            string finishNow = Body(vm, "public void FinishNow(ChannelId channel, string jobId)",
                                    "public void WatchAd(");
            if (finishNow == null)
                failures.Add("[one-rush-path] FinishNow(ChannelId, string) is GONE - the one command every " +
                             "queue-row finish affordance is wired to");
            else if (!finishNow.Contains("svc.TryInstantFinish(channel, jobId, out string failure)"))
                failures.Add("[one-rush-path] FinishNow no longer routes through " +
                             "BuildTimerService.TryInstantFinish(channel, jobId, out failure). That method is " +
                             "where JobRushPolicy refuses gated kinds and where the wallet is actually " +
                             "debited (BuildTimerService.cs:1337-1411); bypassing it is a GAP where the owner " +
                             "asked for a WALL (BuildTimerService.cs:1586-1588)");
        }

        // ── 6 [price-from-service] — WALL, green today ───────────────────────────
        private static void CasePriceFromService(string vm, List<string> failures)
        {
            if (!vm.Contains("svc.InstantFinishPrice(channel, job.StructureId)"))
                failures.Add("[price-from-service] the row's price is no longer asked of " +
                             "BuildTimerService.InstantFinishPrice(channel, structureId) " +
                             "(ManageScreenVM.cs:766). The crystal number on SPEED UP must be the number " +
                             "TryInstantFinish will actually charge - a second formula guarantees a face " +
                             "that disagrees with the debit");

            if (!vm.Contains("BuildTimerService.FinishPaysGold(job.JobKind)"))
                failures.Add("[price-from-service] the CURRENCY is no longer the service's decision " +
                             "(ManageScreenVM.cs:773). ⚠ Panel 8 draws a crystal on the active row; a " +
                             "TrainTroop job is priced in GOLD (WO-1372) and that divergence is RECORDED, " +
                             "not overwritten here - the wallet is not this screen's ruling to make");

            // `FinishPrice = 0` is the Q12 stack header's deliberate "no paid verb on an
            // aggregate" (ManageScreenVM.cs:737) - it is removed before the sweep so it cannot
            // mask a real hardcoded price sitting beside it.
            string code = StripComments(vm).Replace("FinishPrice = 0", "");
            if (System.Text.RegularExpressions.Regex.IsMatch(code, @"FinishPrice\s*=\s*\d+\s*[,;]"))
                failures.Add("[price-from-service] a LITERAL crystal price is assigned to a queue row. The " +
                             "mockup's 25 is one job's price at one moment, never the formula");
        }

        // ── 7 [rows-not-inline] — WALL, green today. The 2026-08-31 ruling, restated ──
        // Scoped EXACTLY as ManageQueueDrawerRegression scopes it: the body of RenderList, and
        // two literals. Restated here because replacing the drawer's section header with tabs is
        // precisely the edit that could move rows back into the browse catalogue by accident.
        private static void CaseRowsNotInline(string panel, List<string> failures)
        {
            string renderList = Body(panel, "private void RenderList()", "private string FindSummary");
            if (renderList == null)
            {
                failures.Add("[rows-not-inline] could not locate RenderList's body - the scoped ban cannot be " +
                             "evaluated, so it is reported as a FAILURE rather than passing vacuously");
                return;
            }
            if (renderList.Contains("AddQueueRow"))
                failures.Add("[rows-not-inline] RenderList builds queue rows inline beneath the browse " +
                             "catalogue - the browse destination overflows at landscape height " +
                             "(F8 2026-08-31). The tabs belong in the DRAWER; the browse list stays clean");
            if (renderList.Contains("AddSectionHeader(\"IN QUEUE - \""))
                failures.Add("[rows-not-inline] the IN QUEUE section header is back in the browse list. It is " +
                             "being RETIRED in favour of the drawer's tab row, not relocated into the " +
                             "catalogue");
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>Source between <paramref name="from"/> and the next <paramref name="until"/>,
        /// or null when either marker is absent. Deliberately null-on-miss: a scoped assertion
        /// that cannot find its scope must FAIL, not pass silently on an empty string.</summary>
        // WO-2019: same commands and touch targets, clearer work-timeline hierarchy.
        private static void CaseVisualTimeline(string vm, string panel, List<string> failures)
        {
            if (!vm.Contains("QueueTimingText(") ||
                !vm.Contains("return time + \" LEFT | \" + percent + \"% DONE\""))
                failures.Add("[visual-timeline] queue timing copy is no longer compact/model-owned; " +
                             "the old sentence-length state will ellipsise beside the actions");
            if (!panel.Contains("\"WORK TIMELINE\"") ||
                !panel.Contains("QueueTimelineTrack") ||
                !panel.Contains("QueueTimelineMarker"))
                failures.Add("[visual-timeline] the overlay no longer paints its NOW/numbered work timeline");
            if (!panel.Contains("CapacityChip") ||
                !panel.Contains("tabImage.sprite = null"))
                failures.Add("[visual-timeline] channel selectors have regressed from the compact segmented " +
                             "rail to three equally loud ornate CTA plates");
            if (!panel.Contains("StyleQueueAction(fin") ||
                !panel.Contains("image.sprite = null") ||
                !panel.Contains("if (primary) ElarionUiKit.GoldPerimeter"))
                failures.Add("[visual-timeline] queue actions no longer use the flat contained treatment; " +
                             "finish cost can again read as detached from its button");
        }

        private static string Body(string src, string from, string until)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(until, a + from.Length, StringComparison.Ordinal);
            return b < 0 ? null : src.Substring(a, b - a);
        }

        private static int Count(string src, string needle)
        {
            int n = 0, i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>Blanks // line comments, /* */ blocks and string literals so a COUNT of a
        /// call site cannot be inflated by prose that discusses it. This repo has already shipped
        /// a false pass from exactly that (DungeonGemExclusivityRegression.cs:31).</summary>
        private static string StripComments(string src)
        {
            var sb = new System.Text.StringBuilder(src.Length);
            int i = 0;
            while (i < src.Length)
            {
                if (i + 1 < src.Length && src[i] == '/' && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }
                if (i + 1 < src.Length && src[i] == '/' && src[i + 1] == '*')
                {
                    while (i < src.Length && !(i + 1 < src.Length && src[i] == '*' && src[i + 1] == '/'))
                    { sb.Append(src[i] == '\n' ? '\n' : ' '); i++; }
                    if (i + 1 < src.Length) { sb.Append("  "); i += 2; }
                    continue;
                }
                if (src[i] == '"')
                {
                    sb.Append(' ');
                    i++;
                    while (i < src.Length && src[i] != '"' && src[i] != '\n')
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(' '); i++; }
                        sb.Append(' ');
                        i++;
                    }
                    if (i < src.Length && src[i] == '"') { sb.Append(' '); i++; }
                    continue;
                }
                sb.Append(src[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
