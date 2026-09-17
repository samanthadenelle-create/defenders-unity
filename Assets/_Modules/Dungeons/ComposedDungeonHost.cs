// =============================================================================
// ComposedDungeonHost -- the live owner of one composed (Pipeline A) dungeon run.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Dungeons   Namespace: DeNelle.Dungeons
//
// ComposedDungeonBootstrap INSTALLS; this component OWNS. It holds the run state
// for the scene's lifetime so the two things that need it after load -- the lantern
// HUD and the exit's payout -- have somewhere to read it from. Before WO-1112 the
// bootstrap created a DungeonRuntimeState in a local variable and let it fall out of
// scope, which is why the composed exit had nothing to pay a run out from.
//
// ⚠ WHY THE HERO PILLARS ARM ONE FRAME LATE, AND WHY THAT IS THE POINT (WO-1112):
// SceneRouter.GoDungeonScene now CARRIES the town hero into a composed dungeon,
// because the baked Keeper has no HeroAbilities (Q/W/E/R were dead, silently). That
// means TWO Player-tagged heroes exist on the frame the scene loads -- the carried
// one and the baker's own -- and HeroControlEnsurer.DedupeHeroes destroys the baked
// one in the carried hero's favour. Unity's Destroy is deferred to END OF FRAME, so
// a GameObject.FindGameObjectWithTag("Player") on the load frame can hand back the
// DOOMED rig. Arming the lantern on that object would attach the light, the HUD feed
// and the ambush director to something that ceases to exist moments later -- and it
// would do so SILENTLY, which is this whole pipeline's signature failure. Waiting a
// frame costs nothing and makes the hero resolution unambiguous. The run state and
// StartRun still happen immediately; only the hero-dependent half waits.
//
// Instrumented per CLAUDE.md sec.12 -- [Flow:ComposedDungeon] on every step and
// branch, including the ones that used to fail with no trace at all.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Dungeons.RoomForge;
using DeNelle.Village;
using Newtonsoft.Json;
using UnityEngine;
using CoreDialogue = DeNelle.Core.Dialogue;

namespace DeNelle.Dungeons
{
    /// <summary>Runtime owner of a composed dungeon's run state, lantern, HUD and ambush.</summary>
    [DisallowMultipleComponent]
    public sealed class ComposedDungeonHost : MonoBehaviour
    {
        private const string Sys = "ComposedDungeon";

        /// <summary>
        /// The host for the composed dungeon currently loaded, or null. Single-scene by
        /// construction (one DungeonCompose_* root per scene, and the bootstrap is idempotent).
        /// </summary>
        public static ComposedDungeonHost Current { get; private set; }

        private DungeonRuntimeState _state;
        private Transform _composeRoot;
        private Lantern _lantern;
        private DungeonHudController _hud;
        private DungeonComposeLayout _layout;
        private OutpostEnemyGroupSpawner _bossSpawner;
        private readonly List<BreakableContainer> _bossHoard = new List<BreakableContainer>();

        /// <summary>The live run record for this dungeon. Null only if StartRun never ran.</summary>
        public DungeonRuntimeState RunState => _state;

        /// <summary>The Keeper's lantern once armed (null during the first frame).</summary>
        public Lantern ActiveLantern => _lantern;

        /// <summary>Installer entry point — see the header for why the hero half is deferred.</summary>
        public void Install(Transform composeRoot, DungeonRuntimeState state)
        {
            _composeRoot = composeRoot;
            _state = state;
            Current = this;
            StartCoroutine(ArmHeroPillarsNextFrame());
        }

        private void OnDestroy()
        {
            if (_bossSpawner != null) _bossSpawner.BossCleared -= HandleBossCleared;

            // WO-1805 Lane A: the teach's subscriptions are released with the host. DialogueService
            // is STATIC, so a live EndedWithId handler on a destroyed host would fire into a dead
            // object on every later conversation in the session.
            if (_teachHooked && _lantern != null)
            {
                _lantern.OilStoneUsed -= HandleFirstOilStoneRefill;
                _lantern.FinalWarningEntered -= HandleLanternFinalWarning;
            }
            _teachHooked = false;
            if (_introEndedHooked)
            {
                CoreDialogue.DialogueService.EndedWithId -= HandleIntroDialogueEnded;
                _introEndedHooked = false;
            }

            if (Current == this) Current = null;
        }

        private IEnumerator ArmHeroPillarsNextFrame()
        {
            // One frame: long enough for HeroControlEnsurer's sceneLoaded pass to run and for
            // the destroy of the losing duplicate hero to actually resolve.
            yield return null;
            Guard.Try(Sys, "arm composed hero pillars", ArmHeroPillars);
        }

        private void ArmHeroPillars()
        {
            using var _scope = FlowTrace.Enter(Sys, $"arm hero pillars on '{gameObject.scene.name}'");

            var heroGo = GameObject.FindGameObjectWithTag("Player");
            if (heroGo == null)
            {
                FlowTrace.Warn(Sys, "no Player-tagged hero one frame after load - lantern, oil meter and ambush are NOT armed for this run.");
                return;
            }
            FlowTrace.Step(Sys, $"hero resolved as '{heroGo.name}' (scene='{heroGo.scene.name}') - carried hero wins the dedupe when GoDungeonScene armed the WO-1112 carry.");

            // WO-1222 -- THE PLACEMENT ASSERTION THE COMPOSED PIPELINE NEVER HAD.
            // The hand-built pipeline teleports its Keeper every Begin() (DungeonController.
            // PlaceHero); a composed scene has no DungeonController, so until this line NOTHING
            // in the composed path ever checked where the carried hero actually ended up. It is
            // deliberately an OUTCOME check rather than a placement call: the hero root is DDOL
            // and other DDOL systems write that same transform -- on 2026-08-26 the owner entered
            // dg_healers_cottage standing at (5000, 0, 4991), which is BattleArena's staged hero
            // stance to the centimetre, and the black screen was the camera honestly following a
            // hero parked ~7km away in an arena staging area. One authority, both pipelines:
            // DungeonHeroSeat. It stands down while a real staged battle owns the hero, so the
            // seat is re-checked by the watchdog below once that fight resolves.
            DungeonHeroSeat.VerifyArrival(heroGo, gameObject.scene, null, 0f, "composed");
            StartCoroutine(SeatWatchdog(heroGo));

            DungeonCandleVfxInstaller.Rebind(gameObject.scene, heroGo.transform);

            // WO-1805 section 7: the layout is loaded BEFORE the lantern is armed, purely so the
            // entry net below can state the dungeon's ROOM COUNT next to its light budget. LoadLayout
            // is a pure read of _composeRoot + Resources and has no ordering dependency on the hero
            // half; the ambush director still reads the same _layout further down.
            _layout = LoadLayout();

            // Collect baked oil stones (planar refill, same contract as cottage).
            var stones = CollectOilStones();

            // Ensure a lantern light follows the Keeper.
            _lantern = heroGo.GetComponentInChildren<Lantern>(true);
            if (_lantern == null)
            {
                var lightGo = new GameObject("Lantern");
                lightGo.transform.SetParent(heroGo.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
                lightGo.AddComponent<Light>();
                _lantern = lightGo.AddComponent<Lantern>();
                FlowTrace.Step(Sys, "created Lantern under Player (composed bake had none)");
            }

            _lantern.ConfigureStandalone(stones, heroGo.transform);

            // ⛔ WO-1805 section 7 - THE STANDING NET, and the cheapest half of the whole ticket.
            // A dungeon's FREE LIGHT CEILING is burn x (1 + caches): dg_starter_loop authors ONE
            // cache for ELEVEN rooms, so the first dungeon in the game hands the player ~400s of
            // light for an 11-room level and then leaves them in a 1.35u halo for the remainder,
            // with the ambush multiplier on. Nothing announced that. Now every entry self-reports
            // its own budget, so an under-provisioned dungeon names itself on the way in instead of
            // waiting for the owner's eyes (CLAUDE.md section 14).
            //
            // ⛔ THE BURN IS READ, NEVER WRITTEN. DungeonLanternBalance.SecondsToEmpty exists for
            // exactly this; a literal 200 here would go stale the first time the drain moves (the
            // Lane C rail can move it at runtime), which is the duplicated-state failure CLAUDE.md
            // sections 2 / 5 / 8 each record a scar from.
            float burnSeconds = DungeonLanternBalance.SecondsToEmpty;
            int roomCount = _layout != null && _layout.rooms != null ? _layout.rooms.Count : 0;
            float ceilingSeconds = burnSeconds * (1 + stones.Count);
            FlowTrace.Step(Sys,
                $"lantern armed standalone: stones={stones.Count} rooms={roomCount} hero='{heroGo.name}' " +
                $"burn={_lantern.EstimatedSecondsRemaining:F0}s at full oil (WO-1112 tripled via dungeon-balance.json), " +
                $"authored burn={burnSeconds:F0}s -> free light ceiling={ceilingSeconds:F0}s for {roomCount} room(s) " +
                $"= {(roomCount > 0 ? ceilingSeconds / roomCount : 0f):F0}s per room before the dark.");

            // Every authored cache is also a one-use emergency still. It spends real persisted
            // crafting materials for a partial refill; the free cache itself is independently
            // one-use in Lantern, so neither path can become an infinite fountain.
            if (_composeRoot != null)
            {
                foreach (var marker in _composeRoot.GetComponentsInChildren<ComposedOilStone>(true))
                {
                    if (marker == null) continue;
                    var still = marker.GetComponent<ComposedOilStill>();
                    if (still == null) still = marker.gameObject.AddComponent<ComposedOilStill>();
                    still.Configure(heroGo.transform, _lantern);
                }
            }

            InstallOilHud();

            // WO-1805 Lane A: the teach, AFTER the meter is bound - so the first thing the player
            // reads and the first thing they can look at agree.
            Guard.Try(Sys, "install lantern teach", InstallLanternTeach);

            // WO-1001 slice 6: darkness ambush director (higher odds when oil critical).
            ComposedKeyBag.Clear();
            var ambush = gameObject.GetComponent<ComposedAmbushDirector>();
            if (ambush == null) ambush = gameObject.AddComponent<ComposedAmbushDirector>();
            int tier = _layout != null ? Mathf.Max(1, _layout.tier) : 1;
            ambush.Configure(_lantern, heroGo.transform, _state, tier);
            FlowTrace.Step(Sys, $"ComposedAmbushDirector armed (slice 6 darkness ambush, tier={tier})");

            InstallBossContract();

            // WO-1001 1b/7: count what the bake actually left in the scene. These are the pillars
            // whose bake-time Configure used to be discarded by SaveScene, so a zero here on a
            // dungeon that authored them is the exact signature of that class of defect returning.
            if (_composeRoot != null)
            {
                int ports = _composeRoot.GetComponentsInChildren<DungeonPortLink>(true).Length;
                int locks = _composeRoot.GetComponentsInChildren<ComposedLockedPort>(true).Length;
                int keys = _composeRoot.GetComponentsInChildren<ComposedKeyPickup>(true).Length;
                int traps = _composeRoot.GetComponentsInChildren<ComposedTrapHazard>(true).Length;
                FlowTrace.Step(Sys,
                    $"pillars present in '{gameObject.scene.name}': stairPorts={ports} lockedPorts={locks} " +
                    $"keys={keys} traps={traps} oilStones={stones.Count}");
            }
        }

        /// <summary>
        /// WO-1222 — THE STANDING NET, not just an entry check.
        /// <para>
        /// The entry assertion above proves the ARRIVAL. It cannot prove the next thirty seconds:
        /// the hero root is DontDestroyOnLoad and so is BattleArena, whose staged stance sits ~7km
        /// out at (5000, 0, 5000) — a fight that stages and then leaves the hero there (an
        /// orphaned encounter, a return warp that never lands) puts the player back on a black
        /// screen with a working joystick, at 60 fps, with nothing thrown. That state is
        /// unrecoverable by the player and must never be silent.
        /// </para>
        /// <para>
        /// So: while a staged battle is live, this stands down completely — the arena legitimately
        /// owns the hero. Only when the hero is inside the arena footprint with NO battle running,
        /// for a sustained window, does it call the one authority. The window is what keeps a
        /// legitimate mid-warp frame (the arena's own stage-in / return hops pass through) from
        /// tripping it, exactly as BattleArena's own out-of-arena grace does.
        /// </para>
        /// </summary>
        private IEnumerator SeatWatchdog(GameObject heroGo)
        {
            const float PollSeconds = 0.5f;
            const float StrandedGraceSeconds = 3f;
            var wait = new WaitForSecondsRealtime(PollSeconds);
            float stranded = 0f;
            FlowTrace.Step(Sys,
                $"seat watchdog ARMED on '{gameObject.scene.name}' - a hero left inside the staged arena with no live " +
                $"battle for {StrandedGraceSeconds:0}s is corrected by DungeonHeroSeat and reported, never left silent.");

            while (true)
            {
                yield return wait;
                if (heroGo == null)
                {
                    FlowTrace.Step(Sys, "seat watchdog STOPPED - the hero object is gone (scene teardown or hero swap).");
                    yield break;
                }
                if (DeNelle.Village.Arena.BattleArena.AnyBattleInProgress)
                {
                    stranded = 0f;   // the arena owns the hero; nothing here is a fault
                    continue;
                }
                if (!DeNelle.Village.Arena.BattleArena.IsArenaPosition(heroGo.transform.position))
                {
                    stranded = 0f;
                    continue;
                }

                stranded += PollSeconds;
                if (stranded < StrandedGraceSeconds) continue;

                FlowTrace.Fail(Sys,
                    $"seat watchdog FIRED: hero has been inside BattleArena's staged arena at {heroGo.transform.position} " +
                    $"for {stranded:0.0}s in composed scene '{gameObject.scene.name}' with NO battle running. That is the " +
                    "black-screen state - the camera follows the hero honestly and there is nothing there to draw. " +
                    "Handing it to the one placement authority.");
                var verdict = DungeonHeroSeat.VerifyArrival(heroGo, gameObject.scene, null, 0f, "composed/watchdog");
                stranded = 0f;

                // A net that cannot fix the thing it found must not become a per-3s log firehose:
                // say so ONCE, plainly, and stop. The Fail above is already in the break-log, and a
                // repeating line would bury it under copies of itself.
                if (!verdict.Corrected && !verdict.DeferredToBattle)
                {
                    FlowTrace.Fail(Sys,
                        $"seat watchdog STANDING DOWN in '{gameObject.scene.name}': the hero is in the arena and this scene " +
                        $"offers no seat to correct it to ({verdict.Detail}). The run is unplayable and no further correction " +
                        "is possible from here - re-bake the dungeon so it carries a '" + DungeonHeroSeat.ArrivalMarkerName + "'.");
                    yield break;
                }
            }
        }

        private DungeonComposeLayout LoadLayout()
        {
            if (_composeRoot == null) return null;
            const string prefix = "DungeonCompose_";
            string id = _composeRoot.name.StartsWith(prefix, System.StringComparison.Ordinal)
                ? _composeRoot.name.Substring(prefix.Length)
                : gameObject.scene.name;
            var text = Resources.Load<TextAsset>("Data/Canonical/dungeon-layouts/" + id);
            if (text == null)
            {
                FlowTrace.Warn(Sys, $"difficulty/boss contract: layout '{id}' not found");
                return null;
            }
            return Guard.Try(Sys, $"parse layout '{id}' for difficulty/boss contract",
                () => JsonConvert.DeserializeObject<DungeonComposeLayout>(text.text), null);
        }

        private void InstallBossContract()
        {
            if (_composeRoot == null || _state == null || _layout == null) return;

            var spawners = FindObjectsByType<OutpostEnemyGroupSpawner>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < spawners.Length; i++)
            {
                var candidate = spawners[i];
                if (candidate != null && candidate.gameObject.scene == gameObject.scene && candidate.IsBossGroup)
                {
                    _bossSpawner = candidate;
                    break;
                }
            }
            if (_bossSpawner == null)
            {
                FlowTrace.Step(Sys, "no authored boss spawner - boss gate contract not needed");
                return;
            }

            _bossSpawner.BossCleared += HandleBossCleared;
            if (_bossSpawner.IsBossCleared) HandleBossCleared(_bossSpawner);

            Transform bossRoom = _composeRoot.Find(_bossSpawner.RoomId);
            if (bossRoom == null)
            {
                FlowTrace.Warn(Sys, $"boss room '{_bossSpawner.RoomId}' not found - exits cannot be gated by room");
                return;
            }
            Bounds roomBounds = DungeonRoomBounds.Compute(bossRoom.gameObject);
            var containers = FindObjectsByType<BreakableContainer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < containers.Length; i++)
            {
                var chest = containers[i];
                if (chest == null || chest.gameObject.scene != gameObject.scene) continue;
                if (DungeonRoomBounds.SqrDistanceXZ(roomBounds, chest.transform.position) > 0.25f) continue;
                _bossHoard.Add(chest);
                chest.gameObject.SetActive(_state.BossDefeated);
            }
            var exits = FindObjectsByType<DungeonExitInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int gated = 0;
            for (int i = 0; i < exits.Length; i++)
            {
                var exit = exits[i];
                if (exit == null || exit.gameObject.scene != gameObject.scene) continue;
                if (DungeonRoomBounds.SqrDistanceXZ(roomBounds, exit.transform.position) > 0.25f) continue;
                exit.SetBossGate(_state);
                gated++;
            }
            FlowTrace.Step(Sys, $"boss contract armed: room='{_bossSpawner.RoomId}' gatedExits={gated} " +
                $"sealedHoard={_bossHoard.Count}");
        }

        private void HandleBossCleared(OutpostEnemyGroupSpawner source)
        {
            if (_state == null || _state.BossDefeated) return;
            _state.MarkBossDefeated();
            for (int i = 0; i < _bossHoard.Count; i++)
                if (_bossHoard[i] != null) _bossHoard[i].gameObject.SetActive(true);
            FlowTrace.Step(Sys, $"boss clear recorded once for '{gameObject.scene.name}' - boss-room exits unlocked");
        }

        private List<DungeonOilStone> CollectOilStones()
        {
            var stones = new List<DungeonOilStone>();
            if (_composeRoot == null) return stones;
            var markers = _composeRoot.GetComponentsInChildren<ComposedOilStone>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                var m = markers[i];
                if (m == null) continue;
                Vector3 p = m.transform.position;
                stones.Add(new DungeonOilStone
                {
                    id = m.Id,
                    roomId = "",
                    position = new DungeonPoint { x = p.x, y = p.y, z = p.z },
                    radius = m.Radius,
                });
            }
            return stones;
        }

        /// <summary>
        /// WO-1112 (A7): installs the SAME oil meter the cottage pipeline uses.
        /// <para>
        /// THE DEFECT: DungeonHudController.SetLantern had exactly ONE production caller —
        /// DungeonController — and DungeonController is in no dg_* scene, while DungeonBaker
        /// never places the HUD. So the composed player watched an invisible flask drain to
        /// empty and then played the rest of the run in the dark, with the ambush multiplier
        /// on, with no meter and no idea why.
        /// </para>
        /// <para>
        /// ⚠ REUSED, NOT REBUILT, AND EXPLICITLY NOT A UXML PATH. CLAUDE.md sec.8: UXML does not
        /// render in player builds. DungeonHudController was rewritten code-first (WO-1005) for
        /// exactly that reason and builds its own uGUI canvas in OnEnable; it needs nothing from
        /// a scene but a live GameObject and a lantern pushed in through the existing SetLantern
        /// seam. Its serialized _document field stays null here, which its own HideLegacyUxmlHud
        /// treats as the expected code-built case.
        /// </para>
        /// </summary>
        private void InstallOilHud()
        {
            _hud = GetComponentInChildren<DungeonHudController>(true);
            if (_hud == null)
            {
                var hudGo = new GameObject("ComposedDungeonHud");
                hudGo.transform.SetParent(transform, false);
                _hud = hudGo.AddComponent<DungeonHudController>();
                FlowTrace.Step(Sys, "DungeonHudController installed on the composed host (code-built uGUI oil meter; composed bake places none)");
            }
            _hud.SetLantern(_lantern);
            FlowTrace.Step(Sys,
                $"oil meter bound to lantern '{(_lantern != null ? _lantern.name : "<null>")}' - " +
                "the composed run finally has a readable flask (WO-1112 A7).");
        }

        // =====================================================================
        //  WO-1805 LANE A - THE LANTERN TEACH
        // ---------------------------------------------------------------------
        //  Owner's report, verbatim (2026-09-16): "we never really ever go over the mechanics of
        //  the torch... nobody understands why the torch runs out and why just become suddenly
        //  dark. We need some kind of a first time in there to understand the torch and the light".
        //
        //  ⛔ THE DEFECT WAS NOT MISSING COPY. The teach was fully built, in her words, and
        //  UNREACHABLE: dun_torch_warden is delivered by TorchWardenDresser, whose only production
        //  caller is DungeonController, which exists in exactly ONE scene on disk
        //  (Dungeon_HealersCottage) - and every player-facing portal routes to a composed dg_*
        //  scene instead. So the player met the oil mechanic for the first time as an unexplained
        //  blackout. This is the composed path's own teach, on the composed path's own host.
        //
        //  ⚠ A DIALOGUE SCREEN, NEVER A WORLD ACTOR (memory tutorial-guide-body-one-time-then-images,
        //  owner ruling 2026-08-16: "i dont need to see it, can be a dialogue screen"). No Bryn body
        //  is spawned in a dungeon: no seating, no facing, no navmesh, no despawn lifecycle, no
        //  stall risk. Bryn SPEAKS (the lead's ruling; the copy is hers and her portrait resolves).
        //
        //  ⚠ TWO KEYS, AND THE DIFFERENCE IS LOAD-BEARING.
        //    IntroShownKey     - latched when the dialogue ACTUALLY ENDED. This is the "one-shot
        //                        means one-shot" gate: a save that has read it never reads it again,
        //                        which is what keeps the teach ABSENT when a session opens straight
        //                        into dg_ember_deep at the boss.
        //    IntroCompletedKey - latched when the player first REFILLS at an oil stone, i.e. does
        //                        the thing the teach described. That is the teach's completion, and
        //                        it also satisfies the gate, so a player who learned the mechanic by
        //                        doing it is never lectured about it afterwards.
        //  NEITHER is latched on the ATTEMPT. A Play() that renders nothing must leave the save
        //  untouched (the WO-844 potion-lesson class of bug: never mark taught on the attempt).
        // =====================================================================

        /// <summary>The composed-dungeon lantern teach. A row in dialogues.json, both twins.</summary>
        public const string LanternIntroDialogueId = "dun_lantern_intro";

        /// <summary>One-shot key: the intro dialogue rendered AND closed. Persisted in SeenTutorials.</summary>
        public const string LanternIntroShownKey = "dun_lantern_intro_shown";

        /// <summary>One-shot key: the player has refilled at an oil stone - the teach is complete.</summary>
        public const string LanternIntroCompletedKey = "dun_lantern_intro_done";

        /// <summary>One-shot key: the guttering warning line has been shown once on this save.</summary>
        public const string LanternGutteringShownKey = "dun_lantern_guttering_shown";

        /// <summary>
        /// The darkness beat's one line. ⚠ A TOAST, NOT A MODAL, on purpose: this fires while the
        /// fog wall is closing and ComposedAmbushDirector's darkness multiplier is arming, and a
        /// modal dialogue suppresses hero input for as long as it is open. ASCII hyphen only -
        /// CopyHygieneRegression retires em/en dashes from player copy (WO-1333 / WO-1588).
        /// </summary>
        public const string LanternGutteringLine = "Your flame gutters - find an oil stone.";

        private bool _teachHooked;
        private bool _introEndedHooked;

        private void InstallLanternTeach()
        {
            if (_lantern == null)
            {
                FlowTrace.Warn(Sys, "lantern teach NOT installed - no lantern was armed for this run, so there is " +
                                    "nothing to teach about and no refill to complete on.");
                return;
            }

            if (!_teachHooked)
            {
                _lantern.OilStoneUsed += HandleFirstOilStoneRefill;
                _lantern.FinalWarningEntered += HandleLanternFinalWarning;
                _teachHooked = true;
            }

            var svc = GameStateService.Instance;
            bool shown = HasSeen(svc, LanternIntroShownKey);
            bool completed = HasSeen(svc, LanternIntroCompletedKey);
            bool flagOn = DeNelle.Core.FeatureFlags.CustomDialogue;
            bool alreadyRunning = CoreDialogue.DialogueService.IsRunning;

            if (shown || completed)
            {
                FlowTrace.Step("DungeonTeach",
                    $"lantern intro: seen=true played=false key='{LanternIntroShownKey}' " +
                    $"(shown={shown} completed={completed}) - one-shot per save, nothing shown in " +
                    $"'{gameObject.scene.name}'.");
                return;
            }

            // ⛔ THE FLAG IS CHECKED BEFORE Play, NOT AFTER. With ff.customdialogue OFF,
            // DialogueView.Bootstrap never subscribed DialogueService.Opened - so Play() still
            // returns TRUE, sets ActiveVm, makes IsRunning true and suppresses hero input, with no
            // panel on screen and no way to close it. Play's return value CANNOT detect that. This
            // guard is the difference between "no teach" and "a soft-locked dungeon".
            if (!flagOn)
            {
                FlowTrace.Warn("DungeonTeach",
                    $"lantern intro: seen=false played=false key='{LanternIntroShownKey}' - ff.customdialogue is " +
                    "OFF, so no View is subscribed and a Play() would open a VM nothing can render or close. " +
                    "DECLINED, save untouched; the teach is still owed and will play the next entry with the flag on.");
                return;
            }

            if (alreadyRunning)
            {
                FlowTrace.Warn("DungeonTeach",
                    $"lantern intro: seen=false played=false key='{LanternIntroShownKey}' - another dialogue is " +
                    "already open on entry, so the teach stands down rather than stomping it. Save untouched; it " +
                    "is retried on the next dungeon entry.");
                return;
            }

            if (!_introEndedHooked)
            {
                CoreDialogue.DialogueService.EndedWithId += HandleIntroDialogueEnded;
                _introEndedHooked = true;
            }

            bool played = CoreDialogue.DialogueService.Play(LanternIntroDialogueId);
            if (!played)
            {
                CoreDialogue.DialogueService.EndedWithId -= HandleIntroDialogueEnded;
                _introEndedHooked = false;
                FlowTrace.Warn("DungeonTeach",
                    $"lantern intro: seen=false played=false key='{LanternIntroShownKey}' - " +
                    $"Play('{LanternIntroDialogueId}') returned false, i.e. the row is MISSING from " +
                    "dialogues.json (DialogueCatalog.Find). Save untouched.");
                return;
            }

            FlowTrace.Step("DungeonTeach",
                $"lantern intro: seen=false played=true key='{LanternIntroShownKey}' - playing " +
                $"'{LanternIntroDialogueId}' in '{gameObject.scene.name}' with the oil meter already on screen. " +
                "The one-shot latches on the dialogue's END, never on this call.");
        }

        /// <summary>Latches the one-shot ONLY when the intro actually finished rendering.</summary>
        private void HandleIntroDialogueEnded(string dialogueId)
        {
            if (dialogueId != LanternIntroDialogueId) return;
            CoreDialogue.DialogueService.EndedWithId -= HandleIntroDialogueEnded;
            _introEndedHooked = false;
            MarkSeen(LanternIntroShownKey,
                "the lantern intro rendered and closed - it never plays again on this save");
        }

        /// <summary>
        /// The teach's COMPLETION beat: the player has stood in an oil stone and watched the flask
        /// fill. Nothing in the game could observe this before WO-1805 added Lantern.OilStoneUsed.
        /// </summary>
        private void HandleFirstOilStoneRefill()
        {
            var svc = GameStateService.Instance;
            if (HasSeen(svc, LanternIntroCompletedKey)) return;
            MarkSeen(LanternIntroCompletedKey,
                "first oil-stone refill - the lantern teach is COMPLETE (the player did the thing, " +
                "so the intro is owed to nobody)");
        }

        /// <summary>The darkness beat: one line, once per save, on the final-warning edge.</summary>
        private void HandleLanternFinalWarning()
        {
            var svc = GameStateService.Instance;
            if (HasSeen(svc, LanternGutteringShownKey))
            {
                FlowTrace.Step("DungeonTeach",
                    $"lantern guttering: seen=true played=false key='{LanternGutteringShownKey}' - " +
                    "the warning line is one-shot per save and has already been shown.");
                return;
            }

            DeNelle.Core.UI.ElarionUiKit.ShowToast(
                LanternGutteringLine,
                DeNelle.Core.UI.ElarionUiKit.ToastTone.Danger,
                lifeSeconds: 4.5f);
            MarkSeen(LanternGutteringShownKey,
                "the guttering warning has been shown once - the meter carries it from here");
            FlowTrace.Step("DungeonTeach",
                $"lantern guttering: seen=false played=true key='{LanternGutteringShownKey}' - showed " +
                $"'{LanternGutteringLine}' as a non-blocking toast (a modal here would suppress hero input " +
                "while the ambush multiplier arms).");
        }

        private static bool HasSeen(GameStateService svc, string key)
        {
            var state = svc != null ? svc.State : null;
            if (state == null || state.SeenTutorials == null) return false;
            return state.SeenTutorials.TryGetValue(key, out bool seen) && seen;
        }

        /// <summary>
        /// Persists one one-shot key. MarkTutorialSeen writes the key AND Saves in one call (the
        /// TorchWardenInteractable.GrantTorchOnce idiom), and is itself a no-op when already set.
        /// </summary>
        private static void MarkSeen(string key, string why)
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                FlowTrace.Warn("DungeonTeach",
                    $"cannot latch one-shot key '{key}' - no GameStateService/state is live. The beat was " +
                    "delivered but nothing was persisted, so it may repeat on the next entry.");
                return;
            }
            svc.MarkTutorialSeen(key);
            FlowTrace.Step("DungeonTeach", $"one-shot key '{key}' SET and saved: {why}.");
        }

        /// <summary>Ends the run record, if one is still active. Idempotent.</summary>
        public void EndRun()
        {
            if (_state != null && _state.RunActive)
            {
                _state.EndRun();
                FlowTrace.Step(Sys, "run ended (composed exit).");
            }
        }
    }
}
