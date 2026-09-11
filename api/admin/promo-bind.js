'use strict';

// WO-1698. Same-origin write surface; both Command Center secrets are required.
// Lookup metadata is not identity proof. Only verified Google sign-in populates it.
const { neon } = require('@neondatabase/serverless');
const { keyOk, normalizePromoCode } = require('../_lib/ops');
const { readBodyExact } = require('../_lib/http');
const { deriveEmailHmac } = require('../_lib/google-identity');
const { logApiEvent } = require('../_lib/audit');

async function bindCode(sql, fingerprint, code) {
    const players = await sql`SELECT player_id FROM play_identities WHERE email_hmac = ${fingerprint} LIMIT 2`;
    if (!players.length) return { success: false, error: 'NO_MATCH' };
    // Duplicate mail claims / key rotations must never pick an arbitrary player.
    if (players.length !== 1 || !/^play-[0-9a-f]{64}$/.test(players[0].player_id)) {
        return { success: false, error: 'AMBIGUOUS_MATCH' };
    }
    const player = players[0].player_id;
    const updated = await sql`UPDATE promo_codes SET bound_wallet = ${player}
        WHERE code = ${code} AND active = TRUE
          AND (bound_wallet IS NULL OR bound_wallet = ${player})
        RETURNING code`;
    if (updated.length) return { success: true, bound: true };
    const codes = await sql`SELECT active, bound_wallet FROM promo_codes WHERE code = ${code} LIMIT 1`;
    if (!codes.length) return { success: false, error: 'CODE_NOT_FOUND' };
    if (codes[0].active !== true) return { success: false, error: 'CODE_INACTIVE' };
    if (codes[0].bound_wallet != null && codes[0].bound_wallet !== player) {
        return { success: false, error: 'ALREADY_BOUND_ELSEWHERE' };
    }
    // A concurrent operator changed the row after the conditional update refused.
    return { success: false, error: 'CODE_CHANGED_RETRY' };
}

function createHandler(deps = {}) {
    const connect = deps.connect || neon;
    const audit = deps.audit || logApiEvent;
    return async function handler(req, res) {
        res.setHeader('Cache-Control', 'no-store');
        let sql;
        const finish = async (status, result) => {
            // Stable codes only: no input, player, email, HMAC or driver error text.
            await audit(sql, null, 'admin_promo_bind', { result: result.success ? 'BOUND' : result.error });
            return res.status(status).json(result);
        };
        const refuse = (status, error) => finish(status, { success: false, error });
        const env = deps.env || process.env;
        const headers = req.headers || {};
        if (req.method !== 'POST') return refuse(400, 'METHOD_NOT_ALLOWED');
        if (!env.ADMIN_DASH_KEY) return refuse(400, 'ADMIN_NOT_CONFIGURED');
        if (!keyOk(headers['x-admin-key'], env.ADMIN_DASH_KEY)) return refuse(400, 'UNAUTHORIZED');
        if (!env.ADMIN_OPS_KEY) return refuse(400, 'OPS_WRITE_NOT_CONFIGURED');
        if (!keyOk(headers['x-admin-ops-key'], env.ADMIN_OPS_KEY)) return refuse(400, 'OPS_UNAUTHORIZED');
        if (!env.GOOGLE_IDENTITY_KEY || !env.GOOGLE_IDENTITY_KEY.trim()) return refuse(400, 'GOOGLE_IDENTITY_UNCONFIGURED');
        let body;
        try {
            const raw = await readBodyExact(req, 4096);
            if (raw.buffer.length > 4096) return refuse(400, 'BAD_BODY');
            body = JSON.parse(raw.buffer.toString('utf8'));
        } catch (_) { return refuse(400, 'BAD_BODY'); }
        if (!body || typeof body !== 'object' || Array.isArray(body)) return refuse(400, 'BAD_BODY');
        let fingerprint, code;
        try { fingerprint = deriveEmailHmac(body.email, env.GOOGLE_IDENTITY_KEY); }
        catch (_) { return refuse(400, 'EMAIL_INVALID'); }
        try {
            if (typeof body.code !== 'string') return refuse(400, 'CODE_INVALID');
            code = normalizePromoCode(body.code);
        } catch (_) { return refuse(400, 'CODE_INVALID'); }
        try {
            sql = connect(env.DATABASE_URL);
            return await finish(200, await bindCode(sql, fingerprint, code));
        } catch (_) {
            return refuse(500, 'LOOKUP_UNAVAILABLE');
        }
    };
}
module.exports = createHandler();
module.exports.config = { api: { bodyParser: false } };
module.exports._test = { bindCode, createHandler };
