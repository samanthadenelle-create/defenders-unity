'use strict';

// =============================================================================
// test/admin.store-open-quote-funnel.test.js — WO-1841.
//   the oracle for the store-open -> price-quote funnel cross-link on
//   GET /api/admin/stats?view=economy and ?view=purchases.
//
// ⛔ READ THIS BEFORE CHANGING ANYTHING HERE — THE TICKET'S PREMISE WAS FALSE.
// -----------------------------------------------------------------------------
// WO-1841 was written as "no store-open event exists anywhere in the client or
// backend today". It does. Verified at source, not from a doc:
//
//   * `store_opened {door}` is emitted at Assets/_Modules/Wallet/PackStore.cs:610
//     from TrackStoreOpened() (:601), called from exactly one site — OnEnable
//     (:563). The GOOGLE_PLAY artifact carries the mirror at
//     Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:108; the two asmdefs
//     are defineConstraints '!GOOGLE_PLAY' / 'GOOGLE_PLAY', so they are NEVER
//     both compiled and step 1 cannot double-fire.
//   * Both landed in commit 9b47c9ad93 (2026-09-05) as WO-1388.
//   * api/admin/stats.js ?view=economy has rendered them as `store_funnel`
//     (7d/30d counts, distinct players, and a `door` breakdown) ever since.
//   * A device log proves it fires: Logs/f8-inbox/device/SM02G4061955851/
//     flags-20260917/logcat-after-372984.txt, 09-17 10:26:46.393 —
//     "[Flow:Store]   funnel store_opened door=hud-card (inferred)".
//
// So NO new event was added and NO new admin view was added. Adding either would
// have double-counted step 1 of a live funnel, or duplicated a working view.
//
// WHAT WAS ACTUALLY MISSING, and is what these cases pin.
// -----------------------------------------------------------------------------
// The two halves of the owner's question lived in two views with no pointer
// between them: `store_opened` is in ?view=economy, and the PRICE-QUOTE step is
// purchase_quotes, reported by ?view=purchases -> quote_funnel. An operator
// reading a low `issued` could not tell "nobody opened the store" from "they
// opened it and never reached the till" without already knowing the other view
// existed — the same undiscoverability CLAUDE.md keeps paying for.
//
// ⭐ AND THE CROSS-LINK IS A POINTER, NOT A RATIO. That is the load-bearing part:
//   * analytics_events keys on player_id; purchase_quotes keys on `wallet`
//     (api/schema.sql:1395). No bridge exists, so there is no per-player
//     opened->quoted conversion to compute, and a ratio across two different
//     denominators would READ as a conversion rate while being an artifact.
//   * api/admin/stats.js already states its own contract for this: client-
//     reported intent and server settlement "are never blended".
// So both responses must say joinable_to_store_opened is FALSE and say WHY, and
// neither may publish a percentage spanning the two sources.
//
// ⚠ ONE MORE FACT THAT HAD TO BE READ, OR THE WHOLE STEP WOULD BE MEANINGLESS:
// opening the store does NOT mint a quote row. PackStore.Open calls
// RefreshQuotedPrices (PackStore.cs:571), which hits the LIST mode of
// api/purchases/quote.js — "binds nothing, persists nothing" (quote.js:353).
// The INSERTs (quote.js:471, :519) fire only on a single-SKU quote. Had the
// opposite been true, issued would be ~opens x packs and the "drop-off" the
// ticket asked for would have been noise.
//
// PROVEN RED, measured — not asserted. With api/admin/stats.js restored to its
// HEAD state the run is `pass 3 / fail 3`, and the three that fail are exactly
// cases 3, 4 and 5 (the keys are simply absent from both response bodies).
//
// Cases 1, 2 and 6 pass in BOTH states, each for a stated reason:
//   * case 1 is the pre-existing auth gate, which this ticket does not touch;
//   * case 2 passes at HEAD BECAUSE THE TICKET'S PREMISE WAS FALSE — store_opened
//     was already step 1 of a working funnel. Its passing red run is the evidence
//     for the finding above, not a weakness in the case, and it is kept so a
//     later lane cannot quietly remove step 1 while adding something new;
//   * case 6 is the ratio lint, which must hold BEFORE and AFTER or it is not
//     guarding anything.
//
//   node --test test/admin.store-open-quote-funnel.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.join(__dirname, '..');
const STATS_REL = 'api/admin/stats.js';
const STATS_ABS = path.join(REPO, STATS_REL);
const KEY = 'store-open-funnel-test-key';

// -- harness. res/req + the stubbed neon driver are borrowed verbatim from
//    test/admin.events.view.test.js so the admin endpoints are exercised ONE
//    way, not two. Nothing can connect: every tagged template is captured
//    instead, so a query that slipped past the read-only guards is visible here
//    rather than silently running against a real table.

function fakeRes() {
    const out = { statusCode: null, body: null, headers: {} };
    const res = {
        setHeader(k, v) { out.headers[String(k).toLowerCase()] = v; return res; },
        status(code) { out.statusCode = code; return res; },
        json(obj) { out.body = obj; return res; },
        send() { return res; },
        end() { return res; },
    };
    res.out = out;
    return res;
}

function fakeReq(query, headers) {
    return { method: 'GET', headers: headers || { 'x-admin-key': KEY }, query: query || {} };
}

const DRIVER_ID = require.resolve('@neondatabase/serverless');

function loadHandlerWithCapture() {
    const captured = [];
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = {
        neon() {
            const sql = (strings, ...values) => {
                captured.push({ sql: Array.from(strings).join(' ? '), values: values });
                return Promise.resolve([]);
            };
            return sql;
        },
    };
    delete require.cache[require.resolve(STATS_ABS)];
    const handler = require(STATS_ABS);
    // Restore immediately — the handler holds the stub through its closure, and a
    // leaked stub would poison every later test file in the same process.
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(STATS_ABS)];
    return { handler: handler, captured: captured };
}

async function call(query, headers) {
    const prevKey = process.env.ADMIN_DASH_KEY;
    const prevUrl = process.env.DATABASE_URL;
    process.env.ADMIN_DASH_KEY = KEY;
    // Syntactically valid, deliberately unreachable: real enough that neon()
    // constructs, dead enough that a real query could not succeed.
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const { handler, captured } = loadHandlerWithCapture();
    const res = fakeRes();
    try {
        await handler(fakeReq(query, headers), res);
    } finally {
        if (prevKey === undefined) delete process.env.ADMIN_DASH_KEY; else process.env.ADMIN_DASH_KEY = prevKey;
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, captured: captured };
}

// -- 1. THE GATE (pre-existing; passes in both states, and must) --------------

test('economy and purchases refuse without the admin key, and issue no query', async () => {
    for (const view of ['economy', 'purchases']) {
        const r = await call({ view: view }, { 'x-admin-key': 'wrong' });
        assert.equal(r.out.statusCode, 400, 'view=' + view + ' (this API answers 400, never 401)');
        assert.deepEqual(r.captured, [], 'a refused request must not touch the database');
    }
});

// -- 2. STEP 1 IS STILL THERE, AND IS STILL store_opened ---------------------

test('store_funnel still leads with store_opened and still breaks it down by door', async () => {
    const r = await call({ view: 'economy' });
    assert.equal(r.out.statusCode, 200);
    const sf = r.out.body.store_funnel;
    assert.ok(sf, '?view=economy must carry store_funnel');
    const steps = (sf.steps || []).map((s) => s.event);
    assert.equal(steps[0], 'store_opened',
        'store_opened is step 1 of the funnel and must be reported first, even at zero');
    assert.ok(Object.prototype.hasOwnProperty.call(sf, 'store_opened_doors'),
        'the door breakdown is how "which entry point" is answered');
    // Every step present even at zero: a step that vanishes when it has no rows
    // reads as "not measured" instead of "measured, nobody did it".
    for (const name of ['pack_tapped', 'checkout_started', 'checkout_failed', 'purchase_completed']) {
        assert.ok(steps.includes(name), 'store_funnel omits ' + name);
    }
});

// -- 3. THE FORWARD POINTER: store_funnel NAMES the quote step --------------

test('store_funnel names where the price-quote step lives, and that it is NOT joinable', async () => {
    const r = await call({ view: 'economy' });
    const qs = (r.out.body.store_funnel || {}).quote_step;
    assert.ok(qs, 'store_funnel must name the quote step — that is the WO-1841 ask');
    assert.match(qs.where, /view=purchases/,
        'the pointer must name the view an operator has to open');
    assert.match(qs.where, /quote_funnel/, 'and the key inside it');
    assert.match(qs.source, /purchase_quotes/,
        'the pointer must name the TABLE, so nobody looks for it in analytics_events');

    // ⭐ The honest refusal, stated rather than implied.
    assert.equal(qs.joinable_to_store_opened, false,
        'a per-player opened->quoted join is impossible and must be declared impossible');
    assert.match(qs.why_not, /player_id/, 'the reason must name the client-side key');
    assert.match(qs.why_not, /wallet/, 'and the server-side key');

    // And the fact that keeps the step meaningful at all.
    assert.match(qs.not_a_quote, /persists nothing|display/i,
        'the response must state that opening the store does not itself mint a quote row');
});

// -- 4. THE REVERSE POINTER: quote_funnel NAMES the earlier step ------------

test('quote_funnel names store_funnel as its preceding step', async () => {
    const r = await call({ view: 'purchases' });
    assert.equal(r.out.statusCode, 200);
    const qf = r.out.body.quote_funnel;
    assert.ok(qf, '?view=purchases must carry quote_funnel');
    assert.ok(qf.preceding_step, 'a low `issued` is undiagnosable without the earlier step');
    assert.match(qf.preceding_step, /view=economy/, 'the reverse pointer must name the other view');
    assert.match(qf.preceding_step, /store_opened/, 'and the event that answers "did anyone open it"');
    assert.match(qf.preceding_step, /cannot be joined/i,
        'the reverse pointer must carry the same refusal as the forward one, or one of them lies');
    // The numbers themselves must still be there — the pointer is an addition,
    // never a replacement for the counts this view already published.
    for (const k of ['issued', 'consumed', 'consumed_pct', 'wallets_quoted']) {
        assert.ok(Object.prototype.hasOwnProperty.call(qf, k), 'quote_funnel lost ' + k);
    }
});

// -- 5. THE TWO POINTERS AGREE ---------------------------------------------

test('both directions of the cross-link are present in one pass, and neither is one-way', async () => {
    const econ = await call({ view: 'economy' });
    const purch = await call({ view: 'purchases' });
    const fwd = (econ.out.body.store_funnel || {}).quote_step;
    const rev = (purch.out.body.quote_funnel || {}).preceding_step;
    assert.ok(fwd && rev,
        'a half-built cross-link is worse than none: whichever view the operator opens first is '
        + 'the one that must point at the other');
});

// -- 6. ⛔ NO BLENDED RATIO, EVER (must hold before AND after) --------------

test('no response key divides a client-reported count by a server-reported one', () => {
    const src = fs.readFileSync(STATS_ABS, 'utf8');
    // pct(a, b) is this file's percentage helper. A call whose two arguments come
    // from opposite sides of the client/server line would be exactly the fake
    // conversion rate this ticket refused to publish.
    const forbidden = [
        /pct\(\s*issued\s*,\s*[a-z_]*store[a-z_]*\s*\)/i,
        /pct\(\s*[a-z_]*store[a-z_]*\s*,\s*issued\s*\)/i,
        /pct\(\s*[a-z_]*quote[a-z_]*\s*,\s*[a-z_]*opened[a-z_]*\s*\)/i,
        /pct\(\s*[a-z_]*opened[a-z_]*\s*,\s*[a-z_]*quote[a-z_]*\s*\)/i,
    ];
    for (const re of forbidden) {
        assert.ok(!re.test(src),
            'a percentage spanning analytics_events and purchase_quotes appeared (' + re + '). '
            + 'The two have different denominators and no shared identity key; such a number reads '
            + 'as a conversion rate and is an artifact. Report the counts side by side.');
    }
    // And the contract sentence itself must survive in the file.
    assert.match(src, /never blended/,
        'the client-reported vs server-truth contract must stay stated in api/admin/stats.js');
});
