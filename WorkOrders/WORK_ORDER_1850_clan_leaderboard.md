# WORK ORDER 1850 — Clan system, step 7: real leaderboard fed by clan_vigil_weight

**Status:** READY TO IMPLEMENT

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
