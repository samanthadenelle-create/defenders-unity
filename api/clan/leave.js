// =============================================================================
// api/clan/leave.js — WO-1845. POST /api/clan/leave
// -----------------------------------------------------------------------------
//   POST  { playerId }                Headers: X-Wallet / X-Nonce / X-Signature
//                                      (or X-Session — the WO-1157 session rail)
//   200   { ok:true }                                   ← a Member or Officer left
//   200   { ok:true, newLeader:<wallet> }               ← a Leader left, succession ran
//   200   { ok:true, clanDeleted:true }                 ← the Leader was the sole member
//   401   any auth refusal (same shape as every other route)
//   404   CLAN_NOT_IN_CLAN
//   409   { ok:false, error:'leader_must_transfer', ... } ← now the RACE label only
//   429   CLAN_RATE_LIMITED  (+ Retry-After and a retry_after field)
//   500   SERVER_ERROR
//
// ⚠ WO-1846 CHANGED THIS ROUTE'S RULE, AND THE OLD ONE IS RETIRED. WO-1845 shipped a
// flat 409 `leader_must_transfer` for a Leader, because succession had no rule yet. It
// has one now (api/_lib/clan.leaveAsLeader: oldest Officer, else oldest Member, else
// the clan is deleted), so a Leader leaving SUCCEEDS. The literal string survives as
// the RACE label — the read said leader and the write moved nothing — because the
// client already branches on it and it still means "your leave did not happen".
//
// `{ ok: true }` stays byte-identical for the ordinary member case: the succession
// fields are added only when they are true, so nothing the client already reads moves.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail } = require('../_lib/clan-http');
const { leaveClan } = require('../_lib/clan');

async function handler(req, res) {
    // WO-1846: 5 leaves per wallet per hour (clan_rate_limit action 'leave').
    const pre = await beginClanRequest(req, res, 'POST', 'leave');
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
        await logApiEvent(sql, wallet, 'clan_left', {
            clanId: result.clanId,
            clanRemoved: result.clanRemoved,
            newLeader: result.newLeader || null,
            successionFrom: result.successionFrom || null,
        });
    } catch (_) { /* telemetry never fails a completed write */ }

    // ADDITIVE ONLY. The member case answers exactly { ok: true }, as WO-1845 specified
    // and as its test asserts with deepEqual; the succession fields appear only on the
    // paths that actually have one, which is what makes this change invisible to a
    // client that has not been taught about them yet.
    const payload = { ok: true };
    if (result.clanDeleted) payload.clanDeleted = true;
    if (result.newLeader) payload.newLeader = result.newLeader;
    return res.status(200).json(payload);
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
