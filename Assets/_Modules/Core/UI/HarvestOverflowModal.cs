using System.Collections.Generic;
using TMPro;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;

namespace DeNelle.Core.UI
{
    /// <summary>Phone-safe, modal-owned presentation for one player-chosen harvest batch.</summary>
    public sealed class HarvestOverflowModal : MonoBehaviour
    {
        private static HarvestOverflowModal _active;
        private ElarionUiKit.ObsidianModal _modal;
        private PanelHandle _panel;
        private WorldHold.Handle _hold;

        /// <summary>
        /// WO-1392 - the <see cref="BankOverflowStatus.Source"/> tag of a row produced by the
        /// COLLECTOR sweep (ResourceCollectorService.BuildCollectorRows). Those rows report units
        /// that are STILL WAITING in the collectors - nothing was burned - so the body copy for
        /// them says "waiting", never "lost". Every other source (the Echo silo dump's
        /// TownBankCapacity clamp, the offline haul) still burns its overflow today and keeps
        /// the "lost" sentence.
        /// </summary>
        public const string CollectorSource = "Collectors";

        /// <summary>
        /// WO-1434 - the <see cref="BankOverflowStatus.Source"/> of a row produced by the ECHO
        /// SILO dump. It is the WARN SCOPE tag (BankOverflowToastPresenter stamps it), NOT a
        /// ClampGrant sourceTag: every silo row reaches the bank through
        /// EconomyService.GrantSpendable and therefore arrives tagged "Grant", which is why the
        /// old substring test on "DumpSilos" never matched a single row on the device.
        /// <para>THESE ROWS DO NOT BURN EITHER. EchoService.DumpSilos settles against the APPLIED
        /// basket (`s.SiloResources -= bankedFromSilo`), so what the cap refused stays in the silo
        /// and banks on the next dump. Proven on the owner's Seeker 2026-09-06: "silo dump: 28800
        /// wood stayed in the silo - Wood storage full", and pool 57600 was unchanged across three
        /// dumps (12:51:25, 12:56:03, 12:56:06).</para>
        /// </summary>
        public const string SiloSource = "EchoService.DumpSilos";

        // WO-1392 - ONE screen per tap. CollectAll banks the collectors AND dumps the Echo silo,
        // and each half used to reach Present on its own (the silo through its warn scope), so
        // the second call closed the first. While a batch is open, Present only QUEUES; the
        // outermost EndBatch presents everything once, collector rows first.
        private static readonly List<BankOverflowStatus> _batch = new List<BankOverflowStatus>();
        private static int _batchDepth;
        private static string _batchTag;

        /// <summary>Open a presentation batch (nesting counted; dispose it - prefer <c>using</c>).</summary>
        public static Batch BeginBatch(string tag) => new Batch(tag);

        /// <summary>True while a batch is open (the oracle seam for [one-modal-per-tap]).</summary>
        public static bool BatchOpen => _batchDepth > 0;

        /// <summary>Rows queued in the open batch (the oracle seam; empty outside a batch).</summary>
        public static IReadOnlyList<BankOverflowStatus> BatchedRows => _batch;

        public struct Batch : System.IDisposable
        {
            private bool _open;
            internal Batch(string tag)
            {
                _open = true;
                if (_batchDepth == 0) { _batch.Clear(); _batchTag = string.IsNullOrEmpty(tag) ? "?" : tag; }
                _batchDepth++;
            }
            public void Dispose()
            {
                if (!_open) return;
                _open = false;
                if (_batchDepth > 0) _batchDepth--;
                if (_batchDepth == 0) EndBatch();
            }
        }

        private static void EndBatch()
        {
            if (_batch.Count == 0) { _batchTag = null; return; }
            var rows = new List<BankOverflowStatus>(_batch);
            _batch.Clear();
            FlowTrace.Step("Bank", $"harvest-result batch [{_batchTag ?? "?"}] closing with {rows.Count} row(s) -> one modal.");
            _batchTag = null;
            Present(rows);
        }

        public static void Present(IReadOnlyList<BankOverflowStatus> results)
        {
            if (results == null || results.Count == 0) return;
            if (_batchDepth > 0)
            {
                for (int i = 0; i < results.Count; i++) _batch.Add(results[i]);
                FlowTrace.Step("Bank", $"harvest-result: {results.Count} row(s) queued into batch [{_batchTag ?? "?"}].");
                return;
            }
            if (!Application.isPlaying) return;
            if (_active != null) _active.Close();
            var host = new GameObject("HarvestOverflowModalHost");
            DontDestroyOnLoad(host);
            _active = host.AddComponent<HarvestOverflowModal>();
            _active.Open(results);
        }

        private void Open(IReadOnlyList<BankOverflowStatus> results)
        {
            // WO-1471: PLAYER-OWNED, not the bounded default. The player dismisses this modal at
            // their own pace, so elapsed time is not evidence of a leak - the 180s ceiling fired
            // under an open modal (device 12:51:25 -> 12:53:06, 101s of WORLD CLOCK FROZEN in
            // Main_Castle_Overworld). The probe reuses the SAME liveness expression this Open
            // passes to PanelManager.Register below: "does its owner still exist", never "is this
            // old". The Acquire precedes the modal build, but Open is synchronous inside Present,
            // so no watchdog tick can observe _modal null (the probe is polled, not evaluated here).
            _hold = WorldHold.AcquirePlayerOwned("harvest-overflow-result",
                () => this != null && _modal != null && _modal.canvas != null);
            _modal = ElarionUiKit.BuildObsidianModal("HarvestOverflowUI", "HARVEST RESULT",
                new Vector2(0.16f, 0.08f), new Vector2(0.84f, 0.92f), Close,
                sortingOrder: 31020);
            MedievalUiSkin.ApplyShell(_modal.chrome, compact: false);
            _panel = PanelManager.Register("Harvest Result", Close,
                () => _modal != null && _modal.canvas != null && _modal.canvas.activeInHierarchy);
            if (!PanelManager.NotifyOpened(_panel)) { Close(); return; }

            var content = _modal.chrome.content.transform;
            // WO-1525 - ROWS, NOT PROSE. The VM decides every number, word and door
            // (HarvestResultVM); this modal only draws them. The old single-Label body is gone:
            // it was eleven lines of paragraph for three resources, which is the defect.
            var vm = HarvestResultVM.Build(results, BuiltContainers);
            BuildRows(content, vm);
            // BuildObsidianModal already owns the one shared Close face. A second content
            // button here occupied the same lower band and produced the doubled CLOSE seen
            // in the owner's harvest-result capture.
            // WO-1370 §12 - the modal's OWN numbers are traced, so a screenshot of unreadable copy
            // can be checked against what the code actually put on the screen (the previous trace
            // logged only a row COUNT, which proved nothing about legibility).
            var trace = new System.Text.StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (i > 0) trace.Append(" | ");
                trace.Append($"{r.ResourceName} granted={r.Granted}/{r.Requested} lost={r.Lost} " +
                             $"store={r.Current}/{r.Max} overCap={r.OverCap}");
            }
            FlowTrace.Step("Bank",
                $"harvest-result modal OPEN with {results.Count} aggregated resource row(s): {trace}");
            // WO-1525 - ONE extra line, the VM's own shape summary. Deliberately NOT the long-form
            // BuildBody copy: a multi-line Step floods the F8 inbox harvester, which greps per line.
            FlowTrace.Step("Bank", $"harvest-result rows: {vm.TraceLine}");
            // (!) THE LINE THE 2026-09-07 CAPTURE DID NOT HAVE (CLAUDE.md section 12).
            // The two traces above carry the INPUT numbers and a row COUNT; neither proves what
            // text reached the player, which is why the owner's frame could only be read by eye.
            // ONE grep for "harvest-result screen" now returns the EXACT strings, and
            // HarvestResultShapeRegression asserts the same strings equal the banked deltas.
            FlowTrace.Step("Bank", $"harvest-result screen: {vm.ScreenText}");
            // The merge is the WO-1525b fix; say out loud when it actually folded something, so a
            // regression that re-splits the rows is visible in one line rather than as a "+N more".
            if (vm.SourceStatusCount != vm.TotalRowCount)
                FlowTrace.Step("Bank",
                    $"harvest-result: {vm.SourceStatusCount} producer status(es) merged to " +
                    $"{vm.TotalRowCount} resource row(s) - the welcome-back screen sums the same " +
                    "producers per resource (OfflineHarvestService.BuildReturnRows), so the two " +
                    "screens now report one figure per resource.");
        }

        // -- WO-1525: the row renderer --------------------------------------------
        // Geometry lives HERE and nowhere else. The VM has no opinion about anchors, and the
        // fractions below are the sister screen's proven shape (WelcomeBackPopup's door rows):
        // a plate per resource, the door drawn at the plate's FULL height rather than inset - that
        // inset is what WelcomeBackPopup's own DoorRowH comment records as giving the touch
        // measurement straight back. HarvestResultVM.MaxRows (3) is DERIVED from the four constants
        // below; change one and re-derive it there. The pixel heights are owed to a capture.

        /// <summary>Top of the row band, in modal-content fractions.</summary>
        private const float RowsTop = 0.86f;
        /// <summary>Floor of the row band when only the footer sits below it.</summary>
        private const float RowsFloor = 0.30f;
        /// <summary>Floor of the row band when a "+N more" line sits below it too.</summary>
        private const float RowsFloorWithOverflow = 0.36f;
        /// <summary>Gap between plates.</summary>
        private const float RowGap = 0.02f;
        /// <summary>A plate never grows past this, however few rows there are - a two-line row
        /// stretched over half the modal reads as an error state, not as emphasis.</summary>
        private const float RowHeightMax = 0.20f;

        private void BuildRows(Transform content, HarvestResultVM vm)
        {
            if (vm == null || vm.Rows.Count == 0) return;

            float floor = string.IsNullOrEmpty(vm.OverflowLine) ? RowsFloor : RowsFloorWithOverflow;
            int n = vm.Rows.Count;
            float band = RowsTop - floor;
            float h = Mathf.Min(RowHeightMax, (band - RowGap * n) / n);
            float y = RowsTop;

            for (int i = 0; i < n; i++)
            {
                var row = vm.Rows[i];
                if (row == null) continue;
                float top = y;
                Guard.Try("Bank", "draw the '" + row.ResourceName + "' harvest row",
                    () => BuildRow(content, top, h, row));
                y -= h + RowGap;
            }

            if (!string.IsNullOrEmpty(vm.OverflowLine))
            {
                var more = ElarionUiKit.Label(content, vm.OverflowLine, floor - 0.055f, floor - 0.01f,
                    ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Left,
                    0.08f, 0.92f, bold: false);
                ElarionUiKit.FitSingleLine(more, ElarionUi.FontFloorMobile, ElarionUi.FontMicro);
            }

            if (!string.IsNullOrEmpty(vm.FooterLine))
            {
                // ONCE, for the whole screen. The old body said this sentence per resource.
                //
                // (!) THE VARIABLE IS NAMED `label` ON PURPOSE, AND THE FIT IS A REAL ONE.
                // TownBankCapRegression [clamped-grant-warns] (TownBankCapRegression.cs:461-463)
                // does a SOURCE-TEXT scan of this file for the literal
                // "FitBlock(label, ElarionUi.FontFloorMobile" and for the ABSENCE of any direct
                // ellipsis overflow-mode assignment in this file (the exact token is deliberately
                // NOT spelled out here - writing it even inside a comment trips the scan, which is
                // itself evidence of what a source-text lint can and cannot see). It is the
                // readable-floor guarantee for the one WRAPPING block on this screen, which after
                // WO-1525 is this footer sentence (the
                // rows are single-line fields and take FitSingleLine, which the kit gives the same
                // 30px floor). FitBlock is the correct helper here regardless of the oracle: it
                // wraps and TRUNCATES rather than ellipsizing, so the reassurance can never be
                // shortened into a lie. Do not rename this variable to satisfy a linter's opposite
                // - and do not weaken the oracle; that it pins a variable NAME rather than the
                // behaviour is flagged in WORK_ORDER_1525's RESULT for the lead.
                var label = ElarionUiKit.Label(content, vm.FooterLine, 0.225f, 0.29f,
                    ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.TopLeft,
                    0.08f, 0.92f, bold: false);
                ElarionUiKit.FitBlock(label, ElarionUi.FontFloorMobile, ElarionUi.FontMicro);
            }
        }

        /// <summary>One resource plate: name + banked on top, the store bar under it, the waiting
        /// figure beneath, and the single action chip on the right at full plate height.</summary>
        private void BuildRow(Transform content, float top, float h, HarvestResultRow row)
        {
            var plate = ElarionUiKit.AddImage(content, "HarvestRow_" + row.ResourceName,
                new Vector2(0.06f, top - h), new Vector2(0.94f, top),
                new Color(0.05f, 0.045f, 0.04f, 0.96f), rounded: false);

            float leftEnd = row.HasAction ? 0.62f : 0.96f;

            // THE NAME - the row's identity, upper case, top-left. Position + weight carry the
            // hierarchy here; nothing is distinguished by hue (the owner is colourblind).
            // ⛔ THE FOUR BANDS BELOW ARE APPORTIONED SO EACH SEATS THE 30 px FontFloor LINE
            // (WO-1658). Do not shrink one to buy room for another without redoing the sum.
            // MEASURED, not inferred — 2026-09-10_0943_363722_logcat.txt, the session whose
            // `[Flow:Bank] harvest-result rows: ... shown=3 ... footer='reassure'` fixes n=3 with
            // no "+N more" line, so h = min(RowHeightMax, (0.86 - 0.30 - 0.02*3) / 3) = 0.1667 of
            // a ~805 px content rect = a ~134 px plate. At that plate the OLD fractions resolved:
            //   bar value 0.30..0.50 = 0.20 -> `rect 742x26 ... floor 30 -> 22, fontSize now 24`
            //   waiting   0.03..0.27 = 0.24 -> `rect 742x32 ... floor 30 -> 26, fontSize now 29`
            // i.e. the store line shipped at 24 px and the waiting line at 29 px. THE ROOT CAUSE
            // IS THE ONE THE GUARD'S OWN COMMENT NAMES: a band authored as a fraction OF a
            // fraction. The guard was concealing it, not saving it (0 post-check iterations,
            // stillBlank=0).
            // THE THRESHOLD: the guard relaxes when floor(h / lineFactor) - 1 < FontFloor, so a
            // band is legal at h >= (FontFloor + 1) * lineFactor = 31 * 1.1499 = 35.65 px.
            // lineFactor is the font's own faceInfo.lineHeight / pointSize — ElarionLocaleFallback
            // .asset is 73.59375 / 64 = 1.1499, the 1.15 the device logged.
            // THE NEW SUM on a 134 px plate (>= 0.29 buys 38.9 px, comfortably past 35.65):
            //   waiting 0.01..0.30 = 0.29 -> 38.9 px   bar 0.31..0.60 = 0.29 -> 38.9 px
            //   name    0.61..0.99 = 0.38 -> 50.9 px   banked 0.59..1.00 = 0.41 -> 54.9 px
            // THE COST, stated rather than hidden: name keeps FontLabel(40) at full size (needs
            // 46 px), banked renders ~47 instead of FontBody(50) (which needs 57.5). Both stay
            // far above the floor and the hierarchy (banked largest, then name) is preserved.
            // ⚠ RESIDUAL, unfixed and reported in WO-1658's RESULT: when a "+N more" line exists
            // the floor moves to RowsFloorWithOverflow and h = (0.50 - 0.06)/3 = 0.1467 -> a
            // ~118 px plate, where 0.29 buys only 34 px and the guard relaxes to 28. Three 36 px
            // bands plus gaps do not fit 118 px, so no fraction closes that case — it needs the
            // row band itself to grow, which collides with the footer sentence at 0.225..0.29.
            var name = ElarionUiKit.Label(plate.transform, row.ResourceName.ToUpperInvariant(),
                0.61f, 0.99f, ElarionUi.Parchment, ElarionUi.FontLabel,
                TextAlignmentOptions.Left, 0.04f, leftEnd * 0.62f, bold: true);
            // Every label on this plate carries the DOCUMENTED mobile floor explicitly
            // (ElarionUi.FontFloorMobile = 30): the kit's default is the same number today, and
            // writing it out is what stops a later default change from silently making the harvest
            // screen sub-legible. Past the floor these single-line fields ellipsize INSIDE the kit
            // (FitSingleLine's own contract) rather than shrinking into unreadable text.
            ElarionUiKit.FitSingleLine(name, ElarionUi.FontFloorMobile, ElarionUi.FontLabel);

            // THE BIG NUMBER - what BANKED. The largest glyph on the plate on purpose: it is the
            // one figure the player came to read.
            var banked = ElarionUiKit.Label(plate.transform, row.BankedText, 0.59f, 1f,
                ElarionUi.Gold, ElarionUi.FontBody, TextAlignmentOptions.Right,
                leftEnd * 0.62f, leftEnd, bold: true);
            ElarionUiKit.FitSingleLine(banked, ElarionUi.FontFloorMobile, ElarionUi.FontBody);

            // THE STORE - a bar whose value label carries BOTH figures AND the state WORD.
            var bar = ElarionUiKit.Bar(plate.transform, ElarionUiKit.BarKind.Castle,
                // WO-1658: 0.31..0.60, NOT the old 0.30..0.50.
                // ⚠ THE BAR'S BAND *IS* THE VALUE LABEL'S BAND — that is why this line is the
                // driver. `ElarionUiKit.Bar` (ElarionUiKit.cs:2114) builds its track as
                // `Well(parent, anchorMin, anchorMax)` -> `AddImage(parent, "Well", …)`
                // (ElarionUiKit.cs:157, which is the `Well` in the device path
                // `HarvestRow_Wood/Well/Label`), then parents the value label at 0..1 of it
                // (ElarionUiKit.cs:2136-2139). So the label inherits these two anchors exactly.
                // ⛔ Grow the band HERE, at the caller — never those kit-side 0..1 anchors, which
                // every `Bar` in the game reads. (This is `ElarionUiKit.Bar`, NOT the Obsidian
                // kit's `BuildObsidianBar`, whose root is named `ObsidianBar_<kind>`; the "Well"
                // in the logged path is what tells the two apart.)
                new Vector2(0.04f, 0.31f), new Vector2(leftEnd, 0.60f), withValue: true);
            if (bar != null)
            {
                if (bar.fill != null) bar.fill.fillAmount = row.Fill01;
                if (bar.valueLabel != null)
                {
                    bar.valueLabel.text = row.StorageText;
                    ElarionUiKit.FitSingleLine(bar.valueLabel, ElarionUi.FontFloorMobile, ElarionUi.FontLabel);
                }
            }

            // THE SECOND NUMBER - what waits (or, on the burn path, what was lost).
            if (!string.IsNullOrEmpty(row.WaitingText))
            {
                var waiting = ElarionUiKit.Label(plate.transform, row.WaitingText, 0.01f, 0.30f,
                    ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Left,
                    0.04f, leftEnd, bold: false);
                ElarionUiKit.FitSingleLine(waiting, ElarionUi.FontFloorMobile, ElarionUi.FontMicro);
            }

            if (!row.HasAction) return;

            // THE DOOR. Full plate height ON PURPOSE - see the MinTouchPx note on
            // HarvestResultVM.MaxRows; insetting it gives the measurement straight back.
            var captured = row;
            // !! TWO LINES, NEVER AN ELLIPSIS (owner device 2026-09-07 01:14,
            // Logs/device/screens/owner-screen-20260907-011426.png). The chip was drawn with
            // FitSingleLine, whose contract ellipsizes past the mobile floor, and the frame shows
            // "UPGRADE LUMBER..." and "UPGRADE STONEYA..." while the shorter "UPGRADE FOUNDRY" fit.
            // A truncated chip names a building that does not exist, on the one control whose whole
            // job is to say WHERE TO GO - so the break point is AUTHORED by the VM (ActionVerb /
            // ActionTarget) and drawn on two lines at the same readable floor. FitBlock wraps and
            // TRUNCATES rather than ellipsizing, so a face that still cannot fit loses whole words
            // instead of turning a name into a lie.
            string chipFace = string.IsNullOrEmpty(row.ActionTarget)
                ? row.ActionText
                : row.ActionVerb + "\n" + row.ActionTarget;
            var chip = ElarionUiKit.BuildObsidianButton(plate.transform, chipFace,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.65f, 0f), new Vector2(0.97f, 1f),
                () => Route(captured));
            MedievalUiSkin.ApplyButton(chip, primary: false);
            var face = chip != null ? chip.targetGraphic as UnityEngine.UI.Image : null;
            if (face != null) face.type = UnityEngine.UI.Image.Type.Simple;
            var chipLabel = chip != null ? chip.GetComponentInChildren<TMP_Text>() : null;
            if (chipLabel != null)
            {
                chipLabel.text = chipFace;
                ElarionUiKit.FitBlock(chipLabel, ElarionUi.FontFloorMobile, ElarionUi.FontLabel);
            }
        }

        /// <summary>
        /// Open the row's door. Close is NOT called first: PanelRouter's own post-open verify
        /// reads the modal arbiter, and destroying this host before it runs would make a good
        /// open report as the WO-465 invisible-scrim failure. The arbiter closes this card when
        /// the destination registers as open.
        /// </summary>
        private void Route(HarvestResultRow row)
        {
            if (row == null || !row.HasAction) return;
            bool opened = PanelRouter.Open(row.ActionDoor, row.ActionContext);
            if (!opened)
            {
                FlowTrace.Warn("Bank",
                    $"harvest-result: the '{row.ActionText}' door onto '{row.ActionDoor}' did not open " +
                    "- the player is left on the result card with no route to the fix (WO-1525).");
                return;
            }
            Close();
        }

        /// <summary>
        /// How many storage containers of <paramref name="r"/> are already BUILT - the one live
        /// signal the VM needs, and the only thing that decides BUILD versus UPGRADE. Derived from
        /// the single reader (TownBankCapacity.Apportion), never a second count: the base store
        /// slot is always present and is not a container.
        /// </summary>
        private static int BuiltContainers(BankResource r)
        {
            int built = 0;
            Guard.Try("Bank", "count built storage containers for " + r, () =>
            {
                var ap = TownBankCapacity.Apportion(r);
                if (ap.Slots == null) return;
                for (int i = 0; i < ap.Slots.Length; i++)
                    if (!ap.Slots[i].IsBaseStore) built++;
            });
            return built;
        }

        /// <summary>
        /// WO-1370 - the body copy, rebuilt so ONE resource reads as one paragraph.
        ///
        /// <para>THE DEFECT (owner, 2026-09-04, on a single-resource overflow): the old loop was
        /// written for a LIST and emitted three independent blocks separated by blank lines -
        /// "Stone" / "Collected: 0 of 90 | Uncollected: 90" / "Storage: 3000 / 3000..." / "Each
        /// uncollected amount was not added to storage." The resource WORD and its storage FIGURE
        /// sat in different blocks, so <c>3000 / 3000</c> had no visible owner and the owner could
        /// not tell what it referred to; and the trailing sentence said "Each" about a list of
        /// one.</para>
        ///
        /// <para>THE RULES THIS COPY HOLDS:
        /// (1) the resource NAME and ITS storage figure share the FIRST line, always;
        /// (2) the loss is named with the word "lost", not implied by a subtraction;
        /// (3) no redundant restatement after the list - each block is self-contained;
        /// (4) singular and plural both read correctly ("1 unit was" / "90 units were"), and the
        ///     sentence subject is never the resource noun itself, because "crystals was" and
        ///     "stone were" cannot both be right from one template;
        /// (5) ASCII ONLY - a non-ASCII dash or bullet renders as tofu on the device;
        /// (6) no meaning is carried by colour anywhere (the owner is red/green colourblind).</para>
        ///
        /// <para>(!) WO-1525 - THIS IS NO LONGER WHAT THE SCREEN DRAWS. The modal now renders
        /// <see cref="HarvestResultVM"/> rows (name + banked figure + storage bar + one door);
        /// this composer is retained because <c>HarvestResultCopyRegression</c> calls it directly
        /// and its WO-1370/1392/1434 law words are the record of what the prose had to say. Whether
        /// that suite is retired or rewritten against the VM is a LEAD RULING, not this lane's call
        /// - flagged in WORK_ORDER_1525's RESULT.</para>
        ///
        /// <para>(!) CORRECTED 2026-09-06 (WO-1525, CLAUDE.md section 11B): the paragraph here previously
        /// claimed the literals <c>Collected: ... of ...</c> and <c>Uncollected: ...</c> were
        /// "PINNED by TownBankCapRegression [clamped-grant-warns]". Read at source this session,
        /// that case (<c>TownBankCapRegression.cs:384-399</c>) asserts the load-bearing WARN FIRES
        /// on a clamped grant and that a full wallet banks 0 of 500 - it never inspects this copy.
        /// The strings that ARE pinned are pinned by <c>HarvestResultCopyRegression</c>, which calls
        /// <see cref="BuildBody"/> directly. A copied claim about which suite guards what is exactly
        /// the duplicated state sections 2/5/16 each describe.</para>
        /// </summary>
        public static string BuildBody(IReadOnlyList<BankOverflowStatus> results)
        {
            var blocks = new List<string>();
            for (int i = 0; i < results.Count; i++)
            {
                var s = results[i];
                string name = string.IsNullOrEmpty(s.ResourceName) ? "Resource" : s.ResourceName;
                string unit = name.ToLowerInvariant();

                // LINE 1 - the resource and ITS storage figure, on one line, in that order. This
                // is the whole fix: "3000 / 3000" can never again float free of the word it
                // describes. The state word is TEXT, never a tint.
                //
                // WO-1392 - the figure is the store AFTER this collect. BankOverflowStatus.Current
                // is the wallet BEFORE the grant was applied (that is what the clamp weighed), and
                // printing it raw put "Wood storage: 2021 / 4000" beside "414 lost" on the owner's
                // 2026-09-04 screen - 2021 + 414 < 4000, so the cap that bit looked like a lie.
                // The number the player can check against the rail is Current + Granted (= 4000).
                long after = (long)Mathf.Max(0, s.Current) + Mathf.Max(0, s.Granted);
                string state = s.OverCap ? " (over capacity)" : (after >= s.Max ? " (full)" : "");
                bool fromCollectors = string.Equals(s.Source, CollectorSource, System.StringComparison.Ordinal);
                bool fromSilo = !fromCollectors && !string.IsNullOrEmpty(s.Source)
                                && s.Source.IndexOf("DumpSilos", System.StringComparison.Ordinal) >= 0;
                string from = fromCollectors ? " from your collectors"
                    : (fromSilo ? " from the Echo silo" : "");
                var block = new List<string>
                {
                    $"{name} storage: {after} / {s.Max}{state}",
                    $"Collected: {s.Granted} of {s.Requested}{from}   |   Uncollected: {s.Lost}",
                };

                if (fromCollectors)
                {
                    // WO-1392 - NEVER BURN. A collector row's "uncollected" units are STILL IN THE
                    // COLLECTOR (ResourceCollector.Collect only drains what banked), so the copy
                    // says WAITING and names the real cap by its player word - the storage figure
                    // the rail shows - never "lost". Singular and plural authored separately.
                    block.Add(s.Lost == 1
                        ? $"That 1 {unit} is still waiting in your collectors - nothing was lost."
                        : $"Those {s.Lost} {unit} are still waiting in your collectors - nothing was lost.");
                    block.Add(s.OverCap
                        ? $"{name} storage {s.Max} is exceeded. It banks by itself once you spend below {s.Max} and collect again."
                        : $"{name} storage {s.Max} is full. Spend {unit}, or upgrade a {s.ContainerName}, then collect again.");
                }
                else if (fromSilo)
                {
                    // WO-1434 - NEVER BURN, SECOND PRODUCER. This branch is new because the
                    // silo's rows were falling into the "lost" branch below and telling the owner
                    // that 57,600 units had been destroyed while EchoService.DumpSilos was, in the
                    // same millisecond, logging that it had kept every one of them. Same shape as
                    // the collector branch above, different container word.
                    block.Add(s.Lost == 1
                        ? $"That 1 {unit} is still waiting in your Echo silo - nothing was lost."
                        : $"Those {s.Lost} {unit} are still waiting in your Echo silo - nothing was lost.");
                    block.Add(s.OverCap
                        ? $"{name} storage {s.Max} is exceeded. It banks by itself once you spend below {s.Max} and collect again."
                        : $"{name} storage {s.Max} is full. Spend {unit}, or upgrade a {s.ContainerName}, then collect again.");
                }
                else
                {
                    // LINE 3 - the loss, said out loud. Two authored branches rather than one
                    // interpolated verb, so the singular reads "was" and the plural reads "were"
                    // without a template that can only be right for one of them.
                    //
                    // ⚠ WO-1434 - THIS BRANCH IS NOW THE EXCEPTION, NOT THE RULE. Both LIVE
                    // producers (collectors, Echo silo) retain what the cap refused; a row only
                    // reaches here from a path that genuinely discards - e.g.
                    // OfflineHarvestService.Grant, whose ClampGrant result is banked and whose
                    // pre-clamp accrual is dropped on the floor (latent: that path accrued
                    // total=0 on the owner's device and did not run). Before adding a producer to
                    // this branch, PROVE it burns; the [Flow:Bank] "LOST N" warn is the BANK
                    // saying it refused the units, never a statement about what the caller did
                    // with them.
                    block.Add(s.Lost == 1
                        ? $"That 1 {unit} was not added to storage - it is lost."
                        : $"Those {s.Lost} {unit} were not added to storage - they are lost.");

                    // LINE 4 - what to do about it. Over-cap is a DIFFERENT situation from a full
                    // bank (see BankOverflowStatus.OverCap) and keeps its own, non-punitive wording.
                    // WO-1392: the cap that bit is named by its player word ("Wood storage 4000").
                    block.Add(s.OverCap
                        ? $"Earned {unit} resumes once you spend below {s.Max}."
                        : $"{name} storage {s.Max} is full. Upgrade a {s.ContainerName}, or spend {unit}, before collecting again.");
                }

                blocks.Add(string.Join("\n", block));
            }
            // Blocks are separated by a blank line; there is NO trailing summary sentence. With
            // one resource a summary read as a stray fragment ("Each ..." about a list of one),
            // and with several it only repeated what every block already said.
            return string.Join("\n\n", blocks);
        }

        // WO-1471: the per-frame renew Update is DELETED. Renewing every frame was the
        // workaround for the bounded ceiling; a player-owned hold has no ceiling, so there is
        // nothing to renew. WorldHold.Renew itself stays - it is still the seam a bounded beat
        // uses to legitimately extend.

        // WO-1471: Close is now IDEMPOTENT, because the new OnDisable step-out means the normal
        // dismissal path runs it TWICE - the tap calls Close (which defers Destroy(gameObject)),
        // and the host's death at end of frame calls it again through OnDisable. It clears its own
        // fields so the second pass is a no-op rather than a second NotifyClosed/Destroy against
        // the modal arbiter that WelcomeBack shares.
        private void Close()
        {
            if (_panel == null && _hold == null && _modal == null) return;
            if (_panel != null) PanelManager.NotifyClosed(_panel);
            _panel = null;
            _hold?.Dispose();
            _hold = null;
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
            _modal = null;
            if (_active == this) _active = null;
            Destroy(gameObject);
        }

        // WO-1360/WO-1471: with no ceiling the host's OWN lifecycle is the net. A merely-DISABLED
        // component never receives OnDestroy and can neither be dismissed nor release, so the hold
        // and the panel step out TOGETHER here - releasing the hold alone would leave an orphaned
        // HARVEST RESULT card over a running world (the WO-1016 shape from the other direction).
        private void OnDisable() => Close();

        private void OnDestroy()
        {
            _hold?.Dispose();
            _hold = null;
            if (_active == this) _active = null;
        }
    }
}
