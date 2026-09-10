// =====================================================================
//  LayoutOracle — WO-1060. THE clamp/overlap oracle. One implementation.
// ---------------------------------------------------------------------
//  WHY THIS LIVES IN CORE AND NOT IN THE CAPTURE HARNESS.
//
//  The three asserts below were born inside UICaptureLaunch.AuditGeometry,
//  which only ever runs on the headless SCREENSHOT path. That made them
//  unprovable: a suite that wanted to demonstrate the oracle firing could not
//  reach them (DeNelle.Editor references DeNelle.EditorRegression, so the
//  reverse reference is a cycle), and the only alternative — writing a second
//  copy inside the regression assembly — is the two-oracles-disagreeing trap
//  the 2026-08-22 seat correctly refused.
//
//  So the RULES have exactly one home, here, and both callers share it:
//    * UICaptureLaunch.AuditGeometry  — measures every captured panel at three
//      aspects on the settled, post-scaler layout.
//    * UiTouchClampRegression         — measures SYNTHETIC canvases whose
//      defects are authored on purpose, so the oracle can be SEEN going red
//      before anyone trusts it green (PROD-008's rule: an oracle never seen
//      red is not evidence).
//
//  ⛔ MEASUREMENT UNITS. Every rect is converted into ROOT-CANVAS LOCAL space,
//  which is the kit's reference-px space — the same units MinTouchPx and the
//  zone fractions are authored in. `rect.height` in raw screen px until the
//  CanvasScaler has applied was F8-5's root cause; do not re-introduce a raw
//  read. Callers are responsible for handing this a canvas whose layout has
//  already settled at a known aspect.
//
//  ⛔ THE MESSAGE IS THE DELIVERABLE. The owner is red/green colourblind, so a
//  failure may never be a colour, a highlight or a picture. Every line below
//  names BOTH colliding widgets by hierarchy path, both rects, and the exact
//  overlap in reference px — "these overlap" is useless, "'claimButton'
//  overlaps 'rewardRow' by 34px on Y" is actionable. Keep it that way.
// =====================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Core.UI
{
    /// <summary>The numeric layout gate: Assert A (sub-touch-floor authoring), Assert B
    /// (no two interactive rects may intersect) and Assert C (the glyphs inside a rect
    /// must survive). Asserts A and B are pure measurement — they never move, resize or
    /// disable anything they inspect.
    /// <para>⚠ ASSERT C IS THE ONE EXCEPTION AND IT IS DECLARED HERE, NOT DISCOVERED LATER
    /// (WO-1630). It calls <c>TMP_Text.ForceMeshUpdate()</c> on every label it examines,
    /// which REGENERATES that label's mesh. There is no other way to read glyph survival:
    /// the count lives on the generated mesh and <c>textInfo</c> is stale — or null — until
    /// a layout pass has run. It is the same call WO-1628's own probe and
    /// <c>NightMarketRuntimeLayoutRegression.CheckNotTruncated</c> make, and it regenerates
    /// what was already there rather than changing any authored value: no rect moves, no
    /// component is disabled, no string is rewritten. Leaving the "pure measurement"
    /// sentence standing unqualified would have made this header lie, which is the failure
    /// mode CLAUDE.md §15 names.</para></summary>
    public static class LayoutOracle
    {
        /// <summary>Containment slack, reference px. Sub-pixel seams are not defects.</summary>
        public const float ContainSlackPx = 1.5f;
        /// <summary>Overlap must exceed this on BOTH axes to count, reference px.</summary>
        public const float OverlapPadPx = 2f;
        /// <summary>A Button covering this fraction of the canvas is a scrim, not a control.</summary>
        public const float ScrimAreaFraction = 0.80f;

        public enum FindingKind
        {
            /// <summary>Assert B — two interactive rects intersect.</summary>
            ButtonsOverlap,
            /// <summary>Assert B (occlusion half) — a visible button covers foreign text.</summary>
            ButtonOverText,
            /// <summary>Assert A — an authored band resolves under <see cref="ElarionUiKit.MinTouchPx"/>.</summary>
            SubTouchFloorBand,
            /// <summary>Assert C (WO-1630) — a label draws fewer glyphs than the printable
            /// characters it was given: the rect is where it belongs and the words inside it
            /// are not.</summary>
            TextTruncated,
            /// <summary>Assert C, STAND-DOWN HALF — a label produced no textInfo after
            /// ForceMeshUpdate (no font resolved), so its glyph survival was NOT proved this
            /// run.
            /// <para>⛔ ITS OWN KIND, NOT A PREFIX ON <see cref="TextTruncated"/>, AND THAT IS
            /// THE WHOLE POINT. A caller asking "did Assert C go red?" matches on the kind; if
            /// an unproved label shared the truncation kind, a run in which NO font resolved
            /// would answer YES and a red-first proof could be satisfied by a total measurement
            /// failure. A run where nothing could be measured must never be able to read as a
            /// run where everything fit.</para></summary>
            TextUnmeasured,
        }

        /// <summary>One defect, already worded for the log.</summary>
        public readonly struct Finding
        {
            public readonly FindingKind Kind;
            /// <summary>True when both controls share a parent (the pre-existing UI_GEOMETRY gate's
            /// narrower case). Cross-parent pairs are the WO-1060 widening and are routed by the
            /// caller to the touch marker only, so the older gate's behaviour is unchanged.</summary>
            public readonly bool SameParent;
            public readonly string Message;

            public Finding(FindingKind kind, bool sameParent, string message)
            {
                Kind = kind; SameParent = sameParent; Message = message;
            }

            public override string ToString() { return Message; }
        }

        /// <summary>Measure a settled canvas. <paramref name="label"/>/<paramref name="w"/>/<paramref name="h"/>
        /// only decorate the messages — the numbers come from the resolved rects.</summary>
        public static List<Finding> Audit(GameObject canvasGo, string label, int w, int h)
        {
            return Audit(canvasGo, label, w, h, out _);
        }

        /// <summary>As above, plus the number of labels Assert C actually MEASURED — the ones that
        /// survived every exclusion and produced a real textInfo.
        /// <para>⛔ THE COUNT EXISTS BECAUSE THE FINDINGS ALONE CANNOT DISTINGUISH "EVERY LABEL FIT"
        /// FROM "NO RULE LOOKED AT A SINGLE LABEL" (WO-1630). Both return zero findings. A caller
        /// that reports the first as clean without reading this number is the exact blindness the
        /// whole ticket exists to end — a typo, an inverted comparison or a predicate that silently
        /// excludes every control reports a clean run and looks identical to a healthy one.</para></summary>
        public static List<Finding> Audit(GameObject canvasGo, string label, int w, int h,
                                          out int glyphLabelsMeasured)
        {
            glyphLabelsMeasured = 0;
            var found = new List<Finding>();
            RectTransform root = canvasGo != null ? canvasGo.GetComponent<RectTransform>() : null;
            if (root == null) return found;

            string at = " [" + label + " @" + w + "x" + h + "]";

            TMP_Text[] texts = canvasGo.GetComponentsInChildren<TMP_Text>(false);
            Button[] buttons = canvasGo.GetComponentsInChildren<Button>(false);

            if (!TryRectInRoot(root, root, out Rect canvasRect)) canvasRect = new Rect(0f, 0f, w, h);
            float canvasArea = Mathf.Max(1f, canvasRect.width * canvasRect.height);

            // ---- ASSERT B: NO TWO INTERACTIVE RECTS MAY INTERSECT -----------------
            //
            //  ⚠ THIS RULE USED TO REQUIRE `a.parent == b.parent`, AND THAT NARROWING
            //  IS WHY IT MISSED THE DEFECT IT WAS WRITTEN FOR. The overlaps that
            //  actually shipped were between DIFFERENT parents — WO-1058's
            //  Cancel(0.885-0.98) sitting inside where Upgrade(0.76-0.98) was, and the
            //  2026-08-22 Night Market frames where a card in one shelf ROW was drawn
            //  over the card in the row above it, occluding a price's leading digit
            //  (120 SKR read as "20 SKR"). Two rows are two parents, so the sibling
            //  test walked straight past a wrong price on the money screen.
            //
            //  The ONLY pairs excluded are ANCESTOR/DESCENDANT ones: a button nested
            //  inside another button's subtree is a composition (a tappable card with a
            //  tappable child), a different defect class this rule does not adjudicate.
            for (int i = 0; i < buttons.Length; i++)
            {
                var a = buttons[i];
                if (!ButtonUsable(a, root, canvasGo.transform, canvasArea, out Rect ar)) continue;
                for (int j = i + 1; j < buttons.Length; j++)
                {
                    var b = buttons[j];
                    if (b == null) continue;
                    if (IsDescendantOf(a.transform, b.transform)) continue;   // composition, not collision
                    if (IsDescendantOf(b.transform, a.transform)) continue;
                    if (!ButtonUsable(b, root, canvasGo.transform, canvasArea, out Rect br)) continue;
                    if (!Overlaps(ar, br, OverlapPadPx, out float ow, out float oh)) continue;

                    string line = "BUTTONS OVERLAP" + at + " '" +
                                  PathOf(a.transform, canvasGo.transform) + "' " + RectStr(ar) + " and '" +
                                  PathOf(b.transform, canvasGo.transform) + "' " + RectStr(br) +
                                  " share " + ow.ToString("0.#") + "x" + oh.ToString("0.#") +
                                  " ref px -- two tap targets in one place; only one can win the raycast.";
                    found.Add(new Finding(FindingKind.ButtonsOverlap,
                                          a.transform.parent == b.transform.parent, line));
                }
            }

            // ---- ASSERT B (occlusion half): a visible button must not sit on foreign text ----
            foreach (var b in buttons)
            {
                if (!ButtonUsable(b, root, canvasGo.transform, canvasArea, out Rect br)) continue;
                if (!HasVisibleGraphic(b)) continue;     // hit areas / scrims cannot collide visually
                foreach (var t in texts)
                {
                    if (t == null || !t.enabled || !t.gameObject.activeInHierarchy) continue;
                    if (string.IsNullOrEmpty(t.text) || t.color.a < 0.05f) continue;
                    if (IsDescendantOf(t.transform, b.transform)) continue;   // its own label
                    if (IsDescendantOf(b.transform, t.transform)) continue;
                    if (ClippedOut(t.transform, canvasGo.transform, root)) continue;
                    var trt = t.transform as RectTransform;
                    if (!TryRectInRoot(trt, root, out Rect tr)) continue;
                    if (!Overlaps(br, tr, OverlapPadPx, out float ow, out float oh)) continue;

                    found.Add(new Finding(FindingKind.ButtonOverText, true,
                        "BUTTON OVER TEXT" + at + " '" + PathOf(b.transform, canvasGo.transform) +
                        "' " + RectStr(br) + " covers '" + PathOf(t.transform, canvasGo.transform) +
                        "' (\"" + Snippet(t.text) + "\") " + RectStr(tr) + " by " +
                        ow.ToString("0.#") + "x" + oh.ToString("0.#") + " ref px."));
                }
            }

            // ---- ASSERT A: authored band under the kit touch floor ----------------
            //
            //  The guard grows a sub-floor button in LateUpdate, which never runs in an
            //  edit-mode capture — so what is measured here IS the pre-grow AUTHORED
            //  size. That is the point: the sub-floor band is the DEFECT SIGNATURE; the
            //  symmetric growth is only its consequence, and by the time you can see the
            //  growth the neighbour is already overlapped.
            foreach (var b in buttons)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                if (!HasMinTouchGuard(b)) continue;      // not a kit button; not this rule's contract
                var brt = b.transform as RectTransform;
                if (!TryRectInRoot(brt, root, out Rect br)) continue;
                float shortest = Mathf.Min(br.width, br.height);
                if (shortest >= ElarionUiKit.MinTouchPx - 0.5f) continue;

                // WO-1623 — THE HOST'S MEASURED SIZE TRAVELS WITH THE FINDING.
                // Without it the reader of a capture log can only DERIVE the parent height
                // (resolved px / authored fraction) and then author the fix off an inference,
                // which is exactly the guess CLAUDE.md §11B forbids. WO-1623's own §1d had to
                // publish a table labelled "derived, not measured" for this reason, and its
                // §4 Step 1 made printing the real number the lane's first task. The parent is
                // the rect a fractional band is a fraction OF, so it is the one extra number
                // that turns "too short" into "and here is what it must be".
                string host = string.Empty;
                var parentRt = brt != null ? brt.parent as RectTransform : null;
                if (parentRt != null && TryRectInRoot(parentRt, root, out Rect pr))
                {
                    float needFrac = pr.height > 0.01f ? ElarionUiKit.MinTouchPx / pr.height : 0f;
                    host = " Its host '" + PathOf(parentRt, canvasGo.transform) + "' measures " +
                           pr.width.ToString("0.#") + "x" + pr.height.ToString("0.#") +
                           " ref px, so the floor is " + needFrac.ToString("0.###") +
                           " of the host's HEIGHT -- author it in px, not as that fraction.";
                }
                found.Add(new Finding(FindingKind.SubTouchFloorBand, true,
                    "SUB-TOUCH-FLOOR BAND" + at + " '" + PathOf(b.transform, canvasGo.transform) +
                    "' resolves " + br.width.ToString("0.#") + "x" + br.height.ToString("0.#") +
                    " ref px -- shortest side " + shortest.ToString("0.#") + " is " +
                    (ElarionUiKit.MinTouchPx - shortest).ToString("0.#") + " px UNDER " +
                    "ElarionUiKit.MinTouchPx (" + ElarionUiKit.MinTouchPx.ToString("0.#") +
                    "). ClampMinTouch will grow it SYMMETRICALLY about its centre at runtime and " +
                    "spill it into both neighbours. Author the band AT the floor." + host));
            }

            // ---- ASSERT C: THE GLYPHS INSIDE THE RECT MUST SURVIVE (WO-1630) ------
            //
            //  ⛔ EVERY OTHER RULE ON THIS PATH MEASURES *WHERE* A RECT IS. A label whose
            //  rect sits neatly inside its plate while TMP has cut the contents away is
            //  invisible to all of them — which is why seven truncated Build Collections
            //  captions rendered "nothing affordable y" at two of three aspects on
            //  2026-09-10 while the geometry run on the same log passed 91 canvases,
            //  correctly, because no rule looked. The only detector left for that whole
            //  class was a human opening a PNG.
            //
            //  THE TEST IS VISIBLE GLYPHS vs PRINTABLE SOURCE CHARACTERS, read off the
            //  GENERATED MESH. The shape is lifted from
            //  NightMarketRuntimeLayoutRegression.CheckNotTruncated rather than invented
            //  a third time. The lighter HubRepairAffordance.WarnIfClipped records in its
            //  own doc block why the raw `characterCount < text.Length` form is the WEAKER
            //  signal: characterCount is TMP's PARSED count, so a newline or a markup tag
            //  in the source convicts a perfectly healthy string. Soft wrap alone does not
            //  trip the glyph form either — a wrapped label that kept every character
            //  counts every character, which is why WO-1628's 1920x1080 probe read
            //  "2 lines, 22 chars" against a 22-character source and was NOT a defect.
            //
            //  ⚠ WHY NOT `isTextTruncated` AS THE TEST. Its semantics are overflow-mode
            //  dependent and were NOT verified against TMP source when this rule was
            //  written, so it is printed as a CORROBORATING field and never asserted on.
            //  The glyph count is mode-agnostic: it catches a Truncate cut and an Ellipsis
            //  substitution the same way.
            foreach (var t in texts)
            {
                // Exclusions, each one justified where it sits. They mirror the
                // ButtonOverText predicate above so the two rules cannot disagree about
                // which labels exist.
                if (t == null || !t.enabled || !t.gameObject.activeInHierarchy) continue;
                if (t.color.a < 0.05f) continue;                 // invisible: it has no glyphs to lose
                // Masked/scrolled-out content is clipped BY CONSTRUCTION. Its glyph count
                // is a property of the viewport, not of the authoring, and cannot be judged.
                if (ClippedOut(t.transform, canvasGo.transform, root)) continue;
                // Overflow mode CANNOT truncate — by definition it spills instead. Sweeping
                // those is pure noise, and noise is what pushes real findings past the
                // caller's print cap. (Measured across Assets/ 2026-09-10: only Ellipsis,
                // Truncate and Overflow are ever assigned in this tree; Linked and Page,
                // whose glyphs legitimately live on ANOTHER label, do not occur, so they are
                // deliberately not special-cased on a hypothetical.)
                if (t.overflowMode == TextOverflowModes.Overflow) continue;

                string src = t.text ?? string.Empty;
                int printable = PrintableCount(src, t.richText);
                if (printable == 0) continue;                    // nothing was asked for; a different rule's business

                // A rect that cannot be resolved into root-canvas space cannot be JUDGED in
                // root-canvas space -- exactly the skip ButtonOverText makes above. Substituting
                // a zero rect and carrying on would print "(x 0..0, y 0..0)" findings against
                // degenerate or unbuilt labels nobody ever sees, which is first-run noise on
                // precisely the objects that are not the defect.
                var trt = t.transform as RectTransform;
                if (!TryRectInRoot(trt, root, out Rect tr)) continue;

                t.ForceMeshUpdate();
                var info = t.textInfo;
                if (info == null || info.characterInfo == null)
                {
                    // ⛔ NOT A SILENT CONTINUE. A branch that returns having asserted nothing
                    // is the most expensive defect class in this repo, and worse inside an
                    // oracle that polices others for it. This is the same stand-down
                    // CheckNotTruncated declares as a PartialSkip that NAMES the label.
                    found.Add(new Finding(FindingKind.TextUnmeasured, true,
                        "TEXT UNMEASURED" + at + " '" + PathOf(t.transform, canvasGo.transform) +
                        "' (\"" + Snippet(src) + "\") produced NO textInfo after ForceMeshUpdate -- " +
                        "no font resolved, so this label's glyph survival is NOT PROVED this run. " +
                        "It is reported, never skipped: a run where nothing could be measured must " +
                        "never read as a run where everything fit."));
                    continue;
                }

                // MEASURED: it passed every exclusion and TMP produced a real mesh for it. Counted
                // here rather than at the top of the loop, so an excluded or unmeasurable label can
                // never inflate the number a caller uses to decide whether this run proved anything.
                glyphLabelsMeasured++;

                int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
                // A deliberate partial reveal is not a defect: typewriter/reveal effects author
                // exactly this state on purpose, and convicting them would train the reader to
                // ignore the rule. Both windows are checked because either one alone hides
                // glyphs the mesh never drew.
                if (t.maxVisibleCharacters < count || t.firstVisibleCharacter > 0) continue;

                int visible = 0;
                for (int i = 0; i < count; i++)
                    if (info.characterInfo[i].isVisible) visible++;
                if (visible >= printable) continue;

                string facts = " " + RectStr(tr) + " at font " + t.fontSize.ToString("0.#") +
                               " [autosize " + t.fontSizeMin.ToString("0.#") + ".." +
                               t.fontSizeMax.ToString("0.#") + ", enabled=" + t.enableAutoSizing +
                               "] overflow=" + t.overflowMode + " wrap=" + t.textWrappingMode +
                               " isTextTruncated=" + t.isTextTruncated +
                               " (corroborating only -- the glyph count is the assertion).";

                if (visible == 0)
                {
                    found.Add(new Finding(FindingKind.TextTruncated, true,
                        "TEXT CULLED WHOLE" + at + " '" + PathOf(t.transform, canvasGo.transform) +
                        "' (\"" + Snippet(src) + "\") draws ZERO of " + printable +
                        " printable glyphs." + facts +
                        " The band has no room for one character at the resolved size."));
                    continue;
                }

                found.Add(new Finding(FindingKind.TextTruncated, true,
                    "TEXT TRUNCATED" + at + " '" + PathOf(t.transform, canvasGo.transform) +
                    "' (\"" + Snippet(src) + "\") draws " + visible + " of " + printable +
                    " printable glyphs." + facts +
                    " The rect is where it belongs and the words inside it are not -- give the " +
                    "band room or shorten the copy; lowering the font floor trades one unreadable " +
                    "caption for another."));
            }

            return found;
        }

        /// <summary>Non-whitespace characters of the source string — the denominator Assert C
        /// measures visible glyphs against.
        /// <para>⚠ MARKUP IS EXCLUDED WHEN THE LABEL PARSES IT, AND THAT GUARD IS MEASURED, NOT
        /// DEFENSIVE. TMP strips tag characters out during parsing so they never reach
        /// characterInfo, while <c>char.IsWhiteSpace</c> happily counts every one of them as
        /// printable — so a raw non-whitespace count reads
        /// <c>&lt;color=#FF6B6B&gt;No Wallet Bound&lt;/color&gt;</c>
        /// (Assets/_Modules/Core/Debug/DebugCanvasUI.cs:156) as 36 printable against 13 drawn
        /// and convicts a label that lost nothing.</para>
        /// <para>KNOWN LIMIT, stated rather than hidden: a <c>&lt;sprite&gt;</c> tag draws one
        /// visible glyph and contributes zero printable, so a sprite-bearing label reads
        /// visible &gt; printable and is passed. That direction is a MISS, never a false
        /// conviction, which is the safe direction for a rule that must survive its first
        /// live run without being suppressed.</para>
        /// <para>An unmatched '&lt;' is counted as an ordinary character rather than swallowing
        /// the remainder of the string — copy containing a lone angle bracket is text, not
        /// markup.</para></summary>
        public static int PrintableCount(string s, bool richText)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (richText && c == '<')
                {
                    int close = s.IndexOf('>', i + 1);
                    if (close > i) { i = close; continue; }      // a real tag: skipped whole
                }
                if (!char.IsWhiteSpace(c)) n++;
            }
            return n;
        }

        // ---------------------------------------------------------------------
        //  Geometry helpers (all measurements in ROOT-CANVAS local = reference px).
        // ---------------------------------------------------------------------

        /// <summary>Resolve a rect into root-canvas local (reference px) space.</summary>
        public static bool TryRectInRoot(RectTransform rt, RectTransform root, out Rect r)
        {
            r = default(Rect);
            if (rt == null || root == null) return false;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = root.InverseTransformPoint(c[i]);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            if (float.IsNaN(minX) || float.IsNaN(minY) || float.IsInfinity(maxX) || float.IsInfinity(maxY))
                return false;
            r = new Rect(minX, minY, maxX - minX, maxY - minY);
            return r.width > 0.5f && r.height > 0.5f;
        }

        /// <summary>How far <paramref name="inner"/> pokes outside <paramref name="outer"/> (px; &lt;=0 = contained).</summary>
        public static float OutsideBy(Rect inner, Rect outer)
        {
            float left = outer.xMin - inner.xMin;
            float right = inner.xMax - outer.xMax;
            float bottom = outer.yMin - inner.yMin;
            float top = inner.yMax - outer.yMax;
            return Mathf.Max(Mathf.Max(left, right), Mathf.Max(bottom, top));
        }

        public static bool Overlaps(Rect a, Rect b, float pad, out float ow, out float oh)
        {
            ow = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            oh = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return ow > pad && oh > pad;
        }

        /// <summary>Nearest masking ancestor between <paramref name="t"/> and <paramref name="stopAt"/> (exclusive).</summary>
        public static RectTransform NearestClipper(Transform t, Transform stopAt)
        {
            for (Transform p = t != null ? t.parent : null; p != null; p = p.parent)
            {
                if (p == stopAt) break;
                if (p.GetComponent<RectMask2D>() != null || p.GetComponent<Mask>() != null)
                    return p as RectTransform;
            }
            return null;
        }

        /// <summary>True when the element is clipped and not FULLY inside its clipper (scrolled out).</summary>
        public static bool ClippedOut(Transform t, Transform canvasRoot, RectTransform root)
        {
            RectTransform clip = NearestClipper(t, canvasRoot);
            if (clip == null) return false;
            var rt = t as RectTransform;
            if (!TryRectInRoot(rt, root, out Rect er)) return true;
            if (!TryRectInRoot(clip, root, out Rect cr)) return true;
            return OutsideBy(er, cr) > ContainSlackPx;
        }

        public static bool ButtonUsable(Button b, RectTransform root, Transform canvasRoot,
                                        float canvasArea, out Rect r)
        {
            r = default(Rect);
            if (b == null || !b.gameObject.activeInHierarchy) return false;
            var brt = b.transform as RectTransform;
            if (!TryRectInRoot(brt, root, out r)) return false;
            if (r.width * r.height >= canvasArea * ScrimAreaFraction) return false;   // scrim
            if (ClippedOut(b.transform, canvasRoot, root)) return false;
            return true;
        }

        public static bool HasVisibleGraphic(Button b)
        {
            var g = b != null ? b.targetGraphic : null;
            return g != null && g.enabled && g.color.a >= 0.05f;
        }

        /// <summary>The ClampMinTouch guard is a PRIVATE nested type in the kit -- match by name.</summary>
        public static bool HasMinTouchGuard(Button b)
        {
            if (b == null) return false;
            foreach (var mb in b.GetComponents<MonoBehaviour>())
                if (mb != null && string.Equals(mb.GetType().Name, "UiKitMinTouchGuard", StringComparison.Ordinal))
                    return true;
            return false;
        }

        public static bool IsDescendantOf(Transform child, Transform ancestor)
        {
            if (child == null || ancestor == null) return false;
            for (Transform p = child; p != null; p = p.parent)
                if (p == ancestor) return true;
            return false;
        }

        public static string PathOf(Transform t, Transform stopAt)
        {
            if (t == null) return "<null>";
            string s = t.name;
            for (Transform p = t.parent; p != null && p != stopAt; p = p.parent)
                s = p.name + "/" + s;
            return s;
        }

        public static string RectStr(Rect r)
        {
            return "(x " + r.xMin.ToString("0.#") + ".." + r.xMax.ToString("0.#") +
                   ", y " + r.yMin.ToString("0.#") + ".." + r.yMax.ToString("0.#") + ")";
        }

        public static string Snippet(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length <= 48 ? s : s.Substring(0, 45) + "...";
        }
    }
}
