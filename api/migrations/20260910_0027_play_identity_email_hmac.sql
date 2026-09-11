-- WO-1698: optional lookup metadata. No raw Google subject or email is stored.
-- This table is lookup-only, never an authentication authority. Existing players
-- populate it on next verified Google sign-in. Include it in account erasure.
CREATE TABLE IF NOT EXISTS play_identities (
    player_id TEXT PRIMARY KEY CHECK (player_id ~ '^play-[0-9a-f]{64}$'),
    email_hmac TEXT CHECK (email_hmac IS NULL OR email_hmac ~ '^[0-9a-f]{64}$')
);
ALTER TABLE play_identities ADD COLUMN IF NOT EXISTS email_hmac TEXT;
CREATE INDEX IF NOT EXISTS play_identities_email_hmac_idx
    ON play_identities (email_hmac) WHERE email_hmac IS NOT NULL;
