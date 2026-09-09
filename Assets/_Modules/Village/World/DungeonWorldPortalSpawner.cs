// =============================================================================
// DungeonWorldPortalSpawner — WO-165 / DEF-188: hidden dungeon portals in the
// open world.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World
//
// THE FEATURE (lightweight, runtime, code-built, WebGL-safe):
//   The dungeons used to be reachable only from a fixed ring of entrances around
//   the village Heart (DungeonEntranceBootstrap). This relocates dungeon access
//   to a few DISCOVERABLE portals hidden out in the OuterWorld regions: the hero
//   roams a region, stumbles on a glowing arch, walks up to it, and it loads the
//   dungeon. Portals start UNDISCOVERED (dimmed) and "light up" the first time the
//   hero gets close — a small fog-of-war reveal so finding one feels like a find.
//
// RECONCILIATION (no parallel system — CLAUDE.md §9). EVERYTHING is reused:
//   * PLACEMENT  — AUTHORED WORLD-POSITION TABLE (owner ruling 2026-07-13:
//     "Portals are NOT in town — portals are wherever in the world we want",
//     refined live: "visible from castle but a little walk"). The old random
//     region-fan placement could exhaust its attempts and silently place
//     NOTHING (the MaxPlaceAttempts=24 give-up); the table is deterministic —
//     a portal ALWAYS appears at its authored spot, NavMesh-seated with the
//     townsfolk-injector ground band [-0.35..0.75] so it never lands on a
//     wall-top. Retune by editing AuthoredPortals below.
//   * PORTAL VISUAL + INTERACTION + LOAD — the EXISTING DungeonPortal component:
//     it already builds its own PortalVFXController glow, shows the proximity
//     [Tap/F] prompt + MobileInteractButton, and routes via
//     SceneManager.LoadScene("Dungeon_" + id). We just place it and Configure() it.
//   * DUNGEON DATA — code-injected DungeonDefs in LoadDefs(), one per COMPOSED dungeon,
//     each gated on CanStreamedLevelBeLoaded so an uncomposed graph yields no portal
//     rather than a door onto a missing scene. Resources/Dungeons/*.asset is still read
//     first if anything is ever authored there, but as of 2026-08-22 that folder holds
//     no DungeonDef: the last two (FolksGranary, HealersCottage) were the project's only
//     legacy ScriptableObject dungeons and were retired in favour of composed graphs.
//     The old "inline fallback onto Dungeon_HealersCottage / Dungeon_FolksGranary" is
//     gone with them.
//   * DISCOVERY — self-contained proximity reveal (mirrors NodeDiscoverySystem's
//     dim->fade-in approach) so portals are genuinely hidden-until-found WITHOUT
//     editing NodeDiscoverySystem (which only tracks Mine/Settlement/Camp nodes).
//
// ISOLATION:
//   * Self-bootstraps via RuntimeInitializeOnLoadMethod(AfterSceneLoad) — NO scene
//     edit, NO prefab hard-dependency, NO bake. It waits (re-scan) for the OuterWorld
//     scene + a baked NavMesh, then places once. No existing file is modified.
//   * Village -> Core only. Cross-module reads are null-conditional.
//   * Coexists with DungeonEntranceBootstrap (village ring) — that bootstrap is NOT
//     auto-added to any scene here, so by default the WORLD portals are the access
//     path. If both are present they simply offer two doors to the same dungeon.
//
// Canon: the village is Elarion (never Avalon). ASCII-only runtime strings.
// =============================================================================
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.World;

namespace DeNelle.Village.World
{
    /// <summary>
    /// Places a few hidden, discoverable dungeon portals at NavMesh-valid positions
    /// out in the OuterWorld regions and wires each to load its dungeon via the
    /// existing <see cref="DungeonPortal"/>. Self-bootstrapping; placement comes from
    /// the authored AuthoredPortals table (owner ruling 2026-07-13), seated on the
    /// NavMesh; reuses DungeonDef data and the DungeonPortal interaction/load path.
    /// </summary>
    public sealed class DungeonWorldPortalSpawner : MonoBehaviour
    {
        public static DungeonWorldPortalSpawner Instance { get; private set; }

        // ── Tunables (code-only; no SO authoring) ────────────────────────────
        [Tooltip("How far the hero must come for an undiscovered portal to reveal (metres). " +
                 "Larger than DungeonPortal's own ActivateRadius so the portal lights up " +
                 "as you approach, before the [F] prompt arms.")]
        public float DiscoverRadius = 26f;

        [Tooltip("Seconds the reveal fade-in takes when a portal is first discovered.")]
        public float FadeInSeconds = 0.8f;

        [Tooltip("How dim an undiscovered portal renders (0 hidden, 1 full). A faint " +
                 "silhouette reads as 'something is out there' without giving it away.")]
        [Range(0f, 1f)] public float UndiscoveredDim = 0.12f;

        [Tooltip("Seconds between placement attempts while waiting for OuterWorld + a baked " +
                 "NavMesh. Once placed, this stops re-trying.")]
        public float PlaceRetryInterval = 1.0f;

        [Tooltip("Height of the portal arch opening in metres (the keystone sits ~1.1 m above this). " +
                 "WO-869: raised 4 -> 6 so the arch reads as a landmark from across the field at " +
                 "2340x1080, not a thin frame you only notice up close. The hero is ~1.8 m, so 6 m " +
                 "is roughly three hero-heights of clear opening.")]
        public float PortalHeight = 6f;

        // ── Authored world placements (owner ruling 2026-07-13) ─────────────
        // "Portals are NOT in town — portals are wherever in the world we want",
        // refined live: "they should travel to get there ... make visible from
        // castle but a little walk". Geography: wall ring ~r=44, bridge outer
        // ends ~r=66-72, south gate ~z=-62 reads adjacent, the cave at z=-404 is
        // a trek. These sit ~95-100m past the wall ring (~140m from origin,
        // ~70-75m walk from the nearest bridge end) on open overworld ground with
        // a clear sightline back to the castle, on two compass sides so the two
        // dungeons pull the player different ways. Retune by editing this table
        // (owner has the Orient/dev tools). FacingYawDeg fronts the arch toward
        // the castle so the hero reads its face on approach.
        private struct AuthoredPortal
        {
            public string DungeonId;
            public Vector3 WorldPos;
            public float FacingYawDeg;
            public AuthoredPortal(string id, Vector3 pos, float yaw)
            { DungeonId = id; WorldPos = pos; FacingYawDeg = yaw; }
        }

        private static readonly AuthoredPortal[] AuthoredPortals =
        {
            // EAST of the walls: ~141m from origin (~97m past the r~44 ring,
            // ~72m walk from the east bridge end). Yaw 262 faces the castle.
            // REROUTE (WO): the east portal now leads to the composed KayKit starter-loop
            // dungeon 'dg_starter_loop' (GraphDungeonComposer) instead of Dungeon_HealersCottage.
            // DungeonPortal routes by this id; LoadDefs injects a matching def when the scene is
            // in the build. Overworld is origin-centered, so (140,0,20) stays reachable.
            new AuthoredPortal("dg_starter_loop", new Vector3(140f, 0f, 20f), 262f),
            // WO-1001 Phase 2A: Sunken Vault — NW of the walls (distinct compass pull from
            // starter east + cottage south). Same ~141m radius class as the east portal.
            // (x,z) = (-100, 100) ≈ 141m; yaw faces castle (SE).
            new AuthoredPortal("dg_sunken_vault", new Vector3(-100f, 0f, 100f), 142f),
            // WO-1001 Phase 2B: the Bonecrypt — SOUTH-WEST, the fourth compass pull (E starter,
            // NW vault, S cottage). Same ~141m radius class as the others: (-100,-100) ~= 141m.
            // Yaw 45 fronts the arch back toward the castle (atan2 of origin - pos).
            new AuthoredPortal("dg_bonecrypt", new Vector3(-100f, 0f, -100f), 45f),
            // WO-1001 Phase 2C: the Ember Deep — DUE NORTH, and deliberately the longest walk of
            // the set (~145m on a clear axis) because it is the hardest crawl: 6 floors, orc into
            // troll, and the richest deep-boss table. Yaw 180 faces the castle.
            new AuthoredPortal("dg_ember_deep", new Vector3(0f, 0f, 145f), 180f),
            // WEST row: the Granary. RESTORED 2026-08-22 pointing at the COMPOSED
            // 'dg_folks_granary', not the retired legacy stub.
            //
            // History, kept because the seat/yaw below is inherited from it: WO-776
            // (2026-07-30) removed this row because the legacy 'FolksGranary' was "a
            // contentless stub ... a real door into a hollow room". The legacy content type
            // (a Resources/Dungeons ScriptableObject pointing at a hand-built
            // Dungeon_FolksGranary.unity) is now GONE — owner ruling 2026-08-22: "the last
            // two break every time, so remove them and create two new ones". The replacement
            // is a graph composed by GraphDungeonComposer like every dungeon that works.
            // Same world seat and yaw as the WO-776 template row, so the west compass pull
            // is unchanged.
            new AuthoredPortal("dg_folks_granary", new Vector3(-140f, 0f, -20f), 82f),
            // SOUTH of the walls -- the third compass pull. RE-POINTED 2026-08-22 from the
            // legacy id "HealersCottage" to the composed 'dg_healers_cottage'.
            //
            // ⚠ THE OLD COMMENT HERE CALLED THIS "the richest dungeon in the game (lore
            // stones, mini-boss, chests, crafting, checkpoints)". THAT WAS FALSE IN THE ONLY
            // sense that matters, and it is exactly the class CLAUDE.md warns about
            // (comments lie; verify from the artifact). The owner, 2026-08-22: "it might be
            // rich but ive never in all the time 5 months ever gotten past room 1 ... so its
            // not accessable so its worth redoing well with proven method". Content the
            // player cannot reach is not content — it is a liability that reads as an asset
            // in review, which is how this one survived five months behind a flattering
            // sentence. The rebuild target is REACHABILITY: a crawl that resolves entry ->
            // rooms -> keep -> exit on the proven composer path.
            //
            // The id is now the graphId, and the composed scene is named after it
            // (DungeonBaker writes Assets/Scenes/DungeonCompose/<dungeonId>.unity), so
            // DungeonPortal.EnterDungeon loads it VERBATIM and never reaches its
            // "Dungeon_" + id fallback. That fallback used to be load-bearing here, when the
            // bare id "HealersCottage" had to resolve to Dungeon_HealersCottage.unity.
            //
            // Seat = the EAST row rigidly yaw-rotated +90 deg about the origin,
            // (x,z) -> (z,-x): (140,20) -> (20,-140). Same 141.4m radius, same ~96m walk
            // past the r~44 plinth face. Yaw = Atan2(-x,-z) = Atan2(-20,140) = -8.13 deg
            // -> 352 (the east row's 262 + 90), so the arch fronts the castle like its
            // siblings. GROUND: this lands in the WO-468 cave-road corridor, which
            // ExteriorTerrainBuilder holds at EXACTLY Y=0 for |x| <= 20 over z in
            // [-700,-76] (CorridorWeight / CavePathFlattenHalf=20) -- the only one of the
            // three seats whose height is pinned inside the [-0.35..0.75] ground band
            // rather than relying on the SeatOnGround search (the other two sit past
            // ReliefBlendRadius=140, where +-1.8m of relief applies). Tree/rock scatter is
            // rejected within 37m of that road line, so the seat is flat and prop-free,
            // 10m clear of the painted road and ~78m outside the r=44..62 moat band.
            // If a headless run reports navmesh-seated=False, retune to (16f, 0f, -140f).
            new AuthoredPortal("dg_healers_cottage", new Vector3(20f, 0f, -140f), 352f),
        };

        // ⛔ THE BAND IS AN UPPER LIMIT ONLY — corrected 2026-08-20 (owner: "portal floating in the air").
        //
        // This was copied verbatim from CastleTownsfolkInjector, whose job is to reject ELEVATED
        // mesh: a wall-top or a bridge deck is never a valid spawn. That purpose is entirely
        // one-sided, but the constant was written as a symmetric window, so the LOWER bound
        // rejected perfectly good ground that simply sits below sea level — and then the fallback
        // planted the portal at y=0 regardless.
        //
        // MEASURED: ExteriorTerrain at the starter-loop portal column (140, 20) is -2.51 m, and the
        // hero provably stands there on-navmesh at y=-2.65. The sample was rejected for being 2 m
        // too LOW, the fallback forced y=0, and the portal hung ~2.5 m in the air. THREE of five
        // portals failed identically in one run (dg_starter_loop, dg_sunken_vault, dg_bonecrypt);
        // only the two authored over high ground seated.
        //
        // So the ceiling is kept (it is the real rule) and the floor is dropped to the terrain's
        // own reach. ExteriorTerrain's origin is y=-4, so -6 clears the lowest authored ground with
        // margin while still rejecting anything at wall-top height.
        private const float GroundMinY = -6f;
        private const float GroundMaxY = 0.75f;

        // PlayerPrefs key prefix for "this portal has been discovered" (position-keyed,
        // same convention as NodeDiscoverySystem so discovery survives a reload).
        private const string PrefKeyPrefix = "dotr-dungeon-portal-discovered-";

        // Cap on placement retries. Each retry runs NavMesh.CalculateTriangulation()
        // (HEAVY on the big OuterWorld mesh). If no portal can ever seat (every region
        // match fails), the un-capped retry re-ran that triangulation forever on the
        // main thread = a hard hang on OuterWorld load. After MaxPlaceAttempts failures
        // we give up (portals simply don't appear) so the game stops grinding.
        private const int MaxPlaceAttempts = 24;

        private bool _placed;
        private int _placeAttempts;
        private float _retryTimer;
        private Transform _root;
        private Transform _hero;

        // WO-1114: how often the door-state poll re-asks DungeonStatusCatalog. Slow on
        // purpose - the payload lands once, a live flip is a human operation, and this is
        // a dictionary lookup per portal, not work.
        private const float DoorStateCheckInterval = 0.5f;
        private float _nextDoorStateCheck;

        // One placed portal + its discovery bookkeeping.
        private sealed class Portal
        {
            public Transform Root;
            public string Key;
            public bool Discovered;
            public Renderer[] Renderers;
            public Color[] BaseColors;
            public float FadeT;       // 0..1 reveal progress (1 = done)
            public VFXHandle GateVfx; // looping magic-circle rune ring at the arch base (null until attached)
            // WO-869 threshold aura. Held so the WO-753 one-owner teardown can Stop() it -
            // an orphaned looping portal aura with no portal under it is the exact bug that
            // rule exists to prevent.
            public VFXHandle ThresholdVfx;
            // Owner VFX pick 2026-08-16 ("Magic circle dark star ... use this rotated for the
            // portals"): the mirrored Hovl circle standing VERTICAL in the arch opening. A plain
            // instantiated child of Root (NOT pooled - loaded from the tracked mirror, so it
            // costs no VFXManager loop slot); held so the teardown loop and the art-swap
            // re-seat can destroy/re-attach it explicitly.
            //
            // WO-1062 (owner ruling 2026-08-22, "think we need two vfs each facing outwards"):
            // there are now TWO of them - the SAME mirrored prefab, one facing each way out of
            // the doorway, 0.25m apart along the threshold normal. CircleVfx is the +90 plane
            // (facing Root.forward, the authored FacingYawDeg), CircleVfxBack is the -90 plane.
            // ⛔ BOTH are held because BOTH must be torn down: this used to be a single handle
            // and every teardown/re-seat site assumed one instance, so a second plane added
            // without widening the field here leaks past every teardown.
            public GameObject CircleVfx;
            public GameObject CircleVfxBack;
            // Owner 2026-08-14 ("all of the portals should be this"): the shared PortalStructure
            // art, swapped in over the code-built cube arch. Held so the Addressables handle is
            // released on teardown and so the discovery dim can retarget onto the real renderers.
            public PortalStructure.SwapResult Swap;
            public bool ArtStanding;
            // WO-1114: which dungeon this door leads to, and the door state that is
            // currently DRESSED on it. The id is needed because the appearance owner
            // (ApplyDoorState) asks DungeonStatusCatalog per portal; the applied state
            // is held so a live flip re-dresses once and only once.
            public string DungeonId;
            public DungeonDoorState DoorState;
            public bool DoorStateApplied;
        }

        private readonly List<Portal> _portals = new List<Portal>();

        // =====================================================================
        // Self-bootstrap (no scene edit). Runs after every scene load; idempotent.
        // =====================================================================
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Dungeon entry is ON by default (FeatureFlags.DungeonPortals, defaultOn: true) — the
            // portals spawn unless explicitly disabled. (It was gated OFF around 2026-07-01 while the
            // first 2 dungeons were placeholder-primitive "pill" scenes and the KayKit dungeon art
            // wasn't wired; the default has since been flipped back ON.) Disable for testing:
            // PlayerPrefs "ff.dungeonportals" = 0.
            if (!DeNelle.Core.FeatureFlags.DungeonPortals) return;
            if (Instance != null) return;
            var go = new GameObject("DungeonWorldPortalSpawner");
            go.AddComponent<DungeonWorldPortalSpawner>();
            Object.DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            // Destroy(this), not the host — DDOL singleton pattern (CLAUDE.md memory).
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnVisibilitySceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnVisibilitySceneLoaded;
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnVisibilitySceneLoaded;
            if (Instance == this) Instance = null;
        }

        private void OnVisibilitySceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                             UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            ApplyWorldOnlyVisibility(scene.name);
        }

        private void ApplyWorldOnlyVisibility(string sceneName)
        {
            if (_root == null) return;
            bool visible = DeNelle.Core.HubScenes.IsOverworld(sceneName);
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            FlowTrace.Step("DungeonPortal",
                $"world portal ring visibility={(visible ? "ON" : "OFF")} for scene '{sceneName}' " +
                "(raid/dungeon scenes never inherit the DDOL map portals).");
        }

        private void Update()
        {
            if (!_placed)
            {
                _retryTimer -= Time.deltaTime;
                if (_retryTimer <= 0f)
                {
                    _retryTimer = Mathf.Max(0.25f, PlaceRetryInterval);
                    TryPlace();
                }
                return;
            }

            EnsureHero();
            TickDiscovery();
        }

        // =====================================================================
        // Placement — one portal per authored table row (owner ruling 2026-07-13).
        // Waits (retry) only for a baked NavMesh; once the mesh exists placement is
        // DETERMINISTIC: every authored portal is built (ground-band-seated when the
        // sample lands, raw authored point otherwise) — the old random-region path
        // could exhaust its attempts and silently place NOTHING.
        // =====================================================================
        private void TryPlace()
        {
            // Owner session 2026-07-13 (proving line: "no baked NavMesh after 24 attempts —
            // stopping retries ... (no portals this session)"): the DDOL bootstrap starts
            // this retry clock on the TITLE screen, where no navmesh exists — all capped
            // attempts burned in the menus and placement retired before the overworld ever
            // loaded. Attempts only count IN the overworld; menus don't spend the budget.
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            // Portal coordinates and BiomeRoads bounds belong to the merged terrain overworld.
            // A generic hub gate is too wide: Village2 is an enemy stronghold hub with a baked
            // NavMesh but deliberately NO Terrain, so the old branch attempted to derive/place
            // the overworld ring there and correctly tripped the typed no-Terrain failure.
            if (!DeNelle.Core.HubScenes.IsOverworld(active))
                return;   // menu/town/stronghold/battle scene — wait, no attempt spent

            var defs = LoadDefs();
            if (defs.Count == 0) return; // nothing to place (no built dungeon scenes)

            // Gate on a baked NavMesh existing — the ground-band seat needs it. The
            // triangulation is empty until the OuterWorld NavMesh bake is present at
            // runtime. Retries stay CAPPED: CalculateTriangulation is HEAVY and an
            // uncapped retry loop hard-hung the OuterWorld load pre-cap.
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                _placeAttempts++;
                if (_placeAttempts >= MaxPlaceAttempts)
                {
                    _placed = true;
                    Debug.LogWarning($"[DungeonWorldPortals] no baked NavMesh after {_placeAttempts} attempts — " +
                        "stopping retries to avoid a CalculateTriangulation freeze (no portals this session).");
                }
                return;
            }

            _root = new GameObject("[DungeonWorldPortals]").transform;
            DontDestroyOnLoad(_root.gameObject);
            ApplyWorldOnlyVisibility(active);

            int placed = 0;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null || string.IsNullOrEmpty(def.DungeonId)) continue;

                if (!TryGetAuthored(def.DungeonId, out AuthoredPortal entry))
                {
                    // Never fall back to random ground — an unauthored dungeon is a
                    // loud authoring gap, not a silent roll of the dice.
                    Debug.LogWarning($"[DungeonWorldPortals] '{def.DungeonId}' has NO authored world position " +
                        "in AuthoredPortals — portal not placed; add a table row.");
                    continue;
                }

                Vector3 pos = SeatOnGround(entry.WorldPos, out bool seated);
                BuildPortal(def, pos, entry.FacingYawDeg);
                placed++;
                FlowTrace.Step("DungeonPortal",
                    $"placed '{def.DungeonId}' at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}) " +
                    $"(authored {entry.WorldPos}, navmesh-seated={seated}, facingYaw={entry.FacingYawDeg:F0}).");
            }

            // Authored placement is one-pass deterministic — never re-roll.
            _placed = true;
            Debug.Log($"[DungeonWorldPortals] Placed {placed}/{defs.Count} authored dungeon portal(s) in the world.");
        }

        // =====================================================================
        //  THE HOLLOW ROADS row is DERIVED, not typed (owner 2026-08-16).
        // ---------------------------------------------------------------------
        //  Every row in AuthoredPortals above is a hand-typed coordinate, and that is
        //  fine for them -- they were placed by eye and the owner has felt-tested where
        //  they stand. The tunnel mouth is NOT allowed to be a sixth typed coordinate:
        //  the standing rule on this lane is that a new world position comes from
        //  MEASURED geometry, because two bugs shipped on 2026-08-15 from constants that
        //  had quietly stopped matching the world.
        //
        //  So it is derived from the table ITSELF, which is the only honest measurement
        //  available for "where do portals live in this world":
        //    * RADIUS  = the mean distance-from-origin of the existing rows. They form a
        //                deliberate ~141m ring class; averaging reads that class off the
        //                data instead of restating it, so re-placing a portal carries.
        //    * BEARING = the centre of the WIDEST ANGULAR GAP between existing rows. The
        //                tunnel mouth lands where the compass is emptiest, which is both
        //                the right design answer (a fifth distinct pull) and
        //                self-maintaining: add a portal and the gap moves on its own.
        //    * YAW     = Atan2 back toward the origin, matching every sibling row's
        //                "arch fronts the castle" convention.
        //  Then SeatOnGround does the real ground work on the result, exactly as it does
        //  for the typed rows, and the derivation is TRACED so a wrong mouth shows its own
        //  arithmetic in the capture rather than making the next seat re-derive it.
        // =====================================================================
        private static bool TryDeriveHollowRoadsPortal(out AuthoredPortal entry)
        {
            entry = default;

            if (!DeNelle.Core.FeatureFlags.BiomeRoads)
            {
                FlowTrace.Step("DungeonPortal",
                    "Hollow Roads portal SKIPPED - ff.biomeroads is OFF, so the tunnel mouth is not placed.");
                return false;
            }

            // Measure the ring off the typed sibling rows.
            float sumRadius = 0f;
            int counted = 0;
            var bearings = new System.Collections.Generic.List<float>(AuthoredPortals.Length);
            for (int i = 0; i < AuthoredPortals.Length; i++)
            {
                Vector3 p = AuthoredPortals[i].WorldPos;
                float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                if (r < 1f) continue;                      // a row at the origin carries no bearing
                sumRadius += r;
                counted++;
                float deg = Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg;
                if (deg < 0f) deg += 360f;
                bearings.Add(deg);
            }

            if (counted == 0 || bearings.Count == 0)
            {
                // No siblings to measure against => derive NOTHING. There is deliberately no typed
                // fallback: a guessed mouth is worse than an absent one, and an absent one is loud.
                FlowTrace.Fail("DungeonPortal",
                    "Hollow Roads portal CANNOT be derived - AuthoredPortals carries no row with a " +
                    "non-zero radius to measure a ring or a bearing from. The tunnel mouth is NOT placed " +
                    "(no typed fallback by design).");
                return false;
            }

            float radius = sumRadius / counted;

            // Widest angular gap on the ring. Sorting then walking the wrap-around seam is the whole
            // algorithm; with one bearing the gap is the full circle and the answer is "opposite it".
            bearings.Sort();
            float bestGap = -1f, bestMid = 0f;
            for (int i = 0; i < bearings.Count; i++)
            {
                float a = bearings[i];
                float b = (i + 1 < bearings.Count) ? bearings[i + 1] : bearings[0] + 360f;
                float gap = b - a;
                if (gap > bestGap) { bestGap = gap; bestMid = a + gap * 0.5f; }
            }
            while (bestMid >= 360f) bestMid -= 360f;

            float rad = bestMid * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);

            // Face the castle, same convention as every sibling row.
            float yaw = Mathf.Atan2(-pos.x, -pos.z) * Mathf.Rad2Deg;
            if (yaw < 0f) yaw += 360f;

            // Sanity-check against the MEASURED world, not against a typed extent: a mouth outside
            // the terrain is unreachable, and it must say so rather than be placed into the void.
            if (DeNelle.Core.World.BiomeRoads.TryMeasureWorldBounds(out Bounds worldBounds))
            {
                // Hand the MEASURED bounds forward to the tunnel. The tunnel scene carries no
                // terrain, so this is the only moment the world it drops back INTO can honestly be
                // measured; without this the drops would have to fall back to a typed extent, and
                // "derived, never typed" would quietly become a comment rather than a property.
                HollowRoadsDropInjector.RememberHubBounds(worldBounds);

                // Compare against the bounds' OWN frame (centre +/- extent), not |world coord| vs a
                // half-extent: those are only the same number while the terrain is centred on the
                // origin, and this test exists precisely to catch worlds that are not what we assume.
                if (Mathf.Abs(pos.x - worldBounds.center.x) > worldBounds.extents.x
                    || Mathf.Abs(pos.z - worldBounds.center.z) > worldBounds.extents.z)
                {
                    FlowTrace.Fail("DungeonPortal",
                        $"Hollow Roads mouth derived to {pos}, which is OUTSIDE the measured world bounds " +
                        $"(centre {worldBounds.center}, half-extents {worldBounds.extents}). Not placed - the " +
                        "sibling portal ring must have moved far off the terrain.");
                    return false;
                }
            }

            entry = new AuthoredPortal(DeNelle.Core.World.BiomeRoads.TunnelSceneId, pos, yaw);
            FlowTrace.Step("DungeonPortal",
                $"Hollow Roads mouth DERIVED (not typed): ring radius {radius:F1}m = mean of {counted} sibling " +
                $"row(s); bearing {bestMid:F1}deg = centre of the widest {bestGap:F1}deg gap between them; " +
                $"=> {pos}, yaw {yaw:F0} (faces castle). SeatOnGround will do the ground pass next.");
            return true;
        }

        private static bool TryGetAuthored(string dungeonId, out AuthoredPortal entry)
        {
            // The Hollow Roads mouth is derived from measured geometry, so it is answered here
            // rather than by a table row (see TryDeriveHollowRoadsPortal for why).
            if (string.Equals(dungeonId, DeNelle.Core.World.BiomeRoads.TunnelSceneId,
                              System.StringComparison.OrdinalIgnoreCase))
                return TryDeriveHollowRoadsPortal(out entry);

            for (int i = 0; i < AuthoredPortals.Length; i++)
            {
                if (string.Equals(AuthoredPortals[i].DungeonId, dungeonId, System.StringComparison.OrdinalIgnoreCase))
                {
                    entry = AuthoredPortals[i];
                    return true;
                }
            }
            entry = default;
            return false;
        }

        // Ground-band NavMesh seat: accept a sample only BELOW the ceiling so a portal never seats
        // on a wall-top / bridge deck; widen the search once; and if no mesh answers, fall back to
        // the TERRAIN height rather than y=0 — a portal always appears where authored, and
        // "where authored" has never meant "at sea level".
        private static Vector3 SeatOnGround(Vector3 authored, out bool seated)
        {
            float[] radii = { 8f, 20f };
            for (int i = 0; i < radii.Length; i++)
            {
                if (NavMesh.SamplePosition(authored, out NavMeshHit hit, radii[i], NavMesh.AllAreas)
                    && hit.position.y >= GroundMinY && hit.position.y <= GroundMaxY)
                {
                    seated = true;
                    return hit.position;
                }
            }

            // NO NAVMESH. The old fallback returned y=0 flat, which is why a portal over ground at
            // -2.5 m hung in the air. Ask the terrain instead: it is authored, always present in the
            // overworld, and needs no bake. Only if there is no terrain either do we keep the
            // authored y — and then we say so, loudly, instead of inventing a zero.
            float groundY;
            bool haveGround = TrySampleTerrainHeight(authored, out groundY);

            FlowTrace.Warn("DungeonPortal",
                $"SeatOnGround: no ground-band [{GroundMinY}..{GroundMaxY}] NavMesh hit within 20m of " +
                $"authored {authored} — falling back to " +
                (haveGround ? $"TERRAIN height y={groundY:F2}." : $"the authored y={authored.y:F2} (NO terrain either).") +
                " Verify walkability in the next fleet run.");

            seated = false;
            return new Vector3(authored.x, haveGround ? groundY : authored.y, authored.z);
        }

        /// <summary>
        /// Terrain height under a world point. Checks the terrain that actually contains the point
        /// rather than <c>Terrain.activeTerrain</c>, so a multi-tile world cannot silently answer
        /// with the wrong tile's height.
        /// </summary>
        private static bool TrySampleTerrainHeight(Vector3 world, out float y)
        {
            y = 0f;
            var terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) return false;

            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 pos = t.transform.position;
                Vector3 size = t.terrainData.size;
                if (world.x < pos.x || world.x > pos.x + size.x ||
                    world.z < pos.z || world.z > pos.z + size.z) continue;
                y = t.SampleHeight(world) + pos.y;
                return true;
            }
            return false;
        }

        // =====================================================================
        // Build one portal: a code-built placeholder arch + the EXISTING DungeonPortal
        // driver (proximity prompt + SceneManager.LoadScene). DungeonPortal adds its own
        // PortalVFXController glow in Start(), so the arch reads as a portal with no asset.
        // =====================================================================
        private void BuildPortal(DungeonDef def, Vector3 pos, float facingYawDeg)
        {
            using var _ = FlowTrace.Enter("DungeonPortals", $"BuildPortal id='{def?.DungeonId}'");
            if (def == null)
            {
                FlowTrace.Fail("DungeonPortals", "BuildPortal: null def — skipping (no NRE-abort of the placement loop).");
                return;
            }

            var root = new GameObject($"DungeonWorldPortal_{def.DungeonId}");
            root.transform.SetParent(_root, false);
            root.transform.position = pos;
            // Authored facing (fronts read toward the castle — owner-retunable in the table).
            root.transform.rotation = Quaternion.Euler(0f, facingYawDeg, 0f);

            var renderers = BuildArch(root.transform, PortalHeight, def.AccentColor);

            // V (TGVRU-V): a portal with no live renderer is INVISIBLE — the hero can never find
            // it (the whole feature is "stumble on a glowing arch"). Count enabled renderers with
            // a material; Warn-loud if none so an invisible portal self-reports instead of being a
            // silent "feature does nothing". Non-fatal — the trigger still loads the dungeon if hit.
            int liveRenderers = 0;
            if (renderers != null)
                foreach (var r in renderers)
                    if (r != null && r.enabled && r.sharedMaterial != null) liveRenderers++;
            if (liveRenderers == 0)
                FlowTrace.Warn("DungeonPortals",
                    $"BuildPortal: '{def.DungeonId}' arch has 0 live renderer(s) at {pos} — portal will be INVISIBLE (null shader/material?).");

            // Trigger + kinematic RB so the transform-moved hero collider fires OnTriggerEnter
            // on the portal side (same pattern as DungeonEntranceBootstrap / WO-08 gates).
            var sphere = root.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 2.5f;
            sphere.center = new Vector3(0f, 1f, 0f);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // The EXISTING interaction + load driver. Configure() sets which dungeon it routes
            // to; its EnterDungeon() loads "Dungeon_" + dungeonId via SceneManager.LoadScene.
            var portal = root.AddComponent<DungeonPortal>();
            portal.Configure(def.DungeonId, def.ResolveName());

            // Discovery bookkeeping — start dimmed/hidden until the hero finds it.
            var colors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) colors[i] = SafeColor(renderers[i]);

            var entry = new Portal
            {
                Root = root.transform,
                Key = MakeKey(pos),
                Renderers = renderers,
                BaseColors = colors,
                DungeonId = def.DungeonId,   // WO-1114: the key ApplyDoorState resolves the door with
            };
            entry.Discovered = PlayerPrefs.GetInt(entry.Key, 0) == 1;
            entry.FadeT = entry.Discovered ? 1f : 0f;
            ApplyDim(entry, entry.Discovered ? 1f : UndiscoveredDim);
            _portals.Add(entry);

            // Owner felt-test 2026-07-15 ("the dungeon portal arch looks plain -- make it
            // magical"): a portal already found in a prior session (persisted Discovered)
            // gets its arcane rune-ring immediately so it reads as an active gateway on
            // load; a fresh (undiscovered) portal blooms it on when the hero finds it
            // (Discover()), preserving the fog-of-war reveal.
            if (entry.Discovered) { AttachGateVfx(entry); AttachThresholdAura(entry); AttachPortalCircle(entry); }

            // WO-1114 CALL SITE 1 of 2 — dress the door for its status the moment it is built,
            // so a sealed dungeon is never bright-and-inviting for a frame. The second call
            // site is inside SwapInSharedStructureAsync (the re-seat), and it is the one that
            // is easy to forget: without it the real portal art loads over the top and the
            // closed treatment silently reverts.
            ApplyDoorState(entry, DungeonStatusCatalog.For(entry.DungeonId));

            // Owner 2026-08-14: wear the SAME structure as the dungeon exit. Kicked async and
            // deliberately AFTER the cube arch is already standing, so the portal is never
            // empty for a frame - an Addressables miss leaves the player the cube arch instead
            // of an invisible landmark (the whole feature is "stumble on a glowing arch").
            SwapInSharedStructureAsync(entry).Forget();

            // WO-869 belt-and-braces: recover this portal's materials RIGHT NOW rather than
            // waiting for the guard's next deferred sweep. SweepGameObject is the per-object
            // seam (hideStrayPrimitives:false, so it repaints and never hides), and the arch is
            // already registered as protected art above.
            DeNelle.Core.MagentaGuard.SweepGameObject(root, "DungeonWorldPortalSpawner.BuildPortal");

            // WO-1156: clear runtime foliage from the measured doorway and both camera approaches.
            // This never moves the portal and is repeated after the async art swap below.
            HubFoliageInjector.ClearPortalApproach(root.transform);

            FlowTrace.Step("DungeonPortals",
                $"BuildPortal: '{def.ResolveName()}' portal committed at {pos} " +
                $"(discovered={entry.Discovered}, liveRenderers={liveRenderers}).");
        }

        // =====================================================================
        // THE THRESHOLD ARCH (WO-869 REBUILD, owner 2026-08-04: "we need that portal to
        // look way better ... the point is the whole thing needs redone").
        // ---------------------------------------------------------------------
        // WHAT WAS WRONG (owner Seeker capture docs/ui-review/2026-08-04-seeker/08-portal-magenta.png):
        // the old arch was TWO 0.3 m sticks and a flat bar - a rectangle drawn in the air,
        // 1.8 m wide and 4 m tall, with ZERO depth. Nothing about it said "this leads
        // somewhere": no ground it stood on, no threshold to cross, no near/far face, and
        // at 2340x1080 across an open field it read as a thin frame, not a landmark.
        //
        // THE REBUILD, and why each part earns its place:
        //   PLINTH (2 stepped slabs) - the portal is PLANTED. A landmark you navigate toward
        //     needs a footprint on the ground; the upper step is also the floor you cross,
        //     which is what turns a frame into a THRESHOLD.
        //   TWO PILLAR RINGS (front + back, 1.8 m apart in Z) - this is the single most
        //     important change. Depth is what makes an opening read as a way INTO somewhere
        //     rather than a picture frame: you see the near pillars, the far pillars behind
        //     them, and a lit surface suspended between. One ring can never do that.
        //   STEPPED PILLARS (base drum / shaft / capital) - a varying silhouette survives
        //     distance. A constant-width stick reads as a line; a stepped column still reads
        //     as architecture when it is 40 m away and 30 px tall.
        //   LINTEL PER RING + CORNICE + KEYSTONE - gives the top a PEAK instead of a flat bar,
        //     so the shape is identifiable against the skyline from across the field.
        // The active threshold SURFACE and the aura are owned by PortalVFXController, which
        // now fits itself to this arch's real bounds (so this geometry can be retuned freely).
        //
        // Still 100% code-built primitives: NO .unity scene edit, NO new art asset, NO bake
        // (CLAUDE.md section 3). Cost is ~18 cube renderers sharing ONE material, so they
        // SRP-batch - for the single landmark that is the entrance to a whole pillar.
        // =====================================================================

        // Half-width of the walk-through opening. 1.8 m half = a 3.6 m doorway: the hero is
        // ~1.8 m, so it reads as something you walk INTO rather than squeeze past.
        private const float ArchHalfWidth = 1.8f;
        // Half-depth: the two pillar rings sit at +/- this, i.e. a 1.8 m deep threshold.
        private const float ArchHalfDepth = 0.9f;
        // Pillar cross-section.
        private const float PillarThickness = 0.45f;

        /// <summary>
        /// Build the threshold arch (see the block comment above). Returns every renderer so the
        /// discovery layer can dim/restore them. Colliders stripped - the sphere trigger on the
        /// root is the only collider, so the player can walk through the opening.
        /// </summary>
        private Renderer[] BuildArch(Transform parent, float h, Color accent)
        {
            // MAGENTA ROOT CAUSE (WO-869): this used a RAW Shader.Find, which returns NULL in a
            // stripped player build - and when it did, `mat` stayed null, MakeBox's
            // `if (mat != null)` skipped the assignment, and the cube kept Unity's DEFAULT
            // material, which under URP renders MAGENTA. That is the pink arch in the Seeker
            // capture, and the old unlit-sprite-shader fallback behind it only ever made it
            // worse (an unlit sprite shader on a 3D arch). MagentaGuard.ResolveUrpLitShader is
            // the ROBUST resolver written for exactly this: it falls back to BORROWING a URP/Lit
            // shader off a material already live in the loaded scene, which is guaranteed to be
            // in the build because it is serialized in a built scene.
            Shader lit = DeNelle.Core.MagentaGuard.ResolveUrpLitShader();
            FlowTrace.Step("DungeonPortals",
                $"BuildArch: URP/Lit resolved={(lit != null)} via MagentaGuard.ResolveUrpLitShader " +
                $"(rawShaderFind={(Shader.Find("Universal Render Pipeline/Lit") != null)}) - " +
                "a false rawShaderFind with a true resolve IS the magenta cause, captured.");
            if (lit == null)
                FlowTrace.Fail("DungeonPortals",
                    "BuildArch: NO URP/Lit shader resolvable - the arch will render with Unity's default " +
                    "material, which is MAGENTA under URP. Add 'Universal Render Pipeline/Lit' to " +
                    "GraphicsSettings AlwaysIncludedShaders.");

            Material mat = lit != null ? new Material(lit) { name = "PortalArch_URP" } : null;
            if (mat != null)
            {
                mat.EnableKeyword("_EMISSION");
                // DEF-94: arch reads as an arcane-violet portal (per-dungeon accent kept
                // as a subtle tint), NOT a flat pastel AccentColor frame. WO-272's glow
                // layers on top of this base.
                Color archBase = PortalVFXController.ArchBaseColor(accent);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", archBase);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", PortalVFXController.ArchEmissionColor(accent));
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);   // URP: 0 = Opaque
                if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 1f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            }

            float hw = ArchHalfWidth;
            float dz = ArchHalfDepth;
            float pt = PillarThickness;
            float pillarX = hw + pt * 0.5f;             // pillar centre, just outside the opening
            float outerW  = (pillarX + pt * 0.5f) * 2f; // full frame width including pillars
            float outerD  = dz * 2f + pt;               // full frame depth including both rings

            var list = new List<Renderer>(18);

            // 1) PLINTH - two stepped slabs. Plants the portal and gives the threshold a floor.
            list.Add(MakeBox(parent, "Arch_PlinthLower", new Vector3(0f, 0.175f, 0f),
                             new Vector3(outerW + 1.2f, 0.35f, outerD + 1.0f), mat));
            list.Add(MakeBox(parent, "Arch_PlinthUpper", new Vector3(0f, 0.50f, 0f),
                             new Vector3(outerW + 0.4f, 0.30f, outerD + 0.3f), mat));

            // 2) FOUR PILLARS in two depth rings - the read of DEPTH lives here.
            float drumH    = 0.50f;
            float capH     = 0.40f;
            float drumTop  = 0.65f + drumH;                       // plinth top (0.65) + drum
            float capBase  = h - capH;
            float shaftH   = Mathf.Max(0.6f, capBase - drumTop);   // never inverts on a small h
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float x = pillarX * sx;
                    float z = dz * sz;
                    list.Add(MakeBox(parent, "Arch_PillarBase", new Vector3(x, 0.65f + drumH * 0.5f, z),
                                     new Vector3(pt * 1.6f, drumH, pt * 1.6f), mat));
                    list.Add(MakeBox(parent, "Arch_PillarShaft", new Vector3(x, drumTop + shaftH * 0.5f, z),
                                     new Vector3(pt, shaftH, pt), mat));
                    list.Add(MakeBox(parent, "Arch_PillarCapital", new Vector3(x, capBase + capH * 0.5f, z),
                                     new Vector3(pt * 1.5f, capH, pt * 1.5f), mat));
                }
            }

            // 3) A LINTEL ACROSS EACH RING - closes each ring into its own arch, so the
            //    silhouette is two nested openings, not one flat frame.
            for (int sz = -1; sz <= 1; sz += 2)
                list.Add(MakeBox(parent, "Arch_Lintel", new Vector3(0f, h + 0.225f, dz * sz),
                                 new Vector3(outerW + 0.3f, 0.45f, pt * 1.3f), mat));

            // 4) CORNICE + KEYSTONE - ties the two rings together and gives the top a PEAK,
            //    which is what makes the shape identifiable against the sky at distance.
            list.Add(MakeBox(parent, "Arch_Cornice", new Vector3(0f, h + 0.60f, 0f),
                             new Vector3(outerW + 0.8f, 0.30f, outerD + 0.4f), mat));
            list.Add(MakeBox(parent, "Arch_Keystone", new Vector3(0f, h + 1.10f, 0f),
                             new Vector3(0.90f, 0.90f, pt * 1.6f), mat));

            // WO-869: tell MagentaGuard these primitives are DELIBERATE ART. Without this the
            // scene sweep classifies every Cube as a stray placeholder pill and DISABLES it -
            // which would turn "magenta portal" into "no portal at all", a strictly worse bug
            // (the feature is literally "stumble on a glowing arch"). Registered here, after the
            // renderers exist, so the guard's very next sweep recovers rather than hides them.
            DeNelle.Core.MagentaGuard.ProtectPrimitiveArt(parent != null ? parent.gameObject : null,
                                                          "DungeonWorldPortalSpawner.BuildArch");

            return list.ToArray();
        }

        private static Renderer MakeBox(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col); // arch shouldn't block movement; the trigger is the only collider
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null) r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return r;
        }

        // =====================================================================
        // Discovery — dim until the hero approaches, then fade the arch up + persist.
        // Mirrors NodeDiscoverySystem; kept self-contained so no existing file is edited.
        // =====================================================================
        private void TickDiscovery()
        {
            float revealSqr = DiscoverRadius * DiscoverRadius;
            bool heroValid = _hero != null;

            // WO-1114: the status payload lands asynchronously, so it can arrive AFTER the
            // portals are built. Re-ask the catalog on a slow throttle and let the one
            // appearance owner re-dress anything that changed - that is what makes a flip
            // land in the world within a cache period with no rebuild. Cheap: a dictionary
            // lookup per portal, twice a second, and ApplyDoorState no-ops when nothing moved.
            bool checkDoors = Time.time >= _nextDoorStateCheck;
            if (checkDoors) _nextDoorStateCheck = Time.time + DoorStateCheckInterval;

            for (int i = _portals.Count - 1; i >= 0; i--)
            {
                var p = _portals[i];
                if (p == null || p.Root == null)
                {
                    // Release the pooled rune-ring loop back to the shared VFX pool so a
                    // torn-down portal never leaves an orphaned looping effect behind.
                    p?.GateVfx?.Stop(true);
                    p?.ThresholdVfx?.Stop(true);   // WO-753: no aura outlives its portal
                    // The circle is a plain child of Root, so it normally dies with it - this
                    // Destroy only matters if Root was cleared without a destroy. Same WO-753 rule.
                    // WO-1062: BOTH outward-facing planes, through the one teardown owner.
                    ClearPortalCircle(p);
                    // Release the shared-structure bundle too, or it stays resident for the
                    // whole session once a portal is torn down.
                    if (p != null) PortalStructure.Release(ref p.Swap);
                    _portals.RemoveAt(i);
                    continue;
                }

                if (checkDoors) ApplyDoorState(p, DungeonStatusCatalog.For(p.DungeonId));

                if (!p.Discovered)
                {
                    if (heroValid &&
                        (p.Root.position - _hero.position).sqrMagnitude <= revealSqr)
                        Discover(p);
                    continue;
                }

                if (p.FadeT < 1f)
                {
                    float step = FadeInSeconds > 0f ? Time.deltaTime / FadeInSeconds : 1f;
                    p.FadeT = Mathf.Clamp01(p.FadeT + step);
                    ApplyDim(p, Mathf.Lerp(UndiscoveredDim, 1f, p.FadeT));
                }
            }
        }

        private void Discover(Portal p)
        {
            if (p == null || p.Discovered) return;
            p.Discovered = true;
            p.FadeT = 0f;
            PlayerPrefs.SetInt(p.Key, 1);
            PlayerPrefs.Save();
            // Bloom the arcane rune ring on as the hero finds it -- the discovery reads
            // as the gateway "activating" (owner felt-test: make the entrance magical).
            AttachGateVfx(p);
            AttachThresholdAura(p);
            AttachPortalCircle(p);
            // WO-1114: discovery just lit the full treatment. If this door is closed, the one
            // appearance owner takes it straight back off - a sealed door must not bloom open
            // the instant the hero finds it.
            p.DoorStateApplied = false;
            ApplyDoorState(p, DungeonStatusCatalog.For(p.DungeonId));
            Debug.Log($"[DungeonWorldPortals] Hero discovered a hidden dungeon portal (key {p.Key}).");
        }

        // =====================================================================
        // Magical portal VFX (owner felt-test 2026-07-15 "the dungeon portal arch looks
        // plain -- can creative make it magical?"; retuned 2026-07-24 "the harsh magenta
        // swirl + light beams read too hot -- give me a SOFT aura spilling out of the portal").
        // Uses the shared VFXManager pool (catalog key "PP_GroundFog") as a soft ground mist
        // that pools at the arch base -- so the entrance reads as a gentle magical emanation,
        // on top of the arch's own PortalVFXController soft-violet halo. ONE pooled looping
        // system per portal (mobile-cheap, returned to the pool on teardown). COLORBLIND-SAFE
        // (owner red/green): the read is soft LUMINANCE + slow drift, the faint violet is only a hint.
        // =====================================================================
        // SOFT aura spilling out of the portal (owner felt-test 2026-07-24: the harsh
        // magenta rune-ring + light pillars read too hot -- swap the layer to a low, soft
        // ground mist that POOLS around the arch base). Luminance-led + near-neutral: a
        // pale cool white with only the faintest violet hint, at reduced alpha, so it reads
        // as a gentle glow spilling out rather than a bright saturated ring. COLORBLIND-SAFE
        // (owner red/green): the read is soft LUMINANCE + slow motion, hue is barely a whisper.
        private static readonly Color GateTint = new Color(0.82f, 0.80f, 0.92f, 0.45f); // pale cool white, faint violet hint, low alpha
        private const string GateVfxKey  = "PP_GroundFog";   // soft ground mist emanation (was harsh "Dungeon_Portal_Gate" rune-ring)
        private const float  GateVfxScale = 3.4f;  // wide + low: mist pools around the ~1.8 m arch base, not a framed ring

        private void AttachGateVfx(Portal p)
        {
            if (p == null || p.Root == null) return;
            if (p.GateVfx != null) return;   // already holding the loop (idempotent)

            // Seat the mist just above the arch base so it pools on the floor threshold
            // without z-fighting the ground. Parent to the arch so it tracks + tears down with it.
            Vector3 mistPos = p.Root.position + Vector3.up * 0.05f;
            p.GateVfx = VFXManager.PlayKey(
                GateVfxKey, mistPos, Quaternion.identity, p.Root,
                GateTint, GateVfxScale);

            FlowTrace.Step("Portal",
                $"AttachGateVfx: '{GateVfxKey}' soft ground mist " +
                (p.GateVfx != null
                    ? $"spawned at ({mistPos.x:F1}, {mistPos.y:F1}, {mistPos.z:F1}) (loop held) -- soft aura now spills out of the portal base."
                    : "no-op (VFXManager/catalog not ready or key unauthored -- regen the Hovl catalog) -- procedural glow remains."));
        }

        // =====================================================================
        // WO-869 THRESHOLD AURA - routing WIRED, prefab pick HELD for the owner.
        // ---------------------------------------------------------------------
        // The WO asks for an aura from the shipped Mirza Beig Ultimate VFX pack, and the pack
        // does contain exactly five portal loops (verified on disk, and the inventory doc
        // docs/asset-inventory/04_vfx_spells_audio.md independently counts "portal x5"):
        //     pf_vfx-ult_demo_psys_loop_ghostPortal
        //     pf_vfx-ult_demo_psys_loop_ghostPortal2
        //     pf_vfx-ult_demo_psys_loop_portalBlue
        //     pf_vfx-ult_demo_psys_loop_portalBlueTutorial
        //     pf_vfx-ult_demo_psys_loop_portalOrange
        //
        // WHICH ONE IS NOT MINE TO DECIDE. The standing owner directive is: the OWNER tags the
        // VFX key in the Caster, the CLI maps key -> hook VERBATIM, and an UNTAGGED hook is HELD,
        // never filled by whatever looks right (memory: vfx-map-owner-tags-no-creative-pick).
        // None of the five is tagged in Assets/Editor/VfxManualPicks.json today, and the owner's
        // 2026-08-04 relaxation covered the "projectile" category for TOWERS - not the portal.
        //
        // So the SEAM is fully built and the pick is the only thing missing: the moment the owner
        // tags a row with this key, the aura lights up with no further code change. Until then
        // PlayKey is a clean no-op and the rebuilt geometry + PP_GroundFog mist carry the read.
        private const string ThresholdAuraKey = "Portal_Threshold_Aura";

        // =====================================================================
        // WO-1035 SEATING RATIOS (owner 2026-08-16, verbatim: "the portal VFX prefab is
        // supposed to be inside the portal model, not larger than it" / "Maybe 1/3 of
        // protal.fbx height centered at y/2 x(end-start)/2 to be centered").
        // ---------------------------------------------------------------------
        // WHAT WAS WRONG - and it was arithmetic, not art. The old code passed
        //     scale = bounds.size.x * 0.9
        // straight into VFXManager.PlayKey / localScale. That number is a WORLD SIZE IN
        // METRES (~4-5 m for a 6 m landmark arch), but the parameter it fed is a
        // MULTIPLIER on an already-several-metres-wide authored prefab: the portalBlue
        // vortex is authored around a ~2 m particle and "Magic circle dark star" around a
        // ~5 m plane. Multiplying one by the other is what produced 15-25 m effects
        // standing beside a 6 m portal - the owner's "huge blobs".
        //
        // THE FIX, and why it cannot drift: measure the portal, take the owner's fraction
        // of it to get a TARGET SIZE IN METRES, then ask VFXManager to MEASURE THE PREFAB
        // and return the multiplier that makes it read at that size (ResolveFitScale, the
        // WO-870 fit-to-size seam - "measure -> normalize -> scale by the gameplay number,
        // never trust authored scale"). Both halves are measured; the only constants below
        // are documented RATIOS against those measurements, never metres.
        // =====================================================================

        /// <summary>Owner's fraction: the effect spans ~1/3 of the portal's measured extent, so it
        /// fills the middle of the opening and is contained by the arch on every side.</summary>
        private const float OpeningSpanFraction = 1f / 3f;

        /// <summary>Clamp on the derived fit multiplier. NOT a size: it is the bound on
        /// target/measured, so a degenerate measurement (a prefab that measures 0.01) cannot turn
        /// into a 500x effect, and a huge one cannot collapse to nothing. Deliberately wide - the
        /// derivation, not the clamp, is meant to pick the number.</summary>
        private const float MinFitScale = 0.02f;
        private const float MaxFitScale = 20f;

        // =====================================================================
        // Owner 2026-08-14: "all of the portals should be this" + "i want high end much better
        // vfx prefabs that actually look good".
        // ---------------------------------------------------------------------
        // Until now the overworld gate and the dungeon exit were two different objects wearing
        // the word "portal": ~18 code-built cubes here, the owner's Tripo art there. Both now
        // load the SAME structure through DeNelle.Core.World.PortalStructure.
        //
        // The cube arch is NOT deleted - it is the standing fallback while the async load is in
        // flight and permanently if the content build is missing. On success it is retired and
        // the discovery layer RETARGETS onto the real renderers, or the fog-of-war reveal would
        // keep dimming geometry the player can no longer see while the real art blazed away at
        // full brightness from frame 0.
        // =====================================================================
        private async UniTaskVoid SwapInSharedStructureAsync(Portal p)
        {
            if (p == null || p.Root == null) return;

            // Landmark scale, not interior scale: this arch is read from across an open field at
            // ~141 m, where the exit portal's 2.7 m (1.5x hero) would be a speck. PortalHeight is
            // the existing, owner-tuned landmark height and stays the single source for it.
            var swap = await PortalStructure.SwapInAsync(p.Root, PortalHeight, "Portal_Owner");
            // STORE the swap BEFORE the Ok gate: SwapInAsync creates its Addressables handle
            // before awaiting, so the result can carry a VALID handle even when Ok is false
            // (load failed / host died mid-await). Returning first stranded that handle where
            // the teardown loop's PortalStructure.Release(ref p.Swap) could never release it —
            // one leaked handle per failed swap, portal bundle resident all session
            // (review fba0b1079..0e4690036 finding #5; DungeonExitInteractable already
            // assigns-then-checks, this mirrors it).
            p.Swap = swap;
            if (!swap.Ok || p.Root == null) return;   // Warn already emitted by PortalStructure
            p.ArtStanding = true;

            // Retire the placeholder cubes now that real art stands in their place.
            for (int i = 0; i < p.Renderers.Length; i++)
                if (p.Renderers[i] != null) p.Renderers[i].gameObject.SetActive(false);

            // RETARGET the discovery dim onto the real renderers and re-apply the level this
            // portal is currently at, so an undiscovered portal stays a faint silhouette.
            var real = swap.Instance.GetComponentsInChildren<Renderer>(true);
            p.Renderers = real;
            p.BaseColors = new Color[real.Length];
            for (int i = 0; i < real.Length; i++) p.BaseColors[i] = SafeColor(real[i]);
            ApplyDim(p, p.Discovered ? Mathf.Lerp(UndiscoveredDim, 1f, p.FadeT) : UndiscoveredDim);

            // WO-1062: THE ONE REAL GAP IN THE MAGENTA NET ON THIS SCREEN. BuildPortal already
            // sweeps this root (MagentaGuard.SweepGameObject) and registers the cube arch as
            // protected art - but THIS art arrives AFTER that sweep, on an async continuation,
            // so its renderers had never been looked at by anything. That is how a magenta patch
            // survives on a portal in a project that has a global guard. Sweep the swapped-in
            // instance here, on the same per-object seam, so the offender self-identifies in the
            // break-log instead of costing a guess-and-rebuild cycle.
            DeNelle.Core.MagentaGuard.SweepGameObject(
                swap.Instance, "DungeonWorldPortalSpawner.SwapInSharedStructureAsync");

            HubFoliageInjector.ClearPortalApproach(p.Root);

            // The procedural glow quad / halo / vortex were the no-art fallback. With real art
            // and a real catalogued loop they are two additive billboards inside somebody else's
            // vortex - stand them down (the point light stays; it lights the arch geometry).
            var vfx = p.Root.GetComponent<PortalVFXController>();
            if (vfx != null) vfx.SuppressProceduralSurfaces("shared PortalStructure art is standing");

            // Re-seat the aura on the real opening: it was sized to the cube arch, and the
            // loaded art's bounds are the only honest reference for where the threshold IS.
            if (p.ThresholdVfx != null) { p.ThresholdVfx.Stop(); p.ThresholdVfx = null; }
            if (p.Discovered) AttachThresholdAura(p);
            // Re-seat the owner's dark-star circle the same way: it was centred + scaled on the
            // cube arch, and the loaded art's measured bounds are the only honest reference.
            // WO-1062: BOTH planes come off and BOTH go back on - one teardown owner.
            ClearPortalCircle(p);
            if (p.Discovered) AttachPortalCircle(p);

            // WO-1114 CALL SITE 2 of 2 — THE RE-SEAT. Both measured VFX were just
            // re-attached against the real art's bounds; if this door is closed they must
            // come straight back off, or the sigil treatment is silently undone every time
            // the shared structure finishes loading. It is async, so missing this reads as
            // "correct in the editor, wrong on device".
            p.DoorStateApplied = false;
            ApplyDoorState(p, DungeonStatusCatalog.For(p.DungeonId));

            FlowTrace.Step("Portal",
                $"overworld gate now wears the shared portal structure at {PortalHeight:0.#}m - " +
                $"{real.Length} real renderer(s), cube arch retired, discovery dim retargeted.");
        }

        private void AttachThresholdAura(Portal p)
        {
            if (p == null || p.Root == null) return;
            if (p.ThresholdVfx != null) return;   // already holding the loop (idempotent)

            // MEASURED seat + MEASURED size (WO-1035). Both come from the portal's own renderer
            // bounds - the real structure's once it is standing, the cube arch's while the async
            // swap is in flight - so a re-scaled, re-authored or swapped mesh carries the effect
            // with it instead of stranding it at a literal.
            Bounds b = MeasurePortalBounds(p, out string src);
            Vector3 thresholdPos = OpeningCentre(p, b);
            float target = OpeningTargetSize(b);
            float scale = VFXManager.ResolveFitScale(ThresholdAuraKey, target, MinFitScale, MaxFitScale);
            // WO-1156, visually measured by StructurePoseCapture.RunPortalAngles: portalBlue is
            // directional. Root.rotation presents only a thin vertical trace from the real
            // doorway approach; local +90Y aligns its face with the art's Root.right normal.
            Quaternion auraRotation = p.Root.rotation * Quaternion.Euler(0f, 90f, 0f);
            p.ThresholdVfx = VFXManager.PlayKey(
                ThresholdAuraKey, thresholdPos, auraRotation, p.Root,
                null, scale);

            if (p.ThresholdVfx != null)
            {
                FlowTrace.Step("Portal",
                    $"AttachThresholdAura: '{ThresholdAuraKey}' aura seated at the measured opening centre " +
                    $"({thresholdPos.x:F1}, {thresholdPos.y:F1}, {thresholdPos.z:F1}) - {BoundsLine(b, src)} " +
                    $"target={target:0.00}m (x{OpeningSpanFraction:0.###} of the smaller measured span) " +
                    $"authored={VFXManager.MeasureKeyVisualSize(ThresholdAuraKey):0.00}m -> scale={scale:0.000} " +
                    $"normal=Root.right localYaw=+90 (loop held).");
                return;
            }

            // Section 12: say WHY it is absent, and say it once - so this reads as a deliberate
            // state in a capture rather than a broken effect someone re-debugs later.
            FlowTrace.Once("Portal", "threshold-aura-unresolved",
                $"AttachThresholdAura: key '{ThresholdAuraKey}' did NOT resolve. It IS tagged (owner directive " +
                "2026-08-14) in Assets/Editor/VfxManualPicks.json -> Mirza Beig Ultimate VFX " +
                "'pf_vfx-ult_demo_psys_loop_portalBlue', so the live causes are: the Hovl catalog has not been " +
                "regenerated since the tag (Defenders/VFX/Generate Hovl VFX Catalog), or the global loop cap is " +
                "hit. The arch + PP_GroundFog mist carry the read meanwhile.");
        }

        // =====================================================================
        // OWNER VFX PICK 2026-08-16 (verbatim): "Assets\Hovl Studio\Magic circles\Prefabs\
        // Magic circle dark star.prefab" - "use this rotated for the portals".
        // ---------------------------------------------------------------------
        // The pick is mapped VERBATIM (memory: vfx-map-owner-tags-no-creative-pick). The
        // Hovl pack is GITIGNORED, so runtime loads the TRACKED MIRROR written by
        // DeNelle.Editor.PortalCircleVfxMirror.Run (markers PORTAL_CIRCLE_VFX_OK/FAIL)
        // to Assets/Resources/VFX/Portal/PortalCircleDarkStar.prefab - keep the two
        // paths in lockstep. Mirror absent = warn-once + the portal renders exactly as
        // before (never an error; a fresh clone must not go red on art).
        //
        // "ROTATED" is the owner's key word: the prefab is authored as a FLAT GROUND
        // circle (face up, +Y normal). For the portals it STANDS VERTICAL in the arch
        // opening, with its face-normal pointing OUT THROUGH THE DOORWAY.
        //
        // ⛔ THE DOORWAY NORMAL ON THIS ART IS Root.RIGHT, NOT Root.forward. MEASURED,
        // not assumed: DeNelle.Editor.StructurePoseCapture.RunPortalAngles orbits the
        // real 'dungeon/exit/portal' art (Assets/Art/Dungeon/Exit/Portal.fbx) at eight
        // headings and writes a PNG of each. At heading 0/180 - i.e. along Root.forward -
        // the camera sees the arch's SOLID STONE SIDE and the opening is not visible at
        // all. The doorway is face-on at heading 90/270, which is +-Root.right. Rotating
        // about local X (Euler +-90,0,0) puts the circle's face-normal on Root.forward,
        // which is PERPENDICULAR to the doorway - so both approaches saw the disc EDGE-ON
        // as a grey shard. That is the owner's 2026-08-22 defect, and the earlier X-axis
        // fix did not remove it. The rotation therefore goes on local Z, which is the
        // axis WO-1062 section 2 explicitly authorised choosing:
        // "if the arch's local axes make a literal +90/-90 point the planes somewhere
        //  other than outward along the doorway, the intent is 'one facing each way out
        //  of the arch' - implement that and state which axis you applied it on."
        // Evidence: docs/ui-evidence/portal-angles-2026-08-23/*.png.
        //
        // ⚠ The code-built cube-arch FALLBACK is the other way round - BuildArch puts its
        // pillar rings at +-ArchHalfDepth on Z, so you walk through it along Root.forward.
        // The axis is therefore chosen PER GEOMETRY off MeasurePortalBounds' own `src`,
        // never hardcoded; see AttachPortalCircle.
        //
        // ADDITIVE to the existing threshold aura (portalBlue vortex) + PP_GroundFog
        // mist, per the routing note: the owner said "use this for the portals", which
        // reads as the face presentation - whether the vortex stands down is the
        // OWNER's call after seeing them stacked, not ours.
        //
        // Deliberately NOT routed through VFXManager.PlayKey: the mirror is a plain
        // tracked prefab (no catalog row), a direct child instance costs no global
        // loop slot (the 20-slot cap starvation class), and teardown is free - the
        // instance dies with its portal Root.
        // =====================================================================
        private const string CirclePrefabResourcePath = "VFX/Portal/PortalCircleDarkStar";

        // WO-1062, OWNER-AUTHORED 2026-08-22 (verbatim: "put .25 between them"). Distance in
        // metres between the two outward-facing planes, measured along the threshold normal.
        // ⛔ Not a tuning knob - do not re-tune it to "fix" the look; if the result is wrong
        // it is the rotation axis, not this number (WO-1062 section 2).
        private const float CircleSeparation = 0.25f;

        // Load-once cache; _circleLooked distinguishes "never tried" from "tried, absent"
        // so a missing mirror costs one Resources.Load per session, not one per portal.
        private static GameObject _circlePrefab;
        private static bool _circleLooked;

        // =====================================================================
        // WO-1114 — THE ONE APPEARANCE OWNER FOR DOOR STATE.
        // ---------------------------------------------------------------------
        // This method is the ONLY thing that decides how a dungeon door LOOKS for
        // its status. It adds no spawner, no holder GameObject and no teardown
        // path: everything it touches is the existing per-portal Portal record, so
        // the WO-753 one-owner teardown at TickDiscovery still owns every handle it
        // leaves behind. ⛔ Do not add a rival visual path for a closed door.
        //
        // CLOSED reads as DARK AND INERT: the threshold aura and the owner's
        // dark-star circle come off, and the procedural glow surfaces stand down.
        // That is already correct world-language for "this does not open", and it
        // is the DEFAULT treatment for every closed state today.
        //
        // ⛔ SIGIL ART IS NOT INVENTED HERE. The payload may carry a sigil key
        // ("seal" / "rubble" / "water"), but no VFX prefab is tagged for one yet and
        // the CLI never picks or substitutes an effect (owner directive: the owner
        // tags the key, the CLI maps it verbatim). Until then an unresolved key logs
        // ONCE and the default treatment ships. The seat is MEASURED here and now —
        // MeasurePortalBounds -> OpeningCentre -> OpeningTargetSize, the same three
        // helpers the aura and circle use - so when the art is tagged it drops into
        // a proven, already-logged position instead of a guessed one.
        // =====================================================================
        private void ApplyDoorState(Portal p, DungeonDoorInfo info)
        {
            if (p == null || p.Root == null) return;

            bool changed = !p.DoorStateApplied || p.DoorState != info.State;
            p.DoorState = info.State;
            p.DoorStateApplied = true;

            if (info.IsOpen)
            {
                // OPEN is the ground state and every failure path resolves to it. Restore
                // the discovered treatment if a previous closed pass took it off - a door
                // that re-opens must not stay dark until the next boot.
                if (p.Discovered) { AttachThresholdAura(p); AttachPortalCircle(p); }
                if (changed)
                    FlowTrace.Step("Portal",
                        $"door '{p.DungeonId}' is OPEN - standard portal treatment stands.");
                return;
            }

            // Closed: take the invitation off. Same calls, same fields, same owner as the
            // WO-753 teardown - no second path.
            if (p.ThresholdVfx != null) { p.ThresholdVfx.Stop(true); p.ThresholdVfx = null; }
            ClearPortalCircle(p);   // WO-1062: both outward planes, one teardown owner
            var vfx = p.Root.GetComponent<PortalVFXController>();
            if (vfx != null) vfx.SuppressProceduralSurfaces("WO-1114: the door is closed - dark and inert");

            // Measure the seat the sigil WILL take, and say so in the log, so the art
            // hand-off is numbers rather than another guess (CLAUDE.md section 12).
            Bounds b = MeasurePortalBounds(p, out string src);
            Vector3 centre = OpeningCentre(p, b);
            float target = OpeningTargetSize(b);

            if (!string.IsNullOrEmpty(info.Sigil))
                FlowTrace.Once("Portal", "sigil-unresolved-" + info.Sigil,
                    $"ApplyDoorState: sigil key '{info.Sigil}' has no tagged VFX yet - shipping the " +
                    "DEFAULT closed treatment (aura + circle removed). The key is mapped verbatim " +
                    "once the owner tags art for it; nothing is substituted.");

            if (changed)
                FlowTrace.Step("Portal",
                    $"door '{p.DungeonId}' is {info.State} - aura + circle removed, portal reads dark and inert. " +
                    $"Sigil seat measured at ({centre.x:0.0},{centre.y:0.0},{centre.z:0.0}) target={target:0.00}m " +
                    $"{BoundsLine(b, src)} sigil='{(string.IsNullOrEmpty(info.Sigil) ? "none" : info.Sigil)}'.");
        }

        // =====================================================================
        // WO-1062 — THE ONE TEARDOWN OWNER FOR THE PORTAL FACE.
        // ---------------------------------------------------------------------
        // The face is TWO instances (see Portal.CircleVfx / CircleVfxBack). Every
        // site that used to Destroy the single handle now calls this instead, so a
        // plane can never be added or moved without every teardown path following
        // it. ⛔ Do not re-inline a Destroy of one plane at a call site.
        // =====================================================================
        private void ClearPortalCircle(Portal p)
        {
            if (p == null) return;
            if (p.CircleVfx != null) { Destroy(p.CircleVfx); p.CircleVfx = null; }
            if (p.CircleVfxBack != null) { Destroy(p.CircleVfxBack); p.CircleVfxBack = null; }
        }

        private void AttachPortalCircle(Portal p)
        {
            if (p == null || p.Root == null) return;
            // Idempotent on EITHER handle: a half-attached face (one plane up, one not)
            // is a state we never want to build a second plane on top of.
            if (p.CircleVfx != null || p.CircleVfxBack != null) return;

            if (!_circleLooked)
            {
                _circleLooked = true;
                // Via the VFX seam (Addressables-first, Resources-fallback): Resources/VFX is
            // force-included in every build and is migrating to Addressables. The const is already
            // the full Resources-relative key the grouper addresses, so only the load path changes.
            _circlePrefab = DeNelle.Core.VfxAssetLoader.LoadVfxPrefab(CirclePrefabResourcePath);
            }
            if (_circlePrefab == null)
            {
                FlowTrace.Once("Portal", "portal-circle-missing",
                    $"AttachPortalCircle: Resources '{CirclePrefabResourcePath}' did not load - the " +
                    "owner-picked 'Magic circle dark star' mirror is not on this machine yet (run " +
                    "DeNelle.Editor.PortalCircleVfxMirror.Run, marker PORTAL_CIRCLE_VFX_OK). " +
                    "Portal renders as before - deliberate fallback, never an error.");
                return;
            }

            // Same measured references the threshold aura uses (WO-1035): the real structure's
            // bounds once it stands, the cube arch's while the swap is in flight. The circle
            // prefab has NO catalog row, so it is measured directly through the same fit-to-size
            // seam rather than by a second copy of that measurement here.
            Bounds b = MeasurePortalBounds(p, out string src);
            Vector3 centre = OpeningCentre(p, b);
            float target = OpeningTargetSize(b);
            float scale = VFXManager.ResolveFitScale(_circlePrefab, target, MinFitScale, MaxFitScale);

            // =================================================================
            // WO-1062 (owner ruling 2026-08-22): TWO planes, each facing OUTWARD.
            // -----------------------------------------------------------------
            // ONE plane has ONE good viewing hemisphere, so the OPPOSITE approach saw the
            // unlit BACK FACE - the owner's "black shards". Two planes, back to back,
            // delete that by construction.
            //
            // OWNER-AUTHORED VALUES, not invented: "one rotated 90 other rotated -90" and
            // "put .25 between them". WHICH AXIS carries the +-90 is a MEASURED choice, not a
            // preference, and it is NOT THE SAME FOR BOTH GEOMETRIES - which is exactly why it
            // cannot be a constant:
            //
            //   sharedArt (Assets/Art/Dungeon/Exit/Portal.fbx, what ships) - the doorway opens
            //     along +-Root.RIGHT. Proven by the eight-heading orbit in
            //     docs/ui-evidence/portal-angles-2026-08-23/: at heading 0/180 (Root.forward)
            //     the camera sees SOLID STONE and no opening at all; the doorway is face-on at
            //     90/270. So the +-90 goes on local Z:
            //         Euler(0,0,-90) * up = +Root.right   (one approach)
            //         Euler(0,0,+90) * up = -Root.right   (the other approach)
            //
            //   cubeArch (the code-built fallback, BuildArch) - the OPPOSITE. Its pillar rings
            //     sit at +-ArchHalfDepth on Z and it is ArchHalfWidth wide on X, so you walk
            //     through along +-Root.FORWARD and the +-90 goes on local X, as before.
            //
            // The earlier version hardcoded the X axis for BOTH, so on the shipping art both
            // normals landed on the arch's solid stone SIDE, both doorway approaches read the
            // discs EDGE-ON, and the owner's defect survived its own fix. ⛔ Do not collapse
            // this back to one axis: the two geometries genuinely disagree, and this is
            // re-evaluated on the swap because AttachPortalCircle is re-run there (:1078).
            //
            // The branch reads off `src` - the SAME string MeasurePortalBounds already returns
            // for which geometry it measured - so the seat and the facing can never disagree
            // about which portal is standing.
            //
            // Each plane is pushed HALF the 0.25m separation along that SAME doorway normal,
            // to its own side, so the pair stays centred on the measured opening and the two
            // are never coplanar (no z-fighting).
            //
            // SAME prefab, SAME owner-tagged key for both - nothing is picked or substituted
            // (memory: vfx-map-owner-tags-no-creative-pick). Still ZERO VFXManager loop slots:
            // these are plain Instantiate children of Root, so two of them is still two of
            // nothing as far as the loop budget is concerned.
            // =================================================================
            bool doorwayOnRight = src == "sharedArt";
            Quaternion rotFront = p.Root.rotation *
                (doorwayOnRight ? Quaternion.Euler(0f, 0f, -90f) : Quaternion.Euler(90f, 0f, 0f));
            Quaternion rotBack = p.Root.rotation *
                (doorwayOnRight ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(-90f, 0f, 0f));
            Vector3 normal = doorwayOnRight ? p.Root.right : p.Root.forward;
            Vector3 halfGap = normal * (CircleSeparation * 0.5f);

            var go = Instantiate(_circlePrefab, centre + halfGap, rotFront, p.Root);
            go.name = "[PortalCircle_DarkStar_Front]";
            go.transform.localScale = Vector3.one * scale;
            p.CircleVfx = go;

            var goBack = Instantiate(_circlePrefab, centre - halfGap, rotBack, p.Root);
            goBack.name = "[PortalCircle_DarkStar_Back]";
            goBack.transform.localScale = Vector3.one * scale;
            p.CircleVfxBack = goBack;

            Vector3 front = go.transform.position;
            Vector3 back = goBack.transform.position;
            FlowTrace.Step("Portal",
                $"AttachPortalCircle: '{CirclePrefabResourcePath}' stood vertical INSIDE the opening of portal " +
                $"'{p.Key}' at ({centre.x:F1}, {centre.y:F1}, {centre.z:F1}) as TWO outward-facing planes " +
                $"(WO-1062) - axis={(doorwayOnRight ? "Z/Root.right" : "X/Root.forward")} for geometry '{src}', " +
                $"doorwayNormal=({normal.x:F2},{normal.y:F2},{normal.z:F2}), " +
                $"front at ({front.x:F1}, {front.y:F1}, {front.z:F1}), " +
                $"back at ({back.x:F1}, {back.y:F1}, {back.z:F1}), gap={CircleSeparation:0.00}m " +
                $"along that doorway normal - over facingYaw " +
                $"{p.Root.eulerAngles.y:F0} - {BoundsLine(b, src)} target={target:0.00}m " +
                $"authored={VFXManager.MeasureVisualSize(_circlePrefab):0.00}m -> scale={scale:0.000} " +
                "(owner pick 2026-08-16: Magic circle dark star as the portal face; 0 loop slots).");
        }

        // =====================================================================
        // THE ONE MEASURED REFERENCE (WO-1035). Every seat + size above reads from here,
        // so the aura and the circle can never disagree about where the opening is.
        // =====================================================================

        /// <summary>World renderer bounds of whatever portal geometry is CURRENTLY standing:
        /// the loaded shared structure once the async swap has landed, else the code-built cube
        /// arch (which is the deliberate standing fallback, not an error state). Falls back once
        /// more to a synthetic box from the arch's own authoring constants if nothing measurable
        /// exists yet, so this never returns degenerate bounds to a caller.</summary>
        private Bounds MeasurePortalBounds(Portal p, out string src)
        {
            if (p != null && p.ArtStanding && p.Swap.Instance != null)
            {
                Bounds art = PortalStructure.MeasureBounds(p.Swap.Instance);
                if (art.size.y > 0.001f) { src = "sharedArt"; return art; }
            }

            // Cube-arch fallback: measure the primitives that are actually up rather than
            // re-deriving them, so a retune of BuildArch carries automatically.
            if (p != null && p.Renderers != null)
            {
                Bounds b = default; bool has = false;
                for (int i = 0; i < p.Renderers.Length; i++)
                {
                    var r = p.Renderers[i];
                    if (r == null || !r.gameObject.activeInHierarchy) continue;
                    if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
                }
                if (has && b.size.y > 0.001f) { src = "cubeArch"; return b; }
            }

            // Last resort - the arch's authoring constants, which is what BuildArch will build.
            src = "synthetic";
            Vector3 root = p != null && p.Root != null ? p.Root.position : Vector3.zero;
            float w = (ArchHalfWidth + PillarThickness) * 2f;
            return new Bounds(root + Vector3.up * (PortalHeight * 0.5f),
                              new Vector3(w, PortalHeight, ArchHalfDepth * 2f + PillarThickness));
        }

        /// <summary>Centre of the opening = the measured bounds centre (the owner's "centered at
        /// y/2 x(end-start)/2"). Never a literal: the structure is height-normalized at runtime,
        /// so a hardcoded y goes silently wrong the next time the art or PortalHeight is retuned.</summary>
        private Vector3 OpeningCentre(Portal p, Bounds b)
        {
            if (b.size.y <= 0.001f)
                return (p != null && p.Root != null ? p.Root.position : Vector3.zero)
                       + Vector3.up * (PortalHeight * 0.5f);
            return b.center;
        }

        /// <summary>Target world size of the effect: the owner's 1/3 fraction of the portal's
        /// measured extent. Taken against the SMALLER of the two facing spans (height, width) so
        /// the effect is contained on BOTH axes - a wide-and-short arch would otherwise get an
        /// effect that overflows its pillars while its height read perfectly correct.</summary>
        private float OpeningTargetSize(Bounds b)
        {
            float span = Mathf.Min(b.size.x, b.size.y);
            if (span <= 0.001f) span = PortalHeight;
            return span * OpeningSpanFraction;
        }

        /// <summary>The measured numbers, formatted once, so a capture reads NUMBERS at the next
        /// tuning pass instead of inviting another guess (CLAUDE.md section 12).</summary>
        private static string BoundsLine(Bounds b, string src) =>
            $"measured[{src}] size=({b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00}) " +
            $"centre=({b.center.x:0.0},{b.center.y:0.0},{b.center.z:0.0}) " +
            $"y[{b.min.y:0.00}..{b.max.y:0.00}]";

        private static void ApplyDim(Portal p, float brightness)
        {
            if (p == null || p.Renderers == null) return;
            brightness = Mathf.Clamp01(brightness);
            bool visible = brightness > 0.001f;
            for (int i = 0; i < p.Renderers.Length; i++)
            {
                var r = p.Renderers[i];
                if (r == null) continue;
                Color baseCol = (p.BaseColors != null && i < p.BaseColors.Length) ? p.BaseColors[i] : Color.white;
                Color dimmed = new Color(baseCol.r * brightness, baseCol.g * brightness,
                                         baseCol.b * brightness, baseCol.a);
                if (r.enabled != visible) r.enabled = visible;
                SetColor(r, dimmed);
            }
        }

        private static Color SafeColor(Renderer r)
        {
            if (r == null) return Color.white;
            var m = r.sharedMaterial;
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.color;
            return Color.white;
        }

        private static void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            var m = r.material; // instanced — don't stomp the shared arch material across portals
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else if (m.HasProperty("_Color")) m.color = c;
        }

        // =====================================================================
        // Data — DungeonDef from Resources, else an inline fallback covering ONLY the
        // dungeon scenes that exist in the build (identical policy to DungeonEntranceBootstrap).
        // =====================================================================
        private static List<DungeonDef> LoadDefs()
        {
            var all = new List<DungeonDef>(Resources.LoadAll<DungeonDef>("Dungeons"));
            var built = new List<DungeonDef>(all.Count);
            foreach (var d in all)
            {
                if (d == null || string.IsNullOrEmpty(d.SceneName)) continue;
                // The WO-776 "FolksGranary" skip is GONE because the def it skipped is gone:
                // Resources/Dungeons/FolksGranary.asset and HealersCottage.asset — the only two
                // legacy ScriptableObject dungeons in the project — were deleted 2026-08-22 and
                // replaced by composed graphs (dg_folks_granary / dg_healers_cottage, injected
                // below). This foreach now iterates an empty Resources folder in practice; it is
                // kept, not deleted, so a future authored DungeonDef still works.
                //
                // Only place portals to scenes that are actually loadable (in Build Settings).
                if (d.SceneExists && UnityEngine.Application.CanStreamedLevelBeLoaded(d.SceneName))
                    built.Add(d);
            }

            // REROUTE (WO): guarantee the composed starter-loop dungeon is represented whenever
            // its scene is in the build, without needing a Resources/Dungeons .asset. Its
            // DungeonId IS the full scene name, so DungeonPortal loads it verbatim (see
            // DungeonPortal.EnterDungeon). The east AuthoredPortal row keys placement to it.
            const string StarterLoop = "dg_starter_loop";
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(StarterLoop)
                && !built.Exists(x => x != null && string.Equals(x.DungeonId, StarterLoop, System.StringComparison.OrdinalIgnoreCase)))
            {
                built.Add(MakeDef(StarterLoop, StarterLoop, "Starter Loop", new Color(0.62f, 0.72f, 1f)));
            }

            // WO-1001 Phase 2A: composed multi-level Sunken Vault (same def inject pattern).
            const string SunkenVault = "dg_sunken_vault";
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(SunkenVault)
                && !built.Exists(x => x != null && string.Equals(x.DungeonId, SunkenVault, System.StringComparison.OrdinalIgnoreCase)))
            {
                built.Add(MakeDef(SunkenVault, SunkenVault, "Sunken Vault of Elarion", new Color(0.45f, 0.62f, 0.85f)));
            }

            // WO-1001 Phase 2B: the Bonecrypt (5 floors, key-gated deep floor, Necromancer).
            const string Bonecrypt = "dg_bonecrypt";
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(Bonecrypt)
                && !built.Exists(x => x != null && string.Equals(x.DungeonId, Bonecrypt, System.StringComparison.OrdinalIgnoreCase)))
            {
                built.Add(MakeDef(Bonecrypt, Bonecrypt, "The Bonecrypt", new Color(0.38f, 0.66f, 0.44f)));
            }

            // WO-1001 Phase 2C: the Ember Deep (6 floors, orc into troll, Ogre warlord).
            const string EmberDeep = "dg_ember_deep";
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(EmberDeep)
                && !built.Exists(x => x != null && string.Equals(x.DungeonId, EmberDeep, System.StringComparison.OrdinalIgnoreCase)))
            {
                built.Add(MakeDef(EmberDeep, EmberDeep, "The Ember Deep", new Color(0.92f, 0.55f, 0.28f)));
            }

            // THE HOLLOW ROADS (owner 2026-08-16) — the tunnel system, not a dungeon: one mouth,
            // one crossroads, four arms that drop into the four cardinal biomes. Injected on the
            // same pattern as its siblings above, and behind the SAME CanStreamedLevelBeLoaded
            // test, which is what makes the whole spoke self-hiding before the graph is composed:
            // no scene => no def => no portal, rather than a door onto a missing scene.
            // Flag-gated too, so ff.biomeroads = 0 removes it with no rebuild.
            string hollowRoads = DeNelle.Core.World.BiomeRoads.TunnelSceneId;
            if (DeNelle.Core.FeatureFlags.BiomeRoads
                && UnityEngine.Application.CanStreamedLevelBeLoaded(hollowRoads)
                && !built.Exists(x => x != null && string.Equals(x.DungeonId, hollowRoads, System.StringComparison.OrdinalIgnoreCase)))
            {
                built.Add(MakeDef(hollowRoads, hollowRoads,
                                  DeNelle.Core.World.BiomeRoads.TunnelDisplayName,
                                  new Color(0.72f, 0.66f, 0.86f)));
            }

            // 2026-08-22 — the two REPLACEMENTS for the retired legacy ScriptableObject
            // dungeons, injected on exactly the same pattern as their siblings above.
            //
            // ⛔ THE DISPLAY NAME IS NOT OPTIONAL. DungeonPortal.DisplayName falls back to the
            // RAW ID when no name is authored, so a nameless def puts "dg_healers_cottage" on
            // the arch prompt and on the realm-map pin. Every id injected in this method
            // therefore carries an authored, player-facing name. Names ruled by the owner
            // 2026-08-22 ("healers cottage and grainery"): "Healer's Cottage" and "Granary".
            // ASCII apostrophe (U+0027) only — canon strings are ASCII-only.
            const string HealersCottage = "dg_healers_cottage";
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(HealersCottage)
                && !built.Exists(x => x != null && string.Equals(x.DungeonId, HealersCottage, System.StringComparison.OrdinalIgnoreCase)))
            {
                built.Add(MakeDef(HealersCottage, HealersCottage, "Healer's Cottage", new Color(1f, 0.82f, 0.48f)));
            }

            const string FolksGranary = "dg_folks_granary";
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(FolksGranary)
                && !built.Exists(x => x != null && string.Equals(x.DungeonId, FolksGranary, System.StringComparison.OrdinalIgnoreCase)))
            {
                built.Add(MakeDef(FolksGranary, FolksGranary, "Granary", new Color(0.6f, 0.9f, 0.7f)));
            }

            // No inline fallback any more. It used to name Dungeon_HealersCottage — the legacy
            // hand-built scene — which is retired; a fallback onto a retired scene is a door
            // into the exact content the owner could never get past room 1 of. Every dungeon
            // now comes from the injections above, each gated on CanStreamedLevelBeLoaded, so
            // an uncomposed graph yields NO portal rather than a portal onto nothing.
            return built;
        }

        private static DungeonDef MakeDef(string id, string scene, string name, Color accent)
        {
            var d = ScriptableObject.CreateInstance<DungeonDef>();
            d.DungeonId = id; d.SceneName = scene; d.DisplayName = name;
            d.AccentColor = accent; d.SceneExists = true;
            return d;
        }

        // =====================================================================
        // Helpers.
        // =====================================================================
        private void EnsureHero()
        {
            if (_hero != null) return;
            var p = SafeFindWithTag("Player");
            _hero = p != null ? p.transform : null;
        }

        private static GameObject SafeFindWithTag(string tag)
        {
            try { return GameObject.FindWithTag(tag); }
            catch (UnityEngine.UnityException) { return null; }
        }

        private static string MakeKey(Vector3 pos) =>
            PrefKeyPrefix +
            Mathf.RoundToInt(pos.x) + "_" +
            Mathf.RoundToInt(pos.y) + "_" +
            Mathf.RoundToInt(pos.z);
    }
}
