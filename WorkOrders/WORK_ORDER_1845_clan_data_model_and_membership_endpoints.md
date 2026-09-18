# WO-1845 — Clan system, step 2: clan data model + create/join/leave endpoints

**Status:** DONE - committed 4d944f98f, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW

## Context — clan WO-2 in the chain, depends on WO-1844

Source material: `docs/CLAN_SYSTEM_DEEPSEEK_FACT_PACKET_2026-09-17.md`,
`docs/CLAN_WORK_ORDERS_PROOFING_2026-09-17.md`, `docs/SKR Integtration.md` (DeepSeek's original
draft — this ticket is its "WO-2"). Depends on WO-1844 (`wallet_identity` table) already landed and
committed. WO-1846 (roles/succession/rate limits) depends on this ticket.

## Scope

Create `clans`, `clan_members`, and `clan_messages` tables. Implement `POST /api/clan/create`,
`POST /api/clan/join`, `POST /api/clan/leave`, `GET /api/clan/me`. Server-side invite-code
generation (replaces the client's local `GenerateClanCode()`, which never validated anything).

## Non-scope

- No chat endpoints. `clan_messages` is created but unused until the chat ticket.
- No role assignment beyond Leader. Officer/Member assignment is WO-1846.
- No leader succession. Leaving as Leader is blocked until WO-1846 defines the rule.
- No rate limits beyond whatever generic ones already exist. Clan-specific rate limits are WO-1846.
- No leaderboard. That's a later ticket.

## Schema (new migration file `api/migrations/<timestamp>_clan_tables.sql`)

```sql
CREATE TABLE IF NOT EXISTS clans (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  tag TEXT NOT NULL,
  join_policy TEXT NOT NULL DEFAULT 'invite',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_by_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  CONSTRAINT clans_code_format CHECK (code ~ '^[A-HJ-NP-Z2-9]{6}$'),
  CONSTRAINT clans_name_len CHECK (char_length(name) BETWEEN 1 AND 32),
  CONSTRAINT clans_tag_len CHECK (char_length(tag) BETWEEN 1 AND 5)
);

CREATE TABLE IF NOT EXISTS clan_members (
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  role TEXT NOT NULL DEFAULT 'member',
  joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (clan_id, wallet),
  CONSTRAINT clan_members_role_valid CHECK (role IN ('leader','officer','member'))
);

CREATE UNIQUE INDEX IF NOT EXISTS clan_members_one_clan_per_wallet
  ON clan_members (wallet);

CREATE TABLE IF NOT EXISTS clan_messages (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  sender_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  phrase_id TEXT NULL,
  text TEXT NULL,
  sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT clan_messages_has_body CHECK (phrase_id IS NOT NULL OR text IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS clan_messages_clan_sent_idx
  ON clan_messages (clan_id, sent_at DESC);
```

`clan_members_one_clan_per_wallet` enforces one clan per wallet globally — this is a first-pass
engineering default, not an owner ruling. Flag it in your hand-back so it's on record, but build to
it; it matches the simplest version of every downstream ticket (Vigil, ballot) and can be revisited
later if the owner wants multi-clan membership.

## Endpoint behavior

**`POST /api/clan/create`** — body `{ name, tag }`. Auth: `authenticate()`. Reject if wallet already
in a clan. Generate server-side invite code: 6 chars from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, retry
on collision (max 10 attempts, then 500). Insert `clans` row, insert `clan_members` row with
`role='leader'`. Return `{ clanId, code, name, tag, role: 'leader' }`.

**`POST /api/clan/join`** — body `{ code }`. Auth: `authenticate()`. Reject if wallet already in a
clan. Look up clan by code (uppercase-normalized). 404 if not found. Insert `clan_members` row with
`role='member'`. Return `{ clanId, name, tag, role: 'member' }`.

**`POST /api/clan/leave`** — Auth: `authenticate()`. Reject if wallet is Leader (until WO-1846
defines succession) — return 409 with `{ error: 'leader_must_transfer' }`. Delete the
`clan_members` row. If the clan has zero members after, delete the `clans` row. Return
`{ ok: true }`.

**`GET /api/clan/me`** — Auth: `authenticate()`. Return the caller's clan membership + clan info,
or `{ clan: null }`.

## Acceptance criteria

- [ ] Create returns a valid code matching the format constraint. Code is unique.
- [ ] Join with a valid code adds the wallet as Member.
- [ ] Join with an invalid code returns 404.
- [ ] Join when already in a clan returns 409.
- [ ] Leave as Member removes the row. If last member, clan is deleted.
- [ ] Leave as Leader returns 409 with the correct error code.
- [ ] `GET /api/clan/me` returns null for a wallet with no clan, populated for one with a clan.
- [ ] All endpoints reject unauthenticated requests with the same error shape as existing routes.
- [ ] `node --test test/*.test.js` before/after counts reported, new tests for every endpoint.

## Test plan

1. Create two wallets. Wallet A creates a clan. Wallet B joins via code. Verify both memberships.
2. Wallet B leaves. Verify Wallet A is still Leader and the clan still exists.
3. Wallet A attempts to leave. Verify 409.
4. Attempt join with a code containing excluded characters (e.g. `O`/`0`/`I`/`1`). Verify 400 from
   the format constraint.
5. Attempt create with a name longer than 32 characters. Verify 400.

## Rollback

Drop the three tables. No existing data is touched. The client stub still reads from PlayerPrefs
until the gate-opening ticket.

## VERIFY BEFORE BUILD

- Confirm `gen_random_uuid()` is available on the Neon instance (it is on Postgres 13+, but verify).
- Confirm the `wallet_identity` foreign keys are satisfiable — WO-1844's migration must be applied
  before this one runs, or these FKs fail at migration time. Sequence matters; do not apply this
  migration out of order.
- Confirm the client's existing `ClanState` JSON shape maps cleanly to these tables (per the fact
  packet: `{Id, Code, Name, Tag, JoinPolicy, CreatedAtUnix, Members[], Messages[]}`). Fields map 1:1
  except `JoinPolicy` (hardcoded to `'open'` client-side, defaulting to `'invite'` server-side) —
  flag this discrepancy in your hand-back rather than silently picking one; it needs a ruling before
  the gate-opening ticket, not here.

## Copy rules

No investment language. Clan membership is not a financial product. Invite codes are not
transferable assets.

---

## IMPLEMENTATION RECORD (2026-09-17) — files, proof, and the flags the lead must rule on

**Files written**
- `api/migrations/20260917_0030_clan_tables.sql` — the schema exactly as specified.
- `api/schema.sql` — description block appended (CRLF preserved: 2292 LF / 2292 CRLF measured after).
- `api/_lib/clan.js` — all logic, sql injected.
- `api/_lib/clan-http.js` — the shared CORS/body/auth preamble (ONE copy, not four).
- `api/clan/create.js`, `api/clan/join.js`, `api/clan/leave.js`, `api/clan/me.js`.
- `test/clan-membership.test.js` — 40 cases.

**Test run:** before 908 tests / 906 pass / 1 fail / 1 todo → after 948 / 946 / 1 / 1. The one
failure is pre-existing and unrelated: two red assertions, both Heartbound, identical before and after.
`heartbound-contract.test.js:272` carries `{ todo: 'WO-1693 finding 2 …' }` (read at source), so it is
the counted `todo`; the counted `fail` is `heartbound-suite.test.js:251`. Neither file is touched here.

**What was NOT executed, stated plainly:** the three data-modifying CTEs (create, join, leave) have run
only against a recording tagged-template mock. No Postgres has parsed or executed them, so their
behaviour is reasoned-from-the-manual, not measured — specifically the untyped-`$n` coercion in
`INSERT … SELECT id, $2, $3 FROM new_clan` and the cascade interaction in the leave statement. Cheapest
close: once `run-migrations.mjs` lands 0030, one create → join → leave smoke against preview.

**NO DDL WAS RUN AGAINST LIVE NEON** (WO-1505 discipline). The migration is verified statically only:
`auditAdditive` from `tools/run-migrations.mjs` returns `[]` for it (asserted in the test file), and the
runner's "exactly TWO non-re-runnable migrations" oracle stays green, so 0030 adds no re-run hazard.

### FLAG 1 — `JoinPolicy`: the two sides do not share a VOCABULARY, not just a default
Not merely `'open'` vs `'invite'`. `ClanService.cs:85` declares the field as `'open' | 'closed'` and
`:172` hardcodes `"open"`; this server defaults `'invite'`, and **the migration puts no CHECK on
`join_policy` at all**, so any string is storable. Three values across two systems with no constraint.
Nothing was silently picked: the column carries the specified `'invite'` default, `/create` does not
accept a `joinPolicy` input, and `/me` + `/join` return the stored value verbatim. **Needs a ruling
(vocabulary + default + whether a CHECK is added) before the gate-opening ticket.**

### FLAG 2 — tag length: client 2–4, server 1–5
`ClanService.CreateClan` truncates to 4 (`ClanService.cs:160`) and its comment says "2–4 char clan
tag"; `clans_tag_len` permits 1–5. A server-created 5-char tag is one the client was never built to
render. Server enforces its own schema; unreconciled on purpose.

### FLAG 3 — one clan per wallet is an engineering default, not a ruling
`clan_members_one_clan_per_wallet` is a UNIQUE index on `wallet` alone. Built as the WO instructed;
on the record here so multi-clan membership stays a live option.

### FLAG 4 — a spec gap filled by existing convention, not invented
The WO's bodies (`{name, tag}` / `{code}`) name no identity field, but `authenticate()` routes by the
SHAPE of the id being acted on and cannot be called without one. The preamble reads `body.playerId`
for POST (as `referral/claim.js:101` and `game/save.js:317` do) and `query.playerId` for GET (as
`game/load.js:106` does), with `X-Wallet` as a last resort. No weaker path: whatever arrives still has
to survive `authenticate()`.

### FLAG 5 — the routes require `auth.mode === 'wallet'`, which the WO did not say
Every clan wallet column is a foreign key onto `wallet_identity`, and ONLY the wallet rail ever writes
that table (`touchWalletIdentity`, called from `authenticate()` on that rail alone). A guest- or
play-shaped identity reaching any clan INSERT is a 23503 surfacing as a 500, never a membership. The
preamble therefore refuses non-wallet modes with `AUTH_WALLET_REQUIRED`. It is **not**
`authenticateGranting()` — that allowlist admits `google` (a `play-` id), the exact shape that cannot
satisfy these keys.

### FLAG 6 — 409 is a new status code for this API
`api/_lib/http.js` documents the project constraint as "200 | 400 | 401 | 404 | 500" and
`grep -rn "status(409)" api/` returned nothing before this lane. The WO specifies 409 explicitly, so
409 it is — flagged because it widens that constraint.

### FLAG 7 — the empty-clan cleanup is unreachable today
It fires only for the LAST non-leader member, and a leader can never leave, so no ordinary flow
reaches it until WO-1846 defines succession. Implemented because the WO specifies it; noted so it is
not later read as dead code and removed.

**`gen_random_uuid()` — confirmed, with evidence:** already the UUID primary-key default of
`account_deletion_requests`, created by `api/migrations/20260830_0014_account_deletion_requests.sql:4`
against this same live instance (PostgreSQL 17.11, as recorded by the WO-1844 lane). It is a
PostgreSQL 13+ builtin needing no extension.
