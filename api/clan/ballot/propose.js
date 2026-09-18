// =============================================================================
// api/clan/ballot/propose.js — WO-1853 (clan WO-10). POST /api/clan/ballot/propose
// -----------------------------------------------------------------------------
//   POST  /api/clan/ballot/propose
//         Headers: X-Wallet / X-Nonce / X-Signature (or X-Session)
//         Body:    { playerId:<wallet>, tier:<1..5>, optionIds:[ "<id>", ... ] }
//   200   the same body GET /api/clan/ballot/current answers with, carrying the new ballot
//   400   CLAN_BALLOT_BAD_TIER | CLAN_BALLOT_BAD_OPTIONS | PLAYER_ID_* | METHOD_NOT_ALLOWED
//   401   any auth refusal (the same shape every other clan route answers with)
//   403   CLAN_BALLOT_TIER_LOCKED  — the clan's Vigil is below this tier's bar
//   404   CLAN_NOT_IN_CLAN         — the caller belongs to no clan
//   409   CLAN_BALLOT_ALREADY_OPEN — one open ballot per clan (acceptance criterion 7)
//   429   CLAN_RATE_* (see below)
//   500   SERVER_ERROR (a DATABASE failure only)
//   503   CLAN_BALLOT_WEIGHT_UNAVAILABLE — the chain read degraded BELOW the bar; unknowable
//
// ⛔ `playerId` IS PART OF THE BODY AND THE WORK ORDER'S SHAPE OMITS IT. The shared
// preamble resolves the caller's identity from body.playerId first (api/_lib/clan-http.js
// :111-115) and cannot be called without an identity at all. So the accepted body is
// `{ playerId, tier, optionIds }`; `options` is taken as an alias of `optionIds`, and
// `wallet` also works as the identity because it is third in the preamble's chain. This is
// recorded for the client lane exactly as clanTargetWallet's header recorded the same
// class of gap for /kick.
//
// ⚠ THE RATE BUDGET IS THE UNDECLARED-ACTION FALLBACK, DELIBERATELY. 'ballot_propose' is
// not a key of CLAN_RATE_LIMITS (api/_lib/wallet-auth.js:749), so touchClanRate applies the
// STRICTEST declared budget (3/hour) and logs '[wallet-auth] clan rate: undeclared action'
// — which is the signal that function's author built for exactly this (:757-761). Passing no
// action at all was the alternative and was rejected: the same comment says an unlimited
// default is "the one outcome a rate limiter must never have". Adding the two keys is a
// two-line follow-up in wallet-auth.js, a 66KB file another lane edited the same night, and
// is left to the lead rather than taken here (CLAUDE.md §9 file-disjointness).
//
// ⛔ A DEGRADED VIGIL BELOW THE BAR IS A 503, NOT A 403, and the asymmetry is the point.
// readClanVigil UNDER-STATES a clan's weight when a member's chain read fails (a degraded
// member contributes 0 — api/_lib/clan-vigil.js:127-132). So a weight that CLEARS the bar
// is trustworthy (the true weight is at least what we read) and the propose proceeds, while
// a weight BELOW the bar may be an artefact of somebody else's RPC outage — refusing that
// with 403 "your clan has not earned this tier" would state something we did not measure.
// =============================================================================

'use strict';

const { AuthCode } = require('../../_lib/wallet-auth');
const { quietFail } = require('../../_lib/http');
const { beginClanRequest, clanFail } = require('../../_lib/clan-http');
const { readMembership, ClanCode } = require('../../_lib/clan');
const ballotLib = require('../../_lib/clan-ballot');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST', 'ballot_propose');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    // The caller's clan comes from their OWN proven membership and never from the request,
    // so this route cannot open a ballot in somebody else's clan.
    let membership;
    try {
        membership = await readMembership(sql, wallet);
    } catch (err) {
        console.error('[clan/ballot/propose] membership read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!membership) return clanFail(res, 404, ClanCode.NOT_IN_CLAN, ref);

    // Shape first, and BEFORE any chain read: a malformed tier must never cost an RPC.
    const tier = ballotLib.normalizeTier(body.tier);
    if (!tier.ok) return clanFail(res, 400, tier.code, ref);
    const options = ballotLib.normalizeOptionIds(
        body.optionIds !== undefined ? body.optionIds : body.options, tier.value);
    if (!options.ok) return clanFail(res, 400, options.code, ref);

    const clanId = membership.clanId;

    try {
        // An EXPIRED open ballot is closed here, which is what makes the work order's
        // "the next request to any ballot endpoint closes it" true on this endpoint too —
        // and it is what frees the one-open-ballot slot for this propose.
        const latest = await ballotLib.readLatestBallot(sql, clanId);
        if (latest && latest.closedAt == null) {
            const settled = await ballotLib.settleBallot(sql, clanId, latest);
            if (settled.ballot && settled.ballot.closedAt == null) {
                return clanFail(res, 409, ballotLib.BallotCode.ALREADY_OPEN, ref);
            }
        }
    } catch (err) {
        console.error('[clan/ballot/propose] open-ballot check failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    // ── The tier gate ────────────────────────────────────────────────────────
    let vigil;
    try {
        vigil = await ballotLib.readVigilFor(sql, clanId, wallet);
    } catch (err) {
        // readClanVigil swallows every RPC fault by design, so reaching here means the
        // ROSTER query failed — a database problem.
        console.error('[clan/ballot/propose] vigil read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!ballotLib.meetsTier(vigil.vigilWeight, tier.value)) {
        return clanFail(res, vigil.degraded ? 503 : 403,
            vigil.degraded ? ballotLib.BallotCode.WEIGHT_UNAVAILABLE : ballotLib.BallotCode.TIER_LOCKED,
            ref);
    }

    // ── Open it ──────────────────────────────────────────────────────────────
    let epoch;
    let ballot;
    try {
        epoch = await ballotLib.readEpoch(sql);
        if (!epoch) throw new Error('epoch read returned no row');
        ballot = await ballotLib.insertBallot(
            sql, clanId, wallet, tier.value, options.value, epoch.endsAt);
    } catch (err) {
        // The partial unique index refusing a second open ballot — criterion 7's race arm.
        if (ballotLib.uniqueViolation(err)) {
            return clanFail(res, 409, ballotLib.BallotCode.ALREADY_OPEN, ref);
        }
        console.error('[clan/ballot/propose] open failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!ballot) return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);

    let perks = [];
    try {
        perks = await ballotLib.readPerks(sql, clanId);
    } catch (err) {
        // A perk read is decoration on this response; the ballot is already open.
        console.warn('[clan/ballot/propose] perk read failed (continuing):', err && err.message);
    }

    // No votes exist yet, so the tally is computed rather than read — one fewer round trip
    // than re-settling a ballot we just created.
    return res.status(200).json(ballotLib.toWire({
        ballot: ballot,
        decision: ballotLib.decideBallot([], vigil.memberCount, vigil.vigilWeight),
        epoch: epoch,
        vigilWeight: vigil.vigilWeight,
        vigilDegraded: vigil.degraded,
        myVote: null,
        perks: perks,
    }));
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
