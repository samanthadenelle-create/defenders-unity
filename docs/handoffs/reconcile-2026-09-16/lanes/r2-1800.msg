feat(store): WO-1800 - sale badges on the pack cards and the open-wave pulse

Owner 2026-09-16: "put big sales signs with x% off!!!! you know some flash" / "make the packs pulse when the store opens". A bold near-black tag ("30% OFF"), struck anchor, sale price large, countdown line when the sale has an end, fail-closed to no badge (a zero never renders as 0% off); the shelf plays one staggered attention pulse per card on open, discounted cards keep the badge pulse; greyscale-safe by shape and weight; NightMarketUiRegression.CheckStorewideSale + a headless NightMarket_Sale capture entry. Also fixed: the walletless card would have printed the full price as its one large figure on a sale.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
