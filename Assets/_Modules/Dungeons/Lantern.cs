// =============================================================================
// Lantern — the Keeper's hero-attached dungeon lantern (Week 6).
// -----------------------------------------------------------------------------
// Port spec Part 3 row:
//   src/modules/dungeons/lantern/ -> _Modules/Dungeons/Lantern.cs + LanternLight.prefab
// Port spec Part 5 Week 6: "PointLight attached to hero, intensity falls over
// time (oil mechanic). Refill at oil stones. Audio: lantern-flicker.mp3 at low oil."
//
// C# port of src/modules/dungeons/3d/lantern/DungeonLantern3D.tsx. The React
// component is a warm THREE.PointLight that follows the Keeper; this is its
// Unity incarnation — a `UnityEngine.Light` of type Point, parented to (or
// following) the hero rig.
//
// ── The oil mechanic (design §4 — the lantern intro/payoff beats) ──
// The lantern burns OIL. Oil drains slowly while the run is active; as oil
// drops, the light's range AND intensity fall with it, so the dark closes in.
// Walking into an oil stone's radius refills the flask. At low oil the
// lantern-flicker SFX plays and the flame breathing speeds up — a felt warning
// to find an oil stone.
//
// ── Tincture light-shrink (design §4 Beat 6) ──
// The Apothecary mini-boss's "Tincture" special shrinks the lantern reach to
// 50% for 6 seconds. C# port of lanternDebuff.ts — see TriggerTincture().
//
// The flame "breathes" — a slow intensity oscillation — UNLESS reduced motion
// is set, in which case the intensity is pinned to its mean (a calm light).
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Dungeons
{
    /// <summary>
    /// The Keeper's lantern — a hero-following point light whose reach and
    /// brightness fall as oil drains, refilled at oil stones. Port of the React
    /// <c>DungeonLantern3D</c> + <c>lanternDebuff</c>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class Lantern : MonoBehaviour
    {
        // ── Tuning — radius model (DungeonLantern3D BASE_DISTANCE etc.) ───────

        [Header("Light reach (world units)")]
        [Tooltip("Always-on lantern reach at full oil — the §5-amendment base ~6u.")]
        [SerializeField] private float _baseRange = 6f;

        [Tooltip("Extra reach when the equipped Lightbearer Cloak is owned (+1 tile).")]
        [SerializeField] private float _cloakBonusRange = 1.5f;

        [Tooltip("The lantern's mean point-light intensity at full oil.")]
        [SerializeField] private float _baseIntensity = 9f;

        [Tooltip("Warm lantern colour (matches the gate-Wardens' torch tone).")]
        [SerializeField] private Color _lanternColor = new Color(0.965f, 0.788f, 0.478f);

        [Tooltip("Height above the hero pivot the lantern light sits — chest height.")]
        [SerializeField] private float _lanternHeight = 1.4f;

        // ── Tuning — oil mechanic ────────────────────────────────────────────

        [Header("Oil")]
        [Tooltip("Maximum oil in the flask (arbitrary units — a full flask).")]
        [SerializeField] private float _maxOil = 100f;

        [Tooltip("Oil drained per second while the run is active.")]
        [SerializeField] private float _oilDrainPerSec = 1.6f;

        [Tooltip("Below this fraction of max oil, the lantern reads as 'low oil' " +
                 "— the flicker SFX plays and the flame breathes faster.")]
        [SerializeField, Range(0f, 1f)] private float _lowOilFraction = 0.25f;

        [Tooltip("Floor on the oil-driven dimming — the lantern never fully dies, " +
                 "so an out-of-oil Keeper can still find an oil stone.")]
        [SerializeField, Range(0f, 1f)] private float _minOilLightFraction = 0.35f;

        /// <summary>
        /// WO-1805 Lane C: the SHIPPING final-warning window, as a named const rather than an
        /// inline literal on the field below. The rail row dungeon.lanternFinalWarningSec ships at
        /// exactly this number, and DungeonLanternTeachRegression pins the two against each other
        /// instead of against a copied 30 - a default written twice is a default that rots
        /// (CLAUDE.md sections 2 / 5 / 8).
        /// </summary>
        public const float DefaultFinalWarningSeconds = 30f;

        [Tooltip("Seconds before empty when the flame begins its final visible collapse.")]
        [SerializeField] private float _finalWarningSeconds = DefaultFinalWarningSeconds;

        [Tooltip("Tight safety halo left at zero oil. It reveals the Keeper's immediate footing, " +
                 "not the route ahead.")]
        [SerializeField] private float _emptySafetyRange = 1.35f;

        [Tooltip("Mean light intensity at zero oil. Keeps the immediate halo readable.")]
        [SerializeField] private float _emptySafetyIntensity = 0.8f;

        // ── Tuning — breathing + Tincture (lanternDebuff.ts) ─────────────────

        [Header("Flame breathing")]
        [Tooltip("Breathing depth — +/- this fraction of the base intensity.")]
        [SerializeField, Range(0f, 0.5f)] private float _breathAmplitude = 0.12f;

        [Tooltip("Breathing rate at full oil — a slow ~0.2 Hz flame breath.")]
        [SerializeField] private float _breathHz = 0.2f;

        [Tooltip("Breathing-rate multiplier applied when oil is low — a faster, " +
                 "anxious flicker that reads as a warning.")]
        [SerializeField] private float _lowOilBreathMult = 3f;

        [Header("Tincture debuff (Apothecary boss — design §4 Beat 6)")]
        [Tooltip("Lantern reach multiplier while a Tincture is active (50%).")]
        [SerializeField, Range(0f, 1f)] private float _tinctureRangeMul = 0.5f;

        [Tooltip("How long a single Tincture cast dims the lantern (6s).")]
        [SerializeField] private float _tinctureDurationSec = 6f;

        [Tooltip("How fast the reach eases toward its target each second.")]
        [SerializeField] private float _rangeLerpPerSec = 9f;

        [Header("Audio")]
        [Tooltip("lantern-flicker.mp3 — looped while oil is low.")]
        [SerializeField] private AudioSource _flickerAudio;

        [Header("Accessibility")]
        [Tooltip("When true, the flame breathing is pinned flat (reduced motion).")]
        [SerializeField] private bool _reducedMotion;

        // ── Runtime ──────────────────────────────────────────────────────────

        private Light _light;
        private DungeonController _controller;
        private Transform _hero;
        private IReadOnlyList<DungeonOilStone> _oilStones;
        private readonly HashSet<string> _spentOilStones = new HashSet<string>();
        /// <summary>
        /// WO-1001 slice 5: composed dungeons have no full DungeonController — when true,
        /// oil drains without a cottage run state (ComposedDungeonBootstrap arms this).
        /// </summary>
        private bool _standaloneRun;

        /// <summary>Current oil in the flask, 0..maxOil.</summary>
        private float _oil;

        /// <summary>The live, eased point-light range (rides below the full reach).</summary>
        private float _liveRange;

        /// <summary>Seconds remaining on the active Tincture dimming, or 0.</summary>
        private float _tinctureRemaining;

        /// <summary>True while the equipped Lightbearer Cloak is owned (+1 tile reach).</summary>
        private bool _cloakOwned;
        private bool _fogWasEnabled;
        private FogMode _fogModeBeforeWarning;
        private float _fogStartBeforeWarning;
        private float _fogEndBeforeWarning;
        private bool _darknessGradeApplied;
        private bool _expeditionBlessingApplied;
        private float _expeditionOilMultiplier = 1f;

        // ── WO-1805 section 7: the drain path's instrumentation state ────────
        // ⛔ Before this ticket NOTHING on the drain path logged. No capture on disk showed a run
        // reaching empty, and the WO could not prove one ever had - not because it never happened
        // but because a run that went dark left no trace to find. These four edges are now
        // permanent (CLAUDE.md section 12: instrumentation is never stripped, only flagged off).
        private bool _finalWarningReported;
        private bool _flaskEmptyReported;
        private bool _darknessReported;
        private float _darknessEnteredAtRealtime;
        private float _darkSecondsAccrued;

        /// <summary>
        /// WO-1805 Lane A: raised the moment an oil stone is SPENT (the first one of these is the
        /// teach's completion beat - the player has performed the thing the intro described).
        /// <para>
        /// Before this event <see cref="CheckOilStones"/> mutated the flask and returned with no
        /// event and no trace, so nothing outside this class could observe a refill at all -
        /// which is why the lantern teach had no way to latch closed on the player's own action.
        /// </para>
        /// </summary>
        public event System.Action OilStoneUsed;

        /// <summary>
        /// WO-1805 Lane A: raised ONCE per armed lantern, on the edge where
        /// <see cref="IsFinalWarning"/> first becomes true - i.e. the instant the fog wall and the
        /// range collapse begin. The host listens rather than polling <c>IsFinalWarning</c> from an
        /// Update, so the "your flame gutters" beat cannot fire twice or drift a frame.
        /// </summary>
        public event System.Action FinalWarningEntered;

        // ── Read-only state ──────────────────────────────────────────────────

        /// <summary>Oil remaining as a 0..1 fraction of a full flask.</summary>
        public float OilFraction => _maxOil > 0f ? Mathf.Clamp01(_oil / _maxOil) : 0f;

        /// <summary>True when oil has dropped into the warning band.</summary>
        public bool IsLowOil => OilFraction <= _lowOilFraction;

        /// <summary>
        /// WO-1001 slice 6: true when the flask is critically low or empty — the dark
        /// has closed in. Drives higher ambush odds (RandomEncounterTable darkness mult)
        /// and the "push vs extract" tension for legendary loot gates.
        /// </summary>
        public bool IsInDarkness => OilFraction <= Mathf.Min(_lowOilFraction, 0.12f);

        /// <summary>True during the final visible burn-down window.</summary>
        public bool IsFinalWarning => EstimatedSecondsRemaining <= _finalWarningSeconds;

        /// <summary>0 at the start of the final warning, 1 when the flask is empty.</summary>
        public float FinalWarningProgress => _finalWarningSeconds > 0f
            ? Mathf.Clamp01(1f - EstimatedSecondsRemaining / _finalWarningSeconds)
            : (OilFraction <= 0f ? 1f : 0f);

        /// <summary>
        /// The oil fraction at or below which the lantern reads as "low oil" —
        /// the warning band the HUD oil meter tints amber/red against.
        /// </summary>
        public float LowOilFraction => _lowOilFraction;

        /// <summary>
        /// Estimated seconds of light left before the flask runs dry, at the
        /// current drain rate (a Tincture does not change the drain rate, only
        /// reach). Returns <see cref="float.PositiveInfinity"/> when the lantern
        /// is not draining — e.g. before a run starts. The HUD reads this for the
        /// duration readout (owner acceptance checklist: make the duration legible).
        /// </summary>
        public float EstimatedSecondsRemaining =>
            _oilDrainPerSec > 0f ? _oil / _oilDrainPerSec : float.PositiveInfinity;

        /// <summary>True while the Apothecary boss's Tincture is shrinking the reach.</summary>
        public bool IsTinctureActive => _tinctureRemaining > 0f;

        /// <summary>The lantern's full (un-debuffed, full-oil) reach in world units.</summary>
        public float FullRange => _baseRange + (_cloakOwned ? _cloakBonusRange : 0f);

        /// <summary>Adds a bounded portion of a flask. Used by scarce dungeon field stills.</summary>
        public bool AddOilFraction(float fraction)
        {
            if (fraction <= 0f || _oil >= _maxOil - 0.01f) return false;
            _oil = Mathf.Min(_maxOil, _oil + _maxOil * Mathf.Clamp01(fraction));
            return true;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            ApplyBalanceData();
            _light = GetComponent<Light>();
            _light.type = LightType.Point;
            _light.color = _lanternColor;
            _light.intensity = _baseIntensity;
            _light.shadows = LightShadows.None; // the lantern is a fill light, not a shadow caster
            _oil = _maxOil;
            _liveRange = FullRange;
            _light.range = _liveRange;
            _fogWasEnabled = RenderSettings.fog;
            _fogModeBeforeWarning = RenderSettings.fogMode;
            _fogStartBeforeWarning = RenderSettings.fogStartDistance;
            _fogEndBeforeWarning = RenderSettings.fogEndDistance;
        }

        /// <summary>
        /// WO-1112 (owner ruling 2026-08-16: "we should make the lanterns last triple that at
        /// minimum"): the oil tuning comes from DATA, not from these serialized defaults.
        /// <para>
        /// ⚠ THIS DELIBERATELY OVERRIDES THE SERIALIZED FIELDS, IN BOTH PIPELINES. The
        /// hand-built Dungeon_HealersCottage scene serialized _maxOil 100 / _oilDrainPerSec 1.6
        /// — byte-identical to the code defaults, i.e. it was never separately tuned and had the
        /// same 62.5s burn. Letting the scene's copy win would mean tuning the composed dungeons
        /// while the cottage silently kept the old number, which is exactly the drift that made
        /// this a hidden knob in the first place. ONE authority: dungeon-balance.json.
        /// </para>
        /// Guarded and self-reporting: an unreadable file falls back to the catalog's built-in
        /// defaults (which mirror the authored json), never to a hard failure.
        /// </summary>
        private void ApplyBalanceData()
        {
            float priorMax = _maxOil;
            float priorDrain = _oilDrainPerSec;
            Guard.Try("Dungeon", "apply lantern balance data", () =>
            {
                _maxOil = DungeonLanternBalance.MaxOil;
                _oilDrainPerSec = DungeonLanternBalance.OilDrainPerSec;
            });

            // WO-1805 Lane C: the REMOTE RAIL sits OVER the authored json, and only when the owner
            // has actually moved a row.
            //
            // ⚠ WHY THE ROW IS COMPARED TO ITS OWN SHIPPING DEFAULT INSTEAD OF SIMPLY WINNING. The
            // rail carries no floats (TunableKind is Bool|Int only), so the drain rides as an
            // integer x100 whose default IS today's authored 0.50/s. A row that always won would
            // make dungeon-balance.json DEAD DATA the first time anyone re-authored it: the json
            // would say 0.40 and the build would keep burning 0.50 from a default nobody set. So
            // the rail is a DEVIATION detector - at the shipping default the json stays the single
            // authority and the behaviour is bit-identical to the pre-ticket build, which is the
            // whole promise of an identity default (RemoteTunables.cs says so in capitals).
            string drainProvenance = "json";
            string warnProvenance = "code-default";
            Guard.Try("Dungeon", "apply lantern remote tunables", () =>
            {
                int drainX100 = RemoteTunables.Int(RemoteTunables.KeyDungeonLanternDrainPerSecX100);
                if (drainX100 != RemoteTunables.DungeonLanternDrainPerSecX100Default && drainX100 > 0)
                {
                    _oilDrainPerSec = Mathf.Max(0.01f, drainX100 / 100f);
                    drainProvenance = "rail(" + RemoteTunables.KeyDungeonLanternDrainPerSecX100 + "=" + drainX100 + ")";
                }

                int warnSec = RemoteTunables.Int(RemoteTunables.KeyDungeonLanternFinalWarningSec);
                if (warnSec != RemoteTunables.DungeonLanternFinalWarningSecDefault && warnSec >= 0)
                {
                    _finalWarningSeconds = Mathf.Max(0f, warnSec);
                    warnProvenance = "rail(" + RemoteTunables.KeyDungeonLanternFinalWarningSec + "=" + warnSec + ")";
                }
            });

            float secondsToEmpty = _oilDrainPerSec > 0f ? _maxOil / _oilDrainPerSec : 0f;
            float secondsToLatch = (1f - Mathf.Min(_lowOilFraction, 0.12f)) * secondsToEmpty;
            FlowTrace.Step("Dungeon",
                $"Lantern balance applied on '{name}': maxOil {priorMax:F0}->{_maxOil:F0} " +
                $"drain {priorDrain:F2}->{_oilDrainPerSec:F2}/s [{drainProvenance}] = " +
                $"{secondsToEmpty:F0}s to empty, " +
                $"~{secondsToLatch:F0}s to the darkness latch, " +
                $"final-warning window {_finalWarningSeconds:F0}s [{warnProvenance}].");
        }

        /// <summary>
        /// Stops the looped flicker SFX if the lantern is disabled or torn down
        /// mid-run while oil is low — otherwise the loop plays on after the run
        /// ends (the flicker is started/stopped only inside <see cref="Update"/>).
        /// </summary>
        private void OnDisable()
        {
            if (_flickerAudio != null && _flickerAudio.isPlaying)
                _flickerAudio.Stop();
            RestoreDungeonFog();

            // WO-1805 section 7: the DURATION half of the darkness question. The edge above says it
            // happened; only teardown can say for how long, and "the player spent 4 of a 6 minute
            // run in a 1.35u halo" is the sentence the owner's report was describing.
            //
            // ⚠ THIS LANTERN IS ADDED TO THE CARRIED HERO (ComposedDungeonHost creates it under the
            // Player when the bake placed none), and the carried hero root is DontDestroyOnLoad - so
            // OnDisable here is NOT reliably the dungeon's exit. The line is still worth having and
            // is honest about what it is: it reports the accrual since this component was last
            // armed. Recorded as an open question in the WO-1805 RESULT rather than silently
            // assumed to be scene-scoped.
            if (_darkSecondsAccrued <= 0f) return;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            FlowTrace.Step("DungeonOil",
                $"darkness TOTAL on lantern teardown in scene='{scene}': {_darkSecondsAccrued:F0}s spent below the " +
                $"0.12 latch (first armed at t={_darknessEnteredAtRealtime:F0}s), oil={_oil:F0}/{_maxOil:F0} at teardown.");
        }

        /// <summary>
        /// Wires the lantern to the dungeon: the controller (for run state), the
        /// hero transform it follows, and the layout's oil-stone refill points.
        /// Called by <see cref="DungeonController"/> on dungeon load.
        /// </summary>
        public void Configure(
            DungeonController controller,
            IReadOnlyList<DungeonOilStone> oilStones,
            Transform hero)
        {
            _controller = controller;
            _standaloneRun = false;
            _oilStones = oilStones;
            _spentOilStones.Clear();
            ResetOilEdgeReports();
            _hero = hero;
            _oil = _maxOil;
            _liveRange = FullRange;
            ApplyExpeditionBlessing();
        }

        /// <summary>
        /// WO-1001 slice 5: arm the lantern on a composed dungeon (no DungeonController).
        /// Oil drains for the whole scene visit; oil stones still refill on proximity.
        /// </summary>
        public void ConfigureStandalone(IReadOnlyList<DungeonOilStone> oilStones, Transform hero)
        {
            _controller = null;
            _standaloneRun = true;
            _oilStones = oilStones;
            _spentOilStones.Clear();
            ResetOilEdgeReports();
            _hero = hero;
            _oil = _maxOil;
            _liveRange = FullRange;
            ApplyExpeditionBlessing();
            FlowTrace.Step("Dungeon",
                $"Lantern.ConfigureStandalone: oil={_oil:F0} stones={oilStones?.Count ?? 0} " +
                $"hero='{(hero != null ? hero.name : "<null>")}' (composed path)");
        }

        /// <summary>
        /// Sets whether the equipped Lightbearer Cloak is owned. The Root Cellar
        /// chest grants the Cloak; on equip the reach grows by one tile
        /// (design §4 Beat 4 / acceptance criterion 6).
        /// </summary>
        public void SetCloakOwned(bool owned)
        {
            _cloakOwned = owned;
        }

        /// <summary>Sets the reduced-motion preference — pins the flame breathing flat.</summary>
        public void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
        }

        // ── Per-frame ────────────────────────────────────────────────────────

        private void Update()
        {
            FollowHero();

            bool runActive = _standaloneRun
                || (_controller != null
                    && _controller.RuntimeState != null
                    && _controller.RuntimeState.RunActive);

            if (runActive)
            {
                DrainOil(Time.deltaTime);
                CheckOilStones();
            }

            TrackOilEdges();
            TickTincture(Time.deltaTime);
            ApplyRange(Time.deltaTime);
            ApplyIntensity();
            ApplyDarknessVisibility();
            DriveFlickerAudio();
        }

        /// <summary>
        /// Re-arms the WO-1805 one-shot edge reports for a fresh visit. A lantern is re-Configured
        /// per dungeon entry (and the composed one may be re-used on the carried hero), so the
        /// edges must be per-VISIT or the second dungeon of a session would report nothing.
        /// </summary>
        private void ResetOilEdgeReports()
        {
            _finalWarningReported = false;
            _flaskEmptyReported = false;
            _darknessReported = false;
            _darknessEnteredAtRealtime = 0f;
            _darkSecondsAccrued = 0f;
        }

        /// <summary>
        /// WO-1805 section 7 - THE TWO STATE EDGES THE DRAIN PATH NEVER ANNOUNCED, plus the event
        /// the lantern teach's darkness beat rides on.
        /// <para>
        /// Both are edge-triggered and reported exactly ONCE per armed lantern, never per frame: a
        /// per-frame line on this path would evict the boot window out of the Android logcat ring
        /// and destroy the evidence it was added to collect (memory
        /// <c>logcat-ring-buffer-destroys-evidence</c>; the same reasoning the fog writer above
        /// carries). The elapsed-dark TOTAL is accrued here and printed on teardown, because "how
        /// long was the player actually in the dark" is the number the owner's report was about and
        /// it cannot be known at the edge.
        /// </para>
        /// </summary>
        private void TrackOilEdges()
        {
            if (IsFinalWarning && !_finalWarningReported)
            {
                _finalWarningReported = true;
                int total = _oilStones != null ? _oilStones.Count : 0;
                // ⚠ Step, NOT Once. FlowTrace.Once is SESSION-scoped (FlowTrace.cs:228-237 - a
                // key is added to s_seen and only ResetSession clears it), so a fixed key here
                // would print for the FIRST dungeon of a session and stay silent for every one
                // after it - the exact evidence gap this line was added to close. The per-visit
                // bool above already makes it edge-triggered, which is what the WO asked for.
                FlowTrace.Step("DungeonOil",
                    $"final-warning ENTERED: t={Time.timeSinceLevelLoad:F0}s, oil={_oil:F0}/{_maxOil:F0}, " +
                    $"~{EstimatedSecondsRemaining:F0}s left, caches spent={_spentOilStones.Count}/{total}, " +
                    $"window={_finalWarningSeconds:F0}s. Range collapses to {_emptySafetyRange:0.00}u and a linear " +
                    "fog wall closes to 0.45..3.2m over this window - this is the 'suddenly dark' beat.");
                FinalWarningEntered?.Invoke();
            }

            if (IsInDarkness)
            {
                if (!_darknessReported)
                {
                    _darknessReported = true;
                    _darknessEnteredAtRealtime = Time.timeSinceLevelLoad;
                    // Step + the per-visit bool, for the same reason as the edge above.
                    FlowTrace.Step("DungeonOil",
                        $"darkness latch ARMED: t={Time.timeSinceLevelLoad:F0}s, oil fraction={OilFraction:0.00} " +
                        "(<= 0.12). ComposedAmbushDirector's darkness rate multiplier is live from here; " +
                        "the elapsed-dark total is printed when this lantern is torn down.");
                }
                _darkSecondsAccrued += Time.deltaTime;
            }
        }

        /// <summary>
        /// Pulls the dungeon fog wall inward with the dying flame. At zero oil only the
        /// immediate safety halo remains readable; distant room torches cannot reveal the route
        /// through the fog. Refilling restores the scene-authored fog exactly.
        /// </summary>
        private void ApplyDarknessVisibility()
        {
            if (!IsFinalWarning)
            {
                RestoreDungeonFog();
                return;
            }

            float collapse = Mathf.SmoothStep(0f, 1f, FinalWarningProgress);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = Mathf.Lerp(_fogStartBeforeWarning, 0.45f, collapse);
            RenderSettings.fogEndDistance = Mathf.Lerp(_fogEndBeforeWarning, 3.2f, collapse);

            // WO-1602: this is a PER-FRAME RenderSettings write, so it gets a Throttle and
            // never a plain Step (CLAUDE.md §12; memory logcat-ring-buffer-destroys-evidence —
            // a per-frame atmosphere line evicts the boot window out of the 256 KiB Android
            // ring and destroys the evidence it was added to collect).
            //
            // WHY A DUNGEON LANTERN IS ON THE TOWN'S FOG TIMELINE AT ALL: this writer FLIPS
            // fogMode to Linear and re-homes fogStart/fogEnd to the values it captured in its
            // OWN Awake. RenderSettings is global to the active scene, so if a Lantern is ever
            // alive while the town is active, the town's exponential haze silently becomes a
            // 0.45m..3.2m linear wall — which is the "dense pale haze, walls washed out" shape
            // the owner photographed. Whether that ever happens is not readable from source;
            // one throttled line naming the scene answers it outright.
            FlowTrace.Throttle("Atmos", "lantern-darkness-fog", 5f,
                $"Lantern WROTE fog (final-warning darkness, per-frame): scene='" +
                $"{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' mode=Linear " +
                $"start={RenderSettings.fogStartDistance:0.00} end={RenderSettings.fogEndDistance:0.00} " +
                $"collapse={collapse:0.00}. A scene name here that is NOT a dungeon is the bug.");

            _darknessGradeApplied = true;
        }

        private void RestoreDungeonFog()
        {
            if (!_darknessGradeApplied) return;
            RenderSettings.fog = _fogWasEnabled;
            RenderSettings.fogMode = _fogModeBeforeWarning;
            RenderSettings.fogStartDistance = _fogStartBeforeWarning;
            RenderSettings.fogEndDistance = _fogEndBeforeWarning;
            _darknessGradeApplied = false;

            // EDGE-TRIGGERED, not per-frame: the guard above means this body runs only on the
            // transition back. It restores the values captured in Awake, which is only correct
            // if Awake ran in the scene this restore lands in — so both are printed.
            FlowTrace.Step("Atmos",
                $"Lantern WROTE fog (restore): scene='" +
                $"{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' on={_fogWasEnabled} " +
                $"mode={_fogModeBeforeWarning} start={_fogStartBeforeWarning:0.00} end={_fogEndBeforeWarning:0.00} " +
                "(values captured in this Lantern's Awake).");
        }

        /// <summary>Keeps the lantern light at the Keeper's chest height.</summary>
        private void FollowHero()
        {
            if (_hero == null) return;
            Vector3 p = _hero.position;
            p.y += _lanternHeight;
            transform.position = p;
        }

        /// <summary>Drains oil over time; the run's lantern slowly burns down.</summary>
        private void DrainOil(float dt)
        {
            _oil = Mathf.Max(0f, _oil - (_oilDrainPerSec / Mathf.Max(1f, _expeditionOilMultiplier)) * dt);

            // WO-1805 section 7: the flask hitting zero used to clamp silently. A run that went
            // fully dark left NO line anywhere, which is why the ticket could not prove one had
            // ever happened. Once, on the edge, naming what recourse is left - because "empty with
            // two unspent caches still in the level" and "empty with nothing left" are different
            // bugs and the difference is invisible without this.
            if (_oil > 0f || _flaskEmptyReported) return;
            _flaskEmptyReported = true;
            int total = _oilStones != null ? _oilStones.Count : 0;
            int unspent = Mathf.Max(0, total - _spentOilStones.Count);
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            // Step + the per-visit _flaskEmptyReported bool: Once's key is session-scoped, so the
            // SECOND dungeon of a session would reach empty in silence (FlowTrace.cs:228-237).
            FlowTrace.Step("DungeonOil",
                $"flask EMPTY at t={Time.timeSinceLevelLoad:F0}s in scene='{sceneName}' - " +
                $"remaining recourse: unspent oil stones={unspent}/{total}, expedition blessing x{_expeditionOilMultiplier:0}. " +
                "From here the light is the 1.35u safety halo behind a 3.2m fog wall and the ambush " +
                "multiplier is armed.");
        }

        /// <summary>Consumes one bounded Lantern Blessing for this dungeon visit.
        /// Triple is preferred when owned; otherwise double. It stretches burn-time
        /// only and never changes combat power, loot, or darkness thresholds.</summary>
        private void ApplyExpeditionBlessing()
        {
            if (_expeditionBlessingApplied) return;
            _expeditionBlessingApplied = true;
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            var inventory = state != null ? state.GearInventory : null;
            if (inventory == null) return;

            const string tripleKey = "convenience:lantern-oil-3x-expedition";
            const string doubleKey = "convenience:lantern-oil-2x-expedition";
            bool consumed = false;
            if (inventory.TryGetValue(tripleKey, out int triple) && triple > 0)
            {
                inventory[tripleKey] = triple - 1;
                _expeditionOilMultiplier = 3f;
                consumed = true;
            }
            else if (inventory.TryGetValue(doubleKey, out int twice) && twice > 0)
            {
                inventory[doubleKey] = twice - 1;
                _expeditionOilMultiplier = 2f;
                consumed = true;
            }

            if (!consumed) return;
            svc.Save();
            FlowTrace.Step("Dungeon", $"Lantern Blessing applied: {_expeditionOilMultiplier:0}x oil duration for this expedition; one charge consumed.");
        }

        /// <summary>
        /// Refills the flask when the Keeper stands within an oil stone's radius
        /// (design §4 — oil stones are the lantern's refill points).
        /// </summary>
        private void CheckOilStones()
        {
            if (_hero == null || _oilStones == null) return;
            Vector3 heroPos = _hero.position;
            // Cottage oil stones are planar (Y=0 layout). Composed multi-level uses full 3D
            // so a stone on floor -6 does not refill the hero on floor 0 (WO-1001 slice 5).
            bool planar = !_standaloneRun;
            if (planar) heroPos.y = 0f;

            foreach (var stone in _oilStones)
            {
                if (stone == null) continue;
                string stoneId = string.IsNullOrEmpty(stone.id) ? stone.GetHashCode().ToString() : stone.id;
                if (_spentOilStones.Contains(stoneId)) continue;
                Vector3 stonePos = stone.position.ToWorld();
                if (planar) stonePos.y = 0f;
                float r = stone.radius;
                if ((heroPos - stonePos).sqrMagnitude <= r * r)
                {
                    // A cache is a route-planning resource, not an infinite fountain. Consume it
                    // only when it can actually add oil so arriving full never wastes the cache.
                    if (_oil < _maxOil - 0.01f)
                    {
                        float before = _oil;

                        // WO-1805 Lane C: how much of the flask a cache returns is a rail row that
                        // ships at 100 - a top-up, exactly as this line has always behaved. The Min
                        // makes 100 arithmetically identical to the old `_oil = _maxOil` from ANY
                        // starting level, so an empty client_tunables table changes nothing.
                        int refillPct = 100;
                        Guard.Try("DungeonOil", "resolve oil-stone refill pct", () =>
                        {
                            refillPct = Mathf.Clamp(
                                RemoteTunables.Int(RemoteTunables.KeyDungeonLanternOilStoneRefillPct), 1, 100);
                        });
                        _oil = Mathf.Min(_maxOil, _oil + _maxOil * (refillPct / 100f));
                        _spentOilStones.Add(stoneId);

                        // ⛔ THE LINE THIS PATH NEVER HAD (WO-1805 section 7). It mutated the flask
                        // and returned silently, so no capture could say whether the player had
                        // ever found a cache - the single biggest evidence gap in the ticket.
                        float addedSeconds = _oilDrainPerSec > 0f ? (_oil - before) / _oilDrainPerSec : 0f;
                        int stoneTotal = _oilStones != null ? _oilStones.Count : 0;
                        FlowTrace.Step("DungeonOil",
                            $"oil stone '{stoneId}' SPENT: oil {before:F0}->{_oil:F0} of {_maxOil:F0} " +
                            $"(+{addedSeconds:F0}s at {_oilDrainPerSec:F2}/s, refill={refillPct}%), " +
                            $"{_spentOilStones.Count}/{stoneTotal} caches used in this visit.");

                        // The teach's completion beat. Raised AFTER the mutation and the trace so a
                        // listener that reads the flask sees the refilled value.
                        OilStoneUsed?.Invoke();
                    }
                    return;
                }
            }
        }

        /// <summary>Begins (or refreshes) a Tincture light-shrink — port of triggerTincture.</summary>
        public void TriggerTincture()
        {
            // A fresh cast always re-arms the full window rather than stacking.
            _tinctureRemaining = _tinctureDurationSec;
        }

        /// <summary>Clears the Tincture channel — called on run teardown.</summary>
        public void ClearTincture()
        {
            _tinctureRemaining = 0f;
        }

        private void TickTincture(float dt)
        {
            if (_tinctureRemaining > 0f)
                _tinctureRemaining = Mathf.Max(0f, _tinctureRemaining - dt);
        }

        /// <summary>
        /// Eases the live light range toward its target. The target is the full
        /// reach scaled by the oil level (low oil pulls the dark in) and, while
        /// a Tincture is active, halved on top of that.
        /// </summary>
        private void ApplyRange(float dt)
        {
            // Oil scales reach between minOilLightFraction (empty) and 1 (full).
            float oilScale = Mathf.Lerp(_minOilLightFraction, 1f, OilFraction);
            float target = FullRange * oilScale;
            if (IsFinalWarning)
            {
                // The last thirty seconds are a distinct physical warning: the navigable pool
                // collapses into a tight footing halo. Two incommensurate waves create an
                // organic wick flutter without random state or frame-rate dependence.
                float collapse = Mathf.SmoothStep(0f, 1f, FinalWarningProgress);
                target = Mathf.Lerp(target, _emptySafetyRange, collapse);
                if (!_reducedMotion)
                {
                    float flutter = 0.91f + 0.06f * Mathf.Sin(Time.time * 13.7f)
                                           + 0.03f * Mathf.Sin(Time.time * 31.1f);
                    target *= Mathf.Clamp(flutter, 0.82f, 1f);
                }
            }
            if (IsTinctureActive) target *= _tinctureRangeMul;

            if (_reducedMotion)
            {
                _liveRange = target;
            }
            else
            {
                float k = 1f - Mathf.Pow(0.0001f,
                    Mathf.Min(dt, 0.05f) * (_rangeLerpPerSec / 9f));
                _liveRange += (target - _liveRange) * k;
            }
            _light.range = _liveRange;
        }

        /// <summary>
        /// Drives the point-light intensity — the slow flame breathing scaled by
        /// oil (low oil dims the mean and quickens the breath). Pinned flat when
        /// reduced motion is set.
        /// </summary>
        private void ApplyIntensity()
        {
            // Oil scales the mean intensity the same way it scales reach.
            float oilScale = Mathf.Lerp(_minOilLightFraction, 1f, OilFraction);
            float mean = _baseIntensity * oilScale;
            if (IsFinalWarning)
            {
                float collapse = Mathf.SmoothStep(0f, 1f, FinalWarningProgress);
                mean = Mathf.Lerp(mean, _emptySafetyIntensity, collapse);
            }

            if (_reducedMotion)
            {
                _light.intensity = mean;
                return;
            }

            float hz = IsLowOil ? _breathHz * _lowOilBreathMult : _breathHz;
            float amplitude = IsFinalWarning
                ? Mathf.Lerp(_breathAmplitude, 0.42f, FinalWarningProgress)
                : _breathAmplitude;
            float breath = Mathf.Sin(Time.time * hz * 2f * Mathf.PI) * amplitude;
            if (IsFinalWarning)
                breath += Mathf.Sin(Time.time * 23.3f) * 0.1f * FinalWarningProgress;
            _light.intensity = mean * (1f + breath);
        }

        /// <summary>Loops the lantern-flicker SFX while oil is low; stops it otherwise.</summary>
        private void DriveFlickerAudio()
        {
            if (_flickerAudio == null) return;
            if (IsLowOil && !_flickerAudio.isPlaying)
            {
                _flickerAudio.loop = true;
                _flickerAudio.Play();
            }
            else if (!IsLowOil && _flickerAudio.isPlaying)
            {
                _flickerAudio.Stop();
            }
        }
    }
}
