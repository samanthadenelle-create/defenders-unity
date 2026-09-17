// =============================================================================
// FeedbackLabelSizeRegression [feedback-label-size] — WO-1814
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Shape: public static bool Run(out string reason) — registered in DataRegression.RunAll.
//
// THE CAPTURE THIS PINS (owner's Seeker, build 372984, 2026-09-16 20:51:30, town wave 13):
//   logs/device/owner-fireball-20260916/Screenshot_20260916-205131.png
//   logcat 20:51:30.479  [Flow:Feedback] text label spawned 'LEVEL UP!  Lv.8'
//
// "LEVEL UP!  Lv.8" rendered as translucent green letters ~318 px of CAP HEIGHT on a
// 2670x1200 screen — 26% of the screen height — with the 15-character string running off
// BOTH edges. Measured off the capture, not estimated: green ink (G-R>18 && G-B>18)
// occupies rows y=160..478 and no row above y=160.
//
// THE ARITHMETIC, which is the entire finding:
//   A Unity TextMesh renders its em box at characterSize * fontSize / 10 WORLD UNITS.
//   DamageNumberSpawner builds the label at characterSize 0.11, fontSize 96, and
//   ProgressionManager.Grant asks for scale 1.4:
//       0.11 * 96 / 10 * 1.4 = 1.478 m of world height.
//   The town over-the-shoulder camera logs a seat 3.3-4.7 m from the hero that session
//   ([Flow:Camera] "seat 3.9m of 3.9m", 20:50:40), at vfov 60 (logcat "fov=60.0";
//   Main_Castle_Overworld.unity:22981 "field of view: 60"). On a 1200 px-tall frame
//   that is 1200 / (2*tan30) = 1039.2 px per metre-of-height per metre-of-distance, so
//       1.478 * 1039.2 / 3.5 = 439 px of em box, ~314 px of cap height.
//   The capture measures 318. THE CODE WAS DOING EXACTLY WHAT IT SAID. Nothing was
//   mis-scaled; the term that decides on-screen size — CAMERA DISTANCE — was absent.
//
// ⚠ THE HYPOTHESIS THIS FILE DELIBERATELY REFUTES. The first attempt at this ticket
// clamped the scale 1.4 -> 1.0 and called it fixed. Put 1.0 through the same arithmetic:
// 1.056 m em -> 314 px em, ~224 px cap, still 19% of screen height, and the 15-character
// string still ~1.3x wider than the screen. Case [clamp-alone-is-not-a-fix] encodes that
// refutation so the clamp cannot come back as a "simplification".
//
// CASES
//   1 [observed-capture]         requested 1.4 at the measured 3.5 m seat now yields a
//                                world em <= 1.5 m and <= 14% of screen height.
//   2 [screen-constant]          on-screen height is the SAME (within 1%) at 4 m and 9 m —
//                                that invariance IS the fix; a distance-free size is the bug.
//   3 [never-larger-than-before] no distance produces a world size larger than the
//                                pre-WO-1814 one. This change may only shrink.
//   4 [close-floor]              a camera jammed to 1 m does not shrink the label to nothing.
//   5 [string-fits-width]        "LEVEL UP!  Lv.8" fits inside 2670 px at the capture seat.
//   6 [clamp-alone-is-not-a-fix] the refuted hypothesis, kept as arithmetic.
//   7 [source-pin]               BuildLabel still routes through DistanceCompensatedScale
//                                and still carries its FlowTrace (CLAUDE.md §12: never strip).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using DeNelle.Village;

namespace DeNelle.Editor
{
    /// <summary>Oracle for the world-space feedback label's on-screen size (WO-1814).</summary>
    public static class FeedbackLabelSizeRegression
    {
        private const string SpawnerSrc = "Assets/_Modules/Village/Enemies/DamageNumberSpawner.cs";

        /// <summary>Device frame the owner captured on: 2670x1200, vfov 60.</summary>
        private const float ScreenHeightPx = 1200f;
        private const float ScreenWidthPx  = 2670f;
        private const float VerticalFovDeg = 60f;

        /// <summary>Camera seat measured in that session's [Flow:Camera] lines (3.3-4.7 m).</summary>
        private const float CaptureCameraDistance = 3.5f;

        /// <summary>Scale ProgressionManager.Grant asks for on a level-up.</summary>
        private const float LevelUpScale = 1.4f;

        /// <summary>Average advance width of a BOLD capital in em. Arial Bold caps run
        /// ~0.6-0.72 em; 0.62 is the working figure and the width case states it, because a
        /// width assertion with an unnamed assumption is not evidence.</summary>
        private const float BoldCapAdvanceEm = 0.62f;

        /// <summary>Batchmode entry: writes its own distinct marker, exits 1 on failure.</summary>
        public static void RunAll()
        {
            bool ok;
            string reason;
            try
            {
                ok = Run(out reason);
            }
            catch (Exception ex)
            {
                ok = false;
                reason = "threw " + ex.GetType().Name + ": " + ex.Message;
            }

            Debug.Log((ok ? "FEEDBACK_LABEL_SIZE_OK " : "FEEDBACK_LABEL_SIZE_FAIL ") + reason);
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>Pixels of on-screen height per world metre at <paramref name="distance"/>.</summary>
        private static float PxPerMetre(float distance)
        {
            float halfFovRad = VerticalFovDeg * 0.5f * Mathf.Deg2Rad;
            return ScreenHeightPx / (2f * Mathf.Tan(halfFovRad) * Mathf.Max(0.01f, distance));
        }

        /// <summary>On-screen em-box height (px) of a label requested at
        /// <paramref name="requested"/> and seen from <paramref name="distance"/> metres.</summary>
        private static float OnScreenEmPx(float requested, float distance)
        {
            float worldScale = DamageNumberSpawner.DistanceCompensatedScale(requested, distance);
            return DamageNumberSpawner.LabelWorldEmHeight(worldScale) * PxPerMetre(distance);
        }

        /// <summary>
        /// DataRegression-shaped contract. True when every case passes; <paramref name="reason"/>
        /// always carries a human-readable summary. Never throws — RunAll folds an unexpected
        /// exception into a failure.
        /// </summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();

            // ── 1 [observed-capture] — the owner's exact inputs ──────────────
            float capScale = DamageNumberSpawner.DistanceCompensatedScale(LevelUpScale, CaptureCameraDistance);
            float capEmM   = DamageNumberSpawner.LabelWorldEmHeight(capScale);
            float capEmPx  = capEmM * PxPerMetre(CaptureCameraDistance);
            float capFrac  = capEmPx / ScreenHeightPx;
            if (capEmM > 1.5f || capFrac > 0.14f)
                failures.Add($"[observed-capture] scale {LevelUpScale:0.00} at the measured {CaptureCameraDistance:0.0}m " +
                             $"seat gives worldEm={capEmM:0.000}m ({capEmPx:0}px, {capFrac:P0} of a 1200px screen) — " +
                             "the band is <=1.5m and <=14%; the capture that opened WO-1814 read 1.478m / 26%");
            else log.Append($"[observed-capture] ok ({capEmM:0.000}m, {capFrac:P0}); ");

            // ── 2 [screen-constant] — the invariance IS the fix ──────────────
            float px4 = OnScreenEmPx(LevelUpScale, 4f);
            float px9 = OnScreenEmPx(LevelUpScale, 9f);
            if (Mathf.Abs(px4 - px9) > 0.01f * Mathf.Max(px4, px9))
                failures.Add($"[screen-constant] on-screen height moved with distance inside the clamp band " +
                             $"({px4:0.0}px at 4m vs {px9:0.0}px at 9m) — a label whose screen size depends on how " +
                             "close the camera sits is the WO-1814 defect itself");
            else log.Append($"[screen-constant] ok ({px4:0.0}px); ");

            // ── 3 [never-larger-than-before] — this change may only shrink ───
            float preFixEmM = DamageNumberSpawner.LabelWorldEmHeight(LevelUpScale);
            foreach (float d in new[] { 0.5f, 2f, 5f, 10f, 25f, 120f })
            {
                float em = DamageNumberSpawner.LabelWorldEmHeight(
                    DamageNumberSpawner.DistanceCompensatedScale(LevelUpScale, d));
                if (em > preFixEmM + 1e-4f)
                {
                    failures.Add($"[never-larger-than-before] at {d:0.0}m the label is {em:0.000}m of world em, " +
                                 $"larger than the pre-WO-1814 {preFixEmM:0.000}m — the distance factor's ceiling " +
                                 "has been raised above 1.0 and the fix can now grow a label instead of only shrinking one");
                    break;
                }
            }
            if (failures.Count == 0 || !failures[failures.Count - 1].StartsWith("[never-larger"))
                log.Append($"[never-larger-than-before] ok (<= {preFixEmM:0.000}m); ");

            // ── 4 [close-floor] — do not shrink to nothing at point blank ────
            float nearScale = DamageNumberSpawner.DistanceCompensatedScale(LevelUpScale, 1f);
            if (nearScale < LevelUpScale * 0.25f)
                failures.Add($"[close-floor] a 1m camera collapses the label to scale {nearScale:0.000} " +
                             $"(< {LevelUpScale * 0.25f:0.000}) — the floor that stops a point-blank seat from " +
                             "erasing the level-up moment is gone");
            else log.Append($"[close-floor] ok ({nearScale:0.000}); ");

            // ── 5 [string-fits-width] — the symptom was horizontal too ───────
            const string Captured = "LEVEL UP!  Lv.8";
            float widthPx = Captured.Length * BoldCapAdvanceEm * capEmPx;
            if (widthPx > ScreenWidthPx * 0.85f)
                failures.Add($"[string-fits-width] '{Captured}' ({Captured.Length} chars at {BoldCapAdvanceEm:0.00} em " +
                             $"average bold-cap advance) spans {widthPx:0}px of a {ScreenWidthPx:0}px frame at the " +
                             "capture seat — the owner's screenshot shows it running off both edges");
            else log.Append($"[string-fits-width] ok ({widthPx:0}px); ");

            // ── 6 [clamp-alone-is-not-a-fix] — the refuted first attempt ─────
            float clampOnlyEmM  = DamageNumberSpawner.LabelWorldEmHeight(1.0f);
            float clampOnlyPx   = clampOnlyEmM * PxPerMetre(CaptureCameraDistance);
            float clampOnlyFrac = clampOnlyPx / ScreenHeightPx;
            if (clampOnlyFrac <= 0.14f)
                failures.Add($"[clamp-alone-is-not-a-fix] a bare 1.4->1.0 scale clamp now computes to {clampOnlyFrac:P0} " +
                             "of screen height at the capture seat, which would mean the arithmetic in this file no " +
                             "longer matches the one that refuted it — re-derive before trusting either");
            else log.Append($"[clamp-alone-is-not-a-fix] ok (clamp alone still {clampOnlyFrac:P0}); ");

            // ── 7 [source-pin] — the seam and its trace must survive ─────────
            string src = ReadSource(SpawnerSrc, failures);
            if (src != null)
            {
                if (!src.Contains("DistanceCompensatedScale(scale, camDistance)"))
                    failures.Add("[source-pin] DamageNumberSpawner.BuildLabel no longer routes its scale through " +
                                 "DistanceCompensatedScale — the camera-distance term WO-1814 added has been removed " +
                                 "and the label is sized in metres again");
                else if (!src.Contains("label-size"))
                    failures.Add("[source-pin] the BuildLabel FlowTrace that names requested/camDist/emHeight/parent " +
                                 "is gone. CLAUDE.md §12: instrumentation is PERMANENT — stripping it turns the next " +
                                 "size report back into a screenshot-and-protractor job");
                else log.Append("[source-pin] ok; ");
            }

            if (failures.Count > 0)
            {
                reason = $"{failures.Count} failure(s): " + string.Join(" | ", failures);
                return false;
            }

            reason = "7/7 cases pass — " + log.ToString().TrimEnd(' ', ';');
            return true;
        }

        /// <summary>Read a source file, recording a failure (and returning null) if it is gone.</summary>
        private static string ReadSource(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add($"[source-pin] {path} is missing — the feedback label spawner this suite pins is gone");
                return null;
            }
            return File.ReadAllText(path);
        }
    }
}
