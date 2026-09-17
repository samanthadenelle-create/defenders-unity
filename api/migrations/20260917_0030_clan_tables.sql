-- WO-1845 (clan step 2): the three clan tables.
--
-- Sequenced AFTER 20260917_0029_wallet_identity.sql, and that ordering is LOAD
-- BEARING, not cosmetic: clans.created_by_wallet, clan_members.wallet and
-- clan_messages.sender_wallet are all foreign keys onto wallet_identity(wallet).
-- Applied out of order, every one of them fails at migration time with 42P01.
-- The one runner (tools/run-migrations.mjs) derives its list in FILENAME ORDER,
-- so 0029 precedes 0030 by construction as long as nobody renames either file.
--
-- ONE CLAN PER WALLET, GLOBALLY, and that is an ENGINEERING DEFAULT rather than an
-- owner ruling (recorded as such in the work order): clan_members_one_clan_per_wallet
-- is a UNIQUE index on wallet ALONE, so the composite primary key permits many
-- members per clan while the index permits only one clan per member. It matches the
-- simplest version of every downstream ticket in this chain and can be revisited
-- if multi-clan membership is ever ruled in.
--
-- clan_messages EXISTS AND IS WRITTEN BY NOTHING in this ticket. Chat endpoints are
-- a later ticket; the table is declared now so its shape needs no second migration,
-- exactly as 0029 declared its three reserved columns.
--
-- gen_random_uuid() is a PostgreSQL 13+ builtin (no pgcrypto extension needed) and is
-- ALREADY the UUID primary-key default of account_deletion_requests, created by
-- 20260830_0014_account_deletion_requests.sql:4 against this same live instance
-- (PostgreSQL 17.11, as recorded by the 0029 lane).
--
-- Re-runnable: every statement is IF NOT EXISTS and this file INSERTs nothing, so it
-- is safe under a ledger re-apply or a --baseline run. The three CHECK constraints are
-- declared INLINE inside their CREATE TABLE bodies rather than as ALTER ... ADD
-- CONSTRAINT, which is what keeps them idempotent without the guard idiom 0010/0011/
-- 0012/0017 needed.
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

-- The one-clan-per-wallet invariant. The composite primary key above cannot express
-- it (it is satisfied by the same wallet appearing under two clan_ids), so the
-- invariant lives here and is enforced by the database rather than by a pre-SELECT
-- in the endpoint -- which is what makes two simultaneous joins land as one 23505
-- instead of two rows.
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
