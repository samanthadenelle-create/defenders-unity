// =============================================================================
// DungeonController — the Healer's Cottage scene orchestrator (Weeks 5-6).
// -----------------------------------------------------------------------------
// Port spec Part 3 row:
//   src/modules/dungeons/ -> _Modules/Dungeons/DungeonController.cs
// Port spec Part 5 Week 5: "scene manager that loads room layout, places hero
// at spawn, manages camera (Cinemachine follow rig, top-down isometric tilt)."
//
// One controller orchestrates the Dungeon_HealersCottage scene:
//   1. Loads the canonical layout JSON (StreamingAssets, via DungeonLayoutLoader).
//   2. Starts a run on the DungeonRuntimeState ScriptableObject.
//   3. Places the Keeper at the layout's spawn point.
//   4. Aims the Cinemachine follow camera at the hero (top-down isometric tilt).
//   5. Tracks which room the hero is in each frame; ticks the encounter clock.
//   6. Hands the layout to the dungeon's interactables (lore stones, checkpoints,
//      encounter triggers, Bryn, lantern) so each wires itself off shared data.
//
// CANON: the town is "Avalon", the world-tree is "Elarion" / "the Heart", the
// dungeon NPC is "Bryn" — all canon names. They are NOT typed inline in
// user-facing copy (port spec Part 4 routes them through canon-strings.json);
// this file uses them only in comments.
//
// All async flows return UniTask — never `async void` (port spec Part 3 mandate).
// =============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using Unity.Cinemachine;
using UnityEngine;

namespace DeNelle.Dungeons
{
    /// <summary>
    /// Orchestrates the Healer's Cottage dungeon scene — loads the room layout,
    /// places the Keeper at spawn, drives the Cinemachine follow camera, and
    /// tracks the hero's current room. The KayKit Dungeon model pack is not yet
    /// imported, so this controller builds the runtime scaffolding (layout load,
    /// run lifecycle, camera, room tracking, interactable wiring) — the final
    /// mesh assembly lands once the pack is staged (port spec Part 7 Week 5).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonController : MonoBehaviour
    {
        [Header("Dungeon identity")]
        [Tooltip("Canonical dungeon id — keys the layout JSON under " +
                 "StreamingAssets/Data/Canonical/dungeons/. v2 foundation ships " +
                 "the Healer's Cottage only.")]
        [SerializeField] private string _dungeonId = "healers-cottage";

        [Header("State")]
        [Tooltip("The runtime-only ScriptableObject holding the active run " +
                 "(current room, checkpoints, lore-stones). Shared with the " +
                 "dungeon interactables.")]
        [SerializeField] private DungeonRuntimeState _runtimeState;

        [Header("Scene actors")]
        [Tooltip("The Keeper's hero rig — moved to the layout's spawn point on load.")]
        [SerializeField] private Transform _hero;

        [Tooltip("The Keeper's dungeon-walk controller — input is held off across " +
                 "the spawn teleport, then enabled once the run is live. Optional: " +
                 "auto-found on the hero rig when left unset.")]
        [SerializeField] private DungeonHero _heroController;
        // F8 2026-07-30: true while a real-time arena fight owns the Keeper's movers —
        // gates the per-frame EnsureSingleDungeonMover so the arena's WarpHero/WarpTo works.
        private bool _arenaOwnsHero;

        [Tooltip("Cinemachine camera that follows the Keeper (top-down isometric tilt).")]
        [SerializeField] private CinemachineCamera _followCamera;

        [Tooltip("Optional isometric camera rig component. When set, it owns the " +
                 "framing maths; when null the controller applies the framing " +
                 "inline from the offset/pitch fields below.")]
        [SerializeField] private DungeonCameraRig _cameraRig;

        [Tooltip("Hero-attached lantern — handed the layout's oil stones on load.")]
        [SerializeField] private Lantern _lantern;

        [Tooltip("Bryn the Wanderer — placed + configured from the layout's bryn block.")]
        [SerializeField] private Bryn _bryn;

        [Header("Crafting (Workstream C)")]
        [Tooltip("The shared crafting-ingredient inventory ScriptableObject — wired " +
                 "to the ingredient pickups + the crafting pedestal on load.")]
        [SerializeField] private DungeonInventory _dungeonInventory;

        [Tooltip("The crafting-pedestal interactable — configured from " +
                 "crafting-recipes.json's pedestal block. Replaces the §5.4 " +
                 "placeholder shard pedestal in the Hidden Vault.")]
        [SerializeField] private CraftingPedestal _craftingPedestal;

        [Tooltip("Parent for the spawned ingredient-pickup motes — one child per " +
                 "crafting-recipes.json ingredientPlacements[] entry, in order.")]
        [SerializeField] private Transform _ingredientRoot;

        [Tooltip("The UI Toolkit crafting panel controller — subscribed to the " +
                 "crafting pedestal's open/close events on load.")]
        [SerializeField] private CraftingPanelController _craftingPanel;

        [Header("Dungeon HUD (Workstream C)")]
        [Tooltip("The dungeon HUD controller — fed the Lantern reference so its " +
                 "oil meter reads the lantern's public API each frame.")]
        [SerializeField] private DungeonHudController _dungeonHud;

        [Header("Interactable parents (wired by the scene builder)")]
        [Tooltip("Parent for the spawned lore-stone interactables.")]
        [SerializeField] private Transform _loreStoneRoot;

        [Tooltip("Parent for the spawned checkpoint shrines.")]
        [SerializeField] private Transform _checkpointRoot;

        [Tooltip("Parent for the spawned encounter triggers.")]
        [SerializeField] private Transform _encounterRoot;

        [Header("Hero vitals — LAST-DITCH fallback only (WO-775)")]
        [Tooltip("Fallback hero HP seeded onto the run state ONLY when neither a live " +
                 "HeroHealth on the hero rig nor HeroHealth.Instance resolves (guarded by " +
                 "a FlowTrace.Warn). The real seed reads the LIVE hero's MaxHp (base + gear " +
                 "+ talents) — see SeedHeroVitalsFromLiveHero.")]
        [SerializeField] private float _fallbackHp = 120f;

        [Tooltip("Fallback hero mana seeded onto the run state ONLY when no live " +
                 "HeroAbilities resolves off the hero rig (guarded by a FlowTrace.Warn).")]
        [SerializeField] private float _fallbackMana = 60f;

        [Header("Camera framing (top-down isometric)")]
        [Tooltip("Camera offset from the hero, world units. Gives the spec's " +
                 "top-down isometric tilt (pulled back + up, looking down). Capped " +
                 "at bind time by CameraMaxHeightAboveHero so the rig sits just over " +
                 "the ~4u room ceiling, not far above it (owner felt-test 2026-07-16).")]
        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 9f, -6.25f);

        [Tooltip("Camera pitch in degrees — the isometric down-tilt.")]
        [SerializeField] private float _cameraPitch = 52f;

        /// <summary>
        /// Hard cap on how far above the hero the inline follow camera may sit
        /// (world units). The rooms carry a ~4u ceiling (DungeonSceneBuilder.
        /// WallHeight); ~9u keeps the rig just over the roofline for a framed
        /// dungeon-iso look. Mirrors DungeonCameraRig._maxHeightAboveHero so both
        /// camera paths behave identically. Owner-tunable via that rig field.
        /// </summary>
        private const float CameraMaxHeightAboveHero = 9f;

        [Header("Audio")]
        [Tooltip("Looping dungeon ambient BGM source — echoes-beneath-elarion.mp3 " +
                 "at the mix-spec volume (port spec Part 5 Week 5).")]
        [SerializeField] private AudioSource _ambientBgm;

        [Tooltip("The dungeon ambient clip (echoes-beneath-elarion). MAY BE NULL " +
                 "until the audio file is imported under Assets/Audio/ — the code " +
                 "path is guarded and logs a warning rather than erroring. The " +
                 "AudioSource's own clip is used as a fallback when this is unset.")]
        [SerializeField] private AudioClip _ambientBgmClip;

        [Tooltip("Dungeon BGM volume — audio-mix-spec.md §2 fixes the 'dungeon' " +
                 "track at 0.25 (very soft, ambient only). Master volume scales " +
                 "this multiplicatively once the MusicDirector lands.")]
        [SerializeField, Range(0f, 1f)] private float _ambientBgmVolume = 0.25f;

        // ── Runtime ──────────────────────────────────────────────────────────

        /// <summary>The loaded layout, or null before <see cref="EnterDungeon"/> completes.</summary>
        public DungeonLayout Layout { get; private set; }

        /// <summary>True once the dungeon has finished loading and the run is live.</summary>
        public bool Ready { get; private set; }

        /// <summary>The runtime run state — current room, checkpoints, lore read.</summary>
        public DungeonRuntimeState RuntimeState => _runtimeState;

        /// <summary>The hero's last-known room id, cached to detect room crossings.</summary>
        private string _lastRoomId = string.Empty;

        /// <summary>
        /// The canonical lore-fragment set (lore-fragments.json) — feeds Bryn's
        /// entrance line + each lore stone's reading text. Null when the file
        /// could not be loaded; the interactables then fall back to inline copy.
        /// </summary>
        private LoreFragmentSet _loreFragments;

        /// <summary>The hydrated scripted/boss encounter triggers, in layout order.</summary>
        private readonly List<EncounterTrigger> _encounterTriggers = new List<EncounterTrigger>();

        /// <summary>
        /// The canonical crafting data set (crafting-recipes.json) — feeds the
        /// ingredient pickups, the crafting pedestal and the crafting UI. Null
        /// when the file could not be loaded; the crafting layer then stays inert.
        /// </summary>
        private CraftingDataSet _craftingData;

        // WO-770.3b: true while subscribed to BattleArena.OnBattleEnded (the real-time settle bridge).
        private bool _arenaSubscribed;

        // Felt-test 2026-07-26 ("cannot move — it slides me around"): the dungeon Keeper ends up with TWO
        // movers — DungeonHero (CharacterController, the intended dungeon mover) AND an injected
        // HeroLocomotion (a NavMeshAgent added by the village HeroBodySwapper during the body swap). No
        // navmesh is baked in the cottage, so the agent runs its OFF-MESH fall-snap, sliding the hero and
        // stomping DungeonHero's CharacterController.Move every frame (input appears dead). We keep
        // DungeonHero the SOLE mover by NEUTRALIZING the injected HeroLocomotion's MOVEMENT while leaving
        // the component ENABLED — disabling it would make FindAnyObjectByType<HeroLocomotion>() (excludes
        // disabled) return null, orphaning the exit prompt AND breaking dungeon enemy targeting (§7). The
        // swap is async, so this is polled from Update until enforced (and re-applied if a later swap
        // re-enables the agent). The two statics it toggles are restored on teardown (RestoreInjectedHeroMover).
        private DeNelle.Village.HeroLocomotion _injectedHeroLoco;
        private UnityEngine.AI.NavMeshAgent _injectedHeroAgent;
        private bool _moverNeutralized;   // true while the injected HeroLocomotion's movement is gated off

        // F8 2026-08-05 (dungeon unplayable from the first encounter): true once the Keeper has been
        // provisioned as a FIGHT-CAPABLE hero (PlayerAttackController + HeroHealth + gear/loadout).
        // Latched so the provisioning runs EXACTLY once per run — see EnsureKeeperFightCapable.
        private bool _keeperFightProvisioned;
        // Time.time at which the run went live (Ready). Bounds the wait for the async body swap so a
        // failed swap degrades to a LOGGED late-provision instead of a permanently unarmed Keeper.
        private float _readySinceTime;
        private const float BodySwapWaitSeconds = 3f;

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Start()
        {
            EnterDungeon().Forget();
        }

        private void OnDestroy()
        {
            // patch 6 (F8 2026-07-30): a teardown reached WITHOUT ExitToVillage — hero death-EVAC
            // (HeroHealth.HandleDeath -> SceneRouter.GoCastle, which never notifies the arena),
            // quit-to-menu, or any other scene route. Abandon a still-live real-time fight FIRST, before
            // this scene's stage/combatants/hero are destroyed under it. Idempotent with the
            // ExitToVillage call (the arena's own _resolved latch + the _arenaOwnsHero gate make the
            // second call a no-op).
            AbandonRealtimeBattle("DungeonController.OnDestroy (scene teardown / EVAC route)");

            // Restore the injected HeroLocomotion's movement + the shared statics FIRST, before ANY of the
            // early-returns below — otherwise the ATB round-trip teardown (HasPendingEncounter) would leave
            // GroundSnapEnabled=false + a zeroed scripted-move on the hero, freezing it in the battle scene.
            RestoreInjectedHeroMover();

            UnsubscribeRealtimeSettle(); // WO-770.3b: drop the arena hook FIRST (survives the early-returns below)

            if (_runtimeState == null || !_runtimeState.RunActive) return;

            // CRITICAL (BUG-008 round-trip): when an encounter battle is pending,
            // this scene is being torn down to route into ATBBattle — NOT a
            // genuine dungeon exit. EndRun() would wipe the encounter handoff +
            // hero vitals the ScriptableObject is carrying across the round-trip,
            // so the dungeon could never resume. Leave the run intact; the
            // resume path on re-entry (or a real ExitToVillage) ends it.
            if (_runtimeState.HasPendingEncounter) return;

            // No battle pending — a real teardown (e.g. quit-to-menu, not the
            // Apothecary exit). WO-749: bank any gathered scatter to the persistent
            // larder before the per-run inventory dies with the scene. (The
            // Apothecary ExitToVillage path already deposited + set RunActive false,
            // so it returns above and never double-deposits here.)
            if (_dungeonInventory != null)
                DungeonLootGrant.DepositDungeonInventory(_dungeonInventory);
            _runtimeState.EndRun();
        }

        /// <summary>
        /// Loads the canonical layout, starts the run, places the Keeper at the
        /// spawn point, aims the follow camera and wires up the interactables.
        /// Returns a <see cref="UniTask"/> — never <c>async void</c>.
        /// </summary>
        public async UniTask EnterDungeon()
        {
            using var _flow = FlowTrace.Enter("Dungeon", $"EnterDungeon id='{_dungeonId}'");
            Ready = false;

            // Resolve the hero's walk controller up-front so input can be held
            // off across the spawn teleport (the Keeper must not drift while the
            // layout loads / the camera snaps into frame).
            if (_heroController == null && _hero != null)
                _heroController = _hero.GetComponent<DungeonHero>();
            if (_heroController != null)
                _heroController.SetInputEnabled(false);

            // F8 2026-07-30: WO-450 canon — the 'Player' tag rides whatever rig embodies the
            // player. Nothing tagged the dungeon Keeper (HeroControlEnsurer skips non-village
            // scenes), so every FindWithTag consumer — BattleArena's warp + out-of-arena
            // self-heal, return-point restore — was blind here (captured "<no Player>", which
            // staged a PHANTOM fight that latched combat). Tag it.
            if (_hero != null && !_hero.gameObject.CompareTag("Player"))
                Guard.Try("Dungeon", "tag Keeper as Player", () => _hero.gameObject.tag = "Player");

            Layout = await DungeonLayoutLoader.LoadAsync(_dungeonId);
            if (Layout == null)
            {
                // HARD STOP: no layout means NO geometry, NO actors — the scene would
                // sit blank with the hero frozen (input was disabled above). Fail loud
                // AND hand input back so the Keeper is never stuck in a dead scene.
                FlowTrace.Fail("Dungeon",
                    $"EnterDungeon: Layout '{_dungeonId}' failed to load — dungeon cannot " +
                    "assemble. Re-enabling hero input so the Keeper is not frozen in a blank scene.");
                if (_heroController != null) _heroController.SetInputEnabled(true);
                return;
            }
            FlowTrace.Step("Dungeon",
                $"EnterDungeon: layout '{Layout.id}' loaded — rooms={Layout.rooms?.Length ?? 0}, " +
                $"loreStones={Layout.loreStones?.Length ?? 0}, checkpoints={Layout.checkpoints?.Length ?? 0}, " +
                $"scriptedEncounters={Layout.scriptedEncounters?.Length ?? 0}, miniBoss={(Layout.miniBoss != null ? "yes" : "no")}.");
            // VERIFY rooms hydrated: a zero-room layout builds no playable space.
            if ((Layout.rooms?.Length ?? 0) == 0)
                FlowTrace.Warn("Dungeon",
                    $"EnterDungeon: layout '{Layout.id}' has ZERO rooms — the dungeon will have no " +
                    "navigable space and room-tracking will never resolve a room.");

            // Load the canonical lore-fragment set — feeds Bryn's entrance line
            // and each lore stone's reading text. A null set is non-fatal: the
            // interactables fall back to the layout JSON's inline copy.
            _loreFragments = await LoreFragmentsLoader.LoadAsync();

            // Load the canonical crafting data (Workstream C) — feeds the
            // ingredient pickups, the crafting pedestal and the crafting UI. A
            // null set is non-fatal: the crafting layer simply stays inert.
            _craftingData = await CraftingDataLoader.LoadAsync();

            // An encounter battle just resolved if the run state still carries a
            // pending handoff — this scene LOAD is the ATB round-trip return.
            bool resuming = _runtimeState != null && _runtimeState.HasPendingEncounter;

            // A fresh run starts with an empty larder; a resume (the ATB
            // round-trip return) keeps whatever ingredients were gathered.
            if (!resuming && _dungeonInventory != null)
                _dungeonInventory.Clear();

            int seed = MakeRunSeed();
            Vector3 spawnPos = resuming
                ? _runtimeState.EncounterResumePosition
                : ResolveSpawnPosition();
            string entryRoomId = Layout.spawn?.roomId ?? Layout.entryRoomId;

            // StartRun deliberately preserves the encounter handoff + hero
            // vitals so they survive this reload (see DungeonRuntimeState).
            if (_runtimeState != null && !resuming)
                _runtimeState.StartRun(Layout.id, entryRoomId, spawnPos, seed);
            else if (_runtimeState != null && !_runtimeState.RunActive)
                // A resume after a process-fresh reload still needs a live run.
                _runtimeState.StartRun(Layout.id, entryRoomId, spawnPos, seed);

            // Seed the hero vitals on a fresh run so the checkpoint heal +
            // the ATB round-trip have numbers (Week-6 checklist item 7). On a
            // resume the vitals already rode the round-trip on the run state, so
            // they are left untouched. WO-775: the seed now reads the LIVE hero
            // rig (base + gear + talents), not the retired 120/60 placeholder literals.
            if (_runtimeState != null && !resuming && !_runtimeState.HasHeroVitals)
                SeedHeroVitalsFromLiveHero();

            PlaceHero(spawnPos);
            ConfigureCamera();
            ConfigureLantern();
            ConfigureBryn();
            DressEntranceNpc();
            HydrateLoreStones();
            HydrateCheckpoints();
            HydrateEncounters();
            ConfigureCrafting();
            HydrateChests();
            HydrateExits();
            ConfigureDungeonHud();
            DressTraversalLinks();
            SweepPlaceholderCubes();
            StartAmbientAudio();
            SubscribeRealtimeSettle(); // WO-770.3b: hook BattleArena so a real-time fight settles the dungeon

            // Settle any in-flight ATB encounter — the dungeon module's side of
            // the BUG-008 round-trip. The matching EncounterTrigger marks itself
            // fired + (on a boss victory) flags the boss defeated.
            if (resuming)
                ResolvePendingEncounter();

            DungeonRoom currentRoom = Layout.RoomAt(spawnPos);
            _lastRoomId = currentRoom?.id ?? entryRoomId;
            // RENDER-COMMIT: the scene is assembled + the Keeper framed. Ready flips the
            // per-frame loop on, so it self-reports the commit (and which room the hero
            // resolved into) — a blank/frozen run is then diagnosable from the trace.
            FlowTrace.Step("Dungeon",
                $"EnterDungeon: run live (Ready=true) — spawn={spawnPos}, entryRoom='{_lastRoomId}', " +
                $"resuming={resuming}.");
            _readySinceTime = Time.time;   // F8 2026-08-05: bounds the body-swap wait in EnsureKeeperFightCapable
            Ready = true;

            // The run is live and the Keeper is framed — hand movement back.
            if (_heroController != null)
                _heroController.SetInputEnabled(true);
        }

        /// <summary>
        /// WO-775: seed the run's hero vitals from the LIVE hero rig — HP from
        /// <see cref="DeNelle.Village.HeroHealth"/> (base + gear + talents), mana from
        /// <see cref="DeNelle.Village.HeroAbilities"/> — instead of the retired hardcoded
        /// 120/60 placeholder. Resolved off <see cref="_hero"/> with <c>TryGetComponent</c>
        /// (NOT <c>GetComponent&lt;T&gt;() ??</c> — the NoNullCoalesceOnGetComponent lint
        /// fails the gate on that), with <see cref="DeNelle.Village.HeroHealth.Instance"/>
        /// as the fallback when the component is not yet on the rig (mid body-swap). The
        /// <c>_fallbackHp</c> / <c>_fallbackMana</c> literals survive ONLY as a guarded
        /// last-ditch fallback — each behind a <see cref="FlowTrace.Warn"/> — when neither
        /// live source resolves. Extracted from the fresh-run seed gate so the EditMode
        /// test can prove the seed reads real hero stats, not the placeholders.
        /// </summary>
        internal void SeedHeroVitalsFromLiveHero()
        {
            if (_runtimeState == null) return;

            DeNelle.Village.HeroHealth heroHealth = null;
            DeNelle.Village.HeroAbilities heroAbilities = null;
            if (_hero != null)
            {
                if (_hero.TryGetComponent<DeNelle.Village.HeroHealth>(out var hh)) heroHealth = hh;
                if (_hero.TryGetComponent<DeNelle.Village.HeroAbilities>(out var ha)) heroAbilities = ha;
            }
            if (heroHealth == null) heroHealth = DeNelle.Village.HeroHealth.Instance;

            float seedHp, seedMaxHp, seedMana, seedMaxMana;

            if (heroHealth != null)
            {
                seedHp = heroHealth.Hp;
                seedMaxHp = heroHealth.MaxHp;
            }
            else
            {
                FlowTrace.Warn("Dungeon",
                    $"SeedHeroVitalsFromLiveHero: no live HeroHealth on " +
                    $"'{(_hero != null ? _hero.name : "null")}' nor HeroHealth.Instance — " +
                    $"falling back to the {_fallbackHp:F0} HP placeholder.");
                seedHp = _fallbackHp;
                seedMaxHp = _fallbackHp;
            }

            if (heroAbilities != null)
            {
                seedMana = heroAbilities.Mana;
                seedMaxMana = heroAbilities.MaxMana;
            }
            else
            {
                FlowTrace.Warn("Dungeon",
                    $"SeedHeroVitalsFromLiveHero: no live HeroAbilities on " +
                    $"'{(_hero != null ? _hero.name : "null")}' — falling back to the " +
                    $"{_fallbackMana:F0} mana placeholder.");
                seedMana = _fallbackMana;
                seedMaxMana = _fallbackMana;
            }

            _runtimeState.SetHeroVitals(seedHp, seedMaxHp, seedMana, seedMaxMana);
            FlowTrace.Step("Dungeon",
                $"SeedHeroVitalsFromLiveHero: seeded from live rig — HP {seedHp:F0}/{seedMaxHp:F0}, " +
                $"mana {seedMana:F0}/{seedMaxMana:F0}.");
        }

        /// <summary>
        /// Exits the dungeon — tears the run down and routes back to the village.
        /// Called by the Apothecary back-door once the mini-boss is defeated.
        /// </summary>
        public UniTask ExitToVillage()
        {
            // patch 6 (F8 2026-07-30): if a real-time arena fight is still live, abandon it BEFORE the
            // scene load destroys its stage + combatants out from under it — otherwise it resolves as a
            // phantom WIN (unearned loot + a return warp into the scene we are loading). No-op otherwise.
            AbandonRealtimeBattle("ExitToVillage");

            // Hand the hero's movement back before we leave — re-enable the injected HeroLocomotion's agent
            // and restore the shared GroundSnap/scripted-move statics so the village hero is never left frozen.
            RestoreInjectedHeroMover();

            // WO-1041 / WO-1042 — THE DUNGEON'S EXCLUSIVE PAYOUT, granted BEFORE EndRun() wipes the
            // run record. This is the one link WO-1041 §2 measured as missing: everything downstream
            // (the rough stone item, the polish job, jeweler-recipes.json, the ring chain, the equip
            // and stat pipeline) was already built and had no source.
            GrantRunPayout(_runtimeState, "DungeonController.ExitToVillage");

            if (_runtimeState != null && _runtimeState.RunActive)
                _runtimeState.EndRun();
            // The crafting inventory is per-run — bank the gathered scatter to the
            // PERSISTENT larder (WO-749 gap 2 bridge) BEFORE clearing it, so delving
            // actually stocks the village crafting supply. Then clear so a stale run's
            // ingredients never leak into the next dungeon run.
            if (_dungeonInventory != null)
            {
                DungeonLootGrant.DepositDungeonInventory(_dungeonInventory);
                _dungeonInventory.Clear();
            }
            StopAmbientAudio();
            // Owner ruling 2026-07-13 ("map the dungeon in"): the exit goes HOME — the
            // merged overworld hub (SceneRouter.Castle -> Main_Castle_Overworld), not the
            // ABANDONED legacy Village scene this predated (canon: Village.unity retired).
            return SceneRouter.LoadSceneWithFade(SceneRouter.Castle);
        }

        // ── WO-1041/1042: the run payout ─────────────────────────────────────

        /// <summary>
        /// Pay a COMPLETED run its rough stone, and record the grade that will shape the polish.
        /// <para>
        /// ⛔ EVERY COMPLETED RUN PAYS — the stone is GUARANTEED, not a chance (WO-1041 §3, WO-1040
        /// §3b trap 3). A no-deaths or boss-only gate would mean the median player never once sees
        /// the reward that justifies the dungeon, and would lock out precisely the players who are
        /// dying and most need the power. Mastery moves the ODDS of what the stone becomes; it is
        /// never the only door.
        /// </para>
        /// <para>
        /// ⚠ "COMPLETED" IS NOT "ENTERED". A player who walks in and straight back out has not run a
        /// dungeon, and paying that would make the stone a free tap-farm rather than a reward for
        /// risk. The bar is deliberately LOW — one encounter, one chest, or the boss — so it excludes
        /// only the no-op round trip, never a real if unsuccessful delve.
        /// </para>
        /// <para>
        /// ⚠ WO-1112 — THIS IS THE PROJECT'S ONE PAYOUT AUTHORITY, WHICH IS WHY IT IS STATIC.
        /// It used to be a private instance method reachable only from
        /// <see cref="ExitToVillage"/>, i.e. only from the COTTAGE pipeline — and
        /// DungeonController is in no dg_* scene at all. So a cleared COMPOSED dungeon paid
        /// nothing, and because <c>DungeonRunPayout.LastPolishScore</c> is written nowhere else,
        /// JewelPolishService scored EVERY composed run 0: the whole rough-stone economy was
        /// inert in exactly the dungeons that get played. Taking the run state as a PARAMETER
        /// (rather than reading the instance field) is what lets the composed exit reach the
        /// same authority. DO NOT copy this body anywhere — a second payout site would be a
        /// duplicate-authority bug, and four of those surfaced in one day.
        /// </para>
        /// </summary>
        /// <param name="st">The run to judge and pay. Null is handled and traced, never thrown.</param>
        /// <param name="via">Call-site label for the trace, so a capture says WHICH exit paid.</param>
        public static void GrantRunPayout(DungeonRuntimeState st, string via)
        {
            DeNelle.Core.Diagnostics.Guard.Try("JewelPolish", $"grant dungeon run payout ({via})", () =>
            {
                if (st == null)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("JewelPolish",
                        $"run payout skipped ({via}) - no DungeonRuntimeState to judge completion from.");
                    return;
                }

                int chests = st.ChestsOpened != null ? st.ChestsOpened.Count : 0;
                int secrets = st.SecretRoomsFound != null ? st.SecretRoomsFound.Count : 0;
                bool engaged = st.BossDefeated || st.RandomEncounterCount > 0 || chests > 0 || secrets > 0;
                if (!engaged)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Step("JewelPolish",
                        $"run payout withheld ({via}) - the player entered and left without an encounter, chest " +
                        "or secret. Not a completed run; not a bug.");
                    return;
                }

                // ONE RUBRIC (WO-1041 §3 / WO-1042 §5(2)): the grade comes from DungeonRunGrade and
                // nowhere else. ⚠ WO-1040 is NOT implemented, so the tree carries no kill/death/potion/
                // elapsed record yet; the fields below are what genuinely exists today and the rest
                // stay at their defaults. When WO-1040 lands it fills DungeonRunStats at the source
                // and THIS call site does not change.
                var stats = new DeNelle.Core.Catalog.DungeonRunStats
                {
                    BossDefeated = st.BossDefeated,
                    EnemiesKilled = st.RandomEncounterCount,
                    DeepestFloor = chests + secrets,     // provisional depth proxy - WO-1040 replaces
                };
                int score = DeNelle.Core.Catalog.DungeonRunGrade.PolishScore(stats);

                var inv = DeNelle.Village.Crafting.VillageInventory.Instance;
                if (inv == null)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("JewelPolish",
                        $"run payout LOST ({via}) - no VillageInventory to bank the rough stone into.");
                    return;
                }

                if (!st.TryClaimReward())
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Step("JewelPolish",
                        $"run payout skipped ({via}) - this run already evaluated its one reward.");
                    return;
                }

                string stoneId = DeNelle.Core.Catalog.DungeonExclusiveItems.RoughStoneId;

                // WO-1373 — STARTER DUNGEONS DO NOT PAY ROUGH STONE. Owner ruling 2026-09-09,
                // verbatim: "5% drop rate in dungeons not included the starter dungeons".
                //
                // HOW A STARTER DUNGEON IS IDENTIFIED IN DATA, quoted at source 2026-09-09: the
                // authored layout carries a "tier" field —
                // Assets/Resources/Data/Canonical/dungeon-layouts/<dungeonId>.json, e.g.
                // dg_starter_loop / dg_healers_cottage / dg_folks_granary / dg_hollow_roads all
                // read "tier": 1, while dg_sunken_vault is 2, dg_bonecrypt 3 and dg_ember_deep 4.
                // Tier 1 IS the starter band; there is no separate "starter" flag anywhere in
                // the dungeon data (checked across dungeon-graphs/, dungeon-layouts/,
                // dungeon-balance.json and dungeon-kit.json).
                //
                // ⛔ THE GATE COVERS THE GUARANTEED FIRST STONE TOO, and that is a real
                // consequence, stated rather than buried: the one-time introduction now waits
                // for the player's first tier-2+ delve instead of landing in dg_starter_loop.
                // The alternative reading — gate only the 5% ROLL and let a starter dungeon
                // still pay the introduction — would leave starter dungeons dropping stone,
                // which is the thing "not included" most plainly refuses. JewelerProgression
                // hangs off that first acquisition, so this MOVES when the Jeweler is revealed.
                // It is the top open item in the WO-1373 RESULT.
                int dungeonTier = ResolveDungeonTier(st.DungeonId);
                if (dungeonTier > 0 && dungeonTier < RoughStoneMinDungeonTier)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Step("JewelPolish",
                        $"run payout ({via}): '{st.DungeonId}' is a STARTER dungeon (layout tier " +
                        $"{dungeonTier}, below the minimum {RoughStoneMinDungeonTier}) - no rough " +
                        "stone, per the owner ruling 2026-09-09. Everything else this run earned " +
                        "is unaffected.");
                    return;
                }

                bool firstDungeonStone = !inv.HasEverAcquired(stoneId);
                if (!firstDungeonStone && !ShouldAwardPostFirstStone(UnityEngine.Random.value))
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Step("JewelPolish",
                        $"run payout ({via}): first rough-stone introduction already earned; " +
                        $"post-first roll missed the {PostFirstRoughStoneDropRate:P0} drop rate.");
                    return;
                }
                // WO-1373: the bank + grade + announce is now BankRoughStone, the ONE writer of
                // DungeonRunPayout.LastPolishScore in the whole project. This call site did not
                // move; only the three statements it used to inline did, so the raid settle can
                // reach the SAME authority instead of copying it (WO-1112, [exit-pays]).
                BankRoughStone(score, via,
                    $"boss={st.BossDefeated}, encounters={st.RandomEncounterCount}, " +
                    $"chests={chests}, secrets={secrets}");
            });
        }

        // =====================================================================
        //  WO-1373 - THE ONE PLACE A ROUGH STONE IS BANKED AND GRADED.
        // =====================================================================
        //
        // ⛔ EXACTLY ONE SITE UNDER Assets/_Modules MAY WRITE
        // DungeonRunPayout.LastPolishScore, and this is it. ComposedDungeonRunRegression
        // case [exit-pays] scans every module .cs and FAILS on a second writer, with the
        // WO-1112 reason in its own failure text: "the payout was DUPLICATED rather than
        // shared. Two payout authorities drift, then double-pay or disagree on the grade."
        //
        // ⚠ IT IS REACHED FROM DeNelle.Village BY INVERSION, NOT BY A DIRECT CALL, and
        // that is forced rather than chosen: DeNelle.Dungeons references DeNelle.Village,
        // so DeNelle.Village cannot reference DeNelle.Dungeons without a cycle. The raid
        // settle therefore calls DeNelle.Core.Catalog.DungeonRunPayout.GrantRoughStone,
        // whose delegate is installed below - the same CoreServices-shaped inversion
        // CLAUDE.md section 5 mandates for every other cross-module call.
        //
        // ⚠ THE CALLER OWNS THE GATE, THIS OWNS THE GRANT. Eligibility is deliberately NOT
        // decided here: the dungeon's engagement / claim / starter-tier / drop-roll gates
        // live in GrantRunPayout, and the raid's tier + per-UTC-day gates live in
        // RaidScoring. Two producers were the bug; two POLICIES over one producer is the
        // point of the seam.

        /// <summary>
        /// Bank one rough stone, record its polish grade, and announce it. Returns TRUE only
        /// when a stone actually reached the larder - a caller that spends a daily cap must
        /// stamp its ledger on TRUE and leave it unspent otherwise.
        /// </summary>
        /// <param name="score">The grade carried to the bench. Clamped 0..<see cref="DeNelle.Core.Catalog.DungeonRunGrade.MaxStars"/>.</param>
        /// <param name="via">Call-site label for the trace, so a capture says WHAT earned it.</param>
        /// <param name="detail">Free-text context for the trace (run stats, camp id). May be null.</param>
        public static bool BankRoughStone(int score, string via, string detail)
        {
            var inv = DeNelle.Village.Crafting.VillageInventory.Instance;
            if (inv == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("JewelPolish",
                    $"rough stone LOST ({via}) - no VillageInventory to bank it into. Nothing was " +
                    "granted and no grade was recorded, so a caller holding a daily cap must NOT " +
                    "stamp it.");
                return false;
            }

            string stoneId = DeNelle.Core.Catalog.DungeonExclusiveItems.RoughStoneId;
            bool firstDungeonStone = !inv.HasEverAcquired(stoneId);
            score = Mathf.Clamp(score, 0, DeNelle.Core.Catalog.DungeonRunGrade.MaxStars);

            // Only the reward authority may stamp this as EARNED. Shop/dev/plain
            // inventory Add calls deliberately cannot reveal the Jeweler.
            inv.AddEarned(stoneId, 1);
            DungeonRunPayout.LastPolishScore = score;

            DeNelle.Core.Diagnostics.FlowTrace.Step("JewelPolish",
                $"rough stone GRANTED ({via}): 1x '{stoneId}' (polish score {score}; " +
                $"{detail ?? "no detail"}; firstEverAcquired={firstDungeonStone} - that flag is " +
                "what reveals the Jeweler). Take it to the bench; the outcome is rolled there.");

            // WO-1596: TELL SOMEBODY. The grant above is still the ONE producer - this raises
            // no reward and changes no state; it only announces that one was paid, so a
            // presentation layer can put a full-screen moment in front of it. The listeners
            // run in their OWN Guard on purpose: a throwing subscriber must not be reported
            // as "grant dungeon run payout FAILED" when the stone is already banked.
            RaiseRoughStoneGranted(stoneId, score, firstDungeonStone);
            return true;
        }

        /// <summary>
        /// Install <see cref="BankRoughStone"/> as the project-wide grant authority (WO-1373).
        /// <para>⛔ Runs at BOOT, not on a dungeon scene load, and that is required: the raid
        /// settle earns a stone in a <c>RaidBase_*</c> scene where no <c>DungeonController</c>
        /// instance exists. The authority is static, so no instance is needed - only this
        /// assignment. Idempotent by assignment.</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallRoughStoneGrantAuthority()
        {
            DungeonRunPayout.RoughStoneGrantAuthority =
                (score, via) => BankRoughStone(score, via, null);
            DeNelle.Core.Diagnostics.FlowTrace.Step("JewelPolish",
                "rough stone grant authority INSTALLED (DungeonController.BankRoughStone). Every " +
                "module that earns a stone routes through this one writer of LastPolishScore.");
        }

        /// <summary>
        /// Raised immediately AFTER a rough stone has been banked and its polish score recorded -
        /// never before, and never on a withheld/missed payout (WO-1596).
        /// <para>Arguments: <c>(stoneId, polishScore, firstEver)</c>. <c>firstEver</c> is the flag
        /// measured BEFORE the add, so it is true exactly once per player - the guaranteed
        /// introduction the Jeweler and the Rings of Power hang off.</para>
        /// <para>⛔ SUBSCRIBERS ARE PRESENTATION ONLY. This is an announcement, not a seam for a
        /// second payout: a listener that grants anything re-creates the duplicate-authority bug
        /// WO-1112 spent a day undoing. Subscribe, render, unsubscribe.</para>
        /// </summary>
        public static event System.Action<string, int, bool> RoughStoneGranted;

        private static void RaiseRoughStoneGranted(string stoneId, int score, bool firstEver)
        {
            var handler = RoughStoneGranted;
            if (handler == null) return;
            DeNelle.Core.Diagnostics.Guard.Try("JewelPolish", "rough stone granted listeners",
                () => handler(stoneId, score, firstEver));
        }

        /// <summary>
        /// Owner tuning for subsequent completed eligible dungeons. The first dungeon-earned
        /// stone bypasses this roll and remains guaranteed exactly once (on an ELIGIBLE dungeon
        /// - see the starter-tier gate in <see cref="GrantRunPayout"/>).
        ///
        /// <para>⚠ WO-1373, 2026-09-09: this was a compiled <c>const float 0.15f</c> and the
        /// owner ruled 5%. It is now a rail-backed PERCENT
        /// (<c>dungeon.roughStoneDropPct</c>, shipping default 5) read through
        /// <c>SpecFor</c> first, so an unregistered key answers the shipping default rather than
        /// <c>Int</c>'s 0-for-unknown - which here would mean "no dungeon ever pays a stone
        /// again". A row of 15 restores the previous rate exactly. Clamped 0..100 before the
        /// divide, so a console typo cannot make the roll certain or negative.</para>
        ///
        /// <para>⛔ The NAME is unchanged on purpose: two oracles read it
        /// (<c>RoughStoneFanfareRegression</c> asserts it stays inside (0,1);
        /// <c>JewelerDiscoveryFtueRegression</c> pinned the literal <c>0.15f</c> and needs the
        /// ruling-driven re-point named in the WO-1373 RESULT). Renaming it would turn one
        /// stale pin into two.</para>
        /// </summary>
        public static float PostFirstRoughStoneDropRate => PostFirstRoughStoneDropPct / 100f;

        /// <summary>
        /// The rail-backed PERCENT behind <see cref="PostFirstRoughStoneDropRate"/>. Separate
        /// because the rail carries integers only and this is the one place the percent becomes
        /// a fraction. Rail: <c>dungeon.roughStoneDropPct</c>, shipping default 5.
        /// </summary>
        public static int PostFirstRoughStoneDropPct
        {
            get
            {
                var spec = DeNelle.Core.Ops.RemoteTunables.SpecFor(
                    DeNelle.Core.Ops.RemoteTunables.KeyDungeonRoughStoneDropPct);
                if (spec == null)
                    return DeNelle.Core.Ops.RemoteTunables.DungeonRoughStoneDropPctDefault;
                return Mathf.Clamp(DeNelle.Core.Ops.RemoteTunables.Int(
                    DeNelle.Core.Ops.RemoteTunables.KeyDungeonRoughStoneDropPct), 0, 100);
            }
        }

        public static bool ShouldAwardPostFirstStone(float roll01)
            => roll01 >= 0f && roll01 < PostFirstRoughStoneDropRate;

        /// <summary>
        /// WO-1373 — the lowest authored layout <c>tier</c> that may pay a rough stone. Tier 1
        /// is the starter band (dg_starter_loop, dg_healers_cottage, dg_folks_granary,
        /// dg_hollow_roads all read <c>"tier": 1</c>), so 2 is "anything past the starters".
        ///
        /// <para>⚠ THIS IS A CONST, NOT A ROW, AND THAT IS AN OPEN ITEM. The lane that landed
        /// WO-1373 registered the three knobs the work order enumerated
        /// (<c>raid.roughStoneMinTier</c>, <c>raid.roughStonePerDayCap</c>,
        /// <c>dungeon.roughStoneDropPct</c>); section 6's standing rule says every number is a
        /// row, and this one is not yet. It is named in the RESULT rather than smuggled in as a
        /// fourth unrequested knob.</para>
        /// </summary>
        public const int RoughStoneMinDungeonTier = 2;

        /// <summary>
        /// WO-1373 — the authored <c>tier</c> of <paramref name="dungeonId"/>, read from
        /// <c>Resources/Data/Canonical/dungeon-layouts/&lt;id&gt;.json</c>. Returns 0 when the
        /// layout cannot be found or parsed.
        ///
        /// <para>⛔ 0 IS "UNKNOWN", AND THE CALLER FAILS OPEN ON IT — deliberately, and stated
        /// so nobody has to derive it. A dungeon whose layout is missing from Resources still
        /// pays, because the alternative is a silent, permanent loss of the only material the
        /// Jeweler chain consumes on every dungeon whose layout has not been mirrored. The
        /// tier gate exists to hold BACK the starters, which are the ids most certain to have a
        /// layout on disk; failing closed here would punish content for a packaging accident.
        /// The Warn names it either way, so a capture can tell "starter, refused" from "unknown,
        /// allowed".</para>
        ///
        /// <para>⚠ <c>DungeonRoomBinder.LoadTier</c> is the SAME read against the SAME path, and
        /// it is private in a file this lane does not own. That is duplicated state and it is
        /// flagged in the RESULT for consolidation onto this method rather than left unremarked.</para>
        /// </summary>
        public static int ResolveDungeonTier(string dungeonId)
        {
            if (string.IsNullOrEmpty(dungeonId)) return 0;
            return DeNelle.Core.Diagnostics.Guard.Try("JewelPolish",
                $"resolve layout tier for '{dungeonId}'",
                () =>
                {
                    var text = Resources.Load<TextAsset>("Data/Canonical/dungeon-layouts/" + dungeonId);
                    if (text == null)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("JewelPolish",
                            $"no layout at Data/Canonical/dungeon-layouts/{dungeonId} - tier UNKNOWN. " +
                            "The rough-stone starter gate fails OPEN on an unknown tier, so this run " +
                            "is still eligible; it is not being silently refused.");
                        return 0;
                    }
                    var layout = Newtonsoft.Json.JsonConvert
                        .DeserializeObject<DeNelle.Dungeons.RoomForge.DungeonComposeLayout>(text.text);
                    return layout != null ? Mathf.Max(0, layout.tier) : 0;
                }, 0);
        }

        // ── Per-frame: room tracking + encounter clock ───────────────────────

        private void Update()
        {
            if (!Ready || _runtimeState == null || _hero == null) return;

            // F8 2026-08-05: provision the Keeper as a FIGHT-CAPABLE hero once the body swap has
            // landed. Once-only + self-gated; see EnsureKeeperFightCapable for the captured proof.
            EnsureKeeperFightCapable();

            // Felt-fix: keep DungeonHero the sole mover (kill the injected village NavMeshAgent locomotion
            // that slides the hero on the un-navmeshed cottage floor). Polled — the body swap is async.
            if (!_arenaOwnsHero) EnsureSingleDungeonMover();   // F8 2026-07-30: arena owns the hero mid-fight

            // Push the hero's live position into the run state — drives the
            // encounter engine and the proximity checks on every interactable.
            Vector3 heroPos = _hero.position;
            _runtimeState.SetHeroPosition(heroPos);

            // Detect a room crossing — the room whose footprint contains the
            // hero. Secret-room footprints are checked too, so brushing through
            // an illusory wall registers the discovery.
            DungeonRoom room = Layout.RoomAt(heroPos);
            if (room != null && room.id != _lastRoomId)
            {
                _lastRoomId = room.id;
                _runtimeState.SetCurrentRoom(room.id);
                if (room.secret) _runtimeState.MarkSecretRoomFound(room.id);
            }

            // WO-770.1: reveal the boss-gated back-door exit the instant the mini-boss falls
            // (it is spawned hidden at the Workshop). The always-open normal exit is separate.
            if (_bossBackDoor != null && !_bossBackDoor.gameObject.activeSelf && _runtimeState.BossDefeated)
            {
                _bossBackDoor.gameObject.SetActive(true);
                FlowTrace.Step("Dungeon", "WO-770.1: boss back-door exit revealed (BossDefeated).");
            }

            // Advance the encounter cooldown clock (no-op while in combat). v1
            // gates random encounters off (Layout.disableRandomEncounters) — the
            // clock still ticks so v1.1 can flip them on without a code change.
            _runtimeState.TickEncounterClock(Time.deltaTime);
        }

        // ── Hero placement ───────────────────────────────────────────────────

        // WO-770.1: the boss-gated back-door exit, spawned HIDDEN and revealed by Update once
        // the mini-boss falls (BossDefeated). Null when the layout has no "workshop" room.
        private DungeonExitInteractable _bossBackDoor;

        /// <summary>
        /// WO-770.1 (fixes the roach-motel D-finding): inject the dungeon's RETURN exits at
        /// runtime (no scene rebake). The rich Healer's Cottage previously only left via the
        /// post-boss Apothecary back-door — a hero who could not (or would not) beat the mini-boss
        /// was trapped. Two exits close that:
        ///   • NORMAL — always open, at the ENTRY room centre, so you can leave any time.
        ///   • BOSS BACK-DOOR — at the Workshop, spawned hidden, revealed once the mini-boss falls.
        /// Both mirror <see cref="DungeonExitInteractable"/>'s walk-in + Interact-button pattern and
        /// route through <see cref="ExitToVillage"/> (banks the run's crafting scatter, ends the run).
        /// Positions come straight off the layout's room bounds — no invented coordinates.
        /// </summary>
        private void HydrateExits()
        {
            if (Layout == null) { FlowTrace.Warn("Dungeon", "HydrateExits: no Layout — no exits placed."); return; }

            // NORMAL exit — the entry room the hero spawned into (spawn.roomId, else entryRoomId).
            DungeonRoom entry = Layout.FindRoom(Layout.spawn?.roomId ?? Layout.entryRoomId);
            if (entry?.bounds != null)
            {
                // F8 2026-08-05 (owner felt-test, Seeker) — "two big flat green bars fill the view".
                // The arch USED to seat at entry.bounds.Center, which is the very point the hero is
                // dropped on (ResolveSpawnPosition falls back to that same centre, and this layout's
                // explicit spawn sits on it). Captured, same run:
                //     EnterDungeon: run live ... spawn=(-28.00, 0.00, 0.00), entryRoom='garden-approach'
                //     HydrateExits: NORMAL exit at entry room 'garden-approach' centre (-28.00, 1.76, 0.00)
                // Identical XZ — so the hero materialised INSIDE the archway with a 0.35 x 2.6 x 0.35
                // emerald pillar 1.1m to either side (DungeonExitInteractable.BuildVisual's Pillar_L /
                // Pillar_R, flat URP/Unlit sRGB(51,140,77)); at point-blank range each pillar reads as a
                // hard-edged green bar, not as geometry. It also parked the hero inside the arch's 2.0m
                // walk-in TriggerRadius, leaving only the _armed latch between run-start and an instant
                // self-exit. Seating the arch OFF the spawn removes that dependency (the latch stays as
                // defence in depth). Same spirit as the compose path's basePos + (0,0,-2.6) nudge off
                // the hero seat (DungeonExitInteractable.ResolveExitPosition), generalised to the room's
                // long axis so it works for any layout.
                Vector3 spawnPos = ResolveSpawnPosition();
                Vector3 pos = SeatExitOnFloor(OffsetExitFromSpawn(entry.bounds, spawnPos));
                var normalExit = DungeonExitInteractable.Spawn(pos, () => ExitToVillage().Forget(), "Leave Dungeon");
                normalExit.SetHero(_hero);   // push the rig so the prompt is independent of HeroLocomotion's enabled state

                // Planar separation only — SeatExitOnFloor moves y onto the floor, which is not a
                // clearance we care about. This line is what the next capture reads to PROVE the fix.
                float clearance = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(spawnPos.x, spawnPos.z));
                FlowTrace.Step("Dungeon", $"HydrateExits: NORMAL exit in entry room '{entry.id}' at {pos} " +
                                          $"(spawn {spawnPos}, clearance {clearance:0.00}m).");
                if (clearance < ExitSpawnClearance)
                {
                    // Never silent: a room too small to clear the arch must be a logged line, so a future
                    // layout edit cannot quietly re-create the spawn-inside-the-archway collision.
                    FlowTrace.Warn("Dungeon", $"HydrateExits: NORMAL exit is only {clearance:0.00}m from the spawn " +
                                              $"(want >= {ExitSpawnClearance:0.#}m) — room '{entry.id}' is too small to " +
                                              $"clear the arch. exit={pos} spawn={spawnPos}; the hero may spawn inside " +
                                              "the archway (green pillars in frame) and relies on the _armed latch to " +
                                              "avoid an instant self-exit.");
                }
            }
            else
            {
                FlowTrace.Warn("Dungeon", "HydrateExits: entry room has no bounds — NORMAL exit NOT placed (run could be un-leavable!).");
            }

            // BOSS BACK-DOOR — the Workshop room (present in the Healer's Cottage layout). Spawned
            // hidden; Update reveals it on BossDefeated. Absent room = another dungeon id: skip it,
            // the normal exit still frees the run.
            DungeonRoom workshop = Layout.FindRoom("workshop");
            if (workshop?.bounds != null)
            {
                Vector3 pos = SeatExitOnFloor(workshop.bounds.Center);
                _bossBackDoor = DungeonExitInteractable.Spawn(pos, () => ExitToVillage().Forget(), "Secret Exit");
                _bossBackDoor.SetHero(_hero);   // push the rig so the prompt is independent of HeroLocomotion's enabled state
                bool alreadyBeaten = _runtimeState != null && _runtimeState.BossDefeated;
                _bossBackDoor.gameObject.SetActive(alreadyBeaten);
                FlowTrace.Step("Dungeon", $"HydrateExits: BOSS back-door at 'workshop' centre {pos} (active={alreadyBeaten}).");
            }
            else
            {
                FlowTrace.Step("Dungeon", "HydrateExits: no 'workshop' room in this layout — no boss back-door (normal exit still frees the run).");
            }
        }

        /// <summary>
        /// Minimum planar separation we want between the hero's spawn and the exit arch — enough
        /// that the hero starts clear of both pillars AND outside the arch's 2.0m walk-in
        /// TriggerRadius (DungeonExitInteractable.TriggerRadius).
        /// </summary>
        private const float ExitSpawnClearance = 3f;

        /// <summary>
        /// How far we actually step the arch off the spawn. Deliberately ABOVE
        /// <see cref="ExitSpawnClearance"/>: the healers-cottage entry room ('garden-approach',
        /// bounds min(-36,-8) max(-20,8), spawn (-28,0,0)) would otherwise land at exactly 3.000m
        /// and sit on the warn threshold, so any layout tweak or float wobble would flip the log
        /// line. 4m keeps a real margin and still clamps comfortably inside that 16x16 room.
        /// </summary>
        private const float ExitSpawnStep = 4f;

        /// <summary>
        /// How far the arch must stay off a room wall. Its pillars sit at ±1.1m with a 0.35m
        /// footprint and the lintel spans 2.7m (DungeonExitInteractable.BuildVisual), so the true
        /// half-width is 1.35m — 1.45m here for margin. Used as the inset when clamping into bounds.
        /// </summary>
        private const float ExitBoundsInset = 1.45f;

        /// <summary>
        /// F8 2026-08-05: place the NORMAL exit arch <see cref="ExitSpawnStep"/> off the hero's
        /// spawn instead of on top of it, along the room's LONG axis (whichever of the footprint's X
        /// or Z extent is larger — the long axis is the one with room to step into), pushing toward
        /// whichever end of that axis has more space left from the spawn. The result is CLAMPED into
        /// <paramref name="bounds"/> with <see cref="ExitBoundsInset"/> so the arch can never land in
        /// or past a wall; a room too narrow to hold the inset collapses to its centre line rather
        /// than inverting the clamp. The cross axis stays on the room's centre line (clamped the same
        /// way) so the arch never hugs a side wall. Returns a floor-plane point — the caller still
        /// passes it through <see cref="SeatExitOnFloor"/>, which is unchanged.
        /// </summary>
        private static Vector3 OffsetExitFromSpawn(DungeonBounds bounds, Vector3 spawn)
        {
            Vector3 centre = bounds.Center;   // min/max are structs (DungeonPointXZ) — never null

            // Bounds are authored min/max but treat them as unordered — a swapped pair would
            // otherwise invert every clamp below and silently push the arch outside the room.
            float minX = Mathf.Min(bounds.min.x, bounds.max.x), maxX = Mathf.Max(bounds.min.x, bounds.max.x);
            float minZ = Mathf.Min(bounds.min.z, bounds.max.z), maxZ = Mathf.Max(bounds.min.z, bounds.max.z);

            bool alongX = (maxX - minX) >= (maxZ - minZ);   // long axis = the larger extent
            float axisPos = alongX ? spawn.x : spawn.z;
            float axisMin = alongX ? minX : minZ;
            float axisMax = alongX ? maxX : maxZ;

            // Step toward the end of the long axis with more room between it and the spawn.
            float dir = (axisMax - axisPos) >= (axisPos - axisMin) ? 1f : -1f;
            float target = ClampInside(axisPos + dir * ExitSpawnStep, axisMin, axisMax);

            float cross = ClampInside(alongX ? centre.z : centre.x, alongX ? minZ : minX, alongX ? maxZ : maxX);

            return alongX ? new Vector3(target, centre.y, cross)
                          : new Vector3(cross, centre.y, target);
        }

        /// <summary>
        /// Clamp <paramref name="v"/> into [min, max] inset by <see cref="ExitBoundsInset"/> on both
        /// ends. When the span is too narrow to hold the inset, returns the span's midpoint.
        /// </summary>
        private static float ClampInside(float v, float min, float max)
        {
            float lo = min + ExitBoundsInset;
            float hi = max - ExitBoundsInset;
            return lo <= hi ? Mathf.Clamp(v, lo, hi) : (min + max) * 0.5f;
        }

        /// <summary>
        /// Seat an exit's origin on the room floor so its arch stands rather than floating at the
        /// room's mid-height bounds centre (a short ray down, the townsfolk y-band idiom). Falls
        /// back to the raw point when nothing is hit.
        /// </summary>
        private static Vector3 SeatExitOnFloor(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            return p;
        }

        /// <summary>The layout's spawn world position, falling back to the origin.</summary>
        private Vector3 ResolveSpawnPosition()
        {
            if (Layout?.spawn != null) return Layout.spawn.position.ToWorld();

            // No explicit spawn — drop the Keeper at the centre of the entry room.
            DungeonRoom entry = Layout?.FindRoom(Layout.entryRoomId);
            if (entry?.bounds != null) return entry.bounds.Center;
            return Vector3.zero;
        }

        /// <summary>
        /// F8 2026-08-05 — THE DUNGEON SOFTLOCK FIX. Provision the Keeper as a FIGHT-CAPABLE hero
        /// exactly once per run, AFTER the async body swap has landed.
        ///
        /// PROVEN FROM DEVICE CAPTURE (2026-08-05), not inferred:
        ///   15:25:37.007 [Flow:Hero] Ensure begin scene='Dungeon_HealersCottage' isVillage=False
        ///   15:25:37.019 [Flow:Hero] Ensure: no hero in non-village scene 'Dungeon_HealersCottage'
        ///                            - nothing to ensure (skipping).
        ///   15:25:37.220 [Flow:Dungeon] SeedHeroVitalsFromLiveHero: no live HeroHealth on 'Keeper'
        ///                            nor HeroHealth.Instance - falling back to the 120 HP placeholder.
        ///   15:25:56.x   [Flow:HudKit] attack fired but no PlayerAttackController in scene   (x5)
        ///   enemy: 77x Idle_A while 69x inRange=True — aware, in range, idle, nothing to hit.
        /// The Keeper staged into BattleArena as a PARTIAL hero: 'Player'-tagged (EnterDungeon does
        /// that) but with NO PlayerAttackController — she could not damage the enemy — and NO
        /// HeroHealth — EnemyBrain deals damage ONLY through HeroHealth, so the enemy could not
        /// damage her. Mutual null-target deadlock: the fight could never resolve, so BattleLock
        /// never released and the run was dead from the first encounter.
        ///
        /// WHY THE FIX LIVES HERE AND NOT IN HeroControlEnsurer'S SCENE GATE. Two compounding causes:
        ///   (a) HeroControlEnsurer.IsVillageScene matches Village*/*Castle*/raid only, so Ensure()
        ///       early-returns for every Dungeon_* scene; AND
        ///   (b) a RACE that makes a dungeon clause ALONE insufficient — at sceneLoaded the Keeper
        ///       has no HeroLocomotion yet (HeroBodySwapper injects it ~160ms later,
        ///       HeroBodySwapper.cs:722), so Ensure()'s FindLoco() would find nothing and skip even
        ///       with a widened gate, and Ensure only re-runs on the next sceneLoaded — which never
        ///       comes, because the arena stages ADDITIVELY.
        /// So the DUNGEON owns the call, and the ORDERING is the fix: PlayerAttackController.Awake
        /// caches HeroLocomotion + ActorAnimator off the rig (PlayerAttackController.cs:206-209), so
        /// provisioning before the swap would arm a controller wired to a body that no longer exists.
        /// The injected HeroLocomotion is therefore used as the post-swap signal.
        ///
        /// IDEMPOTENT + ONCE: latched on <see cref="_keeperFightProvisioned"/>, and the ensurer it
        /// calls is itself null-checked per component, so nothing can double-attach.
        ///
        /// NEVER SILENT: if the body swap never lands, we still provision after
        /// <see cref="BodySwapWaitSeconds"/> behind a FlowTrace.Warn rather than leave the Keeper
        /// unarmed — an unwinnable encounter must be impossible, and a degraded path must be logged.
        /// </summary>
        private void EnsureKeeperFightCapable()
        {
            if (_keeperFightProvisioned) return;
            if (_hero == null) return;

            // Post-swap signal: HeroBodySwapper adds HeroLocomotion at the END of the swap, once the
            // real class body + its Animator exist. Wait for it — but only for a BOUNDED window, so a
            // failed/absent swap degrades to a logged late-provision instead of an unarmed Keeper.
            bool bodySwapped = _hero.GetComponent<DeNelle.Village.HeroLocomotion>() != null;
            if (!bodySwapped)
            {
                if (Time.time - _readySinceTime < BodySwapWaitSeconds) return;   // still swapping
                FlowTrace.Warn("Dungeon",
                    $"EnsureKeeperFightCapable: HeroBodySwapper never injected a HeroLocomotion on " +
                    $"'{_hero.name}' within {BodySwapWaitSeconds:0.#}s of the run going live — provisioning " +
                    "the combat components ANYWAY so the first encounter is winnable. The rig's animator " +
                    "wiring is degraded (attack may not animate); the fight itself will still resolve.");
            }

            _keeperFightProvisioned = true;
            Guard.Try("Dungeon", "provision Keeper as a fight-capable hero", () =>
                DeNelle.Village.HeroControlEnsurer.EnsureHeroCombatComponents(
                    _hero.gameObject,
                    $"DungeonController.EnsureKeeperFightCapable id='{_dungeonId}' bodySwapped={bodySwapped}"));

            // PROOF LINE (§12): the next capture must show, without a code read, that the Keeper can
            // both deal and take damage BEFORE any encounter stages.
            FlowTrace.Step("Dungeon",
                $"EnsureKeeperFightCapable: Keeper '{_hero.name}' provisioned (bodySwapped={bodySwapped}) — " +
                $"attack={(_hero.GetComponent<DeNelle.Village.PlayerAttackController>() != null)} " +
                $"health={(_hero.GetComponent<DeNelle.Village.HeroHealth>() != null)}. " +
                "An encounter staged from here can resolve.");
        }

        /// <summary>
        /// Felt-fix (2026-07-26): guarantee ONE mover on the dungeon Keeper. The village HeroBodySwapper
        /// injects a HeroLocomotion (NavMeshAgent) during the async body swap; with no navmesh baked in the
        /// cottage the agent's off-mesh fall-snap slides the hero and stomps DungeonHero's input.
        ///
        /// We keep DungeonHero the SOLE mover WITHOUT disabling the HeroLocomotion component (that would
        /// orphan every FindAnyObjectByType&lt;HeroLocomotion&gt; type-resolver — the exit prompt AND §7 enemy
        /// targeting — because those exclude disabled components). Instead we NEUTRALIZE its movement, three
        /// levers (no per-instance freeze API exists on HeroLocomotion):
        ///   • disable the injected NavMeshAgent — kills its internal off-mesh drive,
        ///   • HeroLocomotion.GroundSnapEnabled=false — kills the off-mesh fall-snap/slide on the un-navmeshed
        ///     cottage floor (HeroLocomotion:~391-409 / the snap at :1094),
        ///   • HeroLocomotion.SetScriptedMove(Vector2.zero) — forces its ReadMoveInput() to zero (:1412) so the
        ///     input-driven `transform.position += step` (:905) never fires against DungeonHero.
        /// Both statics are RESTORED on teardown (<see cref="RestoreInjectedHeroMover"/>) so they never leak
        /// to the village hero. Polled from Update (the swap finishes a frame or two late) and re-applied if a
        /// later swap re-enables the agent.
        /// </summary>
        private void EnsureSingleDungeonMover()
        {
            if (_hero == null) return;
            if (_injectedHeroLoco == null)
                _injectedHeroLoco = _hero.GetComponent<DeNelle.Village.HeroLocomotion>();
            if (_injectedHeroLoco == null) return; // not injected yet — nothing to neutralize

            // CRITICAL: keep the component ENABLED (type-resolution + enemy targeting rely on it). If an
            // earlier build/hot-reload left it disabled, undo that here.
            if (!_injectedHeroLoco.enabled)
                _injectedHeroLoco.enabled = true;

            if (_injectedHeroAgent == null)
                _injectedHeroAgent = _hero.GetComponent<UnityEngine.AI.NavMeshAgent>();

            bool flippedAgent = false;
            if (_injectedHeroAgent != null && _injectedHeroAgent.enabled)
            {
                _injectedHeroAgent.enabled = false;
                flippedAgent = true;
            }

            // Re-assert the movement gates each poll (a later body swap could re-enable the agent / reset these).
            DeNelle.Village.HeroLocomotion.GroundSnapEnabled = false;
            DeNelle.Village.HeroLocomotion.SetScriptedMove(Vector2.zero);

            bool firstApply = !_moverNeutralized;
            _moverNeutralized = true;
            if (firstApply || flippedAgent)
                FlowTrace.Step("Dungeon",
                    "felt-fix: neutralized injected HeroLocomotion (kept ENABLED for type-resolution/enemy targeting) — " +
                    "NavMeshAgent disabled, GroundSnapEnabled=false (no off-mesh fall-snap slide), scripted-move zeroed " +
                    "(no input-driven transform write). DungeonHero (CharacterController) is the sole mover.");
        }

        /// <summary>
        /// Undo <see cref="EnsureSingleDungeonMover"/>'s neutralize on dungeon teardown so the movement
        /// state — and the two STATIC gates the injected HeroLocomotion shares with the village hero — never
        /// leak past the dungeon: re-enable the NavMeshAgent, restore GroundSnapEnabled, and clear the
        /// scripted-move override. Guarded on <see cref="_moverNeutralized"/>, so it is safe to call from
        /// both the explicit exit and OnDestroy (and on paths where nothing was ever neutralized).
        /// </summary>
        private void RestoreInjectedHeroMover()
        {
            if (!_moverNeutralized) return;
            _moverNeutralized = false;
            DeNelle.Village.HeroLocomotion.GroundSnapEnabled = true;
            DeNelle.Village.HeroLocomotion.ClearScriptedMove();
            if (_injectedHeroAgent != null) _injectedHeroAgent.enabled = true;
            if (_injectedHeroLoco != null) _injectedHeroLoco.enabled = true;
            FlowTrace.Step("Dungeon",
                "restored injected HeroLocomotion mover on teardown — NavMeshAgent re-enabled, GroundSnapEnabled=true, scripted-move cleared.");
        }

        /// <summary>Moves the Keeper to <paramref name="spawnPos"/> facing the layout's heading.</summary>
        private void PlaceHero(Vector3 spawnPos)
        {
            if (_hero == null) return;

            float facingY = Layout?.spawn?.facingY ?? 0f;

            // The DungeonHero owns the safe teleport (it disables its own
            // CharacterController across the move and clears any tap target).
            if (_heroController != null)
            {
                _heroController.Teleport(spawnPos, facingY);
            }
            else
            {
                // No DungeonHero present — the shared authority does the mover-safe move
                // (HeroLocomotion.WarpTo, else a CharacterController-suspended transform write).
                // WO-1222: this used to be an inline copy of that logic. It is now the SAME code
                // the composed pipeline runs, so the two dungeon paths cannot drift apart again.
                DungeonHeroSeat.Seat(_hero, spawnPos, facingY, "data-driven Begin()");
            }

            // WO-1222 — PROVE IT LANDED. The hand-built path has always CALLED a placement; what
            // neither path had was an assertion that the hero is still there once every other
            // writer has had its say. The hero root can be DontDestroyOnLoad and BattleArena
            // (also DDOL) warps it ~7km to the staged arena, which is exactly how the composed
            // Healer's Cottage handed the owner a black screen on 2026-08-26. One authority,
            // both pipelines — a healthy placement costs one captured line and nothing else.
            DungeonHeroSeat.VerifyArrival(_hero.gameObject,
                UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
                spawnPos, facingY, "data-driven");
        }

        // ── Camera ───────────────────────────────────────────────────────────

        /// <summary>
        /// Aims the Cinemachine follow camera at the Keeper with the spec's
        /// top-down isometric tilt. Prefers the dedicated <see cref="DungeonCameraRig"/>
        /// (it owns the framing maths); falls back to an inline offset/pitch
        /// setup for a hand-wired camera. The camera follows the hero — and feeds
        /// the hero its yaw so WASD stays screen-relative under the tilt.
        /// </summary>
        private void ConfigureCamera()
        {
            if (_hero == null) return;

            // Preferred path: the camera rig component self-configures (it owns
            // the ceiling-aware height cap — see DungeonCameraRig.EffectiveOffset).
            if (_cameraRig != null)
            {
                _cameraRig.Bind(_hero);
                FlowTrace.Step("DungeonCam",
                    $"ConfigureCamera: bound via DungeonCameraRig (target='{_hero.name}').");
            }
            else if (_followCamera != null)
            {
                _followCamera.Follow = _hero;
                // LookAt is left unset: with no Aim component the rig keeps the
                // authored fixed pitch — the steady isometric tilt the spec asks
                // for (no orbit, no free-look — that is the village rig).
                _followCamera.LookAt = null;

                // Cap the height so the camera never floats far above the ~4u room
                // ceiling ("camera overtop / too high", owner 2026-07-16). Scale the
                // whole offset uniformly so the iso ANGLE holds and only distance tightens.
                Vector3 off = _cameraOffset;
                if (off.y > CameraMaxHeightAboveHero && off.y > 0.01f)
                {
                    float scale = CameraMaxHeightAboveHero / off.y;
                    off = new Vector3(off.x * scale, CameraMaxHeightAboveHero, off.z * scale);
                }

                // Seat the camera at the (capped) isometric offset behind + above
                // the hero, tilted down. CinemachineFollow eases it along.
                var camTransform = _followCamera.transform;
                camTransform.position = _hero.position + off;
                camTransform.rotation = Quaternion.Euler(_cameraPitch, 0f, 0f);

                FlowTrace.Step("DungeonCam",
                    $"ConfigureCamera: inline follow — authored={_cameraOffset} effective={off} " +
                    $"(cap={CameraMaxHeightAboveHero}) pitch={_cameraPitch} target='{_hero.name}'.");

                var follow = _followCamera.GetComponent<CinemachineFollow>();
                if (follow != null)
                {
                    follow.FollowOffset = off;
                    var settings = follow.TrackerSettings;
                    settings.PositionDamping = new Vector3(1.4f, 1.4f, 1.4f);
                    follow.TrackerSettings = settings;
                }
            }

            // Tell the hero which camera its WASD vector is relative to so
            // screen-up always maps to the camera's forward under the tilt.
            if (_heroController != null)
            {
                Camera unityCam = ResolveUnityCamera();
                if (unityCam != null) _heroController.SetCamera(unityCam);
            }
        }

        /// <summary>The Unity camera the Cinemachine brain drives — for hero input framing.</summary>
        private Camera ResolveUnityCamera()
        {
            // The CinemachineCamera is a controller, not a renderer — the real
            // Camera is the one with the CinemachineBrain (usually Camera.main).
            if (Camera.main != null) return Camera.main;
            return Object.FindAnyObjectByType<Camera>();
        }

        // ── Interactable + actor wiring ──────────────────────────────────────

        /// <summary>Hands the lantern its oil-stone refill points from the layout.</summary>
        private void ConfigureLantern()
        {
            if (_lantern == null || Layout == null) return;
            _lantern.Configure(this, Layout.oilStones, _hero);
        }

        /// <summary>
        /// Places + configures Bryn from the layout's <c>bryn</c> block, hands
        /// her the hero transform she watches for the proximity check, and the
        /// lore-fragment set her entrance line is sourced from (Week-6 checklist
        /// item 2).
        /// </summary>
        private void ConfigureBryn()
        {
            if (_bryn == null || Layout?.bryn == null) return;
            _bryn.Configure(Layout.bryn, _runtimeState);
            _bryn.SetHero(_hero);
            // WO-770.7 (D14): surface Bryn's greeting through the toast too, so her voice reads even
            // when the world-space speech bubble is unwired (MUTE). Bryn keeps its HUD-free isolation
            // via this delegate seam (same idiom as IWandererBubble).
            _bryn.SetToastSink(DungeonToastView.Show);
            if (_loreFragments != null)
                _bryn.SetLoreFragments(_loreFragments);
        }

        /// <summary>
        /// WO-711 item 1 (owner order 2026-07-13, verbatim: "SKIN THE PILL IN
        /// HEALERS COTTAGE AS A NPC"): dresses the entrance placeholder pill
        /// (Bryn's capsule stand-in from the scene builder) with a real
        /// People-pack body + a Talk teaching the torch/light need. Runs right
        /// after <see cref="ConfigureBryn"/> so Bryn's authored placement and
        /// rotation are final. Purely additive runtime dress — a failure logs
        /// and leaves the pill visible; never breaks the dungeon.
        /// </summary>
        private void DressEntranceNpc()
        {
            Guard.Try("Dungeon", "dress entrance NPC (torch warden)",
                () => TorchWardenDresser.Dress(_bryn, Layout, _hero));
        }

        /// <summary>
        /// Wires the crafting system (Workstream C) — the ingredient pickups,
        /// the crafting pedestal and the crafting UI panel — from the canonical
        /// crafting-recipes.json. Each ingredient pickup under
        /// <see cref="_ingredientRoot"/> pairs with one
        /// <c>ingredientPlacements[]</c> entry in file order; the pedestal takes
        /// the file's <c>pedestal</c> block. A null crafting data set leaves the
        /// whole layer inert (the file may not be imported yet).
        /// </summary>
        private void ConfigureCrafting()
        {
            if (_craftingData == null) return;

            // ── Ingredient pickups — child[i] pairs with placement[i]. ───────
            if (_ingredientRoot != null && _craftingData.IngredientPlacements != null)
            {
                var pickups = _ingredientRoot.GetComponentsInChildren<IngredientPickup>(true);
                int total = _craftingData.IngredientPlacements.Count;
                int n = Mathf.Min(pickups.Length, total);
                // WO-749: MORE placements than scene pickups is NORMAL now (the 12-ingredient
                // floor scatter is authored in data, not baked into the scene). Only warn when
                // the SCENE has extra pickups no placement feeds (a real drift).
                if (pickups.Length > total)
                {
                    FlowTrace.Warn("Dungeon",
                        $"ConfigureCrafting: {pickups.Length} scene ingredient-pickups but only {total} " +
                        $"placements in crafting data — hydrating the first {n}, {pickups.Length - total} left inert.");
                }
                for (int i = 0; i < n; i++)
                    pickups[i].Configure(
                        _craftingData.IngredientPlacements[i], _dungeonInventory, _hero);

                // WO-749 gap 2/4 — floor scatter without a scene bake: every placement
                // beyond the scene-authored pickups is runtime-authored as a tinted mote.
                // Collected ids ride the per-run DungeonInventory and bank to the larder on
                // exit (DungeonLootGrant.DepositDungeonInventory).
                for (int i = n; i < total; i++)
                {
                    var placement = _craftingData.IngredientPlacements[i];
                    if (placement == null) continue;
                    Color tint = TintForIngredient(placement.IngredientId);
                    // WO-1132 d5: the authored glyph picks the mote's SHAPE FAMILY, so
                    // ingredients are told apart by silhouette rather than by pastel hue
                    // (colourblind law — see the IngredientPickup file header).
                    string glyph = GlyphForIngredient(placement.IngredientId);
                    IngredientPickup.CreateRuntime(_ingredientRoot, placement, _dungeonInventory, _hero, tint, glyph);
                }
                if (total > n)
                    FlowTrace.Step("DungeonLoot",
                        $"ConfigureCrafting: runtime-authored {total - n} scatter mote(s) beyond {n} scene pickup(s).");
            }

            // ── Crafting pedestal + its UI panel. ────────────────────────────
            if (_craftingPedestal != null && _craftingData.Pedestal != null)
            {
                _craftingPedestal.Configure(
                    _craftingData.Pedestal, _craftingData, _dungeonInventory, _hero);

                // WO-770.7 (D13): surface the craft — CraftingPedestal.ToastRequested fired into
                // the void (0 subscribers) before, so a completed craft gave no confirmation.
                _craftingPedestal.ToastRequested.RemoveListener(DungeonToastView.Show);
                _craftingPedestal.ToastRequested.AddListener(DungeonToastView.Show);

                // Subscribe the UI panel to the pedestal's open/close events —
                // keeps the pedestal a pure scene actor and the panel a pure view.
                if (_craftingPanel != null)
                    _craftingPanel.BindPedestal(_craftingPedestal);
            }
        }

        /// <summary>
        /// The tint (from crafting-recipes.json <c>tint</c> hex) for a scatter mote,
        /// falling back to loot-gold when the ingredient has no authored tint.
        /// </summary>
        private Color TintForIngredient(string ingredientId)
        {
            var ing = _craftingData?.FindIngredient(ingredientId);
            if (ing != null && !string.IsNullOrEmpty(ing.Tint)
                && ColorUtility.TryParseHtmlString("#" + ing.Tint, out Color c))
                return c;
            return new Color(0.95f, 0.82f, 0.35f);   // loot-gold fallback
        }

        /// <summary>
        /// The single-char <c>glyph</c> authored for an ingredient in
        /// crafting-recipes.json, or <c>null</c> when the ingredient (or its glyph) does
        /// not resolve. The scatter mote turns this into a SHAPE FAMILY so the
        /// silhouette carries identity — the authored tints are mostly pastels and read
        /// as the same white pellet in a dark dungeon. Null-safe in the same shape as
        /// <see cref="TintForIngredient"/>; a null simply keeps the plain sphere mote.
        /// </summary>
        private string GlyphForIngredient(string ingredientId)
        {
            var ing = _craftingData?.FindIngredient(ingredientId);
            return string.IsNullOrEmpty(ing?.Glyph) ? null : ing.Glyph;
        }

        /// <summary>
        /// Wires the treasure chests (WO-749 gap 1) — attaches a
        /// <see cref="DungeonChestInteract"/> to each layout chest so its rewardKey
        /// resolves to a larder loot grant on open. The scene builder places chest
        /// VISUALS named <c>Chest_{id}</c> with no behaviour; this attaches the
        /// interact at runtime (NO scene bake), falling back to a runtime marker at
        /// the layout coords when the visual is absent (pack not imported). Idempotent
        /// — a chest already carrying the component is skipped.
        /// </summary>
        private void HydrateChests()
        {
            if (Layout?.chests == null || Layout.chests.Length == 0 || _runtimeState == null) return;

            using var _flow = FlowTrace.Enter("DungeonLoot", "HydrateChests");

            // Bucket every "Chest_*" transform once so each layout chest finds its visual.
            var byName = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                if (t.name.StartsWith("Chest_", System.StringComparison.Ordinal)
                    && !byName.ContainsKey(t.name))
                    byName[t.name] = t;
            }

            int wired = 0;
            foreach (var chest in Layout.chests)
            {
                if (chest == null || string.IsNullOrEmpty(chest.id)) continue;
                var localChest = chest;   // capture for the closure
                Guard.Try("DungeonLoot", $"wire chest '{localChest.id}'", () =>
                {
                    GameObject host;
                    if (byName.TryGetValue($"Chest_{localChest.id}", out var visual) && visual != null)
                    {
                        host = visual.gameObject;
                    }
                    else
                    {
                        host = new GameObject($"Chest_{localChest.id}");
                        host.transform.SetParent(transform, false);
                        host.transform.position = localChest.position.ToWorld();
                        FlowTrace.Warn("DungeonLoot",
                            $"chest '{localChest.id}' has no scene visual — runtime marker placed at " +
                            $"{host.transform.position} (KayKit chest mesh not imported?).");
                    }

                    if (host.GetComponent<DungeonChestInteract>() == null)
                    {
                        host.AddComponent<DungeonChestInteract>()
                            .Configure(localChest, _runtimeState, _hero);
                        wired++;
                    }
                });
            }
            FlowTrace.Step("DungeonLoot",
                $"HydrateChests: wired {wired} chest interactable(s) of {Layout.chests.Length}.");
        }

        /// <summary>
        /// Feeds the dungeon HUD (Workstream C) its Lantern reference so the oil
        /// meter polls the lantern's public API each frame. The HUD is a passive
        /// display — it never mutates the lantern.
        /// </summary>
        private void ConfigureDungeonHud()
        {
            if (_dungeonHud != null && _lantern != null)
                _dungeonHud.SetLantern(_lantern);
        }

        /// <summary>
        /// Hydrates the lore stones placed under <see cref="_loreStoneRoot"/> from
        /// the layout's <c>loreStones</c> array (Week-6 checklist item 3). The
        /// scene builder places one <see cref="LoreStone"/> per layout entry, in
        /// layout order, so child[i] pairs with <c>loreStones[i]</c>. Each stone
        /// is also handed the lore-fragment set for its canon reading text.
        /// </summary>
        private void HydrateLoreStones()
        {
            if (_loreStoneRoot == null || Layout?.loreStones == null) return;

            var stones = _loreStoneRoot.GetComponentsInChildren<LoreStone>(true);
            int total = Layout.loreStones.Length;
            int n = Mathf.Min(stones.Length, total);
            // VERIFY: the layout authored lore stones but the scene placed NONE — Mathf.Min
            // would silently hydrate 0 and report "success". Assert >0 so a stripped/empty
            // _loreStoneRoot self-reports instead of leaving the room bare of readable lore.
            if (total > 0 && stones.Length == 0)
            {
                FlowTrace.Warn("Dungeon",
                    $"HydrateLoreStones: layout authored {total} lore stone(s) but the scene placed " +
                    "ZERO under _loreStoneRoot — no lore will be readable. Check the scene builder.");
            }
            else if (stones.Length != total)
            {
                FlowTrace.Warn("Dungeon",
                    $"HydrateLoreStones: count mismatch — {stones.Length} in scene, {total} in layout. " +
                    $"Hydrating the first {n}.");
            }

            for (int i = 0; i < n; i++)
            {
                int idx = i;   // capture for the closure
                FlowTrace.Try("Dungeon", $"configure lore stone[{idx}]", () =>
                {
                    stones[idx].Configure(Layout.loreStones[idx], _runtimeState, _hero, total);
                    if (_loreFragments != null)
                        stones[idx].SetLoreFragments(_loreFragments);
                    // WO-770.4 (fixes D6): the missing subscriber. A tap (via the stone's
                    // MobileInteractButton) raises ReadRequested; the code-built Obsidian
                    // LoreReadingModal renders the canon fragment. Closes the triple gap
                    // (input + subscriber + view) so lore is actually readable in gameplay.
                    stones[idx].ReadRequested.RemoveListener(LoreReadingModal.Show);
                    stones[idx].ReadRequested.AddListener(LoreReadingModal.Show);
                });
            }
            FlowTrace.Step("Dungeon", $"HydrateLoreStones: hydrated {n} of {total} lore stone(s).");
        }

        /// <summary>
        /// Hydrates the checkpoint shrines under <see cref="_checkpointRoot"/>
        /// from the layout's <c>checkpoints</c> array (Week-6 checklist item 4).
        /// Child[i] pairs with <c>checkpoints[i]</c>.
        /// </summary>
        private void HydrateCheckpoints()
        {
            if (_checkpointRoot == null || Layout?.checkpoints == null) return;

            var shrines = _checkpointRoot.GetComponentsInChildren<Checkpoint>(true);
            int total = Layout.checkpoints.Length;
            int n = Mathf.Min(shrines.Length, total);
            // VERIFY: layout authored checkpoints but the scene placed NONE — without a
            // checkpoint there is no heal/respawn anchor. Assert >0 so a stripped root
            // self-reports rather than silently shipping a save-less run.
            if (total > 0 && shrines.Length == 0)
            {
                FlowTrace.Warn("Dungeon",
                    $"HydrateCheckpoints: layout authored {total} checkpoint(s) but the scene placed " +
                    "ZERO under _checkpointRoot — no heal/respawn anchor exists. Check the scene builder.");
            }
            else if (shrines.Length != total)
            {
                FlowTrace.Warn("Dungeon",
                    $"HydrateCheckpoints: count mismatch — {shrines.Length} in scene, {total} in layout. " +
                    $"Hydrating the first {n}.");
            }

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                FlowTrace.Try("Dungeon", $"configure checkpoint[{idx}]",
                    () => shrines[idx].Configure(Layout.checkpoints[idx], _runtimeState, _hero));
                // WO-770.7 (D13): surface the reach — Checkpoint.ToastRequested fired into the
                // void (0 subscribers) before, so checkpoints healed silently.
                shrines[idx].ToastRequested.RemoveListener(DungeonToastView.Show);
                shrines[idx].ToastRequested.AddListener(DungeonToastView.Show);
            }
            FlowTrace.Step("Dungeon", $"HydrateCheckpoints: hydrated {n} of {total} checkpoint(s).");
        }

        /// <summary>
        /// Hydrates the encounter triggers under <see cref="_encounterRoot"/>
        /// (Week-6 checklist item 5). The scene builder places one trigger per
        /// scripted encounter in layout order, then the mini-boss trigger LAST.
        /// So child[0..k-1] take <c>scriptedEncounters[i]</c> and the trailing
        /// child takes <c>miniBoss</c>.
        /// </summary>
        private void HydrateEncounters()
        {
            _encounterTriggers.Clear();
            if (_encounterRoot == null || Layout == null) return;

            var triggers = _encounterRoot.GetComponentsInChildren<EncounterTrigger>(true);
            DungeonScriptedEncounter[] scripted =
                Layout.scriptedEncounters ?? System.Array.Empty<DungeonScriptedEncounter>();
            bool hasBoss = Layout.miniBoss != null;
            int expected = scripted.Length + (hasBoss ? 1 : 0);

            // VERIFY: the layout expects encounters (scripted and/or a boss) but the scene
            // placed NONE — the dungeon would have no fights at all (and an unbeatable boss
            // gate). Assert >0 so a stripped _encounterRoot self-reports rather than shipping
            // a combat-less / un-clearable run.
            if (expected > 0 && triggers.Length == 0)
            {
                FlowTrace.Warn("Dungeon",
                    $"HydrateEncounters: layout expects {expected} encounter(s) ({scripted.Length} scripted + " +
                    $"{(hasBoss ? 1 : 0)} boss) but the scene placed ZERO triggers under _encounterRoot — " +
                    "no fights will fire and the boss gate can never clear. Check the scene builder.");
            }
            else if (triggers.Length != expected)
            {
                FlowTrace.Warn("Dungeon",
                    $"HydrateEncounters: count mismatch — {triggers.Length} in scene, {expected} expected " +
                    $"({scripted.Length} scripted + {(hasBoss ? 1 : 0)} boss). Hydrating what aligns.");
            }

            int scriptedCount = Mathf.Min(scripted.Length, triggers.Length);
            for (int i = 0; i < scriptedCount; i++)
            {
                int idx = i;
                FlowTrace.Try("Dungeon", $"configure scripted encounter[{idx}]",
                    () => triggers[idx].ConfigureScripted(this, scripted[idx], _runtimeState, _hero));
                _encounterTriggers.Add(triggers[i]);
            }

            // The trailing trigger is the mini-boss (the Apprentice of the
            // Apothecary — design §4 Beat 6), configured via ConfigureBoss.
            if (hasBoss && triggers.Length > scripted.Length)
            {
                EncounterTrigger bossTrigger = triggers[triggers.Length - 1];
                FlowTrace.Try("Dungeon", "configure boss encounter",
                    () => bossTrigger.ConfigureBoss(this, Layout.miniBoss, 4f, _runtimeState, _hero));
                _encounterTriggers.Add(bossTrigger);
            }
            else if (hasBoss)
            {
                // A boss is authored but no trigger slot remained for it — the boss fight
                // can never fire, so the dungeon's exit gate can never unlock.
                FlowTrace.Warn("Dungeon",
                    "HydrateEncounters: layout authored a mini-boss but no trailing trigger was " +
                    "available to host it — the boss fight cannot fire and the exit gate stays locked.");
            }
            FlowTrace.Step("Dungeon",
                $"HydrateEncounters: hydrated {_encounterTriggers.Count} trigger(s) ({scriptedCount} scripted" +
                $"{(hasBoss && triggers.Length > scripted.Length ? " + 1 boss" : string.Empty)}).");
        }

        /// <summary>
        /// Settles the in-flight ATB encounter on dungeon re-entry — the dungeon
        /// side of the BUG-008 round-trip (Week-6 checklist item 6). Reads the
        /// pending encounter id off the run state and calls the matching
        /// trigger's <see cref="EncounterTrigger.ResumePendingEncounter"/>; a
        /// boss victory flags the boss defeated.
        /// </summary>
        private void ResolvePendingEncounter()
        {
            if (_runtimeState == null || !_runtimeState.HasPendingEncounter) return;

            // WO-770.3 (fixes D4): read the settled outcome off the Core-level carrier BattleController
            // stamped before the hand-back. Only a stamped Victory is a win; a Defeat OR a missing
            // carrier (dev/direct-play with no battle) is a loss. This is the ATB (ff.dungeonrealtime
            // OFF) resume, reached only on the scene-reload round-trip — the real-time path has NO
            // round-trip and settles via OnRealtimeBattleEnded (WO-770.3b) instead of ever reaching here.
            var battleCarrier = SceneRouter.PendingBattle;
            bool victory = battleCarrier != null && battleCarrier.LastOutcome == BattleResultKind.Victory;
            bool wasBoss = _runtimeState.PendingEncounterIsBoss;

            FlowTrace.Step("Dungeon",
                $"ResolvePendingEncounter (ATB reload): id='{_runtimeState.PendingEncounterId}' victory={victory} " +
                $"boss={wasBoss} carrier={(battleCarrier == null ? "none" : battleCarrier.LastOutcome.ToString())}.");
            SettleEncounter(victory, wasBoss);
        }

        /// <summary>
        /// WO-770.3b — the ONE encounter-settlement authority, shared by BOTH battle paths so a
        /// win/loss behaves identically whichever path ran:
        ///   • VICTORY: credit the per-encounter loot + clear the combat lock. ResumeAfterEncounter(true)
        ///     credits the mini-boss internally when the pending fight WAS the boss — which unlocks the
        ///     WO-770.1 back-door. The hero resumes in place.
        ///   • DEFEAT: the WO-770.3 LOCKED path — clear the lock with NO boss credit + NO loot, then
        ///     ExitToVillage (the run ends; the boss-gated back-door stays sealed).
        /// The ATB path calls this from <see cref="ResolvePendingEncounter"/> on the scene-reload resume;
        /// the real-time path calls it from <see cref="OnRealtimeBattleEnded"/> (BattleArena warps the
        /// hero back in-scene with NO round-trip, so its completion event is the dungeon's only signal).
        /// The trigger-ownership loop the ATB path used is gone: the owning trigger's fired-flag already
        /// restores from HasFiredScriptedEncounter on the reload, so the settle only needs the state.
        /// </summary>
        internal void SettleEncounter(bool victory, bool wasBoss)
        {
            if (_runtimeState == null || !_runtimeState.HasPendingEncounter) return;

            if (!victory)
            {
                FlowTrace.Warn("Dungeon",
                    $"SettleEncounter: DEFEAT (boss={wasBoss}) — ending the run + returning to Village " +
                    "(no loot, boss NOT credited, no back-door unlock).");
                // F8 2026-07-30 seq512 (defeat freeze): this settle routes its OWN scene exit, so
                // the arena's pending ReturnHomeWithFade warp must not fire into the leaving scene
                // (it manufactured an off-mesh void coordinate and froze the Keeper). OnBattleEnded
                // runs while that coroutine is parked at its fade-out yield, so this cancel is
                // always observed before the warp line executes.
                DeNelle.Village.Arena.BattleArena.Existing?.CancelPendingReturnWarp(
                    "dungeon DEFEAT is routing ExitToVillage");
                _runtimeState.ResumeAfterEncounter(false); // clears _inCombat + handoff; victory=false => no boss credit
                ExitToVillage().Forget();
                return;
            }

            DungeonLootGrant.GrantEncounter(wasBoss);       // WO-749: credit the per-encounter loot on a win
            _runtimeState.ResumeAfterEncounter(true);        // clears _inCombat + handoff; credits the boss when wasBoss
            FlowTrace.Step("Dungeon",
                $"SettleEncounter: VICTORY settled (boss={wasBoss}) — combat lock cleared, hero resumes in place.");
        }

        // ── WO-770.3b: real-time BattleArena settlement bridge ───────────────────
        // The real-time dungeon fight (ff.dungeonrealtime ON, the DEFAULT) stages in an isolated
        // BattleArena and warps the hero back IN-SCENE — there is NO scene round-trip, so the
        // OnDestroy/EnterDungeon resume never fires and ResolvePendingEncounter is never reached.
        // Without this hook nothing clears the combat lock, credits the boss, or ends the run on a
        // loss (the "real-time resume seam" gap). Subscribe to BattleArena's completion event and
        // route it through the SAME SettleEncounter the ATB path uses so the two paths stay in parity.

        private void SubscribeRealtimeSettle()
        {
            if (_arenaSubscribed || !FeatureFlags.DungeonRealtimeBattle) return;
            var arena = DeNelle.Village.Arena.BattleArena.Instance; // lazy persistent (DontDestroyOnLoad) singleton
            if (arena == null) return;
            arena.OnBattleEnded += OnRealtimeBattleEnded;
            // Dungeon-FPV combat-camera switch (2026-07-26): a real-time fight forces the dungeon
            // rig to OVER-THE-SHOULDER on stage-in and restores FPV traversal on end — OTS combat,
            // FPV walking. Null-safe on the rig; no-op when FPV is off (SetCombatFraming still just
            // toggles OTS<->the resolved mode).
            arena.OnBattleStaged += OnRealtimeBattleStaged;
            _arenaSubscribed = true;
            FlowTrace.Step("Dungeon", "WO-770.3b: subscribed to BattleArena.OnBattleStaged/OnBattleEnded (real-time settle + combat-camera switch).");
        }

        private void UnsubscribeRealtimeSettle()
        {
            if (!_arenaSubscribed) return;
            var arena = DeNelle.Village.Arena.BattleArena.Existing; // never CREATE a host just to unsubscribe
            if (arena != null)
            {
                arena.OnBattleEnded -= OnRealtimeBattleEnded;
                arena.OnBattleStaged -= OnRealtimeBattleStaged;
            }
            _arenaSubscribed = false;
        }

        /// <summary>
        /// Audit R-A1 (2026-08-01): toggles the Keeper's CharacterController while a
        /// real-time arena fight owns the hero. SetInputEnabled(false) leaves
        /// DungeonHero.Update calling _controller.Move(gravity) every frame — two live
        /// collision bodies (the CC + the arena-driven HeroLocomotion/NavMeshAgent)
        /// driving the SAME transform. Null-safe; no-op when already in the wanted state.
        /// </summary>
        private void SetHeroCharacterController(bool enabled, string reason)
        {
            var cc = _heroController != null
                ? _heroController.GetComponent<CharacterController>()
                : (_hero != null ? _hero.GetComponent<CharacterController>() : null);
            if (cc == null || cc.enabled == enabled) return;
            cc.enabled = enabled;
            FlowTrace.Step("Dungeon",
                $"R-A1: hero CharacterController {(enabled ? "RE-ENABLED" : "DISABLED")} ({reason}) -- sole collision body enforced.");
        }

        /// <summary>
        /// A real-time BattleArena encounter just staged — force the dungeon camera to
        /// over-the-shoulder for the fight (only meaningful while WE have a pending
        /// dungeon encounter; DungeonController lives only in the dungeon scene, so any
        /// arena stage reached here is a dungeon fight). Restored on
        /// <see cref="OnRealtimeBattleEnded"/>.
        /// </summary>
        private void OnRealtimeBattleStaged(DeNelle.Village.Arena.EncounterParams _)
        {
            if (_runtimeState == null || !_runtimeState.HasPendingEncounter) return;
            FlowTrace.Step("Dungeon", "dungeon-fpv: BattleArena staged — SetCombatFraming(true) (OTS for the fight).");
            _cameraRig?.SetCombatFraming(true);

            // F8 2026-07-30: the arena drives the Keeper's injected HeroLocomotion for the
            // fight (WarpHero -> WarpTo). Suspend the sole-mover neutralize + stand
            // DungeonHero down so the two movers never double-drive. OnBattleStaged fires
            // BEFORE StageRoutine's WarpHero, so the un-neutralize lands before the warp.
            _arenaOwnsHero = true;
            _heroController?.SetInputEnabled(false);
            // Audit R-A1 (2026-08-01): input-off alone is NOT enough — DungeonHero.Update
            // still calls _controller.Move(gravity) every frame, so TWO collision bodies
            // (this CharacterController + the arena-driven HeroLocomotion/NavMeshAgent)
            // keep fighting over ONE transform for the whole fight. Disable the CC so the
            // arena's mover is the sole collision body; restored on end AND on abandon.
            SetHeroCharacterController(false, "arena staged");
            RestoreInjectedHeroMover();
        }

        /// <summary>
        /// INSTRUMENTATION ONLY (2026-08-05). A capture showed THREE different hero positions
        /// inside one post-fight settle window — (50,0,50), the warp target (-28,0.08,0), and a
        /// sampled (-24.2,7.1) — and no one could name which system wrote the last one. This
        /// handler is the settle window, so dumping the pose on ENTRY and on EVERY EXIT makes
        /// the next capture self-explaining: whatever moves between the entry and exit lines
        /// was written by something inside this handler, and whatever differs from the exit
        /// line was written after it. No logic, ordering or behaviour depends on this.
        /// </summary>
        private string HeroPose(string when)
        {
            if (_hero == null) return $"{when}=<null>";
            var cc = _hero.GetComponent<CharacterController>();
            var ag = _hero.GetComponent<UnityEngine.AI.NavMeshAgent>();
            string ccState = cc == null ? "<none>" : (cc.enabled ? "LIVE" : "disabled");
            string agState = ag == null ? "<none>"
                : (ag.enabled ? (ag.isOnNavMesh ? "on-mesh" : "off-mesh") : "disabled");
            return $"{when}: pos={_hero.position:F2} rotY={_hero.eulerAngles.y:F1} " +
                   $"cc={ccState} agent={agState} scene='{_hero.gameObject.scene.name}'";
        }

        private void OnRealtimeBattleEnded(DeNelle.Village.Arena.EncounterParams _, bool won)
        {
            FlowTrace.Step("Dungeon", $"OnRealtimeBattleEnded ENTRY (won={won}) — {HeroPose("hero")}");

            // Restore FPV/iso traversal framing regardless of who launched the fight (a no-op
            // when combat framing was never forced) BEFORE the pending-encounter guard below.
            _cameraRig?.SetCombatFraming(false);

            // F8 2026-07-30: hand the hero back to the dungeon movers.
            // EnsureSingleDungeonMover re-neutralizes HeroLocomotion next Update.
            _arenaOwnsHero = false;
            // Audit R-A1 (2026-08-01): re-enable the CharacterController disabled on stage
            // BEFORE input returns, so DungeonHero's first Move lands on a live body.
            SetHeroCharacterController(true, "arena ended");
            _heroController?.SetInputEnabled(true);

            // Only settle a dungeon encounter WE launched — guards a stray arena event + double-settle.
            if (_runtimeState == null || !_runtimeState.HasPendingEncounter)
            {
                FlowTrace.Step("Dungeon", $"OnRealtimeBattleEnded EXIT (no pending encounter — not ours) — {HeroPose("hero")}");
                return;
            }
            bool wasBoss = _runtimeState.PendingEncounterIsBoss;
            FlowTrace.Step("Dungeon", $"WO-770.3b: real-time BattleArena ended (won={won}, boss={wasBoss}) — settling.");
            SettleEncounter(won, wasBoss);

            FlowTrace.Step("Dungeon", $"OnRealtimeBattleEnded EXIT (settled, won={won}, boss={wasBoss}) — {HeroPose("hero")}");
        }

        /// <summary>
        /// ABANDONMENT UNWIND (patch 6, F8 2026-07-30): this dungeon is going away WHILE a real-time
        /// arena fight is live — portal / back-door exit, quit-to-menu, or a hero death-EVAC scene
        /// route. BattleArena is DontDestroyOnLoad but its stage and every spawned enemy are
        /// active-scene objects, so it would keep ticking an encounter whose stage, combatants and
        /// hero are all being destroyed and resolve it as a PHANTOM WIN (unearned loot + a return
        /// warp). Tear it down with NO outcome and hand the Keeper back.
        ///
        /// Gated strictly on <c>_arenaOwnsHero</c> — that flag is set ONLY by
        /// <see cref="OnRealtimeBattleStaged"/> for an encounter WE staged and cleared on end, so the
        /// ATB round-trip path (ff.dungeonrealtime OFF) never reaches here and its pending-encounter
        /// handoff is never touched.
        /// </summary>
        private void AbandonRealtimeBattle(string reason)
        {
            if (!_arenaOwnsHero) return;

            FlowTrace.Warn("Dungeon",
                $"patch 6: ABANDONING the live real-time BattleArena encounter — {reason} " +
                "(no loot, no boss credit, no settle).");

            var arena = DeNelle.Village.Arena.BattleArena.Existing;   // never CREATE a host just to abandon
            if (arena != null)
                Guard.Try("Dungeon", "abandon real-time arena encounter",
                    () => arena.ResolveAbandoned($"dungeon abandoned the encounter ({reason})"));

            // Hand the Keeper's movers back (Update resumes EnsureSingleDungeonMover) and drop the
            // combat framing. Guard-wrapped: this also runs from OnDestroy, where the rig may already
            // be torn down.
            _arenaOwnsHero = false;
            // Audit R-A1 (2026-08-01): restore the CharacterController disabled on stage —
            // guard-wrapped because this path also runs from OnDestroy, where the hero rig
            // may already be destroyed under us.
            Guard.Try("Dungeon", "restore hero CharacterController on abandon",
                () => SetHeroCharacterController(true, "arena abandoned"));
            if (_cameraRig != null)
                Guard.Try("Dungeon", "restore framing on abandon", () => _cameraRig.SetCombatFraming(false));

            // Drop the handoff WITHOUT settling it — the same unwind EncounterTrigger.RollbackHandoff
            // uses for a stage that never started, so a re-entry can never resume a fight that no
            // longer exists and OnDestroy's HasPendingEncounter early-return does not strand the run.
            if (_runtimeState != null)
            {
                _runtimeState.ClearPendingEncounter();
                _runtimeState.ResolveEncounter();
            }
        }

        // ── Traversal ports (WO-711 items 3-4 — owner rulings, live walk) ────

        /// <summary>
        /// Dresses every door and staircase with a simple interact-to-port pair
        /// (WO-711: "anywhere with a door, use Door action — nav-link PORT from
        /// one side to the other"; "same with steps going up"; "we can cook
        /// later but for now simple"). Runtime-authored — never a scene edit.
        ///
        /// DOORS are keyed on the layout's <c>kind=="doorway"</c> wall segments
        /// (each carries <c>leadsTo</c>): one <see cref="DungeonPortLink"/> per
        /// side at the doorway midpoint, offset ~1.5u into each room, targeting
        /// the mate point across the wall. Illusory walls are SKIPPED (the
        /// secret walk-through is the design). STAIRS have no layout data —
        /// they are keyed on DungeonSceneBuilder's authored vertical connectors
        /// (BuildVerticalConnectors: stairs_long/-wood/-narrow + the cellar-entry
        /// placeholder), so the three pairs are table-driven for the Healer's
        /// Cottage only; another dungeon id logs a Warn instead of guessing.
        /// </summary>
        private void DressTraversalLinks()
        {
            if (Layout == null || _hero == null) return;

            using var _flow = FlowTrace.Enter("Dungeon", "DressTraversalLinks (WO-711)");

            var root = new GameObject("TraversalLinks");
            root.transform.SetParent(transform, false);

            // Room -> floor Y. The layout JSON carries NO level field (bounds are
            // XZ-only); the level assignment is the scene builder's (ground Y=0,
            // upper Y=6, underground Y=-6 — DungeonSceneBuilder.cs YUpper/YUnder
            // + each RoomDef.Level). Unknown rooms fall back to any floor-level
            // layout object in that room (checkpoint/encounter/chest y), else 0.
            var roomY = new System.Collections.Generic.Dictionary<string, float>
            {
                { "garden-approach", 0f }, { "entrance-room", 0f }, { "main-room", 0f },
                { "kitchen", 0f }, { "pantry-alcove", 0f }, { "workshop", 0f },
                { "loft-bedroom", 6f }, { "loft-study", 6f },
                { "root-cellar", -6f }, { "storage", -6f },
                { "crypt-sublevel", -6f }, { "hidden-vault", -6f },
            };

            float LevelYFor(DungeonRoom room)
            {
                if (room == null) return 0f;
                if (roomY.TryGetValue(room.id, out float y)) return y;
                foreach (var c in Layout.checkpoints)
                    if (c != null && c.roomId == room.id) return c.position.y;
                foreach (var e in Layout.scriptedEncounters)
                    if (e != null && e.roomId == room.id) return e.triggerPosition.y;
                foreach (var ch in Layout.chests)
                    if (ch != null && ch.roomId == room.id) return ch.position.y;
                FlowTrace.Warn("Dungeon",
                    $"DressTraversalLinks: no level Y known for room '{room.id}' — assuming 0.");
                return 0f;
            }

            // Seat a point on the floor band so a port never lands inside
            // geometry (the townsfolk y-band idiom): short ray down from just
            // above the authored point onto the room's floor collider.
            Vector3 SeatOnFloor(Vector3 p)
            {
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down,
                        out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
                    return hit.point + Vector3.up * 0.05f;
                return p;
            }

            float YawTo(Vector3 from, Vector3 to)
            {
                Vector3 d = to - from; d.y = 0f;
                if (d.sqrMagnitude < 0.0001f) return 0f;
                return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }

            int built = 0;

            void BuildPair(string pairName, string prompt,
                Vector3 posA, string labelA, Vector3 posB, string labelB)
            {
                Guard.Try("Dungeon", $"traversal pair '{pairName}'", () =>
                {
                    Vector3 a = SeatOnFloor(posA);
                    Vector3 b = SeatOnFloor(posB);

                    var goA = new GameObject($"PortLink_{pairName}_A");
                    goA.transform.SetParent(root.transform, false);
                    goA.transform.position = a;
                    goA.AddComponent<DungeonPortLink>().Configure(
                        prompt, b, YawTo(a, b), _hero, _heroController, labelA, labelB);

                    var goB = new GameObject($"PortLink_{pairName}_B");
                    goB.transform.SetParent(root.transform, false);
                    goB.transform.position = b;
                    goB.AddComponent<DungeonPortLink>().Configure(
                        prompt, a, YawTo(b, a), _hero, _heroController, labelB, labelA);

                    built++;
                    FlowTrace.Step("Dungeon",
                        $"DressTraversalLinks: pair '{pairName}' ('{prompt}') " +
                        $"{labelA}@{a} <-> {labelB}@{b}.");
                });
            }

            // ── DOORS — one pair per doorway wall segment (deduped: the same
            //    doorway is listed in BOTH rooms' wall arrays). ────────────────
            //
            // ALIGN FIX (owner F8 "open door not aligned with door location"):
            // the visible door GAP is cut by DungeonSceneBuilder from its OWN
            // hardcoded RoomDef door offsets, quantized to 4u wall segments —
            // which DRIFT from this layout JSON's doorway coords (garden<->
            // entrance: mesh gap ~z=2 vs JSON mid z=6 => a ~4u sideways offset,
            // so the 'Open Door' prompt fired well off the door mouth). We anchor
            // each side's port at that ROOM'S OWN built 'wall_doorway' mesh
            // (its transform IS the gap centre) instead of the JSON midpoint, so
            // the prompt sits where the Keeper actually sees the door. Runtime
            // only — no scene edit. Falls back to the JSON mid if a mesh is
            // missing (e.g. the pack was not imported -> placeholder box).
            var doorMeshByRoom = CollectDoorMeshes();

            var done = new System.Collections.Generic.HashSet<string>();
            foreach (var room in Layout.rooms)
            {
                if (room?.walls == null) continue;
                foreach (var wall in room.walls)
                {
                    if (wall == null || !wall.IsDoorway) continue;
                    if (string.IsNullOrEmpty(wall.leadsTo)) continue;

                    string key = string.CompareOrdinal(room.id, wall.leadsTo) < 0
                        ? room.id + "|" + wall.leadsTo
                        : wall.leadsTo + "|" + room.id;
                    if (!done.Add(key)) continue;

                    DungeonRoom other = Layout.FindRoom(wall.leadsTo);
                    if (other == null)
                    {
                        // e.g. the workshop doorway leadsTo "exit" — not a room;
                        // the dungeon exit is its own flow (ExitToVillage).
                        FlowTrace.Warn("Dungeon",
                            $"DressTraversalLinks: doorway in '{room.id}' leadsTo " +
                            $"'{wall.leadsTo}' which is not a room — no port authored (un-keyable).");
                        continue;
                    }

                    // Doorway midpoint + the wall's perpendicular, signed into
                    // each room (toward that room's footprint centre).
                    Vector3 s = wall.start.ToWorld(), e = wall.end.ToWorld();
                    Vector3 mid = (s + e) * 0.5f;
                    Vector3 dir = (e - s).normalized;
                    Vector3 n = new Vector3(dir.z, 0f, -dir.x);
                    Vector3 intoA = Vector3.Dot(n, room.bounds.Center - mid) >= 0f ? n : -n;

                    // Seat each side at that room's OWN door mesh (falls back to
                    // the JSON midpoint when no built doorway mesh is found).
                    Vector3 gapA = NearestDoorMeshXZ(room.id, mid, doorMeshByRoom, out bool haveA);
                    Vector3 gapB = NearestDoorMeshXZ(other.id, mid, doorMeshByRoom, out bool haveB);

                    // PROVE the offset (section 12): mesh gap vs the old JSON anchor.
                    FlowTrace.Step("Dungeon",
                        $"DoorAlign '{room.id}<->{other.id}': jsonMid={mid:F2} " +
                        $"gapA[{(haveA ? "mesh" : "fallback")}]={gapA:F2} " +
                        $"gapB[{(haveB ? "mesh" : "fallback")}]={gapB:F2} " +
                        $"deltaA={(gapA - mid).magnitude:F2} deltaB={(gapB - mid).magnitude:F2}.");

                    float yA = LevelYFor(room), yB = LevelYFor(other);
                    Vector3 posA = gapA + intoA * 1.5f + Vector3.up * yA;
                    Vector3 posB = gapB - intoA * 1.5f + Vector3.up * yB;

                    BuildPair($"Door_{room.id}__{other.id}", "Open Door",
                        posA, room.id, posB, other.id);
                }
            }

            // ── STAIRS — the builder's three authored vertical connectors
            //    (no layout data; Healer's Cottage table only). ────────────────
            if (Layout.id == "healers-cottage")
            {
                // stairs_long @ (-2,-6,-2): Main Room (ground) <-> Root Cellar;
                // the cellar-entry affordance is dressed at root-cellar (-18,·,0).
                BuildPair("Stairs_main-room__root-cellar", "Climb",
                    new Vector3(-2f, 0f, -2f), "main-room",
                    new Vector3(-18f, -6f, 0f), "root-cellar");

                // stairs_wood @ (0,0,-4): Main Room (ground) <-> Loft Bedroom (Y6).
                BuildPair("Stairs_main-room__loft-bedroom", "Climb",
                    new Vector3(0f, 0f, -4f), "main-room",
                    new Vector3(2f, 6f, -4f), "loft-bedroom");

                // stairs_narrow @ (-12,-6,5): Entrance Room trapdoor <-> Root Cellar.
                BuildPair("Stairs_entrance-room__root-cellar", "Climb",
                    new Vector3(-12f, 0f, 5f), "entrance-room",
                    new Vector3(-13f, -6f, 5f), "root-cellar");
            }
            else
            {
                FlowTrace.Warn("Dungeon",
                    $"DressTraversalLinks: no stair table for dungeon '{Layout.id}' — " +
                    "stairs get no ports until authored (doors still dressed from the layout).");
            }

            FlowTrace.Step("Dungeon",
                $"DressTraversalLinks: {built} traversal pair(s) authored ({built * 2} port links).");
        }

        /// <summary>
        /// Buckets every built <c>wall_doorway</c> mesh in the scene by the room
        /// it lives under (walking parents to the enclosing <c>Room_&lt;id&gt;</c>
        /// node). Used to seat each door port at the visible gap rather than the
        /// JSON midpoint (the two authorings drift — see DressTraversalLinks).
        /// </summary>
        private System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Transform>> CollectDoorMeshes()
        {
            var byRoom = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Transform>>();
            Guard.Try("Dungeon", "collect door meshes", () =>
            {
                foreach (var t in Object.FindObjectsByType<Transform>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t == null) continue;
                    string n = t.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    // The KayKit doorway piece instantiates as "wall_doorway";
                    // exclude illusory walls and our own PortLink markers.
                    if (n.IndexOf("doorway", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (n.StartsWith("[ILLUSORY]", System.StringComparison.Ordinal)) continue;
                    if (n.StartsWith("PortLink", System.StringComparison.Ordinal)) continue;

                    string rid = OwningRoomId(t);
                    if (rid == null) continue;
                    if (!byRoom.TryGetValue(rid, out var list))
                    {
                        list = new System.Collections.Generic.List<Transform>();
                        byRoom[rid] = list;
                    }
                    list.Add(t);
                }
            });
            return byRoom;
        }

        /// <summary>
        /// The XZ (floor-plane, y=0) of the door mesh in <paramref name="roomId"/>
        /// nearest <paramref name="near"/>; <paramref name="found"/> is false and
        /// the JSON point is returned when the room has no built doorway mesh.
        /// </summary>
        private Vector3 NearestDoorMeshXZ(string roomId, Vector3 near,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Transform>> byRoom,
            out bool found)
        {
            found = false;
            Vector3 result = new Vector3(near.x, 0f, near.z);
            if (byRoom != null && byRoom.TryGetValue(roomId, out var list) && list != null)
            {
                float best = float.MaxValue;
                foreach (var t in list)
                {
                    if (t == null) continue;
                    Vector3 p = t.position;
                    float dx = p.x - near.x, dz = p.z - near.z;
                    float d = dx * dx + dz * dz;
                    if (d < best)
                    {
                        best = d;
                        result = new Vector3(p.x, 0f, p.z);
                        found = true;
                    }
                }
            }
            return result;
        }

        /// <summary>Walks up from <paramref name="t"/> to the enclosing
        /// <c>Room_&lt;id&gt;</c> node and returns its id, or null if none.</summary>
        private static string OwningRoomId(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.name != null && p.name.StartsWith("Room_", System.StringComparison.Ordinal))
                    return p.name.Substring(5);
            }
            return null;
        }

        /// <summary>
        /// Hides leftover WHITE placeholder primitive boxes — DungeonSceneBuilder's
        /// MakePlaceholderCube fallback for a KayKit prop whose mesh failed to load
        /// (owner F8: "white placeholder cube on the dungeon floor"). Only NEAR-WHITE
        /// primitive cubes named "[PLACEHOLDER] ..." are swept; the deliberately-
        /// tinted stand-ins (hearth/rug/water) render in colour and are LEFT ALONE.
        /// Runtime-only (no scene edit); idempotent (a re-imported mesh leaves no box).
        /// <para>
        /// WO-1047 INSTRUMENTATION (§12 — this sweeper is the ticket's cheapest branch). The owner
        /// reported an untextured ORANGE cube that the reticle locked onto. This sweep already
        /// exists and did NOT hide it, and the three possible reasons are indistinguishable without
        /// data: (1) it is a placeholder this filter MISSED, (2) it is authored/tinted and correctly
        /// skipped, (3) it was spawned AFTER this one-shot sweep ran. So the sweep now CENSUSES every
        /// active primitive-Cube renderer in the dungeon and NAMES the ones it does not hide, with
        /// colour, hierarchy path, layer and component list. One run identifies the cube; nothing
        /// about the sweep's BEHAVIOUR changes (the same boxes are hidden as before).
        /// </para>
        /// </summary>
        private void SweepPlaceholderCubes()
        {
            using var _flow = FlowTrace.Enter("Dungeon", "SweepPlaceholderCubes");
            int hidden = 0;
            int cubesSeen = 0;
            int cubesLogged = 0;
            const int CubeLogCap = 16;   // bounded: a census, not a firehose
            Guard.Try("Dungeon", "sweep placeholder cubes", () =>
            {
                foreach (var t in Object.FindObjectsByType<Transform>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t == null || !t.gameObject.activeSelf) continue;

                    bool named = t.name.StartsWith("[PLACEHOLDER]", System.StringComparison.Ordinal);

                    // Only raw primitive boxes (built-in "Cube" mesh) — never a real FBX.
                    var mf = t.GetComponent<MeshFilter>();
                    bool primitiveCube = mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Cube";

                    // Keep the tinted stand-ins; hide only the untinted white/magenta boxes.
                    var r = t.GetComponent<Renderer>();
                    Color c = Color.white;
                    string shader = "(no renderer)";
                    if (r != null && r.sharedMaterial != null)
                    {
                        var m = r.sharedMaterial;
                        shader = m.shader != null ? m.shader.name : "(null shader)";
                        if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
                        else if (m.HasProperty("_Color")) c = m.color;
                    }
                    bool nearWhite = c.r > 0.85f && c.g > 0.85f && c.b > 0.85f;

                    // WO-1047 census: NAME every bare primitive cube standing in the dungeon,
                    // swept or not. The unidentified orange cube is a primitive cube by the
                    // owner's screenshot, so whatever spawns it lands in this list.
                    if (primitiveCube)
                    {
                        cubesSeen++;
                        if (cubesLogged < CubeLogCap)
                        {
                            cubesLogged++;
                            FlowTrace.Step("Dungeon",
                                $"[cube-census] '{DescribePath(t)}' rgb=({c.r:F2},{c.g:F2},{c.b:F2}) " +
                                $"shader='{shader}' layer='{LayerMask.LayerToName(t.gameObject.layer)}' " +
                                $"tag='{t.gameObject.tag}' pos={t.position:F2} scale={t.lossyScale:F2} " +
                                $"namedPlaceholder={named} willHide={(named && nearWhite)} " +
                                $"components=[{DescribeComponents(t.gameObject)}] " +
                                $"children=[{DescribeChildren(t)}]");
                        }
                    }

                    if (!named) continue;

                    if (!primitiveCube)
                    {
                        FlowTrace.Step("Dungeon",
                            $"SweepPlaceholderCubes: SKIP '{t.name}' - named [PLACEHOLDER] but mesh is " +
                            $"'{(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "(none)")}', not the " +
                            "built-in Cube (a real FBX resolved, or a non-mesh stand-in).");
                        continue;
                    }

                    if (!nearWhite)
                    {
                        // WO-1047: the ORANGE cube would land HERE if it is a named placeholder —
                        // this filter keeps tinted stand-ins ON PURPOSE. Say so out loud with the
                        // colour, so "it survived the sweep" stops being a mystery.
                        FlowTrace.Step("Dungeon",
                            $"SweepPlaceholderCubes: SKIP '{DescribePath(t)}' - TINTED placeholder cube " +
                            $"rgb=({c.r:F2},{c.g:F2},{c.b:F2}) at {t.position:F2}; the sweep hides only " +
                            "near-white boxes, so a deliberately-tinted stand-in is LEFT ALONE by design.");
                        continue;
                    }

                    FlowTrace.Step("Dungeon",
                        $"SweepPlaceholderCubes: hiding white placeholder '{t.name}' at " +
                        $"{t.position:F2} (missing KayKit mesh -> default-material box).");
                    t.gameObject.SetActive(false);
                    hidden++;
                }
            });
            FlowTrace.Step("Dungeon",
                $"SweepPlaceholderCubes: {hidden} white placeholder box(es) hidden; " +
                $"{cubesSeen} primitive cube(s) present in the dungeon ({cubesLogged} censused). " +
                "NOTE (WO-1047): this sweep is ONE-SHOT at hydration - anything spawned later is " +
                "structurally unreachable by it.");
        }

        // ── WO-1047 diagnostic formatters (§12) ──────────────────────────────
        // Small, allocation-tolerant describers used ONLY by instrumentation. They exist so a
        // captured line NAMES an object instead of describing it ("a prop", "an orange cube").

        private static string DescribePath(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new System.Text.StringBuilder(t.name);
            var p = t.parent;
            int guard = 0;
            while (p != null && guard++ < 12)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        private static string DescribeComponents(GameObject go)
        {
            if (go == null) return "(null)";
            var comps = go.GetComponents<Component>();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) { sb.Append(sb.Length > 0 ? ", " : "").Append("(missing script)"); continue; }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(comps[i].GetType().Name);
            }
            return sb.ToString();
        }

        private static string DescribeChildren(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new System.Text.StringBuilder();
            int n = Mathf.Min(t.childCount, 8);
            for (int i = 0; i < n; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t.GetChild(i) != null ? t.GetChild(i).name : "(null)");
            }
            if (t.childCount > n) sb.Append(", +").Append(t.childCount - n).Append(" more");
            return sb.Length == 0 ? "(none)" : sb.ToString();
        }

        // ── Audio ────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the looping dungeon ambient BGM (echoes-beneath-elarion) at the
        /// audio-mix-spec §2 volume (0.25). Guards a missing clip: the MP3 may not
        /// be imported yet — when no clip is present this logs a warning and the
        /// dungeon plays silently rather than erroring (port spec Week 5 note).
        /// </summary>
        private void StartAmbientAudio()
        {
            if (_ambientBgm == null) return;

            _ambientBgm.loop = true;
            _ambientBgm.playOnAwake = false;
            _ambientBgm.volume = _ambientBgmVolume;

            // Prefer the explicitly-assigned clip; otherwise fall back to any
            // clip already on the AudioSource (the scene builder may wire it
            // there directly once the MP3 is imported).
            if (_ambientBgmClip != null)
                _ambientBgm.clip = _ambientBgmClip;

            if (_ambientBgm.clip == null)
            {
                FlowTrace.Warn("Dungeon",
                    "StartAmbientAudio: ambient clip 'echoes-beneath-elarion' is not assigned — " +
                    "the MP3 is not yet imported under Assets/Audio/. Dungeon plays silently; " +
                    "wire the clip when the file lands.");
                return;
            }

            if (!_ambientBgm.isPlaying) _ambientBgm.Play();
        }

        /// <summary>Stops the ambient BGM on dungeon exit.</summary>
        private void StopAmbientAudio()
        {
            if (_ambientBgm != null && _ambientBgm.isPlaying) _ambientBgm.Stop();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Produces the per-run seed for the deterministic encounter sequence.
        /// A fresh, non-zero seed each run; v1.1 can swap this for a save-derived
        /// value so a run is fully reproducible.
        /// </summary>
        private static int MakeRunSeed()
        {
            int seed = System.Environment.TickCount ^ (int)(Time.realtimeSinceStartup * 1000f);
            return seed == 0 ? 1 : seed;
        }

        /// <summary>The interactable parent transforms — read by the scene builder.</summary>
        public IReadOnlyList<Transform> InteractableRoots =>
            new[] { _loreStoneRoot, _checkpointRoot, _encounterRoot };
    }
}
