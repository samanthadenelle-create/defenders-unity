// =============================================================================
// EquipmentPanel — the "Gear Preview" showcase + equip screen (WO Gear-Preview, 2026-06-28).
// -----------------------------------------------------------------------------
// Opened via Yarn "OpenEquip" / PanelRouter. Restyled to the owner's mock: a large
// central 3D HERO preview (live, equipped gear visible) framed by labeled Obsidian
// SLOT plates — Full Armor Set, Shield (Off Hand), Weapon (Main Hand), Amulet, Ring.
// Tapping a slot opens a bottom DRAWER listing the compatible owned items for that
// slot, with Equip / Unequip. We DELINEATE the main-hand weapon (sword / 1H / 2H)
// from the OFF-HAND shield (owner requirement): shields appear only in the off-hand
// list; the main-hand list excludes them. The model's EnforceHandSlots still resolves
// 2H↔off-hand conflicts on equip.
//
// MVVM (WO-434): an IPanelView bound to EquipVM. ALL state/logic (slots, equipped
// items, compatible lists, equip/unequip/swap, target picker, stat readouts) lives in
// EquipVM; this View is a DUMB SKIN that repaints from vm.* on vm.Changed and routes
// taps back as commands. It never reads GearLoadout / inventory / catalog directly.
//
// PRESENTATION: routes through the shared DeNelle.Core.UI kit (ElarionUiKit + ElarionUi
// + RpgUiCatalog). Sprite-FIRST with procedural fallback (WebGL-safe). This screen LEADS
// the Obsidian look — it uses the slot/panel pack art whenever present.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Catalog;   // StructureRoles — the single naming authority
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;
using DeNelle.Village.Crafting;

namespace DeNelle.Village.Hero
{
    public sealed class EquipmentPanel : MonoBehaviour, IPanelView
    {
        private GameObject _ui;

        // WO-434 — the bound ViewModel + the model seams injected at the open-site.
        private EquipVM _vm;
        private InventoryStore _store;
        private readonly List<GearLoadoutEquipTarget> _targetAdapters = new List<GearLoadoutEquipTarget>();

        // Live 3D hero preview (the showcase centerpiece) + the per-target body it previews.
        private HeroPreviewViewer _preview;
        private RawImage _previewImage;
        private Image _previewSilhouette;   // sil_male fallback behind the render (WO-713 §D)
        // WO-1015 E2 — the STRUCTURAL fallback. These two labels are fixed-pixel bands built
        // unconditionally, so the preview box carries the hero's identity in WORDS even when no
        // silhouette art resolves and no render texture exists. A blank box is not reachable.
        private TMPro.TextMeshProUGUI _previewNameLabel;
        private TMPro.TextMeshProUGUI _previewStateLabel;
        private readonly List<GameObject> _targetBodies = new List<GameObject>();
        private int _previewTargetIndex = -1;

        // Legacy demo-def equip (preserved) — still equips basic_sword / leather_armor on the HERO.
        private HeroEquipment _equip;

        // Live regions.
        private Transform _panelTransform;
        private RectTransform _slotListContent; // the scroll column the 5 slot ROWS are parented to
        private bool _slotListScrolls;          // budget outcome — traced, never guessed
        private GameObject _targetBar;

        // Approved 2026 medieval Equipment workspace. These are presentation hosts only; all
        // selection, item, slot, and action state remains in EquipVM.
        private RectTransform _approvedPlayerStrip;
        private RectTransform _approvedEquippedHost;
        private RectTransform _approvedInventoryHost;
        private RectTransform _approvedDetailHost;
        private static readonly bool ApprovedWorkspaceEnabled = true;

        // Change-drawer (tap a slot → browse compatible items for it).
        private GameObject _drawerHost;
        private string _drawerSlotKey;          // the slot the drawer is editing (null = closed)
        private GameObject _listContentArea;    // the drawer's list host (set when the drawer opens)
        private RectTransform _scrollContent;

        private static readonly Color TabSelectedTint = new Color(1.15f, 1.10f, 0.92f, 1f);
        private static readonly Color TabRestTint     = new Color(0.58f, 0.55f, 0.50f, 1f);

        private const float RowHeightPx = 64f;
        private const float RowGapPx    = 4f;

        // =====================================================================
        //  WO-1015 — THE FIXED-PIXEL BAND BUDGET (E3/E4/E5/E7)
        // ---------------------------------------------------------------------
        //  Every one of these is REFERENCE PIXELS, never a fraction of a parent. They are PUBLIC
        //  so EquipmentScreenLayoutRegression [equipment-screen-layout] can replay this arithmetic
        //  without a play session: the oracle reads them by reflection and fails the build if any
        //  band drops under its own floor (the kit touch floor for a tappable band, one whole TMP
        //  line box for a text band) or if the three slot text bands stop summing inside the row.
        //
        //  THE SLOT ROW, banded (E4 — label / value / hint, three bands that cannot share pixels):
        //      pad 8 + label 44 + gap 6 + value 48 + gap 6 + hint 44 + pad 8 = 164 px
        //  164 >= MinTouchPx(112), and each text band >= FontFloor(30) * 1.25 line box (37.5 px).
        // =====================================================================

        /// <summary>Guaranteed gutter between any two bands — no two bands ever touch.</summary>
        public const float BandGapPx = 12f;

        /// <summary>One whole slot row. == SlotRowPadPx*2 + the three text bands + their two gaps.</summary>
        public const float SlotRowPx = 164f;
        /// <summary>Top/bottom padding inside a slot row.</summary>
        public const float SlotRowPadPx = 8f;
        /// <summary>Band 1 of a slot row: the slot NAME ("Weapon (Main Hand)").</summary>
        public const float SlotLabelBandPx = 44f;
        /// <summary>Band 2 of a slot row: the equipped item name, or the WORD "Empty"
        /// (colourblind law — an empty slot says so in words, never by colour).</summary>
        public const float SlotValueBandPx = 48f;
        /// <summary>Band 3 of a slot row: the grant line, or the pointer at the system that fills it.</summary>
        public const float SlotHintBandPx = 44f;
        /// <summary>Gutter between the three text bands inside a slot row.</summary>
        public const float SlotRowBandGapPx = 6f;
        /// <summary>E5 — the slot's ART band. A real FIXED-pixel square, not a fraction: the icons
        /// were "a few dark pixels" because they were 0.36 x 0.40 of a ~64 px plate (~25x23 px).</summary>
        public const float SlotIconPx = 112f;
        /// <summary>Slots on this doll: chest / off-hand / main-hand / amulet / ring.</summary>
        public const int SlotCount = 5;

        /// <summary>Floor under which the preview art band is squeezed rather than seated.</summary>
        public const float PreviewMinPx = 320f;
        /// <summary>E2 fallback band: the hero's NAME, always rendered, fixed px.</summary>
        public const float PreviewNameBandPx = 56f;
        /// <summary>E2 fallback band: the live/portrait STATE word, always rendered, fixed px.</summary>
        public const float PreviewStateBandPx = 44f;
        /// <summary>Inner padding around the preview's art region.</summary>
        public const float PreviewPadPx = 8f;

        /// <summary>The target picker band (hero + companions). >= MinTouchPx.</summary>
        public const float TargetBarPx = 120f;

        /// <summary>Measured well WIDTH at or above which the doll lays out as two columns
        /// (slots | preview). Below it the preview is a top band and the slots scroll under it.</summary>
        public const float TwoColumnMinWidthPx = 900f;
        /// <summary>Left (slot) column width as a fraction of the content row, two-column mode.</summary>
        public const float SlotColumnFrac = 0.52f;
        /// <summary>Horizontal gutter between the two columns.</summary>
        public const float ColumnGapFrac = 0.02f;

        /// <summary>Reclaimed body TOP. FrameCharacter's authored body zone caps at 0.605 (the baked
        /// portrait arch); its header band starts at 0.905 and its medallion at 0.900, so 0.875
        /// clears both ornaments while reclaiming the dead strip between (E3).</summary>
        public const float BodyTopFrac = 0.875f;
        /// <summary>ElarionUiKit's DefaultCloseZone.y — the bottom band the shared Close reserves.</summary>
        public const float CloseBandY0 = 0.050f;
        /// <summary>The well floor clears the Close box by this much.</summary>
        public const float CloseGapY = 0.020f;

        // Modal-arbiter handle (one panel at a time) + PanelRouter registration.
        private PanelHandle _panelHandle;

        // True while the panel UI exists/visible — the IsOpen probe for PanelManager.
        public bool IsOpen => _ui != null;

        // ── Registration (mirror HeroSkillTreePanelMvvm) ──────────────────────────
        private void Awake()
        {
            _panelHandle = PanelManager.Register("Character", Close, () => IsOpen);
            PanelRouter.Register(PanelId.EquipmentPanel, Open);
        }

        public void Open()
        {
            if (_ui != null) return;

            ConstructViewModel();

            // WO screen-conformance: match the standard modal sorting band (31000) the
            // other Obsidian modals use (ElarionUiKit.BuildObsidianModal default), so the
            // panel isn't off-band under/over sibling modals. Was 2500.
            _ui = ElarionUiKit.BuildModalCanvas("EquipmentPanel", sortingOrder: 31000);
            _ui.transform.SetParent(transform, false);
            var canvas = _ui.GetComponent<Canvas>();
            if (canvas != null) canvas.overrideSorting = true;
            ElarionUiKit.Scrim(_ui.transform, () => _vm?.Close());

            // SHARED Obsidian chrome (WO-554): black panel + gold trim + gold header +
            // the ONE standard Close button. Replaces the old backdrop + brown PanelFramed +
            // dark solidFill + per-panel "X". Content lives on chrome.content (0..1 anchors).
            // Header shows the HERO'S NAME, not the generic "CHARACTER" (owner ask 2026-06-29:
            // "upgrade from Character in the ragdoll to characters name"). Resolved from the active
            // hero's class -> canon full name (Grom Ironhand, etc.), matching the inventory card.
            string headerName = "HERO - EQUIPMENT";
            // WO-1015 E3/E7: the panel was 0.12..0.88 x 0.06..0.95 — a narrow column inside which the
            // kit's FIXED-PIXEL canonical Close (360x132) looked enormous relative to the content it
            // closes. Widened to 0.06..0.94 x 0.05..0.96: the Close keeps its canonical size (never
            // resized here — E7 is a PROPORTION defect, and the fix is to give the content the space,
            // not to shrink the one shared CTA).
            var chrome = ElarionUiKit.BuildObsidianPanel(_ui.transform, headerName,
                new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.96f),
                () => _vm?.Close(), headerX0: 0.10f, headerX1: 0.90f,
                frameName: RpgUiCatalog.FrameCharacter, medallionIcon: "armor");
            MedievalUiSkin.ApplyShell(chrome);
            BuildApprovedHeaderActions(chrome);
            var panel = chrome.content;

            // =================================================================
            //  WO-1015 E3 — ONE OWNED GEOMETRY PASS (the ManageScreenPanel.BuildChrome law):
            //  measure the well, sum the fixed bands, hand the remainder to content.
            // -----------------------------------------------------------------
            //  E3's ROOT CAUSE, proven at source, not eyeballed: this screen dropped its content
            //  into chrome.layout.body, and ElarionUiKit.ZonesFor(FrameCharacter) authors that zone
            //  as (0.060, 0.110, 0.940, 0.605) — its top is 0.605 because Stats_Panel bakes a
            //  PORTRAIT ARCH above it. This screen never used the arch, so the whole 0.605..0.905
            //  strip (30% of the panel, ~40% of what the player reads as the frame body) rendered
            //  as empty black and every piece of content was crammed into the bottom half. That is
            //  the defect verbatim.
            //
            //  It also caused E4. Every slot band was a FRACTION of a fraction: at the owner's
            //  2340x1080 device the post-scale canvas is ~978px, the panel ~890px, the body zone
            //  0.495 of that = ~440px, and the Amulet plate 0.145 of THAT = ~64px. Its interior
            //  caption / name / grant bands were 0.17 / 0.17 / 0.18 of 64px = ~11px each — against
            //  a FontFloor(30) line box of ~37px. TMP renders OUTSIDE its rect by default, so three
            //  ~11px bands each painted a ~37px line box and the three overprinted one another.
            //  Fixed pixels, summed against a measured well, make that arithmetically impossible.
            // =================================================================
            float canvasH = ElarionUiKit.PostScaleCanvasHeight(
                chrome.root != null ? chrome.root.transform : _ui.transform);
            float panelFracH = 0.91f, panelFracW = 0.88f;
            if (chrome.root != null)
            {
                var rootRt = (RectTransform)chrome.root.transform;
                panelFracH = Mathf.Max(0.05f, rootRt.anchorMax.y - rootRt.anchorMin.y);
                panelFracW = Mathf.Max(0.05f, rootRt.anchorMax.x - rootRt.anchorMin.x);
            }
            float panelPx  = Mathf.Max(1f, canvasH * panelFracH);
            float panelWpx = Mathf.Max(1f, CanvasWidthPx(canvasH) * panelFracW);

            // The kit reserves the bottom of every framed panel for the ONE shared Close (a fixed
            // CanonCtaHeight box growing up from CloseBandY0). The well floor drops straight onto
            // that band plus a gap — exactly ManageScreenPanel's band-5 arithmetic.
            float closeBandTop = CloseBandY0 + ElarionUiKit.CanonCtaHeight / panelPx;
            float bodyFloor    = closeBandTop + CloseGapY;

            RectTransform bodyRt = chrome.layout != null ? chrome.layout.body : null;
            RectTransform well;
            if (bodyRt != null)
            {
                // RECLAIM the arch strip. BodyTopFrac(0.875) clears FrameCharacter's header band
                // (y0 = 0.905) and its medallion socket (y0 = 0.900), so nothing is painted over the
                // frame's ornament — only over the unused baked arch, which the body zone's own
                // ZoneBacking plate (a stretched child, so it grows with this resize) covers.
                bodyRt.anchorMin = new Vector2(bodyRt.anchorMin.x, bodyFloor);
                bodyRt.anchorMax = new Vector2(bodyRt.anchorMax.x, BodyTopFrac);
                bodyRt.offsetMin = new Vector2(bodyRt.offsetMin.x, 0f);
                bodyRt.offsetMax = new Vector2(bodyRt.offsetMax.x, 0f);
                well = bodyRt;
            }
            else
            {
                // Procedural fallback frame (no frame art — WebGL-safe): mint the same well by hand
                // so the band cursor below still measures from a real body top.
                well = MakeZone(panel != null ? panel.transform : _ui.transform, "Zone_Body_Equip",
                                new Vector2(0.055f, bodyFloor), new Vector2(0.945f, BodyTopFrac));
            }
            _panelTransform = well;

            if (ApprovedWorkspaceEnabled)
            {
                BuildApprovedWorkspace(well);
                Bind(_vm);
                if (chrome.root != null)
                    ElarionUiKit.AttachPanelOpenFx(_ui, chrome.root.GetComponent<RectTransform>());
                if (!PanelManager.NotifyOpened(_panelHandle)) return;
                Debug.Log("[EquipmentPanel] Opened approved three-column Equipment workspace bound to EquipVM.");
                return;
            }

            float wellPx = Mathf.Max(0f, (BodyTopFrac - bodyFloor) * panelPx);
            float wellWpx = Mathf.Max(1f, panelWpx * Mathf.Max(0.05f, well.anchorMax.x - well.anchorMin.x));

            // ── THE SUM. Only ONE band is fixed at the top level: the target picker, and only when
            //    there is more than one assignable member. Everything else is the slot list (which
            //    SCROLLS at fixed row pitch — it can never compress a row) and the preview (which
            //    absorbs the remainder, with a floor below which it is stacked rather than squeezed).
            bool multiTarget = _vm != null && _vm.TargetNames.Count > 1;
            float targetCost = multiTarget ? TargetBarPx + BandGapPx : 0f;
            float contentPx  = wellPx - targetCost;

            // The slot list at its natural (unscrolled) height: five rows and four gutters.
            float slotsNaturalPx = SlotCount * SlotRowPx + (SlotCount - 1) * BandGapPx;

            // TWO-COLUMN when the well is wide enough to seat a readable slot column beside the
            // preview; otherwise the preview takes a fixed TOP band and the slots scroll beneath it.
            // Chosen on MEASURED WIDTH, never on a guessed aspect ratio.
            bool twoColumn = wellWpx >= TwoColumnMinWidthPx;
            float previewBandPx, slotsBandPx;
            if (twoColumn)
            {
                previewBandPx = contentPx;      // full-height right column
                slotsBandPx   = contentPx;      // full-height left column
            }
            else
            {
                // Stacked: the preview keeps its floor, the slot list takes the remainder, and the
                // remainder is never allowed below one whole row + its gutter (it scrolls instead).
                previewBandPx = Mathf.Max(PreviewMinPx, contentPx - slotsNaturalPx - BandGapPx);
                slotsBandPx   = contentPx - previewBandPx - BandGapPx;
                if (slotsBandPx < SlotRowPx)
                {
                    previewBandPx = Mathf.Max(0f, contentPx - SlotRowPx - BandGapPx);
                    slotsBandPx   = contentPx - previewBandPx - BandGapPx;
                }
            }
            _slotListScrolls = slotsBandPx < slotsNaturalPx;

            // §12: the geometry is PROVEN by a capture, not by an eyeball. One line, every band.
            FlowTrace.Step("Equip", string.Format(
                "bands(px): canvas={0:0} panel={1:0}x{2:0} well={3:0}x{4:0} (bodyFloor={5:F3} top={6:F3}, " +
                "reclaimed from the FrameCharacter 0.605 arch cap) || layout={7} target={8:0} " +
                "preview={9:0}[floor {10:0}] slots={11:0}[natural {12:0} = {13}x{14:0}row + {15}x{16:0}gap, " +
                "{17}] gaps={18:0} => content={19:0}",
                canvasH, panelPx, panelWpx, wellPx, wellWpx, bodyFloor, BodyTopFrac,
                twoColumn ? "two-column" : "stacked", multiTarget ? TargetBarPx : 0f,
                previewBandPx, PreviewMinPx, slotsBandPx, slotsNaturalPx,
                SlotCount, SlotRowPx, SlotCount - 1, BandGapPx,
                _slotListScrolls ? "SCROLLS" : "fits",
                targetCost + (twoColumn ? 0f : BandGapPx), contentPx));

            if (previewBandPx < PreviewMinPx)
                FlowTrace.Warn("Equip", string.Format(
                    "preview band is {0:0}px, under the {1:0}px floor - the paperdoll art is squeezed. " +
                    "The name + state fallback bands still render (they are fixed px), so the box " +
                    "cannot go blank; only the portrait art loses room.", previewBandPx, PreviewMinPx));

            // ── LAY THE BANDS. One cursor, top-down, gutter after every band; or two columns.
            float cursor = 0f;
            if (multiTarget)
                BuildTargetBar(Band(well, "Band_TargetBar", ref cursor, TargetBarPx));

            RectTransform previewBand, slotsBand;
            if (twoColumn)
            {
                var row = Band(well, "Band_Content", ref cursor, contentPx);
                slotsBand   = Column(row, "Col_Slots",   0f, SlotColumnFrac - ColumnGapFrac * 0.5f);
                previewBand = Column(row, "Col_Preview", SlotColumnFrac + ColumnGapFrac * 0.5f, 1f);
            }
            else
            {
                previewBand = Band(well, "Band_Preview", ref cursor, previewBandPx);
                slotsBand   = Band(well, "Band_Slots",   ref cursor, slotsBandPx);
            }

            // Hero preview (the showcase centerpiece) — with its structural fallback (E2).
            BuildPreviewWidget(previewBand);

            // The five slot ROWS. A scroll zone at FIXED row pitch: a row can never be compressed
            // to fit, so the three text bands inside it can never be forced to overprint (E4).
            var slotScroll = ElarionUiKit.MakeScrollZone(slotsBand, spacing: BandGapPx, padding: 4);
            _slotListContent = slotScroll != null ? slotScroll.content : null;
            if (_slotListContent == null)
                FlowTrace.Fail("Equip", "MakeScrollZone returned no content - the slot list has no host; " +
                                        "no slot row can be built and the screen would read as gear-less.");

            Bind(_vm);

            // WO-713 — the ONE shared open ease (kit PanelOpenCloseFx, WO-714 P8): scale
            // target = the chrome panel rect (never the overlay canvas root).
            if (chrome.root != null)
                ElarionUiKit.AttachPanelOpenFx(_ui, chrome.root.GetComponent<RectTransform>());

            // Modal arbiter: announce the open (closes any other panel; one-modal-at-a-time).
            // Rejected during battle — NotifyOpened already invoked Close in that case.
            if (!PanelManager.NotifyOpened(_panelHandle))
                return;

            Debug.Log("[EquipmentPanel] Opened - Gear Preview showcase bound to EquipVM (MVVM).");
        }

        // =====================================================================
        //  WO-1015 — geometry primitives (the ManageScreenPanel.Band law, verbatim)
        // =====================================================================

        /// <summary>
        /// Seat the next band under the previous one and advance the cursor by its height PLUS the
        /// guaranteed gutter. Top-anchored, top pivot, explicit <c>sizeDelta.y</c> — the height is
        /// REFERENCE PIXELS, never a fraction of the parent. Fraction bands are the whole of E3/E4:
        /// a 37px TMP line box inside an 11px fraction band paints straight over its neighbours.
        /// </summary>
        private static RectTransform Band(RectTransform parent, string name, ref float cursorPx,
                                          float heightPx, float x0 = 0.01f, float x1 = 0.99f,
                                          float gapPx = -1f)
        {
            if (gapPx < 0f) gapPx = BandGapPx;
            float h = Mathf.Max(0f, heightPx);
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x0, 1f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, h);
            rt.anchoredPosition = new Vector2(0f, -cursorPx);
            cursorPx += h + gapPx;
            return rt;
        }

        /// <summary>A full-height COLUMN of a band. Horizontal fractions are legitimate here: the
        /// column's WIDTH is not what any text band is measured against — every text band inside it
        /// is pinned in fixed pixels off the row's own top edge.</summary>
        private static RectTransform Column(RectTransform parent, string name, float x0, float x1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x0, 0f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>A fixed-pixel band pinned to the BOTTOM of its host, cursor running upward.
        /// Used inside the preview so the name/state fallback bands can never be squeezed out.</summary>
        private static RectTransform BandFromBottom(RectTransform parent, string name, ref float cursorPx,
                                                    float heightPx, float x0 = 0.02f, float x1 = 0.98f)
        {
            float h = Mathf.Max(0f, heightPx);
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x0, 0f);
            rt.anchorMax = new Vector2(x1, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, h);
            rt.anchoredPosition = new Vector2(0f, cursorPx);
            cursorPx += h + SlotRowBandGapPx;
            return rt;
        }

        private static RectTransform MakeZone(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Post-scale canvas WIDTH in the same reference-px space as
        /// <see cref="ElarionUiKit.PostScaleCanvasHeight"/> — one scaleFactor drives both axes.
        /// DERIVED, never read off a live rect on the creation frame (that returns raw screen px).</summary>
        private static float CanvasWidthPx(float canvasH)
        {
            float sw = ElarionUiKit.SurfaceWidth, sh = ElarionUiKit.SurfaceHeight;
            if (sw < 1f || sh < 1f) return canvasH * (1080f / 1920f);   // headless: kit portrait reference
            return canvasH * (sw / sh);
        }

        // ── Construct the model seams + the pure ViewModel at the open-site ──────────
        private void ConstructViewModel()
        {
            DisposeViewModel();

            var targets = new List<IEquipTarget>();
            _targetAdapters.Clear();
            _targetBodies.Clear();

            _equip = FindAnyObjectByType<HeroEquipment>();
            var hero = GameObject.FindWithTag("Player");
            if (_equip == null && hero != null) _equip = hero.AddComponent<HeroEquipment>();
            if (hero == null)
            {
                var loco = FindAnyObjectByType<HeroLocomotion>();
                if (loco != null) hero = loco.gameObject;
            }
            if (hero != null)
            {
                var hl = hero.GetComponent<GearLoadout>();
                if (hl == null) hl = hero.AddComponent<GearLoadout>();
                string hjob = ResolveHeroJob(hl);
                var adapter = new GearLoadoutEquipTarget(hl, HeroName(hjob), hjob);
                _targetAdapters.Add(adapter);
                targets.Add(adapter);
                _targetBodies.Add(ResolveBody(hero));
            }

            foreach (var comp in FindObjectsByType<StoryCompanion>())
            {
                if (comp == null) continue;
                var cl = comp.GetComponent<GearLoadout>();
                if (cl == null) continue;
                string cjob = comp.Hero.ToString().ToLowerInvariant();
                var adapter = new GearLoadoutEquipTarget(cl, comp.DisplayName, cjob);
                _targetAdapters.Add(adapter);
                targets.Add(adapter);
                _targetBodies.Add(ResolveBody(comp.gameObject));
            }

            // DI-in-Open (strict-MVVM): EquipVM.CreateDefault resolves the owned-store handle itself
            // (VillageInventory.Instance, WO-578 UNIONed with the party targets) so this View no longer
            // names VillageInventory.Instance. Targets stay View-resolved (they wrap live GameObjects the
            // preview also needs). The factory returns the store so we keep the handle to dispose.
            _vm = EquipVM.CreateDefault(targets, Close, out _store);
        }

        private static GameObject ResolveBody(GameObject root)
        {
            if (root == null) return null;
            var body = root.transform.Find("HeroBody");
            return body != null ? body.gameObject : root;
        }

        private string ActiveWeaponId()
        {
            if (_vm == null) return null;
            int idx = _vm.ActiveTargetIndex;
            if (idx < 0 || idx >= _targetAdapters.Count) return null;
            var w = _targetAdapters[idx].EquippedWeapon;
            return w != null ? w.id : null;
        }

        private GameObject ActiveBody()
        {
            if (_vm == null) return null;
            int idx = _vm.ActiveTargetIndex;
            return (idx >= 0 && idx < _targetBodies.Count) ? _targetBodies[idx] : null;
        }

        // WO-567: off-hand (shield) id + armor tier for the active target, so the Gear Preview
        // mirrors the FULL equipped look (weapon + shield + armor tint), not just the weapon.
        private string ActiveOffHandId()
        {
            if (_vm == null) return null;
            int idx = _vm.ActiveTargetIndex;
            if (idx < 0 || idx >= _targetAdapters.Count) return null;
            var o = _targetAdapters[idx].EquippedOffHand;
            return o != null ? o.id : null;
        }

        private int ActiveArmorTier()
        {
            if (_vm == null) return 0;
            int idx = _vm.ActiveTargetIndex;
            if (idx < 0 || idx >= _targetAdapters.Count) return 0;
            return GearLoadout.ArmorVisualTier(_targetAdapters[idx].EquippedArmor);
        }

        private static string ResolveHeroJob(GearLoadout loadout)
        {
            var ha = loadout != null ? loadout.GetComponent<HeroAbilities>() : null;
            string j = ha != null ? ha.HeroClass : null;
            return string.IsNullOrEmpty(j) ? AbilityCatalog.DefaultClass : j;
        }

        // ── IPanelView ──────────────────────────────────────────────────────────────
        public void Bind(IPanelViewModel vm)
        {
            Unbind();
            _vm = vm as EquipVM;
            if (_vm == null) return;
            _vm.Changed += Render;
            Render();
        }

        public void Unbind()
        {
            if (_vm != null) _vm.Changed -= Render;
        }

        private void Render()
        {
            if (_vm == null) return;
            if (_approvedEquippedHost != null)
            {
                RenderApprovedWorkspace();
                return;
            }
            HighlightTargets();
            RebuildSlots();
            RenderPreview();
            if (_drawerSlotKey != null) RebuildList();   // keep the open drawer fresh
        }

        // =====================================================================
        // Approved Hero -> Equipment composition: player strip, equipped slots,
        // category-filtered inventory, and one selected-item detail/action pane.
        // =====================================================================
        private void BuildApprovedWorkspace(RectTransform well)
        {
            if (well == null) return;
            _approvedPlayerStrip = ApprovedPanel(well, "PlayerStrip",
                new Vector2(0.01f, 0.79f), new Vector2(0.99f, 0.99f));
            _approvedEquippedHost = ApprovedPanel(well, "EquippedColumn",
                new Vector2(0.01f, 0.01f), new Vector2(0.275f, 0.77f));
            _approvedInventoryHost = ApprovedPanel(well, "InventoryColumn",
                new Vector2(0.29f, 0.01f), new Vector2(0.675f, 0.77f));
            _approvedDetailHost = ApprovedPanel(well, "DetailColumn",
                new Vector2(0.69f, 0.01f), new Vector2(0.99f, 0.77f));
        }

        private static RectTransform ApprovedPanel(Transform parent, string name,
                                                    Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            // content-panel.png is a horizontal source sheet with a large transparent tail.
            // Stretching it into these tall columns made the visible frame end halfway down,
            // leaving equipment/detail rows apparently floating outside their panel. These
            // columns need scalable geometry: one continuous black-iron body and four restrained
            // antique-gold structural rules.
            image.sprite = null;
            image.color = new Color(0.018f, 0.018f, 0.021f, 0.985f);
            image.raycastTarget = false;
            void Edge(string edgeName, Vector2 min, Vector2 max)
            {
                var edge = ElarionUiKit.AddImage(go.transform, edgeName, min, max,
                    new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.78f), false);
                edge.GetComponent<Image>().raycastTarget = false;
            }
            Edge("GoldTop",    new Vector2(.012f, .992f), new Vector2(.988f, 1f));
            Edge("GoldBottom", new Vector2(.012f, 0f),    new Vector2(.988f, .008f));
            Edge("GoldLeft",   new Vector2(0f, .008f),    new Vector2(.008f, .992f));
            Edge("GoldRight",  new Vector2(.992f, .008f), Vector2.one);
            return rt;
        }

        private void RenderApprovedWorkspace()
        {
            ClearApprovedChildren(_approvedPlayerStrip);
            ClearApprovedChildren(_approvedEquippedHost);
            ClearApprovedChildren(_approvedInventoryHost);
            ClearApprovedChildren(_approvedDetailHost);
            RenderApprovedPlayerStrip();
            RenderApprovedEquipped();
            RenderApprovedInventory();
            RenderApprovedDetail();
        }

        private static void ClearApprovedChildren(RectTransform host)
        {
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var child = host.GetChild(i);
                if (child == null) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        private void RenderApprovedPlayerStrip()
        {
            if (_approvedPlayerStrip == null || _vm == null) return;
            var portraitFrame = ElarionUiKit.AddImage(_approvedPlayerStrip, "PortraitMedallion",
                new Vector2(0.012f, 0.02f), new Vector2(0.145f, 0.98f), Color.white, false);
            var frameImage = portraitFrame.GetComponent<Image>();
            var frame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/circular-bezel-four-point");
            if (frameImage != null && frame != null)
            {
                frameImage.sprite = frame; frameImage.type = Image.Type.Simple;
                frameImage.preserveAspect = true;
            }
            var portrait = new GameObject("Portrait", typeof(RawImage));
            portrait.transform.SetParent(portraitFrame.transform, false);
            var portraitRt = (RectTransform)portrait.transform;
            portraitRt.anchorMin = new Vector2(0.13f, 0.13f);
            portraitRt.anchorMax = new Vector2(0.87f, 0.87f);
            portraitRt.offsetMin = Vector2.zero; portraitRt.offsetMax = Vector2.zero;
            var portraitImage = portrait.GetComponent<RawImage>();
            portraitImage.texture = Resources.Load<Texture2D>(DeNelle.Core.HeroPortraitPaths.ResourceKey(
                PortraitSlug(_vm.Portrait.IconName)));
            portraitImage.color = portraitImage.texture != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            portraitImage.raycastTarget = false;

            var identity = ElarionUiKit.Label(_approvedPlayerStrip, _vm.CharacterLabel.ToUpperInvariant(),
                0.54f, 0.90f, ElarionUi.Gold, 34, TMPro.TextAlignmentOptions.Left,
                0.155f, 0.37f, bold: true);
            ElarionUiKit.FitSingleLine(identity, 22f, 34f);

            for (int i = 0; i < Mathf.Min(2, _vm.Stats.Count); i++)
            {
                var stat = _vm.Stats[i];
                float y1 = i == 0 ? 0.72f : 0.40f;
                float y0 = y1 - 0.22f;
                BuildApprovedStateBar(_approvedPlayerStrip, "Player" + i,
                    new Vector2(0.37f, y0), new Vector2(0.72f, y1), stat.Bar.Fill01,
                    stat.Bar.Label, i == 0
                        ? new Color(.48f, .075f, .08f, 1f)
                        : new Color(.07f, .20f, .48f, 1f));
            }

            // Equipment intentionally shows one economy only. The runtime chip remains data-bound.
            var gold = ElarionUiKit.CurrencyChip(_approvedPlayerStrip, ElarionUiKit.CurrencyKind.Gold,
                new Vector2(0.80f, 0.18f), new Vector2(0.98f, 0.82f), tag: "GOLD");
            var economy = FindAnyObjectByType<EconomyService>();
            if (gold != null)
            {
                gold.SetAmount(economy != null ? economy.Coins : 0, animate: false);
                if (gold.plate != null) gold.plate.color = new Color(.025f, .024f, .023f, .98f);
                var plateFrame = gold.root != null ? gold.root.transform.Find("PlateFrame") : null;
                var plateFrameImage = plateFrame != null ? plateFrame.GetComponent<Image>() : null;
                if (plateFrameImage != null)
                    plateFrameImage.color = new Color(ElarionUi.Gold.r, ElarionUi.Gold.g,
                        ElarionUi.Gold.b, .90f);
            }
        }

        private static void BuildApprovedStateBar(Transform parent, string name,
                                                   Vector2 min, Vector2 max, float fill01,
                                                   string value, Color fillColor)
        {
            var track = ElarionUiKit.AddImage(parent, name + "Track", min, max,
                new Color(.018f, .018f, .021f, .98f), false);
            var trackRt = (RectTransform)track.transform;
            var fill = ElarionUiKit.AddImage(track.transform, name + "Fill",
                new Vector2(.012f, .14f), new Vector2(Mathf.Lerp(.012f, .988f,
                    Mathf.Clamp01(fill01)), .86f), fillColor, false);
            fill.GetComponent<Image>().raycastTarget = false;
            void Rule(string ruleName, Vector2 a, Vector2 b)
            {
                ElarionUiKit.AddImage(track.transform, ruleName, a, b,
                    new Color(ElarionUi.Gold.r, ElarionUi.Gold.g,
                        ElarionUi.Gold.b, .82f), false);
            }
            Rule("Top", new Vector2(0f, .93f), Vector2.one);
            Rule("Bottom", Vector2.zero, new Vector2(1f, .07f));
            Rule("Left", Vector2.zero, new Vector2(.008f, 1f));
            Rule("Right", new Vector2(.992f, 0f), Vector2.one);
            var label = ElarionUiKit.Label(track.transform, value ?? "", .05f, .95f,
                ElarionUi.Parchment, 20, TMPro.TextAlignmentOptions.Center,
                .05f, .95f, bold: true);
            ElarionUiKit.FitSingleLine(label, 15f, 20f);
        }

        private void RenderApprovedEquipped()
        {
            if (_approvedEquippedHost == null || _vm == null) return;
            ApprovedHeading(_approvedEquippedHost, "EQUIPPED");
            string[] order = { EquipVM.SlotMainhand, EquipVM.SlotOffHand, EquipVM.SlotChest,
                               EquipVM.SlotAmulet, EquipVM.SlotRing };
            for (int i = 0; i < order.Length; i++)
            {
                string key = order[i];
                float top = 0.88f - i * 0.166f;
                float bottom = top - 0.145f;
                var slot = FindSlot(key);
                bool filled = slot.HasValue && slot.Value.Content.HasValue;
                string value = filled ? slot.Value.Content.Value.Name : "Empty";
                var row = ApprovedPanel(_approvedEquippedHost, "Slot_" + key,
                    new Vector2(0.035f, bottom), new Vector2(0.965f, top));
                var button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row.GetComponent<Image>();
                string capturedKey = key;
                button.onClick.AddListener(() => SelectApprovedSlot(capturedKey));
                ElarionUiKit.StyleButtonColors(button);
                var medallion = ElarionUiKit.AddImage(row, "SlotMedallion",
                    new Vector2(.025f, .04f), new Vector2(.245f, .96f), Color.white, false);
                var medallionImage = medallion.GetComponent<Image>();
                medallionImage.sprite = Resources.Load<Sprite>(
                    "UI/ElarionMedieval/frames/circular-bezel-four-point");
                medallionImage.type = Image.Type.Simple;
                medallionImage.preserveAspect = true;
                if (filled)
                {
                    var art = ElarionUiKit.AddImage(medallion.transform, "ItemArt",
                        new Vector2(.22f, .22f), new Vector2(.78f, .78f), Color.white, false)
                        .GetComponent<Image>();
                    art.sprite = ResolveApprovedItemArt(key, slot.Value.Content.Value);
                    art.preserveAspect = true;
                    if (art.sprite == null) art.color = Color.clear;
                }
                var caption = ElarionUiKit.Label(row, ShortSlotCaption(key),
                    0.52f, 0.90f, ElarionUi.Gold, 21, TMPro.TextAlignmentOptions.Left,
                    0.28f, 0.94f, bold: true);
                ElarionUiKit.FitSingleLine(caption, 16f, 21f);
                var equippedName = ElarionUiKit.Label(row, value,
                    0.10f, 0.50f, filled ? ElarionUi.Parchment : ElarionUi.ParchmentDim,
                    19, TMPro.TextAlignmentOptions.Left, 0.28f, 0.94f);
                ElarionUiKit.FitSingleLine(equippedName, 15f, 19f);
            }
        }

        private static string ShortSlotCaption(string key)
        {
            switch (key)
            {
                case EquipVM.SlotMainhand: return "MAIN HAND";
                case EquipVM.SlotOffHand: return "OFF HAND";
                case EquipVM.SlotChest: return "ARMOR";
                case EquipVM.SlotAmulet: return "AMULET";
                case EquipVM.SlotRing: return "RING";
                default: return SlotCaption(key).ToUpperInvariant();
            }
        }

        private void SelectApprovedSlot(string key)
        {
            SelectSlotKey(key);
        }

        private void RenderApprovedInventory()
        {
            if (_approvedInventoryHost == null || _vm == null) return;
            ApprovedHeading(_approvedInventoryHost,
                HudStrings.HeroFaceLabel(HudStrings.KeyHeroBag, "chrome"));
            var selected = _vm.SelectedItem;
            int count = Mathf.Min(5, _vm.CompatibleItems.Count);
            if (count == 0)
            {
                ElarionUiKit.Label(_approvedInventoryHost, "No compatible items owned.",
                    0.40f, 0.62f, ElarionUi.ParchmentDim, 26,
                    TMPro.TextAlignmentOptions.Center, 0.08f, 0.92f);
                return;
            }
            const float gap = 0.025f;
            const float width = 0.285f;
            for (int i = 0; i < count; i++)
            {
                var item = _vm.CompatibleItems[i];
                float x0 = 0.03f + i * (width + gap);
                float x1 = x0 + width;
                bool isSelected = selected.HasValue && selected.Value.Id == item.Id;
                string itemId = item.Id;
                var card = ApprovedPanel(_approvedInventoryHost, "ItemCard_" + itemId,
                    new Vector2(x0, 0.40f), new Vector2(x1, 0.84f));
                var cardButton = card.gameObject.AddComponent<Button>();
                cardButton.targetGraphic = card.GetComponent<Image>();
                cardButton.onClick.AddListener(() => _vm.SelectItem(itemId));
                ElarionUiKit.StyleButtonColors(cardButton);
                var artFrame = ElarionUiKit.AddImage(card, "ItemMedallion",
                    new Vector2(.16f, .30f), new Vector2(.84f, .96f), Color.white, false);
                var artFrameImage = artFrame.GetComponent<Image>();
                artFrameImage.sprite = Resources.Load<Sprite>(
                    "UI/ElarionMedieval/frames/circular-bezel-four-point");
                artFrameImage.type = Image.Type.Simple;
                artFrameImage.preserveAspect = true;
                var art = ElarionUiKit.AddImage(artFrame.transform, "ItemArt",
                    new Vector2(.22f, .22f), new Vector2(.78f, .78f), Color.white, false)
                    .GetComponent<Image>();
                art.sprite = ResolveApprovedItemArt(_vm.SelectedSlotKey, item);
                art.preserveAspect = true;
                if (art.sprite == null) art.color = Color.clear;
                var itemLabel = ElarionUiKit.Label(card, item.Name.ToUpperInvariant(),
                    .12f, .30f, ElarionUi.Parchment, 21,
                    TMPro.TextAlignmentOptions.Center, .06f, .94f, bold: true);
                itemLabel.textWrappingMode = TMPro.TextWrappingModes.Normal;
                ElarionUiKit.FitBlock(itemLabel, 15f, 21f);
                ElarionUiKit.Label(card,
                    item.Equipped ? "EQUIPPED" : isSelected ? "SELECTED" : "",
                    .02f, .12f, isSelected ? ElarionUi.Gilt : ElarionUi.ParchmentDim,
                    16, TMPro.TextAlignmentOptions.Center, .06f, .94f, bold: true);
            }
        }

        private void RenderApprovedDetail()
        {
            if (_approvedDetailHost == null || _vm == null) return;
            ApprovedHeading(_approvedDetailHost, "ITEM DETAILS");
            var selected = _vm.SelectedItem;
            if (!selected.HasValue)
            {
                ElarionUiKit.Label(_approvedDetailHost, "Select an item to compare.",
                    0.44f, 0.62f, ElarionUi.ParchmentDim, 25,
                    TMPro.TextAlignmentOptions.Center, 0.08f, 0.92f);
                return;
            }
            var item = selected.Value;
            var iconFrame = ElarionUiKit.AddImage(_approvedDetailHost, "SelectedMedallion",
                new Vector2(0.31f, 0.66f), new Vector2(0.69f, 0.90f), Color.white, false);
            var image = iconFrame.GetComponent<Image>();
            var bezel = Resources.Load<Sprite>("UI/ElarionMedieval/frames/circular-bezel-four-point");
            if (image != null && bezel != null) { image.sprite = bezel; image.type = Image.Type.Simple; image.preserveAspect = true; }
            var icon = ElarionUiKit.AddImage(iconFrame.transform, "ItemIcon",
                new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.78f), Color.white, false).GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = ResolveApprovedItemArt(_vm.SelectedSlotKey, item);
                icon.preserveAspect = true;
                if (icon.sprite == null) icon.color = new Color(0f, 0f, 0f, 0f);
            }
            var name = ElarionUiKit.Label(_approvedDetailHost, item.Name.ToUpperInvariant(),
                0.55f, 0.64f, ElarionUi.Gold, 26, TMPro.TextAlignmentOptions.Center,
                0.05f, 0.95f, bold: true);
            ElarionUiKit.FitSingleLine(name, 18f, 26f);
            ElarionUiKit.Label(_approvedDetailHost, SlotCaption(_vm.SelectedSlotKey),
                0.47f, 0.54f, ElarionUi.ParchmentDim, 21,
                TMPro.TextAlignmentOptions.Center, 0.08f, 0.92f);
            if (item.Equipped)
            {
                ElarionUiKit.Label(_approvedDetailHost, "EQUIPPED",
                    0.34f, 0.43f, ElarionUi.Affordable, 23,
                    TMPro.TextAlignmentOptions.Center, 0.08f, 0.92f, bold: true);
            }
            else
            {
                ElarionUiKit.Label(_approvedDetailHost, "COMPARE TO EQUIPPED",
                    0.38f, 0.46f, ElarionUi.Gold, 19,
                    TMPro.TextAlignmentOptions.Center, 0.08f, 0.92f, bold: true);
                var lines = _vm.SelectedComparison;
                for (int i = 0; i < Mathf.Min(3, lines.Count); i++)
                {
                    var line = lines[i];
                    float top = 0.37f - i * 0.075f;
                    float bottom = top - 0.065f;
                    string direction = line.Delta > 0.01f ? "UP +" + Mathf.RoundToInt(line.Delta)
                        : line.Delta < -0.01f ? "DOWN " + Mathf.RoundToInt(line.Delta) : "SAME";
                    Color deltaColor = line.Delta > 0.01f ? ElarionUi.Affordable
                        : line.Delta < -0.01f ? ElarionUi.Danger : ElarionUi.ParchmentDim;
                    ElarionUiKit.Label(_approvedDetailHost, line.Label,
                        bottom, top, ElarionUi.Parchment, 18,
                        TMPro.TextAlignmentOptions.Left, 0.08f, 0.43f);
                    ElarionUiKit.Label(_approvedDetailHost, line.Candidate,
                        bottom, top, ElarionUi.Parchment, 18,
                        TMPro.TextAlignmentOptions.Right, 0.43f, 0.66f, bold: true);
                    ElarionUiKit.Label(_approvedDetailHost, direction,
                        bottom, top, deltaColor, 18,
                        TMPro.TextAlignmentOptions.Right, 0.66f, 0.92f, bold: true);
                }
            }
            var action = ElarionUiKit.ButtonPack(_approvedDetailHost,
                item.Equipped ? new LocalizedText("village.hero.equipment.remove_button").Resolve() : new LocalizedText("village.hero.equipment.equip_button").Resolve(), ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.12f, 0.07f), new Vector2(0.88f, 0.21f),
                () => _vm.ActivateSelected(), RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(action, primary: true);
        }

        private Sprite ResolveApprovedItemArt(string slotKey, ItemVM item)
        {
            var sprite = ResolveSlotItemArt(slotKey, item);
            if (sprite != null)
            {
                float aspect = sprite.rect.height > 0f
                    ? sprite.rect.width / sprite.rect.height : 1f;
                if (aspect >= 0.45f && aspect <= 2.2f) return sprite;
            }
            return RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, SlotIconName(slotKey));
        }

        private static void ApprovedHeading(Transform host, string text)
        {
            var heading = ElarionUiKit.Label(host, text, 0.89f, 0.985f,
                ElarionUi.Gold, 28, TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f, bold: true);
            ElarionUiKit.FitSingleLine(heading, 20f, 28f);
        }

        private static string PortraitSlug(string heroClass)
        {
            switch ((heroClass ?? "").ToLowerInvariant())
            {
                case "ranger": return "Sylas";
                case "mage": return "Thrain";
                case "cleric": return "Elara";
                default: return "Grom";
            }
        }

        private void BuildApprovedHeaderActions(ElarionUiKit.PanelChrome chrome)
        {
            if (chrome == null || chrome.root == null) return;
            var back = ElarionUiKit.ButtonPack(chrome.root.transform, "BACK",
                ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.025f, 0.885f), new Vector2(0.15f, 0.975f),
                Close, RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(back, primary: false);
            var backLabel = back != null ? back.GetComponentInChildren<TMPro.TMP_Text>() : null;
            if (backLabel != null) ElarionUiKit.FitSingleLine(backLabel, 18f, 26f);

            var talents = ElarionUiKit.ButtonPack(chrome.root.transform,
                HudStrings.HeroFaceLabel(HudStrings.KeyHeroSkills, "button"),
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.82f, 0.885f), new Vector2(0.975f, 0.975f),
                OpenTalentsFromEquipment, RpgUiCatalog.ButtonFrame);
            MedievalUiSkin.ApplyButton(talents, primary: true);
            var talentLabel = talents != null ? talents.GetComponentInChildren<TMPro.TMP_Text>() : null;
            if (talentLabel != null) ElarionUiKit.FitSingleLine(talentLabel, 18f, 26f);
        }

        private void OpenTalentsFromEquipment()
        {
            Close();
            PanelRouter.Open(PanelId.HeroSkillTree);
        }

        // WO-434 Phase D — drive the live preview from vm state.
        private void RenderPreview()
        {
            if (_previewImage == null || _vm == null) return;
            int idx = _vm.ActiveTargetIndex;
            if (_preview == null || idx != _previewTargetIndex)
            {
                BeginOrRetargetPreview();
                _previewTargetIndex = idx;
            }
            else
            {
                RefreshPreviewWeapon();
            }
        }

        // ── The five slot ROWS (WO-1015 E4/E5) ───────────────────────────────────────
        // Was: five plates hand-anchored at body FRACTIONS, each slicing its own interior into
        // four more fractions. On the owner's device the short Amulet/Ring plates resolved to
        // ~64 px and their interior bands to ~11 px each — under a third of one FontFloor line
        // box — so caption, name and grant all painted the same pixels. That is E4 verbatim.
        //
        // Now: one fixed-pitch scroll column of SlotRowPx rows. The row height is pixels the row
        // OWNS; the three text bands are pinned in fixed pixels off the row's top edge and sum to
        // exactly the row height. Overlap is not expressible in this geometry.
        private void RebuildSlots()
        {
            if (_slotListContent == null) return;
            for (int i = _slotListContent.childCount - 1; i >= 0; i--)
            {
                var c = _slotListContent.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }

            int built = 0;
            // Reading order top-down: what you wear, then what you hold, then trinkets.
            built += BuildGearSlotRow(EquipVM.SlotChest)    ? 1 : 0;
            built += BuildGearSlotRow(EquipVM.SlotMainhand) ? 1 : 0;
            built += BuildGearSlotRow(EquipVM.SlotOffHand)  ? 1 : 0;
            built += BuildGearSlotRow(EquipVM.SlotAmulet)   ? 1 : 0;
            built += BuildGearSlotRow(EquipVM.SlotRing)     ? 1 : 0;

            if (built != SlotCount)
                FlowTrace.Fail("Equip", string.Format(
                    "slot rows built {0}, expected {1} - a slot is MISSING from the doll, not merely " +
                    "mis-drawn. SlotCount and the band budget disagree with what the VM exposes.",
                    built, SlotCount));
            else
                FlowTrace.Step("Equip", string.Format(
                    "slot rows: {0} x {1:0}px (bands {2:0}/{3:0}/{4:0} + pads {5:0} + gaps {6:0} = {7:0}), " +
                    "icon art band {8:0}px, list {9}.",
                    built, SlotRowPx, SlotLabelBandPx, SlotValueBandPx, SlotHintBandPx,
                    SlotRowPadPx * 2f, SlotRowBandGapPx * 2f,
                    SlotRowPadPx * 2f + SlotLabelBandPx + SlotValueBandPx + SlotHintBandPx + SlotRowBandGapPx * 2f,
                    SlotIconPx, _slotListScrolls ? "SCROLLS" : "fits"));
        }

        /// <summary>
        /// One slot ROW: a fixed-height tappable plate carrying an ART band and THREE stacked,
        /// non-overlapping text bands — label / value / hint (the WO-911 queue-row banding).
        /// FILLED shows the item name + its one-line grant (WO-683 BESTOWS read). EMPTY says the
        /// WORD "Empty" and points at the system that fills the slot — never a colour-only state.
        /// </summary>
        private bool BuildGearSlotRow(string slotKey)
        {
            if (_slotListContent == null) return false;
            var slot = FindSlot(slotKey);

            // ── The row host: BOTH the LayoutElement AND sizeDelta. The scroll column has
            //    childControlHeight off, so a row that sets only one of them collapses to zero.
            var go = new GameObject("Slot_" + slotKey, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_slotListContent, false);
            var rt = (RectTransform)go.transform;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = SlotRowPx;
            le.minHeight = SlotRowPx;          // >= MinTouchPx(112) — the whole row is the tap target
            le.flexibleWidth = 1f;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, SlotRowPx);

            var img = go.GetComponent<Image>();
            bool selected = slotKey == _drawerSlotKey;
            // Real Obsidian equipment-slot plate (UI_BLINK_TEMPLATE_CANON §4) — sprite-FIRST,
            // sliced, white; the procedural Cell tint is the WebGL-safe null fallback.
            var plate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotArmor);
            if (plate != null)
            {
                img.sprite = plate; img.type = Image.Type.Sliced;
                img.color = selected ? TabSelectedTint : Color.white;
            }
            else
            {
                img.color = selected ? ElarionUiKit.CellSelected : ElarionUiKit.Cell;
                ElarionUiKit.ApplyRounded(img);
            }

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            ElarionUiKit.StyleButtonColors(btn);
            string key = slotKey;
            btn.onClick.AddListener(() => OnSlotTapped(key));

            bool filled = slot.HasValue && slot.Value.Content.HasValue;
            var item = filled ? slot.Value.Content.Value : default;

            // ── E5: the ART BAND. A FIXED SlotIconPx square pinned to the row's left edge, vertically
            //    centred. Fixed px is the whole fix — the old icon was 0.36 x 0.36 of a plate that had
            //    itself collapsed, which is why it read as "a few dark pixels".
            var iconGo = new GameObject("IconBand", typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(SlotIconPx, SlotIconPx);
            irt.anchoredPosition = new Vector2(SlotRowPadPx + 6f, 0f);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.raycastTarget = false;

            // THE SHARED resolver — GearIconCatalog, the same path the queue/cards and InventoryGrid
            // use. No second resolver is introduced here, so an item cannot look like one thing on
            // the doll and another in the grid.
            var iconSprite = filled ? ResolveSlotItemArt(slotKey, item) : null;
            if (iconSprite == null)
                iconSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, SlotIconName(slotKey));
            if (iconSprite != null)
            {
                iconImg.sprite = iconSprite;
                iconImg.preserveAspect = true;
                iconImg.color = filled ? RarityTint(item.Rarity) : new Color(1f, 1f, 1f, 0.35f);
            }
            else
            {
                // No art at all: the band still carries a legible glyph rather than a dark square.
                iconImg.color = new Color(0f, 0f, 0f, 0f);
                var q = ElarionUiKit.Label(iconGo.transform, "?", 0f, 1f,
                    filled ? ElarionUi.Parchment : ElarionUi.ParchmentDim,
                    ElarionUi.FontTitle, TMPro.TextAlignmentOptions.Center, 0f, 1f, bold: true);
                q.raycastTarget = false;
                ElarionUiKit.FitSingleLine(q, 0f, ElarionUi.FontTitle);
            }

            // ── The TEXT COLUMN: everything right of the art band. It never shares pixels with the
            //    icon (fixed left inset), and its three bands never share pixels with each other.
            var text = new GameObject("TextColumn", typeof(RectTransform));
            text.transform.SetParent(go.transform, false);
            var trt = (RectTransform)text.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(SlotRowPadPx + 6f + SlotIconPx + 14f, SlotRowPadPx);
            trt.offsetMax = new Vector2(-(SlotRowPadPx + 6f), -SlotRowPadPx);

            // BAND 1 — the slot's own name. Always present, so the row is identifiable even empty.
            float cursor = 0f;
            var labelBand = Band(trt, "Band_Label", ref cursor, SlotLabelBandPx, 0f, 1f, SlotRowBandGapPx);
            var lbl = ElarionUiKit.Label(labelBand, SlotCaption(slotKey), 0f, 1f,
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left, 0f, 1f, bold: true);
            lbl.raycastTarget = false;
            ElarionUiKit.FitSingleLine(lbl, 0f, ElarionUi.FontLabel);

            // BAND 2 — the equipped item, or the WORD "Empty". Colourblind law: the state is said,
            // never merely tinted (the dim colour is decoration on top of the word, not the signal).
            var valueBand = Band(trt, "Band_Value", ref cursor, SlotValueBandPx, 0f, 1f, SlotRowBandGapPx);
            string valueText = filled ? ElarionUiKit.SpacedDisplayName(item.Name) : "Empty";
            var val = ElarionUiKit.Label(valueBand, valueText, 0f, 1f,
                filled ? ElarionUi.Parchment : ElarionUi.ParchmentDim,
                ElarionUi.FontBody, TMPro.TextAlignmentOptions.Left, 0f, 1f, bold: filled);
            val.raycastTarget = false;
            ElarionUiKit.FitSingleLine(val, 0f, ElarionUi.FontBody);

            // BAND 3 — why it matters (filled), or where to get one (empty). Never blank: an empty
            // band that renders nothing is indistinguishable from a band that failed to render.
            var hintBand = Band(trt, "Band_Hint", ref cursor, SlotHintBandPx, 0f, 1f, SlotRowBandGapPx);
            string hintText = filled ? (_vm != null ? _vm.GrantLineFor(slotKey) : "") : EmptySlotPointer(slotKey);
            if (string.IsNullOrEmpty(hintText))
                hintText = filled ? "No stat bonus" : "Nothing equipped";
            var hint = ElarionUiKit.Label(hintBand, hintText, 0f, 1f,
                filled ? ElarionUi.Gilt : ElarionUi.ParchmentDim,
                ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Left, 0f, 1f);
            hint.raycastTarget = false;
            ElarionUiKit.FitSingleLine(hint, 0f, ElarionUi.FontLabel);

            FlowTrace.Step("Equip", string.Format(
                "slot '{0}' bound: filled={1} item='{2}' iconResolved={3} value='{4}' hint='{5}'",
                slotKey, filled, filled ? item.Id : "-", iconSprite != null, valueText, hintText));
            return true;
        }

        // GrantLine MOVED to EquipVM.GrantLineFor (strict-MVVM: it read GearCatalog in the View).
        // BuildGearSlot now reads the filled slot's grant line off the bound VM — verbatim math.

        // EMPTY slots route the player to the system that fills them (owner spec: a pointer,
        // never a bare "Empty"). ASCII only.
        // The BUILDING WORDS come from the catalog (WO-1161: StructureRoles is the single
        // naming authority) — a pointer that names a building the town no longer calls by
        // that name sends the player looking for a sign that does not exist. Fallbacks are
        // generic, reached only if the catalog has not loaded.
        private static string EmptySlotPointer(string slotKey)
        {
            switch (slotKey)
            {
                case EquipVM.SlotAmulet:
                case EquipVM.SlotRing:
                    return $"Craft one at the {StructureRoles.By[StructureRole.Jeweler].DisplayName ?? "jeweler"}";
                case EquipVM.SlotMainhand:
                case EquipVM.SlotOffHand:
                case EquipVM.SlotChest:
                    return $"{StructureRoles.By[StructureRole.Weaponsmith].DisplayName ?? "Craft"} or buy gear in town";
                default:                   return "";
            }
        }

        private void OnSlotTapped(string slotKey)
        {
            if (_vm == null) return;
            // Toggle: tapping the open slot again closes the drawer.
            if (_drawerSlotKey == slotKey) { CloseDrawer(); return; }
            SelectSlotKey(slotKey);
            OpenDrawer(slotKey);
        }

        private void SelectSlotKey(string slotKey)
        {
            if (_vm == null) return;
            for (int i = 0; i < _vm.EquipSlots.Count; i++)
                if (_vm.EquipSlots[i].SlotKey == slotKey) { _vm.SelectSlot(i); return; }
        }

        // ── Change-drawer: a bottom tray listing compatible items for the chosen slot ──
        private void OpenDrawer(string slotKey)
        {
            CloseDrawer();
            _drawerSlotKey = slotKey;

            // WO-1015: the drawer is a fraction of the WELL, and the well now ends above the shared
            // bottom-centre Close band by construction (see the band budget in Open) — so the old
            // hand-tuned 0.17 floor, which was compensating for a well that ran under the Close, is
            // no longer needed and was simply wasting the drawer's own height.
            _drawerHost = ElarionUiKit.PanelFramed(_panelTransform,
                new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.94f),
                deep: true, packSpriteName: RpgUiCatalog.PanelWindowDark);
            var fill = ElarionUiKit.AddImage(_drawerHost.transform, "DrawerFill",
                new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.97f),
                new Color(0.04f, 0.045f, 0.05f, 0.99f));
            var fImg = fill.GetComponent<Image>();
            if (fImg != null) fImg.raycastTarget = false;
            fill.transform.SetAsFirstSibling();

            var drawerTitle = ElarionUiKit.Label(_drawerHost.transform, "Change " + SlotCaption(slotKey), 0.84f, 0.97f,
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
            ElarionUiKit.FitSingleLine(drawerTitle, 0f, ElarionUi.FontLabel);   // never wraps over the buttons

            // Unequip + Done buttons (top row of the drawer).
            var unequip = ElarionUiKit.ButtonPack(_drawerHost.transform, "Unequip", ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.06f, 0.84f), new Vector2(0.26f, 0.965f), () => { _vm?.Unequip(); }, RpgUiCatalog.ButtonFrame);
            CreamTab(unequip);
            var done = ElarionUiKit.ButtonPack(_drawerHost.transform, "Done", ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.74f, 0.84f), new Vector2(0.94f, 0.965f), CloseDrawer, RpgUiCatalog.ButtonFrame);
            CreamTab(done);

            // List host inside the drawer.
            _listContentArea = new GameObject("ListArea", typeof(RectTransform));
            _listContentArea.transform.SetParent(_drawerHost.transform, false);
            var la = _listContentArea.GetComponent<RectTransform>();
            la.anchorMin = new Vector2(0.05f, 0.06f); la.anchorMax = new Vector2(0.95f, 0.80f);
            la.offsetMin = Vector2.zero; la.offsetMax = Vector2.zero;

            RebuildList();
            RebuildSlots();   // refresh slot highlight
        }

        private void CloseDrawer()
        {
            _drawerSlotKey = null;
            _listContentArea = null;
            _scrollContent = null;
            if (_drawerHost != null) { Destroy(_drawerHost); _drawerHost = null; }
            RebuildSlots();
        }

        // ── Target picker (hero + companions) ────────────────────────────────────────
        // WO-1015: takes its own FIXED-PIXEL band (TargetBarPx >= MinTouchPx) instead of the old
        // (0.30,0.875)-(0.70,0.91) body fraction — 3.5% of a ~440px body was a 15px tall row of
        // touch targets, which ClampMinTouch then grew straight over the slots underneath.
        private void BuildTargetBar(RectTransform band)
        {
            if (band == null) return;
            var bar = new GameObject("TargetBar", typeof(RectTransform));
            bar.transform.SetParent(band, false);
            var br = bar.GetComponent<RectTransform>();
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
            br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;
            _targetBar = bar;

            var names = _vm != null ? _vm.TargetNames : (IReadOnlyList<string>)new List<string>();
            int n = Mathf.Max(1, names.Count);
            const float gap = 0.012f;
            float w = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < names.Count; i++)
            {
                int idx = i;
                float x0 = gap + i * (w + gap);
                // 0.96 * TargetBarPx(120) = 115px >= MinTouchPx(112), so ClampMinTouch never GROWS
                // a picker button out of its own band onto the content below it.
                var btn = ElarionUiKit.ButtonPack(bar.transform, names[i], ElarionUiKit.ButtonKind.Gold,
                    new Vector2(x0, 0.02f), new Vector2(x0 + w, 0.98f),
                    () => _vm?.SelectTarget(idx), RpgUiCatalog.ButtonFrame);
                CreamTab(btn);
                ElarionUiKit.ClampMinTouch(btn);
                if (btn != null) btn.name = "Tgt_" + idx;
            }
            HighlightTargets();
        }

        // ── WO-1015 E1: the in-panel "Orient" word-button is GONE from this screen ──────────
        // It was a DEV-GATED strand (#if DEVELOPMENT_BUILD || UNITY_EDITOR) copy-pasted into three
        // player-facing screens — the build palette (removed by WO-1010 D1), the inventory footer
        // (InventoryUIBuilder, removed by this WO) and here. Dev-gated is not the same as invisible:
        // the owner's felt-test builds ARE development builds, so it rendered on her screen every
        // time, and here it sat at body-fraction (0.02,0.02)-(0.20,0.07) which lands straight over
        // the Shield (Off Hand) plate and clipped its text.
        //
        // The tool itself is NOT removed and NOT reimplemented: SeatingEditorOverlay keeps its ONE
        // sanctioned entry point, AdminOverlay ("Orient Asset" / "Seating Editor",
        // Assets/_Modules/HUD/AdminOverlay.cs:361 + :608) — a dev screen, not a gameplay screen.
        // Do not re-add a per-screen launcher: EquipmentScreenLayoutRegression [equipment-screen-layout]
        // fails the build if the word "Orient" reappears as a control in this file.

        private void HighlightTargets()
        {
            if (_targetBar == null || _vm == null) return;
            string active = "Tgt_" + _vm.ActiveTargetIndex;
            foreach (Transform child in _targetBar.transform)
            {
                if (child == null) continue;
                var img = child.GetComponent<Image>();
                if (img != null) img.color = child.name == active ? TabSelectedTint : TabRestTint;
            }
        }

        // ── List build (inside the drawer) ───────────────────────────────────────────
        private void RebuildList()
        {
            using var _ = FlowTrace.Enter("Equip", $"EquipmentPanel.RebuildList slot={_vm?.SelectedSlotKey}");
            _scrollContent = null;
            if (_listContentArea == null) return;
            for (int i = _listContentArea.transform.childCount - 1; i >= 0; i--)
            {
                var c = _listContentArea.transform.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }

            var listRoot = BuildScrollContent();
            int wantCount = _vm != null ? _vm.CompatibleItems.Count : 0;

            var (built, failed) = _vm != null
                ? Guard.TryEach("Equip", "build gear row", _vm.CompatibleItems,
                    item => CreateGearRow(listRoot, item))
                : (0, 0);

            FlowTrace.Step("Equip",
                $"EquipmentPanel stocked {built} gear row(s) (wanted {wantCount}, failed {failed}).");

            if (built == 0)
            {
                if (wantCount == 0)
                    FlowTrace.Warn("Equip",
                        $"EquipmentPanel has NO compatible items for slot {_vm?.SelectedSlotKey} - empty-state row (data-empty).");
                else
                    FlowTrace.Fail("Equip",
                        $"EquipmentPanel had {wantCount} item(s) but built 0 rows ({failed} failed) - empty-state row (built-but-broken).");
                CreateEmptyStateRow(listRoot, "No gear available for this slot.");
            }

            FinalizeScroll();
        }

        private void CreateEmptyStateRow(Transform parent, string msg)
        {
            var go = new GameObject("EmptyStateRow", typeof(TMPro.TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeightPx;
            le.minHeight = RowHeightPx;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            t.text = msg;
            t.fontSize = ElarionUi.FontLabel;
            t.color = ElarionUi.ParchmentDim;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.raycastTarget = false;
            ElarionUiKit.FitSingleLine(t);   // empty-state copy fits its row
        }

        private Transform BuildScrollContent()
        {
            var well = ElarionUiKit.Well(_listContentArea.transform, Vector2.zero, Vector2.one);
            var wImg = well.GetComponent<Image>();
            if (wImg != null) wImg.raycastTarget = false;

            var viewport = new GameObject("Viewport", typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.transform.SetParent(_listContentArea.transform, false);
            var vr = viewport.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
            var vImg = viewport.GetComponent<Image>();
            vImg.color = new Color(0f, 0f, 0f, 0.001f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("ScrollContent", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta = Vector2.zero;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = RowGapPx;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = vr;
            scroll.content = cr;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            _scrollContent = cr;
            return content.transform;
        }

        private void FinalizeScroll()
        {
            if (_scrollContent == null) return;
            Canvas.ForceUpdateCanvases();
            var contentArea = _listContentArea != null ? _listContentArea.transform as RectTransform : null;
            if (contentArea != null) LayoutRebuilder.ForceRebuildLayoutImmediate(contentArea);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        }

        // One gear row in the drawer: slot-appropriate glyph + name + Equip CTA.
        private void CreateGearRow(Transform parent, ItemVM row)
        {
            var go = new GameObject("GearRow_" + row.Id, typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeightPx;
            le.minHeight = RowHeightPx;
            var rowImg = go.GetComponent<Image>();
            DressRowPlate(rowImg, row.Equipped);

            string slotKey = _vm != null ? _vm.SelectedSlotKey : EquipVM.SlotMainhand;
            var sock = ElarionUiKit.TechGearSocket(go.transform, "Socket",
                new Vector2(0.02f, 0.12f), new Vector2(0.16f, 0.88f),
                new Color(0.85f, 0.7f, 0.2f, 0.9f), isWeapon: slotKey == EquipVM.SlotMainhand);
            sock.GetComponent<Image>().raycastTarget = false;
            var iconSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, SlotIconName(slotKey));
            var iconGo = ElarionUiKit.AddImage(sock.transform, "Icon",
                new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), Color.white, rounded: false);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.raycastTarget = false;
            if (iconSprite != null) { iconImg.sprite = iconSprite; iconImg.preserveAspect = true; }
            else iconImg.color = new Color(0f, 0f, 0f, 0f);

            string nameText = row.Name;
            if (row.Equipped) nameText += "   [Equipped]";
            var rowName = ElarionUiKit.Label(go.transform, nameText, 0.18f, 0.92f,
                row.Equipped ? ElarionUi.Gilt : ElarionUi.Parchment,
                ElarionUi.FontBody, TMPro.TextAlignmentOptions.Left, 0.18f, 0.74f, bold: row.Equipped);
            ElarionUiKit.FitSingleLine(rowName, 0f, ElarionUi.FontBody);   // never wraps under the Equip CTA

            string id = row.Id;
            bool isWeaponSlot = slotKey == EquipVM.SlotMainhand;
            var btn = ElarionUiKit.TechPrimaryButton(go.transform, row.Equipped ? "Equipped" : "Equip",
                new Vector2(0.76f, 0.14f), new Vector2(0.98f, 0.86f),
                () => DoEquip(id, isWeaponSlot));
            if (btn != null) btn.interactable = !row.Equipped;
        }

        private static void DressRowPlate(Image rowImg, bool equipped)
        {
            if (rowImg == null) return;
            var plate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotItem);
            if (plate != null)
            {
                rowImg.sprite = plate;
                rowImg.type   = Image.Type.Sliced;
                rowImg.color  = equipped ? new Color(1.15f, 1.10f, 0.92f, 1f) : Color.white;
                return;
            }
            rowImg.color = equipped ? ElarionUiKit.CellSelected : ElarionUiKit.Cell;
            ElarionUiKit.ApplyRounded(rowImg);
        }

        private void DoEquip(string id, bool isWeapon)
        {
            if (_vm == null) return;
            _vm.Equip(id);

            // WO-1214 Ruling 2 — a REFUSED equip must not be reported as a success. This block
            // used to be a single unconditional "Equipped <item>." toast, which meant a Mage who
            // tapped a dropped shield was TOLD the shield was equipped while the seam had refused
            // it and changed nothing. That is worse than a silent failure: the player walks away
            // believing the slot changed. The refusal SENTENCE is shown instead — words naming the
            // reason, never a greyed control and never colour alone (the owner is red/green
            // colourblind, so the tone is a redundant cue and never the message).
            string refusal = _vm.LastRefusal;
            if (!string.IsNullOrEmpty(refusal))
            {
                ElarionUiKit.ShowToast(refusal, ElarionUiKit.ToastTone.Danger);
                DeNelle.Core.Diagnostics.FlowTrace.Warn("EquipPanel",
                    $"DoEquip('{id}') REFUSED - shown to the player as: \"{refusal}\" (the item stays in the bag " +
                    "and stays sellable). No slot was changed and no confirmation was claimed.");
                return;
            }

            if (_vm.ActiveTargetIndex == 0 && _equip != null && (id == "basic_sword" || id == "leather_armor"))
                _equip.Equip(id);
            // WO-713 A.5 — visible confirmation as the ONE transient kit toast (WO-714 P5);
            // the id is spaced so a raw itemId never reaches the player (P10).
            ElarionUiKit.ShowToast("Equipped " + ElarionUiKit.SpacedDisplayName(id) + ".",
                ElarionUiKit.ToastTone.Confirm);
            Debug.Log($"[EquipmentPanel] Equipped {id} via EquipVM - hero visual/stat updated.");
        }

        // =====================================================================
        //  WO-1015 E2 — the hero preview: STRUCTURAL fallback + §12 instrumentation
        // ---------------------------------------------------------------------
        //  The owner's capture shows a flat dark-navy rectangle. That colour is AMBIGUOUS by
        //  construction and this is why it must be instrumented rather than guessed at: it is
        //  simultaneously (a) this host's own fill Color(0.02, 0.047, 0.094), (b) the preview
        //  camera's clearFlags SolidColor backgroundColor — the SAME rgb, HeroPreviewViewer.cs's
        //  "#050c18 viewport bg". So a live render of an EMPTY frustum, a render that never ran,
        //  and a RawImage that was never enabled all look IDENTICAL on screen. No screenshot can
        //  separate them; only a trace can.
        //
        //  The candidates the instrumentation separates, and the line that settles each:
        //    A  no source body        -> "preview: body=NULL"            (EquipmentPanel, below)
        //    B  rig/RT never built    -> "Begin returned ok=False"       (EquipmentPanel, below)
        //    C  RT alloc failed       -> "rt.Create() FAILED"            (HeroPreviewViewer.Begin)
        //    D  camera sees nothing   -> "cullingMask layer=<n> named=<bool>" + the renderer
        //                                enumeration already in Begin (renderer count / layer)
        //    E  model degenerate      -> "bounds=<size> dist=<f>"        (HeroPreviewViewer.Begin)
        //    F  drew nothing at all   -> "RT PROBE: uniform clear colour" (the decisive line —
        //                                a readback that reports whether ANY pixel differs from
        //                                the camera's clear colour; that is the ONLY signal that
        //                                distinguishes "rendered an empty scene" from "rendered
        //                                the hero", and it cannot be inferred from source)
        //    G  built but not shown   -> "outcome=fallback reason=..."   (EquipmentPanel, below)
        //
        //  AND, regardless of which one it turns out to be, the box is no longer allowed to be
        //  blank: the NAME band and the STATE band below are built unconditionally in fixed
        //  pixels, before any of the render machinery is touched. Even with zero art and zero
        //  render texture the player reads "Thrain the Wise / Portrait view - live model
        //  unavailable". A paperdoll screen showing NOTHING is now unreachable.
        // =====================================================================
        private void BuildPreviewWidget(Transform parent)
        {
            var host = ElarionUiKit.AddImage(parent, "HeroPreview",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Color(0.02f, 0.047f, 0.094f, 1f), rounded: false);
            var hostRt = (RectTransform)host.transform;
            var hostImg = host.GetComponent<Image>();
            if (hostImg != null)
            {
                hostImg.raycastTarget = false;
                // Central hero plate: real Obsidian character socket (UI_BLINK_TEMPLATE_CANON §4),
                // sprite-FIRST; the dark glass fill stays as the WebGL-safe null fallback.
                var charPlate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotCharacter);
                if (charPlate != null)
                {
                    hostImg.sprite = charPlate;
                    hostImg.type   = Image.Type.Sliced;
                    hostImg.color  = Color.white;
                }
            }

            // ── THE FALLBACK BANDS FIRST. Fixed pixels, pinned to the bottom, built before the
            //    render rig is even considered — so no failure downstream can prevent them.
            float up = PreviewPadPx;
            var stateBand = BandFromBottom(hostRt, "Band_PreviewState", ref up, PreviewStateBandPx);
            var nameBand  = BandFromBottom(hostRt, "Band_PreviewName",  ref up, PreviewNameBandPx);

            _previewNameLabel = ElarionUiKit.Label(nameBand, HeroFullName(ResolveActiveHeroJob()), 0f, 1f,
                ElarionUi.Gilt, ElarionUi.FontBody, TMPro.TextAlignmentOptions.Center, 0f, 1f, bold: true);
            _previewNameLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(_previewNameLabel, 0f, ElarionUi.FontBody);

            // Colourblind law: the live/fallback distinction is a WORD, never a tint.
            _previewStateLabel = ElarionUiKit.Label(stateBand, "Loading view...", 0f, 1f,
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Center, 0f, 1f);
            _previewStateLabel.raycastTarget = false;
            ElarionUiKit.FitSingleLine(_previewStateLabel, 0f, ElarionUi.FontLabel);

            // ── The ART REGION: everything above the two fallback bands.
            float artFloorPx = PreviewPadPx + PreviewStateBandPx + SlotRowBandGapPx
                             + PreviewNameBandPx + SlotRowBandGapPx;
            var art = new GameObject("PreviewArt", typeof(RectTransform));
            art.transform.SetParent(hostRt, false);
            var artRt = (RectTransform)art.transform;
            artRt.anchorMin = Vector2.zero; artRt.anchorMax = Vector2.one;
            artRt.offsetMin = new Vector2(PreviewPadPx, artFloorPx);
            artRt.offsetMax = new Vector2(-PreviewPadPx, -PreviewPadPx);

            // WO-713 §D: the Obsidian sil_male silhouette sits BEHIND the live render — when the
            // RT rig is unavailable the well still reads as a hero, not a black box.
            var silGo = ElarionUiKit.AddImage(artRt, "Silhouette",
                new Vector2(0.10f, 0.02f), new Vector2(0.90f, 0.98f),
                new Color(0f, 0f, 0f, 0f), rounded: false);
            _previewSilhouette = silGo.GetComponent<Image>();
            _previewSilhouette.raycastTarget = false;
            var silSp = RpgUiCatalog.Get(RpgUiCatalog.RoleSilhouette, RpgUiCatalog.SilMale);
            bool silResolved = silSp != null;
            if (silResolved)
            {
                _previewSilhouette.sprite = silSp;
                _previewSilhouette.preserveAspect = true;
                _previewSilhouette.color = new Color(1f, 1f, 1f, 0.55f);
            }
            else
            {
                // NO ART. The old code set _previewSilhouette = null here, and HidePreview then had
                // literally nothing to show — that is one live path to the owner's blank box, and it
                // fails SILENTLY. It is now said out loud, and the name/state bands carry the box.
                _previewSilhouette.enabled = false;
                _previewSilhouette = null;
                FlowTrace.Warn("Equip",
                    "preview: silhouette art '" + RpgUiCatalog.SilMale + "' did NOT resolve from role '" +
                    RpgUiCatalog.RoleSilhouette + "' - the art fallback is unavailable on this build. " +
                    "The name + state WORD bands are the fallback instead (they always render).");
            }

            var imgGo = new GameObject("PreviewRawImage", typeof(RectTransform), typeof(RawImage));
            imgGo.transform.SetParent(artRt, false);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _previewImage = imgGo.GetComponent<RawImage>();
            _previewImage.raycastTarget = false;
            _previewImage.color = Color.white;
            _previewImage.enabled = false;

            FlowTrace.Step("Equip", string.Format(
                "preview widget built: art floor={0:0}px above the fixed name({1:0})+state({2:0}) bands, " +
                "silhouetteArt={3}, charPlate={4}. Fallback bands are unconditional - a blank preview " +
                "box is not reachable from here.",
                artFloorPx, PreviewNameBandPx, PreviewStateBandPx, silResolved,
                RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotCharacter) != null));
        }

        private void BeginOrRetargetPreview()
        {
            if (_previewImage == null) return;

            var body = ActiveBody();
            string weaponId = ActiveWeaponId();
            string offHandId = ActiveOffHandId();
            int armorTier = ActiveArmorTier();

            // CANDIDATE A. Proves whether the panel ever HAD a body to preview. If this line says
            // NULL, nothing about render textures, layers or materials is relevant — ResolveBody /
            // the target list is the dead step and no preview code should be touched.
            FlowTrace.Step("Equip", string.Format(
                "preview: targetIndex={0} body={1} bodies={2} weapon='{3}' offHand='{4}' armorTier={5}",
                _vm != null ? _vm.ActiveTargetIndex : -1,
                body != null ? body.name : "NULL", _targetBodies.Count,
                weaponId ?? "-", offHandId ?? "-", armorTier));

            if (body == null)
            {
                ShowPreviewFallback("no hero body resolved for the active target (ResolveBody found " +
                                    "neither a 'HeroBody' child nor a tagged Player root)");
                return;
            }

            bool ok;
            bool fresh = _preview == null;
            if (fresh)
            {
                _preview = new HeroPreviewViewer();
                ok = _preview.Begin(body, textureSize: 512, weaponId: weaponId,
                                    offHandId: offHandId, armorTier: armorTier);
            }
            else
            {
                ok = _preview.Retarget(body, weaponId, offHandId, armorTier);
                if (!ok) ok = _preview.IsValid;
            }

            // CANDIDATES B/C/G. Each field is reported separately rather than as one boolean, so the
            // trace names WHICH link failed instead of only that the chain did.
            var tex = _preview != null ? _preview.Texture : null;
            FlowTrace.Step("Equip", string.Format(
                "preview: {0} returned ok={1} IsValid={2} texture={3} ({4}x{5}, created={6})",
                fresh ? "Begin" : "Retarget", ok, _preview != null && _preview.IsValid,
                tex != null ? "present" : "NULL",
                tex != null ? tex.width : 0, tex != null ? tex.height : 0,
                tex != null && tex.IsCreated()));

            if (ok && _preview.IsValid && tex != null)
            {
                _previewImage.texture = tex;
                _previewImage.enabled = true;
                _preview.SetRotation(18f);
                if (_previewSilhouette != null) _previewSilhouette.enabled = false;
                if (_previewStateLabel != null) _previewStateLabel.text = "Live view";

                // CANDIDATE F — THE DECISIVE LINE. Everything above proves the rig was CONSTRUCTED;
                // only this proves anything was DRAWN. Without it a rig that renders an empty
                // frustum reports a perfect green chain and still shows the owner a flat navy box.
                _preview.ProbeRenderedContent("Equip");
                FlowTrace.Step("Equip", "preview: outcome=LIVE (render texture bound and shown).");
            }
            else
            {
                ShowPreviewFallback(string.Format(
                    "{0} ok={1}, IsValid={2}, texture={3}",
                    fresh ? "Begin" : "Retarget", ok,
                    _preview != null && _preview.IsValid, tex != null ? "present" : "NULL"));
            }
        }

        private void RefreshPreviewWeapon()
        {
            if (_preview == null || !_preview.IsValid) return;
            // WO-567: mirror the full equipped look (weapon + shield + armor tint), not just weapon.
            _preview.RefreshGear(ActiveWeaponId(), ActiveOffHandId(), ActiveArmorTier());
        }

        /// <summary>
        /// The ONE fallback path. Hides the dead render, brings the silhouette back if there is
        /// art, and — always — puts the reason in the trace and a WORD on the screen. There is no
        /// code path from here to an empty box: the name and state bands are already built.
        /// </summary>
        private void ShowPreviewFallback(string reason)
        {
            HidePreview();
            if (_previewStateLabel != null)
                _previewStateLabel.text = _previewSilhouette != null
                    ? "Portrait view - live model unavailable"
                    : "Live model unavailable";
            FlowTrace.Warn("Equip", "preview: outcome=FALLBACK reason=" + reason +
                                    " (silhouette=" + (_previewSilhouette != null) +
                                    "; the name + state bands carry the box either way).");
        }

        private void HidePreview()
        {
            if (_previewImage != null) { _previewImage.enabled = false; _previewImage.texture = null; }
            // Obsidian silhouette stands in whenever the live render is off (WO-713 §D law).
            if (_previewSilhouette != null) _previewSilhouette.enabled = true;
        }

        private void DisposePreview()
        {
            _preview?.Dispose();
            _preview = null;
            HidePreview();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────
        private SlotVM? FindSlot(string slotKey)
        {
            if (_vm == null) return null;
            foreach (var s in _vm.EquipSlots)
                if (s.SlotKey == slotKey) return s;
            return null;
        }

        private static string SlotCaption(string slotKey)
        {
            switch (slotKey)
            {
                case EquipVM.SlotMainhand: return "Weapon (Main Hand)";
                case EquipVM.SlotOffHand:  return "Shield (Off Hand)";
                case EquipVM.SlotChest:    return "Full Armor Set";
                case EquipVM.SlotAmulet:   return "Amulet";
                case EquipVM.SlotRing:     return "Ring";
                default:                   return slotKey;
            }
        }

        // Resolve a filled slot's REAL item art from its ItemVM (role + id), mirroring
        // InventoryGrid.ResolveItemIcon so the doll's slots match the grid. Returns null when no
        // art resolves (caller falls back to the slot glyph) — so accessories/shields with no sheet
        // art keep their sensible glyph (heart/compass/shield), never the gold inventory bag.
        private static Sprite ResolveSlotItemArt(string slotKey, ItemVM item)
        {
            // OFF-HAND is shields-only (V1): resolve SHIELD art via the ARMOR role (GearIconCatalog
            // maps armor -> ItemIconCatalog.ForArmor, which has the shield keyword handling), NEVER
            // the weapon role. The off-hand ItemVM is tagged IconRoleWeapon, and the weapon path falls
            // through to a SWORD sprite for a shield id — owner bug "offhand shows a sword". Null ->
            // caller falls back to the shield glyph (a shield, never a sword).
            if (slotKey == EquipVM.SlotOffHand)
                return GearIconCatalog.Resolve(EquipVM.IconRoleArmor, item.Id);
            // Weapon/armor slots resolve by the item's own role through the presentation seam (this
            // View no longer names GearCatalog); accessories have no sheet art -> null (glyph fallback).
            return GearIconCatalog.Resolve(item.IconRole, item.Id);
        }

        private static string SlotIconName(string slotKey)
        {
            switch (slotKey)
            {
                case EquipVM.SlotMainhand: return RpgUiCatalog.IconSword;
                case EquipVM.SlotOffHand:  return RpgUiCatalog.IconShield;
                case EquipVM.SlotChest:    return RpgUiCatalog.IconInventory;
                case EquipVM.SlotAmulet:   return RpgUiCatalog.IconHeart;
                case EquipVM.SlotRing:     return RpgUiCatalog.IconCompass;
                default:                   return RpgUiCatalog.IconInventory;
            }
        }

        private static Color RarityTint(string rarity)
        {
            switch ((rarity ?? "").ToLowerInvariant())
            {
                case "rare":      return new Color(0.55f, 0.75f, 1f, 1f);
                case "epic":      return new Color(0.78f, 0.55f, 1f, 1f);
                case "legendary": return new Color(1f, 0.84f, 0.32f, 1f);
                default:          return Color.white;
            }
        }

        private static void CreamTab(Button btn)
        {
            if (btn == null) return;
            var lbl = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (lbl == null) return;
            lbl.color = ElarionUi.Parchment;
            lbl.fontStyle = TMPro.FontStyles.Bold;
            lbl.outlineColor = new Color32(20, 12, 4, 235);
            lbl.outlineWidth = 0.22f;
            lbl.transform.SetAsLastSibling();
        }

        private static string HeroName(string job)
        {
            switch ((job ?? "").ToLowerInvariant())
            {
                case "knight": return "Grom";
                case "mage":   return "Thrain";
                case "ranger": return "Sylas";
                case "cleric": return "Elara";
                default:        return Cap(job);
            }
        }

        // Canon FULL name for the panel header (matches HeroInventoryController.HeroDisplayName).
        private static string HeroFullName(string job)
        {
            switch ((job ?? "").ToLowerInvariant())
            {
                case "knight": return "Grom Ironhand";
                case "mage":   return "Thrain the Wise";
                case "ranger": return "Sylas Swift";
                case "healer":
                case "cleric": return "Elara Dawnlight";
                default:        return Cap(job);
            }
        }

        // Active hero's class for the header (no loadout needed) — mirrors ResolveHeroJob's source.
        private static string ResolveActiveHeroJob()
        {
            var ha = FindAnyObjectByType<HeroAbilities>();
            string j = ha != null ? ha.HeroClass : null;
            return string.IsNullOrEmpty(j) ? AbilityCatalog.DefaultClass : j;
        }

        private static string Cap(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private void DisposeViewModel()
        {
            Unbind();
            _vm?.Dispose();
            _vm = null;
            _store?.Dispose();
            _store = null;
            foreach (var a in _targetAdapters) a?.Dispose();
            _targetAdapters.Clear();
        }

        private void Close()
        {
            DisposeViewModel();
            DisposePreview();
            if (_ui != null) Destroy(_ui);
            _ui = null;
            _slotListContent = null;
            _slotListScrolls = false;
            _previewNameLabel = null;
            _previewStateLabel = null;
            _targetBar = null;
            _drawerHost = null;
            _drawerSlotKey = null;
            _listContentArea = null;
            _scrollContent = null;
            _panelTransform = null;
            _previewImage = null;
            _previewSilhouette = null;
            _previewTargetIndex = -1;
            _targetBodies.Clear();
            PanelManager.NotifyClosed(_panelHandle);
        }

        private void OnDestroy()
        {
            DisposeViewModel();
            DisposePreview();
            if (_ui != null) Destroy(_ui);
            PanelRouter.Unregister(PanelId.EquipmentPanel, Open);
            PanelManager.NotifyClosed(_panelHandle);
        }
    }
}
