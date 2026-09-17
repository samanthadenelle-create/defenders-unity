feat(store): WO-1815 + WO-1819 - the shelf is SKR-only at flat amounts, with a measured sale sign

Owner: "change the store to SKR only and set to flat amounts" / "test with sales promos" / "so you can see screenshots of discounts". packs.json skrFlat per pack on the 100/200/300/500/1000/9999 ladder (1000 rung proposed), no "$" or per-SKR copy on the shelf, PurchaseQuoteService envelope Rate nullable, walletless banner "priced in SKR" (10 locales), Compact card name block fits two lines, gold 30% OFF plate (ink/fill 11.57:1, fill/surface 7.39:1 measured off the pixels), pulse kept, rotation reverted. Captures Builds/ui-capture/Store_SkrFlat_{NoSale,Sale30}_2670x1200.png (STORE_SKR_FLAT_CAPTURE_OK 2/2). StoreSkrFlatLadderRegression; ImpulsePack allowlist admits skrFlat; NightMarket suites updated.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU
