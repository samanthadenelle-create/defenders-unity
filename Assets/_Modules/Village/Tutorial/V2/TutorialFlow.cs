// =============================================================================
// TutorialFlow — the Tutorial V2 thin interpreter (WO-T1, spec §2.1c).
// -----------------------------------------------------------------------------
// Walks the tutorial-steps.json registry (TutorialStepCatalog): for each
// mandatory step — wait trigger → track tutorial_step_enter → apply
// pausePressure/grant → play the intro dialogue (the GUIDE — the player's first
// pet-Echo, WO-1012 P2; speaker = the "{guide}" token resolved via
// TutorialGuide — through the SAME custom
// dialogue system as every NPC) → arm the WO-1012 presentation kit (FocusMask +
// GuidePointer chevron + ObjectiveStrip; the ONE corner skip lives for the whole
// flow) → await the
// ONE completion signal (TutorialSignals) with a generous STEP-STUCK watchdog →
// play outro → track complete → persist SeenTutorials → next. On the last step
// it is the ONLY V2-path caller of GameStateService.FinishOnboarding() and it
// kicks WaveManager.BeginLoop (the same handoff the legacy director does).
//
// WO-T3/T4 self-driving steps (2026-07-03): grant.prepaidTower credits one
// watchtower's crystals through GameStateService (idempotent per save); a step
// completing on wave.cleared spawns the scripted teaching wave via
// TutorialWaveSpawner (and polls its IsCleared, since it bypasses the wave
// loop); a step completing on arena.resolved:win stages ONE guaranteed rep via
// the OverworldEncounterSpawner factory chain once the hero crosses out.
//
// Contextual one-shot steps (flowId "contextual") ride the SAME registry: armed
// on their trigger signal, they play a short line + spotlight, never pause
// pressure, never gate, auto-complete, and persist "tutorial_ctx:<id>" so they
// fire once per save, ever.
//
// It contains NO content, NO UI construction (Core affordances only), NO
// economy — data in, signals out. Gated behind ff.tutorialv2 (default OFF);
// the legacy TutorialDirector stands down while the flag is ON (WO-T5 deletes it).
//
// Bot verifiability: FlowTrace.Step on step enter/complete, FlowTrace.Fail
// "STEP-STUCK :: <id>" after WatchdogSeconds without progress — headless
// oracles read the [Flow:Tutorial] lines.
// =============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.Tutorial;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreDialogue = DeNelle.Core.Dialogue;

namespace DeNelle.Village
{
    /// <summary>
    /// Data-driven tutorial interpreter (spec docs/TUTORIAL_V2_SPEC_2026-07-02.md).
    /// Self-bootstraps on hub scenes when <see cref="FeatureFlags.TutorialV2"/> is ON.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialFlow : MonoBehaviour
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        private const float SettleSeconds = 1.25f;      // scene settle (hero/NavMesh/HUD) — same as legacy
        private const float WatchdogSeconds = 120f;     // generous in-step bound → STEP-STUCK oracle
        // F8 seq 603 (STEP-STUCK :: founding_hollow, 2026-08-02): on a blank Build-Your-Own
        // town, PLACEMENT steps (completion "build.structure_placed:<id>") are legitimately
        // slow — the player browses the carousel, pans, and places into an empty field. Those
        // steps get this longer bound (per-kind const, matching the house style of the other
        // tuning consts above); the watchdog ALSO pauses outright while the builder is open
        // (TickWatchdog), so only truly-idle time counts against either bound.
        private const float PlacementWatchdogSeconds = 300f;

        // ── WO-1036 (F8 seq 2513/2343, 2026-08-17) — THE BACKGROUND-TIME DEFECT ─────────────
        // PROVEN FROM CAPTURE, not inferred. The watchdog used to hold a wall-clock stamp
        // (_watchdogAt) and trip on `Time.unscaledTime - _watchdogAt >= bound`. Time.unscaledTime
        // is NOT clamped by Time.maximumDeltaTime, so the first frame after the OS backgrounds and
        // restores the app carries the WHOLE suspend window as one unscaledDeltaTime — and the
        // whole window was charged to the step as "idle". The proving lines, one capture, one frame:
        //     [Flow:Tutorial] coach :: step 'founding_walk' idle 245s ... (beat 2/4)
        //     [Flow:Tutorial] STEP-STUCK :: founding_walk ... after 245s in-step (bound 120s ...
        //                     builderOpenedThisStep=False, coachBeats=2)
        //     [Flow:Offline]  Claim #6 (resume): resume window -- counting from the background edge
        //     [Flow:Offline]  Claim #6 (resume): ONE delta = 196s ...
        // Coach beat 2 is due at 90s and fired at 245s; the 4-beat cadence had only spent 2 beats.
        // TWO INDEPENDENT wall-clock timers were late by the SAME ~196s — that is a stopped frame
        // loop plus a resume jump, never a doubled bound. 45s (beat 1) + 196s (background) = 241s,
        // which is the 2026-08-15 capture to the second. The player had ~49s of played time on the
        // beat and it was rescued-and-SKIPPED on the resume frame, before they could move.
        // Compounding it: PauseController.OnApplicationPause(true) auto-pauses to timeScale 0 and
        // NEVER auto-resumes, so the rescue fires while the hero is frozen and cannot walk
        // ([Flow:HeroOwner] "WORLD CLOCK FROZEN: Time.timeScale=0.00", same capture).
        // THE FIX: the bound is spent by PLAYED FRAMES, never by wall clock. StepClock accumulates
        // per-frame unscaledDeltaTime CLAMPED to MaxWatchdogFrameStepSeconds, and excludes frames
        // where the world clock is frozen or the builder owns the screen. A suspend jump can now
        // contribute at most one clamped frame, and it is TRACED rather than silently charged.
        // ⚠ NOT a bound change: WatchdogSeconds stays 120f (WO-962 §3 forbids lengthening it).
        private const float MaxWatchdogFrameStepSeconds = 1f;

        private const float ReachedRadius = 6f;         // hero.reached:<anchor> proximity (m)
        private const float ContextualAutoCloseSeconds = 10f; // hint without dialogue: auto-dismiss
        /// <summary>
        /// WO-1340 — THE ESCAPE BOUND for a contextual TEACH step (one that waits on a real
        /// gameplay completion signal instead of on its own text box closing). Generous,
        /// because the player is meant to wander off, open other screens and come back: this
        /// is not a gate and nothing waits on it. But it is FINITE and it always ticks
        /// (TickContextual runs every frame in every phase), which is what makes a stuck
        /// teach beat impossible.
        ///
        /// ⚠ THIS IS NOT THE MANDATORY-CHAIN WATCHDOG AND MUST NOT BE CONFUSED WITH IT.
        /// WatchdogSeconds (120f) is a bound on a step that BLOCKS the FTUE, and WO-962 §3
        /// forbids lengthening it. This bound governs a hint that blocks nothing, so a longer
        /// value is not a softlock risk - the failure mode it prevents is a spotlight that
        /// never lets go, not a player who cannot proceed.
        /// </summary>
        private const float ContextualAwaitSeconds = 240f;

        // F8 seq 632 ROOT CAUSE 3 (2026-08-02): a step may author SEVERAL highlights
        // (founding_defend = ["hud.wave_button", "world.gate_direction"]) but UiSpotlight is a
        // singleton with ONE target, so only Highlight[0] was ever shown and the rest were
        // silently dropped. The flow now WALKS the whole list, rotating the single spotlight
        // across every authored id so each one is actually rendered.
        private const float HighlightCycleSeconds = 4f;

        // F8 seq 632 ROOT CAUSE 4 (2026-08-02): five silent minutes must never be possible. While a
        // step is stranded, re-state the objective as a toast on an escalating cadence, capped so it
        // can never become spam.
        //
        // ── WO-1238 (F8 seq 3610, 2026-08-26) — THE CADENCE IS MEASURED, NOT CHOSEN ─────────────
        // The ticket forbids guessing a cadence, so this ladder was derived from the durations the
        // flow ALREADY prints. Every "STEP-COMPLETE :: <id> (<n>s)" line under logs/ and tmp/f8pull
        // was counted — n=156 SUCCESSFUL completions, i.e. how long a player who gets there takes:
        //
        //     step              n     p50     p75     p90     max
        //     founding_walk    25    12.9s   28.0s   89.6s  152.6s
        //     founding_greet   36     4.0s    5.9s    6.2s   33.0s
        //     founding_stores  16    23.8s  150.0s  150.0s  150.0s
        //     ALL             156     6.2s   23.8s  120.0s  300.0s
        //
        //     71.2% of successful completions land under 15s
        //     76.9% under 25s
        //     84.0% under 45s   ... and STILL 84.0% under 60s
        //
        // ⭐ THE LOAD-BEARING NUMBER IS THAT FLAT STRETCH. Between 45s and 60s the curve does not
        // move: in the captured data NOBODY who was going to succeed succeeded in that window. A
        // player still awaiting at ~50s is LOST, not slow — that is the knee, and it is where the
        // coaching must escalate rather than repeat.
        //
        // The OLD cadence was a flat 45s x 4 beats. Against the 120s bound that could only ever
        // deliver TWO beats, and the first landed AFTER 84% of players had already finished — so it
        // coached almost nobody, and it coached them by saying the same sentence twice.
        //
        // The ladder is expressed as FRACTIONS OF THE STEP'S OWN BOUND, so the 300s placement kind
        // stretches with it and NEITHER BOUND IS TOUCHED (WO-962 §3 / WO-1238 "do not lengthen"):
        //     0.21 -> 25s default / 63s placement    beat 1: restate the objective
        //     0.42 -> 50s / 126s                     beat 2: escalate — say HOW, not just what
        //     0.71 -> 85s / 213s                     beat 3: strongest channel, 35s of headroom
        //                                                    before the 120s rescue
        private static readonly float[] CoachNudgeLadder = { 0.21f, 0.42f, 0.71f };

        /// <summary>Beat cap = the ladder's length. One authority, so a retune cannot leave the
        /// cap and the schedule disagreeing.</summary>
        private static int CoachNudgeMaxBeats => CoachNudgeLadder.Length;

        /// <summary>
        /// WO-1238: the toast sorting order for the BUILDER-CONFUSION redirect. The redirect is
        /// raised while build mode owns the screen, so it must sit above the build UI or the one
        /// message the player needs is the one they cannot see. Every other coach beat fires in
        /// the open world and uses ElarionUiKit's default.
        /// </summary>
        private const int CoachRedirectSortingOrder = 5200;

        /// <summary>Persistence key prefixes (SeenTutorials — additive, no schema change:
        /// GameState.SeenTutorials is an existing SerializableDict, SaveSchema.cs:254).</summary>
        private const string SeenPrefix = "tutorial_v2:";
        private const string CtxSeenPrefix = "tutorial_ctx:";
        private const string GrantSeenPrefix = "tutorial_v2_grant:";

        // ── WO-T3: prepaid-tower grant ("this first one is on me") ─────────────
        // One watchtower's crystal cost, credited through the SAME store BuildMenu
        // charges (GameStateService.AddCrystals -> GameState.Resources.Crystals,
        // BuildMenu.OnConfirmBuild). 150 = the Flame/Ice Tower cost — Flame is the
        // menu's default-selected variant AND (with Ice) the only variant the stub
        // material counts can afford (BuildMenu.GetMaterialCount: wood 20 / stone 5
        // — Stone Tower needs stone 10, Aether needs 8, so both are un-buildable
        // today). Owner-tunable; move to catalog data with the WO-31 "Week 6" table.
        private const int PrepaidTowerCrystals = 150;

        // ── Scripted town wave (spec step 4: "HORN BLAST", no Start-Wave press) ─
        private const int TownWaveCount = 3;

        // ── Staged world encounter (spec step 5: a guaranteed rep, not a hunt) ──
        private const float StagedRepMinDistance = 10f;   // ahead of the hero — outside AggroRange(8)
        private const float StagedRepMaxDistance = 16f;

        /// <summary>True while a pausePressure step runs — a hold OTHER systems may
        /// consult so mid-tutorial steps that FOLLOW the town wave don't re-open the
        /// loop (spec §2.1 pausePressure). The WaveManager first-run gate (!Onboarded)
        /// already covers the whole first run; this lock is the explicit seam.</summary>
        public static bool PressureHeld { get; private set; }

        // ── Probe/observability surface (AutoPilot AssertTutorialArms, F8-29) ──
        // Read-only: lets the headless probe assert "fresh save => flow is LIVE, not
        // parked Finished" without reflection. No behaviour hangs off these.

        /// <summary>Current interpreter phase name (probe read — e.g. "Settling", "AwaitCompletion", "Finished").</summary>
        public string PhaseName => _phase.ToString();

        /// <summary>True when the mandatory chain is parked <c>Finished</c> (returning player,
        /// already ran this session, or — the F8-29 failure — declined a fresh run).</summary>
        public bool IsFinished => _phase == Phase.Finished;

        /// <summary>True once a mandatory chain has started this session (the
        /// <c>s_ranThisSession</c> resume block) — a probe uses this to tell a legitimate
        /// mid-session Finished from the fresh-boot decline.</summary>
        public static bool RanThisSession => s_ranThisSession;

        /// <summary>Current mandatory step id, or null when idle/finished (probe read).</summary>
        public string CurrentStepId => _step != null ? _step.Id : null;

        /// <summary>
        /// WO-1414 C -- the completion signal the LIVE step is waiting on when that signal is a
        /// DIALOGUE ending, else null. Read off <c>_awaitSignal</c> through the published
        /// instance (<c>s_instance</c>), never mirrored into a second field: a copy is the
        /// duplicated state that goes stale, which is the failure this repo keeps paying for.
        /// <para>
        /// WHY ANY OF THIS EXISTS: on the owner's device 2026-09-05 the welcome-back modal
        /// opened over the founding beat, blocked the SKIP control
        /// (<c>SKIP_TOP_HIT_BLOCKED top=ObsidianPanel path=WelcomeBackUI/ObsidianPanel</c>), and the
        /// beat then died on its own watchdog (<c>STEP-STUCK :: founding_greet -- no
        /// 'dialogue.ended:tut_founding_greet' after 120s</c>) -- so the first-run tutorial was
        /// silently skipped. <c>OfflineHarvestService.TryShowPopup</c> reads this and DEFERS.
        /// </para>
        /// Null-safe and side-effect-free: no flow, no live step, or a step awaiting anything
        /// else all read as null.
        /// </summary>
        public static string AwaitedDialogueSignal
        {
            get
            {
                var flow = s_instance;
                // A LIVE step is required, not just a leftover signal string: _awaitSignal is
                // assigned per step (EnterStep) and is NOT nulled when the chain finishes, so
                // reading it alone would report "awaiting" forever after the last beat -- and a
                // deferral with no release is a worse defect than the popup it prevents.
                if (flow == null || flow._step == null || flow._phase == Phase.Finished) return null;
                string sig = flow._awaitSignal;
                return !string.IsNullOrEmpty(sig)
                       && sig.StartsWith(TutorialSignals.DialogueEndedPrefix, StringComparison.OrdinalIgnoreCase)
                    ? sig : null;
            }
        }

        /// <summary>WO-1414 C -- true while a live tutorial step is waiting for a dialogue to end.
        /// Nothing modal may open over that beat; see <see cref="AwaitedDialogueSignal"/>.</summary>
        public static bool IsAwaitingDialogue => AwaitedDialogueSignal != null;

        /// <summary>The live step's intro dialogue id (WO-702: SylasStewardInjector routes a
        /// manual Talk to this — replaying the current beat's line; null when none).</summary>
        public string CurrentIntroDialogueId =>
            _step != null && _step.Dialogue != null ? _step.Dialogue.Intro : null;

        /// <summary>
        /// F8 2026-07-08 ("died in tutorial") + the RECURRING F8 "enemies never spawn on the whole
        /// first run": TRUE only while the hero stands in the roster-less VILLAGE zone during the
        /// EARLY in-town FTUE. The ambient hostile spawners (WaveManager auto-loop, OverworldEncounter
        /// ring/scatter reps, RegionMobSpawner) consult this and stay OFF so the player cannot be
        /// killed while learning to build the town.
        /// <para>
        /// DECOUPLED from the whole first run (owner fix): the old definition suppressed for the
        /// entire run (ff.tutorialv2 and !Onboarded), and Onboarded only flips true when the tutorial
        /// COMPLETES/skips -- so leaving town never resumed spawns. The peace window still lifts the
        /// instant the hero ventures OUT of the village (the zone test below), resuming ambient spawns
        /// WITHOUT cancelling the tutorial.
        /// <para>
        /// END-AFTER-DEFEND (owner ruling 2026-07-24): the venture-out back half (world_encounter,
        /// return_home, freedom) was SCRAPPED, so the chain now ENDS at founding_defend. There is no
        /// longer an arena.resolved:win step, so VentureOutOrder returns int.MaxValue -- the ORDER
        /// boundary never trips and suppression holds for the whole (shorter) in-village tutorial,
        /// lifted normally by Onboarded when FinishFlow runs after the defend beat (the zone test still
        /// resumes spawns the moment the hero leaves the village even mid-tutorial).
        /// </para>
        /// Suppressed only when ALL hold: <c>ff.tutorialv2</c> on AND <see cref="GameState.Onboarded"/>
        /// false (outer guard -- a completed player is NEVER suppressed); a live TutorialFlow with the
        /// flow still running; the current step is BEFORE the venture-out beat (now moot -- no such
        /// step exists post-scrap, so this order test never trips); and the hero is in
        /// the roster-less Village zone (RegionSpawnTable.HasRoster false -- the SAME zone test the
        /// staged-rep probe uses). Any missing ref (no instance / no hero / thrown zone lookup) FAILS
        /// OPEN (returns false = spawns allowed) -- it never NREs and never wedges the world empty.
        /// <para>
        /// NOTE: the tutorial's OWN scripted encounters do NOT route through the gated ambient paths
        /// (the founding_defend teaching wave spawns via TutorialWaveSpawner ->
        /// WaveManager.SpawnEnemyForExternalMode; the staged world_encounter rep via EnemyFactory
        /// directly), so they still fire regardless of this flag -- only the AMBIENT sources suppress.
        /// </para>
        /// </summary>
        public static bool HostilesSuppressedForTutorial
        {
            get
            {
                // Outer guard: only the V2 FTUE, and only until onboarding completes. Once Onboarded
                // flips true (FinishFlow / SkipAll -> FinishOnboarding) this is NEVER suppressed
                // again -- a returning player always has live hostiles.
                if (!FeatureFlags.TutorialV2) return false;
                var svc = GameStateService.Instance;
                if (svc == null || svc.State == null || svc.State.Onboarded) return false;

                // Decoupled from !Onboarded ALONE (the recurring bug): the peace window now holds
                // ONLY while the hero is in-town and early in the FTUE. No instance / not started
                // yet -> fail open (spawns allowed), never suppress the whole first run.
                var flow = s_instance;
                if (flow == null) return false;
                return flow.IsInTownEarlyFtue();
            }
        }

        /// <summary>
        /// The TOWN WAVE CLOCK window: TRUE while the FTUE chain is LIVE on a not-yet-Onboarded
        /// save, REGARDLESS of hero zone. Distinct from <see cref="HostilesSuppressedForTutorial"/>,
        /// which is the ZONE-scoped AMBIENT window (leaving town resumes ambient spawns -- owner
        /// ruling 2026-07-24, deliberately NOT regressed here).
        /// <para>
        /// WHY A SECOND PREDICATE (F8 2026-08-05 "wave 1 attacked me while I was still on the
        /// tutorial screen"): the zone-scoped window is the right answer for AMBIENT hostiles but
        /// the WRONG one for the town wave CLOCK -- WaveManager consulted it at the DOOR
        /// (BeginLoop / GuardedKickoff) and, once EnterCountdown had armed the phase, the clock ran
        /// to zero and spawned no matter what the window said afterwards (captured: cd29.9 -> cd6.8
        /// with the tutorial live). WaveManager now re-checks THIS predicate every tick and stands
        /// the countdown down. Splitting the two stops the owner's two rulings from fighting over
        /// one boolean.
        /// </para>
        /// <para>
        /// <c>_phase != Finished</c> is deliberate and load-bearing: it closes the Awake-vs-Start
        /// race where <c>s_instance</c> is published in Awake but <c>_phase</c> is not set until
        /// Start. <c>Idle</c> can therefore only ever mean "Start has not run yet" -- Start parks
        /// returning players at <c>Finished</c>, and every exit (FinishFlow, SkipAll -> FinishFlow)
        /// sets Finished AND FinishOnboarding before kicking the loop. Do NOT weaken this to
        /// <c>!= Idle &amp;&amp; != Finished</c>.
        /// </para>
        /// Fails OPEN on a missing instance/state, the same rule as the ambient gate.
        /// </summary>
        public static bool WaveLoopSuppressedForTutorial => IsMandatoryChainLive;

        /// <summary>
        /// WO-1414 D -- TRUE while the MANDATORY FTUE chain owns the session: an armed instance
        /// on a not-yet-Onboarded save that has not parked at <c>Finished</c>. This is the ONE
        /// predicate for "the tutorial is in charge right now"; <see cref="WaveLoopSuppressedForTutorial"/>
        /// delegates to it rather than carrying a second copy of the same three checks.
        /// <para>
        /// ⚠ THIS IS THE KEY THE WELCOME-BACK DEFERRAL MUST USE, NOT <see cref="IsAwaitingDialogue"/>.
        /// Captured 2026-09-05 (F8 seq 4682/4705): at hub load the flow is ARMED but not STARTED --
        /// during the <c>SettleSeconds</c> window <c>_step</c> is still null, so
        /// <see cref="AwaitedDialogueSignal"/> reads null and the narrow key evaluates FALSE. The
        /// modal opened in that window, the chain then started UNDERNEATH it, and the founding beat
        /// died on its watchdog (<c>STEP-STUCK :: founding_greet</c>) with the SKIP control
        /// unreachable (<c>SKIP_TOP_HIT_BLOCKED top=ObsidianPanel path=WelcomeBackUI/ObsidianPanel</c>).
        /// The narrow key cannot close that window because the window is exactly where it reads null.
        /// </para>
        /// <para>
        /// <c>Idle</c> counts as LIVE, deliberately -- see the note on
        /// <see cref="WaveLoopSuppressedForTutorial"/>: <c>s_instance</c> is published in
        /// <c>Awake</c> but <c>_phase</c> is not set until <c>Start</c>, so <c>Idle</c> can only
        /// mean "Start has not run yet", which is the hub-load window this exists to cover. Do NOT
        /// weaken it to <c>!= Idle &amp;&amp; != Finished</c>. Fails OPEN (false) on a missing
        /// flag / state / instance, the same rule as the spawner gates.
        /// </para>
        /// </summary>
        public static bool IsMandatoryChainLive
        {
            get
            {
                if (!FeatureFlags.TutorialV2) return false;
                var svc = GameStateService.Instance;
                if (svc == null || svc.State == null || svc.State.Onboarded) return false;
                var flow = s_instance;
                if (flow == null) return false;          // fail-open, same rule as the ambient gate
                return flow._phase != Phase.Finished;
            }
        }

        /// <summary>WO-1414 D -- "<c>&lt;phase&gt;/&lt;stepId&gt;</c>" for the live flow, for a trace
        /// line raised from OUTSIDE the flow (the welcome-back deferral). Static because the
        /// deferral has no instance handle; null-safe, side-effect-free, and it names the Settle
        /// window honestly as <c>Settling/&lt;none&gt;</c> rather than printing an empty awaited signal.</summary>
        public static string LiveChainStateLine
        {
            get
            {
                var flow = s_instance;
                if (flow == null) return "<no flow>";
                return flow._phase + "/" + (flow._step != null ? flow._step.Id : "<none>");
            }
        }

        // -- FTUE peace window (decoupled from !Onboarded) ---------------------
        // The single live instance (set in Awake, cleared in OnDestroy) lets the static
        // HostilesSuppressedForTutorial getter the spawners already call consult the running
        // flow's current step + the hero's zone. One instance ever (Bootstrap dedupes).
        private static TutorialFlow s_instance;

        // Per-frame memo: HeroHealth consults the peace window on EVERY damage tick, so the zone
        // lookup runs at most once per frame no matter how many spawners ask.
        private int _suppressFrame = -1;
        private bool _suppressCached;

        // Cached order of the venture-out step (world_encounter -- the arena.resolved:win beat).
        // int.MinValue = uncomputed. END-AFTER-DEFEND (2026-07-24): that step was scrapped, so the
        // scan finds nothing and this resolves to int.MaxValue -- the order boundary never trips and
        // suppression is gated by the zone test alone (held in-village, lifted on leaving / Onboarded).
        private int _ventureOutOrder = int.MinValue;

        /// <summary>TRUE only while the hero stands in the roster-less Village zone during the early
        /// in-town FTUE (before the venture-out beat). Fails open on any missing ref.</summary>
        private bool IsInTownEarlyFtue()
        {
            if (_suppressFrame == Time.frameCount) return _suppressCached;
            _suppressFrame = Time.frameCount;
            _suppressCached = ComputeInTownEarlyFtue();
            return _suppressCached;
        }

        private bool ComputeInTownEarlyFtue()
        {
            // Flow must be live -- a fresh run in progress, never idle/returning/finished.
            if (_phase == Phase.Idle || _phase == Phase.Finished) return false;

            // Boundary: suppress only for steps BEFORE the venture-out beat. END-AFTER-DEFEND
            // (2026-07-24): the venture-out steps are scrapped, so VentureOutOrder is int.MaxValue and
            // this order test never trips -- founding_defend (order 45, the final step) stays suppressed
            // and its teaching wave still fires (TutorialWaveSpawner bypasses this gate). Suppression
            // then lifts via Onboarded when FinishFlow runs after defend, or the moment the hero leaves
            // the village (the zone test below). During the pre-first-step Settling window
            // (_step == null) treat as before-boundary.
            if (_step != null && _step.Order >= VentureOutOrder()) return false;

            // Zone: the hero must be in the roster-less Village zone. HasRoster is TRUE only in
            // OuterWorld roster regions (the SAME test as the staged-rep zone check below), so
            // in-village = NOT HasRoster. Resolve the hero lazily; no hero -> fail open.
            var hero = _hero != null ? _hero : (_hero = FindAnyObjectByType<HeroLocomotion>());
            if (hero == null) return false;

            Vector3 heroPos = hero.transform.position;
            bool hasRoster = true;   // fail-open: a thrown zone lookup => treat as not-in-village
            Guard.Try("Tutorial", "hostile-suppression zone check", () =>
                hasRoster = DeNelle.Core.World.RegionSpawnTable.HasRoster(
                    DeNelle.Core.World.ZoneManager.GetZone(heroPos)));
            return !hasRoster;
        }

        /// <summary>Order of the venture-out step (world_encounter -- the arena.resolved:win beat):
        /// the boundary below which the in-town peace window holds. int.MaxValue when no such step
        /// exists (data change) or the chain has not loaded yet -- the zone test still gates it.
        /// END-AFTER-DEFEND (2026-07-24): that step was scrapped from the chain, so this now ALWAYS
        /// returns int.MaxValue -- kept as a graceful no-op boundary; the zone test is the live gate.</summary>
        private int VentureOutOrder()
        {
            if (_ventureOutOrder != int.MinValue) return _ventureOutOrder;
            if (_steps == null) return int.MaxValue;   // chain not loaded yet -- do not cache
            int found = int.MaxValue;
            foreach (var s in _steps)
            {
                if (s == null || s.Completion == null) continue;
                if (string.Equals(s.Completion.Signal, TutorialSignals.ArenaWin, StringComparison.OrdinalIgnoreCase))
                { found = s.Order; break; }
            }
            _ventureOutOrder = found;
            return found;
        }

        // =====================================================================
        //  WO-1036 — StepClock: the watchdog's budget, spent in PLAYED FRAMES
        // ---------------------------------------------------------------------
        //  PUBLIC and PURE on purpose: it reads no UnityEngine.Time, so the oracle
        //  (Assets/Editor/Regression/TutorialWatchdogBoundRegression.cs) can replay the
        //  captured 196s suspend jump deterministically and assert the bound is honoured.
        //  Nothing else in the project may re-implement this accounting — one owner.
        // =====================================================================

        /// <summary>Frame-accumulated in-step budget. <see cref="Tick"/> is fed one raw
        /// <c>Time.unscaledDeltaTime</c> per frame; anything larger than
        /// <see cref="MaxFrameStepSeconds"/> is an app-suspend jump, not played time, and is
        /// clamped + recorded instead of charged (WO-1036).</summary>
        public sealed class StepClock
        {
            /// <summary>Largest single frame that can count as played time. A 5 FPS device
            /// still accumulates honestly (0.2s frames); a backgrounded app cannot.</summary>
            public const float MaxFrameStepSeconds = MaxWatchdogFrameStepSeconds;

            /// <summary>Seconds of played frames since the last <see cref="Reset"/>, excluded
            /// frames included. The honest "how long has the player been on this beat".</summary>
            public float Played { get; private set; }

            /// <summary>Seconds charged against the watchdog bound — played time MINUS the
            /// excluded frames (builder open / world clock frozen).</summary>
            public float Charged { get; private set; }

            /// <summary>Seconds of played frames deliberately NOT charged (builder / frozen).</summary>
            public float Excluded { get; private set; }

            /// <summary>Wall-clock seconds discarded as app-suspend jumps — the WO-1036 signal.
            /// Non-zero means the OS backgrounded the app during this step.</summary>
            public float DiscardedJumpSeconds { get; private set; }

            /// <summary>How many frames carried a suspend-sized delta.</summary>
            public int DiscardedJumpFrames { get; private set; }

            /// <summary>Full reset — a new step begins.</summary>
            public void Reset()
            {
                Played = 0f; Charged = 0f; Excluded = 0f;
                DiscardedJumpSeconds = 0f; DiscardedJumpFrames = 0;
            }

            /// <summary>Zero only the charged budget (the step "begins again" for the player —
            /// the WO-702 deferred-intro release). Played/discard history is kept as evidence.</summary>
            public void RestartCharged() => Charged = 0f;

            /// <summary>Accept one frame. Returns the seconds actually accepted (clamped).</summary>
            /// <param name="rawUnscaledDelta">Raw <c>Time.unscaledDeltaTime</c> for this frame.</param>
            /// <param name="excluded">True when this frame must not be charged (builder open,
            /// or the world clock is frozen so the hero physically cannot act).</param>
            public float Tick(float rawUnscaledDelta, bool excluded)
            {
                if (rawUnscaledDelta <= 0f || float.IsNaN(rawUnscaledDelta)) return 0f;
                float step = rawUnscaledDelta;
                if (step > MaxFrameStepSeconds)
                {
                    DiscardedJumpSeconds += rawUnscaledDelta - MaxFrameStepSeconds;
                    DiscardedJumpFrames++;
                    step = MaxFrameStepSeconds;
                }
                Played += step;
                if (excluded) Excluded += step; else Charged += step;
                return step;
            }

            /// <summary>True once the charged budget has spent <paramref name="bound"/> seconds.</summary>
            public bool Expired(float bound) => Charged >= bound;
        }

        private enum Phase { Idle, Settling, WaitTrigger, Running, AwaitCompletion, Finished }

        private List<TutorialStepDef> _steps;            // mandatory chain (ordered)
        private List<TutorialStepDef> _contextual;       // contextual one-shots
        private int _index = -1;
        private Phase _phase = Phase.Idle;
        private TutorialStepDef _step;
        private float _stepEnteredAt;               // WALL clock — analytics + the honest "wall vs played" split
        /// <summary>WO-1036: the watchdog/coach budget, spent in PLAYED frames. Replaces the old
        /// wall-clock <c>_watchdogAt</c> stamp, which charged app-background time to the step.</summary>
        private readonly StepClock _stepClock = new StepClock();
        private bool _suspendJumpTraced;            // one [Flow:Tutorial] line per step per suspend
        /// <summary>WO-1414 D: the slice of <see cref="StepClock.Excluded"/> that was excluded
        /// because a MODAL owned the screen, kept separately so the STEP-STUCK breakdown can say
        /// WHY, not just how much. StepClock stays pure and untouched (it is pinned by
        /// TutorialWatchdogBoundRegression); this is the reason-tag that lives with the flow.</summary>
        private float _modalExcludedSeconds;
        private int _skips;
        private float _flowStartedAt;
        private bool _completionArmed;
        private string _awaitSignal;

        // Highlight walk (ROOT CAUSE 3) — every authored id for the live step, rotated through the
        // single UiSpotlight so none is silently dropped.
        private readonly List<string> _highlightIds = new List<string>();
        private int _highlightIndex;
        private float _nextHighlightAt;

        // Coach escalation (ROOT CAUSE 4) — per-step nudge bookkeeping.
        private int _coachBeats;
        private float _nextCoachAt;   // WO-1036: a CHARGED-seconds threshold on _stepClock, not a wall stamp
        private bool _builderOpenedThisStep;
        // WO-1238: the builder-confusion redirect is ONE per step. Opening an unrelated menu is a
        // confusion tell worth answering once; answering it every toggle would be nagging.
        private bool _builderRedirectFired;
        // WO-1238: set on the builder-open edge of a NON-placement step, cleared when the redirect
        // is re-delivered on the CLOSE edge — the toast is best-effort over the build UI, the
        // close-edge guide line is the guaranteed delivery.
        private bool _builderRedirectPending;

        private HeroLocomotion _hero;
        private WaveManager _wave;

        // Scripted town-wave runtime state (step 'town_wave', WO-T4).
        private TutorialWaveSpawner _tutorialWave;
        private bool _townWaveArmed;          // scripted wave requested for the current step
        private bool _townWaveSpawnSettled;   // SpawnAt finished — IsCleared is meaningful now

        // Staged world-encounter runtime state (step 'world_encounter', WO-T4).
        private bool _stagedRepPending;       // waiting for the hero to cross into OuterWorld
        private bool _stagedRepDone;          // one rep staged (or staging abandoned) this step
        private float _nextStageProbeAt;      // 1 Hz staging probe

        // Contextual runtime state.
        private TutorialStepDef _activeCtx;
        private float _ctxEnteredAt;
        /// <summary>WO-1340 — the gameplay signal the live contextual TEACH step waits on
        /// (null for an ordinary hint, which completes on its own dialogue ending).</summary>
        private string _ctxAwaitSignal;
        /// <summary>WO-1340 — one CTX-STUCK line per contextual teach, never a per-frame spam.</summary>
        private bool _ctxStuckTraced;

        private static bool s_ranThisSession;

        // =====================================================================
        //  Bootstrap (no scene edit) — mirrors the legacy director's pattern
        // =====================================================================

        // F8-29 (owner fresh-boot "i did not get a tutorial", RCA 2026-07-08): this was a ONE-SHOT
        // AfterSceneLoad that evaluated the hub gate against the TITLE scene (IsHub false) and —
        // unlike its sibling bootstraps (CompanionMeetingTrigger, CastleCompanionIntroducerInjector)
        // — never subscribed sceneLoaded, so the V2 interpreter could NEVER construct in the real
        // Title -> HeroSelect -> hub flow. Proof: zero [Flow:Tutorial] lines in the fresh session.
        // Now: evaluate at boot AND on every scene load; the decline path traces (never silent).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnAnySceneLoaded;
            SceneManager.sceneLoaded += OnAnySceneLoaded;
            TryArm("boot");
        }

        private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode) => TryArm("sceneLoaded");

        private static void TryArm(string reason)
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!FeatureFlags.TutorialV2)
            {
                FlowTrace.Step("Tutorial", $"Bootstrap({reason}): ff.tutorialv2 OFF — dormant.");
                return;
            }
            if (!HubScenes.IsHub(scene))
            {
                FlowTrace.Step("Tutorial", $"Bootstrap({reason}): scene '{scene}' is not a hub — waiting.");
                return;
            }
            // F8 seq 632 ROOT CAUSE 1 (2026-08-02) - the flow could arm where building is IMPOSSIBLE.
            // Village2 is BOTH a HubScenes.Names entry AND ownership:"Enemy" in scene-configs.json.
            // BuildModeController.Enter() refuses outright on an enemy-owned scene, so EVERY placement
            // step ("build.structure_placed:<id>") is a GENUINE can-never-complete state there: the
            // player taps the spotlit BUILD button and nothing happens, forever. The owner sat 300s on
            // founding_hollow and was auto-advanced by the watchdog.
            //
            // WHY THIS GATE AND NOT step.Scene: TutorialStepDef.Scene is DEAD DATA (Phase.WaitTrigger is
            // declared and never assigned; the only .Trigger reads are the contextual path), and the
            // authored value is "MainCastle_Hall" - a LEGACY scene, NOT the live hub
            // (Main_Castle_Overworld, CLAUDE.md sec.7). Honouring it would stop the FTUE from EVER
            // running. The correct, smaller fix is the semantic one: the FTUE is a TOWN flow, so refuse
            // to arm anywhere its steps cannot complete. sceneLoaded re-evaluates, so walking back into
            // the home hub still arms it.
            if (HubScenes.IsEnemyOwnedScene(scene))
            {
                FlowTrace.Warn("Tutorial", $"Bootstrap({reason}): hub scene '{scene}' is ENEMY-OWNED " +
                    "(scene-configs.json ownership=Enemy) - build mode is refused there, so the founding " +
                    "placement steps could never complete. NOT arming; re-evaluated on the next scene load.");
                return;
            }
            if (FindAnyObjectByType<TutorialFlow>() != null) return;
            var go = new GameObject("TutorialFlow");
            go.AddComponent<TutorialFlow>();
            // WO-854 Silo E (2026-08-04): the signal adapters are NOT added here any more.
            // TutorialSignalAdapters now runs its own RuntimeInitializeOnLoadMethod bootstrap,
            // because this arm path returns early on ff.tutorialv2 OFF and on an enemy-owned hub -
            // which meant every wave/build/arena signal existed only while the FTUE was armed, and
            // story-quest stages that complete off those signals silently inherited the flag.
            // Adding the component here again would stand up a SECOND emitter host on a latching bus.
            go.AddComponent<TutorialWorldAnchors>();     // world.guide / world.gate_direction resolvers
            FlowTrace.Step("Tutorial", $"Bootstrap({reason}): TutorialFlow armed in hub '{scene}'.");
        }

        private void Awake()
        {
            // Publish the single live instance so the static HostilesSuppressedForTutorial getter
            // the spawners call can reach this flow's current step + hero zone. Set as early as
            // possible (before Start) so the peace window can hold from the first settle frame.
            s_instance = this;
        }

        private void Start()
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;

            _steps = TutorialStepCatalog.MandatorySteps();
            _contextual = TutorialStepCatalog.ContextualSteps();
            TutorialSignals.Raised += OnSignal;

            bool firstRun = state != null && !state.Onboarded && !s_ranThisSession;
            if (firstRun && _steps.Count > 0)
            {
                s_ranThisSession = true;
                _flowStartedAt = Time.unscaledTime;
                DeNelle.Core.Analytics.EventTracker.Track("tutorial_started", new
                {
                    flowId = TutorialStepCatalog.FlowId,
                    mode = OnboardingMode.FullTutorial ? "intro" : "new",
                });
                FlowTrace.Step("Tutorial", $"flow '{TutorialStepCatalog.FlowId}' started ({_steps.Count} steps).");
                _phase = Phase.Settling;
                _stepEnteredAt = Time.unscaledTime;
                // WO-1012 §2b piece 5 — THE ONE SKIP: a single corner control for the whole
                // flow (one confirm sheet inside TutorialSkipUi -> SkipAll). Shown once here,
                // hidden in FinishFlow. The banner's inline "Skip >" and the big Skip
                // Tutorial button are both retired.
                TutorialSkipUi.Show(SkipAll);
            }
            else
            {
                _phase = Phase.Finished;   // returning player — contextual watchers only
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            TutorialSignals.Raised -= OnSignal;
            PressureHeld = false;
            // WO-1012: a mid-run teardown (scene unload) must not strand the kit chrome —
            // the pieces are DontDestroyOnLoad singletons and would otherwise outlive the flow.
            if (_phase != Phase.Idle && _phase != Phase.Finished)
            {
                HideHighlight();
                GuideLineUi.Hide();
                ObjectiveStripUi.Hide();
                TutorialSkipUi.Hide();
                DeNelle.Pets.PetHeroLeash.ClearLeadTarget();   // WO-1012 P2: no stranded lead
                TutorialWorldAnchors.ClearLatch("flow torn down mid-run");   // WO-962
            }
        }

        // =====================================================================
        //  Update-driven state machine (headless-friendly; no UI dependency)
        // =====================================================================

        private void Update()
        {
            switch (_phase)
            {
                case Phase.Settling:
                    if (Time.unscaledTime - _stepEnteredAt >= SettleSeconds)
                    {
                        ResolveSceneRefs();
                        AdvanceToNextStep();
                    }
                    break;

                case Phase.AwaitCompletion:
                    TickStepClock();       // WO-1036: spend the bound in PLAYED frames, once per frame
                    TickDeferredIntro();   // WO-702 truce: release a builder-held intro
                    TickHighlightCycle();  // ROOT CAUSE 3: walk EVERY authored highlight
                    TickCoachNudge();      // ROOT CAUSE 4: never five silent minutes again
                    TickProximityProbe();
                    TickScriptedWave();
                    TickStagedEncounter();
                    TickWatchdog();
                    break;
            }

            TickContextual();
        }

        private void ResolveSceneRefs()
        {
            _hero = FindAnyObjectByType<HeroLocomotion>();
            _wave = FindAnyObjectByType<WaveManager>();
        }

        // =====================================================================
        //  Mandatory chain
        // =====================================================================

        private void AdvanceToNextStep()
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;

            while (true)
            {
                _index++;
                if (_index >= _steps.Count) { FinishFlow(); return; }
                _step = _steps[_index];
                if (_step == null || string.IsNullOrEmpty(_step.Id)) continue;

                // Resume support: a step already completed on this save never replays.
                if (state != null && state.SeenTutorials != null &&
                    state.SeenTutorials.TryGetValue(SeenPrefix + _step.Id, out bool seen) && seen)
                {
                    // WO-1012 P2: reconcile essential GRANTS on the resume-skip too — a save
                    // that marked the step seen before its grant persisted (or a save from
                    // before the starter-pet grant MOVED steps, founding_hollow → the ARRIVE
                    // beat) must never resume half-granted. Both applies are idempotent per
                    // save (the tutorial_v2_grant key persists first), so this is a no-op on
                    // any healthy save.
                    if (_step.Grant != null && _step.Grant.PrepaidTower) ApplyPrepaidTowerGrant(_step);
                    if (_step.Grant != null && _step.Grant.StarterPet) ApplyStarterPetGrant(_step);
                    FlowTrace.Step("Tutorial", $"step '{_step.Id}' already seen — resuming past it.");
                    continue;
                }

                // Prebuilt / Default-Town skip (owner ruling 2026-07-24): a build-teaching step marked
                // skipIfPrebuilt is SKIPPED when the town is already laid out (BaseLayout carries the
                // Default-Town seed signature). CRITICAL: apply its GRANTS first (a skipped step's
                // essential grant must still land — no player is ever left half-granted), reusing the exact
                // idempotent grant path SkipAll uses (ApplyPrepaidTowerGrant / ApplyStarterPetGrant),
                // then mark it seen so a resume never replays it — but do NOT play its intro dialogue.
                // A Build-Your-Own (blank template) town has no such signature (IsTownPrebuilt false),
                // so the kept build-teaching steps still run in full.
                if (_step.SkipIfPrebuilt && IsTownPrebuilt())
                {
                    FlowTrace.Step("Tutorial", $"step '{_step.Id}' skipIfPrebuilt — town is prebuilt (Default Town); " +
                        "applying its grants + marking seen, intro suppressed (owner ruling 2026-07-24).");
                    if (_step.Grant != null && _step.Grant.PrepaidTower) ApplyPrepaidTowerGrant(_step);
                    if (_step.Grant != null && _step.Grant.StarterPet) ApplyStarterPetGrant(_step);
                    GameStateService.Instance?.MarkTutorialSeen(SeenPrefix + _step.Id);
                    continue;
                }
                break;
            }

            EnterStep(_step);
        }

        private void EnterStep(TutorialStepDef step)
        {
            _stepEnteredAt = Time.unscaledTime;
            _stepClock.Reset();                    // WO-1036: a fresh PLAYED budget for this beat
            _suspendJumpTraced = false;
            _modalExcludedSeconds = 0f;            // WO-1414 D: per-step, like the clock it annotates
            _completionArmed = false;
            _awaitSignal = step.Completion != null ? step.Completion.Signal : null;
            _coachBeats = 0;
            _builderOpenedThisStep = false;
            _builderRedirectFired = false;
            _builderRedirectPending = false;
            _probeTraceAtCharged = 0f;
            // WO-1036: CHARGED seconds, not a wall stamp. WO-1238: the schedule now comes off the
            // ladder, which needs _awaitSignal (set above) to know which bound this step carries.
            _nextCoachAt = CoachBeatDueAt(0);

            FlowTrace.Step("Tutorial", $"STEP-ENTER :: {step.Id} (order={step.Order}, completes on '{_awaitSignal}').");

            // WO-1238 §1: PUBLISH THE SCHEDULE. The ticket's demand was to instrument rather than
            // guess, and the STEP-STUCK line can only report beats ALREADY spent. This names, on
            // entry, when each beat was due — so a future capture proves whether a beat was late,
            // suppressed, or simply never reached, without re-deriving the ladder from source.
            var due = new System.Text.StringBuilder();
            for (int i = 0; i < CoachNudgeMaxBeats; i++)
                due.Append(i > 0 ? " / " : "").Append(CoachBeatDueAt(i).ToString("0")).Append('s');
            FlowTrace.Step("Tutorial", $"COACH-LADDER :: {step.Id} - bound {WatchdogSecondsForCurrentStep():0}s, " +
                $"{CoachNudgeMaxBeats} beat(s) due at {due} of PLAYED time (WO-1238 measured ladder).");

            // WO-962 (owner F8 seq 2301): a hero.reached:<anchor> step LATCHES its anchor on
            // ENTER. "Nearest gate" is measured from the HERO, so a live per-frame resolve
            // moved the target to a different side of town every time the player obeyed it
            // (south -> east -> north in one founding_walk) and the beat could not be walked
            // to. Resolve ONCE here; TickProximityProbe re-calls LatchAnchor only to cover an
            // anchor that is not resolvable YET (it is a no-op once latched). The latch is
            // dropped in CompleteCurrentStep / FinishFlow / teardown, so a re-entered step
            // resolves once again. NOT fixed by widening ReachedRadius or the watchdog.
            if (!string.IsNullOrEmpty(_awaitSignal) &&
                _awaitSignal.StartsWith(TutorialSignals.HeroReachedPrefix, StringComparison.OrdinalIgnoreCase))
                TutorialWorldAnchors.LatchAnchor(_awaitSignal.Substring(TutorialSignals.HeroReachedPrefix.Length));
            else
                TutorialWorldAnchors.ClearLatch($"step '{step.Id}' does not complete on hero.reached");
            DeNelle.Core.Analytics.EventTracker.Track("tutorial_step_enter", new
            {
                stepId = step.Id,
                order = step.Order,
                flowId = TutorialStepCatalog.FlowId,
            });

            // Pressure hold — the WaveManager first-run gate (!Onboarded,
            // WaveManager auto-arm block) covers the whole first run; this exposes
            // the explicit per-step lock for anything else that pushes pressure.
            PressureHeld = step.PausePressure;

            // Grant (WO-T3): the guided-build prepaid tower — credit one watchtower's
            // cost through the SAME economy seam BuildMenu charges. Idempotent per save.
            if (step.Grant != null && step.Grant.PrepaidTower)
                ApplyPrepaidTowerGrant(step);

            // Grant (WO-1012 P2, owner re-ruling 2026-08-09): the starter-pet grant is
            // ENTER-side now — the guide IS the pet-Echo, so it must exist (roster +
            // deployed body via PetAcquisitionService → PetDeployer.SyncDeployedToSlots)
            // BEFORE the beat's dialogue plays in its voice. Moved here from
            // CompleteCurrentStep (the WO-702 reward-follows-placement rule) in the same
            // change that moved the grant from founding_hollow to the ARRIVE beat
            // (founding_greet — tutorial-steps.json v2): the pet wakes near the Heart as
            // the cold open's payoff, then speaks. Idempotent per save.
            if (step.Grant != null && step.Grant.StarterPet)
                ApplyStarterPetGrant(step);

            // PART 2 (guided-build tolerance): a build step whose demanded structure is
            // ALREADY placed (free-carousel player, or a tower dropped before the step
            // armed) must not burn the 120s watchdog. If BaseLayout already satisfies the
            // completion signal, complete NOW down the normal path (grants + outro + advance)
            // instead of clearing the latch and waiting on an event that already fired.
            if (TryAutoCompleteAlreadyBuilt(step)) return;

            // CLEAR the completion latch BEFORE the intro plays: a stale earlier raise
            // must not complete the step, but a raise DURING the intro must (e.g. a
            // dialogue.ended completion that IS the intro's own end).
            if (!string.IsNullOrEmpty(_awaitSignal)) TutorialSignals.Clear(_awaitSignal);
            _completionArmed = true;
            _phase = Phase.AwaitCompletion;

            // WO-T4 self-driving combat steps — keyed on the COMPLETION SIGNAL (data-
            // driven: no step-id branching), so the json stays the only content source.
            //  * wave.cleared      -> spawn the scripted teaching wave (spec step 4:
            //    "HORN BLAST" — the step must complete WITHOUT the player pressing
            //    Start Wave; the loop is held closed by the !Onboarded gate anyway).
            //  * wave.tutorial_band_repelled -> the SAME scripted band (WO-1012 P3,
            //    beat 7 ENEMIES AT THE GATE): the arc's payoff beat completes on the
            //    band-scoped signal so an ambient clear can never satisfy it. The
            //    peace window (WaveLoopSuppressedForTutorial) holds the loop closed,
            //    so ONLY this band spawns during the arc.
            //  * arena.resolved:win -> stage ONE guaranteed rep once the hero crosses
            //    into OuterWorld (spec step 5 — no hunting for a random encounter).
            if (string.Equals(_awaitSignal, TutorialSignals.WaveCleared, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_awaitSignal, TutorialSignals.TutorialBandRepelled, StringComparison.OrdinalIgnoreCase))
                StartScriptedTownWave(step);
            if (string.Equals(_awaitSignal, TutorialSignals.ArenaWin, StringComparison.OrdinalIgnoreCase))
            {
                _stagedRepPending = true;
                _stagedRepDone = false;
                _nextStageProbeAt = 0f;
                FlowTrace.Step("Tutorial", $"step '{step.Id}' will stage one guaranteed rep once the hero enters an OuterWorld roster region.");
            }

            // Presentation (Core kit affordances — read the step model only).
            // WO-1012 §2b piece 3: the thin bottom-center ObjectiveStrip replaces the fat
            // top banner. ONE sentence + whole-chain progress beads (done = steps behind
            // us in the chain, resume-skips included) — the per-step "(0/1)" counter and
            // the banner's skip affordances are retired (Objective.Count is now unread;
            // the ONE skip is the TutorialSkipUi corner control armed at flow start).
            // WO-1012 P2: objective texts may name the guide via the "{guide}" data
            // token — resolved through the identity seam at render time, never stored.
            if (step.Objective != null && !string.IsNullOrEmpty(step.Objective.Text))
                ObjectiveStripUi.Show(TutorialGuide.ResolveToken(step.Objective.Text), done: _index, total: _steps.Count);
            else
                ObjectiveStripUi.Hide();
            ArmHighlights(step);

            // Intro dialogue — the standard NPC template; its end raises
            // dialogue.ended:<id> through the Core adapter.
            // WO-702 truce (owner F8 2026-07-13 "pause the sylas dialogue till either
            // action asked is completed or closed builder"; captured collision:
            // STEP-STUCK :: founding_town — the intro opened BEHIND the build palette
            // and sat unread 120s): while the builder is open, HOLD the intro and play
            // it when build mode exits (TickDeferredIntro). The completion gate stays
            // dialogue.ended — the dialogue simply presents when it can be read.
            if (step.Dialogue != null && !string.IsNullOrEmpty(step.Dialogue.Intro))
            {
                if (DeNelle.Core.BuildModeState.IsActive)
                {
                    _deferredIntroStepId = step.Id;
                    _deferredIntroId = step.Dialogue.Intro;
                    FlowTrace.Step("Tutorial",
                        $"deferred-intro :: step '{step.Id}' intro '{step.Dialogue.Intro}' held while the builder is open (WO-702 truce) — plays on builder exit.");
                }
                else if (!CoreDialogue.DialogueService.Play(step.Dialogue.Intro))
                    FlowTrace.Warn("Tutorial", $"step '{step.Id}' intro dialogue '{step.Dialogue.Intro}' unknown — continuing without it.");
            }
        }

        // ── PART 2: guided-build tolerance — auto-complete an already-built step ──
        // A founding-arc build step whose completion is a BUILD signal must not strand for
        // the full watchdog when the player ALREADY placed the demanded structure. On
        // EnterStep, if GameState.BaseLayout already satisfies the await signal, complete
        // the step immediately down the SAME completion path a real signal takes (grants,
        // outro, seen-mark, advance) — never clear-the-latch-and-wait on an event that fired
        // before the step armed. Chains cleanly: each auto-complete advances to the next
        // step, which re-checks (a carousel player who placed pet-house + lumberyard + a
        // tower up front walks through all three build steps at once).
        //
        // Signal -> BaseLayout presence:
        //   build.tower_placed          -> ANY Tower/Gate (defense) structure present
        //   build.structure_placed:<id> -> that itemId present (collector ids: lumberyard, pet-house)

        /// <summary>Auto-complete the entering step if BaseLayout already satisfies its build
        /// completion signal. Returns true when it consumed the step (caller returns without
        /// arming AwaitCompletion). No-op for any non-build step.</summary>
        private bool TryAutoCompleteAlreadyBuilt(TutorialStepDef step)
        {
            if (string.IsNullOrEmpty(_awaitSignal)) return false;
            if (!BaseLayoutSatisfiesBuildSignal(_awaitSignal)) return false;

            FlowTrace.Step("Tutorial", $"STEP-AUTOCOMPLETE :: {step.Id} — completion signal '{_awaitSignal}' " +
                "already satisfied by an existing structure in BaseLayout; completing without waiting (guided-build tolerance).");
            CompleteCurrentStep(skipped: false);   // normal path: grants + outro + seen-mark + advance
            return true;
        }

        /// <summary>Maps a build completion signal to a GameState.BaseLayout presence check.
        /// False for any non-build signal (proximity / dialogue / wave / arena steps are never
        /// auto-completed this way) and when nothing is placed yet.</summary>
        private static bool BaseLayoutSatisfiesBuildSignal(string signal)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.BaseLayout == null || state.BaseLayout.Count == 0) return false;

            // build.structure_placed:<id> — a specific catalog itemId must already be placed.
            if (signal.StartsWith(TutorialSignals.StructurePlacedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string wantId = signal.Substring(TutorialSignals.StructurePlacedPrefix.Length);
                if (string.IsNullOrEmpty(wantId)) return false;
                foreach (var rec in state.BaseLayout)
                    if (BuildIdMatches(rec.itemId, wantId))
                        return true;
                return false;
            }

            // build.tower_placed — ANY tower/defense structure already present.
            if (string.Equals(signal, TutorialSignals.TowerPlaced, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var rec in state.BaseLayout)
                    if (IsDefenseStructure(rec.itemId)) return true;
                return false;
            }

            return false;   // not a build signal
        }

        /// <summary>
        /// True when a placed record's itemId satisfies a wanted build id. Normally an
        /// ordinal-ignore-case equality, plus WO-748 id-drift reconciliation: the Default
        /// Town seed writes the wood storefront as <c>collector_lumbermill</c> (the seed's
        /// collector id; earlier migrations used <c>lumbermill</c>), but the founding_stores
        /// FTUE step keys on <c>lumberyard</c>. They are the same wood-resource building under
        /// different catalog ids, so accept any — the guided-build step auto-satisfies for a
        /// Default Town founding (and for a self-built lumberyard).
        /// </summary>
        private static bool BuildIdMatches(string recordId, string wantId)
        {
            if (string.Equals(recordId, wantId, StringComparison.OrdinalIgnoreCase)) return true;
            return IsWoodResourceId(recordId) && IsWoodResourceId(wantId);
        }

        /// <summary>The interchangeable wood-resource building ids (WO-748 id drift): the FTUE
        /// step's <c>lumberyard</c>, the older migration's <c>lumbermill</c>, and the current
        /// Default-Town seed's <c>collector_lumbermill</c> — all the same wood collector.</summary>
        private static bool IsWoodResourceId(string id)
        {
            return string.Equals(id, "lumberyard", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "lumbermill", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "collector_lumbermill", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Prebuilt/Default-Town detection (owner ruling 2026-07-24): true when
        /// GameState.BaseLayout carries the Default-Town seed signature — a seeded Echo Hollow
        /// (<c>pet-house</c>) or wood collector (<c>collector_lumbermill</c> / lumbermill / lumberyard,
        /// via <see cref="IsWoodResourceId"/>). This is the SAME BaseLayout presence the guided-build
        /// auto-completer reads, NOT StrategicPlacementMigrated (already flipped back true by tutorial
        /// time). A Build-Your-Own (blank template) town leaves BaseLayout without the signature, so this
        /// returns false and the build-teaching steps (founding_hollow/stores/town) run in full.</summary>
        private static bool IsTownPrebuilt()
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null || state.BaseLayout == null || state.BaseLayout.Count == 0) return false;
            foreach (var rec in state.BaseLayout)
            {
                if (string.IsNullOrEmpty(rec.itemId)) continue;
                if (string.Equals(rec.itemId, "pet-house", StringComparison.OrdinalIgnoreCase)) return true;
                if (IsWoodResourceId(rec.itemId)) return true;
            }
            return false;
        }

        /// <summary>True when a placed itemId resolves to a Tower/Gate (Defense) in the catalog
        /// — the "any tower/defense structure present" rule for build.tower_placed.</summary>
        private static bool IsDefenseStructure(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(itemId);
            if (entry == null) return false;
            return entry.type == DeNelle.Core.Catalog.CatalogType.Tower
                || entry.type == DeNelle.Core.Catalog.CatalogType.Gate;
        }

        // ── WO-702: deferred step-intro (dialogue/builder truce) ──────────────
        // A step whose intro would have opened UNDER the build palette holds it here;
        // the Update loop releases it the first frame the builder is closed. If the
        // step already completed/changed while deferred (e.g. the player finished the
        // asked build action), the stale intro is dropped with a trace, never played
        // over the NEXT step.
        private string _deferredIntroStepId;
        private string _deferredIntroId;

        private void TickDeferredIntro()
        {
            if (string.IsNullOrEmpty(_deferredIntroId)) return;
            if (DeNelle.Core.BuildModeState.IsActive)
            {
                // Builder still open — keep holding. WO-1036: the STEP-STUCK watchdog is already
                // protected from a legitimate long builder session because TickStepClock EXCLUDES
                // every build-mode frame from the charged budget; no stamp to push here any more.
                return;
            }

            string id = _deferredIntroId, stepId = _deferredIntroStepId;
            _deferredIntroId = null;
            _deferredIntroStepId = null;

            if (_step == null || !string.Equals(_step.Id, stepId, StringComparison.OrdinalIgnoreCase))
            {
                FlowTrace.Step("Tutorial",
                    $"deferred-intro :: held intro '{id}' for step '{stepId}' DROPPED — the step is no longer live (completed/changed while the builder was open).");
                return;
            }

            FlowTrace.Step("Tutorial",
                $"deferred-intro :: builder closed — playing held intro '{id}' for step '{stepId}' (WO-702 truce).");
            // The step effectively BEGINS for the player when the intro finally shows —
            // restart the STEP-STUCK budget so a long, legitimate builder session doesn't
            // count against the watchdog (WO-1036: charged budget only; the played/discard
            // history is kept so the STEP-STUCK line can still show what really happened).
            _stepClock.RestartCharged();
            if (!CoreDialogue.DialogueService.Play(id))
                FlowTrace.Warn("Tutorial", $"step '{stepId}' deferred intro dialogue '{id}' unknown — continuing without it.");
        }

        // =====================================================================
        //  ROOT CAUSE 3 (F8 seq 632) — walk EVERY authored highlight
        // ---------------------------------------------------------------------
        //  EnterStep used to do `UiSpotlight.Show(step.Highlight[0])` and drop the rest
        //  on the floor with no trace. founding_defend authors
        //  ["hud.wave_button", "world.gate_direction"], so the GATE callout — the half
        //  that tells the player WHERE the enemies come from — never rendered, ever.
        //  UiSpotlight is a singleton with ONE target, so the fix is to ROTATE it across
        //  the whole authored list on a slow, readable cadence rather than to teach only
        //  the first item. A single-id step never cycles (no churn, no log spam).
        // =====================================================================

        /// <summary>Builds the live step's highlight walk and shows the first id. A PLACEMENT
        /// step with no authored highlight still falls back to the Build-button coach mark
        /// (the F8 seq 603 rule); a step with nothing at all clears the spotlight.</summary>
        private void ArmHighlights(TutorialStepDef step)
        {
            _highlightIds.Clear();
            _highlightIndex = 0;

            if (step != null && step.Highlight != null)
                foreach (var h in step.Highlight)
                    if (!string.IsNullOrEmpty(h) && !_highlightIds.Contains(h))
                        _highlightIds.Add(h);

            if (_highlightIds.Count == 0 && IsPlacementStep())
            {
                // F8 seq 603 (2026-08-02): a PLACEMENT step always lights the Build-button
                // coach highlight so the player is pointed at the door into the builder even
                // when the step data authors no highlight. Same registry path every authored
                // highlight takes (UiSpotlight -> TutorialHighlightRegistry; "hud.build_button"
                // is registered by HudKitController). This is the code-level guarantee
                // against data drift.
                _highlightIds.Add("hud.build_button");
                FlowTrace.Step("Tutorial", $"step '{step.Id}' placement step with no authored highlight - " +
                    "defaulting the coach highlight to 'hud.build_button' (F8 seq 603 rule).");
            }

            if (_highlightIds.Count == 0)
            {
                HideHighlight();
                return;
            }

            _nextHighlightAt = Time.unscaledTime + HighlightCycleSeconds;
            // WO-1012 §2b: FocusMask style follows the beat kind (tap = Focus dim+block,
            // gesture/movement = lighter Gesture, combat = Glow), and the ONE gold chevron
            // (GuidePointer) rides the same highlight id.
            ShowHighlight(_highlightIds[0], MaskStyleForCurrentStep());
            if (_highlightIds.Count > 1)
                FlowTrace.Step("Tutorial", $"step '{step.Id}' authors {_highlightIds.Count} highlights " +
                    $"[{string.Join(", ", _highlightIds)}] - the ONE spotlight rotates across all of them every " +
                    $"{HighlightCycleSeconds:0}s (F8 seq 632: only Highlight[0] used to render).");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  WO-1344 — ONE door for "point at this id", so the owner-tagged marker
        //  and the yellow FocusMask glow can never both be drawn on the same beat.
        //
        //  FtueWorldPointer.TryShow answers TRUE only for a WORLD-anchored highlight
        //  ("world.guide", "world.gate_direction") whose key can actually draw. For
        //  every UI-rect highlight ("hud.*", "build.card.*", "deck.card.skills") it
        //  answers FALSE and these two lines run EXACTLY as they always did — this is
        //  a presentation swap on the navigation beats only, not a behaviour change:
        //  same ids, same styles, same rotation, same completion signals.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Point at <paramref name="highlightId"/> with whichever cue owns it:
        /// the owner-tagged world marker when the id resolves to a world anchor, else the
        /// FocusMask + chevron at <paramref name="style"/> (the pre-WO-1344 behaviour).</summary>
        private static void ShowHighlight(string highlightId, UiSpotlight.MaskStyle style)
        {
            if (FtueWorldPointer.TryShow(highlightId))
            {
                // The marker serves this beat now — stand the yellow glow down so the
                // player is shown ONE cue, not two.
                UiSpotlight.Hide();
                GuidePointer.Hide();
                return;
            }
            UiSpotlight.Show(highlightId, style);
            GuidePointer.Show(highlightId);
        }

        /// <summary>Stop pointing, whichever cue was doing it.</summary>
        private static void HideHighlight()
        {
            FtueWorldPointer.Hide();
            UiSpotlight.Hide();
            GuidePointer.Hide();
        }

        /// <summary>Rotates the single spotlight across every authored highlight id so none is
        /// silently dropped. No-op for a 0/1-id step.</summary>
        private void TickHighlightCycle()
        {
            if (_highlightIds.Count < 2) return;
            if (Time.unscaledTime < _nextHighlightAt) return;
            _nextHighlightAt = Time.unscaledTime + HighlightCycleSeconds;
            _highlightIndex = (_highlightIndex + 1) % _highlightIds.Count;
            ShowHighlight(_highlightIds[_highlightIndex], MaskStyleForCurrentStep());
        }

        /// <summary>WO-1012 §2b: the FocusMask style for the LIVE step, keyed on its
        /// completion signal (data-driven — no step-id branching):
        ///   * placement/movement beats -> Gesture (~35% dim, never blocks — the drag /
        ///     the walk must land anywhere, and the world stays readable);
        ///   * combat beats -> Glow (no dim: the player is fighting);
        ///   * everything else (tap-the-button / open-the-panel / read-the-line) ->
        ///     Focus (~65% dim, raycast-block outside the cutout — and UiSpotlight
        ///     itself only ever blocks on a resolved UI-rect target, so a world-anchored
        ///     Focus cutout still passes all input).</summary>
        private UiSpotlight.MaskStyle MaskStyleForCurrentStep()
        {
            string s = _awaitSignal;
            if (string.IsNullOrEmpty(s)) return UiSpotlight.MaskStyle.Glow;
            if (s.StartsWith(TutorialSignals.StructurePlacedPrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, TutorialSignals.TowerPlaced, StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith(TutorialSignals.HeroReachedPrefix, StringComparison.OrdinalIgnoreCase))
                return UiSpotlight.MaskStyle.Gesture;
            if (string.Equals(s, TutorialSignals.WaveCleared, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, TutorialSignals.TutorialBandRepelled, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, TutorialSignals.ArenaWin, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, TutorialSignals.ArenaLoss, StringComparison.OrdinalIgnoreCase))
                return UiSpotlight.MaskStyle.Glow;
            return UiSpotlight.MaskStyle.Focus;
        }

        /// <summary>The palette-card highlight id for the live placement step
        /// ("build.card.&lt;wanted structure id&gt;" — BuildPaletteUI registers one per
        /// rendered card), or null for any non-specific-placement step.</summary>
        private string PlacementCardHighlightId()
        {
            if (string.IsNullOrEmpty(_awaitSignal) ||
                !_awaitSignal.StartsWith(TutorialSignals.StructurePlacedPrefix, StringComparison.OrdinalIgnoreCase))
                return null;
            string wantId = _awaitSignal.Substring(TutorialSignals.StructurePlacedPrefix.Length);
            return string.IsNullOrEmpty(wantId) ? null : "build.card." + wantId;
        }

        // =====================================================================
        //  ROOT CAUSE 4 (F8 seq 632) — the escalating coach beat
        // ---------------------------------------------------------------------
        //  The owner sat FIVE SILENT MINUTES on founding_hollow. The objective banner was
        //  up, but nothing ever re-stated the ask and nothing noticed the builder had never
        //  been opened. A stranded player must be coached, not timed out: while the step is
        //  awaiting and the builder has NOT been opened even once, re-toast the objective
        //  on the measured ladder, escalating from the objective line to an explicit
        //  instruction, capped at CoachNudgeMaxBeats so it can never be spam.
        //
        // ─────────────────────────────────────────────────────────────────────
        //  ⭐ WO-1238 (F8 seq 3610, 2026-08-26) — WHY THE COACH SAID NOTHING
        // ---------------------------------------------------------------------
        //  CAPTURED, one line, not theorised:
        //
        //      [Flow:Tutorial] STEP-STUCK :: founding_walk - no 'hero.reached:guide_gate'
        //      after 120s in-step (... builderOpenedThisStep=True, coachBeats=0)
        //
        //  ZERO beats in 120s. The cause was not the cadence and not the watchdog: it was
        //  the line that used to read `if (_builderOpenedThisStep) return;` — an
        //  UNCONDITIONAL, permanent stand-down for the rest of the step. The player opened
        //  the BUILD menu during founding_walk, whose ask is "walk to the gate", and the
        //  coach read that as "the player found the door" and shut its mouth forever.
        //
        //  ⛔ THAT INFERENCE IS ONLY TRUE ON A PLACEMENT STEP. On a placement beat the ask
        //  IS the builder, so opening it is progress and standing down is correct. On every
        //  OTHER step, opening an unrelated menu is the exact OPPOSITE signal — it is the
        //  player telling you in behaviour that they do not know what the step wants. The
        //  flow was already RECORDING that tell (`builderOpenedThisStep`) and using it to
        //  suppress the one thing that would have helped.
        //
        //  So the stand-down is now conditioned on IsPlacementStep(), and on a non-placement
        //  step the builder-open edge instead fires a REDIRECT beat — the cheapest real win
        //  in the ticket, because the signal was already there.
        //
        //  ⛔ THE WATCHDOG IS NOT TOUCHED. Its rescue contract (step SKIPPED, outro
        //  suppressed, grants still applied) and the WO-1036 played-time clock are correct
        //  and are why this was a logged annoyance instead of a stuck player. This block
        //  only fills the 120 seconds BEFORE it fires.
        // =====================================================================

        /// <summary>
        /// PLAYED seconds at which coach beat <paramref name="beatIndex"/> (0-based) is due, as
        /// the ladder's fraction of THIS step's watchdog bound. Pure, so the regression can pin
        /// the schedule without a play session, and stateless, so a retune cannot drift.
        /// </summary>
        public static float CoachBeatDueAt(int beatIndex, float bound)
        {
            if (CoachNudgeLadder.Length == 0) return float.MaxValue;
            int i = Mathf.Clamp(beatIndex, 0, CoachNudgeLadder.Length - 1);
            return CoachNudgeLadder[i] * bound;
        }

        private float CoachBeatDueAt(int beatIndex)
            => CoachBeatDueAt(beatIndex, WatchdogSecondsForCurrentStep());

        /// <summary>The number of coach beats the ladder delivers inside <paramref name="bound"/>.
        /// Pinned by the regression: it must never be zero, or a stranded player is rescued
        /// without ever having been coached.</summary>
        public static int CoachBeatsInsideBound(float bound)
        {
            int n = 0;
            for (int i = 0; i < CoachNudgeLadder.Length; i++)
                if (CoachNudgeLadder[i] * bound < bound) n++;
            return n;
        }

        /// <summary>
        /// WO-1238 §3 — REACT TO THE BUILDER-OPEN SIGNAL. Called once per step, on the frame the
        /// builder first opens. On a PLACEMENT beat this is progress (the ask IS the builder), so
        /// the ladder stands down and the ghost finger takes over. On any other beat it is a
        /// confusion tell, and it is answered instead of swallowed.
        /// </summary>
        private void OnBuilderOpenedDuringStep()
        {
            if (IsPlacementStep())
            {
                FlowTrace.Step("Tutorial", $"coach :: step '{_step.Id}' - builder opened on a PLACEMENT beat; " +
                    "the escalating nudge stands down (the player has found the door).");
                // WO-1012 §2b piece 2: a PLACEMENT step is a gesture beat — once the
                // builder is open, the ghost finger replays the card->field drag arc
                // on a 2s loop until the first real placement (NotifyGestureSuccess in
                // CompleteCurrentStep fades it permanently).
                string card = PlacementCardHighlightId();
                if (card != null)
                    GuidePointer.ShowDrag(card, new Vector2(0.5f, 0.45f));
                return;
            }

            FlowTrace.Warn("Tutorial", $"coach :: step '{_step.Id}' - builder opened on a NON-placement beat " +
                $"awaiting '{_awaitSignal}' at {_stepClock.Charged:0}s played. That is a CONFUSION TELL, not " +
                "progress: the player opened an unrelated menu because they do not know what the step wants " +
                "(WO-1238, captured as builderOpenedThisStep=True with coachBeats=0). Redirecting; the ladder " +
                "does NOT stand down.");

            if (_builderRedirectFired) return;
            _builderRedirectFired = true;
            DeliverBuilderRedirect(onCloseEdge: false);
            // Guarantee a second delivery on the close edge, where nothing can occlude it.
            _builderRedirectPending = true;
        }

        /// <summary>
        /// The redirect itself. Fired twice at most for one step: best-effort over the build UI on
        /// the open edge (high sorting order), then guaranteed on the guide's portrait line when
        /// the builder closes and the player is back in the world.
        /// </summary>
        private void DeliverBuilderRedirect(bool onCloseEdge)
        {
            if (_step == null) return;

            string objective = _step.Objective != null && !string.IsNullOrEmpty(_step.Objective.Text)
                ? TutorialGuide.ResolveToken(_step.Objective.Text) : null;
            if (string.IsNullOrEmpty(objective))
            {
                // §12: no silent failure. An unauthored objective is the one case where there is
                // nothing honest to redirect TO, and it is a content defect worth naming.
                FlowTrace.Warn("Tutorial", $"coach :: step '{_step.Id}' cannot redirect a builder-confusion open - " +
                    "the step authors NO objective text, so there is nothing honest to say. The step teaches " +
                    "nothing while stranded (tutorial-steps.json).");
                return;
            }

            // ASCII only, and the meaning never rides on hue: "not this step" is carried by the
            // words, and the cue words point at motion/luminance ("glowing"), never at a colour.
            string msg = "Not this step yet - " + objective;

            if (onCloseEdge)
            {
                Guard.Try("Tutorial", "builder redirect guide line",
                    () => GuideLineUi.Show(TutorialGuide.DisplayName, msg, 5f));
                ReassertSpotlight();
                FlowTrace.Step("Tutorial", $"coach :: step '{_step.Id}' builder-confusion REDIRECT delivered on the " +
                    "close edge (guide line + spotlight re-asserted) - WO-1238.");
                return;
            }

            Guard.Try("Tutorial", "builder redirect toast", () =>
                ElarionUiKit.ShowToast(msg, ElarionUiKit.ToastTone.Gold, 4.5f, CoachRedirectSortingOrder));
            FlowTrace.Step("Tutorial", $"coach :: step '{_step.Id}' builder-confusion REDIRECT raised over the build " +
                $"UI (sortingOrder {CoachRedirectSortingOrder}) - WO-1238.");
        }

        /// <summary>Re-show the first authored highlight from the top of its rotation. A glow the
        /// player scrolled past is worth re-showing whenever we speak.</summary>
        private void ReassertSpotlight()
        {
            if (_highlightIds.Count == 0) return;
            _highlightIndex = 0;
            _nextHighlightAt = Time.unscaledTime + HighlightCycleSeconds;
            ShowHighlight(_highlightIds[0], MaskStyleForCurrentStep());
        }

        /// <summary>
        /// WO-1238 §1 — the beat's WORDS escalate, not just its timing. Repeating one sentence
        /// three times is not coaching; the measured data says a player still awaiting at the
        /// second beat is lost, so the second and third beats say HOW, derived from the step's own
        /// completion signal rather than from per-step authored copy (which does not exist and
        /// would go stale the moment a step's signal changed).
        /// ASCII only; every cue reads by motion or luminance, never by hue (owner is red/green
        /// colour-blind).
        /// </summary>
        private string CoachMessageForBeat(int beat, string objective)
        {
            if (beat <= 1) return objective;   // beat 1 restates the ask, verbatim.

            string how = HowHintForAwaitedSignal();
            if (string.IsNullOrEmpty(how)) return objective;
            return string.IsNullOrEmpty(objective) ? how : objective + " - " + how;
        }

        /// <summary>The concrete "how" for the step's completion signal kind. Public + static so
        /// the regression pins every branch without a play session.</summary>
        public static string HowHintForAwaitedSignal(string awaitSignal)
        {
            if (string.IsNullOrEmpty(awaitSignal)) return null;
            if (awaitSignal.StartsWith(TutorialSignals.StructurePlacedPrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(awaitSignal, TutorialSignals.TowerPlaced, StringComparison.OrdinalIgnoreCase))
                return "tap BUILD to open the builder.";
            if (awaitSignal.StartsWith(TutorialSignals.HeroReachedPrefix, StringComparison.OrdinalIgnoreCase))
                return "walk to the glowing marker on your compass.";
            if (awaitSignal.StartsWith(TutorialSignals.DialogueEndedPrefix, StringComparison.OrdinalIgnoreCase))
                return "tap the glowing prompt to continue.";
            if (awaitSignal.StartsWith(TutorialSignals.PanelOpenedPrefix, StringComparison.OrdinalIgnoreCase))
                return "tap the glowing button on the bar.";
            return "follow the glowing marker.";
        }

        private string HowHintForAwaitedSignal() => HowHintForAwaitedSignal(_awaitSignal);

        private void TickCoachNudge()
        {
            if (_step == null) return;

            if (DeNelle.Core.BuildModeState.IsActive)
            {
                if (!_builderOpenedThisStep)
                {
                    _builderOpenedThisStep = true;
                    OnBuilderOpenedDuringStep();
                }
                // While build mode owns the screen there is nothing more to say: the watchdog
                // is paused and the step clock is excluding these frames either way.
                return;
            }

            // WO-1238: the builder just CLOSED after a confusion-open. Deliver the redirect on
            // the channel that cannot be occluded by the build UI, now that the player is back
            // in the world and can actually act on it.
            if (_builderRedirectPending)
            {
                _builderRedirectPending = false;
                DeliverBuilderRedirect(onCloseEdge: true);
            }

            // ⭐ WO-1238: the stand-down is PLACEMENT-ONLY. See the block header.
            if (_builderOpenedThisStep && IsPlacementStep()) return;
            if (_coachBeats >= CoachNudgeMaxBeats) return;
            // WO-1036: the coach cadence rides the SAME played-frame budget as the watchdog.
            // It used to be a wall stamp, so an app-background window burned beats the player
            // never saw — captured proof: "coach :: step 'founding_walk' idle 245s ... (beat 2/4)",
            // i.e. beat 2 of 4 delivered in what the wall clock called four minutes.
            // WO-1238: the due time comes off the LADDER (a fraction of this step's bound), not
            // from "last beat + a flat interval". Stateless, so a suspend or a builder session
            // cannot slide the whole schedule past the rescue.
            if (_stepClock.Charged < CoachBeatDueAt(_coachBeats)) return;

            _coachBeats++;
            _nextCoachAt = CoachBeatDueAt(_coachBeats);

            string objective = _step.Objective != null && !string.IsNullOrEmpty(_step.Objective.Text)
                ? TutorialGuide.ResolveToken(_step.Objective.Text) : null;   // WO-1012 P2 guide token
            string msg = CoachMessageForBeat(_coachBeats, objective);

            if (string.IsNullOrEmpty(msg))
            {
                // No authored objective = nothing honest to say. Report it rather than
                // toasting a placeholder (CLAUDE.md sec.12: no silent failure, no fiction).
                FlowTrace.Warn("Tutorial", $"coach :: step '{_step.Id}' has been idle " +
                    $"{_stepClock.Charged:0}s played (wall {Time.unscaledTime - _stepEnteredAt:0}s) with NO authored objective text - " +
                    "cannot re-state the ask; the step teaches nothing while stranded.");
                _coachBeats = CoachNudgeMaxBeats;   // do not re-check every frame
                return;
            }

            ElarionUiKit.ShowToast(msg, ElarionUiKit.ToastTone.Gold, 3.4f);

            // WO-1238 §2 — MAKE THE ASK FINDABLE, NOT MERELY STATED.
            // The device screenshot from this capture window (break_00_error.png, 14:22:18) shows
            // the objective strip rendered as the SMALLEST, lowest-contrast text on the screen,
            // tucked under an occluding dialogue card and directly above an action bar whose
            // labels are five times its size. Restating the ask on that same channel a third time
            // is not escalation. The FINAL beat therefore also speaks on the guide's own
            // portrait line — an existing, non-blocking, auto-dismissing channel the flow already
            // links against and has never used for coaching.
            if (_coachBeats >= CoachNudgeMaxBeats)
                Guard.Try("Tutorial", "coach final guide line",
                    () => GuideLineUi.Show(TutorialGuide.DisplayName, msg, 5f));

            FlowTrace.Warn("Tutorial", $"coach :: step '{_step.Id}' idle " +
                $"{_stepClock.Charged:0}s played (wall {Time.unscaledTime - _stepEnteredAt:0}s) awaiting '{_awaitSignal}' " +
                $"(builderOpenedThisStep={_builderOpenedThisStep}) - " +
                $"re-stated the objective (beat {_coachBeats}/{CoachNudgeMaxBeats}, due {CoachBeatDueAt(_coachBeats - 1):0}s, " +
                $"next {(_coachBeats < CoachNudgeMaxBeats ? _nextCoachAt.ToString("0") + "s" : "none")}).");

            // Re-assert the spotlight from the top of the walk: a glow the player scrolled
            // past is worth re-showing with the toast.
            ReassertSpotlight();
        }

        // =====================================================================
        //  ROOT CAUSE 2 (F8 seq 632) — emitter/listener id equivalence
        // ---------------------------------------------------------------------
        //  founding_stores awaited "build.structure_placed:lumberyard" and OnSignal matched
        //  it with a strict string.Equals, while the PRE-placement auto-completer
        //  (BaseLayoutSatisfiesBuildSignal) had used the wood-id equivalence helper
        //  (BuildIdMatches) since WO-748. That asymmetry is the defect: the Town palette
        //  ships BOTH "lumberyard" (the Lumberyard storage container) and
        //  "collector_lumbermill" (the card literally labelled Lumbermill, the one that
        //  actually harvests timber). A player who placed the harvester raised
        //  "build.structure_placed:collector_lumbermill", the strict compare said no, and
        //  the step stalled for the full 300s bound with the player having DONE THE THING.
        //  The live signal path now applies the SAME equivalence the auto-completer does.
        // =====================================================================

        /// <summary>True when a RAISED signal satisfies the step's AWAITED signal. Exact
        /// (ordinal-ignore-case) for every signal, plus the wood-id equivalence for
        /// <c>build.structure_placed:&lt;id&gt;</c> — the same <see cref="BuildIdMatches"/>
        /// rule <see cref="BaseLayoutSatisfiesBuildSignal"/> has always used.</summary>
        private static bool SignalSatisfies(string awaited, string raised)
        {
            if (string.IsNullOrEmpty(awaited) || string.IsNullOrEmpty(raised)) return false;
            if (string.Equals(raised, awaited, StringComparison.OrdinalIgnoreCase)) return true;

            if (!awaited.StartsWith(TutorialSignals.StructurePlacedPrefix, StringComparison.OrdinalIgnoreCase) ||
                !raised.StartsWith(TutorialSignals.StructurePlacedPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string wantId = awaited.Substring(TutorialSignals.StructurePlacedPrefix.Length);
            string gotId = raised.Substring(TutorialSignals.StructurePlacedPrefix.Length);
            return BuildIdMatches(gotId, wantId);
        }

        private void OnSignal(string id)
        {
            // Mandatory-step completion.
            if (_phase == Phase.AwaitCompletion && _completionArmed &&
                !string.IsNullOrEmpty(_awaitSignal) &&
                SignalSatisfies(_awaitSignal, id))
            {
                if (!string.Equals(id, _awaitSignal, StringComparison.OrdinalIgnoreCase))
                    FlowTrace.Step("Tutorial", $"step '{_step.Id}' completed by EQUIVALENT signal '{id}' " +
                        $"(awaited '{_awaitSignal}') - same building under a different catalog id " +
                        "(F8 seq 632 root cause 2: the player did the thing, the game now notices).");
                CompleteCurrentStep(skipped: false);
                return;
            }

            // WO-1340 — a live TEACH beat: completion is the AUTHORED gameplay signal.
            if (_activeCtx != null && !string.IsNullOrEmpty(_ctxAwaitSignal) &&
                SignalSatisfies(_ctxAwaitSignal, id))
            {
                FlowTrace.Step("Tutorial", $"CTX-TAUGHT :: {_activeCtx.Id} - '{id}' observed after " +
                    $"{Time.unscaledTime - _ctxEnteredAt:0}s. The player DID the thing, not just read about it.");
                CompleteContextual("complete");
                return;
            }

            // WO-1340 — route hand-off: follow the player along the taught path by re-pointing
            // the spotlight. Presentation only; never completes or holds the beat.
            if (_activeCtx != null) TryAdvanceCtxRoute(id);

            // Contextual completion: the hint's own dialogue ended.
            //
            // ⚠ A TEACH BEAT DELIBERATELY DOES NOT COMPLETE HERE. Closing the text box is the
            // player agreeing to go and do it, not the doing - and it is the very first thing
            // they do, so completing on it is what made the old ctx_talents hint teach nothing.
            // The beat stays live (spotlight following the route) until the real signal lands or
            // the escape bound expires.
            if (_activeCtx != null && string.IsNullOrEmpty(_ctxAwaitSignal) &&
                _activeCtx.Dialogue != null &&
                !string.IsNullOrEmpty(_activeCtx.Dialogue.Intro) &&
                string.Equals(id, TutorialSignals.DialogueEndedPrefix + _activeCtx.Dialogue.Intro,
                              StringComparison.OrdinalIgnoreCase))
            {
                CompleteContextual("complete");
                return;
            }

            // Contextual triggers (armed any time after Start, incl. post-tutorial).
            TryTriggerContextual(id);
        }

        /// <summary>Per-step skip intent. WO-1012 §2b piece 5: NO UI raises this any
        /// more — the banner's inline "Skip &gt;" is retired with the banner, and the
        /// ONE player-facing skip (TutorialSkipUi corner control) routes to
        /// <see cref="SkipAll"/> through its confirm sheet. Kept public for probes /
        /// dev tooling; still honours the authored skippable flag.</summary>
        public void SkipCurrentStep()
        {
            if (_phase != Phase.AwaitCompletion || _step == null || !_step.Skippable) return;
            _skips++;
            DeNelle.Core.Analytics.EventTracker.Track("tutorial_step_skip", new
            {
                stepId = _step.Id,
                order = _step.Order,
                seconds = Time.unscaledTime - _stepEnteredAt,
            });
            FlowTrace.Step("Tutorial", $"STEP-SKIP :: {_step.Id}.");
            CompleteCurrentStep(skipped: true);
        }

        /// <summary>
        /// Player-facing SKIP TUTORIAL (owner directive 2026-07-08): completes the ENTIRE
        /// FTUE in one call so a skipper ends in the SAME state as a completer — NOT half-
        /// granted. It (a) applies every mandatory step's cumulative essential GRANTS
        /// (grant.prepaidTower = +150 crystals; WO-702 grant.starterPet = the founding
        /// Hollow's pet — both idempotent per save), (b) marks every step
        /// seen so a resume never replays one, then (c) reuses the SINGLE completion path
        /// <see cref="FinishFlow"/> — which sets Onboarded (GameStateService.FinishOnboarding),
        /// tears down the spotlight/banner/pressure-hold, and kicks the normal town wave loop.
        /// Step-scoped combat drivers (scripted town wave, staged rep) + any live contextual
        /// hint are disarmed first so nothing fires after skip. Idempotent — a second call
        /// once <see cref="Phase.Finished"/> is a no-op.
        /// </summary>
        public void SkipAll()
        {
            if (_phase == Phase.Finished) return;

            FlowTrace.Step("Tutorial", $"SKIP-ALL :: requested at step '{(_step != null ? _step.Id : "<none>")}' " +
                $"(index {_index}/{(_steps != null ? _steps.Count : 0)}) — completing the whole FTUE.");

            // Disarm the current step's completion latch + every step-scoped combat driver so a
            // late scripted-wave clear / still-pending rep stage can never fire after the skip.
            _completionArmed = false;
            _townWaveArmed = false;
            _stagedRepPending = false;

            // Dismiss any live contextual hint (clears its spotlight) so nothing lingers.
            if (_activeCtx != null) CompleteContextual("skip");

            // Apply EVERY mandatory step's cumulative essential grants + mark each step seen, so
            // the skipper ends in the SAME state as a completer. ApplyPrepaidTowerGrant persists a
            // grant flag BEFORE crediting (idempotent per save — no double-grant); already-seen
            // steps are skipped for the seen-mark but their grant is still reconciled idempotently.
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (_steps != null)
            {
                foreach (var step in _steps)
                {
                    if (step == null || string.IsNullOrEmpty(step.Id)) continue;
                    if (step.Grant != null && step.Grant.PrepaidTower)
                        ApplyPrepaidTowerGrant(step);   // idempotent: no-op if already granted this save
                    // WO-1012 P2: the ARRIVE-beat starter-pet grant is essential too — a
                    // skipper ends with the pet like a completer. Same idempotent key.
                    if (step.Grant != null && step.Grant.StarterPet)
                        ApplyStarterPetGrant(step);
                    bool alreadySeen = state != null && state.SeenTutorials != null &&
                        state.SeenTutorials.TryGetValue(SeenPrefix + step.Id, out bool seen) && seen;
                    if (!alreadySeen) svc?.MarkTutorialSeen(SeenPrefix + step.Id);
                }
            }

            // Owner 2026-07-10 felt-bug ("cancelled tutorial but it restarts on the Build button"):
            // an explicit Skip must ALSO silence the CONTEXTUAL one-shot hints. ctx_first_spend fires
            // on economy.can_afford_upgrade — which THIS skip's own crystal grant guarantees — and
            // spotlights hud.build_button, so opening Build after a cancel resurfaced a guide hint that
            // reads as "the tutorial restarted". Mark every one-shot ctx seen (same persistence CtxSeen
            // checks) so a skipper gets NO further tutorial content; completers still receive the hints.
            if (_contextual != null)
                foreach (var ctx in _contextual)
                    if (ctx != null && !string.IsNullOrEmpty(ctx.Id) && ctx.OneShot)
                        svc?.MarkTutorialSeen(CtxSeenPrefix + ctx.Id);

            DeNelle.Core.Analytics.EventTracker.Track("tutorial_skipped_all", new
            {
                fromStep = _step != null ? _step.Id : null,
                index = _index,
                seconds = Time.unscaledTime - _flowStartedAt,
            });
            FlowTrace.Step("Tutorial", "SKIP-ALL :: grants applied + all steps marked seen — finishing flow " +
                "(Onboarded set, spotlight/banner/pressure-hold torn down, town loop kicked).");

            // Reuse the SINGLE completion path — do NOT invent a divergent finisher.
            _index = _steps != null ? _steps.Count : 0;
            _step = null;
            FinishFlow();
        }

        private void CompleteCurrentStep(bool skipped)
        {
            _completionArmed = false;
            // Disarm the step-scoped combat drivers so a late scripted-wave clear or a
            // still-pending rep stage can never fire into a LATER step.
            _townWaveArmed = false;
            _stagedRepPending = false;
            var step = _step;

            // WO-1012 §2b: a genuinely-completed gesture beat fades its ghost-finger
            // replay PERMANENTLY (the player has proven the drag). Computed here while
            // _awaitSignal still belongs to the completing step.
            if (!skipped)
            {
                string gestureCard = PlacementCardHighlightId();
                if (gestureCard != null) GuidePointer.NotifyGestureSuccess(gestureCard);
            }

            if (!skipped)
            {
                // WO-1238 §1: the wall duration on this line IS the distribution the ladder was
                // derived from (n=156 completions across logs/ + tmp/f8pull). It is now joined by
                // the PLAYED duration and the beats spent, so the next derivation can ask the
                // question this one could not: does coaching change whether a step completes?
                // Without these two fields a coached success and an uncoached one are the same
                // line, and the ladder can only ever be re-tuned by guessing again.
                FlowTrace.Step("Tutorial", $"STEP-COMPLETE :: {step.Id} " +
                    $"({Time.unscaledTime - _stepEnteredAt:0.0}s, played {_stepClock.Charged:0.0}s, " +
                    $"coachBeats={_coachBeats}, builderOpened={_builderOpenedThisStep}).");
                DeNelle.Core.Analytics.EventTracker.Track("tutorial_step_complete", new
                {
                    stepId = step.Id,
                    order = step.Order,
                    seconds = Time.unscaledTime - _stepEnteredAt,
                    secondsPlayed = _stepClock.Charged,
                    coachBeats = _coachBeats,
                    builderOpened = _builderOpenedThisStep,
                });
            }

            GameStateService.Instance?.MarkTutorialSeen(SeenPrefix + step.Id);

            // The old WO-702 COMPLETION-side starter-pet grant is GONE from here:
            // WO-1012 P2 moved it ENTER-side (EnterStep) — the guide IS the pet-Echo
            // and must exist before the beat speaks. Skip/rescue paths still end fully
            // granted: EnterStep ran before any completion, and SkipAll/skipIfPrebuilt/
            // resume all reconcile grants idempotently.

            // WO-1012 P2: end the guide-lead the moment the beat completes — the leash
            // resumes natural exploration (safe no-op when no lead was active).
            bool wasLeading = DeNelle.Pets.PetHeroLeash.IsLeading;
            DeNelle.Pets.PetHeroLeash.ClearLeadTarget();

            // WO-1108 Lane B — ARRIVAL AND VANISH ARE THE SAME EVENT. Owner, verbatim:
            // "it takes you to the gate, gives you your dialogue, then it disappears."
            // Deliberately hung on the EXISTING lead-clear point rather than on a step id:
            // a lead was in force means THIS was the escort beat (the only lead beat the
            // data authors — tutorial-steps.json has exactly one hero.reached completion),
            // so the two can never disagree about when the escort ended. Fires on a skipped
            // /watchdog-rescued beat too: the escort is over either way, and a body left
            // standing there is the defect. The appearance owner is EchoWorldPresence —
            // this line does not decide when the Echo comes back.
            if (wasLeading)
                DeNelle.Village.World.Camps.EchoWorldPresence.NotifyEscortComplete(
                    $"tutorial step '{step.Id}' {(skipped ? "skipped" : "complete")}");

            // WO-962: the anchor latch dies WITH the step (completed OR watchdog-skipped),
            // so a re-entered step resolves once again instead of inheriting a stale target.
            TutorialWorldAnchors.ClearLatch($"step '{step.Id}' {(skipped ? "skipped" : "complete")}");

            _highlightIds.Clear();   // ROOT CAUSE 3: never rotate a dead step's walk
            _highlightIndex = 0;
            HideHighlight();
            GuideLineUi.Hide();      // WO-1012: a guide one-liner never outlives its beat
            ObjectiveStripUi.Hide();
            PressureHeld = false;

            // Outro (the guide reacts) — plays over the transition; never gates the chain.
            // A SKIPPED step (player skip OR a watchdog rescue) never plays one: the outro
            // is the guide reacting to a thing the player did, so playing it for a step that
            // did not happen narrates a fiction (F8 seq 632 root cause 4).
            if (!skipped && step.Dialogue != null && !string.IsNullOrEmpty(step.Dialogue.Outro))
            {
                if (!CoreDialogue.DialogueService.Play(step.Dialogue.Outro))
                    FlowTrace.Warn("Tutorial", $"step '{step.Id}' outro dialogue '{step.Dialogue.Outro}' unknown — skipped.");
            }

            AdvanceToNextStep();
        }

        private void FinishFlow()
        {
            _phase = Phase.Finished;
            _step = null;
            HideHighlight();
            GuideLineUi.Hide();
            ObjectiveStripUi.Hide();
            TutorialSkipUi.Hide();   // the ONE skip leaves with the flow
            DeNelle.Pets.PetHeroLeash.ClearLeadTarget();   // WO-1012 P2: no lead outlives the flow

            // WO-1108 Lane B: no guide BODY outlives the flow either. SkipAll routes here
            // (it reuses FinishFlow), so a player who skips at beat 1 — before any lead was
            // ever asserted, i.e. before the CompleteCurrentStep vanish could fire — must
            // not be left with a wolf standing in the town forever. Unconditional and
            // idempotent: NotifyEscortComplete only sweeps when there is something to sweep.
            DeNelle.Village.World.Camps.EchoWorldPresence.NotifyEscortComplete("FTUE flow finished");

            TutorialWorldAnchors.ClearLatch("flow finished");   // WO-962: no latch outlives the flow
            PressureHeld = false;

            DeNelle.Core.Analytics.EventTracker.Track("tutorial_completed", new
            {
                totalSeconds = Time.unscaledTime - _flowStartedAt,
                skips = _skips,
            });
            FlowTrace.Step("Tutorial", $"flow COMPLETE ({Time.unscaledTime - _flowStartedAt:0.0}s, {_skips} skips).");

            // The SINGLE V2-path finisher (spec §2.1c): mark onboarded + kick the loop —
            // the same handoff the legacy director performs (TutorialDirector.SkipToGameplay).
            GameStateService.Instance?.FinishOnboarding();
            DeNelle.Village.Monetization.DailyChestController.NotifyTutorialFinished();
            if (_wave == null) _wave = FindAnyObjectByType<WaveManager>();
            _wave?.BeginLoop().Forget();
        }

        // =====================================================================
        //  WO-T3 — grant.prepaidTower ("this first one is on me")
        // =====================================================================

        /// <summary>
        /// Credits one watchtower's crystal cost through the SAME store BuildMenu
        /// charges (GameStateService.AddCrystals -> GameState.Resources.Crystals,
        /// BuildMenu.OnConfirmBuild BuildMenu.cs:403). Idempotent PER SAVE: the
        /// grant persists a SeenTutorials flag (the same mechanism step completion
        /// uses) BEFORE crediting, so a re-entered/replayed step never re-grants.
        /// </summary>
        private void ApplyPrepaidTowerGrant(TutorialStepDef step)
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (svc == null || state == null)
            {
                FlowTrace.Warn("Tutorial", $"step '{step.Id}' grant.prepaidTower — GameStateService unavailable, grant skipped (player pays from the starter wallet).");
                return;
            }

            string key = GrantSeenPrefix + step.Id;
            if (state.SeenTutorials != null &&
                state.SeenTutorials.TryGetValue(key, out bool granted) && granted)
            {
                FlowTrace.Step("Tutorial", $"step '{step.Id}' grant.prepaidTower already granted on this save — not re-granted.");
                return;
            }

            svc.MarkTutorialSeen(key);               // persist FIRST — a re-entry can never double-pay
            svc.AddCrystals(PrepaidTowerCrystals);   // clamps, persists, raises ResourcesChanged (HUD + BuildMenu re-render)
            FlowTrace.Step("Tutorial", $"step '{step.Id}' grant.prepaidTower APPLIED: +{PrepaidTowerCrystals} crystals " +
                $"(one watchtower — BuildMenu default variant cost) via GameStateService.AddCrystals; balance now {state.Resources.Crystals}.");
        }

        // =====================================================================
        //  WO-702 — grant.starterPet (the Echo Hollow's reward: the pet emerges)
        // =====================================================================

        /// <summary>Starter pet species the founding-arc grant applies -- "ice-wolf".
        ///
        /// ⚠ THREE OWNER RULINGS LIVE ON THIS ONE CONSTANT. None is deleted, because each was
        /// right when it was made and the reasons are what a future session needs:
        ///   1. 2026-07-16 -- "aether-sprite, the ETHEREAL SPIRIT, NOT the quadruped ice-wolf
        ///      that T-posed." The T-pose was real, and its cause is now known: the pets are
        ///      AccuRig CC_Base HUMAN bipeds IMPORTED AS GENERIC (animationType 2, avatarSetup 0),
        ///      so Unity built no avatar and nothing retargeted. And the "ice-wolf" asset was not
        ///      even a wolf -- object "fox", mesh Coyote_Mesh, a coyote skinned to a human
        ///      skeleton. Both halves of that ruling were sound.
        ///   2. 2026-07-17 -- "Echoes are portrait-card spirits, NOT 3D models." See the birth
        ///      site below; that ruling STILL STANDS for the roster and is deliberately NOT
        ///      reversed here.
        ///   3. 2026-08-10 -- "we should have Ice wolf" / "owners decision to switch", reaffirmed.
        ///      What changed materially: a real QUADRUPED wolf rig with five baked clips
        ///      (idle2 / running / sniffing / default / fallen) now ships in-tree, so ruling 1's
        ///      technical objection is GONE -- there is nothing left to T-pose. The body loads
        ///      from Resources/Pets/ice-wolf.prefab with Resources/Pets/ice-wolf.controller,
        ///      which is PetDeployer's FIRST controller probe.
        ///
        /// Canon agrees: the guide's identity is "Aldwin, the Ice Echo" wearing the
        /// Echoes/Portraits/Frosthowl portrait, and Frosthowl IS the ice wolf -- so the soul the
        /// unlock card names and the body the player follows are finally the same animal.
        /// (WO-961.)</summary>
        /// <remarks>
        /// RETIRED 2026-07-16 REASONING, kept verbatim so ruling 1 survives its own reversal:
        /// "aether-sprite", the ETHEREAL SPIRIT (owner call 2026-07-16: the founding Echo must
        /// read as an ethereal spirit, NOT the quadruped ice-wolf that T-posed). Of the three
        /// starter models this is the only ethereal/spirit one (element "aether", archetype
        /// "Heart-Ward", fairy/sprite body) and the only HUMANOID rig (AccuRig CC_Base_*
        /// skeleton) -- so a humanoid idle controller dropped at
        /// Resources/Pets/aether-sprite.controller (or the shared Resources/Pets/PetIdle.controller)
        /// binds via PetDeployer.WirePetAnimator and settles it out of the bind pose. The
        /// floating-spirit hover/drift/aura (EchoSpiritPresentation) reads ethereal even before
        /// that idle exists. PetSelect is bypassed under ff.bypasspetselect, so this default is
        /// what the founding Echo becomes.
        /// CORRECTION to that text, verified at source 2026-08-10: "the ONLY humanoid rig" is
        /// wrong on the word ONLY -- ice-wolf.fbx carried the identical CC_Base skeleton. Both
        /// were imported Generic, which is why neither ever had an avatar.
        /// </remarks>
        private const string StarterPetSpecies = "ice-wolf";

        /// <summary>
        /// Grants the starter pet — since WO-1012 P2 (owner re-ruling 2026-08-09) on
        /// ENTER of the ARRIVE beat (founding_greet): the pet-Echo IS the guide and
        /// wakes near the Heart before it speaks. (History: WO-702 granted it on
        /// COMPLETION of founding_hollow — reward-follows-placement — until the
        /// guide re-ruling moved it.) Three moves, each self-reporting:
        ///   1. record <c>GameState.StarterPetId</c> (the SAME field the old PetSelect
        ///      confirm wrote — SyncSlotsFromState restores slot 0 from it on reload),
        ///   2. VISIBLE BIRTH (owner refinement 2026-07-13): the pet's body emerges AT
        ///      the placed Echo Hollow via PetDeployer.SummonAt (the WO-360 one-summon
        ///      path; PetHeroLeash then walks it to the hero) — Guard-wrapped so a failed
        ///      visual spawn NEVER blocks the grant,
        ///   3. roster grant through the ONE funnel PetAcquisitionService.Acquire
        ///      (GameState.Pets + OwnedPets + slot + Save; idempotent via its Owns check).
        /// Idempotent per save like prepaidTower: the tutorial_v2_grant key persists FIRST.
        /// Order matters: StarterPetId before SummonAt (its owned-gate), SummonAt before
        /// Acquire (so the slot redeploy sees the Hollow-born body and never double-spawns).
        /// </summary>
        private void ApplyStarterPetGrant(TutorialStepDef step)
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (svc == null || state == null)
            {
                FlowTrace.Warn("Tutorial", $"step '{step.Id}' grant.starterPet — GameStateService unavailable, grant skipped.");
                return;
            }

            string key = GrantSeenPrefix + step.Id;
            if (state.SeenTutorials != null &&
                state.SeenTutorials.TryGetValue(key, out bool granted) && granted)
            {
                FlowTrace.Step("Tutorial", $"step '{step.Id}' grant.starterPet already granted on this save — not re-granted.");
                return;
            }
            svc.MarkTutorialSeen(key);   // persist FIRST — a replay can never double-grant (prepaidTower order)

            // 1) StarterPetId — the canonical "pet-<species>" catalog id.
            if (string.IsNullOrEmpty(state.StarterPetId))
            {
                var def = DeNelle.Pets.PetCatalog.FindBySpecies(StarterPetSpecies);
                state.StarterPetId = def != null && !string.IsNullOrEmpty(def.Id)
                    ? def.Id : "pet-" + StarterPetSpecies;
            }

            // 2) THE GUIDE'S BODY — restored 2026-08-10 (WO-961), NARROWLY.
            //
            // HISTORY, both rulings intact: on 2026-07-17 the owner ruled "Echoes are portrait-card
            // spirits, NOT 3D models -- scrap giving them a model", which retired the SummonAt body
            // AND the EchoSpiritPresentation floating-spirit layer. THAT RULING STILL STANDS FOR THE
            // ROSTER: echoes awaken as portrait cards (EchoUnlockDialogue) and live in EchoRosterView.
            // Nothing here re-introduces a menagerie of 3D pets.
            //
            // WHY THE GUIDE IS THE EXCEPTION: WO-1012's beat 2 instructs the player, in words, to
            // "Follow {guide} to the gate". With no body, world.guide fell down its resolution chain
            // to the Sylas steward NPC -- the owner's F8 seq 2304, whose entire message was "npc".
            // A step cannot tell the player to follow something that was ruled out of existence. So
            // exactly ONE Echo -- the founding guide -- gets a world body, and only because a beat
            // points at it.
            //
            // The body is the WO-961 ice wolf (Resources/Pets/ice-wolf.prefab + ice-wolf.controller,
            // a real quadruped rig with its own idle/run clips). EchoSpiritPresentation is
            // deliberately NOT re-enabled: it existed to MASK the sprite's missing idle, and a
            // hovering wolf is wrong.
            //
            // SummonAt reuses a live summon, so this is idempotent across a re-entered beat. The
            // DATA grant below (StarterPetId + roster Acquire) is UNCHANGED -- it was never what
            // broke -- and the abstract EchoService silo/workforce is untouched.
            //
            // NOTE ON THE LOOKUP (corrected 2026-08-10 during implementation): there is NO
            // PetDeployer.Instance -- PetDeployer is a plain MonoBehaviour with no singleton
            // accessor (see Assets/_Modules/Pets/PetDeployer.cs:29). Resolving it that way does not
            // compile, so this uses the SAME self-heal every other caller uses
            // (DialogueCommandSink.EnsurePetDeployer / EchoAutoDeployTrigger.EnsurePetDeployer):
            // find one, and build a configured one if the scene ships none. The hub CAN ship
            // without a deployer, and a null here is exactly the silent no-body this ticket exists
            // to kill.
            // WO-1108 Lane B: the summon is ROUTED THROUGH THE SINGLE APPEARANCE OWNER
            // (EchoWorldPresence) instead of calling PetDeployer.SummonAt here. Nothing
            // about the beat changes — EchoWorldPresence uses the same SummonAt path and
            // the same EnsurePetDeployer self-heal — but all THREE appearance transitions
            // (escort summon / vanish on arrival / the one post-battle reappearance) now
            // read from one owner and one trace stream, which is what stops a second seam
            // (the retired WO-360 outpost summon) from growing back.
            {
                Vector3 birthPos = transform != null ? transform.position : Vector3.zero;
                if (TutorialWorldAnchors.TryResolveAnchor("guide_anchor", out Vector3 anchorPos)) birthPos = anchorPos;

                bool bodyBorn = DeNelle.Village.World.Camps.EchoWorldPresence.SummonEscortBody(
                    birthPos, $"tutorial step '{step.Id}' grant.starterPet");

                if (bodyBorn)
                    FlowTrace.Step("Tutorial", $"step '{step.Id}' grant.starterPet — guide BODY summoned ('{StarterPetSpecies}') at {birthPos} " +
                        "(WO-961: exactly one Echo has a world body, because a beat says to follow it; the roster stays portrait cards). " +
                        "WO-1108: it VANISHES when the gate beat completes and returns once, after the first battle.");
                else
                    FlowTrace.Warn("Tutorial", $"step '{step.Id}' grant.starterPet — guide body NOT summoned (species '{StarterPetSpecies}'); " +
                        "'Follow {guide}' will resolve to the steward stand-in. Check Resources/Pets/ice-wolf.prefab resolves.");
            }

            // 3) Roster grant (the single funnel — Acquire saves, covering StarterPetId too).
            var petSvc = DeNelle.Pets.PetAcquisitionService.Instance;
            if (petSvc == null)
            {
                FlowTrace.Warn("Tutorial", $"step '{step.Id}' grant.starterPet — PetAcquisitionService.Instance is null; StarterPetId recorded, roster entry deferred.");
                svc.Save();   // still persist StarterPetId + the grant key
                return;
            }
            bool acquiredNew = false;
            Guard.Try("Tutorial", "starter-pet Acquire", () =>
                acquiredNew = petSvc.Acquire(StarterPetSpecies, DeNelle.Pets.PetAcquisitionSource.Starter));
            FlowTrace.Step("Tutorial", $"step '{step.Id}' grant.starterPet APPLIED: '{StarterPetSpecies}' " +
                $"(acquiredNew={acquiredNew}, StarterPetId='{state.StarterPetId}', roster={(state.Pets != null ? state.Pets.Count : 0)} pet(s)).");
        }

        // ResolveHollowPosition() REMOVED (F8 seq 632 sweep, 2026-08-02): it existed only to
        // anchor the PetDeployer.SummonAt "visible birth" of the founding Echo at the placed
        // Hollow. That birth was SCRAPPED on 2026-07-17 (echoes are portrait cards, not 3D
        // models), leaving this method with ZERO callers. Dead code in a flow this load-bearing
        // reads as a live path and mis-teaches the next reader; it is gone. The Hollow BUILDING
        // itself is untouched and still real.
        //
        // STILL GONE after WO-961 (2026-08-10), and deliberately: the guide's body is BACK (see
        // ApplyStarterPetGrant step 2) but it is NOT born at the Hollow any more. WO-1012 P2 moved
        // the grant to the ENTER of the ARRIVE beat, where the guide wakes AT THE HEART, before any
        // Hollow has been placed. The birth position now comes from the "guide_anchor" resolver
        // (TutorialWorldAnchors:218/243), which is the same anchor the beat's own copy points the
        // player at -- so the body and the objective can never disagree about where the guide is.
        // Do NOT resurrect a Hollow-relative position; it would put the body where the beat isn't.

        // EnsureGuidePetDeployer() REMOVED (WO-1108 Lane B, 2026-08-16) — it was the FOURTH
        // spelling of the same PetDeployer self-heal (DialogueCommandSink, EchoAutoDeployTrigger,
        // this one), and its own doc comment already flagged that. Its single caller,
        // ApplyStarterPetGrant, now goes through EchoWorldPresence.SummonEscortBody, which
        // reuses EchoAutoDeployTrigger.EnsurePetDeployer. One appearance owner, one self-heal.
        // Do NOT re-add a local copy: a private deployer here is how a second body-spawning
        // seam gets built without the appearance owner ever knowing a body exists.

        // =====================================================================
        //  WO-T4 — scripted town wave (spec step 4: horn blast, no Start-Wave press)
        // =====================================================================

        private void StartScriptedTownWave(TutorialStepDef step)
        {
            _townWaveArmed = false;
            _townWaveSpawnSettled = false;
            _townWaveForcedClear = false;   // WO-1300: a fresh arm never inherits a forced settle

            if (_wave == null) _wave = FindAnyObjectByType<WaveManager>();
            var gate = NearestGateToHero();
            if (_tutorialWave == null)
            {
                _tutorialWave = FindAnyObjectByType<TutorialWaveSpawner>();
                if (_tutorialWave == null) _tutorialWave = gameObject.AddComponent<TutorialWaveSpawner>();
            }
            _tutorialWave.SetWaveManager(_wave);

            _townWaveArmed = true;
            FlowTrace.Step("Tutorial", $"step '{step.Id}' scripted town wave: SpawnAt({(gate != null ? gate.SpawnId : "NULL-gate")}, {TownWaveCount}) " +
                "via TutorialWaveSpawner (wave loop stays closed — the !Onboarded gate holds).");
            RunScriptedTownWave(gate).Forget();
        }

        /// <summary>WO-1300: how long (in PLAYED seconds) the pre-fight dialogue may hold the
        /// scripted band back before the arm proceeds anyway. The intro line is a BUFFER, not a
        /// gate; a dialogue that never ends must not silently turn it into one. Deliberately far
        /// under the watchdog bound so the band still has time to be fought and repelled - this
        /// is NOT a watchdog change (WO-1300 forbids touching that bound).</summary>
        private const float TownWaveDialogueHoldBoundSeconds = 30f;

        /// <summary>WO-1300: cadence (PLAYED seconds) of the scripted-band arm trace.</summary>
        private const float TownWaveTraceSeconds = 5f;

        private async UniTaskVoid RunScriptedTownWave(WaveSpawnPoint gate)
        {
            // ── WO-1300: THE ARM MUST NEVER GO QUIET, AND MUST NEVER WEDGE ────────────────
            // This is a fire-and-forget UniTaskVoid. Before WO-1300 its whole await chain was
            // UNGUARDED, so there were two ways for founding_defend to burn the full 120s
            // watchdog and be rescued-as-SKIPPED without ONE [Flow:Tutorial] line saying why:
            //   1. SpawnAt THREW (it awaits WaveManager.GetEnemyCatalogAsync -> WaveDataLoader,
            //      which can fault) - the exception surfaced as an unobserved-task log with no
            //      tutorial context, _townWaveSpawnSettled stayed false, and TickScriptedWave
            //      never armed. The step then awaited a signal whose ONLY publisher was never
            //      going to run.
            //   2. The dialogue-hold loop below never released, so the band never spawned.
            // Both now report themselves and both now fall back to the spawner's OWN documented
            // "proceed, don't wedge" contract, so the beat completes instead of being silently
            // skipped. PERMANENT instrumentation (CLAUDE.md sec.12) - never strip.
            try
            {
                // EnterStep arms this self-driving wave before it presents the step intro.
                // Yield once so the dialogue can open, then treat that line as the authored
                // pre-fight buffer. Spawning while Dialogue still owns Modal posture hides
                // the combat HUD even though the enemies are already attacking.
                await UniTask.Yield();

                float nextTraceAt = TownWaveTraceSeconds;
                while (_townWaveArmed && CoreDialogue.DialogueService.IsRunning)
                {
                    if (_stepClock.Charged >= nextTraceAt)
                    {
                        nextTraceAt = _stepClock.Charged + TownWaveTraceSeconds;
                        FlowTrace.Warn("Tutorial",
                            $"scripted band HELD by the pre-fight dialogue for {_stepClock.Charged:0}s played " +
                            $"(bound {TownWaveDialogueHoldBoundSeconds:0}s). Nothing has spawned yet, so " +
                            $"'{TutorialSignals.TutorialBandRepelled}' cannot be raised while this holds.");
                    }
                    if (_stepClock.Charged >= TownWaveDialogueHoldBoundSeconds)
                    {
                        FlowTrace.Warn("Tutorial",
                            $"scripted band PROCEEDING despite an open dialogue - the pre-fight line has held for " +
                            $"{_stepClock.Charged:0}s played, past the {TownWaveDialogueHoldBoundSeconds:0}s buffer bound. " +
                            "The intro is a buffer, not a gate; holding forever is how this beat used to be " +
                            "rescued-and-SKIPPED with no fiction narrated (WO-1300).");
                        break;
                    }
                    await UniTask.Yield();
                }
                if (!_townWaveArmed) return;
                if (_tutorialWave == null)
                {
                    FlowTrace.Fail("Tutorial", "scripted band arm found NO TutorialWaveSpawner - nothing can spawn.");
                    SettleScriptedWaveWithoutBand("no TutorialWaveSpawner");
                    return;
                }

                // SpawnAt awaits the enemy catalog before any enemy exists; IsCleared would
                // read true (spawn-requested, none live) during that await — so the clear
                // poll (TickScriptedWave) only arms once the spawn has actually settled.
                await _tutorialWave.SpawnAt(gate, TownWaveCount);
                _townWaveSpawnSettled = true;
                FlowTrace.Step("Tutorial",
                    $"scripted band spawn SETTLED - the clear poll is armed and will raise " +
                    $"'{TutorialSignals.TutorialBandRepelled}' when the last body dies.");
            }
            catch (System.Exception ex)
            {
                // A THROWN arm is the silent-stuck case. Say so, then settle the poll anyway:
                // TutorialWaveSpawner's own contract is "a skipped spawn reads IsCleared=true so
                // the tutorial proceeds rather than wedging" - honour it here too. The signal is
                // still raised by the ONE publisher (TickScriptedWave), never from this catch.
                FlowTrace.Fail("Tutorial",
                    $"scripted band arm THREW before the spawn settled: {ex.GetType().Name}: {ex.Message}. " +
                    $"Awaiting '{TutorialSignals.TutorialBandRepelled}' would never be satisfied, so the beat is " +
                    "settled as an empty band (the player is not held at a fight that cannot start).");
                SettleScriptedWaveWithoutBand("the arm threw");
            }
        }

        /// <summary>
        /// WO-1300: let the clear poll arm with NO live band, so the step completes down its
        /// normal path instead of stranding until the watchdog rescues-and-SKIPS it. Reuses
        /// <see cref="TutorialWaveSpawner"/>'s documented proceed-don't-wedge contract rather
        /// than raising the completion signal here - <see cref="TickScriptedWave"/> stays the
        /// ONE publisher of 'wave.tutorial_band_repelled'.
        /// </summary>
        private void SettleScriptedWaveWithoutBand(string reason)
        {
            if (!_townWaveArmed) return;
            if (_tutorialWave != null) _tutorialWave.MarkClearedWithoutBand(reason);
            _townWaveForcedClear  = true;
            _townWaveSpawnSettled = true;
        }

        /// <summary>WO-1300: set when the scripted band could not be armed at all, so
        /// <see cref="TickScriptedWave"/> may complete the beat without a live band.</summary>
        private bool _townWaveForcedClear;

        /// <summary>
        /// The scripted wave bypasses WaveManager's wave loop (TutorialWaveSpawner owns
        /// the spawned enemies' lifecycle; WaveManager.OnWaveCleared never fires for it),
        /// so the flow polls the spawner's own all-dead check and raises the bus signal
        /// itself. A skipped spawn (no WaveManager / no gate / empty catalog) reads
        /// IsCleared=true by the spawner's proceed-don't-wedge contract — the step then
        /// completes rather than stranding (the spawner already logged the warning).
        /// </summary>
        private void TickScriptedWave()
        {
            if (!_townWaveArmed || !_townWaveSpawnSettled) return;
            // WO-1300: a forced settle (the arm threw / no spawner) reads as cleared here, so a
            // band that could never spawn completes the beat instead of stranding it. The
            // spawner's own IsCleared still governs every normal run.
            if (!_townWaveForcedClear && (_tutorialWave == null || !_tutorialWave.IsCleared)) return;
            _townWaveForcedClear = false;
            _townWaveArmed = false;
            // WO-1012 P3: raise the id the LIVE step awaits — the arc's ENEMIES beat
            // completes on the band-scoped 'wave.tutorial_band_repelled' (so an ambient
            // clear can never satisfy it); a legacy wave.cleared step still gets its id.
            bool bandBeat = string.Equals(_awaitSignal, TutorialSignals.TutorialBandRepelled, StringComparison.OrdinalIgnoreCase);
            FlowTrace.Step("Tutorial", "scripted town wave CLEARED (all tutorial enemies dead) — raising '" +
                (bandBeat ? TutorialSignals.TutorialBandRepelled : TutorialSignals.WaveCleared) + "'.");
            TutorialSignals.Raise(bandBeat ? TutorialSignals.TutorialBandRepelled : TutorialSignals.WaveCleared);
        }

        /// <summary>Nearest wave gate to the hero — the same nearest-gate rule the legacy
        /// director and TutorialWorldAnchors.ResolveNearestGate use ("the gate the tower covers").</summary>
        private WaveSpawnPoint NearestGateToHero()
        {
            if (_hero == null) _hero = FindAnyObjectByType<HeroLocomotion>();
            Vector3 from = _hero != null ? _hero.transform.position : Vector3.zero;

            WaveSpawnPoint best = null;
            float bestSqr = float.MaxValue;
            foreach (var p in FindObjectsByType<WaveSpawnPoint>(FindObjectsSortMode.None))
            {
                if (p == null) continue;
                float sqr = (p.transform.position - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            return best;
        }

        // =====================================================================
        //  WO-T4 — staged world encounter (spec step 5: one guaranteed rep)
        // =====================================================================

        /// <summary>
        /// Once the hero has crossed into an OuterWorld roster region, stages ONE rep at
        /// a guaranteed near anchor (10-16m ahead, navmesh-snapped, path-complete) through
        /// the SAME factory chain OverworldEncounterSpawner.SpawnRep drives — EnemyFactory
        /// .Build -> Enemy.Configure -> RepEngageWatcher.Init (def values mirror SpawnRep,
        /// OverworldEncounterSpawner.cs:313-343; migrate both onto the spec's fixed-anchor
        /// SpawnRep overload when that lands, spec §2.6). Retries at 1 Hz until an anchor
        /// resolves; never wedges — the STEP-STUCK watchdog still covers abandonment.
        /// </summary>
        private void TickStagedEncounter()
        {
            if (!_stagedRepPending || _stagedRepDone) return;
            if (Time.unscaledTime < _nextStageProbeAt) return;
            _nextStageProbeAt = Time.unscaledTime + 1f;

            if (!FeatureFlags.OverworldEncounter)
            {
                // RepEngageWatcher is inert while the flag is off — a staged rep could
                // never engage. Stand down loudly; arena.resolved:win must then come
                // from the player finding a fight by other means (or the watchdog reports).
                _stagedRepDone = true;
                FlowTrace.Warn("Tutorial", "staged encounter SKIPPED: ff.overworldencounter is OFF — no rep can engage; step relies on the watchdog.");
                return;
            }
            if (!OverworldEncounterSpawner.OuterWorldLoaded()) return;   // world not streamed in yet
            if (_hero == null) { _hero = FindAnyObjectByType<HeroLocomotion>(); if (_hero == null) return; }

            Vector3 heroPos = _hero.transform.position;
            bool inOuter = false;
            Guard.Try("Tutorial", "staged-rep zone check", () => inOuter =
                DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(heroPos)));
            if (!inOuter) return;   // hero still castle-side — wait for the crossing

            // Anchor: ahead of the hero, just outside the rep's 8m aggro ring, on the
            // navmesh, in an OuterWorld roster zone, with a COMPLETE path from the hero
            // (the same candidate gates SpawnRep applies — guaranteed reachable).
            Vector3 anchor = Vector3.zero;
            bool anchorFound = false;
            var path = new UnityEngine.AI.NavMeshPath();
            Vector3 fwd = _hero.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            for (int attempt = 0; attempt < 8 && !anchorFound; attempt++)
            {
                float yaw = UnityEngine.Random.Range(-40f, 40f);
                float dist = UnityEngine.Random.Range(StagedRepMinDistance, StagedRepMaxDistance);
                Vector3 cand = heroPos + Quaternion.Euler(0f, yaw, 0f) * fwd * dist;
                if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                bool candInOuter = false;
                Guard.Try("Tutorial", "staged-rep anchor zone gate", () => candInOuter =
                    DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(hit.position)));
                if (!candInOuter) continue;
                if (UnityEngine.AI.NavMesh.CalculatePath(heroPos, hit.position, UnityEngine.AI.NavMesh.AllAreas, path)
                    && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                { anchor = hit.position; anchorFound = true; }
            }
            if (!anchorFound) return;   // retry next tick — the hero keeps walking, candidates change

            // Def mirrors OverworldEncounterSpawner.SpawnRep's rep exactly (owner-tuned
            // 2026-07-01 values): field-killable hook, zero contact damage, +5% chase.
            var def = new EnemyDef
            {
                Id = "orc-warrior", Name = "Orc Warleader", DisplayName = "Orc Warband", Ai = "walker",
                Hp = 98f, MoveSpeed = 6.3f, ContactDamage = 0f,
                AttackInterval = 1.5f, Height = 2.0f, AggroRadius = 8f,
                XpReward = 42
            };

            Enemy enemy = null;
            Guard.Try("Tutorial", "stage tutorial rep", () =>
            {
                enemy = EnemyFactory.Build(def, anchor, Quaternion.identity, transform);
                if (enemy == null) return;
                enemy.gameObject.name = "OrcRep_Tutorial";
                enemy.Configure("orc-rep-tutorial", def, null);    // no Heart — tethered hook, not a marcher
                enemy.SetBrainTargetPosition(anchor);              // idle at its anchor until it sees you
                int threat = 1;
                Guard.Try("Tutorial", "staged-rep zone threat", () =>
                    threat = Mathf.Max(1, DeNelle.Core.World.ZoneManager.ThreatLevel(anchor)));
                enemy.gameObject.AddComponent<RepEngageWatcher>()
                     .Init(new[] { "orc-warrior", "orc-tank", "orc-mage" }, threat);   // the SpawnRep OrcFamily
            });

            _stagedRepDone = true;
            if (enemy != null)
                FlowTrace.Step("Tutorial", $"staged tutorial rep 'OrcRep_Tutorial' at {anchor} ({Vector3.Distance(heroPos, anchor):0.0}m from hero) — engage pops the BattleArena; win raises 'arena.resolved:win'.");
            else
                FlowTrace.Fail("Tutorial", "staged tutorial rep FAILED to build (EnemyFactory returned null) — step relies on natural reps / the watchdog.");
        }

        // =====================================================================
        //  hero.reached:<anchor> — the proximity probe (spec §2.1b)
        // =====================================================================

        private void TickProximityProbe()
        {
            if (string.IsNullOrEmpty(_awaitSignal) ||
                !_awaitSignal.StartsWith(TutorialSignals.HeroReachedPrefix, StringComparison.OrdinalIgnoreCase))
                return;

            string anchorId = _awaitSignal.Substring(TutorialSignals.HeroReachedPrefix.Length);

            // ── WO-1300: THE PROBE MUST NEVER GO QUIET ────────────────────────────────────
            // Both preconditions below used to `return` in SILENCE. When either held for the
            // whole beat the walk step produced ZERO walk-probe lines and then a bare
            // STEP-STUCK 120s later - the F8 capture named the missing SIGNAL but could not
            // name the missing PRECONDITION, which is the whole reason WO-1300 needed a
            // second investigation (CLAUDE.md sec.12: a step that can go stuck and cannot
            // report it is the bug repeating). They now report themselves on the SAME
            // played-time cadence as the progress trace, so one capture separates:
            //   * "no hero"      -> HeroLocomotion never resolved (probe never ran at all)
            //   * "no anchor"    -> the gate/Heart never resolved, so there is nothing to
            //                       walk to and no distance to shrink
            //   * a walk-probe line with a distance -> the probe IS running; read the distance
            // PERMANENT instrumentation (CLAUDE.md sec.12) - never strip.
            if (_hero == null)
            {
                _hero = FindAnyObjectByType<HeroLocomotion>();
                if (_hero == null)
                {
                    if (_stepClock.Charged >= _probeTraceAtCharged)
                    {
                        _probeTraceAtCharged = _stepClock.Charged + ProbeTraceSeconds;
                        FlowTrace.Warn("Tutorial",
                            $"walk-probe STALLED :: '{_awaitSignal}' - no HeroLocomotion in the scene, so the " +
                            $"proximity check cannot run at all (played={_stepClock.Charged:0}s of the " +
                            $"{WatchdogSecondsForCurrentStep():0}s bound). The beat CANNOT complete while this holds; " +
                            "this is a hero-spawn/scene defect, not a walk-distance one.");
                    }
                    return;
                }
            }

            // WO-962: idempotent re-latch. EnterStep already latched this anchor; this call
            // returns immediately when it did, and only takes effect when the anchor was NOT
            // resolvable at ENTER (a late-spawning gate / a hero that had not landed yet) -
            // in which case the FIRST frame it resolves becomes the latch for the whole step.
            // It can never RE-target a latch that already took, which is the defect.
            TutorialWorldAnchors.LatchAnchor(anchorId);
            if (!TutorialWorldAnchors.TryResolveAnchor(anchorId, out Vector3 pos))
            {
                if (_stepClock.Charged >= _probeTraceAtCharged)
                {
                    _probeTraceAtCharged = _stepClock.Charged + ProbeTraceSeconds;
                    FlowTrace.Warn("Tutorial",
                        $"walk-probe STALLED :: '{_awaitSignal}' - anchor '{anchorId}' does not resolve this frame " +
                        $"(no latch, and the live resolver answered nothing - for 'guide_gate' that means no " +
                        $"WaveSpawnPoint and/or no HeartController was found). hero={_hero.transform.position} " +
                        $"played={_stepClock.Charged:0}s of the {WatchdogSecondsForCurrentStep():0}s bound. " +
                        "There is NOTHING to walk to: the beat cannot complete however far the player walks.");
                }
                return;
            }

            // WO-1012 P2: the GUIDE (pet-Echo) LEADS every movement beat — re-asserted
            // each frame (the anchor can resolve late or move; the leash seam dedupes
            // its own tracing). Cleared on beat completion / flow teardown. Verified at
            // source: Pet.cs steers to SetHomePost; PetHeroLeash.SetLeadTarget is the
            // narrowest carrot-override seam (no new movement system).
            DeNelle.Pets.PetHeroLeash.SetLeadTarget(pos);

            Vector3 heroPos = _hero.transform.position;
            Vector3 d = heroPos - pos;
            d.y = 0f;

            // ── WO-1036 (A): the walk-beat progress trace — PERMANENT (CLAUDE.md §12) ───────────
            // The STEP-STUCK line proves the event is ABSENT; it never says WHY, and the three
            // causes need opposite fixes. This splits them from data alone, on a cadence measured
            // in PLAYED seconds so a backgrounded app cannot flood it:
            //   * distance shrinking to ~0 with no raise  -> the trigger/radius is the defect
            //   * distance never shrinking                -> the hero cannot (or does not) path there
            //   * anchor moving between lines             -> the WO-962 latch regressed
            //   * guide=none mid-beat                     -> the Echo despawned before arrival (WO-1108)
            // Cheap: one line per ProbeTraceSeconds of played time while a hero.reached step is live.
            if (_stepClock.Charged >= _probeTraceAtCharged)
            {
                _probeTraceAtCharged = _stepClock.Charged + ProbeTraceSeconds;
                bool guideAlive = TutorialWorldAnchors.HasLiveGuideBody;
                FlowTrace.Step("Tutorial",
                    $"walk-probe :: '{_awaitSignal}' anchor={pos} latched={TutorialWorldAnchors.IsLatched(anchorId)} " +
                    $"hero={heroPos} dist={d.magnitude:0.0}m (reach {ReachedRadius:0}m) " +
                    $"guideBody={(guideAlive ? "ALIVE" : "NONE")} leadSet={DeNelle.Pets.PetHeroLeash.IsLeading} " +
                    $"played={_stepClock.Charged:0}s wall={Time.unscaledTime - _stepEnteredAt:0}s " +
                    $"timeScale={Time.timeScale:0.00} suspendGap={_stepClock.DiscardedJumpSeconds:0}s");
            }

            if (d.sqrMagnitude <= ReachedRadius * ReachedRadius)
                TutorialSignals.Raise(_awaitSignal);
        }

        /// <summary>WO-1036: cadence (in PLAYED seconds) of the walk-beat progress trace.</summary>
        private const float ProbeTraceSeconds = 5f;
        private float _probeTraceAtCharged;

        // =====================================================================
        //  Watchdog — STEP-STUCK oracle (bot verifiability)
        // =====================================================================

        /// <summary>True when the CURRENT awaited completion is a specific-structure
        /// placement ("build.structure_placed:&lt;id&gt;" — founding_hollow / founding_stores).
        /// Drives the longer placement watchdog bound + the Build-button default highlight
        /// (F8 seq 603, 2026-08-02).</summary>
        private bool IsPlacementStep() =>
            !string.IsNullOrEmpty(_awaitSignal) &&
            _awaitSignal.StartsWith(TutorialSignals.StructurePlacedPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>The STEP-STUCK bound for the current step: placement-completion steps get
        /// <see cref="PlacementWatchdogSeconds"/> (300s), everything else the default 120s.</summary>
        private float WatchdogSecondsForCurrentStep() =>
            IsPlacementStep() ? PlacementWatchdogSeconds : WatchdogSeconds;

        /// <summary>WO-1036: true while the world clock is frozen (pause menu, F8 note freeze,
        /// PauseController's OnApplicationPause auto-pause — which never auto-resumes). The hero
        /// physically cannot walk while this holds ([Flow:HeroOwner] "WORLD CLOCK FROZEN"), so
        /// charging the beat's watchdog budget for it skips a step the player was never able to
        /// attempt. Excluded, exactly like builder time.</summary>
        private static bool WorldClockFrozen => Time.timeScale <= 0f;

        /// <summary>
        /// WO-1036 — the ONE place the in-step budget advances, ticked once per frame from
        /// <see cref="Update"/> before any consumer reads it (watchdog + coach nudge share it, so
        /// they can never disagree about how long the player has really been on the beat).
        /// Suspend jumps are clamped and TRACED, never charged: see the captured proof in the
        /// MaxWatchdogFrameStepSeconds note above.
        /// </summary>
        private void TickStepClock()
        {
            if (_step == null) return;

            bool builder = DeNelle.Core.BuildModeState.IsActive;
            bool frozen  = WorldClockFrozen;
            // WO-1414 D -- THE THIRD EXCLUSION, and it is the same rule as the other two: a beat
            // must never be charged for seconds the player could not act in. Captured 2026-09-05
            // (F8 seq 4682): the welcome-back modal sat over founding_greet at sortingOrder 32020
            // while the SKIP control lives at 6000, so the ONE escape hatch was unreachable, and
            // the breakdown still read "played-and-charged 120s ... excluded (builder/frozen) 0s"
            // before the beat was RESCUED and recorded as SKIPPED. A modal is neither the builder
            // nor a frozen clock, so it fell through both existing gates.
            // NARROW ON PURPOSE: the welcome-back report specifically, NOT "any PanelManager modal".
            // Pausing under Manage/Build/inventory screens would mask a genuinely stuck beat -- the
            // exclusion is for a modal the player did not open and cannot see past, not for one they
            // chose. Widen only with a capture that names the screen.
            bool modal   = DeNelle.Village.UI.WelcomeBackPopup.IsOpen;
            int  before  = _stepClock.DiscardedJumpFrames;

            float accepted = _stepClock.Tick(Time.unscaledDeltaTime, excluded: builder || frozen || modal);
            // Attribute the frame to the modal only when the modal is the reason it was excluded,
            // so the breakdown's "of which modal" slice can never overstate itself on a frame the
            // builder or a frozen clock would have excluded anyway.
            if (modal && !builder && !frozen) _modalExcludedSeconds += accepted;

            if (_stepClock.DiscardedJumpFrames > before && !_suspendJumpTraced)
            {
                _suspendJumpTraced = true;
                FlowTrace.Warn("Tutorial",
                    $"step '{_step.Id}': a {_stepClock.DiscardedJumpSeconds:0}s frame gap was DISCARDED from the " +
                    $"in-step budget — the app was backgrounded/suspended, not played (WO-1036, F8 seq 2513). " +
                    $"Charged {_stepClock.Charged:0}s of the {WatchdogSecondsForCurrentStep():0}s bound; wall clock " +
                    $"reads {Time.unscaledTime - _stepEnteredAt:0}s. Before this fix the whole gap was charged and " +
                    "the beat was rescued-and-SKIPPED on the resume frame.");
            }
        }

        private void TickWatchdog()
        {
            if (_step == null) return;
            if (_phase != Phase.AwaitCompletion) return;   // only a live, awaiting step can strand

            // F8 seq 603 (2026-08-02): the watchdog PAUSES while build mode is active — the
            // player is DOING the asked thing (browsing/placing in the builder), so builder
            // time never counts against the bound. Same build-mode seam the deferred-intro
            // truce already reads (BuildModeState.IsActive, fed by the build.mode_entered/
            // exited flow). WO-1036: the pause is now enforced by TickStepClock EXCLUDING the
            // frame from the charged budget — a true pause, kept, with pre-builder idle intact.
            if (DeNelle.Core.BuildModeState.IsActive)
            {
                FlowTrace.Once("Tutorial", "watchdog-builder-pause",
                    "STEP-STUCK watchdog PAUSED while the builder is open (build-mode time never counts — F8 seq 603 rule, 2026-08-02).");
                return;
            }

            // WO-1036: never rescue a step while the world is frozen. PauseController auto-pauses
            // on OnApplicationPause and NEVER auto-resumes, so the first resumed frame used to trip
            // the watchdog while the hero still could not move (captured: seq 2343's WORLD CLOCK
            // FROZEN lines alongside the STEP-STUCK). A frozen player has not abandoned the beat.
            if (WorldClockFrozen)
            {
                FlowTrace.Once("Tutorial", "watchdog-frozen-pause",
                    "STEP-STUCK watchdog PAUSED while Time.timeScale<=0 (pause menu / background auto-pause) — " +
                    "the hero cannot move, so this is not idle time (WO-1036).");
                return;
            }

            // WO-1414 D: never rescue a step while a modal the player did not open owns the screen.
            // Captured 2026-09-05 (F8 seq 4682/4705): the welcome-back report opened over
            // founding_greet and covered the ONE skip control (SKIP_TOP_HIT_BLOCKED
            // top=ObsidianPanel path=WelcomeBackUI/ObsidianPanel), and the beat was then rescued-and
            // -SKIPPED for 120s the player had no way to spend. Same shape as the two pauses above:
            // TickStepClock has already EXCLUDED the frame, this is the visible "why we are not
            // tripping" line. In a healthy run the deferral (WO-1414 D half A) takes the modal away
            // within a frame of the chain going live, so this line should NOT fire -- if a capture
            // ever shows it, half A did not hold and that is the finding.
            if (DeNelle.Village.UI.WelcomeBackPopup.IsOpen)
            {
                FlowTrace.Once("Tutorial", "watchdog-modal-pause",
                    "STEP-STUCK watchdog PAUSED while the welcome-back report owns the screen — it covers the " +
                    "skip control (sortingOrder 32020 vs 6000), so this is not idle time (WO-1414 D). " +
                    "The reveal should already have been deferred; seeing this line means it was not.");
                return;
            }

            float bound = WatchdogSecondsForCurrentStep();
            if (!_stepClock.Expired(bound)) return;

            // Owner ruling 2026-07-08 ("auto-advance the watchdog"): a step whose combat/
            // completion driver can never settle (e.g. town_wave's scripted spawner) must not
            // strand the banner forever. On trip we RESCUE (advance) the stuck step down the
            // SAME path a real completion takes — CompleteCurrentStep -> AdvanceToNextStep — so
            // state stays consistent (latch disarmed, drivers disarmed, seen-marked, UI hidden,
            // next step entered with a fresh watchdog window).
            //
            // F8 seq 632 ROOT CAUSE 4 (2026-08-02): the rescue is recorded as SKIPPED, never as a
            // genuine completion. It used to pass skipped:false, which made a step the player never
            // did indistinguishable from one they did: the STEP-COMPLETE trace and the
            // tutorial_step_complete analytic both fired, and — worse — the OUTRO played, so Sylas
            // narrated "There - your Hollow stands, and your first Echo has answered" for a Hollow
            // that was never built and is absent from BaseLayout. The tutorial must never tell the
            // player a thing happened that did not. skipped:true suppresses the outro + the
            // completion trace/analytic while KEEPING the two things a rescue must still do:
            //   * MarkTutorialSeen  — otherwise the stuck step replays forever on resume, and
            //   * the essential GRANT (grant.starterPet / grant.prepaidTower) — the SkipAll rule:
            //     a player who did not finish a beat must never end up half-granted and blocked
            //     out of the systems the later beats depend on.
            //
            // Fires ONCE per stuck step: CompleteCurrentStep advances _step and EnterStep resets
            // _stepClock/_stepEnteredAt, so this cannot re-trip on the same step.
            string stuckId  = _step.Id;
            string awaited  = _awaitSignal;
            float  idle     = _stepClock.Charged;                        // WO-1036: PLAYED, charged time
            float  wall     = Time.unscaledTime - _stepEnteredAt;        // what the old line reported
            float  excluded = _stepClock.Excluded;
            float  modalOut = _modalExcludedSeconds;                     // WO-1414 D: WHY, not just how much
            float  jumped   = _stepClock.DiscardedJumpSeconds;
            _stepClock.RestartCharged();   // re-arm guard (belt-and-suspenders alongside the advance)

            FlowTrace.Fail("Tutorial", $"STEP-STUCK :: {stuckId} — no '{awaited}' after " +
                $"{idle:0}s in-step (bound {bound:0}s" +
                (IsPlacementStep() ? ", placement 300s rule, builder time excluded" : ", builder time excluded") +
                $"; ff.tutorialv2 on; builderOpenedThisStep={_builderOpenedThisStep}, coachBeats={_coachBeats}); " +
                // WO-1036: the played/wall split IS the evidence. A large gap between them means the
                // app was backgrounded or the world was frozen, and the beat is NOT what stalled.
                // WO-1414 D: the excluded total now carries its REASONS. A non-zero modal slice says
                // a screen the player did not open held the beat; a zero one says it did not.
                $"[WO-1036 clock: played-and-charged {idle:0}s, wall {wall:0}s, excluded (builder/frozen/modal) " +
                $"{excluded:0}s of which modal {modalOut:0}s, discarded suspend gap {jumped:0}s]; " +
                "RESCUED via watchdog and recorded as SKIPPED - the step was NOT completed, its outro is " +
                "suppressed (no fiction narrated), grants still applied so the player is never half-granted.");
            DeNelle.Core.Analytics.EventTracker.Track("tutorial_step_drop", new
            {
                stepId = stuckId,
                secondsIdle = idle,
                secondsWall = wall,
                secondsExcluded = excluded,
                secondsExcludedModal = modalOut,   // WO-1414 D
                secondsSuspendGap = jumped,
                autoAdvanced = true,
                recordedAs = "skipped",
                builderOpened = _builderOpenedThisStep,
                coachBeats = _coachBeats,
            });

            // Reuse the normal completion/advance path, recorded HONESTLY as a skip.
            _skips++;
            CompleteCurrentStep(skipped: true);
        }

        // =====================================================================
        //  Contextual one-shots (flowId "contextual") — spec CREATIVE SCOPE
        // =====================================================================

        private void TryTriggerContextual(string signalId)
        {
            if (_activeCtx != null || _contextual == null) return;

            foreach (var ctx in _contextual)
            {
                if (ctx == null || ctx.Trigger == null) continue;
                if (!string.Equals(ctx.Trigger.Type, "signal", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(ctx.Trigger.Signal, signalId, StringComparison.OrdinalIgnoreCase)) continue;
                if (CtxSeen(ctx)) continue;

                _activeCtx = ctx;
                _ctxEnteredAt = Time.unscaledTime;
                // WO-1340 — a TEACH step waits on the world (a talent actually LEARNED), not on
                // its own text box closing. Clear the latch FIRST: the bus latches, so a stale
                // raise from earlier in the session would otherwise complete this beat the instant
                // it armed and teach nothing (the same latch-before-await contract the mandatory
                // chain uses).
                _ctxAwaitSignal = ctx.AwaitsGameplayCompletion ? ctx.Completion.Signal : null;
                _ctxStuckTraced = false;
                if (_ctxAwaitSignal != null) TutorialSignals.Clear(_ctxAwaitSignal);
                FlowTrace.Step("Tutorial", $"CTX-ENTER :: {ctx.Id} (trigger '{signalId}')" +
                    (_ctxAwaitSignal != null
                        ? $" - TEACH beat, awaiting '{_ctxAwaitSignal}' with a {ContextualAwaitSeconds:0}s escape bound."
                        : "."));
                DeNelle.Core.Analytics.EventTracker.Track("contextual_step_enter", new
                {
                    stepId = ctx.Id,
                    triggerSignal = signalId,
                });

                // Never pausePressure, never gate — a short line + a spotlight only.
                // WO-1012: contextual hints keep the GLOW language (no dim, never blocks)
                // and ride the same chevron cue as the mandatory chain.
                if (ctx.Highlight != null && ctx.Highlight.Count > 0)
                {
                    ShowHighlight(ctx.Highlight[0], UiSpotlight.MaskStyle.Glow);
                }
                // WO-1389 - a beat with a spotlight and NO dialogue (the TRAINING NOW coach-mark)
                // still has to SAY something: its authored hint rides the same gold toast the
                // stuck-step coach uses, so the mechanism is one, not two.
                if (!string.IsNullOrEmpty(ctx.Hint))
                    ShowCoachHint(ctx.Id, ctx.Hint);
                if (ctx.Dialogue != null && !string.IsNullOrEmpty(ctx.Dialogue.Intro))
                {
                    if (!CoreDialogue.DialogueService.Play(ctx.Dialogue.Intro))
                        FlowTrace.Warn("Tutorial", $"contextual '{ctx.Id}' dialogue '{ctx.Dialogue.Intro}' unknown.");
                }
                return;
            }
        }

        /// <summary>WO-1389 - the coach-mark SENTENCE for a route hop / hintful beat: the SAME
        /// gold toast surface the stuck-step coach (TickCoach) uses, so guided taps and rescue
        /// nudges look like one voice. Guarded: a toast that throws must never break the beat.</summary>
        private static void ShowCoachHint(string ctxId, string hint)
        {
            bool ok = Guard.Try("Tutorial", "coach hint toast " + ctxId, () =>
                ElarionUiKit.ShowToast(hint, ElarionUiKit.ToastTone.Gold, 4.5f));
            FlowTrace.Step("Tutorial", $"CTX-HINT :: {ctxId} - \"{hint}\"" + (ok ? "" : " (toast FAILED)"));
        }

        /// <summary>
        /// WO-1389 - TRUE once the contextual step <paramref name="ctxId"/> has been latched on
        /// this save (the tutorial_ctx:&lt;id&gt; SeenTutorials key). Exposed so an EMITTER can
        /// stop re-raising a trigger the flow will never consume again (TutorialSignalAdapters'
        /// raid.first_completed re-raise), without a second copy of the key prefix anywhere.
        /// Null state = not seen.
        /// </summary>
        public static bool IsContextualSeen(string ctxId)
        {
            if (string.IsNullOrEmpty(ctxId)) return false;
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return state != null && state.SeenTutorials != null &&
                   state.SeenTutorials.TryGetValue(CtxSeenPrefix + ctxId, out bool seen) && seen;
        }

        /// <summary>
        /// WO-1340 — re-point the live contextual hint's spotlight when the player reaches the
        /// next hop of its authored route. A hop with an empty highlight HIDES the spotlight:
        /// the player has arrived and the screen they now need to read must not be masked.
        /// Presentation only — never completes, never holds, never gates.
        /// </summary>
        private void TryAdvanceCtxRoute(string signalId)
        {
            var route = _activeCtx.Route;
            if (route == null || route.Count == 0) return;

            foreach (var hop in route)
            {
                if (hop == null || string.IsNullOrEmpty(hop.Signal)) continue;
                if (!string.Equals(hop.Signal, signalId, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.IsNullOrEmpty(hop.Highlight))
                {
                    HideHighlight();
                    FlowTrace.Step("Tutorial", $"CTX-ROUTE :: {_activeCtx.Id} - '{signalId}' reached; " +
                        "spotlight released (the player is on the screen the beat was pointing at).");
                }
                else
                {
                    ShowHighlight(hop.Highlight, UiSpotlight.MaskStyle.Glow);
                    FlowTrace.Step("Tutorial", $"CTX-ROUTE :: {_activeCtx.Id} - '{signalId}' reached; " +
                        $"spotlight now on '{hop.Highlight}'.");
                }
                // WO-1389 - the hop's coach-mark sentence, if authored ("Pick a troop").
                if (!string.IsNullOrEmpty(hop.Hint))
                    ShowCoachHint(_activeCtx.Id, hop.Hint);
                return;
            }
        }

        private void TickContextual()
        {
            if (_activeCtx == null) return;

            // WO-1340 — a TEACH beat outlives its dialogue on purpose (the spotlight has to
            // survive the text box so it can point at the route), so the 10s no-dialogue
            // auto-dismiss below must NOT apply to it. Its bound is ContextualAwaitSeconds,
            // and it ALWAYS ticks - this method runs every frame in every phase.
            //
            // §12: the beat NAMES ITSELF on expiry. WO-1300 exists because two stuck beats
            // emitted nothing and cost two investigations; a teach beat that quietly stopped
            // pointing would be the same defect in a cheaper coat.
            if (!string.IsNullOrEmpty(_ctxAwaitSignal))
            {
                if (Time.unscaledTime - _ctxEnteredAt >= ContextualAwaitSeconds)
                {
                    if (!_ctxStuckTraced)
                    {
                        _ctxStuckTraced = true;
                        FlowTrace.Warn("Tutorial", $"CTX-STUCK :: {_activeCtx.Id} - no " +
                            $"'{_ctxAwaitSignal}' after {ContextualAwaitSeconds:0}s. RELEASING the hint " +
                            "(spotlight cleared, marked seen) so it can never linger. The player was " +
                            "NOT blocked at any point - this beat gates nothing - but the teach did " +
                            "not land, so the route it points at is the thing to check.");
                    }
                    CompleteContextual("timeout");
                }
                return;
            }

            // No dialogue (or it never opened) — auto-dismiss after a short beat so a
            // contextual hint can never linger like a gate.
            if (Time.unscaledTime - _ctxEnteredAt >= ContextualAutoCloseSeconds &&
                !CoreDialogue.DialogueService.IsRunning)
                CompleteContextual("dismiss");
        }

        private void CompleteContextual(string outcome)
        {
            var ctx = _activeCtx;
            _activeCtx = null;
            // WO-1340 — clear the await state on EVERY exit path (complete / timeout / skip /
            // dismiss), not just the happy one. A surviving _ctxAwaitSignal would make the NEXT
            // ordinary hint behave like a teach beat and refuse to close on its own dialogue.
            _ctxAwaitSignal = null;
            _ctxStuckTraced = false;
            if (ctx == null) return;

            FlowTrace.Step("Tutorial", $"CTX-{outcome.ToUpperInvariant()} :: {ctx.Id}.");
            DeNelle.Core.Analytics.EventTracker.Track("contextual_step_" + outcome, new
            {
                stepId = ctx.Id,
                seconds = Time.unscaledTime - _ctxEnteredAt,
            });
            if (ctx.OneShot)
                GameStateService.Instance?.MarkTutorialSeen(CtxSeenPrefix + ctx.Id);

            // Only clear the spotlight/pointer if the mandatory chain isn't using them;
            // when it IS, re-assert the live step's own highlight (the ctx hint borrowed
            // the singletons — hand them back, don't leave the ctx target lit).
            if (_phase != Phase.AwaitCompletion)
            {
                HideHighlight();
            }
            else if (_highlightIds.Count > 0)
            {
                ShowHighlight(_highlightIds[_highlightIndex % _highlightIds.Count], MaskStyleForCurrentStep());
            }
        }

        private static bool CtxSeen(TutorialStepDef ctx)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return ctx.OneShot && state != null && state.SeenTutorials != null &&
                   state.SeenTutorials.TryGetValue(CtxSeenPrefix + ctx.Id, out bool seen) && seen;
        }
    }
}
