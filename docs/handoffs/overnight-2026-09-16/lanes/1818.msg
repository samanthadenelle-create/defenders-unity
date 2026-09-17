feat(api): WO-1818 - quotes and the ledger honour flat SKR prices

A SKU with skrFlat quotes ceil(flat*(10000-saleBps)/10000) whole SKR with rate null / rateSource flat-skr, no coingecko dependency; verify reads the persisted row (no recompute); usd_rate NOT NULL satisfied by 0 under flat-skr. Tests 90/90 on the three store files, 769/771 overall (the remaining one was the tunables money-word test, fixed by WO-1811 renaming its knob). Needs the owner-run production deploy.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
