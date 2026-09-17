docs(board): reconcile the 09-09..09-16 evidence, handoffs and ticket record

Work orders, RESULT files, handoff notes, MASTER_CATALOG updates, the 09-13 checkpoint inventory and this 09-16 reconciliation plan. No runtime code. Regenerate BOARD.html (python tools/board_build.py) in this same commit so the derived board matches the Status lines the lane commits flipped.

Gated: <FILL - COMPILE_GATE_OK + REGRESSION_OK n/n, log path, HH:MM, run on this exact tree before any commit in this sequence>

Also removes docs/WARDROBE_ARCHITECTURE.zip (20 MB, dead since 2026-07-04; owner ruling 2026-09-16 "is deleted so thats correct call"). Carries the 2026-09-16 canon docs (raid difficulty levers, SKR and Heartbound briefs, WO-1766/1769-1775 tickets, the reconciliation plan) and the regenerated BOARD.html.
