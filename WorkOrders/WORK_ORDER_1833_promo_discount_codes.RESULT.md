# WORK ORDER 1833 — RESULT

**Status:** IMPLEMENTED
**Date:** 2026-09-17
**Silo held:** `api/**` + `test/**` only. No `.cs`, no `.unity`, no `*.generated.json`, no git, no
Unity, no deploy, no secret read or printed.

---

## 1. TEST COUNTS — BEFORE AND AFTER, MEASURED THIS SESSION

`node --test test/*.test.js`, both runs captured to file and read back:

| | tests | pass | fail | todo |
|---|---|---|---|---|
| **BEFORE** (baseline, before any edit) | 771 | 769 | 1 | 1 |
| **AFTER** | 793 | 791 | 1 | 1 |

**+22 tests, +22 passes, ZERO new failures.** The single failure is identical in both runs and is
**pre-existing and outside this silo**: `test/heartbound-suite.test.js` — *"⛔ no client-readable file
under Assets/ carries a Heartbound tier NAME"* (it names `Assets/Editor/WallTools/RaidPostAudit.cs`).
The `todo` is `heartbound-state`'s WO-1693 marker. Neither was touched.

⚠ Note on reading that run: the `node:test` summary is the `ℹ tests / pass / fail` block, not a `# `
TAP block — a `grep '^# '` returns nothing and must not be read as "no summary, therefore a crash".

Three **pre-existing pinned-shape** assertions had to be updated, all deliberately and all named here
rather than silently relaxed:
* `test/command-center.test.js:482` — the promo-draft `deepEqual`. That case exists to prove the draft
  lands on **exactly** the columns `promo_codes` holds, so a new column must be **named**, not excused.
* `test/command-center.test.js:518` — the created_by cascade case. The cascade is now three shapes, so
  a database missing `created_by` raises 42703 **twice** before landing on the oldest shape. It still
  lands there; the case now queues the second throw and asserts the middle shape's INSERT in between.
* `test/store-sale.test.js` — the pinned source line for the shortfall-lost-the-race fallback. The
  **law** it pins (a lost race must never land the player at full price) is unchanged; the function it
  routes through is renamed, and the assertion now additionally pins that the **promo argument is
  present in that fallback** — a player holding a 30% promo must not pay full freight for losing a race.

## 2. FILES

**New**
* `api/_lib/promo-discount.js` — the one reader/gate. Pure core (`readDiscountSpec`,
  `resolveRedeemWindow`, `pickActiveDiscount`) + `readActivePromoDiscount(sql, playerId)`.
* `api/migrations/20260917_0028_promo_discount_codes.sql` — **the applyable copy.**
* `test/promo.discount-codes.test.js` — 8 behavioural cases driving the real redeem handler.

**Changed**
* `api/schema.sql` — table 3: the three columns as an **ALTER-only note** (see §4).
* `api/_lib/store-sale.js` — `PROMO_REASON`, `DISCOUNT_PRECEDENCE`, `resolveStorefrontDiscount`,
  `resolveEffectiveDiscount`, `clampDiscountBps` alias.
* `api/promo/redeem.js` — step-1 SELECT (two-shape cascade), step 2b window gate, zero-reward backstop,
  `discount` on the response, a `promo_discount_redeem` audit event.
* `api/purchases/quote.js` — promo read beside `readStoreSale` (**quote mode only**), precedence at both
  quote-mode call sites, `discountSource` + `promoEndsAt` on the wire, `promoBps`/`promoCode` on the
  audit event. The public LIST is deliberately unchanged.
* `api/_lib/ops.js` + `api/admin/ops.js` — authoring a discount code; the 42703 cascade becomes three
  shapes and `attribution_on_row` is re-keyed so the middle shape does not report a false alarm.
* `test/store-sale.test.js` (+8 cases), `test/purchases.quote.test.js` (+3 cases),
  `test/command-center.test.js` (+2 cases, 2 shapes updated).

## 3. THE LOGIC, BY FILE:LINE (all re-read at source after the edits)

**Redeem.** `api/promo/redeem.js:382` — the step-1 SELECT names `discount_bps`, `discount_starts_at`,
`discount_ends_at`, inside a **42703 two-shape cascade**. ⛔ That cascade is not decoration: migration
0028 is applied by a human and **a deploy can beat them to it**, and naming an absent column on *this*
statement would 500 **every redemption of every code** — an optional feature becoming a total outage of
the promo rail. `:496-520` is the window gate (`resolveRedeemWindow` → `EXPIRED`, before anything is
written, so the code is **not consumed**). `:600` adds `discountSpec == null` to the zero-reward
backstop — without it the owner's discount-only code is refused `REWARD_UNAVAILABLE` forever. `:879`
audits `promo_discount_redeem`; `:899` returns `discount: { bps, label, endsAt }`. **Nothing was added
after the atomic claim.**

**Quote.** `api/purchases/quote.js:348` reads the active discount beside `readStoreSale`, **QUOTE MODE
ONLY** (`sku ? … : null`); `:447` and `:501` apply it through `resolveEffectiveDiscount`; `:171-172` put
`discountSource` + `promoEndsAt` on the wire and leave `saleBps`/`saleLabel`/`saleEndsAt` **null on a
promo quote** — a personal discount must not light a storefront badge, and the sale's countdown is the
wrong clock for a personal window.

⛔ **THE PUBLIC LIST DOES *NOT* APPLY IT — corrected before handback.** The first pass priced the shelf
from the promo as well. That contradicts the rule the LIST loop already states in its own words about
the per-wallet shortfall discount (*"the LIST is public and unauthenticated, so there is no wallet to
judge"*) and, worse, would let any caller POST any wallet into an unauthenticated body and read back
whether that wallet holds a live promo and at what bps — the same enumeration `redeem.js` closes
deliberately by answering `INVALID_CODE` for a code bound to someone else. It also bought nothing felt:
the shipped APK ignores per-row price fields on the card. **The shelf shows the SALE; the till applies
the promo to a PROVEN wallet.** No `promoBps` on the LIST envelope either.

**Authoring cascade.** `api/_lib/ops.js:527-580` — THREE shapes, not two (see the ticket §4.6). The
middle shape exists so a database carrying migration 0021 but not 0028 does not silently strip
`created_by` from every ordinary grant code authored in that window.

**Precedence.** `api/_lib/store-sale.js:173` `DISCOUNT_PRECEDENCE = 'promo-overrides-sale'`, `:175`
`resolveStorefrontDiscount` — a **two-branch precedence with no `max()` in it** — and `:200`
`resolveEffectiveDiscount`, which composes it with the untouched WO-1799 shortfall law and re-labels the
reason `'promo'` so the persisted audit row never calls a personal discount a storewide sale.

## 4. THINGS FOUND AT SOURCE THAT CHANGED THE PLAN

1. **The columns must NOT go in the `CREATE TABLE` body.** `tools/schema-parity.mjs:53` parses only
   `CREATE TABLE` bodies, so a declared-but-unapplied column reads as **drift and blocks every
   deploy**. `api/schema.sql` already says this under `created_by`. They went in as an ALTER-only note,
   byte-identical to the migration's statements, and a regression pins that the two agree.
2. **`purchase_quotes.discount_reason` has no `CHECK`** (`api/schema.sql:1395`) and `/verify` prices
   from the row's `amount_base_units` — so `'promo'` needs **no migration and no verifier change**.
   Proven, not assumed: a promo-priced row verifies and an overpayment is still refused.
3. **`validatePromoDraft` would have refused the owner's exact ask.** `REWARD_EMPTY` fires on a code
   with no pack and zero currency, which is precisely SPOTLIGHT30's shape.
4. **The CST trap is real and would have shipped silently** (see §6.2).

## 5. THE TWO DESIGN DECISIONS, RESTATED

* **Second discount code while one is active → BOTH STAND, THE LARGER bps IS USED.** With the derived
  design there is nothing to replace; each redemption is its own ledger row, each code owns its own
  fixed window, and the reader's `ORDER BY discount_bps DESC LIMIT 1` picks the better one. When the
  better one's window closes the other takes over with **no write and no sweep**.
* **PROMO OVERRIDES THE SALE — not `max()`, not additive.** A 3000 bps promo beats a 5000 bps sale, by
  ruling. ⚠ The per-wallet **shortfall apology** is still `max()` against the winner, because the owner
  ruled on promo vs the **global sale** and did not re-rule the shortfall — a player is never charged
  *more* for holding an entitlement. One line to change if she wants otherwise.
* **No `player_discounts` table and no cleanup path** — `discount_ends_at > NOW()` at read time.

## 6. FOR THE OWNER

1. **`node tools/run-migrations.mjs` must run before SPOTLIGHT30 can exist.** Not run here: no live DB
   access in this silo, by the rules. Until it runs, redeem degrades to plain-grant behaviour (proven
   by a test that raises the real 42703) and authoring a discount code **refuses loudly** with
   `DISCOUNT_COLUMNS_MISSING` rather than quietly creating a code that discounts nothing.
2. ⛔ **AUTHOR THE WINDOW WITH AN EXPLICIT UTC OFFSET** — `2026-09-18T12:00:00-05:00`. `Date.parse` of
   a bare `2026-09-18 12:00` is read as **UTC** on Vercel, landing a public campaign **5–6 hours out**
   with a perfectly valid timestamp stored and nothing downstream able to notice. The new validator
   **refuses** a bare wall clock (`WINDOW_NEEDS_OFFSET`). September is CDT = `-05:00`; CST = `-06:00`.
3. **A promo-creation path already exists** — Command Center → `POST /api/admin/ops`
   `action: "promo.create"` → `_lib/ops.validatePromoDraft` + `createPromo`, now accepting
   `discountBps` / `discountStartsAt` / `discountEndsAt`. It needs `ADMIN_DASH_KEY` (not read, not
   printed, not handled here). The response echoes the window **in words**, as UTC, so a typo is
   visible at authoring time instead of at noon on the day.
4. **THE SHIPPED APK CANNOT DRAW "PROMO ACTIVE".** `PromoCodeService` renders the promo `message`
   string; `PackStore` draws card prices from authored client data. So the discount is **felt at the
   confirm line** (which reads `discountLabel` + `usdSaving`) and the only place the player is *told*
   about the window is the operator's `message`. Suggested: `30% off everything until 11:59pm CST Thu
   — already applied at checkout.` A shelf badge needs a new build.
5. ⛔ **A GUEST CAN REDEEM SPOTLIGHT30 AND CAN NEVER SPEND IT — THIS NEEDS A RULING.** `redeem.js`
   admits the guest rail (WO-1440 reversal), but `quote.js` QUOTE mode is behind `authenticateGranting`
   — **wallet only**. So a guest redemption burns a `max_redemptions` slot, a `per_player_limit` slot and
   an IP-budget unit for a discount that player cannot use, and I found **no guest→wallet migration of
   `promo_redemptions` rows**, so the discount is also lost if they later link a wallet. Not implemented,
   because it is a ruling, not a bug. The cheap option if she wants it closed: refuse a **discount-only**
   code on the guest rail, unburned (`auth.unproven && discountSpec && crystals<=0 && coins<=0` →
   `REWARD_UNAVAILABLE`), placed **before** the step-5b IP budget so no unit is spent. A code that carries
   a grant **and** a discount should still be honoured — the guest gets the grant.
6. **KNOWN GAP, NOT FIXED, DELIBERATELY SCOPED OUT:** `api/pi/quote.js` has its own sale read and does
   **not** apply the personal promo, so a player buying on the **Pi rail** would not get their promo
   price. It is a separate currency rail and was outside the briefed files. One ruling away from being
   a ticket.
7. **What could NOT be confirmed from here:** anything needing the live database or a deploy — that the
   migration applies cleanly against production, that `promo_codes` on live has the shape
   `information_schema` will report afterwards, and the end-to-end redeem→quote flow against real rows.
   Every claim above is from a file opened this session or a test run captured this session.
