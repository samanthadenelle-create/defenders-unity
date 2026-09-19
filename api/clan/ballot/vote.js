// =============================================================================
// api/clan/ballot/vote.js — WO-1853 (clan WO-10). POST /api/clan/ballot/vote
// -----------------------------------------------------------------------------
//   POST  /api/clan/ballot/vote
//         Headers: X-Wallet / X-Nonce / X-Signature (or X-Session)
//         Body:    { playerId:<wallet>, ballotId:<uuid>, optionId:"<id>" }
//   200   the same body GET /api/clan/ballot/current answers with, with my_vote set
//   400   CLAN_BALLOT_BAD_BALLOT_ID | CLAN_BALLOT_BAD_OPTIONS | PLAYER_ID_* | METHOD_NOT_ALLOWED
//   401   any auth refusal (the same shape every other clan route answers with)
//   404   CLAN_NOT_IN_CLAN | CLAN_BALLOT_NOT_FOUND
//   409   CLAN_BALLOT_CLOSED — the ballot has closed (and this request closed it, if expired)
//   429   CLAN_RATE_* — see propose.js's note on the undeclared-action budget
//   500   SERVER_ERROR (a DATABASE failure only)
//   503   CLAN_BALLOT_WEIGHT_UNAVAILABLE — THIS member's chain read degraded
//
// ⛔ THE WEIGHT IS A SNAPSHOT, AND A RE-VOTE MOVES THE OPTION ONLY. Acceptance criterion 4.
// The INSERT ... ON CONFLICT (ballot_id, wallet) DO UPDATE names option_id and nothing else
// (api/_lib/clan-ballot.js upsertVote), so a member whose stake or tenure changed mid-ballot
// keeps the weight they had when they first voted — in BOTH directions, which is what makes
// it a snapshot rather than a cap.
//
// ⛔ A DEGRADED READ OF THIS MEMBER'S OWN POSITION IS A 503, NEVER A ZERO-WEIGHT VOTE.
// Accepting the vote at weight 0 would disenfranchise them for the whole ballot on the
// strength of somebody else's outage, and the snapshot rule above means it could never be
// corrected. A 503 says "ask again", which is true. A REAL zero — a member who has staked
// nothing — is NOT degraded (api/_lib/skr-staking.js reports that as a trustworthy zero,
// pinned by test/clan-vigil.test.js:373) and their vote is accepted at weight 0: it counts
// toward the head-count participation threshold and adds nothing to the plurality, which is
// exactly what tenure-weighted voting means.
//
// ⛔ AND ONLY THE CALLER'S OWN CLAN'S BALLOT IS VISIBLE. The ballot is read WHERE clan_id =
// <the caller's own membership> AND id = <requested>, so a ballot id belonging to another
// clan is a 404 — indistinguishable from a wrong id, which is the point (the same privacy
// rule clan.js readMemberInClan's header states).
// =============================================================================

'use strict';

const { AuthCode } = require('../../_lib/wallet-auth');
const { quietFail } = require('../../_lib/http');
const { beginClanRequest, clanFail } = require('../../_lib/clan-http');
const { readMembership, ClanCode } = require('../../_lib/clan');
const ballotLib = require('../../_lib/clan-ballot');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST', 'ballot_vote');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    let membership;
    try {
        membership = await readMembership(sql, wallet);
    } catch (err) {
        console.error('[clan/ballot/vote] membership read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!membership) return clanFail(res, 404, ClanCode.NOT_IN_CLAN, ref);
    const clanId = membership.clanId;

    // Shape first: a garbage id is a 400, not a read that finds nothing and reports 404 —
    // "that is not a ballot id" and "no such ballot in your clan" are different facts, the
    // same distinction clan.js normalizeCode draws between BAD_CODE and NOT_FOUND.
    const rawBallotId = body.ballotId !== undefined ? body.ballotId : body.ballot_id;
    if (!ballotLib.isUuid(rawBallotId)) {
        return clanFail(res, 400, ballotLib.BallotCode.BAD_BALLOT_ID, ref);
    }
    const ballotId = String(rawBallotId).trim();
    const rawOptionId = body.optionId !== undefined ? body.optionId : body.option_id;
    const optionId = rawOptionId == null ? '' : String(rawOptionId).trim();
    if (optionId === '') return clanFail(res, 400, ballotLib.BallotCode.BAD_OPTIONS, ref);

    let ballot;
    try {
        ballot = await ballotLib.readBallotInClan(sql, clanId, ballotId);
    } catch (err) {
        console.error('[clan/ballot/vote] ballot read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!ballot) return clanFail(res, 404, ballotLib.BallotCode.NOT_FOUND, ref);

    // An expired ballot is CLOSED HERE — "the next request to any ballot endpoint closes
    // it" — and then the vote is refused, because it arrived after the epoch ended.
    if (ballot.closedAt != null || ballot.expired) {
        try {
            await ballotLib.settleBallot(sql, clanId, ballot);
        } catch (err) {
            console.error('[clan/ballot/vote] settle on a closed ballot failed:', err);
        }
        return clanFail(res, 409, ballotLib.BallotCode.CLOSED, ref);
    }

    if (!ballot.options.includes(optionId)) {
        return clanFail(res, 400, ballotLib.BallotCode.BAD_OPTIONS, ref);
    }

    let vigil;
    try {
        vigil = await ballotLib.readVigilFor(sql, clanId, wallet);
    } catch (err) {
        console.error('[clan/ballot/vote] vigil read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    // No roster row for a proven member should be impossible (the roster IS clan_members),
    // but an unknowable weight must never be recorded as zero. See this file's header.
    if (!vigil.member || vigil.memberDegraded) {
        return clanFail(res, 503, ballotLib.BallotCode.WEIGHT_UNAVAILABLE, ref);
    }

    let settled;
    let perks = [];
    try {
        await ballotLib.upsertVote(sql, ballotId, wallet, optionId, vigil.myContribution);
        settled = await ballotLib.settleBallot(sql, clanId, ballot, { clanWeight: vigil.vigilWeight });
        perks = await ballotLib.readPerks(sql, clanId);
    } catch (err) {
        console.error('[clan/ballot/vote] vote write failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    return res.status(200).json(ballotLib.toWire({
        ballot: settled.ballot || ballot,
        decision: settled.decision,
        epoch: null,
        vigilWeight: vigil.vigilWeight,
        vigilDegraded: vigil.degraded,
        myVote: optionId,
        perk: settled.perk,
        perks: perks,
        circleName: membership.name,
    }));
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
