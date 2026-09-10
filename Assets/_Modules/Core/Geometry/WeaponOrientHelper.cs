// =============================================================================
// WeaponOrientHelper — the GENERALIZATION of the bow's bounds seat to every
// weapon archetype (WO-1123; BINDING canon: docs/WEAPON_ARMOR_ORIENT_LOGIC.md,
// docs/ARCHITECTURE_PRINCIPLES.md §4).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Geometry
// Lives in Core so Village / Pets / Dungeons can all read it across the asmdef
// boundary (WO-1123 §3, acceptance 2).
//
// WHY THIS FILE EXISTS
// ---------------------------------------------------------------------------
// ARCHITECTURE_PRINCIPLES §4 names `WeaponOrientHelper` directly and it was never
// written. What existed was WeaponBoundsOrient — the BOW's seat, bow-specific by
// its public surface (NormalizeInto / ComputeBowHeldRotation / TryAspectRatio).
// The owner's instruction (2026-08-19, verbatim intent) was to GENERALIZE that one
// helper, "so we can pass it the object such as the bow with its stipulations as
// opposed to others" — ONE entry point, mesh + archetype stipulations in, seat out,
// with the bow's current behaviour preserved EXACTLY as one archetype.
//
// ⚠ THE BOW ARCHETYPE IS A DELEGATION, NOT A REIMPLEMENTATION. Every bow path here
// calls WeaponBoundsOrient with the same arguments the equip path already passes.
// The bow is felt-verified by the owner (2026-08-19) and is the TEMPLATE, not a
// target — a "tidy-up" that inlines or paraphrases that solve is a regression by
// construction.
//
// ── THE SHARED AXIS FRAME (owner, 2026-08-19, verbatim) ──────────────────────
//   "Y = the LONGEST dimension, X = the MIDDLE dimension, Z = the NARROWEST."
// Every archetype rule below is expressed in that frame — longest / middle /
// narrowest are MEASURED off the bounds, never assumed off the FBX import.
//
// ⚠ NAMING TRANSPOSITION, DELIBERATELY NOT "FIXED" (flagged for the CLI/owner).
// The SEATED prop frame this project already ships — WeaponBoundsOrient
// .AlignAxesYLongXNarrowZWide — puts longest→+Y, NARROWEST→+X, MIDDLE→+Z. That
// is X and Z swapped relative to the owner's naming above. The names differ; the
// GEOMETRY does not — "longest", "middle" and "narrowest" identify the same three
// measured extents either way, and every rule here is written against the measured
// role (longest / middle / narrowest), then mapped to whichever prop-local axis the
// existing align actually seated it on, VERIFIED by measurement (see
// TryMeasureAxes + the post-align check in TryComputeShieldMountRotation).
// Re-permuting the shipped align to match the naming would rotate the felt-verified
// bow 90° about its long axis for a documentation reason. NOT DONE. If the owner
// wants the seated frame itself re-lettered, that is a separate ticket with a
// screenshot per family.
//
// ── PRECEDENCE (WO-1123 acceptance 4), asserted in this order ────────────────
//   1. authored offset row (Offset Forge / Seating Editor)   → SeatSource.AuthoredOffset
//   2. manual:true on the catalog row                        → SeatSource.Manual
//   3. derived from mesh bounds + archetype                  → SeatSource.Derived
//   4. archetype default constant (kept, never deleted)      → SeatSource.ArchetypeDefault
// ResolveSource() IS that order, as one pure function, so no caller can re-order it
// by accident and a test can assert it without a scene.
//
// ── §12 ─────────────────────────────────────────────────────────────────────
// Every derivation emits a FlowTrace line carrying the MEASURED inputs AND the
// output: the three extents, which axis took each role, the archetype, the chosen
// end/side with the score that chose it, and the resulting rotation/grip. Each line
// is phrased so a WRONG answer prints differently from a right one (an ambiguous
// side prints both scores and the margin; a mis-seated align prints the axis it
// actually landed on). A line that cannot embarrass you is decoration.
// =============================================================================

// COMPANION REFERENCE: docs/WEAPON_MESH_ARCHETYPES.md — what each archetype's mesh IS, in terms a
// program can measure (the profile-curve primitive, and the per-family DISAMBIGUATOR that separates
// the two ends of an axis, which a bounding box can never answer). Read it before adding a family.

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Geometry
{
    /// <summary>The weapon families this helper carries stipulations for. Anything else is
    /// <see cref="Unknown"/> — and Unknown DERIVES NOTHING: the caller keeps its existing
    /// behaviour and a Warn says so. Inventing a pose for a family the owner has not ruled on
    /// is exactly the guess this file exists to end.</summary>
    public enum WeaponArchetype
    {
        Unknown = 0,
        Bow,
        Sword,
        Staff,
        Shield
    }

    /// <summary>Which tier of the precedence ladder produced (or would produce) a seat.</summary>
    public enum SeatSource
    {
        /// <summary>An owner-authored Offset Forge / Seating Editor row exists — it wins outright.</summary>
        AuthoredOffset,
        /// <summary>`manual: true` on the catalog row — CANON, never overwritten by a derived pass.</summary>
        Manual,
        /// <summary>Derived from this mesh's own bounds + the archetype's stipulations.</summary>
        Derived,
        /// <summary>Nothing could be measured — the hand-typed archetype constant, kept as the
        /// documented last resort (WO-1123 acceptance 3: the constants are NOT deleted).</summary>
        ArchetypeDefault
    }

    /// <summary>The three measured extents and which local axis carries each role. Reported in the
    /// owner's language (longest / middle / narrowest) so a trace line can be read against her rule
    /// without a translation step.</summary>
    public struct MeasuredAxes
    {
        public Vector3 Size;
        /// <summary>0 = X, 1 = Y, 2 = Z.</summary>
        public int LongestAxis, MiddleAxis, NarrowestAxis;
        public float LongestLen, MiddleLen, NarrowestLen;

        public string Describe() =>
            $"size={Size:0.####} longest={WeaponOrientHelper.AxisName(LongestAxis)}({LongestLen:0.####}) " +
            $"middle={WeaponOrientHelper.AxisName(MiddleAxis)}({MiddleLen:0.####}) " +
            $"narrowest={WeaponOrientHelper.AxisName(NarrowestAxis)}({NarrowestLen:0.####})";
    }

    /// <summary>The seat one call produced: what it decided, why, and off what measurements.</summary>
    public struct WeaponSeat
    {
        public WeaponArchetype Archetype;
        public SeatSource Source;
        /// <summary>True only when the rotation/grip below came out of MEASURED geometry.</summary>
        public bool Derived;
        /// <summary>Rotation expressed in the MOUNT's (hand bone / back socket) local frame.</summary>
        public Quaternion MountLocalRotation;
        /// <summary>The grip point, in grip-root-local space, that was moved onto the origin.</summary>
        public Vector3 GripLocal;
        public MeasuredAxes Axes;
        /// <summary>One sentence naming the rule that fired, or the reason nothing did.</summary>
        public string Why;
    }

    public static class WeaponOrientHelper
    {
        // ── Archetype stipulations. All DIMENSIONLESS fractions of the prop's own measured
        //    size, so every rule is scale-free and no metre literal enters a derivation. ──

        /// <summary>STAFF grip, owner ruling 2026-08-19 verbatim: "The longest length is Y, and you
        /// go three quarters of the way up Y, and that can be where the hand is attached."
        /// <para>
        /// ⚠ THIS SUPERSEDES docs/WEAPON_ARMOR_ORIENT_LOGIC.md's older "staff → grip lower third".
        /// The owner's 2026-08-19 ruling wins; the doc line is corrected in the same change. Both
        /// values are recorded here because a future reader WILL find the old third in an older doc
        /// copy and must be able to tell which one is current without re-asking her.
        /// </para></summary>
        public const float StaffGripFractionUpLongAxis = 0.75f;

        /// <summary>SWORD grip when no cross-guard can be measured: this far up the long axis from
        /// the BLUNT end. Archetype default (precedence tier 4), never a derived value — a trace
        /// line says out loud when this fired instead of the measured cross-guard.</summary>
        public const float SwordFallbackGripFractionFromHilt = 0.12f;

        /// <summary>Y bins used to profile a silhouette.</summary>
        private const int Bins = 48;
        /// <summary>Fraction of the long axis, at each end, sampled to judge which end tapers.</summary>
        private const float EndBand = 0.12f;
        /// <summary>Minimum relative gap between the two ends' widths before the taper test is
        /// allowed to decide. Below this the mesh does not answer the owner's "find the edge that
        /// is NOT sharp" — so we Warn and keep the existing behaviour instead of guessing.</summary>
        private const float TaperDecisionMargin = 0.15f;
        /// <summary>Minimum relative gap (as a fraction of the long axis) between the two ends'
        /// distances from the GRIP ORIGIN before the grip-at-origin test is allowed to decide which
        /// end is the hilt. A prop whose origin sits mid-length answers nothing, and guessing there
        /// is how a global sign got shipped in the first place — so below this we decline and say so.
        /// Used by <see cref="TryResolveSheathedTipSign"/>, which is the device-side path because it
        /// reads bounds, not vertices (the live props ship with Read/Write OFF).</summary>
        private const float GripEndDecisionMargin = 0.15f;
        /// <summary>Below this relative gap the two ends are treated as MEASURABLY IDENTICAL rather
        /// than merely undecided — a symmetrical prop (owner ruling WO-1136). It is an order of
        /// magnitude tighter than <see cref="TaperDecisionMargin"/> ON PURPOSE: the band BETWEEN the
        /// two is where a prop's ends genuinely differ and we failed to read the difference, and that
        /// band must stay a hard failure. Widening this constant to swallow a specific prop would be
        /// an exemption list wearing a number's clothes.
        /// <para>staff_A, the mesh that forced this: taper relGap 0.001, grip relGap 0.000 — both an
        /// order of magnitude under this bar, i.e. symmetrical, not merely ambiguous.</para></summary>
        public const float SignAgnosticSymmetryEpsilon = 0.02f;
        /// <summary>How many times longer than the second-longest extent the long axis must measure
        /// before <see cref="TrySheathesVertical"/> will make any claim about verticality. An
        /// axis-aligned box around a prop tilted 45° has near-equal extents and its "longest axis"
        /// is a coin flip, so a verticality answer derived from it would be noise reported as a
        /// measurement. Refuse instead.</summary>
        public const float VerticalCarryMinAxisDominance = 2f;
        /// <summary>How far off vertical a sign-agnostic prop's long axis may sit and still count as
        /// carried upright. The shipped trace calls the failure at ~90 ("lying across the body").</summary>
        public const float VerticalCarryToleranceDeg = 5f;
        /// <summary>Outer band of a shield half, as a fraction of that half's extent, used to score
        /// "is this face a broad flat surface (smooth) or a small cluster (the handle)".</summary>
        private const float ShieldFaceBand = 0.25f;
        /// <summary>Minimum gap between the two faces' smoothness scores before the handle side is
        /// allowed to decide. Below this: Warn + no flip.</summary>
        private const float ShieldDecisionMargin = 0.10f;
        /// <summary>A cross-guard must be this much wider than the median section to count.</summary>
        private const float CrossGuardSpikeRatio = 1.6f;
        /// <summary>How far (as a fraction of a half-thickness) the vertex centroid must sit off the
        /// box centre before the "dish" signal is allowed to name the convex front of a shield.</summary>
        private const float ShieldDishMargin = 0.05f;

        // =====================================================================
        //  NAME → ARCHETYPE  ("the words and dimensions alone tell you one thing")
        // =====================================================================

        /// <summary>
        /// Maps a catalog category (weapons.json `category`) and/or an id / mesh name onto an
        /// archetype. Category wins when it answers; the name is the fallback classifier.
        /// <para>
        /// DELIBERATE NON-MEMBERS: axe / hammer / mace / wand / crossbow all return
        /// <see cref="WeaponArchetype.Unknown"/>. The owner's 2026-08-19 spec covers bow, sword,
        /// staff and shield ONLY, and each of those families' rules leans on a property the
        /// excluded ones do not have — an axe head does not taper to a point, so the sword's
        /// "find the edge that is NOT sharp" test would confidently pick the wrong end; a crossbow
        /// is widest across X and is held across the body, which is why WeaponBoundsOrient excludes
        /// it from the bow solve by construction. Unknown = keep today's behaviour + say so.
        /// </para>
        /// </summary>
        public static WeaponArchetype Classify(string category, string idOrMeshName)
        {
            string c = (category ?? string.Empty).ToLowerInvariant();
            string n = (idOrMeshName ?? string.Empty).ToLowerInvariant();

            // Crossbow first: it CONTAINS "bow" and must never be classified as one.
            if (c.Contains("crossbow") || n.Contains("crossbow")) return WeaponArchetype.Unknown;

            if (c.Contains("shield") || c.Contains("buckler")) return WeaponArchetype.Shield;
            if (c.Contains("bow")) return WeaponArchetype.Bow;
            if (c.Contains("staff") || c.Contains("stave")) return WeaponArchetype.Staff;
            if (c.Contains("sword") || c.Contains("blade") || c.Contains("dagger")) return WeaponArchetype.Sword;
            if (!string.IsNullOrEmpty(c)) return WeaponArchetype.Unknown;   // categorised, just not by us

            if (n.Contains("shield") || n.Contains("buckler")) return WeaponArchetype.Shield;
            if (n.Contains("bow")) return WeaponArchetype.Bow;
            if (n.Contains("staff") || n.Contains("stave")) return WeaponArchetype.Staff;
            if (n.Contains("sword") || n.Contains("blade") || n.Contains("dagger")) return WeaponArchetype.Sword;
            return WeaponArchetype.Unknown;
        }

        // =====================================================================
        //  PRECEDENCE — one pure function, so the order cannot drift per caller
        // =====================================================================

        /// <summary>
        /// The WO-1123 precedence ladder, in order: authored offset row → manual → derived →
        /// archetype default. Pure and scene-free so a regression can assert the ORDER (feed it
        /// every combination and the answer must not depend on anything else).
        /// <para>
        /// WHY `manual` SITS SECOND AND NOT LAST: 81 of the 96 rows in weapons.json carry
        /// `manual: true` and, until this change, WeaponDef did not even declare the field — the
        /// flag read as protection to anyone authoring gear and protected nothing. The first
        /// derived pass over weapons would have silently overwritten all 81 owner-dialled rows.
        /// The structure side already paid this bill once (2026-08-18: an axis-bake zeroed
        /// corrections it believed redundant and the town lay down).
        /// </para>
        /// </summary>
        public static SeatSource ResolveSource(bool hasAuthoredOffset, bool manual, bool canDerive)
        {
            if (hasAuthoredOffset) return SeatSource.AuthoredOffset;
            if (manual) return SeatSource.Manual;
            if (canDerive) return SeatSource.Derived;
            return SeatSource.ArchetypeDefault;
        }

        /// <summary>True when a derived pass is ALLOWED to touch this row at all. The one-line
        /// form of the two top tiers, for call sites that only need the veto.</summary>
        public static bool MayDerive(bool hasAuthoredOffset, bool manual)
            => ResolveSource(hasAuthoredOffset, manual, true) == SeatSource.Derived;

        // =====================================================================
        //  WO-1215 — `manual` MUST NAME A CORRECTION THAT EXISTS
        // =====================================================================
        //
        // ⛔ READ THIS BEFORE CHANGING THE LADDER ABOVE. The ladder is CORRECT and is NOT touched.
        // What was wrong is the VALUE fed into its `manual` input.
        //
        // `manual: true` means, in ARCHITECTURE_PRINCIPLES §4's own words, "a rare human nudge
        // perfects it" — an OWNER-DIALLED SEAT that the auto pass must never overwrite. It is a
        // claim about a human act, and a claim about a human act is only worth honouring when the
        // artefact of that act exists.
        //
        // MEASURED, not assumed (Assets/Resources/Data/Canonical/weapons.json, re-parsed 2026-08-26):
        //   • 96 rows. 81 carry `manual: true`.
        //   • 77 of those 81 ALSO carry `generated: true` — i.e. the row was emitted by
        //     Assets/Editor/Catalog/GearCatalogGenerator.cs, which stamps `["manual"] = false`
        //     on a fresh row with the comment "set true by hand to lock the row forever" (:386-387).
        //     `generated:true` + `manual:true` together is therefore a CONTRADICTION on its face.
        //   • The contradiction has a named origin: commit af96fe788 ("apply the ratified WO-500
        //     curve to the 65 and unhide them"), a DATA-ONLY BALANCE PASS, whose own body records
        //     "blink_ rows 65, manual=true 65/65" — and, two paragraphs later, predicts this exact
        //     defect: "offsets.json has authored seating for exactly sword_A/D/F/G + shield_A ...
        //     and ZERO blink mesh key has authored seating. The shelf traded dialed weapons for
        //     un-dialed ones. Screenshot-class risk, not log-class."
        //   • All 19 shield rows: 18 have NO row in Assets/OffsetForge/offsets.json at all.
        //
        // CONSEQUENCE OF FEEDING THE RAW FLAG IN: `manual` vetoed the derivation to protect a seat
        // that had never been dialled, so the shield fell through to the archetype constant — which,
        // on the NATIVE addressable path those 18 shields take, is IDENTITY. That is the flat slab
        // through the hero's chest in tmp/shield-seat-101829.png, and it is the construct §4 bans
        // by name. The flag read as protection and protected nothing — the SAME failure WO-1123
        // fixed one layer down, where `manual` was authored 81 times and read zero times.
        //
        // ⚠ THE FIX IS NOT "IGNORE manual". A row that a human really did dial is still canon, and
        // that is what the `hasAuthoredSeat` term below preserves: `tripo_shield_a` is
        // generated+manual AND has an authored `shield_A` row, so it stays substantiated and
        // untouched. `generated:false` rows (the 4 arrow rows, and every hand-authored row) are
        // trusted unconditionally — a human wrote the row, so a human may have meant the flag.

        /// <summary>
        /// True when a row's <c>manual: true</c> names a correction that ACTUALLY EXISTS, and is
        /// therefore entitled to veto a derived seat.
        /// <para>
        /// A row that was machine-emitted (<paramref name="rowIsGenerated"/>) and carries no
        /// authored seat has no dialled pose to protect: its <c>manual</c> flag was stamped by a
        /// generator or a data pass, not by the owner's eye. Honouring it does not preserve a
        /// correction — it preserves IDENTITY, which is the one outcome §4 forbids.
        /// </para>
        /// Pure and scene-free, so <c>AttachmentOffsetRegression</c> can assert every combination
        /// without a hero, a mesh or a play session.
        /// </summary>
        /// <param name="manual">The row's raw <c>manual</c> flag as loaded from the catalog.</param>
        /// <param name="rowIsGenerated">The row's <c>generated</c> flag. True = machine-emitted.</param>
        /// <param name="hasAuthoredSeat">True when an Offset Forge row exists for this prop's seat
        /// key — the artefact of the human act <c>manual</c> claims.</param>
        public static bool ManualSeatIsSubstantiated(bool manual, bool rowIsGenerated, bool hasAuthoredSeat)
            => manual && (hasAuthoredSeat || !rowIsGenerated);

        /// <summary>
        /// <see cref="MayDerive(bool,bool)"/> with the WO-1215 substantiation test folded in — the
        /// overload every catalog-driven call site should use, because a catalog row is exactly the
        /// place an unsubstantiated <c>manual</c> can come from.
        /// </summary>
        public static bool MayDerive(bool hasAuthoredOffset, bool manual, bool rowIsGenerated)
            => MayDerive(hasAuthoredOffset,
                         ManualSeatIsSubstantiated(manual, rowIsGenerated, hasAuthoredOffset));

        // =====================================================================
        //  THE ONE ENTRY POINT — mesh + archetype stipulations in, seat out
        // =====================================================================

        /// <summary>
        /// Seats <paramref name="prop"/> under <paramref name="gripRoot"/> per its archetype's
        /// stipulations and returns the mount-local rotation for <paramref name="mount"/>.
        /// <para>
        /// HONOURS `manual` FIRST (WO-1123 §2, ⛔ ordering clause): an authored-offset or
        /// manual:true row returns FALSE with the prop UNTOUCHED — not normalized, not rotated,
        /// not shifted — so running this pass twice over a dialled row is a zero delta by
        /// construction, which is exactly what acceptance 1 asks to be proven by diff.
        /// </para>
        /// </summary>
        /// <param name="prop">The loaded weapon prop (not yet seated).</param>
        /// <param name="gripRoot">The grip root the prop is parented under.</param>
        /// <param name="mount">The bone/socket the grip root will hang on (hand or back socket).</param>
        /// <param name="body">The wearer's root transform — supplies up/forward. Never the wrist.</param>
        /// <param name="targetLength">Held length (m) along the LONGEST axis.</param>
        /// <param name="outwardFromBody">Which way "away from the player" points for this mount:
        /// body.forward in the hand, -body.forward on the back. Shield only; ignored otherwise.</param>
        public static bool TrySeat(GameObject prop, Transform gripRoot, Transform mount, Transform body,
                                   WeaponArchetype archetype, float targetLength,
                                   bool hasAuthoredOffset, bool manual,
                                   Vector3 outwardFromBody, out WeaponSeat seat)
        {
            seat = new WeaponSeat { Archetype = archetype, MountLocalRotation = Quaternion.identity };

            if (prop == null || gripRoot == null)
            {
                seat.Source = SeatSource.ArchetypeDefault;
                seat.Why = "null prop or grip root";
                FlowTrace.Warn("Equip", "WeaponOrientHelper.TrySeat: null prop or gripRoot — no seat " +
                    "derived; the caller keeps whatever pose it already had.");
                return false;
            }

            // ── TIER 1 + 2: authored row, then manual. Both return with the prop UNTOUCHED. ──
            SeatSource gate = ResolveSource(hasAuthoredOffset, manual, canDerive: true);
            if (gate == SeatSource.AuthoredOffset || gate == SeatSource.Manual)
            {
                seat.Source = gate;
                seat.Why = gate == SeatSource.AuthoredOffset
                    ? "an authored Offset Forge row exists — it outranks any derivation"
                    : "manual:true on the catalog row — CANON, never overwritten by a derived pass";
                FlowTrace.Step("Equip",
                    $"OrientHelper '{prop.name}' archetype={archetype}: SKIPPED, source={seat.Source} " +
                    $"(authoredRow={hasAuthoredOffset} manual={manual}). Prop left EXACTLY as loaded — " +
                    "no normalize, no rotate, no grip shift. Running this pass again is a zero delta.");
                return false;
            }

            if (archetype == WeaponArchetype.Unknown)
            {
                seat.Source = SeatSource.ArchetypeDefault;
                seat.Why = "archetype Unknown — the owner's 2026-08-19 spec covers bow/sword/staff/shield only";
                FlowTrace.Warn("Equip",
                    $"OrientHelper '{prop.name}': archetype UNKNOWN (not one of bow/sword/staff/shield). " +
                    "NOT deriving a pose — the caller keeps its existing behaviour. A new family needs " +
                    "an owner rule, not a guess (WO-1123).");
                return false;
            }

            // ── BOW: delegate, verbatim. This IS WeaponBoundsOrient's felt-verified path. ──
            if (archetype == WeaponArchetype.Bow)
            {
                WeaponBoundsOrient.NormalizeInto(prop, gripRoot, targetLength,
                                                 WeaponBoundsOrient.GripAnchor.BowGrip);
                TryMeasureAxes(prop, gripRoot, out seat.Axes);
                seat.MountLocalRotation = WeaponBoundsOrient.ComputeBowHeldRotation(mount, body);
                seat.Source = SeatSource.Derived;
                seat.Derived = true;
                seat.Why = "BOW: WeaponBoundsOrient BowGrip + ComputeBowHeldRotation (delegated verbatim)";
                FlowTrace.Step("Equip",
                    $"OrientHelper '{prop.name}' BOW: {seat.Axes.Describe()} -> mountLocalEuler=" +
                    $"{seat.MountLocalRotation.eulerAngles:0.#} (delegated to WeaponBoundsOrient — if this " +
                    "line's numbers ever differ from the BowOrient line below it, the delegation drifted).");
                return true;
            }

            // ── SHIELD: centre seat, thin axis = face normal pointed AWAY from the player. ──
            if (archetype == WeaponArchetype.Shield)
            {
                WeaponBoundsOrient.NormalizeInto(prop, gripRoot, targetLength,
                                                 WeaponBoundsOrient.GripAnchor.Centre,
                                                 resolveBladeUpFromHilt: false);
                TryMeasureAxes(prop, gripRoot, out seat.Axes);
                Vector3 up = body != null ? body.up : Vector3.up;
                if (!TryComputeShieldMountRotation(prop, gripRoot, mount, outwardFromBody, up,
                                                   out Quaternion shieldRot, out string why))
                {
                    seat.Source = SeatSource.ArchetypeDefault;
                    seat.Why = why;
                    return false;
                }
                seat.MountLocalRotation = shieldRot;
                seat.GripLocal = Vector3.zero;   // Centre anchor already put the strap centre on the origin
                seat.Source = SeatSource.Derived;
                seat.Derived = true;
                seat.Why = why;
                return true;
            }

            // ── SWORD / STAFF: long axis up, grip at the measured end. ──
            WeaponBoundsOrient.NormalizeInto(prop, gripRoot, targetLength,
                                             WeaponBoundsOrient.GripAnchor.HiltEnd);
            TryMeasureAxes(prop, gripRoot, out seat.Axes);

            float gripY;
            string gripWhy;
            bool gripOk;
            if (archetype == WeaponArchetype.Staff)
                gripOk = TryDeriveStaffGripY(prop, gripRoot, out gripY, out gripWhy);
            else
                gripOk = TryDeriveSwordGripY(prop, gripRoot, out gripY, out gripWhy);
            if (!gripOk)
            {
                seat.Source = SeatSource.ArchetypeDefault;
                seat.Why = gripWhy;
                return false;
            }

            Vector3 lp = prop.transform.localPosition;
            lp.y -= gripY;
            prop.transform.localPosition = lp;
            seat.GripLocal = new Vector3(0f, gripY, 0f);
            seat.MountLocalRotation = ComputeBladeUpRotation(mount, body);
            seat.Source = SeatSource.Derived;
            seat.Derived = true;
            seat.Why = gripWhy;
            return true;
        }

        // =====================================================================
        //  SHIELD  (owner, 2026-08-19)
        // =====================================================================
        //
        // HER WORDS, the whole rule in three clauses:
        //   "Look at the rotation. One side is gonna be relatively smooth, the other side is gonna
        //    have a handle."
        //   "You take the thinnest side of the object, which will generally be the Z, but whichever
        //    of the three is the shortest is the thickness of the shield."
        //   "the thinness/thickness of the shield is facing away from the player ... with the handle
        //    where the hand mounts on the off-player's hand"
        //
        // So: the NARROWEST measured extent is the shield's thickness, therefore its FACE NORMAL;
        // that normal points AWAY from the player; and of the two faces it separates, the HANDLED
        // one is the one against the hand — i.e. inward. Note the clause "whichever of the three is
        // the shortest" is the owner explicitly refusing to let this be an axis-name rule: Z is her
        // expectation, the measurement is the authority.
        //
        // WHAT THIS REPLACES: EquipmentController's drawn shield was IDENTITY ∘ a 180° yaw, and its
        // sheathed shield was the hand-typed (0, 90, 192) whose own comment concedes it has "no
        // relationship to geometry OR the chest-bone axes". Both constants are KEPT as the
        // documented fallback (§12: fallbacks and instrumentation are never stripped) — this only
        // outranks them when the geometry actually answers.

        /// <summary>
        /// The MEASURED half of a shield seat: which extent is the thickness and which face carries
        /// the handle. Resolving it walks every vertex, so it is done ONCE per attach and handed to
        /// the cheap per-frame <see cref="ComputeShieldMountRotation"/> — ApplyHoldPose re-asserts
        /// the sheathed pose EVERY FRAME, and a per-frame vertex scan of a shield mesh is not a
        /// thing this project can afford (see the throttled sheathed-offset traces that blinded
        /// three F8 captures for the same class of mistake).
        /// </summary>
        public struct ShieldFrame
        {
            public bool Valid;
            public bool HandleResolved;
            /// <summary>True when the handle sits on the POSITIVE side of the thickness axis.</summary>
            public bool HandleOnPositiveSide;
            public float PositiveSmoothScore, NegativeSmoothScore;
            public MeasuredAxes Axes;
            /// <summary>Unit axis, in the SEAT frame (the grip root), carrying the shield's
            /// thickness — i.e. its face normal. Whichever of the three measured shortest, per the
            /// owner: "whichever of the three is the shortest is the thickness of the shield."</summary>
            public Vector3 ThicknessAxis;
            /// <summary>Unit axis carrying the longest extent.</summary>
            public Vector3 LongAxis;
        }

        /// <summary>Resolves the expensive, pose-independent half of a shield seat. Call once per
        /// attach; feed the result to <see cref="ComputeShieldMountRotation"/> every pose.</summary>
        public static bool TryResolveShieldFrame(GameObject prop, Transform parent, out ShieldFrame frame)
        {
            frame = default;
            if (prop == null || parent == null)
            {
                FlowTrace.Warn("Equip", "ShieldFrame: null prop/parent — keeping the existing " +
                    "hand-typed shield pose (the identity-drawn / (0,90,192)-sheathed fallback).");
                return false;
            }
            if (!TryMeasureAxes(prop, parent, out MeasuredAxes axes))
            {
                FlowTrace.Warn("Equip", $"ShieldFrame '{prop.name}': NO measurable renderer bounds — " +
                    "keeping the existing hand-typed shield pose. A shield with no bounds cannot have a " +
                    "derived face normal; this line is the reason it still uses the constant.");
                return false;
            }
            // NO AXIS-NAME PREMISE. The thickness is "whichever of the three is the shortest"
            // (owner, verbatim) MEASURED in the seat frame — so this works on a NormalizeInto'd
            // prop (thickness lands on X) and equally on a NATIVE prop that skipped the align and
            // kept its authored axes. That generality is the point: the LIVE default shield
            // (knight_shield_starter -> ShieldWithItemLogic) is on the native path, so a rule that
            // assumed the post-align permutation would have fixed every shield except the broken one.
            //
            // What CAN'T be answered is a shield that isn't plate-shaped: if the shortest and
            // longest extents are within a hair of each other there is no face to point anywhere.
            if (axes.LongestLen <= 1e-5f || axes.NarrowestLen >= axes.LongestLen * 0.9f)
            {
                FlowTrace.Warn("Equip",
                    $"ShieldFrame '{prop.name}': NOT PLATE-SHAPED — {axes.Describe()}. The shortest " +
                    "extent is not meaningfully thinner than the longest, so no face normal exists to " +
                    "point away from the player. Keeping the existing hand-typed pose rather than " +
                    "inventing one (WO-1123: ambiguity falls back, it does not guess).");
                return false;
            }

            frame.Valid = true;
            frame.Axes = axes;
            frame.ThicknessAxis = UnitAxis(axes.NarrowestAxis);
            frame.LongAxis = UnitAxis(axes.LongestAxis);
            frame.HandleResolved = TryResolveShieldHandleSide(prop, parent, axes.NarrowestAxis,
                                                              out bool handlePositive,
                                                              out float plus, out float minus);
            frame.HandleOnPositiveSide = handlePositive;
            frame.PositiveSmoothScore = plus;
            frame.NegativeSmoothScore = minus;
            string tName = AxisName(axes.NarrowestAxis);
            FlowTrace.Step("Equip",
                $"ShieldFrame '{prop.name}': {axes.Describe()} | thickness={tName} " +
                $"({axes.NarrowestLen:0.####}m) longAxis={AxisName(axes.LongestAxis)} | handleSide=" +
                (frame.HandleResolved ? (handlePositive ? "+" + tName : "-" + tName) : "UNRESOLVED") +
                $" smoothScore(+{tName})={plus:0.###} (-{tName})={minus:0.###} " +
                $"margin={Mathf.Abs(plus - minus):0.###}. Resolved ONCE per attach; the per-pose " +
                "rotation is built from these numbers.");
            return true;
        }

        /// <summary>
        /// The cheap per-pose half: the rotation, in <paramref name="mount"/>'s LOCAL frame, that
        /// points the measured THICKNESS axis along <paramref name="outwardWorld"/> and the LONGEST
        /// axis along <paramref name="upWorld"/>, with the handled face turned inward. No mesh
        /// access — safe to call every frame.
        /// </summary>
        public static Quaternion ComputeShieldMountRotation(ShieldFrame frame, Transform mount,
                                                            Vector3 outwardWorld, Vector3 upWorld)
        {
            if (mount == null || !frame.Valid) return Quaternion.identity;

            Vector3 outward = outwardWorld.sqrMagnitude > 1e-6f ? outwardWorld.normalized : Vector3.forward;
            Vector3 up = upWorld.sqrMagnitude > 1e-6f ? upWorld.normalized : Vector3.up;
            up -= Vector3.Dot(up, outward) * outward;
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.forward;
                up -= Vector3.Dot(up, outward) * outward;
            }
            up.Normalize();

            // Two orthonormal frames, then the rotation that carries one onto the other — no Euler
            // is ever named, so there is no sign or order to guess.
            //   SOURCE (the prop's own): +X -> thickness, +Y -> long axis.
            //   TARGET (the world we want): +X -> outward (away from the player), +Y -> up.
            // Unity's basis is +Z = Cross(+X, +Y) and LookRotation(f, u) maps +X -> Cross(u, f), so
            // LookRotation(Cross(a, b), b) is exactly "the frame whose X is a and whose Y is b".
            Quaternion source = Quaternion.LookRotation(
                Vector3.Cross(frame.ThicknessAxis, frame.LongAxis), frame.LongAxis);
            Quaternion target = Quaternion.LookRotation(Vector3.Cross(outward, up), up);
            if (frame.HandleResolved && frame.HandleOnPositiveSide)
            {
                // The handle is on the +thickness side, which currently points OUTWARD. Spin the
                // target 180° about its own up so the thickness axis maps to -outward instead: the
                // handled face turns inward to the hand, the smooth face takes the outward normal,
                // and the long axis is untouched, so the shield stays upright.
                target = target * Quaternion.AngleAxis(180f, Vector3.up);
            }
            Quaternion worldTarget = target * Quaternion.Inverse(source);
            return Quaternion.Inverse(mount.rotation) * worldTarget;
        }

        /// <summary>
        /// The rotation, in <paramref name="mount"/>'s LOCAL frame, that points the shield's
        /// THICKNESS axis along <paramref name="outwardWorld"/> (away from the player) and its
        /// LONGEST axis along <paramref name="upWorld"/>, with the handled face turned inward.
        /// <para>
        /// Built in WORLD from the body's own axes and then expressed in the mount's local frame —
        /// the same construction ComputeSheathRotation and ComputeBowHeldRotation already use, for
        /// the same reason: it follows the bone through animation and contains no guessed Euler.
        /// </para>
        /// Works on a NormalizeInto'd prop AND on a native one that kept its authored axes — the
        /// thickness is whichever extent MEASURES shortest in the seat frame, never a named axis.
        /// (An axis-name premise would have fixed every shield except the live default, which is on
        /// the native path. And the align itself has been wrong before — WO-970: it could only yaw,
        /// and left long axes on X for a month.)
        /// </summary>
        public static bool TryComputeShieldMountRotation(GameObject prop, Transform parent, Transform mount,
                                                         Vector3 outwardWorld, Vector3 upWorld,
                                                         out Quaternion mountLocal, out string why)
        {
            mountLocal = Quaternion.identity;
            why = "shield: not derived";
            if (!TryResolveShieldFrame(prop, parent, out ShieldFrame f))
            {
                why = "shield: frame unmeasurable (see the ShieldFrame Warn above for which clause failed)";
                return false;
            }
            return TryComputeShieldMountRotation(f, prop != null ? prop.name : "<null>", mount,
                                                 outwardWorld, upWorld, out mountLocal, out why);
        }

        /// <summary>
        /// The same seat from an ALREADY-RESOLVED <see cref="ShieldFrame"/> — so a caller that
        /// cached the frame at attach (the runtime does) pays exactly ONE vertex walk per shield,
        /// not one per call. <paramref name="subject"/> names the prop in the trace.
        /// </summary>
        public static bool TryComputeShieldMountRotation(ShieldFrame frame, string subject, Transform mount,
                                                         Vector3 outwardWorld, Vector3 upWorld,
                                                         out Quaternion mountLocal, out string why)
        {
            mountLocal = Quaternion.identity;
            why = "shield: not derived";

            if (mount == null || !frame.Valid)
            {
                why = mount == null ? "shield: null mount" : "shield: frame not resolved";
                FlowTrace.Warn("Equip", $"ShieldOrient '{subject}': " +
                    (mount == null ? "null mount" : "unresolved frame") +
                    " — keeping the existing hand-typed shield pose (the identity-drawn / " +
                    "(0,90,192)-sheathed fallback).");
                return false;
            }

            MeasuredAxes axes = frame.Axes;
            bool handleResolved = frame.HandleResolved;
            bool handlePositive = frame.HandleOnPositiveSide;
            float plusScore = frame.PositiveSmoothScore, minusScore = frame.NegativeSmoothScore;
            bool flipped = handleResolved && handlePositive;
            string tName = AxisName(axes.NarrowestAxis);

            Vector3 outward = outwardWorld.sqrMagnitude > 1e-6f ? outwardWorld.normalized : Vector3.forward;
            Vector3 up = upWorld.sqrMagnitude > 1e-6f ? upWorld.normalized : Vector3.up;
            up -= Vector3.Dot(up, outward) * outward;
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.forward;
                up -= Vector3.Dot(up, outward) * outward;
            }
            up.Normalize();

            mountLocal = ComputeShieldMountRotation(frame, mount, outward, up);

            // ── §12 PROVING LINE ─────────────────────────────────────────────────────────────
            // Re-composes the answer and MEASURES it. `faceNormalOffOutward` is the number the
            // owner's rule IS: it must read ~0° (thickness facing away from the player). When the
            // handle side could not be resolved, `handleSide=UNRESOLVED` names it and the margin
            // prints — an unresolved shield is a shield that may be wearing its strap outward, and
            // that is a visibly different failure from a shield that is 90° off.
            Quaternion composed = mount.rotation * mountLocal;
            // Measure the axes the RULE names, not a hardcoded +X: the thickness axis of THIS prop.
            Vector3 faceWorld = composed * frame.ThicknessAxis;
            Vector3 longWorld = composed * frame.LongAxis;
            float faceOff = Vector3.Angle(faceWorld, flipped ? -outward : outward);
            float longOff = Vector3.Angle(longWorld, up);
            float identityFaceOff = Vector3.Angle(mount.rotation * frame.ThicknessAxis, outward);
            why = $"SHIELD: thickness({tName}, {axes.NarrowestLen:0.####}m) -> " +
                  $"outward; longest -> up; handle " + (handleResolved
                      ? (handlePositive ? "on +" + tName + ", flipped inward" : "already inward on -" + tName)
                      : "UNRESOLVED (no flip)");
            FlowTrace.Step("Equip",
                $"ShieldOrient '{subject}' mount='{mount.name}': {axes.Describe()} | " +
                $"handleSide={(handleResolved ? (handlePositive ? "+" + tName : "-" + tName) : "UNRESOLVED")} " +
                $"smoothScore(+{tName})={plusScore:0.###} (-{tName})={minusScore:0.###} " +
                $"margin={Mathf.Abs(plusScore - minusScore):0.###} flipped={flipped} | " +
                $"outward={outward:0.##} up={up:0.##} -> mountLocalEuler={mountLocal.eulerAngles:0.#} " +
                $"faceNormalOffOutward={faceOff:0.#}deg longAxisOffUp={longOff:0.#}deg " +
                $"| identitySeatWouldBeOff={identityFaceOff:0.#}deg (that is the pre-fix drawn seat)");
            return true;
        }

        /// <summary>
        /// Which face of a seated shield carries the HANDLE (owner: "one side is gonna be relatively
        /// smooth, the other side is gonna have a handle").
        /// <para>
        /// MEASURED, not named: split the mesh at the thickness mid-plane and, per side, score what
        /// fraction of that side's vertices live in the OUTER <see cref="ShieldFaceBand"/> of its own
        /// extent. A smooth face is a broad flat surface — nearly all of its vertices sit at the
        /// extreme, so it scores HIGH. A handle is a small strap standing off the plate — only a
        /// cluster reaches the extreme, so that side scores LOW. The lower score is the handle.
        /// </para>
        /// Returns false when the two scores are within <see cref="ShieldDecisionMargin"/> — an
        /// ambiguous mesh gets a Warn and the caller's existing behaviour, never a coin-flip pose.
        /// </summary>
        /// <param name="thicknessAxis">0 = X, 1 = Y, 2 = Z — the MEASURED thinnest axis of this prop
        /// in the seat frame. Passed in rather than assumed, so a native (un-normalized) shield is
        /// judged on its own authored axes.</param>
        public static bool TryResolveShieldHandleSide(GameObject prop, Transform parent, int thicknessAxis,
                                                      out bool handleOnPositiveSide,
                                                      out float positiveSmoothScore, out float negativeSmoothScore)
        {
            handleOnPositiveSide = false;
            positiveSmoothScore = 0f;
            negativeSmoothScore = 0f;
            string tName = AxisName(thicknessAxis);

            var verts = new List<Vector3>();
            CollectLocalVerts(prop, parent, verts);
            if (verts.Count < 12)
            {
                // ⚠ SAY WHICH IT IS, DON'T ASK (WO-1215). This line used to end "(Read/Write disabled
                // on the mesh?)" — a QUESTION in a device capture, which is the one thing a capture
                // must never contain: the reader is then back to theorising, and the shipped-prop
                // Read/Write trap is precisely the failure mode that "looks right in the editor and
                // is silently inert on device". Count it and state it. Measured 2026-08-26: the 25
                // Blink Shield1h_* FBXs all import with `isReadable: 0`, so on device this branch is
                // the EXPECTED path for them and the seat below is bounds-only by construction.
                CountMeshes(prop, out int meshTotal, out int meshUnreadable);
                FlowTrace.Warn("Equip", $"ShieldHandleSide '{prop.name}': only {verts.Count} readable " +
                    $"vertices — {meshUnreadable} of {meshTotal} mesh(es) have Read/Write DISABLED " +
                    "(`isReadable: 0` in the FBX .meta). The smooth-vs-handle face cannot be measured, " +
                    "so NO flip is applied: the shield's ORIENTATION is still fully derived from " +
                    "mesh.bounds (thickness -> away from the player, longest -> up), but which of its " +
                    "two faces ends up outward is unresolved and it may be worn strap-outward. FIX, if " +
                    "the owner wants the crest guaranteed outward: enable Read/Write on that FBX. " +
                    "Do NOT invent a second face heuristic — a single-renderer plate has no bounds-only " +
                    "signal that separates its two faces, so any such rule would be a coin-flip.");
                return false;
            }

            float tMin = float.MaxValue, tMax = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                float t = verts[i][thicknessAxis];
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }
            float mid = 0.5f * (tMin + tMax);
            float plusExtent = tMax - mid;
            float minusExtent = mid - tMin;
            if (plusExtent < 1e-5f || minusExtent < 1e-5f)
            {
                FlowTrace.Warn("Equip", $"ShieldHandleSide '{prop.name}': degenerate thickness on {tName} " +
                    $"(+{plusExtent:0.#####} / -{minusExtent:0.#####}) — no flip applied.");
                return false;
            }

            int plusTotal = 0, plusOuter = 0, minusTotal = 0, minusOuter = 0;
            float plusBand = tMax - plusExtent * ShieldFaceBand;
            float minusBand = tMin + minusExtent * ShieldFaceBand;
            for (int i = 0; i < verts.Count; i++)
            {
                float t = verts[i][thicknessAxis];
                if (t >= mid) { plusTotal++; if (t >= plusBand) plusOuter++; }
                else { minusTotal++; if (t <= minusBand) minusOuter++; }
            }
            if (plusTotal == 0 || minusTotal == 0)
            {
                FlowTrace.Warn("Equip", $"ShieldHandleSide '{prop.name}': all vertices on one side of " +
                    $"the {tName} mid-plane — no flip applied.");
                return false;
            }

            positiveSmoothScore = plusOuter / (float)plusTotal;
            negativeSmoothScore = minusOuter / (float)minusTotal;
            float margin = Mathf.Abs(positiveSmoothScore - negativeSmoothScore);

            // SECOND SIGNAL — the dish (docs/WEAPON_MESH_ARCHETYPES.md §2, disambiguator 4): a shield
            // is a curved plate, convex toward the world and concave toward the body, so its vertex
            // MASS sits toward the convex shell. The sign of (centroid - box centre) along the
            // thickness axis therefore names the FRONT, and the handle is the other side. Always
            // measured and always logged; only USED when the smooth-face score cannot decide.
            float sum = 0f;
            for (int i = 0; i < verts.Count; i++) sum += verts[i][thicknessAxis];
            float centroid = sum / verts.Count;
            float halfSpan = 0.5f * (tMax - tMin);
            float dishBias = halfSpan > 1e-6f ? (centroid - mid) / halfSpan : 0f;

            if (margin < ShieldDecisionMargin)
            {
                if (Mathf.Abs(dishBias) >= ShieldDishMargin)
                {
                    // Mass toward +T => +T is the convex FRONT => the handle is on -T.
                    handleOnPositiveSide = dishBias < 0f;
                    FlowTrace.Step("Equip",
                        $"ShieldHandleSide '{prop.name}': smooth-face score was ambiguous " +
                        $"(margin={margin:0.###}) — decided by the DISH instead: centroid sits " +
                        $"{dishBias:0.###} of a half-thickness toward {(dishBias > 0f ? "+" : "-")}{tName}, " +
                        $"so that side is the convex FRONT and the handle is on " +
                        $"{(handleOnPositiveSide ? "+" : "-")}{tName}.");
                    return true;
                }
                FlowTrace.Warn("Equip",
                    $"ShieldHandleSide '{prop.name}': AMBIGUOUS — smoothScore(+{tName})=" +
                    $"{positiveSmoothScore:0.###} (-{tName})={negativeSmoothScore:0.###} " +
                    $"margin={margin:0.###} < {ShieldDecisionMargin:0.##}, and the dish is flat too " +
                    $"(centroidBias={dishBias:0.###} < {ShieldDishMargin:0.##}). " +
                    "Neither face reads as the smooth one, so NO flip is applied and the existing pose " +
                    "stands. This mesh needs an owner dial, not a derived guess (WO-1123).");
                return false;
            }

            // The SMOOTH face is the high score; the handle is the other side.
            handleOnPositiveSide = positiveSmoothScore < negativeSmoothScore;
            // Disagreement between the two independent signals is not fatal — the smooth-face score
            // wins — but it is exactly the state that precedes a shield worn backwards, so it must
            // never be silent.
            bool dishSaysPositive = dishBias < 0f;
            if (Mathf.Abs(dishBias) >= ShieldDishMargin && dishSaysPositive != handleOnPositiveSide)
                FlowTrace.Warn("Equip",
                    $"ShieldHandleSide '{prop.name}': THE TWO SIGNALS DISAGREE. The smooth-face score " +
                    $"puts the handle on {(handleOnPositiveSide ? "+" : "-")}{tName} " +
                    $"(+={positiveSmoothScore:0.###} -={negativeSmoothScore:0.###}), the dish puts it on " +
                    $"{(dishSaysPositive ? "+" : "-")}{tName} (centroidBias={dishBias:0.###}). Taking the " +
                    "smooth-face answer. If the owner reports this shield worn backwards, THIS is the line.");
            return true;
        }

        // =====================================================================
        //  SWORD  (owner, 2026-08-19)
        // =====================================================================
        //
        // HER WORDS: "Find the pointy edge that goes farthest away" — the blade tip is the far end
        // along the longest axis. "The hilt is gonna be the short edge." "You find the edge that is
        // NOT sharp, and you go up to the hilt." So: identify which END does NOT taper; that end is
        // the hilt, and the grip sits just up at it. Blade points +Y; never blade-in-hand, never
        // laid flat.

        /// <summary>
        /// Which end of a seated blade is the HILT — the end that does NOT taper.
        /// <para>
        /// Profiles the section width in each end band and compares them: the sharp end tapers to
        /// near nothing, the hilt end does not. Returns false when the two ends are within
        /// <see cref="TaperDecisionMargin"/> of each other — an untapered prop (a blunt training
        /// bar, a two-headed mace) does not answer the owner's question and gets a Warn plus the
        /// caller's existing behaviour.
        /// </para>
        /// </summary>
        public static bool TryResolveSwordHiltEnd(GameObject prop, Transform parent,
                                                  out bool hiltAtMinY,
                                                  out float minEndWidth, out float maxEndWidth)
        {
            hiltAtMinY = true;
            minEndWidth = 0f;
            maxEndWidth = 0f;

            var verts = new List<Vector3>();
            CollectLocalVerts(prop, parent, verts);
            if (verts.Count < 12)
            {
                FlowTrace.Warn("Equip", $"SwordHilt '{prop.name}': only {verts.Count} readable vertices " +
                    "— the taper test cannot run; keeping the existing blade-up resolution.");
                return false;
            }

            float yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                if (verts[i].y < yMin) yMin = verts[i].y;
                if (verts[i].y > yMax) yMax = verts[i].y;
            }
            float length = yMax - yMin;
            if (length < 1e-4f)
            {
                FlowTrace.Warn("Equip", $"SwordHilt '{prop.name}': degenerate long axis {length:0.#####}m " +
                    "— taper test skipped.");
                return false;
            }

            float loEdge = yMin + length * EndBand;
            float hiEdge = yMax - length * EndBand;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                // Section "width" = distance from the long axis in the plane perpendicular to it.
                float w = new Vector2(v.x, v.z).magnitude;
                if (v.y <= loEdge && w > minEndWidth) minEndWidth = w;
                if (v.y >= hiEdge && w > maxEndWidth) maxEndWidth = w;
            }

            float bigger = Mathf.Max(minEndWidth, maxEndWidth);
            if (bigger < 1e-5f)
            {
                FlowTrace.Warn("Equip", $"SwordHilt '{prop.name}': both ends measure zero width — " +
                    "taper test cannot decide; keeping the existing blade-up resolution.");
                return false;
            }
            float rel = Mathf.Abs(minEndWidth - maxEndWidth) / bigger;
            if (rel < TaperDecisionMargin)
            {
                FlowTrace.Warn("Equip",
                    $"SwordHilt '{prop.name}': AMBIGUOUS taper — endWidth(-Y)={minEndWidth:0.#####} " +
                    $"(+Y)={maxEndWidth:0.#####} relGap={rel:0.###} < {TaperDecisionMargin:0.##}. " +
                    "Neither end reads as the pointy one, so the existing blade-up resolution stands. " +
                    "An axe/hammer reaching this line is a CLASSIFY bug, not a mesh problem.");
                return false;
            }

            // The NON-tapering (wider) end is the hilt.
            hiltAtMinY = minEndWidth > maxEndWidth;
            FlowTrace.Step("Equip",
                $"SwordHilt '{prop.name}': ySpan={length:0.####}m endWidth(-Y)={minEndWidth:0.#####} " +
                $"(+Y)={maxEndWidth:0.#####} relGap={rel:0.###} -> hilt at " +
                (hiltAtMinY ? "-Y (blade points +Y, correct)" : "+Y (blade points -Y — the prop is " +
                 "seated BLADE-DOWN, i.e. the hand is on the sharp end)"));
            return true;
        }

        // =====================================================================
        //  WHICH WAY IS "INVERTED" — PER MESH, NOT PER GAME  (owner F8 2026-08-21)
        // =====================================================================
        //
        // THE DEFECT THIS EXISTS TO END: EquipmentController._sheatheLongAxisSign is ONE serialized
        // number that decides whether prop-local +Y maps onto +body.up or -body.up at the hip. On
        // 2026-08-20 an F8 on Blaise read -1 as upside down, so +1 shipped; on 2026-08-21 an F8 on
        // the owner's Flameblade read +1 as upside down. Both reports are true. A sword's tip is at
        // prop +Y on a mesh that went through NormalizeInto + SeatHiltLowerHalf and at prop -Y on a
        // NATIVE prop whose artist authored it the other way round (SeatNative deliberately skips
        // the normalize and keeps the authored frame). So the correct sign is a property of the
        // MESH, and any single global value is guaranteed to be wrong for half the catalogue —
        // flipping it does not fix the bug, it only moves it to whoever carries the other prop.
        //
        // ⛔ AND IT MUST NOT BE VERTEX-BASED. The live props ship with Read/Write DISABLED
        // (KnightGearProofCapture's standing finding: "only 0 readable vertices"), so every
        // vertex-reading derivation in this file — TryResolveSwordHiltEnd included — is INERT ON
        // THE DEVICE. A fix that only works in the editor is not a fix. The taper test is therefore
        // tried FIRST (it is the owner's own rule, "find the pointy edge that goes farthest away",
        // and it is exact where it runs) and the BOUNDS test carries the device: mesh.bounds needs
        // no Read/Write, and the NATIVE seat's own shipped contract is "trust grip-at-origin", so
        // the end of the prop nearest the grip root's ORIGIN is the hilt and the far end is the tip.
        //
        // AXIS-AGNOSTIC ON PURPOSE. The long axis is MEASURED, never assumed to be Y: a native prop
        // keeps its authored axes, and asking a Z-long prop about its Y span reports a healthy
        // number about the wrong direction — the class of mistake that produced the flat shield.
        // The caller gets the axis back and can say so when it is not the axis the pose assumes.

        // ── SIGN-AGNOSTIC IS NOT THE SAME THING AS UNDECIDABLE (owner ruling WO-1136, 2026-08-22) ──
        //
        // Owner, verbatim: "the staff should be longest mesh on Y axis with and placed with staff
        // still verticle not horizontal".
        //
        // staff_A measures taper relGap 0.001 and grip-origin relGap 0 — its two ends are identical
        // to four decimals. Before this change that collapsed into the same single failure outcome as
        // a prop whose ends genuinely DIFFER but not by enough to decide (relGap somewhere between
        // the symmetry epsilon and the 0.15 decision margin). Those are two different facts about a
        // mesh and they want two different responses:
        //   • ends measurably IDENTICAL  -> the mesh HAS no up. There is nothing to get wrong about
        //     the sign, so demanding one is demanding an answer the geometry does not contain. What
        //     still matters, and IS measurable, is that it hangs VERTICAL rather than across the body.
        //   • ends measurably DIFFERENT but under the margin -> the mesh does encode an up and the
        //     derivation could not read it. That is a real defect and must stay a hard failure.
        // Collapsing the two is the same discard-the-distinction bug this repo found twice this week
        // (a loader returning bare null for both "pending" and "absent"). So the distinction is
        // RECORDED at the point of measurement, never re-inferred at a call site from a null-ish
        // return value.
        //
        // ⛔ THIS IS NOT AN EXEMPTION. staff_A is named in the comments as the evidence and NOWHERE in
        // a condition — grep this file for it and every hit is prose. Sign-agnostic is a
        // MEASURED property, and a sign-agnostic prop still has to pass the verticality oracle below
        // — one lying across the body fails exactly as loudly as an undecidable one.

        /// <summary>What kind of answer the mesh's own geometry was able to give about its ends.</summary>
        public enum SheathedSignDecision
        {
            /// <summary>Nothing usable was measured, OR the two ends measurably DIFFER yet by less
            /// than the decision margin. The prop encodes an up that we failed to read: a real
            /// defect, and the caller keeps its serialized fallback.</summary>
            Undecidable = 0,
            /// <summary>A sign was measured — one end is the hilt, the other the tip.</summary>
            Decided,
            /// <summary>The two ends are measurably IDENTICAL (both discriminators under
            /// <see cref="SignAgnosticSymmetryEpsilon"/>): a symmetrical prop, e.g. a quarterstaff.
            /// There is no sign to get wrong. Not a failure — but <see cref="TrySheathesVertical"/>
            /// still has to pass, because "which way up" being meaningless does not make "lying
            /// across the body" acceptable.</summary>
            SignAgnostic
        }

        /// <summary>What the mesh itself says about which end hangs DOWN when it is sheathed.</summary>
        public struct SheathedTipResolution
        {
            /// <summary>False = no SIGN was measured; the caller keeps its own fallback. Read
            /// <see cref="Decision"/> to learn whether that is because the prop is symmetrical
            /// (nothing to decide) or because it is broken (an up we could not read).</summary>
            public bool Valid;
            /// <summary>Which of the three outcomes this measurement reached. Always set, including
            /// on every early-out, so no caller has to infer it from <see cref="Valid"/>.</summary>
            public SheathedSignDecision Decision;
            /// <summary>True when vertices were readable and the taper test actually ran. The live
            /// props ship Read/Write OFF, so on device this is false and the grip-origin gap is the
            /// only discriminator there is — which is exactly what the symmetry test then judges on.</summary>
            public bool TaperMeasured;
            /// <summary>Relative width gap between the two end bands (0 = identical ends). Only
            /// meaningful when <see cref="TaperMeasured"/>.</summary>
            public float TaperRelGap;
            /// <summary>Relative gap between the two ends' distances from the grip origin, as a
            /// fraction of the long axis (0 = the grip sits exactly mid-prop).</summary>
            public float GripRelGap;
            /// <summary>Longest extent divided by the SECOND longest, in the grip root's frame. A
            /// value near 1 means there is no well-defined long axis, so no claim about verticality
            /// can be made from an axis-aligned box — the oracle refuses rather than guessing.</summary>
            public float LongAxisDominance;
            /// <summary>Angle, in degrees, between the measured long axis and the vertical ONCE THE
            /// SHEATHE POSE IS APPLIED. The pose maps grip-root-local +Y onto the vertical
            /// (EquipmentController.ComputeSheathRotation), so this is the angle between the measured
            /// long axis and grip-local +Y — the number the shipped `tiltFromVertical` trace prints.
            /// ⚠ QUANTIZED BY CONSTRUCTION: bounds are axis-aligned in the grip frame, so this reads
            /// 0 or 90 and never 45. <see cref="LongAxisDominance"/> is what catches the in-between
            /// case, where the box has no long axis to be quantized about.</summary>
            public float LongAxisOffVerticalDeg;
            /// <summary>Multiplier for body.up that puts this prop's TIP toward the ground:
            /// the value <c>EquipmentController._sheatheLongAxisSign</c> should have carried.</summary>
            public float BodyUpSign;
            /// <summary>0 = X, 1 = Y, 2 = Z — the MEASURED long axis, in the grip root's frame.</summary>
            public int LongAxis;
            /// <summary>True when the tip sits at the POSITIVE end of <see cref="LongAxis"/>.</summary>
            public bool TipAtPositiveEnd;
            /// <summary>"taper" (owner's pointy-edge rule) or "grip-origin" (the native contract).</summary>
            public string Source;
            public string Why;
        }

        /// <summary>
        /// Resolve, from THIS mesh's own geometry, the sign that hangs its tip downward at the hip.
        /// Never throws; returns false with a stated reason when the prop cannot answer, in which
        /// case the caller must keep its serialized tuning value (§12: the fallback is not stripped).
        /// <para>
        /// ⚠ A `false` RETURN IS TWO DIFFERENT FACTS AND THEY ARE NOT INTERCHANGEABLE. Read
        /// <see cref="SheathedTipResolution.Decision"/>: <see cref="SheathedSignDecision.SignAgnostic"/>
        /// means the mesh is symmetrical and HAS no sign to give (a quarterstaff — the caller's
        /// fallback is harmless because either way up is the same picture), while
        /// <see cref="SheathedSignDecision.Undecidable"/> means the mesh DOES encode an up that we
        /// could not read (the caller's fallback may well hang it upside down). Both still return
        /// false, because in neither case did we MEASURE a sign, and manufacturing one would launder
        /// a guess as a derivation — the thing M1d exists to forbid.
        /// </para>
        /// </summary>
        public static bool TryResolveSheathedTipSign(GameObject prop, Transform gripRoot,
                                                     out SheathedTipResolution resolution)
        {
            resolution = default;
            resolution.Decision = SheathedSignDecision.Undecidable;
            resolution.LongAxisOffVerticalDeg = 90f;   // "unknown" reads as the failure, never as pass
            if (prop == null || gripRoot == null)
            {
                resolution.Why = "sheathe-sign: no prop / no grip root to measure in.";
                return false;
            }
            if (!TryLocalBounds(prop, gripRoot, out Bounds b))
            {
                resolution.Why = "sheathe-sign: no measurable bounds (no renderer, or no readable mesh).";
                return false;
            }

            Vector3 s = b.size;
            int a = (s.x >= s.y && s.x >= s.z) ? 0 : (s.y >= s.z ? 1 : 2);
            int p = (a + 1) % 3, q = (a + 2) % 3;
            float length = s[a];
            // Everything the verticality oracle needs is measured HERE, at the one place that already
            // has the bounds, and carried on the struct — so no call site re-derives it and no two
            // call sites can disagree about what this prop's long axis is.
            resolution.LongAxis = a;
            float second = Mathf.Max(s[p], s[q]);
            resolution.LongAxisDominance = second > 1e-6f ? length / second : float.PositiveInfinity;
            resolution.LongAxisOffVerticalDeg = Vector3.Angle(UnitAxis(a), Vector3.up);
            if (length < 1e-4f)
            {
                resolution.Why = $"sheathe-sign: degenerate long axis ({length:0.#####} m on " +
                                 $"{AxisName(a)}) — nothing to hang either way up.";
                return false;
            }

            float lo = b.center[a] - b.extents[a];
            float hi = b.center[a] + b.extents[a];
            bool decided = false, hiltAtMin = true;
            string source = null, why = null;

            // ── 1. THE OWNER'S RULE, where the vertices can be read: the end that does NOT taper
            //       is the hilt. Exact, and it is the rule she stated in her own words.
            var verts = new List<Vector3>();
            CollectLocalVerts(prop, gripRoot, verts);
            if (verts.Count >= 12)
            {
                float loEdge = lo + length * EndBand;
                float hiEdge = hi - length * EndBand;
                float wLo = 0f, wHi = 0f;
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 v = verts[i];
                    float w = new Vector2(v[p], v[q]).magnitude;
                    if (v[a] <= loEdge && w > wLo) wLo = w;
                    if (v[a] >= hiEdge && w > wHi) wHi = w;
                }
                float bigger = Mathf.Max(wLo, wHi);
                float rel = bigger > 1e-5f ? Mathf.Abs(wLo - wHi) / bigger : 0f;
                resolution.TaperMeasured = true;
                resolution.TaperRelGap = rel;
                if (rel >= TaperDecisionMargin)
                {
                    hiltAtMin = wLo > wHi;          // the WIDER (non-tapering) end is the hilt
                    decided = true;
                    source = "taper";
                    why = $"taper on {AxisName(a)}: endWidth(-)={wLo:0.#####} (+)={wHi:0.#####} " +
                          $"relGap={rel:0.###} -> hilt at {(hiltAtMin ? "-" : "+")}{AxisName(a)}";
                }
                else
                {
                    why = $"taper AMBIGUOUS on {AxisName(a)} (relGap={rel:0.###} < " +
                          $"{TaperDecisionMargin:0.##}) — neither end reads as the pointy one";
                }
            }
            else
            {
                why = $"taper unavailable ({verts.Count} readable vertices — Read/Write is OFF on " +
                      "this prop, which is the SHIPPED state of the live weapons)";
            }

            // ── 2. THE DEVICE PATH: grip-at-origin. The grip root's origin sits at the grip, so the
            //       nearer end along the long axis is the hilt and the far end is the tip. Needs no
            //       vertices at all — mesh.bounds is always available.
            if (!decided)
            {
                float dLo = Mathf.Abs(lo), dHi = Mathf.Abs(hi);
                float rel = Mathf.Abs(dLo - dHi) / length;
                resolution.GripRelGap = rel;
                if (rel < GripEndDecisionMargin)
                {
                    // ── NEITHER TEST DECIDED. Now split the two facts that used to be one (WO-1136).
                    //    SYMMETRICAL: every discriminator we could actually run came back at
                    //    essentially ZERO — the ends are the same object. There is no sign to get
                    //    wrong, so this is not the upside-down defect. (A taper we could not run at
                    //    all cannot testify either way, so it is simply not counted; on device that
                    //    leaves the grip gap as the whole of the evidence, which is the same evidence
                    //    the device path already ships on.)
                    bool taperSaysSymmetric = !resolution.TaperMeasured
                                              || resolution.TaperRelGap < SignAgnosticSymmetryEpsilon;
                    bool gripSaysSymmetric = rel < SignAgnosticSymmetryEpsilon;
                    string gaps = $"taper" + (resolution.TaperMeasured
                                      ? $"RelGap={resolution.TaperRelGap:0.####}"
                                      : "=NOT MEASURABLE (Read/Write off)") +
                                  $" gripRelGap={rel:0.####} (symmetry bar {SignAgnosticSymmetryEpsilon:0.###}, " +
                                  $"decision bar {GripEndDecisionMargin:0.##})";

                    if (taperSaysSymmetric && gripSaysSymmetric)
                    {
                        resolution.Decision = SheathedSignDecision.SignAgnostic;
                        resolution.Source = "symmetry";
                        resolution.Why = why + $"; and the grip origin sits mid-prop on {AxisName(a)} " +
                            $"(|-end|={dLo:0.####} |+end|={dHi:0.####}). SIGN-AGNOSTIC: {gaps} — the two " +
                            "ends are measurably IDENTICAL, so this mesh HAS no upside down and no sign " +
                            "exists to be right or wrong about (owner ruling WO-1136). What still has to " +
                            "hold is VERTICALITY: longest axis on " +
                            $"{AxisName(1)} and carried upright — see TrySheathesVertical, which is the " +
                            "assertion that replaces the sign for props like this one.";
                        FlowTrace.Step("Equip", $"SheatheSign '{prop.name}': {resolution.Why} " +
                            $"longest={AxisName(a)}({length:0.####}m) dominance=" +
                            $"{resolution.LongAxisDominance:0.##} longAxisOffVertical=" +
                            $"{resolution.LongAxisOffVerticalDeg:0.#}deg.");
                        return false;   // no SIGN was measured — the caller must not be handed one
                    }

                    resolution.Decision = SheathedSignDecision.Undecidable;
                    resolution.Why = why + $"; and the grip origin sits mid-prop on {AxisName(a)} " +
                        $"(|-end|={dLo:0.####} |+end|={dHi:0.####} relGap={rel:0.###} < " +
                        $"{GripEndDecisionMargin:0.##}), so neither end is the hilt by proximity either. " +
                        "NOTHING DECIDABLE — the caller's serialized sign stands and this prop may " +
                        $"hang upside down. And it is NOT merely symmetrical: {gaps}, i.e. the ends DO " +
                        "measurably differ, so this mesh encodes an up that the derivation failed to " +
                        "read. That is a real defect, not a staff.";
                    FlowTrace.Warn("Equip", $"SheatheSign '{prop.name}': {resolution.Why}");
                    return false;
                }
                hiltAtMin = dLo < dHi;
                source = "grip-origin";
                why += $"; grip-origin on {AxisName(a)}: |-end|={dLo:0.####} |+end|={dHi:0.####} " +
                       $"relGap={rel:0.###} -> hilt at {(hiltAtMin ? "-" : "+")}{AxisName(a)}";
            }

            // Tip is the end OPPOSITE the hilt. To hang it downward, prop-local +axis must map onto
            // -body.up when the tip is at +axis, and onto +body.up when the tip is at -axis.
            bool tipAtPositive = hiltAtMin;
            // ⚠ ASSIGN FIELDS, never `resolution = new SheathedTipResolution { … }`. The measured
            // geometry (the two relGaps, the axis dominance, the seated tilt) was recorded on this
            // struct on the way down; re-constructing it here would silently blank all of it and the
            // verticality oracle would then read zeros as facts.
            resolution.Valid = true;
            resolution.Decision = SheathedSignDecision.Decided;
            resolution.BodyUpSign = tipAtPositive ? -1f : 1f;
            resolution.LongAxis = a;
            resolution.TipAtPositiveEnd = tipAtPositive;
            resolution.Source = source;
            resolution.Why = why;
            FlowTrace.Step("Equip",
                $"SheatheSign '{prop.name}': size={s:0.####} longest={AxisName(a)}({length:0.####}m) " +
                $"src={source} {why} -> tip at {(tipAtPositive ? "+" : "-")}{AxisName(a)}, " +
                $"bodyUpSign={resolution.BodyUpSign:+0;-0} (the sign that hangs the TIP DOWN). " +
                "This is PER MESH: a single global sign is wrong for half the catalogue by construction.");
            return true;
        }

        /// <summary>
        /// THE ASSERTION THAT REPLACES THE SIGN FOR A SYMMETRICAL PROP (owner ruling WO-1136).
        /// <para>
        /// Owner, verbatim: "the staff should be longest mesh on Y axis with and placed with staff
        /// still verticle not horizontal". So for a prop whose ends are measurably identical the
        /// question is not WHICH END IS UP — that has no answer and does not need one — it is whether
        /// the thing hangs UPRIGHT or across the body. That question is measurable, and the player can
        /// see the difference.
        /// </para>
        /// Three clauses, each of which can fail on its own and says which one did:
        ///   1. a long axis actually exists (dominance ≥ <see cref="VerticalCarryMinAxisDominance"/>);
        ///   2. it is Y in the grip root's frame — the axis EquipmentController.ComputeSheathRotation
        ///      maps onto the vertical, so anything else seats sideways no matter what sign is used;
        ///   3. the resulting angle off vertical is within <see cref="VerticalCarryToleranceDeg"/> —
        ///      the shipped `tiltFromVertical` trace, computed from bounds instead of a live socket.
        /// <para>
        /// ⛔ NO NAME IS CONSULTED. This is measurement all the way down, so a symmetrical prop that
        /// would lie horizontal fails exactly as hard as an undecidable one — which is the whole point
        /// of not writing an exemption list.
        /// </para>
        /// </summary>
        public static bool TrySheathesVertical(SheathedTipResolution r, out float tiltDeg, out string why)
        {
            tiltDeg = r.LongAxisOffVerticalDeg;
            if (r.LongAxisDominance < VerticalCarryMinAxisDominance)
            {
                why = $"NO LONG AXIS to be vertical about: longest/second={r.LongAxisDominance:0.###} < " +
                      $"{VerticalCarryMinAxisDominance:0.##}. An axis-aligned box around a prop that is " +
                      "tilted (or simply not long) has near-equal extents, so whichever axis measures " +
                      "'longest' is a coin flip and any verticality answer read off it would be noise " +
                      "reported as a measurement. Refusing to claim one.";
                return false;
            }
            if (r.LongAxis != 1)
            {
                why = $"long axis is {AxisName(r.LongAxis)}, NOT Y, in the grip root's frame " +
                      $"(dominance={r.LongAxisDominance:0.##}). The sheathe pose maps grip-local +Y onto " +
                      "the vertical, so this prop hangs ACROSS THE BODY — the ~90deg the shipped " +
                      "tiltFromVertical trace calls out. Owner ruling WO-1136: a staff is the longest " +
                      "mesh on Y and is placed vertical, not horizontal.";
                return false;
            }
            if (tiltDeg > VerticalCarryToleranceDeg)
            {
                why = $"long axis is Y but sits {tiltDeg:0.#}deg off vertical (> " +
                      $"{VerticalCarryToleranceDeg:0.#}deg).";
                return false;
            }
            why = $"VERTICAL: longest axis is Y (dominance={r.LongAxisDominance:0.##}), " +
                  $"{tiltDeg:0.#}deg off vertical once the sheathe pose maps grip-local +Y up.";
            return true;
        }

        /// <summary>
        /// The grip Y for a seated blade: the hilt end, then "up to the hilt" — the centre of the
        /// handle segment beneath the cross-guard when a cross-guard can be measured, else the
        /// archetype-default <see cref="SwordFallbackGripFractionFromHilt"/> up from that end.
        /// </summary>
        public static bool TryDeriveSwordGripY(GameObject prop, Transform parent, out float gripY, out string why)
        {
            gripY = 0f;
            why = "sword: not derived";

            if (!TryLocalBounds(prop, parent, out Bounds b))
            {
                why = "sword: bounds unmeasurable";
                FlowTrace.Warn("Equip", $"SwordGrip '{prop.name}': no measurable bounds — no grip derived.");
                return false;
            }
            float yMin = b.center.y - b.extents.y;
            float yMax = b.center.y + b.extents.y;
            float length = yMax - yMin;
            if (length < 1e-4f)
            {
                why = "sword: degenerate long axis";
                FlowTrace.Warn("Equip", $"SwordGrip '{prop.name}': degenerate long axis — no grip derived.");
                return false;
            }

            // Which end is the hilt? On failure fall back to the seated convention (-Y), which is
            // what EnsureHandleAtShortYEnd already resolved — never a guess of our own.
            bool taperOk = TryResolveSwordHiltEnd(prop, parent, out bool hiltAtMinY, out _, out _);
            if (!taperOk) hiltAtMinY = true;
            float hiltY = hiltAtMinY ? yMin : yMax;
            float dir = hiltAtMinY ? 1f : -1f;

            // Cross-guard = a clear width spike in the hilt-side half. Grip = midway between the
            // hilt end and that spike, i.e. the handle's centre — "you go up to the hilt".
            var widths = new float[Bins];
            var hit = new bool[Bins];
            CollectWidthProfile(prop, parent, yMin, length, widths, hit);
            float median = MedianOfHit(widths, hit);
            float binH = length / Bins;
            int spikeBin = -1;
            float spikeW = 0f;
            for (int i = 0; i < Bins; i++)
            {
                if (!hit[i]) continue;
                float by = yMin + (i + 0.5f) * binH;
                bool inHiltHalf = hiltAtMinY ? by <= yMin + length * 0.5f : by >= yMin + length * 0.5f;
                if (!inHiltHalf) continue;
                if (widths[i] > spikeW) { spikeW = widths[i]; spikeBin = i; }
            }

            bool spikeUsed = spikeBin >= 0 && median > 1e-6f && spikeW >= median * CrossGuardSpikeRatio;
            if (spikeUsed)
            {
                float spikeY = yMin + (spikeBin + 0.5f) * binH;
                gripY = 0.5f * (hiltY + spikeY);
                why = "SWORD: hilt = the non-tapering end; grip = handle centre below the measured cross-guard";
            }
            else
            {
                gripY = hiltY + dir * length * SwordFallbackGripFractionFromHilt;
                why = "SWORD: hilt = the non-tapering end; NO cross-guard measured -> archetype-default " +
                      $"grip {SwordFallbackGripFractionFromHilt:0.##} of the length up from the hilt";
            }

            FlowTrace.Step("Equip",
                $"SwordGrip '{prop.name}': ySpan={length:0.####}m hiltEnd=" + (hiltAtMinY ? "-Y" : "+Y") +
                $" taperResolved={taperOk} crossGuard=" + (spikeUsed
                    ? $"MEASURED bin={spikeBin} w={spikeW:0.#####} vs median={median:0.#####} " +
                      $"(ratio={(median > 1e-6f ? spikeW / median : 0f):0.##}, needs >= {CrossGuardSpikeRatio:0.##})"
                    : $"NOT FOUND (best w={spikeW:0.#####} median={median:0.#####}) -> ARCHETYPE DEFAULT") +
                $" -> gripY={gripY:0.####} ({(gripY - yMin) / length:0.###} up the long axis)");
            return true;
        }

        /// <summary>
        /// STAFF grip: <see cref="StaffGripFractionUpLongAxis"/> along the longest axis, per the
        /// owner's 2026-08-19 ruling — which SUPERSEDES the older "grip lower third" line in
        /// docs/WEAPON_ARMOR_ORIENT_LOGIC.md (corrected in the same change).
        /// <para>
        /// ⚠ KNOWN GAP, stated rather than papered over: the ruling measures 0.75 "up" — i.e. FROM
        /// THE FOOT toward the head — and this method takes min-Y as the foot, which is whatever
        /// WeaponBoundsOrient.EnsureHandleAtShortYEnd already resolved. It does NOT independently
        /// find the head. docs/WEAPON_MESH_ARCHETYPES.md §3 names the disambiguator that would
        /// (a local cross-section bulge in the outer ~20% of the long axis) and also notes that a
        /// plain quarterstaff has no head at all, so the ends are genuinely interchangeable. On a
        /// staff seated head-down this grip lands 0.75 from the HEAD instead. That gap is REAL and
        /// unchanged; only the owner's device screenshot can close it.
        /// </para>
        /// <para>
        /// ⚠ THE "measurement-only" SENTENCE THAT USED TO CLOSE THIS BLOCK IS RETIRED (WO-1431,
        /// 2026-09-09). It read: *"That is why the staff rule is measurement-only today (see
        /// TraceMeasuredSeat) and is not yet wired into the live melee seat."* It was true when
        /// written and became the defect: this method computed the owner-ruled 0.75 grip on every
        /// staff equip and the live path DISCARDED it, seating the shaft at ~0.18 via
        /// EquipmentController.SeatHiltLowerHalf — the owner's *"they grasp it 75% on the lower
        /// half"*. The derivation is now APPLIED, through
        /// EquipmentController.SeatMeleeGripPoint, for staves whose row passes the precedence
        /// ladder. Do not re-demote it to a prediction.
        /// </para>
        /// </summary>
        public static bool TryDeriveStaffGripY(GameObject prop, Transform parent, out float gripY, out string why)
            => TryDeriveStaffGripY(prop, parent, out gripY, out _, out _, out why);

        /// <summary>
        /// WO-1431 overload: the same derivation, additionally reporting the MEASUREMENT it was
        /// derived from (<paramref name="yMin"/> = the foot, <paramref name="length"/> = the long
        /// axis span, both parent-local). The applying caller needs these to state the grip as a
        /// FRACTION in its trace, and it must state it off THIS measurement — computing the
        /// fraction from a second bounds reader (EquipmentController has its own, coarser,
        /// world-AABB copy) would print a number that does not describe the shift that happened.
        /// Two readers of the same geometry disagreeing by a little is how a trace stops being
        /// evidence.
        /// </summary>
        public static bool TryDeriveStaffGripY(GameObject prop, Transform parent, out float gripY,
                                               out float yMin, out float length, out string why)
        {
            gripY = 0f;
            yMin = 0f;
            length = 0f;
            why = "staff: not derived";
            if (!TryLocalBounds(prop, parent, out Bounds b))
            {
                why = "staff: bounds unmeasurable";
                FlowTrace.Warn("Equip", $"StaffGrip '{prop.name}': no measurable bounds — no grip derived.");
                return false;
            }
            yMin = b.center.y - b.extents.y;
            length = b.extents.y * 2f;
            if (length < 1e-4f)
            {
                why = "staff: degenerate long axis";
                FlowTrace.Warn("Equip", $"StaffGrip '{prop.name}': degenerate long axis — no grip derived.");
                return false;
            }
            gripY = yMin + length * StaffGripFractionUpLongAxis;
            why = $"STAFF: grip at {StaffGripFractionUpLongAxis:0.##} up the longest axis (owner ruling " +
                  "2026-08-19; supersedes the retired 'grip lower third')";
            FlowTrace.Step("Equip",
                $"StaffGrip '{prop.name}': ySpan={length:0.####}m yMin={yMin:0.####} " +
                $"fraction={StaffGripFractionUpLongAxis:0.##} -> gripY={gripY:0.####}. If this ever reads " +
                "at or below the midpoint the retired lower-third rule has crept back in.");
            return true;
        }

        /// <summary>
        /// Archetype-default held rotation for a long-axis weapon: prop +Y (the blade/haft line) onto
        /// the body's UP, prop +Z (the flat) onto the body's FORWARD — blade up and away, never laid
        /// flat, never blade-in-hand.
        /// <para>
        /// ⚠ NOT the live melee seat. EquipmentController.ComputeMeleeGripRotation builds the same
        /// mapping from the RIG HAND's own axes (_handBladeAxis/_handGripUpAxis) and is unchanged by
        /// WO-1123. This is the helper's own body-derived default for callers that have no rig-axis
        /// calibration (Pets, Dungeons, editor fixtures) — a rig-axis seat is strictly better where
        /// one exists, so do not swap the melee path onto this without a screenshot per family.
        /// </para>
        /// </summary>
        public static Quaternion ComputeBladeUpRotation(Transform mount, Transform body)
        {
            if (mount == null || body == null)
            {
                FlowTrace.Warn("Equip", "BladeUpRotation: mount or body is NULL — returning IDENTITY, " +
                    "which maps the blade line onto the bone's raw +Y. On this rig that is the fist " +
                    "axis, so the weapon will read as if it grew straight out of the knuckles.");
                return Quaternion.identity;
            }
            Vector3 blade = body.up.normalized;
            Vector3 flat = body.forward;
            flat -= Vector3.Dot(flat, blade) * blade;
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Mathf.Abs(blade.z) < 0.9f ? Vector3.forward : Vector3.right;
                flat -= Vector3.Dot(flat, blade) * blade;
            }
            flat.Normalize();
            Quaternion worldTarget = Quaternion.LookRotation(flat, blade);
            Quaternion mountLocal = Quaternion.Inverse(mount.rotation) * worldTarget;
            Quaternion composed = mount.rotation * mountLocal;
            float bladeTilt = Vector3.Angle(composed * Vector3.up, blade);
            FlowTrace.Step("Equip",
                $"BladeUpRotation mount='{mount.name}': bodyUp={blade:0.##} bodyFwd={flat:0.##} -> " +
                $"mountLocalEuler={mountLocal.eulerAngles:0.#} bladeTiltFromUp={bladeTilt:0.#}deg " +
                "(must read ~0; ~90 means the blade is lying across the body).");
            return mountLocal;
        }

        // =====================================================================
        //  MEASUREMENT — the step-1 instrument (WO-1123 §4)
        // =====================================================================

        /// <summary>Measures the three extents and which local axis carries each role. This is the
        /// owner's frame vocabulary (longest/middle/narrowest), read off the renderer bounds.</summary>
        public static bool TryMeasureAxes(GameObject prop, Transform parent, out MeasuredAxes axes)
        {
            axes = default;
            if (!TryLocalBounds(prop, parent, out Bounds b)) return false;
            Vector3 s = b.size;
            int lng = (s.x >= s.y && s.x >= s.z) ? 0 : (s.y >= s.z ? 1 : 2);
            int sht = (s.x <= s.y && s.x <= s.z) ? 0 : (s.y <= s.z ? 1 : 2);
            if (sht == lng) sht = (lng + 1) % 3;
            int med = 3 - lng - sht;
            axes = new MeasuredAxes
            {
                Size = s,
                LongestAxis = lng,
                MiddleAxis = med,
                NarrowestAxis = sht,
                LongestLen = s[lng],
                MiddleLen = s[med],
                NarrowestLen = s[sht]
            };
            return true;
        }

        /// <summary>
        /// WO-1123 §4 step 1: a read-only BEFORE/AFTER measurement of a seated prop. Changes
        /// nothing — it states what the archetype rules WOULD say about this mesh next to what the
        /// live path actually did, so the CLI's Unity run has a prediction to diff against instead
        /// of a screenshot to squint at.
        /// </summary>
        public static void TraceMeasuredSeat(GameObject prop, Transform gripRoot, Transform mount,
                                             WeaponArchetype archetype, string subject)
        {
            if (prop == null || gripRoot == null) return;
            if (!TryMeasureAxes(prop, gripRoot, out MeasuredAxes axes))
            {
                FlowTrace.Warn("Equip", $"OrientMeasure '{subject}': NO measurable bounds on the seated " +
                    "prop — the derived rules cannot be predicted for this mesh.");
                return;
            }

            string prediction;
            switch (archetype)
            {
                case WeaponArchetype.Sword:
                    prediction = TryResolveSwordHiltEnd(prop, gripRoot, out bool hiltMin, out float wLo, out float wHi)
                        ? $"taper says hilt at {(hiltMin ? "-Y" : "+Y")} (endWidth -Y={wLo:0.#####} +Y={wHi:0.#####})"
                        : "taper AMBIGUOUS — no prediction";
                    break;
                case WeaponArchetype.Staff:
                    prediction = TryDeriveStaffGripY(prop, gripRoot, out float sy, out _)
                        ? $"staff grip would sit at localY={sy:0.####} ({StaffGripFractionUpLongAxis:0.##} up)"
                        : "staff grip unmeasurable — no prediction";
                    break;
                case WeaponArchetype.Shield:
                    prediction = TryResolveShieldHandleSide(prop, gripRoot, axes.NarrowestAxis,
                                                            out bool hPlus, out float sp, out float sm)
                        ? $"handle on {(hPlus ? "+" : "-")}{AxisName(axes.NarrowestAxis)} " +
                          $"(smoothScore + = {sp:0.###}, - = {sm:0.###})"
                        : "handle side AMBIGUOUS — no prediction";
                    break;
                default:
                    prediction = "no archetype prediction (Bow delegates to WeaponBoundsOrient; Unknown derives nothing)";
                    break;
            }

            FlowTrace.Step("Equip",
                $"OrientMeasure '{subject}' archetype={archetype}: {axes.Describe()} | " +
                $"seatedLocalEuler={prop.transform.localRotation.eulerAngles:0.#} " +
                $"seatedLocalPos={prop.transform.localPosition:0.####} " +
                $"mount='{(mount != null ? mount.name : "<none>")}' " +
                $"mountLocalEuler={gripRoot.localRotation.eulerAngles:0.#} | PREDICTION: {prediction}");
        }

        internal static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";

        private static Vector3 UnitAxis(int i) =>
            i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        // =====================================================================
        //  Geometry plumbing (local copies — WeaponBoundsOrient's are private and
        //  its public surface must not be widened just to share them)
        // =====================================================================

        private static void CollectLocalVerts(GameObject prop, Transform parent, List<Vector3> outVerts)
        {
            foreach (var mf in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var verts = mf.sharedMesh.vertices;
                Transform mt = mf.transform;
                for (int v = 0; v < verts.Length; v++)
                    outVerts.Add(parent.InverseTransformPoint(mt.TransformPoint(verts[v])));
            }
            foreach (var smr in prop.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                var verts = smr.sharedMesh.vertices;
                Transform mt = smr.transform;
                for (int v = 0; v < verts.Length; v++)
                    outVerts.Add(parent.InverseTransformPoint(mt.TransformPoint(verts[v])));
            }
        }

        /// <summary>How many meshes this prop carries, and how many of them are NOT readable
        /// (`isReadable: 0`). Used only to turn a Warn's question mark into a fact — the vertex
        /// walkers above already skip unreadable meshes, so this changes no decision (WO-1215).</summary>
        private static void CountMeshes(GameObject prop, out int total, out int unreadable)
        {
            total = 0;
            unreadable = 0;
            if (prop == null) return;
            foreach (var mf in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                total++;
                if (!mf.sharedMesh.isReadable) unreadable++;
            }
            foreach (var smr in prop.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null) continue;
                total++;
                if (!smr.sharedMesh.isReadable) unreadable++;
            }
        }

        /// <summary>Per-Y-bin max radial width (distance from the long axis).</summary>
        private static void CollectWidthProfile(GameObject prop, Transform parent, float yMin, float length,
                                                float[] widths, bool[] hit)
        {
            var verts = new List<Vector3>();
            CollectLocalVerts(prop, parent, verts);
            float inv = widths.Length / length;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                int bin = Mathf.Clamp((int)((v.y - yMin) * inv), 0, widths.Length - 1);
                float w = new Vector2(v.x, v.z).magnitude;
                if (!hit[bin] || w > widths[bin]) widths[bin] = w;
                hit[bin] = true;
            }
        }

        private static float MedianOfHit(float[] values, bool[] hit)
        {
            var list = new List<float>();
            for (int i = 0; i < values.Length; i++) if (hit[i]) list.Add(values[i]);
            if (list.Count == 0) return 0f;
            list.Sort();
            return list[list.Count / 2];
        }

        // ⛔ THE MEASUREMENT WAS THE BUG (owner report 2026-08-20: "redo the sword and sheild. not
        // working" — the sheathed shield rendered FLAT, like a plate at the hip).
        //
        // WHAT THIS REPLACED, and why it could never be right:
        //     Bounds wb = r.bounds;                                  // WORLD-axis-aligned AABB
        //     Vector3 c = parent.InverseTransformPoint(wb.center);
        //     Vector3 e = parent.InverseTransformVector(wb.extents);  // ← the defect
        // `r.bounds` is the renderer's AABB *in world axes*. Re-expressing that half-size VECTOR in
        // another basis and taking |components| does not yield the prop's extents in that basis —
        // it yields the world box's diagonal smeared across the new axes. The two agree only when
        // the prop happens to be axis-aligned with `parent`. Every measurement taken while the prop
        // sat at a rotated seat was therefore a different shape than the mesh.
        //
        // THE PROOF, from the owner's capture (logs/device, pid 32572), because this is exactly the
        // §12 case where a static read would have argued the old code was fine:
        //   MEASURED after hold: worldEuler=(90.00, 105.00, 0.00) worldBounds=s(0.92, 0.20, 0.81)
        // Back-solving that AABB through that rotation gives prop-local extents X=0.63, Y=0.78,
        // Z=0.20. So the mesh's NARROWEST axis is Z and its LONGEST is Y. But the pose had put
        // local Y on `outward` and local Z on `up` — i.e. ComputeShieldMountRotation was handed a
        // frame claiming Thickness=Y(0.78) and Long=Z(0.20), the exact inverse of the real mesh.
        // The solve then did its job perfectly (faceOffOutward=0deg longTiltFromVertical=0deg) and
        // stood the shield's THICKNESS up: a 0.20 m vertical extent, the dinner plate the owner saw.
        // The angles were true statements about a false frame — which is why an euler assertion
        // could not catch it and a BOUNDS-SHAPE assertion can (SheathePoseRegression case G8).
        //
        // NOW: the mesh's own local bounds, corner-transformed through the renderer's transform into
        // `parent`. That is the real OBB projected onto the parent's axes, and it is invariant to
        // whatever seat the prop happens to be sitting at when the measurement is taken — which is
        // the property every caller here already assumed it had.
        //
        // ⚠ SCOPE, deliberately: WeaponBoundsOrient keeps its OWN TryLocalBounds and is NOT touched.
        // The bow is felt-verified against that copy (owner, 2026-08-19); changing the numbers under
        // a felt-approved pose to fix a different prop is how one fix becomes two bugs. Same for
        // EquipmentController's private copy, which feeds the proportional SIZE solve, not a pose.
        private static bool TryLocalBounds(GameObject prop, Transform parent, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            bool fellBack = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;

            foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (TryRendererLocalBounds(r, out Bounds rb))
                {
                    Vector3 c = rb.center, e = rb.extents;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var p = new Vector3(
                            c.x + ((corner & 1) == 0 ? -e.x : e.x),
                            c.y + ((corner & 2) == 0 ? -e.y : e.y),
                            c.z + ((corner & 4) == 0 ? -e.z : e.z));
                        Vector3 local = parent.InverseTransformPoint(r.transform.TransformPoint(p));
                        if (!any) { min = max = local; any = true; }
                        else { min = Vector3.Min(min, local); max = Vector3.Max(max, local); }
                    }
                    continue;
                }

                // FALLBACK, kept and never stripped (§12): a renderer with no readable mesh (a
                // particle system, a procedural renderer) still has a world AABB. It is the OLD,
                // rotation-sensitive estimate, so it is announced rather than silently mixed in.
                fellBack = true;
                Bounds wb = r.bounds;
                Vector3 wc = parent.InverseTransformPoint(wb.center);
                Vector3 we = parent.InverseTransformVector(wb.extents);
                we = new Vector3(Mathf.Abs(we.x), Mathf.Abs(we.y), Mathf.Abs(we.z));
                if (!any) { min = wc - we; max = wc + we; any = true; }
                else { min = Vector3.Min(min, wc - we); max = Vector3.Max(max, wc + we); }
            }

            if (!any) return false;
            bounds = new Bounds((min + max) * 0.5f, max - min);
            if (fellBack)
                FlowTrace.Throttle("Equip", "local-bounds-fallback-" + prop.name, 5f,
                    $"TryLocalBounds '{prop.name}': at least one renderer has no readable mesh, so its " +
                    "WORLD AABB was folded in. That estimate is rotation-sensitive (it is the defect " +
                    "fixed on 2026-08-20), so any axis ordering derived from this prop is suspect while " +
                    "the prop sits at a rotated seat.");
            return true;
        }

        /// <summary>The renderer's bounds in ITS OWN local space (never world). SkinnedMeshRenderer
        /// publishes exactly this as localBounds; a MeshRenderer's comes off the shared mesh.</summary>
        private static bool TryRendererLocalBounds(Renderer r, out Bounds local)
        {
            local = default;
            if (r is SkinnedMeshRenderer smr)
            {
                if (smr.sharedMesh == null) return false;
                local = smr.localBounds;
                return local.size.sqrMagnitude > 1e-12f;
            }
            var filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            local = filter.sharedMesh.bounds;
            return local.size.sqrMagnitude > 1e-12f;
        }
    }
}
