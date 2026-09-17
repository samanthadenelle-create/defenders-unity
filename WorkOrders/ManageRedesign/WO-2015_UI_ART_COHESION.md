# WO-2015 — Standardize Manage Art and Icon Contracts

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:19:08, build 2026.09.17.373943). PRIOR STATUS: FIXED - awaiting owner test-build art/cohesion proof (2026-09-10 status ruling). Existing portraits and current Manage frames are built; this status does not grant visual approval. See docs/READY_CLEARANCE_2026-09-10.md. PRIOR STATUS: READY TO IMPLEMENT

**Priority:** P1  
**Depends on:** new layout geometry stable

## Objective

Make the simplified UI cohesive without embedding gameplay state into artwork.

## Building portrait contract

- square source
- full building visible
- consistent elevated 3/4 perspective
- consistent footprint positioning
- dark Elarion palette
- controlled warm focal light
- transparent/near-transparent vignette edges
- no text
- no UI frame
- no baked lock/max/rarity state

## Troop portrait contract

- same portrait framing across all 9
- readable silhouette
- no UI text in art

## Required navigation icons

- Build
- Army
- Research
- Queue
- Heart

## Required state icons

- upgrade affordable
- upgrade unaffordable
- in progress
- queue blocked
- locked
- max

State belongs to UI overlay, not portrait.

## Asset keys

VM supplies asset keys.

UI must not derive portrait filename from gameplay ID unless a shared asset registry is already the canonical design.

## Acceptance criteria

- all grid assets readable at final tile size
- state overlay remains legible independent of portrait color
- no image contains baked lock or level


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
