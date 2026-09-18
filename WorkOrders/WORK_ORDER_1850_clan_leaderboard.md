# WORK ORDER 1850 — Clan system, step 7: real leaderboard fed by clan_vigil_weight

**Status:** READY FOR LEAD REVIEW

## Context — clan WO-7 in the chain, depends on WO-1848 (landed, committed 43fce9820)

Source: `docs/SKR Integtration.md` ("WO-7"), `CLI_LANES_WO_NUMBERS.md` banner (1850 row).
WO-1265's binding readiness sequence names "a real leaderboard" as one of its gate items — this
replaces whatever fake/client-only leaderboard exists today (per the original clan-system audit
this session: the pre-existing clan system was entirely client-side and fake).

## Scope

1. **Read the WO-1265 readiness sequence and the original clan audit first** — confirm at source
   what "real leaderboard" was actually specified to mean before building anything. Do not assume.
2. A `clan_vigil_weight` column or derived value is named in the banner as the ranking basis — this
   does NOT yet exist anywhere in the schema (verify via `grep -rn "vigil_weight" api/`). If it
   genuinely doesn't exist, this ticket's first job is defining the simplest honest metric available
   TODAY (member count is the only thing every clan currently has) and naming clearly, in the
   hand-back, that "Vigil weight" (the SKR staking-tenure concept from
   `docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`) is NOT wired in yet — that's WO-1852+
   territory (SKR Vigil read), which this ticket explicitly depends on being built LATER, not now.
   Build the leaderboard's SHAPE (endpoint + query + ranking) so swapping in a real Vigil-weight
   column later is a one-column change, not a rewrite.
3. New read-only endpoint (likely `GET /api/clan/leaderboard`, following the existing `api/clan/*.js`
   pattern) returning ranked clans by whatever metric this ticket lands on (member count as the
   honest placeholder, ORDER BY + LIMIT, paginated the same way other list endpoints in this repo are).
4. No new writes, no new tables beyond what's already there unless the metric genuinely requires one
   — flag rather than assume.

## Non-scope

- Do NOT build the actual SKR Vigil-weight calculation — that's WO-1852, which depends on WO-1851
  (the gate) being open first, per the banner's own dependency chain. This ticket produces a
  leaderboard SHAPE ready to receive that column later.
- Do NOT touch WO-1851 (gate/join-policy) or WO-1858 (chat wiring) — file-disjoint, in progress.
- No client/Unity UI for displaying the leaderboard — that's implied future scope, not this ticket
  (server endpoint only, per the pattern every other clan WO in this chain has followed).

## Acceptance criteria

- [ ] `GET /api/clan/leaderboard` (or whatever name matches existing convention) returns clans
      ranked by the chosen honest-today metric, with clan name/tag/member count, never a wallet
      address (per the standing "never render a wallet" rule already enforced elsewhere in this
      admin/API surface).
- [ ] The query/response shape is documented as accepting a future ranking-column swap without an
      API shape change (or the hand-back explains why that's not achievable and what would need to
      change later).
- [ ] Auth: read-only, but still routes through `authenticate()` per the existing clan-endpoint
      convention (verify whether a leaderboard should require auth at all, or be public — flag if
      genuinely ambiguous rather than picking silently).
- [ ] New tests, `node --test test/*.test.js` before/after counts reported.
- [ ] No wallet address ever appears in the response — pin this with a test, the same way
      `api/admin/stats.js` already pins it elsewhere in this codebase.

## Test plan

1. Seed 3+ clans with varying member counts via the existing create/join endpoints. Call the
   leaderboard endpoint; confirm correct ranking order.
2. Confirm response contains no wallet address anywhere, at any nesting depth.
3. Full regression suite green.

## Rollback

Delete the new endpoint file. No schema changes unless the metric genuinely requires one (flagged
above); if none were added, rollback is a straight file deletion.

## Copy rules

No investment/jargon language. Ranking is a game leaderboard, not a financial ranking.

## IMPLEMENTATION RECORD (2026-09-17)

**Read at source before building, per scope item 1:**
- `WorkOrders/WORK_ORDER_1265_gate_local_clan_chat_until_backend_ready.md` — readiness sequence
  item 5: "Add the clan leaderboard near the beginning of the player clan experience once
  server-owned clan identities and ranking inputs exist; do not derive it from the local
  PlayerPrefs stub." Acceptance: "Existing leaderboard remains independent and untouched."
- `docs/SKR Integtration.md:511-559` — the actual WO-7 draft ("Clan leaderboard fed by real
  identities"), which is what this ticket (WO-1850, "clan WO-7 in the chain") implements. It
  specifies the fallback metric PRECISELY: `member_count × days_since_created`, tie-broken by
  `created_at ASC`, response shape `{ rank, clanId, name, tag, metric, memberCount }`, default
  limit 50 / max 100, and `authenticate()` (read-only). This is more specific than this WO's own
  paraphrase ("member count as the honest placeholder") and was followed as the sourced spec
  rather than the paraphrase, since it is the ticket's own named source and carries a concrete,
  testable acceptance example ("a clan with one member and one day old ranks below a clan with
  three members and ten days old").
- `docs/CLAN_WORK_ORDERS_PROOFING_2026-09-17.md` — flags that WO-7's ranking metric is a
  first-pass engineering default, NOT an owner ruling. Recorded here for the same reason: nobody
  downstream should treat `member_count × days_since_created` as settled beyond challenge.

**`clan_vigil_weight` verified absent, per scope item 2:**
`grep -rn "clan_vigil_weight" api/` returns zero hits (checked again post-implementation as a
pinned regression in `test/clan-leaderboard.test.js`, which also greps every file under
`api/migrations/`). `vigil_weight` (without the `clan_` prefix) appears only in planning docs
(`docs/SKR Integtration.md`, `docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`) — never in
code or a migration. **The real SKR Vigil-weight calculation is WO-1852+ scope and was NOT
built here.**

**What shipped:**
- `api/_lib/clan.js` — added `getLeaderboard(sql, limit)`, `clampLeaderboardLimit(raw)`,
  `LEADERBOARD_DEFAULT_LIMIT` (50), `LEADERBOARD_MAX_LIMIT` (100). One query: a `ranked` CTE
  computes `member_count` (subquery over `clan_members`) and `days_since_created`
  (`EXTRACT(EPOCH FROM (NOW() - created_at)) / 86400`, floored at 0), then the outer SELECT
  computes `metric = member_count * days_since_created` and ranks with
  `ROW_NUMBER() OVER (ORDER BY metric DESC, created_at ASC)`. No wallet column is ever selected.
- `api/clan/leaderboard.js` — new route, `GET /api/clan/leaderboard?limit=N`. Routes through the
  SAME shared preamble every other clan route uses (`beginClanRequest`, no rate-limit `action`
  since this spends no budget), which forces `auth.mode === 'wallet'` exactly like create/join/
  leave/me/promote/demote/kick. **Flag, per acceptance criterion 3's "flag if genuinely
  ambiguous":** the WO-7 draft only asked for `authenticate()` (any proven identity), not
  specifically wallet-mode; this route reuses `beginClanRequest` anyway because (a) it reads the
  same wallet-gated `clans`/`clan_members` tables every other clan route reads, (b) it is what
  makes "the same error shape as other clan endpoints" true by construction rather than by two
  copies staying in sync, and (c) nothing in this ticket needs a non-wallet identity to work. If
  a future ticket wants the leaderboard open to guest/play- identities, that is a one-line change
  to swap `beginClanRequest` for a bare `authenticate()` call — flagged here rather than decided
  silently.
- `test/clan-leaderboard.test.js` — new, standalone (not added to `test/clan-membership.test.js`,
  to stay file-disjoint from the concurrent WO-1851/WO-1858 lanes touching that shared test file).
  13 tests: the query shape and metric, empty-result handling, limit clamping, the authenticated
  happy path, the unauthenticated-401/guest-401 shapes (identical to every other clan route), the
  OPTIONS preflight, a no-wallet-anywhere-in-the-response assertion (mirrors `api/admin/stats.js`'s
  own pin), and two source-level guards: the leaderboard code never references the unrelated
  leaderboard's table, and `clan_vigil_weight` is absent from both the query and every migration.

**Future-swap shape (acceptance criterion 2):** the ranking column lives in exactly one SQL
expression (`member_count * days_since_created` in `getLeaderboard`'s two SELECT clauses).
Swapping in a real `clan_vigil_weight` column later (WO-1852+, once WO-9's schema lands) means
replacing that one expression — the route, response shape (`rank/clanId/name/tag/metric/
memberCount`), ORDER BY direction, and `created_at ASC` tie-break all stay as they are. No API
shape change.

**Verification:**
- `node --check api/clan/leaderboard.js` → OK. `node --check api/_lib/clan.js` → OK.
  `node --check test/clan-leaderboard.test.js` → OK.
- `node --test test/*.test.js` — **before: 1034 tests, 1032 pass, 1 fail, 1 todo. After: 1047
  tests, 1045 pass, 1 fail, 1 todo.** The one failure (`heartbound-suite.test.js` /
  `heartbound-contract.test.js`, `RaidPostAudit.cs` / streak-column findings) is PRE-EXISTING,
  unrelated to this ticket (Heartbound/wall-durability system, not clan), and unchanged by this
  change — file-disjoint confirmed by diff (this ticket touched only `api/_lib/clan.js`,
  `api/clan/leaderboard.js`, `test/clan-leaderboard.test.js`, and this WO file).
- `node --test test/clan-leaderboard.test.js` alone → 13/13 pass.

**Not committed** — per instructions, left for lead review/gate/commit.
