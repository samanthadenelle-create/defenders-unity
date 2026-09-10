// =============================================================================
// TownSuspension - the ONE authority for "the town is standing still because the
// player is elsewhere in their own game" (owner ruling 2026-08-07).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core
//
// THE OWNER'S RULING, VERBATIM:
//   "everything pauses except harvesting, while player is active"
//   "part of that is deliberate that they need to make sure town is strong
//    before leaving it unattended."
//
// THE AXIS IS **ACTIVE vs OFFLINE**, NOT dungeon-vs-town. Getting this backwards
// inverts the whole design, so it is stated once, here, and every gate reads it
// from this file:
//
//   PLAYER ACTIVE, ELSEWHERE (a dungeon, a raid, an arena fight)
//       -> the town PAUSES. The player is PRESENT, just not standing in the town.
//          Suspended: wave countdown, enemy spawns, structure damage, HEART
//          damage. Still running: HARVESTING, always.
//
//   PLAYER OFFLINE (app closed / backgrounded)
//       -> the town is EXPOSED. DELIBERATELY. That is where the defensive
//          pressure lives - "make sure town is strong before leaving it
//          unattended" - and it must NOT be softened. Offline accrual is a
//          SEPARATE path (OfflineHarvestService, 10h cap) and NOTHING in this
//          file is ever consulted for it. Do not conflate the two.
//
// WHY NOT Time.timeScale: because the player is STANDING IN the other scene.
// Freezing the clock would freeze the dungeon they are playing. So the town's
// systems are suspended DELIBERATELY, one by one, each checking this authority
// at its own tick site.
//
// THE SCENE RULE THAT MAKES THAT SAFE (SuspendedFor): a system suspends only if
// its own GameObject does NOT live in the currently ACTIVE scene. Anything in the
// scene the player is actually standing in is NEVER suspended, no matter what
// this authority says. That single test is what keeps a global town pause from
// ever touching the dungeon - it is not possible to freeze the room the player is
// in through this API.
//
// ⛔ ...AND THAT RULE IS SCOPED TO A **SCENE** HOLD (WO-1694, 2026-09-10). The
// active-scene test answers "did the player LEAVE this scene?", so it is honoured
// only for the FLOOR - a hold a scene CHANGE created. A FLOORLESS, hand-driven
// Suspend (the ARENA, staged 7 km away in the SAME scene) has no scene change to
// reason about, and applying the exemption to it voided the caller entirely: the
// village wave clock ticked through the whole arena fight and spawned a wave 0.7 s
// after the win, which re-raised the battle-lock and failed the quiescence gate.
// Proven on the owner's Seeker, 2026-09-10 15:20; the log line numbers are cited
// on SuspendedFor. Pinned by Editor/Regression/ArenaInSceneSuspensionRegression.cs.
//
// THE SCENE IS A FLOOR, NOT A PEER FLAG (WO-1017, proven by F8 seq 2314): while the
// active scene is off-hub it holds the town down, and an ad-hoc hold layered on top
// (a BattleArena encounter staged INSIDE a dungeon) can only ever ADD. Its Resume
// falls back TO the floor, never through it. Before this, the arena's paired Resume
// released the dungeon's own baseline and the town ran on with the player still
// standing in the dungeon. See Resume + ApplySceneBaseline.
//
// HARVESTING IS NOT REPRESENTED HERE AT ALL. There is deliberately no
// HarvestSuspended flag: a flag that exists is a flag someone eventually reads,
// and the ruling is that harvesting never stops. Its absence is the enforcement.
//
// PAUSE-SEAM PRECEDENT: this mirrors RepEngageWatcher.PauseAll/ResumeAll (a
// static gate honoured at the top of each Update plus at the action entry point)
// and adds that seam's hardest-won lesson - EVERY teardown path must resume, or
// the freeze leaks for the rest of the session. See ResumeIfStale below.
//
// Instrumented [Flow:TownSuspend]. No silent transitions.
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core
{
    /// <summary>
    /// What to do with a wave that is ALREADY IN PROGRESS when the player leaves
    /// the town. OPEN DESIGN QUESTION - the owner has not ruled. Both behaviours
    /// are implemented so the ruling is a one-line change and never a rewrite.
    /// </summary>
    public enum InProgressWavePolicy
    {
        /// <summary>
        /// DEFAULT. The live wave is frozen where it stands and resumes (after the
        /// return grace) exactly as it was. Non-destructive: nothing the player
        /// already fought is thrown away, and nothing is handed to them for free.
        /// Chosen as the default only because it DISCARDS NOTHING - not because it
        /// is known to be the ruling.
        /// </summary>
        SuspendAndResume = 0,

        /// <summary>
        /// The live wave is abandoned on exit and the town returns to its calm
        /// posture. Kinder on return (the player never walks back into a fight they
        /// left mid-swing) but it erases progress through that wave and can be
        /// farmed - enter a dungeon to wipe a wave going badly.
        /// </summary>
        CancelOnEntry = 1,
    }

    /// <summary>
    /// Single authority for suspending the town's systems while the player is
    /// ACTIVE but elsewhere. Read-only for nearly every consumer: call
    /// <see cref="SuspendedFor"/> at a tick site and obey it.
    /// </summary>
    public static class TownSuspension
    {
        /// <summary>
        /// Seconds the town stays suspended AFTER the player returns. The return must
        /// NOT dump a held wave on a player who is still loading in / re-orienting -
        /// the same courtesy the post-loss path gives (RepEngageWatcher's 3.5s
        /// PostLossGraceSeconds, matched deliberately so the two windows feel alike).
        /// </summary>
        public const float DefaultReturnGraceSeconds = 3.5f;

        /// <summary>
        /// OPEN QUESTION - see <see cref="InProgressWavePolicy"/>. Settable so the
        /// owner's ruling lands as one assignment. Nothing in this file assumes which
        /// way it goes; both paths are honoured by the wave-side gate.
        /// </summary>
        public static InProgressWavePolicy WavePolicy { get; set; } = InProgressWavePolicy.SuspendAndResume;

        private static bool _suspended;
        private static string _reason = "none";
        private static float _graceUntil = -1f;
        private static string _lastActiveScene;

        // ── The scene FLOOR (WO-1017) ───────────────────────────────────────────
        // Non-null while the ACTIVE scene itself demands the town stand still (the
        // player is active somewhere that is not a hub and not the front-end). This is
        // the baseline every ad-hoc hold sits ON TOP OF; see Resume for why it exists.
        // Written ONLY by ApplySceneBaseline, so there is exactly one writer and the
        // floor can never drift from the scene the player is standing in.
        private static string _floorReason;
        private static string _floorScene;

        /// <summary>True while the town is held still because the player is elsewhere.</summary>
        public static bool IsSuspended => _suspended;

        /// <summary>Why the town is suspended (scene name / caller tag) - for traces only.</summary>
        public static string Reason => _reason;

        /// <summary>
        /// True while the post-return grace is still running. Town systems stay held
        /// during this window even though <see cref="IsSuspended"/> has gone false.
        /// </summary>
        public static bool ReturnGraceActive => Time.time < _graceUntil;

        /// <summary>Seconds left in the post-return grace (0 when not in one).</summary>
        public static float ReturnGraceRemaining => Mathf.Max(0f, _graceUntil - Time.time);

        /// <summary>
        /// The combined gate: suspended outright, OR still inside the return grace.
        /// This is what tick sites should consult, via <see cref="SuspendedFor"/>.
        /// </summary>
        public static bool Held => _suspended || ReturnGraceActive;

        // ── The scene-safe gate every consumer should call ──────────────────────

        /// <summary>
        /// THE gate. True when <paramref name="owner"/> is a town-side object that
        /// must hold still right now.
        ///
        /// The load-bearing clause is the ACTIVE-SCENE EXEMPTION: an object living in
        /// the scene the player is currently standing in is NEVER suspended. That is
        /// what makes a town-wide pause safe to ship - a dungeon's own enemies, its own
        /// damageable props and its own spawners all sit in the active scene, so this
        /// returns false for them even at the height of a town suspension. It is
        /// structurally impossible to freeze the room the player is in through this API.
        ///
        /// ⛔ AND IT IS EXEMPTION FROM A **SCENE** HOLD ONLY (WO-1694, 2026-09-10).
        /// The exemption is the answer to ONE question - "did the player LEAVE this
        /// scene?" - so it may only be applied to a hold that a scene CHANGE created,
        /// i.e. the FLOOR (see ApplySceneBaseline). Applied to an ad-hoc, FLOORLESS
        /// hold it answers a question nobody asked and silently voids the caller:
        ///
        ///   The ARENA stages 7 km away in the SAME scene, so there is no scene change
        ///   and no floor. BattleArena drives Suspend() by hand for exactly that reason
        ///   (BattleArena.cs:502-516, whose own comment says so). But WaveManager lives
        ///   in that same active scene, so the exemption fired for it and the town's
        ///   wave clock ran through the entire fight - the precise defect the hand-driven
        ///   call was added to prevent, defeated one method away from the call.
        ///
        ///   PROVEN, owner's Seeker build 363866, 2026-09-10 15:20 session
        ///   (Builds/device-frames/2026-09-10_1520_logcat.txt):
        ///     L710344  town SUSPENDED (arena battle staged at ArenaCentre ...)
        ///     L710750..L714808  [Flow:HUD] Countdown wave 1 cd19.8 ... cd8.0 ... cd0.8
        ///                       -- the clock ticked every second THROUGH the fight
        ///     grep "HELD at phase" over all 732k lines => ZERO. The SuspendAndResume
        ///                       hold never once engaged for the arena.
        ///     L714859  StartWave(1) -> 4 village enemies, 0.8s after the arena win's
        ///                       clean BATTLE_SESSION_RELEASED at L714122 (holders=[none])
        ///     L715193  BATTLE_QUIESCENCE_FAIL (arena win): battle-lock HELD by
        ///                       WaveManager.OnEnable's probe. Nobody declared a win over
        ///                       live enemies; a village wave landed on top of the win.
        ///   The same-session CONTROL is the dungeon at L398282-L405463: a real scene
        ///   change, so a floor, and NOT ONE countdown tick in that window.
        ///
        /// A null owner, or an object with no valid scene (DontDestroyOnLoad singletons
        /// such as the region roamer spawner), is treated as TOWN-SIDE and suspended.
        /// That direction is deliberate: the failure mode of wrongly suspending a
        /// persistent town service is a paused town, which is the intent anyway; the
        /// failure mode of wrongly running one is damage the absent player cannot see.
        /// </summary>
        public static bool SuspendedFor(GameObject owner)
        {
            if (!Held) return false;
            if (owner == null) return true;

            var scene = owner.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.name)) return true;   // DDOL / unowned

            if (scene.handle == SceneManager.GetActiveScene().handle)
            {
                // The player is standing here - never hold it still, PROVIDED the hold is
                // the one this test can reason about: a scene the player walked out of.
                if (_floorReason != null) return false;

                // FLOORLESS HOLD: an ad-hoc Suspend with no scene change behind it. The
                // player is elsewhere INSIDE this scene (the arena, 7 km away), so "same
                // scene" no longer means "standing among these objects" and the exemption
                // would void the only pause this case has. Hold it.
                //
                // Throttled to one line per 10s per reason: this runs from WaveManager.Update,
                // and a per-frame trace here evicts the boot window out of the device logcat
                // ring (FlowTrace.cs:293-300).
                // _reason is reset to "none" by Resume, so during the return grace the hold has no
                // reason left to name - say "return-grace" rather than print 'none' and read like a bug.
                string who = _suspended ? _reason : "return-grace";
                FlowTrace.Throttle("TownSuspend", "floorless-hold-covers-active-scene", 10f,
                    $"floorless hold '{who}' is holding ACTIVE-scene owners too (grace " +
                    $"{ReturnGraceRemaining:F1}s). There was no scene change, so the active-scene " +
                    "exemption does not apply: the player is elsewhere INSIDE this scene and the " +
                    "town clock must stand still. WO-1694 - before this, the arena's hand-driven " +
                    "Suspend was silently voided here and the village wave clock ran the whole fight.");
                return true;
            }

            return true;
        }

        /// <summary>
        /// Convenience for a MonoBehaviour tick site: <c>if (TownSuspension.SuspendedFor(this)) return;</c>
        /// </summary>
        public static bool SuspendedFor(Component owner)
            => SuspendedFor(owner != null ? owner.gameObject : null);

        // ── Transitions ─────────────────────────────────────────────────────────

        /// <summary>
        /// Hold the town still. Idempotent - a second call while already suspended
        /// only refreshes the reason, it never stacks (the leak that would need a
        /// matching count of Resumes to clear).
        /// </summary>
        public static void Suspend(string reason)
        {
            string r = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
            if (_suspended)
            {
                if (r != _reason)
                {
                    _reason = r;
                    FlowTrace.Step("TownSuspend", $"town already suspended - reason updated to '{r}'.");
                }
                return;
            }
            _suspended = true;
            _reason = r;
            _graceUntil = -1f;
            // WO-1694: name the SHAPE of the hold, because it decides who it actually covers.
            // A FLOORLESS hold (no scene change - the arena, staged 7 km away in this same scene)
            // covers the active scene too; a FLOORED one exempts it. That single sentence is the
            // difference between the arena's Suspend working and being a no-op, and it used to be
            // invisible in the log: the "town SUSPENDED" line read identically in both cases.
            string coverage = _floorReason != null
                ? $"FLOORED by '{_floorScene}' - objects in the ACTIVE scene are EXEMPT (the player is standing among them)"
                : "FLOORLESS (no scene change) - ACTIVE-scene town objects are held TOO, because the player is elsewhere INSIDE this scene";
            FlowTrace.Step("TownSuspend",
                $"town SUSPENDED ({r}). Held: wave countdown, enemy spawns, structure damage, heart damage. " +
                $"Still running: HARVESTING (never suspended). Wave policy={WavePolicy}. Coverage: {coverage}.");
        }

        /// <summary>
        /// Release the town, then hold it for <paramref name="graceSeconds"/> more so the
        /// return does not dump a suspended wave on a player who has just loaded in.
        /// Idempotent and safe to call when not suspended (that is the point - every
        /// teardown path can call it unconditionally, which is how the RepEngageWatcher
        /// seam avoids leaking a permanent freeze).
        /// </summary>
        public static void Resume(string reason, float graceSeconds = DefaultReturnGraceSeconds)
        {
            if (!_suspended)
            {
                // Not an error. Teardown paths call this unconditionally on purpose.
                return;
            }

            string prior = _reason;
            string why = string.IsNullOrEmpty(reason) ? "unspecified" : reason;

            // ── THE SCENE FLOOR (WO-1017) ───────────────────────────────────────
            // PROVEN BY CAPTURE (F8 seq 2314, Player.log L195137/L213948/L224706): the
            // scene gate DID fire on entering 'Dungeon_HealersCottage' and suspended the
            // town. A real-time BattleArena encounter then staged INSIDE that dungeon and
            // called Suspend() again - which, being idempotent and FLAT, only rewrote the
            // reason. When the fight resolved, BattleArena's paired Resume() released the
            // whole suspension, DUNGEON BASELINE INCLUDED, and started a return grace for a
            // return that had not happened. 2.7 s later TownActivityProbe caught the town
            // running with the player still standing in the dungeon.
            //
            // The defect is NOT a missed classification - it is that a NESTED hold could
            // lift the town on its way out. So the scene baseline is a FLOOR, not another
            // peer flag: an ad-hoc hold may only ever ADD to it, and releasing that hold
            // can only fall BACK to the floor, never through it. Expressed here rather
            // than by counting Suspends because the two Resume call sites pass a
            // resume-REASON, not the key they took - they cannot be matched to their own
            // Suspend without changing their call signatures.
            if (_floorReason != null)
            {
                _graceUntil = -1f;   // the player never came back; a return grace would be a lie
                _reason = _floorReason;
                FlowTrace.Warn("TownSuspend",
                    $"Resume ({why}) released the nested hold '{prior}' but the player is STILL off-hub " +
                    $"in '{_floorScene}' - the SCENE FLOOR re-asserts the suspension immediately and NO " +
                    "return grace is started. A nested hold (arena battle, cutscene) must never lift the " +
                    "town on its way out. Reason restored to the floor.");
                return;
            }

            _suspended = false;
            float g = Mathf.Max(0f, graceSeconds);
            _graceUntil = Time.time + g;
            FlowTrace.Step("TownSuspend",
                $"town RESUMED ({why}) after '{prior}' - " +
                $"holding {g:0.0}s return grace so no held wave lands the instant the player is back.");
            _reason = "none";
        }

        // ── Automatic scene-driven drive ────────────────────────────────────────

        /// <summary>
        /// Reset the statics on a fresh play session. Domain reload can be OFF in this
        /// project, so a static left true would carry a permanent town freeze into the
        /// next Play - the exact leak class the arena seam's watchdogs exist to prevent.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _suspended = false;
            _reason = "none";
            _graceUntil = -1f;
            _lastActiveScene = null;
            _floorReason = null;
            _floorScene = null;
            WavePolicy = InProgressWavePolicy.SuspendAndResume;
        }

        /// <summary>
        /// Drive the suspension off the ACTIVE SCENE. Deliberately expressed as "not a
        /// hub" rather than "is a dungeon": the ruling is about the player being ACTIVE
        /// SOMEWHERE ELSE, and dungeons are only one of the places that can be. Raids,
        /// ATB battles and any future elsewhere are covered by the same sentence, and no
        /// new scene has to be added to a whitelist to be safe. There is no IsDungeon()
        /// helper in the project, and this design means one is not needed.
        ///
        /// The ARENA is NOT covered by this hook - it is staged 7 km away in the SAME
        /// scene, so no scene change occurs. BattleArena drives it explicitly at the two
        /// points it already drives RepEngageWatcher.PauseAll/ResumeAll.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallSceneHook()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EvaluateActiveScene("startup");
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode) => EvaluateActiveScene("sceneLoaded:" + s.name);

        private static void OnActiveSceneChanged(Scene from, Scene to) => EvaluateActiveScene("activeSceneChanged:" + to.name);

        private static void EvaluateActiveScene(string trigger)
        {
            string name = SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(name)) return;
            ApplySceneBaseline(name, trigger);
        }

        /// <summary>
        /// Does the scene named <paramref name="sceneName"/> ITSELF demand that the town
        /// stand still? Derived from scene KIND, never from a dungeon whitelist: the test
        /// is "not a hub and not the front-end", so a dungeon baked tomorrow - a composed
        /// <c>dg_*</c>, a hand-built <c>Dungeon_*</c>, a <c>RaidBase_*</c>, an ATB battle -
        /// is classified correctly on the day it exists with no list to edit. That is the
        /// whole reason the rule is phrased negatively; see InstallSceneHook.
        ///
        /// Public because it is the ONE predicate the regression can pin per scene kind
        /// without loading a scene.
        /// </summary>
        public static bool SceneDemandsSuspension(string sceneName, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(sceneName)) return false;

            // Menus / front-end are not "the player is off adventuring" - there is no town
            // loaded to protect and suspending there would only muddy the trace.
            bool isHub = HubScenes.IsHub(sceneName);
            bool isFrontEnd = sceneName == SceneRouter.Title
                              || sceneName.StartsWith("HeroSelect", System.StringComparison.OrdinalIgnoreCase);
            if (isHub || isFrontEnd) return false;

            reason = "player active in '" + sceneName + "'";
            return true;
        }

        /// <summary>
        /// Set the scene FLOOR from <paramref name="sceneName"/> and move the suspension to
        /// match it. The single writer of the floor.
        ///
        /// THE DEDUP IS DELIBERATELY ONE-DIRECTIONAL (WO-1017). When the active scene has
        /// not changed we may still RE-ASSERT a lost floor, but we must NEVER auto-resume:
        /// a legitimate ad-hoc hold sits above the floor (the arena stages 7 km away in the
        /// SAME hub scene, so its suspension is floorless by design), and an unsolicited
        /// resume on some unrelated additive sceneLoaded would cancel that fight's pause
        /// mid-swing. Upward only.
        ///
        /// Public so the regression can drive the exact transition a scene load drives,
        /// instead of green-ticking over a scene it cannot load headlessly.
        /// </summary>
        public static void ApplySceneBaseline(string sceneName, string trigger)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            string t = string.IsNullOrEmpty(trigger) ? "unspecified" : trigger;

            bool demands = SceneDemandsSuspension(sceneName, out string floor);
            // Store the reason EXACTLY as it is applied to Suspend below, trigger and all.
            // Storing the bare `floor` here made the fallback restore a DIFFERENT string than
            // the one the floor was raised with, so after a nested Resume the town reported a
            // reason that no longer said how it got there - caught by [town-suspend-floor]
            // case [nested-hold]. The floor's reason is the thing that explains the real state,
            // so it must survive a clobber verbatim.
            string floorReason = demands ? floor + " (" + t + ")" : null;
            _floorReason = floorReason;
            _floorScene = demands ? sceneName : null;

            if (sceneName == _lastActiveScene)
            {
                // Same scene. Upward-only: restore a floor that something released out
                // from under us, and say so loudly - a silent re-assert would hide the
                // very clobber this ticket exists to stop.
                if (demands && !_suspended)
                {
                    FlowTrace.Warn("TownSuspend",
                        $"scene FLOOR was lost while still in '{sceneName}' - re-asserting ({t}). " +
                        "Something released the town without the player leaving.");
                    Suspend(floorReason);
                }
                return;
            }

            _lastActiveScene = sceneName;

            if (!demands)
            {
                _floorReason = null;
                _floorScene = null;
                Resume("active scene '" + sceneName + "' is a hub / front-end (" + t + ")");
                return;
            }

            Suspend(floorReason);
        }
    }
}
