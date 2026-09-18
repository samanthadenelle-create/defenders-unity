// =============================================================================
// HelpMenu - the HELP modal: PanelId.Help, opened from the "Help" row inside Settings.
// WO-1399: this screen used to be what the gear dock's "Settings" row opened (the row
// was mislabelled; the trace text said "Settings" too). The dock row now opens the real
// SettingsController via Core SettingsGate, and this menu registers PanelId.Help so
// Settings can route to it exactly as it routes to Game Guide / Defence Reports.
// Surfaces whatever HelpMenuVM offers: Report Bug (WO-596 BugReportView),
// Controls, Reset Hero & Echoes, Credits — plus Dev Tools + the gated dev grant in
// dev builds only. The list itself is VM state; see WO-882 below.
// -----------------------------------------------------------------------------
// WO-F conversion (2026-07-03, coverage matrix row #44): UIDocument/UITK panel
// -> code-built uGUI on the Obsidian master frame (BuildObsidianModal: Blink
// FrameCore + medallion + the ONE shared Close + tap-outside scrim). The old
// UITK card (legacy LegacyRuntime.ttf text, own runtime PanelSettings, borrowed
// theme) is retired — this file is the REFERENCE conversion for the rest of the
// UIDocument family. Spawned by HelpMenuBootstrap (RuntimeInitializeOnLoad).
//
// AdminOverlay handoff kept: "Dev tools" lends AdminOverlay a runtime
// PanelSettings (AdminOverlay is still UITK); we synthesize one on demand now
// that this menu no longer renders through a UIDocument itself.
//
// WO-882 (2026-08-05) - THE BLANK THIRD BUTTON. This file is now VIEW ONLY:
//   * The ENTRY LIST moved to HelpMenuVM. The View walks vm.Entries and stamps one
//     kit row per entry; it does NOT decide which rows exist and it must never
//     skip/guard an entry itself (skipping in the View leaves the unavailable entry
//     in the model for the next consumer). Adding a row means adding a CANDIDATE in
//     the VM - HelpMenuEntryRegression fails the gate on a literal row here.
//   * The well is snapped to WHOLE rows (ScrollWellRowSnap) + a fixed-pixel
//     "showing N of M" hint band. Measured from the WO-882 capture: the mask cut
//     row 3 at 36 of its 146 px, so the button drew but its centred label did not -
//     a tappable box with no text. Half-rows are now impossible.
//   * The old "Dev Tools" label override forced ElarionUi.Ink (near-black, 0.14/
//     0.10/0.06) onto the label. That was written when Yellow meant a GOLD face;
//     since 2026-07-16 ObsidianButtonSpriteName resolves EVERY colour to the grey
//     plate, so the override painted dark ink on a dark grey button - a second,
//     genuinely label-less row. Dropped: the kit owns label ink (Parchment).
//   * TMP strings are ASCII-only (the toast bullets + em dash rendered as tofu).
//
// WO-884 (2026-08-06) - ONLY ONE ROW OF FIVE. Owner, live felt-test: "in settings
// everything is missing ... only see report an issue". CAPTURED PROOF, Player.log
// (single play session, no exception anywhere between the two lines):
//   [Flow:UI] BuildObsidianPanel 'Help' frame=frame_core ... bodyYMin 0.075->0.378
//             footerY 0.155->0.233 reservedYMin=0.378 panelAnchors=(0.26,0.12)-(0.74,0.88)
//             canvasH=1035 closeBandTop=0.218
//   [Flow:UI] HelpMenu: 5 entr(ies) stamped from HelpMenuVM (dev context=True, ...)
// So all five rows WERE built (not data-empty, not thrown) - they were CULLED BY
// LAYOUT. Arithmetic off those exact captured numbers:
//   panel 0.76*1035 = 786.6 px; FrameCore declares a 0.130-tall FOOTER band this
//   menu never fills; the kit re-seats it above the shared Close box (0.233-0.363)
//   and floors the BODY above THAT (0.378). Body = 359.6 px, well = 330.9 px.
//   ScrollWellRowSnap: pass 1 -> 2 whole rows; 5 > 2 so the hint band is paid for
//   (60 px) -> pass 2 -> ONE row. Row 1 is "Report a Bug". Exactly the report.
// THREE fixes, all here, none in the shared kit:
//   1. FOOTER RECLAIM - this menu has no footer content, so take the dead band back:
//      re-seat layout.body's bottom edge a fixed 20 px above the Close box (the same
//      "the screen re-seats its own zone" idiom SettingsController uses for header).
//   2. LANDSCAPE PANEL - 0.26-0.74 x 0.12-0.88 is a portrait window. The target
//      surface is 2670x1200 LANDSCAPE: height is the scarce axis, width is free.
//   3. COLUMN WRAP - a single column of five 132 px rows needs 732 px and cannot fit
//      any panel on a 1035 px canvas (proven, not guessed). Rows now lay out in a
//      fixed-pixel GRID that wraps to TWO columns when one column cannot seat every
//      entry - 5 entries become 3 rows (432 px) and all of them are on screen.
// The snap + the "showing N of M" hint STAY as the safety net (a narrower canvas can
// still overflow); they are no longer the thing standing between the owner and her
// menu. Row height is untouched (CanonCtaHeight) - fix layout, never shrink controls.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class HelpMenu : MonoBehaviour
    {
        public static HelpMenu Instance { get; private set; }

        private ElarionUiKit.ObsidianModal _modal;
        private ElarionUiKit.ConfirmModal _resetConfirm;
        private ElarionUiKit.ToastParts _toast;
        private float _toastUntil;

        // ── WO-882 view state: the VM owns WHAT is listed, these own HOW it lays out ──
        private HelpMenuVM _vm;
        private RectTransform _stack;          // the row grid (ScrollRect content)
        private RectTransform _wellRt;         // the masked well the grid scrolls inside
        private RectTransform _bodyZone;       // layout.body, when the frame carries one (WO-884)
        private UnityEngine.UI.GridLayoutGroup _grid;   // WO-884: 1 or 2 columns, fixed-px cells
        private ScrollWellRowSnap _wellSnap;   // keeps the well a whole number of rows tall
        private TMPro.TMP_Text _moreHint;      // fixed-pixel "showing N of M" band

        /// <summary>Fixed row height in canvas units - the kit CTA height, already >= MinTouchPx.</summary>
        public static readonly float RowHeightPx = ElarionUiKit.CanonCtaHeight;

        /// <summary>Fixed gap between rows - matches ElarionUiKit.BuildButtonColumn's default.</summary>
        public const float RowGapPx = 18f;

        /// <summary>Fixed hint band height. >= one FontLabel line box (40 * 1.25 = 50).</summary>
        public const float HintBandPx = 52f;

        /// <summary>Fixed breathing gap between the well's snapped bottom and the hint band.</summary>
        public const float HintGapPx = 8f;

        // ── WO-884 footer reclaim + column wrap ──────────────────────────────────
        /// <summary>Lower edge of the kit's ONE shared Close band, as a fraction of the panel.
        /// Mirrors the private ElarionUiKit.DefaultCloseZone.y (ElarionUiKit.cs:296) - the box is
        /// seated there and grown UP by CanonCtaHeight (SeatSharedCloseInside).</summary>
        public const float CloseBandBottomFrac = 0.050f;

        /// <summary>Fixed clearance in canvas px between the top of that Close box and the body's
        /// reclaimed floor. Matches the kit's own 0.020-of-panel gap at the reference panel size.</summary>
        public const float CloseClearPx = 20f;

        /// <summary>A second column is only worth taking when each cell stays this wide in canvas
        /// px - narrower and FitSingleLine would shrink "Grant Resources (dev)" to a squint.</summary>
        public const float MinCellWidthPx = 260f;

        /// <summary>Hard ceiling on the wrap: two columns is a menu, three is a keypad.</summary>
        public const int MaxColumns = 2;

        // DEF-212 modal arbiter handle. The Help menu is a full-screen modal, so it
        // MUST route through PanelManager like every other panel — otherwise it stacks
        // over open content and its scrim can trap the player.
        private PanelHandle _panelHandle;

        private void Awake()
        {
            Instance = this;
            _panelHandle = PanelManager.Register("Help", Close, () => IsOpen);
            // WO-1399: the one reflection-free door onto this menu (Settings' "Help" row).
            PanelRouter.Register(PanelId.Help, Open);
        }

        private void OnDestroy()
        {
            CloseResetConfirm();
            if (Instance == this) Instance = null;
            PanelRouter.Unregister(PanelId.Help, Open);
            if (_vm != null)
            {
                _vm.Changed -= OnEntriesChanged;
                _vm.Dispose();
                _vm = null;
            }
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        /// <summary>True while the Help modal is visible.</summary>
        public bool IsOpen => _modal != null && _modal.canvas != null && _modal.canvas.activeSelf;

        // ── AdminOverlay handoff (T-030) ─────────────────────────────────────────
        // AdminOverlay is still UITK and needs a PanelSettings to render. This menu
        // no longer owns a UIDocument, so we synthesize a runtime PanelSettings on
        // demand (own unique name — OnboardingPanelGuard matches by name and must
        // never tear this down; theme borrowed from any live doc so fonts inherit).
        private PanelSettings _adminPanelSettings;

        // ── Hidden dev unlock (owner ask 2026-07-12) ─────────────────────────────
        // Mobile has no Ctrl+Shift+A chord and release builds compile-strip the Dev
        // Tools launcher (LB-11), so on a phone there was NO way to dev-grant
        // resources. 5 taps on this card's TITLE within a 3s window flips a
        // persisted unlock (PlayerPrefs) that reveals a minimal "Grant Resources"
        // action — the grant ONLY, not the full AdminOverlay, so the LB-11 release
        // lock on the admin panel itself stays intact.
        // SECURITY (store-hardening Path A, S1): the 5-tap dev resource-grant is compile-STRIPPED from
        // release (non-Development) builds so a public/store APK cannot self-grant unlimited resources.
        // Preserved in Editor/Development builds so the owner keeps the on-phone dev grant while developing.
        // WO-882: the tap COUNTER + the unlock rule moved into HelpMenuVM.TapTitle (they
        // are state). Only the persistence KEY stays here - the VM is UnityEngine-free and
        // reads/writes the pref through MenuHost (IHost.DevUnlockPersisted).
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        private const string DevUnlockPref = "dotr.devunlock";
#endif

        public PanelSettings ActivePanelSettings
        {
            get
            {
                if (_adminPanelSettings != null) return _adminPanelSettings;
                _adminPanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _adminPanelSettings.name = "HelpRuntimePanelSettings";
                _adminPanelSettings.sortingOrder = 2700;
                foreach (var existing in UnityEngine.Object.FindObjectsByType<UIDocument>(
                             FindObjectsInactive.Include))
                {
                    if (existing == null || existing.panelSettings == null) continue;
                    if (existing.panelSettings.themeStyleSheet != null)
                    {
                        _adminPanelSettings.themeStyleSheet = existing.panelSettings.themeStyleSheet;
                        break;
                    }
                }
                return _adminPanelSettings;
            }
        }

        private void Update()
        {
            if (_toast != null && _toast.card != null && _toastUntil > 0f
                && Time.unscaledTime > _toastUntil)
            {
                _toast.card.SetActive(false);
                _toastUntil = 0f;
            }
        }

        // ── UI construction (lazy — first open builds) ───────────────────────────
        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;

            // Taller modal so the action rows fit without overlap (owner 2026-07-16 "layers stacked").
            // WO-884: widened 0.26-0.74 -> 0.18-0.82 and heightened 0.12-0.88 -> 0.08-0.92. The old
            // window was portrait-shaped; the shipping surface is 2670x1200 LANDSCAPE, where height
            // is the scarce axis (canvasH=1035 in the capture) and width is nearly free. This buys
            // the well ~157 px of the ~400 px the row list needs; the footer reclaim + column wrap
            // below buy the rest.
            _modal = ElarionUiKit.BuildObsidianModal("HelpMenuUI",
                new LocalizedText("settings.help.help").Resolve(),
                new Vector2(0.18f, 0.08f), new Vector2(0.82f, 0.92f), Close,
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "settings");

            bool bodyIsZone = _modal.chrome.layout != null && _modal.chrome.layout.body != null;
            _bodyZone = bodyIsZone ? _modal.chrome.layout.body : null;
            var body = bodyIsZone
                ? _modal.chrome.layout.body.transform
                : _modal.chrome.content.transform;

            // -- WO-795: scrollable button well (RumorBoardPanel Viewport/Content pattern) --
            // The plain BuildButtonColumn VLG does not clip: with 4 release rows (6 in dev
            // builds) at the 112px touch floor / 132px preferred height the tail overflowed
            // the body rect and collided with the kit's bottom-center shared Close band
            // (DefaultCloseZone, fixed 360x132 box growing up from panel y=0.050). Wrap the
            // column in a masked vertical ScrollRect sized to end ABOVE that band:
            //  - Zone_Body path: the kit factory already raises the zone's bottom edge above
            //    the Close box (close-band reservation), so the well fills the zone using the
            //    column's old insets (0.06 side, 0.04 top/bottom) - same visual rect as before.
            //  - chrome.content fallback: no reservation exists there, so anchor the well's
            //    bottom at 0.24 of the panel - clear of the worst-case (landscape) top of the
            //    fixed Close box (~0.22) plus a gap.
            var wellGo = new GameObject("ButtonScrollWell",
                typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.RectMask2D),
                typeof(UnityEngine.UI.ScrollRect));
            wellGo.transform.SetParent(body, false);
            var wellRt = wellGo.GetComponent<RectTransform>();
            wellRt.anchorMin = bodyIsZone ? new Vector2(0.06f, 0.04f) : new Vector2(0.06f, 0.24f);
            wellRt.anchorMax = bodyIsZone ? new Vector2(0.94f, 0.96f) : new Vector2(0.94f, 0.875f);
            wellRt.offsetMin = Vector2.zero;
            wellRt.offsetMax = Vector2.zero;
            wellGo.GetComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.001f); // drag catcher
            _wellRt = wellRt;

            // -- WO-884: the row host is a GRID, not a single column. ----------------
            // A VerticalLayoutGroup can only ever stack ONE row per line, and a single
            // column of five CanonCtaHeight rows needs 5*150-18 = 732 canvas px - more
            // than any panel can offer on the captured 1035 px canvas. The grid wraps to
            // a second column when (and only when) one column cannot seat every entry,
            // which turns 5 rows into 3 and puts the whole menu on screen. Cell size is
            // set in FIXED CANVAS PIXELS by LayoutColumns once the well has a resolved
            // rect - never a fraction of the parent, so ClampMinTouch can never inflate a
            // cell over its neighbour. It doubles as the ScrollRect CONTENT: top-stretched
            // (pivot 0.5,1) with a ContentSizeFitter, so any residual overflow scrolls
            // instead of spilling into the Close band.
            var gridGo = new GameObject("ButtonGrid", typeof(RectTransform),
                typeof(UnityEngine.UI.GridLayoutGroup), typeof(UnityEngine.UI.ContentSizeFitter));
            gridGo.transform.SetParent(wellGo.transform, false);
            var stack = (RectTransform)gridGo.transform;
            stack.anchorMin = new Vector2(0f, 1f);
            stack.anchorMax = new Vector2(1f, 1f);
            stack.pivot     = new Vector2(0.5f, 1f);
            stack.offsetMin = Vector2.zero;
            stack.offsetMax = Vector2.zero;
            _grid = gridGo.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            _grid.startCorner    = UnityEngine.UI.GridLayoutGroup.Corner.UpperLeft;
            _grid.startAxis      = UnityEngine.UI.GridLayoutGroup.Axis.Horizontal;  // reading order
            _grid.childAlignment = TextAnchor.UpperCenter;
            _grid.constraint     = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 1;                       // resolved in LayoutColumns
            _grid.spacing        = new Vector2(RowGapPx, RowGapPx);
            _grid.cellSize       = new Vector2(MinCellWidthPx, RowHeightPx);  // width resolved later
            var stackFit = gridGo.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            stackFit.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            stackFit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;

            var wellScroll = wellGo.GetComponent<UnityEngine.UI.ScrollRect>();
            wellScroll.viewport = wellRt;
            wellScroll.content  = stack;
            wellScroll.horizontal = false;
            wellScroll.vertical   = true;
            wellScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            wellScroll.scrollSensitivity = 25f;
            FlowTrace.Step("UI", "HelpMenu: button column wrapped in ScrollRect well (WO-795) bodyIsZone=" + bodyIsZone);

            // -- WO-882: whole-row snap. The mask must never cut ACROSS a row (a clipped
            // row draws its plate but not its centred label = a blank button). Fixed
            // pixels, never a fraction of the parent. --------------------------------
            _wellSnap = wellGo.AddComponent<ScrollWellRowSnap>();
            _wellSnap.rowHeightPx = RowHeightPx;
            _wellSnap.rowGapPx = RowGapPx;
            _wellSnap.reserveBottomPx = 0f;

            // -- WO-882: fixed-pixel "there is more below" band, pinned to the well's
            // ORIGINAL bottom anchor line (the snap only raises the well above it). --
            _moreHint = ElarionUiKit.Label(body, "", 0f, 0f, ElarionUi.ParchmentDim,
                ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center,
                wellRt.anchorMin.x, wellRt.anchorMax.x);
            var hintRt = _moreHint.rectTransform;
            hintRt.anchorMin = new Vector2(wellRt.anchorMin.x, wellRt.anchorMin.y);
            hintRt.anchorMax = new Vector2(wellRt.anchorMax.x, wellRt.anchorMin.y);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.anchoredPosition = Vector2.zero;
            hintRt.sizeDelta = new Vector2(0f, HintBandPx);
            _moreHint.raycastTarget = false;
            _moreHint.gameObject.SetActive(false);

            // -- WO-882: the VM owns the entry list; this View only stamps it. --------
            _stack = stack;
            if (_vm == null)
            {
                _vm = HelpMenuVM.CreateDefault(new MenuHost(this));
                _vm.Changed += OnEntriesChanged;
            }
            BuildRows();

#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
            // Hidden dev unlock (owner 2026-07-12): 5 taps on the card TITLE (a TMP Graphic
            // — it carries the Button directly; no extra widget). The counter + window now
            // live in HelpMenuVM.TapTitle; revealing the grant row is a VM rebuild, not a
            // SetActive on a row the View pre-built. SECURITY (store-hardening Path A, S1):
            // compile-STRIPPED from release so a public/store APK has no unlock path at all.
            if (_modal.chrome != null && _modal.chrome.title != null)
            {
                _modal.chrome.title.raycastTarget = true;
                var titleBtn = _modal.chrome.title.gameObject.GetComponent<UnityEngine.UI.Button>();
                if (titleBtn == null) titleBtn = _modal.chrome.title.gameObject.AddComponent<UnityEngine.UI.Button>();
                titleBtn.transition = UnityEngine.UI.Selectable.Transition.None;
                titleBtn.targetGraphic = _modal.chrome.title;
                titleBtn.onClick.AddListener(OnTitleTapped);
            }
#endif

            // Toast (status messages) — kit ToastCard, low-center, fades after 5s.
            // (dev-unlock handlers live below with the other On* handlers)
            _toast = ElarionUiKit.ToastCard(_modal.canvas.transform,
                ElarionUiKit.ToastTone.Info, accentLeft: true, TextAnchor.MiddleCenter);
            var trt = _toast.card.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.14f, 0.045f);
            trt.anchorMax = new Vector2(0.86f, 0.115f);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            _toast.card.SetActive(false);

            // Snap + hint WHILE the canvas is still active — the headless UI capture
            // (UICaptureLaunch.CaptureHelpMenu) only calls EnsureBuilt, never SetOpen, so
            // this is the one pass it gets. ScrollWellRowSnap re-runs on every later
            // dimension change (both capture aspects, device rotation).
            RefreshWell();

            _modal.canvas.SetActive(false);   // built hidden; SetOpen shows it
        }

        // ── WO-882 row rendering — the View stamps EXACTLY what the VM offers ─────

        /// <summary>Re-stamp the button column from <c>vm.Entries</c>. No entry is filtered,
        /// skipped or guarded here: an entry the View cannot render is one the VM must not
        /// have emitted (HelpMenuVM.Entry.IsRenderable).</summary>
        private void BuildRows()
        {
            if (_stack == null || _vm == null) return;

            for (int i = _stack.childCount - 1; i >= 0; i--)
            {
                var child = _stack.GetChild(i).gameObject;
                child.transform.SetParent(null, false);   // detach NOW so the grid stops counting it
                // The headless UI capture builds this modal in EDIT mode, where Destroy is illegal.
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            var entries = _vm.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                ElarionUiKit.AddColumnButton(_stack, entry.Label,
                    entry.Danger ? ElarionUiKit.ObsidianButtonColor.Red
                                 : ElarionUiKit.ObsidianButtonColor.Gray,
                    entry.Command);
            }

            FlowTrace.Step("UI", "HelpMenu: " + entries.Count + " entr(ies) stamped from HelpMenuVM (dev context="
                + _vm.IsDevContext + ", dev unlocked=" + _vm.DevUnlocked + ")");
        }

        /// <summary>
        /// WO-884 FOOTER RECLAIM. FrameCore declares a footer band (ZonesFor, ElarionUiKit.cs:422)
        /// that THIS menu never fills. The kit re-seats that band above the shared Close box and
        /// then floors the BODY above the band, which on the captured build cost 0.303 of the panel
        /// - 238 canvas px of empty strip - and starved the row well down to a single visible row.
        /// Take it back: drop the body's bottom edge to a FIXED clearance above the Close box.
        ///
        /// Runs from RefreshWell (post <c>Canvas.ForceUpdateCanvases</c>) rather than EnsureBuilt
        /// because the panel rect reads RAW SCREEN PIXELS on the canvas's creation frame - the exact
        /// trap the kit documents at ElarionUiKit.cs:612. Idempotent, and re-runs on rotation.
        /// </summary>
        private void ReclaimFooterBand()
        {
            if (_bodyZone == null) return;
            var panel = _bodyZone.parent as RectTransform;
            if (panel == null) return;

            float panelPx = panel.rect.height;
            if (panelPx <= 1f) return;                       // layout not resolved yet - try next open

            // The shared Close is a fixed CanonCtaHeight box grown UP from CloseBandBottomFrac.
            float floorFrac = (CloseBandBottomFrac * panelPx + ElarionUiKit.CanonCtaHeight + CloseClearPx)
                              / panelPx;

            // Never invert or hair-thin the body on a degenerate (very short) panel - in that case
            // the kit's own conservative floor is the safer answer and the well scrolls instead.
            if (floorFrac >= _bodyZone.anchorMax.y - 0.05f) return;
            if (_bodyZone.anchorMin.y <= floorFrac + 0.001f) return;   // already at/below the floor

            _bodyZone.anchorMin = new Vector2(_bodyZone.anchorMin.x, floorFrac);
            _bodyZone.offsetMin = new Vector2(_bodyZone.offsetMin.x, 0f);
            FlowTrace.Step("UI", "HelpMenu: footer band reclaimed - body floor "
                + _bodyZone.anchorMin.y.ToString("F3") + " (panel " + panelPx.ToString("F0") + "px)");
        }

        /// <summary>
        /// WO-884 COLUMN WRAP. One column while every entry fits in the well; two when it does not
        /// and the cells stay legible. Cell size is FIXED CANVAS PIXELS measured off the resolved
        /// well - never a fraction - so a cell can never resolve under the kit touch floor and get
        /// grown symmetrically over its neighbour. Row HEIGHT is never reduced (owner law: fix the
        /// layout, do not shrink the controls).
        /// </summary>
        private void LayoutColumns()
        {
            if (_grid == null || _stack == null || _wellRt == null || _vm == null) return;

            // offsetMin.y is ScrollWellRowSnap's own trim, so the UNTRIMMED well height - the space
            // actually available to lay out into - is rect.height + that trim.
            float wellPx = _wellRt.rect.height + _wellRt.offsetMin.y;
            // The grid is anchor-stretched to the well, so either rect answers; take whichever has
            // resolved (a ContentSizeFitter can leave the content's own width at 0 on frame one).
            float widthPx = Mathf.Max(_stack.rect.width, _wellRt.rect.width);
            if (wellPx <= 1f || widthPx <= 1f) return;       // not measured yet

            int count = Mathf.Max(1, _vm.Entries.Count);
            float pitch = RowHeightPx + RowGapPx;

            int cols = 1;
            while (cols < MaxColumns)
            {
                int rows = Mathf.CeilToInt(count / (float)cols);
                if (rows * pitch - RowGapPx <= wellPx) break;             // this many columns fits
                float nextCell = (widthPx - RowGapPx * cols) / (cols + 1);
                if (nextCell < MinCellWidthPx) break;                     // another column would squint
                cols++;
            }

            _grid.constraintCount = cols;
            _grid.cellSize = new Vector2((widthPx - RowGapPx * (cols - 1)) / cols, RowHeightPx);
        }

        /// <summary>Reclaim the dead footer band, wrap the rows into as many columns as it takes,
        /// snap the well to whole rows, then text-encode anything still off-screen. Two snap
        /// passes: measure without a reserve, and only pay for the hint band when the list
        /// actually overflows.</summary>
        private void RefreshWell()
        {
            if (_wellSnap == null || _vm == null) return;

            Canvas.ForceUpdateCanvases();
            // Reclaim FIRST - it changes the well's height, which every step below measures.
            ReclaimFooterBand();
            Canvas.ForceUpdateCanvases();
            LayoutColumns();
            if (_stack != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_stack);

            _wellSnap.reserveBottomPx = 0f;
            _wellSnap.Snap(true);

            int total = _vm.Entries.Count;
            int cols = Mathf.Max(1, _grid != null ? _grid.constraintCount : 1);
            int totalRows = Mathf.CeilToInt(total / (float)cols);
            // VisibleRows stays 0 until a layout pass resolves the well - never claim
            // "showing 0 of N" off an unmeasured rect; the next open re-runs this.
            bool overflows = _wellSnap.VisibleRows > 0 && totalRows > _wellSnap.VisibleRows;
            if (overflows)
            {
                _wellSnap.reserveBottomPx = HintBandPx + HintGapPx;
                _wellSnap.Snap(true);
                overflows = _wellSnap.VisibleRows > 0 && totalRows > _wellSnap.VisibleRows;
            }

            FlowTrace.Step("UI", "HelpMenu: well laid out - " + total + " entries in " + cols
                + " column(s), " + totalRows + " row(s), " + _wellSnap.VisibleRows + " visible row(s)");

            if (_moreHint == null) return;
            if (overflows)
            {
                // Text-encoded state (never colour alone), ASCII only. Counts ENTRIES, not rows -
                // with two columns a "row" is two menu items and the player counts items.
                int shown = Mathf.Min(total, _wellSnap.VisibleRows * cols);
                // WO-1857: ONE sentence with named holes, not four concatenated fragments.
                _moreHint.text = LocalText.Format("hud.help_menu.overflow_hint", shown, total);
                _moreHint.gameObject.SetActive(true);
            }
            else
            {
                _moreHint.gameObject.SetActive(false);
            }
        }

        /// <summary>The VM changed its entry list (e.g. the dev unlock opened a row) — re-stamp.</summary>
        private void OnEntriesChanged()
        {
            BuildRows();
            RefreshWell();
        }

        // ── Actions ────────────────────────────────────────────────────────────
        public void ToggleOverlay()
        {
            // WO-1399: this trace used to call the menu "Settings" (gear -> ToggleOverlay) - the
            // misnomer was baked into the evidence. This is the HELP menu; the gear dock no
            // longer calls it (AutoPilot's CaptureDockOverlays still does).
            FlowTrace.Step("UI", $"HelpMenu: toggle requested (currently open={IsOpen})");
            SetOpen(!IsOpen);
        }

        /// <summary>WO-1399: the PanelRouter opener for <see cref="PanelId.Help"/> - Settings'
        /// "Help" row lands here. Idempotent: an already-open menu stays open.</summary>
        public void Open()
        {
            FlowTrace.Step("UI", $"HelpMenu: open requested via PanelId.Help (currently open={IsOpen})");
            SetOpen(true);
        }

        /// <summary>Explicitly hide the Help modal (shared Close + modal-arbiter close).</summary>
        public void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            if (open) EnsureBuilt();
            if (_modal == null || _modal.canvas == null) return;
            _modal.canvas.SetActive(open);
            // Route through the modal arbiter (DEF-212): opening closes any other open
            // panel; closing clears our slot. NotifyOpened/Closed are no-ops when state
            // is unchanged, so the handle's Close callback won't recurse.
            if (open) PanelManager.NotifyOpened(_panelHandle);
            else PanelManager.NotifyClosed(_panelHandle);
            if (open) RefreshWell();   // re-snap + refresh the hint at the live screen size
            FlowTrace.Step("UI", $"HelpMenu {(open ? "shown" : "hidden")} - kit modal active={_modal.canvas.activeSelf} timeScale={Time.timeScale}");
        }

        /// <summary>WO-596 — route to the player bug-report form. Close FIRST so the
        /// form's clean-frame capture never includes this menu.</summary>
        private void OnReportBug()
        {
            FlowTrace.Step("BugReport", "Settings -> Report a bug — opening BugReportView");
            Close();
            BugReportView.Open();
        }

        private void OnShowControls()
        {
            // ASCII-ONLY (WO-882): the em dash + bullet glyphs rendered as tofu boxes on device.
            ShowToast(new LocalizedText("hud.help_menu.controls_body").Resolve());
        }

        private void OnShowCredits()
        {
            // Credits accuracy (2026-08-04): the previous string claimed "Audio: original
            // soundtrack", which was affirmatively FALSE - the music is owner-original
            // (Suno Pro), but a large share of the shipping SFX is third-party licensed
            // (leohpaz RPG Essentials, Unity Asset Store EULA; Hovl Studio skill sounds
            // inside the VFX prefabs). ASCII-ONLY: non-ASCII renders as tofu on device.
            ShowToast(new LocalizedText("hud.help_menu.credits_body").Resolve());
        }

        // SECURITY (store-hardening Path A, S1): the 5-tap dev unlock + resource grant are stripped from
        // release builds (see the guarded call sites + fields above). Preserved in Editor/Development.
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        /// <summary>Title tap (owner 2026-07-12). The 5-tap COUNTER + the rolling window are
        /// VM state (HelpMenuVM.TapTitle); the View only forwards the tap and the clock.
        /// The VM raises Changed on unlock, which re-stamps the rows.</summary>
        private void OnTitleTapped()
        {
            if (_vm == null) return;
            bool was = _vm.DevUnlocked;
            _vm.TapTitle(Time.unscaledTime);
            if (!was && _vm.DevUnlocked)
            {
                FlowTrace.Step("UI", "HelpMenu: dev unlock flipped ON (5-tap title) - Grant Resources offered by the VM.");
                ShowToast("Dev actions unlocked.");
            }
        }

        /// <summary>
        /// Grants the AdminOverlay full-resource bundle (wood/food/iron/crystals + coins)
        /// through EconomyService.GrantSpendableUncapped — which writes Wood/Iron into BOTH
        /// wallets (in-session pool + GameState) so shop AND upgrade flows can spend it.
        /// HUD can't reference DeNelle.Village, so reached by reflection — the exact
        /// AdminOverlay.OnLoadResources idiom (the documented HUD→Village seam).
        /// </summary>
        private void OnGrantResources()
        {
            var ecoType = Type.GetType("DeNelle.Village.EconomyService, DeNelle.Village");
            var instProp = ecoType?.GetProperty("Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var eco = instProp?.GetValue(null);
            if (eco == null) { ShowToast("Grant failed - economy not alive yet."); return; }

            // ⚠ UNCAPPED, DELIBERATELY (audit 2026-08-15) — same fix as AdminOverlay /
            // OwnerDevToolsOverlay, which this method's idiom is copied from. "GrantSpendable"
            // routes through the TownBankCapacity clamp, so this 50,000 wood grant landed ~2,500
            // and the toast below still announced 50k. The lookup is reflection-BY-STRING, so
            // nothing but DevGrantUncappedRegression can see it if it drifts back.
            var grant = ecoType.GetMethod("GrantSpendableUncapped",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(int), typeof(int), typeof(int), typeof(int) }, null);
            if (grant == null) { ShowToast("Grant failed - GrantSpendableUncapped not found."); return; }
            grant.Invoke(eco, new object[] { 50000, 25000, 50000, 25000 }); // wood, food, iron, crystals

            var addCoins = ecoType.GetMethod("AddCoins",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(int) }, null);
            if (addCoins != null) addCoins.Invoke(eco, new object[] { 50000 });

            FlowTrace.Step("UI", "HelpMenu: dev Grant Resources fired (50k wood/iron, 25k stone/crystals, 50k coins).");
            ShowToast("Granted: 50k wood/iron, 25k stone/crystals, 50k gold.");
        }
#endif // DEVELOPMENT_BUILD || UNITY_EDITOR — 5-tap dev resource grant (store-hardening S1)

        /// <summary>Raises the shared confirmation sheet before destructive progress reset.</summary>
        private void OnResetProgress()
        {
            if (_resetConfirm != null && _resetConfirm.canvas != null) return;
            FlowTrace.Step("UI", "HelpMenu: reset Hero & Echoes requested - showing confirmation");
            _resetConfirm = ElarionUiKit.BuildConfirmModal(
                "ResetProgressConfirm",
                new LocalizedText("hud.help_menu.reset_confirm_title").Resolve(),
                new LocalizedText("hud.help_menu.reset_confirm_body").Resolve(),
                new LocalizedText("hud.help_menu.reset_confirm_accept").Resolve(),
                new LocalizedText("hud.help_menu.reset_confirm_cancel").Resolve(),
                onConfirm: () =>
                {
                    FlowTrace.Step("UI", "HelpMenu: reset Hero & Echoes confirmed");
                    CloseResetConfirm();
                    ResetProgressConfirmed();
                },
                onCancel: () =>
                {
                    FlowTrace.Step("UI", "HelpMenu: reset Hero & Echoes cancelled");
                    CloseResetConfirm();
                },
                confirmKind: ElarionUiKit.ButtonKind.Danger);
        }

        /// <summary>Resets save state after confirmation, then routes back to HeroSelect.</summary>
        private void ResetProgressConfirmed()
        {
            try
            {
                // WO-1511: GameStateService and SceneRouter both live in DeNelle.Core, which
                // DeNelle.HUD.asmdef already references ("DeNelle.Core" is its first entry) —
                // so both are called directly. The two "type missing" toasts they used to
                // guard are unreachable by construction now (a missing type is a compile
                // error, not a runtime toast); the LIVE guard — service not alive — is kept
                // verbatim, as is the try/catch below.
                var service = DeNelle.Core.State.GameStateService.Instance;
                if (service == null)
                {
                    ShowToast(new LocalizedText("hud.help_menu.reset_failed_service").Resolve());
                    return;
                }
                service.ResetToNewGame();

                ShowToast(new LocalizedText("hud.help_menu.reset_success").Resolve());
                DeNelle.Core.SceneRouter.GoHeroSelect();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[HelpMenu] Reset failed: " + ex.Message);
                ShowToast(new LocalizedText("hud.help_menu.reset_failed_generic").Resolve());
            }
        }

        private void CloseResetConfirm()
        {
            if (_resetConfirm != null && _resetConfirm.canvas != null)
            {
                if (Application.isPlaying) Destroy(_resetConfirm.canvas);
                else DestroyImmediate(_resetConfirm.canvas);
            }
            _resetConfirm = null;
        }

        /// <summary>Opens the AdminOverlay (owner tools). SECURITY (LB-11):
        /// compile-stripped from release builds along with its launcher.</summary>
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        private void OnOpenDevTools()
        {
            FlowTrace.Step("UI", "DevPanel toggle/click reached (HelpMenu 'Dev tools' -> AdminOverlay)");
            // Spawn-or-find AdminOverlay and hand it a live PanelSettings (T-030: hub
            // scenes ship no UIDocument of their own; without this Open() no-ops).
            var admin = UnityEngine.Object.FindAnyObjectByType<AdminOverlay>(FindObjectsInactive.Include);
            if (admin == null)
            {
                var go = new GameObject("AdminOverlay");
                SceneManager.MoveGameObjectToScene(go, gameObject.scene);
                admin = go.AddComponent<AdminOverlay>();
            }
            if (!admin.TryBuild(ActivePanelSettings))
            {
                FlowTrace.Warn("UI", "DevPanel open FAILED — AdminOverlay.TryBuild returned false " +
                    "(no PanelSettings in this scene; dev tools went nowhere)");
                ShowToast("Dev tools unavailable - no UI panel settings in this scene.");
                return;
            }
            FlowTrace.Step("UI", "DevPanel built — opening AdminOverlay");
            // Close Help FIRST, then open Admin — both route through PanelManager.
            Close();
            admin.Open();
        }
#endif // DEVELOPMENT_BUILD || UNITY_EDITOR — dev tools launcher

        private void ShowToast(string message)
        {
            if (_toast == null || _toast.card == null || _toast.label == null) return;
            _toast.label.text = message;
            _toast.card.SetActive(true);
            _toastUntil = Time.unscaledTime + 5f;
        }

        // ── WO-882: the VM's Unity-side seam ─────────────────────────────────────
        // HelpMenuVM references NO UnityEngine type, so the dev CONTEXT and the
        // persisted unlock reach it through here. Command bodies just forward to the
        // View's existing handlers; the two dev commands are compile-stripped inside
        // (the interface members stay so the release build still implements IHost).
        private sealed class MenuHost : HelpMenuVM.IHost
        {
            private readonly HelpMenu _menu;
            public MenuHost(HelpMenu menu) { _menu = menu; }

            public bool IsDevContext
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
                get { return true; }
#else
                get { return false; }
#endif
            }

            public bool DevUnlockPersisted
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
                get { return PlayerPrefs.GetInt(DevUnlockPref, 0) == 1; }
                set { PlayerPrefs.SetInt(DevUnlockPref, value ? 1 : 0); PlayerPrefs.Save(); }
#else
                get { return false; }
                set { }
#endif
            }

            public void ReportBug() { if (_menu != null) _menu.OnReportBug(); }
            public void ShowControls() { if (_menu != null) _menu.OnShowControls(); }
            public void ShowCredits() { if (_menu != null) _menu.OnShowCredits(); }
            public void ResetProgress() { if (_menu != null) _menu.OnResetProgress(); }
            public void CloseMenu() { if (_menu != null) _menu.Close(); }

            public void OpenDevTools()
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
                if (_menu != null) _menu.OnOpenDevTools();
#endif
            }

            public void GrantResources()
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
                if (_menu != null) _menu.OnGrantResources();
#endif
            }
        }
    }
}
