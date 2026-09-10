// =============================================================================
// TroopGearApplier — seats weapon / offhand meshes on a troop body (WO-troop-gear).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village
//
// TroopFactory skins the body only; this step attaches optional gear so a
// Spearman does not look identical to a bare Footman. Prefers Resources paths
// (TroopGear/* mirrored from Supercyan by SupercyanResourceWire). Falls back to
// a thin primitive prop when a path is missing so combat still reads as armed.
//
// Bones: Humanoid RightHand for main weapon, LeftHand for offhand/shield/bow.
// Grip transforms are coarse defaults for 1.8 m humanoids — tune via def fields
// later if needed. Colliders on gear are stripped (visual only).
//
// ⛔ EXCEPT THE SHIELD (WO-1616, 2026-09-09). The off-hand is NOT seated by the coarse
// defaults below: it goes through the game's ONE shield-seating authority,
// EquipmentController.SeatShieldMountRotation / SeatShieldPlateOnSocket — the same
// measured derivation the hero uses. Do not add a shield case back into
// ApplyDefaultGrip, and do not author a troop offsets table: that split is the defect.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>Attaches troop weapon/offhand visuals after the body is skinned.</summary>
    public static class TroopGearApplier
    {
        public static void Apply(GameObject visualRoot, TroopDef def)
        {
            if (visualRoot == null || def == null) return;

            string weapon = def.Weapon;
            string offhand = def.Offhand;
            if (string.IsNullOrEmpty(weapon) && string.IsNullOrEmpty(offhand)) return;

            var anim = visualRoot.GetComponentInChildren<Animator>(true);
            if (anim == null || !anim.isHuman)
            {
                FlowTrace.Warn("TroopGear",
                    $"id={def.Id}: no humanoid Animator — gear skipped " +
                    $"(weapon='{weapon ?? ""}' offhand='{offhand ?? ""}').");
                return;
            }

            if (!string.IsNullOrEmpty(weapon))
            {
                bool bow = weapon.IndexOf("bow", System.StringComparison.OrdinalIgnoreCase) >= 0
                           || weapon.IndexOf("Bow", System.StringComparison.OrdinalIgnoreCase) >= 0;
                HumanBodyBones bone = bow ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                Attach(visualRoot, anim, bone, weapon, isOffhand: false, isBow: bow, troopId: def.Id);
            }

            if (!string.IsNullOrEmpty(offhand))
            {
                Attach(visualRoot, anim, HumanBodyBones.LeftHand, offhand,
                    isOffhand: true, isBow: false, troopId: def.Id);
            }
        }

        private static void Attach(GameObject visualRoot, Animator anim, HumanBodyBones boneId,
            string resourcesPath, bool isOffhand, bool isBow, string troopId)
        {
            Transform hand = null;
            try { hand = anim.GetBoneTransform(boneId); }
            catch { /* invalid avatar */ }

            if (hand == null)
            {
                FlowTrace.Warn("TroopGear",
                    $"id={troopId}: bone {boneId} missing — cannot attach '{resourcesPath}'.");
                return;
            }

            // Strip prior gear on this hand (reconfigure / re-skin).
            for (int i = hand.childCount - 1; i >= 0; i--)
            {
                var ch = hand.GetChild(i);
                if (ch != null && ch.name.StartsWith("TroopGear_", System.StringComparison.Ordinal))
                    Object.Destroy(ch.gameObject);
            }

            GameObject instance = null;
            var prefab = Resources.Load<GameObject>(resourcesPath);
            if (prefab != null)
            {
                instance = Object.Instantiate(prefab, hand, false);
                instance.name = "TroopGear_" + prefab.name;
            }
            else
            {
                instance = BuildPrimitiveFallback(resourcesPath, isOffhand, isBow);
                instance.transform.SetParent(hand, false);
                FlowTrace.Warn("TroopGear",
                    $"id={troopId}: Resources '{resourcesPath}' missing — primitive fallback.");
            }

            // Strip colliders — troops use the body capsule only.
            foreach (var c in instance.GetComponentsInChildren<Collider>(true))
                Object.Destroy(c);

            // WO-1616: a SHIELD is seated by the game's ONE shield-seating authority; everything
            // else keeps the coarse per-family grips below. The predicate is the same one
            // ApplyDefaultGrip used to test, moved out so the shield never reaches that method.
            bool isShield = !isBow &&
                            (isOffhand ||
                             resourcesPath.IndexOf("shield", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (isShield)
                SeatShield(instance, hand, anim, troopId, resourcesPath);
            else
                ApplyDefaultGrip(instance.transform, isOffhand, isBow, resourcesPath);

            FlowTrace.Step("TroopGear",
                $"id={troopId}: attached '{resourcesPath}' on {boneId} " +
                $"(src={(prefab != null ? "Resources" : "primitive")}).");
        }

        // =====================================================================================
        //  WO-1616 — the NPC shield reads the HERO's seat, because there is only one
        // =====================================================================================
        //
        // WHAT THIS REPLACES: the deleted `else if (shield)` branch of ApplyDefaultGrip wrote one
        // hard-coded triple — a few centimetres out from the bone, a flat 90-degree yaw, scale
        // forced to one. (The exact numbers are quoted in WORK_ORDER_1616_*.md §1 and its RESULT,
        // and are deliberately NOT repeated here: the ticket's acceptance is that this file no
        // longer contains them in any form, and a "documentation" copy is how a deleted constant
        // gets pasted back.)
        // One triple, applied to every off-hand on every rig, with no per-rig, per-mesh or
        // per-shield term — which is why every deployed raid NPC wore a visibly wrong shield for a
        // whole raid while the hero, five metres away, wore a correct one. The correct seat was
        // never missing; this path simply could not reach it.
        //
        // ⛔ THE CONSTANT IS DELETED, NOT RE-DIALLED. WO-1616 §4: "Never add a second offset table."
        // A per-troop offsets file, or a better-looking triple, is two authorities that drift apart
        // the moment a new rig or a new shield mesh arrives (CLAUDE.md §2/§5/§16 each tell this same
        // story about a different copy). Re-dialling by eye would have shipped the SAME bug with
        // nicer numbers.
        //
        // ⚠ A GRIP ROOT IS REQUIRED, NOT COSMETIC. The measured ShieldFrame is expressed in the
        // PARENT's frame, and the seat then writes a rotation onto that parent. If the prop were
        // both the measured subject and the rotated transform, rotating it would invalidate the very
        // measurement it was derived from. The hero has always had this two-level shape
        // (gripRoot -> prop); this gives the NPC the same one. The root carries the `TroopGear_`
        // prefix so the existing re-skin cleanup in Attach() still destroys it (and, with it, the
        // prop beneath).
        //
        // ⚠ SCALE IS NO LONGER FORCED TO 1. The deleted branch overwrote the prefab root's scale;
        // the hero's path leaves a native prop's pivot and scale untouched and derives ROTATION
        // only. If the troop shield now reads a different SIZE on device, that is this line, and it
        // is deliberate — name it in the felt-test rather than re-adding a scale write.
        private static void SeatShield(GameObject instance, Transform hand, Animator anim,
                                       string troopId, string path)
        {
            if (instance == null || hand == null) return;

            var gripRoot = new GameObject("TroopGear_ShieldGrip");
            gripRoot.transform.SetParent(hand, false);
            gripRoot.transform.localPosition = Vector3.zero;
            gripRoot.transform.localRotation = Quaternion.identity;
            gripRoot.transform.localScale = Vector3.one;
            // worldPositionStays:false — the root is identity under the same bone, so the prop's
            // authored local transform is carried across byte-for-byte.
            instance.transform.SetParent(gripRoot.transform, false);

            Transform body = anim != null ? anim.transform : hand.root;

            // ── POSE-SETTLE BEFORE MEASURING THE ARM (WO-1616) ──────────────────────────────────
            // TroopFactory.Build calls Apply() SYNCHRONOUSLY, one line after ApplyTroopAnimator —
            // so the Animator has been BOUND but has never EVALUATED: the rig is standing in its
            // BIND POSE. That matters because GearSeat.GetShieldAxes derives `outward` by projecting
            // the body's left onto the plane perpendicular to the forearm, and in a T-pose the
            // forearm IS the body's left: the projection collapses to its documented fallback and
            // the derived seat is taken against axes that no animated pose ever shows. The shield's
            // local rotation is then rigid on the bone, so the error travels into every frame after.
            // (GearSeat.SnapHandleToSocket names the same degenerate case in its own doc: "when the
            // forearm is parallel to the shoulders the heater is posed upright".) The hero is immune
            // for a reason that does not apply here — it equips while already posed.
            //
            // Update(0f) advances the state machine by zero and WRITES THE ENTRY STATE ONTO THE
            // BONES. It is a no-op when no controller is bound, so it cannot make an un-animated
            // troop worse. ⚠ UNPROVEN ON DEVICE — the arm angle is printed in the seat line below
            // precisely so a bounce can be settled from the log instead of re-theorised.
            if (anim != null && anim.isHuman && anim.runtimeAnimatorController != null)
                Guard.Try("TroopGear", "pose-settle before shield seat", () => anim.Update(0f));

            SeatShieldOnHand(instance, gripRoot.transform, hand, anim, body,
                             troopId + " '" + path + "'");
        }

        /// <summary>
        /// WO-1616 §12 instrument. How far the off-hand FOREARM is from the body's own left/right
        /// axis at the moment the seat is derived. Near 0 or 180 means GetShieldAxes' projection is
        /// degenerate (a bind/T pose) and the derived seat was taken against fallback axes — read
        /// this before re-theorising a wrong-looking NPC shield.
        /// </summary>
        private static string DescribeOffHandArm(Animator anim, Transform body)
        {
            if (anim == null || !anim.isHuman || body == null) return "armVsBodyRight=n/a(no rig)";
            Transform forearm = null, wrist = null;
            try
            {
                forearm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm)
                          ?? anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                wrist = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            }
            catch { /* invalid avatar */ }
            if (forearm == null) return "armVsBodyRight=n/a(no forearm bone)";

            Vector3 alongArm = wrist != null ? (wrist.position - forearm.position) : forearm.up;
            if (alongArm.sqrMagnitude < 1e-8f) return "armVsBodyRight=n/a(zero-length arm)";
            alongArm.Normalize();
            float vsRight = Vector3.Angle(alongArm, body.right);
            float fromAxis = Mathf.Min(vsRight, 180f - vsRight);
            return $"armVsBodyRight={vsRight:0.#}deg" +
                   (fromAxis < 15f
                       ? " ⚠ DEGENERATE (arm lies along the body's left/right axis, so GetShieldAxes' " +
                         "outward projection collapsed to its fallback — a bind/T pose)"
                       : " (projection well-conditioned)");
        }

        /// <summary>
        /// WO-1616. Seats an already-parented shield prop through the ONE authority
        /// (<see cref="EquipmentController.SeatShieldMountRotation"/> +
        /// <see cref="EquipmentController.SeatShieldPlateOnSocket"/>) and emits the device line.
        /// Public static so <c>TroopShieldSeatRegression</c> asserts the SHIPPED call, not a copy of
        /// it — a suite that re-typed these two calls would stay green while this path regressed.
        /// <para>
        /// NOTE — there is deliberately no precedence input. `mayDerive` is unconditionally TRUE
        /// here because the troop path has NO authored-offset channel and NO `manual` flag:
        /// `TroopDef.Offhand` is a bare Resources path, not a catalog row, so there is no
        /// owner-dialled seat that derivation could overrule. If troop gear ever gains authored
        /// seats, feed that verdict in here — do not add a second ladder.
        /// </para>
        /// </summary>
        public static EquipmentController.ShieldSeat SeatShieldOnHand(
            GameObject prop, Transform gripRoot, Transform hand, Animator anim, Transform body,
            string subject)
        {
            var seat = EquipmentController.SeatShieldMountRotation(
                prop, gripRoot, hand, anim, body, mayDerive: true, subject: subject);

            if (seat.Derived)
                EquipmentController.SeatShieldPlateOnSocket(gripRoot, hand, anim, body, seat.Frame, subject);

            // ── §12 PROVING LINE ────────────────────────────────────────────────────────────────
            // The five "attached 'TroopGear/Shield' on LeftHand" lines the RCA quotes are NOT proof
            // of anything: they printed identically while the constant was seating the shield wrong.
            // THIS line names the rule, the authority and the resulting transform, so a bad seat can
            // be split into "the derivation never ran" (rule=SHIELD-NOT-DERIVED, with the
            // ShieldFrame Warn above saying which clause failed) vs "it ran and is wrong"
            // (rule=SHIELD-DERIVED-…, with the numbers to argue about).
            FlowTrace.Step("TroopGear",
                $"SHIELD SEAT APPLIED {subject}: rule={seat.Rule} " +
                "authority=EquipmentController.SeatShieldMountRotation " +
                $"frameValid={seat.FrameValid} derived={seat.Derived} " +
                $"lPos={gripRoot.localPosition} lEuler={gripRoot.localEulerAngles:0.#} " +
                $"lScale={gripRoot.localScale} {DescribeOffHandArm(anim, body)} " +
                "— WO-1616: this used to be one hard-coded triple, identical on every rig and " +
                "every shield mesh.");
            return seat;
        }

        private static void ApplyDefaultGrip(Transform t, bool isOffhand, bool isBow, string path)
        {
            // Coarse grips for ~1.8 m Supercyan / Tripo humanoids.
            bool spear = path.IndexOf("spear", System.StringComparison.OrdinalIgnoreCase) >= 0
                         || path.IndexOf("Spear", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool staff = path.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0
                         || path.IndexOf("Staff", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool axe = path.IndexOf("axe", System.StringComparison.OrdinalIgnoreCase) >= 0
                       || path.IndexOf("Axe", System.StringComparison.OrdinalIgnoreCase) >= 0;

            // ⛔ NO SHIELD BRANCH LIVES HERE ANY MORE (WO-1616). It held one hard-coded triple for
            // every off-hand on every rig; it is DELETED, not moved and not re-dialled, and the
            // shield is routed to the game's one shield-seating authority in Attach() before this
            // method is ever called. If you are here to "fix the NPC shield", you are in the wrong
            // method — read EquipmentController.SeatShieldMountRotation. Re-adding a triple here
            // recreates the exact defect (two authorities, the NPC's wrong) and reddens
            // TroopShieldSeatRegression case `npc-shield-uses-the-shared-authority`.

            if (isBow)
            {
                // ⚠ KNOWN GAP, DELIBERATELY NOT CHANGED HERE (2026-08-16). The owner's canonical bow
                // rule is UNIVERSAL - "all players and enemies follow this rule" - and every OTHER
                // bow path now DERIVES its seat from the rig via
                // DeNelle.Core.Geometry.WeaponBoundsOrient.ComputeBowHeldRotation (read its header:
                // it carries her four-clause rule verbatim). The hero + enemy archers get it through
                // HeroBowAttachment; companions and non-ranger classes through
                // EquipmentController.AttachLoadedProp's bow branch, drawn AND sheathed.
                //
                // THIS line is the last dialed constant, and the Euler below is exactly the kind of
                // one-axis guess the rule rejects: it can only pitch the prop, so it has no answer
                // for which way the BELLY faces (clauses 2 and 4 - string parallel to and nearest
                // the person, hand on the curved edge furthest from the person). It is left alone
                // tonight for a REASON, not an oversight: TroopGearApplier instantiates the troop
                // prefab RAW - it never runs NormalizeInto - so prop-local +Y is not guaranteed to
                // be the limb span and the derivation's premise does not hold here. Routing this
                // through it means first normalising every troop prop, which re-sizes ALL troop
                // gear (this method also seats spears, staves, axes, shields off the same path).
                // That is its own lane with its own felt-check, not a rider on the bow fix.
                t.localPosition = new Vector3(0.02f, 0.04f, 0.02f);
                t.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                t.localScale = Vector3.one * 1.0f;
            }
            else if (staff)
            {
                t.localPosition = new Vector3(0.01f, 0.03f, 0f);
                t.localRotation = Quaternion.Euler(0f, 0f, 90f);
                t.localScale = Vector3.one * 1.0f;
            }
            else if (spear)
            {
                t.localPosition = new Vector3(0.01f, 0.02f, 0f);
                t.localRotation = Quaternion.Euler(0f, 0f, 90f);
                t.localScale = Vector3.one * 1.0f;
            }
            else if (axe)
            {
                t.localPosition = new Vector3(0.02f, 0.03f, 0f);
                t.localRotation = Quaternion.Euler(0f, 0f, 80f);
                t.localScale = Vector3.one * 1.0f;
            }
            else
            {
                // Sword default
                t.localPosition = new Vector3(0f, 0.02f, 0f);
                t.localRotation = Quaternion.Euler(0f, 0f, 90f);
                t.localScale = Vector3.one * 1.0f;
            }
        }

        private static GameObject BuildPrimitiveFallback(string path, bool isOffhand, bool isBow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TroopGear_Fallback";
            bool spear = path != null && path.IndexOf("spear", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool staff = path != null && path.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool shield = isOffhand || (path != null && path.IndexOf("shield", System.StringComparison.OrdinalIgnoreCase) >= 0);

            Vector3 scale;
            Color color;
            if (isBow)
            {
                scale = new Vector3(0.05f, 0.7f, 0.12f);
                color = new Color(0.45f, 0.32f, 0.18f);
            }
            else if (shield)
            {
                scale = new Vector3(0.45f, 0.55f, 0.08f);
                color = new Color(0.55f, 0.55f, 0.6f);
            }
            else if (staff)
            {
                scale = new Vector3(0.04f, 1.2f, 0.04f);
                color = new Color(0.5f, 0.4f, 0.28f);
            }
            else if (spear)
            {
                scale = new Vector3(0.03f, 1.4f, 0.03f);
                color = new Color(0.65f, 0.65f, 0.7f);
            }
            else
            {
                scale = new Vector3(0.04f, 0.7f, 0.04f);
                color = new Color(0.72f, 0.73f, 0.76f);
            }
            go.transform.localScale = scale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                    mr.sharedMaterial = mat;
                }
            }
            return go;
        }
    }
}
