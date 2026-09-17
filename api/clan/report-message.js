// =============================================================================
// api/clan/report-message.js — WO-1847. POST /api/clan/report-message
// -----------------------------------------------------------------------------
//   POST  { playerId, clanId, messageId }   Headers: X-Wallet / X-Nonce / X-Signature
//                                            (or X-Session — the WO-1157 session rail)
//   200   { ok:true }
//   400   CLAN_BAD_CLAN_ID | CLAN_BAD_MESSAGE_ID  (or any preamble argument refusal)
//   401   any auth refusal (same shape as every other clan route)
//   403   CLAN_REPORT_NOT_MEMBER
//   500   SERVER_ERROR | CLAN_IDENTITY_MISSING
//
// ⛔ WHY THIS ENDPOINT EXISTS AT ALL. WO-1265 — this project's standing ruling on clan
// chat — required moderation and reporting to be in place before free-text messaging
// shipped. WO-1847 replaces native chat with Cherry's embedded room chat WITH free text,
// under an explicit owner ruling that waives the pre-ship rate-limit gate for a
// pre-revenue hackathon demo. This route is the mitigation that ruling names: it keeps
// the underlying condition structurally true from the first build, so a report has
// somewhere to land before anyone can send a message nobody can report.
//
// ⛔ AND WHAT IT DELIBERATELY IS NOT. There is no read side, no admin view, and no
// consequence to the reported player — the review tooling is deferred with the admin
// clan-health ticket. `clan_reports` is written by this route and read by nothing, and
// that is the ticket's explicit non-scope. The copy rule follows from it: the client
// says "Reported — thank you" and never implies an immediate action, because none
// happens yet.
//
// ⛔ NO RATE LIMIT BEYOND THE GENERIC ONES, by the work order. A report is a low-value
// abuse target: flooding it costs the attacker a signed request each and gains them a
// table nobody reads. The generic auth-rail budgets already apply through the shared
// preamble.
// =============================================================================

'use strict';

const { AuthCode } = require('../_lib/wallet-auth');
const { quietFail } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { beginClanRequest, clanFail } = require('../_lib/clan-http');
const { reportMessage } = require('../_lib/clan');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    let result;
    try {
        result = await reportMessage(sql, wallet, body.clanId, body.messageId);
    } catch (err) {
        console.error('[clan/report-message] failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        return clanFail(res, result.status, result.code, ref);
    }

    try {
        // ⚠ THE REPORTED message_id IS NOT LOGGED. It is an opaque Cherry identifier and
        // the audit log is a different retention surface from the table that is meant to
        // hold it; duplicating it here would put the same moderation data in two places
        // with one deletion path. The report id and the clan are enough to find the row.
        await logApiEvent(sql, wallet, 'clan_message_reported', { clanId: result.clanId, reportId: result.reportId });
    } catch (_) { /* telemetry never fails a completed write */ }

    return res.status(200).json({ ok: true });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
