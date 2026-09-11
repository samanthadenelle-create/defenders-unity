# WO-2012 — Replace Per-Destination Queue UI With One Global Queue

**Status:** FIXED - awaiting owner test-build proof (2026-09-10 status ruling). Shared three-channel queue is built/captured; the older activity-strip requirement is superseded by the newer no-activity-strip mockup ruling. See docs/READY_CLEARANCE_2026-09-10.md. PRIOR STATUS: IN PROGRESS - PARTIAL: a Manage queue drawer exists (ManageScreenPanel.cs, ManageQueueDrawerRegression.cs); one global queue used from all three tabs with no per-destination drawer left is not verified at HEAD 2026-09-06.

**Priority:** P0  
**Depends on:** WO-2002  
**Note:** P0 because the new common shell depends on it.

## Objective

Create one queue door and one queue overlay for Builder, Training, and Research lines.

## Header

Top-right:
`QUEUE [count]`

Count is supplied by model.

## Overlay tabs

- BUILDERS
- TRAINING
- RESEARCH

Model supplies rows for selected queue channel.

## Context strip

Each Manage tab receives a contextual strip from `ManageActivityVM`.

BUILD example:
`Archer Tower → L2 · 7m left · +4 queued`

ARMY:
`Footman · 40s left · +6 queued`

RESEARCH:
`Warding Runes · 9m left · +2 queued`

Tap opens Queue scoped to that channel.

## Queue commands

Existing legal actions may include:
- Finish Now
- Ad
- Cancel
- Move Up

Those commands must remain service/model-owned and exposed to UI as actions.

## UI restrictions

The queue overlay may not:
- inspect BuildTimerService directly
- calculate queue depth
- calculate remaining time
- calculate refund
- decide whether Finish Now is legal

## Acceptance criteria

- one queue overlay used from all Manage tabs
- contextual strip opens correct queue channel
- queue-blocked selected items can route here
- no dead per-destination queue drawer remains


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
