# WO-1492: thirteen ManageRedesign work orders carry no Status line and are invisible to the board

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Board hygiene. `WorkOrders/ManageRedesign/` + `tools/board_build.py`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1492 -> 1493 in the same edit).

## 1. EVIDENCE

`WorkOrders/ManageRedesign/WO-2001` through `WO-2017` carry no `**Status:**` line. `BOARD.html` is DERIVED
from those lines (CLAUDE.md sec.2), so a seventeen-ticket program - the largest lane in flight - does not
appear on the board at all.

The true states, from this session's audit of each:

```
2001 LANDED   2002 LANDED   2003 FIXED    2004 UNPROVEN  2005 FIXED
2006 LANDED   2007 PARTIAL  2008 LANDED   2009 PARTIAL   2010 PARTIAL
2011 FIXED    2012 PARTIAL  2013 LANDED   2014 PARTIAL   2015 NOT DONE
2016 PARTIAL  2017 FIXED
```

## 2. FIX SHAPE

- Add a `**Status:**` line to each of the seventeen files using the board's vocabulary, seeded from the states
  above. Where the audit says PARTIAL, name what is outstanding in one clause.
- Make `tools/board_build.py` FAIL on any `WorkOrders/**/*.md` work-order file with no `**Status:**` line, so
  a whole program can never go invisible again. That is the durable half.

## 3. WHAT NOT TO DO
- Do not mark anything DONE on the audit's word alone. LANDED and FIXED are claims until re-verified; use the
  vocabulary that says so.

## 4. ACCEPTANCE
- [ ] All seventeen carry a Status line; `python tools/board_build.py` shows them on `BOARD.html`.
- [ ] `board_build.py` fails on a WO file with no Status line; proven by removing one locally.
