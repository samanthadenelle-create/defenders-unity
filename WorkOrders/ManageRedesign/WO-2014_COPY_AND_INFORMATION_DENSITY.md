# WO-2014 — Normalize Manage Copy and Remove Implementation-State Noise

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:19:07, build 2026.09.17.373943). PRIOR STATUS: FIXED - awaiting owner test-build proof (2026-09-10 status ruling). Current mockup screens and one-heading/state-word presentation are built/captured; owner judges remaining copy/density against the latest rulings. See docs/READY_CLEARANCE_2026-09-10.md. PRIOR STATUS: IN PROGRESS - PARTIAL: the audit of 2026-09-06 records partial delivery through the mockup screen rebuilds; no WO-2014 marker exists in the tree and the copy/density acceptance is not verified at HEAD.

**Priority:** P0  
**Depends on:** selected-item VMs

## Objective

Reduce text to decision-relevant information.

## Copy rule

Every selected item should answer:

- what is it
- what does it do
- what changes
- what it costs
- what can I do
- why not, if blocked

## Remove/rewrite

Avoid:
- "2 placed · lowest L1"
- duplicate "Max / MAX LEVEL / At max level"
- "Building Now" when a timer already communicates progress
- raw queue mechanics unless relevant
- internal IDs
- "Village Tier" if Heart Level is the same progression concept

## Model responsibility

All final strings should come from VM/model or localization/content layer.

UI should not concatenate business-language strings beyond harmless formatting.

Bad:
`"Requires " + tierType + " " + tier`

Good:
VM supplies:
`Requires Heart Level 5`

## Acceptance criteria

- one state sentence maximum in normal detail
- no duplicate max messaging
- no UI-generated progression terminology


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
