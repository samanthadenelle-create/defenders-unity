# WORK ORDER 1833 — Promo codes that carry a DISCOUNT, not just a grant (SPOTLIGHT30)

**Status:** IMPLEMENTED

**Silo:** `api/**` + `test/**` only (Node, Vercel serverless). No Unity, no client `.cs`, no deploy.
**Lane:** Monetization/Backend (§9 — fully isolated from every Unity lane).
**Opened:** 2026-09-17

---

## 1. THE OWNER'S ASK, VERBATIM, AND THE TWO CORRECTIONS THAT FOLLOWED IT

1. *"if i set up promocode spotlight30 as a promo code can you make it make everything 30% off?"*
2. Duration: *"48 hours"* — then **CORRECTED** to *"flat 12:00 - 11:59 CST"*.
3. Stacking: *"personal promo should override global i would think not stack"*.

### ⛔ CORRECTION 2 IS A REAL BEHAVIOUR DIFFERENCE, NOT A WORDING CHANGE.
The first reading was a **rolling per-player window** (`expires_at = redeemed_at + 48h`). The owner
means a **FIXED CALENDAR WINDOW shared by every redeemer**: one promotional event with one real end
time. So the window is **authored ONCE on the `promo_codes` row as two absolute `TIMESTAMPTZ`
values** and is **never** computed from a redemption time. A player who redeems late gets **whatever
is left of the shared window**, not a fresh 48 hours. Recorded here because a seat reading only the
first sentence would build the wrong thing and it would look correct in every test it wrote.

### ⛔ CORRECTION 3 RETIRES THE LEAD'S EARLIER `max()` DEFAULT.
The lead's stated default was *best-of* (`max(promoBps, saleBps)`). **That is superseded.** The rule
is **OVERRIDE / PRECEDENCE**: an active personal promo **replaces** the storewide sale outright.
Concretely and intentionally: with `store.saleBps = 5000` running and SPOTLIGHT30 also live, a player
who redeemed SPOTLIGHT30 is charged **30% off, not 50% off**. That is the ruling, not a bug.

---

## 2. WHAT EXISTS TODAY (read at source 2026-09-17, line numbers verified this session)

| Fact | Authority |
|---|---|
| `promo_codes` has NO discount concept at all — only `reward_crystals`, `reward_coins`, `reward_pack_sku`, `tier1/tier2_*`, `redemption_count` | `api/schema.sql:431-449` |
| Redemption is ONE atomic claim-then-grant statement per reward shape (`WITH claimed AS (UPDATE promo_codes … RETURNING) , recorded AS (INSERT INTO promo_redemptions …)`) | `api/promo/redeem.js:580-611`, `:635-660`, `:675-706`, `:726-745` |
| Why it MUST be one statement: two statements on the Neon HTTP driver are two transactions; a count-then-insert over-issued **50 grants against a cap of 20** | `api/promo/redeem.js:462-479` |
| A code must never burn for zero reward (the structural backstop) | `api/promo/redeem.js:509-527` |
| Expiry refusal → `EXPIRED` | `api/promo/redeem.js:447-450` |
| `UNIQUE (code, player_id)` — one redemption per code per player, plus an index on `player_id` | `api/schema.sql:551`, `:559-560` |
| The storewide sale knob, read through the one tunables reader, fail-to-no-sale | `api/_lib/store-sale.js:60-155` |
| The combination rule today: **max, never additive**, sale wins ties | `api/_lib/store-sale.js:113-139` (`resolveDiscount`) |
| Price is computed in ONE place for shelf and till | `api/_lib/purchase-catalog.js:465` (`buildQuoteBody`), called at `api/purchases/quote.js:332`, `:392`, `:442` |
| `purchase_quotes.discount_reason` is a bare `TEXT` — **no CHECK constraint**, so a new reason string needs no migration | `api/schema.sql:1395` |
| `/verify` reads `discount_bps` + `discount_reason` off the row and prices from the row, never recomputing | `api/purchases/verify.js:321` |
| An operator CAN author a promo code today: Command Center → `api/admin/ops.js` → `api/_lib/ops.js` `validatePromoDraft` / `createPromo` | `api/_lib/ops.js:294-342`, `:418-460` |
| `validatePromoDraft` throws `REWARD_EMPTY` on a code with no pack and zero currency — **a discount-only code is refused at authoring time until this ticket changes it** | `api/_lib/ops.js:~318` |
| `api/schema.sql` is a DESCRIPTION; only `api/migrations/*.sql` + `node tools/run-migrations.mjs` run against production | `api/schema.sql:93-109`, `api/migrations/20260907_0021_promo_codes_created_by.sql` |

---

## 3. DESIGN DECISIONS — STATED, WITH THE REASONING

### D1. NO `player_discounts` TABLE. The active discount is DERIVED from the ledger.
The brief offered a new table *or* a column set, "your call". **The call is neither: it is a JOIN.**

Because the window is FIXED (correction 2), a player's active discount is a pure function of two rows
that already exist — their `promo_redemptions` row and the `promo_codes` row it references:

```sql
SELECT pr.code, pc.discount_bps, pc.discount_ends_at
  FROM promo_redemptions pr
  JOIN promo_codes pc ON pc.code = pr.code
 WHERE pr.player_id = $1
   AND pc.discount_bps IS NOT NULL AND pc.discount_bps > 0
   AND pc.discount_ends_at IS NOT NULL AND pc.discount_ends_at > NOW()
   AND (pc.discount_starts_at IS NULL OR pc.discount_starts_at <= NOW())
 ORDER BY pc.discount_bps DESC
 LIMIT 1
```

A `player_discounts` table would be a **second copy of a fact the ledger already holds**, written by
a second statement, which is (a) the duplicated-state failure CLAUDE.md §2/§5/§8/§16 each record a
scar from, and (b) an extra write that the Neon HTTP driver makes **non-atomic** with the claim
(`redeem.js:470-479`) — so a crash between the two would grant the code and silently lose the
discount, with no un-burn. The derived read cannot drift, cannot be half-written, and needs no
cleanup job. It is served by the existing `idx_promo_redemptions_player` index (`schema.sql:559-560`).

**Consequence (answers brief item 4):** there is **no cleanup path and none is needed** —
`discount_ends_at > NOW()` is evaluated at read time, and an expired promo simply stops matching. No
row ever has to be deleted or swept.

### D2. A SECOND DISCOUNT CODE WHILE ONE IS ACTIVE → **both stand; the LARGER bps is used.**
Not "extend", not "replace", not "refuse". With D1 there is nothing to replace: each redemption is
its own ledger row and each code owns its own fixed window, so redeeming a second discount code is
already legal and already recorded. The reader's `ORDER BY pc.discount_bps DESC LIMIT 1` picks the
better one while both windows are live, and when the better one's window closes the other takes over
automatically. This is the simplest safe default because it adds **zero** new state and **zero** new
refusal path: no player can lose a discount they were given, and no code is consumed for nothing.
(Note this is a comparison **between two personal promos**, which is a different axis from D3's
promo-vs-sale precedence.)

### D3. PROMO **OVERRIDES** THE SALE. One named function, one line to flip.
`api/_lib/store-sale.js` gains:

```js
const DISCOUNT_PRECEDENCE = 'promo-overrides-sale';   // flip here to go additive/max
function resolveStorefrontDiscount(promoBps, saleBps) { … }
```

Its result then feeds the **existing** `resolveDiscount(…)` vs the shortfall discount, untouched.

⚠ **STATED EXPLICITLY SO IT IS NOT MISTAKEN FOR AN OVERSIGHT:** the owner ruled on promo vs the
**GLOBAL SALE**. She did **not** re-rule the per-wallet **shortfall apology** discount, whose
max-not-additive law predates this ticket (`store-sale.js:113-139`, WO-1799). So a shortfall that is
*larger* than the promo still wins, exactly as it already wins over a larger sale. A player is never
charged MORE because they hold a promo. If the owner wants the promo to beat the shortfall too, that
is a one-line change in the same function — surfaced, not decided here.

### D4. "NOT YET STARTED" AND "ALREADY ENDED" BOTH ANSWER `EXPIRED`.
Coordinator-directed reuse of the existing rejection at `redeem.js:447-450`, **not** a semantic choice
of mine: `EXPIRED` for a window that has not opened yet is a slightly odd word to a player. It is
taken because inventing a second error code would need a client change the shipped APK cannot
receive (`PromoCodeService.MapErrorKey` maps a fixed set; an unknown code lands on the calm
unknown-error line). Recorded so the next seat knows it was deliberate. The refusal does **NOT**
consume the code, so a player who tries early can redeem when the window opens.

### D5. NO DURATION IS HARDCODED ANYWHERE.
"12:00 – 11:59 CST" is 36h read one way and 48h read another. Two absolute `TIMESTAMPTZ` columns make
that **the operator's authored fact**, not an arithmetic constant in code. Nothing in `api/` ever adds
hours to anything.

### D6. A CODE MAY CARRY A GRANT **AND** A DISCOUNT, OR EITHER ALONE.
The columns are independent and additive. A discount **counts as a reward** for the zero-reward
backstop (`redeem.js:520`), so a discount-only code (`reward_crystals = 0, reward_coins = 0,
discount_bps = 3000`) redeems instead of being refused `REWARD_UNAVAILABLE` forever. SPOTLIGHT30 is
expected to be exactly that shape.

---

## 4. THE CHANGES

### 4.1 Schema — three additive nullable columns + one index

```sql
ALTER TABLE promo_codes ADD COLUMN IF NOT EXISTS discount_bps      INTEGER;
ALTER TABLE promo_codes ADD COLUMN IF NOT EXISTS discount_starts_at TIMESTAMPTZ;
ALTER TABLE promo_codes ADD COLUMN IF NOT EXISTS discount_ends_at   TIMESTAMPTZ;
```

* ⛔ **NOT** written into the `CREATE TABLE` body — **corrected during implementation.** `api/schema.sql`
  itself records why (read at source, the `created_by` note in table 3): **`tools/schema-parity.mjs`
  parses only the `CREATE TABLE` bodies in that file** (`tools/schema-parity.mjs:53`), so a column
  declared there but not yet applied reads as **DRIFT and BLOCKS EVERY DEPLOY** until a human runs the
  SQL. The three columns therefore go in as an **ALTER-only note** beneath table 3, exactly as
  `created_by` did. `CREATE TABLE IF NOT EXISTS` could not have added them to an existing table anyway
  (memory: `idempotent-ddl-hides-a-stale-table`).
* **The applyable copy is `api/migrations/20260917_0028_promo_discount_codes.sql`**, run with
  `node tools/run-migrations.mjs`. `api/schema.sql` is a description and does not run.
* `discount_bps` is deliberately **not** `NOT NULL DEFAULT 0`: a default stamps every historical code,
  and NULL reads correctly as "this code predates discounts".
* No `CHECK` on the bps value: the **clamp** lives on the read, once, in `store-sale.clampSaleBps`
  (`store-sale.js:68-91`, ceiling 7000 bps = 70% off), so a fat-thumbed `30000` cannot hand the store
  away even if it reaches the row. A CHECK would be a second, divergent opinion about the ceiling.

### 4.2 `api/_lib/promo-discount.js` (NEW) — the one reader, pure core + one query
* `resolveRedeemWindow(row, nowMs)` → `{ ok, error }` — pure; the `EXPIRED` gate of D4.
* `pickActiveDiscount(rows, nowMs)` → `{ bps, code, endsAtMs } | null` — pure; D2's precedence.
* `readActivePromoDiscount(sql, playerId, nowMs)` → same shape; **fails to NO DISCOUNT** on an
  unreadable table, matching `readStoreSale`'s fail-to-no-sale: a failure may only ever charge the
  ORDINARY price, never an invented one.

### 4.3 `api/_lib/store-sale.js` — `PROMO_REASON`, `DISCOUNT_PRECEDENCE`, `resolveStorefrontDiscount`
D3. `PROMO_REASON = 'promo'` persists in `purchase_quotes.discount_reason` (bare TEXT, no CHECK —
verified `schema.sql:1395`).

### 4.4 `api/promo/redeem.js`
* Step 1 SELECT (`:352-361`) gains the three columns. ⛔ **Forgetting to SELECT a column is exactly how
  the pack-sku burn shipped** (`:397-406`) — this is the same trap.
* New gate beside step 2's expiry: outside `[discount_starts_at, discount_ends_at]` → `EXPIRED`,
  unconsumed.
* The zero-reward backstop (`:520`) treats `discount_bps > 0` as a reward (D6).
* The response carries `discount: { bps, label, endsAt }` additively and the `message` is passed
  through unchanged — **the shipped APK renders `message` and nothing else** (see §6), so the operator's
  authored sentence is what the player actually reads.
* ⛔ **No second write.** Per D1 the redemption row IS the discount record; the existing atomic claim is
  untouched. Nothing is appended after it.

### 4.5 `api/purchases/quote.js`
* Read the player's active discount next to `readStoreSale`, **QUOTE MODE ONLY** (`quote.js:348`,
  `sku ? … : null`).
* ⛔ **CORRECTED DURING IMPLEMENTATION — THE LIST DOES *NOT* APPLY IT.** The first pass priced the
  public shelf from the promo as well; that is wrong for the reason the LIST loop already states in its
  own words about the per-wallet shortfall discount: *"the LIST is public and unauthenticated, so there
  is no wallet to judge."* A promo is personal too, and pricing a shelf from a **claimed** playerId
  would let any caller POST any wallet into an unauthenticated body and read back whether that wallet
  holds a live promo and at what bps — the same enumeration `redeem.js` closes deliberately by
  answering `INVALID_CODE` for a code bound to someone else. It also buys nothing felt: the shipped APK
  ignores these per-row price fields on the card. So: **the shelf shows the SALE; the till applies the
  promo to a PROVEN wallet** (by `:348` the request is past `authenticateGranting`). No `promoBps` on
  the LIST envelope either.
* Wire additions (**additive — WO-1818's flat-SKR fields and WO-1799's `saleBps`/`saleLabel`/
  `saleEndsAt` are untouched**): `discountSource` (`'promo' | 'sale' | 'repair_shortfall' | null`) and
  `promoEndsAt`. `saleBps` stays gated on `discountReason === SALE_REASON` (`:154-156`), so a promo
  quote correctly reports `saleBps: null` — the client must not print a storefront badge for a
  personal discount.

### 4.6 `api/_lib/ops.js` + `api/admin/ops.js` — authoring
`validatePromoDraft` accepts `discountBps` / `discountStartsAt` / `discountEndsAt`, treats a discount
as satisfying `REWARD_EMPTY`, and refuses a half-authored window (bps with no end, or end before
start), and **requires an explicit UTC offset on both bounds** (see §6.2 — the trap that would have
shipped silently).

`createPromo` names the new columns inside the `PG_UNDEFINED_COLUMN` (42703) cascade, which becomes
**THREE shapes, not two** — corrected during implementation: full → (a draft that WANTS a discount
throws `DISCOUNT_COLUMNS_MISSING`, nothing written) → **`created_by` WITHOUT the discount columns** →
neither. ⛔ Without that middle shape, a database carrying migration 0021 but not 0028 — the real state
of production between the two runs — falls straight to the oldest shape and **silently strips
`created_by` from every ordinary grant code authored in that window**: a path that worked yesterday
degrading today because of a feature it has nothing to do with. `api/admin/ops.js`'s
`attribution_on_row` flag is re-keyed to `shape !== 'without_created_by'` so the middle shape does not
raise a false alarm about the one thing that flag exists to report truthfully.

---

## 5. ACCEPTANCE CRITERIA

- [ ] `node --test test/*.test.js` — no NEW failures vs the recorded baseline (`tests 771 / pass 769 /
      fail 1 / todo 1`; the one failure is `heartbound-suite.test.js`, pre-existing and outside this silo).
- [ ] A discount code grants its window: redeeming inside `[starts, ends]` succeeds and the player's
      derived active discount reports the authored bps and `discount_ends_at` — **not** redemption + 48h.
- [ ] Redeeming before `discount_starts_at` or after `discount_ends_at` → `EXPIRED`, **unconsumed**.
- [ ] Quote reflects **the promo, not the larger sale**: promo 3000 bps beats a live 5000 bps sale.
- [ ] With no promo, the sale applies exactly as it does today (WO-1799 regression intact).
- [ ] An expired promo stops applying with no write, no sweep, no cleanup.
- [ ] The atomic claim still behaves: a discount-carrying code cannot double-grant, and the cap
      predicate and the `23505` path are unchanged.
- [ ] A discount-only code (0 crystals, 0 coins) is NOT refused `REWARD_UNAVAILABLE`.
- [ ] `discountSource` names the winner; `saleBps` is null on a promo-priced quote.
- [ ] The migration file exists, is reachable by `tools/run-migrations.mjs`, and matches `api/schema.sql`.

## 6. WHAT NEEDS THE OWNER'S EYES

1. **A LIVE MIGRATION MUST BE RUN BEFORE SPOTLIGHT30 CAN EXIST.** `node tools/run-migrations.mjs`
   with `DATABASE_URL` in env. Until then `promo_codes` has no `discount_bps` and the authoring path
   falls back to the no-discount shape. **This seat cannot and did not run it** — no live DB access,
   no deploy, by the silo rules.
2. **CST HAS NO OFFSET IN `Date.parse`.** `ops.js optionalExpiry` (`:282-291`) uses `Date.parse`, so
   `"2026-09-18 12:00"` typed without an offset parses as **UTC** on Vercel and lands the window 5–6
   hours off. The new validator therefore **requires an explicit offset** (`2026-09-18T12:00:00-05:00`).
   The owner (or whoever authors the row) must type the offset. CDT is `-05:00` in September; CST is
   `-06:00`.
3. **THE SHIPPED APK CANNOT DRAW "PROMO ACTIVE" ANYWHERE.** `PromoCodeService` renders the promo
   `message` string, and `PackStore` draws card prices from authored client data (`quote.js:135-150`
   records this at source for the sale fields). So: the **discount is FELT at the confirm line** (which
   does read `discountLabel` + `usdSaving`), and the only place the player is *told* about the window
   is the `message` text the operator authors. Recommended message: `30% off everything until 11:59pm
   CST Thu — already applied at checkout.` A badge on the shelf needs a new APK.
4. **Does the shortfall apology still beat a larger promo?** D3 says yes, because she ruled on promo vs
   the global SALE only. One line to change if she wants otherwise.
5. **`ADMIN_DASH_KEY` is needed to author the code through the Command Center.** Not read, not printed
   and not handled by this seat.

## 7. WHAT NOT TO TOUCH

* Any `.cs`, any `.unity`, any `*.generated.json`.
* `api/purchases/verify.js` / `fulfill.js` / `reconcile.js` — they run AFTER the money moves and price
  from the persisted row. They need no change and must not gain one (`quote.js:253-262`).
* The atomic claim statements' cap predicate, the `23505` handler, the IP budget, the owner bypass.
* `CLI_LANES_WO_NUMBERS.md` (number pre-assigned by the lead).
