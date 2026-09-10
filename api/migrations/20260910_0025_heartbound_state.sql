-- =============================================================================
-- 20260910_0025_heartbound_state.sql   (WO-1675, HEART-002)
-- -----------------------------------------------------------------------------
-- The APPLYABLE copy. api/schema.sql carries the identical CREATE TABLE body as a
-- DESCRIPTION and is never run against production (api/schema.sql:105-107); only
-- files under api/migrations/ are applied, and the runner derives its list from
-- this directory (tools/run-migrations.mjs:26-28). The two bodies must stay
-- character-identical, because tools/schema-parity.mjs compares the DEPLOYED
-- database against schema.sql's CREATE TABLE body and nothing else.
--
-- ⛔ WHY A TABLE AND NOT A SAVE FIELD. The save is CLIENT-AUTHORED. api/game/save.js
--    describes its own guards as "anti-grief / anti-corruption ceilings, NOT a
--    server-authoritative economy" (:70-72) and records that the simulated systems
--    reach this backend "only inside the opaque save blob... a RECORD, NOT A
--    CONTROL" (:410-421). Heartbound decides who gets paid by a Heart Pulse and
--    HEART-004 requires that a pulse can never pay the same player twice — an
--    invariant a replayed client blob could rewrite if it lived in the blob.
--    Consequently `SaveSchema.CurrentVersion` DOES NOT BUMP for this feature
--    (Assets/_Modules/Core/State/SaveSchema.cs:41). A bump would be the defect.
--
-- ⛔ THE KEY IS THE WALLET. api/schema.sql:60 — `player_id TEXT PRIMARY KEY --
--    BoundWallet address`; api/_lib/wallet-auth.js:27-29 — "THE WALLET REMAINS THE
--    SOLE IDENTITY ON THE SEEKER/APK ARTIFACT". RULED 2026-09-10: ONE WALLET = ONE
--    REALM, no re-binding, NO LINKAGE TABLE. So the spec's separate `walletAddress`
--    field is deliberately absent (it would be a second copy of the primary key)
--    and the spec's entire "Wallet Change" section is dropped rather than built.
--    A different wallet is a different player with a different row.
--
-- ⛔ EVERY COLUMN IS IN THE CREATE BODY, NOT ONE ALTER. tools/schema-parity.mjs
--    parses CREATE TABLE bodies only and is structurally blind to ALTER-added
--    columns (tools/run-migrations.mjs:42-44). A column added by ALTER here would
--    be invisible to the one gate that would catch it going missing in production.
--
-- ⚠ AND THAT IS ALSO THIS FILE'S LIMIT (memory: idempotent-ddl-hides-a-stale-table).
--   `CREATE TABLE IF NOT EXISTS` is a NO-OP against a table that already exists with
--   a different shape, and reports success while doing nothing. There is no such
--   table today, so this is a clean create — but judge it by the SHAPE QUERY at the
--   foot of this file, never by the statement returning and never by an exit code.
--
-- ⛔ NULLABLE TEXT FOR THE PULSE AND ECHO IDS, ON PURPOSE. WO-1677 (pulses) and
--    WO-1678 (echo events) have not designed their identifier shape yet. TEXT holds
--    whatever they choose; a BIGINT guessed today would need an ALTER COLUMN
--    tomorrow, which is the class of change the scar above says hides. NULL means
--    "this player has never seen one", which is the honest value for every row on
--    the day this lands.
--
-- ⛔ ACTIVATION DEFAULTS TO NOW() AND IS NEVER RE-STAMPED. spec :242 — "Do NOT
--    retroactively award historical Elarion pulses". `activated_at_utc` is the floor
--    every pulse query in WO-1677 filters on. The upsert in
--    api/_lib/heartbound-state.js re-asserts the STORED value on conflict so a
--    reconnecting wallet cannot move its own floor in either direction.
--
-- ADDITIVE. One CREATE TABLE and two CREATE INDEX. Zero DROP / DELETE / TRUNCATE /
-- rename; no existing table is read or written; no back-fill.
--
-- RE-RUNNABLE. Every object is IF NOT EXISTS.
--
-- Apply (owner, DATABASE_URL in env — it is REDACTED for every agent seat, so this
-- has NOT been run by the authoring lane):
--     node tools/run-migrations.mjs        -> MIGRATIONS_OK applied=N skipped=M
-- =============================================================================

CREATE TABLE IF NOT EXISTS heartbound_state (
    player_id                        TEXT PRIMARY KEY,
    status                           TEXT NOT NULL DEFAULT 'DORMANT' CHECK (status IN ('DORMANT','ACTIVE','STALE','UNSTAKING','DISCONNECTED','SUSPENDED')),
    activated_at_utc                 TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_verified_at_utc             TIMESTAMPTZ,
    last_actual_stake                NUMERIC(39,0) NOT NULL DEFAULT 0,
    effective_resonating_stake       NUMERIC(39,0) NOT NULL DEFAULT 0,
    last_verification_attempt_at_utc TIMESTAMPTZ,
    last_verification_code           TEXT,
    stale_since_utc                  TIMESTAMPTZ,
    continuous_pulse_count           INTEGER NOT NULL DEFAULT 0,
    total_lifetime_pulses            INTEGER NOT NULL DEFAULT 0,
    last_global_pulse_id             TEXT,
    last_player_pulse_id             TEXT,
    resonance_score                  NUMERIC(39,6) NOT NULL DEFAULT 0,
    resonance_tier                   INTEGER NOT NULL DEFAULT 0,
    highest_lifetime_tier            INTEGER NOT NULL DEFAULT 0,
    current_tree_resonance_stage     INTEGER NOT NULL DEFAULT 0,
    pending_echo_events              JSONB NOT NULL DEFAULT '[]',
    last_echo_event_id               TEXT,
    version                          INTEGER NOT NULL DEFAULT 1,
    created_at                       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- The pulse cron's working set: every ACTIVE row, oldest verification first.
CREATE INDEX IF NOT EXISTS idx_heartbound_state_status_verified
    ON heartbound_state (status, last_verified_at_utc);

-- Staleness sweep: the rows sitting inside (or past) the grace window.
CREATE INDEX IF NOT EXISTS idx_heartbound_state_stale_since
    ON heartbound_state (stale_since_utc);

-- Verify by SHAPE, never by the statement returning (expect 22 rows):
--   SELECT column_name, data_type, is_nullable, column_default
--     FROM information_schema.columns
--    WHERE table_name = 'heartbound_state' ORDER BY ordinal_position;
--
-- And the status CHECK, which is the constraint class that has silently rejected a
-- valid row in this repo before (tools/schema-parity.mjs:5-15, drift 4):
--   SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c
--     JOIN pg_class rel ON rel.oid = c.conrelid
--    WHERE rel.relname = 'heartbound_state' AND c.contype = 'c';
--
-- ⛔ resonance_score IS NUMERIC(39,6), NOT AN INTEGER AND NOT A FLOAT. WO-1676 owns
--    the resonance curve and has not landed; an integer column would decide its
--    scale here by accident, and a float would decide its precision. Six decimal
--    places hold either answer, and api/_lib/heartbound-state.js reads it ::text so
--    no JS float ever touches it.
--
-- ⛔ last_actual_stake / effective_resonating_stake ARE NUMERIC(39,0), NOT BIGINT.
--    Raw SKR is a u128 on-chain (NativeSkrStakeQuery.cs:65,74 deserialises two of
--    them); u128 max is ~3.4e38 and BIGINT tops out at ~9.2e18. They are read ::text
--    and handled as BigInt in JS — Number.MAX_SAFE_INTEGER is ~9e15, which a real
--    position exceeds at 6 decimals, and the rounding would land in a reward.
