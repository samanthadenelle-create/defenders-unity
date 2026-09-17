// =============================================================================
// api/clan/demote.js — WO-1846. POST /api/clan/demote
// -----------------------------------------------------------------------------
//   POST  { playerId, wallet }        Headers: X-Wallet / X-Nonce / X-Signature
//                                      (or X-Session — the WO-1157 session rail)
//         `wallet` is the TARGET, `playerId` the CALLER. See promote.js and
//         clan-http.clanTargetWallet for the field-name note.
//   200   { ok:true, wallet, role:'member' }
//   400   CLAN_BAD_TARGET | CLAN_SELF_TARGET | BAD_PAYLOAD | PLAYER_ID_MISSING
//   401   any auth refusal (same shape as every other clan route)
//   403   CLAN_FORBIDDEN            ← the caller is not the Leader
//   404   CLAN_NOT_IN_CLAN | CLAN_TARGET_NOT_IN_CLAN
//   409   CLAN_TARGET_ROLE          ← the target is not an Officer
//   409   CLAN_RACED
//   429   CLAN_RATE_LIMITED  (+ Retry-After and a retry_after field)
//   500   SERVER_ERROR
//
// The exact mirror of promote.js, deliberately: one function per direction in
// api/_lib/clan.js and one thin route each, so neither direction can grow a rule the
// other does not have.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail, clanTargetWallet } = require('../_lib/clan-http');
const { demoteMember } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST', 'demote');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    let result;
    try {
        result = await demoteMember(sql, wallet, clanTargetWallet(body));
    } catch (err) {
        console.error('[clan/demote] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        return clanFail(res, result.status, result.code, ref);
    }

    try {
        await logApiEvent(sql, wallet, 'clan_demoted', { clanId: result.clanId, target: result.wallet });
    } catch (_) { /* telemetry never fails a completed write */ }

    return res.status(200).json({ ok: true, wallet: result.wallet, role: result.role });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
