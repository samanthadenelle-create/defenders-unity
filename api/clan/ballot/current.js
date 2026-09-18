// =============================================================================
// api/clan/ballot/current.js — WO-1853 (clan WO-10). GET /api/clan/ballot/current
// -----------------------------------------------------------------------------
//   GET   /api/clan/ballot/current?playerId=<wallet>
//         Headers: X-Wallet / X-Nonce / X-Signature (or X-Session)
//   200   { ok:true, headline, epoch:{...}, tiers:[...], vigil_weight, vigil_degraded,
//           ballot: {...}|null, result: {closed,passed,reason,winning_option,perk_id}|null,
//           perks:[ { tier, perk_id, activated_at, expires_at } ] }
//   400   PLAYER_ID_* | METHOD_NOT_ALLOWED
//   401   any auth refusal (the same shape every other clan route answers with)
//   404   CLAN_NOT_IN_CLAN — the caller belongs to no clan
//   500   SERVER_ERROR (a DATABASE failure only — never an RPC one)
//
// ⛔ THIS IS THE ENDPOINT THAT CLOSES AN EXPIRED BALLOT — acceptance criterion 8, "an
// expired ballot closes on next read and returns the result (pass or fail)". Closing spends
// NO chain read: every weight was snapshotted when the vote was cast, so an SKR outage can
// never leave a clan's ballot stuck open (api/_lib/clan-ballot.js settleBallot).
//
// ⛔ AN RPC FAILURE IS ALWAYS A 200 ON A READ, exactly as GET /api/clan/vigil answers. The
// Vigil is read here for two reasons only — `tiers[].unlocked`, so the client knows what it
// may propose, and the advisory `weighted_turnout` denominator — and neither is worth
// failing a read for. A degraded read reports `vigil_degraded: true` and a weight that is
// UNDER-stated, which is stated rather than hidden. The ONLY 500 here is a database failure.
//
// ⛔ AND IT SPENDS NO CLAN RATE BUDGET. beginClanRequest is called with three arguments, not
// four — a pure read, like /me, /leaderboard and /vigil. The reasoning is written out at
// api/clan/vigil.js:28-33: charging a read against an action budget would let a player lose
// the ability to leave their clan by looking at it.
//
// ⛔ NO WALLET-TO-OPTION MAP IS RENDERED. The response carries per-option tallies and the
// CALLER'S OWN vote, never who voted for what — see api/_lib/clan-ballot.js toWire's header
// for the full reasoning and the flag raised for the owner.
// =============================================================================

'use strict';

const { AuthCode } = require('../../_lib/wallet-auth');
const { quietFail } = require('../../_lib/http');
const { beginClanRequest, clanFail } = require('../../_lib/clan-http');
const { readMembership, ClanCode } = require('../../_lib/clan');
const ballotLib = require('../../_lib/clan-ballot');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'GET');
    if (pre.done) return;
    const { sql, wallet, ref } = pre;

    let membership;
    try {
        membership = await readMembership(sql, wallet);
    } catch (err) {
        console.error('[clan/ballot/current] membership read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!membership) return clanFail(res, 404, ClanCode.NOT_IN_CLAN, ref);
    const clanId = membership.clanId;

    let epoch;
    let vigil;
    let settled;
    let perks = [];
    try {
        epoch = await ballotLib.readEpoch(sql);
        vigil = await ballotLib.readVigilFor(sql, clanId, wallet);
        const latest = await ballotLib.readLatestBallot(sql, clanId);
        settled = await ballotLib.settleBallot(sql, clanId, latest, { clanWeight: vigil.vigilWeight });
        // AFTER the settle, so a perk this very request activated is in the answer.
        perks = await ballotLib.readPerks(sql, clanId);
    } catch (err) {
        console.error('[clan/ballot/current] read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    const myVoteRow = (settled.votes || []).find((v) => v.wallet === String(wallet)) || null;

    return res.status(200).json(ballotLib.toWire({
        ballot: settled.ballot,
        decision: settled.decision,
        epoch: epoch,
        vigilWeight: vigil.vigilWeight,
        vigilDegraded: vigil.degraded,
        myVote: myVoteRow ? myVoteRow.optionId : null,
        perk: settled.perk,
        perks: perks,
    }));
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
