// =============================================================================
// api/profile/usernames.js — WO-1870 (Circle screen, Lane A). GET /api/profile/usernames
// -----------------------------------------------------------------------------
//   GET   /api/profile/usernames?playerId=<caller>&playerIds=a,b,c
//         Headers: X-Wallet / X-Nonce / X-Signature (or X-Session + X-Wallet)
//   200   { ok:true, names:[ { playerId, username } ] }   ← absent ids are simply absent
//   400   PLAYER_ID_MISSING | BAD_PAYLOAD
//   401   any auth refusal
//   500   SERVER_ERROR
//
// ⛔ WHY THIS ROUTE EXISTS AT ALL, AND WHY IT IS THE ONLY NEW SERVER FILE IN THIS LANE.
// The Circle screen's Members tab shows one row per member and each row wants a NAME. The
// only per-player name this product has is player_profiles.username (WO-129) — and there was
// NO batch read of it: api/profile/get.js answers ONE wallet at a time and also joins
// leaderboard_scores for headline stats, so a ten-member roster would be ten round trips and
// thirty extra queries for two strings. api/leaderboard/get.js and api/showcase/top.js both
// already LEFT JOIN player_profiles for exactly this reason; this route is that join, asked
// directly.
//
// ⛔ LEAD RULING 2026-09-18, RECORDED WHERE IT BINDS: the Circle screen's "Remnant name" is
// the EXISTING username. No display_name column, no migration 0038, no
// POST /api/profile/display-name. The uniqueness index and the profanity gate on
// api/profile/username.js STAY — a demo does not remove a live safety gate. This file is
// therefore a READ ONLY; nothing here writes a name.
//
// ⛔ THE RESPONSE IS AN ARRAY, NOT A MAP, AND THAT IS NOT A STYLE CHOICE. The client parses
// with UnityEngine.JsonUtility (DeNelle.HUD.asmdef has no Newtonsoft and asmdefs are not
// edited for this ticket), and JsonUtility CANNOT deserialise a dictionary: a
// { "<playerId>": "<name>" } body would parse to an empty object silently, and every member
// row would fall back to a truncated address forever with no error anywhere. An array of
// {playerId, username} rows parses exactly.
//
// AUTHENTICATED, not public, to match every other clan-adjacent read (api/clan/me.js's own
// header): a roster of names is not something anyone who can type an address should be able
// to enumerate. authenticate() routes by the SHAPE of the claimed id and cannot be called
// without one, so `playerId` (the CALLER) is required alongside `playerIds` (the SUBJECTS).
//
// Status codes: 200 | 400 | 401 | 500
// =============================================================================

'use strict';

const { neon } = require('@neondatabase/serverless');
const { AuthCode, authenticate } = require('../_lib/wallet-auth');
const { applyCors, newRef, quietFail } = require('../_lib/http');

// A roster is bounded by the clan size cap; 64 is comfortably above it and keeps a hostile
// query from turning one request into an unbounded IN-list.
const MAX_IDS = 64;

async function handler(req, res) {
    if (applyCors(req, res, 'GET, OPTIONS')) return;
    const ref = newRef();

    if (req.method !== 'GET') {
        return quietFail(res, 400, AuthCode.METHOD_NOT_ALLOWED, ref);
    }

    const query = req.query || {};
    const headers = req.headers || {};
    const claimed = firstNonEmpty([query.playerId, query.wallet, headers['x-wallet']]);
    if (!claimed) {
        return quietFail(res, 400, AuthCode.PLAYER_ID_MISSING, ref);
    }

    const rawIds = query.playerIds != null ? String(query.playerIds) : '';
    const ids = [];
    for (const part of rawIds.split(',')) {
        const id = part.trim();
        if (id === '') continue;
        if (ids.indexOf(id) !== -1) continue;
        ids.push(id);
        if (ids.length >= MAX_IDS) break;
    }
    // An empty list is not an error — it is a legitimate "nobody to look up yet", and the
    // caller's own id is always at least one of them in practice.
    if (rawIds !== '' && ids.length === 0) {
        return quietFail(res, 400, AuthCode.BAD_PAYLOAD, ref);
    }

    let sql;
    try {
        sql = neon(process.env.DATABASE_URL);
    } catch (err) {
        console.error('[profile/usernames] DB init failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    let auth;
    try {
        // GET carries no body, so the signed payload is null — the same shape
        // api/clan/me.js's preamble uses for its own read.
        auth = await authenticate(sql, req, null, claimed);
    } catch (err) {
        console.error('[profile/usernames] Auth failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!auth.ok) {
        return quietFail(res, 401, auth.code || AuthCode.SERVER_ERROR, ref);
    }

    if (ids.length === 0) {
        return res.status(200).json({ ok: true, names: [] });
    }

    try {
        // player_profiles' PK column is NAMED wallet but is documented at
        // api/schema.sql:1000 as "= player_data.player_id" — the rail-agnostic id that also
        // carries play- and guest- ids. So this read is not wallet-only by construction.
        const rows = await sql`
            SELECT wallet, username
            FROM player_profiles
            WHERE wallet = ANY(${ids})
              AND username IS NOT NULL
        `;
        const names = rows.map((r) => ({ playerId: r.wallet, username: r.username }));
        return res.status(200).json({ ok: true, names });
    } catch (err) {
        console.error('[profile/usernames] DB error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
}

function firstNonEmpty(values) {
    for (const v of values) {
        if (v == null) continue;
        const s = String(v).trim();
        if (s !== '') return s;
    }
    return null;
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
