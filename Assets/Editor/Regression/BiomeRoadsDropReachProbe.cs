// =============================================================================
// BiomeRoadsDropReachProbe — MEASURE the four biome-drop destinations against the
// hub's real terrain and its real baked navmesh. WO-1692.
// -----------------------------------------------------------------------------
// WHY THIS IS AN INSTRUMENT AND NOT A GATE CASE (CLAUDE.md sec.12):
//   The WO-1692 RCA proved WHERE the old probe was asked (inside dg_hollow_roads,
//   whose Single load has already unloaded the hub and its navmesh — SceneRouter.cs:323)
//   and moved the grounding to the hub. It did NOT prove HOW FAR the hub's baked
//   navmesh actually reaches, or what the terrain height is at the four derived
//   points — and nobody should guess. This file measures both, by opening the shipped
//   hub scene and asking its own data.
//
//   It is deliberately NOT registered in DataRegression: it OPENS A SCENE, which is
//   slow and stateful, and a suite case that can fail because a bake is short would
//   block every unrelated gate on a question that is the owner's to rule. So it is a
//   one-command measurement whose OUTPUT is the finding:
//
//     Unity -batchmode -quit -projectPath <repo> \
//       -executeMethod DeNelle.Editor.Regression.BiomeRoadsDropReachProbe.RunStandalone
//
//   Judge it by the marker on a FRESH log — BIOME_ROADS_REACH_OK (every derived point
//   has walkable navmesh under it) or BIOME_ROADS_REACH_SHORT (at least one does not) —
//   never by the exit code (CLAUDE.md sec.16).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using DeNelle.Core.World;

namespace DeNelle.Editor.Regression
{
    public static class BiomeRoadsDropReachProbe
    {
        private const string HubScene = "Assets/Scenes/Main_Castle_Overworld.unity";

        /// <summary>The radius HollowRoadsDropInjector itself trusts when grounding a drop.</summary>
        private const float SeatRadius = 12f;

        /// <summary>A deliberately huge second radius, used ONLY to REPORT how far the nearest
        /// navmesh actually is when the seat radius misses. It decides nothing.</summary>
        private const float ReportRadius = 600f;

        [MenuItem("Defenders/Diagnostics/Probe Biome Road Drop Reach")]
        public static void RunStandalone()
        {
            var log = new StringBuilder();
            log.AppendLine("--- BIOME ROAD DROP REACH (hub terrain + baked navmesh, measured) ---");

            try
            {
                if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), HubScene)))
                {
                    Debug.LogError(log + $"BIOME_ROADS_REACH_FAIL: the hub scene {HubScene} is not on disk - " +
                                         "nothing to measure against.");
                    return;
                }

                EditorSceneManager.OpenScene(HubScene, OpenSceneMode.Single);

                Terrain terrain = Terrain.activeTerrain;
                if (terrain == null)
                {
                    Debug.LogError(log + "BIOME_ROADS_REACH_FAIL: the opened hub has NO active Terrain, so the " +
                                         "world the drops target cannot be measured. (Nothing is concluded about " +
                                         "the navmesh from this - the measurement simply did not happen.)");
                    return;
                }

                if (!BiomeRoads.TryMeasureWorldBounds(out Bounds bounds))
                {
                    Debug.LogError(log + "BIOME_ROADS_REACH_FAIL: BiomeRoads.TryMeasureWorldBounds refused the " +
                                         "opened hub - see its own Fail line above for which condition it hit.");
                    return;
                }

                log.AppendLine($"  world bounds MEASURED: centre {bounds.center} size {bounds.size}; " +
                               $"terrain '{terrain.name}' at {terrain.transform.position}");

                List<BiomeRoads.Drop> drops = BiomeRoads.ResolveDrops(bounds);
                log.AppendLine($"  ResolveDrops derived {drops.Count} of {BiomeRoads.DropRegions.Length} points.");

                int walkable = 0;
                for (int i = 0; i < drops.Count; i++)
                {
                    BiomeRoads.Drop drop = drops[i];
                    Vector3 p = drop.Point;

                    float groundY = terrain.SampleHeight(p) + terrain.transform.position.y;
                    var grounded = new Vector3(p.x, groundY, p.z);

                    bool rawHit = NavMesh.SamplePosition(p, out NavMeshHit rawSample, SeatRadius, NavMesh.AllAreas);
                    bool groundHit = NavMesh.SamplePosition(grounded, out NavMeshHit groundSample, SeatRadius,
                                                            NavMesh.AllAreas);
                    bool anyHit = NavMesh.SamplePosition(grounded, out NavMeshHit farSample, ReportRadius,
                                                         NavMesh.AllAreas);

                    if (groundHit) walkable++;

                    // Every verdict word is computed OUT of the interpolation holes on purpose:
                    // CLAUDE.md sec.1 -- CompileGate's brace scanner has no interpolated-string model,
                    // and a nested quote inside a {...} hole can withhold COMPILE_GATE_OK from a file
                    // whose braces are perfectly balanced.
                    string rawWord = rawHit ? "HIT " + rawSample.position : "MISS";
                    string groundWord = groundHit ? "HIT " + groundSample.position : "MISS";
                    string nearestWord = anyHit
                        ? Vector3.Distance(grounded, farSample.position).ToString("F1") + "m at " + farSample.position
                        : "NONE";

                    log.AppendLine(
                        $"  {BiomeRoads.ZoneName(drop.Region),-12} derived {p} (Y = bounds centre) | terrain ground " +
                        $"Y {groundY:F2} (delta {p.y - groundY:F2}m) | navmesh@{SeatRadius}m from DERIVED: " +
                        $"{rawWord} | from GROUNDED: {groundWord} | nearest navmesh within {ReportRadius}m: " +
                        $"{nearestWord}");
                }

                bool complete = drops.Count == BiomeRoads.DropRegions.Length && walkable == drops.Count;
                log.Append(complete
                    ? $"BIOME_ROADS_REACH_OK {walkable}/{BiomeRoads.DropRegions.Length} derived drop points have " +
                      $"navmesh within {SeatRadius}m once grounded on the terrain."
                    : $"BIOME_ROADS_REACH_SHORT {walkable}/{BiomeRoads.DropRegions.Length} derived drop points are " +
                      "walkable. The rows above carry the terrain delta and the distance to the nearest mesh, " +
                      "which is what separates 'the derived Y was in the air' from 'the bake does not reach'.");

                if (complete) Debug.Log(log.ToString());
                else Debug.LogWarning(log.ToString());
            }
            catch (Exception ex)
            {
                // The stack is the point of a throwing probe (CLAUDE.md sec.12).
                Debug.LogError(log + $"BIOME_ROADS_REACH_FAIL: probe THREW {ex.GetType().Name}: {ex.Message}\n" +
                                     ex.StackTrace);
            }
        }
    }
}
