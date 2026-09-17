'use strict';

// =============================================================================
// POST /api/pi/fulfill — WO-1797. THE PI RAIL'S FULFILMENT ACKNOWLEDGEMENT.
// -----------------------------------------------------------------------------
// THE DEFECT THIS CLOSES: before this route existed, every Pi purchase sat
// `verified` FOREVER, by construction. `api/purchases/fulfill.js` was the only
// writer of `status='fulfilled'`, and it refused a Pi caller three ways (a
// base58-only txid regex, a devnet/mainnet-beta-only network check, and
// authenticateGranting's allowlist). So the one column built to answer "did the
// player actually receive the goods" could not distinguish a WORKING Pi purchase
// from a BROKEN one — the alarm that matters was dead. (WO-1797 §1.)
//
// ⛔ THIS ROUTE DOES NOT GRANT ANYTHING AND MUST NEVER LEARN HOW. It records one
//    fact: A CLIENT SAYS IT PERSISTED A GRANT IT ALREADY HOLDS. The grant itself
//    is api/pi/complete.js (server ledger) + PiGrantApplier (local save).
//
// ⛔ WHY NOT JUST FLIP THE ROW IN /api/pi/complete? Because the server cannot know
//    the client persisted anything. A server-side flip is one word and it would
//    make this column LIE ON EVERY RAIL (WO-1797 §3.1). Declined on purpose.
//
// ── THE BEARER, STATED EXACTLY, BECAUSE IT IS THE WHOLE SECURITY ARGUMENT ─────
// A Pi identity is NOT on the granting allowlist (api/_lib/wallet-auth.js:862-869
// has exactly two modes, `wallet` and `google`, and `authenticate()` mints no Pi
// session), so this route cannot borrow /api/purchases/fulfill's auth. It does NOT
// borrow /api/pi/complete's either: that route has no caller authentication at all
// — its security property is that Pi's own /payments/<id> API is the oracle, and an
// ack proves nothing about a caller.
//
// So the bearer is named explicitly: **the (paymentId, txid) PAIR must already
// exist in our own pi_payments ledger with state='granted'**, and that row's
// player_id must match the entitlement's wallet — so a caller holding one payment's
// pair can never flip a DIFFERENT player's row. Both halves are server-written
// (complete.js:220-231).
//
// ⛔ THE LOAD-BEARING HALF OF THE ARGUMENT IS THE BOUNDED WORST CASE, NOT THE
//    UNGUESSABILITY. The entropy of a Pi payment id has NOT been measured here and no
//    claim is made about it. What IS certain is the ceiling: the only thing a forged
//    ack can do is mark a row THE PLAYER HAS ALREADY PAID FOR as delivered. It cannot
//    grant, create, refund or move money, and it cannot close a manual_review row.
//    Contrast a widened granting allowlist, where the worst case is a free pack.
//    ⚠ If that ceiling ever rises — if anything downstream starts READING `fulfilled`
//    to decide what a player is owed — this bearer is no longer sufficient and the
//    route needs a real Pi session. Say so then; do not quietly lean on it.
//
// WIRE
//   ->  { paymentId, txid }
//   <-  200 { ok:true, state:'fulfilled', paymentId, txid, sku, entitlementId, ref }
//       400 { ok:false, code:'BAD_PAYLOAD'|'PI_PAYMENT_UNKNOWN'|'PI_TXID_MISMATCH' }
//       404 { ok:false, code:'PI_ENTITLEMENT_UNKNOWN' }
//       409 { ok:false, code:'PI_ACK_NOT_YOURS'|'PI_PAYMENT_NOT_GRANTED' }
//       503 { ok:false, code:'PI_RECORD_FAILED' }
//
// REPLAY IS A NO-OP, DELIBERATELY: the client also acks from the
// onIncompletePaymentFound recovery path, so an ack must be safe to send twice. The
// conditional UPDATE and the COALESCE'd timestamp live in
// api/_lib/purchase-fulfilment.js — ONE statement in the tree writes `fulfilled`,
// and a replay writes nothing, emits no second audit row, and re-answers 200.
//
// ⚠ WHAT THAT RECOVERY PATH DOES **NOT** COVER — read before trusting it (WO-1797
// §3.3 overstates this). complete.js calls Pi's own /payments/<id>/complete at
// :149-164, BEFORE the entitlement insert. So a payment that reached /complete
// successfully is `developer_completed` at Pi and Pi will NOT re-present it through
// onIncompletePaymentFound. A fresh purchase whose fire-and-forget ack is then lost
// (tab closed, offline, crash) therefore does NOT self-heal — it stays `verified`.
// That residual gap is smaller than the defect this route closes, and closing it
// needs a deliberate sweep (a reconcile over pi_payments.state='granted' rows whose
// entitlement is still `verified`), not an assumption.
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const { applyCors, newRef, quietFail, readBodyExact } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { markEntitlementFulfilled } = require('../_lib/purchase-fulfilment');
const pi = require('../_lib/pi-payments');

const MAX_BODY_BYTES = 16 * 1024;

function refuse(res, status, code, ref, extra) {
    return res.status(status).json(Object.assign({ ok: false, code,
        message: pi.PI_MESSAGES[code] || undefined, ref }, extra || {}));
}

async function tryDb(run) {
    try { return { ok: true, rows: await run() }; }
    catch (err) { return { ok: false, err }; }
}

async function handler(req, res) {
    if (applyCors(req, res, 'POST, OPTIONS')) return;
    const ref = newRef();
    if (req.method !== 'POST') return quietFail(res, 400, 'METHOD_NOT_ALLOWED', ref);

    let body;
    try { body = JSON.parse((await readBodyExact(req, MAX_BODY_BYTES)).buffer.toString('utf8')); }
    catch (_) { return quietFail(res, 400, 'BAD_PAYLOAD', ref); }

    const paymentId = String((body && body.paymentId) || '').trim();
    const txid = String((body && body.txid) || '').trim();
    // Per-rail shapes, the same two this rail already validates at /approve and
    // /complete. The Solana TX_SIG_RE is deliberately NOT reused or relaxed.
    if (!pi.PAYMENT_ID_RE.test(paymentId) || !pi.TXID_RE.test(txid))
        return quietFail(res, 400, 'BAD_PAYLOAD', ref);

    let sql;
    try { sql = neon(process.env.DATABASE_URL); }
    catch (_) { return quietFail(res, 500, 'SERVER_ERROR', ref); }

    // ── THE BEARER: our own granted ledger row for this (paymentId, txid). ──
    const ledgerQ = await tryDb(() => sql`
        SELECT payment_id, player_id, sku, txid, state
        FROM pi_payments WHERE payment_id = ${paymentId} LIMIT 1`);
    if (!ledgerQ.ok) return refuse(res, 503, 'PI_RECORD_FAILED', ref);
    const ledger = ledgerQ.rows && ledgerQ.rows.length ? ledgerQ.rows[0] : null;
    if (!ledger) {
        // No silent failures on a payment path: an ack for a payment we never
        // recorded is audited, because it is either a client bug or a probe.
        await logApiEvent(sql, null, 'pi_fulfil_ack_refused',
            { ref, paymentId, txid, reason: 'payment_unknown' });
        return refuse(res, 400, 'PI_PAYMENT_UNKNOWN', ref);
    }
    if (String(ledger.state || '') !== 'granted') {
        await logApiEvent(sql, ledger.player_id, 'pi_fulfil_ack_refused',
            { ref, paymentId, txid, reason: 'state_' + String(ledger.state || 'null') });
        return refuse(res, 409, 'PI_PAYMENT_NOT_GRANTED', ref);
    }
    if (String(ledger.txid || '') !== txid) {
        await logApiEvent(sql, ledger.player_id, 'pi_fulfil_ack_refused',
            { ref, paymentId, txid, reason: 'txid_mismatch' });
        return refuse(res, 400, 'PI_TXID_MISMATCH', ref);
    }

    // ── The entitlement the Pi rail wrote for that txid (complete.js:191-201). ──
    const rowsQ = await tryDb(() => sql`
        SELECT entitlement_id, wallet, sku, network, status
        FROM purchase_entitlements WHERE tx_signature = ${txid} LIMIT 1`);
    if (!rowsQ.ok) return refuse(res, 503, 'PI_RECORD_FAILED', ref);
    const row = rowsQ.rows && rowsQ.rows.length ? rowsQ.rows[0] : null;
    if (!row) {
        await logApiEvent(sql, ledger.player_id, 'pi_fulfil_ack_refused',
            { ref, paymentId, txid, reason: 'entitlement_missing' });
        return refuse(res, 404, 'PI_ENTITLEMENT_UNKNOWN', ref);
    }
    // Ownership, on every axis the ledger can vouch for. A mismatch on ANY of
    // them means this pair does not bear this row, so it is a 409, never a flip.
    if (String(row.network || '') !== pi.PI_NETWORK ||
        String(row.wallet || '') !== String(ledger.player_id || '') ||
        String(row.sku || '') !== String(ledger.sku || '')) {
        await logApiEvent(sql, ledger.player_id, 'pi_fulfil_ack_refused',
            { ref, paymentId, txid, reason: 'entitlement_mismatch',
              rowNetwork: row.network, rowSku: row.sku });
        return refuse(res, 409, 'PI_ACK_NOT_YOURS', ref);
    }

    // `manual_review` is the third status the schema allows (api/schema.sql:1215).
    // A row a human is holding must NOT be silently closed by a client ack, and
    // must not be reported to the client as delivered either.
    if (String(row.status || '') !== 'verified' && String(row.status || '') !== 'fulfilled') {
        await logApiEvent(sql, ledger.player_id, 'pi_fulfil_ack_refused',
            { ref, paymentId, txid, reason: 'status_' + String(row.status || 'null') });
        return refuse(res, 409, 'PI_MANUAL_REVIEW', ref, { reason: String(row.status || '') });
    }

    let outcome;
    try { outcome = await markEntitlementFulfilled(sql, row, String(ledger.player_id), ref); }
    catch (_) { return refuse(res, 503, 'PI_RECORD_FAILED', ref); }

    return res.status(200).json({ ok: true, state: outcome.state, replay: !outcome.flipped,
        paymentId, txid, sku: row.sku, entitlementId: String(row.entitlement_id), ref });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
module.exports._test = { refuse, tryDb };
