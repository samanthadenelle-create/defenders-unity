// =============================================================================
// DailyQuestHud — today's daily quests on the pack QUESTS reference (WO-714 W3).
// Spawned by DailyQuestHudBootstrap once a scene has a hero (so it does NOT
// appear on Title / HeroSelect).
// -----------------------------------------------------------------------------
// WO-714 Phase 2 W3 conformance (2026-07-13): the compact FrameCore card ->
// the pack QUESTS reference grammar (Quest_Log_Panel / FrameQuest master-detail,
// the owner-ratified FrameCrafting template shape):
//   * bodyLeft  (dark well)      = quest rows (kit Obsidian buttons, selected =
//                                  Yellow; right-aligned readable state
//                                  "+ Done" / "2 / 5" — counts carry the state,
//                                  color is reinforcement only, colorblind law)
//   * bodyRight (parchment well) = the WO-693 shared COMPACT DETAIL CARD
//                                  (ElarionUiKit.BuildParchmentDetailCard:
//                                  name + slot flavor -> REWARDS -> PROGRESS).
//                                  No CTA — rewards auto-dispense on completion
//                                  (DEF-223 reward bridge), there is no claim.
// The ONE shared Close is the chrome's; the frame supplies ALL chrome (no
// per-screen plates/rims/fills — the old BuildRow/BgFor/RimFor grammar is gone).
//
// WO-795 (2026-08-01): the quest list is a ScrollRect well (Viewport drag-catcher +
// RectMask2D; top-anchored Content = VerticalLayoutGroup + ContentSizeFitter, bottom
// pad one row); rows are fixed-height LayoutElement hosts (MinTouchPx). The old
// anchored-fraction rows + truncation break; are gone — a deep quest log scrolls.
//
// STRICT MVVM (Silo E, 2026-07-17): this View binds a DailyQuestVM and DISPLAYS
// vm.* only — it owns NO quest data or logic. The quest set, row SELECTION, and
// reward lookups all live in the VM (which subscribes to the service's SetChanged
// and raises Changed). View-side memory is presentation-only: _celebrated
// (completion toast fires exactly once per quest, owner WO-35).
//
// WO-879 (2026-08-05) — THE EMPTY STATE IS THE VM's, RENDERED ONCE. The 2026-08-04
// capture (Builds/ui-capture/DailyQuestHud_2340x1080.png) showed the same message
// TWICE in two mismatched chromes: an italic line in the dark list well AND a
// parchment detail card, with ~half the panel dead black. Root cause: the View both
// DECIDED emptiness and AUTHORED the copy, at two call sites. Now the VM produces
// ONE EmptyStateInfo (Active + Headline + Detail) and this View renders it in ONE
// place — the detail well, WIDENED across the whole body while empty (the dark list
// well is switched off, so there is no dead-black half and no second chrome). The
// former "Select a quest" prompt is gone too: it was the View re-deriving selection
// state, and the VM's invariant (a non-empty set always has a valid selection) makes
// it unreachable — vm.TryGetSelected owns that lookup now.
// Empty-state bands are FIXED reference pixels, never a fraction of the well.
//
// Toggled on-demand by the TOWN ACTIONS "Quests" button (WO-411, via Toggle());
// hidden while any modal is open (modal stand-down discipline).
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;
using TMPro;
using UnityEngine;
using UnityEngine.UI;   // ScrollRect/RectMask2D/layout: the WO-795 scroll-list pattern

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class DailyQuestHud : MonoBehaviour
    {
        private ElarionUiKit.PanelChrome _chrome;
        private GameObject _canvas;
        private Transform _listHost;     // bodyLeft — dark list well
        private Transform _detailHost;   // bodyRight — parchment detail well
        private Transform _rowHost;      // WO-795 ScrollRect Content; quest rows rebuilt into it
        private ScrollRect _scroll;      // the quest-list scroller (built once in EnsureBuilt)

        // WO-879: the two body wells as RectTransforms + their AS-BUILT anchors, so the
        // empty state can collapse the split into ONE well and restore it exactly.
        private RectTransform _listRt;
        private RectTransform _detailRt;
        private Vector2 _listAnchorMin, _listAnchorMax;
        private Vector2 _detailAnchorMin, _detailAnchorMax;
        private bool _collapsedForEmpty;

        // One quest row in reference px (1080x1920 canvas). MinTouchPx keeps the tap
        // target legal (WO-795: fixed-height layout rows replace anchored fractions).
        private const float RowPx = ElarionUiKit.MinTouchPx;

        // WO-879 empty-state bands — FIXED reference px on the 1080x1920 canvas, NEVER a
        // fraction of the well. A fraction band shrinks with the well and TMP culls the
        // line whole (the WO-841 / WO-852 failure class). Each band is at least one TMP
        // line box (~1.25em) at the font it renders, so neither line can ever be clipped.
        public const float EmptyHeadlinePx = 64f;   // >= FontBody  50 * 1.25 = 62.5
        public const float EmptyDetailPx   = 50f;   // >= FontLabel 40 * 1.25 = 50
        public const float EmptyGapPx      = 12f;
        // Quests we've already celebrated, so a toast fires only on the FIRST time
        // a quest reaches Completed (owner WO-35: "expected something on completion").
        private readonly HashSet<string> _celebrated = new HashSet<string>();
        private bool _initialized;
        private bool _visible;           // user toggle state (the card starts hidden)

        // Strict MVVM (Silo E): ALL quest state + selection + reward lookups live in the
        // VM; this View reads vm.* only and never touches DailyQuestService / DailyQuestCatalog.
        private DailyQuestVM _vm;

        public static DailyQuestHud Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            EnsureBuilt();
            if (_vm != null) _vm.Changed += Repaint;
            // MODAL DISCIPLINE (eyes-on pass 2026-07-03: the floating card must not
            // overlap open modal frames): hide while any modal is open, like the kit's
            // `modal` posture.
            PanelManager.OpenStateChanged += ApplyVisibility;
            Repaint();
        }

        private void OnDisable()
        {
            if (_vm != null) _vm.Changed -= Repaint;
            PanelManager.OpenStateChanged -= ApplyVisibility;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_vm != null) _vm.Changed -= Repaint;
            _vm?.Dispose();
            _vm = null;
            if (_canvas != null) Destroy(_canvas);
        }

        /// <summary>Show/hide the daily-quest panel — driven by the TOWN ACTIONS "Quests"
        /// button (WO-411: quests are on-demand, not a free-floating top-right panel).</summary>
        public void Toggle()
        {
            _visible = !_visible;
            ApplyVisibility();
            if (_visible) Repaint();
        }

        // Effective visibility = user toggled ON *and* no modal is open.
        private void ApplyVisibility()
        {
            if (_chrome == null || _chrome.root == null) return;
            _chrome.root.SetActive(_visible && !PanelManager.AnyOpen);
        }

        // ── UI construction (kit FrameQuest master-detail on its own overlay canvas) ──
        private void EnsureBuilt()
        {
            if (_canvas != null) return;

            // VM FIRST — it resolves DailyQuestService + DailyQuestCatalog itself, so this
            // View never touches a service; it owns the quest set, selection + reward lookups.
            _vm = DailyQuestVM.CreateDefault(() => { _visible = false; ApplyVisibility(); });

            _canvas = ElarionUiKit.BuildModalCanvas("DailyQuestHud", 80);
            var c = _canvas.GetComponent<Canvas>();
            if (c != null) c.overrideSorting = true;

            // The pack QUESTS reference: FrameQuest (Quest_Log_Panel) master-detail —
            // dark quest list LEFT, parchment detail RIGHT, medallion, ONE shared kit
            // Close (it hides the card, matching the Quests-button toggle). No backdrop
            // scrim — this stays a non-blocking HUD surface, not a PanelManager modal.
            _chrome = ElarionUiKit.BuildObsidianPanel(_canvas.transform, "Daily Quests",
                new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.88f),
                onClose: () => { _visible = false; ApplyVisibility(); },
                withBackdrop: false, frameName: RpgUiCatalog.FrameQuest, medallionIcon: "quest");
            MedievalUiSkin.ApplyShell(_chrome);

            // Drop-zones only (canon §4): split wells when the frame resolves; graceful
            // fallback zones on the procedural path (frame art absent — never blank).
            var layout = _chrome.layout;
            _listHost = layout != null && layout.bodyLeft != null
                ? (Transform)layout.bodyLeft
                : (layout != null && layout.body != null
                    ? (Transform)layout.body
                    : MakeZone(_chrome.content.transform, "ListWell",
                        new Vector2(0.05f, 0.24f), new Vector2(0.48f, 0.86f)));
            _detailHost = layout != null && layout.bodyRight != null
                ? (Transform)layout.bodyRight
                : MakeZone(_chrome.content.transform, "DetailWell",
                    new Vector2(0.52f, 0.24f), new Vector2(0.95f, 0.86f));

            // WO-879: remember the AS-BUILT well anchors (the kit's close-band reservation
            // may already have raised their floors) so the empty state can span the body and
            // the quest state can restore the split exactly — no re-derived fractions here.
            _listRt   = _listHost   as RectTransform;
            _detailRt = _detailHost as RectTransform;
            if (_listRt != null)   { _listAnchorMin   = _listRt.anchorMin;   _listAnchorMax   = _listRt.anchorMax; }
            if (_detailRt != null) { _detailAnchorMin = _detailRt.anchorMin; _detailAnchorMax = _detailRt.anchorMax; }

            // WO-795 scroll well (RumorBoardPanel recipe): Viewport (near-invisible Image
            // drag-catcher + RectMask2D) filling the list well; Content = top-anchored
            // VerticalLayoutGroup + ContentSizeFitter. Rows are fixed-height LayoutElement
            // hosts, so EVERY quest lists and scrolls: no fraction math, no truncation.
            var viewportGo = new GameObject("QuestViewport",
                typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportGo.transform.SetParent(_listHost, false);
            var vpr = viewportGo.GetComponent<RectTransform>();
            vpr.anchorMin = Vector2.zero;
            vpr.anchorMax = Vector2.one;
            vpr.offsetMin = Vector2.zero; vpr.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // drag catcher

            var contentGo = new GameObject("QuestRows",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
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
            // Bottom pad = one row so the last quest scrolls fully clear of the mask.
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

            _chrome.root.SetActive(false);   // built hidden; Toggle() shows it
        }

        // ── Repaint (renders from vm.* ONLY — strict MVVM, Silo E) ────────────────
        private void Repaint()
        {
            if (_listHost == null || _detailHost == null || _rowHost == null || _vm == null) return;
            ClearZone(_rowHost);
            ClearZone(_detailHost);

            var quests = _vm.Quests;

            // WO-879 — the VM owns the empty-state; the View asks ONCE and renders ONCE.
            // No emptiness test, no empty copy and no second call site live in this View.
            var empty = _vm.EmptyState;
            CollapseWellsForEmpty(empty.Active);

            if (empty.Active)
            {
                BuildEmptyState(empty);
            }
            else
            {
                // Quest rows (dark well, left) — the Jeweler/Crafting master-list grammar:
                // kit Obsidian buttons, selected = Yellow face, readable right-aligned state.
                // WO-795: fixed-height LayoutElement hosts inside the ScrollRect Content;
                // the VerticalLayoutGroup stacks them and the ScrollRect scrolls them —
                // every quest lists, none is ever truncated (the old break; is gone).
                for (int i = 0; i < quests.Count; i++)
                {
                    var item = quests[i];
                    string key = item.Id;
                    bool selected = key == _vm.SelectedId;
                    var host = new GameObject("Row_" + key,
                        typeof(RectTransform), typeof(LayoutElement));
                    host.transform.SetParent(_rowHost, false);
                    var le = host.GetComponent<LayoutElement>();
                    le.preferredHeight = RowPx;
                    le.minHeight = RowPx;
                    var rowBtn = ElarionUiKit.BuildObsidianButton(host.transform, item.Name,
                        ElarionUiKit.ObsidianButtonStyle.Style1,
                        selected ? ElarionUiKit.ObsidianButtonColor.Yellow
                                 : ElarionUiKit.ObsidianButtonColor.Gray,
                        Vector2.zero, Vector2.one,
                        () => _vm.Select(key));   // command -> VM raises Changed -> Repaint
                    // State text carries the state (colorblind law); color = reinforcement.
                    ElarionUiKit.AddRowStateSuffix(rowBtn,
                        item.Equipped ? "+ Done" : _vm.ProgressText(key),
                        item.Equipped ? ElarionUi.Affordable : ElarionUi.ParchmentDim);
                }

                // Detail (parchment well, right — the WO-693 shared compact card). The VM owns
                // the selection lookup (WO-879); the View no longer re-derives it, so there is
                // no second, View-authored empty state hiding in this branch.
                if (_vm.TryGetSelected(out var sel)) BuildDetail(sel);
            }

            // Fire a completion toast the first time each quest reaches Completed.
            // The first paint seeds _celebrated silently (so quests already done from a
            // prior session don't toast on load); after that, new completions pop a toast.
            foreach (var item in quests)
            {
                if (!item.Equipped) continue;
                string key = _vm.CelebrationKeyFor(item.Id);
                if (string.IsNullOrEmpty(key) || _celebrated.Contains(key)) continue;
                _celebrated.Add(key);
                if (_initialized) ShowCompletionToast(item.Name);
            }
            _initialized = true;
        }

        // ── WO-879 empty state: ONE panel, one chrome, fixed-pixel bands ──────────
        //
        // Layout only — every string comes from the VM's single EmptyStateInfo. The block
        // is a fixed-pixel stack centred in the (widened) parchment well: a headline band
        // and a detail band, each at least one TMP line box tall. Nothing here decides
        // WHETHER the panel is empty; Repaint asked the VM once.
        private void BuildEmptyState(DailyQuestVM.EmptyStateInfo empty)
        {
            if (_detailHost == null) return;

            var block = new GameObject("EmptyState",
                typeof(RectTransform), typeof(VerticalLayoutGroup));
            block.transform.SetParent(_detailHost, false);
            var brt = block.GetComponent<RectTransform>();
            // Centre-anchored, FIXED height = the sum of its bands (never a well fraction).
            brt.anchorMin = new Vector2(0.06f, 0.5f);
            brt.anchorMax = new Vector2(0.94f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            bool hasDetail = !string.IsNullOrEmpty(empty.Detail);
            brt.sizeDelta = new Vector2(0f,
                EmptyHeadlinePx + (hasDetail ? EmptyGapPx + EmptyDetailPx : 0f));

            var vlg = block.GetComponent<VerticalLayoutGroup>();
            vlg.childControlWidth  = true; vlg.childForceExpandWidth  = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.spacing = EmptyGapPx;

            var head = ElarionUiKit.Label(EmptyBand(block.transform, "EmptyHeadline", EmptyHeadlinePx),
                empty.Headline, 0f, 1f, ElarionUi.Gold, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0f, 1f, bold: true);
            ElarionUiKit.FitSingleLine(head, ElarionUi.FontFloorMobile);

            if (!hasDetail) return;
            var body = ElarionUiKit.Label(EmptyBand(block.transform, "EmptyDetail", EmptyDetailPx),
                empty.Detail, 0f, 1f, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                TextAlignmentOptions.Center, 0f, 1f);
            body.fontStyle = FontStyles.Italic;
            ElarionUiKit.FitSingleLine(body, ElarionUi.FontFloorMobile);
        }

        // A fixed-height band host inside the empty-state stack (px, not a fraction).
        private static Transform EmptyBand(Transform parent, string name, float px)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = px;
            le.minHeight = px;
            return go.transform;
        }

        // While empty there is ONE well, not two: the dark quest list is switched off (no
        // dead-black half, no place for a second message to appear) and the parchment well
        // spans the union of both as-built rects, so the single empty panel is centred on
        // the whole body in ONE chrome. Restoring flips both back verbatim.
        private void CollapseWellsForEmpty(bool empty)
        {
            if (_detailRt == null) return;
            if (_collapsedForEmpty == empty && _listRt != null &&
                _listRt.gameObject.activeSelf == !empty) return;
            _collapsedForEmpty = empty;

            // The populated detail well deliberately uses parchment for long-form quest copy.
            // An empty state spans the whole body, where that same fill became a giant beige
            // slab. Switch only the empty presentation to the shared near-black surface.
            var backing = _detailRt.Find("ZoneBacking")?.GetComponent<Image>();
            if (backing != null)
                backing.color = empty
                    ? new Color(0.035f, 0.035f, 0.045f, 0.98f)
                    : new Color(0.827f, 0.760f, 0.576f, 1f);

            if (_listRt != null) _listRt.gameObject.SetActive(!empty);

            if (empty && _listRt != null)
            {
                _detailRt.anchorMin = new Vector2(
                    Mathf.Min(_listAnchorMin.x, _detailAnchorMin.x),
                    Mathf.Min(_listAnchorMin.y, _detailAnchorMin.y));
                _detailRt.anchorMax = new Vector2(
                    Mathf.Max(_listAnchorMax.x, _detailAnchorMax.x),
                    Mathf.Max(_listAnchorMax.y, _detailAnchorMax.y));
            }
            else
            {
                _detailRt.anchorMin = _detailAnchorMin;
                _detailRt.anchorMax = _detailAnchorMax;
            }
            _detailRt.offsetMin = Vector2.zero;
            _detailRt.offsetMax = Vector2.zero;
        }

        // ── Parchment detail card (WO-693 grammar: name + flavor -> REWARDS -> PROGRESS) ──
        private void BuildDetail(ItemVM item)
        {
            // REWARDS — the slot's grant (from vm.RewardFor), relayed generically ("+ Crystals +25").
            // All-ASCII glyphs; Good tone is reinforcement only (counts are the carrier).
            var rewards = new List<ElarionUiKit.DetailCardRow>();
            var r = _vm.RewardFor(item.Id);
            if (r.Crystals > 0)
                rewards.Add(new ElarionUiKit.DetailCardRow("+", "Crystals",
                    "+" + r.Crystals, ElarionUiKit.DetailRowTone.Good));
            if (r.Stone > 0)
                rewards.Add(new ElarionUiKit.DetailCardRow("+", "Stone",
                    "+" + r.Stone, ElarionUiKit.DetailRowTone.Good));
            if (r.Wisdom > 0)
                rewards.Add(new ElarionUiKit.DetailCardRow("+", "Wisdom",
                    "+" + r.Wisdom, ElarionUiKit.DetailRowTone.Good));
            if (r.RandomItem)
                rewards.Add(new ElarionUiKit.DetailCardRow("*", "Bonus item", "1",
                    ElarionUiKit.DetailRowTone.Dim));

            // PROGRESS — "OK done" / count toward target; glyph + counts carry the state.
            bool completed = item.Equipped;
            var progress = new List<ElarionUiKit.DetailCardRow>
            {
                new ElarionUiKit.DetailCardRow(
                    completed ? "OK" : "*",
                    completed ? "Complete" : "In progress",
                    _vm.ProgressText(item.Id),
                    completed ? ElarionUiKit.DetailRowTone.Good : ElarionUiKit.DetailRowTone.Neutral),
            };

            ElarionUiKit.BuildParchmentDetailCard(_detailHost, new ElarionUiKit.DetailCardSpec
            {
                Title = item.Name,
                Flavor = _vm.FlavorFor(item.Id),
                BestowsHeader = "REWARDS",
                Bestows = rewards,
                RequiresHeader = "PROGRESS",
                Requires = progress,
                // No CTA: completion rewards auto-dispense (DEF-223 reward bridge).
            });
        }

        // A brief centered "Daily Quest Complete" toast that auto-dismisses (kit ToastCard).
        private void ShowCompletionToast(string label)
        {
            if (_canvas == null) return;
            var toast = ElarionUiKit.ToastCard(_canvas.transform,
                ElarionUiKit.ToastTone.Confirm, accentLeft: false, TextAnchor.MiddleCenter);
            if (toast == null || toast.card == null) return;
            var rt = toast.card.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.30f, 0.78f);
            rt.anchorMax = new Vector2(0.70f, 0.86f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            if (toast.label != null) toast.label.text = "Daily Quest Complete\n" + label;
            StartCoroutine(DismissAfter(toast.card, 3.2f));
        }

        private System.Collections.IEnumerator DismissAfter(GameObject go, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (go != null) Destroy(go);
        }

        // ── presentation helpers ─────────────────────────────────────────────────

        // Clear a drop-zone's repaintable children, PRESERVING the factory's ZoneBacking
        // plate (the two-tone parchment-bleed fix paints it as the zone's first child —
        // destroying it would re-expose the baked art seam under the content).
        private static void ClearZone(Transform host)
        {
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var child = host.GetChild(i);
                if (child == null || child.name == "ZoneBacking") continue;
                Destroy(child.gameObject);
            }
        }

        // Fallback drop-zone for the PROCEDURAL (frame-art-absent) path only.
        private static Transform MakeZone(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go.transform;
        }
    }
}
