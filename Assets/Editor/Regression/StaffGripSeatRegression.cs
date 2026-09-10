// =============================================================================
// StaffGripSeatRegression [staff-grip-seat]  —  WO-1431
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Markers:  STAFF_GRIP_SEAT_OK / STAFF_GRIP_SEAT_FAIL
// Standalone: run-unity-method DeNelle.Editor.Regression.StaffGripSeatRegression.RunAll
//
// THE DEFECT THIS SUITE EXISTS TO CATCH (owner, 2026-09-06, verbatim):
//   "the staff needs reversed, right now they grasp it 75% on the lower half
//    instead of on the upper half of the staff"
//
// THE PROVEN CAUSE (docs/READY_RCA_2026-09-09.md, device trace on Hero (Blaise),
// tripo_staff_a, a measured 1.2639 m staff): WeaponOrientHelper.TryDeriveStaffGripY
// computed the owner-ruled answer — fraction 0.75 -> gripY 0.7204 — and the live
// attach path THREW IT AWAY. The derivation was reachable only through
// TraceMeasuredSeat, a read-only prediction, while every non-native melee prop was
// seated by EquipmentController.SeatHiltLowerHalf: 18% up from the foot, with a
// crossguard refinement that `break`s at the midpoint (`if (by > mid) break;`) and
// so CANNOT return anything above 0.50. Two programs, one prop, and only the wrong
// one moved it.
//
// ⛔ WHY A NEW SUITE AND NOT A CASE IN AttachmentOffsetRegression. That suite's staff
// cases (Case5_StaffNeutralDefault, Case7_DrawnSeatVerticality) are about the drawn
// ROTATION — the owner's 2026-08-26 "staff drawn is showing horizontal" ruling and the
// StaffDrawnGripNudgeDefault (90,0,0) constant. This suite is about the GRIP POINT: WHERE
// along the shaft the fist closes. They are different axes of the same prop and folding
// them together is how a rotation fix and a grip fix start reverting each other. Nothing
// here touches a rotation, and Case 2 below actively guards that no future staff repair
// drags the bladed families along with it.
//
// ⛔ THE ORACLE MEASURES THE SEATED PROP, NOT THE RETURNED NUMBER. Every case re-reads
// the prop's bounds AFTER the shipped seat ran and asks where the hand (the grip root's
// origin) ended up along the shaft. A derived value can be arithmetically perfect and
// the prop still not move — six prior staff fixes asserted the deriver and shipped a
// build in which the staff was visibly wrong (docs/WEAPON_ARMOR_ORIENT_LOGIC.md,
// WO-1226 note 1: "Never accept it as proof a prop is standing"). The returned struct is
// asserted too, but only as a SECOND opinion — if the two disagree, that disagreement is
// itself a failure, because it means the trace line the owner is asked to read on device
// does not describe what happened to the mesh.
//
// ⛔ NOT PROVABLE HERE, stated rather than papered over: WHICH END IS THE HEAD.
// TryDeriveStaffGripY takes min-Y as the foot, i.e. whatever
// WeaponBoundsOrient.EnsureHandleAtShortYEnd already resolved; it does not independently
// find the finial (docs/WEAPON_MESH_ARCHETYPES.md §3 names the disambiguator, and also
// notes a plain quarterstaff has no head at all). On a staff imported head-down this
// grip lands 0.75 from the HEAD. Only the owner's device screenshot closes that, and it
// is named in WORK_ORDER_1431_*.RESULT.md as the thing the felt-test is judging.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Geometry;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class StaffGripSeatRegression
    {
        // The device-measured staff, so the fixture is not an invented number:
        // READY_RCA_2026-09-09 records tripo_staff_a on Hero (Blaise) at 1.2639 m.
        private const float DeviceMeasuredStaffLengthM = 1.2639f;
        private const float ShaftThicknessM = 0.06f;
        private const float SwordLengthM = 1.00f;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("STAFF_GRIP_SEAT_OK - " + reason);
            else Debug.LogError("STAFF_GRIP_SEAT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string staffSummary = "";
            string catalogSummary = "";
            try
            {
                Case(failures, "staff-seats-upper-shaft",
                     () => staffSummary = Case1_StaffSeatsOnTheUpperShaft(failures));
                Case(failures, "sword-keeps-lower-hilt",
                     () => Case2_SwordKeepsTheLowerHilt(failures));
                Case(failures, "precedence-still-wins",
                     () => Case3_AuthoredOrManualSeatStillWins(failures));
                Case(failures, "archetype-dispatch",
                     () => Case4_ArchetypeDispatchIsFamilyExact(failures));
                Case(failures, "live-staff-rows-derivable",
                     () => catalogSummary = Case5_LiveStaffRowsAreDerivable(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "STAFF GRIP SEAT OK - " + staffSummary + "; " + catalogSummary +
                         "; the bladed families keep the WO-577 hilt-lower-half rule and an " +
                         "authored/substantiated-manual seat still outranks the derivation";
                return true;
            }
            reason = "staff-grip-seat FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  Fixture — a bare shaft on a bare grip root. No scene, no rig, no hero.
        // =====================================================================
        //
        // The prop is a primitive cube scaled to a shaft: a unit cube's mesh bounds are ±0.5, so a
        // localScale of (t, L, t) gives a parent-local span of exactly L centred on the grip root's
        // origin — yMin = -L/2, yMax = +L/2. That is the same shape NormalizeInto hands the seat on
        // the live path (longest axis on +Y, bounds centred), which is why the fixture is a bounds
        // fixture and not a mesh import: the seat reads BOUNDS, and a cube's bounds are exact.
        private sealed class Shaft : IDisposable
        {
            public GameObject GripRoot;
            public GameObject Prop;

            public Shaft(string name, float lengthM)
            {
                GripRoot = new GameObject("EquipmentProp_" + name);
                Prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Prop.name = name;
                Prop.transform.SetParent(GripRoot.transform, false);
                Prop.transform.localScale = new Vector3(ShaftThicknessM, lengthM, ShaftThicknessM);
                Prop.transform.localPosition = Vector3.zero;
                Prop.transform.localRotation = Quaternion.identity;
            }

            /// <summary>
            /// WHERE THE HAND ENDED UP, measured off the SEATED prop: the grip root's origin is the
            /// hand bone, so after a seat the fraction of the shaft below y=0 IS the grip fraction.
            /// This is the assertion that cannot be satisfied by a correct-looking return value.
            /// </summary>
            public bool TryMeasureSeatedGripFraction(out float fraction, out float yMin, out float yMax)
            {
                fraction = 0f;
                yMin = 0f;
                yMax = 0f;
                bool any = false;
                foreach (var r in Prop.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    Bounds wb = r.bounds;
                    Vector3 c = GripRoot.transform.InverseTransformPoint(wb.center);
                    Vector3 e = GripRoot.transform.InverseTransformVector(wb.extents);
                    float lo = c.y - Mathf.Abs(e.y);
                    float hi = c.y + Mathf.Abs(e.y);
                    if (!any) { yMin = lo; yMax = hi; any = true; }
                    else { yMin = Mathf.Min(yMin, lo); yMax = Mathf.Max(yMax, hi); }
                }
                if (!any) return false;
                float len = yMax - yMin;
                if (len < 1e-4f) return false;
                fraction = (0f - yMin) / len;   // the hand sits at the grip root's origin
                return true;
            }

            public void Dispose()
            {
                if (GripRoot != null) UnityEngine.Object.DestroyImmediate(GripRoot);
            }
        }

        // =====================================================================
        //  Case 1 — THE STAFF SEATS ON THE UPPER SHAFT (the owner's ruling)
        // =====================================================================
        //
        // ⭐ THIS CASE IS RED AGAINST THE PRE-WO-1431 TREE BY CONSTRUCTION, and it says so with a
        // measurement rather than a claim: the SAME fixture is run twice through the SAME shipped
        // dispatcher, once with mayDerive=true (the fix) and once with mayDerive=false (which is
        // byte-for-byte the old behaviour — SeatHiltLowerHalf, the only thing the live path ever
        // called). If the two land at the same fraction, the dispatch is not dispatching and this
        // case is measuring nothing; that is asserted too, because a probe that cannot fail is the
        // WO-1138 lesson.
        private static string Case1_StaffSeatsOnTheUpperShaft(List<string> failures)
        {
            using var staff = new Shaft("tripo_staff_a", DeviceMeasuredStaffLengthM);

            var seat = EquipmentController.SeatMeleeGripPoint(
                staff.Prop, staff.GripRoot.transform, WeaponArchetype.Staff,
                mayDerive: true, subject: "staff-grip-seat fixture");

            if (!seat.Measured)
            {
                failures.Add("[staff-seats-upper-shaft] the shipped dispatcher reported NOTHING " +
                             "measured on a " + DeviceMeasuredStaffLengthM.ToString("0.####") +
                             "m shaft with a live MeshRenderer. Either TryLocalBounds stopped " +
                             "reading primitive meshes or the staff branch was removed - the seat " +
                             "cannot be asserted at all until that is fixed.");
                return "";
            }

            if (seat.Rule != "STAFF-DERIVED-UPPER-SHAFT")
                failures.Add("[staff-seats-upper-shaft] a STAFF was seated by rule '" + seat.Rule +
                             "', not the owner-ruled derivation. WO-1431: the staff grip must come " +
                             "from WeaponOrientHelper.TryDeriveStaffGripY (fraction " +
                             WeaponOrientHelper.StaffGripFractionUpLongAxis.ToString("0.##") +
                             "), not from the bladed hilt-lower-half rule. If this fires, the live " +
                             "path has been routed back to SeatHiltLowerHalf and the owner's " +
                             "\"they grasp it 75% on the lower half\" is back.");

            if (!staff.TryMeasureSeatedGripFraction(out float measured, out float yMin, out float yMax))
            {
                failures.Add("[staff-seats-upper-shaft] the SEATED prop has no measurable bounds - " +
                             "the oracle cannot see where the hand landed.");
                return "";
            }

            float want = WeaponOrientHelper.StaffGripFractionUpLongAxis;
            if (Mathf.Abs(measured - want) > 0.02f)
                failures.Add("[staff-seats-upper-shaft] THE HAND IS NOT ON THE UPPER SHAFT: the " +
                             "seated staff puts the grip root's origin at " +
                             measured.ToString("0.###") + " up the long axis (want " +
                             want.ToString("0.##") + " +/-0.02). Measured off the SEATED prop: " +
                             "yMin=" + yMin.ToString("0.####") + " yMax=" + yMax.ToString("0.####") +
                             " span=" + (yMax - yMin).ToString("0.####") + "m. Owner ruling " +
                             "2026-08-19 (\"you go three quarters of the way up Y, and that can be " +
                             "where the hand is attached\") and the 2026-09-06 bounce (\"they grasp " +
                             "it 75% on the lower half\"). A reading near 0.18 means " +
                             "SeatHiltLowerHalf seated it - the exact WO-1431 defect. FIX THE SEAT, " +
                             "NOT THIS ORACLE.");

            if (measured <= 0.5f)
                failures.Add("[staff-seats-upper-shaft] the grip is on the LOWER half (" +
                             measured.ToString("0.###") + " up the shaft). This is the owner's " +
                             "reported defect stated as a number: SeatHiltLowerHalf's crossguard " +
                             "scan terminates at the midpoint (`if (by > mid) break;`), so any " +
                             "value at or below 0.50 is the bladed rule applied to a staff.");

            // SECOND OPINION: the number the DEVICE trace will print must describe the mesh that
            // actually moved. If the returned struct and the re-measured prop disagree, the
            // "MELEE GRIP APPLIED" line the owner is asked to look for is not evidence.
            if (Mathf.Abs(seat.GripFraction - measured) > 0.02f)
                failures.Add("[staff-seats-upper-shaft] THE TRACE LIES ABOUT THE MESH: the shipped " +
                             "dispatcher reports fraction=" + seat.GripFraction.ToString("0.###") +
                             " but the SEATED prop measures " + measured.ToString("0.###") + ". The " +
                             "reported value and the applied shift are computed from different " +
                             "bounds readings; the device capture line stops being usable evidence. " +
                             "(WO-1431 fed both from WeaponOrientHelper's own measurement for " +
                             "exactly this reason - see the 6-arg TryDeriveStaffGripY overload.)");

            // THE RED-FIRST DIFFERENTIAL, run rather than asserted in prose. mayDerive:false is the
            // pre-WO-1431 live path, unchanged, and it MUST land low.
            using var legacy = new Shaft("tripo_staff_a_legacy", DeviceMeasuredStaffLengthM);
            var legacySeat = EquipmentController.SeatMeleeGripPoint(
                legacy.Prop, legacy.GripRoot.transform, WeaponArchetype.Staff,
                mayDerive: false, subject: "staff-grip-seat legacy differential");
            // ⛔ The bool is CHECKED, not discarded. An unmeasurable legacy fixture would leave
            // legacyFraction at 0, and 0 satisfies both differential asserts below - the probe would
            // certify the fix while measuring nothing, which is the very failure shape this block
            // exists to rule out (WO-1138).
            if (!legacy.TryMeasureSeatedGripFraction(out float legacyFraction, out _, out _))
            {
                failures.Add("[staff-seats-upper-shaft] the LEGACY differential fixture has no " +
                             "measurable bounds, so the red-first probe proves nothing this run. " +
                             "Its fraction would default to 0 and silently satisfy both asserts " +
                             "below. Fix the fixture before trusting this case.");
                return "";
            }

            if (legacySeat.Rule != "HILT-LOWER-HALF" || legacyFraction > 0.5f)
                failures.Add("[staff-seats-upper-shaft] THE DIFFERENTIAL PROBE IS BROKEN: the " +
                             "pre-WO-1431 path (rule='" + legacySeat.Rule + "', fraction=" +
                             legacyFraction.ToString("0.###") + ") no longer lands on the lower " +
                             "half, so this case would pass even if the fix were reverted. The " +
                             "whole suite is worthless until the probe reddens against the old " +
                             "behaviour again. Fix the probe, not the game.");

            if (Mathf.Abs(measured - legacyFraction) < 0.2f)
                failures.Add("[staff-seats-upper-shaft] the derived seat (" +
                             measured.ToString("0.###") + ") and the legacy seat (" +
                             legacyFraction.ToString("0.###") + ") land in the same place - the " +
                             "archetype dispatch is not dispatching and this case is measuring " +
                             "nothing.");

            return "staff seated at " + measured.ToString("0.###") + " up a " +
                   (yMax - yMin).ToString("0.####") + "m shaft (legacy path lands at " +
                   legacyFraction.ToString("0.###") + ")";
        }

        // =====================================================================
        //  Case 2 — THE BLADED FAMILIES DO NOT MOVE
        // =====================================================================
        //
        // docs/WEAPON_ARMOR_ORIENT_LOGIC.md, in its own words: "a staff repair must not rotate
        // every melee family to fix one." The sword's hilt-lower-half seat is felt-verified; this
        // case fails the moment a future staff change generalises itself across melee.
        private static void Case2_SwordKeepsTheLowerHilt(List<string> failures)
        {
            using var sword = new Shaft("sword_A", SwordLengthM);

            var seat = EquipmentController.SeatMeleeGripPoint(
                sword.Prop, sword.GripRoot.transform, WeaponArchetype.Sword,
                mayDerive: true, subject: "sword-grip-seat fixture");

            if (seat.Rule != "HILT-LOWER-HALF")
                failures.Add("[sword-keeps-lower-hilt] a SWORD was seated by rule '" + seat.Rule +
                             "'. The bladed archetype grips at the hilt, on the LOWER half (owner " +
                             "2026-08-19: \"you find the edge that is NOT sharp, and you go up to " +
                             "the hilt\"), and it is felt-verified. WO-1431 changes the STAFF only.");

            if (!sword.TryMeasureSeatedGripFraction(out float measured, out _, out _))
            {
                failures.Add("[sword-keeps-lower-hilt] the seated sword has no measurable bounds.");
                return;
            }

            if (measured >= 0.5f)
                failures.Add("[sword-keeps-lower-hilt] THE SWORD GRIP MOVED TO THE UPPER HALF (" +
                             measured.ToString("0.###") + " up the blade line) - the hero is now " +
                             "holding the sword by the blade. A staff fix has generalised itself " +
                             "across the melee families; scope it back to WeaponArchetype.Staff.");

            // The archetype default is ~0.18; assert the shape, not a re-typed constant, so an
            // owner re-dial of the hilt rule does not redden a staff suite.
            if (measured > 0.35f)
                failures.Add("[sword-keeps-lower-hilt] the sword grip drifted up to " +
                             measured.ToString("0.###") + " of the blade line. The hilt sits near " +
                             "the pommel (~0.18 by the WO-577 rule); anything this high is a " +
                             "regression in SeatHiltLowerHalf, not a staff change.");
        }

        // =====================================================================
        //  Case 3 — PRECEDENCE: AN OWNER-DIALLED SEAT STILL OUTRANKS THE DERIVATION
        // =====================================================================
        //
        // docs/WEAPON_ARMOR_ORIENT_LOGIC.md, BINDING: "Manual corrections are canon and must never
        // be overwritten by the auto heuristic", and the ladder authored offset -> manual ->
        // derived -> archetype default (WeaponOrientHelper.ResolveSource). WO-1431 adds a NEW
        // consumer of the derived tier, so the tier above it is re-asserted here at the point of
        // application - the ladder being correct in ResolveSource proves nothing if the new call
        // site ignores it.
        private static void Case3_AuthoredOrManualSeatStillWins(List<string> failures)
        {
            // An authored row or a SUBSTANTIATED manual flag both arrive at the seat as
            // mayDerive=false. The staff must then take the untouched pre-WO-1431 path.
            using var dialled = new Shaft("tripo_staff_a_dialled", DeviceMeasuredStaffLengthM);
            var seat = EquipmentController.SeatMeleeGripPoint(
                dialled.Prop, dialled.GripRoot.transform, WeaponArchetype.Staff,
                mayDerive: false, subject: "staff precedence fixture");

            if (seat.Rule != "HILT-LOWER-HALF")
                failures.Add("[precedence-still-wins] a staff whose row is authored or " +
                             "substantiated-manual was seated by rule '" + seat.Rule + "'. The " +
                             "derived tier must NOT touch it (docs/WEAPON_ARMOR_ORIENT_LOGIC.md: " +
                             "\"never overwrite a manual=true correction\"; the structure side paid " +
                             "this bill once already on 2026-08-18 when an axis-bake zeroed " +
                             "corrections it believed redundant and the town lay down).");

            // And the ladder itself, at the two inputs WO-1431 actually feeds it.
            if (WeaponOrientHelper.ResolveSource(hasAuthoredOffset: true, manual: false, canDerive: true)
                != SeatSource.AuthoredOffset)
                failures.Add("[precedence-still-wins] an authored Offset Forge row no longer " +
                             "outranks the derived seat in ResolveSource.");
            if (WeaponOrientHelper.ResolveSource(hasAuthoredOffset: false, manual: true, canDerive: true)
                != SeatSource.Manual)
                failures.Add("[precedence-still-wins] a substantiated manual flag no longer " +
                             "outranks the derived seat in ResolveSource.");
            if (!WeaponOrientHelper.MayDerive(hasAuthoredOffset: false, manual: false))
                failures.Add("[precedence-still-wins] MayDerive now refuses an unauthored, " +
                             "non-manual row - the WO-1431 staff derivation could never fire.");
        }

        // =====================================================================
        //  Case 4 — THE DISPATCH IS FAMILY-EXACT
        // =====================================================================
        //
        // The live call site classifies with WeaponOrientHelper.Classify(vis.kind.ToString(), mesh).
        // Two things must hold or the fix lands on the wrong props: WeaponClass.Staff must reach the
        // Staff archetype even when the mesh is not literally named "staff", and the families the
        // owner has NOT ruled on must not be dragged in. Per the 2026-08-19 spec, wand / axe /
        // hammer / mace / crossbow classify Unknown and DERIVE NOTHING - "Ask the owner; do not
        // guess." (GearSeat.Classify deliberately folds wand into Staff for MOUNT purposes; the
        // grip dispatch must not inherit that, which is why the live path calls the helper's
        // classifier and not GearSeat's.)
        private static void Case4_ArchetypeDispatchIsFamilyExact(List<string> failures)
        {
            if (WeaponOrientHelper.Classify("staff", "tripo_staff_a") != WeaponArchetype.Staff)
                failures.Add("[archetype-dispatch] category 'staff' no longer classifies as Staff - " +
                             "the live staff rows (category=\"staff\" in weapons.json) would take " +
                             "the bladed seat.");

            if (WeaponOrientHelper.Classify("Staff", "Heroes_Props_Weapons_Unnamed") != WeaponArchetype.Staff)
                failures.Add("[archetype-dispatch] a staff whose MESH is not named 'staff' no " +
                             "longer classifies as Staff. The live call site passes " +
                             "vis.kind.ToString() as the category for exactly this case; if the " +
                             "kind stops answering, every unluckily-named staff silently falls back " +
                             "to the sword rule - the same latent-defect shape WO-1431 §6 flagged.");

            if (WeaponOrientHelper.Classify("wand", "wand_A") == WeaponArchetype.Staff)
                failures.Add("[archetype-dispatch] a WAND now classifies as Staff and would take " +
                             "the 0.75 grip. The owner's 2026-08-19 spec covers bow/sword/staff/" +
                             "shield only; wand/axe/hammer/mace/crossbow are Unknown and derive " +
                             "nothing (\"Ask the owner; do not guess\"). A wand is a baton, not a " +
                             "quarterstaff - gripping one three quarters up is a new defect.");

            if (WeaponOrientHelper.Classify("axe", "axe_A") == WeaponArchetype.Staff)
                failures.Add("[archetype-dispatch] an AXE now classifies as Staff.");

            // A prop the dispatcher cannot classify must still be SEATED - Unknown means "keep
            // today's behaviour", never "leave it at identity".
            using var unknown = new Shaft("mystery_prop", SwordLengthM);
            var seat = EquipmentController.SeatMeleeGripPoint(
                unknown.Prop, unknown.GripRoot.transform, WeaponArchetype.Unknown,
                mayDerive: true, subject: "unknown archetype fixture");
            if (seat.Rule != "HILT-LOWER-HALF" || !seat.Measured)
                failures.Add("[archetype-dispatch] an UNKNOWN archetype was not seated by the " +
                             "existing hilt rule (rule='" + seat.Rule + "', measured=" +
                             seat.Measured + "). Unknown must keep today's behaviour and say so - " +
                             "leaving a prop unseated at identity is how the shield ended up as a " +
                             "flat slab through the hero's chest (WO-1215).");
        }

        // =====================================================================
        //  Case 5 — THE FIX ACTUALLY REACHES THE SHIPPED STAVES
        // =====================================================================
        //
        // A correct derivation that the precedence ladder vetoes on every live row is a fix that
        // never fires. This case reads the SHIPPED catalog + the SHIPPED Offset Forge registry and
        // asserts that at least one staff row is derivable - i.e. it has no authored offsets.json
        // row and its `manual: true` is DEMOTED by WO-1215's substantiation test (all four
        // tripo_staff_* rows are generated:true + manual:true with no authored seat, which is the
        // "names a correction that does not exist" case).
        //
        // It asserts a COUNT of zero derivable staves as the failure, never a specific row id: the
        // owner may dial a staff in the Seating Editor at any time, which legitimately moves that
        // row out of the derived tier. Pinning an id here would turn her own dial into a red gate.
        private static string Case5_LiveStaffRowsAreDerivable(List<string> failures)
        {
            var all = GearCatalog.AllWeapons();
            if (all == null || all.Count == 0)
            {
                failures.Add("[live-staff-rows-derivable] the weapon catalog loaded EMPTY - this " +
                             "case cannot tell whether the staff fix reaches anything.");
                return "";
            }

            int staffRows = 0, derivable = 0, authored = 0, manualHeld = 0;
            var derivableIds = new List<string>();
            foreach (var w in all)
            {
                if (w == null) continue;
                if (WeaponOrientHelper.Classify(w.category, w.id) != WeaponArchetype.Staff) continue;
                staffRows++;

                string meshKey = MeshKeyOf(w);
                bool hasOffset = (!string.IsNullOrEmpty(meshKey) &&
                                  AttachmentOffsetRegistry.TryGetOffset(meshKey, out _)) ||
                                 AttachmentOffsetRegistry.TryGetOffset(w.id, out _);
                bool substantiated = WeaponOrientHelper.ManualSeatIsSubstantiated(
                    w.manual, w.generated, hasOffset);

                if (hasOffset) authored++;
                else if (substantiated) manualHeld++;

                if (WeaponOrientHelper.MayDerive(hasOffset, substantiated))
                {
                    derivable++;
                    if (derivableIds.Count < 6) derivableIds.Add(w.id);
                }
            }

            if (staffRows == 0)
            {
                failures.Add("[live-staff-rows-derivable] the shipped catalog contains NO staff " +
                             "row at all (checked " + all.Count + " weapons via " +
                             "WeaponOrientHelper.Classify). Either the mage's staves were renamed " +
                             "out of the 'staff' category or the catalog did not load - WO-1431 " +
                             "would be fixing nothing either way.");
                return "";
            }

            if (derivable == 0)
                failures.Add("[live-staff-rows-derivable] all " + staffRows + " staff rows are " +
                             "vetoed out of the derived tier (" + authored + " authored, " +
                             manualHeld + " substantiated-manual), so the WO-1431 upper-shaft grip " +
                             "can never fire on a shipped staff. Measured 2026-09-09: offsets.json " +
                             "held 26 rows and NONE was a staff, and every tripo_staff_* row was " +
                             "generated:true + manual:true -> DEMOTED by ManualSeatIsSubstantiated " +
                             "-> derivable. If that changed, either a staff was hand-dialled (fine " +
                             "- then this fix is moot for it and the ticket needs re-reading) or " +
                             "the substantiation test regressed (not fine - see WO-1215).");

            return staffRows + " staff rows: " + derivable + " derivable" +
                   (derivableIds.Count > 0 ? " (" + string.Join(", ", derivableIds) + ")" : "") +
                   ", " + authored + " authored, " + manualHeld + " manual-held";
        }

        /// <summary>
        /// The Offset Forge key the attach path would use: the mesh name, which for these rows is
        /// the last segment of prefabPath (e.g. "Heroes/Props/Weapons/staff_A" -> "staff_A").
        /// EquipmentController resolves it from WeaponVisual.mesh, which is not reachable without a
        /// live equip; the prefab leaf is the same string and is checked ALONGSIDE the id, never
        /// instead of it, so a miss here cannot fake a derivable row.
        /// </summary>
        private static string MeshKeyOf(WeaponDef w)
        {
            if (w == null || string.IsNullOrEmpty(w.prefabPath)) return null;
            int slash = w.prefabPath.LastIndexOf('/');
            return slash >= 0 && slash + 1 < w.prefabPath.Length
                ? w.prefabPath.Substring(slash + 1)
                : w.prefabPath;
        }
    }
}
