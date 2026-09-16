// =============================================================================
// AutoPilotDriver — DEV-ONLY autonomous playtest bot ("AutoPilot").
// -----------------------------------------------------------------------------
// The bot DRIVES the game itself through its real PUBLIC seams (it never fakes
// input or sets transform.position): it walks the hero to each scene gate, opens
// each vendor + HUD panel, actuates the buttons on them, forces a wave, and
// finally walks into the south exit. The EXISTING capture layer does the
// recording — BreakCaptureHarness rides Application.logMessageReceived, so every
// FlowTrace.Warn / FlowTrace.Fail this bot emits lands in break-log.jsonl. The
// bot therefore writes NO break file of its own; it only writes a per-run
// summary (autopilot-summary.json) the headless AutoPilotTickets emitter reads.
//
// STATE MACHINE: one coroutine, phase by phase. Every phase logs FlowTrace.Step
// on enter + exit and is wrapped in a per-phase REALTIME watchdog
// (WaitForSecondsRealtime). Realtime is mandatory: the F8 flag flow sets
// Time.timeScale = 0, which would freeze a scaled WaitForSeconds and hang the
// bot forever. On timeout the phase emits FlowTrace.Fail("Auto","<phase>
// TIMEOUT") and advances — it NEVER hangs. A global ~4-minute cap aborts the
// whole run if something wedges between phases.
//
// PHASES (in order):
//   BootToGameplay    — load MainCastle_Hall (skips the Title->PetSelect UI flow
//                       a headless bot can't drive); the additive OuterWorld load
//                       fires via the existing WorldSceneLoader.
//   ResolveHero       — find the HeroLocomotion (abort gracefully if absent).
//   WalkToEachGate    — drive the hero to every SceneTransitionTrigger.
//   OpenEachVendor    — open each BuildingInteractable's surface (as it would),
//                       actuate its clickables, then close.
//   OpenEachHUDPanel  — open every registered PanelId, actuate, close.
//   TriggerWave       — WaveManager.ForceBeginNextWave, poll the phase advances.
//   AttemptExitCastle — walk into the south exit, record scene change.
//
// ⚠ THE LIST ABOVE IS THE ORIGINAL SPINE, NOT THE SEQUENCE — read RunAll for that.
// Dozens of assertion phases have been seated between these since. What matters here
// is the TAIL: AssertExactlyOneGuideBody and AssertFreshSaveFtue (WO-1500) each found
// a NEW GAME, so no phase that reads the pre-reset save may ever be added after them.
//
// On completion it writes the summary and Application.Quit()s — UNLESS it was
// started from the dev-panel button (quitOnDone:false), so an in-editor manual
// run leaves the editor open.
//
// RELEASE-SAFE: the whole file is #if DEVELOPMENT_BUILD || UNITY_EDITOR.
// =============================================================================

#if DEVELOPMENT_BUILD || UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;   // WO-597: UQueryExtensions.Query for the UITK close-affordance scan (types stay fully qualified to avoid Button ambiguity)
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Village;
using DeNelle.Village.Hero;
using DeNelle.Village.Crafting;
using DeNelle.Village.World.Camps;   // EnemyOutpost / RaidOutpostSystem — WO-449 walk-to phase
// Enemy (WO-449 combat-on-approach assertion) resolves via `using DeNelle.Village;` above.

namespace DeNelle.DevTools
{
    /// <summary>
    /// DEV-ONLY autonomous playtest bot. A coroutine state machine that drives the
    /// hero + UI through their public seams while the always-on BreakCaptureHarness
    /// records any break. Spawned by <see cref="AutoPilotInstaller"/> (on
    /// <c>--autopilot</c> / the AUTOPILOT env var) or by the dev-panel "Run
    /// AutoPilot" button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AutoPilotDriver : MonoBehaviour
    {
        // ── tunables (realtime seconds) ──────────────────────────────────────
        private const float GlobalCapSeconds   = 420f;  // ~7 min hard cap on the whole run (240→300 WO-597 popup oracle; 300→420 verification probes 2026-07-07: AssertScatterRecords alone waits up to 3 bounded 25s maintain-tick windows — without headroom the cap would abort the late encounter phases)
        private const float WalkToGateTimeout   = 25f;   // per-gate approach
        private const float VendorTimeout       = 20f;   // per-vendor open+actuate+close
        private const float ContractTimeout     = 12f;   // per-vendor-context contract assertion
        private const float EconomyDeductTimeout = 15f;  // open shop + read-before + buy + read-after assert
        private const float EquipTimeout         = 15f;   // add gear + equip + assert loadout changed
        private const float HudPanelTimeout     = 15f;   // per-panel open+actuate+close
        private const float WaveTimeout         = 30f;   // wait for the wave phase to advance (bumped 20→30: covers the bounded start-retry window; player keeps a ~45s countdown)
        private const float ExitTimeout         = 30f;   // walk into the south exit
        private const float HomeReturnTimeout   = 90f;   // WO-602 — round trip: (self-armed exit leg if needed) + walk out ~20m + walk back to the gate landing + warp in
        private const float SettleSeconds        = 0.4f;  // brief pause after an open/close
        private const float BootTimeout          = 30f;   // load MainCastle_Hall + settle
        private const float ResolveHeroTimeout   = 15f;   // hero may spawn after scene load
        private const float OuterWalkTimeout     = 60f;   // WO-449: poll outpost realize (~10s) + walk ~70m + engage
        private const float PopupCloseWaitSeconds = 3f;   // WO-597: bounded close-wait per panel — a hang IS the bug; the bound converts it into a named POPUP_NO_CLOSE Fail instead of the generic softlock

        // WO-449 ANTI-WARP: the continuous-walk loop must NEVER teleport. A single-frame
        // hero displacement beyond this many metres is a WARP (the bug this phase guards),
        // not a walk — the hero's NavMesh walk moves far less than this per frame.
        private const float WalkMaxStepMeters = 3f;

        // The gameplay scene the bot must be in before it can drive.
        // WO-608: flag-aware. Under ff.MergedWorld the home hub is the single merged
        // Main_Castle_Overworld scene (all content in-scene), so the headless fleet boots + drives
        // THAT scene for the continuous-walk verification; OFF = legacy MainCastle_Hall.
        // TargetScene / the moat oracle / the popup-recover reload all derive from this, so they follow the flag.
        private static string GameplayScene => DeNelle.Core.FeatureFlags.MergedWorld
            ? "Main_Castle_Overworld"
            : "MainCastle_Hall";

        // Default seed when no --seed arg is supplied — a fixed value keeps a lone
        // run fully deterministic. Fleet runs pass distinct seeds to diverge paths.
        public const int DefaultSeed = 12345;

        // Set true when started from the dev-panel button — then we DON'T quit on done.
        private bool _quitOnDone = true;

        // Fleet variation: a seeded RNG shuffles the work order within phases (gates,
        // vendors, panels, click order) so different seeds explore different paths.
        private System.Random _rng = new System.Random(DefaultSeed);
        private int _seed = DefaultSeed;
        // When set (fleet mode --run=<id>), summary is written under
        // persistentDataPath/autopilot-runs/<id>/ alongside this run's break-log.
        private string _runId;

        private HeroLocomotion _hero;
        private float _runStartRealtime;

        // Passive assertion probes that ride alongside the scripted phases and catch
        // UX/structural defects the phase machine misses (unexpected auto-cross,
        // coplanar z-fight floors, walk-through-wall clips, dual-navmesh / stranding).
        // Spawned + armed only on an autopilot run (this driver is autopilot-only).
        private AutoPilotProbes _probes;

        // Per-phase result rows for the summary file.
        private readonly List<PhaseResult> _phases = new List<PhaseResult>();

        // WO-597: per-panel POPUP-CLOSABLE verdicts (PASS / OPEN_FAILED / NO_CLOSE /
        // NOT_REGISTERED), written into autopilot-summary.json alongside the phases.
        private readonly List<PopupCloseResult> _popupVerdicts = new List<PopupCloseResult>();

        // WO-602 — the exit phase actually crossed (fast-path: HomeReturnRoundTrip skips its
        // self-armed exit leg when this latched; the round trip is ATTEMPTED either way).
        private bool _exitCrossed;
        private readonly List<HomeReturnResult> _homeReturnVerdicts = new List<HomeReturnResult>();

        /// <summary>
        /// Configure + start the bot. <paramref name="quitOnDone"/> false (the
        /// dev-panel manual run) leaves the editor open on completion; true (the
        /// headless --autopilot run) quits so a CI invocation terminates.
        /// </summary>
        public void Begin(bool quitOnDone)
        {
            Begin(quitOnDone, DefaultSeed, null);
        }

        /// <summary>
        /// Fleet-aware start. <paramref name="seed"/> drives the per-run path variation
        /// (work-order shuffles + click order); <paramref name="runId"/>, when non-null,
        /// namespaces the summary into persistentDataPath/autopilot-runs/&lt;id&gt;/ to
        /// match the BreakCaptureHarness output for the same run.
        /// </summary>
        public void Begin(bool quitOnDone, int seed, string runId, string startScene = null)
        {
            _quitOnDone = quitOnDone;
            _seed = seed;
            _runId = string.IsNullOrEmpty(runId) ? null : runId;
            _startScene = string.IsNullOrEmpty(startScene) ? null : startScene.Trim();
            _rng = new System.Random(seed);
            StartCoroutine(RunAll());
        }

        // Optional boot-scene override (--scene=<name> / AUTOPILOT_SCENE). When set, BootToGameplay loads
        // THIS scene instead of MainCastle_Hall — the "instant spawn into Village2 / a garrison" the owner
        // asked for, so a headless/dev bot lands directly in the system under test (the real Village2Raid/
        // Garrison/HeroControlEnsurer path) with no traversal. The scene MUST be in Build Settings to load
        // by name; if it isn't, the LoadScene throws and is caught/logged (falls through to abort).
        private string _startScene;
        private string TargetScene => string.IsNullOrEmpty(_startScene) ? GameplayScene : _startScene;

        private void Start()
        {
            // If something added this component directly (no Begin call), still run.
            if (_runStartRealtime == 0f && !_started)
                Begin(_quitOnDone);
        }

        private bool _started;

        private IEnumerator RunAll()
        {
            if (_started) yield break;
            _started = true;
            // FOCUS-LOSS IMMUNITY (windowed runs 13:51 + 14:01, 2026-07-03): a -Graphics bot
            // run FROZE the moment the owner used her machine (window unfocused ->
            // OnApplicationPause in the log, break-log dead after t=110, killed at the cap)
            // because the player defaults to pausing in background while the driver's
            // realtime budgets keep expiring. A bot run must never pause on focus loss.
            Application.runInBackground = true;
            _runStartRealtime = Time.realtimeSinceStartup;
            FlowTrace.Step("Auto", $"AutoPilot START (quitOnDone={_quitOnDone}, seed={_seed}, run='{_runId ?? "<none>"}', scene='{ActiveScene()}').");

            // WAVE-DETERMINISM RESET (replay-wave RCA 2026-07-11, Builds/replay-wave.log): the
            // fleet's PERSISTENT save accumulates BestWave across runs (all 20 waves cleared ->
            // WaveManager.ResolveStartWave seeds s_resumeWaveId = BestWave+1 = 21 -> 'no WaveDef
            // for waveId=21 — schedule exhausted, phase->Complete' instantly -> AssertWaveVendorRules
            // and every wave oracle fail with 'combat authority never armed (phase Complete)').
            // Seeded chaos + FIXED oracles require a deterministic STARTING state, so zero the
            // save's wave-progress fields BEFORE BootToGameplay loads the hub (and its WaveManager).
            // Autopilot-only by construction: this driver exists only on an opted-in bot run.
            yield return ResetWaveProgressForDeterminism();

            // Arm the passive assertion probes (autopilot-only — this driver is the sole
            // spawner). They watch world state across every phase via FlowTrace.Fail.
            try
            {
                _probes = gameObject.AddComponent<AutoPilotProbes>();
                _probes.Arm();
            }
            catch (Exception ex) { FlowTrace.Warn("Auto", "Failed to arm AutoPilotProbes: " + ex.Message); }

            // WO-452 §A (tranche 3): arm the UI panel-health guard alongside the probes —
            // it watches for duplicate UIDocument / onboarding-panel-in-gameplay-scene
            // defects (the dev-tools-dead-after-Yarn class) and Fails to break-log.jsonl.
            try
            {
                var guards = gameObject.AddComponent<AutoPilotLogGuards>();
                guards.Arm();
            }
            catch (Exception ex) { FlowTrace.Warn("Auto", "Failed to arm AutoPilotLogGuards: " + ex.Message); }

            // Skip Yarn: a background suppressor dismisses any dialogue that auto-starts
            // (e.g. the companion intro SylasFirstMeeting on entering MainCastle_Hall) so a
            // headless bot never stalls inside a conversation it can't read. This is the
            // concrete first case of the owner's "bot decides from a tag what to skip" idea —
            // dialogue is the prime skip; a future BotHints/tag pass generalizes per-scene
            // (ATB / Arena coverage) what each bot exercises vs skips.
            StartCoroutine(SuppressDialogue());

            // FIRST: get into the gameplay scene. Headless can't drive the
            // Title->PetSelect->MainCastle_Hall UI flow, so jump straight there.
            yield return RunPhase("BootToGameplay", BootToGameplay(), abortIfFailed: true);

            yield return RunPhase("ResolveHero", ResolveHero(), abortIfFailed: true);
            // If the hero never resolved, RunAll short-circuits to the summary.
            if (_hero != null)
            {
                // Hero-crossing test (owner 2026-06-21): if a HeroLinkCrossing pair exists (e.g. Village2 gate),
                // walk the hero into the entry and assert it WARPS to the destination. Runs first so --scene=Village2
                // proves the gate crossing headless (owner never the detector).
                yield return RunPhase("AssertHeroCrossing", AssertHeroCrossing());
                // HERO_TURN_PROBE (owner "turn-left-before-walk" RCA 2026-07-10): from a KNOWN idle
                // facing offset 90° from the camera-forward, drive FORWARD via the scripted-move seam
                // and capture the per-frame [Flow:HeroTurn] rotation trace — a headless, deterministic
                // measurement of the move-start yaw slew the owner FEELS as a left swing. Emits the
                // HERO_TURN_PROBE :: summary marker; the full step trace is in the [Flow:HeroTurn] lines.
                yield return RunPhase("AssertHeroTurnOnMoveStart", AssertHeroTurnOnMoveStart());
                // F8-29 verification probe: runs EARLY (before any phase mutates the save /
                // completes tutorial steps) — asserts the sceneLoaded re-arm actually put a
                // TutorialFlow in the hub and, on a fresh save, that the flow is LIVE.
                yield return RunPhase("AssertTutorialArms", AssertTutorialArms());
                // WHITE-PALADIN VERIFICATION PROBE (owner directive: every fixed flow proves its
                // repaired chain): assert the hero body's PACKAGE albedo audit bound every
                // material (19/19 post-extraction) AND that the WHITE HERO ROOT Fail stayed dead.
                yield return RunPhase("AssertHeroHasAlbedo", AssertHeroHasAlbedo());
                yield return RunPhase("WalkToEachGate", WalkToEachGate());
                yield return RunPhase("OpenEachVendor", OpenEachVendor());
                // Runs even if building discovery found 0 vendors: it opens shops DIRECTLY
                // by context (the castle storefronts route through CastleNpcInteractable +
                // DialogueService, NOT BuildingInteractable), so the contract assertion is
                // not gated on building discovery.
                yield return RunPhase("AssertVendorContracts", AssertVendorContracts());
                // TKT-15 talk-fix DATA-VERIFY: drive each castle vendor's REAL Interact() (the path
                // the HUD Talk button fires) and assert SHOPPABLE vendors open the Buy/Sell dialogue,
                // not the upgrade panel (the regression). Closes the castle-vendor headless coverage hole.
                yield return RunPhase("AssertVendorTalkRoute", AssertVendorTalkRoute());
                // LEVER 1 confirming test (owner 2026-07-24, "stores pre-stand on a fresh hub"):
                // the DATA-confirmed hub bug was store NPCs NEVER appearing (the injector anchored
                // only to live/replayed Buildings, but standdown stands the baked ring + stations
                // down -> nothing replayed -> zero vendors). This oracle asserts EVERY vendor role
                // (CastleVendorNpcInjector.VendorRoles) has a seated NPC AND no non-action id maps
                // to a vendor — FAILING loudly (FlowTrace.Fail) on any action anchor with no NPC.
                yield return RunPhase("AssertVendorCoverage", AssertVendorCoverage());
                // ASSERTION-DEPTH EXPANSION: the bot used to mostly verify "didn't crash" +
                // the vendor STOCK contract. These two phases assert real CORRECTNESS of the
                // economy + equip wiring — that a buy actually deducts the cost AND grows the
                // inventory, and that equipping actually changes the hero's loadout/stat.
                yield return RunPhase("AssertEconomyDeduct", AssertEconomyDeduct());
                yield return RunPhase("AssertEquip", AssertEquip());
                // WO-452 tranche D: live play -> quicksave -> reload round-trip oracle. Mutate
                // wallet (resources), party roster and the tracked quest id, GameStateService
                // Save()->Load(), and assert all three survived. Complements the headless
                // SessionRegression schema round-trip by guarding the LIVE play->save->reload path.
                yield return RunPhase("AssertSaveRoundTrip", AssertSaveRoundTrip());
                // P0 (owner 2026-07-07, "armed but zero PlaceConfirm checks"): drive the REAL
                // tutorial first-tower path end-to-end — Enter build mode, arm tower_ground_archer
                // via the real Arm path, inject a click through the REAL IBuildInput seam
                // (BuildModeController.SetInput — the same seam EnsureTouchInput installs through),
                // then assert PlaceConfirm→StructurePlaced→TutorialSignals 'build.tower_placed'→
                // (if the tutorial flow is live) step persistence. NO logic bypass: if a placement
                // gate is stuck, this FAILS and the throttled '[Flow:Build] PlaceLoop BLOCKED at
                // <gate>' lines name the culprit — that is its purpose.
                yield return RunPhase("AssertTutorialFirstTower", AssertTutorialFirstTower());
                // WO-702 founding-arc probe (guide identity per WO-1012 P2): on a FRESH
                // save, assert a GUIDE body (the pet-Echo, else the steward stand-in)
                // stands near the Heart (+ 'world.guide' resolves to it), drive the live
                // founding_greet dialogue to its end through the REAL Advance path, place
                // the Echo Hollow through the REAL build gate (ArmById('pet-house') +
                // injected click — the 2990aaf6 lesson, no logic bypass), then assert the
                // per-item signal, the starter-pet grant (Pets roster + StarterPetId), the
                // FTUE peace window, and that DEFEND (ForceBeginNextWave→GuardedKickoff)
                // refuses while the arc is incomplete. Every failed link names itself.
                yield return RunPhase("AssertFoundingArc", AssertFoundingArc());
                // WO-677 / MOB-1 (owner 2026-07-12, mobile web: Move/Sell unreachable): the touch
                // verb bar (Rotate ⟲⟳ + Cancel) only exists on touch devices (EnsureTouchInput
                // gates on Input.touchSupported), so NO desktop/fleet log has ever executed its
                // Awake→AdoptPanelSettings — the suspected silent-non-render root has zero
                // captured data. This phase manufactures the §12 capture through the REAL path:
                // instantiate the driver in the live scene, let AdoptPanelSettings run for real
                // (its ':239 No sibling PanelSettings' warning lands in THIS log if none exists),
                // census every UIDocument-with-PanelSettings so the verdict is named either way,
                // and assert the bar is actually renderable. Pre-fix this FAILS (the proving
                // line); after the WO-677 Lane A uGUI rebuild it PASSES (the post-fix line).
                yield return RunPhase("AssertTouchVerbBarRenderable", AssertTouchVerbBarRenderable());
                // WO-677 Lane D: the full mobile edit chain through the REAL seams —
                // arm → cancel via the controller's RequestUiCancel latch (the same
                // web-safe pattern the shipped PLACE button uses) → idle reached →
                // tap-select the placed structure (real click through IBuildInput →
                // UpdateSelectLoop) → Move (ProbeBeginMoveSelected, the Move button's
                // handler target) → commit at a new cell → assert the layout record
                // moved. Every link that fails names itself (the Lane B traces).
                // WO-683 adds link DPAD: write the REAL HudMoveInput static (the seam
                // the build-overlay d-pad publishes) → assert the armed ghost's cell
                // changed via ProbeArmedGhostCell (the reflection merge is alive).
                yield return RunPhase("AssertBuildMoveChain", AssertBuildMoveChain());
                // DETERMINISTIC GARRISON-ROSTER DIAG (tickets #2 troll-orientation + #4 magenta): the chaos
                // walk cannot reliably reach Village2's garrison before the time budget, so the orc/troll
                // roster never spawns to be inspected. Build the EXACT village2_stronghold roster HERE via the
                // canonical EnemyFactory path (no traversal) and let EnemyFactory's own render-verify + the
                // worldUp trace + TripoMatFix VERIFY lines capture each one. Also warps the hero to prove the
                // WarpTo path keeps its body (the #2 bare-pill hero-side check). Read-only diagnosis; cleans up.
                yield return RunPhase("DiagGarrisonRoster", DiagGarrisonRoster());
                // P0 re-entrancy verification probe: Play(A) -> A's Closed synchronously
                // chains Play(B) (the tutorial's dialogue.ended shape) -> close B. Proves
                // the per-VM stale-Closed guard keeps the successor's panel alive and that
                // hero input releases (the frozen-build-mode root, DialogueView.cs RCA).
                yield return RunPhase("AssertDialogueChain", AssertDialogueChain());
                yield return RunPhase("OpenEachHUDPanel", OpenEachHUDPanel());
                // WO-597 slice 1: the POPUP-CLOSABLE oracle — registry-driven walk of every
                // PanelId: open -> assert a close affordance exists (the shared master-frame
                // Close) -> trigger it -> assert actually closed. Violations land as
                // error-level FlowTrace.Fail("PopupClose", "POPUP_NO_CLOSE :: ...") /
                // "POPUP_OPEN_FAILED :: ..." so break-log ranks them; per-panel verdicts go
                // into autopilot-summary.json (popupClose[]).
                yield return RunPhase("AssertPopupClose", AssertPopupClose());
                // F8-30 VERIFICATION PROBE: open the orient editor via the real OpenDevOrient
                // path, assert the PanelManager 'OrientEditor' registration, then release it
                // through the EXTERNAL path (PanelManager.CloseAll) that leaked pre-fix. Runs
                // BEFORE TriggerWave so the arbiter's battle-lock cannot reject the open.
                yield return RunPhase("AssertOrientModalReleases", AssertOrientModalReleases());
                // UI-fidelity capture for the DOCK overlays the PanelRouter sweep can't reach
                // (ClanChat/Leaderboard/Jukebox/HelpMenu open via their own singleton Toggle()/
                // Open(), not PanelRouter.Open) + a castle-facing moat-ring beauty angle. Both
                // run BEFORE TriggerWave so no battle-lock rejects the dock opens. Guarded — a
                // capture failure logs + continues; captures render only graphics-on.
                yield return RunPhase("CaptureDockOverlays", CaptureDockOverlays());
                // UI-fidelity capture for the GAMEPLAY-SCENE panels the PanelRouter sweep can't
                // reach because they are NOT registered with PanelRouter (they open via their own
                // singleton / static Open()/Show()/Pause() or need a stub VM). Mirrors
                // CaptureDockOverlays: force-open each, screenshot panel_<Screen>.png with the token
                // the assembler expects (see UI_REVIEW/_mapping.json deliveredShot), then close. Runs
                // BEFORE TriggerWave so no battle-lock rejects a PanelManager open. Fully guarded — a
                // failing panel logs + continues; captures render only graphics-on.
                yield return RunPhase("CaptureExtraPanels", CaptureExtraPanels());
                // Moat completeness oracle — hub-only, runs in play-mode AFTER a settle wait so
                // its reachability leg sees a live navmesh (mirrors CastleNavTopologyDiag). Emits
                // its own MOAT_COMPLETE / MOAT_INCOMPLETE marker to break-log.
                yield return RunPhase("VerifyMoatOracle", VerifyMoatOracle());
                yield return RunPhase("CaptureMoatRing", CaptureMoatRing());
                // Merged-world castle EXTERIOR evidence (owner 2026-07-04): the QA fleet never
                // SHOWS the castle exterior, so a leftover bridge/seam structure out there is
                // invisible to CLI + owner. One aerial top-down (whole castle + ~150m ring) plus
                // one shot 25m OUTSIDE each of the 4 gates looking BACK at the castle.
                yield return RunPhase("CaptureCastleExterior", CaptureCastleExterior());
                yield return RunPhase("TriggerWave", TriggerWave());
                // WO-452 tranche C: combat invariants during the (just-triggered) wave — hero HP
                // never goes negative while still alive, >=1 placed tower actually fired in the
                // defense window, and >=2 distinct enemy types appeared. N/A (skipped) in a scene
                // with no wave loop (e.g. the MainCastle_Hall hub).
                yield return RunPhase("AssertCombatInvariants", AssertCombatInvariants());
                // F8-14 verification probe: rides the wave TriggerWave just forced —
                // asserts the shared combat authority armed, vendors ducked out of sight,
                // a shop-open verb is BLOCKED (Warn+toast, never a panel), the build-mode
                // entry gate stays open (read-only), then force-clears the wave and
                // watches the vendors restore.
                yield return RunPhase("AssertWaveVendorRules", AssertWaveVendorRules());
                // F8-16 VERIFICATION PROBE: with live enemies in scene (the wave just ran; a
                // disposable factory enemy is built if none survived), assert the compass enemy
                // buffer fills AND >=1 ACTIVE pip meets the 10x16px visibility floor —
                // -nographics renders nothing but the rect math is fully assertable.
                yield return RunPhase("AssertCompassMarks", AssertCompassMarks());
                // AttemptExitCastle deliberately crosses a scene seam, so tell the
                // UNEXPECTED-CROSS probe this load is intentional (else it would flag
                // the bot's own exit). Clear it again right after.
                _probes?.SetIntentionalCrossPhase(true);
                yield return RunPhase("AttemptExitCastle", AttemptExitCastle());
                // WO-602 ROUND TRIP: after a successful exit, walk outward ~20m then navigate back
                // to the nearest gate's OUTER return entrance and assert the hero re-enters the
                // courtyard (y≈liftY AND inside the plinth footprint). Exit-only coverage is how
                // "no way back home" shipped unfelt. Stays inside the intentional-cross window
                // (the return is a deliberate seam warp too).
                yield return RunPhase("HomeReturnRoundTrip", HomeReturnRoundTrip());
                _probes?.SetIntentionalCrossPhase(false);

                // WO-449: the continuous-walk raid loop — walk to a live outpost and
                // prove combat triggers ON FOOT (no teleport). This phase loads NO scene, so the
                // UNEXPECTED-CROSS probe stays ARMED (NOT marked intentional): a re-introduced
                // raid/outpost teleport would trip AutoPilotProbes' scene-load Fail.
                yield return RunPhase("WalkToOuterWorldOutpost", WalkToOuterWorldOutpost());

                // F8-8 VERIFICATION PROBE: warp into an outer roster zone inside the scatter
                // band, drive the REAL MaintainLoop, and assert generation -> 85m sight
                // ACTIVATION -> 115m CULL, with counts + bands in the PASS lines.
                yield return RunPhase("AssertScatterRecords", AssertScatterRecords());

                // WO-482: drive the overworld-encounter -> isolated BattleArena loop HEADLESSLY via the
                // REAL trigger path end-to-end (NOT a BeginEncounter bypass). Warp the hero into an
                // OuterWorld roster region, force the spawner's real SpawnRep, then warp the hero onto
                // the rep so RepEngageWatcher's own Update fires Engage()->BeginEncounter. Asserts a rep
                // SPAWNED + the battle DROPPED + the family staged, then force-wins. This FAILS if the
                // rep->engage->battle path is broken (the spawn-gate bug that sailed through the old
                // direct-call oracle). The owner is never the tester (memory never-dragdrop-or-manual).
                yield return RunPhase("AssertEncounterBattle", AssertEncounterRealPath());

                // DUNGEON LOOP PROBE (owner 2026-08-05, F8 night): the owner had to WALK HER OWN
                // HERO into the Healer's Cottage to prove the 219924ca combat-components fix. She is
                // the product owner, not the tester (memory never-dragdrop-or-manual-playtest). This
                // phase performs that EXACT sequence headless — hub -> resolve the portal at runtime ->
                // tap the REAL Interact prompt -> reach a scripted encounter -> win it -> survive the
                // post-victory settle -> walk -> return to the hub — and asserts the five things she
                // had to check by hand (A combat-capable, B on-mesh, C can actually MOVE, D scene not
                // black, E no duplicate pose writers). Loads scenes both ways, so it sits inside an
                // intentional-cross window; it restores the hub before the one-guide phase runs.
                _probes?.SetIntentionalCrossPhase(true);
                yield return RunPhase("AssertDungeonLoop", AssertDungeonLoop());
                _probes?.SetIntentionalCrossPhase(false);

                // ONE-GUIDE LOCK (WO-971, owner ruling 2026-08-10: "why are two tutorials
                // active?" / "remove the original" / "only the new wolf one stays").
                // SUPERSEDES the FTUE-1 "AssertStewardSurvivesNewGame" probe, which asserted
                // the OPPOSITE — that the Sylas steward body respawned on a New Game. That
                // steward WAS the second guide: her 20:42 capture put it at (2.00,0.08,3.00)
                // and the wolf at (2.00,0.06,3.00), with the one 'world.guide' spotlight
                // alternating between them. The steward is deleted, so the probe that
                // guarded its lifecycle is replaced by the probe that guards its ABSENCE.
                // Runs in the RESET TAIL deliberately: it resets the save and reloads the
                // scene, so nothing state-dependent may sit downstream of it. The reload is
                // an intentional scene load — window the UNEXPECTED-CROSS probe around it.
                // (It was the LAST phase until WO-1500 seated AssertFreshSaveFtue behind it;
                // both live in this tail because both own a New Game, and no phase that reads
                // the pre-reset save may ever be added after either of them.)
                _probes?.SetIntentionalCrossPhase(true);
                yield return RunPhase("AssertExactlyOneGuideBody", AssertExactlyOneGuideBody());
                _probes?.SetIntentionalCrossPhase(false);

                // FRESH-SAVE FTUE LANE (WO-1500). LAST, for the same reason as the phase
                // above: it founds a New Game of its own. See the method header for why this
                // is the ONE phase that must normally be run on its own
                // (--phases=FreshSaveFtue) rather than at the tail of a full sweep.
                _probes?.SetIntentionalCrossPhase(true);
                yield return RunPhase("AssertFreshSaveFtue", AssertFreshSaveFtue());
                _probes?.SetIntentionalCrossPhase(false);
            }

            FlowTrace.Step("Auto", "AutoPilot complete");
            WriteSummary();

            if (_quitOnDone)
            {
                FlowTrace.Step("Auto", "AutoPilot quitting (quitOnDone=true).");
                Application.Quit();
            }
            else
            {
                FlowTrace.Step("Auto", "AutoPilot done — editor left open (quitOnDone=false).");
            }
        }

        // WO-482 — HEADLESS verify of the overworld-encounter -> isolated BattleArena loop
        // via the REAL trigger path (NOT a BeginEncounter bypass). The old oracle called
        // BattleArena.BeginEncounter DIRECTLY and would miss bugs in the real spawn path.
        // This drives the ACTUAL chain: warp the hero into a roster region on navmesh ->
        // force the spawner's REAL SpawnRep ->
        // warp the hero onto the rep so RepEngageWatcher's OWN Update fires Engage()->
        // BeginEncounter -> assert BattleInProgress -> assert the family staged -> force-win.
        // Every failure is a FlowTrace.Fail so it lands in break-log.jsonl as a ranked ticket.
        private IEnumerator AssertEncounterRealPath()
        {
            const string Tag = "Auto";
            EnsureHero("AssertEncounterBattle");   // re-resolve a post-stream hero (RCA 2026-07-08) — unlock overworld coverage
            if (_hero == null) { _lastDetail = "no hero - skipped"; FlowTrace.Warn(Tag, "AssertEncounterRealPath: no hero - skipped (EnsureHero named the reason above)."); yield break; }

            // 1) Enable the (default-OFF) feature for the assertion; restore after.
            int prevFlag = PlayerPrefs.GetInt("ff.overworldencounter", -1);
            PlayerPrefs.SetInt("ff.overworldencounter", 1);

            // 2) Warp the hero to a point that is BOTH on navmesh AND classified by ZoneManager
            //    as an OUTER roster region (so the spawner can spawn reps). Sample candidate points.
            Vector3 homePos = _hero.transform.position;
            Vector3[] candidates = { new Vector3(40f, 0f, 40f), new Vector3(60f, 0f, 0f), new Vector3(0f, 0f, 40f), new Vector3(40f, 0f, 0f), new Vector3(-40f, 0f, 40f) };
            Vector3 landing = Vector3.zero;
            bool placed = false;
            foreach (var c in candidates)
            {
                if (!UnityEngine.AI.NavMesh.SamplePosition(c, out var nh, 12f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                bool roster = false;
                try { roster = DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(nh.position)); }
                catch (Exception ex) { FlowTrace.Warn(Tag, "AssertEncounterRealPath: zone check threw " + ex.Message); }
                if (roster) { landing = nh.position; placed = true; break; }
            }

            if (!placed)
            {
                _lastDetail = "no on-mesh roster region found -> rep cannot anchor";
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: no candidate point was BOTH on navmesh AND in a roster region (navmesh not baked / zones not defined) - the real rep path cannot run.");
                RestoreEncounterFlag(prevFlag);
                yield break;
            }

            try { _hero.WarpTo(landing); } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertEncounterRealPath: hero WarpTo threw " + ex.Message); }
            for (int i = 0; i < 3; i++) yield return null;
            FlowTrace.Step(Tag, "AssertEncounterRealPath: hero warped into roster region @ " + landing + ".");

            // 3) Force the spawner's REAL spawn path (SpawnRep) -- do NOT spawn reps ourselves,
            //    do NOT call BeginEncounter. Then wait for a real OrcRep_* to exist.
            var spawner = DeNelle.Village.OverworldEncounterSpawner.Instance;
            if (spawner == null)
            {
                _lastDetail = "OverworldEncounterSpawner.Instance NULL";
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: OverworldEncounterSpawner.Instance was NULL - the spawner never bootstrapped.");
                RestoreEncounterFlag(prevFlag);
                yield break;
            }
            try { spawner.ForcePopulateForTest(); } catch (Exception ex) { FlowTrace.Fail(Tag, "AssertEncounterRealPath: ForcePopulateForTest threw " + ex.GetType().Name + ": " + ex.Message); }

            GameObject rep = null;
            float spawnWait = 0f;
            while (spawnWait < 8f)
            {
                rep = FindRepObject();
                if (rep != null) break;
                spawnWait += Time.deltaTime; yield return null;
            }

            bool repSpawned = rep != null;
            if (!repSpawned)
            {
                // THIS is the bug the old direct-call oracle missed: no rep ever materialised.
                _lastDetail = "repSpawned=false -> rep->engage->battle path BROKEN";
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: NO OrcRep_* spawned within 8s after the REAL SpawnRep path - the overworld rep never materialised (the spawn-gate class of bug).");
                RestoreEncounterFlag(prevFlag);
                yield break;
            }
            FlowTrace.Step(Tag, "AssertEncounterRealPath: real rep '" + rep.name + "' spawned @ " + rep.transform.position + ".");

            // 4) Drive the REAL engage: warp the hero ONTO the rep (within EngageRange ~2.6m) so
            //    RepEngageWatcher's OWN Update fires Engage()->BeginEncounter. Do NOT call Engage().
            Vector3 onRep = rep.transform.position;
            try { _hero.WarpTo(onRep); } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertEncounterRealPath: hero WarpTo(rep) threw " + ex.Message); }
            FlowTrace.Step(Tag, "AssertEncounterRealPath: hero warped onto rep @ " + onRep + " (within EngageRange) - waiting for the watcher to fire Engage.");

            // 5) Assert the battle DROPS (BattleInProgress becomes true) within ~6s -- the real
            //    "drop to battle" the owner never reached.
            var arena = DeNelle.Village.Arena.BattleArena.Instance;
            float engageWait = 0f;
            while (engageWait < 6f && !(arena != null && arena.BattleInProgress))
            {
                // re-assert position in case any locomotion nudged the hero off the rep before the watcher ticked
                engageWait += Time.deltaTime; yield return null;
            }
            bool droppedToBattle = arena != null && arena.BattleInProgress;
            if (!droppedToBattle)
            {
                _lastDetail = "repSpawned=true droppedToBattle=false -> engage->battle BROKEN";
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: rep spawned but BattleInProgress never became true within 6s of the hero touching it - RepEngageWatcher.Engage()->BeginEncounter did NOT drop to battle.");
                RestoreEncounterFlag(prevFlag);
                yield break;
            }
            FlowTrace.Step(Tag, "AssertEncounterRealPath: dropped to battle (BattleInProgress=true) via the REAL watcher engage.");

            // 6) Let the arena build + bake + warp + spawn settle, then assert the family staged.
            // COMBAT-HUD CAPTURE (WO-611 image-pair): the EARLY shot fires at 0.9s — runs
            // 9405-9408 proved even 2.2s races the Lv1 bot's death (timeout tails show an
            // end-state on screen; only run 9404 ever landed the 2.2s frame). The HUD posture
            // flips the moment the battle drops, so 0.9s is enough for the widgets; a second
            // best-effort LATE shot at 2.6s gets a dressed-arena frame when the bot survives.
            float build = 0f; int shots = 0;
            while (build < 4f)
            {
                build += Time.deltaTime; yield return null;
                bool early = shots == 0 && build >= 0.9f;
                bool late = shots == 1 && build >= 2.6f;
                if (early || late)
                {
                    shots++;
                    try
                    {
                        string shotDir = System.IO.Path.Combine(Application.persistentDataPath, "ui-shots");
                        System.IO.Directory.CreateDirectory(shotDir);
                        string name = early ? "battle_hud.png" : "battle_hud_late.png";
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, name));
                        FlowTrace.Step(Tag, "AssertEncounterRealPath: " + name + " captured at " + build.ToString("F1") + "s.");
                    }
                    catch (Exception ex) { FlowTrace.Warn(Tag, "battle_hud capture threw " + ex.Message); }
                }
            }

            var found = new System.Collections.Generic.List<DeNelle.Village.Enemy>();
            foreach (var e in UnityEngine.Object.FindObjectsByType<DeNelle.Village.Enemy>())
                if (e != null && e.gameObject.name.StartsWith("ArenaEnemy_")) found.Add(e);

            int skinned = 0, orcCtrl = 0;
            foreach (var e in found)
            {
                if (e == null) continue;
                if (e.GetComponentInChildren<SkinnedMeshRenderer>() != null) skinned++;   // real orc mesh, not a capsule
                // WO-606 made arena families data-driven (SpawnAreaTable draws), so non-orc
                // rosters (SkeletonHumanoid etc.) are legitimate — assert the real invariant
                // "will animate" (controller bound + valid avatar when Humanoid), not the
                // controller NAME containing "Orc" (false T-pose flags on skeleton draws).
                var anim = e.GetComponentInChildren<Animator>();
                bool rigged = anim != null && anim.runtimeAnimatorController != null
                              && (!anim.isHuman || (anim.avatar != null && anim.avatar.isValid));
                if (rigged) orcCtrl++;
            }

            if (found.Count == 0)
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: NO arena enemies spawned - the open-arena family never materialised.");
            else if (skinned < found.Count)
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: " + (found.Count - skinned) + "/" + found.Count + " orcs fell back to a CAPSULE (model failed to load).");
            else if (orcCtrl < found.Count)
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: " + (found.Count - orcCtrl) + "/" + found.Count + " arena enemies lack a valid animator (no controller, or Humanoid with an invalid avatar — would T-pose).");
            else
                FlowTrace.Step(Tag, "AssertEncounterRealPath: " + found.Count + " arena enemies spawned, all skinned + validly rigged.");

            // Force the WIN path deterministically (headless can't drive hero attacks).
            foreach (var e in found)
                if (e != null) { try { e.Kill(); } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertEncounterRealPath: Kill threw " + ex.Message); } }

            // Wait for resolution (BattleInProgress -> false).
            float wait = 0f;
            while (arena.BattleInProgress && wait < 15f) { wait += Time.deltaTime; yield return null; }
            bool resolved = !arena.BattleInProgress;
            if (!resolved)
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: battle did NOT resolve within 15s after the family died - loop stuck.");

            // Assert the hero returned to the engagement spot (where it touched the rep).
            float dist = (_hero != null) ? Vector3.Distance(_hero.transform.position, onRep) : 999f;
            bool heroReturn = dist <= 5f;
            if (!heroReturn)
                FlowTrace.Fail(Tag, "AssertEncounterRealPath: hero NOT returned (dist " + dist.ToString("F1") + "m from engagement spot).");

            bool pass = repSpawned && droppedToBattle && found.Count > 0 && skinned == found.Count && orcCtrl == found.Count && resolved && heroReturn;
            _lastDetail = "repSpawned=" + repSpawned + " droppedToBattle=" + droppedToBattle +
                          " spawned=" + found.Count + " skinned=" + skinned + " orcRig=" + orcCtrl +
                          " resolved=" + resolved + " heroReturn=" + dist.ToString("F1") + "m -> " + (pass ? "PASS" : "FAIL");
            FlowTrace.Step(Tag, "AssertEncounterRealPath: " + _lastDetail);

            RestoreEncounterFlag(prevFlag);
        }

        // Find a live overworld rep object (named "OrcRep_*" by the spawner's SpawnRep).
        private static GameObject FindRepObject()
        {
            foreach (var e in UnityEngine.Object.FindObjectsByType<DeNelle.Village.Enemy>())
                if (e != null && e.gameObject.name.StartsWith("OrcRep_")) return e.gameObject;
            return null;
        }

        private static void RestoreEncounterFlag(int prev)
        {
            if (prev < 0) PlayerPrefs.DeleteKey("ff.overworldencounter");
            else PlayerPrefs.SetInt("ff.overworldencounter", prev);
        }

        // =====================================================================
        //  AssertDungeonLoop — DUNGEON_LOOP_PROBE (owner 2026-08-05, F8 night)
        // ---------------------------------------------------------------------
        //  WHY THIS EXISTS: to prove the 219924ca combat-components fix the owner
        //  had to WALK HER OWN HERO into the Healer's Cottage. She is the product
        //  owner, not the tester (memory never-dragdrop-or-manual-playtest). This
        //  phase performs that EXACT hand sequence headless:
        //     hub -> resolve the portal AT RUNTIME -> tap the REAL Interact prompt
        //     -> reach a scripted encounter -> win it -> survive the post-victory
        //     settle -> try to WALK -> return to the hub.
        //
        //  FIVE ASSERTIONS, each with its OWN Fail text so a failure names itself:
        //    A  HERO IS COMBAT-CAPABLE. The staged hero carries BOTH
        //       PlayerAttackController AND HeroHealth. Before 219924ca the composed
        //       Keeper had NEITHER — it could not damage or be damaged, so the fight
        //       could never resolve and the player was softlocked. Mirrors the
        //       proving line "[Flow:HeroEnsure] combat components ensured on
        //       'Keeper' ... attack=... health=..." (HeroControlEnsurer.cs:469).
        //       Checked TWICE: on dungeon entry (A1) and at arena staging (A2).
        //    B  HERO IS ON THE NAVMESH AFTER THE FIGHT. TONIGHT'S LIVE BUG:
        //         [Flow:Seam] WarpTo sample MISS for (-28.00,0.08,0.00)
        //                     (no navmesh within 5m) - hero will land OFF-MESH.
        //         [Flow:Seam] WarpTo post-warp: agent.isOnNavMesh=False
        //       B1 = the agent was OFF-MESH when the victory warp landed (the
        //       captured signature). B2 = after the settle NEITHER mover is live
        //       (agent disabled AND CharacterController disabled) -> pinned hero.
        //    C  HERO CAN ACTUALLY MOVE. Stronger than B and the thing the owner
        //       FELT ("could not move at all"): drive the REAL player input seam
        //       (DeNelle.HUD.Kit.HudMoveInput — the on-screen D-pad, read by BOTH
        //       movers: HeroLocomotion in the hub, DungeonHero.SampleKitDpadMove in
        //       the dungeon) in four directions and require a real position delta.
        //       An on-mesh but PINNED hero passes B and fails here.
        //    D  THE SCENE IS NOT BLACK AFTER THE FIGHT. Also live tonight: the
        //       arena stage prefab carries a scene-wide Directional light
        //       ("KeyLight", ArenaPrefabBuilder.cs:174) that lit the whole dungeon
        //       during the fight and died with the stage, while ApplyCavernMood
        //       overwrote global ambient and RestoreCavernMood dropped it ~20x
        //       (BattleArena.cs:1142/1165). Compares a PRE-fight sample against a
        //       POST-fight sample — never hard-coded values, so an authored lighting
        //       change cannot produce a false red. D1 = 'KeyLight' is the active sun
        //       in a dungeon scene. D2 = ambient did not come back. D3 = every
        //       directional light died (literally black).
        //    E  NO DUPLICATE POSE WRITERS. Tonight THREE positions appeared in one
        //       settle window — (50,0,50) with no WarpTo line, the warp target
        //       (-28,0.08,0), and the sampled (-24.2,7.1). The hero must land where
        //       the ARENA claims (EncounterParams.ReturnPosition) or where the
        //       DUNGEON left him (his pre-fight pose). Landing at neither means a
        //       third system writes the pose. All four poses are logged either way,
        //       so the next capture names the writer even when this passes.
        //
        //  ASSEMBLY CONSTRAINT (report-worthy): DeNelle.DevTools references
        //  DeNelle.Village/Core/HUD but NOT DeNelle.Dungeons, so DungeonController /
        //  DungeonHero / EncounterTrigger / DungeonRuntimeState CANNOT be named here.
        //  Everything this probe drives goes through Village-side public seams
        //  (DungeonPortal, MobileInteractButton, BattleArena, HeroLocomotion) plus
        //  UnityEngine primitives; the one Dungeons-side object it must FIND (the
        //  scripted encounter trigger) is located by GetType().Name — no
        //  System.Reflection, no member invocation (§10 stays clean).
        //
        //  RUN IT (the fleet cannot reach a dungeon on a full sweep — the 420s
        //  GlobalCapSeconds expires long before this phase):
        //    powershell -ExecutionPolicy Bypass -File .\run-autopilot-fleet.ps1 `
        //        -Count 2 -TimeoutMin 10 -Phases DungeonLoop
        //  Add -Graphics ONLY if a frame is wanted; this probe asserts no pixels, and
        //  a -Graphics run rewrites ui-shots (canon: no -Graphics => flat-black frames
        //  that overwrite real captures).
        // =====================================================================
        private const float DungeonMoveDeltaMeters = 0.5f;   // C: "actually moved" floor
        private const float DungeonPoseToleranceMeters = 6f; // E: how close counts as "landed where claimed"

        private IEnumerator AssertDungeonLoop()
        {
            const string Tag = "Auto";
            string hubScene = ActiveScene();

            EnsureHero("AssertDungeonLoop");
            if (_hero == null)
            {
                _lastDetail = "no hero - skipped";
                FlowTrace.Warn(Tag, "AssertDungeonLoop: no hero - skipped (EnsureHero named the reason above).");
                yield break;
            }

            // ── link 0: RESOLVE THE PORTAL AT RUNTIME ────────────────────────
            // NEVER hard-code (20.00,0.14,-140.00): DungeonWorldPortalSpawner seats the arch
            // off an authored table PLUS a navmesh search (and the header there already warns
            // "if a headless run reports navmesh-seated=False, retune to (16,0,-140)"). A
            // hard-coded probe would walk to empty ground after any retune and prove nothing.
            DungeonPortal portal = null;
            float portalWait = 0f;
            while (portalWait < 12f)
            {
                portal = PickDungeonPortal(_hero.transform.position);
                if (portal != null) break;
                portalWait += Time.deltaTime;
                yield return null;
            }
            if (portal == null)
            {
                _lastDetail = "no DungeonPortal in world - N/A (skipped)";
                FlowTrace.Warn(Tag, $"AssertDungeonLoop: no DungeonPortal component anywhere in scene '{hubScene}' within 12s " +
                    "— DungeonWorldPortalSpawner never seated an arch (no baked navmesh / every authored seat rejected). " +
                    "Read its [Flow:DungeonPortals] lines in this run; the dungeon loop is UNREACHABLE on foot this session.");
                yield break;
            }
            Vector3 portalPos = portal.transform.position;
            FlowTrace.Step(Tag, $"AssertDungeonLoop: link 0 PASS — resolved portal '{portal.gameObject.name}' at {portalPos} " +
                $"(hero {HorizontalDistance(_hero.transform.position, portalPos):0.0}m away, scene '{hubScene}').");

            // ── link 1: REACH THE PORTAL ON FOOT ─────────────────────────────
            // Real navigation first (SetAutoWalk — the same seam WalkToEachGate uses). The walk
            // is NOT what this probe is testing, so a stall degrades to a NAMED warp assist
            // rather than failing the whole dungeon loop.
            _hero.SetAutoWalk(portal.transform);
            float walk = 0f;
            bool walked = false;
            while (walk < 55f)
            {
                if (_hero == null) break;
                if (HorizontalDistance(_hero.transform.position, portalPos) <= 2.6f) { walked = true; break; }
                walk += Time.deltaTime;
                yield return null;
            }
            if (_hero != null) _hero.ClearAutoWalk();
            if (!walked)
            {
                if (_hero == null)
                {
                    _lastDetail = "hero destroyed during the portal walk";
                    FlowTrace.Fail(Tag, "AssertDungeonLoop: FAIL at link 1 — the hero was DESTROYED while walking to the portal.");
                    yield break;
                }
                FlowTrace.Warn(Tag, $"AssertDungeonLoop: link 1 DEGRADED — did not reach '{portal.gameObject.name}' on foot within 55s " +
                    $"(stopped {HorizontalDistance(_hero.transform.position, portalPos):0.0}m short; navmesh gap / blocked path — " +
                    "AutoPilotProbes SEAM-REACHABLE covers that class). Warping to the arch so the ENTRY assertions still run.");
                Vector3 near = portalPos;
                if (UnityEngine.AI.NavMesh.SamplePosition(portalPos, out var ph, 8f, UnityEngine.AI.NavMesh.AllAreas))
                    near = ph.position;
                try { _hero.WarpTo(near); } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertDungeonLoop: portal WarpTo threw " + ex.Message); }
                for (int i = 0; i < 3; i++) yield return null;
            }
            else
                FlowTrace.Step(Tag, $"AssertDungeonLoop: link 1 PASS — reached the portal ON FOOT in {walk:0.0}s (no teleport).");

            // ── link 2: ENTER THROUGH THE REAL INTERACT TAP ──────────────────
            // WO-777 removed the walk-in auto-route: MobileInteractButton.Request ->
            // InvokeActive() is the SOLE entry path a player has. Drive exactly that.
            bool tapped = false;
            float promptWait = 0f;
            while (promptWait < 6f)
            {
                if (MobileInteractButton.IsShowingFor(portal))
                {
                    tapped = MobileInteractButton.InvokeActive();
                    if (tapped) break;
                }
                promptWait += Time.deltaTime;
                yield return null;
            }
            if (tapped)
                FlowTrace.Step(Tag, "AssertDungeonLoop: link 2 — fired the REAL shared Interact prompt (MobileInteractButton.InvokeActive), the player's only entry path.");
            else
            {
                // NAMED degradation, never silent: the entry SEAM is then uncovered by this run,
                // but the post-entry assertions (the P0 + tonight's bugs) still get their data.
                string fallbackScene = ResolveDungeonSceneName(portal.gameObject.name);
                if (string.IsNullOrEmpty(fallbackScene))
                {
                    _lastDetail = "Interact prompt never armed AND no loadable dungeon scene";
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL at link 2 — the shared Interact prompt never armed for '{portal.gameObject.name}' " +
                        "within 6s at the arch AND no 'Dungeon_*' scene could be resolved from its name. The portal is DEAD: a player standing " +
                        "at this arch has no way in (read the [Flow:DungeonPortal] lines).");
                    yield break;
                }
                FlowTrace.Warn(Tag, $"AssertDungeonLoop: link 2 DEGRADED — the shared Interact prompt never armed within 6s at the arch " +
                    $"(MobileInteractButton could not build/observe a request headless). The ENTRY SEAM IS NOT COVERED BY THIS RUN. " +
                    $"Falling back to a direct load of '{fallbackScene}' so the post-entry assertions (A/B/C/D/E) still capture data.");
                try { SceneManager.LoadScene(fallbackScene); }
                catch (Exception ex) { FlowTrace.Fail(Tag, $"AssertDungeonLoop: fallback LoadScene('{fallbackScene}') threw — {ex.Message}"); yield break; }
            }

            float loadWait = 0f;
            while (loadWait < 30f && !IsDungeonScene(ActiveScene())) { loadWait += Time.deltaTime; yield return null; }
            string dungeonScene = ActiveScene();
            if (!IsDungeonScene(dungeonScene))
            {
                _lastDetail = $"never entered a dungeon (still '{dungeonScene}')";
                FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL at link 2 — 30s after the entry tap the active scene is STILL '{dungeonScene}'; " +
                    "no dungeon ever loaded (SceneRouter.LoadSceneWithFade never completed / the target is not in Build Settings).");
                yield break;
            }
            FlowTrace.Step(Tag, $"AssertDungeonLoop: link 2 PASS — inside dungeon scene '{dungeonScene}' after {loadWait:0.0}s.");

            // Re-resolve the hero: the hub HeroLocomotion was destroyed with the hub, and the
            // composed dungeon Keeper is injected a beat after the scene becomes active.
            _hero = null;
            float heroWait = 0f;
            while (heroWait < 20f && _hero == null)
            {
                _hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                if (_hero != null) break;
                heroWait += Time.deltaTime;
                yield return null;
            }
            if (_hero == null)
            {
                _lastDetail = $"no hero in '{dungeonScene}'";
                FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL — entered '{dungeonScene}' but no HeroLocomotion existed within 20s. " +
                    "The dungeon never provisioned a Keeper; the run is unplayable from the first frame.");
                yield return ReturnToHub(hubScene);
                yield break;
            }
            GameObject heroGo = _hero.gameObject;
            FlowTrace.Step(Tag, $"AssertDungeonLoop: dungeon hero '{heroGo.name}' resolved at {heroGo.transform.position} after {heroWait:0.1}s.");

            // ── ASSERTION A1: COMBAT-CAPABLE ON ENTRY ────────────────────────
            // HeroControlEnsurer attaches both from a polling Watch loop, so give it a window
            // before concluding. This is the 219924ca P0: without them the Keeper could neither
            // damage nor be damaged and the fight could never resolve.
            bool hasAttack = false, hasHealth = false;
            float combatWait = 0f;
            while (combatWait < 10f)
            {
                hasAttack = heroGo.GetComponent<PlayerAttackController>() != null;
                hasHealth = heroGo.GetComponent<HeroHealth>() != null;
                if (hasAttack && hasHealth) break;
                combatWait += Time.deltaTime;
                yield return null;
            }
            bool failA1 = !(hasAttack && hasHealth);
            if (failA1)
                FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL A1 — the dungeon hero '{heroGo.name}' is NOT COMBAT-CAPABLE on entry after 10s: " +
                    $"attack={(hasAttack ? "present" : "MISSING")} health={(hasHealth ? "present" : "MISSING")}. This is the 219924ca P0 REGRESSED — " +
                    "the hero stages into the arena unable to damage or be damaged, the fight can never resolve, and the player is SOFTLOCKED. " +
                    "The proving line that should be in this run is \"[Flow:HeroEnsure] combat components ensured on '" + heroGo.name + "' ... attack=... health=...\".");
            else
                FlowTrace.Step(Tag, $"AssertDungeonLoop: A1 PASS — '{heroGo.name}' carries PlayerAttackController + HeroHealth on entry (219924ca holds).");

            // PRE-FIGHT baselines for D (lighting) and E (pose ownership). Sampled on the FIRST
            // frame the hero exists, because Healer's Cottage spawns him at (-28,0,0) — which is
            // ALSO garden-hollow-one's triggerPosition (healers-cottage.json), so the first
            // encounter can fire before this probe takes a single step. If a fight is already
            // live these baselines are contaminated by the arena, so we record that and soften
            // the comparisons rather than reporting a false red.
            var preArena = DeNelle.Village.Arena.BattleArena.Existing;
            bool preSampleClean = !(preArena != null && preArena.BattleInProgress);
            var preLight = SampleLighting();
            Vector3 dungeonPose = heroGo.transform.position;
            bool dungeonPoseValid = preSampleClean;
            FlowTrace.Step(Tag, $"AssertDungeonLoop: PRE-FIGHT sample (clean={preSampleClean}) — {preLight}; " +
                $"dungeon-claimed hero pose {dungeonPose}" +
                (preSampleClean ? "." : " — A FIGHT WAS ALREADY LIVE on the first hero frame (the spawn point IS an encounter trigger), " +
                                        "so the D baseline is arena-contaminated and E falls back to the arena's EncounterParams.ReturnPosition, which carries the true pre-fight pose."));

            if (dungeonScene.StartsWith("dg_", StringComparison.OrdinalIgnoreCase))
            {
                yield return AssertComposedDungeonRuntime(dungeonScene, heroGo, failA1, tapped);
                yield return ReturnToHub(hubScene);
                yield break;
            }

            // ── link 4: REACH A SCRIPTED ENCOUNTER ───────────────────────────
            // Healer's Cottage authors 4 scripted encounters + a mini-boss. Their triggers live
            // in DeNelle.Dungeons (unreferenced here), so locate them by GetType().Name and warp
            // into proximity — the trigger fires on a DISTANCE check in its own Update
            // (EncounterTrigger.TickScripted), NOT on a physics OnTriggerEnter, so a warp into
            // range drives the REAL fire path with no bypass.
            var triggers = FindEncounterTriggers(heroGo.transform.position);
            if (triggers.Count == 0)
            {
                _lastDetail = $"no encounter triggers in '{dungeonScene}'";
                FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL at link 4 — '{dungeonScene}' contains ZERO EncounterTrigger objects. " +
                    "The dungeon authored no scripted encounters, or DungeonController never hydrated them from the layout " +
                    "(read its \"scriptedEncounters=N, miniBoss=...\" line in this run). There is nothing to fight.");
                yield return ReturnToHub(hubScene);
                yield break;
            }
            FlowTrace.Step(Tag, $"AssertDungeonLoop: link 4 — {triggers.Count} encounter trigger(s) found; walking the list nearest-first.");

            // Capture the settle-entry facts the instant the arena reports the fight over.
            var arena = DeNelle.Village.Arena.BattleArena.Instance;
            bool endedFired = false, endedWon = false;
            Vector3 arenaClaimPose = Vector3.zero;
            bool arenaClaimValid = false;
            Vector3 settleEntryPose = Vector3.zero;
            bool entryAgentEnabled = false, entryOnNavMesh = false;
            Action<DeNelle.Village.Arena.EncounterParams, bool> onEnded = null;
            onEnded = (p, won) =>
            {
                if (endedFired) return;
                endedFired = true;
                endedWon = won;
                if (p != null) { arenaClaimPose = p.ReturnPosition; arenaClaimValid = true; }
                if (heroGo != null)
                {
                    settleEntryPose = heroGo.transform.position;
                    var ag = heroGo.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    entryAgentEnabled = ag != null && ag.enabled;
                    entryOnNavMesh = entryAgentEnabled && ag.isOnNavMesh;
                }
                FlowTrace.Step(Tag, $"AssertDungeonLoop: SETTLE ENTRY — OnBattleEnded(won={won}) arenaClaim={(arenaClaimValid ? arenaClaimPose.ToString() : "<none>")} " +
                    $"heroPose={settleEntryPose} agentEnabled={entryAgentEnabled} agentOnNavMesh={entryOnNavMesh}.");
            };

            bool dropped = false;
            bool failA2 = false;
            float moveSweepBest = 0f;
            var postLight = default(LightingSample);
            Vector3 settleExitPose = Vector3.zero;
            bool failB1 = false, failB2 = false, failC = false, failD1 = false, failD2 = false, failD3 = false, failE = false;
            bool returned = false, resolved = false;
            float returnSeconds = 0f;
            string moverRegime = "unknown";

            if (arena == null)
            {
                _lastDetail = "BattleArena.Instance NULL";
                FlowTrace.Fail(Tag, "AssertDungeonLoop: FAIL at link 4 — BattleArena.Instance was NULL inside the dungeon; " +
                    "the real-time combat host never bootstrapped, so no encounter can ever stage.");
                yield return ReturnToHub(hubScene);
                yield break;
            }

            arena.OnBattleEnded += onEnded;
            try
            {
                // The spawn point IS an encounter trigger in Healer's Cottage, so the fight may
                // already be staging before we take a step. That is the REAL path firing on its
                // own — accept it rather than warping the hero out of a live encounter.
                if (arena.BattleInProgress)
                {
                    dropped = true;
                    FlowTrace.Step(Tag, "AssertDungeonLoop: link 4 PASS (immediate) — a scripted encounter had ALREADY fired by the time the hero resolved " +
                        "(the dungeon's spawn point sits inside the first trigger's radius); no warp needed, the real fire path ran on its own.");
                }

                // Warp onto each trigger in turn until one fires.
                foreach (var trig in triggers)
                {
                    if (dropped) break;
                    if (trig == null) continue;
                    Vector3 tp = trig.transform.position;
                    try { _hero.WarpTo(tp); }
                    catch (Exception ex) { FlowTrace.Warn(Tag, $"AssertDungeonLoop: WarpTo('{trig.name}') threw {ex.Message}"); }
                    FlowTrace.Step(Tag, $"AssertDungeonLoop: standing on encounter trigger '{trig.name}' @ {tp} — waiting for its own Update to fire.");

                    float fireWait = 0f;
                    while (fireWait < 6f)
                    {
                        if (arena.BattleInProgress) { dropped = true; break; }
                        fireWait += Time.deltaTime;
                        yield return null;
                    }
                    if (dropped) break;
                    FlowTrace.Warn(Tag, $"AssertDungeonLoop: trigger '{trig.name}' did not fire within 6s (already fired this run / combat locked / run not active) — trying the next.");
                }

                if (!dropped)
                {
                    _lastDetail = $"no encounter fired ({triggers.Count} trigger(s) tried)";
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL at link 4 — stood on ALL {triggers.Count} encounter trigger(s) and BattleInProgress never became true. " +
                        "The dungeon's scripted-encounter chain is DEAD (run never went active / combat lock stuck / LaunchBattle never reached BeginEncounter) — " +
                        "read the [Flow:Dungeon] \"EncounterTrigger.TickScripted: FIRING\" lines: their absence is the proof.");
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertDungeonLoop: link 4 PASS — a scripted encounter FIRED and the arena staged (BattleInProgress=true).");

                // ── ASSERTION A2: COMBAT-CAPABLE AT STAGING ──────────────────
                // Let the stage build/warp/spawn settle, then re-read on the LIVE staged hero
                // (a body swap during staging is exactly how the P0 could come back).
                yield return Wait(4f);
                var stagedHero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                GameObject stagedGo = stagedHero != null ? stagedHero.gameObject : heroGo;
                bool sAttack = stagedGo != null && stagedGo.GetComponent<PlayerAttackController>() != null;
                bool sHealth = stagedGo != null && stagedGo.GetComponent<HeroHealth>() != null;
                failA2 = !(sAttack && sHealth);
                if (failA2)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL A2 — the STAGED hero '{(stagedGo != null ? stagedGo.name : "<null>")}' is NOT COMBAT-CAPABLE inside the arena: " +
                        $"attack={(sAttack ? "present" : "MISSING")} health={(sHealth ? "present" : "MISSING")}. This is the exact 219924ca softlock: " +
                        "the hero cannot damage or be damaged, so the encounter can never resolve and the player is stuck in the fight forever.");
                else
                    FlowTrace.Step(Tag, $"AssertDungeonLoop: A2 PASS — staged hero '{stagedGo.name}' has PlayerAttackController + HeroHealth; this fight CAN resolve.");
                if (stagedGo != null) heroGo = stagedGo;
                if (stagedHero != null) _hero = stagedHero;

                // ── link 5: WIN THE ENCOUNTER ────────────────────────────────
                // Headless cannot drive hero attacks, so kill the staged family through the same
                // Enemy.Kill() seam AssertEncounterRealPath uses — the WIN path itself is real.
                int killed = 0;
                var staged = arena.StagedEnemies;
                if (staged != null)
                    for (int i = 0; i < staged.Count; i++)
                        if (staged[i] != null) { try { staged[i].Kill(); killed++; } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertDungeonLoop: Kill threw " + ex.Message); } }
                foreach (var e in UnityEngine.Object.FindObjectsByType<DeNelle.Village.Enemy>(FindObjectsSortMode.None))
                    if (e != null && e.gameObject.name.StartsWith("ArenaEnemy_"))
                    {
                        // §12: never swallow silently — a Kill that throws is exactly how a fight
                        // stays unwinnable, and link 5 below would then blame the win gate.
                        try { e.Kill(); killed++; }
                        catch (Exception ex) { FlowTrace.Warn(Tag, $"AssertDungeonLoop: Kill('{e.gameObject.name}') threw {ex.Message}"); }
                    }
                FlowTrace.Step(Tag, $"AssertDungeonLoop: link 5 — killed {killed} staged combatant(s); waiting for the arena to resolve.");

                float resolveWait = 0f;
                while (resolveWait < 25f && arena.BattleInProgress) { resolveWait += Time.deltaTime; yield return null; }
                resolved = !arena.BattleInProgress;
                if (!resolved)
                {
                    _lastDetail = "battle never resolved after the family died";
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL at link 5 — every staged combatant was killed but BattleInProgress stayed TRUE for 25s. " +
                        "The win gate never fired; the player is locked in a fight with nothing left to kill." +
                        (failA2 ? " (A2 already failed — a hero that cannot damage is the likely cause.)" : ""));
                    yield break;
                }
                FlowTrace.Step(Tag, $"AssertDungeonLoop: link 5 PASS — battle resolved in {resolveWait:0.0}s (won={endedWon}).");
                if (!endedWon)
                    // A DEFEAT settles down a different road: DungeonController.SettleEncounter routes
                    // ExitToVillage, so the hero LEAVES the dungeon and B/C/D/E would be measured in the
                    // hub against a dungeon baseline. Say so loudly — the assertions below still run
                    // (a hero who cannot move in the HUB is just as broken) but the scene changed under them.
                    FlowTrace.Warn(Tag, "AssertDungeonLoop: the encounter was LOST despite killing every staged combatant " +
                        "(the hero died first — HeroHealth is live, so this is possible). The dungeon settles a defeat by routing " +
                        "ExitToVillage, so the post-settle assertions below are measured wherever that landed, not in the dungeon. " +
                        "Read the verdict's scene field before treating a D/E red as a dungeon defect.");

                // ── link 6: SURVIVE THE POST-VICTORY SETTLE + RETURN ─────────
                // On a WIN the masked return is DEFERRED behind the victory summary's Continue
                // tap (~20s softlock-guard timeout headless). Poll the REAL arrival signal —
                // BattleArena.IsArenaPosition going false means the ~7km return warp landed.
                float retWait = 0f;
                while (retWait < 35f)
                {
                    if (heroGo == null) break;
                    if (!DeNelle.Village.Arena.BattleArena.IsArenaPosition(heroGo.transform.position)) { returned = true; break; }
                    retWait += Time.deltaTime;
                    yield return null;
                }
                returnSeconds = retWait;
                if (!returned)
                {
                    _lastDetail = "hero STRANDED at the far arena after the win";
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL at link 6 — 35s after the victory the hero is STILL inside the ~7km staged arena " +
                        $"({(heroGo != null ? heroGo.transform.position.ToString() : "<hero destroyed>")}). ReturnHomeWithFade never ran: the player is " +
                        "stranded in an empty void with the dungeon still loaded — an unrecoverable softlock.");
                    yield break;
                }
                FlowTrace.Step(Tag, $"AssertDungeonLoop: link 6 — return warp landed after {returnSeconds:0.0}s; letting the settle finish before asserting.");
                yield return Wait(3.5f);   // dungeon re-neutralizes its mover + RestoreCavernMood lands
                settleExitPose = heroGo != null ? heroGo.transform.position : Vector3.zero;

                // ── ASSERTION B: ON THE NAVMESH / A LIVE MOVER ───────────────
                var agent = heroGo != null ? heroGo.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
                var cc = heroGo != null ? heroGo.GetComponent<CharacterController>() : null;
                bool agentLive = agent != null && agent.enabled && agent.gameObject.activeInHierarchy;
                bool ccLive = cc != null && cc.enabled && cc.gameObject.activeInHierarchy;
                bool lateOnMesh = agentLive && agent.isOnNavMesh;
                moverRegime = agentLive ? (lateOnMesh ? "NavMeshAgent(on-mesh)" : "NavMeshAgent(OFF-MESH)")
                            : (ccLive ? "CharacterController" : "NONE");

                // B1 — the captured signature: the victory warp landed the agent OFF the mesh.
                failB1 = entryAgentEnabled && !entryOnNavMesh;
                if (failB1)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL B1 — the post-victory warp landed the hero OFF THE NAVMESH " +
                        $"(at settle entry agent.enabled=true, agent.isOnNavMesh=FALSE @ {settleEntryPose}). This is TONIGHT'S captured bug verbatim: " +
                        "\"[Flow:Seam] WarpTo sample MISS ... (no navmesh within 5m) - hero will land OFF-MESH\" followed by " +
                        "\"[Flow:Seam] WarpTo post-warp: agent.isOnNavMesh=False\". The arena is warping to a point the dungeon has no navmesh under.");
                // B2 — after the settle NOTHING can move the hero.
                failB2 = !agentLive && !ccLive;
                if (failB2)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL B2 — after the settle the hero has NO LIVE MOVER " +
                        $"(NavMeshAgent {(agent == null ? "absent" : "disabled")}, CharacterController {(cc == null ? "absent" : "disabled")}) @ {settleExitPose}. " +
                        "Both movers were stood down and neither was handed back — the hero is PINNED and nothing the player does can move him.");
                // Late off-mesh in a pure-agent regime is the same felt failure, named separately.
                if (agentLive && !lateOnMesh)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL B1b — after the settle the NavMeshAgent is ENABLED but agent.isOnNavMesh=FALSE @ {settleExitPose}. " +
                        "The agent is live on nothing: pathing silently no-ops and the hero cannot walk.");
                if (!failB1 && !failB2 && !(agentLive && !lateOnMesh))
                    FlowTrace.Step(Tag, $"AssertDungeonLoop: B PASS — a live mover owns the hero after the settle (regime={moverRegime}).");

                // ── ASSERTION C: THE HERO CAN ACTUALLY MOVE ──────────────────
                // The thing the owner FELT. Drive the REAL player input seam and require a real
                // delta. Four directions so a wall on one side cannot fake a red.
                moveSweepBest = 0f;
                Vector3 moveStart = heroGo != null ? heroGo.transform.position : Vector3.zero;
                Vector2[] dirs = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
                foreach (var d in dirs)
                {
                    DeNelle.HUD.Kit.HudMoveInput.Set(d);
                    float hold = 0f;
                    while (hold < 1.4f)
                    {
                        hold += Time.deltaTime;
                        if (heroGo != null)
                        {
                            float delta = HorizontalDistance(heroGo.transform.position, moveStart);
                            if (delta > moveSweepBest) moveSweepBest = delta;
                        }
                        yield return null;
                    }
                    DeNelle.HUD.Kit.HudMoveInput.Set(Vector2.zero);
                    yield return null;
                    if (moveSweepBest >= DungeonMoveDeltaMeters) break;   // proven; stop early
                }
                DeNelle.HUD.Kit.HudMoveInput.Set(Vector2.zero);
                failC = moveSweepBest < DungeonMoveDeltaMeters;
                if (failC)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL C — after the victory settle the hero CANNOT MOVE. Drove the real on-screen D-pad seam " +
                        $"(DeNelle.HUD.Kit.HudMoveInput — read by HeroLocomotion in town and by DungeonHero.SampleKitDpadMove in the dungeon) in 4 directions " +
                        $"for ~1.4s each and the hero travelled {moveSweepBest:0.00}m (floor {DungeonMoveDeltaMeters:0.0}m). Mover regime = {moverRegime}. " +
                        "THIS IS THE OWNER'S REPORTED SYMPTOM (\"could not move at all\") reproduced headless. " +
                        (failB1 || failB2 ? "B already named the mechanism." : "B passed, so the hero is on a live mover and STILL pinned — the input seam or DungeonHero.SetInputEnabled(true) never came back."));
                else
                    FlowTrace.Step(Tag, $"AssertDungeonLoop: C PASS — hero moved {moveSweepBest:0.00}m on real D-pad input after the settle (regime={moverRegime}).");

                // ── ASSERTION D: THE SCENE IS NOT BLACK ──────────────────────
                postLight = SampleLighting();
                // D1 — the arena's stage light became the dungeon's sun (it dies with the stage).
                failD1 = IsDungeonScene(ActiveScene()) &&
                         postLight.SunName.IndexOf("KeyLight", StringComparison.OrdinalIgnoreCase) >= 0;
                if (failD1)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL D1 — the active sun in dungeon scene '{ActiveScene()}' is '{postLight.SunName}' " +
                        $"(intensity {postLight.SunIntensity:0.00}). That is the ARENA STAGE prefab's scene-wide Directional light " +
                        "(ArenaPrefabBuilder \"KeyLight\"), not the dungeon's own Directional Light. It lit the whole dungeon during the fight and " +
                        "DIES WITH THE STAGE — the room goes black the moment the stage is destroyed.");
                // D3 — literally no directional light left.
                failD3 = preLight.EnabledDirectionals > 0 && postLight.EnabledDirectionals == 0;
                if (failD3)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL D3 — every enabled Directional light died across the fight " +
                        $"(pre={preLight.EnabledDirectionals}, post=0). The dungeon is LITERALLY BLACK after the victory.");
                // D2 — ambient did not come back to its authored value. Compared PRE vs POST, never
                // hard-coded, so an authored lighting retune cannot produce a false red.
                float ambDelta = Mathf.Max(
                    Mathf.Abs(preLight.Ambient.r - postLight.Ambient.r),
                    Mathf.Max(Mathf.Abs(preLight.Ambient.g - postLight.Ambient.g),
                              Mathf.Abs(preLight.Ambient.b - postLight.Ambient.b)));
                float intensityRatio = postLight.AmbientIntensity <= 0.0001f
                    ? (preLight.AmbientIntensity <= 0.0001f ? 1f : 999f)
                    : preLight.AmbientIntensity / postLight.AmbientIntensity;
                // Only assertable when the PRE sample was taken outside a live fight — otherwise the
                // baseline is already the arena's cavern mood and the comparison proves nothing.
                failD2 = preSampleClean && (ambDelta > 0.02f || intensityRatio > 2f || intensityRatio < 0.5f);
                if (!preSampleClean)
                    FlowTrace.Warn(Tag, $"AssertDungeonLoop: D2 NOT ASSERTED — the pre-fight ambient baseline was captured while a fight was already live, " +
                        $"so it is the arena's cavern mood, not the dungeon's authored value. Census only: PRE {preLight} | POST {postLight} " +
                        $"(max channel delta {ambDelta:0.000}, intensity pre/post {intensityRatio:0.00}x). D1/D3 still assert.");
                if (failD2)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL D2 — post-fight ambient does NOT match the dungeon's authored pre-fight ambient. " +
                        $"PRE {preLight} vs POST {postLight} (max channel delta {ambDelta:0.000}, intensity pre/post {intensityRatio:0.00}x). " +
                        "BattleArena.ApplyCavernMood overwrites RenderSettings.ambientLight/ambientIntensity for the fight and RestoreCavernMood " +
                        "must put the dungeon's own values back — it did not.");
                if (!failD1 && !failD2 && !failD3)
                    FlowTrace.Step(Tag, $"AssertDungeonLoop: D PASS — lighting came back to the dungeon's authored values. PRE {preLight} | POST {postLight}.");

                // ── ASSERTION E: NO DUPLICATE POSE WRITERS ───────────────────
                float dArena = arenaClaimValid ? HorizontalDistance(settleExitPose, arenaClaimPose) : float.MaxValue;
                float dDungeon = dungeonPoseValid ? HorizontalDistance(settleExitPose, dungeonPose) : float.MaxValue;
                // Never fail E on no evidence: if NEITHER claim is trustworthy this is a census, not a verdict.
                bool eAssertable = arenaClaimValid || dungeonPoseValid;
                failE = eAssertable && dArena > DungeonPoseToleranceMeters && dDungeon > DungeonPoseToleranceMeters;
                // ALWAYS log all four poses — on a pass this is the census that names the writer
                // next time; on a fail it is the evidence.
                FlowTrace.Step(Tag, $"AssertDungeonLoop: POSE CENSUS — dungeonClaim(pre-fight)={(dungeonPoseValid ? dungeonPose.ToString() : "<contaminated: fight already live>")} " +
                    $"arenaClaim(EncounterParams.ReturnPosition)={(arenaClaimValid ? arenaClaimPose.ToString() : "<no params>")} " +
                    $"settleEntry={settleEntryPose} settleExit={settleExitPose} | dToArena={(arenaClaimValid ? dArena.ToString("0.00") : "n/a")}m dToDungeon={(dungeonPoseValid ? dDungeon.ToString("0.00") : "n/a")}m " +
                    $"| entryAgentEnabled={entryAgentEnabled} entryOnNavMesh={entryOnNavMesh} regime={moverRegime}.");
                if (failE)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: FAIL E — the hero settled at {settleExitPose}, which NEITHER system claims: " +
                        $"the arena claims {(arenaClaimValid ? arenaClaimPose.ToString() : "<no EncounterParams>")} ({(arenaClaimValid ? dArena.ToString("0.00") + "m away" : "unknown")}) and the dungeon left him at " +
                        $"{(dungeonPoseValid ? dungeonPose.ToString() + " (" + dDungeon.ToString("0.00") + "m away)" : "<pre-fight pose contaminated>")}, tolerance {DungeonPoseToleranceMeters:0}m. " +
                        "A THIRD system is writing the hero pose inside the settle window " +
                        "(tonight three positions appeared in one window: (50,0,50) with NO WarpTo line, the warp target (-28,0.08,0), and the sampled (-24.2,7.1)). " +
                        "Cross the POSE CENSUS line above with the [Flow:Seam] WarpTo lines and the [Flow:Dungeon] settle lines in this run — the pose with no WarpTo line names the culprit.");
                else if (!eAssertable)
                    FlowTrace.Warn(Tag, "AssertDungeonLoop: E NOT ASSERTED — neither the arena (no EncounterParams on OnBattleEnded) nor the dungeon " +
                        "(pre-fight pose contaminated by an already-live fight) produced a trustworthy claim. The POSE CENSUS above is still captured in full, " +
                        "so the next run's trace can still name a third pose writer.");
                else
                    FlowTrace.Step(Tag, $"AssertDungeonLoop: E PASS — the hero settled where a system claims him " +
                        $"({(dArena <= dDungeon ? "arena return position" : "dungeon pre-fight pose")}, {Mathf.Min(dArena, dDungeon):0.00}m).");
            }
            finally
            {
                arena.OnBattleEnded -= onEnded;
                DeNelle.HUD.Kit.HudMoveInput.Set(Vector2.zero);
            }

            // ── MARKER + summary row ─────────────────────────────────────────
            bool pass = !failA1 && !failA2 && !failB1 && !failB2 && !failC && !failD1 && !failD2 && !failD3 && !failE
                        && dropped && resolved && returned;
            string verdict =
                $"DUNGEON_LOOP_PROBE :: dungeon='{dungeonScene}' sceneAtVerdict='{ActiveScene()}' " +
                $"entered={(tapped ? "real-tap" : "fallback-load")} won={endedWon} baselineClean={preSampleClean} " +
                $"A_combatCapable={(failA1 || failA2 ? "FAIL" : "PASS")} " +
                $"B_onNavMesh={(failB1 || failB2 ? "FAIL" : "PASS")} " +
                $"C_canMove={(failC ? "FAIL" : "PASS")}(delta={moveSweepBest:0.00}m) " +
                $"D_notBlack={(failD1 || failD2 || failD3 ? "FAIL" : "PASS")} " +
                $"E_singlePoseWriter={(failE ? "FAIL" : "PASS")} " +
                $"regime={moverRegime} returnSeconds={returnSeconds:0.0} verdict={(pass ? "PASS" : "FAIL")}";
            FlowTrace.Step(Tag, verdict);
            _lastDetail = verdict;

            // Leave the run where it found it so downstream phases are unaffected.
            yield return ReturnToHub(hubScene);
        }

        private IEnumerator AssertComposedDungeonRuntime(string dungeonScene, GameObject heroGo,
                                                         bool entryCombatFailed, bool tapped)
        {
            const string Tag = "Auto";
            yield return Wait(2f);

            var spawners = UnityEngine.Object.FindObjectsByType<OutpostEnemyGroupSpawner>(
                FindObjectsSortMode.None);
            int sceneSpawners = 0, unbound = 0, living = 0;
            OutpostEnemyGroupSpawner boss = null;
            foreach (var spawner in spawners)
            {
                if (spawner == null || spawner.gameObject.scene.name != dungeonScene) continue;
                sceneSpawners++;
                if (!spawner.HasRoomArea) unbound++;
                if (spawner.IsBossGroup) boss = spawner;
                foreach (var enemy in spawner.LiveEnemies)
                    if (enemy != null) living++;
            }

            bool expectsCombat = dungeonScene != "dg_hollow_roads";
            bool expectsBoss = dungeonScene == "dg_sunken_vault" ||
                               dungeonScene == "dg_bonecrypt" ||
                               dungeonScene == "dg_ember_deep";
            bool failRooms = unbound > 0 || (expectsCombat && (sceneSpawners == 0 || living == 0));
            bool failBoss = expectsBoss && boss == null;
            if (failRooms)
                FlowTrace.Fail(Tag, $"AssertDungeonLoop: COMPOSED FAIL rooms/spawns scene='{dungeonScene}' " +
                    $"spawners={sceneSpawners} unbound={unbound} living={living}");
            else
                FlowTrace.Step(Tag, $"AssertDungeonLoop: COMPOSED rooms/spawns PASS scene='{dungeonScene}' " +
                    $"spawners={sceneSpawners} living={living} allRoomBound=true");
            if (failBoss)
                FlowTrace.Fail(Tag, $"AssertDungeonLoop: COMPOSED FAIL '{dungeonScene}' has no live authored boss spawner");

            bool bossCleared = !expectsBoss;
            if (boss != null)
            {
                int killed = 0;
                var snapshot = new List<Enemy>(boss.LiveEnemies);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (snapshot[i] == null) continue;
                    snapshot[i].Kill();
                    killed++;
                }
                float wait = 0f;
                while (wait < 5f && !boss.IsBossCleared) { wait += Time.deltaTime; yield return null; }
                bossCleared = boss.IsBossCleared;
                if (!bossCleared)
                    FlowTrace.Fail(Tag, $"AssertDungeonLoop: COMPOSED FAIL boss clear signal did not fire after {killed} boss kill(s)");
                else
                    FlowTrace.Step(Tag, $"AssertDungeonLoop: COMPOSED boss lifecycle PASS killed={killed} clearSignal=true");
            }

            float moveBest = 0f;
            Vector3 start = heroGo != null ? heroGo.transform.position : Vector3.zero;
            Vector2[] dirs = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            foreach (var dir in dirs)
            {
                DeNelle.HUD.Kit.HudMoveInput.Set(dir);
                float hold = 0f;
                while (hold < 1.4f)
                {
                    hold += Time.deltaTime;
                    if (heroGo != null)
                        moveBest = Mathf.Max(moveBest, HorizontalDistance(heroGo.transform.position, start));
                    yield return null;
                }
                DeNelle.HUD.Kit.HudMoveInput.Set(Vector2.zero);
                if (moveBest >= DungeonMoveDeltaMeters) break;
            }
            DeNelle.HUD.Kit.HudMoveInput.Set(Vector2.zero);
            bool failMove = moveBest < DungeonMoveDeltaMeters;
            if (failMove)
                FlowTrace.Fail(Tag, $"AssertDungeonLoop: COMPOSED FAIL hero moved only {moveBest:0.00}m on real D-pad input");

            bool pass = !entryCombatFailed && !failRooms && !failBoss && bossCleared && !failMove;
            string verdict = $"DUNGEON_LOOP_PROBE :: dungeon='{dungeonScene}' entered=" +
                $"{(tapped ? "real-tap" : "fallback-load")} mode=composed spawners={sceneSpawners} " +
                $"living={living} roomOwnership={(failRooms ? "FAIL" : "PASS")} " +
                $"bossLifecycle={(bossCleared && !failBoss ? "PASS" : "FAIL")} " +
                $"movement={(failMove ? "FAIL" : "PASS")}(delta={moveBest:0.00}m) verdict={(pass ? "PASS" : "FAIL")}";
            FlowTrace.Step(Tag, verdict);
            _lastDetail = verdict;
        }

        // Return the bot to the hub after the dungeon excursion so the phases queued behind
        // this one (and WriteSummary) see the scene they expect. Mirrors the popup-recovery reload.
        private IEnumerator ReturnToHub(string hubScene)
        {
            string target = string.IsNullOrEmpty(hubScene) ? TargetScene : hubScene;
            if (ActiveScene() == target) yield break;
            FlowTrace.Step("Auto", $"AssertDungeonLoop: returning to hub '{target}' so downstream phases run in the scene they expect.");
            try { SceneManager.LoadScene(target); }
            catch (Exception ex) { FlowTrace.Warn("Auto", $"AssertDungeonLoop: hub reload threw {ex.Message}"); yield break; }

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < BootTimeout && ActiveScene() != target) yield return null;
            _hero = null;                       // force EnsureHero to re-resolve the hub hero
            EnsureHero("AssertDungeonLoop.return");
        }

        // Pick the dungeon portal to drive. Prefers the Healer's Cottage arch (the owner's
        // sequence; DungeonWorldPortalSpawner names its roots "DungeonWorldPortal_<id>"), then
        // any other authored portal, nearest first. RESOLVED AT RUNTIME on purpose — the seat
        // comes from an authored table plus a navmesh search, so a retune must not break the probe.
        private static DungeonPortal PickDungeonPortal(Vector3 heroPos)
        {
            var all = UnityEngine.Object.FindObjectsByType<DungeonPortal>(FindObjectsSortMode.None);
            if (all == null || all.Length == 0) return null;
            string requested = CommandLineValue("--dungeon=");
            DungeonPortal preferred = null, nearest = null;
            float bestDist = float.MaxValue;
            foreach (var p in all)
            {
                if (p == null) continue;
                if (!string.IsNullOrEmpty(requested) &&
                    p.gameObject.name.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
                if (preferred == null && p.gameObject.name.IndexOf("Healer", StringComparison.OrdinalIgnoreCase) >= 0)
                    preferred = p;
                float d = HorizontalDistance(heroPos, p.transform.position);
                if (d < bestDist) { bestDist = d; nearest = p; }
            }
            return preferred != null ? preferred : nearest;
        }

        private static string CommandLineValue(string prefix)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] != null && args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return args[i].Substring(prefix.Length);
            return string.Empty;
        }

        // Resolve the scene a portal routes to from its GameObject name, replicating
        // DungeonPortal.EnterDungeon's own resolution (verbatim id first, then the legacy
        // "Dungeon_" prefix). Only used by the NAMED link-2 fallback. "" when nothing loads.
        private static string ResolveDungeonSceneName(string portalObjectName)
        {
            const string Prefix = "DungeonWorldPortal_";
            string id = portalObjectName != null && portalObjectName.StartsWith(Prefix, StringComparison.Ordinal)
                ? portalObjectName.Substring(Prefix.Length)
                : portalObjectName;
            if (string.IsNullOrEmpty(id)) return string.Empty;
            try
            {
                if (Application.CanStreamedLevelBeLoaded(id)) return id;
                string prefixed = "Dungeon_" + id;
                if (Application.CanStreamedLevelBeLoaded(prefixed)) return prefixed;
            }
            catch (Exception ex) { FlowTrace.Warn("Auto", "AssertDungeonLoop: scene-name resolve threw " + ex.Message); }
            return string.Empty;
        }

        // A dungeon scene by naming convention: the legacy authored form ("Dungeon_HealersCottage")
        // and the composed GraphDungeonComposer form ("dg_starter_loop").
        private static bool IsDungeonScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return sceneName.StartsWith("Dungeon_", StringComparison.OrdinalIgnoreCase)
                || sceneName.StartsWith("dg_", StringComparison.OrdinalIgnoreCase)
                || sceneName.IndexOf("Dungeon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Locate the dungeon's scripted/boss encounter triggers WITHOUT naming the type:
        // EncounterTrigger lives in DeNelle.Dungeons, which DeNelle.DevTools does NOT reference.
        // GetType().Name only — no System.Reflection, no member invocation (§10 stays clean).
        // Nearest-first so the probe fights the encounter the hero would actually walk into.
        private static List<MonoBehaviour> FindEncounterTriggers(Vector3 heroPos)
        {
            var hits = new List<MonoBehaviour>();
            try
            {
                foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (mb == null) continue;
                    string n = mb.GetType().Name;
                    if (string.Equals(n, "EncounterTrigger", StringComparison.Ordinal) ||
                        string.Equals(n, "DungeonStubEncounter", StringComparison.Ordinal))
                        hits.Add(mb);
                }
                hits.Sort((a, b) => HorizontalDistance(heroPos, a.transform.position)
                    .CompareTo(HorizontalDistance(heroPos, b.transform.position)));
            }
            catch (Exception ex) { FlowTrace.Warn("Auto", "AssertDungeonLoop: encounter-trigger scan threw " + ex.Message); }
            return hits;
        }

        /// <summary>
        /// DUNGEON_LOOP_PROBE assertion-D unit: what the scene's lighting looks like right now.
        /// Sampled PRE-fight and POST-settle and compared to each other — never to hard-coded
        /// numbers — so an authored lighting change cannot produce a false red.
        /// </summary>
        private struct LightingSample
        {
            public Color Ambient;
            public float AmbientIntensity;
            public string SunName;
            public float SunIntensity;
            public int EnabledDirectionals;
            public override string ToString() =>
                $"ambient=({Ambient.r:0.000},{Ambient.g:0.000},{Ambient.b:0.000}) ambientI={AmbientIntensity:0.000} " +
                $"sun='{SunName}' sunI={SunIntensity:0.00} enabledDirectionals={EnabledDirectionals}";
        }

        // The EFFECTIVE sun: RenderSettings.sun when it is live, else the brightest enabled
        // Directional light in any loaded scene (which is what actually lights the room, and is
        // exactly how the arena's stage "KeyLight" takes over a dungeon).
        private static LightingSample SampleLighting()
        {
            var s = new LightingSample
            {
                Ambient = RenderSettings.ambientLight,
                AmbientIntensity = RenderSettings.ambientIntensity,
                SunName = "<none>",
                SunIntensity = 0f,
                EnabledDirectionals = 0,
            };
            try
            {
                Light best = RenderSettings.sun;
                if (best != null && !best.isActiveAndEnabled) best = null;
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l == null || l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                    s.EnabledDirectionals++;
                    if (best == null || l.intensity > best.intensity) best = l;
                }
                if (best != null) { s.SunName = best.name; s.SunIntensity = best.intensity; }
            }
            catch (Exception ex) { FlowTrace.Warn("Auto", "AssertDungeonLoop: lighting sample threw " + ex.Message); }
            return s;
        }

        // =====================================================================
        //  PHASE: AssertScatterRecords — F8-8 verification probe
        // ---------------------------------------------------------------------
        // Proves the repaired scatter chain with captured lines, end-to-end:
        //   link 2  GenerateScatterRecords produced seeded records across bands
        //   link 3  a record within the 85m sight radius ACTIVATED a live rep
        //   link 4  warping past the 115m cull radius CULLED it (record kept)
        // Drives the REAL production path: EnsureMaintainLoopForTest starts the
        // same MaintainLoop MaybePopulate would (all its gates still apply every
        // tick — the new on-change 'MaintainLoop gated: <gate>' line names any
        // stuck gate). Ring reps are frozen via RepEngageWatcher.PauseAll so a
        // chase-engage cannot drop a battle mid-probe (battle gates the loop).
        // =====================================================================
        private IEnumerator AssertScatterRecords()
        {
            const string Tag = "Auto";
            EnsureHero("AssertScatterRecords");   // re-resolve a post-stream hero (RCA 2026-07-08) — unlock overworld coverage
            if (_hero == null) { _lastDetail = "no hero - skipped"; FlowTrace.Warn(Tag, "AssertScatterRecords: no hero - skipped (EnsureHero named the reason above)."); yield break; }

            int prevFlag = PlayerPrefs.GetInt("ff.overworldencounter", -1);
            PlayerPrefs.SetInt("ff.overworldencounter", 1);
            RepEngageWatcher.PauseAll();   // no roam/chase/engage while the probe holds still
            Vector3 home = _hero.transform.position;
            Quaternion homeRot = _hero.transform.rotation;

            var spawner = DeNelle.Village.OverworldEncounterSpawner.Instance;
            if (spawner == null)
            {
                _lastDetail = "OverworldEncounterSpawner.Instance NULL";
                FlowTrace.Fail(Tag, "AssertScatterRecords: FAIL at link 0 — OverworldEncounterSpawner.Instance was NULL (spawner never bootstrapped).");
                RestoreScatterProbe(prevFlag);
                yield break;
            }

            // link 1 — warp the hero onto navmesh, in an OUTER roster zone, inside the
            // 60-320m scatter band from world origin (same mechanics as AssertEncounterRealPath).
            Vector3 landing = Vector3.zero;
            bool placed = false;
            float[] radii = { 70f, 90f, 110f };
            for (int r = 0; r < radii.Length && !placed; r++)
            {
                for (int a = 0; a < 8 && !placed; a++)
                {
                    float ang = a * 45f * Mathf.Deg2Rad;
                    Vector3 cand = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radii[r];
                    if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out var nh, 12f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    float dOrigin = new Vector2(nh.position.x, nh.position.z).magnitude;
                    if (dOrigin < 60f || dOrigin > 320f) continue;
                    bool roster = false;
                    try { roster = DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(nh.position)); }
                    catch (Exception ex) { FlowTrace.Warn(Tag, "AssertScatterRecords: zone check threw " + ex.Message); }
                    if (!roster) continue;
                    landing = nh.position;
                    placed = true;
                }
            }
            if (!placed)
            {
                _lastDetail = "no on-mesh roster point in the 60-320m scatter band";
                FlowTrace.Fail(Tag, "AssertScatterRecords: FAIL at link 1 — no candidate point was on navmesh AND in a roster zone AND inside the 60-320m scatter band (navmesh not baked / zones not defined).");
                RestoreScatterProbe(prevFlag);
                yield break;
            }
            try { _hero.WarpTo(landing); } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertScatterRecords: WarpTo threw " + ex.Message); }
            yield return null;
            FlowTrace.Step(Tag, $"AssertScatterRecords: hero warped into the scatter band @ {landing} ({new Vector2(landing.x, landing.z).magnitude:0}m from origin).");

            // link 2 — GENERATION: ensure the real maintain loop runs, then wait up to
            // 2 maintain ticks (interval 10s) + margin for GenerateScatterRecords.
            spawner.EnsureMaintainLoopForTest();
            float w = 0f;
            while (w < 25f && spawner.GeneratedScatterCount == 0) { w += Time.unscaledDeltaTime; yield return null; }
            if (spawner.GeneratedScatterCount == 0)
            {
                _lastDetail = "0 scatter records after 2 maintain ticks";
                FlowTrace.Fail(Tag, "AssertScatterRecords: FAIL at link 2 — GenerateScatterRecords produced 0 records within 2 maintain ticks (~25s). Read the 'MaintainLoop gated: <gate>' line in this run — it names the stuck gate (else generation found no valid ground).");
                RestoreScatterProbe(prevFlag);
                _hero.WarpTo(home, homeRot);
                yield break;
            }
            int nearC = 0, midC = 0, farC = 0;
            for (int i = 0; i < spawner.GeneratedScatterCount; i++)
                if (spawner.TryGetScatterAnchor(i, out _, out int b)) { if (b == 0) nearC++; else if (b == 1) midC++; else farC++; }
            FlowTrace.Step(Tag, $"AssertScatterRecords: link 2 PASS — {spawner.GeneratedScatterCount} scatter records generated (bands: near[60-120m]={nearC} mid[120-200m]={midC} far[200-320m]={farC}).");

            // link 3 — ACTIVATION: stand on the nearest record's anchor (well inside the
            // 85m sight radius) and wait up to 2 maintain ticks for the live rep.
            int nearest = -1; float bestD = float.MaxValue; Vector3 nearestAnchor = Vector3.zero;
            for (int i = 0; i < spawner.GeneratedScatterCount; i++)
                if (spawner.TryGetScatterAnchor(i, out var anc, out _))
                {
                    float d = Vector3.Distance(_hero.transform.position, anc);
                    if (d < bestD) { bestD = d; nearest = i; nearestAnchor = anc; }
                }
            if (bestD > 40f && UnityEngine.AI.NavMesh.SamplePosition(nearestAnchor, out var ah, 12f, UnityEngine.AI.NavMesh.AllAreas))
            {
                try { _hero.WarpTo(ah.position); } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertScatterRecords: WarpTo(anchor) threw " + ex.Message); }
                yield return null;
            }
            float heroToRec = Vector3.Distance(_hero.transform.position, nearestAnchor);
            int actBefore = spawner.ScatterActivations;
            w = 0f;
            while (w < 25f && spawner.ScatterActivations == actBefore && spawner.LiveScatterCount == 0)
            { w += Time.unscaledDeltaTime; yield return null; }
            bool activated = spawner.ScatterActivations > actBefore || spawner.LiveScatterCount > 0;
            if (!activated)
            {
                _lastDetail = $"record #{nearest} at {heroToRec:0}m never ACTIVATED";
                FlowTrace.Fail(Tag, $"AssertScatterRecords: FAIL at link 3 — record #{nearest} with the hero {heroToRec:0}m away (< 85m sight) never ACTIVATED within 2 maintain ticks. See the 'scatter record ... NO complete path' / 'MaintainLoop gated' lines in this run — they name the dead link (reachability / live cap / gated tick).");
                RestoreScatterProbe(prevFlag);
                _hero.WarpTo(home, homeRot);
                yield break;
            }
            FlowTrace.Step(Tag, $"AssertScatterRecords: link 3 PASS — scatter ACTIVATED (activations +{spawner.ScatterActivations - actBefore}, live={spawner.LiveScatterCount}) with the hero {heroToRec:0}m from record #{nearest} (sight radius 85m).");

            // link 4 — CULL: warp >=150m away (another roster-valid record anchor when one
            // exists, else a sampled roster point at ~300m) and wait for the cull trace.
            Vector3 farPoint = Vector3.zero;
            bool haveFar = false;
            for (int i = 0; i < spawner.GeneratedScatterCount && !haveFar; i++)
                if (spawner.TryGetScatterAnchor(i, out var anc, out _) && Vector3.Distance(anc, nearestAnchor) >= 150f)
                { farPoint = anc; haveFar = true; }
            for (int a = 0; a < 8 && !haveFar; a++)
            {
                float ang = a * 45f * Mathf.Deg2Rad;
                Vector3 cand = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 300f;
                if (Vector3.Distance(cand, nearestAnchor) < 150f) continue;
                if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out var nh2, 20f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                bool roster = false;
                try { roster = DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(nh2.position)); }
                catch (Exception ex) { FlowTrace.Warn(Tag, "AssertScatterRecords: far zone check threw " + ex.Message); }
                if (!roster) continue;
                farPoint = nh2.position;
                haveFar = true;
            }
            if (!haveFar)
            {
                _lastDetail = $"gen={spawner.GeneratedScatterCount} act OK, cull SKIPPED (no far roster point)";
                FlowTrace.Warn(Tag, "AssertScatterRecords: cull leg SKIPPED — no roster-valid navmesh point >= 150m from the activated record (world/navmesh too small for the cull assert).");
            }
            else
            {
                int cullBefore = spawner.ScatterCulls;
                try { _hero.WarpTo(farPoint); } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertScatterRecords: WarpTo(far) threw " + ex.Message); }
                yield return null;
                float away = Vector3.Distance(_hero.transform.position, nearestAnchor);
                w = 0f;
                while (w < 25f && spawner.ScatterCulls == cullBefore) { w += Time.unscaledDeltaTime; yield return null; }
                if (spawner.ScatterCulls == cullBefore)
                {
                    _lastDetail = "cull trace never fired after the 150m warp";
                    FlowTrace.Fail(Tag, $"AssertScatterRecords: FAIL at link 4 — hero warped {away:0}m from the live record (> cull radius 115m) but no scatter CULL fired within 2 maintain ticks (cull pass in MaintainScatter dead or the tick gated — see 'MaintainLoop gated').");
                }
                else
                {
                    _lastDetail = $"PASS gen={spawner.GeneratedScatterCount} (near {nearC}/mid {midC}/far {farC}) act+{spawner.ScatterActivations - actBefore} cull+{spawner.ScatterCulls - cullBefore}";
                    FlowTrace.Step(Tag, $"AssertScatterRecords: PASS — {spawner.GeneratedScatterCount} records generated (near {nearC}, mid {midC}, far {farC}), ACTIVATED at {heroToRec:0}m (< 85m sight), CULLED at {away:0}m (> 115m; culls +{spawner.ScatterCulls - cullBefore}).");
                }
            }

            _hero.WarpTo(home, homeRot);
            RestoreScatterProbe(prevFlag);
        }

        // Shared probe cleanup: unfreeze the reps + restore the encounter flag.
        private static void RestoreScatterProbe(int prevFlag)
        {
            RepEngageWatcher.ResumeAll();
            RestoreEncounterFlag(prevFlag);
        }

        // =====================================================================
        //  PHASE: AssertCompassMarks — F8-16 verification probe
        // ---------------------------------------------------------------------
        // With >=1 live Enemy (a disposable factory orc is built if the wave left
        // none), asserts BOTH halves of the F8-16 fix on the HUD-kit compass:
        //   data half    — the enemy buffer fills (provider wired + resolves)
        //   layout half  — >=1 ACTIVE pip whose rect meets the 10x16px visibility
        //                  floor (-nographics renders nothing; rect math asserts).
        // =====================================================================
        private IEnumerator AssertCompassMarks()
        {
            const string Tag = "Auto";
            // Prefer the ACTIVE instance — posture-driven kit rebuilds can leave stale
            // inactive compass widgets, and asserting against one fails on a dead buffer.
            DeNelle.HUD.Kit.HudCompassWidget widget = null;
            foreach (var cw in UnityEngine.Object.FindObjectsByType<DeNelle.HUD.Kit.HudCompassWidget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (widget == null) widget = cw;
                if (cw != null && cw.isActiveAndEnabled) { widget = cw; break; }
            }
            if (widget == null)
            {
                bool kitAlive = UnityEngine.Object.FindAnyObjectByType<DeNelle.HUD.Kit.HudKitController>(FindObjectsInactive.Include) != null;
                if (kitAlive)
                {
                    _lastDetail = "HudKitController alive but NO compass widget";
                    FlowTrace.Fail(Tag, "AssertCompassMarks: FAIL at link 0 — HudKitController is alive but no HudCompassWidget exists (kit build / hud-areas occupancy row broken).");
                }
                else
                {
                    _lastDetail = "no HUD kit this run - skipped";
                    FlowTrace.Warn(Tag, "AssertCompassMarks: no HudKitController/compass in this scene — skipped.");
                }
                yield break;
            }

            // Guarantee a live Enemy for the buffer (the wave usually leaves some).
            Enemy diag = null;
            bool anyEnemy = false;
            foreach (var e in UnityEngine.Object.FindObjectsByType<Enemy>())
                if (e != null && e.gameObject.activeInHierarchy) { anyEnemy = true; break; }
            if (!anyEnemy)
            {
                Vector3 want = (_hero != null ? _hero.transform.position : Vector3.zero) + new Vector3(4f, 0f, 6f);
                if (UnityEngine.AI.NavMesh.SamplePosition(want, out var hit, 10f, UnityEngine.AI.NavMesh.AllAreas)) want = hit.position;
                try
                {
                    var def = DeNelle.Village.World.Camps.GarrisonStatBlocks.BuildTypedDef("orc-raider", 1);
                    diag = EnemyFactory.Build(def, want, Quaternion.identity, null);
                    if (diag != null) { diag.gameObject.name = "CompassProbeEnemy"; FlowTrace.Step(Tag, "AssertCompassMarks: no live Enemy — built disposable 'CompassProbeEnemy' via the canonical factory."); }
                }
                catch (Exception ex) { FlowTrace.Warn(Tag, "AssertCompassMarks: fallback enemy build threw " + ex.Message); }
                yield return null;
            }

            if (!widget.EnemyProviderWired)
            {
                _lastDetail = "EnemyProvider NULL";
                FlowTrace.Fail(Tag, "AssertCompassMarks: FAIL at link 1 — the compass EnemyProvider delegate is NOT wired (HudKitController provider wiring broken) — the buffer can never fill (data-empty half).");
                if (diag != null) UnityEngine.Object.Destroy(diag.gameObject);
                yield break;
            }

            // Force provider polls + let LateUpdate run the buffer + rect math.
            float w = 0f;
            widget.ForceProviderPoll();
            while (w < 3f && widget.EnemyMarkCount == 0) { w += Time.unscaledDeltaTime; widget.ForceProviderPoll(); yield return null; }

            // Fleet flake 2/4 (seeds 3/4): a lone surviving Enemy can be a PAUSED rep or sit
            // outside the compass provider's range, so 'anyEnemy' skipped the probe build and
            // link 2 failed on a legitimately-empty buffer. If the buffer is still empty and we
            // did not build our own NEAR-HERO enemy, build it now and re-wait — the probe must
            // assert the provider against an in-range enemy, not whatever the wave left behind.
            if (widget.EnemyMarkCount == 0 && diag == null)
            {
                Vector3 want2 = (_hero != null ? _hero.transform.position : Vector3.zero) + new Vector3(4f, 0f, 6f);
                if (UnityEngine.AI.NavMesh.SamplePosition(want2, out var hit2, 10f, UnityEngine.AI.NavMesh.AllAreas)) want2 = hit2.position;
                try
                {
                    var def2 = DeNelle.Village.World.Camps.GarrisonStatBlocks.BuildTypedDef("orc-raider", 1);
                    diag = EnemyFactory.Build(def2, want2, Quaternion.identity, null);
                    if (diag != null) { diag.gameObject.name = "CompassProbeEnemy"; FlowTrace.Step(Tag, "AssertCompassMarks: buffer empty with only distant/paused enemies — built near-hero 'CompassProbeEnemy'."); }
                }
                catch (Exception ex) { FlowTrace.Warn(Tag, "AssertCompassMarks: near-hero probe enemy build threw " + ex.Message); }
                w = 0f;
                while (w < 3f && widget.EnemyMarkCount == 0) { w += Time.unscaledDeltaTime; widget.ForceProviderPoll(); yield return null; }
            }

            int live = 0;
            foreach (var e in UnityEngine.Object.FindObjectsByType<Enemy>())
                if (e != null && e.gameObject.activeInHierarchy) live++;

            if (widget.EnemyMarkCount == 0)
            {
                _lastDetail = $"buffer EMPTY with {live} live Enemy";
                FlowTrace.Fail(Tag, $"AssertCompassMarks: FAIL at link 2 — compass enemy buffer EMPTY while {live} live Enemy exist (provider/type resolution — the data-empty half of F8-16).");
                if (diag != null) UnityEngine.Object.Destroy(diag.gameObject);
                yield break;
            }

            // Pips animate in LateUpdate — an INACTIVE widget instance can prove the DATA
            // half (direct poll) but can never activate a pip. Wait briefly for an active
            // instance (posture rebuilds swap them), then verify layout there; none active
            // = the layout half is honestly UNVERIFIABLE this posture, not failed.
            w = 0f;
            while (w < 2f && !widget.isActiveAndEnabled)
            {
                w += Time.unscaledDeltaTime;
                foreach (var cw2 in UnityEngine.Object.FindObjectsByType<DeNelle.HUD.Kit.HudCompassWidget>(FindObjectsSortMode.None))
                    if (cw2 != null && cw2.isActiveAndEnabled) { widget = cw2; widget.ForceProviderPoll(); break; }
                yield return null;
            }
            if (!widget.isActiveAndEnabled)
            {
                _lastDetail = $"data half PASS (buffer={widget.EnemyMarkCount}); layout half skipped — no ACTIVE compass this posture";
                FlowTrace.Warn(Tag, $"AssertCompassMarks: data half PASS (buffer={widget.EnemyMarkCount}) — layout half SKIPPED: no active compass widget this posture (pips need LateUpdate). Named skip, not silent.");
                if (diag != null) UnityEngine.Object.Destroy(diag.gameObject);
                yield break;
            }

            w = 0f;
            while (w < 3f && widget.ActiveTickCount == 0) { w += Time.unscaledDeltaTime; yield return null; }
            bool sized = widget.TryGetFirstActiveTickSize(out Vector2 pip);
            if (widget.ActiveTickCount == 0 || !sized)
            {
                _lastDetail = $"buffer={widget.EnemyMarkCount} but 0 ACTIVE pips";
                FlowTrace.Fail(Tag, $"AssertCompassMarks: FAIL at link 3 — buffer holds {widget.EnemyMarkCount} enemies but NO pip GameObject went ACTIVE (built-but-invisible half — UpdateEnemyTicks hero/tick-layer path).");
            }
            else if (pip.x < 10f || pip.y < 16f)
            {
                _lastDetail = $"pip {pip.x:0}x{pip.y:0}px below the 10x16 floor";
                FlowTrace.Fail(Tag, $"AssertCompassMarks: FAIL at link 4 — active pip rect {pip.x:0}x{pip.y:0}px is below the 10x16 visibility floor (the F8-16 sub-visible-sliver regression is back).");
            }
            else
            {
                _lastDetail = $"PASS buffer={widget.EnemyMarkCount} pips={widget.ActiveTickCount} rect={pip.x:0}x{pip.y:0}px";
                FlowTrace.Step(Tag, $"AssertCompassMarks: PASS — enemy buffer={widget.EnemyMarkCount} (live Enemy={live}), active pips={widget.ActiveTickCount}, first pip rect {pip.x:0}x{pip.y:0}px >= the 10x16 visibility floor.");
            }

            if (diag != null) UnityEngine.Object.Destroy(diag.gameObject);
        }

        // =====================================================================
        //  PHASE: AssertHeroHasAlbedo — white-Paladin verification probe
        // ---------------------------------------------------------------------
        // Asserts the WHITE-HERO ASSET FIX chain held this run: the PACKAGE albedo
        // audit bound EVERY material (19/19 once ExtractKnightHeroPackage remapped
        // the embedded PNGs) AND the 'WHITE HERO ROOT' Fail did NOT fire. The check
        // ordering was verified from code: the Fail lives inside AuditPackageAlbedo,
        // which runs AFTER ColorPackageBodyIfNullAlbedo (the binding/tint), and it
        // early-outs when a texture OR tint is bound — no reorder was needed; this
        // probe pins that ordering so a regression is a named Fail, not a false alarm.
        // =====================================================================
        private IEnumerator AssertHeroHasAlbedo()
        {
            const string Tag = "Auto";
            if (_hero == null) { _lastDetail = "no hero - skipped"; FlowTrace.Warn(Tag, "AssertHeroHasAlbedo: no hero - skipped."); yield break; }

            // The build-time audit fires inside HeroBodySwapper on the package/KnightV3
            // paths — give a late body swap a moment, else run the SAME read-only audit
            // on the live body ourselves.
            float w = 0f;
            while (w < 5f && !HeroBodySwapper.LastAlbedoAuditRan) { w += Time.unscaledDeltaTime; yield return null; }
            if (!HeroBodySwapper.LastAlbedoAuditRan)
            {
                Transform bodyT = _hero.transform.Find("HeroBody");
                GameObject target = bodyT != null ? bodyT.gameObject : _hero.gameObject;
                FlowTrace.Step(Tag, $"AssertHeroHasAlbedo: build-time audit never ran (non-package body path this run) — running the read-only audit on '{target.name}' now.");
                try { HeroBodySwapper.AuditPackageAlbedo(target); }
                catch (Exception ex) { FlowTrace.Warn(Tag, "AssertHeroHasAlbedo: audit threw " + ex.Message); }
            }

            int bound = HeroBodySwapper.LastAlbedoAuditBound;
            int total = HeroBodySwapper.LastAlbedoAuditTotal;
            bool white = HeroBodySwapper.LastAuditWhiteHeroRootFired;

            if (total <= 0)
            {
                _lastDetail = "audit scanned 0 materials";
                FlowTrace.Fail(Tag, "AssertHeroHasAlbedo: FAIL — the albedo audit scanned 0 materials on the hero body (no renderers/materials: the body build itself broke upstream — see the [Flow:HeroBody] lines this run).");
                yield break;
            }
            if (bound < total)
            {
                _lastDetail = $"albedo {bound}/{total} bound";
                FlowTrace.Fail(Tag, $"AssertHeroHasAlbedo: FAIL — PACKAGE albedo audit {bound}/{total}: {total - bound} material(s) still textureless (TripoAssetPostprocessor.ExtractKnightHeroPackage extraction/remap did not bind — the white-Paladin class).");
            }
            else
            {
                FlowTrace.Step(Tag, $"AssertHeroHasAlbedo: PASS — PACKAGE albedo audit {bound}/{total} (every material carries a bound _BaseMap/_MainTex).");
            }

            if (white)
            {
                _lastDetail = (_lastDetail ?? "") + " + WHITE HERO ROOT fired";
                FlowTrace.Fail(Tag, "AssertHeroHasAlbedo: FAIL — the 'WHITE HERO ROOT' Fail fired this run. It runs AFTER ColorPackageBodyIfNullAlbedo and only when texture AND tint are BOTH absent, so this is a genuine white hero — not the retired stale-ordering false alarm.");
            }
            else
            {
                FlowTrace.Step(Tag, "AssertHeroHasAlbedo: PASS — 'WHITE HERO ROOT' did NOT fire this run (check ordered after the tint/texture binding; early-outs when either is bound).");
                if (bound == total) _lastDetail = $"PASS albedo {bound}/{total}, no WHITE HERO ROOT";
            }
        }

        // =====================================================================
        //  PHASE: AssertOrientModalReleases — F8-30 verification probe
        // ---------------------------------------------------------------------
        // Drives the REAL dev-orient open path (the same OpenDevOrient call
        // BuildPaletteUI's OnOrientRequested / BuildModeController.
        // OpenOrientEditorForArmed land on, with a real catalog id + loadable
        // prefab), asserts the F8-30 PanelManager 'OrientEditor' registration,
        // then releases it via the EXTERNAL path that leaked pre-fix —
        // PanelManager.CloseAll — and asserts the full release: IsOpen=false +
        // AnyOpen=false (BuildModeController's placement freeze reads IsOpen, so
        // the build freeze releases with it). The Orient open/close FlowTrace
        // lines from the F8-30 fix are the paired evidence.
        // =====================================================================
        private IEnumerator AssertOrientModalReleases()
        {
            const string Tag = "Auto";

            // Deterministic baseline: no other modal may hold the arbiter slot.
            PanelManager.CloseAll();
            yield return null;

            // A REAL catalog id with a Resources-loadable visual prefab (prefer the
            // tutorial tower; else first loadable entry across every CatalogType).
            string id = null; GameObject prefab = null; string display = null;
            var candidates = new List<DeNelle.Core.Catalog.CatalogEntry>();
            var preferred = DeNelle.Core.Catalog.CatalogRegistry.Get("tower_ground_archer");
            if (preferred != null) candidates.Add(preferred);
            foreach (DeNelle.Core.Catalog.CatalogType t in Enum.GetValues(typeof(DeNelle.Core.Catalog.CatalogType)))
            {
                var list = DeNelle.Core.Catalog.CatalogRegistry.OfType(t);
                if (list == null) continue;
                foreach (var e in list) if (e != null) candidates.Add(e);
            }
            foreach (var e in candidates)
            {
                if (string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.visualPrefabPath)) continue;
                var p = Resources.Load<GameObject>(e.visualPrefabPath);
                if (p == null) continue;
                id = e.id; prefab = p;
                display = string.IsNullOrEmpty(e.displayName) ? e.id : e.displayName;
                break;
            }
            if (id == null)
            {
                _lastDetail = "no catalog entry with a loadable visual prefab";
                FlowTrace.Fail(Tag, "AssertOrientModalReleases: FAIL at link 0 — no CatalogRegistry entry has a Resources-loadable visualPrefabPath; cannot drive OpenDevOrient with a real id.");
                yield break;
            }

            var menu = UnityEngine.Object.FindAnyObjectByType<TowerPlacementRotateMenu>(FindObjectsInactive.Include);
            bool created = false;
            if (menu == null)
            {
                menu = new GameObject("DevOrientMenu(Probe)").AddComponent<TowerPlacementRotateMenu>();
                created = true;
            }
            menu.OpenDevOrient(id, prefab, display);
            yield return null;

            if (!menu.IsOpen)
            {
                _lastDetail = $"OpenDevOrient('{id}') did not open";
                FlowTrace.Fail(Tag, $"AssertOrientModalReleases: FAIL at link 1 — OpenDevOrient('{id}') left IsOpen=false (open rejected or threw — battle-lock, or ShowPanel never ran).");
                if (created) UnityEngine.Object.Destroy(menu.gameObject);
                yield break;
            }
            if (!(PanelManager.AnyOpen && PanelManager.OpenPanelName == "OrientEditor"))
            {
                _lastDetail = $"open but arbiter slot='{PanelManager.OpenPanelName ?? "<none>"}'";
                FlowTrace.Fail(Tag, $"AssertOrientModalReleases: FAIL at link 2 — orient editor IsOpen=true but PanelManager does not hold 'OrientEditor' (slot='{PanelManager.OpenPanelName ?? "<none>"}') — the F8-30 registration regressed (external closers can no longer see it).");
                menu.Close();
                if (created) UnityEngine.Object.Destroy(menu.gameObject);
                yield break;
            }
            FlowTrace.Step(Tag, $"AssertOrientModalReleases: links 1+2 PASS — OpenDevOrient('{id}') open AND PanelManager slot 'OrientEditor' taken (the F8-30 registration).");

            // link 3 — the EXTERNAL release path that never worked pre-F8-30.
            PanelManager.CloseAll();
            yield return null;

            if (!menu.IsOpen && !PanelManager.AnyOpen)
            {
                _lastDetail = $"PASS open+registered+released via CloseAll ('{id}')";
                FlowTrace.Step(Tag, "AssertOrientModalReleases: PASS — PanelManager.CloseAll released the orient editor (IsOpen=false, AnyOpen=false; BuildModeController's placement-freeze gate reads IsOpen, so the build freeze released with it).");
            }
            else
            {
                _lastDetail = $"CloseAll leak: IsOpen={menu.IsOpen} AnyOpen={PanelManager.AnyOpen}";
                FlowTrace.Fail(Tag, $"AssertOrientModalReleases: FAIL at link 3 — after PanelManager.CloseAll the modal leaked (IsOpen={menu.IsOpen}, AnyOpen={PanelManager.AnyOpen}, slot='{PanelManager.OpenPanelName ?? "<none>"}') — the F8-30 external-release click-lock is back.");
                menu.Close();
            }

            if (created) UnityEngine.Object.Destroy(menu.gameObject);
        }

        // Background guard: dismiss any Yarn dialogue that auto-starts, so the bot never
        // stalls inside a conversation it cannot read headless. Runs ~1/sec for the bot's
        // lifetime (the host GameObject is destroyed on quit, ending this loop).
        // WO-597: the popup-close oracle deliberately opens a structure dialogue CARD to
        // verdict the dialogue view as a closable surface — pause the suppressor while it
        // does, or the 1s Stop() loop below would kill the card mid-assertion.
        private bool _pauseDialogueSuppression;

        private IEnumerator SuppressDialogue()
        {
            while (true)
            {
                try
                {
                    if (!_pauseDialogueSuppression && DialogueService.IsRunning)
                    {
                        DialogueService.Stop();
                        FlowTrace.Step("Auto", "SuppressDialogue: dismissed an auto-started dialogue (skip-Yarn).");
                    }
                }
                catch (System.Exception ex) { FlowTrace.Warn("Auto", "SuppressDialogue: " + ex.Message); }
                yield return Wait(1f);
            }
        }

        // =====================================================================
        //  Phase runner — wraps each phase in a realtime watchdog + step logs
        // =====================================================================

        private bool _abortRun;

        /// <summary>
        /// Runs one phase coroutine against a REALTIME watchdog. If the phase does
        /// not finish within its declared timeout, the phase is failed
        /// (FlowTrace.Fail) and the run advances — never hangs. The global cap is
        /// also enforced here.
        /// </summary>
        // Optional phase filter ("--phases=Encounter,PopupClose"): when present, only phases
        // whose name contains one of the comma tokens (case-insensitive) run; the rest are
        // skipped with a Step line. Boot/hero prerequisites always run. Added 2026-07-06 so a
        // single-purpose capture run (one UI frame) is ~2 minutes, not the full 24-phase sweep.
        private static string[] _phaseFilter;
        private static bool PhaseAllowed(string name)
        {
            if (_phaseFilter == null)
            {
                var args = Environment.GetCommandLineArgs();
                string raw = null;
                foreach (var a in args)
                    if (a != null && a.StartsWith("--phases=", StringComparison.OrdinalIgnoreCase))
                        raw = a.Substring("--phases=".Length);
                _phaseFilter = string.IsNullOrEmpty(raw)
                    ? Array.Empty<string>()
                    : raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }
            if (_phaseFilter.Length == 0) return true;
            if (name == "BootToGameplay" || name == "ResolveHero") return true;   // prerequisites
            foreach (var t in _phaseFilter)
                if (name.IndexOf(t.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // =====================================================================
        //  AssertTutorialFirstTower — P0 real-input build-placement probe
        // ---------------------------------------------------------------------
        // Owner 2026-07-07 (captured symptom: '[Flow:Build] Armed placement for ...'
        // x5 but ZERO '[Flow:Build] PlaceConfirm check' lines): drive the REAL
        // tutorial first-tower evaluation path headless, with NO logic bypass —
        //   Enter() -> ArmById('tower_ground_archer') (the same Arm() a palette tap
        //   fires) -> inject one click/frame through the REAL IBuildInput seam
        //   (SetInput — the exact seam EnsureTouchInput installs the touch driver
        //   through) -> assert each link and NAME the one that broke:
        //     link 1  the place loop POLLS the input (if not: a gate is stuck —
        //             the throttled 'PlaceLoop BLOCKED at <gate>' lines name it)
        //     link 2  a consumed click COMMITS (StructurePlaced fires)
        //     link 3  TutorialSignals 'build.tower_placed' raises
        //     link 4  (if a TutorialFlow is live) a tutorial_v2 step persists
        // If the real path is blocked, this probe FAILS — that is its purpose.
        // =====================================================================

        /// <summary>
        /// Deterministic <see cref="IBuildInput"/> for the probe: one injected click is
        /// latched and consumed on the first PlaceOrSelect read (mirrors the touch driver's
        /// single-frame latch). Polls/Consumed counters split "loop never evaluated input"
        /// (gate stuck upstream) from "click read but never committed" (suppress/invalid).
        /// </summary>
        private sealed class BotBuildInput : IBuildInput
        {
            private Vector2 _point;
            private bool _pending;
            /// <summary>How many times the place loop read PlaceOrSelect (0 = loop never evaluated).</summary>
            public int Polls { get; private set; }
            /// <summary>How many injected clicks were actually consumed by the loop.</summary>
            public int Consumed { get; private set; }
            public void Click(Vector2 screenPoint) { _point = screenPoint; _pending = true; }
            /// <summary>Aim the ray point WITHOUT latching a click (WO-683: park the armed
            /// ghost at a spot so the d-pad drive can be observed — nothing places).</summary>
            public void PointAt(Vector2 screenPoint) { _point = screenPoint; }
            public Vector2 ScreenPoint => _point;
            public bool PlaceOrSelect
            {
                get
                {
                    Polls++;
                    if (!_pending) return false;
                    _pending = false;
                    Consumed++;
                    return true;
                }
            }
            public bool Cancel => false;
            public bool Rotate => false;
        }

        /// <summary>Count persisted mandatory tutorial-step completions ("tutorial_v2:*" keys).</summary>
        private static int CountTutorialV2Seen()
        {
            var svc = DeNelle.Core.State.GameStateService.Instance;
            var st = svc != null ? svc.State : null;
            if (st == null || st.SeenTutorials == null) return 0;
            int n = 0;
            foreach (var kv in st.SeenTutorials)
                if (kv.Value && kv.Key != null && kv.Key.StartsWith("tutorial_v2:", StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>The camera actually rendering to screen (highest depth, no RT) — same rule
        /// BuildModeController.ActiveScreenCamera uses, so the probe projects through the view
        /// the placement ray will cast from.</summary>
        private static Camera HighestDepthScreenCamera()
        {
            Camera best = null;
            foreach (var c in Camera.allCameras)
            {
                if (c == null || !c.enabled || c.targetTexture != null) continue;
                if (best == null || c.depth > best.depth) best = c;
            }
            return best != null ? best : Camera.main;
        }

        private IEnumerator AssertTutorialFirstTower()
        {
            const string Tag = "Auto";
            const string TowerId = "tower_ground_archer";

            // 0) Snapshot the tutorial persistence BEFORE the drive (link-4 baseline).
            int seenBefore = CountTutorialV2Seen();
            bool tutorialLive = FindAnyObjectByType<TutorialFlow>() != null;

            // 1) ENTER build mode through the real path (real Enter — no state poking).
            var ctrl = BuildModeController.EnsureExists();
            if (ctrl.IsActive) { ctrl.Exit(); yield return null; }
            ctrl.Enter();
            yield return null; yield return null;   // let Enter settle (camera pull, palette, grid)
            if (!ctrl.IsActive)
            {
                _lastDetail = "Enter() refused — build mode never activated";
                FlowTrace.Fail(Tag, "AssertTutorialFirstTower: BuildModeController.Enter() did not activate (enemy-owned scene / entry gate) — link 0 BROKEN before placement could even arm.");
                yield break;
            }

            // 2) Fund the cost gate (test setup, not a bypass — the gate still runs), then ARM
            //    through the SAME Arm() path a palette-card tap fires.
            var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(TowerId);
            if (entry == null)
            {
                _lastDetail = "'" + TowerId + "' not in CatalogRegistry";
                FlowTrace.Fail(Tag, "AssertTutorialFirstTower: '" + TowerId + "' is not in CatalogRegistry — the tutorial first-tower entry is missing from the catalog.");
                ctrl.Exit();
                yield break;
            }
            var cost = BuildModeController.CostFor(entry);
            var econ = EconomyService.Instance;
            if (econ != null) econ.Grant(BuildModeController.ToEconomy(cost));
            else DeNelle.Core.State.GameStateService.Instance?.AddCrystals(cost.crystals);

            if (!ctrl.ArmById(TowerId))
            {
                _lastDetail = "ArmById failed";
                FlowTrace.Fail(Tag, "AssertTutorialFirstTower: ArmById('" + TowerId + "') returned false — arming path broken.");
                ctrl.Exit();
                yield break;
            }

            // 3) Swap the bot input in through the REAL seam (SetInput — same seam
            //    EnsureTouchInput installs LeanTouchBuildDriver through).
            var bot = new BotBuildInput();
            ctrl.SetInput(bot);

            bool placedFired = false;
            string placedId = null;
            Action<string> onPlaced = id => { placedFired = true; placedId = id; };
            BuildModeController.StructurePlaced += onPlaced;
            DeNelle.Core.Tutorial.TutorialSignals.Clear(DeNelle.Core.Tutorial.TutorialSignals.TowerPlaced);

            try
            {
                // 4) Inject clicks at candidate ground points. DYNAMIC (2026-07-11 fix,
                //    replay-run0.log proof: all 8 fixed cells rejected Occupied/BadSurface
                //    after the 07-10 Colosseum hub structure landed on them): sample rings
                //    of cells around the map centre, project each through the live overview
                //    camera, and PRE-VALIDATE via the controller's own reason-aware gate
                //    (BuildModeController.ProbeArmedPlacementAt → IsValidPlacement — the
                //    EXACT check that computes ghostValid). Only actually-valid points are
                //    clicked; the old fixed offsets remain as a last-resort fallback so a
                //    probe-seam fault can't silently blind the phase.
                var cam = HighestDepthScreenCamera();
                var grid = PlacementGrid.Instance;
                if (cam == null || grid == null)
                {
                    _lastDetail = "cam=" + (cam != null) + " grid=" + (grid != null);
                    FlowTrace.Fail(Tag, "AssertTutorialFirstTower: " + (cam == null ? "no screen camera" : "no PlacementGrid") + " after Enter() — cannot project a ground click.");
                    yield break;
                }
                var centre = new Vector2Int(grid.gridWidth / 2, grid.gridHeight / 2);
                var candidates = new List<Vector2>();   // screen points, pre-validated
                var candidateCells = new List<Vector2Int>();
                int sampled = 0;
                string mode = "dynamic";
                // Expanding rings (radius 2..12 cells, step 2) around the centre — near
                // placements first (tutorial-plausible), widening until enough valid cells.
                for (int r = 2; r <= 12 && candidates.Count < 8; r += 2)
                {
                    for (int dx = -r; dx <= r && candidates.Count < 8; dx++)
                    for (int dz = -r; dz <= r && candidates.Count < 8; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;   // ring shell only
                        var cell = centre + new Vector2Int(dx, dz);
                        if (cell.x < 0 || cell.y < 0 || cell.x >= grid.gridWidth || cell.y >= grid.gridHeight) continue;
                        Vector3 sp3 = cam.WorldToScreenPoint(grid.CellToWorld(cell));
                        if (sp3.z <= 0f) continue;                                     // behind the camera
                        if (sp3.x < 0f || sp3.y < 0f || sp3.x >= Screen.width || sp3.y >= Screen.height) continue;   // off-screen
                        sampled++;
                        var sp = new Vector2(sp3.x, sp3.y);
                        if (!ctrl.ProbeArmedPlacementAt(sp, out _)) continue;           // the game's own validity gate
                        candidates.Add(sp);
                        candidateCells.Add(cell);
                    }
                }
                if (candidates.Count == 0)
                {
                    // LAST RESORT — the historical fixed offsets (pre-2026-07-11 behaviour).
                    mode = "fixed-fallback";
                    var cellOffsets = new Vector2Int[]
                    {
                        new Vector2Int(3, 3), new Vector2Int(-4, 2), new Vector2Int(5, -3),
                        new Vector2Int(-5, -5), new Vector2Int(0, 6), new Vector2Int(6, 0),
                        new Vector2Int(-6, 4), new Vector2Int(2, -6),
                    };
                    foreach (var off in cellOffsets)
                    {
                        Vector3 sp3 = cam.WorldToScreenPoint(grid.CellToWorld(centre + off));
                        if (sp3.z <= 0f) continue;
                        candidates.Add(new Vector2(sp3.x, sp3.y));
                        candidateCells.Add(centre + off);
                    }
                }
                FlowTrace.Step(Tag, "FirstTower candidates: sampled=" + sampled + " valid=" + (mode == "dynamic" ? candidates.Count : 0) + " mode=" + mode);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (placedFired) break;
                    Vector2 sp = candidates[i];
                    // TWO-STEP placement (owner ruling 2026-07-13): the world click only
                    // DROPS the pending ghost — the PLACE latch is the ONLY commit. Drive
                    // the REAL player sequence: click to drop, let the place loop consume
                    // it, then raise the PLACE latch (the on-screen button's channel).
                    bot.Click(sp);
                    FlowTrace.Step(Tag, "AssertTutorialFirstTower: injected click (two-step DROP) at screen (" + sp.x.ToString("F0") + "," + sp.y.ToString("F0") + ") for cell " + candidateCells[i] + ".");
                    float dw = 0f;
                    while (dw < 0.3f && !placedFired) { dw += Time.deltaTime; yield return null; }
                    ctrl.RequestUiPlaceConfirm();
                    FlowTrace.Step(Tag, "AssertTutorialFirstTower: PLACE latch raised for the pending drop (two-step commit).");
                    float w = 0f;
                    while (w < 1.2f && !placedFired) { w += Time.deltaTime; yield return null; }
                }

                // 5) Verdict — name the broken link. The bot's counters split the failure modes.
                if (!placedFired)
                {
                    if (bot.Polls == 0)
                    {
                        _lastDetail = "input seam NEVER polled — a gate upstream blocks the place loop";
                        FlowTrace.Fail(Tag, "AssertTutorialFirstTower: FAIL at link 1 — the place loop NEVER polled IBuildInput.PlaceOrSelect while armed. A gate between 'armed' and PlaceConfirm is blocking: read the throttled '[Flow:Build] PlaceLoop BLOCKED at <gate>' lines in this run — they name it.");
                    }
                    else if (bot.Consumed == 0)
                    {
                        _lastDetail = "polled " + bot.Polls + "x but the click latch was never consumed";
                        FlowTrace.Fail(Tag, "AssertTutorialFirstTower: FAIL at link 1 — input polled " + bot.Polls + "x but no injected click was ever consumed (latch/frame-ordering fault in the input seam).");
                    }
                    else
                    {
                        _lastDetail = "clicks consumed (" + bot.Consumed + ") but no StructurePlaced";
                        FlowTrace.Fail(Tag, "AssertTutorialFirstTower: FAIL at link 2 — " + bot.Consumed + " click(s) reached the PlaceConfirm evaluation but NO placement committed at any candidate point (suppressed by joystick-zone/pickable-UI, rejected invalid, or Place() aborted) — see the '[Flow:Build] PlaceConfirm' / reject lines in this run.");
                    }
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertTutorialFirstTower: links 1+2 PASS — PlaceConfirm CONFIRMED, placement committed, StructurePlaced('" + placedId + "') fired.");

                // 6) Link 3 — the tutorial signal bus.
                float sw = 0f;
                while (sw < 2f && !DeNelle.Core.Tutorial.TutorialSignals.HasFired(DeNelle.Core.Tutorial.TutorialSignals.TowerPlaced))
                { sw += Time.deltaTime; yield return null; }
                if (!DeNelle.Core.Tutorial.TutorialSignals.HasFired(DeNelle.Core.Tutorial.TutorialSignals.TowerPlaced))
                {
                    _lastDetail = "StructurePlaced fired but 'build.tower_placed' never raised";
                    FlowTrace.Fail(Tag, "AssertTutorialFirstTower: FAIL at link 3 — StructurePlaced fired but TutorialSignals 'build.tower_placed' never raised within 2s (TutorialSignalAdapters not alive / not subscribed to the LIVE placement event).");
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertTutorialFirstTower: link 3 PASS — TutorialSignals 'build.tower_placed' raised.");

                // 7) Link 4 — STEP-COMPLETE, only assertable when a TutorialFlow is live. Soft
                //    (Warn, not Fail) because a mid-run bot's flow is rarely parked ON the build
                //    step; a Fail here would be noise, while the Warn still surfaces a dead
                //    interpreter when the owner's repro IS on that step.
                if (tutorialLive)
                {
                    float cw = 0f;
                    while (cw < 3f && CountTutorialV2Seen() <= seenBefore) { cw += Time.deltaTime; yield return null; }
                    if (CountTutorialV2Seen() > seenBefore)
                        FlowTrace.Step(Tag, "AssertTutorialFirstTower: link 4 PASS — a tutorial_v2 step completed (persisted) after the signal.");
                    else
                        FlowTrace.Warn(Tag, "AssertTutorialFirstTower: link 4 SOFT — signal raised but no tutorial_v2 step persisted within 3s (flow not awaiting the build step in this run, or the interpreter did not consume the signal).");
                }
                else
                    FlowTrace.Step(Tag, "AssertTutorialFirstTower: link 4 skipped — no live TutorialFlow this run.");

                _lastDetail = "PASS — placed '" + placedId + "', signal raised (polls=" + bot.Polls + ", clicks=" + bot.Consumed + ")";
                FlowTrace.Step(Tag, "AssertTutorialFirstTower: PASS — the real tutorial first-tower chain is intact end-to-end.");
            }
            finally
            {
                BuildModeController.StructurePlaced -= onPlaced;
                ctrl.SetInput(null);           // restore the default DesktopBuildInput
                if (ctrl.IsActive) ctrl.Exit();
            }
        }

        // =====================================================================
        //  AssertFoundingArc — WO-702 founding-FTUE chain probe (real gates)
        // ---------------------------------------------------------------------
        // Fresh-save founding arc, link by link (each failure NAMES the dead link):
        //   link 0  context gates (ff.tutorialv2 / hub / fresh save) — N/A otherwise
        //   link 1  a GUIDE BODY present near the Heart AND 'world.guide' resolving to
        //           it (WO-1012 P2: the guide IS the pet-Echo body "Pet_<species>",
        //           deployed by the ARRIVE-beat starter grant; the steward stand-in
        //           is the parked-rotation fallback body)
        //   link 2  founding_greet driven to its end via the REAL dialogue Advance
        //   link 3  Echo Hollow through the REAL build gate: Enter → ArmById('pet-house')
        //           → injected click (IBuildInput seam) → placement commits →
        //           'build.structure_placed:pet-house' raised (the WO-702 signal)
        //   link 4  starter-pet grant: GameState.Pets grew + StarterPetId set
        //   link 5  FTUE peace window: HostilesSuppressedForTutorial TRUE throughout
        //   link 6  DEFEND refused: ForceBeginNextWave leaves the wave phase Idle
        // =====================================================================
        private IEnumerator AssertFoundingArc()
        {
            const string Tag = "Auto";
            const string HollowId = "pet-house";
            string hollowSignal = DeNelle.Core.Tutorial.TutorialSignals.StructurePlacedPrefix + HollowId;
            string scene = ActiveScene();
            FlowTrace.Step(Tag, "AssertFoundingArc: ENTER — WO-702 founding-arc chain probe.");

            // ── link 0: context gates ────────────────────────────────────────
            if (!DeNelle.Core.FeatureFlags.TutorialV2)
            {
                _lastDetail = "ff.tutorialv2 OFF — N/A (skipped)";
                FlowTrace.Step(Tag, "AssertFoundingArc: ff.tutorialv2 OFF — N/A, skipping.");
                yield break;
            }
            if (!DeNelle.Core.HubScenes.IsHub(scene))
            {
                _lastDetail = $"'{scene}' not a hub — N/A (skipped)";
                FlowTrace.Step(Tag, $"AssertFoundingArc: scene '{scene}' is not a hub — N/A.");
                yield break;
            }
            var svc = DeNelle.Core.State.GameStateService.Instance;
            var st = svc != null ? svc.State : null;
            if (st == null)
            {
                _lastDetail = "GameStateService unavailable — N/A (skipped)";
                FlowTrace.Warn(Tag, "AssertFoundingArc: GameStateService/State unavailable — N/A.");
                yield break;
            }
            if (st.Onboarded)
            {
                _lastDetail = "save already Onboarded — N/A (returning player)";
                FlowTrace.Step(Tag, "AssertFoundingArc: save already Onboarded — founding arc done; N/A.");
                yield break;
            }

            // ── link 5 (entry): the peace window must already hold on a fresh save ──
            if (!TutorialFlow.HostilesSuppressedForTutorial)
            {
                _lastDetail = "FAIL link 5 — peace window NOT held on a fresh save";
                FlowTrace.Fail(Tag, "AssertFoundingArc: FAIL at link 5 — fresh save (Onboarded=false, ff.tutorialv2 ON) but TutorialFlow.HostilesSuppressedForTutorial is FALSE; the founding peace window is broken at entry.");
                yield break;
            }
            FlowTrace.Step(Tag, "AssertFoundingArc: link 5 (entry) PASS — peace window held (HostilesSuppressedForTutorial=true).");

            // ── link 1: 'world.guide' resolves to a live GUIDE BODY near the Heart ──
            // WO-1012 P2: the guide IS the player's first pet-Echo (root "Pet_<species>",
            // deployed once the ARRIVE-beat starter grant lands).
            // ⚠ WO-971: this check used to ALSO accept "Sylas" / "CompanionIntroducer" as a
            // valid guide body. Those stand-ins are DELETED (owner ruling 2026-08-10,
            // "remove the original", "only the new wolf one stays") — and accepting them
            // meant this probe would have gone GREEN on the exact build the owner rejected,
            // where the spotlight sat on a peasant NPC. Only a real pet-Echo body counts now.
            // Resolving only to the Heart/town-anchor fallbacks = NO body ever spawned
            // = the beat has no physical guide — FAIL. (DevTools cannot reference
            // DeNelle.Pets, so the pet body is classified by its PetDeployer name.)
            Transform guideT = null;
            bool guideIsBody = false;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 5f)
            {
                var a = DeNelle.Core.UI.TutorialHighlightRegistry.Resolve("world.guide");
                guideT = a.IsValid ? a.World : null;
                if (guideT != null)
                {
                    string n = guideT.name ?? "";
                    guideIsBody = n.StartsWith("Pet_", StringComparison.OrdinalIgnoreCase);
                    if (guideIsBody) break;
                }
                yield return null;
            }
            if (!guideIsBody)
            {
                _lastDetail = $"FAIL link 1 — 'world.guide' never resolved to a guide body (got '{(guideT != null ? guideT.name : "<invalid>")}')";
                FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 1 — 'world.guide' resolved to '{(guideT != null ? guideT.name : "<invalid>")}' for 5s on a fresh save; no pet-Echo guide body was deployed (ARRIVE-beat grant -> PetDeployer 'Pet_<species>'). Read the [Flow:PetAcquire] lines in this run. WO-971: there is no steward stand-in to fall back to any more — the chain is pet body, then the Heart.");
                yield break;
            }
            var heart = FindAnyObjectByType<HeartController>();
            if (heart != null)
            {
                float d = Vector3.Distance(guideT.position, heart.transform.position);
                if (d > 20f)
                    FlowTrace.Warn(Tag, $"AssertFoundingArc: link 1 SOFT — guide body '{guideT.name}' is {d:0.0}m from the Heart (expected a courtyard-adjacent spawn <= 20m).");
                else
                    FlowTrace.Step(Tag, $"AssertFoundingArc: link 1a PASS — guide body '{guideT.name}' {d:0.0}m from the Heart.");
            }
            FlowTrace.Step(Tag, $"AssertFoundingArc: link 1 PASS — 'world.guide' resolves to the live guide body '{guideT.name}'.");

            // ── link 2: drive founding_greet's dialogue to its end (real Advance) ──
            var flow = FindAnyObjectByType<TutorialFlow>();
            bool greetCompleted = false;   // WO-1012 P2: greet ENTER fires the starter-pet grant — link 4 keys off this
            if (flow != null && string.Equals(flow.CurrentStepId, "founding_greet", StringComparison.OrdinalIgnoreCase))
            {
                // Wait for the beat's intro dialogue to open, then Advance through it —
                // the SAME call the continue tap fires (DialogueViewModel.Advance).
                t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 5f && !DeNelle.Core.Dialogue.DialogueService.IsRunning)
                    yield return null;
                int taps = 0;
                while (DeNelle.Core.Dialogue.DialogueService.IsRunning && taps < 40)
                {
                    DeNelle.Core.Dialogue.DialogueService.ActiveVm?.Advance();
                    taps++;
                    yield return null;
                }
                // The dialogue.ended completion should move the flow off founding_greet.
                t0 = Time.realtimeSinceStartup;
                bool advanced = false;
                while (Time.realtimeSinceStartup - t0 < 5f)
                {
                    if (!string.Equals(flow.CurrentStepId, "founding_greet", StringComparison.OrdinalIgnoreCase)) { advanced = true; break; }
                    yield return null;
                }
                if (!advanced)
                {
                    _lastDetail = $"FAIL link 2 — founding_greet never completed after {taps} Advance taps";
                    FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 2 — drove the greet dialogue with {taps} real Advance taps but the flow never left founding_greet (dialogue.ended:tut_founding_greet not consumed).");
                    yield break;
                }
                greetCompleted = true;
                FlowTrace.Step(Tag, $"AssertFoundingArc: link 2 PASS — founding_greet completed via real dialogue Advance ({taps} taps); flow now on '{flow.CurrentStepId}'.");
            }
            else
                FlowTrace.Step(Tag, $"AssertFoundingArc: link 2 skipped — flow not parked on founding_greet this run (step '{(flow != null ? flow.CurrentStepId : "<no flow>")}').");

            // ── link 3+4: the Echo Hollow placement + the starter-pet grant ─────
            int petsBefore = st.Pets != null ? st.Pets.Count : 0;
            bool hollowAlreadyPlaced = false;
            if (st.BaseLayout != null)
                foreach (var rec in st.BaseLayout)
                    if (string.Equals(rec.itemId, HollowId, StringComparison.OrdinalIgnoreCase)) { hollowAlreadyPlaced = true; break; }

            if (hollowAlreadyPlaced)
                FlowTrace.Step(Tag, "AssertFoundingArc: link 3 N/A — a pet-house already stands on this save (singleton; idempotent rerun). Grant asserted from persisted state below.");
            else
            {
                var ctrl = BuildModeController.EnsureExists();
                if (ctrl.IsActive) { ctrl.Exit(); yield return null; }
                ctrl.Enter();
                yield return null; yield return null;
                if (!ctrl.IsActive)
                {
                    _lastDetail = "FAIL link 3 — build Enter() refused";
                    FlowTrace.Fail(Tag, "AssertFoundingArc: FAIL at link 3 — BuildModeController.Enter() did not activate; the Hollow placement chain is dead before arming.");
                    yield break;
                }

                var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(HollowId);
                if (entry == null)
                {
                    _lastDetail = "FAIL link 3 — 'pet-house' not in CatalogRegistry";
                    FlowTrace.Fail(Tag, "AssertFoundingArc: FAIL at link 3 — 'pet-house' missing from CatalogRegistry; the founding_hollow step can never arm.");
                    ctrl.Exit();
                    yield break;
                }
                // First-build freebie (owner 2026-07-13 evening): when the Hollow's FREE
                // first build is live, do NOT fund the gate — the freebie itself must
                // carry the placement (that's the link under test). Only a consumed-flag
                // rerun still funds the normal cost so the gate can pass.
                var econ = EconomyService.Instance;
                bool freebieWasLive = BuildModeController.FreeBuildAvailable(entry);
                if (!freebieWasLive)
                {
                    var cost = BuildModeController.CostFor(entry);
                    if (econ != null) econ.Grant(BuildModeController.ToEconomy(cost));   // fund the gate — the gate still runs
                    else DeNelle.Core.State.GameStateService.Instance?.AddCrystals(cost.crystals);
                }
                // Snapshot the ledger AFTER any funding grant — if the FREE first build
                // charges anyway, the post-commit balance drops below this pair and the
                // link 3b assertion below fails loud.
                int woodBeforePlace = econ != null ? econ.Wood : st.Wood;
                int ironBeforePlace = econ != null ? econ.Iron : st.Iron;

                if (!ctrl.ArmById(HollowId))
                {
                    _lastDetail = "FAIL link 3 — ArmById('pet-house') refused";
                    FlowTrace.Fail(Tag, "AssertFoundingArc: FAIL at link 3 — ArmById('pet-house') returned false (arming path broken for the Hollow).");
                    ctrl.Exit();
                    yield break;
                }

                var bot = new BotBuildInput();
                ctrl.SetInput(bot);
                bool placedFired = false;
                string placedId = null;
                Action<string> onPlaced = id => { if (string.Equals(id, HollowId, StringComparison.OrdinalIgnoreCase)) { placedFired = true; placedId = id; } };
                BuildModeController.StructurePlaced += onPlaced;
                DeNelle.Core.Tutorial.TutorialSignals.Clear(hollowSignal);

                try
                {
                    var cam = HighestDepthScreenCamera();
                    var grid = PlacementGrid.Instance;
                    if (cam == null || grid == null)
                    {
                        _lastDetail = "FAIL link 3 — no camera/grid after Enter()";
                        FlowTrace.Fail(Tag, "AssertFoundingArc: FAIL at link 3 — " + (cam == null ? "no screen camera" : "no PlacementGrid") + " after Enter(); cannot project a ground click.");
                        yield break;
                    }
                    var centre = new Vector2Int(grid.gridWidth / 2, grid.gridHeight / 2);
                    var candidates = new List<Vector2>();
                    for (int r = 2; r <= 12 && candidates.Count < 8; r += 2)
                    {
                        for (int dx = -r; dx <= r && candidates.Count < 8; dx++)
                        for (int dz = -r; dz <= r && candidates.Count < 8; dz++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                            var cell = centre + new Vector2Int(dx, dz);
                            if (cell.x < 0 || cell.y < 0 || cell.x >= grid.gridWidth || cell.y >= grid.gridHeight) continue;
                            Vector3 sp3 = cam.WorldToScreenPoint(grid.CellToWorld(cell));
                            if (sp3.z <= 0f) continue;
                            if (sp3.x < 0f || sp3.y < 0f || sp3.x >= Screen.width || sp3.y >= Screen.height) continue;
                            var sp = new Vector2(sp3.x, sp3.y);
                            if (!ctrl.ProbeArmedPlacementAt(sp, out _)) continue;
                            candidates.Add(sp);
                        }
                    }
                    FlowTrace.Step(Tag, $"AssertFoundingArc: {candidates.Count} pre-validated Hollow candidate cell(s).");
                    for (int i = 0; i < candidates.Count && !placedFired; i++)
                    {
                        // TWO-STEP placement (owner ruling 2026-07-13): click = DROP only;
                        // the PLACE latch commits (same sequence AssertTutorialFirstTower drives).
                        bot.Click(candidates[i]);
                        float dw = 0f;
                        while (dw < 0.3f && !placedFired) { dw += Time.deltaTime; yield return null; }
                        ctrl.RequestUiPlaceConfirm();
                        float w = 0f;
                        while (w < 1.2f && !placedFired) { w += Time.deltaTime; yield return null; }
                    }
                    if (!placedFired)
                    {
                        _lastDetail = $"FAIL link 3 — no Hollow placement committed (polls={bot.Polls}, consumed={bot.Consumed})";
                        FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 3 — armed 'pet-house' but no placement committed at any candidate (polls={bot.Polls}, clicksConsumed={bot.Consumed}); read the '[Flow:Build]' reject lines in this run.");
                        yield break;
                    }
                    FlowTrace.Step(Tag, $"AssertFoundingArc: link 3a PASS — Hollow placement committed (StructurePlaced('{placedId}')).");

                    float sw = 0f;
                    while (sw < 2f && !DeNelle.Core.Tutorial.TutorialSignals.HasFired(hollowSignal)) { sw += Time.deltaTime; yield return null; }
                    if (!DeNelle.Core.Tutorial.TutorialSignals.HasFired(hollowSignal))
                    {
                        _lastDetail = "FAIL link 3 — per-item signal never raised";
                        FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 3 — StructurePlaced fired but '{hollowSignal}' never raised within 2s (TutorialSignalAdapters.OnStructurePlaced not raising the WO-702 per-item id).");
                        yield break;
                    }
                    FlowTrace.Step(Tag, $"AssertFoundingArc: link 3 PASS — '{hollowSignal}' raised.");

                    // ── link 3b: the FIRST Hollow build is FREE (owner 2026-07-13 evening) ──
                    // The freebie was live at arm time, so the committed placement must have
                    // charged NOTHING (wood/iron not below the post-grant snapshot) and burned
                    // the one-shot per-id flag into FreeBuildsUsed (never resets).
                    if (freebieWasLive)
                    {
                        int woodAfter = econ != null ? econ.Wood : st.Wood;
                        int ironAfter = econ != null ? econ.Iron : st.Iron;
                        if (woodAfter < woodBeforePlace || ironAfter < ironBeforePlace)
                        {
                            _lastDetail = $"FAIL link 3b — FREE first Hollow charged the ledger (wood {woodBeforePlace}->{woodAfter}, iron {ironBeforePlace}->{ironAfter})";
                            FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 3b — the Hollow's first-build freebie was live but the ledger DROPPED (wood {woodBeforePlace}->{woodAfter}, iron {ironBeforePlace}->{ironAfter}); Place() charged instead of consuming the freebie.");
                            yield break;
                        }
                        bool flagBurned = false;
                        if (st.FreeBuildsUsed != null)
                            foreach (var fid in st.FreeBuildsUsed)
                                if (string.Equals(fid, HollowId, StringComparison.OrdinalIgnoreCase)) { flagBurned = true; break; }
                        if (!flagBurned)
                        {
                            _lastDetail = "FAIL link 3b — freebie paid but 'pet-house' missing from FreeBuildsUsed";
                            FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 3b — the Hollow placed free but FreeBuildsUsed does not contain '{HollowId}' (got [{(st.FreeBuildsUsed == null ? "<null>" : string.Join(",", st.FreeBuildsUsed))}]); the one-shot flag did not burn — a re-place would be free again.");
                            yield break;
                        }
                        FlowTrace.Step(Tag, $"AssertFoundingArc: link 3b PASS — first Hollow build was FREE (wood {woodBeforePlace}->{woodAfter}, iron {ironBeforePlace}->{ironAfter}) and FreeBuildsUsed now contains '{HollowId}' (one-shot burned).");
                    }
                    else
                        FlowTrace.Step(Tag, "AssertFoundingArc: link 3b N/A — the pet-house freebie was already consumed on this save (idempotent rerun).");
                }
                finally
                {
                    BuildModeController.StructurePlaced -= onPlaced;
                    ctrl.SetInput(null);
                    if (ctrl.IsActive) ctrl.Exit();
                }
            }

            // ── link 4: the ARRIVE-beat (enter-side) starter-pet grant ───────────
            // WO-1012 P2: the grant moved from founding_hollow COMPLETION to
            // founding_greet ENTER — the guide IS the pet-Echo and must exist before
            // it speaks. Asserted hard whenever the flow demonstrably passed the
            // ARRIVE beat this run (greet completed above) or the arc already
            // progressed on this save (flow past greet / Hollow standing); a probe run
            // where the flow never entered the chain leaves the grant correctly untriggered.
            float gw = 0f;
            bool granted = false;
            while (gw < 6f)
            {
                int petsNow = st.Pets != null ? st.Pets.Count : 0;
                if (!string.IsNullOrEmpty(st.StarterPetId) && (petsNow > petsBefore || petsNow > 0)) { granted = true; break; }
                gw += Time.deltaTime;
                yield return null;
            }
            bool flowWasOnHollow = flow != null && string.Equals(flow.CurrentStepId, "founding_hollow", StringComparison.OrdinalIgnoreCase);
            if (granted)
                FlowTrace.Step(Tag, $"AssertFoundingArc: link 4 PASS — starter pet granted (Pets={(st.Pets != null ? st.Pets.Count : 0)}, StarterPetId='{st.StarterPetId}').");
            else if (greetCompleted || flowWasOnHollow || hollowAlreadyPlaced)
            {
                _lastDetail = "FAIL link 4 — ARRIVE beat passed but no pet grant (Pets/StarterPetId unchanged)";
                FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 4 — the ARRIVE beat is behind us (greetCompleted={greetCompleted}, onHollow={flowWasOnHollow}, hollowPlaced={hollowAlreadyPlaced}) but GameState.Pets ({petsBefore}→{(st.Pets != null ? st.Pets.Count : 0)}) / StarterPetId ('{st.StarterPetId}') show NO grant within 6s; ApplyStarterPetGrant never fired on founding_greet ENTER (WO-1012 P2 enter-side rule).");
                yield break;
            }
            else
                FlowTrace.Step(Tag, "AssertFoundingArc: link 4 skipped — the flow never entered the ARRIVE beat this run, so the enter-side grant is correctly untriggered.");

            // ── link 6: DEFEND must refuse while the arc is incomplete ───────────
            if (!st.Onboarded)
            {
                var wm = FindAnyObjectByType<WaveManager>();
                if (wm == null)
                    FlowTrace.Warn(Tag, "AssertFoundingArc: link 6 SOFT — no WaveManager in scene; DEFEND-refusal unassertable here.");
                else
                {
                    wm.ForceBeginNextWave();   // → GuardedKickoff, which must stand down under the FTUE guard
                    float dw = 0f;
                    while (dw < 2f) { dw += Time.deltaTime; yield return null; }
                    if (wm.Phase != WavePhase.Idle)
                    {
                        _lastDetail = $"FAIL link 6 — DEFEND armed a wave mid-founding (phase '{wm.Phase}')";
                        FlowTrace.Fail(Tag, $"AssertFoundingArc: FAIL at link 6 — ForceBeginNextWave moved the wave phase to '{wm.Phase}' while the founding arc is incomplete; GuardedKickoff's FTUE stand-down is broken.");
                        yield break;
                    }
                    FlowTrace.Step(Tag, "AssertFoundingArc: link 6 PASS — DEFEND refused (wave phase stayed Idle under the FTUE guard).");
                }

                // link 5 (exit): the peace window still holds after everything above.
                if (!TutorialFlow.HostilesSuppressedForTutorial)
                {
                    _lastDetail = "FAIL link 5 — peace window dropped mid-arc";
                    FlowTrace.Fail(Tag, "AssertFoundingArc: FAIL at link 5 — HostilesSuppressedForTutorial went FALSE while the arc is still incomplete (the peace window dropped mid-founding).");
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertFoundingArc: link 5 (exit) PASS — peace window still held.");
            }

            _lastDetail = "PASS — founding-arc chain intact (guide body, greet, Hollow, grant, peace, DEFEND-refusal)";
            FlowTrace.Step(Tag, "AssertFoundingArc: PASS — the WO-702 founding-arc chain is intact end-to-end.");
        }

        // =====================================================================
        //  AssertTouchVerbBarRenderable — WO-677 / MOB-1 §12 proof capture
        // ---------------------------------------------------------------------
        // The touch verb bar (Rotate ⟲⟳ + Cancel) is the ONLY touch exit from the
        // armed place state; if it cannot render, tap-select (and so Move/Sell/
        // Upgrade) is unreachable on mobile. The bar never instantiates on
        // desktop (EnsureTouchInput gates on Input.touchSupported), so this phase
        // runs its REAL construction path here and asserts renderability:
        //   1. census every UIDocument that carries a PanelSettings (what
        //      AdoptPanelSettings could adopt — names the web-vs-dev-build story),
        //   2. instantiate LeanTouchBuildDriver (Awake → AdoptPanelSettings runs
        //      for real; its ':239' warning lands in this log when nothing is
        //      adoptable) and Install() it (builds the bar),
        //   3. verdict: renderable = a uGUI Cancel button under the driver OR a
        //      UIDocument with a non-null PanelSettings. Neither → FAIL (the
        //      WO-677 proving line). Cleans up; read-only over the scene.
        // =====================================================================
        private IEnumerator AssertTouchVerbBarRenderable()
        {
            const string Tag = "Auto";

            // 1) Census — every candidate PanelSettings source in the live scene.
            int withPs = 0;
            var census = new System.Text.StringBuilder();
            foreach (var doc in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsInactive.Include))
            {
                if (doc == null || doc.panelSettings == null) continue;
                withPs++;
                census.Append('\'').Append(doc.gameObject.name).Append("'(sort=").Append(doc.sortingOrder).Append(") ");
            }
            FlowTrace.Step(Tag, "TouchVerbBar census: UIDocuments-with-PanelSettings=" + withPs +
                (withPs > 0 ? " → " + census.ToString().TrimEnd() : " (nothing for AdoptPanelSettings to adopt)"));

            // 2) Run the driver's REAL construction path.
            var go = new GameObject("WO677_TouchVerbBarProbe");
            LeanTouchBuildDriver driver = null;
            try { driver = go.AddComponent<LeanTouchBuildDriver>(); }   // Awake → AdoptPanelSettings NOW
            catch (Exception ex)
            {
                _lastDetail = "driver AddComponent threw: " + ex.Message;
                FlowTrace.Fail(Tag, "AssertTouchVerbBarRenderable: LeanTouchBuildDriver.AddComponent THREW — " + ex.Message);
                UnityEngine.Object.Destroy(go);
                yield break;
            }
            yield return null;
            driver.Install(HighestDepthScreenCamera());                // builds the bar (EnsureBuilt)
            yield return null;

            // 3) Verdict — is the bar renderable by EITHER construction? The uGUI bar's
            // Cancel is found by its stable GO name "BuildTouchCancel" (kit labels are
            // TMP and DevTools carries no TMPro ref; same pattern as "CloseButton").
            bool ugui = false;
            foreach (var b in go.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (b != null && b.gameObject.name.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                { ugui = true; break; }
            }
            var barDoc = go.GetComponent<UnityEngine.UIElements.UIDocument>();
            bool uitkRenderable = barDoc != null && barDoc.panelSettings != null;

            if (ugui)
            {
                _lastDetail = "PASS — code-built uGUI Cancel present (UIDocument dependency gone)";
                FlowTrace.Step(Tag, "AssertTouchVerbBarRenderable: PASS — touch verb bar is code-built uGUI; Cancel renders without any PanelSettings adoption.");
            }
            else if (uitkRenderable)
            {
                _lastDetail = "adopted PanelSettings (UITK path live) — re-diagnose per WO-677 candidates 6";
                FlowTrace.Warn(Tag, "AssertTouchVerbBarRenderable: bar ADOPTED a PanelSettings (census above names the source) — the UITK bar WOULD render in THIS build; if mobile web still can't cancel, the adoptable doc is dev-build-only or a WO-677 candidate-6 suppressor is eating the tap. RE-DIAGNOSE before fixing.");
            }
            else
            {
                _lastDetail = "FAIL — no PanelSettings adoptable + no uGUI bar: Cancel can never render on touch";
                FlowTrace.Fail(Tag, "AssertTouchVerbBarRenderable: FAIL — the touch verb bar has NO PanelSettings to adopt and NO uGUI construction (census=" + withPs + "). Rotate/Cancel silently never draw on a touch device → the armed state is inescapable → tap-select/Move/Sell unreachable (WO-677 root CONFIRMED).");
            }

            UnityEngine.Object.Destroy(go);
        }

        // =====================================================================
        //  PickRepeatableArmEntry — WO-707 friendly-fire guard (fleet 2026-07-13,
        //  10/12 runs): AssertFoundingArc places the SINGLETON 'pet-house' (it IS
        //  the founding beat), so any LATER probe link that re-arms ps.itemId hits
        //  the WO-707 singleton gate ("Already built" — BuildModeController.
        //  SingletonAlreadyBuilt reads BaseLayout) and dies as a false FAIL:
        //    [Flow:Auto] AssertBuildMoveChain: FAIL at link DPAD — ArmById('pet-house') refused.
        //  Outside the founding beat, probes must arm a REPEATABLE id: prefer the
        //  requested id when it is not singleton, else the storage containers
        //  (lumberyard/foundry/silo — repeatable by design), else the tower.
        // =====================================================================
        private static DeNelle.Core.Catalog.CatalogEntry PickRepeatableArmEntry(params string[] preferredIds)
        {
            foreach (var id in preferredIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var e = DeNelle.Core.Catalog.CatalogRegistry.Get(id);
                if (e == null) continue;
                if (e.repo != null && e.repo.singleton)
                {
                    FlowTrace.Step("Auto", $"PickRepeatableArmEntry: '{id}' is repo.singleton — skipped (WO-707 gate would refuse a re-arm); trying the next repeatable id.");
                    continue;
                }
                return e;
            }
            return null;
        }

        // =====================================================================
        //  AssertBuildMoveChain — WO-677 Lane D: arm→cancel→select→move→commit
        //  + WO-683 link DPAD: drive HudMoveInput → armed ghost cell changes
        // ---------------------------------------------------------------------
        // The exact chain the owner cannot complete on mobile (MOB-1). Drives the
        // REAL paths: the controller's UI-cancel latch (RequestUiCancel — the
        // web-safe latch pattern proven by the PLACE button), a real injected
        // click through IBuildInput for tap-select, ProbeBeginMoveSelected (the
        // Move handler's target), and a real click to commit the move. PASS =
        // the persisted layout record changed cell. WO-683 extends the chain:
        // re-arm, write the REAL HudMoveInput static (the seam the build d-pad
        // publishes + BuildModeController's reflection merge reads), assert the
        // armed ghost's cell changes (ProbeArmedGhostCell).
        // =====================================================================
        private IEnumerator AssertBuildMoveChain()
        {
            const string Tag = "Auto";
            const string TowerId = "tower_ground_archer";

            var ctrl = UnityEngine.Object.FindFirstObjectByType<BuildModeController>();
            if (ctrl == null)
            {
                _lastDetail = "no BuildModeController — skipped";
                FlowTrace.Warn(Tag, "AssertBuildMoveChain: no BuildModeController in scene — skipped.");
                yield break;
            }
            if (!ctrl.IsActive) ctrl.Enter();
            yield return null; yield return null;
            if (!ctrl.IsActive)
            {
                _lastDetail = "Enter() refused";
                FlowTrace.Fail(Tag, "AssertBuildMoveChain: BuildModeController.Enter() did not activate — chain untestable.");
                yield break;
            }

            var bot = new BotBuildInput();
            ctrl.SetInput(bot);
            try
            {
                var cam = HighestDepthScreenCamera();
                var grid = PlacementGrid.Instance;
                if (cam == null || grid == null)
                {
                    _lastDetail = "cam=" + (cam != null) + " grid=" + (grid != null);
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: no screen camera / PlacementGrid — cannot project clicks.");
                    yield break;
                }

                // 0) A placed structure to edit — prefer one left by AssertTutorialFirstTower;
                //    otherwise place one here through the real place path.
                var ps = UnityEngine.Object.FindFirstObjectByType<PlacedStructure>();
                if (ps == null)
                {
                    var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(TowerId);
                    if (entry == null)
                    {
                        _lastDetail = "'" + TowerId + "' not in catalog and no PlacedStructure in scene";
                        FlowTrace.Fail(Tag, "AssertBuildMoveChain: nothing placed and '" + TowerId + "' missing from catalog.");
                        yield break;
                    }
                    GrantCost(BuildModeController.CostFor(entry));
                    if (!ctrl.ArmById(TowerId))
                    {
                        _lastDetail = "ArmById failed";
                        FlowTrace.Fail(Tag, "AssertBuildMoveChain: ArmById('" + TowerId + "') failed — cannot seed a structure.");
                        yield break;
                    }
                    Vector2 seedSp;
                    if (!TryFindValidArmedPoint(ctrl, cam, grid, out seedSp))
                    {
                        _lastDetail = "no valid seed cell";
                        FlowTrace.Fail(Tag, "AssertBuildMoveChain: no valid placement point found to seed the chain.");
                        yield break;
                    }
                    bot.Click(seedSp);
                    float sw = 0f;
                    while (sw < 2f && UnityEngine.Object.FindFirstObjectByType<PlacedStructure>() == null)
                    { sw += Time.deltaTime; yield return null; }
                    ps = UnityEngine.Object.FindFirstObjectByType<PlacedStructure>();
                    if (ps == null)
                    {
                        _lastDetail = "seed placement never committed";
                        FlowTrace.Fail(Tag, "AssertBuildMoveChain: seed placement click never produced a PlacedStructure.");
                        yield break;
                    }
                }
                var startCell = ps.gridCell;

                // 1) ARM, then CANCEL through the WO-677 UI latch — the armed state must exit.
                //    WO-707: never re-arm a singleton the founding beat already placed
                //    (ps can BE the pet-house) — pick a repeatable id instead.
                var entry2 = PickRepeatableArmEntry(ps.itemId, "lumberyard", "foundry", "silo", TowerId);
                if (entry2 != null)
                {
                    GrantCost(BuildModeController.CostFor(entry2));
                    if (ctrl.ArmById(entry2.id))
                    {
                        yield return null;
                        ctrl.RequestUiCancel();
                        yield return null; yield return null;
                        if (ctrl.HasArmedEntry)
                        {
                            _lastDetail = "RequestUiCancel did NOT exit the armed state";
                            FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link CANCEL — RequestUiCancel latch consumed but the armed state persists (the touch exit is broken; this is the MOB-1 trap).");
                            yield break;
                        }
                        FlowTrace.Step(Tag, "AssertBuildMoveChain: link CANCEL PASS — RequestUiCancel exits the armed state to idle.");
                    }
                }

                // 2) TAP-SELECT the placed structure through the real idle select loop.
                Vector3 sp3 = cam.WorldToScreenPoint(ps.transform.position + Vector3.up * 0.5f);
                if (sp3.z <= 0f)
                {
                    _lastDetail = "structure behind camera — select untestable";
                    FlowTrace.Warn(Tag, "AssertBuildMoveChain: placed structure projects behind the camera — select link skipped.");
                    yield break;
                }
                bot.Click(new Vector2(sp3.x, sp3.y));
                float w = 0f;
                var selUi = (BuildSelectionUI)null;
                while (w < 2f)
                {
                    selUi = UnityEngine.Object.FindFirstObjectByType<BuildSelectionUI>();
                    if (selUi != null && selUi.gameObject.activeInHierarchy) break;
                    w += Time.deltaTime; yield return null;
                }
                if (selUi == null || !selUi.gameObject.activeInHierarchy)
                {
                    _lastDetail = "tap on structure never showed BuildSelectionUI";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link SELECT — idle click on the placed structure never opened the Move/Sell panel (read the '[Flow:Build] SelectLoop:' lines above — they name the dead link).");
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertBuildMoveChain: link SELECT PASS — BuildSelectionUI shown for '" + ps.itemId + "'.");

                // 3) MOVE via the Move handler's target, then commit at a fresh valid point.
                if (!ctrl.ProbeBeginMoveSelected())
                {
                    _lastDetail = "ProbeBeginMoveSelected refused";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link MOVE — BeginMoveSelected did not enter move mode with a live selection.");
                    yield break;
                }
                bool committed = false;
                for (int attempt = 0; attempt < 6 && !committed; attempt++)
                {
                    Vector2 mp;
                    if (!TryFindNearbyGroundPoint(cam, grid, startCell, 2 + attempt * 2, out mp)) continue;
                    bot.Click(mp);
                    float cw = 0f;
                    while (cw < 1.2f && ps.gridCell == startCell) { cw += Time.deltaTime; yield return null; }
                    committed = ps.gridCell != startCell;
                }
                if (!committed)
                {
                    _lastDetail = "move never committed (cell unchanged " + startCell + ")";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link COMMIT — move mode entered but no click committed a new cell (all candidates invalid, or the move-loop confirm is broken).");
                    yield break;
                }

                // 4) WO-683 — D-PAD: re-arm, park the ghost at a valid point (aim only, no
                //    click), then drive the SAME seam the build merge reads —
                //    DeNelle.HUD.Kit.HudMoveInput (DevTools references DeNelle.HUD, so this
                //    writes the REAL static the build-overlay d-pad publishes by reflection)
                //    — and assert the armed ghost's grid cell CHANGES (the pad pans the
                //    view, the ghost ray re-lands: exactly the arrow-key behavior).
                //    WO-707 (fleet 2026-07-13, 10/12 runs): ps.itemId was the founding
                //    beat's SINGLETON 'pet-house'; re-arming it here hit the singleton
                //    gate — pick a repeatable id (containers/tower) for the DPAD arm.
                var entry3 = PickRepeatableArmEntry(ps.itemId, "lumberyard", "foundry", "silo", TowerId);
                if (entry3 == null)
                {
                    _lastDetail = "no catalog entry for the DPAD link";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link DPAD — no catalog entry to arm.");
                    yield break;
                }
                GrantCost(BuildModeController.CostFor(entry3));
                if (!ctrl.ArmById(entry3.id))
                {
                    _lastDetail = "ArmById failed for the DPAD link";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link DPAD — ArmById('" + entry3.id + "') refused.");
                    yield break;
                }
                Vector2 parkSp;
                if (!TryFindValidArmedPoint(ctrl, cam, grid, out parkSp))
                {
                    _lastDetail = "no valid park point for the DPAD link";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link DPAD — no valid point to park the armed ghost.");
                    yield break;
                }
                bot.PointAt(parkSp);                       // aim only — nothing places
                yield return null; yield return null;      // let the place loop track the ghost
                Vector2Int ghostStart;
                if (!ctrl.ProbeArmedGhostCell(out ghostStart))
                {
                    _lastDetail = "ProbeArmedGhostCell refused (no armed ghost)";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link DPAD — armed but ProbeArmedGhostCell returned false (no live ghost/grid).");
                    yield break;
                }
                bool dpadMoved = false;
                Vector2Int ghostNow = ghostStart;
                // Second direction in case the pan clamped at a grid edge on the first.
                Vector2[] dpadDirs = { Vector2.up, Vector2.down };
                for (int d = 0; d < dpadDirs.Length && !dpadMoved; d++)
                {
                    DeNelle.HUD.Kit.HudMoveInput.Set(dpadDirs[d]);
                    float dw = 0f;
                    while (dw < 1.5f && !dpadMoved)
                    {
                        dw += Time.deltaTime;
                        yield return null;
                        dpadMoved = ctrl.ProbeArmedGhostCell(out ghostNow) && ghostNow != ghostStart;
                    }
                    DeNelle.HUD.Kit.HudMoveInput.Set(Vector2.zero);
                }
                ctrl.RequestUiCancel();   // back out the probe's armed entry (return to idle)
                yield return null;
                if (!dpadMoved)
                {
                    _lastDetail = "d-pad drive never changed the armed ghost's cell (stuck at " + ghostStart + ")";
                    FlowTrace.Fail(Tag, "AssertBuildMoveChain: FAIL at link DPAD — HudMoveInput.Move published but the armed ghost's cell never changed (the WO-683 reflection merge is dead — read the '[Flow:Build] HudMoveInput' warn lines above).");
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertBuildMoveChain: link DPAD PASS — HudMoveInput seam moved the armed ghost " + ghostStart + " -> " + ghostNow + " (WO-683).");

                _lastDetail = "PASS — " + ps.itemId + " moved " + startCell + " -> " + ps.gridCell +
                              "; d-pad moved ghost " + ghostStart + " -> " + ghostNow;
                FlowTrace.Step(Tag, "AssertBuildMoveChain: PASS — arm->cancel->select->move->commit->dpad intact; '" + ps.itemId + "' moved " + startCell + " -> " + ps.gridCell + ".");
            }
            finally
            {
                // WO-683 — never leak a held d-pad vector into hero movement if the
                // coroutine aborts mid-drive (HeroLocomotion reads the same static).
                DeNelle.HUD.Kit.HudMoveInput.Set(Vector2.zero);
                ctrl.SetInput(null);
                if (ctrl.IsActive) ctrl.Exit();
            }
        }

        /// <summary>Fund a build cost (test setup, not a bypass — the cost gate still runs).
        /// Same funding path AssertTutorialFirstTower uses.</summary>
        private static void GrantCost(DeNelle.Core.Catalog.ResourceCost cost)
        {
            var econ = EconomyService.Instance;
            if (econ != null) econ.Grant(BuildModeController.ToEconomy(cost));
            else DeNelle.Core.State.GameStateService.Instance?.AddCrystals(cost.crystals);
        }

        /// <summary>Ring-sample screen points around the grid centre until the controller's own
        /// reason-aware gate (ProbeArmedPlacementAt) accepts one. Requires an armed entry.</summary>
        private static bool TryFindValidArmedPoint(BuildModeController ctrl, Camera cam, PlacementGrid grid, out Vector2 screenPoint)
        {
            screenPoint = default;
            var centre = new Vector2Int(grid.gridWidth / 2, grid.gridHeight / 2);
            for (int r = 2; r <= 12; r += 2)
            {
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                    var cell = centre + new Vector2Int(dx, dz);
                    if (cell.x < 0 || cell.y < 0 || cell.x >= grid.gridWidth || cell.y >= grid.gridHeight) continue;
                    Vector3 p = cam.WorldToScreenPoint(grid.CellToWorld(cell));
                    if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x >= Screen.width || p.y >= Screen.height) continue;
                    var sp = new Vector2(p.x, p.y);
                    if (ctrl.ProbeArmedPlacementAt(sp, out _)) { screenPoint = sp; return true; }
                }
            }
            return false;
        }

        /// <summary>An on-screen ground point roughly <paramref name="ringRadius"/> cells from
        /// <paramref name="fromCell"/> (move-commit candidates; validity is judged by the live
        /// move loop itself, so this only needs to be on screen).</summary>
        private static bool TryFindNearbyGroundPoint(Camera cam, PlacementGrid grid, Vector2Int fromCell, int ringRadius, out Vector2 screenPoint)
        {
            screenPoint = default;
            var offsets = new[]
            {
                new Vector2Int(ringRadius, 0), new Vector2Int(-ringRadius, 0),
                new Vector2Int(0, ringRadius), new Vector2Int(0, -ringRadius),
                new Vector2Int(ringRadius, ringRadius), new Vector2Int(-ringRadius, -ringRadius),
            };
            foreach (var off in offsets)
            {
                var cell = fromCell + off;
                if (cell.x < 0 || cell.y < 0 || cell.x >= grid.gridWidth || cell.y >= grid.gridHeight) continue;
                Vector3 p = cam.WorldToScreenPoint(grid.CellToWorld(cell));
                if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x >= Screen.width || p.y >= Screen.height) continue;
                screenPoint = new Vector2(p.x, p.y);
                return true;
            }
            return false;
        }

        private IEnumerator RunPhase(string name, IEnumerator phase, bool abortIfFailed = false)
        {
            if (_abortRun) yield break;
            if (!PhaseAllowed(name))
            {
                FlowTrace.Step("Auto", $"PHASE SKIPPED by --phases filter: {name}");
                yield break;
            }

            float start = Time.realtimeSinceStartup;
            FlowTrace.Step("Auto", $"PHASE ENTER: {name}");

            float timeout = TimeoutFor(name);
            bool timedOut = false;
            bool threw = false;
            string error = null;

            while (true)
            {
                // global cap
                if (Time.realtimeSinceStartup - _runStartRealtime > GlobalCapSeconds)
                {
                    FlowTrace.Fail("Auto", $"GLOBAL CAP ({GlobalCapSeconds:0}s) exceeded during '{name}' — aborting run.");
                    _abortRun = true;
                    timedOut = true;
                    break;
                }
                // per-phase realtime watchdog
                if (Time.realtimeSinceStartup - start > timeout)
                {
                    FlowTrace.Fail("Auto", $"{name} TIMEOUT (>{timeout:0}s) — advancing.");
                    timedOut = true;
                    break;
                }

                bool moveNext;
                try
                {
                    moveNext = phase.MoveNext();
                }
                catch (Exception ex)
                {
                    threw = true;
                    error = ex.Message;
                    FlowTrace.Fail("Auto", $"{name} THREW: {ex.Message}");
                    break;
                }
                if (!moveNext) break;          // phase finished normally
                yield return phase.Current;    // honour the phase's own yields
            }

            float dur = Time.realtimeSinceStartup - start;
            string status = threw ? "threw" : (timedOut ? "timeout" : "ok");
            _phases.Add(new PhaseResult { phase = name, status = status, seconds = dur, detail = error ?? _lastDetail });
            _lastDetail = null;
            FlowTrace.Step("Auto", $"PHASE EXIT: {name} ({status}, {dur:0.0}s)");

            if (abortIfFailed && status != "ok")
            {
                FlowTrace.Warn("Auto", $"Critical phase '{name}' did not pass — ending run early.");
                _abortRun = true;
            }
        }

        // A phase can stash a one-line detail (e.g. counts) for the summary row.
        private string _lastDetail;

        // =====================================================================
        //  PHASE: DiagGarrisonRoster (tickets #2 + #4 deterministic capture)
        //  Builds the real village2_stronghold roster via the canonical
        //  GarrisonStatBlocks -> EnemyFactory.Build path, in the CURRENT scene, so
        //  the orc/troll/hollow bodies actually instantiate (the chaos walk never
        //  reaches the garrison in time). Each Build self-reports via EnemyFactory's
        //  render-verify + the worldUp trace + TripoMatFix VERIFY lines, which is the
        //  data that proves: (#4) orcs render URP not magenta after the fixer fix;
        //  (#2) the troll's worldUp (tipped vs upright). Then warp the hero and dump
        //  its body/renderer/onMesh state (the #2 bare-pill hero-side check). Cleans up.
        // =====================================================================
        private IEnumerator DiagGarrisonRoster()
        {
            string[] roster = { "orc-berserker", "orc-shaman", "orc-raider", "troll", "hollow-warrior", "hollow-walker" };
            Vector3 around = _hero != null ? _hero.transform.position : Vector3.zero;
            var root = new GameObject("[DiagGarrisonRoster]").transform;
            int built = 0, magenta = 0, tipped = 0;

            for (int i = 0; i < roster.Length; i++)
            {
                string id = roster[i];
                Vector3 want = around + new Vector3((i - 2) * 2.5f, 0f, 7f);
                if (UnityEngine.AI.NavMesh.SamplePosition(want, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas)) want = hit.position;

                Enemy enemy = null;
                try
                {
                    EnemyDef def = DeNelle.Village.World.Camps.GarrisonStatBlocks.BuildTypedDef(id, 1);
                    enemy = EnemyFactory.Build(def, want, Quaternion.identity, root);   // logs render-verify + worldUp (+ TripoMatFix)
                }
                catch (Exception ex)
                {
                    FlowTrace.Fail("DiagRoster", $"build '{id}' threw {ex.GetType().Name}: {ex.Message}");
                }

                // Let the body skin + the Tripo material fixer run a few frames before we read shaders.
                yield return Wait(0.35f);

                if (enemy != null)
                {
                    built++;
                    bool anyMagenta = false; Vector3 up = Vector3.up; bool haveUp = false;
                    foreach (var r in enemy.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var m = r.sharedMaterial;
                        string sh = (m != null && m.shader != null) ? m.shader.name : "<null>";
                        bool isMagenta = sh.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0
                                      || sh.IndexOf("Hidden/", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isMagenta) anyMagenta = true;
                        if (!haveUp) { up = r.transform.up; haveUp = true; }
                        FlowTrace.Step("DiagRoster", $"'{id}' renderer '{r.name}' shader='{sh}' magenta={isMagenta}");
                    }
                    bool isTipped = Vector3.Angle(up, Vector3.up) > 30f;   // >30deg off vertical = laid over
                    if (anyMagenta) magenta++;
                    if (isTipped) tipped++;
                    FlowTrace.Step("DiagRoster",
                        $"'{id}' SUMMARY magenta={anyMagenta} worldUp={up} tipped={isTipped} (upright iff worldUp~=(0,1,0)).");
                }
            }

            // #2 hero-side: warp the hero and confirm it keeps its skinned body + lands on navmesh
            // (the bare-pill arrival theory). WarpTo self-logs SamplePosition HIT/MISS + isOnNavMesh.
            if (_hero != null)
            {
                int bodyRenderers = 0;
                foreach (var smr in _hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr != null && smr.enabled && smr.sharedMesh != null) bodyRenderers++;
                Vector3 dest = around + new Vector3(0f, 0f, 9f);
                _hero.WarpTo(dest);
                yield return Wait(0.2f);
                int afterRenderers = 0;
                foreach (var smr in _hero.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr != null && smr.enabled && smr.sharedMesh != null) afterRenderers++;
                FlowTrace.Step("DiagRoster",
                    $"HERO post-warp body skinned-renderers before={bodyRenderers} after={afterRenderers} name='{_hero.name}' " +
                    $"(bare-pill iff after==0).");
            }

            yield return Wait(0.3f);
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            _lastDetail = $"roster built {built}/{roster.Length}, magenta={magenta}, tipped={tipped}";
            FlowTrace.Step("DiagRoster", $"DiagGarrisonRoster done — built {built}/{roster.Length}, magenta={magenta}, tipped={tipped}.");
        }

        // =====================================================================
        //  PHASE: AssertHeroCrossing — walk the hero into a HeroLinkCrossing entry
        //  and confirm it WARPS to the paired destination (owner 2026-06-21, headless
        //  test of the Village2 gate crossing). Skips cleanly if no crossing pair.
        // =====================================================================
        private IEnumerator AssertHeroCrossing()
        {
            if (_hero == null) { _lastDetail = "no hero"; yield break; }
            // Pick the CLOSEST enterable crossing to the hero (the one it can actually walk to on its island).
            HeroLinkCrossing entry = null; float best = 9999f;
            foreach (var c in HeroLinkCrossing.Registry)
            {
                if (c == null || !c.bidirectional || c.Partner() == null) continue;
                float d = HorizontalDistance(_hero.transform.position, c.transform.position);
                if (d < best) { best = d; entry = c; }
            }
            if (entry == null) { _lastDetail = "no HeroLinkCrossing pair"; FlowTrace.Step("Auto", "AssertHeroCrossing: no crossing pair in scene — skipping."); yield break; }

            Vector3 destPos = entry.Partner().transform.position;
            FlowTrace.Step("Auto", $"AssertHeroCrossing: nearest crossing '{entry.crossingId}' @ {entry.transform.position} " +
                                   $"(d={best:F1}); walking hero {_hero.transform.position} into it; partner @ {destPos}.");
            // WO-530: capture pre-test pose + flag this as an INTENTIONAL warp phase so the continuous
            // SEAM/wall probes don't false-fire while the hero is deliberately displaced ~7km.
            Vector3 home = _hero.transform.position; Quaternion homeRot = _hero.transform.rotation;
            _probes?.SetIntentionalCrossPhase(true);
            _hero.SetAutoWalk(entry.transform);

            // A REAL warp = a single-frame position JUMP far larger than a walk step (not mere proximity).
            bool warped = false; bool reachedEntry = false;
            Vector3 lastPos = _hero.transform.position;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 14f)
            {
                if (_hero == null) break;
                Vector3 pos = _hero.transform.position;
                CaptureBridgeCrossing(pos);   // feet-on-stone shot at the bridge mouth (t≈9s, cap-proof)
                if (HorizontalDistance(pos, entry.transform.position) < entry.enterRadius + 0.5f) reachedEntry = true;
                if (HorizontalDistance(pos, lastPos) > 6f) { warped = true; break; }   // teleport, not a step
                lastPos = pos;
                yield return null;
            }
            if (_hero != null) _hero.ClearAutoWalk();
            if (warped)
            {
                FlowTrace.Step("Auto", $"AssertHeroCrossing: CROSSED '{entry.crossingId}' — real warp jump, hero now {_hero.transform.position}.");
                _lastDetail = "crossing OK (warp fired)";
                // Post-warp internal navigation: from the landing point, can the hero PATH to the keep / chokepoint?
                Vector3 land = _hero.transform.position;
                foreach (var n in new[] { "Spawn_Keep", "Spawn_Chokepoint", "Spawn_Rear" })
                {
                    var go = GameObject.Find(n);
                    if (go == null) continue;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(land, out var a, 4f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(go.transform.position, out var b, 6f, UnityEngine.AI.NavMesh.AllAreas))
                    { FlowTrace.Warn("Auto", $"post-warp: '{n}' has no navmesh nearby."); continue; }
                    var p = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, UnityEngine.AI.NavMesh.AllAreas, p);
                    FlowTrace.Step("Auto", $"post-warp inside-nav: landing -> '{n}': status={p.status} (PathComplete = walkable inside).");
                }
            }
            else
            {
                // PROXIMITY FALLBACK (RCA 2026-06-29): the crossing warp ARMS on proximity
                // (the hero within enterRadius of the marker), NOT on a strict navmesh-complete
                // walk. A NavMeshAgent can stop a few cm short at a PathPartial edge — the field
                // capture was `hero -> seam: PathPartial, lastCornerToTarget = 0.10m` — so the
                // strict "did it physically jump?" drive never registers a warp even though the
                // seam IS reachable (the proximity warp would arm). Before hard-failing, recompute
                // the nav path to the marker: if its last corner lands within the firing radius,
                // treat it as PASS-with-note. A path that gets NOWHERE NEAR the marker (off-mesh /
                // PathInvalid / far) STILL fails — no false-green is introduced.
                float prox = Mathf.Max(0.5f, entry != null ? entry.enterRadius : 2f) + 0.5f;
                float lastCornerDist = float.PositiveInfinity;
                var proxStatus = UnityEngine.AI.NavMeshPathStatus.PathInvalid;
                if (_hero != null && entry != null
                    && UnityEngine.AI.NavMesh.SamplePosition(_hero.transform.position, out var hHit, 3f, UnityEngine.AI.NavMesh.AllAreas)
                    && UnityEngine.AI.NavMesh.SamplePosition(entry.transform.position, out var sHit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    var pp = new UnityEngine.AI.NavMeshPath();
                    UnityEngine.AI.NavMesh.CalculatePath(hHit.position, sHit.position, UnityEngine.AI.NavMesh.AllAreas, pp);
                    proxStatus = pp.status;
                    int cc = pp.corners != null ? pp.corners.Length : 0;
                    if (cc > 0) lastCornerDist = HorizontalDistance(pp.corners[cc - 1], entry.transform.position);
                }
                bool reachableByProximity = lastCornerDist <= prox;
                if (reachableByProximity)
                {
                    FlowTrace.Step("Auto", $"AssertHeroCrossing: PASS-with-note on '{entry?.crossingId}' — no single-frame warp jump, " +
                        $"but the nav path's last corner closes to {lastCornerDist:0.00}m <= proximity {prox:0.0}m (path={proxStatus}); " +
                        "the crossing's proximity warp arms here, so the seam IS reachable (the strict walk stopped short at a PathPartial edge — not a defect).");
                    _lastDetail = "crossing OK (reachable by proximity)";
                }
                else
                {
                    FlowTrace.Fail("Auto", $"AssertHeroCrossing: NO warp on '{entry?.crossingId}' — reachedEntry={reachedEntry}, " +
                        $"path last corner {lastCornerDist:0.0}m from marker > proximity {prox:0.0}m (path={proxStatus}), " +
                        $"hero {(_hero != null ? _hero.transform.position.ToString() : "<null>")}. The marker is genuinely unreachable on the hero's navmesh island.");
                    _lastDetail = "crossing FAILED";
                }
            }

            // WO-530: restore the hero to its pre-test position before the next phase. Leaving it at the
            // ~7km partner-region landing made WalkToEachGate + the continuous SEAM/wall probes measure
            // from 7045m on a different navmesh island — the false SEAM-UNREACHABLE / wall-clip / 0-of-5
            // report. Restore + drop the intentional-cross flag.
            if (_hero != null) _hero.WarpTo(home, homeRot);
            _probes?.SetIntentionalCrossPhase(false);
            FlowTrace.Step("Auto", "AssertHeroCrossing: restored hero to pre-test position.");
        }

        // =====================================================================
        //  PROBE: AssertExactlyOneGuideBody — the ONE-GUIDE lock (WO-971)
        // ---------------------------------------------------------------------
        // OWNER RULING 2026-08-10, verbatim: "why are two tutorials active?" /
        // "remove the original" / "only the new wolf one stays".
        //
        // PROVEN BY CAPTURE (her Player.log, the 20:42 build) — four lines that
        // together ARE the bug, which is why this probe exists at all:
        //   [Flow:SylasSteward] Sylas steward spawned at (2.00, 0.08, 3.00)
        //   [Flow:Tutorial] guide BODY summoned ('ice-wolf') at (2.00, 0.06, 3.00)
        //   [Flow:Tutorial] FocusMask resolved highlightId=world.guide target=Sylas
        //   [Flow:Tutorial] FocusMask resolved highlightId=world.guide target=Pet_ice-wolf
        // Two guide bodies two centimetres apart and ONE spotlight alternating
        // between them, while the objective strip read "Follow Aldwin to the gate".
        //
        // SUPERSEDES the FTUE-1 probe AssertStewardSurvivesNewGame, which asserted
        // the exact OPPOSITE (that the steward body respawned on a same-run New
        // Game). That probe was correct for its day and is now the defect's alibi:
        // it would have gone GREEN on the very build the owner rejected. When a
        // ruling reverses, the probe that guarded the old behaviour must be
        // reversed too, not merely deleted — otherwise nothing watches the new one.
        //
        // Links (each failure NAMES the dead link):
        //   link 0  context gates (ff.tutorialv2 / hub / GameStateService) — N/A else
        //   link 1  a fresh save + hub reload through the REAL services
        //   link 2  NO steward body: no GameObject named "Sylas" in the hub
        //   link 3  at most ONE live guide body (DeNelle.Pets.Pet) in the scene
        //   link 4  "world.guide" resolves to that body, or to the Heart — never
        //           to a humanoid stand-in
        // Runs LAST: it resets the save and reloads the scene.
        // =====================================================================
        private IEnumerator AssertExactlyOneGuideBody()
        {
            const string Tag = "Auto";
            FlowTrace.Step(Tag, "AssertExactlyOneGuideBody: ENTER - WO-971 one-guide lock (the owner's 'why are two tutorials active?').");

            // -- link 0: context gates ---------------------------------------
            if (!DeNelle.Core.FeatureFlags.TutorialV2)
            {
                _lastDetail = "ff.tutorialv2 OFF - N/A (skipped)";
                FlowTrace.Step(Tag, "AssertExactlyOneGuideBody: ff.tutorialv2 OFF - N/A, skipping.");
                yield break;
            }
            string scene = ActiveScene();
            if (!DeNelle.Core.HubScenes.IsHub(scene))
            {
                _lastDetail = $"'{scene}' not a hub - N/A (skipped)";
                FlowTrace.Step(Tag, $"AssertExactlyOneGuideBody: scene '{scene}' is not a hub - N/A.");
                yield break;
            }
            var svc = DeNelle.Core.State.GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                _lastDetail = "GameStateService unavailable - N/A (skipped)";
                FlowTrace.Warn(Tag, "AssertExactlyOneGuideBody: GameStateService/State unavailable - N/A.");
                yield break;
            }

            // -- link 1: a genuinely fresh founding arc, via the REAL services --
            try { svc.ResetToNewGame(); }
            catch (Exception ex)
            {
                _lastDetail = "FAIL link 1 - ResetToNewGame threw";
                FlowTrace.Fail(Tag, $"AssertExactlyOneGuideBody: FAIL at link 1 - ResetToNewGame() threw {ex.GetType().Name}: {ex.Message}.");
                yield break;
            }
            string hub = DeNelle.Core.SceneRouter.Castle;
            try { SceneManager.LoadScene(hub); }
            catch (Exception ex)
            {
                _lastDetail = "FAIL link 1 - hub reload threw";
                FlowTrace.Fail(Tag, $"AssertExactlyOneGuideBody: FAIL at link 1 - LoadScene('{hub}') threw {ex.GetType().Name}: {ex.Message} (is it in Build Settings?).");
                yield break;
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < BootTimeout && ActiveScene() != hub) yield return null;
            if (ActiveScene() != hub)
            {
                _lastDetail = "FAIL link 1 - hub never became active after the New-Game reload";
                FlowTrace.Fail(Tag, $"AssertExactlyOneGuideBody: FAIL at link 1 - '{hub}' never became the active scene within {BootTimeout:0}s after the New-Game reload.");
                yield break;
            }
            for (int i = 0; i < 3; i++) yield return null;   // let Awake/Start + sceneLoaded injectors run
            FlowTrace.Step(Tag, $"AssertExactlyOneGuideBody: link 1 PASS - hub '{hub}' reloaded on a fresh save.");

            // Re-resolve the hero (the reload destroyed the old one) - same idiom as the
            // PopupClose recovery reload; keeps any later consumer honest.
            _hero = null;
            t0 = Time.realtimeSinceStartup;
            while (_hero == null && Time.realtimeSinceStartup - t0 < ResolveHeroTimeout)
            {
                _hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                if (_hero != null) break;
                yield return null;
            }

            // -- link 2: NO steward stand-in body seats, at any point ---------
            // Watched over a window, not sampled once: the whole point of the WO-1014
            // failure was that the stand-in seated on hub load and the wolf arrived a
            // beat LATER, so a single early sample sees one body and calls it fine.
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f)
            {
                var standIn = GameObject.Find("Sylas") ?? GameObject.Find("CompanionIntroducer");
                if (standIn != null)
                {
                    _lastDetail = $"FAIL link 2 - stand-in body '{standIn.name}' seated in the hub";
                    FlowTrace.Fail(Tag, $"AssertExactlyOneGuideBody: FAIL at link 2 - a humanoid guide stand-in named '{standIn.name}' is standing in the hub at {standIn.transform.position}. WO-971 removed the stand-in outright (owner: 'remove the original', 'only the new wolf one stays'); something has re-introduced a second guide figure. Read the [Flow:Tutorial] FocusMask lines in this run - if they alternate between two targets, this is the owner's exact 2026-08-10 report reproduced.");
                    yield break;
                }
                yield return null;
            }
            FlowTrace.Step(Tag, "AssertExactlyOneGuideBody: link 2 PASS - no steward/introducer stand-in body seated during the 8s founding window.");

            // -- link 3: at most ONE live guide body in the scene -------------
            // DevTools cannot reference DeNelle.Pets (see the asmdef), so the pet body is
            // classified by its PetDeployer root name "Pet_<species>" - the same idiom
            // AssertFoundingArc uses. Roots only: a child named Pet_* would double-count.
            var bodies = new List<string>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t == null || t.parent != null) continue;
                if ((t.name ?? "").StartsWith("Pet_", StringComparison.OrdinalIgnoreCase)) bodies.Add(t.name);
            }
            if (bodies.Count > 1)
            {
                _lastDetail = $"FAIL link 3 - {bodies.Count} guide bodies live";
                FlowTrace.Fail(Tag, $"AssertExactlyOneGuideBody: FAIL at link 3 - {bodies.Count} live 'Pet_*' bodies in the hub [{string.Join(", ", bodies)}]. The founding arc summons EXACTLY ONE Echo with a world body because a beat tells the player to follow it (WO-961); the rest of the roster stays portrait cards. Two bodies means the spotlight can point at the wrong one.");
                yield break;
            }
            FlowTrace.Step(Tag, $"AssertExactlyOneGuideBody: link 3 PASS - {bodies.Count} live guide body/bodies (<= 1).");

            // -- link 4: the guide anchor resolves to a body or the Heart -----
            // The surviving chain is: live pet-Echo body -> the Heart. A HUMANOID answering
            // here means a body link came back even if no stand-in was found by name.
            var guideT = DeNelle.Village.TutorialWorldAnchors.LiveGuideBody();
            if (guideT == null)
            {
                FlowTrace.Step(Tag, "AssertExactlyOneGuideBody: link 4 - no guide body yet this run; the chain falls to the Heart, which is the intended degradation (never a stand-in character).");
            }
            else if (!(guideT.name ?? "").StartsWith("Pet_", StringComparison.OrdinalIgnoreCase))
            {
                _lastDetail = $"FAIL link 4 - guide resolved to a non-pet '{guideT.name}'";
                FlowTrace.Fail(Tag, $"AssertExactlyOneGuideBody: FAIL at link 4 - LiveGuideBody() answered '{guideT.name}', which is not a PetDeployer 'Pet_<species>' root. The guide is the player's first pet-Echo; anything else answering is a stand-in link returning through a new door.");
                yield break;
            }
            else
            {
                FlowTrace.Step(Tag, $"AssertExactlyOneGuideBody: link 4 PASS - the guide resolves to the pet-Echo body '{guideT.name}'.");
            }

            _lastDetail = "exactly one guide: no stand-in, <= 1 pet body, chain clean";
            FlowTrace.Step(Tag, "AssertExactlyOneGuideBody: PASS - the founding arc has exactly ONE guide figure and no legacy stand-in (WO-971 locked).");
        }

        // =====================================================================
        //  AssertFreshSaveFtue - THE STANDING FRESH-SAVE FTUE LANE (WO-1500)
        // ---------------------------------------------------------------------
        //  WHAT WAS BLIND, on captured evidence and not on inference. Across all
        //  five fleet logs captured 2026-09-06 there were ZERO [Flow:Onboard*]
        //  lines; every one carried 'raid.first_completed already latched' and
        //  echoes=4/6. Every run was a RETURNING save. The only artefacts of
        //  minute one through minute ten were PNGs from 2026-09-01 - five days and
        //  the whole Manage 2000-block earlier. For a retention-limited product the
        //  first ten minutes are the worst place to be unobserved, and NOTHING in
        //  the fleet founded a town of its own to look at them.
        //
        //  WHY THE OTHER FRESH-SAVE PHASES DID NOT COVER IT: AssertTutorialArms and
        //  AssertFoundingArc both open with "on a fresh save" and both go N/A the
        //  moment the boot save is Onboarded (AssertTutorialArms link 2 returns N/A
        //  on st.Onboarded, and again on flow.IsFinished && TutorialFlow
        //  .RanThisSession). They are correct as written - a returning save has no
        //  founding arc to assert - but the consequence is that a fleet running on
        //  a returning save reports GREEN while asserting nothing about the FTUE.
        //  A phase that only runs when someone happens to have deleted the save is
        //  not coverage. This lane FOUNDS THE TOWN ITSELF so the observation cannot
        //  depend on the state the machine happened to boot with.
        //
        //  ⚠ RUN THIS LANE ALONE:  --phases=FreshSaveFtue
        //  Two independent reasons, both measured, not stylistic:
        //    1. TutorialFlow.RanThisSession is PROCESS state. By the tail of a full
        //       sweep the flow has usually already run once (AssertFoundingArc drives
        //       founding_greet; AssertExactlyOneGuideBody founds a New Game just
        //       above), so link 3 can only report SOFT - it cannot prove the flow is
        //       LIVE on a boot that has never seen it. Alone, it can.
        //    2. GlobalCapSeconds is 420 and a full sweep already sits at the budget
        //       (see the AssertDungeonLoop note in TimeoutFor). A lane appended to a
        //       full sweep is the lane most likely to be cut off by the cap - which
        //       would restore the exact silence this phase exists to end.
        //  It stays wired into the default sequence anyway, because a lane that only
        //  exists as a command line is a lane somebody forgets to run.
        //
        //  LINKS (each failure NAMES its link, and every one is a FlowTrace.Fail so
        //  it lands in break-log.jsonl and ranks as a ticket):
        //    link 0  context gates (hub scene / GameStateService) - N/A otherwise
        //    link 1  a genuinely new town: ResetToNewGame through the REAL service,
        //            with the three persisted invariants read IMMEDIATELY, before
        //            the reload can let a cold-load claim advance the clock
        //    link 2  the hub reloads and the hero re-resolves on that new save
        //    link 3  the FTUE is LIVE (a TutorialFlow exists and is not parked
        //            Finished) - the [Flow:Onboard*]/[Flow:Tutorial] silence itself
        //    link 4  the guide BEATS advance: walk the step spine and NAME every
        //            beat id reached, so minute one is in the log by id
        //    link 5  THE FIRST WELCOME-BACK CLAIMS NOTHING. OfflineHarvest case 6
        //            (WO-1414) already pins the MODEL - a fresh GameState carries
        //            LastHarvestClaimMs=0 and an empty ever-built ledger, and an
        //            empty result fails HasSummaryContent. This link pins the FLOW:
        //            the claim that actually runs on a real new town yields a fresh
        //            clock, a ZERO window, and no welcome-back popup on screen.
        //            That is the owner's 2026-09-05 "YOUR REALM WORKED FOR 8h 22m"
        //            on START NEW, watched end to end instead of in a fixture.
        //
        //  ⛔ THE MARKER IS PRINTED ONLY WHEN THE FTUE WAS ACTUALLY OBSERVED - a LIVE
        //  flow AND at least one named beat. Every other outcome prints
        //  FRESH_SAVE_FTUE_SOFT and NAMES its precondition, so the fleet reports
        //  FLEET_LANE_MISSING rather than a green line over an unanswered question.
        //  Printing OK on a run where the flow was flag-gated off or already spent
        //  would reproduce the 2026-09-06 state - a pass with nothing behind it -
        //  inside the very lane written to end it.
        //
        //  Emits FRESH_SAVE_FTUE_OK on success - the marker run-autopilot-fleet.ps1
        //  judges the lane by, on a FRESH per-instance player.log.
        // =====================================================================
        private IEnumerator AssertFreshSaveFtue()
        {
            const string Tag = "Auto";
            FlowTrace.Step(Tag, "AssertFreshSaveFtue: ENTER - WO-1500 standing fresh-save FTUE lane (the first ten minutes, observed).");

            // -- link 0: context gates ---------------------------------------
            string scene = ActiveScene();
            if (!DeNelle.Core.HubScenes.IsHub(scene))
            {
                _lastDetail = $"'{scene}' not a hub - N/A (skipped)";
                FlowTrace.Step(Tag, $"AssertFreshSaveFtue: scene '{scene}' is not a hub - N/A, skipping.");
                yield break;
            }
            var svc = DeNelle.Core.State.GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                _lastDetail = "GameStateService unavailable - N/A (skipped)";
                FlowTrace.Warn(Tag, "AssertFreshSaveFtue: GameStateService/State unavailable - N/A.");
                yield break;
            }

            // ⛔ THE TWO PRECONDITIONS ARE CHECKED HERE, BEFORE THE RESET, AND THEY ARE THE
            // REASON THIS LANE IS NORMALLY RUN ALONE. Both were originally checked further
            // down and both were wrong there:
            //  * ff.tutorialv2 OFF -> there is no guide flow to observe at all, so founding a
            //    town and walking a spine that cannot exist spends the budget of a full sweep
            //    (GlobalCapSeconds is 420 and the sweep already sits at it - see the
            //    AssertDungeonLoop note in TimeoutFor) to answer nothing.
            //  * TutorialFlow.RanThisSession -> s_ranThisSession latches in Bootstrap the
            //    first time a fresh save arms the flow (TutorialFlow.cs:640-643) and is
            //    PROCESS state that no New Game clears. So by the tail of a full sweep the
            //    flow is correctly parked Finished and this lane CANNOT see a first boot.
            //    Checked after the reset, it produced a guaranteed false ticket every night:
            //    the beat walk's `while (!flow.IsFinished)` never entered, zero beats were
            //    recorded, and the lane FAILED a perfectly healthy build.
            // Both exit as N/A - a precondition that cannot be met is not a defect.
            if (!DeNelle.Core.FeatureFlags.TutorialV2)
            {
                _lastDetail = "ff.tutorialv2 OFF - N/A (skipped)";
                FlowTrace.Step(Tag, "FRESH_SAVE_FTUE_SOFT :: ff.tutorialv2 is OFF, so there is no guide flow to observe. Nothing was founded and nothing was asserted.");
                yield break;
            }
            if (TutorialFlow.RanThisSession)
            {
                _lastDetail = "tutorial already ran this process - N/A (run the lane alone)";
                FlowTrace.Step(Tag, "FRESH_SAVE_FTUE_SOFT :: the tutorial flow already ran in THIS PROCESS (TutorialFlow.RanThisSession latches in Bootstrap and no New Game clears it), so a first boot cannot be observed from here and founding another town would only spend the run's remaining budget. Run the lane on its own: --phases=FreshSaveFtue (or run-autopilot-fleet.ps1 -Lane freshsave-ftue).");
                yield break;
            }

            // -- link 1: found a new town, and read the persisted invariants NOW --
            // The reads happen BEFORE the reload on purpose. OfflineHarvestService.Start
            // fires ClaimDeferred("cold-load") on the reloaded scene, and the coordinator
            // STAMPS the clock on every claim (fresh ones included) - so a read taken after
            // the reload would legitimately see a non-zero LastHarvestClaimMs and this link
            // would fail on the fix rather than on the defect.
            try { svc.ResetToNewGame(); }
            catch (Exception ex)
            {
                _lastDetail = "FAIL link 1 - ResetToNewGame threw";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 1 - ResetToNewGame() threw {ex.GetType().Name}: {ex.Message}.");
                yield break;
            }

            var st = svc.State;
            double claimClock = st != null ? st.LastHarvestClaimMs : -1d;
            bool onboarded = st != null && st.Onboarded;
            int everBuilt = (st != null && st.EverBuiltStructureIds != null) ? st.EverBuiltStructureIds.Count : -1;
            if (onboarded || claimClock != 0d || everBuilt != 0)
            {
                _lastDetail = $"FAIL link 1 - onboarded={onboarded} claimClock={claimClock:0} everBuilt={everBuilt}";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 1 - ResetToNewGame() left Onboarded={onboarded}, " +
                                    $"LastHarvestClaimMs={claimClock:0} and {everBuilt} ever-built id(s). A new town must carry " +
                                    "Onboarded=false (or the FTUE never arms), a ZERO harvest clock (or the coordinator's fresh-clock " +
                                    "arm is skipped and the PREVIOUS save's away window is paid out - the owner's 2026-09-05 " +
                                    "'YOUR REALM WORKED FOR 8h 22m' on START NEW) and an EMPTY ever-built ledger (or the harvest tick " +
                                    "pays buildings this town does not have). OfflineHarvestRegression case 6 pins the same three on " +
                                    "the model; this is the live service disagreeing with it.");
                yield break;
            }
            int claimsBefore = OfflineClaimCoordinator.ClaimCount;
            FlowTrace.Step(Tag, $"AssertFreshSaveFtue: link 1 PASS - new town founded (Onboarded=false, LastHarvestClaimMs=0, " +
                                $"ever-built ledger empty, newGameThisProcess={OfflineClaimCoordinator.NewGameThisProcess}); " +
                                $"claims so far this process = {claimsBefore}.");

            // -- link 2: the hub reloads on that new save, and a hero re-resolves --
            string hub = DeNelle.Core.SceneRouter.Castle;
            try { SceneManager.LoadScene(hub); }
            catch (Exception ex)
            {
                _lastDetail = "FAIL link 2 - hub reload threw";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 2 - LoadScene('{hub}') threw {ex.GetType().Name}: {ex.Message} (is it in Build Settings?).");
                yield break;
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < BootTimeout && ActiveScene() != hub) yield return null;
            if (ActiveScene() != hub)
            {
                _lastDetail = "FAIL link 2 - hub never became active after the New-Game reload";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 2 - '{hub}' never became the active scene within {BootTimeout:0}s after the New-Game reload; the first screen of a new game does not come up.");
                yield break;
            }
            for (int i = 0; i < 3; i++) yield return null;   // Awake/Start + the sceneLoaded injectors

            _hero = null;
            t0 = Time.realtimeSinceStartup;
            while (_hero == null && Time.realtimeSinceStartup - t0 < ResolveHeroTimeout)
            {
                _hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                if (_hero != null) break;
                yield return null;
            }
            if (_hero == null)
            {
                _lastDetail = "FAIL link 2 - no hero on the founded town";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 2 - no HeroLocomotion resolved within {ResolveHeroTimeout:0}s of the New-Game hub load. A brand-new player has no character to move.");
                yield break;
            }
            FlowTrace.Step(Tag, $"AssertFreshSaveFtue: link 2 PASS - hub '{hub}' up on the new save and the hero resolved at {_hero.transform.position}.");

            // -- link 3: the FTUE is LIVE, not parked --------------------------
            // Both N/A cases (flag off, flow already spent this process) were settled at
            // link 0 and exited there, so everything reaching here is a HARD assertion: on a
            // town founded seconds ago the flow must exist and must not be parked Finished.
            bool tutorialAsserted = false;
            TutorialFlow flow = null;
            t0 = Time.realtimeSinceStartup;
            while (flow == null && Time.realtimeSinceStartup - t0 < 4f)
            {
                flow = UnityEngine.Object.FindAnyObjectByType<TutorialFlow>();
                if (flow != null) break;
                yield return null;
            }
            if (flow == null)
            {
                _lastDetail = "FAIL link 3 - no TutorialFlow on a founded town";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 3 - ff.tutorialv2 ON and '{hub}' IS a hub, but no TutorialFlow was constructed within 4s of founding a new town. The new player is dropped into the world with no guide at all - this is the ZERO-[Flow:Onboard*] silence WO-1500 was opened on, reproduced.");
                yield break;
            }
            if (flow.IsFinished)
            {
                _lastDetail = "FAIL link 3 - fresh save but the flow parked Finished";
                FlowTrace.Fail(Tag, "AssertFreshSaveFtue: FAIL at link 3 - a town founded seconds ago (Onboarded=false, and the flow had NOT run this process - link 0 proved that) yet TutorialFlow phase=Finished. The interpreter declined the run, which is the F8-29 'no tutorial on a fresh boot' symptom on a save this phase created itself.");
                yield break;
            }
            tutorialAsserted = true;
            FlowTrace.Step(Tag, $"AssertFreshSaveFtue: link 3 PASS - the FTUE is LIVE on the founded town (phase '{flow.PhaseName}', step '{flow.CurrentStepId ?? "<none>"}').");

            // -- link 4: walk the guide beats and NAME each one ---------------
            // WHAT THIS WALKS, said exactly: TutorialFlow.SkipCurrentStep is the seam the
            // flow itself documents as "kept public for probes / dev tooling", and it still
            // honours the authored skippable flag - so this advances the REAL step machine
            // and stops dead on the first beat that requires a real action. That stop is
            // information, not a failure: AssertFoundingArc and AssertTutorialFirstTower are
            // the phases that DRIVE beats one and two through their real gates. What this
            // link owns is the SPINE - that beats exist, that they advance, and that every
            // id reached is written into the log, so minute one is observable by id every
            // night instead of once every five days.
            //
            // ⚠ THE WINDOW IS WALL-CLOCK, NOT FRAMES, AND THAT IS DELIBERATE. A newly armed
            // flow sits in Phase.Settling with _step still NULL until AdvanceStep runs
            // (TutorialFlow.cs:651 then :733), and the first beat's intro is a dialogue the
            // bot's own SuppressDialogue has to dismiss first. An early frame-count bound
            // (the original `guard < 200` was ~3s at 60fps) would sample that settle window,
            // see no step id, and FAIL a healthy flow for being slow. The frame guard that
            // remains is only an anti-spin backstop for a zero-delta frame loop.
            var beats = new List<string>();
            {
                string last = null;
                float walkStart = Time.realtimeSinceStartup;
                int guard = 0;
                while (Time.realtimeSinceStartup - walkStart < 30f && guard++ < 20000 && !flow.IsFinished)
                {
                    string id = flow.CurrentStepId;
                    if (!string.IsNullOrEmpty(id) && id != last)
                    {
                        beats.Add(id);
                        last = id;
                        FlowTrace.Step(Tag, $"AssertFreshSaveFtue: beat {beats.Count} = '{id}' (phase '{flow.PhaseName}').");
                    }
                    try { flow.SkipCurrentStep(); }
                    catch (Exception ex) { FlowTrace.Warn(Tag, "AssertFreshSaveFtue: SkipCurrentStep threw " + ex.Message); break; }
                    yield return null;
                }
                if (beats.Count == 0)
                {
                    _lastDetail = "FAIL link 4 - a founded town reached ZERO guide beats";
                    FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 4 - a town founded seconds ago reached NO guide beat at all within 30s (flow phase '{flow.PhaseName}', finished={flow.IsFinished}). A new player is being shown nothing; this is the blind first ten minutes stated as a failing assertion.");
                    yield break;
                }
                if (!flow.IsFinished)
                {
                    FlowTrace.Step(Tag, $"AssertFreshSaveFtue: link 4 PASS (spine walked) - {beats.Count} beat(s) [{string.Join(" -> ", beats)}]; the walk stopped at '{flow.CurrentStepId ?? "<none>"}', which is the first beat needing a REAL action (this seam honours the authored skippable flag). That beat's real drive belongs to AssertFoundingArc / AssertTutorialFirstTower.");
                }
                else
                {
                    FlowTrace.Step(Tag, $"AssertFreshSaveFtue: link 4 PASS - the whole spine walked to Finished across {beats.Count} beat(s) [{string.Join(" -> ", beats)}].");
                }
            }

            // -- link 5: THE FIRST WELCOME-BACK CLAIMS NOTHING -----------------
            // Prefer the REAL trigger: OfflineHarvestService.Start fires ClaimDeferred
            // ("cold-load") two frames into the reloaded scene. Wait for it; only drive the
            // claim by hand if it never came, and SAY SO in the trace - a probe that quietly
            // manufactures the event it is asserting proves nothing about the shipped path.
            t0 = Time.realtimeSinceStartup;
            while (OfflineClaimCoordinator.ClaimCount == claimsBefore && Time.realtimeSinceStartup - t0 < 8f)
                yield return null;

            bool droveClaim = false;
            if (OfflineClaimCoordinator.ClaimCount == claimsBefore)
            {
                var harvest = OfflineHarvestService.Instance ?? UnityEngine.Object.FindAnyObjectByType<OfflineHarvestService>();
                if (harvest == null)
                {
                    _lastDetail = "FAIL link 5 - no OfflineHarvestService on a founded town";
                    FlowTrace.Fail(Tag, "AssertFreshSaveFtue: FAIL at link 5 - no cold-load claim landed within 8s of the New-Game hub load AND no OfflineHarvestService exists to drive one. Nothing owns the returning-session report on a brand-new town, so nothing can be asserted about what it would say.");
                    yield break;
                }
                droveClaim = true;
                FlowTrace.Warn(Tag, "AssertFreshSaveFtue: link 5 - no cold-load claim arrived within 8s of the reload (the same-process launch latch, OfflineHarvestService._windowClaimed, is not reset by a New Game), so the lane drives ClaimAccrual() itself. The ARITHMETIC below is still the shipped one - OfflineClaimCoordinator.Claim - but the TRIGGER on this run was the probe, not the lifecycle.");
                try { harvest.ClaimAccrual(); }
                catch (Exception ex)
                {
                    _lastDetail = "FAIL link 5 - ClaimAccrual threw";
                    FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 5 - ClaimAccrual() threw {ex.GetType().Name}: {ex.Message}.");
                    yield break;
                }
            }

            if (OfflineClaimCoordinator.ClaimCount == claimsBefore)
            {
                _lastDetail = "FAIL link 5 - no claim ran at all on the founded town";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 5 - ClaimCount never advanced past {claimsBefore}. The first session of a new town never runs an offline claim, so the clock is never seeded and the SECOND launch would measure its window from zero - the largest away report the game can produce.");
                yield break;
            }

            var window = OfflineClaimCoordinator.LastWindow;
            if (!window.WasFreshClock || window.ElapsedSeconds != 0d)
            {
                _lastDetail = $"FAIL link 5 - first claim window fresh={window.WasFreshClock} elapsed={window.ElapsedSeconds:0}s";
                FlowTrace.Fail(Tag, $"AssertFreshSaveFtue: FAIL at link 5 - the FIRST claim on a brand-new town reported WasFreshClock={window.WasFreshClock} and a window of {window.ElapsedSeconds:0}s (claim #{window.Sequence}, reason '{window.Reason}'). It must take the fresh-clock arm and measure ZERO. A non-zero window here IS the owner's 2026-09-05 START NEW report: 'YOUR REALM WORKED FOR 8h 22m' with WOOD +11520 / IRON +6912 / STONE +15000, paid out of the PREVIOUS save's wall time.");
                yield break;
            }

            var popup = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.UI.WelcomeBackPopup>();
            if (popup != null)
            {
                _lastDetail = "FAIL link 5 - a welcome-back report is on screen on a new town";
                FlowTrace.Fail(Tag, "AssertFreshSaveFtue: FAIL at link 5 - a WelcomeBackPopup is live on a town founded seconds ago. The reveal gate is OfflineHarvestResult.HasSummaryContent and a zero window carries none of its five axes, so a popup here means a second producer is opening the screen behind the gate - the exact shape of the defect OfflineHarvestRegression case 6 pins on the model.");
                yield break;
            }

            // -- the verdict, and ONLY on real observation ---------------------
            // The marker is the fleet's whole judgement of this lane, so it may not be
            // printed by a run that reached here without seeing the FTUE. tutorialAsserted
            // and a non-empty beat list are exactly "a live flow, and beats a player would
            // have been shown"; without both, the honest line is SOFT and the fleet reports
            // FLEET_LANE_MISSING - which is the correct answer to a question nobody answered.
            if (tutorialAsserted && beats.Count > 0)
            {
                _lastDetail = $"fresh town: {beats.Count} beat(s), first claim zero ({(droveClaim ? "probe-driven" : "lifecycle")}), no welcome-back";
                FlowTrace.Step(Tag, $"FRESH_SAVE_FTUE_OK :: new town founded; beats={beats.Count} [{string.Join(" -> ", beats)}]; " +
                                    $"first claim #{window.Sequence} ('{window.Reason}') took the fresh-clock arm with a 0s window and revealed nothing " +
                                    $"(trigger={(droveClaim ? "probe" : "cold-load")}).");
            }
            else
            {
                _lastDetail = $"SOFT - tutorialLive={tutorialAsserted}, beats={beats.Count}";
                FlowTrace.Step(Tag, $"FRESH_SAVE_FTUE_SOFT :: the claim half held (window 0s, no welcome-back) but the FTUE half was not observed (tutorialLive={tutorialAsserted}, beats={beats.Count}). Not marked OK: a green line over an unobserved FTUE is the 2026-09-06 state this lane exists to end.");
            }

            // -- TEARDOWN: leave a RETURNING save, not a fresh one -------------
            // ⚠ NIGHT-2 SELF-DEFEAT, and it is the whole reason this block exists. The lane
            // leaves whatever save it founded on disk. If that save is still FRESH, the NEXT
            // night's boot arms the flow itself, s_ranThisSession latches in Bootstrap
            // (TutorialFlow.cs:640-643), and this lane's link-0 precondition then refuses -
            // so a lane written to run every night would have worked exactly once.
            // SkipAll is the sanctioned completer (its own doc: a skipper ends in the SAME
            // state as a completer, applying every mandatory grant and routing through the
            // single FinishFlow / FinishOnboarding path), so the save left behind is a normal
            // returning one. It runs AFTER the verdict so it can never colour it.
            // KNOWN AND ACCEPTED: a run that FAILED a link above exits before this teardown
            // and leaves a fresh save, so the NEXT run refuses at link 0 with SOFT. That
            // cascade is visible (FLEET_LANE_FAIL both nights), never silent, and a failing
            // lane is a ticket somebody is already holding - so the teardown is deliberately
            // not wrapped around the failure paths, where it would tidy away the evidence
            // state a repro needs.
            if (!flow.IsFinished)
            {
                try { flow.SkipAll(); }
                catch (Exception ex) { FlowTrace.Warn(Tag, "AssertFreshSaveFtue: teardown SkipAll threw " + ex.Message); }
            }
            try { svc.Save(); }
            catch (Exception ex) { FlowTrace.Warn(Tag, "AssertFreshSaveFtue: teardown Save threw " + ex.Message); }
            FlowTrace.Step(Tag, $"AssertFreshSaveFtue: teardown - the founded save was completed through SkipAll and persisted (Onboarded={(svc.State != null && svc.State.Onboarded)}), so the NEXT run of this lane boots on a returning save and its link-0 precondition holds.");
        }

        private static float TimeoutFor(string phase)
        {
            switch (phase)
            {
                case "WalkToEachGate":    return WalkToGateTimeout * 6f; // covers multiple gates
                case "OpenEachVendor":    return VendorTimeout * 12f;
                case "AssertVendorContracts": return ContractTimeout * 8f; // covers the known-context set
                case "AssertVendorTalkRoute": return ContractTimeout * 8f; // one Interact() per castle vendor
                case "AssertVendorCoverage": return 22f;   // LEVER 1 — ~14s seat-poll for the anchor fallback + slack
                case "AssertEconomyDeduct": return EconomyDeductTimeout;
                case "AssertEquip":       return EquipTimeout;
                case "AssertSaveRoundTrip": return 20f;       // WO-452 D — save/load round-trip
                case "AssertTutorialFirstTower": return 45f;  // P0 real-input build-placement probe (enter + arm + 8 candidate clicks + signal waits)
                case "AssertFoundingArc": return 75f;         // WO-702: Sylas poll + greet dialogue drive + Hollow placement + grant/DEFEND waits
                case "AssertExactlyOneGuideBody": return 60f;    // WO-971: New-Game hub reload + hero re-resolve + an 8s stand-in watch window + slack
                // WO-1500 fresh-save FTUE lane: New-Game hub reload (<=30s) + hero re-resolve (<=15s)
                // + a 4s TutorialFlow find-poll + the bounded 30s beat walk + an 8s wait for the real
                // cold-load claim + slack. Sized so the phase can NEVER outlive its own budget; the
                // 420s GlobalCapSeconds is why this lane is normally run alone (see the method header).
                case "AssertFreshSaveFtue": return 110f;
                case "AssertCombatInvariants": return 20f;    // WO-452 C — ~12s defense window + slack
                case "OpenEachHUDPanel":  return HudPanelTimeout * 8f;
                case "AssertPopupClose":  return 100f;  // WO-597: ~13 registered ids x (settle + bounded 3s close-wait worst case) + the dialogue-card row
                case "CaptureDockOverlays": return 30f;  // 4 dock overlays x (open + 2-frame render + capture + settle close)
                case "VerifyMoatOracle":  return 15f;    // 1.5s settle + side-effect-free oracle
                case "CaptureMoatRing":   return 40f;    // 5 shots (overview + 4 cardinal seams) x (spawn cam + 2-frame render + capture + destroy)
                case "CaptureCastleExterior": return 40f; // 5 shots (1 aerial + 4 gate-exterior) x (spawn cam + 2-frame render + capture + destroy)
                case "TriggerWave":       return WaveTimeout;
                case "AssertDialogueChain": return 25f;   // quiesce + Play A + chain B + close + input-release poll
                case "AssertWaveVendorRules": return 75f; // arm-wait 15s + hide-wait 6s + shop attempt + clear-wait 20s + restore-wait 6s + slack
                case "AssertTutorialArms": return 12f;    // 4s find-poll + state reads
                case "AssertScatterRecords": return 110f; // F8-8 probe — 3 bounded 25s maintain-tick waits + warps
                case "AssertCompassMarks": return 20f;    // F8-16 probe — provider polls + rect asserts
                case "AssertHeroHasAlbedo": return 15f;   // white-Paladin probe — audit read (5s late-swap grace)
                case "AssertOrientModalReleases": return 20f; // F8-30 probe — open + arbiter asserts + CloseAll
                case "AttemptExitCastle": return ExitTimeout;
                case "HomeReturnRoundTrip": return HomeReturnTimeout;   // WO-602 — walk out 20m + walk back + warp
                case "BootToGameplay":    return BootTimeout;
                case "ResolveHero":       return ResolveHeroTimeout;
                case "WalkToOuterWorldOutpost": return OuterWalkTimeout;
                case "DiagGarrisonRoster": return 45f;
                case "AssertHeroCrossing": return 18f;
                case "AssertHeroTurnOnMoveStart": return 12f; // settle-to-idle + ~2s forward-hold trace + restore
                // DUNGEON_LOOP_PROBE: walk/warp to the portal + fade-load the dungeon + reach a scripted
                // encounter + stage the arena (~4s) + kill + the full post-victory settle (~8s) + the
                // 4-direction move sweep (~6s) + the hub reload. Deliberately the longest phase in the
                // file. NOTE the 420s GlobalCapSeconds: on a FULL sweep this phase sits past the budget,
                // so run it with --phases=DungeonLoop (see the probe header for the exact command).
                case "AssertDungeonLoop": return 200f;
                default:                  return 30f;
            }
        }

        // =====================================================================
        //  PROBE: AssertDialogueChain — P0 dialogue re-entrancy verification
        // ---------------------------------------------------------------------
        // Drives the JUST-LANDED per-VM stale-Closed guard (DialogueView RCA
        // 2026-07-08: a Closed handler chaining synchronously into the NEXT
        // dialogue left the successor alive-but-headless — Ended never fired,
        // HeroLocomotion.InputSuppressed stuck TRUE, build mode froze at its
        // first gate). Real path, real authored ids:
        //   Play(A='lumbermill') -> FROM A's Closed invocation (EndedWithId)
        //   synchronously Play(B='arcane-tower') -> close B normally.
        // Links asserted (each named on FAIL):
        //   1  A plays and its panel builds (DialogueView.IsShowing).
        //   2  A's Closed chain fires and the chained Play(B) is accepted.
        //   3  B SURVIVES the chain: IsRunning + IsShowing true, and the
        //      'stale Closed ... IGNORED' Warn fired for A only (<=1, never B).
        //   4  B's Ended fires on a normal close.
        //   5  HeroLocomotion.InputSuppressed releases (the felt symptom).
        // =====================================================================
        private IEnumerator AssertDialogueChain()
        {
            const string Tag = "Auto";
            const string DlgA = "lumbermill";     // authored in dialogues.json (structure talk)
            const string DlgB = "arcane-tower";   // authored successor — the chained dialogue
            FlowTrace.Step(Tag, $"AssertDialogueChain: ENTER — Play('{DlgA}') -> Closed-chain Play('{DlgB}') -> close (tutorial re-entrancy shape).");

            if (!DeNelle.Core.FeatureFlags.CustomDialogue)
            {
                _lastDetail = "ff.customdialogue OFF — N/A (skipped)";
                FlowTrace.Step(Tag, "AssertDialogueChain: ff.customdialogue OFF — N/A, skipping (gate declined, traced).");
                yield break;
            }
            var view = FindAnyObjectByType<DeNelle.HUD.DialogueView>();
            if (view == null)
            {
                _lastDetail = "no DialogueView — bootstrap never spawned it";
                FlowTrace.Fail(Tag, "AssertDialogueChain: FAIL at link 0 — ff.customdialogue is ON but no DialogueView exists (view bootstrap dead; NO dialogue can render).");
                yield break;
            }

            // The bot's own suppressor Stop()s any running dialogue ~1/sec — pause it
            // (same mechanism the WO-597 popup-close oracle uses) for this probe.
            _pauseDialogueSuppression = true;

            // Quiesce: close anything already talking (tutorial intro etc.) so the chain starts clean.
            if (DeNelle.Core.Dialogue.DialogueService.IsRunning)
            {
                DeNelle.Core.Dialogue.DialogueService.Stop();
                yield return null;
            }

            int staleWarns = 0;      // 'stale Closed from a superseded dialogue IGNORED' count
            int panelBuilds = 0;     // one [Flow:DlgLayout] line per DialogueView.BuildUi
            bool chainFired = false, chainPlayOk = false, bEnded = false;
            Application.LogCallback logCb = (msg, stack, type) =>
            {
                if (string.IsNullOrEmpty(msg)) return;
                if (msg.Contains("stale Closed from a superseded dialogue IGNORED")) staleWarns++;
                if (msg.Contains("[Flow:DlgLayout]")) panelBuilds++;
            };
            Action<string> onEnded = id =>
            {
                if (!chainFired && string.Equals(id, DlgA, StringComparison.Ordinal))
                {
                    chainFired = true;
                    // SYNCHRONOUS re-entrant chain FROM INSIDE A's Closed invocation list —
                    // exactly the tutorial's dialogue.ended -> STEP-ENTER -> Play(next) shape.
                    chainPlayOk = DeNelle.Core.Dialogue.DialogueService.Play(DlgB);
                }
                else if (string.Equals(id, DlgB, StringComparison.Ordinal)) bEnded = true;
            };
            Application.logMessageReceived += logCb;
            DeNelle.Core.Dialogue.DialogueService.EndedWithId += onEnded;
            try
            {
                // Link 1 — Play A and see its panel.
                if (!DeNelle.Core.Dialogue.DialogueService.Play(DlgA))
                {
                    _lastDetail = $"'{DlgA}' unknown to DialogueCatalog";
                    FlowTrace.Fail(Tag, $"AssertDialogueChain: FAIL at link 1 — Play('{DlgA}') returned false (id missing from dialogues.json / catalog not loaded).");
                    yield break;
                }
                yield return null; yield return null;
                if (!view.IsShowing)
                {
                    _lastDetail = "A played but no panel";
                    FlowTrace.Fail(Tag, $"AssertDialogueChain: FAIL at link 1 — Play('{DlgA}') ran but DialogueView.IsShowing=false (panel never built).");
                    yield break;
                }
                FlowTrace.Step(Tag, $"AssertDialogueChain: link 1 PASS — '{DlgA}' playing, panel built (builds={panelBuilds}).");

                // Link 2 — close A; the Closed callback synchronously chains Play(B).
                DeNelle.Core.Dialogue.DialogueService.Stop();
                yield return null; yield return null;
                if (!chainFired)
                {
                    _lastDetail = "A closed but EndedWithId never fired";
                    FlowTrace.Fail(Tag, $"AssertDialogueChain: FAIL at link 2 — Stop() on '{DlgA}' but EndedWithId('{DlgA}') never raised (the tutorial's chain trigger is dead).");
                    yield break;
                }
                if (!chainPlayOk)
                {
                    _lastDetail = "chained Play(B) refused";
                    FlowTrace.Fail(Tag, $"AssertDialogueChain: FAIL at link 2 — the chained Play('{DlgB}') from inside A's Closed returned false (re-entrant Play refused).");
                    yield break;
                }
                FlowTrace.Step(Tag, $"AssertDialogueChain: link 2 PASS — A's Closed chained synchronously into Play('{DlgB}').");

                // Link 3 — THE FIX'S INVARIANT: B survives A's stale close.
                bool bAlive = DeNelle.Core.Dialogue.DialogueService.IsRunning;
                if (!bAlive || !view.IsShowing)
                {
                    _lastDetail = $"post-chain IsRunning={bAlive} IsShowing={view.IsShowing} staleWarns={staleWarns}";
                    FlowTrace.Fail(Tag, $"AssertDialogueChain: FAIL at link 3 — successor '{DlgB}' did NOT survive the chain (IsRunning={bAlive}, panel={view.IsShowing}) — the per-VM stale-Closed guard is not protecting the successor (alive-but-headless regression).");
                    yield break;
                }
                if (staleWarns > 1)
                {
                    _lastDetail = $"staleWarns={staleWarns} (>1 — guard swallowed a REAL close)";
                    FlowTrace.Fail(Tag, $"AssertDialogueChain: FAIL at link 3 — stale-Closed Warn fired {staleWarns}x; it must fire for A only (a second fire means B's own close was mis-classified as stale).");
                    yield break;
                }
                if (staleWarns == 0)
                    FlowTrace.Warn(Tag, "AssertDialogueChain: link 3 SOFT — the stale-Closed guard never fired for A (invocation order changed?); B's panel survived regardless.");
                else
                    FlowTrace.Step(Tag, $"AssertDialogueChain: link 3 PASS — B alive+visible; stale-Closed Warn fired exactly once (A, not B); panel builds={panelBuilds}.");

                // Link 4 — close B normally; its Ended must fire.
                DeNelle.Core.Dialogue.DialogueService.Stop();
                yield return null; yield return null;
                if (!bEnded)
                {
                    _lastDetail = "B closed but Ended never fired";
                    FlowTrace.Fail(Tag, $"AssertDialogueChain: FAIL at link 4 — Stop() on '{DlgB}' but EndedWithId('{DlgB}') never raised (Ended lost after the chain — the alive-but-headless symptom).");
                    yield break;
                }

                // Link 5 — hero input releases (HeroLocomotion polls IsRunning; give it a beat).
                float w = 0f;
                while (w < 2f && HeroLocomotion.InputSuppressed) { w += Time.unscaledDeltaTime; yield return null; }
                if (HeroLocomotion.InputSuppressed)
                {
                    _lastDetail = "InputSuppressed stuck TRUE after the chain";
                    FlowTrace.Fail(Tag, "AssertDialogueChain: FAIL at link 5 — HeroLocomotion.InputSuppressed is still TRUE 2s after the chained dialogue closed (the captured frozen-build-mode symptom).");
                    yield break;
                }

                _lastDetail = $"PASS — chain A->B survived (staleWarns={staleWarns}, panelBuilds={panelBuilds}, input released)";
                FlowTrace.Step(Tag, $"AssertDialogueChain: PASS — Play->Closed->Play chain intact end-to-end: B's panel survived the stale close, B's Ended fired, hero input released (staleWarns={staleWarns}, builds={panelBuilds}).");
            }
            finally
            {
                DeNelle.Core.Dialogue.DialogueService.EndedWithId -= onEnded;
                Application.logMessageReceived -= logCb;
                if (DeNelle.Core.Dialogue.DialogueService.IsRunning) DeNelle.Core.Dialogue.DialogueService.Stop();
                _pauseDialogueSuppression = false;
            }
        }

        // =====================================================================
        //  PROBE: AssertWaveVendorRules — F8-14 combat shop-rules verification
        // ---------------------------------------------------------------------
        // Rides the wave TriggerWave just forced (or forces one via the SAME
        // mechanism). Links asserted (each named on FAIL):
        //   1  AmbientNPC.IsCombatActive goes TRUE (the ONE shared authority).
        //   2  Vendors duck out of sight — 'vendors hidden (wave)' trace OR a
        //      direct read: every CastleVendorWaveHider's renderers disabled +
        //      its CastleNpcInteractable disabled; TalkPromptRegistry drains.
        //   3  A shop-open verb (DialogueCommandSink.Run("OpenShop")) is
        //      BLOCKED: the 'OpenShop BLOCKED' Warn fires and PartyShop never
        //      opens (PanelRouter.PanelOpened watched).
        //   4  Build-mode entry stays open — READ-ONLY check of Enter()'s only
        //      gate (SceneOwnership.IsEnemyOwned); we do NOT enter.
        //   5  Best-effort: force-clear the wave (kill enemies) and see
        //      'vendors restored' after the all-clear.
        // =====================================================================
        private IEnumerator AssertWaveVendorRules()
        {
            const string Tag = "Auto";
            FlowTrace.Step(Tag, "AssertWaveVendorRules: ENTER — wave => vendors hidden + shop blocked + build gate open (F8-14).");

            var wm = WaveManager.Instance ?? UnityEngine.Object.FindAnyObjectByType<WaveManager>();
            if (wm == null)
            {
                _lastDetail = "no WaveManager — N/A (skipped)";
                FlowTrace.Step(Tag, "AssertWaveVendorRules: no WaveManager in scene — N/A (no wave loop), skipping.");
                yield break;
            }

            bool hiddenLine = false, restoredLine = false, blockedLine = false;
            Application.LogCallback logCb = (msg, stack, type) =>
            {
                if (string.IsNullOrEmpty(msg)) return;
                if (msg.Contains("vendors hidden (wave)")) hiddenLine = true;
                if (msg.Contains("vendors restored")) restoredLine = true;
                if (msg.Contains("OpenShop BLOCKED")) blockedLine = true;
            };
            Application.logMessageReceived += logCb;
            try
            {
                // Link 1 — a wave is running (reuse the TriggerWave mechanism verbatim).
                // Force a REAL combat window (Active wave). Under the owner's 2026-07-10 rule a long
                // between-wave Countdown reads as TOWN (NPCs visible), so this oracle must drive the wave
                // to Active — not accept a resting Countdown — or IsCombatActive legitimately stays false.
                if (wm.Phase == WavePhase.Idle || wm.Phase == WavePhase.Countdown)
                {
                    FlowTrace.Step(Tag, $"AssertWaveVendorRules: wave {wm.Phase} — forcing to Active via ForceSpawnNextWaveNow.");
                    wm.ForceSpawnNextWaveNow();
                }
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 15f && !AmbientNPC.IsCombatActive) yield return null;
                if (!AmbientNPC.IsCombatActive)
                {
                    _lastDetail = $"combat authority never armed (phase {wm.Phase})";
                    FlowTrace.Fail(Tag, $"AssertWaveVendorRules: FAIL at link 1 — wave forced but AmbientNPC.IsCombatActive never went TRUE within 15s (phase '{wm.Phase}') — the shared combat authority is dead, so EVERY downstream rule (vendor hide, shop gate) is inert.");
                    yield break;
                }
                FlowTrace.Step(Tag, $"AssertWaveVendorRules: link 1 PASS — AmbientNPC.IsCombatActive=true (wave phase '{wm.Phase}').");

                // Link 2 — vendors hidden (trace line OR direct renderer/interactable read).
                var hiders = FindObjectsByType<CastleVendorWaveHider>(FindObjectsSortMode.None);
                if (hiders.Length == 0)
                {
                    FlowTrace.Warn(Tag, "AssertWaveVendorRules: link 2 N/A — no CastleVendorWaveHider in this scene (no vendor NPCs to hide); hide-rule unverifiable here.");
                }
                else
                {
                    bool allHidden = false;
                    float h0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - h0 < 6f && !allHidden)
                    {
                        allHidden = true;
                        foreach (var h in hiders)
                        {
                            if (h == null) continue;
                            foreach (var r in h.GetComponentsInChildren<Renderer>(true))
                                if (r != null && r.enabled) { allHidden = false; break; }
                            var it = h.GetComponent<CastleNpcInteractable>();
                            if (it != null && it.enabled) allHidden = false;
                            if (!allHidden) break;
                        }
                        if (!allHidden) yield return null;
                    }
                    if (!allHidden && !hiddenLine)
                    {
                        _lastDetail = $"{hiders.Length} vendor(s) still visible/interactable in combat";
                        FlowTrace.Fail(Tag, $"AssertWaveVendorRules: FAIL at link 2 — combat active for 6s but {hiders.Length} vendor NPC(s) are still visible/interactable and no 'vendors hidden (wave)' trace fired (CastleVendorWaveHider not hiding).");
                        yield break;
                    }
                    FlowTrace.Step(Tag, $"AssertWaveVendorRules: link 2 PASS — {hiders.Length} vendor(s) hidden (trace={hiddenLine}, renderers+interact off={allHidden}).");
                    if (TalkPromptRegistry.Count > 0)
                        FlowTrace.Warn(Tag, $"AssertWaveVendorRules: TalkPromptRegistry still holds {TalkPromptRegistry.Count} talkable(s) during combat — vendors deregister on hide; a non-vendor talkable may be in hero range (verify in this run's trace).");
                    else
                        FlowTrace.Step(Tag, "AssertWaveVendorRules: TalkPromptRegistry empty during combat — the HUD Talk route is unreachable.");
                }

                // Link 3 — a shop-open verb is BLOCKED (Warn + toast), never a panel.
                bool shopOpened = false;
                Action<PanelId> onOpen = id => { if (id == PanelId.PartyShop) shopOpened = true; };
                PanelRouter.PanelOpened += onOpen;
                try
                {
                    // The EXACT method DialogueService routes dialogue verbs to — its
                    // ShopsClosedForCombat gate is the F8-14 fix under test.
                    new DialogueCommandSink().Run("OpenShop", new[] { "lumbermill" });
                }
                finally { PanelRouter.PanelOpened -= onOpen; }
                yield return null;
                if (shopOpened)
                {
                    _lastDetail = "OpenShop OPENED PartyShop mid-combat";
                    FlowTrace.Fail(Tag, "AssertWaveVendorRules: FAIL at link 3 — DialogueCommandSink.Run('OpenShop') OPENED PartyShop while combat is active (the F8-14 shop gate is dead).");
                    yield break;
                }
                if (!blockedLine)
                {
                    _lastDetail = "OpenShop neither opened nor warned (silent no-op)";
                    FlowTrace.Fail(Tag, "AssertWaveVendorRules: FAIL at link 3 — OpenShop neither opened a panel NOR fired the 'OpenShop BLOCKED' Warn/toast (silently swallowed — the no-silent-failure rule is violated).");
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertWaveVendorRules: link 3 PASS — OpenShop BLOCKED via the Warn+toast path; PartyShop never opened.");

                // Link 4 — build-mode entry gate, READ-ONLY (do not Enter). Enter()'s only
                // refusal gate is enemy ownership — a wave must NOT lock building.
                if (SceneOwnership.IsEnemyOwned)
                {
                    _lastDetail = "IsEnemyOwned=true in hub during wave";
                    FlowTrace.Fail(Tag, "AssertWaveVendorRules: FAIL at link 4 — SceneOwnership.IsEnemyOwned=TRUE during the hub wave; BuildModeController.Enter() would refuse (build lock regression).");
                    yield break;
                }
                FlowTrace.Step(Tag, "AssertWaveVendorRules: link 4 PASS — build entry gate open (IsEnemyOwned=false); the wave does not lock build mode.");

                // Link 5 — best-effort all-clear: force-clear the wave and watch the restore.
                int killed = 0;
                foreach (var e in UnityEngine.Object.FindObjectsByType<Enemy>())
                    if (e != null) { try { e.Kill(); killed++; } catch (Exception ex) { FlowTrace.Warn(Tag, "AssertWaveVendorRules: Kill threw " + ex.Message); } }
                FlowTrace.Step(Tag, $"AssertWaveVendorRules: force-cleared the wave ({killed} enemies killed) — waiting for the all-clear.");
                float c0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - c0 < 20f && AmbientNPC.IsCombatActive) yield return null;
                if (AmbientNPC.IsCombatActive)
                {
                    FlowTrace.Step(Tag, "AssertWaveVendorRules: link 5 N/A — combat authority still active after the clear (loop re-armed a countdown / staged battle live); restore rides the next calm window.");
                }
                else if (hiders.Length > 0)
                {
                    float r0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - r0 < 6f && !restoredLine) yield return null;
                    if (restoredLine)
                        FlowTrace.Step(Tag, "AssertWaveVendorRules: link 5 PASS — 'vendors restored' fired after the all-clear.");
                    else
                        FlowTrace.Warn(Tag, "AssertWaveVendorRules: link 5 SOFT — all-clear reached but no 'vendors restored' trace within 6s (restore transition not observed this run).");
                }
                _lastDetail = $"PASS — authority armed, {hiders.Length} vendor(s) hidden, shop blocked, build gate open, restore={restoredLine}";
                FlowTrace.Step(Tag, "AssertWaveVendorRules: PASS — F8-14 combat shop-rules chain verified end-to-end.");
            }
            finally { Application.logMessageReceived -= logCb; }
        }

        // =====================================================================
        //  PROBE: AssertTutorialArms — F8-29 tutorial re-arm verification
        // ---------------------------------------------------------------------
        // Drives the sceneLoaded re-arm fix (TutorialFlow.Bootstrap used to be a
        // one-shot that evaluated the TITLE scene and never re-armed; proof was
        // ZERO [Flow:Tutorial] lines in the owner's fresh session). Links:
        //   1  In a hub with ff.tutorialv2 ON, the '[Flow:Tutorial] Bootstrap(...)
        //      armed' precondition holds: FindAnyObjectByType<TutorialFlow>() != null.
        //   2  Fresh-save context (GameStateService.State.Onboarded == false and
        //      the flow has not already run this session): the flow phase is NOT
        //      Finished (a Finished fresh flow IS the 'no tutorial' symptom).
        // Read via TutorialFlow's public probe surface (PhaseName / IsFinished /
        // RanThisSession) — no reflection.
        // =====================================================================
        private IEnumerator AssertTutorialArms()
        {
            const string Tag = "Auto";
            string scene = ActiveScene();
            FlowTrace.Step(Tag, $"AssertTutorialArms: ENTER — verify the sceneLoaded re-arm put a TutorialFlow in '{scene}' (F8-29).");

            if (!DeNelle.Core.FeatureFlags.TutorialV2)
            {
                _lastDetail = "ff.tutorialv2 OFF — N/A (skipped)";
                FlowTrace.Step(Tag, "AssertTutorialArms: ff.tutorialv2 OFF — N/A, skipping (Bootstrap correctly dormant, already traced by the flow).");
                yield break;
            }
            if (!DeNelle.Core.HubScenes.IsHub(scene))
            {
                _lastDetail = $"'{scene}' not a hub — N/A (skipped)";
                FlowTrace.Step(Tag, $"AssertTutorialArms: scene '{scene}' is not a hub — N/A; Bootstrap correctly waits for a hub load.");
                yield break;
            }

            // Link 1 — the interpreter exists (poll briefly: Bootstrap fires on sceneLoaded,
            // construction is same-frame, but give a slow boot a beat).
            TutorialFlow flow = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f)
            {
                flow = FindAnyObjectByType<TutorialFlow>();
                if (flow != null) break;
                yield return null;
            }
            if (flow == null)
            {
                _lastDetail = "hub + ff ON but NO TutorialFlow — re-arm dead";
                FlowTrace.Fail(Tag, $"AssertTutorialArms: FAIL at link 1 — ff.tutorialv2 ON and '{scene}' IS a hub, but FindAnyObjectByType<TutorialFlow>() found nothing within 4s — the Bootstrap/sceneLoaded re-arm (F8-29 fix) did not construct the interpreter.");
                yield break;
            }
            FlowTrace.Step(Tag, $"AssertTutorialArms: link 1 PASS — TutorialFlow armed in hub '{scene}' (the 'Bootstrap(...) armed' precondition holds).");

            // Link 2 — fresh-save context: the flow must be LIVE, not parked Finished.
            var svc = DeNelle.Core.State.GameStateService.Instance;
            var st = svc != null ? svc.State : null;
            if (st == null)
            {
                _lastDetail = "armed; GameStateService unavailable — phase assert skipped";
                FlowTrace.Warn(Tag, "AssertTutorialArms: link 2 SOFT — GameStateService/State unavailable; fresh-save phase assertion skipped.");
                yield break;
            }
            if (st.Onboarded)
            {
                _lastDetail = $"armed; save Onboarded — Finished is correct (phase '{flow.PhaseName}')";
                FlowTrace.Step(Tag, $"AssertTutorialArms: link 2 N/A — save already Onboarded; parked phase '{flow.PhaseName}' is the correct returning-player state.");
                yield break;
            }
            if (flow.IsFinished && TutorialFlow.RanThisSession)
            {
                _lastDetail = "armed; flow already ran this session (resume block) — Finished expected";
                FlowTrace.Step(Tag, "AssertTutorialArms: link 2 N/A — flow already ran this session (s_ranThisSession resume block); Finished is expected mid-session.");
                yield break;
            }
            if (flow.IsFinished)
            {
                _lastDetail = "FRESH save but flow parked Finished — declined to run";
                FlowTrace.Fail(Tag, "AssertTutorialArms: FAIL at link 2 — FRESH save (Onboarded=false, not run this session) yet TutorialFlow phase=Finished — the interpreter declined the run (the F8-29 'no tutorial on fresh boot' symptom).");
                yield break;
            }
            _lastDetail = $"PASS — armed + LIVE (phase '{flow.PhaseName}', fresh save)";
            FlowTrace.Step(Tag, $"AssertTutorialArms: PASS — fresh save and the flow is LIVE (phase '{flow.PhaseName}', not Finished).");
        }

        // =====================================================================
        //  BOT-BOOT: wave-determinism reset (runs BEFORE BootToGameplay)
        // =====================================================================

        /// <summary>
        /// Bot-boot save normalization (replay-wave RCA 2026-07-11): zero ONLY the
        /// wave-progress fields (<c>GameState.BestWave</c> + <c>WavesCompleted</c>) so
        /// <c>WaveManager.ResolveStartWave</c> (WaveManager.cs:684-690) seeds
        /// <c>s_resumeWaveId</c> = 1 instead of the fleet-accumulated BestWave+1, which
        /// exhausts the 20-wave schedule and parks the loop at phase Complete before any
        /// oracle runs. Deliberately does NOT touch the rest of the save — Onboarded /
        /// tutorial / resources feed other probes (AssertTutorialArms link 2 asserts on
        /// the REAL Onboarded state). Persists via Save() so a later hub-load re-read and
        /// the next fleet run both start fresh. Autopilot-only by construction: only
        /// AutoPilotInstaller (opt-in --autopilot/AUTOPILOT) or the dev-panel button
        /// spawn this driver — a player session never reaches this path.
        /// </summary>
        private IEnumerator ResetWaveProgressForDeterminism()
        {
            // GameStateService self-installs on its own AfterSceneLoad hook, whose order
            // vs. AutoPilotInstaller's AfterSceneLoad hook is undefined — poll briefly.
            var svc = DeNelle.Core.State.GameStateService.Instance;
            float t0 = Time.realtimeSinceStartup;
            while ((svc == null || svc.State == null) && Time.realtimeSinceStartup - t0 < 5f)
            {
                yield return null;
                svc = DeNelle.Core.State.GameStateService.Instance;
            }
            if (svc == null || svc.State == null)
            {
                FlowTrace.Warn("Auto", "bot boot: wave-progress reset SKIPPED — GameStateService/State unavailable after 5s poll; a fleet-accumulated BestWave may exhaust the wave schedule (phase Complete before any oracle).");
                yield break;
            }

            int wasBest      = svc.State.BestWave;
            int wasCompleted = svc.State.WavesCompleted;
            if (wasBest == 0 && wasCompleted == 0)
            {
                FlowTrace.Step("Auto", "bot boot: wave progress already fresh (bestCleared=0) -> deterministic wave 1.");
                yield break;
            }

            svc.State.BestWave       = 0;
            svc.State.WavesCompleted = 0;
            svc.Save();

            // Late-reset diagnostic: if a WaveManager ALREADY armed its loop in the boot
            // scene (edge: the build's first scene IS the hub and BeginLoop won the race),
            // its in-session resume static captured the stale BestWave before this landed.
            var earlyWm = WaveManager.Instance ?? UnityEngine.Object.FindAnyObjectByType<WaveManager>();
            if (earlyWm != null && earlyWm.CurrentWaveId > 1)
                FlowTrace.Warn("Auto", $"bot boot: wave-progress reset landed AFTER a WaveManager armed (currentWave={earlyWm.CurrentWaveId}) — the in-session resume static kept the stale seed; wave oracles may still see the exhausted schedule this run.");

            FlowTrace.Step("Auto", $"bot boot: wave progress reset (was bestCleared={wasBest}, wavesCompleted={wasCompleted}) -> deterministic wave 1.");
        }

        // =====================================================================
        //  PHASE: BootToGameplay (FIRST)
        //  A headless bot can't drive the Title->PetSelect->MainCastle_Hall UI
        //  flow, so if we're not already in the gameplay scene, load it directly.
        //  MainCastle_Hall is in Build Settings, so LoadScene-by-name works
        //  headless; its single load triggers the additive OuterWorld load via the
        //  existing WorldSceneLoader. We then wait (realtime) for the active scene
        //  to BE MainCastle_Hall plus a few frames so its Awake/Start has run.
        // =====================================================================
        private IEnumerator BootToGameplay()
        {
            string target = TargetScene;   // --scene override (Village2 / a garrison) else MainCastle_Hall
            if (ActiveScene() == target)
            {
                FlowTrace.Step("Auto", $"BootToGameplay -> already in '{target}', nothing to load.");
                _lastDetail = "already in gameplay scene";
                yield break;
            }

            FlowTrace.Step("Auto", $"BootToGameplay -> loading {target} (from '{ActiveScene()}')" +
                                   (_startScene != null ? " [--scene override]" : "") + ".");
            try
            {
                SceneManager.LoadScene(target);
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("Auto", $"BootToGameplay: LoadScene('{target}') threw — {ex.Message} " +
                               "(is the --scene target in Build Settings?)");
                _lastDetail = "LoadScene threw";
                yield break;
            }

            float t0 = Time.realtimeSinceStartup;
            bool arrived = false;
            while (Time.realtimeSinceStartup - t0 < BootTimeout)
            {
                if (ActiveScene() == target) { arrived = true; break; }
                yield return null;
            }

            if (!arrived)
            {
                FlowTrace.Fail("Auto", $"BootToGameplay: '{target}' never became active within {BootTimeout:0}s — aborting.");
                _lastDetail = "scene never active";
                yield break;
            }

            // Give the scene a couple of frames so Awake/Start (and the additive
            // OuterWorld load it kicks off) get a chance to run before ResolveHero.
            for (int i = 0; i < 3; i++) yield return null;

            FlowTrace.Step("Auto", $"BootToGameplay -> arrived in '{target}'.");
            _lastDetail = $"loaded {target}";
        }

        // =====================================================================
        //  PHASE: ResolveHero
        // =====================================================================
        private IEnumerator ResolveHero()
        {
            // Poll — the hero may spawn a moment after scene load (the BootToGameplay
            // additive OuterWorld load + hero spawn can lag the active-scene swap).
            float t0 = Time.realtimeSinceStartup;
            while (_hero == null && Time.realtimeSinceStartup - t0 < ResolveHeroTimeout)
            {
                _hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                if (_hero != null) break;
                yield return null;
            }

            if (_hero == null)
            {
                FlowTrace.Fail("Auto", "ResolveHero: no HeroLocomotion in scene — cannot drive. Aborting gracefully.");
                _lastDetail = "hero not found";
                yield break;
            }
            FlowTrace.Step("Auto", $"ResolveHero: hero '{_hero.name}' at {_hero.transform.position}.");
        }

        // =====================================================================
        //  PHASE: AssertHeroTurnOnMoveStart — HERO_TURN_PROBE
        // ---------------------------------------------------------------------
        //  Owner "turn-left-before-walk" RCA (2026-07-10). Grok 86847b7f proved the
        //  hero SLEWS its root yaw toward the camera-relative move heading on move
        //  start (Quaternion.Slerp(rotation, LookRotation(move), _rotationSpeed*dt));
        //  when the idle facing differs from that heading, a large angle = a visible
        //  swing = the complaint. This probe MEASURES that slew headlessly, with no
        //  render/click dependency, so the CLI can drive "move north from idle" and
        //  compare the applied rotation to the source math WITHOUT the owner testing:
        //    1) settle the hero to idle (Velocity ~0),
        //    2) set a KNOWN idle facing 90° off the camera-forward (worst-case ~90 dYaw),
        //    3) arm HeroLocomotion.TurnDebug + FlowTrace so [Flow:HeroTurn] logs EVERY
        //       frame, then drive FORWARD via the scripted-move seam (the SAME
        //       ReadMoveInput → camera-basis → Velocity path the player uses),
        //    4) sample the yaw each frame → framesToAlign / maxDYaw / finalDYaw,
        //    5) emit the HERO_TURN_PROBE :: summary marker + restore state (TurnDebug off).
        //  The full step trace is the [Flow:HeroTurn] lines in Player.log/break-log.
        // =====================================================================
        private IEnumerator AssertHeroTurnOnMoveStart()
        {
            const string Tag = "Auto";
            EnsureHero("AssertHeroTurnOnMoveStart");
            if (_hero == null)
            {
                _lastDetail = "no hero - skipped";
                FlowTrace.Warn(Tag, "AssertHeroTurnOnMoveStart: no hero - skipped (EnsureHero named the reason above).");
                yield break;
            }

            var tf = _hero.transform;
            Vector3 startPos = tf.position;

            // Camera-relative forward heading the hero will be asked to face. HeroLocomotion computes
            // move = Euler(0,camYaw,0) * (input.x,0,input.y); for forward (0,1) the world heading is
            // exactly camYaw. Headless there is usually no SmartMobileCamera → camYaw 0 → target north.
            var cam = UnityEngine.Object.FindObjectOfType<SmartMobileCamera>();
            float camYaw = cam != null ? cam.CameraYaw : 0f;
            float targetYaw = camYaw;                 // heading of the forward move vector
            float startYaw  = Mathf.Repeat(camYaw + 90f, 360f);  // idle facing 90° off → worst-case left swing

            // 1) Settle to idle — clear any leftover scripted move, wait for velocity to drain.
            HeroLocomotion.ClearScriptedMove();
            float w = 0f;
            while (w < 2f && _hero.Velocity.sqrMagnitude > 0.01f) { w += Time.deltaTime; yield return null; }

            // 2) Known idle state: warp in place at the chosen facing (WarpTo zeroes velocity + sets yaw).
            _hero.WarpTo(startPos, Quaternion.Euler(0f, startYaw, 0f));
            yield return null;
            startYaw = tf.eulerAngles.y;   // read back the actual applied yaw

            // 3) Arm the fine trace + drive forward through the real input seam.
            bool prevTurnDebug = HeroLocomotion.TurnDebug;
            bool prevFlow      = FlowTrace.Enabled;
            HeroLocomotion.TurnDebug = true;
            FlowTrace.Enabled        = true;
            HeroLocomotion.SetScriptedMove(new Vector2(0f, 1f));   // "press forward" — arm the scripted-move seam
            // Confirm the gate inputs are live AND that no competing owner will bypass the trace block.
            // probeDriving (TurnDebug && scripted-move) makes the probe authoritative over the
            // InputSuppressed / _autoWalkTarget early-returns a hub TutorialFlow/dialogue can set.
            FlowTrace.Step(Tag, $"[Flow:HeroTurn] PROBE ARMED startYaw={startYaw:F1} targetYaw={targetYaw:F1} camYaw={camYaw:F1} " +
                                $"TurnDebug={HeroLocomotion.TurnDebug} FlowEnabled={FlowTrace.Enabled} " +
                                $"IsAutoWalking={_hero.IsAutoWalking} InputSuppressed={HeroLocomotion.InputSuppressed}");

            float maxDYaw = 0f, finalDYaw = 0f;
            int frames = 0, framesToAlign = -1;
            float probe = 0f;
            while (probe < 2f)
            {
                yield return null;
                probe += Time.deltaTime;
                frames++;
                float dyaw = Mathf.Abs(Mathf.DeltaAngle(tf.eulerAngles.y, targetYaw));
                if (dyaw > maxDYaw) maxDYaw = dyaw;
                finalDYaw = dyaw;
                if (framesToAlign < 0 && dyaw < 2f) framesToAlign = frames;   // first frame within 2° of target
            }

            // 4) Disarm — stop pressing, clear the seam, restore toggles + position.
            HeroLocomotion.ClearScriptedMove();
            HeroLocomotion.TurnDebug = prevTurnDebug;
            _hero.WarpTo(startPos, Quaternion.Euler(0f, startYaw, 0f));   // put the hero back where the probe found it

            bool aligned = finalDYaw < 2f;
            string verdict =
                $"HERO_TURN_PROBE :: startYaw={startYaw:F1} targetYaw={targetYaw:F1} " +
                $"framesToAlign={(framesToAlign < 0 ? frames : framesToAlign)} " +
                $"maxDYaw={maxDYaw:F1} finalDYaw={finalDYaw:F1} branch=town-slew aligned={aligned}";
            FlowTrace.Step(Tag, verdict);
            _lastDetail = verdict;

            FlowTrace.Enabled = prevFlow;   // restore last (so the marker above still logged)
        }

        // =====================================================================
        //  EnsureHero — LATE (overworld) hero re-resolve + self-reporting skip.
        //  RCA 2026-07-08 (the "overworld probes all skip: no hero" linchpin):
        //  the driver resolves _hero EXACTLY ONCE in ResolveHero and only ever
        //  re-resolves it in the popup-recovery reload path. The town probes run
        //  early (WalkToEachGate…AssertCompassMarks) while that cached handle is
        //  still alive, so they find the hero. Between them and the overworld
        //  probes a scene stream/unload (WorldSceneLoader additive OuterWorld,
        //  a wave/celebration swap, or the popup-recovery reload) DESTROYS the
        //  GameObject _hero points at — Unity-fake-nulls the cached reference —
        //  and NOTHING re-resolves it, so AttemptExitCastle / HomeReturnRoundTrip
        //  / WalkToOuterWorldOutpost / AssertScatterRecords / AssertEncounterBattle
        //  each read _hero==null and skip. The sibling AutoPilotProbes component
        //  never went hero-blind because its RefreshHero re-runs the SAME lookup
        //  every 2s (AutoPilotProbes.cs:339); the DRIVER lacked that refresh —
        //  THAT is the difference between the town probes (pass) and the overworld
        //  probes (skip).
        //
        //  This re-runs the CANONICAL lookup ResolveHero uses
        //  (FindAnyObjectByType<HeroLocomotion>) so a fresh post-stream hero
        //  instance is picked up and coverage UNLOCKS. HARNESS-INTEGRITY: it never
        //  emits a Fail — a genuinely hero-less state returns false and the caller
        //  keeps its existing NAMED Warn/skip, so a legitimately-blocked probe
        //  reports a named skip, never a false ticket. It only finds the hero
        //  wherever it currently lives; it does NOT cross the WO-453 warp/seam.
        //  Returns true when _hero is (now) non-null.
        // =====================================================================
        private bool EnsureHero(string phase)
        {
            if (_hero != null) return true;   // cached handle still alive — nothing to do

            var found = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
            string active = ActiveScene();
            int loaded = SceneManager.sceneCount;
            if (found != null)
            {
                _hero = found;
                FlowTrace.Step("Auto", $"EnsureHero[{phase}]: cached hero handle was STALE (destroyed by a scene stream/reload since ResolveHero) — " +
                    $"RE-RESOLVED '{found.name}' in scene '{found.gameObject.scene.name}' at {found.transform.position} " +
                    $"(active '{active}', {loaded} scene(s) loaded). Overworld coverage proceeds.");
                return true;
            }

            // Genuinely no HeroLocomotion in ANY loaded scene — NAME the reason (not a bare skip).
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < loaded; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                sb.Append(sc.name);
                if (i < loaded - 1) sb.Append(", ");
            }
            FlowTrace.Warn("Auto", $"EnsureHero[{phase}]: no HeroLocomotion in ANY loaded scene " +
                $"(active '{active}', loaded=[{sb}]) — the hero was destroyed and not re-spawned; the phase legitimately skips (named, not a false ticket).");
            return false;
        }

        // =====================================================================
        //  PHASE: WalkToEachGate
        //  Drive the hero (via SetAutoWalk) to within ProximityRadius of every
        //  SceneTransitionTrigger. We do NOT want to actually cross here (that's
        //  the LAST phase), so we record the scene name before/after and clear
        //  the auto-walk the instant we're in range.
        // =====================================================================
        private IEnumerator WalkToEachGate()
        {
            var gates = UnityEngine.Object.FindObjectsByType<SceneTransitionTrigger>();
            if (gates == null || gates.Length == 0)
            {
                FlowTrace.Warn("Auto", "WalkToEachGate: no SceneTransitionTrigger in scene.");
                _lastDetail = "0 gates";
                yield break;
            }
            Shuffle(gates);   // seeded order so different bots probe gates differently
            FlowTrace.Step("Auto", $"WalkToEachGate: {gates.Length} gate(s) found.");

            int reached = 0;
            foreach (var gate in gates)
            {
                if (gate == null) continue;
                string sceneBefore = ActiveScene();
                float radius = Mathf.Max(1f, gate.ProximityRadius);
                FlowTrace.Step("Auto", $"WalkToEachGate: heading to '{gate.name}' (radius={radius:0.0}m, scene='{sceneBefore}').");

                _hero.SetAutoWalk(gate.transform);
                float t0 = Time.realtimeSinceStartup;
                bool inRange = false;
                while (Time.realtimeSinceStartup - t0 < WalkToGateTimeout)
                {
                    if (_hero == null) break;
                    float d = HorizontalDistance(_hero.transform.position, gate.transform.position);
                    // Stop just SHORT of the gate's fire radius so we don't cross now.
                    if (d <= radius + 0.5f) { inRange = true; break; }
                    yield return null;
                }
                _hero.ClearAutoWalk();

                string sceneAfter = ActiveScene();
                if (inRange)
                {
                    reached++;
                    FlowTrace.Step("Auto", $"WalkToEachGate: reached '{gate.name}' (scene before='{sceneBefore}', after='{sceneAfter}').");
                }
                else
                {
                    FlowTrace.Warn("Auto", $"WalkToEachGate: did NOT reach '{gate.name}' within {WalkToGateTimeout:0}s (closest path blocked / navmesh edge).");
                }

                // If walking to a gate accidentally crossed (proximity fired), bail
                // out of the rest — the scene changed under us.
                if (sceneAfter != sceneBefore)
                {
                    FlowTrace.Warn("Auto", $"WalkToEachGate: scene changed '{sceneBefore}'->'{sceneAfter}' while approaching '{gate.name}' — stopping gate sweep.");
                    break;
                }
                yield return Wait(SettleSeconds);
            }
            _lastDetail = $"{reached}/{gates.Length} gates reached";
        }

        // =====================================================================
        //  PHASE: OpenEachVendor
        //  Enumerate BuildingInteractable. Its Interact() is private, but routes
        //  through the SAME public seams it would: DialogueService.PlayStructure
        //  for the economy vendors and PanelRouter.Open for the legacy panels.
        //  We replicate that routing here, verify the surface opened, actuate the
        //  clickables, then close.
        // =====================================================================
        private IEnumerator OpenEachVendor()
        {
            // ROBUST DISCOVERY: FindObjectsByType spans ALL loaded scenes, but buildings can
            // load a beat after the scene becomes active. So RETRY for up to ~5s before concluding 0 —
            // runtime-injected buildings can spawn shortly after scene load. (FindObjectsSortMode.None.)
            BuildingInteractable[] buildings = null;
            float t0Discover = Time.realtimeSinceStartup;
            int attempts = 0;
            while (Time.realtimeSinceStartup - t0Discover < 5f)
            {
                attempts++;
                buildings = UnityEngine.Object.FindObjectsByType<BuildingInteractable>();
                if (buildings != null && buildings.Length > 0) break;
                yield return Wait(0.5f);
            }

            if (buildings == null || buildings.Length == 0)
            {
                // NOTE: in MainCastle_Hall this is EXPECTED — the castle storefronts route
                // through CastleNpcInteractable (spawned by CastleVendorNpcInjector) +
                // DialogueService, NOT BuildingInteractable. The AssertVendorContracts phase
                // below covers vendors directly by context, so 0 here is not a dead end.
                FlowTrace.Step("Auto", $"OpenEachVendor: 0 BuildingInteractable after {attempts} attempt(s) over ~5s. " +
                    "MainCastle_Hall vendors use CastleNpcInteractable, not BuildingInteractable — covered by AssertVendorContracts.");
                _lastDetail = "0 BuildingInteractable (castle uses CastleNpcInteractable)";
                yield break;
            }
            Shuffle(buildings);   // seeded vendor order
            FlowTrace.Step("Auto", $"OpenEachVendor: {buildings.Length} building(s) found (after {attempts} discovery attempt(s)).");
            foreach (var b in buildings)
                if (b != null) FlowTrace.Step("Auto", $"OpenEachVendor: discovered BuildingInteractable '{b.name}'.");

            int opened = 0;
            foreach (var bi in buildings)
            {
                if (bi == null) continue;
                string name = bi.name;

                // Walk near it first (so a real run mirrors the player approach).
                _hero.SetAutoWalk(bi.transform);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 8f)
                {
                    if (_hero == null) break;
                    if (HorizontalDistance(_hero.transform.position, bi.transform.position) <= 6.5f) break;
                    yield return null;
                }
                _hero.ClearAutoWalk();

                // Open the surface the way the building would. The vendor routing is
                // building-specific (see BuildingInteractable.Interact): the economy
                // buildings open the shared Yarn structure dialogue; the rest open a
                // PanelRouter panel. We try the dialogue first, then panels.
                bool surfaceOpened = false;
                FlowTrace.Step("Auto", $"OpenEachVendor: interacting with '{name}'.");

                // The bot can't read the building's private hookId, so it tries the
                // structure dialogue for the well-known economy ids and the panel ids
                // for the rest — exactly the two public routes Interact() takes.
                // We probe by attempting a structure dialogue with the building name
                // as a label; PlayStructure returns false if the id isn't routable.
                // Practically, the HUD-panel route below is the reliable verifiable
                // path, so we exercise it for every registered panel in the next phase
                // and here we focus on the dialogue surface.
                if (DialogueService.PlayStructure("market", name)
                    || DialogueService.PlayStructure("farm", name))
                {
                    yield return Wait(SettleSeconds);
                    surfaceOpened = DialogueService.IsRunning;
                    if (surfaceOpened)
                    {
                        FlowTrace.Step("Auto", $"OpenEachVendor: '{name}' opened a structure dialogue.");
                        ClickableActuator.ActuateAll(null, _rng);
                        yield return Wait(SettleSeconds);
                        DialogueService.Stop();
                        opened++;
                    }
                }

                if (!surfaceOpened)
                {
                    // Fall through to a panel route if one is registered for a
                    // common building panel (Building Upgrade is the broadest).
                    if (PanelRouter.IsRegistered(PanelId.BuildingUpgrade)
                        && PanelRouter.Open(PanelId.BuildingUpgrade))
                    {
                        yield return Wait(SettleSeconds);
                        if (PanelManager.AnyOpen)
                        {
                            FlowTrace.Step("Auto", $"OpenEachVendor: '{name}' opened BuildingUpgrade panel.");
                            ClickableActuator.ActuateAll(null, _rng);
                            yield return Wait(SettleSeconds);
                            PanelManager.CloseOpen();
                            opened++;
                            surfaceOpened = true;
                        }
                    }
                }

                if (!surfaceOpened)
                    FlowTrace.Warn("Auto", $"OpenEachVendor: '{name}' opened no verifiable surface (no routable dialogue / panel).");

                yield return Wait(SettleSeconds);
            }
            _lastDetail = $"{opened}/{buildings.Length} vendor surfaces opened";
        }

        // =====================================================================
        //  PHASE: AssertVendorContracts
        //  The point of the chain: bots judge whether a vendor sells the RIGHT
        //  category. For each KNOWN vendor context the game uses, open the shop for
        //  that context and assert its ACTUAL built stock (PartyShopPanelMvvm.CurrentStock)
        //  stays within VendorStockContract.AllowedFor(context). A violation is a
        //  LogError (FlowTrace.Fail) -> break-log -> ticket. Runs even if building
        //  discovery found 0 vendors, because it opens shops DIRECTLY by context via
        //  PartyShopPanelMvvm.Open — the same seam DialogueCommandSink "OpenShop" uses
        //  (PanelId.PartyShop).
        //
        //  ⚠ WO-1430 (2026-09-06): this phase used to drive the legacy ShopPanel, which was
        //  DELETED as doorless in that change (no production file opened it; only this bot and
        //  UICaptureLaunch constructed it, and a harness is not a door). It now drives the LIVE
        //  party shop, so a throw here is a real player-facing defect rather than a dead screen's.
        //
        //  Known contexts: the spec set {forge, market, jeweler, armorer} PLUS the
        //  contexts the castle actually wires (CastleVendorNpcInjector.VendorFor):
        //  lumbermill, farm, pet-house, arcane-tower. The jeweler is modeled as Armor
        //  in the contract today (the crystal/jewelry adornment arc). The non-shop
        //  ids (lumbermill/farm/pet-house/arcane-tower) still resolve a contract
        //  (Potion / general default) and a stock, so asserting them is valid and
        //  cheap — and catches a future regression if one of them starts stocking gear.
        // =====================================================================
        private IEnumerator AssertVendorContracts()
        {
            // The known vendor-context set: the spec minimum plus the contexts the
            // castle storefronts actually open (verified against CastleVendorNpcInjector).
            var contexts = new List<string>
            {
                "forge", "market", "jeweler", "armorer",
                "lumbermill", "farm", "pet-house", "arcane-tower",
            };
            Shuffle(contexts); // seeded order so different bots probe contexts differently

            // ⚠ CLEAR THE MODAL ARBITER FIRST (WO-1430). PartyShopPanelMvvm registers with
            // PanelManager and its Open() BAILS on a NotifyOpened rejection - and its Close()
            // has already run DisposeViewModel(), which NULLS _vm. A rejected open therefore
            // reads back as EMPTY stock, and this phase would warn "built EMPTY stock" on every
            // context: a false signal in the break-log. The old ShopPanel never registered with
            // PanelManager, so this was not needed before. Mirrors UICaptureLaunch's party-shop
            // capture, which clears the arbiter the same way.
            try { PanelManager.CloseAll(); } catch { }

            // One reusable PartyShopPanelMvvm host: find an existing one, else create our own.
            // (AddComponent runs Awake -> PanelRouter.Register x3; Register is a dictionary
            // assignment and never throws - PanelRouter.cs:204/229/253 - and we only create a
            // host when none was found, so no live registration is clobbered.)
            // Re-Open(ctx) closes the prior surface, so vendors never stack. We destroy
            // the host we created at the end.
            PartyShopPanelMvvm panel = UnityEngine.Object.FindAnyObjectByType<PartyShopPanelMvvm>();
            bool createdHost = false;
            GameObject host = null;
            if (panel == null)
            {
                host = new GameObject("AutoPilotPartyShopPanelHost");
                panel = host.AddComponent<PartyShopPanelMvvm>();
                createdHost = true;
            }

            int checkedCount = 0, violations = 0, emptyWarns = 0;
            foreach (var ctx in contexts)
            {
                try
                {
                    FlowTrace.Step("Auto", $"AssertVendorContracts: opening shop for context '{ctx}'.");
                    panel.Open(ctx, null);
                }
                catch (Exception ex)
                {
                    FlowTrace.Fail("Auto", $"AssertVendorContracts: Open('{ctx}') threw — {ex.Message}");
                    continue;
                }

                // Wait a frame so the stock build (inside Open) has populated CurrentStock.
                // (It is synchronous, but a frame is cheap insurance.)
                yield return null;

                // A NotifyOpened REFUSAL (in battle, or another modal holds the arbiter) leaves
                // the panel closed with a disposed VM, so CurrentStock reads EMPTY. Distinguish
                // that from a genuinely empty vendor - reporting a refused open as an empty
                // vendor would be measuring the wrong thing (CLAUDE.md §11B).
                if (!panel.IsOpen)
                {
                    FlowTrace.Warn("Auto", $"AssertVendorContracts: '{ctx}' open was REFUSED by PanelManager " +
                        "(battle/another modal) - the panel is closed and its VM disposed, so stock cannot be " +
                        "read. NOT counted as an empty vendor.");
                    yield return Wait(SettleSeconds);
                    continue;
                }

                try
                {
                    string vc = panel.VendorContext;
                    var allowed = VendorStockContract.AllowedFor(vc);
                    var stock = panel.CurrentStock;
                    int n = stock != null ? stock.Count : 0;

                    bool compliant = true;
                    if (stock != null)
                    {
                        foreach (var item in stock)
                        {
                            if ((allowed & item.kind) == 0)
                            {
                                compliant = false;
                                violations++;
                                FlowTrace.Fail("Auto", $"VENDOR CONTRACT VIOLATION: '{vc}' allows {allowed} but stocked {item.id} ({item.kind})");
                            }
                        }
                    }

                    if (n == 0)
                    {
                        // A vendor should not be empty when its contract allows a category the
                        // catalog actually has stock for. Weapon/Armor come from GearCatalog;
                        // Potion is the panel's built-in potion list (always available), so an
                        // allowed-Potion vendor that built nothing is the meaningful empty case.
                        bool catalogHasWeapons = SafeAny(GearCatalog.AllWeapons());
                        bool catalogHasArmors  = SafeAny(GearCatalog.AllArmors());
                        bool shouldHaveStock =
                            ((allowed & GearKind.Weapon) != 0 && catalogHasWeapons) ||
                            ((allowed & GearKind.Armor)  != 0 && catalogHasArmors)  ||
                            ((allowed & GearKind.Potion) != 0);
                        if (shouldHaveStock)
                        {
                            emptyWarns++;
                            FlowTrace.Warn("Auto", $"AssertVendorContracts: vendor '{vc}' (allows {allowed}) built EMPTY stock though the catalog has matching wares.");
                        }
                        else
                        {
                            FlowTrace.Step("Auto", $"AssertVendorContracts: '{vc}' empty (allows {allowed}; catalog has no matching gear) — not flagged.");
                        }
                    }
                    else if (compliant)
                    {
                        FlowTrace.Step("Auto", $"AssertVendorContracts: '{vc}' COMPLIES — {n} item(s), all within {allowed}.");
                    }

                    checkedCount++;
                }
                catch (Exception ex)
                {
                    FlowTrace.Fail("Auto", $"AssertVendorContracts: assertion for '{ctx}' threw — {ex.Message}");
                }

                yield return Wait(SettleSeconds);
            }

            // Close/dispose: re-Open's Close ran per ctx; destroy our host so nothing lingers.
            // (PartyShopPanelMvvm DOES register with PanelManager in Awake and unregisters in
            // OnDestroy, so destroying the host is still the bot's clean teardown.) Belt-and-braces
            // CloseOpen too.
            try { PanelManager.CloseOpen(); } catch { }
            if (createdHost && host != null) UnityEngine.Object.Destroy(host);

            _lastDetail = $"{checkedCount} contexts checked, {violations} violation(s), {emptyWarns} empty-warn(s)";
            FlowTrace.Step("Auto", $"AssertVendorContracts: {_lastDetail}.");
        }

        // Null-safe "any element" over an IEnumerable (GearCatalog returns lists that are
        // never null per its contract, but guard anyway).
        private static bool SafeAny<T>(System.Collections.Generic.IEnumerable<T> seq)
        {
            if (seq == null) return false;
            foreach (var _ in seq) return true;
            return false;
        }

        // =====================================================================
        //  PHASE: AssertVendorTalkRoute  (TKT-15 talk-fix DATA-VERIFY, 2026-06-20)
        //  Owner top-priority bug: the castle Talk button must open the vendor Buy/Sell
        //  DIALOGUE for SHOPPABLE vendors (forge/armorer/market/jeweler), NOT be stolen
        //  by the upgrade panel (the TKT-15 regression). OpenEachVendor never exercises
        //  this — castle vendors are CastleNpcInteractable, not BuildingInteractable. So
        //  this oracle drives the REAL routing: for each castle vendor it reflect-invokes
        //  the private Interact() (the exact path the HUD Talk button fires via
        //  TalkPromptRegistry) and asserts a SHOPPABLE vendor took the dialogue route
        //  (DialogueService.IsRunning) rather than opening the BuildingUpgrade panel. A
        //  violation is FlowTrace.Fail -> break-log -> ranked ticket. Upgrade-only vendors
        //  (lumbermill/farm/arcane-tower) are reported, not flagged. PlayStructure is the
        //  same headless-safe seam OpenEachVendor already uses.
        // =====================================================================
        private IEnumerator AssertVendorTalkRoute()
        {
            var vendors = UnityEngine.Object.FindObjectsByType<CastleNpcInteractable>();
            if (vendors == null || vendors.Length == 0)
            {
                _lastDetail = "0 CastleNpcInteractable found (not in MainCastle_Hall?)";
                FlowTrace.Warn("Auto", "AssertVendorTalkRoute: 0 castle vendors found — talk route not exercised.");
                yield break;
            }

            var fId = typeof(CastleNpcInteractable).GetField(
                "_structureId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fId == null)
            {
                _lastDetail = "reflection failed (_structureId not found)";
                FlowTrace.Fail("Auto", "AssertVendorTalkRoute: could not reflect _structureId on CastleNpcInteractable.");
                yield break;
            }

            int checkedCount = 0, shoppableChecked = 0, violations = 0;
            foreach (var v in vendors)
            {
                if (v == null) continue;
                string id = fId.GetValue(v) as string;
                if (string.IsNullOrEmpty(id)) continue;

                bool shoppable = false;
                try { var def = BuildingCatalog.Find(id); shoppable = def != null && def.IsShoppable; } catch { }

                // Verify the routing DECISION via the SAME shared method Interact() uses — PURE, no
                // side effects. (Invoking the full Interact() would host a Yarn dialogue whose teardown
                // Stop() races the known No-node bug, polluting the break-log every cycle — the RUN-1
                // harness-integrity trap. ResolveRoute is the single source of truth, so asserting it
                // cannot drift from the real branch, and the opened SURFACE is headless-invisible anyway.)
                string route = null;
                try { route = CastleNpcInteractable.ResolveRoute(id); }
                catch (Exception ex) { FlowTrace.Fail("Auto", $"AssertVendorTalkRoute: ResolveRoute('{id}') threw — {ex.Message}"); }

                checkedCount++;
                if (shoppable)
                {
                    shoppableChecked++;
                    if (route == "talk-dialogue")
                        FlowTrace.Step("Auto", $"AssertVendorTalkRoute: '{id}' shoppable -> route='talk-dialogue' (FIX OK).");
                    else
                    {
                        violations++;
                        FlowTrace.Fail("Auto", $"TALK-ROUTE VIOLATION: shoppable vendor '{id}' resolves to '{route ?? "<none>"}' " +
                            "(expected 'talk-dialogue') — the upgrade short-circuit is stealing Talk.");
                    }
                }
                else
                {
                    FlowTrace.Step("Auto", $"AssertVendorTalkRoute: '{id}' not-shoppable -> route='{route ?? "<none>"}' (informational).");
                }

                yield return null;
            }

            _lastDetail = $"{checkedCount} castle vendors ({shoppableChecked} shoppable), {violations} talk-route violation(s)";
            FlowTrace.Step("Auto", $"AssertVendorTalkRoute: {_lastDetail}.");
        }

        // =====================================================================
        //  PHASE: AssertVendorCoverage  (LEVER 1 confirming test, owner 2026-07-24)
        //  DATA-confirmed hub bug: store NPCs NEVER appeared — the vendor injector anchored
        //  ONLY to a live/replayed Building/collector, but strategic-placement standdown
        //  (always-on, WO-682) stands the baked ring + the two stations down, so on a fresh
        //  hub nothing replayed -> zero vendors ([Flow:Vendor] "<role> awaiting building —
        //  vendor not spawned" for EVERY role). The fix seats a vendor at each baked-storefront
        //  / station ANCHOR even under standdown. This oracle encodes the "should this structure
        //  have an NPC" logic from the INJECTOR'S OWN role map (CastleVendorNpcInjector.
        //  VendorRoles / RoleForBuildingId), NOT a hardcoded list:
        //    1. EVERY vendor role must have a seated CastleVendor_<role> NPC in the hub.
        //    2. NO non-action id (tower/wall/gate/mine/fountain/deco) may map to a vendor role.
        //  A violation is FlowTrace.Fail -> break-log -> ranked ticket (fails loudly).
        // =====================================================================
        private IEnumerator AssertVendorCoverage()
        {
            const string Tag = "Auto";
            var roles = CastleVendorNpcInjector.VendorRoles();
            if (roles == null || roles.Count == 0)
            {
                _lastDetail = "no vendor roles declared";
                FlowTrace.Warn(Tag, "AssertVendorCoverage: CastleVendorNpcInjector.VendorRoles() is empty — nothing to verify.");
                yield break;
            }

            // The injector's anchor poll ticks every ~2s; give the baked/station fallback time
            // to seat every role on this (possibly fresh) hub before asserting.
            var missing = new System.Collections.Generic.List<string>(roles);
            float t0 = Time.realtimeSinceStartup;
            while (missing.Count > 0 && Time.realtimeSinceStartup - t0 < 14f)
            {
                missing.RemoveAll(r =>
                    GameObject.Find($"CastleVendor_{r}") != null ||
                    GameObject.Find($"CastleVendor_{r}_Placeholder") != null);
                if (missing.Count == 0) break;
                yield return Wait(1f);
            }

            int seated = roles.Count - missing.Count;
            if (missing.Count > 0)
                FlowTrace.Fail(Tag, $"VENDOR-COVERAGE VIOLATION: {missing.Count}/{roles.Count} action storefront/station role(s) " +
                    $"have NO seated vendor NPC after 14s — [{string.Join(", ", missing)}]. The hub bug (store NPCs never appear) " +
                    "is present for these roles — the baked/station anchor fallback failed to seat them.");
            else
                FlowTrace.Step(Tag, $"AssertVendorCoverage: all {roles.Count} vendor role(s) have a seated NPC (LEVER 1 pre-stand OK).");

            // Non-action exclusion (gap #3): tower/wall/gate/mine/fountain/deco/repair ids must map
            // to NO vendor role, so placing a defense/decoration never spawns a spurious merchant.
            int leaks = 0;
            foreach (var nonAction in new[] { "tower_ground_archer", "wall_stone", "gate_main", "mine_gold", "fountain_plaza", "deco_banner", "repair_kit" })
            {
                string role = CastleVendorNpcInjector.RoleForBuildingId(nonAction);
                if (!string.IsNullOrEmpty(role))
                {
                    leaks++;
                    FlowTrace.Fail(Tag, $"NON-ACTION LEAK: id '{nonAction}' maps to vendor role '{role}' — a defense/deco must " +
                        "NEVER get a vendor (RoleForBuildingId must return null for it).");
                }
            }
            if (leaks == 0)
                FlowTrace.Step(Tag, "AssertVendorCoverage: non-action exclusion holds (towers/walls/gates/mines/fountains/deco map to no vendor role).");

            _lastDetail = $"{seated}/{roles.Count} roles seated, {missing.Count} missing, {leaks} non-action leak(s)";
            FlowTrace.Step(Tag, $"AssertVendorCoverage: {_lastDetail}.");
        }

        // =====================================================================
        //  PHASE: AssertEconomyDeduct  (ASSERTION-DEPTH EXPANSION)
        //  Correctness, not just "didn't crash": a buy must DEDUCT exactly the item
        //  cost from EconomyService AND grow VillageInventory by one. Open a vendor's
        //  shop (the same PartyShopPanelMvvm.Open(ctx) seam AssertVendorContracts uses), read the
        //  resource snapshot BEFORE, pick the first AFFORDABLE item in CurrentStock,
        //  perform the buy, read AFTER, and assert the delta is EXACTLY the cost and the
        //  inventory gained the item. A mismatch is FlowTrace.Fail -> break-log -> ticket.
        //
        //  FALLBACK (documented): the shop's real buy handlers are PRIVATE — the bot cannot
        //  cleanly trigger the panel's own buy. So per the spec it asserts the LOWER-LEVEL
        //  invariant the handlers are built on: for an affordable item,
        //  EconomyService.TrySpend(cost) deducts exactly that cost and VillageInventory.Add(id,1)
        //  grows the count — exactly the two lines each Try* handler runs on success. Potions are
        //  SKIPPED for cost-resolution (their cost is not exposed on the panel); we resolve
        //  weapon/armor cost via GearCatalog.GetBuyCost, which is the authoritative cost the
        //  handlers spend.
        //  ⚠ WO-1430 (2026-09-06): re-pointed off the legacy ShopPanel (deleted as doorless) onto
        //  the live PartyShopPanelMvvm, so this phase now exercises the shop the player opens.
        // =====================================================================
        private IEnumerator AssertEconomyDeduct()
        {
            var eco = EconomyService.Instance;
            var inv = VillageInventory.Instance;
            if (eco == null || inv == null)
            {
                FlowTrace.Warn("Auto", $"AssertEconomyDeduct: missing service (eco={(eco != null)}, inv={(inv != null)}) — skipping.");
                _lastDetail = "no economy/inventory service — skipped";
                yield break;
            }

            // Clear the modal arbiter first - PartyShopPanelMvvm registers with PanelManager and
            // its Open() bails on a NotifyOpened refusal (see AssertVendorContracts for the full
            // note). Here a refusal is benign: an empty stock falls through to the documented
            // synthetic-cost fallback below rather than raising a ticket, but the Warn keeps the
            // REASON honest instead of reading as "the catalog had nothing affordable".
            try { PanelManager.CloseAll(); } catch { }

            // One reusable PartyShopPanelMvvm host (mirror of AssertVendorContracts' teardown).
            PartyShopPanelMvvm panel = UnityEngine.Object.FindAnyObjectByType<PartyShopPanelMvvm>();
            bool createdHost = false;
            GameObject host = null;
            if (panel == null)
            {
                host = new GameObject("AutoPilotEconomyPanelHost");
                panel = host.AddComponent<PartyShopPanelMvvm>();
                createdHost = true;
            }

            // Open a general vendor (empty context -> "all" contract, the widest stock) so
            // we maximize the chance of finding an affordable, cost-resolvable gear item.
            try { panel.Open("", null); }
            catch (Exception ex)
            {
                FlowTrace.Fail("Auto", $"AssertEconomyDeduct: PartyShopPanelMvvm.Open('') threw — {ex.Message}");
                if (createdHost && host != null) UnityEngine.Object.Destroy(host);
                _lastDetail = "Open threw";
                yield break;
            }
            yield return null; // let the stock build populate CurrentStock
            if (!panel.IsOpen)
                FlowTrace.Warn("Auto", "AssertEconomyDeduct: the shop open was REFUSED by PanelManager " +
                    "(battle/another modal), so CurrentStock is empty for that reason - the synthetic-cost " +
                    "fallback below still exercises the TrySpend/Add invariant.");

            // Pick the first AFFORDABLE weapon/armor in the actual built stock whose cost is
            // resolvable + non-zero (a free item can't prove a deduction). Potions skipped.
            string buyId = null; GearKind buyKind = GearKind.None; ResourceCost cost = default; bool found = false;
            var stock = panel.CurrentStock;
            if (stock != null)
            {
                foreach (var item in stock)
                {
                    if (item.kind == GearKind.Weapon)
                    {
                        var w = GearCatalog.FindWeapon(item.id);
                        if (w == null) continue;
                        cost = GearCatalog.GetBuyCost(w);
                    }
                    else if (item.kind == GearKind.Armor)
                    {
                        var a = GearCatalog.FindArmor(item.id);
                        if (a == null) continue;
                        cost = GearCatalog.GetBuyCost(a);
                    }
                    else continue; // potion cost is not exposed on the panel — not assertable here

                    if (cost.IsZero) continue;        // need a real cost to prove a deduction
                    if (!eco.CanAfford(cost)) continue;
                    buyId = item.id; buyKind = item.kind; found = true;
                    break;
                }
            }

            // Teardown the panel surface now — we assert against the services directly.
            try { PanelManager.CloseOpen(); } catch { }
            if (createdHost && host != null) UnityEngine.Object.Destroy(host);

            if (!found)
            {
                // Not a ticket-worthy failure: the vendor simply had no affordable, cost-bearing
                // gear (empty catalog / can't afford anything). Grant a known item + cost so the
                // INVARIANT is still exercised — this is the documented lower-level fallback.
                FlowTrace.Step("Auto", "AssertEconomyDeduct: no affordable cost-bearing gear in stock; " +
                    "exercising the TrySpend/Add invariant with a synthetic cost the wallet can afford.");
                cost = new ResourceCost(wood: 1);
                if (!eco.CanAfford(cost))
                {
                    FlowTrace.Warn("Auto", "AssertEconomyDeduct: wallet cannot afford even 1 wood — cannot assert deduction. Skipping.");
                    _lastDetail = "no affordable item + empty wallet — skipped";
                    yield break;
                }
                buyId = "autopilot-deduct-probe"; buyKind = GearKind.None;
            }

            // Snapshot BEFORE.
            int wBefore = eco.Wood, iBefore = eco.Iron, fBefore = eco.Stone, cBefore = eco.Crystals;
            int invBefore = inv.Get(buyId);
            FlowTrace.Step("Auto", $"AssertEconomyDeduct: buying '{buyId}' ({buyKind}) cost W{cost.Wood} F{cost.Stone} I{cost.Iron} C{cost.Crystals} " +
                $"(wallet before W{wBefore} F{fBefore} I{iBefore} C{cBefore}, inv {invBefore}).");

            // Perform the buy via the lower-level invariant the private handlers run.
            bool spent = eco.TrySpend(cost);
            if (spent && inv != null) inv.Add(buyId, 1);

            int wAfter = eco.Wood, iAfter = eco.Iron, fAfter = eco.Stone, cAfter = eco.Crystals;
            int invAfter = inv.Get(buyId);

            // ASSERT: spend succeeded, resources dropped by EXACTLY the cost, inventory +1.
            bool ok = spent;
            if (!spent)
                FlowTrace.Fail("Auto", $"AssertEconomyDeduct: TrySpend returned FALSE for an affordable cost (CanAfford was true) — economy did not deduct for '{buyId}'.");

            if (spent)
            {
                bool deductExact = (wBefore - wAfter) == cost.Wood
                                && (iBefore - iAfter) == cost.Iron
                                && (fBefore - fAfter) == cost.Stone
                                && (cBefore - cAfter) == cost.Crystals;
                if (!deductExact)
                {
                    ok = false;
                    FlowTrace.Fail("Auto", $"AssertEconomyDeduct: economy did not deduct by the exact cost — " +
                        $"deltas W{wBefore - wAfter}/F{fBefore - fAfter}/I{iBefore - iAfter}/C{cBefore - cAfter} " +
                        $"vs cost W{cost.Wood}/F{cost.Stone}/I{cost.Iron}/C{cost.Crystals}.");
                }
                if (invAfter != invBefore + 1)
                {
                    ok = false;
                    FlowTrace.Fail("Auto", $"AssertEconomyDeduct: inventory not updated — '{buyId}' was {invBefore}, now {invAfter} (expected {invBefore + 1}).");
                }
            }

            if (ok)
                FlowTrace.Step("Auto", $"AssertEconomyDeduct: PASS — '{buyId}' deducted exactly + inventory {invBefore}->{invAfter}.");
            _lastDetail = ok
                ? $"deduct OK for '{buyId}' (inv {invBefore}->{invAfter})"
                : $"deduct/inventory MISMATCH for '{buyId}'";
            yield return Wait(SettleSeconds);
        }

        // =====================================================================
        //  PHASE: AssertEquip  (ASSERTION-DEPTH EXPANSION)
        //  Correctness: equipping a weapon must CHANGE the hero's loadout/stat. Resolve
        //  (or attach) the hero's GearLoadout, pick a catalog weapon, add it to the
        //  inventory, equip it via GearLoadout.EquipWeaponById (the same public seam the
        //  the shop's EQUIP action uses), and assert EquippedWeapon is non-null afterward AND
        //  the loadout actually reflects what we equipped (or WeaponMult moved). A no-op
        //  equip (loadout unchanged) is FlowTrace.Fail -> break-log -> ticket.
        // =====================================================================
        private IEnumerator AssertEquip()
        {
            // Resolve the hero's GearLoadout (lazily attach if absent — the shop's EQUIP
            // path does the same: AddComponent<GearLoadout>() on the hero when none exists).
            GameObject heroGo = _hero != null ? _hero.gameObject : null;
            if (heroGo == null)
            {
                var tagged = GameObject.FindWithTag("Player");
                if (tagged != null) heroGo = tagged;
            }
            if (heroGo == null)
            {
                FlowTrace.Warn("Auto", "AssertEquip: no hero GameObject to equip on — skipping.");
                _lastDetail = "no hero — skipped";
                yield break;
            }

            GearLoadout loadout = heroGo.GetComponent<GearLoadout>();
            if (loadout == null) loadout = heroGo.AddComponent<GearLoadout>();
            yield return null; // let Awake/OnEnable run (Refresh may auto-equip a best item)

            // Pick a catalog weapon to force-equip. Prefer one DIFFERENT from whatever is
            // auto-equipped so the change is observable even if a best-weapon was auto-set.
            // WO-1214: the candidate MUST be one this hero may legally hold. This loop used to
            // take the FIRST row in the catalog regardless of class or level - which "worked"
            // only because GearLoadout.EquipWeaponById enforced neither gate. The seam now fails
            // closed (Ruling 3), so an ineligible pick would be refused and this phase would
            // report a phantom "equip path is a no-op". Ask the ONE authority instead.
            WeaponDef target = null;
            var beforeWeapon = loadout.EquippedWeapon;
            string beforeId = beforeWeapon != null ? beforeWeapon.id : null;
            float multBefore = loadout.WeaponMult;

            var heroAbilities = heroGo.GetComponent<HeroAbilities>();
            var heroProgression = heroGo.GetComponent<HeroProgression>();
            string equipJob = heroAbilities != null && !string.IsNullOrEmpty(heroAbilities.HeroClass)
                ? heroAbilities.HeroClass : AbilityCatalog.DefaultClass;
            int equipLevel = heroProgression != null ? heroProgression.Level : 1;

            foreach (var w in GearCatalog.AllWeapons())
            {
                if (w == null || w.IsOffHandItem) continue;     // main hand only — a shield is a different slot
                if (!GearCatalog.CanEquipWeapon(w, equipJob, equipLevel, out _)) continue;
                if (target == null) target = w;                 // first ELIGIBLE as a fallback
                if (beforeId == null || w.id != beforeId) { target = w; break; } // prefer a different one
            }

            if (target == null)
            {
                FlowTrace.Warn("Auto",
                    $"AssertEquip: the gear catalog serves class '{equipJob}' NO main-hand weapon at level " +
                    $"{equipLevel} — skipping (cannot assert equip wiring). This is a CATALOG gap, not an equip bug.");
                _lastDetail = "no eligible catalog weapon — skipped";
                yield break;
            }

            // Own it first (mirrors the player flow: buy/own -> equip), then equip via the
            // public seam. EquipWeaponById no-ops if the id isn't in the catalog — we chose
            // target FROM the catalog, so it must resolve.
            if (VillageInventory.Instance != null) VillageInventory.Instance.Add(target.id, 1);

            FlowTrace.Step("Auto", $"AssertEquip: equipping '{target.id}' (before weapon='{beforeId ?? "<null>"}', mult={multBefore:0.00}).");
            loadout.EquipWeaponById(target.id);
            yield return null; // let ApplyStats run

            var afterWeapon = loadout.EquippedWeapon;
            float multAfter = loadout.WeaponMult;

            // ASSERT: a weapon is now equipped AND the loadout reflects the equip — either it
            // is the exact piece we forced, or (defensive) the damage multiplier moved. A
            // loadout that did NOT change at all means the equip path is a no-op.
            bool nowEquipped = afterWeapon != null;
            bool isTarget    = afterWeapon != null && afterWeapon.id == target.id;
            bool multMoved   = !Mathf.Approximately(multAfter, multBefore);
            bool changed     = isTarget || multMoved;

            if (!nowEquipped)
            {
                FlowTrace.Fail("Auto", $"AssertEquip: EquippedWeapon is NULL after EquipWeaponById('{target.id}') — equip path did not set the loadout.");
                _lastDetail = "equip left EquippedWeapon null";
            }
            else if (!changed)
            {
                FlowTrace.Fail("Auto", $"AssertEquip: loadout did NOT change — equipped '{(afterWeapon != null ? afterWeapon.id : "<null>")}' " +
                    $"but expected '{target.id}' and WeaponMult stayed {multAfter:0.00}. Equip is a no-op.");
                _lastDetail = "equip did not change loadout";
            }
            else
            {
                FlowTrace.Step("Auto", $"AssertEquip: PASS — EquippedWeapon='{afterWeapon.id}' (target '{target.id}', " +
                    $"mult {multBefore:0.00}->{multAfter:0.00}).");
                _lastDetail = $"equipped '{afterWeapon.id}' (mult {multBefore:0.00}->{multAfter:0.00})";
            }
            yield return Wait(SettleSeconds);
        }

        // =====================================================================
        //  PHASE: AssertSaveRoundTrip  (WO-452 tranche D)
        //  Live play -> quicksave -> reload oracle. Mutate three asserted domains
        //  (wallet/Resources, party roster, tracked quest id) to KNOWN marker values,
        //  GameStateService.Save(), PERTURB the live SO away from those values, then
        //  Load() and assert all three were restored. This guards the LIVE
        //  serialize->PlayerPrefs->migrate->validate->apply path the headless
        //  SessionRegression data round-trip can't see. Restores the player's original
        //  save at the end so the probe leaves no trace.
        // =====================================================================
        private IEnumerator AssertSaveRoundTrip()
        {
            const string Tag = "Auto";
            var svc = DeNelle.Core.State.GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                FlowTrace.Warn(Tag, "AssertSaveRoundTrip: GameStateService/State unavailable — skipping.");
                _lastDetail = "no GameStateService — skipped";
                yield break;
            }

            var state = svc.State;

            // WO-586 fleet save-probe ISOLATION: all N fleet processes share ONE PlayerPrefs
            // hive, so probing the real "dotr-save" slot let sibling instances stomp the blob
            // inside this probe's 2-frame Save()->Load() window — the 07-03/07-06 false
            // WALLET/ROSTER/QUEST drift (foreign blob carries a valid HMAC, loads silently).
            // Swap in a seed-suffixed provider for the probe's duration: every write/read maps
            // slot -> slot + "-probe-<seed>", unique per instance. The REAL slot is never
            // touched, so no preserve/restore of the player blob is needed.
            var origProvider = DeNelle.Core.State.GameStateService.Provider;
            DeNelle.Core.State.GameStateService.Provider = new SeedScopedSaveProvider(origProvider, "-probe-" + _seed);

            string probeRosterId = "AutoPilotSaveProbe-" + _seed;
            string probeQuestId  = "autopilot-quest-" + _seed;
            const int ProbeCrystals = 4242;

            // 1) Set KNOWN marker state across wallet/roster/quest.
            bool ok = true; string err = null;
            try
            {
                var res = state.Resources; res.Crystals = ProbeCrystals; state.Resources = res;
                if (state.PartyMemberIds == null) state.PartyMemberIds = new List<string>();
                if (!state.PartyMemberIds.Contains(probeRosterId)) state.PartyMemberIds.Add(probeRosterId);
                if (state.Quests == null) state.Quests = DeNelle.Core.State.QuestProgress.Empty();
                state.Quests.TrackedId = probeQuestId;
            }
            catch (Exception ex) { ok = false; err = ex.Message; }
            if (!ok)
            {
                FlowTrace.Fail(Tag, "AssertSaveRoundTrip: failed to set marker state — " + err);
                _lastDetail = "set-marker threw";
                RestoreProbe(origProvider, svc);
                yield break;
            }

            // 2) QUICKSAVE.
            try { svc.Save(); } catch (Exception ex) { ok = false; err = ex.Message; }
            if (!ok)
            {
                FlowTrace.Fail(Tag, "AssertSaveRoundTrip: Save() threw — " + err);
                _lastDetail = "Save threw";
                RestoreProbe(origProvider, svc);
                yield break;
            }
            yield return null;

            // 3) PERTURB the live SO away from the saved values (without persisting) so the
            //    reload has something to actually restore.
            try
            {
                var res2 = state.Resources; res2.Crystals = 0; state.Resources = res2;
                if (state.PartyMemberIds != null) state.PartyMemberIds.Remove(probeRosterId);
                if (state.Quests != null) state.Quests.TrackedId = "perturbed-" + _seed;
            }
            catch (Exception ex) { FlowTrace.Warn(Tag, "AssertSaveRoundTrip: perturb threw — " + ex.Message); }
            yield return null;

            // 4) RELOAD from the saved PlayerPrefs.
            try { svc.Load(); } catch (Exception ex) { ok = false; err = ex.Message; }
            if (!ok)
            {
                FlowTrace.Fail(Tag, "AssertSaveRoundTrip: Load() threw — " + err);
                _lastDetail = "Load threw";
                RestoreProbe(origProvider, svc);
                yield break;
            }
            yield return null;

            // 5) ASSERT the three domains survived the round-trip.
            int crystalsAfter = state.Resources.Crystals;
            bool rosterAfter = state.PartyMemberIds != null && state.PartyMemberIds.Contains(probeRosterId);
            string questAfter = state.Quests != null ? state.Quests.TrackedId : null;

            bool walletOk = crystalsAfter == ProbeCrystals;
            bool rosterOk = rosterAfter;
            bool questOk  = questAfter == probeQuestId;

            if (!walletOk)
                FlowTrace.Fail(Tag, $"AssertSaveRoundTrip: WALLET drift — crystals {crystalsAfter} after reload, expected {ProbeCrystals}.");
            if (!rosterOk)
                FlowTrace.Fail(Tag, $"AssertSaveRoundTrip: ROSTER drift — '{probeRosterId}' missing after reload " +
                    $"(roster=[{(state.PartyMemberIds != null ? string.Join(",", state.PartyMemberIds) : "<null>")}]).");
            if (!questOk)
                FlowTrace.Fail(Tag, $"AssertSaveRoundTrip: QUEST drift — tracked id '{questAfter ?? "<null>"}' after reload, expected '{probeQuestId}'.");

            bool pass = walletOk && rosterOk && questOk;
            if (pass)
                FlowTrace.Step(Tag, $"AssertSaveRoundTrip: PASS — wallet+roster+quest all survived Save()->Load() (crystals={crystalsAfter}, roster has probe, quest='{questAfter}').");
            _lastDetail = $"wallet={walletOk} roster={rosterOk} quest={questOk} -> {(pass ? "PASS" : "FAIL")}";

            // Restore the real provider so the probe leaves no marker behind.
            RestoreProbe(origProvider, svc);
        }

        // WO-586: unwind the probe's seed-scoped provider — delete the probe slot, restore
        // the real provider, and re-Load the player's untouched real save (when one exists)
        // so the rest of the run continues from real state. Never throws.
        private static void RestoreProbe(DeNelle.Core.State.ISaveProvider origProvider, DeNelle.Core.State.GameStateService svc)
        {
            try
            {
                string slot = DeNelle.Core.State.SaveSchema.PlayerPrefsKey;
                DeNelle.Core.State.GameStateService.Provider?.Delete(slot);   // maps to the -probe-<seed> slot
                DeNelle.Core.State.GameStateService.Provider = origProvider;
                if (svc != null && origProvider != null && origProvider.Exists(slot)) svc.Load();
            }
            catch (Exception ex) { FlowTrace.Warn("Auto", "AssertSaveRoundTrip: restore threw — " + ex.Message); }
        }

        // WO-586: test-only decorator — maps every slot to slot+suffix ("-probe-<seed>") so a
        // fleet instance's save probe round-trips a slot no sibling process touches. Shipping
        // save code (GameStateService/SaveSchema/LocalSaveProvider) is unchanged; this lives
        // and dies inside AssertSaveRoundTrip.
        private sealed class SeedScopedSaveProvider : DeNelle.Core.State.ISaveProvider
        {
            private readonly DeNelle.Core.State.ISaveProvider _inner;
            private readonly string _suffix;
            public SeedScopedSaveProvider(DeNelle.Core.State.ISaveProvider inner, string suffix)
            { _inner = inner; _suffix = suffix; }
            public bool Exists(string slot)             => _inner.Exists(slot + _suffix);
            public string Read(string slot)             => _inner.Read(slot + _suffix);
            public void Write(string slot, string json) => _inner.Write(slot + _suffix, json);
            public void Delete(string slot)             => _inner.Delete(slot + _suffix);
        }

        // =====================================================================
        //  PHASE: AssertCombatInvariants  (WO-452 tranche C)
        //  Runs during the triggered wave and asserts three combat invariants:
        //    (1) HERO HP never < 0 while still alive (not defeated) — a clamp regression.
        //    (2) A placed TOWER actually fired in the defense window — detected via the
        //        existing TowerAI "picked target" acquire trace (emitted on the fire tick
        //        when FlowTrace.Enabled). We assert the event APPEARED, not damage math.
        //    (3) >=2 distinct enemy type ids appeared (read off WaveManager.LiveEnemies).
        //  N/A (skipped, success) when the scene has no WaveManager (e.g. the hub). To
        //  avoid false tickets: a single-type wave is a Warn (early waves are 1 type),
        //  and the tower check downgrades to a Warn when FlowTrace is disabled.
        // =====================================================================
        private IEnumerator AssertCombatInvariants()
        {
            const string Tag = "Auto";
            var wm = WaveManager.Instance ?? UnityEngine.Object.FindAnyObjectByType<WaveManager>();
            if (wm == null)
            {
                FlowTrace.Step(Tag, "AssertCombatInvariants: no WaveManager in scene — N/A (no wave loop), skipping.");
                _lastDetail = "no WaveManager — N/A (skipped)";
                yield break;
            }

            var hero = HeroHealth.Instance;

            // Detect a REAL tower fire via the existing TowerAI acquire trace ("picked target"
            // is logged on the fire tick, immediately before FireAt). Subscribe for the window.
            bool towerFired = false;
            Application.LogCallback onLog = (msg, st, type) =>
            {
                if (string.IsNullOrEmpty(msg)) return;
                if (msg.IndexOf("[Flow:TowerAI]", StringComparison.OrdinalIgnoreCase) >= 0
                    && msg.IndexOf("picked target", StringComparison.OrdinalIgnoreCase) >= 0)
                    towerFired = true;
            };
            Application.logMessageReceived += onLog;

            var seenTypes = new HashSet<string>();
            bool heroHpNegative = false;
            float heroHpAtFault = 0f;

            // Make sure a wave is running so towers have something to shoot.
            if (wm.Phase == WavePhase.Idle)
            {
                try { wm.ForceSpawnNextWaveNow(); }
                catch (Exception ex) { FlowTrace.Warn(Tag, "AssertCombatInvariants: ForceSpawnNextWaveNow threw " + ex.Message); }
            }

            bool towersExist = UnityEngine.Object.FindObjectsByType<TowerCombat>().Length > 0;

            const float Window = 12f;   // defense window
            float t0 = Time.realtimeSinceStartup;
            bool hudShot = false;
            while (Time.realtimeSinceStartup - t0 < Window)
            {
                // COMBAT-HUD CAPTURE (WO-611 image-pair): the wave flips the SAME
                // hostile(activebattle) posture as the arena, and the bot reliably survives
                // waves (12/12 fleet) — unlike the arena drop, where 4 straight runs died
                // before the frame. Shoot once, 3s into the defense window (wave live,
                // widgets settled). Realtime-driven, so a death-pause can't stall it.
                if (!hudShot && Time.realtimeSinceStartup - t0 >= 3f)
                {
                    hudShot = true;
                    try
                    {
                        string shotDir = System.IO.Path.Combine(Application.persistentDataPath, "ui-shots");
                        System.IO.Directory.CreateDirectory(shotDir);
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, "battle_hud_wave.png"));
                        FlowTrace.Step(Tag, "AssertCombatInvariants: battle_hud_wave.png captured (hostile posture HUD).");
                    }
                    catch (Exception ex) { FlowTrace.Warn(Tag, "battle_hud_wave capture threw " + ex.Message); }
                }
                // (1) hero HP invariant — never negative while still alive.
                if (hero != null && hero.Hp < 0f && hero.IsAlive)
                {
                    heroHpNegative = true;
                    heroHpAtFault = hero.Hp;
                }

                // (3) enemy variety — collect distinct type ids from the live roster.
                var live = wm.LiveEnemies;
                if (live != null)
                {
                    for (int i = 0; i < live.Count; i++)
                    {
                        var e = live[i];
                        if (e == null || e.IsDead) continue;
                        string id = !string.IsNullOrEmpty(e.EnemyDefId) ? e.EnemyDefId : e.EnemyId;
                        if (!string.IsNullOrEmpty(id)) seenTypes.Add(id);
                    }
                }
                yield return null;
            }

            Application.logMessageReceived -= onLog;

            // (1) hero HP verdict
            if (heroHpNegative)
                FlowTrace.Fail(Tag, $"AssertCombatInvariants: hero HP went NEGATIVE ({heroHpAtFault:0.0}) while still alive (not defeated) — HP-clamp regression.");

            // (2) tower-fired verdict
            if (towersExist)
            {
                if (towerFired)
                    FlowTrace.Step(Tag, "AssertCombatInvariants: a tower fired during the defense window (TowerAI acquire trace seen).");
                else if (FlowTrace.Enabled)
                    FlowTrace.Fail(Tag, "AssertCombatInvariants: NO tower fired during the ~12s defense window though tower(s) are placed (no TowerAI acquire trace) — towers inert.");
                else
                    FlowTrace.Warn(Tag, "AssertCombatInvariants: tower-fire check skipped (FlowTrace disabled — no acquire trace to read).");
            }
            else
            {
                FlowTrace.Step(Tag, "AssertCombatInvariants: no towers placed — tower-fire invariant N/A.");
            }

            // (3) enemy-variety verdict
            if (seenTypes.Count >= 2)
                FlowTrace.Step(Tag, $"AssertCombatInvariants: {seenTypes.Count} distinct enemy type(s) observed [{string.Join(",", seenTypes)}].");
            else if (seenTypes.Count == 0)
                FlowTrace.Warn(Tag, "AssertCombatInvariants: no enemies spawned during the window — variety check N/A.");
            else
                FlowTrace.Warn(Tag, $"AssertCombatInvariants: only 1 enemy type observed [{string.Join(",", seenTypes)}] — wave lacked variety (early single-type waves are expected; warning, not a hard fail).");

            bool towerOk = !towersExist || towerFired || !FlowTrace.Enabled;
            bool pass = !heroHpNegative && towerOk;
            _lastDetail = $"heroHpNeg={heroHpNegative} towerFired={towerFired} (towers={towersExist}) enemyTypes={seenTypes.Count} -> {(pass ? "PASS" : "FAIL")}";
        }

        // =====================================================================
        //  PHASE: OpenEachHUDPanel
        //  For every PanelId registered with PanelRouter: open, assert AnyOpen,
        //  actuate the clickables on the open surface, then CloseOpen.
        // =====================================================================
        // Write a per-panel screenshot for UI-fidelity review (compare vs the Blink template
        // PNGs in Assets/Blink/.../Panels_Obsidian). Renders only with graphics ON — a
        // -nographics fleet writes a blank frame. Lands in persistentDataPath/ui-shots/.
        private static void CaptureUiPanel(string name)
        {
            CaptureRawShot("panel_" + name + ".png");
        }

        // Shared best-effort screenshot writer — the ONE graphics-on guard every capture
        // route uses (renders only with graphics ON; a -nographics fleet writes a blank
        // frame, never an error). Lands in persistentDataPath/ui-shots/ next to the
        // panel_/bridge_ shots. Any arbitrary file name (panel_<Screen>.png, moat_ring.png,
        // <scene>.png) routes through here so there is exactly one capture path to maintain.
        // BLANK-WRITE GUARD (2026-08-04). This writer used to fire unconditionally, and
        // the comment above recorded the consequence as acceptable: "a -nographics fleet
        // writes a blank frame, never an error". It is NOT acceptable, and it is what
        // emptied the owner's UI review.
        //
        // PROVING DATA, not inference: Builds\Windows\DefendersOfTheRealm.exe was built at
        // 21:18:09 on 2026-08-04; three minutes later, at 21:21:06, THIRTY-FIVE
        // panel_*.png files in ui-shots were rewritten at exactly 33150 bytes each -- the
        // signature of a flat black frame. A fleet had run in the DEFAULT mode
        // (run-autopilot-fleet.ps1 passes -batchmode -nographics unless -Graphics is
        // given), and every panel it walked overwrote a previously REAL review shot with
        // black. build-ui-review.ps1 then paired those blanks against the Blink templates
        // and reported "PAIR COMPLETE", which is why the owner opened INDEX.html and saw
        // "mostly just the blank templates and nothing else".
        //
        // A logic/flow fleet has no business writing review artefacts at all. With no
        // graphics device we now write NOTHING: the previous real shot survives, and a
        // screen that was never captured reads as MISSING in the review -- which gets
        // chased -- instead of as a blank, which gets reviewed.
        private static bool s_warnedNoGraphicsShot;

        private static void CaptureRawShot(string fileName)
        {
            try
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    if (!s_warnedNoGraphicsShot)
                    {
                        s_warnedNoGraphicsShot = true;
                        FlowTrace.Warn("Auto", "UI shots SKIPPED for this run: no graphics device " +
                            "(-nographics). ScreenCapture would write flat black frames and silently " +
                            "overwrite the real UI_REVIEW shots. Re-run with " +
                            "run-autopilot-fleet.ps1 -Graphics for capture.");
                    }
                    return;
                }

                string dir = System.IO.Path.Combine(Application.persistentDataPath, "ui-shots");
                System.IO.Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, fileName));
            }
            catch { /* capture is best-effort; never break the drive loop */ }
        }

        // Settled screenshot writer — guarantees the backbuffer is POST-RENDER before the grab.
        // WaitForEndOfFrame parks the coroutine until AFTER every camera has rendered and the frame
        // is about to be presented — essential for a panel that hosts a LIVE 3D preview
        // (EquipmentPanel: a manually-driven preview camera -> RenderTexture -> RawImage). A plain
        // `yield return null` can grab the backbuffer MID-COMPOSITE and write RGB static (the
        // reproducible ~5MB garbage EquipmentPanel PNG). extraSettleFrames gives a preview an extra
        // beat to finish rendering before the end-of-frame grab.
        // GRAPHICS GUARD: WaitForEndOfFrame only resumes when a graphics device is present; under a
        // -nographics fleet it would NEVER fire and hang the drive, so it is gated on the device
        // (headless still writes a blank frame via CaptureRawShot — never an error, never a hang).
        private IEnumerator CaptureUiPanelSettled(string name, int extraSettleFrames = 0)
        {
            for (int i = 0; i < extraSettleFrames; i++) yield return null;
            bool hasGfx = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (hasGfx) yield return new WaitForEndOfFrame();
            CaptureUiPanel(name);
            if (hasGfx) yield return new WaitForEndOfFrame();
            else yield return null;
        }

        private IEnumerator OpenEachHUDPanel()
        {
            // BATTLE-AWARE (run 10603: this phase opened 0/12 while the battle-aware popup
            // oracle right after it passed 11 — DiagGarrisonRoster's spawns leave battle-lock
            // briefly raised and PanelManager designed-rejects every open). Same bounded
            // wait the popup oracle uses.
            float battleWait = 0f;
            while (DeNelle.Core.Combat.BattleLock.IsInBattle() && battleWait < 20f)
            {
                yield return Wait(1f);
                battleWait += 1f;
            }
            if (battleWait > 0f)
                FlowTrace.Step("Auto", $"OpenEachHUDPanel: waited {battleWait:F0}s for battle-lock to clear.");

            int opened = 0, registered = 0;
            // Seeded panel order so different bots open panels in different sequences.
            var panelIds = new List<PanelId>();
            foreach (PanelId pid in Enum.GetValues(typeof(PanelId))) panelIds.Add(pid);
            Shuffle(panelIds);
            foreach (PanelId id in panelIds)
            {
                if (!PanelRouter.IsRegistered(id)) continue;
                registered++;

                FlowTrace.Step("Auto", $"OpenEachHUDPanel: opening '{id}'.");
                bool ok = PanelRouter.Open(id);
                yield return Wait(SettleSeconds);

                if (!ok)
                {
                    FlowTrace.Warn("Auto", $"OpenEachHUDPanel: PanelRouter.Open({id}) returned false.");
                    continue;
                }
                if (!PanelManager.AnyOpen)
                {
                    FlowTrace.Warn("Auto", $"OpenEachHUDPanel: '{id}' opened but PanelManager.AnyOpen is false (panel may not register a handle).");
                }
                else
                {
                    FlowTrace.Step("Auto", $"OpenEachHUDPanel: '{id}' open (PanelManager='{PanelManager.OpenPanelName}').");
                    opened++;
                }

                // UI-fidelity shot (renders graphics-on; blank under -nographics). EquipmentPanel
                // hosts a LIVE 3D preview (preview camera -> RenderTexture -> RawImage); give it
                // extra frames + an end-of-frame grab so the full composite lands in the backbuffer
                // (a plain yield grabbed RGB static — the ~5MB garbage PNG). WaitForEndOfFrame helps
                // every panel, so ALL panels route through the settled capture.
                int extraFrames = (id == PanelId.EquipmentPanel) ? 4 : 0;
                yield return CaptureUiPanelSettled(id.ToString(), extraFrames);

                ClickableActuator.ActuateAll(null, _rng);
                yield return Wait(SettleSeconds);

                PanelManager.CloseOpen();
                yield return Wait(SettleSeconds);
            }
            FlowTrace.Step("Auto", $"OpenEachHUDPanel: {opened}/{registered} registered panels verified open.");
            _lastDetail = $"{opened}/{registered} panels";
        }

        // =====================================================================
        //  PHASE: AssertPopupClose (WO-597 slice 1 — the POPUP-CLOSABLE oracle)
        //  Every popup the game can open MUST have a WORKING close trigger
        //  (owner 2026-07-02). REGISTRY-DRIVEN: enumerates the full PanelId enum
        //  (the panel registry — a new panel = a new enum value + Register call,
        //  so new panels are auto-covered) and for each registered id:
        //    open  -> assert a close AFFORDANCE exists (the shared master-frame
        //             chrome Close — ElarionUiKit.ObsidianCloseButton builds the
        //             ONE standard uGUI button named "CloseButton"; a *close*
        //             -named uGUI/UITK button also counts. Programmatic
        //             PanelManager.CloseOpen is NOT an affordance, and there is
        //             no global ESC->close router today, so no button = NO_CLOSE)
        //    click -> the affordance, the way a player would
        //    assert-> actually closed: the modal arbiter record cleared
        //             (PanelManager.AnyOpen false — this is also what releases
        //             the suppressed world input/prompts) AND the affordance
        //             gone/inactive in the hierarchy.
        //  VERDICTS (per panel, into autopilot-summary.json popupClose[]):
        //    PASS           — closed via its own affordance within the bound
        //    OPEN_FAILED    — PanelRouter.Open false / nothing recorded open (the
        //                     'open action ran but NO panel recorded open' class;
        //                     kept DISTINCT from NO_CLOSE so tickets separate
        //                     can't-open from can't-close)
        //    NO_CLOSE       — no affordance, the handler threw, or still open
        //                     after the bounded wait (a hang IS the bug: the bound
        //                     converts a stuck panel into the named Fail below,
        //                     never the generic 180s softlock)
        //    NOT_REGISTERED — no opener registered in this scene (informational)
        //  Violations are ERROR-LEVEL: FlowTrace.Fail("PopupClose",
        //  "POPUP_NO_CLOSE :: <panel> — <attempted route>") / "POPUP_OPEN_FAILED
        //  :: <panel> — ..." so they land in break-log.jsonl headless and rank as
        //  tickets. After a violation the run FORCE-CONTINUES (arbiter force-close,
        //  then destroy the stuck modal root, then scene reload as last resort) so
        //  one broken panel doesn't cost coverage of the rest.
        // =====================================================================
        private IEnumerator AssertPopupClose()
        {
            // Registry-driven enumeration: the PanelId enum IS the registry surface.
            var ids = new List<PanelId>();
            foreach (PanelId pid in Enum.GetValues(typeof(PanelId))) ids.Add(pid);
            Shuffle(ids);   // seeded order — chaos seeds walk the registry in different orders

            int pass = 0, openFailed = 0, noClose = 0, unregistered = 0;
            foreach (PanelId id in ids)
            {
                float tPanel = Time.realtimeSinceStartup;

                // BATTLE-AWARE (fleet-9000 RCA): PanelManager REJECTS every gameplay panel
                // while BattleLock.IsInBattle() (WO-437) — opening during a wave/engagement
                // is DESIGNED to fail. One run's popup phase overlapping a garrison wave
                // produced OPEN_FAILED x12 false positives. Wait (bounded) for the battle to
                // end; if it doesn't, record SKIPPED_IN_BATTLE — not a Fail.
                float battleWait = 0f;
                while (DeNelle.Core.Combat.BattleLock.IsInBattle() && battleWait < 20f)
                {
                    yield return Wait(1f);
                    battleWait += 1f;
                }
                if (DeNelle.Core.Combat.BattleLock.IsInBattle())
                {
                    FlowTrace.Warn("PopupClose", $"'{id}' skipped — BattleLock still in battle after {battleWait:F0}s (panel opens are designed-rejected in battle).");
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = id.ToString(), verdict = "SKIPPED_IN_BATTLE", route = "",
                        seconds = battleWait, detail = "battle-lock active through the whole wait window — open rejection would be by design, not a defect",
                    });
                    continue;
                }

                if (!PanelRouter.IsRegistered(id))
                {
                    unregistered++;
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = id.ToString(), verdict = "NOT_REGISTERED", route = "",
                        seconds = 0f, detail = "no opener registered in this scene — skipped",
                    });
                    continue;
                }

                // Clean slate: nothing may be open before this id's open, else the
                // arbiter signals below are ambiguous.
                if (PanelManager.AnyOpen) { PanelManager.CloseOpen(); yield return Wait(SettleSeconds); }

                FlowTrace.Step("PopupClose", $"opening '{id}' for the close-affordance oracle.");
                bool okOpen = false;
                try { okOpen = PanelRouter.Open(id); }
                catch (Exception ex) { FlowTrace.Warn("PopupClose", $"PanelRouter.Open({id}) threw at the seam: {ex.Message}"); }
                yield return Wait(SettleSeconds);

                if (!okOpen || !PanelManager.AnyOpen)
                {
                    openFailed++;
                    string why = !okOpen
                        ? "PanelRouter.Open returned false (opener threw, or open action ran but NO panel recorded open — the WO-465 invisible class)"
                        : "Open returned true but PanelManager.AnyOpen is false after settle";
                    FlowTrace.Fail("PopupClose", $"POPUP_OPEN_FAILED :: {id} — {why}.");
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = id.ToString(), verdict = "OPEN_FAILED", route = "PanelRouter.Open",
                        seconds = Time.realtimeSinceStartup - tPanel, detail = why,
                    });
                    PanelManager.CloseOpen();   // release any half-open record
                    yield return Wait(SettleSeconds);
                    continue;
                }

                string openName = PanelManager.OpenPanelName ?? id.ToString();

                // 1) A close AFFORDANCE must exist on the open surface.
                UnityEngine.UI.Button uClose = FindUGuiCloseButton();
                UnityEngine.UIElements.Button tkClose = uClose == null ? FindUiToolkitCloseButton() : null;
                string route;
                if (uClose == null && tkClose == null)
                {
                    noClose++;
                    route = "searched active uGUI 'CloseButton'/*close* + visible UITK *close* buttons — none found";
                    FlowTrace.Fail("PopupClose", $"POPUP_NO_CLOSE :: {id} — no close affordance on open panel '{openName}' ({route}).");
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = id.ToString(), verdict = "NO_CLOSE", route = route,
                        seconds = Time.realtimeSinceStartup - tPanel,
                        detail = "panel opened but exposes no Close control a player could tap",
                    });
                    yield return ForceContinueAfterStuckPanel(id, null, null);
                    continue;
                }

                // 2) TRIGGER it the way a player would.
                route = uClose != null
                    ? $"uGUI button '{uClose.name}'"
                    : $"UITK button '{(string.IsNullOrEmpty(tkClose.name) ? tkClose.text : tkClose.name)}'";
                bool clicked = false;
                try
                {
                    if (uClose != null) { uClose.onClick?.Invoke(); }
                    else { ClickUiToolkitButton(tkClose); }
                    clicked = true;
                    FlowTrace.Step("PopupClose", $"'{id}': triggered close via {route}.");
                }
                catch (Exception ex)
                {
                    FlowTrace.Fail("PopupClose", $"POPUP_NO_CLOSE :: {id} — close handler THREW via {route}: {ex.Message}");
                }

                // 3) BOUNDED close-wait -> assert actually closed. The bound converts a
                //    panel that swallows input and never closes into the named Fail.
                bool closed = false;
                float t0 = Time.realtimeSinceStartup;
                while (clicked && Time.realtimeSinceStartup - t0 < PopupCloseWaitSeconds)
                {
                    if (IsPopupFullyClosed(uClose, tkClose)) { closed = true; break; }
                    yield return null;
                }

                float took = Time.realtimeSinceStartup - tPanel;
                if (clicked && closed)
                {
                    pass++;
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = id.ToString(), verdict = "PASS", route = route,
                        seconds = took, detail = $"closed + input released in {Time.realtimeSinceStartup - t0:0.00}s",
                    });
                    FlowTrace.Step("PopupClose", $"'{id}': PASS — closed via {route}.");
                }
                else
                {
                    noClose++;
                    string detail = clicked
                        ? $"clicked {route} but panel still open after {PopupCloseWaitSeconds:0}s (AnyOpen={PanelManager.AnyOpen}, open='{PanelManager.OpenPanelName ?? "<none>"}') — stuck/hang converted to this named Fail"
                        : $"close trigger threw via {route}";
                    if (clicked)   // the throw case already emitted its own POPUP_NO_CLOSE above
                        FlowTrace.Fail("PopupClose", $"POPUP_NO_CLOSE :: {id} — {detail}.");
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = id.ToString(), verdict = "NO_CLOSE", route = route,
                        seconds = took, detail = detail,
                    });
                    yield return ForceContinueAfterStuckPanel(id, uClose, tkClose);
                }

                yield return Wait(SettleSeconds);
            }

            // RIPPLE (NPC card lane, 2026-07-02): PlayStructure("market"/...) now shows a
            // 2-node DIALOGUE CARD first (auto-advances to OpenShop -> PartyShop). The card
            // view is itself a closable popup surface — verdict it too. (The registry walk
            // above is unaffected: it opens panels via PanelRouter.Open directly, so the
            // card-first flow can never read as OPEN_FAILED there.)
            yield return AssertDialogueCardClose();

            FlowTrace.Step("PopupClose",
                $"oracle done: {pass} PASS, {openFailed} OPEN_FAILED, {noClose} NO_CLOSE, {unregistered} NOT_REGISTERED of {ids.Count} PanelIds (+1 dialogue-card row).");
            _lastDetail = $"{pass} pass / {openFailed} open-failed / {noClose} no-close / {unregistered} unregistered (+card)";
        }

        // WO-597 + NPC-card ripple: verdict the structure-dialogue CARD as a closable
        // surface. Opens the market card via the REAL seam (DialogueService.PlayStructure),
        // asserts the shared chrome Close exists on the DialogueView, triggers it, and
        // asserts the dialogue actually ended. The card's own auto-advance to OpenShop ->
        // PartyShop is the EXPECTED card-first flow, NOT a close failure — if a shop panel
        // pops during/after the close it is closed via the arbiter and the row still passes.
        private IEnumerator AssertDialogueCardClose()
        {
            const string RowName = "DialogueCard(market)";
            float tRow = Time.realtimeSinceStartup;
            _pauseDialogueSuppression = true;   // hold off the 1s SuppressDialogue Stop() loop

            bool played = false;
            try { played = DialogueService.PlayStructure("market", "PopupCloseOracle"); }
            catch (Exception ex) { FlowTrace.Warn("PopupClose", "PlayStructure('market') threw: " + ex.Message); }
            yield return Wait(SettleSeconds);

            try
            {
                if (!played || !DialogueService.IsRunning)
                {
                    // Not routable in this scene (or it already auto-completed into the shop —
                    // the card-first flow). Neither is a can't-open ticket: record informational.
                    string note = !played
                        ? "PlayStructure('market') not routable in this scene — skipped"
                        : "card auto-completed before the probe (card-first flow ran through to OpenShop)";
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = RowName, verdict = "NOT_REGISTERED", route = "DialogueService.PlayStructure",
                        seconds = Time.realtimeSinceStartup - tRow, detail = note,
                    });
                    FlowTrace.Step("PopupClose", $"'{RowName}': {note}.");
                    PanelManager.CloseOpen();   // if the flow already opened the shop, release it
                }
                else
                {
                    // Card is up. OWNER CONTRACT 2026-07-08 (F8-22 one-action-one-button +
                    // 'tap to continue'): a linear line shows ONLY the passive tap hint — the
                    // close path IS advancing (the VM auto-closes on the final Advance). The
                    // oracle drives that REAL path; the shared Close only exists in the
                    // degenerate no-options/empty-text state, so demanding a Close button
                    // here was asserting the pre-ruling contract (fleet 4/4 false ticket).
                    UnityEngine.UI.Button uClose = FindUGuiCloseButton();
                    if (uClose != null)
                    {
                        string route = $"uGUI button '{uClose.name}'";
                        bool clicked = false;
                        try { uClose.onClick?.Invoke(); clicked = true; }
                        catch (Exception ex) { FlowTrace.Fail("PopupClose", $"POPUP_NO_CLOSE :: {RowName} — close handler THREW via {route}: {ex.Message}"); }
                        _cardCloseClicked = clicked; _cardCloseRoute = route;
                    }
                    else
                    {
                        var vm = DeNelle.Core.Dialogue.DialogueService.ActiveVm;
                        int taps = 0;
                        for (; taps < 12 && DialogueService.IsRunning; taps++)
                        {
                            try
                            {
                                // A linear line advances; an OPTIONS node needs a choice — the
                                // player picks; the bot picks the LAST option (cards put the
                                // leave/decline exit last by authoring convention).
                                if (vm.ShowingOptions && vm.OptionLabels.Count > 0)
                                    vm.Choose(vm.OptionLabels.Count - 1);
                                else
                                    vm.Advance();
                            }
                            catch (Exception ex)
                            {
                                FlowTrace.Fail("PopupClose", $"POPUP_NO_CLOSE :: {RowName} — Advance/Choose threw on tap {taps + 1}: {ex.Message}");
                                break;
                            }
                        }
                        if (!DialogueService.IsRunning)
                        {
                            _cardCloseClicked = true;
                            _cardCloseRoute = $"tap-advance x{taps} (owner one-action contract — VM auto-closed on final line)";
                            FlowTrace.Step("PopupClose", $"'{RowName}': closed via {_cardCloseRoute}.");
                        }
                        else
                        {
                            FlowTrace.Fail("PopupClose", $"POPUP_NO_CLOSE :: {RowName} — still running after {taps} tap-advances and no Close affordance (options stuck or advance dead).");
                            _popupVerdicts.Add(new PopupCloseResult
                            {
                                panel = RowName, verdict = "NO_CLOSE", route = $"tap-advance x{taps} — card never ended",
                                seconds = Time.realtimeSinceStartup - tRow, detail = "dialogue card unclosable through the real player path",
                            });
                            DialogueService.Stop();   // force-continue
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("PopupClose", $"'{RowName}' probe threw: {ex.Message}");
            }

            // Bounded close-wait (outside the try — coroutines can't yield in a try/catch).
            if (_cardCloseClicked)
            {
                bool ended = false;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < PopupCloseWaitSeconds)
                {
                    if (!DialogueService.IsRunning) { ended = true; break; }
                    yield return null;
                }

                // Card-first flow tolerance: closing/advancing the card may legitimately land
                // in the shop (OpenShop command). An open shop after the card ended is the NEW
                // expected flow, not a close failure — release it and note it.
                bool shopPopped = PanelManager.AnyOpen;
                if (shopPopped) PanelManager.CloseOpen();

                if (ended)
                {
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = RowName, verdict = "PASS", route = _cardCloseRoute,
                        seconds = Time.realtimeSinceStartup - tRow,
                        detail = shopPopped ? "card closed; card-first flow opened the shop (expected) — released via arbiter" : "card closed cleanly",
                    });
                    FlowTrace.Step("PopupClose", $"'{RowName}': PASS — closed via {_cardCloseRoute}{(shopPopped ? " (shop popped per card-first flow, released)" : "")}.");
                }
                else
                {
                    FlowTrace.Fail("PopupClose", $"POPUP_NO_CLOSE :: {RowName} — clicked {_cardCloseRoute} but the dialogue is still running after {PopupCloseWaitSeconds:0}s (stuck card).");
                    _popupVerdicts.Add(new PopupCloseResult
                    {
                        panel = RowName, verdict = "NO_CLOSE", route = _cardCloseRoute,
                        seconds = Time.realtimeSinceStartup - tRow, detail = "card still running after bounded close-wait",
                    });
                    try { DialogueService.Stop(); } catch { }   // force-continue
                }
                _cardCloseClicked = false; _cardCloseRoute = null;
            }

            // Cleanup: never leave a card or shop open for the next phase; resume suppression.
            try { if (DialogueService.IsRunning) DialogueService.Stop(); } catch { }
            PanelManager.CloseOpen();
            _pauseDialogueSuppression = false;
            yield return Wait(SettleSeconds);
        }

        // Scratch state for AssertDialogueCardClose (set inside its try block; the bounded
        // wait must yield OUTSIDE try/catch, so the two halves hand off through these).
        private bool _cardCloseClicked;
        private string _cardCloseRoute;

        // "Actually closed": the modal arbiter record is cleared (this is also what
        // un-suppresses world prompts/input — MobileInteractButton reads AnyOpen) AND
        // the close affordance itself is gone/inactive (guards the inverse of the
        // invisible-scrim class: a panel that clears its record but stays on screen).
        private static bool IsPopupFullyClosed(UnityEngine.UI.Button uClose, UnityEngine.UIElements.Button tkClose)
        {
            if (PanelManager.AnyOpen) return false;
            if (uClose != null && uClose.isActiveAndEnabled) return false;      // Unity-fake-null => destroyed => closed
            if (tkClose != null)
            {
                try
                {
                    // ANCESTOR-AWARE (fleet-9500 PetSkillTree false NO_CLOSE): UITK panels close
                    // by hiding the OVERLAY ancestor — the button's OWN resolvedStyle.display
                    // stays Flex forever, so checking only the button reads every properly
                    // closed panel as "still open". Walk the parent chain: any display:None
                    // ancestor means the affordance is gone from screen.
                    if (tkClose.panel != null && tkClose.enabledInHierarchy)
                    {
                        bool anyHidden = false;
                        for (var ve = (UnityEngine.UIElements.VisualElement)tkClose; ve != null; ve = ve.parent)
                        {
                            if (ve.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
                            {
                                anyHidden = true;
                                break;
                            }
                        }
                        if (!anyHidden) return false;
                    }
                }
                catch { /* detached element mid-teardown => treat as closed */ }
            }
            return true;
        }

        // The shared master-frame Close: ElarionUiKit.ObsidianCloseButton names the ONE
        // standard button "CloseButton" (obsidian black+gold canon — no per-panel X).
        // Prefer that exact name; accept any *close*-named button as a legacy affordance.
        // Among candidates pick the topmost (highest root-canvas sortingOrder) so we hit
        // the OPEN modal's Close, not a HUD leftover underneath.
        private static UnityEngine.UI.Button FindUGuiCloseButton()
        {
            UnityEngine.UI.Button best = null;
            long bestScore = long.MinValue;
            UnityEngine.UI.Button[] all;
            try { all = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>(); }
            catch (Exception ex) { FlowTrace.Warn("PopupClose", "uGUI close scan failed: " + ex.Message); return null; }
            if (all == null) return null;

            foreach (var b in all)
            {
                if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
                if (!b.gameObject.scene.IsValid()) continue;   // skip prefab assets
                string n = b.name ?? "";
                bool exact = string.Equals(n, "CloseButton", StringComparison.OrdinalIgnoreCase);
                if (!exact && n.IndexOf("close", StringComparison.OrdinalIgnoreCase) < 0) continue;

                int sort = 0;
                var cv = b.GetComponentInParent<Canvas>();
                if (cv != null)
                {
                    var rootCv = cv.rootCanvas != null ? cv.rootCanvas : cv;
                    sort = rootCv.sortingOrder;
                }
                long score = (long)sort * 10 + (exact ? 1 : 0);
                if (score > bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        // UITK fallback (code-built UIDocument panels): a visible, enabled Button whose
        // name or label contains "close".
        private static UnityEngine.UIElements.Button FindUiToolkitCloseButton()
        {
            UnityEngine.UIElements.UIDocument[] docs;
            try { docs = UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(); }
            catch (Exception ex) { FlowTrace.Warn("PopupClose", "UITK close scan failed: " + ex.Message); return null; }
            if (docs == null) return null;

            foreach (var d in docs)
            {
                if (d == null || d.rootVisualElement == null) continue;
                // DEV-SURFACE EXCLUSION (fleet-9000 RCA): dev builds carry always-visible
                // UITK overlays (dev panel, admin, help) whose close buttons this scan found
                // FIRST — the oracle clicked 'dev-panel-close' while the panel under test
                // (Workshop) sat untouched, and a foreign 'Close' staying visible flipped
                // PetSkillTree's verdict to NO_CLOSE even though AnyOpen went false. Only
                // the panel under test's own surface counts as a player affordance.
                string host = d.gameObject != null ? d.gameObject.name : "";
                if (host.IndexOf("dev", StringComparison.OrdinalIgnoreCase) >= 0
                    || host.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0
                    || host.IndexOf("help", StringComparison.OrdinalIgnoreCase) >= 0
                    || host.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                List<UnityEngine.UIElements.Button> buttons;
                try { buttons = d.rootVisualElement.Query<UnityEngine.UIElements.Button>().ToList(); }
                catch { continue; }
                foreach (var b in buttons)
                {
                    if (b == null) continue;
                    if (!b.enabledInHierarchy) continue;
                    // ANCESTOR-AWARE (fleet-9500 Workshop RCA): a hidden panel hides its OVERLAY
                    // ancestor, not each child — checking only the button's own display let this
                    // scan return foreign Close buttons from closed panels (the oracle then
                    // clicked another panel's handler and verdicted Workshop NO_CLOSE). Skip any
                    // button with a display:None ancestor.
                    bool hidden = false;
                    for (var ve = (UnityEngine.UIElements.VisualElement)b; ve != null; ve = ve.parent)
                    {
                        if (ve.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None)
                        {
                            hidden = true;
                            break;
                        }
                    }
                    if (hidden) continue;
                    string n = b.name ?? ""; string t = b.text ?? "";
                    if (n.StartsWith("dev-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (n.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0
                        || t.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
                        return b;
                }
            }
            return null;
        }

        // Synthesize a real click on a UITK button (same seam ClickableActuator uses:
        // SendEvent assigns the target, so the Clickable manipulator runs the handlers).
        private static void ClickUiToolkitButton(UnityEngine.UIElements.Button b)
        {
            // FLEET-9700 PROOF the Mouse/ClickEvent path never ran the handler in the built
            // player: with the finder fixed to each panel's OWN close button, all three UITK
            // panels stayed open (AnyOpen=True) after the "click" — Unity 6's Clickable listens
            // to POINTER events (manual Mouse events don't synthesize them), and a pooled
            // ClickEvent without a target dispatches nowhere. NavigationSubmitEvent is the
            // supported programmatic activation — Clickable handles it and invokes clicked
            // synchronously. The legacy events are kept for any non-Clickable listeners.
            using (var submit = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled())
            {
                submit.target = b;
                b.SendEvent(submit);
            }
            Vector2 c = b.worldBound.center;
            using (var down = UnityEngine.UIElements.MouseDownEvent.GetPooled(c, 0, 1, Vector2.zero))
                b.SendEvent(down);
            using (var up = UnityEngine.UIElements.MouseUpEvent.GetPooled(c, 0, 1, Vector2.zero))
                b.SendEvent(up);
        }

        // FORCE-CONTINUE after a violation so one broken panel never costs the rest of
        // the run's coverage. Escalation ladder:
        //   1. PanelManager.CloseOpen() — arbiter force-close (always clears the record).
        //   2. If the panel's UI is STILL on screen, destroy/disable its root outright
        //      (the stuck modal would otherwise cover every later surface).
        //   3. If teardown itself threw, reload the boot scene + re-resolve the hero.
        private IEnumerator ForceContinueAfterStuckPanel(PanelId id,
            UnityEngine.UI.Button uClose, UnityEngine.UIElements.Button tkClose)
        {
            PanelManager.CloseOpen();
            yield return Wait(SettleSeconds);
            if (IsPopupFullyClosed(uClose, tkClose)) yield break;   // recovered — continue the sweep

            bool tornDown = false;
            try
            {
                if (uClose != null && uClose.isActiveAndEnabled)
                {
                    var cv = uClose.GetComponentInParent<Canvas>();
                    var root = cv != null ? (cv.rootCanvas != null ? cv.rootCanvas : cv).gameObject : uClose.gameObject;
                    FlowTrace.Warn("PopupClose", $"'{id}' still visible after arbiter force-close — destroying stuck modal root '{root.name}' to recover coverage.");
                    UnityEngine.Object.Destroy(root);
                    tornDown = true;
                }
                else if (tkClose != null && tkClose.panel != null)
                {
                    // Find the UIDocument hosting the stuck element and disable it.
                    var docs = UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>();
                    if (docs != null)
                        foreach (var d in docs)
                            if (d != null && d.rootVisualElement != null && d.rootVisualElement.panel == tkClose.panel)
                            {
                                FlowTrace.Warn("PopupClose", $"'{id}' still visible after arbiter force-close — disabling stuck UIDocument '{d.name}' to recover coverage.");
                                d.gameObject.SetActive(false);
                                tornDown = true;
                                break;
                            }
                }
                else
                {
                    // No affordance handle to trace a root from (the NO-affordance case) —
                    // the arbiter record is already cleared; nothing more to tear down.
                    tornDown = true;
                }
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("PopupClose", $"'{id}' stuck-panel teardown threw: {ex.Message} — falling back to scene reload.");
            }
            yield return Wait(SettleSeconds);
            if (tornDown) yield break;

            // Last resort: reload the boot scene so the rest of the run keeps coverage.
            FlowTrace.Warn("PopupClose", $"'{id}' unrecoverable in-place — reloading '{TargetScene}' to continue the run.");
            try { SceneManager.LoadScene(TargetScene); }
            catch (Exception ex) { FlowTrace.Fail("PopupClose", $"recovery scene reload threw: {ex.Message}"); yield break; }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < BootTimeout && ActiveScene() != TargetScene) yield return null;
            for (int i = 0; i < 3; i++) yield return null;
            _hero = null;
            float t1 = Time.realtimeSinceStartup;
            while (_hero == null && Time.realtimeSinceStartup - t1 < ResolveHeroTimeout)
            {
                _hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                if (_hero != null) break;
                yield return null;
            }
            FlowTrace.Step("PopupClose", $"recovery reload complete (hero {(_hero != null ? "re-resolved" : "NOT found — later walk phases will skip")}).");
        }

        // =====================================================================
        //  PHASE: TriggerWave
        //  WaveManager.ForceBeginNextWave(), then poll until the phase advances
        //  off its current value (or timeout -> Fail).
        //
        //  CALIBRATION (false-positive fix): the hub (MainCastle_Hall) has NO wave
        //  system, so when there's no WaveManager there is nothing to advance — the
        //  old code warned but the phase still burned its full timeout and the
        //  emitter logged a timeout/hang ticket (false positive, 8/8). We now treat
        //  "no WaveManager in scene" as N/A and return SUCCESS (skipped) immediately.
        //  It is ONLY a real Fail when a WaveManager EXISTS and its Phase refuses to
        //  advance after ForceBeginNextWave within the timeout.
        // =====================================================================
        private IEnumerator TriggerWave()
        {
            // Trigger + poll the CANONICAL singleton (active-scene WaveManager) so we
            // drive and watch the SAME instance — with two WaveManagers live (hub +
            // Village2), a bare Find could trigger one and poll the other (the
            // intermittent "TriggerWave timeout"). Fall back to Find pre-Awake.
            var wm = WaveManager.Instance ?? UnityEngine.Object.FindAnyObjectByType<WaveManager>();
            if (wm == null)
            {
                FlowTrace.Step("Auto", "TriggerWave: no WaveManager in scene — N/A (hub has no wave loop), skipping.");
                _lastDetail = "no WaveManager — N/A (skipped)";
                yield break;
            }

            WavePhase before = wm.Phase;

            // PROBE-FLAKE FIX (overnight cycle 2, RCA-confirmed): an earlier phase's
            // ClickableActuator pass clicks the "Defend!" button, so the wave may ALREADY
            // be running (Countdown/Active) before TriggerWave runs. ForceSpawnNextWaveNow
            // is a deliberate no-op while Active, so the old `Phase != before` poll could
            // never trip and hung 30s — the intermittent 5/12 "TriggerWave timeout". An
            // already-running wave IS success; only a wave stuck at Idle is a real failure.
            if (before != WavePhase.Idle)
            {
                FlowTrace.Step("Auto", $"TriggerWave: wave already running (phase '{before}') — N/A, skipping.");
                _lastDetail = $"already running ({before})";
                yield break;
            }

            FlowTrace.Step("Auto", $"TriggerWave: forcing next wave (phase before='{before}').");
            wm.ForceSpawnNextWaveNow();   // immediate spawn (skip countdown) — only reached from Idle

            float t0 = Time.realtimeSinceStartup;
            bool advanced = false;
            while (Time.realtimeSinceStartup - t0 < WaveTimeout)
            {
                // Success = the wave is now running, regardless of an intermediate Countdown.
                if (wm.Phase == WavePhase.Active || wm.Phase != before) { advanced = true; break; }
                yield return null;
            }

            if (advanced)
                FlowTrace.Step("Auto", $"TriggerWave: phase advanced '{before}'->'{wm.Phase}'.");
            else
                FlowTrace.Fail("Auto", $"TriggerWave: phase did NOT advance from '{before}' within {WaveTimeout:0}s.");
            _lastDetail = $"phase {before}->{wm.Phase}";
        }

        // =====================================================================
        //  PHASE: AttemptExitCastle (LAST)
        //  Walk into the south exit trigger and detect the REAL crossing.
        //
        //  CALIBRATION (false-positive fix): the castle-seam crossing loads the world
        //  ADDITIVELY (SceneTransitionTrigger.loadAdditive=true), so on a SUCCESSFUL
        //  crossing the ACTIVE scene STAYS MainCastle_Hall — the old "did the active
        //  scene change?" test therefore ALWAYS timed out (false positive). The reliable
        //  signal is that the seam WARPS the hero to trigger.targetPosition on Cross().
        //  We poll the hero's distance to that warp landing instead.
        //
        //  SUCCESS = hero actually warped to within 8m of the seam's targetPosition.
        //  FAILURE (real, ticket-worthy) = within the timeout the hero never reached
        //  the gate's ProximityRadius (couldn't path), OR reached it but never warped
        //  (seam didn't fire). The Fail names WHICH, with the closest distance reached.
        // =====================================================================
        private IEnumerator AttemptExitCastle()
        {
            EnsureHero("AttemptExitCastle");   // re-resolve a post-stream hero (RCA 2026-07-08) — unlock overworld coverage
            var gates = UnityEngine.Object.FindObjectsByType<SceneTransitionTrigger>();
            if (gates == null || gates.Length == 0)
            {
                FlowTrace.Warn("Auto", "AttemptExitCastle: no SceneTransitionTrigger to exit through.");
                _lastDetail = "no exit gate";
                yield break;
            }

            // Pick the exit gate NAVMESH-DERIVED, not "south-most" (§12 captured proof
            // from prior fleet runs: global south-most was an outpost portal far from the
            // courtyard on a disjoint navmesh; the hero could never path there. Selection:
            // among gates the hero can actually PATH to on the live navmesh, pick south-most.)
            // Selection: among triggers the hero can actually PATH to on the live navmesh
            // (NavMesh.CalculatePath complete — lift-aware for free, it reads the baked
            // y=liftY courtyard), pick the south-most. Fall back to global south-most
            // (the old behavior, loud-warned) only if nothing is reachable.
            SceneTransitionTrigger exit = null;
            float minZ = float.MaxValue;
            SceneTransitionTrigger exitAny = null;
            float minZAny = float.MaxValue;
            Vector3 heroPos = _hero != null ? _hero.transform.position : Vector3.zero;
            foreach (var g in gates)
            {
                if (g == null) continue;
                // WO-602: skip RETURN triggers — a trigger targeting the CURRENT hub scene is a
                // way BACK IN (the outer "Enter Elarion" entrances), not an exit. Without this the
                // south return trigger (souther than the deck trigger + AI-link-reachable) would
                // win the south-most pick and the phase would test the return, not the exit.
                if (string.Equals(g.targetSceneName, ActiveScene(), StringComparison.Ordinal)) continue;
                float z = g.transform.position.z;
                if (z < minZAny) { minZAny = z; exitAny = g; }
                if (_hero != null && NavReachable(heroPos, g.transform.position) && z < minZ)
                {
                    minZ = z;
                    exit = g;
                }
            }
            if (exit == null && exitAny != null)
            {
                FlowTrace.Warn("Auto", $"AttemptExitCastle: NO trigger is navmesh-reachable from the hero @ {heroPos} — " +
                    $"falling back to global south-most '{exitAny.name}' @ {exitAny.transform.position} (likely a cross-scene stall).");
                exit = exitAny;
            }
            // No exit trigger OR no hero is NOT a ticket-worthy failure — there is
            // simply nothing to attempt (e.g. a scene with no wired seam, or the hero
            // was destroyed/unloaded). End the phase cleanly (ok/skipped), never throw
            // or burn the timeout. Only a hero that REACHED the gate but never warped
            // is a real Fail (handled below).
            if (exit == null || _hero == null)
            {
                FlowTrace.Warn("Auto", "AttemptExitCastle: no exit trigger / hero — skipping");
                _lastDetail = exit == null ? "no exit gate" : "no hero — skipped";
                yield break;
            }

            // SceneTransitionTrigger (DeNelle.Village) exposes these as PUBLIC fields —
            // read them by typed access (no reflection => no FieldInfo NRE). Cache the
            // gate's transform position too: a one-way crossing can unload/destroy the
            // SOURCE seam after the warp, which would Unity-fake-null `exit` and NRE on
            // `exit.transform.position` in the poll loop below.
            string targetScene = exit.targetSceneName;
            Vector3 warpTarget = exit.targetPosition;
            float radius = Mathf.Max(1f, exit.ProximityRadius);
            Vector3 gatePos = exit.transform.position;
            string gateName = exit.name;
            Vector3 heroStart = _hero.transform.position;

            FlowTrace.Step("Auto", $"AttemptExitCastle: walking into '{exit.name}' " +
                $"(target='{targetScene}'@{warpTarget}, radius={radius:0.0}m, heroStart={heroStart}).");

            // Drive toward the seam. The seam is now CONFIRM-TO-CROSS only — it no
            // longer auto-warps on proximity. While the hero is in range the trigger
            // registers a "Travel to <dest>" prompt on the shared MobileInteractButton
            // each frame (LateUpdate). So once we reach proximity we TAP that prompt
            // (MobileInteractButton.InvokeActive) exactly as a real player would; the
            // seam's tap callback then WarpTo's the hero to targetPosition.
            _hero.SetAutoWalk(exit.transform);

            float t0 = Time.realtimeSinceStartup;
            bool warped = false;
            bool reachedProximity = false;
            bool tapped = false;
            float closestToGate = float.MaxValue;   // closest the hero ever got to the gate
            // Walk budget runs 2s SHORTER than the phase watchdog (RunPhase also uses
            // ExitTimeout): with equal budgets the wrapper always killed this coroutine
            // first, so the diagnostic Fail branches below NEVER emitted (fleet 2026-07-02:
            // 4 runs logged only the generic "AttemptExitCastle TIMEOUT", detail="").
            float walkBudget = Mathf.Max(5f, ExitTimeout - 2f);
            while (Time.realtimeSinceStartup - t0 < walkBudget)
            {
                // Hero (or its GameObject) could be destroyed/unloaded mid-walk — bail
                // cleanly rather than NRE on `_hero.transform`.
                if (_hero == null) break;

                Vector3 pos = _hero.transform.position;

                // Did the seam warp us to the landing? (full 3D distance — the warp
                // sets Y too). This is the authoritative crossing signal.
                if (Vector3.Distance(pos, warpTarget) < 8f) { warped = true; break; }

                // Track how close we get to the gate (horizontal — navmesh is planar).
                // Use the CACHED gate position: the source seam may be gone after a
                // crossing, so we must never dereference `exit.transform` here.
                float dGate = HorizontalDistance(pos, gatePos);
                if (dGate < closestToGate) closestToGate = dGate;
                if (dGate <= radius + 0.5f) reachedProximity = true;

                // In range + the seam's "Travel to ..." prompt is up this frame ->
                // TAP it (simulate the on-screen confirm) so the seam crosses. We poll
                // each frame because the trigger (re)registers the prompt in LateUpdate;
                // tapping is harmless to repeat (InvokeActive no-ops once cleared) but
                // we stop driving toward the gate once we've fired the confirm.
                if (reachedProximity && MobileInteractButton.IsActive)
                {
                    if (MobileInteractButton.InvokeActive())
                    {
                        tapped = true;
                        FlowTrace.Step("Auto", $"AttemptExitCastle: bot tapped seam '{gateName}' -> cross (Travel to '{targetScene}').");
                    }
                }

                yield return null;
            }
            if (_hero != null) _hero.ClearAutoWalk();

            if (warped)
            {
                float finalDist = _hero != null ? Vector3.Distance(_hero.transform.position, warpTarget) : 0f;
                FlowTrace.Step("Auto", $"AttemptExitCastle: CROSSED — hero warped to '{targetScene}' target {warpTarget} (now {finalDist:0.0}m from landing).");
                _lastDetail = $"crossed to {targetScene} (warped to target)";
                _exitCrossed = true;   // WO-602: arms the HomeReturnRoundTrip phase
            }
            else if (!reachedProximity)
            {
                // Real, ticket-worthy: the hero could not path to the seam at all.
                FlowTrace.Fail("Auto", $"AttemptExitCastle: hero could not path to the gate '{gateName}' — " +
                    $"closest {closestToGate:0.0}m of radius {radius:0.0}m within {ExitTimeout:0}s (navmesh edge / blocked).");
                _lastDetail = $"could not reach gate (closest {closestToGate:0.0}m / radius {radius:0.0}m)";
            }
            else if (!tapped)
            {
                // Real, ticket-worthy: reached proximity but the "Travel to ..." prompt
                // never went active, so the bot had nothing to tap (seam didn't register
                // its confirm prompt).
                FlowTrace.Fail("Auto", $"AttemptExitCastle: no seam prompt to tap — hero reached closest {closestToGate:0.0}m of gate '{gateName}' " +
                    $"(radius {radius:0.0}m) but the 'Travel to {targetScene}' confirm prompt never went active within {ExitTimeout:0}s.");
                _lastDetail = $"no seam prompt (reached {closestToGate:0.0}m / radius {radius:0.0}m, nothing to tap)";
            }
            else
            {
                // Real, ticket-worthy: tapped the confirm prompt but the seam never
                // warped us (the tap callback failed to cross).
                FlowTrace.Fail("Auto", $"AttemptExitCastle: tapped seam but it did NOT cross — hero reached closest {closestToGate:0.0}m of gate '{gateName}' " +
                    $"(radius {radius:0.0}m), tapped the 'Travel to {targetScene}' prompt, but no warp to target {warpTarget} within {ExitTimeout:0}s.");
                _lastDetail = $"tapped but no cross (reached {closestToGate:0.0}m / radius {radius:0.0}m, no warp)";
            }
        }

        // =====================================================================
        //  PHASE: HomeReturnRoundTrip (WO-602 — runs right after AttemptExitCastle)
        //  The owner shipped "no way back into town" because the fleet only ever
        //  tested the EXIT. This closes the loop: after a successful exit, walk
        //  OUTWARD ~20m from the landing, then navigate back to the nearest gate's
        //  OUTER return entrance (the RuntimeSeam_ReturnTrigger_* built by
        //  RuntimeRegionGate.BuildReturnEntrance) and assert the hero is back on
        //  the courtyard: |y - castle.liftY| <= 0.5 AND horizontal r < 44 (inside
        //  the plinth footprint, CastleHubBuilder.PlinthHalf). Two return paths can
        //  satisfy it: the passive HeroLinkCrossing dest warp (widened lane) fires
        //  on the walk-in, or the bot taps the visible "Enter Elarion" prompt —
        //  exactly what a player would do. On failure: FlowTrace.Fail("AutoTest",
        //  "HOME_RETURN_FAIL :: ...") and force-continue. Per-run verdict row goes
        //  into autopilot-summary.json (homeReturn[]), like the popupClose rows.
        //
        //  ORACLE WIDENING (2026-07-02 — fleet ran 1 PASS + 5 SKIPPED): the phase
        //  no longer skips when the exit phase didn't latch _exitCrossed (chaos
        //  wander / bot warped to Outpost1 / seam stall). It SELF-ARMS instead:
        //  WarpTo the courtyard, run the exit leg itself (same confirm-to-cross
        //  sequence AttemptExitCastle drives), then the round trip — so every run
        //  produces an ATTEMPTED PASS/FAIL verdict. SKIPPED remains only for the
        //  genuinely impossible state: no hero.
        // =====================================================================
        // Owner 2026-07-03 "I walk ON TOP of the bridge": one screenshot per run taken while
        // the hero is physically ON the south bridge span — the feet-on-stone visual proof
        // (renders graphics-on; blank under -nographics). Box = south span: x≈-4.4±4,
        // z between the plinth face (-53) and the outer end (~-76).
        private bool _bridgeShotTaken;
        private void CaptureBridgeCrossing(Vector3 pos)
        {
            if (_bridgeShotTaken) return;
            // Box includes the bridge MOUTH (z from -50): the crossing warp fires at the
            // threshold, so the hero's deepest on-foot point is deck-meets-plinth — exactly
            // the junction the feet-on-stone proof needs (windowed run 10100: crossing at
            // t≈9s, the deeper-span legs never ran before the 300s cap).
            if (pos.z > -50f || pos.z < -76f || Mathf.Abs(pos.x + 4.4f) > 5f) return;
            _bridgeShotTaken = true;
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "ui-shots");
                System.IO.Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "bridge_crossing.png"));
                FlowTrace.Step("Auto", $"bridge-crossing shot captured at {pos} (feet-on-stone visual proof).");
            }
            catch { /* capture is best-effort */ }
        }

        // =====================================================================
        //  PHASE: CaptureDockOverlays
        //  UI-fidelity capture for the DOCK / non-PanelRouter overlays that
        //  OpenEachHUDPanel misses. Those screens are opened by their own
        //  singleton Toggle()/Open()/ToggleOverlay() (the HUD kit dock buttons),
        //  NOT via PanelRouter.Open, so the registry-driven panel sweep never
        //  shoots them. For each: resolve the MonoBehaviour singleton, open it,
        //  wait a frame so the modal builds + renders, CaptureUiPanel("<Name>")
        //  (writes panel_<Name>.png, graphics-on), then close via the shared
        //  PanelManager arbiter (all four register a PanelHandle). Fully guarded —
        //  a surface that fails to resolve/open logs FlowTrace.Warn and the pass
        //  continues to the next; one missing dock never aborts the run.
        // =====================================================================
        private IEnumerator CaptureDockOverlays()
        {
            int shot = 0, tried = 0;

            // ClanChat — DeNelle.HUD.ClanChatPanel, opened by the dock via Toggle().
            tried++;
            var clan = UnityEngine.Object.FindAnyObjectByType<DeNelle.HUD.ClanChatPanel>();
            if (clan == null)
                FlowTrace.Warn("Auto", "CaptureDockOverlays: no ClanChatPanel in scene — skipping ClanChat shot.");
            else
            {
                bool opened = false;
                try { clan.Toggle(); opened = true; }
                catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: ClanChat Toggle threw " + ex.Message); }
                if (opened)
                {
                    yield return null;                    // let the modal build + render this frame
                    CaptureUiPanel("ClanChat");           // -> panel_ClanChat.png
                    yield return null;                    // flush ScreenCapture with the panel still up
                    shot++;
                    FlowTrace.Step("Auto", "CaptureDockOverlays: captured panel_ClanChat.png.");
                    try { PanelManager.CloseOpen(); } catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: ClanChat close threw " + ex.Message); }
                    yield return Wait(SettleSeconds);
                }
            }

            // Leaderboard — DeNelle.HUD.LeaderboardPanel, opened via Toggle().
            tried++;
            var lb = UnityEngine.Object.FindAnyObjectByType<DeNelle.HUD.LeaderboardPanel>();
            if (lb == null)
                FlowTrace.Warn("Auto", "CaptureDockOverlays: no LeaderboardPanel in scene — skipping Leaderboard shot.");
            else
            {
                bool opened = false;
                try { lb.Toggle(); opened = true; }
                catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: Leaderboard Toggle threw " + ex.Message); }
                if (opened)
                {
                    yield return null;
                    CaptureUiPanel("Leaderboard");        // -> panel_Leaderboard.png
                    yield return null;
                    shot++;
                    FlowTrace.Step("Auto", "CaptureDockOverlays: captured panel_Leaderboard.png.");
                    try { PanelManager.CloseOpen(); } catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: Leaderboard close threw " + ex.Message); }
                    yield return Wait(SettleSeconds);
                }
            }

            // Jukebox — DeNelle.Audio.MusicSelectionPanel, opened via Open().
            tried++;
            var juke = UnityEngine.Object.FindAnyObjectByType<DeNelle.Audio.MusicSelectionPanel>();
            if (juke == null)
                FlowTrace.Warn("Auto", "CaptureDockOverlays: no MusicSelectionPanel in scene — skipping Jukebox shot.");
            else
            {
                bool opened = false;
                try { juke.Open(); opened = true; }
                catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: Jukebox Open threw " + ex.Message); }
                if (opened)
                {
                    yield return null;
                    CaptureUiPanel("Jukebox");            // -> panel_Jukebox.png
                    yield return null;
                    shot++;
                    FlowTrace.Step("Auto", "CaptureDockOverlays: captured panel_Jukebox.png.");
                    try { PanelManager.CloseOpen(); } catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: Jukebox close threw " + ex.Message); }
                    yield return Wait(SettleSeconds);
                }
            }

            // HelpMenu — DeNelle.HUD.HelpMenu, exposes a static Instance + ToggleOverlay()/Close().
            tried++;
            var help = DeNelle.HUD.HelpMenu.Instance ?? UnityEngine.Object.FindAnyObjectByType<DeNelle.HUD.HelpMenu>();
            if (help == null)
                FlowTrace.Warn("Auto", "CaptureDockOverlays: no HelpMenu in scene — skipping HelpMenu shot.");
            else
            {
                bool opened = false;
                try { help.ToggleOverlay(); opened = true; }
                catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: HelpMenu ToggleOverlay threw " + ex.Message); }
                if (opened)
                {
                    yield return null;
                    CaptureUiPanel("HelpMenu");           // -> panel_HelpMenu.png
                    yield return null;
                    shot++;
                    FlowTrace.Step("Auto", "CaptureDockOverlays: captured panel_HelpMenu.png.");
                    try { help.Close(); } catch (Exception ex) { FlowTrace.Warn("Auto", "CaptureDockOverlays: HelpMenu Close threw " + ex.Message); }
                    yield return Wait(SettleSeconds);
                }
            }

            _lastDetail = $"{shot}/{tried} dock overlays captured";
            FlowTrace.Step("Auto", $"CaptureDockOverlays: {shot}/{tried} dock overlays captured (ClanChat/Leaderboard/Jukebox/HelpMenu).");
        }

        // =====================================================================
        //  PHASE: CaptureExtraPanels
        //  UI-fidelity shots for the gameplay-scene panels the PanelRouter sweep
        //  (OpenEachHUDPanel) can NOT reach because they are not registered with
        //  PanelRouter — they open via their own singleton / static Open()/Show()/
        //  Pause() entrypoints (some need a stub VM). Mirrors CaptureDockOverlays:
        //  force-open each, wait a couple frames, CaptureUiPanel("<Screen>") using
        //  the SAME token the assembler expects (UI_REVIEW/_mapping.json deliveredShot
        //  = panel_<Screen>.png), then close. Every open is GUARDED so one failing
        //  panel can never abort the phase; captures render only graphics-on.
        //  Runs BEFORE TriggerWave so no battle-lock rejects a PanelManager open.
        //  FRONT-END screens (14 HeroSelect, 15 Dialogue) are ALSO captured here: both
        //  render without their front-end scene — HeroSelect builds its whole screen in
        //  code on OnEnable (activated with the returning-player skip defeated so it
        //  does not GoCastle), Dialogue plays a real authored conversational node through
        //  the DialogueService runner (suppression paused across the shot).
        // =====================================================================
        private int _extraShotCount;

        private IEnumerator CaptureExtraPanels()
        {
            _extraShotCount = 0;

            // ── 16 Build Menu — DeNelle.Village.BuildMenu (instance Open/Close; lazy build) ──
            yield return CaptureComponentPanel<BuildMenu>("BuildMenu", m => m.Open(), m => m.Close());

            // ── 20 Settings — DeNelle.Settings.SettingsController (instance Open/Close) ──
            yield return CaptureComponentPanel<DeNelle.Settings.SettingsController>("Settings", m => m.Open(), m => m.Close());

            // ── 21 Pause — DeNelle.Settings.PauseController (Pause zeroes timeScale; Resume restores).
            //    Wait() is WaitForSecondsRealtime so a frozen timeScale never hangs the capture. ──
            yield return CaptureComponentPanel<DeNelle.Settings.PauseController>("Pause", m => m.Pause(), m => m.Resume());

            // ── 22 Tower Manager — DeNelle.Village.UI.TowerManagerPanel (self-installed singleton) ──
            yield return CaptureComponentPanel<DeNelle.Village.UI.TowerManagerPanel>("TowerManager", m => m.Show(), m => m.Hide());

            // ── 30 Troop Training — DeNelle.Village.Hero.TroopTrainingPanel (instance Open/Close) ──
            yield return CaptureComponentPanel<TroopTrainingPanel>("TroopTraining", m => m.Open(), m => m.Close());

            // ── 32 Inventory (bag) — DeNelle.Village.HeroInventoryController (singleton Open/Close) ──
            yield return CaptureComponentPanel<HeroInventoryController>("Inventory", m => m.Open(), m => m.Close());

            // ── 12 Equipment (Gear Preview) — DeNelle.Village.Hero.EquipmentPanel (instance Open;
            //    Close() is PRIVATE, OnDestroy owns teardown → drive on a THROWAWAY host we destroy).
            //    NOT reached by OpenEachHUDPanel: no EquipmentPanel is instantiated in the gameplay
            //    scene, so PanelRouter never has it registered and the PanelRouter sweep skips it —
            //    the stale corrupt panel_EquipmentPanel.png persists. We force-open it here.
            //    It hosts a LIVE 3D preview (disabled camera → RenderTexture → RawImage, rendered
            //    manually on Open), so use the SETTLED capture path with extra settle frames + an
            //    end-of-frame grab (a plain yield grabs mid-composite RGB static — the ~5MB garbage
            //    PNG). Fully guarded so it can never abort the drive; graphics-gated inside
            //    CaptureUiPanelSettled (headless writes a blank frame, never hangs). ──
            {
                GameObject eqHost = null;
                bool eqOpened = Guard.Try("Auto", "CaptureExtraPanels open EquipmentPanel", () =>
                {
                    eqHost = new GameObject("Capture_EquipmentPanel");
                    var ep = eqHost.AddComponent<EquipmentPanel>();   // Awake registers PanelRouter/PanelManager
                    ep.Open();
                });
                if (eqOpened && eqHost != null)
                {
                    yield return Wait(SettleSeconds);                        // let Open build + render the preview RT
                    yield return CaptureUiPanelSettled("EquipmentPanel", extraSettleFrames: 5);   // -> panel_EquipmentPanel.png
                    _extraShotCount++;
                    FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_EquipmentPanel.png.");
                }
                else
                    FlowTrace.Warn("Auto", "CaptureExtraPanels: EquipmentPanel open threw — skipped panel_EquipmentPanel.png.");
                // OnDestroy tears down the modal + unregisters (Close is private) — destroy the host.
                Guard.Try("Auto", "CaptureExtraPanels destroy EquipmentPanel", () =>
                {
                    if (eqHost != null) UnityEngine.Object.Destroy(eqHost);
                });
                yield return Wait(SettleSeconds);
            }

            // ── 31 Merchant Shop — REMOVED 2026-09-06 (WO-1430). This shot drove the legacy
            //    DeNelle.Village.Hero.ShopPanel on a throwaway host; that panel is DELETED (it had no
            //    door - this bot and UICaptureLaunch were its only constructors). The merchant screen
            //    is NOT missing from the review set: PanelId.PartyShop is registered by
            //    PartyShopPanelMvvm, so the OpenEachHUDPanel sweep already shoots panel_PartyShop.png.
            //    The UI_REVIEW row keyed "ShopPanel" is therefore a duplicate view of one screen and is
            //    named in UiCaptureCoverageRegression.KnownUncapturable with that reason - the same
            //    shape as the retired "HeroTalents" row.

            // ── 28 Raid Selection — DeNelle.Village.Hero.RaidSelectionScreen (static self-heal Open) ──
            {
                bool opened = Guard.Try("Auto", "CaptureExtraPanels open RaidSelection", () => RaidSelectionScreen.Open());
                if (opened)
                {
                    yield return null;
                    CaptureUiPanel("RaidSelection");           // -> panel_RaidSelection.png
                    yield return null;
                    _extraShotCount++;
                    FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_RaidSelection.png.");
                    var rs = UnityEngine.Object.FindAnyObjectByType<RaidSelectionScreen>();
                    Guard.Try("Auto", "CaptureExtraPanels close RaidSelection", () => { if (rs != null) rs.Close(); });
                    yield return Wait(SettleSeconds);
                }
                else FlowTrace.Warn("Auto", "CaptureExtraPanels: RaidSelection open threw — skipped.");
            }

            // ── 29 Raid Deploy — DeNelle.Village.Hero.RaidDeployScreen (static Open(SceneConfigDef)).
            //    Needs a target def: prefer a real catalog entry; fall back to a minimal stub so the
            //    chrome still renders (Open(null) is a designed no-op). ──
            {
                SceneConfigDef def = null;
                Guard.Try("Auto", "CaptureExtraPanels resolve raid def", () =>
                {
                    def = SceneConfigCatalog.Find("fortified_garrison");
                    if (def == null)
                    {
                        var all = SceneConfigCatalog.All;
                        if (all != null && all.Count > 0) def = all[0];
                    }
                });
                if (def == null)
                    def = new SceneConfigDef { id = "capture_raid", displayName = "Raid", difficulty = "Regular" };

                bool opened = Guard.Try("Auto", "CaptureExtraPanels open RaidDeploy", () => RaidDeployScreen.Open(def));
                if (opened)
                {
                    yield return null;
                    CaptureUiPanel("RaidDeploy");              // -> panel_RaidDeploy.png
                    yield return null;
                    _extraShotCount++;
                    FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_RaidDeploy.png.");
                    var rd = UnityEngine.Object.FindAnyObjectByType<RaidDeployScreen>();
                    Guard.Try("Auto", "CaptureExtraPanels close RaidDeploy", () => { if (rd != null) rd.Close(); });
                    yield return Wait(SettleSeconds);
                }
                else FlowTrace.Warn("Auto", "CaptureExtraPanels: RaidDeploy open threw — skipped.");
            }

            // ── 23 End State — DeNelle.Village.UI.EndStateView.Show(vm). Build a stub victory VM;
            //    Destroy(view.gameObject) is the intended teardown (OnDestroy clears the posture flag). ──
            {
                DeNelle.Village.UI.EndStateView view = null;
                bool shown = Guard.Try("Auto", "CaptureExtraPanels show EndState", () =>
                {
                    var vm = DeNelle.Village.UI.EndStateVM.FromBattleVictory(
                        stars: 3, durationSeconds: 42f, xp: 120, wisdom: 30, wood: 15, iron: 8,
                        gearName: null, onContinue: null, autoTimeoutSeconds: 999f, perfect: false);
                    view = DeNelle.Village.UI.EndStateView.Show(vm);
                });
                if (shown && view != null)
                {
                    // EndStateView reveals body content + the primary button via a STAGGERED
                    // reveal tween (each element starts alpha=0, fades in only after its Delay;
                    // the button lands last at ~0.53s delay + 0.20s fade). A single-frame grab
                    // catches everything still at alpha 0 -> empty body + no button (the SME F8).
                    // Wait out the reveal (unscaled — plays through timeScale=0) before the shot.
                    yield return Wait(1.0f);
                    CaptureUiPanel("EndState");                // -> panel_EndState.png
                    yield return null;
                    _extraShotCount++;
                    FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_EndState.png.");
                    Guard.Try("Auto", "CaptureExtraPanels close EndState", () =>
                    {
                        if (view != null) UnityEngine.Object.Destroy(view.gameObject);
                    });
                    yield return Wait(SettleSeconds);
                }
                else FlowTrace.Warn("Auto", "CaptureExtraPanels: EndState show returned null — skipped.");
            }

            // ── 26 Echo Workforce (Harvest) — DeNelle.Village.EchoWorkforceHud. Show/Hide are private;
            //    it toggles off the Core HarvestPanelGate event. Only drive an EXISTING (already-Started,
            //    subscribed) instance — a freshly created one hasn't subscribed yet this frame. ──
            {
                var echo = UnityEngine.Object.FindAnyObjectByType<EchoWorkforceHud>();
                if (echo == null)
                    FlowTrace.Warn("Auto", "CaptureExtraPanels: no EchoWorkforceHud in scene — skipping EchoWorkforce shot.");
                else
                {
                    bool toggled = Guard.Try("Auto", "CaptureExtraPanels open EchoWorkforce", () => HarvestPanelGate.RequestToggle());
                    if (toggled)
                    {
                        yield return null;
                        CaptureUiPanel("EchoWorkforce");        // -> panel_EchoWorkforce.png
                        yield return null;
                        _extraShotCount++;
                        FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_EchoWorkforce.png.");
                        Guard.Try("Auto", "CaptureExtraPanels close EchoWorkforce", () => HarvestPanelGate.RequestToggle());
                        yield return Wait(SettleSeconds);
                    }
                }
            }

            // ── 18 Bug Report — DeNelle.HUD.BugReportView.Open() (static self-heal). Awake freezes
            //    timeScale + builds the form in a coroutine, so give it a beat before the shot; Close()
            //    is public and restores timeScale on teardown. Reachable in the gameplay scene (HUD asm). ──
            {
                bool wasOpen = UnityEngine.Object.FindAnyObjectByType<DeNelle.HUD.BugReportView>() != null;
                bool opened = Guard.Try("Auto", "CaptureExtraPanels open BugReport", () => DeNelle.HUD.BugReportView.Open());
                if (opened && !wasOpen)
                {
                    yield return Wait(0.5f);                    // let OpenRoutine build the form
                    CaptureUiPanel("BugReport");                // -> panel_BugReport.png
                    yield return null;
                    _extraShotCount++;
                    FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_BugReport.png.");
                    var br = UnityEngine.Object.FindAnyObjectByType<DeNelle.HUD.BugReportView>();
                    Guard.Try("Auto", "CaptureExtraPanels close BugReport", () => { if (br != null) br.Close(); });
                    yield return Wait(SettleSeconds);
                }
                else if (wasOpen)
                    FlowTrace.Warn("Auto", "CaptureExtraPanels: a BugReportView was already open — skipping to avoid disturbing it.");
                else
                    FlowTrace.Warn("Auto", "CaptureExtraPanels: BugReport open threw — skipped.");
            }

            // ── 14 Hero Select — DeNelle.Onboarding.HeroSelectController. It builds its WHOLE
            //    screen in code on OnEnable (kit uGUI, NO UXML / PanelSettings), so it renders in
            //    the gameplay scene WITHOUT loading the front-end scene. Two hazards handled:
            //    (a) OnEnable ROUTES to the Castle (GoCastle -> scene reload, FATAL to this run) when
            //        a hero is already chosen — always true in gameplay. We create the host INACTIVE
            //        (so OnEnable does not fire on AddComponent), flip its private
            //        _skipWhenIntroComplete = false, THEN activate so OnEnable BUILDS, not routes. If
            //        the field can't be flipped we DO NOT activate (never risk the fatal reload).
            //    (b) OnDisable destroys the panel's own canvas, so Destroy(host) is the clean teardown.
            {
                GameObject host = null;
                bool armed = Guard.Try("Auto", "CaptureExtraPanels arm HeroSelect", () =>
                {
                    host = new GameObject("Capture_HeroSelect");
                    host.SetActive(false);   // hold OnEnable until the skip is defeated
                    var hs = host.AddComponent<DeNelle.Onboarding.HeroSelectController>();
                    var f = typeof(DeNelle.Onboarding.HeroSelectController).GetField(
                        "_skipWhenIntroComplete",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (f == null)
                        throw new Exception("_skipWhenIntroComplete not found — refusing to activate (would GoCastle -> reload).");
                    f.SetValue(hs, false);
                    host.SetActive(true);    // NOW OnEnable builds the screen (no route)
                });
                if (armed && host != null)
                {
                    yield return Wait(SettleSeconds);              // let BuildScreen paint
                    yield return CaptureUiPanelSettled("HeroSelect");   // -> panel_HeroSelect.png
                    _extraShotCount++;
                    FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_HeroSelect.png.");
                    Guard.Try("Auto", "CaptureExtraPanels destroy HeroSelect", () =>
                    {
                        if (host != null) UnityEngine.Object.Destroy(host);
                    });
                    yield return Wait(SettleSeconds);
                }
                else
                {
                    FlowTrace.Warn("Auto", "CaptureExtraPanels: HeroSelect not armed (create/reflection failed) — skipped panel_HeroSelect.png.");
                    Guard.Try("Auto", "CaptureExtraPanels cleanup HeroSelect", () =>
                    {
                        if (host != null) UnityEngine.Object.Destroy(host);   // inactive host — safe teardown
                    });
                }
            }

            // ── 15 Dialogue — DeNelle.HUD.DialogueView renders from the custom (Yarn-free)
            //    DialogueService runner + dialogues.json. Play a REAL authored CONVERSATIONAL node
            //    ("brom_intro" — a greeting line + options, NOT a transaction that auto-routes to a
            //    panel) so the bottom dialogue strip renders, capture it, then Stop(). The 1s
            //    SuppressDialogue loop is PAUSED across the shot (else it would Stop() the card
            //    mid-capture, exactly as AssertDialogueCardClose does). Guarded — an unauthored id
            //    or a missing view logs + skips, never a faked shot. ──
            {
                _pauseDialogueSuppression = true;   // hold off SuppressDialogue's 1s Stop() loop

                // HUD-SUPPRESS FOR THE SHOT (PM review 2026-07-04): the dialogue strip is an
                // INLINE bottom panel (withBackdrop:false), so a live-gameplay capture composites
                // the whole gameplay HUD over/around it — the yellow nav-ring (moveCluster), the
                // level bars, resource chips and quest banner bled into the portrait slot and
                // occluded the option row. Fade the whole-HUD CanvasGroup (VillageHudController
                // .SetHudVisible → HudAreasHost.Group) and hide the mobile joystick so the shot
                // shows ONLY the dialogue strip on a clean background, then restore both.
                var hudForShot = UnityEngine.Object.FindAnyObjectByType<DeNelle.HUD.VillageHudController>();
                GameObject joyCanvasForShot = null;
                Guard.Try("Auto", "CaptureExtraPanels suppress HUD for Dialogue", () =>
                {
                    if (hudForShot != null) hudForShot.SetHudVisible(false);
                    var joy = DeNelle.Village.VirtualJoystick.Instance;
                    if (joy != null)
                    {
                        var jc = joy.transform.Find("JoystickCanvas");
                        if (jc != null && jc.gameObject.activeSelf)
                        {
                            joyCanvasForShot = jc.gameObject;
                            joyCanvasForShot.SetActive(false);
                        }
                    }
                });

                bool played = Guard.Try("Auto", "CaptureExtraPanels play Dialogue",
                    () => DialogueService.Play("brom_intro"));
                if (played && DialogueService.IsRunning)
                {
                    yield return Wait(SettleSeconds);
                    yield return CaptureUiPanelSettled("Dialogue");   // -> panel_Dialogue.png
                    _extraShotCount++;
                    FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_Dialogue.png (HUD suppressed).");
                    Guard.Try("Auto", "CaptureExtraPanels stop Dialogue", () => DialogueService.Stop());
                    yield return Wait(SettleSeconds);
                }
                else
                    FlowTrace.Warn("Auto", "CaptureExtraPanels: brom_intro not routable (unauthored or already running) — skipped panel_Dialogue.png.");

                // Restore the gameplay HUD + joystick (never leave the felt-test HUD hidden).
                Guard.Try("Auto", "CaptureExtraPanels restore HUD after Dialogue", () =>
                {
                    if (hudForShot != null) hudForShot.SetHudVisible(true);
                    if (joyCanvasForShot != null) joyCanvasForShot.SetActive(true);
                });

                _pauseDialogueSuppression = false;  // resume dialogue suppression
            }

            _lastDetail = $"{_extraShotCount} extra panels captured";
            FlowTrace.Step("Auto", $"CaptureExtraPanels: {_extraShotCount} gameplay-scene panels captured " +
                "(BuildMenu/Settings/Pause/TowerManager/TroopTraining/Inventory/RaidSelection/RaidDeploy/EndState/EchoWorkforce/BugReport/HeroSelect/Dialogue).");
        }

        // Find-or-create a component of type T, GUARD-open it, screenshot panel_<shotName>.png,
        // then GUARD-close it. Never destroys the host (some of these register with PanelManager;
        // a destroyed-but-registered owner would fault a later CloseOpen) — a hidden dormant panel
        // is harmless for the rest of the run. One failing panel logs + continues.
        private IEnumerator CaptureComponentPanel<T>(string shotName, Action<T> openFn, Action<T> closeFn)
            where T : MonoBehaviour
        {
            T inst = UnityEngine.Object.FindAnyObjectByType<T>();
            if (inst == null)
            {
                T made = null;
                Guard.Try("Auto", "CaptureExtraPanels create " + typeof(T).Name, () =>
                {
                    made = new GameObject("Capture_" + typeof(T).Name).AddComponent<T>();
                });
                inst = made;
            }
            if (inst == null)
            {
                FlowTrace.Warn("Auto", "CaptureExtraPanels: no " + typeof(T).Name + " (create failed) — skipping " + shotName + ".");
                yield break;
            }

            bool opened = Guard.Try("Auto", "CaptureExtraPanels open " + shotName, () => openFn(inst));
            if (!opened)
            {
                FlowTrace.Warn("Auto", "CaptureExtraPanels: open threw for " + shotName + " — skipped.");
                yield break;
            }

            // MID-FADE GUARD (2026-08-04). This used to be a bare `yield return null` +
            // CaptureUiPanel: ONE frame after the open call. Every Obsidian panel opens
            // through PanelOpenCloseFx, which animates alpha 0->1 and scale 0.92->1 over
            // ~0.2s, so a next-frame grab caught the panel at partial alpha with the world
            // and the gameplay HUD showing straight through it. panel_TroopTraining.png in
            // the 2026-08-04 capture is the proof: a ghost-transparent Barracks modal over
            // a live town. That is not reviewable -- you cannot judge contrast, plate fill
            // or text legibility through a half-faded panel. Let the FX finish, THEN use the
            // settled writer (end-of-frame, post-render) the router sweep already uses.
            yield return Wait(0.4f);
            yield return CaptureUiPanelSettled(shotName, extraSettleFrames: 2);
            _extraShotCount++;
            FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_" + shotName + ".png.");

            Guard.Try("Auto", "CaptureExtraPanels close " + shotName, () => closeFn(inst));
            yield return Wait(SettleSeconds);
        }

        // Create a THROWAWAY host carrying T, GUARD-open it, screenshot, then DESTROY the host
        // (its OnDestroy tears down the modal). For panels with no public Close whose OnDestroy
        // owns cleanup — never touches a real in-scene instance. (Its last caller, the legacy
        // ShopPanel merchant shot, was removed with that panel in WO-1430; the helper is kept
        // because it is the general recipe for any Close-less panel and UiCaptureCoverageRegression
        // still recognises CaptureThrowawayPanel calls as a coverage route.)
        private IEnumerator CaptureThrowawayPanel<T>(string shotName, Action<T> openFn)
            where T : MonoBehaviour
        {
            T inst = null;
            bool made = Guard.Try("Auto", "CaptureExtraPanels create " + typeof(T).Name, () =>
            {
                inst = new GameObject("Capture_" + typeof(T).Name).AddComponent<T>();
            });
            if (!made || inst == null)
            {
                FlowTrace.Warn("Auto", "CaptureExtraPanels: could not create " + typeof(T).Name + " — skipping " + shotName + ".");
                yield break;
            }

            bool opened = Guard.Try("Auto", "CaptureExtraPanels open " + shotName, () => openFn(inst));
            if (opened)
            {
                // Same mid-fade guard as CaptureComponentPanel above (see its banner).
                yield return Wait(0.4f);
                yield return CaptureUiPanelSettled(shotName, extraSettleFrames: 2);
                _extraShotCount++;
                FlowTrace.Step("Auto", "CaptureExtraPanels: captured panel_" + shotName + ".png.");
            }
            else FlowTrace.Warn("Auto", "CaptureExtraPanels: open threw for " + shotName + " — skipped.");

            Guard.Try("Auto", "CaptureExtraPanels destroy " + typeof(T).Name, () =>
            {
                if (inst != null) UnityEngine.Object.Destroy(inst.gameObject);
            });
            yield return Wait(SettleSeconds);
        }

        // =====================================================================
        //  PHASE: CaptureMoatRing
        //  A castle-facing MOAT-RING beauty angle (owner "you walk ON TOP of the
        //  bridge" world, the same ui-shots review folder). The moat is a water
        //  annulus of radius ~44..62 around origin (CastleMoatBuilder: MoatInner
        //  44, MoatOuter 62); we frame the whole castle + moat from an ELEVATED
        //  OBLIQUE angle. To avoid fighting the gameplay follow-camera (which
        //  re-seats the main camera every LateUpdate), we spawn a SHORT-LIVED
        //  capture camera at a HIGHER depth so it composites on top, shoot, then
        //  destroy it. Renders only graphics-on (blank under -nographics); fully
        //  guarded so a capture failure logs + continues. -> moat_ring.png
        // =====================================================================
        private IEnumerator CaptureMoatRing()
        {
            const float MoatOuterRadius  = 62f;   // = CastleMoatBuilder.MoatOuterRadius (band 44..62)
            const float MoatCentreRadius = 53f;   // = CastleMoatBuilder.MoatCentreRadius (band centreline)

            int shot = 0;

            // ---- OVERVIEW: whole castle + moat from an elevated south-west oblique -------
            // Distance chosen so the ~124m-wide ring sits inside a 55-deg FOV with headroom.
            Vector3 overviewEye = new Vector3(MoatOuterRadius * 1.5f, MoatOuterRadius * 1.15f, -(MoatOuterRadius * 1.9f));
            yield return CaptureFramedShot(overviewEye, new Vector3(0f, 3f, 0f), 55f, "moat_ring.png");
            shot++;

            // ---- PER-SEAM: one framed shot per cardinal bridge, from OUTSIDE looking down the
            // crossing toward the castle (the "does the seam read as natural" review pairs). Each
            // cardinal crossing is the south stone bridge yaw-rotated about origin (South 0 / West
            // 90 / North 180 / East 270). Prefer the real RuntimeSeam_Bridge_<label> object's
            // renderer-bounds centre; fall back to the cardinal radial at the moat centreline.
            var cardinals = new (string label, float yaw, string suffix)[]
            {
                ("North", 180f, "N"), ("East", 270f, "E"), ("South", 0f, "S"), ("West", 90f, "W"),
            };
            foreach (var (label, yaw, suffix) in cardinals)
            {
                // Outward radial for this side = the south -Z direction rotated by the side's yaw.
                Vector3 outward = (Quaternion.Euler(0f, yaw, 0f) * Vector3.back).normalized;

                // Bridge centre: the real object if present (bounds centre), else the radial.
                Vector3 centre = outward * MoatCentreRadius; centre.y = 1f;
                var bridgeGo = GameObject.Find("RuntimeSeam_Bridge_" + label);
                if (bridgeGo != null)
                {
                    Bounds bb = default; bool haveBounds = false;
                    foreach (var r in bridgeGo.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        if (!haveBounds) { bb = r.bounds; haveBounds = true; } else bb.Encapsulate(r.bounds);
                    }
                    if (haveBounds) centre = bb.center;
                    FlowTrace.Step("Auto", $"CaptureMoatRing: framing seam '{label}' from RuntimeSeam_Bridge object @ {centre}.");
                }
                else
                {
                    FlowTrace.Warn("Auto", $"CaptureMoatRing: no RuntimeSeam_Bridge_{label} — using cardinal radial fallback @ {centre}.");
                }

                // Eye: outside the crossing + elevated, looking castle-ward (midway between the
                // crossing and the plinth centre) so both the bridge deck and the wall read.
                Vector3 eye = centre + outward * 34f + Vector3.up * 22f;
                Vector3 lookTarget = new Vector3(centre.x * 0.45f, 3f, centre.z * 0.45f);
                yield return CaptureFramedShot(eye, lookTarget, 52f, "moat_seam_" + suffix + ".png");
                shot++;
            }

            _lastDetail = $"{shot} moat shots captured (moat_ring + 4 seams)";
            FlowTrace.Step("Auto", $"CaptureMoatRing: {shot} shots captured (moat_ring.png + moat_seam_N/E/S/W.png).");
        }

        // Spawn a SHORT-LIVED capture camera at a HIGHER depth (so it composites over the
        // gameplay follow-camera without disturbing its transform), point it eye->lookTarget,
        // let it render two frames, write fileName, then destroy the camera. The ONE moat-shot
        // camera path — reused by the overview + every per-seam shot. Renders only graphics-on
        // (blank under -nographics); fully guarded so a miss logs via FlowTrace and continues.
        private IEnumerator CaptureFramedShot(Vector3 eye, Vector3 lookTarget, float fov, string fileName)
        {
            GameObject camGo = null;
            try
            {
                camGo = new GameObject("[AutoPilot_MoatShotCam]");
                var cam = camGo.AddComponent<Camera>();
                cam.depth = 100f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = fov;
                camGo.transform.position = eye;
                camGo.transform.rotation = Quaternion.LookRotation((lookTarget - eye).normalized, Vector3.up);
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Auto", $"CaptureFramedShot({fileName}): capture-camera setup threw {ex.Message}");
                if (camGo != null) UnityEngine.Object.Destroy(camGo);
                yield break;
            }

            // Let the camera render a couple of frames before the capture flushes.
            yield return null;
            yield return null;
            CaptureRawShot(fileName);
            yield return null;   // flush ScreenCapture before we tear the camera down
            if (camGo != null) UnityEngine.Object.Destroy(camGo);
            FlowTrace.Step("Auto", $"CaptureFramedShot: captured {fileName} (eye={eye}).");
        }

        // =====================================================================
        //  PHASE: CaptureCastleExterior  (owner directive 2026-07-04 — "required for bots")
        //  The headless QA fleet never SHOWS the merged-world castle EXTERIOR, so a leftover
        //  bridge/seam structure out past the walls is invisible to CLI + owner. This phase
        //  self-serves that evidence with 5 short-lived capture-camera shots (reuses the
        //  CaptureFramedShot moat-cam path — composites over the follow-cam, shoots 2 frames,
        //  destroys the cam; blank under -nographics; each shot Guard-wrapped so one miss logs
        //  + continues). The castle sits at world ORIGIN flush at y=0; moat outer band = 62,
        //  gates at the cardinals ~r=58 (South -Z / North +Z / East +X / West -X).
        //   1) AERIAL  -> castle_aerial.png
        //   2) 25m OUTSIDE each gate, facing the castle -> castle_gate_S/W/N/E.png
        // =====================================================================
        private IEnumerator CaptureCastleExterior()
        {
            const float GateRadius   = 58f;   // gate ring (moat band 44..62; gates seat ~58)
            const float OutsideDist  = 25f;   // owner: "go 25m OUTSIDE each gate"
            const float EyeHeight     = 2.5f;  // eye height at the gate (owner ~2-3m)

            int shot = 0;

            // ---- 1) AERIAL / top-down --------------------------------------------------
            // A slight bird's-eye TILT rather than dead-straight-down: a pure nadir view reads
            // flat (roofs only, no wall/gate relief). Eye high on -Z, tilted down onto origin,
            // wide 60-deg FOV frames the whole ~124m-wide castle + inner ring + all 4 gates +
            // the surrounding ~150m so any stray exterior structure is caught in one shot.
            Vector3 aerialEye = new Vector3(0f, 220f, -120f);
            yield return CaptureFramedShot(aerialEye, new Vector3(0f, 0f, 0f), 60f, "castle_aerial.png");
            shot++;
            FlowTrace.Step("Auto", $"CaptureCastleExterior: aerial eye={aerialEye} -> (0,0,0) fov=60 -> castle_aerial.png.");

            // ---- 2) 25m OUTSIDE each gate, looking BACK at the castle -------------------
            // outwardDir points radially OUT from origin through the gate. eye = outward*(58+25)
            // + up*2.5 (just past the gate at eye height); lookTarget = the castle body (0,4,0).
            var gates = new (string label, Vector3 outward)[]
            {
                ("S", new Vector3(0f, 0f, -1f)),
                ("W", new Vector3(-1f, 0f, 0f)),
                ("N", new Vector3(0f, 0f, 1f)),
                ("E", new Vector3(1f, 0f, 0f)),
            };
            foreach (var (label, outward) in gates)
            {
                Vector3 dir = outward.normalized;
                Vector3 gatePos    = dir * GateRadius;
                Vector3 eye        = dir * (GateRadius + OutsideDist) + Vector3.up * EyeHeight;
                Vector3 lookTarget = new Vector3(0f, 4f, 0f);
                string fileName    = "castle_gate_" + label + ".png";
                FlowTrace.Step("Auto", $"CaptureCastleExterior: gate {label} gatePos={gatePos} eye={eye} -> {lookTarget} fov=55 -> {fileName}.");
                yield return CaptureFramedShot(eye, lookTarget, 55f, fileName);
                shot++;
            }

            _lastDetail = $"{shot} castle-exterior shots captured (aerial + 4 gates S/W/N/E)";
            FlowTrace.Step("Auto", $"CaptureCastleExterior: {shot} shots captured (castle_aerial.png + castle_gate_S/W/N/E.png).");
        }

        // =====================================================================
        //  PHASE: VerifyMoatOracle
        //  Runs the moat completeness oracle (CastleMoatBuilder.VerifyMoatComplete,
        //  side-effect-free, logs its own MOAT_COMPLETE / MOAT_INCOMPLETE marker to
        //  break-log). Its reachability leg needs a LIVE navmesh, so it must run in
        //  PLAY-MODE AFTER the settle delay (mirrors CastleNavTopologyDiag's ~1.5s
        //  wait for the RuntimeRegionGate rebake + world load), NOT at boot.
        //  Hub-only (the moat builds on MainCastle_Hall); skips cleanly on a
        //  --scene override. Guarded — a throw logs + the run continues.
        // =====================================================================
        private IEnumerator VerifyMoatOracle()
        {
            if (ActiveScene() != GameplayScene)
            {
                _lastDetail = $"skipped (scene='{ActiveScene()}', not the castle hub)";
                FlowTrace.Step("Auto", $"VerifyMoatOracle: not on '{GameplayScene}' (scene='{ActiveScene()}') — moat not built here, skipping.");
                yield break;
            }

            // Settle: let the RuntimeRegionGate rebake + world navmesh come live
            // before the reachability leg probes it (else it self-reports INCONCLUSIVE).
            yield return Wait(1.5f);

            bool ok = false;
            try { ok = DeNelle.Village.World.CastleMoatBuilder.VerifyMoatComplete(); }
            catch (Exception ex) { FlowTrace.Warn("Auto", "VerifyMoatOracle: VerifyMoatComplete threw " + ex.Message); }

            _lastDetail = ok ? "MOAT_COMPLETE" : "MOAT_INCOMPLETE (see break-log)";
            FlowTrace.Step("Auto", $"VerifyMoatOracle: oracle returned {(ok ? "MOAT_COMPLETE" : "MOAT_INCOMPLETE")}.");
        }

        // TODO(front-end capture): Title + HeroSelect are NOT reachable from this driver's
        // lifecycle. BootToGameplay deliberately SKIPS the Title->HeroSelect->MainCastle_Hall
        // front-end (a headless bot can't drive those uGUI/UITK creation flows — see the
        // BootToGameplay header) and jumps straight into the gameplay scene via
        // SceneManager.LoadScene(GameplayScene). By the time this AutoPilotDriver's RunAll
        // begins, the front-end scenes have already been bypassed, so there is no live Title/
        // HeroSelect surface to CaptureUiPanel here without faking it. The clean hook would be
        // a SEPARATE, EARLIER capture pass owned by the front-end scenes' own bootstraps (e.g.
        // a one-frame ScreenCapture on TitleScreen/HeroSelect first-shown, gated by the same
        // --autopilot / AUTOPILOT env flag AutoPilotInstaller reads), writing title.png /
        // heroselect.png into the same persistentDataPath/ui-shots folder. That belongs in the
        // Title/HeroSelect boot code, not this post-front-end driver — wiring it from here would
        // require re-loading those scenes mid-run and driving their flows, which is exactly the
        // headless-undriveable path BootToGameplay exists to avoid. Left unbuilt on purpose.

        private IEnumerator HomeReturnRoundTrip()
        {
            const float PlinthHalf = 44f;         // = CastleHubBuilder.PlinthHalf (courtyard footprint)
            const float SelfArmExitBudget = 25f;  // widen: courtyard-warp + walk into the seam + confirm-tap
            const float WalkOutBudget     = 15f;  // leg 1: walk ~20m outward from the landing
            const float ReturnLegBudget   = 40f;  // leg 2: navigate back + re-enter (25+15+40 < HomeReturnTimeout-2)
            float t0 = Time.realtimeSinceStartup;

            EnsureHero("HomeReturnRoundTrip");   // re-resolve a post-stream hero (RCA 2026-07-08) — unlock overworld coverage
            if (_hero == null)
            {
                // The ONLY remaining SKIP: with no hero there is nothing to drive.
                FlowTrace.Warn("Auto", "HomeReturnRoundTrip: no hero — impossible to attempt, skipping (EnsureHero named the reason above).");
                _lastDetail = "skipped (no hero)";
                _homeReturnVerdicts.Add(new HomeReturnResult { gate = "n/a", verdict = "SKIPPED", seconds = 0f, detail = _lastDetail });
                yield break;
            }

            float liftY = PlayerPrefs.GetFloat("castle.liftY", 3f);

            if (!_exitCrossed)
            {
                // SELF-ARM: route the hero home (the WarpTo helper every other phase
                // uses — self-logs its navmesh sample), then run the exit leg here.
                FlowTrace.Step("Auto", "HomeReturnRoundTrip: exit phase never crossed — self-arming (warp to courtyard + run the exit leg first).");
                try { _hero.WarpTo(new Vector3(0f, liftY, 0f)); }
                catch (Exception ex) { FlowTrace.Warn("Auto", "HomeReturnRoundTrip: courtyard warp threw " + ex.Message); }
                yield return null;   // let the warp settle a frame

                // Pick the exit seam with AttemptExitCastle's calibrated predicate:
                // never a RETURN trigger (target == hub is the way back IN), prefer a
                // navmesh-reachable trigger, south-most wins; global south-most fallback.
                string hubScene = ActiveScene();
                SceneTransitionTrigger exit = null;    float minZ    = float.MaxValue;
                SceneTransitionTrigger exitAny = null; float minZAny = float.MaxValue;
                Vector3 heroPos = _hero.transform.position;
                foreach (var g in UnityEngine.Object.FindObjectsByType<SceneTransitionTrigger>())
                {
                    if (g == null || string.Equals(g.targetSceneName, hubScene, StringComparison.Ordinal)) continue;
                    float z = g.transform.position.z;
                    if (z < minZAny) { minZAny = z; exitAny = g; }
                    if (NavReachable(heroPos, g.transform.position) && z < minZ) { minZ = z; exit = g; }
                }
                if (exit == null) exit = exitAny;
                if (exit == null)
                {
                    FlowTrace.Fail("AutoTest", "HOME_RETURN_FAIL :: gate=<none> — self-armed leg found NO exit seam " +
                        "(no SceneTransitionTrigger leaves the hub), so the round trip cannot start (WO-602 widen).");
                    _lastDetail = "self-arm: no exit seam";
                    _homeReturnVerdicts.Add(new HomeReturnResult { gate = "none", verdict = "FAIL",
                        seconds = Time.realtimeSinceStartup - t0, detail = _lastDetail });
                    yield break;   // force-continue: the run proceeds to the next phase regardless
                }

                // Drive the exit exactly as AttemptExitCastle does: walk in, tap the
                // seam's "Travel to ..." confirm, and treat the warp-to-landing as
                // the authoritative crossing signal.
                Vector3 exitWarpTarget = exit.targetPosition;
                float exitRadius = Mathf.Max(1f, exit.ProximityRadius);
                Vector3 exitGatePos = exit.transform.position;
                string exitName = exit.name;
                FlowTrace.Step("Auto", $"HomeReturnRoundTrip: self-armed exit via '{exitName}' @ {exitGatePos} (radius {exitRadius:0.0}m).");
                _hero.SetAutoWalk(exit.transform);
                bool crossedOut = false;
                float tExit = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tExit < SelfArmExitBudget)
                {
                    if (_hero == null) break;
                    Vector3 pos = _hero.transform.position;
                    CaptureBridgeCrossing(pos);
                    if (Vector3.Distance(pos, exitWarpTarget) < 8f) { crossedOut = true; break; }
                    if (HorizontalDistance(pos, exitGatePos) <= exitRadius + 0.5f &&
                        MobileInteractButton.IsActive && MobileInteractButton.InvokeActive())
                        FlowTrace.Step("Auto", $"HomeReturnRoundTrip: self-armed leg tapped seam '{exitName}'.");
                    yield return null;
                }
                if (_hero != null) _hero.ClearAutoWalk();
                if (!crossedOut || _hero == null)
                {
                    Vector3 hp0 = _hero != null ? _hero.transform.position : Vector3.zero;
                    FlowTrace.Fail("AutoTest", $"HOME_RETURN_FAIL :: gate={exitName} heroPos={hp0} — self-armed exit leg never crossed " +
                        $"(no warp to {exitWarpTarget} within {SelfArmExitBudget:0}s), so the return could not be exercised (WO-602 widen).");
                    _lastDetail = "self-arm: exit leg never crossed";
                    _homeReturnVerdicts.Add(new HomeReturnResult { gate = "none", verdict = "FAIL",
                        seconds = Time.realtimeSinceStartup - t0, detail = _lastDetail });
                    yield break;   // force-continue
                }
                FlowTrace.Step("Auto", "HomeReturnRoundTrip: self-armed exit crossed — proceeding with the round trip.");
            }

            // ORACLE FIX (WO-602 repro 2026-07-13, data-proven): the y≈liftY(3) expectation was the
            // moat-plinth design, NOT the merged world's walkable truth — the portal warp lands the
            // hero home and the navmesh settles him at y≈0.08 (captured: WarpTo((0,3,0)) sample HIT
            // (0.34, 0.08, 0.86); the probe's own self-arm warp to (0,liftY,0) settles to 0.08 too).
            // Assert the FELT requirement: back inside the courtyard footprint, standing on the mesh
            // the game actually walks — the courtyard's REAL ground Y, sampled once from the navmesh.
            float courtyardY = liftY;
            if (UnityEngine.AI.NavMesh.SamplePosition(Vector3.zero, out var courtHit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                courtyardY = courtHit.position.y;
            bool BackHome() => _hero != null
                && Mathf.Abs(_hero.transform.position.y - courtyardY) <= 1.0f
                && HorizontalDistance(_hero.transform.position, Vector3.zero) < PlinthHalf;

            // 1) Walk OUTWARD ~20m from the castle (radially away from the origin) so the
            //    return is a real approach, not a residual overlap with the landing radii.
            Vector3 start = _hero.transform.position;
            Vector3 outward = start; outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.back;
            var outMarker = new GameObject("__AutoPilot_HomeReturn_OutMarker");
            outMarker.transform.position = start + outward * 20f;
            _hero.SetAutoWalk(outMarker.transform);
            float tOut = Time.realtimeSinceStartup;   // leg-relative: t0 may already carry the self-armed exit leg
            while (Time.realtimeSinceStartup - tOut < WalkOutBudget)
            {
                if (_hero == null) break;
                if (HorizontalDistance(_hero.transform.position, start) >= 18f) break;
                yield return null;
            }
            if (_hero != null) _hero.ClearAutoWalk();
            UnityEngine.Object.Destroy(outMarker);
            Vector3 farPos = _hero != null ? _hero.transform.position : start;
            FlowTrace.Step("Auto", $"HomeReturnRoundTrip: walked out to {farPos} ({HorizontalDistance(farPos, start):0.0}m from landing) — now returning home.");

            // 2) Find the nearest OUTER return entrance (a SceneTransitionTrigger whose target
            //    IS the hub scene — the same predicate AttemptExitCastle now excludes).
            SceneTransitionTrigger ret = null;
            float bestD = float.MaxValue;
            string hub = ActiveScene();
            foreach (var g in UnityEngine.Object.FindObjectsByType<SceneTransitionTrigger>())
            {
                if (g == null || !string.Equals(g.targetSceneName, hub, StringComparison.Ordinal)) continue;
                float d = HorizontalDistance(farPos, g.transform.position);
                if (d < bestD) { bestD = d; ret = g; }
            }
            string gateName = ret != null ? ret.name : "<none>";
            // Side suffix for the verdict/fail line ("RuntimeSeam_ReturnTrigger_South" -> "South").
            string gateSide = gateName.Contains("_") ? gateName.Substring(gateName.LastIndexOf('_') + 1) : gateName;

            if (ret == null || _hero == null)
            {
                Vector3 hp = _hero != null ? _hero.transform.position : Vector3.zero;
                FlowTrace.Fail("AutoTest", $"HOME_RETURN_FAIL :: gate=<none> heroPos={hp} — NO outer return entrance exists " +
                    "(no SceneTransitionTrigger targets the hub scene). The way back home is unwired (WO-602).");
                _lastDetail = "no outer return entrance found";
                _homeReturnVerdicts.Add(new HomeReturnResult { gate = "none", verdict = "FAIL",
                    seconds = Time.realtimeSinceStartup - t0, detail = _lastDetail });
                yield break;   // force-continue: the run proceeds to the next phase regardless
            }

            // 3) Drive back to the gate's outer landing; the passive HeroLinkCrossing lane may
            //    warp us in en route, else tap the visible "Enter Elarion" prompt like a player.
            FlowTrace.Step("Auto", $"HomeReturnRoundTrip: navigating back to '{gateName}' @ {ret.transform.position} " +
                $"(r={ret.ProximityRadius:0.0}m, {bestD:0.0}m away) — assert courtyard y≈{liftY:0.0} r<{PlinthHalf:0}.");
            _hero.SetAutoWalk(ret.transform);
            bool home = false;
            bool tapped = false;
            float closest = float.MaxValue;
            Vector3 retPos = ret.transform.position;
            float retRadius = Mathf.Max(1f, ret.ProximityRadius);
            float tRet = Time.realtimeSinceStartup;   // leg-relative budget; legs sum to < HomeReturnTimeout-2 so the Fail below emits
            while (Time.realtimeSinceStartup - tRet < ReturnLegBudget)
            {
                if (_hero == null) break;
                CaptureBridgeCrossing(_hero.transform.position);
                if (BackHome()) { home = true; break; }
                float d = HorizontalDistance(_hero.transform.position, retPos);
                if (d < closest) closest = d;
                if (!tapped && d <= retRadius + 0.5f && MobileInteractButton.IsActive && MobileInteractButton.InvokeActive())
                {
                    tapped = true;
                    FlowTrace.Step("Auto", $"HomeReturnRoundTrip: bot tapped 'Enter Elarion' on '{gateName}'.");
                    // ORACLE FIX (WO-602 repro 2026-07-13): STOP walking once the cross is confirmed —
                    // the tap starts a fade->warp; leaving autowalk armed marched the hero back OUT of
                    // the courtyard after the warp landed (captured: repositioned @ (0.34,0.08,0.86),
                    // then walked south and an organic encounter's BattleArena.WarpHero hijacked the
                    // run to the 5000-offset arena — the FAIL snapshot). Stand still; poll BackHome.
                    _hero.ClearAutoWalk();
                }
                yield return null;
            }
            if (_hero != null) _hero.ClearAutoWalk();

            float secs = Time.realtimeSinceStartup - t0;
            if (home)
            {
                Vector3 hp = _hero.transform.position;
                FlowTrace.Step("AutoTest", $"HOME_RETURN_OK :: gate={gateSide} heroPos={hp} " +
                    $"(y within 0.5 of liftY={liftY:0.0}, r={HorizontalDistance(hp, Vector3.zero):0.0}<{PlinthHalf:0}; tapped={tapped}) in {secs:0.0}s.");
                _lastDetail = $"returned home via {gateSide} (tapped={tapped})";
                _homeReturnVerdicts.Add(new HomeReturnResult { gate = gateSide, verdict = "PASS", seconds = secs, detail = _lastDetail });
            }
            else
            {
                Vector3 hp = _hero != null ? _hero.transform.position : Vector3.zero;
                FlowTrace.Fail("AutoTest", $"HOME_RETURN_FAIL :: gate={gateSide} heroPos={hp} — hero never re-entered the courtyard " +
                    $"(closest {closest:0.0}m of return radius {retRadius:0.0}m, tapped={tapped}) within {ReturnLegBudget:0}s (WO-602).");
                _lastDetail = $"return FAILED via {gateSide} (closest {closest:0.0}m, tapped={tapped})";
                _homeReturnVerdicts.Add(new HomeReturnResult { gate = gateSide, verdict = "FAIL", seconds = secs, detail = _lastDetail });
                // force-continue: no throw/abort — the remaining phases still run.
            }
        }

        // =====================================================================
        //  PHASE: WalkToOuterWorldOutpost  (WO-449 + WO-452 seeded chaos)
        //  The continuous-walk raid loop, asserted end-to-end: resolve a live
        //  OuterWorld EnemyOutpost (RaidOutpostSystem.Outposts), WALK to it via the
        //  same SetAutoWalk seam WalkToEachGate uses, and prove BOTH oracles:
        //    (a) ANTI-WARP: no single-frame jump > WalkMaxStepMeters (the assertion
        //        that would have caught the old teleport), and
        //    (b) COMBAT-ON-APPROACH: the hero reaches the outpost ON FOOT and a
        //        garrison Enemy is alive + within engage range on arrival.
        //  This phase LOADS NO SCENE — the UNEXPECTED-CROSS probe is left armed, so a
        //  re-introduced raid teleport trips AutoPilotProbes' scene-load Fail.
        //
        //  SEEDED CHAOS (WO-452): a per-phase seed ("autopilot.seed" env/PlayerPrefs,
        //  else the run seed) drives WHICH outpost is targeted + jittered pause/dash
        //  beats along the walk. Chaos changes the PATH only — the anti-warp +
        //  combat-on-approach oracles ALWAYS run, never weakened by the random route.
        // =====================================================================
        private IEnumerator WalkToOuterWorldOutpost()
        {
            // ── Seed source: env var / PlayerPrefs "autopilot.seed", else the run seed. ──
            int seed = _seed;
            try
            {
                string env = Environment.GetEnvironmentVariable("autopilot.seed");
                if (!string.IsNullOrEmpty(env) && int.TryParse(env, out int es)) seed = es;
                else if (PlayerPrefs.HasKey("autopilot.seed")) seed = PlayerPrefs.GetInt("autopilot.seed");
            }
            catch { /* env read is best-effort; fall back to the run seed */ }
            var rng = new System.Random(seed);
            FlowTrace.Step("Auto", $"WalkToOuterWorldOutpost seed={seed}");

            EnsureHero("WalkToOuterWorldOutpost");   // re-resolve a post-stream hero (RCA 2026-07-08) — unlock overworld coverage
            if (_hero == null)
            {
                FlowTrace.Warn("Auto", "WalkToOuterWorldOutpost: no hero — skipping (EnsureHero named the reason above).");
                _lastDetail = "no hero — skipped";
                yield break;
            }

            // (a) Resolve a realized outpost — they materialise ~10s after the OuterWorld
            // additive load (RaidOutpostSystem.SpawnDelaySeconds), so poll up to ~12s.
            EnemyOutpost outpost = null;
            float tPoll = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tPoll < 12f && outpost == null)
            {
                var live = new List<EnemyOutpost>();
                var arr = RaidOutpostSystem.Outposts;   // entries null until realized
                if (arr != null)
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] != null) live.Add(arr[i]);
                if (live.Count > 0)
                {
                    // Chaos: target a RANDOM available outpost (oracles are outpost-agnostic).
                    outpost = live[rng.Next(live.Count)];
                    break;
                }
                yield return Wait(0.5f);
            }

            if (outpost == null)
            {
                // Not ticket-worthy on its own: the walk loop may be flag-off (RaidContinuousWalk)
                // or the outpost simply never realized in this scene. Warn + skip cleanly.
                FlowTrace.Warn("Auto", "WalkToOuterWorldOutpost: no realized EnemyOutpost found within ~12s " +
                    "(continuous-walk flag off, or not in OuterWorld) — skipping.");
                _lastDetail = "no outpost realized — skipped";
                yield break;
            }

            Transform target = outpost.transform;
            float engageRange = EnemyOutpost.GarrisonRing + 2f;   // "reached on foot" proximity
            Vector3 heroStart = _hero.transform.position;
            FlowTrace.Step("Auto", $"WalkToOuterWorldOutpost: walking to '{outpost.OutpostId}' at {target.position} " +
                $"(engageRange={engageRange:0.0}m, heroStart={heroStart}).");

            _hero.SetAutoWalk(target);

            // Walk + assert. prev tracks the previous frame's position for the anti-warp test.
            Vector3 prev = _hero.transform.position;
            float t0 = Time.realtimeSinceStartup;
            bool reachedOnFoot = false;
            int nextJitterBeat = 2 + rng.Next(3);   // first pause/dash beat after a few frames
            int frame = 0;

            while (Time.realtimeSinceStartup - t0 < OuterWalkTimeout)
            {
                if (_hero == null) break;
                Vector3 pos = _hero.transform.position;

                // ── (b/anti-warp) ORACLE: a single-frame jump > WalkMaxStepMeters is a WARP. ──
                float d = Vector3.Distance(pos, prev);
                if (d > WalkMaxStepMeters)
                {
                    FlowTrace.Fail("Auto", $"hero WARPED instead of walked: single-frame jump {d:0.0}m " +
                        "(continuous-walk loop must never teleport).");
                    _hero.ClearAutoWalk();
                    _lastDetail = $"WARP detected ({d:0.0}m single-frame jump)";
                    yield break;   // end the phase FAILED
                }
                prev = pos;

                // Reached the outpost ON FOOT?
                if (HorizontalDistance(pos, target.position) <= engageRange)
                {
                    reachedOnFoot = true;
                    break;
                }

                // ── CHAOS: jittered pause/dash beats. Occasionally drop auto-walk for a few
                // frames (a "pause"), then re-issue it (a "dash" resumes the route). This varies
                // the PATH/timing only; the oracles above run every frame regardless. No per-frame
                // allocation — just counters + the existing SetAutoWalk/ClearAutoWalk seam.
                if (frame >= nextJitterBeat)
                {
                    _hero.ClearAutoWalk();
                    int pauseFrames = 1 + rng.Next(4);
                    for (int p = 0; p < pauseFrames; p++)
                    {
                        if (_hero == null) break;
                        // Anti-warp still armed during the pause (the hero should be ~still).
                        Vector3 pp = _hero.transform.position;
                        float dp = Vector3.Distance(pp, prev);
                        if (dp > WalkMaxStepMeters)
                        {
                            FlowTrace.Fail("Auto", $"hero WARPED during chaos-pause: single-frame jump {dp:0.0}m " +
                                "(continuous-walk loop must never teleport).");
                            _lastDetail = $"WARP detected during pause ({dp:0.0}m)";
                            yield break;
                        }
                        prev = pp;
                        yield return null;
                    }
                    if (_hero == null) break;
                    _hero.SetAutoWalk(target);   // resume the route
                    nextJitterBeat = frame + 4 + rng.Next(6);
                }

                frame++;
                yield return null;
            }
            if (_hero != null) _hero.ClearAutoWalk();

            if (!reachedOnFoot)
            {
                FlowTrace.Fail("Auto", $"WalkToOuterWorldOutpost: hero never reached outpost '{outpost.OutpostId}' on foot " +
                    $"within {OuterWalkTimeout:0}s (closest approach failed; navmesh edge / blocked).");
                _lastDetail = "did not reach outpost on foot";
                yield break;
            }

            // ── (b) COMBAT-ON-APPROACH ORACLE: reached on foot — is the garrison engaging? ──
            // Give the aggro a moment to pull a defender into engage range, then assert at least
            // one garrison Enemy is alive within range. (The outpost may auto-clear if it spawned
            // empty — that is its own legit anti-deadlock; treat an already-cleared outpost as
            // "nothing to engage" and report it, not a hard combat failure.)
            bool engaged = false;
            float tEngage = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tEngage < 6f && !engaged)
            {
                if (outpost == null || _hero == null) break;
                if (outpost.Cleared) break;   // garrison gone (cleared/auto-cleared)
                engaged = AnyGarrisonEngaging(outpost, _hero.transform.position, engageRange + 6f);
                if (engaged) break;
                yield return null;
            }

            if (engaged)
            {
                FlowTrace.Step("Auto", $"WalkToOuterWorldOutpost: PASS — reached '{outpost.OutpostId}' ON FOOT (no warp) " +
                    "and a garrison defender engaged on approach.");
                _lastDetail = "reached on foot + garrison engaged";
            }
            else if (outpost != null && outpost.Cleared)
            {
                FlowTrace.Warn("Auto", $"WalkToOuterWorldOutpost: reached '{outpost.OutpostId}' on foot but it was already CLEARED " +
                    "(empty/auto-cleared garrison) — nothing to engage.");
                _lastDetail = "reached on foot; outpost already cleared";
            }
            else
            {
                FlowTrace.Fail("Auto", "reached outpost on foot but garrison never engaged on approach");
                _lastDetail = "reached on foot but no engage";
            }
        }

        // True if any living garrison Enemy under the outpost is within engageRange of the hero
        // (the "combat on approach" signal). Cheap GetComponentsInChildren over the outpost root
        // only (the garrison is parented under it), run a handful of times, not per frame.
        private static bool AnyGarrisonEngaging(EnemyOutpost outpost, Vector3 heroPos, float engageRange)
        {
            if (outpost == null) return false;
            var enemies = outpost.GetComponentsInChildren<Enemy>(includeInactive: false);
            if (enemies == null) return false;
            float r2 = engageRange * engageRange;
            foreach (var e in enemies)
            {
                if (e == null || !e.IsAlive) continue;
                if ((e.transform.position - heroPos).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        // =====================================================================
        //  Summary file
        // =====================================================================
        private void WriteSummary()
        {
            try
            {
                var summary = new RunSummary
                {
                    utc = DateTime.UtcNow.ToString("o"),
                    totalSeconds = Time.realtimeSinceStartup - _runStartRealtime,
                    aborted = _abortRun,
                    seed = _seed,                       // WO-452 tranche E: reproducibility
                    runId = _runId ?? "",               // WO-452 tranche E: replay handle
                    phases = _phases.ToArray(),
                    popupClose = _popupVerdicts.ToArray(),   // WO-597: per-panel POPUP-CLOSABLE verdicts
                    homeReturn = _homeReturnVerdicts.ToArray(),   // WO-602: home-return round-trip verdict
                };
                // Fleet mode: write into persistentDataPath/autopilot-runs/<id>/ so it
                // sits beside this run's break-log.jsonl and the aggregator can count
                // it as one run's coverage. Default (no --run) -> root, unchanged.
                string baseDir = Application.persistentDataPath;
                string outDir = baseDir;
                if (!string.IsNullOrEmpty(_runId))
                {
                    outDir = Path.Combine(Path.Combine(baseDir, "autopilot-runs"), _runId);
                    Directory.CreateDirectory(outDir);
                }
                string path = Path.Combine(outDir, "autopilot-summary.json");
                File.WriteAllText(path, JsonUtility.ToJson(summary, true));
                FlowTrace.Step("Auto", $"AutoPilot summary written -> {path}");
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Auto", "AutoPilot: failed to write summary — " + ex.Message);
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        private static string ActiveScene()
        {
            try { return SceneManager.GetActiveScene().name; } catch { return "?"; }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // Can the hero PATH from `from` to `to` on the live baked navmesh? Both ends are
        // sampled (4m — trigger markers float ~1.5m above the surface, and the raised
        // castle courtyard bakes at y=castle.liftY) then a full path is calculated;
        // only PathComplete counts (a partial path = the Outpost1 cross-scene stall).
        private static bool NavReachable(Vector3 from, Vector3 to)
        {
            const float SampleTol = 4f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(from, out var a, SampleTol, UnityEngine.AI.NavMesh.AllAreas)) return false;
            if (!UnityEngine.AI.NavMesh.SamplePosition(to,   out var b, SampleTol, UnityEngine.AI.NavMesh.AllAreas)) return false;
            var path = new UnityEngine.AI.NavMeshPath();
            return UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, UnityEngine.AI.NavMesh.AllAreas, path)
                   && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
        }

        // Seeded Fisher-Yates in-place shuffle of the per-phase work order so distinct
        // seeds explore distinct paths. Uses _rng (seeded in Begin). Null-safe.
        private void Shuffle<T>(IList<T> list)
        {
            if (list == null || _rng == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        // Realtime wait — never freezes under timeScale=0 (F8 flag flow).
        private static WaitForSecondsRealtime Wait(float seconds)
            => new WaitForSecondsRealtime(seconds);

        [Serializable]
        private struct PhaseResult
        {
            public string phase;
            public string status;   // ok / timeout / threw
            public float seconds;
            public string detail;
        }

        // WO-597: one POPUP-CLOSABLE verdict per PanelId, serialized into
        // autopilot-summary.json (popupClose[]) alongside the phase rows.
        [Serializable]
        private struct PopupCloseResult
        {
            public string panel;    // PanelId name (e.g. "PartyShop")
            public string verdict;  // PASS / OPEN_FAILED / NO_CLOSE / NOT_REGISTERED
            public string route;    // the close route attempted (e.g. "uGUI button 'CloseButton'")
            public float seconds;   // wall time spent on this panel
            public string detail;   // one-line why (matches the break-log Fail message)
        }

        // WO-602: one HOME-RETURN round-trip verdict per run, serialized into
        // autopilot-summary.json (homeReturn[]) alongside the popupClose rows.
        [Serializable]
        private struct HomeReturnResult
        {
            public string gate;     // gate side attempted (e.g. "South") or "none"/"n/a"
            public string verdict;  // PASS / FAIL / SKIPPED
            public float seconds;   // wall time spent on the round trip
            public string detail;   // one-line why (matches the break-log line)
        }

        [Serializable]
        private struct RunSummary
        {
            public string utc;
            public float totalSeconds;
            public bool aborted;
            public int seed;        // WO-452 tranche E — the run's seed (for replay)
            public string runId;    // WO-452 tranche E — the run id (namespaces output)
            public PhaseResult[] phases;
            public PopupCloseResult[] popupClose;   // WO-597 — per-panel closable verdicts
            public HomeReturnResult[] homeReturn;   // WO-602 — per-run home-return round-trip verdict
        }
    }
}

#endif // DEVELOPMENT_BUILD || UNITY_EDITOR
