// =============================================================================
// TownActivityProbe - INSTRUMENTATION ONLY for the town-suspension ruling
// (owner 2026-08-07: "everything pauses except harvesting, while player is
// active").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHY THIS EXISTS (CLAUDE.md section 12): the implementation rule for that ruling
// was "enumerate what actually ticks during a dungeon from FlowTrace before
// switching anything off - do not guess the list". A static read of the codebase
// produces a CANDIDATE list; it cannot produce the real one, because what is
// alive during a dungeon depends on scene lifetime, on which singletons are
// DontDestroyOnLoad, and on whether the town scene stayed additively loaded. All
// three vary at runtime and none of them are visible from source.
//
// So this probe reports, from a REAL dungeon session, three things per town
// system, on change only:
//   * ALIVE?    - does the object still exist once the player is elsewhere
//   * WHERE     - which scene owns it (or DontDestroyOnLoad), because that is
//                 what decides whether TownSuspension.SuspendedFor catches it
//   * GATED?    - would the suspension gate actually hold it right now
//
// THAT THIRD COLUMN IS THE POINT. A system that is alive and ticking but which
// SuspendedFor() returns false for is an UNGATED LEAK - the town is still being
// acted on while the player is away, and the suspension silently does not cover
// it. Those lines are logged as warnings so a capture surfaces them without
// anyone having to reason about assembly-level scene ownership by hand.
//
// It also names anything sitting in the ACTIVE scene, because those are the
// objects that MUST NOT be suspended (the player is standing among them). A
// suspension that started catching those would be the Time.timeScale mistake
// arriving by a different road, and this probe is how that gets caught.
//
// READ-ONLY. Never suspends, resumes, damages, spawns or mutates anything.
// Instrumented [Flow:TownProbe].
//
// -----------------------------------------------------------------------------
// WO-1779 - THE PROBE IS SCENE-GATED OUT OF RAID SCENES, AND THAT GATE IS THE
// WHOLE REASON THIS BLOCK EXISTS.
//
// MEASURED, from the owner's Seeker capture
// logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt (build
// 2026.09.16.371701): this probe polled INSIDE RaidBase_IronBastion and its
// per-frame scope blew the 4 ms budget NINETEEN times between log lines 3038288
// (13:24:29, 7.3 ms) and 3059147 (13:26:01, 6.9 ms), peaking at 10.5 ms
// (line 3049140) - on a frame the same window measured at 56.5 ms / 18 fps
// (`LOW fps=18 ms=56.5 ... scene=RaidBase_IronBastion`, line 3057519). Its own
// report named the scene out loud: line 3038287 reads
// `[Flow:TownProbe] scene='RaidBase_IronBastion' ... Enemy x23 in the ACTIVE
// scene (these MUST keep running)`.
//
// WHY IT WAS THERE AT ALL: `OnSceneLoaded(Scene s, ...)` DISCARDED its `Scene s`
// and called TrySpawn() unconditionally, so a DontDestroyOnLoad town probe was
// (re)spawned into every scene the game ever loads, raid bases included.
//
// WHY DISABLE-IN-RAID AND NOT DESTROY-OFF-HUB. This probe's ENTIRE PURPOSE is to
// watch the town while the player is elsewhere - `Poll` deliberately no-ops in a
// hub scene (see below), and WO-1017 was only ever visible because the probe was
// alive inside Dungeon_HealersCottage. Destroying it on every non-hub load would
// delete the only observer of the thing it observes. A RAID is the one off-hub
// scene where its answer is already known and its cost is not affordable: the
// raid is a declared, clocked, scored assault (HubScenes.IsRaid /
// SceneDeclaresCombat) whose enemies all live in the ACTIVE scene, which is the
// one case `Poll` can only ever report as "these MUST keep running". So the raid
// - and ONLY the raid - turns the component OFF (`enabled = false`, so Unity
// never calls Update at all), and the next non-raid scene turns it back ON.
// Dungeons, the overworld and the outposts keep their observer.
//
// The 4-arg FlowTrace.Measure scope STAYS in Update (CLAUDE.md sec.12:
// instrumentation is permanent, flag it off, never strip it) so wherever the
// probe DOES run its cost is still measured and still named in the roll-up.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// Read-only diagnostic that enumerates which TOWN systems are still alive and
    /// ticking while the player is elsewhere, and whether the town-suspension gate
    /// actually covers each one. Turns the suspension's coverage from an assumption
    /// into a captured line.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TownActivityProbe : MonoBehaviour
    {
        private const float PollInterval = 3f;

        private float _timer;
        private string _lastReport;

        /// <summary>The one live probe, cached at Awake. Read instead of FindAnyObjectByType so
        /// the scene gate can reach an instance whose Behaviour it has DISABLED (WO-1779) — a
        /// disabled component is a state Find* semantics should not have to be trusted for.</summary>
        private static TownActivityProbe _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // BOTH callbacks, deliberately. sceneLoaded fires for additive loads that do not
            // change the active scene, and activeSceneChanged fires for a SetActiveScene that
            // loads nothing. Their relative ORDER is not something this file relies on: both
            // funnel into the same idempotent gate, which resolves the scene name itself, so
            // whichever arrives first (or second, or alone) converges on the same state.
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            ApplySceneGate(GateSceneName(SceneManager.GetActiveScene()));
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode)
            => ApplySceneGate(GateSceneName(s));

        private static void OnActiveSceneChanged(Scene from, Scene to)
            => ApplySceneGate(GateSceneName(to));

        /// <summary>The scene name the gate judges: the ACTIVE scene, because that is what
        /// <see cref="Poll"/> reports on and what decides whether the player is in a raid.
        /// Falls back to the handed scene while the active one is still unnamed (a load in
        /// flight), so the gate never judges an empty string.</summary>
        private static string GateSceneName(Scene handed)
        {
            string active = SceneManager.GetActiveScene().name;
            return string.IsNullOrEmpty(active) ? handed.name : active;
        }

        /// <summary>
        /// TRUE when the probe should tick in <paramref name="sceneName"/>. FALSE for a RAID
        /// scene ONLY — see the WO-1779 block in this file's header for the measurement and for
        /// why a dungeon (the scene the probe was built for) deliberately still ticks.
        /// </summary>
        /// <remarks>Classification goes through <see cref="HubScenes.IsRaid"/>, the canonical
        /// classifier, never a fresh StartsWith — a private copy of "is this a raid?" is the
        /// drift HubScenes exists to prevent (its own header, WO-411/920).</remarks>
        public static bool ShouldTickIn(string sceneName) => !HubScenes.IsRaid(sceneName);

        /// <summary>
        /// Apply the scene gate to one probe instance and return whether it is now ticking.
        /// Idempotent, and logs only on a TRANSITION so the enable/disable is greppable in a
        /// device capture without becoming a per-scene-load heartbeat.
        /// </summary>
        /// <remarks>Takes the instance explicitly so the gate is provable headlessly, with no
        /// PlayMode and no RuntimeInitializeOnLoadMethod — see TownSuspendSceneFloorRegression
        /// case (h).</remarks>
        public static bool ApplyGate(TownActivityProbe probe, string sceneName)
        {
            bool run = ShouldTickIn(sceneName);
            if (probe == null) return run;
            if (probe.enabled == run) return run;

            probe.enabled = run;
            if (run)
            {
                // Poll on the next frame rather than PollInterval later: the first report after
                // coming back out of a raid is the one that says what the town looks like now.
                probe._timer = 0f;
                FlowTrace.Step("TownProbe", "scene='" + sceneName + "' is not a raid -> probe ENABLED " +
                                            "(it observes the town while the player is away).");
            }
            else
            {
                FlowTrace.Step("TownProbe", "scene='" + sceneName + "' is a RAID -> probe DISABLED. " +
                                            "WO-1779: its poll cost up to 10.5ms of a 16ms frame inside " +
                                            "RaidBase_IronBastion, and every enemy there is in the ACTIVE " +
                                            "scene, so it had nothing to report and no budget to report it in.");
            }
            return run;
        }

        private static void ApplySceneGate(string sceneName)
        {
            if (_instance != null)
            {
                ApplyGate(_instance, sceneName);
                return;
            }
            // Nothing to gate yet. Only SPAWN where the probe is allowed to tick — spawning a
            // probe into a raid just to switch it off would put a DontDestroyOnLoad object into
            // the raid whose whole lifetime is spent disabled.
            if (!ShouldTickIn(sceneName)) return;
            TrySpawn();
        }

        private static void TrySpawn()
        {
            if (_instance != null) return;
            if (FindAnyObjectByType<TownActivityProbe>() != null) return;
            var go = new GameObject("TownActivityProbe");
            DontDestroyOnLoad(go);   // must outlive the town scene to observe its absence
            go.AddComponent<TownActivityProbe>();
        }

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // WO-1483: town frame path — Poll ENUMERATES town activity, so its cadence and
            // its per-poll cost both matter to the empty-town floor.
            // WO-1779: this scope STAYS (CLAUDE.md §12) — the gate below decides WHERE the probe
            // runs; this decides that wherever it runs, the cost is still named in the roll-up.
            using var _perf = DeNelle.Core.Diagnostics.FlowTrace.Measure(
                "Perf", "TownActivityProbe.Update", 4f, 1f);

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = PollInterval;

            // Defence in depth, at the POLL cadence (every 3s), not per frame: if a scene
            // transition ever arrives without either callback firing, the gate still catches it
            // here before the expensive enumeration runs, and disables the component for good.
            string scene = SceneManager.GetActiveScene().name;
            if (!ShouldTickIn(scene))
            {
                ApplyGate(this, scene);
                return;
            }

            Guard.Try("TownProbe", "enumerate town activity", Poll);
        }

        private void Poll()
        {
            // Only interesting while the player is actually elsewhere. In the hub this
            // would be a constant, meaningless heartbeat.
            var active = SceneManager.GetActiveScene();
            if (HubScenes.IsHub(active.name)) return;

            var findings = new List<string>();
            int ungated = 0;

            // WAVES - the headline system. Scene-scoped and baked into the hub scenes, so
            // whether it is even ALIVE here answers the first real question: does the town
            // keep running because its scene stayed loaded, or does it simply cease to
            // exist and the true defect is what happens on RETURN?
            foreach (var wm in FindObjectsByType<WaveManager>(FindObjectsSortMode.None))
                ungated += Note(findings, "WaveManager(phase=" + wm.Phase + ",cd=" +
                                          wm.CountdownRemaining.ToString("0.0") + ")", wm.gameObject, active);

            // STRUCTURE DAMAGE-OVER-TIME - a burning town structure keeps losing HP.
            foreach (var burn in FindObjectsByType<StructureBurn>(FindObjectsSortMode.None))
            {
                if (!burn.IsBurning) continue;
                ungated += Note(findings, "StructureBurn(BURNING '" + burn.name + "')", burn.gameObject, active);
            }

            // THE HEART - event-driven, so it never ticks on its own; what matters is
            // whether it still EXISTS to be contact-damaged by something that does.
            foreach (var heart in FindObjectsByType<HeartController>(FindObjectsSortMode.None))
                ungated += Note(findings, "HeartController", heart.gameObject, active);

            // LIVE ENEMIES - these Update independently of the wave loop, and their bodies
            // are pooled under DontDestroyOnLoad, so "the wave manager is gone" does NOT
            // imply "nothing is attacking the town". Split by scene so dungeon enemies
            // (which must keep running) are never confused with town enemies (which must not).
            int townEnemies = 0, activeSceneEnemies = 0;
            foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                if (e.gameObject.scene.handle == active.handle) activeSceneEnemies++;
                else townEnemies++;
            }
            if (townEnemies > 0)
            {
                findings.Add("Enemy x" + townEnemies + " OUTSIDE the active scene [gated=" +
                             TownSuspension.SuspendedFor((GameObject)null) + "]");
            }
            if (activeSceneEnemies > 0)
                findings.Add("Enemy x" + activeSceneEnemies + " in the ACTIVE scene (these MUST keep running)");

            string report =
                "scene='" + active.name + "' suspended=" + TownSuspension.IsSuspended +
                " grace=" + TownSuspension.ReturnGraceRemaining.ToString("0.0") + "s" +
                " policy=" + TownSuspension.WavePolicy +
                " reason='" + TownSuspension.Reason + "' :: " +
                (findings.Count == 0 ? "no town systems alive here" : Join(findings));

            if (report == _lastReport) return;
            _lastReport = report;

            if (!TownSuspension.IsSuspended && findings.Count > 0)
                FlowTrace.Fail("TownProbe",
                    report + " -> town systems are alive while the player is NOT in a hub scene, " +
                    "and the suspension is NOT engaged. The scene-driven gate did not fire for this scene.");
            else if (ungated > 0)
                FlowTrace.Warn("TownProbe",
                    report + " -> " + ungated + " town system(s) are alive but NOT covered by " +
                    "TownSuspension.SuspendedFor. These are ungated leaks: the town is still being " +
                    "acted on while the player is away.");
            else
                FlowTrace.Step("TownProbe", report);
        }

        /// <summary>
        /// Record one system with its scene and whether the suspension gate covers it.
        /// Returns 1 when it is an UNGATED leak (alive, town-side, but not held), else 0.
        /// </summary>
        private static int Note(List<string> into, string label, GameObject go, Scene active)
        {
            bool inActive = go != null && go.scene.handle == active.handle;
            bool gated = TownSuspension.SuspendedFor(go);
            string where = go == null ? "<null>"
                : (string.IsNullOrEmpty(go.scene.name) ? "DontDestroyOnLoad" : go.scene.name);

            into.Add(label + " scene=" + where + (inActive ? "(ACTIVE-must-not-suspend)" : "") +
                     " gated=" + gated);

            // Something town-side, alive, and not held is the leak this probe exists to name.
            return (!inActive && !gated) ? 1 : 0;
        }

        private static string Join(IEnumerable<string> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(p);
            }
            return sb.ToString();
        }
    }
}
