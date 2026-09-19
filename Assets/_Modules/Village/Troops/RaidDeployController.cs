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
//   BREACH  — (WO-1719) toggle Breach on, then tap a WALL SEGMENT to order the whole
//             warband onto THAT panel (TroopBreachOrder). It overrides the automatic
//             most-damaged pick; that pick stays the fallback when no order stands.
//             A wall tap with Breach OFF is unchanged - it still falls through to Rally.
//   RETREAT — survivors = the living deployed bodies' OwnedTroopIds; reconcile the
//             army and evac home via GoCastle. WO-1810: deployed-but-not-survivor is
//             REMOVED from the roster (it used to be "→ wounded", healed free on a
//             timer, which is the defect the owner reported), and the exit itself also
//             costs a share of the survivors — 60% on a chosen retreat, all of them on
//             a fail. See RaidCasualtyPolicy.
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
        private Button _breachButton;
        private Button _retreatButton;
        private readonly List<TrayTile> _tiles = new List<TrayTile>();

        /// <summary>The empty-tray sentence, held so WO-1646's probe can PROVE it wrapped rather
        /// than being truncated. Null whenever the tray has tiles.</summary>
        private TMPro.TextMeshProUGUI _emptyTrayLabel;

        // ── Tap state machine ─────────────────────────────────────────────────
        private string _armedDefId;     // the TroopDefId armed for the next ground tap (null = none)
        private bool _rallyMode;        // true while the Rally toggle is on (next tap sets the rally point)
        private bool _breachMode;       // WO-1719: true while Breach is on (next wall tap sets TroopBreachOrder)
        private BreachOrderMarker _breachMarker;   // WO-1723 Q2: the in-world bracket on the ordered panel
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
            // WO-1719: same lifetime for the breach order - a static holding LAST raid's
            // wall would point the new warband at a destroyed object from frame one.
            TroopBreachOrder.Clear();

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
            TroopBreachOrder.Clear();   // WO-1719: nor a breach order (it holds a scene object)
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
            // WO-1810 - a watchdog exit is not a player retreat: it is a raid that never finalized,
            // which is a FAIL. Latched, so a retreat that already declared itself is untouched.
            DeclareRaidExitOutcome(RaidExitOutcome.Failed, "stranding watchdog: " + reason);
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
            SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.time_expired").Resolve());
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

            // WO-1719: Breach is checked FIRST and RETURNS unconditionally, so a breach tap
            // can never also move the rally flag. The three arm states are mutually exclusive
            // (ToggleBreach / ToggleRally / ArmTile each zero the other two), so at most one
            // of these branches is live at a time - this order only settles the tie if a
            // future edit ever breaks that exclusivity, and it settles it toward the mode the
            // player most recently pressed.
            if (_breachMode) { HandleBreachTap(screenPoint); return; }
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

        // ═════════════════════════════════════════════════════════════════════
        // ⛔ WO-1777 — THE UI GUARD USED TO SEE ONLY THIS CONTROLLER'S OWN CANVAS.
        // ---------------------------------------------------------------------
        // MEASURED, not theorised (owner Bastion capture
        // Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt): of 41 distinct
        // `HandleBreachTap IN - screenPoint=` values, FORTY sat in one ~100x50 px box at
        // x 774-876, y 78-130 of a 2670x1200 screen - i.e. normalised x 0.290-0.328,
        // y 0.065-0.108 - and every one resolved the not-a-wall outcome on 'RaidGround'.
        // Forty presses inside one small box is a finger on a fixed button, and that box is
        // inside HudLayoutBands.ThumbActionRowMinY..MaxY (0.015-0.150), the band the owner
        // reserved for the kit ability row on 2026-09-06. With Breach armed those presses were
        // harmless noise; with Deploy armed the SAME press also drops a troop on the ground in
        // front of the hero, because both paths share this one guard.
        //
        // WHY THE GUARD COULD NOT SEE IT: the old body kept only hits satisfying
        // `IsChildOf(_ui.transform)` - this controller's own canvas. The ability faces are built
        // by the kit in DeNelle.HUD, on a different canvas, and DeNelle.Village may not
        // reference DeNelle.HUD (CLAUDE.md sec.5). So the test is now made by COMPONENT IDENTITY
        // (Selectable / IEventSystemHandler / Canvas / GraphicRaycaster - all UnityEngine.UI or
        // UnityEngine.EventSystems types) plus the SHARED BAND DATA in DeNelle.Core.UI. No type
        // reference into DeNelle.HUD is added, and none is needed.
        //
        // ⚠ THE BAND CLAUSE IS WHAT MAKES THE FIX INDEPENDENT OF AN UNPROVEN FACT. Which ability
        // FACE sits at x ~ 820 was NOT established by the audit lane (WO-1777 sec.5 acceptance 4
        // records it as open). The band clause catches all forty taps by arithmetic alone, so the
        // fix never has to know. It is derived from HudLayoutBands - never a literal typed here
        // (the duplicated-state failure CLAUDE.md sec.2 / sec.5 / sec.16 each describe).
        //
        // ⚠ AND IT IS DELIBERATELY NOT AN ALL-GRAPHICS RULE. RaidHudController's readout is
        // tap-transparent by design; a rule that consumed ANY raycast hit would swallow
        // legitimate world taps under a backing plate. Only an INTERACTABLE component consumes -
        // and `interactable`/`enabled` are NOT tested, because a cooldown-locked ability face
        // must still eat the press (otherwise "press a greyed face -> a troop deploys" replaces
        // the bug with a subtler one).
        //
        // ⛔ THE JOYSTICK IS LEFT EXACTLY AS IT WAS (lead instruction, 2026-09-16). It needs no
        // clause here: VirtualJoystick polls UnityEngine.Input directly and sets
        // `img.raycastTarget = false` (VirtualJoystick.cs:12-13, :219), so it never appears in a
        // graphic raycast at all and this guard cannot change its behaviour. WO-1777 sec.4 item 3
        // (also calling VirtualJoystick.IsInZone here) is therefore DEFERRED, not done.
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>Guard verdict: the hit was on this controller's own canvas.</summary>
        public const string UiRuleOwnCanvas = "own-canvas-hit";
        /// <summary>Guard verdict: a foreign canvas' Selectable (button / toggle / slider) was under the finger.</summary>
        public const string UiRuleSelectable = "foreign-selectable";
        /// <summary>Guard verdict: a foreign canvas' pointer-event handler was under the finger.</summary>
        public const string UiRuleEventHandler = "foreign-event-handler";
        /// <summary>Guard verdict: the point fell inside the reserved kit ability-row band.</summary>
        public const string UiRuleThumbBand = "reserved-thumb-band";
        /// <summary>Guard verdict: nothing consumed the point — it is a world tap.</summary>
        public const string UiRuleNone = "none";

        /// <summary>
        /// True when a screen point is consumed by UI and must NEVER become a world tap.
        /// Covers EVERY canvas the player can press during a raid (this HUD's tray, the kit
        /// ability row / dock, the raid readout, toasts) plus the reserved ability-row band.
        /// Traces WHAT consumed it — measured, never inferred (CLAUDE.md sec.11B). Null-safe.
        /// </summary>
        private bool IsPointerOverUi(Vector2 screenPoint)
        {
            bool consumed = TryFindUiConsumer(screenPoint, out string consumerName, out string canvasName,
                                              out bool ownCanvas, out string rule, out int hitCount);
            if (!consumed) return false;

            // Nested quotes are built into LOCALS first: CompileGate.BraceBalanced has no
            // interpolated-string model, so a '"' inside an interpolation hole ends the string
            // for its scanner and the file reads unbalanced (CLAUDE.md sec.1, WO-1096).
            string scope = ownCanvas ? "own" : "foreign";
            string consumerQuoted = "'" + consumerName + "'";
            string canvasQuoted = "'" + canvasName + "'";
            DeNelle.Core.Diagnostics.FlowTrace.Throttle("Raid", "world-tap-rejected-ui", 0.25f,
                "world tap REJECTED as UI: screenPoint=" + screenPoint +
                " screenNorm=" + DescribeScreenNorm(screenPoint) +
                " uiHits=" + hitCount +
                " consumer=" + consumerQuoted +
                " canvas=" + canvasQuoted +
                " scope=" + scope +
                " rule=" + rule +
                " - MEASURED ONLY: consumer/canvas are what the graphic raycast reported under " +
                "this point, or the reserved band when rule=" + UiRuleThumbBand + ". No deploy, " +
                "no rally, no breach this frame.");
            return true;
        }

        /// <summary>
        /// The guard's measurement. Raycasts every registered GraphicRaycaster through
        /// EventSystem.current (which iterates ALL of them, not just this canvas'), falling back
        /// to resolving the raycasters BY COMPONENT when no EventSystem exists (headless /
        /// EditMode). Then decides consumption by component identity, and last by the reserved
        /// ability-row band. Out-params name what was found so the trace can state it.
        /// </summary>
        private bool TryFindUiConsumer(Vector2 screenPoint, out string consumerName, out string canvasName,
                                       out bool ownCanvas, out string rule, out int hitCount)
        {
            consumerName = "<none>";
            canvasName = "<none>";
            ownCanvas = false;
            rule = UiRuleNone;
            hitCount = 0;

            var hits = new List<UnityEngine.EventSystems.RaycastResult>();
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                // Deliberately RaycastAll and not IsPointerOverGameObject(): the latter needs a
                // per-touch pointerId under the new Input System and warns when called outside
                // event processing. RaycastAll also hands back WHICH object was hit, which the
                // trace above has to report.
                var data = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPoint };
                es.RaycastAll(data, hits);
            }
            else
            {
                var casters = FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None);
                var probe = new UnityEngine.EventSystems.PointerEventData(null) { position = screenPoint };
                for (int i = 0; i < casters.Length; i++)
                {
                    var caster = casters[i];
                    if (caster == null || !caster.isActiveAndEnabled) continue;
                    DeNelle.Core.Diagnostics.Guard.Try("Raid", "ui guard: raycast a GraphicRaycaster",
                        () => caster.Raycast(probe, hits));
                }
            }
            hitCount = hits.Count;

            for (int i = 0; i < hits.Count; i++)
            {
                var go = hits[i].gameObject;
                if (go == null) continue;

                // This HUD's own canvas: ANY hit consumes, unchanged from the pre-WO-1777
                // behaviour — a press on the tray's backing plate between two tiles must not
                // fall through to the ground either.
                if (_ui != null && go.transform.IsChildOf(_ui.transform))
                {
                    consumerName = go.name;
                    canvasName = DescribeCanvas(go);
                    ownCanvas = true;
                    rule = UiRuleOwnCanvas;
                    return true;
                }

                // A foreign canvas: consume only an INTERACTABLE component, by type identity.
                var sel = go.GetComponentInParent<Selectable>(true);
                if (sel != null)
                {
                    consumerName = sel.gameObject.name;
                    canvasName = DescribeCanvas(sel.gameObject);
                    rule = UiRuleSelectable;
                    return true;
                }
                var handler = go.GetComponentInParent<UnityEngine.EventSystems.IEventSystemHandler>(true);
                var handlerComponent = handler as Component;
                if (handlerComponent != null)
                {
                    consumerName = handlerComponent.gameObject.name;
                    canvasName = DescribeCanvas(handlerComponent.gameObject);
                    rule = UiRuleEventHandler;
                    return true;
                }
            }

            // Last: the band the owner reserved for the kit ability row. A round medallion does
            // not fill its cell, so a press in the gap BETWEEN two faces raycasts nothing — and
            // that press is still the player reaching for an ability, not for the ground.
            if (IsInReservedThumbBand(screenPoint))
            {
                consumerName = "<reserved ability-row band>";
                canvasName = "<kit actionBar band (shared data, not a raycast hit)>";
                rule = UiRuleThumbBand;
                return true;
            }
            return false;
        }

        /// <summary>
        /// True when a screen point (px, origin bottom-left) falls inside the band reserved for
        /// the kit ability row. Screen-space wrapper; the arithmetic lives in the pure overload.
        /// </summary>
        public static bool IsInReservedThumbBand(Vector2 screenPoint)
        {
            int w = Screen.width;
            int h = Screen.height;
            if (w <= 0 || h <= 0) return false;
            return IsInReservedThumbBand(screenPoint.x / w, screenPoint.y / h);
        }

        /// <summary>
        /// Normalised-coordinate form of the reserved ability-row band test. PURE — no screen, no
        /// canvas, no scene — so a headless oracle can drive it. The band is read from
        /// <see cref="HudLayoutBands"/>: y from ThumbActionRowMinY to ThumbActionRowMaxY, x from
        /// MoveClusterMount.xMax (documented there as also the actionBar's left edge, and pinned
        /// equal to it by RaidHudThumbBandRegression) to the right screen edge. NEVER a literal.
        /// </summary>
        public static bool IsInReservedThumbBand(float nx, float ny)
        {
            Rect band = HudLayoutBands.ThumbActionRowBand(HudLayoutBands.MoveClusterMount.xMax, 1f);
            return band.Contains(new Vector2(nx, ny));
        }

        /// <summary>Root canvas name for a hit object, or a stated reason there is none.</summary>
        private static string DescribeCanvas(GameObject go)
        {
            if (go == null) return "<null>";
            var c = go.GetComponentInParent<Canvas>(true);
            if (c == null) return "<no canvas>";
            var root = c.rootCanvas;
            return root != null ? root.name : c.name;
        }

        /// <summary>
        /// A screen point as a normalised "x,y" pair. WO-1790 sec.3 item 4: this is the ONE field
        /// that would have made WO-1777's real cause obvious on first read.
        /// </summary>
        private static string DescribeScreenNorm(Vector2 screenPoint)
        {
            int w = Screen.width;
            int h = Screen.height;
            if (w <= 0 || h <= 0) return "<no screen>";
            float nx = screenPoint.x / w;
            float ny = screenPoint.y / h;
            return nx.ToString("0.000") + "," + ny.ToString("0.000");
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
            // WO-1777 acceptance 1 greps this exact prefix to prove no deploy tap survives the UI
            // guard in the bottom band, so the line must exist and must carry the screen point.
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "HandleDeployTap IN - screenPoint=" + screenPoint +
                " screenNorm=" + DescribeScreenNorm(screenPoint) +
                " inReservedThumbBand=" + IsInReservedThumbBand(screenPoint) +
                " armedDefId='" + _armedDefId + "' (a true inReservedThumbBand here would mean the " +
                "WO-1777 UI guard regressed - the guard refuses that band before this method runs).");

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
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.deploy_tap_inside").Resolve());
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    $"DEPLOY REFUSED - tap resolved to {hit.point} on '{(hit.collider != null ? hit.collider.name : "?")}', " +
                    $"which has no baked NavMesh within {TroopFactory.NavSampleRadius}m. Spawning here would " +
                    "produce an inert troop that never fights and still counts as a survivor at reconcile " +
                    $"(inflating SurvivalPct past the {RaidScoring.HighSurvivalPct * 100f:0}% 3-star axis). " +
                    "No troop consumed; the tile stays armed.");
                return;
            }

            var army = Army();
            if (army == null) { SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.no_army").Resolve()); return; }

            // The next deployable troop of the armed type (healthy, not already deployed).
            PlayerTroop next = NextDeployableOfType(army, _armedDefId);
            if (next == null)
            {
                SetStatus(DeNelle.Core.UI.LocalText.Format("village.troops.raid_deploy.no_more_ready", DisplayName(_armedDefId)));
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
                SetStatus(DeNelle.Core.UI.LocalText.Format("village.troops.raid_deploy.couldnt_deploy", DisplayName(_armedDefId)));
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
        //  BREACH (WO-1719) — the player picks the wall the warband cracks
        // ---------------------------------------------------------------------
        // Owner ruling 2026-09-14, verbatim: "tap the wall segment directly, and it
        // overrides ... add a button for breach and select a wall segment" / "the most
        // damanged [stays the fallback]" / "all together unless they have aggro".
        //
        // ⛔ THIS IS A MODE, NOT A CHANGE TO THE DEFAULT TAP. WO-1717 sec.3b proved a raw
        // wall tap already resolves - RaycastGround falls through to ~0, so it silently
        // becomes a rally muster point ON the masonry. That behaviour is UNCHANGED here:
        // with Breach off, Update never reaches HandleBreachTap and the tap is the same
        // rally it has always been. Making a wall-hit rally tap IMPLICITLY mean "breach"
        // was the alternative and was rejected - it would have retargeted the whole warband
        // from a mis-tap, with no way to say "no, just muster there".
        // =====================================================================

        private void HandleBreachTap(Vector2 screenPoint)
        {
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"HandleBreachTap IN - screenPoint={screenPoint}, breachModeOn={_breachMode} " +
                "screenNorm=" + DescribeScreenNorm(screenPoint) +
                " inReservedThumbBand=" + IsInReservedThumbBand(screenPoint) +
                " (sanity trace only - Update's _breachMode gate means this should always read true; " +
                "a false here would mean this method was reached with breach mode already off. " +
                "WO-1777: a true inReservedThumbBand here would mean the UI guard regressed - the " +
                "guard refuses that band before this method runs).");

            // 2026-09-14 breach-tap-miss investigation (do NOT strip — CLAUDE.md §12):
            // captured "hit 'RaidGround'" on a tap squarely on a rendered wall, while the
            // SAME-frame hostile-structure sweep (mask=Enemy|Structure) found the same
            // WallSegment fine. Source-read comparison against WallRepairController.HandleTap
            // (the production-proven wall-tap-select path) did NOT find a mask/trigger/distance
            // mismatch: this controller's _groundMask defaults to ~0 (never overridden by any
            // prefab/scene — self-installs via a bare AddComponent, WallSegment self-install at
            // :195), so RaycastGround's mask-then-fallback already queries ALL layers on its
            // FIRST try here — the "falls through to ~0" note two paragraphs up describes the
            // general helper, not a narrower mask actually in effect on this controller. Ruled
            // out too: WallSegment.ApplyTierBlockerHeight (WallSegment.cs:195-200) early-returns
            // for every raid wall (no PlacedStructure), and RaidBaseGenerator.PlaceSegment sizes
            // the BoxCollider straight from renderer bounds. None of that explains the miss —
            // the diagnostics below exist to pin the ACTUAL cause on the next live capture.
            if (!RaycastGround(screenPoint, out RaycastHit hit))
            {
                LogBreachTapDiagnostics(screenPoint, false, default);
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_tap_wall").Resolve());
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"HandleBreachTap OUT: outcome=raycast_miss screenPoint={screenPoint} " +
                    "screenNorm=" + DescribeScreenNorm(screenPoint) +
                    " inReservedThumbBand=" + IsInReservedThumbBand(screenPoint) +
                    " - MEASURED: RaycastGround matched no collider at all (masked mask and the ~0 " +
                    "fallback both returned false). No wall resolved, standing order (if any) " +
                    "UNCHANGED. See breach-tap-diag-* lines above for the raycast/collider detail; " +
                    "no cause is asserted here.");
                return;
            }

            // GetComponentInParent, not GetComponent: a wall's collider commonly lives on a
            // child mesh, and a raycast returns THAT collider. Looking only at the hit object
            // would refuse perfectly good taps on exactly the panels the player aims at.
            var wall = hit.collider != null ? hit.collider.GetComponentInParent<WallSegment>() : null;
            if (wall == null)
            {
                LogBreachTapDiagnostics(screenPoint, true, hit);
                // A miss is a NO-OP with a hint, never a clear. Losing a standing order to a
                // stray tap on the ground is the failure the player cannot see or undo; the
                // Breach toggle is the visible cancel (ToggleBreach), mirroring ToggleRally.
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_not_wall").Resolve());
                // WO-1790: locals first (CompileGate has no interpolated-string model, CLAUDE.md
                // sec.1), and the message states ONLY what was measured.
                string hitName = hit.collider != null ? hit.collider.name : "nothing";
                string hitLayer = hit.collider != null
                    ? LayerMask.LayerToName(hit.collider.gameObject.layer) : "<n/a>";
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "breach tap resolved no WallSegment (hit '" + hitName +
                    "') - the standing order, if any, is UNCHANGED.");
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "HandleBreachTap OUT: outcome=not_wall_segment hitCollider='" + hitName +
                    "' hitLayer=" + hitLayer +
                    " hitPoint=" + hit.point +
                    " hitDistance=" + hit.distance.ToString("0.00") +
                    " screenPoint=" + screenPoint +
                    " screenNorm=" + DescribeScreenNorm(screenPoint) +
                    " inReservedThumbBand=" + IsInReservedThumbBand(screenPoint) +
                    " - MEASURED: the ray resolved that collider and GetComponentInParent" +
                    "<WallSegment>() found no WallSegment on it or its parents. Nothing more is " +
                    "claimed: this line does NOT say the wall hierarchy, the collider or the ray " +
                    "is at fault. No order placed, standing order (if any) UNCHANGED.");
                return;
            }

            if (!wall.IsAlive)
            {
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_section_down").Resolve());
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"HandleBreachTap OUT: outcome=wall_already_dead wall='{wall.name}' - hit a real " +
                    "WallSegment but wall.IsAlive is false (that section already collapsed). No order " +
                    "placed; standing order (if any) UNCHANGED - the player must pick a section that " +
                    "is still standing.");
                return;
            }

            if (wall.Faction != DeNelle.Core.Combat.CombatFaction.Hostile)
            {
                // Faction is DERIVED from SceneOwnership (WO-1717 sec.2), so in a raid every
                // base wall reads Hostile. A friendly one here means the tap found the wrong
                // scene's masonry - refuse rather than order the warband onto their own wall.
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_not_enemy").Resolve());
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"HandleBreachTap OUT: outcome=wrong_faction wall='{wall.name}' faction={wall.Faction} " +
                    "- hit a live WallSegment but it did not resolve Hostile (expected in a raid scene, " +
                    "faction is derived from SceneOwnership per WO-1717 sec.2). Refused rather than " +
                    "order the warband onto friendly masonry; standing order (if any) UNCHANGED.");
                return;
            }

            TroopBreachOrder.Set(wall);
            SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_ordered").Resolve());
            RefreshBreachButton();
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"HandleBreachTap OUT: outcome=success wall='{wall.name}' faction={wall.Faction} - " +
                "TroopBreachOrder.Set(wall) placed (that call logs its own line too), Breach button " +
                "refreshed. The whole warband now targets this section, overriding the automatic " +
                "most-damaged pick until the order is cleared or the wall falls.");
        }

        /// <summary>
        /// 2026-09-14 breach-tap-miss investigation (CLAUDE.md §12 — additive, permanent;
        /// never strip). Called from every branch of <see cref="HandleBreachTap"/> that did
        /// NOT resolve a WallSegment, so a live capture can pin the ACTUAL cause instead of
        /// one more guess. Throttled per-tap-window (not per-frame) so a rapid double-tap on
        /// a genuine miss cannot flood the log and evict the boot window (memory:
        /// logcat-ring-buffer-destroys-evidence) — 0.25s still lets a second deliberate test
        /// tap through, per the owner's own workflow of tapping again to retry.
        /// </summary>
        /// <param name="groundHit">Whether the masked RaycastGround call (the actual
        /// gameplay decision) matched ANYTHING at all.</param>
        /// <param name="hit">The RaycastGround result, only meaningful when groundHit is true.</param>
        private void LogBreachTapDiagnostics(Vector2 screenPoint, bool groundHit, RaycastHit hit)
        {
            // -- mask + camera identity -----------------------------------------------
            int maskValue = _groundMask.value;
            string maskLayers = DescribeLayerMask(maskValue);
            string camName = _camera != null ? _camera.name : "<null>";
            bool camIsMain = _camera != null && Camera.main != null && _camera == Camera.main;
            string camPixelRect = _camera != null ? _camera.pixelRect.ToString() : "<n/a>";
            Ray ray = _camera != null ? _camera.ScreenPointToRay(screenPoint) : default;

            string maskedLine =
                $"mask=0x{maskValue:X8} ({maskLayers}) rayDistance={_rayDistance:0} " +
                $"queriesHitTriggers={Physics.queriesHitTriggers} camera='{camName}' " +
                $"isCameraMain={camIsMain} camPixelRect={camPixelRect} screenSize={Screen.width}x{Screen.height} " +
                $"screenPoint={screenPoint} screenNorm={DescribeScreenNorm(screenPoint)} " +
                $"inReservedThumbBand={IsInReservedThumbBand(screenPoint)} " +
                $"rayOrigin={ray.origin} rayDir={ray.direction} " +
                $"maskedCallMatched={groundHit}" +
                (groundHit
                    ? $" hitPoint={hit.point} hitName='{(hit.collider != null ? hit.collider.name : "<null>")}' " +
                      $"hitLayer={(hit.collider != null ? LayerMask.LayerToName(hit.collider.gameObject.layer) : "<n/a>")} " +
                      $"hitIsTrigger={(hit.collider != null && hit.collider.isTrigger)} hitDistance={hit.distance:0.00}"
                    : "");
            DeNelle.Core.Diagnostics.FlowTrace.Throttle("Raid", "breach-tap-diag-mask", 0.25f, maskedLine);

            // -- unmasked RaycastAll(~0), sorted by distance, up to the first WallSegment
            //    or 5 hits, whichever comes first ---------------------------------------
            var allHits = Physics.RaycastAll(ray, _rayDistance, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));
            var sb = new System.Text.StringBuilder("unmasked RaycastAll (~0, sorted): ");
            int shown = 0;
            bool sawWall = false;
            for (int i = 0; i < allHits.Length && shown < 5 && !sawWall; i++)
            {
                var h = allHits[i];
                if (h.collider == null) continue;
                var seg = h.collider.GetComponentInParent<WallSegment>();
                sb.Append($"[{shown}] '{h.collider.name}' layer={LayerMask.LayerToName(h.collider.gameObject.layer)} " +
                          $"isTrigger={h.collider.isTrigger} dist={h.distance:0.00} isWallSegment={seg != null}; ");
                shown++;
                if (seg != null) sawWall = true;
            }
            if (shown == 0) sb.Append("(no colliders along the ray at all)");
            DeNelle.Core.Diagnostics.FlowTrace.Throttle("Raid", "breach-tap-diag-rayall", 0.25f, sb.ToString());

            // -- WallSegment nearest THE RAY (WO-1790) ---------------------------------
            // ⛔ THIS BLOCK USED TO DRAW A CONCLUSION IT HAD NOT MEASURED, AND IT COST A WRONG P0.
            // Three defects, all fixed here, recorded so neither comes back:
            //
            //   1. It picked the wall nearest the CAMERA — the squared magnitude of the wall's
            //      position minus the RAY ORIGIN. For a tap that never went near a wall that is an
            //      ARBITRARY wall, so both `…IntersectsRay=False` values were expected noise about
            //      a wall nobody aimed at. It now picks the wall nearest THE RAY (perpendicular
            //      distance to the ray, t clamped >= 0) and PRINTS that distance, so the reader can
            //      see for themselves whether the wall is relevant at all.
            //   2. It unioned the segment's child renderers with includeInactive set TRUE, and
            //      printed that under a name reading as "what the player sees". On an IronBastion
            //      segment that union swallows the inactive placeholder twin and the Ruin_* rubble
            //      tiles, which is how 2.00 m of collider read as "half of a 8.24 m wall".
            //      WO-1723 sec.6 had already retired that exact reading nine days earlier. It now
            //      reports ACTIVE, ENABLED renderers only, under the name activeRendererBounds,
            //      with the counted/skipped tally beside it.
            //   3. The comment here told the reader that two Falses meant "the ray itself is
            //      wrong", and the sibling OUT line invited suspicion of the wall hierarchy. Both
            //      inferences are DELETED. This block states measurements; the reader concludes.
            //
            // ⛔ Do NOT strip this block (CLAUDE.md sec.12, owner ruling 2026-08-09). It may be
            // flagged off. The fix for a lying instrument is to make it truthful, never to delete it.
            var walls = FindObjectsByType<WallSegment>(FindObjectsSortMode.None);
            WallSegment nearest = null;
            float nearestRayDist = float.MaxValue;
            for (int i = 0; i < walls.Length; i++)
            {
                if (walls[i] == null) continue;
                float d = DistanceFromRay(ray, walls[i].transform.position);
                if (d < nearestRayDist) { nearestRayDist = d; nearest = walls[i]; }
            }
            if (nearest != null)
            {
                var col = nearest.GetComponent<Collider>();
                var rends = nearest.GetComponentsInChildren<Renderer>(false);
                Bounds? rBounds = null;
                int counted = 0;
                int skipped = 0;
                int transientSkipped = 0;
                for (int i = 0; i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) { skipped++; continue; }
                    // WO-1722 item 1 — proven cause (CastingTelegraphVfx.TryBeginTargetMarker
                    // parents a transient "CastTargetMarker" AoE-scaled VFX onto whatever unit a
                    // spell wind-up targets). When the target is this WallSegment, that marker is
                    // a live, ACTIVE child during the windup window, so the active-only filter
                    // above does not exclude it — it must be named and skipped explicitly, the
                    // same way RaidWallTierProof.IsTransientCastMarker already does on the editor
                    // side (Assets/Editor/RaidWallTierProof.cs:207-212). Same ONE literal name,
                    // not per-wall-id.
                    if (IsTransientCastMarker(r.transform)) { transientSkipped++; continue; }
                    if (rBounds == null) rBounds = r.bounds;
                    else { var b = rBounds.Value; b.Encapsulate(r.bounds); rBounds = b; }
                    counted++;
                }
                bool colliderIntersects = col != null && col.bounds.IntersectRay(ray);
                bool rendererIntersects = rBounds.HasValue && rBounds.Value.IntersectRay(ray);

                // Locals first — CompileGate's brace scanner has no interpolated-string model.
                string colliderEnabledText = (col != null && col.enabled).ToString();
                string colliderTriggerText = (col != null && col.isTrigger).ToString();
                string colliderLayerText = col != null ? LayerMask.LayerToName(col.gameObject.layer) : "<n/a>";
                string colliderBoundsText = col != null ? col.bounds.ToString() : "<n/a>";
                string activeRendererBoundsText = rBounds.HasValue ? rBounds.Value.ToString() : "<none active>";
                string nearestLine =
                    "WallSegment nearest THE RAY='" + nearest.name + "'" +
                    " nearestWallDistToRay=" + nearestRayDist.ToString("0.00") +
                    " wallsInScene=" + walls.Length +
                    " colliderPresent=" + (col != null) +
                    " colliderEnabled=" + colliderEnabledText +
                    " colliderIsTrigger=" + colliderTriggerText +
                    " colliderLayer=" + colliderLayerText +
                    " colliderBounds=" + colliderBoundsText +
                    " activeRendererBounds=" + activeRendererBoundsText +
                    " activeRenderersCounted=" + counted +
                    " renderersSkippedInactiveOrDisabled=" + skipped +
                    " renderersSkippedTransientCastMarker=" + transientSkipped +
                    " colliderBoundsIntersectsRay=" + colliderIntersects +
                    " activeRendererBoundsIntersectsRay=" + rendererIntersects +
                    " - MEASURED ONLY. nearestWallDistToRay is the perpendicular distance from the " +
                    "ray to that wall's origin: a large value means this wall was never aimed at, " +
                    "and its two IntersectsRay flags say nothing about the tap. No cause is asserted " +
                    "here.";
                DeNelle.Core.Diagnostics.FlowTrace.Throttle("Raid", "breach-tap-diag-nearest", 0.25f, nearestLine);
            }
        }

        /// <summary>
        /// WO-1722 item 1 — the ONE literal name production code assigns the transient combat-VFX
        /// target-lock marker (<c>CastingTelegraphVfx.TryBeginTargetMarker</c>,
        /// Assets/_Modules/Village/Vfx/CastingTelegraphVfx.cs:257-268), instantiated PARENTED to
        /// whatever unit a spell wind-up is targeting and self-destroying windup+1s later. Ported
        /// from the editor-side proof (RaidWallTierProof.IsTransientCastMarker) so this RUNTIME
        /// diagnostic stops reporting an AoE-scaled VFX's bounds as "the wall's visual footprint".
        /// Excludes generically, by name, never by wall id.
        /// </summary>
        private static bool IsTransientCastMarker(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur.name == "CastTargetMarker") return true;
            return false;
        }

        /// <summary>
        /// Perpendicular distance from <paramref name="ray"/> to <paramref name="point"/>, with the
        /// ray parameter clamped to t &gt;= 0 so geometry BEHIND the camera measures from the ray
        /// origin instead of reporting a false near miss. WO-1790 sec.3 item 1.
        /// </summary>
        private static float DistanceFromRay(Ray ray, Vector3 point)
        {
            Vector3 dir = ray.direction;
            float len = dir.magnitude;
            if (len <= 0.0001f) return (point - ray.origin).magnitude;
            dir /= len;
            float t = Vector3.Dot(point - ray.origin, dir);
            if (t < 0f) t = 0f;
            Vector3 closest = ray.origin + dir * t;
            return (point - closest).magnitude;
        }

        /// <summary>Names the layers a mask value resolves to (0..31), for a diagnostic line.</summary>
        private static string DescribeLayerMask(int mask)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                string n = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(n)) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(n);
            }
            return sb.Length > 0 ? sb.ToString() : "none-named";
        }

        /// <summary>
        /// Arm / disarm Breach mode. Turning it OFF also CLEARS the standing order, exactly
        /// as <see cref="ToggleRally"/> clears <see cref="TroopRally.Point"/> - the toggle is
        /// the player's only visible cancel, and a button reading "Breach" while the warband
        /// still obeys an invisible old pick is the same confusion that comment records.
        /// </summary>
        private void ToggleBreach()
        {
            bool wasOn = _breachMode;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"ToggleBreach IN - player touched the Breach button. wasOn={wasOn} -> " +
                (wasOn ? "DISARMING breach mode." : "ARMING breach mode (next wall tap orders the warband)."));

            _breachMode = !_breachMode;
            // WO-1746 (owner ruling WO-1738): Breach is a persistent STANCE, and this button is
            // where the player arms it. Armed BEFORE the first wall tap on purpose - "we are
            // breaching" is the player's declaration, so a blocked warband with no tap yet already
            // works the automatic most-damaged panel at full damage instead of going reluctant.
            // The disarm rides on TroopBreachOrder.Clear() in the else branch below.
            if (_breachMode) TroopBreachOrder.SetStanceArmed(true);
            if (_breachMode)
            {
                Disarm();               // breach + deploy are exclusive arm states
                _rallyMode = false;     // breach + rally are exclusive arm states
                RefreshRallyButton();
                RefreshTiles();
                EnsureBreachMarker();   // WO-1723 Q2 - the ordered panel gets a visible bracket
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_tap_wall").Resolve());
            }
            else
            {
                bool hadOrder = TroopBreachOrder.HasOrder;
                TroopBreachOrder.Clear();
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_order_dropped").Resolve());
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"ToggleBreach OUT - breach mode DISARMED. hadStandingOrder={hadOrder} " +
                    (hadOrder
                        ? "- an explicit wall order WAS cleared, the automatic most-damaged rule takes back over."
                        : "- no standing order existed, this was a no-op clear."));
            }
            RefreshBreachButton();

            if (_breachMode)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "ToggleBreach OUT - breach mode ARMED. Deploy disarmed, rally disarmed, " +
                    "tiles/rally button refreshed; awaiting a wall tap in HandleBreachTap.");
            }
        }

        /// <summary>
        /// WO-1723 Lane B, owner ruling Q2 (2026-09-14): Breach mode STAYS ARMED after a
        /// successful order, and the ordered panel gets a clear visual highlight so the player can
        /// see which section the warband is on and that a re-tap MOVED it. Created once, lazily,
        /// when Breach first arms — the marker then follows <see cref="TroopBreachOrder"/> by its
        /// own Version poll, including the two transitions no writer here would announce: the
        /// ordered panel COLLAPSING (the order self-clears) and scene teardown.
        /// </summary>
        private void EnsureBreachMarker()
        {
            if (_breachMarker != null) return;
            _breachMarker = BreachOrderMarker.Create(transform);
        }

        private void RefreshBreachButton()
        {
            if (_breachButton == null) return;
            var lbl = _breachButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (lbl != null) lbl.text = _breachMode ? new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_on").Resolve() : new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_button").Resolve();
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
            SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.rally_set").Resolve());
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
            if (_rallyMode)
            {
                Disarm();   // rally + deploy are exclusive arm states
                // WO-1719: the MODE is exclusive; the standing breach ORDER is NOT cleared
                // here. They are two axes - the player may aim a rally while the warband is
                // already committed to a panel, the same way ArmTile leaves TroopRally.Point
                // alone. Only the Breach toggle, a retreat and teardown drop the order.
                _breachMode = false;
                // WO-1746: the MODE flag goes with it, but TroopBreachOrder.StanceActive stays
                // TRUE while that standing order lives (its `|| HasOrder` half) - aiming a rally
                // must not silently drop the warband to 10% on the panel it is still ordered onto.
                TroopBreachOrder.SetStanceArmed(false);
                RefreshBreachButton();
            }
            else
            {
                // Turning Rally off is also the player's only visible way to cancel a
                // previously placed muster point. Leaving TroopRally.Point populated here
                // made the tray read "Rally" while troops kept walking to the old flag and
                // never resumed the spire push (the Seeker capture showed exactly that
                // confusion: Rally ON + a live flag beside the squad). Clear both the
                // shared command and its marker so the next idle scan can follow the
                // objective again.
                TroopRally.Clear();
                if (_rallyFlag != null) _rallyFlag.SetActive(false);
            }
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
                // WO-1810 - THIS LINE USED TO PROMISE "the fallen recover", which is no longer true
                // and was the defect the owner reported: the fallen are dead and the retreat itself
                // costs a share of the survivors. The player must be told the price BEFORE the tap.
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.retreat_confirm").Resolve());
                if (_retreatButton != null)
                {
                    var lbl = _retreatButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    if (lbl != null) lbl.text = new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.confirm_retreat_button").Resolve();
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
            // WO-1810 - DECLARE THE EXIT BEFORE SETTLING IT. The timeout funnels through this same
            // method and its toast says "your warband retreats", but a player who ran out of clock
            // did NOT retreat: the reason string is the only honest discriminator, and a timeout is
            // a FAIL (100% of the survivors) while a chosen retreat costs 60% of them.
            bool playerRetreat = !string.Equals(reason, DeNelle.Village.UI.EndStateVM.TimeoutReason,
                                                System.StringComparison.OrdinalIgnoreCase);
            DeclareRaidExitOutcome(
                playerRetreat ? RaidExitOutcome.Retreat : RaidExitOutcome.Failed,
                playerRetreat ? "the player pressed Retreat" : "the raid clock expired (reason=" + reason + ")");

            SettlePartialLoot(reason);

            // A retreat / clock-expiry exit is never a 3-star clear -> 0 stars, no veterancy.
            ReconcileRaidEnd(0);

            TroopRally.Clear();
            TroopBreachOrder.Clear();   // WO-1719: the order dies with the raid it was given in
            GameStateService.Instance?.Save();
            SetStatus(string.Equals(reason, DeNelle.Village.UI.EndStateVM.TimeoutReason,
                                    System.StringComparison.OrdinalIgnoreCase)
                ? new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.time_falling_back").Resolve()
                : new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.retreating").Resolve());

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
                    _lastDeployedCount, _lastSurvivorCount,
                    // WO-1810 - the screen states the COST: how many troops this raid lost for good.
                    // The REASON half is already the factory's first argument, so the sentence is
                    // composed by the VM from the exit it was already told about - never here.
                    _lastLostCount);

                DeNelle.Village.UI.EndStateView.Show(vm);

                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "NON-VICTORY RESULT shown (" + reason + "): stars=" +
                    (result != null ? result.Stars.ToString() : "unknown") + " razed=" +
                    (result != null ? result.DestructionPercent + "%" : "unknown") +
                    " banked=" + _retreatCredited.Wood + "w/" + _retreatCredited.Iron + "i/" +
                    _retreatCredited.Stone + "f/" + _retreatCredited.Coins + "g/" +
                    _retreatCredited.Crystals + "c short=" + _retreatRewardShort +
                    " deployed=" + _lastDeployedCount + " survived=" + _lastSurvivorCount +
                    " lost=" + _lastLostCount + " returned=" + _lastReturnedCount +
                    ". Before WO-1561 this exit routed home with NO screen at all, and before " +
                    "WO-1810 it cost no troops at all.");
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

        // WO-1810 - what this raid actually COST, captured in the same place for the same reason.
        // -1 = never reconciled, and the screen then omits the line rather than printing a zero it
        // cannot prove. TotalLost = killed + the policy's share of the survivors; Returned = the
        // bodies that came home healthy.
        private int _lastLostCount = -1;
        private int _lastReturnedCount = -1;

        /// <summary>
        /// WO-1810 - troops this raid removed from the roster for good (killed + the exit's share of
        /// the survivors), or -1 if this raid never reconciled. Read by the VICTORY screen: a won raid
        /// still kills troops, so it has the same duty to say so as the retreat screen does. Read-only
        /// - <see cref="ReconcileRaidEnd"/> is the one writer.
        /// </summary>
        public int LastTroopsLost => _lastLostCount;

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

            // =================================================================
            //  OWNER RULING 2026-09-16 ~21:20 - A FAILED RAID PAYS NOTHING
            // =================================================================
            //  Verbatim answer to WO-1810's open question: a FAILED raid (timeout
            //  or wipe - any non-victory that is not a player RETREAT) pays NO
            //  loot; a player retreat keeps the partial loot scaled by damage
            //  done, exactly as today; victory is unchanged.
            //
            //  ⛔ THE SCORE IS STILL FINALIZED ABOVE, DELIBERATELY. Stars, razed %
            //  and the clock are what the result screen reports, and the Finalized
            //  latch is what stops a second exit paying twice - skipping Finalize
            //  would blank the screen and re-open that door. Only the PAYMENT is
            //  refused here.
            //
            //  ⚠ raid.lootFailPct (PROD022 #21, default 18) is NOT deleted: it
            //  still prices RaidScoring.LootFor and still pays a 0-star RETREAT.
            //  The fail branch simply never consults it.
            //
            //  ⚠ CONSEQUENCE, RECORDED NOT HIDDEN: the hero-death settlement
            //  declares no outcome (by design), so it resolves to fail and now
            //  pays nothing either. That reverses WO-1110's UNRULED default
            //  ("death pays what retreat pays") - and it is reversed by a RULING,
            //  which outranks it. Death and timeout are both "the warband did not
            //  come home"; only a chosen retreat brings the spoils back.
            bool paysLoot = _exitOutcome == RaidExitOutcome.Retreat;
            if (!paysLoot)
            {
                _retreatCredited = default(ResourceCost);
                _retreatRewardShort = false;
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "raid-end loot: outcome=fail paid=0 (owner ruling 2026-09-16). " +
                    $"Exit '{exitLabel}', declared outcome {RaidCasualtyPolicy.Word(_exitOutcome)}, " +
                    $"{result?.DestructionPercent ?? 0}% razed - the score is SETTLED (stars/razed/clock " +
                    "still report) but nothing is granted, so the result screen draws no spoils rows.");
                return;
            }

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"{exitLabel} settle: partial loot for {result?.DestructionPercent ?? 0}% razed " +
                "(outcome=retreat - the player chose to pull out, so the damage-scaled share is paid).");
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
                int w0 = eco.Wood, f0 = eco.Stone, i0 = eco.Iron, c0 = eco.Crystals, g0 = eco.Coins;
                eco.Grant(loot);
                int dw = eco.Wood - w0, df = eco.Stone - f0, di = eco.Iron - i0,
                    dc = eco.Crystals - c0, dg = eco.Coins - g0;
                _retreatCredited = new ResourceCost(wood: dw, stone: df, iron: di, crystals: dc, coins: dg);
                _retreatRewardShort = dw < loot.Wood || df < loot.Stone || di < loot.Iron
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
                int c0 = state.Resources.Crystals, f0 = state.Resources.Stone;
                if (loot.Crystals != 0) gs.AddCrystals(loot.Crystals);
                if (loot.Stone != 0) gs.AddStone(loot.Stone);
                int dcF = state.Resources.Crystals - c0, dfF = state.Resources.Stone - f0;
                _retreatCredited = new ResourceCost(stone: dfF, crystals: dcF);
                _retreatRewardShort = dcF < loot.Crystals || dfF < loot.Stone
                                   || loot.Wood != 0 || loot.Iron != 0 || loot.Coins != 0;
                LogRetreatCredit("GameStateService fallback", loot, 0, dfF, 0, dcF, 0);
                return;
            }

            DeNelle.Core.Diagnostics.FlowTrace.Fail("Raid",
                "RETREAT LOOT LOST - no EconomyService and no loaded GameState, so the settled " +
                $"partial loot (wood {loot.Wood}, iron {loot.Iron}, food {loot.Stone}, gold " +
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
                $"wood {dWood}/{requested.Wood}, food {dFood}/{requested.Stone}, iron {dIron}/{requested.Iron}, " +
                $"crystals {dCrystals}/{requested.Crystals}, gold {dCoins}/{requested.Coins} (credited/requested)";

            bool shortfall = dWood < requested.Wood || dFood < requested.Stone || dIron < requested.Iron
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
        /// WO-1768 — HAS THIS RAID'S ARMY ALREADY BEEN SETTLED? The latch above, made
        /// ANSWERABLE so a caller can narrate what its own call actually did.
        ///
        /// <para>THE CAPTURED DEFECT (owner Seeker, build 2026.09.16.371701, logcat
        /// pull-20260916-143101 lines 3059910/3059911): the hero's death path called
        /// <see cref="ReconcileRaidEnd"/> on an already-won raid, this latch no-oped and
        /// said so — "raid-end reconcile already ran for this raid - ignoring the duplicate
        /// call." — and HeroHealth's NEXT line still announced "army settled as a failure
        /// (0 stars); the troops still standing break and flee home, the fallen are
        /// wounded." Nothing had happened. The army was intact (army 10, deployable=10 read
        /// 0.09 s later) and the false line cost the RCA lane its first hour.</para>
        ///
        /// <para>A property rather than a bool return from <see cref="ReconcileRaidEnd"/>
        /// on purpose: no existing call site changes, and a caller reads it BEFORE and AFTER
        /// its own call to learn whether IT was the one that settled. Read-only — the latch
        /// keeps exactly one writer.</para>
        /// </summary>
        public bool Reconciled => _reconciled;

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
        /// could never reconstruct deployedIds.
        ///
        /// <para>⚠ WO-1810 - THIS PARAGRAPH USED TO END "Deployed-but-not-survivor troops are marked
        /// wounded (never deleted)", AND THAT WAS THE DEFECT. Owner ruling 2026-09-16: "any troop
        /// killed is dead so 60% of whats left". The fallen are REMOVED from the roster; a FAILED
        /// exit also loses 100% of the survivors and a player RETREAT 60% of them
        /// (<see cref="RaidCasualtyPolicy"/>, rates on the rail). The wounded/recovery call is kept
        /// only as the backstop behind that removal.</para>
        ///
        /// <para>On a 3-star clear each SURVIVING troop still gains one veterancy rank.
        /// LATCHED - the second call is a logged no-op.</para>
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

        // =====================================================================
        //  WO-1810 - WHICH EXIT IS SETTLING, DECLARED BY THE EXIT ITSELF
        // =====================================================================
        //  The cost of a raid depends on the OUTCOME, and the outcome is NOT
        //  derivable from starsEarned: a 0-star victory and a retreat both arrive
        //  as 0. So each exit DECLARES itself, and an exit that never declares
        //  resolves to Failed (the ruling) while SAYING SO in the trace.
        //
        //  ⛔ WHY VICTORY DECLARES EARLY (at RaidVictoryController's _handled latch,
        //  not at its ReconcileArmy call): HeroHealth settles a dead hero with
        //  ReconcileRaidEnd(0) and only stands down once the victory SCREEN is up
        //  (HeroHealth's VictoryOwnsTheReturn gate). A hero dying between the win
        //  and the screen would otherwise reach this method with the field still
        //  undeclared and lose 100% of a WON warband. HeroHealth itself is left
        //  untouched - it does not need to know, because the default is correct
        //  for it.
        private RaidExitOutcome _exitOutcome = RaidExitOutcome.Undeclared;

        /// <summary>
        /// WO-1810 - the raid exit declares WHAT it is before it settles the army. Latched to the
        /// FIRST declaration: a raid is won or lost once, and a later exit narrating the same raid
        /// (hero death after a win, the stranding watchdog behind a retreat) must never re-price it.
        /// </summary>
        public void DeclareRaidExitOutcome(RaidExitOutcome outcome, string why)
        {
            if (_exitOutcome != RaidExitOutcome.Undeclared)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "raid exit outcome already declared as " + RaidCasualtyPolicy.Word(_exitOutcome) +
                    " - ignoring a later '" + RaidCasualtyPolicy.Word(outcome) + "' (" + (why ?? "no reason") +
                    "). The first exit to declare owns what this raid cost.");
                return;
            }
            _exitOutcome = outcome;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "raid exit outcome DECLARED: " + RaidCasualtyPolicy.Word(outcome) + " (" +
                (why ?? "no reason") + "). This is what the army settlement will be priced on.");
        }

        /// <summary>The declared exit outcome (test/diagnostic). Undeclared until an exit says so.</summary>
        public RaidExitOutcome DeclaredExitOutcome => _exitOutcome;

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

            // Survivors = the living deployed bodies' owning ids; everyone else we deployed FELL.
            //
            // ⚠ WO-1810 - the fallen are now DEAD, not wounded. Owner ruling 2026-09-16: "any
            // troop killed is dead". The lines below used to end "-> wounded (recovery countdown).
            // NEVER deleted." and that was the defect: three troops died on the owner's Seeker at
            // 19:59:01, came home wounded, and healed free in 20 minutes.
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
            // the deploy-ledger note above). WO-1810: the non-victory result screen used to state
            // "N troops return wounded" from these two numbers and now states "N troops lost" from
            // _lastLostCount below; a raid that never reconciled leaves all of them at -1 and the
            // screen omits the line rather than printing a zero it cannot prove.
            _lastDeployedCount = deployedIds.Count;
            _lastSurvivorCount = survivorIds.Count;

            // =================================================================
            //  WO-1810 - THE CASUALTIES. Killed = dead; fail loses the rest too;
            //  retreat loses the policy's share of the survivors.
            // =================================================================
            var outcome = _exitOutcome;
            if (outcome == RaidExitOutcome.Undeclared)
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "raid-end reconcile: NO exit declared an outcome for this raid - pricing it as a " +
                    "FAIL per the owner ruling 2026-09-16. This is the correct default for the hero-death " +
                    "settlement, which declares nothing on purpose; on any OTHER exit it means a " +
                    "declaration went missing and the raid may have been priced too harshly.");

            var casualties = RaidCasualtyPolicy.Decide(deployedIds.Count, survivorIds.Count, outcome);

            // The ids to remove: everyone who fell, plus the policy's share of the survivors
            // (rookies first, deterministically - never UnityEngine.Random; see PickLostSurvivors).
            var doomed = new List<string>();
            var survivorSet = new HashSet<string>(survivorIds, System.StringComparer.Ordinal);
            foreach (var id in deployedIds)
                if (!survivorSet.Contains(id)) doomed.Add(id);          // killed on the field

            var lostSurvivors = RaidCasualtyPolicy.PickLostSurvivors(army, survivorIds, casualties.LostByPolicy);
            doomed.AddRange(lostSurvivors);

            int removed = 0;
            DeNelle.Core.Diagnostics.Guard.Try("Raid", "remove raid casualties from the roster",
                () => { removed = army.RemoveOwned(doomed); });

            // The BACKSTOP, and the reason this call is kept rather than deleted: the fallen are
            // already gone above, so this normally touches NOTHING - but a deployed body that
            // somehow escaped removal is wounded here instead of walking home silently healthy.
            // (It is also what keeps the difficulty-scaled recovery above live and honest:
            // RaidCooldownRegression pins that this method resolves recovery from the camp.)
            int woundedByBackstop = 0;
            DeNelle.Core.Diagnostics.Guard.Try("Raid", "reconcile army after raid", () =>
            {
                army.ReconcileAfterRaid(deployedIds, survivorIds, recovery);
                // Count only THIS raid's deployed ids, never every wounded troop in the roster:
                // a troop wounded by some earlier path is not a casualty of this raid.
                var deployedSet = new HashSet<string>(deployedIds, System.StringComparer.Ordinal);
                if (army.Owned != null)
                    foreach (var t in army.Owned)
                        if (t != null && t.Wounded && !string.IsNullOrEmpty(t.Id) && deployedSet.Contains(t.Id))
                            woundedByBackstop++;
            });

            _lastLostCount = casualties.TotalLost;
            _lastReturnedCount = casualties.Returned;

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"raid-end casualties: outcome={RaidCasualtyPolicy.Word(outcome)} " +
                $"deployed={deployedIds.Count} killed={casualties.Killed} survivors={survivorIds.Count} " +
                $"lostByPolicy={casualties.LostByPolicy} returned={casualties.Returned} " +
                $"(rate {casualties.LossPctApplied}% of the survivors, removed={removed} of " +
                $"{doomed.Count} ids). Killed troops are DEAD - the player rebuilds at the barracks.");

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                $"raid-end reconcile - deployed {deployedIds.Count}, survivors {survivorIds.Count}, " +
                $"wounded {woundedByBackstop} (stars {starsEarned}, " +
                $"recovery {recovery:F0}s). The wounded count is what the BACKSTOP actually left " +
                "wounded (WO-1810), not deployed-minus-survivors - the fallen were removed.");

            GrantVeterancy(army, survivorIds, starsEarned);
        }

        /// <summary>
        /// ⛔ THE ONE STAR COUNT A FULL CLEAR MEANS, FOR BOTH THINGS THAT HANG OFF IT.
        ///
        /// <para>WO-1789 section 3.3 retired a hardcoded literal <c>3</c> that sat in
        /// <see cref="GrantVeterancy"/>'s gate AND, separately, inside its own trace string, while
        /// <see cref="OwnedBaseProgression.CaptureStarsRequired"/> carried the identical number for
        /// the capture gate. Two independent 3-star gates is duplicated state - the exact failure
        /// CLAUDE.md sections 2, 5 and 8 each describe in their own words - and a doc or a caption
        /// quoting either one would have gone stale the first time the owner retuned it.</para>
        ///
        /// <para>Its VALUE is <see cref="OwnedBaseProgression.CaptureStarsRequired"/>, never a
        /// re-typed literal, so retuning the capture gate retunes this with it. It is a NAMED const
        /// rather than a bare forward because the two rules are different rules that happen to share
        /// a number: if the owner ever splits them, THIS is the one line that changes, and the
        /// victory screen's caption (<c>EndStateVM.VeterancyDeniedCaption</c>, which reads this) moves
        /// with the gate automatically.</para>
        /// </summary>
        internal const int VeterancyStarsRequired = OwnedBaseProgression.CaptureStarsRequired;

        /// <summary>
        /// The survivor reward: on a FULL clear (<see cref="VeterancyStarsRequired"/> stars) every
        /// troop that walked off the field gains one veterancy rank
        /// (<see cref="ArmyStorage.AddVeterancy"/>, capped at PlayerTroop.MaxVeterancyRank) - the
        /// "+5% damage per survived 3-star raid" ladder PlayerTroop already documents and
        /// TroopDeployer.SpawnFromArmy already consumes via PlayerTroop.DamageMultiplier. Before
        /// this, AddVeterancy had ZERO callers repo-wide. Below that star count nothing is granted.
        ///
        /// <para>⚠ THE DENIAL IS NO LONGER LOG-ONLY. Until WO-1789 the Step below was the ONLY
        /// output of this branch, so a player who missed the rank by one star was told nothing at
        /// all (the owner's own 2026-09-16 run: <c>veterancy: 2 star(s) - no ranks granted (3 stars
        /// required).</c>). The caption now rides the victory screen's star row -
        /// <c>RaidVictoryController.ShowVictoryScreen</c> -> <c>EndStateVM.StarCaption</c>. This
        /// trace STAYS (section 12: instrumentation is permanent, never stripped once a surface
        /// exists for it).</para>
        /// </summary>
        private static void GrantVeterancy(ArmyStorage army, List<string> survivorIds, int starsEarned)
        {
            if (starsEarned < VeterancyStarsRequired)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    $"veterancy: {starsEarned} star(s) - no ranks granted ({VeterancyStarsRequired} stars " +
                    "required). The victory screen states this under the star row (WO-1789).");
                return;
            }
            if (army.Owned == null || survivorIds == null || survivorIds.Count == 0)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    $"veterancy: {VeterancyStarsRequired}-star clear but NO surviving deployed troops - " +
                    "no ranks granted.");
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
                $"veterancy: {VeterancyStarsRequired}-star clear - {promoted} of {survivors.Count} " +
                "survivor(s) gained a rank.");
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

        // =====================================================================
        // ⛔ WO-1646 — EVERY FACE ON THIS BAR SHIPPED UNDER THE TOUCH FLOOR, AT EVERY ASPECT.
        // ---------------------------------------------------------------------
        // MEASURED, not theorised: the WO-1645 capture (Builds/wave5-capture2, fresh 07:33
        // 2026-09-10) reported UI_GEOMETRY_FAIL x15 over 97 canvases, EVERY failure on
        // RaidDeployHud and NONE on RaidHud. `Panel/ObsBtn_Deploy All`, `ObsBtn_Rally` and
        // `ObsBtn_Retreat` resolved:
        //     103.7 ref px tall at 1920x1080
        //      93.9 ref px tall at 2340x1080
        //      92.7 ref px tall at 2670x1200   <- the Seeker's real surface
        // against ElarionUiKit.MinTouchPx = 112 (ElarionUiKit.cs:347). Every one is UNDER.
        //
        // THE ARITHMETIC THAT PRODUCED THEM, so this can never be re-guessed. The faces were
        // authored y 0.18-0.82 OF THE BAR = 0.64, and the bar is DeployBarHeight (0.150) of
        // screen, so a face is 0.150 * 0.64 = 0.096 of the canvas's REFERENCE height:
        //     1920x1080 -> refH 1080.0 -> 103.68     (reported 103.7)
        //     2340x1080 -> refH  978.3 ->  93.92     (reported  93.9)
        //     2670x1200 -> refH  965.4 ->  92.67     (reported  92.7)
        // refH is HudLayoutBands.CanvasReferenceSize(w, h).y (:379-387), Unity's own match-0.5
        // formula. All three reproduce the oracle's numbers exactly - the model is confirmed, so
        // the "after" figures below are arithmetic, not hope.
        //
        // ⚠ WHY THIS WAS INVISIBLE UNTIL NOW, AND WHY IT IS NOT A COSMETIC TICKET.
        // ElarionUiKit.ClampMinTouch (ElarionUiKit.cs:1069) RESCUES an under-floor control at
        // runtime by GROWING it - symmetrically, in LateUpdate, spilling into whatever sits
        // beside it. So the bar was never untappable; it was silently re-laid-out every frame
        // into a shape nobody authored. The kit's own comment at :1082-1098 says this in as many
        // words: *"by the time the clamp grows a control, the layout it was meant to protect is
        // already spilled into its neighbours, and nothing anywhere says so"* - and that
        // LateUpdate never runs in an edit-mode capture, which is exactly why the AUTHORED-BAND
        // assert in UICaptureLaunch.AuditGeometry is the gate and the clamp is not.
        //
        // ⛔ THE FIX IS DERIVED, NOT A PER-ASPECT NUMBER. Three literals - one per aspect - would
        // be the duplicated-state failure CLAUDE.md sec.2 / sec.5 / sec.16 each describe, and
        // there is no hook to apply them at anyway. The face fraction below is computed FROM the
        // kit's floor, this bar's own height, and the canvas reference constants, so if any of
        // those move the band follows with no edit here.
        //
        // ⚠ AND IT IS DERIVED AGAINST THE WIDEST ASPECT, NOT AGAINST Screen.*. Two reasons, both
        // hard: (1) refH = sqrt(CanvasRefWidth * CanvasRefHeight / aspect), so refH SHRINKS as a
        // device gets wider - the widest supported aspect is the worst case, and a fraction that
        // clears the floor there clears it everywhere narrower; (2) Screen.* DOES NOT MOVE in a
        // batchmode capture (UICaptureLaunch's own banner says so and tracks it as
        // _screenStuckBuilds), so a Screen-derived fraction would author one shape headless and a
        // different one on the device - the capture would stop being evidence. A constant
        // expression authors the SAME band in both, which is the only way the gate can prove the
        // device.
        // =====================================================================

        /// <summary>The widest landscape aspect the touch floor is derived against (21:9). The
        /// worst case, because reference height falls as aspect widens - see the block above.</summary>
        private const float TouchFloorWidestAspect = 21f / 9f;

        /// <summary>Reference px of headroom above <see cref="ElarionUiKit.MinTouchPx"/>. The floor
        /// is a floor, not a target: authoring EXACTLY 112 puts the band one rounding from failing
        /// the same assert it was written to pass.</summary>
        private const float TouchFloorSafetyPx = 2f;

        /// <summary>What the faces shipped at before WO-1646 (y 0.18-0.82). The derived fraction
        /// never goes BELOW this - a narrower band could only ever be a regression.</summary>
        private const float LegacyFaceHeightFraction = 0.64f;

        /// <summary>Ceiling, so a face can never eat the plate's own inset entirely.</summary>
        private const float MaxFaceHeightFraction = 0.96f;

        /// <summary>
        /// Height of a bar FACE (and of a tray tile) as a fraction OF THE BAR, derived so the
        /// resolved rect clears <see cref="ElarionUiKit.MinTouchPx"/> at every supported aspect.
        /// <para>PUBLIC on purpose: <c>RaidHudThumbBandRegression</c> currently asserts the tray
        /// against a typed <c>0.64f</c>. That literal is now STALE - it under-states the real
        /// height, so the case still passes while measuring something the code no longer does.
        /// It should read THIS instead, exactly as the readout seat is required to read
        /// <c>HudLayoutBands.RaidReadoutBand</c> rather than a copied rect. Not re-pointed here
        /// because this ticket is scoped to one file; called out in the RESULT.</para>
        /// </summary>
        public static float DeployFaceHeightFraction
        {
            get
            {
                // refH at the worst (widest) supported aspect.
                float widestRefH = Mathf.Sqrt(
                    (HudLayoutBands.CanvasRefWidth * HudLayoutBands.CanvasRefHeight) / TouchFloorWidestAspect);
                float barPx = DeployBarHeight * widestRefH;
                if (barPx <= 0f) return LegacyFaceHeightFraction;
                float need = (ElarionUiKit.MinTouchPx + TouchFloorSafetyPx) / barPx;
                return Mathf.Clamp(need, LegacyFaceHeightFraction, MaxFaceHeightFraction);
            }
        }

        /// <summary>Bottom edge of a face, as a fraction of the bar. The band is CENTRED, so the
        /// plate keeps equal inset above and below.</summary>
        private static float FaceY0 { get { return (1f - DeployFaceHeightFraction) * 0.5f; } }

        /// <summary>Top edge of a face, as a fraction of the bar.</summary>
        private static float FaceY1 { get { return 1f - FaceY0; } }

        // ⛔ WO-1646 DEFECT 2: THE TRAY'S EDGES LIVE HERE AND NOWHERE ELSE.
        // These were typed twice - once as BuildTrayTiles' `left`/`right` locals and once as the
        // empty-state label's x0/x1. WO-1639 moved one of them (0.55 -> 0.390) and not the other,
        // and the label ended up spanning the "Deploy All" and "Rally" faces. One definition, so
        // the next edit to the tray's width cannot leave anything behind.
        /// <summary>Left edge of the troop tray, as a fraction of the bar.</summary>
        private const float TrayLeftX = 0.03f;
        /// <summary>Right edge of the troop tray, as a fraction of the bar. ⚠ WO-1639 pulled this
        /// in from 0.55 to free width for the "Deploy All" face - do not widen it without moving
        /// that face, and never re-type it beside a widget.</summary>
        private const float TrayRightX = 0.68f;
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
                // WO-1885: Breach + Rally only, bottom-middle, still ABOVE the ability row
                // (WO-1436). Troops live on RaidTroopTrayBand at the top.
                return HudLayoutBands.StackAboveThumbBand(0.32f, 0.68f, DeployBarHeight, 0f);
            }
        }

        /// <summary>The status line's band, stacked on the bar with the SAME shared gap constant
        /// so the two can never drift apart.</summary>
        public static Rect DeployStatusBand
        {
            get
            {
                return HudLayoutBands.StackAboveThumbBand(
                    0.32f, 0.68f, DeployStatusHeight,
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
            var trayBand = HudLayoutBands.RaidTroopTrayBand;
            var tray = ElarionUiKit.Panel(_ui.transform,
                new Vector2(trayBand.xMin, trayBand.yMin), new Vector2(trayBand.xMax, trayBand.yMax),
                deep: false, innerRim: false);
            var trayImg = tray.GetComponent<Image>();
            if (trayImg != null) trayImg.color = new Color(0.04f, 0.035f, 0.03f, 0.38f);
            BuildTrayTiles(tray.transform);
            float faceY0 = FaceY0, faceY1 = FaceY1;
            _deployAllButton = ElarionUiKit.Button(tray.transform,
                new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.deploy_all_button").Resolve(),
                ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.70f, faceY0), new Vector2(0.98f, faceY1), DeployAll);

            var barBand = DeployBarBand;
            var bar = ElarionUiKit.Panel(_ui.transform,
                new Vector2(barBand.xMin, barBand.yMin), new Vector2(barBand.xMax, barBand.yMax),
                deep: false, innerRim: false);
            var barImg = bar.GetComponent<Image>();
            if (barImg != null) barImg.color = new Color(0.04f, 0.035f, 0.03f, 0.38f);
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "WO-1885 troop tray TOP y " +
                trayBand.yMin.ToString("F3") + ".." + trayBand.yMax.ToString("F3") +
                "; Breach/Rally bottom-middle y " +
                barBand.yMin.ToString("F3") + ".." + barBand.yMax.ToString("F3") +
                " (still above ability row " +
                HudLayoutBands.ThumbActionRowMaxY.ToString("F3") + ").");

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

            _breachButton = ElarionUiKit.Button(bar.transform,
                new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.breach_button").Resolve(),
                ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.04f, faceY0), new Vector2(0.48f, faceY1), ToggleBreach);
            _rallyButton = ElarionUiKit.Button(bar.transform,
                new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.rally_button").Resolve(),
                ElarionUiKit.ButtonKind.Quiet,
                new Vector2(0.52f, faceY0), new Vector2(0.96f, faceY1), ToggleRally);
            // Owner layout: persistent exit above the right-side raid readout.
            var retreatBand = HudLayoutBands.RaidRetreatBand;
            _retreatButton = ElarionUiKit.Button(_ui.transform, new DeNelle.Core.UI.LocalizedText("common.retreat").Resolve(), ElarionUiKit.ButtonKind.Danger,
                new Vector2(retreatBand.xMin, retreatBand.yMin),
                new Vector2(retreatBand.xMax, retreatBand.yMax), OnRetreatPressed);

            if (_status != null) _status.text = "";
            RefreshTiles();
            RefreshRallyButton();
            RefreshBreachButton();
            StartCoroutine(WO1639BarProbe());
        }

        // =====================================================================
        //  WO-1639 STEP 1 INSTRUMENTATION — PERMANENT (CLAUDE.md sec.12)
        // ---------------------------------------------------------------------
        // ⚠ Interpolated parts are computed into locals FIRST: the compile gate's brace
        // scanner has no interpolated-string model, so a quote inside a `{...}` hole ends
        // the string as far as it is concerned (CLAUDE.md sec.1). Concatenation only.
        // =====================================================================

        /// <summary>
        /// WO-1646 STEP 1 / PERMANENT: print the DERIVED face band and what it actually resolved
        /// to in reference px on THIS device, against the kit's floor. The gate measures the
        /// authored band headless; this is the runtime half, so a device whose aspect is outside
        /// what <see cref="TouchFloorWidestAspect"/> assumes says so in the log instead of being
        /// silently rescued by ClampMinTouch (which spills into neighbours - ElarionUiKit.cs:1082-1098).
        /// </summary>
        private void LogTouchFloor()
        {
            float frac = DeployFaceHeightFraction;
            var refSize = HudLayoutBands.CanvasReferenceSize(Screen.width, Screen.height);
            float barPx = DeployBarHeight * refSize.y;
            float facePx = frac * barPx;
            string sFrac = frac.ToString("F4");
            string sBar = barPx.ToString("F1");
            string sFace = facePx.ToString("F1");
            string sFloor = ElarionUiKit.MinTouchPx.ToString("F0");
            string sRef = refSize.x.ToString("F0") + "x" + refSize.y.ToString("F0");
            string sScreen = Screen.width + "x" + Screen.height;
            string msg = "[wo1646-touch] screen=" + sScreen + " reference=" + sRef +
                         " barH=" + sBar + "px faceFrac=" + sFrac + " -> faceH=" + sFace +
                         "px vs MinTouchPx=" + sFloor;
            if (facePx + 0.5f < ElarionUiKit.MinTouchPx)
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    msg + " <- STILL UNDER THE FLOOR. This device is wider than " +
                    "TouchFloorWidestAspect assumes, so ClampMinTouch will grow these faces and " +
                    "spill them into their neighbours. Widen that constant or raise DeployBarHeight.");
            else
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid", msg + " - clears the floor.");
        }

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
            LogTouchFloor();
            LogFaceFit("deployAll", _deployAllButton);
            LogFaceFit("breach", _breachButton);   // WO-1719: the new third face
            LogFaceFit("rally", _rallyButton);
            LogFaceFit("retreat", _retreatButton);
            LogEmptyTrayLabel();
            LogToastOrdering();
        }

        /// <summary>
        /// WO-1646 Step 1 / PERMANENT: prove the empty-tray sentence WRAPPED rather than being
        /// truncated. <see cref="ElarionUiKit.FitBlock"/> ends in TMP's Truncate mode, which drops
        /// a line that does not fit its rect - so a sentence that needs one line more than the
        /// band seats goes PARTLY MISSING with no error. Same read shape as
        /// <c>LogFaceFit</c>: drawn characters against the string's length, plus the line count
        /// and the seated size, so the next capture answers this instead of arithmetic doing it.
        /// </summary>
        private void LogEmptyTrayLabel()
        {
            var t = _emptyTrayLabel;
            if (t == null) return;   // the tray has tiles; nothing to prove
            t.ForceMeshUpdate();
            var corners = new Vector3[4];
            t.rectTransform.GetWorldCorners(corners);
            float bandW = Mathf.Abs(corners[2].x - corners[1].x);
            float bandH = Mathf.Abs(corners[1].y - corners[0].y);
            string raw = t.text ?? string.Empty;
            int drawn = t.textInfo != null ? t.textInfo.characterCount : -1;
            int lines = t.textInfo != null ? t.textInfo.lineCount : -1;
            string msg = "[wo1646-label] empty-tray sentence" +
                         " band=" + bandH.ToString("F1") + "x" + bandW.ToString("F1") + "px" +
                         " font=" + t.fontSize.ToString("F1") +
                         " [" + t.fontSizeMin.ToString("F1") + ".." + t.fontSizeMax.ToString("F1") + "]" +
                         " lines=" + lines +
                         " chars=" + drawn + "/" + raw.Length +
                         " truncated=" + t.isTextTruncated +
                         " overflow=" + t.overflowMode.ToString() +
                         " scaleFactor=" + ScaleFactorOf(t);
            if (t.isTextTruncated || lines <= 0 || (drawn >= 0 && drawn < raw.Length))
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    msg + " <- THE SENTENCE IS CUT. The tray strip cannot seat it at the " +
                    "FontFloor; widen TrayRightX (and the faces with it) or raise the band - " +
                    "do NOT shorten the copy, that is the owner's call.");
            else
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid", msg + " - whole sentence drawn.");
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
            _emptyTrayLabel = null;
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
                // ⛔ WO-1646 DEFECT 2 — THE EMPTY-TRAY LABEL SAT *UNDERNEATH* THE BUTTONS.
                // MEASURED by the WO-1645 capture at 2670x1200 (root-canvas local px): this
                // label's rect spanned x -427.4..549.9 while `Panel/ObsBtn_Deploy All` spanned
                // 143.9..504.8 and `ObsBtn_Rally` 527.3..767.9 - on the IDENTICAL y band
                // -302.2..-209.5. Both faces were painted straight over the sentence.
                //
                // THE CAUSE IS A HALF-APPLIED WO-1639. That ticket pulled the tray's right edge
                // in from 0.55 to 0.390 to free width for the "Deploy All" face - and this label,
                // the tray's OWN empty-state copy, kept its authored x1 of 0.68, which now reaches
                // straight across the new face. A shared edge held in two places drifted the
                // moment one of them moved: the same duplicated-state failure CLAUDE.md documents
                // in sec.2, sec.5 and sec.16, in miniature.
                //
                // THE FIX IS TO STOP HOLDING IT TWICE. The label is the TRAY's, so it takes the
                // tray's own `left`/`right` - the same two values the tiles are laid out from,
                // hoisted above this branch so there is exactly ONE definition of where the tray
                // ends. It can never again be left behind when that edge moves.
                //
                // ⚠ AND IT WRAPS INSTEAD OF ELLIPSISING. The tray strip is 0.360 of a 1503.5-ref-px
                // bar = 541 px, and the sentence is 49 characters: at FontLabel 40 it needs roughly
                // 1078 px, so on ONE line FitSingleLine would autoshrink to the 30 px FontFloor and
                // then CUT it - trading a covered sentence for a truncated one. FitBlock wraps it
                // to two lines instead, and two lines at 40 need 2 x NeedPx(40) = 100.8 px against
                // the derived face band's 116.7 px at 2670x1200. It seats, at full size, with the
                // whole sentence. NO FONT GOES UNDER FontFloor and NO PLAYER COPY IS SHORTENED.
                var empty = ElarionUiKit.Label(bar, new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.no_troops_train").Resolve(),
                    FaceY0, FaceY1, ElarionUi.ParchmentDim, ElarionUi.FontLabel,
                    TMPro.TextAlignmentOptions.Left, TrayLeftX, TrayRightX);
                ElarionUiKit.FitBlock(empty);
                // ⚠ AND IT IS PROVEN, NOT ASSERTED. FitBlock's overflow mode is TRUNCATE, which
                // DROPS a line that will not fit the rect - so "two lines at 40 need 100.8 px and
                // the band is 116.7" is a heuristic (RaidSelectionScreen.NeedPx is a SINGLE-LINE
                // seat estimate; TMP's real multi-line height is lineCount x fontSize x the FONT
                // ASSET's line-height ratio, which this lane did not read). A 16 px margin resting
                // on an unread number is the WO-1464 "0.04 px margin" class all over again, so the
                // label is held and MEASURED by LogEmptyTrayLabel() instead of trusted.
                _emptyTrayLabel = empty;
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
            float left = TrayLeftX, right = TrayRightX;
            float w = (right - left) / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                string defId = defIds[i];
                float x0 = left + i * w;
                float x1 = x0 + w * 0.94f;

                string label = DisplayName(defId);
                // ⚠ WO-1646: the tiles are BUTTONS on the same bar, so they carry the identical
                // under-floor defect the three named faces did - the capture simply could not see
                // it, because headless there is no GameStateService and the tray builds EMPTY
                // (that caveat is recorded in the WO-1645 capture's own log line). Fixing only the
                // three faces the oracle named would have left the defect shipping for every
                // player who actually owns troops. Derived band, same as the faces.
                var btn = ElarionUiKit.Button(bar, label, ElarionUiKit.ButtonKind.Gold,
                    new Vector2(x0, FaceY0), new Vector2(x1, FaceY1), () => ArmTile(defId));

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
                var square = new GameObject("SquarePortrait", typeof(RectTransform), typeof(AspectRatioFitter));
                square.transform.SetParent(portraitSeat.transform, false);
                var squareFit = square.GetComponent<AspectRatioFitter>();
                squareFit.aspectRatio = 1f;
                squareFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                var def = TroopCatalog.Find(defId);
                string icon = def != null && !string.IsNullOrEmpty(def.IconId) ? def.IconId : defId;
                ElarionUiKit.Portrait(square.transform,
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
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.deploy_all_unavailable").Resolve());
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(hero.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            Vector3 desired = hero.transform.position + forward * 7f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(desired, out UnityEngine.AI.NavMeshHit seat,
                                                       8f, UnityEngine.AI.NavMesh.AllAreas))
            {
                SetStatus(new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.deploy_all_needs_ground").Resolve());
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
            SetStatus(deployedNow > 0 ? DeNelle.Core.UI.LocalText.Format("village.troops.raid_deploy.deployed_assault_formation_fmt", deployedNow)
                                      : new DeNelle.Core.UI.LocalizedText("village.troops.raid_deploy.all_ready_deployed").Resolve());
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "DEPLOY ALL -> " + deployedNow + " troop(s), tactic=Assault Formation, seat=" + seat.position + ".");
        }

        private void ArmTile(string defId)
        {
            _rallyMode = false;
            RefreshRallyButton();
            // WO-1719: arming a tile turns the breach MODE off (exclusive arm states) but
            // leaves the standing ORDER, exactly as it leaves TroopRally.Point.
            _breachMode = false;
            // WO-1746: same as the rally arm - the mode flag drops, the STANCE survives for as
            // long as an explicit order does (TroopBreachOrder.StanceActive).
            TroopBreachOrder.SetStanceArmed(false);
            RefreshBreachButton();
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
            if (_deployAllButton != null)
            {
                _deployAllButton.interactable = totalRemaining > 0;
                _deployAllButton.gameObject.SetActive(totalRemaining > 0);
            }
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
