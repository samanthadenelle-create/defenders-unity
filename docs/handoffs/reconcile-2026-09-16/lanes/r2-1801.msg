feat(store): WO-1801 - the first-sale path: the $1.99 first buy completes the Barracks and a shortfall door offers it at the moment of need

Owner 2026-09-16: "i just want a first sale". 30-day production funnel: store opened by 3 ids, pack tapped by 2, purchases 0. builders-hour re-authored to 700 wood / 360 iron / 300 stone + crew ("Raise the Barracks": covers the Barracks 600/320 with headroom at the 2000 base cap, iron kept under the iron rung so it is not dominated); a real shortfall door on the build-info panel ("Short 320 Iron - raise it now for $1.99") opening the store on that pack with the shortfall hint, once per session per structure, never in combat, gone after any purchase; shortfall_offer_shown/tapped events; FirstSalePathRegression with no cost/cap literals. The old FocusShortfall had zero callers. sku-catalog.generated.json regenerated from packs.json.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
