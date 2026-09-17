-- WO-1847 (clan step 4): the clan message-report table.
--
-- Sequenced AFTER 20260917_0029_wallet_identity.sql and 20260917_0030_clan_tables.sql,
-- and that ordering is LOAD BEARING, not cosmetic: clan_reports.reporter_wallet is a
-- foreign key onto wallet_identity(wallet) and clan_reports.clan_id onto clans(id).
-- Applied out of order, both fail at migration time with 42P01. The one runner
-- (tools/run-migrations.mjs) derives its list in FILENAME ORDER, so 0029 and 0030
-- precede 0031 by construction as long as nobody renames any of the three files.
--
-- ⛔ WHY THIS TABLE EXISTS, AND WHY IT IS WRITE-ONLY TODAY. WO-1265 — this project's
-- standing ruling on clan chat — required moderation and reporting to exist before
-- free-text messaging shipped. WO-1847 ships Cherry's embedded chat WITH free text,
-- under an explicit owner ruling that waives the pre-ship rate-limit gate for a
-- pre-revenue hackathon demo. This table is the mitigation that keeps the underlying
-- condition STRUCTURALLY true: a report has somewhere to land from the first build.
-- Nothing reads it in this ticket, and that is scope, not an oversight — the admin
-- review surface is deferred alongside the admin clan-health ticket.
--
-- ⚠ message_id IS TEXT AND DELIBERATELY NOT A FOREIGN KEY. Cherry owns message
-- persistence and delivery; this game's database never sees a clan_messages row for
-- an embedded message (clan_messages, declared by 0030, is still written by nothing).
-- A foreign key here would reference a table that cannot contain the id, so the column
-- records the external identifier as the opaque string it is. Validation of its SHAPE
-- is therefore the endpoint's job, not the schema's — see reportMessage in
-- api/_lib/clan.js.
--
-- ON DELETE CASCADE on clan_id, matching clan_members and clan_messages in 0030: when
-- a clan is removed its reports go with it. reporter_wallet deliberately does NOT
-- cascade — wallet_identity rows are never removed, and a report must outlive any
-- later churn in the reporter's membership.
--
-- gen_random_uuid() is a PostgreSQL 13+ builtin (no pgcrypto extension needed) and is
-- already the UUID primary-key default of clans and clan_messages, created by
-- 20260917_0030_clan_tables.sql against this same live instance.
--
-- Re-runnable: every statement is IF NOT EXISTS and this file adds no rows, so it is safe
-- under a ledger re-apply or a --baseline run, and it is purely additive — it removes
-- nothing and alters nothing — which is what the one runner's additive audit in
-- test/migrations.runner.test.js requires. The prose avoids the destructive keywords
-- themselves, as 0030 does, so no stricter reading of this file can ever trip on a
-- comment describing what the file does not do.
CREATE TABLE IF NOT EXISTS clan_reports (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reporter_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
    message_id TEXT NOT NULL,
    clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
    reported_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- The index a review tool will read the table by: newest reports for one clan. Declared
-- now with the table so the deferred admin surface needs no second migration.
CREATE INDEX IF NOT EXISTS clan_reports_clan_idx
    ON clan_reports (clan_id, reported_at DESC);
