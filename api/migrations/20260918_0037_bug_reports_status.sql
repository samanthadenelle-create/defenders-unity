-- WO-1867: give the Player Issues tab a dismiss/resolve path.
--
-- Owner report: four `bug_reports` rows (ids 11-14) are the same player, same
-- route, submitted 3 seconds apart, description "ggsgagsbshhshs" on all four -
-- a keyboard mash, not a real report. There was no way to mark a report
-- reviewed, resolved, or noise anywhere in the stack; the console's Player
-- Issues tab is explicitly documented read-only
-- (docs/COMMAND_CENTER_REFERENCE.md, "Read-only - no actions here.").
--
-- ⛔ THIS IS A TRIAGE FLAG, NOT A DELETE. No row this migration touches is ever
-- removed - `status` only changes which rows the DEFAULT view returns. See
-- api/admin/stats.js ?view=ops (reports) for the read side and api/admin/ops.js
-- action `bugreport.set_status` for the write side.
--
-- Additive + idempotent, same shape as api/schema.sql's own bug_reports block
-- (ADD COLUMN IF NOT EXISTS) and the guarded-CHECK idiom
-- 20260825_0002_repair_parity_remainder.sql uses, because Postgres has no
-- `ADD CONSTRAINT IF NOT EXISTS`.
--
-- Existing rows default to 'open' - nothing that has ever been filed silently
-- becomes "resolved" just because this column did not exist yesterday.

ALTER TABLE bug_reports
    ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'open';

-- reviewed_by is a free-text OPERATOR LABEL, same rule as ops.js
-- normalizeOperator - never a player identity. reviewed_at is NULL until the
-- first status write touches the row.
ALTER TABLE bug_reports
    ADD COLUMN IF NOT EXISTS reviewed_by TEXT NULL;

ALTER TABLE bug_reports
    ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMPTZ NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class rel ON rel.oid = c.conrelid
        WHERE c.contype = 'c'
          AND rel.relname = 'bug_reports'
          AND pg_get_constraintdef(c.oid) ILIKE '%status%'
    ) THEN
        ALTER TABLE bug_reports
            ADD CONSTRAINT bug_reports_status_check
            CHECK (status IN ('open', 'resolved', 'noise'));
    END IF;
END $$;

-- The default Player Issues view is `WHERE status = 'open' ORDER BY report_id
-- DESC LIMIT 50` (api/admin/stats.js). Partial because the moment triage runs,
-- the overwhelming majority of historical rows will be 'resolved'/'noise' and
-- have no business sitting in an index built for the small open queue.
CREATE INDEX IF NOT EXISTS idx_bug_reports_status_open
    ON bug_reports (report_id DESC)
    WHERE status = 'open';
