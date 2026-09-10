# Weapon / Armor / Item Auto-Orient Logic — BINDING CANON

> **Owner mandate (2026-06-13):** this logic was worked out in detail and **prior sessions never
> applied it** — weapons got slapped onto the hand at identity and sat wrong (blade flat / pointing
> forward, gripped by the blade). **Do NOT re-derive this worse. Apply it. Manual corrections are
> canon and must never be overwritten by the auto heuristic.** Referenced from `docs/ARCHITECTURE.md`
> and `docs/ARCHITECTURE_PRINCIPLES.md`; read before touching any weapon/armor attach or item placement.

## The principle
**Orient any weapon / armor / placeable from its NAME + its MESH BOUNDS — deterministically — never at
identity.** The name tells you the *archetype*; the bounding box tells you the *axes*. Together they
yield a best-estimate transform with no guessed Euler. A rare human nudge then perfects it and *teaches*
the heuristic.

> **⚠ UPDATED 2026-08-19 (WO-1123, owner ruling — BINDING).** `WeaponOrientHelper` now EXISTS
> (`Assets/_Modules/Core/Geometry/WeaponOrientHelper.cs`, assembly `DeNelle.Core`) and `manual` is
> READ on the gear side for the first time. Two lines below are **superseded** and are marked in
> place: the staff's grip (**lower third → 0.75 up the long axis**) and the shield's rule (now
> stated as thickness-away-from-the-player + handle-inward). Nothing else in this doc changed.

## The shared axis frame (owner, 2026-08-19 — verbatim)

> "**Y = the LONGEST dimension, X = the MIDDLE dimension, Z = the NARROWEST dimension.**"

Every archetype rule is expressed in that frame, and longest/middle/narrowest are **MEASURED off the
bounds, never assumed off the FBX import**.

**⚠ Naming vs. the shipped seat, deliberately NOT reconciled.** The seated prop frame this project
actually ships (`WeaponBoundsOrient.AlignAxesYLongXNarrowZWide`) puts longest→+Y, **narrowest→+X,
middle→+Z** — X and Z swapped relative to the naming above. The *names* differ; the *geometry* does
not. Every rule in `WeaponOrientHelper` is written against the measured ROLE (longest / middle /
narrowest) and then mapped onto whichever local axis that role actually landed on, verified by
measurement each time. Re-permuting the shipped align to match the letters would rotate the
**felt-verified bow** 90° about its long axis for a documentation reason. If the seated frame is to
be re-lettered, that is its own ticket with a screenshot per family.

## The bounds rule (geometry → orientation)
Measure combined renderer bounds, then:
- **Longest axis → the item's primary direction.** For a blade/staff/bow that's the LENGTH → stand it
  **vertical (longest axis → world +Y)**.
- **Narrowest axis → the flat / thickness** (a blade is thin flat-to-flat; a shield is thin front-to-back).
- **The grip/seat is at ONE END of the longest axis** — identified by where the cross-section *changes*
  (a sword's cross-guard *widens*; opposite the tapering point).

This is exactly what `CatalogOrientationBaker` already does for **structures** (longest→+Y, base-centre to
origin) and what `HeroBowAttachment.NormalizeInto` does for the **bow**. The system below GENERALIZES the
bow's logic to every weapon + armor.

## Worked example — a SWORD (the canonical case)
1. **Title = "sword"** → bladed weapon → grip rules apply.
2. **Bounds:** narrowest axis = blade thickness; longest of the other two = blade LENGTH; middle = width.
3. **Rotate longest axis → +Y** → sword stands **vertical, blade up**, narrow axis = the flat face.
4. **Hilt end** = the end where the cross-section widens (cross-guard), opposite the point.
5. **Grip point** = the handle **just below the cross-guard** → align to the `RightHand` bone.
Result: vertical, blade up, hand below the hilt — never blade-in-hand or laid flat.

(Archetype rules extend per type — **as ruled by the owner on 2026-08-19 and implemented in
`WeaponOrientHelper`**; the two superseded lines are struck, not deleted, because a reader will find
the old wording in an older copy and must be able to tell which is current:)

- **SWORD** — *"Find the pointy edge that goes farthest away"* (the tip is the far end of the longest
  axis); *"the hilt is gonna be the short edge"*; *"you find the edge that is NOT sharp, and you go up
  to the hilt."* So the **non-tapering end is the hilt**, the blade points **+Y**, and the grip sits
  just up at the hilt — the handle centre below the cross-guard when one can be measured, else an
  archetype-default fraction up from that end. Never blade-in-hand, never laid flat.
- **STAFF** — ~~grip lower third~~ **SUPERSEDED 2026-08-19.** Owner, verbatim: *"The longest length is
  Y, and you go three quarters of the way up Y, and that can be where the hand is attached."*
  → **grip at 0.75 along the longest axis** (`WeaponOrientHelper.StaffGripFractionUpLongAxis`).
- **SHIELD** — ~~flat face forward, centre→hand~~ **RESTATED 2026-08-19.** Owner, verbatim: *"One side
  is gonna be relatively smooth, the other side is gonna have a handle. You take the thinnest side of
  the object, which will generally be the Z, but whichever of the three is the shortest is the
  thickness of the shield"* … *"the thinness/thickness of the shield is facing away from the player,
  with the handle where the hand mounts on the off-player's hand."* So: **narrowest measured extent =
  the thickness = the face normal**, pointed **away from the player** (on the back socket that is
  −body.forward); the **handled (non-smooth) face turns inward** to the mount. Note *"whichever of the
  three is the shortest"* is the owner explicitly refusing an axis-NAME rule — the measurement is the
  authority, which is also what lets the rule work on a NATIVE prop that skipped the align.
- **BOW** — unchanged and **felt-verified by the owner (2026-08-19)**. `WeaponOrientHelper`'s Bow
  archetype DELEGATES to `WeaponBoundsOrient` verbatim; it is the template, not a target.
- **axe / hammer / mace / wand / crossbow** — **no rule exists yet**, so they classify as `Unknown`
  and DERIVE NOTHING: the caller keeps its existing behaviour and a `FlowTrace.Warn` says so. Each
  family's rule leans on a property these lack (an axe head does not taper, so the sword's
  "which end is not sharp" test would confidently pick the wrong end). Ask the owner; do not guess.
- **helm/chest** → fit to the head/torso socket, no hand grip. (Unchanged, not yet implemented.)

## The system — `WeaponOrientHelper` (generalize `HeroBowAttachment.NormalizeInto`)
1. **Estimate:** `name → archetype` + `bounds → axes` ⇒ best-estimate `{rotation, gripOffset, scale}`.
2. **Apply at EQUIP:** `EquipmentController` calls the helper when a weapon attaches → it self-orients
   onto the hand socket the Humanoid rigs expose (any enemy/hero × any weapon).
3. **Dev-build adjust:** the Orientation Inspector / DevOrient (RAID-launched, our tooling) lets the
   owner nudge the offset live; it saves the corrected transform with **`manual=true`**.
4. **`manual=true` is CANON** — the auto heuristic preserves it untouched, forever (same rule
   `CatalogOrientationBaker` already enforces for structures).
5. **Statistical enhancement:** compare the auto-estimate vs the manual correction across items; the
   accumulated deltas refine the archetype defaults (e.g. "swords consistently need +5° → fold it into
   the rule"). Corrections *teach* the next estimate.

## Precedence — the ONE order, asserted in code (WO-1123)

```
authored offset row  →  manual: true  →  derived  →  archetype default
```

`WeaponOrientHelper.ResolveSource(hasAuthoredOffset, manual, canDerive)` **is** that order, as one
pure function, so no call site can re-order it by accident and `AttachmentOffsetRegression`'s
`seat-precedence` case asserts it without a scene.

**`manual` is now READ.** `WeaponDef.manual` (`GearCatalog.cs`) exists; `EquipmentController
.IsManualOrientRow` is its one gear-side reader. 81 of the 96 rows in `weapons.json` author it and,
until 2026-08-19, **nothing declared the field** — the flag read as protection and protected nothing.
A `manual: true` row is left **exactly as loaded**: not normalized, not rotated, not shifted, so a
second pass over it is a zero delta by construction.

> ### ⚠ UPDATED 2026-08-26 (WO-1215, BINDING) — `manual` MUST NAME A CORRECTION THAT EXISTS
> The sentence that used to close this paragraph — *"The 15 hand-authored rows (including
> `knight_shield_starter`, the live default shield) are the ones eligible for derivation"* — was
> **true of the flag and false of the world**, and it is retired. Reading the raw flag straight into
> the ladder left **18 of the 19 shields at IDENTITY**, sitting through the hero's body
> (`tmp/shield-seat-101829.png`, owner felt-test 2026-08-26).
>
> **Measured, from the shipped catalogs:** 77 of the 81 `manual: true` rows also carry
> `generated: true` — yet `GearCatalogGenerator.cs:386-387` emits a fresh row as
> `["generated"] = true, ["manual"] = false` with the comment *"set true by hand to lock the row
> forever"*. **The generator never writes that pair.** It exists because commit `af96fe788`, a
> **data-only WO-500 balance pass**, stamped it across all 65 `blink_` rows — its own body records
> *"blink_ rows 65, manual=true 65/65"* and, two paragraphs later, predicts this exact defect:
> *"offsets.json has authored seating for exactly sword_A/D/F/G + shield_A … and ZERO blink mesh key
> has authored seating. The shelf traded dialed weapons for un-dialed ones. Screenshot-class risk,
> not log-class."*
>
> **The rule now:** `manual: true` is honoured when it names a correction that exists —
> `WeaponOrientHelper.ManualSeatIsSubstantiated(manual, rowIsGenerated, hasAuthoredSeat)`:
>
> ```
> substantiated = manual AND (an Offset Forge row exists  OR  the row is not machine-generated)
> ```
>
> - A **hand-authored** row (`generated: false`) claiming `manual` is trusted unconditionally — a
>   human wrote the row, so a human may have meant the flag.
> - A **generated** row **with** an authored seat is canon: `tripo_shield_a` → `shield_A` keeps its
>   hand-dialled `rot -160/-180/-84, pos 0.12/-0.01/0, scale 1.04, fullOverride` untouched.
> - A **generated** row with **no** authored seat protects nothing but IDENTITY, so it no longer
>   vetoes derivation. That is the 18 `blink_shield1h_*`.
>
> The **ladder itself is unchanged** — `ResolveSource` still reads authored → manual → derived →
> archetype default. What changed is the VALUE fed to its `manual` input. Call sites take the 3-arg
> `MayDerive(hasAuthoredOffset, manual, rowIsGenerated)`. Pinned by
> `AttachmentOffsetRegression` case `shield-seat-substantiation`, which asserts the truth table, the
> gate, every shield row in the live catalog, and `shield_A`'s dialled numbers value-by-value.
>
> ⚠ **Residual, named rather than guessed:** the 25 `Shield1h_*` FBXs import with `isReadable: 0`,
> so on device the smooth-vs-handle face score cannot be measured and **no 180° face flip is
> applied**. The seat is still fully derived from `mesh.bounds` (thickness → away from the player,
> longest → up); only *which* of the two faces ends up outward is unresolved, and the Warn says so
> by count. A single-renderer plate has **no bounds-only signal** separating its faces — the fix is
> enabling Read/Write on those FBXs, never a second heuristic.

**Ambiguity falls back, it never guesses.** Every measured decision carries a decision margin — the
sword's taper gap, the shield's smooth-vs-handle face score, the shield's plate-shaped check. Under
the margin the helper emits a `FlowTrace.Warn` naming both measurements and **returns the caller to
its existing behaviour**. The hand-typed constants (`Shield` preset euler, `_sheatheOffHandLocalEuler
= (0,90,192)`) are **kept in the code as that documented fallback** — never deleted (CLAUDE.md §12).

## What is wired live, and what is measurement only (WO-1123, 2026-08-19)

| path | state |
|---|---|
| **shield, drawn** | **DERIVED** (both native and normalized props), global weapon yaw withheld |
| **shield, sheathed** | **DERIVED** off the back socket with outward = −body.forward; the Seating Editor preview shares the same method so the two can never disagree |
| **bow, drawn + sheathed** | unchanged — felt-verified |
| **staff, drawn + sheathed** | grip point **DERIVED** (0.75 up the long axis) via `EquipmentController.SeatMeleeGripPoint`, precedence-gated; sword/dagger and every Unknown family keep the hilt-lower-half seat and the read-only prediction. (updated 2026-09-09, WO-1431 lane HERO-GRIP) |

**Companion reference:** `docs/WEAPON_MESH_ARCHETYPES.md` — what each archetype's mesh *is* in
measurable terms (the bin/profile-curve primitive and the per-family **disambiguator** that separates
the two ends of an axis, which a bounding box can never answer). This doc is the *canon*; that one is
the *dictionary* the helper classifies against.

## ⚠ ADDED 2026-08-26 (WO-1226) — INSTRUMENT THE SEAT, AND WHAT IT ALREADY PROVED

Three facts, sourced from the code, that a future session must not re-derive:

**1. The `tiltFromVertical` trace is NOT a seat measurement, and never was.** The line
`sheathed long axis on '<hero>': tiltFromVertical=0deg` is computed inside
`EquipmentController.ComputeSheathRotation` as `(socket.rotation * result) * Vector3.up`, where
`result` is the quaternion that method is *about to return*. It (a) asks `Vector3.up` instead of the
mesh, so a prop whose long axis landed on grip-local X or Z reads a perfect 0 while hanging
sideways; (b) is composed *before* `ApplySheathedOffset` writes more rotation onto the same
transform; and (c) **has no counterpart at all on the DRAWN branch**. It is a true statement about
the deriver's output. The owner's broken build prints `0deg`. **Never accept it as proof a prop is
standing.**

**2. The real instrument is `EquipmentController.TraceSeatChain`** (`[Flow:Equip] SEAT CHAIN
[DRAWN|SHEATHED] …`), called at the END of both `ApplyHoldPose` main-weapon branches, after the last
rotation write. It measures the prop's own long axis (`WeaponOrientHelper.TryMeasureAxes`, from
`mesh.bounds` — shipped props may import with Read/Write OFF, which makes vertex approaches silently
inert on device) and pushes it through the chain step by step: **step1** prop→grip-root (is the long
axis even grip-local +Y?), **step2** the grip root's own localRotation, **step3** the parent bone /
socket world rotation, **step4** the seated world direction and its tilt off the body's vertical —
plus an ATTRIBUTION line naming *which step turned it how far*. The pure half
(`MeasureSeatedLongAxis`, `ComposeMeleeGripRotation`, `ComposeDrawnMeleeLocalRotation`) is
`public static` so `AttachmentOffsetRegression` case `drawn-seat-verticality` asserts the SHIPPED
composition instead of re-typing it.

**3. `renderers=2` on a staff is a TrailRenderer, and the "staff measures as a cube" was a
DIAGNOSTIC lie.** `WeaponTrailController.EnsureTrail` parents a code-built `TrailRenderer`
("WeaponTrail") onto `EquipmentController.GripRoot`; its `bounds` is the world AABB of the swing
ribbon, which the `parent-scale compensate` line was encapsulating with the prop's, inflating a
1.3 m shaft into a ~1.5 m box. **No deriver ever read it** — every orient path measures the *prop*
(the grip root's child), and `WeaponOrientHelper.TryLocalBounds` reads mesh-local bounds off a
MeshFilter/SkinnedMeshRenderer, which a TrailRenderer has neither of. The compensate line now
measures geometry-owning renderers only and names the excluded ones.

### The open ruling this instrument surfaced — the DRAWN staff
With the shipped serialized defaults `_handBladeAxis (0,1,0)` and `_handGripUpAxis (0,0,1)`,
`ComposeMeleeGripRotation` is `Quaternion.LookRotation((0,0,1),(0,1,0))` — **exactly the identity**.
`_staffGripEuler` ships as `(0,0,0)` and neither `staff_A` nor `tripo_staff_a` has an `offsets.json`
row, so the entire drawn staff seat is `Euler(0,180,0)` and the shaft lands on the hand bone's raw
local +Y: **the sword rule, applied to a staff**. Nothing is 90° "wrong"; there is simply no staff
archetype in the drawn seat. The owner's rule (*"the pointed object is Y top, flat is bottom"*, and
the STAFF entry above) says a held staff stands. **Fixing it means an owner-ruled staff drawn
correction plus a device screenshot — and it collides head-on with
`AttachmentOffsetRegression.Case5_StaffNeutralDefault`, which PINS `_staffGripEuler` to neutral as a
WO-970 residual.** The two cases now disagree deliberately. Do not silence either; do not flip
`_sheatheLongAxisSign` (WO-1136 — that only moves the defect onto the other heroes).

**The SHEATHED main-hand path is not the fault here.** `ComputeSheathRotation` is genuinely
body-derived (`LookRotation(worldFlat, worldBlade)` off the body's own axes, long axis on
`body.up * sign`), so the hip carry does stand upright. The owner's report is about combat, i.e.
DRAWN — the one state that had no trace.

## Existing groundwork (extend, do NOT rebuild)
- `Assets/Editor/CatalogOrientationBaker.cs` — bounds-orient for the structures catalog (longest→+Y,
  base-to-origin, `{euler,offset,scale,note}`, `manual=true` preserved). The template.
- `WeaponBoundsOrient` (`Assets/_Modules/Core/Geometry/`) — the bow's bounds-based auto-orient
  (`NormalizeInto` / `ComputeBowHeldRotation` / `TryAspectRatio`). **The seed. Do not modify it.**
- `WeaponOrientHelper` (`Assets/_Modules/Core/Geometry/`) — **the generalization (WO-1123, 2026-08-19).**
  One entry point (`TrySeat`) takes a prop + an archetype's stipulations; `Classify` maps name/category
  → archetype; `ResolveSource` is the precedence ladder; `TryResolveShieldFrame` /
  `ComputeShieldMountRotation` split the shield seat into one measured half (cached per attach) and a
  cheap per-pose half; `TraceMeasuredSeat` is the read-only measurement instrument. In `DeNelle.Core`
  so Village, Pets and Dungeons can all read it across the asmdef boundary.
- `Assets/_Modules/Village/Hero/EquipmentController.cs` — where weapons equip/attach (the apply point).
- The **Orientation Inspector** (manual correction tool) + DevOrient/RAID (the dev-adjust UI).

## The mandate (why this doc exists)
"The words and dimensions alone tell you one thing." Deriving the transform from name + geometry makes
weapon/armor placement **deterministic and reusable** — do it right once → every item self-orients →
unlimited weapons × unlimited wearers with no per-asset hand-tuning. **Future sessions: apply this; build
`WeaponOrientHelper` on the existing baker/normalizer; never overwrite a `manual=true` correction.**
