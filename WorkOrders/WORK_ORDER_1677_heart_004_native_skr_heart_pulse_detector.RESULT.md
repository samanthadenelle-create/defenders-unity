# WORK ORDER 1677 — HEART-004 · RESULT

**Status:** IMPLEMENTED (backend only; not gated, not committed, not deployed by this lane)
**Date:** 2026-09-10
**Lane:** HEART-004 SME, isolated worktree at `dev` `c96030b5c` (`git merge --ff-only refs/heads/dev` ran clean before any edit).
**Seat rules honoured:** no `.cs` touched, no Unity run, no commit, no push, `CLI_LANES_WO_NUMBERS.md` untouched.

---

> ### ⚠ REVISED after a second `git merge --ff-only refs/heads/dev` (now at `4329accd1`)
> Two things this lane originally recorded as gaps were **snapshot artifacts of the ff point**, and the
> coordinator corrected them: `api/_lib/heartbound-resonance.js` + `heartbound-resonance-config.json`
> landed at **`cde1c1f63`**, *after* the first ff, and the triage rulings live uncommitted in the main
> tree. §6 items 2, 3 and 6 below are struck through accordingly. The reconciliation is §3.3b, and it
> is not cosmetic: **the real module's contract differs from the proposed one in unit, shape and call
> count**, so the first revision would not have run. Q-CONFIG is also now RULED — §3.4.

## 1. Files changed (the exact list)

| Path | State |
|---|---|
| `api/_lib/heartbound-pulse.js` | **NEW** — all detector logic; zero I/O of its own, every external fact injected |
| `api/cron/heart-pulse.js` | **NEW** — the daily Vercel cron function (auth + wiring shell only) |
| `api/_lib/heartbound-pulse-schema.sql` | **NEW** — DDL for `global_heart_pulse` + `player_pulse_grant` |
| `test/heartbound-pulse.test.js` | **NEW** — 24 `node:test` cases with an in-memory recording `sql` mock |
| `vercel.json` | **MODIFIED** — one line: the third cron |

`git status --short` in the worktree, verbatim:
```
 M vercel.json
?? api/_lib/heartbound-pulse-schema.sql
?? api/_lib/heartbound-pulse.js
?? api/cron/
?? test/heartbound-pulse.test.js
```

**Not touched, as instructed:** `api/schema.sql`, `api/game/save.js`, `api/_lib/heartbound-resonance.js`,
`api/_lib/heartbound-state.js`, `api/_lib/tunable-manifest.js`, `CLI_LANES_WO_NUMBERS.md`, any `.cs`,
any `.unity`.

---

## 2. Measured evidence

### 2.1 The tests — quoted, not summarised

`node --test test/heartbound-pulse.test.js` (node **v24.11.1**, measured with `node --version`):

```
ℹ tests 27
ℹ suites 0
ℹ pass 27
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 59.5983
```
`node --check` clean on all three `.js` after the reconciliation (`CHECK_OK`).

Judged by the runner's own result lines, not by the exit code (CLAUDE.md §8).

### 2.2 The suite actually bites — proven by mutation, not by inspection

A passing suite proves nothing until a mutant fails it. Two mutants were injected and reverted:

**Mutant A — the calendar-day cadence key replaced by the elapsed-ms floor it deliberately is not:**
```
✖ Q-CADENCE: CRON JITTER MUST NOT SKIP A DAY — 23h59m25s apart across midnight still MINTS
ℹ tests 24     ℹ pass 23     ℹ fail 1
```

**Mutant B — `MAX_CATCHUP_PULSES` raised 5 → 50:**
```
✖ acceptance 4: catch-up is capped at 5 pending pulses, oldest first, remainder REPORTED
✖ acceptance 4: a returning player's PENDING pulses are promoted, capped at 5, remainder deferred
ℹ tests 24     ℹ pass 22     ℹ fail 2
```

**Mutant C (after the HEART-003 reconciliation) — `applyPulse` swapped for `observeStake`,
i.e. the wrong transition from the same module:**
```
✖ a PENDING row from an earlier failed run is PROMOTED in place, never re-inserted
✖ the grant carries HEART-003's own numbers — computed independently, not restated
ℹ tests 27     ℹ pass 25     ℹ fail 2
```

Revert proven by content comparison, not by a diff of an untracked file:
`cmp <scratchpad>/hp*.bak api/_lib/heartbound-pulse.js` → **`CMP_IDENTICAL_RESTORED`** after each,
then `ℹ tests 27  ℹ pass 27  ℹ fail 0`.

### 2.2b The board parses the flipped Status line — measured, not assumed

`tools/board_build.py` loaded and run against the WO file:
```
classify: ('Done', False)
bucket: Done
contradiction:            <- empty; no contradiction flagged
```
So the `**Status:** IMPLEMENTED — 2026-09-10. …` shape (a leading canonical keyword plus a summary,
with the superseded-note as a following blockquote) lands in **Done** with no near-miss.

### 2.3 Syntax check on every new `.js`

```
node --check api/_lib/heartbound-pulse.js   -> CHECK_OK pulse
node --check api/cron/heart-pulse.js        -> CHECK_OK cron
node --check test/heartbound-pulse.test.js  -> CHECK_OK test
```

### 2.4 Sources read at source this session (not from a doc)

- `vercel.json` — **exactly two** crons before this change, both daily (`0 4 * * *`, `30 4 * * *`). Now three.
- `api/admin/cleanup.js:33-59,62-63,66-74` — the cron auth pattern copied in shape: SHA-256 →
  `timingSafeEqual`, `Bearer CRON_SECRET` **or** `X-Admin-Key`, refusal **400**, no CORS.
- `api/referral/install-brag.js:114-151` — the idempotency shape §0c names, read and reused verbatim in
  shape: `INSERT … ON CONFLICT (…) DO NOTHING RETURNING`; **zero rows returned ⇒ "already granted"**,
  re-read the original, never a second reward.
- `api/schema.sql:960-989` — `achievement_grants`, `PRIMARY KEY (wallet, achievement_id)`, whose own
  comment reads *"Generic idempotency ledger… The PK STRUCTURALLY prevents a double-grant"*.
  `player_pulse_grant` is that shape keyed on `(player_id, global_pulse_id)`. Its rows are **not**
  reused (WO §5).
- `test/benefactors.test.js:52-64` — the recording tagged-template `mockSql` style this suite follows.
- `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:376-468` (HEART-004 in full) and
  `:200-375` (HEART-002/003, for the state fields and the arithmetic names I consume).
- `WorkOrders/WORK_ORDER_1676_…md:22-45` — the HEART-003 function table, which is where the resonance
  call shape comes from.

---

## 3. What was built, and the reasoning that is load-bearing

### 3.1 D1/D2 — the DDL, in a NEW file (`api/_lib/heartbound-pulse-schema.sql`)

- `global_heart_pulse` — spec `:404-416` fields. **`UNIQUE (new_share_price)`** makes acceptance 1
  structural: the share price only rises, so a re-scan over the same chain state cannot mint a second row.
- `player_pulse_grant` — **`PRIMARY KEY (player_id, global_pulse_id)`**. Acceptance 2 is a property of
  the table, not of the job being careful.
- **`NUMERIC(39,0)`, never `BIGINT`, for both price columns.** The share price is a u128 on chain
  (`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:65`, read at byte offset 137); Postgres `BIGINT` is
  int64 and **would overflow**. 39 digits covers `2^128-1`; scale 0 satisfies spec `:1113` ("never
  floats"). JS binds them as strings from `bigint` — no value crosses a JS `number`.

⛔ **This DDL is applied by NOTHING yet, deliberately.** `api/schema.sql` belongs to the HEART-002 lane
this wave, so this lane may not edit it. **THE LEAD MUST fold these two blocks into `api/schema.sql`
or run this file as a migration** before the route can do anything but 500. Consequently
**WO acceptance 6 (`MIGRATIONS_OK applied=N skipped=M` + a shape query) is NOT DONE by this lane** —
it is the lead's step, and it is named here rather than quietly ticked.

### 3.2 D3 — the detector (`api/_lib/heartbound-pulse.js` + `api/cron/heart-pulse.js`)

All logic lives in `_lib`; the cron file is auth + wiring, so the whole job is testable without a DB.

- `detectPulse()` — newest pulse → chain price → mint at most one.
- `processPlayerForPulse()` — spec `:432-443`'s ten ordered steps.
- `selectCatchupPulses()` — pure, so the cap is testable without a database.
- `runHeartPulseDetector()` — the job, in three phases: **DETECT → RESUME → SWEEP** (§3.3a). One bad
  player is logged and skipped, never fatal (§12 step 2).

**⛔ The cadence floor is a UTC CALENDAR-DAY key, NOT an elapsed-millisecond floor — and that
distinction is a real bug caught before it shipped.** A `now - last < 86_400_000` test reads as
equivalent and is not: Vercel does not fire a cron to the second, so day N at 05:00:45 followed by day
N+1 at 05:00:10 is 86,399,000 ms — *inside* the floor — and a real advancement would have been refused
and the pulse skipped for a whole day on ordinary scheduler jitter. `MAX_PULSES_PER_UTC_DAY` +
`utcDayKey()` have no such edge, and the case is pinned by a test that fails against the elapsed-ms
form (mutant A, §2.2). Vercel cron schedules are UTC, which is why the key is UTC. The `BASELINE` row
is exempt from the floor: it is bookkeeping, not a pulse, so the first real pulse does not wait a day
behind it.

**First-run baseline — a decision this lane made and is naming.** With an empty table there is no
"previously processed share price". The first run writes a `BASELINE` row (sequence 0, previous price
`NULL`) that **grants nothing**, so the next run has something real to compare against. Without it the
first deploy would either mint a pulse for an advancement nobody observed, or never mint at all.

### 3.3a D4 retry — the three phases, and why RESUME is not a special case

One scheduled run does:

1. **DETECT** — mint at most one pulse (§3.4).
2. **RESUME** — process **every** pulse still at status `MINTED`, oldest first, capped at
   `MAX_CATCHUP_PULSES`. The pulse just minted is one of those rows, so the happy path and the
   crash-recovery path are **the same code**: a run that died half way through pulse 43 leaves 43
   `MINTED`, and the next run finishes it. There is no "the pulse I just made" branch that could be
   forgotten in a retry.
3. **SWEEP** — promote `PENDING` grants sitting on pulses already `COMPLETE` (the player whose RPC read
   failed while everyone else's succeeded), grouped per player, oldest first, capped at 5 with the
   remainder returned as `deferred`.

Phases 2 and 3 **are** D4's "retry later". Spec `:451-461` chose *mark pending, re-scan next run* over
a queue precisely so it could be a table and a cron (WO-1677 §5 forbids adding queue infrastructure),
and this is that re-scan. Every write in both phases goes through the same `ON CONFLICT` gate, so a
re-run can never double-pay. If a run has no pulse, no unfinished work and no debts, it returns before
touching the HEART-002/003 seams — a quiet day never fails on a seam it did not need.

### 3.3b Reconciliation against the REAL `heartbound-resonance.js` (read at source, `cde1c1f63`)

The proposed five-name contract was **wrong in three ways**, all of which would have failed at runtime
rather than quietly — but none of which would have been caught without opening the file:

| Axis | Proposed (first revision) | **Actual, read at source** |
|---|---|---|
| Unit | raw u128 base units (`bigint`) throughout | **whole SKR as a `Number`** — `isEligible(actualSkr, cfg)` (`:154`), `applyPulse(state, actualSkr, cfg)` (`:297`) |
| Shape | five loose functions over scalars | a **state transition**: `{actualSkr, effectiveSkr, continuousPulseCount, highestLifetimeTier}` in, a new frozen state out (`:250-254`) |
| Call count | three calls (activation ramp / pulse ramp / score) | **one** — `applyPulse` already does the immediate-decrease clamp (`:304`, the anti-flash-stake asymmetry), the 25% ramp, the streak increment and the `highestLifetimeTier` lift; `evaluate(state, cfg)` (`:348`) then derives score/tier/tierName |
| Names | `isEligibleStake`, `effectiveStakeOnActivation`, `effectiveStakeOnPulse`, `resonanceScore({…})`, `resonanceTier` | `isEligible`, `activate`, `applyPulse`, `observeStake`, `evaluate` (+ the boundary helpers) |

Changes made:
- **The lazy require and the name-validation shim are DELETED.** The module is `require`d directly at
  the top of the file. It is pure (no DB, no network), so that is safe for the tests too, and the
  require *is* the contract proof — a renamed export is a `TypeError` naming it. The shim was
  duplicated state about another file's exports; deleting the copy is the cure (CLAUDE.md §2/§5/§8).
- **`processPlayerForPulse` now calls `applyPulse` + `evaluate` once each**, passing the module's
  trailing `cfg` on every call. `deps.resonanceConfig` is threaded through and left `undefined` by
  default, which is exactly how the module's `cfg = DEFAULT_CONFIG` defaults engage — one call shape,
  never two.
- **`stakeReadingToSkr(reading, resonance, cfg)` is the single unit crossing** and uses HEART-003's own
  boundary helpers (`rawTokensToSkr` `:128`, `skrFromShares` `:138`). It accepts `{skr}`,
  `{rawTokens|rawU128}` or `{sharesRaw, sharePriceRaw}` in a fixed precedence and throws with all three
  named otherwise — because **HEART-001 has still not landed**, so the reading's field names are not
  yet fixed and this refuses to guess one. A malformed snapshot is treated as an unverified reading
  (PENDING), never as a zero stake.
- **`effective_stake` in the DDL moved `NUMERIC(39,0)` → `NUMERIC(39,6)`.** HEART-003 returns
  fractional SKR; scale 0 would have silently rounded every audit row. Six decimals is exactly the
  chain's base-unit granularity (`chain.skrBaseUnits = 1e6`). The **share-price** columns stay scale 0
  — those are u128 integers. (`heartbound-pulse-schema.sql` is this lane's own file; `api/schema.sql`
  was not touched, and neither was `vercel.json` in this revision.)
- **The suite now drives the REAL module**, not a fake — a fake can agree with a contract the real
  module does not have, which is precisely the failure this reconciliation closed. Three cases added:
  one that derives its expectation by calling `applyPulse`/`evaluate` directly (so it tracks the curve,
  not a typed-in number), one that proves the unit boundary is real (99 SKR = 99,000,000 base units is
  **below** the 100 SKR floor — a raw comparison would have called it eligible), and one for the
  malformed snapshot. Mutant C (§2.2) proves the transition call is pinned.

### 3.3 D4 + Q2 — RPC failure, and the reconciliation written into the code

Q2 (fail to last-known verified state) and spec `:451-461` (no unverified rewards) govern **different
things**, and conflating them would have shipped a payout on stale data. The code comment states it so
the next lane does not re-litigate it:

- **Q2 governs the PLAYER STATE**: never zero the stake, never reset the streak. The implementation is
  stronger than "restore last known" — the failure path **writes no state at all**, which the test
  asserts by failing if `persistPlayerState` is called.
- **Spec `:451-461` governs the REWARD**: none is generated; a `PENDING` grant row is recorded
  (`pending_reason` `RPC_UNAVAILABLE` or, past the 72 h grace, `STALE`) and promoted **in place** on a
  later successful run — one row per `(player, pulse)` forever, so the PK keeps meaning what it says.
- Grace **72 h** (`STALE_GRACE_MS`) and catch-up cap **5** (`MAX_CATCHUP_PULSES`) are constants at the
  top of the module, with the spec's own `:463` sentence kept in the comment. ⚠ They are **module
  constants, not config**, because WO-1676's Q-CONFIG (where backend-only knobs live) is **unruled** —
  see §5.

### 3.4 The rulings, as implemented

| Ruling | Where it lives |
|---|---|
| **Q-CADENCE** — rare, ceremonial, at most one per day | `MAX_PULSES_PER_UTC_DAY = 1` + `utcDayKey()`; a **real** advancement on a date that already pulsed is refused (`SKIP.INTERVAL_FLOOR`) and deliberately **dropped, not queued** — queueing would restore the faucet the floor exists to close. This is the `MIN_PULSE_INTERVAL` §0a said the spec lacked. |
| **Q-CRON** — a third daily cron accepted | `vercel.json`: `{ "path": "/api/cron/heart-pulse", "schedule": "0 5 * * *" }` — 05:00, after both existing daily crons so a long retention sweep never overlaps it. Worst-case pulse latency is therefore **24 h**, which is the stated consequence of the daily ruling. |
| **Q1** — the stake read is backend-only, HEART-001's | This lane wrote **no chain read**. `readSharePrice` / `readStake` are injected and default to placeholders throwing `"HEART-001 seam not wired: …"`. A source lint in the suite fails if `getAccountInfo` / `@solana` / `web3.js` / `fetch(` ever appears in either new file. |
| **Q-INJECT** — no injection route in production | The cron reads **only** the chain. A test asserts the route never touches `req.body` or `req.query`, and there is no "simulate pulse" export. |
| **Q2** | §3.3 above. |
| **Q-CONFIG** — RULED 2026-09-10: backend-only knobs are **Command Center server-only rows** | Not built here — the `serverOnly` marker in `api/_lib/tunable-manifest.js` is HEART-006 / HEART-009's job. This lane keeps its three constants in ONE block with that destination named beside them (`heartbound-pulse.js`, the `--- Config ---` header), so the later lift is a data move. `tunable-manifest.js` was NOT touched. |

### 3.5 Seams left for the other lanes (one line each to wire)

| Seam | Owner | Default |
|---|---|---|
| `readSharePrice()` → `{rawU128:bigint, sourceSlot?, chainReference?}` | HEART-001 | throws `HEART-001 seam not wired` |
| `readStake(wallet)` → `{skr}` \| `{rawTokens\|rawU128}` \| `{sharesRaw, sharePriceRaw}` | HEART-001 | throws `HEART-001 seam not wired` |
| `listActiveStates(sql)` | HEART-002 | throws `HEART-002 seam not wired` |
| `persistPlayerState(sql, state)` | HEART-002 | throws `HEART-002 seam not wired` |

**HEART-003 IS NO LONGER A SEAM.** `api/_lib/heartbound-resonance.js` + its config JSON landed at
`cde1c1f63` and are `require`d directly; see §3.3b for the reconciliation. Only the two HEART-001 reads
and the two HEART-002 state functions remain injected.

⚠ **HEART-001 has still NOT landed** (`grep -rn "rawU128|sharesRaw" api/_lib/*.js` at `4329accd1` hits
only `heartbound-resonance.js` and this lane's own file), so the stake reading's field names are not
fixed. `stakeReadingToSkr` therefore accepts the three shapes above in a fixed precedence and throws
naming all three rather than guessing one — and a snapshot it cannot read is treated as an unverified
reading (PENDING), never as a zero stake.

### 3.6 State fields consumed (the contract HEART-002 must satisfy)

`playerId`, `walletAddress`, `activatedAtUtc`, `lastVerifiedAtUtc`, `lastActualStake`,
`effectiveResonatingStake`, `continuousPulseCount`, `totalLifetimePulses`. Written back:
those plus `lastGlobalPulseId`, `resonanceScore`, `resonanceTier`.

---

## 4. Acceptance, line by line — honestly

| # | Criterion | Status |
|---|---|---|
| 1 | Exactly one pulse per unique advancement; second run mints nothing | **MET, tested** — three cases (mint / no-advancement / lost race) |
| 2 | A duplicate job cannot double-pay; PK violation → "already granted" | **MET, tested** — quoted mapping in §3.1; the test asserts `ON CONFLICT (player_id, global_pulse_id) DO NOTHING` is the gate |
| 3 | RPC timeout leaves the streak intact, no reward | **MET, tested** — at both levels (share price, per player); the test fails if state is written |
| 4 | Offline player's pending pulses on return, capped at 5 | **MET, tested** — the SWEEP phase (§3.3a) enumerates PENDING grants from `player_pulse_grant`, groups per player, promotes the 5 oldest in place and returns `deferred` for the rest. Test: seven owed → five paid, `deferred: 2`, five in-place UPDATEs and never a sixth INSERT. Mutant B (§2.2) proves the cap is enforced by the code and not by the test's arithmetic. |
| 5 | No pulse before `activated_at_utc` is processed | **MET, tested** — the chain is not even read on that path |
| 6 | `MIGRATIONS_OK applied=N skipped=M` + a shape query | **NOT DONE — the lead's step.** The DDL is in a new file because `api/schema.sql` is HEART-002's this wave. Nothing applies it yet. |
| 7 | Measured share-price cadence in the RESULT | **SUPERSEDED by Q-CADENCE** — see §5 |
| 8 | Status flipped, RESULT written, both paths reported | **MET** |

---

## 5. D0 — the cadence measurement, as a RECORD (not a gate)

**What the ruling settles.** Q-CADENCE (owner, 2026-09-10) bounds the pulse economy by **policy**: at
most one pulse per day. That bound holds **regardless of how fast the chain's share price advances** —
if it ticks per slot the floor discards the surplus; if it ticks per epoch the floor never binds. This
is why §0a's "designed against an assumed cadence" risk is closed without the measurement: nothing in
the design now depends on the chain's rate. `MAX_PULSES_PER_UTC_DAY` is the floor §0a said was missing.

**⛔ THE CADENCE REMAINS UNMEASURED. Stated as unproven, not estimated** (CLAUDE.md §11B). No RPC call
was made from this lane; there is no captured series, and none is invented here.

**What a later measurement would still tell us** — worth taking, and cheap:
1. **Whether the daily cron IDLES or DISCARDS.** If the price advances less often than daily, some days
   mint nothing and "one pulse per day" is a ceiling nobody reaches — the felt cadence is then the
   chain's, not the design's, and the ceremony may need a different trigger.
2. **How much is being discarded.** If it advances many times a day, the floor silently drops most
   advancements. That is intended, but the *ratio* is the input to HEART-009's "below 10% combined
   economic acceleration" and to any future decision to raise the floor.
3. **Whether `previous_share_price` is meaningful.** Under a fast tick, the recorded previous price is
   the price at the last *pulse*, not the last *advancement* — fine for audit, misleading if anyone
   later derives a reward size from the delta. Nothing does today.

**How to take it, unchanged from §0a:** poll `getAccountInfo` on StakeConfig
`4HQy82s9CHTv1GsYKnANHMiHfhcqesYkK6sB3RDSYyqw` (`NativeSkrStakeQuery.cs:26`), read the u128 at byte
offset 137 (`:65`), record the advancement timestamps over 24-48 h. **Best done once HEART-001's
backend read exists** (Q1) rather than as a throwaway script — the same read serves both.

---

## 6. Unproven / not done, named as such

1. **The cadence** — §5. Unmeasured.
2. **Acceptance 6** — no migration was run; no `MIGRATIONS_OK`, no shape query. The DDL has never
   touched a database, so the SQL is **syntactically unexecuted**: the tests assert its *text*, not that
   Postgres accepts it. The lead's fold-in run is the first real execution.
3. **`sql.transaction`** — `node_modules/` is absent from this worktree (`ls node_modules/@neondatabase/serverless/`
   → nothing), so whether this repo's driver exposes a multi-statement transaction is **unproven** and
   is not relied on. Spec step 10's "persist transactionally" is satisfied by a **gate-first** design:
   the grant INSERT is the gate and the state write only runs when it won. A crash between the two
   leaves a granted row and an un-advanced streak — recoverable, and never a double-pay; the opposite
   order would double-pay. **That is a design choice, not a transaction**, and it is written in the file
   header so nobody later reads "transactional" into it.
4. **The Vercel plan's cron granularity** — still not checked. It no longer matters for this ticket
   (daily is the ruling), so it was not pursued; it remains unanswerable from this repo.
5. ~~**`grep -n "RULED" … returns ZERO hits`**~~ — **RESOLVED, and it was a snapshot artifact.** The
   ruled blocks live **uncommitted in the main tree**, which a worktree at a committed ref cannot see;
   the coordinator confirmed it. The rulings implemented here (Q-CADENCE, Q-CRON, Q1, Q2, Q-INJECT,
   and now Q-CONFIG) are the owner's, relayed by the lane brief. Nothing was invented. ⚠ The
   CLAUDE.md §15 point still stands in weaker form: they are not yet **committed**, so a fresh clone
   reads only questions.
6. ~~**The five resonance function names are a proposed contract**~~ — **RESOLVED.** The real module
   landed at `cde1c1f63` and is now required directly; the proposed names were wrong in unit, shape
   and call count, and §3.3b records the diff. ⚠ Reading it also proved the wider point: **a contract
   inferred from a work order is hearsay** (CLAUDE.md §11B) — the first revision was honest about
   being unproven, and it was still wrong in three ways.
7. **`STALE_GRACE_MS` / `MAX_CATCHUP_PULSES` / `MAX_PULSES_PER_UTC_DAY` are module constants that have
   a NAMED destination.** Q-CONFIG is now **RULED** (§3.4): backend-only Heartbound knobs become
   **Command Center server-only rows** — a `serverOnly` marker in `api/_lib/tunable-manifest.js`
   honoured by `build()`, so a backend-read knob stops failing the three-way join at
   `tunable-manifest.js:848-853`. **Building that marker is HEART-006 / HEART-009's job, not this
   lane's**, so the three constants stay put — deliberately in ONE block, with the destination written
   beside them, so lifting them onto the rail is a data move — the same reasoning
   `heartbound-resonance-config.json` records for its own table.
8. **Nothing was deployed.** The cron only becomes live on the owner's next Vercel deploy — `vercel.json`
   is read at deploy time (the same note `api/admin/cleanup.js` carries about itself).

---

## 7. Hand-back

- **WO:** `WorkOrders/WORK_ORDER_1677_heart_004_native_skr_heart_pulse_detector.md` — `**Status:**`
  flipped to **IMPLEMENTED** with the date, a one-line summary, and a banner recording that §0a /
  acceptance 7 is superseded by Q-CADENCE rather than skipped.
- **RESULT:** `WorkOrders/WORK_ORDER_1677_heart_004_native_skr_heart_pulse_detector.RESULT.md` (this file).
- **For the lead:** fold `api/_lib/heartbound-pulse-schema.sql` into `api/schema.sql` (or run it as a
  migration) once HEART-002 releases that file, then run acceptance 6; regenerate `BOARD.html`; commit
  by explicit path.
