# WORK ORDER 1815 — RESULT

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Branch:** `dev` (working tree; this lane did not commit, gate, push, or touch `CLI_LANES_WO_NUMBERS.md`)

---

## 1. What landed

| File | Lines / what |
|---|---|
| `Assets/Resources/Data/Canonical/packs.json` + `Assets/StreamingAssets/Data/Canonical/packs.json` | `pricing.skrFlat` on **27 of 29** packs (+1 `_schemaNotes.skrFlat` note at `:18`); `currencyDisclaimer` (`:27`) re-worded. Byte-identical twins. |
| `api/_lib/sku-catalog.generated.json` | **GENERATED ONLY** — `node tools/gen-sku-catalog.mjs`. Never hand-edited. See §5. |
| `Assets/_Modules/Commerce/PackCatalog.cs` | `:78-98` — `[JsonProperty("skrFlat")] public double SkrFlat;` on `PackPricing`, with the "authoring field, not a client price" contract. `:412-416` the `CurrencyDisclaimer` fallback re-worded with the data (it was a second copy of the sentence). |
| `Assets/_Modules/Wallet/PurchaseQuoteService.cs` | `:309-361` — `IsFlatPriced` (`:340`), `SkrEffectiveLabel` (`:343`), `HasStruckSkr(double)` (`:354`), `SkrAnchorLabel(double)` (`:359`). Pure formatters; one comparison, no price arithmetic. |
| `Assets/_Modules/Wallet/PackStore.cs` | `:2665-2694` sale line now strikes **SKR** (ranking recorded at `:2677`); `:3748-3840` the two switches (`ShowUsdAlongsideSkr` `:3764`, `SkrFlatShelfPrices` `:3784`) + `FlatSkrFor` `:3791` / `SkrShelfLabel` `:3795` / `WarnIfServedFigureIsNotFlat` `:3829`; `:3862-3876` `StorePriceMajor`'s walletless dollar branch retired (`skrShelf` at `:3874`); `:3902-3910` `StorePriceMinor`'s SKR-only stand-down; `:4597-4624` the confirm line. |
| `Assets/Editor/Regression/StoreSkrFlatLadderRegression.cs` | NEW — 9 case groups, marker `SKR_FLAT_LADDER_OK`, and the single home of the rounding rule (`DiscountedFlatSkr`). |
| `Assets/Editor/Regression/DataRegression.cs` | `:406-410` registers the new suite (the one line the brief allows) **+ one unrelated repair at `:792`, §6**. |
| `Assets/Editor/Regression/NightMarketNoWalletRegression.cs` | `:533-557` `[anchors]` re-pointed: the walletless Solana card must carry an **SKR** figure, and a `'$'` beside it is now its own named failure. |
| `Assets/Editor/UICaptureLaunch.cs` | `:230-238` `SkrFlatTargets`; `:3139-3225` `RunStoreSkrFlatCaptureHeadless` (marker `STORE_SKR_FLAT_CAPTURE_OK`); `:4582-4586` the two new statics; `:4588-4650` the stub row now prices from the authored rung through `DiscountedFlatSkr` (`:4624`) and emits the sale fields only when `saleBps > 0` (`:4628`). |

WO + RESULT: `WorkOrders/WORK_ORDER_1815_store_skr_only_flat_amounts_sale_proof.md` (+ this file).

## 2. The tier → SKR table as shipped (mapped by USD anchor; full rationale in the WO §3)

| USD | SKR | Packs |
|---|---|---|
| 1.99 | **100** | `builders-hour`, `impulse-{wood,iron,stone,crystals}-small` |
| 2.99 | **200** | `impulse-{wood,iron,stone,crystals}-medium` |
| 4.99 | **300** | `starters-hand`, `hearth-spark`, `keepers-satchel`, `bloomtide-bundle`, `impulse-{wood,iron,stone,crystals}-large` |
| 9.99 | **500** | `folks-thanks`, `permanent-builder`, `frostfall-bundle`, `embergrove-bundle`, `hero-wardrobe-pack`, `realm-defender-bundle` |
| 19.99 | **1000** ⚠ | `patron-of-elarion`, `echo-patron-pack`, `builders-cache` |
| 49.99 | **9999** | `founders-vow` |
| — | none | `welcome-500`, `welcome-100` (promo-only, no `pricing.usd`) |

⚠ **1000 is this lane's proposal, not the owner's ruling** — her ladder has five rungs and the shelf has six
USD bands; the two alternatives are named in the WO §3. **9999 SKR ≈ $175** at the rate WO-1798 captured,
3.5x its $49.99 anchor: her number, recorded with its consequence.

## 3. Proofs — each measured this session

**Data.** Binary patch, both copies, shape preserved (CRLF file, so CR is pinned too):

```
before: bytes 53565 ; LF 930 = CRLF 930 = CR 930 ; no BOM ; sha256 6b61f0fb...1ae08e
after : bytes 55439 ; LF 958 = CRLF 958 = CR 958 ; no BOM ; sha256 f9309e36...0b737f6
delta : +28 lines = 27 skrFlat fields + 1 _schemaNotes row     json.loads: OK on both
both copies byte-identical (same sha256)
```

**Capture — `STORE_SKR_FLAT_CAPTURE_OK 2/2; geometry=clean; touch=clean`**, judged on a FRESH UTF-16 log
(`Builds/store-skr-flat-capture6.log`, 479 909 bytes, 2026-09-16 21:29 — re-shot on the EXACT tree being
handed back, after the last edit), not on an exit code. Zero `error CS` under `Assets/`. The PNG byte
counts are identical to the previous run, so the last three edits provably changed no pixel.

- `Builds/ui-capture/Store_SkrFlat_NoSale_2670x1200.png` (2 393 556 bytes)
- `Builds/ui-capture/Store_SkrFlat_Sale30_2670x1200.png` (2 253 738 bytes)
- greyscale crop of the sign: `Builds/ui-capture/_skrflat_sale_tag_grey.png`

**Both PNGs opened and read.** No-sale frame: `1000 SKR`, `9999 SKR`, `500 SKR` on the PACKS row,
`500 / 300 / 100 SKR` on MOVING, `200 SKR` on the three CLOSE-THE-GAP chips. **Not one `$` appears on any
price, anywhere on the frame.** Sale frame: a bold `30% OFF` tag on six cards, the old figure struck
(`1000 SKR`, `9999 SKR`, `500 SKR`) with the discounted figure beneath it (`700`, `7000`, `350 SKR`) —
which is `ceil(flat x 0.70)` at every rung.

**Greyscale contrast, measured off the PNG** (WCAG relative luminance, not hex arithmetic; the owner is
colourblind so this is the gate):

```
sale tag ink vs tag fill    17.00:1      (bar 4.5:1)
sale tag ink vs card behind 14.15:1
struck "1000 SKR"           15.48:1
effective "700 SKR"         11.93:1
tag FILL vs card plate       1.20:1   <-- see the caveat below
```

⚠ **The tag's black fill barely separates from the dark card (1.20:1).** The sign reads by its TEXT, which
is far above the bar; it does not read as a raised badge. That is a look judgement for the owner, stated
rather than smoothed over.

**JS suites** (the two my data change can break): `node --test test/admin.skus.view.test.js
test/purchases.quote.test.js` → **55 pass, 0 fail**.

**Byte gates:** `python tools/gate_brace.py <7 files>` → `GATE_BRACE_SUMMARY bad=0 of 7` (exit 0);
Python NUL scan → `nul-clean` on all seven.

## 4. Two defects the capture caught, fixed before hand-back

1. **The sale line truncated on three cards at once.** `"was <s>1000 SKR</s> - ends in 2d 3h"` drew
   **11 of 21 printable glyphs** in the card's 272 px sale box at font 30 / NoWrap / Ellipsis
   (`patron-of-elarion`, `founders-vow`, `permanent-builder`; glyph-oracle lines in
   `Builds/store-skr-flat-capture2.log`). An SKR anchor is simply longer than the `$4.99` WO-1800 wrote
   that line for — **so the combined line has never fitted, and no Unity run had ever judged it** (WO-1800
   shipped with "NO UNITY RAN" in its own RESULT). Fixed by ranking: the strike wins when there is one and
   drops its now-redundant `"was "` prefix; the countdown is what the line says when there is no anchor to
   strike. Re-shot clean.
2. **My first re-word of `currencyDisclaimer` overflowed the footer** (32 of 47 glyphs at font 19 in a
   358 px band). Shortened to `"Priced in SKR - token value moves."` and re-shot clean. The original
   `"Token price moves with the market."` would have been a false sentence beside a flat price.

## 5. The `api/` change the lead must make

Full spec in the WO §6. **One item is already done and it is the only `api/` path this lane touched:**
`node tools/gen-sku-catalog.mjs` was run (three times, after each data edit) because
`test/admin.skus.view.test.js` pins the generated mirror EQUAL to canonical and goes red the instant
packs.json changes — the same reason WO-1801's lane ran it. **The file was never hand-edited.**

Still owed, and **the shelf is not genuinely flat until it lands**: `purchase-catalog.js` must read
`skrFlat` out of that generated mirror, price `ceil(flat x (10000-bps)/10000)` in `buildQuoteBody`, return
`rate: null` / `rateSource: 'flat-skr'`, make the `!rate` guard conditional (a flat SKU must quote when
coingecko is down — that is the *point*), and `quote.js:273` must skip `fetchSkrUsdRate` for flat SKUs.
Until then the served figure is still rate-derived; `PackStore.WarnIfServedFigureIsNotFlat` names both
numbers in the trace so the interim is a captured line, not a surprise.

## 6. ⚠ ONE REPAIR OUTSIDE THIS LANE, DISCLOSED

`Assets/Editor/Regression/DataRegression.cs:792` registered another lane's brand-new
`SyntyCastleUrpRegression` as `DeNelle.Editor.Regression.SyntyCastleUrpRegression`, but that file
(untracked, `??`) declares `namespace DeNelle.Editor`. **`error CS0234` — the whole editor assembly failed
to compile**, so no capture and no gate could run for any lane. Proof:
`Builds/store-skr-flat-capture.log`. Fixed by dropping `.Regression` from that one call. Their suite file
was not touched. **The lead should tell that lane** — it is the exact trap the comment eight lines above it
already warns about.

## 7. ⛔ UNPROVEN — read before believing anything above

1. **NO `COMPILE_GATE_OK` AND NO `REGRESSION_OK` FROM THIS LANE.** The tree compiled (the capture ran, zero
   `error CS` under `Assets/`), but that is not the gate, and **the new suite has never executed** —
   `DataRegression` was outside this lane's Unity allowance. Its own cases could be wrong.
1b. **⚠ A FIFTH JOB FOR `skrFlat` THAT THE WO's §2 DOES NOT LIST, and the interim it creates.** §2 gives
   the field four jobs; the code has a fifth — `PackStore.SkrShelfLabel` prints the authored rung when the
   server served no figure for that row, gated by `SkrFlatShelfPrices` (`:3784`) and now traced on every
   use (`FlowTrace.Once "skr-flat-fallback/<sku>"`). It exists because the walletless shelf must still say
   a price (WO-1409, pinned by `NightMarketNoWalletRegression`) and the only honest figure left is the
   constant. **The scenario to know: public LIST unreachable + binding QUOTE reachable ⇒ the shelf prints
   `300 SKR` and the confirm step says `you will send exactly 431 SKR`.** The CHARGE is protected (the
   confirm line states the server's own figure before any signature, and `MatchesBaseUnits` refuses a
   quote that cannot round-trip) — but the SHELF misled, and that gap closes only when WO-1815 §6 lands.
   ⚠ Also note the confirm line now drops the dollars on the flat path, which **supersedes the owner's
   2026-08-23 "be transparent that price is approx 2.99" ruling** at that step; the 09-16 SKR-only ruling
   is the later word, and the rate line is still printed verbatim for a rate-derived quote.
2. **The suites I re-pointed or could have broken are un-run:** `NightMarketNoWalletRegression` (edited),
   `NightMarketUiRegression`, `StorePiSkinCurrencyRegression`, `BuyGateAndPriceLadderRegression`,
   `StoreReturnToManageRegression`, `ImpulsePackRegression`. Each was read at source and the pinned
   literals were kept in place and in order — **read, not executed.** Two worth naming:
   `StorePiSkinCurrencyRegression:311-320`'s two `RequireOrdered` sequences both still hold (the
   `AmountLabel` return is still `StorePriceMajor`'s last statement after `if (PiDisplay)`, and the
   `UsdApprox()` return still follows `if (PiDisplay)` inside `StorePriceMinor` — kept COMPILED behind the
   switch precisely so it would); and `ImpulsePackRegression` CASE 13 `[gap-reaches-shelf]` (`:987-1075`)
   turned out to be **source-structural** — its `"Price unavailable" -> "UNAVAILABLE"` text sits inside a
   failure MESSAGE, not in an expectation about rendered copy — so the SKR-only catch-up chips (`200 SKR`
   in both PNGs) do not touch it. `NightMarketNoWalletRegression`'s `[anchors]` case was sharpened rather
   than merely widened: on the walletless Solana shelf it now REQUIRES an SKR figure and FAILS on a `'$'`
   beside it, which is the composed-tree form of the owner's ruling (a source grep could not have said it).
3. **The PULSE IS NOT PROVEN.** It exists (WO-1800: `PackStore.cs:440/501/825/1814-1821/2753/2813`,
   `StorePackCard.EvaluatePulse:389`) and this lane added none, but the capture composes by
   `Awake -> EnsureBuilt -> Render` and never ticks `Update`, so both stills show cards at rest.
4. **⛔ THE WALLETLESS BANNER STILL SAYS "prices shown in USD"** — visible top-left in BOTH PNGs, and it is
   now false. `storeWalletlessBrowsingBanner` is a **localized** key with real translations in **10
   languages** (`Assets/Resources/Data/Canonical/{ar,de,en,es,fr,ja,ko,pt-BR,ru,zh-Hans}.json:420` + the
   StreamingAssets twins), and every one of those files is currently dirty from another lane. Re-wording
   English alone would leave nine locales lying. **This needs its own ticket and a translation pass.**
5. **`"Raise the Barracks"` truncates — 14 of 16 glyphs — in BOTH frames.** WO-1801's pack name, present
   before this lane and unrelated to it. That WO's RESULT reasoned about the name's width from
   "Permanent Builder" (17) without a capture; the measurement says 18 does not fit. Theirs to fix.
6. **The 1000-SKR rung and the whole ladder are a PROPOSAL.** One owner word changes a number in
   packs.json and one array in the suite.
7. **The bottom shelf row is clipped by the scroll viewport in both frames** — on the sale frame the
   visible part of those cards shows the STRUCK price with the discounted one below the fold until the
   player scrolls. Pre-existing scroll behaviour, not introduced here, but worth her eyes.
8. **Only 6 of the 9 shelf rows carry a ribbon** (measured in the built tree, not assumed). WO-1800's
   ranking — state word > sale ribbon > merchandising badge — suppresses the other three. That ranking is
   that lane's own design call, still un-ruled.
9. **No live rate was fetched this session.** Every "≈ $X" in the WO §3 is WO-1798's single coingecko call
   at 2026-09-16 21:48Z and is already stale.
