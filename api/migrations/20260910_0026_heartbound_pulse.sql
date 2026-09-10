-- =============================================================================
-- api/_lib/heartbound-pulse-schema.sql — WO-1677 / HEART-004 (2026-09-10)
-- -----------------------------------------------------------------------------
-- DDL for the Heart Pulse ledger: the GLOBAL pulse row (one per unique observed
-- SKR share-price advancement) and the PER-PLAYER grant row (the structural
-- "a pulse can never pay the same player twice" guarantee).
--
-- ⛔ THIS FILE IS NOT APPLIED BY ANYTHING YET, ON PURPOSE.
--    `api/schema.sql` is owned by the HEART-002 lane this wave (WO-1675), so
--    this lane may not edit it. The LEAD must either fold these two blocks into
--    `api/schema.sql` or run this file as a migration (`api/migrations/`)
--    before /api/cron/heart-pulse can do anything but 500.
--    Acceptance 6 of WO-1677 (`MIGRATIONS_OK applied=N skipped=M` + a shape
--    query) is therefore the LEAD's step, not this lane's — named as NOT DONE
--    in the RESULT rather than quietly ticked.
--
-- SHAPE PRECEDENT (read at source 2026-09-10, not copied from a doc):
--   api/schema.sql:965-984 — `achievement_grants`, PRIMARY KEY (wallet,
--   achievement_id), whose own comment reads: "Generic idempotency ledger for
--   'grant this player X exactly once'… The PK STRUCTURALLY prevents a
--   double-grant". `player_pulse_grant` below is that shape, keyed on
--   (player_id, global_pulse_id) per spec :445-449.
--   The write shape is api/referral/install-brag.js:118-140 —
--   `INSERT … ON CONFLICT … DO NOTHING RETURNING`, empty result ⇒ "already
--   granted", re-read the original, never a second reward. HEART-004 reuses
--   that exact shape; it does not invent a second one.
--
-- PRECISION: the SKR share price is a u128 on chain
-- (Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:65 reads it at byte offset
-- 137). Postgres BIGINT is int64 and WOULD OVERFLOW. Both price columns are
-- NUMERIC(39,0) — integer-only, 39 digits ≥ u128's 2^128-1 (39 digits). Spec
-- :1113 ("never floats") is satisfied by the 0 scale. Bind them as STRINGS
-- from JS `bigint`; never as JS `number`.
-- =============================================================================


-- =============================================================================
-- global_heart_pulse — one row per unique observed share-price advancement.
-- -----------------------------------------------------------------------------
-- Written by : api/cron/heart-pulse.js (daily cron; see vercel.json "crons")
-- Fields per spec :404-416 (pulseId, sequenceNumber, previousSharePrice,
-- newSharePrice, detectedAtUtc, sourceSlot, chainReference, status,
-- processedAtUtc).
--
--   status  BASELINE  — the first-ever run recorded the current share price so
--                       a later run has something to compare against. It grants
--                       NOTHING. Without it the first deploy would compare
--                       against NULL and either mint a pulse for a price it
--                       never observed rising, or skip forever.
--           MINTED    — a real advancement; eligible players may be processed.
--           COMPLETE  — every eligible player has been processed or recorded
--                       PENDING; processed_at_utc is set.
--
-- ⭐ UNIQUE (new_share_price) is the "exactly one pulse per unique observed
--    advancement" guarantee (spec :428) made STRUCTURAL rather than
--    procedural: the share price only ever rises (rewards accrue into it), so a
--    re-scan over the same chain state hits the unique index and mints nothing.
--    That is WO-1677 acceptance 1, enforced by the database rather than by the
--    job remembering to check.
-- =============================================================================
CREATE TABLE IF NOT EXISTS global_heart_pulse (
    pulse_id             BIGSERIAL     PRIMARY KEY,
    sequence_number      BIGINT        NOT NULL UNIQUE,          -- 0 = baseline row
    previous_share_price NUMERIC(39,0),                          -- NULL on the baseline row only
    new_share_price      NUMERIC(39,0) NOT NULL UNIQUE,          -- ⭐ the anti-double-mint constraint
    detected_at_utc      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),   -- server clock, never a client ts
    source_slot          BIGINT,                                 -- Solana slot of the read (nullable: RPC may omit)
    chain_reference      TEXT,                                   -- StakeConfig account / tx reference for audit
    status               TEXT          NOT NULL DEFAULT 'MINTED',-- BASELINE | MINTED | COMPLETE
    processed_at_utc     TIMESTAMPTZ
);

-- The detector's hot read is "the newest pulse" (last processed share price +
-- the MIN_PULSE_INTERVAL floor check), so index the ordering it uses.
CREATE INDEX IF NOT EXISTS idx_global_heart_pulse_seq_desc
    ON global_heart_pulse (sequence_number DESC);

-- The catch-up read is "pulses this player has no grant row for, oldest first".
CREATE INDEX IF NOT EXISTS idx_global_heart_pulse_detected
    ON global_heart_pulse (detected_at_utc);


-- =============================================================================
-- player_pulse_grant — the per-player idempotency ledger (spec :445-449).
-- -----------------------------------------------------------------------------
--   PRIMARY KEY (player_id, global_pulse_id)
-- is the whole point: "a pulse can never pay the same player twice" becomes a
-- property of the table, not a property of the job being careful. A duplicate
-- background job's second INSERT hits the PK, `ON CONFLICT DO NOTHING` returns
-- zero rows, and the caller maps that to alreadyGranted — the
-- api/referral/install-brag.js:118-140 mapping, verbatim in shape.
--
--   status  GRANTED — verified stake, reward computed, streak advanced.
--           PENDING — ⚠ SKR verification was unavailable for THIS player at
--                     THIS pulse (RPC error / stale beyond grace). Per spec
--                     :451-461 and owner ruling Q2: the streak is NOT reset,
--                     NO unverified reward is generated, and the row exists so
--                     a later run can pay it once verification returns. A
--                     PENDING row is later promoted in place (UPDATE), never
--                     re-INSERTed — which is why status lives on the same row
--                     as the PK rather than in a separate attempts table.
--
--   effective_stake / resonance_score are the snapshot AT the pulse, so the
--   audit record is self-describing even after the curve is retuned — the same
--   reasoning as achievement_grants.reward (api/schema.sql:975-977).
--
--   ⚠ effective_stake is NUMERIC(39,**6**), not (39,0): HEART-003 works in WHOLE
--   SKR and returns a fractional figure (api/_lib/heartbound-resonance.js:128
--   `rawTokensToSkr`), while the SHARE PRICE columns above stay scale 0 because
--   those are u128 integers. Six decimals is exactly the chain's base-unit
--   granularity (`heartbound-resonance-config.json` chain.skrBaseUnits = 1e6),
--   so nothing is lost and nothing is invented. Scale 0 here would have silently
--   ROUNDED every player's effective stake on the way into the audit row.
-- =============================================================================
CREATE TABLE IF NOT EXISTS player_pulse_grant (
    player_id       TEXT          NOT NULL,               -- = player_data.player_id (wallet). NOT FK'd, matching achievement_grants.
    global_pulse_id BIGINT        NOT NULL REFERENCES global_heart_pulse (pulse_id) ON DELETE CASCADE,
    status          TEXT          NOT NULL DEFAULT 'GRANTED', -- GRANTED | PENDING
    effective_stake NUMERIC(39,6),                        -- effective stake in WHOLE SKR at grant time (NULL while PENDING)
    resonance_score INTEGER,                              -- FLOOR(StakePower+TenurePower) at grant time
    resonance_tier  INTEGER,
    reward          JSONB         NOT NULL DEFAULT '{}',  -- snapshot of what was granted (audit)
    pending_reason  TEXT,                                 -- e.g. 'RPC_UNAVAILABLE' | 'STALE' (PENDING rows only)
    granted_at      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    PRIMARY KEY (player_id, global_pulse_id)
);

-- "What does this player still owe / what did they receive" — the panel read
-- and the catch-up scan both filter by player then by status.
CREATE INDEX IF NOT EXISTS idx_player_pulse_grant_player_status
    ON player_pulse_grant (player_id, status);
