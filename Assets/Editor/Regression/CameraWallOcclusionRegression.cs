using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1734: the WO-385 occlusion contract — FADE the occluder and HOLD the seat; pull in ONLY
    /// when an occluder is point-blank, and never closer than the authored floor.
    /// <para>
    /// ⚠ THIS SUITE PREVIOUSLY PINNED THE DEFECT, AND THAT IS THE FINDING. It was born in commit
    /// 486cd7b17 (2026-09-01) — the SAME commit that replaced WO-385's fade with the DEF-151 hard
    /// pull-in — and it asserted `!method.Contains("FadeOccluder(col)")` plus
    /// `method.Contains("nearestOccluderDist &lt; float.MaxValue")`, i.e. it required the regression
    /// to stay. Its header cited "WO-1289", which is
    /// `WorkOrders/WORK_ORDER_1289_ground_meadow_regrade_chroma_oracle.md` — the ground-meadow
    /// regrade, nothing to do with the camera — so the citation never pointed at a ruling. Both
    /// source-text assertions are INVERTED below under the owner's 2026-09-15 ruling
    /// ("Restore the fade + guard as documented"). Nothing is weakened: the same two lines are
    /// still pinned, now to the documented contract instead of against it.
    /// </para>
    /// </summary>
    public static class CameraWallOcclusionRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            // Floor argument = the authored _minCollisionDistance default (1.2 m).
            float normal = SmartMobileCamera.AllowedCameraDistance(5f, 3f, 0.2f, 1.2f);
            if (Math.Abs(normal - 2.8f) > 0.001f)
                failures.Add("normal wall hit did not seat camera at hit minus skin");

            // WO-1734: the floor is the AUTHORED minimum, not the old bare 0.25f literal.
            float tight = SmartMobileCamera.AllowedCameraDistance(5f, 0.1f, 0.2f, 1.2f);
            if (Math.Abs(tight - 1.2f) > 0.001f)
                failures.Add("tight wall hit did not use the authored _minCollisionDistance floor");

            // The boom always wins the ceiling: a floor wider than the seat must not push the
            // camera further out than its own authored distance (Mathf.Clamp min>max trap).
            float floorAboveBoom = SmartMobileCamera.AllowedCameraDistance(0.9f, 0.1f, 0.2f, 1.2f);
            if (Math.Abs(floorAboveBoom - 0.9f) > 0.001f)
                failures.Add("a floor wider than the boom pushed the camera past its authored seat");

            float clearCap = SmartMobileCamera.AllowedCameraDistance(5f, 9f, 0.2f, 1.2f);
            if (Math.Abs(clearCap - 5f) > 0.001f)
                failures.Add("collision distance exceeded the authored camera boom");

            string path = Path.Combine("Assets", "_Modules", "Village", "Hero", "SmartMobileCamera.cs");
            string source = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            string method = ExtractMethodBody(source, "private Vector3 ApplyCollision");
            if (source.Length > 0 && method.Length == 0)
                failures.Add("ApplyCollision could not be located - the source-text pins below did not run");

            // INVERTED under the owner's 2026-09-15 ruling — see the type summary.
            if (!method.Contains("FadeOccluder(col)"))
                failures.Add("ApplyCollision no longer fades the occluder (WO-385 contract lost)");
            if (!method.Contains("RestoreFadedNotHitThisFrame()"))
                failures.Add("faded occluders are never restored, so a wall can stay invisible");
            // ⚠ SCOPE MATTERS ON THIS ONE, AND IT IS THE REASON ExtractMethodBody EXISTS.
            // `nearestOccluderDist < float.MaxValue` is FORBIDDEN inside ApplyCollision (it is the
            // DEF-151 hard pull-in) but LEGITIMATE in TraceOcclusionOutcome, where it is a null
            // sentinel deciding whether there is an occluder distance to print. Asserting it over
            // anything wider than ApplyCollision's own braces reports the defect as "back" while
            // the real guard below it is correct.
            if (method.Contains("nearestOccluderDist < float.MaxValue"))
                failures.Add("the DEF-151 hard pull-in is back: ANY occluder at ANY distance pulls in");
            if (!method.Contains("nearestOccluderDist < _occluderPullInDistance"))
                failures.Add("pull-in is not gated on the point-blank backstop distance");
            if (!method.Contains("_minCollisionDistance"))
                failures.Add("the seat floor is not bounded by the authored _minCollisionDistance");

            int smoothAt = source.IndexOf("Vector3 smoothed = Vector3.SmoothDamp", StringComparison.Ordinal);
            int collisionAt = source.IndexOf("transform.position = ApplyCollision(smoothed, dt)", StringComparison.Ordinal);
            if (smoothAt < 0 || collisionAt < smoothAt)
                failures.Add("collision is not applied to the final smoothed camera position");
            if (!source.Contains("if (_cam.nearClipPlane > 0.08f) _cam.nearClipPlane = 0.08f;"))
                failures.Add("tight-seat near-plane cap is missing");

            // WO-1734 §12: the fade-vs-pull-in trace is PERMANENT instrumentation — it is the only
            // thing that makes "did the camera hold its seat or collapse into the wall" readable
            // from a capture without a theory. Whole-FILE scope on purpose: these live in
            // TraceOcclusionOutcome, deliberately OUTSIDE the ApplyCollision span above.
            if (!source.Contains("OCCLUDER PULL-IN ENTERED") || !source.Contains("OCCLUDER PULL-IN RELEASED"))
                failures.Add("the pull-in transition trace was stripped (CLAUDE.md sec.12 - instrumentation is permanent)");
            if (!source.Contains("OCCLUDER FADED"))
                failures.Add("the occluder-fade trace was stripped (CLAUDE.md sec.12)");

            reason = failures.Count == 0
                ? "CAMERA_WALL_OCCLUSION_OK occluders fade and the seat holds; pull-in is point-blank only"
                : "CAMERA_WALL_OCCLUSION_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }

        /// <summary>
        /// Return the text of exactly ONE method — its signature plus its brace-matched body — and
        /// nothing that follows it.
        /// </summary>
        /// <remarks>
        /// ⛔ WHY THIS IS BRACE-MATCHED AND NOT ANCHORED ON THE NEXT METHOD SIGNATURE. It used to
        /// read `IndexOf("private Vector3 ApplyCollision")` .. `IndexOf("private void FadeOccluder")`
        /// and `Contains(...)` over that whole span. **A fence built from "the next method
        /// signature" is only correct until somebody adds a method between the two anchors** — and
        /// that is not hypothetical: WO-1734 added `TraceOcclusionOutcome` between them in the same
        /// session, whose trace-formatting line legitimately reads
        /// `nearestOccluderDist &lt; float.MaxValue` as a NULL SENTINEL (is there an occluder to
        /// name?), not as a pull-in guard. The lint read a second method's body as if it were
        /// `ApplyCollision`'s and reported the DEF-151 pull-in as "back" while the real guard
        /// beneath it correctly read `&lt; _occluderPullInDistance`. A RED gate on a correct fix.
        ///
        /// ⚠ That was the SECOND misfire of this one lint in a single day — it had already pinned
        /// the DEFECT in place (see the type summary), and then it misfired on the fix. The lesson
        /// for whoever edits it next: **the span must be derived from the code's own structure, not
        /// from a neighbouring symbol's name.** Braces are structure; the name of the method that
        /// happens to sit below is not.
        ///
        /// The scanner skips `//` and `/* */` comments and string/char literals (including verbatim
        /// and interpolated ones) so a brace inside a comment or a string cannot close the body
        /// early. Returns <see cref="string.Empty"/> when the signature is absent or the body is
        /// unbalanced — the caller FAILS on empty rather than silently passing every Contains().
        /// </remarks>
        // Declared as a balanced PAIR of consts rather than written inline as char literals: the
        // CLAUDE.md sec.1 raw brace one-liner counts every brace in the file including those inside
        // literals, so a lone open-brace char comparison makes a correct file read as mismatched
        // to the human running that check. Two consts, one of each, keeps BOTH counters honest.
        private const char OpenBrace  = '{';
        private const char CloseBrace = '}';

        private static string ExtractMethodBody(string source, string signature)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            int at = source.IndexOf(signature, StringComparison.Ordinal);
            if (at < 0) return string.Empty;

            int open = source.IndexOf(OpenBrace, at);
            if (open < 0) return string.Empty;

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                char c = source[i];

                // Line comment — skip to the newline.
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    int nl = source.IndexOf('\n', i);
                    if (nl < 0) return string.Empty;
                    i = nl;
                    continue;
                }

                // Block comment — skip to the terminator.
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) return string.Empty;
                    i = end + 1;
                    continue;
                }

                // Verbatim string: @"..." where "" is an escaped quote and \ is NOT an escape.
                if (c == '@' && i + 1 < source.Length && source[i + 1] == '"')
                {
                    i += 2;
                    while (i < source.Length)
                    {
                        if (source[i] == '"')
                        {
                            if (i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                            break;
                        }
                        i++;
                    }
                    if (i >= source.Length) return string.Empty;
                    continue;
                }

                // Regular string or char literal. Interpolated holes are deliberately treated as
                // opaque string content: a brace inside a $"..." hole is not a block brace, and
                // this method only needs the BODY's block structure, so skipping the whole literal
                // is both simpler and safer than modelling interpolation.
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < source.Length && source[i] != quote)
                    {
                        if (source[i] == '\\') i++;   // escape consumes the next char
                        i++;
                    }
                    if (i >= source.Length) return string.Empty;
                    continue;
                }

                if (c == OpenBrace) depth++;
                else if (c == CloseBrace)
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(at, i - at + 1);
                }
            }

            return string.Empty;   // unbalanced — caller treats this as a failure, never a pass
        }
    }
}
