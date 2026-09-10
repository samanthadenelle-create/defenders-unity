# WO-1627 - WEAPON_ARMOR_ORIENT_LOGIC needs its preview rows, and the WO-1620 hand-back needs a CORRECTION banner

**Status:** FIXED 2026-09-10 - lead reviewed: five cited rows in docs/WEAPON_ARMOR_ORIENT_LOGIC.md, CORRECTION banners on the 1620 RESULT (pure insertions) and 1616 item 8 closed; docs only. (was: IMPLEMENTED - awaiting lead review, lane ORIENT-DOC)

**Sequencing note (2026-09-10):** the READY status below said *"SEQUENCED behind the lead's commit of
the WO-1620 RESULT"*. The lead's dispatch to this lane OVERRODE that guard explicitly, instructing the
banner be appended to the still-uncommitted `WORK_ORDER_1620_*.RESULT.md` in the main tree, with
nothing else in that file rewritten. Named here rather than hidden (CLAUDE.md sec.11B B). A snapshot
of the file was taken before the edit and the resulting `diff -u` is quoted in the RESULT, since an
untracked file produces no git hunk.

**Original status line, kept for the record:** READY TO IMPLEMENT - **SEQUENCED behind the lead's commit of the WO-1620 RESULT**
(measured 2026-09-10: `git status` reports
`?? WorkOrders/WORK_ORDER_1620_seating_editor_shield_preview_must_route_through_the_shared_authority.RESULT.md`
- UNTRACKED, i.e. that lane's hand-back is still uncommitted in the shared tree. Appending a banner to
it before the lead commits it is the CLAUDE.md sec.11 multi-session collision; the sibling
`WORK_ORDER_1623_*.RESULT.md` carries its own *"RECONCILE, DO NOT BLIND-REPLACE"* warning for the same
reason. `WORK_ORDER_1616_*.RESULT.md` is tracked, so only the 1620 file needs the guard.)
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; bumped 1625 -> 1628 in the SAME edit)
**Silo / Lane:** Docs / canon (`docs/WEAPON_ARMOR_ORIENT_LOGIC.md` + two frozen RESULT files) -
**NO .cs, no Unity, no gate, no commit**
**Severity:** P2 canon. Two lanes flagged the same table in two days, and one of them handed back a
statement that is **false at source** - which is the CLAUDE.md sec.15 duplicated-state failure caught
before it cost anyone a morning.
**Type:** EXISTING system, documentation only. **NOTHING IN THE GAME CHANGES.**
**Owner words:** none - lane finding.

---

## 0. READ THIS FIRST - THE BRIEF'S PREMISE WAS DISPROVEN AND THIS TICKET IS RE-SCOPED

This slot was briefed as *"the Seating Editor BOW preview passes `_animator.transform` where the
attach path passes `transform`; make the bow preview pass the same body the attach path passes."*

**That is not what the tree says.** Measured 2026-09-10 in
`Assets/_Modules/Village/Hero/EquipmentController.cs`:

| site | expression |
|---|---|
| **bow ATTACH**, `:1440` | `Transform bowBody = _animator != null ? _animator.transform : transform;` |
| **bow PREVIEW**, `:5234` | `Transform previewBody = _animator != null ? _animator.transform : transform;` |

**Character-for-character the same ternary.** The bow preview already passes exactly what the bow
attach passes. There is no preview-vs-attach divergence on the bow, and no code fix to make. Both
then call `WeaponBoundsOrient.ComputeBowHeldRotation` with it (`:1445`, `:5236`). The bow's SHEATHED
preview uses the same ternary inline at `:5390`, and `TraceBowSeatMeasured` at `:1611` uses it too -
so the bow seam is internally consistent in four places.

**Where the false premise came from,** and it is worth naming because the shape repeats: the WO-1620
lane proved that the **SHIELD** attach passes bare `transform` (`:2918-2919`,
`SeatShieldMountRotation(prop, gripRoot.transform, hand, _animator, transform, shieldMayDerive, id)`)
and correctly aligned the shield PREVIEW to it (`:5276`). Its hand-back then generalised "attach
passes `transform`" from the shield seam to the bow seam, which is a different seam with a different
convention.

**What is actually true, and is NOT a defect:** the repo carries **two conventions for "the body
transform"** - the shield seam uses bare `transform` on both sides, the bow seam uses the
`_animator ?? transform` ternary on both sides. `_animator` is resolved with
`GetComponentInChildren<Animator>()` (`:5673-5674`, also assigned `:920`), so on a rig whose Animator
sits on a child the two differ. **Whether that difference changes any bow's rendered pose is NOT
INVESTIGATED and IS NOT CLAIMED here.** It is an open convention question that deserves one
instrumented read (CLAUDE.md sec.12) before anyone touches it - not a ticket to change a line.

**So this ticket does the two things that ARE proven:** write the missing doc rows, and correct the
frozen artifact that says otherwise. **Do not "fix" the bow branch.** The in-code comment at
`:5266-5267` already says it is explicitly out of scope, and it is right.

---

## 1. What was measured (read at source 2026-09-10)

### 1a. The doc table is short two rows

`docs/WEAPON_ARMOR_ORIENT_LOGIC.md:164-171`, the section headed
*"What is wired live, and what is measurement only (WO-1123, 2026-08-19)"*, is a four-row table:

| line | row |
|---|---|
| `:168` | **shield, drawn** - DERIVED (both native and normalized props), global weapon yaw withheld |
| `:169` | **shield, sheathed** - DERIVED off the back socket [...] *"the Seating Editor preview shares the same method so the two can never disagree"* |
| `:170` | **bow, drawn + sheathed** - unchanged - felt-verified |
| `:171` | **staff, drawn + sheathed** - grip point DERIVED [...] (updated 2026-09-09, WO-1431) |

The sentence at `:169` - *"the Seating Editor preview shares the same method so the two can never
disagree"* - appears on the **sheathed shield** row only. The **drawn shield** row (`:168`) has no
such clause, and until WO-1620 landed it would have been untrue there.

### 1b. Two lanes flagged this same table, two days apart

- `WorkOrders/WORK_ORDER_1616_*.RESULT.md:172-177`, item 8: *"`docs/WEAPON_ARMOR_ORIENT_LOGIC.md`
  should gain a row (flagged, NOT edited - outside this lane's file list). Its 'what is wired live'
  table describes the shield rows for the hero only."* It supplies suggested wording for a **raid NPC**
  shield row.
- `WorkOrders/WORK_ORDER_1620_*.RESULT.md:193-198`: *"Doc row to WRITE (flagged, not written - WO
  sec.9) [...] needs the preview's shield row stating that it routes through
  `EquipmentController.TryDeriveShieldMountRotation`, so 'the Seating Editor preview shares the same
  method so the two can never disagree' becomes true for the shield as well as the bow. **This is the
  second flag on that doc** [...] Two flags on one table is a signal, not a licence."*

Both lanes were right to decline (each was out of its file list). Neither could mint. This is the
ticket that closes both.

### 1c. The frozen artifact that carries the false statement

`WorkOrders/WORK_ORDER_1620_seating_editor_shield_preview_must_route_through_the_shared_authority.RESULT.md:200-203`:

> **New finding for the lead to triage (no ticket minted - the numbering banner is lead-owned):** the
> **bow** preview branch (`:5230-5238`) passes `_animator.transform` as its body while this lane proved
> attach passes `transform`. Whether `ComputeBowHeldRotation` is sensitive to that difference was **not
> investigated and is not claimed**. It is the same shape as divergence 4 and deserves one read.

The lane's own caution ("not investigated and is not claimed") is exemplary. The clause that is wrong
is *"while this lane proved attach passes `transform`"* - it proved that of the **shield** attach.
`:103-105` of the same RESULT carries the same generalisation.

**This is precisely the shape CLAUDE.md sec.15 covers:** a dated RESULT is FROZEN and is never
rewritten; a wrong statement inside one gets a `CORRECTION` banner from its owner. There is already a
worked precedent in this exact family - `WORK_ORDER_1616_*.RESULT.md:178`, item 9, opens
**"CORRECTION 2026-09-09 (lead, re-measured at source): THIS ITEM IS WRONG - NOT MINTED."** and then
states what the file actually says. Copy that shape.

## 2. What is NOT claimed

- **NOT claimed: that the bow's body-transform convention is wrong, right, or worth changing.** Not
  investigated. Sec.0.
- **NOT claimed: that any rig in this project has its Animator on a child of the
  EquipmentController's GameObject.** The `GetComponentInChildren` call at `:5673-5674` makes it
  POSSIBLE; nothing read this session makes it ACTUAL. Do not write the word "would" into the doc
  about a rig nobody has looked at.
- **NOT claimed: that the missing doc rows caused a defect.** They are a canon gap flagged twice, not
  a bug.
- **NOT read this session:** `WeaponBoundsOrient.ComputeBowHeldRotation`'s body, `GearSeat.cs`, and
  `WeaponOrientHelper.cs`. Cited only via the call sites and the comments that name them. If a doc row
  needs a claim about what those methods DO, open them first (CLAUDE.md sec.11B - a fact copied from a
  comment is hearsay).

## 3. Target - what "fixed" means

`docs/WEAPON_ARMOR_ORIENT_LOGIC.md`'s live table answers "does the Seating Editor preview agree with
what the game attaches?" for **every** row, in one place, sourced from the call sites. And the two
frozen RESULT files no longer send the next seat after a divergence that is not there.

## 4. The fix - three edits, all markdown

### 4a. `docs/WEAPON_ARMOR_ORIENT_LOGIC.md:164-171` - complete the table

Keep the existing four rows' wording; extend or add rows so the table states, each with its file:line:

1. **shield, drawn** (`:168`) - gains the preview clause: the Seating Editor preview routes through
   `EquipmentController.TryDeriveShieldMountRotation` (declared `:2295`), the same compute the attach
   path's `SeatShieldMountRotation` (`:2190`, called `:2918`) runs, so the preview and the game cannot
   disagree. WO-1620, 2026-09-10.
2. **shield, raid NPC** - a new row, wording supplied verbatim by `WORK_ORDER_1616_*.RESULT.md:174-177`:
   derived through the same authority as the hero (`SeatShieldMountRotation` / `SeatShieldPlateOnSocket`);
   `TroopGearApplier` holds no shield constant. The second caller is
   `Assets/_Modules/Village/Troops/TroopGearApplier.cs:240`. WO-1616, 2026-09-09.
3. **bow, drawn + sheathed** (`:170`) - keep *"unchanged - felt-verified"* and add the fact the two
   lanes went looking for: preview and attach pass the **same** body expression
   (`_animator != null ? _animator.transform : transform`) at `:1440` (attach) and `:5234` (drawn
   preview), and `:5390` (sheathed preview) - so this seam already agrees with itself.
4. **A short note under the table** recording the open convention question and forbidding a blind
   edit: the shield seam standardises on bare `transform` (`:2919`, `:5276`); the bow seam
   standardises on the ternary; `_animator` comes from `GetComponentInChildren` (`:5673-5674`).
   **Not a defect, not investigated - instrument before touching (CLAUDE.md sec.12).**

Every row states its file:line and its WO number, matching the `:171` staff row's existing
*"(updated 2026-09-09, WO-1431 lane HERO-GRIP)"* convention. **Do not restate a rotation formula in
this doc** - point at the method that owns it. A formula copied into a doc is the state that rots
(CLAUDE.md sec.5, sec.8).

### 4b. `WORK_ORDER_1620_*.RESULT.md` - add a CORRECTION banner, do NOT rewrite the body

Add a banner at `:200` (the "New finding" block) and a pointer at `:103-105`, in the
`WORK_ORDER_1616_*.RESULT.md:178` shape:

> **CORRECTION 2026-09-10 (WO-1627, re-measured at source): the clause "while this lane proved attach
> passes `transform`" does not hold for the BOW.** That was proven of the SHIELD attach
> (`EquipmentController.cs:2918-2919`). The bow's own attach passes
> `_animator != null ? _animator.transform : transform` (`:1440`) - the identical expression the bow
> preview passes (`:5234`). **There is no bow preview-vs-attach divergence. Do not open a ticket to
> fix one.** What remains is a cross-seam convention difference, not investigated and not claimed.

Everything else in that file stays byte-identical. The lane's work was correct; one generalisation
was not.

### 4c. `WORK_ORDER_1616_*.RESULT.md:172-177` - close item 8

Append one line noting the row was written by WO-1627 on 2026-09-10, so the third lane to read this
file does not flag it a third time. Do not alter item 8's body, and do not touch item 9's existing
CORRECTION.

## 5. Acceptance

1. `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` renders as a valid markdown table with every row carrying a
   file:line and a WO number. Paste the finished table verbatim into the RESULT.
2. **Every file:line written into the doc is re-opened and confirmed by the implementing lane before
   it is written** - not copied from this ticket (CLAUDE.md sec.11B: a line number copied from a doc
   is hearsay until re-read). List in the RESULT which ones you opened.
3. `git diff --stat` shows **exactly three files**, all `.md`, and **zero** `.cs`.
4. The two RESULT bodies are unchanged apart from the appended banner / line - prove it by quoting the
   diff hunks.
5. ASCII only in every line added.

## 6. Pins - what must not move

- **NO `.cs` FILE IS EDITED IN THIS LANE.** Not `EquipmentController.cs`, not `TroopGearApplier.cs`,
  not `GearSeat.cs`, not `WeaponOrientHelper.cs`, not `WeaponBoundsOrient.cs`, not a regression.
- **The bow branch at `:5230-5238` and the in-code comment at `:5260-5267`.** The comment's closing
  clause - *"The bow branch above keeps `_animator.transform`; it is a different derivation with its
  own history and is explicitly out of scope - do not 'align' it here without a ticket"* - is
  ACCURATE and stays. **This ticket is not that ticket, and does not license that edit.**
- **`SeatingPreviewShieldRegression`** (suite markers `SEATING_PREVIEW_SHIELD_OK` /
  `SEATING_PREVIEW_SHIELD_FAIL`, entry `public static bool Run(out string reason)`). A docs lane adds
  no oracle. If a future ticket rules on the bow convention, its pin belongs beside that suite -
  **noted here for that future ticket, not to be built now.**
- **The four existing table rows' meaning** at `:168-171`. Extend and add; do not rewrite a row's
  verdict.
- **`WORK_ORDER_1616_*.RESULT.md` item 9's CORRECTION** and every other frozen RESULT body.
- **The doc's other sections.** The file is 249 lines; only the live table at `:164-171` and one note
  beneath it are in scope.

## 7. What NOT to touch

- Do **not** mint or imply a follow-up ticket to change the bow's body transform. If someone wants
  that question closed, the next step is an instrumented read (CLAUDE.md sec.12), and it needs the
  owner's word first because it is a felt-orientation surface she has already dialled against.
- Do **not** run Unity, fire a gate, or commit. Hand the diff back (CLAUDE.md sec.11).
- Do **not** touch `docs/MASTER_CATALOG.md` or any other canon file in this lane.

## 8. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1627_weapon_orient_doc_preview_rows_and_a_correction_on_the_1620_handback.RESULT.md`
is written, with all paths reported. The lead regenerates `BOARD.html`.
