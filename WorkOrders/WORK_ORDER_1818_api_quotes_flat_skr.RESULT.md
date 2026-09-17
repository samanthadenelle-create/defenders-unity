# WO-1818 RESULT — the served quote now honours the flat SKR amount

**Status:** IMPLEMENTED
**Lane:** `api/**` + `test/**` SME. No git, no Unity, no deploy, no secret read, no live DB or oracle call.
**Ticket:** `WorkOrders/WORK_ORDER_1818_api_quotes_flat_skr.md`

---

## 1. Files changed (post-change line numbers, all re-read at source)

| File | Ranges | What |
|---|---|---|
| `api/_lib/purchase-catalog.js` | `:121-195` new block; `:441-462` `quoteFlatAmount`; `:465-545` `buildQuoteBody`; `:598-600` exports | `SKR_FLAT` derived from `sku-catalog.generated.json` (`:152`), `skrFlatFor` (`:171`), `isFlatSku` (`:177`), `FLAT_RATE_SOURCE='flat-skr'` (`:185`), `FLAT_ROW_RATE=0` (`:195`), `quoteFlatAmount` (`:454`), conditional rate guard (`:475`), flat branch + `usdRateForRow` in the body |
| `api/purchases/quote.js` | `:66-71` import; `:278-300` rate block; `:352-357` LIST envelope; `:415`, `:444-457` INSERTs | `needsRate` is data-driven (`:292`); `fetchSkrUsdRate` + the 503 skipped when no rate is needed (`:294`); both INSERT sites bind `${built.usdRateForRow}` (`:415`, `:457`) |
| `api/purchases/verify.js` | `:19-20` import; `:26-46` `ledgerRate` (`:42`); `:368`, `:424`, `:457`; `:466-468` `_test` | Stored `0` normalised back to `null` for a `flat-skr` row at the three echo/copy sites. **No amount arithmetic changed — none was needed.** |
| `api/schema.sql` | `:1368-1378` | Comment only beside `purchase_quotes.usd_rate`. **No DDL, no migration.** |
| `test/purchases.quote.test.js` | `:28-53` witness consts + the premise case; repoints at `:307`, `:338`, `:372`, `:414`; `:597-756` the new flat block. 756 lines | 8 new cases + 4 repointed |
| `test/store-sale.test.js` | `:40-55` witness consts; `:229` repoint; `:249-283` the new flat sale case; `:310-330` the flat half of the verifier case; repoints at `:332`, `:372`. 586 lines | 1 new case, 4 repointed, flat coverage added to the verifier case |

## 2. Test counts — exactly as run

```
node --test test/purchases.quote.test.js test/admin.skus.view.test.js
  ℹ tests 64   ℹ pass 64   ℹ fail 0

node --test test/*.test.js            (package.json "test" script's own glob)
  ℹ tests 771  ℹ pass 769  ℹ fail 1   ℹ todo 1   ℹ skipped 0
```

**Pre-change baseline, captured before the first edit:** `tests 761, pass 759, fail 1, todo 1`. The one
failure is the **same pre-existing, unrelated** case both times: `test/tunables-manifest.test.js:289`,
*"no server-authoritative money value can ever reach this manifest"*, actual `army.dismissRefundPercent`
against `/price|sku|entitle|grant|usd|payout|refund|cost|purchase|wallet/i`. The todo is
*"the streak reaches heartbound_state"* (`# WO-1693 finding 2`). **+10 tests, 0 new failures.**

⚠ `node --test test/` (a bare directory) does **not** work on this machine — Node 24.11.1 resolves it as
a module entry and dies with `Cannot find module 'D:\EoA\test'`. The working invocation is the glob in
`package.json`, `node --test test/*.test.js`, run from a shell that expands it.

## 3. The two findings worth the lead's attention

**a. `purchase_quotes.usd_rate` is `NUMERIC(24,12) NOT NULL` (`api/schema.sql:1366`), and a naive
`rate: null` would have 500'd the entire till.** Both INSERTs sit inside one `try` whose `catch` is
`quietFail(…500…)`, so every flat quote — 27 of 29 packs — would have failed on the deploy meant to fix
the price. Resolved **without a migration**: the row stores `FLAT_ROW_RATE = 0` with
`rate_source = 'flat-skr'` as the discriminator, the wire still says `rate: null`, and one helper
(`verify.js ledgerRate`, `:42`) normalises the 0 back to `null` on the **money path** — the response
echo and both `purchase_entitlements` copies. ⚠ The **admin views are deliberately not covered**:
`api/admin/db.js:312`, `:324` and `api/admin/stats.js:1061` SELECT `usd_rate` raw and will show
`0.000000000000` beside `flat-skr`. Operator reads, not money decisions, and `rate_source` is in the
same row — said out loud here because an earlier draft of this file and of the `schema.sql` comment
claimed *every* reader was normalised, which was false. Relaxing the column to NULL later is safe,
removes the wart, and is the owner's call; the code does not need it.

**c. The LIST envelope's `rate` is non-nullable on the shipped client — a latent device-wide crash,
now tripwired.** The per-row fields are safe: `PurchaseQuoteService.cs:84`, `:86`, `:131` declare
`double? UsdEffective`, `double? UsdSaving`, `double? Rate`, so the 27 flat rows that now send nulls to
every player parse fine. But the **envelope** declares `public double Rate` at `:509` (`ListEnvelope`)
and `:517` (`ListResponse`), and Newtonsoft throws converting null to `System.Double` — failing the
whole envelope, i.e. an empty store on every device. It **cannot fire today** (the shelf is not all-flat,
so a real rate is still fetched), which is exactly why it is a landmine: the day someone authors
`skrFlat` for the two monthly cards — a change made wholly in `Assets/`, by a seat with no reason to open
`api/` — the store dies silently. A new case in `test/purchases.quote.test.js` goes **RED the moment the
shelf becomes all-flat** unless those two client fields are `double?` first, and it also pins the three
per-row fields nullable. Fixing the C# is outside this silo: **hand-back to the lead.**

**b. The shelf still depends on coingecko for two SKUs, and that is a decision, not a bug.**
`monthly-wayfarer` and `monthly-keeper` are authored in `battle_monthly.json`, which
`tools/gen-sku-catalog.mjs` does not copy, so they carry no `skrFlat` and stay rate-derived. A **binding
quote for a flat SKU never touches the oracle**; the public **LIST** still fetches it because those two
rows are on the shelf, so an oracle outage still 503s the list. The condition is written data-driven
(`quotableSkus(network).every(isFlatSku)`), so authoring `skrFlat` for those two cards makes the shelf
oracle-independent with **no `api/` change** — that authoring lives in `Assets/`, outside this silo.

## 4. What I could not prove

- **Nothing was deployed and nothing ran against production.** `vercel.json` sets
  `git.deploymentEnabled:false`; the deploy is the owner's. Every claim above is from `node --test` on
  this tree plus files opened at source, never from a live request.
- **No live database was touched.** That the flat INSERT succeeds against the real
  `purchase_quotes.usd_rate NOT NULL` column is argued from the DDL text (`api/schema.sql:1366`,
  `api/migrations/20260824_0001_repair_schema_parity.sql:43`) and from `FLAT_ROW_RATE` being the
  integer 0 — it is **not** proven by an executed INSERT. `tools/schema-parity.mjs` needs
  `DATABASE_URL` and was not run.
- **No live coingecko call was made.** The oracle-down cases drive `global.fetch`, which proves the code
  path, not the third party.
- **The client half is unverified from here.** Whether the shipped APK renders the served figure is
  WO-1815/WO-1798's claim about `PackStore.cs` / `PurchaseQuoteService.cs`; this lane only read those
  two files where existing tests already grep them, and changed neither.
- **A live sale was not exercised end to end.** `store.saleBps` is a `client_tunables` row; setting it
  is `node tools/client-tunables.mjs set store.saleBps 3000` against the real database, which this lane
  did not run.
