// =============================================================================
// api/clan/kick.js — WO-1846. POST /api/clan/kick
// -----------------------------------------------------------------------------
//   POST  { playerId, wallet }        Headers: X-Wallet / X-Nonce / X-Signature
//                                      (or X-Session — the WO-1157 session rail)
//         `wallet` is the TARGET, `playerId` the CALLER. See promote.js and
//         clan-http.clanTargetWallet for the field-name note.
//   200   { ok:true }                 ← the work order's literal body
//   400   CLAN_BAD_TARGET | CLAN_SELF_TARGET | BAD_PAYLOAD | PLAYER_ID_MISSING
//   401   any auth refusal (same shape as every other clan route)
//   403   CLAN_FORBIDDEN            ← a Member kicking anyone, or an Officer aiming
//                                     at another Officer or at the Leader
//   404   CLAN_NOT_IN_CLAN | CLAN_TARGET_NOT_IN_CLAN
//   409   CLAN_RACED
//   429   CLAN_RATE_LIMITED  (+ Retry-After and a retry_after field)
//   500   SERVER_ERROR
//
// ⛔ A SELF-KICK IS A 400, NOT A LEAVE. /leave has succession attached to it; routing
// a Leader's own exit through here would remove them with no successor chosen, which
// is the exact leaderless state WO-1846 exists to close.
//
// Copy: "Removed from the clan". Plain and non-punitive by the work order's rule —
// there is no appeal or review process yet, so the wording must not imply one.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail, clanTargetWallet } = require('../_lib/clan-http');
const { kickMember } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST', 'kick');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    let result;
    try {
        result = await kickMember(sql, wallet, clanTargetWallet(body));
    } catch (err) {
        console.error('[clan/kick] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        return clanFail(res, result.status, result.code, ref);
    }

    try {
        await logApiEvent(sql, wallet, 'clan_kicked', {
            clanId: result.clanId, target: result.wallet, removedRole: result.removedRole, by: result.by,
        });
    } catch (_) { /* telemetry never fails a completed write */ }

    return res.status(200).json({ ok: true });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
