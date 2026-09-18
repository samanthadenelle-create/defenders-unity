# WO-1844 — Clan system, step 1: server-side wallet identity table + first-seen tracking

**Status:** DONE - committed 23cb59db8, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW — implemented 2026-09-17. Migration
`api/migrations/20260917_0029_wallet_identity.sql` + the `api/schema.sql` description +
`touchWalletIdentity()` in `api/_lib/wallet-auth.js` (one call site, in `authenticate()`'s wallet
branch, fail-open) + `test/wallet-identity.test.js` (14 cases). Suite: 908 tests / 906 pass, the one
failure pre-existing and unrelated (`heartbound-suite`: a Heartbound tier name readable in
`Assets/Editor/WallTools/RaidPostAudit.cs`). **Not applied to Neon** — the migration runs through
`tools/run-migrations.mjs` at deploy; until then `touchWalletIdentity` degrades to a logged warning
per request. Two items for the lead to rule on are recorded under "Open for the lead" at the bottom
of this file.

*(The status line previously read `**Status: READY TO IMPLEMENT**` — the colon inside the bold, which
`tools/board_build.py:248` flags as a NEAR_MISS_STATUS_MARKER rather than parsing. Corrected to the
canonical `**Status:**` form in the same edit.)*

## Context — this is the first of an 11-step chain, not an independent ticket

This is part of a sequential clan-system build spec drafted by DeepSeek and proofed this session
against the actual codebase. Full source material:
- `docs/CLAN_SYSTEM_DEEPSEEK_FACT_PACKET_2026-09-17.md` — the codebase facts this whole spec was
  built from.
- `docs/CLAN_WORK_ORDERS_PROOFING_2026-09-17.md` — the review pass, including corrections DeepSeek
  has since incorporated.
- `docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md` — the Vigil design this table is a
  prerequisite for.

This ticket is **clan WO-1** in that spec. It has no dependencies and is the prerequisite for every
other step (clan tables, the Vigil read, Genesis Token binding all key off this table). Do not
parallelize this against later steps in the chain — WO-1845 (clan WO-2) cannot start until this
lands and is verified.

## Scope

Create a `wallet_identity` table. Populate it on every successful `authenticate()` call in
`api/_lib/wallet-auth.js`. This is the prerequisite for the clan system, the Vigil read, and
Genesis Token binding.

## Non-scope

- No multi-wallet linking. One wallet = one row. Wallet switching orphans the old row (see the
  owner-ruled copy below — this is accepted, not a bug to fix here).
- No profile fields (display name, avatar). Only identity and timestamps.
- No migration of existing `player_data` rows. `wallet_identity` is a new, separate table.

## Schema (new migration file `api/migrations/<timestamp>_wallet_identity.sql`)

```sql
CREATE TABLE IF NOT EXISTS wallet_identity (
  wallet TEXT PRIMARY KEY,
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  first_seen_staked_at TIMESTAMPTZ NULL,
  sgt_mint TEXT NULL,
  sgt_verified_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS wallet_identity_first_seen_staked_idx
  ON wallet_identity (first_seen_staked_at)
  WHERE first_seen_staked_at IS NOT NULL;
```

`sgt_mint`/`sgt_verified_at` are included now (unused until clan WO-11) so the table shape doesn't
need a second migration later — but do not build any Genesis Token logic in this ticket.

## Implementation

Add to `api/_lib/wallet-auth.js` a new exported function `touchWalletIdentity(sql, wallet)`:
- Upsert on the `wallet` primary key.
- On insert: set `first_seen_at` and `last_seen_at` to `NOW()`.
- On conflict: update `last_seen_at` to `NOW()` only.
- Wrap in try/catch. On failure, log and continue (**fail-open**, matching the existing
  `touchGuestRate` pattern in this same file — read that function first and follow its shape).
- **Do not throw.** Identity tracking must never block a request that would otherwise succeed.

Call `touchWalletIdentity(sql, wallet)` from `authenticate()` and `authenticateGranting()`
immediately after `verifySession`/`verifyAndConsume` returns `{ok: true, wallet}`. **Not** from
`authenticatePromoRedeem()` — promo redemption is a narrower path and out of scope here.

## Acceptance criteria

- [ ] First call with a new wallet inserts a row with `first_seen_at` and `last_seen_at` set.
- [ ] Second call with the same wallet updates only `last_seen_at`.
- [ ] `first_seen_staked_at` remains NULL on both — it's populated by clan WO-9 (a later ticket in
  this chain), not here.
- [ ] A failed DB write does not cause the auth call to fail. Verify by temporarily dropping the
  table and confirming existing auth still succeeds.
- [ ] No existing route behavior changes when the table exists and is healthy.
- [ ] `node --test test/*.test.js` before/after counts reported. New test coverage added for
  `touchWalletIdentity`'s insert/update/fail-open behavior.

## Test plan

1. Insert a wallet, verify row.
2. Re-authenticate, verify `last_seen_at` advanced and `first_seen_at` did not.
3. Simulate a missing table (rename it), run a request through `authenticate()`, verify 200 and a
   logged warning.
4. Restore the table, verify no further errors.

## Rollback

Drop the table. Remove the `touchWalletIdentity` call sites. Auth continues to work without
identity tracking.

## VERIFY BEFORE BUILD

- Confirm Neon Postgres supports the `WHERE ... IS NOT NULL` partial index syntax used above (it
  does on Postgres 13+, but verify against the actual Neon instance in use).
- Confirm no existing table is already named `wallet_identity`. The fact packet confirms none
  exists as of this session, but re-grep `api/schema.sql` before writing the migration — state may
  have changed.

## Owner ruling — wallet switching

Deferred, not a gap to fix here. A wallet switch orphans the old clan membership. Any UI surfacing
this must state it honestly. Approved player-facing copy for later tickets that need it:

> "Your clan membership is tied to your wallet. Switching wallets will require rejoining."

## Copy rules (binding on this ticket and every later one in this chain)

No investment language anywhere player-facing. Never "earn," "yield," "return," "APY." Identity is
not a financial product.

---

## Open for the lead (implementation findings, 2026-09-17)

Two places where the spec above does not survive contact with the code. Both are implemented the
honest way and pinned by a test, so whichever way the lead rules the behaviour is written down rather
than discovered later.

**1. "Call from `authenticate()` AND `authenticateGranting()`, not `authenticatePromoRedeem()`" —
those three are not three verification points.** `authenticateGranting()` (`wallet-auth.js:830`) and
`authenticatePromoRedeem()` (`:916`) both *delegate* to `authenticate()` and neither proves anything
itself. So a single call inside `authenticate()`'s wallet branch satisfies the first two, and
**promo-redeem inherits it** — a WALLET-mode redeem does touch the row. A guest-mode redeem (the case
the promo exception exists for) does not, because the touch is gated on the wallet rail. The
alternative — a flag parameter to opt out — is refused by this file's own reasoning (a boolean makes
the safe answer the one you must remember to ask for), and refactoring the gate is out of scope.
Pinned by the two `promo redeem` cases in `test/wallet-identity.test.js`.

**2. The touch is gated on the WALLET rail only, not on "`verifySession` returned ok".**
`verifySession` also serves the `play-` rail (`:777`), and `first_seen_staked_at` / `sgt_mint` are
Solana concepts a `play-` id can never carry. Stated as an assumption, pinned by the `play- id` case.

**3. Acceptance criterion 4 (drop the table, confirm auth still succeeds) was NOT executed live, and
must not be** — firing DDL at live Neon from a lane is the WO-1505 defect. The code path is covered by
a throwing-client test instead (`a missing table degrades to a warning and NEVER rejects`). The live
check belongs after `tools/run-migrations.mjs` applies 0029. **Deploy-order consequence:** if the JS
ships before the migration is applied, every wallet auth logs one warning per request — noisy, not
breaking, which is the point of fail-open.

**4. Flagged, not built: account erasure does not yet delete from this table.** Migration 0027's own
header says *"Include it in account erasure"* for `play_identities`; `wallet_identity` is a new
wallet-keyed table and the deletion path (exercised by `test/account-deletion-request.test.js`) was
deliberately not touched — out of this ticket's scope. A `wallet_identity` row therefore survives an
account deletion request until a later step in this chain adds it.

**5. The touch is `await`ed on purpose.** Fire-and-forget would be cheaper, but on Vercel a floating
promise can be frozen the moment the response returns, so both "it actually writes" and "it fails open
loudly" depend on the await. Do not optimise it into a bare call.
