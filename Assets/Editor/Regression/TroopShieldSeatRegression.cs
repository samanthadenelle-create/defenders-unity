// =============================================================================
// TroopShieldSeatRegression [troop-shield-seat]  —  WO-1616
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers:  TROOP_SHIELD_SEAT_OK / TROOP_SHIELD_SEAT_FAIL
// Standalone: run-unity-method DeNelle.Editor.Regression.TroopShieldSeatRegression.RunAll
//
// THE DEFECT THIS SUITE EXISTS TO CATCH (WO-1616 §1, read at source 2026-09-09):
//   TroopGearApplier.ApplyDefaultGrip attached EVERY off-hand to LeftHand with one
//   hard-coded position/rotation/scale triple, under a header conceding its own
//   ceiling ("Coarse grips for ~1.8 m Supercyan / Tripo humanoids"). No per-rig,
//   per-mesh or per-shield term existed anywhere in it, so every deployed raid NPC
//   wore a visibly wrong shield for the whole raid — the live log names
//   troop-echo-legionnaire with TroopGear/Shield on LeftHand five times.
//
// The hero never had this bug: the hero MEASURES (WeaponOrientHelper.TryResolveShieldFrame
// -> GearSeat.GetShieldAxes -> TryComputeShieldMountRotation -> EnsureShieldOuterFaces
// -> GearSeat.ShieldPlateOffBone). WO-1616's fix is ONE authority shared by both, never a
// second offset table — so this suite's real subject is not "is the angle nice", it is
// "do the two paths execute the SAME instructions".
//
// ⛔ WHY THE FIXTURE'S HAND IS ROTATED BY AN AWKWARD EULER, AND WHY THERE IS A CONTROL.
// On a hand at identity the deleted constant's flat 90-degree yaw happens to land the
// plate's thickness on the same world LINE the derivation aims for — so an oracle written
// on a clean rig would go GREEN against the old code and prove nothing (the WO-1138
// "a probe that cannot fail" lesson, and the reason six staff fixes shipped visibly wrong
// staves before WO-1431). Every case here therefore seats the shield on a hand carrying a
// deliberately non-trivial local rotation, and Case 1 additionally runs a CONTROL fixture
// with the legacy triple written by the suite itself and FAILS IF THE CONTROL IS NOT RED.
// The suite that cannot detect the defect is itself a defect.
//
// ⛔ THE MUTATION THIS CATCHES, stated so it can be re-run by hand: re-insert an
// `else if (shield) { t.localPosition = ...; t.localRotation = Quaternion.Euler(0,90,0); }`
// branch in TroopGearApplier.ApplyDefaultGrip and route the off-hand back to it. Case 1's
// derived assertion and Case 3's literal-seat assertion both go red immediately.
//
// ⚠ WHAT THIS SUITE CANNOT PROVE (§11B — named, not papered over): it cannot prove the
// shield LOOKS right on the owner's device. It measures the seat's geometry against the
// owner's own rule (thickness away from the body, longest extent along the forearm), on a
// synthetic plate. Which of the two faces ends outward depends on the smooth-vs-handle
// score, which needs a readable mesh; the shipped troop shield (Supercyan Fantasy_Shield,
// isReadable: 1 read at source 2026-09-09) can be scored on device, but a plain cube
// fixture has no handle to find. The acceptance is the raid screenshot
// (memory `screenshots-are-primary-evidence-for-visual-defects`).
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Geometry;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class TroopShieldSeatRegression
    {
        // A heater-ish plate: clearly plate-shaped (narrowest is far under 0.9x the longest,
        // which is TryResolveShieldFrame's own "is this a plate at all" clause).
        private static readonly Vector3 PlateScale = new Vector3(0.40f, 0.55f, 0.06f);

        // The awkward hand pose. Nothing is special about these numbers except that they are
        // NOT axis-aligned, which is the whole point — see the header.
        private static readonly Vector3 HandEuler = new Vector3(37f, 101f, -23f);

        // The deleted constant, written HERE (in the control) and nowhere in shipped code.
        private static readonly Vector3 LegacyPos = new Vector3(0.05f, 0.05f, 0.02f);
        private static readonly Vector3 LegacyEuler = new Vector3(0f, 90f, 0f);

        private const float DerivedToleranceDeg = 15f;
        private const float ControlMustBeOffByDeg = 30f;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("TROOP_SHIELD_SEAT_OK - " + reason);
            else Debug.LogError("TROOP_SHIELD_SEAT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string seatSummary = "";
            string catalogSummary = "";
            try
            {
                Case(failures, "npc-shield-uses-the-shared-authority",
                     () => seatSummary = Case1_NpcShieldSeatsOnTheMeasuredFrame(failures));
                Case(failures, "one-authority-hero-and-npc-agree",
                     () => Case2_TroopPathAddsNothingOfItsOwn(failures));
                Case(failures, "no-literal-seat-survives",
                     () => Case3_NoLiteralSeatSurvives(failures));
                Case(failures, "unmeasurable-falls-back-never-guesses",
                     () => Case4_UnmeasurableShieldFallsBack(failures));
                Case(failures, "precedence-still-vetoes",
                     () => Case5_PrecedenceStillVetoesTheDerivation(failures));
                Case(failures, "live-offhands-are-shields",
                     () => catalogSummary = Case6_LiveOffhandRowsAreShields(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "TROOP SHIELD SEAT OK - " + seatSummary + "; " + catalogSummary +
                         "; the raid NPC and the hero seat a shield through the same authority, " +
                         "an unmeasurable plate falls back instead of guessing, and precedence " +
                         "still outranks the derivation";
                return true;
            }
            reason = "troop-shield-seat FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  Fixture — a body, a rotated hand bone, a grip root, a plate. No scene, no rig.
        // =====================================================================
        //
        // The prop is a primitive cube: a unit cube's mesh bounds are exactly +/-0.5, so a
        // localScale of (w, h, t) gives an exact parent-local extent — the seat reads BOUNDS,
        // and a cube's bounds need no import, no readability and no art.
        //
        // The two-level shape (gripRoot -> prop) is not decoration: the measured ShieldFrame is
        // expressed in the PARENT's frame and the seat then rotates that parent, so the prop must
        // NOT be the transform being rotated. That is exactly the shape WO-1616 gave the NPC path.
        private sealed class Plate : IDisposable
        {
            public GameObject Body;
            public Transform Hand;
            public Transform GripRoot;
            public GameObject Prop;

            public Plate(string name) : this(name, PlateScale) { }

            public Plate(string name, Vector3 scale)
            {
                Body = new GameObject("Fixture_Body_" + name);
                Body.transform.position = Vector3.zero;
                Body.transform.rotation = Quaternion.identity;

                var hand = new GameObject("LeftHand");
                hand.transform.SetParent(Body.transform, false);
                hand.transform.localPosition = new Vector3(-0.22f, 1.15f, 0.05f);
                hand.transform.localRotation = Quaternion.Euler(HandEuler);
                Hand = hand.transform;

                var grip = new GameObject("TroopGear_ShieldGrip");
                grip.transform.SetParent(Hand, false);
                GripRoot = grip.transform;

                Prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Prop.name = name;
                Prop.transform.SetParent(GripRoot, false);
                Prop.transform.localScale = scale;
                Prop.transform.localPosition = Vector3.zero;
                Prop.transform.localRotation = Quaternion.identity;
            }

            /// <summary>The body-derived axes the owner's rule is written against. Animator is
            /// null in the fixture, which is the documented no-rig branch of GetShieldAxes:
            /// outward = -body.right, up = body.up.</summary>
            public void Axes(out Vector3 outward, out Vector3 up)
                => GearSeat.GetShieldAxes(null, Body.transform, out outward, out up);

            /// <summary>The angle between the plate's measured face normal and the outward LINE,
            /// measured off the SEATED transform. Deliberately sign-agnostic: which of the two
            /// faces ends outward is the handle-side question, which a featureless cube cannot
            /// answer and which only a device screenshot closes (see the header).</summary>
            public float FaceOffOutwardDeg(WeaponOrientHelper.ShieldFrame frame)
            {
                Axes(out Vector3 outward, out _);
                Vector3 faceWorld = GripRoot.rotation * frame.ThicknessAxis;
                float off = Vector3.Angle(faceWorld, outward);
                return Mathf.Min(off, 180f - off);
            }

            /// <summary>The angle between the plate's longest extent and the forearm/up axis,
            /// same line-convention as above.</summary>
            public float LongOffUpDeg(WeaponOrientHelper.ShieldFrame frame)
            {
                Axes(out _, out Vector3 up);
                Vector3 longWorld = GripRoot.rotation * frame.LongAxis;
                float off = Vector3.Angle(longWorld, up);
                return Mathf.Min(off, 180f - off);
            }

            public bool TryFrame(out WeaponOrientHelper.ShieldFrame frame)
                => WeaponOrientHelper.TryResolveShieldFrame(Prop, GripRoot, out frame);

            public void Dispose()
            {
                if (Body != null) UnityEngine.Object.DestroyImmediate(Body);
            }
        }

        // =====================================================================
        //  Case 1 — THE NPC SHIELD IS SEATED FROM THE MEASURED FRAME
        // =====================================================================
        //
        // ⭐ RED AGAINST THE PRE-WO-1616 TREE, and it proves that AT RUN TIME rather than in
        // prose: the CONTROL fixture below is seated with the legacy triple (typed in this file,
        // where a constant belongs when its job is to be shown wrong) and the case FAILS IF THE
        // CONTROL IS NOT AT LEAST 30 DEGREES OFF. So the pass cannot be a coincidence of the
        // fixture's pose, and the failure message can quote both numbers.
        private static string Case1_NpcShieldSeatsOnTheMeasuredFrame(List<string> failures)
        {
            string summary = "";

            using (var live = new Plate("TroopGear_Shield"))
            {
                var seat = TroopGearApplier.SeatShieldOnHand(
                    live.Prop, live.GripRoot, live.Hand, null, live.Body.transform,
                    "troop-shield-seat fixture");

                if (!seat.FrameValid)
                {
                    failures.Add("[npc-shield-uses-the-shared-authority] the shared authority " +
                                 "reported NO measurable shield frame on a " + PlateScale +
                                 " plate with a live MeshRenderer. Either TryResolveShieldFrame " +
                                 "stopped reading primitive meshes or the NPC path no longer " +
                                 "reaches it - the seat cannot be asserted at all until that is " +
                                 "fixed, and every raid NPC is back on an underived shield.");
                    return "";
                }

                if (!seat.Derived || seat.Rule != EquipmentController.ShieldRuleDerived)
                {
                    failures.Add("[npc-shield-uses-the-shared-authority] the NPC shield was seated " +
                                 "by rule '" + seat.Rule + "' (derived=" + seat.Derived + "), not " +
                                 "the measured derivation '" + EquipmentController.ShieldRuleDerived +
                                 "'. WO-1616: the troop path must reach the SAME authority the hero " +
                                 "uses. If this fires, the NPC is back on a path of its own.");
                    return "";
                }

                float faceOff = live.FaceOffOutwardDeg(seat.Frame);
                float longOff = live.LongOffUpDeg(seat.Frame);

                if (faceOff > DerivedToleranceDeg)
                    failures.Add("[npc-shield-uses-the-shared-authority] THE PLATE IS NOT FACING " +
                                 "OUTWARD: its measured thickness axis sits " +
                                 faceOff.ToString("0.#") + " deg off the body's outward line " +
                                 "(want <= " + DerivedToleranceDeg.ToString("0") + "). Owner rule, " +
                                 "docs/WEAPON_ARMOR_ORIENT_LOGIC.md: \"the thinness/thickness of " +
                                 "the shield is facing away from the player, with the handle where " +
                                 "the hand mounts\". A reading near 90 deg is the edge-on slab the " +
                                 "raid capture showed.");

                if (longOff > DerivedToleranceDeg)
                    failures.Add("[npc-shield-uses-the-shared-authority] the plate's LONGEST extent " +
                                 "sits " + longOff.ToString("0.#") + " deg off the forearm/up axis " +
                                 "(want <= " + DerivedToleranceDeg.ToString("0") + ") - the shield " +
                                 "is lying sideways on the arm.");

                // The plate must also have been pushed OFF the bone, or it renders through the
                // forearm. Half the measured thickness here is 0.03 m; GearSeat caps the push at
                // 0.12 m. Zero means SeatShieldPlateOnSocket never ran.
                float push = live.GripRoot.localPosition.magnitude;
                if (push < 1e-3f)
                    failures.Add("[npc-shield-uses-the-shared-authority] the seated grip root is " +
                                 "still AT the bone origin (|localPosition|=" + push.ToString("0.####") +
                                 "m). GearSeat.ShieldPlateOffBone never ran, so the plate intersects " +
                                 "the forearm - only the handle loop is allowed to.");
                if (push > 0.30f)
                    failures.Add("[npc-shield-uses-the-shared-authority] the seated grip root is " +
                                 push.ToString("0.###") + "m off the bone - that is a floating " +
                                 "shield, not a strapped one (GearSeat caps the plate push at " +
                                 "0.12m; anything larger means a second writer moved it).");

                summary = "npc plate faceOff=" + faceOff.ToString("0.#") + "deg longOff=" +
                          longOff.ToString("0.#") + "deg offBone=" + push.ToString("0.###") + "m";
            }

            // ── THE CONTROL: the deleted constant, on the same fixture ────────────────────────
            using (var control = new Plate("TroopGear_Shield_LEGACY_CONTROL"))
            {
                if (!control.TryFrame(out WeaponOrientHelper.ShieldFrame frame) || !frame.Valid)
                {
                    failures.Add("[npc-shield-uses-the-shared-authority] the CONTROL fixture could " +
                                 "not be measured, so this case cannot prove it is able to detect " +
                                 "the defect at all.");
                    return summary;
                }

                control.GripRoot.localPosition = LegacyPos;
                control.GripRoot.localRotation = Quaternion.Euler(LegacyEuler);
                control.GripRoot.localScale = Vector3.one;

                float controlFaceOff = control.FaceOffOutwardDeg(frame);
                if (controlFaceOff < ControlMustBeOffByDeg)
                    failures.Add("[npc-shield-uses-the-shared-authority] ⛔ THE CONTROL IS NOT RED. " +
                                 "The deleted hard-coded triple lands the plate only " +
                                 controlFaceOff.ToString("0.#") + " deg off outward on this " +
                                 "fixture (needs >= " + ControlMustBeOffByDeg.ToString("0") + " for " +
                                 "the case to mean anything), so a green result here would NOT " +
                                 "prove the derivation is doing the work. Rotate the fixture's hand " +
                                 "further off-axis - do NOT relax the derived tolerance. This is the " +
                                 "WO-1138 'a probe that cannot fail' guard.");
                else
                    summary += "; legacy control " + controlFaceOff.ToString("0.#") + "deg off (red, as required)";
            }

            return summary;
        }

        // =====================================================================
        //  Case 2 — ONE AUTHORITY: the troop path adds NOTHING of its own
        // =====================================================================
        //
        // This is the case the ticket is actually about. Two identical fixtures: one seated
        // through the NPC entry point, one seated by calling the shared authority directly, the
        // way the hero does. If the troop path ever re-acquires a nudge, a scale write or a second
        // offset of its own, the two diverge and this goes red — which is the ONLY mechanical
        // guard against "one authority" quietly becoming two again.
        private static void Case2_TroopPathAddsNothingOfItsOwn(List<string> failures)
        {
            using var viaTroop = new Plate("TroopGear_Shield_A");
            using var viaAuthority = new Plate("TroopGear_Shield_B");

            var troopSeat = TroopGearApplier.SeatShieldOnHand(
                viaTroop.Prop, viaTroop.GripRoot, viaTroop.Hand, null, viaTroop.Body.transform,
                "one-authority fixture A");

            var direct = EquipmentController.SeatShieldMountRotation(
                viaAuthority.Prop, viaAuthority.GripRoot, viaAuthority.Hand, null,
                viaAuthority.Body.transform, mayDerive: true, subject: "one-authority fixture B");
            if (direct.Derived)
                EquipmentController.SeatShieldPlateOnSocket(
                    viaAuthority.GripRoot, viaAuthority.Hand, null, viaAuthority.Body.transform,
                    direct.Frame, "one-authority fixture B");

            if (!troopSeat.Derived || !direct.Derived)
            {
                failures.Add("[one-authority-hero-and-npc-agree] one of the two seats did not " +
                             "derive (troop=" + troopSeat.Derived + " direct=" + direct.Derived +
                             ") - the comparison below would be meaningless, so it is not made.");
                return;
            }

            float rotDelta = Quaternion.Angle(viaTroop.GripRoot.localRotation,
                                              viaAuthority.GripRoot.localRotation);
            float posDelta = Vector3.Distance(viaTroop.GripRoot.localPosition,
                                              viaAuthority.GripRoot.localPosition);
            float scaleDelta = Vector3.Distance(viaTroop.GripRoot.localScale,
                                                viaAuthority.GripRoot.localScale);

            if (rotDelta > 0.5f || posDelta > 1e-3f || scaleDelta > 1e-3f)
                failures.Add("[one-authority-hero-and-npc-agree] ⛔ TWO AUTHORITIES AGAIN. Seating " +
                             "the same plate through TroopGearApplier and through " +
                             "EquipmentController.SeatShieldMountRotation directly gave DIFFERENT " +
                             "results: rot " + rotDelta.ToString("0.##") + " deg, pos " +
                             posDelta.ToString("0.####") + " m, scale " + scaleDelta.ToString("0.####") +
                             " apart. The troop path is adding a term of its own again - that is the " +
                             "exact WO-1616 defect, whatever the numbers look like. Delete the extra " +
                             "term; do not re-tune it (WO-1616 §4).");
        }

        // =====================================================================
        //  Case 3 — THE LITERAL SEAT MUST NOT SURVIVE ANYWHERE
        // =====================================================================
        //
        // Named after the mutation: re-add the triple and this fires. It asserts the OUTCOME
        // (where the prop ended up), not the source text, because a source grep passes the moment
        // someone writes the same numbers with different formatting.
        private static void Case3_NoLiteralSeatSurvives(List<string> failures)
        {
            using var live = new Plate("TroopGear_Shield");
            TroopGearApplier.SeatShieldOnHand(
                live.Prop, live.GripRoot, live.Hand, null, live.Body.transform,
                "no-literal-seat fixture");

            bool posIsLegacy = Vector3.Distance(live.GripRoot.localPosition, LegacyPos) < 1e-3f;
            bool rotIsLegacy = Quaternion.Angle(live.GripRoot.localRotation,
                                                Quaternion.Euler(LegacyEuler)) < 1f;

            if (posIsLegacy || rotIsLegacy)
                failures.Add("[no-literal-seat-survives] ⛔ THE HARD-CODED TRIPLE IS BACK. The " +
                             "seated NPC shield landed on the deleted constant (pos match=" +
                             posIsLegacy + ", rot match=" + rotIsLegacy + "). WO-1616 §5: " +
                             "TroopGearApplier must contain no literal shield seat, and the fix is " +
                             "to DELETE the copy, never to re-dial it - a second table drifts from " +
                             "the hero's the moment a new rig or a new shield mesh arrives " +
                             "(CLAUDE.md §2/§5/§16).");
        }

        // =====================================================================
        //  Case 4 — AN UNMEASURABLE PLATE FALLS BACK; IT NEVER GUESSES
        // =====================================================================
        //
        // A cube is not plate-shaped: its shortest extent is not meaningfully thinner than its
        // longest, so no face normal exists to point anywhere. TryResolveShieldFrame refuses,
        // and the authority must leave the prop exactly as it arrived with a Warn saying so.
        //
        // ⚠ DELIBERATE BEHAVIOUR CHANGE, RECORDED: before WO-1616 an unmeasurable troop shield
        // got the hard-coded triple. It now stays at its parent's frame. That is a direct
        // consequence of WO-1616 §4/§5 (the constant is deleted), and it is the honest outcome —
        // an un-tunable constant on an unmeasurable mesh was never a seat, only a number.
        private static void Case4_UnmeasurableShieldFallsBack(List<string> failures)
        {
            using var blob = new Plate("TroopGear_Shield_NOT_A_PLATE", new Vector3(0.3f, 0.3f, 0.3f));

            var seat = TroopGearApplier.SeatShieldOnHand(
                blob.Prop, blob.GripRoot, blob.Hand, null, blob.Body.transform,
                "not-plate fixture");

            if (seat.Derived || seat.FrameValid)
                failures.Add("[unmeasurable-falls-back-never-guesses] a CUBE was accepted as a " +
                             "shield (frameValid=" + seat.FrameValid + " derived=" + seat.Derived +
                             "). TryResolveShieldFrame's plate-shape clause exists so an ambiguous " +
                             "mesh FALLS BACK instead of inventing a face normal (WO-1123: " +
                             "\"ambiguity falls back, it does not guess\").");

            if (seat.Rule != EquipmentController.ShieldRuleNotDerived)
                failures.Add("[unmeasurable-falls-back-never-guesses] the un-derived seat reported " +
                             "rule '" + seat.Rule + "' instead of '" +
                             EquipmentController.ShieldRuleNotDerived + "'. That string is the " +
                             "discriminator the owner and the F8 triage read on device to split " +
                             "\"the derivation never ran\" from \"it ran and is wrong\" - it must " +
                             "not drift.");

            if (Quaternion.Angle(blob.GripRoot.localRotation, Quaternion.identity) > 0.01f ||
                blob.GripRoot.localPosition.magnitude > 1e-4f)
                failures.Add("[unmeasurable-falls-back-never-guesses] an unmeasurable shield was " +
                             "MOVED anyway (lPos=" + blob.GripRoot.localPosition + " lEuler=" +
                             blob.GripRoot.localEulerAngles + "). When the geometry cannot answer, " +
                             "the authority must return the caller to its existing frame - writing " +
                             "a guessed transform here is how the deleted constant was born.");
        }

        // =====================================================================
        //  Case 5 — PRECEDENCE STILL OUTRANKS THE DERIVATION (the hero's guard)
        // =====================================================================
        //
        // WO-1616 must not become a hero-visual change. The hero feeds the WO-1123/WO-1215 ladder
        // (authored offset row -> substantiated manual -> derived -> archetype default) into the
        // shared authority as `mayDerive`. With it FALSE, nothing may move — and the frame must
        // STILL be measured, because the sheathed pose has its own channel and reads it (the
        // owner's 2026-08-20 "measure first, decide after" ruling; before it, not one ShieldFrame
        // line appeared in a whole device capture).
        private static void Case5_PrecedenceStillVetoesTheDerivation(List<string> failures)
        {
            using var owned = new Plate("TroopGear_Shield_AUTHORED");

            var seat = EquipmentController.SeatShieldMountRotation(
                owned.Prop, owned.GripRoot, owned.Hand, null, owned.Body.transform,
                mayDerive: false, subject: "precedence fixture");

            if (seat.Derived)
                failures.Add("[precedence-still-vetoes] the derivation RAN on a row whose seat is " +
                             "owned by precedence (mayDerive=false). An owner-dialled seat must be " +
                             "left byte-for-byte alone - docs/WEAPON_ARMOR_ORIENT_LOGIC.md: " +
                             "\"manual=true is CANON\".");

            if (!seat.FrameValid)
                failures.Add("[precedence-still-vetoes] the shield frame was NOT measured on a " +
                             "precedence-owned row. Measurement is not a decision (owner ruling " +
                             "2026-08-20): the walk happens once at attach and the SHEATHED pose, " +
                             "which has its own precedence channel, reads the same numbers. Moving " +
                             "the walk back inside the drawn gate is the 2026-08-20 defect.");

            if (Quaternion.Angle(owned.GripRoot.localRotation, Quaternion.identity) > 0.01f)
                failures.Add("[precedence-still-vetoes] a precedence-owned shield had a rotation " +
                             "written onto its grip root anyway (lEuler=" +
                             owned.GripRoot.localEulerAngles + ").");
        }

        // =====================================================================
        //  Case 6 — EVERY LIVE OFF-HAND IS ACTUALLY A SHIELD
        // =====================================================================
        //
        // The routing predicate is `isOffhand || path contains "shield"`, so a future troop given a
        // torch, a lantern or a buckler-that-is-not-a-plate in the off-hand slot would be sent to
        // the SHIELD authority and (correctly) refused by the plate-shape clause — landing at its
        // parent's frame with a Warn. That is a real, visible consequence and it should surface as
        // a gate line, not as a raid screenshot.
        //
        // Asserts a COUNT, never an id: the roster is owner-editable and pinning ids here would
        // turn her own authoring into a red gate (the WO-1431 Case 5 convention).
        private static string Case6_LiveOffhandRowsAreShields(List<string> failures)
        {
            var all = TroopCatalog.All;
            if (all == null || all.Count == 0)
            {
                failures.Add("[live-offhands-are-shields] the troop catalog loaded EMPTY - this " +
                             "case cannot tell whether the fix reaches any shipped troop.");
                return "";
            }

            int offhandRows = 0, shieldRows = 0;
            var nonShield = new List<string>();
            foreach (var t in all)
            {
                if (t == null || string.IsNullOrEmpty(t.Offhand)) continue;
                // "None" is the roster's own sentinel for "no off-hand" (it is a JSON string, not
                // null). Counting it as an off-hand would make this case argue about placeholders.
                if (string.Equals(t.Offhand, "None", StringComparison.OrdinalIgnoreCase)) continue;
                offhandRows++;
                if (t.Offhand.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0) shieldRows++;
                else if (nonShield.Count < 6) nonShield.Add(t.Id + " -> " + t.Offhand);
            }

            if (offhandRows == 0)
            {
                failures.Add("[live-offhands-are-shields] NO troop in the shipped roster carries an " +
                             "off-hand at all, so WO-1616's fix reaches nothing on device. Measured " +
                             "2026-09-09: troop-shieldguard and troop-echo-legionnaire both carry " +
                             "'TroopGear/Shield'. If that changed, either the roster was edited " +
                             "(fine - re-read the ticket) or the catalog did not load (not fine).");
                return "";
            }

            if (shieldRows != offhandRows)
                failures.Add("[live-offhands-are-shields] " + (offhandRows - shieldRows) + " of " +
                             offhandRows + " live off-hands are NOT shields (" +
                             string.Join(", ", nonShield) + "). TroopGearApplier routes EVERY " +
                             "off-hand to the shield authority, which refuses anything that is not " +
                             "plate-shaped - so these props will sit at their parent's frame with a " +
                             "Warn rather than being seated. Give the non-shield off-hands their " +
                             "own archetype rule (owner ruling required per " +
                             "docs/WEAPON_ARMOR_ORIENT_LOGIC.md: \"Ask the owner; do not guess\"); " +
                             "do NOT reinstate a catch-all constant.");

            return offhandRows + " live off-hand rows, " + shieldRows + " shields";
        }
    }
}
