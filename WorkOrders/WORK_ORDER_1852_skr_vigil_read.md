# WORK ORDER 1852 — Clan system, step 9: SKR Vigil read (percentage-staked + game-tracked tenure)

**Status:** READY FOR LEAD REVIEW - implemented 2026-09-17; `node --check` clean on all 5 JS files; `node --test test/*.test.js` 1047/1045 pass -> 1098/1096 pass with the SAME two pre-existing Heartbound reds and no new ones; NO commit, NO deploy (lead owns both). Two items need a lead/owner ruling before this ships: the percent DENOMINATOR (the spec's literal formula divides by zero for every real staker - FLAG 1) and whether `VIGIL_STAMP_ON_AUTH` should default OFF on cost grounds (FLAG 2). PRIOR STATUS: READY TO IMPLEMENT

## Context — clan WO-9 in the chain, depends on WO-1851 (landed, committed 2c22f341d)

Source: `docs/SKR Integtration.md` ("WO-9"), `docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`
(the "Vigil" design doc from this session's SKR deep-dive). WO-1851's gate is open; this is the first
ticket that actually reads real on-chain SKR stake state into the clan system.

## Scope

`GET /api/clan/vigil` returning per-member `{wallet, percent_staked, tenure_seconds,
vigil_contribution}` and a clan-level `vigil_weight` aggregate. Populates
`wallet_identity.first_seen_staked_at` on first observation of a staked wallet.

### Implementation

1. Extend `api/_lib/skr-staking.js` with `getStakedPercentage(sql, wallet)`:
   - Call the existing `verifyStake(wallet)` for the active staked amount.
   - Read the wallet's total SKR SPL token balance (one RPC call — **VERIFY BEFORE BUILD**: confirm
     the SKR token mint address at source before writing this; do not assume it from memory/prose).
   - Return `{ stakedRaw, balanceRaw, percent: stakedRaw / balanceRaw }`.
   - Cache 60 seconds per wallet — reuse the existing `resolveServedState`/`isCacheFresh` pattern
     already in `skr-staking.js` rather than inventing a second caching mechanism.
2. Extend `touchWalletIdentity` (`api/_lib/wallet-auth.js`, from WO-1844): after the upsert, if
   `first_seen_staked_at IS NULL`, call `getStakedPercentage`; if `percent > 0`, set
   `first_seen_staked_at = NOW()`. Use **Postgres time**, not Node time (avoids clock skew — owner's
   own stated preference in the source spec).
3. New endpoint `GET /api/clan/vigil`: `authenticate()`, caller must be in a clan (reuse the existing
   `beginClanRequest` preamble pattern). For each member: `tenure_seconds = NOW() - first_seen_staked_at`
   (0 if NULL), `vigil_contribution = percent_staked * tenure_seconds`, `vigil_weight = sum(...)`.

## Non-scope

- No chain history indexing — tenure counts from first observation only (owner-ruled).
- No per-epoch bucketing — raw elapsed seconds since `first_seen_staked_at`.
- No ballot/perk logic — that's WO-1853.
- No client UI.

## Acceptance criteria

- [ ] A wallet with 0 stake: `percent_staked = 0`, `first_seen_staked_at = NULL`.
- [ ] A wallet with stake: `first_seen_staked_at` set on first observation, never updates after.
- [ ] `GET /api/clan/vigil` returns correct sums for a two-member clan with different stakes.
- [ ] An RPC error does NOT 500 the endpoint — affected members show `percent_staked = 0` and a
      `degraded: true` flag on that member (or the response, per whichever shape reads cleaner — pick
      one and be consistent, flag the choice in the hand-back).
- [ ] Caching: two calls within 60 seconds produce exactly one RPC round-trip per wallet.
- [ ] Never renders a wallet address to any surface beyond what the caller's own clan membership
      already permits (this endpoint is clan-internal, not admin/public — still worth a test pinning
      that it never leaks into, say, an error message verbatim if avoidable).

## Test plan

1. Wallet A stakes 100 SKR, holds 200 total → `percent_staked = 0.5`.
2. Wallet B has no stake → `percent_staked = 0`, `first_seen_staked_at = NULL`.
3. Both in one clan, call `/api/clan/vigil` → `vigil_weight = 0.5 × tenure_A` (B contributes 0).
4. Simulate an RPC failure → endpoint still returns 200 with `degraded: true`.

## Rollback

Remove the endpoint; revert `touchWalletIdentity` to its WO-1844 behavior. `first_seen_staked_at`
stays NULL for all rows — no data loss.

## VERIFY BEFORE BUILD

- The SKR token mint address, at source, before writing any balance-read code — do not assume it.
- Confirm `skr-staking.js` doesn't already read total balance somewhere reusable before adding a new
  RPC call.

## Copy rules

"Vigil" is the approved player-facing term for tenure. Never state duration in hours or days — state
in epochs, or "the tree remembers." Never use "earn," "yield," "return," "APY."

---

## IMPLEMENTATION RECORD (2026-09-17) — files, proof, and the flags the lead must rule on

**Files written**
- `api/_lib/skr-staking.js` — `getStakedPercentage`, `readSkrBalance`, `vigilPercentPpm`,
  `ppmToFraction`, `isFreshWithin`, `VIGIL_CACHE_TTL_SECONDS`, `_resetVigilCache`. No existing
  behaviour changed: `isCacheFresh` now delegates to `isFreshWithin` and is asserted byte-identical
  at both edges (299 s true / 300 s false / null false).
- `api/_lib/wallet-auth.js` — `touchWalletIdentity` extended; new `stampFirstSeenStaked` and
  `vigilStampOnAuthEnabled`. The WO-1844 header paragraph that said this function "writes ONLY two
  columns … Nothing here may set, read or reason about them" is now CORRECTED in place rather than
  left to lie (CLAUDE.md §15).
- `api/_lib/clan-vigil.js` — NEW. Roster read, aggregation, per-member stamping, wire shape.
- `api/clan/vigil.js` — NEW. `GET /api/clan/vigil`.
- `test/clan-vigil.test.js` — NEW, 49 cases.
- `test/wallet-identity.test.js` — WO-1844's pin file, two pins NARROWED + three pins ADDED. See
  FLAG 6; this is the only file touched that another ticket owns.

**NOT touched:** `api/_lib/clan.js` (WO-1851's file — the brief forbade it; that is why the Vigil
lives in its own module), anything under `Assets/`, any migration. No new migration is needed:
`first_seen_staked_at` was already declared by `20260917_0029_wallet_identity.sql:22` and its partial
index by `:30-32`, explicitly "the Vigil's future working set".

**Test run:** before **1047 tests / 1045 pass / 1 fail / 1 todo** → after **1098 / 1096 / 1 / 1**.
The fail and the todo are PRE-EXISTING, unrelated, and identical before and after:
`heartbound-contract.test.js:272` carries `{ todo: 'WO-1693 finding 2 …' }` (the counted todo) and
`heartbound-suite.test.js:251` is the counted fail. Neither file is touched here. Same two reds
WO-1845's record named. `node --check` passes on all five JS files.

**⛔ NO DEPLOY, NO COMMIT, NO DDL.** Nothing was pushed and no migration was run.

---

### ⛔ VERIFY-BEFORE-BUILD, ITEM 1 — the SKR mint, confirmed at source BEFORE any balance code

The work order required this and it was done first, from three independent places, none of them a doc:

| Where | What it says |
|---|---|
| `api/_lib/skr-staking.js:105` | `const SKR_MINT = 'SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3'` |
| `api/_lib/purchase-catalog.js:31` | `const MAINNET_SKR_MINT = 'SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3'` |
| `test/purchases.verify.test.js:56` | already PINS that exact string |

Then confirmed **live on mainnet**, because two files agreeing is still two files:
`getTokenSupply(SKRbvo6Gf7…NPGZhW3)` → `{ amount: "10599697046828418", decimals: 6, uiAmountString:
"10599697046.828418" }` at slot **447954072**, apiVersion 4.3.0-rc.0. **Decimals 6**, matching
`SKR_DECIMALS` — so the 1000x decimals trap `purchase-catalog.js:37-48` records is not re-opened.

**No fourth copy was created.** `clan-vigil.js` reads the mint from `skr-staking`, and
`test/clan-vigil.test.js` asserts by grep that the literal `SKRbvo6` does not appear in it.

⚠ Note on the staking PROGRAM id: `SKRskrmtL83pcL4YqLWt6iPefDqwXQWHSw9S9vz94BZ`. It is prefixed
`SKR` and sits four lines from the mint, so it reads like a second mint address and is not one. It
was not used anywhere as a mint.

### ⛔ VERIFY-BEFORE-BUILD, ITEM 2 — no reusable balance read already existed

`grep -rn "getTokenAccount\|getBalance\|getProgramAccounts\|jsonParsed" api/` returned only
`purchases/verify.js:89` and `tower-swap/log.js:93` (both `getTransaction`, for signature
verification) and `skr-staking.js:372` (`getAccountInfo`). **Nothing in the repo read an SPL token
balance.** `skr-staking.js`'s own header says "Two getAccountInfo calls, and that is the whole
interaction" — accurate before this ticket. So one new RPC method was genuinely required.

### THE RPC CALL SHAPE — MEASURED, NOT ASSUMED

The brief said not to guess at call shapes, so the shape was executed against mainnet before being
written down. The probe never printed the RPC URL.

```
getTokenAccountsByOwner
  [ <owner>, { mint: SKR_MINT }, { encoding: 'jsonParsed', commitment: 'confirmed' } ]
-> { context: { slot, apiVersion }, value: [ { pubkey, account: { data: { parsed: { info: {
      isNative, mint, owner, state,
      tokenAmount: { amount: "<base units, STRING>", decimals: 6, uiAmount, uiAmountString }
   } } } } } ] }
```

Four behaviours observed rather than inferred, each of which decides a branch in `readSkrBalance`:

| Input | Observed | What the code does |
|---|---|---|
| owner holding SKR | HTTP 200, `value` array | sum every `tokenAmount.amount` as BigInt |
| `11111111111111111111111111111112` (no SKR) | HTTP 200, **`value: []`** | a REAL `0n`, `degraded: false` |
| `not-a-pubkey` | HTTP 200, JSON-RPC **`-32602 "Invalid param"`** | `invalid_response`, amounts stay NULL |
| same owner, USDC mint filter | 0 accounts | the mint filter really filters |

**⛔ AND A WALLET CAN HOLD MORE THAN ONE TOKEN ACCOUNT FOR ONE MINT.** Measured: owner
`4HQy82s9CHTv1GsYKnANHMiHfhcqesYkK6sB3RDSYyqw` holds **two** SKR accounts —
`5BxcwkSrbZs4RT1we2xABuJ9XDyqT1pfehZVGaZ5qgFF` (26 SKR) and
`8isViKbwhuhFhsv2t8vaFL74pKCqaFPQXo1KkeQwZbB8` (5,019,397,722.353354 SKR). An ATA-only read — the
obvious implementation — would have reported one of those and been wrong by **eight orders of
magnitude**. `readSkrBalance` sums all of them in ONE round trip, and a test pins it.

Free corroboration worth one line: `getProgramAccounts` filtered by the UserStake discriminator and
`dataSize: 169` returned **47,925** live accounts. `skr-staking.js:51` recorded **47,862** on
2026-09-10. The IDL layout this module decodes still holds a week later.

---

## ⛔ FLAG 1 — THE SPEC'S PERCENT FORMULA DIVIDES BY ZERO FOR EVERY REAL STAKER. A ruling is needed.

**This is the one thing the lead must read.** Implementation §1 says return
`percent: stakedRaw / balanceRaw`. Read literally — staked over the wallet's own SPL balance — that
is **undefined for every real staker**, because **staking MOVES THE TOKENS OUT OF THE WALLET**:

- `StakeConfig.stake_vault` (IDL offset 73, read from the chain) =
  `8isViKbwhuhFhsv2t8vaFL74pKCqaFPQXo1KkeQwZbB8` — which **is** the largest SKR token account in
  existence, **5,019,397,938.846808 SKR**, owned by the StakeConfig PDA
  `4HQy82s9CHTv1GsYKnANHMiHfhcqesYkK6sB3RDSYyqw`. Not by any player.
- Five real top stakers, read end-to-end at slot ~447954300, **all hold ZERO SKR in their own
  wallets**:

| wallet | active stake (SKR) | liquid | `staked/liquid` | `staked/(staked+liquid)` |
|---|---|---|---|---|
| `DLQtTaJKEU8yCxC2boPMdttJf3BWDNzFbJXGei8upiUZ` | 1,140,806,412 | **0** | **÷0** | 1.0 |
| `FLyuVULVz1yFdtCnyMexct2weP6VWuuqrWLawC22Jt8X` | 57,040,320.6 | **0** | **÷0** | 1.0 |
| `JBjKnMrQkGYtCK4d1X8fECg1w8jY8HD98cp7oXCeApNn` | 57,040,320.6 | **0** | **÷0** | 1.0 |
| `6kMxQyLFybR1sDrjwWM1UftYZBpWbma3x1M5Y2P9UBAQ` | 57,040,320.6 | **0** | **÷0** | 1.0 |
| `9hmq4gxbMcZsdPDfRQ5fKY8nwLTS7oC685T9a14mNcMQ` | 57,040,320.6 | **0** | **÷0** | 1.0 |

**The work order's own test plan settles the intent and agrees with the fix:** *"Wallet A stakes 100
SKR, holds 200 total → `percent_staked = 0.5`"* is only satisfiable if the denominator is **TOTAL
HOLDINGS** (100 staked + 100 liquid). So `balanceRaw` in §1 means *total*, not *liquid*, and §1 and
the test plan were never in agreement under the literal reading.

**What shipped:** `percent = activeStaked / (activeStaked + unstaking + liquid)`. The return object
carries `activeStakedRaw`, `unstakingRaw`, `liquidRaw`, `totalRaw` **and** `balanceRaw` as an alias
of `totalRaw`, so the spec's field name exists and means what its test plan implies. This is a
reconciliation of the spec against itself, **not** a silent deviation — it is flagged here per §11B
and needs a one-word confirmation.

**Sub-decision inside FLAG 1, also needing a nod:** `unstakingRaw` is INCLUDED in the denominator.
The discriminating case, written down so nobody has to re-derive it: *50 active / 50 unstaking / 0
liquid* reads **0.5** with unstaking counted and **1.0** without. Included is the honest "share of
your SKR that is on Vigil" — mid-cooldown tokens are still the player's SKR and are provably not
actively staked (`shares` already excludes them; `skr-staking.js` header). The NUMERATOR is
`activeStakedRaw` alone either way.

**Live end-to-end proof of the shipped function** (the real `getStakedPercentage`, real mainnet):

| wallet | status | active | liquid | total | percent |
|---|---|---|---|---|---|
| `DLQtTaJKEU8…upiUZ` | VERIFIED | 1,140,806,412 SKR | 0 | 1140806412000000 | **1** |
| `7bgYJAMpn8zeRq9thSJdA2PhqCpJPVx3jAww3pFYr44S` | NO_STAKE | 0 | 510,000,000 SKR | 510000000000000 | **0** |
| `11111111111111111111111111111112` | NO_STAKE | 0 | 0 | 0 | **0** |

All three `degraded: false`, all three finite, none NaN, slots 447957950-447957952. Second call on
each returned `fromCache: true` in **0 ms**.

## ⛔ FLAG 2 — the auth-path probe is a STANDING COST. Recommend defaulting it OFF.

The work order puts the stamp in `touchWalletIdentity`, which runs on **every proven wallet-rail
request** — every save, every load, every clan call. Implemented as specified, but with a switch,
because the arithmetic deserves a decision rather than a default:

- A wallet that has never staked keeps `first_seen_staked_at` NULL **forever**, so for that wallet
  the check never stops being reached. (A staker pays once, ever — the early return sees the stamp.)
- One probe is **3 RPC round-trips** (2 `getAccountInfo` + 1 `getTokenAccountsByOwner`).
- The 60 s cache bounds it to ≤1 probe/wallet/minute/**warm instance** → **~60 probes ≈ 180 RPC
  calls per hour per active non-staked wallet.**
- **No mainnet RPC provider has been provisioned for this project at all** — `mainnetRpcUrl()`'s own
  TODO says so, and the public endpoint 429'd a *single* `getTokenLargestAccounts` during this
  ticket's probing. Against a metered provider this is a real line item, and memory records a
  $200/month stop-loss with cost standing top of mind.

`VIGIL_STAMP_ON_AUTH` **defaults ON** (the work order's specified behaviour; a switch defaulting to
doing nothing would have shipped the feature dark). **Recommendation: the lead sets it OFF.**
Turning it off does **not** disable the Vigil — `/api/clan/vigil` stamps the column itself when it
reads a member's stake, so acceptance criterion 2 ("set on first observation") still holds; tenure
then begins on a clan read instead of on any auth. Both paths are tested.

**⛔ AND THE TEST SUITE NOW HAS AN ENV DEPENDENCY IT DID NOT HAVE BEFORE — the lead should know this
before touching CI environment variables.** Every existing test that drives `authenticate()` on the
wallet rail (`clan-membership`, `clan-leaderboard`, `clan-roles`, `wallet-identity`'s own wiring
tests, `game.load`, …) uses a mock that answers `INSERT INTO wallet_identity` **without** a
`first_seen_staked_at` field. `undefined != null` is false, so `touchWalletIdentity` now falls into
`stampFirstSeenStaked` → `getStakedPercentage` → `mainnetRpcUrl()` on every one of them. They pass
today **only because `SOLANA_MAINNET_RPC_URL` is unset in the test shell**, which short-circuits to
`rpc_url_unset` before any socket is opened. A CI or dev shell that exports that variable would turn
several unrelated suites into live mainnet callers. Not a defect in those tests — a dependency this
ticket introduced. It is a further argument for defaulting the switch OFF, and the two tests this
ticket adds that touch the path pin their own endpoint explicitly rather than inheriting the env.

## FLAG 3 — `getStakedPercentage(sql, wallet)` shipped as `getStakedPercentage(wallet, opts)`

The work order's signature takes `sql`. It was dropped, deliberately: `skr-staking.js` has **zero**
database imports today and is a pure chain reader whose header argues for exactly that shape, and
the cache does not need a database (see FLAG 4). Matching `verifyStake(wallet, opts)` also means
`test/skr-staking.test.js:233`'s structural product-rule-7 property — "takes a wallet and nothing
that could be an amount" — extends to it unchanged, and `test/clan-vigil.test.js` now asserts
`getStakedPercentage.length === 2` for the same reason. All database work stayed in `clan-vigil.js`
and `wallet-auth.js`.

## FLAG 4 — the 60-second cache is IN-PROCESS, so criterion 5 holds per warm instance

Acceptance criterion 5 ("two calls within 60 seconds produce exactly one RPC round-trip per wallet")
is **measured, not asserted**: the test starts a real `http.createServer`, records every JSON-RPC
method and address, and counts round trips. Proven: 1 `getTokenAccountsByOwner` + 2 `getAccountInfo`
on the first call, **zero** additional at t+59 s, one more at t+60 s (the boundary is exclusive).
Also proven live — the second call returned `fromCache: true` in 0 ms.

**Stated plainly: it is a module-level `Map`, so on Vercel each warm lambda holds its own.** The
criterion holds within a warm instance, not across a cold start or a second region. Two deliberate
choices inside it, both flagged rather than buried:
- **Failures are cached too.** Otherwise a dead RPC costs (members × 3) round trips on *every*
  request and turns someone else's outage into our own stampede.
- **It does NOT write `skr_stake_snapshots`.** That table's `verified_at` advancing *is* the
  mechanism behind Heartbound's grace-window ruling (`api/heartbound/status.js:191-196`); a Vigil
  read touching it would silently change how a different feature behaves during an outage.

The work order said not to invent a second caching mechanism. Taken literally: the freshness rule
now lives in exactly ONE function, `isFreshWithin`, which both `isCacheFresh` (300 s) and the Vigil
(60 s) call. Two numbers, one comparison.

## FLAG 5 — the degraded shape: PER MEMBER is the authority, top-level is a convenience

The work order asked for one shape to be picked and the choice flagged. **Both** are emitted, with a
stated hierarchy: `degraded: true` on the affected **member** (whose `percent_staked` reads 0) is the
authority, because a clan-level sum cannot say *which* member was unreadable; the top-level
`degraded` is derived from the members purely so a client can ask "is any of this untrustworthy"
without walking the array.

**A degraded member contributes ZERO, and that under-states the weight on purpose.** The alternative
— carrying a last-known percentage forward — is Heartbound's grace-window ruling, which exists
because losing a *streak* to an outage is unfair. A Vigil weight is a live *comparison between
clans*, so inventing a number we cannot currently see would advantage whoever happened to be
unreadable. **An RPC failure is always a 200** (criterion 4); the only 500 on this route is a
database failure, since without the roster there is nothing to shape.

## FLAG 6 — I EDITED ANOTHER TICKET'S PIN FILE. Please sanity-check this one.

`test/wallet-identity.test.js` is WO-1844's. Two of its pins went red on my change, and **they were
right to**: they pinned WO-1844's non-scope, in their own words — *"The three reserved columns belong
to later steps in this chain (the Vigil read, Genesis Token binding)."* **WO-1852 is that later
step**, chartered by this work order to populate `first_seen_staked_at` from this very function. So
they were NARROWED rather than worked around:

- `⛔ the conflict path updates last_seen_at and NOTHING ELSE` — the reserved-column sweep now
  forbids `first_seen_staked_at` in the **SET clause** (still load-bearing: putting it there would
  reset every staker's Vigil on every request) while permitting it in `RETURNING`. `sgt_mint` /
  `sgt_verified_at` remain forbidden anywhere.
- `⛔ no logic … touches the reserved Genesis Token columns` — narrowed to the two SGT columns, since
  building this logic is now the point. Replaced the removed coverage with a **containment** check:
  every `first_seen_staked_at` reference must sit inside `touchWalletIdentity` or
  `stampFirstSeenStaked`, which is where the guards live.
  **Red-proved, not assumed:** injecting a stray reference into the exports block is CAUGHT
  (spans 18887-21200 of a 26543-char file). A pin that cannot fail is worse than no pin.
- Three pins ADDED: the stamp is a separate idempotent Postgres-clocked UPDATE; the kill switch
  really kills (zero RPC, one SQL statement); and the switch's default.

Two WO-1844 invariants were re-checked as still green and untouched: the `ON CONFLICT … DO UPDATE
SET last_seen_at = NOW()` clause still names that column **alone** (so `first_seen_at` survives every
visit), and `authenticate()`'s return shape is still exactly `{ok, mode, identity}`.

## FLAG 7 — tenure NEVER resets, and the number means "time since first Vigil"

Per the work order ("never updates after"), a wallet that is observed staked, fully unstakes, then
restakes resumes from its **original** `first_seen_staked_at`. Contribution is 0 while unstaked, so
it is self-correcting in the product sense, but the tenure *number* is "time since we first saw you
staked", not "time spent staked". Flagged because the copy rules call this the Vigil and a player may
reasonably read it the other way. **No chain history** either, by the work order's own non-scope: a
wallet that staked a year before this feature existed starts its Vigil the day we first see it.

## FLAG 8 — the leaderboard metric swap is STILL OPEN; WO-1852 did not close it

`api/_lib/clan.js:930` (WO-1850) says the real Vigil weight is "WO-1852+ territory". **This ticket
does not close it**, and the leaderboard is untouched (asserted by grep — it does not import
`clan-vigil`). `/api/clan/leaderboard` ranks *every* clan and cannot RPC every member of every clan
per request; switching its metric needs a **persisted** `vigil_weight` column plus a refresh cadence,
i.e. a migration and a cron. Out of this ticket's scope — named here so nobody reads 1852 as having
finished it.

## Smaller decisions, on the record

- **404, not 200+null, for a caller in no clan.** `/api/clan/me` answers 200 + `clan:null` because
  "where do I stand" has that as a *successful* answer. "What is MY CLAN'S Vigil" has no subject
  without a clan, so it is 404 with the **existing** `CLAN_NOT_IN_CLAN` (`clan.js:138`, already the
  404 for `/leave`, `/promote`, `/demote`, `/kick`). No new code was invented.
- **No clan rate budget is spent.** `beginClanRequest(req, res, 'GET')` — three arguments, like
  `/me` and `/leaderboard`. The six `CLAN_RATE_LIMITS` budgets are for actions that CHANGE the
  roster; charging a read against them would let a player lose the ability to leave their clan by
  looking at it. Pinned by a test.
- **Concurrency is bounded at 5.** There is **no member cap in the schema** (grepped: no
  `max_member`/`member_cap` under `api/`; migration 0030 carries only the one-clan-per-wallet UNIQUE
  index), so clan size is unbounded and an unbounded fan-out would hand a large clan's worth of
  simultaneous RPC to a provider that rate-limits. The limit is measured in a test (peak in-flight
  ≤ 5 and > 1), not merely configured.
- **Wallets ARE rendered by this endpoint, and that is the one place they may be.** The caller has
  proven membership and a roster is what they are entitled to see. A test asserts no wallet string
  reaches ANY refusal body at any depth (criterion 6), and that the public-shaped leaderboard still
  renders none.
- **The roster is `LEFT JOIN`, not `JOIN`.** `wallet_identity` is written fail-open and can therefore
  be absent for a member; an inner join would silently drop them from their own clan's Vigil and the
  sum would be quietly wrong.

## What was NOT executed, stated plainly

- **No SQL in this ticket has been parsed or executed by Postgres.** The roster query (the
  `LEFT JOIN` + `EXTRACT(EPOCH …)`) and both stamp `UPDATE`s have run only against a recording
  tagged-template mock. Their *text* is asserted (LEFT JOIN present, `NOW()` present, `IS NULL`
  guard present, bound parameters), which catches a dropped guard but **not** a syntax or type
  error. Cheapest close: one `GET /api/clan/vigil` against preview with two members.
- **`degraded` has never been observed on a REAL outage** — only against a local server told to
  return 500 and against `127.0.0.1:1`. The branch is exercised; a genuine provider 429 is not.
- **The two-member acceptance case (test plan 3) is proven against fixtures, not two real staking
  wallets.** We do not control two mainnet wallets with known SKR stakes, so the 0.5-and-0 arithmetic
  is pinned offline. The single-wallet half of it IS proven live (table in FLAG 1).
- **`VIGIL_STAMP_ON_AUTH` has not been exercised in a deployed environment**, only in-process.
- **The live probes used the `HELIUS_RPC_URL` from `.env.local`, not `SOLANA_MAINNET_RPC_URL`.** The
  latter's value is redacted by this seat's secret scrubber and could not be read or used; the former
  is a working mainnet endpoint. So "the endpoint this feature will run against" is still unproven —
  it is the same unprovisioned-provider gap `mainnetRpcUrl()`'s TODO already records. The RPC URL was
  never printed to any log.

**Process note, since it cost real time and would cost the next seat the same:**
`test/wallet-identity.test.js` was NOT found by my pre-work reads — I searched for `verifyStake` /
`isCacheFresh` consumers and for clan endpoints, but never for tests pinning `wallet_identity`. The
full suite found it. **Before extending any `_lib` function, grep `test/` for its name**, not just
`api/`.
