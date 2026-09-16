// =============================================================================
// DEPRECATED (owner 2026-06-24): F10 dev menu retired - use Settings -> DevTools
// (AdminOverlay). Remove after confirming no tool is lost. Activation (F10 hotkey +
// 5-tap corner gesture) is gated OFF below; the class + all action handlers remain
// intact so any F10-only tool can be migrated to the Settings menu before purge.
// TAGGED FOR REMOVAL (owner 2026-06-28): superseded by AdminOverlay; remove after
// confirming no tool is lost.
// -----------------------------------------------------------------------------
// DevPanelController — the DEV-ONLY in-game QA / debug console (DeNelle.DevTools)
// -----------------------------------------------------------------------------
// An in-game console for QA: it loads resources and jumps game state so the
// docs/qa/qa-test-plan.md test cases and docs/qa/uat-script.md UAT steps can be
// set up without a full playthrough (e.g. jump straight to a wave, top up
// crystals, grant a pack, spawn Syndrath the dragon boss).
//
// ── RELEASE-SAFE — the single most important property of this file ──────────
// The ENTIRE file body is wrapped in `#if DEVELOPMENT_BUILD || UNITY_EDITOR`.
// A shipped (non-development) player build compiles it to an EMPTY file —
// nothing of this panel ships. Belt-and-braces, the DeNelle.DevTools.asmdef
// also carries a define constraint ("UNITY_EDITOR || DEVELOPMENT_BUILD") so the
// whole assembly is skipped in a release build. Any call site that spawns this
// panel (see DevBootstrap.cs) is gated the same way. See the integrator notes
// at the foot of this file and docs/port-notes/dev-panel.md.
//
// ── MODULE ISOLATION EXCEPTION ───────────────────────────────────────────────
// The project's modules are normally isolated (port spec Part 2). DevTools is
// TOOLING, not gameplay — it MAY reference the gameplay modules (Core, Village,
// Wallet, HUD) because it is dev-only and compiled out of release. It reaches
// systems through their existing PUBLIC APIs / Core seams where reasonable:
//   - GameStateService — crystals / entitlements / Heart-adjacent state.
//   - SceneRouter       — scene jumps.
//   - HeartController / WaveManager / DragonBoss — found in the loaded scene.
//   - WalletService + StubWalletProvider — mock balances.
// Everything is null-guarded: a scene that lacks (say) a WaveManager simply
// reports "no WaveManager in scene" instead of throwing.
//
// ── UI ───────────────────────────────────────────────────────────────────────
// A UI Toolkit overlay (DevPanel.uxml/.uss). Toggled by a hotkey (F1 by
// default) AND by tapping the on-screen "DEV" corner chip. The action groups
// and their buttons are built ONCE at runtime into the "dev-group-list"
// container — matches the VillageHudController ability-bar pattern.
// =============================================================================

#if DEVELOPMENT_BUILD || UNITY_EDITOR

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.HUD;
using DeNelle.Village;
using DeNelle.Wallet;
using UnityEngine;
using UnityEngine.UIElements;
using PanelMgr = DeNelle.Core.UI.PanelManager;

namespace DeNelle.DevTools
{
    /// <summary>
    /// DEV-ONLY in-game QA / debug console. A UI Toolkit overlay toggled by a
    /// hotkey or the on-screen corner chip; its buttons load resources and jump
    /// game state so QA can set up a scenario without a full playthrough.
    /// <para>
    /// Compiled ONLY into Editor + Development builds — the whole file is
    /// <c>#if DEVELOPMENT_BUILD || UNITY_EDITOR</c> and the DeNelle.DevTools
    /// asmdef carries a matching define constraint. A release player build
    /// contains none of it.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DevPanelController : MonoBehaviour
    {
        // ── Inspector wiring ──────────────────────────────────────────────────

        [Header("UI")]
        [Tooltip("UIDocument hosting DevPanel.uxml. Falls back to the component on this GameObject.")]
        [SerializeField] private UIDocument _document;

        [Header("Hotkey")]
        [Tooltip("Key that toggles the dev console open / closed. Default F10 — obscure on purpose so a " +
                 "tester won't stumble onto the dev tools; the visible DEV corner chip is hidden too.")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.F10;

        [Tooltip("Open the panel automatically when the scene starts.")]
        [SerializeField] private bool _openOnStart;

        [Header("Crystal grants")]
        [Tooltip("Amount the small 'GIVE crystals' button adds.")]
        [SerializeField] private int _crystalSmallGrant = 100;

        [Tooltip("Amount the large 'GIVE crystals' button adds.")]
        [SerializeField] private int _crystalLargeGrant = 1000;

        [Header("Defaults for the typed actions")]
        [Tooltip("Default pack / entitlement SKU pre-filled in the GRANT field.")]
        [SerializeField] private string _defaultPackId = "hearth-spark";

        [Tooltip("Default wave number pre-filled in the JUMP-to-wave field.")]
        [SerializeField] private int _defaultJumpWave = 5;

        [Tooltip("Default mock wallet balance applied to every rail by MOCK wallet.")]
        [SerializeField] private double _defaultMockBalance = 100d;

        [Header("Spawn prefabs (dev-only)")]
        [Tooltip("Boss_Dragon prefab (carries a DragonBoss). Assigned for the " +
                 "'Spawn Syndrath' action. Optional — the action reports cleanly if blank.")]
        [SerializeField] private DragonBoss _dragonBossPrefab;

        // ── UXML element names — the binding contract with DevPanel.uxml ─────
        private const string RootName = "dev-panel-root";
        private const string CornerTapName = "dev-corner-tap";
        private const string WindowName = "dev-panel-window";
        private const string CloseButtonName = "dev-panel-close";
        private const string StatusName = "dev-status";
        private const string GroupListName = "dev-group-list";

        // ── USS class names — styled by DevPanel.uss ─────────────────────────
        private const string WindowOpenClass = "dev-panel-window--open";
        private const string GroupClass = "dev-group";
        private const string GroupCaptionClass = "dev-group__caption";
        private const string GroupRowClass = "dev-group__row";
        private const string ActionButtonClass = "dev-action-button";
        private const string ToggleOnClass = "dev-action-button--toggle-on";
        private const string ToggleOffClass = "dev-action-button--toggle-off";
        private const string TextFieldClass = "dev-text-field";

        // ── Bound UI elements ────────────────────────────────────────────────
        private VisualElement _root;
        private VisualElement _cornerTap;
        private VisualElement _window;
        private Button _closeButton;
        private Label _status;
        private VisualElement _groupList;
        private bool _bound;
        private bool _isOpen;

        // DEF-212 single-modal arbiter handle. The dev console MUST route through
        // PanelManager like every other in-game modal (HelpMenu / AdminOverlay):
        // otherwise a global back/ESC (PanelManager.CloseOpen) can't dismiss it and
        // it neither closes nor is closed-by the other modals — leaving a panel the
        // player "cannot close" (F8 telemetry). See docs/MASTER_CATALOG/hud.md.
        private PanelHandle _panelHandle;

        // ── Live metrics readout (the "decked-out" telemetry; dev-only) ───────
        // A code-built panel of label:value rows refreshed a few times a second
        // while the console is open. Keyed by a short id so UpdateMetrics() can
        // set each value without re-querying the visual tree.
        private VisualElement _metricsPanel;
        private readonly Dictionary<string, Label> _metricValues = new Dictionary<string, Label>();
        private float _fpsSmoothed;
        private float _metricsTimer;
        private const float MetricsInterval = 0.2f;   // ~5 refreshes / second

        // ── Live typed-action input values ───────────────────────────────────
        private string _packIdInput;
        private int _waveInput;
        private double _mockBalanceInput;
        private int _levelInput = 10;
        private string _modifierJson = "";   // WO-430 — dev modifier-override JSON paste

        // Queue time-skip readout button (owner 2026-08-04). Its LABEL carries the
        // accumulated skip, so the state is visible without a new widget type.
        private Button _timeSkipButton;

        // ── God-mode / instant-win toggles ───────────────────────────────────
        // DevTools owns these flags; the integrator reads them from gameplay
        // (see the integrator notes at the foot of this file). They are static
        // so gameplay can query them without holding a panel reference.

        /// <summary>
        /// DEV god-mode flag. When true the integrator suppresses damage to the
        /// Heart / hero. Read by gameplay; never on in a release build (this
        /// whole type is compiled out).
        /// </summary>
        public static bool GodMode { get; private set; }

        /// <summary>
        /// DEV instant-win-wave flag. When true the integrator clears the active
        /// wave immediately. Read by gameplay.
        /// </summary>
        public static bool InstantWinWave { get; private set; }

        /// <summary>Raised when <see cref="GodMode"/> changes (so gameplay can refresh).</summary>
        public static event Action<bool> GodModeChanged;

        /// <summary>Raised when <see cref="InstantWinWave"/> changes.</summary>
        public static event Action<bool> InstantWinWaveChanged;

        /// <summary>Buttons whose label / class reflect a toggle — refreshed after every action.</summary>
        private readonly List<ToggleBinding> _toggleButtons = new List<ToggleBinding>();

        /// <summary>One toggle button + the predicate that decides its on/off look.</summary>
        private struct ToggleBinding
        {
            public Button Button;
            public string OnLabel;
            public string OffLabel;
            public Func<bool> IsOn;
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            _packIdInput = _defaultPackId;
            _waveInput = Mathf.Max(1, _defaultJumpWave);
            _mockBalanceInput = _defaultMockBalance;
        }

        private void OnEnable()
        {
            BindElements();
            // Register with the single-modal arbiter so a global back/ESC (CloseOpen)
            // can dismiss the console and opening it closes any other open panel.
            if (_panelHandle == null)
                _panelHandle = PanelMgr.Register("DevConsole", Close, () => _isOpen);

            // WO-928/owner ruling 2026-08-08: the on-screen DEV chips are gone
            // (ff.devresourcetool now defaults OFF everywhere), so Settings is the door in.
            // Registering the id here rather than exposing the type keeps the seam clean:
            // DeNelle.Settings references Core only and never DevTools, and because THIS
            // assembly is compiled out of release builds, nothing registers the id there and
            // PanelRouter.IsRegistered stays false - which is what lets the Settings entry hide
            // itself instead of offering a button that does nothing. Same reflection-free
            // pattern RealmMapPanel uses.
            DeNelle.Core.UI.PanelRouter.Register(DeNelle.Core.UI.PanelId.DevPanel, OpenFromRouter);

            SetOpen(_openOnStart);
        }

        /// <summary>Router entry: force the console OPEN (never a toggle). A Settings row that
        /// sometimes closes the thing it just opened is the kind of affordance that reads as
        /// broken, and the router's contract is "open this panel".</summary>
        private void OpenFromRouter() => SetOpen(true);

        private void OnDisable()
        {
            if (_closeButton != null) _closeButton.clicked -= Close;
            if (_cornerTap != null)
                _cornerTap.UnregisterCallback<ClickEvent>(OnCornerTapped);
            // Clear our slot if we were the open panel, so we never leave a stale
            // "open" record that suppresses world prompts after we're gone.
            if (_panelHandle != null) PanelMgr.NotifyClosed(_panelHandle);

            // Symmetry with OnEnable. A router id left registered against a destroyed
            // controller is a Settings button that survives its own panel and throws on tap.
            DeNelle.Core.UI.PanelRouter.Unregister(DeNelle.Core.UI.PanelId.DevPanel, OpenFromRouter);

            _bound = false;
        }

        private void Update()
        {
            // Hotkey toggle. Input.GetKeyDown is fine for a dev tool — no need to
            // route a dev console through the Input System action maps.
            // PLAYER-BUILD SAFETY: the F1 toggle is gated behind the global
            // DevHotkeys kill-switch (default OFF) so a key press can never pop the
            // dev console in the shipped .exe OR the editor unless a dev opts in
            // (PlayerPrefs ff.devhotkeys=1). The on-screen "DEV" corner chip remains
            // the always-available entry.
            // DEPRECATED (owner 2026-06-24): F10 dev menu retired - use Settings -> DevTools
            // (AdminOverlay). Remove after confirming no tool is lost. The F10 hotkey toggle is
            // disabled (gated behind a constant false) so this menu no longer opens; the Settings
            // menu is the single dev-tools entry. Handler kept for migration/restore.
            const bool F10MenuRetired = true;
            if (!F10MenuRetired &&
                DeNelle.Core.FeatureFlags.DevHotkeys && Input.GetKeyDown(_toggleKey))
                SetOpen(!_isOpen);

            // Smooth FPS every frame (unscaled so it reads true during slow-mo).
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                float inst = 1f / dt;
                _fpsSmoothed = _fpsSmoothed <= 0f ? inst : Mathf.Lerp(_fpsSmoothed, inst, 0.1f);
            }

            // Refresh the readout a few times a second while open (cheap for a dev tool).
            if (_isOpen)
            {
                _metricsTimer += dt;
                if (_metricsTimer >= MetricsInterval)
                {
                    _metricsTimer = 0f;
                    UpdateMetrics();
                }
            }
        }

        // =====================================================================
        //  UI Toolkit binding
        // =====================================================================

        private void BindElements()
        {
            _root = _document != null ? _document.rootVisualElement : null;
            if (_root == null)
            {
                Debug.LogWarning("[DevPanelController] No UIDocument root — dev console will not display.");
                return;
            }

            // ── Code-built scaffold (NOT bound from DevPanel.uxml) ────────────
            // This project's UXML templates render EMPTY in player builds (the same
            // trap that blanked BattleHUD / BuildMenu / PackStore). To guarantee the
            // console draws in a development build, the whole UI is constructed here
            // in C# with inline styles — no dependency on the .uxml/.uss assets.
            _root.Clear();
            _root.style.flexGrow = 1f;
            _root.pickingMode = PickingMode.Ignore;   // never eat gameplay input

            // INVISIBLE corner TAP-ZONE (mobile has no F-keys, so the dev tools need a touch entry that
            // a tester won't stumble onto). No "DEV" label, fully transparent — it opens the console ONLY
            // on FIVE rapid taps (OnCornerTapped), the classic hidden-dev-menu gesture. A stray single
            // tap does nothing.
            // DEPRECATED (owner 2026-06-24): F10 dev menu retired - use Settings -> DevTools
            // (AdminOverlay). Remove after confirming no tool is lost. The 5-tap corner gesture is
            // disabled: the tap-zone is no longer created/registered, so this menu cannot spawn.
            // The Settings -> DevTools menu is the single dev-tools entry. (OnCornerTapped kept.)
            bool F10CornerTapRetired = true; // non-const so the retired branch doesn't emit CS0162
            if (!F10CornerTapRetired)
            {
                _cornerTap = new Label("") { name = CornerTapName };
                StyleCornerChip(_cornerTap);
                _cornerTap.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0f)); // invisible
                _cornerTap.style.borderTopWidth = 0; _cornerTap.style.borderBottomWidth = 0;
                _cornerTap.style.borderLeftWidth = 0; _cornerTap.style.borderRightWidth = 0;
                _cornerTap.RegisterCallback<ClickEvent>(OnCornerTapped);
                _root.Add(_cornerTap);
            }

            // The console window — hidden until opened.
            _window = new VisualElement { name = WindowName };
            StyleWindow(_window);
            _root.Add(_window);

            // Header row: title + close (✕).
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6;
            var title = new Label("◆ DEV CONSOLE");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.color = new Color(0.85f, 0.92f, 1f);
            header.Add(title);
            _closeButton = new Button { text = "✕", name = CloseButtonName };
            StyleChromeButton(_closeButton);
            _closeButton.clicked += Close;
            header.Add(_closeButton);
            _window.Add(header);

            // Live metrics readout — the decked-out telemetry block.
            BuildMetricsPanel(_window);

            // Status line.
            _status = new Label("Ready.") { name = StatusName };
            _status.style.color = new Color(0.7f, 0.85f, 0.7f);
            _status.style.fontSize = 11;
            _status.style.marginTop = 4;
            _status.style.marginBottom = 4;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _window.Add(_status);

            // Scrollable action-group list (the existing code-built groups).
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            _window.Add(scroll);
            _groupList = scroll.contentContainer;

            BuildActionGroups();
            _bound = true;
        }

        // Hidden-gesture entry (mobile-safe): the dev console opens ONLY on FIVE rapid taps in the
        // invisible corner zone, so a tester won't stumble onto it with a stray tap. The streak resets
        // when the gap between taps exceeds the window. (F10 also toggles it, for desktop QA.)
        private float _lastTapTime;
        private int _tapStreak;
        private const float TapWindowSec = 2.0f;
        private const int TapsToReveal = 5;

        private void OnCornerTapped(ClickEvent _)
        {
            float now = Time.unscaledTime;
            _tapStreak = (now - _lastTapTime <= TapWindowSec) ? _tapStreak + 1 : 1;
            _lastTapTime = now;
            // DEV-TAP-DIAG (owner F8 2026-06-21 "dev tools still blocked after shop"): log EVERY tap that
            // reaches the dev UIDocument. If taps stop arriving after a shop, UITK pointer input is being
            // eaten upstream (the real block); if they arrive but the panel doesn't open, it's elsewhere.
            FlowTrace.Step("DevTapDiag", $"corner tap RECEIVED streak={_tapStreak}/{TapsToReveal} (UITK input IS reaching the dev document).");
            if (_tapStreak < TapsToReveal) return;   // not enough rapid taps yet — stay hidden
            _tapStreak = 0;
            FlowTrace.Step("UI", $"DevPanel revealed via {TapsToReveal}-tap corner gesture.");
            SetOpen(true);
        }

        /// <summary>Opens / closes the panel window.</summary>
        public void SetOpen(bool open)
        {
            FlowTrace.Step("UI", $"DevPanel toggle/click reached (DevPanelController.SetOpen open={open}, bound={_bound})");
            _isOpen = open;
            if (_window != null)
            {
                _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                // Belt-and-braces: a hidden window must never be pickable. display:None
                // already removes it from layout/picking, but flipping pickingMode too
                // guarantees a closed console returns input to the world (no trapped taps).
                _window.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
            }
            // Hide the corner chip while open (the window covers that corner anyway).
            if (_cornerTap != null)
                _cornerTap.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;

            // Single-modal arbiter (DEF-212): opening closes any other open panel;
            // closing clears our slot so nothing thinks a modal still owns the screen.
            if (_panelHandle != null)
            {
                if (open) PanelMgr.NotifyOpened(_panelHandle);
                else PanelMgr.NotifyClosed(_panelHandle);
            }

            if (open)
            {
                RefreshToggleButtons();
                UpdateMetrics();
            }

            FlowTrace.Step("UI", $"DevPanel (corner) {(open ? "shown" : "hidden")} — " +
                $"windowExists={_window != null} " +
                $"display={(_window != null ? _window.style.display.value.ToString() : "n/a")} " +
                $"picking={(_window != null ? _window.pickingMode.ToString() : "n/a")} timeScale={Time.timeScale}");
            if (open && _window == null)
                FlowTrace.Warn("UI", "DevPanel open FAILED — _window is null (UIDocument root never bound)");
        }

        /// <summary>Closes the panel.</summary>
        public void Close() => SetOpen(false);

        /// <summary>True while the console window is open. Read by the corner-tap gesture driver.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>Flips the console open/closed. Entry point for the 5-tap corner gesture (WebGL/touch).</summary>
        public void Toggle() => SetOpen(!_isOpen);

        // =====================================================================
        //  Live metrics readout — built once, refreshed ~5x/sec while open
        // =====================================================================

        /// <summary>Builds the telemetry block (label:value rows by category).</summary>
        private void BuildMetricsPanel(VisualElement parent)
        {
            _metricsPanel = new VisualElement();
            _metricsPanel.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.92f);
            SetRadius(_metricsPanel, 6);
            SetPadding(_metricsPanel, 7);
            _metricsPanel.style.marginBottom = 6;

            AddMetricSection(_metricsPanel, "PERFORMANCE");
            AddMetricRow(_metricsPanel, "fps", "FPS");
            AddMetricRow(_metricsPanel, "frame", "Frame ms");

            AddMetricSection(_metricsPanel, "WAVE / ENEMIES");
            AddMetricRow(_metricsPanel, "wave", "Wave #");
            AddMetricRow(_metricsPanel, "phase", "Phase");
            AddMetricRow(_metricsPanel, "countdown", "Countdown");
            AddMetricRow(_metricsPanel, "enemies", "Live enemies");
            AddMetricRow(_metricsPanel, "boss", "Apex boss");

            AddMetricSection(_metricsPanel, "HERO");
            AddMetricRow(_metricsPanel, "level", "Level");
            AddMetricRow(_metricsPanel, "xp", "XP → next");
            AddMetricRow(_metricsPanel, "dmg", "Dmg mult");

            AddMetricSection(_metricsPanel, "HEART");
            AddMetricRow(_metricsPanel, "heart", "HP");
            AddMetricRow(_metricsPanel, "heartstate", "State");

            AddMetricSection(_metricsPanel, "ECONOMY");
            AddMetricRow(_metricsPanel, "crystals", "Crystals");
            AddMetricRow(_metricsPanel, "food", "Food");
            AddMetricRow(_metricsPanel, "coins", "Coins");
            AddMetricRow(_metricsPanel, "materials", "Stone / Iron / Wood");
            AddMetricRow(_metricsPanel, "wisdom", "Wisdom");

            AddMetricSection(_metricsPanel, "FLAGS");
            AddMetricRow(_metricsPanel, "cheats", "Cheats");

            parent.Add(_metricsPanel);
        }

        /// <summary>Adds a small caption that heads a metric category.</summary>
        private static void AddMetricSection(VisualElement parent, string caption)
        {
            var l = new Label(caption);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize = 9;
            l.style.color = new Color(0.45f, 0.6f, 0.85f);
            l.style.marginTop = 4;
            l.style.letterSpacing = 1f;
            parent.Add(l);
        }

        /// <summary>Adds a label:value row and registers its value label under <paramref name="key"/>.</summary>
        private void AddMetricRow(VisualElement parent, string key, string caption)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;

            var cap = new Label(caption);
            cap.style.color = new Color(0.66f, 0.7f, 0.78f);
            cap.style.fontSize = 11;

            var val = new Label("—");
            val.style.color = new Color(0.95f, 0.97f, 1f);
            val.style.fontSize = 11;
            val.style.unityFontStyleAndWeight = FontStyle.Bold;
            val.style.whiteSpace = WhiteSpace.Normal;
            val.style.unityTextAlign = TextAnchor.MiddleRight;
            val.style.flexShrink = 1f;

            row.Add(cap);
            row.Add(val);
            parent.Add(row);
            _metricValues[key] = val;
        }

        /// <summary>Pulls live values from the loaded scene's systems into the readout.</summary>
        private void UpdateMetrics()
        {
            if (_metricValues.Count == 0) return;

            SetMetric("fps", _fpsSmoothed.ToString("0"));
            SetMetric("frame", (_fpsSmoothed > 0f ? 1000f / _fpsSmoothed : 0f).ToString("0.0"));

            // ── Wave ──────────────────────────────────────────────────────────
            var wm = FindFirst<WaveManager>();
            if (wm != null)
            {
                SetMetric("wave", wm.CurrentWaveId.ToString());
                SetMetric("phase", wm.Phase.ToString());
                SetMetric("countdown", wm.Phase == WavePhase.Countdown
                    ? wm.CountdownRemaining.ToString("0.0") + "s" : "—");
            }
            else { SetMetric("wave", "—"); SetMetric("phase", "no WaveManager"); SetMetric("countdown", "—"); }

            // ── Live enemies + family breakdown ────────────────────────────────
            var enemies = UnityEngine.Object.FindObjectsByType<Enemy>();
            if (enemies == null || enemies.Length == 0)
            {
                SetMetric("enemies", "0");
            }
            else
            {
                var byId = new Dictionary<string, int>();
                foreach (var e in enemies)
                {
                    if (e == null) continue;
                    string id = string.IsNullOrEmpty(e.EnemyDefId) ? "?" : e.EnemyDefId;
                    byId.TryGetValue(id, out int n);
                    byId[id] = n + 1;
                }
                var sb = new System.Text.StringBuilder();
                sb.Append(enemies.Length).Append("  (");
                bool first = true;
                foreach (var kv in byId)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(kv.Value).Append('×').Append(kv.Key);
                }
                sb.Append(')');
                SetMetric("enemies", sb.ToString());
            }

            var boss = FindFirst<DragonBoss>();
            SetMetric("boss", boss == null ? "—" : (boss.IsDead ? "dead" : "ALOFT — " + boss.BossId));

            // ── Hero ──────────────────────────────────────────────────────────
            var hero = HeroProgression.Instance;
            if (hero != null)
            {
                SetMetric("level", hero.Level.ToString());
                float pct = hero.XpToNext > 0f ? (hero.Xp / hero.XpToNext) * 100f : 0f;
                SetMetric("xp", $"{hero.Xp:0} / {hero.XpToNext:0}  ({pct:0}%)");
                SetMetric("dmg", "×" + hero.DamageMultiplier.ToString("0.00"));
            }
            else { SetMetric("level", "—"); SetMetric("xp", "no hero"); SetMetric("dmg", "—"); }

            // ── Heart ─────────────────────────────────────────────────────────
            var heart = FindHeart();
            if (heart != null)
            {
                SetMetric("heart", heart.Hp.ToString("0") + "%");
                SetMetric("heartstate", heart.State.ToString());
            }
            else { SetMetric("heart", "—"); SetMetric("heartstate", "no Heart"); }

            // ── Economy ───────────────────────────────────────────────────────
            var svc = GameStateService.Instance;
            if (svc != null && svc.State != null)
            {
                var st = svc.State;
                var r = st.Resources;
                SetMetric("crystals", r.Crystals.ToString("N0"));
                SetMetric("food", r.Stone.ToString("N0"));
                SetMetric("coins", r.Coins.ToString("N0"));
                // WO-1212: Stone reads the live slot (Resources.Stone), not the retired field.
                SetMetric("materials", $"{r.Stone} / {st.Iron} / {st.Wood}");
            }
            else { SetMetric("crystals", "—"); SetMetric("food", "—"); SetMetric("coins", "—"); SetMetric("materials", "—"); }

            var wis = DeNelle.Village.Talents.WisdomCurrencyService.Instance;
            SetMetric("wisdom", wis != null ? wis.Wisdom.ToString() : "—");

            // ── Flags ─────────────────────────────────────────────────────────
            string flags = (GodMode ? "GOD-MODE  " : "") + (InstantWinWave ? "INSTANT-WIN" : "");
            SetMetric("cheats", string.IsNullOrEmpty(flags) ? "off" : flags.Trim());
        }

        /// <summary>Sets a metric value label by key (no-op if the row is missing).</summary>
        private void SetMetric(string key, string value)
        {
            if (_metricValues.TryGetValue(key, out var lbl) && lbl != null) lbl.text = value;
        }

        // =====================================================================
        //  Inline style helpers (no USS dependency — renders in dev builds)
        // =====================================================================

        private static void StyleWindow(VisualElement w)
        {
            w.style.position = Position.Absolute;
            w.style.top = 8;
            w.style.right = 8;
            w.style.width = 360;
            w.style.maxHeight = Length.Percent(94);
            w.style.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 0.97f);   // dark glass (ElarionUiKit.GlassDeep tone)
            SetPadding(w, 10);
            SetRadius(w, 8);
            w.style.display = DisplayStyle.None;
            // T-003: gilt rune rim instead of the off-theme navy border.
            var bc = new Color(0.831f, 0.686f, 0.216f, 0.70f);
            w.style.borderLeftWidth = 1; w.style.borderRightWidth = 1;
            w.style.borderTopWidth = 1; w.style.borderBottomWidth = 1;
            w.style.borderLeftColor = bc; w.style.borderRightColor = bc;
            w.style.borderTopColor = bc; w.style.borderBottomColor = bc;
        }

        private static void StyleCornerChip(VisualElement chip)
        {
            // T-003: the old chip read as an off-theme NAVY-BLUE blob (reported 30+ times).
            // Retheme to the ElarionUi dark-glass + gold language and make it small/subtle
            // so it stops drawing the eye on the hub / Title while staying the dev entry.
            // (UITK overlay — hard-code the ElarionUi tokens; this asmdef can't reference
            //  the UGUI ElarionUi palette type, but the values match it 1:1.)
            chip.style.position = Position.Absolute;
            chip.style.bottom = 6;   // bottom-LEFT (owner): quiet corner, away from the top-right resource/settings UI
            chip.style.left = 6;
            chip.style.paddingLeft = 6; chip.style.paddingRight = 6;
            chip.style.paddingTop = 1; chip.style.paddingBottom = 1;
            // ElarionUiKit.GlassDeep (0.04,0.05,0.07) — dark glass, low alpha = subtle.
            chip.style.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 0.55f);
            // ElarionUi.Gold (0.831,0.686,0.216) ink, slightly muted so it doesn't shout.
            chip.style.color = new Color(0.831f, 0.686f, 0.216f, 0.80f);
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.fontSize = 9;
            // Thin gilt hairline rim — the dark-glass + gold treatment, not a flat blob.
            var gilt = new Color(0.831f, 0.686f, 0.216f, 0.55f);
            chip.style.borderLeftWidth = 1; chip.style.borderRightWidth = 1;
            chip.style.borderTopWidth = 1; chip.style.borderBottomWidth = 1;
            chip.style.borderLeftColor = gilt; chip.style.borderRightColor = gilt;
            chip.style.borderTopColor = gilt; chip.style.borderBottomColor = gilt;
            SetRadius(chip, 3);
        }

        private static void StyleChromeButton(Button b)
        {
            b.style.backgroundColor = new Color(0.18f, 0.20f, 0.28f);
            b.style.color = new Color(0.9f, 0.92f, 0.96f);
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.fontSize = 13;
            b.style.paddingLeft = 8; b.style.paddingRight = 8;
            b.style.paddingTop = 1; b.style.paddingBottom = 1;
            b.style.marginLeft = 0; b.style.marginRight = 0;
            SetRadius(b, 4);
        }

        private static void SetPadding(VisualElement e, float p)
        {
            e.style.paddingLeft = p; e.style.paddingRight = p;
            e.style.paddingTop = p; e.style.paddingBottom = p;
        }

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        // =====================================================================
        //  Action-group construction — built once into "dev-group-list"
        // =====================================================================

        /// <summary>
        /// Builds the grouped action buttons into the "dev-group-list" container.
        /// Mirrors the VillageHudController ability-bar build pattern.
        /// </summary>
        private void BuildActionGroups()
        {
            if (_groupList == null) return;
            _groupList.Clear();
            _toggleButtons.Clear();

            // ── RESOURCES ────────────────────────────────────────────────────
            var resources = AddGroup("Resources");
            AddButton(resources, "+100 Crystals", () => GiveCrystals(100));
            AddButton(resources, "+1000 Crystals", () => GiveCrystals(1000));
            AddButton(resources, "Load up (full base): +50k Wood/Stone/Iron", GiveBuildMaterials);
            AddButton(resources, "+5 Wisdom (talents)", () => GiveWisdom(5));
            AddButton(resources, "+25 Wisdom (talents)", () => GiveWisdom(25));
            AddButton(resources, "Respec Knight (free talents)", () => RespecHero("knight"));
            AddButton(resources, "+150 XP (hero)", () => GiveHeroXp(150f));
            AddButton(resources, "+10,000 XP (hero)", () => GiveHeroXp(10000f));
            AddButton(resources, "Level up hero", LevelHero);
            // One-click level jumps for skill-tree testing: route through the REAL level
            // path (HeroProgression.AddXp -> ApplyLevelRewards) so each gained level grants
            // its Wisdom (the skill-tree spend currency) + skill points. Reaching L5 banks
            // ~8 Wisdom, L10 ~23 — both ample to unlock + equip weapon skills (tier-1 = 1
            // Wisdom each) in the Hero skill tree. See SetHeroLevelTo().
            AddButton(resources, "Set Level 5 (+skill pts)", () => SetHeroLevelTo(5));
            AddButton(resources, "Set Level 10 (+skill pts)", () => SetHeroLevelTo(10));
            AddButton(resources, "Set Level 15 (+skill pts)", () => SetHeroLevelTo(15));
            AddTextField(resources, _levelInput.ToString(),
                v => { if (int.TryParse(v, out var n)) _levelInput = Mathf.Max(1, n); });
            AddButton(resources, "Set hero to level N", SetHeroLevel);
            AddButton(resources, "Trigger wave (skip countdown)", TriggerWave);

            // ── CITY UPGRADES (WO-430) ───────────────────────────────────────
            var upgrades = AddGroup("City upgrades (free, dev)");
            AddButton(upgrades, "Arcane Tower: cycle tier", () => CycleBuildingTier("arcane-tower"));
            AddButton(upgrades, "Armorer: cycle tier",      () => CycleBuildingTier("armorer"));
            AddButton(upgrades, "Forge: cycle tier",        () => CycleBuildingTier("forge"));
            AddButton(upgrades, "Lumber Mill: cycle tier",  () => CycleBuildingTier("lumbermill"));
            AddButton(upgrades, "Windmill: cycle tier",     () => CycleBuildingTier("windmill"));
            AddButton(upgrades, "MAX all buildings (tier 4)", () => SetAllBuildingTiers(4));
            AddButton(upgrades, "RESET all buildings (tier 0)", () => SetAllBuildingTiers(0));
            AddTextField(upgrades, _modifierJson, v => _modifierJson = v);
            AddButton(upgrades, "Apply modifier override (JSON)",
                () => DeNelle.Core.State.ModifierService.SetOverrideJson(_modifierJson));
            AddButton(upgrades, "Clear modifier override",
                () => DeNelle.Core.State.ModifierService.ClearOverride());

            // ── ENTITLEMENTS ─────────────────────────────────────────────────
            var entitlements = AddGroup("Grant pack / entitlement");
            AddTextField(entitlements, _packIdInput, v => _packIdInput = v);
            AddButton(entitlements, "Grant by id", () => GrantEntitlement(_packIdInput));
            AddButton(entitlements, "Grant ALL packs", GrantAllPacks);

            // ── HEART ────────────────────────────────────────────────────────
            var heart = AddGroup("Heart");
            AddButton(heart, "HP 100%", () => SetHeartHp(100f));
            AddButton(heart, "HP 50%", () => SetHeartHp(50f));
            AddButton(heart, "HP 10%", () => SetHeartHp(10f));
            AddButton(heart, "State: Serene", () => SetHeartState(HeartState.Serene));
            AddButton(heart, "State: Warning", () => SetHeartState(HeartState.Warning));
            AddButton(heart, "State: Critical", () => SetHeartState(HeartState.Critical));
            AddButton(heart, "State: Boss", () => SetHeartState(HeartState.Boss));

            // ── WAVES / ENEMIES ──────────────────────────────────────────────
            var waves = AddGroup("Waves & enemies");
            AddButton(waves, "Spawn enemy", SpawnEnemy);
            AddButton(waves, "Spawn Syndrath (dragon boss)", SpawnSyndrath);
            AddTextField(waves, _waveInput.ToString(),
                v => { if (int.TryParse(v, out var n)) _waveInput = Mathf.Max(1, n); });
            AddButton(waves, "Jump to wave N", () => JumpToWave(_waveInput));
            AddToggleButton(waves, "Instant-win wave: ON", "Instant-win wave: OFF",
                () => InstantWinWave, ToggleInstantWinWave);

            // ── QUEUE TIME-SKIP (owner 2026-08-04) ───────────────────────────
            // "A speed timer for testing building queues ... but NOT impact the battle
            // timer." WO-855 Phase 4 made structure builds tier-scaled (30s / 1.5m /
            // 4.5m / 13.5m / 40m / 2h, barracks up to 8h), so waiting them out is no
            // longer a testing strategy. These push TimeSource.NowUnixMs forward; they
            // are ADDITIVE (tap +10 min six times = +1h) and the last button's LABEL
            // shows the running total and clears it.
            //
            // Time.timeScale is deliberately NOT touched — that is the one thing the
            // owner ruled out, because it WOULD speed up combat.
            //
            // !! WHAT A SKIP ALSO MOVES — it is not only the build queue. TimeSource is
            // the shared wall-clock seam, so every offline-accrual consumer advances:
            //   • Obsidian queues (Builder/Train/Research)  — THE TARGET; due jobs
            //     complete on the next sweep, pending jobs cascade into free slots.
            //   • OfflineHarvestService  — PAYS that much offline node/settlement/pet
            //     income on the next claim (capped by OfflineCapSeconds).
            //   • EchoService            — fills the Echo silo, clamped to its 4h cap.
            //   • ResourceCollector      — PAYS that much into each collector's pending
            //     pool (WO-859 away catch-up), clamped by its capacity cap.
            //   • TroopRecoveryService   — HEALS wounded troops by that much (roster
            //     availability between raids; never in-battle pacing).
            // A wallet/roster that jumps after a skip is EXPECTED, not a bug. Full
            // enumeration + save-safety assessment: Core/Diagnostics/DevClock.cs header.
            //
            // !! RESET CAVEAT: a job ENQUEUED while skipped keeps a FinishMs beyond real
            // time and looks stalled for that long after a Reset. Safe order: enqueue at
            // real time → skip → let it finish → reset. Reset is FlowTrace.Warn'd.
            //
            // COMBAT IS SAFE, verified at source 2026-08-04: WaveManager, RaidScoring,
            // ATBCombatManager, BattleController, EnemyBrain and HeroHealth contain ZERO
            // TimeSource references — all engine time. Pinned by DevTimeSkipRegression.
            var queueClock = AddGroup("Queue time-skip (build/train/research)");
            AddButton(queueClock, "+1 min", () => DevTimeSkip(60d * 1000d));
            AddButton(queueClock, "+10 min", () => DevTimeSkip(600d * 1000d));
            AddButton(queueClock, "+1 hour", () => DevTimeSkip(3600d * 1000d));
            // The reset button's LABEL is the live readout of the accumulated skip
            // (same idiom AdminOverlay uses for Lock-On) — no new widget type needed.
            _timeSkipButton = AddButton(queueClock, TimeSkipLabel(), ResetDevTimeSkip);

            // ── SCENE ────────────────────────────────────────────────────────
            var scene = AddGroup("Scene jump");
            AddButton(scene, "Title", () => JumpScene(SceneRouter.Title));
            AddButton(scene, "Castle (hub)", () => JumpScene(SceneRouter.Castle));
            AddButton(scene, "Outpost1", () => JumpScene("Outpost1"));   // owner 2026-07-03: dev port for outpost testing (skips the cave walk)
            AddButton(scene, "Village", () => JumpScene(SceneRouter.Village));
            AddButton(scene, "Dungeon", () => JumpScene(SceneRouter.DungeonHealersCottage));
            AddButton(scene, "ATBBattle", () => JumpScene(SceneRouter.ATBBattle));

            // ── RAIDS (dev) ──────────────────────────────────────────────────
            // First-playable troop raid entry (WO-453 Step 4): jump straight into a
            // raid base. Auto-trains a few troops first so the deploy tray isn't empty.
            var raids = AddGroup("Raids (dev)");
            AddButton(raids, "Raid: raider camp (small)",
                () => DevEnterRaid(SceneRouter.RaidBaseRaiderCampSmall));
            AddButton(raids, "Raid: fortified garrison",
                () => DevEnterRaid(SceneRouter.RaidBaseFortifiedGarrison));
            AddButton(raids, "Raid: mage enclave",
                () => DevEnterRaid(SceneRouter.RaidBaseMageEnclave));
            AddButton(raids, "Raid: Iron Bastion",
                () => DevEnterRaid(SceneRouter.RaidBaseIronBastion));
            AddButton(raids, "MAX troop types",
                () => SetStatus(DeNelle.Village.World.Camps.DevSkipKit.PrepCastlePower()));
            AddButton(raids, "Grant Iron Bastion town (skip grind)",
                () => SetStatus(DeNelle.Village.World.Camps.DevSkipKit.GrantCapturedTownAndEnter()));

            // ── WALLET ───────────────────────────────────────────────────────
            var wallet = AddGroup("Mock wallet balance");
            AddTextField(wallet, _mockBalanceInput.ToString("0.###"),
                v => { if (double.TryParse(v, out var d)) _mockBalanceInput = Math.Max(0d, d); });
            AddButton(wallet, "Mock SOL", () => MockWallet(CurrencyKind.Sol));
            AddButton(wallet, "Mock USDC", () => MockWallet(CurrencyKind.Usdc));
            AddButton(wallet, "Mock SKR", () => MockWallet(CurrencyKind.Skr));
            AddButton(wallet, "Mock ALL rails", MockWalletAll);

            // ── UI KIT DEMO (HUD_OBSIDIAN P1 — single sanctioned insertion) ──
            // Spawns the ElarionUiKit Obsidian widget showcase (every §1 widget at 3
            // sizes, live SetValue sweep, 9/145 fill-contract proof, both factory
            // modes) so the orchestrator can screenshot-verify the kit. Kit-team
            // owned; this one AddButton line is the only DevPanel touch.
            var uiKit = AddGroup("UI Kit demo (P1)");
            AddButton(uiKit, "Toggle Obsidian kit demo", ElarionUiKitDemo.Toggle);

            // ── REALM MAP (WO-826) ───────────────────────────────────────────
            // Dev/headless door to the parchment overworld panel — same reflection-free
            // PanelRouter route the HUD Map button uses (RealmMapPanel registers the opener).
            var realmMap = AddGroup("Realm Map (WO-826)");
            AddButton(realmMap, "Open Realm Map", () =>
            {
                if (!DeNelle.Core.UI.PanelRouter.Open(DeNelle.Core.UI.PanelId.RealmMap))
                    Debug.LogWarning("[DevPanel] PanelId.RealmMap has no registered opener (no hero scene?)");
            });

            // ── CHEATS ───────────────────────────────────────────────────────
            var cheats = AddGroup("Cheats");
            AddToggleButton(cheats, "God-mode: ON", "God-mode: OFF",
                () => GodMode, ToggleGodMode);

            // (WO-682: the "Feature flags" group's only row — the ff.strategicplacement
            // Town/Defenses/Walls toggle — was removed with the flag; strategic placement
            // is always on. New preview flags belong HERE, not OwnerDevToolsOverlay.)

            // AutoPilot (QA bot) — runs the autonomous playtest driver in-editor
            // with quitOnDone:false so a manual run never closes the editor.
            var autopilot = AddGroup("AutoPilot (QA bot)");
            AddButton(autopilot, "Run AutoPilot", RunAutoPilot);

            // ── ANIMATION (feel tuning + KayKit proof) ───────────────────────
            // Stride-polish runtime knob (HeroLocomotionCadence, PlayerPrefs
            // "anim.runCadence"; 1.5 = the baked default) + the KayKit knight
            // side-by-side animation proof (proof-before-decision; editor-only
            // load of the gitignored pack, §4-guarded no-op when absent).
            var animFeel = AddGroup("Animation (feel)");
            AddButton(animFeel, "Spawn KayKit Knight (anim proof)", KayKitAnimProof.SpawnBesideHero);
            AddButton(animFeel, "Despawn KayKit proof", KayKitAnimProof.Despawn);
            AddButton(animFeel, "Run cadence -0.1", () => NudgeRunCadence(-0.1f));
            AddButton(animFeel, "Run cadence +0.1", () => NudgeRunCadence(+0.1f));
            AddButton(animFeel, "Run cadence reset (1.5)",
                () => SetRunCadence(HeroLocomotionCadence.BakedNetRunCadence));

            RefreshToggleButtons();

            int wiredButtons = _groupList != null
                ? _groupList.Query<Button>().ToList().Count : 0;
            FlowTrace.Step("UI", $"DevPanel action groups built — wired {wiredButtons} buttons");
        }

        // =====================================================================
        //  Actions — QUEUE TIME-SKIP (owner 2026-08-04)
        // =====================================================================
        // Drives DeNelle.Village.TimeSource's dev skip (stored in Core's DevClock so
        // the live Settings→Dev-tools panel can reach it too). Additive; forward-only;
        // FlowTrace'd on every change; compiled out of release twice over (this whole
        // assembly is defineConstraint'd, and DevClock's backing field is #if-gated).

        /// <summary>Live readout of the accumulated queue-clock skip, used as a button label.</summary>
        private static string TimeSkipLabel()
        {
            return DeNelle.Core.Diagnostics.DevClock.SkipMs > 0d
                ? "Queue clock: +" + DeNelle.Core.Diagnostics.DevClock.DescribeCurrent() + "  (TAP TO RESET)"
                : "Queue clock: real time (nothing to reset)";
        }

        /// <summary>Pushes the queue wall-clock forward by <paramref name="deltaMs"/>.</summary>
        private void DevTimeSkip(double deltaMs)
        {
            double total = DeNelle.Village.TimeSource.AddDevSkipMs(deltaMs);
            if (_timeSkipButton != null) _timeSkipButton.text = TimeSkipLabel();
            SetStatus($"Queue clock +{DeNelle.Core.Diagnostics.DevClock.Describe(deltaMs)} " +
                      $"(total +{DeNelle.Core.Diagnostics.DevClock.Describe(total)}). Build/train/research jobs " +
                      "due within it complete on the next sweep. Combat + wave timers UNAFFECTED. " +
                      "Offline income + troop recovery also advance — expected, not a bug.");
        }

        /// <summary>Clears the accumulated queue-clock skip (back to the device clock).</summary>
        private void ResetDevTimeSkip()
        {
            double cleared = DeNelle.Village.TimeSource.ResetDevSkip();
            if (_timeSkipButton != null) _timeSkipButton.text = TimeSkipLabel();
            SetStatus(cleared > 0d
                ? $"Queue clock reset (cleared +{DeNelle.Core.Diagnostics.DevClock.Describe(cleared)}). Accrual " +
                  "stamps self-heal on the next tick; a job ENQUEUED while skipped keeps its skewed finish time."
                : "Queue clock was already at real time — nothing to reset.");
        }

        /// <summary>Nudges the hero locomotion-cadence knob (stride-polish, 2026-07-02).</summary>
        private static void NudgeRunCadence(float delta)
            => SetRunCadence(HeroLocomotionCadence.RunCadence + delta);

        /// <summary>Sets + logs the hero locomotion-cadence knob ("anim.runCadence").</summary>
        private static void SetRunCadence(float value)
        {
            HeroLocomotionCadence.RunCadence = value;
            Debug.Log($"[DevPanel] anim.runCadence = {HeroLocomotionCadence.RunCadence:0.00} " +
                      "(net run-clip cadence multiplier; 1.5 = baked default, applies live in locomotion)");
        }

        /// <summary>Adds a captioned action group and returns its button row.</summary>
        private VisualElement AddGroup(string caption)
        {
            var group = new VisualElement { name = $"dev-group-{caption}" };
            group.AddToClassList(GroupClass);
            group.style.marginTop = 6;

            var captionLabel = new Label(caption);
            captionLabel.AddToClassList(GroupCaptionClass);
            captionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            captionLabel.style.fontSize = 10;
            captionLabel.style.color = new Color(0.55f, 0.68f, 0.9f);
            captionLabel.style.marginBottom = 2;
            group.Add(captionLabel);

            var row = new VisualElement { name = "dev-group-row" };
            row.AddToClassList(GroupRowClass);
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            group.Add(row);

            _groupList.Add(group);
            return row;
        }

        /// <summary>Adds a plain action button to a group row.</summary>
        private Button AddButton(VisualElement row, string label, Action onClick)
        {
            var button = new Button { text = label };
            button.AddToClassList(ActionButtonClass);
            button.style.backgroundColor = new Color(0.16f, 0.19f, 0.27f);
            button.style.color = new Color(0.88f, 0.91f, 0.96f);
            button.style.fontSize = 11;
            button.style.paddingLeft = 7; button.style.paddingRight = 7;
            button.style.paddingTop = 3; button.style.paddingBottom = 3;
            button.style.marginLeft = 2; button.style.marginRight = 2;
            button.style.marginTop = 2; button.style.marginBottom = 2;
            button.style.borderTopLeftRadius = 4; button.style.borderTopRightRadius = 4;
            button.style.borderBottomLeftRadius = 4; button.style.borderBottomRightRadius = 4;
            button.clicked += () =>
            {
                FlowTrace.Step("UI", $"DevPanel click reached — action '{label}'");
                try { onClick?.Invoke(); }
                catch (Exception ex)
                {
                    Debug.LogError($"[DevPanelController] Action '{label}' threw: {ex}");
                    SetStatus($"'{label}' failed — {ex.Message}");
                }
            };
            row.Add(button);
            return button;
        }

        /// <summary>
        /// Adds a button whose label + colour reflect a boolean toggle. The
        /// <paramref name="onAction"/> flips the underlying flag.
        /// </summary>
        private void AddToggleButton(
            VisualElement row, string onLabel, string offLabel,
            Func<bool> isOn, Action onAction)
        {
            var button = AddButton(row, offLabel, () =>
            {
                onAction?.Invoke();
                RefreshToggleButtons();
            });
            _toggleButtons.Add(new ToggleBinding
            {
                Button = button,
                OnLabel = onLabel,
                OffLabel = offLabel,
                IsOn = isOn,
            });
        }

        /// <summary>Adds an inline text-entry field; pushes edits through <paramref name="onChange"/>.</summary>
        private void AddTextField(VisualElement row, string initial, Action<string> onChange)
        {
            var field = new TextField { value = initial ?? string.Empty };
            field.AddToClassList(TextFieldClass);
            field.style.minWidth = 60;
            field.style.marginLeft = 2; field.style.marginRight = 2;
            field.style.marginTop = 2; field.style.marginBottom = 2;
            field.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            row.Add(field);
        }

        /// <summary>Re-syncs every toggle button's label + on/off class to its flag.</summary>
        private void RefreshToggleButtons()
        {
            foreach (var t in _toggleButtons)
            {
                if (t.Button == null) continue;
                bool on = t.IsOn != null && t.IsOn();
                t.Button.text = on ? t.OnLabel : t.OffLabel;
                t.Button.EnableInClassList(ToggleOnClass, on);
                t.Button.EnableInClassList(ToggleOffClass, !on);
            }
        }

        // =====================================================================
        //  Actions — RESOURCES
        // =====================================================================

        /// <summary>Adds <paramref name="amount"/> crystals — routed through
        /// EconomyService.GrantSpendable so the grant lands in the economy AND fires
        /// EconomyService.OnChanged (which HeartHudBridge uses to refresh the on-screen
        /// resource bar). Writing GameState directly (as this used to) only raised
        /// GameStateService.ResourcesChanged — which reaches the HUD bar only IF the
        /// EconomyService→GameState bridge happens to be attached; the GrantSpendable
        /// path is the single correct, bootstrap-robust grant. Falls back to a direct
        /// GameState write only if the economy service hasn't bootstrapped yet.</summary>
        // WO-430 — dev: cycle a building's tier 0->max->0 (free, ignores cost), persist + recompute.
        private void CycleBuildingTier(string buildingId)
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null) { SetStatus("No game state."); return; }
            if (svc.State.BuildingTiers == null)
                svc.State.BuildingTiers = new System.Collections.Generic.Dictionary<string, int>();

            int cur = DeNelle.Core.State.ModifierService.TierOf(buildingId);
            int max = DeNelle.Core.State.BuildingTierCatalog.MaxTier(buildingId);
            int next = (max <= 0 || cur >= max) ? 0 : cur + 1;
            svc.State.BuildingTiers[buildingId] = next;
            svc.Save();
            DeNelle.Core.State.ModifierService.Recompute();
            SetStatus($"{buildingId} -> tier {next}/{max}.");
        }

        // WO-430 — dev: set ALL upgradable buildings to a tier (clamped to each one's max).
        private void SetAllBuildingTiers(int tier)
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null) { SetStatus("No game state."); return; }
            if (svc.State.BuildingTiers == null)
                svc.State.BuildingTiers = new System.Collections.Generic.Dictionary<string, int>();

            foreach (var b in DeNelle.Core.State.BuildingTierCatalog.All)
            {
                if (b == null || b.Id == null) continue;
                int max = DeNelle.Core.State.BuildingTierCatalog.MaxTier(b.Id);
                svc.State.BuildingTiers[b.Id] = Mathf.Clamp(tier, 0, max);
            }
            svc.Save();
            DeNelle.Core.State.ModifierService.Recompute();
            SetStatus($"All buildings -> tier {tier}.");
        }

        private void GiveCrystals(int amount)
        {
            var eco = EconomyService.Instance;
            if (eco != null)
            {
                eco.GrantSpendable(crystals: amount);
                int now = 0;
                var svc = GameStateService.Instance;
                if (svc != null && svc.State != null) now = svc.State.Resources.Crystals;
                PingHud();
                SetStatus($"Gave {amount} crystals — now {now}.");
                return;
            }

            // Fallback: economy service not alive yet — at least fill the GameState wallet.
            var state = RequireState("give crystals");
            if (state == null) return;
            var r = state.Resources;
            r.Crystals += amount;
            state.Resources = r;
            SaveAndNotifyResources();
            PingHud();
            SetStatus($"Gave {amount} crystals — now {r.Crystals}.");
        }

        /// <summary>Grants Wisdom (hero talent currency) for fast skill-tree testing.</summary>
        private void GiveWisdom(int amount)
        {
            var svc = DeNelle.Village.Talents.WisdomCurrencyService.Instance;
            if (svc == null) { SetStatus("WisdomCurrencyService not in scene yet."); return; }
            svc.Grant(amount);
            SetStatus($"Gave {amount} Wisdom — now {svc.Wisdom}.");
        }

        /// <summary>Respecs a hero's talents (refunds Wisdom + frees nodes) so the
        /// node-graph plan→CONFIRM flow can be felt-tested on a clean slate.</summary>
        private void RespecHero(string heroSlug)
        {
            var svc = DeNelle.Village.Talents.WisdomCurrencyService.Instance;
            if (svc == null) { SetStatus("WisdomCurrencyService not in scene yet."); return; }
            svc.RespecHero(heroSlug);
            SetStatus($"Respec '{heroSlug}' — nodes freed, Wisdom now {svc.Wisdom}.");
        }

        /// <summary>Grants the hero raw XP for fast level/progression testing.</summary>
        private void GiveHeroXp(float amount)
        {
            var hp = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.HeroProgression>();
            if (hp == null) { SetStatus("HeroProgression not in scene yet."); return; }
            int levels = hp.AddXp(amount);
            if (levels > 0)
                DeNelle.Village.DamageNumberSpawner.SpawnLabel(
                    $"LEVEL UP!  Lv.{hp.Level}", hp.WorldPosition, new Color(0.45f, 1f, 0.55f, 1f), 1.4f);
            SetStatus($"Gave {amount:0} XP — hero is Lv.{hp.Level}"
                      + (levels > 0 ? $" (+{levels} level)" : "") + ".");
        }

        /// <summary>Forces one hero level (also grants its Wisdom + stat bonus).</summary>
        private void LevelHero()
        {
            var hp = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.HeroProgression>();
            if (hp == null) { SetStatus("HeroProgression not in scene yet."); return; }
            hp.AddXp(hp.XpToNext + 1f);
            DeNelle.Village.DamageNumberSpawner.SpawnLabel(
                $"LEVEL UP!  Lv.{hp.Level}", hp.WorldPosition, new Color(0.45f, 1f, 0.55f, 1f), 1.4f);
            SetStatus($"Hero leveled to Lv.{hp.Level}.");
        }

        /// <summary>Levels the hero up to the typed target level (repeated AddXp until reached).</summary>
        private void SetHeroLevel()
        {
            SetHeroLevelTo(Mathf.Max(1, _levelInput));
        }

        /// <summary>
        /// Sets the hero to <paramref name="target"/> by feeding XP through the REAL
        /// level path (HeroProgression.AddXp). Each level crossed runs ApplyLevelRewards,
        /// which grants that level's Wisdom (the skill-tree spend currency) + a SkillSystem
        /// point — so after this the owner can open the Hero skill tree and spend Wisdom to
        /// unlock + equip weapon skills (abilityId -> W/E/R slot via HeroLoadout). One-click
        /// "Set Level 5/10" buttons call this. No-op if already at/above the target.
        /// </summary>
        private void SetHeroLevelTo(int target)
        {
            var hp = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.HeroProgression>();
            if (hp == null) { SetStatus("HeroProgression not in scene yet."); return; }
            target = Mathf.Max(1, target);
            int guard = 0;
            while (hp.Level < target && guard++ < 500)
                hp.AddXp(hp.XpToNext + 1f);

            int wisdom = DeNelle.Village.Talents.WisdomCurrencyService.Instance?.Wisdom ?? 0;
            int skillPts = DeNelle.Core.Progression.SkillSystem.Instance?.AvailablePoints ?? 0;
            DeNelle.Village.DamageNumberSpawner.SpawnLabel(
                $"LEVEL {hp.Level}", hp.WorldPosition, new Color(0.45f, 1f, 0.55f, 1f), 1.4f);
            SetStatus($"Set hero to Lv.{hp.Level} (target {target}) — {wisdom} Wisdom + " +
                      $"{skillPts} skill pts to spend in the skill tree.");
        }

        /// <summary>Skips the prep countdown and starts the wave now (WaveManager.ForceBeginNextWave).</summary>
        private void TriggerWave()
        {
            var wm = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.WaveManager>();
            if (wm == null) { SetStatus("WaveManager not in scene yet."); return; }
            wm.ForceBeginNextWave();
            SetStatus("Triggered the wave — countdown skipped.");
        }

        /// <summary>Tops up the gathered build materials + the four harvestables
        /// (Wood/Food/Iron/Crystals) and the Magic tech axis (DEF-121) for testing
        /// the resource-building upgrade loop incl. the Magic-gated arcane tier.</summary>
        private void GiveBuildMaterials()
        {
            var state = RequireState("give materials");
            if (state == null) return;

            // Wood/Iron live in TWO spendable wallets that don't sync: the EconomyService
            // in-session pool (the gear shop + the HUD resource bar) and GameState.Wood/Iron
            // (the structure-upgrade flow's ResourceLedger). Writing GameState directly
            // (as this used to) filled only the upgrade wallet, so the shop + HUD bar saw
            // nothing. Route Wood/Iron through EconomyService.GrantSpendable so ONE dev
            // action lands them in BOTH — Food/Crystals are GameState-backed inside it.
            var eco = EconomyService.Instance;
            if (eco != null)
            {
                // WO-857 Phase F: this is the "fill the tank so I can test something else" button —
                // it must NOT be storage-gated, or a dev top-up silently becomes baseCap and the
                // toast the real economy owes the player fires on a tool action. Uncapped by intent.
                eco.GrantSpendableUncapped(wood: 50000, stone: 25000, iron: 50000, crystals: 25000);
                eco.AddCoins(50000);   // Gold — the shop/sell wallet (GameState.Resources.Coins); raises ResourcesChanged so the HUD gold readout updates.
            }
            else
            {
                // Fallback if the economy service hasn't bootstrapped yet: at least fill
                // the persisted GameState wallet the upgrade flow + HUD-via-GameState read.
                state.Wood += 50000;
                state.Iron += 50000;
                var bal0 = state.Resources;
                bal0.Stone += 25000;
                bal0.Crystals += 25000;
                bal0.Coins += 50000;   // Gold
                state.Resources = bal0;
            }

            // WO-1212: the `state.Stone += 50000` dev top-up is REMOVED - it filled the
            // retired balance nothing spends. Stone the player can actually spend is granted
            // as `food` in both branches above (25000).
            state.Magic += 100;     // Magic tech axis (DEF-121) — building-upgrade gate
            SaveAndNotifyResources();
            PingHud();              // force the on-screen resource bar to populate immediately

            // dev-grant-both-wallets fix: trace the granted amounts + resulting BOTH-store
            // totals — the in-session pool (EconomyService.Snapshot, shop/HUD wallet) and
            // GameState.Wood/Iron (the structure-upgrade flow's wallet) — so a dev grant is
            // traceable end-to-end and proves both wallets filled from one action.
            {
                var ecoSnap = EconomyService.Instance;
                if (ecoSnap != null)
                {
                    var s = ecoSnap.Snapshot;
                    FlowTrace.Step("Eco",
                        $"DevGrant (DevPanel) +W50000 F25000 I50000 C25000 -> " +
                        $"pool W{s.Wood} I{s.Iron} F{s.Stone} C{s.Crystals} | " +
                        $"GameState W{state.Wood} I{state.Iron} F{state.Resources.Stone} C{state.Resources.Crystals}");
                }
                else
                {
                    FlowTrace.Step("Eco",
                        $"DevGrant (DevPanel, GameState-fallback) -> " +
                        $"GameState W{state.Wood} I{state.Iron} F{state.Resources.Stone} C{state.Resources.Crystals}");
                }
            }

            SetStatus($"Topped up — Gold {state.Resources.Coins}, Wood {state.Wood}, Food {state.Resources.Stone}, " +
                      $"Iron {state.Iron}, Crystals {state.Resources.Crystals}, Magic {state.Magic}.");
        }

        // =====================================================================
        //  Actions — ENTITLEMENTS
        // =====================================================================

        /// <summary>
        /// Grants a pack / entitlement by id — records the SKU in OwnedItemIds
        /// (the entitlement key) and, if the id is a known pack, also applies its
        /// economy top-up and its cosmetic SKUs. Mirrors PackStore.ApplyPackContents.
        /// </summary>
        private void GrantEntitlement(string id)
        {
            var state = RequireState("grant entitlement");
            if (state == null) return;

            if (string.IsNullOrWhiteSpace(id))
            {
                SetStatus("Grant skipped — enter a pack / entitlement id.");
                return;
            }
            id = id.Trim();

            RecordOwned(state, id);

            var pack = PackCatalog.Find(id);
            if (pack != null)
            {
                ApplyPackEconomy(state, pack);
                if (pack.Contents != null && pack.Contents.Cosmetics != null)
                    foreach (var sku in pack.Contents.Cosmetics)
                        RecordOwned(state, sku);
                SetStatus($"Granted pack '{pack.Name}' ({id}) + contents.");
            }
            else
            {
                SetStatus($"Granted entitlement '{id}' (not a known pack — recorded as owned only).");
            }

            GameStateService.Instance.Save();
            GameStateService.Instance.PlayerChanged.Invoke();
        }

        /// <summary>Grants every pack in the canonical catalogue.</summary>
        private void GrantAllPacks()
        {
            var state = RequireState("grant all packs");
            if (state == null) return;

            int count = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (pack == null || string.IsNullOrEmpty(pack.Sku)) continue;
                RecordOwned(state, pack.Sku);
                ApplyPackEconomy(state, pack);
                if (pack.Contents != null && pack.Contents.Cosmetics != null)
                    foreach (var sku in pack.Contents.Cosmetics)
                        RecordOwned(state, sku);
                count++;
            }
            GameStateService.Instance.Save();
            GameStateService.Instance.PlayerChanged.Invoke();
            GameStateService.Instance.ResourcesChanged.Invoke();
            SetStatus($"Granted all {count} packs + contents.");
        }

        private static void ApplyPackEconomy(GameState state, PackDef pack)
        {
            var econ = pack.Contents != null ? pack.Contents.Economy : null;
            if (econ == null) return;
            var r = state.Resources;
            r.Crystals += econ.Crystals;
            r.Stone += econ.Stone;
            r.Coins += econ.Coins;
            state.Resources = r;
        }

        private static void RecordOwned(GameState state, string sku)
        {
            if (state.OwnedItemIds == null) state.OwnedItemIds = new List<string>();
            if (!string.IsNullOrEmpty(sku) && !state.OwnedItemIds.Contains(sku))
                state.OwnedItemIds.Add(sku);
        }

        // =====================================================================
        //  Actions — HEART
        // =====================================================================

        /// <summary>Sets the Heart's HP (0-100) on the HeartController in the loaded scene.</summary>
        private void SetHeartHp(float hp)
        {
            var heart = FindHeart();
            if (heart == null)
            {
                SetStatus("No HeartController in scene — load the Village scene first.");
                return;
            }
            heart.SetHp(hp);
            PushHeartHpToHud(heart);
            SetStatus($"Heart HP set to {hp:0}.");
        }

        /// <summary>Sets the Heart's threat state on the HeartController in the loaded scene.</summary>
        private void SetHeartState(HeartState heartState)
        {
            var heart = FindHeart();
            if (heart == null)
            {
                SetStatus("No HeartController in scene — load the Village scene first.");
                return;
            }
            heart.SetState(heartState);
            SetStatus($"Heart state set to {heartState}.");
        }

        /// <summary>Pushes the Heart's HP into the VillageHud if one is present (display sync).</summary>
        private static void PushHeartHpToHud(HeartController heart)
        {
            var hud = FindFirst<VillageHudController>();
            if (hud != null) hud.SetHeartHp(heart.Hp, 100f);
        }

        // =====================================================================
        //  Actions — WAVES / ENEMIES
        // =====================================================================

        /// <summary>
        /// Asks the WaveManager in the scene to begin its loop at the chosen wave.
        /// WaveManager.BeginLoop re-enters the countdown for that wave; the
        /// integrator can also expose a direct jump (see the integrator notes).
        /// </summary>
        private void JumpToWave(int wave)
        {
            var manager = FindFirst<WaveManager>();
            if (manager == null)
            {
                SetStatus("No WaveManager in scene — load the Village scene first.");
                return;
            }
            // _startWave is a serialized private field on WaveManager; the public
            // seam is BeginLoop(), which re-enters the loop at the manager's
            // configured start wave. To jump to an ARBITRARY wave the integrator
            // exposes WaveManager.JumpToWave (see the integrator notes / port
            // note). Until then this restarts the loop, the safe public call.
            manager.BeginLoop().Forget();
            SetStatus($"Wave loop (re)started — target wave {wave}. " +
                      "Wire WaveManager.JumpToWave for an arbitrary jump (see port note).");
        }

        /// <summary>
        /// Spawns one extra enemy by restarting / nudging the wave loop. The
        /// WaveManager owns enemy instantiation from its prefab + catalogue, so
        /// the dev panel asks IT to spawn rather than instantiating a bare Enemy
        /// (which would have no EnemyDef / NavMesh config).
        /// </summary>
        private void SpawnEnemy()
        {
            var manager = FindFirst<WaveManager>();
            if (manager == null)
            {
                SetStatus("No WaveManager in scene — load the Village scene first.");
                return;
            }
            // Enemy spawning is internal to WaveManager's batch loop. The clean
            // dev seam is a WaveManager.DevSpawnOne() the integrator adds (see
            // the port note). Until then, kicking the loop is the public path.
            manager.BeginLoop().Forget();
            SetStatus("Asked WaveManager to run a wave. " +
                      "Wire WaveManager.DevSpawnOne for a single-enemy spawn (see port note).");
        }

        /// <summary>
        /// Spawns Syndrath the Devourer — the apex dragon boss. Instantiates the
        /// DragonBoss prefab assigned in the inspector (or finds an existing one)
        /// over the Heart and Configures it. Falls back with a clear status when
        /// no prefab is wired.
        /// </summary>
        private void SpawnSyndrath()
        {
            var heart = FindHeart();
            Transform anchor = heart != null ? heart.transform : null;

            var existing = FindFirst<DragonBoss>();
            if (existing != null && !existing.IsDead)
            {
                SetStatus($"Syndrath '{existing.BossId}' is already aloft.");
                return;
            }

            if (_dragonBossPrefab == null)
            {
                SetStatus("No DragonBoss prefab wired on DevPanelController — " +
                          "assign Boss_Dragon in the inspector to spawn Syndrath.");
                return;
            }

            // #66: match the lowered dragon _orbitHeight (22 -> 10) so the dev-spawned, scale-0.3
            // dragon drops into frame instead of far overhead.
            Vector3 spawnPos = (anchor != null ? anchor.position : Vector3.zero)
                               + new Vector3(0f, 10f, 0f);
            var dragon = Instantiate(_dragonBossPrefab, spawnPos, Quaternion.identity);
            dragon.Configure("dev-syndrath", anchor);
            SetStatus("Spawned Syndrath the Devourer over the Heart.");
        }

        /// <summary>Toggles the DEV instant-win-wave flag.</summary>
        private void ToggleInstantWinWave()
        {
            InstantWinWave = !InstantWinWave;
            InstantWinWaveChanged?.Invoke(InstantWinWave);
            SetStatus($"Instant-win-wave {(InstantWinWave ? "ENABLED" : "disabled")}.");
        }

        // =====================================================================
        //  Actions — SCENE
        // =====================================================================

        /// <summary>Jumps to a canonical scene through the SceneRouter.</summary>
        private void JumpScene(string sceneName)
        {
            SetStatus($"Loading scene '{sceneName}'…");
            SceneRouter.LoadScene(sceneName);
        }

        /// <summary>
        /// DEV raid entry (WO-453 Step 4, first-playable): if the army has no deployable
        /// troops, auto-trains a small starter force (footman x4, archer x3 via the same
        /// ArmyStorage/EconomyService seam the Barracks uses) so the deploy tray isn't
        /// empty, then routes into the raid via the shared SceneRouter.GoRaid contract.
        /// Throwaway QA hook — never ships (whole file is dev-only).
        /// </summary>
        private void DevEnterRaid(string sceneName)
        {
            var svc = GameStateService.Instance;
            var army = svc != null && svc.State != null ? svc.State.Army : null;
            if (army != null)
            {
                int deployable = 0;
                if (army.Owned != null)
                    foreach (var t in army.Owned)
                        if (t != null && t.IsDeployable) deployable++;

                if (deployable == 0)
                {
                    // Make sure a dev raid always has bodies: top up resources, then train.
                    var eco = EconomyService.Instance;
                    if (eco != null) eco.GrantSpendableUncapped(wood: 5000, stone: 5000, iron: 5000);   // WO-857: dev raid setup, not player income — never storage-gated
                    int f = DeNelle.Village.TroopDialogueCommands.Train("troop-footman", 4);
                    int a = DeNelle.Village.TroopDialogueCommands.Train("troop-archer", 3);
                    SetStatus($"Auto-trained {f} footman + {a} archer for the raid.");
                }
            }

            SetStatus($"Entering raid '{sceneName}'…");
            SceneRouter.GoRaid(sceneName);
        }

        // =====================================================================
        //  Actions — WALLET
        // =====================================================================

        /// <summary>
        /// Sets a mock balance for one currency rail. Routes through
        /// <see cref="DevWalletProbe"/> — a dev-only StubWalletProvider seam — so
        /// the panel never has to fabricate a wallet flow itself.
        /// </summary>
        private void MockWallet(CurrencyKind currency)
        {
            DevWalletProbe.SetMockBalance(currency, _mockBalanceInput);
            SetStatus($"Mock {currency} balance set to {_mockBalanceInput:0.###} " +
                      "(applies to a DevWalletProbe-backed WalletService).");
        }

        /// <summary>Sets the mock balance on all three rails (SOL / USDC / SKR).</summary>
        private void MockWalletAll()
        {
            DevWalletProbe.SetMockBalance(CurrencyKind.Sol, _mockBalanceInput);
            DevWalletProbe.SetMockBalance(CurrencyKind.Usdc, _mockBalanceInput);
            DevWalletProbe.SetMockBalance(CurrencyKind.Skr, _mockBalanceInput);
            SetStatus($"Mock balance set to {_mockBalanceInput:0.###} on SOL / USDC / SKR.");
        }

        // =====================================================================
        //  Actions — CHEATS
        // =====================================================================

        /// <summary>Toggles the DEV god-mode flag.</summary>
        private void ToggleGodMode()
        {
            GodMode = !GodMode;
            GodModeChanged?.Invoke(GodMode);
            SetStatus($"God-mode {(GodMode ? "ENABLED" : "disabled")}.");
        }

        // =====================================================================
        //  Actions — AUTOPILOT (QA bot)
        // =====================================================================

        /// <summary>
        /// Spawns the autonomous playtest bot (<see cref="AutoPilotDriver"/>) for a
        /// MANUAL in-editor run: quitOnDone:false so it never closes the editor.
        /// The headless --autopilot launch uses AutoPilotInstaller (quitOnDone:true).
        /// </summary>
        private void RunAutoPilot()
        {
            if (UnityEngine.Object.FindAnyObjectByType<AutoPilotDriver>() != null)
            {
                SetStatus("AutoPilot already running.");
                return;
            }
            var go = new GameObject("~AutoPilotDriver (manual)");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var driver = go.AddComponent<AutoPilotDriver>();
            driver.Begin(quitOnDone: false);
            SetStatus("AutoPilot started (manual run — editor stays open).");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>Returns the live GameState, or null (with a status) when no service exists.</summary>
        private GameState RequireState(string action)
        {
            var service = GameStateService.Instance;
            if (service == null || service.State == null)
            {
                SetStatus($"Cannot {action} — no GameStateService in the scene.");
                return null;
            }
            return service.State;
        }

        /// <summary>Saves the state and raises the resources-changed event.</summary>
        private static void SaveAndNotifyResources()
        {
            var service = GameStateService.Instance;
            if (service == null) return;
            service.Save();
            service.ResourcesChanged.Invoke();
        }

        /// <summary>
        /// Robustly refreshes the on-screen top resource bar immediately after a dev
        /// grant. The normal path is EconomyService.OnChanged → HeartHudBridge →
        /// VillageHudController.SetResources, but that depends on the bridge being
        /// present + subscribed in the loaded scene. The dev panel can reach BOTH the
        /// EconomyService (its live wallet snapshot) and the VillageHudController
        /// directly (DevTools may reference gameplay modules), so it pushes the wallet
        /// to the bar itself — guaranteeing the bar populates the moment a grant lands,
        /// even in a scene (e.g. the castle hub) where the bridge race could lag.
        /// </summary>
        private static void PingHud()
        {
            var hud = FindFirst<VillageHudController>();
            if (hud == null) return;

            var eco = EconomyService.Instance;
            if (eco != null)
            {
                // HUD signature is SetResources(wood, iron, food, gems).
                var snap = eco.Snapshot;
                hud.SetResources(snap.Wood, snap.Iron, snap.Stone, snap.Crystals);
                hud.SetCrystals(snap.Crystals);
                return;
            }

            // No economy service — fall back to the persisted GameState crystal count.
            var svc = GameStateService.Instance;
            if (svc != null && svc.State != null)
                hud.SetCrystals(svc.State.Resources.Crystals);
        }

        /// <summary>Finds the HeartController in the loaded scene, or null.</summary>
        private static HeartController FindHeart() => FindFirst<HeartController>();

        /// <summary>First active object of type T in the loaded scene, or null.</summary>
        private static T FindFirst<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindAnyObjectByType<T>();
        }

        /// <summary>Writes a line to the status label (and the console).</summary>
        private void SetStatus(string message)
        {
            if (_status != null) _status.text = message;
            Debug.Log($"[DevPanel] {message}");
        }

        /// <summary>True once DevPanel.uxml has been bound and the groups built.</summary>
        public bool IsBound => _bound;
    }
}

#endif // DEVELOPMENT_BUILD || UNITY_EDITOR

// =============================================================================
// INTEGRATOR NOTES — wiring the dev console (DEV builds only).
// -----------------------------------------------------------------------------
// This whole file is compiled out of a release build. To use it in the Editor
// or a Development build:
//
//   1. Either let DevBootstrap.cs spawn the panel automatically (it has a
//      [RuntimeInitializeOnLoadMethod] bootstrap, also #if-gated), OR add a
//      GameObject with a UIDocument (Source Asset = DevPanel.uxml) and this
//      DevPanelController component beside it. The bootstrap is the no-touch
//      path — nothing to add per scene.
//
//   2. Hotkey: F1 toggles the console (configurable on the component). The
//      on-screen "DEV" corner chip toggles it too (touch-friendly).
//
//   3. "Spawn Syndrath" needs the Boss_Dragon prefab assigned to
//      DevPanelController._dragonBossPrefab.
//
//   4. The two cheat flags are READ by gameplay (DevTools must not reach into
//      a gameplay damage path). In a DEV build the integrator can gate, e.g.:
//        #if DEVELOPMENT_BUILD || UNITY_EDITOR
//        if (DeNelle.DevTools.DevPanelController.GodMode) return; // skip damage
//        #endif
//      and listen to GodModeChanged / InstantWinWaveChanged.
//
//   5. JUMP-to-wave / SPAWN-enemy: WaveManager keeps enemy spawning + the
//      start-wave internal. For an ARBITRARY wave jump and a single-enemy
//      spawn the integrator adds two small public dev seams to WaveManager,
//      both #if-gated, e.g.:
//        #if DEVELOPMENT_BUILD || UNITY_EDITOR
//        public void DevJumpToWave(int wave) { EnterCountdown(Mathf.Max(1, wave)); }
//        public void DevSpawnOne(string enemyType) { /* SpawnBatch one */ }
//        #endif
//      Until those exist the panel falls back to BeginLoop() and says so in
//      its status line. See docs/port-notes/dev-panel.md.
//
//   6. MOCK wallet: the panel sets balances on DevWalletProbe. For them to be
//      visible, the wallet-using screen must build its WalletService over a
//      DevWalletProbe (a DEV-only IWalletProvider) — see DevWalletProbe.cs.
// =============================================================================
