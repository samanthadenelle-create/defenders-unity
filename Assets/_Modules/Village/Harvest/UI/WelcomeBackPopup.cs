using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.HudModel;   // WO-1408 -- PostureSignals: the raid gate, army fill and Heartfire
using DeNelle.Core.UI;

namespace DeNelle.Village.UI
{
    /// <summary>
    /// One-tap summary of the whole away window: the authoritative offline haul, Echo
    /// mending, the QUEUE JOBS THAT FINISHED, and what the collectors are holding.
    ///
    /// <para>LANE G -- THE RETURNING SESSION ACTUALLY REPORTS. The economy map
    /// (docs/PROGRAM_RAID_ECONOMY_2026-09-04.md sec.7) opens the ideal returning session on
    /// two beats this screen could not say: "BUILD COMPLETE -&gt; collect" and
    /// "Resources full -&gt; collect". Measured at source before the change: the gate here
    /// read <c>result.Total &lt;= 0 &amp;&amp; !result.HasMendNews</c>, so a player who
    /// finished three builds overnight and accrued no node harvest got NO SCREEN AT ALL, and
    /// the COLLECT button called <see cref="Dismiss"/> -- a button labelled with a verb that
    /// performed no verb.</para>
    ///
    /// <para>! THIS IS THE ONE RETURN-TIME SURFACE. Do NOT add a second popup for jobs or
    /// for collectors; rows go HERE. It is already registered with
    /// <see cref="PanelManager"/>, already suppressed during combat AND deferred until a hub
    /// scene is active (never over Title -- owner felt-test 2026-09-04) by
    /// OfflineHarvestService.TryShowPopup, and a second screen would have to re-earn
    /// all of it.</para>
    /// </summary>
    public sealed class WelcomeBackPopup : MonoBehaviour
    {
        private static WelcomeBackPopup s_active;

        /// <summary>
        /// WO-1414 D -- TRUE while a welcome-back report is ON SCREEN. Read by
        /// <c>OfflineHarvestService.Update</c> (to re-park a report that opened before the
        /// mandatory tutorial chain went live) and by <c>TutorialFlow.TickStepClock</c> (to
        /// exclude modal-held seconds from the STEP-STUCK budget).
        /// <para>
        /// Sourced from <c>s_active</c>, never from a mirrored bool: the same one-owner rule
        /// <c>TutorialFlow.AwaitedDialogueSignal</c> is written to. <see cref="Dismiss"/>
        /// and <see cref="OnDestroy"/> both clear it, so it can never latch true after teardown.
        /// </para>
        /// </summary>
        public static bool IsOpen => s_active != null;

        /// <summary>
        /// WO-1414 D -- the result the OPEN report is showing, or null when nothing is open.
        /// The re-park path needs the on-screen result itself: <c>OfflineHarvestService._lastResult</c>
        /// can be a later re-measure of the same window (see the smaller-window guard in
        /// <see cref="Show"/>), and parking that instead would release a different report than the
        /// one that was taken away.
        /// </summary>
        public static OfflineHarvestResult ActiveResult => s_active != null ? s_active._result : null;

        private OfflineHarvestResult _result;
        private ElarionUiKit.ObsidianModal _modal;
        private PanelHandle _panelHandle;
        private bool _open;

        /// <summary>WO-1408 -- the optional rows and the ready line, decided by the pure VM.
        /// Built once in <see cref="BuildUi"/>; the View never re-decides a destination.</summary>
        private WelcomeBackDoorsVM _doors;

        /// <summary>True once the FINISHED WHILE AWAY door row has been drawn, so
        /// <see cref="AddCompletedJobRows"/> does not say the same thing a second time.</summary>
        private bool _finishedRowDrawn;

        // -- Row geometry, shared by every row builder below --------------------
        /// <summary>Height of one data row in body-normalized units.</summary>
        private const float RowH = 0.095f;

        /// <summary>Gap under a data row.</summary>
        private const float RowGap = 0.012f;

        /// <summary>Rows stop here. Below this the body has run out and a row would be drawn
        /// off the bottom of the modal (the report can already carry seven lines -- see the
        /// COLLECT seating note further down).</summary>
        private const float MinRowY = 0.06f;

        /// <summary>At most this many finished jobs get their own row; the rest collapse into
        /// one aggregate line, so a long night can never push the report off-screen.</summary>
        private const int MaxJobRows = 3;

        /// <summary>At most this many per-resource collector rows (the rail has four resources;
        /// the body also has to hold the haul + job rows), the rest collapse into one "+N MORE" line.</summary>
        private const int MaxCollectorRows = 4;

        /// <summary>WO-1408 -- a DOOR row is more than twice a data row's height, and the number
        /// is a measurement, not taste. The body band is 0.22..0.82 of a modal that is 0.84 of the
        /// screen, so body height = 0.60 x 0.84 x screenH. At 1200px that is ~605px, and a
        /// <see cref="RowH"/> row is ~57px -- under HALF the project's MinTouchPx (112). At 0.21
        /// the plate is ~127px (at a 1080-tall screen, ~114px), and the door face fills it
        /// EXACTLY (anchors 0..1), so the face clears the floor on both. A door the player cannot
        /// reliably hit is the same cul-de-sac this ticket exists to close, one layer down.</summary>
        private const float DoorRowH = 0.21f;

        private static bool HasRoom(float y) => y - RowH >= MinRowY;

        /// <summary>Room for a DOOR row (taller than a data row -- see <see cref="DoorRowH"/>).</summary>
        private static bool HasDoorRoom(float y) => y - DoorRowH >= MinRowY;

        public static void Show(OfflineHarvestResult result)
        {
            // THE GATE LIVES ON THE RESULT, not here (OfflineHarvestResult.HasSummaryContent):
            // haul OR mend OR a finished job OR resources waiting to be collected. Re-deriving
            // it at this call site is what let the collector-only town fall through the crack.
            if (result == null || !result.HasSummaryContent)
            {
                FlowTrace.Step("Offline",
                    "welcome-back: no summary content (haul/mend/jobs/collectors all empty) -- not shown.");
                return;
            }
            // NEVER REPLACE A SHOWN REPORT WITH A SMALLER ONE (owner felt-test 2026-09-04 22:29:
            // "YOUR REALM WORKED FOR 0m" after 12.6h away). The cold-load and resume triggers
            // raced during boot; the second claim measured ~0s and this rebuild threw the real
            // 12h report away. OfflineHarvestService now latches the claim itself; this is the
            // belt to that brace: a later result covering LESS away time than the one on screen
            // is not news, it is the same window measured again.
            if (s_active != null && s_active._result != null && result.AwaySeconds < s_active._result.AwaySeconds)
            {
                FlowTrace.Warn("Offline",
                    $"welcome-back: a later result (away {result.AwaySeconds:0}s, haul={result.Total}) would REPLACE the " +
                    $"open report (away {s_active._result.AwaySeconds:0}s, haul={s_active._result.Total}) with a smaller " +
                    "window -- kept the first; the later result is a re-measure of the same window.");
                return;
            }
            FlowTrace.Step("Offline",
                $"welcome-back REVEAL: haul={result.Total} mendNews={result.HasMendNews} " +
                $"jobs={result.CompletedJobCount} collectorsPending={result.PendingCollectorTotal} " +
                $"across {result.PendingCollectorCount} collector(s).");
            if (s_active != null) s_active.Dismiss();
            var host = new GameObject("WelcomeBackPopup");
            var popup = host.AddComponent<WelcomeBackPopup>();
            s_active = popup;
            popup._result = result;
            popup.BuildUi();
        }

        private void BuildUi()
        {
            _modal = ElarionUiKit.BuildObsidianModal("WelcomeBackUI", "WELCOME BACK, KEEPER",
                new Vector2(0.18f, 0.08f), new Vector2(0.82f, 0.92f), Dismiss,
                sortingOrder: 32020, frameName: RpgUiCatalog.FrameCore);
            if (_modal == null || _modal.canvas == null) { Dismiss(); return; }
            MedievalUiSkin.ApplyShell(_modal.chrome, compact: false);

            // ONE BUTTON (owner felt-test 2026-09-04 22:30: "collect close over top of each other,
            // dont need both"). The kit's modal builder seats its own shared CLOSE face in the bottom
            // thumb band with no label/hook parameter, and COLLECT below is seated on the same
            // band -- so the two faces overprinted. COLLECT already dismisses; the shell's Close
            // face is hidden, not destroyed (the kit still owns it; the scrim + PanelManager back
            // path still reach Dismiss for a no-collect exit).
            if (_modal.chrome != null && _modal.chrome.close != null)
                _modal.chrome.close.gameObject.SetActive(false);

            if (_modal.chrome.layout != null && _modal.chrome.layout.body != null)
            {
                var bodyRect = _modal.chrome.layout.body;
                bodyRect.anchorMin = new Vector2(bodyRect.anchorMin.x, 0.22f);
                bodyRect.anchorMax = new Vector2(bodyRect.anchorMax.x, 0.82f);
                bodyRect.offsetMin = Vector2.zero;
                bodyRect.offsetMax = Vector2.zero;
            }

            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? (Transform)_modal.chrome.layout.body : _modal.chrome.content.transform;

            var summary = ElarionUiKit.Label(body, AwayText(), 0.86f, 0.98f,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Center,
                0.05f, 0.95f, bold: false);
            ElarionUiKit.FitSingleLine(summary);

            // WO-1408 -- decide the doors BEFORE anything is drawn. The four live signals are read
            // HERE, not at claim time, and deliberately: the reveal is deferred until a hub scene is
            // active (OfflineHarvestService.TryShowPopup), so the posture rail's values at claim time
            // can be a boot-default from a scene the player never saw. What the screen says about
            // readiness must be true at the moment it is on screen.
            Guard.Try("WelcomeBack", "decide the return-screen doors", () =>
                _doors = WelcomeBackDoorsVM.Build(_result,
                    PostureSignals.RaidCapable,
                    PostureSignals.ArmyFillUsed, PostureSignals.ArmyFillCap,
                    PostureSignals.HeartfireLit, PostureSignals.HeartfireMax));
            if (_doors == null) _doors = new WelcomeBackDoorsVM();
            FlowTrace.Step("WelcomeBack", _doors.TraceLine);

            float y = 0.82f;
            // WO-1434 -- the BANKED haul first (what already landed in the wallet during the
            // claim), then the one waiting-table. These are different facts and they are never
            // the same units: Grant() has already applied the haul, while the table below is
            // what COLLECT would move. On the owner's 2026-09-06 capture the haul was 0 on
            // every axis ("accrued over 13221s: worker-owned=0 node(s), total=0"), which is why
            // no haul row drew -- correctly.
            AddResourceRow(body, ref y, _result.AetherCrystals, "AETHER CRYSTALS");
            AddResourceRow(body, ref y, _result.Food, "STONE");
            AddResourceRow(body, ref y, _result.Iron, "IRON");
            AddResourceRow(body, ref y, _result.Wood, "WOOD");
            // WO-1408 -- THE DOORS COME BEFORE THE INFORMATIONAL LINES, and that ordering is the
            // fix, not a preference. The mend / collector / capped lines TELL; a door row LETS THE
            // PLAYER GO SOMEWHERE. When the body runs out, the thing that must survive is the one
            // that turns the return moment into a next move.
            AddDoorRows(body, ref y);
            AddMendRows(body, ref y);
            AddCompletedJobRows(body, ref y);
            AddCollectorRow(body, ref y);

            if (_result.WasCapped)
            {
                // WO-1434 - THE SUBJECT OF THIS SENTENCE WAS WRONG. `WasCapped` is
                // `window.ExceedsCap(OfflineCapHours)` (OfflineHarvestService:385) -- the AWAY
                // WINDOW hit its 10h ceiling. It says nothing about storage, and the old line
                // ("Storage filled while you were away. Check in sooner to keep every reward.")
                // named storage AND implied a reward had been taken away. Neither is true: the
                // window cap means later hours never accrued, and nothing that DID accrue is
                // ever discarded (see the proof block on OfflineHarvestService.BuildReturnRows).
                // WO-1499 (2026-09-06) -- THE HALF-MOVE IS NOW CLOSED. The header suffix in
                // AwayTextFor said "(STORAGE FULL)" off this SAME `WasCapped` bit; it now reads
                // "(AWAY LIMIT REACHED)" and the pin in AwaySummaryReportRegression case8 moved
                // with it. Body line and header suffix finally name the one subject that is true.
                var capped = ElarionUiKit.Label(body,
                    // No number here ON PURPOSE: the ceiling is OfflineHarvestService
                    // .OfflineCapHours, and a copy of it in this string is duplicated state that
                    // goes stale the day the storage ladder raises the window (offline-storage
                    // .json authors 10h/12h/16h/24h/36h per tier). Name the rule, not the value.
                    "Your realm gathers for a limited stretch while you are away. Nothing gathered is lost.",
                    Mathf.Max(0.03f, y - 0.12f), y, ElarionUi.Gold,
                    ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
                ElarionUiKit.FitBlock(capped, 26f, ElarionUi.FontMicro);
            }

            // This report can contain seven data lines; the generic footer zone is
            // re-seated above the shared Close reservation and lands in that data stack.
            // Seat the sole action directly in the shell's bottom thumb band instead.
            var collect = ElarionUiKit.BuildObsidianButton(_modal.chrome.content.transform, "COLLECT",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.37f, 0.045f), new Vector2(0.63f, 0.155f), CollectAndDismiss);
            MedievalUiSkin.ApplyButton(collect, primary: true);
            var face = collect != null ? collect.targetGraphic as Image : null;
            if (face != null) face.type = Image.Type.Simple;

            // WO-1408 -- the readiness line and its SMALL second door, beside COLLECT. Guarded:
            // a failure here must cost the player the RAID shortcut, never the whole report.
            Guard.Try("WelcomeBack", "seat the ready line and its door",
                () => AddReadyBand(_modal.chrome.content.transform));

            _open = true;
            _panelHandle = PanelManager.Register("Welcome Back", Dismiss, () => _open);
            if (!PanelManager.NotifyOpened(_panelHandle)) Dismiss();
        }

        private static void AddResourceRow(Transform body, ref float y, int amount, string label)
        {
            if (amount <= 0) return;
            const float h = 0.095f;
            var plate = ElarionUiKit.AddImage(body, "Reward_" + label,
                new Vector2(0.08f, y - h), new Vector2(0.92f, y),
                new Color(0.05f, 0.045f, 0.04f, 0.96f), rounded: false);
            var name = ElarionUiKit.Label(plate.transform, label, 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Left,
                0.05f, 0.70f, bold: false);
            var value = ElarionUiKit.Label(plate.transform, "+" + amount, 0f, 1f,
                ElarionUi.Gold, ElarionUi.FontLabel, TextAlignmentOptions.Right,
                0.70f, 0.95f, bold: true);
            ElarionUiKit.FitSingleLine(name); ElarionUiKit.FitSingleLine(value);
            y -= h + 0.012f;
        }

        private void AddMendRows(Transform body, ref float y)
        {
            var mend = _result != null ? _result.Mend : null;
            if (mend == null || !mend.HasContent) return;
            AddMendLine(body, ref y, EchoMendCopy.AwayMendedLine(mend), ElarionUi.Parchment);
            AddMendLine(body, ref y, EchoMendCopy.AwaySpentLine(mend), ElarionUi.ParchmentDim);
            AddMendLine(body, ref y, EchoMendCopy.AwayStallLine(mend), ElarionUi.Gold);
        }

        private static void AddMendLine(Transform body, ref float y, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            const float h = 0.09f;
            var label = ElarionUiKit.Label(body, text, y - h, y, color,
                ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.07f, 0.93f, bold: true);
            ElarionUiKit.FitBlock(label, 24f, ElarionUi.FontMicro);
            y -= h + 0.01f;
        }

        // =====================================================================
        //  LANE G -- the two rows the returning session was missing
        // =====================================================================

        /// <summary>
        /// "BUILD COMPLETE -&gt; collect" (economy map sec.7 beat 1). One row per finished
        /// queue job, capped at <see cref="MaxJobRows"/> with an aggregate line for the rest.
        /// <para>The verb and the label come from the SHARED card seam
        /// (BuildTimerService.EntryFor, carried on OfflineHarvestResult.OfflineJobLine), so
        /// the away summary says the same words the queue card said while the job was
        /// running -- never a second vocabulary.</para>
        /// </summary>
        private void AddCompletedJobRows(Transform body, ref float y)
        {
            var jobs = _result != null ? _result.CompletedJobs : null;
            if (jobs == null || jobs.Count == 0) return;
            // WO-1408 -- the FINISHED WHILE AWAY door row already named these jobs AND gave them
            // somewhere to go. Drawing the per-job plates underneath it would print the same fact
            // twice, which is exactly the six-lines-for-three-facts defect WO-1434 measured on the
            // owner's device one screen up. The door row supersedes; this stays as the path for a
            // window whose door row could not be seated.
            if (_finishedRowDrawn)
            {
                FlowTrace.Step("WelcomeBack",
                    $"per-job rows skipped: the FINISHED WHILE AWAY door row already names {jobs.Count} job(s) " +
                    "and carries the Manage door.");
                return;
            }

            int rendered = 0;
            for (int i = 0; i < jobs.Count && rendered < MaxJobRows; i++)
            {
                var j = jobs[i];
                if (j == null) continue;
                if (!HasRoom(y)) break;
                string verb = string.IsNullOrEmpty(j.Verb) ? "COMPLETE" : j.Verb;
                string label = string.IsNullOrEmpty(j.Label) ? "JOB" : j.Label;
                AddPlateRow(body, ref y, verb + " COMPLETE", label, ElarionUi.Gold);
                rendered++;
            }

            int remaining = jobs.Count - rendered;
            if (remaining <= 0) return;
            if (HasRoom(y)) AddPlateRow(body, ref y, "ALSO FINISHED", "+" + remaining + " MORE", ElarionUi.Gold);
            else
                FlowTrace.Warn("Offline",
                    $"welcome-back: {remaining} finished job(s) had no room left in the report body " +
                    "(the haul + mend lines filled it) -- they are NOT lost, only unlisted.");
        }

        /// <summary>
        /// "Resources full -&gt; collect" (economy map sec.7 beat 2). What the collectors are
        /// STILL HOLDING -- this claim did not bank it, the COLLECT button does.
        /// <para>! COPY LAW (CollectorStatusGate's header): "Storage" / "Bank" belongs to the
        /// WALLET; this surface says COLLECTORS. The existing capped-haul line above does say
        /// STORAGE, and that is the same distinction rather than a contradiction: that line is
        /// about the wallet cap, this one is about collectors.</para>
        /// </summary>
        private void AddCollectorRow(Transform body, ref float y)
        {
            if (_result == null) return;
            if (!_result.HasCollectorNews && !_result.HasSiloNews) return;

            // =================================================================
            //  WO-1434 -- ONE ROW PER RESOURCE, AMOUNT AND DESTINY TOGETHER.
            // -----------------------------------------------------------------
            //  WHAT THIS METHOD USED TO DRAW, from the owner's device 2026-09-06
            //  (build 358161, screencap 12:50:59):
            //      WOOD  WAITING   +10609        <- this loop
            //      IRON  WAITING    +6365
            //      STONE WAITING   +25808
            //      Storage nearly full - 10609 wood will wait     <- AddCollectWaitRows
            //      Storage nearly full - 6365 iron will wait
            //      Storage nearly full - 25808 stone will wait
            //  Six lines for three facts, each integer printed twice, and every row
            //  wearing a reward's "+" while its ENTIRE amount was going to wait. She
            //  tapped COLLECT and banked ZERO ("[Flow:Eco] Grant +W0 +I0").
            //
            //  AND IT WAS DRAWING THREE OF FIVE. The Echo silo -- 57,600 units, at cap,
            //  the largest single thing that happened while she was away -- had no row
            //  here and no term in the reveal gate. It reached the player only AFTER the
            //  tap, in the harvest-result modal, described as "lost".
            //
            //  The table below is the fix: OfflineHarvestService.BuildReturnRows merges
            //  BOTH producers per resource and pairs each amount with what becomes of it,
            //  so there is one row per resource and no line repeats another's number.
            //  Nothing here is lost -- see the proof block on BuildReturnRows.
            // =================================================================
            List<OfflineHarvestService.ReturnRow> rows = null;
            Guard.Try("Offline", "build the welcome-back resource table",
                () => rows = OfflineHarvestService.BuildReturnRows(_result));

            // The old single "3 COLLECTORS WAITING +16716" line survives ONLY as the fallback for
            // a result that carries totals but no per-resource lines (older producers / an oracle
            // fixture that fills PendingCollectorTotal and nothing else).
            if (rows == null || rows.Count == 0)
            {
                if (!_result.HasCollectorNews || !HasRoom(y)) return;
                string left = _result.PendingCollectorCount == 1
                    ? "COLLECTOR WAITING"
                    : _result.PendingCollectorCount + " COLLECTORS WAITING";
                AddPlateRow(body, ref y, left, "+" + _result.PendingCollectorTotal, ElarionUi.Gold);
                return;
            }

            int rendered = 0, next = 0;
            while (next < rows.Count && rendered < MaxCollectorRows && HasRoom(y))
            {
                var r = rows[next];
                next++;
                string label = OfflineHarvestService.ReturnRowLabel(r);
                string destiny = OfflineHarvestService.ReturnRowDestiny(r);
                FlowTrace.Step("Offline",
                    $"welcome-back row: {r.Word} pending={r.Pending} (collectors {r.FromCollectors} + silo {r.FromSilo}) " +
                    $"headroom={r.Headroom} banks={r.Banks} stays={r.Waits} -> '{label}' | '{destiny}'.");
                // The value column is WIDE here because it carries a sentence, not a number.
                AddPlateRow(body, ref y, label, destiny, ElarionUi.Gold, valueSplit: 0.46f);
                rendered++;
            }

            // Whatever the row budget (or the body) could not seat collapses into one line.
            int remaining = 0, remainingUnits = 0;
            for (int i = next; i < rows.Count; i++) { remaining++; remainingUnits += rows[i].Pending; }
            if (remaining > 0)
            {
                if (HasRoom(y)) AddPlateRow(body, ref y, "ALSO WAITING", remainingUnits + " MORE", ElarionUi.Gold);
                else
                    FlowTrace.Warn("Offline",
                        $"welcome-back: {remaining} more resource row(s) ({remainingUnits} units) had no room left " +
                        "in the report body -- they stay where they are, only the row is unlisted.");
            }

            AddDestinyFooter(body, ref y, rows);
        }

        /// <summary>
        /// WO-1434 - the two sentences that belong to the TABLE rather than to any one row.
        /// <para>(1) What happens to everything that will not fit, said plainly and without the
        /// word "lost" - because nothing is lost, on either producer (proof block on
        /// OfflineHarvestService.BuildReturnRows).</para>
        /// <para>(2) `FOUNDATIONAL_RULINGS.md` section 7: a player earning nothing into a
        /// resource must be TOLD, in words. A silo at its ceiling means the Echoes have stopped
        /// gathering entirely, which no row can say - a half-full silo also has a waiting row and
        /// is still filling.</para>
        /// Words and layout, never hue: the owner is red/green colourblind.
        /// </summary>
        private void AddDestinyFooter(Transform body, ref float y,
                                      IReadOnlyList<OfflineHarvestService.ReturnRow> rows)
        {
            string stalled = OfflineHarvestService.SiloStalledLine(_result);
            if (!string.IsNullOrEmpty(stalled))
            {
                if (HasRoom(y)) AddMendLine(body, ref y, stalled, ElarionUi.Gold);
                else FlowTrace.Warn("Offline",
                    "welcome-back: the Echo-silo-full line had no room left in the report body -- the silo is " +
                    "still full and still gathering nothing; only the sentence is unlisted.");
            }

            string footer = OfflineHarvestService.ReturnFooterLine(rows);
            if (string.IsNullOrEmpty(footer)) return;
            if (!HasRoom(y))
            {
                FlowTrace.Warn("Offline",
                    $"welcome-back: the footer '{footer}' had no room left in the report body -- the units still " +
                    "stay where they are on COLLECT (never burned), only the sentence is unlisted.");
                return;
            }
            // !! THE FOOTER GETS ITS OWN, TALLER BAND - IT IS A SENTENCE, NOT A LINE.
            // Owner frame 2026-09-07 01:13 (Logs/device/screens/owner-harvest-20260907-011321.png)
            // ends mid-word: "...Spend, or upgrade stora". AddMendLine seats every line in a
            // 0.09-tall band and fits with FitBlock, which WRAPS then TRUNCATES - correct behaviour
            // (it can never shorten a reassurance into a lie) applied to a box only one line high.
            // The mend lines that share AddMendLine are short and still fit; this one is the only
            // full sentence on the screen, so it takes a two-line band of its own rather than
            // shrinking every other line to suit it.
            AddFooterSentence(body, ref y, footer);
        }

        /// <summary>Two lines of room for the one full sentence on this screen. Same fit helper as
        /// <see cref="AddMendLine"/> (wrap-then-truncate, never an ellipsis), twice the height.</summary>
        private static void AddFooterSentence(Transform body, ref float y, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            const float h = 0.19f;
            var label = ElarionUiKit.Label(body, text, y - h, y, ElarionUi.Gold,
                ElarionUi.FontMicro, TextAlignmentOptions.Top, 0.07f, 0.93f, bold: true);
            ElarionUiKit.FitBlock(label, 24f, ElarionUi.FontMicro);
            y -= h + 0.01f;
        }

        // =====================================================================
        //  WO-1408 -- THE NEXT DOOR
        // =====================================================================

        /// <summary>
        /// Draw the optional door rows the VM produced. Each is a plate carrying WHAT happened
        /// and ONE button that goes there.
        /// <para>⛔ ZERO ROWS IS THE NORMAL CASE and it must stay silent: no "nothing finished"
        /// line, no greyed door. A row exists only when it is true (see WelcomeBackDoorsVM).</para>
        /// </summary>
        private void AddDoorRows(Transform body, ref float y)
        {
            if (_doors == null || _doors.Rows.Count == 0) return;
            for (int i = 0; i < _doors.Rows.Count; i++)
            {
                var row = _doors.Rows[i];
                if (row == null) continue;
                if (!HasDoorRoom(y))
                {
                    FlowTrace.Warn("WelcomeBack",
                        $"the '{row.Label}' door row had no room left in the report body -- the player is " +
                        "returned to the HUD with no route to it, which is the WO-1408 defect recurring; " +
                        "the fact itself is not lost (the panel it points at still holds it).");
                    continue;
                }
                float rowTop = y;
                var captured = row;
                if (Guard.Try("WelcomeBack", "build the '" + row.Label + "' door row",
                        () => BuildDoorRow(body, rowTop, captured))
                    && captured.TraceKind == "finished")
                    _finishedRowDrawn = true;
                y -= DoorRowH + RowGap;
            }
        }

        /// <summary>One door row: label + detail stacked on the left, the door on the right.</summary>
        private void BuildDoorRow(Transform body, float top, WelcomeBackDoorRow row)
        {
            var plate = ElarionUiKit.AddImage(body, "Door_" + row.Label,
                new Vector2(0.08f, top - DoorRowH), new Vector2(0.92f, top),
                new Color(0.05f, 0.045f, 0.04f, 0.96f), rounded: false);

            var label = ElarionUiKit.Label(plate.transform, row.Label, 0.52f, 0.94f,
                ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Left,
                0.04f, 0.64f, bold: true);
            ElarionUiKit.FitSingleLine(label);

            if (!string.IsNullOrEmpty(row.Detail))
            {
                var detail = ElarionUiKit.Label(plate.transform, row.Detail, 0.06f, 0.48f,
                    ElarionUi.Gold, ElarionUi.FontMicro, TextAlignmentOptions.Left,
                    0.04f, 0.64f, bold: false);
                ElarionUiKit.FitSingleLine(detail);
            }

            // The door fills the plate's FULL height (0..1) ON PURPOSE: DoorRowH was sized so that
            // a full-height face clears MinTouchPx, and insetting it inside the plate gives the
            // measurement straight back (0.88 of a 127px plate is 112px at 1200 and 100px at 1080
            // -- the exact miss this constant exists to avoid).
            var door = ElarionUiKit.BuildObsidianButton(plate.transform, row.DoorText,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.66f, 0f), new Vector2(0.97f, 1f),
                () => CollectThenRoute(row));
            MedievalUiSkin.ApplyButton(door, primary: false);
            var doorFace = door != null ? door.targetGraphic as Image : null;
            if (doorFace != null) doorFace.type = Image.Type.Simple;
        }

        /// <summary>
        /// The one line above COLLECT plus the SMALL second door beside it. Seated in the shell's
        /// bottom band (content space), NOT in the body: the body can already be full of rows and
        /// the readiness line is chrome for the action, not another data row.
        /// </summary>
        private void AddReadyBand(Transform content)
        {
            if (_doors == null || !_doors.HasReadyDoor) return;

            var line = ElarionUiKit.Label(content, _doors.ReadyLine, 0.168f, 0.215f,
                ElarionUi.Gold, ElarionUi.FontMicro, TextAlignmentOptions.Center,
                0.06f, 0.94f, bold: true);
            ElarionUiKit.FitSingleLine(line);

            // SAME y band as COLLECT (0.045..0.155), to its right. COLLECT keeps its exact
            // geometry -- it is pinned by AwaySummaryReportRegression case 5 as the literal
            // `new Vector2(0.63f, 0.155f), CollectAndDismiss`, and it stays the PRIMARY.
            var raid = ElarionUiKit.BuildObsidianButton(content, _doors.ReadyDoorText,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.68f, 0.045f), new Vector2(0.90f, 0.155f),
                CollectThenRouteReady);
            MedievalUiSkin.ApplyButton(raid, primary: false);
            var raidFace = raid != null ? raid.targetGraphic as Image : null;
            if (raidFace != null) raidFace.type = Image.Type.Simple;
        }

        /// <summary>The ready door's tap: same one path as a row door, on the VM's ready target.</summary>
        private void CollectThenRouteReady()
        {
            if (_doors == null) { Dismiss(); return; }
            CollectThenRoute(new WelcomeBackDoorRow
            {
                Label = "READY",
                DoorText = _doors.ReadyDoorText,
                Door = _doors.ReadyDoor,
                TraceKind = _doors.ReadyKind,
            });
        }

        /// <summary>
        /// ⭐ THE ONE PATH: collect first, then route. Never a second collect command and never a
        /// second navigation mechanism.
        ///
        /// <para>⚠ THE ORDER LOOKS BACKWARDS AND IS NOT. The route is ARMED on
        /// <see cref="PanelManager.SetReturnDoor"/> BEFORE the collect runs, because the collect
        /// is not silent: ResourceCollectorService.CollectAll opens a HarvestOverflowModal batch
        /// (ResourceCollectorService.cs:152/164), which registers with the SAME exclusive arbiter.
        /// A synchronous PanelRouter.Open here would therefore swap that result modal off the
        /// screen the instant it appeared -- the player would tap COLLECT-and-go and never see
        /// what they collected.
        ///
        /// The return-door arbiter (WO-1400) already solves exactly this: a SWAP keeps the door,
        /// and it FIRES on the first close-to-nothing after the WO-1393 grace. So the destination
        /// opens when the harvest result is dismissed, and immediately when there was no result
        /// modal to show. One mechanism, the existing one -- no coroutine, no "open next frame"
        /// timer, no second navigation path to keep in sync.</para>
        /// </summary>
        private void CollectThenRoute(WelcomeBackDoorRow row)
        {
            if (row == null) { Dismiss(); return; }
            var door = row.Door;
            string context = row.DoorContext;
            string what = row.DoorText;

            PanelManager.SetReturnDoor("Welcome Back door: " + what, () =>
            {
                bool opened = string.IsNullOrEmpty(context)
                    ? PanelRouter.Open(door)
                    : PanelRouter.Open(door, context);
                if (opened)
                    FlowTrace.Step("WelcomeBack",
                        $"door '{what}' routed to {door}" +
                        (string.IsNullOrEmpty(context) ? "" : " tab '" + context + "'") + ".");
                else
                    FlowTrace.Fail("WelcomeBack",
                        $"door '{what}' could NOT open {door} -- nothing is registered for that id, or the " +
                        "open was refused; the player is left on the HUD, which is the WO-1408 defect.");
            });

            FlowTrace.Step("WelcomeBack",
                $"door '{what}' tapped (kind={row.TraceKind}) -> collect first, then {door}" +
                (string.IsNullOrEmpty(context) ? "" : " tab '" + context + "'") + ".");

            Dismiss();        // arms the return door (close-to-nothing)
            PerformCollect(); // may open the harvest-result modal, which KEEPS the armed door
        }

        /// <summary>The shared two-column plate row: label left, value right. Extracted so a
        /// job row and a collector row cannot drift from a haul row.
        /// <para>WO-1434 - <paramref name="valueSplit"/> is where the value column starts (0..1
        /// across the plate). The default 0.70 suits a NUMBER; a WO-1434 destiny row's value is a
        /// short sentence ("258 FITS, 10351 STAYS") and needs the wider 0.46, or FitSingleLine
        /// shrinks it to unreadable on a phone. Layout carries meaning here, so the split is a
        /// parameter rather than a second row builder that could drift.</para></summary>
        private static void AddPlateRow(Transform body, ref float y, string left, string right, Color rightColor,
                                        float valueSplit = 0.70f)
        {
            if (string.IsNullOrEmpty(left)) return;
            var plate = ElarionUiKit.AddImage(body, "Row_" + left,
                new Vector2(0.08f, y - RowH), new Vector2(0.92f, y),
                new Color(0.05f, 0.045f, 0.04f, 0.96f), rounded: false);
            float split = Mathf.Clamp(valueSplit, 0.30f, 0.90f);
            // The label column ends AT the split for the default (0.70), byte-identical to the
            // pre-WO-1434 geometry the job/haul rows were tuned against; only a WIDE value column
            // takes the 0.02 gutter, where the two texts would otherwise touch.
            float labelEnd = split >= 0.60f ? split : split - 0.02f;
            var name = ElarionUiKit.Label(plate.transform, left, 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Left,
                0.05f, labelEnd, bold: false);
            // A number gets the display face; a sentence gets the body face, or FitSingleLine
            // shrinks it past legibility on a phone. Keyed off the split so a caller cannot pick
            // a wide column and a display font together by accident.
            int valueFont = split < 0.60f ? ElarionUi.FontMicro : ElarionUi.FontLabel;
            var value = ElarionUiKit.Label(plate.transform, right ?? string.Empty, 0f, 1f,
                rightColor, valueFont, TextAlignmentOptions.Right,
                split, 0.95f, bold: true);
            ElarionUiKit.FitSingleLine(name); ElarionUiKit.FitSingleLine(value);
            y -= RowH + RowGap;
        }

        /// <summary>
        /// The COLLECT button's verb, finally performed. It carries the tap to the EXISTING
        /// command -- <see cref="CollectorStatusGate.RequestCollectAll"/>, the same seam the
        /// ambient HUD collectors chip taps -- and then dismisses. No second collect command is
        /// minted here, and this screen never touches a collector directly.
        /// <para>With no Village-side listener installed (a boot race) the tap must still close
        /// the screen rather than dead-end, and it says so in the trace.</para>
        /// </summary>
        private void CollectAndDismiss()
        {
            PerformCollect();
            Dismiss();
        }

        /// <summary>
        /// WO-1408 -- the collect VERB on its own, extracted so the door faces perform the SAME
        /// one command before they route. Deliberately NOT a second collect path: this is the
        /// original body of <see cref="CollectAndDismiss"/>, which still calls it, so there is one
        /// implementation and the button and the doors cannot drift.
        /// </summary>
        private void PerformCollect()
        {
            // WO-1434 - THE SILO IS A REASON TO COLLECT TOO. This gate read HasCollectorNews
            // alone, so a town whose collectors were empty but whose Echo silo was FULL got a
            // COLLECT button that only dismissed -- and ResourceCollectorService.CollectAll is
            // the very call that dumps the silo (it wraps `echo.DumpSilos()`). Same verb, same
            // one command; only the gate was too narrow.
            if (_result != null && (_result.HasCollectorNews || _result.HasSiloNews))
            {
                if (CollectorStatusGate.HasSubscriber)
                {
                    FlowTrace.Step("Offline",
                        "welcome-back COLLECT -> CollectorStatusGate.RequestCollectAll (" +
                        _result.PendingCollectorTotal + " waiting across " +
                        _result.PendingCollectorCount + " collector(s), plus " +
                        _result.SiloTotal + " in the Echo silo).");
                    Guard.Try("Offline", "welcome-back collect-all",
                        () => CollectorStatusGate.RequestCollectAll());
                }
                else
                {
                    FlowTrace.Warn("Offline",
                        "welcome-back COLLECT tapped but no Village listener is installed on " +
                        "CollectorStatusGate -- nothing banked; the pending is untouched and the " +
                        "collectors chip can still bank it.");
                }
            }
            else
            {
                FlowTrace.Step("Offline",
                    "welcome-back COLLECT tapped with nothing pending -- no collect command raised. " +
                    "(The CLOSE is the caller's: CollectAndDismiss dismisses, a door routes.)");
            }
        }

        private string AwayText() => AwayTextFor(_result.AwaySeconds, _result.WasCapped);

        /// <summary>
        /// The whole summary line, exposed for the away-summary oracle.
        /// <para>
        /// WO-1499 (2026-09-06): the suffix names the AWAY WINDOW, not storage. `wasCapped` is
        /// `window.ExceedsCap(OfflineCapHours)` (OfflineHarvestService:386) -- the away window hit
        /// its ceiling, so later hours never accrued. It is not a bank-full signal, and the old
        /// "(STORAGE FULL)" wording sent the player to upgrade storage for a ceiling that was TIME.
        /// No hour number in the string ON PURPOSE (same reason as the body line above): the
        /// ceiling is tiered in offline-storage.json, so a copy here is duplicated state.
        /// A genuine bank-full state is said elsewhere in its own words --
        /// OfflineHarvestService.ReturnRowDestiny emits "STORAGE FULL - STAYS PUT" per return row.
        /// </para>
        /// </summary>
        public static string AwayTextFor(double awaySeconds, bool wasCapped)
        {
            string span = FormatAwaySpan(awaySeconds);
            return wasCapped ? $"YOUR REALM WORKED FOR {span} (AWAY LIMIT REACHED)" : $"YOUR REALM WORKED FOR {span}";
        }

        /// <summary>
        /// Away span as hours AND minutes (owner ruling 2026-09-04 22:29: "minutes and hours if
        /// applicable should show"). Deterministic, ASCII, floor-based, and it can never print
        /// "0m": under a minute reads "under 1m".
        /// <code>
        ///   45328 s -> "12h 35m"     59 s -> "under 1m"     3600 s -> "1h 0m"
        ///   90000 s -> "1d 1h"       2100 s -> "35m"
        /// </code>
        /// Days carry hours only (a day-scale absence does not need its minutes).
        /// </summary>
        public static string FormatAwaySpan(double awaySeconds)
        {
            if (double.IsNaN(awaySeconds) || awaySeconds < 0.0) awaySeconds = 0.0;
            long total = (long)System.Math.Floor(awaySeconds);
            if (total < 60L) return "under 1m";
            long days = total / 86400L;
            long hours = (total % 86400L) / 3600L;
            long minutes = (total % 3600L) / 60L;
            if (days >= 1L) return days + "d " + hours + "h";
            if (hours >= 1L) return hours + "h " + minutes + "m";
            return minutes + "m";
        }

        /// <summary>
        /// WO-1414 A -- close the open report, if any. The popup is code-built and owns its own
        /// host object, so an outside owner has no other way to reach it. Safe when nothing is
        /// open -- a no-op that still traces, so a capture can tell "nothing was open" from "the
        /// hook never ran".
        /// <para>
        /// TWO callers, both in <c>OfflineHarvestService</c>, and the trace says which via
        /// <paramref name="why"/>:
        /// (A) the New Game hook -- a summary on screen when START NEW is pressed was measured on
        /// the PREVIOUS save; (D) the mandatory-tutorial re-park -- the report is still about THIS
        /// save and is re-parked in <c>_deferredReveal</c>, so the caller reads
        /// <see cref="ActiveResult"/> before calling this.
        /// </para>
        /// ⚠ THE MESSAGE BELOW DELIBERATELY DOES NOT NAME A SAVE. It asserted "on the previous
        /// save" when WO-1414 A was the only caller; under caller (D) that sentence is false, and
        /// a trace line that states a fact it cannot know is the §11B failure this repo pays for.
        /// </summary>
        public static void DismissIfOpen(string why)
        {
            var open = s_active;
            if (open == null)
            {
                FlowTrace.Step("Offline", $"welcome-back DismissIfOpen({why}): nothing open.");
                return;
            }
            double away = open._result != null ? open._result.AwaySeconds : 0.0;
            FlowTrace.Step("Offline",
                $"welcome-back DISMISSED ({why}): the open report covered {away:0}s of away time.");
            open.Dismiss();
        }

        private void Dismiss()
        {
            _open = false;
            if (_panelHandle != null) { PanelManager.NotifyClosed(_panelHandle); _panelHandle = null; }
            if (_modal != null && _modal.canvas != null)
            {
                if (Application.isPlaying) Destroy(_modal.canvas); else DestroyImmediate(_modal.canvas);
            }
            _modal = null;
            if (s_active == this) s_active = null;
            if (gameObject != null)
            {
                if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
            }
        }

        private void OnDestroy()
        {
            _open = false;
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            if (s_active == this) s_active = null;
        }
    }
}
