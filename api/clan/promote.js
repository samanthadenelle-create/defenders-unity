// =============================================================================
// api/clan/promote.js — WO-1846. POST /api/clan/promote
// -----------------------------------------------------------------------------
//   POST  { playerId, wallet }        Headers: X-Wallet / X-Nonce / X-Signature
//                                      (or X-Session — the WO-1157 session rail)
//         `wallet` is the TARGET. `playerId` is the CALLER and is REQUIRED — see
//         clan-http.clanTargetWallet for why the two cannot share one field, and
//         for the `target` / `targetWallet` aliases a client should prefer.
//   200   { ok:true, wallet, role:'officer' }
//   400   CLAN_BAD_TARGET | CLAN_SELF_TARGET | BAD_PAYLOAD | PLAYER_ID_MISSING
//   401   any auth refusal (same shape as every other clan route)
//   403   CLAN_FORBIDDEN            ← the caller is not the Leader
//   404   CLAN_NOT_IN_CLAN | CLAN_TARGET_NOT_IN_CLAN
//   409   CLAN_TARGET_ROLE | CLAN_RACED
//   429   CLAN_RATE_LIMITED  (+ Retry-After and a retry_after field)
//   500   SERVER_ERROR
//
// Promotion is a GAME ROLE and nothing else — no asset, no governance right, no
// entitlement. The copy rules in the work order say so and the response says nothing
// more than the new role.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail, clanTargetWallet } = require('../_lib/clan-http');
const { promoteMember } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST', 'promote');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    let result;
    try {
        result = await promoteMember(sql, wallet, clanTargetWallet(body));
    } catch (err) {
        console.error('[clan/promote] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        return clanFail(res, result.status, result.code, ref);
    }

    try {
        await logApiEvent(sql, wallet, 'clan_promoted', { clanId: result.clanId, target: result.wallet });
    } catch (_) { /* telemetry never fails a completed write */ }

    return res.status(200).json({ ok: true, wallet: result.wallet, role: result.role });
}

module.exports = handler;
// ⛔ AFTER the assignment above, never before it: `module.exports = handler` REPLACES
// the exports object and would throw this away.
module.exports.config = { api: { bodyParser: false } };
