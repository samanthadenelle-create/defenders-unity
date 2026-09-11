# WO-2010 — Replace Flat Research Rail With School-First Research

**Status:** FIXED - awaiting owner test-build proof (2026-09-10 status ruling). Current captured Research grid shows five schools in one row; see docs/READY_CLEARANCE_2026-09-10.md. PRIOR STATUS: IN PROGRESS - PARTIAL: a research-school surface exists (ManageViewContract.cs, ManageScreenVM.cs); the school-first layout acceptance (no 17-perk flat list, all schools visible without scrolling) is not verified at HEAD 2026-09-06.

**Priority:** P0  
**Depends on:** WO-2002

## Objective

Replace the current 17-row flat research list with a two-level structure.

## Level 1 — Research schools

Render schools/providers as a small grid.

Examples:
- Cathedral of Magic — Magic
- Armorer — Defense
- Forge / Weaponsmith — Weapons
- Barracks — Army

The authoritative school list comes from the model.

## Level 2 — Perks for selected school

Render a compact local list.

Each row shows:
- perk name
- state
- cost/duration where relevant
- action or requirement

States include:
- researched
- available
- unaffordable
- queue blocked
- locked by school/building/Heart
- in progress

## Direct navigation

If a perk requires Armorer Level 3:
[ VIEW ARMORER ]

If it requires Heart Level 4:
[ VIEW HEART ]

Commands come from model.

## Acceptance criteria

- no 17-perk flat list
- all schools visible without scrolling at target aspect
- local school list does not require eight-screen browsing
- researched rows are visually quiet
- actionable row is obvious without opening a secondary detail


---

## Provenance and reconciliation (added by the CLI seat, 2026-09-06)

Authored outside this repo and delivered by the owner as `Elarion_Manage_Redesign_Detailed_WorkOrders.zip`
on 2026-09-06. Filed verbatim; **the body above is the author's and has not been edited.** Everything the CLI
seat adds appears under a heading like this one.

**Numbering:** this set uses a **2000-block**, a THIRD namespace alongside the CLI main line and the UI seat's
reserved block. It is declared on `CLI_LANES_WO_NUMBERS.md` so it cannot collide (CLAUDE.md section 2 - the banner is the
sole authority). Do not renumber these into the main line.

**Supersedes:** `WORK_ORDER_1427` (why-can't-I) and `WORK_ORDER_1428` (the Manage card grows to the mockup).
Both were minted earlier the same day from the owner's playtest and her mockup; this program subsumes them and
goes further by replacing the rail model rather than enriching the card.

**Measured facts this set is consistent with** (from `docs/manage-flow-map/MAP.md`, run `Builds/flowmap1`):
43 rail rows across four areas, about two visible at a time; Buildings 6 + Defense 11 = 17, which is the number
the canon cites; the scroll auditor reporting `geometry=5 touch=5` on deliberately scrolled frames, which WO-2016
is right to call a fix rather than a waiver.
