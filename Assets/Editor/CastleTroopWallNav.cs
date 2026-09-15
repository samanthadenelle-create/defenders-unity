// =============================================================================
// CastleTroopWallNav — bakes the MainCastle_Hall NavMeshSurface and PROVES a
// NavMeshAgent troop can path from the hero spawn UP onto the castle walls
// (owner 2026-06-14: troops garrison the ramparts; the hand-built steps + hidden
// NavMesh-link planes are the route up — but they only work once the surface is
// re-baked to absorb them).
//
// Bake: reflection on Unity.AI.Navigation.NavMeshSurface (no hard package dep,
// same as OuterWorldNavBake / CastleHubBuilder). Respects the owner's surface
// config (collect/geometry/agent) — we only invoke BuildNavMesh() + persist data.
//
// Verify (instrument-first, NOT assume): logs the baked navmesh Y-range (does the
// mesh even reach rampart height?), then for each elevated target (the link planes
// + any Stair/Step/Rampart/Battlement object top) samples with a TIGHT tolerance
// (so a low target can't snap up onto an elevated patch and fake success — memory
// batchmode-spatial-verify-traps) and computes a spawn->target path. Success =
// a PathComplete to a target whose sampled point is genuinely elevated (a real
// climb, not a ground snap). Read-only after the bake/save.
//
// Batchmode: DeNelle.Editor.CastleTroopWallNav.BakeAndVerify
//            DeNelle.Editor.CastleTroopWallNav.VerifyOnly
// Logs "TROOP_WALL_NAV_OK :: <detail>" or "TROOP_WALL_NAV_FAIL :: <detail>".
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DeNelle.Editor
{
    public static class CastleTroopWallNav
    {
        private const string ScenePath = "Assets/Scenes/MainCastle_Hall.unity";
        private const string AssetDir  = "Assets/Scenes/MainCastle_Hall";

        // Names that mark elevated walkable destinations a wall-garrison troop should reach.
        private static readonly string[] RampartFragments =
            { "Plane", "Rampart", "Battlement", "Stair", "Step", "Walkway", "Parapet" };

        [MenuItem("Defenders/Castle/Bake + Verify Troop Wall Nav")]
        public static void BakeAndVerify()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[CastleTroopWallNav] opened " + ScenePath);

            int baked = BakeSurfaces();
            if (baked == 0)
            {
                Debug.Log("[CastleTroopWallNav] TROOP_WALL_NAV_FAIL :: no NavMeshSurface produced data — nothing to walk on.");
                return;
            }
            // WO-1731: BakeSurfaces() rewrote each NavMeshSurface's m_NavMeshData. If the scene
            // is not written back the reference is lost and the scene keeps pointing at the
            // asset the bake replaced -- which ships as NO navmesh, with nothing on screen
            // saying so. A bake that cannot persist its own reference THROWS (RaidNavBake.cs:109-111).
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException(
                    "[CastleTroopWallNav] could not save navigation for " + ScenePath +
                    " -- the bake would leave the scene pointing at a navmesh it never persisted (WO-1731).");
            AssetDatabase.SaveAssets();
            Debug.Log($"[CastleTroopWallNav] baked {baked} surface(s) + saved scene.");

            Verify();
        }

        [MenuItem("Defenders/Castle/Verify Troop Wall Nav (no bake)")]
        public static void VerifyOnly()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Verify();
        }

        // Read-only: WHY are the islands disconnected? Dump the agent bake settings
        // (maxSlope/stepClimb) + what components actually sit on the link planes/stairs
        // (NavMeshLink? MeshCollider? NavMeshModifier?) so we fix the real cause.
        [MenuItem("Defenders/Castle/Diagnose Troop Wall Nav")]
        public static void Diagnose()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var surfType = ResolveType("Unity.AI.Navigation.NavMeshSurface");
            var surfaces = surfType != null
                ? UnityEngine.Object.FindObjectsByType(surfType)
                : Array.Empty<UnityEngine.Object>();
            foreach (var s in surfaces)
            {
                object idObj = surfType.GetProperty("agentTypeID")?.GetValue(s);
                int id = idObj is int ii ? ii : 0;
                var bs = NavMesh.GetSettingsByID(id);
                Debug.Log($"[CastleTroopWallNav.Diag] surface agentTypeID={id} -> " +
                          $"slope(maxWalkable)={bs.agentSlope}deg climb(stepHeight)={bs.agentClimb} " +
                          $"radius={bs.agentRadius} height={bs.agentHeight}");
            }

            int linkCount = 0;
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string tn = c.GetType().FullName;
                    if (tn.IndexOf("NavMeshLink", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tn.IndexOf("OffMeshLink", StringComparison.OrdinalIgnoreCase) >= 0)
                    { linkCount++; Debug.Log($"[CastleTroopWallNav.Diag] LINK '{tn}' on '{go.name}' @ {go.transform.position}"); }
                }
            }
            Debug.Log($"[CastleTroopWallNav.Diag] total NavMeshLink/OffMeshLink components = {linkCount}");

            foreach (var frag in new[] { "Plane", "Dungeon_Stairs_Stone" })
            {
                foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                {
                    if (go.name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var comps = go.GetComponents<Component>();
                    var names = new List<string>();
                    foreach (var c in comps) names.Add(c == null ? "<null>" : c.GetType().Name);
                    var mc = go.GetComponent<MeshCollider>();
                    var mr = go.GetComponent<MeshRenderer>();
                    Debug.Log($"[CastleTroopWallNav.Diag] '{go.name}' @ {go.transform.position} scale={go.transform.lossyScale} " +
                              $"rotXZ=({go.transform.eulerAngles.x:F0},{go.transform.eulerAngles.z:F0}) " +
                              $"collider={(mc != null)} rendererEnabled={(mr != null && mr.enabled)} comps=[{string.Join(",", names)}]");
                }
            }
            Debug.Log("[CastleTroopWallNav.Diag] DONE");
        }

        // The honest edit-mode test for NavMeshLink-bridged climbs: a link works at
        // runtime iff BOTH its endpoints sit on baked navmesh. (Edit-mode CalculatePath
        // can't traverse links — they register on OnEnable at runtime — so we validate
        // structure, not traversal.) Reports each link's endpoint on-mesh status + the
        // elevation it bridges. Run AFTER a bake.
        [MenuItem("Defenders/Castle/Verify Troop Wall Links")]
        public static void VerifyLinks()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int surfaces = ReloadCommittedNavMesh();
            if (surfaces == 0) { Debug.Log("[CastleTroopWallNav] TROOP_WALL_NAV_FAIL :: no committed navmesh."); return; }

            var linkType = ResolveType("Unity.AI.Navigation.NavMeshLink");
            if (linkType == null) { Debug.Log("[CastleTroopWallNav] TROOP_WALL_NAV_FAIL :: NavMeshLink type not found."); return; }
            var startP = linkType.GetProperty("startPoint");
            var endP   = linkType.GetProperty("endPoint");

            var links = UnityEngine.Object.FindObjectsByType(linkType);
            int valid = 0, dead = 0, bridging = 0;
            foreach (var l in links)
            {
                var mb = l as MonoBehaviour; if (mb == null) continue;
                Vector3 ls = (Vector3)startP.GetValue(l), le = (Vector3)endP.GetValue(l);
                Vector3 ws = mb.transform.TransformPoint(ls), we = mb.transform.TransformPoint(le);
                bool sOn = NavMesh.SamplePosition(ws, out NavMeshHit hs, 1.0f, NavMesh.AllAreas);
                bool eOn = NavMesh.SamplePosition(we, out NavMeshHit he, 1.0f, NavMesh.AllAreas);
                bool ok = sOn && eOn;
                float dy = (sOn && eOn) ? Mathf.Abs(he.position.y - hs.position.y) : 0f;
                bool bridges = ok && dy > 1.0f;
                if (ok) valid++; else dead++;
                if (bridges) bridging++;
                Debug.Log($"[CastleTroopWallNav.Link] '{mb.name}' start{ws}->{(sOn ? "ON" : "OFF")} " +
                          $"end{we}->{(eOn ? "ON" : "OFF")} bridgesDY={dy:F2}m {(bridges ? "BRIDGES-ELEVATION" : ok ? "flat/short" : "DEAD-END")}");
            }

            string verdict = bridging > 0
                ? $"TROOP_WALL_NAV_OK :: {bridging} link(s) bridge ground<->elevation with both ends on navmesh " +
                  $"({valid}/{links.Length} valid). Troops traverse these at runtime to garrison the walls. " +
                  $"(Cleanup: {links.Length} links across the planes — dedupe to 1 per plane.)"
                : valid > 0
                  ? $"TROOP_WALL_NAV_FAIL :: {valid} link(s) have both ends on mesh but none bridge >1m elevation — " +
                    "links are flat/too short to reach the rampart."
                  : "TROOP_WALL_NAV_FAIL :: no link has both endpoints on navmesh — endpoints float off-mesh " +
                    "(move them onto the courtyard floor + the rampart walkway, or re-bake so those patches exist).";
            Debug.Log("[CastleTroopWallNav] " + verdict);
        }

        // Cleanup: keep ONE NavMeshLink per GameObject, remove redundant duplicates
        // (the planes accrued 1-3 each during hand-authoring). Identical links are
        // harmless at runtime but noisy — collapse to one. Saves the scene.
        [MenuItem("Defenders/Castle/Dedupe Wall NavMesh Links")]
        public static void DedupeLinks()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var linkType = ResolveType("Unity.AI.Navigation.NavMeshLink");
            if (linkType == null) { Debug.LogError("[CastleTroopWallNav] NavMeshLink type not found."); return; }

            int removed = 0;
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                var links = go.GetComponents(linkType);
                for (int i = 1; i < links.Length; i++) // keep [0], drop the rest
                {
                    UnityEngine.Object.DestroyImmediate(links[i], true);
                    removed++;
                }
                if (links.Length > 1)
                    Debug.Log($"[CastleTroopWallNav] '{go.name}': {links.Length} links -> 1 (removed {links.Length - 1}).");
            }

            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log($"[CastleTroopWallNav] dedupe done — removed {removed} duplicate NavMeshLink(s).");
        }

        // ── Bake every NavMeshSurface in the open scene (reflection) ────────────
        private static int BakeSurfaces()
        {
            var surfType = ResolveType("Unity.AI.Navigation.NavMeshSurface");
            if (surfType == null) { Debug.LogError("[CastleTroopWallNav] NavMeshSurface type not found."); return 0; }

            var surfaces = UnityEngine.Object.FindObjectsByType(surfType);
            if (surfaces.Length == 0) { Debug.LogError("[CastleTroopWallNav] no NavMeshSurface in scene."); return 0; }

            var build    = surfType.GetMethod("BuildNavMesh", Type.EmptyTypes);
            var dataProp = surfType.GetProperty("navMeshData");
            if (build == null || dataProp == null) { Debug.LogError("[CastleTroopWallNav] NavMeshSurface API mismatch."); return 0; }

            if (!System.IO.Directory.Exists(AssetDir))
                AssetDatabase.CreateFolder("Assets/Scenes", "MainCastle_Hall");

            int n = 0;
            for (int i = 0; i < surfaces.Length; i++)
            {
                var surf = surfaces[i];
                // Log the owner's config so we can SEE what gets collected (no override).
                object collect = surfType.GetProperty("collectObjects")?.GetValue(surf);
                object geom    = surfType.GetProperty("useGeometry")?.GetValue(surf);
                object agent   = surfType.GetProperty("agentTypeID")?.GetValue(surf);
                Debug.Log($"[CastleTroopWallNav] surface[{i}] config: collect={collect} geometry={geom} agentTypeID={agent}");

                build.Invoke(surf, null);
                var data = dataProp.GetValue(surf) as UnityEngine.Object;
                if (data == null)
                {
                    Debug.LogWarning($"[CastleTroopWallNav] surface[{i}] navMeshData NULL after bake — collected nothing.");
                    continue;
                }
                if (!AssetDatabase.Contains(data))
                {
                    string path = $"{AssetDir}/NavMesh-MainCastle-{i}.asset";
                    var prior = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (prior != null) AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(data, path);
                    Debug.Log($"[CastleTroopWallNav] surface[{i}] navmesh asset -> {path}");
                }
                else Debug.Log($"[CastleTroopWallNav] surface[{i}] navMeshData updated in place.");
                n++;
            }
            return n;
        }

        // ── Verify spawn -> rampart path ───────────────────────────────────────
        private static void Verify()
        {
            int surfaces = ReloadCommittedNavMesh();
            if (surfaces == 0) { Debug.Log("[CastleTroopWallNav] TROOP_WALL_NAV_FAIL :: no committed navmesh surface data."); return; }

            // Holistic: does the baked mesh even reach rampart height?
            var tri = NavMesh.CalculateTriangulation();
            float meshMinY = float.MaxValue, meshMaxY = float.MinValue;
            foreach (var v in tri.vertices) { if (v.y < meshMinY) meshMinY = v.y; if (v.y > meshMaxY) meshMaxY = v.y; }
            Debug.Log($"[CastleTroopWallNav] baked navmesh: {tri.vertices.Length} verts, " +
                      $"Y range [{meshMinY:F2} .. {meshMaxY:F2}] ({tri.indices.Length / 3} tris).");

            var spawnGo = GameObject.Find("HeroStartPoint_PlayerSpawn") ?? GameObject.Find("Capsule");
            if (spawnGo == null) { Debug.Log("[CastleTroopWallNav] TROOP_WALL_NAV_FAIL :: no hero spawn marker."); return; }
            Vector3 spawn = spawnGo.transform.position;
            if (!NavMesh.SamplePosition(spawn, out NavMeshHit hSpawn, 5f, NavMesh.AllAreas))
            { Debug.Log($"[CastleTroopWallNav] TROOP_WALL_NAV_FAIL :: spawn {spawn} not on navmesh (troops spawn stuck)."); return; }
            Debug.Log($"[CastleTroopWallNav] spawn {spawn} -> onMesh {hSpawn.position}.");

            var targets = CollectRampartTargets();
            Debug.Log($"[CastleTroopWallNav] {targets.Count} elevated target candidate(s) (sorted high->low).");
            targets.Sort((a, b) => b.pos.y.CompareTo(a.pos.y));

            string bestDetail = null;
            float bestGain = 0f;
            foreach (var t in targets)
            {
                // Tight tolerance: a genuinely-elevated walkable patch must exist near the
                // target, else SamplePosition can't snap up onto it from the ground.
                bool on = NavMesh.SamplePosition(t.pos, out NavMeshHit hT, 1.5f, NavMesh.AllAreas);
                if (!on) { Debug.Log($"[CastleTroopWallNav]   '{t.name}' @ {t.pos} -> NOT on navmesh (within 1.5m)."); continue; }

                float elevAboveSpawn = hT.position.y - hSpawn.position.y;
                bool genuinelyElevated = elevAboveSpawn > 1.5f; // a real climb, not the ground patch

                var path = new NavMeshPath();
                NavMesh.CalculatePath(hSpawn.position, hT.position, NavMesh.AllAreas, path);
                int corners = path.corners != null ? path.corners.Length : 0;
                float topCornerY = corners > 0 ? path.corners[corners - 1].y : hSpawn.position.y;

                Debug.Log($"[CastleTroopWallNav]   '{t.name}' targetY={t.pos.y:F2} sampledY={hT.position.y:F2} " +
                          $"gainAboveSpawn={elevAboveSpawn:F2}m status={path.status} corners={corners} topCornerY={topCornerY:F2}");

                if (path.status == NavMeshPathStatus.PathComplete && genuinelyElevated && elevAboveSpawn > bestGain)
                {
                    bestGain = elevAboveSpawn;
                    bestDetail = $"troop paths from spawn up to '{t.name}' (+{elevAboveSpawn:F2}m, " +
                                 $"sampledY {hT.position.y:F2}) via {corners} corners — ramparts ARE reachable.";
                }
            }

            if (bestDetail != null)
                Debug.Log("[CastleTroopWallNav] TROOP_WALL_NAV_OK :: " + bestDetail);
            else
                Debug.Log("[CastleTroopWallNav] TROOP_WALL_NAV_FAIL :: no COMPLETE path from spawn to any elevated " +
                          $"(+1.5m) rampart target. Mesh Y-top is {meshMaxY:F2} vs spawn {hSpawn.position.y:F2}. " +
                          "Either the steps/link-planes didn't bake walkable (slope > agent maxSlope, or geometry " +
                          "mode misses their colliders), or no rampart-named target exists to aim at.");
        }

        // Elevated walkable destinations: the hidden link planes + any rampart/stair object.
        private static List<(string name, Vector3 pos)> CollectRampartTargets()
        {
            var list = new List<(string, Vector3)>();
            var seen = new HashSet<int>();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            {
                if (!seen.Add(go.GetInstanceID())) continue;
                bool match = false;
                foreach (var frag in RampartFragments)
                    if (go.name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                if (!match) continue;

                // Prefer renderer-bounds top (the walkable surface), else transform position.
                Vector3 pos = go.transform.position;
                var rends = go.GetComponentsInChildren<Renderer>(true);
                if (rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    pos = new Vector3(b.center.x, b.max.y, b.center.z);
                }
                list.Add((go.name, pos));
            }
            return list;
        }

        private static int ReloadCommittedNavMesh()
        {
            NavMesh.RemoveAllNavMeshData();
            var surfType = ResolveType("Unity.AI.Navigation.NavMeshSurface");
            if (surfType == null) return 0;
            var dataProp = surfType.GetProperty("navMeshData");
            var surfaces = UnityEngine.Object.FindObjectsByType(surfType);
            int n = 0;
            foreach (var s in surfaces)
            {
                var data = dataProp != null ? dataProp.GetValue(s) as NavMeshData : null;
                if (data != null) { NavMesh.AddNavMeshData(data); n++; }
            }
            return n;
        }

        private static Type ResolveType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
