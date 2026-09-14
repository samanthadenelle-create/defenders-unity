// =============================================================================
// AdminOverlay — owner-only debug controls. Trigger waves, give crystals,
// reset the save, toggle the cold open, etc.
// -----------------------------------------------------------------------------
// Owner-gate: matches the wallet address bound on GameStateService.State
// against AdminOverlay.OwnerWalletAddress. Until the owner's address is
// pasted in (or until the Connect Wallet flow lands in Week 7), the overlay
// is reachable via the debug chord Ctrl+Shift+A.
//
// All actions call through reflection so the HUD asmdef stays decoupled from
// DeNelle.Village / DeNelle.Core.State (which already do reference Core).
// =============================================================================

using System;
using System.Reflection;
using DeNelle.Core.Catalog;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;      // CurrencySkinResolver - the Core seam to the Wallet assembly (HUD may not reference DeNelle.Wallet)
using UnityEngine;
using UnityEngine.UIElements;
using PanelMgr = DeNelle.Core.UI.PanelManager;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class AdminOverlay : MonoBehaviour
    {
        /// <summary>
        /// Paste the owner's Solana wallet address here (lower-cased). Until
        /// then the overlay is reachable only via the debug chord.
        /// </summary>
        public const string OwnerWalletAddress = ""; // TODO(owner)

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _overlay;
        private Label _status;
        private bool _bound;

        // DEF-212 modal arbiter handle (same discipline as HelpMenu / the shop panels).
        private PanelHandle _panelHandle;

#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        // Dev orient tool — catalog id the owner types in. (dev-only — LB-11)
        private TextField _orientIdField;

        // Lock-On A/B toggle button (WO-512) — label reflects ff.lockon state, retargeted on tap.
        private Button _lockOnButton;

        // FLAG-chip toggle (WO-1170) — label reflects ff.flagbutton state.
        private Button _flagButtonToggle;

        // Wallet reset (WO-1171) — two-tap confirm, like the full-reset button below.
        private Button _walletResetButton;
        private float _walletResetArmedUntil;
        private Button _fullResetButton;
        private float _fullResetArmedUntil;   // two-tap confirm window (owner 2026-07-08 full reset)

        // Queue time-skip (owner 2026-08-04). The label of this button IS the state
        // readout — same idiom as _lockOnButton — so the accumulated skip is always
        // visible without adding a widget type to this panel.
        private Button _timeSkipButton;
#endif

        // Reflection handles — resolved lazily on first show.
        // WO-1511: the cached GameStateService Type is gone — the type is named directly.
        private object _gameStateInstance;
        private object _gameStateState;
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        private Type _waveManagerType;
        private object _waveManagerInstance;
#endif

        // True once BuildUi() has actually run (i.e. a PanelSettings was found and the
        // overlay VisualElements exist). When false, Open() would silently no-op — the
        // T-030 "dev tools goes nowhere" failure in scenes (MainCastle_Hall) that ship
        // NO UIDocument/PanelSettings of their own, so the Awake-time borrow finds none.
        private bool _built;

        private void Awake()
        {
            TryBuild(null);
            // Route through the single-modal arbiter (DEF-212) so opening the admin overlay
            // closes any other open panel (incl. Help) and closing clears the slot.
            // Registered even if the UI hasn't built yet — re-registering is harmless and
            // the handle is needed the moment a later TryBuild() succeeds.
            _panelHandle = PanelMgr.Register("Admin", Close, () => IsOpen);
        }

        /// <summary>
        /// Build the overlay UI if it hasn't been built yet. Needs a PanelSettings, which
        /// it borrows from any UIDocument already in the scene; callers may pass an explicit
        /// <paramref name="fallback"/> (e.g. HelpMenu's live PanelSettings) for scenes that
        /// ship no UIDocument of their own. Returns true once the UI exists.
        /// </summary>
        public bool TryBuild(PanelSettings fallback)
        {
            if (_built) return true;
            _document = GetComponent<UIDocument>();
            if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            // OWN PanelSettings (2026-06-13). We USED to BORROW another UIDocument's
            // panelSettings (HelpMenu's, or the onboarding asset) — but a PanelSettings backs
            // only ONE live panel, so HelpMenu's doc + this doc fought over the same asset and
            // AdminOverlay ended up panel=<null>: "Dev tools" opened (display=Flex) but rendered
            // NOTHING ("clicking devtools disappears"). Create our OWN uniquely-named runtime
            // PanelSettings (borrow only the themeStyleSheet for fonts/colors), so the dev panel
            // is independent of HelpMenu's lifecycle. Mirrors the HelpMenu own-PanelSettings fix.
            if (_document.panelSettings == null)
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.name = "AdminRuntimePanelSettings";
                // ⛔ WO-1718 — DO NOT DELETE THESE THREE LINES AS NOISE. A freshly created
                // PanelSettings defaults to scaleMode = ConstantPhysicalSize (referenceDpi 96),
                // which scales the WHOLE panel by realScreenDpi/96. On the Seeker (~400 dpi) that
                // is ≈4.2x: FontBody (50 REFERENCE px, ElarionUi.cs:111-115) rendered ≈210 device
                // px and the panel filled the screen with rows drawn on top of each other (owner
                // report 2026-09-14, docs/handoffs/devtools_panel_oversized_text.png). Owning our
                // own PanelSettings (2026-06-13, below) is what silently dropped the scaler the
                // BORROWED asset used to bring (OnboardingPanelSettings.asset: m_ScaleMode: 2).
                // These values MATCH the uGUI CanvasScaler every other screen uses
                // (ElarionUiKit.cs:107-111 — ScaleWithScreenSize, 1080x1920, MatchWidthOrHeight,
                // match 0.5) so the two toolkits resolve the SAME reference px. Unity's default
                // match is 0.0, so it must be set explicitly. Pinned by AdminPanelScaleRegression.
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1080, 1920);
                ps.match = 0.5f;
                if (fallback != null && fallback.themeStyleSheet != null)
                {
                    ps.themeStyleSheet = fallback.themeStyleSheet;
                }
                else
                {
                    foreach (var existing in UnityEngine.Object.FindObjectsByType<UIDocument>(
                                 FindObjectsInactive.Include))
                    {
                        if (existing == _document || existing.panelSettings == null) continue;
                        if (existing.panelSettings.themeStyleSheet != null)
                        {
                            ps.themeStyleSheet = existing.panelSettings.themeStyleSheet;
                            break;
                        }
                    }
                }
                _document.panelSettings = ps;
            }
            // The DEV overlay must sit ABOVE EVERY in-game panel so it is always usable — incl. the
            // vendor/shop modals (uGUI Canvas at sortingOrder 31000 + a full-screen scrim). At the old
            // 2710 the dev panel opened BENEATH an open shop's scrim after talking to a vendor, so its
            // buttons were non-clickable. 32000 keeps it on top of the 31000 shop + everything below.
            _document.sortingOrder = 32000; // topmost — above the 31000 shop/vendor modals
            // FIX (RCA 2026-06-21, data-proven sortOrder=0 at runtime): UIDocument.sortingOrder does NOT
            // reliably propagate to PanelSettings.sortingOrder — and input dispatch reads the PanelSettings.
            // Set it on the (own, runtime) PanelSettings so the layering is REAL (dev panel above the shop).
            if (_document.panelSettings != null) _document.panelSettings.sortingOrder = 32000;
            BuildUi();
            _built = true;
            return true;
        }

        private void OnDestroy()
        {
            PanelMgr.NotifyClosed(_panelHandle);
        }

        /// <summary>True while the admin overlay is visible (its backdrop is pickable).</summary>
        public bool IsOpen =>
            _overlay != null && _overlay.style.display != DisplayStyle.None;

        private void Update()
        {
            // Debug chord: Ctrl + Shift + A → toggle overlay. Survives the
            // pre-wallet build state. Uses legacy Input Manager since the HUD
            // asmdef doesn't reference Unity.InputSystem.
            // PLAYER-BUILD SAFETY: the admin chord is gated behind the global
            // DevHotkeys kill-switch (default OFF) so it can never pop the owner-only
            // admin overlay in the shipped .exe OR the editor unless a dev opts in
            // (PlayerPrefs ff.devhotkeys=1). The Help menu's "Dev tools" button
            // (AdminOverlay.Open) remains the always-available entry.
            if (!DeNelle.Core.FeatureFlags.DevHotkeys) return;
            if (Input.GetKeyDown(KeyCode.A) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftShift)   || Input.GetKey(KeyCode.RightShift)))
            {
                Toggle();
            }
        }

        // ── UI ──────────────────────────────────────────────────────────────
        private void BuildUi()
        {
            _root = _document.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.right = 0;
            _root.style.top = 0;  _root.style.bottom = 0;

            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.right = 0;
            _overlay.style.top = 0;  _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.86f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.display = DisplayStyle.None;
            _root.Add(_overlay);

            var card = new VisualElement();
            // WO-1718: the card no longer carries a FIXED 560 reference-px ceiling. Those were
            // pre-ladder DESKTOP px (font ladder tripled 2026-07-04: body 15 -> 50), and the
            // longest live captions here are ~45 chars — e.g. "Queue clock: real time   (nothing
            // to reset)" and the armed "SURE? Wipes save+prefs, archives dials, QUITS" — which at
            // fontSize 50 measure ≈1150 reference px. A 560px card could not hold ONE of them on
            // a line. With no explicit width the flex column sizes the card to its widest child,
            // so the card grows to fit the captions; maxWidth is a PERCENT of the panel so it can
            // never exceed the screen at any resolution (at 2340x1080 the panel resolves to
            // 2119x978 reference px — ElarionUiKit.cs:4053 — so 92% = ~1950px of headroom).
            card.style.minWidth = 560; card.style.maxWidth = Length.Percent(92);
            // F8-11 (owner 2026-07-07 "menu needs a scroll bar"): the tool list outgrew the
            // screen — cap the card and let the button column scroll (see ScrollView below).
            card.style.maxHeight = Length.Percent(86);
            card.style.paddingTop = 22;  card.style.paddingBottom = 22;
            card.style.paddingLeft = 26; card.style.paddingRight = 26;
            // Stone panel from the shared theme, but with a DANGER-red rim so the
            // owner-only debug overlay reads as "admin / careful", still in-family.
            card.style.backgroundColor = ElarionUi.PanelStoneDark;
            ElarionUi.SetRadius(card, ElarionUi.RadiusLg);
            ElarionUi.SetBorderWidth(card, 2);
            ElarionUi.SetBorderColor(card, new Color(ElarionUi.Danger.r, ElarionUi.Danger.g, ElarionUi.Danger.b, 0.75f));
            _overlay.Add(card);

            var title = new Label(ElarionUi.CrestGlyph + "  Admin — owner-only");
            title.style.fontSize = ElarionUi.FontTitle;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = ElarionUi.Danger;
            title.style.marginBottom = 6;
            { var tf = AdminFont(); if (tf != null) title.style.unityFont = tf; }
            card.Add(title);
            card.Add(ElarionUi.MakeRule());

            // F8-11: the tool buttons live in a vertical ScrollView so the menu scrolls when it
            // outgrows the capped card; Close + status stay pinned below, always reachable.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexShrink = 1;
            card.Add(scroll);

            // Owner-trimmed (2026-06-11): only the two controls used live remain — a WORKING
            // full-resource grant (through the same EconomyService wallet the shop spends from,
            // so you can actually buy) + the Yarn/tutorial reset. The rest (wave trigger,
            // onboarded toggles, save, reset-save, orient tool) are dropped from the panel; their
            // handlers stay in the file in case they're wanted back.
            // ── DEV-ONLY GRANT/TOOL BUTTONS (LB-11 / E-ADMIN / E-DEVTOOLS) ──────
            // SECURITY: these mint spendable resources / Wisdom / levels and launch
            // dev tools. They are compile-stripped from release builds so a player can
            // never reach them — only DEVELOPMENT_BUILD / the editor compile them in.
            // The Close button below stays so the (release-gated, can't-open) overlay
            // is still dismissable if it ever renders.
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
            // ⛔ WO-1512 — THE CURRENCY/PROGRESSION GRANT DOOR IS DEVELOPER-ONLY, NOT TESTER-ONLY.
            // Everything in the inner guard below MINTS value (spendable resources, Wisdom, hero
            // levels). The outer guard admits TESTER_BUILD, which is the APK that goes to Firebase
            // App Distribution — i.e. to people who are not developers — while the pay path is LIVE
            // (memory `published-but-payments-never-activated`). A tester who can mint 50,000 of
            // every resource makes the economy unmeasurable and the purchase funnel meaningless.
            // The inner guard therefore drops TESTER_BUILD: grants compile ONLY in the Editor or a
            // Development build. The rest of the tester tooling (FLAG chip, time-skip, wallet reset,
            // VFX parade, seating editor) stays reachable under the outer guard because none of it
            // creates value. The handlers themselves carry the SAME inner guard (see below), so this
            // is compile-stripping, not a hidden button.
            // ⚠ DIRECTION IS DELIBERATE: the narrow guard is the one that gates the money. Widening
            // it back to TESTER_BUILD re-opens the door — do not "simplify" the two guards into one.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            scroll.Add(Button("Load resources (full base)",   OnLoadResources));
            // Level shortcuts — same REAL leveling path the F10 DevPanel uses
            // (HeroProgression.AddXp -> ApplyLevelRewards grants Wisdom + skill points),
            // reached by reflection here since the HUD asmdef can't reference DeNelle.Village.
            scroll.Add(Button("Set Level 5 (+skill pts)",     () => OnSetHeroLevel(5)));
            scroll.Add(Button("Set Level 10 (+skill pts)",    () => OnSetHeroLevel(10)));
#endif
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
            // Owner 2026-09-11: skip grind to felt-test owned town. Not a store-economy grant.
            scroll.Add(Button("Set Level 15 (+skill pts)",    () => OnSetHeroLevel(15)));
            scroll.Add(Button("MAX all buildings",            OnMaxAllBuildings));
            scroll.Add(Button("MAX troop types",              OnMaxTroopTypes));
            scroll.Add(Button("Grant Iron Bastion town",      OnGrantCapturedTown));
            scroll.Add(Button("Raid: Iron Bastion",           OnEnterIronBastionRaid));
#endif
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Direct Wisdom grants (owner F8 2026-06-28: "Set Level 10 isn't doing it" —
            // SetHeroLevel is a NO-OP once already >= the target level, so it grants no new
            // Wisdom). These add Wisdom unconditionally regardless of level. The only "+Wisdom"
            // buttons used to live on the DEPRECATED F10 DevPanelController (which the owner never
            // sees) — this is the live Settings -> DevTools panel, so they belong HERE.
            scroll.Add(Button("+25 Wisdom (talents)",         () => OnGiveWisdom(25)));
            scroll.Add(Button("+100 Wisdom (talents)",        () => OnGiveWisdom(100)));
#endif // DEVELOPMENT_BUILD || UNITY_EDITOR — WO-1512 value-minting grants, never TESTER_BUILD
            scroll.Add(Button("Trigger next wave",            OnTriggerWave));
            // ── QUEUE TIME-SKIP (owner 2026-08-04) ───────────────────────────
            // "A speed timer for testing building queues ... but NOT impact the battle
            // timer." WO-855 Phase 4 made structure builds tier-scaled (30s / 1.5m /
            // 4.5m / 13.5m / 40m / 2h, barracks up to 8h) so waiting them out is no
            // longer a testing strategy. These push TimeSource.NowUnixMs forward via
            // DevClock; they are ADDITIVE (tap +10 min six times = +1h) and the fourth
            // button's LABEL shows the running total and clears it.
            //
            // Time.timeScale is deliberately NOT touched — that is the thing the owner
            // ruled out, because it WOULD speed up combat.
            //
            // !! WHAT A SKIP ALSO MOVES — it is not only the build queue. TimeSource is
            // the shared wall-clock seam, so every offline-accrual consumer advances too:
            //   • Obsidian queues (Builder/Train/Research)  — THE TARGET; due jobs
            //     complete on the next sweep and pending jobs cascade into free slots.
            //   • OfflineHarvestService  — PAYS that much offline node/settlement/pet
            //     income on the next claim (capped by OfflineCapSeconds).
            //   • EchoService            — fills the Echo silo, clamped to its 4h cap.
            //   • ResourceCollector      — PAYS that much into each collector's pending
            //     pool (WO-859 away catch-up), clamped by its capacity cap.
            //   • TroopRecoveryService   — HEALS wounded troops by that much (roster
            //     availability between raids; never in-battle pacing).
            // So a wallet/roster that jumps after a skip is EXPECTED, not a bug. The
            // full enumeration + the save-safety assessment live in the header of
            // Assets/_Modules/Core/Diagnostics/DevClock.cs — read it before filing.
            //
            // !! RESET CAVEAT: a job ENQUEUED while skipped keeps a FinishMs beyond real
            // time, so after a Reset it looks stalled for that long. Safe order:
            // enqueue at real time → skip → let it finish → reset. Reset is FlowTrace
            // .Warn'd so a capture always explains a rewound clock.
            //
            // COMBAT IS SAFE, verified at source 2026-08-04: WaveManager, RaidScoring,
            // ATBCombatManager, BattleController, EnemyBrain and HeroHealth contain ZERO
            // TimeSource references — they all run on Time.deltaTime / Time.time.
            // DevTimeSkipRegression pins that so a refactor can't silently change it.
            scroll.Add(Button("Queue time-skip  +1 min",      () => OnDevTimeSkip(60d * 1000d)));
            scroll.Add(Button("Queue time-skip  +10 min",     () => OnDevTimeSkip(600d * 1000d)));
            scroll.Add(Button("Queue time-skip  +1 hour",     () => OnDevTimeSkip(3600d * 1000d)));
            _timeSkipButton = Button(TimeSkipLabel(), OnResetDevTimeSkip);
            scroll.Add(_timeSkipButton);
            scroll.Add(Button("VFX Parade",                   OnVfxParade));
            // WO-577: in-game Seating Editor (Offset Forge slice 2) — dial weapon/shield
            // attachment offsets live on the equipped hero, save to offsets.json.
            scroll.Add(Button("Seating Editor (gear)",        OnSeatingEditor));
            // Lock-On A/B toggle (WO-512): flip ff.lockon live so the owner can compare
            // locked vs free camera mid-fight in the built exe. FeatureFlags.Get reads
            // PlayerPrefs live each call (no cache), so the write below takes effect next frame.
            _lockOnButton = Button(LockOnLabel(), OnToggleLockOn);
            scroll.Add(_lockOnButton);
            // ⭐ FLAG chip toggle (WO-1170, owner 2026-08-24). THE ONLY WAY TO REACH ff.flagbutton
            // FROM A PHONE. The chip defaults OFF everywhere (store-hardening ruling 2026-08-07) and
            // the tester APK is a RELEASE build, so Debug.isDebugBuild is false there too — which
            // left the owner on a touch device with NO capture trigger at all: no F8 key, the 5-tap
            // corner gesture retired, and the dev panel's "Feature flags" group holding ZERO rows
            // since ff.strategicplacement was removed. The flag existed and was unreachable.
            //
            // ⛔ WHY A ONE-TAP CHIP AND NOT A MENU ITEM — the owner's own diagnosis, and it is
            // right: any capture you reach by NAVIGATING photographs the navigation. Settings ->
            // Report a bug closes the menu first, but you still had to open Settings over the very
            // screen you were trying to photograph. The chip captures at the instant of the tap,
            // with nothing opened, which is the whole reason it exists.
            _flagButtonToggle = Button(FlagButtonLabel(), OnToggleFlagButton);
            scroll.Add(_flagButtonToggle);
            // ⭐ WALLET RESET (WO-1171, owner ruling 2026-08-17 finally wired 2026-08-24).
            // "yes it should auto connect, there is a menu option to reset" - the auto-connect half
            // shipped in August and THIS half never did. WalletService.Disconnect() was fully
            // implemented and called by nothing.
            // ⚠ Routed through the Core seam (CurrencySkinResolver.RequestWalletDisconnect) because
            // DeNelle.HUD may NOT reference DeNelle.Wallet - the same reason the connect button uses
            // RequestWalletConnect. Do not "simplify" this into a direct call.
            _walletResetButton = Button(WalletResetLabel(), OnWalletReset);
            scroll.Add(_walletResetButton);
            // F8-11 (owner 2026-07-07): "Reset Yarn" row REMOVED — Yarn was dropped (WO-455/557);
            // OnReplayTutorial stays in the file per the owner-trim convention above.
            // Owner 2026-07-08: "full reset option that clears all persistent data and resources
            // and wisdom to a brand new instance". Two-tap confirm; see OnFullReset for the design
            // (wipe PlayerPrefs, ARCHIVE owner-dialed local files, quit — relaunch boots fresh).
            _fullResetButton = Button("FULL RESET (new player — wipes + quits)", OnFullReset);
            scroll.Add(_fullResetButton);
#endif
            card.Add(Button("Close",                        Toggle));
            FlowTrace.Step("UI", "DevPanel (AdminOverlay) UI built");

            _status = new Label(string.Empty);
            _status.style.color = ElarionUi.ParchmentDim;
            _status.style.fontSize = ElarionUi.FontLabel;
            { var sf = AdminFont(); if (sf != null) _status.style.unityFont = sf; }
            _status.style.marginTop = 8;
            _status.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_status);
        }

        private static Button Button(string label, Action onClick)
        {
            var b = new Button(onClick) { text = label };
            ElarionUi.StyleButton(b, ElarionUi.ButtonKind.Neutral);
            // WO-1718: the `b.style.minHeight = 38` override that used to sit here is DELETED.
            // It undercut ElarionUi.StyleButton's minHeight = TapTarget (88, ElarionUi.cs:198),
            // and its own comment ("override the 44 default") was stale — TapTarget was raised
            // 44 -> 88 for mobile. A 38px box cannot hold a FontBody (50) line box (~58px), so
            // every caption overflowed its own row onto the next one even at 1:1 scale. The kit
            // default now stands: 88 is both the mobile touch floor and roomy for a 50px line.
            b.style.unityFontStyleAndWeight = FontStyle.Normal;
            var f = AdminFont(); if (f != null) b.style.unityFont = f;
            return b;
        }

        // WO-417: explicit font so admin-overlay text renders even when the borrowed
        // PanelSettings theme has no default font (blank rows = backgrounds draw, glyphs don't).
        private static Font _adminFont;
        private static Font AdminFont()
        {
            if (_adminFont == null) _adminFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _adminFont;
        }

        // ── Dev orient tool row ──────────────────────────────────────────────
        // SECURITY (LB-11): dev-only tool — compile-stripped from release builds.
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        private VisualElement BuildOrientRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop = 8; row.style.marginBottom = 4;

            // Relabelled "crafting id" → "catalog id" — the field takes a CatalogRegistry id.
            var idLabel = new Label("catalog id");
            idLabel.style.color = ElarionUi.ParchmentDim;
            idLabel.style.fontSize = ElarionUi.FontLabel;
            { var lf = AdminFont(); if (lf != null) idLabel.style.unityFont = lf; }
            idLabel.style.width = 70;
            row.Add(idLabel);

            _orientIdField = new TextField { value = "" };
            _orientIdField.style.flexGrow = 1;
            _orientIdField.style.marginRight = 6;
            var input = _orientIdField.Q(className: "unity-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = new Color(0.05f, 0.04f, 0.03f, 1f);
                input.style.color = ElarionUi.Parchment;
            }
            // Placeholder via tooltip (UIElements 2021 has no native placeholder).
            _orientIdField.tooltip = "catalog id, e.g. mill or tower_ground_archer";
            row.Add(_orientIdField);

            var btn = Button("Orient Asset", OnOrientAsset);
            btn.style.marginTop = 0; btn.style.marginBottom = 0;
            btn.style.width = 130;
            row.Add(btn);
            return row;
        }

        private void OnOrientAsset()
        {
            string id = _orientIdField != null ? (_orientIdField.value ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrEmpty(id)) { SetStatus("Orient: type a catalog id first."); return; }

            // Resolve the prefab path: prefer the CatalogEntry's visualPrefabPath;
            // fall back to treating the typed id itself as a Resources path.
            string prefabPath = null;
            string displayName = id;
            var entry = CatalogRegistry.Get(id);
            if (entry != null)
            {
                prefabPath  = entry.visualPrefabPath;
                displayName = !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : id;
            }
            if (string.IsNullOrEmpty(prefabPath)) prefabPath = id;   // raw-path fallback

            var prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null)
            {
                SetStatus(entry == null
                    ? $"Orient: id '{id}' not in CatalogRegistry and not loadable as a Resources path."
                    : $"Orient: '{id}' found, but Resources.Load failed for '{prefabPath}'.");
                return;
            }

            if (!OpenOrientMenu(id, prefab, displayName))
            {
                SetStatus("Orient: could not open TowerPlacementRotateMenu (DeNelle.Village missing?).");
                return;
            }

            // Hide the admin overlay so the orient panel is unobstructed (route through
            // the arbiter so the modal slot is cleared, not just the display flag).
            Close();
            SetStatus($"Orienting '{id}'. Confirm in the panel — recipe logs to Console ([OrientRecipe]).");
        }

        /// <summary>
        /// Find-or-create the DeNelle.Village TowerPlacementRotateMenu and invoke
        /// its OpenDevOrient(string,GameObject,string) via reflection (HUD asmdef
        /// does not reference DeNelle.Village). Returns false if the type is absent.
        /// </summary>
        private bool OpenOrientMenu(string id, GameObject prefab, string displayName)
        {
            var menuType = Type.GetType("DeNelle.Village.TowerPlacementRotateMenu, DeNelle.Village");
            if (menuType == null) return false;

            var menu = UnityEngine.Object.FindAnyObjectByType(menuType);
            if (menu == null)
            {
                var go = new GameObject("DevOrientMenu");
                menu = go.AddComponent(menuType);
            }

            var open = menuType.GetMethod("OpenDevOrient",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string), typeof(GameObject), typeof(string) },
                null);
            if (open == null) return false;

            open.Invoke(menu, new object[] { id, prefab, displayName });
            return true;
        }
#endif // DEVELOPMENT_BUILD || UNITY_EDITOR — dev orient tool

        public void Toggle() => SetOpen(!IsOpen);

        /// <summary>Show the admin overlay (Help menu's "Dev tools" routes here).</summary>
        public void Open() => SetOpen(true);

        /// <summary>Hide the admin overlay (Close button + modal-arbiter close).</summary>
        public void Close() => SetOpen(false);

        private void SetOpen(bool open)
        {
            FlowTrace.Step("UI", $"DevPanel toggle/click reached (AdminOverlay.SetOpen open={open}, built={_built})");
            if (_overlay == null)
            {
                FlowTrace.Warn("UI", "DevPanel open FAILED — AdminOverlay._overlay is null (UI never built)");
                return;
            }
            // SECURITY (LB-11 / E-ADMIN): REAL runtime gate. In a STORE player build the
            // overlay must NEVER open for a non-owner. Tester APKs compile TESTER_BUILD
            // (overnight-apk-build -Tester) but are still release-shaped, so
            // Debug.isDebugBuild is false — the Help "Dev Tools" row is visible and then
            // this gate closed Help and bounced to the HUD (Seeker 365962 logcat 22:37:37
            // and 22:38:07, not-authorised warn). Owner 2026-09-11: tester skip kit must
            // open. Store builds do not define TESTER_BUILD.
#if !TESTER_BUILD
            if (open && !IsAuthorised() && !Application.isEditor && !Debug.isDebugBuild)
            {
                FlowTrace.Warn("UI", "DevPanel open BLOCKED — not authorised (release owner gate)");
                return;
            }
#endif
            _overlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _overlay.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
            // Single-modal arbiter (DEF-212): opening closes any other open panel; closing
            // clears our slot so the invisible-but-pickable backdrop can't trap input.
            if (open) PanelMgr.NotifyOpened(_panelHandle);
            else PanelMgr.NotifyClosed(_panelHandle);
            if (open) SetStatus("Ready.");
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
            // Re-sync the queue time-skip readout every time the panel opens: the skip is
            // process-global (DevClock) and may have been moved from the F10 DevPanel or a
            // headless oracle since this panel was last shown.
            if (open && _timeSkipButton != null) _timeSkipButton.text = TimeSkipLabel();
#endif
            FlowTrace.Step("UI", $"DevPanel (AdminOverlay) {(open ? "shown" : "hidden")} — " +
                $"display={_overlay.style.display.value} picking={_overlay.pickingMode} timeScale={Time.timeScale}");
        }

        private bool IsAuthorised()
        {
            if (string.IsNullOrEmpty(OwnerWalletAddress)) return false;
            ResolveGameState();
            if (_gameStateState == null) return false;
            var addr = GetMember<string>(_gameStateState, "BoundWallet");
            return addr != null && addr.Equals(OwnerWalletAddress, StringComparison.OrdinalIgnoreCase);
        }

        // ── Reflection helpers ──────────────────────────────────────────────
        private void ResolveGameState()
        {
            if (_gameStateInstance != null && _gameStateState != null) return;
            // WO-1511: GameStateService is in DeNelle.Core, and DeNelle.HUD.asmdef references
            // "DeNelle.Core" — so the resolve is a direct call. The fields stay `object`
            // deliberately: the reflection below them crosses into DeNelle.Village (WaveManager),
            // which the asmdef does NOT reference, and that seam is sanctioned (CLAUDE.md §5).
            var service = DeNelle.Core.State.GameStateService.Instance;
            if (service == null) return;
            _gameStateInstance = service;
            _gameStateState = service.State;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        private void ResolveWaveManager()
        {
            if (_waveManagerInstance != null) return;
            _waveManagerType = Type.GetType("DeNelle.Village.WaveManager, DeNelle.Village");
            if (_waveManagerType == null) return;
            _waveManagerInstance = UnityEngine.Object.FindAnyObjectByType(_waveManagerType);
        }
#endif

        private static T GetMember<T>(object obj, string name) where T : class
        {
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) return p.GetValue(obj) as T;
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f?.GetValue(obj) as T;
        }

        // ── DEV-ONLY reflection helpers + action handlers (LB-11 / E-ADMIN / E-DEVTOOLS) ──
        // SECURITY: everything below mutates economy/level/save state or launches dev tools.
        // Compile-stripped from release builds — a player build contains none of this code.
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        private static void SetField(object obj, string name, object value)
        {
            var t = obj.GetType();
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) { f.SetValue(obj, value); return; }
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            p?.SetValue(obj, value);
        }

        private void InvokeMethod(object obj, string method, params object[] args)
        {
            if (obj == null) return;
            var m = obj.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            if (m == null) { SetStatus($"Method '{method}' not found."); return; }
            m.Invoke(obj, args);
        }

        // ── Action handlers ─────────────────────────────────────────────────
        private void OnTriggerWave()
        {
            // Force a FRESH resolve every click. _waveManagerInstance is held as `object`, so the
            // `!= null` guard in ResolveWaveManager uses reference equality — it does NOT see Unity's
            // fake-null on a DESTROYED WaveManager from a prior scene, so the cached ref goes stale
            // after a scene change and the trigger silently no-ops (WO-327). Re-find the live one.
            _waveManagerInstance = null;
            ResolveWaveManager();
            if (_waveManagerInstance == null)
            {
                SetStatus("No WaveManager in this scene — nothing to trigger.");
                return;
            }
            InvokeMethod(_waveManagerInstance, "ForceSpawnNextWaveNow");
            SetStatus("Jumped to next wave (ForceSpawnNextWaveNow — spawns immediately, skips countdown).");
        }

        /// <summary>
        /// Launches the RUNTIME VFX Parade overlay (VfxParade.VfxParadeRuntime) so the
        /// owner can curate effects in the built exe with no editor open. The HUD asmdef
        /// does not reference VfxParade.Runtime, so the singleton is created + shown via
        /// reflection (same idiom as the orient menu / wave manager). The overlay pauses
        /// time itself and restores it on Close; this just opens it and hides the admin
        /// panel so it is unobstructed.
        /// </summary>
        private void OnVfxParade()
        {
            var runtimeType = Type.GetType("VfxParade.VfxParadeRuntime, VfxParade.Runtime");
            if (runtimeType == null)
            {
                SetStatus("VFX Parade: VfxParade.Runtime assembly/type not found in this build.");
                return;
            }

            var launch = runtimeType.GetMethod("Launch",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (launch == null)
            {
                SetStatus("VFX Parade: VfxParadeRuntime.Launch() not found.");
                return;
            }

            object instance = null;
            try { instance = launch.Invoke(null, null); }
            catch (Exception e)
            {
                SetStatus("VFX Parade: launch threw - " + e.Message);
                return;
            }

            if (instance == null)
            {
                SetStatus("VFX Parade: Launch returned null (no manifest baked? run VfxParadeManifestBuilder.Build).");
                return;
            }

            // Hide the admin overlay so the parade panel is unobstructed (route through the
            // arbiter so the modal slot is cleared, not just the display flag).
            Close();
            SetStatus("VFX Parade opened. Use Next/Prev, tag a moment + note, Bookmark -> vfx-picks.json.");
        }

        // ── In-game Seating Editor (WO-577) ──────────────────────────────────
        /// <summary>
        /// Launch the live weapon/shield Seating Editor (Offset Forge slice 2). The HUD asmdef
        /// can't reference DeNelle.Village, so SeatingEditorOverlay.Launch() is invoked by
        /// reflection (same idiom as the orient menu / VFX parade). It finds the equipped hero
        /// itself. Hides the admin overlay so the seating panel is unobstructed.
        /// </summary>
        private void OnSeatingEditor()
        {
            var t = Type.GetType("DeNelle.Village.UI.SeatingEditorOverlay, DeNelle.Village");
            if (t == null)
            {
                SetStatus("Seating Editor: DeNelle.Village.UI.SeatingEditorOverlay not found in this build.");
                return;
            }
            var launch = t.GetMethod("Launch", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (launch == null)
            {
                SetStatus("Seating Editor: SeatingEditorOverlay.Launch() not found.");
                return;
            }
            object instance = null;
            try { instance = launch.Invoke(null, null); }
            catch (Exception e) { SetStatus("Seating Editor: launch threw — " + e.Message); return; }

            Close();
            SetStatus(instance != null
                ? "Seating Editor opened. Pick Main/Off-hand, dial from vertical, Save (writes offsets.json + logs JSON)."
                : "Seating Editor: no equipped hero/weapon found to edit.");
        }

        // ── Lock-On A/B toggle (WO-512) ──────────────────────────────────────
        // FeatureFlags.Get reads PlayerPrefs live each call (no in-memory cache), so writing
        // "ff.lockon" + Save() here takes effect on the NEXT FeatureFlags.LockOn read — the
        // lock-on slices (SmartMobileCamera / HeroLocomotion / BattleArena) re-check it each
        // frame, so the owner can flip locked vs free camera mid-fight with no rebuild/restart.
        private static string LockOnLabel()
        {
            return "Lock-On: " + (DeNelle.Core.FeatureFlags.LockOn ? "ON" : "OFF");
        }

        private void OnToggleLockOn()
        {
            bool on = !DeNelle.Core.FeatureFlags.LockOn;   // resolved value, then invert
            PlayerPrefs.SetInt("ff.lockon", on ? 1 : 0);
            PlayerPrefs.Save();
            if (_lockOnButton != null) _lockOnButton.text = LockOnLabel();
            FlowTrace.Step("UI", "DevPanel (AdminOverlay) ff.lockon = " + (on ? "ON" : "OFF"));
            SetStatus("Lock-On " + (on ? "ON" : "OFF") + " (live next frame) - re-engage or move to feel the camera change.");
        }

        // ── FLAG chip toggle (WO-1170) ───────────────────────────────────────
        // Same live-read contract as the Lock-On pair above: FeatureFlags.Get reads PlayerPrefs on
        // every call, and FlagCaptureButton.ShouldShow re-checks it, so the chip appears/disappears
        // without a rebuild or restart. PlayerPrefs persist across launches, so this is set ONCE per
        // device and the capture trigger is there from then on.
        private static string FlagButtonLabel()
        {
            return "FLAG chip: " + (DeNelle.Core.FeatureFlags.FlagButton ? "ON" : "OFF");
        }

        private void OnToggleFlagButton()
        {
            bool on = !DeNelle.Core.FeatureFlags.FlagButton;   // resolved value, then invert
            PlayerPrefs.SetInt("ff.flagbutton", on ? 1 : 0);
            PlayerPrefs.Save();
            if (_flagButtonToggle != null) _flagButtonToggle.text = FlagButtonLabel();
            FlowTrace.Step("UI", "DevPanel (AdminOverlay) ff.flagbutton = " + (on ? "ON" : "OFF"));
            SetStatus(on
                ? "FLAG chip ON - a one-tap capture chip now sits on the left edge. Tap it the moment "
                + "something looks wrong: it shoots the CURRENT frame, so nothing has to be opened first."
                : "FLAG chip OFF - no on-screen capture trigger on this device.");
        }

        // ── Wallet reset (WO-1171) ───────────────────────────────────────────
        // TWO-TAP CONFIRM, deliberately: disconnecting drops the sealed MWA session, so the next
        // cold start stops auto-resuming and the player must re-authorize in the wallet app. That is
        // recoverable but not free, and it is the kind of thing a mis-tap should not do.
        private static string WalletResetLabel()
        {
            return CurrencySkinResolver.IsWalletConnected
                ? "Disconnect Wallet (" + CurrencySkinResolver.ConnectedWalletShortAddress + ")"
                : "Disconnect Wallet (none connected)";
        }

        private void OnWalletReset()
        {
            if (Time.unscaledTime > _walletResetArmedUntil)
            {
                _walletResetArmedUntil = Time.unscaledTime + 4f;
                if (_walletResetButton != null) _walletResetButton.text = "Tap again to DISCONNECT";
                SetStatus("Disconnecting clears the saved wallet session - the next launch will ask you to " +
                          "Connect again instead of reconnecting silently. Tap again within 4s to confirm.");
                return;
            }

            _walletResetArmedUntil = 0f;
            CurrencySkinResolver.RequestWalletDisconnect();
            if (_walletResetButton != null) _walletResetButton.text = WalletResetLabel();
            FlowTrace.Step("UI", "DevPanel (AdminOverlay) wallet disconnect requested.");
            SetStatus("Wallet disconnected. Reconnect from the title screen's Connect Wallet button.");
        }

        // ── Queue time-skip (owner 2026-08-04) ───────────────────────────────
        // Pushes DeNelle.Village.TimeSource.NowUnixMs forward so the Obsidian build/
        // train/research queues resolve without a real-time wait. Driven through
        // DeNelle.Core.Diagnostics.DevClock (NOT reflection): the HUD asmdef references
        // DeNelle.Core, and DevClock lives there precisely so this panel can reach the
        // clock seam legally. See the button block in BuildUi() for the full list of
        // what else a skip moves, and DevClock.cs for the authoritative treatment.

        /// <summary>Button label that doubles as the live skip readout (Lock-On idiom).</summary>
        private static string TimeSkipLabel()
        {
            return DevClock.SkipMs > 0d
                ? "Queue clock: +" + DevClock.DescribeCurrent() + "   (TAP TO RESET)"
                : "Queue clock: real time   (nothing to reset)";
        }

        /// <summary>Adds <paramref name="deltaMs"/> to the dev queue-clock skip (additive).</summary>
        private void OnDevTimeSkip(double deltaMs)
        {
            double total = DeNelle.Core.Diagnostics.DevClock.Add(deltaMs);
            if (_timeSkipButton != null) _timeSkipButton.text = TimeSkipLabel();
            FlowTrace.Step("UI",
                $"DevPanel (AdminOverlay) queue time-skip +{DevClock.Describe(deltaMs)} -> total {DevClock.Describe(total)}");
            SetStatus($"Queue clock +{DevClock.Describe(deltaMs)} (total +{DevClock.Describe(total)}). " +
                      "Build/train/research jobs due within it complete on the next sweep. " +
                      "Combat + wave timers are UNAFFECTED (they run on engine time). " +
                      "Offline income + troop recovery also advance - that is expected.");
        }

        /// <summary>Clears the dev queue-clock skip (back to the real device clock).</summary>
        private void OnResetDevTimeSkip()
        {
            double cleared = DeNelle.Core.Diagnostics.DevClock.Reset();
            if (_timeSkipButton != null) _timeSkipButton.text = TimeSkipLabel();
            FlowTrace.Step("UI", $"DevPanel (AdminOverlay) queue time-skip RESET (cleared {DevClock.Describe(cleared)})");
            SetStatus(cleared > 0d
                ? $"Queue clock reset (cleared +{DevClock.Describe(cleared)}). Accrual stamps self-heal on the " +
                  "next tick; a job ENQUEUED while skipped keeps its skewed finish time and may look stalled."
                : "Queue clock was already at real time - nothing to reset.");
        }

        // WO-1512: resource/Wisdom mint stays developer-only at the BUTTON layer.
        // Owner 2026-09-11: skip handlers (level, troops, town) also compile for TESTER_BUILD.
#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        private void OnGiveCrystals(int delta)
        {
            ResolveGameState();
            if (_gameStateInstance == null || _gameStateState == null)
            {
                SetStatus("GameStateService not alive yet.");
                return;
            }
            // State has Resources.Crystals (nested struct) per the SaveSchema; just
            // call an "AddCrystals" if it exists, else log the gap.
            InvokeMethod(_gameStateInstance, "AddCrystals", delta);
            SetStatus($"+{delta} crystals requested (if AddCrystals isn't defined, owner adds it).");
        }
#endif // DEVELOPMENT_BUILD || UNITY_EDITOR — WO-1512

        private void OnSetOnboarded(bool value)
        {
            ResolveGameState();
            if (_gameStateState == null) { SetStatus("State unavailable."); return; }
            SetField(_gameStateState, "Onboarded", value);
            InvokeMethod(_gameStateInstance, "Save");
            SetStatus($"Onboarded set to {value} + saved.");
        }

        private void OnSave()
        {
            ResolveGameState();
            InvokeMethod(_gameStateInstance, "Save");
            SetStatus("Saved.");
        }

        private void OnReset()
        {
            ResolveGameState();
            InvokeMethod(_gameStateInstance, "ResetToNewGame");

            // ResetToNewGame() clears Onboarded + the party roster but NOT the
            // FTUE gate. The intro Yarn (CompanionMeetingTrigger) is gated once
            // per save via PlayerPrefs "yarn.companionMeeting.seen" (matches
            // CompanionMeetingTrigger.SeenKey). If we leave it set, the tutorial
            // Yarn short-circuits, FinishOnboarding never runs, AddToParty never
            // fires, and the player lands with an empty roster / no companion.
            PlayerPrefs.DeleteKey("yarn.companionMeeting.seen");
            PlayerPrefs.Save();

            // The in-place reset doesn't reload the scene, so the trigger's
            // _hostedThisSession latch and TutorialDirector.s_ranThisSession can't
            // re-fire this session. Reload the village scene so a devtools reset
            // behaves like a real New Game -> tutorial runs -> companion joins.
            UnityEngine.SceneManagement.SceneManager.LoadScene("Village2");
            SetStatus("Reset(): new game -> FTUE gate cleared, reloading Village2 (tutorial + companion re-fire).");
        }

        // ─── WO-1512: VALUE-MINTING GRANTS — DEVELOPER-ONLY (TESTER_BUILD EXCLUDED) ───
        // Resource grant, hero-level grant, Wisdom grant and their trace helpers. Compile-stripped
        // from a TESTER_BUILD APK as well as from release, so the Firebase App Distribution build
        // physically cannot mint currency. Matches the inner guard on the buttons above.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>
        /// Grants a full base of SPENDABLE resources through EconomyService.GrantSpendableUncapped —
        /// which lands Wood/Iron in BOTH wallets the game keeps: the in-session pool the shop +
        /// HUD bar read AND GameState.Wood/Iron the structure-upgrade flow (ResourceLedger)
        /// spends. Plain Grant only filled the in-session pool, so dev-granted Wood/Iron was
        /// unspendable in the upgrade flow. HUD asmdef can't reference DeNelle.Village, so
        /// EconomyService is reached by reflection (same idiom as the orient menu / wave manager).
        /// </summary>
        private void OnLoadResources()
        {
            var ecoType = Type.GetType("DeNelle.Village.EconomyService, DeNelle.Village");
            var instProp = ecoType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var eco = instProp?.GetValue(null);
            if (eco == null) { SetStatus("Resources: EconomyService not alive yet."); return; }

            // GrantSpendableUncapped(int wood = 0, int food = 0, int iron = 0, int crystals = 0) —
            // mirrors Wood/Iron into GameState so the upgrade flow can spend them too.
            //
            // ⚠ UNCAPPED, DELIBERATELY (audit 2026-08-15). This used to resolve "GrantSpendable",
            // which routes through the TownBankCapacity clamp — so a 50,000 wood dev grant into a
            // 2,500 bank silently vaporised ~95% of itself, with a throttled toast as the only tell.
            // GrantSpendableUncapped is the DevHarness path that exists for exactly this.
            // The lookup is reflection-BY-STRING, so no compiler or source lint can see it if it
            // drifts back — DevGrantUncappedRegression pins both dev surfaces to this method name.
            var grant = ecoType.GetMethod("GrantSpendableUncapped",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(int), typeof(int), typeof(int), typeof(int) }, null);
            if (grant == null) { SetStatus("Resources: EconomyService.GrantSpendableUncapped(int,int,int,int) not found."); return; }

            grant.Invoke(eco, new object[] { 50000, 25000, 50000, 25000 }); // wood, food, iron, crystals

            // Gold/Coins: GrantSpendable has NO coins param, so this dev grant never gave gold.
            // AddCoins (public) tops up the shop/sell wallet (GameState.Resources.Coins) and fires
            // ResourcesChanged so the HUD gold readout updates. Reflected (HUD can't ref Village).
            var addCoins = ecoType.GetMethod("AddCoins",
                BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(int) }, null);
            if (addCoins != null) addCoins.Invoke(eco, new object[] { 50000 });

            // dev-grant-both-wallets fix: log the granted amounts + the resulting BOTH-store
            // totals (in-session pool via Snapshot + persisted GameState.Wood/Iron) so a dev
            // grant is traceable end-to-end — proves Wood/Iron landed in shop/HUD AND upgrade flow.
            LogGrantTrace(eco, ecoType, 50000, 25000, 50000, 25000);

            // GrantSpendable fires EconomyService.OnChanged, which HeartHudBridge uses to
            // refresh the on-screen resource bar. Belt-and-braces: push the wallet straight
            // to the VillageHudController too so the bar populates immediately even if the
            // bridge isn't subscribed yet in this scene (e.g. the castle hub bootstrap race).
            PingResourceBar(eco, ecoType);

            SetStatus("Loaded: +50k Gold, +50k Wood, +50k Iron, +25k Food, +25k Crystals (shop + upgrades) — now buy something.");
        }

        /// <summary>
        /// Forces the on-screen top resource bar to refresh from the EconomyService
        /// wallet immediately after a grant. EconomyService.Snapshot is a struct in
        /// DeNelle.Village (read by reflection — HUD can't reference Village), but the
        /// VillageHudController lives in THIS assembly so its SetResources is called
        /// directly. Guarantees the bar populates even if HeartHudBridge isn't yet
        /// subscribed in the loaded scene.
        /// </summary>
        private void PingResourceBar(object eco, Type ecoType)
        {
            if (eco == null || ecoType == null) return;

            var hud = UnityEngine.Object.FindAnyObjectByType<VillageHudController>();
            if (hud == null) return;

            var snapProp = ecoType.GetProperty("Snapshot", BindingFlags.Public | BindingFlags.Instance);
            if (snapProp == null) return;
            var snap = snapProp.GetValue(eco);
            if (snap == null) return;

            var snapType = snap.GetType();
            int wood     = GetIntField(snapType, snap, "Wood");
            int iron     = GetIntField(snapType, snap, "Iron");
            int food     = GetIntField(snapType, snap, "Food");
            int crystals = GetIntField(snapType, snap, "Crystals");

            // HUD signature: SetResources(wood, iron, food, gems).
            hud.SetResources(wood, iron, food, crystals);
            hud.SetCrystals(crystals);
        }

        private static int GetIntField(Type t, object obj, string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.GetValue(obj) is int vi) return vi;
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.GetValue(obj) is int pi) return pi;
            return 0;
        }

        /// <summary>
        /// dev-grant-both-wallets fix: emits a FlowTrace("Eco") line with the granted
        /// amounts AND the resulting totals from BOTH stores — the in-session pool
        /// (EconomyService.Snapshot, read by reflection) plus GameState.Wood/Iron (the
        /// upgrade flow's wallet). Confirms a single dev grant filled both wallets.
        /// </summary>
        private void LogGrantTrace(object eco, Type ecoType, int wood, int food, int iron, int crystals)
        {
            int poolWood = 0, poolIron = 0, poolFood = 0, poolCrys = 0;
            var snapProp = ecoType?.GetProperty("Snapshot", BindingFlags.Public | BindingFlags.Instance);
            var snap = snapProp?.GetValue(eco);
            if (snap != null)
            {
                var st = snap.GetType();
                poolWood = GetIntField(st, snap, "Wood");
                poolIron = GetIntField(st, snap, "Iron");
                poolFood = GetIntField(st, snap, "Food");
                poolCrys = GetIntField(st, snap, "Crystals");
            }

            int gsWood = 0, gsIron = 0;
            ResolveGameState();
            if (_gameStateState != null)
            {
                gsWood = GetMember<object>(_gameStateState, "Wood") is int gw ? gw : 0;
                gsIron = GetMember<object>(_gameStateState, "Iron") is int gi ? gi : 0;
            }

            FlowTrace.Step("Eco",
                $"DevGrant (AdminOverlay) +W{wood} F{food} I{iron} C{crystals} -> " +
                $"pool W{poolWood} I{poolIron} F{poolFood} C{poolCrys} | " +
                $"GameState W{gsWood} I{gsIron}");
        }
#endif // DEVELOPMENT_BUILD || UNITY_EDITOR — resource mint helpers

#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
        /// <summary>
        /// Sets the hero to <paramref name="target"/> through the SAME real leveling path the
        /// F10 DevPanel's "Set Level 5/10" uses (DevPanelController.SetHeroLevelTo): feed XP via
        /// HeroProgression.AddXp(XpToNext + 1) until the target level is reached, so each level
        /// crossed runs ApplyLevelRewards and banks that level's Wisdom (the skill-tree spend
        /// currency) + a skill point. The HUD asmdef can't reference DeNelle.Village, so the
        /// HeroProgression loop + the resulting Wisdom read are done by reflection (same idiom
        /// as OnLoadResources / the orient menu / the wave manager above). No-op if already
        /// at/above the target.
        /// </summary>
        private void OnSetHeroLevel(int target)
        {
            target = Mathf.Max(1, target);

            var hpType = Type.GetType("DeNelle.Village.HeroProgression, DeNelle.Village");
            var hp = hpType != null ? UnityEngine.Object.FindAnyObjectByType(hpType) : null;
            if (hp == null) { SetStatus("Level: HeroProgression not in scene yet."); return; }

            var levelProp = hpType.GetProperty("Level", BindingFlags.Public | BindingFlags.Instance);
            var xpToNextProp = hpType.GetProperty("XpToNext", BindingFlags.Public | BindingFlags.Instance);
            var addXp = hpType.GetMethod("AddXp",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float) }, null);
            if (levelProp == null || xpToNextProp == null || addXp == null)
            {
                SetStatus("Level: HeroProgression API (Level/XpToNext/AddXp) not found.");
                return;
            }

            int LevelNow() => levelProp.GetValue(hp) is int l ? l : 0;
            float XpToNextNow() => xpToNextProp.GetValue(hp) is float x ? x : 0f;

            // Mirror SetHeroLevelTo: repeated AddXp(XpToNext + 1) until the target level.
            int guard = 0;
            while (LevelNow() < target && guard++ < 500)
                addXp.Invoke(hp, new object[] { XpToNextNow() + 1f });

            int reached = LevelNow();

            // Resulting Wisdom (skill-tree spend currency) — WisdomCurrencyService.Instance.Wisdom.
            int wisdom = 0;
            var wisType = Type.GetType("DeNelle.Village.Talents.WisdomCurrencyService, DeNelle.Village");
            var wisInst = wisType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (wisInst != null)
            {
                var wisProp = wisType.GetProperty("Wisdom", BindingFlags.Public | BindingFlags.Instance);
                if (wisProp?.GetValue(wisInst) is int w) wisdom = w;
            }

            FlowTrace.Step("Hero",
                $"DevPanel (AdminOverlay) set hero -> Lv.{reached} (target {target}), Wisdom {wisdom}");
            SetStatus($"Set hero to Lv.{reached} (target {target}) — {wisdom} Wisdom to spend in the skill tree.");
        }

        /// <summary>
        /// Grants <paramref name="amount"/> Wisdom directly (the skill-tree spend currency),
        /// independent of hero level. WisdomCurrencyService lives in DeNelle.Village.Talents,
        /// which the HUD asmdef can't reference, so we reach it by reflection (same idiom as
        /// the Wisdom READ in OnSetHeroLevel): WisdomCurrencyService.Instance.Grant(amount).
        /// </summary>
        private void OnGiveWisdom(int amount)
        {
            var wisType = Type.GetType("DeNelle.Village.Talents.WisdomCurrencyService, DeNelle.Village");
            var wisInst = wisType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (wisInst == null) { SetStatus("Wisdom: WisdomCurrencyService not in scene yet."); return; }

            var grant = wisType.GetMethod("Grant",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
            if (grant == null) { SetStatus("Wisdom: WisdomCurrencyService.Grant(int) not found."); return; }
            grant.Invoke(wisInst, new object[] { amount });

            int wisdom = 0;
            var wisProp = wisType.GetProperty("Wisdom", BindingFlags.Public | BindingFlags.Instance);
            if (wisProp?.GetValue(wisInst) is int w) wisdom = w;

            FlowTrace.Step("Hero", $"DevPanel (AdminOverlay) granted +{amount} Wisdom -> {wisdom} total.");
            SetStatus($"+{amount} Wisdom — now {wisdom} to spend in the skill tree.");
        }

        static string CallDevSkip(string method)
        {
            var t = Type.GetType("DeNelle.Village.World.Camps.DevSkipKit, DeNelle.Village");
            var m = t?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            if (m == null) return "DevSkipKit." + method + " not in this build.";
            return m.Invoke(null, null) as string ?? "ok";
        }

        void OnMaxAllBuildings()
        {
            SetStatus(CallDevSkip("PrepCastlePower"));
        }

        void OnMaxTroopTypes()
        {
            SetStatus(CallDevSkip("PrepCastlePower"));
        }

        void OnGrantCapturedTown()
        {
            SetStatus(CallDevSkip("GrantCapturedTownAndEnter"));
        }

        void OnEnterIronBastionRaid()
        {
            SetStatus(CallDevSkip("EnterIronBastionRaid"));
        }
#endif // TESTER_BUILD skip + L15 (owner 2026-09-11)

        // ── FULL RESET (owner 2026-07-08: "clears all persistent data and resources and
        // wisdom to a brand new instance") ────────────────────────────────────────────
        // Design: wipe + QUIT — a relaunch boots genuinely fresh. In-place resets leave
        // stale DDOL singletons (EconomyService in-session Wood/Iron pool, Wisdom, HUD
        // models) holding old values; quitting is the only zero-residue "new instance".
        // Owner-dialed local files (gear offsets, structure orientations) are ARCHIVED
        // (renamed .bak-<stamp>), never deleted — a reset must not destroy un-baked
        // creative tuning. Two-tap confirm, same pattern as the flee button.
        private void OnFullReset()
        {
            if (Time.unscaledTime >= _fullResetArmedUntil)
            {
                _fullResetArmedUntil = Time.unscaledTime + 3f;
                if (_fullResetButton != null) _fullResetButton.text = "SURE? Wipes save+prefs, archives dials, QUITS";
                SetStatus("Full reset armed — tap again within 3s. PlayerPrefs wiped (save, flags, cosmetics), local dial files archived, app quits.");
                return;
            }

            Guard.Try("Admin", "full reset (wipe + archive + quit)", () =>
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string root = Application.persistentDataPath;
                int archived = 0;
                foreach (var name in new[] { "attachment-offsets.json", "structure-orientations.json" })
                {
                    string path = System.IO.Path.Combine(root, name);
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Move(path, path + ".bak-" + stamp);
                        archived++;
                    }
                }
                FlowTrace.Step("Admin", "FULL RESET: archived " + archived + " owner-dial file(s) " +
                    "(.bak-" + stamp + "), wiping ALL PlayerPrefs (save 'dotr-save', feature-flag " +
                    "overrides, cosmetics 'dotr-cosmetics-v1', battle-pass) — quitting for a fresh boot.");
                PlayerPrefs.DeleteAll();
                PlayerPrefs.Save();
                Application.Quit();
                // Editor Play mode: Quit() is a no-op — tell the owner what to do.
                SetStatus("FULL RESET done (prefs wiped, dials archived). In the editor, stop Play manually; the next run is a brand-new instance.");
            });
        }

        private void OnReplayTutorial()
        {
            // Force the intro Yarn to replay WITHOUT wiping progress (unlike Reset):
            // clear the FTUE gate then reload the village so CompanionMeetingTrigger +
            // TutorialDirector re-fire (their session latches reset on scene load). The
            // companion re-joins on tutorial completion. Key = CompanionMeetingTrigger.SeenKey.
            PlayerPrefs.DeleteKey("yarn.companionMeeting.seen");
            PlayerPrefs.Save();
            // Owner-requested: a Yarn reset drops back to Character Select so the whole
            // onboarding flow (HeroSelect -> PetSelect -> Village -> tutorial) replays.
            DeNelle.Core.SceneRouter.GoHeroSelect();
            SetStatus("Replay Tutorial: FTUE gate cleared — dropping to Character Select (HeroSelect).");
        }
#endif // DEVELOPMENT_BUILD || UNITY_EDITOR — dev grant/tool handlers

        private void SetStatus(string s)
        {
            if (_status != null) _status.text = s;
        }
    }
}
