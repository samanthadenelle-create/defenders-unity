// =============================================================================
// api/clan/join.js — WO-1845. POST /api/clan/join
// -----------------------------------------------------------------------------
//   POST  { playerId, code }          Headers: X-Wallet / X-Nonce / X-Signature
//                                      (or X-Session — the WO-1157 session rail)
//   200   { ok:true, clanId, name, tag, role:'member' }
//   400   CLAN_BAD_CODE  ← a code carrying an excluded character (O/0/I/1) or the
//                           wrong length. Deliberately NOT a 404: "that is not a
//                           code" and "no clan has that code" are different facts,
//                           and a player retyping a mistyped code needs the first.
//   401   any auth refusal (same shape as every other route)
//   404   CLAN_NOT_FOUND
//   409   CLAN_ALREADY_IN_CLAN
//   500   CLAN_IDENTITY_MISSING | SERVER_ERROR
//
// The code is uppercase-normalised before the lookup, so a player typing lowercase
// joins rather than being told their clan does not exist.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail } = require('../_lib/clan-http');
const { joinClan } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    let result;
    try {
        result = await joinClan(sql, wallet, body.code);
    } catch (err) {
        console.error('[clan/join] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        return clanFail(res, result.status, result.code, ref);
    }

    try {
        await logApiEvent(sql, wallet, 'clan_joined', { clanId: result.clanId, tag: result.tag });
    } catch (_) { /* telemetry never fails a completed write */ }

    return res.status(200).json({
        ok: true,
        clanId: result.clanId,
        name: result.name,
        tag: result.tag,
        role: result.role,
        joinPolicy: result.joinPolicy,
    });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
