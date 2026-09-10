# WO-2007 — Simplify Building Detail and Upgrade Action

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane MANAGE-DOOR); owner device match closes

**Priority:** P0  
**Depends on:** WO-2006, WO-2003

## Objective

Make the selected BUILD detail answer only the player's immediate decision.

## Standard selected building contract

Model supplies:

- title
- current level text
- short description
- current/next stats
- costs
- duration
- upgrade-track state
- action state
- primary action
- prerequisite action if blocked
- progress if active

## Available example

LUMBER MILL — LEVEL 2

Produces wood over time.

Production 120/hr → 180/hr  
Storage 2,000 → 3,000

1,200 Wood · 600 Stone · 45m

[ UPGRADE ]

## Heart-gated example

Important: the building is not "Locked" if it is already built and functioning.

LUMBER MILL — LEVEL 2

Produces wood over time.

Next upgrade requires Heart Level 5.

[ VIEW HEART ]

The VM should express:
- item availability = owned/operational
- upgrade action = gated

## Queue-blocked example

LUMBER MILL — LEVEL 2

Upgrade ready.

Builder queue full · 5/5 queued

[ VIEW QUEUE ]

Do not show an enabled UPGRADE button.

## In-progress example

LUMBER MILL — LEVEL 2 → 3

18m remaining

[ VIEW QUEUE ]

## Max example

FORGE — LEVEL 4 · MAX

No upgrade action.

Other building interactions, if any, remain available.

## Acceptance criteria

- no duplicate state copy
- no giant disabled "MAX LEVEL" action
- no false lock on owned buildings
- no enabled upgrade CTA while queue-blocked
- prerequisite CTA is supplied by model


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
