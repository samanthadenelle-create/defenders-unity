// =============================================================================
// AmbientNPC — an ambient townsperson of Elarion village (Workstream D).
// -----------------------------------------------------------------------------
// One ambient villager: a KayKit civilian model that either WANDERS the village
// on the baked NavMesh or stands IDLE at an authored spot, and shows an
// engage-on-approach word bubble when the Keeper draws near.
//
// This is the village twin of the dungeon's Bryn — the proximity / hysteresis /
// line-pick pattern is lifted from Bryn.cs, re-homed in DeNelle.Village so it
// carries no DeNelle.Dungeons dependency (module isolation). The speech bubble
// itself is TownsfolkBubble (this module's twin of WandererBubble).
//
// ── Behaviour ──
//   • Wanderers pick a random NavMesh point inside a roam radius of their home
//     anchor, walk there via a NavMeshAgent, pause, then pick another. If no
//     NavMesh is present the agent is simply disabled and the villager stands
//     idle — it never errors.
//   • Idlers stay put and gently sway / turn, like Bryn.
//   • When the Keeper is within speakRadius a TownsfolkBubble fades in with the
//     next line from this villager's TownsfolkDialogue pool; it hides again past
//     a hysteresis margin so an edge-loitering Keeper does not flicker it.
//   • While speaking, a wanderer halts and faces the Keeper — it "engages."
//
// The scene builder (VillageSceneBuilder) adds this by reflection, sets the
// serialized fields through SerializedObject, and calls Configure(...). The
// Keeper transform is handed in via SetHero — the NPC never reaches into the
// scene for it.
//
// Legacy Input Manager only — this component takes no input at all; it just
// watches a transform. (No UnityEngine.InputSystem anywhere.)
// =============================================================================

using UnityEngine;
using UnityEngine.AI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// An ambient village townsperson — wanders or idles, and speaks a word
    /// bubble when the Keeper approaches. Built into the Village scene by
    /// <c>VillageSceneBuilder</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientNPC : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which dialogue archetype this villager speaks as (drives the " +
                 "line pool + the bubble's display name).")]
        [SerializeField] private TownsfolkDialogue.Archetype _archetype =
            TownsfolkDialogue.Archetype.Villager;

        [Header("Movement")]
        [Tooltip("When true the villager roams the NavMesh; when false it stands " +
                 "idle at its home anchor.")]
        [SerializeField] private bool _wander = true;

        [Tooltip("Roam radius (world units) around the home anchor a wanderer " +
                 "picks its destinations within.")]
        [SerializeField] private float _roamRadius = 7f;

        [Tooltip("Walk speed of a wandering villager (world units / second).")]
        [SerializeField] private float _walkSpeed = 1.6f;

        [Tooltip("Seconds a wanderer pauses on reaching a destination before " +
                 "choosing the next one.")]
        [SerializeField] private float _pauseSeconds = 2.5f;

        [Header("Proximity speech")]
        [Tooltip("World-unit distance within which the villager speaks to the Keeper.")]
        [SerializeField] private float _speakRadius = 5.5f;

        [Tooltip("Hysteresis margin added to the speak radius for the fade-OUT " +
                 "threshold — stops the bubble flickering at the edge.")]
        [SerializeField] private float _speakHysteresis = 1.5f;

        [Header("Idle motion")]
        [Tooltip("Idle Y-sway frequency (Hz) — gentle, lifelike.")]
        [SerializeField] private float _swayHz = 0.45f;

        [Tooltip("Idle Y-sway amplitude (world units).")]
        [SerializeField] private float _swayAmplitude = 0.04f;

        [Tooltip("When true the idle sway is disabled (reduced motion).")]
        [SerializeField] private bool _reducedMotion;

        [Header("Wiring")]
        [Tooltip("The world-space speech bubble. Assigned by the scene builder.")]
        [SerializeField] private TownsfolkBubble _bubble;

        // ── Runtime ──────────────────────────────────────────────────────────

        private NavMeshAgent _agent;
        private Transform _hero;
        [SerializeField] private Vector3 _homeAnchor;   // builder-set; serialized so it survives the scene save
        private float _baseY;
        private float _pauseTimer;
        private bool _hasNavMesh;
        private int _lineCursor;
        private float _seedPhase;   // per-NPC sway phase so a crowd does not pulse in sync

        /// <summary>True while the Keeper is close enough that this villager is speaking.</summary>
        public bool Speaking { get; private set; }

        // ── Combat shelter (owner feature 2026-07-06: villagers hide during battle) ──
        // Only WANDERING villagers flee — vendor / introducer / static bodies are
        // configured wander=false by their injectors and must never leave their post.
        private enum ShelterState
        {
            /// <summary>Normal ambient behaviour (wander / idle / speak).</summary>
            None = 0,
            /// <summary>Combat went active; waiting a short random stagger before running.</summary>
            FleeStagger,
            /// <summary>Hurrying to the nearest building "door" on the NavMesh.</summary>
            Fleeing,
            /// <summary>Out of sight inside the shelter (renderers off).</summary>
            Hidden,
            /// <summary>Combat cleared; waiting the calm delay before stepping back out.</summary>
            ReturnDelay,
            /// <summary>Walking back to the home anchor to resume ambient life.</summary>
            Returning,
        }

        private ShelterState _shelter = ShelterState.None;
        private float _shelterTimer;
        private Vector3 _shelterPoint;
        private string _shelterName;
        private Renderer[] _bodyRenderers;   // cached once; toggled for hide/unhide

        /// <summary>Hurry multiplier applied to the walk speed while fleeing — reads as urgency.</summary>
        private const float FleeSpeedMultiplier = 2.1f;

        // Combat-active authority — the SAME inputs the HUD context uses (owner ruling
        // 2026-07-06): wave Phase Countdown||Active OR BattleLock.IsInBattle().
        // Polled once per interval and shared across every villager (cheap).
        private static float s_combatNextPoll;
        private static bool s_combatActive;

        // Shared shelter-anchor cache (the scene's Building collection, refreshed lazily).
        private static Building[] s_shelterBuildings;
        private static float s_sheltersNextRefresh;

        // Observability counters for the [Flow:Townsfolk] transition lines.
        private static int s_fleeingCount;
        private static int s_hiddenCount;

        // Presence census (fleet run 9413: zero [Flow:Townsfolk] lines was ambiguous
        // between "no NPCs in the scene" and "poll early-outs"). Every live AmbientNPC
        // counts itself; wander-eligible = _wander && live on-mesh agent (the flee gate).
        // One Step fires on each combat-activation naming both counts.
        private static int s_instanceCount;
        private static int s_wanderEligibleCount;
        private bool _shelterEligible;      // whether THIS npc is in the eligible tally
        private static bool s_lastCombatActive;

        // ── Animator (DEF-91: purchased NPC model pack — walk / idle / talk clips) ─
        private Animator _animator;
        private static readonly int SpeedHash   = Animator.StringToHash("Speed");
        private static readonly int TalkingHash = Animator.StringToHash("IsTalking");
        // WO-163: cached once at init — whether THIS NPC's controller actually
        // declares the param. Driving an absent param logs an error EVERY frame
        // (3,351-error spam). Guard the SetFloat/SetBool with these.
        private bool _hasSpeedParam;
        private bool _hasTalkingParam;

        // ── Configuration ────────────────────────────────────────────────────

        /// <summary>
        /// Configures the villager from the scene builder. <paramref name="homeAnchor"/>
        /// is the world point a wanderer roams around (and an idler stands at);
        /// it defaults to the current transform position when not set explicitly.
        /// Called after the component is added + its fields are wired.
        /// </summary>
        public void Configure(TownsfolkDialogue.Archetype archetype, bool wander,
                              Vector3 homeAnchor)
        {
            _archetype = archetype;
            _wander = wander;
            _homeAnchor = homeAnchor;
        }

        /// <summary>
        /// Sets the Keeper transform this villager watches for the proximity
        /// check. The scene builder / a runtime caller assigns this — the NPC
        /// never searches the scene itself.
        /// </summary>
        public void SetHero(Transform hero)
        {
            _hero = hero;
        }

        /// <summary>Assigns the speech bubble (the scene builder uses this when wiring by reflection).</summary>
        public void SetBubble(TownsfolkBubble bubble)
        {
            _bubble = bubble;
        }

        /// <summary>Sets the reduced-motion preference — disables the idle sway.</summary>
        public void SetReducedMotion(bool reduced)
        {
            _reducedMotion = reduced;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Start()
        {
            if (_homeAnchor == Vector3.zero) _homeAnchor = transform.position;
            _baseY = transform.position.y;
            _seedPhase = Random.value * Mathf.PI * 2f;
            _lineCursor = Random.Range(0, 64);   // varied opening line per villager

            // WO-29: when this villager's KayKit civilian model is absent (the
            // Models packs are gitignored, so a clone / this machine may not have
            // them) the builder leaves a default-white placeholder primitive — the
            // "white pill" the owner saw. Tint any such untinted body by archetype
            // at runtime so a missing model still reads as a person, never a blank
            // capsule. Skips real textured meshes (only recolours the white default).
            EnsureBodyTinted();

            // Resolve the Keeper if the builder did not hand one over — a tagged
            // "Player" GameObject, else the Hero rig the village builder names.
            if (_hero == null) _hero = ResolveHeroFallback();

            _agent = GetComponent<NavMeshAgent>();
            _hasNavMesh = _wander && _agent != null && _agent.isOnNavMesh;

            if (_agent != null)
            {
                if (_hasNavMesh)
                {
                    _agent.speed = _walkSpeed;
                    _agent.angularSpeed = 240f;
                    _agent.acceleration = 12f;
                    _agent.stoppingDistance = 0.2f;
                    PickNewDestination();
                }
                else
                {
                    // No NavMesh (or an idle villager) — disable the agent so it
                    // never warns or drifts; the villager stands its ground.
                    _agent.enabled = false;
                }
            }

            _bubble?.Hide();

            // Cache the body renderers once for the combat-shelter hide/unhide.
            _bodyRenderers = GetComponentsInChildren<Renderer>(true);

            // Presence census: count this NPC (and whether it can flee) so the
            // combat-active Step can name "N ambient NPCs (M wander-eligible)".
            s_instanceCount++;
            _shelterEligible = _wander && _hasNavMesh && _agent != null && _agent.enabled;
            if (_shelterEligible) s_wanderEligibleCount++;

            // Grab the Animator from the mesh child (if the NPC model pack prefab is present).
            // Null-safe: locomotion and speech still work without it.
            _animator = GetComponentInChildren<Animator>();

            // WO-163: cache which params the controller actually has, so UpdateAnimator
            // never drives an absent param (was spamming 3,351 errors/run). A controller
            // with no runtimeAnimatorController has no parameters → both stay false.
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.nameHash == SpeedHash)   _hasSpeedParam   = true;
                    if (p.nameHash == TalkingHash) _hasTalkingParam = true;
                }
            }
        }

        private void Update()
        {
            // WO-1483: town frame path, PER-INSTANCE — the townsfolk injector spawns many of
            // these and they tick in an EMPTY town (no enemies needed), which is exactly
            // WO-1483's case. Tight per-instance budget; the roll-up sums them.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "AmbientNPC.Update", 1f, 1f);

            UpdateShelter();
            if (_shelter != ShelterState.None)
            {
                // Sheltering owns the villager: no proximity speech, no roaming,
                // no idle sway — just keep the walk animation honest.
                UpdateAnimator();
                return;
            }

            UpdateProximity();
            UpdateRoaming();
            UpdateIdleMotion();
            UpdateAnimator();
        }

        // ── Combat shelter (flee into a house during battle, return after) ───

        // Mirror HudContextEvaluator.ImminentThreshold / HeroLocomotion.CombatImminentThreshold
        // (owner 2026-07-08/10): a long between-wave Countdown reads as TOWN, so ambient NPCs stay
        // VISIBLE; only the imminent window (or an Active wave / staged battle) hides them. Without
        // this, a 275s idle-hub countdown counted as combat and left every NPC permanently hidden
        // while the HUD + hero read Town — F8 "not in battle, where are NPCs" (proven: Player.log
        // 'vendors hidden (wave): 10' + 'injected 5 villagers' during 'Countdown ... cd269.8').
        private const float CombatImminentThreshold = 5f;

        /// <summary>
        /// Shared combat-active check — the same authority the HUD context uses
        /// (owner ruling 2026-07-06): wave <see cref="WavePhase.Countdown"/> or
        /// <see cref="WavePhase.Active"/>, OR any staged battle via
        /// <c>BattleLock.IsInBattle()</c>. Polled at most every 0.25s, shared by
        /// every villager in the scene.
        /// </summary>
        private static bool CombatActive()
        {
            if (Time.unscaledTime >= s_combatNextPoll)
            {
                s_combatNextPoll = Time.unscaledTime + 0.25f;
                s_combatActive = Guard.Try("Townsfolk", "combat-poll", () =>
                {
                    var wm = WaveManager.Instance;
                    // WO-1736: !IsAwaitingPlayerStart — an endless wave parked on the player's
                    // DEFEND press holds its countdown at 0, so a bare `<= 5f` kept the whole
                    // town's NPCs in combat behaviour through an open-ended peaceful build
                    // phase. Captured window and reasoning: HudContextEvaluator.IsWaveActive.
                    bool wave = wm != null &&
                                (wm.Phase == WavePhase.Active ||
                                 (wm.Phase == WavePhase.Countdown &&
                                  !wm.IsAwaitingPlayerStart &&
                                  wm.CountdownRemaining <= CombatImminentThreshold));
                    return wave || DeNelle.Core.Combat.BattleLock.IsInBattle();
                }, false);

                // Presence census on each combat activation (fleet run 9413: a silent
                // run could not distinguish "no NPCs" from "poll early-out" — this line
                // names it: eligible=0 means nothing in this scene CAN flee).
                if (s_combatActive && !s_lastCombatActive)
                    FlowTrace.Step("Townsfolk",
                        $"combat-active: {s_instanceCount} ambient NPCs " +
                        $"({s_wanderEligibleCount} wander-eligible)");
                s_lastCombatActive = s_combatActive;
            }
            return s_combatActive;
        }

        /// <summary>
        /// Public read of the shared combat-active authority (ticket F8-14): the SAME
        /// wave/battle signal the townsfolk flee to shelter on (wave Countdown/Active
        /// OR <c>BattleLock.IsInBattle()</c>, shared 0.25s poll). Reused by the castle
        /// vendor hider + the wave shop-gate so nothing invents a second combat poll.
        /// </summary>
        public static bool IsCombatActive => CombatActive();

        /// <summary>
        /// Steps the flee/hide/return state machine. Only WANDERING villagers with a
        /// live NavMeshAgent participate — static bodies (vendors, the companion
        /// introducer, idlers) are configured wander=false and never leave their post.
        /// </summary>
        private void UpdateShelter()
        {
            bool combat = CombatActive();

            switch (_shelter)
            {
                case ShelterState.None:
                    if (combat && _wander && _hasNavMesh && _agent != null && _agent.enabled)
                    {
                        // Stop talking mid-panic; stagger the run so a crowd
                        // scatters organically instead of bolting in sync.
                        if (Speaking) { Speaking = false; _bubble?.Hide(); }
                        _shelterTimer = Random.Range(0f, 1.5f);
                        _shelter = ShelterState.FleeStagger;
                    }
                    break;

                case ShelterState.FleeStagger:
                    if (!combat) { _shelter = ShelterState.None; break; }
                    _shelterTimer -= Time.deltaTime;
                    if (_shelterTimer <= 0f) BeginFlee();
                    break;

                case ShelterState.Fleeing:
                    if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
                    {
                        HideBody();   // agent died mid-run — just duck out of sight
                        break;
                    }
                    if (!_agent.pathPending &&
                        _agent.remainingDistance <= _agent.stoppingDistance + 0.35f)
                        HideBody();   // reached the door — slip inside
                    break;

                case ShelterState.Hidden:
                    if (!combat)
                    {
                        _shelterTimer = Random.Range(3f, 5f);   // calm delay
                        _shelter = ShelterState.ReturnDelay;
                    }
                    break;

                case ShelterState.ReturnDelay:
                    if (combat) { _shelter = ShelterState.Hidden; break; }
                    _shelterTimer -= Time.deltaTime;
                    if (_shelterTimer <= 0f) BeginReturn();
                    break;

                case ShelterState.Returning:
                    if (combat)
                    {
                        // Battle re-ignited mid-walk — drop to None so the next
                        // frame re-triggers a fresh (staggered) flee from here.
                        _shelter = ShelterState.None;
                        break;
                    }
                    if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh ||
                        (!_agent.pathPending &&
                         _agent.remainingDistance <= _agent.stoppingDistance + 0.35f))
                        ResumeAmbient();
                    break;
            }
        }

        /// <summary>
        /// Picks the nearest building anchor and hurries there. Falls back to the
        /// home anchor when the scene has no buildings (they still duck home).
        /// </summary>
        private void BeginFlee()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            {
                // Can't run anywhere — just hide in place once combat is on.
                HideBody();
                return;
            }

            if (!TryPickShelter(out _shelterPoint, out _shelterName))
            {
                _shelterPoint = _homeAnchor;
                _shelterName = "home anchor";
                FlowTrace.Once("Townsfolk", "no-shelter-buildings",
                    "no Building anchors found — villagers shelter at their home anchors");
            }

            _agent.speed = _walkSpeed * FleeSpeedMultiplier;
            _agent.isStopped = false;
            _agent.SetDestination(_shelterPoint);
            _shelter = ShelterState.Fleeing;
            s_fleeingCount++;
            FlowTrace.Step("Townsfolk",
                $"flee: {_archetype} runs to '{_shelterName}' " +
                $"({_shelterPoint.x:F1},{_shelterPoint.z:F1}) — fleeing={s_fleeingCount} hidden={s_hiddenCount}");
        }

        /// <summary>
        /// Finds the nearest scene <see cref="Building"/> to this villager and
        /// NavMesh-samples a reachable point beside it (its "door" — building
        /// blockers carve the mesh, so the sample lands at the wall's edge).
        /// The Building collection is cached statically and refreshed every 10s
        /// (player-placed buildings appear between refreshes at worst).
        /// </summary>
        private bool TryPickShelter(out Vector3 point, out string name)
        {
            point = default;
            name = null;

            if (s_shelterBuildings == null || Time.unscaledTime >= s_sheltersNextRefresh)
            {
                s_sheltersNextRefresh = Time.unscaledTime + 10f;
                s_shelterBuildings = Guard.Try("Townsfolk", "shelter-scan",
                    () => FindObjectsByType<Building>(FindObjectsSortMode.None),
                    fallback: null) ?? new Building[0];
            }

            Building best = null;
            float bestSqr = float.MaxValue;
            Vector3 here = transform.position;
            foreach (var b in s_shelterBuildings)
            {
                if (b == null) continue;
                Vector3 d = b.transform.position - here;
                d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = b; }
            }
            if (best == null) return false;

            if (!NavMesh.SamplePosition(best.transform.position, out NavMeshHit hit, 6f,
                                        NavMesh.AllAreas))
                return false;

            point = hit.position;
            name = string.IsNullOrEmpty(best.BuildingId) ? best.Type.ToString() : best.BuildingId;
            return true;
        }

        /// <summary>Slips the villager out of sight at the shelter (renderers off, agent halted).
        /// The GameObject stays active so this component keeps watching for the all-clear.</summary>
        private void HideBody()
        {
            SetBodyVisible(false);
            if (Speaking) { Speaking = false; }
            _bubble?.Hide();
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                _agent.isStopped = true;
            if (_shelter == ShelterState.Fleeing && s_fleeingCount > 0) s_fleeingCount--;
            _shelter = ShelterState.Hidden;
            s_hiddenCount++;
            FlowTrace.Step("Townsfolk",
                $"hidden: {_archetype} inside '{_shelterName ?? "shelter"}' — " +
                $"fleeing={s_fleeingCount} hidden={s_hiddenCount}");
        }

        /// <summary>Steps back out of the shelter and walks home at normal pace.</summary>
        private void BeginReturn()
        {
            SetBodyVisible(true);
            if (s_hiddenCount > 0) s_hiddenCount--;

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.speed = _walkSpeed;
                _agent.isStopped = false;
                Vector3 target = _homeAnchor;
                if (NavMesh.SamplePosition(_homeAnchor, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    target = hit.position;
                _agent.SetDestination(target);
                _shelter = ShelterState.Returning;
            }
            else
            {
                ResumeAmbient();   // no agent — just reappear and stand
                return;
            }

            FlowTrace.Step("Townsfolk",
                $"return: {_archetype} walks home from '{_shelterName ?? "shelter"}' — hidden={s_hiddenCount}");
        }

        /// <summary>Hands control back to the normal ambient loop (wander/idle/speak).</summary>
        private void ResumeAmbient()
        {
            SetBodyVisible(true);
            _shelter = ShelterState.None;
            if (_agent != null && _agent.enabled)
            {
                _agent.speed = _walkSpeed;
                if (_agent.isOnNavMesh) PickNewDestination();
            }
            FlowTrace.Step("Townsfolk", $"resumed: {_archetype} back to ambient life");
        }

        /// <summary>Toggles the cached body renderers (hide/unhide without deactivating —
        /// Update must keep running to notice the battle ending).</summary>
        private void SetBodyVisible(bool visible)
        {
            if (_bodyRenderers == null) return;
            foreach (var r in _bodyRenderers)
                if (r != null) r.enabled = visible;
        }

        /// <summary>Keeps the shared flee/hidden counters honest when a villager is
        /// disabled or destroyed mid-shelter (scene unload, injector rebuild).</summary>
        private void OnDisable()
        {
            if (_shelter == ShelterState.Fleeing && s_fleeingCount > 0) s_fleeingCount--;
            if ((_shelter == ShelterState.Hidden || _shelter == ShelterState.ReturnDelay) &&
                s_hiddenCount > 0) s_hiddenCount--;
            _shelter = ShelterState.None;
        }

        /// <summary>Un-counts this NPC from the presence census. In OnDestroy (not
        /// OnDisable) so enable/disable cycles never double-count — Start() counts once,
        /// this decrements once.</summary>
        private void OnDestroy()
        {
            if (_bodyRenderers != null)   // proxy for "Start ran and counted us"
            {
                if (s_instanceCount > 0) s_instanceCount--;
                if (_shelterEligible && s_wanderEligibleCount > 0) s_wanderEligibleCount--;
                _shelterEligible = false;
            }
        }

        // ── Proximity speech ─────────────────────────────────────────────────

        /// <summary>
        /// Drives the speaking state from the Keeper's distance with asymmetric
        /// thresholds (enter at <see cref="_speakRadius"/>, leave at the wider
        /// hysteresis radius). On a fresh entry into range the bubble shows the
        /// next line from this villager's pool.
        /// </summary>
        private void UpdateProximity()
        {
            if (_hero == null)
            {
                if (Speaking) { Speaking = false; _bubble?.Hide(); }
                return;
            }

            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = _hero.position;     b.y = 0f;
            float dist = (a - b).magnitude;

            if (!Speaking && dist <= _speakRadius)
            {
                Speaking = true;
                string name = TownsfolkDialogue.NameFor(_archetype);
                string line = PickSpokenLine();
                _lineCursor++;   // next approach steps to the next line
                _bubble?.Show(name, line);
            }
            else if (Speaking && dist > _speakRadius + _speakHysteresis)
            {
                Speaking = false;
                _bubble?.Hide();
            }
        }

        /// <summary>
        /// Chooses the line this villager speaks on approach. Normally the next
        /// line from its archetype pool — BUT as the apex Black Dragon nears, the
        /// town drops ESCALATING foreshadow rumors so a new player learns to build
        /// the anti-air Sky Ballista before the dragon arrives (owner directive
        /// 2026-07-08). The tier is driven by how close the dragon wave is:
        ///   • NEAR / IMMINENT — urgent; always spoken while the dragon is close
        ///     (gated so they never fire from wave 1).
        ///   • FAR / MID — woven in on alternating approaches so the town still
        ///     reads lived-in rather than one-note.
        /// Falls back to normal ambient chatter when no wave loop is present.
        /// </summary>
        private string PickSpokenLine()
        {
            var wm = WaveManager.Instance;
            if (wm != null)
            {
                // F8 2026-08-31: a sparse subset of random townsfolk teach the two hard-to-find
                // tower paths using the UI's exact labels. Stop after the opening waves so this
                // remains onboarding help rather than permanent repetitive chatter.
                if (TownsfolkDialogue.ShouldOfferBuildHelp(wm.CurrentWaveId, _archetype, _lineCursor))
                    return TownsfolkDialogue.BuildHelpFor(_archetype, _lineCursor);

                // A dragon actually aloft forces the most urgent tier outright.
                bool apexLive = wm.LiveApexBoss != null && !wm.LiveApexBoss.IsDead;
                TownsfolkDialogue.DragonHintTier tier = apexLive
                    ? TownsfolkDialogue.DragonHintTier.Imminent
                    : TownsfolkDialogue.TierForWave(wm.CurrentWaveId);

                bool urgent = tier == TownsfolkDialogue.DragonHintTier.Near ||
                              tier == TownsfolkDialogue.DragonHintTier.Imminent;
                // Urgent tiers always warn; distant tiers alternate rumor / normal
                // chatter so early waves aren't a wall of dragon talk.
                if (urgent || (_lineCursor & 1) == 0)
                    return TownsfolkDialogue.DragonRumor(tier, _lineCursor);
            }
            return TownsfolkDialogue.LineFor(_archetype, _lineCursor);
        }

        // ── Roaming ──────────────────────────────────────────────────────────

        /// <summary>
        /// Steps a wandering villager: while speaking it halts and turns to face
        /// the Keeper (it "engages"); otherwise it walks to its current
        /// destination, pauses on arrival, then picks a new one.
        /// </summary>
        private void UpdateRoaming()
        {
            if (!_hasNavMesh || _agent == null || !_agent.enabled) return;

            // Engage: a speaking villager stops and faces the Keeper.
            if (Speaking)
            {
                _agent.isStopped = true;
                FaceHero();
                return;
            }
            _agent.isStopped = false;

            // Arrived at the destination — pause, then roam again.
            if (!_agent.pathPending &&
                _agent.remainingDistance <= _agent.stoppingDistance + 0.05f)
            {
                _pauseTimer -= Time.deltaTime;
                if (_pauseTimer <= 0f) PickNewDestination();
            }
        }

        /// <summary>
        /// Picks a fresh random NavMesh destination within the roam radius of the
        /// home anchor and sends the agent there, then arms the arrival pause.
        /// </summary>
        private void PickNewDestination()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 disc = Random.insideUnitCircle * _roamRadius;
                Vector3 candidate = _homeAnchor + new Vector3(disc.x, 0f, disc.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    _agent.SetDestination(hit.position);
                    break;
                }
            }
            _pauseTimer = _pauseSeconds + Random.Range(-0.5f, 1.5f);
        }

        /// <summary>Smoothly turns the villager to face the Keeper while engaged.</summary>
        private void FaceHero()
        {
            if (_hero == null) return;
            Vector3 dir = _hero.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation =
                Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 6f);
        }

        // ── Idle motion ──────────────────────────────────────────────────────

        /// <summary>
        /// Gives a non-wandering (or NavMesh-less) villager a gentle Y-sway so it
        /// reads as alive rather than frozen. A wanderer actually moving on the
        /// NavMesh is left to the agent; the sway only applies when stationary.
        /// </summary>
        private void UpdateIdleMotion()
        {
            // A villager with a live NavMeshAgent lets the agent own its
            // transform — writing position here (even while the agent is paused
            // between roam legs) fights the agent and jitters. Only true idlers
            // (no agent, or a disabled one) get the sway.
            if (_agent != null && _agent.enabled)
            {
                _baseY = transform.position.y;
                return;
            }
            if (_reducedMotion) return;

            float y = _baseY + Mathf.Sin(Time.time * _swayHz * 2f * Mathf.PI + _seedPhase)
                               * _swayAmplitude;
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, y, p.z);

            // An idle villager that is not roaming also faces the Keeper when
            // they are near, so engaging feels deliberate.
            if (Speaking && !(_hasNavMesh && _agent != null && _agent.enabled))
                FaceHero();
        }

        // ── Animator driver (DEF-91) ─────────────────────────────────────────

        /// <summary>
        /// Drives the NPC model pack Animator's Speed and IsTalking parameters
        /// from live agent velocity and the Speaking flag. No-ops gracefully when
        /// no Animator is present (placeholder primitives have none).
        /// </summary>
        private void UpdateAnimator()
        {
            if (_animator == null) return;

            // Speed: agent velocity magnitude when moving, 0 when idle/stopped.
            // WO-163: only drive params the controller actually declares.
            if (_hasSpeedParam)
            {
                float speed = 0f;
                if (_agent != null && _agent.enabled && !_agent.isStopped)
                    speed = _agent.velocity.magnitude;
                _animator.SetFloat(SpeedHash, speed, 0.08f, Time.deltaTime);
            }

            // IsTalking: mirrors the Speaking state so the talk clip plays while
            // the bubble is visible.
            if (_hasTalkingParam)
                _animator.SetBool(TalkingHash, Speaking);
        }

        // ── Body tint (WO-29 white-pill safety-net) ──────────────────────────

        /// <summary>
        /// Recolours any default-white / untextured placeholder body this villager
        /// carries (a primitive left when the KayKit model is missing) with an
        /// archetype tint, so it never renders as a blank white capsule. Real
        /// textured meshes are left untouched — only Unity's built-in
        /// "Default-Material" (or a null material) is replaced.
        /// </summary>
        private void EnsureBodyTinted()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Material tinted = null;   // built lazily, shared across any placeholder parts
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mat = r.sharedMaterial;
                bool isDefault = mat == null ||
                                 mat.name.StartsWith("Default-Material") ||
                                 mat.name.StartsWith("Lit");   // bare URP/Lit instance
                if (!isDefault) continue;

                if (tinted == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (shader == null) return;
                    tinted = new Material(shader) { name = "AmbientNPC_" + _archetype };
                    Color c = ArchetypeTint(_archetype);
                    if (tinted.HasProperty("_BaseColor")) tinted.SetColor("_BaseColor", c);
                    if (tinted.HasProperty("_Color")) tinted.SetColor("_Color", c);
                }
                r.sharedMaterial = tinted;
            }
        }

        /// <summary>Warm, distinguishable tint per townsfolk archetype (WO-29).</summary>
        private static Color ArchetypeTint(TownsfolkDialogue.Archetype a)
        {
            switch (a)
            {
                case TownsfolkDialogue.Archetype.Trader:        return Hex("c2925a"); // amber
                case TownsfolkDialogue.Archetype.Guard:         return Hex("8a6b5a"); // earthy brown
                case TownsfolkDialogue.Archetype.Child:         return Hex("7fa8c9"); // soft blue
                case TownsfolkDialogue.Archetype.Elder:         return Hex("a09890"); // grey-white
                // WO-116 wardens — each reads at a glance even as a placeholder body.
                case TownsfolkDialogue.Archetype.Blacksmith:    return Hex("5a5048"); // soot/iron grey
                case TownsfolkDialogue.Archetype.Quartermaster: return Hex("9c7b3f"); // ledger-brown ochre
                case TownsfolkDialogue.Archetype.Archmage:      return Hex("8a6fb0"); // ward-violet
                case TownsfolkDialogue.Archetype.Farmer:        return Hex("8a9a52"); // field green
                default:                                        return Hex("c2a882"); // Villager warm tan
            }
        }

        private static Color Hex(string rrggbb)
        {
            return ColorUtility.TryParseHtmlString("#" + rrggbb, out var c) ? c : Color.white;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Last-resort Keeper lookup when the builder / controller did not call
        /// SetHero — a GameObject whose name starts with "Hero" (the village
        /// builder names the hero rig "Hero (Blaise)"). Returns null when none
        /// exists; the villager simply stays silent until a hero is assigned.
        ///
        /// <para>Deliberately name-based, not tag-based: the project's
        /// TagManager defines no "Player" tag, and FindGameObjectWithTag throws
        /// on an undefined tag.</para>
        /// </summary>
        private Transform ResolveHeroFallback()
        {
            foreach (var t in
                     UnityEngine.Object.FindObjectsByType<Transform>())
            {
                if (t != null && t.name.StartsWith("Hero")) return t;
            }
            return null;
        }
    }
}
