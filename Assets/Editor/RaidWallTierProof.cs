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
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    // Headed, in-editor Play-mode proof (WO-1719/1720 follow-up, 2026-09-14). Confirms the
    // RaidBaseDresser collider/renderer-height fix — and, by extension, WO-1720's tower
    // line-of-sight fix, since both fire-gating checks and the breach tap use the SAME
    // WallSegment BoxCollider — holds across every raid difficulty tier, not just the one
    // wall (Wall_Outer_SS_3, Hard tier) captured live during diagnosis.
    //
    // Follows the established OwnedTownMovePlayProof sequence exactly: SceneRouter.GoCastle()
    // first for a REAL hero via the production route, then SceneRouter.GoRaid(scene) per tier
    // — never a raw scene boot, which would skip the hero-carry hook GoRaid relies on. This
    // must actually render (no -batchmode) to be a meaningful headed proof; run it via
    // `-executeMethod DeNelle.Editor.RaidWallTierProof.Run` on a normal (headed) Unity Editor
    // invocation, never inside a batchmode gate run.
    //
    // Independent check, not a re-read of the fix's own log line: for every WallSegment, reads
    // its BoxCollider height and separately finds the tallest active Renderer within 1.2m of
    // the collider's XZ centre (the clad cosmetic art shares that footprint per
    // RaidBaseDresser.CladRing) and compares. This does not trust the fix's own evidence trail
    // blind — it re-derives the same numbers a second way.
    public static class RaidWallTierProof
    {
        private const string Arm = "RaidWallTierProof.Armed";
        private const string OutDirKey = "RaidWallTierProof.OutDir";
        private const float HeightMismatchToleranceMetres = 0.75f;
        private const float NearbyRendererRadiusMetres = 1.2f;

        // difficulty per Assets/Resources/Data/Canonical/scene-configs.json (read at source
        // 2026-09-14): raider_camp_small=Regular, fortified_garrison=Hard, mage_enclave=Extreme.
        private static readonly (string Scene, string TierLabel)[] Tiers =
        {
            ("RaidBase_raider_camp_small", "Regular"),
            ("RaidBase_fortified_garrison", "Hard"),
            ("RaidBase_mage_enclave", "Extreme"),
        };

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("RaidWallTierProof requires a closed Play session.");

            string outDir = Environment.GetEnvironmentVariable("RAID_WALL_TIER_PROOF_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Builds", "raid-wall-tier-proof");
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
            var go = new GameObject("RaidWallTierProofDriver");
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
                    "# Raid wall tier proof (WO-1719/1720 follow-up) - " + DateTime.Now.ToString("s"),
                    "Independent bounds re-derivation, not a re-read of the fix's own log line.",
                };
                bool anyFail = false;
                bool sceneFailure = false;

                SceneRouter.GoCastle();
                float castleDeadline = Time.realtimeSinceStartup + 75f;
                while (SceneManager.GetActiveScene().name != SceneRouter.Castle && Time.realtimeSinceStartup < castleDeadline)
                    yield return null;
                if (SceneManager.GetActiveScene().name != SceneRouter.Castle)
                {
                    lines.Add("\nFAIL - production castle route did not finish within 75s.");
                    sceneFailure = true;
                }

                HeroLocomotion hero = null;
                if (!sceneFailure)
                {
                    while (Time.realtimeSinceStartup < castleDeadline)
                    {
                        hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                        if (hero != null && hero.gameObject.scene == SceneManager.GetActiveScene()) break;
                        yield return null;
                    }
                    if (hero == null)
                    {
                        lines.Add("\nFAIL - the real castle did not supply a hero.");
                        sceneFailure = true;
                    }
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

                        // Let RaidBaseGenerator (collider sizing) + RaidBaseDresser (clad re-skin
                        // + the WO-1719 fix's collider resync) finish before measuring.
                        float wallSettle = Time.realtimeSinceStartup + 2.5f;
                        while (Time.realtimeSinceStartup < wallSettle) yield return null;

                        MeasureTier(label, lines, ref anyFail);

                        string shotPath = Path.Combine(outDir, $"tier-{label}.png");
                        ScreenCapture.CaptureScreenshot(shotPath);
                        yield return null; // let the screenshot flush before the next scene tears it down

                        // Reset to castle between tiers so each GoRaid starts from the same
                        // clean, production-routed state as the one this proof already verified.
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
                string marker = overallFail ? "RAID_WALL_TIER_PROOF_FAIL" : "RAID_WALL_TIER_PROOF_OK";
                File.WriteAllText(Path.Combine(outDir, overallFail ? "FAIL.txt" : "PASS.txt"), marker);
                FlowTrace.Step("RaidWallTierProof", marker + " - see " + outDir);
                Debug.Log("[RaidWallTierProof] " + marker + " - report at " + outDir);

                yield return new WaitForSecondsRealtime(0.5f);
                EditorApplication.isPlaying = false;
                yield return new WaitForSecondsRealtime(1.0f); // let the editor unwind Play mode before exiting
                EditorApplication.Exit(overallFail ? 1 : 0);
            }

            private static void MeasureTier(string label, List<string> lines, ref bool anyFail)
            {
                var walls = UnityEngine.Object.FindObjectsByType<WallSegment>(FindObjectsSortMode.None);
                var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
                int checkedCount = 0, failCount = 0, collapsedSkipped = 0;
                float worstDelta = 0f;
                string worstWall = "";

                foreach (var wall in walls)
                {
                    var box = wall.GetComponent<BoxCollider>();
                    if (box == null || !box.enabled) { collapsedSkipped++; continue; } // collapsed walls drop their collider — expected, not a mismatch

                    Vector3 c = box.bounds.center;
                    float colliderH = box.bounds.size.y;

                    float nearestVisibleH = 0f;
                    foreach (var r in renderers)
                    {
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                        var b = r.bounds;
                        float dx = b.center.x - c.x, dz = b.center.z - c.z;
                        if (dx * dx + dz * dz > NearbyRendererRadiusMetres * NearbyRendererRadiusMetres) continue;
                        if (b.size.y > nearestVisibleH) nearestVisibleH = b.size.y;
                    }

                    checkedCount++;
                    float delta = Mathf.Abs(nearestVisibleH - colliderH);
                    // nearestVisibleH==0 means no active renderer sits near this collider at all
                    // (e.g. mid-collapse) — not the defect this proof targets, so it does not fail.
                    bool ok = nearestVisibleH <= 0.01f || delta <= HeightMismatchToleranceMetres;
                    if (!ok)
                    {
                        failCount++;
                        anyFail = true;
                        if (delta > worstDelta) { worstDelta = delta; worstWall = wall.name; }
                    }
                    lines.Add($"  {(ok ? "ok  " : "FAIL")} {wall.name}: colliderH={colliderH:F2}m nearestVisibleH={nearestVisibleH:F2}m delta={delta:F2}m");
                }

                lines.Add($"  -> tier {label}: {checkedCount} wall(s) checked (+{collapsedSkipped} collapsed/no-collider skipped), {failCount} mismatch(es)" +
                          (failCount > 0 ? $", worst={worstWall} delta={worstDelta:F2}m" : "") + ".");
            }
        }
    }
}
