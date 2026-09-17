// =============================================================================
// BattlePlansService -- WO-1804: the ONE lifecycle owner for both new plans kinds.
// Spawns the wave-2 Enemy Battle Plans in the defended town, spawns the Bastion Plans
// at the Ember Deep's boss, and opens each kind's reveal when it is SAFE to act on it.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Self-bootstraps exactly like CastleDefensePlansBootstrap (RuntimeInitializeOnLoadMethod,
// no scene authoring, no VillageSceneBuilder re-save, no .unity touched). One persistent
// DDOL service; a cheap 1 Hz scan decides everything from PERSISTED STATE, so every
// acceptance shape falls out of the pure rules in BattlePlans.cs.
//
// -----------------------------------------------------------------------------
// ⛔ EVERY RESOLUTION IS BY COMPONENT. THERE IS NO "SpawnPoint" TAG.
// -----------------------------------------------------------------------------
// CLAUDE.md section 7 and the RCA in-code at CastleDefensePlansService.cs:303-312:
// GameObject.FindGameObjectsWithTag("SpawnPoint") THROWS because the tag is not declared
// in TagManager.asset (four tags exist: Tower, Building, HeartTarget, Player), which is
// how WO-1038 shipped a drop that never spawned -- every scan died on that line. So:
//   * the hero is found by HeroHealth (component),
//   * the fallback seat by WaveSpawnPoint (component) through
//     CastleDefensePlansService.TryResolveSpawnSeat -- the PUBLIC PURE helper that
//     already owns the inset maths and the deterministic ordinal-name tie-break, reused
//     rather than re-derived (the four cardinal markers are equidistant, so a
//     nearest-by-distance pick resolves on FindObjectsByType ITERATION ORDER),
//   * the dungeon boss by OutpostEnemyGroupSpawner.IsBossGroup (component), the same
//     predicate ComposedDungeonHost.InstallBossContract uses at :278.
// Not one tag lookup exists in this file, and none may be added.
//
// -----------------------------------------------------------------------------
// THE BASTION REVEAL PLAYS IN THE BOSS ROOM. Owner ruling 2026-09-16, verbatim:
// "keep the reveal in the boss room".
// -----------------------------------------------------------------------------
// The drop, the pickup AND the screen all happen at the boss. What crosses the scene
// load is the CTA, and it crosses as TWO LATCHES, never as a route (BattlePlans.cs
// documents both, with the assembly-direction reason they live there):
//
//   1. "Raid the Iron Bastion" tapped in the boss room
//        -> BattlePlans.RequestRaidGridOnHubArrival(campId)   [latch 2]
//        -> BattlePlans.RequestDungeonExit(why)               [latch 1]
//   2. DungeonExitInteractable claims latch 1 on its own proximity tick and raises its
//      ORDINARY Continue/Cancel confirm -> ExecuteLeave -> _onLeave ==
//      DungeonController.ExitToVillage, which BANKS the run's crafting scatter.
//   3. The first hub frame after that, TryConsumeRaidGridRequest below opens the grid.
//
// ⛔ AND THIS IS WHY IT IS NOT A DIRECT CALL. Read at source 2026-09-16:
// ExecuteLeave (:1015-1057) is the ONLY path that banks the run, and a
// SceneRouter.GoRaid out of the boss room would bypass it and silently cost the player
// everything they carried down. Making a method on that class public would not have
// helped either: DeNelle.Dungeons references DeNelle.Village and NOT the reverse (both
// asmdefs read 2026-09-16), so a Village type can never call a Dungeons method however
// visible it is. The latch is the only shape that both respects that direction and
// keeps the player's own "Continue to exit" confirm in the loop.
//
// The in-dungeon toast survives ONLY as the fallback for a frame that cannot render the
// screen at all (see FallbackToastAfterScans) -- never alongside a reveal that worked,
// which would be a second voice over the moment.
//
// Instrumented per section 12: drop spawned / picked up (in the pickup) / reveal shown /
// CTA tapped / handed off, each a FlowTrace.Step carrying the wave number or the boss
// scene. Every early return NAMES ITS REASON -- CastleDefensePlansService.cs:143-145
// records what silent early returns cost the last time.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using UnityEngine;
using CoreDialogue = DeNelle.Core.Dialogue.DialogueService;

namespace DeNelle.Village
{
    /// <summary>Installs the single persistent <see cref="BattlePlansService"/>.</summary>
    public static class BattlePlansBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (BattlePlansService.Instance != null) return;
            var go = new GameObject("BattlePlansService");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<BattlePlansService>();

            // PROVE the service installed. The WO-1105 lesson: until Init emitted a line, a run
            // where the drop never spawned produced a log byte-identical to one where it did.
            FlowTrace.Step(BattlePlans.Sys,
                "battle-plans service installed (DDOL, self-bootstrap; camp threshold " +
                BattlePlans.RequiredWavesSurvived + " waves survived) -- WO-1804");
        }
    }

    /// <summary>
    /// Owns both WO-1804 plans lifecycles: decides from persisted state whether each drop
    /// should stand, spawns it, arms the dungeon boss hook, and opens each reveal where its
    /// CTA is actionable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattlePlansService : MonoBehaviour
    {
        public static BattlePlansService Instance { get; private set; }

        /// <summary>
        /// The dungeon whose boss drops the Bastion Plans when the data does not say.
        /// OWNER RULING 2026-09-16, verbatim: "Make it the one that's the ember deep".
        ///
        /// <para>⛔ THIS IS A FALLBACK, NOT THE AUTHORITY. The authority is DATA: the
        /// <c>plansDungeonId</c> field on the <c>iron_bastion</c> row of scene-configs.json,
        /// so the owner can move the drop to another dungeon by editing one string. This const
        /// exists only so an un-authored row degrades to the ruled dungeon with a Warn instead
        /// of silently dropping nothing anywhere.</para>
        ///
        /// <para>A composed dungeon's id IS its scene name -- DungeonWorldPortalSpawner.cs:1580
        /// builds its def as <c>MakeDef(EmberDeep, EmberDeep, "The Ember Deep", ...)</c>, i.e.
        /// id and sceneName are the same string -- which is why the scene-name compare below is
        /// a legitimate identity check and not a guess.</para>
        /// </summary>
        public const string DefaultBastionPlansDungeonId = "dg_ember_deep";

        /// <summary>Close enough to read immediately, outside the pickup trigger so the
        /// movement that ended the wave cannot collect the plans unseen. Mirrors
        /// CastleDefensePlansService.PlayerDropOffsetMetres.</summary>
        private const float PlayerDropOffsetMetres = 3.25f;

        private const float ScanInterval = 1.0f;         // the castle-plans / EchoWaveUnlockBridge cadence
        private const float HeartbeatSeconds = 5f;       // throttle for the "not spawning, because..." line

        private float _nextScan;
        private GameObject _campProp;
        private GameObject _bastionProp;

        // The dungeon boss hook: armed once per dungeon scene, torn down on scene change.
        private OutpostEnemyGroupSpawner _bossSpawner;
        private string _armedInScene;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            DisarmBossHook("service destroyed");
            if (Instance == this) Instance = null;
        }

        // =====================================================================
        //  THE SCAN
        // =====================================================================

        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null)
            {
                FlowTrace.Throttle(BattlePlans.Sys, "bp-idle-nostate", HeartbeatSeconds,
                    "battle-plans idle: no GameState yet (GameStateService" +
                    (svc == null ? ".Instance" : ".State") + " is null) -- still scanning");
                return;
            }

            // The ACTIVE scene, not this service's own: the service is DDOL, so its
            // gameObject.scene is the DontDestroyOnLoad pseudo-scene forever and would name
            // the wrong place on every check.
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            Guard.Try(BattlePlans.Sys, "camp plans scan", () => TickCampDrop(state));
            Guard.Try(BattlePlans.Sys, "bastion plans scan", () => TickBastionDrop(scene));
            Guard.Try(BattlePlans.Sys, "plans reveal scan", () => TickReveals(scene));
        }

        // =====================================================================
        //  KIND 1 -- the wave-2 Enemy Battle Plans, in the defended town
        // =====================================================================

        private void TickCampDrop(GameState state)
        {
            bool held = ProgressionUnlocks.IsUnlocked(BattlePlans.CampPlansId);
            bool propAlive = _campProp != null;
            bool inBattle = BattleLock.IsInBattle();
            bool everRaided = state.EverCompletedRaid;

            if (!BattlePlans.ShouldSpawnCampDrop(state.WavesCompleted, held, propAlive,
                                                inBattle, everRaided))
            {
                // A save that already raided is SKIPPED SILENTLY to the player, but never to the
                // log -- the brief's rule, and section 12's: name the reason on every look.
                FlowTrace.Throttle(BattlePlans.Sys, "bp-camp-idle", HeartbeatSeconds,
                    "camp plans not spawning: wavesCompleted=" + state.WavesCompleted +
                    " (need >=" + BattlePlans.RequiredWavesSurvived + ") held=" + held +
                    " propAlive=" + propAlive + " inBattle=" + inBattle +
                    " everRaided=" + everRaided + (everRaided
                        ? " -- SKIPPED FOR GOOD: this save has already raided, so there is nothing to introduce"
                        : " -- ShouldSpawnCampDrop false"));
                return;
            }

            // Only the defended town runs the village wave loop; a raid/dungeon/battle scene has
            // no village WaveManager and must never grow this drop (the castle-plans rule).
            if (FindAnyObjectByType<WaveManager>() == null)
            {
                FlowTrace.Throttle(BattlePlans.Sys, "bp-camp-nowavemgr", HeartbeatSeconds,
                    "camp plans WITHHELD: the rule says spawn (wavesCompleted=" +
                    state.WavesCompleted + ") but this scene has no village WaveManager -- not the " +
                    "defended town, so no drop grows here");
                return;
            }

            Vector3 seat = ResolveTownSeat(out string seatSource);
            var pickup = BattlePlansPickup.Spawn(BattlePlansKind.EnemyCamp, seat,
                "wave-clear beat, wavesCompleted=" + state.WavesCompleted + ", seat=" + seatSource);
            _campProp = pickup != null ? pickup.gameObject : null;
        }

        /// <summary>
        /// Prefer a visible seat just ahead of the hero who just survived the wave -- the same
        /// "the reward lands where you are standing" rule the castle plans learned in WO-1105.
        /// The deterministic gate seat is the loading/headless fallback, resolved BY COMPONENT
        /// through the castle plans' own public pure helper so the inset maths and the
        /// ordinal-name tie-break exist exactly once in the repo.
        /// </summary>
        private static Vector3 ResolveTownSeat(out string source)
        {
            var heroes = FindObjectsByType<HeroHealth>(FindObjectsSortMode.None);
            if (heroes != null)
            {
                for (int i = 0; i < heroes.Length; i++)
                {
                    var hero = heroes[i];
                    if (hero == null || !hero.gameObject.activeInHierarchy) continue;

                    Vector3 forward = hero.transform.forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.01f) { forward = -hero.transform.position; forward.y = 0f; }
                    if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

                    source = "player:" + hero.name;
                    return GroundSnap(hero.transform.position + forward.normalized * PlayerDropOffsetMetres);
                }
            }

            var spawns = FindObjectsByType<WaveSpawnPoint>(FindObjectsSortMode.None);
            var candidates = new List<CastleDefensePlansService.SeatCandidate>(
                spawns != null ? spawns.Length : 0);
            if (spawns != null)
                for (int i = 0; i < spawns.Length; i++)
                {
                    if (spawns[i] == null) continue;
                    candidates.Add(new CastleDefensePlansService.SeatCandidate(
                        spawns[i].name, spawns[i].transform.position, spawns[i].GatePosition));
                }

            if (CastleDefensePlansService.TryResolveSpawnSeat(candidates, out var seat, out string via))
            {
                source = "fallback:" + via;
                return GroundSnap(seat);
            }

            source = "fallback:heart-approach";
            return GroundSnap(new Vector3(0f, 0f, 10f));   // near the Heart, on the approach
        }

        private static Vector3 GroundSnap(Vector3 seat)
        {
            if (Physics.Raycast(seat + Vector3.up * 20f, Vector3.down, out var hit, 60f))
                return hit.point + Vector3.up * 0.15f;
            return seat + Vector3.up * 0.3f;
        }

        // =====================================================================
        //  KIND 2 -- the Bastion Plans, on the Ember Deep's boss
        // =====================================================================

        /// <summary>
        /// The authored dungeon whose boss carries the Bastion Plans, READ FROM DATA (the
        /// <c>plansDungeonId</c> field on the iron_bastion scene-config row). Falls back to
        /// the owner's ruled dungeon WITH a Warn -- never silently to nothing.
        /// </summary>
        public static string BastionPlansDungeonId()
        {
            var def = SceneConfigCatalog.Find(BattlePlans.BastionConfigId);
            if (def != null && !string.IsNullOrEmpty(def.plansDungeonId)) return def.plansDungeonId;
            FlowTrace.Warn(BattlePlans.Sys,
                "scene-configs.json row '" + BattlePlans.BastionConfigId + "' authors no " +
                "plansDungeonId -- falling back to the ruled default '" +
                DefaultBastionPlansDungeonId + "'. Author the field to move the drop.");
            return DefaultBastionPlansDungeonId;
        }

        /// <summary>The dungeon's player-facing name for the lock sentence, from the same row.
        /// Falls back to the id so a missing string is visible, never invented.</summary>
        public static string BastionPlansDungeonName()
        {
            var def = SceneConfigCatalog.Find(BattlePlans.BastionConfigId);
            if (def != null && !string.IsNullOrEmpty(def.plansDungeonName)) return def.plansDungeonName;
            return BastionPlansDungeonId();
        }

        private void TickBastionDrop(string scene)
        {
            bool held = ProgressionUnlocks.IsUnlocked(BattlePlans.BastionPlansId);
            string wanted = BastionPlansDungeonId();
            bool rightDungeon = !string.IsNullOrEmpty(scene) &&
                                string.Equals(scene, wanted, System.StringComparison.OrdinalIgnoreCase);

            if (held || !rightDungeon)
            {
                if (_armedInScene != null) DisarmBossHook(held ? "plans held" : "left the dungeon");
                FlowTrace.Throttle(BattlePlans.Sys, "bp-bastion-idle", HeartbeatSeconds,
                    "bastion plans not arming: scene='" + scene + "' wanted='" + wanted +
                    "' rightDungeon=" + rightDungeon + " held=" + held);
                return;
            }

            // Arm once per entry into the authored dungeon. Resolved BY COMPONENT with the same
            // IsBossGroup predicate ComposedDungeonHost uses, and the same IsBossCleared check it
            // makes at :290 -- so a re-arm LATER IN THE SAME SESSION (the service re-armed after a
            // scene shuffle, or the drop prop died with a floor) still seats the plans without
            // waiting for a second event.
            //
            // ⚠ SAME-SESSION ONLY, AND THAT IS CORRECT, NOT A GAP. Read at source 2026-09-16:
            // IsBossCleared is a runtime auto-property, and the persisted-looking twin
            // DungeonRuntimeState.BossDefeated is explicitly per-RUN ("defeated this run",
            // guarded by _runActive, DungeonRuntimeState.cs:460-469) and lives in the Dungeons
            // assembly, which DeNelle.Village cannot reference anyway (Dungeons -> Village
            // already; the reverse is a cycle). So a player who leaves the dungeon without
            // walking over the plans meets a fresh boss on the next run and drops them again --
            // nothing is lost, and the HELD flag is the only thing that ends the offer.
            if (!string.Equals(_armedInScene, scene, System.StringComparison.OrdinalIgnoreCase))
            {
                DisarmBossHook("re-arming for '" + scene + "'");
                var spawners = FindObjectsByType<OutpostEnemyGroupSpawner>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; spawners != null && i < spawners.Length; i++)
                {
                    if (spawners[i] == null || !spawners[i].IsBossGroup) continue;
                    _bossSpawner = spawners[i];
                    break;
                }
                _armedInScene = scene;

                if (_bossSpawner == null)
                {
                    FlowTrace.Warn(BattlePlans.Sys,
                        "bastion plans: '" + scene + "' is the authored plans dungeon but NO " +
                        "OutpostEnemyGroupSpawner with IsBossGroup was found in it -- the drop " +
                        "cannot arm. Nothing else is affected; the Bastion stays locked.");
                    return;
                }

                _bossSpawner.BossCleared += OnBossCleared;
                FlowTrace.Step(BattlePlans.Sys,
                    "bastion plans ARMED on the boss group of '" + scene + "' (room='" +
                    _bossSpawner.RoomId + "', alreadyCleared=" + _bossSpawner.IsBossCleared + ") -- WO-1804");
            }

            // State-driven, exactly like the castle prop: whether the clear happened this
            // session (the event) or last session (IsBossCleared on arm), the rule is the same.
            bool bossDown = _bossSpawner != null && _bossSpawner.IsBossCleared;
            if (!BattlePlans.ShouldSpawnBastionDrop(bossDown, true, held, _bastionProp != null)) return;

            Vector3 seat = ResolveBossSeat();
            var pickup = BattlePlansPickup.Spawn(BattlePlansKind.Bastion, seat,
                "dungeon boss defeated in '" + scene + "', room='" +
                (_bossSpawner != null ? _bossSpawner.RoomId : "?") + "'");
            _bastionProp = pickup != null ? pickup.gameObject : null;

            // ⛔ NO TOAST HERE. Owner ruling 2026-09-16: "keep the reveal in the boss room" -- the
            // REVEAL is the beat at the boss now, so a toast announcing the same thing would be a
            // second voice over the moment. The toast survives only as the FALLBACK for a frame
            // that cannot render the screen at all (TryReveal below), which is the one case where
            // the drop would otherwise land in silence.
        }

        private void OnBossCleared(OutpostEnemyGroupSpawner source)
        {
            FlowTrace.Step(BattlePlans.Sys,
                "bastion plans: BossCleared fired for room='" +
                (source != null ? source.RoomId : "?") + "' -- next scan seats the drop");
            _nextScan = 0f;   // seat it on the very next frame, not up to a second later
        }

        private Vector3 ResolveBossSeat()
        {
            // At the boss's feet: the spawner's own transform is the authored boss seat, which is
            // where the player is standing when it dies. Nudged toward the hero so the prop is
            // never inside the corpse.
            Vector3 at = _bossSpawner != null ? _bossSpawner.transform.position : Vector3.zero;
            var heroes = FindObjectsByType<HeroHealth>(FindObjectsSortMode.None);
            for (int i = 0; heroes != null && i < heroes.Length; i++)
            {
                if (heroes[i] == null || !heroes[i].gameObject.activeInHierarchy) continue;
                Vector3 toward = heroes[i].transform.position - at;
                toward.y = 0f;
                if (toward.sqrMagnitude > 0.01f) at += toward.normalized * 1.6f;
                break;
            }
            return GroundSnap(at);
        }

        private void DisarmBossHook(string why)
        {
            if (_bossSpawner != null)
            {
                _bossSpawner.BossCleared -= OnBossCleared;
                FlowTrace.Step(BattlePlans.Sys, "bastion plans boss hook DISARMED (" + why + ")");
            }
            _bossSpawner = null;
            _armedInScene = null;
        }

        // =====================================================================
        //  THE REVEAL -- opened where its CTA is actionable
        // =====================================================================

        /// <summary>Scans before the fallback toast fires. The reveal retries once a second, so
        /// this is ~5 s of a screen that will not build (CustomDialogue off, an overlay-build
        /// failure, an arbiter that keeps refusing) before the drop is announced in words instead.
        /// MEASURED as a bound, not chosen as a feel: the reveal's own arbiter retry runs for
        /// OpenRetryMaxSeconds, so anything longer than a few seconds here would let the player
        /// stand over an unannounced prop.</summary>
        private const int FallbackToastAfterScans = 5;

        private readonly Dictionary<BattlePlansKind, int> _revealAttempts =
            new Dictionary<BattlePlansKind, int>();
        private readonly HashSet<BattlePlansKind> _fallbackToasted = new HashSet<BattlePlansKind>();

        private void TickReveals(string scene)
        {
            TryConsumeRaidGridRequest(scene);
            TryReveal(BattlePlansKind.EnemyCamp, scene);
            TryReveal(BattlePlansKind.Bastion, scene);
        }

        /// <summary>
        /// LATCH 2's consumer. The reveal's CTA in the boss room cannot open the raid grid there
        /// (the grid is a town door, and a raid load from a dungeon skips the run's banking), so it
        /// latched the request and asked the dungeon's own exit to take the player home. This is the
        /// far side: the FIRST hub frame after that exit opens the grid.
        ///
        /// <para>⛔ STILL NOT A ROUTE. Nothing here loads a scene; it opens a panel in the scene it
        /// is already standing in. The hub was reached by DungeonExitInteractable -> ExecuteLeave ->
        /// DungeonController.ExitToVillage, i.e. the banked path, which is the entire point.</para>
        /// </summary>
        private void TryConsumeRaidGridRequest(string scene)
        {
            if (!BattlePlans.HasPendingRaidGrid) return;
            if (!HubScenes.IsHub(scene))
            {
                FlowTrace.Throttle(BattlePlans.Sys, "bp-grid-waiting", HeartbeatSeconds,
                    "raid-grid request WAITING: active scene '" + scene + "' is not a hub yet " +
                    "(the sanctioned dungeon exit is still carrying the player home)");
                return;
            }
            if (BattleLock.IsInBattle() || PanelManager.AnyOpen || CoreDialogue.IsRunning)
            {
                FlowTrace.Throttle(BattlePlans.Sys, "bp-grid-busy", HeartbeatSeconds,
                    "raid-grid request HELD: inBattle=" + BattleLock.IsInBattle() +
                    " anyPanelOpen=" + PanelManager.AnyOpen +
                    " dialogueRunning=" + CoreDialogue.IsRunning + " -- retrying next scan");
                return;
            }

            string campId = BattlePlans.ConsumeRaidGridRequest();
            FlowTrace.Step(BattlePlans.Sys,
                "raid-grid request HONOURED in hub '" + scene + "' for camp '" + campId +
                "': the player left the dungeon through its own banked exit and the grid opens now. " +
                "RaidSelectionScreen.Open() takes no argument, so the camp is its own row. -- WO-1804");
            Guard.Try(BattlePlans.Sys, "open raid grid on hub arrival",
                () => DeNelle.Village.Hero.RaidSelectionScreen.Open());
        }

        /// <summary>
        /// Can this kind's reveal play in <paramref name="scene"/>? PURE so the regression pins it:
        /// the camp reveal is a TOWN beat, and the Bastion reveal plays IN THE BOSS ROOM.
        ///
        /// <para>OWNER RULING 2026-09-16, verbatim: <i>"keep the reveal in the boss room"</i>. The
        /// earlier build deferred it to the hub to protect the run's banking; that protection now
        /// lives in the CTA's latch instead (it asks the dungeon's own exit to take the player home),
        /// so the screen can stay where the drama is.</para>
        /// </summary>
        public static bool RevealPlayableIn(BattlePlansKind kind, string scene, bool isHub,
                                            bool isPlansDungeon)
            => isHub || (kind == BattlePlansKind.Bastion && isPlansDungeon);

        private void TryReveal(BattlePlansKind kind, string scene)
        {
            if (!ProgressionUnlocks.IsUnlocked(BattlePlans.PlansIdFor(kind))) return;
            if (BattlePlansReveal.HasBeenSeen(kind)) return;

            bool isHub = HubScenes.IsHub(scene);
            bool isPlansDungeon = kind == BattlePlansKind.Bastion &&
                                  !string.IsNullOrEmpty(scene) &&
                                  string.Equals(scene, BastionPlansDungeonId(),
                                                System.StringComparison.OrdinalIgnoreCase);

            if (!RevealPlayableIn(kind, scene, isHub, isPlansDungeon))
            {
                FlowTrace.Throttle(BattlePlans.Sys, "bp-reveal-wrongscene-" + kind, HeartbeatSeconds,
                    "plans reveal DEFERRED kind=" + kind + ": scene '" + scene + "' is neither a hub " +
                    "nor this kind's own dungeon. The plans are already HELD and the unlock already " +
                    "stands; only the screen waits.");
                return;
            }

            // ⚠ THE BATTLE CHECK IS NOT DROPPED IN THE DUNGEON, IT IS RE-AIMED. BattleLock is the
            // TOWN wave authority and the camp reveal must never open over a live wave. A dungeon
            // boss room is combat by definition and the boss is already dead at this point, so
            // gating the Bastion reveal on it would deadlock the very moment the owner asked for.
            // PanelManager and the dialogue check still apply everywhere: this screen must never
            // stack on another modal.
            bool blockedByBattle = !isPlansDungeon && BattleLock.IsInBattle();
            if (blockedByBattle || PanelManager.AnyOpen || CoreDialogue.IsRunning)
            {
                FlowTrace.Throttle(BattlePlans.Sys, "bp-reveal-busy-" + kind, HeartbeatSeconds,
                    "plans reveal DEFERRED kind=" + kind + ": blockedByBattle=" + blockedByBattle +
                    " anyPanelOpen=" + PanelManager.AnyOpen + " dialogueRunning=" + CoreDialogue.IsRunning +
                    " -- retrying on the next scan (never over a live town wave, never over a modal)");
                return;
            }

            _revealAttempts.TryGetValue(kind, out int attempts);
            _revealAttempts[kind] = attempts + 1;

            Guard.Try(BattlePlans.Sys, "show plans reveal", () => BattlePlansReveal.Show(kind));

            // THE FALLBACK, AND ONLY AS A FALLBACK. If the screen still has not been delivered
            // after FallbackToastAfterScans looks, this frame cannot render it (CustomDialogue off,
            // an overlay that will not build, an arbiter that keeps refusing) -- so the drop is
            // announced in WORDS rather than landing in silence. Once per kind, ever.
            if (attempts + 1 >= FallbackToastAfterScans && !BattlePlansReveal.HasBeenSeen(kind) &&
                _fallbackToasted.Add(kind))
            {
                FlowTrace.Warn(BattlePlans.Sys,
                    "plans reveal UNDELIVERED kind=" + kind + " after " + (attempts + 1) +
                    " scans in scene '" + scene + "' -- falling back to a one-line toast so the drop " +
                    "is not silent. The plans are HELD and the unlock stands regardless; the seen " +
                    "flag is NOT set, so the screen still retries.");
                Guard.Try(BattlePlans.Sys, "plans fallback toast", () =>
                    ElarionUiKit.ShowToast(BattlePlans.TitleFor(kind) + " recovered.",
                        ElarionUiKit.ToastTone.Info));
            }
        }
    }
}
