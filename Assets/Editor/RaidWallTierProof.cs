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
                        MeasureTierChildFootprint(label, lines, ref anyFail);

                        // WO-1722: the live device capture that found Wall_Outer_SE_17's giant
                        // footprint mismatch happened ~90s INTO COMBAT, not at raid start — and
                        // the raid-start measurement just above reproduces NONE of that mismatch.
                        // Re-measure the SAME wall set after a long IDLE settle (no combat, no
                        // player input) to separate "something drifts over time on its own" from
                        // "it takes an actual gameplay event (damage, deploy, AI) to trigger it".
                        // Bounded to the Regular tier only to keep this proof's total runtime sane.
                        if (label == "Regular")
                        {
                            lines.Add("  -- re-measuring child-footprint after 90s IDLE (no combat) --");
                            float longIdleUntil = Time.realtimeSinceStartup + 90f;
                            while (Time.realtimeSinceStartup < longIdleUntil) yield return null;
                            MeasureTierChildFootprint(label + "-after90sIdle", lines, ref anyFail);
                        }

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

            // WO-1722 follow-up (2026-09-14) — PROVEN CAUSE for the "Wall_Outer_SE_35-class"
            // outlier (headed capture, D:\eoa\Builds\raid-wall-tier-proof-diag1\REPORT.md): it is
            // NOT a RaidBaseDresser/CladRing art-fit defect. The oversized renderer bounds belong
            // to a "CastTargetMarker" instance — the Hovl "Marker 2 Pointer Loop" ground VFX
            // `CastingTelegraphVfx.TryBeginTargetMarker` (Assets/_Modules/Village/Vfx/
            // CastingTelegraphVfx.cs:257-268) instantiates PARENTED to whatever unit a spell
            // wind-up is targeting, self-destroying windup+1s later. When an enemy ability targets
            // a WallSegment's transform (an arcane siege bolt at the wall, observed on
            // Wall_Outer_SE_35 in mage_enclave AND Wall_Outer_SS_0 in fortified_garrison — two
            // different walls, two different tiers, same marker children 'CastTargetMarker' ->
            // 'Flash'/'ShockWave'/'Marker' with matching bounds), its still-live marker is a CHILD
            // of the wall at the moment this proof measures, and its ground-AoE-scaled bounds (up
            // to ~14m) get encapsulated into "the wall's visual footprint" by both scans below.
            // Both measurements must exclude this transient combat VFX generically (by the ONE
            // literal name that production code assigns it), not per wall id.
            private static bool IsTransientCastMarker(Transform t)
            {
                for (var cur = t; cur != null; cur = cur.parent)
                    if (cur.name == "CastTargetMarker") return true;
                return false;
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
                        if (IsTransientCastMarker(r.transform)) continue;
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

            // WO-1722: re-derives the SAME numbers RaidDeployController.LogBreachTapDiagnostics
            // captured live (Wall_Outer_SE_17, Regular tier) — GetComponentsInChildren<Renderer>
            // on the WallSegment ITSELF, never a nearby-radius scan across the whole scene, and
            // all THREE axes (X/Y/Z), not just Y. This is the check that actually catches the
            // footprint mismatch MeasureTier's radius scan cannot see (its 1.2m radius can miss or
            // mis-hit a renderer that isn't actually a child of the wall it is nearest to).
            private const float FootprintMismatchToleranceMetres = 0.75f;

            private static void MeasureTierChildFootprint(string label, List<string> lines, ref bool anyFail)
            {
                var walls = UnityEngine.Object.FindObjectsByType<WallSegment>(FindObjectsSortMode.None);
                int checkedCount = 0, failCount = 0, collapsedSkipped = 0, noRendererSkipped = 0;
                float worstDelta = 0f;
                string worstWall = "";
                lines.Add($"  -- child-footprint re-derivation (X/Y/Z, own children only) --");

                foreach (var wall in walls)
                {
                    var box = wall.GetComponent<BoxCollider>();
                    if (box == null || !box.enabled) { collapsedSkipped++; continue; }

                    var rends = wall.GetComponentsInChildren<Renderer>(true);
                    Bounds? rb = null;
                    foreach (var r in rends)
                    {
                        if (r == null) continue;
                        if (IsTransientCastMarker(r.transform)) continue; // see IsTransientCastMarker header
                        if (rb == null) rb = r.bounds; else { var bb = rb.Value; bb.Encapsulate(r.bounds); rb = bb; }
                    }
                    if (rb == null) { noRendererSkipped++; continue; }

                    Vector3 colliderSize = box.bounds.size;
                    Vector3 rendererSize = rb.Value.size;
                    Vector3 delta = new Vector3(
                        Mathf.Abs(rendererSize.x - colliderSize.x),
                        Mathf.Abs(rendererSize.y - colliderSize.y),
                        Mathf.Abs(rendererSize.z - colliderSize.z));
                    float worstAxis = Mathf.Max(delta.x, delta.y, delta.z);

                    checkedCount++;
                    bool ok = worstAxis <= FootprintMismatchToleranceMetres;
                    if (!ok)
                    {
                        failCount++;
                        anyFail = true;
                        if (worstAxis > worstDelta) { worstDelta = worstAxis; worstWall = wall.name; }
                    }
                    lines.Add($"  {(ok ? "ok  " : "FAIL")} {wall.name}: colliderSize={colliderSize:F2} rendererSize={rendererSize:F2} delta={delta:F2}");

                    // WO-1722 follow-up instrumentation: the universal ~1.00m Y residual (the
                    // hidden WallSegment placeholder mesh, unrelated to this ticket) is NOT worth
                    // a per-renderer dump. A gross outlier (Wall_Outer_SE_35-class, >3m on any
                    // axis) is - dump every renderer this WallSegment actually owns as a CHILD
                    // (name, mesh, local scale, world bounds) so the extra/oversized one can be
                    // named directly instead of inferred from aggregate encapsulated size alone.
                    if (worstAxis > 3f)
                    {
                        lines.Add($"    -- outlier dump for {wall.name} ({rends.Length} child renderer(s)) --");
                        foreach (var r in rends)
                        {
                            if (r == null) continue;
                            var mf = r.GetComponent<MeshFilter>();
                            string meshName = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "(none)";
                            lines.Add($"      renderer '{r.name}' mesh='{meshName}' localScale={r.transform.localScale:F3} " +
                                      $"worldBounds.size={r.bounds.size:F3} worldBounds.center={r.bounds.center:F3}");
                        }
                    }
                }

                lines.Add($"  -> tier {label} child-footprint: {checkedCount} wall(s) checked (+{collapsedSkipped} collapsed, +{noRendererSkipped} no child renderer), {failCount} mismatch(es)" +
                          (failCount > 0 ? $", worst={worstWall} worstAxisDelta={worstDelta:F2}m" : "") + ".");
            }
        }
    }
}
