-- =============================================================================
-- schema.sql — COMPLETE Neon Postgres schema for Defenders of the Realm v2
-- -----------------------------------------------------------------------------
-- Run once in the Neon console: Dashboard → SQL Editor → paste & run.
-- Idempotent: every object uses IF NOT EXISTS, so re-running is safe.
--
-- This file is the single source of truth for the backend DB. It covers EVERY
-- backend feature the Unity client calls:
--
--   ENDPOINT                       TABLE(S)                          FUNCTION IN api/?
--   ----------------------------   -------------------------------   -----------------
--   GET  /api/auth/nonce           auth_nonces                       YES (auth/nonce.js)
--   GET  /api/game/load            player_data, auth_nonces          YES (game/load.js)
--   POST /api/game/save            player_data, auth_nonces          YES (game/save.js)
--   POST /api/events/track         analytics_events                  NO  (client-only)
--   POST /api/promo/redeem         promo_codes, promo_redemptions    NO  (client-only)
--   POST /api/referral/generate    referrals                         NO  (client-only)
--   POST /api/referral/claim       referrals, referral_claims        NO  (client-only)
--   POST /api/tower-swap/log       tower_swaps                       NO  (client-only)
--   POST /api/bug-report           bug_reports                       NO  (client-only)
--   GET  /api/leaderboard          leaderboard_scores, player_profiles  YES (leaderboard/get.js)   [WO-129]
--   POST /api/leaderboard/submit   leaderboard_scores                YES (leaderboard/submit.js) [WO-129]
--   GET  /api/profile              player_profiles, leaderboard_scores  YES (profile/get.js)       [WO-129]
--   POST /api/profile/username     player_profiles                   YES (profile/username.js)   [WO-129]
--   POST /api/profile/social       player_profiles                   YES (profile/social.js)     [WO-129]
--   POST /api/referral/install-brag  achievement_grants              YES (referral/install-brag.js) [WO-129]
--
-- "client-only" = the Unity client POSTs to the endpoint but the serverless
-- function does NOT yet exist in api/. The table is defined here so that when
-- the owner writes the function, the DB is already aligned with the client's
-- payload. See api/DB_SETUP.md for the per-field provenance and any guesses.
--
-- CONVENTIONS
--   • player_id  TEXT — the BoundWallet address (Solana). Same value across all
--     tables; it is the join key. We DO NOT add a hard FK from feature tables to
--     player_data because a player can fire analytics / bug reports / promo
--     redemptions before their first save row exists (player_data is created
--     lazily on the first /api/game/save). A hard FK would reject those rows.
--     Where the relationship is guaranteed (referral_claims → referrals) we DO
--     add an FK.
--   • Timestamps are TIMESTAMPTZ DEFAULT NOW() (server receive time). Client-
--     supplied timestamps (clientTs / timestamp) are stored in their own column
--     so we can measure clock skew and never trust the client for ordering.
--   • JSONB for free-form / nested payloads (event properties, save state).
-- =============================================================================


-- =============================================================================
-- 1. player_data  — one row per player; the delta-merged save blob.
-- -----------------------------------------------------------------------------
-- UNCHANGED from the original schema (kept verbatim so /api/game/save's upsert
-- and /api/game/load's SELECT keep working). game_state holds the client's
-- SyncDeltaPayload merged into a single JSONB document (camelCase keys).
--
-- Written by  : api/game/save.js  (INSERT … ON CONFLICT (player_id) DO UPDATE,
--               merging  game_state || EXCLUDED.game_state).
-- Read by     : api/game/load.js  (SELECT game_state, schema_version, updated_at).
-- =============================================================================
CREATE TABLE IF NOT EXISTS player_data (
    player_id      TEXT        PRIMARY KEY,          -- BoundWallet address
    -- NULLABLE, NO DEFAULT (2026-09-07, WO-1457 — migration 0022). NULL means "this
    -- row never declared a version — do NOT run a migration chain on load". It used
    -- to read `INTEGER NOT NULL DEFAULT 10`, and that 10 was SaveSchema.CurrentVersion
    -- on the day this file was written — a live constant copied into DDL, so it was
    -- wrong one bump later. A brand-new player on a version-less client INSERTed at 10
    -- with current-shaped state (save.js's version-less branch names this column
    -- nowhere, so the DEFAULT stood); load.js returned the 10; ApplyBackendState drove
    -- the whole v10→current chain over state that was never v10. See migration
    -- 20260907_0022 for the full reading.
    schema_version INTEGER,
    -- The monotonic generation number of the town in this row (2026-09-07, WO-1598 —
    -- migration 0023). NULL means "this player has never declared a reset". A save that
    -- declares an epoch ABOVE the stored one is a NEW GAME: api/game/save.js stands the
    -- COMPARATIVE sanity guards (implausible_drop / bestWave rollback) down for that one
    -- write and advances the epoch, because those rules compare against a town that no
    -- longer exists. Equal = ordinary save. Below = a stale device replaying a dead town,
    -- refused 409 SAVE_RESET_STALE. NO DEFAULT, deliberately: a 0 default would make every
    -- existing row claim generation zero on no evidence, which is schema_version's
    -- `DEFAULT 10` mistake (migration 0022) repeated.
    reset_epoch    INTEGER,
    game_state     JSONB       NOT NULL DEFAULT '{}',
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- DRIFT RECONCILE (2026-05-31): an earlier deploy created player_data with ONLY
-- (player_id, game_state, updated_at) -- missing schema_version + created_at that
-- save.js/load.js read+write, so the live save function FAILED against it. CREATE
-- ... IF NOT EXISTS above does NOT alter an existing table, so add the columns
-- explicitly. Additive + idempotent (no data touched, no drops):
ALTER TABLE player_data ADD COLUMN IF NOT EXISTS schema_version INTEGER;
ALTER TABLE player_data ADD COLUMN IF NOT EXISTS created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW();
-- 2026-09-07 (WO-1598): the CREATE TABLE above cannot add this to an EXISTING player_data,
-- and every live database has one. The applyable copy is
-- api/migrations/20260907_0023_player_data_reset_epoch.sql — this line is a DESCRIPTION.
ALTER TABLE player_data ADD COLUMN IF NOT EXISTS reset_epoch    INTEGER;

-- ⛔ AND THE ALTERs ABOVE CANNOT UNDO THE OLD DEFAULT. `ADD COLUMN IF NOT EXISTS` is a
-- no-op on a column that already exists, so on any database that already ran the
-- `NOT NULL DEFAULT 10` form, re-running this file changes NOTHING and leaves the 10
-- in place while reading as success (memory: idempotent-ddl-hides-a-stale-table).
-- Only `ALTER COLUMN` can relax an existing column, and only a file under
-- api/migrations/ is ever applied to production — this file is a DESCRIPTION of the
-- schema, not a thing that runs. The applyable copy is:
--     api/migrations/20260907_0022_game_saves_schema_version_default.sql
--         ALTER TABLE player_data ALTER COLUMN schema_version DROP DEFAULT;
--         ALTER TABLE player_data ALTER COLUMN schema_version DROP NOT NULL;
--     node tools/run-migrations.mjs
-- Verify by SHAPE (is_nullable 'YES' AND column_default NULL), never by exit code.

-- TRUST TIER (2026-08-02, the guest rail). Which auth rail last wrote this row:
--   'wallet' — an ed25519 signature over a single-use nonce proved key ownership.
--   'google' — (added 2026-08-30, WO-1282 PIN-1b) a Google ID token, RS256-verified
--              against Google's JWKS, proved the account; the player id is
--              HMAC-SHA256(GOOGLE_IDENTITY_KEY, google_sub) derived SERVER-SIDE, so the
--              client cannot mint one. PROVEN, like 'wallet' and unlike 'guest'. Exists
--              only for the Google Play / AAB artifact — the wallet remains the SOLE
--              identity on the Seeker / dApp-Store artifact (owner ruling 2026-08-30).
--   'guest'  — an unverified device-hash bearer id (see guest_rate_limit's header
--              for exactly how little that is worth).
-- Recorded so the distinction is VISIBLE in one column instead of inferred from
-- the id's shape, and so any future real-value feature can filter on it rather
-- than trusting every row equally. Written by api/game/save.js on every upsert.
-- The DEFAULT is 'legacy', never 'wallet': any row that predates this column was
-- written before the two rails existed, and back-filling it as wallet-proven
-- would be a lie told by a schema migration.
ALTER TABLE player_data ADD COLUMN IF NOT EXISTS trust          TEXT        NOT NULL DEFAULT 'legacy';

-- Index for frequent sorted queries (leaderboard, etc.)
CREATE INDEX IF NOT EXISTS idx_player_data_best_wave
    ON player_data ((game_state->>'bestWave') DESC NULLS LAST);

-- Index to quickly find players updated recently (analytics / ops)
CREATE INDEX IF NOT EXISTS idx_player_data_updated_at
    ON player_data (updated_at DESC);

-- -----------------------------------------------------------------------------
-- game_state JSONB structure (all keys optional — only sent when changed):
-- {
--   "bestWave":       integer,
--   "crystals":       integer,
--   "food":           integer,
--   "coins":          integer,
--   "voidshards":     integer,
--   "stone":          integer,
--   "iron":           integer,
--   "wood":           integer,
--   "towers":         [int, int, int, int, int, int, int, int, int],  -- 9 slots
--   "towerAbilities": [int, int, int, int, int, int, int, int, int],  -- 9 slots
--   "pets":           [ { ... PetData ... } ],
--   "ownedPets":      [ "Aether" | "Flame" | "Ice" | ... ],
--   "starterPetId":   string | null
-- }
-- NOTE: the live client (GameStateService.SendDelta) actually POSTs the FULL
-- camelCase PersistedState snapshot (with null fields stripped) PLUS "playerId",
-- so game_state may also carry nested objects like "resources":{...} and the
-- many other PersistedState fields. save.js only promotes the whitelisted keys
-- above into the merged delta; any extra keys it ignores. The JSONB column will
-- hold whatever save.js chooses to write — keep save.js as the gatekeeper.
-- -----------------------------------------------------------------------------


-- =============================================================================
-- 1b. auth_nonces  — single-use wallet-signature challenges (WO-120 §D security).
-- -----------------------------------------------------------------------------
-- Endpoint : GET  /api/auth/nonce?wallet=<base58>   (api/auth/nonce.js — issues)
--            POST /api/game/save  (verifies a signature over the nonce; consumes)
-- Client   : Assets/_Modules/Core/State/GameStateService.cs (fetch → sign → send)
--
-- WHY: /api/game/save + /api/game/load were keyed ONLY by the PUBLIC wallet
-- address (?playerId=<wallet>) with NO proof of ownership, so anyone who knew a
-- wallet could overwrite/read that player's save. This table backs a
-- challenge–response: the server issues a random one-time nonce bound to the
-- claimed wallet; the client signs {nonce}+payload with the wallet's ed25519
-- key (Solana); save verifies the signature against the wallet pubkey, then
-- BURNS the nonce so it can never be replayed.
--
-- WALLET SCHEME (ASSUMPTION — flagged): Solana / ed25519. The whole client is
-- Solana (WalletService base58 address = player_id; tower-swap uses Solana tx
-- sigs; Solana Pay). Verification therefore uses tweetnacl ed25519 over the
-- base58-decoded wallet pubkey. If the chain were ever EVM, swap the verify
-- helper for ecrecover — the table and flow are scheme-agnostic.
--
--   nonce       — the random challenge (server-generated, PK). The client signs
--                 a deterministic message that embeds this value.
--   wallet      — the base58 address the nonce was issued TO. The save endpoint
--                 must verify the signature against THIS pubkey AND match it to
--                 the payload's playerId, else 401.
--   used        — false on issue; set true the instant a save consumes it. A
--                 second save presenting the same nonce is rejected (replay).
--   expires_at  — short TTL (default 5 min). Expired nonces are unusable even if
--                 never consumed; a periodic sweep (or the WHERE clause on
--                 verify) discards them.
--
-- Consume is atomic: the verify step does
--   UPDATE auth_nonces SET used = TRUE
--   WHERE nonce = $1 AND wallet = $2 AND used = FALSE AND expires_at > NOW()
--   RETURNING nonce;
-- A zero-row result means missing / already-used / expired / wrong-wallet → 401.
-- =============================================================================
-- =============================================================================
-- 1c. auth_sessions — short-lived wallet bearer tokens (WO-1157).
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/auth/session   (api/auth/session.js — issues, from ONE burned nonce)
-- Verified : _lib/wallet-auth.verifySession, via the X-Session header
--
-- WHY: every authenticated call used to demand a FRESH signature, because the signed
-- message embeds a single-use nonce AND a hash of the request body. That is excellent
-- security and a poor purchase: buying one pack prompted the wallet three times —
-- connect, an auth signature per backend call, and the transfer. The owner, mid-canary:
-- "i had to verify with wallet 3 times… cant it roll into one transaction like every
-- other site?" It can: sites cache the SESSION, never the purchase consent.
--
-- ⛔ THE TRADEOFF, STATED RATHER THAN HIDDEN. A body-bound signature cannot be replayed
-- against a different request. A bearer token CAN, until it expires. This is therefore a
-- deliberate, bounded reduction in security, and the bound IS the justification:
--   * 15-minute TTL (SESSION_TTL_SECONDS). Do not raise it for convenience.
--   * bound to ONE wallet; a session for A can never act for B (SESSION_WRONG_WALLET).
--   * revocable, and pruned on every issue.
--   * additive — the signature rail is untouched and still authenticates.
-- Never let this become a permanent login. The window is the whole argument.
--
--   token       — random 32-byte base64url bearer credential (PK).
--   wallet      — the base58 address this session speaks for. NOT a claim the client makes:
--                 it is copied from the wallet whose signature was verified at issue time.
--   revoked     — kill switch; a revoked token reports UNKNOWN, not EXPIRED, so a client
--                 does not sit in a re-signing loop against a credential that is gone.
--   expires_at  — short TTL. Expiry is a NORMAL state: the client re-signs once and continues.
-- =============================================================================
CREATE TABLE IF NOT EXISTS auth_sessions (
    token      TEXT        PRIMARY KEY,
    wallet     TEXT        NOT NULL,
    revoked    BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL
);
CREATE INDEX IF NOT EXISTS auth_sessions_wallet_idx  ON auth_sessions (wallet);
CREATE INDEX IF NOT EXISTS auth_sessions_expires_idx ON auth_sessions (expires_at);

-- IDENTITY KIND (2026-08-30, WO-1282 PIN-1b — the Google Play rail).
--
-- ⚠ READ THE `wallet` COLUMN ABOVE AS A **SUBJECT**, NOT STRICTLY A WALLET ADDRESS.
-- It is a LIVE PRIMARY KEY REFERENCE used by _lib/wallet-auth.verifySession and by
-- every Play endpoint (purchases/google-play-binding|verify|fulfill), so it KEEPS ITS
-- NAME — renaming it would break the deployed functions for zero behavioural gain. What
-- changed is what may appear in it:
--   * a base58 Solana address  — the wallet rail (Seeker / dApp Store artifact), OR
--   * 'play-<64 hex>'          — HMAC-SHA256(GOOGLE_IDENTITY_KEY, google_sub), derived
--                                SERVER-SIDE in _lib/google-identity.derivePlayerId and
--                                minted ONLY by api/auth/google-session.js.
-- The two shapes are lexically disjoint, so a row can never be read as the wrong rail.
--
-- identity_kind records WHICH proof stood behind the mint — 'wallet' (ed25519 signature
-- over a burned nonce) or 'google' (a Google-signed ID token, RS256-verified against
-- Google's JWKS). It is AUDIT, never an authorization input: nothing reads it to decide
-- access, so a wrong value cannot grant anything. The DEFAULT is 'wallet' because every
-- row that predates this column was issued by api/auth/session.js, which is the wallet
-- path and remains untouched — additive and idempotent, same pattern as player_data.trust.
--
-- ⛔ The wallet remains the SOLE identity on the Seeker/APK artifact (owner ruling
--    2026-08-30). 'google' exists only for the Google Play / AAB artifact.
ALTER TABLE auth_sessions ADD COLUMN IF NOT EXISTS identity_kind TEXT NOT NULL DEFAULT 'wallet';

-- SIGNED_AT (2026-09-06, WO-1441 — the renewal cap).
--
-- A session may now be exchanged for a fresh one WITHOUT a new wallet signature
-- (_lib/wallet-auth.renewSession), because a 15-minute TTL with no renewal killed cloud
-- save mid-session and the save route may not raise a wallet sheet while the player walks.
-- Renewal ROTATES the token, so `created_at` restarts on every renew and cannot bound the
-- chain. THIS column is the one that can: it records when the ORIGINAL SIGNATURE happened
-- and is COPIED FORWARD, unchanged, into every renewed row.
--
-- ⛔ IT IS THE ONLY THING STOPPING A SESSION BECOMING A PERMANENT LOGIN. Renewal refuses
-- past signed_at + SESSION_ABSOLUTE_TTL_SECONDS (12h) and the player signs again. If you
-- ever find renewal setting this to NOW(), the cap is gone and every leaked-but-renewed
-- chain lives forever — that is the bug to look for here, not a slow query.
--
-- DEFAULT NOW() is correct for pre-existing rows: they were minted by a real signature at
-- some point at or before now, so treating that as the chain origin can only ever make the
-- cap STRICTER for them, never looser.
ALTER TABLE auth_sessions ADD COLUMN IF NOT EXISTS signed_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE TABLE IF NOT EXISTS auth_nonces (
    nonce      TEXT        PRIMARY KEY,             -- random one-time challenge (base64url, 32 bytes)
    wallet     TEXT        NOT NULL,                -- base58 wallet the nonce was issued to
    used       BOOLEAN     NOT NULL DEFAULT FALSE,  -- burned on first successful verify (replay guard)
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),  -- issue time
    expires_at TIMESTAMPTZ NOT NULL                 -- created_at + short TTL (issuer sets, e.g. 5 min)
);

-- Look an issued nonce up by wallet (and prune a wallet's stale challenges).
CREATE INDEX IF NOT EXISTS idx_auth_nonces_wallet
    ON auth_nonces (wallet);

-- Sweep expired/used nonces cheaply (a cron or the next issue call can run:
--   DELETE FROM auth_nonces WHERE expires_at < NOW() OR used = TRUE;).
CREATE INDEX IF NOT EXISTS idx_auth_nonces_expires
    ON auth_nonces (expires_at);


-- =============================================================================
-- 1c. guest_rate_limit  — the GUEST rail's only defence (2026-08-02).
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/game/save, GET /api/game/load (both via
--            api/_lib/wallet-auth.verifyGuest → touchGuestRate)
-- Client   : Assets/_Modules/Core/State/GameStateService.cs — the id this table
--            keys on is EXACTLY the one EnsureAccount already mints:
--              "guest-local-" + sha256(SystemInfo.deviceUniqueIdentifier + salt)
--
-- WHY A GUEST RAIL EXISTS AT ALL: the APK front door is "Connect Wallet OR Play
-- as Guest", and testers are being recruited now. Before this, save/load required
-- a wallet signature UNCONDITIONALLY, so every guest tester was structurally
-- unable to reach the database — their progress lived and died in PlayerPrefs on
-- one device, and the owner could see nothing of what they played.
--
-- WHAT A GUEST IDENTITY IS WORTH — stated plainly so nobody later mistakes it for
-- authentication: it is a BEARER TOKEN. The only secret is the 256-bit device
-- hash itself; whoever presents it gets that row, like an unguessable URL. It
-- cannot be revoked and cannot move to a new device. That is an honest trade for
-- a throwaway tester save and is NEVER acceptable for real value — which is
-- enforced structurally, not by policy: a guest id is 76 chars containing '-' and
-- hex '0', so it can never satisfy the base58 wallet regex, and therefore can
-- never key a wallet row or reach a wallet-gated feature.
--
-- The budget below is per guest id and SHARED by save+load (one bounded total,
-- not two). At 30/60s it is ~4x the client's own maximum sync rate (its
-- MinSyncDelay is 8s), so an honest tester never sees it.
--
--   hits / window_started_at — the sliding window, advanced in one atomic UPSERT.
--   total_hits              — lifetime counter, purely for "is this id abusive".
--   last_seen               — drives the cleanup sweep.
-- =============================================================================
CREATE TABLE IF NOT EXISTS guest_rate_limit (
    guest_id          TEXT        PRIMARY KEY,             -- "guest-local-<64 lowercase hex>"
    window_started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),  -- start of the current 60s window
    hits              INTEGER     NOT NULL DEFAULT 0,      -- requests inside the current window
    total_hits        BIGINT      NOT NULL DEFAULT 0,      -- lifetime requests (abuse signal)
    last_seen         TIMESTAMPTZ NOT NULL DEFAULT NOW()   -- last request from this id
);

-- Sweep idle guests (api/admin/cleanup.js drops rows untouched for 30 days).
CREATE INDEX IF NOT EXISTS idx_guest_rate_limit_last_seen
    ON guest_rate_limit (last_seen);


-- =============================================================================
-- 2. analytics_events  — player behaviour analytics.
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/events/track   (FUNCTION NOT YET IN api/ — client-only)
-- Client   : Assets/_Modules/Core/Analytics/EventTracker.cs
--
-- The client batches events and POSTs:
--   { "events": [ { "playerId", "eventName", "properties", "clientTs" }, ... ] }
-- where each event is EventTracker.TrackedEvent:
--   playerId   string  — BoundWallet or "anonymous"
--   eventName  string  — snake_case: session_start, wave_completed,
--                        purchase_completed, bundle_viewed, promo_redeemed,
--                        referral_code_generated, referral_shared,
--                        referral_claimed, tower_swap_completed, + custom
--   properties string  — JSON STRING (the client serializes the props object to
--                        a string before sending). The function should parse it
--                        to JSONB on insert:  properties::jsonb  (or store raw
--                        text in properties_raw if it ever fails to parse).
--   clientTs   long    — unix epoch MILLISECONDS captured on the device.
--
-- One DB row per event (the function loops over the events array and inserts
-- each). event_id is a generated surrogate PK because events are not unique by
-- any client field (a player can fire the same eventName many times).
-- =============================================================================
CREATE TABLE IF NOT EXISTS analytics_events (
    event_id    BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    player_id   TEXT        NOT NULL,             -- "playerId" (BoundWallet | "anonymous")
    event_name  TEXT        NOT NULL,             -- "eventName" (snake_case)
    properties  JSONB       NOT NULL DEFAULT '{}',-- parsed from the client's "properties" JSON string
    client_ts   BIGINT,                           -- "clientTs" — device unix epoch MILLIS
    received_at TIMESTAMPTZ NOT NULL DEFAULT NOW()-- server receive time (trusted ordering)
);

-- Events for one player, newest first (per-player funnels / debugging).
CREATE INDEX IF NOT EXISTS idx_analytics_events_player_time
    ON analytics_events (player_id, received_at DESC);

-- Aggregate by event name over time (e.g. count session_start per day).
CREATE INDEX IF NOT EXISTS idx_analytics_events_name_time
    ON analytics_events (event_name, received_at DESC);


-- =============================================================================
-- 2c. packs — DB authority for promo pack grants (WO-1258).
-- -----------------------------------------------------------------------------
-- `contents` is the PackContents JSON object consumed by the APK. Night Market
-- browsing still uses packs.json; promo redeem never does after the inline-pack
-- client ships. Existing packs.json rows are seeded once by tools/seed-promo-packs.mjs.
-- =============================================================================
CREATE TABLE IF NOT EXISTS packs (
    sku           TEXT        PRIMARY KEY,
    name          TEXT        NOT NULL,
    contents      JSONB       NOT NULL,
    active        BOOLEAN     NOT NULL DEFAULT TRUE,
    store_visible BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =============================================================================
-- 3. promo_codes  — operator-issued promo codes (the catalog of valid codes).
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/promo/redeem   (FUNCTION NOT YET IN api/ — client-only)
-- Client   : Assets/_Modules/Core/Promo/PromoCodeService.cs
--
-- The client POSTs:  { playerId, code }  and expects back
--   success: { success:true, reward:{ crystals, coins }, message }
--   failure: { success:false, error:"INVALID_CODE"|"ALREADY_REDEEMED"
--                                   |"EXPIRED"|"PLAYER_LIMIT_REACHED" }
--
-- This table is the SOURCE OF TRUTH for what a code grants and its limits. The
-- owner/operator inserts rows here by hand (or via an admin tool). The redeem
-- function looks the code up here, checks expiry + caps, then records the
-- redemption in promo_redemptions (table 4).
--
-- Codes are normalised to UPPERCASE by the client (code.Trim().ToUpperInvariant())
-- BEFORE sending, so store + compare uppercase. code is the PK / lookup key.
--
-- Error-code mapping the redeem function must implement:
--   row missing                          → INVALID_CODE
--   NOW() > expires_at (when not null)   → EXPIRED
--   active = false                       → INVALID_CODE (or a disabled state)
--   global redemption count >= max_redemptions (when not null) → ALREADY_REDEEMED
--   this player already in promo_redemptions for this code     → ALREADY_REDEEMED
--   player's total distinct redeemed codes >= per_player_limit → PLAYER_LIMIT_REACHED
-- =============================================================================
CREATE TABLE IF NOT EXISTS promo_codes (
    code             TEXT        PRIMARY KEY,           -- normalised UPPERCASE code (e.g. "TEST10")
    reward_crystals  INTEGER     NOT NULL DEFAULT 0,    -- → response reward.crystals
    reward_coins     INTEGER     NOT NULL DEFAULT 0,    -- → response reward.coins
    message          TEXT,                              -- → response.message (shown to player)
    active           BOOLEAN     NOT NULL DEFAULT TRUE, -- operator kill-switch
    max_redemptions  INTEGER,                           -- NULL = unlimited global uses
    per_player_limit INTEGER,                           -- NULL = no cross-code cap (PLAYER_LIMIT_REACHED gate)
    expires_at       TIMESTAMPTZ,                       -- NULL = never expires
    bound_wallet     TEXT,                              -- NULL = public code; SET = only this player_id may redeem
    reward_pack_sku  TEXT,                              -- NULL = use reward_crystals/coins; SET = grant this pack's whole contents
    tier1_pack_sku   TEXT,                              -- optional first-N pack reward (WO-1256)
    tier1_limit      INTEGER CHECK (tier1_limit IS NULL OR tier1_limit > 0),
    tier2_pack_sku   TEXT,                              -- reward after tier1_limit
    tier2_reward_crystals INTEGER,                      -- optional pack-free second tier
    tier2_reward_coins INTEGER,                         -- optional pack-free second tier
    redemption_count INTEGER NOT NULL DEFAULT 0,        -- atomic ordinal for tier selection
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- reward_pack_sku (added 2026-08-17, owner ask: "i need you to be able to add wood
-- and all the regular resources too. Or we should also try grant_pack") -----------
-- reward_crystals/reward_coins can only ever pay TWO currencies. The owner needs
-- wood, iron and food — and glimmer exists too.
--
-- ⛔ THE OBVIOUS FIX IS THE WRONG ONE. Adding reward_wood / reward_iron / reward_food
-- columns means a MIGRATION EVERY TIME A RESOURCE IS INVENTED, and it still would not
-- cover glimmer. Worse, it creates a SECOND definition of "a bundle of resources"
-- alongside packs.json, and the two will drift.
--
-- Instead: name a PACK. packs.json already authors contents.economy as an open bag —
--     impulse-wood-small -> {"wood":1000}
--     hearth-spark       -> {"glimmer":25,"crystals":200,"food":50,"coins":100}
-- so a pack sku reaches EVERY resource type, in any combination, already authored and
-- already priced. The client applies it through PackStoreVM.ApplyPackContents — the
-- SAME seam a real purchase uses (GrantSpendablePurchased -> PurchasedOrPromised,
-- never clamped, WO-857 Phase F), which was proven working on device 2026-08-17.
-- Every future pack becomes grantable for free, with no schema change.
--
-- PRECEDENCE: when reward_pack_sku is set it WINS and the crystal/coin columns are
-- ignored — one source of truth per code, never a merge of two.
-- ⚠ The sku is NOT validated by the DB. An unknown sku must fail LOUDLY client-side
-- (the code is already burned by then), never silently grant nothing.
--
-- MIGRATION (idempotent, nullable, safe on the live table):
--     ALTER TABLE promo_codes ADD COLUMN IF NOT EXISTS reward_pack_sku TEXT;

-- bound_wallet (added 2026-08-17, WO-1115) ------------------------------------
-- NULL  → a PUBLIC code: anyone may redeem, subject to the other gates. This is
--         every launch promo, influencer code and apology grant. Unchanged.
-- SET   → a PRIVATE code, redeemable ONLY by that player_id (a BoundWallet address).
--
-- WHY: the owner wants DEV codes that grant resources outright. On a PUBLISHED
-- game that is a free-money exploit the moment it leaks — forum post, screenshot,
-- support ticket. Binding makes a leak INERT: anyone else gets INVALID_CODE and
-- the code is NOT consumed, so the owner's own grant still works afterwards.
--
-- ⚠ The binding is only as strong as player_id, which is why the check lives in
-- redeem.js AFTER _lib/wallet-auth.authenticate() — a base58 id has, by then,
-- produced an ed25519 signature over the exact body bytes plus a single-use nonce.
-- It compares a PROVEN identity, never a claimed one. Never evaluate this against
-- a wallet taken from the request body: that is precisely the hole the 2026-08-15
-- audit closed here (player_id used to come straight from the body, letting anyone
-- burn a victim's code and lock them out of it forever).
--
-- A refused private code returns INVALID_CODE, not a distinct error, on purpose: a
-- private code must be indistinguishable from a nonexistent one to anyone who is
-- not its owner. A distinct "not yours" would confirm the code is real and worth
-- hunting for.
--
-- MIGRATION for an existing database (idempotent, safe on a live table — the
-- column is nullable so every existing row stays a public code):
--     ALTER TABLE promo_codes ADD COLUMN IF NOT EXISTS bound_wallet TEXT;

-- created_by (added 2026-08-27, WO-1244 - the Command Center console) ----------
-- The operator label that AUTHORED the code. NULL on every code written before
-- the console existed, and on any code authored while this migration is unrun.
--
-- ⚠ ADDED BY ALTER, NOT IN THE CREATE TABLE BODY ABOVE, AND THAT IS DELIBERATE.
-- tools/schema-parity.mjs parses only the CREATE TABLE bodies in this file, so a
-- column declared there but not yet run reads as DRIFT and BLOCKS EVERY DEPLOY
-- until a human runs the SQL. There is no migration runner in this repo. Putting
-- it here keeps the gate honest about the money tables while an optional,
-- attribution-only column catches up.
--
-- api/_lib/ops.js writes it through a TWO-SHAPE cascade: it names created_by,
-- and on 42703 (undefined column) falls back to the shape without it and reports
-- attribution_on_row:false in the response. The code is still authored; the
-- durable history row in analytics_events (event_name = 'admin_ops_write')
-- carries the operator either way. NEVER let a missing attribution column become
-- a reason a promo could not be created during an incident.
--
-- MIGRATION (idempotent, nullable, safe on the live table):
--     ALTER TABLE promo_codes ADD COLUMN IF NOT EXISTS created_by TEXT;


-- =============================================================================
-- 4. promo_redemptions  — one row each time a player redeems a code.
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/promo/redeem   (same function as table 3)
--
-- Enforces one-time-use: a UNIQUE (code, player_id) constraint means a second
-- redeem of the same code by the same player violates the unique index, which
-- the function maps to ALREADY_REDEEMED. Counting rows per code enforces
-- max_redemptions; counting distinct codes per player enforces per_player_limit.
--
-- player_id is NOT FK'd to player_data (an anonymous player may redeem before
-- their first save). code IS FK'd to promo_codes (a redemption can't exist for
-- a code that isn't in the catalog).
-- =============================================================================
CREATE TABLE IF NOT EXISTS promo_redemptions (
    redemption_id BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code          TEXT        NOT NULL REFERENCES promo_codes(code) ON DELETE CASCADE,
    player_id     TEXT        NOT NULL,             -- "playerId" (BoundWallet | "anonymous")
    crystals      INTEGER     NOT NULL DEFAULT 0,   -- snapshot of reward granted (audit; code reward may change later)
    coins         INTEGER     NOT NULL DEFAULT 0,   -- snapshot of reward granted
    pack_sku      TEXT,                                -- exact pack granted (tier-safe audit snapshot)
    contents      JSONB,                               -- immutable DB pack snapshot applied by the client
    redemption_ordinal INTEGER,                        -- atomic campaign ordinal for cohort/audit
    redeemed_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (code, player_id)                        -- one redemption per code per player
);

-- Count redemptions per code fast (max_redemptions enforcement).
CREATE INDEX IF NOT EXISTS idx_promo_redemptions_code
    ON promo_redemptions (code);

-- Count a player's distinct redeemed codes fast (per_player_limit enforcement).
CREATE INDEX IF NOT EXISTS idx_promo_redemptions_player
    ON promo_redemptions (player_id);

-- ⚠ CORRECTION 2026-09-06 (WO-1440): "Counting rows per code enforces max_redemptions"
-- above WAS THE BUG, not just the documentation of it. A SELECT COUNT(*) followed by an
-- INSERT is two transactions on the HTTP driver; measured with a cap of 20 and fifty
-- concurrent actors, ALL FIFTY were granted. The cap is now claimed in the SAME statement
-- as the insert, by an UPDATE on promo_codes carrying
-- `(max_redemptions IS NULL OR redemption_count < max_redemptions)` — the row lock is what
-- makes it atomic. The count above survives only as a cheap early-out.
--
-- ⛔ THEREFORE `promo_codes.redemption_count` IS NOW LOAD-BEARING FOR EVERY CODE, not just
--    tiered ones: every grant path bumps it. Never repair the ledger by deleting
--    promo_redemptions rows without also lowering redemption_count, or the code's cap will
--    stay spent. (Verified in step 2026-09-06: all live codes' counters were in step with
--    their ledgers before the change.)
--
-- MIGRATION (idempotent, nullable, safe on the live table) — the attributability column
-- the guest-redeem reversal requires. Deliberately outside the CREATE body, like
-- promo_codes.created_by, so tools/schema-parity.mjs cannot read it as drift:
--     ALTER TABLE promo_redemptions ADD COLUMN IF NOT EXISTS ip_hash TEXT;
-- Ships with promo_ip_budget in
--     api/migrations/20260906_0019_promo_guest_redeem_ip_budget.sql


-- =============================================================================
-- 4b. promo_ip_budget — the guest promo rail's only NON-FORGEABLE gate (WO-1440).
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/promo/redeem   (guest rail only)
--
-- The owner reversed the wallet-only rule on promo redemption 2026-09-06 so the live
-- FIRSTWATCH campaign could reach the published build's players, who are guests. A
-- guest id is MINTED BY THE CLIENT and is therefore not a scarcity key. The caller's
-- IP is the one signal the client cannot choose, so redemption spends a fixed-window
-- budget per (hashed IP, code) — counted on GRANTS ONLY, so a typo or an expired code
-- never costs a household a slot, and never counted for a proven wallet.
--
-- ip_hash is audit.js's existing salted digest. ONE hashing rule project-wide, so this
-- table joins to the auth-reject rows. A raw IP is stored nowhere.
--
-- The threshold lives in api/promo/redeem.js (PROMO_IP_MAX_GRANTS_PER_WINDOW), not
-- here — a limit copied into two places is the duplicated state that goes stale.
-- =============================================================================
CREATE TABLE IF NOT EXISTS promo_ip_budget (
    ip_hash           TEXT        NOT NULL,            -- audit.hashIp(req): salted, truncated, non-reversible
    code              TEXT        NOT NULL,            -- the promo code (uppercase)
    window_started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    grants            INTEGER     NOT NULL DEFAULT 0,  -- grants inside the CURRENT fixed window
    total_grants      BIGINT      NOT NULL DEFAULT 0,  -- lifetime, never reset — the farming signal
    last_grant_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (ip_hash, code)
);

-- Sweep/aging reads only; nothing in the request path scans these.
CREATE INDEX IF NOT EXISTS idx_promo_ip_budget_last_grant
    ON promo_ip_budget (last_grant_at);

-- The after-the-fact clawback query: which IPs took the most of one campaign.
CREATE INDEX IF NOT EXISTS idx_promo_ip_budget_code_total
    ON promo_ip_budget (code, total_grants DESC);


-- =============================================================================
-- 5. referrals  — each player's own unique referral code.
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/referral/generate  (FUNCTION NOT YET IN api/ — client-only)
-- Client   : Assets/_Modules/Core/Referral/ReferralService.cs
--
-- generate POSTs:  { playerId }  and expects:
--   { success:true, code, referralUrl }
-- The function should UPSERT one row per player (generate-or-reuse): if the
-- player already has a code, return it; otherwise mint a new unique code and a
-- referralUrl, insert, and return them. The client caches code+url in
-- PlayerPrefs, so the function must return the SAME code on repeat calls.
--
--   player_id    — owner of the code (the referrer). PK: one code per player.
--   code         — the unique shareable code (uppercase; claimers send it
--                  uppercased). UNIQUE so claim can look a referrer up by code.
--   referral_url — "referralUrl" returned to the client for the share sheet.
--   reward_cap   — optional per-period cap on how many successful claims this
--                  referrer is rewarded for (CAP_REACHED in claim). NULL =
--                  no cap. (GUESS: the client lists "Referrer reward cap" as a
--                  backend rule but never sends a number — owner sets policy.)
-- =============================================================================
CREATE TABLE IF NOT EXISTS referrals (
    player_id    TEXT        PRIMARY KEY,            -- "playerId" — the referrer (code owner)
    code         TEXT        NOT NULL UNIQUE,        -- "code" — unique shareable referral code
    referral_url TEXT,                               -- "referralUrl" — deep/share link
    reward_cap   INTEGER,                            -- max rewarded claims (NULL = unlimited); CAP_REACHED gate
    claim_count  INTEGER     NOT NULL DEFAULT 0,     -- denormalised count of successful claims (cap check)
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Look a referrer up by their code (claim flow resolves code → referrer).
CREATE INDEX IF NOT EXISTS idx_referrals_code
    ON referrals (code);


-- =============================================================================
-- 6. referral_claims  — one row each time a player claims someone's code.
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/referral/claim  (FUNCTION NOT YET IN api/ — client-only)
-- Client   : Assets/_Modules/Core/Referral/ReferralService.cs
--
-- claim POSTs:  { playerId, code }  and expects:
--   success: { success:true, reward:{ crystals }, message }
--   failure: { success:false, error:"SELF_REFERRAL"|"ALREADY_CLAIMED"
--                                   |"INVALID_CODE"|"CAP_REACHED" }
--
-- claimer_id = the player redeeming the code; we resolve code → referrer via
-- referrals. The function must enforce:
--   code not in referrals                         → INVALID_CODE
--   referrer player_id == claimer_id              → SELF_REFERRAL
--   claimer already has a row here                → ALREADY_CLAIMED
--                                                   (UNIQUE(claimer_id) below)
--   referrals.claim_count >= referrals.reward_cap → CAP_REACHED
-- On success: insert this row, bump referrals.claim_count, grant the claimer
-- crystals (response reward.crystals), and trigger the referrer's reward.
--
-- UNIQUE(claimer_id): a player may only ever claim ONE referral code (matches
-- the client's "one claim per player" guard). claimer_id is therefore the
-- natural one-per-player key, but we keep a surrogate PK for FK ergonomics.
-- referral_code is FK'd to referrals(code) — can't claim a non-existent code.
-- =============================================================================
CREATE TABLE IF NOT EXISTS referral_claims (
    claim_id      BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    referral_code TEXT        NOT NULL REFERENCES referrals(code) ON DELETE CASCADE,
    referrer_id   TEXT        NOT NULL,             -- denormalised: referrals.player_id at claim time
    claimer_id    TEXT        NOT NULL,             -- "playerId" of the claiming player
    crystals      INTEGER     NOT NULL DEFAULT 0,   -- claimer reward granted (response reward.crystals)
    message       TEXT,                             -- response.message
    claimed_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (claimer_id)                             -- one claim per player ever (ALREADY_CLAIMED gate)
);

-- Count / list claims for a given referral code (cap + referrer dashboards).
CREATE INDEX IF NOT EXISTS idx_referral_claims_code
    ON referral_claims (referral_code);

-- Count a referrer's earned claims (reward cap per referrer).
CREATE INDEX IF NOT EXISTS idx_referral_claims_referrer
    ON referral_claims (referrer_id);


-- =============================================================================
-- 7. tower_swaps  — audit log of paid instant tower swaps (Solana Pay).
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/tower-swap/log
-- Client   : Assets/_Modules/Village/Buildings/TowerSwapService.cs
--
-- Log POSTed after a swap payment. WALLET-GATED (_lib/wallet-auth.authenticate),
-- so player_id is a PROVEN identity, not a body field:
--   { playerId, waveId, fromTower, toTower, currency, costUsdc, txSig, timestamp }
--     playerId   string — the authenticated identity (must match the auth headers)
--     waveId     int    — wave number the swap happened on
--     fromTower  string — tower display name swapped FROM (e.g. "Arcane")
--     toTower    string — tower display name swapped TO
--     currency   string — CurrencyKind.ToString() ("Usdc" | "Skr")   CLIENT-CLAIMED
--     costUsdc   number — flat cost in USDC (currently 2.5)          CLIENT-CLAIMED
--     txSig      string — Solana transaction signature
--     timestamp  long   — client unix epoch SECONDS (note: SECONDS, not millis)
--
-- ── HOW MUCH OF A ROW IS PROOF (corrected 2026-08-15) ────────────────────────
-- These comments used to call tx_sig "on-chain proof" while the endpoint asked
-- the chain NOTHING and trusted every field from the request body. Read the
-- `verification` column before treating any row as evidence of a payment:
--
--   'onchain'        SOLANA_RPC_URL was configured and getTransaction confirmed
--                    the signature exists, succeeded, and was SIGNED BY the
--                    authenticated wallet. Even here, cost_usdc and currency are
--                    still CLIENT-CLAIMED — recipient/amount checking needs the
--                    treasury address + SPL mint and is not implemented yet.
--   'client-claimed' No RPC configured (or a guest-rail row). A business record
--                    of what the client said happened. NOT proof of payment.
--
-- tx_sig is UNIQUE so the same signature can't be logged twice. tx_sig may be
-- NULL if a swap path ever logs without a signature, so the UNIQUE index is
-- partial (NULLs allowed, only non-null sigs deduped). Note the consequence the
-- endpoint now alarms on: a conflict against a row owned by a DIFFERENT player
-- is signature squatting, not an honest retry.
-- =============================================================================
CREATE TABLE IF NOT EXISTS tower_swaps (
    swap_id    BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    player_id  TEXT        NOT NULL,                -- "playerId"
    wave_id    INTEGER,                             -- "waveId"
    from_tower TEXT,                                -- "fromTower" (display name)
    to_tower   TEXT,                                -- "toTower" (display name)
    currency   TEXT,                                -- "currency" ("Usdc" | "Skr")
    cost_usdc  NUMERIC(12,4),                       -- "costUsdc" (flat 2.5)
    tx_sig     TEXT,                                -- "txSig" (Solana signature)
    client_ts  BIGINT,                              -- "timestamp" — client unix epoch SECONDS
    logged_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),  -- server receive time
    -- 'onchain' | 'client-claimed' — see the header. NEVER read a row as proof of
    -- payment without checking this.
    verification TEXT NOT NULL DEFAULT 'client-claimed'
);

-- Existing deployments predate the column; every pre-existing row was written
-- with NO on-chain check at all, so 'client-claimed' is the truthful backfill.
ALTER TABLE tower_swaps
    ADD COLUMN IF NOT EXISTS verification TEXT NOT NULL DEFAULT 'client-claimed';

-- One player's swap history, newest first.
CREATE INDEX IF NOT EXISTS idx_tower_swaps_player_time
    ON tower_swaps (player_id, logged_at DESC);

-- Dedup on-chain payments (only non-null signatures are constrained).
CREATE UNIQUE INDEX IF NOT EXISTS uq_tower_swaps_tx_sig
    ON tower_swaps (tx_sig)
    WHERE tx_sig IS NOT NULL;


-- =============================================================================
-- 8. bug_reports  — in-game bug reports (the "?"/gear Help menu).
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/bug-report   (FUNCTION NOT YET IN api/ — client-only)
-- Client   : Assets/_Modules/HUD/HelpMenu.cs
--
-- IMPORTANT: HelpMenu posts to a DIFFERENT host than every other service:
--   https://defenders-of-the-realm.vercel.app/api/bug-report   (NO "-v2")
-- The save/load/analytics/promo/referral/tower-swap services all use
--   https://defenders-of-the-realm-v2.vercel.app/...
-- So bug-report currently lands in the OLDER (React app's) deployment. See
-- DB_SETUP.md → "Host mismatch" for the decision the owner must make
-- (point the client at -v2, or run this table on the older project's DB).
--
-- The client POSTs:
--   { "description": <text>,
--     "context": { "route": <sceneName>, "appVersion": <Application.version> } }
-- Description is capped at 4000 chars client-side (per HelpMenu comment) and
-- carries the auto-captured block (scene, time, build, device, screen, the
-- on-disk screenshot PATH — the image itself is NOT uploaded, only its path).
-- No playerId is sent by HelpMenu; we still add a nullable player_id column for
-- when the function is wired to attach the caller's wallet.
-- =============================================================================
CREATE TABLE IF NOT EXISTS bug_reports (
    report_id    BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    description  TEXT        NOT NULL,               -- "description" (<= 4000 chars)
    route        TEXT,                               -- context.route (active scene name)
    app_version  TEXT,                               -- context.appVersion (Application.version)
    player_id    TEXT,                               -- client-side SALTED HASH of the Pi uid
    wallet       TEXT,                               -- ⭐ SERVER-VERIFIED wallet, or NULL. Never a claim.
    context      JSONB       NOT NULL DEFAULT '{}',  -- full "context" object, future-proofed
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ⭐ `wallet` (owner ruling 2026-08-24). player_id is a SALTED HASH while every
-- money and auth table keys on the RAW wallet, so a bug report could not be
-- correlated with that player's purchases or auth rejects at all -- "this reporter
-- also has an unfulfilled purchase" was simply not answerable. This column makes it
-- answerable.
--
-- ⛔ IT HOLDS A SERVER-VERIFIED WALLET OR NOTHING. api/bug-report.js resolves it by
-- calling verifySession() on the `x-session` bearer; a client-asserted wallet is
-- NEVER written here (it goes to context.verifiedWallet's absence, i.e. nowhere).
-- A column that sometimes holds a proof and sometimes a claim cannot be joined
-- safely -- you would never know which rows are evidence.
--
-- ⚠ AND AN UNVERIFIED REPORT IS STILL STORED, wallet NULL. The player whose auth is
-- broken is exactly the player most likely to file a bug; gating the sink on the
-- signed rail would drop the highest-value reports we have.
CREATE INDEX IF NOT EXISTS idx_bug_reports_wallet ON bug_reports (wallet) WHERE wallet IS NOT NULL;

-- DRIFT RECONCILE (2026-08-02) — THE REASON bug_reports HAS 0 ROWS.
-- Captured from production, request 02:25:43 UTC 2026-08-03:
--     NeonDbError: column "player_id" of relation "bug_reports" does not exist
--     SQLSTATE 42703, api/bug-report.js:124
-- The LIVE table was created before this file's definition and is missing columns
-- api/bug-report.js writes, so EVERY report a tester has submitted from Settings
-- returned 500 and was never stored. Same class of drift as player_data above.
-- CREATE TABLE ... IF NOT EXISTS does NOT alter an existing table, so every
-- column has to be added explicitly. Additive + idempotent (no data touched):
--
-- ROUND 2 (2026-08-03) — this block forgot its own lesson and omitted the PK.
-- Captured from production runtime logs, request 22:51:45 UTC 2026-08-03, on the
-- deploy that promoted this file's round-1 fix:
--     NeonDbError: column "report_id" does not exist
--     SQLSTATE 42703, position 29, api/admin/db.js:288
-- Round 1 added the five columns named below, so the error simply MOVED to the
-- next missing column — report_id, which every read AND write path leads with:
--   * api/admin/db.js:289/295 (view=bugreports) and :313/320/326 (view=bugreport)
--     -> HTTP 500, which is why the admin view broke the moment it went live.
--   * api/bug-report.js:169/178/188/200 -- all four RETURNING report_id clauses
--     raise 42703, so the :217-234 cascade burns attempts 1-4 and lands on
--     attempt 5 (description_only_no_returning). A report DOES store, but as one
--     flat text blob with reportId null and an EMPTY context -- no screenshot,
--     no trace tail, no player attribution. The endpoint still returns 200, so
--     the loss is invisible except for the console.warn at :222.
-- Identity WITHOUT the PRIMARY KEY clause on purpose: the live table may already
-- carry a PK under another name, and ADD PRIMARY KEY would hard-fail the whole
-- script. Identity alone is all the after_id cursor needs. The table has 0 rows,
-- so the backfill touches nothing.
ALTER TABLE bug_reports ADD COLUMN IF NOT EXISTS report_id   BIGINT      GENERATED ALWAYS AS IDENTITY;
CREATE UNIQUE INDEX IF NOT EXISTS uq_bug_reports_report_id ON bug_reports (report_id);
ALTER TABLE bug_reports ADD COLUMN IF NOT EXISTS route       TEXT;
ALTER TABLE bug_reports ADD COLUMN IF NOT EXISTS app_version TEXT;
ALTER TABLE bug_reports ADD COLUMN IF NOT EXISTS player_id   TEXT;
ALTER TABLE bug_reports ADD COLUMN IF NOT EXISTS wallet      TEXT;   -- 2026-08-24, see above
ALTER TABLE bug_reports ADD COLUMN IF NOT EXISTS context     JSONB       NOT NULL DEFAULT '{}';
ALTER TABLE bug_reports ADD COLUMN IF NOT EXISTS created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW();

-- Recent reports first (triage view).
CREATE INDEX IF NOT EXISTS idx_bug_reports_created
    ON bug_reports (created_at DESC);


-- =============================================================================
-- 9. player_profiles  — the identity layer the leaderboard needs (WO-129 §2.2).
-- -----------------------------------------------------------------------------
-- Endpoint : GET  /api/profile?wallet=<addr>        (api/profile/get.js)
--            POST /api/profile/username             (api/profile/username.js)
--            POST /api/profile/social/link|unlink   (api/profile/social.js)
-- Client   : ProfileService (Core) — WO-129 §4 (NEW; not yet built)
--
-- The durable identity key stays the WALLET (player_id, same join key as every
-- other table). username is a DISPLAY LABEL mapped onto that wallet — the public
-- name shown on the leaderboard instead of a raw address (WO-129 §2.2).
--
--   wallet           — base58 address (PK). Same value as player_data.player_id;
--                      NOT FK'd (a profile may be created before the first save).
--   username         — public display name. NULL until the player sets one (the
--                      client shows "Defender#<short-wallet>" as a default until
--                      then; we do NOT persist that default — it's derived).
--   username_ci      — lower(username), kept UNIQUE so the server enforces
--                      case-insensitive uniqueness (WO-129 §2.2 USERNAME_TAKEN).
--                      A generated column so it can never drift from username.
--   avatar_id        — chosen hero/portrait id (defaults to current hero client-
--                      side; NULL = "use current hero"). No new art for MVP.
--   social_links     — JSONB opt-in map { "x": {handle, public}, "discord":{...} }.
--                      Opt-in only; surfaced publicly only when public=true.
--   created_at       — first profile touch.
--   renamed_at       — last username change (NULL = never renamed). Backs the
--                      "one free rename, then cost/cap" policy (WO-129 §2.2 US-8);
--                      the rename-cost lever itself is enforced client/economy
--                      side — this column is the server's timestamp of record.
--
-- username_ci is UNIQUE; the username endpoint maps a 23505 on it → USERNAME_TAKEN.
-- =============================================================================
CREATE TABLE IF NOT EXISTS player_profiles (
    wallet       TEXT        PRIMARY KEY,            -- base58 address (= player_data.player_id)
    username     TEXT,                               -- public display name (NULL until set)
    username_ci  TEXT        GENERATED ALWAYS AS (lower(username)) STORED, -- case-insensitive key
    avatar_id    TEXT,                               -- chosen hero/portrait (NULL = current hero)
    social_links JSONB       NOT NULL DEFAULT '{}',  -- opt-in { provider: { handle, public } }
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    renamed_at   TIMESTAMPTZ                         -- last username change (NULL = never)
);

-- Case-insensitive uniqueness for usernames (NULLs excluded → many un-named rows OK).
CREATE UNIQUE INDEX IF NOT EXISTS uq_player_profiles_username_ci
    ON player_profiles (username_ci)
    WHERE username_ci IS NOT NULL;


-- =============================================================================
-- 10. leaderboard_scores  — server-authoritative standings (WO-129 §2.1).
-- -----------------------------------------------------------------------------
-- Endpoint : GET  /api/leaderboard?metric=<m>&period=<id>&wallet=<addr>
--                                                   (api/leaderboard/get.js)
--            POST /api/leaderboard/submit           (api/leaderboard/submit.js)
-- Client   : LeaderboardService (Core) — WO-129 §4 (NEW; read-only client).
--
-- ONE row per (wallet, metric, period_id) — a player's BEST score on that board
-- for that period. The submit endpoint only ever RAISES a score (a max-merge via
-- GREATEST on conflict), so it is a monotonic high-water mark per board and a
-- replayed/stale submit can never lower a standing. The client NEVER asserts a
-- final rank — rank is computed at read time by ordering scores (WO-129 §5:
-- "NO client-authoritative scores").
--
--   wallet      — the player (= player_data.player_id). NOT FK'd (score may land
--                 before first save).
--   metric      — which board: 'highest_wave' (SHIP FIRST), 'longest_hold',
--                 'total_resources', 'clan', 'arena' (reserved). Free TEXT so a
--                 new board needs no migration — the endpoints whitelist values.
--   period_id   — 'alltime' (never resets) or a week key 'YYYY-Www' (e.g.
--                 '2026-W22', resets Mon 00:00 UTC — WO-129 §2.1).
--   score       — the standing value (BIGINT — wave count, seconds held, total
--                 resources). Higher = better for every MVP board.
--   meta        — optional JSONB context for the row (e.g. run id, hero used).
--   updated_at  — when this best was last raised.
--
-- The PK (wallet, metric, period_id) is what makes submit idempotent + a clean
-- upsert target; uq is implied by the composite PK.
-- =============================================================================
CREATE TABLE IF NOT EXISTS leaderboard_scores (
    wallet     TEXT        NOT NULL,                 -- player (= player_data.player_id)
    metric     TEXT        NOT NULL,                 -- board id ('highest_wave', ...)
    period_id  TEXT        NOT NULL,                 -- 'alltime' | 'YYYY-Www'
    score      BIGINT      NOT NULL DEFAULT 0,       -- standing (higher = better)
    meta       JSONB       NOT NULL DEFAULT '{}',    -- optional row context
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (wallet, metric, period_id)
);

-- The ranking query: ORDER BY score DESC within one board. This index makes both
-- the top-N read and the caller's rank-window (COUNT scores above them) fast.
CREATE INDEX IF NOT EXISTS idx_leaderboard_scores_board_rank
    ON leaderboard_scores (metric, period_id, score DESC);


-- =============================================================================
-- 11. achievement_grants  — one-time, server-validated grants (WO-129 §2.4).
-- -----------------------------------------------------------------------------
-- Endpoint : POST /api/referral/install-brag        (api/referral/install-brag.js)
-- Client   : ReferralService (Core) — install-brag bonus (WO-129 §2.4, extend).
--
-- Generic idempotency ledger for "grant this player X exactly once". The install
-- brag is modelled as achievement_id = 'install_brag'. The PK (wallet,
-- achievement_id) STRUCTURALLY prevents a double-grant — a second request for the
-- same pair violates the PK, which the endpoint maps to "already granted" and
-- returns the original (no second reward). This is the same anti-abuse shape the
-- WO calls for: "the client requests; the server grants", keyed on durable wallet.
--
--   wallet         — who was granted (= player_data.player_id). NOT FK'd.
--   achievement_id — the one-time grant key ('install_brag', and future grants).
--   reward         — JSONB snapshot of what was granted (e.g. { crystals, flair })
--                    so the audit record is self-describing even if policy changes.
--   granted_at     — when (also the idempotency timestamp returned on re-request).
-- =============================================================================
CREATE TABLE IF NOT EXISTS achievement_grants (
    wallet         TEXT        NOT NULL,             -- player (= player_data.player_id)
    achievement_id TEXT        NOT NULL,             -- 'install_brag' | future one-time grants
    reward         JSONB       NOT NULL DEFAULT '{}',-- snapshot of granted reward (audit)
    granted_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (wallet, achievement_id)
);

-- List a player's one-time grants (profile "Founding Herald" flair, audits).
CREATE INDEX IF NOT EXISTS idx_achievement_grants_wallet
    ON achievement_grants (wallet);


-- =============================================================================
-- OPTIONAL SEED — a test promo code so /api/promo/redeem can be smoke-tested.
-- The client ships an editor menu "Simulate Promo Redeem (TEST10)", so seed it.
--
-- ⚠ GUARDED 2026-08-18 — THIS BLOCK USED TO RUN UNCONDITIONALLY. Its own comment
-- said "Comment this out in production if you don't want a live test code" and it
-- was NOT commented out. schema.sql is what api/DEPLOY.md step 2 tells the operator
-- to paste into the Neon SQL editor, so following the deploy checklist SEEDED A
-- LIVE, PUBLIC, UNCAPPED, NEVER-EXPIRING FREE-CRYSTAL CODE into production:
--     active = TRUE, max_redemptions = NULL (unlimited), per_player_limit = NULL,
--     expires_at = NULL (forever), bound_wallet = NULL (anyone).
-- The game is PUBLISHED and the redeem door is reachable in the Realm Store
-- (PackStore.cs:207-213, deliberately outside the purchase feature flag), so
-- "TEST10" is four keystrokes from being posted somewhere public. A relying-on-
-- a-human-to-remember safety note is not a safety mechanism; the default had to
-- change. Nothing is deleted here — the seed still exists, it is now OPT-IN.
--
-- ⛔ NOTE THE COMMENT IS KEPT, NOT STRIPPED. The record of what the default was is
-- the reason the next reader believes the guard matters.
--
-- HOW TO SEED IT DELIBERATELY (dev/staging only) — run in the SAME session:
--     SET dotr.seed_test_codes = 'on';
--     \i schema.sql            -- (or paste this file)
-- Anything else — a plain paste, a migration runner, a fresh Neon project — leaves
-- TEST10 UNSEEDED. Re-running with the flag on is still idempotent.
--
-- Even when opted in, the seeded row is now DEFANGED relative to the old one:
-- capped at 25 total redemptions and expiring 30 days out, so a leak from a test
-- environment has a bounded blast radius instead of an unbounded one. Adjust
-- deliberately if a test needs more; never remove the caps to "make testing easier".
--
-- ⚠ THIS GUARD PROTECTS FUTURE APPLICATIONS OF schema.sql. It says NOTHING about
-- whether the row is ALREADY LIVE in the production database from an earlier run —
-- this file cannot know that, and deleting production data is the OWNER'S call, not
-- this script's. To find out, run against production (read-only):
--     SELECT code, active, reward_crystals, reward_coins, max_redemptions,
--            per_player_limit, expires_at, bound_wallet, created_at
--       FROM promo_codes WHERE code = 'TEST10';
--     SELECT COUNT(*) AS burned FROM promo_redemptions WHERE code = 'TEST10';
-- If it is present and active, the least-destructive remedy is a kill-switch flip,
-- NOT a delete (promo_redemptions.code is FK'd ON DELETE CASCADE, so deleting the
-- code would also erase the audit trail of who redeemed it):
--     UPDATE promo_codes SET active = FALSE WHERE code = 'TEST10';
-- =============================================================================
DO $seed_test_codes$
BEGIN
    -- current_setting(..., true) returns NULL instead of erroring when the setting
    -- was never set, which is precisely the "plain paste" case that must NOT seed.
    IF COALESCE(current_setting('dotr.seed_test_codes', true), 'off') = 'on' THEN
        INSERT INTO promo_codes (
            code, reward_crystals, reward_coins, message, active,
            max_redemptions, expires_at
        )
        VALUES (
            'TEST10', 10, 0, 'Thanks for testing — 10 Aether Crystals!', TRUE,
            25, NOW() + INTERVAL '30 days'
        )
        ON CONFLICT (code) DO NOTHING;
        RAISE NOTICE 'schema.sql: TEST10 promo code SEEDED (dotr.seed_test_codes=on). Capped at 25 redemptions, expires in 30 days. DO NOT DO THIS IN PRODUCTION.';
    ELSE
        RAISE NOTICE 'schema.sql: TEST10 promo code NOT seeded (dotr.seed_test_codes is off/unset). This is the safe default.';
    END IF;
END
$seed_test_codes$;

-- =============================================================================
-- dungeon_status — WO-1114. One row per dungeon door. PUBLIC READ, no auth.
--
--   A closed dungeon must read as WORLD, never as BUILD STATUS. headline and
--   body are AUTHORED PROSE shown to the player at the door. NEVER write
--   "under construction", "coming soon", "disabled", "WIP" or "TODO" into them.
--   Assets/Editor/Regression/DungeonStatusRegression.cs enforces that rule on
--   the CLIENT's default copy, but it cannot see rows written here — so the
--   status vocabulary is ALSO pinned below by CHECK constraint, and the prose
--   rule is stated here because this table is outside the oracle's reach.
--
--   ⛔ CORRECTED 2026-08-26 (owner ruling, WO-1223). These two paragraphs used to
--   read "the client treats an unknown status as OPEN and warns (it never fails
--   closed)" and "an EMPTY table is correct and healthy — absence means open".
--   BOTH ARE NOW FALSE, and inverted deliberately. The client fails CLOSED: an
--   unparseable status, a missing row and an empty table all seal the door. The
--   CHECK constraint below therefore stops being belt-and-braces and becomes a
--   real outage guard — a status typo that reaches this table SHUTS that dungeon.
--   ⚠ Every seed is 'open' ON PURPOSE. Which dungeons are finished is a
--   play-feel judgement the owner has not made yet (WO-1114 §9), and seeding
--   the wrong pair would close FINISHED content.
--
-- Written by : api/admin/db.js (admin, X-Admin-Key) or the Neon SQL editor.
-- Read by    : api/dungeon-status.js (public GET).
-- =============================================================================
CREATE TABLE IF NOT EXISTS dungeon_status (
    dungeon_id TEXT        PRIMARY KEY,
    status     TEXT        NOT NULL DEFAULT 'open'
                           CHECK (status IN ('open','sealed','collapsed','rescue','flooded')),
    headline   TEXT,
    body       TEXT,
    sigil      TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- OWNER RULING 2026-08-26 (WO-1223): "not acesable if not in table, if in table and
-- works then yes". The CLIENT now FAILS CLOSED - DungeonStatusCatalog.For resolves an
-- absent row, an absent table, a rejected payload and an unparseable status all to
-- Sealed. Two things follow, and both are load-bearing:
--   * A SEED ROW IS NO LONGER COSMETIC. The comment above says the seeds "exist only so
--     the admin DB viewer shows something"; that is no longer true. A gated dungeon with
--     no row is SHUT to every player. Every id in api/_lib/dungeon-manifest.json marked
--     accounting='portal-gated' MUST have a row here, and
--     test/dungeon-status.manifest.test.js reds if one does not.
--   * ⚠ ON CONFLICT DO NOTHING means re-running this file will NOT add the two rows
--     below to a database provisioned before today. Insert them into Neon by hand (or
--     through api/admin/db.js) or those two doors stay closed in production.
-- Every seed stays 'open' ON PURPOSE (WO-1114 §9) - which dungeons are finished is the
-- owner's play-feel call, and seeding a wrong 'sealed' would close finished content.
INSERT INTO dungeon_status (dungeon_id, status) VALUES
    ('dg_starter_loop',    'open'),
    ('dg_sunken_vault',    'open'),
    ('dg_bonecrypt',       'open'),
    ('dg_ember_deep',      'open'),
    ('dg_folks_granary',   'open'),
    -- OWNER RULING 2026-08-26: 'sealed', NOT 'open'. This is the dungeon that BLACK-SCREENED
    -- her (WO-1223) -- being able to shut it was the entire reason the row was wanted. The
    -- "every seed stays 'open' ON PURPOSE" rule above guards against closing FINISHED content;
    -- this is not finished content, so the rule does not apply. Written to the live Neon DB the
    -- same day and verified by shape query (6/6 rows). Flip to 'open' when the black screen is
    -- fixed and the owner has felt-tested it -- one UPDATE, no deploy.
    ('dg_healers_cottage', 'sealed')
ON CONFLICT (dungeon_id) DO NOTHING;

-- =============================================================================
-- 15. purchase_entitlements — MON-1147 durable, replay-safe purchase authority.
-- A row exists only after the backend independently verifies the finalized chain
-- transaction against its own SKU/amount/recipient contract.
-- =============================================================================
CREATE TABLE IF NOT EXISTS purchase_entitlements (
    entitlement_id      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tx_signature        TEXT NOT NULL UNIQUE,
    wallet              TEXT NOT NULL,
    sku                 TEXT NOT NULL,
    -- ⭐ WO-1318: 'pi' joins 'solana'. Pi is a SECOND RAIL INTO THE SAME LEDGER,
    -- not a second ledger: revenue reporting, patronage totals and the replay
    -- guard all read this one table, and a rail that kept its own grant rows
    -- would silently sit outside every one of them.
    -- MIGRATION on a live table (api/migrations/20260902_0017_pi_payments.sql):
    --   ALTER TABLE purchase_entitlements DROP CONSTRAINT purchase_entitlements_rail_check;
    --   ALTER TABLE purchase_entitlements ADD  CONSTRAINT purchase_entitlements_rail_check
    --       CHECK (rail IN ('solana','pi'));
    rail                TEXT NOT NULL CHECK (rail IN ('solana','pi')),
    -- ⚠ 'mainnet-beta' IS THE SPELLING THE CODE SENDS. api/purchases/verify.js takes
    -- `network` straight off the wire, where it is 'devnet' | 'mainnet-beta' (the
    -- Solana cluster name; PurchaseEntitlementVerifier.WireNetwork). This CHECK
    -- used to list 'mainnet' only, so a fresh database built from THIS FILE would
    -- reject every mainnet insert with the money already moved. Corrected 2026-08-23
    -- (WO-1158). 'mainnet' is kept for any row an older deployment already wrote.
    -- MIGRATION on a live table:
    --   ALTER TABLE purchase_entitlements DROP CONSTRAINT purchase_entitlements_network_check;
    --   ALTER TABLE purchase_entitlements ADD  CONSTRAINT purchase_entitlements_network_check
    --       CHECK (network IN ('devnet','mainnet','mainnet-beta'));
    -- 'pi' added WO-1318. Pi has ONE network from our side: testnet vs mainnet is
    -- a property of the API KEY and the Pi Browser, never of a request field, so
    -- there is deliberately no 'pi-testnet' value a client could ask for.
    network             TEXT NOT NULL CHECK (network IN ('devnet','mainnet','mainnet-beta','pi')),
    currency            TEXT NOT NULL CHECK (currency IN ('SOL','USDC','SKR','PI')),
    -- ⚠ THE NAME IS LEGACY, THE MEANING IS "BASE UNITS OF WHATEVER WAS PAID".
    -- SKR at 6 or 9 decimals, Pi at 7 (WO-1318). Read `currency` + the rail before
    -- interpreting this number; never assume lamports.
    expected_lamports   BIGINT NOT NULL CHECK (expected_lamports > 0),
    observed_lamports   BIGINT NOT NULL CHECK (observed_lamports > 0),
    recipient           TEXT NOT NULL,
    observed_recipient  TEXT NOT NULL,
    chain_slot          BIGINT,
    status              TEXT NOT NULL CHECK (status IN ('verified','fulfilled','manual_review')),
    verified_at         TIMESTAMPTZ NOT NULL,
    fulfilled_at        TIMESTAMPTZ,
    -- ── WO-1158: WHAT THE PLAYER ACTUALLY BOUGHT, AT WHAT PRICE ──────────────
    -- Owner requirement, verbatim: "they buy for 3 skr at X price so thats what
    -- resolves on db". A row that stores only the base-unit amount cannot answer
    -- "what was this worth?" six months later, and a third-party rate source on
    -- the money path makes that question inevitable — a disputed charge has to be
    -- reconstructable from the row alone, never re-derived from today's market.
    -- All four are NULL on the two CANARY skus, whose amount is a pinned protocol
    -- constant with no rate behind it (rate_source reads 'server-pinned').
    quote_ref           TEXT,                        -- the purchase_quotes.quote_ref this settled
    usd_anchor          NUMERIC(12,4),               -- the authored ladder price, e.g. 2.99
    usd_rate            NUMERIC(24,12),              -- USD per SKR used to derive the amount
    rate_source         TEXT,                        -- WHICH oracle, e.g. 'coingecko:seeker:low_24h'
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (wallet, tx_signature, sku)
);

-- MIGRATION for an existing database (idempotent, nullable, safe on a live table):
--   ALTER TABLE purchase_entitlements ADD COLUMN IF NOT EXISTS quote_ref  TEXT;
--   ALTER TABLE purchase_entitlements ADD COLUMN IF NOT EXISTS usd_anchor NUMERIC(12,4);
--   ALTER TABLE purchase_entitlements ADD COLUMN IF NOT EXISTS usd_rate   NUMERIC(24,12);
--   ALTER TABLE purchase_entitlements ADD COLUMN IF NOT EXISTS rate_source TEXT;

CREATE INDEX IF NOT EXISTS idx_purchase_entitlements_wallet
    ON purchase_entitlements (wallet, created_at DESC);

-- =============================================================================
-- 15b. google_play_purchases — WO-1255 dormant Google Play proof/state ledger.
-- Applying the schema does not activate this rail. api/purchases/google-play-*.js
-- additionally require GOOGLE_PLAY_BILLING_ENABLED=true, the exact package,
-- account-binding HMAC key, and a Play-authorized service account.
-- =============================================================================
CREATE TABLE IF NOT EXISTS google_play_purchases (
    purchase_token      TEXT PRIMARY KEY,
    player_id           TEXT NOT NULL,
    package_name        TEXT NOT NULL,
    product_id          TEXT NOT NULL,
    sku                 TEXT NOT NULL,
    product_type        TEXT NOT NULL CHECK (product_type IN ('consumable','non_consumable','subscription')),
    state               TEXT NOT NULL CHECK (state IN
        ('created','pending','purchased','verified','granted','consumed','acknowledged',
         'cancelled','voided','refunded')),
    obfuscated_account_id TEXT NOT NULL,
    google_order_id     TEXT,
    purchase_time       TIMESTAMPTZ,
    verified_at         TIMESTAMPTZ,
    granted_at          TIMESTAMPTZ,
    finalized_at        TIMESTAMPTZ,
    last_google_state   JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (package_name, product_id, purchase_token)
);

CREATE INDEX IF NOT EXISTS idx_google_play_purchases_player
    ON google_play_purchases (player_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_google_play_purchases_unfinished
    ON google_play_purchases (updated_at)
    WHERE state IN ('created','pending','purchased','verified','granted');

-- Authenticated Pub/Sub inbox for Google Play Real-time Developer Notifications.
-- Quarantine is deliberately durable: RTDN is a change hint, not proof, and this
-- table must not be mistaken for an entitlement-reversal implementation.
CREATE TABLE IF NOT EXISTS google_play_rtdn_messages (
    message_id          TEXT PRIMARY KEY,
    package_name       TEXT NOT NULL,
    notification_kind  TEXT NOT NULL CHECK (notification_kind IN
        ('oneTimeProductNotification','subscriptionNotification',
         'voidedPurchaseNotification','pendingRefundReviewNotification','testNotification')),
    event_time          TIMESTAMPTZ NOT NULL,
    status              TEXT NOT NULL CHECK (status IN
        ('processing','processed','quarantined','retry')),
    quarantine_reason   TEXT,
    purchase_token      TEXT,
    google_order_id     TEXT,
    pending_refund_token TEXT,
    attempts            INTEGER NOT NULL DEFAULT 1 CHECK (attempts > 0),
    processed_at        TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_google_play_rtdn_attention
    ON google_play_rtdn_messages (status, updated_at)
    WHERE status IN ('quarantined','retry');

-- Default-off pull safety net for voids that are missed or delayed on RTDN.
-- Observations remain quarantined until an explicit, audited entitlement
-- reversal policy is implemented; these tables never revoke by themselves.
CREATE TABLE IF NOT EXISTS google_play_voided_events (
    event_fingerprint  TEXT PRIMARY KEY,
    package_name       TEXT NOT NULL,
    purchase_token     TEXT,
    google_order_id    TEXT,
    purchase_time      TIMESTAMPTZ,
    voided_time        TIMESTAMPTZ,
    voided_source      INTEGER CHECK (voided_source BETWEEN 0 AND 2),
    voided_reason      INTEGER CHECK (voided_reason BETWEEN 0 AND 8),
    voided_quantity    INTEGER CHECK (voided_quantity > 0),
    status             TEXT NOT NULL CHECK (status IN ('quarantined','resolved','ignored')),
    quarantine_reason  TEXT NOT NULL,
    google_payload     JSONB NOT NULL,
    observed_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_google_play_voided_attention
    ON google_play_voided_events (status, observed_at)
    WHERE status = 'quarantined';
CREATE INDEX IF NOT EXISTS idx_google_play_voided_purchase
    ON google_play_voided_events (purchase_token, voided_time DESC);

CREATE TABLE IF NOT EXISTS google_play_voided_cursors (
    package_name                TEXT PRIMARY KEY,
    last_success_start_time_ms  BIGINT NOT NULL CHECK (last_success_start_time_ms >= 0),
    last_success_end_time_ms    BIGINT NOT NULL CHECK (last_success_end_time_ms >= last_success_start_time_ms),
    last_success_at             TIMESTAMPTZ NOT NULL,
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Authenticated account/data deletion requests. This queue is intentionally
-- separate from destructive execution: an operator must apply the documented
-- retention and pseudonymization rules before marking a request complete.
CREATE TABLE IF NOT EXISTS account_deletion_requests (
    request_id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    player_id          TEXT        NOT NULL,
    identity_kind      TEXT        NOT NULL CHECK (identity_kind IN ('wallet', 'google', 'guest')),
    request_scope      TEXT        NOT NULL CHECK (request_scope IN ('account', 'associated_data')),
    request_categories TEXT[]      NOT NULL DEFAULT ARRAY[]::TEXT[],
    status             TEXT        NOT NULL DEFAULT 'requested'
                                  CHECK (status IN ('requested', 'in_progress', 'completed', 'rejected', 'cancelled')),
    requested_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at       TIMESTAMPTZ,
    operator_note      TEXT,
    CHECK (
        (request_scope = 'account' AND cardinality(request_categories) = 0)
        OR
        (request_scope = 'associated_data' AND cardinality(request_categories) > 0)
    ),
    CHECK (request_categories <@ ARRAY[
        'cloud_saves', 'gameplay_analytics', 'diagnostics', 'bug_reports'
    ]::TEXT[])
);
CREATE UNIQUE INDEX IF NOT EXISTS account_deletion_requests_one_active_per_player
    ON account_deletion_requests (player_id)
    WHERE status IN ('requested', 'in_progress');
CREATE INDEX IF NOT EXISTS account_deletion_requests_status_requested
    ON account_deletion_requests (status, requested_at);

-- =============================================================================
-- 16. purchase_quotes — WO-1158. THE SERVER'S OWN PRICE, WRITTEN DOWN.
-- -----------------------------------------------------------------------------
-- Issued by POST /api/purchases/quote, spent by POST /api/purchases/verify.
--
-- ⛔ WHY A TABLE AND NOT A SIGNED BLOB IN THE CLIENT'S POCKET: a quote must be
-- SINGLE-USE, and single-use needs somewhere to record that it was used. The
-- UNIQUE quote_ref plus the conditional UPDATE in verify.js is that record, and
-- it is the only thing standing between us and one favourable quote being
-- replayed across many payments.
--
-- ⛔ AND IT MUST EXPIRE. An unexpiring quote is a free option on a volatile
-- asset: a player could sit on a good rate and exercise it after the market
-- moved. TTL is 5 minutes (purchase-catalog.QUOTE_TTL_SECONDS).
--
-- ⚠ EXPIRY IS JUDGED AGAINST THE TRANSACTION'S blockTime, NOT AGAINST WALL CLOCK
-- AT VERIFY TIME. Wallet approval is a human action with no countdown and chain
-- finality is not instant, so "now" at verify time would refuse honest players
-- whose money has already moved. A payment that lands outside the window anyway
-- is recorded 'manual_review', never discarded.
--
-- The two CANARY skus never appear here at all: their amount is a pinned protocol
-- constant, not a quoted price.
-- =============================================================================
CREATE TABLE IF NOT EXISTS purchase_quotes (
    quote_id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    -- The opaque id handed to the client. Random, NOT the serial: an enumerable
    -- id invites guessing at other players' quotes.
    quote_ref           TEXT NOT NULL UNIQUE,
    wallet              TEXT NOT NULL,          -- the PROVEN wallet the quote was issued to
    sku                 TEXT NOT NULL,
    -- 'pi' + 'PI' added WO-1318: the Pi rail issues its price out of THIS table.
    -- One quote table, one TTL, one single-use rule, two rails.
    network             TEXT NOT NULL CHECK (network IN ('devnet','mainnet-beta','pi')),
    currency            TEXT NOT NULL CHECK (currency IN ('SKR','PI')),
    -- ⛔ THE EXACT INTEGER THE CLIENT MUST TRANSFER. Stored as NUMERIC(40,0), not
    -- BIGINT, because base units at 9 decimals leave far less headroom than they
    -- look like they do and no price should ever be capped by its column.
    amount_base_units   NUMERIC(40,0) NOT NULL CHECK (amount_base_units > 0),
    -- ⛔ READ OFF THE MINT, NEVER OFF A DOC OR A SIBLING NETWORK: devnet test SKR
    -- is 9, mainnet SKR is 6. Persisted per-quote so a later decimals change can
    -- never retro-reinterpret an already-issued amount.
    decimals            SMALLINT NOT NULL CHECK (decimals >= 0 AND decimals <= 18),
    -- ⚠ NULLABLE SINCE WO-1318 — these three are SOLANA facts. A Pi quote has no
    -- mint and no associated token account; its payee is the app's own Pi wallet,
    -- which Pi resolves from the API key and reports back as the payment's
    -- `to_address`. They remain NOT-NULL-in-practice on the Solana rail, where
    -- purchases/quote.js always writes all three, and contractFromQuoteRow()
    -- refuses a row that is missing them.
    mint                TEXT,
    recipient           TEXT,
    recipient_ata       TEXT,
    usd_anchor          NUMERIC(12,4) NOT NULL,     -- the authored ladder price (2.99, 4.99, ...)
    usd_rate            NUMERIC(24,12) NOT NULL,    -- USD per SKR at issue time
    rate_source         TEXT NOT NULL,              -- WHICH oracle produced usd_rate
    issued_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at          TIMESTAMPTZ NOT NULL,
    consumed_at         TIMESTAMPTZ,
    consumed_tx         TEXT,                       -- the signature that spent it
    -- ⭐ WO-1177, the shortfall discount. BOTH NULLABLE BY DESIGN: every quote
    -- issued before 2026-08-24, and every undiscounted quote after it,
    -- legitimately has no discount. A NOT NULL DEFAULT 0 would make "no discount"
    -- and "a zero-bps discount" indistinguishable — and this column exists
    -- precisely so the real discount rate is a number we can READ, not assume.
    discount_bps        INT,                        -- basis points off usd_anchor (2000 = 20%)
    -- ⛔ The SERVER's label, never the client's `reason` hint. That hint is
    -- logged and never trusted; storing it here would turn an audit column into
    -- a repetition of whatever the caller typed.
    discount_reason     TEXT
);

-- The lookup verify.js actually does.
CREATE INDEX IF NOT EXISTS idx_purchase_quotes_wallet_sku
    ON purchase_quotes (wallet, sku, issued_at DESC);
-- Sweeping expired, never-consumed quotes.
CREATE INDEX IF NOT EXISTS idx_purchase_quotes_expiry
    ON purchase_quotes (expires_at) WHERE consumed_at IS NULL;
-- ⭐ WO-1177's 7-day rate limit asks "has this wallet had a discounted quote
-- since NOW() - 7 days". Without this index that is a full scan of every quote
-- ever issued, ON THE MONEY PATH, at purchase time. Partial: only discounted
-- rows can ever be the answer.
CREATE INDEX IF NOT EXISTS idx_purchase_quotes_discount
    ON purchase_quotes (wallet, issued_at DESC) WHERE discount_bps IS NOT NULL;

-- =============================================================================
-- 16b. pi_payments — WO-1318. The Pi rail's LIFECYCLE ledger.
-- -----------------------------------------------------------------------------
-- ⛔ THIS IS NOT AN ENTITLEMENT AND IT GRANTS NOTHING. It is the Pi twin of
-- google_play_purchases: a per-rail record of approve -> complete, keyed by the
-- payment id the platform owns. The GRANT still lands in purchase_entitlements,
-- with rail = 'pi', so there is exactly one grant path in this project.
--
-- WHY IT EXISTS AT ALL: Pi's onReadyForServerCompletion / onIncompletePayment
-- Found can fire more than once, in different sessions, for the same payment. A
-- replay is only recognisable as a replay if the FIRST one was written down.
-- state = 'granted' is the short-circuit that stops a second grant before Pi is
-- even called.
--
-- 'manual_review' is the row that must never be missing: it means the money moved
-- and we could NOT match it to a quote. Dropping that case is a player who paid
-- and got nothing.
-- =============================================================================
CREATE TABLE IF NOT EXISTS pi_payments (
    payment_id          TEXT PRIMARY KEY,       -- Pi's own payment identifier
    player_id           TEXT NOT NULL,          -- 'pi-<uid>' — never a bare uid
    pi_uid              TEXT NOT NULL,
    sku                 TEXT NOT NULL,
    quote_ref           TEXT NOT NULL,          -- the purchase_quotes row it is bound to
    -- The amount the SERVER quoted, in base units, copied at approve time so the
    -- ledger can be read without re-joining a quote that may later be swept.
    amount_base_units   NUMERIC(40,0) NOT NULL CHECK (amount_base_units > 0),
    decimals            SMALLINT NOT NULL CHECK (decimals >= 0 AND decimals <= 18),
    state               TEXT NOT NULL CHECK (state IN
        ('approved','completed','granted','rejected','manual_review')),
    txid                TEXT,                   -- the Pi blockchain transaction
    to_address          TEXT,                   -- payee as Pi reported it
    reject_reason       TEXT,
    approved_at         TIMESTAMPTZ,
    completed_at        TIMESTAMPTZ,
    granted_at          TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_pi_payments_player
    ON pi_payments (player_id, created_at DESC);
-- The rows a human has to look at.
CREATE INDEX IF NOT EXISTS idx_pi_payments_attention
    ON pi_payments (updated_at) WHERE state IN ('approved','completed','manual_review');

-- =============================================================================
-- 17. patronage_benefactors - WO-1073. THE BENEFACTORS OF THE REALM WALL.
-- -----------------------------------------------------------------------------
-- Owner ruling 2026-08-27, verbatim: "we add a benefactors of the Realm wall and
-- they get added to that, and every kingdom can see it. and a custom monumnet."
--
-- ONE GLOBAL ROW SET, read by every kingdom. That is why it is a table and not a
-- client cosmetic: a wall only its owner can see is not status, it is a receipt.
--
-- WHAT THIS TABLE IS NOT. It is NOT the entitlement and it is NOT the money.
-- Lifetime spend lives in purchase_entitlements and is SUMMED on read
-- (api/_lib/patronage.js); nothing is copied here, so this table can never
-- disagree with what was actually paid. A row here says only: this wallet has
-- crossed the founder threshold AND has chosen how it wishes to be named.
--
-- MEMBERSHIP IS OPT-IN BY CONSTRUCTION. A row exists only once the player sets a
-- patron name, so a founder who never chooses one is never published. That is
-- deliberate: they land on a public list as a consequence of PAYING, so the act
-- of choosing a name is the consent.
--
-- COLUMNS
--   wallet          - base58 address (PK). NEVER selected by the public read.
--   tier_id         - CHECK pins it to the single ruled tier. $50 Patron and
--                     $150 High Patron are ruled OFF the wall ("Do NOT list $50
--                     or $150"); this constraint means the database refuses them
--                     even if application code one day tried.
--   patron_name     - the PLAYER-CHOSEN public alias. Never a wallet, never an
--                     email, never a real name (api/_lib/patron-name.js gates
--                     format, length, profanity and impersonation).
--   patron_name_ci  - lower(patron_name), UNIQUE, so two founders cannot appear
--                     under the same name. A generated column, so it can never
--                     drift from patron_name.
--   name_edits_used - the bounded edit allowance (MAX_PATRON_NAME_EDITS = 3 in
--                     api/_lib/patron-name.js). Wall entry is permanent because
--                     an SPL transfer cannot reverse, so a regretted name would
--                     be permanent too with no edit path - and unlimited edits
--                     would turn an honour roll into a broadcast channel.
--   monument_asset_id
--                   - THE BESPOKE MONUMENT, PER PATRON. Owner ruling 2026-08-27,
--                     verbatim: "being it will be a custom fbx i will work with
--                     them one on to create and then add in game". The $500 rung
--                     is a COLLABORATION, not a catalog row, so this is a
--                     per-wallet asset key and NOT one shared mesh.
--                     NULL = this patron is still on the shared stand-in. That
--                     is the ONLY representation of "placeholder" -- the CHECK
--                     forbids storing the stand-in id itself, so "placeholder"
--                     can never be spelled two ways and drift.
--                     PER-PATRON, NOT A GLOBAL PHASE: Founder A can carry their
--                     real monument while Founder B is still on the stand-in.
--   monument_assigned_at
--                   - when the operator assigned it (Command Center, WO-1244).
--   monument_verified_at
--                   - WHEN THE ASSET WAS PROVEN PRESENT IN THE SHIPPED CATALOG.
--                     Section 16: bundle names are CONTENT-HASHED, so every
--                     content build invalidates every earlier proof, and a
--                     monument that was never pushed renders as NOTHING with no
--                     error on screen. This column is what lets the ship chain
--                     ask "which proofs predate the newest content build?"
--                     instead of trusting that somebody remembered.
--   granted_at      - founding order. The public ordinal is derived from this,
--                     so an early founder never loses their place.
--   name_updated_at - last rename (NULL = never renamed).
--
-- There is deliberately NO amount, NO currency and NO expiry column here: the
-- wall is cosmetic/status only (WO-1073 section 3.1) and lifetime totals only
-- ever grow (section 3.4), so there is nothing to clock and nothing to claw back.
--
-- The monument columns are STATUS too: an asset KEY, and two timestamps about
-- proof. No quantity, no currency, no expiry -- a bespoke mesh is the honour, and
-- it can never become something to spend.
-- =============================================================================
CREATE TABLE IF NOT EXISTS patronage_benefactors (
    wallet          TEXT        PRIMARY KEY,
    tier_id         TEXT        NOT NULL CHECK (tier_id IN ('founder_benefactor')),
    patron_name     TEXT        NOT NULL,
    patron_name_ci  TEXT        GENERATED ALWAYS AS (lower(patron_name)) STORED,
    name_edits_used INTEGER     NOT NULL DEFAULT 0 CHECK (name_edits_used >= 0),
    monument_asset_id    TEXT   CHECK (monument_asset_id <> 'monument_founder_standin'),
    monument_assigned_at TIMESTAMPTZ,
    monument_verified_at TIMESTAMPTZ,
    granted_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    name_updated_at TIMESTAMPTZ,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Case-insensitive uniqueness of the public name (23505 -> PATRON_NAME_TAKEN).
CREATE UNIQUE INDEX IF NOT EXISTS uq_patronage_benefactors_name_ci
    ON patronage_benefactors (patron_name_ci);

-- The wall read: tier filter + founding order, in one index.
CREATE INDEX IF NOT EXISTS idx_patronage_benefactors_wall
    ON patronage_benefactors (tier_id, granted_at ASC);


-- =============================================================================
-- 18. maintenance_toggles - WO-1243. THE OPERATOR KILL SWITCHES.
--
--   Six rows, one per area, and there is no seventh:
--     farming | raiding | arena | dungeons | store | server
--   `server` is the whole game. When it is closed EVERY area is closed, whatever
--   its own row says.
--
-- ⛔ WHAT THIS IS FOR, IN THE OWNER'S WORDS (2026-08-27):
--     "mine allows if we see someone finds a hack, we seal that area and patch"
--   It is EXPLOIT CONTAINMENT, not a maintenance-window nicety. That is why the
--   seal is enforced server-side (api/_lib/maintenance.js, called from
--   api/purchases/quote.js, api/game/save.js, api/leaderboard/submit.js) and not
--   only in the client: someone exploiting the game runs whatever client they
--   like, and a toggle only the client reads clears the area of honest players
--   while the attacker carries on.
--
-- ⛔ FAIL-OPEN, AND IT IS THE OPPOSITE OF dungeon_status ABOVE, ON PURPOSE.
--   An unreachable table, a timeout or a malformed row leaves EVERY area ON.
--   Owner-confirmed, verbatim: "correct cause i cannot help if server is
--   unreachable". There, absence must not GRANT access to content; here, absence
--   must not DENY access to the whole game. Do not unify the two.
--
-- ⚠ SO A MISSING ROW COSTS NOTHING HERE - which is the one mercy in the
--   ON CONFLICT DO NOTHING trap that shut two dungeons in production this week
--   (WO-1223). An un-back-filled database simply has nothing sealed, which is
--   the correct resting state. The rows below still SHOULD exist so the operator
--   surface (tools/command-centre.ps1 -Maintenance) can list and flip them;
--   tools/maintenance-toggle.mjs UPSERTs, so it creates a row it cannot find.
--
--   message is AUTHORED PROSE shown to every player in a rolling banner. Keep it
--   ASCII and keep it readable as maintenance from its WORDS - the owner is
--   red/green colourblind and no meaning may live in colour alone.
--
--   updated_by / updated_at are the AUDIT TRAIL: "when did we seal it, and who
--   flipped it" must be answerable after an incident. updated_by is an operator
--   label (a machine or role name), never a player identity.
--
-- Written by : tools/maintenance-toggle.mjs (DATABASE_URL) or the Neon SQL editor.
-- Read by    : api/maintenance.js (public GET) + api/_lib/maintenance.js (enforcement).
-- =============================================================================
CREATE TABLE IF NOT EXISTS maintenance_toggles (
    area_id    TEXT        PRIMARY KEY
                           CHECK (area_id IN ('farming','raiding','arena','dungeons','store','server')),
    closed     BOOLEAN     NOT NULL DEFAULT FALSE,
    message    TEXT,
    updated_by TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Every seed is OPEN (closed = FALSE) on purpose. Seeding a seal would close a
-- working area on the next provision, and a kill switch that arrives pre-pulled
-- is worse than no kill switch.
INSERT INTO maintenance_toggles (area_id, closed, message, updated_by) VALUES
    ('farming',  FALSE, NULL, 'schema-seed'),
    ('raiding',  FALSE, NULL, 'schema-seed'),
    ('arena',    FALSE, NULL, 'schema-seed'),
    ('dungeons', FALSE, NULL, 'schema-seed'),
    ('store',    FALSE, NULL, 'schema-seed'),
    ('server',   FALSE, NULL, 'schema-seed')
ON CONFLICT (area_id) DO NOTHING;

-- =============================================================================
-- 19. catalog_items / catalog_collections - WO-1272 remote collection pointers.
--
-- Neon owns metadata and ordered pointers only. Large art/model payloads stay in
-- immutable Addressables/R2 objects. Existing packaged canonical JSON remains the
-- offline fallback; these rows may add compatible content without making network
-- availability a prerequisite for the baseline game.
-- =============================================================================
CREATE TABLE IF NOT EXISTS catalog_items (
    sku                     TEXT PRIMARY KEY,
    item_kind               TEXT NOT NULL CHECK (item_kind IN
                                ('building','pack','tower','cosmetic','decoration','offer')),
    definition              JSONB NOT NULL DEFAULT '{}'::jsonb,
    version                 INTEGER NOT NULL CHECK (version > 0),
    active                  BOOLEAN NOT NULL DEFAULT FALSE,
    min_client_version      TEXT,
    packaged_fallback_key   TEXT,
    fallback_sku            TEXT REFERENCES catalog_items(sku) ON DELETE RESTRICT,
    asset_url               TEXT,
    asset_sha256            TEXT CHECK (asset_sha256 ~ '^[0-9a-f]{64}$'),
    asset_size_bytes        BIGINT CHECK (asset_size_bytes > 0),
    asset_version           INTEGER CHECK (asset_version > 0),
    expiry_behavior         TEXT NOT NULL DEFAULT 'lock'
                                CHECK (expiry_behavior IN ('hide','lock','fallback')),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (
        (asset_url IS NULL AND asset_sha256 IS NULL AND asset_size_bytes IS NULL AND asset_version IS NULL)
        OR
        (asset_url IS NOT NULL AND asset_sha256 IS NOT NULL AND asset_size_bytes IS NOT NULL AND asset_version IS NOT NULL)
    ),
    CHECK (fallback_sku IS NULL OR fallback_sku <> sku)
);

CREATE TABLE IF NOT EXISTS catalog_collections (
    collection_id           TEXT PRIMARY KEY,
    context                 TEXT NOT NULL CHECK (context IN ('build','shop','owned','showcase')),
    title                   TEXT NOT NULL,
    subtitle                TEXT,
    icon_key                TEXT,
    icon_url                TEXT,
    icon_sha256             TEXT CHECK (icon_sha256 ~ '^[0-9a-f]{64}$'),
    version                 INTEGER NOT NULL CHECK (version > 0),
    active                  BOOLEAN NOT NULL DEFAULT FALSE,
    starts_at               TIMESTAMPTZ,
    ends_at                 TIMESTAMPTZ,
    min_client_version      TEXT,
    fallback_collection_id TEXT REFERENCES catalog_collections(collection_id) ON DELETE RESTRICT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK ((icon_url IS NULL AND icon_sha256 IS NULL) OR
           (icon_url IS NOT NULL AND icon_sha256 IS NOT NULL)),
    CHECK (ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at),
    CHECK (fallback_collection_id IS NULL OR fallback_collection_id <> collection_id)
);

CREATE TABLE IF NOT EXISTS catalog_collection_items (
    collection_id  TEXT NOT NULL REFERENCES catalog_collections(collection_id) ON DELETE CASCADE,
    sku            TEXT NOT NULL REFERENCES catalog_items(sku) ON DELETE RESTRICT,
    display_order  INTEGER NOT NULL CHECK (display_order >= 0),
    badge          TEXT,
    visibility_rule JSONB NOT NULL DEFAULT '{}'::jsonb,
    PRIMARY KEY (collection_id, sku),
    UNIQUE (collection_id, display_order)
);

CREATE INDEX IF NOT EXISTS idx_catalog_collections_active
    ON catalog_collections (context, active, starts_at, ends_at);
CREATE INDEX IF NOT EXISTS idx_catalog_collection_items_order
    ON catalog_collection_items (collection_id, display_order);

-- =============================================================================
-- 20. sku_entitlements - WO-1275 non-payment reward authority.
--
-- This table is deliberately separate from purchase_entitlements. A reward has
-- no chain transaction, expected amount, recipient, or settlement state, and
-- pretending otherwise would weaken the money ledger's invariants. grant_id is
-- the idempotency key: retrying one award returns the same entitlement rather
-- than incrementing quantity or creating a duplicate.
-- =============================================================================
CREATE TABLE IF NOT EXISTS sku_entitlements (
    entitlement_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    wallet          TEXT NOT NULL,
    sku             TEXT NOT NULL REFERENCES catalog_items(sku) ON DELETE RESTRICT,
    grant_id        TEXT NOT NULL UNIQUE,
    source_kind     TEXT NOT NULL CHECK (source_kind IN
                        ('progression','tournament','promotion','community','operator','migration')),
    source_ref      TEXT,
    quantity        INTEGER NOT NULL DEFAULT 1 CHECK (quantity > 0),
    state           TEXT NOT NULL DEFAULT 'active' CHECK (state IN ('active','revoked')),
    granted_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at      TIMESTAMPTZ,
    revoked_at      TIMESTAMPTZ,
    revoke_reason   TEXT,
    metadata        JSONB NOT NULL DEFAULT '{}'::jsonb,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (expires_at IS NULL OR expires_at > granted_at),
    CHECK ((state = 'active' AND revoked_at IS NULL AND revoke_reason IS NULL) OR
           (state = 'revoked' AND revoked_at IS NOT NULL AND revoke_reason IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS idx_sku_entitlements_wallet_active
    ON sku_entitlements (wallet, state, expires_at, granted_at DESC);
CREATE INDEX IF NOT EXISTS idx_sku_entitlements_sku
    ON sku_entitlements (sku, granted_at DESC);

-- =============================================================================
-- 21. public_town_showcases - WO-1276 explicit-opt-in, privacy-safe town visits.
--
-- owner_wallet is an internal authorization/join key and is never returned by a
-- public route. Public reads use high-entropy opaque showcase_id values. Each
-- publication writes an immutable, layout-only version; unpublish flips the
-- directory bit without destroying history or exposing the private save blob.
-- =============================================================================
CREATE TABLE IF NOT EXISTS public_town_showcases (
    owner_wallet     TEXT        PRIMARY KEY,
    showcase_id      TEXT        NOT NULL UNIQUE,
    public_owner_id  TEXT        NOT NULL UNIQUE,
    current_version  BIGINT      NOT NULL DEFAULT 0 CHECK (current_version >= 0),
    published        BOOLEAN     NOT NULL DEFAULT FALSE,
    published_at     TIMESTAMPTZ,
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (showcase_id ~ '^sh_[A-Za-z0-9_-]{16,93}$'),
    CHECK (public_owner_id ~ '^po_[A-Za-z0-9_-]{16,93}$')
);

CREATE TABLE IF NOT EXISTS public_town_snapshot_versions (
    owner_wallet           TEXT        NOT NULL,
    showcase_id            TEXT        NOT NULL,
    snapshot_version       BIGINT      NOT NULL CHECK (snapshot_version >= 1),
    schema_version         INTEGER     NOT NULL CHECK (schema_version IN (1, 2)),
    catalog_version        INTEGER     NOT NULL CHECK (catalog_version >= 1),
    minimum_client_version TEXT        NOT NULL,
    structures             JSONB       NOT NULL,
    equipped_cosmetic_skus JSONB       NOT NULL DEFAULT '[]'::jsonb,
    public_hero_lineup     JSONB       NOT NULL DEFAULT '[]'::jsonb,
    public_army_lineup     JSONB       NOT NULL DEFAULT '[]'::jsonb,
    selected_echoes        JSONB       NOT NULL DEFAULT '[]'::jsonb,
    echoes_saved           INTEGER     NOT NULL DEFAULT 0,
    banner_sku             TEXT,
    title_sku              TEXT,
    town_level             INTEGER     NOT NULL DEFAULT 1,
    public_achievement_skus JSONB      NOT NULL DEFAULT '[]'::jsonb,
    leaderboard_rank       INTEGER,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (owner_wallet, snapshot_version),
    FOREIGN KEY (owner_wallet) REFERENCES public_town_showcases(owner_wallet) ON DELETE CASCADE,
    FOREIGN KEY (showcase_id) REFERENCES public_town_showcases(showcase_id) ON DELETE CASCADE,
    UNIQUE (showcase_id, snapshot_version),
    CHECK (jsonb_typeof(structures) = 'array'),
    CHECK (jsonb_array_length(structures) <= 300),
    CHECK (jsonb_typeof(equipped_cosmetic_skus) = 'array' AND jsonb_array_length(equipped_cosmetic_skus) <= 16),
    CHECK (jsonb_typeof(public_hero_lineup) = 'array' AND jsonb_array_length(public_hero_lineup) <= 4),
    CHECK (jsonb_typeof(public_army_lineup) = 'array' AND jsonb_array_length(public_army_lineup) <= 12),
    CHECK (jsonb_typeof(selected_echoes) = 'array' AND jsonb_array_length(selected_echoes) <= 4),
    CHECK (jsonb_typeof(public_achievement_skus) = 'array' AND jsonb_array_length(public_achievement_skus) <= 32),
    CHECK (echoes_saved BETWEEN 0 AND 1000000),
    CHECK (banner_sku IS NULL OR banner_sku ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CHECK (title_sku IS NULL OR title_sku ~ '^[a-z0-9][a-z0-9_-]{0,63}$'),
    CHECK (town_level BETWEEN 1 AND 1000),
    CHECK (leaderboard_rank IS NULL OR leaderboard_rank BETWEEN 1 AND 1000000)
);

CREATE INDEX IF NOT EXISTS idx_public_town_showcases_directory
    ON public_town_showcases (published, updated_at DESC)
    WHERE published = TRUE;

-- =============================================================================
-- 22. showcase contests - WO-1277 default-off community voting and cosmetics.
--
-- No contest, candidate, tier, or vote is seeded here. Runtime routes additionally
-- require COMMUNITY_SHOWCASE_VOTING_ENABLED=true. Candidates bind to one immutable
-- published snapshot version; one wallet gets one immutable vote per contest.
-- Reward tiers point only at server-authored catalog SKUs and finalization grants
-- idempotent non-payment sku_entitlements.
-- =============================================================================
CREATE TABLE IF NOT EXISTS showcase_contests (
    contest_id      TEXT PRIMARY KEY CHECK (contest_id ~ '^[a-z0-9][a-z0-9_-]{2,63}$'),
    title           TEXT NOT NULL,
    starts_at       TIMESTAMPTZ NOT NULL,
    voting_ends_at  TIMESTAMPTZ NOT NULL,
    finalized_at    TIMESTAMPTZ,
    finalized_by    TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (voting_ends_at > starts_at),
    CHECK ((finalized_at IS NULL AND finalized_by IS NULL) OR
           (finalized_at IS NOT NULL AND finalized_by IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS showcase_contest_candidates (
    contest_id      TEXT NOT NULL REFERENCES showcase_contests(contest_id) ON DELETE RESTRICT,
    showcase_id     TEXT NOT NULL REFERENCES public_town_showcases(showcase_id) ON DELETE RESTRICT,
    snapshot_version BIGINT NOT NULL CHECK (snapshot_version >= 1),
    eligible        BOOLEAN NOT NULL DEFAULT FALSE,
    entered_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (contest_id, showcase_id),
    FOREIGN KEY (showcase_id, snapshot_version)
        REFERENCES public_town_snapshot_versions(showcase_id, snapshot_version) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS showcase_contest_votes (
    contest_id      TEXT NOT NULL,
    voter_wallet    TEXT NOT NULL,
    showcase_id     TEXT NOT NULL,
    cast_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (contest_id, voter_wallet),
    FOREIGN KEY (contest_id, showcase_id)
        REFERENCES showcase_contest_candidates(contest_id, showcase_id) ON DELETE RESTRICT
);

CREATE OR REPLACE FUNCTION reject_showcase_vote_mutation() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'showcase votes are immutable';
END;
$$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS showcase_votes_immutable ON showcase_contest_votes;
CREATE TRIGGER showcase_votes_immutable
    BEFORE UPDATE OR DELETE ON showcase_contest_votes
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_vote_mutation();

CREATE TABLE IF NOT EXISTS showcase_contest_reward_tiers (
    contest_id      TEXT NOT NULL REFERENCES showcase_contests(contest_id) ON DELETE RESTRICT,
    tier_id         TEXT NOT NULL CHECK (tier_id ~ '^[a-z0-9][a-z0-9_-]{1,31}$'),
    placement_from  INTEGER NOT NULL CHECK (placement_from >= 1),
    placement_to    INTEGER NOT NULL CHECK (placement_to >= placement_from),
    cosmetic_sku    TEXT NOT NULL REFERENCES catalog_items(sku) ON DELETE RESTRICT,
    duration_days   INTEGER CHECK (duration_days IS NULL OR duration_days > 0),
    PRIMARY KEY (contest_id, tier_id),
    UNIQUE (contest_id, placement_from),
    UNIQUE (contest_id, placement_to)
);

CREATE INDEX IF NOT EXISTS idx_showcase_votes_count
    ON showcase_contest_votes (contest_id, showcase_id);

-- WO-1277 category-qualified, authored, auditable voting foundation. The v1
-- tables above remain immutable history; runtime voting uses these v2 tables.
CREATE TABLE IF NOT EXISTS showcase_contest_categories (
    contest_id TEXT NOT NULL REFERENCES showcase_contests(contest_id) ON DELETE RESTRICT,
    category_id TEXT NOT NULL CHECK (category_id ~ '^[a-z0-9][a-z0-9_-]{1,31}$'),
    label TEXT NOT NULL CHECK (char_length(label) BETWEEN 1 AND 80),
    vote_weight NUMERIC(8,4) NOT NULL DEFAULT 1 CHECK (vote_weight > 0),
    discovery_salt TEXT NOT NULL CHECK (char_length(discovery_salt) BETWEEN 16 AND 128),
    rules_version INTEGER NOT NULL DEFAULT 1 CHECK (rules_version > 0),
    active BOOLEAN NOT NULL DEFAULT FALSE,
    authored_by TEXT NOT NULL,
    authored_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (contest_id, category_id)
);
CREATE TABLE IF NOT EXISTS showcase_contest_category_candidates (
    contest_id TEXT NOT NULL,
    category_id TEXT NOT NULL,
    showcase_id TEXT NOT NULL,
    snapshot_version BIGINT NOT NULL CHECK (snapshot_version >= 1),
    eligible BOOLEAN NOT NULL DEFAULT FALSE,
    eligibility_reason TEXT,
    authored_by TEXT NOT NULL,
    entered_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (contest_id, category_id, showcase_id),
    FOREIGN KEY (contest_id, category_id) REFERENCES showcase_contest_categories(contest_id, category_id) ON DELETE RESTRICT,
    FOREIGN KEY (showcase_id, snapshot_version) REFERENCES public_town_snapshot_versions(showcase_id, snapshot_version) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS showcase_contest_category_votes (
    contest_id TEXT NOT NULL,
    category_id TEXT NOT NULL,
    voter_wallet TEXT NOT NULL,
    showcase_id TEXT NOT NULL,
    cast_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (contest_id, category_id, voter_wallet),
    FOREIGN KEY (contest_id, category_id, showcase_id)
      REFERENCES showcase_contest_category_candidates(contest_id, category_id, showcase_id) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS showcase_contest_category_reward_tiers (
    contest_id TEXT NOT NULL,
    category_id TEXT NOT NULL,
    tier_id TEXT NOT NULL CHECK (tier_id ~ '^[a-z0-9][a-z0-9_-]{1,31}$'),
    placement_from INTEGER NOT NULL CHECK (placement_from >= 1),
    placement_to INTEGER NOT NULL CHECK (placement_to >= placement_from),
    cosmetic_sku TEXT NOT NULL REFERENCES catalog_items(sku) ON DELETE RESTRICT,
    duration_days INTEGER CHECK (duration_days IS NULL OR duration_days > 0),
    authored_by TEXT NOT NULL,
    authored_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (contest_id, category_id, tier_id),
    FOREIGN KEY (contest_id, category_id) REFERENCES showcase_contest_categories(contest_id, category_id) ON DELETE RESTRICT,
    UNIQUE (contest_id, category_id, placement_from), UNIQUE (contest_id, category_id, placement_to)
);
CREATE TABLE IF NOT EXISTS showcase_contest_result_runs (
    result_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    contest_id TEXT NOT NULL,
    category_id TEXT NOT NULL,
    rules_version INTEGER NOT NULL CHECK (rules_version > 0),
    finalized_by TEXT NOT NULL,
    finalized_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (contest_id, category_id),
    FOREIGN KEY (contest_id, category_id) REFERENCES showcase_contest_categories(contest_id, category_id) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS showcase_contest_result_rows (
    result_id BIGINT NOT NULL REFERENCES showcase_contest_result_runs(result_id) ON DELETE RESTRICT,
    showcase_id TEXT NOT NULL REFERENCES public_town_showcases(showcase_id) ON DELETE RESTRICT,
    placement INTEGER NOT NULL CHECK (placement >= 1),
    vote_count BIGINT NOT NULL CHECK (vote_count >= 0),
    weighted_score NUMERIC(20,4) NOT NULL CHECK (weighted_score >= 0),
    PRIMARY KEY (result_id, showcase_id), UNIQUE (result_id, placement)
);
CREATE TABLE IF NOT EXISTS showcase_contest_result_reversals (
    result_id BIGINT PRIMARY KEY REFERENCES showcase_contest_result_runs(result_id) ON DELETE RESTRICT,
    reversed_by TEXT NOT NULL,
    reason TEXT NOT NULL CHECK (char_length(reason) BETWEEN 3 AND 500),
    reversed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE OR REPLACE FUNCTION reject_showcase_v2_audit_mutation() RETURNS trigger AS $$
BEGIN RAISE EXCEPTION 'showcase voting audit rows are immutable'; END;
$$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS showcase_category_votes_immutable ON showcase_contest_category_votes;
CREATE TRIGGER showcase_category_votes_immutable BEFORE UPDATE OR DELETE ON showcase_contest_category_votes
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_v2_audit_mutation();
DROP TRIGGER IF EXISTS showcase_category_candidates_immutable ON showcase_contest_category_candidates;
CREATE TRIGGER showcase_category_candidates_immutable BEFORE UPDATE OR DELETE ON showcase_contest_category_candidates
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_v2_audit_mutation();
DROP TRIGGER IF EXISTS showcase_categories_immutable ON showcase_contest_categories;
CREATE TRIGGER showcase_categories_immutable BEFORE UPDATE OR DELETE ON showcase_contest_categories
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_v2_audit_mutation();
DROP TRIGGER IF EXISTS showcase_category_tiers_immutable ON showcase_contest_category_reward_tiers;
CREATE TRIGGER showcase_category_tiers_immutable BEFORE UPDATE OR DELETE ON showcase_contest_category_reward_tiers
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_v2_audit_mutation();
DROP TRIGGER IF EXISTS showcase_result_runs_immutable ON showcase_contest_result_runs;
CREATE TRIGGER showcase_result_runs_immutable BEFORE UPDATE OR DELETE ON showcase_contest_result_runs
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_v2_audit_mutation();
DROP TRIGGER IF EXISTS showcase_result_rows_immutable ON showcase_contest_result_rows;
CREATE TRIGGER showcase_result_rows_immutable BEFORE UPDATE OR DELETE ON showcase_contest_result_rows
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_v2_audit_mutation();
DROP TRIGGER IF EXISTS showcase_result_reversals_immutable ON showcase_contest_result_reversals;
CREATE TRIGGER showcase_result_reversals_immutable BEFORE UPDATE OR DELETE ON showcase_contest_result_reversals
    FOR EACH ROW EXECUTE FUNCTION reject_showcase_v2_audit_mutation();
CREATE INDEX IF NOT EXISTS idx_showcase_category_votes_count
    ON showcase_contest_category_votes (contest_id, category_id, showcase_id);

-- =============================================================================
-- client_tunables - PROD-022. THE REMOTE KNOBS THE Pi CRASH LOOP IS BISECTED WITH.
--
--   Owner ruling 2026-09-02, verbatim:
--     "make the testing as robust as possible with as many solutions as
--      possible... all we really have to do is just flip a flag and possibly
--      redeploy"
--
--   A WebGL rebuild costs about thirty minutes. PROD-022 is a P0 crash loop that
--   reproduces only inside Pi Browser on the owner's iPhone. So every candidate
--   mitigation ships in ONE build behind its OWN key, all defaulting to today's
--   behaviour, and the bisect becomes flag flips against THIS table.
--
-- WHY A SEPARATE TABLE AND NOT maintenance_toggles (asked and answered, WO PROD-022):
--   maintenance_toggles is PK-CHECK-constrained to exactly six area ids, its
--   payload shape is boolean + operator prose, and its six-id domain is
--   source-linted three ways (MaintenanceArea enum / AREAS array / this CHECK) by
--   MaintenanceTogglesRegression. Putting knobs there would force that CHECK open
--   - defeating the lint that exists to keep the six honest - and would overload
--   `closed` and `message` as a value field. Different domain, different shape,
--   different failure semantics. The PATTERN is reused end to end (public
--   unauthenticated GET, short edge cache, fail-to-safe, writes only through the
--   two-key admin endpoint and the operator CLI); only the table is new.
--
-- FAIL-TO-DEFAULT, and it is neither fail-open nor fail-closed because nothing
--   here is a seal. An unreachable table, a timeout, a malformed row or a missing
--   key leaves the CLIENT at its SHIPPING DEFAULT, i.e. today's behaviour. The
--   defaults live in the build (DeNelle.Core.Ops.RemoteTunables.Registry); this
--   table only ever carries OVERRIDES. An EMPTY table is the correct resting
--   state and is exactly what ships.
--
-- ⛔ NO ROWS ARE SEEDED. Deliberate: a seeded knob is a knob already flipped, and
--   the whole invariant is that an untouched database means an untouched build.
--
--   key   - matches RemoteTunables.Registry. An unknown key is IGNORED by the
--           client (forward compatibility) and REFUSED by the operator tools.
--   value - TEXT. Bools are '0'/'1'; ints are decimal. Parsed leniently by the
--           client, and a value it cannot parse falls back to the default and
--           says so in the trace rather than poisoning the knob.
--
-- Written by : tools/client-tunables.mjs (DATABASE_URL) or api/admin/ops.js.
-- Read by    : api/client-tunables.js (public GET).
-- Owner list : docs/PROD022_TUNABLE_FLAGS.md
-- =============================================================================
CREATE TABLE IF NOT EXISTS client_tunables (
    key        TEXT        PRIMARY KEY,
    value      TEXT        NOT NULL,
    updated_by TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =============================================================================
-- skr_stake_snapshots - WO-1674 / HEART-001. THE BACKEND-AUTHORITATIVE NATIVE
-- SKR STAKE. Applied by api/migrations/20260910_0024_skr_stake_snapshots.sql,
-- whose header carries the full reasoning; this block is a DESCRIPTION.
--
--   Product rule 7 (docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:22):
--     "The Unity client must never be trusted to report the amount of SKR staked."
--
--   Until WO-1674 it was trusted: Assets/_Modules/Wallet/NativeSkrStakeQuery.cs
--   computed the amount ON THE DEVICE and StakeRewardsResolver.Query is a public
--   settable property, so a patched build could report any number. That bought
--   only extra Jeweler ATTEMPTS, which is why it was tolerable. HEART-005 makes it
--   buy resources, so it stopped being tolerable. This table is where the value
--   the server itself read off mainnet lives.
--
--   Written by : api/heartbound/status.js (the ONLY writer).
--   Read by    : api/heartbound/status.js; every later Heartbound route.
--   Verifier   : api/_lib/skr-staking.js (PDA + Anchor decode + share math).
--
-- IT IS ALSO THE CACHE. There is no KV/Redis in this project (grep for
--   @vercel/kv|upstash|edge-config|redis under api/ returns nothing), and the only
--   in-repo server-side cache is a module-scope `let jwksCache` in
--   api/_lib/google-identity.js:64-68 which does NOT survive across warm
--   instances and so cannot honestly implement a 5-minute TTL or a 60-second
--   cooldown. The row's own verified_at IS the cache clock. No dependency added.
--
-- THE TWO TIMESTAMPS ARE NOT REDUNDANT. verified_at is the last SUCCESSFUL chain
--   read and the amounts belong to it; last_attempt_at moves on every try. The
--   owner's 2026-09-10 13:10 ruling serves last-known state through an RPC outage
--   within a bounded grace window, and that window is measured from the last
--   SUCCESS - stamp one column on failures and an outage renews its own grace
--   forever.
--
-- NULL AMOUNTS MEAN UNKNOWN, NEVER ZERO. Same rule, and the same scar, as
--   player_data.schema_version (migration 0022, the WO-1457 corruption) and
--   player_data.reset_epoch (migration 0023). A DEFAULT 0 would turn "never
--   verified" into "verified to hold nothing".
--
-- NUMERIC(39,0) because shares and share_price are u128 on chain and do not fit a
--   BIGINT; exact integers, never floats, because these decide rewards.
--
-- PRIMARY KEY (player_id) is acceptance criterion 6: reconnecting the same
--   authenticated wallet UPSERTS and cannot create a second Heartbound account.
-- =============================================================================
CREATE TABLE IF NOT EXISTS skr_stake_snapshots (
    player_id            TEXT        PRIMARY KEY,   -- = the wallet (schema.sql:60)
    wallet_address       TEXT        NOT NULL,
    guardian_pool        TEXT        NOT NULL,
    user_stake_address   TEXT,
    shares_raw           NUMERIC(39,0),             -- u128, NULL = never verified
    share_price_raw      NUMERIC(39,0),             -- u128, scaled 1e9
    active_staked_raw    NUMERIC(39,0),             -- SKR base units (6 decimals)
    unstaking_raw        NUMERIC(39,0),             -- token amount, NOT shares
    unstake_timestamp    BIGINT,                    -- i64 unix, unstake INITIATED
    cooldown_seconds     BIGINT,                    -- read from StakeConfig, never hardcoded
    unstaking_ready      BOOLEAN     NOT NULL DEFAULT FALSE,
    source_slot          BIGINT,
    verification_status  TEXT        NOT NULL
        CHECK (verification_status IN (
            'VERIFIED', 'NO_STAKE', 'RPC_UNAVAILABLE', 'ACCOUNT_NOT_FOUND',
            'WALLET_NOT_LINKED', 'INVALID_RESPONSE', 'STALE')),
    error_code           TEXT,
    verified_at          TIMESTAMPTZ,               -- last SUCCESS; the grace clock
    last_attempt_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_skr_stake_snapshots_status_verified
    ON skr_stake_snapshots (verification_status, verified_at DESC);
-- 23. heartbound_state  — the backend-owned SKR Heartbound record (WO-1675, HEART-002).
-- -----------------------------------------------------------------------------
-- Endpoint : GET /api/heartbound/status              (panel lane, not yet built)
-- Written by: api/_lib/heartbound-state.js           (the ONE data-access seam)
-- Applyable copy: api/migrations/20260910_0025_heartbound_state.sql
--
-- ⛔ THIS FILE IS A DESCRIPTION AND IS NEVER APPLIED (see the note above player_data
--    at :105-107). The CREATE TABLE body below is CHARACTER-IDENTICAL to migration
--    0024's, deliberately: tools/schema-parity.mjs compares the DEPLOYED database
--    against THIS body, so the two disagreeing is the gate lying.
--
-- ⛔ WHY NOT IN THE SAVE. api/game/save.js:70-72 calls its own guards "anti-grief /
--    anti-corruption ceilings, NOT a server-authoritative economy", and :410-421
--    records that the simulated systems reach this backend "only inside the opaque
--    save blob… a RECORD, NOT A CONTROL". Heartbound decides who a Heart Pulse pays,
--    and HEART-004 requires that a pulse can never pay the same player twice — an
--    invariant a replayed client blob could rewrite. So the state is backend-owned and
--    Assets/_Modules/Core/State/SaveSchema.cs's CurrentVersion DOES NOT BUMP.
--
-- ⛔ THE KEY IS THE WALLET (schema.sql:60; api/_lib/wallet-auth.js:27-29). RULED
--    2026-09-10: ONE WALLET = ONE REALM, no re-binding, NO LINKAGE TABLE. The spec's
--    separate walletAddress field is deliberately absent (it would be a second copy of
--    the primary key) and its "Wallet Change" section is dropped, not built.
--
--   player_id                    — the wallet (= player_data.player_id). NOT FK'd,
--                                  matching achievement_grants (:965-984).
--   status                       — the six spec states, CHECK-constrained.
--   activated_at_utc             — set ONCE. The floor every pulse query filters on
--                                  ("Do NOT retroactively award historical pulses").
--   last_verified_at_utc         — last SUCCESSFUL chain read; the grace window is
--   last_actual_stake              measured from it. A FAILED verification never
--   effective_resonating_stake     touches these three (fail to last-known, never 0).
--   last_verification_*          — the failure path's own columns.
--   stale_since_utc              — when the CURRENT outage began, not the last retry.
--   *_pulse_id / last_echo_event_id — nullable TEXT: WO-1677/1678 have not designed
--                                  their id shape, and a guessed BIGINT would need an
--                                  ALTER COLUMN (memory: idempotent-ddl-hides-a-stale-table).
--   resonance_tier               — may fall.
--   highest_lifetime_tier        — monotonic; written with GREATEST() in SQL so a
--                                  caller cannot lower it even by mistake.
--   version                      — THIS ROW's generation, for optimistic concurrency.
--                                  NOT SaveSchema.CurrentVersion, which is untouched.
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

-- =============================================================================
-- END OF SCHEMA
-- =============================================================================
