// =============================================================================
// QuestTrackerHud — far-RIGHT HUD quest ICON that opens the Rumor Board.
// -----------------------------------------------------------------------------
// OWNER RULING 2026-07-06: "the on screen quest board should be minimized to just
// an icon on screen, and open to the panel on click." The old always-expanded
// tracker card (QUEST eyebrow + title + objective + "tap to open board") is GONE
// from the HUD — the board panel is now the reading surface. This widget is a
// single ~52px kit-dressed icon button at the same right-edge anchor; a subtle
// gold dot (luminance cue, not hue-meaning — owner is colorblind) marks that the
// tracked quest changed/updated since the player last opened the board.
//
// CANON-CORRECT (CLAUDE.md §8): code-built uGUI (Canvas + Image + TMP), NOT a
// UIDocument. The prior UIDocument version did not render (trace: active=False /
// hasRoot=False / rootResolved=0x0) — UIDocument HUDs are unreliable in this project.
// Builds its OWN ScreenSpaceOverlay Canvas (no PanelSettings/theme dependency).
//
// STRICT MVVM (Silo E, 2026-07-17): the tracked-quest RESOLUTION (type-aware WO-454
// fallback) now lives in a QuestTrackerVM; this View binds it and reads vm.* only
// (HasTrackedQuest + UpdateSnapshot), repainting on vm.Changed. It still hides while
// any modal is open and when no quest is active. Click → PanelRouter.Open(
// PanelId.RumorBoard), the exact route the old "tap to open board" card used.
// Spawned by QuestTrackerHudBootstrap once a scene has a hero.
// =============================================================================

using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class QuestTrackerHud : MonoBehaviour
    {
        private GameObject _ui;
        private RectTransform _card;        // the icon-button medallion (visibility toggled on repaint)
        private GameObject _attentionDot;   // subtle gold update cue (luminance, not hue-meaning)
        private string _lastSnapshot;       // tracked-id|objective — detects quest updates for the dot
        private bool _painted;              // first Repaint never arms the dot

        // Strict MVVM (Silo E): the tracked-quest RESOLUTION lives in the VM; this View
        // reads vm.* only and never touches QuestService / QuestCatalog.
        private QuestTrackerVM _vm;

        private void OnEnable()
        {
            Build();
            if (_vm != null) _vm.Changed += Repaint;
            // MODAL DISCIPLINE (eyes-on pass 2026-07-03: the tracker card overlapped the open
            // Gear Shop / Talents frames in the bot captures — this widget predates the kit's
            // hud-areas occupancy, so it must observe the arbiter itself): hide while any
            // modal is open, exactly like the kit's `modal` posture stands the HUD down.
            PanelManager.OpenStateChanged += SyncModalVisibility;
            SyncModalVisibility();
        }

        private void OnDisable()
        {
            if (_vm != null) _vm.Changed -= Repaint;
            PanelManager.OpenStateChanged -= SyncModalVisibility;
        }

        private void SyncModalVisibility()
        {
            if (_ui != null) _ui.SetActive(!PanelManager.AnyOpen);
        }

        private void OnDestroy()
        {
            if (_vm != null) _vm.Changed -= Repaint;
            _vm?.Dispose();
            _vm = null;
            if (_ui != null) Destroy(_ui);
        }

        // ── Build the uGUI canvas + the right-side card ──────────────────────────
        private void Build()
        {
            if (_ui != null) return;

            // VM FIRST — it resolves QuestService + QuestCatalog itself and owns the
            // WO-454 tracked-quest resolution, so this View never touches a service.
            _vm = QuestTrackerVM.CreateDefault(null);

            _ui = new GameObject("QuestTrackerHudUI");
            _ui.transform.SetParent(transform, false);

            var canvas = _ui.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80; // above wave timer, below modals (matches daily chips band)

            var scaler = _ui.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            _ui.AddComponent<GraphicRaycaster>();

            // OWNER 2026-07-06: minimized to a single ICON at the same right-edge anchor
            // (centre of the old card's 0.34–0.58 band), ~52px kit-dressed medallion, CLICKABLE.
            var card = new GameObject("TrackerIcon", typeof(Image), typeof(Button));
            card.transform.SetParent(_ui.transform, false);
            _card = card.GetComponent<RectTransform>();
            _card.anchorMin = new Vector2(1f, 0.46f);
            _card.anchorMax = new Vector2(1f, 0.46f);
            _card.pivot = new Vector2(1f, 0.5f);
            _card.sizeDelta = new Vector2(52f, 52f);
            // SHARED RIGHT GUTTER (2026-08-05): every right-rail element - the Echoes chip,
            // the Builders chip, the Resources chip - sits ElarionUi.PadPanel * 3f from the
            // screen edge. This medallion was authored at -10f, so on the Seeker's 2670x1200
            // it landed ~12 device px from the edge and read as running OFF-SCREEN against a
            // rounded corner, while its neighbours sat ~67px in. Four different right edges on
            // one rail is the defect; the number is now derived from the same constant they
            // all use, so it cannot drift apart again.
            _card.anchoredPosition = new Vector2(-(ElarionUi.PadPanel * 3f), 0f);
            // Kit-dressed plate: Obsidian action-slot sprite; translucent stone square fallback.
            var plateImg = card.GetComponent<Image>();
            var plate = RpgUiCatalog.Get(RpgUiCatalog.RoleSlot, RpgUiCatalog.SlotAction);
            if (plate != null)
            {
                plateImg.sprite = plate;
                plateImg.color = Color.white;
            }
            else
            {
                var bg = ElarionUi.PanelStoneDark;
                plateImg.color = new Color(bg.r, bg.g, bg.b, 0.72f);
            }
            card.GetComponent<Button>().onClick.AddListener(OpenBoard); // click → pop the board

            // Quest concept icon (scroll/map glyph). Sprite from the catalog; if the pack is
            // not imported, a procedural gold ◈ glyph stands in — NEVER blank.
            var iconSprite = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconQuest);
            if (iconSprite != null)
            {
                var icon = new GameObject("Icon", typeof(Image));
                icon.transform.SetParent(_card, false);
                var ir = icon.GetComponent<RectTransform>();
                ir.anchorMin = new Vector2(0.5f, 0.5f);
                ir.anchorMax = new Vector2(0.5f, 0.5f);
                ir.sizeDelta = new Vector2(34f, 34f);
                var ii = icon.GetComponent<Image>();
                ii.sprite = iconSprite;
                ii.preserveAspect = true;
                ii.raycastTarget = false; // let the medallion's Button receive the click
            }
            else
            {
                var glyph = new GameObject("IconGlyph", typeof(TMPro.TextMeshProUGUI));
                glyph.transform.SetParent(_card, false);
                var gr = glyph.GetComponent<RectTransform>();
                gr.anchorMin = Vector2.zero;
                gr.anchorMax = Vector2.one;
                gr.offsetMin = Vector2.zero;
                gr.offsetMax = Vector2.zero;
                var gt = glyph.GetComponent<TMPro.TextMeshProUGUI>();
                ElarionUiKit.EnsureFont(gt); // font-safe before .text (TMP GenerateTextMesh NRE)
                gt.text = "!"; // ASCII quest marker (RPG "!" convention; build-font-safe, no glyph tofu)
                // WO-1826: was 26, under ElarionUi.FontFloorMobile (owner: "do it, apply the 30
                // floor to the hud"). This is the icon-less fallback marker, so it IS the whole
                // medallion's content when the sprite is missing. Seat proof: the glyph rect
                // stretches the full _card, which is 52 x 52 ref px (:107), and a 30 px line needs
                // 36. One glyph, so there is no width risk.
                gt.fontSize = ElarionUi.FontFloorMobile;
                gt.color = ElarionUi.Gilt;
                gt.alignment = TMPro.TextAlignmentOptions.Center;
                gt.raycastTarget = false;
            }

            // Attention dot: small gold ● top-right of the medallion, shown only when the
            // tracked quest changes/updates after the first paint; cleared on open.
            // IMAGE dot, not a TMP glyph — U+25CF is unverified in the project font and a
            // tofu box here would violate the no-tofu law (the star-tofu lesson). A rotated
            // gold Image quad reads as a diamond stud; sprite-free, glyph-proof.
            var dot = new GameObject("AttentionDot", typeof(UnityEngine.UI.Image));
            dot.transform.SetParent(_card, false);
            var dr = dot.GetComponent<RectTransform>();
            dr.anchorMin = new Vector2(1f, 1f);
            dr.anchorMax = new Vector2(1f, 1f);
            dr.pivot = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(10f, 10f);
            dr.anchoredPosition = new Vector2(-4f, -4f);
            dr.localEulerAngles = new Vector3(0f, 0f, 45f);
            var di = dot.GetComponent<UnityEngine.UI.Image>();
            di.color = ElarionUi.Gilt;
            di.raycastTarget = false;
            _attentionDot = dot;
            _attentionDot.SetActive(false);

            Repaint();
        }

        private void OpenBoard()
        {
            if (_attentionDot != null) _attentionDot.SetActive(false); // the board is the reading surface — cue served
            if (!PanelRouter.Open(PanelId.RumorBoard))
                FlowTrace.Warn("HUD", "QuestTracker: RumorBoard opener not registered — cannot pop the board.");
        }

        // ── Repaint = visibility + update-cue only (owner 2026-07-06: the icon carries
        //    no text; the Rumor Board panel is the reading surface). Renders from vm.*
        //    ONLY — the WO-454 tracked-quest resolution lives in the VM (strict MVVM). ──
        private void Repaint()
        {
            if (_card == null || _vm == null) return;

            if (!_vm.HasTrackedQuest) { _card.gameObject.SetActive(false); return; }

            _card.gameObject.SetActive(true);

            // Update cue: the tracked quest id or its current objective changed since the
            // last paint → light the gold dot. First paint never arms it (session start
            // is not "new"). Cleared in OpenBoard when the player reads the board.
            string snapshot = _vm.UpdateSnapshot;
            if (_painted && _attentionDot != null && snapshot != _lastSnapshot)
                _attentionDot.SetActive(true);
            _lastSnapshot = snapshot;
            _painted = true;
        }
    }
}
