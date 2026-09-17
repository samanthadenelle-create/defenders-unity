// =============================================================================
// api/clan/me.js — WO-1845. GET /api/clan/me
// -----------------------------------------------------------------------------
//   GET   /api/clan/me?playerId=<wallet>
//         Headers: X-Wallet / X-Nonce / X-Signature (or X-Session)
//   200   { ok:true, clan:null }                      ← not in a clan
//   200   { ok:true, clan:{ clanId, code, name, tag, joinPolicy, createdAt,
//                           memberCount }, role, joinedAt }
//   401   any auth refusal (same shape as every other route)
//   500   SERVER_ERROR
//
// ⛔ NO CLAN IS A 200, NOT A 404. "You are in no clan" is a successful answer to the
// question; a 404 would make the client treat the ordinary state of a brand-new
// player as an error. (api/profile/get.js answers 404 for a missing profile because
// there the wallet is a PATH PARAMETER — a different question with a different
// shape of no.)
//
// AUTHENTICATED, not public: a membership read names the clan a wallet belongs to,
// and there is no reason for that to be readable by anyone who can type an address.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { beginClanRequest } = require('../_lib/clan-http');
const { readMembership } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'GET');
    if (pre.done) return;
    const { sql, wallet, ref } = pre;

    let membership;
    try {
        membership = await readMembership(sql, wallet);
    } catch (err) {
        console.error('[clan/me] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!membership) {
        return res.status(200).json({ ok: true, clan: null });
    }

    return res.status(200).json({
        ok: true,
        clan: {
            clanId: membership.clanId,
            code: membership.code,
            name: membership.name,
            tag: membership.tag,
            joinPolicy: membership.joinPolicy,
            createdAt: membership.createdAt,
            memberCount: membership.memberCount,
        },
        role: membership.role,
        joinedAt: membership.joinedAt,
    });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
