// =============================================================================
// RaidHudController — the LIVE raid HUD (WO-771.11, LOCKED teleport/deploy loop).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The passive readout during a raid: a 180s countdown, a star-progress indicator,
// live %-destruction, and troops alive/deployed. It reads RaidScoring.Instance every
// frame (passive — it never drives combat) and renders through code-built uGUI
// (NO UXML — repo rule §8), mirroring RaidDeployController's self-install +
// ElarionUiKit chrome.
//
// ⚠ WO-1464 CORRECTION: this header used to say the deploy tray (bottom) and this HUD
// (top) "sit on complementary edges and never overlap". They never overlapped EACH
// OTHER, and neither of them was ever checked against the town HUD underneath — which
// is how this panel came to paint its clock across the hero nameplate and its stars
// across the compass on the owner's device. Both seats now come from
// HudLayoutBands (DeNelle.Core.UI) and the exclusion is a red gate, not a claim.
//
// COLOURBLIND-SAFE (repo law): every state reads by SHAPE / MOTION / NUMBER, never
// hue alone — the timer is a number + a shrinking bar (+ a pulse under 30s), stars
// are filled/empty DIAMONDS + an "n/3" count, destruction is a number + a fill bar,
// troops are a plain "alive/deployed" number.
//
// ASCII-only runtime strings. Canon: the village is Elarion (never Avalon).
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// The live raid HUD (timer / stars / destruction% / troop counts). Passive —
    /// binds <see cref="RaidScoring"/> and renders; never mutates game state.
    /// Self-installs into any <c>RaidBase_*</c> scene (idempotent).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidHudController : MonoBehaviour
    {
        // Refresh cadence — the HUD polls the scorer ~10x/sec (cheap; the numbers
        // change slowly). The timer text still counts smoothly enough at 10Hz.
        private const float RefreshInterval = 0.1f;
        private float _refreshTimer;

        private GameObject _ui;

        // Widgets refreshed each poll.
        private TMPro.TextMeshProUGUI _timerLabel;
        private RectTransform _timerFill;          // shrinks left->right with the clock
        private TMPro.TextMeshProUGUI _destLabel;
        private RectTransform _destFill;           // grows with destruction%
        // THE OBJECTIVE (owner concept 2026-08-02): the raid is won by razing the central
        // spire, so the headline readout on the right is the spire's HP, not a corpse count.
        private TMPro.TextMeshProUGUI _objLabel;
        private RectTransform _objFill;            // DRAINS as the spire is chipped down
        private TMPro.TextMeshProUGUI _troopLabel;
        private TMPro.TextMeshProUGUI _starCount;  // "n/3"
        private readonly Image[] _starDiamonds = new Image[3];

        private static readonly Color StarLit = ElarionUi.Gilt;
        private static readonly Color StarDim = new Color(1f, 1f, 1f, 0.14f);

        // =====================================================================
        //  Self-install — one HUD per RaidBase_* scene
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            TryInstall(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                          UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            TryInstall(scene.name);
        }

        private static void TryInstall(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (!sceneName.StartsWith("RaidBase", StringComparison.OrdinalIgnoreCase)) return;
            if (FindAnyObjectByType<RaidHudController>() != null) return;

            var go = new GameObject("RaidHudController");
            go.AddComponent<RaidHudController>();
            FlowTrace.Step("Raid", $"RaidHudController self-installed in raid scene '{sceneName}'.");
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Start()
        {
            BuildHud();
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui);
        }

        private void Update()
        {
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshInterval;
            Refresh();
        }

        // =====================================================================
        //  HUD construction (code-built uGUI, reserved right column — WO-1464)
        // =====================================================================

        // ── WO-1464: WHERE THE READOUT SITS, AND WHY IT IS NOT A TOP STRIP ANY MORE ──────
        //
        // ⛔ THE DEFECT, MEASURED ON THE OWNER'S DEVICE (build 358872, 2670x1200, mid-raid at
        // 1:13 - Logs/device/screens/owner-screen-20260907-004502.png): this panel was authored
        // x 0.020-0.980, y 0.860-0.990, straight across the town HUD's whole top row. In that one
        // frame "1:13" is painted over the hero nameplate ("Th... Lv 7" plus its health/XP bars),
        // and "1/3" and "Troops 10/10" are painted over the compass's NE / E ticks and the bar
        // beneath them. The comment at the head of this file claimed the two raid surfaces "sit
        // on complementary edges and never overlap" - true of each other, and never checked
        // against the HUD underneath, which is the WO-1219 / WO-1436 failure for the third time.
        //
        // ⛔ THE SEAT IS SHARED DATA: HudLayoutBands.RaidReadoutBand (DeNelle.Core.UI). That file
        // carries the whole argument for why a full-width top strip CANNOT exist on this HUD and
        // why the right-hand column is free for a raid's entire duration. Do NOT re-introduce a
        // literal rect here - a Village-local literal is precisely what could not see the
        // nameplate it was landing on.

        /// <summary>The readout's screen band. Exposed so the oracle asserts the exclusion from
        /// the AUTHORED seat this method consumes, never from a figure copied into a test.</summary>
        public static Rect ReadoutBand
        {
            get { return HudLayoutBands.RaidReadoutBand; }
        }

        private void BuildHud()
        {
            if (_ui != null) Destroy(_ui);

            // Below the deploy HUD's 30000 so the deploy tray/buttons stay tappable on top.
            _ui = ElarionUiKit.BuildModalCanvas("RaidHud", 29000);

            // A framed dark-glass COLUMN on the right (the deploy tray owns the bottom, and the
            // town HUD owns the whole top row - see the WO-1464 block above).
            var band = ReadoutBand;
            var bar = ElarionUiKit.Panel(_ui.transform,
                new Vector2(band.xMin, band.yMin), new Vector2(band.xMax, band.yMax), deep: false);
            FlowTrace.Step("Raid",
                "raid readout seated in the reserved right column: x " +
                band.xMin.ToString("F3") + ".." + band.xMax.ToString("F3") + ", y " +
                band.yMin.ToString("F3") + ".." + band.yMax.ToString("F3") +
                " (clear of the hero nameplate and the compass - WO-1464).");
            // Passive HUD: the strip must never intercept a deploy/rally tap.
            var barImg = bar.GetComponent<Image>();
            if (barImg != null)
            {
                barImg.raycastTarget = false;
                // Quiet glass, not a gilt slab. The five readout rows stay (layout
                // oracles pin SPIRE / Razed / timer); only the chrome gets out of the way.
                barImg.color = new Color(0.04f, 0.035f, 0.03f, 0.42f);
            }
            var barT = bar.transform;

            // ── THE COLUMN, top to bottom: TIMER / SPIRE / RAZED / STARS / TROOPS ────────
            // Fractions below are OF THE PANEL, so the whole stack follows the band above with
            // no second set of screen literals. Rows are ordered by how often the player looks:
            // the clock is the one thing checked constantly, so it takes the top of the column.
            const float PadX0 = 0.05f, PadX1 = 0.95f;

            // ── TIMER (big number + shrinking bar under it) ──────────────────────
            _timerLabel = MakeLabel(barT, "3:00", new Vector2(PadX0, 0.795f), new Vector2(PadX1, 0.985f),
                ElarionUi.Parchment, ElarionUi.FontBody, TMPro.TextAlignmentOptions.Right, bold: true);

            var timerTrack = ElarionUiKit.AddImage(barT, "TimerTrack",
                new Vector2(PadX0, 0.735f), new Vector2(PadX1, 0.780f), new Color(0f, 0f, 0f, 0.5f), rounded: false);
            timerTrack.GetComponent<Image>().raycastTarget = false;
            var timerFillGo = ElarionUiKit.AddImage(timerTrack.transform, "TimerFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUi.Gilt, rounded: false);
            timerFillGo.GetComponent<Image>().raycastTarget = false;
            _timerFill = (RectTransform)timerFillGo.transform;

            // ── THE OBJECTIVE (spire HP) + its draining bar ──────────────────────
            // The old right column said "Razed N%" and was fed a pure corpse count, so it
            // read 100% with every structure untouched. The headline is the WIN CONDITION -
            // the spire - and the blended destruction sits under it as the secondary
            // (scoring) number.
            _objLabel = MakeLabel(barT, "SPIRE 100%", new Vector2(PadX0, 0.575f), new Vector2(PadX1, 0.705f),
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right, bold: true);

            var objTrack = ElarionUiKit.AddImage(barT, "ObjectiveTrack",
                new Vector2(PadX0, 0.515f), new Vector2(PadX1, 0.560f), new Color(0f, 0f, 0f, 0.5f), rounded: false);
            objTrack.GetComponent<Image>().raycastTarget = false;
            var objFillGo = ElarionUiKit.AddImage(objTrack.transform, "ObjectiveFill",
                new Vector2(0f, 0f), new Vector2(1f, 1f), ElarionUi.Gilt, rounded: false);
            objFillGo.GetComponent<Image>().raycastTarget = false;
            _objFill = (RectTransform)objFillGo.transform;

            // ── RAZED % + its growing bar ────────────────────────────────────────
            _destLabel = MakeLabel(barT, "Razed 0%", new Vector2(PadX0, 0.365f), new Vector2(PadX1, 0.485f),
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right);

            var destTrack = ElarionUiKit.AddImage(barT, "DestTrack",
                new Vector2(PadX0, 0.310f), new Vector2(PadX1, 0.350f), new Color(0f, 0f, 0f, 0.5f), rounded: false);
            destTrack.GetComponent<Image>().raycastTarget = false;
            var destFillGo = ElarionUiKit.AddImage(destTrack.transform, "DestFill",
                new Vector2(0f, 0f), new Vector2(0f, 1f), ElarionUi.Affordable, rounded: false);
            destFillGo.GetComponent<Image>().raycastTarget = false;
            _destFill = (RectTransform)destFillGo.transform;

            // ── STAR PROGRESS (3 diamonds + n/3) ─────────────────────────────────
            // Diamonds are FIXED-SIZE reference px hung on a point anchor, so they keep their
            // silhouette at any band aspect; the "n/3" beside them is the number that carries
            // the state without hue (repo colourblind law).
            for (int i = 0; i < 3; i++)
            {
                float cx = 0.16f + i * 0.16f;
                var d = ElarionUiKit.AddImage(barT, "Star" + i,
                    new Vector2(cx, 0.220f), new Vector2(cx, 0.220f), StarDim, rounded: false);
                var img = d.GetComponent<Image>();
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.sizeDelta = new Vector2(34f, 34f);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f);   // diamond
                _starDiamonds[i] = img;
            }
            _starCount = MakeLabel(barT, "0/3", new Vector2(0.58f, 0.155f), new Vector2(PadX1, 0.285f),
                ElarionUi.Gilt, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right, bold: true);

            // ── Troops alive/deployed (a plain number at the foot of the column) ──
            // ⚠ 0.130 of the panel, not 0.110. The band resolves to 347.5 reference px at the
            // owner's 2670x1200 (0.360 of a 965.4-unit canvas), so a 0.110 row is 38.2 ref px -
            // UNDER the 38.6 that seats the 30 px FontFloor, and TMP Ellipsis culls a line it
            // cannot seat, rendering it BLANK. That is the WO-1519 [seat] finding applied here
            // before it could ship, not after.
            _troopLabel = MakeLabel(barT, "Troops 0/0", new Vector2(PadX0, 0.010f), new Vector2(PadX1, 0.140f),
                ElarionUi.ParchmentDim, ElarionUi.FontLabel, TMPro.TextAlignmentOptions.Right);

            Refresh();
        }

        private static TMPro.TextMeshProUGUI MakeLabel(Transform parent, string text,
            Vector2 anchorMin, Vector2 anchorMax, Color color, int fontSize,
            TMPro.TextAlignmentOptions align, bool bold = false)
        {
            var go = new GameObject("Label", typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            t.text = text;
            t.color = color;
            t.fontSize = fontSize;
            t.fontStyle = bold ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
            t.alignment = align;
            t.raycastTarget = false;
            // WO-1464: the readout column is narrower than the old full-width strip, so every
            // row is overflow-protected by the kit rather than by hoping the string is short.
            ElarionUiKit.FitSingleLine(t);
            return t;
        }

        // =====================================================================
        //  Refresh — pull the live scorer numbers into the widgets (passive)
        // =====================================================================

        private void Refresh()
        {
            var s = RaidScoring.Instance;
            if (s == null) return;

            // Timer: number + shrinking bar. Pulse (motion, not hue) under 30s so a
            // colourblind player still reads "running out".
            float remaining = s.RemainingSeconds;
            if (_timerLabel != null) _timerLabel.text = FormatTime(remaining);
            if (_timerFill != null)
            {
                float frac = s.ClockSeconds > 0f ? Mathf.Clamp01(remaining / s.ClockSeconds) : 0f;
                _timerFill.anchorMax = new Vector2(frac, 1f);
                float pulse = remaining <= 30f && remaining > 0f
                    ? 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
                if (_timerLabel != null) _timerLabel.transform.localScale = Vector3.one * pulse;
            }

            // Stars: filled/empty diamonds (shape) + n/3 (number).
            int stars = s.ProjectedStars;
            for (int i = 0; i < _starDiamonds.Length; i++)
                if (_starDiamonds[i] != null)
                    _starDiamonds[i].color = i < stars ? StarLit : StarDim;
            if (_starCount != null) _starCount.text = stars + "/3";

            // THE OBJECTIVE: spire HP remaining. Colourblind-safe - a NUMBER plus a bar
            // that DRAINS (motion), never hue alone. "SPIRE DOWN" is the win read.
            if (_objLabel != null || _objFill != null)
            {
                if (!s.HasObjective)
                {
                    // Legacy raid base with no spire - say so rather than showing a fake bar.
                    if (_objLabel != null) _objLabel.text = "CLEAR THE BASE";
                    if (_objFill != null) _objFill.anchorMax = new Vector2(1f, 1f);
                }
                else if (s.ObjectiveComplete)
                {
                    if (_objLabel != null) _objLabel.text = "SPIRE DOWN";
                    if (_objFill != null) _objFill.anchorMax = new Vector2(0f, 1f);
                }
                else
                {
                    float frac = Mathf.Clamp01(s.ObjectiveHpFraction);
                    if (_objLabel != null) _objLabel.text = "SPIRE " + Mathf.CeilToInt(frac * 100f) + "%";
                    if (_objFill != null) _objFill.anchorMax = new Vector2(frac, 1f);
                }
            }

            // Secondary (scoring) readout: how much of the BASE has been razed - the
            // objective-weighted blend of spire damage + garrison cleared.
            int pct = Mathf.Clamp(Mathf.RoundToInt(s.DestructionPct * 100f), 0, 100);
            if (_destLabel != null) _destLabel.text = "Razed " + pct + "%";
            if (_destFill != null) _destFill.anchorMax = new Vector2(pct / 100f, 1f);

            // Troops alive / deployed (plain number).
            if (_troopLabel != null) _troopLabel.text = "Troops " + s.TroopsAlive + "/" + s.TroopsDeployed;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
