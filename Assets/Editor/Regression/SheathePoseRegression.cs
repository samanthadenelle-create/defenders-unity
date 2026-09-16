// =============================================================================
// SheathePoseRegression — the SHEATHED carry: the SWORD hangs from the HIP, the
// SHIELD rides the off-hand FOREARM, one socket per slot, long axis vertical, and the
// "which end is down" sign is measured PER MESH — never a global constant.
// Marker: SHEATHE_POSE_OK / SHEATHE_POSE_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Standalone entry: DeNelle.Editor.SheathePoseRegression.RunAll (Exits 1 on failure).
//
// THE OWNER'S RULING (2026-08-20, verbatim):
//   "sheathed should sit inverted with the longest mesh (y) up and down attached to
//    hip bone"
// plus, separately, "Shield isnt showing either" and "starter loop shield sword
// placement issues too".
//
// WHY THIS SUITE EXISTS, AND WHY IT ASSERTS *BOTH* DIRECTIONS
// ---------------------------------------------------------------------------
// The owner asked for it in the same breath as the fix: "write a regression test to
// always confirm it too." A one-directional guard is nearly worthless here, because
// every one of the three defects it pins LOOKED FINE IN SOURCE and was only visible
// in a capture:
//
//   1. THE ANCHOR. ResolveBackSocket() asked for HumanBodyBones.Chest. On the live CC
//      rig that resolves to the LOW SPINE — the capture line is
//        "ResolveBackSocket on 'Hero (Blaise)': sheathe anchor under bone 'CC_Base_Spine01'."
//      so the "back" carry was already sitting at the waist. Reading the source, "Chest"
//      looks like the back. Only the bone name in the trace tells the truth.
//
//   2. ONE SOCKET, TWO PROPS. Both the main weapon and the off-hand parented to the
//      SAME transform at the SAME origin:
//        "AttachOffHandProp MEASURED after hold: id='knight_shield_starter'
//         parent='SheatheSocket_Back' state=SHEATHED worldBounds=c(-0.13, 0.81, -5.07)
//         s(0.72, 0.92, 0.72)"
//      The shield was never missing. It rendered a real 0.72 x 0.92 x 0.72 m volume,
//      inside the hero's body, where the player cannot see it. A null-check oracle
//      would have passed this bug forever.
//
//   3. THE DIAGONAL. ComputeSheathRotation leaned the long axis
//      _sheatheBladeDiagonalDeg (28) off vertical — a baldric carry — which on a
//      waist-height anchor reads as a sword lying sideways across the belt
//      (logs/device/sheathed-weapon.png).
//
// So each case below runs the SAME predicate over the shipped state (must PASS) and
// over a tombstone of the KNOWN-BAD state (must FAIL). A rule that cannot fail on the
// bug it was written for is a rule that is not being evaluated, and this project has
// shipped that mistake before.
//
// HOW EACH CASE IS DRIVEN
// ---------------------------------------------------------------------------
//   (a) REAL GEOMETRY, REAL METHOD. Cases G1-G4 build a GameObject, AddComponent the
//       SHIPPED EquipmentController and invoke its private ComputeSheathRotation
//       through reflection. That method reads only `_animator` (null in a headless
//       editor, so it falls back to the component's own transform — which this suite
//       owns and rotates to an awkward angle on purpose) and the socket's rotation.
//       No play session, no Avatar, no re-implementation of the math: a re-implemented
//       copy could pass while the shipped path fails, which is worth nothing.
//       EquipmentController has no [ExecuteAlways], so AddComponent runs no Awake here.
//   (b) REAL HELPER. Case G5 drives WeaponOrientHelper.ComputeShieldMountRotation, the
//       same entry point the sheathed shield pose calls, with a synthetic ShieldFrame.
//   (d) REAL RENDERER, REAL MEASUREMENT. Cases G8/G9 build an actual MeshRenderer plate
//       at the live shield's proportions, run the shipped TryResolveShieldFrame on it at
//       a hostile attach rotation, apply the shipped pose and assert the RENDERED WORLD
//       VOLUME. Added 2026-08-20 second pass, after every angle in this suite read
//       perfect while the shield rendered flat on the owner's device.
//
// ─────────────────────────────────────────────────────────────────────────────
//  THE 2026-08-21 F8 — TWO MORE DEFECTS, AND ONE OF THEM WAS IN THIS SUITE
// ─────────────────────────────────────────────────────────────────────────────
//   4. THE SWORD HANGS UPSIDE DOWN. Owner: "sword upside down (sheathed)".
//      ⛔ AND THE OLD CASE G2 HAD PINNED IT THERE. G2 asserted a GLOBAL DEFAULT for
//      _sheatheLongAxisSign — "ship +1 tip UP" — written on 08-20 after an F8 on
//      Blaise read -1 as upside down. On 08-21 an F8 on the Flameblade read +1 as
//      upside down. Both captures are correct: which end is the tip is a property of
//      the MESH (NormalizeInto puts it at prop +Y; a NATIVE prop keeps the artist's
//      axes), so ANY single global value is wrong for part of the catalogue and
//      flipping it only chooses which hero ships broken. An oracle that pins a global
//      constant here does not guard the pose — it ratifies whichever hero was
//      photographed last. G2/G3 are rebuilt around the PER-MESH derivation
//      (WeaponOrientHelper.TryResolveSheathedTipSign), M1 proves two mirror-image
//      meshes get OPPOSITE signs, M2 lints that the pose actually reads the
//      measurement, and M3 requires every SHIPPED weapon mesh to be able to answer —
//      because a mesh that declines falls back to exactly the global guess that
//      caused this.
//   5. THE SHIELD IS ON THE HIP, NOT THE ARM. Owner: "shield is attaching to hip not
//      wrist or arm" / "the shield on arm or arm bone". Proving line, every frame of
//      the capture:
//        key='ShieldWithItemLogic' ... parent='SheatheSocket_HipOff' parentLossy=(1.67,1.67,1.67)
//      The STOWED pose was the wrong one — the drawn pose was already on the LeftHand
//      bone. A1 pins the off-hand anchor to a LEFT ARM bone (asked for BEFORE any hip,
//      and gated on the slot so the SWORD keeps its 08-20 hip ruling); A2 pins that a
//      DRAWN prop still cannot reach any stow anchor, so a fix to one pose can never
//      quietly move the other.
//
// ⚠ THE SHIELD DOES NOT TAKE THE SWORD'S "INVERTED" RULE (owner ruling scope, 2026-08-20).
// The instruction "sheathed should sit inverted with the longest mesh (y) up and down" is
// about a SWORD - the long axis is the blade, inverted is tip-down in a scabbard. A shield
// has no tip and no meaningful end-for-end. It keeps the felt-approved WO-1123 rule
// (thickness away from the player, handle inward), applied at the new HIP anchor. G1-G4
// assert the sword rule; G5-G9 assert the shield rule; neither is applied to the other.
//   (c) SOURCE LINT for the two facts that are structural rather than numeric (which
//       BONE is asked for, and which VARIABLE each slot parents to). Both lints run on
//       COMMENT-BLANKED source — the tombstone comments in EquipmentController name the
//       retired bone and the retired socket name, and a rule that can match its own
//       tombstone punishes the author for documenting the fix.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Geometry;
using DeNelle.Village;

namespace DeNelle.Editor
{
    /// <summary>
    /// Pins the owner's 2026-08-20 sheathe ruling: hip anchor, one socket per slot,
    /// long axis vertical + inverted. Returns true (summary) / false (detail); never throws.
    /// </summary>
    public static class SheathePoseRegression
    {
        private const string EquipRelPath = "_Modules/Village/Hero/EquipmentController.cs";

        // Angular slack. 2 deg is far tighter than any defect this pins (the retired
        // diagonal was 28 deg and the retired horizontal read ~90) and far looser than
        // float noise through two quaternion compositions.
        private const float AngleTolDeg = 2f;

        // ─────────────────────────────────────────────────────────────────────
        //  TOMBSTONES — the code as it stood BEFORE the ruling. Every predicate is
        //  run over these too and MUST reject them. They are deliberately verbatim
        //  (bone name, socket name, shared variable) rather than paraphrased: a
        //  paraphrase drifts away from the bug until the guard no longer covers it.
        // ─────────────────────────────────────────────────────────────────────
        private const string TombstoneAnchor = @"
        private Transform ResolveBackSocket()
        {
            if (_backSocket != null) return _backSocket;
            if (_animator == null || !_animator.isHuman) return null;
            Transform anchor = _animator.GetBoneTransform(HumanBodyBones.Chest);
            if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.Spine);
            if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (anchor == null) return null;
            var go = new GameObject(NAME);
            go.transform.SetParent(anchor, false);
            _backSocket = go.transform;
            return _backSocket;
        }
";

        private const string TombstoneSharedSocket = @"
        private void ApplyHoldPose()
        {
            bool drawn = _combatActive;
            Transform back = drawn ? null : ResolveBackSocket();
            if (_gripRoot != null)
            {
                if (!drawn && back != null)
                {
                    _gripRoot.SetParent(back, false);
                }
            }
            if (_currentOffHandProp != null)
            {
                var offT = _currentOffHandProp.transform;
                if (!drawn && back != null)
                {
                    offT.SetParent(back, false);
                }
            }
        }
";

        // The off-hand resolver as it stood BEFORE the 2026-08-21 F8: hips-first for BOTH slots, so
        // the shield hung on 'SheatheSocket_HipOff' — the parent the owner's capture printed on
        // every frame while she wrote "shield is attaching to hip not wrist or arm".
        private const string TombstoneHipOnlyOffHand = @"
        private Transform ResolveSheatheSocket(bool offHand)
        {
            Transform cached = offHand ? _sheatheSocketOff : _sheatheSocketMain;
            if (cached != null) return cached;
            if (_animator == null || !_animator.isHuman) return null;
            Transform anchor = _animator.GetBoneTransform(HumanBodyBones.Hips);
            bool onHips = anchor != null;
            if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.Spine);
            if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.Chest);
            if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (anchor == null) return null;
            var go = new GameObject(NAME);
            go.transform.SetParent(anchor, false);
            if (offHand) _sheatheSocketOff = go.transform; else _sheatheSocketMain = go.transform;
            return go.transform;
        }
";

        // The sheathe sign as it stood on the morning of 2026-08-21: ONE serialized number for every
        // weapon in the game. Not a typo and not carelessness — it was deliberate, documented, and
        // wrong, and it was FLIPPED twice in two days chasing two heroes carrying two differently
        // authored meshes. Any future body of ComputeSheathRotation that looks like this must fail.
        private const string TombstoneGlobalSignOnly = @"
        private Quaternion ComputeSheathRotation(Transform socket, float sideSign)
        {
            Transform body = _animator != null ? _animator.transform : transform;
            float sign = _sheatheLongAxisSign >= 0f ? 1f : -1f;
            Vector3 vertical = body.up * sign;
            float rad = _sheatheBladeDiagonalDeg * Mathf.Deg2Rad;
            Vector3 worldBlade = (vertical * Mathf.Cos(rad) + body.right * (-sideSign) * Mathf.Sin(rad)).normalized;
            Vector3 worldFlat = body.right * sideSign;
            Quaternion worldTarget = Quaternion.LookRotation(worldFlat, worldBlade);
            return Quaternion.Inverse(socket.rotation) * worldTarget;
        }
";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- SHEATHE POSE (owner ruling 2026-08-20: hips, one socket per slot, vertical + inverted) ---");

            string equipPath = null;
            try { equipPath = Path.Combine(Application.dataPath, EquipRelPath.Replace('/', Path.DirectorySeparatorChar)); }
            catch { }

            if (string.IsNullOrEmpty(equipPath) || !File.Exists(equipPath))
            {
                // NOT a hollow pass. EquipmentController is the subject of this suite; if it
                // cannot be read there is nothing here to confirm and saying OK would be a lie.
                reason = "Assets/" + EquipRelPath + " not found — the sheathe pose cannot be verified.";
                Debug.LogError(log + "SHEATHE_POSE_FAIL: " + reason);
                return false;
            }

            string raw = ReadOrEmpty(equipPath);
            string codeOnly = BlankComments(raw);              // comments gone, string literals KEPT
            string codeNoStrings = BlankStringLiterals(codeOnly); // comments AND string contents gone

            // ── CASE L1: the sheathe anchor asks for the HIPS bone, first ─────────────
            RunPredicate(failures, log, "L1 anchor=Hips",
                AnchorIsHips, codeNoStrings, TombstoneAnchor,
                "the sheathe socket resolves to HumanBodyBones.Hips before any spine/chest fallback");

            // ── CASE L2: the two slots parent to DIFFERENT sockets ────────────────────
            // Run on the STRING-BLANKED source: ApplyHoldPose is full of interpolated trace
            // messages, and a `{` inside one of those would unbalance the brace scan and hand
            // the predicate half a method to reason about.
            RunPredicate(failures, log, "L2 separate sockets",
                SlotsUseSeparateSockets, codeNoStrings, TombstoneSharedSocket,
                "the main-hand and off-hand sheathed branches parent to two DIFFERENT socket variables");

            // ── CASE L2b: and the two sockets are two NAMED objects ───────────────────
            // Checked against comment-blanked source (string literals KEPT) so it reads the
            // real GameObject names, and so the retired name surviving only in a tombstone
            // comment cannot trip it.
            // ⚠ THE OFF-HAND NAME CHANGED ON 2026-08-21: HipOff -> ArmOff. The owner's F8 —
            // "shield is attaching to hip not wrist or arm" — moved the shield's mount to the
            // off-hand FOREARM, and the anchor's NAME has to move with it. A socket called
            // 'SheatheSocket_HipOff' sitting on an arm bone is the same class of lie as the old
            // 'SheatheSocket_Back' sitting on CC_Base_Spine01: it is how a reader concludes the
            // mount is right when the transform says otherwise.
            int beforeL2b = failures.Count;
            foreach (var socketName in new[] { "SheatheSocket_HipMain", "SheatheSocket_ArmOff" })
                if (codeOnly.IndexOf(socketName, StringComparison.Ordinal) < 0)
                    failures.Add("L2b: no sheathe socket named " + socketName + " is created. Two slots need " +
                                 "two named anchors; one shared anchor is what buried the shield in the body.");
            if (codeOnly.IndexOf("SheatheSocket_Back", StringComparison.Ordinal) >= 0)
                failures.Add("L2b: the shared 'SheatheSocket_Back' anchor is back in the code. It was ONE " +
                             "transform carrying both props at one origin, under a bone that resolved to " +
                             "CC_Base_Spine01 — the reported waist-carry and the invisible shield in one object.");
            if (failures.Count == beforeL2b)
                log.AppendLine("  L2b two named hip sockets, no shared back socket ....... ok");

            // ── CASE L3: the sheathed shield derivation is not gated on the DRAWN row ─
            // (The capture's proving line: "off-hand seat NOT derived ... source=AuthoredOffset
            //  (authoredRow=True ...)" — an authored DRAWN row switched off the SHEATHED
            //  derivation, and not one ShieldFrame line was emitted in the entire session.)
            if (codeNoStrings.IndexOf("_currentOffHandSheathDerivable", StringComparison.Ordinal) < 0)
                failures.Add("L3: EquipmentController has no _currentOffHandSheathDerivable — the sheathed " +
                             "shield pose is gated on the DRAWN pose's precedence again, which is what left " +
                             "the shield posed by a retired back-carry constant");
            else
                log.AppendLine("  L3 sheathed derivability is its own flag ................ ok");

            // ── CASE A1: the OFF-HAND sheathe anchor is an ARM bone, first ────────────
            // (Owner F8 2026-08-21, verbatim: "shield is attaching to hip not wrist or arm".
            //  The proving line was in every frame of the capture:
            //    key='ShieldWithItemLogic' ... parent='SheatheSocket_HipOff')
            RunPredicate(failures, log, "A1 off-hand=arm",
                OffHandAnchorIsArm, codeNoStrings, TombstoneHipOnlyOffHand,
                "the off-hand sheathe socket resolves to a LEFT ARM bone before any hip fallback");

            // ── CASE A2: and the DRAWN shield never reaches a sheathe socket at all ───
            CheckDrawnOffHandGoesToTheHand(failures, log, codeNoStrings);

            // ── CASE S1: the two hip sides are opposite, by construction ──────────────
            CheckSidesAreOpposite(failures, log);
            CheckNativeSwordMainHipAndDrawPreserved(failures, log);

            // ── CASE M1: the sheathed sign is MEASURED off the mesh, both ways ────────
            CheckPerMeshTipSignIsDerived(failures, log);

            // ── CASE M2: and the shipped pose actually consults that measurement ──────
            RunPredicate(failures, log, "M2 pose reads the mesh",
                SheathRotationConsultsPerMeshSign, codeNoStrings, TombstoneGlobalSignOnly,
                "ComputeSheathRotation takes its sign from the per-mesh measurement, with the " +
                "serialized field as the fallback — not from the field alone");

            // ── CASE M3: every SHIPPED weapon mesh can actually answer ────────────────
            CheckShippedWeaponMeshesResolveASign(failures, log);

            // ── CASE M3b: and M3's sign-agnostic branch can REFUSE, not just accept ───
            CheckSignAgnosticVerticalClauseHasTeeth(failures, log);

            // ── CASES G1-G4: the SHIPPED ComputeSheathRotation, driven for real ───────
            CheckShippedRotation(failures, log);

            // ── CASE G5: the shield's sheathed rule, through the real helper ──────────
            CheckShieldMountRule(failures, log);

            // ── CASES G8/G9: the shield's RENDERED SHAPE, on a real MeshRenderer ──────
            // The one that would have caught the flat shield before the owner saw it.
            CheckSheathedShieldRendersAsAPlate(failures, log);

            // ── CASE D1: no SHIPPED absolute @sheathed row can outrank the derivation ─
            CheckNoShippedAbsoluteSheathedRow(failures, log);

            // ── CASE D2: the DEFAULT flags do not un-do the derived pose ─────────────
            CheckSheathedDefaultsDoNotOverrideDerivation(failures, log);

            // ── CASE P1: a grip-at-origin shield is CENTRED on the hip, not hung by it ─
            CheckSheathedOffHandIsCentredOnSocket(failures, log);

            if (failures.Count == 0)
            {
                reason = "sheathe pose: SWORD on the hip anchor, vertical, and hung tip-down by a sign " +
                         "MEASURED off each mesh (never one global constant); SHIELD on the off-hand " +
                         "FOREARM, face-outward, still rendering as a plate, and still seating in the HAND " +
                         "when drawn — every rule proven to REJECT the pre-2026-08-20 back carry, the " +
                         "flat-shield state, the 2026-08-21 hip-mounted shield, and the global-sign state " +
                         "that made 'upside down' true for one hero and false for the other.";
                Debug.Log(log + "SHEATHE_POSE_OK - " + reason);
                return true;
            }

            reason = string.Join(" | ", failures.ToArray());
            Debug.LogError(log + "SHEATHE_POSE_FAIL: " + reason);
            return false;
        }

        /// <summary>Standalone entry point (run-unity-method).</summary>
        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log(reason);
            if (!ok) EditorApplication.Exit(1);
        }

        // =====================================================================
        //  BOTH-DIRECTIONS HARNESS
        // =====================================================================

        private delegate bool SourcePredicate(string source, out string why);

        /// <summary>
        /// Runs one predicate over the SHIPPED source (must pass) and over the tombstone of
        /// the state it was written to catch (must fail). The second half is not ceremony:
        /// it is the only thing that distinguishes a rule that holds from a rule that cannot
        /// fire — and every defect in this suite passed a source read before it was captured.
        /// </summary>
        private static void RunPredicate(List<string> failures, StringBuilder log, string label,
                                         SourcePredicate predicate, string shipped, string tombstone,
                                         string contract)
        {
            bool shippedOk = predicate(shipped, out string shippedWhy);
            if (!shippedOk)
                failures.Add(label + ": SHIPPED CODE VIOLATES the ruling — " + contract + ". " + shippedWhy);

            bool tombstoneOk = predicate(tombstone, out _);
            if (tombstoneOk)
                failures.Add(label + ": the rule ACCEPTS the known-bad pre-2026-08-20 code, so it is not " +
                             "evaluating anything. Contract: " + contract);

            if (shippedOk && !tombstoneOk)
                log.AppendLine("  " + label.PadRight(24) + " shipped=PASS tombstone=REJECTED ... ok");
        }

        // =====================================================================
        //  PREDICATES (source-lint, comment-blanked)
        // =====================================================================

        private static bool AnchorIsHips(string source, out string why)
        {
            // The resolver is found by its SIGNATURE, not by file position, so the check
            // survives the method moving. Either name is accepted as the entry point: the
            // retired one must still be FOUND in order to be rejected on its bone.
            string body = ExtractMethodBody(source, "Transform ResolveSheatheSocket")
                       ?? ExtractMethodBody(source, "Transform ResolveBackSocket");
            if (body == null)
            {
                why = "no sheathe-socket resolver found (looked for ResolveSheatheSocket / ResolveBackSocket).";
                return false;
            }

            int hips = body.IndexOf("HumanBodyBones.Hips", StringComparison.Ordinal);
            if (hips < 0)
            {
                why = "the resolver never asks for HumanBodyBones.Hips. The owner's ruling names the HIP " +
                      "BONE; asking for Chest resolves to CC_Base_Spine01 on the live rig, which is the " +
                      "waist-height carry that was reported.";
                return false;
            }

            // Hips must be asked for FIRST. A Hips line placed after a Chest fallback would
            // never be reached on the rig that matters, and the trace would still say Spine01.
            foreach (var later in new[] { "HumanBodyBones.Chest", "HumanBodyBones.Spine", "HumanBodyBones.UpperChest" })
            {
                int at = body.IndexOf(later, StringComparison.Ordinal);
                if (at >= 0 && at < hips)
                {
                    why = "the resolver asks for " + later + " BEFORE HumanBodyBones.Hips, so the hips " +
                          "branch is unreachable on any rig that maps both.";
                    return false;
                }
            }

            // Two distinct socket objects means the resolver must be told WHICH slot it is for.
            if (body.IndexOf("offHand", StringComparison.Ordinal) < 0)
            {
                why = "the resolver takes no slot argument, so it can only ever produce ONE socket — the " +
                      "shared-anchor shape that buried the shield inside the body.";
                return false;
            }

            why = null;
            return true;
        }

        private static bool SlotsUseSeparateSockets(string source, out string why)
        {
            string body = ExtractMethodBody(source, "void ApplyHoldPose");
            if (body == null)
            {
                why = "ApplyHoldPose not found — cannot verify which socket each slot parents to.";
                return false;
            }

            // Collect the first argument of every SetParent(...) call in the method. The rule is
            // about the SHEATHED parents, so the hand parents are ignored by name below.
            var parents = new List<string>();
            int i = 0;
            while (true)
            {
                int at = body.IndexOf("SetParent(", i, StringComparison.Ordinal);
                if (at < 0) break;
                int start = at + "SetParent(".Length;
                int comma = body.IndexOf(',', start);
                int close = body.IndexOf(')', start);
                int end = comma >= 0 && (close < 0 || comma < close) ? comma : close;
                if (end < 0) break;
                parents.Add(body.Substring(start, end - start).Trim());
                i = end;
            }

            var sheatheParents = new List<string>();
            foreach (var p in parents)
                if (p.IndexOf("sheathe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0)
                    if (!sheatheParents.Contains(p)) sheatheParents.Add(p);

            if (sheatheParents.Count < 2)
            {
                why = "the sheathed branches parent to " + sheatheParents.Count + " distinct socket " +
                      "variable(s) [" + string.Join(", ", sheatheParents.ToArray()) + "]. Both props on ONE " +
                      "transform at ONE origin is exactly the capture's buried shield.";
                return false;
            }

            // (The socket NAMES are case L2b in Run — they live in string literals, which this
            // predicate's input has deliberately blanked so the brace scan is safe.)
            why = null;
            return true;
        }

        /// <summary>
        /// A1 — the off-hand slot's sheathe anchor must be an ARM bone, asked for BEFORE any hip.
        /// The bone ENUM is the only structural fact available to a lint here; the bone that is
        /// actually RESOLVED is printed by the runtime trace, because on this CC rig asking for
        /// Chest famously returned CC_Base_Spine01. Both halves are needed and neither substitutes
        /// for the other.
        /// </summary>
        private static bool OffHandAnchorIsArm(string source, out string why)
        {
            string body = ExtractMethodBody(source, "Transform ResolveSheatheSocket")
                       ?? ExtractMethodBody(source, "Transform ResolveBackSocket");
            if (body == null)
            {
                why = "no sheathe-socket resolver found (looked for ResolveSheatheSocket / ResolveBackSocket).";
                return false;
            }

            int arm = body.IndexOf("HumanBodyBones.LeftLowerArm", StringComparison.Ordinal);
            if (arm < 0)
            {
                why = "the resolver never asks for HumanBodyBones.LeftLowerArm. The owner's 2026-08-21 " +
                      "F8 names the ARM — 'the shield on arm or arm bone' — and the capture proves the " +
                      "shield was parented to 'SheatheSocket_HipOff' on every frame.";
                return false;
            }

            int hips = body.IndexOf("HumanBodyBones.Hips", StringComparison.Ordinal);
            if (hips >= 0 && hips < arm)
            {
                why = "the resolver asks for HumanBodyBones.Hips BEFORE the arm, so on any rig that " +
                      "maps both — i.e. every rig in this game — the arm branch is unreachable and the " +
                      "shield goes back on the hip.";
                return false;
            }

            // The arm branch must be GATED on the slot. An ungated arm lookup would move the SWORD
            // off the hip too, silently un-doing the owner's separate 2026-08-20 ruling. Two props,
            // two rulings: this file has already been burned twice by generalising one to the other.
            int gate = body.IndexOf("offHand", StringComparison.Ordinal);
            if (gate < 0 || gate > arm)
            {
                why = "the arm lookup is not gated on the offHand slot, so the MAIN-hand weapon would " +
                      "take it too — silently retiring the owner's 2026-08-20 hip ruling for the sword.";
                return false;
            }

            why = null;
            return true;
        }

        /// <summary>
        /// M2 — the sheathe rotation must consult the PER-MESH measurement. A body that reads only
        /// <c>_sheatheLongAxisSign</c> is the state that shipped two contradictory F8s in two days.
        /// </summary>
        private static bool SheathRotationConsultsPerMeshSign(string source, out string why)
        {
            string body = ExtractMethodBody(source, "Quaternion ComputeSheathRotation");
            if (body == null)
            {
                why = "ComputeSheathRotation not found — cannot verify where its sign comes from.";
                return false;
            }
            if (body.IndexOf("_sheatheTipSign", StringComparison.Ordinal) < 0)
            {
                why = "the sheathe rotation never reads _sheatheTipSign, so its sign is ONE global " +
                      "number shared by every weapon. That number was flipped on 2026-08-20 (Blaise " +
                      "read -1 as upside down) and flipped back on 2026-08-21 (the Flameblade read +1 " +
                      "as upside down). Both reports were true; a global sign cannot satisfy both.";
                return false;
            }
            if (body.IndexOf("_sheatheLongAxisSign", StringComparison.Ordinal) < 0)
            {
                why = "the serialized _sheatheLongAxisSign fallback is gone from the rotation. §12: a " +
                      "tuning seam is never stripped — an unmeasurable prop would then have no " +
                      "correction available at all.";
                return false;
            }
            why = null;
            return true;
        }

        // =====================================================================
        //  BEHAVIOURAL CASES
        // =====================================================================

        /// <summary>
        /// A2 — while DRAWN, the off-hand must land on the HAND, never on a sheathe socket. The
        /// owner's report distinguishes the two poses ("not wrist or arm"), so an oracle that only
        /// checked the stowed anchor could pass while the in-combat shield sat on a hip.
        /// </summary>
        private static void CheckDrawnOffHandGoesToTheHand(List<string> failures, StringBuilder log,
                                                           string codeNoStrings)
        {
            string body = ExtractMethodBody(codeNoStrings, "void ApplyHoldPose");
            if (body == null)
            {
                failures.Add("A2: ApplyHoldPose not found — cannot verify where the DRAWN shield seats.");
                return;
            }
            int before = failures.Count;

            // Both sheathe sockets must be null while drawn. This is the single line that keeps the
            // in-combat prop off every stow anchor, hip or arm, without enumerating them.
            foreach (var slot in new[] { "false", "true" })
                if (body.IndexOf("drawn ? null : ResolveSheatheSocket(offHand: " + slot + ")",
                                 StringComparison.Ordinal) < 0)
                    failures.Add("A2: the " + (slot == "true" ? "off-hand" : "main-hand") + " sheathe " +
                                 "socket is no longer resolved as `drawn ? null : ResolveSheatheSocket(...)`. " +
                                 "That expression is what guarantees a DRAWN prop cannot reach a stow " +
                                 "anchor; without it the in-combat shield can sit on the hip while the " +
                                 "stowed one looks correct in a screenshot.");

            if (body.IndexOf("offT.SetParent(_offHandHand", StringComparison.Ordinal) < 0)
                failures.Add("A2: the drawn off-hand branch no longer parents to _offHandHand (the " +
                             "LeftHand bone AttachOffHandProp resolved). The drawn shield belongs in the " +
                             "hand — the owner's 'not wrist or arm' is about the STOWED pose.");

            if (failures.Count == before)
                log.AppendLine("  A2 drawn off-hand seats on the HAND, not a stow anchor . ok");
        }

        // =====================================================================
        //  M1 — WHICH END IS THE TIP IS A PROPERTY OF THE MESH
        // =====================================================================
        //
        // The two captures this case exists for, in one place:
        //   2026-08-20  F8 on Blaise            -> _sheatheLongAxisSign = -1 reads UPSIDE DOWN
        //   2026-08-21  F8 on the Flameblade    -> _sheatheLongAxisSign = +1 reads UPSIDE DOWN
        // Both are true. So the case is not "which sign is correct" — that question has no answer —
        // it is "does the shipped derivation give the two meshes OPPOSITE answers, and does it
        // DECLINE rather than guess when a mesh genuinely cannot say".
        //
        // The fixtures are cubes on purpose: a cube has no taper, so the taper branch cannot decide
        // and the case exercises the GRIP-AT-ORIGIN branch — which is the one that carries the
        // device, because the live props ship with Read/Write OFF and every vertex-reading
        // derivation in this codebase is inert there.
        private static void CheckPerMeshTipSignIsDerived(List<string> failures, StringBuilder log)
        {
            GameObject probe = null;
            try
            {
                probe = new GameObject("SheatheTipSignProbe");
                Transform gripRoot = probe.transform;

                // Tip at +Y: a 1 m bar whose grip origin sits 0.1 m from its LOW end.
                float tipUpSign = 0f, tipDownSign = 0f;
                if (!TryProbeTipSign(gripRoot, 0.4f, out tipUpSign, out string whyUp))
                    failures.Add("M1a: a bar with its grip 0.1 m from the low end could not resolve a " +
                                 "sheathed sign (" + whyUp + "). If the shipped derivation declines on " +
                                 "geometry this unambiguous, every real prop falls back to the ONE " +
                                 "global number and the 08-20/08-21 ping-pong resumes.");
                else if (tipUpSign > 0f)
                    failures.Add("M1a: a bar whose TIP is at +Y resolved bodyUpSign=" + tipUpSign +
                                 ". Mapping prop +Y onto +body.up points the tip at the SKY — that is " +
                                 "the literal 'sword upside down (sheathed)' the owner reported.");
                else
                    log.AppendLine("  M1a tip-at-+Y mesh hangs tip DOWN (sign=-1) ............. ok");

                if (!TryProbeTipSign(gripRoot, -0.4f, out tipDownSign, out string whyDown))
                    failures.Add("M1b: the MIRRORED bar (grip 0.1 m from the high end — a native prop " +
                                 "authored the other way up) could not resolve a sheathed sign (" +
                                 whyDown + ").");
                else if (tipDownSign < 0f)
                    failures.Add("M1b: the mirrored bar resolved bodyUpSign=" + tipDownSign + ", the " +
                                 "same direction as its mirror image. The derivation is not reading the " +
                                 "mesh at all.");
                else
                    log.AppendLine("  M1b mirrored mesh hangs tip DOWN too (sign=+1) .......... ok");

                // TEETH. This is the whole argument in one assertion: the two meshes need OPPOSITE
                // signs, therefore no single global value can serve both, therefore flipping the
                // field can never be the fix — it can only choose which hero ships broken.
                if (tipUpSign != 0f && tipDownSign != 0f && tipUpSign * tipDownSign > 0f)
                    failures.Add("M1c: two mirror-image meshes resolved the SAME sign (" + tipUpSign +
                                 " and " + tipDownSign + "). A derivation that cannot distinguish a " +
                                 "prop from its mirror is a global constant wearing a measurement's " +
                                 "clothes, and the reported defect survives it.");
                else if (tipUpSign != 0f && tipDownSign != 0f)
                    log.AppendLine("  M1c mirror-image meshes get OPPOSITE signs .............. ok");

                // And it must DECLINE on a prop that genuinely cannot answer, rather than guessing —
                // a guess here would be indistinguishable from the global constant while looking
                // like a measurement in the trace, which is strictly worse than the bug.
                if (TryProbeTipSign(gripRoot, 0f, out float centred, out _))
                    failures.Add("M1d: a bar whose grip sits at its MIDPOINT still returned a sign (" +
                                 centred + "). Neither end is nearer the grip and a cube has no taper, " +
                                 "so there is nothing to measure — returning a number here launders a " +
                                 "guess as a derivation and hides it from the fallback Warn.");
                else
                    log.AppendLine("  M1d an undecidable mesh DECLINES (fallback stands) ...... ok");
            }
            catch (Exception e)
            {
                failures.Add("M1: the per-mesh tip-sign probe threw — " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>Builds a 1 m bar offset along Y under <paramref name="gripRoot"/> and asks the
        /// SHIPPED derivation which way it hangs. The bar is destroyed before returning so the next
        /// probe measures only its own geometry.</summary>
        private static bool TryProbeTipSign(Transform gripRoot, float localY, out float sign, out string why)
        {
            sign = 0f;
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                bar.name = "TipSignBar";
                bar.transform.SetParent(gripRoot, false);
                bar.transform.localPosition = new Vector3(0f, localY, 0f);
                bar.transform.localScale = new Vector3(0.05f, 1f, 0.05f);
                bool ok = WeaponOrientHelper.TryResolveSheathedTipSign(bar, gripRoot, out var r);
                why = r.Why;
                if (!ok || !r.Valid) return false;
                sign = r.BodyUpSign;
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bar);
            }
        }

        // =====================================================================
        //  M3 — EVERY SHIPPED WEAPON MESH MUST ANSWER THE QUESTION THAT APPLIES TO IT
        // =====================================================================
        //
        // M1 proves the derivation works on geometry we built. M3 asks the question that actually
        // matters: do the meshes THIS GAME SHIPS answer it? A prop that declines falls back to the
        // one global number, and the owner's report is what that looks like on a phone. So this case
        // walks the shipped weapon props, runs the SHIPPED resolver on each, and FAILS by name on
        // any that cannot answer — turning "some weapon somewhere hangs upside down" into a list.
        //
        // ── WHAT CHANGED, AND WHY IT IS NARROWER *AND* STRONGER (WO-1136, owner 2026-08-22) ──────
        //
        // This case used to demand ONE answer of every mesh: a SIGN. staff_A cannot give one — taper
        // relGap 0.001, grip-origin relGap 0.000, its two ends identical to four decimals — and no
        // cleverness extracts a fact the geometry does not contain. Owner ruling, verbatim:
        //   "the staff should be longest mesh on Y axis with and placed with staff still verticle
        //    not horizontal"
        // A symmetrical staff has no upside down, so the SIGN is the wrong question for it. The right
        // one — and unlike tip direction a measurable one — is VERTICALITY, which is what the player
        // can actually see and what the shipped `tiltFromVertical` trace already reports.
        //
        // So the demand is now per-outcome, and every outcome is still failable:
        //   • DECIDED       -> assert the sign, exactly as before. Unchanged.
        //   • SIGN-AGNOSTIC -> the ends are measurably IDENTICAL. No sign is demanded; instead the
        //                      REAL requirement is asserted: longest axis on Y and carried VERTICAL.
        //                      A symmetrical prop that would hang horizontal FAILS here.
        //   • UNDECIDABLE   -> the ends measurably DIFFER but under the decision margin, i.e. the
        //                      mesh encodes an up we failed to read. Still a hard, named failure.
        //
        // ⛔ AND NOT BY EXEMPTION. There is no skip set and no mesh name anywhere in this case — grep
        // it for "staff" and you will find only this comment. staff_A passes because it MEASURES
        // symmetrical, Y-longest and vertical; the next symmetrical prop is held to the same three
        // measurements, and a symmetrical prop that lies across the body is caught, which an
        // exemption list could never do. (M3b below is the proof that clause has teeth.)
        //
        // ⚠ TWO FRAMES, DELIBERATELY. The SIGN is asked in the frame this case has always used: the
        // prop instantiated raw, exactly as authored. The VERTICALITY question cannot be asked there
        // — the sheathe pose consumes the SEATED frame (EquipmentController line ~1223 resolves the
        // sign only AFTER NormalizeInto/SeatHiltLowerHalf, "so it measures the frame the sheathe pose
        // will actually use"), and a raw KayKit FBX is commonly Z-long before seating. Asserting "Y"
        // against the authored frame would red a prop that plays perfectly. So the verticality clause
        // re-seats the instance through the SHIPPED WeaponBoundsOrient.NormalizeInto — the same call
        // the live melee path makes — and measures that. Which also means this clause now covers the
        // seat itself: if the align ever stops putting the longest axis on +Y (it regressed exactly
        // that way once already — WO-970, long axes left on X for a month), this case goes red.
        private const string ShippedWeaponDir = "Assets/Resources/Heroes/Props/Weapons";

        private static void CheckShippedWeaponMeshesResolveASign(List<string> failures, StringBuilder log)
        {
            GameObject probe = null;
            try
            {
                if (!Directory.Exists(ShippedWeaponDir))
                {
                    // NOT a hollow pass — say the coverage is absent rather than implying it passed.
                    log.AppendLine("  M3 shipped weapon meshes .............. SKIPPED (" +
                                   ShippedWeaponDir + " not present in this checkout)");
                    return;
                }

                probe = new GameObject("ShippedWeaponSignProbe");
                var undecided = new List<string>();
                var notVertical = new List<string>();
                int checked_ = 0;
                int agnostic = 0;

                foreach (var path in Directory.GetFiles(ShippedWeaponDir))
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext != ".fbx" && ext != ".prefab") continue;
                    string file = Path.GetFileNameWithoutExtension(path);
                    // Backups (_tripobak_*) are not shipped, and a BOW/SHIELD reads no sheathe sign
                    // at all (the bow keeps its own derived carry, a shield has no tip to invert).
                    if (file.StartsWith("_", StringComparison.Ordinal)) continue;
                    string lower = file.ToLowerInvariant();
                    if (lower.StartsWith("bow") || lower.StartsWith("shield")) continue;

                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                    if (asset == null) continue;

                    GameObject inst = null;
                    try
                    {
                        inst = UnityEngine.Object.Instantiate(asset, probe.transform);
                        inst.transform.localPosition = Vector3.zero;
                        inst.transform.localRotation = Quaternion.identity;
                        checked_++;
                        bool decided = WeaponOrientHelper.TryResolveSheathedTipSign(
                                           inst, probe.transform, out var r) && r.Valid;
                        if (decided)
                        {
                            log.AppendLine("    M3 " + file.PadRight(20) + " sign=" +
                                           r.BodyUpSign.ToString("+0;-0") + " via " + r.Source);
                        }
                        else if (r.Decision == WeaponOrientHelper.SheathedSignDecision.SignAgnostic)
                        {
                            // No sign exists to demand. Demand the thing that does: VERTICAL.
                            // Measured in the SEATED frame (see the two-frames note above), because
                            // that is the frame ComputeSheathRotation hangs on the vertical.
                            agnostic++;
                            if (!TrySeatedVerticality(inst, probe.transform, out float tiltDeg,
                                                      out string vWhy))
                                notVertical.Add(file + " (SIGN-AGNOSTIC, and " + vWhy + ")");
                            else
                                log.AppendLine("    M3 " + file.PadRight(20) +
                                               " SIGN-AGNOSTIC (ends identical: taperRelGap=" +
                                               r.TaperRelGap.ToString("0.####") + " gripRelGap=" +
                                               r.GripRelGap.ToString("0.####") + ") -> " + vWhy +
                                               " tilt=" + tiltDeg.ToString("0.#") + "deg");
                        }
                        else
                        {
                            undecided.Add(file + " (" + (string.IsNullOrEmpty(r.Why) ? "no reason given" : r.Why) + ")");
                        }
                    }
                    finally
                    {
                        if (inst != null) UnityEngine.Object.DestroyImmediate(inst);
                    }
                }

                if (checked_ == 0)
                    failures.Add("M3: no shipped melee weapon meshes were found under " + ShippedWeaponDir +
                                 " to test. This case is the one that asks whether the REAL props can " +
                                 "answer; measuring none of them is not a pass.");
                if (undecided.Count > 0)
                    failures.Add("M3: " + undecided.Count + " of " + checked_ + " shipped weapon meshes " +
                                 "cannot resolve a sheathed orientation — " + string.Join("; ", undecided.ToArray()) +
                                 ". Each of these hangs on the ONE global _sheatheLongAxisSign, which is " +
                                 "correct for at most half the catalogue by construction. That is the " +
                                 "owner's 'sword upside down (sheathed)', and flipping the field only " +
                                 "moves it to the other half. NOTE these are the AMBIGUOUS-BUT-ASYMMETRIC " +
                                 "props: their two ends measurably DIFFER, so the mesh does encode an up " +
                                 "and the derivation failed to read it. A symmetrical prop (a " +
                                 "quarterstaff) is reported separately and is not this failure.");
                if (notVertical.Count > 0)
                    failures.Add("M3: " + notVertical.Count + " of " + checked_ + " shipped weapon meshes " +
                                 "are SIGN-AGNOSTIC (their two ends are measurably identical, so no sign " +
                                 "exists to be right about) yet do NOT seat vertical — " +
                                 string.Join("; ", notVertical.ToArray()) + ". Owner ruling WO-1136: " +
                                 "\"the staff should be longest mesh on Y axis with and placed with staff " +
                                 "still verticle not horizontal\". A prop like this hangs ACROSS THE BODY " +
                                 "— the ~90deg the shipped tiltFromVertical trace names — and no sign can " +
                                 "repair it, because a sign flips a long axis, it does not rotate one " +
                                 "onto the vertical. Fix the SEAT (NormalizeInto / the authored native " +
                                 "frame).");
                if (undecided.Count == 0 && notVertical.Count == 0 && checked_ > 0)
                    log.AppendLine("  M3 all " + checked_ + " shipped weapon meshes answer (" +
                                   (checked_ - agnostic) + " by sign, " + agnostic +
                                   " sign-agnostic + measured VERTICAL) ... ok");
            }
            catch (Exception e)
            {
                failures.Add("M3: walking the shipped weapon meshes threw — " + e.GetType().Name +
                             ": " + e.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// Seats <paramref name="inst"/> through the SHIPPED melee seat and asks the SHIPPED
        /// verticality oracle whether the result hangs upright. One helper, used by BOTH M3 (real
        /// props) and M3b (the fixture that proves the clause can fail) — so the teeth M3b
        /// demonstrates are literally the teeth M3 bites the catalogue with. A re-implemented copy
        /// here could pass while the shipped path fails, which is worth nothing.
        /// </summary>
        /// <remarks>
        /// The seat is <c>WeaponBoundsOrient.NormalizeInto(..., GripAnchor.HiltEnd)</c>, which is the
        /// call EquipmentController makes for melee before it resolves the sheathe sign. Its job is
        /// to put the LONGEST measured axis on +Y; the oracle then measures whether it did, because
        /// ComputeSheathRotation hangs grip-local +Y — and nothing else — on the vertical.
        /// </remarks>
        private static bool TrySeatedVerticality(GameObject inst, Transform gripRoot,
                                                 out float tiltDeg, out string why)
        {
            WeaponBoundsOrient.NormalizeInto(inst, gripRoot, 1f,
                                             WeaponBoundsOrient.GripAnchor.HiltEnd);
            // Re-measure in the seated frame. Only the AXIS/dominance/tilt fields are read here —
            // the seat moves the grip origin to one end, so a sign the resolver "finds" after
            // seating is an artefact of the anchor, not of the mesh. The SIGN question stays in the
            // authored frame where M3 asked it.
            WeaponOrientHelper.TryResolveSheathedTipSign(inst, gripRoot, out var seated);
            return WeaponOrientHelper.TrySheathesVertical(seated, out tiltDeg, out why);
        }

        // =====================================================================
        //  M3b — THE VERTICALITY CLAUSE MUST BE ABLE TO FAIL
        // =====================================================================
        //
        // M3's sign-agnostic branch is the clause that lets staff_A pass. A clause that lets a prop
        // pass is worth exactly nothing unless it can also REFUSE one, so this case drives the SAME
        // helper M3 drives (TrySeatedVerticality) over two fixtures that differ in ONE respect:
        //
        //   FIXTURE A: a symmetrical BAR  (mesh 0.05 x 1 x 0.05) -> must be accepted.
        //   FIXTURE B: a symmetrical SLAB (mesh 0.05 x 1 x 0.98) -> must be REFUSED.
        //
        // Both are symmetrical, so both are sign-agnostic; if the branch were an exemption
        // ("symmetrical props are fine"), B would sail through and this case would go red — which is
        // precisely why it exists. This is the WO-1136 acceptance clause "a sign-agnostic prop
        // rotated to lie horizontal FAILS", asserted by the suite instead of argued in a work order.
        //
        // ⚠ FIXTURE B FIGHTS THE SEAT ON PURPOSE. TrySeatedVerticality re-seats through the shipped
        // NormalizeInto, whose whole job is to put the longest axis back on +Y — so a merely rotated
        // bar would be straightened out and pass, proving nothing. B is therefore built so the seat
        // CANNOT straighten it: its longest extent and its second-longest are near-equal (a slab),
        // so there is no long axis to align and the oracle's first clause — the dominance bar —
        // refuses to make a verticality claim at all. That is the honest failure of a prop that
        // genuinely cannot be carried upright, and it is the one an axis-aligned box can prove.
        //
        // ⛔ AND BOTH FIXTURES ASSERT THEIR OWN PREMISE FIRST (FixtureShapeHolds). On 2026-08-22 both
        // were built by transform.localScale, which NormalizeInto resets to one by design — so both
        // arrived at the oracle as the SAME 1x1x1 cube. A went red (correctly: a cube has no long
        // axis) and B went GREEN while asserting nothing about a slab, because the slab no longer
        // existed. The lesson is not "clause 1 was too strict" — it was right — it is that a fixture
        // must prove it is the shape it claims before its verdict is worth reading.
        private static void CheckSignAgnosticVerticalClauseHasTeeth(List<string> failures, StringBuilder log)
        {
            GameObject probe = null;
            try
            {
                probe = new GameObject("SignAgnosticVerticalProbe");

                // FIXTURE A — a symmetrical bar with a real long axis. Sign-agnostic (no taper, ends
                // identical), and it seats upright.
                var aSize = new Vector3(0.05f, 1f, 0.05f);
                bool aVertical = TryProbeSeatedVerticality(probe.transform, aSize,
                    out float aTilt, out string aWhy, out Vector3 aPre);
                if (!FixtureShapeHolds(failures, "A", aSize, aPre))
                {
                    // premise broken — its verdict is meaningless, so do not report one
                }
                else if (!aVertical)
                    failures.Add("M3b-A: a symmetrical BAR (0.05 x 1 x 0.05) did not read as carried " +
                                 "vertical after the shipped seat — " + aWhy + " (tilt=" +
                                 aTilt.ToString("0.#") + "deg). This is the exact shape of staff_A, " +
                                 "the prop the owner ruled on: \"longest mesh on Y axis ... still " +
                                 "verticle not horizontal\". If this fails, M3's sign-agnostic branch " +
                                 "rejects every staff and the ruling is not implemented.");
                else
                    log.AppendLine("  M3b-A symmetrical bar seats VERTICAL (tilt=" +
                                   aTilt.ToString("0.#") + "deg) ................ ok");

                // FIXTURE B — equally symmetrical, but a SLAB: its two largest extents are within a
                // hair of each other, so no long axis exists for the seat to stand upright. It must
                // be REFUSED, not waved through for being symmetrical.
                var bSize = new Vector3(0.05f, 1f, 0.98f);
                bool bVertical = TryProbeSeatedVerticality(probe.transform, bSize,
                    out float bTilt, out string bWhy, out Vector3 bPre);
                if (!FixtureShapeHolds(failures, "B", bSize, bPre))
                {
                    // premise broken — its verdict is meaningless, so do not report one
                }
                else if (bVertical)
                    failures.Add("M3b-B: a symmetrical SLAB (0.05 x 1 x 0.98 — no long axis to stand " +
                                 "up) was ACCEPTED as carried vertical (tilt=" + bTilt.ToString("0.#") +
                                 "deg, " + bWhy + "). The sign-agnostic branch is passing props for " +
                                 "being symmetrical rather than for being upright — i.e. it is an " +
                                 "exemption list wearing a measurement's clothes, which is exactly " +
                                 "what WO-1136 forbids. staff_A's pass would then mean nothing.");
                else
                    log.AppendLine("  M3b-B symmetrical slab is REFUSED (" + bWhy.Split(':')[0] +
                                   ") ....... ok");

                // And the horizontal case stated in the owner's own terms: a prop whose long axis is
                // NOT Y in the seat frame must be refused. Asked of the ORACLE directly, with a
                // hand-built resolution, because the seat's whole job is to prevent this state — the
                // oracle is the last line that would catch it if the seat ever stopped doing that.
                var lyingDown = new WeaponOrientHelper.SheathedTipResolution
                {
                    Decision = WeaponOrientHelper.SheathedSignDecision.SignAgnostic,
                    LongAxis = 2,                    // Z — across the body
                    LongAxisDominance = 20f,         // unambiguously long, just in the wrong direction
                    LongAxisOffVerticalDeg = 90f
                };
                if (WeaponOrientHelper.TrySheathesVertical(lyingDown, out float hTilt, out string hWhy))
                    failures.Add("M3b-C: a sign-agnostic prop whose long axis is Z — lying ACROSS the " +
                                 "body at " + hTilt.ToString("0.#") + "deg off vertical — was accepted " +
                                 "as vertical (" + hWhy + "). ComputeSheathRotation hangs grip-local " +
                                 "+Y on the vertical and nothing else, so this prop is horizontal on " +
                                 "the hero and the oracle said it was fine.");
                else
                    log.AppendLine("  M3b-C long-axis-Z (horizontal) prop is REFUSED ......... ok");

                // Symmetry of the argument: the SAME oracle must refuse an AMBIGUOUS (not agnostic)
                // prop whose long axis is not Y too — verticality is a property of the geometry, not
                // of which bucket the sign landed in.
                var ambiguousSideways = new WeaponOrientHelper.SheathedTipResolution
                {
                    Decision = WeaponOrientHelper.SheathedSignDecision.Undecidable,
                    LongAxis = 0,
                    LongAxisDominance = 20f,
                    LongAxisOffVerticalDeg = 90f
                };
                if (WeaponOrientHelper.TrySheathesVertical(ambiguousSideways, out _, out _))
                    failures.Add("M3b-D: an ambiguous prop whose long axis is X was accepted as " +
                                 "vertical. The verticality oracle must judge the GEOMETRY, not the " +
                                 "sign outcome.");
                else
                    log.AppendLine("  M3b-D ambiguous + long-axis-X prop is REFUSED .......... ok");
            }
            catch (Exception e)
            {
                failures.Add("M3b: the sign-agnostic verticality fixtures threw — " + e.GetType().Name +
                             ": " + e.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>Builds a box whose MESH measures <paramref name="meshSize"/> under
        /// <paramref name="gripRoot"/> and runs it through the same seat+oracle pair M3 uses.
        /// <paramref name="preSeatSize"/> reports what the fixture actually measured BEFORE the seat,
        /// so the caller can assert the fixture is the shape it claims to be. Destroyed (mesh
        /// included) before returning so the next fixture measures only its own geometry.
        /// <para>
        /// ⛔ THE SHAPE MUST LIVE IN THE MESH, NOT IN `transform.localScale`. This is the whole
        /// lesson of the 2026-08-22 red. The first draft built `CreatePrimitive(Cube)` — whose mesh is
        /// 1x1x1 — and put the bar shape in `localScale = (0.05, 1, 0.05)`. `NormalizeInto`'s FOURTH
        /// LINE is `prop.transform.localScale = Vector3.one;`, by design: a real prop carries its
        /// shape in its mesh and the seat owns the scale (it re-scales to targetLength off the
        /// measured longest axis). So the seat wiped the fixture and handed the oracle a 1x1x1 CUBE.
        /// The captured proof, one line, and it names the defect exactly:
        ///   [Flow:Equip] AlignAxes 'VerticalityFixture': meshSize=(1, 1, 1) longAxis=X ...
        ///   [Flow:Equip] SheatheSign 'VerticalityFixture': ... longest=X(1m) dominance=1 ...
        /// Dominance 1 on a bar authored 20:1. Clause 1 of TrySheathesVertical was RIGHT to refuse
        /// it — an object with no long axis has no verticality — and the fixture was the thing that
        /// was wrong.
        /// </para>
        /// <para>
        /// ⚠ AND IT MADE FIXTURE B A HOLLOW PASS. A and B differ only in size, so once the seat
        /// flattened both to the same cube their traces came out BYTE-IDENTICAL (reg-staff.log lines
        /// 17830-17867 vs 17885-17922). B was being "REFUSED" for being a cube, not for being a slab
        /// — the case was reporting ok while asserting nothing about the thing it was written to
        /// assert. That is why <paramref name="preSeatSize"/> exists and why the caller checks it:
        /// a fixture that is not the shape it claims must FAIL LOUDLY, never quietly pass.
        /// </para>
        /// <para>
        /// ⚠ NO ROTATION KNOB, DELIBERATELY. An earlier draft took a localRotation so a fixture could
        /// be "laid horizontal" — but NormalizeInto zeroes localRotation as its FIRST act and then
        /// aligns the longest axis back onto +Y, so the knob would have done nothing while looking
        /// like it did something. Same class of mistake as the scale, caught earlier. The horizontal
        /// case is proven the two honest ways instead: a SLAB the seat cannot stand up (M3b-B), and
        /// the oracle asked directly about a long-axis-Z prop (M3b-C).
        /// </para></summary>
        private static bool TryProbeSeatedVerticality(Transform gripRoot, Vector3 meshSize,
                                                      out float tiltDeg, out string why,
                                                      out Vector3 preSeatSize)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh baked = null;
            preSeatSize = Vector3.zero;
            try
            {
                bar.name = "VerticalityFixture";
                bar.transform.SetParent(gripRoot, false);
                bar.transform.localPosition = Vector3.zero;
                bar.transform.localRotation = Quaternion.identity;
                bar.transform.localScale = Vector3.one;

                // Scale the unit cube's VERTICES into a new mesh. ⛔ Never mutate `sharedMesh` in
                // place — that is Unity's built-in cube, shared by every primitive in the editor.
                var filter = bar.GetComponent<MeshFilter>();
                Mesh unit = filter.sharedMesh;
                var verts = unit.vertices;            // .vertices already returns a copy
                for (int i = 0; i < verts.Length; i++)
                    verts[i] = new Vector3(verts[i].x * meshSize.x,
                                           verts[i].y * meshSize.y,
                                           verts[i].z * meshSize.z);
                baked = new Mesh { name = "VerticalityFixtureMesh" };
                baked.vertices = verts;
                baked.triangles = unit.triangles;
                baked.RecalculateNormals();
                baked.RecalculateBounds();
                filter.sharedMesh = baked;

                if (WeaponOrientHelper.TryMeasureAxes(bar, gripRoot, out var pre))
                    preSeatSize = pre.Size;
                return TrySeatedVerticality(bar, gripRoot, out tiltDeg, out why);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bar);
                if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        /// <summary>The fixture's own premise, asserted before its result is believed. A fixture that
        /// did not reach the helper in the shape the case describes must FAIL — the 2026-08-22 red
        /// was exactly that state, and the half of it that went unnoticed (fixture B) was a green
        /// tick over an assertion about a shape that no longer existed.</summary>
        private static bool FixtureShapeHolds(List<string> failures, string label,
                                              Vector3 requested, Vector3 measured)
        {
            if ((requested - measured).sqrMagnitude <= 1e-4f) return true;
            failures.Add("M3b-" + label + " FIXTURE PREMISE: the box was authored " + requested +
                         " but measured " + measured + " in the grip root before seating. The case " +
                         "below asserts something about a shape that is not the shape under test, so " +
                         "its verdict — pass OR fail — means nothing. (2026-08-22: the shape was put " +
                         "in transform.localScale, which NormalizeInto resets to one by design, and " +
                         "every fixture arrived at the oracle as a 1x1x1 cube.)");
            return false;
        }

        private static void CheckNativeSwordMainHipAndDrawPreserved(List<string> failures, StringBuilder log)
        {
            const string assetPath = "Assets/Blink/Art/Weapons/LowPoly/MegaWeaponPack1/Meshes_MWP1/Sword1h_01.fbx";
            GameObject host = null;
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null) { failures.Add("SWORD-HIP: captured starter mesh missing: " + assetPath); return; }
                host = new GameObject("SwordHipRegression");
                host.transform.rotation = Quaternion.Euler(0, 37, 0);
                var ec = host.AddComponent<EquipmentController>();
                var t = typeof(EquipmentController);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                Action<string, object> set = (name, value) => t.GetField(name, flags).SetValue(ec, value);
                var kindField = t.GetField("_currentWeaponKind", flags);
                object swordKind = Enum.Parse(kindField.FieldType, "Sword");
                set("_currentWeaponKind", swordKind);
                set("_currentWeaponNative", true);
                set("_currentWeaponMeshKey", "__sword_hip_probe__");
                var socket = new GameObject("SwordHipSocket").transform;
                socket.SetParent(host.transform, false);
                socket.localRotation = Quaternion.Euler(17, 83, -21);
                set("_sheatheSocketMain", socket);
                var hand = new GameObject("RightHandProbe").transform;
                hand.SetParent(host.transform, false);
                set("_weaponHand", hand);
                var grip = new GameObject("SwordGripProbe").transform;
                grip.SetParent(host.transform, false);
                set("_gripRoot", grip);
                var model = UnityEngine.Object.Instantiate(asset, grip);
                var drawnPosition = new Vector3(.02f, -.01f, .03f);
                var drawnRotation = Quaternion.Euler(13, 71, -29);
                set("_weaponDrawnLocalPos", drawnPosition);
                set("_baseGripRot", drawnRotation);
                var resolve = t.GetMethod("ResolveSheathedTipSign", flags);
                var preview = t.GetMethod("ApplySheathedSeatingPreview", flags);
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    model.transform.localScale = new Vector3(1, mirror == 0 ? 1 : -1, 1);
                    resolve.Invoke(ec, new object[] { model, grip, "__sword_hip_probe__", swordKind });
                    ec.SetCombatActive(false);
                    Vector3 runtimePosition = grip.localPosition;
                    Quaternion runtimeRotation = grip.localRotation;
                    var filter = model.GetComponentInChildren<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    { failures.Add("SWORD-HIP: starter mesh bounds not measurable"); continue; }
                    Bounds localBounds = filter.sharedMesh.bounds;
                    Bounds bounds = default;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 p = localBounds.center + Vector3.Scale(localBounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        Vector3 measured = grip.InverseTransformPoint(filter.transform.TransformPoint(p));
                        if (corner == 0) bounds = new Bounds(measured, Vector3.zero);
                        else bounds.Encapsulate(measured);
                    }
                    Vector3 size = bounds.size;
                    int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                    Vector3 lo = bounds.center, hi = bounds.center;
                    lo[axis] -= bounds.extents[axis]; hi[axis] += bounds.extents[axis];
                    bool hiltLow = Mathf.Abs(lo[axis]) < Mathf.Abs(hi[axis]);
                    Vector3 hilt = grip.TransformPoint(hiltLow ? lo : hi);
                    Vector3 tip = grip.TransformPoint(hiltLow ? hi : lo);
                    if (Vector3.Dot(hilt - socket.position, host.transform.right) <= 0f ||
                        Vector3.Angle(tip - hilt, -host.transform.up) > AngleTolDeg)
                        failures.Add("SWORD-HIP: measured starter hilt is not on main-hand hip with blade down (mirror=" + mirror + ")");
                    preview.Invoke(ec, new object[] { grip, false, Vector3.zero, Vector3.zero, false });
                    if (Vector3.Distance(grip.localPosition, runtimePosition) > .0001f ||
                        Quaternion.Angle(grip.localRotation, runtimeRotation) > .01f)
                        failures.Add("SWORD-HIP: seating preview differs from runtime sheath");
                    ec.SetCombatActive(true);
                    if (grip.parent != hand || Vector3.Distance(grip.localPosition, drawnPosition) > .0001f ||
                        Quaternion.Angle(grip.localRotation, drawnRotation) > .01f)
                        failures.Add("SWORD-HIP: sheathing or preview altered the saved drawn pose");
                }
                var side = t.GetMethod("MainHandSheatheSide", flags);
                foreach (string family in new[] { "Staff", "Bow" })
                {
                    set("_currentWeaponKind", Enum.Parse(kindField.FieldType, family));
                    if (!Mathf.Approximately((float)side.Invoke(ec, null), -1f))
                        failures.Add("SWORD-HIP: " + family + " carry side changed outside sword scope");
                }
                log.AppendLine("  SWORD-HIP measured native starter + mirror: main hip, blade down, preview parity, drawn pose retained.");
            }
            catch (Exception ex) { failures.Add("SWORD-HIP: " + ex); }
            finally { if (host != null) UnityEngine.Object.DestroyImmediate(host); }
        }

        private static void CheckSidesAreOpposite(List<string> failures, StringBuilder log)
        {
            try
            {
                var t = typeof(EquipmentController);
                var main = t.GetField("SheatheSideMain", BindingFlags.NonPublic | BindingFlags.Static);
                var off = t.GetField("SheatheSideOff", BindingFlags.NonPublic | BindingFlags.Static);
                if (main == null || off == null)
                {
                    failures.Add("S1: SheatheSideMain / SheatheSideOff are gone. The two slots no longer " +
                                 "have declared opposite sides, so nothing stops them sharing a hip again.");
                    return;
                }
                float m = Convert.ToSingle(main.GetRawConstantValue(), CultureInfo.InvariantCulture);
                float o = Convert.ToSingle(off.GetRawConstantValue(), CultureInfo.InvariantCulture);
                if (Mathf.Approximately(m, 0f) || Mathf.Approximately(o, 0f) || m * o >= 0f)
                    failures.Add("S1: the sheathe sides are not opposite (main=" + m + " off=" + o + "). " +
                                 "Same-sign sides put the sword and the shield on the same hip.");
                else
                    log.AppendLine("  S1 hip sides opposite (main=" + m + " off=" + o + ") .......... ok");
            }
            catch (Exception e)
            {
                failures.Add("S1: could not read the sheathe side constants — " + e.Message);
            }
        }

        private static void CheckShippedRotation(List<string> failures, StringBuilder log)
        {
            GameObject probe = null;
            try
            {
                var t = typeof(EquipmentController);
                var method = t.GetMethod("ComputeSheathRotation",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(Transform), typeof(float) }, null);
                if (method == null)
                {
                    failures.Add("G: ComputeSheathRotation(Transform, float) is gone — the per-slot sheathe " +
                                 "derivation cannot be driven, so the vertical/inverted rule is unproven.");
                    return;
                }
                var diagonalField = t.GetField("_sheatheBladeDiagonalDeg", BindingFlags.NonPublic | BindingFlags.Instance);
                var signField = t.GetField("_sheatheLongAxisSign", BindingFlags.NonPublic | BindingFlags.Instance);
                if (diagonalField == null || signField == null)
                {
                    failures.Add("G: _sheatheBladeDiagonalDeg / _sheatheLongAxisSign are gone — the tilt and " +
                                 "the one-number 'inverted' flip are the two knobs the owner was promised.");
                    return;
                }
                var sideMain = t.GetField("SheatheSideMain", BindingFlags.NonPublic | BindingFlags.Static);
                var sideOff = t.GetField("SheatheSideOff", BindingFlags.NonPublic | BindingFlags.Static);
                float mainSide = sideMain != null ? Convert.ToSingle(sideMain.GetRawConstantValue(), CultureInfo.InvariantCulture) : -1f;
                float offSide = sideOff != null ? Convert.ToSingle(sideOff.GetRawConstantValue(), CultureInfo.InvariantCulture) : 1f;

                probe = new GameObject("SheathePoseProbe");
                // The body faces an arbitrary direction and the socket is rotated nowhere near it.
                // If the derivation were anchor-dependent (a typed euler in the socket's frame is
                // the classic way to be), these two would disagree and the assertions below would
                // read as noise instead of a pose.
                probe.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
                var ec = probe.AddComponent<EquipmentController>();
                var socket = new GameObject("SheatheSocket_HipMain").transform;
                socket.SetParent(probe.transform, false);
                socket.localRotation = Quaternion.Euler(-18f, 164f, 25f);

                Transform body = probe.transform;
                float shippedDiagonal = Convert.ToSingle(diagonalField.GetValue(ec), CultureInfo.InvariantCulture);
                float shippedSign = Convert.ToSingle(signField.GetValue(ec), CultureInfo.InvariantCulture);

                // ── G1: shipped defaults → long axis VERTICAL ────────────────────────
                Vector3 longMain = LongAxisWorld(method, ec, socket, mainSide);
                float tilt = Vector3.Angle(longMain, longMain.y >= 0f ? body.up : -body.up);
                if (!Mathf.Approximately(shippedDiagonal, 0f))
                    failures.Add("G1: _sheatheBladeDiagonalDeg ships at " + shippedDiagonal + ", not 0. The " +
                                 "owner ruled the long axis runs 'up and down'; a non-zero shipped default is " +
                                 "the retired baldric diagonal coming back.");
                else if (tilt > AngleTolDeg)
                    failures.Add("G1: the sheathed long axis sits " + tilt.ToString("0.#") + " deg off vertical " +
                                 "(tolerance " + AngleTolDeg + "). ~28 = the retired baldric diagonal; ~90 = " +
                                 "lying across the body, the state in logs/device/sheathed-weapon.png.");
                else
                    log.AppendLine("  G1 long axis vertical (" + tilt.ToString("0.#") + " deg off) ......... ok");

                // ── G2: THE PER-MESH SIGN OUTRANKS THE GLOBAL FIELD ──────────────────
                //
                // ⛔ THIS CASE USED TO ASSERT A GLOBAL DEFAULT, AND THAT ASSERTION WAS THE BUG,
                // RESTATED AS AN ORACLE. It read: "_sheatheLongAxisSign ships at -1 (tip DOWN), but
                // the owner's 2026-08-21 F8 identified that pose as upside down. Ship +1 tip UP." —
                // written the same day an F8 on Blaise had said the OPPOSITE about the OTHER sign.
                // Both captures were true, because which end is the tip is a property of the MESH:
                // NormalizeInto + SeatHiltLowerHalf put the tip at prop +Y, a NATIVE prop keeps
                // whatever the artist authored. A suite that pins ANY single global value therefore
                // ratifies whichever hero was photographed last and guarantees the other one ships
                // broken. The rule that can actually hold is: the measured, per-mesh sign WINS, and
                // the field survives only as the fallback for a prop that cannot answer.
                var tipSignField = t.GetField("_sheatheTipSign", BindingFlags.NonPublic | BindingFlags.Instance);
                if (tipSignField == null)
                {
                    failures.Add("G2: EquipmentController has no _sheatheTipSign — the per-mesh sheathe " +
                                 "sign is gone and the pose is back on ONE global number for every weapon " +
                                 "in the game. That number cannot be right for both a normalized prop and " +
                                 "a native one; shipping it is the 08-20/08-21 upside-down ping-pong.");
                }
                else
                {
                    // Derived says DOWN while the field says UP: the mesh must win.
                    signField.SetValue(ec, 1f);
                    tipSignField.SetValue(ec, -1f);
                    float downDot = Vector3.Dot(LongAxisWorld(method, ec, socket, mainSide).normalized, body.up);
                    // Derived says UP while the field says DOWN: the mesh must win again — asserted
                    // in BOTH directions so a one-way short-circuit (e.g. `sign = min(field, tip)`)
                    // cannot pass by accident.
                    signField.SetValue(ec, -1f);
                    tipSignField.SetValue(ec, 1f);
                    float upDot = Vector3.Dot(LongAxisWorld(method, ec, socket, mainSide).normalized, body.up);
                    signField.SetValue(ec, shippedSign);
                    tipSignField.SetValue(ec, 0f);

                    if (downDot > -0.99f)
                        failures.Add("G2: a MEASURED sign of -1 did not hang the prop tip-down " +
                                     "(dot(bodyUp) = " + downDot.ToString("0.###") + ", expected ~-1) while " +
                                     "the global field said +1. The per-mesh answer is being ignored, so " +
                                     "every prop still hangs by one shared guess.");
                    else if (upDot < 0.99f)
                        failures.Add("G2: a MEASURED sign of +1 did not hang the prop tip-up " +
                                     "(dot(bodyUp) = " + upDot.ToString("0.###") + ", expected ~+1) while " +
                                     "the global field said -1. The per-mesh answer only wins in one " +
                                     "direction, which is not winning.");
                    else
                        log.AppendLine("  G2 the MEASURED per-mesh sign outranks the field ........ ok");
                }

                // ── G3: the global field is still the FALLBACK, and still flips ──────
                // §12: the tuning seam is not deleted just because it stopped being the authority.
                // With NO measurement available (_sheatheTipSign = 0) the field must still decide,
                // and must still flip the carry end-for-end — otherwise an unmeasurable prop has no
                // way to be corrected at all.
                if (tipSignField != null) tipSignField.SetValue(ec, 0f);
                signField.SetValue(ec, -1f);
                Vector3 flipped = LongAxisWorld(method, ec, socket, mainSide);
                signField.SetValue(ec, 1f);
                Vector3 unflipped = LongAxisWorld(method, ec, socket, mainSide);
                signField.SetValue(ec, shippedSign);
                float flippedDot = Vector3.Dot(flipped.normalized, body.up);
                float unflippedDot = Vector3.Dot(unflipped.normalized, body.up);
                if (flippedDot > -0.99f || unflippedDot < 0.99f)
                    failures.Add("G3: with no measured sign, _sheatheLongAxisSign no longer flips the carry " +
                                 "(dot at -1 = " + flippedDot.ToString("0.###") + ", at +1 = " +
                                 unflippedDot.ToString("0.###") + "; expected ~-1 / ~+1). The fallback seam " +
                                 "was stripped or short-circuited, so a prop the geometry cannot answer for " +
                                 "has no correction left.");
                else
                    log.AppendLine("  G3 the fallback field still flips when nothing is measured  ok");

                // ── G4: TEETH. Re-introduce the diagonal; G1's rule must now FAIL ────
                diagonalField.SetValue(ec, 28f);
                Vector3 diagonalLong = LongAxisWorld(method, ec, socket, mainSide);
                diagonalField.SetValue(ec, shippedDiagonal);
                float diagonalTilt = Vector3.Angle(diagonalLong, diagonalLong.y >= 0f ? body.up : -body.up);
                if (diagonalTilt <= AngleTolDeg)
                    failures.Add("G4: restoring the 28 deg baldric diagonal left the long axis reading vertical " +
                                 "(" + diagonalTilt.ToString("0.#") + " deg). The vertical assertion is therefore " +
                                 "measuring nothing and would not have caught the reported defect.");
                else
                    log.AppendLine("  G4 the retired 28 deg diagonal is REJECTED (" +
                                   diagonalTilt.ToString("0.#") + " deg off) ... ok");

                // ── G5a: the two slots hang on OPPOSITE sides ────────────────────────
                Quaternion mainLocal = (Quaternion)method.Invoke(ec, new object[] { socket, mainSide });
                Quaternion offLocal = (Quaternion)method.Invoke(ec, new object[] { socket, offSide });
                Vector3 mainFlat = (socket.rotation * mainLocal) * Vector3.forward;
                Vector3 offFlat = (socket.rotation * offLocal) * Vector3.forward;
                float sideDot = Vector3.Dot(mainFlat.normalized, offFlat.normalized);
                if (sideDot > -0.9f)
                    failures.Add("G5a: the main-hand and off-hand sheathe poses face the SAME way " +
                                 "(dot = " + sideDot.ToString("0.###") + ", expected ~-1). The two hips are " +
                                 "supposed to mirror; identical poses mean the side argument is ignored.");
                else
                    log.AppendLine("  G5a main/off hips mirror (dot=" + sideDot.ToString("0.##") + ") ......... ok");
            }
            catch (Exception e)
            {
                failures.Add("G: driving the shipped ComputeSheathRotation threw — " + e.GetType().Name +
                             ": " + e.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static Vector3 LongAxisWorld(MethodInfo method, EquipmentController ec, Transform socket, float side)
        {
            var local = (Quaternion)method.Invoke(ec, new object[] { socket, side });
            // Prop-local +Y is the long axis (NormalizeInto + SeatHiltLowerHalf put it there);
            // the pose is returned in the SOCKET's local frame, so compose the socket to reach world.
            return (socket.rotation * local) * Vector3.up;
        }

        private static void CheckShieldMountRule(List<string> failures, StringBuilder log)
        {
            GameObject probe = null;
            try
            {
                probe = new GameObject("SheatheShieldProbe");
                probe.transform.rotation = Quaternion.Euler(0f, -113f, 0f);
                Transform body = probe.transform;
                var socket = new GameObject("SheatheSocket_HipOff").transform;
                socket.SetParent(body, false);
                socket.localRotation = Quaternion.Euler(41f, -12f, 96f);

                // A synthetic plate: thickness on X, long axis on Y — the frame the runtime
                // MEASURES off the mesh (WeaponOrientHelper.TryResolveShieldFrame). Synthesised
                // here because a headless suite has no shield mesh; the SOLVE under test is the
                // shipped one either way.
                var frame = new WeaponOrientHelper.ShieldFrame
                {
                    Valid = true,
                    HandleResolved = true,
                    HandleOnPositiveSide = false,
                    ThicknessAxis = Vector3.right,
                    LongAxis = Vector3.up,
                };

                // ⛔ NOT INVERTED. This read `-body.up`, "inverted, matching the sword", and that
                // generalisation is the defect the owner reported on 2026-08-20 ("redo the sword and
                // sheild. not working"). Her instruction — "sheathed should sit inverted with the
                // longest mesh (y) up and down" — is about a SWORD: the long axis is the blade and
                // inverted means tip-down. A shield has no tip and no meaningful end-for-end, so the
                // extra constraint buys nothing and only gives a mis-measured axis a second way to
                // decide the pose. The shield keeps the felt-approved WO-1123 rule (thickness away
                // from the player, handle inward), now applied at the HIP anchor.
                Vector3 outward = body.right;    // the hip side: away from the leg
                Vector3 longUp = body.up;        // plain up — the sword's inversion is sword-only

                Quaternion local = WeaponOrientHelper.ComputeShieldMountRotation(frame, socket, outward, longUp);
                Quaternion world = socket.rotation * local;
                float faceOff = Vector3.Angle(world * frame.ThicknessAxis, outward);
                float longOff = Vector3.Angle(world * frame.LongAxis, longUp);

                if (longOff > AngleTolDeg)
                    failures.Add("G6: the sheathed SHIELD's long axis sits " + longOff.ToString("0.#") +
                                 " deg off the vertical it was handed — the solve did not honour the " +
                                 "second axis it was given, so the roll is unresolved.");
                else if (faceOff > AngleTolDeg)
                    failures.Add("G6: the sheathed SHIELD's thickness axis sits " + faceOff.ToString("0.#") +
                                 " deg off the hip's outward direction, so its face is not turned away from " +
                                 "the player.");
                else
                    log.AppendLine("  G6 shield: face outward, long axis vertical .............. ok");

                // TEETH: the RETIRED inputs (thickness along -body.forward, the BACK mount) must
                // NOT satisfy the hip rule. If they do, this case is measuring the solver's
                // internals rather than the pose, and the shield could go back to facing the wrong
                // way with the suite still green.
                Quaternion backLocal = WeaponOrientHelper.ComputeShieldMountRotation(frame, socket, -body.forward, body.up);
                float backFaceOff = Vector3.Angle((socket.rotation * backLocal) * frame.ThicknessAxis, outward);
                if (backFaceOff <= AngleTolDeg)
                    failures.Add("G7: the retired BACK-mount inputs (-body.forward) produce the same outward " +
                                 "face as the hip inputs, so G6 cannot tell the two poses apart.");
                else
                    log.AppendLine("  G7 the retired back-mount inputs are REJECTED (" +
                                   backFaceOff.ToString("0.#") + " deg off) ... ok");
            }
            catch (Exception e)
            {
                failures.Add("G6/G7: driving ComputeShieldMountRotation threw — " + e.GetType().Name +
                             ": " + e.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        // =====================================================================
        //  G8/G9 — THE RENDERED SHAPE, not the euler
        // =====================================================================
        //
        // WHY THIS CASE EXISTS, in one sentence: G6 passed while the shield rendered FLAT.
        //
        // The owner's capture (2026-08-20, pid 32572) is the whole argument:
        //   sheathed shield DERIVED: faceOffOutward=0deg longTiltFromVertical=0deg longAxisDotUp=-1
        //   MEASURED after hold: worldEuler=(90.00, 105.00, 0.00) worldBounds=s(0.92, 0.20, 0.81)
        // Every angle read perfect and the prop was a dinner plate at the hip — because the angles
        // are measured against the FRAME's axes, and the frame had named the mesh's longest extent
        // as its "thickness". An assertion phrased in degrees cannot separate "posed correctly" from
        // "posed correctly with respect to a lie". An assertion phrased in RENDERED VOLUME can: a
        // 0.20 m vertical extent on a 0.78 m plate is a collapse no wrong frame can talk its way out
        // of. So this case drives the REAL measurement (TryResolveShieldFrame) and the REAL solve on
        // a real MeshRenderer, at a hostile attach rotation, and asserts the SHAPE that comes out.
        //
        // The hostile attach rotation is the point, not decoration: the retired measurement read the
        // renderer's WORLD AABB and re-expressed its extents vector in the parent basis, which is
        // only correct when the prop is axis-aligned with that parent. Measured at identity the old
        // code passes; measured at a rotated seat — which is where the game measures, because the
        // prop is on the hand — it mis-orders the axes. G9 pins that the collapse is detectable.
        //
        // ⚠ AND THE SHAPE IS MEASURED ALONG THE DIRECTIONS THAT MEAN SOMETHING, NOT ALONG WORLD X/Y/Z.
        // The first draft of G8b asked whether ANY world-AABB axis was thin, and it red-flagged a
        // CORRECTLY posed shield at 0.46 m — because the fixture's body is yawed 64 deg, so the
        // 0.20 m thinness splits across world X and Z and no world axis is thin. An AABB is three
        // fixed directions; the pose is about `outward` and `up`. Projecting onto those two (see
        // ExtentAlong) is exact under any body or socket rotation. The world-Y clause is KEPT
        // alongside them because vertical extent is the one AABB number a yaw cannot smear, and it
        // is the number the device capture actually prints — the oracle should speak the log's
        // language as well as the geometry's.
        private const float PlateThickness = 0.20f;   // the live shield's measured thinness, to scale
        private const float PlateWidth = 0.63f;
        private const float PlateHeight = 0.78f;

        private static void CheckSheathedShieldRendersAsAPlate(List<string> failures, StringBuilder log)
        {
            GameObject probe = null;
            try
            {
                probe = new GameObject("SheatheShieldShapeProbe");
                // Body yaw ONLY. A pitched body would tilt the world Y axis and smear the vertical
                // extent, which would make this case an assertion about the fixture instead of about
                // the pose. The socket below still carries a hostile full rotation, which is what
                // proves the solve is anchor-independent.
                probe.transform.rotation = Quaternion.Euler(0f, 64f, 0f);
                Transform body = probe.transform;

                var socket = new GameObject("SheatheSocket_HipOff").transform;
                socket.SetParent(body, false);
                socket.localRotation = Quaternion.Euler(41f, -12f, 96f);

                // The grip root sits at a ROTATED seat, exactly as it does on the hand when the
                // runtime takes its one measurement.
                var gripRoot = new GameObject("EquipmentProp_OffHand").transform;
                gripRoot.SetParent(socket, false);
                gripRoot.localRotation = Quaternion.Euler(292f, 140f, 105f);

                // A real renderer with a real mesh: thickness on Z, long axis on Y, width on X —
                // the live shield's proportions (back-solved from the owner's capture).
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "ShieldPlate";
                plate.transform.SetParent(gripRoot, false);
                plate.transform.localRotation = Quaternion.identity;
                plate.transform.localScale = new Vector3(PlateWidth, PlateHeight, PlateThickness);

                // ── G8a: the MEASUREMENT names the real axes despite the rotated seat ────────
                if (!WeaponOrientHelper.TryResolveShieldFrame(plate, gripRoot, out var frame) || !frame.Valid)
                {
                    failures.Add("G8: TryResolveShieldFrame could not measure a plain 0.63 x 0.78 x 0.20 " +
                                 "plate at a rotated seat. Every shield pose downstream falls back to a " +
                                 "hand-typed euler when this returns false.");
                    return;
                }

                bool axesRight = frame.Axes.NarrowestAxis == 2 && frame.Axes.LongestAxis == 1;
                if (!axesRight)
                    failures.Add("G8a: the plate measures narrowest=" + AxisLetter(frame.Axes.NarrowestAxis) +
                                 " longest=" + AxisLetter(frame.Axes.LongestAxis) + " (" + frame.Axes.Describe() +
                                 ") but it was built 0.63 x 0.78 x 0.20 — narrowest MUST be Z and longest Y. " +
                                 "A frame that mis-names the axes poses the prop perfectly about a lie: that " +
                                 "is the 2026-08-20 flat shield, and it comes from measuring the WORLD AABB " +
                                 "of a prop that is sitting at a rotated seat.");
                else
                    log.AppendLine("  G8a plate measured correctly at a rotated seat ........... ok");

                // ── G8b: the RENDERED VOLUME is still a plate after the sheathed pose ───────
                Vector3 outward = body.right;
                gripRoot.localRotation = WeaponOrientHelper.ComputeShieldMountRotation(
                    frame, socket, outward, body.up);

                // ⛔ DO NOT ASK A WORLD AABB WHETHER THE PLATE IS THIN. A clause here once read
                // `minExtent > PlateThickness * 2f` — "no axis of the rendered shield is thin" — and
                // it failed on a CORRECTLY posed shield, reporting 0.46 m. That number is not a bug
                // in the pose and not a bad threshold to nudge; it is arithmetic:
                //
                //   the fixture's body is yawed 64 deg, so with the thin axis pointing along
                //   body.right the 0.20 m thickness projects onto BOTH world X and world Z
                //     world X extent = 0.899*0.63 + 0.438*0.20 = 0.654
                //     world Y extent =               1.000*0.78 = 0.780
                //     world Z extent = 0.438*0.63 + 0.899*0.20 = 0.456   <- the reported 0.46
                //
                // A world-axis-aligned box around a rotated plate never exposes the plate's thinness
                // unless the plate happens to be square-on to the world axes. The owner's capture DID
                // read 0.20 — but in world Y, and only because there the thin axis was standing
                // exactly vertical (worldEuler pitch 90), and the VERTICAL extent is the one quantity
                // a yaw cannot smear. So: the vertical clause below is kept, because it is both
                // yaw-invariant and literally the number the device log prints; the "some axis is
                // thin" clause is replaced by extents measured along the axes that MEAN something —
                // outward and up. Those are exact under any body/socket rotation, which makes them
                // strictly stronger than the AABB clause they replace, not a relaxation of it.
                float alongOutward = ExtentAlong(plate, outward);   // must be the THICKNESS
                float alongUp = ExtentAlong(plate, body.up);        // must be the HEIGHT
                Vector3 size = WorldExtents(plate);

                if (alongOutward > PlateThickness * 1.5f)
                    failures.Add("G8b: the shield measures " + alongOutward.ToString("0.##") + " m across " +
                                 "the OUTWARD direction, but its thickness is only " +
                                 PlateThickness.ToString("0.##") + " m. The face is not turned away from " +
                                 "the player — an edge is. This is the dinner-plate defect stated in the " +
                                 "one direction that cannot be smeared by which way the hero happens to face.");
                else if (alongOutward < PlateThickness * 0.5f)
                    failures.Add("G8b: the shield measures only " + alongOutward.ToString("0.###") + " m " +
                                 "across the outward direction against a " + PlateThickness.ToString("0.##") +
                                 " m thickness - the prop has been squashed, not seated.");
                else if (alongUp < PlateHeight * 0.9f)
                    failures.Add("G8b: the shield stands only " + alongUp.ToString("0.##") + " m tall along " +
                                 "the body's up axis from a " + PlateHeight.ToString("0.##") + " m plate. Its " +
                                 "long axis is not upright, so it is hanging on a diagonal or on its side.");
                else if (size.y < PlateHeight * 0.7f)
                    failures.Add("G8b: the sheathed shield renders only " + size.y.ToString("0.##") +
                                 " m tall in WORLD Y from a " + PlateHeight.ToString("0.##") + " m plate. It " +
                                 "is lying FLAT — the exact defect the owner reported, and the exact shape " +
                                 "the capture measured as s(0.92, 0.20, 0.81) while every angle read 0 deg.");
                else
                    log.AppendLine("  G8b sheathed shield renders as a plate (thickness-out=" +
                                   alongOutward.ToString("0.##") + "m upright=" + alongUp.ToString("0.##") +
                                   "m worldY=" + size.y.ToString("0.##") + "m) ... ok");

                // ── G9: TEETH. Feed the solve the SWAPPED frame the old measurement produced and
                // prove the shape check REJECTS it. Without this, G8b could be passing because the
                // fixture cannot fail rather than because the pose is right.
                var swapped = frame;
                swapped.ThicknessAxis = frame.LongAxis;
                swapped.LongAxis = frame.ThicknessAxis;
                gripRoot.localRotation = WeaponOrientHelper.ComputeShieldMountRotation(
                    swapped, socket, outward, body.up);
                // Both of G8b's live clauses are re-run against it, not just one: after the 0.46
                // correction the PRIMARY clause is the outward extent, so proving only that the
                // vertical clause bites would leave the clause that actually guards the pose
                // unexercised. Expected on a swapped frame: outward reads the 0.78 long axis
                // (should trip the >1.5x thickness clause) and world Y collapses to the 0.20
                // thin axis (should trip the vertical clause).
                float badOutward = ExtentAlong(plate, outward);
                float badHeight = WorldExtents(plate).y;
                bool outwardClauseBites = badOutward > PlateThickness * 1.5f;
                bool verticalClauseBites = badHeight < PlateHeight * 0.7f;

                if (!outwardClauseBites)
                    failures.Add("G9: posing the plate off a SWAPPED frame (thickness<->long, which is " +
                                 "precisely what the retired world-AABB measurement produced) leaves only " +
                                 badOutward.ToString("0.##") + " m across the outward direction, so G8b's " +
                                 "PRIMARY clause does not fire. That clause is the one guarding the pose; " +
                                 "if it cannot detect a swapped frame it is not guarding anything.");
                else if (!verticalClauseBites)
                    failures.Add("G9: a SWAPPED frame still renders " + badHeight.ToString("0.##") + " m tall " +
                                 "in world Y, so G8b's vertical clause — the one phrased in the same number " +
                                 "the device capture prints — cannot detect the collapse it exists to detect.");
                else
                    log.AppendLine("  G9 a swapped frame is REJECTED by BOTH clauses (outward=" +
                                   badOutward.ToString("0.##") + "m worldY=" + badHeight.ToString("0.##") +
                                   "m) ... ok");
            }
            catch (Exception e)
            {
                failures.Add("G8/G9: the rendered-shape probe threw — " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// The world-axis-aligned size of a renderer's mesh — computed from the mesh's own corners
        /// rather than read off Renderer.bounds. Identical by definition, and deterministic: outside
        /// Play mode a renderer's cached bounds can lag a transform written in the same frame, and a
        /// stale read here would make this case flicker instead of assert.
        /// </summary>
        private static Vector3 WorldExtents(GameObject go)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return Vector3.zero;
            Bounds mb = filter.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            for (int corner = 0; corner < 8; corner++)
            {
                var p = new Vector3(
                    c.x + ((corner & 1) == 0 ? -e.x : e.x),
                    c.y + ((corner & 2) == 0 ? -e.y : e.y),
                    c.z + ((corner & 4) == 0 ? -e.z : e.z));
                Vector3 w = go.transform.TransformPoint(p);
                if (corner == 0) { min = max = w; }
                else { min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
            }
            return max - min;
        }

        /// <summary>
        /// The prop's extent along an ARBITRARY world direction — the span of its mesh corners
        /// projected onto that axis. This is the measurement a world AABB cannot give you: an AABB
        /// is only ever three fixed world directions, so a plate that is thin along `outward` but
        /// yawed away from the world axes reports a fat minimum extent (0.46 m on this fixture) and
        /// looks nothing like a plate. Projecting onto the direction the pose was ASKED to satisfy
        /// is exact under any body or socket rotation, which is what makes G8b both strict and stable.
        /// </summary>
        private static float ExtentAlong(GameObject go, Vector3 axis)
        {
            var filter = go != null ? go.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) return 0f;
            if (axis.sqrMagnitude < 1e-9f) return 0f;
            axis = axis.normalized;

            Bounds mb = filter.sharedMesh.bounds;
            Vector3 c = mb.center, e = mb.extents;
            float min = float.MaxValue, max = float.MinValue;
            for (int corner = 0; corner < 8; corner++)
            {
                var p = new Vector3(
                    c.x + ((corner & 1) == 0 ? -e.x : e.x),
                    c.y + ((corner & 2) == 0 ? -e.y : e.y),
                    c.z + ((corner & 4) == 0 ? -e.z : e.z));
                float d = Vector3.Dot(go.transform.TransformPoint(p), axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            return max - min;
        }

        private static string AxisLetter(int axis) => axis == 0 ? "X" : axis == 1 ? "Y" : "Z";

        // =====================================================================
        //  SOURCE HELPERS
        // =====================================================================

        /// <summary>
        /// Returns the brace-balanced body that follows <paramref name="signatureFragment"/>,
        /// or null. Operates on comment-blanked (and, for brace safety, ideally string-blanked)
        /// source, so an interpolated string's braces cannot unbalance the scan.
        /// </summary>
        private static string ExtractMethodBody(string source, string signatureFragment)
        {
            if (string.IsNullOrEmpty(source)) return null;
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            while (at >= 0)
            {
                int open = source.IndexOf('{', at);
                if (open < 0) return null;
                // An expression-bodied forwarder (`=> Other(...);`) is not the body we want;
                // skip past it and keep looking for the real one.
                int arrow = source.IndexOf("=>", at, StringComparison.Ordinal);
                int semi = source.IndexOf(';', at);
                if (arrow >= 0 && arrow < open && (semi < 0 || arrow < semi))
                {
                    at = source.IndexOf(signatureFragment, semi > at ? semi : at + signatureFragment.Length,
                                        StringComparison.Ordinal);
                    continue;
                }
                int depth = 0;
                for (int i = open; i < source.Length; i++)
                {
                    if (source[i] == '{') depth++;
                    else if (source[i] == '}')
                    {
                        depth--;
                        if (depth == 0) return source.Substring(open, i - open + 1);
                    }
                }
                return null;
            }
            return null;
        }

        private static string ReadOrEmpty(string path)
        {
            try { return File.ReadAllText(path); } catch { return string.Empty; }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CASE D2 — A FLAG DEFAULT MUST NOT UN-DO THE DERIVED POSE EITHER
        // ─────────────────────────────────────────────────────────────────────
        // D1's sibling, and found the same night by the same capture. With the two stale absolute
        // rows deleted, the sheathed sword STILL read 81 deg off vertical and TIP UP. The trace:
        //
        //   [Flow:Equip]  sheathed long axis ... tiltFromVertical=0deg longAxisDotUp=-1
        //   [Flow:Offset] sheathed FALLBACK (drawn 'sword_A' on back pose):
        //                 pos=(0.01,0.03,-0.01) rot=(117.00,-2.00,110.00)
        //
        // With NO explicit @sheathed row, ApplySheathedOffset falls back to the DRAWN row — and
        // ff.sheathdrawnrot decides whether that row's ROTATION composes too. That euler was
        // authored in the HAND BONE's frame; composing it onto a hip-socket pose is a frame
        // mismatch, which the flag's own documentation said in as many words while its DEFAULT
        // said the opposite. It defaulted ON from a 2026-07-07 owner A/B — an experiment's setting
        // left switched on, defending a BACK carry that the 2026-08-20 ruling then retired.
        //
        // THE INVARIANT: with no explicit sheathed authoring, the derived pose is the pose. A
        // position-only nudge is fine; a rotation compose across frames is not. The flag stays
        // (never strip a seam) — this pins its DEFAULT.
        private static void CheckSheathedDefaultsDoNotOverrideDerivation(List<string> failures, StringBuilder log)
        {
            // Read the DEFAULT, not the machine's current PlayerPrefs value: a dev who flipped the
            // flag locally must not turn this rule off for everyone. The default is the shipped
            // literal in FeatureFlags.cs, so that is what is asserted.
            string flagsPath;
            try { flagsPath = Path.Combine(Application.dataPath, "_Modules/Core/FeatureFlags.cs".Replace('/', Path.DirectorySeparatorChar)); }
            catch { failures.Add("D2: could not resolve FeatureFlags.cs — the sheathed-rotation default is UNVERIFIED."); return; }
            string src = BlankComments(ReadOrEmpty(flagsPath));
            if (string.IsNullOrEmpty(src))
            {
                failures.Add("D2: FeatureFlags.cs not readable — the sheathed-rotation default is UNVERIFIED.");
                return;
            }
            const string Needle = "Get(\"sheathdrawnrot\"";
            int at = src.IndexOf(Needle, StringComparison.Ordinal);
            if (at < 0)
            {
                failures.Add("D2: no Get(\"sheathdrawnrot\", ...) in FeatureFlags.cs. The sheathed-rotation " +
                             "seam was renamed or removed; this rule can no longer see the default it guards.");
                return;
            }
            int close = src.IndexOf(')', at);
            string call = close > at ? src.Substring(at, close - at) : src.Substring(at);
            if (call.Replace(" ", string.Empty).IndexOf("defaultOn:true", StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("D2: ff.sheathdrawnrot defaults ON again. That composes the DRAWN offset row's " +
                             "hand-frame euler onto the derived HIP pose for every player who has never " +
                             "touched the flag — measured on the live Knight as a sheathed sword 81 deg off " +
                             "vertical and TIP UP, i.e. exactly the carry the owner rejected on 2026-08-20. " +
                             "Position-only is the frame-safe fallback; keep the flag, keep the default OFF.");
            else
                log.AppendLine("  D2 ff.sheathdrawnrot defaults OFF (frame-safe) .......... ok");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  CASE P1 — THE SHEATHED SHIELD IS CENTRED ON THE HIP
        // ─────────────────────────────────────────────────────────────────────
        // The third defect from the same capture, and the one every ANGLE-based case in this file
        // is blind to. G6/G8 both read perfect — "faceOffOutward=0deg longTiltFromVertical=0deg" —
        // while the shield floated at CHEST height with backdrop visible between it and the torso.
        //
        // Cause: the live shield is a NATIVE prop, so its origin is its GRIP, and its grip is at the
        // plate's BOTTOM EDGE (fantasy_shield localBounds c=(0, 0.315, 0.035) s=(0.512, 0.63,
        // 0.161)). Seating the ORIGIN at the hip hangs the whole 0.63 m plate upward from it.
        // EquipmentController.ApplyOffHandCentreOnSocket now shifts by the prop's own
        // origin-to-rendered-centre vector so the plate's MIDDLE lands on the hip.
        //
        // This case drives that SHIPPED method (reflection) on a real renderer whose pivot is at its
        // bottom edge — the shield's actual pathology — and asserts the rendered centre lands where
        // the seat put the origin. It also asserts the method is IDEMPOTENT, because ApplyHoldPose
        // re-asserts the pose every frame and a shift that accumulated would walk the shield away
        // over a few seconds of play, which no single screenshot would ever catch.
        private static void CheckSheathedOffHandIsCentredOnSocket(List<string> failures, StringBuilder log)
        {
            GameObject probe = null;
            try
            {
                probe = new GameObject("SheatheOffHandCentreProbe");
                probe.transform.rotation = Quaternion.Euler(0f, 37f, 0f);

                var socket = new GameObject("SheatheSocket_HipOff").transform;
                socket.SetParent(probe.transform, false);
                socket.localRotation = Quaternion.Euler(23f, -47f, 61f);   // a hostile, rig-arbitrary bone frame
                socket.localScale = Vector3.one * 1.67f;                   // the CC rig's real bone lossyScale

                var gripRoot = new GameObject("EquipmentProp_OffHand").transform;
                gripRoot.SetParent(socket, false);
                Vector3 baseLocal = new Vector3(0.12f, 0.03f, -0.02f);   // what the seat computed
                gripRoot.localPosition = baseLocal;
                gripRoot.localRotation = Quaternion.Euler(11f, 200f, 349f);
                Vector3 seatedOrigin = gripRoot.position;   // where the seat PUT the prop

                // A plate whose PIVOT IS ITS BOTTOM EDGE — a cube child pushed up by half its height
                // reproduces exactly the fantasy_shield pathology without needing the asset.
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "ShieldPlate";
                plate.transform.SetParent(gripRoot, false);
                plate.transform.localScale = new Vector3(PlateWidth, PlateHeight, PlateThickness);
                plate.transform.localPosition = new Vector3(0f, PlateHeight * 0.5f, 0f);

                var rend = plate.GetComponent<Renderer>();
                float offBefore = Vector3.Distance(rend.bounds.center, seatedOrigin);
                if (offBefore < PlateHeight * 0.3f)
                {
                    failures.Add("P1: the fixture's plate is already centred on its pivot (" +
                                 offBefore.ToString("0.###") + " m) — it cannot demonstrate the bug it " +
                                 "was built for, so a passing result here would mean nothing.");
                    return;
                }

                var mi = typeof(EquipmentController).GetMethod("ApplyOffHandCentreOnSocket",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi == null)
                {
                    failures.Add("P1: EquipmentController.ApplyOffHandCentreOnSocket is GONE. Without it a " +
                                 "grip-at-origin shield hangs its whole plate upward from the hip and reads as " +
                                 "floating at the chest, with every angle in this suite still green.");
                    return;
                }
                var holder = new GameObject("~equipHolder");
                holder.transform.SetParent(probe.transform, false);
                var ctrl = holder.AddComponent<EquipmentController>();   // no Awake outside play mode

                mi.Invoke(ctrl, new object[] { gripRoot, socket, baseLocal });
                float offAfter = Vector3.Distance(rend.bounds.center, seatedOrigin);
                if (offAfter > CentreToleranceM)
                    failures.Add("P1: after ApplyOffHandCentreOnSocket the plate's rendered centre is still " +
                                 offAfter.ToString("0.###") + " m from the seated point (was " +
                                 offBefore.ToString("0.###") + " m, tolerance " + CentreToleranceM.ToString("0.##") +
                                 " m). A grip-at-origin shield is still hanging by its bottom edge — the " +
                                 "2026-08-20 chest-float.");
                else
                    log.AppendLine("  P1 sheathed shield centres on the hip (" + offBefore.ToString("0.##") +
                                   " -> " + offAfter.ToString("0.##") + " m) ...... ok");

                // IDEMPOTENT: re-asserting the pose must not walk the prop away.
                mi.Invoke(ctrl, new object[] { gripRoot, socket, baseLocal });
                mi.Invoke(ctrl, new object[] { gripRoot, socket, baseLocal });
                float offRepeat = Vector3.Distance(rend.bounds.center, seatedOrigin);
                if (Mathf.Abs(offRepeat - offAfter) > CentreToleranceM)
                    failures.Add("P1b: repeating the centring moved the plate again (" +
                                 offAfter.ToString("0.###") + " -> " + offRepeat.ToString("0.###") + " m). " +
                                 "ApplyHoldPose re-asserts the sheathed pose EVERY FRAME, so a shift that " +
                                 "accumulates walks the shield off the hero during play — and no single " +
                                 "screenshot would ever show it.");
                else
                    log.AppendLine("  P1b centring is idempotent across re-asserts .......... ok");

                // DEVICE RCA 2026-08-29: centring alone can still hide the whole plate because
                // SheatheSocket_ArmOff is an arm BONE, not the skin surface.  Reproduce the live
                // path's measured frame and prove the surface pass moves the centred plate by its
                // half-thickness plus skin clearance.  An enabled renderer at the centred point is
                // exactly the false-green state the installed Seeker build reported.
                var frame = new WeaponOrientHelper.ShieldFrame
                {
                    Valid = true,
                    ThicknessAxis = Vector3.forward,
                    Axes = new MeasuredAxes { NarrowestLen = PlateThickness }
                };
                var frameField = typeof(EquipmentController).GetField("_currentOffHandShieldFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var surfaceMi = typeof(EquipmentController).GetMethod("ApplyOffHandArmSurfaceSeat",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (frameField == null || surfaceMi == null)
                {
                    failures.Add("P1c: arm surface-seat seam is missing. A shield can have an enabled " +
                                 "renderer centred inside SheatheSocket_ArmOff and remain invisible.");
                }
                else
                {
                    frameField.SetValue(ctrl, frame);
                    Vector3 centreBeforeSurface = rend.bounds.center;
                    surfaceMi.Invoke(ctrl, new object[] { gripRoot, socket });
                    float surfaceShift = Vector3.Distance(centreBeforeSurface, rend.bounds.center);
                    float expectedMin = PlateThickness * gripRoot.lossyScale.z * 0.5f + 0.039f;
                    if (surfaceShift < expectedMin)
                        failures.Add("P1c: arm surface seat moved only " + surfaceShift.ToString("0.###") +
                                     " m (expected at least " + expectedMin.ToString("0.###") +
                                     " m = measured half-thickness + skin clearance). The plate can " +
                                     "still remain buried through the hero's arm.");
                    else
                        log.AppendLine("  P1c centred shield inner face clears arm skin (" +
                                       surfaceShift.ToString("0.##") + " m) .......... ok");
                }

                // P1d — the exact 346834 device failure: the renderer and transform survived,
                // but its Addressables-owned sharedMesh became null by the time combat drew it.
                // Prove the attach seam replaces that dependency with a runtime-owned clone.
                var preserveMi = typeof(EquipmentController).GetMethod("PreserveOffHandRenderAssets",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (preserveMi == null)
                {
                    failures.Add("P1d: Addressable render-asset preservation seam is missing; an " +
                                 "enabled shield renderer can later survive with sharedMesh=null.");
                }
                else
                {
                    var sourceMesh = new Mesh { name = "AddressableOwnedShieldMesh" };
                    sourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                    sourceMesh.triangles = new[] { 0, 1, 2 };
                    var ownedProbe = new GameObject("AddressableShieldDependency");
                    ownedProbe.transform.SetParent(gripRoot, false);
                    var ownedFilter = ownedProbe.AddComponent<MeshFilter>();
                    ownedProbe.AddComponent<MeshRenderer>();
                    ownedFilter.sharedMesh = sourceMesh;
                    int preserved = (int)preserveMi.Invoke(ctrl,
                        new object[] { ownedProbe, "knight_shield_starter" });
                    Mesh runtimeMesh = ownedFilter.sharedMesh;
                    UnityEngine.Object.DestroyImmediate(sourceMesh);
                    if (preserved < 1 || runtimeMesh == null || runtimeMesh.name.IndexOf("RuntimeOffHand",
                            StringComparison.Ordinal) < 0 || runtimeMesh.vertexCount != 3)
                        failures.Add("P1d: preserving an Addressable shield did not leave a runtime-owned " +
                                     "mesh after its source dependency was destroyed. Combat can still " +
                                     "turn a healthy attach into MeshRenderer+sharedMesh=null.");
                    else
                        log.AppendLine("  P1d shield mesh survives Addressables source eviction .......... ok");
                }
            }
            catch (Exception e)
            {
                failures.Add("P1: driving ApplyOffHandCentreOnSocket threw — " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>Slack on the centring assertion. The live plate is 0.63 m tall, so the defect it
        /// pins is a ~0.32 m error; 0.02 m is float noise through a scaled, rotated bone chain.</summary>
        private const float CentreToleranceM = 0.02f;

        // ─────────────────────────────────────────────────────────────────────
        //  CASE D1 — THE SHIPPED DATA MUST NOT OUTRANK THE DERIVED HIP POSE
        // ─────────────────────────────────────────────────────────────────────
        // WHY THIS CASE EXISTS, and why it is a DATA lint in a maths suite.
        //
        // Every G-case above passed while the sheathed sword hung diagonally, off the body, on
        // the WRONG hip — because the maths was never the thing that reached the screen. The
        // KnightGearProof play-mode capture (2026-08-20, Builds/KnightGearProof/) caught it in
        // two consecutive trace lines on the live Knight:
        //
        //   [Flow:Equip]  sheathed long axis on 'Hero (Grom)': tiltFromVertical=0deg
        //                 longAxisDotUp=-1 ... socket='SheatheSocket_HipMain'.
        //   [Flow:Offset] sheathed offset 'sword_A@sheathed' applied:
        //                 pos=(0.23,-0.14,0.12) rot=(180.00,-28.00,-51.00) full=True
        //
        // The first line is this suite's rule, computed correctly. The second is
        // ApplySheathedOffset REPLACING it — an `Explicit + fullOverride` row is ABSOLUTE, so it
        // overwrites localPosition AND localRotation outright. That row shipped in
        // Assets/Resources/OffsetForge/offsets.json (and its Assets/OffsetForge/ twin), authored
        // against the RETIRED spine/back socket; its -28 is literally the baldric diagonal G4
        // rejects. Its POSITIVE x also put the sword on the shield's hip, which is how both props
        // ended up on one side while S1 proved the sides were opposite by construction.
        //
        // THE INVARIANT: an absolute sheathed pose is only meaningful in the frame it was
        // authored in, and that frame moved from spine to hip. A SHIPPED one therefore cannot be
        // trusted and silently disables the derivation for every player at once. The owner's own
        // felt-tunes are not affected — the Seating Editor writes to persistentDataPath
        // (AttachmentOffsetRegistry.DevPath), not to these files.
        private static void CheckNoShippedAbsoluteSheathedRow(List<string> failures, StringBuilder log)
        {
            string[] rel =
            {
                "Resources/OffsetForge/offsets.json",   // the one the RUNTIME reads
                "OffsetForge/offsets.json",             // the Forge's authoring twin, kept in sync
            };
            int checkedFiles = 0;
            int before = failures.Count;
            foreach (string r in rel)
            {
                string path;
                try { path = Path.Combine(Application.dataPath, r.Replace('/', Path.DirectorySeparatorChar)); }
                catch { continue; }
                string json = ReadOrEmpty(path);
                if (string.IsNullOrEmpty(json)) continue;
                checkedFiles++;
                foreach (string id in AbsoluteSheathedIds(json))
                    failures.Add("D1: Assets/" + r + " ships an ABSOLUTE sheathed override '" + id +
                                 "' (fullOverride=true). ApplySheathedOffset applies that as an absolute " +
                                 "pos+rot in the sheathe SOCKET's frame, which REPLACES the derived hip " +
                                 "carry every G-case here asserts — so this suite would stay green while " +
                                 "the sword hangs diagonally on the wrong hip, exactly as it did on " +
                                 "2026-08-20. Delete the row (the derivation is the pose) or, if it is a " +
                                 "deliberate felt-tune, author it in the Seating Editor so it lands in " +
                                 "persistentDataPath instead of shipping to every player.");
            }
            if (checkedFiles == 0)
                failures.Add("D1: neither shipped offsets.json could be read — the absolute-override rule is UNVERIFIED.");
            else if (failures.Count == before)
                log.AppendLine("  D1 no shipped absolute @sheathed override (" + checkedFiles + " files) .. ok");
        }

        /// <summary>
        /// Every "<c>&lt;key&gt;@sheathed</c>" row in an offsets.json whose object also carries
        /// <c>"fullOverride": true</c>. Deliberately a scan over the raw text rather than a JSON
        /// parse: this suite must not acquire a dependency on the table's schema to answer a
        /// question about two literals, and a schema change must not silently switch the rule off.
        /// </summary>
        private static List<string> AbsoluteSheathedIds(string json)
        {
            var hits = new List<string>();
            const string Marker = "@sheathed\"";
            int i = 0;
            while (true)
            {
                int at = json.IndexOf(Marker, i, StringComparison.Ordinal);
                if (at < 0) break;
                i = at + Marker.Length;
                // The id, read back to its opening quote.
                int q = json.LastIndexOf('"', at);
                string id = q >= 0 ? json.Substring(q + 1, at + "@sheathed".Length - q - 1) : "<unknown>";
                // The object this id belongs to ends at the next '}' that closes it. Scan forward
                // from the id to that brace and look for fullOverride:true inside.
                int depth = 0, j = q >= 0 ? json.LastIndexOf('{', at) : at;
                if (j < 0) j = at;
                int end = j;
                for (; end < json.Length; end++)
                {
                    if (json[end] == '{') depth++;
                    else if (json[end] == '}') { depth--; if (depth == 0) break; }
                }
                string body = json.Substring(j, Math.Min(json.Length, end + 1) - j);
                if (body.Replace(" ", string.Empty).IndexOf("\"fullOverride\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                    hits.Add(id);
            }
            return hits;
        }

        /// <summary>
        /// Blank out comments so a lint tests CODE, not prose. This file's whole subject is a
        /// change whose tombstone comments quote the retired bone and the retired socket name —
        /// an oracle that cannot tell code from a comment would fail the author for explaining
        /// the fix. (RaidScoringRegression learned this on 2026-08-07.)
        /// </summary>
        private static string BlankComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            bool inString = false, inChar = false, verbatim = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (!inString && !inChar)
                {
                    if (i + 1 < src.Length && c == '/' && src[i + 1] == '*')
                    {
                        int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        sb.Append(' ');
                        if (end < 0) break;
                        i = end + 1;
                        continue;
                    }
                    if (i + 1 < src.Length && c == '/' && src[i + 1] == '/')
                    {
                        int nl = src.IndexOf('\n', i);
                        sb.Append(' ');
                        if (nl < 0) break;
                        sb.Append('\n');
                        i = nl;
                        continue;
                    }
                    if (c == '"')
                    {
                        inString = true;
                        verbatim = i > 0 && src[i - 1] == '@';
                        sb.Append(c);
                        continue;
                    }
                    if (c == '\'') { inChar = true; sb.Append(c); continue; }
                    sb.Append(c);
                    continue;
                }
                sb.Append(c);
                if (inString)
                {
                    if (verbatim)
                    {
                        if (c == '"' && !(i + 1 < src.Length && src[i + 1] == '"')) inString = false;
                        else if (c == '"' && i + 1 < src.Length && src[i + 1] == '"') { sb.Append('"'); i++; }
                    }
                    else if (c == '\\' && i + 1 < src.Length) { sb.Append(src[i + 1]); i++; }
                    else if (c == '"') inString = false;
                }
                else if (inChar)
                {
                    if (c == '\\' && i + 1 < src.Length) { sb.Append(src[i + 1]); i++; }
                    else if (c == '\'') inChar = false;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Blanks the CONTENTS of string literals (the quotes survive), so a brace-balanced
        /// scan cannot be thrown by an interpolated string and so a rule cannot match a name
        /// that appears only inside a trace message. Run AFTER BlankComments.
        /// </summary>
        private static string BlankStringLiterals(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '\'')
                {
                    sb.Append(c);
                    i++;
                    while (i < src.Length && src[i] != '\'')
                    {
                        sb.Append(' ');
                        if (src[i] == '\\') { i++; if (i < src.Length) sb.Append(' '); }
                        i++;
                    }
                    if (i < src.Length) sb.Append('\'');
                    continue;
                }
                if (c != '"') { sb.Append(c); continue; }

                bool verbatim = i > 0 && src[i - 1] == '@';
                sb.Append('"');
                i++;
                while (i < src.Length)
                {
                    char d = src[i];
                    if (verbatim)
                    {
                        if (d == '"' && i + 1 < src.Length && src[i + 1] == '"') { sb.Append("  "); i += 2; continue; }
                        if (d == '"') break;
                    }
                    else
                    {
                        if (d == '\\' && i + 1 < src.Length) { sb.Append("  "); i += 2; continue; }
                        if (d == '"') break;
                    }
                    // Newlines are preserved so line-oriented reasoning about the blanked source
                    // still lines up with the file.
                    sb.Append(d == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i < src.Length) sb.Append('"');
            }
            return sb.ToString();
        }
    }
}
