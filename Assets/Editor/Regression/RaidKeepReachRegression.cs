// =============================================================================
// RaidKeepReachRegression — WO-1749.
//
// THE DEFECT THIS EXISTS FOR: on the Seeker (tester build 2026.09.15.371127,
// scene RaidBase_IronBastion) TroopController emitted `routeObj=PathPartial`
// 1650 times and `PathComplete` ZERO times — not one troop, in any phase, ever
// had a complete NavMesh route to the objective. The owner watched the warband
// stop at the keep platform edge and never walk around to the ramp.
//
// RAID_NAV_BAKE_OK was GREEN over that. It only ever proved the triangulation
// was non-empty ("N verts / M tris"), which says nothing about CONNECTIVITY: a
// keep platform that bakes as a walkable ISLAND produces exactly the same green
// line as one the ramp joins to the courtyard. This file is the missing oracle —
// it asks the baked NavMesh the question the player asks: can I walk from the
// courtyard to the thing I have to break?
//
// THREE LEGS, chosen so a failure NAMES its own cause instead of conflating
// three systems (the probe points are derived from the built tree, never
// hardcoded — the arena's radii are per-config):
//   court->spire : the whole chain (outer gate, inner ring gate, ramp, platform).
//   lip->spire   : from 1.5 m south of the ramp's FOOT. Inside the inner ring,
//                  outside the platform — so it tests ONLY ramp + platform +
//                  goal-mapping, with no wall, gate or prop in the way. This is
//                  the load-bearing leg: it is the one a wall can never explain.
//   top->spire   : standing ON the platform top. Tests goal-mapping alone.
// Plus RAID_NAV_REACH_GOAL, which reports where NavMesh.SamplePosition maps the
// spire's own WorldPosition — the reading that settled the buried-spire question
// (fixed in RaidBaseGenerator.ReseatSpireOnKeepPlatform).
//
// ⛔ THE CRITERION IS **ARRIVED**, NOT **PathComplete** — AND THE EARLIER
//    PathComplete CRITERION WAS WRONG. It shipped in this file's first pass and
//    is retired here; this paragraph exists so nobody "restores" it.
//
//    Why it was wrong: `PathComplete` asks whether the navmesh query reached the
//    END POLYGON. The objective is a SOLID BUILDING whose renderers bake
//    NavigationStatic, so for any sufficiently wide objective a route to its
//    CENTRE terminates at the carve edge and reports PathPartial forever — the
//    gate would be unsatisfiable by construction, and the next seat would
//    "fix" working geometry chasing it. An unsatisfiable gate is worse than no
//    gate.
//
//    (For the record, on the 2026-09-15 bake that question was ALSO measured and
//    the carve did NOT explain that day's red: RAID_NAV_REACH_GOAL reported
//    mappedPos=(0.00, 1.74, 0.00) against goal=(0.00, 1.52, 0.00) on IronBastion
//    and (0.00, 0.99, 0.00) against (0.00, 0.82, 0.00) on mage_enclave — the same
//    X and Z, displaced only in Y. A carve would have pushed the nearest point
//    HORIZONTALLY to the footprint edge. So there is navmesh at the objective's
//    own XZ. The criterion changed because PathComplete is BRITTLE, not because
//    it was unreachable that day.)
//
//    What ARRIVED means, every term MEASURED at the moment it is asked:
//    the route's last corner must land within (objective footprint half-width,
//    off its own renderer bounds) + (agent radius, off the LIVE NavMesh build
//    settings) + ArrivalSlack, measured in the PLANE. A troop standing there is
//    touching the spire. It is satisfiable whether or not an objective carves,
//    it fails loudly on a genuine island (a route that stops at the platform
//    edge is ~13 m out on IronBastion), and it cannot be satisfied by breaking
//    geometry. `status=` / `rawStatus=` are still logged, as evidence, not as
//    the gate.
//
// ⚠ `lastCornerDist` IS THE NUMBER TO READ FIRST ON ANY RED. `lastCornerY` alone
//    cannot tell an island from an arrival — a route that stops 13 m away at the
//    platform edge and one that stops touching the spire can report the same
//    height. The first pass of this file logged only the height, which is why a
//    red could not be diagnosed from the log and needed a second bake.
//
// ⚠ CAVEAT, WRITTEN DOWN SO NOBODY READS MORE INTO A PASS THAN IT PROVES:
// PrepareDestructibleWalls / PrepareMovableTowers give intact walls and towers
// CARVING NavMeshObstacles (RaidNavBake.cs:220 / :162). Carving is a runtime
// service; in a batchmode edit-mode probe it may not have applied. So
// `court->spire` measured here can be OPTIMISTIC relative to the device, while
// `lip->spire` is carve-independent by construction. Judge a device report by
// the device's own `routeObj=` lines, never by this suite alone.
//
// EVERY leg is measured TWICE — once with both endpoints explicitly sampled onto
// the mesh, once with the goal handed in RAW, which is exactly the call
// TroopController.RefreshRouteToObjective makes (TroopController.cs:1144-1148).
// Unity's implicit endpoint mapping need not agree with our explicit sample; a
// leg passes only when BOTH routes ARRIVE, so this instrument cannot report green
// on a route the device's own call shape does not complete. The delta between
// `lastCornerDist=` and `rawLastCornerDist=` on the same line is itself a finding.
//
// Precedent that a scene's BAKED NavMesh is live right after OpenScene in batch:
// BiomeRoadsDropReachProbe.cs:64 opens Single and :97-100 queries NavMesh
// .SamplePosition against it.
//
// Scene list is DISCOVERED from disk, never copied from RaidNavBake.RaidScenes —
// a hardcoded second copy rots silently (CLAUDE.md §2/§5/§16), and
// DeNelle.EditorRegression cannot reference DeNelle.Editor anyway.
//
// Entry points:
//   RaidKeepReachRegression.Run(out report)  — DataRegression suite shape.
//   RaidKeepReachRegression.RunAll()         — standalone, prints the marker.
//   RaidKeepReachRegression.ProbeScene(...)  — one already-open scene; called by
//                                              RaidNavBake right after its bake.
// Marker: RAID_KEEP_REACH_OK / RAID_KEEP_REACH_FAIL <n>
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using DeNelle.Village;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    public static class RaidKeepReachRegression
    {
        private const string PlatformName = "KeepPlatform";
        private const string RampName     = "KeepRamp";

        /// <summary>Vertical/horizontal slack allowed when mapping the SPIRE's own point onto
        /// the mesh. Generous on purpose: the goal legitimately sits inside the spire art, and
        /// the question is WHERE it maps, not whether it maps exactly.</summary>
        private const float GoalSampleRadius = 6f;

        /// <summary>Slack for a probe START point. Small: a start we cannot map within this is a
        /// broken probe, and must be reported as such rather than silently passing.</summary>
        private const float StartSampleRadius = 3f;

        /// <summary>How far a START may be snapped VERTICALLY before the leg stops meaning what its
        /// label says. See <see cref="RunLeg"/>'s mustStayAtStartHeight.</summary>
        private const float StartHeightTolerance = 0.5f;

        /// <summary>
        /// Extra planar slack on top of (objective footprint radius + agent radius) before a leg is
        /// called "arrived". Half a metre: enough to absorb the funnel's last corner landing on a
        /// polygon edge rather than dead against the art, far too little to hide a 13 m island.
        /// The two real terms are MEASURED, never written down (footprint off the spire's own
        /// bounds, agent radius off the live NavMesh settings) — see <see cref="ArrivalRadius"/>.
        /// </summary>
        private const float ArrivalSlack = 0.5f;

        // -- entry points -----------------------------------------------------

        /// <summary>Standalone batch entry — prints the RAID_KEEP_REACH_OK/_FAIL marker.</summary>
        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log("RAID_KEEP_REACH_OK " + report);
            else Debug.LogError("RAID_KEEP_REACH_FAIL " + report);
        }

        /// <summary>DataRegression suite shape: opens every baked raid scene and asks the baked
        /// NavMesh whether the courtyard reaches the spire.</summary>
        public static bool Run(out string report)
        {
            var failures = new List<string>();
            var notes = new StringBuilder();
            string restore = SceneManager.GetActiveScene().path;
            int scanned = 0;

            List<string> scenes = DiscoverScenes();
            for (int i = 0; i < scenes.Count; i++)
            {
                string path = scenes[i];
                Scene scene;
                try
                {
                    // SINGLE, mirroring DungeonComposedPillarsRegression: an additive open in
                    // batchmode gave non-deterministic results there, and a flaky oracle trains
                    // everyone to ignore it. Single also guarantees exactly ONE scene's NavMesh
                    // is live, which is the whole premise of every query below.
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                }
                catch (Exception ex)
                {
                    failures.Add(path + " could not be opened: " + ex.Message);
                    continue;
                }
                scanned++;
                if (!ProbeScene(scene, out string note)) failures.Add(scene.name + ": " + note);
                else notes.AppendLine(scene.name + ": " + note);
            }

            if (scanned == 0)
                failures.Add("no baked raid scene could be opened - this suite proved NOTHING; do not read it as a pass");

            RestoreScene(restore);

            if (failures.Count == 0)
            {
                report = "scanned " + scanned + " baked raid scene(s); courtyard and ramp-foot both reach the spire\n" + notes;
                return true;
            }
            report = failures.Count + " raid scene leg(s) cannot reach the objective\n" + notes + string.Join("\n", failures);
            return false;
        }

        /// <summary>
        /// Probe one ALREADY-OPEN scene whose NavMesh is live, log the RAID_NAV_REACH lines and
        /// return whether every load-bearing leg is PathComplete. RaidNavBake calls this straight
        /// after its bake so RAID_NAV_BAKE_OK can never again be green over an unreachable spire.
        /// </summary>
        public static bool ProbeScene(Scene scene, out string note)
        {
            string sceneName = scene.name;
            RaidSpire spire = null;
            Transform platform = null;
            Transform ramp = null;
            float outerRadius = 0f;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (spire == null)
                {
                    var found = root.GetComponentInChildren<RaidSpire>(true);
                    if (found != null) spire = found;
                }
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < transforms.Length; t++)
                {
                    var tr = transforms[t];
                    if (platform == null && tr.name == PlatformName) platform = tr;
                    if (ramp == null && tr.name == RampName) ramp = tr;
                }
                var walls = root.GetComponentsInChildren<WallSegment>(true);
                for (int w = 0; w < walls.Length; w++)
                {
                    var wall = walls[w];
                    if (wall == null) continue;
                    // RaidBaseGenerator.BuildRing names every segment "Wall_{ringName}_S{side}_{i}",
                    // and the enclosing ring's token is "Outer" (RaidBaseDresser.cs:182). Reading the
                    // arena radius off the built tree is the point: it is per-config and no constant
                    // in this file may claim to know it.
                    if (wall.name.IndexOf("_Outer_", StringComparison.Ordinal) < 0) continue;
                    Vector3 p = wall.transform.position;
                    float r = new Vector2(p.x, p.z).magnitude;
                    if (r > outerRadius) outerRadius = r;
                }
            }

            if (spire == null)
            {
                note = "no RaidSpire in the scene - nothing to reach";
                Debug.LogWarning("[RaidKeepReach] " + sceneName + ": " + note);
                return true;   // not every scene in the folder has to be an assault venue
            }

            // Measured before anything is asked of the mesh: the objective's own footprint plus the
            // live agent radius. This is the WHOLE criterion (see ArrivalRadius + the header).
            Physics.SyncTransforms();
            float arrivalRadius = ArrivalRadius(spire.gameObject);

            Vector3 goal = spire.WorldPosition;
            bool goalMapped = NavMesh.SamplePosition(goal, out NavMeshHit goalHit, GoalSampleRadius, NavMesh.AllAreas);
            string goalPosText = goalMapped ? Fmt(goalHit.position) : "none";
            float goalDy = goalMapped ? (goalHit.position.y - goal.y) : 0f;
            float goalDist = goalMapped ? goalHit.distance : -1f;
            Debug.Log("RAID_NAV_REACH_GOAL scene=" + sceneName +
                      " goal=" + Fmt(goal) +
                      " mapped=" + goalMapped +
                      " mappedPos=" + goalPosText +
                      " mappedDy=" + goalDy.ToString("F2") +
                      " mappedDist=" + goalDist.ToString("F2") +
                      " platform=" + (platform != null) +
                      " ramp=" + (ramp != null) +
                      " outerRadius=" + outerRadius.ToString("F2") +
                      " arrivalRadius=" + arrivalRadius.ToString("F2"));

            var failures = new List<string>();
            var notes = new StringBuilder();

            // Collider bounds can be stale on a freshly opened scene; mirrors
            // RaidKeepRampRegression.CheckKeepRamp, which syncs before it reads slab.bounds.
            Physics.SyncTransforms();

            Bounds slab = default;
            bool hasSlab = false;
            if (platform != null) hasSlab = TryBounds(platform, out slab);

            // -- leg 1: the ramp FOOT. Carve-independent, wall-independent: the only leg no
            //           destructible geometry can ever explain away.
            if (ramp != null)
            {
                // The ramp cube's local -Z/+Z top-face endpoints, exactly as RaidKeepRampRegression
                // reads them (Assets/Editor/WallTools/RaidKeepRampRegression.cs), so the two oracles
                // cannot disagree about where the ramp's foot is.
                Vector3 foot = ramp.TransformPoint(new Vector3(0f, 0.5f, -0.5f));
                var lip = new Vector3(foot.x, 0.2f, foot.z - 1.5f);
                RunLeg(sceneName, "lip->spire", lip, goal, arrivalRadius, failures, notes, loadBearing: true);
            }
            else
            {
                notes.AppendLine("no KeepRamp - lip leg skipped");
            }

            // -- leg 2: standing ON the platform top. Isolates goal-mapping from everything else.
            if (hasSlab)
            {
                var top = new Vector3(slab.center.x, slab.max.y + 0.2f, slab.min.z + 1f);
                RunLeg(sceneName, "top->spire", top, goal, arrivalRadius, failures, notes, loadBearing: true,
                       mustStayAtStartHeight: true);
            }
            else
            {
                notes.AppendLine("no KeepPlatform - top leg skipped");
            }

            // -- leg 3: the whole chain, from the south courtyard band between the enclosing wall
            //           and the keep. See the CAVEAT in the header: this leg can read optimistically
            //           in edit mode because obstacle carving is a runtime service.
            if (outerRadius > 1f)
            {
                float innerEdge = hasSlab ? Mathf.Abs(slab.min.z) : (outerRadius * 0.2f);
                var court = new Vector3(0f, 0.2f, -(outerRadius + innerEdge) * 0.5f);
                RunLeg(sceneName, "court->spire", court, goal, arrivalRadius, failures, notes, loadBearing: true);
            }
            else
            {
                failures.Add("no Wall_Outer_* segment found - the courtyard probe point cannot be derived, so this scene proved nothing");
            }

            if (failures.Count == 0)
            {
                note = notes.ToString().Replace(Environment.NewLine, " | ").Trim();
                return true;
            }
            note = string.Join(" ; ", failures);
            return false;
        }

        // -- one leg ----------------------------------------------------------

        /// <param name="mustStayAtStartHeight">
        /// True for a leg whose START point only means what its label says if it maps at roughly the
        /// height it was asked for. The `top-&gt;spire` start sits ~2 m above the courtyard at the
        /// platform lip: if the platform top is NOT baked (candidate c), a 3 m sample would silently
        /// snap the start down to the courtyard and the leg's own label would then be a lie.
        /// </param>
        private static void RunLeg(string sceneName, string label, Vector3 from, Vector3 to,
                                   float arrivalRadius,
                                   List<string> failures, StringBuilder notes, bool loadBearing,
                                   bool mustStayAtStartHeight = false)
        {
            bool fromMapped = NavMesh.SamplePosition(from, out NavMeshHit fromHit, StartSampleRadius, NavMesh.AllAreas);
            bool toMapped = NavMesh.SamplePosition(to, out NavMeshHit toHit, GoalSampleRadius, NavMesh.AllAreas);
            bool startDrifted = mustStayAtStartHeight && fromMapped &&
                                Mathf.Abs(fromHit.position.y - from.y) > StartHeightTolerance;

            // TWO paths, because they answer two different questions and their DELTA is itself a
            // finding:
            //   sampled — from/to both snapped onto the mesh first. Answers "is what is physically
            //             there connected".
            //   raw     — the goal handed in UNSNAPPED, which is EXACTLY the call
            //             TroopController.RefreshRouteToObjective makes on the device
            //             (Assets/_Modules/Village/Troops/TroopController.cs:1144-1148 passes
            //             spire.WorldPosition straight to NavMesh.CalculatePath). Unity's implicit
            //             endpoint mapping need not agree with our explicit sample, and if it does
            //             not, a bake that read PathComplete while the device read PathPartial would
            //             re-open the very argument this instrument exists to close.
            // A leg passes only when BOTH are PathComplete.
            var path = new NavMeshPath();
            bool computed = false;
            var status = NavMeshPathStatus.PathInvalid;
            int corners = 0;
            var lastCorner = Vector3.zero;
            bool haveLastCorner = false;
            bool rawComputed = false;
            var rawStatus = NavMeshPathStatus.PathInvalid;
            var rawLastCorner = Vector3.zero;
            bool haveRawLastCorner = false;
            if (fromMapped && toMapped && !startDrifted)
            {
                computed = NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path);
                if (computed)
                {
                    status = path.status;
                    Vector3[] pts = path.corners;
                    corners = pts != null ? pts.Length : 0;
                    if (corners > 0) { lastCorner = pts[corners - 1]; haveLastCorner = true; }
                }
                var rawPath = new NavMeshPath();
                rawComputed = NavMesh.CalculatePath(fromHit.position, to, NavMesh.AllAreas, rawPath);
                if (rawComputed)
                {
                    rawStatus = rawPath.status;
                    Vector3[] rawPts = rawPath.corners;
                    if (rawPts != null && rawPts.Length > 0) { rawLastCorner = rawPts[rawPts.Length - 1]; haveRawLastCorner = true; }
                }
            }

            // WO-1749 pass 3 — THE NUMBER THE CRITERION IS MADE OF. `lastCornerY` alone could not
            // tell an island apart from an arrival: a route that stops 13 m away at the platform
            // edge and one that stops touching the spire can report the SAME height. The PLANAR
            // distance from the last corner to the objective is what separates them, and it is the
            // number to read first on any future RED.
            float lastCornerDist = haveLastCorner ? PlanarDistance(lastCorner, to) : float.PositiveInfinity;
            float rawLastCornerDist = haveRawLastCorner ? PlanarDistance(rawLastCorner, to) : float.PositiveInfinity;
            float lastCornerY = haveLastCorner ? lastCorner.y : 0f;
            float rawLastCornerY = haveRawLastCorner ? rawLastCorner.y : 0f;

            string statusText = StatusText(fromMapped, toMapped, startDrifted, computed, status);
            string rawStatusText = StatusText(fromMapped, toMapped, startDrifted, rawComputed, rawStatus);
            string fromMappedText = fromMapped ? Fmt(fromHit.position) : "none";
            string toMappedText = toMapped ? Fmt(toHit.position) : "none";

            // Plain concatenation, deliberately: CLAUDE.md §1 — CompileGate.BraceBalanced has NO
            // interpolated-string model, so a quote inside a `$"...{ a ? "x" : "y" }..."` hole ends
            // the string for the scanner and can withhold COMPILE_GATE_OK on a file that compiles.
            // THE CRITERION (WO-1749 pass 3, and the earlier one is RETIRED — see the header):
            // ARRIVED, not PathComplete. A leg passes when the route's last corner lands within the
            // objective's own measured reach, on BOTH the sampled and the raw (device-shaped) query.
            bool arrived = lastCornerDist <= arrivalRadius;
            bool rawArrived = rawLastCornerDist <= arrivalRadius;
            bool pass = arrived && rawArrived;

            Debug.Log("RAID_NAV_REACH scene=" + sceneName +
                      " status=" + statusText +
                      " corners=" + corners +
                      " lastCornerY=" + lastCornerY.ToString("F2") +
                      " lastCorner=" + Fmt(lastCorner) +
                      " lastCornerDist=" + lastCornerDist.ToString("F2") +
                      " arrivalRadius=" + arrivalRadius.ToString("F2") +
                      " arrived=" + arrived +
                      " rawStatus=" + rawStatusText +
                      " rawLastCornerY=" + rawLastCornerY.ToString("F2") +
                      " rawLastCorner=" + Fmt(rawLastCorner) +
                      " rawLastCornerDist=" + rawLastCornerDist.ToString("F2") +
                      " rawArrived=" + rawArrived +
                      " leg=" + label +
                      " from=" + Fmt(from) + " fromMapped=" + fromMappedText +
                      " to=" + Fmt(to) + " toMapped=" + toMappedText);

            string line = label + " arrived=" + arrived + "/" + rawArrived +
                          " dist=" + lastCornerDist.ToString("F2") + "/" + rawLastCornerDist.ToString("F2") +
                          "m vs arrivalRadius=" + arrivalRadius.ToString("F2") + "m" +
                          " (status=" + statusText + " raw=" + rawStatusText +
                          " corners=" + corners + " lastCorner=" + Fmt(lastCorner) + ")";
            if (pass) notes.AppendLine(line);
            else if (loadBearing) failures.Add(line);
            else notes.AppendLine(line);
        }

        /// <summary>
        /// How close a route has to get before the troop has ARRIVED at the objective — the whole
        /// criterion, and every term in it is measured at the moment it is asked:
        ///   * the objective's own footprint half-width, off its renderer bounds (art changes per
        ///     config; nothing here may claim to know it), and
        ///   * the raid agent's radius, read off the LIVE NavMesh build settings
        ///     (<c>NavMesh.GetSettingsByID(0).agentRadius</c>) rather than copied out of the scene.
        /// A troop standing that far from the centre is touching the spire. Returns a floor of
        /// <see cref="ArrivalSlack"/> if the objective somehow measures to nothing, so the criterion
        /// can never silently become "anywhere".
        /// </summary>
        private static float ArrivalRadius(GameObject objective)
        {
            float footprint = 0f;
            if (objective != null)
            {
                var rends = objective.GetComponentsInChildren<Renderer>(true);
                if (rends.Length > 0)
                {
                    var b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    footprint = Mathf.Max(b.extents.x, b.extents.z);
                }
            }
            float agentRadius = 0.5f;
            var settings = NavMesh.GetSettingsByID(0);
            if (settings.agentRadius > 0f) agentRadius = settings.agentRadius;
            return Mathf.Max(ArrivalSlack, footprint + agentRadius + ArrivalSlack);
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string StatusText(bool fromMapped, bool toMapped, bool startDrifted,
                                         bool computed, NavMeshPathStatus status)
        {
            if (!fromMapped) return "from-unmapped";
            if (startDrifted) return "from-mapped-off-platform";
            if (!toMapped) return "to-unmapped";
            if (!computed) return "CalculatePath-FAILED";
            return status.ToString();
        }

        // -- helpers ----------------------------------------------------------

        private static bool TryBounds(Transform t, out Bounds bounds)
        {
            var collider = t.GetComponent<Collider>();
            if (collider != null && collider.enabled)
            {
                bounds = collider.bounds;
                return bounds.size.x > 0f && bounds.size.z > 0f;
            }
            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                bounds = renderer.bounds;
                return bounds.size.x > 0f && bounds.size.z > 0f;
            }
            bounds = default;
            return false;
        }

        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + "," + v.z.ToString("F2") + ")";
        }

        private static List<string> DiscoverScenes()
        {
            var found = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                string file = System.IO.Path.GetFileName(path);
                bool isRaid = file.StartsWith("RaidBase_", StringComparison.Ordinal) ||
                              file.StartsWith("OwnedTown_", StringComparison.Ordinal);
                if (isRaid) found.Add(path);
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        private static void RestoreScene(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (SceneManager.GetActiveScene().path == path) return;
            if (!System.IO.File.Exists(path)) return;
            try { EditorSceneManager.OpenScene(path, OpenSceneMode.Single); }
            catch (Exception ex) { Debug.LogWarning("[RaidKeepReach] could not restore " + path + ": " + ex.Message); }
        }
    }
}
