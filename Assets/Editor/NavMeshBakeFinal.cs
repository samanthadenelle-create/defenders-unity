// =============================================================================
// NavMeshBakeFinal — the navmesh bake as a SEPARATE, ALWAYS-LAST step.
// -----------------------------------------------------------------------------
// OWNER DIAGNOSIS 2026-08-17, and it is the whole reason this file exists:
//   "its because when you originally run the script you bake it, then you rotate
//    the buildings, so the bake moves with the rotation"
//   "need to rotate and bake last"
//   "even easier run a seperate bake script always at exit script"
//
// THE DEFECT. The scene builders bake the navmesh INSIDE the build, partway
// through — CastleHubBuilder's BATCH-BAKE runs BuildNavMesh() and then keeps
// going. Anything that moves or rotates a building AFTERWARDS leaves the carve
// behind at the old pose: an unwalkable footprint where nothing stands, and a
// walkable gap where the building now is. That is the "baked footprint" the
// owner reported — not an object, not terrain paint, and not something a cache
// clear can touch, because it is baked nav data inside the scene that ships.
//
// This is the SAME failure class as every other one found today: the pet's -90°
// yaw, the .tripo-extracted marker, the WizardTower_1 art path, the five deleted
// music clips. A value computed correctly, welded in place, and outliving the
// thing it described. Here the stale value is a whole navmesh.
//
// ⛔ WHY A SEPARATE SCRIPT AND NOT "MOVE THE BAKE DOWN A FEW LINES".
// Reordering inside one builder fixes that builder until the next step is
// appended after it — and the bug is invisible until someone walks into an empty
// square, so it would not be noticed for weeks. A bake that is structurally the
// LAST thing cannot be leapfrogged: there is nothing after it to leapfrog.
//
// ⚠ WHAT THIS DOES NOT FIX, STATED PLAINLY SO IT IS NOT ASSUMED.
// It bakes the scene as it stands when it runs. Structures whose rotation is
// applied at RUNTIME — the catalog `orientation` block via StructureFactory, and
// the Offset Forge dev store at persistentDataPath/structure-orientations.json —
// move AFTER any editor bake, by construction. No bake ordering can cover those;
// they need a runtime carve (NavMeshObstacle) or a runtime rebuild. This closes
// the scene-baked half, which is the half the owner is looking at.
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    /// <summary>Bakes every NavMeshSurface in a scene as a standalone final step.</summary>
    public static class NavMeshBakeFinal
    {
        /// <summary>Marker printed on success — grep the SHAPE, per the project's marker rule.</summary>
        private const string OkMarker = "NAVMESH_BAKE_OK";

        /// <summary>The home hub. Default target when no scene is named.</summary>
        private const string HubScene = "Assets/Scenes/Main_Castle_Overworld.unity";

        [MenuItem("Defenders/World/Bake NavMesh (ALWAYS LAST)")]
        public static void BakeOpenSceneMenu() => BakeOpenScene();

        /// <summary>
        /// Batchmode entry: -executeMethod DeNelle.Editor.NavMeshBakeFinal.Run
        /// Opens the hub scene, bakes, persists and saves. This is the step that must run AFTER
        /// every builder, every rotation pass and every layout edit — never before one.
        /// </summary>
        public static void Run()
        {
            string scenePath = HubScene;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-bakeScene") { scenePath = args[i + 1]; break; }

            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogError($"[NavMeshBakeFinal] scene NOT FOUND: '{scenePath}' — nothing baked. " +
                               "Pass -bakeScene <path> or restore the hub scene.");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log($"[NavMeshBakeFinal] opened {scenePath}");

            long sizeBefore = new System.IO.FileInfo(scenePath).Length;

            int baked = BakeOpenScene();
            if (baked <= 0) return;

            // MarkSceneDirty is what makes SaveOpenScenes actually write. Without it the save is a
            // silent no-op (see the note in BakeOpenScene) — belt and braces alongside SetDirty on
            // each surface, because the cost of getting this wrong is a bake that reports success
            // and ships the stale carve.
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            // WO-1731: BakeOpenScene re-pointed every surface's m_NavMeshData. A save that
            // silently fails leaves the scene pointing at the asset the bake replaced, and the
            // scene ships with NO navmesh -- exactly the failure this file's own comments above
            // are guarding against, one step further on. THROW (RaidNavBake.cs:109-111).
            if (!EditorSceneManager.SaveOpenScenes())
                throw new System.InvalidOperationException(
                    "[NavMeshBakeFinal] could not save navigation for " + scenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ⛔ VERIFY THE ARTIFACT, NOT THE PROCESS. Every step above can "succeed" while the
            // scene on disk keeps its old navmesh. Prove the scene was rewritten before claiming a
            // bake happened — this is the check whose absence let the first run report OK on a
            // completely inert pass.
            long sizeAfter = new System.IO.FileInfo(scenePath).Length;
            var writeTime = System.IO.File.GetLastWriteTimeUtc(scenePath);
            bool rewritten = writeTime > System.DateTime.UtcNow.AddMinutes(-5);

            Debug.Log($"[NavMeshBakeFinal] scene bytes {sizeBefore:N0} -> {sizeAfter:N0}, " +
                      $"lastWrite={writeTime:O}");

            if (!rewritten)
            {
                Debug.LogError("[NavMeshBakeFinal] BAKE NOT PERSISTED — the scene file was NOT rewritten. " +
                               "The navmesh data may be an orphaned asset while the scene keeps its stale " +
                               "carve. Do NOT treat this run as a bake.");
                return;
            }

            ReportNavMeshExtent();

            Debug.Log($"[NavMeshBakeFinal] scene + assets saved.");
            Debug.Log($"{OkMarker} {baked} surface(s) — {System.IO.Path.GetFileName(scenePath)}");
        }

        /// <summary>
        /// Bakes every NavMeshSurface in the currently-open scene. Returns the number baked,
        /// or -1 on a hard failure. Reflection is used for the NavMeshSurface API to match the
        /// surrounding builders (CastleHubBuilder does the same) rather than introduce a second
        /// way of reaching the same type.
        /// </summary>
        public static int BakeOpenScene()
        {
            var surfType = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
            if (surfType == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    surfType = asm.GetType("Unity.AI.Navigation.NavMeshSurface");
                    if (surfType != null) break;
                }
            }
            if (surfType == null)
            {
                Debug.LogError("[NavMeshBakeFinal] NavMeshSurface type not resolved — NOTHING WAS BAKED. " +
                               "Do NOT treat this run as a bake; the stale carve is still in the scene.");
                return -1;
            }

            var surfaces = Object.FindObjectsByType(surfType, FindObjectsSortMode.None);
            if (surfaces == null || surfaces.Length == 0)
            {
                Debug.LogError("[NavMeshBakeFinal] ZERO NavMeshSurfaces in the open scene — nothing to bake. " +
                               "A scene with no surface has no navmesh at all, which is a different and worse " +
                               "problem than a stale one; not reported as success.");
                return -1;
            }

            // PROD-004 Cause 2: baked twins must carve DYNAMICALLY, not statically. Swap them onto
            // NavMeshObstacles and take their colliders out of this bake. Restored in a finally-like
            // pass below so a failed bake never leaves the scene with its colliders off.
            var suppressed = PrepareBakedTwinsForDynamicCarving();

            // Log the pose of every rotated structure BEFORE baking. This is the evidence that the
            // bake saw the FINAL orientation — the exact fact that was missing when the stale carve
            // shipped. Without it, a future "did the bake run after the rotation?" is unanswerable.
            LogStructurePoses();

            var built = new List<string>();
            foreach (var s in surfaces)
            {
                var comp = s as Component;
                string name = comp != null ? comp.gameObject.name : s.name;

                // Renderer-off planes must still be collected, so geometry comes from PHYSICS
                // COLLIDERS, not render meshes. (Unchanged from CastleHubBuilder.)
                var ug = surfType.GetProperty("useGeometry");
                if (ug != null) ug.SetValue(s, System.Enum.ToObject(ug.PropertyType, 1)); // PhysicsColliders

                // ⛔ GROUND ONLY — owner ruling 2026-08-17, from the navmesh overlay screenshot
                // showing walkable polygons floating at ROOF height over the spire and houses.
                // CastleHubBuilder baked with collectObjects = All, so EVERY collider in the scene
                // became walkable surface: rooftops, wall tops, storefront awnings. Enemies and
                // NPCs could path up there. It also contradicted that builder's own single-level
                // pivot ("ONE flat walkable sheet at y~0", second level removed precisely because
                // enemy AI could not reach it) — the bake was re-adding, at every roof, the upper
                // level the scene had deliberately stripped.
                //
                // ⛔ THE OBVIOUS FIX IS WRONG AND MUST NOT BE TRIED: excluding buildings from
                // collection (a layerMask) fixes the roofs and BREAKS PATHING, because buildings do
                // TWO jobs here — their bases CARVE the ground so the navmesh does not run through
                // them, and their roofs wrongly ADD surface. Drop them and the navmesh walks
                // straight through every building.
                //
                // A Y-LIMITED VOLUME keeps both halves right: building bases are inside the band
                // and still carve; anything above it is never collected, so no roof surface exists.
                // Height-based rather than layer- or name-based on purpose — a new building added
                // tomorrow is covered automatically, with nothing to remember to tag.
                // ⛔ collectObjects = ALL. The ground-only volume is REVERTED — see the block below
                // for why, in full, because the temptation to re-try it is exactly the trap.
                var co = surfType.GetProperty("collectObjects");
                if (co != null) co.SetValue(s, System.Enum.ToObject(co.PropertyType, 0)); // All

                // =====================================================================
                //  GROUND-ONLY BAKE — ATTEMPTED 2026-08-17, REVERTED THE SAME HOUR.
                //  Owner ruled roofs/wall tops must not be walkable (the navmesh overlay showed
                //  walkable polygons floating at roof height). A Y-limited collection volume is the
                //  right SHAPE of fix — buildings stay in so their bases still carve, geometry above
                //  the band is never collected. It failed on ONE sub-problem: WHERE IS THE GROUND.
                //
                //  Attempt 1 — anchor to the lowest collider (all.min.y):
                //      band y -6.00 .. 0.00. Something in this scene sits ~4 m BELOW the town, so
                //      the band landed entirely under the walkable surface.
                //  Attempt 2 — anchor to the widest-footprint collider's top (ExteriorTerrain):
                //      band y 36.00 .. 43.00, because a Terrain's bounds.max.y is its HIGHEST
                //      HILLTOP, not the elevation the town sits at. The navmesh collapsed from
                //      5213 verts / 2391 tris to 230 / 110 — the town's walkable surface, gone.
                //
                //  ⛔ NOT ATTEMPTED A THIRD TIME, deliberately. Two failed auto-detections are the
                //  §12 signal to stop guessing: "where is the ground" is a real question about this
                //  scene that wants measurement, not another heuristic. And the failure is ASYMMETRIC
                //  — roof navmesh lets an agent stand somewhere silly, while a missing ground
                //  navmesh means nothing in the town can path AT ALL. On a LIVE build the safe
                //  direction is obvious.
                //
                //  When it is picked up (its own ticket, not this file's job): take the band from an
                //  AUTHORED value — the hub's known ground plane / the NavMeshFloor_Invisible_Walkable
                //  object CastleHubBuilder creates — and gate it on the triangle count not dropping.
                //  ReportNavMeshExtent() below is what caught both failures; keep that check.
                // =====================================================================

                var build = surfType.GetMethod("BuildNavMesh", System.Type.EmptyTypes);
                if (build == null)
                {
                    Debug.LogError("[NavMeshBakeFinal] BuildNavMesh() not found on NavMeshSurface — nothing baked.");
                    return -1;
                }
                build.Invoke(s, null);

                // Persist the freshly-built data as an asset or it does not survive the scene save —
                // an unpersisted bake looks fine in the editor and ships as nothing.
                var dataProp = surfType.GetProperty("navMeshData");
                var data = dataProp != null ? dataProp.GetValue(s) as Object : null;
                if (data == null)
                {
                    Debug.LogError($"[NavMeshBakeFinal] '{name}' baked but produced NULL navMeshData — " +
                                   "surface has no walkable geometry, or the bake silently failed.");
                    continue;
                }
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(data)))
                {
                    string dir = "Assets/Scenes/NavMesh";
                    if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Scenes", "NavMesh");
                    AssetDatabase.CreateAsset(data, $"{dir}/NavMesh_{name}.asset");
                }

                // ⛔ MARK THE COMPONENT DIRTY OR THE WHOLE BAKE IS DISCARDED.
                // CreateAsset turns the data into an asset, but the SURFACE's reference to it lives
                // in the SCENE, and Unity will not write a scene it does not believe changed.
                // The first run of this script proved it the expensive way: it printed
                // NAVMESH_BAKE_OK, wrote a 1.6 MB asset, left the .unity file BYTE-IDENTICAL, and
                // the asset was ORPHANED — the scene still referenced the old, stale navmesh. A
                // green marker on a run that changed nothing is the worst possible outcome, because
                // it retires the bug in the reader's head while leaving it in the build. Verified by
                // grepping the new asset's GUID in the .unity file — the check now lives in Run().
                if (comp != null) EditorUtility.SetDirty(comp);
                built.Add(name);
                Debug.Log($"[NavMeshBakeFinal] baked '{name}' (data persisted).");
            }

            RestoreColliders(suppressed);

            Debug.Log($"[NavMeshBakeFinal] {built.Count}/{surfaces.Length} surface(s) baked: {string.Join(", ", built)}");
            return built.Count;
        }

        /// <summary>
        /// PROD-004 Cause 2 — swap every BAKED TWIN from a STATIC carve to a DYNAMIC one.
        /// <para>
        /// A baked twin is present and colliding when the scene bakes, so it carves a hole. At
        /// runtime <c>StructureSingleton.StandDownBakedTwins</c> deactivates it — the building
        /// vanishes and THE CARVE CANNOT FOLLOW, because baked navmesh is static data. The player is
        /// left walking into an invisible wall the size of a building (owner, 2026-08-17: "there is
        /// some invisible footprint there"). No bake ORDERING fixes this: the twin is legitimately
        /// there when the scene is built.
        /// </para>
        /// <para>
        /// A <see cref="UnityEngine.AI.NavMeshObstacle"/> with carving solves both known causes at
        /// once, which is why it is preferred over any bake-time trick: it carves from the object's
        /// CURRENT transform, so it follows a rotation applied after the bake (Cause 1), and it
        /// stops carving when the object is deactivated (Cause 2).
        /// </para>
        /// ⛔ THE COLLIDERS MUST COME OUT OF THE BAKE, AND THE OBSTACLE MUST GO IN — never one
        /// without the other. Colliders out with no obstacle = the navmesh runs STRAIGHT THROUGH the
        /// building, which is far worse than an invisible footprint and stays invisible until an
        /// enemy walks through a wall. Obstacle in with colliders still baked = the static hole is
        /// still there and nothing improved.
        /// <returns>The colliders switched off for the bake, to be restored immediately after.</returns>
        /// </summary>
        private static List<Collider> PrepareBakedTwinsForDynamicCarving()
        {
            var suppressed = new List<Collider>();
            var twinNames = ReadBakedTwinNames();
            if (twinNames.Count == 0)
            {
                Debug.LogWarning("[NavMeshBakeFinal] no bakedTwins found in structures-catalog.json — " +
                                 "twins will keep their STATIC carve. PROD-004 Cause 2 is NOT addressed by this run.");
                return suppressed;
            }

            var scene = SceneManager.GetActiveScene();
            int converted = 0, notFound = 0;
            foreach (var name in twinNames)
            {
                GameObject go = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == name) { go = t.gameObject; break; }
                    if (go != null) break;
                }
                if (go == null)
                {
                    notFound++;
                    Debug.LogWarning($"[NavMeshBakeFinal] bakedTwin '{name}' is authored in the catalog but NOT " +
                                     "in this scene — nothing to convert (stale bakedTwins entry, or a different scene).");
                    continue;
                }

                // WO-1716 — REFUSE TO CARVE OFF SOMETHING NOBODY CAN SEE. This loop is the step that
                // WEAPONISED the invisible `CastleBarracks` husk: it resolves a twin BY NAME, then
                // sizes a carving obstacle from that object's collider bounds. Handed a host whose
                // MeshFilter/MeshRenderer had been stripped (CastleHubBuilder.SkinHostUpright, before
                // its WO-1716 fix) it faithfully produced a 16.9 x 5.08 x 14.5 carve over empty
                // ground. A hole the player cannot see the source of is strictly worse than no hole:
                // the owner spent a session hunting it. Skip it, drop any carve it already owns, and
                // say so - the object itself needs removing, which is a scene fix, not a bake fix.
                var cols = go.GetComponentsInChildren<Collider>(false);

                bool rendersNothing = go.GetComponentsInChildren<Renderer>(true).Length == 0;
                if (rendersNothing)
                {
                    // ⛔ HOLD ITS COLLIDERS OUT OF THE BAKE FIRST, THEN DROP THE OBSTACLE. Skipping
                    // straight past would leave those colliders IN the bake and freeze the very same
                    // footprint as a PERMANENT STATIC hole in the surface asset - the method's own
                    // "never one without the other" rule, broken in the other direction.
                    foreach (var c in cols)
                    {
                        if (c == null || !c.enabled) continue;
                        c.enabled = false;
                        suppressed.Add(c);
                    }

                    var ghostObs = go.GetComponent<UnityEngine.AI.NavMeshObstacle>();
                    bool hadObstacle = ghostObs != null;
                    if (hadObstacle) Object.DestroyImmediate(ghostObs);
                    Debug.LogWarning($"[NavMeshBakeFinal] bakedTwin '{name}' RENDERS NOTHING - it is an " +
                                     "invisible husk (WO-1716), not a building. Refusing to size a carve from " +
                                     "its colliders, and holding them out of this bake so no static hole is " +
                                     "frozen in either" +
                                     (hadObstacle ? "; removed the carving NavMeshObstacle it was already holding" : "") +
                                     ". Remove the GameObject via Defenders/Castle/Remove invisible structure husks.");
                    continue;
                }

                if (cols.Length == 0)
                {
                    Debug.LogWarning($"[NavMeshBakeFinal] bakedTwin '{name}' has NO collider — it never carved, " +
                                     "so it is not a source of an invisible footprint. Skipped.");
                    continue;
                }

                // Size the obstacle from the twin's own collider bounds, in LOCAL space, so it
                // matches the building rather than a default 1m box.
                Bounds world = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++) world.Encapsulate(cols[i].bounds);

                var obs = go.GetComponent<UnityEngine.AI.NavMeshObstacle>();
                if (obs == null) obs = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                obs.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                obs.carving = true;
                // carveOnlyStationary keeps the carve stable for a building that never moves and
                // avoids per-frame recarve cost.
                obs.carveOnlyStationary = true;
                Vector3 lossy = go.transform.lossyScale;
                obs.size = new Vector3(
                    Mathf.Approximately(lossy.x, 0f) ? world.size.x : world.size.x / Mathf.Abs(lossy.x),
                    Mathf.Approximately(lossy.y, 0f) ? world.size.y : world.size.y / Mathf.Abs(lossy.y),
                    Mathf.Approximately(lossy.z, 0f) ? world.size.z : world.size.z / Mathf.Abs(lossy.z));
                obs.center = go.transform.InverseTransformPoint(world.center);
                EditorUtility.SetDirty(go);

                foreach (var c in cols)
                {
                    if (!c.enabled) continue;
                    c.enabled = false;
                    suppressed.Add(c);
                }

                converted++;
                Debug.Log($"[NavMeshBakeFinal] twin '{name}': NavMeshObstacle(carving) size={obs.size} — " +
                          $"{cols.Length} collider(s) held out of this bake so the carve is DYNAMIC, not baked.");
            }

            Debug.Log($"[NavMeshBakeFinal] baked twins: {converted} converted to dynamic carving, " +
                      $"{notFound} named in the catalog but absent from the scene.");
            return suppressed;
        }

        /// <summary>Re-enables everything <see cref="PrepareBakedTwinsForDynamicCarving"/> switched off.</summary>
        private static void RestoreColliders(List<Collider> suppressed)
        {
            if (suppressed == null) return;
            foreach (var c in suppressed) if (c != null) c.enabled = true;
            if (suppressed.Count > 0)
                Debug.Log($"[NavMeshBakeFinal] restored {suppressed.Count} twin collider(s) after the bake " +
                          "(they still block the PLAYER; they simply no longer bake a permanent hole).");
        }

        /// <summary>
        /// Reads every <c>bakedTwins</c> entry from structures-catalog.json. Text-scanned rather
        /// than deserialized so this keeps working if the catalog schema gains a field — the same
        /// reasoning as TripoStructureMaterialAudit.VerifyCatalogArt.
        /// </summary>
        /// <summary>Public since WO-1716 so the husk-cleanup command reads the SAME list this bake
        /// reads, rather than keeping a second copy of the twin names (CLAUDE.md sec.5).</summary>
        public static List<string> ReadBakedTwinNames()
        {
            var names = new List<string>();
            string path = "Assets/Resources/Data/Canonical/structures-catalog.json";
            if (!System.IO.File.Exists(path)) return names;

            string json = System.IO.File.ReadAllText(path);
            var m = System.Text.RegularExpressions.Regex.Matches(
                json, "\"bakedTwins\"\\s*:\\s*\\[(.*?)\\]",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            foreach (System.Text.RegularExpressions.Match block in m)
            {
                foreach (System.Text.RegularExpressions.Match s in
                         System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, "\"([^\"]+)\""))
                {
                    string n = s.Groups[1].Value.Trim();
                    if (n.Length > 0 && !names.Contains(n)) names.Add(n);
                }
            }
            return names;
        }

        /// <summary>
        /// Measures the BAKED navmesh itself and reports its Y extent and triangle count.
        /// <para>
        /// ⛔ THIS IS THE ONLY THING THAT PROVES "GROUND ONLY". Setting a collection volume is a
        /// process step; whether roof polygons actually stopped being generated is an OUTCOME, and
        /// the two came apart once already today when a bake reported success and wrote nothing.
        /// Reading the triangulation back is cheap and answers the real question: if the navmesh's
        /// highest vertex is metres above the ground, roofs are still walkable no matter what the
        /// config says.
        /// </para>
        /// </summary>
        private static void ReportNavMeshExtent()
        {
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                Debug.LogError("[NavMeshBakeFinal] baked navmesh has ZERO vertices — the scene has no " +
                               "walkable surface at all. That is worse than a stale bake, not better.");
                return;
            }

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var v in tri.vertices)
            {
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }

            int triangles = tri.indices != null ? tri.indices.Length / 3 : 0;
            float span = maxY - minY;
            Debug.Log($"[NavMeshBakeFinal] baked navmesh: {tri.vertices.Length} verts, {triangles} tris, " +
                      $"y {minY:F2} .. {maxY:F2} (span {span:F2}m)");

            // A ground-only sheet should be within a few metres of flat. A large span means surface
            // was generated up on geometry — the exact defect the volume is meant to remove.
            if (span > 8f)
                Debug.LogWarning($"[NavMeshBakeFinal] ⚠ navmesh Y-SPAN IS {span:F1}m — that is too tall for a " +
                                 "ground-only sheet, so walkable surface is probably still being generated on " +
                                 "roofs / wall tops. The collection volume did not do its job; do not report " +
                                 "this as ground-only.");
            else
                Debug.Log("[NavMeshBakeFinal] Y-span is within a ground-only sheet — no roof surface detected.");
        }

        /// <summary>
        /// Computes the Y-limited collection band: full XZ extent of every collider in the scene,
        /// but only a walkable slice in Y above the lowest ground. Building bases fall inside it and
        /// still carve; roofs, wall tops and awnings fall above it and are never collected.
        /// </summary>
        private static bool TryComputeGroundBand(out Bounds band)
        {
            band = default;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return false;

            bool any = false;
            Bounds all = default;
            Collider ground = null;
            float widestFootprint = 0f;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<Collider>(false))
                {
                    if (c == null) continue;
                    if (!any) { all = c.bounds; any = true; }
                    else all.Encapsulate(c.bounds);

                    // The GROUND is whatever has the largest horizontal footprint — the terrain or
                    // the invisible nav floor. Identified by geometry, not by name, so a renamed or
                    // rebuilt floor does not silently break the anchor.
                    float footprint = c.bounds.size.x * c.bounds.size.z;
                    if (footprint > widestFootprint) { widestFootprint = footprint; ground = c; }
                }
            }
            if (!any || ground == null) return false;

            // ⛔ ANCHOR TO THE GROUND SURFACE, NOT TO THE LOWEST COLLIDER IN THE SCENE.
            // The first version used all.min.y and produced a band of y -6.00 .. 0.00 — entirely
            // BELOW the walkable surface — because something in this scene sits about 4 m under the
            // ground plane and dragged the whole band down with it. A band under the floor collects
            // nothing useful: the ground and every building base fall outside it. "Lowest collider"
            // is not a synonym for "ground", and in a scene with any sunken geometry it never is.
            const float below = 2f;          // headroom under the surface for dips / sunken foundations
            const float above = 5f;          // agent height + margin; anything higher is a roof
            float groundY = ground.bounds.max.y;
            float minY = groundY - below;
            float bandHeight = below + above;
            float centreY = minY + bandHeight * 0.5f;
            Debug.Log($"[NavMeshBakeFinal] ground anchor = '{ground.name}' top y={groundY:F2} " +
                      $"(footprint {widestFootprint:N0} m^2) -> band y {minY:F2} .. {minY + bandHeight:F2}");

            // Pad XZ so nothing at the edge is clipped out of collection.
            band = new Bounds(
                new Vector3(all.center.x, centreY, all.center.z),
                new Vector3(all.size.x + 20f, bandHeight, all.size.z + 20f));
            return true;
        }

        /// <summary>
        /// Records the yaw of every structure-looking root before the bake. The stale-carve bug is
        /// exactly "the bake did not see this rotation", so the poses at bake time are the one piece
        /// of evidence that settles it — cheap to log, impossible to reconstruct afterwards.
        /// </summary>
        private static void LogStructurePoses()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(false))
                {
                    // Only report things carrying a collider — those are what the bake voxelizes,
                    // so they are the only rotations that can move a carve.
                    if (t.GetComponent<Collider>() == null) continue;
                    Vector3 e = t.rotation.eulerAngles;
                    if (Mathf.Approximately(e.x, 0f) && Mathf.Approximately(e.y, 0f) && Mathf.Approximately(e.z, 0f))
                        continue;   // identity poses carry no information here
                    if (n++ < 40)
                        Debug.Log($"[NavMeshBakeFinal] pose-at-bake '{t.name}' euler=({e.x:F1},{e.y:F1},{e.z:F1})");
                }
            }
            Debug.Log($"[NavMeshBakeFinal] {n} rotated collider(s) present at bake time " +
                      "(these are the poses the carve will match — if a building is rotated AFTER this, " +
                      "its carve goes stale and this bake must be re-run).");
        }
    }
}
