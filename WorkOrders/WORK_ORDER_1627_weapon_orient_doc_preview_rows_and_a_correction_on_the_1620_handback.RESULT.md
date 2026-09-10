# WO-1627 RESULT - WEAPON_ARMOR_ORIENT_LOGIC preview rows written; WO-1620 hand-back corrected

**Lane:** ORIENT-DOC (docs / canon) - **2026-09-10**
**Ticket status:** IMPLEMENTED - awaiting lead review
**Files changed: 4 markdown (3 tracked + 1 untracked). ZERO `.cs`. No Unity run, no gate, no commit.**

`git diff --numstat` for the three tracked paths, run 2026-09-10:

```
6	0	WorkOrders/WORK_ORDER_1616_raid_npc_shield_seats_at_a_hard_coded_offset.RESULT.md
10	1	WorkOrders/WORK_ORDER_1627_weapon_orient_doc_preview_rows_and_a_correction_on_the_1620_handback.md
19	2	docs/WEAPON_ARMOR_ORIENT_LOGIC.md
```

- `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` (+19 / -2; `wc -l` 249 -> 266, all growth inside the table block)
- `WorkOrders/WORK_ORDER_1616_raid_npc_shield_seats_at_a_hard_coded_offset.RESULT.md` (+6 / -0, item 8 closing line only)
- `WorkOrders/WORK_ORDER_1620_seating_editor_shield_preview_must_route_through_the_shared_authority.RESULT.md` (+20 / -0, two CORRECTION banners, body untouched - **UNTRACKED file, see sec.4**)
- `WorkOrders/WORK_ORDER_1627_...handback.md` (+10 / -1, Status flip + sequencing note)

---

## 1. Every line re-opened at source THIS session

All in `Assets/_Modules/Village/Hero/EquipmentController.cs` unless named otherwise, read from the
WORKING TREE (which carries the WO-1620 lane's uncommitted edits), not from HEAD. Read-only.

| line(s) opened | what it says |
|---|---|
| `:915-921` | `ReseatForBody`: `:918` `var anim = body.GetComponentInChildren<Animator>();` (a LOCAL), humanoid guard `:919`, `_animator = anim;` at `:920` |
| `:1440` | `Transform bowBody = _animator != null ? _animator.transform : transform;` (bow ATTACH) |
| `:1445` | `WeaponBoundsOrient.ComputeBowHeldRotation(hand, bowBody)` |
| `:1608-1612` | `TraceBowSeatMeasured`; `:1611` = the same ternary again |
| `:2190` | `public static ShieldSeat SeatShieldMountRotation(...)` declaration |
| `:2240` | `if (!TryDeriveShieldMountRotation(frame, hand, animator, body, subject, ...` - the attach path calling the shared deriver |
| `:2295-2298` | `public static bool TryDeriveShieldMountRotation(ShieldFrame frame, Transform hand, Animator animator, Transform body, string subject, out Quaternion mountLocal)` |
| `:2918-2919` | hero shield attach: `SeatShieldMountRotation(prop, gripRoot.transform, hand, _animator, transform, shieldMayDerive, id)` - body = bare `transform` |
| `:2329`, `:2990` | `SeatShieldPlateOnSocket` declaration and the hero snap call |
| `:5230-5238` | bow PREVIEW branch; `:5234` = the identical ternary, `:5236` = `ComputeBowHeldRotation(grt.parent, previewBody)` |
| `:5258-5267` | the in-code out-of-scope comment; its closing clause about the bow branch |
| `:5268-5280` | shield PREVIEW branch; `:5275-5276` = `TryDeriveShieldMountRotation(_previewShieldFrame, grt.parent, _animator, transform, ...)` |
| `:5272-5274` | the comment stating the preview supplies its OWN re-measured frame |
| `:5386-5393` | sheathed preview; `:5389` the call, `:5390` the same ternary |
| `:5673-5674` | `_animator = body != null ? body.GetComponentInChildren<Animator>() : null;` + the fallback line |
| `TroopGearApplier.cs:236-244` | `SeatShieldOnHand` -> `SeatShieldMountRotation` (`:240-241`) and `SeatShieldPlateOnSocket` (`:244`) |
| `TroopGearApplier.cs:274-280` | the "NO SHIELD BRANCH LIVES HERE ANY MORE" comment recording the deleted triple |
| `TroopGearApplier.cs:333-352` | `BuildPrimitiveFallback` - the ONLY shield-shaped literals left in that file (scale/colour, `:348-352`) |
| `docs/WEAPON_ARMOR_ORIENT_LOGIC.md:164-173` | the four-row live table, before the edit |
| `WORK_ORDER_1616_*.RESULT.md:165-185` | item 8 (`:172-176`) and item 9's CORRECTION (`:177-`) |
| `WORK_ORDER_1620_*.RESULT.md:95-115`, `:165-210` | the `:103-105` generalisation and the "New finding" block at `:200-203` (pre-edit numbering; the banners this lane inserted push it to `:208-211`) |
| `tools/board_build.py:248-249` | the Status regexes, checked before the flip |

**Not opened, therefore not claimed about:** `WeaponBoundsOrient.ComputeBowHeldRotation`'s body,
`GearSeat.cs`, `WeaponOrientHelper.cs`. The doc cites them only through their call sites, exactly as
WO sec.2 requires.

## 2. Line-number check on the source documents (CLAUDE.md sec.11B - every cited number re-read)

**One real discrepancy, and two near-misses this lane initially got wrong itself. All three are
written down because the self-inflicted ones are the same failure mode the ticket is about.**

1. **REAL: `WORK_ORDER_1616_*.RESULT.md` item 9's CORRECTION starts at `:177`, not `:178`** as
   WO-1627 sec.1c says. Item 8 is `:172-176`. The shape (item 8, then item 9's CORRECTION) is exactly
   as described, so nothing downstream changes.
2. **NOT a discrepancy - `:920` is correct and this lane was briefly wrong.** A grep for
   `GetComponentInChildren<Animator>` hits `:918`, but that line assigns a LOCAL `var anim`; `:920` is
   `_animator = anim;` after the humanoid guard at `:919`. WO-1627 and `WORK_ORDER_1620_*.RESULT.md:99`
   were right. The doc now cites both (`:918` resolve, `:920` assign).
3. **NOT a discrepancy - the "New finding" block IS at `:200-203`** as the ticket says. It reads
   `:208-211` only AFTER this lane inserted its first banner seven lines above it. Measuring a file
   post-edit and calling the ticket stale would have been an inference from the wrong measurement.

## 3. The finished table, verbatim (`docs/WEAPON_ARMOR_ORIENT_LOGIC.md:164-188`)

```
## What is wired live, and what is measurement only (WO-1123, 2026-08-19)

| path | state |
|---|---|
| **shield, drawn** | **DERIVED** (both native and normalized props), global weapon yaw withheld. The Seating Editor preview shares the same method: both sides call `EquipmentController.TryDeriveShieldMountRotation` (declared `EquipmentController.cs:2295`) - the attach path through `SeatShieldMountRotation` (declared `:2190`, which calls the deriver at `:2240`; called from the hero attach at `:2918-2919`), the preview directly at `:5275-5276` - and both pass the SAME body, bare `transform` (`:2919` attach, `:5276` preview). The preview supplies its own re-measured `_previewShieldFrame` (`:5276`, reasoning `:5272-5274`), so what is shared is the METHOD, not the frame. (added 2026-09-10, WO-1620 / WO-1627) |
| **shield, sheathed** | **DERIVED** off the back socket with outward = -body.forward; the Seating Editor preview shares the same method so the two can never disagree |
| **shield, raid NPC** | **DERIVED** through the same authority as the hero (`EquipmentController.SeatShieldMountRotation` / `SeatShieldPlateOnSocket`), called from `Assets/_Modules/Village/Troops/TroopGearApplier.cs:240-241` (and `:244` for the plate); `TroopGearApplier` holds no shield ROTATION constant - its deleted `else if (shield)` triple is called out at `TroopGearApplier.cs:274-280`, and the only shield-shaped literals left in that file are the scale/colour of the missing-prefab primitive fallback at `:348-352`. (added 2026-09-10, WO-1616 wording, written by WO-1627) |
| **bow, drawn + sheathed** | unchanged - felt-verified. Preview and attach pass the **same** body expression, `_animator != null ? _animator.transform : transform`, at `EquipmentController.cs:1440` (attach), `:5234` (drawn preview) and `:5390` (sheathed preview), and all three hand it to `WeaponBoundsOrient.ComputeBowHeldRotation` (`:1445`, `:5236`, `:5389-5390`); `TraceBowSeatMeasured` uses the same expression at `:1611`. **There is no bow preview-vs-attach divergence.** (confirmed 2026-09-10, WO-1627) |
| **staff, drawn + sheathed** | grip point **DERIVED** (0.75 up the long axis) via `EquipmentController.SeatMeleeGripPoint`, precedence-gated; sword/dagger and every Unknown family keep the hilt-lower-half seat and the read-only prediction. (updated 2026-09-09, WO-1431 lane HERO-GRIP) |

> ### Open convention question - NOT a defect, NOT investigated, do NOT "align" it blind
> The two seams above standardise on **different** expressions for "the body transform", and each is
> internally consistent with itself. The **shield** seam passes bare `transform` on both sides
> (`EquipmentController.cs:2919` attach, `:5276` preview). The **bow** seam passes
> `_animator != null ? _animator.transform : transform` on all of its sides (`:1440`, `:5234`,
> `:5390`, `:1611`). `_animator` is resolved with `GetComponentInChildren<Animator>()`
> (`:5673-5674`; also resolved at `:918` and assigned to `_animator` at `:920`), so on a rig whose
> Animator sits on a CHILD of the EquipmentController's GameObject the two expressions yield
> different transforms with different `right` / `up` / `forward`.
>
> **Nothing here claims any rig in this project is shaped that way, or that any bow's rendered pose
> differs.** Neither was investigated. Closing this question is an instrumented read first
> (CLAUDE.md sec.12), and the in-code comment at `EquipmentController.cs:5260-5267` already rules the
> bow branch out of scope for the shield lane - that comment is accurate and stays. (recorded
> 2026-09-10, WO-1627)
```

**Two characters above are transcribed, not literal:** the live file keeps the pre-existing `-`
(U+2212) in the sheathed row's `-body.forward` and the pre-existing em dash in the bow row's
`unchanged - felt-verified`, because WO sec.6 pins those rows' existing wording. Every character this
lane ADDED is ASCII; the only non-ASCII on a touched line is inherited text the ticket forbade
rewriting. Acceptance 5 is met in that sense and the exception is named rather than glossed.

## 4. Two acceptance criteria could not be met as literally written - scoped, not skipped

**Acceptance 3 ("`git diff --stat` shows exactly three files") assumed a clean tree. It is not clean.**
The shared working tree carries eight modified `.cs` and other lanes' markdown. The path-scoped stat
for this lane, run 2026-09-10:

```
 ...pc_shield_seats_at_a_hard_coded_offset.RESULT.md |  6 ++++++
 ...ew_rows_and_a_correction_on_the_1620_handback.md | 11 ++++++++++-
 docs/WEAPON_ARMOR_ORIENT_LOGIC.md                   | 21 +++++++++++++++++++--
 3 files changed, 35 insertions(+), 3 deletions(-)
```

Zero `.cs` in this lane's paths. `git status --short` shows the fourth file, the WO-1620 RESULT, still
`??` (untracked) - it is this lane's edit target and produces no git hunk.

**Acceptance 4 ("quote the diff hunks") is impossible via git for an untracked file.** The file was
copied to the scratchpad BEFORE any edit and proved afterwards with `diff -u snapshot live`. The
result is two pure insertion hunks (`@@ -104,6 +104,14 @@` and `@@ -202,6 +210,18 @@`), zero deleted
lines, zero modified lines - i.e. the body is byte-identical and only banners were added:

```
@@ -104,6 +104,14 @@
 same divergence is **not investigated and not claimed**; it is flagged in sec.7 below.

+> **CORRECTION 2026-09-10 (WO-1627, re-measured at source) - full banner below the "New finding"
+> paragraph in sec.7.** ... (7 lines)

@@ -202,6 +210,18 @@
 investigated and is not claimed**. It is the same shape as divergence 4 and deserves one read.

+> **CORRECTION 2026-09-10 (WO-1627, re-measured at source): the clause "while this lane proved attach
+> passes `transform`" does not hold for the BOW.** ... (11 lines)
```

The `WORK_ORDER_1616_*.RESULT.md` hunk is likewise a single pure insertion inside item 8, six lines,
with item 9's own CORRECTION untouched.

**Sequencing deviation, named (CLAUDE.md sec.11B B):** this ticket's own Status line sequenced it
behind the lead's commit of the WO-1620 RESULT. The lead's dispatch overrode that explicitly. The
override is recorded in the WO file's header as well as here.

## 5. Pins verified untouched

- **No `.cs` file was edited.** `git diff --stat` for this lane's paths lists three `.md` and nothing
  else; `EquipmentController.cs`, `TroopGearApplier.cs`, `GearSeat.cs`, `WeaponOrientHelper.cs`,
  `WeaponBoundsOrient.cs` and every regression were opened READ-ONLY. Nothing under `Assets/` changed.
- The bow branch `:5230-5238` and the comment `:5260-5267` are quoted, never edited.
- `SeatingPreviewShieldRegression` was not opened and no oracle was added.
- The four existing table rows keep their verdicts; two were EXTENDED, none rewritten, one row added.
- `WORK_ORDER_1616_*.RESULT.md` item 9's CORRECTION and both RESULT bodies are unchanged.
- Only `docs/WEAPON_ARMOR_ORIENT_LOGIC.md:164-188` moved; the file's other sections are untouched
  (`wc -l` 249 -> 266, all growth inside the table block).
- No Unity run, no gate, no commit. `docs/MASTER_CATALOG.md` and every other canon file untouched.

## 6. What this lane could NOT prove

- **That any rig in this project has its Animator on a child of the EquipmentController's GameObject.**
  `GetComponentInChildren` makes it possible; nothing read makes it actual. The doc says so explicitly.
- **That the cross-seam convention difference changes any rendered bow pose.** Not investigated, not
  claimed, and the doc forbids a blind edit rather than proposing one.
- **What `ComputeBowHeldRotation`, `GetShieldAxes` or `EnsureShieldOuterFaces` DO internally.** Not
  opened; only their call sites are cited.
- **That the doc renders correctly in the owner's viewer.** Markdown table syntax is well-formed (five
  data rows, two columns, pipes balanced) but nothing rendered it this session.

## 7. Board

- WO Status line flipped in `WorkOrders/WORK_ORDER_1627_weapon_orient_doc_preview_rows_and_a_correction_on_the_1620_handback.md`
  to `IMPLEMENTED - awaiting lead review (lane ORIENT-DOC 2026-09-10)`; verified it is the first and
  only `**Status:**` line and matches `tools/board_build.py:248`'s `_STATUS_EXACT` regex.
- This RESULT:
  `WorkOrders/WORK_ORDER_1627_weapon_orient_doc_preview_rows_and_a_correction_on_the_1620_handback.RESULT.md`
- The lead regenerates `BOARD.html` and commits. **The WO-1620 RESULT must be committed WITH this
  lane's banner, not before it** - the banner lives in the same untracked file.
