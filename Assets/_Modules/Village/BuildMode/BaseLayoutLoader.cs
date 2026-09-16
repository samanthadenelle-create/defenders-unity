// =============================================================================
// BaseLayoutLoader — the runtime twin of VillageSceneBuilder.BuildBuildings.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WO-108 P0: reads GameState.BaseLayout and instantiates one structure per
// record via the ONE creation path (StructureFactory.Create over CatalogEntry).
// Places at PlacementGrid.CellToWorld(cell) + yawSteps*90, adds a footprint
// collider + a NavMeshObstacle (carve, no per-place rebake) + a PlacedStructure
// marker, and occupies the grid cells.
//
//   • EMPTY BaseLayout  → no-op: the default VillageSceneBuilder village (the
//                         seed) stands untouched.
//   • NON-EMPTY layout  → REPLACES the builder's output at runtime (this is the
//                         player's edited base) by spawning under a dedicated
//                         root the loader owns.
//
// Does NOT touch VillageSceneBuilder or any .unity file — it reads the same
// catalog data the builder authors from and rebuilds at runtime, per the WO.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;   // active-scene name — gate the village base-layout to its own scene
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;   // TGVRU: instrument the base-layout spawn seam (§12)

namespace DeNelle.Village
{
    /// <summary>
    /// Instantiates the player's persisted <see cref="GameState.BaseLayout"/> at
    /// runtime. The data-driven counterpart to the editor village builder; an empty
    /// layout falls through to the default village seed.
    /// </summary>
    public sealed class BaseLayoutLoader : MonoBehaviour
    {
        public static BaseLayoutLoader Instance { get; private set; }

        [Tooltip("Auto-load GameState.BaseLayout on Start. Disable to drive load manually (tests).")]
        [SerializeField] private bool _loadOnStart = true;

        // HUB-SCOPE GUARD (build-defense regression, owner playtest 2026-06-19; RESCOPED
        // F8-39/COV-001 2026-07-08): GameState.BaseLayout is the PLAYER'S HOME base — a single
        // GLOBAL list (NOT scene-scoped). Under the combat pivot the base is now BUILT and
        // COMMITTED in the HOME HUB (SceneRouter.Castle = MainCastle_Hall, or Main_Castle_Overworld
        // when ff.MergedWorld is ON) — see BuildModeController.CommitLayout ("MainCastle_Hall is the
        // HOME hub where the player's base IS built"). So the home hub MUST replay its own base on a
        // reload; skipping it was F8-39 ("towers vanish on death->GoCastle return, all reappear when
        // I add one"). MainCastle_Hall is therefore REMOVED from this skip set (proven by
        // TowerRespawnRegression). The set now only names LEGACY pure-hub variants that are NOT a
        // base-build scene, so a stray place-in-a-non-base-hub can't dump the home base into them.
        private static readonly HashSet<string> _hubScenesNoBaseLayout = new HashSet<string>
        {
            "CastleHub",
        };

        private Transform _root;            // parent for all loaded structures
        private readonly List<PlacedStructure> _loaded = new List<PlacedStructure>();
        private sealed class AuthoredBinding
        {
            public Transform root;
            public PlacedStructure placed;
            public Vector3 initialLocalPosition, initialLocalScale;
            public Quaternion initialLocalRotation;
            public Vector2Int cell, footprint;
        }
        private readonly List<AuthoredBinding> _authoredBindings = new List<AuthoredBinding>();
        private GameStateService _observedStateService;
        private bool _hasAuthoredHistory;

        // Guard: the persisted set is instantiated exactly ONCE per session. After the
        // initial load, incremental adds go through Spawn() (one piece). LoadFromState()
        // must NEVER re-run Rebuild() (destroy-all/respawn-all) — that mass-rebuild is the
        // cause of every prior piece (+ a stale prior-session building) popping on at once.
        private bool _loadedOnce;

        /// <summary>The structures this loader currently has in the scene.</summary>
        public IReadOnlyList<PlacedStructure> Loaded => _loaded;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            ObserveStateReplacement();
            // F8-39: a FRESH loader instance in Awake means the scene (re)loaded — the death→EVAC
            // GoCastle route or any hub load spins up a new loader whose Start() decides the replay.
            // Capturing the scene here shows whether the respawn came back into a scene that WILL
            // replay the base (village) or one that SKIPS it (hub-scope guard) — the "towers gone" split.
            FlowTrace.Step("BaseLayout",
                $"Awake: loader instance created in scene '{SceneManager.GetActiveScene().name}' " +
                $"(_loadedOnce={_loadedOnce}) — Start() will decide replay.");
        }

        private void OnDestroy()
        {
            if (_observedStateService != null) _observedStateService.StateReplaced.RemoveListener(OnStateReplaced);
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            ObserveStateReplacement();
            // F8-39: name the count the loader is about to (not) replay, so a post-respawn/reload
            // capture proves whether the visual rebuild fires (Rebuild logs the built count) or is
            // skipped (hub guard / empty layout Warn). Pairs with LoadFromState's own lines.
            var st0 = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            int persisted = st0 != null && st0.BaseLayout != null ? st0.BaseLayout.Count : 0;
            FlowTrace.Step("BaseLayout",
                $"Start: loadOnStart={_loadOnStart}, persisted BaseLayout={persisted}, live loaded={_loaded.Count}, " +
                $"scene='{SceneManager.GetActiveScene().name}'.");
            if (_loadOnStart) LoadFromState();
        }

        /// <summary>Ensure a loader exists in the scene (called by BuildModeController).</summary>
        public static BaseLayoutLoader EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("BaseLayoutLoader");
            return go.AddComponent<BaseLayoutLoader>();
        }

        private Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("PlayerBaseLayout");
                    _root = go.transform;
                }
                return _root;
            }
        }

        /// <summary>
        /// (Re)build the scene from <see cref="GameState.BaseLayout"/>. An empty or
        /// absent layout is a no-op (the default village seed stands). A non-empty
        /// layout clears any previously-loaded structures and rebuilds.
        /// </summary>
        public void LoadFromState()
        {
            // Personal property has its own revisioned records and reconstruction owner.
            if (SceneManager.GetActiveScene().name == DeNelle.Village.World.Camps.OwnedTownScenePose.SceneName) return;
            if (SceneManager.GetActiveScene().name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName) return;
            // Idempotent: only the FIRST call instantiates the persisted set. Any later
            // call early-returns so we never re-run Rebuild()'s destroy-all/respawn-all
            // (which made every prior piece + a stale prior-session building pop on at
            // once). Subsequent placements add ONE piece each via Spawn() (the add path).
            if (_loadedOnce) return;
            _loadedOnce = true;

            // HUB-SCOPE GUARD: never replay the home base into a LEGACY pure-hub variant that is
            // NOT a base-build scene (CastleHub — see _hubScenesNoBaseLayout). BaseLayout is a
            // single GLOBAL list, so a stray build-in-a-non-base-hub would dump the whole home base
            // there. The HOME hub (SceneRouter.Castle = MainCastle_Hall / Main_Castle_Overworld) is
            // NOT in the set — it is where the base is built/committed, so it MUST replay (F8-39 fix).
            // Self-reports so a future regression (a new hub name mis-added) is visible in capture, §12.
            string scene = SceneManager.GetActiveScene().name;
            if (_hubScenesNoBaseLayout.Contains(scene))
            {
                int n = GameStateService.Instance != null && GameStateService.Instance.State != null
                    && GameStateService.Instance.State.BaseLayout != null
                        ? GameStateService.Instance.State.BaseLayout.Count : 0;
                FlowTrace.Warn("BaseLayout",
                    $"LoadFromState: SKIPPED in hub scene '{scene}' — the village base ({n} placed " +
                    "structure(s)) does NOT replay in a hub (would re-add prior-session towers).");
                return;
            }

            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            var layout = state != null ? state.BaseLayout : null;
            if (layout == null || layout.Count == 0)
            {
                // Empty → fall through to the default village (the seed). No-op.
                FlowTrace.Step("BaseLayout",
                    $"LoadFromState: scene '{scene}' has an empty BaseLayout — default seed stands (no replay).");
                return;
            }

            FlowTrace.Step("BaseLayout",
                $"LoadFromState: scene '{scene}' — replaying {layout.Count} persisted village structure(s).");
            Rebuild(layout);
        }

        /// <summary>
        /// Drop a structure from the live loaded-set WITHOUT freeing its cells or
        /// destroying it (the caller — BuildModeController.SellSelected — already
        /// frees the grid + destroys the object). Prevents a double-free on Exit.
        /// </summary>
        public void Forget(PlacedStructure ps)
        {
            if (ps == null) return;
            _loaded.Remove(ps);
        }

        /// <summary>Destroy currently-loaded structures and free their grid cells.</summary>
        public void ClearLoaded()
        {
            // F8-39: this is the ONLY controlled mass-destroy path (called by Rebuild). Log the
            // count + caller so a PlacedStructure.OnDestroy attributed to ClearLoaded reads as
            // EXPECTED, and a teardown attributed to anything else during a death is the defect.
            if (_loaded.Count > 0)
                FlowTrace.Step("Structures",
                    $"ClearLoaded: destroying {_loaded.Count} loaded structure(s) (controlled rebuild teardown) " +
                    $"by {DeathTrace.Caller(1)} in scene '{SceneManager.GetActiveScene().name}'.");
            var grid = PlacementGrid.Instance;
            foreach (var ps in _loaded)
            {
                if (ps == null) continue;
                grid?.Free(ps.gridCell, ps.footprint);
                Destroy(ps.gameObject);
            }
            _loaded.Clear();
        }

        /// <summary>Build every record in <paramref name="layout"/> into the scene.</summary>
        public void Rebuild(IReadOnlyList<PlacedStructureData> layout)
        {
            ReconcileAuthoredBindings(layout);
            ClearLoaded();

            var grid = PlacementGrid.Instance;
            if (grid == null)
            {
                // The grid is the source of truth for cell↔world; create one if the
                // build flow has not yet (loader can run before BuildModeController).
                grid = new GameObject("PlacementGrid").AddComponent<PlacementGrid>();
            }
            grid.ClearAll();

            int built = 0;
            int withheld = 0;   // WO-673 — migration-managed records deliberately not replayed
            for (int i = 0; i < layout.Count; i++)
            {
                // WO-673 L3 DOUBLE-SPAWN GUARD (docs/WO673_ARCHITECTURE_REVIEW.md §3): a
                // migration-MANAGED record replays only while the bake/injector standdown is
                // active (marker set + not the migration load itself — always-on since
                // WO-682 removed ff.strategicplacement). Otherwise the bake/injector owns
                // that structure this session — replaying the record too would spawn it
                // twice. Non-managed records (towers/walls/defenses) are untouched by
                // this filter.
                if (!StrategicPlacementMigration.ShouldReplayRecord(layout[i].itemId))
                {
                    withheld++;
                    FlowTrace.Step("BaseLayout",
                        $"Rebuild: record '{layout[i].itemId}' WITHHELD — migration-managed id and standdown " +
                        "is not active (bake/injector owns it this session; no double-spawn).");
                    continue;
                }
                if (Spawn(layout[i], grid) != null) built++;
            }
            // U + R: a PARTIAL base (built < count) means some of the player's persisted buildings
            // silently vanished on load — the worst kind of "my base is wrong" bug. Warn (not a
            // happy Log) when any record failed so the shortfall self-reports; each failing record
            // already Fail'd in Spawn with its id. Deliberately-withheld WO-673 records (standdown
            // inactive — the bake owns them) are NOT a shortfall; they're excluded from the check.
            if (built < layout.Count - withheld)
                FlowTrace.Warn("BaseLayout",
                    $"Rebuild: loaded only {built}/{layout.Count - withheld} replayable structure(s) — " +
                    $"{layout.Count - withheld - built} record(s) FAILED to spawn (see prior FAILED lines for ids); " +
                    $"{withheld} withheld (WO-673 standdown inactive).");
            else
                FlowTrace.Step("BaseLayout",
                    $"Rebuild: loaded {built}/{layout.Count - withheld} placed structure(s) from BaseLayout" +
                    (withheld > 0 ? $" ({withheld} migration-managed record(s) withheld — bake owns them)." : "."));
        }

        /// <summary>
        /// Spawn one record: resolve its CatalogEntry, build it via StructureFactory
        /// at the cell centre + yaw, then add footprint collider + NavMeshObstacle +
        /// the PlacedStructure marker, and occupy the grid. Returns null on a missing
        /// entry (logged), so a stale id degrades gracefully.
        /// </summary>
        public PlacedStructure Spawn(PlacedStructureData data, PlacementGrid grid)
        {
            if (!string.IsNullOrEmpty(data.authoredSourceId))
                return Guard.Try<PlacedStructure>("BaseLayout", "bind authored adoption record",
                    () => BindExistingAuthored(data, grid), fallback: null);
            var placed = SpawnForLayout(data, grid, Root, replayStoryServices: true);
            if (placed != null) _loaded.Add(placed);
            return placed;
        }

        private void ObserveStateReplacement()
        {
            var service = GameStateService.Instance;
            if (service == _observedStateService) return;
            if (_observedStateService != null) _observedStateService.StateReplaced.RemoveListener(OnStateReplaced);
            _observedStateService = service;
            if (service != null) service.StateReplaced.AddListener(OnStateReplaced);
        }

        private void OnStateReplaced()
        {
            if (!_hasAuthoredHistory || gameObject.scene != SceneManager.GetActiveScene() ||
                SceneManager.GetActiveScene().name != DeNelle.Core.SceneRouter.Castle) return;
            var layout = _observedStateService != null ? _observedStateService.State?.BaseLayout : null;
            // State replacement is an explicit reconstruction event, not another
            // placement-time LoadFromState request. The once-only load latch stays intact.
            Guard.Try("BaseLayout", "reconstruct after authored state replacement", () =>
            {
                Rebuild(layout ?? new List<PlacedStructureData>());
                StructureSingleton.EnforceAll();
            });
        }

        private void ReconcileAuthoredBindings(IReadOnlyList<PlacedStructureData> layout)
        {
            var grid = PlacementGrid.Instance;
            for (int i = _authoredBindings.Count - 1; i >= 0; i--)
            {
                var binding = _authoredBindings[i];
                grid?.Free(binding.cell, binding.footprint);
                if (binding.root == null) { _authoredBindings.RemoveAt(i); continue; }
                int records = 0;
                bool retained = true;
                if (layout != null)
                    for (int n = 0; n < layout.Count; n++)
                        if (layout[n].itemId == "barracks")
                        {
                            records++;
                            retained &= AuthoredCastleStorefront.IsAuthoredBarracksRecord(layout[n]) &&
                                TryGetAuthoredFootprint(binding.root, grid, layout[n].authoredPose, out var cell, out _) &&
                                cell.x == layout[n].cellX && cell.y == layout[n].cellZ;
                        }
                if (records == 1 && retained) continue;
                if (binding.placed != null)
                {
                    // Runtime Destroy is deferred. Clear identity immediately so a
                    // singleton sweep cannot count the previous save's metadata.
                    binding.placed.itemId = null;
                    binding.placed.authoredSourceId = null;
                    binding.placed.enabled = false;
                    if (Application.isPlaying) Destroy(binding.placed); else DestroyImmediate(binding.placed);
                }
                binding.root.localPosition = binding.initialLocalPosition;
                binding.root.localRotation = binding.initialLocalRotation;
                binding.root.localScale = binding.initialLocalScale;
                _authoredBindings.RemoveAt(i);
                FlowTrace.Step("Barracks", "Removed obsolete adoption metadata and restored authored scene pose; original geometry retained.");
            }
        }

        /// <summary>Measure the exact proposed authored pose before mutating scene geometry.</summary>
        public static bool TryGetAuthoredFootprint(Transform root, PlacementGrid grid, AuthoredStructurePose pose,
            out Vector2Int cell, out Vector2Int footprint)
        {
            cell = default;
            footprint = default;
            if (root == null || grid == null || pose == null || grid.cellSize <= 0f) return false;
            bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
            float norm = pose.qx * pose.qx + pose.qy * pose.qy + pose.qz * pose.qz + pose.qw * pose.qw;
            if (!Finite(pose.x) || !Finite(pose.y) || !Finite(pose.z) || !Finite(norm) || norm < 0.0001f ||
                !Finite(pose.sx) || !Finite(pose.sy) || !Finite(pose.sz) || pose.sx == 0 || pose.sy == 0 || pose.sz == 0) return false;
            var position = new Vector3(pose.x, pose.y, pose.z);
            var rotation = new Quaternion(pose.qx, pose.qy, pose.qz, pose.qw).normalized;
            var scale = new Vector3(pose.sx, pose.sy, pose.sz);
            var parent = root.parent;
            Matrix4x4 proposed = parent == null ? Matrix4x4.TRS(position, rotation, scale)
                : parent.localToWorldMatrix * Matrix4x4.TRS(parent.InverseTransformPoint(position), Quaternion.Inverse(parent.rotation) * rotation, scale);
            Matrix4x4 change = proposed * root.worldToLocalMatrix;
            bool found = false;
            Bounds bounds = default;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                var local = renderer.localBounds;
                Matrix4x4 matrix = change * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z)) return false;
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!found || bounds.size.x <= 0 || bounds.size.z <= 0) return false;
            cell = grid.WorldToCell(bounds.min);
            int endX = Mathf.CeilToInt((bounds.max.x - grid.origin.x) / grid.cellSize);
            int endZ = Mathf.CeilToInt((bounds.max.z - grid.origin.z) / grid.cellSize);
            footprint = new Vector2Int(Mathf.Max(1, endX - cell.x), Mathf.Max(1, endZ - cell.y));
            return grid.InBounds(cell, footprint);
        }

        /// <summary>Bind explicit adoption provenance to existing geometry; never loader-owned destruction.</summary>
        public PlacedStructure BindExistingAuthored(PlacedStructureData data, PlacementGrid grid)
        {
            ObserveStateReplacement();
            if (!AuthoredCastleStorefront.IsAuthoredBarracksRecord(data) || grid == null ||
                SceneManager.GetActiveScene().name != DeNelle.Core.SceneRouter.Castle)
            {
                FlowTrace.Fail("BaseLayout", "Authored adoption record has an unsupported identity, missing pose/grid, or non-story scene; no replacement spawned.");
                return null;
            }
            var root = AuthoredCastleStorefront.Find("CastleBarracks", includeInactive: true);
            var marker = root != null ? root.GetComponent<AuthoredCastleStorefront>() : null;
            var building = root != null ? root.GetComponent<Building>() : null;
            if (marker == null || !marker.PreserveAuthoredVisual || marker.CanonicalId != data.authoredSourceId ||
                building == null || building.BuildingId != data.itemId ||
                !AuthoredCastleStorefront.IsBoundAuthoredRoot(root, data.itemId))
            {
                FlowTrace.Fail("BaseLayout", "Authored barracks binding requires a unique marked root, matching Building, and explicit saved provenance; no replacement spawned.");
                return null;
            }
            var p = data.authoredPose;
            if (!TryGetAuthoredFootprint(root, grid, p, out var cell, out var footprint) ||
                cell.x != data.cellX || cell.y != data.cellZ)
            {
                FlowTrace.Fail("BaseLayout", "Authored barracks pose/geometry/grid origin is invalid; leaving scene geometry untouched.");
                return null;
            }
            AuthoredBinding binding = null;
            for (int i = 0; i < _authoredBindings.Count; i++)
                if (_authoredBindings[i].root == root) { binding = _authoredBindings[i]; break; }
            if (binding == null)
            {
                binding = new AuthoredBinding
                {
                    root = root, initialLocalPosition = root.localPosition,
                    initialLocalRotation = root.localRotation, initialLocalScale = root.localScale
                };
                _authoredBindings.Add(binding);
                _hasAuthoredHistory = true;
            }
            else grid.Free(binding.cell, binding.footprint);
            root.SetPositionAndRotation(new Vector3(p.x, p.y, p.z), new Quaternion(p.qx, p.qy, p.qz, p.qw));
            root.localScale = new Vector3(p.sx, p.sy, p.sz);
            var placed = root.GetComponent<PlacedStructure>();
            if (placed == null || !placed.enabled) placed = root.gameObject.AddComponent<PlacedStructure>();
            placed.itemId = data.itemId;
            placed.authoredSourceId = data.authoredSourceId;
            placed.gridCell = cell;
            placed.footprint = footprint;
            binding.placed = placed;
            binding.cell = cell;
            binding.footprint = footprint;
            placed.yawSteps = data.yawSteps;
            placed.yawOffset = data.yawOffset;
            placed.worldY = data.worldY;
            placed.wallMounted = data.wallMounted;
            placed.level = Mathf.Max(1, data.level);
            placed.sellValue = (CatalogRegistry.Get(data.itemId)?.repo?.buildCost ?? 0) / 2;
            if (root.GetComponent<BuildingInteractable>() == null) root.gameObject.AddComponent<BuildingInteractable>();
            var controller = Object.FindAnyObjectByType<VillageController>();
            if (controller != null && controller.gameObject.scene == root.gameObject.scene)
                controller.RegisterBuilding(building);
            BuildModeController.ApplyTierStats(placed, placed.level);
            root.gameObject.SetActive(BarracksUnlock.IsUnlocked);
            if (BarracksUnlock.IsUnlocked)
                HubStructureVisualInjector.RestoreBakedTwinPhysics(root.gameObject, marker.LegacyName);
            grid.Occupy(placed.gridCell, placed.footprint, placed.itemId);
            if (DeNelle.Core.FeatureFlags.BuildTimers && BuildTimerService.Instance != null &&
                BuildTimerService.Instance.IsBuilding(UnderConstructionVisual.KeyFor(data)) &&
                placed.GetComponent<UnderConstructionVisual>() == null)
                UnderConstructionVisual.Attach(placed, UnderConstructionVisual.KeyFor(data));
            FlowTrace.Step("Barracks", "Bound explicit authored barracks record to original geometry/pose; excluded from loader destruction and tier reskin.");
            return placed;
        }

        // Shared construction replay without a story-layout loader or story timer/vendor side effects.
        public static PlacedStructure SpawnForLayout(PlacedStructureData data, PlacementGrid grid,
            Transform parent, bool replayStoryServices = false)
        {
            var entry = CatalogRegistry.Get(data.itemId);
            if (entry == null)
            {
                // U + R: a stale/renamed id drops one of the player's buildings on load. Fail-loud
                // naming the id (skip-not-abort: the rest of the layout still builds).
                FlowTrace.Fail("BaseLayout",
                    $"Spawn: BaseLayout id '{data.itemId}' not in registry — structure skipped (one building lost).");
                return null;
            }
            // WO-989: rewrite persisted legacy ids to canonical so the next save is clean.
            string canonicalId = CatalogRegistry.CanonicalStructureId(data.itemId);
            if (!string.IsNullOrEmpty(canonicalId) && canonicalId != data.itemId)
            {
                FlowTrace.Step("BaseLayout",
                    $"WO-989 spawn id migrate '{data.itemId}' -> '{canonicalId}'");
                data.itemId = canonicalId;
            }

            var cell = new Vector2Int(data.cellX, data.cellZ);
            Vector3 pos = grid.CellToWorld(cell);
            // SEAT HEIGHT — CellToWorld returns the flat grid plane (Y = origin.y, normally 0).
            // A persisted worldY != 0 seats the structure ELEVATED (e.g. a defense on a wall-walk
            // top — the defensive posture). Old records carry worldY = 0 → ground placement,
            // unchanged. Approximately() so a 0 from an old save is treated as "use the grid plane".
            if (!Mathf.Approximately(data.worldY, 0f)) pos.y = data.worldY;
            // WO-673 L5 — the persisted yaw round-trip: world yaw = yawSteps × 90 + yawOffset.
            // A 45° facing commits as (yawSteps = facing/2, yawOffset = 45), so it replays
            // exactly; old records (yawOffset 0) replay byte-identically.
            float yawDeg = data.yawSteps * 90f + data.yawOffset;
            var rot = Quaternion.Euler(0f, yawDeg, 0f);

            GameObject go = null;
            // G(uard the Build): StructureFactory.Create can throw on a corrupt/missing prefab;
            // an unguarded throw aborts the whole Rebuild loop (every LATER building is lost).
            Guard.Try("BaseLayout", $"StructureFactory.Create '{data.itemId}'",
                () => go = StructureFactory.Create(entry, new Pose(pos, rot), parent));
            if (go == null)
            {
                // THE WORST SEAM (was a fully-silent `if (go == null) return null;`): the factory
                // produced no body for a VALID catalog entry — the player's building vanishes with
                // zero diagnostics. Fail-loud naming the entry id so the dead build self-reports.
                FlowTrace.Fail("BaseLayout",
                    $"Spawn: StructureFactory.Create returned null for entry '{data.itemId}' at cell ({cell.x},{cell.y}) — " +
                    "structure NOT built (one building lost; check the entry's prefabPath).");
                return null;
            }

            // FIX (footprint from CORRECTED bounds) — measure the UPRIGHT, OrientationFix-
            // applied mesh so the footprint matches what the ghost showed (a lying-down
            // prefab would report a long, wrong footprint and refuse to sit near a wall).
            // WO-673 L5 (45° rotation) — TWO footprints with distinct jobs:
            //   • blockerFootprint = the model's own (unrotated) size. The blocker box +
            //     NavMeshObstacle are LOCAL to the yawed root (Pose above), so they rotate
            //     WITH the model — the carve matches the rotated orientation by construction.
            //     Inflating this one would over-carve diagonally past the actual mesh.
            //   • footprint (the grid CLAIM) = rotation-honest cells covering the rotated
            //     mesh's world AABB (×√2 at diagonal yaws) — must match what IsValidPlacement
            //     claimed at place time, or a reload would Occupy fewer cells than validity
            //     promised and a later placement could overlap the diagonal.
            // WO-972 — the CLAIM metric (identical to the measured mesh for every row except a
            // Wall, which claims off its authored placement.footprint). This MUST be the same
            // call BuildModeController.IsValidPlacement claims with, or a reload would Occupy a
            // different cell set than placement promised and the run would re-break on load.
            // The BLOCKER is unaffected in practice: AddFootprintBlocker sizes the box as
            // Clamp(rendered * 0.85, cellSize, claim), which is 3x3 m for a wall at either claim.
            // WO-986: same non-square claim as BuildModeController (save replay must match place).
            Vector2 claimXz = StructureFactory.MeasureClaimFootprintXZ(entry);
            Vector2Int blockerFootprint = grid.FootprintCells(claimXz);
            Vector2Int footprint = grid.FootprintCells(claimXz, yawDeg);

            // A structure that WILL MOVE must NOT carve the navmesh: the carving NavMeshObstacle on
            // the same root as the caravan's NavMeshAgent carved the mesh out from under its own
            // agent — isOnNavMesh false forever, follow dead on arrival (2026-08-15 review finding
            // #1). It KEEPS the box collider (tap-select + contact-damage collider-of-record) and
            // the visual-collider strip; only the static carve is skipped.
            //
            // ⛔ WO-1424 — THE TEST IS "WILL IT MOVE?", NOT "DOES THE COMPONENT EXIST?". The
            // caravan's follow is now split by context (owner ruling 2026-09-06: "it slow follows
            // as a combat attack item as defensive item it stationary"), so HealingCaravanMobility
            // is present on BOTH the offensive escort AND the stationary town structure. Keying the
            // carve on mere presence would leave the DEFENSIVE caravan a 3x4x3 non-trigger collider
            // with no carving obstacle — the documented shape that wedges pets and NPCs forever
            // (PetHeroLeash.cs:556) — i.e. it would have swapped the hero-pin for a pet-pin.
            // FollowsHero is LATCHED in the component's Awake (which has already run: AddComponent
            // in StructureFactory.Create runs Awake synchronously, before Create returns), so this
            // read and the agent/obstacle decisions inside the component can never disagree.
            var caravan = go.GetComponent<HealingCaravanMobility>();
            bool mobile = caravan != null && caravan.FollowsHero;
            AddFootprintBlocker(go, blockerFootprint, grid.cellSize, carveNavMesh: !mobile);
            if (mobile)
                FlowTrace.Step("BaseLayout",
                    $"'{go.name}' is MOBILE (HealingCaravanMobility, OFFENSIVE follow mode) — NavMesh carve skipped so the agent keeps its mesh.");
            else if (caravan != null)
                FlowTrace.Step("BaseLayout",
                    $"'{go.name}' is a STATIONARY (DEFENSIVE) healing caravan — NavMesh carve APPLIED so pets/NPCs path around it instead of wedging on its collider.");

            // "towers shoot through walls" fix (owner 2026-07): a build-mode WALL or GATE must sit on
            // the "Structure" physics layer so the towers' line-of-sight linecast (masked to
            // "Structure") HITS it. Build-mode walls attach a bare WallSegment WITHOUT Configure()
            // (StructureFactory.AttachBehaviorImpl), so WallSegment.RebuildCollider's own layer set
            // never runs for them — this is where the placed-base wall/gate gets the layer. SCOPED to
            // walls + gates ONLY (component probe) so friendly buildings/towers never occlude tower
            // fire. The footprint BoxCollider is the collider-of-record on the ROOT `go` (child
            // colliders are stripped in AddFootprintBlocker), so the root layer is sufficient.
            if (go.GetComponent<WallSegment>() != null || go.GetComponent<Gate>() != null)
            {
                int structureLayer = LayerMask.NameToLayer("Structure");
                if (structureLayer >= 0)
                {
                    go.layer = structureLayer;
                    FlowTrace.Step("Structure",
                        $"'{go.name}' placed on the \"Structure\" layer (wall/gate LoS — towers can no longer shoot through it).");
                }
            }

            var ps = go.AddComponent<PlacedStructure>();
            ps.itemId = data.itemId;
            ps.gridCell = cell;
            ps.footprint = footprint;
            ps.yawSteps = data.yawSteps;
            ps.yawOffset = data.yawOffset;   // WO-673 L5 — keep the exact facing on the live marker (move/save round-trip)
            ps.level = Mathf.Max(1, data.level);
            ps.worldY = data.worldY;
            ps.wallMounted = data.wallMounted;
            ps.sellValue = (entry.repo != null ? entry.repo.buildCost : 0) / 2;

            // ELEVATION PERK — a defense seated on a wall-walk top gets the high-ground range/LOS
            // bonus. Applied HERE (not at place-time) so it covers both fresh placement (Place →
            // Spawn) AND reload from save, and because it is a MULTIPLIER it survives tier upgrades
            // (ApplyTierStats recomputes the base Range from the catalog without touching it). Gated
            // on the explicit wallMounted flag — NOT worldY != 0 — so a structure on merely raised
            // terrain never wrongly gets the bonus. Bounded to +25%.
            if (data.wallMounted)
            {
                const float kWallWalkRangeMult = 1.25f;
                var dt = go.GetComponent<DefenseTower>();
                if (dt != null) dt.ElevationRangeMult = kWallWalkRangeMult;
                var at = go.GetComponent<ArcaneTower>();
                if (at != null) at.ElevationRangeMult = kWallWalkRangeMult;
            }

            // DEF-208 — 3-tier visual progression. When the catalog authors a per-tier MODEL
            // (upgradeVisualPath — owner F8 2026-07-06), the reload swaps to it and the legacy
            // scale/tint step is skipped (the model IS the progression); otherwise the classic
            // taller/tinted read applies (1 bronze · 2 silver · 3 gold). Reskin BEFORE Apply
            // so Apply collects the new model's renderers.
            bool tierModel = ps.level >= 2 && StructureFactory.ReskinForLevel(go, entry, ps.level);
            var tier = go.AddComponent<StructureTierVisual>();
            tier.Apply(tierModel ? 1 : ps.level);
            ps.TierVisual = tier;

            // S5 — re-assert the per-tier GAMEPLAY stats so a structure saved above level 1
            // reloads at its tier (tower range/damage, wall toughness), not the base stats
            // StructureFactory attached. No-op for a level-1 structure.
            if (ps.level > 1)
                BuildModeController.ApplyTierStats(ps, ps.level);

            grid.Occupy(cell, footprint, data.itemId);

            // WO-612: a structure saved mid-construction re-arms its scaffold on load.
            // The service's offline-fair sweep runs before this (it completes overdue
            // jobs on state load), so IsBuilding == true only for genuinely unfinished
            // jobs. Fresh placements are keyed AFTER Spawn (Place calls StartBuild
            // post-charge), so this is load-path only — no double-attach.
            if (replayStoryServices && DeNelle.Core.FeatureFlags.BuildTimers && BuildTimerService.Instance != null
                && BuildTimerService.Instance.IsBuilding(UnderConstructionVisual.KeyFor(data)))
                UnderConstructionVisual.Attach(ps, UnderConstructionVisual.KeyFor(data));

            // WO (owner device felt-test 2026-07-17): RELOAD/hub-enter path had NO vendor NPC for
            // collector buildings (Farm/Lumbermill/Forge). Fresh placement spawns one via
            // BuildModeController.Place -> NotifyBuildingPlaced, but this reload path recreated the
            // structure via StructureFactory.Create ALONE and never notified the injector — and the
            // injector's AnchorVendorsToPlacedBuildings poll only enumerates Building components, which
            // collectors do NOT carry (they attach a ResourceCollector, no Building), so the poll
            // awaited them forever. Notify HERE, symmetric with fresh placement: data.itemId is the
            // catalog id RoleForBuildingId maps (collector_farm->Windmill, etc). NotifyBuildingPlaced
            // is hub-scene-gated + idempotent per-building via VendorSeatMarker, so it will NOT
            // double-spawn on the GameplayBuildings the poll already handles. Guarded like the Place
            // site so a throwing injector can never abort the layout Rebuild loop.
            FlowTrace.Step("Vendor",
                $"reload-notify vendor for placed structure id='{data.itemId}' (BaseLayoutLoader.Spawn).");
            if (replayStoryServices)
                Guard.Try("BaseLayout", "spawn vendor NPC for reloaded building",
                    () => CastleVendorNpcInjector.NotifyBuildingPlaced(data.itemId, ps.transform));

            return ps;
        }

        /// <summary>
        /// Mirror of VillageSceneBuilder.AddBuildingFootprintCollider: a box that
        /// covers the footprint, plus a NavMeshObstacle that CARVES the baked
        /// navmesh at runtime (no per-place rebake — the WO's mobile-safe path).
        /// The gate-clearance rule (PlacementGrid / BuildModeController) keeps a
        /// spawn→Heart lane open, so carving never fully walls off the enemy path.
        /// </summary>
        private static void AddFootprintBlocker(GameObject go, Vector2Int footprintCells, float cellSize,
                                                bool carveNavMesh = true)
        {
            float w = Mathf.Max(1, footprintCells.x) * cellSize;
            float d = Mathf.Max(1, footprintCells.y) * cellSize;

            // Owner F8/felt 2026-07-13 ("ground around certain ones extends into what looks
            // like empty space making navigation impossible"): the cell claim derives from
            // the model's AABB, which INFLATES for models carrying an off-axis roll (the
            // Farm's 212° is ~32° off-axis -> AABB ×~1.3). Blocking walkability with that
            // inflated square leaves un-walkable EMPTY grass around the visible walls. So:
            // the BLOCKER (collider + nav carve) sizes to the visual's ACTUAL rendered
            // bounds in root-local XZ (+small margin), while the grid CLAIM keeps the honest
            // rotated-AABB cells (placement validity unchanged — WO-673 L5). Fallback to the
            // cell rectangle when no renderers are found.
            float cx = 0f, cz = 0f;
            Guard.Try("Structure", "blocker-from-rendered-bounds", () =>
            {
                var rends = go.GetComponentsInChildren<Renderer>(true);
                if (rends == null || rends.Length == 0) return;
                bool any = false;
                float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
                foreach (var r in rends)
                {
                    if (r == null) continue;
                    Bounds b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3(
                            (i & 1) == 0 ? b.min.x : b.max.x,
                            (i & 2) == 0 ? b.min.y : b.max.y,
                            (i & 4) == 0 ? b.min.z : b.max.z);
                        Vector3 local = go.transform.InverseTransformPoint(corner);
                        if (!any) { minX = maxX = local.x; minZ = maxZ = local.z; any = true; }
                        else
                        {
                            minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x);
                            minZ = Mathf.Min(minZ, local.z); maxZ = Mathf.Max(maxZ, local.z);
                        }
                    }
                }
                if (!any) return;
                // Owner F8 2026-07-13 ("this is as close as i can get to the pet house — on
                // the footprint"): renderer bounds include ROOF OVERHANGS/awnings (PetHouse2's
                // eaves + slide), and the blocker box is full-height from the ground — so the
                // roof's ground shadow was solid. Inset to the walls: 85% of the rendered XZ
                // approximates walls-not-eaves across the low-poly set; still clamped to
                // [1 cell .. claim] below, so worst case remains the honest old behavior.
                const float eaveInset = 0.85f;
                float rw = (maxX - minX) * eaveInset;
                float rd = (maxZ - minZ) * eaveInset;
                // Never LARGER than the claimed rectangle (the claim is the ceiling), never
                // smaller than one cell (a sliver blocker lets enemies clip through walls).
                cx = Mathf.Clamp(rw, cellSize, w);
                cz = Mathf.Clamp(rd, cellSize, d);
            });
            if (cx > 0f && cz > 0f && (cx < w - 0.01f || cz < d - 0.01f))
            {
                FlowTrace.Step("Structure",
                    $"blocker sized from rendered bounds {cx:F1}x{cz:F1} (claim was {w:F1}x{d:F1}) — walkable ground matches the visible building");
                w = cx; d = cz;
            }

            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(w, 4f, d);
            box.center = new Vector3(0f, 2f, 0f);

            // MOBILE structures skip the carve (see the Spawn call site): a carving obstacle
            // on the same root as a NavMeshAgent removes the mesh under its own agent.
            if (carveNavMesh)
            {
                var obstacle = go.GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = go.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(w, 4f, d);
                obstacle.center = new Vector3(0f, 2f, 0f);
                obstacle.carving = true;   // carve the baked mesh, no full rebake
            }

            // F8-19 (invisible blocker) — the footprint box above is the ONE collider of
            // record for a placed structure. SkinOptions.Structure does NOT strip the source
            // prefab's own colliders, so the visual arrives carrying its raw MeshCollider —
            // whose physics AABB (e.g. tower_ground_archer: 10.79×11.21×10.79 on a 2.5 m
            // footprint) blocks movement FAR beyond the visible mesh. Strip every collider on
            // the visual CHILDREN; colliders on the ROOT stay (the footprint box, plus any
            // behavior-owned root collider like Gate's force-field BoxCollider). The select-tap
            // raycast (BuildModeController.UpdateSelectLoop) resolves PlacedStructure via
            // GetComponentInParent from the hit collider — the root footprint box satisfies it.
            int stripped = 0;
            Guard.Try("Structure", "strip visual colliders (footprint box is collider-of-record)", () =>
            {
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || c.gameObject == go) continue;   // root colliders stay
                    Object.Destroy(c);
                    stripped++;
                }
            });
            FlowTrace.Step("Structure",
                $"'{go.name}' footprint blocker: stripped {stripped} visual collider(s); " +
                $"kept root footprint box {w:0.##}x{d:0.##}m (h=4) as the one physical/nav footprint.");
        }
    }

    /// <summary>
    /// F8-39 / COV-001 (owner felt-test 2026-07-08: "on town respawn after death all the towers
    /// are missing until I add one, then all replaced"). ROOT: <see cref="BaseLayoutLoader"/> is
    /// created ONLY lazily inside <c>BuildModeController.Place</c> (the sole
    /// <see cref="BaseLayoutLoader.EnsureExists"/> caller) — NOTHING recreates it on a scene load.
    /// So on the death → <c>SceneRouter.GoCastle()</c> hub RELOAD the loader (and every tower) is
    /// torn down with the old scene and the persisted <see cref="GameState.BaseLayout"/> is never
    /// replayed → the towers vanish; the base only re-materialises when the player PLACES one,
    /// which lazily recreates the loader whose deferred <c>Start()</c> then runs the full
    /// <see cref="BaseLayoutLoader.Rebuild"/> (the "all reappear at once" tell).
    /// <para>
    /// FIX: a self-bootstrapping DDOL singleton (mirrors <c>SafeZoneRecovery</c> /
    /// <c>HeroHealthBootstrap</c> — no scene edit, CLAUDE.md §3) that ENSURES a loader exists the
    /// moment the HOME HUB (<c>SceneRouter.Castle</c> — MainCastle_Hall, or Main_Castle_Overworld
    /// when ff.MergedWorld is ON) loads, so the persisted base auto-replays on the death-return AND
    /// on a fresh boot, with NO placement needed. Gated to the home hub ONLY (the scene where the
    /// base is built/committed) so it never dumps the home base into Village2 / raids / dungeons.
    /// No double-build: each scene load destroys the previous (non-DDOL) loader, and the loader's
    /// own idempotent <c>_loadedOnce</c> latch builds exactly once per instance;
    /// <see cref="BaseLayoutLoader.EnsureExists"/> returns the existing instance if a placement
    /// already made one this load.
    /// </para>
    /// </summary>
    internal sealed class BaseLayoutLoaderBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("BaseLayoutLoaderBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<BaseLayoutLoaderBootstrap>();
        }

        private void Awake()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // The scene that booted us may already BE the home hub (fresh boot straight into
            // MainCastle_Hall / Main_Castle_Overworld) — arm immediately so the base loads at boot.
            TryArmForScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Single loads only — an additive stream must not re-arm/rebuild the base.
            if (mode != LoadSceneMode.Single) return;
            TryArmForScene(scene.name);
        }

        /// <summary>Ensure the loader (and thus the base replay) exists when the HOME HUB loads.</summary>
        private void TryArmForScene(string sceneName)
        {
            // Replay the persisted HOME base ONLY in the scene it is built/committed in
            // (SceneRouter.Castle — flag-aware: MainCastle_Hall, or Main_Castle_Overworld when
            // ff.MergedWorld is ON). Never in Village2 / raids / dungeons / enemy scenes.
            if (string.IsNullOrEmpty(sceneName) || sceneName != DeNelle.Core.SceneRouter.Castle) return;

            // A placement this same load may have already spun the loader up — don't make a second.
            if (BaseLayoutLoader.Instance != null)
            {
                FlowTrace.Step("BaseLayout",
                    $"Bootstrap: home hub '{sceneName}' loaded — a BaseLayoutLoader already exists " +
                    $"(loaded={BaseLayoutLoader.Instance.Loaded.Count}); leaving it to its own replay.");
                return;
            }

            // Create the loader; its Start() runs LoadFromState() → Rebuild() → the base replays
            // (the death->GoCastle return + fresh boot, with no placement). Rebuild logs the rebuilt
            // count; this Step names the trigger so a capture reads "base replayed on hub load".
            FlowTrace.Step("BaseLayout",
                $"Bootstrap: home hub '{sceneName}' loaded with NO loader — ensuring BaseLayoutLoader so " +
                "the persisted base AUTO-REPLAYS (death->GoCastle return / fresh boot), no placement needed.");
            BaseLayoutLoader.EnsureExists();
        }
    }
}
