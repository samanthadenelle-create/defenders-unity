# WORK ORDER 1799 — A storewide sale knob, flippable without a build

**Status:** IMPLEMENTED, NOT YET GATED

**Owner, 2026-09-16, verbatim:** *"can we run a 30% deal?"*

**Lane:** `api/` JavaScript + `test/` + `docs/` ONLY. **Nothing under `Assets/` was touched by this lane** — the owner wants the sale without a new APK. Nothing under `ServerData/` is involved, so CLAUDE.md §16 does not bind this change.

**Every file:line below was opened at source on 2026-09-16.** Claims this lane could not prove are collected in §9 and named as unproven.

---

## 0. What existed before this ticket

There was **no storewide sale and no way to run one.** The only discount in the game was the per-wallet **shortfall** discount:

* `SHORTFALL_DISCOUNT_BPS = 2000`, `DISCOUNT_WINDOW_DAYS = 7` (`api/purchases/quote.js:82-83`)
* `discountBpsForReason` (`api/purchases/quote.js:106-108`) — 2000 bps, once per 7 days, and **only** when the client sends the `repair_shortfall` reason hint
* persisted on `purchase_quotes.discount_bps` / `.discount_reason` (`api/schema.sql:1372-1376`, WO-1177)

The shipped client already renders a **server-issued** discount on the confirm line: `discountBps` / `discountLabel` / `usdEffective` / `usdSaving` are parsed in `Assets/_Modules/Wallet/PurchaseQuoteService.cs:84-90` and printed by `PackStore.cs:4035-4041`. So the wire to show a discount **already existed and already worked**. What did not exist was a way to *cause* one.

⛔ **And the price is server-decided by construction, which is what makes a sale possible at all.** `/verify` checks the chain against the **persisted quote row's own** `amount_base_units` (`contractFromQuoteRow`, `api/_lib/purchase-catalog.js:408-424`, compared at `api/purchases/verify.js:88`). There is no client-supplied amount anywhere on that path (`verify.js:230-232` says so out loud). A discounted quote is therefore accepted *without any change to the verifier* — proven in §5, not assumed.

---

## 1. The shape: SERVER-ONLY rows on the existing `client_tunables` rail

Two rows, added to the `TUNABLE_KEYS` allowlist (`api/_lib/tunables.js`), read through the **same** `readTunables` helper `api/client-tunables.js` uses. **No second reader, no new table, no deploy to start or stop a sale.**

| key | kind | resting state | meaning |
|---|---|---|---|
| `store.saleBps` | int | absent / `0` = **NO SALE** | basis points off the USD anchor of every quotable SKU. `3000` = the owner's 30% deal. **Clamped 0..7000 on the read.** |
| `store.saleEndsAtEpochMin` | int | absent / `0` = until cleared | epoch **MINUTES** at which the sale stops by itself |

### ⛔ Why these carry `serverOnly: true` — and why they get NO Command Center card

The brief pointed at the WO-1682 D3 `serverOnly` marker in `api/_lib/tunable-manifest.js:1011-1052`. That marker is honoured **on a `PRESENTATION` row**, which puts a **card on the Command Center page** — and that page's own `OUT_OF_SCOPE_NOTICE` (`tunable-manifest.js:992-996`) reads *"Prices, purchase amounts, entitlements and grants are NEVER editable here and never will be"*, pinned by `test/tunables-manifest.test.js`'s *"no server-authoritative money value can ever reach this manifest"*.

**A sale percentage is a price.** So this lane read the marker on the **allowlist spec** instead:

```js
{ key: 'store.saleBps', kind: 'int', serverOnly: true },
```

and taught `mismatches()` to honour it there (`api/_lib/tunable-manifest.js`, the `spec.serverOnly === true` skip). That buys **exactly the same one exemption** — from the build-registry join, because no build registers these keys — and nothing else. Result: the key is **writable** by the operator CLI, **invisible** on the balance page, the three-way join still agrees, and the money boundary and the ZERO-`serverOnly`-`PRESENTATION`-rows tests are **untouched**.

**⚠ This is a deliberate deviation from the brief's pointer (CLAUDE.md §11B.B).** If the owner wants a sale card on the Command Center, that is her ruling to make and it changes the boundary notice with it.

### ⛔ And the two rows sit OUTSIDE the `TUNABLE_KEYS` array literal, on purpose

`Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` case 4 (`Case4_KeyDomain` → `CompareDomain`) **reads `api/_lib/tunables.js` from disk**, matches `TUNABLE_KEYS = [ … ];` with a regex, pulls every `key: '…'` out of that literal and asserts the result is **exactly** its pinned `ExpectedDefaults` — reporting anything extra as an `UNPINNED key`. A `serverOnly` key has **no build entry by design**, so writing it inside that literal **REDS the Unity regression**, and this lane is forbidden to touch `Assets/` to fix it.

So the literal holds the **client-read domain the build oracle is entitled to pin**, and the two sale rows are appended after it into the same runtime array:

```js
const SERVER_ONLY_TUNABLE_KEYS = [ { key: 'store.saleBps', kind: 'int', serverOnly: true }, … ];
for (const spec of SERVER_ONLY_TUNABLE_KEYS) {
    if (!spec || spec.serverOnly !== true) throw new Error(/* loud at module load */);
    TUNABLE_KEYS.push(spec);
}
```

**The append block is GUARDED so it cannot become a back door:** a client knob placed there would escape the build oracle *and* be withheld from every phone — two failures at once, neither with a symptom — so the loop **throws at module load** on any row missing the marker.

**Measured, not assumed:** `ExpectedDefaults` holds **71** keys; the literal region now yields **71** with **zero** `store.*` inside; the runtime allowlist holds **73**. Two tests re-implement the C# regex so a future seat moving the rows back inside goes red in two seconds instead of at a batchmode gate.

**⚠ THE TIDIER FIX IS THE LEAD'S CALL:** a one-line skip for `serverOnly: true` specs in that C# oracle. With it, these rows move back inside the literal and the whole structure deletes. This lane could not make it (`Assets/` is out of scope — the owner wants the sale with no new APK).

### ⛔ The sale percentage never reaches a phone

`api/client-tunables.js` is **public and unauthenticated**. It now **withholds every `serverOnly` key** from its payload, with the exclusion set **derived from the allowlist** (`TUNABLE_KEYS.filter(s => s.serverOnly === true)`) rather than hand-typed — a typed copy would ship the next such knob's value to every phone, which is CLAUDE.md §2/§5/§16 wearing a money face. A sale bps on a device we do not control is a **second authority about money**.

### ⚠ Two shape constraints the rail imposes, and neither is worked around

1. **NO `store.saleLabel` ROW.** `normalizeValue` (`api/_lib/tunables.js`) accepts a bool or `/^-?\d{1,9}$/` and nothing else — **the rail stores no strings.** The label is therefore DERIVED once, server-side: `saleLabel(3000)` → `"30% off"`.
2. **THE END TIME IS EPOCH *MINUTES*.** Epoch seconds is **ten** digits; the validator caps at **nine**, so a seconds value is **refused at write time** and would read as a mistyped key. Minutes is eight digits and also fits a C# `int`. ⛔ **Do not widen `\d{1,9}`** — it widens every key on the rail.

---

## 2. The combination rule — MAX, never additive

`resolveDiscount(saleBps, shortfallBps, shortfallReason)` (`api/_lib/store-sale.js`), pure and directly tested:

* **sale ≥ shortfall → the sale wins**, reason `'sale'`
* **shortfall > sale → the shortfall wins**, reason `'repair_shortfall'`
* **neither → `{bps: null, reason: null}`** — null, never `0`, because a formatter prints `"0% off"` happily
* **a tie goes to the sale**, which leaves the player's once-per-7-days shortfall window **unspent**

`2000 + 3000 = 5000` would be a 50%-off pack nobody authored, on every SKU at once. And the shortfall discount is a per-wallet **apology for a bad moment**; a sale must not multiply an apology.

### ⛔ The blind spot this ticket had to close: a sale would have POISONED the shortfall window

Both shortfall predicates keyed on `discount_bps IS NOT NULL` **alone** (`quote.js:288-294` and `:325-329` before this change). The moment a sale persisted a `discount_bps`, **every buyer during the sale would have read as "discounted recently" for seven days after the sale ended** — silently denied a genuine shortfall discount as a punishment for having bought during a promotion. Both predicates are now scoped `AND discount_reason = ${SHORTFALL_REASON_SERVER}`, and a test counts **two** of each so a future edit cannot drop one.

### ⛔ A sale is NOT rate-limited, so it does not ride the serializable transaction

The `sql.transaction([...], { isolationLevel: 'Serializable' })` path is the **shortfall's** once-per-window authority. Routing a sale through it would discount **the first pack of the sale and nothing after it**, which is not a sale. `gateOnWindow` is therefore true only for a shortfall, and the ungated INSERT now persists `built.discountBps` / `built.discountReason` instead of a hardcoded `NULL, NULL`.

**And the fallback is the SALE, not full price.** A shortfall that is already in-window, or that loses the concurrent race, falls back to `resolveDiscount(sale.bps, null, ...)` — charging full price during a 30% sale *because* the player also asked for a shortfall discount would punish the exact moment the hint exists to soften.

---

## 3. Which rails the sale reaches

| rail | in scope | why |
|---|---|---|
| **SKR / Solana** (`api/purchases/quote.js`) | ✅ | server-priced; `buildQuoteBody` already took a `discountBps` |
| **Pi** (`api/pi/quote.js`) | ✅ | server-priced; `buildPiQuoteBody` gained the same parameter, and the sale is applied to the **USD before the Pi conversion** so the two rails cannot drift on the same sale |
| **Google Play** | ⛔ **EXCLUDED** | a Play purchase is priced by its in-app product in **Play Console** and charged by Google. The server issues **no amount** for it — `api/purchases/google-play-verify.js` and `api/_lib/google-play-purchases.js` contain no `buildQuoteBody`, no `usdAnchor` and no `amount_base_units` (proven by absence in the test). Running the deal on Play is a **Play Console action**, not a knob. |
| **USDC / SOL** | n/a | **no such purchase rail exists.** `purchase-catalog.js` quotes `currency: 'SKR'` only; the Pi rail quotes `PI`. Recorded so a future seat does not go looking. |

The canaries are **never** on sale: their amount is a protocol constant the verifier checks by exact equality, so a discount on one would refuse the very proof-of-rail it exists to be. `wirePinned` states `saleBps: null` explicitly rather than omitting it.

---

## 4. The public LIST branch (WO-1190) carries the sale

`wireQuote` gained `saleBps` / `saleLabel` / `saleEndsAt`, priced by **the same `buildQuoteBody` the till uses** — a shelf that applied the percentage itself would be a second price calculation, and the two would disagree the first time `ceil()`-to-a-whole-SKR landed differently. The LIST envelope also carries `saleBps` + `saleEndsAt` so a shelf can print **one** banner.

⚠ The **per-wallet shortfall** discount is deliberately **not** applied on LIST: it is per-wallet and the LIST is public and unauthenticated, so there is no wallet to judge. The shelf shows the sale; the till adds the shortfall if this player is owed one. A `saleBps` is null (never `0`) on a row that is not on sale.

### ⭐ The client half already exists — in a PARALLEL lane, uncommitted

Read at source 2026-09-16: `Assets/_Modules/Wallet/PurchaseQuoteService.cs` **already parses `saleBps` / `saleLabel` / `saleEndsAt`** and derives a ribbon + countdown (the `SaleBadgeText` and `SaleEndsAtUtc` members — cited **by symbol, not by line**, because that file is being edited by that lane right now and any line number here would drift before you read it), citing *"WO-1800 client / WO-1799 server"* and the owner's *"put big sales signs with x% off"*. `StorePackCard.cs` is modified in the same working tree. **Those are another lane's uncommitted changes and this lane did not touch them.**

**So the field names are a two-lane contract, and a typo in one is SILENT** — Newtonsoft leaves an unmatched `JsonProperty` null, so a misspelling ships as *"the sale never appeared"* with no error anywhere. A test pins all three names in **both** files.

### ⚠ What the OWNER'S CURRENTLY INSTALLED APK does with all of this

* **The confirm line SHOWS the sale with no new build.** `PackStore.cs` renders `quote.DiscountLabel` and `quote.UsdSavingLabel` in the `AwaitingApproval` commerce state, which the sale populates → *"… 30% off applied. was $2.99 - save $0.90"*.
* **The shelf CARD does not.** `StorePriceMajor` / `StorePriceMinor` (both in `PackStore.cs`) draw a card from **authored client data** (`pack.AmountLabel` / `pack.UsdReference` / `pack.UsdApprox`) or from the Pi/Play provider. The only thing the LIST feeds today is `IsSellable` / `SellableReasonFor`. **A struck-through shelf anchor and the ribbon need the WO-1800 client lane and a new APK.**
* On **Pi**, the headline number IS the server's Pi figure, so a sale shows up there today as a **lower Pi amount with the full-price USD anchor under it**.

---

## 5. The verify / fulfil path needs no change, and that is proven

`/verify` resolves the contract from the persisted row (`verify.js:288-305` → `contractFromQuoteRow`) and compares the chain's `tokenAmount.amount` to `contract.amountBaseUnits` (`verify.js:88`). The `discount_bps` / `discount_reason` columns are **read back but never used to re-derive a price** — they are audit fields. The Pi twin does the same: `api/pi/approve.js` refuses unless the payment's base units equal **the persisted row's** (`approve.js:12-13`, `:82`, `:148`).

Test `the VERIFIER accepts the DISCOUNTED amount and REFUSES the full-price anchor` drives it end to end: a 3000-bps quote body → a row → `contractFromQuoteRow` → a fake finalized transfer of `210000000000` → **`verified`**; the full-price `299000000000` → **`transfer_contract_mismatch`** (overpaying is not a lesser evil).

**`fulfill.js` and `reconcile.js` are clean too, proven by absence.** Neither names `usd_anchor`, `usd_rate`, `USD_ANCHORS`, `buildQuoteBody` or `usdAnchor(` — `fulfill.js` carries **no amount at all**, and `reconcile.js` reports `expected_lamports` off the entitlement row, which `/verify` wrote from the quote's own contract and is therefore **already the discounted figure**. ⛔ The failure this forbids: a settle path that recomputed from the anchor would compute **full price against a payment already made at the sale price** and refuse a purchase whose money has moved. Pinned by a test.

---

## 6. Files touched

| file | change |
|---|---|
| `api/_lib/store-sale.js` | **NEW.** the knob keys, the clamp, the derived label, `resolveSale`, `resolveDiscount`, `readStoreSale` |
| `api/_lib/tunables.js` | +2 allowlist rows with `serverOnly: true`, **appended after** the `TUNABLE_KEYS` literal behind a throwing guard, so the Unity key-domain oracle stays green |
| `api/_lib/tunable-manifest.js` | `mismatches()` honours the marker on the allowlist spec |
| `api/client-tunables.js` | withholds every `serverOnly` key from the public payload |
| `api/_lib/purchase-catalog.js` | `buildQuoteBody` takes a `discountReason`; new `discountLabelFor(bps, reason)`; the body carries `discountReason` |
| `api/purchases/quote.js` | reads the sale once; LIST + QUOTE apply it; `resolveDiscount`; both shortfall predicates scoped by reason; the ungated INSERT persists the discount; the audit row reports the true reason |
| `api/_lib/pi-payments.js` | `buildPiQuoteBody(sku, rate, discountBps, discountReason)`, sale applied to USD before conversion |
| `api/pi/quote.js` | reads the sale, persists it, returns the four display fields |
| `test/store-sale.test.js` | **NEW**, 25 cases |
| `test/purchases.quote.test.js` | the `deepEqual` shape gains `discountReason` |
| `test/tunables-manifest.test.js` | the key-domain filter honours the spec-carried marker |
| `docs/PROD022_TUNABLE_FLAGS.md` | the `store.*` family, and why the six-place rule does **not** apply to a server-only key |

**Not touched:** anything under `Assets/`, `CLI_LANES_WO_NUMBERS.md`, `api/schema.sql` (no migration — `discount_bps` / `discount_reason` already exist), `api/purchases/verify.js`, `fulfill.js`, `reconcile.js`, `api/pi/approve.js`.

---

## 7. Acceptance criteria

1. ✅ `node --test test/*.test.js` — **761 tests, 759 pass, 1 fail, 1 todo.** This lane's own suites are **25/25** (`store-sale`), **27/27** (`tunables-manifest`) and green on `purchases.verify`. ⚠ **Neither of the two `✖` belongs to this lane:** the `todo` is the pre-existing heartbound-streak case (WO-1693 finding 2), and the `fail` is `no impulse rung is strictly dominated…` in `purchases.quote.test.js`, caused by **another lane's uncommitted `packs.json` edit** — `builders-hour` now grants 700 wood + 400 iron + 300 stone + a builder at $1.99, strictly dominating `impulse-iron-small` (400 iron at $1.99). That is WO-1798's to resolve; this lane edited no pack data.
2. ⬜ **Deploy, then a curl of the PRODUCTION quote endpoint showing `discountBps: 3000` and `discountReason`/reason `sale`.** Needs the owner's deploy — see §8.
3. ⬜ **A device screenshot of the confirm line** reading the sale. Owner/PO, felt-verified (CLAUDE.md §13).
4. ✅ The sale percentage is absent from `GET /api/client-tunables`.
5. ✅ No price lever on the Command Center; the money boundary test still green.

---

## 8. Operator recipe — see the RESULT

The exact commands, what the shelf and the confirm line will read, the deploy that must happen first and the undo are in `WORK_ORDER_1799_storewide_sale_knob_30_percent.RESULT.md` §Operator recipe.

---

## 9. Unproven by this lane, stated as unproven (CLAUDE.md §11B.A)

* **Nothing here has run against production.** No deploy, no curl, no database write — all three are outside this lane's scope. The node suite is the only evidence.
* **No device screenshot exists.** The confirm-line claim is read off `PackStore.cs:4035-4041` at source; that it *renders legibly on the Seeker at 30% off* is unproven until the owner looks.
* **The live `client_tunables` table was not read.** That `store.saleBps` is absent there today is an inference from the rows being new, not a measurement.
* **The WO-1800 client lane's code was read, not run.** Whether its ribbon renders correctly is that lane's to prove.
* **`readStoreSale` adds one memoised table read to the quote path** (5 s in-lambda memo, `tunables.MEMO_TTL_MS`). Its latency cost on a cold lambda was **not measured**.
