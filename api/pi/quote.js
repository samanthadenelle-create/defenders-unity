'use strict';

// =============================================================================
// POST /api/pi/quote — WO-1318. THE SERVER DECIDES THE PI AMOUNT.
// -----------------------------------------------------------------------------
// The Pi twin of /api/purchases/quote, on the SAME table and the SAME policy.
// A pack is priced in USD and PAID in Pi, so the Pi figure depends on the rate at
// the moment of purchase. The client may name a SKU. It may never choose the
// number.
//
// ⛔ WHY THIS MATTERS MORE ON PI THAN ON SOLANA. `Pi.createPayment({ amount })`
// runs in the player's browser, so whatever amount reaches Pi is client-supplied
// by construction. The ONLY thing standing between that and a 0.1 Pi purchase of
// a $4.99 pack is /api/pi/approve refusing to approve any payment whose amount is
// not EXACTLY the one persisted here, against this quote id.
//
// ⛔ THE ORACLE FAILS CLOSED. Rate unavailable -> 503 PURCHASE_RATE_UNAVAILABLE
// with a worded reason, never a stale value and never the catalog figure. There
// is deliberately NO static Pi price anywhere in this rail: charging a made-up
// number is worse than refusing to sell.
//
// WIRE
//   ->  { sku, uid, accessToken? }
//   <-  200 { ok:true, quoteId, amount, memo, sku, rate, rateSource, expiresAt, ... }
//       503 { ok:false, code:'PURCHASE_RATE_UNAVAILABLE', message, ref }
//       404 { ok:false, code:'PI_SKU_UNAVAILABLE', message, ref }
//
// ⚠ NO CUSTOM REQUEST HEADER IS READ HERE, ON PURPOSE (api/_lib/http.js note 1):
// this endpoint is called cross-origin from <app>.pinet.com, and a custom header
// turns every call into a preflight. Everything travels in the JSON body.
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const crypto = require('crypto');
const { applyCors, newRef, quietFail, readBodyExact } = require('../_lib/http');
const { logApiEvent } = require('../_lib/audit');
const { enforce: maintenanceEnforce, AREA_STORE } = require('../_lib/maintenance');
const pi = require('../_lib/pi-payments');
// WO-1799 the storewide sale, read through the SAME helper the SKR rail uses.
// ⛔ There is NO shortfall discount on the Pi rail (it has never had one), so the
// sale is the ONLY discount here and resolveDiscount is not needed.
const { readStoreSale, SALE_REASON } = require('../_lib/store-sale');

const MAX_BODY_BYTES = 16 * 1024;

function quoteRef() { return crypto.randomBytes(16).toString('hex'); }

function refuse(res, status, code, ref, extra) {
    return res.status(status).json(Object.assign({ ok: false, code,
        message: pi.PI_MESSAGES[code] || undefined, ref }, extra || {}));
}

async function handler(req, res) {
    if (applyCors(req, res, 'POST, OPTIONS')) return;
    const ref = newRef();
    if (req.method !== 'POST') return quietFail(res, 400, 'METHOD_NOT_ALLOWED', ref);
    // Dormant unless the key is present. Mirrors the Play rail's
    // configurationReady gate — an unconfigured rail refuses, it does not guess.
    if (!pi.configured()) return refuse(res, 503, 'PI_NOT_CONFIGURED', ref);

    let body;
    try { body = JSON.parse((await readBodyExact(req, MAX_BODY_BYTES)).buffer.toString('utf8')); }
    catch (_) { return quietFail(res, 400, 'BAD_PAYLOAD', ref); }

    const sku = String(body.sku || '').trim();
    let uid = String(body.uid || '').trim();
    const accessToken = String(body.accessToken || '').trim();

    // ⭐ IDENTITY: PROVEN WHEN WE CAN, BOUND EITHER WAY.
    //
    // When the client sends its Pi accessToken we ask Pi itself who it belongs to
    // (the same trust boundary api/pi/verify.js uses) and the quote is issued to
    // the PROVEN uid, ignoring whatever the body claimed.
    //
    // ⛔ AND WHEN IT DOES NOT, THE QUOTE IS STILL NOT AN AUTHORIZATION HOLE. A
    // quote grants nothing and charges nothing. It is spendable only by a Pi
    // payment whose `user_uid` — Pi's own statement of who paid, which no client
    // can set — matches this subject; see validatePaymentAgainstQuote. Forging a
    // uid here therefore mints a ticket only its rightful owner can ever use.
    let uidProven = false;
    if (accessToken) {
        const me = await pi.verifyPiAccessToken(accessToken);
        if (!me.ok) return refuse(res, 401, 'PI_TOKEN_REJECTED', ref);
        uid = me.uid;
        uidProven = true;
    }

    const playerId = pi.piPlayerId(uid);
    if (!sku || !playerId) return quietFail(res, 400, 'BAD_PAYLOAD', ref);
    if (pi.piSkuUsd(sku) == null) return refuse(res, 404, 'PI_SKU_UNAVAILABLE', ref);

    let sql = null;
    try { sql = neon(process.env.DATABASE_URL); }
    catch (_) { return quietFail(res, 500, 'SERVER_ERROR', ref); }

    // ⭐ THE PRE-PAYMENT GATE, AND THE ONLY PLACE ON THIS RAIL THE STORE SEAL MAY
    // SIT. A quote is the last step before the player's Pi wallet is asked for
    // anything, so refusing here costs a player nothing.
    // ⛔ NEVER add this to pi/approve.js or pi/complete.js — those run with a
    // payment in flight, and sealing them would strand a paid player.
    if (await maintenanceEnforce(sql, req, res, AREA_STORE, playerId, ref)) return;

    // ── The rate. FAIL CLOSED. ───────────────────────────────────────────────
    const rate = await pi.fetchPiUsdRate();
    if (!rate) {
        await logApiEvent(sql, playerId, 'pi_quote_refused',
            { ref, sku, reason: 'rate_unavailable' });
        return refuse(res, 503, 'PURCHASE_RATE_UNAVAILABLE', ref);
    }

    // FAILS TO NO SALE: an unreadable knob table charges the ORDINARY Pi price.
    const sale = await readStoreSale(sql);
    const built = pi.buildPiQuoteBody(sku, rate,
        sale.bps > 0 ? sale.bps : null, SALE_REASON);
    if (!built) {
        await logApiEvent(sql, playerId, 'pi_quote_refused', { ref, sku, reason: 'contract_unavailable' });
        return refuse(res, 503, 'PI_SKU_UNAVAILABLE', ref);
    }

    // ── One binding, single-use, expiring row, in the EXISTING quote table. ──
    // mint / recipient / recipient_ata are Solana facts and are NULL on this rail;
    // the Pi payee is the app's own Pi wallet, which Pi resolves from the API key
    // and reports back as `to_address` on the payment we validate at approve.
    const quoteId = quoteRef();
    let inserted;
    try {
        inserted = await sql`
            INSERT INTO purchase_quotes
                (quote_ref, wallet, sku, network, currency, amount_base_units, decimals,
                 mint, recipient, recipient_ata, usd_anchor, usd_rate, rate_source,
                 discount_bps, discount_reason, expires_at)
            VALUES (${quoteId}, ${playerId}, ${sku}, ${pi.PI_NETWORK}, ${pi.PI_CURRENCY},
                    ${built.amountBaseUnits}, ${built.decimals},
                    NULL, NULL, NULL, ${built.usdAnchor}, ${built.rate}, ${built.rateSource},
                    ${built.discountBps}, ${built.discountReason},
                    NOW() + (${pi.QUOTE_TTL_SECONDS} * INTERVAL '1 second'))
            RETURNING quote_ref, expires_at`;
    } catch (_) { return quietFail(res, 500, 'SERVER_ERROR', ref); }
    if (!inserted || !inserted.length) return quietFail(res, 500, 'SERVER_ERROR', ref);

    // ⚠ A third-party rate source is a third-party dependency ON THE MONEY PATH.
    // WHICH source and WHICH value backed this quote is logged here AND stored on
    // the row, so a disputed charge can be reconstructed months later.
    await logApiEvent(sql, playerId, 'pi_quote_issued', { ref, sku, quoteId,
        amountBaseUnits: built.amountBaseUnits, usdAnchor: built.usdAnchor,
        rate: built.rate, rateSource: built.rateSource, uidProven,
        discountBps: built.discountBps, discountReason: built.discountReason });

    return res.status(200).json({
        ok: true,
        quoteId,
        amount: built.amount,                    // pass to Pi.createPayment verbatim
        memo: built.memo,                        // ASCII, must match at approve
        sku: built.sku,
        rate: built.rate,
        rateSource: built.rateSource,
        expiresAt: new Date(inserted[0].expires_at).toISOString(),
        // Additive, all derived from the SAME server calculation that priced the
        // amount. The client may DISPLAY these; it may never derive either one.
        network: built.network,
        currency: built.currency,
        decimals: built.decimals,
        amountBaseUnits: built.amountBaseUnits,
        usdAnchor: built.usdAnchor,
        // ── WO-1799 the storewide sale, on the wire ───────────────────────────
        // ⚠ ADDITIVE. The Pi/WebGL client renders the SERVER's Pi figure and the USD
        // anchor beside it (PackStore.StorePriceMajor / StorePriceMinor), and it does
        // NOT read any of the four fields below today — so on Pi a sale shows up as a
        // LOWER Pi amount with the full-price anchor under it, and a struck-through
        // shelf anchor needs a client change. Sent so that change never needs a
        // matching server change.
        usdEffective: built.usdEffective,
        usdSaving: built.usdSaving,
        discountBps: built.discountBps,
        discountLabel: built.discountLabel,
        saleBps: built.discountReason === SALE_REASON ? built.discountBps : null,
        saleEndsAt: built.discountBps != null && sale.endsAtMs
            ? new Date(sale.endsAtMs).toISOString() : null,
        uid,
        playerId,
        uidProven,
        // The exact metadata object Pi.createPayment must carry. Handing it back
        // whole is what stops the two halves drifting into a memo/metadata
        // mismatch that only shows up as a refused approve.
        metadata: { sku: built.sku, quoteId, uid },
        ref,
    });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };
module.exports._test = { quoteRef, refuse };
