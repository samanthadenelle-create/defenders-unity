// =============================================================================
// CastleWallsFromRecipe — DELETE + COMPLETELY RECREATE the castle wall sides
// from the captured recipe, reproducibly.
//
// THE REAL SOLUTION (owner, 2026-06-12): the castle walls are no longer a fragile
// hand-dialed scene — they're data (castle-south-recipe.json, captured by
// CastleOffsetCapture from the owner's authored CastleSide_South). This builder
// reads that recipe, deletes the four existing CastleSide_* groups, rebuilds the
// SOUTH side from the recipe, and mirrors it to West/North/East (90/180/270
// around world origin — same as CastleSideMirror). So a regen reproduces the
// OWNER's layout instead of reverting it. Re-runnable (idempotent).
//
// SAFE: only touches the four CastleSide_* wall groups (NOT the keep/interior/
// hero/camera). Additive prefab instances under Undo; writes no scene itself
// (owner saves + re-bakes navmesh when happy).
// =============================================================================
using System;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class CastleWallsFromRecipe
    {
        private const string PolyRoot  = "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Medieval_M/";
        private const string SouthName = "CastleSide_South";

        // WO-449: dedicated physics layer for castle WALL geometry so HeroTargetIndicator's
        // line-of-sight linecast can be masked to walls only — NOT the hero/ground (Default)
        // or enemies (Enemy). Walls share Default by default, which would self-block the hero;
        // putting them on "Structure" lets the LoS mask occlude targeting through walls cleanly.
        private const string StructureLayerName = "Structure";
        private static readonly (float angle, string label)[] Sides =
            { (90f, "West"), (180f, "North"), (270f, "East") };

        [Serializable] private class Piece  { public string name; public string prefab; public float[] pos; public float[] rot; public float[] scale; }
        [Serializable] private class Recipe { public Piece[] pieces; public float[] parentPos; public float[] parentRot; }

        [MenuItem("Defenders/Castle/Recreate Walls from Recipe (delete + rebuild + mirror)")]
        public static void Recreate()
        {
            var ta = Resources.Load<TextAsset>("Data/castle-south-recipe");
            if (ta == null) { Debug.LogError("[CastleWallsFromRecipe] recipe not found (Resources/Data/castle-south-recipe). Run Capture South Side first."); return; }
            var recipe = JsonUtility.FromJson<Recipe>(ta.text);
            if (recipe == null || recipe.pieces == null || recipe.pieces.Length == 0) { Debug.LogError("[CastleWallsFromRecipe] recipe empty / failed to parse."); return; }

            // Delete the four existing sides for a clean recreate.
            foreach (var n in new[] { SouthName, "CastleSide_West", "CastleSide_North", "CastleSide_East" })
            {
                var ex = GameObject.Find(n);
                if (ex != null) { Undo.DestroyObjectImmediate(ex); Debug.Log("[CastleWallsFromRecipe] removed " + n); }
            }

            // Rebuild the SOUTH side from the recipe.
            var parent = new GameObject(SouthName);
            Undo.RegisterCreatedObjectUndo(parent, "Recreate Castle Walls");
            parent.transform.position    = V(recipe.parentPos) + Vector3.up * CastleHubBuilder.CastleFootprintLiftY;   // WO-593 island raise (mirror preserves Y)
            parent.transform.eulerAngles = V(recipe.parentRot);

            int placed = 0;
            foreach (var piece in recipe.pieces)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PolyRoot + piece.prefab + ".prefab");
                if (prefab == null) { Debug.LogWarning("[CastleWallsFromRecipe] missing prefab: " + piece.prefab); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = piece.name;
                go.transform.SetParent(parent.transform, false);
                go.transform.localPosition    = V(piece.pos);
                go.transform.localEulerAngles = V(piece.rot);
                go.transform.localScale       = V(piece.scale, Vector3.one);
                Undo.RegisterCreatedObjectUndo(go, "Recreate Castle Walls");
                placed++;
            }

            // T-007 — CLOSE THE WALL SEAMS. The authored recipe places ONE corner tower + two
            // wall runs flanking a gate, but leaves real gaps: (a) between the corner tower and
            // the start of the wall run, and (b) at the towerless far end of the run, which only
            // closes once the NEXT side's mirrored corner tower lands there. With one base wall
            // length these read as disconnected segments / a visible seam between a run and the
            // tower. We MEASURE the actual world-space renderer bounds of the placed pieces (never
            // guess a length) and drop filler Wall_Medieval_Stone segments into every gap along the
            // south wall line — EXCEPT the intentional gate opening. Done BEFORE the mirror so the
            // fillers mirror x4 and every side closes identically. Does NOT move any authored piece.
            CloseSouthWallSeams(parent.transform, recipe);

            // T-WALLCOL (owner 2026-06-27): the seam fill leaves the FULL ±GateClearHalf gate span
            // collider-free, so the hero walks through the whole arch AND the WO-449 LoS linecast
            // passes through it (targeting through the wall). Close the gate MASONRY with two invisible
            // flank colliders — extend to the arch centre, back off 1/3 of the half (DoorwayHalf) each
            // side — leaving only the central doorway open. Pre-mirror so all 4 sides inherit it.
            ExtendWallCollidersToGate(parent.transform, recipe);

            // WO-449: put the whole SOUTH wall side (authored pieces + seam fillers, incl. their
            // MeshCollider children) on the "Structure" layer BEFORE the mirror, so the LoS gate's
            // linecast occludes targeting through walls. Done pre-mirror so the x4 clones inherit it.
            // Gate OPENINGS are not wall geometry (they're the gaps + exit strips, untouched here),
            // so they stay passable. Layer != navmesh — the bake is unaffected.
            int structureLayer = LayerMask.NameToLayer(StructureLayerName);
            if (structureLayer >= 0) SetLayerRecursively(parent, structureLayer);
            else Debug.LogWarning("[CastleWallsFromRecipe] WO-449: '" + StructureLayerName +
                                  "' layer missing — wall geometry left on Default (LoS gate will degrade off).");

            // Mirror south -> West/North/East around world origin (the Heart at 0,0,0).
            foreach (var (angle, label) in Sides)
            {
                var clone = UnityEngine.Object.Instantiate(parent, parent.transform.parent);
                clone.name = "CastleSide_" + label;
                var rot = Quaternion.Euler(0f, angle, 0f);
                clone.transform.position = rot * parent.transform.position;
                clone.transform.rotation = rot * parent.transform.rotation;
                Undo.RegisterCreatedObjectUndo(clone, "Recreate Castle Walls");
            }

            Debug.Log("[CastleWallsFromRecipe] recreated south from recipe (" + placed + " pieces) + mirrored x4. " +
                      "Save the scene, then re-bake the castle NavMesh (select the NavMeshSurface > Bake).");
        }

        // =====================================================================
        //  T-WALLCOL APPLY (headless, owner 2026-06-27 "do it from script without me touching"):
        //  SURGICAL — does NOT delete/rebuild the walls (no clobber of the saved scene). Opens
        //  MainCastle_Hall, adds the 2 doorjamb colliders to EACH existing CastleSide_* (idempotent),
        //  SAVES, then runs the PROVEN castle bake (CastleHubBuilder.BatchAddFloorAndBakeCastle —
        //  useGeometry=PhysicsColliders, collectObjects=All) which COLLECTS the new Structure-layer
        //  box colliders and carves the navmesh down to the central 5m doorway. The hero is a
        //  NavMeshAgent, so this bake is what actually stops it walking through the masonry; the LoS
        //  linecast is fixed the moment the colliders exist. Verify after with Defenders/Castle/Verify
        //  Gate Nav. The doorjamb names carry NO "Gate" substring so ExcludeGatesFromNavBake does NOT
        //  exclude them from the bake (that exclusion is only for the arch mesh).
        // =====================================================================
        [MenuItem("Defenders/Castle/Apply Gate DoorJambs + Bake (T-WALLCOL, headless)")]
        public static void ApplyGateDoorJambsAndBake()
        {
            const string scenePath = "Assets/Scenes/MainCastle_Hall.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            // owner F8 2026-06-27 "you added walls inside, not at the gates": a prior apply used the
            // HEAVY CastleHubBuilder.BatchAddFloorAndBakeCastle, which ALSO runs BuildInnerWallRing
            // (a visible concentric Wall ring 18m from centre — the "walls inside"). Remove that stray
            // ring; we only want the INVISIBLE gate doorjambs + a LEAN navmesh re-bake (no structure rebuild).
            var stray = GameObject.Find("InnerWallRing_CoC");
            if (stray != null) { UnityEngine.Object.DestroyImmediate(stray); Debug.Log("[CastleWallsFromRecipe] removed stray InnerWallRing_CoC (owner F8: walls inside)."); }

            int added = AddDoorJambsToOpenScene();
            if (added == 0) { Debug.LogError("[CastleWallsFromRecipe] T-WALLCOL apply: 0 doorjambs added — aborting before bake."); return; }

            LeanBakeNavMeshSurfaces();   // bake-only: collects the doorjamb colliders, NO BuildInnerWallRing / floor / structure rebuild

            // WO-1731: LeanBakeNavMeshSurfaces() above re-pointed every surface's m_NavMeshData
            // and deleted the asset each one replaced. If the scene is not written back, the
            // saved scene keeps the DELETED guid and ships with no navmesh at all -- silently.
            // A bake that cannot persist its own reference THROWS (RaidNavBake.cs:109-111).
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes())
                throw new System.InvalidOperationException(
                    "[CastleWallsFromRecipe] could not save navigation for " + scenePath +
                    " -- the lean re-bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Debug.Log("[CastleWallsFromRecipe] T-WALLCOL apply DONE (clean, no inner ring): invisible gate doorjambs + lean re-bake. 5m doorway.");
        }

        // Lean NavMeshSurface bake (replicates CastleHubBuilder.BakeAllCastleSurfacesAndPersist, which
        // is private): configure every surface to Physics Colliders / All, BuildNavMesh, persist the
        // data asset. NO floor/inner-ring/structure rebuild — just re-bake what's in the scene now.
        private static void LeanBakeNavMeshSurfaces()
        {
            System.Type surfType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                surfType = asm.GetType("Unity.AI.Navigation.NavMeshSurface");
                if (surfType != null) break;
            }
            if (surfType == null) { Debug.LogError("[CastleWallsFromRecipe] LeanBake: NavMeshSurface type not found — bake skipped."); return; }

            var surfaces = UnityEngine.Object.FindObjectsByType(surfType, FindObjectsSortMode.None);
            Debug.Log("[CastleWallsFromRecipe] LeanBake: NavMeshSurface count = " + surfaces.Length);
            foreach (var s in surfaces)
            {
                var ug = surfType.GetProperty("useGeometry");
                if (ug != null) ug.SetValue(s, System.Enum.ToObject(ug.PropertyType, 1)); // PhysicsColliders
                var co = surfType.GetProperty("collectObjects");
                if (co != null) co.SetValue(s, System.Enum.ToObject(co.PropertyType, 0)); // All

                var build = surfType.GetMethod("BuildNavMesh", System.Type.EmptyTypes);
                if (build != null) { build.Invoke(s, null); Debug.Log("[CastleWallsFromRecipe] LeanBake: BuildNavMesh() invoked."); }

                var dataProp = surfType.GetProperty("navMeshData");
                var data = dataProp != null ? dataProp.GetValue(s) as UnityEngine.Object : null;
                if (data != null && !AssetDatabase.Contains(data))
                {
                    if (!System.IO.Directory.Exists("Assets/Scenes/MainCastle_Hall"))
                        AssetDatabase.CreateFolder("Assets/Scenes", "MainCastle_Hall");
                    string assetPath = "Assets/Scenes/MainCastle_Hall/NavMesh-NavMeshSurface.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (existing != null) AssetDatabase.DeleteAsset(assetPath);
                    AssetDatabase.CreateAsset(data, assetPath);
                    Debug.Log("[CastleWallsFromRecipe] LeanBake: navmesh asset written -> " + assetPath);
                }
            }
        }

        // Add the 2 doorjamb colliders to each existing CastleSide_* in the OPEN scene (idempotent).
        // Returns the count added. Measures wall height/thickness from the unrotated South side and
        // reuses it for all 4 (the sides are identical mirrored clones), placing in each side's LOCAL
        // space so the rotated clones land correctly.
        private static int AddDoorJambsToOpenScene()
        {
            var ta = Resources.Load<TextAsset>("Data/castle-south-recipe");
            if (ta == null) { Debug.LogError("[CastleWallsFromRecipe] doorjamb apply: recipe not found."); return 0; }
            var recipe = JsonUtility.FromJson<Recipe>(ta.text);

            float gateX = 0f, lineZ = -40.6f;
            if (recipe != null && recipe.pieces != null)
                foreach (var p in recipe.pieces)
                    if (p != null && p.name == "Gate_South" && p.pos != null && p.pos.Length == 3)
                    { gateX = p.pos[0]; lineZ = p.pos[2]; }

            float wallH = 6f, wallT = 1f;
            var south = GameObject.Find(SouthName);
            if (south != null)
                foreach (Transform child in south.transform)
                {
                    if (!child.name.StartsWith("Wall_South")) continue;
                    var rends = child.GetComponentsInChildren<MeshRenderer>(true);
                    if (rends.Length == 0) continue;
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    wallH = Mathf.Max(2f, b.size.y);
                    wallT = Mathf.Max(0.4f, b.size.z);
                    break;
                }

            float flankLen  = GateClearHalf - DoorwayHalf;
            float centerOff = (GateClearHalf + DoorwayHalf) * 0.5f;
            int structureLayer = LayerMask.NameToLayer(StructureLayerName);
            int added = 0;
            foreach (var side in new[] { SouthName, "CastleSide_West", "CastleSide_North", "CastleSide_East" })
            {
                var root = GameObject.Find(side);
                if (root == null) { Debug.LogWarning($"[CastleWallsFromRecipe] doorjamb apply: {side} not found — skipped."); continue; }
                AddGateFlankCollider(root.transform, "Wall_South_DoorJamb_L", gateX - centerOff, lineZ, flankLen, wallH, wallT);
                AddGateFlankCollider(root.transform, "Wall_South_DoorJamb_R", gateX + centerOff, lineZ, flankLen, wallH, wallT);
                if (structureLayer >= 0)
                {
                    var l = root.transform.Find("Wall_South_DoorJamb_L"); if (l != null) l.gameObject.layer = structureLayer;
                    var r = root.transform.Find("Wall_South_DoorJamb_R"); if (r != null) r.gameObject.layer = structureLayer;
                }
                added += 2;
            }
            Debug.Log($"[CastleWallsFromRecipe] doorjamb apply: {added} collider(s) (5m doorway, h={wallH:F1}) across the castle sides.");
            return added;
        }

        private static Vector3 V(float[] a, Vector3 fallback = default)
            => (a != null && a.Length == 3) ? new Vector3(a[0], a[1], a[2]) : fallback;

        // WO-449: set this GameObject and ALL descendants (which carry the MeshColliders the LoS
        // linecast hits) onto the given layer. Recursive so child renderer/collider GOs are covered.
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        // =====================================================================
        //  T-007 — fill gaps along the SOUTH wall line so the perimeter reads as ONE connected
        //  wall (run abuts its corner tower; the towerless far end reaches the adjacent side's
        //  mirrored corner). MEASURE-not-guess: we use each placed piece's real world-space
        //  renderer bounds. The only gap we preserve is the authored gate opening.
        // =====================================================================
        private const float GateClearHalf = 7.5f;  // keep ~15m clear around the gate centre (4 gates)
        private const float MinGap        = 1.0f;   // ignore hairline seams below this
        // T-WALLCOL: central open doorway half-width = 1/3 of the gate half (owner 2026-06-27, "back
        // off 1/3 each side"). 2.5m -> a 5m walkable doorway; the masonry from 2.5m..7.5m each side is
        // closed by an invisible collider on the Structure layer (passage + LoS occlusion).
        private const float DoorwayHalf   = GateClearHalf / 3f;  // 2.5m central open half-width

        private static void CloseSouthWallSeams(Transform southParent, Recipe recipe)
        {
            var wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PolyRoot + "Wall_Medieval_Stone.prefab");
            if (wallPrefab == null) { Debug.LogWarning("[CastleWallsFromRecipe] Wall_Medieval_Stone prefab missing — seam fill skipped (T-007)."); return; }

            // Wall line geometry: z and the base wall length come from MEASURED bounds, not constants.
            // Gate centre (world X) from the recipe so we never wall over the opening.
            float gateX = 0f, lineZ = -40.6f;
            bool gotGate = false;
            if (recipe.pieces != null)
                foreach (var p in recipe.pieces)
                    if (p != null && p.name == "Gate_South" && p.pos != null && p.pos.Length == 3)
                    { gateX = p.pos[0]; lineZ = p.pos[2]; gotGate = true; }
            if (!gotGate) Debug.LogWarning("[CastleWallsFromRecipe] Gate_South not in recipe — using fallback gate centre 0 (T-007).");

            // Measure the X-extent the existing south pieces already cover, and the base wall length
            // (the un-scaled Wall_Medieval_Stone footprint along X) from a placed wall renderer.
            float minX = float.MaxValue, maxX = float.MinValue, baseWallLen = 0f;
            foreach (Transform child in southParent)
            {
                var rends = child.GetComponentsInChildren<MeshRenderer>(true);
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                minX = Mathf.Min(minX, b.min.x);
                maxX = Mathf.Max(maxX, b.max.x);
                if (child.name.StartsWith("Wall_South"))
                {
                    float sx = Mathf.Abs(child.localScale.x);
                    if (sx > 0.001f) baseWallLen = Mathf.Max(baseWallLen, b.size.x / sx);
                }
            }
            if (baseWallLen < 0.5f) baseWallLen = 10f; // safety fallback if no wall measured

            // Target span: from the south corner tower (left) to the adjacent East side's mirrored
            // corner tower, which lands near world X = +|towerLocalX| after a 270deg mirror.
            float towerLocalX = -42.33f;
            if (recipe.pieces != null)
                foreach (var p in recipe.pieces)
                    if (p != null && p.name != null && p.name.StartsWith("CornerTower") && p.pos != null && p.pos.Length == 3)
                        towerLocalX = p.pos[0];
            float spanMin = Mathf.Min(minX, towerLocalX + 2f);     // start inside the corner tower
            float spanMax = Mathf.Max(maxX, Mathf.Abs(towerLocalX)); // reach the mirrored opposite corner

            // Build an occupancy list of [min,max] X intervals the EXISTING pieces cover, plus the
            // gate opening as a permanent "occupied" (do-not-fill) interval.
            var occupied = new System.Collections.Generic.List<(float a, float b)>();
            foreach (Transform child in southParent)
            {
                var rends = child.GetComponentsInChildren<MeshRenderer>(true);
                if (rends.Length == 0) continue;
                Bounds bb = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) bb.Encapsulate(rends[i].bounds);
                occupied.Add((bb.min.x, bb.max.x));
            }
            occupied.Add((gateX - GateClearHalf, gateX + GateClearHalf)); // protect the gate opening
            occupied.Sort((u, v) => u.a.CompareTo(v.a));

            // Merge intervals, then fill every gap between consecutive intervals (and the leading/
            // trailing gap to spanMin/spanMax) with right-sized scaled wall segments.
            var merged = new System.Collections.Generic.List<(float a, float b)>();
            foreach (var iv in occupied)
            {
                if (merged.Count > 0 && iv.a <= merged[merged.Count - 1].b + 0.01f)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.a, Mathf.Max(last.b, iv.b));
                }
                else merged.Add(iv);
            }

            int fillers = 0;
            float cursor = spanMin;
            for (int i = 0; i <= merged.Count; i++)
            {
                float gapEnd = (i < merged.Count) ? merged[i].a : spanMax;
                FillGap(southParent, wallPrefab, cursor, gapEnd, lineZ, baseWallLen, ref fillers);
                if (i < merged.Count) cursor = Mathf.Max(cursor, merged[i].b);
            }

            Debug.Log($"[CastleWallsFromRecipe] T-007 seam fill: base wall len {baseWallLen:F2}m, " +
                      $"span [{spanMin:F1},{spanMax:F1}] on z={lineZ:F1}, gate clear ±{GateClearHalf} @x={gateX:F1} — added {fillers} filler segment(s).");
        }

        // Fill [from,to] along the south wall line with one scaled Wall_Medieval_Stone (skips a gap
        // narrower than MinGap; a single scaled segment spans any width cleanly for a low-poly wall).
        private static void FillGap(Transform parent, GameObject wallPrefab, float from, float to,
                                    float lineZ, float baseWallLen, ref int fillers)
        {
            float gap = to - from;
            if (gap < MinGap) return;
            float centerX = (from + to) * 0.5f;
            float scaleX  = gap / baseWallLen;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab);
            go.name = "Wall_South_SeamFill_" + (fillers + 1);
            go.transform.SetParent(parent, false);
            // South recipe pieces face rot Y=180 (parent at origin, no rot) — match so the filler
            // renders the same face as the authored runs.
            go.transform.localPosition    = new Vector3(centerX, 0f, lineZ);
            go.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
            go.transform.localScale       = new Vector3(scaleX, 1f, 1f);
            Undo.RegisterCreatedObjectUndo(go, "Recreate Castle Walls");
            fillers++;
        }

        // =====================================================================
        //  T-WALLCOL — close the gate MASONRY (not the doorway) with two invisible flank colliders.
        //  Owner rule (2026-06-27): "extend the straight wall to the halfway point then back off 1/3
        //  each side." halfway = gate centre; back-off = DoorwayHalf (1/3 of the 7.5 gate half = 2.5m).
        //  So each flank collider spans DoorwayHalf..GateClearHalf (2.5..7.5m) off the gate centre — a
        //  5m box — leaving the central 2*DoorwayHalf (5m) doorway open. Collider-ONLY (no renderer):
        //  it sits invisibly inside the gate's existing arch art, and on the Structure layer (set by
        //  the caller pre-mirror) it BOTH blocks passage AND occludes the WO-449 LoS targeting linecast
        //  through the masonry. The open central doorway stays passable + see-through (correct).
        // =====================================================================
        private static void ExtendWallCollidersToGate(Transform southParent, Recipe recipe)
        {
            // gate centre + wall line (local X / Z) from the recipe — same source as the seam fill.
            float gateX = 0f, lineZ = -40.6f; bool gotGate = false;
            if (recipe.pieces != null)
                foreach (var p in recipe.pieces)
                    if (p != null && p.name == "Gate_South" && p.pos != null && p.pos.Length == 3)
                    { gateX = p.pos[0]; lineZ = p.pos[2]; gotGate = true; }
            if (!gotGate) Debug.LogWarning("[CastleWallsFromRecipe] T-WALLCOL: Gate_South not in recipe — using gate centre 0.");

            // wall height + thickness MEASURED from a placed south wall (never guessed).
            float wallH = 6f, wallT = 1f;
            foreach (Transform child in southParent)
            {
                if (!child.name.StartsWith("Wall_South")) continue;
                var rends = child.GetComponentsInChildren<MeshRenderer>(true);
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                wallH = Mathf.Max(2f, b.size.y);
                wallT = Mathf.Max(0.4f, b.size.z);
                break;
            }

            float flankLen  = GateClearHalf - DoorwayHalf;          // 5m collider each flank
            float centerOff = (GateClearHalf + DoorwayHalf) * 0.5f; // 5m off the gate centre
            AddGateFlankCollider(southParent, "Wall_South_DoorJamb_L", gateX - centerOff, lineZ, flankLen, wallH, wallT);
            AddGateFlankCollider(southParent, "Wall_South_DoorJamb_R", gateX + centerOff, lineZ, flankLen, wallH, wallT);

            Debug.Log($"[CastleWallsFromRecipe] T-WALLCOL: 2x {flankLen:F1}m flank colliders @x={gateX - centerOff:F1}/{gateX + centerOff:F1} " +
                      $"z={lineZ:F1} h={wallH:F1}m — central {2f * DoorwayHalf:F1}m doorway left open (passable + targetable).");
        }

        // One invisible, idempotent gate-flank box collider on the south side (mirrored x4 by the caller).
        private static void AddGateFlankCollider(Transform parent, string name, float centerX, float lineZ,
                                                 float lenX, float height, float thickness)
        {
            var existing = parent.Find(name);                // idempotent — don't stack on a re-run
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition    = new Vector3(centerX, 0f, lineZ);
            go.transform.localEulerAngles = Vector3.zero;
            var box = go.AddComponent<BoxCollider>();
            box.size   = new Vector3(lenX, height, thickness);
            box.center = new Vector3(0f, height * 0.5f, 0f);  // base at y=0, like the wall pieces
            Undo.RegisterCreatedObjectUndo(go, "Recreate Castle Walls");
        }
    }
}
