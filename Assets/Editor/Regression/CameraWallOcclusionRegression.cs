using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
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

            CheckSeatSidePullInGate(failures);

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

            // ── WO-1753: THE GATE MEASURES FROM THE SEAT, AND IT IS THE FARTHEST HIT ─────────
            //
            // ⚠ THE ASSERTION BELOW WAS INVERTED ON 2026-09-15, AND THE OLD ONE WAS NOT WRONG WHEN
            // WRITTEN — it pinned WO-1734's fix (gate on _occluderPullInDistance, not float.MaxValue)
            // and that half still stands. What changed is WHICH DISTANCE is compared:
            // `nearestOccluderDist` is measured from the PIVOT (the hero's chest), so with a 4.5 m
            // boom it fired the backstop for a wall 3.9 m in FRONT of the seat and collapsed the
            // seat to the 1.2 m floor. Now FORBIDDEN, along with a gap computed from the nearest
            // hit, which is the same defect wearing the new gate's clothes.
            if (method.Contains("nearestOccluderDist < _occluderPullInDistance"))
                failures.Add("the pull-in is gated on the occluder nearest the HERO (a distance from the "
                    + "pivot), so a wall 3.9 m in front of the seat collapses the camera to the floor - "
                    + "WO-1753: gate on the seat-side gap to the FARTHEST hit");
            if (method.Contains("fullDist - nearestOccluderDist"))
                failures.Add("the seat-side gap is computed from the occluder nearest the HERO - that is a "
                    + "min, so one occluder behind the hero hides an occluder at the seat and the camera "
                    + "body embeds in the seat-side wall");
            if (!method.Contains("SelectOccluderGateDistance(_occluderDistances)"))
                failures.Add("ApplyCollision no longer reduces the sweep through SelectOccluderGateDistance, "
                    + "so the FARTHEST-hit rule the behavioural case below pins is not the one that ships");
            if (!method.Contains("ShouldPullIn(fullDist, occluderGateDist, _occluderPullInDistance)"))
                failures.Add("the pull-in decision is no longer taken by ShouldPullIn against the seat-side gap");
            if (!method.Contains("fullDist, occluderGateDist, _collisionSkin, _minCollisionDistance"))
                failures.Add("the pull-in SEAT is set against a different occluder than the gate judged - "
                    + "feeding the hero-nearest hit here re-creates the collapse from the other side");
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

            // ── WO-1751: TOWERS AND WALLS MUST BE ABLE TO OCCLUDE ────────────────────────────
            //
            // THE DEFECT THIS PINS (owner felt-test 2026-09-15, Seeker build 2026.09.15.371127,
            // Screenshot_20260915-134636.png): the camera parked inside a raid watchtower, no fade,
            // and the WO-1734 occluder trace emitted NOTHING — because `ResolveCollisionMask` built
            // Default | Building | Tower and OMITTED "Structure", which is the layer every raid wall
            // panel is moved to (RaidBaseGenerator.cs:1999-2000; a census of
            // Assets/Scenes/RaidBase_IronBastion.unity reads 158 Wall_* on m_Layer: 8, plus 2
            // layer-8 gatehouses from RaidBaseDresser.cs:1040-1041). With no occluder in the mask
            // the spherecast hit nothing, `_faded` stayed empty, and TraceOcclusionOutcome — which
            // speaks only on a pull-in EDGE or a non-empty fade set — was silent. The absence of a
            // trace line was read as "the camera is not running"; it was "the camera can see no
            // geometry at all".
            //
            // ⛔ THIS IS A BEHAVIOURAL PIN, NOT A SOURCE-TEXT ONE, AND THAT IS DELIBERATE. It CALLS
            // SmartMobileCamera.ComputeDefaultCollisionMask() and resolves each layer index through
            // LayerMask.NameToLayer, so it reads ProjectSettings/TagManager.asset at run time. A
            // `source.Contains("Structure")` lint would be a SECOND hand-maintained copy of the
            // layer list — the duplicated state CLAUDE.md sec.5 forbids, and the exact failure mode
            // that made this very suite pin the defect it was written to prevent.
            int occlusionMask = SmartMobileCamera.ComputeDefaultCollisionMask();
            RequireLayerInMask(failures, occlusionMask, "Default",
                "ground and most structures live here");
            RequireLayerInMask(failures, occlusionMask, "Building",
                "town buildings must fade rather than swallow the camera");
            RequireLayerInMask(failures, occlusionMask, "Tower",
                "towers must fade rather than swallow the camera");
            RequireLayerInMask(failures, occlusionMask, "Structure",
                "EVERY raid wall panel and gatehouse is on this layer (RaidBaseGenerator.cs:1999-2000) "
                + "- without it a raid camera has no occluders at all");
            RequireLayerNotInMask(failures, occlusionMask, "Enemy",
                "a mob must never push or fade against the camera");
            RequireLayerNotInMask(failures, occlusionMask, "UI",
                "UI colliders must never enter the world occlusion cast");

            // The seam that lets a WATCHTOWER be an occluder at all. The raid tower art carries NO
            // authored collider — `addColliders: 0` on both
            // Assets/StructureContent/ArcaneSpire_1.fbx.meta:43 and the KayKit
            // building_watchtower_green.fbx.meta:43, and a census of RaidBase_IronBastion.unity
            // finds every Collider in the scene belongs to Wall_*/RuinStep/KeepPlatform/KeepRamp,
            // never to a Watchtower_*. Its ONLY physics presence is the capsule
            // DefenseTower.Awake adds through EnsureContactCollider. Delete that call and the mask
            // fix above buys the towers nothing, silently — so pin the call, not a comment.
            string towerPath = Path.Combine("Assets", "_Modules", "Village", "Buildings", "DefenseTower.cs");
            string towerSource = File.Exists(towerPath) ? File.ReadAllText(towerPath) : string.Empty;
            if (towerSource.Length == 0)
                failures.Add("DefenseTower.cs could not be read - the watchtower-occluder seam was not checked");
            else
            {
                string awake = ExtractMethodBody(towerSource, "private void Awake");
                if (awake.Length == 0)
                    failures.Add("DefenseTower.Awake could not be located - the watchtower-occluder seam was not checked");
                else if (!awake.Contains("EnsureContactCollider()"))
                    failures.Add("DefenseTower.Awake no longer ensures a contact collider - a raid watchtower "
                        + "then has NO collider at all (its FBX ships addColliders: 0) and can never occlude");
            }

            // ── WO-1751: THE FADE MUST ACTUALLY REACH THE ART THE PLAYER SEES ────────────────
            //
            // Adding "Structure" to the mask above puts 158 Wall_* colliders into the occlusion
            // cast. If the fade cannot reach their visible art, that change ships PULL-IN WITHOUT
            // FADE across an entire raid - which is the owner's 2026-09-14 report ("the camera and
            // targetting still pulls towards walls and since its tighter pathways makes camera spin
            // and targetting very challenging") amplified 158-fold. So the two ship together, and
            // this case is what stops them being separated later.
            //
            // The shape below is READ OUT OF THE BAKED SCENE, not out of a builder's intent:
            // Assets/Scenes/RaidBase_IronBastion.unity, GameObject Wall_Outer_SN_0 (m_Layer: 8,
            // one BoxCollider, one NavMeshObstacle) has THREE direct children - `steel_wall`,
            // `Clad_Wall_Outer_SN_0` and `Ruin_Wall_Outer_SN_0` -> `RuinPiece_0`. The old
            // FadeOccluder used SINGULAR `GetComponentInChildren<Renderer>()` and hid only the
            // first, so the wall kept drawing and the fade was a no-op.
            CheckOccluderResolution(failures);

            // §12: the instrument that makes "the cast found nothing" distinguishable from "the
            // camera never ran". Permanent, per CLAUDE.md sec.12 - never strip it.
            if (!source.Contains("CAMERA SEAT EMBEDDED IN UNSEEN GEOMETRY"))
                failures.Add("the seat-embedded detector trace was stripped (CLAUDE.md sec.12 - instrumentation is permanent)");
            if (!method.Contains("TraceSeatEmbeddedButUnseen(seat)"))
                failures.Add("ApplyCollision no longer runs the seat-embedded detector, so a camera inside "
                    + "geometry the cast cannot see is silent again");
            if (!source.Contains("OCCLUSION MASK RESOLVED"))
                failures.Add("the resolved-occlusion-mask trace was stripped (CLAUDE.md sec.12)");

            // The RESTORE half of the fade contract, pinned where it lives. `_faded[rend]` must be
            // seeded with the renderer's ORIGINAL shadow mode BEFORE the mode is overwritten, or a
            // restore puts back ShadowsOnly and the wall is invisible forever. This is a source-text
            // pin because `_faded` is private per-instance state and standing up a live
            // SmartMobileCamera inside an editor suite would run Awake/OnEnable for real.
            string fadeOne = ExtractMethodBody(source, "private bool FadeOneRenderer");
            if (fadeOne.Length == 0)
                failures.Add("FadeOneRenderer could not be located - the restore contract was not checked");
            else
            {
                if (!fadeOne.Contains("_faded[rend] = rend.shadowCastingMode"))
                    failures.Add("FadeOneRenderer no longer records the renderer's ORIGINAL shadow mode, "
                        + "so a restore cannot put it back and a faded wall stays invisible");
                if (!fadeOne.Contains("_fadedThisFrame.Add(rend)"))
                    failures.Add("FadeOneRenderer no longer marks the renderer for this frame, so "
                        + "RestoreFadedNotHitThisFrame would un-hide a wall that is still occluding");
            }

            reason = failures.Count == 0
                ? "CAMERA_WALL_OCCLUSION_OK occluders fade and the seat holds; pull-in is point-blank only"
                : "CAMERA_WALL_OCCLUSION_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }

        /// <summary>
        /// WO-1753 — BEHAVIOURAL pin on the point-blank backstop's gate: it measures the gap from
        /// the camera SEAT, and it reduces the sweep with a MAX, never a min.
        /// </summary>
        /// <remarks>
        /// ⛔ WHY THIS IS NOT A SOURCE-TEXT LINT. The defect and the fix differ by one word —
        /// farthest vs nearest — and a <c>Contains()</c> cannot tell them apart; an implementation
        /// that folded the sweep with <c>Mathf.Min</c> under the new identifier would sail past
        /// every string assertion above. So the cases below CALL
        /// <see cref="SmartMobileCamera.SelectOccluderGateDistance"/> and
        /// <see cref="SmartMobileCamera.ShouldPullIn"/> with the two-occluder arrangement WO-1753
        /// names, and a nearest-based implementation FAILS them by arithmetic.
        ///
        /// The numbers are the shipped ones, stated rather than assumed: <c>_followOffset.z</c>
        /// -4.5 and <c>_lookAtHeight</c> 2.5 give a boom of ~4.5 m, and
        /// <c>_occluderPullInDistance</c> is 0.6 m.
        /// </remarks>
        private static void CheckSeatSidePullInGate(List<string> failures)
        {
            const float boom = 4.5f;      // the shipped seat distance from the pivot
            const float backstop = 0.6f;  // _occluderPullInDistance

            // -- Case 1: THE DISCRIMINATING ONE. Two occluders — one 0.5 m from the hero's chest,
            // one essentially AT the seat. A min returns 0.5 (gap 4.0 -> no pull-in) and the camera
            // body ends up inside the seat-side wall. The max returns 4.45 (gap 0.05 -> pull in).
            var twoOccluders = new List<float> { 0.5f, 4.45f };
            float gate = SmartMobileCamera.SelectOccluderGateDistance(twoOccluders);
            if (Math.Abs(gate - 4.45f) > 0.001f)
                failures.Add("the occluder gate distance is " + gate.ToString("0.###") + " for hits {0.5, 4.45}"
                    + " - it must be the FARTHEST hit (4.45, the occluder nearest the SEAT). A min hides a "
                    + "wall at the seat behind a wall behind the hero and the camera embeds in it");
            if (!SmartMobileCamera.ShouldPullIn(boom, gate, backstop))
                failures.Add("an occluder AT the camera seat did not fire the point-blank backstop while a "
                    + "second occluder sat behind the hero - the camera body would embed in the seat-side wall");

            // -- Case 2: the ticket's regression case. ONE wall 0.5 m in front of the HERO's chest,
            // i.e. 4.0 m in front of the seat. `0.5 < 0.6` made the OLD hero-side gate fire here and
            // collapse the seat to the 1.2 m floor (a 3.75x zoom); the seat-side gap is 4.0 m, so
            // the fixed gate must not fire.
            float farWall = SmartMobileCamera.SelectOccluderGateDistance(new List<float> { 0.5f });
            if (SmartMobileCamera.ShouldPullIn(boom, farWall, backstop))
                failures.Add("a wall 0.5 m from the hero's chest - 4.0 m in FRONT of the camera seat - still "
                    + "fires the point-blank backstop, collapsing a 4.5 m boom to the floor (WO-1753)");

            // -- Case 3: an occluder genuinely at the seat, alone, still fires. The backstop must
            // not be disarmed by the fix; 0.43 m of clearance is needed (radius 0.35 + near clip
            // 0.08) and the 0.6 m threshold keeps ~0.17 m of margin.
            float atSeat = SmartMobileCamera.SelectOccluderGateDistance(new List<float> { 4.2f });
            if (!SmartMobileCamera.ShouldPullIn(boom, atSeat, backstop))
                failures.Add("an occluder 0.3 m from the camera seat no longer fires the point-blank "
                    + "backstop - the camera body is free to embed in a mesh");

            // -- Case 4: an empty sweep is never a pull-in, and must not read as an occluder at 0.
            float none = SmartMobileCamera.SelectOccluderGateDistance(new List<float>());
            if (none >= 0f)
                failures.Add("an empty sweep did not return the negative no-occluder sentinel (got "
                    + none.ToString("0.###") + "), so a clear line of sight can be mistaken for an "
                    + "occluder at the pivot");
            if (SmartMobileCamera.ShouldPullIn(boom, none, backstop))
                failures.Add("a clear line of sight fired the point-blank backstop");
            if (SmartMobileCamera.ShouldPullIn(boom, SmartMobileCamera.SelectOccluderGateDistance(null), backstop))
                failures.Add("a null hit list fired the point-blank backstop");

            // -- Case 5: the seat is set against the occluder the gate judged. Same arithmetic the
            // shipped call uses, so a regression in AllowedCameraDistance's contract shows up here
            // as the felt symptom (a collapse to the floor) rather than as a bare number.
            float seat = SmartMobileCamera.AllowedCameraDistance(boom, gate, 0.2f, 1.2f);
            if (Math.Abs(seat - 4.25f) > 0.001f)
                failures.Add("the point-blank pull-in seated the camera at " + seat.ToString("0.###")
                    + "m instead of just in front of the seat-side occluder (4.25m) - a pull-in that "
                    + "large is the 3.75x zoom WO-1753 exists to delete");
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

        /// <summary>
        /// WO-1751: assert a NAMED project layer is inside the camera's occlusion mask. Resolves the
        /// index through <see cref="LayerMask.NameToLayer"/> so the assertion reads
        /// ProjectSettings/TagManager.asset rather than carrying a second copy of the layer numbers.
        /// An unknown layer name FAILS loudly - silently skipping it would let a renamed layer
        /// quietly empty the mask, which is the defect this whole block exists to catch.
        /// </summary>
        private static void RequireLayerInMask(List<string> failures, int mask, string layerName, string why)
        {
            int idx = LayerMask.NameToLayer(layerName);
            if (idx < 0)
            {
                failures.Add("project layer '" + layerName + "' does not exist - the camera occlusion mask "
                    + "cannot contain it (" + why + ")");
                return;
            }
            if ((mask & (1 << idx)) == 0)
                failures.Add("camera occlusion mask omits layer " + idx + ":" + layerName + " - " + why);
        }

        /// <summary>
        /// WO-1751 — BEHAVIOURAL pin on <see cref="SmartMobileCamera.CollectOccluderRenderers"/>:
        /// build the baked raid wall's ACTUAL shape and assert the resolution, rather than grep for
        /// a method name. Everything it creates is destroyed in a finally, so a throw mid-case
        /// cannot leak GameObjects into the editor scene.
        /// </summary>
        private static void CheckOccluderResolution(List<string> failures)
        {
            GameObject wall = null, orphanParent = null;
            try
            {
                // -- Case 1: the baked wall. Blocker collider on the root, art as CHILDREN. ------
                wall = new GameObject("Wall_Outer_SN_0");
                int structure = LayerMask.NameToLayer("Structure");
                if (structure >= 0) wall.layer = structure;
                wall.AddComponent<BoxCollider>();

                Renderer steel = AddRendererChild(wall, "steel_wall");
                Renderer clad = AddRendererChild(wall, "Clad_Wall_Outer_SN_0");

                // The breached-state art is an INACTIVE child in the baked scene. Fading it would
                // register it for a restore that REVEALS art the game deliberately hid.
                var ruin = new GameObject("Ruin_Wall_Outer_SN_0");
                ruin.transform.SetParent(wall.transform, false);
                Renderer ruinPiece = AddRendererChild(ruin, "RuinPiece_0");
                ruin.SetActive(false);

                var col = wall.GetComponent<BoxCollider>();
                var found = new List<Renderer>();
                int n = SmartMobileCamera.CollectOccluderRenderers(col, found);

                if (!found.Contains(clad))
                    failures.Add("the occluder fade does not reach the CLAD panel - the wall art the "
                        + "player actually sees would stay drawn while the camera pulls in to it");
                if (!found.Contains(steel))
                    failures.Add("the occluder fade does not reach the wall's second renderer - a "
                        + "partially-faded wall still blocks the view");
                if (found.Contains(ruinPiece))
                    failures.Add("the occluder fade reached an INACTIVE breached-state renderer - "
                        + "restoring it would reveal art the game deliberately hid");
                if (n != 2)
                    failures.Add("occluder resolution returned " + n + " renderer(s) for the baked wall "
                        + "shape; expected exactly the 2 ACTIVE ones");

                // -- Case 2: silent-safe. A bare blocker with no renderable child must fall back to
                // the nearest ANCESTOR renderer and must never throw. A wall that resolves to
                // nothing still pulls in at the point-blank backstop, exactly as before.
                orphanParent = new GameObject("ArtHost");
                orphanParent.AddComponent<MeshFilter>();
                Renderer hostRend = orphanParent.AddComponent<MeshRenderer>();
                var bare = new GameObject("BareBlocker");
                bare.transform.SetParent(orphanParent.transform, false);
                var bareCol = bare.AddComponent<BoxCollider>();

                found.Clear();
                int m = SmartMobileCamera.CollectOccluderRenderers(bareCol, found);
                if (m != 1 || !found.Contains(hostRend))
                    failures.Add("a blocker with no renderable child did not fall back to the nearest "
                        + "ancestor renderer (got " + m + ")");

                // -- Case 3: a null collider resolves to nothing and does not throw. -------------
                found.Clear();
                if (SmartMobileCamera.CollectOccluderRenderers(null, found) != 0 || found.Count != 0)
                    failures.Add("a null collider did not resolve to an empty fade set");
            }
            catch (Exception e)
            {
                failures.Add("occluder resolution threw " + e.GetType().Name + ": " + e.Message
                    + " - the fade path must be silent-safe, never throwing on an odd hierarchy");
            }
            finally
            {
                if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
                if (orphanParent != null) UnityEngine.Object.DestroyImmediate(orphanParent);
            }
        }

        /// <summary>A child GameObject carrying a real MeshRenderer, for the resolution cases.</summary>
        private static Renderer AddRendererChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>();
            return go.AddComponent<MeshRenderer>();
        }

        /// <summary>The inverse pin: layers that must NEVER push or fade against the camera.</summary>
        private static void RequireLayerNotInMask(List<string> failures, int mask, string layerName, string why)
        {
            int idx = LayerMask.NameToLayer(layerName);
            if (idx < 0) return;   // a layer the project does not declare cannot be in the mask
            if ((mask & (1 << idx)) != 0)
                failures.Add("camera occlusion mask INCLUDES layer " + idx + ":" + layerName + " - " + why);
        }

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
