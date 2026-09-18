-- WO-1851 (clan WO-8): closes the gap 20260917_0030_clan_tables.sql left open — the
-- `clans.join_policy` column has a DEFAULT ('invite') but no CHECK, so any string could
-- have been written to it. The owner ruling (WO-1851, 2026-09-17) fixes the vocabulary at
-- exactly two values: 'invite' (join requires the clan's code — the current, unchanged
-- default) and 'open' (anyone may join without a code — not built on the server yet;
-- storing it correctly now avoids a second migration once the join-without-code behavior
-- ships). This migration only adds the constraint; it does not change the column, its
-- default, or any existing row's value.
--
-- Guard idiom copied from 20260828_0006_db_promo_pack_fks.sql (a `pg_constraint` existence
-- check inside a DO block), which is this file set's existing pattern for adding a
-- constraint to a table that may already have been migrated by a prior run — an
-- `ALTER TABLE ... ADD CONSTRAINT` has no `IF NOT EXISTS` form in Postgres, so the guard is
-- what makes the statement idempotent under a ledger re-apply or a --baseline run.
--
-- Safe to add now with no data migration: api/_lib/clan.js's createClan is the only writer
-- of `join_policy` (the INSERT's column list, `20260917_0030_clan_tables.sql`-era code) and
-- it has never written anything but the column DEFAULT — the CTE never named join_policy in
-- its INSERT before this ticket's companion clan.js change, and that change writes only
-- 'invite' or 'open'. Every existing row is therefore already 'invite' and the CHECK cannot
-- reject anything already on disk.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'clans_join_policy_valid') THEN
        ALTER TABLE clans ADD CONSTRAINT clans_join_policy_valid
            CHECK (join_policy IN ('invite', 'open'));
    END IF;
END $$;
