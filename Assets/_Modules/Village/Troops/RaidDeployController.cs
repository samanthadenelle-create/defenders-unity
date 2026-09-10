// =============================================================================
// RaidDeployController — the troop DEPLOY / RALLY / RETREAT HUD + tap state machine
// for a raid base (WO-453 Step 4, first-playable).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Lives in a RaidBase_* scene (self-installs via a RuntimeInitialize hook when the
// loaded scene is enemy-owned AND its name starts "RaidBase"). It is the player's
// command surface for the assault:
//
//   DEPLOY  — tap a troop tile in the bottom tray to ARM that TroopDefId, then tap
//             the ground (RaycastGround, the BuildMode-proven world tap) to drop one
//             deployable PlayerTroop of that type onto the NavMesh. Quantity drains
//             over multiple taps (one PlayerTroop per tap); the tile count counts down.
//             Each drop spawns through the canonical TroopDeployer.SpawnFromArmy path
//             (stamps OwnedTroopId + applies the veterancy DamageMultiplier).
//   RALLY   — toggle Rally on, then tap the ground to set the global TroopRally.Point.
//             Idle troops (no foe in range) walk to it; a foe in range ALWAYS wins
//             (rally only fills the idle gap — owner-decided default).
//   RETREAT — survivors = the living deployed bodies' OwnedTroopIds; reconcile the
//             army (deployed-but-not-survivor → wounded) and evac home via GoCastle.
//
// Code-built uGUI (NO UXML — repo rule). NON-modal: a bottom tray + Rally/Retreat
// buttons that never blacken the screen. Input is the NEW Input System (Mouse.current,
// never legacy Input.*), with an optional Lean.Touch tap mirrored in for mobile.
//
// SCOPE (first playable): win/stars are OUT — only the loss/RETREAT exit is wired.
// Deploy ANYWHERE ON THE NAVMESH (no zone gating) — and that is now ENFORCED, not just
// asserted: HandleDeployTap refuses a tap with no baked NavMesh within
// TroopFactory.NavSampleRadius. It used to be a claim only. RaycastGround falls back to
// ALL layers, so a tap on scenery/rooftop/out-of-bounds terrain resolved a hit and
// spawned an INERT troop that counted as a survivor at reconcile — free 3-star clears
// (defect sweep 2026-08-15). Tunables are [SerializeField] so the owner can tune by feel.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Core.UI;

namespace DeNelle.Village
{
    /// <summary>
    /// The raid-base troop command HUD: a bottom troop tray + Rally toggle + Retreat
    /// button, plus the DEPLOY / RALLY tap state machine. Self-installs into a
    /// <c>RaidBase_*</c> enemy-owned scene; spawns through <see cref="TroopDeployer"/>
    /// and exits via <see cref="SceneRouter.GoCastle"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidDeployController : MonoBehaviour
    {
        // ── Tunables (owner tunes by feel later) ──────────────────────────────
        [Header("Deploy")]
        [Tooltip("Lateral spread (m) between troops dropped from repeated taps of the same tile.")]
        [SerializeField] private float _deploySpread = 1.1f;
        [Tooltip("Ground raycast distance (m) for a deploy / rally tap.")]
        [SerializeField] private float _rayDistance = 800f;
        [Tooltip("Layer mask the deploy/rally tap ray tests first (falls back to all layers).")]
        [SerializeField] private LayerMask _groundMask = ~0;

        [Header("Rally")]
        [Tooltip("Arrival epsilon (m) — a troop within this of the rally point idles instead of jittering. " +
                 "Mirrors TroopController.RallyArrivalEpsilon; kept here so the owner can expose/tune it.")]
        [SerializeField] private float _rallyArrivalEpsilon = 1.25f;

        [Header("Retreat")]
        [Tooltip("If true, the first Retreat tap asks for confirm (second tap evacs); false = evac immediately.")]
        [SerializeField] private bool _retreatConfirm = true;
        [Tooltip("FALLBACK recovery seconds, used only when the camp's difficulty cannot be resolved. " +
                 "The live value SCALES WITH CAMP DIFFICULTY - see RecoveryForDifficulty.")]
        [SerializeField] private float _recoverySeconds = 300f;

        // ── Runtime UI ────────────────────────────────────────────────────────
        private GameObject _ui;
        private Camera _camera;
        private TMPro.TextMeshProUGUI _status;
        private Button _deployAllButton;
        private Button _rallyButton;
        private Button _retreatButton;
        private readonly List<TrayTile> _tiles = new List<TrayTile>();

        // ── Tap state machine ─────────────────────────────────────────────────
        private string _armedDefId;     // the TroopDefId armed for the next ground tap (null = none)
        private bool _rallyMode;        // true while the Rally toggle is on (next tap sets the rally point)
        private bool _retreatPending;   // first Retreat tap, awaiting confirm (when _retreatConfirm)

        // ── Tracking deployed troops (controller + owning army id) ────────────
        private readonly List<Deployed> _deployed = new List<Deployed>();

        // Lean tap latch (mobile) — raised by a Lean.Touch finger tap, consumed in Update.
        private bool _leanTapLatched;
        private Vector2 _leanTapPoint;

        private struct Deployed
        {
            public TroopController Controller;
            public string OwnedId;
        }

        private struct TrayTile
        {
            public string DefId;
            public Button Button;
            public TMPro.TextMeshProUGUI CountLabel;
            /// <summary>The tile's NAME label, captured at construction (WO-1464). Held rather
            /// than re-found, so a refresh can never restyle the count badge by mistake -
            /// GetComponentInChildren returns whichever text child happens to be first.</summary>
            public TMPro.TextMeshProUGUI NameLabel;
        }

        // =====================================================================
        //  Self-install — add this controller to a RaidBase_* enemy-owned scene
        // =====================================================================

        /// <summary>
        /// On every scene load, if the active scene is a <c>RaidBase_*</c> the deploy HUD
        /// installs itself (one frame later so SceneOwnership has resolved + the garrison
        /// spawner has marked the scene enemy-owned). Idempotent — never double-installs.
        /// </summary>
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
            if (!sceneName.StartsWith("RaidBase", System.StringComparison.OrdinalIgnoreCase)) return;
            if (FindAnyObjectByType<RaidDeployController>() != null) return;

            // Clear any stale rally from a prior raid so it can't leak into this one.
            TroopRally.Clear();

            // ── WO-1379: SPEND ONE HEARTFIRE. This is the raid ENTRY seam ────────────
            // Canon docs/CREATIVE_CANON_ELARION_2026-09-04.md section 4: you spend
            // Heartfire, you march. "Raid Orders" is dead - the player is the ruler and
            // nobody issues them orders - but MARCH survives as the verb.
            //
            // WHY HERE: every RaidBase_* entry funnels through this one static, whatever
            // door the player came in by, and it is the mirror of ReconcileRaidEnd, which
            // is already documented as the latched seam every raid EXIT funnels through.
            // Guarded (a charge-accounting throw must never stop a raid from installing
            // its controls) but never swallowed - Guard logs through FlowTrace.Fail.
            //
            // (!) THIS SPENDS; IT DOES NOT REFUSE, AND THAT IS DELIBERATE.
            // By the time a RaidBase scene is loaded the player is already there, so
            // refusing here would strand them in a scene with nothing to do - strictly
            // worse than letting an over-spend through. The REFUSAL lives one step
            // earlier, at the ONE door: RaidSelectionScreen.OnCardTapped checks
            // HeartfireService.HasCharge and shows BlockedMessage (WO-1379, landed
            // 2026-09-05; it REPLACED the per-camp RaidCooldownService.IsOnCooldown gate
            // there - one gate on WHEN you may raid, owner ruling). From that door this
            // Fail line is UNREACHABLE. It stays in the code on purpose (CLAUDE.md
            // section 12: never strip FlowTrace) as the tripwire for any OTHER path that
            // loads a RaidBase_* scene without passing the door - a dev shortcut, a
            // stale deep link, a future second entry. If it fires, the door was bypassed.
            DeNelle.Core.Diagnostics.Guard.Try("Heartfire", "spend heartfire on raid entry", () =>
            {
                if (DeNelle.Village.World.Camps.HeartfireService.TrySpend(sceneName)) return;
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Heartfire",
                    "a raid scene ('" + sceneName + "') was ENTERED with an EMPTY Heartfire pool. " +
                    "The march is allowed to proceed on purpose - refusing inside an already-loaded " +
                    "raid scene would strand the player - but this line means the door gate did not " +
                    "run: RaidSelectionScreen.OnCardTapped refuses on HeartfireService.HasCharge " +
                    "(WO-1379), so whatever loaded this scene BYPASSED the one door. Find that path.");
            });

            var go = new GameObject("RaidDeployController");
            go.AddComponent<RaidDeployController>();
            Debug.Log($"[RaidDeployController] self-installed in raid scene '{sceneName}'.");
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        // The raid scorer (WO-771.6) — bound a few frames after Start so its clock can
        // END the raid (retreat) when time runs out. Null when scoring isn't present.
        private RaidScoring _scoring;

        // WO-1526: local latch so a repeated hero-death notification cannot re-stamp the status
        // line over whatever the player is reading next. The scorer's latch owns the star cap;
        // this one owns the sentence.
        private bool _heroDownAcknowledged;

        /// <summary>
        /// WO-1095 - TRUE once <see cref="OnRaidTimeExpired"/> is actually subscribed to
        /// <c>RaidScoring.OnTimeExpired</c>. It exists so the stranding watchdog's failure text
        /// can STATE whether the subscriber is installed instead of ACCUSING it.
        ///
        /// <para>The old last-resort line said "the OnTimeExpired subscriber is missing or
        /// RaidScoring never installed" on every firing. In the 2026-09-09 capture (F8 seq 4980)
        /// neither was true: no "deploy HUD failed to build" line was emitted, and
        /// <c>RaidScoring.Finalize</c> ran on a live scorer immediately after the watchdog fired
        /// ("stars settled: 0 ... elapsed=50s/180s"). A trace that names a cause it has not
        /// checked sends the next reader to the wrong file (CLAUDE.md sec.11B).</para>
        /// </summary>
        private bool _clockSubscribed;

        /// <summary>
        /// WO-1095 - did <see cref="BuildHud"/> succeed? A raid with a built HUD has a Retreat
        /// button, so it is NOT the exitless state the tight backstop bound was chosen for.
        /// Read by <see cref="ClassifyStranding"/>; reported in the failure text.
        /// </summary>
        private bool _hudBuilt;

        /// <summary>
        /// WO-1110 fault-injection hook: when true the next <see cref="BuildHud"/> throws.
        /// Exists so the "a HUD build failure still leaves an exit" acceptance can be PROVEN
        /// by a deliberate injection rather than by reading the diff (CLAUDE.md §12 — the
        /// data proves it, not the reasoning). Never set outside a test/AutoPilot harness.
        /// </summary>
        public static bool DebugForceBuildHudThrow;

        private void Start()
        {
            _camera = Camera.main;

            // ORDER IS LOAD-BEARING (WO-1110 §1). The clock-expiry subscriber is the raid's
            // LAST-RESORT exit: if the HUD fails to build there is no tray and no Retreat
            // button, and the ONLY way out is the 180s OnTimeExpired -> DoRetreat rescue.
            // BuildHud() used to run FIRST and unguarded, so a throw inside it skipped the
            // StartCoroutine line entirely and left the player in the raid's one exitless
            // state. Subscribe first, build presentation second, and guard the build — the
            // exit hatch must never depend on presentation succeeding.
            StartCoroutine(BindScoringRoutine());

            bool built = DeNelle.Core.Diagnostics.Guard.Try("Raid", "build raid deploy HUD", BuildHud);
            _hudBuilt = built;   // WO-1095: the watchdog reports this instead of guessing at it.
            if (!built)
            {
                // The tray/Retreat button are gone; say so loudly and tell the player the
                // clock will still evac them, so a blank raid never reads as a softlock.
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                    "deploy HUD failed to build - no tray and no Retreat button. The raid clock " +
                    "subscriber IS installed (bound before the build), so OnTimeExpired will still " +
                    "retreat the player; the raid is degraded, not softlocked.");
                DeNelle.Core.UI.ElarionUiKit.ShowToast(
                    "Raid controls failed to load - you will be evacuated when the clock runs out.",
                    DeNelle.Core.UI.ElarionUiKit.ToastTone.Danger, lifeSeconds: 6f);
            }
        }

        private void OnDestroy()
        {
            // Don't let a rally leak across scenes.
            TroopRally.Clear();
            if (_scoring != null) _scoring.OnTimeExpired -= OnRaidTimeExpired;
            if (_rallyFlag != null) Destroy(_rallyFlag);
            if (_ui != null) Destroy(_ui);
        }

        // The scorer self-installs the same frame this HUD does; poll a few frames for
        // it, then subscribe so the 180s clock expiry ends the raid via the retreat path
        // (survivors reconciled, no soft-lock). Mirrors RaidVictoryController.BindRoutine.
        private IEnumerator BindScoringRoutine()
        {
            for (int i = 0; i < 10 && _scoring == null; i++)
            {
                _scoring = RaidScoring.Instance;
                if (_scoring != null) break;
                yield return null;
            }
            if (_scoring != null) SubscribeClock("bind routine");

            // WO-1437: arm the terminal-state net in the SAME place, and for the same reason,
            // the clock subscriber is bound here — before BuildHud, so no exit depends on
            // presentation succeeding (WO-1110 §1's load-bearing order).
            //
            // ⚠ ARMED OUTSIDE THE `_scoring != null` BLOCK, DELIBERATELY. The watchdog's
            // last-resort arm exists precisely to cover "RaidScoring never installed" — and it
            // names that case in its own failure text. Arming it inside the null-check would
            // mean the one scenario it advertises is the one scenario where it never starts,
            // which is a trace that lies (CLAUDE.md §11B). The coroutine handles a null scorer
            // on its own: it re-resolves RaidScoring.Instance each tick and falls back to
            // RaidScoring.DefaultClockSeconds for its backstop deadline.
            DeNelle.Core.Diagnostics.Guard.Try("Raid", "arm raid stranding watchdog",
                () => StartCoroutine(StrandingWatchdog()));
        }

        /// <summary>
        /// WO-1095 - the ONE place the clock-expiry subscriber is installed. Idempotent
        /// (<c>-=</c> then <c>+=</c>), so calling it twice cannot double-fire the retreat.
        ///
        /// <para>It exists because <see cref="BindScoringRoutine"/> gives up after ten frames.
        /// A scorer that self-installs later than that used to leave the raid permanently
        /// unsubscribed - the watchdog re-resolved <c>RaidScoring.Instance</c> every tick but
        /// never bound to it, so the ONE arm that ends a raid on time was never armed and only
        /// the net could get the player out. The watchdog now calls this on its late resolve.</para>
        /// </summary>
        private void SubscribeClock(string where)
        {
            if (_scoring == null || _clockSubscribed) return;
            _scoring.OnTimeExpired -= OnRaidTimeExpired;
            _scoring.OnTimeExpired += OnRaidTimeExpired;
            _clockSubscribed = true;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"raid clock subscriber installed ({where}) - OnTimeExpired -> retreat is armed.");
        }

        // =====================================================================
        //  WO-1437 — THE TERMINAL-STATE NET. Every raid session ends, full stop.
        // =====================================================================
        /// <summary>
        /// Unscaled seconds a SETTLED raid may still be standing in its own scene, WITH NO END
        /// STATE ON SCREEN, before this controller takes the route home back.
        ///
        /// <para>WO-1543 / WO-1561 - THE "COMFORTABLY LONGER THAN 12s" RULE THIS COMMENT USED TO
        /// STATE IS RETIRED, AND DELIBERATELY NOT REPLACED BY A BIGGER NUMBER. Both raid end
        /// states now HOLD while the player interacts, so a reading player can legitimately sit
        /// there forever and no constant here could outlast them. The countdown is instead
        /// suspended while <c>EndStateView.IsShowing</c> (see StrandingWatchdog), which is the
        /// honest condition: an end state on screen IS a live route home. Do not re-tune this
        /// number to chase a screen's dismiss value - that coupling is what went stale.</para>
        /// </summary>
        private const float SettledExitGraceSeconds = 30f;

        /// <summary>
        /// Extra unscaled seconds past the raid clock before the LAST-RESORT arm fires on a
        /// raid that never settled at all (a missing OnTimeExpired subscriber plus a failed
        /// HUD — the raid's one exitless state).
        /// </summary>
        private const float UnsettledBackstopGraceSeconds = 45f;

        // =====================================================================
        //  WO-1095 — THE WATCHDOG MUST MEASURE THE SAME INTERVAL THE CLOCK MEASURES
        // =====================================================================
        /// <summary>
        /// PROVEN DEFECT, 2026-09-09 (docs/READY_RCA_2026-09-09.md, "WO-1095"). The raid clock
        /// is ENGAGEMENT-GATED since WO-1520: <c>RaidScoring.Update</c> returns before
        /// <c>_elapsed += Time.deltaTime</c> until first contact, so staging is free. This
        /// watchdog measured something else entirely - <c>aliveFor</c>, unscaled seconds since
        /// the RAID SCENE loaded - and compared that scene age against <c>clock + 45</c>. Two
        /// different intervals, one bound.
        ///
        /// <para><b>Capture 1 (seq 4967).</b> Raid entered 17:15:42.851Z, watchdog fired
        /// 17:19:27.828Z: 224.977 s of SCENE age, exactly the 180+45 bound - while the owner's
        /// screenshot still read <b>2:46</b> remaining, i.e. 14 s of the 180 s raid had elapsed.
        /// The player was yanked out of a raid with 166 valid seconds left.</para>
        ///
        /// <para><b>Capture 2 (seq 4980).</b> <c>scene_loaded RaidBase_raider_camp_small</c> at
        /// <c>t=1036.537</c>, watchdog Fail at <c>t=1546.019</c> - 509.5 s of wall clock, and the
        /// line printed <c>510s</c>. It was NOT 510 s of raid: the settle line that followed in
        /// the same log reads <c>elapsed=50s/180s</c>. Same defect, one order of magnitude
        /// louder.</para>
        ///
        /// <para><b>THE FIX IS THE MEASUREMENT, NOT THE BOUND.</b> Nothing here is loosened: the
        /// 45 s grace is untouched and the arm's <c>Fail</c> severity is untouched (WO-1095's
        /// "do NOT raise the bound / downgrade the severity" holds). The watchdog now
        /// accumulates <c>engagedFor</c> - unscaled seconds since <c>RaidScoring.Engaged</c> -
        /// and bounds THAT.</para>
        ///
        /// <para><b>WHY UNSCALED AND NOT <c>ElapsedSeconds</c> ITSELF.</b> Same START as the
        /// clock (first engagement), deliberately different TICK. <c>_elapsed</c> advances on
        /// <c>Time.deltaTime</c>, so anything holding <c>timeScale</c> at 0 freezes it - and a
        /// net that reads a frozen number never fires, which converts today's premature exit
        /// into a permanent strand. That is not hypothetical: in seq 4980 the scaled clock was
        /// stuck at 50 s across ~455 s of engaged wall time. This file's own doctrine already
        /// says it ("UNSCALED throughout: a hold left at timeScale=0 by any other system must
        /// never become a new way to strand the player"), and
        /// <c>RaidTerminalStateRegression</c> Case C pins it.</para>
        /// </summary>
        public enum StrandingArm
        {
            /// <summary>Nothing to rescue.</summary>
            None = 0,
            /// <summary>Engaged, and the raid clock's own interval overran its grace.</summary>
            EngagedOverrun = 1,
            /// <summary>No RaidScoring in the scene at all - wall clock is the only measure left.</summary>
            ScorerMissing = 2,
            /// <summary>Still staging with NO HUD: the raid's one genuinely exitless state.</summary>
            StagingWithNoHud = 3,
            /// <summary>Still staging with a working HUD - the generous absolute ceiling.</summary>
            StagingCeiling = 4,
        }

        /// <summary>
        /// Tunable key for the absolute staging ceiling. Read through
        /// <c>RemoteTunables.SpecFor</c> so this call site answers
        /// <see cref="StagingCeilingSecondsDefault"/> until the row is registered, and goes live
        /// the moment it is - it never trips the "unregistered key" trace.
        /// </summary>
        public const string KeyRaidStagingCeilingSeconds = "raid.stagingCeilingSeconds";

        /// <summary>
        /// Unscaled seconds a player may stand in STAGING, with a working HUD (so Retreat is on
        /// screen), before the net takes the route home back.
        ///
        /// <para>⚠ THIS IS A CHOSEN NUMBER, NOT "today's value" - and it is written down as
        /// chosen because nothing today bounds staging at all except the 225 s scene-age bound
        /// that WO-1095 proved to be the false positive. 900 s is deliberately far outside any
        /// staging a player would sit through on purpose, so it can only catch a session that is
        /// genuinely dead. Tune it on the rail, never by editing this const.</para>
        /// </summary>
        public const int StagingCeilingSecondsDefault = 900;

        private static float StagingCeilingSeconds
        {
            get
            {
                var spec = DeNelle.Core.Ops.RemoteTunables.SpecFor(KeyRaidStagingCeilingSeconds);
                if (spec == null) return StagingCeilingSecondsDefault;
                return Mathf.Max(60f, DeNelle.Core.Ops.RemoteTunables.Int(KeyRaidStagingCeilingSeconds));
            }
        }

        /// <summary>
        /// The watchdog's decision, PURE so an oracle can assert it with no scene and no Unity
        /// play session. Every arm below is the same 45 s grace on a DIFFERENT interval; which
        /// interval is legitimate depends only on what actually exists in the scene.
        /// </summary>
        /// <param name="scoringPresent">Is there a live RaidScoring?</param>
        /// <param name="engaged">Has the raid clock started (first contact)?</param>
        /// <param name="aliveForSeconds">Unscaled seconds since the raid scene loaded.</param>
        /// <param name="engagedForSeconds">Unscaled seconds since first engagement.</param>
        /// <param name="clockSeconds">The raid clock.</param>
        /// <param name="graceSeconds"><see cref="UnsettledBackstopGraceSeconds"/>.</param>
        /// <param name="stagingCeilingSeconds"><see cref="StagingCeilingSeconds"/>.</param>
        /// <param name="hudBuilt">Did the deploy HUD build (is there a Retreat button)?</param>
        public static StrandingArm ClassifyStranding(
            bool scoringPresent, bool engaged,
            float aliveForSeconds, float engagedForSeconds,
            float clockSeconds, float graceSeconds, float stagingCeilingSeconds,
            bool hudBuilt)
        {
            float deadline = clockSeconds + graceSeconds;

            // No scorer at all. There is no engagement gate to respect because there is no
            // clock, so scene age IS the only interval - and this is the case the old text
            // named. It keeps the tight bound, because a raid with no scorer can never settle.
            if (!scoringPresent)
                return aliveForSeconds >= deadline ? StrandingArm.ScorerMissing : StrandingArm.None;

            if (engaged)
                return engagedForSeconds >= deadline ? StrandingArm.EngagedOverrun : StrandingArm.None;

            // STAGING. The clock has not started, so the player is not overdue by any measure
            // the game itself uses. Only two things can still be wrong here:
            if (!hudBuilt)
                return aliveForSeconds >= deadline ? StrandingArm.StagingWithNoHud : StrandingArm.None;

            return aliveForSeconds >= stagingCeilingSeconds ? StrandingArm.StagingCeiling : StrandingArm.None;
        }

        /// <summary>
        /// GUARANTEES A RAID SESSION REACHES A TERMINAL STATE. This is the general form the
        /// WO-1437 acceptance asks for, and the runtime twin of the oracle that pins it.
        ///
        /// <para><b>WHY IT HAS TO EXIST, proven by capture.</b> Until now the route home from a
        /// WON raid was owned solely by the victory <c>EndStateView</c> — both its primary
        /// action (Return to Castle) and its <c>AutoDismissSeconds</c> softlock guard live on
        /// that GameObject. Any other modal opening over it destroys BOTH at once. On
        /// 2026-09-06 that is exactly what happened (logs/debug/raid-stuck-2026-09-06.log):</para>
        /// <code>
        /// 13:02:42.214  RETURN - victory screen shown ...; tap or auto-dismiss routes to the castle.
        /// 13:02:47.276  'Victory!' destroyed WITHOUT firing its primary action - EndStateView.Show
        ///               - REPLACED by a new end-state 'YOU HAVE FALLEN'. That action is now abandoned.
        /// 13:02:47.284  'Victory!' destroyed WITHOUT firing its primary action - CloseFromArbiter
        ///               (another modal opened over this end-state).
        /// </code>
        /// <para>Five seconds into a twelve-second guard, the hero died (the raid world keeps
        /// simulating at <c>timeScale=1.00</c> behind the victory screen — measured, not assumed:
        /// every <c>[Flow:HeroOwner]</c> line through 13:04 reads <c>timeScale=1.00</c>), the death
        /// screen replaced the victory screen, and every route home died with it. The player was
        /// left inside a settled raid with only RETREAT — the losing exit — available after
        /// WINNING.</para>
        ///
        /// <para><b>THE PANEL DYING WITHOUT FIRING IS CORRECT</b> — a displaced end-state must
        /// never silently trigger a transition. The defect is that RAID OWNERSHIP was delegated
        /// to a view. This takes that ownership back, exactly as
        /// <c>BattleArena.StrandingWatchdog</c> already does for arenas (WO-969, whose comment
        /// records this same failure shape verbatim). Raids never got the same net; now they have
        /// one, and it covers all four exits — victory, retreat, clock and hero death — from a
        /// single seam rather than four exit-specific patches.</para>
        ///
        /// <para>Safe by construction: <c>ReconcileRaidEnd</c> is latched on <c>_reconciled</c>,
        /// <c>SettlePartialLoot</c> early-returns on <c>RaidScoring.Finalized</c>, and
        /// <c>RaidVictoryController.ReturnHome</c> latches on <c>_returning</c>. A normal exit
        /// therefore makes this a logged no-op, and this firing can never double-pay a raid.</para>
        ///
        /// <para>UNSCALED throughout: a hold left at <c>timeScale=0</c> by any other system must
        /// never become a new way to strand the player — that is the whole point of a net.</para>
        /// </summary>
        private IEnumerator StrandingWatchdog()
        {
            float settledFor = 0f;
            float aliveFor = 0f;
            float engagedFor = 0f;   // WO-1095: the SAME interval the raid clock measures.

            while (true)
            {
                yield return null;

                // The scene changed -> the session reached a terminal state by a normal exit.
                // Nothing to rescue; stand down quietly (this is the overwhelmingly common path).
                if (!DeNelle.Core.HubScenes.IsRaid(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                    yield break;

                float dt = Time.unscaledDeltaTime;
                aliveFor += dt;

                if (_scoring == null)
                {
                    _scoring = RaidScoring.Instance;
                    // WO-1095: BindScoringRoutine gives up after ten frames. A scorer that
                    // installs later than that used to be re-resolved here and then never
                    // subscribed, so the raid's on-time exit stayed unarmed for the whole
                    // session and this net was the only way out. Bind it here too.
                    if (_scoring != null)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                            $"RaidScoring resolved LATE ({aliveFor:0.0}s into the raid scene) - " +
                            "BindScoringRoutine's 10-frame poll had already given up. Subscribing " +
                            "the clock-expiry exit from the watchdog so the raid still ends on time.");
                        SubscribeClock("watchdog late resolve");
                    }
                }

                if (_scoring != null && _scoring.Engaged) engagedFor += dt;

                bool settled = _scoring != null && _scoring.Finalized;
                if (settled)
                {
                    // WO-1543 / WO-1561 - THE NET MUST NOT BECOME THE NEW UNSTOPPABLE TIMER.
                    // The owner ruled that a raid end state HOLDS while the player interacts
                    // (EndStateVM.HoldOnInteraction), which means a reading player has UNLIMITED
                    // time - so no fixed grace is "comfortably longer" any more, and simply
                    // raising the number would only move the yank later. While an end state is on
                    // screen the route home is demonstrably ALIVE (it is that screen's primary
                    // action and its re-arming guard), so there is nothing to rescue: the clock
                    // resets. This makes the watchdog TIGHTER, not looser - it now fires 30s after
                    // the last end state CLOSED with the player still standing in a raid scene,
                    // which is precisely "the route home was eaten".
                    if (DeNelle.Village.UI.EndStateView.IsShowing)
                    {
                        settledFor = 0f;
                        continue;
                    }

                    settledFor += dt;
                    if (settledFor < SettledExitGraceSeconds) continue;

                    // Section 12: never silent. This firing means the route home was eaten.
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                        $"RAID STRANDING WATCHDOG FIRED - the raid SETTLED {settledFor:0}s ago and the " +
                        "player is STILL standing in the raid scene, so whatever owned the route home " +
                        "(the victory end-state's primary action and its auto-dismiss, or the death " +
                        "evac) never ran. Routing home anyway. If you are reading this, find WHAT ate " +
                        "the exit - look for a nearby \"destroyed WITHOUT firing its primary action\" " +
                        "warn. The watchdog is a safety net, NOT the fix (WO-1437).");
                    ForceExitHome("settled-raid stranding watchdog");
                    yield break;
                }

                // LAST-RESORT arms: a raid that never settled at all. WO-1095 - the interval
                // bounded here is the one the RAID CLOCK measures (engaged time), not the age
                // of the scene, because staging is deliberately free (WO-1520).
                float clock = _scoring != null ? _scoring.ClockSeconds : RaidScoring.DefaultClockSeconds;
                bool engaged = _scoring != null && _scoring.Engaged;
                var arm = ClassifyStranding(
                    _scoring != null, engaged, aliveFor, engagedFor,
                    clock, UnsettledBackstopGraceSeconds, StagingCeilingSeconds, _hudBuilt);
                if (arm == StrandingArm.None) continue;

                // STATE the facts; never accuse a subscriber this controller can see is bound.
                // Every one of these was unknown to the reader of the old line, and each of them
                // discriminates a different upstream defect (CLAUDE.md sec.11B / sec.12).
                string state =
                    $"arm={arm} aliveFor={aliveFor:0}s engagedFor={engagedFor:0}s " +
                    $"clock={clock:0}s grace={UnsettledBackstopGraceSeconds:0}s " +
                    $"stagingCeiling={StagingCeilingSeconds:0}s " +
                    $"scoring={(_scoring != null ? "present" : "MISSING")} " +
                    $"engaged={engaged} reason='{(_scoring != null ? _scoring.EngagedReason : "n/a")}' " +
                    $"clockElapsed={(_scoring != null ? _scoring.ElapsedSeconds.ToString("0.0") : "n/a")}s " +
                    $"subscriber={(_clockSubscribed ? "INSTALLED" : "MISSING")} " +
                    $"hudBuilt={_hudBuilt} timeScale={Time.timeScale:0.00}";

                string diagnosis;
                switch (arm)
                {
                    case StrandingArm.ScorerMissing:
                        diagnosis = "There is NO RaidScoring in this scene, so there is no clock and " +
                                    "no on-time exit at all - scene age was the only interval left to " +
                                    "measure. Find why the scorer never self-installed.";
                        break;
                    case StrandingArm.StagingWithNoHud:
                        diagnosis = "The player never engaged AND the deploy HUD failed to build, so " +
                                    "there is no Retreat button and no clock ticking - the raid's one " +
                                    "genuinely exitless state. Find why BuildHud threw.";
                        break;
                    case StrandingArm.StagingCeiling:
                        diagnosis = "The player never engaged at all within the absolute staging " +
                                    "ceiling. The HUD built, so Retreat WAS on screen; this is a dead " +
                                    "session, not an overrun raid. Tune the ceiling on '" +
                                    KeyRaidStagingCeilingSeconds + "', never in code.";
                        break;
                    default:
                        diagnosis = "The raid clock's OWN interval overran its grace without the clock " +
                                    "ever expiring. If clockElapsed above is far BELOW engagedFor, the " +
                                    "scaled clock is being held (RaidScoring advances _elapsed on " +
                                    "Time.deltaTime) - find what is holding timeScale. If it is NOT " +
                                    "below, OnTimeExpired fired and its subscriber did not end the raid.";
                        break;
                }

                DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                    "RAID STRANDING WATCHDOG FIRED (last-resort arm) - a raid that NEVER finalized, so " +
                    "neither the objective, the clock expiry nor Retreat ended this session (WO-1526: " +
                    "hero death is no longer an exit - the army fights on and the raid ends by objective, " +
                    "Retreat or the clock). Routing home anyway. " + diagnosis +
                    " The watchdog is a safety net, NOT the fix (WO-1437). STATE: " + state);
                ForceExitHome("unsettled-raid last-resort watchdog");
                yield break;
            }
        }

        /// <summary>
        /// Settle whatever is still unsettled, then leave. Ordered exactly like
        /// <see cref="DoRetreat"/> (loot before army reconcile) so the watchdog exit pays what
        /// every other exit pays; both calls are latched, so a raid already settled by the
        /// victory path is untouched and this reduces to the route home alone.
        /// </summary>
        private void ForceExitHome(string reason)
        {
            DeNelle.Core.Diagnostics.Guard.Try("Raid", reason + ": settle partial loot",
                () => SettlePartialLoot(reason));
            DeNelle.Core.Diagnostics.Guard.Try("Raid", reason + ": reconcile army",
                () => ReconcileRaidEnd(0));

            // Clear the runtime enemy-owned flag before leaving so the home hub never inherits
            // a stale read from this raid (mirrors RaidVictoryController.ReturnHome).
            DeNelle.Core.Diagnostics.Guard.Try("Raid", reason + ": clear scene ownership",
                () => SceneOwnership.SetEnemyOwned(false));
            DeNelle.Core.State.GameStateService.Instance?.Save();
            DeNelle.Core.SceneRouter.GoCastle();
        }

        // The raid clock ran out (RaidScoring.OnTimeExpired): call off the assault and
        // evac through the normal retreat (reconciles survivors/wounded, GoCastle).
        private void OnRaidTimeExpired()
        {
            SetStatus("Time! The assault is called off - your warband retreats.");
            // WO-1561: the SAME settlement and the SAME screen as a chosen retreat - only the
            // reason differs, and the result screen leads with a sentence that says which exit
            // this was. A player who ran out of clock did not choose to leave.
            DoRetreat(DeNelle.Village.UI.EndStateVM.TimeoutReason);
        }

        private void Update()
        {
            if (_camera == null) { _camera = Camera.main; if (_camera == null) return; }

            // Read the place/rally tap THIS frame (new Input System mouse, or a Lean tap).
            if (!TryReadTapPoint(out Vector2 screenPoint)) return;

            if (_rallyMode) { HandleRallyTap(screenPoint); return; }
            if (!string.IsNullOrEmpty(_armedDefId)) { HandleDeployTap(screenPoint); return; }
        }

        // =====================================================================
        //  Input — new Input System mouse + optional Lean tap (NO legacy Input.*)
        // =====================================================================

        /// <summary>
        /// True for the single frame a deploy/rally tap is confirmed, with the screen
        /// point of the tap. Reads the new Input System mouse (left-click) and a Lean
        /// touch tap latch (mobile). A tap over a UI element (the tray / buttons) is
        /// rejected so a tile/Rally/Retreat press never also drops a troop behind it.
        /// </summary>
        private bool TryReadTapPoint(out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;

            if (_leanTapLatched)
            {
                _leanTapLatched = false;
                screenPoint = _leanTapPoint;
                return !IsPointerOverUi(screenPoint);
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPoint = mouse.position.ReadValue();
                return !IsPointerOverUi(screenPoint);
            }
            return false;
        }

        /// <summary>
        /// True when a screen point is over one of THIS HUD's interactable graphics (the
        /// tray tiles / Rally / Retreat). Uses the canvas GraphicRaycaster so a tap meant
        /// for a button never falls through to a ground deploy. Null-safe.
        /// </summary>
        private bool IsPointerOverUi(Vector2 screenPoint)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            var data = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPoint };
            var hits = new List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(data, hits);
            foreach (var h in hits)
                if (h.gameObject != null && _ui != null && h.gameObject.transform.IsChildOf(_ui.transform))
                    return true;
            return false;
        }

        /// <summary>
        /// Cursor/finger → ground raycast (the BuildModeController.RaycastGround pattern):
        /// the configured ground mask first, then all layers as a fallback so a scene whose
        /// ground sits on an unexpected layer still resolves a hit.
        /// </summary>
        private bool RaycastGround(Vector2 screenPoint, out RaycastHit hit)
        {
            Ray ray = _camera.ScreenPointToRay(screenPoint);
            if (Physics.Raycast(ray, out hit, _rayDistance, _groundMask)) return true;
            return Physics.Raycast(ray, out hit, _rayDistance, ~0);
        }

        // =====================================================================
        //  DEPLOY
        // =====================================================================

        private void HandleDeployTap(Vector2 screenPoint)
        {
            if (!RaycastGround(screenPoint, out RaycastHit hit))
            {
                return;
            }

            // THE NAVMESH GATE (defect sweep 2026-08-15). RaycastGround falls back to ~0 - ALL
            // layers - so a tap on a rooftop, a cliff face, a decorative mesh or the skirt
            // terrain outside the base resolves a perfectly good RaycastHit that is nowhere
            // near walkable ground. Nothing tested that, so the drop went through to
            // TroopFactory, whose SamplePosition then failed and SPAWNED ANYWAY behind a
            // Debug.LogWarning F8 never saw. The result was an INERT troop: no path, no
            // fight, no death - and at reconcile it is alive, so it counts as a SURVIVOR,
            // lifting RaidScoring.SurvivalPct past the 70% high-survival axis. Deploying
            // troops onto scenery literally BOUGHT 3-star clears (and, since victory pays
            // veterancy at 3 stars, promoted the whole warband with it).
            //
            // Fixed at the INPUT, not in the scoring math: the tap is REFUSED, with a
            // player-visible tell, and the army is untouched - no troop is consumed, the tile
            // stays armed, the player just taps somewhere valid. Same radius the factory would
            // have snapped within, so this refuses exactly the taps it could not have placed.
            if (!UnityEngine.AI.NavMesh.SamplePosition(hit.point, out UnityEngine.AI.NavMeshHit navHit,
                                                       TroopFactory.NavSampleRadius, UnityEngine.AI.NavMesh.AllAreas))
            {
                SetStatus("Can't deploy there - tap open ground inside the base.");
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    $"DEPLOY REFUSED - tap resolved to {hit.point} on '{(hit.collider != null ? hit.collider.name : "?")}', " +
                    $"which has no baked NavMesh within {TroopFactory.NavSampleRadius}m. Spawning here would " +
                    "produce an inert troop that never fights and still counts as a survivor at reconcile " +
                    $"(inflating SurvivalPct past the {RaidScoring.HighSurvivalPct * 100f:0}% 3-star axis). " +
                    "No troop consumed; the tile stays armed.");
                return;
            }

            var army = Army();
            if (army == null) { SetStatus("No army to deploy."); return; }

            // The next deployable troop of the armed type (healthy, not already deployed).
            PlayerTroop next = NextDeployableOfType(army, _armedDefId);
            if (next == null)
            {
                SetStatus($"No more {DisplayName(_armedDefId)} ready to deploy.");
                Disarm();
                RefreshTiles();
                return;
            }

            // Spread repeated drops of the same tap-target out around a small ring. The drop
            // uses the SNAPPED point (navHit.position), not the raw raycast hit: the gate above
            // proved walkable mesh within reach, so this places the body ON it rather than
            // relying on a second snap downstream.
            Vector3 deployPoint = navHit.position;
            int stackIndex = CountDeployedOfType(_armedDefId);
            var troop = TroopDeployer.SpawnFromArmy(next, deployPoint, stackIndex, _deploySpread);
            if (troop == null)
            {
                SetStatus($"Couldn't deploy {DisplayName(_armedDefId)}.");
                return;
            }

            _deployed.Add(new Deployed { Controller = troop, OwnedId = next.Id });
            // Feed the scorer (WO-771.6): count the deploy + log it for re-watch. Null-safe.
            RaidScoring.Instance?.RecordDeploy(_armedDefId, deployPoint);
            RefreshTiles();
        }

        // The next army troop of this def that is deployable AND not already on the field.
        private PlayerTroop NextDeployableOfType(ArmyStorage army, string defId)
        {
            if (army == null || army.Owned == null || string.IsNullOrEmpty(defId)) return null;
            foreach (var t in army.Owned)
            {
                if (t == null || !t.IsDeployable) continue;
                if (t.TroopDefId != defId) continue;
                if (IsDeployed(t.Id)) continue;
                return t;
            }
            return null;
        }

        private bool IsDeployed(string ownedId)
        {
            if (string.IsNullOrEmpty(ownedId)) return false;
            foreach (var d in _deployed)
                if (d.OwnedId == ownedId) return true;
            return false;
        }

        private int CountDeployedOfType(string defId)
        {
            int n = 0;
            foreach (var d in _deployed)
                if (d.Controller != null && d.Controller.TroopId == defId) n++;
            return n;
        }

        // Remaining deployable (in the army, healthy, not yet on the field) of a def.
        private int RemainingOfType(ArmyStorage army, string defId)
        {
            if (army == null || army.Owned == null) return 0;
            int n = 0;
            foreach (var t in army.Owned)
            {
                if (t == null || !t.IsDeployable) continue;
                if (t.TroopDefId != defId) continue;
                if (IsDeployed(t.Id)) continue;
                n++;
            }
            return n;
        }

        // =====================================================================
        //  RALLY
        // =====================================================================

        private void HandleRallyTap(Vector2 screenPoint)
        {
            if (!RaycastGround(screenPoint, out RaycastHit hit))
            {
                return;
            }
            TroopRally.Point = hit.point;
            ShowRallyFlag(hit.point);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"rally point moved to {hit.point} — warband musters there.");
            SetStatus("Rally set — idle troops will muster there.");
        }

        // =====================================================================
        //  RALLY FLAG — a visible muster marker dropped/moved at the rally point
        // =====================================================================
        // WO-457: TroopRally.Point is data only; the player needs to SEE where the
        // warband is rallying. No flag prefab exists, so we build a cheap primitive
        // marker (a thin pole + a gold banner quad) and just MOVE it on each rally
        // tap. Non-colliding (the troops path THROUGH the point), null-safe.
        //
        // ⛔ WO-1463 — WHY EVERY PRIMITIVE HERE MUST BE GIVEN A MATERIAL EXPLICITLY.
        // GameObject.CreatePrimitive assigns Unity's BUILT-IN `Default-Material`, which
        // has no URP variant. In the editor it looks fine; in a URP PLAYER BUILD it falls
        // to the magenta error shader, and setting `.color` on it changes nothing — the
        // owner's capture (Logs/device/screens/owner-raid-ui-2026-09-06-143701.png) shows
        // the rally flag as a magenta block for exactly that reason. The cure is the
        // project's ONE runtime material authority, DeNelle.Core.MagentaGuard:
        // BuildUrpLitMaterial resolves URP/Lit and, when the shader was stripped from the
        // build, BORROWS it off a live scene material rather than degrading to Standard
        // (MagentaGuard.cs:814-869). It returns NULL rather than lying, so the caller
        // leaves the renderer alone and WARNS — a silent fallthrough here is what §12
        // forbids. Same pattern as Editor/WallTools/RaidBaseGenerator.ApplyUrpMaterial and
        // Village/World/DungeonWorldPortalSpawner.BuildArch.
        //
        // ⛔ AND `ProtectPrimitiveArt` IS NOT OPTIONAL. MagentaGuard's scene sweep treats a
        // built-in primitive mesh as a stray placeholder and DISABLES it
        // (MagentaGuard.IsPrimitivePlaceholder, :864). Deliberate primitive art must
        // register, or the fix for the magenta flag becomes an invisible flag.
        // Pinned by RaidSelectionLayoutRegression case S8:no-bare-primitive.
        private GameObject _rallyFlag;

        private void ShowRallyFlag(Vector3 groundPoint)
        {
            if (_rallyFlag == null) _rallyFlag = BuildRallyFlag();
            if (_rallyFlag == null) return;
            _rallyFlag.transform.position = groundPoint;
            _rallyFlag.SetActive(true);
        }

        private GameObject BuildRallyFlag()
        {
            try
            {
                var root = new GameObject("RallyFlag");

                // Thin pole (cylinder, ~2.4m tall). Strip the collider so it never
                // blocks deploy/rally raycasts or troop pathing.
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = "Pole";
                pole.transform.SetParent(root.transform, false);
                pole.transform.localScale = new Vector3(0.08f, 1.2f, 0.08f);
                pole.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                StripCollider(pole);
                ApplyUrpMaterial(pole, PoleWood);

                // Gold banner quad near the top of the pole.
                var banner = GameObject.CreatePrimitive(PrimitiveType.Quad);
                banner.name = "Banner";
                banner.transform.SetParent(root.transform, false);
                banner.transform.localScale = new Vector3(0.9f, 0.6f, 1f);
                banner.transform.localPosition = new Vector3(0.45f, 2.0f, 0f);
                StripCollider(banner);
                ApplyUrpMaterial(banner, BannerGilt);

                // Deliberate primitive art -> register, or the MagentaGuard sweep hides it.
                MagentaGuard.ProtectPrimitiveArt(root, "RaidDeployController.BuildRallyFlag");

                return root;
            }
            catch (System.Exception e)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid", "rally flag build failed: " + e.Message);
                return null;
            }
        }

        private static void StripCollider(GameObject go)
        {
            var col = go != null ? go.GetComponent<Collider>() : null;
            if (col != null) Destroy(col);
        }

        /// <summary>
        /// The flag's two tones. NEITHER IS A NEW CREATIVE CHOICE (the owner is colourblind —
        /// memory `owner-colorblind-delegate-visual-creative`): the banner takes the kit's own
        /// canonical gilt, <see cref="ElarionUi.Gilt"/> (#eec848, Core/UI/ElarionUi.cs:60), the
        /// same gold the obsidian chrome trims every panel with; the pole keeps the dark-wood
        /// literal this marker has always shipped with. WO-1463 changes HOW the colour is
        /// applied (a real URP material instead of a tint on the built-in default), not WHICH
        /// colour it is.
        /// </summary>
        private static readonly Color PoleWood   = new Color(0.32f, 0.22f, 0.12f);
        private static readonly Color BannerGilt = ElarionUi.Gilt;

        /// <summary>
        /// Assigns a REAL URP material through the project's one runtime material authority.
        /// Never tints the built-in Default-Material (WO-1463: that is the magenta path), and
        /// never silently no-ops — a null material is the stripped-shader case and it WARNS,
        /// leaving the renderer untouched rather than forcing magenta (MagentaGuard's own
        /// contract, MagentaGuard.cs:843-848).
        /// </summary>
        private static void ApplyUrpMaterial(GameObject go, Color c)
        {
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) return;
            var m = MagentaGuard.BuildUrpLitMaterial(c);
            if (m != null) { r.sharedMaterial = m; return; }
            DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                "rally flag: no URP/Lit shader resolvable for '" + go.name +
                "' - leaving its renderer alone rather than forcing the built-in default.");
        }

        private void ToggleRally()
        {
            _rallyMode = !_rallyMode;
            if (_rallyMode) Disarm();   // rally + deploy are exclusive arm states
            RefreshRallyButton();
        }

        // =====================================================================
        //  RETREAT — the loss/exit path (win/stars are out of first-playable scope)
        // =====================================================================

        private void OnRetreatPressed()
        {
            if (_retreatConfirm && !_retreatPending)
            {
                _retreatPending = true;
                SetStatus("Retreat? Tap again to confirm — survivors come home, the fallen recover.");
                if (_retreatButton != null)
                {
                    var lbl = _retreatButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    if (lbl != null) lbl.text = "Confirm Retreat";
                }
                return;
            }
            // WO-1561: ONE DoRetreat, taking the exit reason. A parameterless forwarder was
            // written here first and deliberately removed: RaidExitParityRegression PIN 1 locates
            // this method by SIGNATURE, so a one-line forwarder would have handed the oracle an
            // empty body and quietly disarmed every exit-parity assertion on it. A lint that
            // matches the wrong overload passes while the contract rots - the failure mode this
            // repo has paid for repeatedly.
            DoRetreat(DeNelle.Village.UI.EndStateVM.RetreatReason);
        }

        /// <summary>
        /// =====================================================================
        ///  WO-1561 - THE NON-VICTORY EXIT NOW REPORTS. P0.
        /// =====================================================================
        ///
        /// <para><b>WHAT THIS METHOD USED TO DO, IN FULL:</b> settle the partial loot, reconcile
        /// the army, clear the rally, save, set a status string and call
        /// <c>SceneRouter.GoCastle()</c>. <b>No <c>EndStateVM</c>. No <c>EndStateView.Show</c>.</b>
        /// Measured: <c>grep -c "EndStateVM\.|EndStateView.Show"</c> on this file returned 1, and
        /// that single hit was a COMMENT. The timeout exit funnels here too
        /// (<see cref="OnRaidTimeExpired"/>).</para>
        ///
        /// <para><b>AND NOTHING PICKED IT UP IN TOWN EITHER</b> - that was checked, because
        /// "deferred to town" would have downgraded the finding. Every reader of
        /// <c>RaidResult</c> is raid-scene-side; the "welcome back" surfaces are the
        /// offline-harvest popups, which never mention a raid. So the outcome was computed,
        /// BANKED, and then discarded unread: a player who retreated - or simply ran out the
        /// clock - was teleported into town having earned real loot and possibly a star, and was
        /// told none of it, while a WIN got the full treatment. That is the exit a new player is
        /// most likely to finish (memory retention-is-the-business-problem).</para>
        ///
        /// <para>NO NEW SCREEN: it reuses the ONE shared <c>EndStateView</c> template the victory
        /// takes, so the two exits cannot drift in shape or in timing. The route home is the
        /// screen's single primary action AND its re-armable auto-dismiss guard, exactly as the
        /// victory's is - and <see cref="StrandingWatchdog"/> is the non-view net behind both, so
        /// no exit is owned only by a destroyable view (WO-1437).</para>
        ///
        /// <para>ORDER IS UNCHANGED AND MUST STAY UNCHANGED: loot settles BEFORE the army
        /// reconcile, because <c>Finalize</c> samples destruction and survival off the LIVE field
        /// (RaidExitParityRegression PIN 1 pins this order across retreat and hero death).</para>
        /// </summary>
        /// <param name="reason">EndStateVM.RetreatReason or EndStateVM.TimeoutReason.</param>
        private void DoRetreat(string reason)
        {
            SettlePartialLoot(reason);

            // A retreat / clock-expiry exit is never a 3-star clear -> 0 stars, no veterancy.
            ReconcileRaidEnd(0);

            TroopRally.Clear();
            GameStateService.Instance?.Save();
            SetStatus(string.Equals(reason, DeNelle.Village.UI.EndStateVM.TimeoutReason,
                                    System.StringComparison.OrdinalIgnoreCase)
                ? "Time! Falling back to the castle..."
                : "Retreating to the castle...");

            ShowNonVictoryResult(reason);
        }

        /// <summary>
        /// WO-1526 — THE HERO FELL AND THE RAID DOES NOT END. Owner ruling 2026-09-06, verbatim:
        /// <i>"Do not let hero death instantly terminate the raid... let the raid continue, but
        /// cap the result at 2 stars if the hero dies. That makes hero survival matter without
        /// turning the hero into a giant red self-destruct button."</i>
        ///
        /// <para><b>WHAT THIS DELIBERATELY DOES NOT DO — and each omission is the ticket:</b> it
        /// does not call <see cref="SettlePartialLoot"/> (WO-1526 sec.3: <i>"Do not settle loot at
        /// the moment of death. Loot settles when the raid actually ends"</i>), does not call
        /// <see cref="ReconcileRaidEnd"/>, does not show an end state, does not route home, and
        /// does not respawn the hero. The raid's exits stay exactly the four it already had —
        /// objective, Retreat, clock, and <see cref="StrandingWatchdog"/> — and every one of them
        /// still settles through the same shared authority, so the WO-1110 parity this ticket
        /// touches is untouched.</para>
        ///
        /// <para>All it does is LATCH the death on the scorer (the star ceiling) and say so on
        /// screen. Raid session ownership lives on this controller, never on a view — the same
        /// rationale <see cref="StrandingWatchdog"/> is written to.</para>
        ///
        /// <para>Idempotent by construction: <c>RaidScoring.NotifyHeroDied</c> latches, and the
        /// local latch keeps a second call from re-writing the status line over whatever the
        /// player is doing next.</para>
        /// </summary>
        public void NotifyHeroDown()
        {
            if (_heroDownAcknowledged) return;
            _heroDownAcknowledged = true;

            if (_scoring == null) _scoring = RaidScoring.Instance;
            if (_scoring != null) _scoring.NotifyHeroDied();
            else
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "NotifyHeroDown: no RaidScoring instance to latch the hero death on, so this " +
                    "raid CANNOT apply the 2-star cap (WO-1526). The raid still continues; the " +
                    "result will over-pay. If you are reading this, the scorer failed to install.");

            SetStatus(DeNelle.Village.UI.EndStateVM.HeroDownArmyFightsOn, HeroDownToastLifeSeconds);

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "hero DOWN - the raid CONTINUES and the army fights on (WO-1526). No loot settled, " +
                "no army reconciled, no route home: the raid ends by objective, Retreat or the " +
                $"clock as it always did, and settles at most {RaidScoring.HeroDeathStarCap} star(s). " +
                $"deployed={(_scoring != null && _scoring.DeployLog != null ? _scoring.DeployLog.Count : -1)} " +
                $"scorer={(_scoring != null ? "present" : "MISSING")}.");
        }

        /// <summary>
        /// WO-1561 - build and show the result screen, then leave through IT rather than
        /// straight past it.
        ///
        /// <para>EVERY NUMBER IS READ OFF THE SETTLED STATE, NEVER OFF AN ESTIMATE: stars, razed
        /// % and the clock come from <c>RaidScoring.Result</c> (the object
        /// <see cref="SettlePartialLoot"/> just settled), and the spoils come from the MEASURED
        /// wallet delta <see cref="GrantRetreatLoot"/> captured either side of the grant. WO-1461
        /// records why that matters: the deploy card quoted ~1,800 wood and 25 arrived, because
        /// the bank was full. This screen reports the 25, and says so when the bank ate the rest.</para>
        ///
        /// <para>A PRESENTATION FAILURE MUST NEVER STRAND THE PLAYER - the same contract
        /// <c>RaidVictoryController.ShowVictoryScreen</c> keeps: if the screen throws, the route
        /// home fires directly.</para>
        /// </summary>
        /// <summary>Latched: the non-victory result is shown at most ONCE per raid.</summary>
        private bool _nonVictoryShown;

        private void ShowNonVictoryResult(string reason)
        {
            // LATCHED, and not defensively. Both settlement calls in DoRetreat are already
            // idempotent, so a second exit reaching here is a harmless no-op for the ECONOMY - but
            // it would call EndStateView.Show a second time, and Show DESTROYS the standing panel
            // with a "destroyed WITHOUT firing its primary action" Warn. Warns are F8-captured, so
            // an unlatched re-show would manufacture triage noise from a non-bug. Mirrors the
            // _handled / _reconciled / _returning latches every other raid exit already carries.
            if (_nonVictoryShown)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "non-victory result already shown for this raid (" + reason + ") - ignoring the " +
                    "duplicate call rather than replacing the standing screen.");
                return;
            }
            _nonVictoryShown = true;

            try
            {
                var result = _scoring != null ? _scoring.Result : null;

                var vm = DeNelle.Village.UI.EndStateVM.FromRaidRetreat(
                    reason, RetreatReturnHome, NonVictoryReturnSeconds,
                    result != null ? result.Stars : -1,
                    result != null ? result.DestructionPercent : -1,
                    result != null ? result.ElapsedSeconds : -1f,
                    _retreatCredited, _retreatRewardShort,
                    _lastDeployedCount, _lastSurvivorCount);

                DeNelle.Village.UI.EndStateView.Show(vm);

                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "NON-VICTORY RESULT shown (" + reason + "): stars=" +
                    (result != null ? result.Stars.ToString() : "unknown") + " razed=" +
                    (result != null ? result.DestructionPercent + "%" : "unknown") +
                    " banked=" + _retreatCredited.Wood + "w/" + _retreatCredited.Iron + "i/" +
                    _retreatCredited.Food + "f/" + _retreatCredited.Coins + "g/" +
                    _retreatCredited.Crystals + "c short=" + _retreatRewardShort +
                    " deployed=" + _lastDeployedCount + " survived=" + _lastSurvivorCount +
                    ". Before WO-1561 this exit routed home with NO screen at all.");
            }
            catch (System.Exception e)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                    "non-victory result screen build threw - returning home directly: " + e.Message);
                RetreatReturnHome();
            }
        }

        /// <summary>
        /// The route home the result screen owns, latched so the button and the auto-dismiss
        /// guard can never both fire it.
        /// </summary>
        private bool _retreatReturned;
        private void RetreatReturnHome()
        {
            if (_retreatReturned) return;
            _retreatReturned = true;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "NON-VICTORY RETURN -> SceneRouter.GoCastle() (the loop continues; no soft-lock).");
            GameStateService.Instance?.Save();
            SceneOwnership.SetEnemyOwned(false);
            SceneRouter.GoCastle();
        }

        /// <summary>
        /// The non-victory screen's anti-softlock guard, in seconds. Matches the victory
        /// screen's WO-1543 value on purpose: the timing rule that ticket landed applies to BOTH
        /// raid end states, and two raid screens on two different clocks is exactly the drift
        /// this repo keeps paying for. Re-armed by any interaction
        /// (<c>EndStateVM.HoldOnInteraction</c>), so a reading player is never yanked.
        /// </summary>
        private const float NonVictoryReturnSeconds = 30f;

        // WO-1561 - the MEASURED credit from this raid's partial-loot grant, kept so the result
        // screen can state what was BANKED rather than what was awarded. Same contract, same
        // shape and the same reason as RaidVictoryController._credited / _rewardShort (WO-978):
        // at a capped town bank the two differ, and the screen is what the player believes.
        private ResourceCost _retreatCredited;
        private bool _retreatRewardShort;

        // WO-1561 - the army outcome of THIS raid, captured where it is already computed
        // (ReconcileRaidEnd walks the deploy ledger, the only place the deployed set exists,
        // because a fallen troop's body is destroyed seconds after death and a scene scan would
        // find survivors only). -1 = never reconciled, and the screen then omits the line rather
        // than printing a zero it cannot prove.
        private int _lastDeployedCount = -1;
        private int _lastSurvivorCount = -1;

        /// <summary>
        /// THE ONE partial-loot settlement, shared by EVERY non-victory raid exit
        /// (WO-932 for retreat/timeout; WO-1110 §3 adds hero death).
        ///
        /// WO-932 next set: settle score + partial loot BEFORE army reconcile / leave.
        /// Victory path grants loot in RaidVictoryController; retreat/timeout used to
        /// skip Finalize entirely so a half-razed base paid nothing and left scorer open.
        ///
        /// BUG THIS CLOSES (WO-1110 §3): hero death reconciled the army but NEVER called
        /// Finalize/LootFor, so dying forfeited razing credit that retreating paid — the
        /// exact inverse of the perverse incentive the retreat-loot block was written to
        /// remove, punishing the more committed play. Owner default (unruled, stated in the
        /// WO): death pays the SAME partial loot as retreat, because the loot is credit for
        /// damage already done. Both exits now call THIS method, so they cannot drift apart.
        ///
        /// Idempotent via <c>RaidScoring.Finalized</c>: whichever exit lands first settles,
        /// the rest are logged no-ops — a raid can never be paid twice.
        /// </summary>
        /// <param name="exitLabel">Which exit is settling ("retreat" / "hero death") — trace only.</param>
        public void SettlePartialLoot(string exitLabel)
        {
            if (_scoring == null) _scoring = RaidScoring.Instance;
            if (_scoring == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    $"{exitLabel} settle: no RaidScoring in the scene - no partial loot to pay.");
                return;
            }
            if (_scoring.Finalized)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"{exitLabel} settle: raid already finalized - loot was paid by the first exit.");
                return;
            }

            RaidResult result = _scoring.Finalize(false);
            ResourceCost loot = _scoring.LootFor(result);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"{exitLabel} settle: partial loot for {result?.DestructionPercent ?? 0}% razed.");
            GrantRetreatLoot(loot, result);
        }

        /// <summary>
        /// WO-932: partial loot on retreat/timeout/death (stars may still be 1 from >=50% razed).
        ///
        /// <para>WO-1561 - IT NOW MEASURES WHAT THE WALLET ACTUALLY TOOK. This used to grant and
        /// walk away, logging the REQUESTED amounts ("+{loot.Crystals}c") as though they had
        /// landed. Raid loot is EarnedIncome, which TownBankCapacity clamps against the town
        /// storage ceiling, so at a full bank the request and the credit differ - and until now
        /// nothing on this exit could tell them apart, because nothing on this exit was shown to
        /// the player at all. The before/after read is the same measurement
        /// <c>RaidVictoryController.GrantLoot</c> takes (the WO-978 contract): the wallet
        /// properties read straight through to the single GameState-backed store, so a delta is a
        /// real measurement and not a derivation.</para>
        ///
        /// <para>Instance, not static, because the measurement has to reach the result screen.
        /// It writes <see cref="_retreatCredited"/> / <see cref="_retreatRewardShort"/> and
        /// NOTHING ELSE reads or writes those two fields.</para>
        /// </summary>
        private void GrantRetreatLoot(ResourceCost loot, RaidResult result)
        {
            int stars = result != null ? result.Stars : 0;
            _retreatCredited = default(ResourceCost);
            _retreatRewardShort = false;

            if (loot.IsZero)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"retreat settle: stars={stars} loot=0 (no grant). The result screen will draw " +
                    "no spoils rows, which is honest - a raid that banked nothing must never " +
                    "advertise a payout.");
                return;
            }

            var eco = EconomyService.Instance;
            if (eco != null)
            {
                int w0 = eco.Wood, f0 = eco.Food, i0 = eco.Iron, c0 = eco.Crystals, g0 = eco.Coins;
                eco.Grant(loot);
                int dw = eco.Wood - w0, df = eco.Food - f0, di = eco.Iron - i0,
                    dc = eco.Crystals - c0, dg = eco.Coins - g0;
                _retreatCredited = new ResourceCost(wood: dw, food: df, iron: di, crystals: dc, coins: dg);
                _retreatRewardShort = dw < loot.Wood || df < loot.Food || di < loot.Iron
                                   || dc < loot.Crystals || dg < loot.Coins;
                LogRetreatCredit("EconomyService", loot, dw, df, di, dc, dg);
                return;
            }

            var gs = GameStateService.Instance;
            var state = gs != null ? gs.State : null;
            if (gs != null && state != null)
            {
                // This fallback route has NO wood/iron/gold mover at all, so those axes are
                // genuinely DROPPED. The basket says so rather than leaving them unset, and the
                // caveat sentence fires - a silent drop is what "I raided and got nothing" looks
                // like from the inside.
                int c0 = state.Resources.Crystals, f0 = state.Resources.Food;
                if (loot.Crystals != 0) gs.AddCrystals(loot.Crystals);
                if (loot.Food != 0) gs.AddFood(loot.Food);
                int dcF = state.Resources.Crystals - c0, dfF = state.Resources.Food - f0;
                _retreatCredited = new ResourceCost(food: dfF, crystals: dcF);
                _retreatRewardShort = dcF < loot.Crystals || dfF < loot.Food
                                   || loot.Wood != 0 || loot.Iron != 0 || loot.Coins != 0;
                LogRetreatCredit("GameStateService fallback", loot, 0, dfF, 0, dcF, 0);
                return;
            }

            DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                "RETREAT LOOT LOST - no EconomyService and no loaded GameState, so the settled " +
                $"partial loot (wood {loot.Wood}, iron {loot.Iron}, food {loot.Food}, gold " +
                $"{loot.Coins}, crystals {loot.Crystals}) was credited NOWHERE. The result screen " +
                "will show no spoils, which at least matches what the player received.");
        }

        /// <summary>
        /// WO-1561 - the retreat twin of <c>RaidVictoryController.LogCredit</c>: always
        /// credited/requested per axis, and a shortfall is a Warn naming both numbers and the
        /// consequence, never a routine Step. A capture must SHOW the clamp rather than agreeing
        /// with a payout that never happened.
        /// </summary>
        private static void LogRetreatCredit(string route, ResourceCost requested,
                                             int dWood, int dFood, int dIron, int dCrystals, int dCoins)
        {
            string measured =
                $"wood {dWood}/{requested.Wood}, food {dFood}/{requested.Food}, iron {dIron}/{requested.Iron}, " +
                $"crystals {dCrystals}/{requested.Crystals}, gold {dCoins}/{requested.Coins} (credited/requested)";

            bool shortfall = dWood < requested.Wood || dFood < requested.Food || dIron < requested.Iron
                          || dCrystals < requested.Crystals || dCoins < requested.Coins;

            if (shortfall)
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    $"RETREAT LOOT SHORT via {route} - the wallet took LESS than the raid awarded: " +
                    measured + ". Raid loot is EarnedIncome, which TownBankCapacity clamps against " +
                    "the town storage ceiling. The result screen states this in WORDS so the player " +
                    "is not left believing a payout that did not land (WO-978 / WO-1461).");
            else
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"RETREAT LOOT credited via {route}: {measured}.");
        }

        // =====================================================================
        //  RAID-END ARMY RECONCILE - the ONE settlement, shared by BOTH exits
        // =====================================================================
        // Set once the army has been reconciled for THIS raid. A raid has more than one
        // reachable exit (victory screen, Retreat button, clock expiry), so the settlement
        // is LATCHED: a raid can never wound or promote the same roster twice.
        private bool _reconciled;

        /// <summary>
        /// The single raid-exit army reconcile, called by BOTH ends of a raid: the retreat /
        /// timeout exit (<see cref="DoRetreat"/>, starsEarned 0) and the VICTORY exit
        /// (RaidVictoryController, starsEarned = the settled RaidResult.Stars).
        ///
        /// BUG THIS CLOSES (2026-07-30): ReconcileAfterRaid had exactly ONE caller - DoRetreat -
        /// so only LOSING an assault ever cost a troop. A won raid was free: nobody was wounded
        /// and no veterancy was paid.
        ///
        /// Computes deployed vs. surviving ids from THIS controller's deploy ledger, the only
        /// place the deployed set exists: a fallen troop's body is destroyed a few seconds after
        /// death (TroopController DeathHoldSeconds), so a scene scan finds survivors only and
        /// could never reconstruct deployedIds. Deployed-but-not-survivor troops are marked
        /// wounded (never deleted); on a 3-star clear each survivor gains one veterancy rank.
        /// LATCHED - the second call is a logged no-op.
        /// </summary>
        // =====================================================================
        //  ATTRITION — recovery scales with camp difficulty (owner ruling 2026-08-21)
        // =====================================================================
        //      Regular  5 min   Hard  20 min   Extreme  45 min
        //
        //  WHY THESE ARE "MEANINGFULLY CHEAPER THAN RETRAINING BUT NEVER FREE":
        //  recovery costs TIME ONLY. Retraining the same unit costs its authored
        //  buildSeconds (270-600s for the units you take into a Hard/Extreme camp) PLUS
        //  the full wood+iron+food basket PLUS a Train queue slot you cannot spend on
        //  anything else. Recovery consumes no slot, no resources, and runs unattended
        //  in parallel across the whole wounded warband. So even the 45-minute Extreme
        //  figure is the cheap option - it just stops a wipe from being a free retry.
        //
        //  ⛔ DO NOT flatten this back to one number. A flat rate is what made a failed
        //  Extreme assault cost exactly as much as a failed practice run.
        // =====================================================================

        /// <summary>Recovery for a Regular-difficulty camp: 5 min (seconds).</summary>
        public const float RecoveryRegularSeconds = 5f * 60f;
        /// <summary>Recovery for a Hard-difficulty camp: 20 min (seconds).</summary>
        public const float RecoveryHardSeconds = 20f * 60f;
        /// <summary>Recovery for an Extreme-difficulty camp: 45 min (seconds).</summary>
        public const float RecoveryExtremeSeconds = 45f * 60f;

        /// <summary>
        /// Recovery seconds for a camp of this difficulty. PURE + static (no scene, no save,
        /// no catalog) so an oracle can assert the table with nothing loaded. An unknown or
        /// blank difficulty resolves to Regular — the FORGIVING direction: a mis-authored
        /// camp must never inflict the 45-minute penalty.
        /// </summary>
        public static float RecoveryForDifficulty(string difficulty)
        {
            switch ((difficulty ?? "Regular").Trim().ToLowerInvariant())
            {
                case "extreme": return RecoveryExtremeSeconds;
                case "hard":    return RecoveryHardSeconds;
                default:        return RecoveryRegularSeconds;
            }
        }

        /// <summary>
        /// The recovery this raid charges: the live camp's difficulty through
        /// <see cref="RecoveryForDifficulty"/>. Falls back to the serialized
        /// <c>_recoverySeconds</c> only when no camp can be resolved (a bare test scene),
        /// and SAYS SO in the trace rather than silently charging the fallback.
        /// </summary>
        private float ResolveRecoverySeconds()
        {
            string configId = null;
            DeNelle.Core.Diagnostics.Guard.Try("Raid", "resolve camp id for attrition", () =>
            {
                var spawner = FindFirstObjectByType<DeNelle.Village.World.Camps.RaidGarrisonSpawner>();
                if (spawner != null) configId = spawner.ConfigId;
            });

            SceneConfigDef def = null;
            if (!string.IsNullOrEmpty(configId))
                DeNelle.Core.Diagnostics.Guard.Try("Raid", "resolve camp difficulty for attrition",
                    () => { def = SceneConfigCatalog.Find(configId); });

            if (def == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "attrition: could not resolve the camp's scene-config (configId='" +
                    (configId ?? "(null)") + "') - charging the serialized fallback " +
                    _recoverySeconds.ToString("F0") + "s instead of the difficulty-scaled rate.");
                return _recoverySeconds;
            }
            return RecoveryForDifficulty(def.difficulty);
        }

        public void ReconcileRaidEnd(int starsEarned)
        {
            if (_reconciled)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "raid-end reconcile already ran for this raid - ignoring the duplicate call.");
                return;
            }
            _reconciled = true;

            var army = Army();
            if (army == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "raid-end reconcile: no ArmyStorage (no GameState) - nothing to reconcile.");
                return;
            }

            // WO-823 Phase E - THE FIRST-RAID STAMP, and the ONLY writer of this flag.
            // ReconcileRaidEnd is the latched seam every raid exit already funnels through
            // (victory -> RaidVictoryController, retreat -> DoRetreat's ReconcileRaidEnd(0),
            // hero death -> HeroHealth's ReconcileRaidEnd(0), now only on a SETTLED raid: WO-1526
            // made a live-raid death take the "raid continues" branch above it), so stamping here covers all
            // three exits with one line and no exit-specific branching. It sits AFTER the
            // army null-guard on purpose: headless has no GameState, so a headless run can
            // never spend a live player's softened first raid.
            // No second writer, ever - a raid screen/panel/VM that set this would fork the
            // one-owner seam and re-create the very drift Phase E exists to remove.
            var raidState = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (raidState != null && !raidState.EverCompletedRaid)
            {
                raidState.EverCompletedRaid = true;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "FIRST RAID COMPLETED (stars " + starsEarned + ") - everCompletedRaid false->true. " +
                    "The raid door now requires the FULL army cap instead of the softened " +
                    "first-raid slot floor, permanently.");
            }

            // Survivors = the living deployed bodies' owning ids; everyone else we
            // deployed fell -> wounded (recovery countdown). NEVER deleted.
            var deployedIds = new List<string>();
            var survivorIds = new List<string>();
            foreach (var d in _deployed)
            {
                if (string.IsNullOrEmpty(d.OwnedId)) continue;
                deployedIds.Add(d.OwnedId);
                if (d.Controller != null && d.Controller.IsAlive)
                    survivorIds.Add(d.OwnedId);
            }

            // WO-728 / owner ruling 2026-08-21 - ATTRITION SCALES WITH CAMP DIFFICULTY.
            // This was a flat 120s for every camp, which made raiding effectively FREE: two
            // minutes of recovery is no cost at all next to a 4-12h camp cooldown, so a failed
            // Extreme assault and a failed practice run charged the player identically and
            // there was no loop, only a faucet with a pause in front of it.
            float recovery = ResolveRecoverySeconds();

            // WO-1561 - captured HERE because this is the only place the deployed set exists (see
            // the deploy-ledger note above). The non-victory result screen states "N troops return
            // wounded" from these two numbers; a raid that never reconciled leaves them at -1 and
            // the screen omits the line rather than printing a zero it cannot prove.
            _lastDeployedCount = deployedIds.Count;
            _lastSurvivorCount = survivorIds.Count;

            DeNelle.Core.Diagnostics.Guard.Try("Raid", "reconcile army after raid",
                () => army.ReconcileAfterRaid(deployedIds, survivorIds, recovery));

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"raid-end reconcile - deployed {deployedIds.Count}, survivors {survivorIds.Count}, " +
                $"wounded {deployedIds.Count - survivorIds.Count} (stars {starsEarned}, " +
                $"recovery {recovery:F0}s).");

            GrantVeterancy(army, survivorIds, starsEarned);
        }

        /// <summary>
        /// The survivor reward: on a 3-STAR clear every troop that walked off the field gains
        /// one veterancy rank (<see cref="ArmyStorage.AddVeterancy"/>, capped at
        /// PlayerTroop.MaxVeterancyRank) - the "+5% damage per survived 3-star raid" ladder
        /// PlayerTroop already documents and TroopDeployer.SpawnFromArmy already consumes via
        /// PlayerTroop.DamageMultiplier. Before this, AddVeterancy had ZERO callers repo-wide.
        /// Below 3 stars nothing is granted.
        /// </summary>
        private static void GrantVeterancy(ArmyStorage army, List<string> survivorIds, int starsEarned)
        {
            if (starsEarned < 3)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"veterancy: {starsEarned} star(s) - no ranks granted (3 stars required).");
                return;
            }
            if (army.Owned == null || survivorIds == null || survivorIds.Count == 0)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "veterancy: 3-star clear but NO surviving deployed troops - no ranks granted.");
                return;
            }

            var survivors = new HashSet<string>(survivorIds, System.StringComparer.Ordinal);
            int promoted = 0;
            DeNelle.Core.Diagnostics.Guard.Try("Raid", "grant survivor veterancy", () =>
            {
                foreach (var t in army.Owned)
                {
                    if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                    if (!survivors.Contains(t.Id)) continue;
                    int before = t.VeterancyRank;
                    army.AddVeterancy(t);
                    if (t.VeterancyRank != before) promoted++;
                }
            });

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"veterancy: 3-star clear - {promoted} of {survivors.Count} survivor(s) gained a rank.");
        }

        // =====================================================================
        //  HUD construction (code-built uGUI, non-modal bottom tray)
        // =====================================================================

        // ── WO-1436: WHERE THE DEPLOY BAR SITS, AND WHY IT IS NOT A LITERAL ──────────
        //
        // OWNER RULING 2026-09-06: the ABILITY ROW owns the thumb position at the bottom; this
        // bar stacks ABOVE it. Casting is constant, deploying is occasional, so the constant
        // action gets the thumb.
        //
        // ⛔ THE DEFECT THIS CLOSES, MEASURED AT SOURCE (WO-1436 §7.3, not inferred):
        //     kit `actionBar` (holds combatDock)  Y 0.015-0.150  canvas sortingOrder  4 000
        //     this bar                            Y 0.010-0.160  canvas sortingOrder 30 000
        // Same band, 26 000 layers above. Once the raid declares combat for its whole duration
        // the ability faces are up PERMANENTLY - and were permanently buried. The owner's
        // felt-test screenshot showed CAST / BLOCK / Arcane Bolt / Mend / EMPTY / ITEM rendering
        // underneath this strip: present, and unreachable, which from the chair is the same thing
        // as absent.
        //
        // ⛔ THE FLOOR IS SHARED DATA, NEVER A NUMBER TYPED HERE. It is
        // HudLayoutBands.BottomOverlayFloorY (DeNelle.Core.UI). This class is in DeNelle.Village,
        // which may not reference DeNelle.HUD (CLAUDE.md §5), so it cannot ask the kit for a free
        // band - and a matching literal on each side is the duplicated-state failure CLAUDE.md
        // documents four times over. If the ability row's height ever changes, this bar follows
        // with no edit here. Do NOT re-introduce a bottom-Y literal in BuildHud.

        /// <summary>Bar height as a screen fraction — the authored strip height, unchanged from
        /// the pre-WO-1436 bar (0.16 - 0.01). Only its SEAT moved.</summary>
        private const float DeployBarHeight = 0.150f;
        /// <summary>Status-line height as a screen fraction (was 0.205 - 0.165).</summary>
        private const float DeployStatusHeight = 0.040f;
        // ── WO-1464: THE LEFT EDGE IS THE STICK'S RIGHT EDGE, AND IT IS NOT A LITERAL ────
        //
        // ⛔ THE DEFECT THIS CLOSES, MEASURED ON THE OWNER'S DEVICE (build 358872,
        // Logs/device/screens/owner-screen-20260907-004502.png, 2670x1200, mid-raid at 1:13):
        // this bar ran x 0.020-0.980 and the movement stick's mount is x 0.010-0.270, y
        // 0.030-0.330. WO-1436 lifted the bar clear of the ability row's Y band and that fix is
        // correct and stays - but the stick reaches HIGHER than the ability row does, so a strip
        // seated on BottomOverlayFloorY (0.160) still crosses it. In the capture the stick's ring
        // is visible peeking out from under the tray slab. The stick is the game's only locomotion
        // control on a phone; covering it does not degrade the HUD, it removes movement.
        //
        // The edge is HudLayoutBands.BottomOverlayLeftX - shared data in DeNelle.Core, for the
        // same reason the floor is: DeNelle.Village may not reference DeNelle.HUD (CLAUDE.md §5),
        // and a matching literal on each side is the duplicated-state failure CLAUDE.md documents
        // four times over. Do NOT re-introduce an x literal here.

        /// <summary>Left edge of both bands - the movement stick's right edge plus the one shared
        /// clearance gap. Never a literal (see the block above).</summary>
        private static float DeployBandMinX
        {
            get { return HudLayoutBands.BottomOverlayLeftX; }
        }
        /// <summary>Right edge of both bands (unchanged).</summary>
        private const float DeployBandMaxX = 0.98f;

        /// <summary>The deploy bar's screen band, seated immediately above the reserved thumb
        /// band. Exposed so the oracle can assert the exclusion from the AUTHORED anchors on both
        /// sides rather than from a copied figure.</summary>
        public static Rect DeployBarBand
        {
            get
            {
                return HudLayoutBands.StackAboveThumbBand(
                    DeployBandMinX, DeployBandMaxX, DeployBarHeight, 0f);
            }
        }

        /// <summary>The status line's band, stacked on the bar with the SAME shared gap constant
        /// so the two can never drift apart.</summary>
        public static Rect DeployStatusBand
        {
            get
            {
                return HudLayoutBands.StackAboveThumbBand(
                    DeployBandMinX, DeployBandMaxX, DeployStatusHeight,
                    DeployBarHeight + HudLayoutBands.ThumbBandClearanceGap);
            }
        }

        private void BuildHud()
        {
            // WO-1110 acceptance: the ONLY way to prove the exit survives a HUD failure is to
            // actually break the HUD. One-shot so the injection cannot wedge a real session.
            if (DebugForceBuildHudThrow)
            {
                DebugForceBuildHudThrow = false;
                throw new System.InvalidOperationException(
                    "WO-1110 fault injection: forced BuildHud failure.");
            }

            if (_ui != null) Destroy(_ui);

            // A plain overlay canvas (NOT a modal scrim — the world stays visible/playable).
            _ui = ElarionUiKit.BuildModalCanvas("RaidDeployHud", 30000);

            // Command bar — a framed dark-glass strip, seated ABOVE the reserved thumb band so
            // the hero's ability row keeps the thumb position (owner ruling 2026-09-06; see the
            // DeployBarBand block above). The band comes from DeNelle.Core.UI, not from here.
            var barBand = DeployBarBand;
            var bar = ElarionUiKit.Panel(_ui.transform,
                new Vector2(barBand.xMin, barBand.yMin), new Vector2(barBand.xMax, barBand.yMax),
                deep: false);
            var barImg = bar.GetComponent<Image>();
            if (barImg != null) barImg.color = new Color(0.04f, 0.035f, 0.03f, 0.38f);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "deploy HUD seated above the reserved thumb band: bar y " +
                barBand.yMin.ToString("F3") + ".." + barBand.yMax.ToString("F3") +
                ", floor " + HudLayoutBands.BottomOverlayFloorY.ToString("F3") +
                " (ability row tops out at " + HudLayoutBands.ThumbActionRowMaxY.ToString("F3") +
                "), and clear of the movement stick: bar x starts at " +
                barBand.xMin.ToString("F3") + ", stick right edge " +
                HudLayoutBands.MoveClusterMount.xMax.ToString("F3") + " (WO-1464).");

            // Status line just above the bar.
            var statusBand = DeployStatusBand;
            var statusGo = new GameObject("Status", typeof(TMPro.TextMeshProUGUI));
            statusGo.transform.SetParent(_ui.transform, false);
            var sr = statusGo.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(statusBand.xMin, statusBand.yMin);
            sr.anchorMax = new Vector2(statusBand.xMax, statusBand.yMax);
            sr.offsetMin = Vector2.zero; sr.offsetMax = Vector2.zero;
            _status = statusGo.GetComponent<TMPro.TextMeshProUGUI>();
            _status.fontSize = ElarionUi.FontLabel;
            _status.color = ElarionUi.Parchment;
            _status.alignment = TMPro.TextAlignmentOptions.Center;
            _status.raycastTarget = false;
            // ⚠ WO-1464: EXPLICIT MIN, AND THE REASON IS ARITHMETIC. DeployStatusHeight is 0.040
            // of screen = 38.62 reference px at the owner's device, against
            // RaidSelectionScreen.NeedPx(FontFloor 30) = 38.58 - a 0.04 px margin. Fitting this
            // line at the default floor would put it one rounding error from TMP culling the
            // whole sentence, and the band is WO-1436's shared constant, not this file's to
            // widen. The sentence ALSO lost 27% of its width when the bar moved off the stick
            // (x 0.02-0.98 -> 0.28-0.98), so leaving it unfitted is not an option either.
            // Floor at FontHardFloor: NeedPx(20) = 26.8, which always seats.
            ElarionUiKit.FitSingleLine(_status, ElarionUiKit.FontHardFloor, ElarionUi.FontLabel);

            BuildTrayTiles(bar.transform);

            // ⛔ WO-1639 DEFECT B — THE FACE WAS TOO NARROW FOR ITS OWN WORD, AND THE WORD IS PINNED.
            // Every one of the four arena frames (Builds/device-frames/2026-09-10_060*_arena_*.png)
            // shows this face reading "DEPLOY ..." - the label autoshrunk to ElarionUiKit.FontFloor
            // (30) and then ELLIPSISED. The arithmetic, at the owner's 2670x1200:
            //   canvas reference = 2147.9 x 965.4 px (HudLayoutBands.CanvasReferenceSize, :379,
            //   Unity's own match-0.5 formula on the 1080x1920 reference), so the bar's
            //   x 0.280-0.980 span is 1503.5 reference px wide.
            //   BuildObsidianButton insets its label to 0.04-0.96 of the face
            //   (ElarionUiKitObsidian.cs:681-683), i.e. 92% usable, then FitSingleLine (:686)
            //   autosizes [30..50] and cuts with Ellipsis (:3054-3070).
            //   OLD: Deploy All 0.130 of the bar = 195.5 px, 179.9 usable, for TEN characters at
            //   FontBody 50 bold. Rally (5) and Retreat (7) fit the same width; "Deploy All"
            //   never could - and RefreshRallyButton (see below) swaps Rally to "Rally ON" (8),
            //   so that face was one character from the same failure.
            //
            // ⛔ SHORTENING THE COPY IS NOT AVAILABLE AND WAS NOT DONE. The exact literal
            // "Deploy All" is pinned by Editor/Regression/RaidDeployUiRegression.cs:411-415, and
            // renaming a player-facing face is the owner's call, not a lane's (CLAUDE.md sec.2).
            // So the FACES WIDEN, and the room comes from the tray, which was over-wide: its
            // tiles are square portraits in a 0.52-of-bar strip. WHAT MOVED, in bar fractions:
            //   tray        0.030-0.550  ->  0.030-0.390   (BuildTrayTiles `right`)
            //   Deploy All  0.565-0.695  ->  0.410-0.650   (0.130 -> 0.240)
            //   Rally       0.700-0.830  ->  0.665-0.825   (0.130 -> 0.160)
            //   Retreat     0.845-0.985  ->  0.840-0.985   (0.140 -> 0.145)
            // The bar's own edges do not move, so DeployBarBand, DeployStatusBand and every
            // y-band case in RaidHudThumbBandRegression are untouched; only x within the bar
            // changes. Predicted seated sizes at 92% usable width and ~0.68 em average advance
            // for this bold face: "Deploy All" ~48pt, "Rally ON" ~40pt, "Retreat" ~42pt - all
            // comfortably above the 30 px FontFloor, so no face reaches the ellipsis at all.
            // NO FONT IS LOWERED ANYWHERE; the fitters keep the kit's own floors.
            // Touch floor: the tray keeps 541 reference px for its tiles, so four troop types
            // seat 135 px each - still over ElarionUiKit.MinTouchPx (112); a fifth relies on the
            // kit's own ClampMinTouch, exactly as it did at the old width.
            _deployAllButton = ElarionUiKit.Button(bar.transform, "Deploy All", ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.410f, 0.18f), new Vector2(0.650f, 0.82f), DeployAll);

            // Rally toggle + Retreat — right edge of the bar. Rally's band carries "Rally ON"
            // (RefreshRallyButton), not "Rally", so it is sized for the LONGER of the two.
            _rallyButton = ElarionUiKit.Button(bar.transform, "Rally", ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.665f, 0.18f), new Vector2(0.825f, 0.82f), ToggleRally);
            _retreatButton = ElarionUiKit.Button(bar.transform, "Retreat", ElarionUiKit.ButtonKind.Danger,
                new Vector2(0.840f, 0.18f), new Vector2(0.985f, 0.82f), OnRetreatPressed);

            if (_status != null) _status.text = "";
            RefreshTiles();
            RefreshRallyButton();
            StartCoroutine(WO1639BarProbe());
        }

        // =====================================================================
        //  WO-1639 STEP 1 INSTRUMENTATION — PERMANENT (CLAUDE.md sec.12)
        // ---------------------------------------------------------------------
        // ⚠ Interpolated parts are computed into locals FIRST: the compile gate's brace
        // scanner has no interpolated-string model, so a quote inside a `{...}` hole ends
        // the string as far as it is concerned (CLAUDE.md sec.1). Concatenation only.
        // =====================================================================

        /// <summary>The canvas scale factor behind a label, as a string, so a device-px reading in
        /// the log can be converted to the reference px the layout arithmetic uses.</summary>
        private static string ScaleFactorOf(TMPro.TextMeshProUGUI t)
        {
            var c = t != null ? t.canvas : null;
            return c != null ? c.scaleFactor.ToString("F4") : "unknown";
        }

        private System.Collections.IEnumerator WO1639BarProbe()
        {
            yield return null;   // one frame so TMP has laid the faces out
            LogFaceFit("deployAll", _deployAllButton);
            LogFaceFit("rally", _rallyButton);
            LogFaceFit("retreat", _retreatButton);
            LogToastOrdering();
        }

        /// <summary>WO-1639 DEFECT B Step 1: does the face show its whole word? Same read shape as
        /// WO-1628 sec.4 Step 1 - band px, the autosize window, drawn characters vs the string's
        /// length, and TMP's own truncation flag. A face whose drawn count is short of its string
        /// is the "DEPLOY ..." defect, and this line names it without a pixel audit.</summary>
        private static void LogFaceFit(string faceName, UnityEngine.UI.Button btn)
        {
            if (btn == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "[wo1639-face] " + faceName + " button is NULL.");
                return;
            }
            var t = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (t == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "[wo1639-face] " + faceName + " has no label.");
                return;
            }
            t.ForceMeshUpdate();
            var corners = new Vector3[4];
            t.rectTransform.GetWorldCorners(corners);
            float labelW = Mathf.Abs(corners[2].x - corners[1].x);
            float labelH = Mathf.Abs(corners[1].y - corners[0].y);
            var faceCorners = new Vector3[4];
            ((RectTransform)btn.transform).GetWorldCorners(faceCorners);
            float faceW = Mathf.Abs(faceCorners[2].x - faceCorners[1].x);
            float faceH = Mathf.Abs(faceCorners[1].y - faceCorners[0].y);
            string raw = t.text ?? string.Empty;
            int drawn = t.textInfo != null ? t.textInfo.characterCount : -1;
            string msg = "[wo1639-face] " + faceName +
                         " text='" + raw + "'" +
                         " face=" + faceW.ToString("F1") + "x" + faceH.ToString("F1") + "px" +
                         " label=" + labelW.ToString("F1") + "x" + labelH.ToString("F1") + "px" +
                         " font=" + t.fontSize.ToString("F1") +
                         " [" + t.fontSizeMin.ToString("F1") + ".." + t.fontSizeMax.ToString("F1") + "]" +
                         " chars=" + drawn + "/" + raw.Length +
                         " truncated=" + t.isTextTruncated +
                         " overflow=" + t.overflowMode.ToString() +
                         // ⚠ GetWorldCorners on a ScreenSpaceOverlay canvas returns DEVICE px, not
                         // the reference px the WO-1639 arithmetic is written in. The scale factor
                         // is logged so the two can be reconciled without guessing (at the owner's
                         // 2670x1200 it is ~1.243).
                         " scaleFactor=" + ScaleFactorOf(t);
            if (t.isTextTruncated || (drawn >= 0 && drawn < raw.Length))
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    msg + " <- FACE IS ELLIPSISED, it does not show its whole word.");
            else
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid", msg);
        }

        /// <summary>WO-1639 DEFECT C Step 1: the draw-order fact that explains the burial, printed
        /// every raid so it can never be inferred again. See <see cref="RaidToastSortingOrder"/>
        /// for the RCA.</summary>
        private void LogToastOrdering()
        {
            var canvas = _ui != null ? _ui.GetComponent<Canvas>() : null;
            string deployOrder = canvas != null ? canvas.sortingOrder.ToString() : "no-canvas";
            string raidHudOrder = "29000 (RaidHudController.BuildHud)";
            string toastOrder = RaidToastSortingOrder.ToString();
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "[wo1639-toast] raid status toasts are raised to sortingOrder " + toastOrder +
                " so they draw ABOVE the deploy HUD canvas (" + deployOrder + ") and the raid " +
                "readout canvas (" + raidHudOrder + "). The kit default is 720, which is BELOW " +
                "both - that is why 'HERO DOWN' was visible only faintly THROUGH the deploy bar " +
                "in Builds/device-frames/2026-09-10_0613_arena_05_hero_left_edge.png.");
        }

        // One tile per troop TYPE the player has deployable in this raid (deduped by def).
        private void BuildTrayTiles(Transform bar)
        {
            _tiles.Clear();
            var army = Army();

            // Distinct deployable def ids, in catalog order so Footman/Archer read stably.
            var defIds = new List<string>();
            if (army != null && army.Owned != null)
            {
                foreach (var t in army.Owned)
                {
                    if (t == null || !t.IsDeployable || string.IsNullOrEmpty(t.TroopDefId)) continue;
                    if (!defIds.Contains(t.TroopDefId)) defIds.Add(t.TroopDefId);
                }
            }

            if (defIds.Count == 0)
            {
                var empty = ElarionUiKit.Label(bar, "No troops to deploy - train at the Barracks first.",
                    0.18f, 0.82f, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                    TMPro.TextAlignmentOptions.Left, 0.03f, 0.68f);
                ElarionUiKit.FitSingleLine(empty);
                return;
            }

            // Lay the tiles across the left of the bar.
            // ⚠ WO-1639 DEFECT B: `right` was 0.55f. It is 0.390f now, and the 0.16 of bar width
            // that frees is what widens the "Deploy All" face from 0.130 to 0.240 so it stops
            // ellipsising (the full arithmetic and the before/after table are in BuildHud, at the
            // _deployAllButton line). The tiles are SQUARE portraits, so the strip they lost was
            // never carrying glyphs: at 0.360 of a 1503.5-reference-px bar the tray still seats
            // 541 px, i.e. 135 px per tile for four troop types - over ElarionUiKit.MinTouchPx
            // (112, ElarionUiKit.cs:347). The tile's HEIGHT fractions (0.18-0.82, and the 0.48
            // count badge inside them) are untouched, so RaidHudThumbBandRegression's
            // "the tile count badge is 0.48 of a tile that is 0.64 of the bar" case still holds.
            int count = defIds.Count;
            float left = 0.03f, right = 0.390f;
            float w = (right - left) / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                string defId = defIds[i];
                float x0 = left + i * w;
                float x1 = x0 + w * 0.94f;

                string label = DisplayName(defId);
                var btn = ElarionUiKit.Button(bar, label, ElarionUiKit.ButtonKind.Gold,
                    new Vector2(x0, 0.18f), new Vector2(x1, 0.82f), () => ArmTile(defId));

                // Captured BEFORE the badge is parented under the button, so this is the name
                // label and can never resolve to the count (WO-1464).
                var nameLabel = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (nameLabel != null)
                {
                    nameLabel.text = string.Empty;
                    nameLabel.enabled = false;
                }

                var portraitSeat = new GameObject("TroopPortrait", typeof(RectTransform));
                portraitSeat.transform.SetParent(btn.transform, false);
                var pr = portraitSeat.GetComponent<RectTransform>();
                pr.anchorMin = new Vector2(0.12f, 0.04f);
                pr.anchorMax = new Vector2(0.88f, 0.96f);
                pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;
                var def = TroopCatalog.Find(defId);
                string icon = def != null && !string.IsNullOrEmpty(def.IconId) ? def.IconId : defId;
                ElarionUiKit.Portrait(portraitSeat.transform,
                    Resources.Load<Sprite>("RpgUi/troop/" + icon), active: false);

                // ── WO-1464: THE COUNT BADGE, READABLE BY LUMINANCE ─────────────────
                // ⛔ It was ElarionUi.Ink (0.137, 0.098, 0.055 - near-black) painted on the
                // obsidian tile's DARK face, hard against the frame's ornate top-right corner
                // and with no fit. In the owner's capture (owner-screen-20260907-004502.png)
                // the "x0" badges are illegible at 2670x1200. The kit's own answer for a label
                // on this face is ObsidianButtonLabelColor -> ElarionUi.Parchment; the badge
                // takes Gilt, which is brighter still and reads as an accent WITHOUT depending
                // on hue (the owner is colourblind - legibility is luminance, size and position).
                // It also moves OFF the corner filigree into the tile's lower-right interior.
                var countGo = new GameObject("Count", typeof(TMPro.TextMeshProUGUI));
                countGo.transform.SetParent(btn.transform, false);
                var cr = countGo.GetComponent<RectTransform>();
                // ⚠ THE BAND IS SIZED, NOT EYEBALLED. The tile resolves to 0.64 x 0.150 of screen
                // = 92.7 reference px at the owner's 2670x1200. A 0.32-tall badge is 29.7 px,
                // UNDER RaidSelectionScreen.NeedPx(FontFloor) = 38.6 - and TMP Ellipsis culls a
                // line it cannot seat, so a badge made "readable" in a band that thin would have
                // rendered BLANK. 0.48 of the tile = 44.5 px seats the floor with room. (Same
                // measurement the WO-1519 lane's [seat] case exists for.)
                cr.anchorMin = new Vector2(0.62f, 0.26f);
                cr.anchorMax = new Vector2(0.93f, 0.74f);
                cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;
                var ct = countGo.GetComponent<TMPro.TextMeshProUGUI>();
                ct.fontSize = ElarionUi.FontLabel;
                ct.color = ElarionUi.Gilt;
                ct.fontStyle = TMPro.FontStyles.Bold;
                ct.alignment = TMPro.TextAlignmentOptions.Right;
                ct.raycastTarget = false;
                ElarionUiKit.FitSingleLine(ct);

                _tiles.Add(new TrayTile { DefId = defId, Button = btn, CountLabel = ct,
                                          NameLabel = nameLabel });
            }
        }

        private void DeployAll()
        {
            var army = Army();
            var hero = GameObject.FindWithTag("Player");
            if (army == null || army.Owned == null || hero == null)
            {
                SetStatus("Deploy All unavailable - no ready army or hero.");
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(hero.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            Vector3 desired = hero.transform.position + forward * 7f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(desired, out UnityEngine.AI.NavMeshHit seat,
                                                       8f, UnityEngine.AI.NavMesh.AllAreas))
            {
                SetStatus("Deploy All needs open ground ahead.");
                return;
            }

            int deployedNow = 0;
            var ready = new List<PlayerTroop>();
            foreach (var owned in army.Owned)
                if (owned != null && owned.IsDeployable && !IsDeployed(owned.Id)) ready.Add(owned);
            foreach (var owned in ready)
            {
                int stack = CountDeployedOfType(owned.TroopDefId);
                var troop = TroopDeployer.SpawnFromArmy(owned, seat.position, stack, _deploySpread);
                if (troop == null) continue;
                _deployed.Add(new Deployed { Controller = troop, OwnedId = owned.Id });
                RaidScoring.Instance?.RecordDeploy(owned.TroopDefId, troop.transform.position);
                deployedNow++;
            }

            Disarm();
            RefreshTiles();
            SetStatus(deployedNow > 0 ? "Deployed " + deployedNow + " troops in assault formation."
                                      : "All ready troops are already deployed.");
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "DEPLOY ALL -> " + deployedNow + " troop(s), tactic=Assault Formation, seat=" + seat.position + ".");
        }

        private void ArmTile(string defId)
        {
            _rallyMode = false;
            RefreshRallyButton();
            _armedDefId = defId;
            RefreshTiles();
        }

        private void Disarm() => _armedDefId = null;

        // Refresh each tile's remaining count + highlight the armed one.
        private void RefreshTiles()
        {
            var army = Army();
            int totalRemaining = 0;
            foreach (var tile in _tiles)
            {
                int remaining = RemainingOfType(army, tile.DefId);
                totalRemaining += remaining;
                if (tile.CountLabel != null)
                    tile.CountLabel.text = tile.DefId == _armedDefId
                        ? "[x" + remaining + "]" : "x" + remaining;
                if (tile.Button != null)
                {
                    // Spent types leave the bar. A row of "Footman x0 / Archer x0" is the
                    // screenshot the owner hated (2026-09-09) - a command strip of dead tiles
                    // sitting on the fight. Rally / Retreat stay.
                    bool keep = remaining > 0 || tile.DefId == _armedDefId;
                    if (tile.Button.gameObject.activeSelf != keep)
                        tile.Button.gameObject.SetActive(keep);
                    tile.Button.interactable = remaining > 0;

                    // ── WO-1464: THE ARMED CUE IS A MARKER, NOT A HUE ───────────────────
                    // ⛔ THIS LINE WAS THE UNREADABLE-TRAY DEFECT ITSELF. The kit already
                    // paints an obsidian button's label in ObsidianButtonLabelColor
                    // (ElarionUi.Parchment) against its dark face - Rally ON and RETREAT read
                    // perfectly in the owner's capture for exactly that reason. This method
                    // then OVERWROTE the tile labels with ElarionUi.Ink (near-black on
                    // near-black), which is why FOOTMAN and ARCHER were the only two unreadable
                    // controls on the screen. The fix is to STOP OVERRIDING, not to pick a
                    // different colour: the kit is the one contrast authority.
                    //
                    // The armed/idle distinction was also hue-only (Affordable green vs Ink),
                    // which the owner cannot see. It is now a SHAPE cue - the armed tile's name
                    // is bracketed - with brightness (Gilt vs Parchment) as a secondary, never
                    // the sole, signal.
                    var lbl = tile.NameLabel;
                    if (lbl != null)
                    {
                        lbl.text = string.Empty;
                        // FitSingleLine is armed once at construction (BuildTrayTiles); TMP
                        // auto-sizing then re-fits the bracketed form on its own. Re-arming the
                        // fit guard every 10Hz refresh would stack components on the label.
                    }
                }
            }
            if (_deployAllButton != null) _deployAllButton.interactable = totalRemaining > 0;
        }

        private void RefreshRallyButton()
        {
            if (_rallyButton == null) return;
            var lbl = _rallyButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (lbl != null) lbl.text = _rallyMode ? "Rally ON" : "Rally";
        }

        // ⛔ WO-1639 DEFECT C — WHY "HERO DOWN" RENDERED *UNDER* THE DEPLOY BAR. RCA, not a guess.
        //
        // The ticket looked for a seat bug and there is none: DeployStatusBand (y 0.320-0.360)
        // does not overlap DeployBarBand (y 0.160-0.310), and _status is built AFTER the bar so
        // uGUI would draw it on top anyway. The frame disagreed with the source because THE
        // SENTENCE IS NOT IN _status AT ALL. SetStatus below CLEARS _status and routes the copy
        // to ElarionUiKit.ShowToast - and the kit's toast builds its own ScreenSpaceOverlay
        // canvas at sortingOrder 720 (ElarionUiKitConformance.cs:393-411). This controller's HUD
        // canvas is 30000 (BuildHud) and RaidHudController's is 29000. 720 is below BOTH, so the
        // toast draws UNDERNEATH the deploy bar, and the ONLY reason the owner could see any of
        // it is the bar plate's own 0.38 alpha. That is exactly the frame: "HERO DO..." faint,
        // through the tan plate, at y 855-885 of 2026-09-10_0613_arena_05_hero_left_edge.png.
        //
        // THE SEAT ARITHMETIC CONFIRMS IT INDEPENDENTLY. The toast card is pivot (0.5, 0),
        // anchoredPosition (0, 220) on a 1080x1920 reference at match 0.5
        // (ElarionUiKitConformance.cs:419-424). At 2670x1200 the scale is
        // sqrt((2670/1080) * (1200/1920)) = 1.2430, so a 76 px card spans 273.5 to 367.9 device
        // px from the BOTTOM, i.e. 832 to 926 from the top, and its vertically centred label
        // lands at ~879. The measured glyphs sit at 855-885. It is the toast.
        //
        // THE FIX IS CALLER-SIDE, because ElarionUiKit* is READ-ONLY for this ticket (WO-1639
        // sec.7): ShowToast already takes a sortingOrder, so the raid passes one above its own
        // two canvases. This is deliberately applied to EVERY raid status, not just hero-down -
        // a toast that draws behind the HUD is useless for "Deploy All needs open ground ahead"
        // as well.
        //
        // RESIDUALS, RECORDED AND NOT FIXED HERE (both are kit-owned, sec.7 forbids the edit):
        //  * the card's SEAT is the kit's hardcoded 220 ref px, which is inside the bar's y band
        //    (0.228-0.307 normalised). Above the bar in sort order it is fully legible, but for
        //    its ~3s life it covers part of the tray. Acceptable for a transient; the alternative
        //    is a kit change.
        //  * the toast label is the kit's 24 px legacy Text, below ElarionUiKit.FontFloor (30).
        //    Flagged for the lead - it is a kit-wide question, not a raid one.

        /// <summary>Sorting order for raid status toasts. ABOVE this controller's HUD canvas
        /// (30000) and RaidHudController's readout canvas (29000); the kit default of 720 is
        /// below both, which is the WO-1639 Defect C burial. Never lower this below 30001.</summary>
        private const int RaidToastSortingOrder = 30500;

        /// <summary>Toast card size in reference px for raid status. The kit's 480x76 default is
        /// sized for ~2 lines of short chrome copy; the raid's longest sentence is
        /// EndStateVM.HeroDownArmyFightsOn ("HERO DOWN - your army fights on", 31 chars) and it
        /// is the single most important message this HUD ever shows (WO-1639 sec.3), so it gets
        /// a card with room rather than a wrap.</summary>
        private const float RaidToastCardWidth = 640f;
        private const float RaidToastCardHeight = 96f;

        /// <summary>Default toast life for raid status, in seconds. Matches what SetStatus has
        /// always passed; named so the one message that needs longer can say so.</summary>
        private const float RaidToastLifeSeconds = 2.2f;

        /// <summary>Toast life for the hero-down line. It is the single most important sentence
        /// this HUD shows (WO-1639 sec.3) and it arrives while the player is being killed, so it
        /// gets more than the chrome default. The STRING is still EndStateVM.HeroDownArmyFightsOn
        /// and is never retyped inline (pinned by RaidScoringRegression.cs:411-413).</summary>
        private const float HeroDownToastLifeSeconds = 4.5f;

        private void SetStatus(string s, float lifeSeconds = RaidToastLifeSeconds)
        {
            // Persistent mid-fight copy ("Rally set — idle troops will muster there")
            // sat in the world and the owner hated it (2026-09-09 raid frame). The
            // status BAND stays (layout oracles measure it); the sentence is a toast.
            // Instructional chatter (armed / tap the ground / Rally off) is not
            // toasted - the tray already says that with brackets and Rally ON.
            if (_status != null) _status.text = "";
            if (string.IsNullOrEmpty(s)) return;
            ElarionUiKit.ShowToast(s, ElarionUiKit.ToastTone.Info,
                lifeSeconds: Mathf.Max(RaidToastLifeSeconds, lifeSeconds),
                sortingOrder: RaidToastSortingOrder,
                cardWidth: RaidToastCardWidth, cardHeight: RaidToastCardHeight);
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static ArmyStorage Army()
        {
            var svc = GameStateService.Instance;
            return svc != null && svc.State != null ? svc.State.Army : null;
        }

        private static string DisplayName(string defId)
        {
            var d = TroopCatalog.Find(defId);
            return d != null && !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName
                 : (string.IsNullOrEmpty(defId) ? "Troop" : defId);
        }

        // ── Lean.Touch tap (mobile) — latched here, consumed in Update ─────────
        // Cheap mirror of the desktop tap so a phone can deploy/rally too. Lean is
        // already vendored + referenced by this asmdef (see LeanTouchBuildDriver).
        private void OnEnable()  { Lean.Touch.LeanTouch.OnFingerTap += OnLeanTap; }
        private void OnDisable() { Lean.Touch.LeanTouch.OnFingerTap -= OnLeanTap; }

        private void OnLeanTap(Lean.Touch.LeanFinger finger)
        {
            if (finger == null || finger.Index < 0) return;   // skip simulated mouse (desktop path owns it)
            if (finger.IsOverGui) return;                       // a UI tap is handled by the button itself
            _leanTapPoint = finger.ScreenPosition;
            _leanTapLatched = true;
        }
    }
}
