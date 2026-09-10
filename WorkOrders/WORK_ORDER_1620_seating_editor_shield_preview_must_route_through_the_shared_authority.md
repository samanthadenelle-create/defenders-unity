# WO-1620 - The Seating Editor's shield preview derives its own rotation: the last second decider for "where does a shield sit"

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-09 (CLI, main-line banner; bumped 1619 -> 1621 in the SAME edit)
**Silo / Lane:** Hero + troop equipment seating (Hygiene silo)
**Severity:** P2 process - not a crash and not (yet) a visible defect on the shipped hero. It is a
**WYSIWYG break in the owner's own tuning tool**: she dials a shield delta against a pose the game
does not render, so every nudge she saves is measured from the wrong baseline. WO-994 is the named
precedent for exactly this class of bug.
**Type:** EXISTING system. Both deciders already exist; one of them should not.
**Owner words:** **none - this is a lane finding.** Surfaced by the WO-1616 NPC-SHIELD lane, which
was told not to touch it (changing it changes what the owner sees while dialling) and flagged it
instead: *"Named, not fixed - it deserves its own ticket."*
**Provenance:** `WorkOrders/WORK_ORDER_1616_raid_npc_shield_seats_at_a_hard_coded_offset.RESULT.md`
sec.3, "ONE HONEST QUALIFIER on 'exactly one call site'".

---

## 0. Why this exists NOW, and not before

Before WO-1616 there were **three** places that decided a shield's rotation: the hero attach path,
`TroopGearApplier`'s hard-coded triple, and this preview. WO-1616 deleted the troop constant and
joined the hero and the NPC onto one `public static` authority. **The preview is the one that is
left.** It is not a new divergence - the WO-1616 RESULT states it was *"already divergent from the
attach path before this ticket"* - it is simply the last one visible now that the others are gone.

## 1. The defect, source-proven (every line opened 2026-09-09)

### 1a. Where it actually lives - THE BRIEF'S FILE POINTER WAS WRONG

The lane brief said to grep `Assets/Editor` for the shield preview. **It is not there.**
`Assets/_Modules/Village/UI/SeatingEditorOverlay.cs` is the in-game UI shell; the seat math is in the
runtime controller:

**`Assets/_Modules/Village/Hero/EquipmentController.cs`, `ApplySeatingPreview` (`:5079`):**

```
:5182   bool shieldPreview = offHand && !fullOverride && _currentOffHandDerivable &&
:5183                        _previewShieldFrame.Valid && grt.parent != null;
:5184   if (shieldPreview)
:5185   {
:5186       Transform shieldPreviewBody = _animator != null ? _animator.transform : transform;
:5187       baseRot = WeaponOrientHelper.ComputeShieldMountRotation(
:5188           _previewShieldFrame, grt.parent, shieldPreviewBody.forward, shieldPreviewBody.up);
:5189   }
```

*(Recorded so the next seat does not lose the same twenty minutes. `GearCasterWindow.cs` and
`DeNelleToolsHub.cs` also mention "Seating Editor" and are **not** it.)*

### 1b. Three concrete divergences from the runtime authority

The runtime authority is `EquipmentController.SeatShieldMountRotation` (`:2190`, per WO-1616 RESULT
sec.3), whose documented chain is `GearSeat.GetShieldAxes` -> `TryComputeShieldMountRotation` ->
`EnsureShieldOuterFaces` -> write `gripRoot.localRotation` (the chain is written at
`EquipmentController.cs:2126`).

| # | the authority does | the preview does | evidence |
|---|---|---|---|
| 1 | derives outward/up via `GearSeat.GetShieldAxes(animator, body, ...)` | passes **raw** `body.forward` / `body.up` | `GearSeat.cs:207`; preview `:5188` |
| 2 | calls `GearSeat.EnsureShieldOuterFaces` - the outer-face correction, which **logs when the opening/inner was aimed wrong** | **never calls it** | `GearSeat.cs:249`, warn text `:270`; absent from `:5184-5189` |
| 3 | is precedence-gated by `mayDerive` (an owner-dialled seat wins) | has no precedence gate at all | `EquipmentController.cs:2190` signature vs `:5182` |

Divergence 1 is the one with teeth: `GetShieldAxes` projects the body's left onto the plane
perpendicular to the forearm. Raw `body.forward/up` ignores the arm entirely, so the preview and the
game agree **only** when the forearm happens to lie along the body axes.

### 1c. What is NOT proven, and must not be written as if it were

- **No captured frame, screenshot or trace of the two poses side by side exists.** The divergence is
  proven **structurally** (three named calls present on one path, absent on the other) - not
  **measured**. CLAUDE.md sec.11B: a code-read LOCATES; it does not measure a delta.
  **The first deliverable is that measurement** (sec.5 step 1).
- **The magnitude is unknown.** It could be a few degrees on the shipped rig or it could be large.
  Do not claim either.
- **No regression currently pins the preview's math.** Grep of `Assets/Editor/Regression/*.cs` for
  `ApplySeatingPreview` / `shieldPreview` / `_previewShieldFrame` returns **zero hits**
  (2026-09-09). `SheathePoseRegression.cs:1485` and `:1542` drive
  `WeaponOrientHelper.ComputeShieldMountRotation` directly as an oracle of **the helper**, not of the
  preview. So there is no oracle to break - and no oracle proving the preview today.

## 2. Target

"Where does a shield sit?" is answered in exactly ONE place, and the Seating Editor shows the owner
the same pose the game ships. The preview stops being a second implementation and becomes a caller.

## 3. Architecture ruling - ONE OWNER PER CONCERN

`docs/ARCHITECTURE_PRINCIPLES.md` **2b.1**. The preview and the attach path answer the identical
question, so they execute the identical instructions.

The file itself already states the rule in its own comments, twice:

- `:5117-5119` (bow, WO-1105 R4): *"the Seating-Editor preview must seat a bow the SAME way the
  attach path does, or the preview would show a grip the game never uses."*
- `:5124-5129` (WO-1431): *"the preview MUST dispatch the grip on the same archetype + the same
  derivability the attach path used, or the owner dials a nudge against an 0.18 baseline and the
  game ships against an 0.75 one - two baselines, the exact class of drift..."*
- and `docs/WEAPON_ARMOR_ORIENT_LOGIC.md`, quoted in that block: *"the Seating Editor preview shares
  the same method so the two can never disagree."*

**The shield is the row where that sentence is not true.** This ticket makes it true. It is not a new
principle - it is the existing one, applied to the slot that was skipped.

### 3a. The design constraint the lane MUST NOT improvise past

`SeatShieldMountRotation` **writes** `gripRoot.localRotation`. The preview needs a **`baseRot`
value** that it then composes:

```
:5191   grt.localPosition = gripPos + pos;
:5192   grt.localRotation = bowPreview || shieldPreview
:5193       ? baseRot * Quaternion.Euler(euler)          // derived: no global yaw
:5194       : ApplyGlobalWeaponYaw(baseRot * Quaternion.Euler(euler));
```

So the join is one of exactly two shapes, and the lane picks one and says why:

- **(a) a compute-only entry point on the authority** that returns the rotation without writing it,
  with the existing `SeatShieldMountRotation` becoming a thin write-wrapper over it; or
- **(b) call the authority to write, read the result back off `gripRoot.localRotation`, then compose
  the delta.**

[STOP] **What is forbidden is a THIRD derivation** - including "the same chain, inlined here", or copying
`GetShieldAxes` + `EnsureShieldOuterFaces` calls into the preview. Copying the steps is the bug this
ticket is about. **(a) is the shape WO-1616 used** (it lifted the hero's steps into `public static`
entry points and made both callers execute them); prefer it unless the lane can state a reason.

### 3b. The frame has one owner too

The preview **re-measures** its own `ShieldFrame` at `:5141-5144`, deliberately, for the WO-1123
reason written at `:5136-5140`: the cached attach-time frame was measured on the runtime seat, which
for a NATIVE shield is `SeatNative`, while the preview re-seats through `NormalizeInto` - *"Reusing
the attach frame across that difference would pose the preview off the wrong axis."*

**That reasoning survives this ticket.** The preview keeps measuring its own frame; it stops deriving
its own **rotation from** that frame. If the chosen join makes the authority re-measure internally,
the preview must be able to hand its own frame in - **one frame, one owner, stated explicitly in the
RESULT.** Do not silently collapse the two.

### 3c. Preserve the owner's precedence, and note the gate

Divergence 3 is real but it is **not** a licence to change what a dialled offset does. If the
authority's `mayDerive` gate is threaded in, an owner-dialled shield seat must keep winning exactly
as it does today at runtime (`rule=SHIELD-SEAT-OWNED-BY-PRECEDENCE`, WO-1616 RESULT sec.5). Say in
the RESULT which value the preview passes for `mayDerive` and why. `_currentOffHandDerivable` is
already the preview's own derivability input at `:5182`.

## 4. THE BEHAVIOUR CHANGE, STATED UP FRONT

[WARN] **This ticket CHANGES WHAT THE OWNER SEES while dialling a shield.** That is the point, and it is
also why WO-1616 refused to do it in passing.

**Consequence to state plainly in the RESULT and to the owner:** any shield offset she has already
dialled and saved was measured against the **old** preview baseline. Once the preview matches the
game, a previously-dialled delta may render differently **in the editor**. It does not change what
the game ships (the runtime path is untouched) - it changes whether the tool agrees with the game.

**Do not "helpfully" migrate saved offsets.** If a saved shield delta looks wrong after this, that is
a finding for the owner to rule on, and it gets its own ticket. Recording it is the job; silently
rewriting her authored data is not.

## 5. Lane split

**Step 1 - MEASURE THE DELTA FIRST (CLAUDE.md sec.12).** Before the join, capture both rotations for
the shipped shield on the shipped rig - the preview's `baseRot` at `:5187` and the authority's result
for the same frame/parent - and print the angle between them (`Quaternion.Angle`). **That number is
the ticket's evidence.** A near-zero delta is still a finding: the join is still right on
one-owner grounds, and the RESULT would say the visible impact was small. **Never write "the preview
was badly off" without this number.**

**Step 2 - JOIN.** Implement 3a. `EquipmentController.cs` only. The runtime attach path is a
**call-site-compatible** change at most; if the chosen join touches the hero's attach path,
call it out loudly - WO-1616 sec.5 is explicit that a shield-seating ticket must not become a hero
visual change.

**Step 3 - REGRESSION.** Sec.7.

One lane, one file - `EquipmentController.cs` is a same-file bottleneck and carries other seats'
uncommitted work (sec.8).

## 6. Pins - what must not move

- `EquipmentController.SeatShieldMountRotation` (`:2190`) and `SeatShieldPlateOnSocket` (`:2265`) -
  the WO-1616 authority. **Extend by adding an entry point; never fork.**
- `TroopGearApplier.SeatShieldOnHand` must keep calling the same authority and must keep emitting its
  device line (`rule=SHIELD-DERIVED-THICKNESS-OUTWARD` / `SHIELD-NOT-DERIVED` /
  `SHIELD-SEAT-OWNED-BY-PRECEDENCE`, WO-1616 RESULT sec.5). Those `rule=` tokens are what a triage
  seat greps - **byte-identical or it is a breaking change**.
- The hero's existing trace strings, including the `(arm=...)` token threaded through
  `SeatShieldPlateOnSocket`'s `extraTraceToken` (WO-1616 RESULT sec.2 records it was nearly lost once
  already). F8 captures grep these.
- The bow preview branch (`bowPreview`, `:5168-5175`) and `nativeMeleePreview` - untouched.
- The WO-1123 frame re-measure at `:5141-5144` and its reasoning comment (sec.3b).
- The scale composition at `:5199-5200` (`ParentScaleCompensation`) - the WYSIWYG break proven
  2026-07-07 lives there and is **not** this ticket.
- `_seatEditMode` reset semantics at `:5105-5109`.
- **`WeaponOrientHelper.cs` and `GearSeat.cs` are READ-ONLY here.** The fix is in the caller.

## 7. RED-first suite spec

New `Assets/Editor/Regression/SeatingPreviewShieldRegression.cs`, markers
`SEATING_PREVIEW_SHIELD_OK` / `SEATING_PREVIEW_SHIELD_FAIL`. **BEHAVIOURAL, not source-text** - this
code is in `DeNelle.Village`, which the regression assembly already reaches (unlike the raid builders,
WO-1619 sec.6), so there is no excuse for a lint.

- **`CaseOneOwner` (the load-bearing case).** Seat the same shield twice - once through the preview
  path, once through the authority the way the attach path does - on a hand carrying a
  **deliberately awkward rotation** (WO-1616 used `Euler(37,101,-23)`; reuse it) and assert the two
  agree within 0.5 deg.
  **RED at HEAD** because the preview uses raw `body.forward/up` and skips `EnsureShieldOuterFaces`.
- **`CaseControlCannotPassForFreeAtIdentity`.** [STOP] **Read this before writing the suite.** WO-1616
  RESULT sec.4 item 1 proved that **on a hand at identity, a wrong shield seat lands on the same
  world line as the right one** - an oracle written on a clean rig goes GREEN against broken code.
  So: a CONTROL that runs the OLD raw-axes computation and **FAILS IF THE CONTROL IS LESS THAN
  30 deg OFF** on the fixture. This is the WO-1138 "a probe that cannot fail" guard. **A suite
  without this case is not acceptance.**
- **`CaseNoSecondDerivation`.** The preview does not itself call `GetShieldAxes`,
  `EnsureShieldOuterFaces` or `ComputeShieldMountRotation`. This one is a source lint and is
  **supplementary**, not the coverage (CLAUDE.md sec.8's own warning).
- **`CaseDialledDeltaStillComposes`.** A non-zero `euler` delta still composes onto the derived base
  and the global yaw is still withheld for a derived seat (`:5192-5194`).
- **`CaseOwnerPrecedenceStillWins`.** Whatever sec.3c decides, pin it.

**Measure the SEATED transform, not a returned number.** `docs/WEAPON_ARMOR_ORIENT_LOGIC.md`
(WO-1226 note 1): a trace can print a perfect `0deg` about a prop hanging sideways. Recompute
face-off-outward from `gripRoot.rotation * frame.ThicknessAxis` after the seat ran, as
`TroopShieldSeatRegression` does.

**Name the mutation in the RESULT:** restore the raw `body.forward/up` call at `:5187-5188` -
`CaseOneOwner` reds.

**Registration:** hand the `DataRegression.cs` line back to the lead. That file is lead-owned; the
lane does not edit it (WO-1616 RESULT sec.2 and WO-1617 RESULT sec.4 both did it this way).

## 8. Sequencing - READ BEFORE STARTING

[STOP] **`EquipmentController.cs` carries UNCOMMITTED work from two other lanes.** WO-1616 RESULT sec.4
records that its lane started *"from the WORKING TREE (which carries lane HERO-GRIP / WO-1431's
uncommitted `EquipmentController.cs` + `WeaponOrientHelper.cs` edits), never from HEAD."*

- **Start from the WORKING TREE, never from HEAD.**
- **SEQUENCED behind WO-1431's and WO-1616's gates.** Both are `IMPLEMENTED - awaiting gate`; neither
  has a `COMPILE_GATE_OK` or a `REGRESSION_OK <n>/<n>` on a fresh log, and WO-1616 additionally owes
  an owner raid felt-test. Landing a third unproven change into the same file makes all three
  unattributable when something moves.
- **Every line number in this ticket is a pointer read 2026-09-09** and will shift the moment the
  lanes above it land. Re-read at source (CLAUDE.md sec.11B).

## 9. Not in scope

- Do not touch `WeaponOrientHelper.cs` or `GearSeat.cs`.
- Do not touch `TroopGearApplier.cs` beyond confirming it still calls the authority.
- Do not re-add any hard-coded shield constant anywhere (WO-1616 deleted the last one; its file
  header and the deleted branch's slot both carry a "do not re-add a shield case here" note).
- Do not change the bow, melee, native or `fullOverride` preview branches.
- Do not migrate, rewrite or delete any saved seating offset (sec.4).
- Do not edit `DataRegression.cs` - lead-owned.
- Do not touch `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` from the lane; **flag** the row it needs (see
  below) and hand it back. WO-1616 RESULT sec.6 item 8 already flagged a different missing row in
  the same table - two flags on one doc is a signal for the lead, not a licence.
- Do not run Unity, gate, or commit. Edit-only; the lead holds the Unity lock and is the sole
  committer.

**Doc row to FLAG (not write):** the "what is wired live" table needs the preview's shield row
stating that it routes through `EquipmentController.SeatShieldMountRotation`, so the sentence *"the
Seating Editor preview shares the same method so the two can never disagree"* becomes true for the
shield as well as the bow.

## 10. Files

| file | role |
|---|---|
| `Assets/_Modules/Village/Hero/EquipmentController.cs` | **the only code file.** Preview `ApplySeatingPreview` `:5079`, the `shieldPreview` branch `:5182-5189`, the frame re-measure `:5141-5144`, the composition `:5191-5194`; authority `SeatShieldMountRotation` `:2190` (**call / add an entry point - do not fork**), `SeatShieldPlateOnSocket` `:2265` |
| `Assets/Editor/Regression/SeatingPreviewShieldRegression.cs` | NEW (sec.7) |
| `Assets/_Modules/Core/Geometry/WeaponOrientHelper.cs` (`ComputeShieldMountRotation :576`, `TryComputeShieldMountRotation :647`) | **READ-ONLY** - context |
| `Assets/_Modules/Core/Geometry/GearSeat.cs` (`GetShieldAxes :207`, `EnsureShieldOuterFaces :249`) | **READ-ONLY** - context |
| `Assets/_Modules/Village/UI/SeatingEditorOverlay.cs` | **READ-ONLY** - the UI shell, not the math |
| `Assets/Editor/Regression/DataRegression.cs` | **DO NOT EDIT** - hand back the registration line |

## 11. Acceptance criteria

- [ ] **The delta was MEASURED before the join** (sec.5 step 1) and the angle in degrees is in the
      RESULT. Not "it diverged" - a number.
- [ ] Exactly ONE code path derives a shield's mount rotation. State its **file:line** in the RESULT
      and name every caller.
- [ ] The preview calls it. No `GetShieldAxes` / `EnsureShieldOuterFaces` / `ComputeShieldMountRotation`
      call remains inside `ApplySeatingPreview`.
- [ ] The join shape (3a option a or b) is named, with the reason it was chosen.
- [ ] Frame ownership stated (3b): who measures, who consumes, and that WO-1123's reasoning survives.
- [ ] `mayDerive` / owner-precedence behaviour stated (3c) and pinned by a case.
- [ ] RED-first: `CaseOneOwner` reds at HEAD; the identity-fixture CONTROL case exists and would fail
      if the fixture were too clean to discriminate; the mutation is named.
- [ ] `[attachment-offset]`, `[sheathe-pose]`, `[staff-grip-seat]` and `[troop-shield-seat]` all stay
      green on a fresh log. **This ticket must not become a hero visual change.**
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` + `SEATING_PREVIEW_SHIELD_OK` on **fresh**
      logs, judged by the marker and the `^REGRESSION_(OK|FAIL)` summary line - never the exit code
      (CLAUDE.md sec.8; memory `gates-report-success-without-proving-it`). Unity logs are UTF-16:
      read with PowerShell `Select-String`.
- [ ] Brace balance + NUL scan on every `.cs` touched (CLAUDE.md sec.1, WO-434).
- [ ] The behaviour change of sec.4 is stated to the owner in plain words, with the saved-offset
      consequence.
- [ ] Everything unproven is listed as unproven (CLAUDE.md sec.11B).
- [ ] **Owner opens the Seating Editor on a shield, confirms the preview matches the game, and
      CLOSES.** This is a WYSIWYG ticket - only her eye can close it. CLI never closes it on a log
      line.
