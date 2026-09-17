# WO-1818 — the served quote must honour the FLAT SKR amount

**Status:** IMPLEMENTED

**Silo:** `api/**` + `test/**` only (Node, Vercel serverless). No Unity, no `Assets/`, no git, no deploy.
**Owner ruling (2026-09-16, verbatim):** *"change the store to SKR only and set to flat amounts"*
**Predecessor:** WO-1815 (client + authoring). Its §6 is this ticket's spec; §6 said the `api/` half was
the lead's and was not touched by that lane.

---

## 1. The defect, stated as the player meets it

WO-1815 authored `pricing.skrFlat` into both `packs.json` copies and regenerated
`api/_lib/sku-catalog.generated.json` (verbatim, `node tools/gen-sku-catalog.mjs`). The **shelf** then
drew the flat figure — **300 SKR**. The **server quote did not read that field at all**, and still
priced `ceil(usd_anchor / coingecko_low_24h)` — **431 SKR** at the rate live that day.

One purchase, two prices, and the one that gets **charged** is the second. This is the same family as
the paid-but-not-granted cases `api/_lib/purchase-catalog.js` is written around: `/verify` runs *after*
the transfer settles, so a figure the player never agreed to cannot be walked back.

A second, quieter half: a flat price whose quote still fetched coingecko **503'd when coingecko was
unreachable** — refusing a sale the server already knew the exact answer to. That is the whole point of
flat, and it was the opposite of what shipped.

## 2. The quote chain, read at source 2026-09-16 (line numbers are pre-change)

| # | Step | Where |
|---|---|---|
| 1 | handler entry, body parse, network/sku validation | `api/purchases/quote.js:187-215` |
| 2 | store seal (kill switch), canary short-circuit, unsold-SKU 404 | `api/purchases/quote.js:244-278` |
| 3 | **the rate — fetched unconditionally, FAIL CLOSED → 503** | `api/purchases/quote.js:273` calling `fetchSkrUsdRate()` at `api/_lib/purchase-catalog.js:300` |
| 4 | storewide sale read (`store.saleBps`, epoch-MINUTES end) | `readStoreSale` → `api/_lib/store-sale.js:143-152`; `resolveSale:96-108`; `resolveDiscount:133-139` |
| 5 | LIST mode: one `buildQuoteBody` per candidate, shelf = till | `api/purchases/quote.js:302-340` |
| 6 | QUOTE mode: shortfall window, `resolveDiscount`, `buildQuoteBody` | `api/purchases/quote.js:343-372` |
| 7 | **the price itself** — `usdAnchor` × rate → `quoteAmount` (`ceil` to whole SKR) → base units | `api/_lib/purchase-catalog.js:365-395` (`buildQuoteBody`), `:281-290` (`quoteAmount`) |
| 8 | persist the row (two INSERT sites: shortfall-gated serializable, and ungated) | `api/purchases/quote.js:385-395` and `:422-432` |
| 9 | wire shape, incl. `saleBps`/`saleLabel`/`saleEndsAt` | `api/purchases/quote.js:122-158` (`wireQuote`), `:160-180` (`wirePinned`) |
| 10 | **verification** — contract from the PERSISTED ROW, exact string compare | `api/purchases/verify.js:305` (`contractFromQuoteRow(quote)`), `:88` (`===`), `:131`, and `purchase-catalog.js:429-446` |

**The two guards that made flat impossible:**
- `purchase-catalog.js:368` — `if (!rail || usd == null || !rate || !(rate.usdPerSkr > 0)) return null;`
  A flat SKU could not quote without a rate it does not use.
- `quote.js:273-281` — the rate is fetched for *every* request and its absence is an unconditional 503.

**The blocker nobody had hit yet:** `purchase_quotes.usd_rate NUMERIC(24,12) **NOT NULL**`
(`api/schema.sql:1366`, and `api/migrations/20260824_0001_repair_schema_parity.sql:43`). Returning
`rate: null` and inserting it would have thrown inside the `try` at `quote.js:378-436` and answered
`quietFail 500` — **the till dead for 27 of the 29 packs**, on the very deploy meant to fix the price.
This lane cannot run a migration and cannot prove one was run, so the code must not require one.

## 3. Facts measured this session (not read from a doc)

- `api/_lib/sku-catalog.generated.json` carries **29** `packs[]` rows; **27** have `pricing.skrFlat`
  (100 / 200 / 300 / 500 / 1000 / 9999). `welcome-500` and `welcome-100` have no `pricing.usd` and are
  not quotable at all.
- **`monthly-wayfarer` and `monthly-keeper` are NOT in `packs[]`.** They are authored in
  `battle_monthly.json`, which `tools/gen-sku-catalog.mjs` does not copy, so they carry **no
  `skrFlat`** and remain rate-derived. "Every quotable SKU is flat" is false today.
- Full suite **before** any edit: `node --test test/*.test.js` → **tests 761, pass 759, fail 1, todo 1**.
  The one failure is pre-existing and unrelated (`test/tunables-manifest.test.js:289`, actual
  `army.dismissRefundPercent` matching a money-word regex).

## 4. What was implemented

1. **`api/_lib/purchase-catalog.js`**
   - `SKR_FLAT` frozen map **derived at require time** from `./sku-catalog.generated.json` `packs[]`
     (`pricing.skrFlat`, whole positive integers only; junk is dropped, never rounded). The JSON is
     required **directly**, never `./sku-catalog.js`, which requires this file back (cycle).
   - `skrFlatFor(sku)`, `isFlatSku(sku)`, `FLAT_RATE_SOURCE = 'flat-skr'`, `FLAT_ROW_RATE = 0`.
   - `quoteFlatAmount(flatSkr, bps, decimals)` — `floor((flat*(10000-bps) + 9999)/10000)`, i.e. **ceil**,
     the same direction as `quoteAmount`, then the existing `BigInt(skr) * 10n ** BigInt(decimals)`.
   - `buildQuoteBody` — the rate guard is now **conditional on non-flat**; flat prices from the constant
     and returns `rate: null`, `rateSource: 'flat-skr'`, `usdEffective: null`, `usdSaving: null`,
     `usdAnchor` **kept**, and one new field `usdRateForRow` (the DB-safe value: the real rate on the
     rate path, `FLAT_ROW_RATE` on the flat path). Sale fields (`discountBps`/`Label`/`Reason`) are
     untouched by flatness, so `wireQuote` still emits `saleBps`/`saleLabel`/`saleEndsAt`.
2. **`api/purchases/quote.js`** — `needsRate` is data-driven: `sku ? !isFlatSku(sku) :
   !quotableSkus(network).every(isFlatSku)`. `fetchSkrUsdRate` and the worded 503 are skipped when no
   rate is needed; the refusal is byte-identical for anything that needs one. LIST envelope reports
   `rate: null` / `rateSource: 'flat-skr'` when no oracle was consulted. Both INSERT sites now bind
   `${built.usdRateForRow}` instead of `${built.rate}` — **one rule, computed once**, not `?? 0` twice.
3. **`api/purchases/verify.js`** — **no amount arithmetic changed and none was needed** (see §5). Added
   `ledgerRate(quote)`: normalises the stored `0` back to `null` when `rate_source === 'flat-skr'`,
   used at the three places the stored rate is echoed or copied forward. Exported on `_test`.
4. **`api/schema.sql`** — comment only, beside `purchase_quotes.usd_rate`, recording that
   `rate_source='flat-skr'` means the 0 is "no oracle", and that relaxing the column to NULL is a safe
   future migration the code does not require. **No DDL, no migration file.**

**Deliberately NOT done:** WO-1815 §6.5's optional `skrAnchor` wire field (new client-facing surface,
not required — the flat constant is pinned equal across both copies by the regressions); any change to
`tools/gen-sku-catalog.mjs` (`skrFlat` already arrives with the verbatim copy); the Pi and Google Play
rails, which price off `pricing.usd` and are out of scope for an SKR-only ruling.

## 5. Item 3 — the verification path recomputes nothing. Cited.

`api/purchases/verify.js` builds the exact-equality contract from the **persisted quote row**:
`contract = contractFromQuoteRow(quote)` at **`verify.js:305`**, reading `row.amount_base_units`
(`purchase-catalog.js:429-446`); the chain is compared by exact **string** equality at
**`verify.js:88`**; a row without a usable amount is refused at **`verify.js:131`**. A grep of
`verify.js`, `fulfill.js` and `reconcile.js` for `quoteAmount` / `fetchSkrUsdRate` / `buildQuoteBody`
returns **nothing** — there is no rate-based recomputation anywhere on the settle path. **So the flat
amount is accepted by construction**, exactly as the sale amount already was, and the only change
required there was the honesty of the reported rate field.

`purchase_entitlements.usd_rate` is **nullable** (`api/schema.sql:1175`), so `ledgerRate` writing
`null` into it is legal on both copy sites (`verify.js:345`, `:401`).

## 6. Tests

`test/purchases.quote.test.js` — 9 new cases (including the client-envelope tripwire, §7): the flat ladder is **derived** (deep-equal against the
generated catalog, so a retyped table goes red), both witness SKUs still sit on the path their cases
assume, flat with no sale, flat with the oracle **down** (three dead-rate shapes), flat with
`saleBps=3000` → **ceil** plus the wired badge fields, ceil at every authored rung × bps, junk-bps
refusal on the flat path, and the verifier reading the persisted amount + `ledgerRate` normalisation.

**Four existing cases were REPOINTED, not relaxed** (`purchases.quote.test.js`), and four more in
`test/store-sale.test.js`: they asserted the rate-derived law against `impulse-wood-medium`, which is
now **flat**, so their assertions had stopped describing the path they name — including *"no rate means
NO quote"*, which a flat SKU must now deliberately violate. Each moved to `monthly-wayfarer`, which is
still rate-derived, keeping the fail-closed law proven; `store-sale.test.js` also gained a flat-sale
case and a flat verifier case. Every repoint carries an in-file comment saying the rule did not change,
the SKU did.

## 7. Acceptance

- [x] A SKU with `skrFlat` quotes `ceil(skrFlat * (10000 - saleBps) / 10000)` whole SKR, scaled by the
      existing per-network decimals.
- [x] `rate: null`, `rateSource: 'flat-skr'`, `usdEffective`/`usdSaving` null, `usdAnchor` kept.
- [x] `fetchSkrUsdRate` is not called for a flat SKU; a flat SKU quotes with the oracle down.
- [x] The `!rate` guard is conditional; non-flat SKUs still fail closed with the same worded 503.
- [x] `saleBps` / `saleLabel` / `saleEndsAt` still returned for flat SKUs.
- [x] The verification path accepts the flat amount (it reads the persisted row — `verify.js:305`).
- [x] `node --test test/purchases.quote.test.js test/admin.skus.view.test.js` → **64/64 pass**.
- [x] `node --test test/*.test.js` → **771 tests, 769 pass, 1 fail, 1 todo**; the single failure is the
      pre-existing, unrelated one present in the pre-change baseline.
- [ ] **Client envelope nullability — the lead's call before deploy.** Safe today, tripwired for the
      all-flat future: see the RESULT §3c (`PurchaseQuoteService.cs:509`, `:517`).
- [x] No `*.generated.json` hand-edited.
- [ ] **Deploy — the owner's, not this lane's.** `vercel.json` sets `git.deploymentEnabled:false`, and
      nothing here has run against the live database or the live rate oracle.

## 8. What NOT to touch

`Assets/**`, `CLI_LANES_WO_NUMBERS.md`, `tools/gen-sku-catalog.mjs` (no change needed),
`api/_lib/sku-catalog.generated.json` (generated only), the two canaries and `wirePinned`,
`pricing.skr` (dead field, left alone), the Pi rail (`api/pi/*`, `api/_lib/pi-payments.js`) and the
Google Play rail, both of which price off the USD anchor by design.
