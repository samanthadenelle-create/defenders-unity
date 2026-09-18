-- WO-1853 (clan step 10): the perk ballot and the perks it activates.
--
-- Sequenced AFTER 20260917_0030_clan_tables.sql and 20260917_0029_wallet_identity.sql,
-- and that ordering is LOAD BEARING, not cosmetic: clan_ballots.clan_id and
-- clan_perks.clan_id are foreign keys onto clans(id), and clan_ballots
-- .proposed_by_wallet / clan_ballot_votes.wallet are foreign keys onto
-- wallet_identity(wallet). Applied out of order, every one of them fails at migration
-- time with 42P01. The one runner (tools/run-migrations.mjs) derives its list in
-- FILENAME ORDER, so 0029 and 0030 precede 0035 by construction as long as nobody
-- renames any of them.
--
-- THE THREE TABLE BODIES ARE THE SOURCE SPEC'S, CHARACTER FOR CHARACTER, from
-- docs/SKR Integtration.md:754-782 (WO-10) as quoted into
-- WorkOrders/WORK_ORDER_1853_five_tier_perk_ballot.md:32-61. Nothing was renamed,
-- re-typed or "improved": the work order calls this the exact shape, and a client and
-- three endpoints branch on these column names.
--
-- ⛔ THE ONE ADDITION IS AN INDEX, NOT A COLUMN, AND IT IS THE INVARIANT THE
--    ENDPOINT WOULD OTHERWISE ONLY ASK POLITELY FOR. Acceptance criterion 7 is "only
--    one open ballot per clan at a time; a second propose returns 409". A pre-SELECT in
--    the route cannot hold that under two simultaneous proposes — both read "none open",
--    both insert, and the clan now has two ballots and no way to say which is current.
--    clan_ballots_one_open_per_clan is a PARTIAL unique index (WHERE closed_at IS NULL),
--    so the database permits any number of CLOSED ballots per clan and exactly one open
--    one. Two racing proposes land as one row and one 23505, which the route answers as
--    the 409 the criterion asks for. This is the same reasoning — and the same shape —
--    as clan_members_one_clan_per_wallet in 0030.
--
-- ⛔ NO UNIQUE INDEX IS NEEDED FOR ONE-VOTE-PER-WALLET: clan_ballot_votes' PRIMARY KEY
--    (ballot_id, wallet) already is one, which is what lets the vote endpoint be a single
--    INSERT ... ON CONFLICT (ballot_id, wallet) DO UPDATE and never a read-then-write.
--    Re-voting updates option_id ALONE; weight and voted_at are the snapshot the work
--    order requires to be immovable (acceptance criterion 4).
--
-- ⛔ clan_perks' PRIMARY KEY (clan_id, tier) IS A ONE-PERK-PER-TIER RULE, and the write
--    path leans on it: a later ballot at the same tier lands as ON CONFLICT DO UPDATE and
--    REPLACES that tier's perk rather than erroring or silently doing nothing. That is an
--    engineering reading of a schema the work order fixed, not an owner ruling — recorded
--    as such in the work order's implementation record.
--
-- expires_at IS DECLARED AND ALWAYS WRITTEN NULL. The work order's VERIFY-BEFORE-BUILD
-- asks whether perks should expire and says to ship NULL and say so plainly if no ruling
-- exists. No ruling exists (searched 2026-09-17: no expiry ruling for clan perks in
-- CLI_LANES_WO_NUMBERS.md's WO-1853 banner note, which says only "fixed-anchor 48h
-- cadence per owner ruling"). The column exists so a future ruling needs no second
-- migration, exactly as 0029 declared its reserved columns.
--
-- gen_random_uuid() is a PostgreSQL 13+ builtin (no pgcrypto extension needed) and is
-- ALREADY the UUID primary-key default of clans (20260917_0030_clan_tables.sql:31)
-- against this same live instance.
--
-- Re-runnable: every statement is IF NOT EXISTS and this file INSERTs nothing, so it is
-- safe under a ledger re-apply or a --baseline run. The CHECK constraint is declared
-- INLINE inside the CREATE TABLE body rather than as ALTER ... ADD CONSTRAINT, which is
-- what keeps it idempotent without the pg_constraint guard idiom 0010/0011/0012/0017
-- needed. Nothing is dropped, deleted or altered, so tools/run-migrations.mjs
-- auditAdditive() passes it with no exemption.
CREATE TABLE IF NOT EXISTS clan_ballots (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
    proposed_by_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
    tier INTEGER NOT NULL,
    options JSONB NOT NULL,
    opened_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    closes_at TIMESTAMPTZ NOT NULL,
    closed_at TIMESTAMPTZ NULL,
    winning_option TEXT NULL,
    CONSTRAINT clan_ballots_tier_range CHECK (tier BETWEEN 1 AND 5)
);

-- The one-open-ballot-per-clan invariant. See this file's header: the composite key
-- cannot express it, and a pre-SELECT in the endpoint cannot hold it under a race.
CREATE UNIQUE INDEX IF NOT EXISTS clan_ballots_one_open_per_clan
    ON clan_ballots (clan_id) WHERE closed_at IS NULL;

-- GET /api/clan/ballot/current reads "the latest ballot for MY clan" on every call.
CREATE INDEX IF NOT EXISTS clan_ballots_clan_opened_idx
    ON clan_ballots (clan_id, opened_at DESC);

CREATE TABLE IF NOT EXISTS clan_ballot_votes (
    ballot_id UUID NOT NULL REFERENCES clan_ballots(id) ON DELETE CASCADE,
    wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
    option_id TEXT NOT NULL,
    voted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    weight NUMERIC NOT NULL,
    PRIMARY KEY (ballot_id, wallet)
);

CREATE TABLE IF NOT EXISTS clan_perks (
    clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
    tier INTEGER NOT NULL,
    perk_id TEXT NOT NULL,
    activated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NULL,
    PRIMARY KEY (clan_id, tier)
);
