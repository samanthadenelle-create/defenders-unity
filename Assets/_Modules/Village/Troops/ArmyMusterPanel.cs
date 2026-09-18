// =============================================================================
// ArmyMusterPanel — Armies loadout bank + one-tap TRAINING ORDER (WO-897 + WO-934,
// re-laid-out by WO-1230).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village
//
// CODE-BUILT uGUI (no UXML). Colourblind-safe TEXT state. ASCII only.
//
// Player loop (fun + value):
//   1. Pick one of 3 saved loadout slots (Raid Push / Wall Hold / Siege Prep)
//   2. Quick-fill a recipe OR step troops with [+] / [-]
//   3. Save to slot (persists)  OR  Train Army (auto-queues Train jobs)
//   4. Watch Obsidian Train queue fill — army prepares while you play
//
// =============================================================================
// WO-1230 — WHY THIS FILE OWNS A LAYOUT TABLE
// -----------------------------------------------------------------------------
// THE DEFECT (owner felt-test, Seeker 2026.08.26.342290, six measured collisions):
// the two command bands were parented straight to `chrome.content.transform` —
// the RAW frame content — while every other element went into a LAYOUT ZONE
// (layout.bodyLeft / bodyRight / footer). The bands therefore sat OUTSIDE the
// zone system and painted over whatever the zones had already placed: the slot
// buttons over the panel TITLE, and the action band over the FOOTER (the wallet)
// AND over the shared kit Close, which is a fixed 360x132 box seated bottom-CENTRE
// (ElarionUiKit.SeatSharedCloseInside / DefaultCloseZone) — that is the "Cl..."
// fragment behind "Save slot 1".
//
// THE FIX is not a nudge. Every element this panel draws is now declared ONCE, in
// ComputeBands / ComputeRowBands below, as an exclusive rect; Open() applies those
// rects to the kit zones it re-seats and to the two NEW band zones it adds, and
// ArmyMusterLayoutRegression measures the SAME table on a live canvas and fails if
// any two rects intersect. One table, one partition, no overlay.
//
// ⛔ THE BANDS ARE DERIVED FROM MinTouchPx, NOT FROM THE MOCKUP'S FRACTIONS. The
// approved mockup drew ~116 device px bands at 2670x1200, which is ~93 canvas
// units — BELOW the 112 floor. Authoring the mockup's number verbatim would have
// handed the layout to ClampMinTouch at runtime, and a clamp that grows a control
// after the fact is exactly how the hero-select overlap was created. The bands are
// therefore computed in reference px and the mockup's PROPORTIONS are honoured
// inside them.
// =============================================================================

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    public sealed class ArmyMusterPanel : MonoBehaviour
    {
        // WO-1512: the staged army USED to live here, as `private static readonly ArmyComposition
        // s_composition` — the View owning the model, which is the §2 violation this file was
        // flagged for. It now lives on ArmyMusterVM, and every verb below is a VM COMMAND. This
        // panel paints the VM and routes taps; it decides no rule and mutates no game state.
        private ArmyMusterVM _vm;

        private GameObject _ui;
        private RectTransform _listContent;
        private Transform _detailHost;
        private Transform _detailBody;
        private RectTransform _selectorHost;
        private RectTransform _actionHost;
        private TextMeshProUGUI _rosterHint;
        private Button _musterCta;
        private TextMeshProUGUI _musterCtaLabel;
        private TextMeshProUGUI _musterCtaSub;
        private PanelHandle _panelHandle;

        /// <summary>WO-1811: TRUE while the LOADOUTS drawer is the surface on screen. It mirrors
        /// <see cref="ArmyMusterVM.LoadoutsOpen"/> and is captured once per Open so a rebuild can
        /// never paint one surface into the other's partition.</summary>
        private bool _drawer;

        /// <summary>WO-1811: the per-troop removal sheet's canvas while it is open (one at a
        /// time, closed with the panel).</summary>
        private GameObject _manageSheet;

        private float _panelW = 1f;
        private float _panelH = 1f;
        private float _rowW = 1f;
        private int _visibleRows = 3;

        /// <summary>A row IS a touch row, plus 4 px of headroom so rounding can never put the
        /// buttons that span it back under ElarionUiKit.MinTouchPx (112).</summary>
        private const float RowHeightPx = ElarionUiKit.MinTouchPx + 4f;
        private const float RowGapPx = 8f;
        private const float HintStripPx = 46f;                       // "+ N more (scroll)" lane
        private static readonly Color RowPlate = new Color(0.16f, 0.16f, 0.18f, 0.92f);
        /// <summary>WO-1230 collision 6: the summary well is CREAM on an all-obsidian UI because
        /// FrameCrafting bakes a parchment right-hand well and the kit paints a parchment plate over
        /// it. The panel re-tints that ONE plate (it does not touch the kit).</summary>
        private static readonly Color SummaryFill = new Color(0.075f, 0.070f, 0.082f, 0.98f);
        private static readonly Color CountFill = new Color(0.10f, 0.09f, 0.06f, 0.95f);

        public bool IsOpen => _ui != null;
        private static ArmyMusterPanel s_host;

        // =====================================================================
        //  WO-1230 LAYOUT TABLE — the single source of truth for every rect this
        //  panel draws, and the thing the layout oracle measures.
        // =====================================================================

        /// <summary>One named, exclusive rect in PANEL fractions (or ROW fractions for the row
        /// table). Public because the layout regression asserts on the same table the panel
        /// builds from — a re-derived copy could not fail (WO-1138 hollow pass).</summary>
        public struct BandRect
        {
            public string Name;
            public Rect Frac;
            public BandRect(string name, float xMin, float yMin, float xMax, float yMax)
            {
                Name = name;
                Frac = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
        }

        /// <summary>Panel anchors on the modal canvas (the values passed to BuildObsidianPanel).</summary>
        public const float PanelAnchorMinX = 0.06f;
        public const float PanelAnchorMinY = 0.05f;
        public const float PanelAnchorMaxX = 0.94f;
        public const float PanelAnchorMaxY = 0.95f;
        public const float PanelFracW = PanelAnchorMaxX - PanelAnchorMinX;
        public const float PanelFracH = PanelAnchorMaxY - PanelAnchorMinY;

        /// <summary>Owner ruling 2026-08-26 (shared with WO-1228): SIX lines, then scroll.</summary>
        public const int MaxVisibleRows = 6;

        /// <summary>The roster count field's font. The oracle measures "999" at this size against
        /// the authored count band, so the two can never drift.</summary>
        public const int CountFontSize = ElarionUi.FontBody;

        /// <summary>The widest count the acceptance criterion names (three digits, no wrap).</summary>
        public const string WidestCount = "999";

        /// <summary>
        /// Every panel-level element as an EXCLUSIVE rect, in fractions of the panel.
        /// <paramref name="panelW"/>/<paramref name="panelH"/> are the panel's size in canvas
        /// REFERENCE units, so the touch bands are real pixels and not a fraction that shrinks
        /// with the surface.
        /// </summary>
        /// <summary>
        /// WO-1811 - THE PRIMARY SURFACE'S table: what you have (ArmyBar + Queue), what to train
        /// (Roster + Status), and GO (Cta), with the preset bank behind one Loadouts door.
        ///
        /// ⚠ THIS IS THE TABLE THE LAYOUT ORACLE MEASURES (ArmyMusterLayoutRegression calls
        /// ComputeBands). The preset drawer has its OWN table below, because the two surfaces occupy
        /// the same lanes at different times - declaring them in one array would read as a pile of
        /// intersections to a suite whose whole job is to find intersections.
        /// "Roster" keeps its name AND its x-range in both tables: the oracle derives the roster row
        /// width from it to measure the count field, so a rename or a reshape there silently changes
        /// what that case is measuring.
        /// </summary>
        public static BandRect[] ComputeBands(float panelW, float panelH)
        {
            panelW = Mathf.Max(1f, panelW);
            panelH = Mathf.Max(1f, panelH);

            float band = Mathf.Clamp((ElarionUiKit.MinTouchPx + 8f) / panelH, 0.09f, 0.19f);
            float gap = Mathf.Clamp(12f / panelH, 0.006f, 0.020f);

            float closeH = Mathf.Clamp(ElarionUiKit.CanonCtaHeight / panelH, 0.10f, 0.22f);
            float closeW = Mathf.Clamp(ElarionUiKit.CanonCtaWidth / panelW, 0.10f, 0.30f);
            float closeTop = 0.992f;
            float closeBottom = closeTop - closeH;
            float closeRight = 0.955f;
            float closeLeft = closeRight - closeW;

            float topTop = closeBottom - gap;
            float topBottom = topTop - band;

            float actionBottom = 0.016f;
            float actionTop = actionBottom + band;

            float bodyTop = topBottom - gap;
            float bodyBottom = actionTop + gap;

            return new[]
            {
                new BandRect("Title", 0.100f, 0.900f, closeLeft - 0.014f, 0.978f),
                new BandRect("Close", closeLeft, closeBottom, closeRight, closeTop),

                // (a) WHAT YOU HAVE - the army bar, and beside it what is training.
                new BandRect("ArmyBar", 0.030f, topBottom, 0.640f, topTop),
                new BandRect("Queue",   0.660f, topBottom, 0.970f, topTop),

                // (b) WHAT TO TRAIN - one row per troop, with the training well beside it.
                new BandRect("Roster", 0.030f, bodyBottom, 0.560f, bodyTop),
                new BandRect("Status", 0.585f, bodyBottom, 0.960f, bodyTop),

                // (c) GO - the raid door, plus the one door to the preset bank.
                new BandRect("Loadouts", 0.030f, actionBottom, 0.290f, actionTop),
                new BandRect("Cta",      0.585f, actionBottom, 0.960f, actionTop),
            };
        }

        /// <summary>
        /// One PRIMARY train row's children (WO-1811): the troop's name, its "45s each - you have 4"
        /// sub-line, and ONE Train button sized from MinTouchPx rather than from a fraction that
        /// shrinks with the surface (the WO-1230 lesson - a clamp that grows a control after the
        /// fact is how controls end up on top of each other).
        /// </summary>
        public static BandRect[] ComputeTrainRowBands(float rowW)
        {
            rowW = Mathf.Max(1f, rowW);
            // TWO faces: Manage (the WO-1811 removal choice) and Train. Both are authored at or
            // over MinTouchPx in BOTH axes - a row IS MinTouchPx tall, so these span its full
            // height rather than insetting, or ClampMinTouch would grow them into the label.
            // WO-1811 felt-test 2026-09-16: "MANAGE" rendered as "MANA..." at 2340x1080. The face
            // was MinTouchPx+16 wide, which clears the TOUCH floor but not the CAPTION - the floor
            // is a minimum for a finger, never a width for a word. Both halves are fixed: the face
            // is authored wider HERE, and the caption is a short word (see ManageFace), so neither
            // one alone has to carry the fit.
            float button = Mathf.Clamp((ElarionUiKit.MinTouchPx + 48f) / rowW, 0.185f, 0.30f);
            float trainRight = 0.985f;
            float trainLeft = trainRight - button;
            float manageRight = trainLeft - 0.012f;
            float manageLeft = manageRight - button;
            float textRight = manageLeft - 0.020f;

            return new[]
            {
                new BandRect("Train.Name",    0.030f, 0.52f, textRight, 0.94f),
                new BandRect("Train.Meta",    0.030f, 0.08f, textRight, 0.48f),
                new BandRect("Train.Manage",  manageLeft, 0f, manageRight, 1f),
                new BandRect("Train.Button",  trainLeft, 0f, trainRight, 1f),
            };
        }

        /// <summary>
        /// The LOADOUTS DRAWER's table - the WO-1230 partition, unchanged except that the Gold chip
        /// is gone. Nothing on this screen has cost gold since WO-1387 (MusterPreview.Cost is
        /// hardcoded zero and prints "Free"), so a wallet readout here was a number with no verb.
        /// </summary>
        public static BandRect[] ComputeLoadoutBands(float panelW, float panelH)
        {
            panelW = Mathf.Max(1f, panelW);
            panelH = Mathf.Max(1f, panelH);

            // A command band is MinTouchPx plus a little breathing room, expressed as a fraction
            // of THIS panel. Clamped so a freak surface cannot eat the body well entirely.
            float band = Mathf.Clamp((ElarionUiKit.MinTouchPx + 8f) / panelH, 0.09f, 0.19f);
            float gap = Mathf.Clamp(12f / panelH, 0.006f, 0.020f);

            // The shared Close keeps its canonical 360x132 box (owner F8 x3 — one Close size on
            // every screen). WO-1230 moves it to the HEADER RIGHT, which is what frees the whole
            // bottom-centre lane the action band needs. The kit's close-band RESERVATION is not
            // touched; this panel re-seats its own zones outright.
            float closeH = Mathf.Clamp(ElarionUiKit.CanonCtaHeight / panelH, 0.10f, 0.22f);
            float closeW = Mathf.Clamp(ElarionUiKit.CanonCtaWidth / panelW, 0.10f, 0.30f);
            float closeTop = 0.992f;
            float closeBottom = closeTop - closeH;
            float closeRight = 0.955f;
            float closeLeft = closeRight - closeW;

            float selTop = closeBottom - gap;
            float selBottom = selTop - band;

            float actionBottom = 0.016f;
            float actionTop = actionBottom + band;

            float bodyTop = selBottom - gap;
            float bodyBottom = actionTop + gap;

            return new[]
            {
                // Title alone in its band — nothing overlays it (collision 2).
                new BandRect("Title", 0.100f, 0.900f, closeLeft - 0.014f, 0.978f),
                new BandRect("Close", closeLeft, closeBottom, closeRight, closeTop),

                // Loadout slots BELOW the title, Clear visually apart, wallet at the lane's end.
                // WO-1811 re-shoot 2026-09-16: 'RAID *ACTIVE*' drew 11 of its 12 glyphs at
                // 1920x1080 (UI_GLYPH_FAIL x1). The bands are wider AND the caption is shorter
                // (see BuildCommandBands) - a rect in the right place whose words were cut away
                // is invisible to every other rule on this path, so neither half is optional.
                new BandRect("Slot.Raid",  0.030f, selBottom, 0.215f, selTop),
                new BandRect("Slot.Hold",  0.230f, selBottom, 0.415f, selTop),
                new BandRect("Slot.Siege", 0.430f, selBottom, 0.615f, selTop),
                new BandRect("Clear",      0.655f, selBottom, 0.845f, selTop),

                new BandRect("Roster",  0.030f, bodyBottom, 0.560f, bodyTop),
                new BandRect("Summary", 0.585f, bodyBottom, 0.960f, bodyTop),

                // Bottom bar: three exclusive lanes, and the Close is no longer one of them.
                new BandRect("Name", 0.030f, actionBottom, 0.290f, actionTop),
                new BandRect("Save", 0.310f, actionBottom, 0.560f, actionTop),
                new BandRect("Cta",  0.585f, actionBottom, 0.960f, actionTop),
            };
        }

        /// <summary>
        /// One roster row's children as EXCLUSIVE rects, in fractions of the row.
        /// <paramref name="rowW"/> is the row's width in canvas reference units so the steppers
        /// are authored AT OR OVER MinTouchPx rather than clamped up into their neighbours.
        /// THE COUNT FIELD IS THE POINT OF THIS TABLE: it used to be authored x 0.70..0.73 — three
        /// percent of the row, ~38 device px — which is why the owner's 20 rendered as a 2 stacked
        /// over a 0 (collision 1, the worst one).
        /// </summary>
        public static BandRect[] ComputeRowBands(float rowW)
        {
            rowW = Mathf.Max(1f, rowW);
            // Fixed-pixel requirements expressed in row fractions. The old 0.22 ceiling made
            // both steppers only 106 px wide on portrait, so ClampMinTouch grew them into the
            // count. Work backwards from the right edge instead: two 120 px controls, a count
            // wide enough for "999" at CountFontSize, and explicit gaps between all three.
            float step = Mathf.Clamp((ElarionUiKit.MinTouchPx + 8f) / rowW, 0.08f, 0.32f);
            float countSpan = Mathf.Clamp(104f / rowW, 0.12f, 0.24f);
            float plusRight = 0.985f;
            float plusLeft = plusRight - step;
            float countRight = plusLeft - 0.010f;
            float countLeft = countRight - countSpan;
            float minusRight = countLeft - 0.010f;
            float minusLeft = minusRight - step;
            float textRight = minusLeft - 0.015f;

            return new[]
            {
                new BandRect("Row.Name",  0.030f, 0.52f, textRight,  0.94f),
                new BandRect("Row.Cost",  0.030f, 0.08f, textRight,  0.48f),
                new BandRect("Row.Minus", minusLeft, 0f, minusRight, 1f),
                new BandRect("Row.Count", countLeft, 0.12f, countRight, 0.88f),
                new BandRect("Row.Plus",  plusLeft, 0f, plusRight,   1f),
            };
        }

        /// <summary>Look one band up by name. Returns a zero rect when absent (never throws).</summary>
        public static Rect Band(BandRect[] bands, string name)
        {
            if (bands == null) return new Rect();
            for (int i = 0; i < bands.Length; i++)
                if (bands[i].Name == name) return bands[i].Frac;
            return new Rect();
        }

        // =====================================================================

        public static void Show()
        {
            if (!BarracksUnlock.IsUnlocked)
            {
                FlowTrace.Step("Muster", "ArmyMusterPanel.Show refused - the Barracks is not built yet.");
                ElarionUiKit.ShowToast(new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.barracks_not_built").Resolve(), ElarionUiKit.ToastTone.Danger);
                return;
            }
            if (s_host == null) s_host = new GameObject("ArmyMusterPanelHost").AddComponent<ArmyMusterPanel>();
            s_host.Open();
        }

        public void Open()
        {
            FlowTrace.Step("Muster", "ArmyMusterPanel.Open - loadout bank + training order UI.");
            Close();

            // WO-1512: hydration is a VM command. The panel does not know what "the active slot"
            // means, only that binding it is the first thing it does.
            if (_vm == null)
            {
                _vm = ArmyMusterVM.CreateDefault();
                _vm.Changed += Rebuild;
            }
            _vm.HydrateFromActiveSlot();

            _ui = ElarionUiKit.BuildModalCanvas("ArmyMusterPanelUI", 31000);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            ElarionUiKit.Scrim(_ui.transform, onTapClose: Close);

            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, _vm.Title,
                new Vector2(PanelAnchorMinX, PanelAnchorMinY), new Vector2(PanelAnchorMaxX, PanelAnchorMaxY),
                Close, frameName: RpgUiCatalog.FrameCrafting, medallionIcon: "sword");

            // ── WO-1230: resolve the panel's REAL size, then partition it ─────
            float canvasH = ElarionUiKit.PostScaleCanvasHeight(chrome.content.transform);
            float canvasW = canvasH * Mathf.Max(1, ElarionUiKit.SurfaceWidth)
                                    / Mathf.Max(1, ElarionUiKit.SurfaceHeight);
            _panelW = PanelFracW * canvasW;
            _panelH = PanelFracH * canvasH;
            // WO-1811: ONE surface at a time. The primary screen answers have / train / go; the
            // preset bank is behind the Loadouts door and brings its own partition with it.
            _drawer = _vm.LoadoutsOpen;
            var bands = CurrentBands();

            var layout = chrome.layout;
            Rect rRoster = Band(bands, "Roster");
            Rect rSummary = Band(bands, _drawer ? "Summary" : "Status");

            // The kit zones are RE-SEATED onto the table (their backing plates are children, so
            // they follow). Nothing is parented to raw chrome.content without a rect of its own.
            Reseat(layout != null ? layout.header : null, Band(bands, "Title"));
            Reseat(layout != null ? layout.body : null, rRoster);
            Reseat(layout != null ? layout.bodyLeft : null, rRoster);
            Reseat(layout != null ? layout.bodyRight : null, rSummary);
            SeatClose(chrome.close, Band(bands, "Close"));

            Transform listHost = layout != null && layout.bodyLeft != null
                ? (Transform)layout.bodyLeft
                : (layout != null && layout.body != null ? (Transform)layout.body : chrome.content.transform);
            _detailHost = layout != null && layout.bodyRight != null
                ? (Transform)layout.bodyRight
                : (layout != null && layout.body != null ? (Transform)layout.body : chrome.content.transform);

            // Collision 6: re-tint the kit's parchment plate on the DETAIL well to obsidian.
            RetintZoneBacking(_detailHost, SummaryFill);

            // The detail well's content lives in its OWN container so the rebuild can clear the
            // content without also destroying the zone's backing plate.
            _detailBody = Zone(_detailHost, "SummaryContent", new Rect(0f, 0f, 1f, 1f));

            var scroll = ElarionUiKit.MakeScrollZone(listHost, RowGapPx, 6);
            _listContent = scroll != null ? scroll.content : null;

            // Reserve the bottom of the roster well for the "+ N more (scroll)" affordance so the
            // hint can never sit on top of a row.
            float rosterH = rRoster.height * _panelH;
            float rosterFullW = rRoster.width * _panelW;
            _rowW = Mathf.Max(1f, rosterFullW - 24f);           // scroll padding + bar gutter
            float hintFrac = rosterH > 1f ? Mathf.Clamp(HintStripPx / rosterH, 0.05f, 0.25f) : 0.12f;
            if (scroll != null && scroll.scroll != null)
            {
                var host = scroll.scroll.transform as RectTransform;
                if (host != null)
                {
                    host.anchorMin = new Vector2(0f, hintFrac);
                    host.anchorMax = Vector2.one;
                    host.offsetMin = Vector2.zero; host.offsetMax = Vector2.zero;
                }
            }
            _rosterHint = ElarionUiKit.Label(listHost, "", 0f, hintFrac,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Midline, 0.04f, 0.96f);
            _rosterHint.raycastTarget = false;
            ElarionUiKit.FitSingleLine(_rosterHint);

            float viewportH = Mathf.Max(1f, rosterH * (1f - hintFrac) - 12f);
            _visibleRows = Mathf.Clamp(Mathf.FloorToInt(viewportH / (RowHeightPx + RowGapPx)), 1, MaxVisibleRows);

            // ── The band zones (the WO-1230 root-cause fix) ───────────────────
            // They are real zones with exclusive rects, not overlays on raw content.
            if (_drawer)
            {
                _selectorHost = Zone(chrome.content.transform, "Zone_SelectorBand",
                    Union(Band(bands, "Slot.Raid"), Band(bands, "Clear")));
                _actionHost = Zone(chrome.content.transform, "Zone_ActionBand",
                    Union(Band(bands, "Name"), Band(bands, "Cta")));
            }
            else
            {
                // WO-1811: the army bar IS the top lane now. The Gold chip that used to sit at the
                // end of it is gone - training has charged nothing since WO-1387, so a wallet
                // readout on this screen was a number with no verb attached to it.
                _selectorHost = Zone(chrome.content.transform, "Zone_ArmyBar",
                    Union(Band(bands, "ArmyBar"), Band(bands, "Queue")));
                _actionHost = Zone(chrome.content.transform, "Zone_ActionBand",
                    Union(Band(bands, "Loadouts"), Band(bands, "Cta")));
            }

            FlowTrace.Step("ArmyUI",
                "ArmyMusterPanel.Open surface=" + (_drawer ? "LOADOUTS-DRAWER" : "PRIMARY") +
                " " + _vm.ArmyLine + " | " + _vm.RoomLine + " | " + _vm.QueueLine + ".");

            FlowTrace.Step("Muster", string.Format(
                "ArmyMusterPanel layout: canvas={0:F0}x{1:F0} panel={2:F0}x{3:F0} " +
                "roster=({4:F3},{5:F3})-({6:F3},{7:F3}) rowW={8:F0} visibleRows={9} bands={10}",
                canvasW, canvasH, _panelW, _panelH,
                rRoster.xMin, rRoster.yMin, rRoster.xMax, rRoster.yMax,
                _rowW, _visibleRows, bands.Length));

            ElarionUiKit.AttachPanelOpenFx(_ui,
                chrome.root != null ? chrome.root.transform as RectTransform : null);

            BarracksService.Changed += Rebuild;
            ArmyMusterService.Mustered += Rebuild;

            if (_panelHandle == null)
                _panelHandle = PanelManager.Register("Armies", Close, () => IsOpen);
            if (!PanelManager.NotifyOpened(_panelHandle))
            {
                FlowTrace.Warn("Muster", "ArmyMusterPanel open rejected by PanelManager (battle-lock) - closed.");
                return;
            }

            Rebuild();
        }

        public void Close()
        {
            CloseManageSheet();
            BarracksService.Changed -= Rebuild;
            ArmyMusterService.Mustered -= Rebuild;

            if (_ui != null && _panelHandle != null)
                PanelManager.NotifyClosed(_panelHandle);

            if (_ui != null) Kill(_ui);
            _ui = null;
            _listContent = null;
            _detailHost = null;
            _detailBody = null;
            _selectorHost = null;
            _actionHost = null;
            _rosterHint = null;
            _musterCta = null;
            _musterCtaLabel = null;
            _musterCtaSub = null;
        }

        private void OnDestroy()
        {
            BarracksService.Changed -= Rebuild;
            ArmyMusterService.Mustered -= Rebuild;
            if (_vm != null) { _vm.Changed -= Rebuild; _vm.Dispose(); _vm = null; }
        }

        // ── Zone plumbing ─────────────────────────────────────────────────────

        /// <summary>A transparent, exclusively-rected drop zone — the same shape the kit's own
        /// private Zone() builds, so a band is a ZONE and never an overlay on raw content.</summary>
        private static RectTransform Zone(Transform parent, string name, Rect frac)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(frac.xMin, frac.yMin);
            rt.anchorMax = new Vector2(frac.xMax, frac.yMax);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static void Reseat(RectTransform zone, Rect frac)
        {
            if (zone == null || frac.width <= 0f || frac.height <= 0f) return;
            zone.anchorMin = new Vector2(frac.xMin, frac.yMin);
            zone.anchorMax = new Vector2(frac.xMax, frac.yMax);
            zone.offsetMin = Vector2.zero; zone.offsetMax = Vector2.zero;
        }

        /// <summary>Seat the ONE shared kit Close in the header-right band. Its canonical
        /// 360x132 box (owner F8 x3 - the same Close size on every screen) is preserved; only
        /// where it sits changes, and it sits in a rect the layout table owns.</summary>
        private static void SeatClose(Button close, Rect frac)
        {
            if (close == null || frac.width <= 0f) return;
            var rt = close.transform as RectTransform;
            if (rt == null) return;
            rt.anchorMin = new Vector2(frac.xMax, frac.yMax);
            rt.anchorMax = new Vector2(frac.xMax, frac.yMax);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(ElarionUiKit.CanonCtaWidth, ElarionUiKit.CanonCtaHeight);
        }

        private static void RetintZoneBacking(Transform zone, Color fill)
        {
            if (zone == null) return;
            var backing = zone.Find("ZoneBacking");
            if (backing == null) { FlowTrace.Warn("Muster", "summary zone has no ZoneBacking plate to re-tint."); return; }
            var img = backing.GetComponent<Image>();
            if (img != null) img.color = fill;
        }

        private static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                                   Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        /// <summary>Re-express a panel-fraction band as a fraction of the zone that holds it.</summary>
        private static Rect ToLocal(Rect zone, Rect band)
        {
            float w = Mathf.Max(1e-4f, zone.width);
            float h = Mathf.Max(1e-4f, zone.height);
            return Rect.MinMaxRect((band.xMin - zone.xMin) / w, (band.yMin - zone.yMin) / h,
                                   (band.xMax - zone.xMin) / w, (band.yMax - zone.yMin) / h);
        }

        // ── Actions ───────────────────────────────────────────────────────────

        // WO-1512: every handler below is a THIN ROUTE — call the VM command, paint what it says.
        // No transaction, no rule, no service call, no model mutation lives in this file any more.
        // The VM returns a neutral MusterTone; mapping it to the kit's toast palette is the one
        // presentation decision left, and it belongs here.

        private static ElarionUiKit.ToastTone Toast(MusterTone tone)
        {
            switch (tone)
            {
                case MusterTone.Good: return ElarionUiKit.ToastTone.Confirm;
                case MusterTone.Warn: return ElarionUiKit.ToastTone.Gold;
                case MusterTone.Bad:  return ElarionUiKit.ToastTone.Danger;
                default:              return ElarionUiKit.ToastTone.Info;
            }
        }

        private void Say(MusterCommandResult result, float seconds = 0f)
        {
            if (string.IsNullOrEmpty(result.Message)) return;
            if (seconds > 0f) ElarionUiKit.ShowToast(result.Message, Toast(result.Tone), seconds);
            else ElarionUiKit.ShowToast(result.Message, Toast(result.Tone));
        }

        private void OnMuster()
        {
            if (_vm == null) return;
            Say(_vm.Muster(), 3.2f);
        }

        private void OnSaveSlot()
        {
            if (_vm == null) return;
            Say(_vm.SaveSlot());
        }

        private void OnSelectSlot(int index)
        {
            if (_vm == null) return;
            Say(_vm.SelectSlot(index));
        }

        private void OnRecipe(int recipe)
        {
            if (_vm == null) return;
            Say(_vm.ApplyRecipe(recipe));
        }

        private void OnCycleName()
        {
            if (_vm == null) return;
            Say(_vm.CycleName());
        }

        // ── Render ────────────────────────────────────────────────────────────

        /// <summary>The band table for the surface currently on screen (WO-1811).</summary>
        private BandRect[] CurrentBands()
        {
            return _drawer ? ComputeLoadoutBands(_panelW, _panelH) : ComputeBands(_panelW, _panelH);
        }

        private void Rebuild()
        {
            if (_ui == null || _detailBody == null || _vm == null) return;
            if (_drawer)
            {
                BuildTroopLadder();
                BuildCommandBands();
                BuildDetail();
                UpdateCta();
            }
            else
            {
                BuildTrainList();
                BuildArmyBar();
                BuildStatusWell();
                BuildPrimaryActions();
            }
        }

        // =====================================================================
        //  WO-1811 - THE PRIMARY SURFACE
        //  (a) what you have, (b) what to train, (c) go. Every string below is
        //  READ off the VM; this file formats no number and decides no rule.
        // =====================================================================

        /// <summary>(a) WHAT YOU HAVE - "Army 7 of 10" with the recovering count on its own line,
        /// and "Room for 3 more" / "Army full - 3 recovering" underneath. The recovering line is
        /// the answer to the owner's "Army is full 10/10" one screen earlier: the wounded were
        /// holding the slots and NOTHING on the old screen said so.</summary>
        private void BuildArmyBar()
        {
            ClearChildren(_selectorHost);
            if (_selectorHost == null) return;

            var bands = CurrentBands();
            Rect zone = Union(Band(bands, "ArmyBar"), Band(bands, "Queue"));
            Rect rArmy = ToLocal(zone, Band(bands, "ArmyBar"));
            Rect rQueue = ToLocal(zone, Band(bands, "Queue"));

            var armyHost = Zone(_selectorHost, "ArmyBarPlate",
                Rect.MinMaxRect(rArmy.xMin, rArmy.yMin, rArmy.xMax, rArmy.yMax));
            Plate(armyHost, SummaryFill);

            string recovering = _vm.RecoveringLine;
            var head = ElarionUiKit.Label(armyHost, _vm.ArmyLine, 0.50f, 0.96f,
                ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.MidlineLeft, 0.04f, 0.96f, bold: true);
            head.raycastTarget = false;
            ElarionUiKit.FitSingleLine(head);

            // WO-1811: ACTIVE (the headline, what fills the cap and goes on the raid) on one line;
            // the qualifiers - recovering, reserve, room - underneath, and each one only while it
            // is true. The owner asked to know "how many troops are trained or how many are
            // deployed"; the headline is deployed, the Reserve term is the rest of trained.
            string reserve = _vm.ReserveLine;
            string under = _vm.RoomLine;
            if (!string.IsNullOrEmpty(reserve)) under = reserve + "   -   " + under;
            if (!string.IsNullOrEmpty(recovering)) under = recovering + "   -   " + under;
            var sub = ElarionUiKit.Label(armyHost, under, 0.06f, 0.48f,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.MidlineLeft, 0.04f, 0.96f);
            sub.raycastTarget = false;
            ElarionUiKit.FitSingleLine(sub);

            var queueHost = Zone(_selectorHost, "QueuePlate",
                Rect.MinMaxRect(rQueue.xMin, rQueue.yMin, rQueue.xMax, rQueue.yMax));
            Plate(queueHost, SummaryFill);
            var q = ElarionUiKit.Label(queueHost, _vm.QueueLine, 0.06f, 0.94f,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Midline, 0.04f, 0.96f);
            q.raycastTarget = false;
            ElarionUiKit.FitSingleLine(q);
        }

        /// <summary>(b) WHAT TO TRAIN - one row per troop with ONE Train button that queues ONE unit
        /// (CoC pattern). The old steppers set a TARGET COMPOSITION and nothing on screen said so,
        /// which is WO-1811 confusion (7).</summary>
        private void BuildTrainList()
        {
            Transform host = _listContent != null ? (Transform)_listContent : null;
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--) Kill(host.GetChild(i).gameObject);

            var rows = _vm.TrainRows();
            if (rows.Count == 0)
            {
                var empty = ElarionUiKit.Label(host, new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.no_troops_unlock").Resolve(),
                    0f, 1f, ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Center);
                var le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = RowHeightPx;
                le.minHeight = RowHeightPx;
                SetHint("");
                return;
            }

            Guard.TryEach("Muster", "train-row", rows, row => BuildTrainRow(host, row));

            int hidden = rows.Count - _visibleRows;
            SetHint(hidden > 0 ? "+ " + hidden + " more (scroll)" : "");
        }

        private void BuildTrainRow(Transform parent, ArmyTrainRow data)
        {
            string id = data.TroopId;

            var row = new GameObject("MusterRow_" + id, typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var le = row.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeightPx;
            le.minHeight = RowHeightPx;
            // ⛔ sizeDelta IS THE HEIGHT, and this line is why every row button measured
            // 100 px. ElarionUiKit.MakeScrollZone builds its column with
            // childControlHeight:FALSE (deliberately - kit rows are sized by explicit
            // sizeDelta, see DefenseMapPlate.cs:159), so the LayoutElement above is ignored
            // and a fresh RectTransform keeps its 100x100 default. The row buttons span the
            // row's full height, so EVERY one of them resolved 12 px under MinTouchPx on all
            // three surfaces (UI_TOUCH_FAIL x36, 2026-09-16 22:03) - a uniform number across
            // different surfaces is the tell that it came from a constant, not a layout.
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, RowHeightPx);

            var plate = row.GetComponent<Image>();
            var slot = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, "slot_talent_1");
            if (slot != null) { plate.sprite = slot; plate.type = Image.Type.Sliced; plate.fillCenter = true; }
            plate.color = RowPlate;

            var rb = ComputeTrainRowBands(_rowW);
            Rect rName = Band(rb, "Train.Name");
            Rect rMeta = Band(rb, "Train.Meta");
            Rect rManage = Band(rb, "Train.Manage");
            Rect rButton = Band(rb, "Train.Button");

            var nameLabel = ElarionUiKit.Label(row.transform, data.Name, rName.yMin, rName.yMax,
                ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.MidlineLeft,
                rName.xMin, rName.xMax, bold: true);
            nameLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(nameLabel);

            var metaLabel = ElarionUiKit.Label(row.transform, data.Meta, rMeta.yMin, rMeta.yMax,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.MidlineLeft,
                rMeta.xMin, rMeta.xMax);
            metaLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(metaLabel);

            // The refusal is a DIMMED button plus a spoken reason on tap, never a hue: the owner is
            // red/green colourblind, so state must survive a greyscale read.
            var kind = data.CanTrain ? ElarionUiKit.ButtonKind.Confirm : ElarionUiKit.ButtonKind.Quiet;
            var train = ElarionUiKit.Button(row.transform, TrainFace(), kind,
                new Vector2(rButton.xMin, rButton.yMin), new Vector2(rButton.xMax, rButton.yMax),
                () => OnTrainOne(id));
            if (train != null)
            {
                ElarionUiKit.ClampMinTouch(train);
                train.interactable = data.CanTrain;
            }

            // WO-1811 owner ruling: a FULL army is rebalanced by REMOVING, and each removal is a
            // CHOICE. One face, one sheet, every applicable verb labelled - rather than two more
            // buttons crowding a row that has to survive a phone in landscape.
            var manage = ElarionUiKit.Button(row.transform, ManageFace(), ElarionUiKit.ButtonKind.Quiet,
                new Vector2(rManage.xMin, rManage.yMin), new Vector2(rManage.xMax, rManage.yMax),
                () => OpenManageSheet(data));
            if (manage != null)
            {
                ElarionUiKit.ClampMinTouch(manage);
                manage.interactable = data.CanManage;
            }
        }

        private static string TrainFace()
        {
            return DeNelle.Core.UI.LocalText.Get(ArmyBoardCopy.KeyTrainButton, "Train");
        }

        private static string ManageFace()
        {
            return DeNelle.Core.UI.LocalText.Get(ArmyBoardCopy.KeyManage, "Manage");
        }

        /// <summary>
        /// WO-1811, owner ruling 2026-09-16 - THE REMOVAL SHEET. One troop type, the two counts
        /// that matter said in words, and every applicable verb as its own labelled face:
        ///   * Move to Reserve - keeps the troop, frees its cap slot, never joins a raid;
        ///   * Dismiss for N gold - the troop is gone and pays the knob's share of its catalog value;
        ///   * Return to army - the way back, offered only while the army has room for it.
        /// It is a SHEET rather than two more row buttons because the row must stay legible on a
        /// phone in landscape with every face at or over MinTouchPx (the WO-1230 lesson).
        /// </summary>
        private void OpenManageSheet(ArmyTrainRow data)
        {
            if (_vm == null) return;
            CloseManageSheet();

            var modal = ElarionUiKit.BuildObsidianModal("ArmyManageSheet", data.Name,
                new Vector2(0.24f, 0.16f), new Vector2(0.76f, 0.84f), CloseManageSheet, 32200);
            _manageSheet = modal != null ? modal.canvas : null;
            if (modal == null || modal.chrome.content == null) return;

            var content = modal.chrome.content.transform;
            var body = ElarionUiKit.Label(content, ArmyBoardCopy.ManageBody(data.Active, data.Reserve),
                0.62f, 0.80f, ElarionUi.Parchment, ElarionUi.FontLabel,
                TextAlignmentOptions.Center, 0.07f, 0.93f);
            body.raycastTarget = false;
            body.enableWordWrapping = true;
            ElarionUiKit.FitBlock(body, 28f, ElarionUi.FontLabel);

            string id = data.TroopId;
            float top = 0.55f;
            // ⛔ THE FACE HEIGHT IS DERIVED IN PIXELS, NOT AUTHORED AS A FRACTION. A flat 0.135 of
            // this modal resolves to ~89 px on the Seeker - under MinTouchPx(112) - so ClampMinTouch
            // would grow every face into the one below it, which is the WO-1230 failure this panel's
            // header forbids. The sheet's own height is the divisor, so the faces are real touch
            // bands on every surface.
            float sheetPx = Mathf.Max(1f, ElarionUiKit.PostScaleCanvasHeight(content) * 0.68f);
            float faceH = Mathf.Clamp((ElarionUiKit.MinTouchPx + 8f) / sheetPx, 0.12f, 0.24f);

            if (data.Active > 0)
            {
                AddSheetFace(content, ref top, faceH,
                    DeNelle.Core.UI.LocalText.Get(ArmyBoardCopy.KeyMoveToReserve, "Move to Reserve"),
                    ElarionUiKit.ButtonKind.Confirm,
                    () => { Say(_vm.MoveToReserveOne(id)); CloseManageSheet(); });
            }
            if (data.Reserve > 0)
            {
                AddSheetFace(content, ref top, faceH,
                    DeNelle.Core.UI.LocalText.Get(ArmyBoardCopy.KeyRecall, "Return to army"),
                    data.CanRecall ? ElarionUiKit.ButtonKind.Confirm : ElarionUiKit.ButtonKind.Quiet,
                    () => { Say(_vm.RecallOne(id)); CloseManageSheet(); });
            }
            AddSheetFace(content, ref top, faceH, ArmyBoardCopy.DismissFace(data.DismissGold),
                ElarionUiKit.ButtonKind.Quiet,
                () => { Say(_vm.DismissOne(id)); CloseManageSheet(); });
            // NO Cancel face on purpose: the kit's shared Close AND the scrim both fire
            // CloseManageSheet, so a fourth face would only be a fourth way to do the same thing -
            // and with three verbs offered it is the one that would not fit inside the sheet.

            FlowTrace.Step("ArmyUI", "manage sheet for '" + id + "' active=" + data.Active +
                                     " reserve=" + data.Reserve + " dismissGold=" + data.DismissGold + ".");
        }

        /// <summary>Stack one full-width face down the sheet, each a real touch band.</summary>
        private static void AddSheetFace(Transform content, ref float top, float faceH, string face,
                                         ElarionUiKit.ButtonKind kind, System.Action onTap)
        {
            const float gap = 0.020f;
            float bottom = top - faceH;
            if (bottom < 0.02f) return;              // never stack a face off the sheet
            var b = ElarionUiKit.Button(content, face, kind,
                new Vector2(0.10f, bottom), new Vector2(0.90f, top), onTap);
            if (b != null) ElarionUiKit.ClampMinTouch(b);
            top = bottom - gap;
        }

        private void CloseManageSheet()
        {
            if (_manageSheet == null) return;
            Kill(_manageSheet);
            _manageSheet = null;
        }

        /// <summary>The right-hand well: what is training, and the one tip. No "staged", no cost, no
        /// save-slot number - the three things the owner could not place.</summary>
        private void BuildStatusWell()
        {
            if (_detailBody == null) return;
            for (int i = _detailBody.childCount - 1; i >= 0; i--)
                Kill(_detailBody.GetChild(i).gameObject);

            var body = new System.Text.StringBuilder();
            body.Append(DeNelle.Core.UI.LocalText.Get(ArmyBoardCopy.KeyInTraining, "IN TRAINING")).Append('\n');
            body.Append(_vm.QueueLine).Append('\n');
            string full = _vm.QueueFullLine;
            if (!string.IsNullOrEmpty(full)) body.Append(full).Append('\n');
            // ⛔ THE ARMY NUMBERS ARE NOT REPEATED HERE. They are the header band's job
            // (BuildArmyBar). Printing them twice is how one copy goes stale against the other -
            // the duplicated-state failure this ticket removed from the old summary column - and
            // the owner's 2026-09-16 felt-test read it as the panel saying the same thing twice.
            // This well is about TRAINING and nothing else.
            body.Append('\n').Append(DeNelle.Core.UI.LocalText.Get(ArmyBoardCopy.KeyTip,
                "Tap Train to add one troop. They train while you play."));

            var text = ElarionUiKit.Label(_detailBody, body.ToString(), 0.04f, 0.96f,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.TopLeft, 0.05f, 0.95f);
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            ElarionUiKit.FitBlock(text, 28f, ElarionUi.FontLabel);

            FlowTrace.Step("ArmyUI", "status well (training only): " + _vm.QueueLine + ".");
        }

        /// <summary>(c) GO - the raid door, live only when the army is ready, plus the ONE door to
        /// the preset bank.</summary>
        private void BuildPrimaryActions()
        {
            ClearChildren(_actionHost);
            if (_actionHost == null) return;

            var bands = CurrentBands();
            Rect zone = Union(Band(bands, "Loadouts"), Band(bands, "Cta"));
            Rect rLoadouts = ToLocal(zone, Band(bands, "Loadouts"));
            Rect rCta = ToLocal(zone, Band(bands, "Cta"));

            var loadouts = ElarionUiKit.Button(_actionHost,
                DeNelle.Core.UI.LocalText.Get(ArmyBoardCopy.KeyLoadouts, "Loadouts"),
                ElarionUiKit.ButtonKind.Quiet,
                new Vector2(rLoadouts.xMin, rLoadouts.yMin), new Vector2(rLoadouts.xMax, rLoadouts.yMax),
                OnToggleLoadouts);
            if (loadouts != null) ElarionUiKit.ClampMinTouch(loadouts);

            bool ready = _vm.ArmyReady;
            _musterCta = ElarionUiKit.Button(_actionHost, _vm.ReadyLine,
                ready ? ElarionUiKit.ButtonKind.Confirm : ElarionUiKit.ButtonKind.Quiet,
                new Vector2(rCta.xMin, rCta.yMin), new Vector2(rCta.xMax, rCta.yMax), OnGoRaid);
            if (_musterCta != null)
            {
                ElarionUiKit.ClampMinTouch(_musterCta);
                _musterCta.interactable = ready;
                _musterCtaLabel = _musterCta.GetComponentInChildren<TextMeshProUGUI>();
            }

            FlowTrace.Step("ArmyUI", "primary actions: ready=" + ready + " face='" + _vm.ReadyLine + "'.");
        }

        /// <summary>A flat obsidian plate behind a primary band, so a word sits on a known ground
        /// rather than on whatever the frame baked there (the WO-1230 collision-6 lesson).</summary>
        private static void Plate(Transform host, Color fill)
        {
            if (host == null) return;
            var go = ElarionUiKit.AddImage(host, "Plate", Vector2.zero, Vector2.one, fill);
            var img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            go.transform.SetAsFirstSibling();
        }

        private void OnTrainOne(string troopId)
        {
            if (_vm == null) return;
            Say(_vm.TrainOne(troopId));
        }

        private void OnToggleLoadouts()
        {
            if (_vm == null) return;
            _vm.ToggleLoadouts();
            Open();     // the surface changes partition, so it is rebuilt outright
        }

        /// <summary>The raid door. It only ever opens the EXISTING raid entry
        /// (<see cref="Hero.RaidSelectionScreen.Open"/>) - the readiness decision was already made
        /// by ArmyReadiness and is never re-judged here (the WO-820 "never add a third opinion" rule).</summary>
        private void OnGoRaid()
        {
            Close();
            Guard.Try("ArmyUI", "open the raid screen", () => DeNelle.Village.Hero.RaidSelectionScreen.Open());
        }

        private void BuildTroopLadder()
        {
            Transform host = _listContent != null ? (Transform)_listContent : null;
            if (host == null) return;

            for (int i = host.childCount - 1; i >= 0; i--) Kill(host.GetChild(i).gameObject);

            // WO-1512: the unlock gate is a RULE and now lives in the VM; the panel just paints
            // whatever roster it is handed.
            var offered = _vm.OfferedTroops();

            if (offered.Count == 0)
            {
                var empty = ElarionUiKit.Label(host, new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.no_troops_unlock").Resolve(),
                    0f, 1f, ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Center);
                var le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = RowHeightPx;
                le.minHeight = RowHeightPx;
                SetHint("");
                return;
            }

            Guard.TryEach("Muster", "troop-row", offered, def => BuildTroopRow(host, def));

            // Owner ruling 2026-08-26 (shared with WO-1228): SIX lines, then scroll. On this
            // landscape frame the well seats fewer, so the affordance says how many are below.
            int hidden = offered.Count - _visibleRows;
            SetHint(hidden > 0 ? "+ " + hidden + " more (scroll)" : "");
        }

        private void SetHint(string text)
        {
            if (_rosterHint == null) return;
            _rosterHint.text = text ?? "";
        }

        private void BuildTroopRow(Transform parent, TroopDef def)
        {
            string id = def.Id;

            var row = new GameObject("MusterRow_" + id, typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var le = row.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeightPx;
            le.minHeight = RowHeightPx;
            // ⛔ sizeDelta IS THE HEIGHT, and this line is why every row button measured
            // 100 px. ElarionUiKit.MakeScrollZone builds its column with
            // childControlHeight:FALSE (deliberately - kit rows are sized by explicit
            // sizeDelta, see DefenseMapPlate.cs:159), so the LayoutElement above is ignored
            // and a fresh RectTransform keeps its 100x100 default. The row buttons span the
            // row's full height, so EVERY one of them resolved 12 px under MinTouchPx on all
            // three surfaces (UI_TOUCH_FAIL x36, 2026-09-16 22:03) - a uniform number across
            // different surfaces is the tell that it came from a constant, not a layout.
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, RowHeightPx);

            var plate = row.GetComponent<Image>();
            var slot = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, "slot_talent_1");
            if (slot != null) { plate.sprite = slot; plate.type = Image.Type.Sliced; plate.fillCenter = true; }
            plate.color = RowPlate;

            var rb = ComputeRowBands(_rowW);
            Rect rName = Band(rb, "Row.Name");
            Rect rCost = Band(rb, "Row.Cost");
            Rect rMinus = Band(rb, "Row.Minus");
            Rect rCount = Band(rb, "Row.Count");
            Rect rPlus = Band(rb, "Row.Plus");

            string name = string.IsNullOrEmpty(def.DisplayName) ? id : def.DisplayName;
            // Cap note for siege maxOwned
            string capTag = def.MaxOwned == 1 ? " (max 1)" : "";
            var nameLabel = ElarionUiKit.Label(row.transform, name + capTag, rName.yMin, rName.yMax,
                ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.MidlineLeft,
                rName.xMin, rName.xMax, bold: true);
            nameLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(nameLabel);

            // Collision 3: the old two-part row string ("550 Gold - 1m 00s each") wrapped to a second
            // line and pushed into the row below. The string is now ONE compact line, and
            // FitSingleLine makes a long one ellipsize inside its own band instead of wrapping into a
            // neighbour. WO-1586 dropped the gold half entirely - see PerUnitLine.
            var costLabel = ElarionUiKit.Label(row.transform, PerUnitLine(def), rCost.yMin, rCost.yMax,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.MidlineLeft,
                rCost.xMin, rCost.xMax);
            costLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(costLabel);

            var minus = ElarionUiKit.Button(row.transform, "-", ElarionUiKit.ButtonKind.Quiet,
                new Vector2(rMinus.xMin, rMinus.yMin), new Vector2(rMinus.xMax, rMinus.yMax), () => Step(id, -1));
            if (minus != null) ElarionUiKit.ClampMinTouch(minus);

            // THE COUNT FIELD. Emphasis is a BORDER, never a hue (the owner is red/green
            // colourblind), and the field is wide enough for three digits with headroom for four.
            var countPlate = ElarionUiKit.AddImage(row.transform, "CountPlate",
                new Vector2(rCount.xMin, rCount.yMin), new Vector2(rCount.xMax, rCount.yMax), CountFill);
            var plateImg = countPlate.GetComponent<Image>();
            if (plateImg != null) plateImg.raycastTarget = false;
            var frameSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleElement, RpgUiCatalog.ElementStat);
            if (frameSprite != null)
            {
                var frameGo = ElarionUiKit.AddImage(countPlate.transform, "CountFrame",
                    Vector2.zero, Vector2.one, ElarionUi.Gilt, rounded: false);
                var frameImg = frameGo.GetComponent<Image>();
                frameImg.sprite = frameSprite;
                frameImg.type = Image.Type.Sliced;
                frameImg.fillCenter = false;
                frameImg.raycastTarget = false;
            }

            var count = ElarionUiKit.Label(countPlate.transform, _vm.CountOf(id).ToString(),
                0.06f, 0.94f, ElarionUi.Parchment, CountFontSize, TextAlignmentOptions.Center,
                0.06f, 0.94f, bold: true);
            count.name = "CountLabel";
            count.raycastTarget = false;
            count.textWrappingMode = TextWrappingModes.NoWrap;
            // 4 digits and beyond: FitSingleLine auto-sizes DOWN inside the 200px box and
            // ellipsizes at the font floor. It never wraps, so a 2-over-0 stack cannot recur.
            ElarionUiKit.FitSingleLine(count);

            var plus = ElarionUiKit.Button(row.transform, "+", ElarionUiKit.ButtonKind.Gold,
                new Vector2(rPlus.xMin, rPlus.yMin), new Vector2(rPlus.xMax, rPlus.yMax), () => Step(id, 1));
            if (plus != null) ElarionUiKit.ClampMinTouch(plus);
        }

        private void Step(string troopId, int delta)
        {
            // WO-1512: the maxOwned ceiling ("you can't stage 2 catapults") is a RULE, decided in
            // the VM. The panel routes the tap and voices the refusal.
            if (_vm == null) return;
            Say(_vm.Step(troopId, delta));
        }

        /// <summary>
        /// The per-troop roster line. TIME ONLY - "1m 00s each" (WO-1586).
        /// This read `new ArmyCost { Gold = def.CostGold }` and printed "550 Gold - 1m 00s each" on
        /// every row, which is half of the "everytime showed as need gold" the owner hit on
        /// 2026-09-07 while rebalancing an army she already owned. Training has charged nothing
        /// since WO-1387; gold is quoted ONLY by the skip verb (HIRE REINFORCEMENTS /
        /// BuildTimerService.InstantFinishPrice), so no train-side surface may name it.
        /// </summary>
        private static string PerUnitLine(TroopDef def)
        {
            return CompactDuration(def.BuildSeconds) + " each";
        }

        /// <summary>ONE-LINE time grammar for a roster row ("45s", "1m", "1m30", "2h"). The long
        /// form (ArmyMusterPlanner.FormatDuration) stays exactly as it is for the summary column and
        /// its regression - this is a row-width presentation choice, not a new duration model.</summary>
        public static string CompactDuration(double seconds)
        {
            // WO-1811: the implementation moved to ArmyBoardCopy (the VM side) because the VM needs
            // it for its row projection and must not call into a MonoBehaviour for a string. This
            // forwards so there is ONE grammar, not two that drift.
            return ArmyBoardCopy.CompactDuration(seconds);
        }

        private void BuildDetail()
        {
            if (_detailBody == null) return;

            for (int i = _detailBody.childCount - 1; i >= 0; i--)
                Kill(_detailBody.GetChild(i).gameObject);

            var preview = _vm.Preview;
            var body = new System.Text.StringBuilder();

            // WO-1811: "STAGED" is gone from every player-facing string. The owner had no model for
            // a plan that is neither owned nor training; this well now names the SAVED ARMY it is
            // editing, in words, and the army/queue truth comes from the same VM lines the primary
            // surface uses, so the two surfaces cannot disagree.
            body.Append(_vm.ArmyName).Append('\n');

            if (preview.TotalUnits <= 0)
            {
                body.Append(new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.empty_army_help").Resolve());
            }
            else
            {
                foreach (var r in _vm.Composition.Rows)
                {
                    if (r == null || r.Count <= 0) continue;
                    body.Append("  ").Append(r.Count).Append("x ")
                        .Append(_vm.DisplayNameOf(r.TroopId)).Append('\n');
                }
                body.Append("\n").Append(new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.cost_label").Resolve()).Append(" ").Append(preview.Cost).Append('\n');
                body.Append("Time: ").Append(ArmyMusterPlanner.FormatDuration(preview.TotalSeconds))
                    .Append(" (").Append(preview.TrainSlots).Append(" train slot")
                    .Append(preview.TrainSlots == 1 ? "" : "s").Append(")\n");
            }

            body.Append("\n").Append(DeNelle.Core.UI.LocalText.Format("village.troops.army_muster.train_queue_fmt", preview.LineDepth, ArmyMusterPlanner.TrainQueueDepthCap, preview.LineRoom)).Append('\n');
            // WO-1811: "Fits now: 5 of 10 (rest stays staged)" was the single most misread line on
// the screen - the 5 is QUEUE room and the 10 is the plan total, two different axes with no
            // label between them. Said in words, naming the queue as the thing that is full.
            if (preview.WouldNotFit > 0)
                body.Append("Starts now: ").Append(preview.WouldFit).Append(" of ")
                    .Append(preview.TotalUnits).Append(" - the rest waits for queue space.\n");

            if (!string.IsNullOrEmpty(_vm.LastResultHeadline))
            {
                body.Append("\nLAST TRAINING ORDER\n").Append(_vm.LastResultHeadline).Append('\n');
                if (!string.IsNullOrEmpty(_vm.LastResultDetail)) body.Append(_vm.LastResultDetail).Append('\n');
            }

            // OWNER RULING 2026-08-26 - the tip line, verbatim.
            body.Append("\n").Append(new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.tip_line").Resolve());

            bool shortOf = preview.TotalUnits > 0 && !preview.Affordable;
            // WO-1586 §12: the REASON STRING the player actually reads, captured verbatim. If a
            // capture ever shows "Gold" in this line again, the WO-1387 ruling has been reversed a
            // third time and the trace names the exact frame it happened on.
            // The chip text is composed OUTSIDE the interpolation hole on purpose: escaped quotes
            // inside a hole read as an unbalanced brace to the compile gate's string stripper
            // (COMPILE_GATE_FAIL 2026-09-07, cg-wave8.log: 62 open vs 65 close on this file).
            // WO-1811: the chip's own text, computed HERE so the trace records the sentence the
            // player actually reads. It used to hardcode the retired "SHORT OF:" prefix into the
            // trace, which would have survived the copy fix and lied about what was on screen.
            string chipText = shortOf
                ? (preview.ShortOf != null && preview.ShortOf.Contains("Army")
                    ? _vm.RoomLine
                    : ArmyBoardCopy.QueueFullLine(preview.LineDepth, ArmyMusterPlanner.TrainQueueDepthCap))
                : "";
            string shortOfChip = shortOf ? "'" + chipText + "'" : "none";
            FlowTrace.Step("Muster",
                "panel detail: units=" + preview.TotalUnits + " cost='" + preview.Cost + "' " +
                "time=" + ArmyMusterPlanner.FormatDuration(preview.TotalSeconds) + " " +
                "shortOfChip=" + shortOfChip + " " +
                "(owned=" + preview.AlreadyOwned + " toTrain=" + preview.ToTrain + " armyRoom=" + preview.ArmyRoom + ").");
            float textFloor = shortOf ? 0.17f : 0.04f;

            var text = ElarionUiKit.Label(_detailBody, body.ToString(), textFloor, 0.96f,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.TopLeft, 0.05f, 0.95f);
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            ElarionUiKit.FitBlock(text, 28f, ElarionUi.FontLabel);

            if (shortOf)
            {
                // A FRAMED WORD-CHIP, never a colour. The owner is red/green colourblind, so the
                // shortfall has to survive a greyscale check: it does, because it is a word in a
                // box rather than a red tint on a number.
                var chip = ElarionUiKit.AddImage(_detailBody, "ShortOfChip",
                    new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.145f), CountFill);
                var chipImg = chip.GetComponent<Image>();
                if (chipImg != null) chipImg.raycastTarget = false;
                var chipFrame = RpgUiCatalog.Get(RpgUiCatalog.RoleElement, RpgUiCatalog.ElementStat);
                if (chipFrame != null)
                {
                    var fg = ElarionUiKit.AddImage(chip.transform, "ShortOfFrame",
                        Vector2.zero, Vector2.one, ElarionUi.Gilt, rounded: false);
                    var fi = fg.GetComponent<Image>();
                    fi.sprite = chipFrame; fi.type = Image.Type.Sliced; fi.fillCenter = false;
                    fi.raycastTarget = false;
                }
                // WO-1811: the chip used to read like an error code while the button underneath
                // still said Train. It now says the SAME plain sentence the primary surface says,
                // produced by the VM (chipText, computed with the trace above).
                var chipLabel = ElarionUiKit.Label(chip.transform, chipText,
                    0.08f, 0.92f, ElarionUi.Parchment, ElarionUi.FontLabel,
                    TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
                chipLabel.raycastTarget = false;
                ElarionUiKit.FitSingleLine(chipLabel);
            }
        }

        private void BuildCommandBands()
        {
            ClearChildren(_selectorHost);
            ClearChildren(_actionHost);
            if (_selectorHost == null || _actionHost == null) return;

            var bands = CurrentBands();
            Rect selZone = Union(Band(bands, "Slot.Raid"), Band(bands, "Clear"));
            Rect actZone = Union(Band(bands, "Name"), Band(bands, "Cta"));

            string[] selectors = { "Raid", "Hold", "Siege", "Clear" };
            string[] selectorBands = { "Slot.Raid", "Slot.Hold", "Slot.Siege", "Clear" };
            for (int i = 0; i < selectors.Length; i++)
            {
                int selection = i;
                bool active = i < _vm.SlotCount && i == _vm.ActiveSlot;
                var kind = active ? ElarionUiKit.ButtonKind.Gold : ElarionUiKit.ButtonKind.Quiet;
                Rect r = ToLocal(selZone, Band(bands, selectorBands[i]));
                // ACTIVE is a WORD, not a hue - greyscale-safe slot state. Single line on
                // purpose: the kit button fits its face with NoWrap + Ellipsis, so a second
                // line would be at the mercy of the overflow mode.
                // The asterisks were pure width: "ACTIVE" is already the greyscale-safe tell
                // (a WORD, never a hue - the owner is red/green colourblind), and dropping the
                // two '*' takes 2 of 12 glyphs off the widest caption on this band.
                string face = active ? selectors[i] + " ACTIVE" : selectors[i];
                var button = ElarionUiKit.Button(_selectorHost, face, kind,
                    new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMax),
                    () => { if (selection < _vm.SlotCount) OnSelectSlot(selection); else OnRecipe(3); });
                if (button != null) ElarionUiKit.ClampMinTouch(button);
            }

            Rect rName = ToLocal(actZone, Band(bands, "Name"));
            Rect rSave = ToLocal(actZone, Band(bands, "Save"));
            Rect rCta = ToLocal(actZone, Band(bands, "Cta"));

            var name = ElarionUiKit.Button(_actionHost, DeNelle.Core.UI.LocalText.Format("village.troops.army_muster.name_button", ShortName(_vm.ArmyName)),
                ElarionUiKit.ButtonKind.Quiet, new Vector2(rName.xMin, rName.yMin),
                new Vector2(rName.xMax, rName.yMax), OnCycleName);
            var save = ElarionUiKit.Button(_actionHost, DeNelle.Core.UI.LocalText.Format("village.troops.army_muster.save_slot", _vm.ActiveSlot + 1),
                ElarionUiKit.ButtonKind.Gold, new Vector2(rSave.xMin, rSave.yMin),
                new Vector2(rSave.xMax, rSave.yMax), OnSaveSlot);
            _musterCta = ElarionUiKit.Button(_actionHost, new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.train_army_button").Resolve(), ElarionUiKit.ButtonKind.Confirm,
                new Vector2(rCta.xMin, rCta.yMin), new Vector2(rCta.xMax, rCta.yMax), OnMuster);
            if (name != null) ElarionUiKit.ClampMinTouch(name);
            if (save != null) ElarionUiKit.ClampMinTouch(save);
            if (_musterCta != null)
            {
                ElarionUiKit.ClampMinTouch(_musterCta);
                _musterCtaLabel = _musterCta.GetComponentInChildren<TextMeshProUGUI>();
                if (_musterCtaLabel != null)
                {
                    // Two lines, two labels: the CTA says WHAT it does, the subline says what
                    // happens to the 15 units that do not start now - "5 start now, 15 stay
                    // staged" without the player reading the summary column.
                    var lrt = _musterCtaLabel.transform as RectTransform;
                    if (lrt != null)
                    {
                        lrt.anchorMin = new Vector2(lrt.anchorMin.x, 0.44f);
                        lrt.anchorMax = new Vector2(lrt.anchorMax.x, 0.94f);
                        lrt.offsetMin = new Vector2(lrt.offsetMin.x, 0f);
                        lrt.offsetMax = new Vector2(lrt.offsetMax.x, 0f);
                    }
                    _musterCtaSub = ElarionUiKit.Label(_musterCta.transform, "", 0.06f, 0.42f,
                        ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Center, 0.04f, 0.96f);
                    _musterCtaSub.raycastTarget = false;
                    ElarionUiKit.FitSingleLine(_musterCtaSub);
                }
            }
        }

        private static void ClearChildren(Transform host)
        {
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--) Kill(host.GetChild(i).gameObject);
        }

        /// <summary>
        /// WO-1811: the one teardown seam. Runtime <c>Destroy</c> is DEFERRED and is illegal in edit
        /// mode, so an edit-mode headless capture of this panel either errored or rendered the OLD
        /// children on top of the new ones. That is why this screen had never been shot by the UI
        /// capture harness at all - the only eyes on it were the owner's, which is the one thing
        /// CLAUDE.md §14 exists to stop relying on.
        /// </summary>
        private static void Kill(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Army";
            if (name.Length <= 12) return name;
            return name.Substring(0, 11) + ".";
        }

        private void UpdateCta()
        {
            if (_musterCta == null || _vm == null) return;
            var preview = _vm.Preview;

            // OWNER RULING 2026-08-26: the CTA reads "Train Army" on every state; the STATE lives
            // in the subline, so the button never becomes a sentence the player has to decode.
            string sub;
            bool interactable;
            if (preview.TotalUnits <= 0)
            {
                sub = preview.LineDepth > 0
                    ? "Queue busy - " + preview.LineDepth + " training"
                    : "Stage troops first";
                interactable = false;
            }
            else if (preview.LineRoom <= 0)
            {
                sub = "Queue full - " + preview.LineDepth + " of " + ArmyMusterPlanner.TrainQueueDepthCap;
                interactable = false;
            }
            else if (!preview.Affordable && preview.ArmyRoom <= 0)
            {
                // ⛔ WO-1811 §1b - THE DEFECT THIS BRANCH EXISTS TO CLOSE. Every branch here read
                // only the QUEUE axis (LineRoom / WouldNotFit), so on a FULL ARMY the button stayed
                // enabled and promised "5 start now" while BarracksService.EnqueueTraining refused
                // every unit at rosterSlots + committed + unitSlots > cap (BarracksService.cs:385,
                // stopReason "Army is full."). The screen promised what the action then refused.
                sub = _vm.RoomLine;
                interactable = false;
            }
            else if (preview.WouldNotFit > 0)
            {
                // WO-1811: "stay staged" is gone - the player has no model for it. The rest is
                // waiting for QUEUE SPACE, which is the thing that is actually full.
                sub = preview.WouldFit + " start now - " + preview.WouldNotFit + " wait for queue space";
                interactable = true;
            }
            else
            {
                sub = preview.LineDepth > 0
                    ? preview.TotalUnits + " start now - " + preview.LineDepth + " already training"
                    : preview.TotalUnits + " start now";
                interactable = true;
            }

            _musterCta.interactable = interactable;
            if (_musterCtaLabel != null) _musterCtaLabel.text = new DeNelle.Core.UI.LocalizedText("village.troops.army_muster.train_army_button").Resolve();
            if (_musterCtaSub != null) _musterCtaSub.text = sub;

            // WO-1586 §12: the CTA's REASON, as the player reads it. Note the button has never been
            // gated on gold - only on queue room - which is why the defect presented as a panel that
            // SAID "need gold" rather than one that refused. Both halves are now in the trace.
            FlowTrace.Step("Muster",
                "panel cta: interactable=" + interactable + " sub='" + sub + "' " +
                "(units=" + preview.TotalUnits + " lineRoom=" + preview.LineRoom + " affordable=" + preview.Affordable + " " +
                "shortOf='" + preview.ShortOf + "').");
        }
    }
}
