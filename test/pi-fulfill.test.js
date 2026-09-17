'use strict';

// =============================================================================
// WO-1797 — the Pi rail had NO fulfilment acknowledgement, so EVERY Pi purchase
// sat `status='verified'` forever and the one column that answers "was the player
// actually served" could not tell a working Pi purchase from a broken one.
// -----------------------------------------------------------------------------
// These cases ARE the acceptance criteria (WO-1797 §4.2). They drive the REAL
// handler (api/pi/fulfill.js) with the neon driver replaced in require.cache, the
// same zero-network pattern test/game.load.test.js established. NO api/ source
// file is modified by this suite.
//
// THE FOUR THAT MATTER, in the order the money cares about:
//   1. THE TRANSITION HAPPENS: a granted (paymentId, txid) pair flips verified ->
//      fulfilled, with fulfilled_at COALESCE'd so it is set once.
//   2. REPLAY IS A NO-OP: the client re-acks on every onIncompletePaymentFound
//      pass (that is the self-healing design, not a bug), so a second ack must
//      write NOTHING and must not emit a second audit row.
//   3. A FORGED ACK IS REFUSED, four ways: unknown payment, a payment that is not
//      `granted`, a txid that does not match the ledger, and a pair that bears a
//      DIFFERENT player's entitlement. None of them may flip a row.
//   4. THE SOLANA RAIL IS BYTE-IDENTICAL: TX_SIG_RE is unchanged and still
//      refuses a Pi hex txid, because the per-rail validator — not a widened
//      regex — is what lets one route serve two alphabets.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const Module = require('node:module');

// ── The database stub, installed before fulfill.js captures the real neon ────

let piRow = null;            // the pi_payments row the ledger answers with
let entRow = null;           // the purchase_entitlements row
let updateAffects = 1;       // rows the conditional UPDATE claims (0 = lost race)
let queries = [];            // every assembled statement, for shape assertions
let events = [];             // analytics_events inserts (the audit trail)
let throwOn = null;          // substring: make that statement reject

function sqlTag(strings, ...values) {
    const text = strings.raw.join('?');
    queries.push({ text, values });
    if (throwOn && text.includes(throwOn)) return Promise.reject(new Error('db down'));

    if (text.includes('FROM pi_payments')) return Promise.resolve(piRow ? [piRow] : []);
    if (text.includes('FROM purchase_entitlements')) return Promise.resolve(entRow ? [entRow] : []);
    if (text.includes('UPDATE purchase_entitlements')) {
        if (updateAffects > 0 && entRow) entRow = Object.assign({}, entRow, { status: 'fulfilled' });
        return Promise.resolve(updateAffects > 0 ? [{ entitlement_id: entRow.entitlement_id }] : []);
    }
    if (text.includes('INSERT INTO analytics_events')) {
        events.push({ playerId: values[0], name: values[1], props: values[2] });
        return Promise.resolve([]);
    }
    return Promise.resolve([]);
}

const neonId = require.resolve('@neondatabase/serverless');
require.cache[neonId] = new Module(neonId, null);
require.cache[neonId].filename = neonId;
require.cache[neonId].loaded = true;
require.cache[neonId].exports = { neon: () => sqlTag };

const piFulfill = require(path.join(__dirname, '..', 'api', 'pi', 'fulfill.js'));
const { _test: solanaFulfill } = require('../api/purchases/fulfill');
const pi = require('../api/_lib/pi-payments');

// ── A request/response pair the real applyCors / readBodyExact can drive ─────

const PAYMENT_ID = 'nAbC_deF-1234567890';
const TXID = '51a1fd62cb5a6ad5fd453af62829df9fd180e8cff582fa12a4487daf5f95bb89'; // the real row's hex txid
const PLAYER = 'pi-a28de6eb-0ed1-4ee3-911d-b95e8d0bdbca';
const SKU = 'hearth-spark';

function makeRes() {
    return {
        statusCode: null, body: null, headers: {}, ended: false,
        setHeader(k, v) { this.headers[k] = v; },
        status(c) { this.statusCode = c; return this; },
        json(b) { this.body = b; return this; },
        end() { this.ended = true; return this; },
    };
}

async function ack(body, method = 'POST') {
    const req = { method, headers: {}, complete: true,
        body: typeof body === 'string' ? body : JSON.stringify(body) };
    const res = makeRes();
    await piFulfill(req, res);
    return res;
}

function reset() {
    piRow = { payment_id: PAYMENT_ID, player_id: PLAYER, sku: SKU, txid: TXID, state: 'granted' };
    entRow = { entitlement_id: 8, wallet: PLAYER, sku: SKU, network: 'pi', status: 'verified' };
    updateAffects = 1;
    queries = [];
    events = [];
    throwOn = null;
}

function flips() {
    return queries.filter(q => q.text.includes('UPDATE purchase_entitlements'));
}

function fulfilEvents() {
    return events.filter(e => e.name === 'purchase_entitlement_fulfilled');
}

// ── 1. THE TRANSITION HAPPENS ────────────────────────────────────────────────

test('a granted (paymentId, txid) pair flips the entitlement verified -> fulfilled', async () => {
    reset();
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });

    assert.equal(res.statusCode, 200, `ack refused: ${JSON.stringify(res.body)}`);
    assert.equal(res.body.ok, true);
    assert.equal(res.body.state, 'fulfilled', 'the row did not reach the fulfilled state');
    assert.equal(res.body.replay, false, 'the first ack must report itself as the real transition');
    assert.equal(res.body.entitlementId, '8');
    assert.equal(res.body.sku, SKU);

    const flip = flips();
    assert.equal(flip.length, 1, 'exactly one UPDATE must run');
    assert.match(flip[0].text, /status = 'fulfilled'/);
    assert.match(flip[0].text, /fulfilled_at = COALESCE\(fulfilled_at, NOW\(\)\)/,
        'fulfilled_at must be COALESCE\'d or a replay would move the delivery timestamp');
    assert.match(flip[0].text, /AND status = 'verified'/,
        'the transition must be CONDITIONAL - an unconditional flip can resurrect a manual_review row');

    assert.equal(fulfilEvents().length, 1, 'the transition was not audited');
    assert.equal(fulfilEvents()[0].playerId, PLAYER,
        'the audit row must be attributed to the paying Pioneer, not anonymous');
});

// ── 2. REPLAY IS A NO-OP ─────────────────────────────────────────────────────

test('a replayed ack writes nothing, emits no second audit row, and still answers 200', async () => {
    reset();
    await ack({ paymentId: PAYMENT_ID, txid: TXID });
    queries = [];
    events = [];

    // The ledger row is unchanged (complete.js short-circuits a replay to
    // state:'granted'), but the entitlement is now already fulfilled.
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });

    assert.equal(res.statusCode, 200, 'a replay must be idempotent, not an error the client retries');
    assert.equal(res.body.state, 'fulfilled');
    assert.equal(res.body.replay, true, 'a replay must be reported as a replay');
    assert.equal(flips().length, 0, 'a replay re-ran the UPDATE');
    assert.equal(fulfilEvents().length, 0, 'a replay emitted a SECOND purchase_entitlement_fulfilled row');
});

test('losing the race to a concurrent ack is reported as fulfilled, never as a failure', async () => {
    reset();
    updateAffects = 0;               // the conditional UPDATE claims zero rows
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });
    assert.equal(res.statusCode, 200);
    assert.equal(res.body.state, 'fulfilled');
    assert.equal(res.body.replay, true);
    assert.equal(fulfilEvents().length, 0, 'the race winner already wrote the audit row');
});

// ── 3. A FORGED ACK IS REFUSED ───────────────────────────────────────────────

test('an ack for a payment we never recorded is refused and AUDITED, never flipped', async () => {
    reset();
    piRow = null;
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });
    assert.equal(res.statusCode, 400);
    assert.equal(res.body.code, 'PI_PAYMENT_UNKNOWN');
    assert.equal(flips().length, 0);
    // No silent failures on a payment path (CLAUDE.md §12).
    assert.equal(events.filter(e => e.name === 'pi_fulfil_ack_refused').length, 1,
        'a refused ack on a money path must leave a trace');
});

test('an ack for a payment that is NOT granted is refused', async () => {
    for (const state of ['approved', 'completed', 'manual_review', 'rejected']) {
        reset();
        piRow.state = state;
        const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });
        assert.equal(res.statusCode, 409, `state='${state}' answered ${res.statusCode}`);
        assert.equal(res.body.code, 'PI_PAYMENT_NOT_GRANTED');
        assert.equal(flips().length, 0, `state='${state}' was allowed to flip a row`);
    }
});

test('the txid must match the ledger - the PAIR is the bearer, not the payment id alone', async () => {
    reset();
    piRow.txid = 'a'.repeat(64);
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });
    assert.equal(res.statusCode, 400);
    assert.equal(res.body.code, 'PI_TXID_MISMATCH');
    assert.equal(flips().length, 0);
});

test('a pair can never flip ANOTHER player\'s row, or a row on another rail', async () => {
    for (const drift of [{ wallet: 'pi-someone-else' }, { sku: 'other-pack' }, { network: 'devnet' }]) {
        reset();
        Object.assign(entRow, drift);
        const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });
        assert.equal(res.statusCode, 409, `${JSON.stringify(drift)} answered ${res.statusCode}`);
        assert.equal(res.body.code, 'PI_ACK_NOT_YOURS');
        assert.equal(flips().length, 0, `${JSON.stringify(drift)} flipped a row it does not own`);
    }
});

test('a manual_review entitlement is NOT closed by a client ack', async () => {
    reset();
    entRow.status = 'manual_review';
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });
    assert.equal(res.statusCode, 409, 'a row a human is holding must not be closed by a client');
    assert.equal(res.body.code, 'PI_MANUAL_REVIEW');
    assert.equal(flips().length, 0);
});

test('malformed shapes and non-POST are refused before the database is touched', async () => {
    for (const body of [{}, { paymentId: PAYMENT_ID }, { txid: TXID },
        { paymentId: PAYMENT_ID, txid: 'short' }, { paymentId: '!!bad!!', txid: TXID },
        { paymentId: PAYMENT_ID, txid: 'has spaces in it and is long enough' }, 'not json']) {
        reset();
        const res = await ack(body);
        assert.equal(res.statusCode, 400, `${JSON.stringify(body)} answered ${res.statusCode}`);
        assert.equal(queries.length, 0, `${JSON.stringify(body)} reached the database`);
    }
    reset();
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID }, 'GET');
    assert.equal(res.statusCode, 400);
    assert.equal(queries.length, 0);
});

test('a database failure answers 503 and never claims the row was fulfilled', async () => {
    reset();
    throwOn = 'FROM pi_payments';
    const res = await ack({ paymentId: PAYMENT_ID, txid: TXID });
    assert.equal(res.statusCode, 503);
    assert.equal(res.body.code, 'PI_RECORD_FAILED');
    assert.equal(res.body.ok, false);
    assert.equal(flips().length, 0);
});

// ── 4. THE SOLANA RAIL IS UNTOUCHED ─────────────────────────────────────────

test('⛔ TX_SIG_RE is still base58-only and still refuses a Pi hex txid', () => {
    assert.equal(String(solanaFulfill.TX_SIG_RE), '/^[1-9A-HJ-NP-Za-km-z]{80,90}$/',
        'the Solana signature regex was relaxed - that is a real hole on the ed25519 rail');
    assert.equal(solanaFulfill.TX_SIG_RE.test(TXID), false,
        'a 64-char hex Pi txid must NOT satisfy the Solana validator');
    assert.equal(solanaFulfill.signatureOk('pi', TXID), true,
        'the Pi rail validator must accept the Pi txid');
    assert.equal(solanaFulfill.signatureOk('pi', 'short'), false);
    assert.equal(solanaFulfill.signatureOk('devnet', TXID), false,
        'the Solana branch must not inherit the Pi alphabet');
    assert.equal(solanaFulfill.signatureOk('devnet', '5'.repeat(88)), true);
    assert.equal(solanaFulfill.signatureOk('mainnet-beta', '5'.repeat(88)), true);
    // The per-rail validator IS pi-payments' own, not a copy.
    assert.equal(solanaFulfill.signatureOk('pi', 'x'.repeat(16)), pi.TXID_RE.test('x'.repeat(16)));
});

test('the Solana rail\'s ownership guard is unchanged', () => {
    const row = { wallet: 'W', sku: 'S', network: 'devnet' };
    assert.equal(solanaFulfill.matches(row, 'W', 'S', 'devnet'), true);
    assert.equal(solanaFulfill.matches(row, 'OTHER', 'S', 'devnet'), false);
    assert.equal(solanaFulfill.matches(row, 'W', 'OTHER', 'devnet'), false);
    assert.equal(solanaFulfill.matches(row, 'W', 'S', 'pi'), false);
    assert.equal(solanaFulfill.matches(null, 'W', 'S', 'devnet'), false);
});

test('⛔ exactly ONE statement in api/ may write status=\'fulfilled\'', () => {
    const fs = require('node:fs');
    const root = path.join(__dirname, '..', 'api');
    const hits = [];
    (function walk(dir) {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const p = path.join(dir, entry.name);
            if (entry.isDirectory()) { walk(p); continue; }
            if (!entry.name.endsWith('.js')) continue;
            const text = fs.readFileSync(p, 'utf8');
            // The WRITE, not a comment or a read: an UPDATE assigning the status.
            if (/UPDATE\s+purchase_entitlements[\s\S]{0,200}?SET\s+status\s*=\s*'fulfilled'/.test(text))
                hits.push(path.relative(root, p).replace(/\\/g, '/'));
        }
    })(root);
    assert.deepEqual(hits, ['_lib/purchase-fulfilment.js'],
        'the fulfilment transition was re-inlined - two rails must share ONE statement (WO-1797 §3.2.4)');
});
