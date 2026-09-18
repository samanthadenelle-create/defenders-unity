// =============================================================================
// MusicSelectionPanel — the player music-selection jukebox UI (WO-162).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Audio   Namespace: DeNelle.Audio
//
// The audio ENGINE for this feature already exists on AudioService:
//   • AudioService.AmbientChoicesFor(context)  — the curated, authorable track set
//   • AudioService.GetAmbientChoice(context)    — the player's persisted pick
//   • AudioService.SetAmbientChoice(context, t) — persist + (if in context) play
//   • AudioService.PlayAmbientContext / IsStateCue — combat-state music OVERRIDES
//     the jukebox pick, returning to it afterwards.
// This file is JUST the selection UI on top of that — no new audio system
// (CLAUDE.md §5/§9, WO-162 constraint).
//
// WO-F conversion (2026-07-03, coverage matrix row #46): UIDocument/UITK card ->
// code-built uGUI on the Obsidian master frame (BuildObsidianModal: FrameCore +
// medallion + the ONE shared Close + tap-outside scrim), per the HelpMenu
// reference recipe. Track rows are Obsidian buttons rebuilt each open so a
// context change (village -> overworld) shows the right set + checkmark.
// Toggle()/Open() stay public — the HUD kit's dock reaches them by reflection.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;   // ScrollRect/RectMask2D/layout: the WO-795 scroll-list pattern
using DeNelle.Core.UI;   // shared Elarion kit — jukebox matches the one game UI

namespace DeNelle.Audio
{
    /// <summary>
    /// A small jukebox panel: lists the curated ambient tracks for the current
    /// context (Village vs Overworld), shows which one is selected, and lets the
    /// player pick one. The pick persists and plays via the existing AudioService
    /// ambient-context path; combat/victory/defeat music still overrides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicSelectionPanel : MonoBehaviour
    {
        /// <summary>Key that toggles the jukebox panel open/closed.</summary>
        public const KeyCode ToggleKey = KeyCode.J;

        /// <summary>WO-563: public open/toggle so a reachable HUD button (the kit dock) can
        /// surface the jukebox on touch — the J key is keyboard-only + gated behind DevHotkeys.</summary>
        public void Toggle() => SetOpen(!_open);
        public void Open()   => SetOpen(true);

        private ElarionUiKit.ObsidianModal _modal;
        private Transform _rowHost;   // ScrollRect Content; rows rebuilt into it each open
        private ScrollRect _scroll;   // the track-list scroller (snap to top on each rebuild)
        private bool _open;

        // One track row in reference px (1080x1920 canvas). MinTouchPx keeps the tap
        // target legal; it is within a hair of the old 0.115-fraction row height.
        private const float RowPx = ElarionUiKit.MinTouchPx;

        // Strict MVVM (Silo E): ALL selection state/logic lives in the VM; this View
        // reads vm.* only and never touches AudioService.
        private JukeboxVM _vm;

        // PanelManager mutual-exclusion handle (one panel at a time).
        private DeNelle.Core.UI.PanelHandle _panelHandle;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Jukebox", () => SetOpen(false), () => _open);
        }

        private void OnDestroy()
        {
            if (_vm != null) _vm.Changed -= RebuildRows;
            _vm?.Dispose();
            _vm = null;
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        private void Update()
        {
            // WO-437: the global 'J' open is gated behind the global DevHotkeys
            // kill-switch (default OFF) — and blocked during battle. ESC-close stays
            // (only acts when this panel is already open).
            if (DeNelle.Core.FeatureFlags.DevHotkeys
                && Input.GetKeyDown(ToggleKey)
                && !DeNelle.Core.Combat.BattleLock.IsInBattle())
                SetOpen(!_open);

            if (_open && Input.GetKeyDown(KeyCode.Escape))
                SetOpen(false);
        }

        // ── UI construction (kit modal, lazy on first open) ─────────────────
        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;

            // WO-1857: "JukeboxUI" is the canvas host name; the title reuses the shared
            // common.jukebox row (the registry names this call site).
            _modal = ElarionUiKit.BuildObsidianModal("JukeboxUI",
                new LocalizedText("common.jukebox").Resolve(),
                new Vector2(0.30f, 0.14f), new Vector2(0.70f, 0.86f), () => SetOpen(false),
                frameName: RpgUiCatalog.FrameCore, medallionIcon: "music");

            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? _modal.chrome.layout.body.transform
                : _modal.chrome.content.transform;

            // 16-PANEL AUDIT (WO-795 pattern): the caption gets its OWN single-line band
            // (NoWrap + Ellipsis via FitSingleLine) below the header trim, and the track
            // list band starts strictly BELOW it, so the two can never collide again.
            var subtitle = ElarionUiKit.Label(body,
                new LocalizedText("audio.jukebox.subtitle").Resolve(),
                0.885f, 0.965f, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                TMPro.TextAlignmentOptions.Center, 0.04f, 0.96f);
            ElarionUiKit.FitSingleLine(subtitle, 24f, ElarionUi.FontLabel);

            // WO-795 scroll list (RumorBoardPanel recipe): Viewport (near-invisible Image
            // drag-catcher + RectMask2D) on the list band; Content = top-anchored
            // VerticalLayoutGroup + ContentSizeFitter. Rows are LayoutElement children,
            // so EVERY track lists and scrolls: no fraction math, no overlap, no cut.
            var viewportGo = new GameObject("TrackViewport", typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportGo.transform.SetParent(body, false);
            var vpr = viewportGo.GetComponent<RectTransform>();
            vpr.anchorMin = new Vector2(0.04f, 0.02f);
            vpr.anchorMax = new Vector2(0.96f, 0.87f);
            vpr.offsetMin = Vector2.zero; vpr.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // drag catcher

            var contentGo = new GameObject("TrackRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var cr = contentGo.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot     = new Vector2(0.5f, 1f);
            cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.childControlWidth  = true; vlg.childForceExpandWidth  = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            vlg.spacing = 8f;
            // Bottom pad = one row so the last track scrolls fully clear of the mask.
            vlg.padding = new RectOffset(6, 6, 6, (int)RowPx + 8);
            var csf = contentGo.GetComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _scroll = viewportGo.GetComponent<ScrollRect>();
            _scroll.viewport = vpr;
            _scroll.content  = cr;
            _scroll.horizontal = false;
            _scroll.vertical   = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 25f;

            _rowHost = contentGo.transform;

            // VM FIRST — it resolves AudioService itself, so this View never touches a service.
            _vm = JukeboxVM.CreateDefault(() => SetOpen(false));
            _vm.Changed += RebuildRows;

            _modal.canvas.SetActive(false);   // built hidden; SetOpen shows it
        }

        // Rebuilds the track rows for the player's CURRENT ambient context, with a
        // checkmark on the selected one. Called every open so the set + selection
        // are always current.
        // Renders purely from vm.* — no AudioService reads (strict MVVM, Silo E).
        private void RebuildRows()
        {
            if (_rowHost == null) return;
            for (int i = _rowHost.childCount - 1; i >= 0; i--)
                Destroy(_rowHost.GetChild(i).gameObject);

            if (_vm == null || !_vm.AudioReady)
            {
                var notReady = ElarionUiKit.Label(_rowHost,
                    new LocalizedText("audio.jukebox.not_ready").Resolve(), 0f, 1f,
                    ElarionUi.Danger, ElarionUi.FontBody,
                    TMPro.TextAlignmentOptions.Center, 0.05f, 0.95f);
                // _rowHost is layout-driven now; give the notice a row-sized slot.
                notReady.gameObject.AddComponent<LayoutElement>().preferredHeight = RowPx;
                return;
            }

            foreach (var row in _vm.Tracks)
            {
                MusicTrack track = row.Track;
                bool isSelected = row.IsSelected;
                // Fixed-height layout slot per track; the VerticalLayoutGroup stacks them
                // and the ScrollRect scrolls them; every track lists, none ever overlaps.
                var host = new GameObject("Row_" + track, typeof(RectTransform), typeof(LayoutElement));
                host.transform.SetParent(_rowHost, false);
                var le = host.GetComponent<LayoutElement>();
                le.preferredHeight = RowPx;
                le.minHeight = RowPx;
                // Selected row = Green family (the pack's own affirmative), rest Gray.
                // ASCII '>' marker (eyes-on 2026-07-03: the checkmark glyph is missing from the TMP font).
                ElarionUiKit.BuildObsidianButton(host.transform,
                    (isSelected ? ">  " : "") + row.DisplayName,
                    ElarionUiKit.ObsidianButtonStyle.Style1,
                    isSelected ? ElarionUiKit.ObsidianButtonColor.Green
                               : ElarionUiKit.ObsidianButtonColor.Gray,
                    Vector2.zero, Vector2.one,
                    () => OnPick(track));
            }

            // Fresh render starts at the top of the list.
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        // Selecting a track routes to the VM command (persists + previews); the VM
        // raises Changed, which re-renders the checkmark via RebuildRows.
        private void OnPick(MusicTrack track)
        {
            _vm?.SetAmbientChoice(track);
        }

        private void SetOpen(bool open)
        {
            if (open) EnsureBuilt();
            if (_modal == null || _modal.canvas == null) { _open = false; return; }
            _open = open;
            _modal.canvas.SetActive(open);
            if (open)
            {
                // Announce open: closes any previously-open panel. Battle-lock may reject
                // (NotifyOpened==false) — revert and stay hidden, never force-show.
                if (!PanelManager.NotifyOpened(_panelHandle))
                {
                    _open = false;
                    _modal.canvas.SetActive(false);
                    return;
                }
                _vm?.Rebuild();   // refresh context + selection on open (raises Changed -> RebuildRows)
            }
            else
            {
                PanelManager.NotifyClosed(_panelHandle);
            }
        }
    }
}
