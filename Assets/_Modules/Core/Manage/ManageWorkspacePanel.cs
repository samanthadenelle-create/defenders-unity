// =============================================================================
// ManageWorkspacePanel - WO-2002. THE ONE DUMB RENDERER every Manage tab
// (BUILD / ARMY / RESEARCH) paints through.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Manage
//
// ============================ THE DUMB-UI RULE ===============================
// Canon 9. This file MAY: bind text supplied by the VM, bind sprites by supplied
// asset key, show/hide by explicit state fields, invoke supplied callbacks, render
// supplied progress values, apply common visual primitives.
//
// This file MAY NOT: calculate costs, inspect player resources, decide locks,
// determine Heart requirements, determine max level, read queue service state,
// calculate queue capacity, derive labels from enum names, parse ids, decide which
// destination a prerequisite CTA opens, calculate upgrade deltas, mutate save data,
// or call Barracks / BuildTimer / Research / Heart services.
//
// ⛔ THAT LIST IS ENFORCED BY A SOURCE ORACLE, NOT BY REVIEW:
//    Assets/Editor/Regression/ManageDumbViewRegression.cs, marker MANAGE_DUMB_VIEW_OK.
// It scans THIS FILE for each banned shape, self-tests every pattern RED against a
// fixture first, and carries a REVERT RECIPE per case. A rule nobody can check is a
// rule that decays - and this repo has the receipts (CLAUDE.md 2, 5, 16).
//
// TWO STRUCTURAL GUARDS BACK THE ORACLE UP:
//  1. ASSEMBLY. This file lives in DeNelle.Core, which does NOT reference
//     DeNelle.Village - so BuildTimerService (Village/Buildings/BuildTimerService.cs)
//     and BarracksService (Village/Troops/BarracksService.cs) are not merely
//     forbidden here, they are UNREACHABLE. The oracle still bans them by name
//     because GameStateService DOES live in Core (Core/State/GameStateService.cs)
//     and the assembly boundary alone would not stop a save read.
//  2. NO INFERENCE FROM NULL. Every state the renderer reads is an explicit field
//     (Visible / Enabled / IsSelected / VisualState). It calls `cb?.Invoke()` and
//     never `if (cb != null)` as a state test; the oracle bans the comparison form.
//
// ⛔ NOT A MonoBehaviour, AND THAT IS DELIBERATE. WO-2002 names it a "panel", but
// PanelDoorRegression (Assets/Editor/Regression/PanelDoorRegression.cs:20-29)
// defines panel-like as MonoBehaviour + a name ending in "Panel", and FAILS any
// such type with no door. This is a RENDERER a host embeds - the same shape as
// ElarionUiKit.BuildObsidianPanel: build under a supplied parent, return handles -
// not a destination the player routes to. Keeping it a plain class is honest about
// that AND keeps the door oracle's teeth sharp for real panels. The dumb-view
// oracle asserts this file contains no ": MonoBehaviour" so nobody "upgrades" it
// later and silently trips panel-door. WO-2001 owns the host that opens it.
//
// ============================= THE BAND LAW ==================================
// ⚠ TMP CULLS AN ENTIRE LINE whose fontSizeMin cannot seat in its rect: a text band
// under about 24 reference px renders BLANK, not small. That cost three separate
// defects on 2026-09-06. So:
//   * every band's height is a FIXED PIXEL constant, stated below with its px;
//   * the heights are SUMMED, subtracted from the MEASURED well, and the GRID takes
//     the remainder - the same law ManageScreenPanel.cs:60-72 already holds after the
//     BUILD-1 overprinting defect;
//   * MinTextBandPx (28) is a HARD FLOOR. A band that cannot be given 28px is
//     OMITTED and announced in px through FlowTrace - never shrunk. An omitted band
//     is visibly missing; a culled one is invisible, and invisible is the trap;
//   * every tap target is authored at or above ElarionUiKit.MinTouchPx (112) so
//     ClampMinTouch never has to rescue it. ElarionUiKit.cs:1100 states the rule:
//     "Author the band above MinTouchPx; do not rely on the clamp." A clamp growth
//     is a control spilling into its neighbour.
//
// MEASURED WELLS this table is annotated against: 533 / 542 / 612 reference px, the
// three captured Manage body wells (ManageScreenPanel.cs:194 "at 2670x1200 well=533";
// HeartPanel.cs:23-30 cites the same span from Builds/manage-redesign-capture.log).
// ⚠ FINDING HANDED BACK WITH THIS WORK ORDER, AND IT IS ARITHMETIC, NOT AN OPINION.
// The MINIMUM stack is header 120 + tabs 120 + selection FLOOR 256 + three 12px
// gaps = 532px, before any filter row, any activity strip and any grid at all. The
// three captured Manage wells are 533 / 542 / 612px. So in the old modal chrome the
// workspace has 1px left for the grid - i.e. THE GRID CANNOT EXIST THERE, and
// canon 3's "at least 12 visible tiles" is unreachable by a factor of about four.
// ⛔ AND THE CONSEQUENCE IS WORSE THAN A CRAMPED GRID. Once the grid clamps to its
// 150px floor the cursor stands at 264 + 150 + 12 = 426px, and the 256px selection
// floor then ends at 682px - inside a 533px well. THE CTA ROW, the single most
// important control on the screen, IS OFF THE BOTTOM on the old modal chrome.
// A full-screen well of roughly 1450px is what the canon actually implies:
// 532 fixed + the filter row 132 + a 4-row BUILD grid at the 190px tile ceiling
// (4 x 190 + 3 x 10 = 790) = about 1454px.
// ⛔ THAT IS WO-2001's CALL (information architecture / the host chrome), NOT this
// file's. This renderer never silently re-columns, never shrinks a text band and
// never paints a culled screen: it measures, trims the selection card in a stated
// order, and REPORTS the shortfall in px through FlowTrace.
//
// ⚠ THE 532px FIGURE ABOVE IS RETIRED ARITHMETIC (WO-1443, 2026-09-06). THREE of its
// four terms are gone on a grid screen: the HEADER BAND no longer exists (breadcrumb
// -> the host's panel title, QUEUE -> the host's top-right pill), the SELECTION band
// collapses to 0 when nothing is selected, and the ACTIVITY strip is retired from
// Manage entirely (the mockup carries running work on the QUEUE badge and in the
// QUEUE overlay, screen 8 - not on every screen). The minimum stack on a grid screen
// is now tabs 120 + one gap 12 = 132px, or 264px with a filter row.
//
// AND THE WELL ITSELF GREW, which is the half that actually mattered. The measured
// gap was one number: MANAGE_FLOW_INVENTORY reported ARMY content=590px in a 190px
// viewport against a mockup panel that says "All 9 troops visible, no scrolling".
// The host now takes 0.02-0.98 of the canvas (was 0.05-0.95) and lifts the body
// ceiling to meet the chrome row at 0.845 (the strip between 0.835 and the frame's
// own header zone at 0.900 held nothing), so the well goes ~533px -> ~553px.
// Against that well, with SQUARE cells and AUTHORED capacity (ManageTabVM.GridRows):
//   ARMY  3x3: grid 553 - 132(tabs)          = 421px -> cell 134px, NINE tiles, no scroll
//   BUILD 5x2: grid 553 - 132(tabs) - 132(chips) = 289px -> cell 140px, TEN tiles, no scroll
// Both clear MinTileHeightPx(120) - which is only legal because the tile now carries
// ONE text band instead of two. Retiring the tab row into the host chrome (it belongs
// to the mockup's HUB, screen 1) is the next ~132px and takes ARMY's cell to ~178px.
// ⚠ AND THE OTHER HALF OF FIX (a) IS STILL OPEN: a DETAIL screen has no tiles, yet
// the grid band is still reserved at its MinTileHeightPx floor. Skipping it when the
// tab has no tiles is the matching change and belongs to WO-2001, not here.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Core.Manage
{
    /// <summary>
    /// The common dumb Manage renderer. Construct it over a body RectTransform, call
    /// <see cref="Bind"/> whenever the model changes, <see cref="Clear"/> to tear down.
    /// It has no Update: re-binding on a tick is the host's job, which keeps every
    /// value on screen a value the model handed over.
    /// </summary>
    public sealed class ManageWorkspacePanel
    {
        // ── BAND TABLE (fixed reference px). Every one clears MinTextBandPx. ──
        // ⛔ THERE IS NO HeaderBandPx. WO-1443 emptied the header band (breadcrumb -> the host's
        // panel title, sub line -> deleted, QUEUE door -> the tab row) and then deleted the band
        // rather than leaving a 120px constant nothing seats in. See BuildTabs' summary.
        // ⛔ NO TabsBandPx EITHER. The BUILD | ARMY | RESEARCH row is gone with the header band -
        // the mockup navigates by the HUB (panel 1) and the back arrow, and no panel draws a tab
        // row. Its 120px + 12px went to the grid. See BuildFilters' band note for the live one.
        private const float FiltersBandPx = 120f;  // same as tabs; 0 when the tab supplies no filters
        private const float ActivityBandPx = 120f; // strip is tappable (OpenQueue) so it is a touch band
        private const float BandGapPx = 12f;       // guaranteed gutter - no two bands ever touch

        // Selection card sub-bands, top-down inside the selection band.
        // ⛔ THE OLD STACKED-CARD BAND TABLE IS DELETED - EIGHT CONSTANTS AND TWO HELPERS.
        // SelTitlePx / SelLevelPx / SelDescPx / SelStatsPx / SelCostPx / SelWhyPx / SelGapPx /
        // SelActionPx / SelectionFullPx, plus StatsLine() and CostLine(), belonged to the card that
        // shared a well with the grid and DROPPED sub-bands when it was short. WO-1443 gave the
        // detail screen the whole body and a two-column layout that flows in px, so none of them was
        // read any more. They are deleted rather than left standing: a constant nothing reads is the
        // duplicated state this file has already been burned by twice today (HeaderSubtitle, and the
        // AtCapacity flag whose pip rendered outside the panel).
        // SelectionFloorPx SURVIVES - the card-shortfall warn reads it, and it is the four bands
        // canon 11 makes non-negotiable: what is it, what does it cost, why can I not act, what can
        // I do.
        private const float SelectionFloorPx = 256f;

        private const float MinGridPx = 130f;      // one MinTileHeightPx(120) row plus a gap
        // 120px: one 0.24 name band = 28.8px, clear of the MinTextBandPx(28) cull floor, and 120 is
        // itself MinTouchPx. Was 150 while the tile carried TWO text bands; the mockup's tile carries
        // one (see the tile band table below), and 150 would have made the mockup's own stated
        // capacity - "All 9 troops visible, no scrolling" - arithmetically impossible in this well.
        private const float MinTileHeightPx = 120f;
        // ⚠ A CEILING IS AS NECESSARY AS A FLOOR. Cell height tracks cell WIDTH so tiles stay
        // squarish, and on a wide band four columns give ~250-300px of width - which would make
        // one row of tiles taller than the entire grid band and silently defeat canon 3's
        // "12 visible tiles". Clamped, so the tile fractions below stay annotated over a range
        // that is actually reachable (150-190px).
        private const float MaxTileHeightPx = 190f;
        /// <summary>
        /// The widest a tile may be against its own height, MEASURED off the owner's mockup:
        /// BUILD's tiles (panel 2) are about 1.07:1 and ARMY's (panel 4) about 2.3:1, so 2.3 is
        /// the drawn ceiling rather than a guess.
        /// <para>⛔ IT EXISTS BECAUSE THE PANEL NOW FILLS THE SCREEN (owner ruling 2026-09-07).
        /// The band grew by half, and without a cap the reclaimed width went straight into the
        /// cells - round 4 already measured what that looks like (793x134, 5.9:1) and called them
        /// BARS. The surplus becomes an even side margin instead, which is what the mockup draws.</para>
        /// </summary>
        private const float MaxTileAspect = 2.3f;
        private const float TileGapPx = 10f;

        /// <summary>
        /// ⚠ THE TMP CULL FLOOR. A text band below this renders BLANK, not small. Bands that
        /// cannot be given this much are OMITTED and reported, never shrunk.
        /// </summary>
        private const float MinTextBandPx = 28f;

        // ── Tile internals, as fractions of the cell. px stated at cell=150 / 190. ──
        // ⛔ THE TILE IS ART + ONE NAME STRIP. That is what the mockup draws, on both grid panels.
        // docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png screens 2 and 4: every tile is a square of
        // art with the name on a strip across its bottom. There is no second text line on a grid
        // tile - the STATE lives on the detail screen (screen 9 draws "Requires Barracks Tier 4"
        // there, beside a padlock), and on the tile it is the medallion that carries it.
        // ⚠ THIS IS ALSO WHAT MAKES THE MOCKUP'S CAPACITY LEGAL. Two text bands at 0.20 and 0.22 of
        // the cell need a 150px cell to clear the 28px TMP cull floor (a band under it renders
        // BLANK, not small). One band at 0.24 clears the same floor at 117px, which is why
        // MinTileHeightPx could honestly come down to 120 and nine troops can be on screen at once.
        // Dropping the second band is not a cosmetic simplification; it is the arithmetic.
        private const float TileProgY0 = 0.005f, TileProgY1 = 0.02f;  // a BAR, no text - exempt
        private const float TileTitleY0 = 0.02f, TileTitleY1 = 0.26f; // 24.0% -> 29px at the 120px floor
        // ⭐ THE ART FILLS THE CARD. Mockup panel 2's tiles are square-ish cards whose illustration
        // reaches the edges, with only the name strip below it - not a small medallion floating in a
        // plate. The art is painted preserveAspect, so on BUILD's near-square 236x220 cell it now
        // fills almost the whole card, while on ARMY's wide 399x188 cell it stays a centred square
        // (as panel 4 draws it). One zone, both shapes, because preserveAspect does the work.
        // Was x 0.14-0.86 / y 0.28-0.99, which threw away 28% of the width on every tile.
        private const float TilePortY0 = 0.26f, TilePortY1 = 0.985f;
        private const float TilePortX0 = 0.04f, TilePortX1 = 0.96f;
        private const float TileMedX0 = 0.80f, TileMedX1 = 0.96f;     // compact medallion, inset top-right
        private const float TileMedY0 = 0.68f, TileMedY1 = 0.93f;
        // WO-1563: the state WORD, top-LEFT, on the medallion's own band. It ends at TileMedX0 so
        // the word and the glyph can never overprint. 0.35 of the cell = 42px at the
        // MinTileHeightPx(120) floor, clear of MinTextBandPx(28) - it takes no existing text band.
        private const float TileStateX0 = 0.03f, TileStateX1 = 0.77f;
        // ⛔ NO TileSelBarX1. The selected tile's cue is a GOLD BORDER around the whole tile
        // (CAPTURE_LOOP_GOAL 3.0b, and the mockup draws it that way on screens 2/4/6), not the
        // left-edge bar this constant used to seat. The constant went with the bar rather than
        // sitting here unread.

        // ── Handles ───────────────────────────────────────────────────────────
        private readonly RectTransform _body;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Last measured well height in reference px. Diagnostics only.</summary>
        public float LastWellPx { get; private set; }

        /// <summary>Last computed grid band height in reference px. Diagnostics only.</summary>
        public float LastGridPx { get; private set; }

        /// <summary>
        /// The queue model from the most recent <see cref="Bind"/>. The HOST paints the door - a
        /// small top-right pill with a count badge, exactly as the owner's mockup draws it on every
        /// panel - and reads the model from here so there is ONE composed queue projection on the
        /// screen instead of the host quietly growing a second one.
        /// </summary>
        public ManageQueueVM Queue { get; private set; }

        public ManageWorkspacePanel(RectTransform body)
        {
            _body = body;
            if (_body == null)
                FlowTrace.Fail("Manage", "ManageWorkspacePanel was constructed over a null body rect - " +
                    "nothing will render and no tab will report why");
        }

        // =====================================================================
        //  BIND - the only entry point. Rebuilds the whole workspace from the VM.
        // =====================================================================

        /// <summary>
        /// Paints <paramref name="vm"/>. A null VM clears the surface and says so; it never
        /// leaves the previous frame's pixels on screen pretending to be current.
        /// </summary>
        public void Bind(ManageWorkspaceVM vm)
        {
            Clear();
            if (_body == null) return;
            if (vm == null)
            {
                FlowTrace.Warn("Manage", "Bind received a null ManageWorkspaceVM - the workspace is " +
                    "cleared rather than left showing a stale frame");
                return;
            }
            Guard.Try("Manage", "render manage workspace", () => Build(vm));
        }

        /// <summary>Destroys everything this renderer spawned. Safe to call repeatedly.</summary>
        public void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                var go = _spawned[i];
                if (go == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        // =====================================================================
        //  LAYOUT - the band law, in one place
        // =====================================================================

        private void Build(ManageWorkspaceVM vm)
        {
            float well = MeasureWellPx();
            LastWellPx = well;

            // The renderer no longer PAINTS the queue door (it is the host's top-right pill since
            // WO-1443), but it still binds the model and hands it over, so there is exactly one
            // composed queue projection on the screen rather than the host growing a second one.
            Queue = vm.Queue;

            ManageTabVM tab = vm.ActiveTab;
            bool hasFilters = tab != null && tab.Filters != null && tab.Filters.Count > 0;
            bool hasActivity = tab != null && tab.Activity != null && tab.Activity.Visible;

            // ⛔ NOTHING ABOVE THE GRID BUT THE FILTER CHIPS. Do not put a band back here.
            // The header band went first (breadcrumb -> the host's panel title, QUEUE -> the host's
            // top-right pill). The TAB row went second, with the hub: mockup panels 2/4/6 carry a
            // title, chips on BUILD, and then the grid - no tab row anywhere. Navigation is panel 1
            // plus the back arrow. Between them that is 264px handed to the tiles, which is what
            // finally lets ARMY seat three ~188px rows instead of three ~134px ones.
            float fixedTop = (hasFilters ? FiltersBandPx + BandGapPx : 0f);
            float fixedBottom = (hasActivity ? ActivityBandPx + BandGapPx : 0f);

            // ⛔ WO-1443 section 3 - THE SELECTION BAND IS NOT RESERVED WHEN NOTHING IS SELECTED.
            // Owner felt-test 2026-09-06, verbatim: "dont need the bottom line, close button is
            // enough". Her capture showed roughly 40% of the screen given to a bordered box holding
            // one hint sentence, because the band was reserved unconditionally and the model filled
            // it with EmptyText. The sentence is deleted at source (ManageScreenVM.FillActiveTab
            // sets EmptyText = null) and the band now COLLAPSES TO ZERO with it, so the grid takes
            // the room - the alternative the ruling explicitly forbids is an empty bordered box
            // with a lone button in it, which is the same 40% doing even less.
            // This is also fix (a) that this file's own header hands back to WO-2001: "the renderer
            // must skip the selection band when Selection.Visible is false".
            bool hasSelection = tab != null && tab.Selection != null && tab.Selection.Visible;
            bool hasTiles = tab != null && tab.Tiles != null && tab.Tiles.Count > 0;

            // ⛔ A GRID SCREEN AND A DETAIL SCREEN ARE DIFFERENT SCREENS. They never share the well.
            // The owner's mockup is explicit about this and it is the shape the model already emits:
            //   panels 2 / 4 / 6 - title, chips on BUILD, GRID. No card.
            //   panels 3 / 5 / 9 - title, one item's DETAIL, filling the panel. No grid.
            // The old layout reserved BOTH on every screen, which is what produced a 392px bordered
            // box holding one hint sentence under a grid that had 150px (the owner's original
            // complaint) and, on a detail screen, an empty 150px grid band above the card.
            // So the well goes to whichever screen this IS - and that completes fix (a) from this
            // file's header, whose second half ("skip the grid band when the tab has no tiles") had
            // been owed since WO-2002.
            float body = well - fixedTop - fixedBottom;
            float selection = 0f, grid = 0f;
            if (hasTiles)
            {
                grid = body;
            }
            else if (hasSelection)
            {
                // The DETAIL screen takes the whole body. It no longer trims sub-bands to fit a
                // 392px reservation, because it is not sharing with anything.
                selection = body;
            }
            else
            {
                // Neither: the grid band paints the model's EmptyText sentence, which is the honest
                // empty state ("Nothing in this filter yet.").
                grid = body;
            }

            if (hasTiles && grid < MinTileHeightPx)
            {
                FlowTrace.Warn("Manage", "grid band is " + grid.ToString("0") + "px - under one " +
                    MinTileHeightPx + "px tile row. The workspace needs a taller well than this host " +
                    "gives it (WO-2001); the grid is clamped and will scroll");
                grid = Mathf.Max(grid, MinTileHeightPx);
            }
            if (hasSelection && selection < SelectionFloorPx)
                FlowTrace.Warn("Manage", "detail card has " + selection.ToString("0") + "px, under the " +
                    SelectionFloorPx + "px floor for the four bands canon 11 makes non-negotiable " +
                    "(what is it, what does it cost, why can I not act, what can I do)");
            LastGridPx = grid;

            float cursor = 0f;
            if (hasFilters)
            {
                BuildFilters(BandFromTop(_body, "ManageFilters", cursor, FiltersBandPx), tab);
                cursor += FiltersBandPx + BandGapPx;
            }

            // Exactly ONE of these two paints. See the note above: a grid screen and a detail screen
            // are different screens and never share the well.
            if (grid > 0f)
            {
                BuildGrid(BandFromTop(_body, "ManageGrid", cursor, grid), tab);
                cursor += grid + BandGapPx;
            }

            if (hasActivity)
            {
                BuildActivity(BandFromTop(_body, "ManageActivity", cursor, ActivityBandPx), tab.Activity);
                cursor += ActivityBandPx + BandGapPx;
            }

            if (selection > 0f)
                BuildSelection(BandFromTop(_body, "ManageSelection", cursor, selection), tab, selection);

            FlowTrace.Step("Manage", "MANAGE_SCREEN " + (hasTiles ? "GRID" : hasSelection ? "DETAIL" : "EMPTY") +
                " well=" + well.ToString("0") + " grid=" + grid.ToString("0") +
                " detail=" + selection.ToString("0") + "px");
        }

        /// <summary>
        /// The body's height in reference px. Falls back to the kit's post-scale canvas height
        /// when the rect has not been laid out yet - and SAYS SO, because a silent fallback here
        /// would make every band number in the trace a fiction.
        /// </summary>
        private float MeasureWellPx()
        {
            float h = _body != null ? _body.rect.height : 0f;
            if (h > 1f) return h;
            float fallback = ElarionUiKit.PostScaleCanvasHeight(_body);
            FlowTrace.Warn("Manage", "body rect has no height yet (rect.height=" + h.ToString("0.##") +
                ") - band arithmetic falls back to the canvas height " + fallback.ToString("0") +
                "px. Bind after a layout pass to get real numbers");
            return fallback;
        }

        // =====================================================================
        //  BANDS
        // =====================================================================

        /// <summary>
        /// ⛔ THERE IS NO HEADER BAND. Do not add one back.
        /// <para>WO-1443 sections 1 and 1B, owner felt-test 2026-09-06: <i>"remove the manage army and
        /// sub line replace the manage top"</i> and <i>"remove heart level queue"</i>. The band used to
        /// carry THREE things - a breadcrumb copy, a sub line, and the QUEUE chip with an
        /// "IDLE . 0 OF 5" line under it. The breadcrumb moved UP into the host chrome's panel title
        /// (ManageScreenPanel.ApplyWorkspaceTitle), the sub line is deleted from the contract, and the
        /// QUEUE door moved into the HOST CHROME as a small top-right pill. With nothing left to hold
        /// the band itself is gone, and its 120px + 12px gutter went to the grid - which, together
        /// with the collapsing selection band (section 3), is where the space the owner wanted comes
        /// from.</para>
        /// <para>⛔ AND THERE IS NO QUEUE FACE IN THIS ROW EITHER - THAT SEAT IS RETIRED.
        /// It existed for about four hours on 2026-09-06, built from the owner's words before her
        /// MOCKUP was in the repo. The mockup
        /// (docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png) draws QUEUE as a SMALL PILL AT TOP-RIGHT
        /// with a count badge on every one of its eight numbered panels, and had said so since 09:26
        /// that morning; she restated it in words the same day - <i>"the queuing doesn't deserve a
        /// place here... something small up with like the previous next back kind of buttons - I don't
        /// think it deserves its own lane."</i> The tab-row seat also produced two defects of its own
        /// in the 14:59 capture: a truncated <c>QUEUE . FULL 5 O...</c> face and a stray gold bar.
        /// Both die with the seat. The door now lives at
        /// ManageScreenPanel.BuildTabs (the host's chrome row) - it MOVED, it was never dropped.</para>
        ///
        /// <para>⛔ AND THERE IS NO TAB ROW AT ALL ANY MORE - BuildTabs IS DELETED, NOT EMPTIED.
        /// Mockup panels 2, 4 and 6 carry a title, the filter chips on BUILD, and then the grid.
        /// There is no BUILD | ARMY | RESEARCH row on any panel; navigation is the HUB (panel 1)
        /// plus the back arrow, which is what ManageScreenPanel.ShowLauncher restores. Its 120px
        /// band and 12px gutter were the last reserve standing between ARMY and the mockup's own
        /// stated capacity, and they are now the tiles': three rows go from ~134px to ~188px.
        /// The model still composes <c>Tabs</c> - the hub and ActiveTabIndex both need it - so this
        /// is a RENDERING that stopped, not a concept that was removed.</para>
        /// </summary>

        private void BuildFilters(RectTransform band, ManageTabVM tab)
        {
            var filters = tab.Filters;
            for (int i = 0; i < filters.Count; i++)
            {
                var f = filters[i];
                if (f == null) continue;
                // Full band height (0f..1f) = 120px >= MinTouchPx(112). See BuildTabs' note.
                var btn = ElarionUiKit.Button(band, f.Label ?? string.Empty,
                    f.IsActive ? ElarionUiKit.ButtonKind.Gold : ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(SlotStart(i, filters.Count), 0f),
                    new Vector2(SlotEnd(i, filters.Count), 1f),
                    MakeInvoker(f.Activate));
                Track(btn);
                if (f.IsActive) Underline(btn.transform);
            }
        }

        // =====================================================================
        //  GRID
        // =====================================================================

        /// <summary>
        /// The school painting's right edge, as a fraction of the well. Mockup panel 7 gives the
        /// picture a little under half the panel; 0.40 leaves the rows the wider half, which is
        /// where the words are.
        /// </summary>
        private const float ListPaintingX1 = 0.40f;

        /// <summary>The gutter between the painting and the first row.</summary>
        private const float ListPaintingGapF = 0.02f;

        /// <summary>
        /// ⭐ MOCKUP PANEL 7's LEFT-HAND PAINTING. Paints the school into the left
        /// <see cref="ListPaintingX1"/> of the well and returns the zone the ROWS now own.
        ///
        /// <para>⛔ THE PICTURE IS SQUARE AND CROPPED, NOT LETTERBOXED. The delivered portraits
        /// are 1024x1024 with transparent corners; <see cref="SquarePortrait"/> envelope-crops to
        /// the zone, which is the same treatment the grid tiles got in WO-1567 panel row 2. A
        /// preserveAspect fit into a tall zone would band the picture with black top and bottom,
        /// which is the failure ManageArt's hub-art note records for the retired landscape strips.
        /// The zone is therefore kept SQUARE and centred vertically rather than stretched.</para>
        ///
        /// <para>Returns null when the art does not resolve - the caller then keeps the full band
        /// and the rows lose nothing. An ART ASK must never cost the player the words.</para>
        /// </summary>
        private RectTransform BuildListPainting(RectTransform band, string artKey,
                                                float bandW, float bandH)
        {
            if (band == null) return null;
            var art = ManageArt.LoadSprite(artKey);
            if (art == null)
            {
                // LoadSprite has already announced the miss by key. This line says what the SCREEN
                // does about it, which the loader cannot know.
                FlowTrace.Once("Manage", "list-painting-miss:" + artKey,
                    "the research tree has no painting for '" + artKey + "', so the rows keep the " +
                    "full well rather than sitting beside an empty rect (mockup panel 7 draws the " +
                    "school on the left). This is an ART ASK, not a layout fault.");
                return null;
            }

            // A SQUARE seat inside the left column, centred on the band's vertical middle. Its side
            // is whichever of the two is smaller, so the picture never overflows either axis.
            float sidePx = Mathf.Min(bandW * ListPaintingX1, bandH);
            float halfW = bandW > 1f ? (sidePx / bandW) * 0.5f : ListPaintingX1 * 0.5f;
            float halfH = bandH > 1f ? (sidePx / bandH) * 0.5f : 0.5f;
            float cx = ListPaintingX1 * 0.5f;
            var artZone = Zone(band, "ListPainting",
                new Vector2(Mathf.Max(0f, cx - halfW), Mathf.Clamp01(0.5f - halfH)),
                new Vector2(Mathf.Min(ListPaintingX1, cx + halfW), Mathf.Clamp01(0.5f + halfH)));
            SquarePortrait(artZone, artKey, dim: false);
            FlowTrace.Step("Manage", "MANAGE_LIST_PAINTING key=" + artKey + " side=" +
                sidePx.ToString("0") + "px in a " + bandW.ToString("0") + "x" + bandH.ToString("0") +
                "px well - the rows take x " + (ListPaintingX1 + ListPaintingGapF).ToString("0.##") +
                "..1.0");

            return Zone(band, "ListRowsHost",
                new Vector2(ListPaintingX1 + ListPaintingGapF, 0f), new Vector2(1f, 1f));
        }

        private void BuildGrid(RectTransform band, ManageTabVM tab)
        {
            if (tab == null) return;
            var tiles = tab.Tiles;
            if (tiles == null || tiles.Count == 0)
            {
                var empty = ElarionUiKit.Label(band, tab.EmptyText ?? string.Empty, 0.42f, 0.58f,
                    ElarionUi.ParchmentDim, ElarionUi.FontBody, TextAlignmentOptions.Center);
                ElarionUiKit.FitSingleLine(empty, 24f, 40f);
                return;
            }

            // ⛔ THE CELL IS SQUARE AND THE CAPACITY IS AUTHORED. Do not go back to sizing the cell
            // from the band width and letting the row count fall out.
            // TWO measured defects came from that, both on 2026-09-06:
            //  (a) MANAGE_FLOW_INVENTORY BUILD: tiles=17 columns=4 visibleRows=0.8 - less than ONE
            //      row of seventeen tiles, under a chip that says ALL. ARMY: tiles=9 rows=3 but
            //      visibleRows=1.0 against content=590px in a 190px viewport, while the mockup's
            //      screen 4 says in words "All 9 troops visible, no scrolling".
            //  (b) A cell sized from the width was ~536x190 - nearly 3:1 - and the delivered frames
            //      are SQUARE 512px art drawn with preserveAspect, so the frame collapsed to a
            //      cellH square in the middle of a wide cell and ran straight through the name.
            // A square cell fixes the second outright and makes the first arithmetic instead of luck.
            int columns = tab.GridColumns > 0 ? tab.GridColumns : 3;
            int rows = tab.GridRows > 0 ? tab.GridRows : 3;

            // ⭐ WO-1567 PANEL ROW 8 - THE SCHOOL PAINTING TAKES THE LEFT OF THE WELL, THE ROWS
            // TAKE THE REST. Mockup panel 7 is a big square picture of the building on the left
            // with its perk rows stacked to the right; the owner's capture
            // (Logs/device/screens/owner-screen-20260907-010151.png) shows four rows spanning the
            // whole well and no picture at all.
            // ⛔ ONLY ON THE LIST SHAPE, and only when the MODEL supplied a key. A grid screen
            // (BUILD / ARMY / the research picker) must keep the full width, and a list with no
            // painting must not lose 40% of its band to an empty rect.
            // The rows are re-parented into a sub-zone, so every measurement below - bandW, the
            // cell, the whole-row trim - resolves against the band the rows actually get. Carving
            // the picture out afterwards is how a "fixed" layout still renders rows off the edge.
            float bandW = band.rect.width > 1f ? band.rect.width : 1080f;
            float bandH = band.rect.height > 1f ? band.rect.height : LastGridPx;
            if (columns == 1 && !string.IsNullOrEmpty(tab.HeaderArtKey))
            {
                var rowsHost = BuildListPainting(band, tab.HeaderArtKey, bandW, bandH);
                if (rowsHost != null)
                {
                    band = rowsHost;
                    // ⚠ ARITHMETIC, NOT A RE-READ. A rect created this frame has had no layout
                    // pass, so `rowsHost.rect.width` can legitimately read 0 - and a 0 here would
                    // silently fall back to the 1080px literal above and size every cell against a
                    // band that does not exist. The fractions are this method's own, one line up.
                    bandW = bandW * (1f - ListPaintingX1 - ListPaintingGapF);
                }
            }

            // ⛔ THE GRID FILLS THE BAND. THE CELL IS NOT SQUARE. Do not "fix" this back to a square.
            // MEASURED in Builds/ui-capture/ManageFlow_ARMY_gridtop_2670x1200.png: a square cell
            // sized by the band HEIGHT gave 3 x 134px = 422px of grid inside an ~1800px band - the
            // tiles huddled in a narrow strip with the panel black on both sides, occupying about
            // 22% of the width. Every element was present and correctly ordered and it still read as
            // a different screen from the mockup, because in the mockup the grid spans the content
            // edge to edge.
            // The geometry is forced and there is no third option: the mockup's panel content is
            // roughly 2:1 and this modal's band is roughly 8:1, so square tiles CANNOT also fill the
            // width. Filling the width is what the picture shows, so the cell is WIDE:
            //   width  = the band divided by the AUTHORED columns (5 / 3 / 4)
            //   height = the band divided by the AUTHORED rows    (2 / 3 / 1)
            // The ART stays square regardless - the portrait and its frame are painted preserveAspect
            // inside TilePort*, which is why a wide cell is safe here and was not before WO-1443
            // moved the frame off the full-cell rect.
            // A wider cell also un-truncates the names ("Siege Catap...", "Echo Legio..."), which was
            // a second symptom of the same cause and not a font problem.
            // ⭐ ONE NAME FOR THE LIST SHAPE, read by the width clamp, the height ceiling and the
            // row factory alike. `columns == 1` was being spelled out at each of those sites and
            // the width clamp was the one that did not get the exemption.
            bool asRowShape = columns == 1;
            float cellW = (bandW - (columns - 1) * TileGapPx) / columns;
            float cellH = (bandH - (rows - 1) * TileGapPx) / rows;
            // ⭐ THE CELL KEEPS THE MOCKUP'S SHAPE EVEN IN A VERY WIDE BAND (owner ruling
            // 2026-09-07 01:14: the panel now FILLS THE SCREEN, so this band is roughly half as
            // wide again as it was). ManageScreenPanel used to solve this by SHRINKING THE WHOLE
            // MODAL to 64% of the canvas until the band came out ~2:1; the owner retired that, and
            // the answer belongs here anyway - it is the GRID that has to be ~2:1, not the modal.
            // ⛔ CLAMPED, THEN CENTRED. A cell wider than MaxTileAspect x its height reads as a
            // BAR (round 4 measured 793x134, 5.9:1, and it was rejected then for the same reason).
            // The reclaimed width becomes an even margin either side - which is what the mockup
            // draws - rather than being poured into the tiles.
            // ⛔ THE WIDTH CLAMP IS FOR A GRID, NOT FOR THE LIST SHAPE. A single-column screen is
            // mockup panel 7's research tree, whose cell is a full-width ROW - icon, name, benefit,
            // a padlock line and a status column - and clamping THAT to 2.3x its height turns a
            // 1064px row into a 316px one and ellipsises every string in it.
            // MEASURED on Builds/cap-manage-wave4.log, the Cathedral tree:
            //   MANAGE_LIST_PAINTING ... side=580px in a 1835x580px well - the rows take x 0.42..1.0
            //   grid cell width clamped from 1064px to 316px (2.3:1 against a 137px cell)
            // and the frame (ManageFlow_RESEARCH_school_2670x1200.png) reads "Arcane Bas...",
            // "RESE...", "QUEU...". The rows were given the right band and then thrown 70% of it
            // away one line later. Same exemption the HEIGHT ceiling below already carries, and for
            // the same reason: columns == 1 is a different shape, not a narrow grid.
            float cellWCap = asRowShape ? float.MaxValue : cellH * MaxTileAspect;
            if (cellW > cellWCap)
            {
                FlowTrace.Step("Manage", "grid cell width clamped from " + cellW.ToString("0") +
                    "px to " + cellWCap.ToString("0") + "px (" + MaxTileAspect.ToString("0.##") +
                    ":1 against a " + cellH.ToString("0") + "px cell) - the band is " +
                    bandW.ToString("0") + "px for " + columns + " columns, so the row centres and " +
                    "the tiles keep the drawn shape instead of stretching into bars");
                cellW = cellWCap;
            }
            // ⭐ A ONE-ROW GRID MAY GROW TO SQUARE (WO-1567 panel row 7). MaxTileHeightPx(190)
            // exists to stop a WIDE cell from making one row taller than the whole band on a
            // multi-row grid; on a SINGLE row there is no second row to crowd out, and the cap was
            // the reason the research picker painted 190px of tile into a ~580px well with the
            // rest black (measured on the owner's device,
            // Logs/device/screens/owner-screen-20260907-005358.png).
            // ⛔ THE CEILING IS THE CELL'S OWN WIDTH, so a tile may become SQUARE and never taller
            // than it is wide - the mockup's shape. It is not "fill the band whatever the cost":
            // an unclamped cell in a tall band would produce portrait-shaped tiles the art was
            // never drawn for.
            // ⛔ AND ONLY ON A REAL GRID. columns == 1 is the LIST shape (mockup panel 7's research
            // tree, BuildListRow), where the cell is a full-width ROW and letting its height chase
            // its width would make one row as tall as the band is wide.
            // ⭐ WO-1567 ROUND 25 - THE CEILING IS THE CELL'S OWN WIDTH ON **EVERY** REAL GRID, NOT
            // ONLY ON A ONE-ROW ONE. THIS IS THE TILE-SIZE DEFECT, AND IT IS ARITHMETIC:
            // MaxTileHeightPx is 190, so a 5x2 BUILD grid in a 580px band painted 2 x 190 + 10 =
            // 390px of tiles and left 190px of the well black -
            // Builds/ui-capture/ManageFlow_BUILD_gridtop_2670x1200.png shows exactly that, the grid
            // sitting in the TOP HALF of an otherwise empty full-bleed panel, and ARMY the same.
            // ⛔ AND THE 190 CAP CANNOT BE THE ANSWER ON A MULTI-ROW GRID EITHER, because `cellH`
            // is ALREADY `(bandH - gaps) / rows` - the authored rows fit the band BY CONSTRUCTION.
            // A second, absolute ceiling on top of that can only ever make them smaller than the
            // band they were divided out of. It was written to stop a WIDE cell from making one row
            // taller than the whole band; the cell's own WIDTH does that job exactly and keeps the
            // mockup's shape - a tile is never taller than it is wide - at any band height.
            // ⛔ STILL NOT ON THE LIST SHAPE. columns == 1 is mockup panel 7's full-width ROW, where
            // letting the height chase the width would make one row as tall as the band is wide.
            float cellCeiling = asRowShape ? MaxTileHeightPx : Mathf.Max(MaxTileHeightPx, cellW);
            cellH = Mathf.Min(cellH, cellCeiling);
            float cell = cellH;   // the vertical governor: text bands and the touch floor read this
            if (cellW < 1f || cellH < 1f)
            {
                FlowTrace.Warn("Manage", "grid resolved to a " + cell.ToString("0") + "px cell for " +
                    columns + "x" + rows + " in a " + bandW.ToString("0") + "x" + bandH.ToString("0") +
                    "px band - the model's capacity request cannot be honoured and the tiles would be " +
                    "sub-pixel. Reporting rather than re-columning silently");
                return;
            }
            if (cell < MinTileHeightPx)
                FlowTrace.Warn("Manage", "grid cell is " + cell.ToString("0") + "px, under the " +
                    MinTileHeightPx + "px floor, because the band is " + bandH.ToString("0") +
                    "px and the mockup asks for " + rows + " rows (it needs " +
                    (rows * MinTileHeightPx + (rows - 1) * TileGapPx).ToString("0") + "px). The " +
                    "TILE is the screen; the well has to grow, and nothing here will shrink text to " +
                    "hide that");

            // ⛔ WHOLE ROWS ONLY, AND THE REMAINDER SAYS HOW MANY ARE HIDDEN.
            // MEASURED in Builds/ui-capture/ManageFlow_Troops_railtop_2670x1200.png (2026-09-06,
            // 14:59): the grid took the whole band, so ARMY's second row was sliced through the
            // middle - three portraits visible from the top down, their NAMES cut off entirely.
            // A half tile is not a scroll hint; it is a tile whose label has been deleted, which is
            // the same class of defect as a text band under the cull floor. And the same frame
            // shows BUILD offering FOUR tiles under a chip that says ALL, with ~24 authored - a
            // claim the screen does not honour, and the exact seam-oracle defect WO-1430 catalogued.
            //
            // So: the viewport is trimmed to a whole number of rows, and the reclaimed strip
            // carries an explicit count of what is off-screen. The grid still scrolls - this adds
            // the WORDS that were missing, it does not replace the gesture. Nothing is shrunk:
            // the strip is only drawn when it clears MinTextBandPx on its own.
            // ⚠ THE EPSILON IS LOAD-BEARING, NOT DEFENSIVE PADDING. `cell` is derived from `bandH`
            // by the very division this floor then inverts, so in exact arithmetic the ratio IS
            // `rows` - and in float it lands on 2.9999997 as readily as on 3.0000002. Without it,
            // an ARMY grid whose three rows fit the band by construction seats TWO of them and
            // reports the third as hidden, which then draws a "+3 MORE" strip over a band that had
            // the room all along. One ULP is not a rounding preference here; it is the difference
            // between the authored grid and a wrong one.
            const float RowFitEpsilon = 0.001f;
            int wholeRows = Mathf.Max(1, Mathf.FloorToInt(
                (bandH + TileGapPx) / (cell + TileGapPx) + RowFitEpsilon));
            float rowsPx = wholeRows * cell + (wholeRows - 1) * TileGapPx;
            float gridW = columns * cellW + (columns - 1) * TileGapPx;
            int shown = wholeRows * columns;
            int hidden = Mathf.Max(0, tiles.Count - shown);
            float leftoverPx = bandH - rowsPx - TileGapPx;
            bool overflowStrip = hidden > 0 && leftoverPx >= MinTextBandPx && bandH > 1f;
            // ⭐ WO-1567 ROUND 26 - THE VIEWPORT IS THE ROWS THAT EXIST, NOT THE ROWS THAT FIT.
            // ⛔ THIS IS WHY THE ROUND-25 CENTRING DID NOTHING. `viewportPx` fell back to the WHOLE
            // BAND whenever the overflow strip was not drawn, so for the research picker - ONE row
            // of five in a 758px band - the viewport was 758px, `bandH - viewportPx` was 0, and the
            // centring branch could never fire. The row then sat at the top of a full-height
            // viewport, which is exactly the frame: tiles at the top, dead well beneath. The band
            // was right and the surplus was real; the viewport was measuring the wrong thing.
            int contentRows = Mathf.Max(1, Mathf.CeilToInt(tiles.Count / (float)columns));
            int seatedRows = Mathf.Min(wholeRows, contentRows);
            float seatedPx = seatedRows * cell + (seatedRows - 1) * TileGapPx;
            float viewportPx = overflowStrip ? rowsPx : Mathf.Min(bandH, seatedPx);

            FlowTrace.Step("Manage", "MANAGE_GRID tiles=" + tiles.Count + " want=" + columns + "x" + rows +
                " cell=" + cell.ToString("0") + "px band=" + bandW.ToString("0") + "x" +
                bandH.ToString("0") + " rowsFit=" + wholeRows + " shown=" + shown +
                " hidden=" + hidden + " gridW=" + gridW.ToString("0") +
                // ⭐ THE FILL FRACTIONS, so "the tiles fill the band" is a measurement and not a
                // claim. fillH is the one that was wrong: 390/580 = 0.67 on BUILD before the
                // ceiling was re-derived from the cell's own width.
                // ⛔ WO-1490 - fillH IS SEATED PIXELS OVER THE BAND, NEVER `rowsPx`. `rowsPx` is
                // how many rows WOULD FIT, which on a grid with fewer rows of CONTENT than of
                // room is not a picture of anything. MEASURED on Builds/cap-manage-wave5c.log,
                // the research picker: `tiles=5 want=5x1 cell=359px band=1835x758 rowsFit=2 ...
                // fillH=0.96` - one 359px row of tiles in a 758px well, i.e. 0.47 of the band
                // painted, reported as 0.96 because TWO rows fit. That is the instrument
                // contradicting the frame (ManageFlow_RESEARCH_gridtop_2670x1200.png shows the
                // row centred with roughly half the well black), and it is the exact number
                // WO-1490's "45% dead band" is judged by. An instrument that reads 0.96 on a
                // 0.47 screen is worse than no instrument: it retires the ticket on paper.
                " fillW=" + (gridW / Mathf.Max(1f, bandW)).ToString("0.##") +
                " fillH=" + (seatedPx / Mathf.Max(1f, bandH)).ToString("0.##") +
                " rowsSeated=" + seatedRows);
            // ⭐ WO-1490 - THE DEAD BAND IS NAMED AS A NUMBER, EVERY TIME, ON EVERY GRID.
            // ⛔ IT IS A `Step`, NOT A `Warn`, AND THAT IS DELIBERATE. On the research picker the
            // surplus is FORCED: five square tiles share the band's width, so the cell can never
            // be taller than bandW/5 however the well grows, and ~370px of a 758px band is
            // unusable by construction (the centring above splits it above and below, which is
            // what mockup panel 6 draws). Warning on a geometry that cannot be otherwise would
            // train the next seat to ignore the line. What matters is that the number is IN the
            // log, so nobody has to infer the dead band from a PNG again.
            float deadBandF = 1f - (seatedPx / Mathf.Max(1f, bandH));
            if (deadBandF > 0.02f)
                FlowTrace.Step("Manage", "MANAGE_GRID_DEAD_BAND " + (deadBandF * 100f).ToString("0") +
                    "% of the " + bandH.ToString("0") + "px band carries no tile - " +
                    seatedRows + " row(s) of content at " + cell.ToString("0") + "px in a well " +
                    "that fits " + wholeRows + ". On a one-row grid this is FORCED (the cell may " +
                    "not be taller than the width the columns share) and the surplus is split " +
                    "above and below; on a multi-row grid it is a defect");
            // ⚠ TWO DIFFERENT THINGS, AND THE OLD MESSAGE CONFLATED THEM.
            // It called ANY off-screen tile a "WELL SHORTFALL", which made the BUILD screen report
            // a failure while it was doing exactly what the mockup asks: panel 2 draws TEN tiles,
            // the catalog holds 22, and the other 12 scroll. That is a scrolling grid, not a defect.
            // A shortfall is when the well cannot seat the AUTHORED capacity - shown < columns*rows.
            if (shown < columns * rows)
                FlowTrace.Warn("Manage", "grid seats only " + shown + " tiles where the mockup asks for " +
                    (columns * rows) + " (" + columns + "x" + rows + ") - a WELL SHORTFALL, not a " +
                    "layout preference. Band " + bandW.ToString("0") + "x" + bandH.ToString("0") + "px");
            else if (hidden > 0)
                FlowTrace.Step("Manage", "grid shows the authored " + shown + " of " + tiles.Count +
                    " tiles; " + hidden + " scroll" +
                    (overflowStrip ? " and the count is on screen in the overflow strip"
                                   : " (no room for the overflow strip)"));

            var scrollGo = new GameObject("ManageGridScroll", typeof(RectTransform), typeof(Image),
                typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(band, false);
            Track(scrollGo);
            // FULL BAND WIDTH, top-anchored, sized to WHOLE rows. The cell width is the band divided
            // by the authored columns, so the grid meets both edges by construction - there is no
            // centring step and no leftover margin to explain.
            // ⭐ WO-1567 ROUND 25 - A GRID THAT FITS IS CENTRED IN ITS BAND; A GRID THAT SCROLLS
            // STAYS TOP-ANCHORED.
            // ⛔ THE DEAD WELL UNDERNEATH IS THE DEFECT THIS CLOSES. The research picker is FIVE
            // tiles in ONE row, and a square tile can never be taller than the width five of them
            // have to share - so with the band 730px tall and the tile 359px square there is 371px
            // the grid cannot use however the cell is sized. MEASURED on
            // Builds/cap-manage-wave4.log: `RESEARCH: ... columns=5 rows=1 viewport=580px
            // content=359px`, and the owner's own frame reads "2x2 of short wide tiles with a dead
            // well beneath". Top-anchoring put every one of those spare pixels in ONE black slab
            // under the tiles; centring splits it above and below, which is what mockup panel 6
            // draws. Nothing is resized to hide the shortfall - the numbers are unchanged and the
            // MANAGE_GRID line still reports them.
            // ⛔ ONLY WHEN NOTHING IS HIDDEN. A scrolling grid must start at its FIRST row, and a
            // centred viewport would open it half a row down.
            bool centreInBand = hidden <= 0 && bandH - viewportPx > 1f;
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 1f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.pivot = new Vector2(0.5f, 1f);
            scrollRt.offsetMin = new Vector2(0f, 0f);
            scrollRt.offsetMax = new Vector2(0f, 0f);
            scrollRt.sizeDelta = new Vector2(0f, viewportPx);
            scrollRt.anchoredPosition = centreInBand
                ? new Vector2(0f, -(bandH - viewportPx) * 0.5f)
                : Vector2.zero;
            if (centreInBand)
                FlowTrace.Step("Manage", "MANAGE_GRID_CENTRED viewport " + viewportPx.ToString("0") +
                    "px in a " + bandH.ToString("0") + "px band - " +
                    (bandH - viewportPx).ToString("0") + "px of surplus split above and below " +
                    "instead of left as a dead slab under the tiles");
            var backing = scrollGo.GetComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 0.001f);   // raycast surface for the drag, visually inert

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = new Vector2(0f, 0f);
            contentRt.offsetMax = new Vector2(0f, 0f);

            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellW, cellH);
            grid.spacing = new Vector2(TileGapPx, TileGapPx);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            // ⭐ CENTRED, NOT LEFT-PACKED. With the cell width clamped to MaxTileAspect a full-screen
            // band has surplus width, and UpperLeft would pile every tile against the left edge and
            // leave one ragged black column on the right - which is how the research picker read on
            // the owner's device. Centring turns the surplus into the mockup's even side margins.
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            // ⭐ ONE COLUMN MEANS A LIST, NOT A COLUMN OF CARDS. Mockup panel 7 (the research tree)
            // is a vertical list of upgrade ROWS - icon, name, one-line effect, and the state on the
            // right - not a stack of square cards with names under them. The MODEL says which by
            // asking for a single column (ManageScreenVM sets GridColumns 1 for
            // ManageScreenKind.ResearchPerks); the View never infers a layout from an id or a tab.
            bool asRows = asRowShape;
            // ⭐ WO-1563 FOLLOW-UP - THE STATE WORD IS SIZED FOR THE LONGEST WORD ON THIS GRID.
            // MEASURED in Builds/ui-capture/ManageFlow_BUILD_gridtop_2670x1200.png: Ballista,
            // Wooden Palisade, Crystal Mine and Cathedral of Magic all read "QUEUE FU..." - the
            // word painted, then TMP ellipsised it. An ellipsis on a STATE word is the same defect
            // class as no word at all: "QUEUE FU..." and "QUEUE FULL" are the same to a reader, but
            // "UPGRADE AVAILABLE" and "UPGRADING" both truncate to "UPGRADI...".
            // ⛔ The longest word is taken from THE MODEL'S OWN TILES, never from a vocabulary list
            // copied into this View - a copy would be duplicated state and would go stale the first
            // time a composer authors a new word (the failure this repo keeps paying for).
            // Every tile then paints at the SAME size, which is also why the grid reads as one row
            // of peers instead of nine independently-fitted labels.
            // ⭐ MEASURED OVER THE CLOSED WORD (WO-1567 panel row 2), which is what a tile paints.
            // Measuring StateText here while the tile painted StateWord would size every label for
            // a string no tile shows - the grid would shrink to fit "SHORT 280 STONE, 720 GOLD"
            // and paint "SHORT" at that size.
            string widestState = "";
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (t == null) continue;
                string w = TileStateWord(t);
                if (string.IsNullOrEmpty(w)) continue;
                if (w.Length > widestState.Length) widestState = w;
            }
            float stateFontPx = ResolveStateWordFont(widestState, cellW);
            for (int i = 0; i < tiles.Count; i++)
            {
                if (asRows) BuildListRow(contentRt, tiles[i], cellH, cellW);
                else BuildTile(contentRt, tiles[i], cellH, cellW, stateFontPx);
            }

            // The honest overflow line. It exists ONLY while the well is too short to seat the
            // capacity the mockup asks for; once the well grows it never renders, which is the
            // point - a filter that says ALL must either show all or say what it is holding back.
            if (overflowStrip)
            {
                float y1 = Mathf.Clamp01((leftoverPx - TileGapPx) / bandH);
                // ⭐ WO-1491 - AN AFFORDANCE, NOT AN INSTRUCTION. It read "12 MORE - SCROLL", which
                // is a sentence telling the player how to use a touchscreen; the count is the only
                // part that carries information. "+12 MORE" reads as a badge on the edge of the
                // grid, which is what the strip is.
                var more = ElarionUiKit.Label(band, "+" + hidden + " MORE", 0f, y1,
                    ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Center,
                    0.05f, 0.95f);
                ElarionUiKit.FitSingleLine(more, 20f, 28f);
            }
        }

        /// <summary>
        /// ⭐ ONE LIST ROW - mockup panel 7, the research tree. Icon, name, one-line effect, and the
        /// STATE on the right: a tick word for done, the state word for available, a padlock and the
        /// requirement for locked.
        ///
        /// <para>⛔ THE THREE STATES ARE THE DETAIL CARD'S OWN VOCABULARY, NOT A SECOND ONE.
        /// The medallion comes from <c>StateIconKey</c> (ManageArt.StatusFor) and the word from
        /// <c>StateText</c> - the same two channels the tiles and the detail card already speak, so
        /// "Researched" / "RESEARCH" / "Requires Cathedral Tier 3" read identically wherever the
        /// player meets them. Inventing a parallel set of research words here is exactly the
        /// duplicated state this file keeps paying for.</para>
        ///
        /// <para>⚠ WHAT THIS ROW CANNOT DO YET, AND IT IS A CONTRACT GAP, NOT A LAYOUT CHOICE:
        /// the mockup puts a GOLD "RESEARCH" BUTTON WITH ITS COST inside the row. <see cref="ManageTileVM"/>
        /// carries no action and no cost - only <c>Activate</c>, which selects. So the row IS the
        /// tap target and it opens the detail card, where the CTA and the cost basket already live
        /// and are already correct. That is honest and it works; it is not the drawing.
        /// Closing it needs a per-row action + cost on ManageTileVM, in ManageViewContract.cs -
        /// the file the panel-8 lane is editing. FLAGGED FOR SEQUENCING rather than edited, so two
        /// lanes do not write the same contract in the same hour.</para>
        /// </summary>
        /// <param name="rowW">The row's own width in px, handed down from the band the grid
        /// resolved. ⛔ PASSED, NOT READ BACK: `parent.rect.width` is 0 on the frame these rows are
        /// built (no layout pass has run), and a zero here would size the icon zone against a rect
        /// that does not exist - the identical trap the drawer's 719px-vs-475px estimate fell into.
        /// It exists so the ICON ZONE CAN BE SQUARE: the medallion art is square, and a square
        /// sprite painted preserveAspect-false into a 94x140 zone is a stretched medallion.</param>
        private void BuildListRow(RectTransform parent, ManageTileVM tile, float rowH, float rowW)
        {
            if (tile == null) return;

            var rowGo = new GameObject("ManageListRow", typeof(RectTransform), typeof(Image), typeof(Button));
            rowGo.transform.SetParent(parent, false);
            var row = rowGo.GetComponent<RectTransform>();

            var plate = rowGo.GetComponent<Image>();
            plate.color = new Color(0.05f, 0.04f, 0.03f, 0.72f);

            var press = rowGo.GetComponent<Button>();
            press.transition = Selectable.Transition.None;
            press.onClick.AddListener(MakeUnityInvoker(tile.Activate));

            // Selected reads as a GOLD BORDER, the same cue the grid tiles use (3.0b) - shape, not
            // hue alone, because the owner is red/green colourblind.
            if (tile.IsSelected) ElarionUiKit.GoldPerimeter(row);

            // ICON, left, square inside the row's height.
            // ⭐ WO-1567 PANEL ROW 8 - THE BAKED CAPTION IS CROPPED OUT.
            // The perk cards carry the perk's NAME painted into their bottom third, and this row
            // typesets that name two columns to the right; the owner's capture shows the baked
            // words leaking out from under every medallion. ManageArt owns both the test and the
            // rect (IsCaptionedPerkIcon / PerkIconU0..V1) - the View asks, it does not decide.
            // ⭐ SQUARE, DERIVED FROM THE ROW'S OWN RECT. The zone is 0.80 of the row's HEIGHT, so
            // its width fraction is that many px over the row's WIDTH. At the research tree's
            // 1064x175 row that is 140x140 instead of the 94x140 a typed 0.10 gave, and the
            // medallion stops being stretched. Clamped so a very wide row cannot make the icon a
            // hairline and a very narrow one cannot let it eat the name column.
            const float RowIconHeightF = 0.80f;
            float iconX1 = 0.012f + Mathf.Clamp(
                (rowH * RowIconHeightF) / Mathf.Max(1f, rowW), 0.04f, 0.22f);
            var iconZone = Zone(row, "RowIcon", new Vector2(0.012f, 0.10f), new Vector2(iconX1, 0.90f));
            // Every text column on this row starts CLEAR of whatever width the icon just took.
            // It was a typed 0.12 against a typed 0.10 icon; once the icon is derived, a typed text
            // origin is a collision waiting for the first row shape that widens it.
            float textX0 = iconX1 + 0.02f;
            if (ManageArt.IsCaptionedPerkIcon(tile.PortraitKey))
                CroppedIcon(iconZone, tile.PortraitKey,
                    ManageArt.PerkIconU0, ManageArt.PerkIconU1,
                    ManageArt.PerkIconV0, ManageArt.PerkIconV1);
            else
                ElarionUiKit.Portrait(iconZone, ManageArt.LoadSprite(tile.PortraitKey), active: false);

            // ⭐ TWO FACTS, TWO ROWS - the mockup's panel 7 shape. A LOCKED row grows a third text
            // line carrying the requirement beside a padlock; every other row keeps two.
            // ⛔ THE BANDS SHIFT, THE ROW DOES NOT GROW. Growing the row would re-open the
            // whole-row seating arithmetic the grid above already resolved, and the third line is
            // only present on the rows that need it.
            bool hasRequirement = !string.IsNullOrEmpty(tile.RequirementText);

            // NAME on top, EFFECT under it. Both bands are stated in px against the row height so
            // neither can fall under the MinTextBandPx cull floor without saying so.
            // ⭐ WO-1567 ROUND 26 - THE EFFECT BAND IS TWO LINES DEEP. It was one, and the frame
            // shows what that cost: "Unlocks the Healing Fountain - restores the Hear..." -
            // FitSingleLine ellipsised the only sentence that says what the perk DOES. The band
            // grows downward into the row's own padding (0.08 / 0.33 instead of 0.10 / 0.35) and
            // the fit below is FitBlock, which WRAPS and truncates rather than ellipsising.
            float nameY0 = hasRequirement ? 0.63f : 0.50f, nameY1 = hasRequirement ? 0.97f : 0.92f;
            float effectY0 = hasRequirement ? 0.33f : 0.08f, effectY1 = hasRequirement ? 0.61f : 0.48f;
            float namePx = rowH * (nameY1 - nameY0), effectPx = rowH * (effectY1 - effectY0);
            float reqPx = hasRequirement ? rowH * 0.28f : 0f;
            // ⭐ THE EFFECT BAND IS JUDGED AGAINST **TWO** LINES (WO-1567 round 26), because that is
            // what it now typesets. Judging a two-line band against the one-line cull floor is how a
            // band that wraps to one-and-a-bit lines passes the check and truncates on screen.
            if (namePx < MinTextBandPx || effectPx < 2f * ElarionUiKit.FontHardFloor ||
                (hasRequirement && reqPx < MinTextBandPx))
                FlowTrace.Warn("Manage", "list row is " + rowH.ToString("0") + "px - its name band (" +
                    namePx.ToString("0") + "px), effect band (" + effectPx.ToString("0") +
                    "px) or requirement band (" + reqPx.ToString("0") + "px) is under the " +
                    MinTextBandPx + "px TMP cull floor and would render BLANK");

            var name = ElarionUiKit.Label(row, tile.Title ?? string.Empty, nameY0, nameY1,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Left,
                textX0, 0.60f, bold: true);
            ElarionUiKit.FitSingleLine(name, 22f, 30f);

            var effect = ElarionUiKit.Label(row, tile.Subtitle ?? string.Empty, effectY0, effectY1,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.TopLeft,
                textX0, 0.60f);
            // ⭐ WO-1567 ROUND 26 - FitBlock, NEVER FitSingleLine. MEASURED on the round-25 frame:
            // "Unlocks the Healing Fountain - restores the Hear..." - a single line ellipsised on
            // the perk whose benefit is the longest, which is exactly the perk a player most needs
            // the sentence for. FitBlock WRAPS and, when a sentence genuinely will not fit,
            // TRUNCATES visibly at the end of a line instead of substituting three dots that look
            // deliberate (the same choice the hub card's description made, for the same reason).
            // ⚠ THE FLOOR IS ElarionUiKit.FontHardFloor (20), NOT the 30 kit FontFloor, and that is
            // stated rather than hidden: two lines must fit the band this row can give - 0.28 of a
            // 175px tree row is ~49px on a locked row - and FitBlock clamps anything under 20 back
            // up, so 20 is the real bottom of the range either way. It is above MinTextBandPx (28)
            // per LINE only because the band holds two of them; a shorter row warns above.
            ElarionUiKit.FitBlock(effect, ElarionUiKit.FontHardFloor, 26f);

            if (hasRequirement)
            {
                // ⭐ THE PADLOCK ROW. Mockup panel 7 draws a padlock and "Requires Cathedral Tier 3"
                // on their own line; the device build glued that sentence onto the benefit with a
                // " . " and then ellipsised it away (ManageViewContract.ManageTileVM.RequirementText
                // records the exact captured string).
                // ⛔ SHAPE, NOT HUE. The padlock is a second, non-colour channel for "locked" - the
                // owner is red/green colourblind, so the word and the glyph both have to carry it.
                PaintSprite(row, "RowRequirementLock", new Vector2(textX0 - 0.005f, 0.04f),
                    new Vector2(textX0 + 0.035f, 0.30f), ManageArt.IconPadlock);
                var need = ElarionUiKit.Label(row, tile.RequirementText, 0.04f, 0.30f,
                    ElarionUi.Gold, ElarionUi.FontLabel, TextAlignmentOptions.Left,
                    textX0 + 0.045f, 0.60f);
                ElarionUiKit.FitSingleLine(need, 18f, 26f);
                // ⚠ THE WO-1518 DOOR AFFORDANCE IS NOT REPEATED HERE. The word that tells the
                // player the row is tappable ("LOCKED - TAP") is composed model-side onto StateText
                // and painted in the state column below, derived from whether a route actually
                // exists. Restating it beside the padlock would be a second copy that goes stale
                // the first time a locked row has no door.
            }

            // ⭐ THE INLINE ACTION, mockup panel 7's gold RESEARCH button with its price beneath.
            // Present ONLY on an AVAILABLE row (ManageVmProjection.ProjectRowAction): a locked row
            // shows the padlock and the requirement instead, which is what the mockup draws, and a
            // researched one needs no control at all.
            var rowAction = tile.RowAction;
            bool hasRowAction = rowAction != null && rowAction.Visible && !string.IsNullOrEmpty(rowAction.Label);
            float stateX0 = hasRowAction ? 0.62f : 0.63f;
            float stateX1 = hasRowAction ? 0.74f : 0.985f;

            if (hasRowAction)
            {
                bool hasCost = !string.IsNullOrEmpty(rowAction.CostText);
                // The button owns the row's full height when there is no price under it, and the
                // upper two thirds when there is - so the cost line always has a real band rather
                // than being squeezed under a control (a sentence with nowhere to sit is a sentence
                // TMP culls).
                var cta = ElarionUiKit.Button(row, rowAction.Label,
                    rowAction.Enabled ? ElarionUiKit.ButtonKind.Gold : ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(0.76f, hasCost ? 0.38f : 0.10f), new Vector2(0.985f, 0.90f),
                    MakeInvoker(rowAction.Activate));
                Track(cta);
                if (cta != null) cta.gameObject.name = "ManageRowAction";
                if (hasCost)
                {
                    var price = ElarionUiKit.Label(row, rowAction.CostText, 0.06f, 0.34f,
                        ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Center,
                        0.76f, 0.985f);
                    ElarionUiKit.FitSingleLine(price, 16f, 24f);
                }
            }

            // STATE: the medallion and the model's own word, side by side. It NARROWS when the row
            // carries an inline action so the two never overprint - the same fact told once.
            PaintSprite(row, "RowStatus", new Vector2(stateX0, 0.22f),
                new Vector2(stateX0 + 0.07f, 0.78f), tile.StateIconKey);
            var state = ElarionUiKit.Label(row, tile.StateText ?? string.Empty, 0.22f, 0.78f,
                tile.IsSelected ? ElarionUi.Gold : ElarionUi.Parchment,
                ElarionUi.FontLabel, TextAlignmentOptions.Left, stateX0 + 0.08f, stateX1);
            ElarionUiKit.FitSingleLine(state, 18f, 26f);

            // A running row keeps its bar, same as a tile.
            if (tile.Progress01.HasValue)
                ProgressBar(row, tile.Progress01.Value, 0.02f, 0.07f, textX0, 0.60f);

            ElarionUiKit.ClampMinTouch(press);
        }

        /// <summary>
        /// One tile. The layer stack is fixed - plate, state frame, selection glow, PORTRAIT,
        /// selection bar, medallion - so a FRAME SWAP IS A SPRITE SWAP even though the delivered
        /// frames are not a consistent set (see ManageArt's header: two opaque centres, two
        /// hollow, and frame-selected's glow bleeds outside its rect). The plate is always
        /// painted so the two hollow frames have something behind them; frame-selected lives on
        /// its OWN, LARGER rect so its bleed does not have to 9-slice against the others.
        ///
        /// <para>⛔ THE FRAMES ARE PAINTED **UNDER** THE PORTRAIT, AND THAT ORDER IS THE FIX FOR
        /// WO-1443 section 2 - DO NOT PUT THEM BACK ON TOP. Until 2026-09-06 the frame was a LAYER-3
        /// overlay above the portrait, which is correct only if every frame's centre is hollow.
        /// MEASURED that day from the delivered PNGs in Assets/Resources/RpgUi/manage/ (System.
        /// Drawing pixel sample, not read off a comment):
        ///   frame-tile      centre alpha 253/255, and 253 across the whole portrait zone up to
        ///                   v=0.75 (transparent only above v~0.95)
        ///   frame-selected  centre alpha 253/255
        ///   frame-locked    centre alpha 0
        ///   frame-max       centre alpha 0
        /// So the two OPAQUE members painted a near-black plate over the portrait and the tile read
        /// as an EMPTY FRAME. That is exactly what the owner captured: Footman and Archer
        /// (unlockBarracksTier 1 -> unlocked -> FrameTile -> covered) rendered blank while Spearman
        /// (tier 2 -> Locked -> FrameLocked, hollow) showed its art. Nothing was missing and nothing
        /// failed to load - which is why ManageArt logged no art-miss line for any troop.
        /// The same defect hit every OWNED building tile on the BUILD tab; one order fixes both.</para>
        /// </summary>
        /// <summary>
        /// The one font size every state word on this grid is painted at: the largest that seats
        /// <paramref name="widest"/> inside the state plate without an ellipsis.
        ///
        /// <para>⛔ MEASURED WITH TMP, NOT ESTIMATED. <c>GetPreferredValues</c> returns the width
        /// this exact font, weight and string would occupy at the label's CURRENT size, so the
        /// scale-down is arithmetic rather than a guessed character-advance ratio - and it needs no
        /// layout pass, which is what makes it usable at build time and in a headless capture.
        /// A ratio would have been a guess, and CLAUDE.md section 12 forbids shipping one.</para>
        ///
        /// <para>Returns the ceiling; the caller still passes it through FitSingleLine, so a word
        /// SHORTER than the widest is never blown up past it.</para>
        /// </summary>
        private float ResolveStateWordFont(string widest, float cellW)
        {
            const float Ceiling = 26f;                 // the band's authored maximum
            if (string.IsNullOrEmpty(widest) || cellW <= 1f) return Ceiling;

            // The label's own usable width, matching the rect BuildTile gives it exactly.
            float availablePx = ((TileStateX1 - 0.01f) - (TileStateX0 + 0.01f)) * cellW;
            if (availablePx <= 1f) return Ceiling;

            var probe = new GameObject("StateWordProbe", typeof(RectTransform), typeof(TextMeshProUGUI));
            try
            {
                var text = probe.GetComponent<TextMeshProUGUI>();
                // The kit's own resolver, so the probe measures the SHIPPED face - measuring a
                // different font would produce a confidently wrong number.
                var face = ElarionUiKit.ResolveDefaultFont();
                if (face != null) text.font = face;
                text.fontSize = Ceiling;
                text.fontStyle = FontStyles.Bold;
                text.enableAutoSizing = false;
                float wantPx = text.GetPreferredValues(widest, 0f, 0f).x;
                if (wantPx <= 1f) return Ceiling;
                if (wantPx <= availablePx) return Ceiling;      // already fits, nothing to do

                float scaled = Ceiling * (availablePx / wantPx);
                if (scaled < ElarionUiKit.FontHardFloor)
                {
                    // ⛔ REPORTED, NEVER ELLIPSISED. Below the hard floor TMP culls the line
                    // outright (the "bare plate" class), so shrinking further would delete the word
                    // rather than fit it. The honest outcome is the floor plus a named shortfall.
                    FlowTrace.Warn("Manage", "the state word '" + widest + "' needs " +
                        wantPx.ToString("0") + "px at " + Ceiling.ToString("0") + "px type but the tile " +
                        "plate offers " + availablePx.ToString("0") + "px, so it would have to drop to " +
                        scaled.ToString("0.#") + "px - under the " + ElarionUiKit.FontHardFloor.ToString("0") +
                        "px floor. Painting at the floor: the TILE must get wider, and nothing here " +
                        "will ellipsise a state word to hide that");
                    return ElarionUiKit.FontHardFloor;
                }
                FlowTrace.Step("Manage", "state word type resolved to " + scaled.ToString("0.#") +
                    "px for the widest word on this grid ('" + widest + "', " + wantPx.ToString("0") +
                    "px at " + Ceiling.ToString("0") + "px in a " + availablePx.ToString("0") + "px plate)");
                return scaled;
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Manage", "could not measure the state word '" + widest + "' (" +
                    ex.GetType().Name + ") - falling back to the authored ceiling, which may ellipsise");
                return Ceiling;
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(probe);
                else UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// ⭐ THE WORD A GRID CELL PAINTS - <c>tile.StateWord</c>, falling back to
        /// <c>tile.StateText</c> when the composer authored no shorter face (WO-1567 panel row 2).
        ///
        /// <para>⛔ THIS IS A CHOICE BETWEEN TWO MODEL-SUPPLIED STRINGS AND NOTHING MORE. It does
        /// not truncate, split, uppercase or re-word either of them - doing any of that would be
        /// the View deriving a label, which canon 9 bans and ManageDumbViewRegression scans for.
        /// The reason the two faces exist at all is WO-1518: the owner asked for the amounts
        /// ("short doesnt help, i need to know waht im short"), and the amounts fit the research
        /// row's state column and the detail card but NOT a grid cell, where they measured as
        /// "SHORT 28..." on her own device (owner-screen-20260907-004825.png).</para>
        /// </summary>
        private static string TileStateWord(ManageTileVM tile)
        {
            if (tile == null) return null;
            return string.IsNullOrEmpty(tile.StateWord) ? tile.StateText : tile.StateWord;
        }

        /// <param name="cellW">The cell's width in px, handed down from the band the grid resolved.
        /// ⛔ PASSED, NOT READ BACK - the cell rect is 0 on the frame the tile is built. It exists
        /// so the portrait's fit mode is decided by ARITHMETIC (see SquarePortrait) instead of by a
        /// guess about the cell's shape.</param>
        private void BuildTile(RectTransform parent, ManageTileVM tile, float cellH, float cellW,
                               float stateFontPx = 26f)
        {
            if (tile == null) return;

            var cellGo = new GameObject("ManageTile", typeof(RectTransform), typeof(Image), typeof(Button));
            cellGo.transform.SetParent(parent, false);
            var cell = cellGo.GetComponent<RectTransform>();

            // LAYER 1 - the plate. Always present, opaque enough to back a hollow frame.
            var plate = cellGo.GetComponent<Image>();
            plate.color = new Color(0.05f, 0.04f, 0.03f, 0.72f);

            // The whole cell is the tap target: cellH >= MinTileHeightPx(150) >= MinTouchPx(112),
            // margin +38px at the floor. Authored, never clamped.
            var press = cellGo.GetComponent<Button>();
            press.transition = Selectable.Transition.None;
            press.onClick.AddListener(MakeUnityInvoker(tile.Activate));

            // LAYER 2 - the state frame, preserve-aspect. NOT sliced (ManageArt header), and NOT
            // above the portrait: two of the four delivered frames have an opaque centre, measured
            // (see this method's summary). Under the portrait an opaque frame reads as the tile's
            // backing art and a hollow one still shows its ring, so ONE order is right for all four.
            //
            // ⛔ AND IT IS PAINTED OVER THE **PORTRAIT ZONE**, NOT Vector2.zero..one. THAT IS THE
            // FIX FOR THE NAMES SITTING ON THE FRAME - do not widen it back to the cell.
            // MEASURED in Builds/ui-capture/ManageFlow_Troops_railtop and _Buildings_railtop
            // (2026-09-06, 14:59): "Footman" / "Archer" / "Spearman" and "Archer Tower" / "Ballista"
            // rendered ON the frame's lower border instead of below it, with the state word cramped
            // under them. The cause is geometry, not the text bands. The frames are SQUARE 512px
            // art drawn with preserveAspect, and a grid cell here is about 536x190 - so a
            // full-cell frame collapses to a cellH-sided square (190px) CENTRED IN THE CELL, i.e.
            // spanning y 0..1 and straight through the title band (0.26-0.48) and the state band
            // (0.05-0.25). It was never able to act as a tile border on a wide cell; it is a
            // portrait medallion frame, and this is the rect it was drawn for. The tile's own
            // border is LAYER 1's plate, which does fill the cell.
            //
            // ⚠ 2026-09-07, WO-1567 panel row 2: THE PORTRAIT NOW COVERS THIS RECT COMPLETELY.
            // Layer 4 spans x 0..1 / y TilePortY0..1 so the art meets the cell edges the way the
            // mockup draws it, which means an UNDER-painted frame contributes nothing but a draw
            // call. It is kept, at its own rect, ONLY for the two frames whose centre is alpha 0
            // (frame-locked and frame-max, measured in this method's summary) and those are painted
            // OVER the art instead - see layer 4a. Painting the two OPAQUE members over the art is
            // what made every owned tile read as an empty frame in the first place, so the split is
            // by MEASURED alpha, never by state name.
            bool hollowFrame = tile.VisualState == ManageTileVisualState.Locked ||
                               tile.VisualState == ManageTileVisualState.Max;
            float frameX0 = TilePortX0;
            float frameX1 = TilePortX1;
            if (tile.ContainPortrait)
            {
                // Army cells are wide while their authored frames are square. Resolve one square
                // seat and give BOTH the frame and portrait that same clipped rect.
                float seatWidth = Mathf.Min(TilePortX1 - TilePortX0,
                    ((TilePortY1 - TilePortY0) * cellH) / Mathf.Max(1f, cellW));
                frameX0 = 0.5f - seatWidth * 0.5f;
                frameX1 = 0.5f + seatWidth * 0.5f;
            }
            if (!hollowFrame)
                PaintSprite(cell, "TileFrame", new Vector2(frameX0, TilePortY0),
                    new Vector2(frameX1, TilePortY1), tile.FrameKey);

            // LAYER 3 - selection GLOW, also under the portrait (frame-selected's centre is opaque
            // at alpha 253, so on top it blanked the portrait of whichever tile was selected).
            // ⛔ RETIRED 2026-09-07 (WO-1567 panel row 2), AND DELETED RATHER THAN LEFT DEAD.
            // This painted frame-selected under the portrait on a grown rect. With layer 4 now
            // covering the cell edge to edge, an UNDER-painted glow is invisible on every frame -
            // and frame-selected's centre is opaque (alpha 253, measured), so it cannot move on
            // top without blanking the art of whichever tile is selected. That leaves nothing it
            // can do. A layer that draws and is never seen is the "dead code that looks like a
            // shipped feature" defect this file already names once.
            // ⚠ NOTHING IS LOST: selection is LAYER 5's gold perimeter, which is what mockup
            // panels 2, 4 and 6 actually draw, and which reads in greyscale.

            // ⭐ LAYER 4 - THE PORTRAIT, SQUARE AND EDGE TO EDGE (mockup panels 2 and 4).
            // ⛔ THE MEDALLION RING IS RETIRED ON GRID TILES. It was ElarionUiKit.Portrait, whose
            // circular disc + gilt ring is the right shape for a hero/combat frame and the wrong
            // one here: the owner's captures (owner-screen-20260907-004825.png BUILD,
            // -005136.png ARMY) show a small round medallion floating in a black plate on every
            // tile, where her mockup draws a square of art reaching the cell edges under a name
            // strip. See SquarePortrait for why cropping beats letterboxing and why the ring's
            // preserveAspect inset was making the art small twice over.
            // ⛔ THE ZONE NOW SPANS THE CELL'S FULL WIDTH. TilePort* insets survive only as the
            // FRAME's rect (layers 2 and 3): a frame is drawn art with its own margins, and the
            // picture is not.
            // ⭐ WO-1567 ROUND 26 - THE ZONE IS THE WHOLE CELL, AND THE TEXT BANDS RIDE OVER IT.
            // ⛔ IT RAN y TilePortY0..1 (0.26..1), which on a now-SQUARE 359px BUILD cell is a
            // 359x266 zone - and a square sprite envelope-fitted into it is scaled to 359x359 and
            // cropped by 93px, split top and bottom. That is the measured defect: Archer Tower,
            // Ballista and Cathedral of Magic all lose their ROOFS. The zone was reserving the
            // bottom quarter for a name strip that already carries its OWN dark plate and is
            // painted LATER (see the name strip below and LAYER 7's state plate) - so it can sit
            // ON the art, which is what mockup panel 2 draws.
            Vector2 portraitMin = tile.ContainPortrait
                ? new Vector2(frameX0, TilePortY0) : Vector2.zero;
            Vector2 portraitMax = tile.ContainPortrait
                ? new Vector2(frameX1, TilePortY1) : Vector2.one;
            var portZone = Zone(cell, "TilePortrait", portraitMin, portraitMax);
            // Locked tiles are DIMMED - panel 4 draws them darker than the unlocked ones. It is a
            // luminance multiply, never a hue change (the owner is red/green colourblind), and it
            // is never the only cue: the tile still carries the word LOCKED and the padlock.
            // ⭐ AND IT IS FITTED BY WIDTH, ANCHORED TO THE BOTTOM, WHENEVER THE WHOLE SUBJECT
            // THEN FITS - the zone's px are handed in so that decision is arithmetic and needs no
            // layout pass. On a square BUILD cell a square building fills the tile with NOTHING
            // cropped; when the cell is wide (ARMY's 2.3:1) the width fit would be 566px tall in a
            // 246px cell and bottom-anchoring would cut the troops' HEADS, so it falls back to the
            // envelope crop that already reads correctly there. See SquarePortrait.
            SquarePortrait(portZone, tile.PortraitKey,
                tile.VisualState == ManageTileVisualState.Locked,
                (portraitMax.x - portraitMin.x) * cellW,
                (portraitMax.y - portraitMin.y) * cellH);

            // LAYER 4a - the HOLLOW state frames, over the art. frame-locked and frame-max are the
            // only two of the four with an alpha-0 centre (measured, see this method's summary), so
            // they are the only two that can ride on top without blanking the portrait. They are
            // the two whose state most needs a silhouette, which is convenient rather than lucky.
            if (hollowFrame)
                PaintSprite(cell, "TileFrame", new Vector2(frameX0, TilePortY0),
                    new Vector2(frameX1, TilePortY1), tile.FrameKey);

            // LAYER 5 - SELECTION IS A GOLD BORDER AROUND THE WHOLE TILE.
            // CAPTURE_LOOP_GOAL 3.0b: "Selected tile carries a gold border", and the mockup draws it
            // that way on screens 2, 4 and 6. It replaces the old left-edge bar, which was a shape
            // cue invented before the picture existed. A border is still SHAPE, not hue alone: it
            // changes the tile's silhouette and reads in greyscale.
            if (tile.IsSelected) ElarionUiKit.GoldPerimeter(cell);

            // LAYER 6 - the status medallion (canon 8: mandatory, model-supplied).
            PaintSprite(cell, "TileStatus", new Vector2(TileMedX0, TileMedY0),
                new Vector2(TileMedX1, TileMedY1), tile.StateIconKey);

            // ⭐ LAYER 7 - WO-1563: THE STATE, IN WORDS. THE ACCESSIBILITY ONE.
            //
            // This method used to reference tile.StateText EXACTLY ZERO times while the model was
            // composing it on every tile (ManageVmProjection sets StateText = item.BadgeText), and
            // the sibling renderer BuildListRow painted it. Same screen family, two opposite
            // answers to "what can I act on?": the RESEARCH rows read RESEARCHED / QUEUE FULL /
            // RESEARCHING in words while the BUILD and ARMY grids said nothing.
            //
            // ⛔ WHY WORDS AND NOT A BETTER GLYPH. The owner is red/green colourblind (memory
            // owner-colorblind-delegate-visual-creative). With no word the grid's ONLY state
            // channel is the medallion, and ManageArt.StatusFor collapses five distinct states
            // onto five small badges that differ partly by a red dot. WO-1516 then correctly
            // WITHHELD the medallion for the Available catch-all (a green up-arrow that meant
            // nothing) - which left those tiles carrying NEITHER glyph NOR text. Removing a
            // meaningless signal without adding a meaningful one leaves the tile mute; this is the
            // meaningful one.
            //
            // ⛔ THE VIEW DOES NOT DERIVE, MAP OR INFER THE STATE. It paints the string the model
            // supplies, exactly as BuildListRow does. There is no switch on VisualState here and
            // there must never be one (canon 9 / the WO-2002 oracle).
            //
            // GEOMETRY, AND WHY IT COSTS NO EXISTING BAND (WO-1563 acceptance 4):
            //   * It takes the medallion's OWN band (TileMedY0..TileMedY1) on the LEFT, mirroring
            //     the medallion on the right - so the two state channels sit on one line and
            //     neither can overprint the other (x ends at TileMedX0).
            //   * That band is 0.35 of the cell = 42px at the MinTileHeightPx(120) floor, clear of
            //     the MinTextBandPx(28) TMP cull threshold. ⛔ The NAME band (0.02-0.26) and the
            //     progress bar are UNTOUCHED - ManageScreenPanel's own "never re-shrink a text
            //     band below ~24px" note is honoured by taking nothing from them.
            //   * The plate is drawn ONLY when there is a word. A plate with no word is the "bare
            //     plate" defect class this file already pays for; an empty state paints nothing.
            //
            // ⛔ NOT COLOUR. The channel is a WORD on a dark plate in the tile's corner - a shape
            // and a string, legible in greyscale, on the same precedent as LAYER 5's gold BORDER.
            // ⭐ AND IT IS THE CLOSED WORD, NOT THE SENTENCE (WO-1567 panel row 2). See
            // TileStateWord: the grid cell paints StateWord, the research LIST ROW and the DETAIL
            // card keep WO-1518's amounts, and the MODEL composes both faces.
            // ⛔ WRITTEN OUT INLINE, NOT CALLED THROUGH TileStateWord, AND ON PURPOSE.
            // ManageProgressiveDisclosureRegression's [grid-tile-states-its-state] case reads THIS
            // METHOD'S BODY and fails if the token `tile.StateText` is absent from it - that oracle
            // exists because BuildTile once referenced the state word ZERO times while the model
            // composed it. Hiding the reference behind a helper would make the oracle go red on
            // working code, which is the worst of both outcomes. The helper is still the one place
            // the RULE is written down, and the grid-wide measurement above calls it.
            string tileState = string.IsNullOrEmpty(tile.StateWord) ? tile.StateText : tile.StateWord;
            if (!string.IsNullOrEmpty(tileState))
            {
                // A contained Army portrait owns the square in the middle of its wide card. Keep
                // the word pill entirely to its left so MAX / QUEUE FULL / LOCKED never print over
                // the framed face. BUILD/category cards keep the wider authored pill.
                float stateX1 = tile.ContainPortrait
                    ? Mathf.Max(TileStateX0 + 0.28f, frameX0 - 0.015f)
                    : TileStateX1;
                var statePlate = ElarionUiKit.AddImage(cell, "TileStatePlate",
                    new Vector2(TileStateX0, TileMedY0), new Vector2(stateX1, TileMedY1),
                    new Color(0.03f, 0.03f, 0.03f, 0.86f));
                var statePlateImage = statePlate != null ? statePlate.GetComponent<Image>() : null;
                if (statePlateImage != null)
                {
                    statePlateImage.raycastTarget = false;
                    ElarionUiKit.ApplyRounded(statePlateImage);
                }

                var stateWord = ElarionUiKit.Label(cell, tileState, TileMedY0, TileMedY1,
                    ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Center,
                    TileStateX0 + 0.01f, stateX1 - 0.01f, bold: true);
                // ⛔ THE CEILING IS THE GRID'S, NOT THIS TILE'S. stateFontPx was resolved once from
                // the LONGEST word on this grid (ResolveStateWordFont), so "MAX" and "QUEUE FULL"
                // paint at the same size and neither ellipsises. Fitting each label independently
                // is what produced "QUEUE FU..." beside a full-size "MAX" on the measured frame.
                ElarionUiKit.FitSingleLine(stateWord, ElarionUiKit.FontHardFloor, stateFontPx);

                float statePx = (TileMedY1 - TileMedY0) * cellH;
                if (statePx < MinTextBandPx)
                    FlowTrace.Warn("Manage", "tile state word band is " + statePx.ToString("0") +
                        "px, under the " + MinTextBandPx + "px TMP cull floor - the plate would " +
                        "paint and the WORD would not, which is the bare-plate defect class");
            }

            // THE NAME STRIP - one band, and the only text on the tile. A dark plate behind it so
            // the name reads against whatever the art happens to be, exactly as the mockup draws.
            var namePlate = ElarionUiKit.AddImage(cell, "TileNamePlate",
                new Vector2(0f, 0f), new Vector2(1f, TileTitleY1 + 0.035f),
                new Color(0.03f, 0.03f, 0.03f, 0.94f));
            var namePlateImage = namePlate != null ? namePlate.GetComponent<Image>() : null;
            if (namePlateImage != null) namePlateImage.raycastTarget = false;

            var title = ElarionUiKit.Label(cell, tile.Title ?? string.Empty, TileTitleY0, TileTitleY1,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Center,
                0.05f, 0.95f, bold: tile.IsSelected);
            ElarionUiKit.FitSingleLine(title, 20f, 30f);      // 0.24 band = 29px at the 120px floor

            // A BAR, not text - exempt from the cull floor because nothing is typeset in it.
            if (tile.Progress01.HasValue) ProgressBar(cell, tile.Progress01.Value,
                TileProgY0, TileProgY1, 0.05f, 0.95f);

            if (cellH < MinTileHeightPx)
                FlowTrace.Warn("Manage", "tile cell resolved to " + cellH.ToString("0") + "px, under the " +
                    MinTileHeightPx + "px floor - its name band falls under the " + MinTextBandPx +
                    "px TMP cull threshold and would render BLANK");
        }

        // =====================================================================
        //  ACTIVITY STRIP
        // =====================================================================

        private void BuildActivity(RectTransform band, ManageActivityVM activity)
        {
            var plate = ElarionUiKit.AddImage(band, "ActivityPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.62f));
            var plateImage = plate.GetComponent<Image>();
            if (plateImage != null) plateImage.raycastTarget = false;

            PaintSprite(band, "ActivityIcon", new Vector2(0.02f, 0.12f), new Vector2(0.12f, 0.88f),
                activity.IconKey);

            // ⚠ The two label bands are inset INSIDE the plate, not flush to it. The capture
            // auditor measured ManageActivity/Label overflowing its backing by 3.7px at the old
            // 0.94 / 0.08 edges, which is a text band spilling past the thing that is supposed to
            // contain it. 0.90 / 0.10 keeps both inside at ActivityBandPx=120 with room to spare,
            // and neither band drops under the 28px cull floor: 0.52-0.90 = 45.6px, 0.10-0.48 = 45.6px.
            var title = ElarionUiKit.Label(band, activity.Title ?? string.Empty, 0.52f, 0.90f,
                ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Left, 0.14f, 0.62f, bold: true);
            ElarionUiKit.FitSingleLine(title, 22f, 32f);      // band 46px

            var timer = ElarionUiKit.Label(band, Join(activity.TimerText, activity.QueuedCountText),
                0.10f, 0.48f, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                TextAlignmentOptions.Left, 0.14f, 0.62f);
            ElarionUiKit.FitSingleLine(timer, 20f, 30f);      // band 46px

            // ⛔ THE STRIP CARRIES NO QUEUE BUTTON. Do not add one back.
            // MEASURED in Builds/ui-capture/ManageFlow_Troops_railtop_2670x1200.png (2026-09-06,
            // 14:59): TWO queue affordances were on screen at once - the tab-row door and a second
            // "QUEUE" face here, bottom right. I had reported "exactly one affordance on screen",
            // and that claim was WRONG: I had counted the retired ManageScreenPanel header toggle
            // and missed this one, because it is built from a hardcoded literal in the view rather
            // than from the queue model. The capture is the evidence; the claim was not.
            // Canon's rule is ONE queue entry (CLAUDE.md 7, ruling Q10+Q13), and the owner's ruling
            // named which one: the tab-row door. So this face stands down and the strip returns to
            // what its name says it is - a STATUS GLANCE (what is running, how long, how many
            // queued). Every one of those words is still painted above.
        }

        // =====================================================================
        //  SELECTION CARD
        // =====================================================================

        private void BuildSelection(RectTransform band, ManageTabVM tab, float bandPx)
        {
            var plate = ElarionUiKit.AddImage(band, "SelectionPlate", Vector2.zero, Vector2.one,
                new Color(0.05f, 0.04f, 0.03f, 0.78f));
            var plateImage = plate.GetComponent<Image>();
            if (plateImage != null) plateImage.raycastTarget = false;
            ElarionUiKit.GoldPerimeter(band);

            // ⚠ WO-1443 section 3: the caller now COLLAPSES this band rather than reserving it, so
            // this arm is a guard, not a layout state. It no longer paints EmptyText - painting a
            // hint sentence in a reserved bordered box is precisely the defect the owner captured -
            // and it SAYS SO rather than drawing an empty plate in silence.
            ManageSelectionVM sel = tab != null ? tab.Selection : null;
            if (sel == null || !sel.Visible)
            {
                FlowTrace.Warn("Manage", "BuildSelection reached with no visible selection - Build() " +
                    "should have collapsed the band. An empty bordered plate is on screen taking " +
                    band.rect.height.ToString("0") + "px.");
                return;
            }

            // The CTA faces are resolved FIRST because whether any of them is refused decides
            // whether the "why" band is needed, and that band is part of the fixed floor.
            var faces = VisibleFaces(sel);
            bool needWhy = false;
            for (int i = 0; i < faces.Count; i++)
                if (!faces[i].Enabled && !string.IsNullOrEmpty(faces[i].DisabledReasonText)) needWhy = true;

            // ⭐ TWO COLUMNS: BIG ART LEFT, EVERYTHING ELSE RIGHT. This is the owner's mockup -
            // panel 3 (LUMBER MILL), panel 5 (ARCHER) and panel 9 (OUTRIDER, locked) are all the
            // same shape: "Big art LEFT; right = name, level, one-line purpose, a before -> after
            // stats table, cost with icons, time, one gold button" (CAPTURE_LOOP_GOAL 3.0).
            //
            // ⛔ THE RIGHT COLUMN FLOWS TOP-DOWN IN PIXELS AND ONLY SEATS WHAT EXISTS.
            // The first draft placed every band at a FIXED FRACTION of the card. On a LOCKED item -
            // which has no stats and no costs - that left two empty fractions in the middle and
            // stranded the CTA at the bottom of a two-thirds-empty column, which is what
            // ManageFlow_ARMY_locked showed. A fixed fraction assumes every screen has the same
            // content; these screens do not.
            // ⛔ AND THE CTA IS AUTHORED AT THE TOUCH FLOOR, IN PX, NOT AS A FRACTION.
            // MEASURED by the capture auditor: 'ObsBtn_VIEW BARRACKS' resolved 626.3x104.1 ref px -
            // 7.9px UNDER MinTouchPx(112) - and the auditor's own warning says why that matters:
            // "ClampMinTouch will grow it SYMMETRICALLY about its centre and spill it into both
            // neighbours. Author the band AT the floor." A fraction of a card whose height varies
            // cannot promise a px floor, so the band is now taken in px and the rest of the column
            // flows above it.
            float cardH = bandPx > 1f ? bandPx : (band.rect.height > 1f ? band.rect.height : 1f);
            float cardW = band.rect.width > 1f ? band.rect.width : 1f;

            // ⚠ THE ART ZONE IS SQUARE. The delivered troop art is square (1254x1254) and is
            // painted preserveAspect, so a NON-square zone leaves a transparent margin inside the
            // portrait rect - and the delivered PNGs carry a TRANSPARENCY CHECKERBOARD BAKED INTO
            // THEIR RGB (measured 2026-09-06: troop-outrider corner pixels read alpha 0 with RGB
            // varying 252/253/250/247/248, which is a checker pattern left in the colour channels
            // when the alpha was zeroed). A square zone is both the mockup's shape and the smallest
            // transparent margin, so it is the safer of the two either way.
            // ⭐ LARGE AND SQUARE, AND NO LONGER A MEDALLION (WO-1567 panel rows 3, 5 and 6).
            // Mockup panels 3, 5 and 6 all draw the art as a big SQUARE block filling the card's
            // left third. The owner's captures showed a circular disc inside a gilt ring instead
            // (owner-screen-20260907-004903.png Lumber Mill, -005222.png Archer, -005311.png
            // Outrider) - the kit's hero/combat frame, correct on a hero card and the wrong shape
            // here. SquarePortrait crops to the zone rather than insetting a preserveAspect square
            // inside a ring, so the SAME art reads roughly a third larger at the same rect.
            // ⚠ THE ZONE IS STILL SQUARE, for the reason recorded above: the delivered PNGs carry a
            // transparency checkerboard baked into their RGB, so any transparent margin shows it.
            // 0.42 replaces 0.38 because the ring's inset is gone and the card can spend the width
            // on the picture the mockup makes the focus of the screen.
            // ⭐ WO-1567 ROUND 25 - THE ART FILLS THE LEFT COLUMN'S FULL HEIGHT, AND THAT IS WHAT
            // FINALLY REMOVES THE RING.
            // ⛔ THE RING IS BAKED INTO THE ART, NOT DRAWN BY THIS CODE. MEASURED, by opening the
            // file: Assets/Resources/RpgUi/troop/troop-footman.png is a 1254x1254 medallion - a
            // gilt circle with fleur-de-lis corner ornaments PAINTED INTO THE PNG, on transparency.
            // So WO-1567 panel row 5's "apply the square treatment" could not work by swapping the
            // portrait call: SquarePortrait was ALREADY in use here, and it is why
            // Builds/ui-capture/ManageFlow_ARMY_max_2670x1200.png still shows a disc in a ring while
            // ManageFlow_ARMY_gridtop_2670x1200.png - the SAME art through the SAME method - shows
            // clean square portraits with no ring at all.
            // ⛔ THE DIFFERENCE IS THE ZONE'S ASPECT, AND IT IS THE WHOLE MECHANISM. SquarePortrait
            // envelope-fits (AspectRatioFitter.EnvelopeParent) inside a RectMask2D: a SQUARE sprite
            // in a SQUARE zone is a 1:1 fit that crops NOTHING, so the ring survives; the same
            // sprite in a NON-square zone is scaled to cover and the ring is cropped away, which is
            // exactly what the grid's 2.3:1 tiles do. `artFrac` was pinned to `cardH * 0.92 / cardW`
            // for the express purpose of keeping the zone square, so it was holding the ring in.
            // ⚠ THE TRANSPARENT-MARGIN WORRY THE OLD NOTE RECORDS IS ANSWERED BY THE SAME MOVE.
            // Its concern was that a non-square zone leaves transparent margin inside the portrait
            // rect, where the delivered PNGs carry a checkerboard baked into their RGB. That is true
            // of preserveAspect, which INSETS; envelope-cropping OVERFLOWS, so the transparent
            // corners are the first thing cut. Cropping is strictly safer here, not riskier.
            // ⛔ AND IT IS WHAT THE MOCKUP DRAWS. Panels 3, 5 and 6 all put a big rectangular block
            // of art down the card's left side, floor to ceiling - not a disc floating in a square.
            float artFrac = Mathf.Min(0.40f, Mathf.Max(0.28f, (cardH * 0.90f) / Mathf.Max(1f, cardW)));
            var portrait = Zone(band, "SelPortrait",
                new Vector2(0.015f, 0.02f), new Vector2(0.015f + artFrac, 0.98f));
            // ⛔ NEVER DIMMED HERE, EVEN WHEN LOCKED. Mockup panel 6 (OUTRIDER, locked) draws the
            // art at FULL brightness - the dim on a grid tile is a scanning cue for "not yours
            // yet" across nine peers, and this screen has one subject. Dimming it would make the
            // detail card look broken while telling the player nothing the padlock row does not.
            SquarePortrait(portrait, sel.PortraitKey, dim: false);

            const float RightX1 = 0.98f;
            // Tracks the portrait's own left inset (0.015) so the text column can never start
            // INSIDE the art - the two used to be authored from two different origins.
            float rightX0 = Mathf.Min(0.60f, 0.015f + artFrac + 0.035f);

            // ---- the column FLOWS: level, description, stats, costs, why, then the CTA ----
            // ⛔ THE CTA IS NOT PINNED TO THE BOTTOM. It was, and the capture showed the result:
            // on a LOCKED item - description and one requirement sentence, no stats, no costs - the
            // right column was two-thirds empty with the button stranded at the floor. The mockup's
            // panels are vertically COMPACT and sit high; panel 9's content is four short things in
            // the top half. So the CTA flows with everything else and the column simply ends.
            // ⛔ BUT ITS HEIGHT IS STILL TAKEN IN PX, NOT AS A FRACTION. The auditor caught
            // 'ObsBtn_VIEW BARRACKS' at 626.3x104.1 ref px - 7.9px under MinTouchPx(112) - because a
            // fraction of a card whose height varies cannot promise a px floor. Its own words:
            // "ClampMinTouch will grow it SYMMETRICALLY about its centre and spill it into both
            // neighbours. Author the band AT the floor."
            float ctaPx = Mathf.Max(ElarionUiKit.MinTouchPx + 8f, cardH * 0.16f);
            if (ctaPx > cardH * 0.42f) ctaPx = cardH * 0.42f;

            float cursorY = 0.96f;                               // fraction, top-down
            float gapF = 16f / cardH;

            // ⛔ THE DOT JOINER IS GONE FROM THIS CARD, AND THAT IS ONE FIX FOR FOUR FRAMES.
            // `Join(a, b)` welds two facts with "  .  ". On the owner's device that produced, in
            // one evening:
            //   panel 3  "Wood production +10%.  .  Wood production +18%."   (-004903)
            //   panel 5  "Back-line ranged DPS. Fragile but hits hard.  .  L7 unlocks Thunderbolt"
            //   panel 6  "Fast flanker. Runs down towers and stragglers.  .  Requires Barracks Tier 4"
            //   panel 5/6 "Level 5  .  UPGRADING" / "Level 1  .  LOCKED"    (the title block)
            // Every one of those is TWO facts of DIFFERENT kinds sharing one band - a description
            // and a promotion, a description and a requirement, a level and a state. The mockup
            // gives each its own line (panel 5: name, "Level 1", then the description, then the
            // stats; panel 6: the requirement on its own padlock row). A separator is not a layout.
            // ⚠ Join() SURVIVES for the cases it was right for - nothing else changes.

            // LEVEL on its own line, with the STATE as a BADGE beside it (mockup panels 3/5/6).
            // ⛔ STILL NOT a gold heading: the earlier capture showed a locked troop headed
            // "LOCKED" in gold where the mockup has no such heading. The badge is a plate + word at
            // the line's right end - a shape and a string, legible in greyscale.
            if (!string.IsNullOrEmpty(sel.LevelText) || !string.IsNullOrEmpty(sel.StateText))
            {
                float h = 44f / cardH;
                bool hasBadge = !string.IsNullOrEmpty(sel.StateText);
                float badgeX0 = hasBadge ? Mathf.Max(rightX0 + 0.10f, RightX1 - 0.26f) : RightX1;
                if (!string.IsNullOrEmpty(sel.LevelText))
                {
                    var levelLine = ElarionUiKit.Label(band, sel.LevelText, cursorY - h, cursorY,
                        ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                        TextAlignmentOptions.Left, rightX0, badgeX0 - 0.01f);
                    ElarionUiKit.FitSingleLine(levelLine, 22f, 32f);
                }
                if (hasBadge)
                {
                    var badgePlate = ElarionUiKit.AddImage(band, "SelStateBadge",
                        new Vector2(badgeX0, cursorY - h), new Vector2(RightX1, cursorY),
                        new Color(0.03f, 0.03f, 0.03f, 0.86f));
                    var badgeImage = badgePlate != null ? badgePlate.GetComponent<Image>() : null;
                    if (badgeImage != null) badgeImage.raycastTarget = false;
                    var badgeWord = ElarionUiKit.Label(band, sel.StateText, cursorY - h, cursorY,
                        ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Center,
                        badgeX0 + 0.005f, RightX1 - 0.005f, bold: true);
                    ElarionUiKit.FitSingleLine(badgeWord, ElarionUiKit.FontHardFloor, 26f);
                }
                cursorY -= h + gapF;
            }

            // THE AUTHORED DESCRIPTION SENTENCE, ALONE. Two lines of room, wrapped, never joined.
            if (!string.IsNullOrEmpty(sel.Description))
            {
                float h = 76f / cardH;
                var desc = ElarionUiKit.Label(band, sel.Description, cursorY - h, cursorY,
                    ElarionUi.Parchment, ElarionUi.FontLabel,
                    TextAlignmentOptions.TopLeft, rightX0, RightX1);
                desc.enableAutoSizing = false;
                desc.fontSize = 26f;
                desc.overflowMode = TextOverflowModes.Ellipsis;
                cursorY -= h + gapF;
            }

            // ⭐ THE AUXILIARY LINE GETS ITS OWN ROW - the promotion note ("L7 unlocks
            // Thunderbolt") or, on a NotUnlocked item, the requirement ("Requires Barracks Tier 4",
            // which ManageVmProjection puts here from item.LockReason).
            // ⛔ A LOCKED ONE CARRIES THE PADLOCK. The owner is red/green colourblind, so the
            // padlock is the SHAPE channel for "locked" (CAPTURE_LOOP_GOAL 3c) and mockup panel 6
            // draws exactly that: a padlock glyph, then the requirement, on their own row. The
            // glyph key is the MODEL's StateIconKey - the View picks no art by state.
            if (!string.IsNullOrEmpty(sel.AuxiliaryText))
            {
                float h = 46f / cardH;
                bool locked = sel.State == ManageTileVisualState.Locked;
                float lockW = locked ? 0.045f : 0f;
                if (locked)
                    PaintSprite(band, "SelAuxLock",
                        new Vector2(rightX0, cursorY - h), new Vector2(rightX0 + lockW, cursorY),
                        sel.StateIconKey);
                var aux = ElarionUiKit.Label(band, sel.AuxiliaryText, cursorY - h, cursorY,
                    ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Left,
                    rightX0 + lockW + (locked ? 0.012f : 0f), RightX1);
                ElarionUiKit.FitSingleLine(aux, 18f, 26f);
                cursorY -= h + gapF;
            }

            // THE STATS TABLE - one ROW PER STAT. The mockup draws
            // "Production   120 / hour  ->  180 / hour" as a table, and that before -> after is what
            // the upgrade BUYS. A locked item has none, and then it takes no room at all.
            if (sel.Stats != null && sel.Stats.Count > 0)
            {
                float h = Mathf.Min(sel.Stats.Count, 5) * 40f / cardH;
                BuildStatRows(band, sel.Stats, rightX0, RightX1, cursorY - h, cursorY);
                cursorY -= h + gapF;
            }

            // ⭐ THE COST BAND - a CAPTION, the priced resources, then the CLOCK ON ITS OWN LINE.
            // Mockup panel 3 draws "Upgrade Cost" over a wood glyph + 1,200 and an iron glyph +
            // 600, with a clock and 45m beneath them; panel 5 draws "Train Cost" the same way.
            // ⛔ THE CLOCK IS NOT A COST ROW. A duration has no bank and no affordability verdict,
            // so it gets its own line rather than a basket slot that would need a special case in
            // every reader (ManageSelectionVM.TimeText says the same thing from the model's side).
            bool hasCosts = sel.Costs != null && sel.Costs.Count > 0;
            bool hasTime = !string.IsNullOrEmpty(sel.TimeText);
            if ((hasCosts || hasTime) && !string.IsNullOrEmpty(sel.CostCaption))
            {
                float h = 32f / cardH;
                var caption = ElarionUiKit.Label(band, sel.CostCaption, cursorY - h, cursorY,
                    ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Left,
                    rightX0, RightX1);
                ElarionUiKit.FitSingleLine(caption, 18f, 24f);
                cursorY -= h + gapF * 0.5f;
            }
            if (hasCosts)
            {
                float h = 58f / cardH;
                BuildCostRow(band, sel.Costs, rightX0, RightX1, cursorY - h, cursorY);
                cursorY -= h + gapF;
            }
            if (hasTime)
            {
                float h = 46f / cardH;
                float clockW = 0.05f;
                PaintSprite(band, "SelTimeIcon",
                    new Vector2(rightX0, cursorY - h), new Vector2(rightX0 + clockW, cursorY),
                    sel.TimeIconKey);
                var time = ElarionUiKit.Label(band, sel.TimeText, cursorY - h, cursorY,
                    ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Left,
                    rightX0 + clockW + 0.012f, RightX1);
                ElarionUiKit.FitSingleLine(time, 18f, 26f);
                cursorY -= h + gapF;
            }

            if (needWhy)
            {
                // Canon 11 question 6: if I cannot act, WHY - panel 9's "Requires Barracks Tier 4".
                // ⛔ THE PADLOCK IS PAINTED INLINE, TO THE LEFT OF THE SENTENCE, AND THE TEXT
                // STARTS AFTER IT. The previous round hung it off the column's left edge at a
                // negative offset and it VANISHED from the capture - a rect outside its column is a
                // rect nobody sees, which is the same mistake as the badge that ended up outside the
                // panel. It matters more here than anywhere else on the screen: the owner is
                // red/green colourblind, so the padlock is the SHAPE channel for "locked" and the
                // dim word alone is not enough (CAPTURE_LOOP_GOAL 3c).
                float h = 46f / cardH;
                float lockW = 0.045f;
                PaintSprite(band, "SelWhyLock",
                    new Vector2(rightX0, cursorY - h), new Vector2(rightX0 + lockW, cursorY),
                    sel.StateIconKey);
                var why = ElarionUiKit.Label(band, WhyLine(faces), cursorY - h, cursorY,
                    ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Left,
                    rightX0 + lockW + 0.012f, RightX1);
                ElarionUiKit.FitSingleLine(why, 18f, 26f);
                cursorY -= h + gapF;
            }

            if (sel.Progress.HasValue)
            {
                float h = 14f / cardH;
                ProgressBar(band, sel.Progress.Value, cursorY - h, cursorY, rightX0, RightX1);
                cursorY -= h + gapF;
            }

            // ONE GOLD CTA. Prefer the lower anchor to use the full detail card; if the composed
            // facts extend into that seat, place it directly below those facts instead.
            float ctaH = ctaPx / cardH;
            const float DetailActionBottom = 0.05f;
            float ctaY0 = cursorY >= DetailActionBottom + ctaH
                ? DetailActionBottom
                : Mathf.Max(0.02f, cursorY - ctaH);
            var actionBand = Zone(band, "SelActions",
                new Vector2(rightX0, ctaY0), new Vector2(RightX1, ctaY0 + ctaH));
            BuildActionRow(actionBand, faces);
            if (ctaPx < ElarionUiKit.MinTouchPx)
                FlowTrace.Warn("Manage", "detail CTA band is " + ctaPx.ToString("0") + "px against the " +
                    ElarionUiKit.MinTouchPx + "px touch floor in a " + cardH.ToString("0") +
                    "px card - the well is too short for one authored button");
            FlowTrace.Step("Manage", "MANAGE_DETAIL card=" + cardH.ToString("0") + "x" +
                cardW.ToString("0") + "px art=" + artFrac.ToString("0.##") + " cta=" +
                ctaPx.ToString("0") + "px stats=" + (sel.Stats != null ? sel.Stats.Count : 0) +
                " costs=" + (sel.Costs != null ? sel.Costs.Count : 0) +
                " faces=" + faces.Count + " why=" + needWhy);
        }

        /// <summary>
        /// The before -> after stats table, one ROW PER STAT (mockup panel 3's
        /// "Production 120 / hour -> 180 / hour"). Rows lay top-down inside the supplied fraction
        /// band and are DROPPED, never shrunk, when the band cannot seat another at the
        /// <see cref="MinTextBandPx"/> cull floor - an omitted row is visibly missing, a culled one
        /// is invisible, and invisible is the trap.
        /// <para>⚠ The delta is carried by the model's own words plus an ASCII arrow, never by
        /// colour. The mockup prints the new value in green; the owner is red/green colourblind, so
        /// the ARROW and the VALUE are the channel and Gold is only emphasis.</para>
        /// </summary>
        private void BuildStatRows(RectTransform band, IReadOnlyList<ManageStatVM> stats,
            float x0, float x1, float y0, float y1)
        {
            if (stats == null || stats.Count == 0) return;
            float bandH = band.rect.height > 1f ? band.rect.height : 1f;
            float spanPx = (y1 - y0) * bandH;
            int seats = Mathf.FloorToInt(spanPx / MinTextBandPx);
            if (seats < 1)
            {
                FlowTrace.Warn("Manage", "the detail card's stats band is " + spanPx.ToString("0") +
                    "px - it cannot seat one row at the " + MinTextBandPx + "px cull floor, so the " +
                    "whole table is omitted rather than rendered blank");
                return;
            }
            int rows = Mathf.Min(seats, stats.Count);
            int hidden = stats.Count - rows;
            if (hidden > 0)
                FlowTrace.Warn("Manage", "detail card shows " + rows + " of " + stats.Count +
                    " stat rows - the band is " + spanPx.ToString("0") + "px");

            float rowH = (y1 - y0) / rows;
            float mid = x0 + (x1 - x0) * 0.42f;
            for (int i = 0; i < rows; i++)
            {
                var s = stats[i];
                if (s == null) continue;
                float rowBottom = y1 - (i + 1) * rowH;
                float inner = rowH * 0.10f;

                var label = ElarionUiKit.Label(band, s.Label ?? string.Empty,
                    rowBottom + inner, rowBottom + rowH - inner,
                    ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Left, x0, mid);
                ElarionUiKit.FitSingleLine(label, 18f, 26f);

                string valueLine = string.IsNullOrEmpty(s.DeltaText)
                    ? (s.Value ?? string.Empty)
                    : (s.Value ?? string.Empty) + "  ->  " + s.DeltaText;
                // ⭐ THE NEXT VALUE IS EMPHASISED BY WEIGHT, NOT BY HUE (WO-1567 panel row 3).
                // The mockup prints the promoted number in green; the owner is red/green
                // colourblind, so a row that CHANGES is set BOLD and keeps the ASCII arrow. Gold
                // stays as a second, redundant channel - never the only one. A row with no delta
                // is regular weight, so the difference is visible in greyscale at a glance.
                bool promoted = !string.IsNullOrEmpty(s.DeltaText);
                var value = ElarionUiKit.Label(band, valueLine,
                    rowBottom + inner, rowBottom + rowH - inner,
                    promoted ? ElarionUi.Gold : ElarionUi.Parchment,
                    ElarionUi.FontLabel, TextAlignmentOptions.Right, mid, x1, bold: promoted);
                ElarionUiKit.FitSingleLine(value, 18f, 26f);
            }
        }

        /// <summary>
        /// The cost basket as ICONS + AMOUNTS across one row, which is how the mockup draws it: a
        /// wood glyph and 1,200, an iron glyph and 600, a clock and 45m.
        /// <para><c>Affordable</c> is a MODEL verdict. An unaffordable line is dimmed AND its
        /// refusal sentence is already on the why band - never colour alone.</para>
        /// </summary>
        private void BuildCostRow(RectTransform band, IReadOnlyList<ManageCostVM> costs,
            float x0, float x1, float y0, float y1)
        {
            if (costs == null || costs.Count == 0) return;
            int n = costs.Count;
            float w = (x1 - x0) / n;
            float pad = (y1 - y0) * 0.15f;
            for (int i = 0; i < n; i++)
            {
                var c = costs[i];
                if (c == null) continue;
                float cx0 = x0 + i * w;
                PaintSprite(band, "SelCostIcon" + i,
                    new Vector2(cx0, y0 + pad), new Vector2(cx0 + w * 0.28f, y1 - pad), c.IconKey);
                // ⭐ THE RESOURCE IS NAMED IN WORDS BESIDE ITS NUMBER (WO-1567 panel row 3).
                // ⛔ THE GLYPH ALONE WAS NEVER ENOUGH, AND ON THIS BUILD THERE WAS NO GLYPH EITHER.
                // The owner's Lumber Mill card read "2600   970" - two bare numbers naming no
                // resource at all (owner-screen-20260907-004903.png), because ManageScreenVM.CostVms
                // set IconKey = null on every row. Both halves are fixed: the model now supplies the
                // delivered glyph, and the row prints the model's own WORD next to the amount. The
                // word is the accessible channel - a small icon is exactly the kind of meaning the
                // owner cannot separate by colour, and a name cannot be misread.
                bool named = !string.IsNullOrEmpty(c.Label);
                var color = c.Affordable ? ElarionUi.Parchment : ElarionUi.ParchmentDim;
                var amount = ElarionUiKit.Label(band, c.AmountText ?? string.Empty,
                    named ? (y0 + y1) * 0.5f : y0, y1, color,
                    ElarionUi.FontLabel, TextAlignmentOptions.Left, cx0 + w * 0.30f, cx0 + w * 0.98f);
                ElarionUiKit.FitSingleLine(amount, 20f, 30f);
                if (named)
                {
                    var word = ElarionUiKit.Label(band, c.Label, y0, (y1 + y0) * 0.5f, color,
                        ElarionUi.FontLabel, TextAlignmentOptions.Left,
                        cx0 + w * 0.30f, cx0 + w * 0.98f);
                    ElarionUiKit.FitSingleLine(word, ElarionUiKit.FontHardFloor, 22f);
                    float wordPx = (y1 - y0) * 0.5f * (band.rect.height > 1f ? band.rect.height : 1f);
                    if (wordPx < MinTextBandPx)
                        FlowTrace.Warn("Manage", "the cost row's resource NAME band is " +
                            wordPx.ToString("0") + "px, under the " + MinTextBandPx + "px TMP cull " +
                            "floor - the amount would paint and the word that says WHICH resource " +
                            "it is would not, which is the defect this row was widened to fix");
                }
            }
        }

        /// <summary>The visible CTA faces, in reading order: the door, the secondary, the primary.</summary>
        private static List<ManageActionVM> VisibleFaces(ManageSelectionVM sel)
        {
            var faces = new List<ManageActionVM>();
            if (sel.RequirementAction != null && sel.RequirementAction.Visible) faces.Add(sel.RequirementAction);
            if (sel.SecondaryAction != null && sel.SecondaryAction.Visible) faces.Add(sel.SecondaryAction);
            if (sel.PrimaryAction != null && sel.PrimaryAction.Visible) faces.Add(sel.PrimaryAction);
            return faces;
        }

        /// <summary>The model's refusal sentences, joined. Joined, never composed.</summary>
        private static string WhyLine(List<ManageActionVM> faces)
        {
            string line = null;
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                if (f.Enabled || string.IsNullOrEmpty(f.DisabledReasonText)) continue;
                line = Join(line, f.DisabledReasonText);
            }
            return line ?? string.Empty;
        }

        /// <summary>
        /// The CTA row. Up to three slots - requirement (ruling 18's door), secondary, primary -
        /// laid by even split so the primary is always the rightmost face.
        /// A 0.04-0.96 vertical inset of a 120px band gives 110px, UNDER MinTouchPx(112);
        /// the inset is therefore 0.02-0.98 => 115px, a measured margin of +3px. Authored, not
        /// clamped (ElarionUiKit.cs:1100).
        /// </summary>
        private void BuildActionRow(RectTransform band, List<ManageActionVM> faces)
        {
            if (faces.Count == 0) return;

            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                // ⭐ THE FACE READS THE VERB, AND NOTHING ELSE (WO-1567 panel rows 3 and 5).
                // ⛔ IT USED TO PASS THE LABEL AND THE COST LINE THROUGH THE DOT JOINER, AND THAT
                // WELD IS THE DEFECT. (Deliberately DESCRIBED and not SPELLED: the oracle that
                // forbids it scans this method's body for the literal call, and it does not strip
                // comments first - a comment quoting the banned form would fail working code. That
                // exact trap cost this repo a regression round on 2026-09-06; see
                // ManageScreenPanel's launcher-card note.) Measured
                // on the owner's device: "UPGRADE  .  STONE 2600  GOL..." (-004903) - the button
                // ellipsised mid-word and the player could not read either the verb or the price -
                // and "TRAIN  .  1M 0S" (-005222), which put a DURATION on a button as though it
                // were a price. The mockup's buttons say "UPGRADE" and "TRAIN 1 ARCHER"; the cost
                // and the clock live in the labelled band above them, where there is room for the
                // resource NAMES and where a number can be compared against a bank.
                // ⚠ NOTHING IS LOST: face.CostText's content is the same basket BuildCostRow
                // paints from sel.Costs, and a REFUSAL still carries its sentence on the why band.
                var btn = ElarionUiKit.Button(band, face.Label,
                    KindFor(face.StyleRole),
                    new Vector2(SlotStart(i, faces.Count), 0.02f),
                    new Vector2(SlotEnd(i, faces.Count), 0.98f),
                    MakeInvoker(face.Activate));
                Track(btn);

                // Explicit field, never inferred from the callback (canon 9 / ManageStateModel's
                // "Invoke being null is an implementation detail, NOT a state").
                btn.interactable = face.Enabled;
            }
        }

        // =====================================================================
        //  PRIMITIVES
        // =====================================================================

        /// <summary>A horizontal band pinned <paramref name="topPx"/> below the parent's top edge.</summary>
        private RectTransform BandFromTop(RectTransform parent, string name, float topPx, float heightPx)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Track(go);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -(topPx + heightPx));
            rt.offsetMax = new Vector2(0f, -topPx);
            return rt;
        }

        private RectTransform Zone(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Paints a supplied asset KEY into a preserve-aspect Image. A key that does not resolve
        /// renders fully transparent (ManageArt has already announced the miss once) rather than
        /// as a white box - a white box reads as art, and art that is wrong is worse than absent.
        /// </summary>
        private void PaintSprite(RectTransform parent, string name, Vector2 min, Vector2 max, string key)
        {
            var go = ElarionUiKit.AddImage(parent, name, min, max, Color.white, rounded: false);
            var img = go != null ? go.GetComponent<Image>() : null;
            if (img == null) return;
            img.sprite = ManageArt.LoadSprite(key);
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (img.sprite == null) img.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// ⭐ THE SQUARE, EDGE-TO-EDGE PORTRAIT - mockup panels 2, 4 and 6 (WO-1567 rows 2/4/7).
        ///
        /// <para>⛔ THIS REPLACES <c>ElarionUiKit.Portrait</c> ON GRID TILES AND THE DETAIL CARD,
        /// AND THE MEDALLION RING GOES WITH IT. The kit's Portrait is a CIRCULAR identity disc
        /// inside a gilt ring - the right shape for a combat/hero frame, and it is still used
        /// everywhere else. It is the wrong shape here: the owner's captures
        /// (Logs/device/screens/owner-screen-20260907-004825.png and -005136.png) show every BUILD
        /// and ARMY tile as a small round medallion floating in a black plate, while every tile in
        /// her mockup is a SQUARE of art that reaches the cell's edges with the name on a strip
        /// below it. Two rings around one picture (the kit's, plus the state frame this file
        /// already paints) is also the reason the art read as small: the ring's preserveAspect
        /// square is inset inside the zone, so the art lost the corners twice over.</para>
        ///
        /// <para>⛔ IT CROPS, IT DOES NOT LETTERBOX OR STRETCH. The art is envelope-fitted inside a
        /// <see cref="RectMask2D"/>, so a 1024x1024 portrait in a wide cell fills the cell and
        /// loses its left and right edges rather than sitting in black bars (letterboxing is what
        /// made the retired landscape card strips read as broken) or distorting (stretching is what
        /// the research picker was doing to them). Square art in a square cell is unchanged.</para>
        ///
        /// <para>⛔ <paramref name="dim"/> IS A LUMINANCE MULTIPLY, NEVER A HUE SHIFT. Mockup panel
        /// 4 draws locked troops darker than unlocked ones. The owner is red/green colourblind
        /// (CAPTURE_LOOP_GOAL 3c: "meaning never carried by hue alone"), so the cue is a flat
        /// brightness multiply that survives greyscale - and it is never the ONLY cue: a dimmed
        /// tile also carries the word LOCKED and the padlock medallion.</para>
        ///
        /// <para>A missing key paints NOTHING here - no tan placeholder disc. ManageArt.LoadSprite
        /// has already announced the miss by key, and an empty framed well reads as "art pending"
        /// where a warm-tan oval read as a rendering bug (WO-1567 section 5 item 3).</para>
        /// </summary>
        /// <param name="zoneWpx">The zone's width in px, or 0 when the caller cannot supply it.</param>
        /// <param name="zoneHpx">The zone's height in px, or 0 when the caller cannot supply it.
        /// <para>⭐ WO-1567 ROUND 26 - WHEN BOTH ARE GIVEN AND THE SPRITE FITS THE ZONE'S WIDTH
        /// WITHOUT EXCEEDING ITS HEIGHT, THE ART IS FITTED BY **WIDTH** AND ANCHORED TO THE
        /// **BOTTOM** instead of envelope-cropped. That shows the WHOLE subject, which is what
        /// mockup panel 2 draws and what the envelope crop was taking away: on a square 359px BUILD
        /// cell a square building was scaled to cover a 359x266 zone and lost 93px, split top and
        /// bottom - Archer Tower, Ballista and Cathedral of Magic all rendered without their roofs.
        /// A building sits on the ground, so any surplus belongs ABOVE it, never split around it.</para>
        /// <para>⛔ IT FALLS BACK TO THE ENVELOPE CROP THE MOMENT THE WIDTH FIT WOULD OVERFLOW, AND
        /// THAT IS NOT DEFENSIVE PADDING. ARMY's cell is 2.3:1 (566x246): a width fit of square art
        /// is 566px tall there, and bottom-anchoring it inside a 246px mask cuts 320px off the
        /// TOP - i.e. the troops' heads. The envelope crop already reads correctly on that shape
        /// (ManageFlow_ARMY_gridtop_2670x1200.png), so the rule is "show the whole subject when the
        /// cell can hold it", not "always fit by width".</para>
        /// <para>⚠ ARITHMETIC, NOT A RECT READ. Both dimensions are handed in by the caller because
        /// the zone's own rect is 0 on the frame the tile is built - the trap that has already cost
        /// this screen a build-time estimate out by 1.5x.</para></param>
        private void SquarePortrait(RectTransform zone, string key, bool dim,
                                    float zoneWpx = 0f, float zoneHpx = 0f)
        {
            if (zone == null) return;
            var sprite = ManageArt.LoadSprite(key);
            if (sprite == null) return;

            if (zone.GetComponent<RectMask2D>() == null) zone.gameObject.AddComponent<RectMask2D>();

            var artGo = new GameObject("PortraitSquare", typeof(RectTransform), typeof(Image));
            artGo.transform.SetParent(zone, false);
            var rt = (RectTransform)artGo.transform;

            var img = artGo.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = false;          // the fitter owns the aspect; preserveAspect would inset it
            img.raycastTarget = false;
            // 0.42 is the kit's own disabled-face multiply (ManageScreenPanel's launcher colours),
            // reused so a dimmed thing looks the same wherever the player meets one.
            img.color = dim ? new Color(0.42f, 0.42f, 0.42f, 1f) : Color.white;

            float w = sprite.rect.width, h = sprite.rect.height;
            float aspect = h > 0.01f ? w / h : 1f;
            bool widthToBottom = zoneWpx > 1f && zoneHpx > 1f && (zoneWpx / aspect) <= zoneHpx + 0.5f;

            if (widthToBottom)
            {
                // Stretch across the zone's width, pinned to its FLOOR; the fitter sets the height
                // from that width, so the picture grows upward and the surplus is a margin above it.
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
                var fitW = artGo.AddComponent<AspectRatioFitter>();
                fitW.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
                fitW.aspectRatio = aspect;
                return;
            }

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (h > 0.01f)
            {
                var fit = artGo.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = aspect;
            }
        }

        /// <summary>
        /// ⭐ PAINT ONLY PART OF A SPRITE - the sub-rect <c>[u0..u1] x [v0..v1]</c> in UV, scaled
        /// to fill <paramref name="zone"/>. Used to keep a BAKED CAPTION out of a medallion.
        ///
        /// <para>⛔ ANCHORS, NOT PIXELS, AND THAT IS THE WHOLE POINT. The child is anchored so the
        /// requested sub-rect lands exactly on the zone: a child spanning parent fractions
        /// <c>a..b</c> puts source <c>u</c> at <c>a + u*(b-a)</c>, so solving
        /// <c>a + u0*(b-a) = 0</c> and <c>a + u1*(b-a) = 1</c> gives <c>b-a = 1/(u1-u0)</c> and
        /// <c>a = -u0/(u1-u0)</c>. It needs no layout pass and no measured rect, which is what
        /// makes it correct on the frame it is built - the trap that has cost this screen a
        /// build-time estimate that was out by 1.5x.</para>
        ///
        /// <para>A RectMask2D on the zone clips the overhang. The Image is preserveAspect FALSE on
        /// purpose: the aspect is already carried by the sub-rect the caller measured, and letting
        /// the Image re-fit would inset the crop back inside the zone.</para>
        /// </summary>
        private void CroppedIcon(RectTransform zone, string key, float u0, float u1, float v0, float v1)
        {
            if (zone == null) return;
            var sprite = ManageArt.LoadSprite(key);
            if (sprite == null) return;                 // LoadSprite already announced the miss

            float fw = u1 - u0, fh = v1 - v0;
            if (fw <= 0.001f || fh <= 0.001f)
            {
                FlowTrace.Warn("Manage", "a cropped icon was asked for a degenerate sub-rect (" +
                    fw.ToString("0.###") + "x" + fh.ToString("0.###") + ") of '" + key +
                    "' - painting the whole sprite rather than a zero-area one");
                ElarionUiKit.Portrait(zone, sprite, active: false);
                return;
            }

            if (zone.GetComponent<RectMask2D>() == null) zone.gameObject.AddComponent<RectMask2D>();

            var go = new GameObject("CroppedIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(zone, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(-u0 / fw, -v0 / fh);
            rt.anchorMax = new Vector2((1f - u0) / fw, (1f - v0) / fh);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = false;
            img.raycastTarget = false;
        }

        /// <summary>A two-part fill bar. Graphic only - nothing is typeset, so no cull floor applies.</summary>
        private void ProgressBar(RectTransform parent, float fill01, float y0, float y1, float x0, float x1)
        {
            float clamped = Mathf.Clamp01(fill01);
            var track = ElarionUiKit.AddImage(parent, "ProgressTrack",
                new Vector2(x0, y0), new Vector2(x1, y1), new Color(0f, 0f, 0f, 0.55f));
            var trackImage = track.GetComponent<Image>();
            if (trackImage != null) trackImage.raycastTarget = false;
            var fill = ElarionUiKit.AddImage(track.transform, "ProgressFill",
                Vector2.zero, new Vector2(clamped, 1f), ElarionUi.Gold);
            var fillImage = fill.GetComponent<Image>();
            if (fillImage != null) fillImage.raycastTarget = false;
        }

        /// <summary>The active-chip SHAPE cue: a solid underline, legible in greyscale.</summary>
        private void Underline(Transform host)
        {
            var bar = ElarionUiKit.AddImage(host, "ActiveUnderline",
                new Vector2(0.10f, 0.00f), new Vector2(0.90f, 0.06f), ElarionUi.Gold);
            var img = bar.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
        }

        private static ElarionUiKit.ButtonKind KindFor(ManageActionStyleRole role)
        {
            switch (role)
            {
                case ManageActionStyleRole.Destructive: return ElarionUiKit.ButtonKind.Danger;
                case ManageActionStyleRole.Navigate: return ElarionUiKit.ButtonKind.Confirm;
                case ManageActionStyleRole.Secondary: return ElarionUiKit.ButtonKind.Quiet;
                default: return ElarionUiKit.ButtonKind.Gold;
            }
        }

        // Even split with a gutter. Shared by tabs, filters and the CTA row so three bands
        // cannot drift into three different slot arithmetics.
        private static float SlotStart(int index, int count) =>
            0.02f + index * (0.96f / Mathf.Max(1, count)) + 0.006f;

        private static float SlotEnd(int index, int count) =>
            0.02f + (index + 1) * (0.96f / Mathf.Max(1, count)) - 0.006f;

        /// <summary>
        /// Wraps a model callback for the kit's Action-shaped click hook. Always non-null, so the
        /// renderer never has to test a callback to decide whether a control exists - that
        /// decision belongs to ManageActionVM.Visible / Enabled.
        /// </summary>
        private static Action MakeInvoker(Action callback) => () => callback?.Invoke();

        private static UnityEngine.Events.UnityAction MakeUnityInvoker(Action callback) =>
            () => callback?.Invoke();

        /// <summary>Joins two model-supplied fragments with a separator. Joins; never invents.</summary>
        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "  .  " + b;
        }

        private void Track(GameObject go) { if (go != null) _spawned.Add(go); }
        private void Track(Component c) { if (c != null) _spawned.Add(c.gameObject); }
    }
}
