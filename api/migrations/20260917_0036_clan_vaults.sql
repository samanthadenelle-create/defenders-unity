-- WO-1854 (clan step 11): the Collective Vigil — a clan's Squads multisig vault,
-- and the Seeker Genesis Token binding that makes its signers hardware-proven.
--
-- ⛔ THIS FILE IS 0036, NOT 0035, AND THAT IS NOT A GAP TO BE TIDIED. WO-1853 (the
-- five-tier perk ballot) was being implemented in the SAME WORKING TREE while this
-- landed and had already written 20260917_0035_clan_ballots.sql (seen untracked in
-- `git status --short api/migrations/` on 2026-09-17). tools/run-migrations.mjs:280
-- derives its list as `readdirSync(dir).filter(.sql).sort()` — FILENAME ORDER — and
-- the ledger keys on the filename, so two files claiming 0035 would not collide on
-- identity but WOULD make the applied order depend on the rest of the name. The
-- number was taken by reading the directory, exactly as CLAUDE.md §2 says a WO number
-- is taken by reading the banner rather than by counting.
--
-- Sequenced AFTER 20260917_0030_clan_tables.sql (clan_vaults.clan_id is a foreign key
-- onto clans(id)) and AFTER 20260917_0029_wallet_identity.sql (the partial unique
-- index below is on that table). Both precede this by filename, by construction.
--
-- Re-runnable: every statement is IF NOT EXISTS and this file INSERTs nothing, so it
-- is safe under a ledger re-apply or a --baseline run. The CHECK constraints are
-- declared INLINE inside the CREATE TABLE body rather than as ALTER ... ADD
-- CONSTRAINT, which is what keeps them idempotent without the guard idiom
-- 0010/0011/0012/0017 needed — the same choice 0030 made and for the same reason.

-- ── The vault row ────────────────────────────────────────────────────────────
-- ONE VAULT PER CLAN (clan_id is the PRIMARY KEY) and one clan per vault
-- (vault_address is UNIQUE). Both halves are deliberate: a clan with two vaults has
-- no single collective Vigil to report, and one vault backing two clans would let a
-- single treasury be counted twice on the same leaderboard.
--
-- ⚠ TWO ADDRESSES ARE STORED, AND THE ASYMMETRY IS THE WHOLE POINT. A Squads
-- "vault" is a PDA that HOLDS assets and has no data of its own; the MULTISIG account
-- is the one that carries the signer list and the threshold. Verified on mainnet
-- 2026-09-17: multisig JEJJhPFUoTzFsAQAoFYUEQDmFUPJjCifHfLgn3V9nYJq (231 bytes, owned
-- by SQDS4ep65T869zMMBKyuUq6aD6EgTu8psMjkvj52pCf) decodes to threshold 2 / 3 members,
-- and its vault index 0 is DUQCftnDJJc4DoqyjPiZETPn9o4EBhwAGrUJoniQgg12 — a different
-- address entirely, which is where any SKR would actually sit. Storing only one of the
-- two would mean re-deriving or reverse-deriving the other, and a vault PDA cannot be
-- reversed to its multisig at all.
--
-- ⚠ multisig_address, vault_index AND first_seen_staked_at ARE ADDITIVE TO THE WORK
--   ORDER'S SQL, which named only (clan_id, vault_address, signer_wallets, threshold,
--   verified_at). Flagged in the implementation record rather than slipped in:
--     * multisig_address — see the paragraph above; without it the signer list can
--       never be RE-READ from the chain, only trusted from this table forever.
--     * vault_index — Squads allows 0..255 vaults per multisig (getVaultPda asserts
--       exactly that range). Defaulting to 0 keeps the work order's single-address
--       body working while recording WHICH vault the stored address is.
--     * first_seen_staked_at — the collective Vigil is percent x TENURE, and a vault
--       PDA has no wallet_identity row to carry a tenure origin. Stamped on the first
--       positive stake observation, which is WO-1852's own "tenure counts from the
--       FIRST OBSERVATION" ruling applied to a vault instead of a member. Using
--       verified_at instead would credit the clan for every second between registering
--       an empty vault and funding it. ⛔ FIRST-PASS DEFAULT, NOT AN OWNER RULING.
--
-- ⛔ THERE IS NO hardware_backed COLUMN, ON PURPOSE. Whether every signer still holds
-- a verified Genesis Token is DERIVED at read time by joining signer_wallets against
-- wallet_identity.sgt_verified_at. A stored boolean would be duplicated state that
-- goes stale the moment a binding is cleared — the failure CLAUDE.md §2/§5/§8/§16 each
-- pay for in its own words.
CREATE TABLE IF NOT EXISTS clan_vaults (
    clan_id UUID PRIMARY KEY REFERENCES clans(id) ON DELETE CASCADE,
    vault_address TEXT NOT NULL UNIQUE,
    multisig_address TEXT NOT NULL,
    vault_index SMALLINT NOT NULL DEFAULT 0,
    signer_wallets JSONB NOT NULL,
    threshold INTEGER NOT NULL,
    verified_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    first_seen_staked_at TIMESTAMPTZ NULL,
    CONSTRAINT clan_vaults_threshold_range CHECK (threshold >= 1),
    CONSTRAINT clan_vaults_signers_is_array CHECK (jsonb_typeof(signer_wallets) = 'array'),
    CONSTRAINT clan_vaults_index_range CHECK (vault_index BETWEEN 0 AND 255)
);

-- ── THE ONE-DEVICE-ONE-WALLET INVARIANT, ENFORCED BY THE DATABASE ────────────
-- ⛔ THIS INDEX IS WHY `sgt_reused` CANNOT BE RACED, and it is the reason the check is
-- not a bare SELECT-then-UPDATE in the endpoint. A Seeker Genesis Token is minted once
-- per DEVICE (Solana Mobile's own wording, confirmed at
-- docs.solanamobile.com/marketing/engaging-seeker-users on 2026-09-17), so one SGT mint
-- may be bound to at most one wallet in this game. Read first and written second, two
-- concurrent verifications of the SAME mint by two different wallets BOTH see "nobody
-- has it" and BOTH write — which is precisely the Sybil hole the check exists to close.
-- With the index, the second write is a 23505 and api/_lib/wallet-auth.verifyGenesisToken
-- classifies it as sgt_reused. Exactly the shape clan_members_one_clan_per_wallet uses
-- for one-clan-per-wallet (0030) and consumeNonce uses for a replayed nonce: the
-- database is the authority, the SELECT only labels the refusal.
--
-- PARTIAL because the overwhelming majority of wallet_identity rows will never carry an
-- sgt_mint, and NULL is not unique to itself in Postgres anyway — the WHERE clause makes
-- that explicit rather than incidental, and keeps the index the size of the Seeker
-- population instead of the size of the player base. Same shape as
-- wallet_identity_first_seen_staked_idx (0029).
CREATE UNIQUE INDEX IF NOT EXISTS wallet_identity_sgt_mint_unique
    ON wallet_identity (sgt_mint)
    WHERE sgt_mint IS NOT NULL;

-- The vault's signers are read back by wallet, so the reverse lookup ("is this wallet a
-- signer on some clan's vault") has an index rather than a JSONB scan of every row.
CREATE INDEX IF NOT EXISTS clan_vaults_signer_wallets_idx
    ON clan_vaults USING GIN (signer_wallets);

-- One clan per multisig, not merely per vault address: two vault indices of the SAME
-- multisig are two different addresses and would otherwise both be registerable, to two
-- different clans, sharing one signer set and one governance.
CREATE UNIQUE INDEX IF NOT EXISTS clan_vaults_multisig_unique
    ON clan_vaults (multisig_address);
