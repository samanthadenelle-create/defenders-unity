# WO-1497: the placed-structure MOVE door shipped under a colliding "WO-1445" and has no ticket of its own

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:07:43, build 2026.09.08.361259). PRIOR STATUS: FIXED - ON THE SEEKER 2026.09.07.358574 - see RESULT
**Silo:** Village/BuildMode (`BuildCollectionBrowser`, the Manage Placed card). Board hygiene.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1497 -> 1498 in the same edit).

## 1. EVIDENCE

Commit `32659c0f6` opens:

```
WO-1445 - the tester's blocker. "He accidentally put a palisade down..."
```

But WO-1445 ON DISK is `WORK_ORDER_1445_offline_grant_discards_clamped_remainder.md`, an unrelated economy
ticket minted the same day. The move door has NO work-order file:

```
grep -rl "Manage Placed\|palisade" WorkOrders/*.md   ->   no ticket for this work
BuildCollectionBrowser.cs:76                          cites "WO-2006 (ruling section 25)" for the same card
```

So the shipped feature is referenced by three different numbers and owned by none. The commit message's
"WO-1445" refers to THIS ticket.

## 2. THE THREE RULINGS, FROM THE COMMIT MESSAGE (recorded here as canon)

1. **A placed palisade is SELECTABLE** - the tester's blocker was that a misplaced wall could not be picked up.
2. **SELL refunds about 50%; CANCEL refunds a flat 100%** - two different doors with two different prices, and
   they must read differently on the face.
3. **MOVE is FREE and LEVEL-PRESERVING** - relocating a structure costs nothing and does not reset its level.

## 3. WHAT IS LEFT

- Nothing to implement; the door is on the Seeker build. This file exists so the board shows it and so the
  three rulings live somewhere other than a commit message.
- Reconcile the reference in `BuildCollectionBrowser.cs:76` to cite this number alongside WO-2006.

## 4. ACCEPTANCE
- [ ] This file appears on `BOARD.html` after `python tools/board_build.py`.
- [ ] `BuildCollectionBrowser.cs:76` cites this WO number.
- [ ] The three rulings copied into `DESIGN-DECISIONS.md`.
