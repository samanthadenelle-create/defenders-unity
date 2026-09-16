// =============================================================================
// api/game/load.js — Vercel Serverless Function
// -----------------------------------------------------------------------------
// Returns the stored game_state for a player. The Unity client calls this on
// scene enter and merges the server record onto the local GameState SO.
//
// WHAT CHANGED 2026-08-02:
//   • Same two-rail auth as save.js (_lib/wallet-auth.authenticate): a base58
//     wallet id still requires a signed, single-use nonce; a "guest-local-<hex>"
//     id takes the rate-limited guest rail. There was no guest path before, so
//     no guest could ever read their own row back.
//   • Structured, quiet failure codes + an audit row per refusal.
//   • THE FULL STATE IS RETURNED. The old handler hand-listed 13 keys, so even a
//     complete server record came back as a husk — no base layout, no queue, no
//     army, no hero level. The client deserialises `data` straight into
//     SaveSchema.PersistedState, so the whole stored object IS the right shape;
//     the 13 keys are still emitted explicitly for older clients.
//   • CORS + preflight (a cross-origin GET carrying X-Wallet preflights, and this
//     function never answered OPTIONS — the web build could not load at all).
//
// Status codes: 200 | 400 | 401 | 404 | 500
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const { AuthCode, authenticate } = require('../_lib/wallet-auth');
const { applyCors, newRef, quietFail } = require('../_lib/http');
const { logAuthReject } = require('../_lib/audit');

// Kept explicit so a client older than this deploy still finds every key it
// expects even if the stored row predates a field.
const LEGACY_KEYS = [
    'bestWave', 'crystals', 'food', 'coins', 'voidshards', 'stone', 'iron', 'wood',
    'towers', 'towerAbilities', 'pets', 'ownedPets', 'starterPetId',
];

/**
 * WO-1128: TIMESTAMPTZ -> unix-ms, tolerating both shapes the Neon HTTP driver
 * returns (a Date object, or an ISO string). NaN is reported as null rather than
 * as a number, so a client never mistakes a parse failure for "the epoch".
 */
function toUnixMs(ts) {
    if (ts == null) return null;
    const ms = ts instanceof Date ? ts.getTime() : Date.parse(String(ts));
    return Number.isFinite(ms) ? ms : null;
}

/**
 * player_data.schema_version -> the wire `schemaVersion`, with NULL preserved AS NULL.
 *
 * ⛔ NULL IS A MEANING, NOT A MISSING VALUE. Since migration 20260907_0022 the column
 * is nullable with NO DEFAULT, and NULL says: THIS ROW NEVER DECLARED A VERSION — the
 * server does not know what shape the blob is in, so DO NOT RUN A MIGRATION CHAIN ON
 * LOAD. It used to be `INTEGER NOT NULL DEFAULT 10`, so a brand-new player on a
 * version-less client landed at 10 (api/game/save.js's version-less branch names the
 * column nowhere, so the DEFAULT stood) and this route handed that fabricated 10 to
 * GameStateService.ApplyBackendState, which drove the whole v10→current migration
 * chain over state that had never been v10 (the WO-1457 corruption).
 *
 * The client is already built for the null: ApplyBackendState takes a `double?` and
 * only trusts it `if (serverSchemaVersion.HasValue && serverSchemaVersion.Value > 0d)`
 * — otherwise it uses SaveSchema.CurrentVersion and warns that the chain is "skipped
 * rather than guessed" (Assets/_Modules/Core/State/GameStateService.cs:2305-2317).
 *
 * Normalised rather than passed through raw so the wire value cannot become
 * `undefined` — JSON.stringify DROPS an undefined member, which turns an explicit
 * "unknown" into a silently absent field. Same decision, but only one of the two
 * spellings is visible in a capture. A non-finite or non-positive value is also
 * reported as unknown: those are the shapes that cannot name a real schema.
 */
function toSchemaVersion(raw) {
    if (raw == null) return null;
    const n = Number(raw);
    return Number.isFinite(n) && n > 0 ? n : null;
}

/**
 * player_data.reset_epoch -> the wire `resetEpoch`, NULL preserved AS NULL (WO-1598).
 *
 * The epoch is how a client can tell it is BEHIND: a device whose local epoch is lower
 * than the row's is holding a town the player has already replaced, and it must not push
 * that town back up. Returning it costs one column on a SELECT this route already runs.
 *
 * ⛔ NEVER `undefined`. JSON.stringify DROPS an undefined member, so an unknown epoch
 * would arrive as an ABSENT field — indistinguishable, on the client, from a backend too
 * old to send one. Same reasoning as toSchemaVersion above, and the same explicit null.
 *
 * ⚠ 0 IS A LEGAL EPOCH ("this player has never reset"), so the accept test is `>= 0`,
 * not the `> 0` toSchemaVersion uses. A `> 0` copied from the neighbour would report a
 * real, stored 0 as "unknown" and let a first reset be judged against nothing.
 */
function toResetEpoch(raw) {
    if (raw == null) return null;
    const n = Number(raw);
    return Number.isInteger(n) && n >= 0 ? n : null;
}

module.exports = async (req, res) => {
    if (applyCors(req, res, 'GET, OPTIONS')) return;

    const ref = newRef();

    if (req.method !== 'GET') {
        return quietFail(res, 400, AuthCode.METHOD_NOT_ALLOWED, ref);
    }

    const { playerId } = req.query || {};
    if (!playerId) {
        return quietFail(res, 400, AuthCode.PLAYER_ID_MISSING, ref);
    }

    let sql;
    try {
        sql = neon(process.env.DATABASE_URL);
    } catch (err) {
        console.error('[load] DB init error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    // ── AUTH GATE — no body, so the wallet rail signs the literal "load" tag ──
    let auth;
    try {
        auth = await authenticate(sql, req, null, playerId);
    } catch (err) {
        console.error('[load] Auth check error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!auth.ok) {
        await logAuthReject(sql, req, {
            code: auth.code, ref, identity: auth.identity, mode: auth.mode, detail: auth.detail,
        });
        const status = (auth.code === AuthCode.PLAYER_ID_BAD_SHAPE ||
                        auth.code === AuthCode.WALLET_MALFORMED) ? 400 : 401;
        return quietFail(res, status, auth.code, ref);
    }

    try {
        const rows = await sql`
            SELECT game_state, schema_version, reset_epoch, updated_at
            FROM player_data
            WHERE player_id = ${playerId}
            LIMIT 1
        `;

        if (rows.length === 0) {
            // Not an error — a first-run player simply has no row yet. Kept at 404
            // because the client already treats a non-2xx here as "keep local".
            return res.status(404).json({ ok: false, code: 'NO_SAVE', ref: ref });
        }

        const row = rows[0];
        const state = row.game_state ?? {};

        // Return the persisted client shape, excluding device-local recovery
        // intents that must never replay as another device's capture authority.
        // Then backfill the legacy keys as explicit nulls
        // so an older client never trips over a missing member.
        const data = Object.assign({}, state);
        delete data.pendingTownCapture;
        delete data.PendingTownCapture;
        for (const k of LEGACY_KEYS) {
            if (data[k] === undefined) data[k] = null;
        }

        return res.status(200).json({
            ok: true,
            success: true,
            // WO-912 s7.2: authoritative server time. The client anchors ServerClock
            // to this against a MONOTONIC timer, so the rewarded-ad window cannot be
            // reset by rolling the device clock (= fabricated ad impressions).
            // Always send it, even on an otherwise-empty response: the handshake is
            // the valuable part, not the payload.
            serverNowMs: Date.now(),
            // WO-1128: the server's own last_seen for this player, in the SAME unit as
            // serverNowMs — the anchor api/game/save.js §RECONCILE measures the client's
            // declared accrual window against. Emitted here so a capture (and any future
            // client-side "your offline haul is provisional" copy) can show BOTH numbers
            // without a second round trip. player_data.updated_at IS this table's
            // last_seen: server-stamped on every accepted save, unwritable by the client.
            serverLastSeenMs: toUnixMs(row.updated_at),
            mode: auth.mode,
            // NULL stays NULL — "never declared, do not migrate". See toSchemaVersion.
            schemaVersion: toSchemaVersion(row.schema_version),
            // WO-1598: the reset generation this row stands at. NULL stays NULL — "this
            // player has never declared a reset" — and the member is ALWAYS present.
            resetEpoch: toResetEpoch(row.reset_epoch),
            updatedAt: row.updated_at,
            data: data,
        });
    } catch (err) {
        console.error('[load] DB error:', err);
        await logAuthReject(sql, req, {
            code: AuthCode.SERVER_ERROR, ref, identity: auth.identity, mode: auth.mode,
            detail: { stage: 'select', message: String(err.message || err).slice(0, 300) },
        });
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
};
