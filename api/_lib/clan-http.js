// =============================================================================
// api/_lib/clan-http.js — WO-1845. The auth/CORS/body preamble the four clan
// routes share, written ONCE.
// -----------------------------------------------------------------------------
// ⛔ WHY THIS IS A FILE AND NOT FOUR COPIES. The preamble is ~40 lines of CORS,
// raw-body read, neon init, authenticate(), logAuthReject and quietFail. Copied
// into create / join / leave / me it is four copies of one rule, and this repo has
// paid for that shape more than once (the push+verify pair inlined into two ship
// chains had ALREADY DRIFTED when it was found — CLAUDE.md §16; the nine bespoke
// migration runners each hardcoded their own file list — WO-1505). Acceptance
// criterion 8 of the work order is literally "all endpoints reject unauthenticated
// requests with the SAME error shape", which is a property a copy cannot hold.
//
// ⛔ AND THE RULE THIS PREAMBLE ADDS, WHICH IS NOT SCOPE CREEP: the clan tables'
// foreign keys point at wallet_identity(wallet), and ONLY the wallet rail ever
// writes that table (api/_lib/wallet-auth.js touchWalletIdentity, called from
// authenticate() on the wallet rail alone). So a guest- or play-shaped identity
// reaching any clan INSERT is a 23503 surfacing as a 500 — never a membership. The
// preamble therefore requires `auth.mode === 'wallet'` and refuses anything else
// with AUTH_WALLET_REQUIRED, the same code authenticateGranting() uses.
//
// ⚠ IT IS NOT authenticateGranting(). That function's allowlist admits `google`
//   (a play- id), which is exactly the shape that cannot satisfy these foreign keys.
//   The work order specifies authenticate(); this is authenticate() plus the one
//   narrower check the schema forces, and both facts are on the record in the
//   hand-back.
//
// Files under api/_lib/ are NOT routed by Vercel (leading underscore). CommonJS.
// =============================================================================

'use strict';

const { neon } = require('@neondatabase/serverless');
const { AuthCode, authenticate, touchClanRate } = require('./wallet-auth');
const { applyCors, newRef, quietFail, readBodyExact, bodyBytesDetail } = require('./http');
const { logAuthReject } = require('./audit');

// A clan request carries a name, a tag or a code. Nothing here is ever large, and a
// ceiling well under the wallet rail's keeps a hostile body out of memory before we
// know who is calling.
const MAX_BODY_BYTES = 8 * 1024;

/**
 * Run the shared preamble.
 *
 * On any refusal this function has ALREADY written the response; the caller must
 * return immediately on `{ done: true }` and touch neither res nor sql.
 *
 * @param {object} req
 * @param {object} res
 * @param {'GET'|'POST'} method  the ONE method this route accepts
 * @param {string} [action]     WO-1846: the clan_rate_limit action this route spends
 *                              budget on ('create' | 'join' | 'leave' | 'promote' |
 *                              'demote' | 'kick'). OMITTED = no budget, which is what
 *                              keeps the 3-argument callers (and /me, a pure read)
 *                              byte-identical in behaviour.
 * @returns {Promise<{done:true} | {done:false, sql:Function, wallet:string, body:object, ref:string}>}
 */
async function beginClanRequest(req, res, method, action) {
    if (applyCors(req, res, method + ', OPTIONS')) return { done: true };
    const ref = newRef();

    if (req.method !== method) {
        quietFail(res, 400, AuthCode.METHOD_NOT_ALLOWED, ref);
        return { done: true };
    }

    // ── The claimed identity ─────────────────────────────────────────────────
    // ⚠ A SPEC GAP, FILLED THE WAY THE REST OF THE API ALREADY DOES IT. The work
    // order's bodies are `{ name, tag }` and `{ code }` and name no identity field,
    // but authenticate() routes by the SHAPE of the id being acted on and cannot be
    // called without one. The convention is settled elsewhere in this API and is
    // followed rather than invented: POST reads body.playerId (api/referral/claim.js
    // :101, api/game/save.js:317) and GET reads req.query.playerId (api/game/load.js
    // :106). X-Wallet is accepted as a last resort so a client that only sets the
    // signature headers is not locked out — and it is NOT a weaker path: whatever id
    // arrives here still has to survive authenticate(), which for a wallet-shaped id
    // means an ed25519 signature or a session bound to that same wallet.
    let body = {};
    let rawBody = null;
    let exactBytes = true;

    if (method === 'POST') {
        try {
            const read = await readBodyExact(req, MAX_BODY_BYTES);
            rawBody = read.buffer;
            exactBytes = read.exact;
        } catch (err) {
            const code = err && err.code === 'BODY_TOO_LARGE' ? AuthCode.PAYLOAD_TOO_LARGE : AuthCode.BAD_PAYLOAD;
            quietFail(res, 400, code, ref);
            return { done: true };
        }
        // An empty body is legitimate for /leave, which needs nothing but an identity.
        const text = rawBody && rawBody.length > 0 ? rawBody.toString('utf8') : '';
        if (text.trim() !== '') {
            try {
                body = JSON.parse(text);
            } catch (_) {
                quietFail(res, 400, AuthCode.BAD_PAYLOAD, ref);
                return { done: true };
            }
            if (!body || typeof body !== 'object' || Array.isArray(body)) {
                quietFail(res, 400, AuthCode.BAD_PAYLOAD, ref);
                return { done: true };
            }
        }
    }

    const query = req.query || {};
    const headers = req.headers || {};
    const claimed = firstNonEmpty([
        body.playerId, body.PlayerId, body.wallet,
        query.playerId, query.wallet,
        headers['x-wallet'],
    ]);
    if (!claimed) {
        quietFail(res, 400, AuthCode.PLAYER_ID_MISSING, ref);
        return { done: true };
    }

    let sql;
    try {
        sql = neon(process.env.DATABASE_URL);
    } catch (err) {
        console.error('[clan] DB init failed:', err);
        quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
        return { done: true };
    }

    // ⛔ PROCEED-AND-TAG on a reconstructed body, never refuse it. Vercel's Node runtime
    // parses req.body regardless of config.api.bodyParser, so `exact` is effectively
    // always false in production — three endpoints 500'd every fresh-device signature
    // for that reason (WO-1453; see _lib/http.bodyBytesDetail for why proceeding cannot
    // create a false accept).
    let auth;
    try {
        auth = await authenticate(sql, req, rawBody, claimed);
    } catch (err) {
        console.error('[clan] Auth failed:', err);
        quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
        return { done: true };
    }

    if (!auth.ok) {
        await logAuthReject(sql, req, {
            code: auth.code,
            ref: ref,
            identity: auth.identity,
            mode: auth.mode,
            detail: Object.assign({}, auth.detail, bodyBytesDetail(exactBytes)),
        });
        const badArgument = auth.code === AuthCode.PLAYER_ID_MISSING || auth.code === AuthCode.PLAYER_ID_BAD_SHAPE;
        quietFail(res, badArgument ? 400 : 401, auth.code, ref);
        return { done: true };
    }

    // The schema-forced narrowing. See this file's header.
    if (auth.mode !== 'wallet') {
        await logAuthReject(sql, req, {
            code: AuthCode.WALLET_REQUIRED,
            ref: ref,
            identity: auth.identity,
            mode: auth.mode,
            detail: { clanRoute: true, provenMode: auth.mode, reason: 'clan_fk_requires_wallet_identity' },
        });
        quietFail(res, 401, AuthCode.WALLET_REQUIRED, ref);
        return { done: true };
    }

    // ── WO-1846: the clan action budget ──────────────────────────────────────
    // ⛔ AFTER AUTH AND BEFORE THE ACTION. After auth because the budget is keyed to a
    // PROVEN wallet — keyed to a claimed one it would be a way to burn someone else's
    // budget by naming them. Before the action because a refused attempt must still
    // spend (see touchClanRate's note): otherwise the cheapest request in the system is
    // the one an attacker repeats.
    if (action) {
        const rate = await touchClanRate(sql, auth.identity, action);
        if (!rate.ok) {
            const retryAfter = rate.detail && rate.detail.retryAfterSeconds
                ? Number(rate.detail.retryAfterSeconds) : 60;
            await logAuthReject(sql, req, {
                code: rate.code,
                ref: ref,
                identity: auth.identity,
                mode: auth.mode,
                detail: Object.assign({ clanRoute: true }, rate.detail),
            });
            // The HTTP header AND the body field. The header is what a well-behaved
            // client/proxy already honours; the body field is what the Unity client can
            // read without touching response headers, and the work order asks for a
            // `retry_after` hint by name.
            res.setHeader('Retry-After', String(retryAfter));
            res.status(429).json({
                ok: false, error: rate.code, code: rate.code, ref: ref, retry_after: retryAfter,
            });
            return { done: true };
        }
    }

    return { done: false, sql: sql, wallet: auth.identity, body: body, ref: ref };
}

function firstNonEmpty(candidates) {
    for (const c of candidates) {
        if (c == null) continue;
        const s = String(c).trim();
        if (s !== '') return s;
    }
    return '';
}

/**
 * Answer a clan business failure. `error` carries the machine code so the work
 * order's literal `{ error: 'leader_must_transfer' }` body holds exactly, and `code`
 * + `ref` keep the quietFail shape every other route in this API answers with.
 */
function clanFail(res, status, code, ref) {
    return res.status(status).json({ ok: false, error: code, code: code, ref: ref });
}

/**
 * WO-1846. The TARGET of a role operation, read from the body.
 *
 * ⛔ READ THIS BEFORE CHANGING THE FIELD NAME — `wallet` IS ALREADY OVERLOADED. The
 * work order specifies the kick body as `{ wallet }`, but `body.wallet` is the THIRD
 * candidate in this file's claimed-identity chain (see firstNonEmpty above), where it
 * means "who is calling". So a body carrying ONLY `{ wallet: B }` makes B the claimed
 * caller, and the request fails auth closed (the session/signature belongs to A) — a
 * confusing 401 rather than a kick, but never a hole: nobody can act as someone else by
 * naming them, because `playerId` is resolved first and the proof still has to match.
 *
 * The supported bodies are therefore `{ playerId: <caller>, wallet: <target> }` (the
 * work order's shape, which works because playerId wins the identity chain) and the two
 * unambiguous aliases `target` / `targetWallet`, which is what a client should prefer.
 * This is recorded in the hand-back for the client lane.
 */
function clanTargetWallet(body) {
    const b = body || {};
    return firstNonEmpty([b.target, b.targetWallet, b.wallet]);
}

module.exports = {
    MAX_BODY_BYTES,
    beginClanRequest,
    clanFail,
    clanTargetWallet,
    firstNonEmpty,
};
