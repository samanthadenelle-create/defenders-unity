// =============================================================================
// BiomeDropGroundProbeRegression [biome-drop-ground-probe]
//   Markers: BIOME_DROP_GROUND_PROBE_OK / BIOME_DROP_GROUND_PROBE_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Reflection + source only - no scene, no play mode,
// and deliberately NO navmesh query (there is no baked navmesh in batchmode; asking for
// one and passing anyway is the hollow pass this file exists to avoid).
//
// THE PIN WO-1091 SHIPPED WITHOUT. The fix landed in 6a5c7a36d and its RESULT file says
// "No regression pin was added for the seat-time refusal or the three-verdict message."
// Mapped to WorkOrders/WORK_ORDER_1091_stoneback_drop_not_ground_probed.md:
//
//   item 1  "a drop point that cannot be grounded refuses the door and says so loudly,
//           rather than seating a door that cannot work"          -> cases B, C
//   item 3  "the timeout message ... names whether the hero reached the point and was
//           moved, or never reached it"                            -> case E
//   item 2  "the Stoneback door delivers the hero inside the 8 m settle radius" is a
//           RUNTIME fact about a walked door. Case D pins the precondition that makes it
//           possible (the grounded point is the ONE point both consumers get); the
//           delivery itself is unpinnable headless and is NOT claimed - see case F.
//
// ⚠ THE RISK THIS SUITE IS WRITTEN TO KEEP VISIBLE (RESULT file, "WHAT IS NOT PROVEN"):
// the probe may now REFUSE doors rather than deliver them. The drop's Y is the world
// bounds CENTRE height (Assets/_Modules/Core/World/BiomeRoads.cs:420,
// `new Vector3(dir.x * reach, worldBounds.center.y, dir.z * reach)` - read at source
// 2026-09-09), and the seat probe searches ArrivalSampleRadius. If the walkable surface
// under a drop sits further below that centre height than the radius reaches, the probe
// misses and the player is told the road is closed. Case F declares that hole as a
// PARTIAL-SKIP with the numbers it can read, rather than passing over it in silence: a
// green suite that hid this would be worse than no suite, because the toast looks like a
// new bug and this file is where the next reader will come looking.
//
// NO COPIED CONSTANTS (CLAUDE.md sec.8): the radii are REFLECTED off the producer, never
// retyped here. A retune moves the assertion with the code; a literal would rot in place.
//
// NO HOLLOW PASS: a missing producer file or a missing const is the FIXTURE - it FAILS,
// naming what it could not read. Exactly one `return` in Run, and it is the failure count.
//
// Standalone batch entry:
//   -Method DeNelle.Editor.Regression.BiomeDropGroundProbeRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DeNelle.Village.World;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1091: a biome road drop is ground-probed before it is seated, refuses
    /// fail-closed when it cannot be grounded, and promises the SAME point it warps to.</summary>
    public static class BiomeDropGroundProbeRegression
    {
        private const string MarkerOk   = "BIOME_DROP_GROUND_PROBE_OK";
        private const string MarkerFail = "BIOME_DROP_GROUND_PROBE_FAIL";

        private const string InjectorSrc = "_Modules/Village/World/HollowRoadsDropInjector.cs";
        private const string RoadsSrc    = "_Modules/Core/World/BiomeRoads.cs";

        [MenuItem("Defenders/Regression/Biome Drop Ground Probe")]
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason);
            else Debug.LogError(reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            var notes = new List<string>();

            // ── A [radii] the two radii, read off the producer, in the right order ──
            bool gotSample = TryFloatConst(typeof(HollowRoadsDropInjector), "ArrivalSampleRadius", out float sampleR);
            bool gotSettle = TryFloatConst(typeof(HollowRoadsDropInjector), "ArrivalSettleRadius", out float settleR);
            if (!gotSample)
                failures.Add("[radii] HollowRoadsDropInjector.ArrivalSampleRadius could not be read as a float " +
                             "constant. It is the radius BOTH the seat probe and the arrival judge use; if it has " +
                             "become a serialized field or been renamed, re-point this case in the same change - " +
                             "an unreadable knob is not a passing one.");
            if (!gotSettle)
                failures.Add("[radii] HollowRoadsDropInjector.ArrivalSettleRadius could not be read as a float " +
                             "constant - the settle test the WO's item 2 is written against.");
            if (gotSample && gotSettle)
            {
                if (sampleR <= 0f || settleR <= 0f)
                    failures.Add($"[radii] a non-positive radius (sample={sampleR}, settle={settleR}) disables the " +
                                 "probe: NavMesh.SamplePosition with radius 0 answers only for a point already on " +
                                 "the mesh, so every derived drop would be refused.");
                else if (sampleR < settleR)
                    failures.Add($"[radii] the seat probe searches {sampleR}m while arrival is judged inside " +
                                 $"{settleR}m. Choosing the point more tightly than the trip is judged means a door " +
                                 "can seat and then fail its own arrival test - the inversion the producer's " +
                                 "comment at the probe warns about.");
                else
                    log.AppendLine($"  [radii] sample={sampleR}m >= settle={settleR}m (both read by reflection)");
            }

            string src = ReadCode(InjectorSrc);
            if (src == null)
            {
                failures.Add("[fixture] " + InjectorSrc + " is MISSING - the ground probe cannot be verified. The " +
                             "file under test is not an optional dependency.");
            }
            else
            {
                // ── B [one-authority] every probe in this file uses the SAME radius ──
                int probes = Count(src, "NavMesh.SamplePosition(");
                int withConst = Count(src, "ArrivalSampleRadius, NavMesh.AllAreas");
                if (probes == 0)
                    failures.Add("[one-authority] the injector no longer probes the navmesh at all. Without a probe " +
                                 "the drop is seated at its DERIVED point - the y=centre-height point that warped " +
                                 "the hero off-mesh in F8 seq 4706 - and nothing notices until the player is there.");
                else if (withConst < probes)
                    failures.Add($"[one-authority] {probes} navmesh probe(s) in the injector but only {withConst} " +
                                 "pass ArrivalSampleRadius. Using one figure to CHOOSE the point and another to " +
                                 "JUDGE the arrival is how a door passes seating and fails arrival - the producer " +
                                 "comment says so at the probe itself.");
                else
                    log.AppendLine($"  [one-authority] all {probes} navmesh probes share ArrivalSampleRadius");

                // ── C [fail-closed] item 1: a miss refuses the door, loudly, to the PLAYER ──
                string seat = Between(src, "if (!NavMesh.SamplePosition(drop.Point", "Vector3 groundedPoint");
                if (seat == null)
                {
                    failures.Add("[fail-closed] the seat-time probe of drop.Point is gone (or no longer guards the " +
                                 "grounded point that follows it). WO-1091 item 1 is exactly this branch: an " +
                                 "ungrounded drop must seat NO door.");
                }
                else
                {
                    if (!seat.Contains("FlowTrace.Fail("))
                        failures.Add("[fail-closed] the refusal no longer raises FlowTrace.Fail. A refusal logged " +
                                     "below error level never reaches the F8 break-log, so the door dead-ends with " +
                                     "no capture behind it (CLAUDE.md sec.12).");
                    if (!seat.Contains("Notify("))
                        failures.Add("[fail-closed] the refusal no longer tells the PLAYER anything. 'Says so loudly' " +
                                     "in item 1 means on screen as well as in the log - otherwise the arm is a " +
                                     "silently dead door.");
                    if (!seat.Contains("return false;"))
                        failures.Add("[fail-closed] the refusal branch does not RETURN. Logging a miss and then " +
                                     "seating the door anyway is the pre-fix behaviour with extra noise.");
                    if (seat.Contains("FlowTrace.Fail(") && seat.Contains("Notify(") && seat.Contains("return false;"))
                        log.AppendLine("  [fail-closed] an ungrounded drop is refused: Fail + player Notify + no seat");
                }

                // ── D [one-point] the grounded point is what BOTH consumers get ──
                bool grounded = src.Contains("Vector3 groundedPoint = groundHit.position;");
                bool toSeam   = src.Contains("seam.targetPosition = groundedPoint;");
                bool toPromise= src.Contains("announce.PromisedPoint = groundedPoint;");
                if (!grounded)
                    failures.Add("[one-point] the probe's hit position is no longer captured as the grounded point. " +
                                 "The probe then proves nothing: it answers 'there is mesh nearby' and the door " +
                                 "still targets the derived coordinate.");
                if (!toSeam)
                    failures.Add("[one-point] the scene seam no longer targets the GROUNDED point. This is the " +
                                 "consumption that matters - a probe whose answer never reaches targetPosition is " +
                                 "the unhonoured BiomeRoads.ResolveDrops contract all over again.");
                if (!toPromise)
                    failures.Add("[one-point] the arrival promise no longer carries the GROUNDED point. Promising " +
                                 "one coordinate and warping to another puts the drift test back to measuring " +
                                 "against a point nothing uses - which is how WO-1604 was minted against the wrong " +
                                 "system.");
                if (src.Contains("seam.targetPosition = drop.Point") || src.Contains("PromisedPoint = drop.Point"))
                    failures.Add("[one-point] a consumer has gone back to the RAW derived drop.Point. That point is " +
                                 "seated at the world bounds CENTRE height, metres in the air on any terrain whose " +
                                 "bounds are not floor-seated.");
                if (grounded && toSeam && toPromise)
                    log.AppendLine("  [one-point] seam target and arrival promise both receive the grounded point");

                // ── E [three-verdicts] item 3: the timeout message tells the cases apart ──
                if (!src.Contains("VerifyArrival(sceneName, hero, waited, closestDrift)"))
                    failures.Add("[three-verdicts] the settle loop no longer hands VerifyArrival the closest drift " +
                                 "it observed. Without it the failure message cannot say whether the hero EVER " +
                                 "reached the promised point, which is precisely what item 3 asks for.");
                else if (!src.Contains("everReached") || !src.Contains("promisedOutsideClamp") ||
                         !src.Contains("sittingOnClampEdge"))
                    failures.Add("[three-verdicts] the arrival failure has collapsed back to fewer than three " +
                                 "verdicts (reached-and-moved / clamped / never-warped). The flat 'the warp did not " +
                                 "happen' sentence was FALSE on F8 seq 4706 and sent the reader into two systems " +
                                 "that had done their jobs - an alarm naming the wrong owner costs more than no " +
                                 "alarm.");
                else
                    log.AppendLine("  [three-verdicts] the arrival failure separates reached / clamped / never-warped");
            }

            // ── F [ground-clearance] the hole, declared rather than hidden ──
            string roads = ReadCode(RoadsSrc);
            if (roads == null)
            {
                failures.Add("[fixture] " + RoadsSrc + " is MISSING - the drop point's derivation cannot be read.");
            }
            else if (!roads.Contains("worldBounds.center.y"))
            {
                log.AppendLine("  [ground-clearance] the drop Y is no longer the world-bounds centre height - the " +
                               "headroom risk below may be obsolete; re-read BiomeRoads before trusting this note");
            }
            else
            {
                notes.Add(RegressionOutcome.PartialSkip("ground-clearance",
                    "the drop Y is still worldBounds.center.y (BiomeRoads.cs:420) and the seat probe searches " +
                    (gotSample ? sampleR.ToString("0.#") + "m" : "an unreadable radius") +
                    " - so a walkable surface further below the bounds centre than that radius reaches makes the " +
                    "probe MISS and the door refuse ('The road to <X> is closed.'). Whether that is true at any of " +
                    "the four live drops is NOT decidable here: it needs a loaded scene with a baked navmesh, and " +
                    "batchmode has neither. The hub bounds were measured at centre y=17 in an EARLIER run " +
                    "(Builds/starter-settlement-proof-r4.log:19075, cited by BiomeRoadDropClassificationRegression's " +
                    "header - NOT re-measured this session). If the owner sees that toast, the finding is 'the " +
                    "probe radius is too small for the derived Y', not 'the fix did not land'"));
            }

            string noteText = notes.Count == 0 ? string.Empty : "\n  " + string.Join("\n  ", notes);
            reason = failures.Count == 0
                ? MarkerOk + " biome drops are ground-probed at seat time, refuse fail-closed, and promise the same " +
                  "point they warp to\n" + log.ToString().TrimEnd() + noteText
                : MarkerFail + " x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }

        /// <summary>Read a private/public float CONSTANT off a producer type, so the assertion moves
        /// with a retune instead of rotting as a copied literal (CLAUDE.md sec.8).</summary>
        private static bool TryFloatConst(Type type, string name, out float value)
        {
            value = 0f;
            if (type == null) return false;
            FieldInfo f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (f == null || !f.IsLiteral || f.IsInitOnly) return false;
            object raw = f.GetRawConstantValue();
            if (!(raw is float)) return false;
            value = (float)raw;
            return true;
        }

        /// <summary>Read a tracked producer source file. Null when absent - every caller treats that
        /// as a FAILURE (fixture rule, INSTRUMENTATION_STANDARD sec.8.5), never as a skip.</summary>
        private static string ReadCode(string assetsRelativePath)
        {
            string full = Path.Combine(Application.dataPath, assetsRelativePath);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        private static int Count(string src, string token)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(token)) return 0;
            int n = 0, i = 0;
            while ((i = src.IndexOf(token, i, StringComparison.Ordinal)) >= 0) { n++; i += token.Length; }
            return n;
        }

        /// <summary>The slice between two anchors, so a case asserts a branch is where it must be.
        /// Null when either anchor is gone or out of order - reported, never silently passed.</summary>
        private static string Between(string src, string startToken, string endToken)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int a = src.IndexOf(startToken, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(endToken, a + startToken.Length, StringComparison.Ordinal);
            if (b < 0) return null;
            return src.Substring(a, b - a);
        }
    }
}
