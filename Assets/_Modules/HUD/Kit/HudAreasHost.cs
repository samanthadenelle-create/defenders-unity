// =============================================================================
// HudAreasHost — ONE screen-space canvas; the named ACTIONABLE AREAS as stable
// mount RectTransforms (HUD_OBSIDIAN_ARCHITECTURE_2026-07-03 A4/A4.1 — P23).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD.Kit
//
// THE OWNER'S TAXONOMY (A4): the HUD kit is GENERIC — one skin divided into
// named actionable areas, each a stable mount point with a wiring contract.
// A4.1 first division (them|us): the HOSTILE tree sits territorially HIGH
// (status crown / targetInfo), the FRIENDLY tree sits LOW at the thumbs
// (actionBar/actionRail/moveCluster/dock). system + feedback are overlays
// outside both trees.
//
// This host builds ONLY scaffolding (canvas + empty RectTransform mounts) —
// zero widgets, zero art. Widgets land in the mounts via HudKitController,
// all from the ElarionUiKit factory (§5 review law).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;   // WO-1219: HudLayoutBands owns the left-column geometry

namespace DeNelle.HUD.Kit
{
    /// <summary>The named actionable areas (A4). Values are hud-areas.json keys.</summary>
    public enum HudArea
    {
        /// <summary>FRIENDLY — hero vitals/telemetrics (top-left).</summary>
        Vitals,
        /// <summary>HOSTILE crown — wave block / heart / target cycle (top-center).</summary>
        Status,
        /// <summary>Overlay chrome — settings/pause/flee (top-right).</summary>
        System,
        /// <summary>HOSTILE — target frame + cast telegraph (high, under the crown).</summary>
        TargetInfo,
        /// <summary>FRIENDLY — big attack + rail slots (bottom-right thumb arc).</summary>
        ActionRail,
        /// <summary>FRIENDLY — ability/potion/town-action row (bottom-center).</summary>
        ActionBar,
        /// <summary>FRIENDLY — the four round movement buttons (bottom-left thumb).</summary>
        MoveCluster,
        /// <summary>Overlay — combat stamps / toasts (full screen, never raycast).</summary>
        Feedback,
        /// <summary>FRIENDLY — chat/social dock (left edge).</summary>
        Dock,
        /// <summary>FRIENDLY — Heart of Elarion / tree-of-life status (left, just below vitals).</summary>
        HeartStatus,
        /// <summary>FRIENDLY — persistent Builders/Training status chip (right, under System; WO-778).</summary>
        QueueStatus,
        /// <summary>FRIENDLY — the corner minimap "you are here" plate (left column, between
        /// the Dock and HeartStatus; WO-828). Calm postures only — see hud-areas.json.</summary>
        Minimap,
    }

    /// <summary>One canvas, nine mounts. Pure scaffolding (see header).</summary>
    public sealed class HudAreasHost : MonoBehaviour
    {
        private readonly Dictionary<HudArea, RectTransform> _mounts = new Dictionary<HudArea, RectTransform>();

        // ── WO-1319: the ActionBar band's x edges, named ONCE ────────────────────────
        // They were two literals inside Build(). The narrow-aspect dock has to know how much
        // free width sits beside the mount, and a second copy of "0.730" living in the dock
        // would be the same duplicated-state drift CLAUDE.md keeps calling out. So the band
        // is authored here and the headroom is DERIVED, never re-typed.
        /// <summary>ActionBar mount LEFT edge. This is also the MoveCluster's RIGHT edge — the
        /// dock may never grow past it, or the bar sits under the movement stick.</summary>
        public const float ActionBarMinX = 0.270f;
        /// <summary>ActionBar mount RIGHT edge (WO-835 widened 0.720 -> 0.730).</summary>
        public const float ActionBarMaxX = 0.730f;
        /// <summary>The canvas-safe right edge every right-hand mount stops at (System /
        /// ActionRail / QueueStatus all end here). Nothing occupies the bottom band between
        /// <see cref="ActionBarMaxX"/> and this, so the dock's overflow width comes from here.</summary>
        public const float SafeRightX = 0.995f;

        /// <summary>Free width to the RIGHT of the ActionBar mount, as a MULTIPLE of the mount's
        /// own width. WO-1319 tier 2 grows the peaceful dock into exactly this and no further.</summary>
        public static float ActionBarRightHeadroomRatio
        {
            get { return (SafeRightX - ActionBarMaxX) / (ActionBarMaxX - ActionBarMinX); }
        }

        /// <summary>The host canvas.</summary>
        public Canvas Canvas { get; private set; }

        /// <summary>The whole-HUD fade/interactivity group (SetHudVisible seam).</summary>
        public CanvasGroup Group { get; private set; }

        /// <summary>The mount for an area (never null after Build).</summary>
        public RectTransform Mount(HudArea area)
        {
            RectTransform rt;
            return _mounts.TryGetValue(area, out rt) ? rt : null;
        }

        /// <summary>Create the host canvas + all area mounts on a fresh GameObject.</summary>
        public static HudAreasHost Create(Transform parentSceneObject)
        {
            var go = new GameObject("HudAreasHost");
            if (parentSceneObject != null) go.transform.SetParent(parentSceneObject, false);
            var host = go.AddComponent<HudAreasHost>();
            host.Build();
            return host;
        }

        private void Build()
        {
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 4000;   // above world/legacy chrome, below battle overlays (5000)/modals (30000+)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            Group = gameObject.AddComponent<CanvasGroup>();

            // A4 area geometry (reference-fraction anchors; hostile HIGH, friendly LOW/thumbs).
            // WO-1219: the Vitals mount spans the hero plate AND the SKILL chip band
            // beneath it; the two are exclusive sub-rects (HudLayoutBands.HeroPlateInVitals /
            // SkillChipInVitals), never one shared box.
            Add(HudArea.Vitals,      HudLayoutBands.VitalsMount);
            Add(HudArea.Status,      new Vector2(0.340f, 0.845f), new Vector2(0.660f, 0.990f));
            Add(HudArea.System,      new Vector2(0.845f, 0.880f), new Vector2(0.995f, 0.985f));
            Add(HudArea.TargetInfo,  new Vector2(0.280f, 0.660f), new Vector2(0.720f, 0.840f));
            // Peaceful economy plaque lives in the approved top-right information column.
            // Combat leaves this mount empty, so thumb actions remain owned by ActionBar.
            Add(HudArea.ActionRail,  new Vector2(0.780f, 0.770f), new Vector2(0.995f, 0.965f));
            // WO-835: widened 0.280-0.720 -> 0.270-0.730 (still symmetric, clear of the
            // MoveCluster right edge at 0.270 and the ActionRail left edge at 0.780) so the
            // 7-face applicability MAX (Build/Talk/Bag/Raids/Map/Quests/Upgrade) keeps each
            // face near the previous 6-face touch size at the constant per-button width.
            // ⭐ WO-1436 - THE Y EDGES ARE NO LONGER AUTHORED HERE. They are
            // HudLayoutBands.ThumbActionRowMinY/MaxY (DeNelle.Core.UI), because the raid deploy
            // bar in DeNelle.Village has to stack ON TOP of this row and DeNelle.Village may not
            // reference DeNelle.HUD (CLAUDE.md §5). Two literals - one here, one there - is the
            // duplicated-state failure CLAUDE.md documents four times over; the band is shared
            // DATA instead. The X edges stay local: nothing outside this assembly needs them, and
            // HudDockLayoutRegression pins their source text verbatim.
            Add(HudArea.ActionBar,   new Vector2(ActionBarMinX, HudLayoutBands.ThumbActionRowMinY),
                                     new Vector2(ActionBarMaxX, HudLayoutBands.ThumbActionRowMaxY));
            // ⭐ WO-1464 - THE STICK'S BAND IS NO LONGER AUTHORED HERE. It is
            // HudLayoutBands.MoveClusterMount (DeNelle.Core.UI), because the raid deploy tray in
            // DeNelle.Village has to start to the RIGHT of it and DeNelle.Village may not
            // reference DeNelle.HUD (CLAUDE.md §5). The tray was drawn straight across the stick
            // in the owner's 2026-09-07 capture for exactly the reason WO-1436's bar was drawn
            // across the ability row: a Village-local literal cannot see a HUD-local one.
            // ⚠ ActionBarMinX above is documented as "also the MoveCluster's RIGHT edge" and its
            // source text is pinned verbatim by HudDockLayoutRegression, so it stays a const here;
            // RaidHudThumbBandRegression asserts the two agree so the drift is a red build.
            Add(HudArea.MoveCluster, HudLayoutBands.MoveClusterMount);
            // ⭐ WO-1219 - THE LEFT COLUMN IS NO LONGER AUTHORED HERE.
            // Vitals / HeartStatus / Minimap / Dock are read from DeNelle.Core.UI.HudLayoutBands,
            // the ONE table that owns the whole column (hero plate -> SKILL chip -> Heart bar ->
            // minimap -> status line -> gear + Store). Four files used to place seven things into
            // one strip with nobody owning the sum, which is exactly how the gear/Store row came to
            // sit on the minimap's lower edge and the region status line came to read out from
            // UNDER the gear (tmp/screen-103219.png + tmp/shield-seat-101829.png, both 2670x1200).
            // HudUiRegression check 8 resolves the same table and FAILS the build if any two bands
            // intersect at the owner's resolution, so the collision is a red gate now, not a
            // felt-test report. ⛔ Do not hardcode a left-column rect here again.
            Add(HudArea.Dock,        HudLayoutBands.DockMount);
            Add(HudArea.HeartStatus, HudLayoutBands.HeartMount);
            // WO-778: Builders/Training chip — right column, below System (0.880) and clear of
            // the ActionRail band above it; the only occupant of this free band (no collision).
            // WO-864 (2026-08-03): the occupant is now a MinTouchPx summary button over a
            // QueueRailView card rail, BOTH laid out in FIXED PIXELS off the top of this
            // band. Nothing inside is a fraction of the band any more, so leftover height is
            // transparent rather than the old full-height dark rows plate that reserved five
            // rows to show one job.
            // ⚠ THE TWO ".42" / "0.420" FIGURES THIS COMMENT CARRIED ARE RETIRED (WO-1670,
            // 2026-09-10). They named the ActionRail's bottom edge, which is authored 0.770 five
            // lines above — the value went stale where it stood while the code beside it moved,
            // and WO-1642 §6.1 caught it. The clearance is REAL either way (this band tops out at
            // 0.750, one clearance gap under 0.770) but read it off the Add(HudArea.ActionRail)
            // call, never off a sentence here. Same for the "318 ref px inside ~328" figure that
            // used to follow: it described the pre-0.510 band and is deliberately not re-typed —
            // HudUiRegression check 11 DERIVES the resting stack's depth from source instead.
            // ⭐ WO-1670 — THE Y EDGES ARE NO LONGER AUTHORED HERE. They are
            // HudLayoutBands.QueueStatusMount (DeNelle.Core.UI), because the Echoes chip in
            // DeNelle.Village has to seat BELOW this band's floor and DeNelle.Village may not
            // reference DeNelle.HUD (CLAUDE.md §5). The chip restated 0.475 as a centre it could
            // not see, and its 112 px box reached 0.022 inside this mount on the owner's Seeker.
            // Third time in this file (ThumbActionRowMinY WO-1436, MoveClusterMount WO-1464), one
            // pattern: two literals in two assemblies is duplicated state; the band is shared DATA.
            Add(HudArea.QueueStatus, HudLayoutBands.QueueStatusMount);
            // The Minimap mount now carries TWO exclusive bands: the square plate hanging from its
            // top-left, and the region STATUS LINE in its own band immediately below the plate -
            // never across it, never beside it competing with the Dock row.
            Add(HudArea.Minimap,     HudLayoutBands.MinimapMount);
            Add(HudArea.Feedback,    Vector2.zero,                Vector2.one);

            // Feedback overlay never eats taps (stamps/toasts are decorative).
            var fb = Mount(HudArea.Feedback);
            if (fb != null) fb.SetAsLastSibling();

            FlowTrace.Step("HudKit", "HudAreasHost built: 9 area mounts on one canvas (scaffolding only)");
        }

        /// <summary>WO-1219: mount an area straight from an authored band rect.</summary>
        private void Add(HudArea area, Rect band)
        {
            Add(area, new Vector2(band.xMin, band.yMin), new Vector2(band.xMax, band.yMax));
        }

        private void Add(HudArea area, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Area_" + area, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _mounts[area] = rt;
        }
    }
}
