// =============================================================================
// DungeonHudController — the dungeon HUD's lantern oil meter, on the Obsidian kit.
// -----------------------------------------------------------------------------
// WO-1005 (dungeon UI cohesion): this View was the last UXML/UIDocument surface a
// dungeon player could actually see. Two problems in one object:
//   1. COHESION — the UXML oil panel was its own one-off styling (DungeonHud.uss),
//      not the obsidian+gold ElarionUiKit chrome every other panel/toast/button
//      wears. The WO's ruling: every player-facing dungeon overlay uses the kit.
//   2. UXML IN BUILDS DOES NOT WORK (CLAUDE.md sec.8, learned the hard way) — the
//      UIDocument came up EMPTY in a player build, so the oil meter the owner
//      acceptance-listed ("make the duration legible") was blank exactly where it
//      mattered.
// The View is now CODE-BUILT uGUI on the kit: an obsidian card (near-black fill +
// soft gold rim, the ToastCard/ObsidianFill chrome), the kit's ObsidianBar as the
// oil bar, kit Labels for the caption + burn-time copy, and the shared ToastCard
// (Danger tone) as the low-oil pill. Nothing here is tappable, so MinTouchPx does
// not apply; the whole overlay is raycast-transparent (never swallows gameplay).
//
// COLOURBLIND LAW: the low/critical state is carried by the PILL'S WORDS and the
// burn-time copy, never by hue alone — the fill tint (amber/red) is a secondary
// reinforcement and only applied when the kit built a tintable fill.
//
// WO-1839 — "hidden by the hud ... easier to read in the entire dungeon". Two proven
// defects, both in THIS file (the UXML/USS the ticket named is the hidden legacy
// sub-tree — see HideLegacyUxmlHud — so nothing there could ever have been the cause):
//   1. NO TEXT AT ALL. The card shipped as a bare bar + ember: no caption naming it,
//      no burn-time readout, no low-oil pill. DungeonHudVM.TimeLabel ("Light: 1m 12s")
//      already existed and the View simply never read it, so the one acceptance item
//      ("make the duration legible") had silently regressed to an unlabeled gauge, and
//      the state read fell back to hue alone — against the COLOURBLIND LAW above. A
//      comment here used to claim the countdown text was "deliberately" omitted; the
//      owner's acceptance ruling outranks it, and it is gone. Text is the carrier now.
//   2. NO SAFE-AREA INSET. The card was pinned at a raw (24,-24) from the screen
//      corner. SafeAreaInset (WO-868) exists for exactly this — screen-anchored HUD
//      chrome — and the top-left corner is where a landscape phone's cutout / rounded
//      corner sits, so the panel was being clipped by the device, not by another HUD
//      layer. No overlapping HUD element was found: DungeonToastView is top-CENTRE at
//      sortingOrder 720, ObjectiveStripUi is bottom-centre and village-FTUE-only, no
//      dg_* scene bakes a HUD canvas, and HudKitController is not DontDestroyOnLoad.
// Every size/position below is a FIRST PASS for the owner to felt-correct.
//
// All player-facing type is at or above ElarionUi.FontFloorMobile (30) per the HUD
// legibility standard — the dungeon panel was missed by the WO-1823/1826 sweep.
//
// LOCALIZED: the caption and the low-oil pill resolve through LocalText (keys
// dungeon.oilCaption / dungeon.oilLowWarning), authored in en.json + its
// StreamingAssets mirror and ai-first-drafted into the nine other locales — the
// same shape the WO-1789 lane used. The English consts below are FALLBACKS only.
//
// MVVM unchanged: DungeonHudVM still owns ALL band logic/copy; this View binds and
// paints, reading NO game state. The DungeonController SetLantern PUSH seam is
// preserved verbatim.
//
// LEGACY SEAM: the cottage scene's UIDocument may still carry DungeonHud.uxml
// (shared with the crafting panel's sub-tree). We hide ONLY the "dungeon-hud-root"
// sub-tree so the crafting panel keeps its document; the serialized _document field
// is kept so existing scene data binds without a rebake.
//
// Instrumented per CLAUDE.md sec.12 — [Flow:DungeonHud] on build, on the legacy
// hide, and on every band transition.
// =============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
// Both UI and UIElements are imported (the kit is uGUI; the legacy hide seam is
// UIElements) — alias the collisions to the uGUI side the kit is built on.
using Image = UnityEngine.UI.Image;

namespace DeNelle.Dungeons
{
    /// <summary>
    /// Drives the dungeon HUD — a glanceable lantern oil meter built from the
    /// Obsidian ElarionUiKit chrome (WO-1005 cohesion; code-built uGUI, no UXML).
    /// A passive UI view: it binds a <see cref="DungeonHudVM"/> and paints; it
    /// never mutates the lantern and never blocks raycasts.
    /// </summary>
    public sealed class DungeonHudController : MonoBehaviour
    {
        private const string Sys = "DungeonHud";

        // Below DungeonToastView (720) — toasts read over the passive meter.
        private const int SortingOrder = 600;

        [Header("Legacy UXML host (kept so existing scene data binds; the HUD sub-tree " +
                "inside it is hidden — the crafting panel may share this document)")]
        [SerializeField] private UIDocument _document;

        [Header("Lantern source")]
        [Tooltip("The Keeper's lantern — the oil meter is fed from its public API " +
                 "each frame. Pushed in by DungeonController on load; optional here.")]
        [SerializeField] private Lantern _lantern;

        [Header("Empty-oil threshold")]
        [Tooltip("At or below this oil fraction the meter reads CRITICAL (red band). " +
                 "A second band inside the lantern's own low-oil fraction so the " +
                 "player gets a graded warning.")]
        [SerializeField, Range(0f, 1f)] private float _criticalOilFraction = 0.1f;

        // ── The ViewModel (owns ALL oil-meter state/band logic + copy) ───────
        private DungeonHudVM _vm;

        // ── Player copy — LOCALE KEYS, never literals ────────────────────────
        // Every player-visible string lives in the locale system (en.json + its
        // StreamingAssets mirror + the nine ai-first-draft locales), per the same
        // discipline the WO-1789 lane applied. The English beside each key is the
        // FALLBACK only — LocalText.Get returns it when no table is installed, which
        // is the case in EditMode oracles, so a headless assertion never reads a
        // [[missing:...]] placeholder. Authored ONCE here, not at each use site.
        private const string KeyOilCaption = "dungeon.oilCaption";
        private const string FallbackOilCaption = "LANTERN OIL";
        private const string KeyOilLowWarning = "dungeon.oilLowWarning";
        private const string FallbackOilLowWarning = "LOW OIL - FIND AN OIL STONE";

        // ── Card geometry (reference px — FIRST PASS, owner felt-corrects) ───
        // Tall enough for a 30px caption row + the bar + a 34px burn-time row + the
        // always-reserved pill row. The pill row is reserved even while hidden so the
        // card never resizes mid-run (a jumping panel is its own legibility defect).
        private const float CardWidthPx  = 470f;
        private const float CardHeightPx = 250f;

        // ── Code-built kit UI ────────────────────────────────────────────────
        private GameObject _canvasGo;
        private RectTransform _cardRt;
        private ElarionUiKit.BarHandle _oilBar;
        private Image _warningGlow;
        private TextMeshProUGUI _timeLabel;
        private GameObject _lowPill;

        // Safe-area re-fit state (rotation / resolution / cutout change).
        private int _lastScreenW;
        private int _lastScreenH;
        private Rect _lastSafeArea;

        // Fill tint band (secondary reinforcement — the pill's WORDS are the carrier).
        private Color _fillNormal;
        private bool _fillTintable;

        // Change-only repaint caches (no per-frame string/state churn).
        private bool _lastLow;
        private bool _lastCritical;
        private bool _legacyHidden;
        private string _lastTimeText;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            // The VM owns the band logic + copy; seed it with the critical threshold
            // (serialized config) and, if a lantern was inspector-assigned, that ref.
            _vm = new DungeonHudVM(_criticalOilFraction);
            if (_lantern != null) _vm.SetLantern(new LanternReadoutAdapter(_lantern));
        }

        private void OnEnable()
        {
            Guard.Try(Sys, "build kit oil HUD", BuildKitHud);
            HideLegacyUxmlHud();
        }

        private void OnDisable()
        {
            if (_canvasGo != null)
            {
                Destroy(_canvasGo);
                _canvasGo = null;
                _cardRt = null;
                _oilBar = null;
                _warningGlow = null;
                _timeLabel = null;
                _lowPill = null;
            }
            // Force a full repaint on the next enable.
            _lastLow = false;
            _lastCritical = false;
            _lastTimeText = null;
        }

        /// <summary>
        /// Binds the lantern the oil meter is fed from. Called by the
        /// <see cref="DungeonController"/> on dungeon load — the controller
        /// already holds the Lantern reference. PUSH seam preserved.
        /// </summary>
        public void SetLantern(Lantern lantern)
        {
            _lantern = lantern;
            if (_vm == null) _vm = new DungeonHudVM(_criticalOilFraction);
            _vm.SetLantern(lantern != null ? new LanternReadoutAdapter(lantern) : null);
        }

        // =====================================================================
        //  Build — Obsidian kit chrome, code-built uGUI (no UXML)
        // =====================================================================

        private void BuildKitHud()
        {
            if (_canvasGo != null) return;   // idempotent across enable cycles

            _canvasGo = new GameObject("DungeonHud_Kit");
            _canvasGo.transform.SetParent(transform, false);

            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // Passive display: never swallow gameplay / interact input. No
            // GraphicRaycaster on purpose — nothing here is tappable.
            var group = _canvasGo.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            // ── The oil card: obsidian near-black rounded fill + soft gold rim
            //    (the ToastCard/ObsidianFill chrome), seated top-left. ──────────
            var card = ElarionUiKit.AddImage(_canvasGo.transform, "OilCard",
                Vector2.zero, Vector2.zero, ElarionUiKit.ObsidianFill, rounded: true);
            ElarionUiKit.AddInnerRim(card, ElarionUiKit.ObsidianTrim);
            _cardRt = (RectTransform)card.transform;
            _cardRt.anchorMin = new Vector2(0f, 1f);
            _cardRt.anchorMax = new Vector2(0f, 1f);
            _cardRt.pivot = new Vector2(0f, 1f);
            _cardRt.sizeDelta = new Vector2(CardWidthPx, CardHeightPx);
            ApplySafeAreaTopLeft();
            var cardImg = card.GetComponent<Image>();
            if (cardImg != null) cardImg.raycastTarget = false;

            // The ember — a wick-shaped SECONDARY reinforcement (it shrinks and pulses
            // as the oil runs out). It sits in the card's top-right corner so it can
            // never sit under the caption or the burn-time copy, which are the real read.
            var glowGo = ElarionUiKit.AddImage(card.transform, "OilEmber",
                Vector2.zero, Vector2.zero, ElarionUi.Gold, rounded: true);
            _warningGlow = glowGo.GetComponent<Image>();
            var grt = (RectTransform)glowGo.transform;
            grt.anchorMin = new Vector2(1f, 1f);
            grt.anchorMax = new Vector2(1f, 1f);
            grt.pivot = new Vector2(1f, 1f);
            grt.anchoredPosition = new Vector2(-18f, -14f);
            grt.sizeDelta = new Vector2(30f, 46f);
            if (_warningGlow != null) _warningGlow.raycastTarget = false;

            // ── Caption — names the gauge. Without it the bar was an unlabeled
            //    sliver of chrome, which is most of what "hidden by the hud" meant.
            ElarionUiKit.Label(card.transform, Copy(KeyOilCaption, FallbackOilCaption), 0.79f, 0.97f,
                ElarionUi.Gold, (int)ElarionUi.FontFloorMobile,
                TextAlignmentOptions.Left, x0: 0.06f, x1: 0.84f, spacing: 2f, bold: true);

            // THE bar (kit section 1.1) — Energy kind: the gold-amber fill reads as
            // lamp oil and matches the HUD bar family art.
            _oilBar = ElarionUiKit.BuildObsidianBar(card.transform,
                ElarionUiKit.ObsidianBarKind.Energy,
                new Vector2(0.06f, 0.57f), new Vector2(0.94f, 0.76f),
                withValue: false, framed: true);

            // ── Burn-time readout — the ACCEPTANCE ITEM ("make the duration
            //    legible"). DungeonHudVM.TimeLabel already projected this copy; the
            //    View just never painted it. Above the 30 floor: this is the number
            //    the player actually reads mid-run, at a glance, while moving.
            _timeLabel = ElarionUiKit.Label(card.transform, DungeonHudVM.FormatBurnTime(float.PositiveInfinity),
                0.33f, 0.55f, new Color(0.96f, 0.93f, 0.86f, 1f), 34,
                TextAlignmentOptions.Left, x0: 0.06f, x1: 0.94f, bold: true);

            // ── Low-oil pill — the PRIMARY colourblind-safe state carrier: the
            //    player is told in WORDS what to do, never by the bar's hue. The row
            //    is reserved always; only the pill's visibility toggles.
            _lowPill = ElarionUiKit.AddImage(card.transform, "LowOilPill",
                new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.29f),
                ElarionUi.DangerFace, rounded: true);
            ElarionUiKit.AddInnerRim(_lowPill, ElarionUiKit.ObsidianTrim);
            var pillImg = _lowPill.GetComponent<Image>();
            if (pillImg != null) pillImg.raycastTarget = false;
            ElarionUiKit.Label(_lowPill.transform,
                Copy(KeyOilLowWarning, FallbackOilLowWarning), 0f, 1f,
                new Color(1f, 0.93f, 0.91f, 1f), (int)ElarionUi.FontFloorMobile,
                TextAlignmentOptions.Center, x0: 0.04f, x1: 0.96f, bold: true);
            _lowPill.SetActive(false);
            _fillNormal = _oilBar != null && _oilBar.fill != null ? _oilBar.fill.color : Color.white;
            // A coloured pack fill stays white on purpose (kit rule) — only a
            // tintable (non-white) fill takes the amber/red band reinforcement.
            _fillTintable = _fillNormal != Color.white;

            FlowTrace.Step(Sys,
                $"oil HUD built (WO-1839): obsidian card {CardWidthPx:0}x{CardHeightPx:0} top-left " +
                $"INSIDE the safe area @ {_cardRt.anchoredPosition} (margin {SafeAreaInset.EdgeMarginPx:0}px); " +
                $"caption+burnTime+lowOilPill all >= FontFloorMobile({ElarionUi.FontFloorMobile:0}) so the " +
                "duration is legible and the low state is carried by WORDS not hue; " +
                $"ObsidianBar kind=Energy fillTintable={_fillTintable}, ember=secondary-reinforcement " +
                $"sortingOrder={SortingOrder} raycast=OFF (code-built uGUI, no UXML)");
        }

        /// <summary>
        /// Resolves one player-visible string through the locale table, falling back to
        /// the authored English. Wrapped in <see cref="Guard.Try"/> per CLAUDE.md §12: a
        /// throwing provider must never cost the player the caption that names the gauge
        /// — losing it is the exact defect WO-1839 was opened for.
        /// </summary>
        private static string Copy(string key, string englishFallback)
        {
            string line = null;
            Guard.Try(Sys, "resolve locale copy " + key,
                () => line = LocalText.Get(key, englishFallback));
            return string.IsNullOrEmpty(line) ? englishFallback : line;
        }

        /// <summary>
        /// Pins the card inside the live <see cref="Screen.safeArea"/> at the TOP-LEFT,
        /// using the shared <see cref="SafeAreaInset"/> pure math. WO-1839: the card was
        /// previously at a raw (24,-24) from the screen corner, which on a landscape
        /// phone puts it under the cutout / rounded corner — the device was clipping it.
        /// <see cref="SafeAreaInset"/> ships only a TOP-RIGHT applier, so the top-left
        /// case is composed here from its public insets rather than widening the shared
        /// kit file from inside a dungeon lane.
        /// </summary>
        private void ApplySafeAreaTopLeft()
        {
            if (_cardRt == null) return;
            var safe = Screen.safeArea;
            int w = Screen.width;
            int h = Screen.height;
            float m = SafeAreaInset.EdgeMarginPx;

            // SafeAreaInset's insets and EdgeMarginPx are DEVICE px; anchoredPosition is
            // in CANVAS units. This canvas is ScaleWithScreenSize (1080x1920 ref), so the
            // two differ — divide by the live scaleFactor, exactly as the shared applier
            // SafeAreaInset.FitTopRightNow does (SafeAreaInset.cs:143-146). Without this
            // the inset lands ~12% off on a Seeker-density screen.
            var canvas = _canvasGo != null ? _canvasGo.GetComponent<Canvas>() : null;
            float scale = (canvas != null && canvas.scaleFactor > 0.0001f) ? canvas.scaleFactor : 1f;

            var screenPx = new Vector2(
                SafeAreaInset.LeftInset(safe) + m,
                -(SafeAreaInset.TopInset(safe, h) + m));
            _cardRt.anchoredPosition = screenPx / scale;
            _lastScreenW = w;
            _lastScreenH = h;
            _lastSafeArea = safe;
        }

        /// <summary>Re-fits the card when the screen or the reported safe area changes
        /// (rotation, resolution change, a cutout becoming reported late on some OEMs).</summary>
        private void RefitIfScreenChanged()
        {
            if (_cardRt == null) return;
            if (Screen.width == _lastScreenW && Screen.height == _lastScreenH
                && Screen.safeArea == _lastSafeArea) return;
            ApplySafeAreaTopLeft();
            FlowTrace.Step(Sys,
                $"oil card re-fitted to safe area: screen={Screen.width}x{Screen.height} " +
                $"safeArea={Screen.safeArea} -> anchoredPosition={_cardRt.anchoredPosition}");
        }

        /// <summary>
        /// Hide ONLY the legacy UXML HUD sub-tree ("dungeon-hud-root") so a scene
        /// still carrying DungeonHud.uxml never double-draws the meter — while the
        /// crafting panel, which may share this UIDocument, keeps its own sub-tree.
        /// </summary>
        private void HideLegacyUxmlHud()
        {
            if (_legacyHidden) return;
            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
            {
                // Expected on a code-built seat with no UIDocument: nothing to hide.
                FlowTrace.Step(Sys, "no legacy UIDocument root - kit HUD is the only oil meter");
                _legacyHidden = true;
                return;
            }
            var legacy = root.Q<VisualElement>("dungeon-hud-root");
            if (legacy != null)
            {
                legacy.style.display = DisplayStyle.None;
                FlowTrace.Step(Sys, "legacy UXML 'dungeon-hud-root' sub-tree HIDDEN " +
                    "(kit HUD replaces it; crafting sub-tree untouched)");
            }
            _legacyHidden = true;
        }

        // =====================================================================
        //  Per-frame — paint from the VM only (no game-state read)
        // =====================================================================

        private void Update()
        {
            if (_vm == null || _canvasGo == null) return;

            RefitIfScreenChanged();

            if (_oilBar != null)
                _oilBar.SetImmediate(_vm.BarFraction, 1f);   // per-frame sweep: no easing

            // The burn-time copy — change-only so a per-frame string never churns.
            if (_timeLabel != null)
            {
                string time = _vm.TimeLabel;
                if (time != _lastTimeText)
                {
                    _lastTimeText = time;
                    _timeLabel.text = time;
                }
            }

            // Band transitions (change-only: pill WORDS + tint reinforcement + trace).
            bool critical = _vm.IsCritical;
            bool low = _vm.ShowLowWarning;
            if (critical != _lastCritical || low != _lastLow)
            {
                _lastCritical = critical;
                _lastLow = low;
                // The words are the carrier — shown before any hue change is applied.
                if (_lowPill != null && _lowPill.activeSelf != low) _lowPill.SetActive(low);
                if (_fillTintable && _oilBar != null && _oilBar.fill != null)
                {
                    _oilBar.fill.color = critical ? ElarionUi.Danger
                        : _vm.IsWarning ? new Color(1f, 0.65f, 0.18f, 1f)   // amber low band
                        : _fillNormal;
                }
                FlowTrace.Step(Sys,
                    $"oil band -> {(critical ? "CRITICAL" : low ? "LOW" : "ok")} " +
                    $"(fraction={_vm.BarFraction:F2}, lowOilPillShown={low}, " +
                    $"burnTime='{_lastTimeText}') - state carried by WORDS, tint is reinforcement only");
            }

            if (_warningGlow != null)
            {
                float urgency = _vm.FinalWarningProgress;
                float pulse = urgency > 0f
                    ? 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.Lerp(5f, 13f, urgency)))
                    : 1f;
                _warningGlow.color = critical ? ElarionUi.Danger
                    : low ? new Color(1f, 0.65f, 0.18f, pulse)
                    : new Color(ElarionUi.Gold.r, ElarionUi.Gold.g, ElarionUi.Gold.b, 0.9f);
                float scale = Mathf.Lerp(1f, 0.48f, urgency) * pulse;
                _warningGlow.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
