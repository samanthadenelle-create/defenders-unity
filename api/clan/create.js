// =============================================================================
// api/clan/create.js — WO-1845, join-policy field added by WO-1851 (clan WO-8).
// POST /api/clan/create
// -----------------------------------------------------------------------------
//   POST  { playerId, name, tag, joinPolicy? }   Headers: X-Wallet / X-Nonce / X-Signature
//                                      (or X-Session — the WO-1157 session rail)
//         joinPolicy is OPTIONAL: 'invite' (default, unchanged) or 'open'. ⚠ 'open' is
//         stored but NOT yet honored differently by /api/clan/join — see api/_lib/clan.js
//         createClan's doc comment. Storing it correctly now avoids a second migration
//         once join-without-code behavior is built.
//   200   { ok:true, clanId, code, name, tag, role:'leader' }
//   400   CLAN_BAD_NAME | CLAN_BAD_TAG | CLAN_BAD_JOIN_POLICY | BAD_PAYLOAD | PLAYER_ID_MISSING
//   401   any auth refusal (same shape as every other route: {ok,code,ref})
//   409   CLAN_ALREADY_IN_CLAN
//   500   CLAN_CODE_UNAVAILABLE | CLAN_IDENTITY_MISSING | SERVER_ERROR
//
// THE INVITE CODE IS MINTED HERE, NOT ON THE DEVICE. That is the point of the
// ticket: the client's ClanService.GenerateClanCode() validated nothing and could
// not, because no server held the other codes. The draw, the collision retry and
// the atomic clan+leader write all live in api/_lib/clan.js — this file is CORS,
// auth and one call.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail } = require('../_lib/clan-http');
const { createClan } = require('../_lib/clan');

async function handler(req, res) {
    // The 4th argument is the WO-1846 clan_rate_limit action: 3 clan creations per
    // wallet per hour, spent BEFORE the work so a refused attempt still costs budget.
    const pre = await beginClanRequest(req, res, 'POST', 'create');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    let result;
    try {
        result = await createClan(sql, wallet, body.name, body.tag, body.joinPolicy);
    } catch (err) {
        console.error('[clan/create] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        return clanFail(res, result.status, result.code, ref);
    }

    try {
        await logApiEvent(sql, wallet, 'clan_created', { clanId: result.clanId, tag: result.tag });
    } catch (_) { /* telemetry never fails a completed write */ }

    return res.status(200).json({
        ok: true,
        clanId: result.clanId,
        code: result.code,
        name: result.name,
        tag: result.tag,
        role: result.role,
        joinPolicy: result.joinPolicy,
        createdAt: result.createdAt,
    });
}

module.exports = handler;
// ⛔ AFTER the assignment above, never before it: `module.exports = handler` REPLACES
// the exports object and would throw this away, which is precisely how save.js ran
// with its body parser still active (see _lib/http.readBodyExact's note).
module.exports.config = { api: { bodyParser: false } };
