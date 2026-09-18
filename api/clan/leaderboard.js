// =============================================================================
// api/clan/leaderboard.js — WO-1850 (clan WO-7). GET /api/clan/leaderboard
// -----------------------------------------------------------------------------
//   GET   /api/clan/leaderboard?limit=50    Headers: X-Wallet / X-Nonce /
//                                            X-Signature (or X-Session)
//   200   { ok:true, clans:[ { rank, clanId, name, tag, metric, memberCount }, ... ] }
//   401   any auth refusal (same shape as every other clan route)
//   500   SERVER_ERROR
//
// ── AUTH: authenticate() (read-only, no value granted), per docs/SKR
//    Integtration.md:531's own draft — NOT a public route. This ticket routes
//    through the SAME beginClanRequest() preamble every other clan endpoint uses
//    (§WO's acceptance criterion: "the same error shape as other authenticated
//    reads"), which is a property only the shared preamble can hold rather than a
//    second hand-rolled auth check drifting from the first. That preamble also
//    forces `auth.mode === 'wallet'` — narrower than the draft strictly asked for,
//    but this route reads clans/clan_members, which are the same wallet-gated
//    tables every other clan route touches, so the narrowing is consistent rather
//    than an invented exception. Flagged here rather than picked silently, per the
//    WO's own instruction to flag genuine ambiguity.
//
// ── THE METRIC IS A NAMED PLACEHOLDER — see api/_lib/clan.js:getLeaderboard for
//    the full account. In one line: `clan_vigil_weight` (the real ranking basis
//    named in docs/SKR Integtration.md:525-526, WO-9) does not exist anywhere in
//    this schema yet (grepped at source, 2026-09-17), so this ships the documented
//    FALLBACK — `member_count × days_since_created` — and names Vigil weight as
//    WO-1852+ scope, never this ticket's.
//
// ── DOES NOT TOUCH THE EXISTING (UNRELATED) LEADERBOARD. api/leaderboard/get.js
//    and the table it reads are untouched by this file — this is a SEPARATE
//    endpoint over the clan tables, exactly as WO-1265's own acceptance criteria
//    and this ticket's non-scope require.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { beginClanRequest } = require('../_lib/clan-http');
const { clampLeaderboardLimit, getLeaderboard } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'GET');
    if (pre.done) return;
    const { sql, ref } = pre;

    const limit = clampLeaderboardLimit((req.query || {}).limit);

    let clans;
    try {
        clans = await getLeaderboard(sql, limit);
    } catch (err) {
        console.error('[clan/leaderboard] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    return res.status(200).json({ ok: true, clans: clans });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
