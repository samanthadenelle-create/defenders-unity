// =============================================================================
// ManageQueueDrawerRegression [manage-queue-drawer]
// -----------------------------------------------------------------------------
// F8 2026-08-31: tower browsing leads; queue administration is opt-in.
//
// ⛔ RE-POINTED 2026-09-04 (WO-1368). THIS SUITE ENFORCED THE DEFECT.
//
// The 2026-08-31 ruling is REAL and is unchanged: inline queue rows made the browse
// list overflow at landscape height, so queue rows must not be built into the browse
// catalogue. But commit 486cd7b17 removed the ONLY call to ManageScreenPanel.AddQueueRow
// - the method that builds `Finish Now`, `Ad`, `Cancel` and `Move up` - and added, in
// the SAME change, a case here that FAILS THE BUILD if that call comes back:
//
//     if (panel.Contains("AddSectionHeader(\"IN QUEUE - \"") ||
//         panel.Contains("AddQueueRow(_vm.QueueRows"))
//         failures.Add("queue jobs are duplicated inline beneath the primary upgrade catalogue");
//
// The verbs were moved to "the explicit header Queue drawer", which contained only the
// display-only rail and the Buy-Builder offer. So for three days the crystal sink and
// the rewarded-ad surface had NO BUILD SITE ANYWHERE - and this suite guaranteed they
// could not be restored. It shipped to a production candidate with REGRESSION_OK green.
// (Owner, playing it: "i dont see the watch ad or pay crtystals to complete early stuff".)
//
// ⭐ WHAT CHANGED, AND WHY IT IS A RE-POINT AND NOT A DELETION (the WO-1159 precedent):
// when a ruling MOVES, the pin moves with it and gets STRICTER. The ban is now SCOPED to
// RenderList - the browse catalogue, which is what the ruling was ever about - and a new
// case REQUIRES the rows to exist in the drawer. Absence is no longer a passing state.
//
// ⚠ WHY THE ORIGINAL ACCEPTANCE CRITERION WAS WORTHLESS, recorded so it is not rewritten:
// the ticket first asked for an oracle asserting `queueRows > 0` while the queue is
// non-empty. `queueRows` is the VM's count and it tracked the real job count PERFECTLY
// all morning while not one verb rendered. Asserting the VM computed rows proves nothing.
// These cases assert the BUILD SITE is reached and the CONTROLS are constructed there.
//
// ⚠ THE HONEST LIMIT: DataRegression runs in editor batchmode with no play session, so
// this suite cannot instantiate the panel and count Buttons. It is a SOURCE sweep. What
// would catch the runtime half is the FlowTrace line RenderQueueDrawer now emits
// ("queue drawer BUILT n row(s) ... FinishNow=n Ad=n Cancel=n") read off a device or
// AutoPilot capture with a job queued - which is also the WO-1368 acceptance evidence.
//
// Cases:
//   1 [drawer-exists]     the drawer is constructed, collapsed by default, reachable by
//                         the QUEUE affordance, and spends no browse height.
//   2 [rows-not-inline]   RenderList (the browse catalogue) builds NO queue rows.
//   3 [rows-have-a-home]  AddQueueRow HAS a caller, and it is RenderQueueDrawer.
//   4 [verbs-exist]       Finish Now / Ad / Cancel / Move up are all still constructed,
//                         and each is wired to its VM command.
//   5 [drawer-rendered]   RenderQueueDrawer is actually invoked - on Render and on open.
//   6 [ad-comment-true]   the in-file claim about the ad flag / ad SDK is not the false
//                         one that shipped (it sent a reader chasing a flag already on).
//   7 [townsfolk-paths]   unchanged: early villagers teach the exact Build/Manage paths.
//  12 [door-opens-on-the-hub] WO-1573: the QUEUE pill on the HUB fires ToggleQueueDrawer, but
//                         ManageQueueDrawer is a child of the operational well and the well was
//                         held inactive whenever the hub was up - SetActive(true) on a child of an
//                         inactive parent, so the door opened nothing while the trace still read
//                         "queue drawer expanded". The well now follows the overlay too, the hub's
//                         cards stand down under it, and MANAGE_QUEUE_DOOR prints
//                         activeInHierarchy so the dead-ancestor state can never read as open.
//
// Markers: MANAGE_QUEUE_DRAWER_OK / MANAGE_QUEUE_DRAWER_FAIL.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    /// <summary>F8 2026-08-31: tower browsing leads; queue administration is opt-in.
    /// WO-1368: opt-in means BEHIND the QUEUE affordance, never NOWHERE.</summary>
    public static class ManageQueueDrawerRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string panelPath = Path.Combine("Assets", "_Modules", "Village", "UI", "Manage", "ManageScreenPanel.cs");
            string panel = File.Exists(panelPath) ? File.ReadAllText(panelPath) : string.Empty;
            if (panel.Length == 0)
                failures.Add("[drawer-exists] ManageScreenPanel.cs is MISSING or empty - nothing below can be trusted");

            // ── 1 [drawer-exists] ────────────────────────────────────────────────────────
            if (!panel.Contains("BuildQueueDrawer(well)"))
                failures.Add("[drawer-exists] Manage does not construct the queue side drawer");
            if (!panel.Contains("_queueDrawer.SetActive(false)"))
                failures.Add("[drawer-exists] queue drawer is not collapsed by default");
            if (!panel.Contains("float fixedNoRail = stripCost + noticeCost"))
                failures.Add("[drawer-exists] rail/slot bands still consume default browse height");
            if (!panel.Contains("ManageHeaderActions") || !panel.Contains("TabsBandPx = 0f"))
                failures.Add("[drawer-exists] Queue is not seated in the title row or the redundant destination band returned");
            if (!panel.Contains("\"QUEUE\""))
                failures.Add("[drawer-exists] right-edge queue affordance is missing");

            // ── 2 [rows-not-inline] — the 2026-08-31 ruling, now SCOPED to the browse list ──
            // The ruling was always about the BROWSE CATALOGUE overflowing. Scoping the ban to
            // RenderList's body is what lets the verbs live in the drawer without weakening it.
            string renderList = Body(panel, "private void RenderList()", "private string FindSummary");
            if (renderList == null)
                failures.Add("[rows-not-inline] could not locate RenderList's body - the scoped ban cannot be evaluated, " +
                             "so it is reported as a FAILURE rather than passing vacuously");
            else
            {
                if (renderList.Contains("AddQueueRow"))
                    failures.Add("[rows-not-inline] RenderList builds queue rows inline beneath the primary upgrade " +
                                 "catalogue - the browse destination overflows at landscape height (F8 2026-08-31)");
                if (renderList.Contains("AddSectionHeader(\"IN QUEUE - \""))
                    failures.Add("[rows-not-inline] the IN QUEUE section header is back in the browse list");
            }

            // ── 3 [rows-have-a-home] — the WO-1368 defect, asserted directly ──────────────
            // A private method with zero callers is dead code that LOOKS like a shipped feature.
            // That is exactly what `Finish Now` and `Ad` were for three days.
            int defs = Count(panel, "private void AddQueueRow(");
            int calls = Count(panel, "AddQueueRow(") - defs;
            if (defs == 0)
                failures.Add("[rows-have-a-home] AddQueueRow is GONE - nothing builds Finish Now / Ad / Cancel / Move up");
            else if (calls == 0)
                failures.Add("[rows-have-a-home] ⛔ AddQueueRow has ZERO CALLERS. The crystal sink and the rewarded-ad " +
                             "surface are unreachable from every tab, every channel, at every queue depth - the exact " +
                             "WO-1368 defect that shipped to a production candidate with every marker green");
            // Bounded by RenderList, which follows it - so this scope holds the drawer renderer
            // ALONE and cannot be satisfied by something RenderList does.
            string drawerRender = Body(panel, "private void RenderQueueDrawer()", "private void RenderList()");
            if (drawerRender == null)
                failures.Add("[rows-have-a-home] RenderQueueDrawer is missing - the drawer has no row build site, which " +
                             "is the state the removal comment already claimed was not true");
            else if (!drawerRender.Contains("AddQueueRow(_vm.QueueRows[i])"))
                failures.Add("[rows-have-a-home] RenderQueueDrawer does not build the VM's queue rows - the drawer would " +
                             "again be a rail and an offer with no verbs");

            // ── 4 [verbs-exist] — each control AND its command ───────────────────────────
            RequirePair(panel, failures, "\"Finish Now\"", "FinishNow(channel, jobId)", "Finish Now (the crystal sink)");
            RequirePair(panel, failures, "\"Ad\"",         "WatchAd(channel, jobId)",   "Ad (the rewarded-ad surface)");
            // ⛔ RE-POINTED 2026-09-07 (WO-1488): the authored face is "CANCEL", not "Cancel".
            // The word was capitalised with the slot that finally fits it - the owner's device
            // (Logs/device/screens/owner-screen-20260907-010356.png) read "CANC...", because three
            // even cluster slots gave a two-letter "Ad" the same ~131px as a six-letter verb.
            // ⚠ WHAT THIS PIN PROTECTS IS UNCHANGED AND IS NOT THE CASING: a control face must
            // exist AND its command must be invoked, or a button that renders and does nothing
            // reads as "shipped". Only the authored spelling moved, and there is still exactly
            // ONE of it.
            RequirePair(panel, failures, "\"CANCEL\"",     "Cancel(channel, jobId)",    "Cancel");
            RequirePair(panel, failures, "\"Move up\"",    "BumpUp(channel, jobId",     "Move up");

            // ── 5 [drawer-rendered] — built is not the same as rendered ──────────────────
            string render = Body(panel, "private void Render()", "private void ApplyOperationalMedievalSkin");
            if (render == null || !render.Contains("RenderQueueDrawer()"))
                failures.Add("[drawer-rendered] Render() does not refresh the open drawer - rows would be built once and " +
                             "then never track the queue");
            else if (render.IndexOf("RenderQueueDrawer()", StringComparison.Ordinal) <
                     render.IndexOf("RenderList()", StringComparison.Ordinal))
                failures.Add("[drawer-rendered] Render() builds the drawer BEFORE RenderList, which clears the tick and " +
                             "progress cells - the drawer's rows would keep their buttons and silently lose their " +
                             "countdowns");
            string toggle = Body(panel, "private void ToggleQueueDrawer()", "private void BuildNotice");
            if (toggle == null || !toggle.Contains("RenderQueueDrawer()"))
                failures.Add("[drawer-rendered] ToggleQueueDrawer does not render the drawer on open - the QUEUE " +
                             "affordance would reveal an empty panel");

            // ── 6 [ad-comment-true] — a comment that lies costs the next seat a session ──
            if (panel.Contains("no ad SDK is wired anywhere"))
                failures.Add("[ad-comment-true] the stale 2026-08-07 comment is back: it calls FeatureFlags.RewardedAdSkip " +
                             "OFF and claims no ad SDK is wired. Both are false (the flag is declared defaultOn:true and " +
                             "LevelPlay is integrated), and it sends a reader chasing a flag that is already on");

            // ── 7 [townsfolk-paths] — unchanged from the original suite ─────────────────
            string first = TownsfolkDialogue.BuildHelpFor(TownsfolkDialogue.Archetype.Trader, 0);
            if (!first.Contains("Tap Build") || !first.Contains("Defense") || !first.Contains("tower card"))
                failures.Add("[townsfolk-paths] townsfolk do not teach the exact Build > Defense > tower-card path");
            string second = TownsfolkDialogue.BuildHelpFor(TownsfolkDialogue.Archetype.Trader, 1);
            if (!second.Contains("Manage") || !second.Contains("Upgrade"))
                failures.Add("[townsfolk-paths] townsfolk do not teach the exact tower upgrade path");
            if (TownsfolkDialogue.ShouldOfferBuildHelp(3, TownsfolkDialogue.Archetype.Trader, 0))
                failures.Add("[townsfolk-paths] onboarding help continues after the opening waves");

            // ── 8 [queue-toggle-closes] — WO-1393 (2026-09-05) ───────────────────────────
            // PROVEN (docs/qa/UI_REVIEW_2026-09-05/10-troops-after-upgrade.png): the top-right
            // QUEUE tap closed nothing, because ToggleQueueDrawer HID the toggle while the drawer
            // was open and BuildTabs rebuilt it hidden. The face must be wired to ToggleQueueDrawer,
            // must stay on screen while open, and a second call must flip the state back.
            // RED mutations: restore `_queueDrawerToggle.gameObject.SetActive(!_queueDrawerOpen)`
            // in either site; change `_queueDrawerOpen = !_queueDrawerOpen` to `= true`.
            // ⚠ PIN CORRECTED 2026-09-06: it pinned a COORDINATE, and the coordinate moved.
            // It required the literal `new Vector2(0.965f, 1f), ToggleQueueDrawer);`. WO-1443 moved
            // the queue face three times under owner rulings and one measurement - into the tab row,
            // out to the host's top-right pill, then wider (0.72-0.95) once the printed rect proved
            // the LABEL was overflowing its 184px button rather than the frame clipping it. Each
            // move was right; the pin failed on the x value every time.
            // ⛔ THE INVARIANT WAS NEVER THE RECTANGLE. It is that the QUEUE face exists, is named
            // so a capture can find it, and its tap raises ToggleQueueDrawer - the door this suite
            // spent three rounds protecting. VERIFIED AT SOURCE THIS ROUND:
            // ManageScreenPanel.cs:1919-1935 builds it on _tabsHost with ToggleQueueDrawer as the
            // callback, and :1938 names the object ManageQueueDrawerToggle.
            // Pinning the wiring keeps the guard and stops the case from forbidding the next ruling.
            string tabs = Body(panel, "private void BuildTabs()", "//  RENDER");
            if (tabs == null || !tabs.Contains("ManageQueueDrawerToggle") ||
                !tabs.Contains("ToggleQueueDrawer)"))
                failures.Add("[queue-toggle-closes] the title-row QUEUE face is not wired to ToggleQueueDrawer");
            if (tabs != null && tabs.Contains("SetActive(!_queueDrawerOpen"))
                failures.Add("[queue-toggle-closes] BuildTabs rebuilds the QUEUE face HIDDEN while the drawer is " +
                             "open - the top-right tap closes nothing (10-troops-after-upgrade.png)");
            if (toggle != null && toggle.Contains("SetActive(!_queueDrawerOpen"))
                failures.Add("[queue-toggle-closes] ToggleQueueDrawer hides the QUEUE face while open - the one " +
                             "affordance that closes the drawer is removed by opening it");
            if (toggle == null || !toggle.Contains("_queueDrawerOpen = !_queueDrawerOpen;") ||
                !toggle.Contains("\"collapsed\""))
                failures.Add("[queue-toggle-closes] ToggleQueueDrawer is not a flip (a second call must collapse " +
                             "and trace 'queue drawer collapsed')");
            // ⚠ PIN MOVED 2026-09-06 (WO-1443), WITH THE RULING, AND IT MOVED IN THE STRICTER
            // DIRECTION. It used to REQUIRE `SetActive(_vm != null && _vm.Channels.Count > 0)`.
            // That condition was safe while the workspace painted its own queue face and this was a
            // spare chrome control; it is NOT safe now that this pill is the only door to the queue
            // on every Manage screen. A door behind a condition is the WO-1430 defect class, so the
            // case now FORBIDS the condition it used to demand. The other half - the face reading as
            // the close while the drawer is open - is unchanged and still pinned.
            // ⚠ PIN MOVED AGAIN 2026-09-06, WITH THE RULING, AND THE REASON IS A REAL DEFECT.
            // It required the pill to relabel itself "HIDE QUEUE" while the drawer was open - the
            // WO-1393 fix, correct when this face was the ONLY way to shut the drawer. Mockup panel
            // 8 gives the overlay its own X (BuildQueueDrawer), so that reason is gone; and the
            // relabel actively BROKE the pill, because SizeQueuePillToLabel measures the word at
            // build time and sizes the button to it - swapping in a longer word afterwards
            // truncated it, which the capture showed as "HIDE QU..." in the chrome slot.
            // ⛔ WHAT THIS CASE DEFENDS IS UNCHANGED: the drawer must always be CLOSEABLE, and the
            // pill must never be gated away. It now pins the three closers that exist - the pill
            // stays visible and toggles, the overlay has its own X, and BACK closes the drawer
            // first - instead of pinning one particular word on one of them.
            string sync = Body(panel, "private void SyncQueueToggleFace()", "private void ToggleQueueDrawer()");
            if (sync == null || !sync.Contains("_queueDrawerToggle.gameObject.SetActive(true);"))
                failures.Add("[queue-toggle-closes] SyncQueueToggleFace does not keep the QUEUE pill " +
                             "unconditionally visible. It is the one door to the queue; gating it strands " +
                             "the surface");
            if (!panel.Contains("ManageQueueOverlayClose"))
                failures.Add("[queue-toggle-closes] the queue overlay has no X of its own. Panel 8 draws one, " +
                             "and it is what replaced the pill's HIDE QUEUE relabel - without it the only way " +
                             "out is the pill the overlay is covering");
            if (!panel.Contains("if (_queueDrawerOpen) { ToggleQueueDrawer(); return; }"))
                failures.Add("[queue-toggle-closes] BACK no longer closes an open queue drawer first - the " +
                             "overlay would swallow the back gesture");
            if (toggle != null && !toggle.Contains("SyncQueueToggleFace()"))
                failures.Add("[queue-toggle-closes] ToggleQueueDrawer does not re-sync the QUEUE face");

            // WO-2019: ARMY proved the legacy short-band branch clips a three-channel drawer.
            if (!panel.Contains("bool band = false;"))
                failures.Add("[queue-one-shape] Queue can re-enter the legacy short-band mode. It must use " +
                             "the same full overlay on BUILD, ARMY, and RESEARCH");
            if (!panel.Contains("_manageExit.gameObject.SetActive(!chromeHidden)"))
                failures.Add("[queue-one-close] the global Manage X remains visible beside the Queue modal's " +
                             "own X, creating two adjacent close controls with different destinations");
            if (!panel.Contains("if (_queueDrawerOpen) drawer.SetAsLastSibling();"))
                failures.Add("[queue-modal-order] the Queue modal is not reasserted as the last-painted " +
                             "workspace child; a later BUILD rebuild can mask its header and actions");

            // ── 9 [drawer-clear-of-card] — WO-1393 (2026-09-05) ──────────────────────────
            // The drawer used to be a full-body overlay on every tab; on Troops it sat OVER the
            // selected-troop card and the UPGRADE tap hit the drawer. Now, on the Troops tab, the
            // drawer is its own BAND under a list viewport that keeps everything ABOVE the card's
            // CTA line, and the TRAINING NOW band collapses. This is a SOURCE pin on the band
            // constants + the placement literals, with the arithmetic replayed at the reference
            // 2670x1200 (well=533 off Builds/manage-capture.log bands(px)) - DataRegression cannot
            // instantiate the panel. RED mutations: keep the whole workspace in view
            // (`DrawerModeListKeepPx = 10f + TroopWorkspacePx`) -> the band drops under 216px;
            // drop the `if (!_drawerBandMode)` off the drawer's rail mount; restore the old
            // full-body list zone (0.30-0.86) -> the header no longer fits under the rail.
            float ws = Const(panel, "TroopWorkspacePx"), cta1 = Const(panel, "TroopCtaY1"),
                  gap = Const(panel, "BandGapPx"), header = Const(panel, "SectionHeaderPx"),
                  row = Const(panel, "RowHeightPx"), band = Const(panel, "TrainingNowBandPx"),
                  strip = Const(panel, "StripBandPx"), rail = 200f;
            if (ws <= 0 || cta1 <= 0 || gap <= 0 || header <= 0 || row <= 0 || band <= 0 || strip <= 0)
                failures.Add("[drawer-clear-of-card] could not read the band constants off the source - the " +
                             "arithmetic cannot be replayed, reported as a FAILURE rather than passing vacuously");
            else
            {
                const float wellRef = 533f;                  // 2670x1200, captured bands(px)
                float list = wellRef - (strip + gap);        // 401
                float keep = 10f + ws * (1f - cta1);         // 154.3: pad + everything above the CTA line
                float drawerPx = list - keep - gap;          // 234.7
                float need = header + row + 20f;             // 216: header + one verb row + scroll pad
                if (10f + ws + 8f + band > list)
                    failures.Add("[drawer-clear-of-card] WO-1382 fold broken: 10 + workspace + 8 + TRAINING NOW = " +
                                 (10f + ws + 8f + band) + " > LIST " + list + " at 2670x1200");
                if (drawerPx < need)
                    failures.Add("[drawer-clear-of-card] the Troops drawer band is " + drawerPx + "px at 2670x1200, " +
                                 "under the " + need + "px a header and one verb row need - the first verb is " +
                                 "under the fold");
                // ⛔ RETIRED 2026-09-06 (WO-1488), NOT DELETED, SO IT IS NOT RE-ADDED. This read
                //     float fullBodyList = 0.84f * (0.82f * wellRef) - 20f;   // 347
                //     if (fullBodyList < rail + 8f + header) ...
                // and it asserted the full-body list could seat THE CARD RAIL. The rail was removed
                // from the overlay in WO-1443 ("⛔ NO CARD RAIL IN THE OVERLAY"), and case 9 below
                // now FAILS if it comes back - so this was demanding room for a control its own
                // suite forbids. Worse, its three literals (0.84, 0.82, 533) were a THIRD copy of
                // the drawer's rect: the panel authored -0.25..0.99 in one method and 0.02..0.84 in
                // another, and the r24 log shows both (drawer=719px, drawer=475px). The rect is now
                // ONE pair of constants and the [rows-inside-the-plate] case below reads them.
                // `rail` is kept in scope above only so this note can name what it measured.
                if (rail <= 0f)
                    failures.Add("[drawer-clear-of-card] the rail height reference went missing");
            }
            // ⛔ RE-POINTED 2026-09-07 (WO-1488), WITH THE REASON KEPT SO IT IS NOT MOVED BACK.
            // It read:
            //     if (!panel.Contains("DrawerModeListKeepPx = 10f + TroopWorkspacePx * (1f - TroopCtaY1)"))
            //         ... "the kept viewport and the CTA rect are no longer tied together"
            // That pin was a VERBATIM STRING MATCH on an expression that measures the RETIRED 260px
            // troop card - TroopWorkspacePx x (1 - TroopCtaY1). The tie it protected was real when
            // the drawer was a BAND under that card; it is not what the player meets now. The
            // overlay path (ApplyDrawerPlacement's else-branch) is the one every Manage screen
            // takes, because `band` requires !WorkspaceActive and the WO-2001 workspace owns the
            // well on all four destinations. So the pin was holding a constant in place for a
            // shape nothing renders, and the owner's frames
            // (Logs/device/screens/owner-screen-20260907-010356.png / -010257.png) show the
            // overlay's rows clipped top and bottom while this case stayed green.
            //
            // ⭐ IT NOW MEASURES THE DERIVED ROW HEIGHT AND PINS THAT NO ROW IS CLIPPED.
            // The invariant is the one that matters on a frame: whatever height a queue row is
            // built at, the list seats a WHOLE number of them and every one of them clears the
            // touch floor. The band expression is left in the source untouched and unpinned - it
            // still seats the legacy band shape, and deleting it is a separate ticket.
            {
                if (!panel.Contains("MakeRowHost(\"QueueRow\", _queueRowPx)"))
                    failures.Add("[drawer-clear-of-card] the queue row is not built at the DERIVED height " +
                                 "(_queueRowPx). Building at the authored RowHeightPx again means the row " +
                                 "height cannot answer the well, and mockup panel 8's five visible rows " +
                                 "become an aspiration instead of arithmetic");
                if (!panel.Contains("QueueRowsVisibleTarget"))
                    failures.Add("[drawer-clear-of-card] the overlay names no visible-row target. Panel 8 draws " +
                                 "FIVE numbered rows; a layout with no capacity to answer to cannot report a " +
                                 "shortfall, which is how a one-row drawer shipped green");
                string seat = Body(panel, "private void SeatQueueListToWholeRows()",
                                          "private void AddQueueRow(");
                if (seat == null)
                    failures.Add("[drawer-clear-of-card] SeatQueueListToWholeRows is missing - nothing trims the " +
                                 "list to whole rows, so the bottom row is sliced through its own text");
                else
                {
                    if (!seat.Contains("_drawerList.rect.height"))
                        failures.Add("[drawer-clear-of-card] the row height is not derived from the MEASURED list " +
                                     "band. A height taken from a constant is the DrawerModeListKeepPx defect " +
                                     "wearing a new name");
                    if (!seat.Contains("Mathf.Clamp(ideal, ElarionUiKit.MinTouchPx, RowHeightPx)"))
                        failures.Add("[drawer-clear-of-card] the derived row height is not clamped into " +
                                     "[MinTouchPx, RowHeightPx]. Below the floor five rows are five rows nobody " +
                                     "can press; above the authored height the row grows past its own text bands");
                    if (!seat.Contains("whole * _queueRowPx"))
                        failures.Add("[drawer-clear-of-card] the whole-row trim no longer measures the height the " +
                                     "rows are actually BUILT at - the two units drift and the last row clips, " +
                                     "which is exactly the row-2 overhang this suite already carries a case for");
                    if (!seat.Contains("QueueRowsVisibleTarget"))
                        failures.Add("[drawer-clear-of-card] the seating never compares what it got against what " +
                                     "panel 8 draws, so a well that seats one row reports nothing");
                }
            }
            string place = Body(panel, "private void ApplyDrawerPlacement()", "private void SyncQueueToggleFace()");
            if (place == null)
                failures.Add("[drawer-clear-of-card] ApplyDrawerPlacement is missing - nothing seats the drawer band");
            else
            {
                if (!place.Contains("_listBandTopPx + Mathf.Min(DrawerModeListKeepPx, _listBandPx) + BandGapPx"))
                    failures.Add("[drawer-clear-of-card] the drawer band is not seated BELOW the kept list viewport " +
                                 "plus the gutter - it can overlap the card's CTAs");
                if (!place.Contains("child.name.StartsWith(TrainingNowPrefix") || !place.Contains("SetActive(!band)"))
                    failures.Add("[drawer-clear-of-card] the TRAINING NOW band does not collapse while the drawer " +
                                 "is open - the drawer supersedes it and needs its height");
                // RE-POINTED 2026-09-06 (CLI, at the gate) — WO-2001 extended this expression to
                // `SetActive(!WorkspaceActive && (!_queueDrawerOpen || band))`. The added guard is
                // CORRECT: the new Manage workspace replaces the legacy list band entirely, so the
                // band must stand down while the workspace renders. The INVARIANT this case exists
                // to protect is unchanged and still holds — in band mode with the drawer open the
                // list band stays up, so the card remains readable (`band` true => the disjunction
                // is true). The pin now asserts the DISJUNCTION, not the whole line, so a future
                // legitimate guard does not re-break it while a real inversion still fails.
                // A pin that requires the old text is a pin that forbids the fix.
                if (!place.Contains("(!_queueDrawerOpen || band)"))
                    failures.Add("[drawer-clear-of-card] band mode hides the list band - the card is gone instead " +
                                 "of readable (ruling #6)");
            }
            if (!panel.Contains("TrainingNowPrefix = \"TroopTrainingNow\"") ||
                !panel.Contains("MakeRowHost(\"TroopTrainingNowBand\"") ||
                !panel.Contains("MakeRowHost(\"TroopTrainingNowRow_\""))
                failures.Add("[drawer-clear-of-card] the TRAINING NOW row names no longer match the collapse prefix");
            if (drawerRender != null)
            {
                // ⚠ PIN INVERTED 2026-09-06 (WO-1443 panel 8), WITH THE RULING, AND IT IS STRICTER.
                // It required the GUARD `if (!_drawerBandMode) MountRail(MakeRowHost("Drawer_QueueRail"...`
                // so the 200px card rail could not clip the IN QUEUE header in band mode. The rail
                // is now gone from the overlay ENTIRELY - mockup panel 8 has numbered rows and no
                // rail, and a status glance that repeats what the rows below it say, in the space
                // they need, is not worth a band in any mode.
                // ⛔ SO THE GUARD'S ABSENCE IS NO LONGER THE DEFECT - THE CALL'S PRESENCE IS.
                // Requiring the guard made this case fail on the fix: it reported "the drawer mounts
                // its rail in band mode" when the truth was that the guarded call had been deleted.
                // VERIFIED AT SOURCE: MountRail has exactly two remaining call sites, neither in the
                // drawer - ManageScreenPanel.cs:2731 (the legacy pinned path, inert because
                // BuildQueueDrawer sets _railBand = null at :1915) and :2950 (the browse list's own
                // RailRow). The invariant this case defends is now satisfied more strongly than the
                // guard ever satisfied it: there is no rail in the overlay, in any mode.
                // ⚠ THE TOKEN IS THE RAIL'S HOST NAME, NOT THE METHOD'S. `drawerRender` is
                // Body(panel, "private void RenderQueueDrawer()", "private void RenderList()"), and
                // MEASURED this round that span is ~9.7k chars and swallows the `private void
                // MountRail(...)` DECLARATION itself - so a bare `MountRail(` check fires on the
                // method's own signature and reports a rail that is not mounted. `Drawer_QueueRail`
                // is the drawer's own rail host and appears nowhere in the file (verified 0),
                // so it names the thing being forbidden without matching a declaration.
                if (drawerRender.Contains("Drawer_QueueRail") ||
                    drawerRender.Contains("MountRail(MakeRowHost("))
                    failures.Add("[drawer-clear-of-card] the queue overlay mounts a card rail again. Panel 8 " +
                                 "has numbered rows and no rail; a 200px strip of card art repeats what those " +
                                 "rows already say and, in band mode, clipped the header under it");
                if (!drawerRender.Contains("MakeRowHost(\"Drawer_SlotOfferRow\""))
                    failures.Add("[drawer-clear-of-card] the Buy-Builder offer is not the drawer list's last row - " +
                                 "a fixed offer zone starves the verb rows of height");
            }
            // ⚠ PIN MOVED 2026-09-06 (WO-1443 panel 8), WITH THE RULING, AND IT IS STRICTER NOW.
            // It required the literal `new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.86f));` so
            // the full-body list would clear the 200px card RAIL and the IN QUEUE header. Both of
            // those are gone: mockup panel 8 has no rail (a status glance saying what the rows below
            // it already say, in the space they need) and no single-channel header (three tabs
            // replace it). The list zone is now the band table's DrawerListY0/Y1.
            // ⛔ WHAT IT DEFENDS IS THE SAME AND IS NOW CHECKED DIRECTLY: the list must not run into
            // the band above it. That is exactly the fault the pin's old form could not see -
            // MANAGE_QUEUE_LAYOUT measured tabs [y 688..806] against listView [y 299..791], a 103px
            // overlap, because ApplyDrawerPlacement re-seated the list to its own 0.86 literal AFTER
            // the render and wiped the authored 0.665. Two writers, one piece of state.
            // So this case now pins the ONE SOURCE and the ORDERING INVARIANT between the bands.
            // ⚠ RE-POINTED AGAIN 2026-09-06, FROM FRACTIONS TO PIXELS, AND IT GOT STRICTER.
            // The band table WAS fractions of the drawer, and the audit proved a fraction cannot
            // carry a px promise: the tab row resolved 95.1px against MinTouchPx(112), and the
            // fixed-120px X overflowed a 42.8px title band into the tabs beneath it. Heights are
            // now PX constants and the fractions are derived from the measured drawer
            // (ManageScreenPanel.SetDrawerBands), which is the band law ManageWorkspacePanel's own
            // header states. So the pin moves to the numbers that are now authored.
            if (!panel.Contains("private const float DrawerTitlePx") ||
                !panel.Contains("private const float DrawerTabsPx") ||
                !panel.Contains("private void SetDrawerBands("))
                failures.Add("[drawer-clear-of-card] the queue overlay's px band table is gone. Its seats were " +
                             "fractions once, which is how the tab row shipped 17px under the touch floor and " +
                             "the X spilled out of its own band");
            else
            {
                float tabsPx = ConstOf(panel, "DrawerTabsPx");
                float titlePx = ConstOf(panel, "DrawerTitlePx");
                // ⚠ DECLARED HERE, IN THIS CASE'S OWN SCOPE. The round-26 title pin below reached
                // for a `minTouch` that belongs to [rows-inside-the-plate]'s block further down -
                // CS0103, and a red gate. Two cases in one method do not share locals; each reads
                // what it asserts on.
                const float minTouch = ElarionUiKit.MinTouchPx;
                if (tabsPx < 0f || titlePx < 0f)
                    failures.Add("[drawer-clear-of-card] could not read DrawerTabsPx / DrawerTitlePx off the " +
                                 "source - a scoped assertion that cannot find its scope FAILS, never passes");
                else
                {
                    if (tabsPx < 112f)
                        failures.Add("[drawer-clear-of-card] the queue tab band is " + tabsPx +
                                     "px, under ElarionUiKit.MinTouchPx (112). Author the band AT the floor - " +
                                     "ClampMinTouch grows a control symmetrically and spills it into its " +
                                     "neighbours, which on this row is the queue list");
                    // ⚠ RE-POINTED: the TAB band now holds the overlay's X, not the title band.
                    // The title band was 132px and the capture showed it EMPTY - the word QUEUE
                    // renders above the drawer's visible top edge, because the drawer's sliced
                    // content-panel art does not reach its own rect. 132px was reserved for
                    // something drawn outside it, and it was the difference between ONE visible row
                    // and TWO. The title is now an overlay above the ceiling (consuming no band,
                    // DrawerTitlePx = 0) and the X moved into the column the tab row already leaves
                    // free at TabsRightStop.
                    // ⛔ RE-POINTED 2026-09-07 (WO-1567 round 26), WITH THE RULING, AND THE REASON
                    // ABOVE IS KEPT BECAUSE IT WAS TRUE OF THE SHAPE IT DESCRIBED. It read
                    //     if (titlePx > 0f) ... "Keep DrawerTitlePx at 0"
                    // which was correct only while the drawer's ceiling left room INSIDE THE WELL
                    // for the header to be drawn above it. Round 25 raised DrawerOverlayY1 to 1.0,
                    // and Builds/cap-manage-wave5.log then failed all three *_queue frames:
                    //   TEXT OFF PLATE 'Drawer_Header/Label' ("QUEUE") overflows its layout.body
                    //   ZoneBacking by 112 ref px -- text y 313.2..425.2 vs plate y -444.8..313.2
                    // i.e. the header had grown off the body's black plate - RULE 1, the
                    // founding-Echo-card defect. Extending the DRAWER's plate does not answer it:
                    // UICaptureLaunch.ZoneBodyAbove walks to the ancestor named Zone_Body and
                    // PlateOf takes THAT zone's backing, not the nearest one.
                    // ⭐ SO THE PIN INVERTS: the title band must be REAL and INSIDE the drawer.
                    // What it always defended is unchanged - the word QUEUE and a MinTouchPx X both
                    // need a rect they fit in, because TMP culls a line whose rect cannot seat its
                    // font floor, and a zero-height zone deletes the word rather than freeing space.
                    if (titlePx < minTouch)
                        failures.Add("[drawer-clear-of-card] the queue title band is " + titlePx +
                                     "px, under ElarionUiKit.MinTouchPx (" + minTouch + "). It carries the " +
                                     "word QUEUE and the overlay's X; at 0 it was drawn ABOVE the drawer, " +
                                     "which since DrawerOverlayY1 = 1.0 means off the body's black plate - " +
                                     "six TEXT OFF PLATE failures on Builds/cap-manage-wave5.log, 112px each");
                    if (!panel.Contains("_drawerHeader.pivot = new Vector2(0.5f, 1f);"))
                        failures.Add("[drawer-clear-of-card] the title zone grows UPWARD out of the drawer " +
                                     "again (pivot 0). With the overlay reaching the well's ceiling that puts " +
                                     "the header off the body plate. It must grow DOWNWARD into the band " +
                                     "DrawerTitlePx now reserves for it.");
                    // ⛔ RE-POINTED 2026-09-06 (WO-1488), WITH THE RULING, AND THE REASON IS KEPT
                    // SO IT IS NOT MOVED BACK. It read
                    //     if (tabsPx <= 120f) ... "must be LARGER than the 120px X it now contains"
                    // which was correct only while the X lived in the tab band - and the capture
                    // (ManageFlow_BUILD_queue_2670x1200.png, 18:39) shows exactly what that seat
                    // cost: the X renders as a FOURTH TAB beside "RESEARCH 2/2", same row, same
                    // face, same height. The X is now in the title overlay at the drawer's
                    // top-right (mockup panel 8). The band keeps 132px because that is what its
                    // FACES need, which the >= 112 case above already pins; asserting it against a
                    // control that is no longer in it would forbid the fix.
                    if (!panel.Contains("BuildObsidianButton(_drawerHeader, \"X\""))
                        failures.Add("[drawer-clear-of-card] the queue overlay's X is not built into the TITLE " +
                                     "OVERLAY. Seated in the tab band it reads as a fourth channel tab - a close " +
                                     "control that looks like a channel is one the player taps to switch " +
                                     "channels (measured 2026-09-06, beside 'RESEARCH 2/2')");
                    if (panel.Contains("BuildObsidianButton(_drawerTabs, \"X\""))
                        failures.Add("[drawer-clear-of-card] the X is back in the queue tab strip");
                    if (!panel.Contains("private void SeatDrawerTitleOverlay()"))
                        failures.Add("[drawer-clear-of-card] SeatDrawerTitleOverlay is gone. A zero-height " +
                                     "title zone does not free space, it DELETES the word: TMP culls a line " +
                                     "whose rect cannot seat its font floor. The overlay seat is what makes " +
                                     "DrawerTitlePx = 0 legal");
                }
            }
            // ...and the SECOND writer must read the table rather than re-typing a fraction.
            if (!panel.Contains("band ? 1.0f : _drawerListY1"))
                failures.Add("[drawer-clear-of-card] ApplyDrawerPlacement re-seats the drawer list from its own " +
                             "literal again instead of the band table. It runs AFTER RenderQueueDrawer, so its " +
                             "value is the one that survives - and a fraction typed twice is a fraction that " +
                             "will disagree with itself");
            if (!panel.Contains("ApplyDrawerPlacement();\n                // WO-1368") &&
                !panel.Contains("ApplyDrawerPlacement();\r\n                // WO-1368"))
                failures.Add("[drawer-clear-of-card] Render() does not re-seat the drawer after RenderList rebuilt " +
                             "the TRAINING NOW band - it comes back active under the drawer");

            // ── 10 [queue-door-in-workspace] — WO-1443 section 1B (owner ruling 2026-09-06) ──────
            // ⛔ THE DOOR THIS CASE PINS HAD NO PIN AT ALL, AND THAT IS WHY IT IS HERE.
            // MEASURED 2026-09-06: in WORKSPACE mode (which is every Manage screen since WO-2001)
            // the QUEUE affordance rendered by ManageWorkspacePanel is the ONLY live route to the
            // queue while nothing is running. ShowWorkspace deactivates the legacy header toggle,
            // `_operationalListBand` (which owns the three OPEN QUEUE bands) is SetActive(false),
            // the activity strip is Visible=false while idle, and the HUD Builders chip's door was
            // retired in WO-911. PanelDoorRegression CANNOT see this: it only inventories
            // MonoBehaviour types named *Panel, and ManageWorkspacePanel is deliberately a plain
            // class. So the single most load-bearing door on the screen was unguarded - the exact
            // shape WO-1430 catalogued three times over. The owner's ruling MOVED it from the
            // header into the tab row; a door that moves and breaks is worse than a chip that was
            // ugly, so the move brought its pin with it.
            // RED mutations: drop `BuildQueueDoor(band, vm.Queue, tabs.Count, slots)` from
            // BuildTabs; change ComposeQueueDoor's `Visible = true` to a condition.
            string wsPath = Path.Combine("Assets", "_Modules", "Core", "Manage", "ManageWorkspacePanel.cs");
            string ws2 = File.Exists(wsPath) ? File.ReadAllText(wsPath) : string.Empty;
            string vmPath = Path.Combine("Assets", "_Modules", "Village", "UI", "Manage", "ManageScreenVM.cs");
            string vm2 = File.Exists(vmPath) ? File.ReadAllText(vmPath) : string.Empty;
            if (ws2.Length == 0 || vm2.Length == 0)
                failures.Add("[queue-door-in-workspace] ManageWorkspacePanel.cs or ManageScreenVM.cs is MISSING - " +
                             "the door cannot be evaluated, reported as a FAILURE rather than passing vacuously");
            else
            {
                // ⚠ PIN MOVED 2026-09-06, SECOND TIME IN ONE DAY, AND THE REASON IS RECORDED SO
                // THE NEXT SEAT DOES NOT MOVE IT BACK. This case first pinned the door in the
                // WORKSPACE TAB ROW, because the owner's ruling had reached us as words. Her MOCKUP
                // (docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png) then landed in the repo and
                // draws QUEUE as a SMALL PILL AT TOP-RIGHT with a count badge on every one of its
                // eight numbered panels - and it had said so since 09:26 that morning. The mockup is
                // the spec (CAPTURE_LOOP_GOAL.md 3.0c) and it wins over a sentence about it.
                // THE INVARIANT IS UNCHANGED: the queue keeps a live, unconditional door on every
                // Manage screen. Only its SEAT moved, from the renderer to the host chrome.
                if (!panel.Contains("BuildQueueCountBadge(_queueDrawerToggle.transform)"))
                    failures.Add("[queue-door-in-workspace] the QUEUE pill carries no count badge. The mockup " +
                                 "draws a badge on every panel and the DIGIT is the meaning (the owner is " +
                                 "red/green colourblind, so a coloured disc alone says nothing)");
                if (ws2.Contains("private void BuildQueueDoor("))
                    failures.Add("[queue-door-in-workspace] the renderer has grown a queue face again. The door " +
                                 "is the HOST's top-right pill; a second one in the body is the two-affordances " +
                                 "defect the 2026-09-06 14:59 capture showed, and its tab-row seat is what " +
                                 "truncated to 'QUEUE . FULL 5 O...'");
                if (!ws2.Contains("Queue = vm.Queue;"))
                    failures.Add("[queue-door-in-workspace] ManageWorkspacePanel no longer binds the queue model. " +
                                 "The host reads it from there so ONE composed projection serves the screen");
                string compose = Body(vm2, "private ManageQueueVM ComposeQueueDoor()",
                                      "private ManageActivityVM ComposeActivity()");
                if (compose == null)
                    failures.Add("[queue-door-in-workspace] could not locate ComposeQueueDoor's body - the " +
                                 "always-visible assertion cannot be evaluated, reported as a FAILURE");
                else if (!compose.Contains("Visible = true,") || !compose.Contains("Label = \"QUEUE\","))
                    failures.Add("[queue-door-in-workspace] ComposeQueueDoor no longer publishes an unconditionally " +
                                 "visible QUEUE door. Gating it on queue contents is what strands it while idle - " +
                                 "the state the owner was actually in when she captured this screen");
            }

            // ── 11 [rows-inside-the-plate] — WO-1488, MEASURED ──────────────────────────────────
            // ⛔ THE ROWS WERE SEATED ON THE DRAWER'S RECT AND THE PLAYER SEES THE PLATE.
            // EVIDENCE, Builds/ui-capture/ManageFlow_BUILD_queue_2670x1200.png (18:39): row 2's
            // title, its CANCEL and its progress bar all paint BELOW the gold frame. And the log
            // from the same round says the list was healthy:
            //     MANAGE_QUEUE_LIST seats 2 whole rows: 292px of 307px  (Builds, r24)
            // Both are true. The whole-row trim measures the list against ITSELF and the list was
            // never wrong - it was seated 12px above the drawer's rect floor (`_drawerListY0 =
            // gap`) while frames/content-panel draws SLICED with a 96px 9-slice border
            // (content-panel.png.meta: spriteBorder {96,96,96,96}), so the visible interior floor
            // is ~96px higher. Two whole rows, ~84px of them outside the frame. THE RECT AND THE
            // ART WERE DIFFERENT RECTS, which is why every existing case passed over it.
            //
            // ⚠ THE REFERENCE HEIGHT IS MEASURED, NOT ASSUMED. well = 579px, from the r24 line
            // `MANAGE_QUEUE_BANDS drawer=475px` divided by the 0.82 span that then rendered. The
            // old wellRef 533 in case 8 above is a DIFFERENT, older capture and is left alone;
            // mixing them is how this file's numbers drift.
            //
            // RED MUTATIONS (each fails this case):
            //   * put `_drawerListY0 = gap;` back in SetDrawerBands
            //   * drop the `Mathf.Min(..., 1f - plate)` ceiling term
            //   * raise DrawerOverlayY1 back to 0.84 (the X's overlay row stops fitting)
            //   * set DrawerOverlayY0 negative (the drawer hangs over CLOSE through 96px of
            //     transparent margin - a visible button the drawer's raycast swallows)
            //   * restore `FitSingleLine(state, 0f, QueueLineFontPx)` (the 30px kit floor, which
            //     is what ellipsised "11m 0s left (0% do...")
            {
                // ⭐ WO-1567 ROUND 25 - RE-MEASURED, BECAUSE THE WELL GREW. It was 579 ("r24
                // drawer=475px / 0.82 span"), and that reading was correct for a well that still
                // reserved the shared CLOSE band on every screen. WO-1567 round 25 reclaimed it:
                // CLOSE is rendered on the HUB ALONE (WO-1491), so the body floor is now the
                // frame's own inner edge and the well is
                //   (WorkspaceHeaderY0 0.838 - WorkspaceBodyFloorY 0.020) x 927 = 758 ref px
                // where 927 is the reference panel height the r24 log itself pins:
                //   580 = (0.838 - 0.050 - 0.020) x panelPx - CanonCtaHeight(132) -> panelPx = 927.
                // ⚠ A YARDSTICK, NOT A CLAIM ABOUT EVERY DEVICE - the RATIOS below are the pin.
                const float wellPx = 758f;
                // ⚠ THE FLOOR ITSELF, NOT A COPY OF IT. This read `112f` with the real name in a
                // trailing comment, which is the duplicated state CLAUDE.md keeps paying for - and
                // this file already has the `using` for it.
                const float minTouch = ElarionUiKit.MinTouchPx;
                float y0 = ConstOf(panel, "DrawerOverlayY0"), y1 = ConstOf(panel, "DrawerOverlayY1");
                float inset = ConstOf(panel, "DrawerPlateInsetPx");
                float overlay = ConstOf(panel, "DrawerTitleOverlayPx");
                float tabsBand = ConstOf(panel, "DrawerTabsPx");
                float bandGap = ConstOf(panel, "DrawerBandGapPx");
                float rowPx = ConstOf(panel, "RowHeightPx");
                float stateFloor = ConstOf(panel, "QueueStateFontFloorPx");
                // ⚠ READ IN THIS SCOPE TOO. The round-26 band sum below reached for the `titlePx`
                // that belongs to [drawer-clear-of-card]'s block above (CS0103). It is the SAME
                // ConstOf read - which resolves DrawerTitlePx through its DrawerTitleOverlayPx
                // alias - so the two cases agree by construction rather than by a shared local.
                float titlePx = ConstOf(panel, "DrawerTitlePx");
                if (y0 < 0f || y1 < 0f || inset < 0f || overlay < 0f || tabsBand < 0f ||
                    bandGap < 0f || rowPx < 0f || stateFloor < 0f || titlePx < 0f)
                    failures.Add("[rows-inside-the-plate] could not read the overlay's rect / plate / timer " +
                                 "constants off the source - a scoped assertion that cannot find its scope " +
                                 "FAILS, it never passes vacuously");
                else
                {
                    float drawerPx = (y1 - y0) * wellPx;
                    // floor = the plate's inner edge; ceiling = the tab row's underside.
                    // ⭐ WO-1567 ROUND 26 - THE TITLE BAND IS PART OF THE SUM NOW. It was omitted
                    // here because DrawerTitlePx was 0 and the header was drawn above the ceiling;
                    // it is inside the drawer since the TEXT OFF PLATE failures, so a sum that
                    // leaves it out over-reports the list by a whole row.
                    float listPx = drawerPx - inset - titlePx - tabsBand - 2f * bandGap;
                    if (listPx < rowPx + 20f)
                        failures.Add("[rows-inside-the-plate] the overlay's list band is " + listPx +
                                     "px INSIDE THE PLATE at well=" + wellPx + " - under the " + (rowPx + 20f) +
                                     "px one row plus scroll padding needs, so not one row is fully visible at " +
                                     "rest. Grow the overlay; never push the band back over the frame art");
                    // ⭐ WO-1567 ROUND 25 - RE-POINTED WITH THE RULING, NOT RELAXED. WHAT THIS CASE
                    // DEFENDS IS UNCHANGED: the title overlay must have a real band to render in,
                    // or the X slides back into the tab strip (where the capture caught it reading
                    // as a fourth tab) or spills off the panel. What changed is WHERE that band
                    // comes from. It used to be the slice of the WELL the ceiling left free, which
                    // cost the rows 21% of the overlay; the overlay now reaches the well's ceiling
                    // (DrawerOverlayY1 = 1) and its title takes the SHARED CHROME ROW's band -
                    // WorkspaceHeaderY0..Y1 - which ApplyDrawerPlacement stands down while the
                    // overlay is up. That is also what mockup panel 8 draws: one heading, the word
                    // QUEUE, with no back arrow and no queue pill beside it.
                    // ⛔ SO THE ROOM IS THE SUM OF BOTH, AND THE STANDDOWN IS PART OF THE PIN. A
                    // ceiling of 1.0 WITHOUT the chrome hidden would print QUEUE straight through
                    // "MANAGE - BUILD" and the queue pill - the BUTTON OVER TEXT failure this
                    // screen has already paid for twice - so the two are asserted together.
                    // ⛔ RE-POINTED AGAIN 2026-09-07 (WO-1567 round 26). This used to assert that
                    // the ceiling left ROOM ABOVE ITSELF for the title, because the title was drawn
                    // there. It is drawn INSIDE now (TEXT OFF PLATE, six failures on
                    // Builds/cap-manage-wave5.log), so "room above the ceiling" is the wrong
                    // question and asking it would forbid the fix. What the case still defends is
                    // the same thing in the new shape: the header must have a real rect that seats
                    // a MinTouchPx X, and it must NOT reach above the body's black plate.
                    if (overlay > titlePx + 0.5f)
                        failures.Add("[rows-inside-the-plate] the title zone is " + overlay +
                                     "px but only " + titlePx + "px is reserved for it inside the drawer, so " +
                                     "it overhangs the tab row beneath - or, at a 1.0 ceiling, the body plate " +
                                     "above. DrawerTitlePx and DrawerTitleOverlayPx are the same band and " +
                                     "must stay the same number");
                    if (y1 > 1.001f)
                        failures.Add("[rows-inside-the-plate] the overlay's ceiling (" + y1 + ") reaches ABOVE " +
                                     "the well. Every row and the header would then be measured against a " +
                                     "layout.body ZoneBacking they no longer sit on - RULE 1 [text-off-plate], " +
                                     "which is what the round-25 pivot-0 header failed by 112px on all three " +
                                     "*_queue frames");
                    if (y1 > 0.999f &&
                        (!panel.Contains("bool chromeHidden = _queueDrawerOpen && !band;") ||
                         !panel.Contains("_tabsHost.gameObject.SetActive(!chromeHidden)") ||
                         !panel.Contains("_workspaceTitle.gameObject.SetActive(!chromeHidden)")))
                        failures.Add("[rows-inside-the-plate] the overlay reaches the well's ceiling but the " +
                                     "shared chrome no longer stands down under it. Its title band is then " +
                                     "the SAME band as the back arrow, the breadcrumb title and the queue " +
                                     "pill - and the breadcrumb is not even in ManageHeaderActions (it is " +
                                     "chrome.title, in the kit frame's Zone_Header), so hiding the row alone " +
                                     "is not enough. Both writers, or neither.");
                    if (overlay < minTouch)
                        failures.Add("[rows-inside-the-plate] the title overlay is " + overlay +
                                     "px and cannot seat a MinTouchPx (" + minTouch + ") X. That is the exact " +
                                     "reason the X was in the tab band at 56px");
                    if (y0 < 0f)
                        failures.Add("[rows-inside-the-plate] the overlay hangs below the well (" + y0 +
                                     "). The plate's bottom " + inset + "px is TRANSPARENT margin, so the shared " +
                                     "CLOSE renders through it while the drawer's raycast eats the tap");
                    if (stateFloor < 20f || stateFloor > 26f)
                        failures.Add("[rows-inside-the-plate] the queue timer's autosize floor is " + stateFloor +
                                     "px. Below ElarionUiKit.FontHardFloor (20) the kit clamps it back up and the " +
                                     "line ellipsises again; above ~26 the longest queued string " +
                                     "(\"Queued - 3rd in line (12h 30m of work)\", ~24px) still clips");
                }
                if (panel.Contains("_drawerListY0 = gap"))
                    failures.Add("[rows-inside-the-plate] the row band is seated off the drawer's RECT again " +
                                 "(_drawerListY0 = gap). The player sees the sliced PLATE, whose interior is " +
                                 inset + "px inside that rect - this is the 2026-09-06 row-2 overhang exactly");
                if (!panel.Contains("_drawerListY0 = plate;") ||
                    !panel.Contains("Mathf.Min(_drawerTabsY0 - gap, 1f - plate)"))
                    failures.Add("[rows-inside-the-plate] the list band is not bounded by the plate on BOTH " +
                                 "edges. A row can cross the frame's top as easily as its bottom");
                if (!panel.Contains("plateSprite.border"))
                    failures.Add("[rows-inside-the-plate] the plate inset is no longer MEASURED off the live " +
                                 "sprite's 9-slice border. A copy of 96 in this file is a copy of a number that " +
                                 "lives in content-panel.png.meta - the duplicated state this screen keeps " +
                                 "paying for");
                if (!panel.Contains("FitSingleLine(state, QueueStateFontFloorPx, QueueLineFontPx)"))
                    failures.Add("[rows-inside-the-plate] the queue row's timer line is not fitted to its own " +
                                 "floor. `0f` resolves to the kit's FontFloor (30) against a 32px max - two " +
                                 "points of headroom, which is what truncated \"11m 0s left (0% do...\"");
                if (Count(panel, "DrawerOverlayY0") < 3 || Count(panel, "DrawerOverlayY1") < 3)
                    failures.Add("[rows-inside-the-plate] the overlay's rect is not read from the shared " +
                                 "constants by BOTH writers. BuildQueueDrawer authored -0.25..0.99 and " +
                                 "ApplyDrawerPlacement 0.02..0.84, and the r24 log carries both drawer heights " +
                                 "(719px and 475px) - one rect, two numbers, and the estimate described neither");
            }

            // ── 12 [door-opens-on-the-hub] — WO-1573 (2026-09-07) ───────────────────────────────
            // ⛔ THE PILL FIRED AND THE PLAYER SAW NOTHING, AND EVERY LOG SAID IT WORKED.
            //
            // PROVEN AT SOURCE (ManageScreenPanel.cs, read 2026-09-07):
            //   * `BuildQueueDrawer(well)` parents ManageQueueDrawer to `well`, and `well` is
            //     `_operationalWell` (`_operationalWell = well;` in the same build method).
            //   * `ApplyScreenVisibility` held that well at `SetActive(!_hubShowing)`.
            //   * the QUEUE pill is built on `_tabsHost`, which hangs off `chrome.content` - a
            //     SIBLING of the well - and NOTHING deactivates it on the hub.
            // So on the hub the pill was live, `ToggleQueueDrawer` ran, `_queueDrawerOpen` flipped
            // and `_queueDrawer.SetActive(true)` landed on a CHILD OF AN INACTIVE PARENT. Nothing
            // rendered, nothing was tappable, and the existing trace still printed
            // "queue drawer expanded" because it reads the FLAG.
            //
            // ⚠ THIS IS THE DEAD-SUBTREE FAULT ApplyScreenVisibility'S OWN HEADER ALREADY RECORDS
            // (`content=0px`: ShowLauncher deactivated the WELL and RenderWorkspace re-activated
            // only the HOST). It was fixed for the workspace host and left open for the overlay,
            // because until the hub could open the queue nobody had walked that path. One flag,
            // one writer - and the writer must re-assert the well with EVERYTHING it contains.
            //
            // RED mutations this case catches:
            //   - restore `_operationalWell.gameObject.SetActive(!_hubShowing);`
            //   - drop the `ApplyScreenVisibility()` call from ToggleQueueDrawer
            //   - leave the hub's card grid up under the overlay
            //   - have ToggleQueueDrawer SetActive the well itself (a second writer)
            string visibility = Body(panel, "private void ApplyScreenVisibility()", "private void ShowWorkspace()");
            if (visibility == null)
                failures.Add("[door-opens-on-the-hub] could not locate ApplyScreenVisibility's body - the scoped " +
                             "assertion cannot be evaluated, so it FAILS rather than passing vacuously");
            else
            {
                if (!visibility.Contains("SetActive(!_hubShowing || _queueDrawerOpen)"))
                    failures.Add("[door-opens-on-the-hub] ⛔ ApplyScreenVisibility does not keep the operational well " +
                                 "alive while the queue overlay is open. ManageQueueDrawer is a CHILD of that well, so " +
                                 "the QUEUE pill on the hub flips the flag, activates the drawer inside an inactive " +
                                 "ancestor, and opens NOTHING - while the trace still reports 'queue drawer expanded' " +
                                 "(WO-1573)");
                if (!visibility.Contains("_launcherHost.gameObject.SetActive(_hubShowing && !_queueDrawerOpen)"))
                    failures.Add("[door-opens-on-the-hub] the hub's card grid is left active under the queue overlay - " +
                                 "an opaque modal carrying DESTRUCTIVE and PAID verbs over a live card grid is the " +
                                 "WO-1368 mis-tap surface, on the hub this time");
                // ⚠ NOT PINNED HERE: the hub's shared CLOSE. It stays `SetActive(_hubShowing)` and
                // is COVERED by the overlay rather than gated (DrawerOverlayY0 = -0.25 of the well
                // spans the Close band, and the drawer's opaque Image eats the raycast). Its exact
                // text is pinned by ManageMockupConformanceRegression.cs:718, so gating it is that
                // suite's edit, not this one's - and it needs a capture first.
            }
            if (!panel.Contains("drawer.SetParent(well, false)"))
                failures.Add("[door-opens-on-the-hub] the queue drawer is no longer parented to the operational well - " +
                             "the ancestor this case guards has moved, so re-derive the fix before deleting the case");
            if (toggle == null)
                failures.Add("[door-opens-on-the-hub] could not locate ToggleQueueDrawer's body - reported as a FAILURE");
            else
            {
                if (!toggle.Contains("ApplyScreenVisibility();"))
                    failures.Add("[door-opens-on-the-hub] ToggleQueueDrawer does not re-assert the screen's visibility, " +
                                 "so the overlay is switched on inside whatever hierarchy the previous screen left " +
                                 "behind - and on the hub that hierarchy is inactive");
                if (toggle.Contains("_operationalWell.gameObject.SetActive"))
                    failures.Add("[door-opens-on-the-hub] ToggleQueueDrawer writes the well's active flag directly. " +
                                 "ApplyScreenVisibility is the SINGLE writer of that flag - two writers deciding one " +
                                 "piece of state by last-write-wins is the whole defect this screen already paid for");
                if (toggle.IndexOf("ApplyScreenVisibility();", StringComparison.Ordinal) >
                    toggle.IndexOf("_queueDrawer.SetActive(_queueDrawerOpen)", StringComparison.Ordinal))
                    failures.Add("[door-opens-on-the-hub] the drawer is activated BEFORE its ancestors are re-asserted, " +
                                 "so the layout pass ApplyDrawerPlacement and RenderQueueDrawer both measure against " +
                                 "never runs on the opening frame");
                // The instrument, pinned. `activeSelf` was true throughout the defect; only
                // `activeInHierarchy` tells the two states apart, and CLAUDE.md 12 forbids
                // removing the trace once a system has been stabilised by it.
                if (!toggle.Contains("MANAGE_QUEUE_DOOR") || !toggle.Contains("activeInHierarchy"))
                    failures.Add("[door-opens-on-the-hub] the MANAGE_QUEUE_DOOR trace (or its activeInHierarchy " +
                                 "measurement) is gone. Without it a dead-ancestor overlay reads as 'expanded' in " +
                                 "every log, which is how this defect survived being captured");
            }

            reason = failures.Count == 0
                ? "MANAGE_QUEUE_DRAWER_OK tower choices lead; queue administration is opt-in AND REACHABLE " +
                  "(Finish Now / Ad / Cancel / Move up are built in the drawer, never in the browse list); " +
                  "early villagers teach exact paths"
                : "MANAGE_QUEUE_DRAWER_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }

        /// <summary>A control is only real when its FACE and its COMMAND are both present - a
        /// button wired to nothing and a command no button calls both read as "shipped".</summary>
        private static void RequirePair(string panel, List<string> failures, string face, string command, string what)
        {
            if (!panel.Contains(face))
                failures.Add("[verbs-exist] the " + what + " control face " + face + " is not constructed anywhere");
            else if (!panel.Contains(command))
                failures.Add("[verbs-exist] the " + what + " face exists but nothing invokes " + command +
                             " - the control would render and do nothing");
        }

        /// <summary>Source between <paramref name="from"/> and the next <paramref name="until"/>,
        /// or null when either marker is absent. Deliberately null-on-miss: a scoped assertion that
        /// cannot find its scope must FAIL, not pass silently on an empty string.</summary>
        private static string Body(string src, string from, string until)
        {
            int a = src.IndexOf(from, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(until, a + from.Length, StringComparison.Ordinal);
            return b < 0 ? null : src.Substring(a, b - a);
        }

        /// <summary>WO-1393: read a `private const float NAME = 123f;` off the source, or -1.
        /// The arithmetic is replayed from the LIVE constants, never from a copy in this file.</summary>
        private static float Const(string src, string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(src,
                @"\b" + name + @"\s*=\s*([0-9]+(?:\.[0-9]+)?)f");
            return m.Success && float.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out float v) ? v : -1f;
        }

        /// <summary>Reads a `private const float NAME = 0.123f;` off the source, or -1. The
        /// arithmetic is replayed from the LIVE constants, never from a copy in this file - the same
        /// stance Const() takes for the band px, and the reason this suite can compare two seats
        /// without knowing either number.</summary>
        /// <summary>
        /// The value of a named const in the source, or -1 when it cannot be read (every caller
        /// treats -1 as a FAILURE, never as a pass - a scoped assertion that cannot find its scope
        /// must not pass vacuously).
        /// <para>⭐ WO-1567 ROUND 25 - IT ALSO RESOLVES A CONST AUTHORED AS ANOTHER CONST. Two of
        /// these bands are now written <c>= ElarionUiKit.MinTouchPx;</c> rather than as a copied
        /// 112f, which is the right way round: the band's ONLY constraint is the touch floor, so it
        /// should follow the floor rather than restate it. Without this arm the numeric regex would
        /// miss them and this suite would report "could not read" on the very authoring style the
        /// repo's duplicated-state rule asks for.</para>
        /// </summary>
        private static float ConstOf(string src, string name) { return ConstOf(src, name, 0); }

        private static float ConstOf(string src, string name, int depth)
        {
            if (depth > 3) return -1f;                   // an alias cycle is a miss, never a hang
            var m = System.Text.RegularExpressions.Regex.Match(src,
                @"\b" + name + @"\s*=\s*([0-9]+(?:\.[0-9]+)?)f");
            if (m.Success && float.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                return v;
            if (System.Text.RegularExpressions.Regex.IsMatch(src,
                    @"\b" + name + @"\s*=\s*ElarionUiKit\.MinTouchPx\s*;"))
                return ElarionUiKit.MinTouchPx;
            // ⭐ ONE HOP TO A NAMED CONST. DrawerTitlePx is authored `= DrawerTitleOverlayPx;`
            // deliberately - the title BAND and the title ZONE must be the same number, and the way
            // to guarantee that is to have one number, not two that agree today.
            var alias = System.Text.RegularExpressions.Regex.Match(src,
                @"\b" + name + @"\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\s*;");
            if (alias.Success && !string.Equals(alias.Groups[1].Value, name, StringComparison.Ordinal))
                return ConstOf(src, alias.Groups[1].Value, depth + 1);
            return -1f;
        }

        private static int Count(string src, string needle)
        {
            int n = 0, i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
