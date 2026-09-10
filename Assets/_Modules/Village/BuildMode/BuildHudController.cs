// =============================================================================
// BuildHudController — the dedicated Build HUD presentation layer (Grok slice 1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// ONE landscape ElarionUiKit canvas (1920x1080, MatchWidthOrHeight=0.5) that owns
// the Build Mode edit-chrome as a single surface, ending the "seat wars" between
// the old fragmented canvases (BuildPlaceButton + the LeanTouchBuildDriver verb
// bar + BuildSelectionUI + the portrait palette header). BuildModeController — the
// BRAIN — drives it via Show/Hide/SetState/RefreshResources; the HUD calls back
// into the controller for the place intents (rotate/place/cancel/exit).
//
// THREE STATES (Grok, CoC mental model):
//   Browse   — shop open; NO verb rail; tap a placed building to select it.
//   Placing  — the LEAN VERB ROW (confirm / rotate / cancel, horizontal since COLUMN-FIT 2026-08-16,
//              seated bottom-right above the resource strip); the shop
//              collapses to the armed-card summary (BuildPaletteUI.Collapse).
//   Selected — the selection verbs (Move/Upgrade/Sell/Cancel) render on the SAME
//              bar family, owned by BuildSelectionUI (kept — the fleet's
//              AssertBuildMoveChain SELECT link asserts that panel renders).
//
// WO-1010 §7 OWNER RULINGS D14 / D10 / D17 / D19 (2026-08-08) — THE RIGHT EDGE AND
// THE BOTTOM BAND ARE THE ONLY CHROME:
//   D14 — the three placement verbs live in a LEAN, FIXED rail (right-thumb territory in
//         landscape). The old "cluster flanks the ghost and flips sides near an edge"
//         follow logic is DELETED: no chrome sits on or beside the piece any more, so the
//         rail needs no per-frame layout at all. The ghost keeps ONLY its name+cost pill.
//         COLUMN-FIT 2026-08-16 (2026-08-16) LAID THE RAIL OUT HORIZONTALLY, seated just above the D19
//         resource strip in the bottom-RIGHT corner — it is NO LONGER a vertical column.
//         Why: the right edge's VERTICAL axis was over-subscribed. At the 1920x1080
//         reference the column claimed 114 (strip+gap) + 384 (vertical rail) + 9 + 428
//         (quick tabs) + 9 + 112 (Done) + 24 (top inset) = EXACTLY 1080, but the owner's
//         Seeker (2670x1200) resolves to a canvas only 965.4 REFERENCE px tall, so the
//         column overflowed by ~115px and Done landed on the top quick-tab. Turning the
//         rail 90 degrees spends the ABUNDANT axis (2148 ref px of width on that device)
//         instead of the scarce one: the band was 132 WIDE x 384 TALL and is now 384 WIDE
//         x 132 TALL, handing 252px back to the vertical budget. See RailBandW/RailBandH.
//   D10 — the exit is labelled "Done" (the "X" glyph is dropped). SUPERSEDED IN PART by
//         WO-1035 (owner F8 seq 2503, 2026-08-16, "the done should match same style and
//         stack above defense and town button"): the compact hand-rolled corner plate is
//         RETIRED. Done is now the COMMON kit button (ElarionUiKit.BuildObsidianButton,
//         Style1) seated as the TOP ITEM of the palette's D15/D21 right-edge quick-tab
//         column — same box, same x, same rhythm as the Town/Defense tabs (literally the
//         same 260x112 box since COLUMN-FIT 2026-08-16 took the tabs down to the MinTouch floor too). See
//         BuildCornerDone for the seat arithmetic; the 2670x1200 saturation it used to warn
//         about is RESOLVED by COLUMN-FIT 2026-08-16 (923 needed vs 965.4 available), and the clamp there
//         is now a net for shorter surfaces rather than the normal path.
//   D17 — ⛔ RETIRED 2026-09-06 BY WO-1411. This note used to read: "the rail speaks
//         ICON language: confirm renders RpgUiCatalog element/check, rotate
//         element/rotate, cancel element/cross ... MakeVerb is sprite-or-null, so the
//         two-letter ASCII stubs remain the fallback." Both reviewers of the merged UI
//         review, independently and off the same frame, read that rail as "three
//         unlabelled buttons" — which is the owner's names-not-icons ruling, on the one
//         screen where the player is about to spend. The rail now speaks WORDS as its
//         PRIMARY (and only) language: PLACE / ROTATE / CANCEL, kit ButtonPack buttons
//         in the same band, with the confirm flipping to BLOCKED when the placement is
//         refused. MakeVerb + MakeDisc went with the glyphs; MakeWordVerb replaced them.
//         The BAND geometry (RailBandW/RailBandH and the COLUMN-FIT seat) is untouched.
//   D19 — the resource strip moved OFF the top: it is now ONE THIN bottom-centre
//         obsidian band (fixed pixel height), display-only, seated so the carousel and
//         the placement hint own the band above it. With the strip gone the old
//         full-width top bar had no tenant left and was deleted outright — it was 10%
//         of the field in raycast-blocking chrome for a decorative label.
//
// The category tabs + the icon-first card carousel stay in BuildPaletteUI's own canvas
// (its card/art/cost/economy/arm logic is reused verbatim, not rebuilt); this
// controller sequences them by state. RailReservedWidthPx / ResourceStripReservedPx are
// PUBLIC so the carousel lane can seat clear of this lane's bands without a cross-edit.
//
// LANDSCAPE only; panels near-black (WO-562 — do NOT lighten); ASCII-only TMP;
// meaning never by colour alone; code-built uGUI on the kit; ZERO UXML. Control
// sizes are NAMED constants set here (this lane must not touch ElarionUiKit's
// MinTouchPx floor — a separate visual lane owns it).
// =============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>The three Build HUD chrome states (Grok CoC model).</summary>
    public enum BuildHudState { Browse, Placing, Selected }

    /// <summary>
    /// The single Build HUD canvas + state machine. Created/owned by
    /// BuildModeController; parents the wallet row, the BUILD MODE label, the Done
    /// exit, and the single place-intent bar.
    /// </summary>
    public sealed class BuildHudController : MonoBehaviour
    {
        // ── NAMED control sizes (this lane sets its own floors; kit lane owns MinTouchPx) ──
        // At the 1920x1080 landscape reference: Exit/close >= 132px shortest side,
        // every other control >= 112px shortest side (owner touch-target ruling).
        private const int SortingOrder = 906;   // above palette dock (900), below selection (910)

        // ── WO-1010 P1: SMALL VISUAL INSIDE A MINTOUCH HIT PAD ─────────────────
        // External testers (2026-08-08) could not read the build screen: "buttons
        // everywhere". Every verb here therefore keeps a SMALL visible plate inside an
        // INVISIBLE MinTouch hit box — padding, never visual growth. Growing the visual
        // to 112 would put opaque slabs down the right edge and undo the redesign.
        private const float ChipVisualPx = 52f;
        private const float ChipHitPx    = ElarionUiKit.MinTouchPx;   // 112
        private const float ChipEdgePx   = 3f;    // accent border thickness on each plate
        private const float ChipIconPx   = 30f;   // sprite glyph inside the 52px plate
        private const float GhostPillW   = 620f;  // widened: the first capture wrapped the
        private const float GhostPillH   = 56f;   // name+cost onto 2 lines and overflowed
        private const float GhostPillLiftPx = 96f;   // pill floats ABOVE the ghost anchor
        private const float SafePadPx       = 24f;   // never let the pill touch the screen edge

        // ── D14: THE LEAN HORIZONTAL VERB ROW (fixed — no per-frame layout) ────
        // Every dimension is FIXED PIXELS at the 1920x1080 reference (never a fraction
        // of screen — a wide canvas stretches fraction anchors into thin bars).
        //
        // COLUMN-FIT 2026-08-16 (2026-08-16): the band is a ROW, not a column. Band HEIGHT is now one
        // MinTouch hit box plus a pad top and bottom; band WIDTH carries the three 112px hit
        // boxes side by side. The three boxes sit INSIDE the band, never straddling its trim,
        // exactly as before — only the axis changed.
        private const float RailPadPx       = 10f;
        private const float RailGutterPx    = 14f;   // gutter BETWEEN the three verbs (now horizontal)
        private const float RailEdgeInsetPx = 24f;   // band's own inset from the RIGHT screen edge
        /// <summary>
        /// Clearance between the row's bottom and the top of the D19 resource strip. The row is
        /// anchored BOTTOM-RIGHT and is only <see cref="RailBandH"/> tall, so it tops out far
        /// below the palette lane's D21 quick-tab stack instead of eating the column the stack
        /// and Done have to share. See BuildIntentBar.
        /// </summary>
        private const float RailBottomGapPx = 16f;
        /// <summary>Band WIDTH: three MinTouch hit boxes + two gutters + a pad each end = 384.</summary>
        private const float RailBandW       = ChipHitPx * 3f + RailGutterPx * 2f + RailPadPx * 2f; // 384
        /// <summary>Band HEIGHT: one MinTouch hit box + a pad top and bottom = 132.</summary>
        private const float RailBandH       = ChipHitPx + RailPadPx * 2f;                          // 132

        /// <summary>
        /// Horizontal band (from the RIGHT screen edge, in 1920x1080 reference px) that the
        /// D14 verb row owns: 24 + 384 = 408. PUBLIC so a neighbouring lane can seat clear of
        /// it without reaching into this file — the D7 lesson was that two surfaces drawing in
        /// one band is the defect, not either surface on its own.
        ///
        /// ⚠ READ WITH <see cref="VerbRowTopPx"/>: this band is only claimed BELOW that line
        /// (y 114..246 from the canvas bottom) and only in the PLACING state. The carousel dock
        /// is wider than 2148-408 and DOES cross this x band — that is not a collision, because
        /// arming a card Collapses the dock (BuildModeController.Arm -> BuildPaletteUI.Collapse)
        /// before this row is ever shown. The permanent tenants of the right column (the D21
        /// quick tabs at x-inset 72 and Done above them) all sit ABOVE VerbRowTopPx.
        /// </summary>
        public const float RailReservedWidthPx = RailEdgeInsetPx + RailBandW;

        /// <summary>
        /// TOP of the D14 verb row in reference px from the canvas BOTTOM:
        /// ResourceStripReservedPx(98) + RailBottomGapPx(16) + RailBandH(132) = 246. PUBLIC for
        /// the same reason as <see cref="RailReservedWidthPx"/> — it is the line every other
        /// bottom-anchored surface has to clear.
        /// </summary>
        public const float VerbRowTopPx = ResourceStripReservedPx + RailBottomGapPx + RailBandH;

        // ── D10 -> WO-1035: Done JOINS THE RIGHT-EDGE QUICK-TAB COLUMN ─────────
        // The compact 76px gilt plate is RETIRED. Owner F8 seq 2503 (2026-08-16, verbatim):
        // "the done should match same style and stack above defense and town button", after
        // the chat note "The skip button is good. Can we style the close button the same
        // style as the other ones". So Done is now the SAME kit button as its neighbours
        // (ElarionUiKit.BuildObsidianButton Style1) seated as the TOP ITEM of the palette's
        // D15/D21 quick-tab column — one column, one box size, one rhythm.
        //
        // COLUMN-FIT 2026-08-16 (2026-08-16): the numbers below no longer MIRROR BuildPaletteUI's D21 band
        // math — they READ it. The old mirror (a private QuickTabStackTopPx = 935f const here)
        // was a hand-copied duplicate of a number the palette owns, and duplicated state is
        // exactly what goes stale. Done now seats off BuildPaletteUI.QuickTabStackTopPx
        // directly (same assembly, published for this), so re-seating the stack can never
        // leave this control behind again.
        private const float CornerInsetPx  = 24f;   // top-edge inset from the canvas top
        /// <summary>Box width — BuildPaletteUI.RestoreTabW. Same box as a quick tab.</summary>
        private const float DoneWidthPx = 260f;
        /// <summary>Box height. MinTouchPx, i.e. the kit floor: the column's scarce axis is
        /// vertical (see the arithmetic in BuildCornerDone). COLUMN-FIT 2026-08-16 took the quick tabs down
        /// to the SAME floor (BuildPaletteUI.QuickTabHeightPx) — they used to be 132px
        /// CanonCtaHeight — so the whole column is now one box size. ClampMinTouch never
        /// grows a control that is already at the floor.</summary>
        private const float DoneHeightPx = ElarionUiKit.MinTouchPx;   // 112
        /// <summary>Column inset — BuildPaletteUI.QuickTabEdgeInsetPx (box RIGHT edge, 72px
        /// in from the screen edge) so Done lands on the tabs' exact x span.</summary>
        private const float QuickTabColumnInsetPx = 72f;
        /// <summary>Clearance between the stack's top tab and Done's bottom edge — the same 9px
        /// the D21 math reserves under the stack.</summary>
        private const float DoneStackGapPx = 9f;

        // ── D19: the thin bottom-centre resource strip ─────────────────────────
        // BuildWalletRow authors kit CurrencyChips (HUD icon parity) at 188x72, 10px
        // spacing, row 24px in from the parent's left edge. Band sized to that content.
        // Display-only — MinTouch does not apply (WO-1010 D19).
        private const float StripChipW    = 188f;
        private const float StripChipH    = 72f;
        private const float StripChipGap  = 10f;
        private const float StripSideInset = 24f;   // BuildWalletRow's own left offset
        private const float StripPadPx     = 8f;
        private const float StripBandH     = StripChipH + StripPadPx * 2f;   // 80
        private const float StripBottomPx  = 18f;

        /// <summary>
        /// Vertical band (from the BOTTOM screen edge, in 1920x1080 reference px) that the
        /// D19 resource strip owns. PUBLIC for the same reason as
        /// <see cref="RailReservedWidthPx"/>: the carousel must rest ABOVE this, and in
        /// PLACE the single hint line sits above the strip, never on it.
        /// </summary>
        public const float ResourceStripReservedPx = StripBottomPx + StripBandH;

        // ── WO-1010 P3: the ONE thin first-run hint line (WO §1 "First-run hint") ──
        // PLACE phase only, seated ABOVE the D19 strip's reserved band. Display-only
        // (non-raycast — a hint must never eat a world tap), fixed pixels, own dark
        // backing per the playbook (it floats over live terrain and cannot borrow
        // contrast from the world). Gated: shows during a player's first 2 build
        // sessions and dismisses forever after 3 successful placements.
        private const float HintLineW = 860f;
        private const float HintLineH = 40f;
        private const float HintGapPx = 8f;     // breathing room above the strip band
        // WO-1411: the seed text is READ FROM THE GUIDE, not restated here. This used to be a
        // literal copy of BuildFirstUseGuide's MoveGhost sentence — two owners of one line, and
        // the copy would have gone stale the moment the guide's wording changed (the exact
        // duplicated-state failure CLAUDE.md §15 keeps catching). SetState/RefreshFirstUseGuide
        // already overwrite this from BuildFirstUseGuide.Copy on every show; this is only the
        // construction-time value, so it should come from the same place.
        private const string HintSessionsKey   = "build.hint.sessions";
        private const string HintPlacementsKey = "build.hint.placements";
        private const int HintSessionLimit   = 2;
        private const int HintPlacementLimit = 3;

        // Callbacks into the BRAIN (BuildModeController wires these).
        private Action _onRotateLeft;
        private Action _onRotateRight;
        private Action _onPlace;
        private Action _onCancel;
        private Action _onExit;

        private GameObject _canvas;
        private GameObject _intentBar;       // shown only in Placing (WO-1010 D14: pill + rail)
        private BuildWalletRow _wallet;
        private BuildHudState _state = BuildHudState.Browse;
        private TextMeshProUGUI _placeName;  // name + cost, floated above the ghost
        private GameObject _blockReasonPlate; // WO-1106: opaque, worded refusal independent of the ghost pill
        private TextMeshProUGUI _blockReasonText;

        // ── WO-1010 P1 state ───────────────────────────────────────────────────
        private RectTransform _canvasRect;
        private RectTransform _ghostPill;    // name + cost, above the ghost (the ONE thing that follows)
        private RectTransform _verbRail;     // D14: confirm / rotate / cancel — a FIXED HORIZONTAL
                                             // row in the bottom-right, above the D19 strip (COLUMN-FIT 2026-08-16)
        private Button _okChip;
        // WO-1411: the confirm verb is a WORD now, always — the D17 sprite/ASCII fork is gone,
        // so this is never null on a built rail and the verdict has exactly one thing to drive.
        private TextMeshProUGUI _okChipLabel;
        /// <summary>WO-1411 — the confirm verb's word while the placement is legal.</summary>
        private const string PlaceVerbWord = "PLACE";
        /// <summary>WO-1411 — and while it is refused. A WORD, never a hue or an alpha: the
        /// owner is red/green colourblind and a dimmed check-mark is exactly the judgement a
        /// capture (or a player) should never be asked to make. The full sentence stays on the
        /// block-reason plate; this is the state on the button itself.</summary>
        private const string BlockedVerbWord = "BLOCKED";
        private GameObject _dpadHost;        // the nudge stick — shown by STATE while Placing (D12), never a toggle
        private GameObject _hintLine;        // WO-1010 P3: the one first-run hint line (PLACE phase, gated)
        private TextMeshProUGUI _hintText;

        private bool _hasGhostAnchor;
        private Vector2 _ghostScreenPoint;
        private bool _ghostValid = true;
        private string _ghostBlockReason = string.Empty;
        /// <summary>Name + cost as authored, kept separate so the blocked reason can be
        /// appended and removed without the base line being lost to string surgery.</summary>
        private string _pillBase = "structure";

        /// <summary>
        /// Live direction from the build-owned nudge pad (zero when released or hidden).
        /// The BRAIN polls this and folds it into its existing pending-drop nudge, so the
        /// pad needs NO cross-assembly reach into the shared HUD's pad.
        /// </summary>
        public Vector2 NudgeVector { get; private set; }

        /// <summary>
        /// Create the HUD host (one per session). <paramref name="parent"/> is the
        /// controller transform so the canvas tears down with it.
        /// </summary>
        public static BuildHudController Create(Transform parent,
            Action onRotateLeft, Action onRotateRight, Action onPlace, Action onCancel, Action onExit)
        {
            var host = new GameObject("BuildHudController");
            if (parent != null) host.transform.SetParent(parent, false);
            var hud = host.AddComponent<BuildHudController>();
            hud._onRotateLeft = onRotateLeft;
            hud._onRotateRight = onRotateRight;
            hud._onPlace = onPlace;
            hud._onCancel = onCancel;
            hud._onExit = onExit;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            if (_canvas != null) return;

            // ── ONE landscape canvas (1920x1080, match 0.5) — NOT the kit's portrait
            //    BuildModalCanvas; this HUD is landscape-only (owner ruling). ──────
            _canvas = new GameObject("BuildHudCanvas");
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<GraphicRaycaster>();
            _canvasRect = _canvas.transform as RectTransform;

            BuildResourceStrip();   // D19 — thin bottom-centre band
            BuildCornerDone();      // D10/WO-1035 — kit Done atop the D21 quick-tab column
            BuildIntentBar();       // D14 — ghost pill + the fixed HORIZONTAL verb row (COLUMN-FIT 2026-08-16)
            BuildNudgePad();        // D12 — state-driven nudge stick (no toggle)
            // COLUMN-FIT 2026-08-16: state the WHOLE bottom-anchored column in one line, with the measured
            // canvas height beside it, so a capture answers "does it fit on THIS device?"
            // without anyone re-deriving the budget from three files.
            float builtCanvasH = ElarionUiKit.PostScaleCanvasHeight(_canvas.transform);
            float columnTop = BuildPaletteUI.QuickTabStackTopPx + DoneStackGapPx + DoneHeightPx;
            FlowTrace.Step("BuildHud",
                "chrome-built (WO-1010 D10/D14/D19 + WO-1035 + COLUMN-FIT 2026-08-16): canvas " +
                builtCanvasH.ToString("0.0") + " ref px tall; column from the bottom = strip 18.." +
                ResourceStripReservedPx + ", dock 98.." + BuildPaletteUI.DockTopPx +
                " (PICK), HORIZONTAL verb row " + (ResourceStripReservedPx + RailBottomGapPx) +
                ".." + VerbRowTopPx + " (PLACING), quick tabs " +
                BuildPaletteUI.QuickTabStackBottomPx + ".." + BuildPaletteUI.QuickTabStackTopPx +
                ", Done " + (columnTop - DoneHeightPx) + ".." + columnTop + ", + " +
                CornerInsetPx + "px inset = " + (columnTop + CornerInsetPx) + " needed vs " +
                builtCanvasH.ToString("0.0") + " available (headroom " +
                (builtCanvasH - columnTop - CornerInsetPx).ToString("0.0") +
                "px); the old full-width top bar is GONE");

            _canvas.SetActive(false);   // built hidden; Show shows it
        }

        /// <summary>
        /// D19 — the resource strip as ONE THIN bottom-centre band, not a top panel.
        /// The band is sized to BuildWalletRow's actual chip content so it hugs the numbers
        /// instead of being a slab with a row parked in it, and it carries its OWN gold edge:
        /// ObsidianFill is (0.02,0.02,0.025) — effectively black — so a band floating over
        /// live terrain cannot borrow contrast from whatever happens to be behind it.
        /// Display-only (no MinTouch floor), but it DOES eat taps so a mis-aimed read of the
        /// wallet never falls through and drags the ghost.
        /// </summary>
        private void BuildResourceStrip()
        {
            var edge = ElarionUiKit.AddImage(_canvas.transform, "BuildResourceStrip",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), ElarionUi.Gilt, rounded: true);
            var edgeRt = edge.transform as RectTransform;
            if (edgeRt != null)
            {
                edgeRt.anchorMin = edgeRt.anchorMax = new Vector2(0.5f, 0f);
                edgeRt.pivot = new Vector2(0.5f, 0f);
                edgeRt.anchoredPosition = new Vector2(0f, StripBottomPx);
                edgeRt.sizeDelta = new Vector2(StripSideInset * 2f + StripChipW, StripBandH);
            }
            var edgeImg = edge.GetComponent<Image>();
            if (edgeImg != null) edgeImg.raycastTarget = true;

            var fill = ElarionUiKit.AddImage(edge.transform, "StripFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUiKit.ObsidianFill, rounded: true);
            var fillRt = fill.transform as RectTransform;
            if (fillRt != null)
            {
                fillRt.offsetMin = new Vector2(ChipEdgePx, ChipEdgePx);
                fillRt.offsetMax = new Vector2(-ChipEdgePx, -ChipEdgePx);
            }
            var fillImg = fill.GetComponent<Image>();
            if (fillImg != null) fillImg.raycastTarget = false;

            var walletGo = new GameObject("BuildWalletRow");
            walletGo.transform.SetParent(fill.transform, false);
            _wallet = walletGo.AddComponent<BuildWalletRow>();
            _wallet.Build(fill.transform);

            // Size the band to the row it actually built. The pool list is DATA (the wallet
            // DTO), so counting the chips beats hard-coding "five" — a sixth pool would
            // otherwise silently overflow the frame it is supposed to sit inside.
            int chips = 0;
            var row = fill.transform.Find("WalletChips");
            if (row != null)
            {
                for (int i = 0; i < row.childCount; i++)
                    if (row.GetChild(i).name.StartsWith("Chip_", StringComparison.Ordinal)) chips++;
            }
            if (chips > 0 && edgeRt != null)
            {
                float content = chips * StripChipW + (chips - 1) * StripChipGap;
                edgeRt.sizeDelta = new Vector2(StripSideInset * 2f + content, StripBandH);
            }
            FlowTrace.Step("BuildHud",
                "D19 resource strip: thin bottom-centre band, " + chips + " pool(s), h=" +
                StripBandH + "px fixed -- reserves " + ResourceStripReservedPx +
                "px of the bottom band (carousel + hint seat ABOVE it)");
        }

        /// <summary>
        /// WO-1035 — Done as the TOP ITEM OF THE RIGHT-EDGE QUICK-TAB COLUMN, built with the
        /// COMMON kit button. Owner F8 seq 2503 (2026-08-16): "the done should match same
        /// style and stack above defense and town button". Nothing here is hand-rolled: the
        /// chrome is ElarionUiKit.BuildObsidianButton(Style1) — the same call the approved
        /// TutorialSkipUi makes and the same family BuildPaletteUI.AddQuickTab uses — so the
        /// frame, the 3-state feedback and the MinTouchPx floor all come from the kit. The
        /// old hand-rolled D10 plate (a 76px gilt disc inside an invisible 112px hit pad)
        /// is GONE; it was the reason this control read differently from every other button
        /// on the screen, and it is exactly what the [ui-obsidian] ratchet forbids.
        ///
        /// COLOUR = Yellow, matching the Defense/Town tabs it now stacks with (the owner's
        /// "match same style" referent is those buttons). Meaning never rests on the face:
        /// the label reads "Done" and the tabs carry the gilt active UNDERLINE that Done
        /// never has.
        ///
        /// SEAT (fixed reference px, read off BuildPaletteUI's published D21 consts):
        ///   x  anchor (1,0), pivot centre, anchoredPosition.x = -(72 + 260/2) = -202
        ///      -> box spans 72..332 in from the right edge: the SAME column as the quick
        ///      tabs, to the pixel, at every canvas width.
        ///   y  BuildPaletteUI.QuickTabStackTopPx(778) + DoneStackGapPx(9) +
        ///      DoneHeightPx/2(56) = 843 -> box spans y 787..899 from the canvas BOTTOM.
        ///   Bottom-anchored like the tabs (NOT top-anchored): the whole column must share
        ///   one origin or the stack shears apart the moment the canvas is not 1080 tall.
        ///
        /// COLUMN-FIT 2026-08-16 — THE COLUMN NOW FITS, AND HERE IS THE ARITHMETIC (all from the bottom):
        ///   strip           18..98    (ResourceStripReservedPx = 98)
        ///   carousel dock   98..401   (BuildPaletteUI.DockTopPx — PICK phase)
        ///   D14 verb row   114..246   (VerbRowTopPx — PLACING phase; horizontal since COLUMN-FIT 2026-08-16)
        ///   quick tabs     410..778   (3 x 112 MinTouch boxes + 2 x 16 gutters, 9px over the dock)
        ///   Done           787..899
        ///   + CornerInsetPx 24        -> 923 required.
        /// The Seeker's 2670x1200 surface resolves to a 965.4 ref-px-tall canvas, so there is
        /// 42.4px of headroom and the clamp below DOES NOT FIRE on the normal path any more.
        /// (Before COLUMN-FIT 2026-08-16 the same sum was 1080 exactly — a 114.6px overflow at that aspect,
        /// which is why Done used to land on the top quick-tab.) The clamp stays as a net for
        /// yet-shorter surfaces; an unreachable exit is the one unacceptable failure.
        ///
        /// D14 ROW CLEARANCE: the verb row is bottom-anchored and tops out at 246. Done's
        /// bottom is 787 — 541px above it, with the whole quick-tab stack in between — so the
        /// D7-class collision the old vertical rail caused cannot recur here.
        /// </summary>
        private void BuildCornerDone()
        {
            // FIXED-PIXEL MOUNT BAND, then the kit button stretched into it — the
            // TutorialSkipUi / HudKitController.BuildRailChip idiom. The mount (not the
            // button) carries the seat because in the kit's PREFAB mode the returned Button
            // can be a DESCENDANT of the instantiated prefab root, so stamping the button's
            // own rect would move a child and leave the frame behind. Fraction anchors on the
            // mount would resolve to a different band at every aspect — the drift the D21
            // band math exists to forbid — so only the mount's POSITION/SIZE are fixed px.
            float wantY = BuildPaletteUI.QuickTabStackTopPx + DoneStackGapPx + DoneHeightPx * 0.5f;   // 843
            float canvasH = ElarionUiKit.PostScaleCanvasHeight(_canvas.transform);
            float maxY = canvasH - CornerInsetPx - DoneHeightPx * 0.5f;
            float seatY = Mathf.Min(wantY, maxY);

            var mountGo = new GameObject("BuildDoneSeat", typeof(RectTransform));
            mountGo.transform.SetParent(_canvas.transform, false);
            var mount = (RectTransform)mountGo.transform;
            mount.anchorMin = mount.anchorMax = new Vector2(1f, 0f);
            mount.pivot = new Vector2(0.5f, 0.5f);
            mount.sizeDelta = new Vector2(DoneWidthPx, DoneHeightPx);
            mount.anchoredPosition =
                new Vector2(-(QuickTabColumnInsetPx + DoneWidthPx * 0.5f), seatY);

            var done = ElarionUiKit.BuildObsidianButton(mount, "Done",
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                Vector2.zero, Vector2.one, () => _onExit?.Invoke());
            if (done == null)
            {
                FlowTrace.Fail("BuildHud",
                    "Done BUILD FAILED - ElarionUiKit.BuildObsidianButton returned no button; " +
                    "build mode has no exit control this session");
                return;
            }
            // Canonical close name kept (probe/close convention; the label reads "Done").
            done.gameObject.name = "CloseButton";

            if (seatY < wantY - 0.5f)
                FlowTrace.Warn("BuildHud",
                    "Done seat CLAMPED: canvas is only " + canvasH.ToString("0") +
                    " ref px tall, so the wanted y " + wantY.ToString("0") +
                    " would be off-screen; seated at " + seatY.ToString("0") +
                    " (bottom " + (seatY - DoneHeightPx * 0.5f).ToString("0") +
                    ") which OVERLAPS the D21 quick-tab stack top (" +
                    BuildPaletteUI.QuickTabStackTopPx + "). AFTER COLUMN-FIT 2026-08-16 THIS SHOULD NOT FIRE " +
                    "AT 2670x1200 (965.4 ref px): the column needs 923 (strip 98 + dock 303 + 9 " +
                    "+ tabs 368 + 9 + Done 112 + inset 24). If it fires there, the budget moved " +
                    "again -- re-read the arithmetic in this method's doc, do not re-tune blind.");

            FlowTrace.Step("BuildHud",
                "Done exit (WO-1035/COLUMN-FIT 2026-08-16): COMMON kit button ElarionUiKit.BuildObsidianButton(" +
                "Style1,Yellow) seated as the TOP ITEM of the D15/D21 right-edge quick-tab column -- box " +
                DoneWidthPx + "x" + DoneHeightPx + "px on the tabs' own x (" +
                QuickTabColumnInsetPx + "px inset), y centre " + seatY.ToString("0") +
                " (wanted " + wantY.ToString("0") + " = stack top " +
                BuildPaletteUI.QuickTabStackTopPx + " + " + DoneStackGapPx + "px gutter -> band " +
                (wantY - DoneHeightPx * 0.5f).ToString("0") + ".." +
                (wantY + DoneHeightPx * 0.5f).ToString("0") + "); canvas " + canvasH.ToString("0") +
                " ref px tall, top inset line " + (canvasH - CornerInsetPx).ToString("0") +
                ", clamp " + (seatY < wantY - 0.5f ? "FIRED" : "NOT needed"));
        }

        // =====================================================================
        //  WO-1010 P1 — the ghost carries its own controls
        // =====================================================================
        /// <summary>
        /// Builds the ghost's name+cost pill and the D14 verb row (horizontal since COLUMN-FIT 2026-08-16).
        ///
        /// THE SPLIT IS THE RULING: the PILL follows the ghost's projected screen point
        /// (pushed by the brain via TrackGhost) — screen-space, never a world-space billboard,
        /// which would shrink with zoom and fall under the MinTouch floor exactly when the
        /// player is placing a small wall piece. The VERB ROW does not follow anything. It is a
        /// slim FIXED band in the bottom-right corner, so the piece being placed is never
        /// covered by its own controls and there is no per-frame layout to get wrong.
        /// </summary>
        private void BuildIntentBar()
        {
            _intentBar = new GameObject("BuildGhostControls", typeof(RectTransform));
            _intentBar.transform.SetParent(_canvas.transform, false);
            var irt = (RectTransform)_intentBar.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;

            // ── Name + cost pill, floating above the ghost. One line, one place — this
            //    absorbs the old "Placing: <name>" hint pill AND the cost readout the
            //    player previously had to find back on the card. Non-raycast so it can
            //    never eat a world tap meant for the ground.
            // Gold edge around a near-black fill, for the same reason as the chips: the pill
            // floats over live terrain and cannot rely on the ground behind it for contrast.
            // WO-944 (owner F8 seq 2250, flagged live in the 22:11 build, verbatim: "can we make
            // the title of the item pin staticl maybe at the top of the screen"): the pill no
            // longer follows the ghost. It PINS top-centre, fixed pixels — the LAST follower on
            // this screen retires, which is UI_PLAYBOOK §8's own preferred answer ("if a control
            // does not have to follow, do not make it follow"). Clear of the corner Done's hit
            // pad by construction (620px centred vs the 112px pad in the far corner).
            var pillEdge = ElarionUiKit.AddImage(_intentBar.transform, "GhostPill",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), ElarionUi.Gilt, rounded: true);
            _ghostPill = pillEdge.transform as RectTransform;
            if (_ghostPill != null)
            {
                _ghostPill.anchorMin = _ghostPill.anchorMax = new Vector2(0.5f, 1f);
                _ghostPill.pivot = new Vector2(0.5f, 1f);
                _ghostPill.anchoredPosition = new Vector2(0f, -CornerInsetPx);
                _ghostPill.sizeDelta = new Vector2(GhostPillW, GhostPillH);
            }
            var pillEdgeImg = pillEdge.GetComponent<Image>();
            if (pillEdgeImg != null) pillEdgeImg.raycastTarget = false;

            var pillFill = ElarionUiKit.AddImage(pillEdge.transform, "GhostPillFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUiKit.ObsidianFill, rounded: true);
            var pillFillRt = pillFill.transform as RectTransform;
            if (pillFillRt != null)
            {
                pillFillRt.offsetMin = new Vector2(ChipEdgePx, ChipEdgePx);
                pillFillRt.offsetMax = new Vector2(-ChipEdgePx, -ChipEdgePx);
            }
            var pillFillImg = pillFill.GetComponent<Image>();
            if (pillFillImg != null) pillFillImg.raycastTarget = false;

            _placeName = MakeText(pillFill.transform, "structure", 22, ElarionUi.Gilt,
                FontStyles.Bold, TextAlignmentOptions.Center,
                new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.98f));
            _placeName.raycastTarget = false;
            // ONE LINE. The first capture wrapped a long name-plus-cost pill onto two lines
            // and pushed it out of its own background. A long name shrinks to fit rather than
            // escaping the plate.
            // CORRECTION 2026-09-06 (WO-1478): this comment used to QUOTE the wrapping string
            // verbatim. That string was a HARNESS STUB hardcoded in Assets/Editor/
            // UICaptureLaunch.cs -- a wood+iron+crystals basket the catalog has never held (the
            // authored Arcane Spire row is iron 360) and a shape WO-947 forbids. Quoting it here
            // gave a fabricated basket a second home in runtime code, so the numbers are gone and
            // only the geometry lesson -- which was always the real point -- remains.
            _placeName.textWrappingMode = TextWrappingModes.NoWrap;
            _placeName.enableAutoSizing = true;
            _placeName.fontSizeMin = 14f;
            _placeName.fontSizeMax = 22f;

            // WO-1106: a refusal is a sentence, not a suffix on the one-line name/cost pill.
            // Pin it to the HUD's top band, below the pill, where world footprint geometry can
            // never pass through it. Both layers are deliberately non-raycast so placement
            // input and footprint/validity logic remain exactly as authored.
            var reasonEdge = ElarionUiKit.AddImage(_intentBar.transform, "GhostBlockReason",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), ElarionUi.Gilt, rounded: true);
            _blockReasonPlate = reasonEdge;
            var reasonRt = reasonEdge.transform as RectTransform;
            if (reasonRt != null)
            {
                reasonRt.anchorMin = reasonRt.anchorMax = new Vector2(0.5f, 1f);
                reasonRt.pivot = new Vector2(0.5f, 1f);
                reasonRt.anchoredPosition = new Vector2(0f, -CornerInsetPx - GhostPillH - 8f);
                reasonRt.sizeDelta = new Vector2(760f, 84f);
            }
            var reasonEdgeImage = reasonEdge.GetComponent<Image>();
            if (reasonEdgeImage != null) reasonEdgeImage.raycastTarget = false;

            var reasonFill = ElarionUiKit.AddImage(reasonEdge.transform, "ReasonOpaqueFill",
                Vector2.zero, Vector2.one, ElarionUiKit.ObsidianFill, rounded: true);
            var reasonFillRt = reasonFill.transform as RectTransform;
            if (reasonFillRt != null)
            {
                reasonFillRt.offsetMin = new Vector2(ChipEdgePx, ChipEdgePx);
                reasonFillRt.offsetMax = new Vector2(-ChipEdgePx, -ChipEdgePx);
            }
            var reasonFillImage = reasonFill.GetComponent<Image>();
            if (reasonFillImage != null)
            {
                reasonFillImage.color = new Color(ElarionUiKit.ObsidianFill.r,
                    ElarionUiKit.ObsidianFill.g, ElarionUiKit.ObsidianFill.b, 1f);
                reasonFillImage.raycastTarget = false;
            }

            _blockReasonText = MakeText(reasonFill.transform, string.Empty, 22f,
                ElarionUi.Parchment, FontStyles.Bold, TextAlignmentOptions.Center,
                new Vector2(0.035f, 0.08f), new Vector2(0.965f, 0.92f));
            _blockReasonText.raycastTarget = false;
            _blockReasonText.textWrappingMode = TextWrappingModes.Normal;
            _blockReasonText.enableAutoSizing = false;
            _blockReasonPlate.SetActive(false);

            // ── D14: THE LEAN HORIZONTAL VERB ROW (COLUMN-FIT 2026-08-16) ───────────────────────
            // A slim obsidian band with its own gold trim, seated in the bottom-RIGHT corner
            // just above the D19 resource strip. The band is a raycast target so a tap that
            // lands in a gutter is eaten by the chrome instead of falling through and dragging
            // the ghost out from under the player's other hand.
            //
            // ANCHORED BOTTOM-RIGHT — capture-proven, and now LAID OUT ACROSS instead of UP.
            // History, because both halves of it are load-bearing:
            //  1) The first build centred this band vertically (y 348..732 from the top at the
            //     1080 reference). The 2026-08-09 capture pair showed the palette lane's D15
            //     quick-tabs in the TOP-right of the very same column (x 1590..1845,
            //     y 170..490) — "Defenses (3)" landed directly on the OK confirm verb. Two
            //     surfaces drawing in one band is the D7 defect class, and the most important
            //     control on the screen was the one underneath it.
            //  2) Re-seating it bottom-right fixed THAT, but left a 384px-tall tenant in a
            //     column that only has ~965 REFERENCE px on the owner's 2670x1200 device (not
            //     the 1080 every one of these numbers was authored against). The column then
            //     summed to exactly 1080 and Done overlapped the top quick-tab.
            // COLUMN-FIT 2026-08-16 turns the band 90 degrees: 384 wide x 132 tall, y 114..246, x 24..408 in
            // from the right edge. Three MinTouch boxes across 2148 ref px of device width is
            // trivial; 384px of the vertical was the whole deficit. The verbs stay in genuine
            // right-thumb reach in landscape — which is what D14 asked for — and the wireframe's
            // "verbs near the piece, chrome off the piece" reading is unchanged.
            //
            // The right edge is now split by ownership:
            //   TOP-right    -> Done (D10/WO-1035, y 787..899 — the quick-tab column's top item)
            //   MIDDLE-right -> the palette's permanent quick-tab stack (D15/D21, y 410..778)
            //   BOTTOM-right -> THIS ROW, above the D19 strip (y 114..246, i.e. VerbRowTopPx)
            // The carousel dock (PICK, y 98..401) crosses this row's x band but never its
            // TIME: arming Collapses the dock before Placing shows the row.
            var railEdge = ElarionUiKit.AddImage(_intentBar.transform, "GhostVerbRail",
                new Vector2(1f, 0f), new Vector2(1f, 0f), ElarionUi.Gilt, rounded: true);
            _verbRail = railEdge.transform as RectTransform;
            if (_verbRail != null)
            {
                _verbRail.anchorMin = _verbRail.anchorMax = new Vector2(1f, 0f);
                _verbRail.pivot = new Vector2(1f, 0f);
                _verbRail.anchoredPosition = new Vector2(-RailEdgeInsetPx,
                                                         ResourceStripReservedPx + RailBottomGapPx);
                _verbRail.sizeDelta = new Vector2(RailBandW, RailBandH);
            }
            var railEdgeImg = railEdge.GetComponent<Image>();
            if (railEdgeImg != null) railEdgeImg.raycastTarget = true;

            var railFill = ElarionUiKit.AddImage(railEdge.transform, "RailFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUiKit.ObsidianFill, rounded: true);
            var railFillRt = railFill.transform as RectTransform;
            if (railFillRt != null)
            {
                railFillRt.offsetMin = new Vector2(ChipEdgePx, ChipEdgePx);
                railFillRt.offsetMax = new Vector2(-ChipEdgePx, -ChipEdgePx);
            }
            var railFillImg = railFill.GetComponent<Image>();
            if (railFillImg != null) railFillImg.raycastTarget = false;

            // ── WO-1411: THE RAIL SPEAKS WORDS. PLACE / ROTATE / CANCEL. ─────────────────
            // ⛔ THE D17 ICON LANGUAGE IS RETIRED HERE, and the sprites are not "missing" —
            // element/check, element/rotate and element/cross all load. They were the problem.
            // The merged UI review (REVIEW_MERGED.md row 10, both reviewers independently, off
            // BuildGhostChips_blocked_2670x1200.png) read the bottom-right of the placement
            // screen as "three unlabelled glyphs (check / rotate / X)" — i.e. three symbols to
            // guess at, on the one screen where the player is about to SPEND. That is the
            // owner's names-not-icons ruling, and the previous design conceded the point in its
            // own comment: the ASCII path existed because "words degrade gracefully".
            //
            // So the words are now the PRIMARY path, not the fallback. Kit ButtonPack buttons
            // (the same widget the confirm modal's CONFIRM/CANCEL use) in the SAME rail band —
            // RailBandW x RailBandH is load-bearing geometry (the COLUMN-FIT note above spells
            // out what it clears), so nothing about the band, its seat or the reading order
            // moves. Only the three tenants inside it change from 52px discs to worded boxes.
            //
            // FIT, in the band's own numbers: the inner fill is RailBandW - 2*ChipEdgePx = 378
            // wide by RailBandH - 2*ChipEdgePx = 126 tall. Three columns at x .015-.325 /
            // .345-.655 / .675-.985 are ~117px wide each with ~7px gutters, and y .02-.98 is
            // ~123px tall — above the kit's MinTouchPx floor on the tap axis that matters here.
            // ⚠ The LABEL fit inside an ornate cap cannot be proven from arithmetic (today's
            // Manage lesson: "a size derived from a number I did not measure is a guess wearing
            // a px suffix"), so every label below is autosizing + NoWrap + Overflow and the
            // capture is the judge.
            //
            // Reading order is UNCHANGED — confirm, rotate, cancel, left to right — so the
            // destructive verb still sits furthest from the confirm a slipped thumb finds.
            var railRt = railFill.transform as RectTransform;
            _okChip = MakeWordVerb(railRt, "OkChip", PlaceVerbWord, ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.015f, 0.02f), new Vector2(0.325f, 0.98f),
                () =>
                {
                    // WO-1615 §3.2 — THE DISCRIMINATING TRACE, and it is PERMANENT (CLAUDE.md §12:
                    // instrumentation is never stripped). Before this line the button path produced
                    // NO trace at all, so a log could not tell "a higher uGUI surface ate the click
                    // and OkChip never fired" from "OkChip fired and the callback was null / bound
                    // to a dead controller instance". Emitted under the "Build" tag (not this file's
                    // usual "BuildHud") deliberately: it must sit in the SAME grep as
                    // "PlaceConfirm: UI PLACE button latch consumed", which is the very next line the
                    // reader expects to see. bound=False => EnsureHud's binding never ran for the
                    // instance that drew this HUD; a bound target whose id is NOT the live
                    // BuildModeController means a stale delegate.
                    var placeTarget = _onPlace?.Target as UnityEngine.Object;
                    FlowTrace.Step("Build",
                        "OkChip TAPPED (onPlace bound=" + (_onPlace != null) +
                        ", target='" + (placeTarget != null ? placeTarget.name : "<none>") +
                        "' id=" + (placeTarget != null ? placeTarget.GetInstanceID() : 0) +
                        "). The bound callback raises BuildModeController.RequestUiPlaceConfirm, " +
                        "which sets the ONE commit latch (ConfirmKind.UiPlace). If this line prints " +
                        "and 'PlaceConfirm: UI PLACE button latch consumed' does not follow, the " +
                        "binding is stale; if this line never prints, the click never reached OkChip.");
                    _onPlace?.Invoke();
                }, out _okChipLabel);
            MakeWordVerb(railRt, "RotChip", "ROTATE", ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.345f, 0.02f), new Vector2(0.655f, 0.98f),
                () => _onRotateRight?.Invoke(), out _);
            MakeWordVerb(railRt, "CancelChip", "CANCEL", ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.675f, 0.02f), new Vector2(0.985f, 0.98f),
                () => _onCancel?.Invoke(), out _);
            FlowTrace.Step("BuildHud",
                "WO-1411 ghost rail: the three verbs are WORDS -- '" + PlaceVerbWord + "' / 'ROTATE' / 'CANCEL' " +
                "(kit ButtonPack), replacing the D17 check/rotate/cross glyphs the merged review read as " +
                "unlabelled. Band geometry unchanged: " + RailBandW + "x" + RailBandH + "px, same seat, same " +
                "left-to-right order. Blocked verdict now flips the confirm WORD to '" + BlockedVerbWord +
                "' instead of dimming a glyph.");

            // Kept as the canonical cancel name so any probe/close convention still resolves
            // it after the word-button retirement.
            var cancelChip = railRt != null ? railRt.Find("CancelChip") : null;
            if (cancelChip != null) cancelChip.gameObject.name = "BuildHudPlaceCancel";

            BuildFirstRunHint(_intentBar.transform);   // WO-1010 P3 — rides the Placing state

            _intentBar.SetActive(false);   // Placing state shows it
            FlowTrace.Step("BuildHud",
                "COLUMN-FIT 2026-08-16 D14: the verb rail is HORIZONTAL -> a FIXED lean row [OK][Rot][X] (" +
                RailBandW + "x" + RailBandH + "px, bottom-right, " + RailEdgeInsetPx +
                "px edge inset) seated " + (ResourceStripReservedPx + RailBottomGapPx) +
                "px up so it clears the D19 strip's reserved " + ResourceStripReservedPx +
                "px band; row owns y " + (ResourceStripReservedPx + RailBottomGapPx) + ".." +
                VerbRowTopPx + " and x " + RailEdgeInsetPx + ".." + RailReservedWidthPx +
                " in from the right edge. It reserves NO part of the column the D21 quick tabs (" +
                BuildPaletteUI.QuickTabStackBottomPx + ".." + BuildPaletteUI.QuickTabStackTopPx +
                ") and Done share; the ghost still carries ONLY its name/cost pill");
        }

        /// <summary>
        /// WO-1010 P3 — the ONE thin first-run hint line (WO §1). Parented under the
        /// intent bar so it exists only in the PLACE phase, seated at fixed pixels ABOVE
        /// the D19 strip's reserved band (never on it — the reserved-band rule) and clear of
        /// the COLUMN-FIT 2026-08-16 horizontal verb row (see the seat note in the body), with its
        /// own gold edge around a near-black backing because it floats over live terrain.
        /// Entirely non-raycast: a hint that eats a world tap would drag the ghost.
        /// Built hidden; <see cref="SetState"/> shows it while the first-run gate holds
        /// (first <see cref="HintSessionLimit"/> sessions, dismissed forever after
        /// <see cref="HintPlacementLimit"/> successful placements).
        /// </summary>
        private void BuildFirstRunHint(Transform parent)
        {
            // ── COLUMN-FIT 2026-08-16 SEAT: BESIDE the verb row, or ABOVE it on a narrow canvas ──
            // The hint is centred and 860px wide; the D14 verb row is now a 384px band hugging
            // the RIGHT edge in the y span 114..246, and both are PLACING-state siblings, so
            // they are on screen at the same time. On any landscape canvas they miss each other
            // by a wide margin (at 2148 ref px the hint spans 644..1504, the row 1740..2124),
            // but on a canvas narrower than 2*(430 + 408 + 8) = 1692 ref px they would overlap.
            // Rather than let a hint print through the confirm verb, the line LIFTS to sit just
            // above the row — still clear of the quick-tab stack (410) by a wide margin.
            float canvasH = ElarionUiKit.PostScaleCanvasHeight(parent);
            float canvasW = canvasH * ElarionUiKit.SurfaceWidth /
                            Mathf.Max(1f, ElarionUiKit.SurfaceHeight);
            bool besideRow = canvasW * 0.5f >= HintLineW * 0.5f + RailReservedWidthPx + HintGapPx;
            float hintY = besideRow
                ? ResourceStripReservedPx + HintGapPx   // 106 — the published low seat
                : VerbRowTopPx + HintGapPx;             // 254 — lifted over the row
            FlowTrace.Step("BuildHud",
                "P3 hint seat: canvas " + canvasW.ToString("0") + "x" + canvasH.ToString("0") +
                " ref px -> " + (besideRow ? "BESIDE" : "ABOVE") + " the D14 verb row (row band y " +
                (ResourceStripReservedPx + RailBottomGapPx) + ".." + VerbRowTopPx + ", x " +
                RailEdgeInsetPx + ".." + RailReservedWidthPx + " from the right edge); hint y " +
                hintY + ".." + (hintY + HintLineH));

            var edge = ElarionUiKit.AddImage(parent, "BuildFirstRunHint",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), ElarionUi.Gilt, rounded: true);
            _hintLine = edge;
            var ert = edge.transform as RectTransform;
            if (ert != null)
            {
                ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0f);
                ert.pivot = new Vector2(0.5f, 0f);
                ert.anchoredPosition = new Vector2(0f, hintY);
                ert.sizeDelta = new Vector2(HintLineW, HintLineH);
            }
            var edgeImg = edge.GetComponent<Image>();
            if (edgeImg != null) edgeImg.raycastTarget = false;

            var fill = ElarionUiKit.AddImage(edge.transform, "HintFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUiKit.ObsidianFill, rounded: true);
            var frt = fill.transform as RectTransform;
            if (frt != null)
            {
                frt.offsetMin = new Vector2(ChipEdgePx, ChipEdgePx);
                frt.offsetMax = new Vector2(-ChipEdgePx, -ChipEdgePx);
            }
            var fillImg = fill.GetComponent<Image>();
            if (fillImg != null) fillImg.raycastTarget = false;

            var t = MakeText(fill.transform, BuildFirstUseGuide.Copy, 18, ElarionUi.Parchment,
                FontStyles.Normal, TextAlignmentOptions.Center,
                new Vector2(0.02f, 0f), new Vector2(0.98f, 1f));
            _hintText = t;
            t.raycastTarget = false;
            // One line, always: the hint shrinks to fit rather than escaping its backing.
            t.textWrappingMode = TextWrappingModes.Normal;
            t.enableAutoSizing = true;
            t.fontSizeMin = 12f;
            t.fontSizeMax = 18f;

            _hintLine.SetActive(false);
            FlowTrace.Step("BuildHud",
                "P3 first-run hint built: seated " + hintY +
                "px up (clears the D19 reserved " + ResourceStripReservedPx + "px band), " +
                HintLineW + "x" + HintLineH + "px fixed, non-raycast; gate = first " +
                HintSessionLimit + " sessions / dismiss after " + HintPlacementLimit + " placements");
        }

        /// <summary>
        /// WO-1411 — ONE RAIL VERB, AS A WORD. Replaces the D14/D17 <c>MakeVerb</c> chip (a
        /// MinTouch-sized invisible hit box wrapping a 52px disc that carried either a pack
        /// sprite or a two-letter ASCII stub) and its <c>MakeDisc</c> helper, both of which
        /// existed only for these three tenants and went with them.
        ///
        /// ⛔ THIS IS A REPLACEMENT, NOT A SECOND WIDGET. The kit's ButtonPack is the same
        /// button the confirm modal's CONFIRM/CANCEL and the corner Done already use, so the
        /// placement screen now speaks the game's one button language instead of a bespoke
        /// disc grammar invented for this rail.
        ///
        /// The GameObject NAME is preserved verbatim for each seat (OkChip / RotChip /
        /// CancelChip) because the capture harness walks the live hierarchy by those names
        /// (UICaptureLaunch.AssertConfirmChipInvalid finds "OkChip"). A rename here would turn
        /// a real measurement into a "verb was renamed or removed" warning and quietly stop
        /// measuring the blocked verdict.
        /// </summary>
        private static Button MakeWordVerb(RectTransform parent, string name, string label,
            ElarionUiKit.ButtonKind kind, Vector2 anchorMin, Vector2 anchorMax, Action onClick,
            out TextMeshProUGUI labelOut)
        {
            labelOut = null;
            var btn = ElarionUiKit.ButtonPack(parent, label, kind, anchorMin, anchorMax, onClick,
                                              RpgUiCatalog.ButtonFrame);
            if (btn == null)
            {
                FlowTrace.Fail("BuildHud",
                    "WO-1411: kit ButtonPack returned null for the ghost verb '" + label +
                    "' -- that seat of the placement rail has NO control at all.");
                return null;
            }
            btn.gameObject.name = name;
            labelOut = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (labelOut != null)
            {
                // A one-word verb must never ellipsis-cull inside an ornate cap. The usable
                // width inside a pack frame is not a knowable fraction of the box (the Manage
                // pass measured that the hard way), so the label shrinks to fit rather than
                // being authored at a size derived from an unmeasured number.
                labelOut.raycastTarget = false;
                labelOut.textWrappingMode = TextWrappingModes.NoWrap;
                labelOut.enableAutoSizing = true;
                labelOut.fontSizeMin = 12f;
                labelOut.fontSizeMax = 22f;
                labelOut.overflowMode = TextOverflowModes.Overflow;
            }
            else
            {
                FlowTrace.Warn("BuildHud",
                    "WO-1411: ghost verb '" + label + "' built without a readable TMP label -- " +
                    "the button would render as a bare plate, which is the icon-only defect again.");
            }
            return btn;
        }

        /// <summary>
        /// The build-owned nudge stick (D12). No toggle: the stick is state-driven —
        /// <see cref="SetState"/> / <see cref="SetNudgePadAllowed"/> show it exactly while
        /// a piece is being positioned. It stays in the HUD because pixel-precise nudging
        /// is what makes a long wall run placeable at all. Built on the Core kit's own
        /// widget seam, so no new reflection bridge into the HUD assembly is introduced.
        /// </summary>
        private void BuildNudgePad()
        {
            // ── NO TOGGLE. The pad follows the STATE. (owner ruling 2026-08-09) ────
            // The first pass gave the nudge pad a corner "+" button. That was the wrong trade:
            // it removed a permanent control by adding a permanent control, and it made
            // pixel-nudging a thing the player has to DISCOVER on a screen already accused of
            // having buttons everywhere. The pad is only ever meaningful while something is
            // being positioned, and that is a state the HUD already knows — so it appears on
            // Placing and leaves with it. The player does nothing.
            var padHost = new GameObject("BuildNudgePad", typeof(RectTransform));
            padHost.transform.SetParent(_canvas.transform, false);
            var prt = (RectTransform)padHost.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            // ── SAME MOVE WIDGET AS THE REGULAR HUD (owner, 2026-08-09). ──────────
            // The first pass built this with BuildVirtualDPad — which is the OLDER WO-611
            // widget. The live HUD builds an ANALOG STICK (HudKitController, FeatureFlags.
            // CombatHud611 defaults ON) and keeps the virtual d-pad only as a
            // construction-failure fallback. The result was two different move controls in one
            // game: a stick while walking, a boxy d-pad the moment you entered build mode.
            // Mirroring the HUD's exact construction — stick first, d-pad only if the stick
            // fails to build — so the movement grammar is the same everywhere and a future
            // change to the HUD's widget does not silently leave build mode behind.
            var stick = ElarionUiKit.BuildAnalogStick(padHost.transform, new Vector2(0.12f, 0.26f),
                v => NudgeVector = v);
            if (stick == null || stick.root == null)
            {
                FlowTrace.Warn("BuildHud",
                    "analog stick unavailable — falling back to the WO-611 virtual D-pad for the build nudge.");
                ElarionUiKit.BuildVirtualDPad(padHost.transform, new Vector2(0.12f, 0.26f),
                    v => NudgeVector = v);
            }
            _dpadHost = padHost;
            _dpadHost.SetActive(false);   // Browse has nothing to nudge
        }

        /// <summary>
        /// WO-1010 D12 — the BRAIN's per-frame verdict on whether the nudge stick may show:
        /// an item is selected AND the carousel is minimized. Both conditions are automatic;
        /// there is no toggle and the player does nothing. Placement ending (placed OR cancelled)
        /// flips this false, which is what "after placed removes" means in practice.
        /// </summary>
        public void SetNudgePadAllowed(bool allowed)
        {
            SetNudgePadVisible(allowed && _state == BuildHudState.Placing);
        }

        private void SetNudgePadVisible(bool show)
        {
            if (_dpadHost == null || _dpadHost.activeSelf == show) return;
            _dpadHost.SetActive(show);
            if (!show) NudgeVector = Vector2.zero;   // a hidden pad must never keep steering
            FlowTrace.Step("BuildHud", "nudge pad " + (show ? "SHOWN (placing)" : "HIDDEN (not placing)") +
                " — follows state, no player action");
        }

        // NOTE: AllowTwoLineLabel (the "Rotate Right" -> "Rotate Ri..." ellipsis fix) was
        // deleted with WO-1010 D14/D10. The rail verbs are hand-built plates whose short
        // ASCII labels autosize inside their own plate; Done is a kit button again
        // (WO-1035) but its label is the single word "Done" in a 260x112 box, so the kit's
        // single-line fit has nothing to shorten and there is still nothing to opt out of.

        // ── WO-1010 P3: the first-run hint's counters ──────────────────────────

        /// <summary>Eligible while BOTH gates hold: within the first
        /// <see cref="HintSessionLimit"/> build sessions AND under
        /// <see cref="HintPlacementLimit"/> successful placements.</summary>
        private static bool HintEligible()
        {
            return !BuildFirstUseGuide.IsComplete;
        }

        /// <summary>Refresh the first-use instruction after a real Build action advances it.</summary>
        public void RefreshFirstUseGuide()
        {
            if (_hintText != null) _hintText.text = BuildFirstUseGuide.Copy;
            if (_hintLine != null)
                _hintLine.SetActive(_state == BuildHudState.Placing && HintEligible());
        }

        private void OnEnable()
        {
            // The BRAIN pushes every successful commit through its StructurePlaced event
            // (raised only AFTER the charge + BaseLayout append); the HUD merely counts them
            // for the hint gate — presentation-owned counters, no new brain seam.
            BuildModeController.StructurePlaced += OnStructurePlacedCountHint;
        }

        private void OnDisable()
        {
            BuildModeController.StructurePlaced -= OnStructurePlacedCountHint;
        }

        private void OnStructurePlacedCountHint(string id)
        {
            int n = PlayerPrefs.GetInt(HintPlacementsKey, 0) + 1;
            PlayerPrefs.SetInt(HintPlacementsKey, n);
            if (n >= HintPlacementLimit && _hintLine != null && _hintLine.activeSelf)
            {
                _hintLine.SetActive(false);
                FlowTrace.Step("BuildHud",
                    "first-run hint DISMISSED FOREVER: " + n + " successful placements");
            }
        }

        // ── Public API the BRAIN drives ────────────────────────────────────────

        public void Show()
        {
            if (_canvas == null) Build();
            if (_canvas != null) _canvas.SetActive(true);
            // WO-1010 P3: one build session = one Show (the brain calls it exactly once per
            // Enter, BuildModeController.cs:543). Counting here keeps the counter in the
            // presentation layer with no new brain seam.
            PlayerPrefs.SetInt(HintSessionsKey, PlayerPrefs.GetInt(HintSessionsKey, 0) + 1);
            RefreshResources();
            SetState(_state);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.SetActive(false);
        }

        /// <summary>Drive the three-state chrome (Grok CoC model).</summary>
        public void SetState(BuildHudState state)
        {
            _state = state;
            // The ghost controls are exclusive to Placing; Browse/Selected hide them
            // (Selected verbs render on BuildSelectionUI's bar, owned by the brain).
            bool placing = state == BuildHudState.Placing;
            if (_intentBar != null && _intentBar.activeSelf != placing)
                _intentBar.SetActive(placing);

            // WO-1411 — THE BANNER TAKES THE PHASE. Placing means a ghost is armed, so the
            // pick steps are over no matter which door the player came through. Without this,
            // arming from the palette carousel (which raises no CategorySelected/ItemSelected)
            // left the guide on Step.Category and the hint below printed "First build: select a
            // category." over a ghost the player was already dragging — the 07:02 capture.
            if (placing) BuildFirstUseGuide.GhostArmed();

            // WO-1010 P3: the one hint line rides the PLACE phase, behind the first-run gate.
            // Parented under the intent bar, so hiding the bar hides it too; this only decides
            // whether an ELIGIBLE player sees it when placement starts.
            if (_hintLine != null)
            {
                bool showHint = placing && HintEligible();
                if (_hintText != null) _hintText.text = BuildFirstUseGuide.Copy;
                if (_hintLine.activeSelf != showHint)
                {
                    _hintLine.SetActive(showHint);
                    if (showHint)
                        FlowTrace.Step("BuildHud", "first-run hint SHOWN (session " +
                            PlayerPrefs.GetInt(HintSessionsKey, 0) + "/" + HintSessionLimit +
                            ", placements " + PlayerPrefs.GetInt(HintPlacementsKey, 0) + "/" +
                            HintPlacementLimit + ")");
                }
            }

            // The nudge pad only makes sense while something is being positioned, and that is
            // a state the HUD already knows — so it comes and goes on its own. No toggle, no
            // discovery burden, and no permanent control left on screen in Browse.
            // State alone is no longer sufficient — D12 gates on carousel-minimized too, pushed
            // each frame by the brain via SetNudgePadAllowed. Leaving Placing always hides it.
            if (!placing) SetNudgePadVisible(false);

            // Leaving a stale anchor behind would park the chips wherever the last ghost
            // died until the next push; drop it with the state.
            if (!placing) _hasGhostAnchor = false;
        }

        /// <summary>
        /// Fold the "Placing: &lt;name&gt;" label into the intent cluster (owner redesign:
        /// "at most a thin label, or fold that into the intent bar"). Called by the brain on
        /// Arm / begin-move so the collapsed shop needs no black summary panel of its own.
        /// ASCII only; presentation-only; safe before the bar is built (no-op).
        /// </summary>
        public void SetPlacingLabel(string displayName)
        {
            if (_placeName == null) return;
            string n = string.IsNullOrEmpty(displayName) ? "structure" : displayName;
            _pillBase = n;
            _placeName.text = n;
            FlowTrace.Step("BuildHud", "ghost pill label: " + n);
        }

        /// <summary>
        /// WO-1010: name AND cost on the ghost's own pill. The cost used to live only on the
        /// card the player already dismissed, so during placement — the exact moment they are
        /// deciding whether to commit — the price was off-screen. ASCII only.
        /// </summary>
        public void SetPlacingLabel(string displayName, string costLine)
        {
            if (_placeName == null) return;
            string n = string.IsNullOrEmpty(displayName) ? "structure" : displayName;
            _pillBase = string.IsNullOrEmpty(costLine) ? n : n + " - " + costLine;
            _placeName.text = _pillBase;
            FlowTrace.Step("BuildHud", "ghost pill: " + _pillBase);
        }

        /// <summary>
        /// Push the ghost's PROJECTED SCREEN POINT plus its validity. The BRAIN projects
        /// (it owns the camera) — presentation never touches a world object or a camera,
        /// which is the layering rule this HUD exists to keep.
        ///
        /// <paramref name="blockedReason"/> is shown on the OK chip when placement is
        /// invalid, so the refusal is READABLE TEXT rather than a colour the player has to
        /// interpret — validity here is shape + word, never tint alone.
        /// </summary>
        public void TrackGhost(Vector2 screenPoint, bool valid, string blockedReason)
        {
            _ghostScreenPoint = screenPoint;
            _hasGhostAnchor = true;
            if (valid != _ghostValid)
            {
                _ghostValid = valid;
                FlowTrace.Step("BuildHud", "ghost validity -> " + (valid ? "OK" : "BLOCKED"));
            }
            _ghostBlockReason = blockedReason ?? string.Empty;
        }

        /// <summary>
        /// Follow the ghost with the PILL (the rail is fixed and needs no pass). LateUpdate so
        /// the anchor pushed this frame is already current. Runs only while Placing, so Browse
        /// costs nothing.
        /// </summary>
        private void LateUpdate()
        {
            LayoutGhostControlsNow();
        }

        /// <summary>
        /// The follow/clamp pass, callable directly. LateUpdate drives it at runtime; the
        /// headless UI capture calls it explicitly because MonoBehaviour ticks do NOT run in
        /// edit mode — without this the capture would photograph the pill parked at the canvas
        /// centre and the screenshot would prove nothing about the clamp rule it is meant to
        /// verify. STILL DIRECTLY CALLABLE by contract, even though D14 shrank the work to the
        /// pill alone: the OK/No verdict below is state, not layout, and the capture needs it.
        /// </summary>
        public void LayoutGhostControlsNow()
        {
            if (_state != BuildHudState.Placing) return;

            // ── NOTHING IS LAID OUT HERE ANY MORE, AND THAT IS THE POINT. ─────────
            // D14 fixed the rail; WO-944 (owner F8 seq 2250, flagged live in the 22:11 build:
            // "can we make the title of the item pin staticl maybe at the top of the screen")
            // pinned the pill top-centre — the last follower is gone, so the whole
            // follow/clamp pass retired with it (UI_PLAYBOOK §8: "if a control does not have
            // to follow, do not make it follow"). TrackGhost still feeds validity + reason;
            // only the VERDICT below remains, which is state, not layout.

            // ── THE VERDICT: state on the button, full reason IN WORDS on the plate. ─
            // The first capture put the whole reason ON the chip and "Not enough Wood" wrapped
            // to four lines and spilled outside a 52px circle — unreadable, and it covered the
            // other chips. A sentence needs the WIDE surface; the button only ever has room for
            // a verb. The block-reason plate carries the why as TEXT, so the refusal is never
            // colour-alone.
            //
            // WO-1411: there is now exactly ONE verdict path, because there is exactly one
            // confirm rendering. The old fork (ASCII label flips OK<->No / D17 sprite dims to
            // alpha 0.35) is gone with the glyphs. A dimmed check-mark was always the weaker
            // half of that fork — it asked a red/green colourblind owner, and a PNG, to judge
            // an alpha change — so the surviving path is the WORD: PLACE while it is legal,
            // BLOCKED while it is refused, plus non-interactable either way.
            if (_okChipLabel != null)
            {
                string want = _ghostValid ? PlaceVerbWord : BlockedVerbWord;
                if (_okChipLabel.text != want) _okChipLabel.text = want;
                _okChipLabel.color = _ghostValid ? ElarionUi.Gilt : new Color(0.86f, 0.32f, 0.30f);
            }
            if (_okChip != null) _okChip.interactable = _ghostValid;

            if (_placeName != null)
            {
                if (_placeName.text != _pillBase) _placeName.text = _pillBase;
                _placeName.color = ElarionUi.Gilt;
            }

            bool showReason = !_ghostValid && !string.IsNullOrWhiteSpace(_ghostBlockReason);
            if (_blockReasonText != null && showReason && _blockReasonText.text != _ghostBlockReason)
                _blockReasonText.text = _ghostBlockReason;
            if (_blockReasonPlate != null && _blockReasonPlate.activeSelf != showReason)
                _blockReasonPlate.SetActive(showReason);
        }

        /// <summary>Re-read the live wallet (called by the brain on transitions).</summary>
        public void RefreshResources()
        {
            _wallet?.Refresh();
        }

        // NOTE: the local PinSize helper (the "long thin rectangles in horizontal mode" fix
        // that collapsed a kit button's fraction anchors onto a fixed pixel box) went with the
        // last kit button in this file — WO-1010 D10 replaced the pinned 200x132 "X Done" with
        // the hand-built corner plate above. The pattern itself is unchanged canon and still
        // lives at BuildPaletteUI.PinSize / BuildTabRow; every rect in this file is now
        // authored at fixed pixels directly, which is the same rule one step earlier.

        // ── uGUI helper (BuildPaletteUI/BuildSelectionUI shape) ────────────────
        private static TextMeshProUGUI MakeText(Transform parent, string text, float size,
            Color color, FontStyles style, TextAlignmentOptions align, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            ElarionUiKit.EnsureFont(t);
            return t;
        }
    }
}
