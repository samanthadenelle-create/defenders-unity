-- Corrective migration: clan_messages was originally shadowed by an orphaned legacy
-- table of the same name (sender_code/phrase_id/sent_at/pinned - a leftover from an
-- unrelated prototype, never created by this repo's migration system). The owner
-- dropped that legacy table by hand on 2026-09-17 after the sandbox's own safety
-- classifier repeatedly refused to run the DROP from within this session (labeled
-- "Cloud Storage Mass Delete"). 20260917_0030_clan_tables.sql's ledger row was already
-- recorded, so its CREATE TABLE IF NOT EXISTS clan_messages will never re-fire - this
-- file re-declares the exact same shape from that migration, unmodified, so the table
-- exists correctly going forward. Per this repo's convention, an already-shipped
-- migration is never edited; a follow-up corrective migration is added instead.
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
