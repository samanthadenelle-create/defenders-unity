using System;
using System.Collections;
using System.IO;
using System.Reflection;
using DeNelle.Core;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    // Headed, in-editor Play-mode proof (owner directive 2026-09-14: "you run it headed" -
    // after live device attempts kept landing on already-collapsed walls, never testing the
    // actual fix). This drives the REAL breach-tap code path -
    // RaidDeployController.HandleBreachTap(Vector2) - against a genuinely intact, undamaged
    // wall the moment a raid starts, before any troop has touched it, and asserts
    // TroopBreachOrder actually took the order. This is the one case the live device session
    // never exercised: every tap there hit a wall that had already collapsed.
    //
    // HandleBreachTap is private (it is gated behind the Breach UI toggle in real play) - this
    // proof reaches it via reflection, which is legitimate here: it is testing the exact method
    // body the real tap dispatches to, not a UI-routing detail. Follows the same
    // castle -> real hero -> SceneRouter.GoRaid sequence as OwnedTownMovePlayProof /
    // RaidWallTierProof (both already established in this repo) - never a raw scene boot.
    public static class RaidBreachTapLiveProof
    {
        private const string Arm = "RaidBreachTapLiveProof.Armed";
        private const string OutDirKey = "RaidBreachTapLiveProof.OutDir";
        private const string RaidScene = "RaidBase_raider_camp_small"; // Regular tier - proven fully clean (0/78) by RaidWallTierProof

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("RaidBreachTapLiveProof requires a closed Play session.");

            string outDir = Environment.GetEnvironmentVariable("RAID_BREACH_TAP_PROOF_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.Combine(Application.dataPath, "..", "Builds", "raid-breach-tap-proof");
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
            var go = new GameObject("RaidBreachTapLiveProofDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>().outDir = SessionState.GetString(OutDirKey, "");
        }

        private sealed class Driver : MonoBehaviour
        {
            public string outDir;
            private void Start() => StartCoroutine(RunProof());

            private IEnumerator RunProof()
            {
                bool ok = false;
                string reason = "";

                SceneRouter.GoCastle();
                float castleDeadline = Time.realtimeSinceStartup + 75f;
                while (SceneManager.GetActiveScene().name != SceneRouter.Castle && Time.realtimeSinceStartup < castleDeadline)
                    yield return null;
                if (SceneManager.GetActiveScene().name != SceneRouter.Castle)
                {
                    reason = "FAIL - production castle route did not finish within 75s.";
                }
                else
                {
                    HeroLocomotion hero = null;
                    while (Time.realtimeSinceStartup < castleDeadline)
                    {
                        hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
                        if (hero != null && hero.gameObject.scene == SceneManager.GetActiveScene()) break;
                        yield return null;
                    }
                    if (hero == null)
                    {
                        reason = "FAIL - the real castle did not supply a hero.";
                    }
                    else
                    {
                        float settleUntil = Time.realtimeSinceStartup + 3f;
                        while (Time.realtimeSinceStartup < settleUntil) yield return null;

                        SceneRouter.GoRaid(RaidScene);
                        float raidDeadline = Time.realtimeSinceStartup + 60f;
                        while (SceneManager.GetActiveScene().name != RaidScene && Time.realtimeSinceStartup < raidDeadline)
                            yield return null;
                        if (SceneManager.GetActiveScene().name != RaidScene)
                        {
                            reason = "FAIL - scene did not load within 60s (stuck on '" + SceneManager.GetActiveScene().name + "').";
                        }
                        else
                        {
                            // Tap IMMEDIATELY (one settle frame only) so nothing has had a chance
                            // to damage or collapse any wall yet - the live device session's own
                            // failure mode (every tap landing on an already-collapsed wall) is
                            // structurally impossible here.
                            for (int frame = 0; frame < 3; frame++) yield return null;

                            RaidDeployController deploy = UnityEngine.Object.FindAnyObjectByType<RaidDeployController>();
                            WallSegment[] walls = UnityEngine.Object.FindObjectsByType<WallSegment>(FindObjectsSortMode.None);
                            WallSegment target = null;
                            BoxCollider targetBox = null;

                            foreach (var w in walls)
                            {
                                var box = w.GetComponent<BoxCollider>();
                                if (box == null || !box.enabled) continue;   // collapsed / no collider
                                if (w.HpFraction < 0.999f) continue;         // must be fully undamaged
                                target = w;
                                targetBox = box;
                                break;
                            }

                            if (deploy == null)
                            {
                                reason = "FAIL - no RaidDeployController instance found in the raid scene.";
                            }
                            else if (target == null)
                            {
                                reason = "FAIL - no fully-undamaged WallSegment with an enabled collider was found " +
                                         "within 3 frames of raid start (" + walls.Length + " WallSegment(s) total).";
                            }
                            else
                            {
                                Camera cam = Camera.main;
                                if (cam == null)
                                {
                                    reason = "FAIL - no Camera.main in the raid scene.";
                                }
                                else
                                {
                                    Vector3 worldPoint = targetBox.bounds.center;
                                    Vector3 screen3 = cam.WorldToScreenPoint(worldPoint);
                                    if (screen3.z <= 0f)
                                    {
                                        reason = "FAIL - target wall '" + target.name + "' bounds centre is BEHIND Camera.main " +
                                                 "(screen.z=" + screen3.z.ToString("F2") + ") - cannot compute a valid tap point this way.";
                                    }
                                    else
                                    {
                                        Vector2 screenPoint = new Vector2(screen3.x, screen3.y);

                                        MethodInfo handleTap = typeof(RaidDeployController).GetMethod(
                                            "HandleBreachTap", BindingFlags.NonPublic | BindingFlags.Instance);
                                        FieldInfo breachModeField = typeof(RaidDeployController).GetField(
                                            "_breachMode", BindingFlags.NonPublic | BindingFlags.Instance);

                                        if (handleTap == null || breachModeField == null)
                                        {
                                            reason = "FAIL - reflection could not find HandleBreachTap/_breachMode on " +
                                                     "RaidDeployController - the method or field was renamed since this proof was written.";
                                        }
                                        else
                                        {
                                            breachModeField.SetValue(deploy, true); // arm Breach exactly as the UI toggle would
                                            FlowTrace.Step("RaidBreachTapLiveProof",
                                                "tapping intact wall '" + target.name + "' (HpFraction=" + target.HpFraction.ToString("F3") +
                                                ") at screenPoint=" + screenPoint + " via reflection into the real HandleBreachTap.");
                                            handleTap.Invoke(deploy, new object[] { screenPoint });

                                            for (int frame = 0; frame < 3; frame++) yield return null;

                                            bool hasOrder = TroopBreachOrder.HasOrder;
                                            IDamageable orderTarget = TroopBreachOrder.Target;
                                            bool matches = hasOrder && ReferenceEquals(orderTarget, (IDamageable)target);

                                            if (matches)
                                            {
                                                ok = true;
                                                reason = "PASS - TroopBreachOrder.Target is exactly the tapped wall '" + target.name +
                                                         "' after the real HandleBreachTap call. The fix holds for an intact wall.";
                                            }
                                            else if (hasOrder)
                                            {
                                                reason = "FAIL - TroopBreachOrder took an order, but for a DIFFERENT object than the " +
                                                         "one tapped ('" + target.name + "'). Order target=" + (orderTarget as UnityEngine.Object)?.name;
                                            }
                                            else
                                            {
                                                reason = "FAIL - TroopBreachOrder.HasOrder is still false after tapping the intact wall " +
                                                         "'" + target.name + "' - the tap did not register as hitting that wall's collider.";
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                File.WriteAllText(Path.Combine(outDir, "REPORT.md"),
                    "# Raid breach-tap LIVE proof (owner-directed headed run) - " + DateTime.Now.ToString("s") + "\n\n" + reason + "\n");
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "breach-tap-attempt.png"));
                yield return null;

                string marker = ok ? "RAID_BREACH_TAP_PROOF_OK" : "RAID_BREACH_TAP_PROOF_FAIL";
                File.WriteAllText(Path.Combine(outDir, ok ? "PASS.txt" : "FAIL.txt"), marker + " - " + reason);
                FlowTrace.Step("RaidBreachTapLiveProof", marker + " :: " + reason);
                Debug.Log("[RaidBreachTapLiveProof] " + marker + " :: " + reason);

                yield return new WaitForSecondsRealtime(0.5f);
                EditorApplication.isPlaying = false;
                yield return new WaitForSecondsRealtime(1.0f);
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }
    }
}
