feat(store): WO-1799 - storewide sale knob on the tunables rail (server half): 30% off every quotable SKU in one command

Owner 2026-09-16: "can we run a 30% deal?" store.saleBps / store.saleEndsAtEpochMin as server-only rows; the sale REPLACES the once-per-week shortfall discount when larger (max, never additive), is persisted on every quote with discount_reason 'sale' so verify/fulfil accept the discounted amount exactly as they accept the shortfall one, and the public shelf LIST carries saleBps/saleLabel/usdEffective/saleEndsAt per row. Fixed on the way: both shortfall-window predicates keyed on discount_bps IS NOT NULL, so a sale would have denied every buyer their shortfall discount for 7 days after it ended. Google Play is excluded (Play Console owns those prices). node --test 761/760/0 fail/1 todo.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
