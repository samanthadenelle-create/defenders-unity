// =============================================================================
// api/clan/vigil.js — WO-1852 (clan WO-9). GET /api/clan/vigil
// -----------------------------------------------------------------------------
//   GET   /api/clan/vigil?playerId=<wallet>
//         Headers: X-Wallet / X-Nonce / X-Signature (or X-Session)
//   200   { ok:true, vigil_weight:<number>, member_count:<int>, degraded:<bool>,
//           members:[ { wallet, role, percent_staked, tenure_seconds,
//                       vigil_contribution, degraded } ] }
//   400   PLAYER_ID_MISSING | PLAYER_ID_BAD_SHAPE | METHOD_NOT_ALLOWED
//   401   any auth refusal (the same shape every other clan route answers with)
//   404   CLAN_NOT_IN_CLAN — the caller belongs to no clan
//   500   SERVER_ERROR (a DATABASE failure only — never an RPC one)
//
// ⛔ NOT IN A CLAN IS A 404 HERE, UNLIKE /api/clan/me's 200 + `clan:null`, and the
// difference is not an inconsistency. /me answers "where do I stand", for which "no
// clan" is a successful answer and the ordinary state of a new player. This route
// answers "what is MY CLAN'S Vigil" — a question with no subject when there is no
// clan. `CLAN_NOT_IN_CLAN` is the existing code for exactly that (api/_lib/clan.js
// :138, already answered as 404 by /leave, /promote, /demote and /kick); it is reused,
// not reinvented.
//
// ⛔ AN RPC FAILURE IS ALWAYS A 200. Acceptance criterion 4. The chain is a
// dependency this endpoint reports ON, never one it fails WITH: an unreadable member
// reads `percent_staked: 0` with `degraded: true`, and the response still carries the
// roster, the tenures and a weight. The ONLY 500 here is a database failure, because
// without the roster there is no answer to shape at all.
//
// ⛔ AND IT SPENDS NO CLAN RATE BUDGET. beginClanRequest is called with three
// arguments, not four — a pure read, like /me and /leaderboard. The six budgets in
// CLAN_RATE_LIMITS are for actions that CHANGE the roster; charging a read against
// them would let a player lose the ability to leave their clan by looking at it.
// (The RPC cost this read can cause is bounded elsewhere: skr-staking's 60-second
// per-wallet cache and clan-vigil's VIGIL_READ_CONCURRENCY.)
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { beginClanRequest, clanFail } = require('../_lib/clan-http');
const { readMembership, ClanCode } = require('../_lib/clan');
const { readClanVigil, toWire } = require('../_lib/clan-vigil');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'GET');
    if (pre.done) return;
    const { sql, wallet, ref } = pre;

    // The caller's clan comes from their OWN proven membership and never from the
    // request, so this route cannot be pointed at another clan's roster.
    let membership;
    try {
        membership = await readMembership(sql, wallet);
    } catch (err) {
        console.error('[clan/vigil] membership read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!membership) {
        return clanFail(res, 404, ClanCode.NOT_IN_CLAN, ref);
    }

    let vigil;
    try {
        vigil = await readClanVigil(sql, membership.clanId);
    } catch (err) {
        // readClanVigil swallows every RPC fault by design, so reaching here means the
        // ROSTER query itself failed — a database problem, and the one thing that
        // genuinely leaves nothing to answer with.
        console.error('[clan/vigil] vigil read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    return res.status(200).json(toWire(vigil));
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
