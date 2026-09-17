# WO-2009 — Simplify Troop Detail, Training, and Upgrade Actions

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:19:01, build 2026.09.17.373943). PRIOR STATUS: FIXED - awaiting owner test-build proof (2026-09-10 status ruling). Current troop detail supports training at max tier and one action command; see docs/READY_CLEARANCE_2026-09-10.md for source/capture evidence. PRIOR STATUS: IN PROGRESS - PARTIAL: the troop progression seam landed with the barracks merge in a6bbc523d (BarracksProgression.cs); the troop-detail train and upgrade acceptance is not verified at HEAD 2026-09-06.

**Priority:** P0  
**Depends on:** WO-2008, WO-2011

## Objective

Keep training and upgrade logic distinct while correctly handling max-level, queue-blocked, and locked states.

## Core rule

MAX applies to the **upgrade track**, not necessarily to troop use.

A Level 7 MAX Footman can still be TRAINABLE.

## Available troop

FOOTMAN — LEVEL 3

Front-line melee fighter.

Army: 4 / 10

550 Gold · 45 sec

[ TRAIN ]

Upgrade:
Health 120 → 135
Damage 28 → 32
800 Wood · 350 Iron

[ UPGRADE ]

## Max troop

FOOTMAN — LEVEL 7 · MAX

Army: 4 / 10

550 Gold · 45 sec

[ TRAIN ]

No upgrade button.

## Queue blocked

FOOTMAN — LEVEL 7 · MAX

Training line full · 5/5 queued

[ VIEW QUEUE ]

Do not render TRAIN as enabled.

## Locked troop

OUTRIDER

Requires Barracks Tier 4.

[ VIEW BARRACKS ]

If Heart level also gates it, the model chooses the blocker and navigation destination.

## One unit per tap

TRAIN queues one troop per activation unless gameplay design is explicitly changed elsewhere.

## View restrictions

The panel may not:
- check gold
- check training queue depth
- check Barracks tier
- check Heart level
- decide max level
- call BarracksService directly

## Acceptance criteria

- training a max-level troop works
- queue-blocked state never shows a false active TRAIN action
- model owns all refusal reasons
- one command path performs training


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
