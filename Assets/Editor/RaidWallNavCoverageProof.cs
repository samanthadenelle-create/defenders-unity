using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    // WO-1722 item 2 — "the wall was not destroyed and i could walk through" is STILL OPEN
    // (the RCA at the bottom of the WO disproved its own original explanation: the giant
    // renderer bounds item 1 found were a transient combat-VFX marker, not an oversized wall,
    // and all 78 Regular-tier walls read X/Z-exact against their collider in the same headed
    // capture). The seam that decides item 2, per that RCA: the hero is a NavMeshAgent driven
    // kinematically (HeroLocomotion.cs:4-8) — a wall's BoxCollider does not stop it at all; only
    // the NavMesh does. RaidNavBake.PrepareDestructibleWalls gives every WallSegment a carving
    // NavMeshObstacle sized from its own collider, but RaidBaseDresser.CladCorners places 8
    // visible "Clad_Corner_S*_L/R" stubs per ring that have NO WallSegment (stripped colliders,
    // excluded from the bake by Zone_Clad name alone) — so nothing in this lane's reading gives
    // THEM a carving obstacle. Geometry suggests the corner tower's own carving obstacle
    // overlaps that span, but the WO says "probably" is not provable and asks for a measurement.
    // This proof is that measurement: it does not read code, it samples the live NavMesh.
    //
    // ⛔ MUST RUN IN PLAY MODE. NavMeshObstacle.carving only applies at runtime; an edit-mode
    // NavMesh.SamplePosition sweep over the ring reads the whole wall line as walkable (the bake
    // deliberately bakes that ground walkable, RaidNavBake.cs:217-219) and proves nothing.
    //
    // Unlike RaidWallTierProof, this probe samples no pixels (no ScreenCapture, no renderer-
    // bounds comparison) — only NavMesh queries and component state — so it is safe to run
    // -batchmode, per the WO's own §(c) minimal command.
    //
    // Batchmode: DeNelle.Editor.RaidWallNavCoverageProof.Run
    public static class RaidWallNavCoverageProof
    {
        private const string Arm = "RaidWallNavCoverageProof.Armed";
        private const string OutDirKey = "RaidWallNavCoverageProof.OutDir";

        // Regular tier only (raider_camp_small) — the tier of the owner's original 09-14 report
        // and the WO's own minimal command target. Item 2's mechanism (carving obstacles /
        // Zone_Clad ownership) is tier-independent per RaidBaseDresser/RaidNavBake, which apply
        // identically to every ring, so one tier is sufficient to answer the coverage question;
        // widen to the full Tiers list (see RaidWallTierProof) if a future run needs to confirm
        // that on Hard/Extreme too.
        private static readonly (string Scene, string TierLabel)[] Tiers =
        {
            ("RaidBase_raider_camp_small", "Regular"),
        };

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("RaidWallNavCoverageProof requires a closed Play session.");

            string outDir = Environment.GetEnvironmentVariable("RAID_WALL_NAV_COVERAGE_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Builds", "raid-wall-nav-coverage");
            Directory.CreateDirectory(outDir);

            SessionState.SetString(OutDirKey, outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Arm, true);
            EditorApplication.EnterPlaymode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeArm()
        {
            if (!SessionState.GetBool(Arm, false)) return;
            SessionState.SetBool(Arm, false);
            var go = new GameObject("RaidWallNavCoverageProofDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>().outDir = SessionState.GetString(OutDirKey, "");
        }

        private sealed class Driver : MonoBehaviour
        {
            public string outDir;
            private void Start() => StartCoroutine(RunAll());

            private IEnumerator RunAll()
            {
                var lines = new List<string>
                {
                    "# Raid wall nav coverage proof (WO-1722 item 2) - " + DateTime.Now.ToString("s"),
                    "Play-mode NavMesh.SamplePosition sweep over every 'Clad_*' piece under Zone_Clad " +
                    "(per-segment panels AND the 8 corner stubs) - answers whether any span of visible " +
                    "standing wall has no carving obstacle actually covering it, at runtime.",
                };
                bool anyFail = false;
                bool sceneFailure = false;

                // Same production route RaidWallTierProof uses: a real hero via SceneRouter, never
                // a raw scene boot (which would skip the hero-carry hook GoRaid relies on).
                SceneRouter.GoCastle();
                float castleDeadline = Time.realtimeSinceStartup + 75f;
                while (SceneManager.GetActiveScene().name != SceneRouter.Castle && Time.realtimeSinceStartup < castleDeadline)
                    yield return null;
                if (SceneManager.GetActiveScene().name != SceneRouter.Castle)
                {
                    lines.Add("\nFAIL - production castle route did not finish within 75s.");
                    sceneFailure = true;
                }

                if (!sceneFailure)
                {
                    float settleUntil = Time.realtimeSinceStartup + 3f;
                    while (Time.realtimeSinceStartup < settleUntil) yield return null;

                    foreach (var (scene, label) in Tiers)
                    {
                        lines.Add($"\n## Tier {label} ({scene})");
                        SceneRouter.GoRaid(scene);
                        float raidDeadline = Time.realtimeSinceStartup + 60f;
                        while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < raidDeadline)
                            yield return null;
                        if (SceneManager.GetActiveScene().name != scene)
                        {
                            lines.Add($"  FAIL - scene did not load within 60s (stuck on '{SceneManager.GetActiveScene().name}').");
                            anyFail = true;
                            continue;
                        }

                        // Let RaidBaseGenerator/RaidBaseDresser finish AND let every carving
                        // NavMeshObstacle settle (carveOnlyStationary needs a stationary frame
                        // before it actually carves) before sampling.
                        float carveSettle = Time.realtimeSinceStartup + 3f;
                        while (Time.realtimeSinceStartup < carveSettle) yield return null;

                        MeasureCoverage(scene, lines, ref anyFail);

                        SceneRouter.GoCastle();
                        float resetDeadline = Time.realtimeSinceStartup + 75f;
                        while (SceneManager.GetActiveScene().name != SceneRouter.Castle && Time.realtimeSinceStartup < resetDeadline)
                            yield return null;
                        if (SceneManager.GetActiveScene().name != SceneRouter.Castle)
                        {
                            lines.Add($"  FAIL - could not reset to castle after tier {label}.");
                            anyFail = true;
                            break;
                        }
                        float resettleUntil = Time.realtimeSinceStartup + 1.5f;
                        while (Time.realtimeSinceStartup < resettleUntil) yield return null;
                    }
                }

                bool overallFail = anyFail || sceneFailure;
                File.WriteAllLines(Path.Combine(outDir, "REPORT.md"), lines);
                string marker = overallFail ? "RAID_WALL_NAV_COVERAGE_FAIL" : "RAID_WALL_NAV_COVERAGE_OK";
                File.WriteAllText(Path.Combine(outDir, overallFail ? "FAIL.txt" : "PASS.txt"), marker);
                FlowTrace.Step("RaidWallNavCoverageProof", marker + " - see " + outDir);
                Debug.Log("[RaidWallNavCoverageProof] " + marker + " - report at " + outDir);

                yield return new WaitForSecondsRealtime(0.5f);
                EditorApplication.isPlaying = false;
                yield return new WaitForSecondsRealtime(1.0f); // let the editor unwind Play mode before exiting
                EditorApplication.Exit(overallFail ? 1 : 0);
            }
        }

        // One line per 'Clad_*' piece: 'Clad_<WallSegment name>' (per-segment panel, owned) or
        // 'Clad_Corner_S<side>_<L|R>' (ownerless stub). Never the nested mesh children under
        // either — GetComponentsInChildren(false) plus the 'Clad_' name-prefix filter keeps each
        // piece counted exactly once.
        private static void MeasureCoverage(string sceneName, List<string> lines, ref bool anyFail)
        {
            var settings = NavMesh.GetSettingsByIndex(0);
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };

            var zone = FindZoneClad();
            if (zone == null)
            {
                lines.Add("  FAIL - no 'Zone_Clad' GameObject found in the loaded raid scene.");
                anyFail = true;
                return;
            }

            var renderers = zone.GetComponentsInChildren<Renderer>(false);
            int pieceCount = 0, blocked = 0, walkableGaps = 0, ownerlessWalkable = 0;

            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                string n = r.transform.name;
                if (!n.StartsWith("Clad_")) continue;

                var owner = r.GetComponentInParent<WallSegment>();
                var b = r.bounds;
                Vector3 centre = new Vector3(b.center.x, b.min.y + 0.15f, b.center.z);

                string obstacleState = "none";
                if (owner != null)
                {
                    var obstacle = owner.GetComponent<NavMeshObstacle>();
                    if (obstacle != null)
                        obstacleState = !obstacle.enabled ? "disabled" : (obstacle.carving ? "carving" : "off");
                }

                bool navSampled = NavMesh.SamplePosition(centre, out _, 0.3f, filter);
                string verdict = navSampled ? "WALKABLE" : "BLOCKED";
                pieceCount++;
                if (navSampled) walkableGaps++; else blocked++;
                if (navSampled && owner == null) ownerlessWalkable++;

                string ownerName = owner != null ? owner.name : "NONE";
                string centreText = centre.x.ToString("F2") + "," + centre.y.ToString("F2") + "," + centre.z.ToString("F2");
                string line = "panel='" + n + "' ownerSegment='" + ownerName + "' obstacle=" + obstacleState +
                    " centre=(" + centreText + ") navSampled=" + navSampled + " -> " + verdict;
                FlowTrace.Step("RaidWallNav", line);
                lines.Add("  " + line);

                // The proving criterion (WO-1722 item 2, "no existing instrument answers it"):
                // navSampled=true on a piece whose owner wall is ALIVE is a hole in a standing
                // wall — the exact walk-through defect, now NAMED instead of inferred. A gap on
                // an ownerless corner stub is reported but does not fail the marker on its own —
                // whether the corner tower's obstacle covers it is the still-open, separately
                // logged question this proof exists to answer, not an assumed pass.
                if (navSampled && owner != null && owner.HpFraction > 0f)
                    anyFail = true;
            }

            string summary = "  -> " + sceneName + ": " + pieceCount + " clad piece(s) sampled, " + blocked +
                " BLOCKED, " + walkableGaps + " WALKABLE (" + ownerlessWalkable + " ownerless/corner-stub, " +
                (walkableGaps - ownerlessWalkable) + " on a live owned WallSegment).";
            lines.Add(summary);
            FlowTrace.Step("RaidWallNav", summary.Trim());

            if (pieceCount == 0)
            {
                lines.Add("  FAIL - 0 'Clad_*' pieces found under Zone_Clad; the dresser may not have run.");
                anyFail = true;
            }
        }

        private static GameObject FindZoneClad()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var found = FindChildNamed(root.transform, "Zone_Clad");
                if (found != null) return found.gameObject;
            }
            return null;
        }

        private static Transform FindChildNamed(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindChildNamed(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
