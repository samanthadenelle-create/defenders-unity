// =============================================================================
// HubRepairAffordance - the OUT-OF-BATTLE (hub / overworld) structure-repair
// affordance (owner felt-test 2026-07-15: "WE do need a repair outside of battle
// in case they could not afford after the battle and needed to farm").
// -----------------------------------------------------------------------------
// THE GAP THIS CLOSES (proven from the code):
//   The repair BACKEND (WallRepairController: CostFor / RepairAll / RepairAllCost
//   / CanAffordMaterials, which also un-breaks broken-shell Towers via Tower.Repair)
//   is complete, but it was only ever REACHABLE in the WAVE context:
//     * WaveFeedbackDirector.OnWaveCleared -> SurfaceWorstRepair() : a ONE-SHOT
//       nudge fired the instant a wave is cleared;
//     * EndStateVM "Repair All" CTA : the ONE-SHOT end-of-battle screen;
//     * WallRepairController tap-to-select : only alive because
//       WaveFeedbackDirector.TrySpawn self-installs the controller, and that
//       method returns early when the scene has NO WaveManager
//       ("if (wave == null) return; // not a wave scene").
//   So a player who cannot AFFORD the repair right after a battle, leaves to FARM,
//   and returns to the hub has NO repair affordance waiting - the one-shot nudge
//   and end-state CTA are gone, and a pure hub scene has no controller at all.
//
// WHAT THIS ADDS (no second repair system - it REUSES the backend):
//   A self-installing, persistent, re-openable "REPAIR ALL" button that appears
//   whenever there are damaged structures AND we are NOT in an active wave. It
//   prices the whole damaged set through WallRepairController.RepairAllCost(),
//   and on tap:
//     * AFFORDABLE (wallet covers the FULL cost) -> WallRepairController.RepairAll()
//       (worst-first, un-breaks broken Towers, spends through the SAME
//       EconomyService path build-mode placement charges);
//     * NOT AFFORDABLE -> shows the cost + the exact SHORTFALL ("go farm") and
//       spends NOTHING (owner ruling: don't spend if they can't afford). Once the
//       player has farmed enough, the SAME button flips to affordable and repairs.
//
// MODULE ISOLATION: lives in DeNelle.Village; references only Village types +
// DeNelle.Core (ElarionUiKit / FlowTrace). Builds its own code-built uGUI canvas
// on the kit (no UXML, per PIPELINE_STATE) - it does NOT touch the HUD kit or the
// WallRepairHudBridge, so it is robust to the HUD's own repair-prompt wiring.
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Village.Buildings.Progression;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village
{
    /// <summary>
    /// Persistent out-of-battle "Repair All" affordance for the hub / overworld.
    /// Self-installs into any gameplay scene that has repairable structures and
    /// drives <see cref="WallRepairController"/> (the one repair backend) so the
    /// player can repair damaged structures anytime after farming - gated on full
    /// affordability, with the shortfall shown when they cannot yet afford it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HubRepairAffordance : MonoBehaviour
    {
        // Below Build HUD (906) / selection (910) chrome; above the world.
        private const int SortingOrder = 905;
        private const float RefreshInterval = 0.75f;
        /// <summary>Rate gate on the refused-tap Warn (see OnClick) - the tally is never lost.</summary>
        private const float RefusedWarnInterval = 2f;

        private WallRepairController _repair;
        private GameObject _canvas;
        private Button _button;
        private Button _acknowledgeButton;
        private Image _buttonImg;
        private TextMeshProUGUI _label;
        private float _timer;
        private string _acknowledgedShortfall = string.Empty;

        // Dead-tap tally + its rate gate (OnClick's unaffordable branch).
        private int _refusedTaps;
        private float _nextRefusedWarnAt;

        // Last label string already reported as clipping (see WarnIfClipped) - de-dupes the poll.
        private string _lastClipReported;

        // Last-announced state so FlowTrace logs transitions, not every poll.
        private enum Vis { Uninit, HiddenInBattle, HiddenNothingDamaged, HiddenPanelOpen, AvailableAffordable, AvailableShort }
        private Vis _last = Vis.Uninit;

        /// <summary>
        /// DIAGNOSTIC SEAM (read-only) - what this affordance is currently showing, for the
        /// F8 repair capture. The two HIDDEN cases are deliberately DISTINCT: "hidden because
        /// a wave is active" and "hidden because nothing is damaged" are opposite diagnoses
        /// for the same reported symptom, and they used to share one log line, which made a
        /// capture unable to tell them apart.
        /// </summary>
        public string DiagnosticState => _last.ToString();

        // =====================================================================
        //  Self-install (mirrors WaveFeedbackDirector's spawn pattern)
        // =====================================================================

        /// <summary>
        /// True once this scene has an affordance, so <see cref="NotifyRepairableAppeared"/> can
        /// be called from a per-structure hot path for the price of a bool test. Cleared on every
        /// scene load - a new scene starts with no affordance regardless of what the last one had.
        /// </summary>
        private static bool s_installedThisScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawn();   // the first scene is already loaded when this runs
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            s_installedThisScene = false;
            s_townGateScene = null;          // re-decide the town gate for the new scene
            TrySpawn();
        }

        // ── WO-1436: the town gate ───────────────────────────────────────────
        // Cached per active scene so NotifyRepairableAppeared (a per-structure hot path)
        // costs a reference compare, and so the refusal is announced ONCE per scene rather
        // than once per structure restored.
        private static string s_townGateScene;
        private static bool s_townGateSuppressed;

        /// <summary>
        /// WO-1436. TRUE when this scene must NOT carry the town repair affordance.
        ///
        /// <para>THE DEFECT (owner felt-test 2026-09-06, and she spotted it unprompted —
        /// she asked why her pets wanted to repair things in a raid). Inside a live raid the
        /// capture reads:</para>
        /// <code>[Flow:Repair] hub repair affordance installed (scene='RaidBase_raider_camp_small')</code>
        /// <para>...and a <c>REPAIR ALL — Wood 263 Iron 133</c> button sat in the middle of a
        /// battlefield, over the raid's own chrome. This gate was never LEAKED past: it was
        /// never asked. <see cref="SceneHasRepairables"/> is a structural test — "does this
        /// scene contain a WallSegment / Gate / Building / Tower?" — and a raid base is FULL
        /// of them. They are the ENEMY'S. The predicate answered its own question correctly
        /// and was the wrong question to be the only one asked.</para>
        ///
        /// <para>REUSED, NOT INVENTED: <see cref="DeNelle.Core.HubScenes.SuppressTownHud"/> is
        /// the WO-550 chokepoint written for exactly this — "should TOWN/SOCIAL/ECONOMY HUD be
        /// suppressed here?" — already gating ~14 panel bootstraps off the same
        /// scene-configs.json ownership data. The raid scene resolves Enemy-owned in the very
        /// same capture (<c>[Flow:World] SceneOwnership resolved 'RaidBase_raider_camp_small'
        /// -> Enemy-owned (IsEnemyOwned=True)</c>), so this is one more caller of a working
        /// gate, not a new parallel rule.</para>
        /// </summary>
        private static bool TownHudSuppressedHere()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!ReferenceEquals(scene, s_townGateScene) && scene != s_townGateScene)
            {
                s_townGateScene = scene;
                s_townGateSuppressed = DeNelle.Core.HubScenes.SuppressTownHud(scene);
                if (s_townGateSuppressed)
                    FlowTrace.Step("Repair",
                        $"hub repair affordance SUPPRESSED (scene='{scene}') - enemy-owned ground " +
                        "(HubScenes.SuppressTownHud). The walls and towers standing here are the " +
                        "ENEMY'S; offering REPAIR ALL over them is town chrome in a raid (WO-1436).");
            }
            return s_townGateSuppressed;
        }

        /// <summary>
        /// WO-1024. Install the affordance the moment a repairable structure actually EXISTS,
        /// instead of only at scene load.
        ///
        /// <para>THE BUG THIS CLOSES. The town is player-built and restored from the save AFTER
        /// the scene finishes loading, so at the instant the scene-load gate ran the town was
        /// legitimately empty, <see cref="SceneHasRepairables"/> answered false, and the one-shot
        /// bailed forever. Meanwhile StructureDamageVisuals installs UNCONDITIONALLY - so a
        /// structure could burn, with fire rendering, and the player had no way to repair anything
        /// for the rest of the session. Captured as <c>WallRepairController=ABSENT
        /// HubRepairAffordance=ABSENT WaveManager=Active</c> (owner F8 seq=2398).</para>
        ///
        /// <para>WHY THE CALLER IS StructureDamageVisuals. It already installs unconditionally and
        /// already knows a repairable exists - it is tracking one. Raising the install from there
        /// inverts the dependency so the repair surface FOLLOWS the town instead of racing it,
        /// which makes the two systems structurally symmetric. That asymmetry was the defect; a
        /// wider predicate could never have fixed it, because the predicate was not wrong - it was
        /// asked too early.</para>
        ///
        /// <para>Idempotent and cheap: a bool test after the first install, and TrySpawn itself
        /// early-returns on an existing instance, so a burst of placement restores cannot install
        /// two.</para>
        /// </summary>
        internal static void NotifyRepairableAppeared()
        {
            if (s_installedThisScene) return;
            TrySpawn();
        }

        private static void TrySpawn()
        {
            // WO-1436: asked BEFORE the structural test, and inside TrySpawn rather than at
            // the sceneLoaded hook, so BOTH entry paths are covered - the scene-load path and
            // NotifyRepairableAppeared(), which is the one that actually fired in the raid
            // (the enemy garrison's walls are restored after the load, exactly like a town's).
            if (TownHudSuppressedHere()) return;

            if (UnityEngine.Object.FindAnyObjectByType<HubRepairAffordance>() != null)
            {
                s_installedThisScene = true;
                return;
            }
            if (!SceneHasRepairables())
            {
                // NO SILENT FAILURE (CLAUDE.md section 12.2). This bare `return` used to be
                // invisible, and it is the single point at which the player can end up with
                // NO repair affordance at all: nothing else installs a WallRepairController
                // in a non-wave scene, so when this bails the Manage screen's "Repair all"
                // offer (which looks the controller up by FindFirstObjectByType) goes quiet
                // too. Meanwhile StructureDamageVisuals installs UNCONDITIONALLY, so fire
                // still renders. Fire with no repair option is exactly that asymmetry, and
                // this line is what proves or clears it in a capture.
                FlowTrace.Warn("Repair",
                    $"hub repair affordance NOT installed (scene='{SceneManager.GetActiveScene().name}') - " +
                    "no WallSegment/Gate/Building/Tower/DefenseTower/ArcaneTower/HarvestSite/collector " +
                    "found YET. Not terminal since WO-1024: StructureDamageVisuals calls " +
                    "NotifyRepairableAppeared() the moment it tracks a structure, so a town restored " +
                    "AFTER scene load installs the affordance then. A scene that genuinely has nothing " +
                    "to repair (Title / HeroSelect / menus) simply never raises that call.");
                return;
            }

            var go = new GameObject("HubRepairAffordance");
            go.AddComponent<HubRepairAffordance>();
            s_installedThisScene = true;
            FlowTrace.Step("Repair",
                $"hub repair affordance installed (scene='{SceneManager.GetActiveScene().name}')");
        }

        /// <summary>
        /// True when the scene has at least one repairable structure kind present.
        ///
        /// COVERAGE FIX: this gate previously tested only WallSegment / Gate / Building /
        /// Tower, while the backend it gates (<see cref="WallRepairController.RepairAllCost"/>)
        /// prices FOUR more surfaces - DefenseTower, ArcaneTower, HarvestSite and
        /// ResourceCollector. Those four are also full members of the damage-visual set, so a
        /// scene holding only those could show a structure ON FIRE and still refuse to install
        /// the one affordance able to repair it. The installer's reach now matches the
        /// backend's exactly; anything RepairAllCost can price, this can install for.
        /// </summary>
        private static bool SceneHasRepairables()
        {
            return UnityEngine.Object.FindAnyObjectByType<WallSegment>() != null
                || UnityEngine.Object.FindAnyObjectByType<Gate>() != null
                || UnityEngine.Object.FindAnyObjectByType<Building>() != null
                || UnityEngine.Object.FindAnyObjectByType<Tower>() != null
                || UnityEngine.Object.FindAnyObjectByType<DefenseTower>() != null
                || UnityEngine.Object.FindAnyObjectByType<ArcaneTower>() != null
                || UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.World.HarvestSite>() != null
                || ResourceCollectorRegistry.All.Count > 0;
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            BuildCanvas();
            SetVisible(false);
        }

        /// <summary>
        /// WO-1708 criterion 3 — react to a modal opening on the SAME frame it opens.
        /// <see cref="Refresh"/> alone would leave the card painted for up to
        /// <see cref="RefreshInterval"/> (0.75 s) after the pause menu appears, which is
        /// long enough to be exactly the screenshot the ticket carries.
        /// </summary>
        private void OnEnable()
        {
            PanelManager.OpenStateChanged -= OnPanelStateChanged;
            PanelManager.OpenStateChanged += OnPanelStateChanged;
        }

        private void OnDisable()
        {
            PanelManager.OpenStateChanged -= OnPanelStateChanged;
        }

        private void OnPanelStateChanged()
        {
            if (!PanelManager.AnyOpen)
            {
                // Re-decide on the very next Update rather than popping the card back
                // immediately — Refresh() owns the affordability/damage decision.
                _timer = 0f;
                return;
            }
            Announce(Vis.HiddenPanelOpen, default, default, false);
            SetVisible(false);
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = RefreshInterval;
            Refresh();
        }

        // =====================================================================
        //  Repair backend access
        // =====================================================================

        /// <summary>
        /// Resolves the one shared repair backend. Reuses an existing controller
        /// (a wave scene installs one); otherwise creates a LOGIC-ONLY controller
        /// (disabled so its own tap-to-select / highlight loop never runs in the
        /// hub) purely to price + apply Repair-All. Never a second repair system.
        /// </summary>
        private WallRepairController EnsureRepair()
        {
            if (_repair != null) return _repair;
            _repair = UnityEngine.Object.FindAnyObjectByType<WallRepairController>();
            if (_repair == null)
            {
                var cgo = new GameObject("WallRepair_HubEngine");
                _repair = cgo.AddComponent<WallRepairController>();
                // Logic-only: we call RepairAllCost / RepairAll directly; disabling
                // stops the controller's Update raycast so no unprompted selection
                // highlight appears in the hub.
                _repair.enabled = false;
                FlowTrace.Step("Repair",
                    "hub affordance self-installed a logic-only WallRepairController (no wave scene present)");
            }
            return _repair;
        }

        // =====================================================================
        //  Refresh - decide visibility + label, gated on out-of-battle + damage
        // =====================================================================

        private void Refresh()
        {
            // ***** WO-1708 criterion 3 — MODAL GATE. FIRST, and deliberately so. *****
            //
            // The owner's screenshots (flag_20260912-033141_00/_01.png) show `PAIR ALL` and
            // `Wood 15  Iron 7` painted across the PAUSE menu. That is NOT a sorting-order
            // bug: the pause modal takes overrideSorting at sortingOrder 31500
            // (PauseController.cs:195, ElarionUiKit.cs:967) so it genuinely draws ON TOP of
            // this canvas's 905 — but ElarionUiKit.Scrim paints the modal backdrop at
            // alpha 0.85 (ElarionUiKit.cs:125), so 15% of whatever sits underneath reads
            // straight through it. Raising this card's sorting order would make it worse and
            // lowering it changes nothing; the only fix is to not be drawn at all.
            //
            // The seam is PanelManager — the single modal arbiter. PauseController is a
            // registered client of it (RegisterBattleAllowed("Pause") at PauseController.cs:247,
            // NotifyOpened at :293), and PanelManager.cs:19 records that MobileInteractButton
            // already reads AnyOpen for exactly this reason ("prompt suppression"). Gating on
            // AnyOpen rather than on PauseController specifically is deliberate and is the
            // wider-correct scope: EVERY registered modal is a full-screen scrimmed surface
            // built by the same kit, so every one of them has the same 0.85 translucency and
            // the same bleed-through. It also keeps this file free of a DeNelle.Settings
            // dependency it does not have.
            //
            // Placed before FindAnyObjectByType<WaveManager>() / EnsureRepair() so the hidden
            // branch has no side effects — in particular it must not self-install a
            // WallRepair_HubEngine while a menu is up.
            if (PanelManager.AnyOpen)
            {
                Announce(Vis.HiddenPanelOpen, default, default, false);
                SetVisible(false);
                return;
            }

            // OUT-OF-BATTLE gate. A pure hub scene (MainCastle_Hall - no WaveManager)
            // always qualifies. In a scene that DOES run waves, show only in the calm
            // postures (Idle before the first wave, Countdown between waves); hide
            // during Active/Breached (a wave is on) and Complete/Defeated (the one-shot
            // EndState "Repair All" CTA owns that moment). This is the farm-then-return
            // hub posture the owner asked for, never mid-battle chrome.
            var wave = UnityEngine.Object.FindAnyObjectByType<WaveManager>();
            bool outOfBattle = wave == null
                || wave.Phase == WavePhase.Idle || wave.Phase == WavePhase.Countdown;
            if (!outOfBattle)
            {
                // DISTINCT from the nothing-damaged case below. This branch is the one that
                // fires while a wave is Active/Breached - i.e. exactly when structures are
                // catching fire - so "on fire with no repair option" during a wave is this
                // line, and it is currently BY DESIGN (repair is a between-waves action).
                Announce(Vis.HiddenInBattle, default, default, false);
                SetVisible(false);
                return;
            }

            var repair = EnsureRepair();
            CoreCost cost = repair != null ? repair.RepairAllCost() : default;
            if (WallRepairController.MaterialsZero(cost))
            {
                // Nothing damaged - no affordance. If a structure is visibly ON FIRE while
                // this branch is the live state, the damage-visual set and the repair set
                // disagree about the same structure, and THAT is the defect.
                Announce(Vis.HiddenNothingDamaged, default, default, false);
                SetVisible(false);
                return;
            }

            bool affordable = repair != null && repair.CanAffordMaterials(cost);
            CoreCost shortfall = Shortfall(cost);

            string shortfallKey = CostKey(shortfall);
            if (!affordable && shortfallKey == _acknowledgedShortfall)
            {
                // The player explicitly closed this exact refusal. Do not reopen it every
                // 0.75 seconds; a changed wallet/shortfall produces a new key and may surface.
                SetVisible(false);
                return;
            }

            SetVisible(true);
            if (_acknowledgeButton != null) _acknowledgeButton.gameObject.SetActive(!affordable);
            if (affordable)
            {
                _acknowledgedShortfall = string.Empty;
                // Copy lives in WallRepairStrings (LOCALIZE). The old "  (tap)" suffix is
                // GONE - a button already reads as tappable, and those glyphs were part of
                // what pushed this line into the kit's ellipsis on the owner's Seeker.
                _label.text = WallRepairStrings.HubRepairAllLabel + "\n" +
                              WallRepairController.DescribeMaterials(cost);
                WarnIfClipped("affordable");
                _label.color = ElarionUi.Parchment;
                if (_buttonImg != null) _buttonImg.color = ElarionUi.ConfirmFace;
                Announce(Vis.AvailableAffordable, cost, shortfall, true);
            }
            else
            {
                // Meaning never by colour alone (kit rule): the shortfall is in TEXT.
                // Copy lives in WallRepairStrings (LOCALIZE) and is deliberately SHORT: the
                // owner's Seeker rendered this as "NEED MORE TO REP... / 115 iron short - go
                // fa...". Shorter copy beats a wider container - the rect is unchanged.
                _label.text = WallRepairStrings.HubNeedMoreLabel + "\n" +
                              string.Format(WallRepairStrings.HubShortfallFormat,
                                            WallRepairController.DescribeMaterials(shortfall));
                WarnIfClipped("short");
                _label.color = ElarionUi.Parchment;
                if (_buttonImg != null) _buttonImg.color = ElarionUi.DangerFace;
                Announce(Vis.AvailableShort, cost, shortfall, false);
            }
        }

        /// <summary>FlowTrace the affordance state, but only when it CHANGES (not every poll).</summary>
        private void Announce(Vis state, CoreCost cost, CoreCost shortfall, bool affordable)
        {
            if (state == _last) return;
            _last = state;
            switch (state)
            {
                case Vis.HiddenInBattle:
                    FlowTrace.Step("Repair",
                        "hub repair affordance: HIDDEN because a wave is Active/Breached " +
                        "(repair is a between-waves action) - structures can burn here with no repair button");
                    break;
                case Vis.HiddenNothingDamaged:
                    FlowTrace.Step("Repair",
                        "hub repair affordance: HIDDEN because RepairAllCost() priced NOTHING " +
                        "(the backend sees no damaged structure). If something is visibly on fire " +
                        "right now, the damage-visual set and the repair set disagree.");
                    break;
                case Vis.HiddenPanelOpen:
                    FlowTrace.Step("Repair",
                        $"hub repair affordance: HIDDEN because a modal is open (PanelManager " +
                        $"holder='{PanelManager.OpenPanelName}'). WO-1708 criterion 3 - the kit's " +
                        "modal scrim is alpha 0.85, so an un-hidden card at sortingOrder 905 reads " +
                        "THROUGH the pause panel even though 31500 draws over it.");
                    break;
                case Vis.AvailableAffordable:
                    FlowTrace.Step("Repair",
                        $"hub repair affordance AVAILABLE + affordable: cost {WallRepairController.DescribeMaterials(cost)}, wallet={WalletLine()}");
                    break;
                case Vis.AvailableShort:
                    FlowTrace.Step("Repair",
                        $"hub repair affordance AVAILABLE + short: cost {WallRepairController.DescribeMaterials(cost)}, " +
                        $"short {WallRepairController.DescribeMaterials(shortfall)}, wallet={WalletLine()}");
                    break;
            }
        }

        // =====================================================================
        //  Click - repair when affordable, otherwise refuse + show shortfall
        // =====================================================================

        private void OnClick()
        {
            var repair = EnsureRepair();
            if (repair == null) return;

            CoreCost cost = repair.RepairAllCost();
            if (WallRepairController.MaterialsZero(cost))
            {
                Refresh();
                return;
            }

            if (!repair.CanAffordMaterials(cost))
            {
                // Owner ruling: do NOT spend when they cannot afford. Show the shortfall.
                CoreCost shortfall = Shortfall(cost);
                // WARN, not Step: a refusal is an anomaly the PLAYER FELT - a tap that did
                // nothing. And the REPEAT is the signal (it means they found no exit), so the
                // dead taps are COUNTED rather than dropped.
                //
                // NOTE ON FlowTrace.Throttle: it logs via Sink.Info and silently DISCARDS the
                // suppressed calls, so it cannot carry Warn severity and cannot count. The rate
                // gate is therefore local, and the tally rides in the message instead.
                _refusedTaps++;
                if (Time.unscaledTime >= _nextRefusedWarnAt)
                {
                    _nextRefusedWarnAt = Time.unscaledTime + RefusedWarnInterval;
                    FlowTrace.Warn("Repair",
                        $"hub repair REFUSED (cannot afford) - dead tap #{_refusedTaps} this session: " +
                        $"cost {WallRepairController.DescribeMaterials(cost)}, " +
                        $"short {WallRepairController.DescribeMaterials(shortfall)}, wallet={WalletLine()} - farm then return");
                }
                Refresh();   // re-render the shortfall
                return;
            }

            // Affordable: repair everything (worst-first). RepairAll un-breaks broken
            // Towers (Tower.Repair clears the broken shell + re-enables its fire loop)
            // and spends through the SAME construction-economy path as build placement.
            var result = repair.RepairAll();
            FlowTrace.Step("Repair",
                $"hub repair AFFORDED: repaired={result.repairedCount} " +
                $"spent {WallRepairController.DescribeMaterials(result.spent)} " +
                $"remaining={result.remainingDamaged} wallet={WalletLine()}");
            _last = Vis.Uninit;   // force a fresh Announce on the next Refresh
            Refresh();
        }

        // =====================================================================
        //  Cost helpers
        // =====================================================================

        /// <summary>Per-material amount the wallet is missing to cover <paramref name="cost"/>.</summary>
        private static CoreCost Shortfall(CoreCost cost)
        {
            var econ = EconomyService.Instance;
            int w = econ != null ? econ.Wood : 0;
            int i = econ != null ? econ.Iron : 0;
            int f = econ != null ? econ.Stone : 0;
            return new CoreCost
            {
                wood = Mathf.Max(0, cost.wood - w),
                iron = Mathf.Max(0, cost.iron - i),
                stone = Mathf.Max(0, cost.stone - f),
                crystals = 0,
            };
        }

        private static string CostKey(CoreCost cost) =>
            $"{cost.wood}:{cost.iron}:{cost.stone}:{cost.crystals}";

        /// <summary>Compact wallet line for FlowTrace (matches WallRepairController's format).</summary>
        private static string WalletLine()
        {
            var econ = EconomyService.Instance;
            if (econ == null) return "<no EconomyService>";
            return $"W{econ.Wood} I{econ.Iron} F{econ.Stone}";
        }

        // =====================================================================
        //  UI - one code-built uGUI canvas + button (ElarionUiKit; no UXML)
        // =====================================================================

        private void BuildCanvas()
        {
            if (_canvas != null) return;

            _canvas = new GameObject("HubRepairCanvas");
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = _canvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvas.AddComponent<GraphicRaycaster>();

            // *** WO-1219 - THIS CARD NOW LANDS IN THE ONE RESERVED TOAST ZONE. ***
            //
            // IT HAS BEEN IN TWO WRONG SEATS AND THE ROOT CAUSE OF BOTH IS THE SAME: a card
            // authored in DeNelle.Village cannot see what DeNelle.HUD has put in the corner it
            // picks. The first seat (x 0.015..0.205, reasoned as "chrome clusters top and bottom,
            // so a mid-LEFT box is the lowest-collision seat") landed on the minimap plate, the
            // region status line AND the gear at once - the owner captured exactly that
            // (tmp/shield-seat-101829.png: "REPAIR ALL / Wood 155 Iron 78" drawn straight across
            // the minimap). The second was a hand-picked free band: correct on the day, and
            // re-derived from HudAreasHost rects this module does not own and is not told about
            // when they move.
            //
            // THE FIX IS NOT A THIRD HAND-PICKED SEAT. It is the shared convention:
            // HudLayoutBands.ToastZone - centred above the action bar, verified clear of every
            // HUD band by HudUiRegression check 8, and used by every transient toast on this
            // screen whichever module raises it. Do NOT author a rect here again; if the zone is
            // wrong, it is wrong for everybody and it moves in ONE place.
            _button = ElarionUiKit.Button(_canvas.transform, "REPAIR ALL", ElarionUiKit.ButtonKind.Confirm,
                ToastZoneMin(0f, 0.72f), ToastZoneMax(0f, 0.72f), OnClick);
            if (_button != null)
            {
                _buttonImg = _button.GetComponent<Image>();
                _label = _button.GetComponentInChildren<TextMeshProUGUI>();
                if (_label != null)
                {
                    // THE CLIP BUG (owner Seeker capture). ElarionUiKit.Button runs
                    // FitSingleLine on every label, arming NoWrap + Ellipsis. The old code
                    // here then opted OUT with enableAutoSizing=false / fontSize=26 without
                    // clearing either mode - so single-line ellipsis stayed armed over a
                    // deliberately TWO-LINE string and each line clipped independently, 26px
                    // sat BELOW the kit's own mobile floor (FontFloor=30), and disabling
                    // autosizing also disarmed the kit's post-layout UiKitTextFitGuard (it
                    // only manipulates fontSizeMin/Max). FitBlock is the kit's multi-line
                    // answer: Normal wrap + Truncate + bounded autosize, guard RE-ARMED.
                    ElarionUiKit.FitBlock(_label, ElarionUiKit.FontFloor);
                    var lrt = _label.rectTransform;
                    FlowTrace.Step("Repair",
                        $"hub repair label fitted: rect {lrt.rect.width:F0}x{lrt.rect.height:F0} " +
                        $"anchors {lrt.anchorMin}-{lrt.anchorMax} wrap={_label.textWrappingMode} " +
                        $"overflow={_label.overflowMode} autoSize={_label.enableAutoSizing} " +
                        $"size=[{_label.fontSizeMin:F0}..{_label.fontSizeMax:F0}]");
                }
            }

            // PROD-014(b): an unaffordable repair is a refusal card, not a dead button.
            // The shared labeled Close is visible only in that state. It routes through the
            // controller's one existing cancellation path, which clears selection + marker.
            // Travels with the button (WO-1219): same width, same height, immediately to its
            // right, still inside the 0.340..0.770 free band.
            // Travels with the button, inside the SAME reserved zone - never spilling out of it.
            var ack = HudLayoutBands.ToastZoneSlice(0.76f, 1f);
            _acknowledgeButton = ElarionUiKit.ObsidianCloseButton(_canvas.transform,
                AcknowledgeRefusal, new Vector4(ack.xMin, ack.yMin, ack.xMax, ack.yMax));
            if (_acknowledgeButton != null) _acknowledgeButton.gameObject.SetActive(false);
        }

        // WO-1219: the card's rect, taken as a horizontal slice of the shared toast zone. Two
        // tiny helpers rather than a local, because the Button factory takes min/max Vector2s.
        private static Vector2 ToastZoneMin(float from, float to)
        {
            var r = HudLayoutBands.ToastZoneSlice(from, to);
            return new Vector2(r.xMin, r.yMin);
        }

        private static Vector2 ToastZoneMax(float from, float to)
        {
            var r = HudLayoutBands.ToastZoneSlice(from, to);
            return new Vector2(r.xMax, r.yMax);
        }

        private void AcknowledgeRefusal()
        {
            var repair = EnsureRepair();
            if (repair != null)
            {
                _acknowledgedShortfall = CostKey(Shortfall(repair.RepairAllCost()));
                repair.CancelRepair();
            }
            FlowTrace.Step("Repair", "hub repair refusal acknowledged; selection and marker cleared.");
            SetVisible(false);
        }

        /// <summary>
        /// CLIP DETECTOR. The owner's phone reported clipped repair copy and nothing in the log
        /// could prove it - TMP drops the glyphs silently. This Warns whenever the label actually
        /// renders fewer characters than it was given, so the next clip report is one grep instead
        /// of an argument over a screenshot.
        ///
        /// <para>textInfo is only populated after a layout pass, so ForceMeshUpdate() runs first -
        /// the same precedent the kit's own UiKitTextFitGuard uses before reading characterCount
        /// (ElarionUiKitObsidian.cs, the guard's post-layout check).</para>
        ///
        /// <para>Deliberately a Warn, not a Fail: characterCount is TMP's PARSED count, so a
        /// newline in the two-line string can read as one character short on its own. It points at
        /// the label; it does not convict it.</para>
        /// </summary>
        private void WarnIfClipped(string which)
        {
            if (!FlowTrace.Enabled || _label == null) return;
            _label.ForceMeshUpdate();
            var info = _label.textInfo;
            if (info == null) return;
            string raw = _label.text ?? string.Empty;
            if (info.characterCount >= raw.Length) return;
            // Refresh() polls every 0.75s, so report each DISTINCT clipped string once rather
            // than flooding the capture with the same line.
            if (raw == _lastClipReported) return;
            _lastClipReported = raw;
            var lrt = _label.rectTransform;
            string flat = raw.Replace("\n", " / ");
            FlowTrace.Warn("Repair",
                $"hub repair label CLIPPING ({which}): rendered {info.characterCount} of {raw.Length} chars - " +
                $"text='{flat}' rect {lrt.rect.width:F0}x{lrt.rect.height:F0} " +
                $"wrap={_label.textWrappingMode} overflow={_label.overflowMode} " +
                $"fontSize={_label.fontSize:F0} size=[{_label.fontSizeMin:F0}..{_label.fontSizeMax:F0}]");
        }

        private void SetVisible(bool visible)
        {
            if (_canvas != null && _canvas.activeSelf != visible)
                _canvas.SetActive(visible);
        }
    }
}
