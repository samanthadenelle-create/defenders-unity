// =============================================================================
// LeaderboardPanel — toggleable leaderboard + player-profile modal (WO-129).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// WO-F conversion (2026-07-03, coverage matrix row #50): UIDocument/UITK card ->
// code-built uGUI on the Obsidian master frame (BuildObsidianModal: FrameCore +
// medallion + the ONE shared Close + scrim), per the HelpMenu reference recipe.
// Opens via Toggle() (the kit dock). Strict MVVM (Silo E): binds a LeaderboardVM
// and reads vm.* only — all LeaderboardService access + the async fetch live in the VM.
//
// Layout (in the frame's body well):
//   • Profile strip — "You", invite code, best wave / crystals / arena W-L.
//   • Metric tabs — Best Wave / Crystals / Arena Wins (re-fetch on tap;
//     active tab = Yellow Obsidian button).
//   • Ranked scroll list — rank, name, score; the local player's row is gold.
//   • Footer — an HONEST source badge ("Local (offline)…") when the source is
//     the stub, so we never pretend the (undeployed) backend is live.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Services;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Social;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class LeaderboardPanel : MonoBehaviour
    {
        private ElarionUiKit.ObsidianModal _modal;
        private Transform _profileHost;
        private Transform _tabHost;
        private Transform _listContent;   // ScrollRect content (VerticalLayoutGroup)
        private ScrollRect _listScroll;
        private TextMeshProUGUI _footer;

        private bool _visible;
        private PanelHandle _panelHandle;

        // Strict MVVM (Silo E): ALL leaderboard state + the async fetch live in the VM;
        // this View reads vm.* only and never touches LeaderboardService.
        private LeaderboardVM _vm;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Leaderboard", () => SetVisible(false), () => _visible);
        }

        private void OnDestroy()
        {
            if (_vm != null) _vm.Changed -= Render;
            _vm?.Dispose();
            _vm = null;
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        public void Toggle() => SetVisible(!_visible);

        private void SetVisible(bool on)
        {
            if (on)
            {
                FlowTrace.Step("Leaderboard", "SetVisible(true) — opening leaderboard panel.");
                EnsureBuilt();
            }
            if (_modal == null || _modal.canvas == null) { _visible = false; return; }
            _visible = on;
            _modal.canvas.SetActive(on);
            if (on)
            {
                if (!PanelManager.NotifyOpened(_panelHandle))
                {
                    _visible = false;
                    _modal.canvas.SetActive(false);   // battle-lock reject — never force-show
                    return;
                }
                _vm?.Refresh();   // re-pull profile + rows (raises Changed -> Render)
                Render();
            }
            else
            {
                PanelManager.NotifyClosed(_panelHandle);
            }
        }

        // ── UI construction (kit modal, lazy on first open) ─────────────────
        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;

            _modal = ElarionUiKit.BuildObsidianModal("LeaderboardUI",
                new LocalizedText("common.leaderboard").Resolve(),
                new Vector2(0.26f, 0.10f), new Vector2(0.74f, 0.92f), () => SetVisible(false),
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "combat");

            // VM FIRST — it resolves LeaderboardService itself + owns the async fetch.
            _vm = LeaderboardVM.CreateDefault(() => SetVisible(false));
            _vm.Changed += Render;

            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? (Transform)_modal.chrome.layout.body
                : _modal.chrome.content.transform;

            // ── PX BAND LADDER (UI audit 2026-08-01, F1) ────────────────────────────
            // WAS: every band was a FRACTION of the body (ProfileStrip 0.86-1.00, TabRail
            // 0.76-0.85, ListScroll 0.08-0.75, Footer 0.00-0.07) and the tab buttons were
            // anchored 0.05-0.95 INSIDE the 0.09-tall rail. Worked arithmetic:
            //   panel      = (0.92 - 0.10)            = 0.82 of the modal canvas height
            //   FrameCore body, after the WO-714 P6 close-band reservation, resolves to
            //   ~363 canvas-local px on the landscape Seeker canvas (~842 px portrait)
            //   rail       = 0.09 x 363              ~= 33 px
            //   tab button = 0.90 x 33               ~= 29 px
            // BuildObsidianButton ends in ClampMinTouch (ElarionUiKitObsidian.cs:650,:685),
            // which grows any sub-floor button to MinTouchPx = 112 px (ElarionUiKit.cs:317)
            // SYMMETRICALLY ABOUT ITS CENTRE (ElarionUiKit.cs:979-988) -- so ~+41 px ABOVE
            // and ~+41 px BELOW the rail. The tabs punched up through the ProfileStrip and
            // down into the ListScroll. Same bug class the settings px-ladder fixed
            // (SettingsController.cs:225,:236 Frac(120f)).
            //
            // NOW: fixed REFERENCE-PIXEL rungs. offsetMin/offsetMax on a CanvasScaler'd
            // canvas are canvas-local units == reference px -- the SAME unit MinTouchPx is
            // measured in -- so these rungs hold at every screen size with no scaler math.
            // Ladder (top -> bottom of the body):
            //   ProfileStrip 68 | gap 8 | TabRail 120 | gap 8 | ListScroll (flex) | gap 8 | Footer 30
            //   fixed total = 68+8+120+8+8+30 = 242 px; the list takes the remainder
            //   (~121 px landscape / ~600 px portrait) and scrolls.
            // The 120 px rail is >= the 112 px floor, so ClampMinTouch is a NO-OP on the tabs
            // and neither neighbouring band can be encroached.
            const float ProfileH = 68f, TabRailH = 120f, FooterH = 30f, Gap = 8f;
            const float TabRailTop = ProfileH + Gap;                 // 76
            const float ListTop    = TabRailTop + TabRailH + Gap;    // 204
            const float ListBottom = FooterH + Gap;                  // 38

            // Profile strip (top of the well).
            _profileHost = PxBandFromTop(body, "ProfileStrip", 0.03f, 0.97f, 0f, ProfileH);
            // Metric tabs.
            _tabHost = PxBandFromTop(body, "TabRail", 0.03f, 0.97f, TabRailTop, TabRailH);
            BuildTabs();
            // Ranked scroll list.
            BuildList(body, ListTop, ListBottom);
            // Footer (source honesty badge).
            var footHost = PxBandFromBottom(body, "Footer", 0.03f, 0.97f, 0f, FooterH);
            _footer = MakeText(footHost, "", 12, ElarionUi.ParchmentDim, FontStyles.Italic,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

            _modal.canvas.SetActive(false);   // built hidden; SetVisible shows it
        }

        private void BuildTabs()
        {
            // Rebuilt whole on metric switch so the active tab re-colors (Yellow = active).
            for (int i = _tabHost.childCount - 1; i >= 0; i--)
                Destroy(_tabHost.GetChild(i).gameObject);

            AddTab(LeaderboardMetric.BestWave,  "Best Wave",  0);
            AddTab(LeaderboardMetric.Crystals,  "Crystals",   1);
            AddTab(LeaderboardMetric.ArenaWins, "Arena Wins", 2);
        }

        private void AddTab(LeaderboardMetric metric, string label, int index)
        {
            float x0 = 0.005f + index * (1f / 3f);
            float x1 = x0 + (1f / 3f) - 0.01f;
            // FULL rail height (0..1 = the 120 px rung), NOT the old 0.05-0.95 inset: 120 px is
            // already above MinTouchPx (112), so ClampMinTouch never inflates the button out of
            // its band. Width stays a fraction -- a third of a ~860 px rail is ~279 px, far above
            // the floor, so the horizontal clamp is a no-op too.
            ElarionUiKit.BuildObsidianButton(_tabHost, label,
                ElarionUiKit.ObsidianButtonStyle.Style1,
                (_vm != null && metric == _vm.Metric) ? ElarionUiKit.ObsidianButtonColor.Yellow
                                                      : ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(x0, 0f), new Vector2(x1, 1f),
                () => SelectMetric(metric));
        }

        private void BuildList(Transform body, float topInsetPx, float bottomInsetPx)
        {
            // ScrollRect + masked viewport + a VerticalLayoutGroup content column
            // (same inline composition as ElarionUiKitDemo — the kit ships no scroll
            // widget yet; kit ask logged in the matrix).
            // Stretches between the px ladder's top/bottom insets (see EnsureBuilt), so it
            // always starts BELOW the 120 px tab rail and ends ABOVE the footer band.
            var scrollHost = PxStretchBand(body, "ListScroll", 0.03f, 0.97f, topInsetPx, bottomInsetPx);
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D), typeof(Image));
            scrollGo.transform.SetParent(scrollHost, false);
            var srt = scrollGo.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = Vector2.one;
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            _listScroll = scroll;

            _listContent = contentGo.transform;
        }

        // ── Repaint ──────────────────────────────────────────────────────────

        private void SelectMetric(LeaderboardMetric metric)
        {
            _vm?.SelectMetric(metric);   // VM re-fetches + raises Changed -> Render
        }

        // Repaints purely from vm.* — no LeaderboardService reads (strict MVVM, Silo E).
        private void Render()
        {
            if (_modal == null || !_visible || _vm == null) return;

            BuildTabs();        // re-color the active tab from vm.Metric
            RebuildProfile();
            RebuildRows();
            if (_footer != null) _footer.text = _vm.FooterText;
        }

        private void RebuildProfile()
        {
            for (int i = _profileHost.childCount - 1; i >= 0; i--)
                Destroy(_profileHost.GetChild(i).gameObject);
            if (_vm == null || string.IsNullOrEmpty(_vm.ProfileHeroLine)) return;

            MakeText(_profileHost, _vm.ProfileHeroLine, 18, ElarionUi.Gilt, FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
            MakeText(_profileHost, _vm.ProfileStatsLine,
                14, ElarionUi.Parchment, FontStyles.Normal,
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(1f, 0.5f));
        }

        private void RebuildRows()
        {
            if (_listContent == null || _vm == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            foreach (var row in _vm.Rows)
                MakeRow(row);
        }

        private void MakeRow(LeaderboardVM.Row row)
        {
            var rowGo = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(_listContent, false);
            // Visit rows reserve a full kit touch rung. BuildObsidianButton clamps to 112 px;
            // anything shorter would inflate across neighboring leaderboard rows on Seeker.
            rowGo.GetComponent<LayoutElement>().preferredHeight = row.CanVisit ? 120f : 48f;
            var bg = rowGo.GetComponent<Image>();
            bg.color = row.IsLocal
                ? new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.16f)
                : ((row.Index & 1) == 1 ? new Color(1f, 1f, 1f, 0.04f) : Color.clear);

            Color fg = row.IsLocal ? ElarionUi.Gilt : ElarionUi.Parchment;
            var style = row.IsLocal ? FontStyles.Bold : FontStyles.Normal;
            MakeText(rowGo.transform, row.Rank, 14, row.IsLocal ? ElarionUi.Gilt : ElarionUi.ParchmentDim,
                FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(0.10f, 1f));
            MakeText(rowGo.transform, row.Name, 14, fg, style,
                TextAlignmentOptions.Left, new Vector2(0.11f, 0f), new Vector2(row.CanVisit ? 0.60f : 0.78f, 1f));
            MakeText(rowGo.transform, row.Score, 14, row.IsLocal ? ElarionUi.Gilt : ElarionUi.Aether,
                FontStyles.Bold, TextAlignmentOptions.Right, new Vector2(row.CanVisit ? 0.58f : 0.78f, 0f),
                new Vector2(row.CanVisit ? 0.73f : 1f, 1f));
            if (row.CanVisit)
            {
                var visit = ElarionUiKit.BuildObsidianButton(rowGo.transform,
                    new LocalizedText("hud.leaderboard.visit_town").Resolve(),
                    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                    new Vector2(.74f, .03f), new Vector2(.995f, .97f), () => OpenTown(row));
                visit.name = "VisitTown_" + row.Rank;
            }
        }

        private void OpenTown(LeaderboardVM.Row row)
        {
            if (_vm == null || !row.CanVisit) return;
            float anchor = _listScroll != null ? _listScroll.verticalNormalizedPosition : 1f;
            var visitPanel = GetComponent<TownShowcaseVisitPanel>();
            if (visitPanel == null) visitPanel = gameObject.AddComponent<TownShowcaseVisitPanel>();
            visitPanel.Open(_vm.VisitEntries, row.ShowcaseId, row.Index, anchor, () =>
            {
                // Opening the showcase correctly claims the global modal slot, which closes
                // this panel. Re-open it through the same arbiter before restoring the exact
                // captured anchor; otherwise Return would reveal gameplay, not the Top 10 row.
                SetVisible(true);
                if (_listScroll != null)
                {
                    Canvas.ForceUpdateCanvases();
                    _listScroll.verticalNormalizedPosition = anchor;
                }
            });
        }

        // ── uGUI helpers ─────────────────────────────────────────────────────

        // ── FIXED-REFERENCE-PIXEL BANDS (UI audit 2026-08-01, F1) ────────────────────
        // The anti-overlap primitive. A band's HEIGHT is set in canvas-local units via
        // offsetMin/offsetMax; on a CanvasScaler'd canvas those units ARE reference px --
        // the same unit ElarionUiKit.MinTouchPx (112) is expressed in. So a 120 px rung is
        // provably above the touch floor at every resolution and ClampMinTouch can never
        // grow a button out of its band into a neighbour. (Same principle as
        // SettingsController's Frac(px) ladder, without needing to replicate the scaler
        // math -- offsets are already in the target unit.) x stays fractional: horizontal
        // room is never the constraint on these rails.

        /// <summary>Band pinned to the TOP of <paramref name="parent"/>: <paramref name="topPx"/>
        /// down from the top edge, <paramref name="heightPx"/> tall (reference px).</summary>
        private static Transform PxBandFromTop(Transform parent, string name,
            float xMin, float xMax, float topPx, float heightPx)
        {
            var rt = NewBand(parent, name);
            rt.anchorMin = new Vector2(xMin, 1f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMax = new Vector2(0f, -topPx);
            rt.offsetMin = new Vector2(0f, -(topPx + heightPx));
            return rt.transform;
        }

        /// <summary>Band pinned to the BOTTOM of <paramref name="parent"/>: <paramref name="bottomPx"/>
        /// up from the bottom edge, <paramref name="heightPx"/> tall (reference px).</summary>
        private static Transform PxBandFromBottom(Transform parent, string name,
            float xMin, float xMax, float bottomPx, float heightPx)
        {
            var rt = NewBand(parent, name);
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, bottomPx);
            rt.offsetMax = new Vector2(0f, bottomPx + heightPx);
            return rt.transform;
        }

        /// <summary>Band that STRETCHES the parent's full height minus fixed px insets top and
        /// bottom -- it absorbs whatever the fixed rungs leave over.</summary>
        private static Transform PxStretchBand(Transform parent, string name,
            float xMin, float xMax, float topInsetPx, float bottomInsetPx)
        {
            var rt = NewBand(parent, name);
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, bottomInsetPx);
            rt.offsetMax = new Vector2(0f, -topInsetPx);
            return rt.transform;
        }

        private static RectTransform NewBand(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        // (the old fraction-anchored ZoneRect helper is gone -- every band on this panel is
        //  now a fixed reference-px rung; see the PxBand* helpers above.)

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
