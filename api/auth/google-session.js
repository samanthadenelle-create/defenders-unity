// =============================================================================
// api/auth/google-session.js — Vercel Serverless Function (WO-1282 PIN-1b)
// -----------------------------------------------------------------------------
// Exchanges a GOOGLE ID TOKEN for the same short-lived bearer session the wallet rail
// already uses, so a Google Play player — who has no wallet — has an identity that can
// key a save and hold an entitlement.
//
//   POST /api/auth/google-session     body: { "idToken": "<Google ID token>" }
//   200 { success:true, ok:true, playerId, token, expiresAt, ttlSeconds }
//   400 { ok:false, code:'BAD_PAYLOAD'|'METHOD_NOT_ALLOWED', ref }
//   401 { ok:false, code:'GOOGLE_TOKEN_...', ref }
//   503 { ok:false, code:'GOOGLE_IDENTITY_UNAVAILABLE', ref }   ← rail not armed
//
// ⛔ THE WALLET IS STILL THE SOLE IDENTITY ON THE SEEKER / dApp-STORE ARTIFACT (owner
//    ruling 2026-08-30). This endpoint exists for the Google Play / AAB artifact only.
//    Nothing here touches api/auth/nonce.js, api/auth/session.js, verifyWallet or
//    verifySession — the Seeker path is byte-identical to what it was, on purpose.
//
// ⛔ THE CLIENT DOES NOT CHOOSE ITS PLAYER ID; IT IS TOLD ONE. The id is
//    'play-' + HMAC-SHA256(GOOGLE_IDENTITY_KEY, sub), computed here from the VERIFIED
//    `sub` of a Google-signed token. The body carries a token and nothing else — a
//    `playerId` field in the request would be ignored, because accepting one is exactly
//    how a self-asserted identity gets handed real value.
//
// ⛔ THE RAW GOOGLE `sub` IS NEVER STORED, LOGGED OR RETURNED. That is the retired
//    Firebase-UID mistake (a provider-owned string used as a save key) and it is not
//    being repeated. Only the HMAC leaves this function.
//
// DORMANT BY DEFAULT, like the rest of the Play rail: GOOGLE_IDENTITY_ENABLED must be
// 'true' AND GOOGLE_IDENTITY_KEY / GOOGLE_IDENTITY_AUDIENCES must be set. A half-set-up
// deployment answers 503 with a stable code — it never falls through to a weaker rail.
//
// Env vars: DATABASE_URL, GOOGLE_IDENTITY_ENABLED, GOOGLE_IDENTITY_KEY,
//           GOOGLE_IDENTITY_AUDIENCES. Optional: GOOGLE_IDENTITY_KEY_PREVIOUS,
//           GOOGLE_IDENTITY_JWKS_URL.
// =============================================================================

'use strict';

const { neon } = require('@neondatabase/serverless');
const { AuthCode, issueSession, isPlayId } = require('../_lib/wallet-auth');
const { applyCors, newRef, quietFail, readBodyExact } = require('../_lib/http');
const { logAuthReject, logApiEvent } = require('../_lib/audit');
const identity = require('../_lib/google-identity');

// A Google ID token is ~1 KB. Nothing legitimate comes near this, and the cap is
// enforced DURING the stream (readBodyExact → readRawBody), so a hostile client cannot
// push an unbounded body into the function before we have decided who they are. It is
// deliberately far tighter than WALLET_MAX_BODY_BYTES: this endpoint carries a
// credential, never a save.
const ID_TOKEN_MAX_BODY_BYTES = 16 * 1024;

// Every refusal the token verifier can return is an AUTHORIZATION failure (401), except
// the two that mean "this deployment is not set up", which are 503 — the client should
// stop asking rather than retry-loop against a switch only an operator can flip.
function statusForCode(code) {
    if (code === identity.GoogleIdentityCode.DISABLED ||
        code === identity.GoogleIdentityCode.UNCONFIGURED ||
        code === identity.GoogleIdentityCode.JWKS_UNAVAILABLE) return 503;
    return 401;
}

/**
 * ⛔ THE NO-RE-KEY GUARD — the one rule in this file that protects real money.
 *
 * google-play-purchases.js:157 HMACs the playerId into Play's setObfuscatedAccountId
 * and timingSafeEqual-compares it back on every verification. There is no alias table
 * and no version field, so a player whose id CHANGES silently loses the ability to
 * verify or restore every purchase they have ever made.
 *
 * The derivation is deterministic, so the same Google account normally resolves to the
 * same id forever and no re-key is possible. There is exactly ONE way to change it:
 * rotating GOOGLE_IDENTITY_KEY. This function is what stops that rotation from
 * detonating a paying player's history — and it is enforced by a DB READ, not by a
 * convention someone has to remember.
 *
 * Behaviour when a rotation is configured (GOOGLE_IDENTITY_KEY_PREVIOUS set):
 *   • no purchases under the OLD id                → issue under the NEW id (clean move)
 *   • purchases under the OLD id, none under NEW   → PIN to the OLD id, permanently, and
 *                                                    log it loudly. The player is never
 *                                                    re-keyed; the rotation simply does
 *                                                    not apply to them.
 *   • purchases under the NEW id                   → the move already happened; use NEW.
 *   • the check itself FAILS                       → refuse (500). We do not issue an id
 *                                                    we cannot prove is safe. Fail closed.
 *
 * @returns {Promise<{ok:true, playerId:string, pinned:boolean} | {ok:false}>}
 */
async function resolveStablePlayerId(sql, subject, env) {
    const currentKey = String(env.GOOGLE_IDENTITY_KEY || '');
    const currentId = identity.derivePlayerId(subject, currentKey);

    const previousKey = String(env.GOOGLE_IDENTITY_KEY_PREVIOUS || '').trim();
    if (!previousKey || previousKey === currentKey) {
        // No rotation configured ⇒ no re-key path exists at all ⇒ nothing to guard, and
        // no DB round-trip on the hot path.
        return { ok: true, playerId: currentId, pinned: false };
    }

    const legacyId = identity.derivePlayerId(subject, previousKey);
    if (legacyId === currentId) return { ok: true, playerId: currentId, pinned: false };

    let legacyRows, currentRows;
    try {
        legacyRows = await sql`SELECT 1 FROM google_play_purchases WHERE player_id = ${legacyId} LIMIT 1`;
        currentRows = await sql`SELECT 1 FROM google_play_purchases WHERE player_id = ${currentId} LIMIT 1`;
    } catch (err) {
        // NOT a silent catch, and NOT a fall-through to "probably fine". If we cannot
        // read the purchase ledger we cannot prove this player has no history, and
        // issuing under a fresh id would be the exact failure the guard exists to stop.
        console.error('[auth/google-session] re-key guard query FAILED:',
            err && err.message ? err.message : err);
        return { ok: false };
    }

    const legacyHasPurchases = Array.isArray(legacyRows) && legacyRows.length > 0;
    const currentHasPurchases = Array.isArray(currentRows) && currentRows.length > 0;

    if (legacyHasPurchases && !currentHasPurchases) {
        return { ok: true, playerId: legacyId, pinned: true };
    }
    return { ok: true, playerId: currentId, pinned: false };
}

// WO-1698: optional admin lookup metadata must never prevent a valid sign-in.
// A missing/unverified claim clears the previous lookup, rather than retaining stale mail.
const EMAIL_LOOKUP_TIMEOUT_MS = 1000;
async function persistEmailFingerprint(sql, playerId, claims, env) {
    const controller = new AbortController();
    let deadline;
    try {
        let fingerprint = null;
        if (claims && claims.email_verified === true && typeof claims.email === 'string') {
            try { fingerprint = identity.deriveEmailHmac(claims.email, env.GOOGLE_IDENTITY_KEY); }
            catch (_) { console.warn('[auth/google-session] email lookup claim invalid'); }
        }
        // This deadline applies ONLY to optional lookup metadata, never auth or sessions.
        // The race also bounds completion if a transport fails to honor cancellation.
        await Promise.race([
            sql('INSERT INTO play_identities (player_id, email_hmac) VALUES ($1, $2) ' +
                'ON CONFLICT (player_id) DO UPDATE SET email_hmac = EXCLUDED.email_hmac',
                [playerId, fingerprint], { fetchOptions: { signal: controller.signal } }),
            new Promise((_, reject) => {
                deadline = setTimeout(() => {
                    controller.abort();
                    reject(new Error('EMAIL_LOOKUP_TIMEOUT'));
                }, EMAIL_LOOKUP_TIMEOUT_MS);
            }),
        ]);
        return true;
    } catch (_) {
        // Do not log database error text: drivers may include statement parameters.
        console.warn(controller.signal.aborted
            ? '[auth/google-session] email lookup timed out; sign-in continues'
            : '[auth/google-session] email lookup unavailable; check migration 0027');
        return false;
    } finally {
        clearTimeout(deadline);
    }
}

async function handler(req, res) {
    if (applyCors(req, res, 'POST, OPTIONS')) return;

    const ref = newRef();

    if (req.method !== 'POST') {
        return quietFail(res, 400, AuthCode.METHOD_NOT_ALLOWED, ref);
    }

    // Arm switch FIRST, before a body is read or a token is looked at: a dormant rail
    // should cost nothing and reveal nothing.
    const configured = identity.identityConfiguration(process.env);
    if (!configured.ok) {
        console.warn(`[auth/google-session] ref=${ref} refused: rail not available (${configured.code})`);
        return quietFail(res, 503, 'GOOGLE_IDENTITY_UNAVAILABLE', ref);
    }

    let rawBody, body;
    try {
        rawBody = (await readBodyExact(req, ID_TOKEN_MAX_BODY_BYTES)).buffer;
        body = JSON.parse(rawBody.toString('utf8'));
    } catch (err) {
        const tooLarge = err && err.code === 'BODY_TOO_LARGE';
        console.warn(`[auth/google-session] ref=${ref} body rejected:`, tooLarge ? 'BODY_TOO_LARGE' : 'unparseable');
        return quietFail(res, 400, tooLarge ? AuthCode.PAYLOAD_TOO_LARGE : AuthCode.BAD_PAYLOAD, ref);
    }
    if (!body || typeof body !== 'object') {
        return quietFail(res, 400, AuthCode.BAD_PAYLOAD, ref);
    }

    // The ONLY field read. A `playerId` in the body is ignored by construction — the id
    // is derived from the proven subject below, never accepted from the caller.
    const idToken = body.idToken != null ? String(body.idToken)
                  : body.id_token != null ? String(body.id_token)
                  : '';
    if (!idToken) {
        return quietFail(res, 400, AuthCode.BAD_PAYLOAD, ref);
    }

    let sql;
    try {
        sql = neon(process.env.DATABASE_URL);
    } catch (err) {
        console.error(`[auth/google-session] ref=${ref} step=db-connect FAILED:`,
            err && err.message ? err.message : err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    // ── PROOF. Signature, issuer, audience, expiry — all of it, before any claim is
    //    believed. verifyIdToken never throws for a bad token; a refusal is a value.
    let verified;
    try {
        verified = await identity.verifyIdToken(idToken, { env: process.env });
    } catch (err) {
        console.error(`[auth/google-session] ref=${ref} step=verify-id-token THREW:`,
            err && err.message ? err.message : err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!verified.ok) {
        // Loud in the db, quiet to the player. The subject is unknown/untrusted at this
        // point, so no identity is recorded — only the class of failure.
        await logAuthReject(sql, req, {
            code: verified.code, ref, mode: 'google', detail: verified.detail || {},
        });
        return quietFail(res, statusForCode(verified.code), verified.code, ref);
    }

    // ── DERIVE (server-side, keyed) + the no-re-key guard. ────────────────────
    let resolved;
    try {
        resolved = await resolveStablePlayerId(sql, verified.subject, process.env);
    } catch (err) {
        console.error(`[auth/google-session] ref=${ref} step=derive FAILED:`,
            err && err.message ? err.message : err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!resolved.ok) {
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    const playerId = resolved.playerId;

    // Belt and braces, exactly as authenticateGranting does it: the id we are about to
    // mint a session for must satisfy the SAME regex every downstream rail routes on.
    // A derivation that somehow produced another shape must never become a session.
    if (!isPlayId(playerId)) {
        console.error(`[auth/google-session] ref=${ref} derived id failed PLAY_RE — refusing to issue`);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    let session;
    try {
        session = await issueSession(sql, playerId, 'google');
    } catch (err) {
        // ⚠ PRIME SUSPECT if this is a 42P01/42703: auth_sessions is missing the
        // identity_kind column. It ships in api/schema.sql and
        // api/migrations/20260830_0013_auth_sessions_identity_kind.sql — apply the
        // migration; this is not a code fix.
        console.error(`[auth/google-session] ref=${ref} step=issue-session FAILED:`,
            err && err.code ? `[${err.code}] ` : '', err && err.message ? err.message : err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    await persistEmailFingerprint(sql, playerId, verified.claims, process.env);

    try {
        await logApiEvent(sql, playerId, 'google_session_issued', {
            ttlSeconds: session.ttlSeconds, ref, rekeyPinned: resolved.pinned === true,
        });
        if (resolved.pinned) {
            // A rotation was configured and this player was HELD BACK from it because
            // they hold purchases. Loud on purpose — an operator needs to see it.
            console.warn(`[auth/google-session] ref=${ref} NO-RE-KEY: player pinned to the previous key ` +
                'because google_play_purchases rows exist under that id.');
        }
    } catch (_) { /* audit is best-effort; never fail a good login on it */ }

    return res.status(200).json({
        success: true,
        ok: true,
        playerId: playerId,
        token: session.token,
        expiresAt: session.expiresAt,
        ttlSeconds: session.ttlSeconds,
    });
}

module.exports = handler;
// Must come AFTER the assignment above — see the note at the bottom of api/game/save.js
// for the exact bug the other ordering caused (config silently discarded, body parser
// never disabled, read hangs).
module.exports.config = { api: { bodyParser: false } };
module.exports._test = { persistEmailFingerprint, resolveStablePlayerId, statusForCode, ID_TOKEN_MAX_BODY_BYTES };
