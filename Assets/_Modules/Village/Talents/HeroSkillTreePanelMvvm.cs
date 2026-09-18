// =============================================================================
// HeroSkillTreePanelMvvm — the hero skill-tree VIEW (MVVM slice). A DUMB SKIN.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Talents
//
// WO-896 (owner 2026-08-15) — SPARSE OBSIDIAN TALENT GRAPH, not a dense grid and
// not the busy "horizontal tracks + MORE BELOW" mitigation. Authoritative look =
// the Obsidian kit Talent Tree demo (Screenshot 2026-08-15 175853.png):
//   * Few LARGE nodes, lots of dark empty space
//   * Gold connectors along prerequisites (diagonal OK)
//   * EXACTLY ONE thick gold FOCUS plate, and it is the BOARD-LEVEL SELECTION.
//     CORRECTED 2026-08-16 (WO-1021 sec 2.1d): this line used to read "the selected /
//     NEXT node", and that premise was VIOLATED BY CONSTRUCTION. HeroSkillTreeVM.
//     ResolveStates resets its nextTaken flag PER TRACK, so the board carries ONE
//     SkillNodeState.Next PER ORDERED TRACK - the view consumed a per-track signal as
//     if it were board-level and grew one oversized gold plate per track (~10 of them
//     at WIS 252, the owner's "still messy"). The VM is CORRECT and untouched. Size is
//     the scarce channel and SELECTION owns it: ResolveFocusNodeId returns AT MOST ONE
//     id, and per-track Next is carried by a same-size, shape+position badge
//     (BuildNextTrackMarker) that survives greyscale.
//   * Rank pip on the plate ("1/1" owned, "0/1" locked)
//   * Calm chrome — FrameTalent + crest title "TALENT TREE"
//   * Owner 2026-08-15 (Screenshot 190301): graph ONLY. No detail column, no loadout
//     band, no Cancel/Respec/CONFIRM footer, no bottom Close. Node tap opens a spend
//     popup (name + desc + "Spend N Wisdom for X?" + Confirm/Cancel). Scrim dismisses.
//
// COLOURBLIND LAW (owner is red/green colourblind): every state is separable with
// colour stripped. Owned = filled plate + rank 1/1; SELECTED = oversized + thick gold
// outer frame (at most one); NEXT = a bottom-left chevron badge at NORMAL size; Locked =
// dim + padlock; Inert = slash + "!". Connectors
// differ by THICKNESS (solid 8 / solid 4 / dashed 3), never by hue.
//
// WO-865 FIXED-PIXEL BANDS still own the outer chrome (action / ability / detail).
// The graph itself is a free-form scroll canvas on authored x/y (auto-layout when
// unset) so nodes never pack into a spreadsheet.
//
// Code-built uGUI ONLY (no UXML — §8). Registers PanelId.HeroSkillTree.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;
using static DeNelle.Core.UI.LocalText;

namespace DeNelle.Village.Talents
{
    [DisallowMultipleComponent]
    public sealed class HeroSkillTreePanelMvvm : MonoBehaviour, IPanelView
    {
        private HeroSkillTreeVM _vm;

        private GameObject _ui;
        private RectTransform _graphContent;     // fixed-size scroll content (nodes + edges live here)
        private TMPro.TextMeshProUGUI _headerLabel;
        private TMPro.TextMeshProUGUI _wisdomLabel;
        private RectTransform _quickSwapHost;
        private TMPro.TextMeshProUGUI _quickSwapStatus;

        // Spend popup (owner 2026-08-15): shown on node tap; Confirm spends, Cancel dismisses.
        // Chrome is BuildObsidianPanel (common kit frame + gold border) — never a bare plate.
        private GameObject _popupRoot;
        /// <summary>The popup's gold-trim CARD (chrome.root). Held so WO-1522 can re-anchor it
        /// BESIDE the selected node instead of over the middle of the tree.</summary>
        private RectTransform _popupCard;
        private GameObject _popupConfirmRing;
        private TMPro.TextMeshProUGUI _popupName;
        private TMPro.TextMeshProUGUI _popupDesc;
        private TMPro.TextMeshProUGUI _popupPrompt;
        private Button _popupConfirmBtn;
        private TMPro.TextMeshProUGUI _popupConfirmLabel;
        private Button _popupCancelBtn;

        private PanelHandle _panelHandle;

        // -- OWNER VFX PICKS 2026-08-16 (both mapped verbatim, both ADDITIVE) --------
        //   POINTER (Hovl "Marker 2 Pointer Loop") -> the single FOCUS (next/selected)
        //     node, behind the code-built gold focus ring.
        //   AURA (Aura_TalentNode, tracked under Resources/VFX/Aura) -> node AURAS.
        //     DEFAULT (NOT A LOCK): the aura lights OWNED/learned nodes - the prestige
        //     read on talents already taken. One owner word flips it to available-to-buy
        //     nodes: change IsAuraNode below.
        // Each pick = one shared off-screen rig (TalentNodeVfxRig: one instance + one
        // RenderTexture, rendered once per frame); every node patch is a RawImage
        // SAMPLING the shared texture, so 10+ owned nodes cost 10 quads, not 10 systems.
        // A missing asset Warns once; the code-built node art stands alone either way.
        private TalentNodeVfxRig _pointerVfx;
        private TalentNodeVfxRig _auraVfx;
        private bool _pointerVfxUnavailable;      // Begin failed once - do not retry per repaint
        private bool _auraVfxUnavailable;
        private string _lastPointerSig;           // change-gated follow trace (sec.12: no per-Render spam)
        private int _lastAuraCount = -1;          // change-gated aura attach-count trace

        // =====================================================================
        // WO-865 FIXED-PIXEL BAND GEOMETRY (reference px on the 1080x1920 modal
        // canvas). NEVER a fraction of the parent -- that is the documented root
        // cause of WO-841 / WO-852 and of every defect in the 2026-08-04 Seeker
        // capture (docs/ui-review/2026-08-04-seeker/07-skills-panel.png).
        //
        // PROVING ARITHMETIC (2340x1080, the capture's device):
        //   scaler 1080x1920 match 0.5 -> scale 1.1040 -> canvas local 2119.6x978.3
        //   panel 0.07,0.05-0.93,0.95  -> 1822.9 x 880.5
        //   FrameTalent body after the shared-Close band reservation -> 1695 x 493 px.
        //   The OLD footer band was 0.070..0.135 OF THAT BODY = 32 px. The kit touch
        //   floor (ElarionUiKit.ClampMinTouch, MinTouchPx 112) then grew every action
        //   button SYMMETRICALLY ABOUT ITS CENTRE by +-40 px, so its top reached
        //   106.6 px while the graph well's floor sat at 0.165*493 = 81.4 px. That
        //   25 px of growth is why Cancel / CONFIRM / Respec painted OVER the grid and
        //   over quick-slots 3 and 4, and why slot 4 vanished under Respec entirely.
        //
        // Every band below is a FIXED pixel height >= its own touch/line-box floor, so
        // the stack is disjoint by construction at ANY aspect ratio. The only
        // fraction left is the LEFT/RIGHT column split (a width, never a text band).
        // =====================================================================

        /// <summary>Inset from the body well's edges (ref px).</summary>
        public const float BodyPadPx = 6f;
        /// <summary>Gap between two stacked fixed-pixel bands (ref px).</summary>
        public const float BandGapPx = 8f;
        /// <summary>The bottom action row (Cancel / Respec / CONFIRM). Buttons are tappable, so
        /// the band IS the kit touch floor -- ClampMinTouch can then never grow one past it.</summary>
        public const float ActionRowPx = ElarionUiKit.MinTouchPx;
        /// <summary>The ability (quick-swap) band. Slots are tap targets, so this is >= the touch
        /// floor; it is a little taller than the floor because the tile stacks TWO fixed line
        /// boxes (slot numeral + a two-line ability name).</summary>
        public const float AbilityRowPx = 132f;

        // ---------------------------------------------------------------------
        // WO-1401 QUICK-SWAP RAIL: TWO FIXED-PX BANDS, DISJOINT BY CONSTRUCTION.
        // Builds/ui-capture.log 2026-09-05 05:13 read BUTTON OVER TEXT x9 on this
        // panel: ObsBtn_1..3 (y 0..112 of the rail - the slot LayoutElement is
        // MinTouchPx and the layout group seats it LOWER) covered QuickSwapHint by
        // exactly 112x9 at all three aspects, because the hint was a FRACTION of the
        // rail (0.78..1 of 132 = y 103..132). A fraction band beside a fixed band is
        // the WO-841/852/865 failure class named above. Every number below is fixed
        // ref px; the hint is pinned FROM THE TOP, the slots seat at the BOTTOM, and
        // the gap between them is BandGapPx for ANY rail width or screen aspect.
        // Pinned by SkillsPanelLayoutRegression case 7 [rail] on the REAL builder.
        // ---------------------------------------------------------------------

        /// <summary>The slot band: the three quick-swap buttons ARE this tall (their LayoutElement
        /// min/preferred, see <see cref="ApplyQuickSwapSlotSize"/>), so the band is the kit touch
        /// floor itself and ClampMinTouch can never grow one past it.</summary>
        public const float QuickSwapSlotBandPx = ElarionUiKit.MinTouchPx;
        /// <summary>The hint band above the slots: one TMP line box at the kit FontFloor
        /// (30 x 1.25 = 37.5) with air; FontMicro 32 fits without ellipsis.</summary>
        public const float QuickSwapHintBandPx = 40f;
        /// <summary>The whole rail = slots + gap + hint. The layout group's top padding restates the
        /// same sum so its own arithmetic seats the slots exactly in the slot band.</summary>
        public const float QuickSwapRailPx = QuickSwapSlotBandPx + BandGapPx + QuickSwapHintBandPx;
        /// <summary>Where the rail's bottom edge sits above the workspace floor.</summary>
        public const float QuickSwapRailBottomPx = BodyPadPx + BandGapPx;
        /// <summary>The graph well's floor: one gap ABOVE the rail's top, so a node plate (a Button)
        /// can never sit on the hint. Derived, never retyped.</summary>
        public const float GraphWellFloorPx = QuickSwapRailBottomPx + QuickSwapRailPx + BandGapPx;

        /// <summary>Default talent plate size (RPG north star: large enough that skill art
        /// reads at a glance). Always >= kit MinTouchPx so nodes stay tappable.</summary>
        public const float NodeSizePx = 136f;

        /// <summary>Right column: the WISDOM currency chip band.</summary>
        public const float WisdomBandPx = 52f;

        // ---------------------------------------------------------------------
        // WO-1410 THE WISDOM CHIP WAS TOO NARROW FOR ITS OWN PINNED SENTENCE.
        // Headless frame Builds/ui-capture/HeroSkillTree_2670x1200.png (22:54)
        // read "WISDOM 0 - next point ..." -- ELLIPSIZED, not culled.
        //
        // WHY, measured (no guess):
        //  - FitSingleLine(_wisdomLabel, 15f, 20f) does NOT render at 15. The kit
        //    CLAMPS minSize UP to ElarionUiKit.FontHardFloor = 20
        //    (ElarionUiKitObsidian.cs:3062-3064), so min == max == 20 and the label
        //    has NO shrink left: too little width means ellipsis, full stop.
        //  - Canvas units (BuildModalCanvas: 1080x1920, match 0.5):
        //      2670x1200 -> scale sqrt((2670/1080)*(1200/1920)) = 1.243 -> 2148 units wide
        //      1920x1080 -> scale 1.0                                  -> 1920 units wide
        //    So 1080p is the NARROW case and sizing must be proven there.
        //  - Width chain: chrome.root = 0.86 of canvas; chrome.content = 0..1 of it;
        //    TalentWorkspace = 0.93 of content. 1080p panel = 0.86*0.93*1920 = 1535.6 u.
        //  - The retired plate was 0.80..0.98 (0.18) with the label at 0.10..0.90:
        //    plate 276.4 u, label 221.1 u at 1080p. The capture's own run calibrates
        //    the bold face at ~0.54 em/char at 20 px (22 chars + ellipsis in the
        //    Seeker label's 247.4 u), so the widest LIVE sample from Render() --
        //    "WISDOM 999 - next point at Level 100", 36 chars -- needs ~396 u.
        //    221 u could not hold even the 3-word prefix. That is the whole defect.
        //
        // THE FIX IS WIDTH, NOT COPY: "next point at Level" is pinned by
        // SkillsPanelLayoutRegression (source law, ~:603) and WisdomBandPx by its
        // LineFloor case (~:258) -- neither moves. The plate grows LEFTWARDS (the
        // column's free side) and the label reclaims the plate's own side inset.
        // ---------------------------------------------------------------------

        /// <summary>WISDOM chip: LEFT anchor, fraction of the talent workspace. Grown from 0.80.
        /// 0.35 of the panel = 537.5 u at 1080p / 601.3 u at 2670x1200 -- against the ~396 u the
        /// widest live sentence needs at 20 px, that is ~22% headroom on the NARROW aspect.
        /// Widening leftwards is free: the graph well is already shortened across the FULL panel
        /// width by (WisdomBandPx + BandGapPx), so no node plate can be covered.</summary>
        public const float WisdomPlateAnchorX0 = 0.63f;
        /// <summary>WISDOM chip: RIGHT anchor. UNCHANGED -- the chip stays pinned to the column's
        /// right edge, inside the frame, and vertically disjoint from the EQUIPMENT button (that
        /// button lives on chrome.root at y 0.84..0.98; the workspace TOPS OUT at 0.84).</summary>
        public const float WisdomPlateAnchorX1 = 0.98f;
        /// <summary>Side inset of the chip's label inside its plate. 0.05 (was 0.10) returns 10%
        /// of the plate to the sentence. Safe: the plate art is content-panel drawn
        /// Image.Type.Simple, whose side margin measures ~1.2% of its 1672 px width
        /// (cf. PopupContentSideInsetPx = 20), so 5% still clears the painted pilaster.</summary>
        public const float WisdomLabelInsetX = 0.05f;
        /// <summary>Right column: the "SELECTED TALENT" caption band (FontMicro 32 -> line ~40).</summary>
        public const float DetailHeadPx = 40f;
        /// <summary>Right column: the talent NAME band (FontTitle, fitted; line box floor 40).</summary>
        public const float DetailNamePx = 60f;
        /// <summary>Right column: the state / "Requires ..." band (FontMicro 32 -> line ~40).</summary>
        public const float DetailStatePx = 42f;

        // =====================================================================
        // WO-1342 SPEND-POPUP GEOMETRY (device capture 2026-09-03, Seeker
        // 2670x1200, `Mend` tapped). Two DISTINCT defects, both numeric, both
        // pinned by SkillsPanelLayoutRegression case 6 [popup]:
        //
        // (a) THE DESCRIPTION LOST HALF ITS SENTENCE. The authored string is
        //     "Unlocks Mend - a small self-heal (25 HP, 12s cd). Assignable to
        //     the hot-swap bar." and the device rendered it up to "Assignable
        //     to" with NO ellipsis. Not a wrap bug -- FitBlock already wraps
        //     (textWrappingMode Normal); a HEIGHT bug. The desc band was
        //     0.48..0.90 of a body zone that resolves to ~149 local units, i.e.
        //     ~63 units of label, which seats TWO line boxes at the 24 px floor.
        //     RenderSpendPopup prepends the gold talent name + "\n", so line 1
        //     is the name, line 2 is the first wrapped line, and TMP's Truncate
        //     overflow CULLS line 3 SILENTLY (Truncate draws no "..."). The
        //     dialog had ~56 units of authored EMPTY band (0.34..0.48 and
        //     0.00..0.16) sitting under it the whole time. Fix = give the
        //     description the room, do not shrink and do not ellipsize.
        //
        // (e) THE FRAME DID NOT ENCLOSE THE MODAL (owner: "the frame around the
        //     modal"). The gold border and the black plate are the SAME rect --
        //     the frame is ONE 9-sliced sprite on chrome.root
        //     (MedievalUiSkin.ApplyShell, "UI/ElarionMedieval/frames/content-panel",
        //     Image.Type.Sliced, border 96/96/96/96) and the plate is the kit's
        //     ZoneBacking(layout.body, ObsidianFill) at Zone_Body fractions of
        //     that same rect, so enclosure looks guaranteed. It is not, because
        //     THE ART DOES NOT PAINT AT THE RECT'S TOP EDGE: content-panel.png
        //     is 1672x941 whose alpha bbox starts at row 94 -- the ENTIRE 96 px
        //     top slice is transparent, so the gold top edge paints ~96 units
        //     BELOW the rect top. Zone_Body's top (0.835) sits 0.165 * ~356
        //     units = ~59 units below the rect top. 59 < 96, so the black plate
        //     began ~37 units (~46 device px) ABOVE the painted frame -- the
        //     capture measures the plate top at y=472 and the gold top edge at
        //     y=517, a 45 px overhang. That is the whole defect, and it is
        //     INDEPENDENT of (a): nothing about the text drives it, and growing
        //     the popup cannot fix it (0.165 * H >= 96 needs H >= ~582 units,
        //     i.e. essentially the whole workspace).
        //     Fix = inset chrome.content by the frame's PAINTED margin, so every
        //     zone the factory hangs off it -- header, body (and its backing
        //     plate), footer -- lands inside the visible border. No rect is
        //     renamed or re-parented (WO-1340 highlights resolve by name).
        //
        // ⛔ The tree's own solver / lattice / node plates are WO-1310's lane and
        //    are NOT touched by any constant below.
        // =====================================================================

        /// <summary>Spend-popup rect, fraction of the talent workspace. Grown from the
        /// original 0.20..0.80 so the wrapped description has somewhere to go AFTER the
        /// frame-inset below eats the top 96 units.</summary>
        public const float PopupAnchorY0 = 0.10f;
        /// <inheritdoc cref="PopupAnchorY0"/>
        public const float PopupAnchorY1 = 0.90f;

        /// <summary>WO-1522 - the popup card's TWO seats, LEFT and RIGHT of the workspace.
        /// The card used to be pinned at x 0.24..0.76: dead centre, over the tree, and in
        /// owner-screen-20260906-202355.png it covers the node the player just tapped, so the
        /// thing being decided about is hidden behind the decision. It is now anchored to the
        /// half OPPOSITE the selected node (RenderSpendPopup -> AnchorPopupBesideNode).
        /// Width is 0.48 (was 0.52); the two seats do not overlap, which is what makes
        /// "never covers the node" a property of the anchors rather than of luck.</summary>
        public const float PopupSideLeftX0 = 0.02f;
        /// <inheritdoc cref="PopupSideLeftX0"/>
        public const float PopupSideLeftX1 = 0.50f;
        /// <inheritdoc cref="PopupSideLeftX0"/>
        public const float PopupSideRightX0 = 0.50f;
        /// <inheritdoc cref="PopupSideLeftX0"/>
        public const float PopupSideRightX1 = 0.98f;

        /// <summary>MEASURED from the art, not guessed: content-panel.png (1672x941, 9-slice
        /// border 96) has 94 fully transparent rows above its gold border, so its 96 px TOP
        /// slice paints nothing and the visible frame starts this many units below its rect's
        /// top edge. Any content anchored to the rect's own top overhangs the border by
        /// exactly (this - the zone's own top gap).</summary>
        public const float PopupFrameArtTopMarginPx = 96f;

        /// <summary>Inset applied to the popup's content layer so the factory zones sit inside
        /// the PAINTED frame. Must be >= <see cref="PopupFrameArtTopMarginPx"/> (case 6 pins it);
        /// the art reaches its own bottom edge, so only the top needs the full margin.</summary>
        public const float PopupContentTopInsetPx = 96f;
        /// <summary>Side inset for the popup content layer -- the frame's left/right border art
        /// carries ~20 units of transparent margin before the gold pilaster.</summary>
        public const float PopupContentSideInsetPx = 20f;
        /// <summary>Bottom inset for the popup content layer (the art paints to its bottom edge).</summary>
        public const float PopupContentBottomInsetPx = 8f;

        /// <summary>Description band, fraction of the popup BODY zone. Takes the whole upper
        /// body (the old 0.48..0.90 left 0.34..0.48 and 0.00..0.16 authored EMPTY while the
        /// sentence was being culled).</summary>
        public const float PopupDescBandY0 = 0.30f;
        /// <inheritdoc cref="PopupDescBandY0"/>
        public const float PopupDescBandY1 = 1.00f;
        /// <summary>State / spend-prompt band, fraction of the popup BODY zone. Disjoint from
        /// the description band by construction.</summary>
        public const float PopupPromptBandY0 = 0.02f;
        /// <inheritdoc cref="PopupPromptBandY0"/>
        public const float PopupPromptBandY1 = 0.26f;

        /// <summary>Description auto-size range. EXPLICIT floor: FitBlock's minSize:0 silently
        /// resolves to ElarionUiKit.FontFloor (30), NOT FontHardFloor (20).</summary>
        public const float PopupDescFontMin = 24f;
        /// <inheritdoc cref="PopupDescFontMin"/>
        public const float PopupDescFontMax = 30f;
        /// <summary>State-line auto-size range (explicit floor, same trap).</summary>
        public const float PopupPromptFontMin = 22f;
        /// <inheritdoc cref="PopupPromptFontMin"/>
        public const float PopupPromptFontMax = 28f;
        /// <summary>The description must seat at least this many whole line boxes at its own
        /// floor: 1 for the prepended gold talent name + 3 for the longest authored sentence.</summary>
        public const int PopupDescMinLineBoxes = 4;

        /// <summary>Thickness of ONE bar of the confirm button's emphasis outline, in px.
        /// The outline is four bars, never a fill: a filled overlay on a Button draws ABOVE
        /// the button's own ornate plate (a parent Graphic renders before its children), which
        /// is the flat gold slab the owner reported on 2026-09-03.</summary>
        public const float ConfirmOutlinePx = 4f;
        /// <summary>Inset of that outline from the confirm button's own edges, in px. It must
        /// stay STRICTLY POSITIVE: the retired overlay grew -5/+5 OUTSIDE the rect and spilled
        /// toward the popup frame. Emphasis is drawn inside the control, never past it.</summary>
        public const float ConfirmOutlineInsetPx = 3f;

        /// <summary>Ability tile: the slot numeral line box -- a WHOLE line box at the kit
        /// FontFloor (30 x 1.25 = 37.5), never the 34 px that only just misses it.</summary>
        public const float SlotKeyBandPx = 38f;
        /// <summary>Ability tile: the ability-name box -- TWO whole FontFloor line boxes, because
        /// the longest catalog name ("Suppressing Volley") needs to wrap to read in full inside a
        /// ~250 px tile. The old band was 23 px (0.5 of a 47 px fraction tile), i.e. below ONE
        /// line box, which is what ellipsized "Emberbrand Throw" to "Emberbrand Thro".</summary>
        public const float SlotNameBandPx = 80f;
        /// <summary>Ability tile: pad above the numeral / below the name (ref px).</summary>
        public const float SlotPadPx = 6f;

        /// <summary>A section label's own line box (FontMicro+2 -> line ~44).</summary>
        public const float SectionBandPx = 46f;
        /// <summary>Clear air above and below a section label.</summary>
        public const float SectionGapPx = 14f;
        /// <summary>The MINIMUM clear pitch a node row must own: one node plate plus a label
        /// band plus air on both sides. WO-865 introduced it to stop a plate painting over the
        /// "Universal - any class" divider; WO-896 keeps it as the FLOOR every track row is
        /// clamped to in RebuildTracks, so a row can never squeeze its plate against its name.</summary>
        public const float SectionClearPx = NodeSizePx + SectionGapPx * 2f + SectionBandPx;

        // -- GRAPH LATTICE -- LIVE (CORRECTED 2026-08-16; do not trust the old note). The
        // banner that stood here since WO-896 called these four constants a "RETIRED LATTICE"
        // and claimed "NOTHING in this file positions a node from them any more". That was
        // FALSE of the code as shipped: RebuildGraph places every plate centre at
        // GraphPadPx + norm * GraphUnitWpx/Hpx (the centres loop below, and the empty-tree
        // sizing), so GraphUnitWpx / GraphUnitHpx / GraphPadPx ARE the px-per-unit lattice the
        // authored hero-talents.json x/y map through. Trusting the retired-lattice claim is
        // exactly how the 2026-08-16 node-overlap defects were mis-read as authoring-only.
        // SkillsPanelLayoutRegression [grid] pins these against the canonical json (full
        // pairwise AABB since 2026-08-16).

        /// <summary>Live lattice: reference px per 1.0 of authored node X (see the note above).</summary>
        public const float GraphUnitWpx = 1180f;
        /// <summary>Live lattice: reference px per 1.0 of authored node Y.</summary>
        public const float GraphUnitHpx = 780f;
        /// <summary>Live lattice: content padding of half a node plate plus air.</summary>
        public const float GraphPadPx = NodeSizePx * 0.5f + 16f;
        /// <summary>History: authored Y the "Universal - any class" band divided the graph at.
        /// The WO-896 push-apart that consumed it is deleted; kept only as a stable const for
        /// any downstream reader — no layout or oracle math reads it since 2026-08-16.</summary>
        public const float SectionBandY = 0.965f;

        // ── WO-896 SPARSE GRAPH LATTICE (Obsidian demo north star) ─────────────────
        // Nodes sit on an authored 0..1 canvas (hero-talents.json x/y). GraphUnit* maps
        // that canvas to fixed reference pixels so the lattice still clears a full node
        // plate (SkillsPanelLayoutRegression [grid] pins this). Unset nodes auto-layout
        // from tier/column without packing into a dense grid.

        /// <summary>FOCUS plate — the BOARD-LEVEL SELECTION only (never "next"; see the header
        /// correction). Oversized gold frame, demo/RPG feel. At most ONE plate on the board is
        /// ever built at this size; TalentFocusSingletonRegression pins that.</summary>
        public const float NodeFocusPx = 168f;

        // ── WO-1021 sec 2.1b — PITCH + SEPARATION LAW (owner 2026-08-15/16: "needs better
        // spacing logic", measured plate overlaps + cost pips landing on the NEIGHBOURING
        // plate in the 2026-08-16 Seeker captures). Clearance is computed from the FOCUS
        // size, never from NodeSizePx: a focused plate grows about its own centre, so a
        // 136-based clearance lets it grow straight into its neighbour.

        /// <summary>Minimum centre-to-centre pitch (Chebyshev: max(|dx|,|dy|)) between ANY two
        /// plates, in ref px. Chebyshev >= P implies Euclidean >= P, so this is the STRONGER
        /// reading of "centre-to-centre pitch" and can never certify a touching pair.</summary>
        public const float MinNodePitchPx = NodeFocusPx * 1.35f;      // 226.8
        /// <summary>How far the solver may stretch pitch to fill a tall/wide well before it
        /// stops. Past this a node's nearest neighbour reads as STRANDED (WO-1021 sec 2.1b
        /// "no stranded nodes" — the bottom-centre orphan in the owner's capture).</summary>
        public const float MaxPitchSpreadMul = 1.9f;
        /// <summary>Lattice floor: a box that can always seat two plates at the minimum pitch,
        /// so a well that has not been laid out yet still produces a legal (scrolling) board
        /// instead of crushing every plate onto one point.</summary>
        public const float MinLatticeWpx = NodeFocusPx + MinNodePitchPx;
        public const float MinLatticeHpx = NodeFocusPx + MinNodePitchPx;
        /// <summary>Authored/auto Y values within this band collapse to ONE ROW. Authored y is
        /// an ORDERING HINT consumed by the solver, not final geometry (WO-1021 sec 2.1b).</summary>
        public const float RowClusterNorm = 0.055f;

        // -- WO-1310 (owner 2026-09-02: "tree looks wrong", "there should be a starting point
        // ... they should visually be in same level; one is middle of a skill tree").
        //
        // WHAT WAS WRONG: the solver consumed the authored norms for SORT ORDER ONLY. Each row
        // was then laid out independently and centred, so a plate's x was its INDEX WITHIN ITS
        // OWN ROW and nothing else - a one-node row landed dead centre regardless of where it
        // sits in the progression, and every row collapsed into a narrow centred column that
        // left the right half of the well empty. The authored magnitudes were thrown away.
        //
        // WHAT IS TRUE NOW: the PROGRESSION norm picks a BOARD-WIDE COLUMN, so two nodes that
        // read as the same step of progression land in the same column on every row. That is
        // exactly the "starting point on one level" the owner asked for, and it is the same
        // property TalentTreeShapeRegression rule 2 [base] enforces in the data.

        /// <summary>Progression values within this band collapse to ONE COLUMN, board-wide.
        /// Same tolerance as <see cref="RowClusterNorm"/>, applied to the other axis.</summary>
        public const float ColClusterNorm = 0.055f;

        /// <summary>How far below its plate the hung nameplate reaches, as a fraction of the
        /// plate size. MUST match BuildNodeNamePlate's anchorMin.y magnitude: the row pitch and
        /// the content inset are both derived from it, so a nameplate can never be sliced by
        /// the mask nor painted over the plate on the row below.</summary>
        public const float NamePlateHangFrac = 0.62f;

        /// <summary>The footprint one plate actually occupies, hung nameplate included. Every
        /// content inset is half of THIS, never half of NodeFocusPx - a focus plate also carries
        /// a BuildOuterRing(0.10) glow, so a NodeFocusPx inset clips the top row's ring and the
        /// bottom row's nameplate at the RectMask2D edge (the WO-1310 top-clip capture).</summary>
        public const float PlateClearPx = NodeFocusPx * 1.30f;              // 218.4

        /// <summary>Minimum ROW pitch: half a focus plate, plus the nameplate hung under it,
        /// plus half the plate on the row below, plus breathing room. Strictly greater than
        /// MinNodePitchPx, so the WO-1021 Chebyshev pitch law is still met - this TIGHTENS it.</summary>
        public const float MinRowPitchPx = NodeFocusPx * 1.80f;             // 302.4

        /// <summary>Minimum COLUMN pitch: the nameplate is 1.56 plate-widths wide, so two plates
        /// at the bare MinNodePitchPx would have overlapping NAMES even with clear art. Also
        /// strictly greater than MinNodePitchPx.</summary>
        public const float MinColPitchPx = NodeFocusPx * 1.60f;             // 268.8

        /// <summary>Earned path (both ends owned/planned) — solid gold, demo-thick.</summary>
        public const float ConnectorThickPx = 10f;
        /// <summary>Live frontier (parent owned, child not) — solid gold, slightly thinner.</summary>
        public const float ConnectorMidPx = 8f;
        /// <summary>Not-yet-earned link — still SOLID gold (RPG trees always show structure),
        /// only dimmer. Dashed edges read as "broken UI", not "locked progression".</summary>
        public const float ConnectorThinPx = 6f;

        /// <summary>How far a connector stops short of each plate centre so the line
        /// meets the plate rim rather than running under the icon.</summary>
        public const float ConnectorInsetFrac = 0.42f;

        /// <summary>Rank pip ("1/1") band seated on the TOP of every plate — demo look.</summary>
        public const float RankBandPx = 28f;

        // Track-row leftovers kept so SkillsPanelLayoutRegression / older names stay
        // resolvable; the live layout never builds track rows.
        public const float TrackTitleWpx = 0f;
        public const float NodePitchPx = 200f;
        public const float NodeNextPx = NodeFocusPx;
        // WO-1310: these two are DEAD track-row leftovers and are NOT the nameplate reservation.
        // A reader who took them at face value concluded the pitch law reserved zero height for
        // the hung nameplate; the live reservation is NamePlateHangFrac -> MinRowPitchPx.
        public const float NodeLabelBandPx = 0f;
        public const float NodeLabelGapPx = 0f;
        public const float TrackTopPadPx = 10f;
        public const float TrackRowPx = NodeSizePx + TrackTopPadPx * 2f;
        public const float TrackGapPx = 24f;
        public const int TrackWrapCount = 4;
        public const float ContentPadPx = GraphPadPx;
        public const float TrackKindBandPx = 38f;
        public const float TrackNameBandPx = 76f;
        public const float TrackNoteBandPx = 38f;

        // =====================================================================
        //  WO-1522 - THE ELEVATION LADDER ("the loadout screen feels very flat")
        // =====================================================================
        // The owner is red/green colourblind, so DEPTH IS NOT A HUE HERE. It is carried by the
        // three things that survive a greyscale capture: LUMINANCE (each tier is a measurably
        // different grey), SIZE (the focus plate is NodeFocusPx against NodeSizePx), and
        // POSITION (recessed well in the middle, raised rail along the floor).
        //
        // The screen used to have exactly ONE surface: the graph viewport painted a near-black
        // slab with no edge, the loadout slots floated straight on the panel's own frame fill,
        // and the wisdom plate was the only thing with a border. Content at three depths, one
        // apparent plane - which is "flat" precisely.
        //
        // TWO AUTHORED SURFACES, NOT THREE, and that is deliberate. The middle tier - the ground
        // both of these sit on - is the frame's own textured centre, which MedievalUiSkin
        // .ApplyShell reveals by clearing the legacy opaque inner fill to alpha 0. Authoring a
        // third plate for it would either be invisible (the graph viewport covers that rect
        // entirely) or would re-cover the very art ApplyShell uncovers. A constant for a surface
        // nobody can see is a hollow assertion, so there isn't one.
        //
        // STOP: these are PUBLIC and they are the ONE authority for the ladder:
        // SkillsPanelLayoutRegression [elevation] reads them and asserts the Rec.709 luma gap, so
        // a future edit that quietly moves the two tiers together fails the gate instead of
        // shipping a flat screen again. Do not re-hardcode either at a call site.

        /// <summary>The RECESSED graph well. The darkest surface on the screen: content sits
        /// INSIDE it, so it reads as a hole in the panel rather than a sheet on top of it.</summary>
        public static readonly Color WellSurface = new Color(0.018f, 0.016f, 0.022f, 1f);

        /// <summary>The RAISED loadout shelf, along the screen's floor - the lightest surface, so
        /// the assigned-skills strip reads as a shelf carrying the medallions rather than four
        /// buttons adrift on the background.</summary>
        public static readonly Color RaisedSurface = new Color(0.115f, 0.105f, 0.125f, 1f);

        /// <summary>The minimum Rec.709 luma step between two ADJACENT rungs. The well and the
        /// shelf are two rungs apart (the frame's own centre is between them), so the suite
        /// asserts twice this between them. Below it the ladder stops being visible in a
        /// greyscale capture, which is the only test that matters for this owner.</summary>
        public const float ElevationLumaStep = 0.02f;

        /// <summary>Bezel inset, ref px: how far the well's gold edge is drawn outside the
        /// plate it frames. An edge is what turns a dark rectangle into a recess.</summary>
        public const float WellBezelInsetPx = 6f;

        // ---------------------------------------------------------------------
        // WO-1601 (a) THE BEZEL ART. Owner frame
        // Logs/device/seeker-shots/Screenshot_20260907-132616.png read a full-width ornate
        // gold band drawn STRAIGHT ACROSS the middle of the tree, over ARCANE BOLT and its
        // connectors, while the loadout shelf below carried NO edge at all. Two symptoms,
        // ONE cause, and it is a property of the ART, measured from the file - not a guess:
        //
        //   card-frame-empty.png is 1774x887 with spriteBorder {96,96,96,96}. Its PAINTED
        //   alpha bounding box is rows 165..713 and cols 16..1757. So:
        //     * the TOP 96-row slice (rows 0..96) is 100% TRANSPARENT - 165 > 96;
        //     * the BOTTOM 96-row slice (rows 791..887) is 100% TRANSPARENT - 713 < 791.
        //   Image.Type.Sliced draws those two strips at the rect's top and bottom edges and
        //   STRETCHES everything between them. The only rows that carry the horizontal rails
        //   therefore land INSIDE the rect, at (165-96)/695 = 9.9% and (713-96)/695 = 88.8%
        //   of the stretched middle. Over the graph well that is a band across the nodes;
        //   over the 172 px shelf bezel the 192 px of border cannot fit at all, Unity shrinks
        //   both strips to 86 px, the middle slice collapses to ZERO height and the rails are
        //   not drawn at all. Predicted rails for the device well (306.5..812.5 device px):
        //   452 / 663; MEASURED in the owner's frame: 449 / 655. That is the proof.
        //
        //   NO RECT CAN FIX IT: the painted top rail sits at 96 + 0.099*(H-192) from the rect
        //   top for every H, so it is never nearer than 96 px to the edge it is meant to be.
        //
        // THE FIX IS THE SPRITE. Of the six frames in Resources/UI/ElarionMedieval/frames,
        // modal-frame-16x9.png (1672x941, border 96) is the ONLY one whose painted alpha
        // actually occupies all four border strips - rows 0..927, cols 11..1660 - so its
        // 9-slice corners and rails land ON the rect edge at any height, including the 172 px
        // shelf (Unity's proportional border shrink still draws real art there). It is the
        // same shell art MedievalUiSkin.ApplyShell hangs on every non-compact panel, so it is
        // already resident; it is hollow (alpha 0 interior), so it still frames without
        // covering. card-frame-empty is NOT edited: PlayerDeckWorkspace.cs:206 and
        // DefenseReportPanel.cs:853 still use it, and TextureImportBudgetRegression pins its
        // dimensions.
        //
        // STOP: this is a PUBLIC const because SkillsPanelLayoutRegression [bezel-art] loads
        // THIS path's PNG and re-measures the alpha strips. Never inline the string.
        public const string BezelFrameResource = "UI/ElarionMedieval/frames/modal-frame-16x9";

        // ---------------------------------------------------------------------
        // WO-1601 (b) FIT THE BOARD INTO THE WELL. Same frame: the 0/1 medallions at both
        // ends of the tree were sliced by the RectMask2D. Measured from the shipped solver at
        // 2670x1200 (well 1705x395 ref px, seven solved columns at MinColPitchPx):
        //   contentW = 1915 ref px against a 1705 px well -> the seventh column's plate spans
        //   1654..1790 and the mask cuts it at 1705 (device x 2394; the frame's cut reads at
        //   2404). contentH = 633 against 395 -> the second row is halved.
        // The board is SCALED to fit instead of being left to overflow at the rest position.
        // Scale, not a re-solve: SolveGraphLatticePx owns pitch and its laws are pinned by
        // [grid]; nothing here may move a centre.
        // ---------------------------------------------------------------------

        /// <summary>How far the board may shrink before a node stops being a legal tap target.
        /// The plate is <see cref="NodeSizePx"/> and the kit floor is MinTouchPx, so this is the
        /// ONE derivation - never a literal. Past it the remainder is reached by scrolling
        /// (WO-896: "sparse graph + scroll is the product").</summary>
        public const float MinGraphFitScale = ElarionUiKit.MinTouchPx / NodeSizePx;   // 112/136

        /// <summary>WO-1601. The board's fit scale: uniform, never above 1 (a board smaller than
        /// the well is never blown up - the solver already centres it), never below
        /// <see cref="MinGraphFitScale"/>. Pure and public so SkillsPanelLayoutRegression [fit]
        /// runs the SHIPPED function rather than a copy of the arithmetic.</summary>
        public static float ResolveGraphFitScale(float contentW, float contentH,
                                                 float wellW, float wellH)
        {
            if (contentW <= 1f || contentH <= 1f || wellW <= 1f || wellH <= 1f) return 1f;
            float s = Mathf.Min(wellW / contentW, wellH / contentH);
            if (float.IsNaN(s) || float.IsInfinity(s)) return 1f;
            return Mathf.Clamp(s, MinGraphFitScale, 1f);
        }

        // ── The only FRACTIONS left in the layout: the column split and the in-tile text
        // insets. Both are WIDTHS, never a text band's height -- the class of failure the
        // fixed-pixel law exists to stop is a band too short for its own line box.
        /// <summary>Right edge of the graph column, as a fraction of the columns region.</summary>
        public const float GraphColumnX1 = 0.615f;
        /// <summary>Left edge of the detail column, as a fraction of the columns region.</summary>
        public const float DetailColumnX0 = 0.640f;
        /// <summary>Gap between two ability slot tiles, as a fraction of the ability band.</summary>
        public const float SlotGapFrac = 0.012f;
        /// <summary>Side inset of the ability-name label inside its tile (fraction of the tile).</summary>
        public const float SlotTextInsetFrac = 0.03f;
        /// <summary>Side inset of every label in the detail column (fraction of the column).</summary>
        public const float DetailTextInsetFrac = 0.03f;

        // Last drawn layout signature — §12 instrumentation logs a line only when the drawn
        // shape CHANGES, never once per Render (Render fires on every selection tap).
        private string _lastLayoutSig;

        public bool IsOpen => _ui != null;

        // ── fixed-pixel band pins (the WO-832 §4 / WO-841 / WO-852 pattern) ──────
        // Re-hang a control on its parent's TOP or BOTTOM edge with a FIXED ref-pixel
        // band; X anchors/offsets are preserved. A band pinned this way never scales
        // with the pane and can never under-height its own line box.

        private static void PinBandFromTop(RectTransform rt, float topPx, float heightPx)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(rt.anchorMin.x, 1f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
            rt.offsetMin = new Vector2(rt.offsetMin.x, -(topPx + heightPx));
            rt.offsetMax = new Vector2(rt.offsetMax.x, -topPx);
        }

        private static void PinBandFromBottom(RectTransform rt, float bottomPx, float heightPx)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 0f);
            rt.offsetMin = new Vector2(rt.offsetMin.x, bottomPx);
            rt.offsetMax = new Vector2(rt.offsetMax.x, bottomPx + heightPx);
        }

        /// <summary>Stretch a control between FIXED pixel insets from its parent's top and bottom
        /// edges. The flexible middle of a band stack -- its extent is still decided by pixels.</summary>
        private static void PinRegion(RectTransform rt, float bottomPx, float topPx)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(rt.anchorMin.x, 0f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
            rt.offsetMin = new Vector2(rt.offsetMin.x, bottomPx);
            rt.offsetMax = new Vector2(rt.offsetMax.x, -topPx);
        }

        /// <summary>A transparent full-width layout host under <paramref name="parent"/>, spanning
        /// the x fractions given. Vertical seat is set afterwards by one of the pins above.</summary>
        private static RectTransform BandHost(Transform parent, string name, float x0, float x1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(x0, 0f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        // ── Registration (mirror BuildingUpgradePanelMvvm) ────────────────────────

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Skills", Close, () => IsOpen);
            PanelRouter.Register(PanelId.HeroSkillTree, Open);
            // EYES-SWEEP 2026-07-06: the legacy PanelId.HeroTalents route is REMOVED (was
            // ff.herotalents-gated; a stale PlayerPrefs "ff.herotalents"=1 re-armed the dead route and
            // the capture fleet rendered panel_HeroTalents fully black). One panel, ONE route:
            // HeroSkillTree. All entry points (ArcaneTower building, dialogue OpenTalents) route to
            // PanelId.HeroSkillTree unconditionally; PanelId.HeroTalents is retired-unroutable.
        }

        private void OnDestroy()
        {
            Unbind();
            if (_vm != null) { _vm.Dispose(); _vm = null; }
            if (_pointerVfx != null) { _pointerVfx.Dispose(); _pointerVfx = null; }
            if (_auraVfx != null) { _auraVfx.Dispose(); _auraVfx = null; }
            if (_ui != null) Destroy(_ui);
            _ui = null;
            PanelRouter.Unregister(PanelId.HeroSkillTree, Open);
        }

        // ── Open: build chrome, construct + bind the VM ───────────────────────────

        public void Open()
        {
            Close();
            BuildChrome();

            // No slug argument = the VM resolves the LIVE hero class (GameState.HeroClass) itself.
            // Do NOT pass a literal here: a hardcoded slug is what made a Ranger browse — and spend
            // Wisdom on — the KNIGHT tree while HeroTalentModifiers folded stats from the real class.
            _vm = new HeroSkillTreeVM(Close);
            Bind(_vm);

            if (!PanelManager.NotifyOpened(_panelHandle))
                return; // rejected (e.g. in battle) — NotifyOpened already invoked Close.

            Debug.Log("[HeroSkillTreePanelMvvm] Opened. Bound HeroSkillTreeVM (node-graph MVVM).");
        }

        // ── IPanelView ────────────────────────────────────────────────────────────

        public void Bind(IPanelViewModel vm)
        {
            Unbind();
            _vm = vm as HeroSkillTreeVM;
            if (_vm == null) return;
            _vm.Changed += Render;
            Render();
        }

        public void Unbind()
        {
            if (_vm != null) _vm.Changed -= Render;
        }

        // ── Render: repaint from vm.* ONLY ────────────────────────────────────────

        private void Render()
        {
            if (_vm == null) return;
            if (_headerLabel != null) _headerLabel.text = _vm.Title;
            if (_wisdomLabel != null)
                _wisdomLabel.text = "WISDOM " + _vm.RemainingWisdom +
                                    " - next point at Level " + _vm.NextWisdomLevel;

            RebuildTracks();
            RenderQuickSwapBar();
            RenderSpendPopup();
        }

        // ── Spend popup (owner 2026-08-15) ────────────────────────────────────────
        // Graph-only screen: all copy + spend lives here. Confirm spends Wisdom now;
        // Cancel (or scrim on the panel) dismisses without spending.

        private void RenderSpendPopup()
        {
            if (_popupRoot == null || _vm == null) return;
            bool show = _vm.HasSelection;
            _popupRoot.SetActive(show);
            if (!show) return;

            AnchorPopupBesideNode();

            if (_popupName != null) _popupName.text = _vm.SelectedNodeName;
            // The compact nested shell deliberately suppresses its outer title band at
            // short landscape heights. Keep the selected talent's identity inside the
            // readable body as well, so no device ratio can show an anonymous prompt.
            if (_popupDesc != null)
                _popupDesc.text = "<color=#E5B93F><b>" + _vm.SelectedNodeName +
                                  "</b></color>\n" + _vm.SelectedNodeDescription;
            if (_popupPrompt != null) _popupPrompt.text = _vm.SelectedSpendPrompt;

            bool canSpend = _vm.CanSpendSelected;
            if (_popupConfirmBtn != null)
            {
                // Keep the button present so layout stays stable; dim when unaffordable.
                _popupConfirmBtn.gameObject.SetActive(true);
                _popupConfirmBtn.interactable = canSpend;
                SetButtonAlpha(_popupConfirmBtn, canSpend ? 1f : 0.35f);
            }
            if (_popupConfirmRing != null)
                _popupConfirmRing.SetActive(canSpend);
            // COLOURBLIND LAW: the WORD carries the state. This used to be
            // `canSpend ? "LEARN" : "OWNED"`, and CanSpendSelected is false for OWNED *and*
            // prereq-LOCKED *and* UNAFFORDABLE alike - so a locked or unaffordable talent
            // painted the word OWNED, a flat lie on the one cue the owner can read. One word
            // per real state, derived from the node's own SkillNodeState.
            if (_popupConfirmLabel != null)
                _popupConfirmLabel.text = ConfirmWordFor(canSpend);
            // Cancel is always the dismiss path (owned / locked / buyable).
            if (_popupCancelBtn != null)
            {
                _popupCancelBtn.interactable = true;
                SetButtonAlpha(_popupCancelBtn, 1f);
            }
        }

        /// <summary>
        /// WO-1522 - seat the learn dialog BESIDE the selected node, never over it.
        ///
        /// The node's plate is measured in the POPUP ROOT's own local space (not in graph-content
        /// space), because the graph SCROLLS: a node's content coordinate says nothing about where
        /// it currently sits on screen. Whichever half of the workspace the plate is in, the card
        /// takes the OTHER half - and the two seats are disjoint by construction
        /// (PopupSideLeftX1 == PopupSideRightX0), so "does not cover the node" follows from the
        /// anchors rather than from a measurement that could drift.
        ///
        /// A node whose seat cannot be resolved (rebuild race, id not on screen) falls back to the
        /// RIGHT seat and says so in the trace - never to the retired centre, which is the seat
        /// that guarantees an overlap.
        /// </summary>
        private void AnchorPopupBesideNode()
        {
            if (_popupCard == null || _popupRoot == null) return;
            var rootRt = (RectTransform)_popupRoot.transform;

            bool resolved = false;
            float nodeFrac = 1f;
            string id = _vm != null ? _vm.SelectedNodeId : "";
            if (_graphContent != null && !string.IsNullOrEmpty(id))
            {
                var seat = _graphContent.Find("Node_" + id) as RectTransform;
                float width = rootRt.rect.width;
                if (seat != null && width > 1f)
                {
                    Vector3 world = seat.TransformPoint(seat.rect.center);
                    Vector2 local = rootRt.InverseTransformPoint(world);
                    nodeFrac = Mathf.Clamp01((local.x - rootRt.rect.xMin) / width);
                    resolved = true;
                }
            }

            // Node on the LEFT half -> card on the RIGHT, and vice versa.
            bool cardRight = !resolved || nodeFrac < 0.5f;
            float x0 = cardRight ? PopupSideRightX0 : PopupSideLeftX0;
            float x1 = cardRight ? PopupSideRightX1 : PopupSideLeftX1;
            if (Mathf.Approximately(_popupCard.anchorMin.x, x0)) return;   // already seated

            _popupCard.anchorMin = new Vector2(x0, PopupAnchorY0);
            _popupCard.anchorMax = new Vector2(x1, PopupAnchorY1);
            _popupCard.offsetMin = Vector2.zero;
            _popupCard.offsetMax = Vector2.zero;
            FlowTrace.Step("SkillTree", "learn dialog seated " + (cardRight ? "RIGHT" : "LEFT") +
                                        " (x " + x0.ToString("F2") + ".." + x1.ToString("F2") +
                                        ") beside node '" + id + "' at frac " +
                                        (resolved ? nodeFrac.ToString("F2") : "UNRESOLVED-default-right"));
        }

        /// <summary>
        /// The one word on the confirm button, derived from the SELECTED node's real state -
        /// never from CanSpendSelected alone (that predicate is false for owned, prereq-locked
        /// and unaffordable nodes alike, so a single ternary can only ever tell the truth about
        /// one of the three). Owned -> OWNED; prerequisite not yet taken -> LOCKED; reachable but
        /// unaffordable -> LEARN, dimmed and non-interactable, with the state line carrying
        /// "Needs N Wisdom (have M)"; buyable -> LEARN.
        ///
        /// A node whose seat cannot be resolved falls back to LEARN, NEVER to OWNED: claiming
        /// ownership the player does not have is the failure this method exists to end.
        /// </summary>
        private string ConfirmWordFor(bool canSpend)
        {
            if (canSpend) return "LEARN";
            if (_vm == null) return "LEARN";

            var tracks = _vm.Tracks;
            string selectedId = _vm.SelectedNodeId;
            if (tracks == null || string.IsNullOrEmpty(selectedId)) return "LEARN";

            // One pass: the selected seat plus a state index for its prerequisites (same seat
            // walk RebuildTracks/FilterCalmFrontier use - SkillNodeState is the VM's own verdict).
            var stateById = new Dictionary<string, SkillNodeState>(64, StringComparer.Ordinal);
            SkillTrackNodeVM selected = default;
            bool found = false;
            bool anyCapstoneOwned = false;
            for (int t = 0; t < tracks.Count; t++)
            {
                var track = tracks[t];
                if (track == null || track.Nodes == null) continue;
                for (int i = 0; i < track.Nodes.Count; i++)
                {
                    var seat = track.Nodes[i];
                    if (string.IsNullOrEmpty(seat.Node.Id)) continue;
                    stateById[seat.Node.Id] = seat.State;
                    if (seat.Node.IsCapstone && seat.State == SkillNodeState.Owned)
                        anyCapstoneOwned = true;
                    if (!found && string.Equals(seat.Node.Id, selectedId, StringComparison.Ordinal))
                    {
                        selected = seat;
                        found = true;
                    }
                }
            }
            if (!found) return "LEARN";
            if (selected.State == SkillNodeState.Owned) return "OWNED";

            // Same precedence the VM's LockReasonFor uses: the one-capstone-per-hero rule is the
            // dominant blocker, so a second capstone reads LOCKED even when it is also unaffordable.
            if (selected.Node.IsCapstone && anyCapstoneOwned) return "LOCKED";

            var prereqs = selected.Node.Prereqs;
            if (prereqs != null)
            {
                for (int p = 0; p < prereqs.Count; p++)
                {
                    string pr = prereqs[p];
                    if (string.IsNullOrEmpty(pr)) continue;
                    if (!stateById.TryGetValue(pr, out var prState)) return "LOCKED";
                    if (prState != SkillNodeState.Owned && prState != SkillNodeState.Planned)
                        return "LOCKED";
                }
            }

            // Prerequisites are met and the capstone rule is clear, so the remaining gate is
            // affordability: Wisdom short = the action is still LEARN (dimmed), with the state
            // line carrying "Needs N Wisdom (have M)". Anything else structural reads LOCKED.
            if (selected.Node.WisdomCost > _vm.RemainingWisdom) return "LEARN";
            return "LOCKED";
        }

                // ── WO-896: SPARSE TALENT GRAPH (Obsidian demo) ───────────────────────────
        // Flatten every track seat onto a free-form canvas. Authored x/y drive placement
        // when present; missing seats auto-layout by tier/column (and branch) with room
        // to breathe. Gold connectors follow real prerequisites (diagonal OK). No "MORE
        // BELOW" cue.
        //
        // WO-1310 corrects this header: it used to claim "No name labels under plates (detail
        // column owns the copy)" while BuildNodeNamePlate hung one under EVERY plate and there
        // is no detail column at all (the body is full-width graph). The nameplate is the ONE
        // name per node, and it carries the ACTIVE/PASSIVE/SLOT-N word on its second line.

        private void RebuildTracks()
        {
            if (_graphContent == null || _vm == null) { ClearContent(); return; }
            Vector2 keptScroll = _graphContent.anchoredPosition;
            ClearContent();

            var allSeats = new List<SkillTrackNodeVM>(64);
            var byId = new Dictionary<string, SkillTrackNodeVM>(64);
            var tracks = _vm.Tracks;
            if (tracks != null)
            {
                for (int t = 0; t < tracks.Count; t++)
                {
                    var track = tracks[t];
                    if (track == null || track.Nodes == null) continue;
                    for (int i = 0; i < track.Nodes.Count; i++)
                    {
                        var seat = track.Nodes[i];
                        if (string.IsNullOrEmpty(seat.Node.Id)) continue;
                        allSeats.Add(seat);
                        byId[seat.Node.Id] = seat;
                    }
                }
            }

            // WO-896 F8 2026-08-15 "look at the overcrowding": drawing every locked leaf
            // (41 seats) is the dense board the owner flagged. Calm frontier only — demo density.
            var seats = FilterCalmFrontier(allSeats, byId);
            if (seats.Count < allSeats.Count)
                FlowTrace.Step("SkillTree", "calm frontier: showing " + seats.Count +
                                            " of " + allSeats.Count + " node(s) (deep locked chains hidden)");

            if (seats.Count == 0)
            {
                var empty = ElarionUiKit.Label(_graphContent, "No talents to show yet.", 0f, 1f,
                    ElarionUi.ParchmentDim, ElarionUi.FontBody,
                    TMPro.TextAlignmentOptions.Center, 0.06f, 0.94f);
                ElarionUiKit.FitSingleLine(empty);
                FlowTrace.Warn("SkillTree", "sparse graph drew ZERO nodes - the graph well is empty");
                _graphContent.sizeDelta = new Vector2(
                    ContentPadPx * 2f + GraphUnitWpx * 0.5f,
                    ContentPadPx * 2f + GraphUnitHpx * 0.4f);
                return;
            }

            // Visible-only byId so connectors never point at hidden plates.
            var visibleById = new Dictionary<string, SkillTrackNodeVM>(seats.Count);
            for (int i = 0; i < seats.Count; i++)
                visibleById[seats[i].Node.Id] = seats[i];

            // Norm positions (0..1+, may extend past 1 for auto secondary branches).
            var norm = new Dictionary<string, Vector2>(seats.Count);
            ResolveGraphNorms(seats, norm);

            // Landscape-mobile composition: authored progression is bottom-to-top, which
            // required opening the graph at a vertical scroll offset and visibly amputated
            // the upper rank. Rotate the semantic axes once so the ENTRY RANK is the top row
            // and progression runs DOWNWARD. Normalize against the visible frontier so the
            // rotation consumes the whole measured well.
            //
            // WO-1310 — THE ROTATION USED TO FEED THE AXES THE WRONG WAY ROUND. It emitted
            // (progressX, trackY): progression became the COLUMN axis and the track lane the
            // ROW axis. The authored lattice is a TIER GRID - a handful of tiers, five lanes
            // per tier - so that put 2-3 columns against a dozen rows, i.e. a TALL NARROW
            // board inside a 1695 x 493 landscape well. That is the owner's "squeezed into a
            // narrow column with the whole right-hand side dead black". Lanes are the many
            // axis and belong on the WIDE one; tiers are the few axis and belong on the short
            // one. Progression still reads base-first because progressX is inverted, so the
            // no-prerequisite base rank is ROW 0 - the rest position, the entry point the
            // owner is looking for - and deeper tiers scroll down under it.
            if (norm.Count > 1)
            {
                float sourceMinX = float.MaxValue, sourceMaxX = float.MinValue;
                float sourceMinY = float.MaxValue, sourceMaxY = float.MinValue;
                foreach (var pair in norm)
                {
                    sourceMinX = Mathf.Min(sourceMinX, pair.Value.x);
                    sourceMaxX = Mathf.Max(sourceMaxX, pair.Value.x);
                    sourceMinY = Mathf.Min(sourceMinY, pair.Value.y);
                    sourceMaxY = Mathf.Max(sourceMaxY, pair.Value.y);
                }
                float spanX = Mathf.Max(0.001f, sourceMaxX - sourceMinX);
                float spanY = Mathf.Max(0.001f, sourceMaxY - sourceMinY);
                var rotated = new Dictionary<string, Vector2>(norm.Count);
                foreach (var pair in norm)
                {
                    float lane = (pair.Value.x - sourceMinX) / spanX;
                    // Authored y ascends DOWNWARD and the base rank carries the LARGEST y,
                    // so inverting puts the base at 0 = the top row = the rest position.
                    float progress = 1f - (pair.Value.y - sourceMinY) / spanY;
                    rotated[pair.Key] = new Vector2(lane, progress);
                }
                norm = rotated;
            }

            // WO-1021 sec 2.1b — POSITION IS SOLVED IN ONE PLACE. The lattice box is derived
            // from the MEASURED well (GraphUnitWpx/Hpx remain the documented fallback for the
            // first frame, before the rect has been laid out); SolveGraphLatticePx then owns
            // pitch, separation, row spread and centring. RebuildGraph does NO geometry of its
            // own past this point — splitting placement across two methods is what let the
            // authored and fallback paths drift into two visual languages.
            var wellRt = _graphContent.parent as RectTransform;
            float wellW = wellRt != null ? wellRt.rect.width : 0f;
            float wellH = wellRt != null ? wellRt.rect.height : 0f;
            float boxW = wellW > 1f ? Mathf.Max(wellW - GraphPadPx * 2f, MinLatticeWpx) : GraphUnitWpx;
            float boxH = wellH > 1f ? Mathf.Max(wellH - GraphPadPx * 2f - RankBandPx, MinLatticeHpx) : GraphUnitHpx;
            if (wellW <= 1f || wellH <= 1f)
                FlowTrace.Warn("SkillTree", "graph well not laid out yet (" + wellW.ToString("F0") + "x" +
                                            wellH.ToString("F0") + ") - solving on the fallback lattice " +
                                            GraphUnitWpx + "x" + GraphUnitHpx + "; the next rebuild re-solves " +
                                            "against the measured rect");

            var orderIds = new List<string>(seats.Count);
            var flatNorm = new float[seats.Count * 2];
            for (int i = 0; i < seats.Count; i++)
            {
                string sid = seats[i].Node.Id;
                orderIds.Add(sid);
                Vector2 nv = norm.TryGetValue(sid, out var nvv) ? nvv : Vector2.zero;
                flatNorm[i * 2] = nv.x;
                flatNorm[i * 2 + 1] = nv.y;
            }
            float[] solved = SolveGraphLatticePx(flatNorm, boxW, boxH);

            float pad = GraphPadPx;
            float maxX = 0f, maxY = 0f;
            float minX = float.MaxValue, minY = float.MaxValue;
            var centers = new Dictionary<string, Vector2>(orderIds.Count);
            for (int i = 0; i < orderIds.Count; i++)
            {
                float cx = solved[i * 2];
                float cyDown = solved[i * 2 + 1];   // px DOWN from content top
                centers[orderIds[i]] = new Vector2(cx, cyDown);
                if (cx > maxX) maxX = cx;
                if (cyDown > maxY) maxY = cyDown;
                if (cx < minX) minX = cx;
                if (cyDown < minY) minY = cyDown;
            }
            if (minX > maxX) minX = maxX;
            if (minY > maxY) minY = maxY;

            // WO-1310 — SYMMETRIC EXTENTS. The retired sizing was
            //   contentW = maxX + NodeFocusPx*0.5 + pad
            // where maxX is the RIGHTMOST CENTRE, so the content rect kept the solver's LEFT
            // centring margin (baked into minX) and truncated the matching RIGHT one. Mirroring
            // the leading margin is what stops the board reading as "shoved left with a dead
            // black half on the right". The floor keeps a half plate + pad even at minX == half.
            float contentW = Mathf.Max(maxX + minX, maxX + PlateClearPx * 0.5f + pad);
            float contentH = Mathf.Max(maxY + minY, maxY + PlateClearPx * 0.5f + pad + RankBandPx);
            // WO-1601 - FIT THE SOLVED BOARD INTO THE WELL BEFORE ANYTHING ELSE READS IT.
            // The solver seats every centre inside its box; the CONTENT rect that carries them
            // is still wider and taller than the mask (measured 1915x633 against a 1705x395 well
            // at 2670x1200), and a top-left rest position then hands the player a half plate at
            // the right edge and a half row at the floor. Scale is the one lever that does not
            // move a centre - SolveGraphLatticePx keeps its pitch laws ([grid] pins them).
            float fitScale = ResolveGraphFitScale(contentW, contentH, wellW, wellH);
            _graphContent.localScale = new Vector3(fitScale, fitScale, 1f);
            // The window the mask shows, expressed in CONTENT units: at fit < 1 the well reveals
            // MORE content than its own px size, and every containment/scroll number below has to
            // be in the same space as the centres or it certifies the wrong rectangle.
            float viewW = wellW > 1f ? wellW / fitScale : contentW;
            float viewH = wellH > 1f ? wellH / fitScale : contentH;
            // ...and never NARROWER than that window. The content rect is top-left anchored and
            // pivoted, so a board smaller than the viewport would rest flush LEFT and leave the
            // remainder as dead black on the right - the defect verbatim. Filling the well means
            // the solver's own centring is what the player sees.
            if (wellW > 1f) contentW = Mathf.Max(contentW, viewW);
            if (wellH > 1f) contentH = Mathf.Max(contentH, viewH);
            _graphContent.sizeDelta = new Vector2(contentW, contentH);

            // Connectors FIRST so opaque plates draw over their ends.
            for (int i = 0; i < seats.Count; i++)
            {
                var seat = seats[i];
                var prereqs = seat.Node.Prereqs;
                if (prereqs == null) continue;
                if (!centers.TryGetValue(seat.Node.Id, out var to)) continue;
                for (int p = 0; p < prereqs.Count; p++)
                {
                    string pr = prereqs[p];
                    if (string.IsNullOrEmpty(pr)) continue;
                    if (!centers.TryGetValue(pr, out var from)) continue;
                    if (!visibleById.TryGetValue(pr, out var parentSeat)) continue;
                    BuildGraphConnector(_graphContent, from, to, parentSeat, seat);
                }
            }

            // WO-1021 sec 2.1d — the focus plate is a BOARD-LEVEL SINGLETON. It is resolved
            // ONCE, for the whole board, by ResolveFocusNodeId; a seat is focus iff it IS that
            // id. Never `seat.State == SkillNodeState.Next` here: Next is a PER-TRACK signal
            // (HeroSkillTreeVM.ResolveStates resets nextTaken per track) and consuming it as a
            // board-level one grew one oversized gold plate per track.
            var focusStates = new List<SkillNodeState>(seats.Count);
            for (int i = 0; i < seats.Count; i++) focusStates.Add(seats[i].State);
            string focusId = ResolveFocusNodeId(orderIds, focusStates, _vm.SelectedNodeId);

            var focusIds = new List<string>(2);
            int auraCount = 0;
            int nextCueCount = 0;
            for (int i = 0; i < seats.Count; i++)
            {
                var seat = seats[i];
                if (!centers.TryGetValue(seat.Node.Id, out var c)) continue;
                bool focus = !string.IsNullOrEmpty(focusId)
                          && string.Equals(focusId, seat.Node.Id, StringComparison.Ordinal);
                if (focus) focusIds.Add(seat.Node.Id);
                if (seat.State == SkillNodeState.Next) nextCueCount++;
                if (IsAuraNode(seat.State)) auraCount++;
                Guard.Try("SkillTree", "build graph node '" + seat.Node.Id + "'",
                    () => BuildGraphNode(seat, c.x, c.y, focus));
            }

            // Owner aura pick: change-gated attach-count line - one Step per COUNT change,
            // never one per Render.
            if (auraCount != _lastAuraCount)
            {
                _lastAuraCount = auraCount;
                FlowTrace.Step("TalentAura", "attach: aura on " + auraCount + " owned node(s) " +
                                             "(default target = OWNED, owner-flippable) rig=" +
                                             (_auraVfx != null && _auraVfx.IsValid ? "vfx" : "art-only"));
            }

            // WO-1021 sec 2.1d ASSERT (sec.12 instrumentation, permanent): the oversized plate
            // is a board-level singleton. Unreachable by construction — logged, never silent,
            // so a future edit that re-conflates Next with focus is named by the trace and not
            // by the owner's third "messy" report.
            if (focusIds.Count > 1)
                FlowTrace.Fail("SkillTree", "FOCUS SINGLETON VIOLATED: " + focusIds.Count +
                                            " oversized plate(s) [" + string.Join(",", focusIds.ToArray()) +
                                            "] - NodeFocusPx may apply to at most ONE plate on the board");

            // Owner pick 2026-08-16: change-gated follow line - one Step per focus CHANGE,
            // never one per Render (Render fires on every selection tap).
            string pointerSig = string.Join(",", focusIds.ToArray());
            if (pointerSig != _lastPointerSig)
            {
                _lastPointerSig = pointerSig;
                if (focusIds.Count > 0)
                    FlowTrace.Step("TalentPointer", "follow: pointer on node(s) [" + pointerSig +
                                                    "] rig=" + (_pointerVfx != null && _pointerVfx.IsValid
                                                        ? "vfx+ring" : "ring-only"));
                else
                    FlowTrace.Step("TalentPointer", "follow: no focus node this rebuild - pointer idle");
            }

            // Keep scroll place; rest = flush top-left so the first nodes are whole.
            // (wellW / wellH were measured above, before the lattice was solved against them.)
            //
            // WO-1310 acceptance 1: the ENTRY POINT is the top-left corner - column 0 is the
            // no-prerequisite base rank after the rotation. A kept scroll is only ever a
            // within-board tap; the FIRST draw of a board (a fresh Open, or a different tree)
            // must rest at that corner, never mid-content. _lastLayoutSig is null exactly then.
            // WO-1601: clamp against the SCALED window, not the raw well px - at fit < 1 the raw
            // well px is a smaller rectangle than the mask actually reveals, so clamping to it
            // would let the board scroll past its own end and re-open a dead margin.
            float maxDown = wellH > 1f ? Mathf.Max(0f, contentH - viewH) : 0f;
            float maxRight = wellW > 1f ? Mathf.Max(0f, contentW - viewW) : 0f;
            if (string.IsNullOrEmpty(_lastLayoutSig)) keptScroll = Vector2.zero;
            _graphContent.anchoredPosition = new Vector2(
                Mathf.Clamp(keptScroll.x, -maxRight, 0f),
                Mathf.Clamp(keptScroll.y, 0f, maxDown));

            string sig = seats.Count + "/" + allSeats.Count + "/" + contentW.ToString("F0") + "x" +
                         contentH.ToString("F0") + "/f" + focusIds.Count + "/n" + nextCueCount;
            if (sig != _lastLayoutSig)
            {
                _lastLayoutSig = sig;
                FlowTrace.Step("SkillTree", "sparse graph drawn: " + seats.Count + " visible of " +
                                            allSeats.Count + " node(s), content " +
                                            contentW.ToString("F0") + "x" + contentH.ToString("F0") + " px, " +
                                            focusIds.Count + " oversized focus plate(s) [law: <=1] and " +
                                            nextCueCount + " per-track NEXT badge(s) at normal size");

                // Spacing probe (2026-08-16, change-gated by the same sig so it never spams):
                // min pairwise centre gap + overlap counts at the CURRENT plate sizes, over the
                // plates actually placed this rebuild (authored AND auto-laid). Two square plates
                // overlap iff both axis deltas are under the half-size sum; clearance for a pair
                // is therefore max(|dx|,|dy|) minus that sum.
                float minGapPx = float.MaxValue;
                int overlapNormal = 0, overlapFocus = 0;
                float halfSumNormal = NodeSizePx;                        // 68 + 68
                float halfSumFocus = (NodeFocusPx + NodeSizePx) * 0.5f;  // 84 + 68
                string worstPair = "-";
                foreach (var a in centers)
                {
                    foreach (var b in centers)
                    {
                        if (string.CompareOrdinal(a.Key, b.Key) >= 0) continue;
                        float dx = Mathf.Abs(a.Value.x - b.Value.x);
                        float dy = Mathf.Abs(a.Value.y - b.Value.y);
                        float sep = Mathf.Max(dx, dy);
                        if (sep < minGapPx) { minGapPx = sep; worstPair = a.Key + "/" + b.Key; }
                        if (dx < halfSumNormal && dy < halfSumNormal) overlapNormal++;
                        else if (dx < halfSumFocus && dy < halfSumFocus) overlapFocus++;
                    }
                }
                if (centers.Count < 2) minGapPx = MinNodePitchPx;
                bool pitchBroken = minGapPx < MinNodePitchPx - 0.5f;
                string spacing = "graph spacing: minCentreGap=" + minGapPx.ToString("F0") +
                                 "px vs pitch law " + MinNodePitchPx.ToString("F0") +
                                 "px (tightest " + worstPair + "), overlaps normal(" + NodeSizePx +
                                 ")=" + overlapNormal + " focus(" + NodeFocusPx + ")=" + overlapFocus +
                                 ", box " + boxW.ToString("F0") + "x" + boxH.ToString("F0") +
                                 ", content " + contentW.ToString("F0") + "x" + contentH.ToString("F0") + " px";
                if (overlapNormal > 0 || overlapFocus > 0 || pitchBroken) FlowTrace.Warn("SkillTree", spacing);
                else FlowTrace.Step("SkillTree", spacing);

                // WO-1310 sec.12 probe - THE CLIP QUESTION, answered by a number on every draw
                // instead of by the owner's eyes. A plate closer to the content origin than half
                // of PlateClearPx has its focus ring (or its hung nameplate) sliced by the
                // RectMask2D; a right/bottom margin narrower than the left/top one is the dead
                // black half the owner reported. Both are LOGGED, and the oracle FAILS on them.
                float clearHalf = PlateClearPx * 0.5f;
                float rightMargin = contentW - maxX;
                float bottomMargin = contentH - maxY;
                string insets = "graph insets: topLeft=" + minX.ToString("F0") + "/" + minY.ToString("F0") +
                                "px, bottomRight=" + rightMargin.ToString("F0") + "/" +
                                bottomMargin.ToString("F0") + "px vs clearance " +
                                clearHalf.ToString("F0") + "px; well " + wellW.ToString("F0") + "x" +
                                wellH.ToString("F0") + " shows " + (wellW > 1f && contentW > wellW ? "part" : "all") +
                                " of the width and " + (wellH > 1f && contentH > wellH ? "part" : "all") +
                                " of the height (scrolls for the rest, resting at the base column)";
                if (minX < clearHalf - 0.5f || minY < clearHalf - 0.5f ||
                    rightMargin < clearHalf - 0.5f || bottomMargin < clearHalf - 0.5f)
                    FlowTrace.Fail("SkillTree", "CLIPPED: " + insets);
                else FlowTrace.Step("SkillTree", insets);

                // WO-1601 sec.12 - THE FIT, AND WHAT IT STILL DOES NOT COVER, as a number on
                // every draw. The margin probe above measures the CONTENT rect; this one
                // measures the REST WINDOW the mask actually reveals, which is the rectangle the
                // owner's frame disagreed with. Reported per axis so a partial fix cannot read
                // as a whole one.
                float plateHalf = NodePlateSizePx(false) * 0.5f;
                float focusHalf = NodePlateSizePx(true) * 0.5f;
                int outRight = 0, outBottom = 0;
                float worstRight = 0f, worstBottom = 0f;
                foreach (var c in centers)
                {
                    float h = string.Equals(focusId, c.Key, StringComparison.Ordinal) ? focusHalf : plateHalf;
                    float overR = (c.Value.x + h) - viewW;
                    float overB = (c.Value.y + h) - viewH;
                    if (overR > 0.5f) { outRight++; worstRight = Mathf.Max(worstRight, overR); }
                    if (overB > 0.5f) { outBottom++; worstBottom = Mathf.Max(worstBottom, overB); }
                }
                string fitLine = "graph fit: scale=" + fitScale.ToString("F3") + " (floor " +
                                 MinGraphFitScale.ToString("F3") + ", = MinTouchPx/NodeSizePx), well " +
                                 wellW.ToString("F0") + "x" + wellH.ToString("F0") + " -> rest window " +
                                 viewW.ToString("F0") + "x" + viewH.ToString("F0") + " content px over a " +
                                 contentW.ToString("F0") + "x" + contentH.ToString("F0") + " board; plates " +
                                 "outside the rest window: right=" + outRight + " (worst " +
                                 worstRight.ToString("F0") + "px) bottom=" + outBottom + " (worst " +
                                 worstBottom.ToString("F0") + "px); scrollable " +
                                 maxRight.ToString("F0") + "x" + maxDown.ToString("F0") + " px";
                if (outRight > 0 || outBottom > 0) FlowTrace.Warn("SkillTree", fitLine);
                else FlowTrace.Step("SkillTree", fitLine);

                TraceLayoutRects();
            }
        }

        /// <summary>
        /// WO-1601 sec.12 - NAME EVERY RECT THE PANEL BUILT, ONCE PER LAYOUT CHANGE.
        ///
        /// The band across the tree was owned by a rect nobody could name from the screenshot:
        /// GraphWellBezel's rect was correct and its ART was not. So this walks the live UI and
        /// states, for every RectTransform: its path, its rect in CANVAS space, and - for any
        /// Image - the sprite, the draw type and, when Sliced, whether the 9-slice border can
        /// even land on the rect's edges. That last line is the one that would have named this
        /// defect on the first read instead of after a pixel forensic pass.
        ///
        /// Change-gated by the caller (_lastLayoutSig), so it emits on a shape change and never
        /// once per Render. Never on a frame path - CLAUDE.md sec.12 forbids per-frame Step.
        /// </summary>
        private void TraceLayoutRects()
        {
            if (_ui == null) return;
            Guard.Try("SkillTree", "trace layout rects", () =>
            {
                Canvas.ForceUpdateCanvases();
                var root = _ui.GetComponent<RectTransform>();
                if (root == null) return;
                var rects = _ui.GetComponentsInChildren<RectTransform>(true);
                FlowTrace.Step("SkillTree", "layout: " + rects.Length + " rect(s) under " + _ui.name +
                                            " - dumping every one (WO-1601)");
                for (int i = 0; i < rects.Length; i++)
                {
                    var rt = rects[i];
                    if (rt == null) continue;
                    var r = RectInRoot(rt, root);
                    string line = "layout rect '" + PathUnder(rt, _ui.transform) + "' x " +
                                  r.xMin.ToString("F0") + ".." + r.xMax.ToString("F0") + " y " +
                                  r.yMin.ToString("F0") + ".." + r.yMax.ToString("F0") +
                                  " (" + r.width.ToString("F0") + "x" + r.height.ToString("F0") + ")";
                    var img = rt.GetComponent<Image>();
                    if (img != null && img.sprite != null)
                    {
                        Vector4 b = img.sprite.border;
                        line += " img=" + img.sprite.name + " type=" + img.type +
                                " border L" + b.x.ToString("F0") + " B" + b.y.ToString("F0") +
                                " R" + b.z.ToString("F0") + " T" + b.w.ToString("F0");
                        if (img.type == Image.Type.Sliced && (b.x + b.z > 0f || b.y + b.w > 0f))
                        {
                            float mul = Mathf.Max(0.0001f, img.pixelsPerUnitMultiplier);
                            float bh = (b.y + b.w) / mul, bw = (b.x + b.z) / mul;
                            if (bh > r.height + 0.5f || bw > r.width + 0.5f)
                                FlowTrace.Warn("SkillTree", "layout: '" + rt.name + "' is Sliced with " +
                                    bw.ToString("F0") + "x" + bh.ToString("F0") + " px of border inside a " +
                                    r.width.ToString("F0") + "x" + r.height.ToString("F0") + " rect - Unity " +
                                    "shrinks the strips and the middle slice collapses, so any art that " +
                                    "lives in the MIDDLE of the sprite is not drawn at all");
                            else
                                line += " sliceMiddle=" + (r.width - bw).ToString("F0") + "x" +
                                        (r.height - bh).ToString("F0") + "px";
                        }
                    }
                    FlowTrace.Step("SkillTree", line);
                }
            });
        }

        /// <summary>A rect in its canvas root's local space - the one comparable frame for two
        /// rects that hang off different parents.</summary>
        private static Rect RectInRoot(RectTransform rt, RectTransform root)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = root.InverseTransformPoint(corners[i]);
                xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
            }
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        /// <summary>Slash-separated path from <paramref name="stopAt"/> - a bare name is
        /// ambiguous the moment two surfaces share one (every node carries a "NamePlate").</summary>
        private static string PathUnder(Transform t, Transform stopAt)
        {
            string path = t.name;
            var p = t.parent;
            int guard = 0;
            while (p != null && p != stopAt && guard++ < 16) { path = p.name + "/" + path; p = p.parent; }
            return path;
        }

        /// <summary>
        /// Calm frontier (RPG talent-tree density): enough nodes for GOLD CONNECTORS to
        /// read, never the full 40-seat spreadsheet.
        ///   • Actionable: Owned / Planned / Next / Available / Inert
        ///   • Class roots (no prereqs) — entry face of the tree
        ///   • One locked step past any root OR owned/planned parent (so lines exist on day 1)
        ///   • Universal shelf only after the tree is engaged
        /// Hidden seats reappear as parents unlock.
        /// </summary>
        private static List<SkillTrackNodeVM> FilterCalmFrontier(
            List<SkillTrackNodeVM> all, Dictionary<string, SkillTrackNodeVM> byId)
        {
            var rootIds = new HashSet<string>(StringComparer.Ordinal);
            bool anyEngaged = false;
            for (int i = 0; i < all.Count; i++)
            {
                var seat = all[i];
                var st = seat.State;
                if (st == SkillNodeState.Owned || st == SkillNodeState.Planned) anyEngaged = true;
                if (IsRootSeat(seat, byId)) rootIds.Add(seat.Node.Id);
            }

            var keep = new List<SkillTrackNodeVM>(all.Count);
            var keptIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < all.Count; i++)
            {
                var seat = all[i];
                var st = seat.State;
                bool keepIt = false;

                if (st == SkillNodeState.Owned || st == SkillNodeState.Planned
                    || st == SkillNodeState.Next || st == SkillNodeState.Available
                    || st == SkillNodeState.Inert)
                    keepIt = true;
                else if (rootIds.Contains(seat.Node.Id))
                {
                    // Universal free-pick pool stays off a brand-new locked board.
                    if (!(seat.Node.IsShared && !anyEngaged)) keepIt = true;
                }
                else
                {
                    // One step past a root or an owned/planned parent → connector can draw.
                    var prereqs = seat.Node.Prereqs;
                    if (prereqs != null)
                    {
                        for (int p = 0; p < prereqs.Count; p++)
                        {
                            string pr = prereqs[p];
                            if (string.IsNullOrEmpty(pr)) continue;
                            if (rootIds.Contains(pr)) { keepIt = true; break; }
                            if (!byId.TryGetValue(pr, out var parent)) continue;
                            if (parent.State == SkillNodeState.Owned || parent.State == SkillNodeState.Planned)
                            { keepIt = true; break; }
                        }
                    }
                }

                if (!keepIt) continue;
                if (keptIds.Add(seat.Node.Id)) keep.Add(seat);
            }

            return keep;
        }

        private static bool IsRootSeat(SkillTrackNodeVM seat, Dictionary<string, SkillTrackNodeVM> byId)
        {
            var prereqs = seat.Node.Prereqs;
            if (prereqs == null || prereqs.Count == 0) return true;
            for (int p = 0; p < prereqs.Count; p++)
            {
                if (string.IsNullOrEmpty(prereqs[p])) continue;
                if (byId.ContainsKey(prereqs[p])) return false;
            }
            return true; // every prereq is hidden/missing → treat as root
        }

        /// <summary>Map every seat to a 0..1(+)-ish canvas position. Authored x/y win;
        /// missing seats auto-layout by tier/column so ranger/mage (no authoring) and
        /// knight secondary branches still read as a calm tree, not a packed grid.</summary>
        private static void ResolveGraphNorms(List<SkillTrackNodeVM> seats,
                                              Dictionary<string, Vector2> norm)
        {
            // Pass 1 — authored.
            int autoIdx = 0;
            for (int i = 0; i < seats.Count; i++)
            {
                var n = seats[i].Node;
                if (n.X >= 0f && n.Y >= 0f)
                    norm[n.Id] = new Vector2(n.X, n.Y);
            }

            // Pass 2 — missing: prefer a seat under a placed prerequisite, else tier/column.
            for (int pass = 0; pass < 3; pass++)
            {
                bool any = false;
                for (int i = 0; i < seats.Count; i++)
                {
                    var n = seats[i].Node;
                    if (norm.ContainsKey(n.Id)) continue;
                    any = true;

                    Vector2? under = null;
                    if (n.Prereqs != null)
                    {
                        for (int p = 0; p < n.Prereqs.Count; p++)
                        {
                            string pr = n.Prereqs[p];
                            if (!string.IsNullOrEmpty(pr) && norm.TryGetValue(pr, out var pp))
                            {
                                // Slight fan-out so multi-children of one parent don't stack.
                                float fan = ((autoIdx++ % 5) - 2) * 0.06f;
                                under = new Vector2(
                                    Mathf.Clamp01(pp.x + fan),
                                    Mathf.Min(1.15f, pp.y + 0.16f));
                                break;
                            }
                        }
                    }
                    if (under.HasValue) { norm[n.Id] = under.Value; continue; }

                    // Tier/column fallback (shared rides a calm bottom band).
                    float y;
                    if (n.IsShared) y = 0.94f;
                    else
                    {
                        int t = Mathf.Clamp(n.Tier <= 0 ? 1 : n.Tier, 1, 4);
                        y = 0.14f + (t - 1) * 0.18f;
                        if (n.Column >= 5) y = Mathf.Min(1.05f, y + 0.10f);
                    }
                    int col = Mathf.Max(0, n.Column);
                    float x = 0.08f + (col % 10) * 0.09f;
                    if (n.IsShared) x = 0.08f + (i % 8) * 0.11f;
                    norm[n.Id] = new Vector2(Mathf.Clamp(x, 0.04f, 1.10f), y);
                }
                if (!any) break;
            }
        }

        /// <summary>
        /// WO-1021 sec 2.1b — THE ONE PLACE POSITION IS SOLVED. Pure, deterministic, and
        /// callable from a headless oracle (primitives in, primitives out, no Unity objects):
        /// SkillsPanelLayoutRegression [grid] runs THIS and measures ITS output, because the
        /// authored x/y are an ORDERING HINT now, not shipped geometry.
        ///
        /// Contract — all four hold BY CONSTRUCTION, so there is no iteration cap to overrun:
        ///   * PITCH: every pair clears MinNodePitchPx in Chebyshev distance, so no two plates
        ///     can touch and no corner pip can land on a neighbouring plate, at any state.
        ///     Clearance comes from the FOCUS size, never NodeSizePx.
        ///   * ORDER (WO-1310): the norm MAGNITUDES are consumed, not just their order. Norm
        ///     index 0 clusters BOARD-WIDE into columns and index 1 into rows, so two nodes
        ///     with the same reading share a column (or a row) everywhere on the board. The
        ///     retired shape clustered rows only and then laid each row out independently and
        ///     centred, so a plate's x was its INDEX INSIDE ITS OWN ROW - which put a one-node
        ///     row dead centre and squeezed the board into a narrow centred column with the
        ///     right half of the well empty.
        ///   * SPREAD: columns/rows stretch to fill the box (capped at MaxPitchSpreadMul) and
        ///     the block is CENTRED on both axes, so no dead bottom third and no dead right
        ///     column; every plate is inset by half of PlateClearPx - the plate PLUS its hung
        ///     nameplate PLUS the focus glow - so no row can be clipped by the mask edge even
        ///     when that plate is the oversized one.
        ///   * ATTACHMENT: the stretch cap means no node's nearest neighbour is ever further
        ///     than MaxPitchSpreadMul pitches away — nothing can strand.
        /// </summary>
        /// <param name="normXY">Flattened authored/auto norms [x0,y0,x1,y1,...].</param>
        /// <param name="boxW">Usable lattice width in ref px (measured well, floored).</param>
        /// <param name="boxH">Usable lattice height in ref px.</param>
        /// <returns>Flattened plate centres [x0,yDown0,...] in content-local ref px.</returns>
        public static float[] SolveGraphLatticePx(float[] normXY, float boxW, float boxH)
        {
            int n = normXY == null ? 0 : normXY.Length / 2;
            var outXY = new float[n * 2];
            if (n == 0) return outXY;

            float half = PlateClearPx * 0.5f;
            boxW = Mathf.Max(boxW, MinLatticeWpx);
            boxH = Mathf.Max(boxH, MinLatticeHpx);

            // WO-1310. THE SOLVER NOW CONSUMES THE NORM MAGNITUDES, NOT JUST THEIR ORDER.
            // It is deliberately axis-NEUTRAL: norm index 0 picks the COLUMN, index 1 picks the
            // ROW, and the CALLER's rotation decides which semantic rides which axis (the panel
            // sends lanes across and progression down; the oracles send raw authored x/y).
            //   * Both axes are clustered BOARD-WIDE, so two nodes with the same reading share
            //     a column (or a row) everywhere on the board - the owner's "starting point ...
            //     visually in the same level". The retired shape clustered rows only, then laid
            //     each row out independently and centred it, so a plate's x was its INDEX
            //     INSIDE ITS OWN ROW: a one-node row landed dead centre and the whole board
            //     collapsed into a narrow centred column.
            //   * A column may not seat two nodes on one row, so a collision inside a column
            //     takes the next free row. Distinct (column,row) seats are what make the
            //     Chebyshev pitch law hold BY CONSTRUCTION rather than by luck.

            // Stable order: column axis, then row axis, then input index — ties never depend on
            // dictionary iteration order, so the same board always solves identically.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = normXY[a * 2].CompareTo(normXY[b * 2]);
                if (c != 0) return c;
                c = normXY[a * 2 + 1].CompareTo(normXY[b * 2 + 1]);
                return c != 0 ? c : a.CompareTo(b);
            });

            // ── COLUMNS: cluster norm index 0, board-wide ──────────────────────────
            var col = new int[n];
            int colCount = 1;
            float anchorX = normXY[order[0] * 2];
            for (int k = 0; k < n; k++)
            {
                int idx = order[k];
                float x = normXY[idx * 2];
                if (k > 0 && x - anchorX > ColClusterNorm) { colCount++; anchorX = x; }
                col[idx] = colCount - 1;
            }

            // ── ROWS: cluster norm index 1, board-wide ─────────────────────────────────
            var laneOrder = new int[n];
            for (int i = 0; i < n; i++) laneOrder[i] = i;
            Array.Sort(laneOrder, (a, b) =>
            {
                int c = normXY[a * 2 + 1].CompareTo(normXY[b * 2 + 1]);
                if (c != 0) return c;
                c = normXY[a * 2].CompareTo(normXY[b * 2]);
                return c != 0 ? c : a.CompareTo(b);
            });
            var lane = new int[n];
            int laneCount = 1;
            float anchorY = normXY[laneOrder[0] * 2 + 1];
            for (int k = 0; k < n; k++)
            {
                int idx = laneOrder[k];
                float y = normXY[idx * 2 + 1];
                if (k > 0 && y - anchorY > RowClusterNorm) { laneCount++; anchorY = y; }
                lane[idx] = laneCount - 1;
            }

            // ── SEAT: the row cluster is the WANTED row; a taken seat probes down ──────
            var columns = new List<List<int>>(colCount);
            for (int c = 0; c < colCount; c++) columns.Add(new List<int>());
            for (int k = 0; k < n; k++) columns[col[laneOrder[k]]].Add(laneOrder[k]);

            var row = new int[n];
            int rowCount = 1;
            for (int c = 0; c < colCount; c++)
            {
                var members = columns[c];
                int nextFree = 0;
                for (int m = 0; m < members.Count; m++)
                {
                    int idx = members[m];
                    int seat = Mathf.Max(lane[idx], nextFree);
                    row[idx] = seat;
                    nextFree = seat + 1;
                    if (seat + 1 > rowCount) rowCount = seat + 1;
                }
            }

            // ── PITCH: fill the measured well, floored by the separation law ─────
            float colPitch = colCount > 1
                ? Mathf.Clamp((boxW - PlateClearPx) / (colCount - 1),
                              MinColPitchPx, MinNodePitchPx * MaxPitchSpreadMul)
                : 0f;
            float rowPitch = rowCount > 1
                ? Mathf.Clamp((boxH - PlateClearPx) / (rowCount - 1),
                              MinRowPitchPx, MinNodePitchPx * MaxPitchSpreadMul)
                : 0f;

            float blockW = colPitch * (colCount - 1);
            float blockH = rowPitch * (rowCount - 1);
            float xLeft = half + Mathf.Max(0f, (boxW - PlateClearPx - blockW) * 0.5f);
            float yTop = half + Mathf.Max(0f, (boxH - PlateClearPx - blockH) * 0.5f);

            for (int i = 0; i < n; i++)
            {
                outXY[i * 2] = xLeft + colPitch * col[i];
                outXY[i * 2 + 1] = yTop + rowPitch * row[i];
            }
            return outXY;
        }

        /// <summary>
        /// WO-1021 sec 2.1d — THE BOARD-LEVEL FOCUS, resolved ONCE for the whole board.
        /// SELECTION owns the scarce size channel, so this returns AT MOST ONE id:
        ///   1. the node the player tapped, when it is on the board;
        ///   2. else the FIRST per-track Next in board order, so an untouched board still has
        ///      one entry read (and the pointer VFX still has exactly one node to sit on);
        ///   3. else "" — no oversized plate at all.
        /// It NEVER returns a set. The per-track Next signal is presented by
        /// BuildNextTrackMarker at NORMAL size instead. TalentFocusSingletonRegression pins it.
        /// </summary>
        public static string ResolveFocusNodeId(IList<string> ids, IList<SkillNodeState> states,
                                                string selectedId)
        {
            if (ids == null || states == null) return "";
            int n = Mathf.Min(ids.Count, states.Count);
            if (!string.IsNullOrEmpty(selectedId))
            {
                for (int i = 0; i < n; i++)
                    if (string.Equals(ids[i], selectedId, StringComparison.Ordinal)) return selectedId;
            }
            for (int i = 0; i < n; i++)
                if (states[i] == SkillNodeState.Next && !string.IsNullOrEmpty(ids[i])) return ids[i];
            return "";
        }

        /// <summary>Plate edge in ref px. It depends on FOCUS AND NOTHING ELSE — no state may
        /// ever buy itself size (WO-1021 sec 2.1d). Pinned by TalentFocusSingletonRegression.</summary>
        public static float NodePlateSizePx(bool focus)
        {
            return focus ? NodeFocusPx : NodeSizePx;
        }

        // Gold progression line between plate centres (RPG talent-tree standard:
        // structure is ALWAYS visible; state is carried by thickness + alpha, never by hiding the line).
        private static void BuildGraphConnector(Transform host, Vector2 fromDown, Vector2 toDown,
                                                SkillTrackNodeVM parent, SkillTrackNodeVM child)
        {
            float x0 = fromDown.x, y0 = fromDown.y;
            float x1 = toDown.x, y1 = toDown.y;
            float dx = x1 - x0, dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 8f) return;

            // Inset by half a plate so the bar meets the rim, not the icon centre.
            float inset = NodeSizePx * ConnectorInsetFrac;
            if (len <= inset * 2f + 4f) return;
            float ux = dx / len, uy = dy / len;
            float ax = x0 + ux * inset, ay = y0 + uy * inset;
            float bx = x1 - ux * inset, by = y1 - uy * inset;
            float mx = (ax + bx) * 0.5f, my = (ay + by) * 0.5f;
            float barLen = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            float angle = Mathf.Atan2(-(by - ay), (bx - ax)) * Mathf.Rad2Deg;

            bool parentActive = parent.State == SkillNodeState.Owned || parent.State == SkillNodeState.Planned;
            bool childActive = child.State == SkillNodeState.Owned || child.State == SkillNodeState.Planned
                            || child.State == SkillNodeState.Next || child.State == SkillNodeState.Available;
            float thick, alpha;
            if (parentActive && childActive) { thick = ConnectorThickPx; alpha = 0.95f; }
            else if (parentActive) { thick = ConnectorMidPx; alpha = 0.88f; }
            else { thick = ConnectorThinPx; alpha = 0.55f; } // locked path still solid gold

            // Glow underlay (professional double-pass) then core gold stroke.
            BuildRotatedBar(host, mx, my, barLen, thick + 4f, angle, alpha * 0.28f);
            BuildRotatedBar(host, mx, my, barLen, thick, angle, alpha);
        }

        private static void BuildRotatedBar(Transform host, float cxDown, float cyDown,
                                            float length, float thickness, float angleDeg, float alpha)
        {
            var go = new GameObject("Edge", typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(length, thickness);
            rt.anchoredPosition = new Vector2(cxDown, -cyDown);
            rt.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            var img = go.GetComponent<Image>();
            img.color = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, alpha);
            img.raycastTarget = false;
        }

        // ── ONE NODE ON THE GRAPH (RPG talent plate) ─────────────────────────────
        // Art is king. Locks are quiet (dim + small corner glyph). Focus is LOUD
        // (size + double gold ring). Rank sits on the plate top like the Obsidian demo.

        private void BuildGraphNode(SkillTrackNodeVM seat, float cxDown, float cyDown, bool focus)
        {
            var node = seat.Node;
            if (string.IsNullOrEmpty(node.Id)) return;
            var state = seat.State;

            float size = NodePlateSizePx(focus);

            var go = new GameObject("Node_" + node.Id, typeof(Image), typeof(Button));
            go.transform.SetParent(_graphContent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cxDown, -cyDown);
            rt.sizeDelta = new Vector2(size, size);

            var img = go.GetComponent<Image>();
            Sprite border = TalentBorderSprite(node);
            if (border != null)
            {
                img.sprite = border;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
                img.color = BorderTintFor(state, focus);
            }
            else
            {
                ElarionUiKit.ApplyRounded(img);
                img.color = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, BorderAlphaFor(state));
            }

            float borderW = focus ? 5f : BorderWidthFor(state);

            // Dark plate behind the skill art (full-bleed art reads like AAA talent trees).
            var fillGo = new GameObject("Fill", typeof(Image));
            fillGo.transform.SetParent(go.transform, false);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            float inset = border != null ? size * 0.12f : borderW;
            fillRt.offsetMin = new Vector2(inset, inset);
            fillRt.offsetMax = new Vector2(-inset, -inset);
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.sprite = ElarionUiKit.CircleSprite;
            fillImg.type = Image.Type.Simple;
            fillImg.preserveAspect = true;
            fillImg.color = PlateFillFor(state);
            fillImg.raycastTarget = false;

            // The approved medallion is circular. Clip square source illustrations to its
            // inner well so old talent-slot corners never protrude through the gold bezel.
            var artMask = fillGo.AddComponent<Mask>();
            artMask.showMaskGraphic = true;

            // FOCUS: double gold frame (demo / PoE / Diablo style selected node).
            if (focus)
            {
                BuildOuterRing(go.transform, 0.10f,
                    new Color(1f, 0.92f, 0.45f, 0.55f));
            }
            else if (node.IsCapstone)
            {
                BuildOuterRing(go.transform, 0.05f,
                    new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b,
                              state == SkillNodeState.Locked ? 0.35f : 0.80f));
            }
            if (state == SkillNodeState.Planned)
                BuildOuterRing(go.transform, 0.045f,
                    new Color(ElarionUi.Affordable.r, ElarionUi.Affordable.g, ElarionUi.Affordable.b, 0.85f));

            // Owner picks 2026-08-16 - attached AFTER every ring so each patch's
            // SetAsFirstSibling lands BEHIND the rings (the patches are opaque well-ink
            // RT quads; a ring first-sibling'd after a patch would draw behind it and
            // vanish). ADDITIVE presentation: nothing above is removed or replaced.
            //   POINTER -> the focus plate.  AURA -> owned plates (DEFAULT, not a lock
            //   - see IsAuraNode; one owner word flips the target state).
            if (focus) AttachPointerVfx(go.transform);
            if (IsAuraNode(state)) AttachAuraVfx(go.transform);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ElarionUiKit.StyleButtonColors(btn);
            btn.interactable = true;
            string id = node.Id;
            btn.onClick.AddListener(() => { if (_vm != null) _vm.Select(id); });

            bool locked = state == SkillNodeState.Locked;
            bool inert = state == SkillNodeState.Inert;
            var sprite = LoadIcon(node.IconPath);
            if (sprite != null)
            {
                var iconGo = new GameObject("Icon", typeof(Image));
                iconGo.transform.SetParent(fillGo.transform, false);
                var ir = iconGo.GetComponent<RectTransform>();
                // Larger art well — skill art is the product, not the chrome.
                ir.anchorMin = Vector2.zero;
                ir.anchorMax = Vector2.one;
                ir.offsetMin = Vector2.zero; ir.offsetMax = Vector2.zero;
                var iImg = iconGo.GetComponent<Image>();
                iImg.sprite = sprite;
                iImg.preserveAspect = true;
                iImg.raycastTarget = false;
                // Full colour when open; locked = dimmed ART (not a giant padlock over it).
                if (state == SkillNodeState.Owned)
                    iImg.color = Color.white;
                else if (locked || inert)
                    iImg.color = new Color(0.72f, 0.72f, 0.76f, 0.88f);
                else
                    iImg.color = Color.white;
            }
            else
            {
                string mono = string.IsNullOrEmpty(node.Name)
                    ? "?" : node.Name.Substring(0, Mathf.Min(2, node.Name.Length));
                var monoLbl = ElarionUiKit.Label(go.transform, mono, 0.22f, 0.78f,
                    locked ? ElarionUi.ParchmentDim : ElarionUi.Parchment,
                    ElarionUi.FontTitle, TMPro.TextAlignmentOptions.Center, 0.10f, 0.90f, bold: true);
                ElarionUiKit.FitSingleLine(monoLbl);
            }

            // Rank — demo grammar, top of plate, small so it doesn't eat the art.
            string rank = (state == SkillNodeState.Owned || state == SkillNodeState.Planned) ? "1/1" : "0/1";
            Color rankInk = state == SkillNodeState.Owned ? ElarionUi.Gilt
                          : locked ? new Color(0.75f, 0.72f, 0.68f, 0.75f)
                          : ElarionUi.Parchment;
            // WO-1310: the rank owns the WHOLE top band now that the type word has moved down
            // into the nameplate. It used to share x 0.12-0.88 with a type badge pinned across
            // x 0.02-0.68, so a glyph of "0/1" painted straight through the badge word and the
            // owner read the pair as one truncated string ("AC1...").
            var rankLbl = ElarionUiKit.Label(go.transform, rank, 0.72f, 0.96f, rankInk,
                (int)ElarionUiKit.FontFloor, TMPro.TextAlignmentOptions.Center, 0.24f, 0.76f, bold: true);
            rankLbl.raycastTarget = false;
            ElarionUiKit.FitSingleLine(rankLbl, ElarionUiKit.FontHardFloor);

            // Mobile-first recognition: icon-only trees force trial-and-error tapping. Keep the
            // art dominant, but state the authoritative talent name in the unused pitch beneath
            // every plate. The dark nameplate is shape-backed, so it stays readable over VFX and
            // does not rely on the node tint.
            //
            // Type is stated in WORDS, not inferred from colour (the colourblind carrier), and it
            // rides the SAME plate as the name on its own line. Assigned actives name the same
            // numbered seat shown in the persistent quick-swap rail.
            // WO-1522: an INERT node (no runtime consumer) states COMING on the SAME word band
            // every other node uses. A word, on the plate, in the capture - so the player never
            // has to open the node to learn it grants nothing, and the cue survives greyscale.
            // The reason it is inert is a FlowTrace line, never a label (HeroSkillTreeVM
            // .DeadNodePlayerLine): the authoring note is not player copy.
            string typeBadge = inert
                ? "COMING"
                : node.Kind == SkillNodeKind.Skill
                    ? (node.EquippedSlot > 0 ? "SLOT " + node.EquippedSlot : new LocalizedText("village.talent_workspace.type_badge_active").Resolve())
                    : new LocalizedText("village.talent_workspace.type_badge_passive").Resolve();
            BuildNodeNamePlate(go.transform, node.Name, typeBadge,
                node.Kind == SkillNodeKind.Skill ? ElarionUi.Gilt : ElarionUi.Parchment, locked);

            if (inert) BuildNodeSlash(go.transform, size);

            // Quiet state badges only — never a padlock the size of the icon.
            switch (state)
            {
                case SkillNodeState.Owned:
                    // Subtle owned tick — art already reads "yours" via full colour + 1/1.
                    break;
                case SkillNodeState.Planned:
                    BuildQuietCornerPip(go.transform, "-" + node.WisdomCost, ElarionUi.Affordable);
                    break;
                case SkillNodeState.Next:
                    // WO-1021 sec 2.1d: per-track NEXT is carried by a QUIET badge at NORMAL
                    // size — shape (chevron) + position (bottom-left, where nothing else lives),
                    // never by growing the plate. Size belongs to the board-level SELECTION.
                    BuildNextTrackMarker(go.transform);
                    BuildQuietCornerPip(go.transform, node.WisdomCost.ToString(), ElarionUi.Parchment);
                    break;
                case SkillNodeState.Available:
                    BuildQuietCornerPip(go.transform, node.WisdomCost.ToString(), ElarionUi.Parchment);
                    break;
                case SkillNodeState.Inert:
                    BuildQuietCornerPip(go.transform, "!", ElarionUi.Parchment);
                    break;
                default:
                    // Locked: tiny lock in corner, art stays visible underneath.
                    BuildQuietLockCorner(go.transform);
                    break;
            }
        }

        // -- Owner picks 2026-08-16: node VFX patches ------------------------------
        // One shared off-screen rig PER PICK (lazy, panel-lifetime); each target plate
        // gets a RawImage SAMPLING the rig's RenderTexture, seated FIRST-SIBLING so the
        // gold rings, plate, art and pips all draw over it. Patches die with the node on
        // every rebuild (ClearContent); the rigs persist until Close() disposes them.

        /// <summary>AURA TARGET STATE - DEFAULT, NOT A LOCK (owner 2026-08-16 pick,
        /// "Node Auras"). Default = OWNED/learned nodes (the lit prestige read on talents
        /// the player has taken). The owner can flip it to available-to-buy nodes in one
        /// word: change this to (state == Next || state == Available).</summary>
        private static bool IsAuraNode(SkillNodeState state)
        {
            return state == SkillNodeState.Owned;
        }

        private void AttachPointerVfx(Transform nodeRoot)
        {
            if (_pointerVfxUnavailable) return;   // Begin already Warned once this open
            if (_pointerVfx == null)
            {
                _pointerVfx = TalentNodeVfxRig.CreatePointer();
                if (!_pointerVfx.Begin())
                {
                    // Begin logged the Warn (missing mirror / RT failure). Keep the
                    // code-built focus ring as the sole pointer; never retry per repaint.
                    _pointerVfx.Dispose();
                    _pointerVfx = null;
                    _pointerVfxUnavailable = true;
                    return;
                }
                FlowTrace.Step("TalentPointer", "attach: rig live - pointer loop presents on the focus node " +
                                                "(additive to the gold ring)");
            }
            if (!_pointerVfx.IsValid) return;
            // Peeks well past the plate so the loop reads as a marker OVER the node,
            // not a texture trapped inside it.
            BuildVfxPatch(nodeRoot, "PointerVfx", _pointerVfx.Texture, 0.35f);
        }

        private void AttachAuraVfx(Transform nodeRoot)
        {
            if (_auraVfxUnavailable) return;
            if (_auraVfx == null)
            {
                _auraVfx = TalentNodeVfxRig.CreateAura();
                if (!_auraVfx.Begin())
                {
                    // Begin logged the Warn. Owned plates keep their code-built gold
                    // border + rank 1/1 prestige read; never retry per repaint.
                    _auraVfx.Dispose();
                    _auraVfx = null;
                    _auraVfxUnavailable = true;
                    return;
                }
                FlowTrace.Step("TalentAura", "attach: rig live - aura presents on owned nodes " +
                                             "(one shared instance sampled per node patch)");
            }
            if (!_auraVfx.IsValid) return;
            // Tighter halo than the pointer - an aura hugs its plate; the pointer floats.
            BuildVfxPatch(nodeRoot, "AuraVfx", _auraVfx.Texture, 0.25f);
        }

        /// <summary>One RT-sampling RawImage patch behind a node plate. Opaque well-ink
        /// quad, so it must be first-sibling'd AFTER every ring on the node is built.</summary>
        private static void BuildVfxPatch(Transform nodeRoot, string name,
                                          RenderTexture texture, float peek)
        {
            var patch = new GameObject(name, typeof(RawImage));
            patch.transform.SetParent(nodeRoot, false);
            var pr = (RectTransform)patch.transform;
            pr.anchorMin = new Vector2(-peek, -peek);
            pr.anchorMax = new Vector2(1f + peek, 1f + peek);
            pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;
            var raw = patch.GetComponent<RawImage>();
            raw.texture = texture;
            raw.color = Color.white;
            raw.raycastTarget = false;
            // ⚠ "behind rings" is true; "behind plate/art" is NOT. nodeRoot owns the node's own
            // Image, and a parent's Graphic draws BEFORE all of its children - so this quad,
            // grown 25-35% past the node, draws OVER the plate. It reads correctly only while
            // the VFX render texture stays largely transparent; a camera clear that fills the
            // RT would black out every focused/owned node. Left as-is (WO-1310 is awaiting the
            // owner's felt-verify and this is the shipped look), but it is the same trap as the
            // skills ConfirmRing and the Journey RAIDS card, not a separate one.
            patch.transform.SetAsFirstSibling();  // behind rings; see the note above re: plate/art
        }

        private void Update()
        {
            // URP never auto-renders the rigs' off-screen cameras; drive them while the
            // panel is open so the loops actually animate. One Render per rig per frame,
            // shared by every patch sampling that rig's texture. No-op otherwise.
            if (_ui == null) return;
            if (_pointerVfx != null) _pointerVfx.RenderTick();
            if (_auraVfx != null) _auraVfx.RenderTick();
        }

        /// <summary>Small bottom-right cost/state pip — never covers the skill art centre.</summary>
        private static void BuildQuietCornerPip(Transform nodeRoot, string glyph, Color ink)
        {
            var pip = new GameObject("Pip", typeof(Image));
            pip.transform.SetParent(nodeRoot, false);
            var pr = (RectTransform)pip.transform;
            pr.anchorMin = new Vector2(0.68f, 0.02f);
            pr.anchorMax = new Vector2(0.98f, 0.28f);
            pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;
            var pImg = pip.GetComponent<Image>();
            ElarionUiKit.ApplyRounded(pImg);
            pImg.color = new Color(0.04f, 0.035f, 0.05f, 0.88f);
            pImg.raycastTarget = false;
            if (!string.IsNullOrEmpty(glyph))
            {
                var lbl = ElarionUiKit.Label(pip.transform, glyph, 0.05f, 0.95f, ink,
                    ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
                lbl.raycastTarget = false;
                // WO-1697: the Seeker captured floor 30 -> 26, finalSize 28 on these
                // pips. A normal node's fractional band is only 31.82 reference px.
                // Seat THIS font's full floor line in pixels, growing upward from
                // the same bottom-right corner; neither fitting floor is relaxed.
                float bandPx = ElarionUiKit.MinBandPxForFloor(lbl);
                const float paddingPx = 2f;
                pr.anchorMax = new Vector2(pr.anchorMax.x, pr.anchorMin.y);
                pr.pivot = new Vector2(pr.pivot.x, 0f);
                pr.sizeDelta = new Vector2(pr.sizeDelta.x, bandPx + 2f * paddingPx);
                pr.anchoredPosition = new Vector2(pr.anchoredPosition.x, 0f);
                var labelRt = lbl.rectTransform;
                labelRt.anchorMin = new Vector2(labelRt.anchorMin.x, 0.5f);
                labelRt.anchorMax = new Vector2(labelRt.anchorMax.x, 0.5f);
                labelRt.pivot = new Vector2(labelRt.pivot.x, 0.5f);
                labelRt.sizeDelta = new Vector2(labelRt.sizeDelta.x, bandPx);
                labelRt.anchoredPosition = new Vector2(labelRt.anchoredPosition.x, 0f);
                ElarionUiKit.FitSingleLine(lbl);
            }
        }

        /// <summary>The ACTIVE/PASSIVE/SLOT N word, seated on the LOWER LINE OF THE NAMEPLATE
        /// (WO-1310) rather than over the skill art. It survives greyscale and makes
        /// hot-swappability readable without opening every node.
        ///
        /// WHY IT MOVED, and why it was NOT deleted: over the art it had roughly 0.66 x 136 = 90
        /// ref px, and FitSingleLine's default minimum is ElarionUiKit.FontFloor (30) - not the
        /// FontHardFloor (20). "PASSIVE" at 30 px bold needs about 115 px, so the badge
        /// ELLIPSISED to three or four glyphs on every plate ("SLI...", "AC1...", "N..."), on
        /// top of the skill icon, while the full name repeated below it. The word carries the
        /// ACTIVE/PASSIVE/SLOT-N state that the colourblind law forbids leaving to colour, so
        /// deleting it was never the fix - it needed a band wide enough to hold it. The
        /// nameplate is 1.56 plate-widths, which is that band.</summary>
        private static void BuildNodeTypeBadge(Transform plateRoot, string text, Color ink)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var badge = new GameObject("TypeBadge", typeof(RectTransform));
            badge.transform.SetParent(plateRoot, false);
            var rt = (RectTransform)badge.transform;
            rt.anchorMin = new Vector2(0.04f, 0.05f);
            rt.anchorMax = new Vector2(0.96f, 0.38f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var label = ElarionUiKit.Label(badge.transform, text, 0.02f, 0.98f, ink,
                ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center, 0.02f, 0.98f,
                spacing: 0.5f, bold: true);
            label.raycastTarget = false;
            // Explicit hard floor: the default floor is FontFloor(30), which is what ellipsised
            // this word on the plate. The band is wide enough that the fit will not need it.
            ElarionUiKit.FitSingleLine(label, ElarionUiKit.FontHardFloor, ElarionUi.FontMicro);
        }

        /// <summary>The one name per node (WO-1310 acceptance 3), hung UNDER the plate with the
        /// type word on a second line. NamePlateHangFrac mirrors anchorMin.y here and is what
        /// the row pitch and the content inset are derived from, so this plate can never be
        /// sliced by the RectMask2D nor painted over the plate on the row below.</summary>
        private static void BuildNodeNamePlate(Transform nodeRoot, string text, string typeWord,
                                               Color typeInk, bool locked)
        {
            bool hasName = !string.IsNullOrWhiteSpace(text);
            bool hasType = !string.IsNullOrWhiteSpace(typeWord);
            if (!hasName && !hasType) return;
            var plate = new GameObject("NamePlate", typeof(Image));
            plate.transform.SetParent(nodeRoot, false);
            var rt = (RectTransform)plate.transform;
            rt.anchorMin = new Vector2(-0.28f, -NamePlateHangFrac);
            rt.anchorMax = new Vector2(1.28f, 0.01f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var image = plate.GetComponent<Image>();
            ElarionUiKit.ApplyRounded(image);
            image.color = new Color(0.025f, 0.022f, 0.028f, 0.96f);
            image.raycastTarget = false;
            if (hasName)
            {
                var label = ElarionUiKit.Label(plate.transform, text.ToUpperInvariant(),
                    hasType ? 0.42f : 0.08f, 0.95f,
                    locked ? ElarionUi.ParchmentDim : ElarionUi.Parchment,
                    18, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f,
                    spacing: 0.25f, bold: true);
                label.raycastTarget = false;
                label.textWrappingMode = TMPro.TextWrappingModes.Normal;
                ElarionUiKit.FitBlock(label, 14f, 18f);
            }
            if (hasType) BuildNodeTypeBadge(plate.transform, typeWord, typeInk);
        }

        /// <summary>Per-track NEXT badge ink / disc (WO-1021 sec 2.1d). PUBLIC so the oracle can
        /// read them and PROVE the cue survives greyscale: a near-white chevron on a near-black
        /// disc is a Rec.709 luma gap of ~0.9, so the badge reads with hue stripped entirely and
        /// is never a hue-only signal. The cue is also SHAPE-carried (a chevron, not a tint) and
        /// POSITION-carried (bottom-left, where no other plate element lives), at NORMAL size.</summary>
        public static readonly Color NextMarkerInk = new Color(0.98f, 0.96f, 0.90f, 0.95f);
        /// <summary>Backing disc for the NEXT badge — near-black so the chevron reads on any art.</summary>
        public static readonly Color NextMarkerDisc = new Color(0.05f, 0.045f, 0.06f, 0.92f);

        /// <summary>The per-track NEXT cue: a small upward chevron badge in the plate's BOTTOM-LEFT
        /// corner. Built from bars, not a font glyph (the TMP font has no chevron), and it never
        /// changes the plate's size — that channel belongs to the board-level selection.</summary>
        private static void BuildNextTrackMarker(Transform nodeRoot)
        {
            var host = new GameObject("NextMarker", typeof(RectTransform));
            host.transform.SetParent(nodeRoot, false);
            var hr = (RectTransform)host.transform;
            // BOTTOM-LEFT: the only quadrant no other plate element claims. The rank pip owns
            // the top band (y 0.72-0.96), the cost pip and the padlock own bottom-RIGHT
            // (x 0.68-0.98), and the art well is 0.14-0.86. Position is half the cue.
            // WO-1522 - THE BAND WAS TOO NARROW FOR ITS OWN WORD. Device frame
            // owner-screen-20260906-202355.png shows this badge reading "N..." on the top node.
            // Arithmetic: the old band x 0.03..0.48 is 0.45 of a ~136 ref-px plate = 61 px, less
            // the label's own 0.08..0.92 inset = ~51 px of text width. FitSingleLine CLAMPS its
            // minSize UP to FontHardFloor (20) - the `0f` passed below can never resolve lower -
            // and "NEXT" bold at 20 px with characterSpacing 1 needs ~55 px. So it ellipsised at
            // the floor, exactly as designed, on a band that could not hold the word.
            // The cost pip starts at x 0.68, so 0.02..0.64 is free and is what is taken here;
            // the spacing is dropped to 0 because letter-spacing was buying nothing but width.
            hr.anchorMin = new Vector2(0.02f, 0.03f);
            hr.anchorMax = new Vector2(0.64f, 0.27f);
            hr.offsetMin = Vector2.zero; hr.offsetMax = Vector2.zero;

            var disc = new GameObject("Disc", typeof(Image));
            disc.transform.SetParent(host.transform, false);
            var dr = (RectTransform)disc.transform;
            dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one;
            dr.offsetMin = Vector2.zero; dr.offsetMax = Vector2.zero;
            var dImg = disc.GetComponent<Image>();
            ElarionUiKit.ApplyRounded(dImg);
            dImg.color = NextMarkerDisc;
            dImg.raycastTarget = false;

            var label = ElarionUiKit.Label(host.transform, "NEXT", 0.08f, 0.92f,
                NextMarkerInk, ElarionUi.FontMicro, TMPro.TextAlignmentOptions.Center,
                0.04f, 0.96f, spacing: 0f, bold: true);
            label.raycastTarget = false;
            // Floor stated EXPLICITLY rather than via 0f, so the arithmetic above is checkable
            // against the constant it actually resolves to.
            ElarionUiKit.FitSingleLine(label, ElarionUiKit.FontHardFloor, ElarionUi.FontMicro);
        }

        /// <summary>Tiny padlock in the corner — locked is "dim art + small glyph", not a wall of UI.</summary>
        private static void BuildQuietLockCorner(Transform nodeRoot)
        {
            var host = new GameObject("QuietLock", typeof(RectTransform));
            host.transform.SetParent(nodeRoot, false);
            var hr = (RectTransform)host.transform;
            hr.anchorMin = new Vector2(0.70f, 0.04f);
            hr.anchorMax = new Vector2(0.96f, 0.30f);
            hr.offsetMin = Vector2.zero; hr.offsetMax = Vector2.zero;

            var disc = new GameObject("Disc", typeof(Image));
            disc.transform.SetParent(host.transform, false);
            var dr = (RectTransform)disc.transform;
            dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one;
            dr.offsetMin = Vector2.zero; dr.offsetMax = Vector2.zero;
            var dImg = disc.GetComponent<Image>();
            ElarionUiKit.ApplyRounded(dImg);
            dImg.color = new Color(0.04f, 0.035f, 0.05f, 0.82f);
            dImg.raycastTarget = false;

            var ink = new Color(ElarionUi.Parchment.r, ElarionUi.Parchment.g, ElarionUi.Parchment.b, 0.80f);
            // Compact lock shape (body + shackle) — scaled for the corner disc.
            LockBar(host.transform, new Vector2(0.5f, 0.34f), new Vector2(14f, 11f), ink);
            LockBar(host.transform, new Vector2(0.36f, 0.58f), new Vector2(2.5f, 9f), ink);
            LockBar(host.transform, new Vector2(0.64f, 0.58f), new Vector2(2.5f, 9f), ink);
            LockBar(host.transform, new Vector2(0.5f, 0.70f), new Vector2(11f, 2.5f), ink);
        }

        private static Sprite TalentBorderSprite(SkillNodeVM node)
        {
            // One canonical circular grammar across Skills and generic item sockets. State is
            // communicated by tint, rank, words and glow—not by stacking legacy slot frames.
            return Resources.Load<Sprite>(
                "UI/ElarionMedieval/frames/circular-bezel-four-point");
        }

        private static Color BorderTintFor(SkillNodeState state, bool focus)
        {
            // Preserve the authored black-iron/antique-gold pixels. Multiplying this sprite
            // by the former grey state colours made the approved bezel effectively vanish.
            // Locked state already has dimmed art plus an explicit lock glyph.
            if (focus) return Color.white;
            switch (state)
            {
                case SkillNodeState.Owned:
                    return Color.white;
                case SkillNodeState.Planned:
                    return Color.white;
                case SkillNodeState.Next:
                case SkillNodeState.Available:
                    return Color.white;
                case SkillNodeState.Inert:
                    return new Color(0.78f, 0.78f, 0.78f, 0.82f);
                default:
                    return new Color(0.88f, 0.88f, 0.88f, 0.90f);
            }
        }

        /// <summary>A fixed-pixel child rect seated from its parent's TOP-LEFT corner
        /// (<paramref name="y"/> counts DOWN). Kept for band helpers / ability tiles.</summary>
        private static RectTransform PxRect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        // A quiet circular state glow behind the one canonical bezel. This deliberately uses
        // no second ornamental frame, avoiding the doubled legacy-ring appearance.
        //
        // ⚠ THIS IS NOT "BEHIND" ANYTHING, AND IT SURVIVES ONLY BECAUSE ITS SPRITE IS HOLLOW.
        // nodeRoot carries the node's OWN Image (BuildNode sets btn.targetGraphic = img), and in
        // uGUI a parent's Graphic draws BEFORE every one of its children - so SetAsFirstSibling
        // orders this first among SIBLINGS while still drawing ON TOP OF THE PLATE. What keeps
        // that harmless is ElarionUiKit.RingSprite being a genuine ring with a transparent
        // centre; swap it for a filled sprite, or drop the sprite so ApplyRounded's filled quad
        // stands in, and this becomes a coloured slab over the node art grown past its rect.
        // That is the defect chain already seen three times: the skills ConfirmRing, the Journey
        // RAIDS card, and this. If a filled highlight is ever wanted here, build it the way
        // BuildSpendPopup now does - an image-less container on the exact target rect holding
        // thin INSET edge bars - never a full-rect fill, never grown outside the rect.
        private static void BuildOuterRing(Transform nodeRoot, float grow, Color color)
        {
            var ring = new GameObject("StateGlow", typeof(Image));
            ring.transform.SetParent(nodeRoot, false);
            var rr = ring.GetComponent<RectTransform>();
            rr.anchorMin = new Vector2(-grow, -grow);
            rr.anchorMax = new Vector2(1f + grow, 1f + grow);
            rr.offsetMin = Vector2.zero; rr.offsetMax = Vector2.zero;
            var rImg = ring.GetComponent<Image>();
            rImg.sprite = ElarionUiKit.RingSprite;
            rImg.type = Image.Type.Simple;
            rImg.preserveAspect = true;
            rImg.color = color;
            rImg.raycastTarget = false;
            ring.transform.SetAsFirstSibling();
        }

        // WO-910 inert mark — one bar struck across the plate face.
        private static void BuildNodeSlash(Transform nodeRoot, float size)
        {
            var bar = new GameObject("InertSlash", typeof(Image));
            bar.transform.SetParent(nodeRoot, false);
            var br = (RectTransform)bar.transform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f);
            br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(size * 0.74f, 5f);
            br.localRotation = Quaternion.Euler(0f, 0f, -22f);
            var bImg = bar.GetComponent<Image>();
            bImg.color = new Color(ElarionUi.Parchment.r, ElarionUi.Parchment.g, ElarionUi.Parchment.b, 0.75f);
            bImg.raycastTarget = false;
        }

        // Locked = a PADLOCK drawn from four bars inside the badge disc (font-free: the TMP
        // font has no lock glyph, and a SHAPE satisfies the colourblind law).
        private static void BuildNodeLockGlyph(Transform nodeRoot)
        {
            var pr = BuildNodeStamp(nodeRoot, null, Color.clear);
            var ink = new Color(ElarionUi.Parchment.r, ElarionUi.Parchment.g, ElarionUi.Parchment.b, 0.85f);

            LockBar(pr, new Vector2(0.5f, 0.36f), new Vector2(26f, 20f), ink);   // body
            LockBar(pr, new Vector2(0.34f, 0.62f), new Vector2(4f, 16f), ink);   // shackle left
            LockBar(pr, new Vector2(0.66f, 0.62f), new Vector2(4f, 16f), ink);   // shackle right
            LockBar(pr, new Vector2(0.5f, 0.74f), new Vector2(20f, 4f), ink);    // shackle top
        }

        private static void LockBar(Transform parent, Vector2 anchor, Vector2 size, Color ink)
        {
            var go = new GameObject("Lock", typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = ink;
            img.raycastTarget = false;
        }

        // Small bottom-right pip disc: dark plate + a short glyph ("-2", "3").
        // ASCII only — eyes-on 2026-07-03: ✓/✗/− are missing from the TMP font.
        private static RectTransform BuildNodeStamp(Transform nodeRoot, string glyph, Color color)
        {
            var pip = new GameObject("Stamp", typeof(Image));
            pip.transform.SetParent(nodeRoot, false);
            var pr = (RectTransform)pip.transform;
            pr.anchorMin = new Vector2(0.62f, -0.06f);
            pr.anchorMax = new Vector2(1.06f, 0.38f);
            pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;
            var pImg = pip.GetComponent<Image>();
            ElarionUiKit.ApplyRounded(pImg);
            pImg.color = new Color(0.05f, 0.045f, 0.06f, 0.92f);   // near-black disc
            pImg.raycastTarget = false;

            if (!string.IsNullOrEmpty(glyph))
            {
                var lbl = ElarionUiKit.Label(pip.transform, glyph, 0.08f, 0.92f, color,
                    ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
                lbl.raycastTarget = false;
                ElarionUiKit.FitSingleLine(lbl);
            }
            return pr;
        }

        // Owned = a CHECK stamp drawn from two rotated gilt bars (no font dependency —
        // the TMP font has no ✓ glyph; a shape also satisfies the colorblind law).
        private static void BuildNodeCheckStamp(Transform nodeRoot)
        {
            var pr = BuildNodeStamp(nodeRoot, null, Color.clear);

            var shortBar = new GameObject("CheckA", typeof(Image));
            shortBar.transform.SetParent(pr, false);
            var sr = (RectTransform)shortBar.transform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.34f, 0.38f);
            sr.sizeDelta = new Vector2(13f, 4.5f);
            sr.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var sImg = shortBar.GetComponent<Image>();
            sImg.color = ElarionUi.Gilt;
            sImg.raycastTarget = false;

            var longBar = new GameObject("CheckB", typeof(Image));
            longBar.transform.SetParent(pr, false);
            var lr = (RectTransform)longBar.transform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.60f, 0.50f);
            lr.sizeDelta = new Vector2(20f, 4.5f);
            lr.localRotation = Quaternion.Euler(0f, 0f, -50f);
            var lImg = longBar.GetComponent<Image>();
            lImg.color = ElarionUi.Gilt;
            lImg.raycastTarget = false;
        }

        // Plate FILL — RPG style: always a dark well so skill art stays full-colour.
        // Owned vs open is the gold border + rank 1/1 (not a washed gold plate that kills art).
        private static Color PlateFillFor(SkillNodeState state)
        {
            switch (state)
            {
                case SkillNodeState.Owned:
                    return new Color(0.08f, 0.07f, 0.05f, 0.98f); // warm dark under gold rim
                case SkillNodeState.Planned:
                    return new Color(0.06f, 0.07f, 0.05f, 0.96f);
                case SkillNodeState.Locked:
                    return new Color(0.025f, 0.024f, 0.032f, 0.98f);
                case SkillNodeState.Inert:
                    return new Color(0.035f, 0.033f, 0.040f, 0.96f);
                default:
                    return new Color(0.045f, 0.040f, 0.055f, 0.97f);
            }
        }

        // Gilt LINE border alpha by state (paired with BorderWidthFor — the WIDTH is the
        // colour-free signal; the alpha only reinforces it).
        private static float BorderAlphaFor(SkillNodeState state)
        {
            switch (state)
            {
                case SkillNodeState.Owned: return 0.95f;
                case SkillNodeState.Planned: return 0.90f;
                case SkillNodeState.Next: return 1.00f;
                case SkillNodeState.Available: return 0.75f;
                case SkillNodeState.Inert: return 0.45f;
                default: return 0.28f;
            }
        }

        // Border WIDTH in px — the focus node's ring is 4x the locked node's.
        private static float BorderWidthFor(SkillNodeState state)
        {
            switch (state)
            {
                case SkillNodeState.Next: return 6f;
                case SkillNodeState.Planned: return 3f;
                case SkillNodeState.Available: return 3f;
                case SkillNodeState.Inert: return 3f;
                case SkillNodeState.Owned: return 2f;
                default: return 1.5f;
            }
        }

        // ── Chrome (presentation only) ────────────────────────────────────────────

        private void BuildChrome()
        {
            _ui = ElarionUiKit.BuildModalCanvas("HeroSkillTreePanelMvvmUI", 31000);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            // Outside tap closes the whole panel (replaces the bottom Close button the
            // owner retired 2026-08-15 — "I don't see the value in ... the close").
            ElarionUiKit.Scrim(_ui.transform, onTapClose: () => { if (_vm != null) _vm.Close(); });

            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform,
                HudStrings.HeroFaceLabel(HudStrings.KeyHeroSkills, "chrome"),
                new Vector2(0.07f, 0.05f), new Vector2(0.93f, 0.95f), () => { if (_vm != null) _vm.Close(); },
                headerX0: 0.18f, headerX1: 0.78f, frameName: RpgUiCatalog.FrameTalent,
                medallionIcon: "talent");
            MedievalUiSkin.ApplyShell(chrome);
            var back = ElarionUiKit.ButtonPack(chrome.root.transform, "BACK",
                ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.02f, 0.84f), new Vector2(0.17f, 0.98f),
                () => { if (_vm != null) _vm.Close(); }, RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(back, primary: false);
            if (back != null && back.targetGraphic is Image backImage) backImage.type = Image.Type.Simple;
            var equipment = ElarionUiKit.ButtonPack(chrome.root.transform, "EQUIPMENT",
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.78f, 0.84f), new Vector2(0.98f, 0.98f),
                OpenEquipmentFromSkills, RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(equipment, primary: true);
            if (equipment != null && equipment.targetGraphic is Image equipmentImage) equipmentImage.type = Image.Type.Simple;
            // FrameTalent keeps the generic broad title band unless its imported title and shadow
            // are explicitly re-seated. Reserve a disjoint centre lane between the two actions.
            if (chrome.title != null && chrome.title.transform.parent != null)
            {
                foreach (var heading in chrome.title.transform.parent.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    var headingRect = heading.rectTransform;
                    headingRect.anchorMin = new Vector2(0.22f, headingRect.anchorMin.y);
                    headingRect.anchorMax = new Vector2(0.78f, headingRect.anchorMax.y);
                    headingRect.offsetMin = new Vector2(0f, headingRect.offsetMin.y);
                    headingRect.offsetMax = new Vector2(0f, headingRect.offsetMax.y);
                }
            }
            // Hide the shared bottom Close — the tree is graph-only; scrim / X dismisses.
            if (chrome.close != null) chrome.close.gameObject.SetActive(false);

            var workspace = new GameObject("TalentWorkspace", typeof(RectTransform));
            workspace.transform.SetParent(chrome.content.transform, false);
            var bodyHost = workspace.GetComponent<RectTransform>();
            bodyHost.anchorMin = new Vector2(0.035f, 0.10f);
            bodyHost.anchorMax = new Vector2(0.965f, 0.84f);
            bodyHost.offsetMin = Vector2.zero;
            bodyHost.offsetMax = Vector2.zero;
            Transform panel = bodyHost;
            _headerLabel = chrome.title;

            // FULL body = the graph. No action row, no loadout band, no detail column.
            // (WO-865 band floors stay as public const so SkillsPanelLayoutRegression still
            // pins touch floors; they no longer consume body height.)
            var graphWell = BandHost(panel, "GraphWell", 0f, 1f);
            PinRegion(graphWell, BodyPadPx, BodyPadPx);
            // WO-1401: the floor is DERIVED from the rail's own bands (rail bottom + rail + gap), so
            // growing the rail can never put a node plate over the hint again.
            graphWell.offsetMin = new Vector2(graphWell.offsetMin.x, GraphWellFloorPx);
            graphWell.offsetMax = new Vector2(graphWell.offsetMax.x, -(WisdomBandPx + BandGapPx));
            // WO-1522 - NO GROUND PLATE IS BUILT HERE, and the reason is measured, not stylistic:
            // BuildScrollGraph's viewport fills this same rect with an OPAQUE WellSurface image, so
            // a plate under it would be 100% occluded - paint the player never sees. The ground
            // this well is recessed into is the frame's own textured centre, which
            // MedievalUiSkin.ApplyShell deliberately reveals (it clears the legacy opaque inner
            // fill to alpha 0); covering that again would undo the shell.
            BuildScrollGraph(graphWell);
            // ...and the EDGE. A dark rectangle with no border is indistinguishable from the
            // panel behind it; the bezel is what makes the well read as a hole. Built AFTER the
            // scroll so it draws on top of the viewport's own slab, raycast off so it never eats
            // a drag (the scroll surface underneath owns every gesture).
            BuildElevationPlate(graphWell, "GraphWellBezel", Color.white, bezel: true,
                                inflatePx: WellBezelInsetPx);

            BuildQuickSwapBar(panel);

            // Wisdom owns a header band above the graph so nodes can never render beneath it.
            var pointsPlate = new GameObject("WisdomPlate", typeof(Image));
            pointsPlate.transform.SetParent(panel, false);
            var pointsRt = (RectTransform)pointsPlate.transform;
            pointsRt.anchorMin = new Vector2(WisdomPlateAnchorX0, 1f);
            pointsRt.anchorMax = new Vector2(WisdomPlateAnchorX1, 1f);
            pointsRt.offsetMin = Vector2.zero; pointsRt.offsetMax = Vector2.zero;
            PinBandFromTop(pointsRt, BodyPadPx, WisdomBandPx);
            var pointsImage = pointsPlate.GetComponent<Image>();
            pointsImage.sprite = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (pointsImage.sprite != null)
            {
                pointsImage.type = Image.Type.Simple;
                pointsImage.color = Color.white;
            }
            else
            {
                ElarionUiKit.ApplyRounded(pointsImage);
                pointsImage.color = new Color(0.035f, 0.03f, 0.035f, 0.98f);
            }
            pointsImage.raycastTarget = false;
            _wisdomLabel = ElarionUiKit.Label(pointsPlate.transform,
                "WISDOM 0 - next point at Level 2",
                WisdomLabelInsetX, 1f - WisdomLabelInsetX, ElarionUi.Gold, 20,
                TMPro.TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);
            // The 15f floor is INERT (the kit clamps it up to FontHardFloor 20) -- kept because
            // the suite pins the call token; the real fit is the plate width computed above.
            ElarionUiKit.FitSingleLine(_wisdomLabel, 15f, 20f);
            FlowTrace.Step("SkillTree", "wisdom chip: plate x " +
                WisdomPlateAnchorX0.ToString("F2") + ".." + WisdomPlateAnchorX1.ToString("F2") +
                " of workspace (" + ((WisdomPlateAnchorX1 - WisdomPlateAnchorX0) * 1535.6f).ToString("F0") +
                " u at 1080p / " + ((WisdomPlateAnchorX1 - WisdomPlateAnchorX0) * 1718.0f).ToString("F0") +
                " u at 2670x1200), label inset " + WisdomLabelInsetX.ToString("F2") +
                ", band " + WisdomBandPx.ToString("F0") + " px, fixed 20 px single line.");

            BuildSpendPopup(panel);
        }

        private void OpenEquipmentFromSkills()
        {
            if (_vm != null) _vm.Close();
            PanelRouter.Open(PanelId.EquipmentPanel);
        }

        /// <summary>
        /// Centered spend popup over the graph. INHERITS the common kit chrome
        /// (<see cref="ElarionUiKit.BuildObsidianPanel"/> + FrameCore): ornate frame, gold
        /// border, title band, body well, footer action strip. Never a bare plate (owner
        /// 2026-08-15 Screenshot 191356). Confirm spends; Cancel / dim / Close dismiss.
        /// </summary>
        private void BuildSpendPopup(Transform panel)
        {
            // Full-rect dim so taps don't hit nodes under the card.
            _popupRoot = new GameObject("SpendPopup", typeof(RectTransform), typeof(Image), typeof(Button));
            _popupRoot.transform.SetParent(panel, false);
            var rootRt = (RectTransform)_popupRoot.transform;
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
            var dim = _popupRoot.GetComponent<Image>();
            dim.color = new Color(0.02f, 0.02f, 0.03f, 0.72f);
            dim.raycastTarget = true;
            var dimBtn = _popupRoot.GetComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.onClick.AddListener(() => { if (_vm != null) _vm.ClearSelection(); });
            _popupRoot.SetActive(false);

            // ── Common kit frame (same factory every other panel uses) ─────────────
            // withBackdrop:false — the dim above is the modal veil; FrameCore supplies
            // the gold-bordered plate. Title is re-written on each selection with the
            // talent name so the header band stays the single title surface.
            var chrome = ElarionUiKit.BuildObsidianPanel(
                _popupRoot.transform,
                "Talent",
                // Built on the RIGHT seat; RenderSpendPopup re-anchors per selection.
                new Vector2(PopupSideRightX0, PopupAnchorY0), new Vector2(PopupSideRightX1, PopupAnchorY1),
                () => { if (_vm != null) _vm.ClearSelection(); },
                headerX0: 0.14f, headerX1: 0.86f,
                withBackdrop: false,
                frameName: RpgUiCatalog.FrameCore,
                medallionIcon: "talent");
            MedievalUiSkin.ApplyShell(chrome, compact: true);
            _popupCard = chrome.root != null ? (RectTransform)chrome.root.transform : null;
            // Nested popup: Cancel is the labeled dismiss; hide the shared bottom Close
            // so we don't stack two "leave" affordances under the buttons.
            if (chrome.close != null) chrome.close.gameObject.SetActive(false);

            // WO-1342 (e) — SEAT THE CONTENT INSIDE THE PAINTED FRAME.
            // chrome.content is the factory's full-rect (0..1) transparent layer and EVERY
            // zone (header / body + its ObsidianFill backing plate / footer) is a fraction
            // OF IT, so one inset here moves the whole modal inside the border art. The top
            // needs the full PopupFrameArtTopMarginPx because content-panel.png's 96 px top
            // slice is transparent (see the constant's proof); the other three edges only
            // carry the art's thin margin. Offsets, never anchors: no rect is renamed or
            // re-parented, so a WO-1340 FTUE highlight still resolves by name.
            if (chrome.content != null)
            {
                var contentRt = (RectTransform)chrome.content.transform;
                contentRt.offsetMin = new Vector2(PopupContentSideInsetPx, PopupContentBottomInsetPx);
                contentRt.offsetMax = new Vector2(-PopupContentSideInsetPx, -PopupContentTopInsetPx);
            }

            _popupName = chrome.title;
            if (_popupName != null)
            {
                _popupName.color = ElarionUi.Gilt;
                _popupName.fontStyle = TMPro.FontStyles.Bold;
            }

            var body = (chrome.layout != null && chrome.layout.body != null)
                ? chrome.layout.body
                : (RectTransform)chrome.content.transform;
            var footer = (chrome.layout != null) ? chrome.layout.footer : null;

            // Body: description + spend prompt (chrome-less labels into the frame well).
            // WO-1342 (a): the description owns the WHOLE upper body so the authored sentence
            // WRAPS instead of being culled. FitBlock wraps (textWrappingMode Normal) and
            // truncates SILENTLY, so the band -- not the wrap flag -- is what has to be right.
            // Floors are passed EXPLICITLY: FitBlock's minSize:0 resolves to
            // ElarionUiKit.FontFloor (30), not FontHardFloor (20).
            const float tx0 = 0.06f, tx1 = 0.94f;
            _popupDesc = ElarionUiKit.Label(body, "", PopupDescBandY0, PopupDescBandY1, ElarionUi.Parchment,
                ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Top, tx0, tx1);
            ElarionUiKit.FitBlock(_popupDesc, PopupDescFontMin, PopupDescFontMax);

            // WO-1342 (c) COLOURBLIND LAW: the state line is NEUTRAL parchment, not
            // ElarionUi.Affordable green. This ONE label carries every state
            // (HeroSkillTreeVM.SelectedSpendPrompt -> "Spend N Wisdom for X?" / "Owned -
            // Active skill" / "Owned - Passive - always active" / "Planned - -N Wisdom" /
            // "Costs N Wisdom" / "NO EFFECT YET - ..." / a lock reason) and its colour was
            // set ONCE at build, so green was not distinguishing states -- it was painting an
            // "affordable" cue over lock and no-effect copy. Every state already reads as a
            // distinct WORD; colour stays out of it.
            _popupPrompt = ElarionUiKit.Label(body, "", PopupPromptBandY0, PopupPromptBandY1, ElarionUi.Parchment,
                ElarionUi.FontBody, TMPro.TextAlignmentOptions.Center, tx0, tx1, bold: true);
            ElarionUiKit.FitBlock(_popupPrompt, PopupPromptFontMin, PopupPromptFontMax);

            // Footer (or body floor fallback): Cancel | CONFIRM — kit ButtonPack + touch floor.
            RectTransform btnHost;
            if (footer != null)
            {
                btnHost = footer;
            }
            else
            {
                btnHost = BandHost(body, "PopupActions", 0f, 1f);
                PinBandFromBottom(btnHost, 4f, ActionRowPx);
            }

            _popupCancelBtn = ElarionUiKit.ButtonPack(btnHost, "Cancel", ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.04f, 0.08f), new Vector2(0.46f, 0.92f),
                () => { if (_vm != null) _vm.ClearSelection(); },
                packSpriteName: RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(_popupCancelBtn, primary: false);
            StyleActionLabel(_popupCancelBtn, ElarionUi.Parchment);

            // Emphasis ring — child of the Confirm button so it never outlives it
            // (Screenshot 191356: orphan yellow square when CONFIRM was hidden).
            _popupConfirmBtn = ElarionUiKit.ButtonPack(btnHost, "CONFIRM", ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.54f, 0.08f), new Vector2(0.96f, 0.92f),
                OnPopupConfirm,
                packSpriteName: RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(_popupConfirmBtn, primary: true);
            _popupConfirmLabel = StyleActionLabel(_popupConfirmBtn, ElarionUi.Gilt);

            // WO-1343 EMPHASIS OUTLINE (device capture 2026-09-03, owner: "see learn is
            // selected but that coloring").
            //
            // ⛔ THIS CONTAINER CARRIES NO IMAGE OF ITS OWN, AND THAT IS THE WHOLE FIX.
            // It used to be a SINGLE full-rect Image with ApplyRounded (a FILLED rounded
            // 9-slice, never an outline) tinted ElarionUi.Gold at a=0.80. A uGUI parent's
            // own Graphic draws BEFORE all of its children, so SetAsFirstSibling put that
            // fill ABOVE MedievalUiSkin.ApplyButton's ornate button-normal-empty plate,
            // not behind it: the "ring" painted a flat gold slab over the entire button
            // face and the Gilt label on top of it read as darker gold THROUGH the fill.
            // The -5/+5 growth also pushed it past the button rect toward the frame edge.
            // Four thin BARS along the inside edges leave the plate art fully visible in
            // every state and cannot overflow, because they are inset, never grown.
            //
            // COLOURBLIND LAW: the emphasis is a BORDER plus the near-white brightness step
            // below - shape and luminance, never hue. Greyscale the capture and LEARN still
            // reads as the framed, brighter action next to CANCEL's plain parchment plate.
            // The confirm word (LEARN / OWNED / LOCKED - ConfirmWordFor) stays the primary cue.
            // WO-1410 moved slot assignment to Loadout, so "ASSIGN SLOT n" is no longer a word
            // this button can show; the old comment naming it was stale.
            //
            // The GameObject keeps the name "ConfirmRing" - WO-1340's FTUE highlights
            // resolve targets BY NAME and RenderSpendPopup toggles it by reference.
            var ring = new GameObject("ConfirmRing", typeof(RectTransform));
            ring.transform.SetParent(_popupConfirmBtn.transform, false);
            ring.transform.SetAsFirstSibling();   // under the label, over the plate
            var ringRt = (RectTransform)ring.transform;
            ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = Vector2.zero;
            ringRt.offsetMax = Vector2.zero;      // inset, never grown - cannot overflow
            var edge = new Color(1f, 0.97f, 0.86f, 0.95f);   // near-white: a LUMINANCE step
            ConfirmEdgeBar(ringRt, "Top",    new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(ConfirmOutlineInsetPx, -ConfirmOutlineInsetPx - ConfirmOutlinePx),
                new Vector2(-ConfirmOutlineInsetPx, -ConfirmOutlineInsetPx), edge);
            ConfirmEdgeBar(ringRt, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(ConfirmOutlineInsetPx, ConfirmOutlineInsetPx),
                new Vector2(-ConfirmOutlineInsetPx, ConfirmOutlineInsetPx + ConfirmOutlinePx), edge);
            ConfirmEdgeBar(ringRt, "Left",   new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(ConfirmOutlineInsetPx, ConfirmOutlineInsetPx),
                new Vector2(ConfirmOutlineInsetPx + ConfirmOutlinePx, -ConfirmOutlineInsetPx), edge);
            ConfirmEdgeBar(ringRt, "Right",  new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-ConfirmOutlineInsetPx - ConfirmOutlinePx, ConfirmOutlineInsetPx),
                new Vector2(-ConfirmOutlineInsetPx, -ConfirmOutlineInsetPx), edge);
            _popupConfirmRing = ring;
        }

        private void OnPopupConfirm()
        {
            if (_vm == null) return;
            if (_vm.CanSpendSelected) _vm.SpendSelected();
        }

        /// <summary>Persistent three-slot hot-swap rail. It sits beside the discovery surface so
        /// the player can learn an active, equip it, and recognize the same numbered slot in combat.</summary>
        private void BuildQuickSwapBar(Transform panel)
        {
            _quickSwapHost = BuildQuickSwapRailHost(panel, out _quickSwapStatus);
        }

        /// <summary>
        /// WO-1522 - ONE builder for every surface in the elevation ladder, so the three tiers
        /// can never be authored three different ways.
        ///
        /// <paramref name="bezel"/> true builds the kit's hollow gold border sprite instead of a
        /// fill. STOP: PLATE AND BEZEL ARE NEVER THE SAME IMAGE - that collapse is verbatim the
        /// WO-1515 tan-slab defect: card-frame-empty has a TRANSPARENT CENTRE, so an image that
        /// takes both the fill colour and the frame sprite ends up with no surface at all and
        /// whatever is behind it reads straight through.
        ///
        /// Every plate is raycast OFF. Depth is presentation; it must not take a single tap away
        /// from the scroll surface or the slots it sits behind.
        /// </summary>
        private static void BuildElevationPlate(Transform host, string name, Color fill,
                                                bool bezel, float inflatePx)
        {
            if (host == null) return;
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-inflatePx, -inflatePx);
            rt.offsetMax = new Vector2(inflatePx, inflatePx);
            var img = go.GetComponent<Image>();
            if (bezel)
            {
                // WO-1601: BezelFrameResource, never a literal - card-frame-empty's 96 px slice
                // strips are fully transparent (painted rows 165..713 of 887), so slicing it
                // paints its rails ~10% and ~89% INTO the rect instead of on its edges. See the
                // const's proof block. Every bezel on this screen goes through this one branch.
                var frame = Resources.Load<Sprite>(BezelFrameResource);
                if (frame != null)
                {
                    img.sprite = frame;
                    img.type = Image.Type.Sliced;
                    img.color = Color.white;
                    // §12: state the slice arithmetic that decides whether the border can land on
                    // the edge at all. b*2 > rect means Unity shrinks both strips and the middle
                    // collapses; that is how the shelf bezel went missing while the well bezel
                    // drew a band across the nodes.
                    Vector4 b = frame.border;
                    FlowTrace.Step("SkillTree", "bezel '" + name + "': sprite=" + BezelFrameResource +
                        " " + frame.rect.width.ToString("F0") + "x" + frame.rect.height.ToString("F0") +
                        " border L" + b.x.ToString("F0") + " B" + b.y.ToString("F0") +
                        " R" + b.z.ToString("F0") + " T" + b.w.ToString("F0") +
                        ", Sliced, inflate " + inflatePx.ToString("F0") + " px around " +
                        (host != null ? host.name : "<null>"));
                }
                else
                {
                    // No art: say so rather than painting a white slab over the graph.
                    FlowTrace.Warn("SkillTree", "WO-1522/1601: " + BezelFrameResource + " did not load - '" +
                                                name + "' ships with NO edge, so its surface reads as one " +
                                                "flat black rectangle again.");
                    go.SetActive(false);
                }
            }
            else
            {
                ElarionUiKit.ApplyRounded(img);
                img.color = fill;
            }
            img.raycastTarget = false;
        }

        /// <summary>
        /// WO-1401. The rail host + its hint, as ONE static builder so the regression suite
        /// measures THIS construction and not a copy of it. Two fixed-px bands inside a
        /// <see cref="QuickSwapRailPx"/>-tall rail: the hint owns the TOP
        /// <see cref="QuickSwapHintBandPx"/> (pinned from the top - never a fraction of the rail),
        /// the slots own the BOTTOM <see cref="QuickSwapSlotBandPx"/> (the layout group seats a
        /// MinTouchPx child LOWER; its top padding restates hint + gap so the two agree), and
        /// <see cref="BandGapPx"/> of air separates them at every aspect. Pure View: no VM, no state.
        /// </summary>
        public static RectTransform BuildQuickSwapRailHost(Transform panel, out TMPro.TextMeshProUGUI hint)
        {
            // WO-1522 - THE RAISED SHELF (elevation tier 2). Its own object, a SIBLING of the
            // rail and an earlier one, so the layout group never tries to lay the plate out as a
            // slot and the medallions always draw on top of it. Before this the four slots sat
            // straight on the panel frame with nothing behind them, which is half of why the
            // screen read flat: the loadout was not a place, it was four loose buttons.
            var shelf = new GameObject("QuickSwapShelf", typeof(RectTransform));
            shelf.transform.SetParent(panel, false);
            var shelfRt = (RectTransform)shelf.transform;
            shelfRt.anchorMin = new Vector2(0.02f, 0f);
            shelfRt.anchorMax = new Vector2(0.98f, 0f);
            shelfRt.pivot = new Vector2(0.5f, 0f);
            shelfRt.offsetMin = new Vector2(0f, QuickSwapRailBottomPx);
            shelfRt.offsetMax = new Vector2(0f, QuickSwapRailBottomPx + QuickSwapRailPx);
            BuildElevationPlate(shelfRt, "QuickSwapShelfPlate", RaisedSurface, bezel: false, inflatePx: 0f);
            BuildElevationPlate(shelfRt, "QuickSwapShelfBezel", Color.white, bezel: true,
                                inflatePx: WellBezelInsetPx);

            var rail = new GameObject("QuickSwapRail", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rail.transform.SetParent(panel, false);
            var host = rail.GetComponent<RectTransform>();
            host.anchorMin = new Vector2(0.02f, 0f);
            host.anchorMax = new Vector2(0.98f, 0f);
            host.pivot = new Vector2(0.5f, 0f);
            host.offsetMin = new Vector2(0f, QuickSwapRailBottomPx);
            host.offsetMax = new Vector2(0f, QuickSwapRailBottomPx + QuickSwapRailPx);

            var layout = rail.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 46f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.LowerCenter;
            // Top padding = the hint band + the gap: inner height is then EXACTLY the slot band, so
            // the group's own arithmetic and the band constants describe the same rectangle.
            layout.padding = new RectOffset(0, 0, Mathf.RoundToInt(QuickSwapHintBandPx + BandGapPx), 0);

            // The default sentence is the VM's (HeroSkillTreeVM.QuickSwapStatus); RenderQuickSwapBar
            // overwrites it on the first paint. ASCII only.
            hint = ElarionUiKit.Label(rail.transform,
                "Assigned skills - change them in " + HudStrings.Get(HudStrings.KeyHeroLoadout) + ".", 1f, 1f,
                ElarionUi.ParchmentDim, ElarionUi.FontMicro,
                TMPro.TextAlignmentOptions.Center, 0.02f, 0.98f);
            hint.gameObject.name = "QuickSwapHint";
            hint.transform.SetAsFirstSibling();
            var hintLayout = hint.gameObject.AddComponent<LayoutElement>();
            hintLayout.ignoreLayout = true;
            // FIXED px from the rail's top. The retired shape was Label(y0: 0.78f, y1: 1f) - a fraction
            // whose bottom (0.78 x 132 = 103) sat 9 px inside the 112 px slot band at every aspect.
            PinBandFromTop((RectTransform)hint.transform, 0f, QuickSwapHintBandPx);
            ElarionUiKit.FitSingleLine(hint);

            FlowTrace.Step("SkillTree", "quick-swap rail built: rail " + QuickSwapRailPx.ToString("F0") +
                " px above floor " + QuickSwapRailBottomPx.ToString("F0") + "; slots y 0.." +
                QuickSwapSlotBandPx.ToString("F0") + ", hint y " +
                (QuickSwapRailPx - QuickSwapHintBandPx).ToString("F0") + ".." + QuickSwapRailPx.ToString("F0") +
                " (gap " + BandGapPx.ToString("F0") + "), graph well floor " + GraphWellFloorPx.ToString("F0"));
            return host;
        }

        /// <summary>WO-1401. The slot's tap size - the ONE place the quick-swap button height is
        /// authored, shared with the regression so the pin measures the shipped size. Equal to
        /// <see cref="QuickSwapSlotBandPx"/>, which is the kit touch floor.</summary>
        public static void ApplyQuickSwapSlotSize(GameObject slot)
        {
            if (slot == null) return;
            var le = slot.GetComponent<LayoutElement>();
            if (le == null) le = slot.AddComponent<LayoutElement>();
            le.minWidth = QuickSwapSlotBandPx;
            le.preferredWidth = QuickSwapSlotBandPx;
            le.minHeight = QuickSwapSlotBandPx;
            le.preferredHeight = QuickSwapSlotBandPx;
        }

        private void RenderQuickSwapBar()
        {
            if (_quickSwapHost == null || _vm == null) return;
            for (int i = _quickSwapHost.childCount - 1; i >= 0; i--)
            {
                var child = _quickSwapHost.GetChild(i);
                if (_quickSwapStatus != null && child == _quickSwapStatus.transform) continue;
                // Changed events can repaint more than once in a frame. Destroy is deferred
                // in play mode, so hide the retired card immediately or duplicate slots flash
                // (and appear in deterministic screenshots) until end-of-frame.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            if (_quickSwapStatus != null) _quickSwapStatus.text = _vm.QuickSwapStatus;

            // WO-1522 - the rail leads with the class's LOCKED BASIC (Q), then the three
            // swappable seats. Canon (CLAUDE.md sec.7): Q is locked, W/E/R swap. The rail
            // previously showed only the three, so nothing on the screen said which ability the
            // player can never change - or that a fourth exists at all.
            // COLOURBLIND LAW: locked is carried by the WORD "LOCKED" under the medallion and by
            // the padlock-corner shape, never by a tint. Both seats keep the same bezel art.
            var slots = new List<LoadoutSlotVM>(_vm.QuickSlots.Count + 1);
            var lockedBasic = _vm.LockedBasic;
            bool hasLocked = !lockedBasic.IsEmpty;
            if (hasLocked) slots.Add(lockedBasic);
            slots.AddRange(_vm.QuickSlots);

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                bool isLockedBasic = hasLocked && i == 0;
                string label = slot.IsEmpty ? slot.SlotKey + "\nEMPTY" : string.Empty;
                var btn = ElarionUiKit.BuildObsidianButton(_quickSwapHost, label,
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    slot.IsEmpty ? ElarionUiKit.ObsidianButtonColor.Gray
                                 : ElarionUiKit.ObsidianButtonColor.Yellow,
                    Vector2.zero, Vector2.one,
                    null);
                if (btn != null) btn.interactable = false;
                MedievalUiSkin.ApplyButton(btn, primary: !slot.IsEmpty);
                if (btn != null && btn.targetGraphic is Image slotImage)
                {
                    var bezel = Resources.Load<Sprite>(
                        "UI/ElarionMedieval/frames/circular-bezel-four-point");
                    if (bezel != null) slotImage.sprite = bezel;
                    slotImage.type = Image.Type.Simple;
                    slotImage.preserveAspect = true;
                    slotImage.color = slot.IsEmpty
                        ? new Color(0.72f, 0.72f, 0.72f, 0.88f) : Color.white;
                }
                ApplyQuickSwapSlotSize(btn.gameObject);   // WO-1401: the one authored slot size
                var text = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (text != null)
                {
                    text.textWrappingMode = TMPro.TextWrappingModes.Normal;
                    text.alignment = TMPro.TextAlignmentOptions.Center;
                    ElarionUiKit.FitBlock(text, 18f, ElarionUi.FontLabel);
                }

                if (!slot.IsEmpty)
                {
                    // The canonical concept table owns the art choice. PreserveAspect and
                    // symmetric anchors keep every silhouette centred inside the circle.
                    var icon = ConceptIconResolver.Resolve(slot.AbilityId) ??
                               ConceptIconResolver.DefaultSprite();
                    if (icon != null)
                    {
                        var iconGo = new GameObject("AbilityIcon", typeof(RectTransform), typeof(Image));
                        iconGo.transform.SetParent(btn.transform, false);
                        var iconRt = (RectTransform)iconGo.transform;
                        iconRt.anchorMin = new Vector2(.18f, .18f);
                        iconRt.anchorMax = new Vector2(.82f, .82f);
                        iconRt.offsetMin = iconRt.offsetMax = Vector2.zero;
                        var iconImage = iconGo.GetComponent<Image>();
                        iconImage.sprite = icon;
                        iconImage.preserveAspect = true;
                        iconImage.raycastTarget = false;
                    }

                    var slotBadge = ElarionUiKit.Label(btn.transform, slot.SlotKey,
                        .68f, .94f, ElarionUi.Gilt, 18,
                        TMPro.TextAlignmentOptions.Center, .68f, .94f, bold: true);
                    slotBadge.raycastTarget = false;
                    ElarionUiKit.FitSingleLine(slotBadge, 14f, 18f);

                    if (isLockedBasic)
                    {
                        // The word, not a hue. It rides the OPPOSITE top corner from the "Q"
                        // badge so the two never share a band, and it is stated on every paint -
                        // a locked seat that looks like a swappable one is the defect.
                        var lockWord = ElarionUiKit.Label(btn.transform, "LOCKED",
                            .68f, .94f, ElarionUi.ParchmentDim, 12,
                            TMPro.TextAlignmentOptions.Center, .04f, .62f, bold: true);
                        lockWord.raycastTarget = false;
                        ElarionUiKit.FitSingleLine(lockWord, 0f, 12f);
                    }
                    // WO-1522 - "ARCANE B..." in owner-screen-20260906-202355.png. The old call
                    // was FitSingleLine(name, 10f, 14f): FitSingleLine clamps minSize UP to
                    // FontHardFloor (20), then clamps min DOWN to max, so 10..14 collapsed to a
                    // FIXED 14 px with NO shrink range at all - and 14 px bold across the
                    // .08..0.92 inset of a 112 px slot is ~93 px of text width, while
                    // "ARCANE BLADE" needs ~96. It could only ellipsize.
                    // Two lines is the fix the WO asks for: FitBlock WRAPS, and the band is
                    // opened to .02..0.30 so two line boxes seat inside it.
                    var name = ElarionUiKit.Label(btn.transform, slot.AbilityName.ToUpperInvariant(),
                        .00f, .30f, ElarionUi.Parchment, 13,
                        TMPro.TextAlignmentOptions.Center, .02f, .98f, bold: true);
                    name.raycastTarget = false;
                    name.textWrappingMode = TMPro.TextWrappingModes.Normal;
                    // Height budget, stated: the band is 0.30 x QuickSwapSlotBandPx(112) = 33.6 px;
                    // two 13 px line boxes at the kit's 1.25 multiplier are 32.5 px. FitBlock
                    // truncates SILENTLY, so the band - not the wrap flag - has to be the thing
                    // that is right (the WO-1342 lesson, same shape).
                    ElarionUiKit.FitBlock(name, 0f, 13f);
                }
            }
        }

        // Retired name kept so SkillsPanelLayoutRegression [source] still finds the token.
        // Spend lives in BuildSpendPopup; this is intentionally empty.
        private void BuildActionRow(RectTransform row) { }

        // The scrollable graph viewport (mask) + fixed-size content (nodes/edges).
        // Full body well; RectMask2D clips at the well's edges.
        private void BuildScrollGraph(Transform panel)
        {
            var areaGo = new GameObject("GraphScroll", typeof(RectTransform), typeof(ScrollRect));
            areaGo.transform.SetParent(panel, false);
            var ar = areaGo.GetComponent<RectTransform>();
            ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one;
            ar.offsetMin = Vector2.zero; ar.offsetMax = Vector2.zero;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewportGo.transform.SetParent(areaGo.transform, false);
            var vr = viewportGo.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
            var vImg = viewportGo.GetComponent<Image>();
            // Calm dark canvas (Obsidian demo): near-black slab, no busy grid lines.
            // Opaque so it overrides the frame fill; raycastable so drag-scroll still works.
            // WO-1522: the literal moved to the ELEVATION LADDER (WellSurface) - this is tier 0,
            // the recessed one, and the suite asserts its luma gap against the other two.
            vImg.color = WellSurface;

            var contentGo = new GameObject("GraphContent", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            _graphContent = contentGo.GetComponent<RectTransform>();
            // TOP-LEFT anchored + pivoted (was centre-anchored). Centring a content rect WIDER
            // than the mask is exactly what sliced a node plate off BOTH frame edges in the
            // 2026-08-04 capture. Flush top-left + ContentPadPx of padding means the rest
            // position always shows the FIRST TRACK ROW WHOLE — plate and name label — which is
            // the WO-896 "no clipped top row" criterion; later rows are reached by scrolling.
            // RebuildTracks sizes the rect in exact pixels.
            _graphContent.anchorMin = _graphContent.anchorMax = new Vector2(0f, 1f);
            _graphContent.pivot = new Vector2(0f, 1f);
            _graphContent.sizeDelta = new Vector2(
                ContentPadPx * 2f + GraphUnitWpx,
                ContentPadPx * 2f + GraphUnitHpx * 0.55f);
            _graphContent.anchoredPosition = Vector2.zero;

            // WO-896: no MORE BELOW cue — sparse graph + scroll is the product.

            var scroll = areaGo.GetComponent<ScrollRect>();
            scroll.content = _graphContent;
            scroll.viewport = vr;
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.scrollSensitivity = 28f;
        }

        // One label treatment for popup buttons: bold, fitted, ink by EMPHASIS only.
        private static TMPro.TextMeshProUGUI StyleActionLabel(Button btn, Color ink)
        {
            var lbl = btn != null ? btn.GetComponentInChildren<TMPro.TextMeshProUGUI>() : null;
            if (lbl == null) return null;
            lbl.color = ink;
            lbl.fontStyle = TMPro.FontStyles.Bold;
            ElarionUiKit.FitSingleLine(lbl);
            return lbl;
        }

        /// <summary>One bar of the confirm button's emphasis outline: a thin edge-anchored quad
        /// with NO sprite, so it can never tint or cover the ornate plate it frames.</summary>
        private static void ConfirmEdgeBar(Transform parent, string name, Vector2 anchorMin,
                                           Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
                                           Color ink)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = go.GetComponent<Image>();
            img.color = ink;
            img.raycastTarget = false;   // the Button under it keeps the whole touch area
        }

        private static void SetButtonAlpha(Button btn, float a)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) { var c = img.color; c.a = a; img.color = c; }
        }

        // ── Black-grid node-canvas sprite (generated once) ────────────────────────

        private static Sprite s_gridSprite;
        private static bool s_gridTried;
        private static Sprite GridSprite()
        {
            if (s_gridSprite != null || s_gridTried) return s_gridSprite;
            s_gridTried = true;
            try
            {
                const int S = 64;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;
                var bg = new Color(0.012f, 0.012f, 0.016f, 1f);  // near-black cell
                var line = new Color(0.15f, 0.16f, 0.21f, 1f);   // faint grid rule
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                        px[y * S + x] = (x == 0 || y == 0) ? line : bg;
                tex.SetPixels(px);
                tex.Apply();
                s_gridSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
                s_gridSprite.name = "SkillTreeGrid";
            }
            catch { s_gridSprite = null; }   // WebGL/headless guard — flat-black fallback used
            return s_gridSprite;
        }

        // Icon cache — Resources.Load is cheap but cached avoids reloading every Render.
        private static readonly Dictionary<string, Sprite> s_iconCache = new Dictionary<string, Sprite>();
        private static Sprite LoadIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (s_iconCache.TryGetValue(path, out var cached)) return cached;
            Sprite sp = Resources.Load<Sprite>(path);
            s_iconCache[path] = sp;   // cache nulls too (atlas not sliced yet) so we don't retry each frame
            return sp;
        }

        // ── Teardown ──────────────────────────────────────────────────────────────

        private void ClearContent()
        {
            if (_graphContent == null) return;
            for (int i = _graphContent.childCount - 1; i >= 0; i--)
            {
                var c = _graphContent.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }
        }

        private void ClearChildren(GameObject host)
        {
            if (host == null) return;
            for (int i = host.transform.childCount - 1; i >= 0; i--)
            {
                var c = host.transform.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }
        }

        private void Close()
        {
            Unbind();
            if (_vm != null) { _vm.Dispose(); _vm = null; }
            _headerLabel = null;
            _wisdomLabel = null;
            _popupRoot = null;
            _popupCard = null;
            _popupConfirmRing = null;
            _popupName = null;
            _popupDesc = null;
            _popupPrompt = null;
            _popupConfirmBtn = null;
            _popupConfirmLabel = null;
            _popupCancelBtn = null;
            _lastLayoutSig = null;
            // Owner picks 2026-08-16: both rigs despawn WITH the panel; the patch
            // RawImages are children of _ui and die in the Destroy below.
            if (_pointerVfx != null) { _pointerVfx.Dispose(); _pointerVfx = null; }
            if (_auraVfx != null) { _auraVfx.Dispose(); _auraVfx = null; }
            _pointerVfxUnavailable = false;   // a later open may retry (fresh session state)
            _auraVfxUnavailable = false;
            _lastPointerSig = null;
            _lastAuraCount = -1;
            if (_ui != null) Destroy(_ui);
            _ui = null;
            _graphContent = null;
            PanelManager.NotifyClosed(_panelHandle);
        }
    }
}
