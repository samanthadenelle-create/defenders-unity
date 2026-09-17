-- WO-1844 (clan step 1): the server-side wallet identity row.
--
-- ONE WALLET = ONE ROW, and nothing else. No profile fields, no multi-wallet
-- linking: a wallet switch orphans the old row, which is an owner-ruled accepted
-- cost, not a defect (see the work order). Populated fail-open by
-- api/_lib/wallet-auth.js touchWalletIdentity() on every PROVEN wallet-rail
-- authentication -- identity tracking may never block a request that would
-- otherwise succeed.
--
-- first_seen_staked_at / sgt_mint / sgt_verified_at are declared HERE and written
-- by NOTHING yet. They belong to later steps in this chain (the Vigil read and
-- Genesis Token binding). They exist now purely so the table shape does not need a
-- second migration later; no logic in WO-1844 touches them, and a row inserted by
-- this ticket's code leaves all three NULL.
--
-- Re-runnable: both statements are IF NOT EXISTS, so this file is safe under a
-- ledger re-apply or a --baseline run.
CREATE TABLE IF NOT EXISTS wallet_identity (
    wallet TEXT PRIMARY KEY,
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    first_seen_staked_at TIMESTAMPTZ NULL,
    sgt_mint TEXT NULL,
    sgt_verified_at TIMESTAMPTZ NULL
);

-- The Vigil's future working set: only the wallets that have ever been seen staked.
-- Partial because the overwhelming majority of rows will never carry the column, and
-- a partial index is the difference between scanning them and skipping them.
CREATE INDEX IF NOT EXISTS wallet_identity_first_seen_staked_idx
    ON wallet_identity (first_seen_staked_at)
    WHERE first_seen_staked_at IS NOT NULL;
