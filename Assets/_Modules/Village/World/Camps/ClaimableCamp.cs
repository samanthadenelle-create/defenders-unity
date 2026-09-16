// =============================================================================
// ClaimableCamp - one outer-world enemy camp in the clear -> claim -> build loop.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// Lifecycle:
//   STAGE 1 CLEAR  - count enemy kills within CampRadius (subscribe to the real
//                    public Enemy.Died event). At KillsRequired -> Clear().
//   STAGE 2 CLAIM  - once cleared, when the hero is near, CampPromptUI shows a
//                    code-built world-space "Claim" prompt (tap / [E]). Tap claims.
//   STAGE 3 BUILD  - on claim, CampBuildMenuUI offers Watchtower / Lumber Outpost /
//                    Farm Outpost. Choosing one spawns an Outpost that auto-harvests
//                    a trickle into the wallet.
//
// ISOLATION: created/owned entirely by CampSystem at runtime. Subscribes to the
// existing PUBLIC Enemy.Died event (read-only) - it does NOT modify Enemy or any
// spawner. Code-built primitive visuals (LogWarning, never error, if optional art
// is absent). Persistence is PlayerPrefs only (claimed id + outpost type); the
// save SCHEMA is untouched (save-owner follow-up).
// Canon: the village is Elarion (never Avalon). ASCII-only runtime strings.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.World;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;   // TGVRU — FlowTrace/Guard on the camp build/fortify/harvest path
using DeNelle.Village.World;

namespace DeNelle.Village.World.Camps
{
    /// <summary>The three lifecycle stages of a camp.</summary>
    public enum CampStage { Hostile, Cleared, Claimed }

    /// <summary>A bounded outer-world camp the player clears, claims, then builds on.</summary>
    [DisallowMultipleComponent]
    public sealed class ClaimableCamp : MonoBehaviour
    {
        // -- Config (set by CampSystem.Configure) -----------------------------
        public RegionId Region { get; private set; } = RegionId.Goldfields;
        public int ThreatLevel { get; private set; }
        public int KillsRequired { get; private set; } = CampSystem.DefaultKillsRequired;
        public float CampRadius { get; private set; } = CampSystem.DefaultCampRadius;

        /// <summary>Stable id (region-based) used as the PlayerPrefs persistence key.</summary>
        public string CampId { get; private set; }

        // -- Runtime state ----------------------------------------------------
        public CampStage Stage { get; private set; } = CampStage.Hostile;
        public int KillCount { get; private set; }

        /// <summary>True once enough kills landed inside the camp - claimable.</summary>
        public bool Cleared => Stage == CampStage.Cleared || Stage == CampStage.Claimed;

        /// <summary>True once the player has claimed and (optionally) built here.</summary>
        public bool Claimed => Stage == CampStage.Claimed;

        /// <summary>Raised when the camp transitions to Cleared. Arg = this camp.</summary>
        public event Action<ClaimableCamp> OnCleared;
        /// <summary>Raised when the camp is claimed. Arg = this camp.</summary>
        public event Action<ClaimableCamp> OnClaimed;
        /// <summary>Raised when the post-build counterattack is repelled (camp secured).</summary>
        public event Action<ClaimableCamp> OnDefended;
        /// <summary>Raised when the post-build counterattack razes the outpost (defense lost).</summary>
        public event Action<ClaimableCamp> OnDefenseLost;

        /// <summary>True once the post-build counterattack has been repelled.</summary>
        public bool Secured { get; private set; }
        /// <summary>True while the post-build counterattack is in progress.</summary>
        public bool UnderAttack => _defense != null && !_defense.Resolved;

        private CampVisual _visual;
        private bool _subscribed;
        private CampGuards _guards;
        private CampDefenseWave _defense;

        // -- Clear reward (WO-216 / DEF-187) ----------------------------------
        // Granted ONCE when the camp transitions Hostile -> Cleared. Crystals bank
        // straight into GameState (the existing economy path); XP feeds the hero via
        // HeroProgression. Scaled by threat tier so deadlier camps pay better.
        /// <summary>Aether Crystals banked on clear (before the threat-tier bonus).</summary>
        public int BaseClearCrystals { get; private set; } = 15;
        /// <summary>Hero XP granted on clear (before the threat-tier bonus).</summary>
        public int BaseClearXp { get; private set; } = 40;
        /// <summary>True once the clear reward has been paid (prevents double-grant).</summary>
        private bool _rewardPaid;

        // -- Persistence (PlayerPrefs only - schema untouched) ----------------
        private const string PrefClearedKey = "dotr-camp-cleared-";   // +CampId -> "1"
        private const string PrefClaimedKey = "dotr-camp-claimed-";   // +CampId -> outpost type int
        private const string PrefSecuredKey = "dotr-camp-secured-";   // +CampId -> "1" (defended)
        private const string PrefRecipeKey  = "dotr-camp-recipe-";    // +CampId -> serialized fortification recipe

        // -- Outpost fortification (WO: claim -> generate a WOOD outpost foundation) --
        // The generated catalog-piece fortification recipe (perimeter walls + corner
        // towers + one gate) in LOCAL cell coords. Pure data; persisted to PlayerPrefs
        // so the same fortification re-Realizes on reload (no village-grid involvement).
        // DESIGN PIVOT (owner 2026-06-16): a 7x7 footprint with a DOUBLE wood-wall ring
        // (CoC "two rows of wooden walls in a square") leaves a 3x3 interior COURTYARD to
        // build in — this is the player's claimed base + the base-building teaching beat.
        [Tooltip("Fortification footprint in grid cells (W x D). cellSize = 3 m.")]
        private const int FortGridWidth = 7;
        private const int FortGridDepth = 7;
        // Concentric wood-wall rings (the "two rows" of the square base).
        private const int FortWallRings = 2;
        private List<PlacedStructureData> _recipe;

        /// <summary>Called by CampSystem immediately after AddComponent.</summary>
        public void Configure(RegionId region, int threat, int killsRequired, float radius)
        {
            Region = region;
            ThreatLevel = threat;
            KillsRequired = Mathf.Max(1, killsRequired);
            CampRadius = Mathf.Max(1f, radius);
            CampId = "camp_" + region;
        }

        // WO-111: Basic enemy outpost extension (clear -> boss -> claim rewards as
        // progression content, additive scene friendly for mobile).
        // Spawn Quaternius enemy-themed buildings (low-poly from catalog).
        // On boss clear: Economy.Grant (scaled by danger) + loot.
        public bool IsEnemyOutpost { get; private set; }

        public void ConfigureAsEnemyOutpost(RegionId region, int threat)
        {
            IsEnemyOutpost = true;
            // Quaternius for enemy camp buildings (see docs/QUATERNIUS_NOTES.md;
            // use Modules/Prefabs for huts/tents as cover).
            SpawnQuaterniusEnemyCamp();
            // Boss spawn hooked in defense clear (extend CampGuards or wave).
            // Rewards: Economy.Grant on boss death.
        }

        private void SpawnQuaterniusEnemyCamp()
        {
            using var _ = FlowTrace.Enter("Camp", $"{CampId} SpawnQuaterniusEnemyCamp");
            // Demo spawn; real load Quaternius enemy/medieval props.
            int built = 0;
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                // G — guard each prop so one bad CreatePrimitive logs + is skipped, never aborting.
                if (Guard.Try("Camp", $"{CampId} enemy prop {idx}", () =>
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    p.name = "QuaterniusEnemyProp";
                    p.transform.SetParent(transform, false);
                    p.transform.localPosition = new Vector3(idx * 5 - 5, 1, 3);
                    p.transform.localScale = Vector3.one * 3f;
                })) built++;
            }
            FlowTrace.Step("Camp", $"{CampId} enemy outpost props (Quaternius): built {built}/3.");
        }

        private void Start()
        {
            using var _ = FlowTrace.Enter("Camp", $"{CampId} Start (stage={Stage})");
            // Build the simple code-built visual tell (campfire/banner primitives).
            _visual = gameObject.AddComponent<CampVisual>();
            _visual.Init(this);

            RestoreFromPrefs();

            if (Stage == CampStage.Hostile)
            {
                // Primary clear path (WO-216): spawn a dedicated guard pack ON the camp.
                // When ALL guards die the camp clears. The legacy proximity-kill counter
                // below stays as a secondary path (e.g. a roaming mob killed inside the
                // footprint also counts), so a camp can never get stuck un-clearable.
                _guards = gameObject.AddComponent<CampGuards>();
                _guards.AllCleared += Clear;
                _guards.Spawn(Region, ThreatLevel);

                SubscribeToKills();
            }
        }

        private void OnDestroy() => UnsubscribeFromKills();

        // =====================================================================
        // STAGE 1 - CLEAR. Count kills inside the camp via the public Enemy.Died.
        // We find Enemy instances lazily (no spawner edit) and subscribe; the dead
        // enemy's world position is proximity-tested against the camp footprint.
        // =====================================================================

        private float _lastScan;

        private void Update()
        {
            if (Stage != CampStage.Hostile) return;

            // Re-scan for newly-spawned enemies every ~1s and subscribe to any we
            // haven't yet. RegionMobSpawner spawns enemies at runtime, so the set
            // grows; we never edit the spawner, just listen to each Enemy.Died.
            if (Time.time - _lastScan > 1f)
            {
                _lastScan = Time.time;
                SubscribeToKills();
            }
        }

        private void SubscribeToKills()
        {
            // Subscribe to every Enemy currently alive (idempotent: -= then +=).
            var enemies = UnityEngine.Object.FindObjectsByType<Enemy>(
                FindObjectsInactive.Exclude);
            foreach (var e in enemies)
            {
                if (e == null) continue;
                e.Died -= HandleEnemyDied;
                e.Died += HandleEnemyDied;
            }
            _subscribed = true;
        }

        private void UnsubscribeFromKills()
        {
            if (!_subscribed) return;
            var enemies = UnityEngine.Object.FindObjectsByType<Enemy>(
                FindObjectsInactive.Include);
            foreach (var e in enemies)
            {
                if (e != null) e.Died -= HandleEnemyDied;
            }
            _subscribed = false;
        }

        private void HandleEnemyDied(Enemy enemy)
        {
            if (Stage != CampStage.Hostile || enemy == null) return;

            // Proximity gate: only kills INSIDE this camp footprint count (do NOT
            // edit RegionMobSpawner - just test where the kill happened).
            float sqr = (enemy.transform.position - transform.position).sqrMagnitude;
            if (sqr > CampRadius * CampRadius) return;

            KillCount++;
            if (KillCount >= KillsRequired)
                Clear();
        }

        /// <summary>Transition Hostile -> Cleared (visual tell + persistence).</summary>
        public void Clear()
        {
            if (Stage != CampStage.Hostile) return;
            Stage = CampStage.Cleared;
            UnsubscribeFromKills();
            if (_guards != null) _guards.AllCleared -= Clear;
            _visual?.SetStage(CampStage.Cleared);
            PlayerPrefs.SetString(PrefClearedKey + CampId, "1");
            PlayerPrefs.Save();

            GrantClearReward();

            Debug.Log($"[ClaimableCamp] {CampId} CLEARED. Approach to claim.");
            OnCleared?.Invoke(this);
        }

        /// <summary>
        /// Pay the one-time clear reward (WO-216 / DEF-187): Aether Crystals banked
        /// into GameState (the existing economy wallet path) + hero XP via
        /// HeroProgression. Both scale with the camp's threat tier so deadlier camps
        /// pay better. Idempotent - the reward is paid at most once per camp/session.
        /// Restored-as-already-cleared camps do NOT re-pay (RestoreFromPrefs marks it).
        /// </summary>
        private void GrantClearReward()
        {
            if (_rewardPaid) return;
            _rewardPaid = true;

            int crystals = BaseClearCrystals + ThreatLevel * 5;
            int xp = BaseClearXp + ThreatLevel * 10;

            // Crystals -> GameState wallet (null-conditional on the cross-module singleton).
            var state = GameStateService.Instance?.State;
            if (state != null)
            {
                GameStateService.Instance?.AddCrystals(crystals);   // unified onto Resources.Crystals; persists + raises ResourcesChanged
            }
            else
            {
                Debug.LogWarning("[ClaimableCamp] GameState null - clear crystals not banked.");
            }

            // XP -> hero progression (same flat-XP path Enemy.Die uses on kill).
            HeroProgression.Instance?.AddXp(xp);

            Debug.Log($"[ClaimableCamp] {CampId} clear reward: +{crystals} crystals, +{xp} XP.");
        }

        // =====================================================================
        // STAGE 2 - CLAIM. Driven by CampPromptUI (hero proximity + tap/[E]).
        // =====================================================================

        /// <summary>Claim a cleared camp. No-op unless Cleared. Idempotent.</summary>
        public void Claim()
        {
            if (Stage != CampStage.Cleared) return;
            Stage = CampStage.Claimed;
            _visual?.SetStage(CampStage.Claimed);
            PlayerPrefs.SetString(PrefClaimedKey + CampId, ((int)OutpostType.None).ToString());
            PlayerPrefs.Save();
            Debug.Log($"[ClaimableCamp] {CampId} CLAIMED. Choose an outpost to build.");
            OnClaimed?.Invoke(this);
        }

        // =====================================================================
        // STAGE 3 - BUILD. Spawn the chosen outpost (auto-harvest faucet).
        // =====================================================================

        private Outpost _outpost;

        /// <summary>True once an outpost has been built on this claimed camp.</summary>
        public bool HasOutpost => _outpost != null;

        /// <summary>Build (or replace) the outpost of the given type on this camp.</summary>
        public Outpost BuildOutpost(OutpostType type)
        {
            using var _ = FlowTrace.Enter("Camp", $"{CampId} BuildOutpost type={type}");
            if (Stage != CampStage.Claimed)
            {
                FlowTrace.Step("Camp", $"{CampId} BuildOutpost: not Claimed (stage={Stage}) — no-op.");
                return null;
            }
            if (type == OutpostType.None)
            {
                FlowTrace.Step("Camp", $"{CampId} BuildOutpost: type None — no-op.");
                return null;
            }

            if (_outpost != null)
            {
                Destroy(_outpost.gameObject);
                _outpost = null;
            }

            var go = new GameObject($"Outpost_{type}_{Region}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            // Suppress the primitive post/cap placeholder — the generated fortification
            // IS the visual now. Harvest + IDamageableStructure logic is untouched.
            _outpost = go.AddComponent<Outpost>();
            _outpost.Init(type, Region, ThreatLevel, buildPrimitiveVisual: false);

            // FIRST SLICE of the outpost->raid->loot loop: generate a REAL catalog-piece
            // WOOD fortification (perimeter walls + corner towers + one gate) from the
            // SAME StructureFactory.Create path the village build mode uses, and persist
            // the recipe so it re-Realizes on reload. LOCAL cell math against this root —
            // never the village-scoped PlacementGrid / GameState.BaseLayout.
            BuildFortification(go.transform, OutpostTier.Wood, persist: true);

            // Attach recruitable hub (small defense grid + Economy-backed troop recruitment
            // for Tank/DPS/Healer). This gives the "outpost needs better defenses as it scales".
            var hub = go.AddComponent<OutpostHub>();
            hub.Region = Region;
            hub.ThreatLevel = ThreatLevel;

            PlayerPrefs.SetString(PrefClaimedKey + CampId, ((int)type).ToString());
            PlayerPrefs.Save();

            FlowTrace.Step("Camp", $"{CampId} built a {type} outpost — auto-harvest online.");

            // DESIGN PIVOT (owner 2026-06-16): the STAGE-4 DEFEND counterattack is RETIRED.
            // It silently auto-resolved when no attackers spawned (NavMesh missing at camp
            // anchors) with zero player feedback -> "secure a node" felt broken. Clear ->
            // claim -> BUILD is the loop now: building flips the camp to the player's own
            // base immediately (no counterattack), so the territory/economy benefit + UI
            // feedback still fire. StartDefense()/HandleDefended() are kept (dead) so the
            // stage can be re-enabled later once attacker spawning is fixed.
            MarkOwned();
            return _outpost;
        }

        // =====================================================================
        // FORTIFICATION - generate + realize the catalog-piece outpost foundation.
        // =====================================================================

        // Generate (or restore) the fortification recipe and realize it onto the
        // outpost root. The surface mask is "everything" (~0) like the sandbox builder;
        // SeatRootOnSurface ignores triggers so prompt/zone volumes don't catch it.
        // When persist is true the freshly-generated recipe is saved to PlayerPrefs;
        // on restore we pass persist:false and reuse the deserialized recipe.
        private void BuildFortification(Transform outpostRoot, OutpostTier tier, bool persist)
        {
            using var _ = FlowTrace.Enter("Camp", $"{CampId} BuildFortification tier={tier}");
            if (outpostRoot == null)
            {
                FlowTrace.Fail("Camp", $"{CampId} BuildFortification: null outpost root — no fortification built.");
                return;
            }

            // G — recipe generation is pure data but still guarded so a degenerate dimension can't
            // throw and abort the whole build. A null/empty recipe self-reports below.
            if (_recipe == null || _recipe.Count == 0)
                Guard.Try("Camp", $"{CampId} generate fortification recipe", () =>
                    _recipe = OutpostFoundationGenerator.GenerateSquareWalledBaseRecipe(
                        FortGridWidth, FortGridDepth, tier, FortWallRings));

            if (_recipe == null || _recipe.Count == 0)
            {
                // R — no recipe -> no walls/towers/gate. Self-report rather than a silent empty fort
                // (the camp would have a navmesh-free, wall-free claimed base).
                FlowTrace.Fail("Camp", $"{CampId} BuildFortification: empty recipe — no fortification pieces realized.");
                return;
            }

            // Realize routes through OutpostFoundationGenerator (already TGVRU-instrumented: it
            // render-verifies + logs each carving footprint, the invisible-blocker self-report).
            Guard.Try("Camp", $"{CampId} realize fortification ({_recipe.Count} pieces)",
                () => OutpostFoundationGenerator.Realize(_recipe, outpostRoot, ~0));

            FlowTrace.Step("Camp", $"{CampId} BuildFortification: realized {_recipe.Count} fortification piece(s) (persist={persist}).");

            if (persist)
            {
                PlayerPrefs.SetString(PrefRecipeKey + CampId, SerializeRecipe(_recipe));
                PlayerPrefs.Save();
            }
        }

        // Compact PlayerPrefs serialization of the recipe (PlacedStructureData is Core).
        // One record per ';' as "itemId,cellX,cellZ,yawSteps,level,yawOffset". Kept tiny
        // + dependency-free (no JSON lib) since this is the camp's existing PlayerPrefs lane.
        private static string SerializeRecipe(IReadOnlyList<PlacedStructureData> recipe)
        {
            if (recipe == null || recipe.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < recipe.Count; i++)
            {
                var r = recipe[i];
                if (i > 0) sb.Append(';');
                sb.Append(r.itemId).Append(',')
                  .Append(r.cellX).Append(',')
                  .Append(r.cellZ).Append(',')
                  .Append(r.yawSteps).Append(',')
                  .Append(r.level).Append(',')
                  .Append(r.yawOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static List<PlacedStructureData> DeserializeRecipe(string raw)
        {
            var list = new List<PlacedStructureData>();
            if (string.IsNullOrEmpty(raw)) return list;
            var records = raw.Split(';');
            foreach (var rec in records)
            {
                if (string.IsNullOrEmpty(rec)) continue;
                var f = rec.Split(',');
                if (f.Length < 6) continue;
                if (!int.TryParse(f[1], out int cx)) continue;
                if (!int.TryParse(f[2], out int cz)) continue;
                if (!int.TryParse(f[3], out int yaw)) continue;
                if (!int.TryParse(f[4], out int lvl)) continue;
                float.TryParse(f[5], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float yoff);
                list.Add(new PlacedStructureData(f[0], cx, cz, yaw, lvl, yoff));
            }
            return list;
        }

        // =====================================================================
        // STAGE 4 - DEFEND. After a build, spawn one counterattack on the outpost.
        // Survive it -> camp SECURED (one-time reward + persisted). Outpost razed
        // -> defense lost, camp stays claimed and is rebuildable.
        // =====================================================================

        // DESIGN PIVOT (owner 2026-06-16): flip a freshly-built camp to a PLAYER-OWNED base —
        // the replacement for the retired DEFEND stage. Idempotent. Sets Secured (the persisted
        // "this camp is held" flag the rest of the loop + UI read), grants the one-time
        // territory/economy benefit, and raises OnDefended so existing listeners (CampPromptUI,
        // difficulty scaling) fire EXACTLY as they did when a counterattack was repelled — just
        // without the broken wave in between.
        private void MarkOwned()
        {
            if (Secured) return;
            Secured = true;
            PlayerPrefs.SetString(PrefSecuredKey + CampId, "1");
            PlayerPrefs.Save();
            Debug.Log($"[ClaimableCamp] {CampId} is now a PLAYER-OWNED base (Defend stage retired).");

            // WO-106: territory multiplier + secured-count for difficulty scaling + passive power.
            EconomyService.Instance?.OnOutpostSecured();
            OnDefended?.Invoke(this);

            // The claimed base IS a harvest base — plant the direct-harvest nodes.
            SpawnHarvestNodes();
        }

        // =====================================================================
        // CLAIMED-BASE HARVEST NODES (owner 2026-06-16: "add a base with harvest
        // nodes after they clear the camp … static locations … seam it all").
        // One direct-harvest MineNode per resource, planted at STATIC local offsets
        // in the courtyard. Parented to the static camp anchor (CampSystem.CampAnchors)
        // so positions are deterministic; re-spawned on every owned (re)load of
        // OuterWorld, so they are effectively persistent / seamed into the world.
        // =====================================================================
        private static readonly MineResource[] CampNodeResources =
            { MineResource.Wood, MineResource.Iron, MineResource.Stone, MineResource.AetherCrystal };
        private bool _nodesSpawned;

        private void SpawnHarvestNodes()
        {
            using var _ = FlowTrace.Enter("Camp", $"{CampId} SpawnHarvestNodes");
            if (_nodesSpawned) { FlowTrace.Step("Camp", $"{CampId} SpawnHarvestNodes: already planted — no-op."); return; }
            _nodesSpawned = true;

            // Courtyard centre in LOCAL fort coords: the recipe lays cells 0..n-1 out
            // from the root origin, so the centre cell is at ((w-1)/2,(d-1)/2)*cellSize.
            float cx = (FortGridWidth - 1) * 0.5f * OutpostFoundationGenerator.CellSize;
            float cz = (FortGridDepth - 1) * 0.5f * OutpostFoundationGenerator.CellSize;
            Vector3 centre = new Vector3(cx, 0f, cz);
            Vector3[] spots =
            {
                centre + new Vector3( 1.6f, 0f,  1.6f),
                centre + new Vector3(-1.6f, 0f,  1.6f),
                centre + new Vector3( 1.6f, 0f, -1.6f),
                centre + new Vector3(-1.6f, 0f, -1.6f),
            };

            int planted = 0;
            for (int i = 0; i < CampNodeResources.Length; i++)
            {
                int idx = i;
                // G — guard each node so one bad MineNode spawn logs + is skipped, never aborting
                // the whole harvest base (which would leave the claimed camp income-less).
                if (Guard.Try("Camp", $"{CampId} plant harvest node {CampNodeResources[idx]}", () =>
                {
                    var go = new GameObject($"HarvestNode_{CampNodeResources[idx]}_{Region}");
                    // Inactive BEFORE AddComponent so MineNode.Awake/Start read the configured
                    // fields (Resource etc.) instead of the defaults — then activate to run them.
                    go.SetActive(false);
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = spots[idx];

                    var node = go.AddComponent<MineNode>();
                    node.Resource         = CampNodeResources[idx];
                    node.UseFiniteReserve = false;   // renewable base income
                    node.AutoBuildVisual  = true;    // shows the lightweight Harvest/<type> model
                    node.YieldPerExtract  = 5;
                    node.ExtractCooldown  = 8f;
                    node.TotalExtracts    = 0;        // 0 = infinite — a permanent base resource
                    node.RespawnSeconds   = 0f;

                    go.SetActive(true);

                    // V — the node must RENDER, else the hero can't see a resource to harvest
                    // (the invisible-marker class). AutoBuildVisual builds it active-side; a Warn
                    // self-reports if it produced no enabled renderer with a mesh.
                    // MineNodeVisual builds from Start(), after this activation callback returns.
                    // A synchronous check can only inspect the pre-build GameObject.
                    StartCoroutine(VerifyNodeRendersAfterStart(
                        go, $"node {CampNodeResources[idx]}", spots[idx]));
                })) planted++;
            }
            FlowTrace.Step("Camp", $"{CampId} planted {planted}/{CampNodeResources.Length} direct-harvest node(s) in the courtyard.");
        }

        // V — confirm a planted harvest node actually RENDERS (>=1 enabled renderer carrying a mesh),
        // else the hero sees no resource to harvest at a courtyard node. A Warn self-report to the
        // break-log; the node still functions (harvestable) either way.
        private void VerifyNodeRenders(GameObject go, string what, Vector3 localPos)
        {
            if (go == null) return;
            int enabledWithMesh = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool hasMesh = r.sharedMaterial != null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == null) hasMesh = false;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh == null) hasMesh = false;
                if (hasMesh) enabledWithMesh++;
            }
            if (enabledWithMesh == 0)
                FlowTrace.Warn("Camp",
                    $"INVISIBLE HARVEST NODE: {CampId} {what} at local {localPos} has 0 enabled renderers " +
                    "with a mesh — hero sees no resource to harvest (AutoBuildVisual produced nothing visible).");
        }

        // RETIRED (design pivot 2026-06-16) — no longer called. Kept so the counterattack
        // stage can be restored once attacker spawning at camp anchors is fixed.
        /// <summary>Runs after MineNodeVisual.Start has had a frame to build its renderer tree.</summary>
        private System.Collections.IEnumerator VerifyNodeRendersAfterStart(
            GameObject go, string what, Vector3 localPos)
        {
            yield return null;
            if (go == null) yield break;
            VerifyNodeRenders(go, what, localPos);
        }

        private void StartDefense()
        {
            if (_outpost == null) return;
            if (Secured) return;                       // already held - no re-raid
            if (_defense != null && !_defense.Resolved) return;  // a raid is already live

            _defense = gameObject.AddComponent<CampDefenseWave>();
            _defense.OnDefended += HandleDefended;
            _defense.OnLost += HandleDefenseLost;
            _defense.Begin(Region, ThreatLevel, _outpost, CampId);
        }

        private void HandleDefended(CampDefenseWave wave)
        {
            if (wave != null) wave.OnDefended -= HandleDefended;
            Secured = true;
            PlayerPrefs.SetString(PrefSecuredKey + CampId, "1");
            PlayerPrefs.Save();
            Debug.Log($"[ClaimableCamp] {CampId} SECURED - counterattack repelled.");

            // WO-106: tell the Economy class (EconomyService) so TerritoryMultiplier
            // and SecuredOutpostCount update for difficulty scaling + passive power.
            // The multiplier is then available to wave/enemy systems without new deps.
            EconomyService.Instance?.OnOutpostSecured();

            OnDefended?.Invoke(this);
        }

        private void HandleDefenseLost(CampDefenseWave wave)
        {
            if (wave != null) wave.OnLost -= HandleDefenseLost;
            // Outpost was razed (it Destroys itself at 0 HP). Camp stays claimed;
            // building a fresh outpost re-triggers the counterattack.
            _outpost = null;
            Debug.LogWarning($"[ClaimableCamp] {CampId} defense lost - outpost razed, rebuild to retry.");
            OnDefenseLost?.Invoke(this);
        }

        // =====================================================================
        // Persistence restore (PlayerPrefs only).
        // =====================================================================

        private void RestoreFromPrefs()
        {
            // A previously SECURED camp stays peaceful (no re-raid on reload).
            Secured = PlayerPrefs.GetString(PrefSecuredKey + CampId, null) == "1";

            // Claimed (and possibly built) takes precedence over merely cleared.
            string claimedRaw = PlayerPrefs.GetString(PrefClaimedKey + CampId, null);
            if (!string.IsNullOrEmpty(claimedRaw))
            {
                Stage = CampStage.Claimed;
                _visual?.SetStage(CampStage.Claimed);
                if (int.TryParse(claimedRaw, out int t) && t != (int)OutpostType.None)
                {
                    var type = (OutpostType)t;
                    var go = new GameObject($"Outpost_{type}_{Region}");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = Vector3.zero;
                    _outpost = go.AddComponent<Outpost>();
                    _outpost.Init(type, Region, ThreatLevel, buildPrimitiveVisual: false);

                    // Re-Realize the persisted catalog-piece fortification (or regenerate
                    // it if no recipe was saved, e.g. a pre-feature save). persist:false on
                    // restore — the recipe is already stored; reuse the deserialized one.
                    _recipe = DeserializeRecipe(PlayerPrefs.GetString(PrefRecipeKey + CampId, null));
                    BuildFortification(go.transform, OutpostTier.Wood, persist: _recipe.Count == 0);

                    // A restored outpost with no "owned" flag is a pre-pivot save that was
                    // mid-counterattack. The DEFEND stage is retired -> just flip it owned
                    // (no re-raid), so it loads as the player's base.
                    if (!Secured) MarkOwned();
                    // Re-plant the courtyard harvest nodes (idempotent) so an already-owned
                    // camp restores its harvest base — the seam that makes them persistent.
                    SpawnHarvestNodes();
                }
                return;
            }

            if (PlayerPrefs.GetString(PrefClearedKey + CampId, null) == "1")
            {
                Stage = CampStage.Cleared;
                _visual?.SetStage(CampStage.Cleared);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Stage == CampStage.Hostile
                ? new Color(0.9f, 0.2f, 0.2f, 0.35f)
                : new Color(0.2f, 0.9f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, CampRadius);
        }
#endif
    }
}
