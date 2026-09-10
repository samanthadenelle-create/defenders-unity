// =============================================================================
// RaidBaseGenerator - data-in / scene-out builder for the RaidBase_*.unity levels.
// -----------------------------------------------------------------------------
// WHAT CHANGED (owner complaint 2026-08-02: "the raid is just a square room with 1
// enemy"). Measured before: the base occupied ~2.4% of the authored 140 m floor (a
// ~21.6 m square), every watchtower sat inside that square so ALL of them could
// shoot the same point, and there was no objective - the raid ended when the last
// BODY fell. The concept the owner approved, built here against the shipped systems:
//
//   1. THE ARENA FILLS ITS SPACE. The ring half-extent now comes from the config's
//      authored `baseRadius` instead of falling out of wallSegmentsPerSide * 1.5 m.
//      With the raid radii raised to 31 / 49 / 54 m the three tiers occupy roughly
//      20% / 50% / 60% of the 140 m RaidGround plane (radius = mapRadius * sqrt(f),
//      mapRadius = 70 m). wallSegmentsPerSide is NOT dead - it is now the MINIMUM
//      panel count per side (i.e. the granularity + the gate width); the builder adds
//      panels as needed so no panel is stretched past MaxSegmentWidth.
//   2. AN OUTER WALL RING IS THE BOUNDARY. Same BuildRing primitive, now solid: each
//      panel gets a real BoxCollider on the "Structure" layer, so walls block
//      movement and line-of-sight instead of being decoration (they never carried a
//      collider before - RaidBaseGenerator only called WallSegment.SetTier, and only
//      WallSegment.Configure builds the blocker).
//   3. MULTIPLE AGGRESSIVE TOWERS on two bands (outer wall line + an inner ring that
//      covers the spire), so there is no safe approach AND no safe spot at the
//      objective. Count = archerTowerCount + mageTowerCount (4 / 7 / 10). TYPE comes
//      from the previously-dead `towers[]` array; range / fire-rate / element /
//      damage-weight / art come from that type's structures-catalog row.
//   4. A CENTRAL SPIRE (RaidSpire) with tier HP 1200 / 2200 / 3500. DESTROYING IT
//      WINS THE RAID. Its art comes from the previously-dead `centralBuilding` key.
//
// KEYS THAT WERE AUTHORED BUT READ BY NOTHING, AND NOW HAVE CONSUMERS:
//   centralBuilding -> the spire's structures-catalog art id      (PlaceSpire)
//   towers[]        -> the turret TYPE palette (weighted by count)(ResolveTowerTypes)
//   difficulty      -> spire HP + the tower DPS budget            (TierFor)
//   baseRadius      -> the ring half-extent (was ignored here)    (BuildConfigLayout)
// WO-1608: `props` + `raidDress` are LIVE — RaidBaseDresser consumes them at bake time.
// WO-932: `eliteCount` is LIVE — RaidGarrisonSpawner.ExpandComposition consumes it.
//
// BALANCE (the reason this is not just "more towers"): every turret's damage is
// scaled by a single factor k so that the WORST point in the arena - found by
// sampling the whole floor - can never take more than the tier's DPS budget. k is
// clamped to <= 1, so a turret is never made stronger than its catalog row authors.
// The builder logs the worst-case concurrency, the resulting DPS and the hero
// time-to-death at 100 HP (base) and 195 HP (geared).
//
// WO-1632: EVERY raid base now also carries an EXTERIOR ARENA BOUNDARY - a square ring
// of landscape pieces at +/-ArenaBoundaryHalfExtent, i.e. the edge of the 140 m plane
// itself, NOT the edge of the base. Owner 2026-09-10: "exterior walls around entire
// arena" / "similar strategy as we used in battle arena". It is the siege venue's own
// OuterBoundary_Ring vocabulary, shared through DeNelle.Editor's ArenaBoundaryRing. It
// has NO gate, it is not part of the layered defense, and it does not touch the base's
// Outer / Keep rings, the spire fit, EnsureUpright or garrison HP.
//
// STILL TRUE FROM BEFORE: tier-driven wall art via WallTierData, "Watchtower" in the
// name so GarrisonTurretArmer can still arm anything this builder did not stat,
// BossSpawn marker for RaidGarrisonSpawner, idempotent roots, LogWarning + fall back
// on a missing (gitignored) prefab - never an error, never a throw.
//
// NAVMESH: this builder does NOT bake one - RaidNavBake is the ONE baker for these
// scenes (it drops the 140 m RaidGround plane, marks renderers NavigationStatic and
// runs the legacy scene bake). Run it AFTER this. See BuildAllRaidScenes' log line.
//
// Batchmode: DeNelle.Editor.RaidBaseGenerator.BuildInOpenScene
//            DeNelle.Editor.RaidBaseGenerator.BuildIntoScene  (pass a scene path)
//            DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes
// Menu:      Defenders/Walls/Build Raid Base - Iron Bastion
//            Defenders/Walls/Build All Raid Scenes (config-driven)
// =============================================================================
using System;                   // Enum.TryParse, StringComparison
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Newtonsoft.Json;          // structures-catalog read (same reader shape as CatalogBootstrap)
using DeNelle.Core;             // CanonicalJson, MagentaGuard
using DeNelle.Core.Combat;      // DamageElement
using DeNelle.Village;          // WallSegment, SceneConfigCatalog, SceneConfigDef, DefenseTower
using DeNelle.Village.Walls;    // WallTier, WallTierData
using DeNelle.Village.World.Camps;  // RaidSpire, RaidGarrisonSpawner
using Object = UnityEngine.Object;   // disambiguate Object (System.Object vs UnityEngine.Object)

namespace DeNelle.Editor
{
    public static class RaidBaseGenerator
    {
        // == Arena scale =======================================================

        /// <summary>
        /// Half-extent of the ground RaidNavBake authors (GroundScale 14 -> a 140 m
        /// Unity Plane -> +/-70 m). The footprint fractions in the tier table are
        /// expressed against THIS. If RaidNavBake.GroundScale changes, change this.
        /// </summary>
        public const float MapHalfExtent = 70f;

        // -- ARENA EXTERIOR BOUNDARY (WO-1632) ---------------------------------
        // Owner directive 2026-09-10, verbatim: "exterior walls around entire arena", then
        // "similar strategy as we used in battle arena". The battle arena frames its venue
        // with ProceduralSiegeArenaBuilder's OuterBoundary_Ring - large polyperfect rocks,
        // colliders ON, "so it reads as a wall". The same vocabulary now frames every raid
        // arena, through the shared ArenaBoundaryRing helper.

        /// <summary>
        /// How far a boundary piece may hang PAST the RaidGround plane edge. The pieces are
        /// static scenery and the plane's own edge is not walkable, so a small overhang is
        /// cosmetic - but it is bounded, not unlimited.
        /// </summary>
        private const float ArenaBoundaryEdgeTolerance = 1.2f;

        /// <summary>
        /// THE BAND. The ring must live entirely between two hard lines:
        /// <list type="bullet">
        /// <item>INNER: <c>MapHalfExtent - StagingPlaneEdgeMargin</c> - the furthest a staging
        /// marker can ever sit, because <see cref="PlaceStagingMarker"/>'s diagonal fallback
        /// (:503-528) clamps its leg at exactly that per axis.</item>
        /// <item>OUTER: <c>MapHalfExtent + ArenaBoundaryEdgeTolerance</c>.</item>
        /// </list>
        /// The ring LINE is the band's midpoint and <see cref="ArenaBoundaryBandHalf"/> is
        /// half its width; a piece reaching more than that inward or outward breaks it.
        /// <para/>
        /// ⚠ THIS REPLACED A CHOSEN INSET, AND THE BAKE IS WHY. WO-1632 first authored
        /// <c>ArenaBoundaryInset = 1.5f</c> with the inward reach priced at
        /// <c>scaleMax / 2</c> - i.e. assuming a 1 m mesh. The 2026-09-10 bake measured the
        /// palette at 3.38 m across, so the 3.4x scale produced an 11.5 m piece reaching
        /// 5.75 m inward and <see cref="AssertBoundaryContainsStaging"/> fired on all three
        /// configs (inner face 62.8 m vs a 66.0 m clamp). The instrument caught it; the fix
        /// is to make the SCALE follow the band instead of the band follow the scale.
        /// </summary>
        private const float ArenaBoundaryBandHalf =
            (StagingPlaneEdgeMargin + ArenaBoundaryEdgeTolerance) * 0.5f;

        /// <summary>Square half-extent of the exterior boundary ring = the band's midpoint.</summary>
        public const float ArenaBoundaryHalfExtent =
            MapHalfExtent + (ArenaBoundaryEdgeTolerance - StagingPlaneEdgeMargin) * 0.5f;

        /// <summary>
        /// Share of the band the widest piece may occupy.
        /// <para/>
        /// ⚠ IT MUST LEAVE ROOM FOR THE JITTER **AND** THE SLACK, and the second bake is why.
        /// At 0.85 the widest piece reached 2.21 m of a 2.60 m half-band and the jitter bound
        /// then spent the remaining 0.39 m exactly, so <c>reach + jitter == bandHalf</c> and the
        /// inner face landed EXACTLY on the 66.0 m staging clamp - the assert fired on all three
        /// configs a second time, on a ring that was otherwise built to spec. Zero slack is not a
        /// pass. The live invariant is
        /// <c>bandHalf*fill + jitter + ArenaBoundaryContainmentSlack &lt;= bandHalf</c>, which at
        /// slack 0.3 over a 2.60 m half-band caps the fill at ~0.88 before the jitter gets
        /// anything at all; 0.70 leaves the jitter ~0.48 m and both faces a full 0.30 m.
        /// </summary>
        private const float ArenaBoundaryBandFill = 0.70f;

        /// <summary>
        /// The margin the ring must keep from BOTH band edges - the staging clamp inward and the
        /// plane-edge tolerance outward. Named in the assert's own message so a failure says how
        /// much room it wanted, not just that it wanted some. Never zero: an inner face that only
        /// EQUALS the clamp is a marker sitting on a boulder.
        /// </summary>
        private const float ArenaBoundaryContainmentSlack = 0.3f;

        /// <summary>
        /// Extra room the LAYOUT reserves ON TOP of <see cref="ArenaBoundaryContainmentSlack"/>,
        /// so the designed clearance EXCEEDS the required clearance instead of equalling it.
        /// <para/>
        /// ⚠ THIS EXISTS BECAUSE THE ASSERT FIRED THREE TIMES, AND THE THIRD WAS PURE ROUNDING.
        /// Bake 3 built the ring to spec and reported "0.30m of clearance where
        /// ArenaBoundaryContainmentSlack requires 0.30m" - the reserve made the clearance land
        /// EXACTLY on the requirement, so `68.6f - 1.82f - 0.48f - 66.0f = 0.2999...` lost a
        /// strict compare to float error. The lesson is not "add an epsilon" (that is fix 2, and
        /// it is necessary but not sufficient): **a design that aims at the limit will keep
        /// touching it.** The reserve now aims 0.05 m PAST the limit, so the assert is a real
        /// test with room on both sides rather than a coin flip on the last bit.
        /// </summary>
        private const float ArenaBoundaryContainmentHeadroom = 0.05f;

        /// <summary>Tolerance for the containment compares - float error must never be a FAIL.</summary>
        private const float ArenaBoundaryContainmentEpsilon = 0.001f;

        /// <summary>Ring root object name (mirrors the siege venue's "OuterBoundary_Ring").</summary>
        private const string ArenaBoundaryRootName = "ArenaBoundary_Ring";
        /// <summary>Root name used INSTEAD when containment fails - the scene carries the finding.</summary>
        private const string ArenaBoundaryUnsafeName = "ArenaBoundary_Ring_CONTAINMENT_FAIL";
        /// <summary>Per-piece name prefix - what the regression greps for in the baked scene.</summary>
        private const string ArenaBoundaryPieceLabel = "ArenaBoundary";

        /// <summary>Centre-to-centre stride as a fraction of the smallest piece footprint (&lt;1 = pieces overlap).</summary>
        private const float ArenaBoundaryOverlap = 0.7f;
        /// <summary>CAP on the symmetric radial scatter; the band fit lowers it as needed.</summary>
        private const float ArenaBoundaryRadialJitterCap = 1.2f;
        /// <summary>
        /// Boulder scale CEILING, not a promise. The band fit shrinks it to whatever the
        /// measured mesh needs - on the 09-10 palette (3.38 m wide) the applied scale lands
        /// near 1.3, which is still a ~4.4 m boulder.
        /// </summary>
        private const float ArenaBoundaryScaleMin = 2.2f;
        private const float ArenaBoundaryScaleMax = 3.4f;
        /// <summary>Cost ceiling per side. When it binds the stride widens and the build log says so.</summary>
        private const int ArenaBoundaryMaxPerSide = 100;

        /// <summary>Widest a single wall panel may be stretched before the builder adds panels instead.</summary>
        private const float MaxSegmentWidth = 3.0f;
        /// <summary>Narrowest a panel may be squeezed (keeps the gate opening walkable).</summary>
        private const float MinSegmentWidth = 1.2f;

        /// <summary>Wall panel height / thickness (the owner's box; X is the tiling axis).</summary>
        private static readonly Vector3 SegSize = new Vector3(1.5f, 3.0f, 1.5f);

        // == Tower balance =====================================================

        /// <summary>A turret never reaches further than this fraction of the arena radius (a lane must exist).</summary>
        private const float TowerRangeFractionOfRadius = 0.55f;
        private const float TowerRangeFloor = 12f;
        /// <summary>Fraction of the turrets placed on the wall line; the rest guard the spire.</summary>
        private const float OuterBandShare = 0.6f;
        /// <summary>Inner (spire-guard) band radius as a fraction of the arena radius.</summary>
        private const float InnerBandFraction = 0.35f;

        /// <summary>Hero base max HP (HeroHealth._maxHp) - the floor case for the time-to-death report.</summary>
        private const float HeroBaseHp = 100f;
        /// <summary>A fully geared/talented hero (HeroHealth.cs:211 worked example) - the ceiling case.</summary>
        private const float HeroGearedHp = 195f;

        // == Art ===============================================================

        /// <summary>Fallback watchtower art when the catalog row has none (or the pack is not imported).</summary>
        private const string FallbackTowerPath = "Structures/Tower_Medieval_Wood";
        /// <summary>Fallback turret types when towers[] is empty.</summary>
        private const string DefaultArcherTowerId = "tower_ground_archer";
        private const string DefaultMageTowerId = "tower_arcane_spire";

        // -- SPIRE MONUMENT FIT (WO-1617). These were three bare literals inside PlaceSpire
        //    ("targetHeight * 1.6f, 8f, 18f"). They are TUNABLES now, with TODAY'S VALUES as the
        //    defaults - WO-1617 changed WHO the fit applies to, never the numbers.
        //
        //    WO-1619 STEP 2 UPDATE: these three are STILL unmoved, but the "must land on the
        //    identical 8.0 m" note that used to sit here is RETIRED, and it was never a target -
        //    it was the SATURATION. The instrumented bake (Builds/wave2-bake, 2026-09-10) printed,
        //    identically for all three baked configs (raider_camp_small / fortified_garrison /
        //    mage_enclave):
        //      "SPIRE FIT ... rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m
        //       wantedFactor=14.366 appliedFactor=8.000 saturatedAt=UPPER achieved=8.02m
        //       (56% of target) - SATURATED"
        //    The monument clamp asked for 14.40 m every time; ScaleToHeight's factor ceiling
        //    handed back 8.02 m. The arcane spire now lands at its 14.40 m target - that is the
        //    change, and the monument tunables below did not move to get it.
        internal const float SpireMonumentMultiplier = 1.6f;
        internal const float SpireMonumentMinHeight = 8f;
        internal const float SpireMonumentMaxHeight = 18f;

        // -- SPIRE FIT FACTOR BOUNDS (WO-1619 step 2). These were two bare literals inside
        //    ScaleToHeight's Mathf.Clamp ("0.2f, 8f"), and the upper one was the whole defect:
        //    it, not the monument clamp above, decided how tall a raid spire actually rendered.
        //
        //    THE RULING (WO-1619 sec.3): the factor bound is NOT the authority for a monument
        //    fit. SpireMonumentMinHeight / SpireMonumentMaxHeight own "how tall should this be",
        //    and the achieved height can never exceed SpireMonumentMaxHeight no matter how large
        //    this ceiling is, because the TARGET is already clamped there before the fit runs.
        //    So this bound's only remaining job is refusing to magnify degenerate art - which is
        //    a different concern from monument height, and must stop silently capping it.
        //
        //    THE VALUE, derived from measured numbers only (CLAUDE.md sec.11B - no invented
        //    "smallest sane art" figure): the tallest target the monument path can ever ask for
        //    is SpireMonumentMaxHeight = 18 m; the measured rendered height of the shipped spire
        //    art is 1.002 m (Builds/wave2-bake, 2026-09-10), so 18 / 1.002 = 17.96 is the largest
        //    factor today's art can legitimately need. 24 clears that with margin for shorter
        //    art, and is NON-BINDING across the whole authored [8 m, 18 m] target range for the
        //    art we actually ship. The exact margin is a lead/owner-tunable choice, not a
        //    measurement - it is a named tunable precisely so retuning it is one edit here.
        //
        //    THE LOWER BOUND DOES NOT MOVE (WO-1619 sec.5 pin): 0.2f is doing real work against
        //    oversized art and no measurement licensed touching it. It is named here only so
        //    that zero magic literals survive in the fit path.
        internal const float SpireFitFactorMin = 0.2f;
        internal const float SpireFitFactorMax = 24f;

        private const string RootName = "RaidBase_IronBastion";
        private const string DefaultScene = "Assets/Scenes/MainCastle_Hall.unity";
        private const string HeroStartName = "HeroStartPoint_PlayerSpawn";

        // == STAGING (WO-1520) =================================================
        //
        // OWNER RULING 2026-09-06, verbatim: "battle for raids should start in some staging
        // area outside of the attack range of everything so time starts on first engage. as
        // soon as you spawn in you start dying without having even a second to deploy some
        // troops."
        //
        // MEASURED BEFORE (raid-no-abilities-2026-09-06.log, 12:59:47): the carried hero was
        // seated at [Flow:Hero] "re-homed carried hero (0.00, 0.08, -39.00) (seat=baked
        // marker)" - i.e. HeroStartName at -(radius + 8) = -39 m on raider_camp_small
        // (baseRadius 31). The outer turret band sits at radius - max(2.5, segW) ~ 28.4 m
        // with a range cap of radius * 0.55 = 17.05 m, so its reach ends at ~45.4 m from the
        // centre. -39 m is 6.4 m INSIDE that reach. The hero was under fire on frame one, and
        // died at 45 s of a 180 s raid.
        //
        // SO: this builder now authors a SEPARATE staging marker whose distance is COMPUTED
        // from the same numbers it used to place the threats, never picked by eye:
        //   towerReach    = max over placed turrets of (|pos| + Range)      (PlaceTowers)
        //   defenderReach = garrison ring + the AwarenessSensor radius      (see below)
        //   staging       = max(towerReach, defenderReach) + StagingMargin
        // and ASSERTS it. If the arena is too wide for the plane to hold a safe point, that
        // is a FINDING about the layout (Debug.LogError, and the regression stays RED) - the
        // assert is never softened. WO-1520 sec.3.
        private const string StagingPointName = "RaidStagingPoint";

        /// <summary>
        /// Clear air between the staging marker and the furthest reach of ANY defender or
        /// turret. Not a grace period (the owner explicitly refused a timer, WO-1520 sec.3) -
        /// it is the slack that keeps a marker safe after a navmesh snap moves it a metre or
        /// two, and after a defender takes one step outward.
        /// </summary>
        private const float StagingMargin = 6f;

        /// <summary>
        /// Keep the staging marker this far inside the RaidGround plane edge so it lands on
        /// baked navmesh (RaidNavBake drops a 140 m plane; the very edge is not walkable).
        /// </summary>
        private const float StagingPlaneEdgeMargin = 4f;

        /// <summary>
        /// The defender perception radius the staging distance is computed against. MIRRORS
        /// <c>AwarenessSensor._perceptionRadius</c> (Assets/_Modules/Village/Enemies/Perception/
        /// AwarenessSensor.cs:82, default 20f) - a serialized private field an Editor script
        /// cannot read without reflection. It is DUPLICATED STATE on purpose and therefore
        /// PINNED: RaidStagingMarkerRegression re-reads that field's default out of the sensor
        /// source and FAILS if the two ever disagree. Never bump one without the other.
        /// </summary>
        public const float DefenderPerceptionRadius = 20f;

        // Side index -> cardinal name, by the 90-degree-around-origin rot index (0=S,1=E,2=N,3=W).
        private static readonly string[] SideName = { "S", "E", "N", "W" };

        // Legacy Iron-Bastion flagship tunables (menu item preserved verbatim).
        private const int OuterSlotsPerSide = 15;
        private const WallTier OuterTier = WallTier.Iron;
        private const int InnerSlotsPerSide = 7;
        private const WallTier InnerTier = WallTier.ReinforcedSteel;

        // =====================================================================
        //  Tier table - keyed off the config's authored `difficulty` string.
        // =====================================================================

        /// <summary>
        /// Per-difficulty objective + survivability budget. `Footprint` is the share of
        /// the 140 m plane the arena should occupy and is used ONLY to sanity-check the
        /// authored baseRadius (radius = MapHalfExtent * sqrt(footprint)); the authored
        /// value is what actually builds, so the data stays in charge.
        /// </summary>
        private struct RaidTier
        {
            public string Name;
            public float Footprint;      // share of the map AREA
            public float SpireHp;        // objective HP
            public float TowerDpsBudget; // hard ceiling on concurrent tower DPS anywhere in the arena
        }

        private static RaidTier TierFor(string difficulty)
        {
            string d = (difficulty ?? "").Trim();
            if (string.Equals(d, "Extreme", StringComparison.OrdinalIgnoreCase))
                return new RaidTier { Name = "Extreme", Footprint = 0.60f, SpireHp = 3500f, TowerDpsBudget = 20f };
            if (string.Equals(d, "Hard", StringComparison.OrdinalIgnoreCase))
                return new RaidTier { Name = "Hard", Footprint = 0.50f, SpireHp = 2200f, TowerDpsBudget = 16f };
            return new RaidTier { Name = "Regular", Footprint = 0.20f, SpireHp = 1200f, TowerDpsBudget = 12f };
        }

        // == Entry points ======================================================

        [MenuItem("Defenders/Walls/Build Raid Base - Iron Bastion")]
        public static void BuildInOpenScene()
        {
            var root = Build();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[RaidBaseGenerator] built '{RootName}' in the open scene " +
                      $"(outer {OuterTier} / keep {InnerTier} - Ctrl+S to keep).");
        }

        // Batch: open <scenePath>, build the bastion under its own root, save.
        public static void BuildIntoScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) scenePath = DefaultScene;
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Build();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[RaidBaseGenerator] built + saved '{RootName}' into {scenePath} " +
                      $"(outer {OuterTier} / keep {InnerTier}).");
        }

        // Batch: build into a FRESH empty scene (camera + light) so the bastion stands ALONE.
        public static void BuildToNewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Build();
            const string path = "Assets/Scenes/RaidBase_IronBastion.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[RaidBaseGenerator] built + saved '{RootName}' into NEW scene {path} " +
                      $"(outer {OuterTier} / keep {InnerTier}).");
        }

        // == Config-driven entry points ========================================

        /// <summary>The three flagship raid levels, in difficulty order.</summary>
        private static readonly string[] RaidConfigIds =
            { "raider_camp_small", "fortified_garrison", "mage_enclave" };

        [MenuItem("Defenders/Walls/Build All Raid Scenes (config-driven)")]
        public static void BuildAllRaidScenes()
        {
            SceneConfigCatalog.Invalidate();   // pick up any fresh JSON edit
            InvalidateStructureCatalog();
            foreach (var id in RaidConfigIds) BuildSceneFor(id);
            Debug.Log($"[RaidBaseGenerator] baked {RaidConfigIds.Length} raid scene(s) from scene-configs.json. " +
                      "NEXT (required): DeNelle.Editor.RaidNavBake.BakeAll - it drops the RaidGround plane and " +
                      "bakes the legacy NavMesh. Without it the hero and every agent have nothing to walk on.");
        }

        /// <summary>
        /// Build ONE config into its own fresh scene (camera + light), saved to
        /// RaidBase_&lt;id&gt;.unity. Deterministic: the same config id always produces
        /// the same layout (the only stochastic input is a stable hash of the id).
        /// </summary>
        public static void BuildSceneFor(string configId)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject($"RaidBase_{configId}");
            root.transform.position = Vector3.zero;
            BuildFromConfig(configId, root.transform);

            string path = $"Assets/Scenes/RaidBase_{configId}.unity";
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[RaidBaseGenerator] built + saved raid '{configId}' into NEW scene {path}.");
        }

        /// <summary>
        /// Build a complete raid level from its scene-config under parentRoot. Idempotent
        /// per-config. Null-guards a missing config (LogWarning, no crash).
        /// </summary>
        public static void BuildFromConfig(string configId, Transform parentRoot)
        {
            var def = SceneConfigCatalog.Find(configId);
            if (def == null)
            {
                Debug.LogWarning($"[RaidBaseGenerator] no scene-config '{configId}' - nothing built.");
                return;
            }

            string rootName = $"RaidBase_{configId}";
            var prior = parentRoot != null ? parentRoot.Find(rootName) : null;
            if (prior != null) Object.DestroyImmediate(prior.gameObject);
            var priorGlobal = GameObject.Find(rootName);
            if (priorGlobal != null && (parentRoot == null || priorGlobal.transform.parent != parentRoot))
                Object.DestroyImmediate(priorGlobal);

            var root = new GameObject(rootName);
            if (parentRoot != null) root.transform.SetParent(parentRoot, false);
            root.transform.localPosition = Vector3.zero;

            BuildConfigLayout(def, root.transform);

            // GARRISON WIRING - the runtime spawner that fills this baked base with its
            // config's garrison (boss + composition, player-level-scaled). It carries the
            // STORED config id (the baked scene is RaidBase_<id>, which will not match the
            // config's sceneName, so SceneOwnership cannot resolve it by name).
            var spawner = root.AddComponent<RaidGarrisonSpawner>();
            spawner.SetConfigId(configId);
        }

        // =====================================================================
        //  The data-driven layout.
        // =====================================================================
        private static void BuildConfigLayout(SceneConfigDef def, Transform root)
        {
            RaidTier tier = TierFor(def.difficulty);
            int seed = StableHash(def.id);

            // -- ARENA RADIUS. The authored baseRadius is the SIZE (owner concept step 1).
            //    Clamped so a fat-fingered value can never spill off the RaidGround plane.
            float suggested = MapHalfExtent * Mathf.Sqrt(Mathf.Clamp01(tier.Footprint));
            float radius = def.baseRadius > 1f ? def.baseRadius : suggested;
            float maxRadius = MapHalfExtent * 0.9f;
            if (radius > maxRadius)
            {
                Debug.LogWarning($"[RaidBaseGenerator] '{def.id}' baseRadius {def.baseRadius:F1}m exceeds the " +
                                 $"RaidGround half-extent budget ({maxRadius:F1}m) - clamped. Raise " +
                                 "RaidNavBake.GroundScale (and MapHalfExtent here) if a bigger arena is wanted.");
                radius = maxRadius;
            }
            radius = Mathf.Max(10f, radius);

            float footprintPct = (radius * radius) / (MapHalfExtent * MapHalfExtent) * 100f;

            // -- OUTER perimeter. entranceCount: 1 = south only, 2 = south + north.
            WallTier outerTier = ParseTier(def.wallTier, WallTier.Wood);
            bool twoGates = def.entranceCount >= 2;
            var outerGates = new bool[4] { true, false, twoGates, false };
            var outer = BuildRing(root, radius, Mathf.Max(3, def.wallSegmentsPerSide), outerTier,
                                  outerGates, "Outer");

            // -- INNER keep ring(s) (interiorWallLayers) - the kill-zone. Each layer sits
            //    at 45% of the ring outside it, with a single NORTH gate opposite the outer
            //    SOUTH gate, so the player crosses the courtyard under fire (the funnel).
            float innermost = outer.HalfExtent;
            int innerLayers = Mathf.Max(0, def.interiorWallLayers);
            for (int layer = 0; layer < innerLayers; layer++)
            {
                float keepRadius = Mathf.Max(8f, innermost * 0.45f);
                var innerGates = new bool[4] { false, false, true, false };
                var keep = BuildRing(root, keepRadius, Mathf.Max(3, def.wallSegmentsPerSide - 2),
                                     WallTier.ReinforcedSteel, innerGates, $"Keep{layer + 1}");
                innermost = keep.HalfExtent;
            }

            // -- THE OBJECTIVE. Central spire at the origin; destroying it wins the raid.
            var spire = PlaceSpire(root, def, tier);

            // -- TOWERS. Two bands so neither the approach nor the objective is safe.
            var towerReport = PlaceTowers(root, def, tier, radius, outer.SegmentWidth, seed);

            // -- BOSS marker. Pushed clear of the spire footprint so the boss does not
            //    spawn inside it (RaidGarrisonSpawner navmesh-snaps from this point).
            float bossOffset = Mathf.Max(4f, innermost * 0.35f);
            var boss = new GameObject("BossSpawn");
            boss.transform.SetParent(root, false);
            boss.transform.localPosition = new Vector3(0f, 0f, -bossOffset);

            // -- HERO ENTRY. The raid scenes carried NO HeroStartPoint_PlayerSpawn, so
            //    HeroControlEnsurer.SpawnEmergencyHero fell back to the CASTLE courtyard
            //    spot (6, liftY+1, 4) - which with a real arena is inside the walls, on top
            //    of the objective. Author the marker outside the south gate instead.
            var heroStart = new GameObject(HeroStartName);
            heroStart.transform.SetParent(root, false);
            heroStart.transform.localPosition = new Vector3(0f, 0f, -(radius + 8f));

            // -- STAGING (WO-1520). The point the hero seats at and troops deploy from,
            //    PROVEN outside every turret's reach and every defender's awareness radius.
            var staging = PlaceStagingMarker(root, def, radius, towerReport.MaxReach);

            // -- ARENA EXTERIOR BOUNDARY (WO-1632). Built LAST so it is measured against the
            //    markers that already exist, and asserted to contain both of them.
            var boundary = BuildArenaBoundary(root, seed);
            if (!AssertBoundaryContainsStaging(def != null ? def.id : root.name, staging.Position,
                                               heroStart.transform.localPosition, boundary))
                MarkBoundaryUnsafe(root);

            RaidBaseDresser.Dress(def, root, new RaidBaseDresser.LayoutContext
            {
                Radius = radius,
                Innermost = innermost,
                TwoGates = twoGates,
                InnerLayers = innerLayers,
                GateWidth = Mathf.Max(RaidBaseDresser.MinGateWidth, outer.GateWidth),
                SegmentWidth = outer.SegmentWidth,
            });

            Debug.Log(
                $"[RaidBaseGenerator] '{root.name}' ({def.displayName}, {tier.Name}) BUILT: " +
                $"radius {radius:F1}m (~{footprintPct:F0}% of the {MapHalfExtent * 2f:F0}m plane), " +
                $"outer {outerTier} {outer.SlotsPerSide}/side @ {outer.SegmentWidth:F2}m panels " +
                $"gates={(twoGates ? "S,N" : "S")}, {innerLayers} keep layer(s) (innermost +/-{innermost:F1}m), " +
                $"SPIRE {(spire != null ? spire.MaxHp.ToString("F0") : "MISSING")} HP at centre = THE WIN CONDITION, " +
                $"{towerReport.Placed} turret(s) [{towerReport.TypeSummary}], " +
                $"worst-case {towerReport.WorstConcurrent} concurrent -> {towerReport.WorstDps:F1} DPS " +
                $"(budget {tier.TowerDpsBudget:F0}, damage x{towerReport.DamageScale:F2}); " +
                $"hero time-to-death {(towerReport.WorstDps > 0.01f ? (HeroBaseHp / towerReport.WorstDps).ToString("F1") : "inf")}s " +
                $"@{HeroBaseHp:F0}HP / {(towerReport.WorstDps > 0.01f ? (HeroGearedHp / towerReport.WorstDps).ToString("F1") : "inf")}s " +
                $"@{HeroGearedHp:F0}HP. BossSpawn @ -{bossOffset:F1}m, hero entry @ -{radius + 8f:F1}m, " +
                $"STAGING @ {staging.Position} ({staging.Distance:F1}m out, {staging.Clearance:F1}m of clear air, " +
                $"{(staging.Safe ? "SAFE" : "*** UNSAFE - see the error above ***")}), " +
                $"ARENA BOUNDARY +/-{ArenaBoundaryHalfExtent:F1}m: {boundary.Placed} landscape piece(s), " +
                $"{boundary.PerSide}/side @ {boundary.Stride:F2}m.");
        }

        // =====================================================================
        //  STAGING MARKER (WO-1520) - a PLACE outside everything's reach, not a timer.
        // =====================================================================

        /// <summary>What <see cref="PlaceStagingMarker"/> resolved, for the build log + the report.</summary>
        private struct StagingReport
        {
            public Vector3 Position;
            /// <summary>Distance from the arena centre to the marker.</summary>
            public float Distance;
            /// <summary>Required distance = max(tower reach, defender reach) + <see cref="StagingMargin"/>.</summary>
            public float Required;
            /// <summary>Metres between the marker and the nearest threat's outer edge (negative = inside it).</summary>
            public float Clearance;
            public float TowerReach;
            public float DefenderReach;
            public bool Safe;
        }

        /// <summary>
        /// Author the staging marker at a point COMPUTED to sit outside every threat in this
        /// base, and ASSERT it.
        ///
        /// <para>The two reaches:
        /// <list type="bullet">
        /// <item>TURRETS - <paramref name="towerMaxReach"/> is measured in PlaceTowers as
        /// max(|position| + Range) over the turrets it actually placed, so it follows the
        /// structures-catalog ranges and the arena's own range cap rather than a magic number.</item>
        /// <item>DEFENDERS - RaidGarrisonSpawner seats its composition on a ring at
        /// <c>max(2, baseRadius * 0.5)</c> (RaidGarrisonSpawner.cs:168) and every spawn carries an
        /// AwarenessSensor with a <see cref="DefenderPerceptionRadius"/> scan, so the furthest a
        /// defender can NOTICE anything is ring + radius. The boss sits further in, at BossSpawn.</item>
        /// </list></para>
        ///
        /// <para>PLACEMENT: due south first (the outer ring's gate side, so the walk in is the
        /// intended approach). A square 140 m plane holds more distance on its diagonal than on
        /// its axis, so when the south axis cannot fit the required distance the marker moves to
        /// the south-west diagonal, which is still on the baked ground. If even that cannot fit,
        /// the marker is clamped to the furthest legal point and the shortfall is reported as a
        /// <see cref="Debug.LogError"/> - a finding about the LAYOUT (the arena is too wide for
        /// its plane), never a softened assert. WO-1520 sec.3.</para>
        /// </summary>
        private static StagingReport PlaceStagingMarker(Transform root, SceneConfigDef def,
                                                        float radius, float towerMaxReach)
        {
            // Defenders: the spawner's own ring formula, then their perception radius on top.
            float garrisonRing = Mathf.Max(2f, (def != null ? def.baseRadius : radius) * 0.5f);
            garrisonRing = Mathf.Max(garrisonRing, radius * 0.5f);
            float defenderReach = garrisonRing + DefenderPerceptionRadius;

            float threatReach = Mathf.Max(towerMaxReach, defenderReach);
            float required = threatReach + StagingMargin;

            float axisBudget = MapHalfExtent - StagingPlaneEdgeMargin;              // straight south
            float diagonalBudget = axisBudget * Mathf.Sqrt(2f);                     // south-west corner run

            Vector3 pos;
            float distance;
            if (required <= axisBudget)
            {
                distance = required;
                pos = new Vector3(0f, 0f, -distance);
            }
            else if (required <= diagonalBudget)
            {
                distance = required;
                float leg = distance / Mathf.Sqrt(2f);
                pos = new Vector3(-leg, 0f, -leg);
                Debug.LogWarning($"[RaidBaseGenerator] '{(def != null ? def.id : root.name)}' needs {required:F1}m of " +
                                 $"staging distance, which does not fit on the south axis ({axisBudget:F1}m budget) - " +
                                 $"the marker moved to the SOUTH-WEST diagonal at {pos}. The arena is wide enough that " +
                                 "the approach no longer lines up with the south gate; consider lowering baseRadius or " +
                                 "the turret range cap.");
            }
            else
            {
                distance = diagonalBudget;
                float leg = distance / Mathf.Sqrt(2f);
                pos = new Vector3(-leg, 0f, -leg);
                Debug.LogError($"[RaidBaseGenerator] STAGING ASSERT FAILED for '{(def != null ? def.id : root.name)}': " +
                               $"a safe staging point needs {required:F1}m from the arena centre " +
                               $"(turret reach {towerMaxReach:F1}m, defender reach {defenderReach:F1}m = ring " +
                               $"{garrisonRing:F1}m + awareness {DefenderPerceptionRadius:F1}m, margin {StagingMargin:F1}m) " +
                               $"but the {MapHalfExtent * 2f:F0}m RaidGround plane offers at most {diagonalBudget:F1}m. " +
                               "The marker is clamped to that furthest legal point and the player WILL spawn under fire. " +
                               "This is a finding about the BASE LAYOUT (baseRadius / turret range cap / RaidNavBake." +
                               "GroundScale), not a number to soften - WO-1520 sec.3.");
            }

            var prior = root.Find(StagingPointName);
            if (prior != null) Object.DestroyImmediate(prior.gameObject);

            var marker = new GameObject(StagingPointName);
            marker.transform.SetParent(root, false);
            marker.transform.localPosition = pos;
            // Face the arena centre so anything that reads the marker's rotation (camera, the
            // hero's landing pose) looks at the base rather than away from it.
            marker.transform.localRotation = Quaternion.LookRotation((Vector3.zero - pos).normalized, Vector3.up);

            return new StagingReport
            {
                Position = pos,
                Distance = distance,
                Required = required,
                Clearance = distance - threatReach,
                TowerReach = towerMaxReach,
                DefenderReach = defenderReach,
                Safe = distance >= threatReach,
            };
        }

        // =====================================================================
        //  THE SPIRE - the win condition. Art from the config's `centralBuilding`.
        // =====================================================================

        private static RaidSpire PlaceSpire(Transform root, SceneConfigDef def, RaidTier tier)
        {
            // WO-1617: the spire slot never carries siege art. ONE decider, shared with
            // PlaceTowerProp and the dresser - see ResolveSpireArtId / IsAuthoredSiegeMachine.
            string catalogId = ResolveSpireArtId(def.centralBuilding);
            var entry = FindStructure(catalogId);

            float targetHeight = 9f;
            if (entry != null && entry.repo != null && entry.repo.visualHeight > 0.5f)
                targetHeight = entry.repo.visualHeight;

            // A machine authored at its true size must not be magnified to monument height.
            // (Belt as well as braces: ResolveSpireArtId already keeps siege art out of this
            // slot, but if a future config or caller ever routes one here, the fit must still
            // leave it alone rather than repeat the 2.6 m -> 14.4 m Ballista blow-up.)
            bool authoredSiege = IsAuthoredSiegeMachine(catalogId);
            if (!authoredSiege)
                targetHeight = Mathf.Clamp(targetHeight * SpireMonumentMultiplier,
                                           SpireMonumentMinHeight, SpireMonumentMaxHeight);   // a monument, not a hut

            GameObject go = null;
            string prefabPath = entry != null ? entry.visualPrefabPath : null;
            if (!string.IsNullOrEmpty(prefabPath))
            {
                var prefab = RaidBaseDresser.LoadVisual(prefabPath);
                if (prefab == null) prefab = RaidBaseDresser.LoadVisual(catalogId);
                if (prefab != null)
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    if (go == null) go = Object.Instantiate(prefab);
                }
                else
                {
                    Debug.LogWarning($"[RaidBaseGenerator] spire art '{prefabPath}' " +
                                     $"(centralBuilding '{catalogId}') not found in StructureContent/KayKit/Synty. " +
                                     "Falling back to a URP-safe primitive obelisk.");
                }
            }
            else if (entry == null)
            {
                // ⛔ NO ROW AT ALL - a DIFFERENT failure from "row exists, path blank", and the two
                //    were one message until 2026-09-10 (WO-1619). That single line cost a
                //    diagnosis: the Forsaken Camp bake printed "has no visualPrefabPath in
                //    structures-catalog.json" for 'tower_ruined_watchtower' when the true state was
                //    that scene-configs.json had been updated and structures-catalog.json had NOT,
                //    so the row did not exist in either canonical twin. The message named the wrong
                //    half of the data and sent the hunt at LoadVisual's search order, which was
                //    working perfectly. A warning that cannot tell "absent" from "blank" is not
                //    instrumentation (CLAUDE.md sec.12).
                Debug.LogWarning($"[RaidBaseGenerator] centralBuilding '{catalogId}' has NO ROW in " +
                                 "structures-catalog.json (the id is not in the catalog at all - check BOTH " +
                                 "canonical twins, Assets/Resources/Data/Canonical/ and " +
                                 "Assets/StreamingAssets/Data/Canonical/; a scene-configs.json re-point whose " +
                                 "matching catalog row was never added lands exactly here) - falling back to a " +
                                 "URP-safe primitive obelisk.");
            }
            else
            {
                Debug.LogWarning($"[RaidBaseGenerator] centralBuilding '{catalogId}' HAS a structures-catalog row " +
                                 "but that row authors no visualPrefabPath - falling back to a URP-safe " +
                                 "primitive obelisk.");
            }

            if (go == null) go = BuildFallbackObelisk(targetHeight);

            go.name = RaidSpire.ObjectName;
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            // Stand it up BEFORE measuring height (a flat FBX would otherwise be "scaled to
            // height" on the wrong axis and become a pancake), then scale + ground-seat.
            //
            // WO-1617: an authored siege machine skips BOTH corrections. EnsureUpright's own
            // warning predicted this false positive and the bake log recorded it firing:
            //   "'spire art 'tower_siege_tower'' imported FLAT (h=2.6m vs 4.4m wide) - applied
            //    the -90 X FBX-flat correction" (Builds/raidbase-bake.log)
            // - a Ballista IS 2.6 m tall and 4.4 m wide, so the heuristic read correct art as a
            // fallen building, tipped it on its edge, and ScaleToHeight then magnified the wrong
            // axis to 14.4 m. The exemption PlaceTowerProp already had is now shared, not copied.
            // WO-1619 step 1: ONE line per spire, and it must name the scene it came from -
            // BuildAllRaidScenes bakes several configs in one run, and "SPIRE 'tower_arcane_spire'"
            // alone cannot be told apart between them in Builds/raidbase-bake.log.
            string fitLabel = $"config '{def.id}' spire '{catalogId}'";

            float built;
            if (authoredSiege)
            {
                built = MeasuredHeight(go);
                Debug.Log($"[RaidBaseGenerator] SPIRE FIT {fitLabel}: EXEMPT (authored siege machine) - " +
                          $"no upright correction and no fit applied; measured={built:F2}m against a " +
                          $"pre-clamp target of {targetHeight:F2}m. (WO-1619 step 1 instrumentation.)");
            }
            else
            {
                EnsureUpright(go, $"spire art '{catalogId}'");
                built = ScaleToHeight(go, targetHeight, fitLabel);
            }
            SeatOnGround(go);

            var spire = go.GetComponent<RaidSpire>();
            if (spire == null) spire = go.AddComponent<RaidSpire>();
            spire.Configure(def.id, catalogId, tier.SpireHp, built);

            Debug.Log($"[RaidBaseGenerator] SPIRE '{catalogId}' placed at centre: {tier.SpireHp:F0} HP, " +
                      $"{built:F1}m tall, art='{(string.IsNullOrEmpty(prefabPath) ? "<primitive>" : prefabPath)}'. " +
                      "It implements IDamageable (hero/troop seam) + IDamageableStructure (enemy seam).");
            return spire;
        }

        /// <summary>
        /// URP-safe fallback obelisk (never a default-material primitive: under URP that
        /// renders MAGENTA). Uses the committed MagentaGuard.BuildUrpLitMaterial path.
        /// </summary>
        private static GameObject BuildFallbackObelisk(float height)
        {
            var rootGo = new GameObject("SpireFallback");

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(rootGo.transform, false);
            shaft.transform.localScale = new Vector3(2.2f, height * 0.5f, 2.2f);  // cylinder is 2 units tall
            shaft.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            ApplyUrpMaterial(shaft, new Color(0.34f, 0.30f, 0.42f));

            var crown = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crown.name = "Crown";
            crown.transform.SetParent(rootGo.transform, false);
            crown.transform.localScale = Vector3.one * 2.6f;
            crown.transform.localPosition = new Vector3(0f, height, 0f);
            crown.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
            ApplyUrpMaterial(crown, new Color(0.62f, 0.36f, 0.78f));

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "Plinth";
            plinth.transform.SetParent(rootGo.transform, false);
            plinth.transform.localScale = new Vector3(5f, 1.2f, 5f);
            plinth.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            ApplyUrpMaterial(plinth, new Color(0.24f, 0.22f, 0.26f));

            return rootGo;
        }

        /// <summary>Assigns a URP/Lit material via the committed MagentaGuard path (never the default).</summary>
        private static void ApplyUrpMaterial(GameObject go, Color color)
        {
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) return;
            var m = MagentaGuard.BuildUrpLitMaterial(color);
            if (m != null) r.sharedMaterial = m;
        }

        // =====================================================================
        //  TOWERS - count from archer+mage, TYPE from towers[], stats from the
        //  structures-catalog row, damage scaled to the tier's DPS budget.
        // =====================================================================

        private struct TowerReport
        {
            public int Placed;
            public int WorstConcurrent;
            public float WorstDps;
            public float DamageScale;
            public string TypeSummary;
            /// <summary>
            /// Furthest metre from the arena centre that ANY placed turret can shoot:
            /// max over plans of (|Pos| + Range). WO-1520 computes the staging distance from
            /// this instead of guessing a number.
            /// </summary>
            public float MaxReach;
        }

        /// <summary>One turret the builder is about to place (position + resolved stats).</summary>
        private sealed class TowerPlan
        {
            public Vector3 Pos;
            public string CatalogId;
            public string PrefabPath;
            public float Range;
            public float FireRate;
            public float RawDamage;
            public bool CanHitAir;
            public DamageElement Element;
            public string Label;
        }

        private static TowerReport PlaceTowers(Transform root, SceneConfigDef def, RaidTier tier,
                                               float radius, float segWidth, int seed)
        {
            var report = new TowerReport { DamageScale = 1f, TypeSummary = "none" };

            int archers = Mathf.Max(0, def.archerTowerCount);
            int mages = Mathf.Max(0, def.mageTowerCount);
            int total = archers + mages;
            if (total <= 0)
            {
                Debug.LogWarning($"[RaidBaseGenerator] '{def.id}' authors 0 towers " +
                                 "(archerTowerCount + mageTowerCount) - the arena has no turrets.");
                return report;
            }

            // TYPE palette from the (previously dead) towers[] array, weighted by count.
            var archerTypes = ResolveTowerTypes(def, DefaultArcherTowerId);
            var mageTypes = ResolveTowerTypes(def, DefaultMageTowerId);

            // BAND SPLIT from the authored towerPlacementStyle:
            //   "Cardinal"        -> every turret on the wall line (the centre is a safe
            //                        pocket) - the easy, teachable shape.
            //   "OverlappingFire" -> ~60% on the wall line, the rest ringing the SPIRE, so
            //                        the objective itself is contested crossfire.
            bool overlapping = !string.Equals(def.towerPlacementStyle, "Cardinal",
                                              StringComparison.OrdinalIgnoreCase);
            float outerShare = overlapping ? OuterBandShare : 1f;

            // Two bands: the wall line (no safe approach) + a spire guard (no safe objective).
            int outerCount = Mathf.Clamp(Mathf.CeilToInt(total * outerShare), 1, total);
            int innerCount = total - outerCount;
            float outerBand = Mathf.Max(4f, radius - Mathf.Max(2.5f, segWidth));

            // Deterministic per-config phase so the three arenas do not read identically.
            float phase = (seed % 360) * Mathf.Deg2Rad;

            float rangeCap = Mathf.Max(TowerRangeFloor, radius * TowerRangeFractionOfRadius);
            var plans = new List<TowerPlan>(total);
            var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // PASS 1 - identity + combat stats. Positions come after, because the inner band
            // radius depends on the resolved RANGES (see below).
            for (int i = 0; i < total; i++)
            {
                bool isMage = i >= archers;
                int kindIndex = isMage ? (i - archers) : i;
                var palette = isMage ? mageTypes : archerTypes;
                string typeId = palette[kindIndex % palette.Count];

                var plan = new TowerPlan
                {
                    CatalogId = typeId,
                    Label = $"Watchtower_{(isMage ? "Mage" : "Archer")}_{kindIndex}",
                };
                ResolveTowerStats(plan, rangeCap);
                plans.Add(plan);

                typeCounts.TryGetValue(typeId, out int n);
                typeCounts[typeId] = n + 1;
            }

            // INNER BAND RADIUS - it MUST be inside its own turrets' reach, or the "spire
            // guard" band does not actually guard the spire. Caught by simulating the layout
            // before it shipped: at 0.35 * radius the Extreme enclave's guards stood 18.9 m
            // out with a 16 m reach, so the objective had ZERO tower coverage - the exact
            // opposite of the intent. Take the tighter of the fraction and 75% of the
            // shortest inner-band reach.
            float innerBand = Mathf.Max(3f, radius * InnerBandFraction);
            if (innerCount > 0)
            {
                float minInnerRange = float.MaxValue;
                for (int i = outerCount; i < plans.Count; i++)
                    minInnerRange = Mathf.Min(minInnerRange, plans[i].Range);
                if (minInnerRange < float.MaxValue)
                    innerBand = Mathf.Max(3f, Mathf.Min(innerBand, minInnerRange * 0.75f));
            }

            // PASS 2 - positions.
            for (int i = 0; i < plans.Count; i++)
            {
                bool onOuter = i < outerCount;
                int bandIndex = onOuter ? i : (i - outerCount);
                int bandTotal = onOuter ? outerCount : Mathf.Max(1, innerCount);
                float band = onOuter ? outerBand : innerBand;
                // The inner band is offset half a step so it never lines up behind the outer one.
                float ang = phase + (bandIndex / (float)bandTotal) * Mathf.PI * 2f
                                  + (onOuter ? 0f : Mathf.PI / bandTotal);
                plans[i].Pos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * band;
            }

            // ---- BALANCE PASS ------------------------------------------------
            // Find the single most-covered point in (and just outside) the arena and
            // scale every turret's damage so THAT point can never exceed the tier's DPS
            // budget. k is clamped to <= 1 so no turret is ever made stronger than its
            // catalog row authors. This is the "limit how many can engage at once"
            // lever, enforced as an arena-wide ceiling instead of a per-tower guess.
            int worstConcurrent;
            float worstRawDps = WorstCaseDps(plans, radius, out worstConcurrent);
            float k = (worstRawDps > 0.01f && worstRawDps > tier.TowerDpsBudget)
                ? tier.TowerDpsBudget / worstRawDps
                : 1f;

            int placed = 0;
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                plan.RawDamage = Mathf.Max(1f, plan.RawDamage * k);
                var go = PlaceTowerProp(root, plan);
                if (go != null) { ArmTower(go, plan); placed++; }
            }

            var summary = new System.Text.StringBuilder();
            foreach (var kv in typeCounts)
            {
                if (summary.Length > 0) summary.Append(", ");
                summary.Append(kv.Value).Append('x').Append(kv.Key);
            }

            // WO-1520: the furthest point any turret can reach, measured off the SAME plans
            // that were just placed (position + resolved range) - the input to the staging
            // distance. Computed here because this is the only place both are known.
            float maxReach = 0f;
            for (int i = 0; i < plans.Count; i++)
            {
                float reach = new Vector2(plans[i].Pos.x, plans[i].Pos.z).magnitude + plans[i].Range;
                if (reach > maxReach) maxReach = reach;
            }
            report.MaxReach = maxReach;

            report.Placed = placed;
            report.WorstConcurrent = worstConcurrent;
            report.WorstDps = worstRawDps * k;
            report.DamageScale = k;
            report.TypeSummary = summary.Length > 0 ? summary.ToString() : "none";

            Debug.Log($"[RaidBaseGenerator] turrets for '{def.id}': {placed}/{total} placed " +
                      $"({outerCount} on the wall line @ {outerBand:F1}m, {innerCount} guarding the spire " +
                      $"@ {innerBand:F1}m, style={(overlapping ? "OverlappingFire" : "Cardinal")}), " +
                      $"range cap {rangeCap:F1}m, types [{report.TypeSummary}]. " +
                      $"Worst point in the arena is covered by {worstConcurrent} turret(s) = " +
                      $"{worstRawDps:F1} raw DPS -> x{k:F2} -> {report.WorstDps:F1} DPS (budget {tier.TowerDpsBudget:F0}).");
            return report;
        }

        /// <summary>
        /// The type palette for a turret slot, expanded from the config's (previously
        /// unread) <c>towers[]</c> array and weighted by each entry's count. Falls back to
        /// a single default id when towers[] is absent/empty.
        /// </summary>
        private static List<string> ResolveTowerTypes(SceneConfigDef def, string fallbackId)
        {
            var list = new List<string>();
            if (def.towers != null)
            {
                for (int i = 0; i < def.towers.Count; i++)
                {
                    var t = def.towers[i];
                    if (t == null || string.IsNullOrEmpty(t.type)) continue;
                    int n = Mathf.Clamp(t.count, 1, 16);
                    for (int c = 0; c < n; c++) list.Add(t.type);
                }
            }
            if (list.Count == 0) list.Add(fallbackId);
            return list;
        }

        /// <summary>
        /// Fill a plan's combat stats from its structures-catalog row (range / damage /
        /// fireRate / canHitAir / element / art). Range is capped so a turret can never
        /// blanket the arena. A missing row logs once and takes sane defaults.
        /// </summary>
        private static void ResolveTowerStats(TowerPlan plan, float rangeCap)
        {
            var entry = FindStructure(plan.CatalogId);
            var repo = entry != null ? entry.repo : null;

            float range = repo != null && repo.range > 0.5f ? repo.range : 18f;
            plan.Range = Mathf.Clamp(range, TowerRangeFloor, rangeCap);
            plan.FireRate = repo != null && repo.fireRate > 0.01f ? repo.fireRate : 0.8f;
            plan.RawDamage = repo != null && repo.damage > 0.01f ? repo.damage : 10f;
            plan.CanHitAir = true;   // the party is ground; keep the turret able to reach it
            plan.Element = ParseElement(repo != null ? repo.element : null);
            plan.PrefabPath = entry != null && !string.IsNullOrEmpty(entry.visualPrefabPath)
                ? entry.visualPrefabPath : FallbackTowerPath;

            if (entry == null)
                Debug.LogWarning($"[RaidBaseGenerator] tower type '{plan.CatalogId}' is not in " +
                                 "structures-catalog.json - using default turret stats + fallback art.");
        }

        /// <summary>
        /// Sample the arena floor and return the highest total DPS any single point can be
        /// exposed to, plus how many turrets reach it. Deterministic and cheap; this is the
        /// number the balance ceiling is enforced against, so the oracle can recompute it.
        /// </summary>
        private static float WorstCaseDps(List<TowerPlan> plans, float radius, out int worstConcurrent)
        {
            worstConcurrent = 0;
            float worst = 0f;
            // Sample the whole arena plus an 8 m approach apron, at ~1.5 m resolution.
            float extent = radius + 8f;
            float step = Mathf.Max(1.25f, extent / 40f);
            for (float x = -extent; x <= extent + 0.001f; x += step)
            {
                for (float z = -extent; z <= extent + 0.001f; z += step)
                {
                    var p = new Vector3(x, 0f, z);
                    float dps = 0f;
                    int n = 0;
                    for (int i = 0; i < plans.Count; i++)
                    {
                        var t = plans[i];
                        Vector3 d = t.Pos - p; d.y = 0f;
                        if (d.sqrMagnitude > t.Range * t.Range) continue;
                        dps += t.RawDamage * t.FireRate;
                        n++;
                    }
                    if (dps > worst) { worst = dps; worstConcurrent = n; }
                }
            }
            return worst;
        }

        /// <summary>
        /// Place the turret prop. The name keeps the "Watchtower" token so
        /// GarrisonTurretArmer still recognises it (it will SKIP this one - the tower
        /// already carries a DefenseTower - which is exactly the intent: the builder's
        /// per-type stats win over the spawner's uniform ones).
        /// </summary>
        private static GameObject PlaceTowerProp(Transform parent, TowerPlan plan)
        {
            var prefab = RaidBaseDresser.LoadVisual(plan.PrefabPath);
            if (prefab == null && plan.PrefabPath != FallbackTowerPath)
            {
                Debug.LogWarning($"[RaidBaseGenerator] turret art '{plan.PrefabPath}' " +
                                 $"('{plan.CatalogId}') not found - trying StructureContent watchtower.");
                prefab = RaidBaseDresser.LoadVisual(FallbackTowerPath);
            }

            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (go == null) go = Object.Instantiate(prefab);
            }
            else
            {
                Debug.LogWarning($"[RaidBaseGenerator] no turret art at all (StructureContent/{FallbackTowerPath} " +
                                 $"missing too) - building a URP-safe primitive turret for '{plan.Label}'.");
                go = BuildFallbackTurret();
                MagentaGuard.ProtectPrimitiveArt(go, "RaidBaseGenerator.PlaceTowerProp");
            }

            go.name = plan.Label;
            go.transform.SetParent(parent, false);
            var outward = plan.Pos.sqrMagnitude > 0.001f ? plan.Pos.normalized : Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.z), Vector3.up);
            go.transform.position = plan.Pos;
            // Catapults and siege towers are intentionally low, wide machines. The generic
            // flat-FBX heuristic sees that silhouette as a fallen building and tips it onto
            // its side, which is exactly the raid F8 report. Their catalog art is authored
            // upright, so preserve it and reserve auto-uprighting for architectural towers.
            if (!IsAuthoredSiegeMachine(plan.CatalogId))
                EnsureUpright(go, $"turret art '{plan.CatalogId}' ({plan.Label})");
            SeatOnGround(go);
            return go;
        }

        /// <summary>
        /// THE ONE DECIDER for "is this catalog art an authored siege machine?" (WO-1617).
        /// Catapults and siege towers are low, wide machines authored at their true size
        /// (the height cadence gives siege 0.75 - KEY_FACTS "ONE HEIGHT CADENCE"), so they must
        /// never be auto-uprighted by the flat-FBX heuristic nor scaled to monument height.
        ///
        /// EVERY placer consults THIS method - <see cref="PlaceTowerProp"/>, <see cref="PlaceSpire"/>
        /// and (via <see cref="ResolveSpireArtId"/>) RaidBaseDresser.MapCatalogArt. It is
        /// deliberately `internal` rather than `private` so the dresser reaches the SAME predicate
        /// instead of copying the two ids: a second copy is how PlaceSpire came to disagree with
        /// PlaceTowerProp in the first place. DO NOT fork it, do not inline the ids elsewhere.
        /// </summary>
        internal static bool IsAuthoredSiegeMachine(string catalogId)
        {
            return string.Equals(catalogId, "tower_catapult", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(catalogId, "tower_siege_tower", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolve the art id the SPIRE SLOT should carry (WO-1617). The spire is the camp's
        /// architectural centrepiece and the raid's win condition; a siege machine is not
        /// architecture. `raider_camp_small` authors the siege-tower id as its `centralBuilding`
        /// (Assets/Resources/Data/Canonical/scene-configs.json:76), which is how the Easy camp's
        /// centrepiece became a Ballista tipped onto its edge and blown up to 14.4 m.
        ///
        /// This routes a siege id to the module's EXISTING default spire art
        /// (<see cref="DefaultMageTowerId"/>) and says so loudly. That is the codebase's own
        /// fallback, NOT a creative pick - which spire art the Forsaken Camp should actually
        /// carry is the owner's call (WO-1617 sec.7 / WO-1607 sec.0). When she rules, change the
        /// JSON's `centralBuilding` and this warning stops firing on its own.
        ///
        /// Both the generator and the dresser call this, so the model the generator MEASURES for
        /// the height fit and the model the dresser INSTANTIATES are always the same one - the
        /// dresser's ReplaceChildrenWith swaps the mesh but inherits the host's fitted scale.
        /// </summary>
        internal static string ResolveSpireArtId(string centralBuilding)
        {
            if (string.IsNullOrEmpty(centralBuilding)) return DefaultMageTowerId;
            if (!IsAuthoredSiegeMachine(centralBuilding)) return centralBuilding;

            Debug.LogWarning($"[RaidBaseGenerator] centralBuilding '{centralBuilding}' is an authored SIEGE MACHINE, " +
                             $"not architecture - it cannot be the spire (WO-1617). Substituting the default spire art " +
                             $"'{DefaultMageTowerId}'. Fix the config's centralBuilding in scene-configs.json once the " +
                             "owner picks the camp's centrepiece; this warning stops when she does.");
            return DefaultMageTowerId;
        }

        /// <summary>
        /// The catalog's OWN art path for a structure id, or null when the id has no row (or the
        /// row authors no path). WO-1619, 2026-09-10.
        ///
        /// `internal` for the same reason <see cref="IsAuthoredSiegeMachine"/> is - so
        /// RaidBaseDresser.MapCatalogArt can reach the SAME source of truth the generator itself
        /// measures (<see cref="PlaceSpire"/> reads `entry.visualPrefabPath`) instead of keeping a
        /// second, hand-maintained id-to-token table. That table answered only four substrings, so
        /// the owner's 2026-09-10 ruled spire art (`tower_ruined_watchtower`) fell through it to
        /// the raw id, failed to load, and was silently replaced with ArcaneSpire_1 - the dresser
        /// undoing the generator's own fitted model. One owner for "what art does this id carry",
        /// and it is the catalog.
        ///
        /// The value is returned VERBATIM, including its "Structures/" prefix:
        /// RaidBaseDresser.LoadVisual strips that prefix itself before searching the KayKit /
        /// Synty / StructureContent roots, so the same string serves the editor bake and the
        /// runtime StructureAssetLoader address without a second spelling.
        /// </summary>
        internal static string CatalogArtPath(string catalogId)
        {
            var entry = FindStructure(catalogId);
            return entry != null ? entry.visualPrefabPath : null;
        }

        /// <summary>URP-safe primitive turret (never a default-material primitive).</summary>
        private static GameObject BuildFallbackTurret()
        {
            var rootGo = new GameObject("TurretFallback");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(rootGo.transform, false);
            body.transform.localScale = new Vector3(1.6f, 2.2f, 1.6f);
            body.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            ApplyUrpMaterial(body, new Color(0.30f, 0.26f, 0.24f));

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "Cap";
            cap.transform.SetParent(rootGo.transform, false);
            cap.transform.localScale = new Vector3(2.2f, 0.5f, 2.2f);
            cap.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            ApplyUrpMaterial(cap, new Color(0.42f, 0.20f, 0.16f));
            return rootGo;
        }

        /// <summary>
        /// Bake an EnemyOwned <see cref="DefenseTower"/> onto the turret with the resolved
        /// stats. Reuses the shipped tower brain (its EnemyOwned path targets the hero +
        /// companions through IDamageableStructure) rather than writing a second one.
        ///
        /// DESTRUCTIBILITY (WO-853 - this SUPERSEDES the old "indestructible by design" note):
        /// an EnemyOwned DefenseTower is now killable BY THE PLAYER. DefenseTower implements
        /// IDamageable as well as IDamageableStructure, and answers the two IsAlive's
        /// differently: IDamageable.IsAlive is liveness-only (`Hp > 0 && !_broken`) and Faction
        /// derives from Allegiance, so an EnemyOwned turret reports CombatFaction.Hostile and
        /// passes the hero's / troops' faction filter - player damage lands via
        /// IDamageable.TakeDamage. The ENEMY seam is UNCHANGED: the explicit
        /// IDamageableStructure.IsAlive still requires PlayerOwned and ApplyContactDamage still
        /// early-returns for anything else, so the garrison never acquires or besieges its own
        /// turret.
        /// </summary>
        private static void ArmTower(GameObject go, TowerPlan plan)
        {
            var dt = go.GetComponent<DefenseTower>();
            if (dt == null) dt = go.AddComponent<DefenseTower>();
            dt.Allegiance = TowerAllegiance.EnemyOwned;
            dt.Range = plan.Range;
            dt.Damage = plan.RawDamage;
            dt.FireRate = plan.FireRate;
            dt.CanHitAir = plan.CanHitAir;
            dt.Element = plan.Element;
            dt.BoltColor = new Color(0.95f, 0.3f, 0.2f);   // hostile red bolt
        }

        // =====================================================================
        //  BuildRing - the reusable concentric primitive, now RADIUS-driven.
        // =====================================================================

        private struct RingReport
        {
            public float HalfExtent;
            public int SlotsPerSide;
            public float SegmentWidth;
            public float GateWidth;
        }

        /// <summary>
        /// Build ONE square ring whose corner offset is <paramref name="targetHalfExtent"/>
        /// (the authored baseRadius). Panel COUNT is derived so no panel is stretched past
        /// <see cref="MaxSegmentWidth"/>, with <paramref name="minSlotsPerSide"/> (the
        /// authored wallSegmentsPerSide) as the floor - so the data still sets the
        /// granularity and the gate width while baseRadius sets the size. Count is forced
        /// ODD so the gate lands on the exact centre panel.
        /// </summary>
        private static RingReport BuildRing(Transform root, float targetHalfExtent, int minSlotsPerSide,
                                            WallTier tier, bool[] gateSides, string ringName)
        {
            var towerPrefab = RaidBaseDresser.LoadVisual(FallbackTowerPath);
            float towerHalf = MeasureTowerHalf(towerPrefab);

            // The wall run per side is the gap between the two corner towers.
            float halfExtent = Mathf.Max(towerHalf + MinSegmentWidth, targetHalfExtent);
            float run = Mathf.Max(MinSegmentWidth, halfExtent * 2f - towerHalf * 2f);

            int n = Mathf.Max(3, minSlotsPerSide);
            int needed = Mathf.CeilToInt(run / MaxSegmentWidth);
            if (needed > n) n = needed;
            if ((n & 1) == 0) n++;                       // force ODD so the gate centres
            float segW = Mathf.Max(MinSegmentWidth, run / n);
            int gateIndex = (n - 1) / 2;

            // Four visual corner posts via the 90-degree-around-origin mirror. They deliberately
            // do NOT carry the "Watchtower" token: authored combat towers above are budgeted to
            // the tier's 12/16/20 worst-case DPS, while runtime-arming these extra posts bypassed
            // that budget and could more than double incoming fire.
            int towers = 0;
            var baseCorner = new Vector3(halfExtent, 0f, -halfExtent);
            for (int s = 0; s < 4; s++)
            {
                var rot = Quaternion.Euler(0f, 90f * s, 0f);
                if (PlaceCornerTower(root, towerPrefab, rot * baseCorner,
                                     $"CornerPost_{ringName}_{SideName[s]}") != null) towers++;
            }

            int gateSpan = 1;
            while (gateSpan * segW < RaidBaseDresser.MinGateWidth && gateSpan + 2 <= n)
                gateSpan += 2;
            int gateStart = gateIndex - gateSpan / 2;
            float gateWidth = gateSpan * segW;

            int segs = 0;
            for (int s = 0; s < 4; s++)
            {
                bool sideHasGate = gateSides != null && s < gateSides.Length && gateSides[s];
                var rot = Quaternion.Euler(0f, 90f * s, 0f);
                var midpoint = rot * new Vector3(0f, 0f, -halfExtent);
                var alongDir = rot * Vector3.right;
                for (int i = 0; i < n; i++)
                {
                    if (sideHasGate && i >= gateStart && i < gateStart + gateSpan) continue;
                    float along = -run * 0.5f + (i + 0.5f) * segW;
                    segs += PlaceSegment(root, midpoint + alongDir * along, rot, tier, segW,
                                         $"{ringName}_S{SideName[s]}_{i}");
                }
            }

            Debug.Log($"[RaidBaseGenerator] ring '{ringName}': target +/-{targetHalfExtent:F1}m -> " +
                      $"+/-{halfExtent:F1}m, {n} panel(s)/side @ {segW:F2}m (floor {minSlotsPerSide}), tier {tier} " +
                      $"({towers} watchtowers, {segs} wall panels), gates=[{GatesToStr(gateSides)}].");

            return new RingReport
            {
                HalfExtent = halfExtent,
                SlotsPerSide = n,
                SegmentWidth = segW,
                GateWidth = gateWidth,
            };
        }

        // =====================================================================
        //  ARENA EXTERIOR BOUNDARY (WO-1632) - the ring that closes the whole plane.
        //
        //  Owner, 2026-09-10: "exterior walls around entire arena" / "similar strategy as
        //  we used in battle arena". This is the SAME vocabulary the siege venue uses to
        //  frame itself (ProceduralSiegeArenaBuilder's OuterBoundary_Ring: large
        //  polyperfect rocks, colliders ON), placed on a SQUARE because the raid ground
        //  plane is a 140 m square and the staging marker's diagonal fallback deliberately
        //  parks in its CORNERS - no circle that fits the plane could contain those points.
        //
        //  It carries NO GATE. It is not part of the base's layered defense; it is the edge
        //  of the world. The base's own Outer / Keep rings are untouched.
        //
        //  NAVMESH: RaidNavBake marks every renderer NavigationStatic and bakes, so vertical
        //  geometry carves out (RaidNavBake.cs:55-64). The ring therefore ends the walkable
        //  area at itself with no extra wiring - and because the ground plane is unchanged,
        //  BakeAll still reports a walkable floor for every raid scene.
        // =====================================================================
        private static ArenaBoundaryRing.BoundaryReport BuildArenaBoundary(Transform root, int seed)
        {
            // Clear BOTH names - a prior run that failed containment left the UNSAFE root, and
            // leaving it behind would red the suite forever on a scene that is now clean.
            var prior = root.Find(ArenaBoundaryRootName);
            if (prior != null) Object.DestroyImmediate(prior.gameObject);
            var priorUnsafe = root.Find(ArenaBoundaryUnsafeName);
            if (priorUnsafe != null) Object.DestroyImmediate(priorUnsafe.gameObject);

            var ringRoot = new GameObject(ArenaBoundaryRootName);
            ringRoot.transform.SetParent(root, false);
            ringRoot.transform.localPosition = Vector3.zero;

            var rng = new System.Random(seed ^ 0x5A17);   // own stream: the ring never shifts turret/prop seeds
            var report = ArenaBoundaryRing.PlaceSquarePerimeter(
                ringRoot.transform, rng, ArenaBoundaryHalfExtent, ArenaBoundaryBandHalf,
                ArenaBoundaryBandFill, ArenaBoundaryContainmentSlack, ArenaBoundaryContainmentHeadroom,
                ArenaBoundaryOverlap, ArenaBoundaryRadialJitterCap,
                ArenaBoundaryRing.RockPaths, ArenaBoundaryPieceLabel,
                ArenaBoundaryScaleMin, ArenaBoundaryScaleMax, ArenaBoundaryMaxPerSide,
                "[RaidBaseGenerator]");

            // Same line shape as the wall rings above, so one grep reads every ring in a bake.
            string gapText = report.WorstGap <= 0f
                ? ((-report.WorstGap).ToString("F2") + "m overlap")
                : ("*** " + report.WorstGap.ToString("F2") + "m GAP ***");
            string clampText = report.Clamped
                ? (" CLAMPED at " + ArenaBoundaryMaxPerSide + "/side")
                : "";
            string scaleText = report.ScaleFitted
                ? (" scale " + ArenaBoundaryScaleMax.ToString("F2") + "->" + report.AppliedScaleMax.ToString("F2") +
                   " band-fitted")
                : (" scale " + report.AppliedScaleMax.ToString("F2"));
            Debug.Log($"[RaidBaseGenerator] ring '{ArenaBoundaryRingName}': target +/-{ArenaBoundaryHalfExtent:F1}m -> " +
                      $"+/-{ArenaBoundaryHalfExtent:F1}m, {report.PerSide} piece(s)/side @ {report.Stride:F2}m " +
                      $"(piece {report.PieceFootprint:F2}m, {gapText}{clampText},{scaleText}, reach " +
                      $"{report.InwardReach:F2}m of {ArenaBoundaryBandHalf:F2}m band, jitter " +
                      $"+/-{report.RadialJitter:F2}m), kit landscape-rock " +
                      $"(0 watchtowers, {report.Placed} boundary pieces), gates=[none].");

            if (report.WorstGap > 0f)
                Debug.LogWarning("[RaidBaseGenerator] the arena boundary ring is NOT continuous - " +
                                 $"{report.WorstGap:F2}m of open ground between pieces. Lower " +
                                 "ArenaBoundaryOverlap or raise ArenaBoundaryMaxPerSide / " +
                                 "ArenaBoundaryScaleMin. This is a finding about the ring's density, " +
                                 "not a number to soften.");

            return report;
        }

        /// <summary>Ring label used in the build log (kept next to the other ring names).</summary>
        private const string ArenaBoundaryRingName = "Arena";

        /// <summary>
        /// PROVE, at every build, that the ring did not fence the player's own markers out.
        /// The staging marker and the hero entry point must both sit inside the ring's inner
        /// face. This never adjusts anything - a violation is a LAYOUT finding and is logged
        /// as an error, the same discipline PlaceStagingMarker uses (:526-538).
        /// <para/>
        /// The faces are computed from the MEASURED widest piece the palette can produce
        /// (<c>BoundaryReport.InwardReach</c>) PLUS the scatter actually applied
        /// (<c>RadialJitter</c>) - never from the scale multiplier alone. Pricing a 3.4x scale
        /// as 1.7 m of reach assumes a 1 m mesh; the 2026-09-10 bake measured 3.38 m and this
        /// assert fired on all three configs, which is exactly what it is for.
        /// <para/>
        /// Returns FALSE on any violation, and the caller then renames the ring root
        /// (<see cref="MarkBoundaryUnsafe"/>) so the finding survives into the saved scene.
        /// </summary>
        private static bool AssertBoundaryContainsStaging(string id, Vector3 staging, Vector3 heroStart,
                                                          ArenaBoundaryRing.BoundaryReport boundary)
        {
            // Pieces are centred on the ring line and scatter symmetrically within the band,
            // so half of the widest piece is the whole inward reach.
            float inwardReach = boundary.InwardReach + boundary.RadialJitter;
            float innerFace = ArenaBoundaryHalfExtent - inwardReach;
            float outerFace = ArenaBoundaryHalfExtent + inwardReach;
            float clampCorner = MapHalfExtent - StagingPlaneEdgeMargin;   // the diagonal fallback's own clamp
            float edgeLimit = MapHalfExtent + ArenaBoundaryEdgeTolerance;
            bool safe = true;

            // STRICT, and the required margin is NAMED. An inner face that merely EQUALS the
            // clamp is a staging marker sitting on a boulder, so the test is
            // innerFace >= clampCorner + slack, not innerFace > clampCorner.
            float innerSlack = innerFace - clampCorner;
            float outerSlack = edgeLimit - outerFace;

            // The compare carries an EXPLICIT epsilon: float error must never read as a FAIL.
            // Bake 3 lost this by 1e-7 on a ring that was built exactly to spec.
            float required = ArenaBoundaryContainmentSlack - ArenaBoundaryContainmentEpsilon;

            if (innerSlack < required)
            {
                safe = false;
                Debug.LogError($"[RaidBaseGenerator] ARENA BOUNDARY ASSERT: the staging clamp reaches " +
                               $"+/-{clampCorner:F3}m per axis and the boundary ring's inner face is at " +
                               $"+/-{innerFace:F3}m (ring +/-{ArenaBoundaryHalfExtent:F3}m minus a MEASURED " +
                               $"{boundary.InwardReach:F3}m reach + {boundary.RadialJitter:F3}m jitter). That " +
                               $"is {innerSlack:F3}m of clearance against a required " +
                               $"{ArenaBoundaryContainmentSlack:F3}m " +
                               $"(epsilon {ArenaBoundaryContainmentEpsilon:F3}m, so the test is " +
                               $"{innerSlack:F3} < {required:F3}) - a staging marker would sit on, or inside, " +
                               "the ring. The LAYOUT is supposed to reserve slack + " +
                               $"ArenaBoundaryContainmentHeadroom ({ArenaBoundaryContainmentHeadroom:F3}m) = " +
                               $"{ArenaBoundaryContainmentSlack + ArenaBoundaryContainmentHeadroom:F3}m, so if " +
                               "this fires the fit is genuinely wrong: lower ArenaBoundaryBandFill (fill + " +
                               "jitter + slack + headroom must fit the half-band) or lower " +
                               "ArenaBoundaryRadialJitterCap. Never raise StagingPlaneEdgeMargin, which would " +
                               "move the authored staging positions.");
            }
            else if (outerSlack < required)
            {
                safe = false;
                Debug.LogError($"[RaidBaseGenerator] ARENA BOUNDARY ASSERT: the ring's outer face is at " +
                               $"+/-{outerFace:F3}m against a {edgeLimit:F3}m edge limit " +
                               $"(plane {MapHalfExtent:F0}m + {ArenaBoundaryEdgeTolerance:F1}m cosmetic " +
                               $"tolerance) = {outerSlack:F3}m of clearance against a required " +
                               $"{ArenaBoundaryContainmentSlack:F3}m (epsilon " +
                               $"{ArenaBoundaryContainmentEpsilon:F3}m, so the test is {outerSlack:F3} < " +
                               $"{required:F3}). Pieces would hang off the world. Lower ArenaBoundaryBandFill.");
            }
            else
            {
                Debug.Log($"[RaidBaseGenerator] arena boundary containment for '{id}': inner face " +
                          $"+/-{innerFace:F3}m (measured reach {boundary.InwardReach:F3}m + jitter " +
                          $"{boundary.RadialJitter:F3}m) vs staging clamp +/-{clampCorner:F3}m = " +
                          $"{innerSlack:F3}m of slack; outer face +/-{outerFace:F3}m vs edge limit " +
                          $"+/-{edgeLimit:F3}m = {outerSlack:F3}m of slack; both clear the required " +
                          $"{ArenaBoundaryContainmentSlack:F3}m by design headroom " +
                          $"{ArenaBoundaryContainmentHeadroom:F3}m.");
            }

            float sMax = Mathf.Max(Mathf.Abs(staging.x), Mathf.Abs(staging.z));
            if (sMax + required > innerFace)
            {
                safe = false;
                Debug.LogError($"[RaidBaseGenerator] ARENA BOUNDARY ASSERT for '{id}': the staging marker at " +
                               $"{staging} sits {sMax:F2}m out on its widest axis against a " +
                               $"{innerFace:F2}m inner face - {innerFace - sMax:F2}m of clearance where " +
                               $"{ArenaBoundaryContainmentSlack:F2}m is required. The player would deploy on " +
                               "top of, or outside, the arena boundary.");
            }

            float hMax = Mathf.Max(Mathf.Abs(heroStart.x), Mathf.Abs(heroStart.z));
            if (hMax + required > innerFace)
            {
                safe = false;
                Debug.LogError($"[RaidBaseGenerator] ARENA BOUNDARY ASSERT for '{id}': the hero entry marker at " +
                               $"{heroStart} sits {hMax:F2}m out against a {innerFace:F2}m inner face - " +
                               $"{innerFace - hMax:F2}m of clearance where {ArenaBoundaryContainmentSlack:F2}m " +
                               "is required. That is a baseRadius too large for this plane - see the radius " +
                               "clamp above.");
            }

            return safe;
        }

        /// <summary>
        /// Name the ring root so a containment failure SURVIVES INTO THE SAVED SCENE.
        /// <para/>
        /// A `Debug.LogError` in a bake log is only a finding while someone is reading that
        /// log; the scene is the artifact the player loads and the artifact the suite reads.
        /// Renaming the root makes `RaidArenaShapeRegression` Case 6 red on the shipped
        /// scene, with no log parsing, exactly as it reds on a MISSING ring.
        /// </summary>
        private static void MarkBoundaryUnsafe(Transform root)
        {
            var ring = root != null ? root.Find(ArenaBoundaryRootName) : null;
            if (ring == null) return;
            ring.gameObject.name = ArenaBoundaryUnsafeName;
            Debug.LogError($"[RaidBaseGenerator] the arena boundary root is renamed to " +
                           $"'{ArenaBoundaryUnsafeName}' so the failure is carried by the SCENE, " +
                           "not only by this log. The suite reds on it until the ring is rebuilt clean.");
        }

        private static string GatesToStr(bool[] gateSides)
        {
            if (gateSides == null) return "none";
            string acc = "";
            for (int s = 0; s < 4 && s < gateSides.Length; s++)
                if (gateSides[s]) acc += (acc.Length > 0 ? "," : "") + SideName[s];
            return acc.Length > 0 ? acc : "none";
        }

        // =====================================================================
        //  Place helpers
        // =====================================================================

        private static float MeasureTowerHalf(GameObject prefab)
        {
            if (prefab == null) return SegSize.x;
            var tmp = Object.Instantiate(prefab);
            float half = SegSize.x;
            var rends = tmp.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
                half = Mathf.Max(b.size.x, b.size.z) * 0.5f;
            }
            Object.DestroyImmediate(tmp);
            return half;
        }

        /// <summary>Place a corner/court post (seated, facing outward), with a cheap fallback when art moved.</summary>
        private static GameObject PlaceCornerTower(Transform parent, GameObject prefab, Vector3 pos, string name)
        {
            GameObject go;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (go == null) go = Object.Instantiate(prefab);
            }
            else
            {
                go = BuildFallbackTurret();
                MagentaGuard.ProtectPrimitiveArt(go, "RaidBaseGenerator.PlaceCornerTower");
                Debug.LogWarning($"[RaidBaseGenerator] tower prefab missing at StructureContent/{FallbackTowerPath}; " +
                                 $"using a lightweight fallback post for '{name}'.");
            }
            go.name = name;                         // visual post; combat towers are separately budgeted
            go.transform.SetParent(parent, false);
            var outward = pos.sqrMagnitude > 0.001f ? pos.normalized : Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.z), Vector3.up);
            go.transform.position = pos;
            SeatOnGround(go);
            return go;
        }

        /// <summary>
        /// FBX-FLAT CORRECTION. Several Resources/Structures entries are raw .fbx models, not
        /// prefabs (ArcaneSpire_1 is one), and this project's FBX imports have a history of
        /// landing FLAT - which is exactly why PlaceSegment applies a hard -90 X to every wall
        /// panel. A flat spire would then be "scaled to height" on the wrong axis and become a
        /// pancake. Rather than hardcode a correction per asset, MEASURE: if the model is
        /// materially wider than it is tall, stand it up and say so. Idempotent + logged.
        /// </summary>
        private static void EnsureUpright(GameObject go, string what)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
            float widest = Mathf.Max(b.size.x, b.size.z);
            if (b.size.y >= widest * 0.8f) return;      // already standing

            go.transform.rotation = go.transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            Debug.LogWarning($"[RaidBaseGenerator] '{what}' imported FLAT " +
                             $"(h={b.size.y:F1}m vs {widest:F1}m wide) - applied the -90 X FBX-flat " +
                             "correction so it stands up. If the art is genuinely squat, this is a false " +
                             "positive - author a prefab with the right orientation instead.");
        }

        /// <summary>Drop an object so its lowest renderer bound sits at y = 0.</summary>
        private static void SeatOnGround(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
            go.transform.position += new Vector3(0f, -b.min.y, 0f);
        }

        /// <summary>
        /// Rendered world height of an object, unchanged (WO-1617). Used where the art is
        /// authored at its true size and must be REPORTED, not refitted - the log line and
        /// RaidSpire.Configure still need a real height even when nothing was scaled.
        /// </summary>
        private static float MeasuredHeight(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return 0f;
            var b = rends[0].bounds;
            for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
            return b.size.y;
        }

        /// <summary>
        /// Uniformly scale an object so its rendered height matches <paramref name="target"/>.
        /// Returns the height achieved.
        ///
        /// WO-1619 STEP 1 - the instrumentation. The clamp below can SATURATE, and until that
        /// line was written a saturated fit was INDISTINGUISHABLE from a satisfied one in the
        /// bake log: the method already returned the truth (b.size.y * f) and nothing compared
        /// it to what was asked for. Every call reports the raw measured bounds height, the
        /// target, the factor the fit WANTED, the factor it was ALLOWED, which bound it hit, and
        /// the achieved height. A saturated fit reports at WARNING, because "I could not do what
        /// I was asked" is an anomaly, not information (CLAUDE.md sec.12).
        ///
        /// WO-1619 STEP 2 - what the instrumentation then PROVED, and the fix it licensed.
        /// Builds/wave2-bake (2026-09-10), all three baked configs, identical to 3 decimals:
        ///   "rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m wantedFactor=14.366
        ///    appliedFactor=8.000 saturatedAt=UPPER achieved=8.02m (56% of target) - SATURATED"
        /// The CAP was the wrong axis, not the target:
        ///  - `raw` is a WORLD-space AABB (Renderer.bounds, encapsulated below), so it ALREADY
        ///    includes the prefab's 0.010 localScale. `wanted = target / raw` is therefore
        ///    world-metres over world-metres and prefabScaleBefore never enters the factor at
        ///    all - a pre-scaled prefab does not make the target wrong, it only explains why the
        ///    factor needed is large.
        ///  - 14.366 is the honest factor for 1.002 m of art at a 14.40 m target, and the old
        ///    8f ceiling refused it, silently, on every raid the game ships.
        /// The two bounds are now the named tunables SpireFitFactorMin / SpireFitFactorMax
        /// declared beside SpireMonumentMultiplier - zero magic literals survive in the fit path
        /// (WO-1619 sec.3). The LOWER bound's value did not move: it is doing real work against
        /// oversized art (WO-1619 sec.5 pin).
        ///
        /// <paramref name="what"/> is the caller's label for the log line. It is REQUIRED and
        /// there is no silent branch: a fit that reports nothing is the exact defect this
        /// instrumentation exists to end, so an empty label falls back to the object name
        /// rather than suppressing the line.
        /// </summary>
        private static float ScaleToHeight(GameObject go, float target, string what)
        {
            string label = string.IsNullOrEmpty(what) ? go.name : what;

            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0)
            {
                Debug.LogWarning($"[RaidBaseGenerator] SPIRE FIT {label}: NO RENDERERS - nothing to " +
                                 $"measure and nothing scaled; target={target:F2}m is being REPORTED as " +
                                 "achieved, which is a fiction. (WO-1619 step 1 instrumentation.)");
                return target;
            }

            var b = rends[0].bounds;
            for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
            float raw = b.size.y;
            Vector3 scaleBefore = go.transform.localScale;

            if (raw <= 0.0001f)
            {
                Debug.LogWarning($"[RaidBaseGenerator] SPIRE FIT {label}: DEGENERATE BOUNDS " +
                                 $"(rawHeight={raw:F5}m) - no fit applied; target={target:F2}m is being " +
                                 "REPORTED as achieved, which is a fiction. (WO-1619 step 1 instrumentation.)");
                return target;
            }

            float wanted = target / raw;
            float f = Mathf.Clamp(wanted, SpireFitFactorMin, SpireFitFactorMax);
            go.transform.localScale *= f;
            float achieved = raw * f;

            // Mathf.Clamp returns `wanted` bit-exactly when it is inside the range, so these two
            // comparisons are exact and - deliberately - do not restate the bound literals.
            bool satUpper = wanted > f;
            bool satLower = wanted < f;
            string bound = satUpper ? "UPPER" : (satLower ? "LOWER" : "none");
            float pct = achieved / target * 100f;

            string line = $"[RaidBaseGenerator] SPIRE FIT {label}: rawHeight={raw:F3}m " +
                          $"prefabScaleBefore={scaleBefore.y:F3} target={target:F2}m " +
                          $"wantedFactor={wanted:F3} appliedFactor={f:F3} saturatedAt={bound} " +
                          $"achieved={achieved:F2}m ({pct:F0}% of target)";

            if (satUpper || satLower)
                Debug.LogWarning(line + " - SATURATED: the fit could NOT reach the height the " +
                                        "generator asked for. (WO-1619 step 1 instrumentation.)");
            else
                Debug.Log(line + " - fit satisfied. (WO-1619 step 1 instrumentation.)");

            return achieved;
        }

        /// <summary>
        /// Place one tier wall panel along a (rotated) side, stretched to
        /// <paramref name="segWidth"/>. The panel is box-fitted + ground-seated while
        /// axis-aligned (reliable world-AABB fit), then rotated to the side.
        ///
        /// NEW: the panel gets a real BoxCollider on the "Structure" layer. Before this
        /// the raid walls carried NO collider (the generator only calls WallSegment.SetTier,
        /// and only WallSegment.Configure builds the blocker + moves the layer), so they
        /// blocked neither movement nor the line-of-sight linecasts that are masked to
        /// "Structure". A boundary wall you can shoot straight through is not a boundary.
        /// </summary>
        private static int PlaceSegment(Transform parent, Vector3 pos, Quaternion sideRot, WallTier tier,
                                        float segWidth, string name)
        {
            var prefab = Resources.Load<GameObject>(WallTierData.Get(tier).SegmentPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[RaidBaseGenerator] missing Resources/{WallTierData.Get(tier).SegmentPrefabPath} (tier {tier}).");
                return 0;
            }

            var seg = new GameObject($"Wall_{name}");
            seg.transform.SetParent(parent, false);
            seg.transform.localPosition = Vector3.zero;
            seg.transform.localRotation = Quaternion.identity;     // fit while axis-aligned

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) go = Object.Instantiate(prefab);
            go.transform.SetParent(seg.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // FBX imports flat -> stand upright

            float seatY = 0f;
            Vector3 sc = seg.transform.localScale;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int k = 1; k < rends.Length; k++) b.Encapsulate(rends[k].bounds);
                if (b.size.x > 0.0001f) sc.x *= segWidth / b.size.x;
                if (b.size.y > 0.0001f) sc.y *= SegSize.y / b.size.y;
                if (b.size.z > 0.0001f) sc.z *= SegSize.z / b.size.z;
                seg.transform.localScale = sc;
                var r2 = go.GetComponentsInChildren<Renderer>(true);
                var b2 = r2[0].bounds;
                for (int k = 1; k < r2.Length; k++) b2.Encapsulate(r2[k].bounds);
                seatY = -b2.min.y;                                  // ground-seat offset
            }

            seg.transform.rotation = sideRot;
            seg.transform.position = new Vector3(pos.x, seatY, pos.z);

            var ws = seg.AddComponent<WallSegment>();
            ws.SetTier((int)tier);

            // SOLID BOUNDARY. Sizes are authored in WORLD units, so they are divided by the
            // fit scale that lives on this transform. WallSegment.Awake adopts this collider
            // as its blocker; the "Structure" layer is what every LoS linecast is masked to.
            var box = seg.AddComponent<BoxCollider>();
            float sx = Mathf.Abs(sc.x) > 0.0001f ? sc.x : 1f;
            float sy = Mathf.Abs(sc.y) > 0.0001f ? sc.y : 1f;
            float sz = Mathf.Abs(sc.z) > 0.0001f ? sc.z : 1f;
            box.size = new Vector3(segWidth / sx, SegSize.y / sy, SegSize.z / sz);
            box.center = new Vector3(0f, (SegSize.y * 0.5f - seatY) / sy, 0f);
            box.isTrigger = false;
            int structureLayer = LayerMask.NameToLayer("Structure");
            if (structureLayer >= 0) seg.layer = structureLayer;

            return 1;
        }

        private static WallTier ParseTier(string s, WallTier fallback)
        {
            if (!string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out WallTier t)) return t;
            return fallback;
        }

        private static DamageElement ParseElement(string s)
        {
            if (!string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out DamageElement e)) return e;
            return DamageElement.None;
        }

        /// <summary>Stable, platform-independent hash of a config id (the deterministic layout seed).</summary>
        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 17;
            unchecked
            {
                int h = 23;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h & 0x7fffffff;
            }
        }

        // =====================================================================
        //  structures-catalog reader (editor-side).
        //  CatalogRegistry is populated by CatalogBootstrap, which is
        //  [RuntimeInitializeOnLoadMethod] and therefore NEVER runs in batchmode
        //  editor code (BlankStartCensusRegression.cs:99 documents the same catch).
        //  So the builder reads the canonical JSON directly, with the same
        //  CanonicalJson (Resources-copy-wins) + Newtonsoft path CatalogBootstrap uses.
        // =====================================================================

        private const string StructuresRelativePath = "Data/Canonical/structures-catalog.json";

        [Serializable]
        private sealed class StructRepo
        {
            public float range;
            public float damage;
            public float fireRate;
            public bool canHitAir;
            public string element;
            public float visualHeight;
            public float heightMul;
        }

        [Serializable]
        private sealed class StructEntry
        {
            public string id;
            public string visualPrefabPath;
            public StructRepo repo;
        }

        [Serializable]
        private sealed class StructFile
        {
            public List<StructEntry> entries;
        }

        private static Dictionary<string, StructEntry> _structures;

        /// <summary>Force a fresh structures-catalog read on next access (after a JSON edit).</summary>
        public static void InvalidateStructureCatalog() => _structures = null;

        private static StructEntry FindStructure(string id)
        {
            EnsureStructures();
            if (string.IsNullOrEmpty(id) || _structures == null) return null;
            return _structures.TryGetValue(id, out var e) ? e : null;
        }

        private static void EnsureStructures()
        {
            if (_structures != null) return;
            _structures = new Dictionary<string, StructEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string text = CanonicalJson.Read(StructuresRelativePath);
                if (string.IsNullOrEmpty(text))
                {
                    Debug.LogWarning($"[RaidBaseGenerator] {StructuresRelativePath} not found - " +
                                     "turret/spire art + stats fall back to defaults.");
                    return;
                }
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                var file = JsonConvert.DeserializeObject<StructFile>(text, settings);
                if (file == null || file.entries == null)
                {
                    Debug.LogWarning("[RaidBaseGenerator] structures-catalog.json parsed empty - defaults used.");
                    return;
                }
                foreach (var e in file.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.id)) continue;
                    _structures[e.id] = e;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RaidBaseGenerator] structures-catalog.json read failed ({ex.Message}) - defaults used.");
            }
        }

        // =====================================================================
        //  Legacy Iron-Bastion flagship layout (menu items preserved).
        // =====================================================================

        private static GameObject Build()
        {
            var prior = GameObject.Find(RootName);
            if (prior != null) Object.DestroyImmediate(prior);

            var root = new GameObject(RootName);
            root.transform.position = Vector3.zero;

            BuildIronBastion(root.transform);
            return root;
        }

        private static void BuildIronBastion(Transform root)
        {
            // Radii preserved from the original slot-driven maths (15 and 7 slots @ 1.5 m
            // plus the corner tower half-footprint) so the flagship reads as it always has.
            var outerGates = new bool[4] { true, false, false, false };  // S only
            var outer = BuildRing(root, OuterSlotsPerSide * SegSize.x * 0.5f + 4f, OuterSlotsPerSide,
                                  OuterTier, outerGates, "Outer");

            var innerGates = new bool[4] { false, false, true, false };  // N only (opposite)
            var inner = BuildRing(root, InnerSlotsPerSide * SegSize.x * 0.5f + 4f, InnerSlotsPerSide,
                                  InnerTier, innerGates, "Keep");

            var boss = new GameObject("BossSpawn");
            boss.transform.SetParent(root, false);
            boss.transform.localPosition = Vector3.zero;

            float courtRadius = (inner.HalfExtent + outer.HalfExtent) * 0.5f;
            var courtPrefab = Resources.Load<GameObject>(FallbackTowerPath);
            int courtTowers = 0;
            for (int c = 0; c < 4; c++)
            {
                var diag = new Vector3((c == 0 || c == 3) ? 1f : -1f, 0f,
                                       (c == 0 || c == 1) ? 1f : -1f).normalized;
                if (PlaceCornerTower(root, courtPrefab, diag * courtRadius, $"CornerPost_Court_{c}") != null)
                    courtTowers++;
            }

            // WO-1632: "exterior walls around entire arena" is EVERY raid base, the parked
            // flagship included. BuildAllRaidScenes does not rebake Iron Bastion (it is not a
            // scene-config), so this only lands when the menu item / Build() is run.
            var boundary = BuildArenaBoundary(root, StableHash(RootName));

            Debug.Log($"[RaidBaseGenerator] '{RootName}': OUTER {OuterTier} (gate S, +/-{outer.HalfExtent:F1}m) + " +
                      $"KEEP {InnerTier} (gate N, +/-{inner.HalfExtent:F1}m) + BossSpawn@centre + " +
                      $"{courtTowers} court towers + ARENA BOUNDARY +/-{ArenaBoundaryHalfExtent:F1}m " +
                      $"({boundary.Placed} landscape piece(s)). " +
                      "Funnel: cross the courtyard S->N under crossfire.");
        }
    }
}
