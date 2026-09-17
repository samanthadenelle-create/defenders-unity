-- WO-1846 (clan step 3): the per-wallet, per-action clan budget.
--
-- Written by api/_lib/wallet-auth.js touchClanRate(), called from the ONE shared clan
-- preamble (api/_lib/clan-http.js beginClanRequest) for the six actions that change
-- state: create, join, leave, promote, demote, kick. GET /api/clan/me is a pure read
-- and deliberately spends nothing.
--
-- THE SHAPE IS guest_rate_limit's, READ AT SOURCE (api/schema.sql:333-343) RATHER THAN
-- ASSUMED -- that table is (guest_id PK, window_started_at, hits, total_hits,
-- last_seen), and the only change here is the KEY: a composite (wallet, action) instead
-- of a single id, because six actions with six different budgets cannot share one
-- counter. One wallet therefore owns at most six rows.
--
-- ⛔ NO FOREIGN KEY ONTO wallet_identity, AND THAT IS DELIBERATE, NOT AN OVERSIGHT.
--    guest_rate_limit has none either, for the same reason: touchWalletIdentity is
--    FAIL-OPEN (it degrades to a warning when migration 0029 is missing), so an FK here
--    would turn a degraded identity touch into a 23503 inside the rate limiter -- i.e.
--    a missing identity row would become a refused clan request. A budget counter must
--    never be able to deny a request the auth rail already approved.
--
-- ⛔ AND THE WRITER IS FAIL-OPEN TOO: if this file has not been applied, touchClanRate
--    catches, logs '[wallet-auth] clan rate table unavailable', and ALLOWS the request.
--    Rate limiting is abuse control, not authorization. The deploy-order hazard this
--    repo has paid for before (auth_sessions.identity_kind 500'ing every session mint
--    for a week) cannot recur through this table.
--
-- Re-runnable: both statements are IF NOT EXISTS and this file INSERTs nothing, so it is
-- safe under a ledger re-apply or a --baseline run. Nothing is dropped, deleted or
-- altered, so tools/run-migrations.mjs auditAdditive() passes it with no exemption.
CREATE TABLE IF NOT EXISTS clan_rate_limit (
    wallet            TEXT        NOT NULL,               -- base58 Solana address, PROVEN before we get here
    action            TEXT        NOT NULL,               -- create | join | leave | promote | demote | kick
    window_started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), -- start of the current 3600s window
    hits              INTEGER     NOT NULL DEFAULT 0,     -- requests inside the current window
    total_hits        BIGINT      NOT NULL DEFAULT 0,     -- lifetime requests (abuse signal)
    last_seen         TIMESTAMPTZ NOT NULL DEFAULT NOW(), -- drives the cleanup sweep
    PRIMARY KEY (wallet, action)
);

-- Sweep idle rows the way api/admin/cleanup.js sweeps guest_rate_limit.
CREATE INDEX IF NOT EXISTS clan_rate_limit_last_seen_idx
    ON clan_rate_limit (last_seen);
