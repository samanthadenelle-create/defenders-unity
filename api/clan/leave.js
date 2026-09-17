// =============================================================================
// api/clan/leave.js — WO-1845. POST /api/clan/leave
// -----------------------------------------------------------------------------
//   POST  { playerId }                Headers: X-Wallet / X-Nonce / X-Signature
//                                      (or X-Session — the WO-1157 session rail)
//   200   { ok:true }
//   401   any auth refusal (same shape as every other route)
//   404   CLAN_NOT_IN_CLAN
//   409   { ok:false, error:'leader_must_transfer', code:'leader_must_transfer', ref }
//   500   SERVER_ERROR
//
// ⛔ A LEADER CANNOT LEAVE, AND THAT IS THE TICKET'S EXPLICIT NON-SCOPE. WO-1846
// defines succession; until it has, a Leader walking out would leave a clan with
// members and no leader — a state no later ticket in this chain has a rule for. The
// refusal carries the work order's literal `leader_must_transfer` so the client can
// branch on the exact string it was specified with.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail } = require('../_lib/clan-http');
const { leaveClan } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST');
    if (pre.done) return;
    const { sql, wallet, ref } = pre;

    let result;
    try {
        result = await leaveClan(sql, wallet);
    } catch (err) {
        console.error('[clan/leave] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        return clanFail(res, result.status, result.code, ref);
    }

    try {
        await logApiEvent(sql, wallet, 'clan_left', { clanId: result.clanId, clanRemoved: result.clanRemoved });
    } catch (_) { /* telemetry never fails a completed write */ }

    return res.status(200).json({ ok: true });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
