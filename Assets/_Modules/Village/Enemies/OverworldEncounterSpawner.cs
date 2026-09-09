// =============================================================================
// OverworldEncounterSpawner — the OPEN-WORLD HOOK for the WO-482 encounter loop.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Owner design 2026-06-23: the open world holds cheap wandering "rep" mobs (a
// single orc that REPRESENTS a family). On ENGAGE -- the mob lands on the hero OR
// the hero attacks the mob -- we POP into the isolated real-time BattleArena where
// the FULL family is staged. The rep itself does NOT fight in-world (hook only):
// it wanders, and on AGGRO it CHASES with a wide leash at ~+5% the hero's speed
// (so a too-tough mob can't be outrun -- the danger-gradient stake) under a
// "they see us" chase-music sting.
//
// REUSE (CLAUDE.md "use items we have"): EnemyFactory builds the rep body (orc
// model + OrcHumanoid rig, WO-482 Slice 1) with ZERO contact damage; EnemyBrain +
// the Enemy hero-aggro (DEF-224) give it the wander/chase for free. The transition
// is the generic BattleArena.BeginEncounter (the isolated open kite arena).
//
// Self-bootstrapping DDOL singleton, FLAG-GATED by FeatureFlags.OverworldEncounter
// (default OFF -- dormant until the vertical is felt-verified). Instrumented per
// CLAUDE.md S12. ASCII logs; LogWarning, never error.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;                 // world-space threat nameplate ("!" alert) on the engaging rep
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Arena;
using DeNelle.Village.UI;             // Billboard — keeps the rep's threat cue facing the camera

namespace DeNelle.Village
{
    /// <summary>Spawns wandering orc "rep" mobs in the open world; engaging one pops into the BattleArena.</summary>
    public sealed class OverworldEncounterSpawner : MonoBehaviour
    {
        public static OverworldEncounterSpawner Instance { get; private set; }

        private const string OuterWorldScene = "Main_Castle_Overworld";  // WO-608: OuterWorld merged into Main_Castle_Overworld
        // Roster POOLS rolled into variable packs (1–7 bodies) at spawn/record time — not fixed trios.
        private static readonly string[] OrcPool = { "orc-warrior", "orc-tank", "orc-mage" };
        private const int PackSizeMin = 1;
        private const int PackSizeMax = 7;

        // Owner balance (2026-07-16): a hero UNDER LowLevelThreshold is never swarmed — every
        // encounter that stages against her is capped to LowLevelEnemyCap concurrent enemies.
        // At LowLevelThreshold+ the existing (uncapped) behaviour stands. Shared by the family
        // roll below and read by BattleArena / CampGuards through CurrentHeroLevel().
        internal const int LowLevelThreshold = 5;
        internal const int LowLevelEnemyCap  = 3;

        /// <summary>Current hero level (HeroProgression live authority; GameState heroLevel save
        /// field v29 as fallback; 1 if neither is up yet).</summary>
        internal static int CurrentHeroLevel()
        {
            if (HeroProgression.Instance != null) return HeroProgression.Instance.Level;
            var s = DeNelle.Core.State.GameStateService.Instance?.State;
            return s != null ? Mathf.Max(1, s.HeroLevel) : 1;
        }

        // Rep tuning. Wide aggro + a chase a touch faster than the hero (~6 base) so it
        // "means something" if you wandered into one too strong. Contact damage ZERO
        // (hook, not a combatant) -- engagement, not death, is what the rep delivers.
        private const float RepChaseSpeed = 6.3f;   // ~+5% over the hero's 6.0
        // CONCURRENT roaming rep count. Owner 2026-06-24 FELT: "drop those 20 spawns down to like 6"
        // / "there are a lot" — the world holds ~6 reps at once (was 8), NOT a 20-up-front swarm.
        // Nudge this to retune crowding.
        private const int   RepCount      = 6;   // owner 2026-06-24: concurrent reps (was 8) — keep the world populated, not crowded

        // RESPAWN/MAINTAIN tuning (owner 2026-06-24 "just set a respawn"): reps are CONSUMED when
        // engaged (Engage() destroys the rep -> the family fights in the BattleArena), so without a
        // maintain loop the world depletes to empty. A repeating maintain loop re-tops the world back
        // to RepCount, but only after a delay so a fresh replacement doesn't pop in instantly on top of
        // the hero. RespawnCheckInterval = how often we re-evaluate the live count; tune both.
        private const float RespawnCheckInterval = 10f;  // owner 2026-06-24: re-top-up cadence (~8-15s feel)

        // BUFFER tuning (owner 2026-06-24 FELT values — dial these): give the hero room to cross
        // out of the castle and walk a bit into the overworld before any rep is on top of her.
        // SpawnMinDistance/SpawnMaxDistance = the ring (around the hero) reps spawn into. Raising
        // the MIN pushes reps DEEPER so a freshly-crossed hero isn't aggro'd right at the seam.
        private const float SpawnMinDistance = 28f;  // was 14f — push reps deeper into the overworld
        private const float SpawnMaxDistance = 55f;  // ring outer edge (unchanged)

        // ── SCATTER RECORDS (F8-8, owner directive) ────────────────────────────────
        // "Random enemy families around the map, only need instantiated when within
        // sight" + "further from castle is harder levels". A SEEDED, persistent set of
        // scatter RECORDS (anchor + family + level) is generated ONCE per session across
        // distance BANDS from the world origin (the castle). Records are pure data —
        // the actual rep GameObject is only instantiated when the hero comes within
        // ScatterActivateRadius (sight), and is culled (record kept) past ScatterCullRadius.
        // Bounded per ARCHITECTURE_PRINCIPLES §2b.1: at most ScatterLiveCap live at once.
        // The owner-felt-tuned hero-ring reps above are UNCHANGED — this layer is additive.
        private const int    ScatterRecordCount    = 18;    // total records across all bands (~6 per band)
        private const float  ScatterBandNearMin    = 60f;   // EASY band: 60-120m from origin
        private const float  ScatterBandNearMax    = 120f;
        private const float  ScatterBandMidMin     = 120f;  // MID band: 120-200m
        private const float  ScatterBandMidMax     = 200f;
        private const float  ScatterBandFarMin     = 200f;  // HARD band: 200-320m
        private const float  ScatterBandFarMax     = 320f;
        private const float  ScatterActivateRadius = 85f;   // hero within this of a record -> instantiate its rep
        private const float  ScatterCullRadius     = 115f;  // hero beyond this -> destroy the live rep (record stays)
        private const float  ScatterRespawnSeconds = 180f;  // killed/engaged record respawn cooldown
        private const int    ScatterLiveCap        = 8;     // max concurrently-live scatter reps (bounded law)
        private const int    ScatterSeed           = 20260707; // FIXED deterministic seed (never wall-clock)
        private const int    ScatterPlaceAttempts  = 24;    // candidate rolls per record before giving up

        // Band levels (floor — the record level is max(band, ZoneManager.ThreatLevel at the anchor)).
        private const int ScatterNearLevel = 1;
        private const int ScatterMidLevel  = 2;
        private const int ScatterFarLevel  = 3;

        // Band pools (composition rolled to 1–7 at record generation). Every id is VERIFIED
        // through the engage path (BattleArena.BuildEncounterDef + EnemyFactory model map).
        // NOTE: "hollow-apprentice" exists only in the ATB stack, NOT in EnemyFactory.
        private static readonly string[] ScatterNearPool      = { "orc-warrior", "orc-tank", "orc-mage" };
        private static readonly string[] ScatterMidOrcPool    = { "orc-tank", "orc-warrior", "orc-mage" };
        private static readonly string[] ScatterMidHollowPool = { "hollow-warrior", "hollow-rogue", "hollow-acolyte" };
        private static readonly string[] ScatterFarHollowPool = { "hollow-warrior", "hollow-rogue", "hollow-acolyte" };
        private static readonly string[] ScatterFarOrcPool    = { "orc-tank", "orc-warrior", "orc-mage" };

        /// <summary>One persistent scatter record — pure data; the rep GameObject only
        /// exists while the hero is within sight (ScatterActivateRadius).</summary>
        private sealed class ScatterRecord
        {
            public int      Index;
            public int      Band;          // 0 near / 1 mid / 2 far
            public Vector3  Anchor;
            public string[] FamilyIds;
            public int      Level;
            public string   ArenaPreset;
            public bool     Alive = true;  // false while on the post-kill respawn cooldown
            public float    RespawnAt;     // Time.time the record comes back Alive
            public GameObject Live;        // the instantiated rep (null while dormant)
            public bool     Spawned;       // true while Live was spawned by us (distinguishes kill from cull)
            public bool     WarnedUnreachable; // one-shot log guard for a no-path anchor
        }

        private readonly List<ScatterRecord> _scatter = new List<ScatterRecord>();
        private bool _scatterGenerated;

        private readonly List<GameObject> _reps = new List<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject("OverworldEncounterSpawner").AddComponent<OverworldEncounterSpawner>();
        }

        private bool _populating;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // The overworld may ALREADY be loaded additively (the active scene is MainCastle_Hall,
            // the overworld streams in over it via WorldSceneLoader) by the time this DDOL singleton
            // boots — the per-scene sceneLoaded callback won't re-fire for an already-loaded scene.
            // So evaluate the WHOLE loaded set now, not just the active scene (the old bug: this
            // checked only GetActiveScene() == "Main_Castle_Overworld", which is FALSE in MainCastle_Hall, so
            // reps never spawned in the live additive setup).
            // SPAWN-INTENT PREWARM (owner report 2026-08-20: "One enemy came in as a pill then
            // switched to enemy"). This spawner's rosters are FIXED static pools, known here at
            // boot, while the reps themselves are not built until the hero walks out and the
            // gates open — seconds to minutes later. Asking for those families NOW means the
            // bundle is usually already resident by the time a body is skinned, which is the only
            // honest way to shrink the placeholder window (the window IS download latency; the
            // captured arrivals ran 0.6s–6.4s, and blocking to wait is what deadlocked the device).
            // Bounded + per-family: exactly the handful of models these pools can produce, never
            // the whole enemy set.
            EnemyFactory.PrewarmForIds(new[]
            {
                "orc-warrior", "orc-tank", "orc-mage",
                "hollow-warrior", "hollow-rogue", "hollow-acolyte"
            });

            MaybePopulate();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // CHAIN CLEANUP (owner 2026-06-30): reps are DontDestroyOnLoad and would otherwise be
            // carried into a single-loaded chain scene (Outpost1/Dungeon/Outpost2) where they don't
            // belong. If the overworld is no longer loaded, despawn them so the outpost reads clean
            // (owner: "if easier and cleaner they can go"). They re-populate via MaybePopulate when
            // the overworld loads again.
            if (!OuterWorldLoaded()) DespawnAllReps();
            MaybePopulate();
        }

        // Destroy + forget every live rep (used when leaving the overworld into a chain scene).
        private void DespawnAllReps()
        {
            int n = 0;
            for (int i = 0; i < _reps.Count; i++)
                if (_reps[i] != null) { Destroy(_reps[i]); n++; }
            _reps.Clear();
            // Scatter layer (F8-8): cull live scatter reps too — the RECORDS persist (pure
            // data), so they re-activate on sight when the hero returns to the overworld.
            for (int i = 0; i < _scatter.Count; i++)
            {
                var rec = _scatter[i];
                if (rec.Live != null) { Destroy(rec.Live); n++; }
                rec.Live = null; rec.Spawned = false;
            }
            if (n > 0)
                FlowTrace.Step("Encounter",
                    $"DespawnAllReps: cleared {n} carried rep(s) (OuterWorld not loaded — chain scene); re-populate on return.");
        }

        // True when the overworld is loaded (active OR additive), case-insensitive — mirrors
        // RaidOutpostSystem.InOuterWorld so the rep gate matches the other world systems.
        internal static bool OuterWorldLoaded()
        {
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                // WO-608 merge: HubScenes.IsOverworld matches legacy "OuterWorld" AND the
                // merged "Main_Castle_Overworld" (the "OuterWorld" vs "Overworld" naming trap).
                if (s.isLoaded && HubScenes.IsOverworld(s.name))
                    return true;
            }
            return false;
        }

        private void MaybePopulate()
        {
            if (!FeatureFlags.OverworldEncounter) { FlowTrace.Step("Encounter", "MaybePopulate: ff.overworldencounter OFF — dormant."); return; }
            if (BattleArena.Instance != null && BattleArena.Instance.BattleInProgress) return; // not while a battle is up
            if (!OuterWorldLoaded())                                  // v1: overworld only
            {
                FlowTrace.Step("Encounter", "MaybePopulate: overworld not loaded yet — waiting for its sceneLoaded.");
                return;
            }
            if (_populating) return;                                  // a populate is already scheduled

            // Stagger off the scene-load frame (mirrors RaidOutpostSystem) so the rep
            // realizes after the world + navmesh are up.
            _populating = true;
            StartCoroutine(PopulateAfterDelay());
        }

        private System.Collections.IEnumerator PopulateAfterDelay()
        {
            yield return new WaitForSeconds(3f);
            _populating = false;

            // The hero spawns in MainCastle_Hall and WARPS into OuterWorld later (SceneTransitionTrigger).
            // If reps were anchored to the hero's CASTLE position they'd strand 26m+ from where the hero
            // actually walks out — "too far, they do not engage". Wait until the hero is actually standing
            // IN the OuterWorld region before anchoring the reps to its current position.
            float waited = 0f;
            while (waited < 30f && !HeroInOuterWorld())
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
            }

            _reps.RemoveAll(r => r == null);   // drop stale references (scene change destroyed them)
            if (!HeroInOuterWorld())
            {
                FlowTrace.Warn("Encounter", "PopulateAfterDelay: hero not in OuterWorld after 30s — anchoring reps to world origin (will re-anchor on next OuterWorld load).");
            }
            int spawned = 0;
            for (int i = _reps.Count; i < RepCount; i++) { SpawnRep(i); spawned++; }
            FlowTrace.Step("Encounter", $"PopulateAfterDelay: ensured {_reps.Count}/{RepCount} reps live (spawned {spawned} this pass).");

            // RESPAWN/MAINTAIN: keep the world topped at RepCount. Reps are CONSUMED on engage
            // (Engage() Destroy()s them), so this loop replaces any that died/engaged after a delay
            // (RespawnCheckInterval) — the world stays populated at ~RepCount without a 20-up-front
            // swarm. Idempotent: only one maintain loop runs (guarded by _maintaining).
            if (!_maintaining)
            {
                _maintaining = true;
                StartCoroutine(MaintainLoop());
            }
        }

        private bool _maintaining;

        // F8-8 probe: last MaintainLoop blocking gate — logged on change only. Starts at a
        // sentinel so the FIRST tick always logs its state (clear or gated).
        private string _lastMaintainGate = "<never-ticked>";

        // Perpetual top-up: every RespawnCheckInterval, while OuterWorld is loaded + no battle is
        // staged + the hero is in OuterWorld, re-spawn replacements until the live count is back at
        // RepCount. The respawn DELAY is the interval itself (a consumed rep is replaced on the next
        // tick, not instantly), so a replacement never pops in on top of a freshly-returned hero.
        // Spawns stay spread out — SpawnRep scatters each onto a random reachable ring point.
        private System.Collections.IEnumerator MaintainLoop()
        {
            var wait = new WaitForSeconds(RespawnCheckInterval);
            while (true)
            {
                yield return wait;

                // SILENT-GATE INSTRUMENTATION (F8-8 probe): these four gates used to skip the
                // tick with NO trace, so "scatter never generates" was undiagnosable from a
                // capture. Name the blocking gate ON CHANGE only (never per-tick spam).
                string gate = null;
                if (!FeatureFlags.OverworldEncounter) gate = "flag-off (ff.overworldencounter)";                 // dormant
                else if (BattleArena.Instance != null && BattleArena.Instance.BattleInProgress) gate = "battle-in-progress"; // not mid-battle
                else if (!OuterWorldLoaded()) gate = "overworld-not-loaded";                                     // OuterWorld only
                else if (!HeroInOuterWorld()) gate = "hero-not-in-outer-roster-zone";                            // anchor to the hero only once she is out
                if (gate != _lastMaintainGate)
                {
                    _lastMaintainGate = gate;
                    FlowTrace.Step("Encounter", gate == null
                        ? "MaintainLoop gates CLEAR — ticking (scatter upkeep + ring top-up run)."
                        : $"MaintainLoop gated: {gate} (logged on change).");
                }
                if (gate != null) continue;

                // SCATTER LAYER (F8-8): generate the seeded records on the first eligible tick
                // (navmesh is up by now — this loop starts 3s+ after the world load), then each
                // tick activate/cull records by hero SIGHT distance. Shares the gates above.
                Guard.Try("Encounter", "scatter maintain", MaintainScatter);

                _reps.RemoveAll(r => r == null);   // drop consumed/destroyed reps
                if (_reps.Count >= RepCount) continue;

                int spawned = 0;
                for (int i = _reps.Count; i < RepCount; i++) { SpawnRep(i); spawned++; }
                if (spawned > 0)
                    FlowTrace.Step("Encounter", $"respawn rep -> {_reps.Count}/{RepCount} live (respawned {spawned} this tick).");
            }
        }

        // The hero is "in" OuterWorld once it is physically inside an outer region (ZoneManager
        // classifies its position into a roster region) — i.e. it has crossed out of the castle/
        // village footprint. Until then, anchoring reps to the hero would place them in the castle.
        private static bool HeroInOuterWorld()
        {
            var hero = GameObject.FindWithTag("Player");
            if (hero == null) return false;
            bool outside = false;
            Guard.Try("Encounter", "hero-in-world check",
                () => outside = DeNelle.Core.World.RegionSpawnTable.HasRoster(
                                    DeNelle.Core.World.ZoneManager.GetZone(hero.transform.position)));
            return outside;
        }

        // FTUE GUARD (F8 2026-07-08 "died in tutorial — nothing should spawn"): while the first-time
        // tutorial is active, NO ambient rep may instantiate — engaging one pops the BattleArena where
        // the hero can die mid-tutorial. Bypassed by _testMode so the fleet oracles (ForcePopulateForTest
        // / EnsureMaintainLoopForTest) still drive the real spawn path on a fresh (pre-onboard) save.
        // Lifts automatically when onboarding completes (TutorialFlow.HostilesSuppressedForTutorial).
        private bool _testMode;

        private void SpawnRep(int index)
        {
            if (!_testMode && TutorialFlow.HostilesSuppressedForTutorial)
            {
                FlowTrace.Step("Encounter", $"suppressed ring rep #{index} — tutorial (FTUE) active.");
                return;
            }
            var hero = GameObject.FindWithTag("Player");
            Vector3 origin = hero != null ? hero.transform.position : Vector3.zero;
            if (hero == null)
                FlowTrace.Warn("Encounter", $"SpawnRep #{index}: no 'Player'-tagged hero found — anchoring rep to world origin (it may strand far from the player).");

            // SCATTER (owner 2026-06-23 "20 random ones roaming everywhere"): each rep takes a RANDOM
            // reachable navmesh point in a ring around the hero, so they populate the world and you can
            // always bump into one. Validate PathComplete (up to 8 tries) so a rep never strands on an
            // island across the seam. Each rep then ROAMS its leash (RepEngageWatcher) until it sees you,
            // then chases. (Replaces the old single-rep courtyard placement; THIS is the spread.)
            // CASTLE = SAFE (owner 2026-06-23): a rep may ONLY spawn on an OuterWorld roster region,
            // never inside the castle/Village footprint (enemies can't reliably traverse the seam
            // navmesh). The anchor starts UNSET -- it is ONLY assigned from a candidate that PASSES the
            // HasRoster zone gate. If the 8-try loop finds none, we DO NOT SPAWN (no castle-side
            // fall-through). This keeps the castle a safe shop/gear haven; the chase begins only once
            // the hero has crossed into OuterWorld.
            // ===== V2 TODO (owner wants to RESOLVE this, not now) =====
            // The castle-safe rule is currently a WORKAROUND for a navmesh limitation: enemy
            // agents don't reliably path ACROSS the RegionGate seam (separate navmesh islands +
            // the hero warp-crossing, not an agent-walkable link). V2: stitch/link the navmesh
            // across the seam (NavMeshLink the agents actually traverse) so reps CAN pursue the
            // hero between regions -- then "castle = safe" becomes a deliberate DESIGN choice
            // (e.g. a warded threshold), not a tech limitation, and this OuterWorld-only spawn
            // gate + the chase-stalls-at-seam behaviour can be lifted/retuned.
            Vector3 anchor = Vector3.zero;
            bool anchorFound = false;
            if (hero != null)
            {
                var path = new UnityEngine.AI.NavMeshPath();
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float a = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float dist = UnityEngine.Random.Range(SpawnMinDistance, SpawnMaxDistance);
                    Vector3 cand = origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out var ch, 8f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    // MOAT EXCLUSION: never anchor a rep in the castle moat water / RegionGate seam
                    // band — re-roll. The moat hides the seam, so this also keeps reps off the cut.
                    if (MoatExclusion.IsInMoatBand(ch.position)) continue;
                    bool inOuter = false;
                    Guard.Try("Encounter", "rep zone gate", () => inOuter =
                        DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(ch.position)));
                    if (!inOuter) continue;
                    if (UnityEngine.AI.NavMesh.CalculatePath(origin, ch.position, UnityEngine.AI.NavMesh.AllAreas, path)
                        && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                    { anchor = ch.position; anchorFound = true; break; }
                }
            }

            // NO castle-side fall-through: if no OuterWorld-side candidate cleared the zone gate in 8
            // tries (e.g. the hero is still in/near the castle), SKIP this spawn so the castle stays safe.
            if (!anchorFound)
            {
                FlowTrace.Warn("Encounter", $"SpawnRep #{index}: no OuterWorld-side candidate in 8 tries -> skipping (castle stays safe).");
                return;
            }

            // Belt-and-suspenders (data 2026-06-23): snap the anchor onto the baked navmesh so the
            // rep spawns walkable + can path to the hero. The terrain re-center (WO-483) puts a floor
            // under the play area; this guards the edges so a rep never lands in a no-navmesh pocket
            // (the old failure: "Failed to create agent because there is no valid NavMesh" / "no
            // COMPLETE path to hero"). If nothing's within 12m, log it LOUD rather than spawn a dead rep.
            if (UnityEngine.AI.NavMesh.SamplePosition(anchor, out var navHit, 12f, UnityEngine.AI.NavMesh.AllAreas))
                anchor = navHit.position;
            else
                FlowTrace.Warn("Encounter", $"SpawnRep #{index}: no navmesh within 12m of {anchor} — rep may be unreachable (check OuterWorld floor/bake).");

            // POST-SNAP CASTLE-SAFE RE-CHECK (owner 2026-06-23): the 12m navmesh snap above can drift
            // the anchor OFF its zone-gated candidate and back across the seam into the Village/castle
            // footprint. Re-confirm the FINAL position is still an OuterWorld roster region; if it
            // drifted into the castle, ABORT the spawn so a snapped point never leaks a rep castle-side.
            bool finalInOuter = false;
            Guard.Try("Encounter", "rep zone gate (post-snap)", () => finalInOuter =
                DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(anchor)));
            if (!finalInOuter)
            {
                FlowTrace.Warn("Encounter", $"SpawnRep #{index}: final anchor {anchor} snapped into a non-OuterWorld (castle/Village) region -> aborting spawn (castle stays safe).");
                return;
            }

            // POST-SNAP MOAT RE-CHECK: the 12m navmesh snap can also drift the anchor into the castle
            // moat water / RegionGate seam band — abort so a snapped point never leaks a rep into the
            // water or onto the seam (mirrors the castle-safe re-check above).
            if (MoatExclusion.IsInMoatBand(anchor))
            {
                FlowTrace.Warn("Encounter", $"SpawnRep #{index}: final anchor {anchor} snapped into the moat/seam band -> aborting spawn (no water/seam rep).");
                return;
            }

            // WO-606: pool/threat/preset from geotagged SpawnArea when authored; roll 1–7 bodies.
            string[] pool = OrcPool;
            int repThreat = ZoneThreatAt(anchor);
            string repPreset = null;
            if (DeNelle.Core.World.SpawnAreaTable.HasAny)
            {
                var draw = DeNelle.Core.World.SpawnAreaTable.BuildDraw(anchor);
                if (draw.Valid && draw.EnemyIds != null && draw.EnemyIds.Length > 0)
                {
                    pool = draw.EnemyIds;
                    repThreat = Mathf.Max(1, draw.Level);
                    repPreset = draw.ArenaPreset;
                }
            }
            string[] repFamily = RollFamilyPack(pool);

            GameObject pack = SpawnOverworldFamilyPack(
                anchor, repFamily, repThreat, repPreset, $"OrcRep_{index}", $"orc-rep-{index}");

            if (pack != null)
            {
                _reps.Add(pack);
                FlowTrace.Step("Encounter",
                    $"spawned family pack #{index} at {anchor} ({repFamily.Length} bodies, threat {repThreat}, +5% chase, 0 dmg).");
            }
        }

        // -----------------------------------------------------------------------------
        // TEST SEAM (WO-482 fleet oracle) — runs the SAME real spawn path MaybePopulate()
        // drives, but WITHOUT the flag/scene/already-populating gates and WITHOUT the
        // 3s+30s stagger waits (the oracle has already warped the hero into an OuterWorld
        // roster region + asserted navmesh). It ensures up to RepCount reps exist via the
        // real SpawnRep -> EnemyFactory -> RepEngageWatcher chain, so the oracle proves the
        // ACTUAL rep->engage->battle path, never a BeginEncounter bypass. ASCII-only.
        // -----------------------------------------------------------------------------
        public void ForcePopulateForTest()
        {
            _testMode = true;   // fleet oracle drives the REAL spawn path on a fresh save — bypass the FTUE guard
            _reps.RemoveAll(r => r == null);
            int spawned = 0;
            for (int i = _reps.Count; i < RepCount; i++) { SpawnRep(i); spawned++; }
            FlowTrace.Step("Encounter", $"ForcePopulateForTest: ensured {_reps.Count}/{RepCount} reps live (spawned {spawned} via real SpawnRep).");
        }

        // -----------------------------------------------------------------------------
        // F8-8 PROBE SEAMS (AutoPilot 'AssertScatterRecords') — read-only counters plus
        // one starter. The probe flips ff.overworldencounter ON mid-run, but MaybePopulate
        // gated on the flag at boot, so MaintainLoop (the only caller of MaintainScatter)
        // may never have started — EnsureMaintainLoopForTest starts the SAME production
        // MaintainLoop (no logic bypass; all its gates still apply every tick).
        // -----------------------------------------------------------------------------

        /// <summary>Scatter records generated this session (0 until the first eligible maintain tick).</summary>
        public int GeneratedScatterCount => _scatter.Count;

        /// <summary>Scatter records with a LIVE (activated) rep GameObject right now.</summary>
        public int LiveScatterCount
        {
            get { int n = 0; for (int i = 0; i < _scatter.Count; i++) if (_scatter[i].Live != null) n++; return n; }
        }

        /// <summary>Cumulative scatter ACTIVATE events this session (each pairs a 'scatter ACTIVATE' trace).</summary>
        public int ScatterActivations { get; private set; }

        /// <summary>Cumulative scatter CULL events this session (each pairs a 'scatter CULL' trace).</summary>
        public int ScatterCulls { get; private set; }

        /// <summary>Read a generated record's anchor + band (0 near / 1 mid / 2 far). False when out of range.</summary>
        public bool TryGetScatterAnchor(int index, out Vector3 anchor, out int band)
        {
            anchor = Vector3.zero; band = -1;
            if (index < 0 || index >= _scatter.Count) return false;
            anchor = _scatter[index].Anchor;
            band = _scatter[index].Band;
            return true;
        }

        /// <summary>Start the real MaintainLoop if it is not already running (probe runs flip the
        /// flag AFTER boot, when MaybePopulate has already declined). Idempotent.</summary>
        public void EnsureMaintainLoopForTest()
        {
            _testMode = true;   // fleet oracle drives the REAL maintain/scatter path on a fresh save — bypass the FTUE guard
            if (_maintaining) return;
            _maintaining = true;
            StartCoroutine(MaintainLoop());
            FlowTrace.Step("Encounter", "EnsureMaintainLoopForTest: maintain loop started (flag flipped after boot — probe run).");
        }

        // Light threat read from the world zone (reuses the shared classifier).
        private static int ZoneThreatAt(Vector3 pos)
        {
            int t = 1;
            Guard.Try("Encounter", "zone threat", () => t = Mathf.Max(1, DeNelle.Core.World.ZoneManager.ThreatLevel(pos)));
            return t;
        }

        // =====================================================================
        // SCATTER RECORDS LAYER (F8-8) — see the const block at the top.
        // Records are generated ONCE per session from a FIXED seed (deterministic
        // — never wall-clock), distributed across origin-distance bands; reps are
        // instantiated only on hero sight and culled beyond ScatterCullRadius.
        // =====================================================================

        /// <summary>Per-tick scatter upkeep: generate once, then activate/cull/respawn.
        /// Called from MaintainLoop AFTER its flag/scene/battle/hero gates.</summary>
        private void MaintainScatter()
        {
            if (!_scatterGenerated) { GenerateScatterRecords(); _scatterGenerated = true; }
            if (_scatter.Count == 0) return;

            var hero = GameObject.FindWithTag("Player");
            if (hero == null) return;
            Vector3 heroPos = hero.transform.position;

            // Pass 1 — reconcile: a record whose rep GameObject vanished WITHOUT us culling it
            // was killed in the field or consumed by an engage -> record goes on the respawn
            // cooldown. Also count the live reps for the cap.
            int live = 0;
            for (int i = 0; i < _scatter.Count; i++)
            {
                var rec = _scatter[i];
                if (rec.Spawned && rec.Live == null)
                {
                    rec.Spawned = false;
                    rec.Alive = false;
                    rec.RespawnAt = Time.time + ScatterRespawnSeconds;
                    FlowTrace.Step("Encounter",
                        $"scatter record #{rec.Index} CONSUMED (killed/engaged) -> respawn in {ScatterRespawnSeconds:0}s.");
                }
                if (rec.Live != null) live++;
            }

            // Pass 2 — respawn cooldowns, culls, activations.
            for (int i = 0; i < _scatter.Count; i++)
            {
                var rec = _scatter[i];

                if (!rec.Alive && Time.time >= rec.RespawnAt)
                {
                    rec.Alive = true;
                    FlowTrace.Step("Encounter", $"scatter record #{rec.Index} respawn cooldown elapsed -> alive again.");
                }

                float dist = Vector3.Distance(heroPos, rec.Anchor);

                // CULL: hero left sight range — destroy the body, keep the record.
                if (rec.Live != null && dist > ScatterCullRadius)
                {
                    Destroy(rec.Live);
                    rec.Live = null; rec.Spawned = false;
                    live--;
                    ScatterCulls++;   // F8-8 probe counter — pairs with the CULL trace below
                    FlowTrace.Step("Encounter",
                        $"scatter CULL #{rec.Index} (family '{rec.FamilyIds[0]}', lvl {rec.Level}) dist={dist:0}m > {ScatterCullRadius:0}m — record kept.");
                    continue;
                }

                // ACTIVATE: hero within sight of a dormant, alive record (bounded by the cap).
                if (rec.Alive && rec.Live == null && dist <= ScatterActivateRadius && live < ScatterLiveCap)
                {
                    // Reachability: only realize a rep the hero could actually meet — an
                    // island anchor would be a dead rep (same concern SpawnRep's PathComplete
                    // check covers, evaluated here at activation time when it's meaningful).
                    var path = new UnityEngine.AI.NavMeshPath();
                    bool reachable = UnityEngine.AI.NavMesh.CalculatePath(heroPos, rec.Anchor, UnityEngine.AI.NavMesh.AllAreas, path)
                                     && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                    if (!reachable)
                    {
                        if (!rec.WarnedUnreachable)
                        {
                            rec.WarnedUnreachable = true;
                            FlowTrace.Warn("Encounter",
                                $"scatter record #{rec.Index} at {rec.Anchor} has NO complete path from the hero — activation skipped (will retry).");
                        }
                        continue;
                    }
                    rec.WarnedUnreachable = false;

                    if (SpawnScatterRep(rec))
                    {
                        live++;
                        ScatterActivations++;   // F8-8 probe counter — pairs with the ACTIVATE trace below
                        FlowTrace.Step("Encounter",
                            $"scatter ACTIVATE #{rec.Index} band={rec.Band} family=[{string.Join(",", rec.FamilyIds)}] lvl={rec.Level} dist={dist:0}m (live {live}/{ScatterLiveCap}).");
                    }
                }
            }
        }

        /// <summary>Seeded one-time generation of the scatter records across the three
        /// origin-distance bands. Deterministic (fixed ScatterSeed + fixed world/navmesh);
        /// each candidate passes the SAME belt-and-suspenders validation SpawnRep uses
        /// (navmesh sample + moat exclusion + roster-zone gate + post-snap re-checks).</summary>
        private void GenerateScatterRecords()
        {
            var rng = new System.Random(ScatterSeed);   // FIXED seed — never Date.now/GetHashCode nondeterminism
            int[] bandCounts = new int[3];
            int placed = 0, failed = 0;

            for (int i = 0; i < ScatterRecordCount; i++)
            {
                int band = i % 3;   // even spread: 6 near / 6 mid / 6 far
                float min, max;
                switch (band)
                {
                    case 0:  min = ScatterBandNearMin; max = ScatterBandNearMax; break;
                    case 1:  min = ScatterBandMidMin;  max = ScatterBandMidMax;  break;
                    default: min = ScatterBandFarMin;  max = ScatterBandFarMax;  break;
                }

                bool found = false;
                Vector3 anchor = Vector3.zero;
                for (int attempt = 0; attempt < ScatterPlaceAttempts && !found; attempt++)
                {
                    float a = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                    float dist = min + (float)rng.NextDouble() * (max - min);
                    Vector3 cand = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist;   // origin = castle (0,0,0)

                    // Same validation chain as SpawnRep: navmesh -> moat -> roster zone -> snap -> re-checks.
                    if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out var ch, 8f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    if (MoatExclusion.IsInMoatBand(ch.position)) continue;
                    bool inOuter = false;
                    Guard.Try("Encounter", "scatter zone gate", () => inOuter =
                        DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(ch.position)));
                    if (!inOuter) continue;

                    Vector3 snapped = ch.position;
                    if (UnityEngine.AI.NavMesh.SamplePosition(snapped, out var navHit, 12f, UnityEngine.AI.NavMesh.AllAreas))
                        snapped = navHit.position;

                    // Post-snap re-checks (the snap can drift across the seam/moat — mirror SpawnRep).
                    bool finalInOuter = false;
                    Guard.Try("Encounter", "scatter zone gate (post-snap)", () => finalInOuter =
                        DeNelle.Core.World.RegionSpawnTable.HasRoster(DeNelle.Core.World.ZoneManager.GetZone(snapped)));
                    if (!finalInOuter) continue;
                    if (MoatExclusion.IsInMoatBand(snapped)) continue;

                    anchor = snapped;
                    found = true;
                }

                if (!found) { failed++; continue; }   // sparse band coverage — bounded, never force-place

                // POOL + LEVEL. DATA PATH FIRST (WO-606 seam): spawn-areas.json carries a roster
                // pool at this anchor; band constants are the F8-8 fallback. Each record rolls a
                // variable pack (1–7 bodies) from its pool so the world never reads as fixed trios.
                string[] pool = null; int level = 0; string preset = null;
                if (DeNelle.Core.World.SpawnAreaTable.HasAny)
                {
                    var draw = DeNelle.Core.World.SpawnAreaTable.BuildDraw(anchor);
                    if (draw.Valid && draw.EnemyIds != null && draw.EnemyIds.Length > 0)
                    {
                        pool = draw.EnemyIds;
                        level = Mathf.Max(1, draw.Level);
                        preset = draw.ArenaPreset;
                    }
                }
                if (pool == null)
                {
                    switch (band)
                    {
                        case 0:
                            pool = ScatterNearPool;
                            level = ScatterNearLevel;
                            break;
                        case 1:
                            pool = rng.Next(2) == 0 ? ScatterMidOrcPool : ScatterMidHollowPool;
                            level = ScatterMidLevel;
                            break;
                        default:
                            pool = rng.Next(3) == 0 ? ScatterFarOrcPool : ScatterFarHollowPool;
                            level = ScatterFarLevel;
                            break;
                    }
                    level = Mathf.Max(level, ZoneThreatAt(anchor));
                }
                string[] family = RollFamilyPack(pool, rng);

                _scatter.Add(new ScatterRecord
                {
                    Index = i, Band = band, Anchor = anchor,
                    FamilyIds = family, Level = level, ArenaPreset = preset,
                });
                bandCounts[band]++; placed++;
            }

            FlowTrace.Step("Encounter",
                $"GenerateScatterRecords: seed={ScatterSeed} placed {placed}/{ScatterRecordCount} " +
                $"(near {bandCounts[0]}, mid {bandCounts[1]}, far {bandCounts[2]}; {failed} found no valid ground) " +
                $"— sight-activated at {ScatterActivateRadius:0}m, culled at {ScatterCullRadius:0}m, cap {ScatterLiveCap}.");
        }

        /// <summary>Record-anchored variant of SpawnRep: SAME EnemyFactory.Build + Configure +
        /// RepEngageWatcher.Init chain, but anchored at the record's persistent anchor (position
        /// picking/validation already happened at generation). The rep body is the family LEADER's
        /// model (FamilyIds[0] — a hollow record visibly reads as a skeleton). Field-killable,
        /// zero contact damage, engage pops the full family into the BattleArena — identical
        /// behaviour contract to the ring reps.</summary>
        private bool SpawnScatterRep(ScatterRecord rec)
        {
            if (!_testMode && TutorialFlow.HostilesSuppressedForTutorial)
            {
                FlowTrace.Step("Encounter", $"suppressed scatter rep #{rec.Index} — tutorial (FTUE) active.");
                return false;
            }
            GameObject pack = SpawnOverworldFamilyPack(
                rec.Anchor, rec.FamilyIds, rec.Level, rec.ArenaPreset,
                $"ScatterRep_{rec.Index}", $"scatter-rep-{rec.Index}");

            if (pack == null)
            {
                string leadId = rec.FamilyIds != null && rec.FamilyIds.Length > 0 ? rec.FamilyIds[0] : "orc-warrior";
                FlowTrace.Warn("Encounter", $"SpawnScatterRep #{rec.Index}: family pack spawn failed for '{leadId}' — record left dormant.");
                return false;
            }
            rec.Live = pack;
            rec.Spawned = true;
            return true;
        }

        /// <summary>
        /// Spawns a visible Monster Family in the overworld: index 0 is the engage hook
        /// (RepEngageWatcher + FamilyLeader), followers hold formation via FamilyMember.
        /// Zero contact damage — the real fight stages in BattleArena on engage.
        /// </summary>
        private GameObject SpawnOverworldFamilyPack(
            Vector3 anchor,
            string[] familyIds,
            int threat,
            string arenaPreset,
            string packObjectName,
            string leaderInstanceId)
        {
            if (familyIds == null || familyIds.Length == 0)
                familyIds = RollFamilyPack(OrcPool);

            GameObject packRoot = null;
            Guard.Try("Encounter", $"spawn family pack '{packObjectName}'", () =>
            {
                packRoot = new GameObject(packObjectName);
                packRoot.transform.SetParent(transform);
                packRoot.transform.position = anchor;

                int packSize = Mathf.Clamp(familyIds.Length, PackSizeMin, PackSizeMax);
                // Owner 2026-07-10: only the family REP/leader roams the overworld (perf — bounded
                // roaming agents). The FULL family (leader + followers) still spawns in the
                // BattleArena on engage from the recipe carried by RepEngageWatcher.Init(familyIds)
                // below — the overworld followers were pure redundant cost (the arena rebuilds the
                // family and destroys the overworld bodies). packSize stays the full rolled size so
                // the leader's reward scaling (BuildOverworldHookDef 'bodies') still pays for the
                // whole family. Flag-gated to felt-revert to full-family roam.
                int n = DeNelle.Core.FeatureFlags.OverworldLeaderOnlyRoam ? 1 : packSize;
                FamilyLeader leader = null;
                // Did the rep LEADER enemy actually build? Track this SEPARATELY from the FamilyLeader
                // component (added only when n>1). The destroy guard below used `leader == null` as the
                // "pack spawned" sentinel — but with leader-only roam (n==1) FamilyLeader is never added,
                // so a VALID leader-only pack was destroyed and no OrcRep survived (fleet AssertEncounterRealPath
                // FAIL 8/8, regression f765eef2). Owner 2026-07-10.
                bool leaderSpawned = false;

                for (int i = 0; i < n; i++)
                {
                    string id = familyIds[i];
                    Vector3 pos = anchor;
                    if (i > 0)
                    {
                        float angle = (i - 1) * (360f / Mathf.Max(1, n - 1));
                        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.5f;
                        pos = anchor + offset;
                        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var slotHit, 6f, UnityEngine.AI.NavMesh.AllAreas))
                            pos = slotHit.position;
                    }

                    bool isLeader = i == 0;
                    var def = BuildOverworldHookDef(id, threat, isLeader, packSize);
                    Enemy enemy = EnemyFactory.Build(def, pos, Quaternion.identity, packRoot.transform);
                    if (enemy == null) continue;

                    enemy.gameObject.name = isLeader ? packObjectName : $"{packObjectName}_{id}_{i}";
                    enemy.Configure($"{leaderInstanceId}{(isLeader ? "" : $"-f{i}")}", def, null);

                    if (isLeader)
                    {
                        leaderSpawned = true;
                        enemy.SetBrainTargetPosition(anchor);
                        // NO EnemyBrain on the leader — RepEngageWatcher is the sole nav writer
                        // (a DPS brain clears SetBrainTargetPosition every frame).
                        if (n > 1)
                            leader = enemy.gameObject.AddComponent<FamilyLeader>();
                        if (enemy.gameObject.GetComponent<AwarenessSensor>() == null)
                            enemy.gameObject.AddComponent<AwarenessSensor>();
                        var watcher = enemy.gameObject.AddComponent<RepEngageWatcher>();
                        watcher.Init(familyIds, threat, arenaPreset, packRoot);
                    }
                    else if (leader != null)
                    {
                        var brain = enemy.gameObject.AddComponent<EnemyBrain>();
                        brain.Role = EnemyBrain.RoleForId(id);
                        brain.RosterId = id;   // owner ruling 2026-08-06: gates weapon attach (casters carry nothing)
                        // Owner 2026-07-10 F8: ranged followers KITE, not rush to melee — turn on the
                        // existing kite system the wave/arena spawners already enable. Ranged -> Kiter.
                        EnemyBrain.ApplyRoleTactics(brain, brain.Role);
                        if (enemy.gameObject.GetComponent<AwarenessSensor>() == null)
                            enemy.gameObject.AddComponent<AwarenessSensor>();
                        var member = enemy.gameObject.AddComponent<FamilyMember>();
                        leader.RegisterMember(member);
                    }
                }

                if (!leaderSpawned)
                {
                    Destroy(packRoot);
                    packRoot = null;
                }
            });

            return packRoot;
        }

        /// <summary>
        /// Roll a variable pack (1–7) from a roster pool. Leader prefers warrior/tank;
        /// followers sample the pool with replacement so size and mix stay unpredictable.
        /// </summary>
        private static string[] RollFamilyPack(string[] pool, System.Random rng = null)
        {
            if (pool == null || pool.Length == 0) pool = OrcPool;
            int size = rng != null
                ? rng.Next(PackSizeMin, PackSizeMax + 1)
                : UnityEngine.Random.Range(PackSizeMin, PackSizeMax + 1);

            // Owner balance (2026-07-16): a low-level hero must not be swarmed. While the hero
            // is UNDER LowLevelThreshold, the rolled family (and thus the arena fight it stages)
            // is capped to LowLevelEnemyCap concurrent bodies. At level 5+ the full roll stands.
            int heroLevel = CurrentHeroLevel();
            if (heroLevel < LowLevelThreshold && size > LowLevelEnemyCap)
            {
                FlowTrace.Step("Encounter", $"enemy count capped: level={heroLevel} requested={size} -> {LowLevelEnemyCap} (family roll).");
                size = LowLevelEnemyCap;
            }

            var pack = new string[size];
            pack[0] = PickLeaderFromPool(pool, rng);
            for (int i = 1; i < size; i++)
            {
                int idx = rng != null ? rng.Next(pool.Length) : UnityEngine.Random.Range(0, pool.Length);
                pack[i] = pool[idx];
            }
            return pack;
        }

        private static string PickLeaderFromPool(string[] pool, System.Random rng)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                string id = pool[i];
                if (id != null && id.IndexOf("warrior", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return id;
            }
            for (int i = 0; i < pool.Length; i++)
            {
                string id = pool[i];
                if (id != null && id.IndexOf("tank", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return id;
            }
            int fallback = rng != null ? rng.Next(pool.Length) : UnityEngine.Random.Range(0, pool.Length);
            return pool[fallback];
        }

        // WO-1103: bounded variance on the leader-carried pack bounty (fraction, +/-15%).
        // TUNABLE opening balance, never a lock — mirrors the enemies.json regular seed.
        private const float PackBountyRewardVariance = 0.15f;

        /// <summary>Hook-only overworld body: leader carries family payout; followers are visual.</summary>
        private static EnemyDef BuildOverworldHookDef(string id, int threat, bool isLeader, int packSize = 1)
        {
            string leadId = id ?? "orc-warrior";
            bool hollow = leadId.IndexOf("hollow", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float levelScale = 1f + 0.08f * (Mathf.Max(1, threat) - 1);
            int bodies = Mathf.Clamp(packSize, PackSizeMin, PackSizeMax);

            if (isLeader)
            {
                // WO-1103 item 5 (owner default): LEADER-CARRY KEPT — the leader's reward is
                // the whole pack's payout (followers pay 0 below). PackBodies > 1 flags the
                // def as a pack carrier so Enemy.Die's field-kill toast words the grant
                // "Pack bounty +N XP +M gold" instead of reading like a single-kill overpay.
                // RewardVariance makes the field bounty range-bound like every other kill
                // (TUNABLE — mirrors the regular-enemy 0.15 data seed).
                return new EnemyDef
                {
                    Id = leadId,
                    Name = hollow ? "Hollow Prowler" : "Orc Warleader",
                    DisplayName = hollow ? "Hollow Pack" : "Orc Warband",
                    Ai = "walker",
                    Hp = 98f * levelScale,
                    MoveSpeed = RepChaseSpeed,
                    ContactDamage = 0f,
                    AttackInterval = 1.5f, Height = 2.0f, AggroRadius = 8f,
                    XpReward = Mathf.RoundToInt(14 * bodies * levelScale),
                    RewardVariance = PackBountyRewardVariance,
                    PackBodies = bodies,
                };
            }

            return new EnemyDef
            {
                Id = leadId,
                Name = leadId.Replace('-', ' '),
                DisplayName = leadId.Replace('-', ' '),
                Ai = "walker",
                Hp = 40f * levelScale,
                MoveSpeed = RepChaseSpeed * 0.95f,
                ContactDamage = 0f,
                AttackInterval = 1.5f, Height = 1.9f, AggroRadius = 0f,
                XpReward = 0
            };
        }
    }

    /// <summary>
    /// Rides on a rep mob: watches for ENGAGE (the rep reaches the hero, OR the hero
    /// attacks the rep) and on the first such event POPS into the BattleArena with the
    /// rep's family, consuming the rep. Also fires the "they see us" chase sting once on
    /// aggro. Pure hook logic (no combat). WO-482.
    /// </summary>
    public sealed class RepEngageWatcher : MonoBehaviour
    {
        private string[] _family;
        private int _threat;
        private string _arenaPreset;   // WO-606: forwarded from the resolved SpawnArea (data only today)
        private GameObject _packRoot;  // whole family pack — consumed on engage / leader death
        private bool _engaged;
        private bool _stung;
        private Enemy _enemy;
        private GameObject _threatCue;   // world-space "!" nameplate raised on aggro (child of the rep)

        /// <summary>True once the rep has spotted the hero and is chasing (FamilyLeader → Wedge).</summary>
        public bool IsPursuing => _stung;

        // AggroRange = how far a rep can NOTICE the hero and start the chase. Lowered from 22f
        // (owner 2026-06-24 FELT buffer) so a rep doesn't spot the hero from across the map / reach
        // back across the seam — the hero gets a buffer after crossing before being hunted. Once
        // aggro'd, the chase/leash/engage behaviour below is UNCHANGED (owner loves the chase).
        private const float AggroRange  = 14f;   // owner 2026-07-10 F8 "enemy should aggro in range": 8->14 (8m read as point-blank/ignored; chase notice widens, fight still only at contact/TouchDistance)
        private const float EngageRange = 2.6f; // contact -> transition
        private const float LeashRadius = 14f;  // wander this far from spawn until aggro

        // -------------------------------------------------------------------------
        //  BATTLE ISOLATION + POST-LOSS GRACE (lose-flow fix, owner TOP priority).
        //  Two STATIC gates shared by EVERY home-scene rep:
        //    * _battlePaused  — while a BattleArena fight is staged, ALL home reps freeze
        //      (no roam/chase/aggro/engage). Removes the home-combat rumble bleed, the
        //      re-engage loop source, AND the double-sim choppiness in one move. Driven by
        //      BattleArena.BeginEncounter (PauseAll) / Resolve (ResumeAll).
        //    * _noEngageUntil — a brief re-aggro GRACE after a LOSS: no rep may aggro/engage
        //      the hero until this wall-clock time, so the hero recovers instead of being
        //      re-fought the instant it warps home. Set by BattleArena.Resolve on a loss.
        //  Both are honored at the TOP of Update() and Engage() so the loop breaks no matter
        //  the exact rep state. Tunables are named consts.
        // -------------------------------------------------------------------------
        private const float PostLossGraceSeconds = 3.5f;   // owner ~3-4s recovery window after a loss

        private static bool  _battlePaused;     // true while a BattleArena fight is staged
        private static float _noEngageUntil;    // Time.time before which no rep may aggro/engage

        /// <summary>Freeze every home-scene rep (roam/chase/aggro/engage) for the duration of a
        /// staged battle. Called by BattleArena.BeginEncounter. Idempotent.</summary>
        public static void PauseAll() => _battlePaused = true;

        /// <summary>Resume home-scene reps after a battle resolves. Called by BattleArena.Resolve.</summary>
        public static void ResumeAll() => _battlePaused = false;

        /// <summary>Battle-WIN cleanup (owner F8 2026-07-10 "after battle is over should return to peaceful
        /// if not being aggroed"): every rep NOT actively pursuing the hero drops combat presentation and
        /// resettles to a fresh calm roam, so the world reads peaceful after a fight instead of leftover reps
        /// milling in combat pose. Reps that ARE pursuing (IsPursuing) are PRESERVED — an active chaser must
        /// finish. Called by BattleArena.Resolve on a WIN. Idempotent; never throws into Resolve.</summary>
        public static void QuietNonPursuersOnBattleEnd()
        {
            Guard.Try("Encounter", "quiet non-pursuers on battle end", () =>
            {
                var watchers = FindObjectsByType<RepEngageWatcher>();
                if (watchers == null) return;
                int quieted = 0;
                foreach (var w in watchers)
                    if (w != null && w.QuietIfNotPursuing()) quieted++;
                FlowTrace.Step("Encounter", $"battle-over: quieted {quieted} non-pursuing rep(s) back to peaceful roam.");
            });
        }

        /// <summary>Instance helper for <see cref="QuietNonPursuersOnBattleEnd"/>: if this rep is NOT pursuing
        /// the hero (and not in a staged fight), drop it out of combat presentation, clear any threat cue, and
        /// force a fresh roam heading so it visibly resettles to calm rather than holding a combat pose.
        /// Returns true if it was quieted. Pursuers/engaged reps are left untouched.</summary>
        private bool QuietIfNotPursuing()
        {
            if (_stung || _engaged) return false;   // actively pursuing / in a fight — preserve
            SetPackCombatPresentation(false);
            if (_threatCue != null) { Destroy(_threatCue); _threatCue = null; }
            _roamRepathAt = 0f;                     // repick a roam point next Update -> visibly resettles
            return true;
        }

        /// <summary>Open a post-loss re-aggro grace window: no rep may aggro/engage the hero until
        /// now + <paramref name="seconds"/> (defaults to the tuned PostLossGraceSeconds). Called by
        /// BattleArena.Resolve on a LOSS so the hero is not instantly re-engaged.</summary>
        public static void BeginPostLossGrace(float seconds = PostLossGraceSeconds)
            => _noEngageUntil = Time.time + Mathf.Max(0f, seconds);

        /// <summary>True while a battle is staged OR the post-loss grace window is open — no rep
        /// may aggro/engage the hero. Read by the aggro check.</summary>
        private static bool EngagementSuppressed => _battlePaused || Time.time < _noEngageUntil;

        /// <summary>
        /// LOSS cleanup: immediately remove any still-live home rep whose GameObject name matches
        /// <paramref name="repId"/> (the EncounterParams.RepId that triggered the fight). The
        /// triggering rep is normally Destroy()'d in Engage(), but a queued Destroy can race the
        /// loss-resolve warp and leave the hero inside its aggro on return — so we DestroyImmediate
        /// any survivor here to guarantee it is gone. Guarded; never throws into Resolve.
        /// </summary>
        public static void DespawnRepImmediate(string repId)
        {
            if (string.IsNullOrEmpty(repId)) return;
            Guard.Try("Encounter", "loss rep despawn", () =>
            {
                var watchers = FindObjectsByType<RepEngageWatcher>();
                if (watchers == null) return;
                foreach (var w in watchers)
                {
                    if (w == null || w.gameObject == null) continue;
                    if (w.gameObject.name != repId) continue;
                    FlowTrace.Step("Encounter", $"DespawnRepImmediate: removing lingering rep '{repId}' on loss (kills the instant re-engage).");
                    DestroyImmediate(w.gameObject);
                }
            });
        }

        private Vector3 _leashCenter;           // spawn point -- centre of the wander leash
        private float   _roamRepathAt;          // next time to pick a new roam point
        private Enemy[] _packEnemies;           // every body in the family pack (presentation drive)
        private float   _nextAmbientGestureAt;  // cosmetic idle fidget while roaming
        private const float AmbientGestureMin = 10f;
        private const float AmbientGestureMax = 18f;

        // CONTACT ENGAGE (owner 2026-06-27): the battle triggers on near-CONTACT with the HERO,
        // not the old generous 2.6m EngageRange. touchDist = heroRadius + repRadius + 0.2f, resolved
        // once from the actual colliders (CharacterController / CapsuleCollider / NavMeshAgent radius).
        // Falls back to a small 0.7m constant (NOT 2.6f) if a radius can't be read. Cached after the
        // first successful resolve. Aggro/chase still use AggroRange — ONLY engage becomes contact.
        private const float TouchPadding      = 0.2f;   // owner: "+.2f difference radius"
        private const float TouchFallbackDist = 0.7f;   // used only if a collider radius can't be resolved
        private float _touchDist = -1f;                 // <0 until resolved from real colliders

        public void Init(string[] family, int threat, string arenaPreset = null, GameObject packRoot = null)
        {
            _family = (family != null && family.Length > 0) ? family : new[] { "orc-warrior" };
            _threat = Mathf.Max(1, threat);
            _arenaPreset = arenaPreset;
            _packRoot = packRoot;
            _enemy = GetComponent<Enemy>();
            _leashCenter = transform.position;                    // wander leash centred on the spawn
            // FIELD-KILL DECOUPLE (owner 2026-06-28): damage no longer auto-engages the arena. With
            // RangedHitsEngage=false the rep can be WHITTLED DOWN and KILLED in the open world by ranged
            // attacks; only near-CONTACT with the hero (TouchDistance in Update) starts the BattleArena.
            // Flip the const true to restore the old "any hit pops the fight" hook behaviour.
            if (RangedHitsEngage && _enemy != null) _enemy.Damaged += OnRepDamaged;   // hero attacked the rep -> engage
            if (_enemy != null) _enemy.Died += OnRepConsumed;
            CachePackEnemies();
            ScheduleAmbientGesture();
        }

        private void CachePackEnemies()
        {
            if (_packRoot == null)
            {
                _packEnemies = _enemy != null ? new[] { _enemy } : null;
                return;
            }
            _packEnemies = _packRoot.GetComponentsInChildren<Enemy>(true);
        }

        private void ScheduleAmbientGesture()
            => _nextAmbientGestureAt = Time.time + UnityEngine.Random.Range(AmbientGestureMin, AmbientGestureMax);

        private void SetPackCombatPresentation(bool on)
        {
            if (_packEnemies == null) return;
            for (int i = 0; i < _packEnemies.Length; i++)
                _packEnemies[i]?.SetCombatPresentation(on);
        }

        // OWNER-TUNABLE hook: when true, ANY damage to the rep (incl. a ranged hit) instantly engages
        // the arena (the legacy un-killable-hook behaviour). When false (V1 default), ranged hits damage
        // the rep normally so it can be field-killed for full XP+loot; only contact starts the fight.
        private const bool RangedHitsEngage = false;

        private void OnDestroy()
        {
            if (RangedHitsEngage && _enemy != null) _enemy.Damaged -= OnRepDamaged;
            if (_enemy != null) _enemy.Died -= OnRepConsumed;
        }

        private void OnRepConsumed(Enemy _) => ConsumePack();

        private void OnRepDamaged(Vector3 _) => Engage("hero-attacked-rep");

        private void Update()
        {
            if (_engaged || !FeatureFlags.OverworldEncounter) return;

            // CHAIN ISOLATION (owner 2026-06-30): reps are DontDestroyOnLoad and survive a single-load
            // into the dungeon chain (Outpost1/Dungeon/Outpost2). They must NOT roam/aggro/engage there
            // — a rep staging a BattleArena in a single-loaded scene is what caused the Village2
            // SpawnFamily NRE / WarpHero-off-mesh errors. Only act while OuterWorld is actually loaded
            // (the reps' home region); in a chain scene OuterWorld is unloaded so they stay inert.
            if (!OverworldEncounterSpawner.OuterWorldLoaded()) return;

            // BATTLE ISOLATION + POST-LOSS GRACE: while a fight is staged, or during the brief
            // post-loss recovery window, EVERY home rep freezes — no roam/chase/aggro/engage.
            // This kills the home-combat rumble bleed, the instant re-engage loop, and the
            // double-sim choppiness. The rep simply holds until the gate clears.
            if (EngagementSuppressed) return;

            // FALL-THROUGH GUARD (owner 2026-06-23 "they fall through ground when I change zones"):
            // a zone/navmesh swap can drop a NavMeshAgent below the floor. Re-seat only when the rep is
            // meaningfully BELOW the sampled navmesh (a REAL fall-through) -- NOT merely below an absolute y.
            // (owner F8 2026-07-10 'ScatterRep_17' spam: far-band terrain legitimately sits at y~-3.9, so the
            // old absolute y<-2 test re-seated onto a navmesh point that was itself <-2 every frame -> dozens
            // of per-frame Warn lines.) The y<-2 stays as a cheap early gate; the real test is "below the mesh".
            // Log throttled to ~1/sec so a persistent condition can't spam the break-log.
            if (transform.position.y < -2f
                && UnityEngine.AI.NavMesh.SamplePosition(transform.position, out var reseatHit, 20f, UnityEngine.AI.NavMesh.AllAreas)
                && reseatHit.position.y - transform.position.y > 0.5f)
            {
                Guard.Try("Encounter", "rep re-seat", () =>
                {
                    transform.position = reseatHit.position;
                    FlowTrace.Throttle("Encounter", $"reseat-{gameObject.name}", 1f,
                        $"rep '{gameObject.name}' fell through floor -> re-seated onto navmesh at {reseatHit.position}.");
                });
            }

            var hero = GameObject.FindWithTag("Player");
            if (hero == null) return;

            // F8 2026-07-11 arena RCA: an out-of-family overworld rep MARCHED into the staged
            // arena mid-battle ('MARCH leader dist=7.5m to hero' during the fight) and stood
            // T-posed in the owner's frame. While a battle owns the space, reps neither aggro
            // nor chase the (warped) hero — they hold/roam where they are and resume after.
            if (BattleArena.AnyBattleInProgress)
            {
                FlowTrace.Throttle("Encounter", $"battle-hold-{gameObject.name}", 5f,
                    $"rep '{gameObject.name}' holding — battle in progress, no chase into the arena.");
                return;
            }

            float d = Vector3.Distance(hero.transform.position, transform.position);

            if (!_stung && d <= AggroRange)
            {
                _stung = true;
                SetPackCombatPresentation(true);
                Guard.Try("Encounter", "chase sting", () => AbilityAudioBridge.PlayDangerSting());
                // THREAT CUE (encounter feedback): raise a visible "!" nameplate over the rep the
                // instant it aggros, so the player connects "that orc is hunting me -> contact starts
                // the fight" — the missing pre-engage telegraph. Pairs the audio sting with a visual.
                RaiseThreatCue();
                FlowTrace.Step("Encounter", "rep aggro -> chase sting + threat nameplate ('they see us').");
            }

            // ROAM until aggro, then CHASE -- "a wandering leash till it goes to battle" (owner 2026-06-23).
            // The rep drives Enemy's brain-position override (no EnemyBrain to clear it): a random leash
            // point while idle, the hero once it sees you. +5% MoveSpeed guarantees the chase closes to
            // EngageRange, so the orc runs you down instead of being left behind.
            if (_enemy != null)
            {
                if (_stung)
                {
                    Guard.Try("Encounter", "rep chase", () => _enemy.SetBrainTargetPosition(hero.transform.position));
                    // F8-46 (owner OPTION A): a chasing rep ALWAYS counts as pursuit. The chase here
                    // is driven by SetBrainTargetPosition and relies on Enemy.DriveNav classifying
                    // chasingHero to pulse ReportPursuit — report directly too (keyed per rep, same
                    // as Enemy.cs) so the A4.5 window + the pursuit battle-probe (combat inputs live
                    // while pursued) can never miss this producer. Pulse self-expires (PursuitTtl).
                    // WO-1603: tagged so a stuck battle-lock names THIS producer instead of naming
                    // PursuitBattleProbe, which only reads the ring. One of three stamp sites.
                    DeNelle.Core.HudModel.PostureSignals.ReportPursuit(
                        _enemy.GetInstanceID(), "OverworldEncounterSpawner/rep-chase");
                }
                else
                {
                    if (Time.time >= _roamRepathAt)
                    {
                        Vector3 roam = PickRoamPoint();
                        Guard.Try("Encounter", "rep roam", () => _enemy.SetBrainTargetPosition(roam));
                        _roamRepathAt = Time.time + UnityEngine.Random.Range(2.5f, 5f);
                    }
                    // Idle-roam fidgets: taunt / cast / swing from the shared humanoid library.
                    if (Time.time >= _nextAmbientGestureAt)
                    {
                        _enemy.PlayAmbientGesture();
                        ScheduleAmbientGesture();
                    }
                }
            }

            // CONTACT ENGAGE (owner 2026-06-27): proximity battle fires only at near-TOUCH of the
            // HERO (heroR+repR+0.2f), not the old generous EngageRange. Aggro/chase above are
            // UNCHANGED — only this engage threshold became contact-based.
            float touchDist = TouchDistance(hero);
            if (d <= touchDist) Engage($"rep-touched-hero d={d:0.0}m touch={touchDist:0.0}m");
        }

        // THREAT CUE (encounter feedback): build a world-space "!" alert + foe name floating above
        // the rep when it aggros, so the rep reads as a THREAT pre-engage. A child of the rep, so it
        // moves with it and is auto-destroyed when Engage() Destroy()s the rep (no manual cleanup).
        // Billboard-faced to the camera. Presentation only; reuses the legacy uGUI + Billboard.
        private void RaiseThreatCue()
        {
            if (_threatCue != null) return;
            Guard.Try("Encounter", "rep threat nameplate", () =>
            {
                var root = new GameObject("RepThreatCue");
                root.transform.SetParent(transform, false);
                root.transform.localPosition = new Vector3(0f, 3.0f, 0f);   // above a ~2m orc
                root.transform.localScale = Vector3.one * 0.01f;            // world-space UI scale

                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var crt = canvas.GetComponent<RectTransform>();
                crt.sizeDelta = new Vector2(260f, 150f);                    // -> ~2.6 x 1.5 world units
                root.AddComponent<DeNelle.Village.UI.Billboard>();         // keep it facing the camera

                // Text-only by owner ruling (2026-09-08): the old 78%-opaque CuePanel read as
                // a shaded box floating over every enemy's head. The Canvas RectTransform is
                // already the layout surface, so parent both words to it directly and leave the
                // world visible behind them.
                var bang = AddCueText(canvas.transform, "!", 96, new Color(0.95f, 0.25f, 0.20f), TextAnchor.UpperCenter);
                var br = bang.rectTransform;
                br.anchorMin = new Vector2(0f, 0.35f); br.anchorMax = new Vector2(1f, 1f);
                br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;

                var foeLabel = AddCueText(canvas.transform, FoeName(), 34, new Color(0.95f, 0.85f, 0.40f), TextAnchor.LowerCenter);
                var nr = foeLabel.rectTransform;
                nr.anchorMin = new Vector2(0f, 0f); nr.anchorMax = new Vector2(1f, 0.35f);
                nr.offsetMin = Vector2.zero; nr.offsetMax = Vector2.zero;

                _threatCue = root;
                FlowTrace.Step("Encounter", $"threat nameplate raised on rep '{gameObject.name}' ('! {FoeName()}').");
            });
        }

        // A player-facing label for the rep's family (ASCII-only, legacy runtime font). An all-orc
        // family reads "Orc Warband" (matching the rep DisplayName); else the leader id is humanised.
        private string FoeName()
        {
            if (_family == null || _family.Length == 0) return "Foes";
            foreach (var id in _family)
                if (id != null && id.IndexOf("orc", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Orc Warband";
            // F8-8: hollow scatter families get a proper warband-style title too.
            foreach (var id in _family)
                if (id != null && id.IndexOf("hollow", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Hollow Pack";
            var lead = _family[0] ?? "Foes";
            lead = lead.Replace('-', ' ').Replace('_', ' ').Trim();
            return lead.Length == 0 ? "Foes" : (char.ToUpperInvariant(lead[0]) + (lead.Length > 1 ? lead.Substring(1) : ""));
        }

        private static Text AddCueText(Transform parent, string s, int size, Color col, TextAnchor anchor)
        {
            var go = new GameObject("CueText");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = s; t.fontSize = size; t.color = col; t.alignment = anchor;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                  ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // CONTACT-ENGAGE threshold (owner 2026-06-27 "has to TOUCH the hero or +.2f difference
        // radius"): touchDist = heroRadius + repRadius + 0.2f, read from the REAL colliders so the
        // battle only fires at near-contact with the HERO. Cached after the first successful resolve
        // (colliders don't change at runtime). If neither radius can be read, falls back to a small
        // 0.7m constant — never the old generous 2.6m. Pure read; no behavior beyond the threshold.
        private float TouchDistance(GameObject hero)
        {
            if (_touchDist > 0f) return _touchDist;   // cached
            float heroR = ColliderRadius(hero);
            float repR  = ColliderRadius(gameObject);
            if (heroR <= 0f && repR <= 0f)
                return TouchFallbackDist;             // neither resolved yet — don't cache, retry next frame
            float hr = heroR > 0f ? heroR : TouchFallbackDist * 0.5f;
            float rr = repR  > 0f ? repR  : TouchFallbackDist * 0.5f;
            _touchDist = hr + rr + TouchPadding;
            FlowTrace.Step("Encounter",
                $"touchDist resolved for rep '{gameObject.name}': heroR={hr:0.00} repR={rr:0.00} +pad {TouchPadding:0.00} => {_touchDist:0.00}m.");
            return _touchDist;
        }

        // Best-effort horizontal radius of a character: CharacterController, then CapsuleCollider,
        // then NavMeshAgent.radius, then any Collider's bounds extent. Returns 0 if nothing readable.
        private static float ColliderRadius(GameObject go)
        {
            if (go == null) return 0f;
            float r = 0f;
            Guard.Try("Encounter", "collider radius", () =>
            {
                var cc = go.GetComponent<CharacterController>();
                if (cc != null) { r = cc.radius * Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z); return; }
                var cap = go.GetComponentInChildren<CapsuleCollider>();
                if (cap != null) { r = cap.radius * Mathf.Max(go.transform.lossyScale.x, go.transform.lossyScale.z); return; }
                var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) { r = agent.radius; return; }
                var col = go.GetComponentInChildren<Collider>();
                if (col != null) { r = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z); }
            });
            return r;
        }

        // Random navmesh point within the leash of the spawn -- the wander target while idle.
        private Vector3 PickRoamPoint()
        {
            Vector3 p = _leashCenter;
            Guard.Try("Encounter", "roam pick", () =>
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * LeashRadius;
                Vector3 cand = _leashCenter + new Vector3(r.x, 0f, r.y);
                if (UnityEngine.AI.NavMesh.SamplePosition(cand, out var hit, 6f, UnityEngine.AI.NavMesh.AllAreas))
                    p = hit.position;
            });
            return p;
        }

        private void Engage(string cause)
        {
            if (_engaged) return;
            if (BattleArena.Instance != null && BattleArena.Instance.BattleInProgress) return;
            // Honor the battle-pause + post-loss grace here too: OnRepDamaged (the hero hit the
            // rep) routes straight to Engage and bypasses Update's gate, so the same suppression
            // must hold or a single stray swing re-starts the fight inside the grace window.
            if (EngagementSuppressed) return;
            _engaged = true;

            // TRIGGER PROOF (instrumentation only — no behavior change): capture exactly WHY this
            // battle began — which rep, the hero distance, whether the hero attacked vs proximity,
            // and the suppression/flag state — so the next F8 capture pinpoints the cause.
            var heroGo = GameObject.FindWithTag("Player");
            float heroDist = heroGo != null ? Vector3.Distance(heroGo.transform.position, transform.position) : -1f;
            FlowTrace.Step("Encounter",
                $"TRIGGER cause='{cause}' rep='{gameObject.name}' heroDist={heroDist:0.0}m " +
                $"aggroRange={AggroRange} engageRange={EngageRange} touchDist={(_touchDist > 0f ? _touchDist : -1f):0.00}m " +
                $"heroAttacked={cause.StartsWith("hero-attacked")} " +
                $"suppressed={EngagementSuppressed} ff={FeatureFlags.OverworldEncounter}");

            var hero = GameObject.FindWithTag("Player");
            string scene = SceneManager.GetActiveScene().name;

            var p = new EncounterParams
            {
                EnemyIds = _family,
                Threat = _threat,
                BackdropContext = ThemeForScene(scene),
                ReturnScene = scene,
                ReturnPosition = hero != null ? hero.transform.position : transform.position,
                ReturnYaw = hero != null ? hero.transform.eulerAngles.y : 0f,
                RepId = gameObject.name,
                ArenaPreset = _arenaPreset,   // WO-606: forward the geotagged area's preset (data only today)
            };

            FlowTrace.Step("Encounter", $"ENGAGE rep '{gameObject.name}' -> BattleArena (family [{string.Join(",", _family)}], threat {_threat}, theme '{p.BackdropContext}', hero={(hero != null ? "found" : "NULL")}).");

            bool started = false;
            var arena = BattleArena.Instance;   // lazy singleton — non-null, but guard anyway
            if (arena == null)
            {
                FlowTrace.Fail("Encounter", "Engage: BattleArena.Instance was NULL — cannot drop to battle.");
            }
            else
            {
                Guard.Try("Encounter", "begin encounter", () => started = arena.BeginEncounter(p));
            }

            // No drop to battle is the OWNER's reported symptom — make the failure LOUD so a
            // capture pinpoints WHY (ff off / battle already in progress / empty family) instead
            // of the rep silently despawning and the player wondering why nothing happened.
            if (started)
                FlowTrace.Step("Encounter", $"Engage: BattleArena.BeginEncounter SUCCEEDED for rep '{gameObject.name}' — dropped to battle.");
            else
                FlowTrace.Fail("Encounter", $"Engage: BattleArena.BeginEncounter returned FALSE for rep '{gameObject.name}' — NO drop to battle (check ff.overworldencounter / BattleInProgress / empty family).");

            // Consume the pack regardless (the full family lives in the battle now); if the
            // battle failed to start (flag off / busy) the pack simply despawns -- never a stuck hook.
            ConsumePack();
        }

        private void ConsumePack()
        {
            if (_packRoot != null)
            {
                Destroy(_packRoot);
                _packRoot = null;
                return;
            }
            Destroy(gameObject);
        }

        private static string ThemeForScene(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return "outerworld";
            string s = scene.ToLowerInvariant();
            if (s.Contains("castle")) return "castle";
            if (s.Contains("dungeon") || s.Contains("cavern")) return "cavern";
            return "outerworld";
        }
    }
}
