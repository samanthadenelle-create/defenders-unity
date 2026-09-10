// =============================================================================
// SeatingPreviewShieldRegression [seating-preview-shield]  —  WO-1620
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers:  SEATING_PREVIEW_SHIELD_OK / SEATING_PREVIEW_SHIELD_FAIL
// Standalone: run-unity-method DeNelle.Editor.Regression.SeatingPreviewShieldRegression.RunAll
//
// THE DEFECT THIS SUITE EXISTS TO CATCH (WO-1620 §1, every line read at source 2026-09-10):
//   `EquipmentController.ApplySeatingPreview` — the Seating Editor's live preview — answered
//   "where does a shield sit?" a SECOND time. Its `shieldPreview` branch called
//   `WeaponOrientHelper.ComputeShieldMountRotation(frame, grt.parent, body.forward, body.up)`
//   directly, while the shipped attach path runs `EquipmentController.SeatShieldMountRotation`,
//   whose chain is GearSeat.GetShieldAxes -> WeaponOrientHelper.TryComputeShieldMountRotation ->
//   GearSeat.EnsureShieldOuterFaces. Three named divergences:
//     1. RAW body.forward / body.up instead of GetShieldAxes, which projects the body's left onto
//        the plane perpendicular to the FOREARM. The raw form ignores the arm entirely, so the two
//        agree only when the forearm happens to lie along the body axes.
//     2. EnsureShieldOuterFaces was never called — so the outer-face correction, AND the Step line
//        that names an opening/inner aimed wrong, were both missing from the preview.
//     3. No precedence gate inside the call (the branch's own `_currentOffHandDerivable` condition
//        turns out to be exactly the attach path's `shieldMayDerive` expression — see Case 5).
//   And a FOURTH, found while joining: the preview passed `_animator.transform` as `body` while
//   attach passes `transform`. `_animator` is resolved with GetComponentInChildren, so on a rig
//   whose Animator sits on a child those are different transforms with different right/up/forward.
//
// WHY IT MATTERS MORE THAN IT LOOKS: this is not a shipped visual defect — the runtime is fine. It
// is a WYSIWYG break inside the OWNER'S OWN TUNING TOOL. She dials a shield delta against a pose
// the game does not render, so every nudge she saves is measured from the wrong baseline. WO-994 is
// the named precedent for this exact class.
//
// ⛔ WHAT THIS SUITE CAN AND CANNOT DRIVE — read before trusting a green (CLAUDE.md §11B).
//   `ApplySeatingPreview` is an INSTANCE method whose shield branch reads `_currentOffHandProp`,
//   `_currentOffHandDerivable`, `_previewShieldFrame` and `_seatEditMode`. `_currentOffHandProp` is
//   assigned in exactly one place — the attach path (`_currentOffHandProp = gripRoot;`) — and there
//   is no public, internal or test seam that seeds it; `DeNelle.Village` publishes no
//   InternalsVisibleTo (grepped 2026-09-10, zero hits). Driving the real preview would therefore
//   mean standing up the catalog + Addressables + a humanoid rig inside an EditMode suite.
//   So the behavioural coverage below is:
//     (a) the SHARED entry point the preview now calls agrees with the authority the attach path
//         calls — the "one owner" pin, which goes red the instant anyone re-forks it;
//     (b) the OLD preview arithmetic, typed out in THIS file (where a thing whose job is to be
//         shown wrong belongs — TroopShieldSeatRegression's `LegacyPos`/`LegacyEuler` precedent),
//         is MEASURABLY off on the fixture, so the pass above cannot be a coincidence;
//     (c) a source lint proving no second derivation survives inside `ApplySeatingPreview`.
//   Case 3's lint is SUPPLEMENTARY, never the coverage (CLAUDE.md §8's own warning). A real
//   `ApplySeatingPreview` case needs a seeding seam on EquipmentController — that is a design call
//   for the lead, flagged in the WO-1620 RESULT, not something this lane invented quietly.
//
// ⛔ THE MUTATION THIS CATCHES, stated so it can be re-run by hand: restore the raw-axes call in
// `ApplySeatingPreview`'s shieldPreview branch —
//     baseRot = WeaponOrientHelper.ComputeShieldMountRotation(
//         _previewShieldFrame, grt.parent, body.forward, body.up);
// Case 3 goes red immediately (the call name reappears inside the method). Fork
// `TryDeriveShieldMountRotation` back into `SeatShieldMountRotation` instead and Case 1 goes red.
//
// ⚠ WHAT THIS SUITE CANNOT PROVE: it cannot prove the preview LOOKS like the game on the owner's
// rig. The fixture has NO Animator, which is the documented no-rig branch of GetShieldAxes
// (outward = -body.right), so the forearm projection — divergence 1's real teeth — is not exercised
// here at all. The delta Case 2 measures is the fixture's, NOT the shipped rig's. Only the owner
// opening the Seating Editor on a shield closes this ticket (WO-1620 §11, last line).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Core.Geometry;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class SeatingPreviewShieldRegression
    {
        // A heater-ish plate: clearly plate-shaped (narrowest far under 0.9x the longest, which is
        // TryResolveShieldFrame's own "is this a plate at all" clause).
        private static readonly Vector3 PlateScale = new Vector3(0.40f, 0.55f, 0.06f);

        // The awkward hand pose, reused verbatim from TroopShieldSeatRegression per WO-1620 §7.
        // Nothing is special about these numbers except that they are NOT axis-aligned.
        private static readonly Vector3 HandEuler = new Vector3(37f, 101f, -23f);

        private const float AgreementToleranceDeg = 0.5f;
        private const float DerivedToleranceDeg = 15f;
        private const float ControlMustBeOffByDeg = 30f;

        private const string EquipmentControllerRelative =
            "_Modules/Village/Hero/EquipmentController.cs";

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("SEATING_PREVIEW_SHIELD_OK - " + reason);
            else Debug.LogError("SEATING_PREVIEW_SHIELD_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string agreeSummary = "";
            string deltaSummary = "";
            try
            {
                Case(failures, "one-owner-preview-and-attach-agree",
                     () => agreeSummary = Case1_OneOwner(failures));
                Case(failures, "control-cannot-pass-for-free",
                     () => deltaSummary = Case2_ControlCannotPassForFree(failures));
                Case(failures, "no-second-derivation-in-the-preview",
                     () => Case3_NoSecondDerivation(failures));
                Case(failures, "dialled-delta-still-composes",
                     () => Case4_DialledDeltaStillComposes(failures));
                Case(failures, "owner-precedence-still-wins",
                     () => Case5_OwnerPrecedenceStillWins(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "SEATING PREVIEW SHIELD OK - " + agreeSummary + "; " + deltaSummary +
                         "; the Seating-Editor preview and the attach path derive a shield's mount " +
                         "rotation through ONE authority, no second derivation survives inside " +
                         "ApplySeatingPreview, a dialled delta still composes onto the derived base " +
                         "with the global yaw withheld, and owner precedence still outranks both";
                return true;
            }
            reason = "seating-preview-shield FAIL x" + failures.Count + ": " +
                     string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex)
            {
                failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // =====================================================================
        //  Fixture — a body, a rotated hand bone, a grip root, a plate. No scene, no rig.
        // =====================================================================
        //
        // Shape lifted from TroopShieldSeatRegression (WO-1616), for its reasons: a primitive cube's
        // mesh bounds are exactly +/-0.5, so a localScale of (w, h, t) gives an exact parent-local
        // extent and the seat — which reads BOUNDS — needs no import, no readability and no art.
        // The two-level shape (gripRoot -> prop) is load-bearing: the measured ShieldFrame is
        // expressed in the PARENT's frame and the seat rotates that parent, so the prop must NOT be
        // the transform being rotated. That is the same shape the hero's own grip root has.
        private sealed class Plate : IDisposable
        {
            public GameObject Body;
            public Transform Hand;
            public Transform GripRoot;
            public GameObject Prop;

            public Plate(string name) : this(name, HandEuler) { }

            public Plate(string name, Vector3 handEuler)
            {
                Body = new GameObject("Fixture_Body_" + name);
                Body.transform.position = Vector3.zero;
                Body.transform.rotation = Quaternion.identity;

                var hand = new GameObject("LeftHand");
                hand.transform.SetParent(Body.transform, false);
                hand.transform.localPosition = new Vector3(-0.22f, 1.15f, 0.05f);
                hand.transform.localRotation = Quaternion.Euler(handEuler);
                Hand = hand.transform;

                var grip = new GameObject("OffHandGrip");
                grip.transform.SetParent(Hand, false);
                GripRoot = grip.transform;

                Prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Prop.name = name;
                Prop.transform.SetParent(GripRoot, false);
                Prop.transform.localScale = PlateScale;
                Prop.transform.localPosition = Vector3.zero;
                Prop.transform.localRotation = Quaternion.identity;
            }

            /// <summary>The body-derived axes the owner's rule is written against. The Animator is
            /// null in the fixture, which is the documented no-rig branch of GetShieldAxes:
            /// outward = -body.right, up = body.up. See the header's "cannot prove" note — the
            /// forearm projection is NOT exercised by this fixture.</summary>
            public void Axes(out Vector3 outward, out Vector3 up)
                => GearSeat.GetShieldAxes(null, Body.transform, out outward, out up);

            /// <summary>The angle between the plate's measured face normal and the outward LINE,
            /// measured off the SEATED transform — never off a returned number. WO-1226 note 1 in
            /// docs/WEAPON_ARMOR_ORIENT_LOGIC.md: a trace can print a perfect 0deg about a prop
            /// hanging sideways. Deliberately sign-agnostic: WHICH of the two faces ends outward is
            /// the handle-side question, which a featureless cube cannot answer and which only a
            /// device screenshot closes.</summary>
            public float FaceOffOutwardDeg(WeaponOrientHelper.ShieldFrame frame)
            {
                Axes(out Vector3 outward, out _);
                Vector3 faceWorld = GripRoot.rotation * frame.ThicknessAxis;
                float off = Vector3.Angle(faceWorld, outward);
                return Mathf.Min(off, 180f - off);
            }

            public bool TryFrame(out WeaponOrientHelper.ShieldFrame frame)
                => WeaponOrientHelper.TryResolveShieldFrame(Prop, GripRoot, out frame);

            public void Dispose()
            {
                if (Body != null) UnityEngine.Object.DestroyImmediate(Body);
            }
        }

        /// <summary>
        /// ⛔ THE DELETED PREVIEW ARITHMETIC, typed out HERE and nowhere in shipped code.
        /// This is exactly what `ApplySeatingPreview`'s shieldPreview branch used to run before
        /// WO-1620: the reduced helper call with RAW body axes, no GearSeat.GetShieldAxes and no
        /// GearSeat.EnsureShieldOuterFaces. It exists so Case 2 can MEASURE how far off it was
        /// rather than assert it in prose, and so the suite is demonstrably able to detect the
        /// defect at all (the WO-1138 "a probe that cannot fail" guard).
        /// </summary>
        private static Quaternion LegacyPreviewBaseRot(
            WeaponOrientHelper.ShieldFrame frame, Transform mount, Transform body)
            => WeaponOrientHelper.ComputeShieldMountRotation(frame, mount, body.forward, body.up);

        // =====================================================================
        //  Case 1 — ONE OWNER: the preview's entry point IS the attach path's
        // =====================================================================
        //
        // The attach path calls `SeatShieldMountRotation`, which WRITES the rotation onto the grip
        // root. The preview cannot use that — it needs the VALUE, because it then composes the
        // owner's dialled euler onto it and decides the global-yaw question. So WO-1620 §3a option
        // (a) was taken: the derivation was lifted into `TryDeriveShieldMountRotation`, and
        // `SeatShieldMountRotation` became a thin write-wrapper over it. That is WO-1616's own
        // shape, and it is the only join that cannot drift, because there is nothing left to drift.
        //
        // This case seats the SAME plate twice — once by letting the authority write it, once by
        // writing the shared entry point's answer the way the preview composes it — and requires
        // them to land within half a degree, measured off the SEATED transform.
        //
        // ⚠ HONESTY: as written today this pin is green by construction (the wrapper calls the
        // entry point). Its job is not to be red now; it is to go red the moment someone re-forks
        // the authority, or gives the write path a step the compute path does not have. That is
        // exactly the failure WO-1620 exists to end, and it has no other mechanical guard.
        private static string Case1_OneOwner(List<string> failures)
        {
            using var viaAuthority = new Plate("Shield_ViaAuthority");
            using var viaPreview = new Plate("Shield_ViaPreviewEntryPoint");

            var seat = EquipmentController.SeatShieldMountRotation(
                viaAuthority.Prop, viaAuthority.GripRoot, viaAuthority.Hand, null,
                viaAuthority.Body.transform, true, "seating-preview-shield fixture (attach)");

            if (!seat.FrameValid)
            {
                failures.Add("[one-owner-preview-and-attach-agree] the authority reported NO " +
                             "measurable shield frame on a " + PlateScale + " plate with a live " +
                             "MeshRenderer. Either TryResolveShieldFrame stopped reading primitive " +
                             "meshes or SeatShieldMountRotation no longer reaches it — nothing " +
                             "below can be asserted until that is fixed.");
                return "";
            }
            if (!seat.Derived || seat.Rule != EquipmentController.ShieldRuleDerived)
            {
                failures.Add("[one-owner-preview-and-attach-agree] the attach path seated by rule '" +
                             seat.Rule + "' (derived=" + seat.Derived + "), not the measured " +
                             "derivation '" + EquipmentController.ShieldRuleDerived + "'. The " +
                             "preview cannot be compared against a path that did not derive.");
                return "";
            }

            // The PREVIEW's shape: it hands in its OWN re-measured frame (WO-1123 — the attach
            // frame was measured on the runtime seat, which for a NATIVE shield is SeatNative,
            // while the preview re-seats through NormalizeInto; reusing it would pose the preview
            // off the wrong axis). Here the fixture's frame is measured the same way the preview
            // measures its own, then handed to the SAME entry point ApplySeatingPreview now calls.
            if (!viaPreview.TryFrame(out WeaponOrientHelper.ShieldFrame previewFrame) ||
                !previewFrame.Valid)
            {
                failures.Add("[one-owner-preview-and-attach-agree] the preview-side fixture could " +
                             "not be measured, so the comparison cannot be made.");
                return "";
            }

            if (!EquipmentController.TryDeriveShieldMountRotation(
                    previewFrame, viaPreview.Hand, null, viaPreview.Body.transform,
                    "seating-preview-shield fixture (preview)", out Quaternion previewBaseRot))
            {
                failures.Add("[one-owner-preview-and-attach-agree] the SHARED entry point " +
                             "TryDeriveShieldMountRotation DECLINED on a plate the attach path " +
                             "derived successfully. The preview would silently fall back to the " +
                             "preset euler while the game derives — the divergence, re-created.");
                return "";
            }

            // Compose it the way ApplySeatingPreview does with a zero delta: the derived base IS
            // the grip root's local rotation, and the global yaw is withheld.
            viaPreview.GripRoot.localRotation = previewBaseRot * Quaternion.Euler(Vector3.zero);

            float agree = Quaternion.Angle(seat.MountLocal, previewBaseRot);
            if (agree > AgreementToleranceDeg)
                failures.Add("[one-owner-preview-and-attach-agree] ⛔ TWO ANSWERS TO ONE QUESTION: " +
                             "the attach path's mount rotation and the entry point the Seating " +
                             "Editor preview calls differ by " + agree.ToString("0.###") +
                             " deg (allowed " + AgreementToleranceDeg.ToString("0.0") + "). " +
                             "docs/ARCHITECTURE_PRINCIPLES.md 2b.1: one owner per concern. The " +
                             "owner would be dialling a shield nudge against a pose the game does " +
                             "not render — the WO-994 class of bug, in her own tuning tool.");

            float attachFaceOff = viaAuthority.FaceOffOutwardDeg(seat.Frame);
            float previewFaceOff = viaPreview.FaceOffOutwardDeg(previewFrame);
            float seatedDelta = Mathf.Abs(attachFaceOff - previewFaceOff);
            if (seatedDelta > AgreementToleranceDeg)
                failures.Add("[one-owner-preview-and-attach-agree] the two SEATED plates do not " +
                             "sit the same way: attach faceOffOutward=" +
                             attachFaceOff.ToString("0.##") + " deg, preview=" +
                             previewFaceOff.ToString("0.##") + " deg. Measured off the seated " +
                             "transform, not off a returned number (WO-1226 note 1).");

            if (attachFaceOff > DerivedToleranceDeg)
                failures.Add("[one-owner-preview-and-attach-agree] the derived plate is NOT facing " +
                             "outward: its measured thickness axis sits " +
                             attachFaceOff.ToString("0.#") + " deg off the body's outward line " +
                             "(want <= " + DerivedToleranceDeg.ToString("0") + "). Owner rule, " +
                             "docs/WEAPON_ARMOR_ORIENT_LOGIC.md: the thinness of the shield faces " +
                             "away from the player, with the handle where the hand mounts.");

            return "one owner: attach vs preview entry point agree to " + agree.ToString("0.###") +
                   " deg, both seated at faceOffOutward=" + attachFaceOff.ToString("0.##") + " deg";
        }

        // =====================================================================
        //  Case 2 — THE CONTROL: the OLD preview arithmetic must be MEASURABLY off
        // =====================================================================
        //
        // ⭐ THIS IS THE CASE THAT MAKES CASE 1 MEAN ANYTHING, and it is the WO-1620 §5 step-1
        // measurement: it prints the ANGLE between the old preview's answer and the authority's,
        // rather than asserting "it diverged" in prose (CLAUDE.md §11B — a code-read LOCATES, it
        // does not measure a delta).
        //
        // ⛔ WO-1616 RESULT §4 item 1 proved that on a hand at IDENTITY a wrong shield seat can land
        // on the same world line as a right one, so an oracle written on a clean rig goes green
        // against broken code (the WO-1138 "a probe that cannot fail" lesson). This case therefore
        // FAILS IF THE CONTROL IS NOT AT LEAST 30 DEG OFF on the fixture, and additionally runs the
        // control on an identity-handed fixture so the two numbers can be compared in the report.
        //
        // ⚠ WHAT THE NUMBER IS AND IS NOT. With no Animator, GetShieldAxes takes its documented
        // no-rig branch (outward = -body.right) while the old preview passed body.forward — two
        // axes 90 deg apart by construction, so this fixture's delta is dominated by that axis
        // choice and is INDEPENDENT of the hand's rotation. The SHIPPED delta, where GetShieldAxes
        // projects onto the plane perpendicular to the forearm, is NOT measured here and is not
        // claimed anywhere in this suite.
        private static string Case2_ControlCannotPassForFree(List<string> failures)
        {
            string summary;

            using (var awkward = new Plate("Shield_LEGACY_CONTROL_awkwardHand"))
            {
                if (!awkward.TryFrame(out WeaponOrientHelper.ShieldFrame frame) || !frame.Valid)
                {
                    failures.Add("[control-cannot-pass-for-free] the CONTROL fixture could not be " +
                                 "measured, so this case cannot prove it is able to detect the " +
                                 "defect at all.");
                    return "";
                }

                if (!EquipmentController.TryDeriveShieldMountRotation(
                        frame, awkward.Hand, null, awkward.Body.transform,
                        "seating-preview-shield control", out Quaternion derived))
                {
                    failures.Add("[control-cannot-pass-for-free] the shared entry point declined " +
                                 "on the control fixture, so there is nothing to compare against.");
                    return "";
                }

                Quaternion legacy = LegacyPreviewBaseRot(frame, awkward.Hand,
                                                         awkward.Body.transform);
                float delta = Quaternion.Angle(derived, legacy);

                // Now MEASURE it the way the owner sees it: seat the plate with the OLD answer and
                // read the face off outward off the seated transform.
                awkward.GripRoot.localRotation = legacy;
                float legacyFaceOff = awkward.FaceOffOutwardDeg(frame);

                if (legacyFaceOff < ControlMustBeOffByDeg)
                    failures.Add("[control-cannot-pass-for-free] ⛔ THE CONTROL IS NOT RED. The " +
                                 "pre-WO-1620 preview arithmetic lands the plate only " +
                                 legacyFaceOff.ToString("0.#") + " deg off outward on this " +
                                 "fixture (needs >= " + ControlMustBeOffByDeg.ToString("0") +
                                 " for Case 1 to mean anything), so a green result there would " +
                                 "NOT prove the shared derivation is doing the work. Rotate the " +
                                 "fixture further off-axis — do NOT relax Case 1's tolerance. This " +
                                 "is the WO-1138 'a probe that cannot fail' guard.");

                summary = "MEASURED DELTA (fixture, no Animator): the retired preview arithmetic " +
                          "sits " + delta.ToString("0.#") + " deg from the authority's answer and " +
                          "seats the plate " + legacyFaceOff.ToString("0.#") + " deg off outward";
            }

            // The identity-handed twin, reported so the two numbers sit side by side in the log and
            // nobody has to take on faith that the awkward pose was necessary (or that it was not).
            using (var identity = new Plate("Shield_LEGACY_CONTROL_identityHand", Vector3.zero))
            {
                if (identity.TryFrame(out WeaponOrientHelper.ShieldFrame frame) && frame.Valid)
                {
                    identity.GripRoot.localRotation =
                        LegacyPreviewBaseRot(frame, identity.Hand, identity.Body.transform);
                    summary += "; on an IDENTITY-handed twin the same arithmetic reads " +
                               identity.FaceOffOutwardDeg(frame).ToString("0.#") + " deg off " +
                               "(reported, not asserted — see the case comment: with no Animator " +
                               "this fixture's delta comes from the outward-axis choice, not the " +
                               "hand pose)";
                }
            }

            return summary;
        }

        // =====================================================================
        //  Case 3 — NO SECOND DERIVATION SURVIVES INSIDE ApplySeatingPreview
        // =====================================================================
        //
        // ⚠ SUPPLEMENTARY, NOT THE COVERAGE (CLAUDE.md §8's own warning about pointing at a
        // source-text lint as a system's proof). It is here because it is the ONE case that catches
        // the WO's named mutation — re-inlining the raw-axes call into the preview — and because
        // the real preview cannot be driven from an EditMode fixture (see the header).
        /// <summary>
        /// The stripped body of `ApplySeatingPreview`, or null with <paramref name="why"/> naming
        /// WHICH clause failed.
        /// <para>
        /// ⛔ IT REPORTS NOTHING ITSELF, ON PURPOSE (WO-1138 / RegressionMarkerRegression RULE 4).
        /// It used to take an `Action&lt;string&gt; Fail` and bank its own failure, which left every
        /// caller writing `if (body == null) return;` — a guard that returns having asserted nothing
        /// AT THE SITE. The caller's only channel is the bool, so the hollow-pass ratchet reads that
        /// as a PASS, and it caught this suite on its first gate run (Builds/wave2-reg1, 491/493,
        /// `SeatingPreviewShieldRegression.cs:613 [A-missing-dependency] guard 'body == null'`).
        /// Handing the reason BACK lets each guard arm bank an unconditional, case-tagged FAIL, so
        /// no path out of this helper can be silent.
        /// </para>
        /// </summary>
        private static string PreviewMethodBody(out string why)
        {
            why = null;
            string src = Read(EquipmentControllerRelative);
            if (src == null)
            {
                why = "could not read Assets/" + EquipmentControllerRelative + " at all";
                return null;
            }
            string code = StripCode(src);

            const string sig = "public void ApplySeatingPreview";
            int start = code.IndexOf(sig, StringComparison.Ordinal);
            if (start < 0)
            {
                why = "'" + sig + "' is GONE from Assets/" + EquipmentControllerRelative +
                      " — if it was renamed or moved, re-point this lint IN THE SAME CHANGE, " +
                      "because a lint that cannot find its subject silently stops guarding it";
                return null;
            }

            int open = code.IndexOf('{', start);
            if (open < 0)
            {
                why = "ApplySeatingPreview was found but has no method body";
                return null;
            }
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0) return code.Substring(open, i - open + 1);
                }
            }
            why = "ApplySeatingPreview's body never closes — the brace walk ran off the end of the file";
            return null;
        }

        /// <summary>
        /// The FAIL text every `body == null` guard banks. FIXTURE-ABSENT is the correct arm of the
        /// three-way rule here (`RegressionMarkerRegression`: fixture-absent -> FAIL naming the
        /// missing path; harness-capability-absent -> PartialSkip; content-absent -> assert through
        /// the fallback). `EquipmentController.cs` is a TRACKED source file at a fixed path, present
        /// in every clone — its absence is never a property of this machine, so PartialSkip would be
        /// wrong: it would declare "this harness cannot run the pin" when the truth is "the subject
        /// of the pin has vanished from the repo".
        /// </summary>
        private static string PreviewSourceMissing(string caseTag, string why)
            => "[" + caseTag + "] ⛔ FIXTURE ABSENT, so this pin asserted NOTHING: " + why +
               ". The missing path is Assets/" + EquipmentControllerRelative + " -> " +
               "ApplySeatingPreview. This is a FAIL and not a skip: that file is tracked and at a " +
               "fixed path, so it is present in every clone — its absence means the subject of the " +
               "pin left the repo, not that this machine cannot run it.";

        private static void Case3_NoSecondDerivation(List<string> failures)
        {
            string body = PreviewMethodBody(out string why);
            if (body == null)
            {
                failures.Add(PreviewSourceMissing("no-second-derivation-in-the-preview", why));
                return;
            }

            // The three steps that belong to the authority and to nothing else.
            var banned = new[]
            {
                "ComputeShieldMountRotation",
                "GetShieldAxes",
                "EnsureShieldOuterFaces",
            };
            foreach (string call in banned)
            {
                if (body.IndexOf(call, StringComparison.Ordinal) >= 0)
                    failures.Add("[no-second-derivation-in-the-preview] ⛔ ApplySeatingPreview calls '" +
                                 call + "' again. That is a SECOND derivation of a shield's mount " +
                                 "rotation, which is the entire defect WO-1620 closed — including " +
                                 "the 'same chain, inlined here' form. The preview must CALL " +
                                 "EquipmentController.TryDeriveShieldMountRotation and compose its " +
                                 "answer; copying the steps is the bug.");
            }

            if (body.IndexOf("TryDeriveShieldMountRotation", StringComparison.Ordinal) < 0)
                failures.Add("[no-second-derivation-in-the-preview] ApplySeatingPreview no longer " +
                             "calls TryDeriveShieldMountRotation at all. Either the shield preview " +
                             "was removed (then remove this suite in the same change and say why) " +
                             "or it grew a third route to the answer.");
        }

        // =====================================================================
        //  Case 4 — A DIALLED DELTA STILL COMPOSES, AND THE GLOBAL YAW IS STILL WITHHELD
        // =====================================================================
        //
        // The join must not change what a saved offset does. Two halves:
        //   (i) arithmetic — the composed grip rotation is base * Euler(delta), so a non-zero
        //       nudge still moves the plate by exactly the amount the owner dialled;
        //   (ii) shape — the composition line still routes a derived seat AWAY from
        //        ApplyGlobalWeaponYaw. WO-1123's ruling: the yaw flip corrects bone-inherited
        //        grips, and a derived world target has not inherited anything, so folding it in
        //        would face the shield's smooth side at the player.
        private static void Case4_DialledDeltaStillComposes(List<string> failures)
        {
            using var plate = new Plate("Shield_DialledDelta");
            if (!plate.TryFrame(out WeaponOrientHelper.ShieldFrame frame) || !frame.Valid)
            {
                failures.Add("[dialled-delta-still-composes] the fixture could not be measured.");
                return;
            }
            if (!EquipmentController.TryDeriveShieldMountRotation(
                    frame, plate.Hand, null, plate.Body.transform,
                    "seating-preview-shield delta", out Quaternion baseRot))
            {
                failures.Add("[dialled-delta-still-composes] the shared entry point declined.");
                return;
            }

            var delta = new Vector3(11f, -27f, 6f);
            plate.GripRoot.localRotation = baseRot * Quaternion.Euler(delta);

            float moved = Quaternion.Angle(baseRot, plate.GripRoot.localRotation);
            float wanted = Quaternion.Angle(Quaternion.identity, Quaternion.Euler(delta));
            if (Mathf.Abs(moved - wanted) > AgreementToleranceDeg)
                failures.Add("[dialled-delta-still-composes] a dialled delta of " + delta +
                             " moved the derived base by " + moved.ToString("0.##") +
                             " deg, but the delta itself is " + wanted.ToString("0.##") +
                             " deg. The owner's nudge is no longer worth what she dialled.");

            // (ii) — the composition SHAPE, read out of the shipped source.
            string body = PreviewMethodBody(out string why);
            if (body == null)
            {
                failures.Add(PreviewSourceMissing("dialled-delta-still-composes", why));
                return;
            }
            int compose = body.IndexOf("bowPreview || shieldPreview", StringComparison.Ordinal);
            if (compose < 0)
            {
                failures.Add("[dialled-delta-still-composes] the derived-vs-yaw composition branch " +
                             "('bowPreview || shieldPreview') is GONE from ApplySeatingPreview. " +
                             "That ternary is what withholds ApplyGlobalWeaponYaw from a derived " +
                             "seat (WO-1123); without it the preview shows a yaw the game does not " +
                             "apply, which is the WO-994 break this ticket closed.");
                return;
            }
            // Everything the ternary's DERIVED arm may contain, in order, before the yaw arm.
            int yaw = body.IndexOf("ApplyGlobalWeaponYaw", compose, StringComparison.Ordinal);
            int euler = body.IndexOf("Quaternion.Euler(euler)", compose, StringComparison.Ordinal);
            if (euler < 0 || (yaw >= 0 && yaw < euler))
                failures.Add("[dialled-delta-still-composes] the composition no longer reads " +
                             "'derived base * Euler(euler)' FIRST and the yaw-applied form second. " +
                             "A derived seat must take the yaw-less arm — check the ternary at the " +
                             "end of ApplySeatingPreview.");
        }

        // =====================================================================
        //  Case 5 — OWNER PRECEDENCE STILL WINS, ON BOTH PATHS
        // =====================================================================
        //
        // WO-1620 §3c: divergence 3 (the preview has no `mayDerive` parameter) is real but it is
        // NOT a licence to change what a dialled offset does. The resolution taken, stated here so
        // it is pinned rather than described:
        //
        //   The preview's gate is POSITIONAL, not a parameter. `ApplySeatingPreview` only enters
        //   the shield branch when `_currentOffHandDerivable` is true, and that field is assigned
        //   from the IDENTICAL expression the attach path builds `shieldMayDerive` from:
        //       !fullOverride && kind == Shield && WeaponOrientHelper.MayDerive(hasOffset, manual)
        //   So the preview effectively passes mayDerive:true and never reaches the derivation when
        //   an owner-dialled seat owns the row — the same verdict, one branch earlier. That is why
        //   `TryDeriveShieldMountRotation` takes no `mayDerive`: it is pure math, and precedence
        //   belongs to the caller that knows whose row it is.
        //
        // This case pins BOTH halves: the authority still refuses when told it may not derive, and
        // the preview's branch is still gated on `_currentOffHandDerivable`.
        private static void Case5_OwnerPrecedenceStillWins(List<string> failures)
        {
            using var dialled = new Plate("Shield_OwnerDialled");
            Quaternion before = dialled.GripRoot.localRotation;

            var seat = EquipmentController.SeatShieldMountRotation(
                dialled.Prop, dialled.GripRoot, dialled.Hand, null, dialled.Body.transform,
                false, "seating-preview-shield precedence fixture");

            if (seat.Derived || seat.Rule != EquipmentController.ShieldRulePrecedence)
                failures.Add("[owner-precedence-still-wins] ⛔ the authority DERIVED a seat it was " +
                             "told it may not touch (rule='" + seat.Rule + "' derived=" +
                             seat.Derived + ", expected '" +
                             EquipmentController.ShieldRulePrecedence + "'). An owner-dialled " +
                             "shield row must outrank the derivation — WO-1616's " +
                             "SHIELD-SEAT-OWNED-BY-PRECEDENCE ruling, unchanged by WO-1620.");

            if (Quaternion.Angle(before, dialled.GripRoot.localRotation) > AgreementToleranceDeg)
                failures.Add("[owner-precedence-still-wins] the grip root MOVED under a precedence " +
                             "veto. Refusing to derive must also mean refusing to write.");

            if (!seat.FrameValid)
                failures.Add("[owner-precedence-still-wins] the frame was not measured under a " +
                             "precedence veto. MEASURE FIRST, DECIDE AFTER (owner ruling " +
                             "2026-08-20): a vetoed shield still needs its frame, because the " +
                             "SHEATHED pose has its own channel and its own precedence and poses " +
                             "from these numbers. Proving line for the original defect: NOT ONE " +
                             "ShieldFrame line appears in logs/device/2026-08-20-equip.log.");

            string body = PreviewMethodBody(out string why);
            if (body == null)
            {
                failures.Add(PreviewSourceMissing("owner-precedence-still-wins", why));
                return;
            }
            int gate = body.IndexOf("_currentOffHandDerivable", StringComparison.Ordinal);
            int call = body.IndexOf("TryDeriveShieldMountRotation", StringComparison.Ordinal);
            if (gate < 0 || call < 0 || gate > call)
                failures.Add("[owner-precedence-still-wins] the preview's shield branch is no " +
                             "longer gated on _currentOffHandDerivable BEFORE it calls the shared " +
                             "derivation. That flag is the preview's whole precedence story — " +
                             "without it the Seating Editor would derive over a seat the owner " +
                             "dialled, while the game keeps her value.");
        }

        // =====================================================================
        //  Source-lint plumbing (shape borrowed from AggroLeashRegression)
        // =====================================================================

        private static string Read(string relative)
        {
            string p = Path.Combine(Application.dataPath, relative);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        // Strip // and /* */ comments AND "..." / '...' literals, so a match can only come from
        // real code. Deliberately simple + conservative: it blanks the contents, never reflows the
        // file, so brace depth and ordering survive.
        private static string StripCode(string src)
        {
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < src.Length && src[i] != quote)
                    {
                        if (src[i] == '\\') i++;
                        i++;
                    }
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
