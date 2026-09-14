// =============================================================================
// StructureHuskCleanup — ONE-TIME scene repair for WO-1716.
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor
//
// Menu:      Defenders/Castle/Preview invisible structure husks   (destroys NOTHING)
//            Defenders/Castle/Remove invisible structure husks    (destroys + SAVES)
// Batchmode: DeNelle.Editor.StructureHuskCleanup.PreviewBatch
//            DeNelle.Editor.StructureHuskCleanup.RemoveBatch
// Markers:   STRUCTURE_HUSK_PREVIEW_OK / STRUCTURE_HUSK_CLEANUP_OK / _FAIL
//
// WHY THIS EXISTS AND WHY IT IS NOT A REGRESSION
//   `Main_Castle_Overworld.unity` carries one proven leftover: a GameObject named
//   `CastleBarracks` (polyperfect `Military_Barracks`) with its MeshFilter and
//   MeshRenderer both removed, a live MeshCollider, and a carving NavMeshObstacle of
//   Size 16.90712 x 5.08288 x 14.51318 on a 0.6/0.9/0.6-scaled object. The owner
//   deleted it live in the Editor and re-baked: the oversized courtyard hole vanished
//   and every remaining structure carved normally. That deletion was diagnostic only —
//   deliberately NOT saved — so the repair has to land through tooling, because
//   CLAUDE.md sec.3 forbids hand-editing the scene and a hand save is not reproducible
//   from a fresh clone.
//
//   It is a MENU COMMAND the lead runs ONCE, not a regression fixture and not a
//   load-time repair. A repair that runs on every headless pass would silently paper
//   over the very defect its oracle is supposed to catch, and the oracle's green would
//   stop meaning anything (CLAUDE.md sec.8: a gate that proves nothing is worse than
//   no gate). The CODE defect is fixed separately and permanently in
//   StructureVisualStrip + CastleHubBuilder.SkinHostUpright + NavMeshBakeFinal.
//
// THE SAFETY RULES — all three must hold before anything is destroyed
//   1. The object is an invisible-but-solid husk by the SHARED predicate
//      (StructureVisualStrip.IsInvisibleNavBlockingHusk) — nothing under it renders,
//      and it still blocks with a solid collider or a carving obstacle.
//   2. Its NAME is an authored `bakedTwins` entry, read from the SAME catalog
//      NavMeshBakeFinal reads (no second copy of the list). A husk answering to a twin
//      name is by definition broken: that name is supposed to be a visible building.
//   3. It carries NO gameplay identity anywhere under it — no Building,
//      AuthoredCastleStorefront, PlacedStructure or ResourceCollector. This is the rule
//      that makes the command safe: the owner's VISIBLE authored barracks is a
//      different GameObject (named `Barracks`, prefab guid 5a258590…, carrying
//      Building + AuthoredCastleStorefront with legacyName `CastleBarracks`), and it
//      fails rules 1 and 3 twice over. Selecting by name alone would have risked it.
//
//   Plus the OwnerCastleLayoutRepair.RepairGroupPhysics discipline: refuse on a dirty
//   scene, and copy the scene file to the audit output directory before saving.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class StructureHuskCleanup
    {
        private const string FlowSys = "Hub";
        /// <summary>The hub scene, taken from the existing owner-castle audit constant rather than
        /// retyped — a second copy of a scene path is exactly what HubSceneLiteralRegression exists
        /// to stop (CLAUDE.md sec.5 duplicated state).</summary>
        private static string ScenePath => OwnerCastleLayoutAudit.ScenePath;
        private const string MarkerPreview = "STRUCTURE_HUSK_PREVIEW_OK";
        private const string MarkerRemoved = "STRUCTURE_HUSK_CLEANUP_OK";
        private const string MarkerFail    = "STRUCTURE_HUSK_CLEANUP_FAIL";

        [MenuItem("Defenders/Castle/Preview invisible structure husks")]
        public static void PreviewMenu() => Execute(false);

        [MenuItem("Defenders/Castle/Remove invisible structure husks")]
        public static void RemoveMenu() => Execute(true);

        /// <summary>Batchmode entry — reports only, saves nothing.</summary>
        public static void PreviewBatch() => Execute(false);

        /// <summary>Batchmode entry — destroys the husks and SAVES the scene.</summary>
        public static void RemoveBatch() => Execute(true);

        private static void Execute(bool apply)
        {
            try
            {
                ExecuteCore(apply);
            }
            catch (Exception ex)
            {
                Debug.LogError(MarkerFail + " - " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        }

        private static void ExecuteCore(bool apply)
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Edit mode only — refusing to run in play mode.");

            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException(
                        "A loaded scene has unsaved changes. Save or discard them first — this command " +
                        "saves the hub scene and must never fold someone else's edits into that save.");

            var twinNames = new HashSet<string>(NavMeshBakeFinal.ReadBakedTwinNames(), StringComparer.Ordinal);
            if (twinNames.Count == 0)
                throw new InvalidOperationException(
                    "structures-catalog.json authored NO bakedTwins — with an empty name list this command " +
                    "could never match anything, so a 'clean' result would be meaningless. Refusing to run.");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var candidates = new List<GameObject>();
            var skipped = new List<string>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    if (!StructureVisualStrip.IsInvisibleNavBlockingHusk(go, out string detail)) continue;

                    if (!twinNames.Contains(go.name))
                    {
                        skipped.Add($"'{go.name}' IS a husk ({detail}) but its name is not an authored " +
                                    "bakedTwin — left alone. Inspect it by hand before removing anything.");
                        continue;
                    }

                    string identity = DescribeGameplayIdentity(go);
                    if (identity != null)
                    {
                        skipped.Add($"'{go.name}' is husk-shaped ({detail}) but carries gameplay identity " +
                                    $"({identity}) — left alone. This is the guard that protects the owner's " +
                                    "authored structures.");
                        continue;
                    }

                    candidates.Add(go);
                    Debug.Log($"[StructureHuskCleanup] CANDIDATE {detail}; localPos={go.transform.localPosition} " +
                              $"worldPos={go.transform.position} localScale={go.transform.localScale} " +
                              $"prefab='{PrefabSourceOf(go)}'.");
                }
            }

            foreach (var s in skipped) Debug.Log("[StructureHuskCleanup] SKIP " + s);

            if (candidates.Count == 0)
            {
                Debug.Log(MarkerPreview + " no invisible structure husks in " + ScenePath +
                          " (" + twinNames.Count + " bakedTwin name(s) checked, " + skipped.Count + " skipped).");
                return;
            }

            if (!apply)
            {
                Debug.Log(MarkerPreview + " " + candidates.Count + " husk(s) would be removed: " +
                          string.Join(", ", candidates.Select(c => c.name)) +
                          ". Run Defenders/Castle/Remove invisible structure husks to apply, then re-bake " +
                          "the navmesh (Defenders/World/Bake NavMesh (ALWAYS LAST)).");
                return;
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(OwnerCastleLayoutAudit.Output);
            string backup = Path.Combine(OwnerCastleLayoutAudit.Output, "before_husk_cleanup_" + stamp + ".unity");
            File.Copy(ScenePath, backup);

            var removed = new List<string>();
            foreach (var go in candidates)
            {
                removed.Add(go.name + " @ " + go.transform.position);
                FlowTrace.Step(FlowSys,
                    $"removing invisible structure husk '{go.name}' at {go.transform.position} (WO-1716).");
                Object.DestroyImmediate(go);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Scene save refused — nothing was persisted.");

            Debug.Log(MarkerRemoved + " removed " + removed.Count + " husk(s): " + string.Join("; ", removed) +
                      ". Backup: " + backup + ". RE-BAKE the navmesh now " +
                      "(Defenders/World/Bake NavMesh (ALWAYS LAST)) — the carve does not update itself.");
        }

        /// <summary>Names any gameplay component under <paramref name="go"/> that makes it a real
        /// structure rather than a leftover, or null when it carries none.</summary>
        private static string DescribeGameplayIdentity(GameObject go)
        {
            // TYPED, not name-matched: DeNelle.Editor references DeNelle.Village, so the compiler
            // can hold this guard to the real types and a rename cannot silently defeat it.
            var found = new List<string>();
            if (go.GetComponentInChildren<DeNelle.Village.Building>(true) != null)
                found.Add(nameof(DeNelle.Village.Building));
            if (go.GetComponentInChildren<DeNelle.Village.AuthoredCastleStorefront>(true) != null)
                found.Add(nameof(DeNelle.Village.AuthoredCastleStorefront));
            if (go.GetComponentInChildren<DeNelle.Village.PlacedStructure>(true) != null)
                found.Add(nameof(DeNelle.Village.PlacedStructure));
            if (go.GetComponentInChildren<DeNelle.Village.Buildings.Progression.ResourceCollector>(true) != null)
                found.Add(nameof(DeNelle.Village.Buildings.Progression.ResourceCollector));
            return found.Count == 0 ? null : string.Join("+", found.Distinct());
        }

        private static string PrefabSourceOf(GameObject go)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return string.IsNullOrEmpty(path) ? "(not a prefab instance / source missing)" : path;
        }
    }
}
