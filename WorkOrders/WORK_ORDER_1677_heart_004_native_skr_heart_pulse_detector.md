# WORK ORDER 1677 — HEART-004: Native SKR Heart Pulse detector

**Status:** IMPLEMENTED — 2026-09-10. Daily pulse-detector cron (`api/cron/heart-pulse.js`) + logic module + DDL + 27 passing node:test cases; consumes the real `heartbound-resonance.js` (landed `cde1c1f63`), chain reads left as named injected seams for HEART-001/002, DDL held out of `api/schema.sql` for the lead to fold in. See `WORK_ORDER_1677_heart_004_native_skr_heart_pulse_detector.RESULT.md`.

> ⚠ **§0a / acceptance 7 (the cadence measurement) is SUPERSEDED, not skipped.** Owner ruling
> **Q-CADENCE (2026-09-10)**: a Heart Pulse is rare and ceremonial, **AT MOST ONE PER DAY**. That is a
> POLICY bound and it holds whatever the chain cadence turns out to be, so the unmeasured on-chain
> tick no longer gates the design. It is implemented as `MAX_PULSES_PER_UTC_DAY` + `utcDayKey()`
> (`api/_lib/heartbound-pulse.js`) — the `MIN_PULSE_INTERVAL` floor §0a said the spec lacked, as a UTC
> calendar-day key rather than an elapsed-ms floor (cron jitter would otherwise skip a day; RESULT §3.2).
> The measurement is still worth taking later, for the reason recorded in the RESULT.
**Silo:** Backend (Neon tables + a scheduled job). No Unity, no gameplay, no scene files.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:376-468` (HEART-004).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW. And its cadence is an UNMEASURED on-chain fact that the spec never bounds.**

### 0a. ⛔ THE GAP THAT MAKES THIS SPEC, NOT READY: there is no `MIN_PULSE_INTERVAL`

The design is *"one global pulse per unique observed share-price advancement"* (spec `:400-402`, `:428`). The share price rises when SKR staking rewards accrue. **How often that is, is a fact about the Solana Mobile staking program that is not in this repo and has not been measured.**

Both readings are plausible and they lead to opposite systems:
- **Slow (per epoch, ~2 days):** a daily cron is sufficient, HEART-004's *"5 pending pulses"* cap (`:463`) is generous, and HEART-009's *"below 10% combined economic acceleration"* (`:910`) is easy to hold.
- **Fast (per slot / continuously compounding):** every scan produces a pulse, "5 pending pulses" is meaningless, and the economy cap is unenforceable by construction.

⛔ **CLAUDE.md §12 applies before a line is written: instrument, do not guess.** The cheap measurement is to poll `getAccountInfo` on the StakeConfig account (`4HQy82s9CHTv1GsYKnANHMiHfhcqesYkK6sB3RDSYyqw`, `Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:26`), read the share price at byte offset 137 (`:65`), and **record the observed advancement cadence over 24-48 hours before designing the job.** Put the captured series in the RESULT. A pulse system designed against an assumed cadence is the `fps=40` failure CLAUDE.md §11B records.

⚠ **Whatever the answer, the spec needs a `MIN_PULSE_INTERVAL` floor it does not currently have** — otherwise "one pulse per advancement" is an uncapped faucet, and every reward in WO-1678 rides on it.

### 0b. The scheduler — what exists, honestly

`vercel.json:6-9` declares **exactly two crons**, both daily:
```json
"crons": [
  { "path": "/api/admin/cleanup", "schedule": "0 4 * * *" },
  { "path": "/api/admin/google-play-voided-reconcile", "schedule": "30 4 * * *" }
]
```
There are **no queues and no durable workflows** — `package.json:12-22` carries no queue/workflow dependency. So the pulse job is a third cron, and the achievable granularity is a **platform-plan question that is not answerable from this repo**. ⛔ **Do not assert the Vercel plan tier from here.** Check it with `vercel:status` or the dashboard and record the answer; that check is a deliverable, not an assumption.

Cron auth pattern to copy: `api/admin/cleanup.js:37-59` — `Authorization: Bearer <CRON_SECRET>` **or** `X-Admin-Key == ADMIN_DASH_KEY`, both SHA-256-then-`timingSafeEqual`; refusal is **400, never 401/403** (`:71-73`); no CORS headers at all (`:62-63`).

### 0c. ⭐ The idempotency shape ALREADY EXISTS — do not invent one

Spec `:445-449` asks for a unique constraint on `playerId + globalPulseId` so *"a pulse can never pay the same player twice"*. Three existing precedents, all read at source:
- `api/schema.sql:978-984` — **`achievement_grants`**, `PRIMARY KEY (wallet, achievement_id)`, described at `:965-971` as a *"Generic idempotency ledger for 'grant this player X exactly once'… The PK STRUCTURALLY prevents a double-grant — a second request for the same pair violates the PK, which the endpoint maps to 'already granted' and returns the original (no second reward)."* **This is the exact shape, already in production, already described in the schema's own words.**
- `api/schema.sql:1127` — `purchase_entitlements.tx_signature TEXT NOT NULL UNIQUE`.
- `api/schema.sql:766-768` — `uq_tower_swaps_tx_sig`, a partial unique index, with the "signature squatting" conflict case written up at `:736-738`.

### 0d. ⛔ It cannot be enforced in the save. See WO-1675 §0a.
`api/game/save.js:70-72` and `:410-421` state that the save is a client-owned blob with anti-grief ceilings, *"NOT a server-authoritative economy"*. Any "already claimed" flag inside it is rewritable by a replayed blob. The `player_pulse_grants` table is the enforcement; the save may only ever mirror it for display.

---

## 1. Deliverables

### D0 — **MEASURE THE CADENCE FIRST** (§0a). This is deliverable zero and it gates the rest.

### D1 — `global_heart_pulse` table + migration
Fields per spec `:404-416`. `previous_share_price` / `new_share_price` as raw integer columns (never floats — spec `:1113`). Unique on the observed advancement so a re-scan cannot mint a second pulse for the same tick.

### D2 — `player_pulse_grant` table
`PRIMARY KEY (player_id, global_pulse_id)` — the `achievement_grants` shape, cited above. This is the row that makes *"a pulse can never pay the same player twice"* structurally true rather than procedurally true.

### D3 — The detector job
Reads StakeConfig, compares to the last processed share price, mints at most one pulse. Then per-player processing per spec `:432-443` (ten ordered steps ending *"Persist transactionally"*).

### D4 — RPC-failure behaviour, per spec `:451-467`
Do not reset the streak; do not generate unverified rewards; mark pending; retry. Grace **72 h**, catch-up cap **5 pending pulses**, both from config (WO-1676 Q-CONFIG). The spec's own reasoning at `:463` is worth keeping in the code comment: *"Anything beyond this should require explicit reconciliation rather than silently vomiting six months of resources into someone's castle."*

⚠ This directly contradicts today's client behaviour, which **fails closed to zero** (`NativeSkrStakeQuery.cs:87-94`). See WO-1674 Q2 — the ruling on fail-closed vs fail-last-known lands here.

---

## 2. Acceptance (from spec `:428-467` + `:1122-1136`)

1. Exactly one global pulse per unique observed share-price advancement — proven by running the detector twice over the same chain state and showing the second run mints nothing.
2. A duplicate background job cannot double-pay: the PK violation is caught and mapped to "already granted", quoted in the RESULT.
3. An RPC timeout leaves the streak intact and produces no reward.
4. A player offline for the grace window receives their pending pulses on return, capped at 5.
5. No pulse dated before `activated_at_utc` is processed (WO-1675 D2).
6. `MIGRATIONS_OK applied=N skipped=M` on a fresh run, **then** a shape query (memory `idempotent-ddl-hides-a-stale-table`).
7. **The measured share-price cadence is in the RESULT with timestamps** (§0a). Without it this ticket is not done, whatever the code does.
8. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 3. Dependencies

- **Blocked by:** WO-1674 (the verifier), WO-1675 (the state table), WO-1676 (the ramp), and §0a's measurement.
- **Blocks:** WO-1678 (every Echo Event hangs off a pulse), WO-1683.

---

## 4. ⛔ OWNER QUESTIONS

### Q-CADENCE. What is the minimum time between two Heart Pulses, and who decides?
§0a. Until the chain cadence is measured this cannot be answered, but the *policy* is the owner's: is a pulse a **rare event the player looks forward to** (daily-ish, ceremonial, worth the 2.5-4 s presentation HEART-007 asks for at `:797`) or a **frequent background tick**? The presentation spec at `:783-799` — a Heartfire pulse travelling through the roots, buildings catching the light — reads as designed for something rare. If the chain ticks fast, a floor has to be imposed, and its value is a design decision, not an engineering one.

### Q-CRON. A third cron, and at what granularity?
Two crons exist, both daily (`vercel.json:6-9`). If daily is the answer, worst-case pulse latency is 24 h and the player sees their pulse on the next login after that. If sub-hourly is wanted, that is a platform-plan question to check before committing. **Not answerable from this repo — check and record.**

---

## 5. What NOT to touch

- ⛔ **`api/admin/cleanup.js`'s retention sweep.** It deletes `web_trace` rows older than `RETENTION_DAYS = 7` (`:34`, `:79-85`). Do not attach pulse work to it; a new job gets its own route so a failure in one does not take the other down.
- ⛔ **`api/game/save.js`.** §0d. The pulse ledger is a table, never a save field.
- ⛔ **`achievement_grants`.** Cite its shape; do not reuse its rows. A pulse is not an achievement and overloading `achievement_id` with pulse ids would make its PK meaningless.
- ⛔ **The `CRON_SECRET` / `ADMIN_DASH_KEY` comparison.** `timingSafeEqual` over SHA-256 digests (`api/admin/cleanup.js:37-43`) is deliberate; a `===` is a timing oracle.
- Do not implement a queue or add a workflow dependency to solve retry. The spec's retry model (mark pending, re-scan next run) is satisfiable with a table and a cron, and adding infrastructure is a bigger decision than this ticket.
- No `.unity` scene files. No `SaveSchema` change.

---

## 6. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`vercel.json:6-9` (the two crons, quoted in full); `package.json:12-22` (no queue/workflow dependency)
`api/admin/cleanup.js:34,37-43,46-59,62-63,71-73,79-85`
`api/schema.sql:736-738,766-768,965-984,1127`
`api/game/save.js:70-72,410-421`
`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:26` (StakeConfig address), `:65` (share price at offset 137), `:87-94` (fail-closed-to-zero, the behaviour D4 must invert)
`api/tower-swap/log.js:70-127` (the `getTransaction` + failure-taxonomy precedent)
