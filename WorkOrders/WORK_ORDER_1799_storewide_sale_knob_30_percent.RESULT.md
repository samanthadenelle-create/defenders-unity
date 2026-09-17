# WORK ORDER 1799 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** `api/` JavaScript + `test/` + `docs/`. **Zero files under `Assets/` touched** — the owner's 30% deal needs **no new APK** to be felt at the confirm line. No deploy, no commit, no database write, no `CLI_LANES_WO_NUMBERS.md` edit.
**Date:** 2026-09-16.

---

## The answer to the owner's question

**Yes — and after deploy it is one command, with no build and no code change to stop it.**

```powershell
node tools/client-tunables.mjs set store.saleBps 3000
```

That is 30% off **every quotable SKU on every server-priced rail** (SKR + Pi), priced by the server, persisted on the quote the verifier checks, and visible on the **confirm line of the APK she already has installed.**

---

## Operator recipe (for the owner)

### Step 0 — the deploy has to happen FIRST

⚠ `vercel.json` sets `"git": { "deploymentEnabled": false }` — **pushing does not deploy.** Until the deploy runs, `store.saleBps` is not a key the server knows, so the CLI will refuse it with `UNKNOWN_TUNABLE_KEY`.

```powershell
npx vercel deploy --target production --skip-domain --yes
```

Then the **lead** verifies the deployment and promotes it. Nothing below works before that.

⚠ **THE DEPLOY IS NOT ISOLATED TO THIS LANE.** It uploads the **working tree**, which today also carries another lane's seven `town.*`/`wave.*` tunable keys and the regenerated `tunable-manifest.generated.json`. Those are additive server-side rows and harmless on their own, but the deploy is not "just the sale" — the lead should know what is going up.

### Step 1 — start the sale

```powershell
# 30% off, storewide, effective on the next quote (no build, no restart)
node tools/client-tunables.mjs set store.saleBps 3000
```

Expect `TUNABLES_SET_OK key=store.saleBps value=3000`. **Judge by that marker, not the exit code.**

### Step 2 (optional) — make it stop by itself

The rail stores **ints only**, so the end time is epoch **MINUTES** (epoch *seconds* is ten digits and the validator caps at nine — it would be refused and look like a mistyped key):

```powershell
# "stop 48 hours from now", computed rather than typed
$mins = [int][math]::Floor(([DateTimeOffset]::UtcNow.AddHours(48)).ToUnixTimeSeconds() / 60)
node tools/client-tunables.mjs set store.saleEndsAtEpochMin $mins
```

At that minute the sale ends on its own. No row needs clearing for it to stop on time.

### Step 3 — see what is set

```powershell
node tools/client-tunables.mjs list
```

⚠ A read failure is **not** "nothing is set" — the tool says so in those words.

### Step 4 — the undo

```powershell
node tools/client-tunables.mjs clear store.saleBps
node tools/client-tunables.mjs clear store.saleEndsAtEpochMin
```

⭐ **`clear` is the way back, not `set … 0`.** `clear` removes the override entirely. (For this particular knob `0` also means "no sale", so both work — but `clear` is the habit the rest of this rail depends on, where `0` is a real value.)

### There is deliberately no label to type

There is **no `store.saleLabel` row**: the rail stores no strings, so the label is derived server-side — `3000` → **`"30% off"`**. Nothing to typo, and the two rails cannot word the same sale differently.

---

## What she will actually SEE

| surface | on the APK installed today | needs the WO-1800 client lane + a new APK |
|---|---|---|
| **Confirm line** (the last screen before paying) | ✅ **"… 30% off applied. was $2.99 - save $0.90"** and a **lower SKR figure**. `PackStore.cs` already renders `quote.DiscountLabel` + `quote.UsdSavingLabel` in its `AwaitingApproval` state. | — |
| **Shelf card** (before the tap) | ❌ unchanged. `StorePriceMajor`/`StorePriceMinor` in `PackStore.cs` draw the card from **authored** client data; the LIST only feeds `IsSellable`. | ✅ ribbon + struck-through anchor + countdown — `PurchaseQuoteService.cs` already parses `saleBps`/`saleLabel`/`saleEndsAt` and derives `SaleBadgeText` / `SaleEndsAtUtc` in that lane's **uncommitted** work (cited by symbol: that file is being edited right now, so a line number would drift) |
| **Pi / WebGL card** | ✅ partially — the headline **is** the server's Pi figure, so it drops; the USD anchor under it still reads full price | ✅ the badge |
| **Google Play** | ❌ **cannot be discounted server-side at all** — see below | — |

### ⛔ Google Play is EXCLUDED, and it is not fixable in `api/`

A Play purchase is priced by its in-app product in **Play Console** and charged by Google. The server issues **no amount** for that rail (`api/purchases/google-play-verify.js` and `api/_lib/google-play-purchases.js` carry no `buildQuoteBody`, no `usdAnchor`, no `amount_base_units` — proven by absence in the test suite). To run the 30% deal on Play, the prices must be changed **in Play Console**, or a Play sale published there. **There is also no USDC or SOL purchase rail** — nothing else in this repo issues a quote.

---

## The rules this landed with

* **MAX, never additive.** A player owed the once-per-7-days **shortfall** discount during a sale gets the **better of the two**, not 50% off. A tie goes to the sale, which leaves their shortfall window **unspent**.
* ⛔ **A sale no longer poisons the shortfall window.** Both window predicates keyed on `discount_bps IS NOT NULL` alone; a sale would have marked **every buyer** "discounted recently" for seven days *after* the sale ended — silently punishing them for buying during a promotion. Both are now scoped by `discount_reason`, and a test counts two of each.
* ⛔ **The sale is not rate-limited.** It bypasses the serializable once-per-window transaction, which is the shortfall's authority; routing a sale through it would have discounted **the first pack of the sale and nothing after it**.
* **The verifier needed no change, and that is proven** — `/verify` compares the chain to the persisted quote row's own `amount_base_units`, so the discounted amount is accepted and the full-price anchor is refused (`transfer_contract_mismatch`).
* **The percentage never reaches a phone.** `GET /api/client-tunables` withholds every `serverOnly` key, with the exclusion set **derived** from the allowlist.
* **No Command Center card.** A sale is a price, and that page's own notice says prices are never editable there. The `serverOnly` marker was read off the **allowlist spec** instead of a `PRESENTATION` row — a **deliberate deviation from the brief's pointer**, so the money boundary stays intact. If the owner wants a card, that is her ruling.
* **No schema migration.** `purchase_quotes.discount_bps` / `.discount_reason` already exist (`api/schema.sql:1372-1376`).
* **`fulfill.js` and `reconcile.js` needed no change either, proven by absence** — neither re-derives an amount from `usd_anchor`/`usd_rate`; `reconcile` reports the `expected_lamports` `/verify` wrote from the quote's contract, which is already the discounted figure.

### ⭐ ONE THING THE LEAD SHOULD DECIDE — a structure this lane chose to keep a Unity gate green

`Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` case 4 reads `api/_lib/tunables.js` from disk, regexes out the `TUNABLE_KEYS = [ … ];` **literal**, and asserts its keys are exactly the pinned `ExpectedDefaults` — anything extra is an `UNPINNED key`. A `serverOnly` key has no build entry **by design**, so writing the two sale rows inside that literal would have **redded `REGRESSION_OK` at HEAD**, and this lane cannot touch `Assets/`.

So the two rows are **appended after the literal** into the same runtime array, behind a loop that **throws at module load** if a row lacks `serverOnly: true` (so the block cannot become a back door for a client knob). **Measured:** `ExpectedDefaults` = **71**; the literal region now yields **71** with **zero** `store.*`; the runtime allowlist = **73**. Two tests re-implement the C# regex, so moving the rows back inside reds in two seconds rather than at a batchmode gate.

**⚠ The tidier fix is a one-line `serverOnly` skip in that C# oracle, and it is the lead's call.** With it, these rows move back inside the literal and the append block deletes. Stated here rather than left for the gate to discover.

---

## Test evidence — a FRESH run, 2026-09-16

```
node --test test/store-sale.test.js          # this lane's new suite
  ℹ tests 25   ℹ pass 25   ℹ fail 0

node --test test/tunables-manifest.test.js
  ℹ tests 27   ℹ pass 27   ℹ fail 0

node --test test/*.test.js                   # the whole suite
  ℹ tests 761  ℹ pass 759  ℹ fail 1  ℹ todo 1  ℹ cancelled 0
```

⚠ **NEITHER of the two `✖` in the full run belongs to this lane.**

1. `the streak reaches heartbound_state # WO-1693 finding 2 — recordVerifiedStake names no streak column` — **pre-existing and `todo`-marked** (which is why it is not counted in `fail`). Heartbound area; this lane touched nothing it reads.
2. `no impulse rung is strictly dominated by another purchasable pack at the same USD anchor` (`purchases.quote.test.js`) — caused by **another lane's uncommitted `packs.json` edit** on the shared tree. Measured at source: `builders-hour` now grants **700 wood + 400 iron + 300 stone + a temporary builder at $1.99**, which strictly dominates `impulse-iron-small` (**400 iron at $1.99**). That is **WO-1798's** ticket ("make the packs more substantial"); this lane edited no pack data and cannot fix it without ruling on pack contents.

*(An earlier, pre-restructure run of this lane's work read 758 / 757 / fail 0 — the packs.json edit landed on the shared tree between that run and this one.)*

The 25 new cases prove, in order: the resting state is no sale · 3000 reads as "30% off" · the clamp holds at both ends and junk never becomes a sale · a past end time ends it · epoch seconds are refused (which is *why* the key names minutes) · MAX-not-additive in every combination · both SQL predicates are reason-scoped · the sale is not window-gated and a losing shortfall falls back to the sale · the priced quote and its label · **the verifier accepts the discounted amount and refuses the anchor** · **`fulfill`/`reconcile` never re-derive a price** · no free/negative quote · canaries never on sale · the wire carries sale fields only for a sale · the field names match the client letter-for-letter · the confirm line renders it today · both server-priced rails read one helper · **Play excluded, proven by absence** · no USDC/SOL rail · the public endpoint withholds the key · the exclusion set is derived · **the sale keys stay outside the literal the Unity oracle pins** · **the append block throws on an unmarked row** · no Command Center card and the boundary holds · the operator CLI accepts the keys and refuses a typo.

---

## ⚠ Read the board badge with care

`BOARD.html` renders WO-1799 as **`Done`** because a `.RESULT.md` exists beside it. **It is not gated and not felt-verified** — the `**Status:**` line says `IMPLEMENTED, NOT YET GATED`, which is the label the brief mandated and the same phrasing WO-1723 uses. Read the status text, not the badge.

---

## Remaining acceptance, and who owns it

| # | criterion | owner |
|---|---|---|
| 2 | a curl of the **production** quote endpoint showing `discountBps: 3000` + reason `sale` | lead, **after** the owner's deploy + promote |
| 3 | a **device screenshot** of the confirm line | owner / PO, felt-verified (CLAUDE.md §13) |

---

## Unproven, stated as unproven (CLAUDE.md §11B.A)

* **Nothing ran against production.** No deploy, no curl, no DB write — all outside this lane. The node suite is the only evidence in this document.
* **No device screenshot exists.** The confirm-line wording is read off `PackStore.cs:4035-4041` at source; that it reads well on the Seeker at 30% off is unproven until the owner looks.
* **The live `client_tunables` table was not read.** That the two rows are absent today is inferred from their being new, not measured.
* **The WO-1800 client lane's code was read, not run** — its ribbon rendering is that lane's to prove. Its files are **uncommitted in this working tree** and were not touched here.
* **The added table read was not benchmarked.** `readStoreSale` adds one memoised query (5 s in-lambda memo) to the quote path; its cold-lambda cost was not measured.
* **No gate was run.** This lane is JavaScript only, so `COMPILE_GATE_OK` / `REGRESSION_OK` do not apply to its own files — but neither has been run at HEAD over the combined tree, which holds several other lanes' in-flight changes (`api/_lib/tunables.js` gained seven WO-1773 keys from another lane while this work was in progress, and `packs.json` changed under WO-1798).
* **The Unity `[tunable-defaults]` key-domain case was reasoned to be green from its own source, NOT run.** `ExpectedDefaults` = 71, the literal region yields 71 with no `store.*`, and a JavaScript test re-implements the same regex — but `REGRESSION_OK` itself has not been produced on a fresh log by this lane. **That is the one thing worth the lead confirming at the gate.**
