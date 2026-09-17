'use strict';

// =============================================================================
// test/admin.ads.view.test.js — WO-1796.
//   the oracle for GET /api/admin/db?view=ads — the ad-revenue read surface.
//
// THE GAP THIS VIEW CLOSES, measured 2026-09-16. The owner asked, verbatim:
// "noone is ever buying a pack and I cant figure out how to see if ads are
// making anything". The money was ALREADY in the database: LevelPlayInitializer
// has been writing one `rewarded_ad_impression` row per impression, carrying the
// network's own figure in properties->>'revenueUsd', since the provider shipped.
// `grep -rn "revenueUsd" api/ tools/` returned ZERO matches — the property was
// written by the client and read by nothing on the server. No SUM, no view, no
// tile. The dead step was never the emitter; it was that nothing added it up.
//
// WHAT IS PROVEN HERE, and why each case exists.
// -----------------------------------------------------------------------------
//  1 THE GATE. The view sits behind ADMIN_DASH_KEY like every other read on this
//    endpoint, and a refusal costs no query (this API answers 200 | 400 | 500).
//
//  2 ⭐ THE ::numeric CAST IS REGEX-GUARDED, AND THAT IS NOT COSMETIC.
//    properties->>'revenueUsd' is TEXT out of JSONB. A bare cast THROWS 22P02 on
//    any non-numeric value — and api/events/track.js has a real code path that
//    wraps a malformed property as `_raw`, which is exactly that shape. One such
//    row would 500 the WHOLE view, so the owner's revenue screen would go dark
//    because one impression arrived odd. The guard is asserted in the SQL the
//    endpoint actually issues, and the absence of an UNGUARDED cast with it.
//
//  3 ⭐ A MISSING REVENUE IS NEVER SUMMED AS ZERO. "the network reported $0.00"
//    and "the network reported nothing" are different facts. The view counts the
//    second separately (impressions_without_revenue) and the client change stores
//    null rather than `?? 0d`. Collapsing them would understate revenue silently
//    and is the same class of lie as rendering a failed query as a 0.
//
//  4 ⭐ eCPM BELOW AN IMPRESSION FLOOR IS NOISE, NOT A METRIC. The whole measured
//    window was 17 impressions. An eCPM off 17 rows is a number the owner would
//    make a spend decision on and it would mean nothing, so below the floor the
//    view returns null plus low_n and the surface prints words.
//
//  5 NO USER INPUT IS EVER SQL TEXT. The driver is STUBBED so every tagged
//    template is captured — literal strings apart from bound values — and `days`
//    is asserted to appear only among the VALUES, for hostile inputs too.
//
//  6 EVERY QUERY IS A SELECT WITH A HARD LIMIT (the read-only-by-construction
//    contract at the top of api/admin/db.js), and `days` clamps.
//
//  7 THE COMPLETION RAIL IS ATTRIBUTED AND THE CROSS-RAIL TRAP IS PRINTED.
//    `rewarded_ad_impression` comes only from LevelPlay; `rewarded_ad_completed`
//    comes from AdGateService, which is provider-agnostic and fires for the Pi
//    Developer Ad Network too. impressions / completions is therefore NOT a
//    completion rate, and the response has to say so rather than leave a reader
//    to divide two numbers that do not belong to each other.
//
//  8 THE VIEW IS IN THE UNKNOWN-VIEW HINT. A view nobody can discover is a view
//    nobody uses — which is how `purchases` stayed missing from that hint for
//    weeks after it shipped.
//
// PROVEN RED: with api/admin/db.js at its pre-WO-1796 state every case below but
// the auth gate FAILS on 'Unknown view. Use: overview | ...' with zero captured
// queries (the gate predates the view and must pass in both states, or the
// assertions above it are vacuous).
//
//   node --test test/admin.ads.view.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.join(__dirname, '..');
const DB_ABS = path.join(REPO, 'api/admin/db.js');
const KEY = 'ads-view-test-key';

// -- harness (res/req shape borrowed verbatim from test/admin.events.view.test.js
//    so the admin endpoints are exercised ONE way, not two) -------------------

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

// -- the STUBBED driver ------------------------------------------------------
// Captures every tagged template WITHOUT a database: `sql` is the literal SQL the
// code wrote, `values` the bound parameters. `responder` lets a case hand back
// canned rows so the JS-side arithmetic (eCPM, the low_n floor, the totals) is
// executed rather than merely read.
const DRIVER_ID = require.resolve('@neondatabase/serverless');

function loadHandlerWithCapture(responder) {
    const captured = [];
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = {
        neon() {
            const sql = (strings, ...values) => {
                const text = Array.from(strings).join(' ? ');
                captured.push({ sql: text, values: values });
                return Promise.resolve(responder ? responder(text, values) : []);
            };
            return sql;
        },
    };
    delete require.cache[require.resolve(DB_ABS)];
    const handler = require(DB_ABS);
    // Restore immediately — the handler holds the stub through its closure, and a
    // leaked stub would poison every later test file in the same process.
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(DB_ABS)];
    return { handler: handler, captured: captured };
}

async function call(query, headers, responder) {
    const prevKey = process.env.ADMIN_DASH_KEY;
    const prevUrl = process.env.DATABASE_URL;
    process.env.ADMIN_DASH_KEY = KEY;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const { handler, captured } = loadHandlerWithCapture(responder);
    const res = fakeRes();
    try {
        await handler(fakeReq(query, headers), res);
    } finally {
        if (prevKey === undefined) delete process.env.ADMIN_DASH_KEY; else process.env.ADMIN_DASH_KEY = prevKey;
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, captured: captured };
}

// The aggregate query is the one naming the `imp` CTE; the completion query is the
// one naming rewarded_ad_completed. Dispatching on content rather than call order
// means a future reordering of the two reads does not silently mis-feed this test.
function isAggQuery(text) { return /WITH imp AS/.test(text); }
function isCompletionQuery(text) { return /rewarded_ad_completed/.test(text); }

function responderFor(aggRow, completionRows) {
    return (text) => {
        if (isAggQuery(text)) return [aggRow];
        if (isCompletionQuery(text)) return completionRows || [];
        return [];
    };
}

// -- 1. THE GATE -------------------------------------------------------------

test('view=ads refuses without the admin key, and issues no query', async () => {
    const r = await call({ view: 'ads' }, { 'x-admin-key': 'wrong' });
    assert.equal(r.out.statusCode, 400);
    assert.equal(r.out.body.error, 'Unauthorized');
    assert.deepEqual(r.captured, [], 'a refused request must not touch the database');
});

// -- 2. THE ::numeric CAST IS REGEX-GUARDED ----------------------------------

test('the revenueUsd cast is regex-guarded, so one non-numeric row cannot 500 the view', async () => {
    const r = await call({ view: 'ads' });
    assert.equal(r.out.statusCode, 200);
    const agg = r.captured.find(c => isAggQuery(c.sql));
    assert.ok(agg, 'the aggregate query must be issued');

    // The guard itself: a CASE whose WHEN is a numeric regex on the JSONB text.
    assert.match(agg.sql, /CASE WHEN properties->>'revenueUsd' ~ '\^-\?\[0-9\]/,
        'the cast must be gated by a numeric regex — a bare cast throws 22P02 (a bad row would 500 the view)');
    assert.match(agg.sql, /THEN \(properties->>'revenueUsd'\)::numeric/);

    // ...and NO unguarded cast anywhere. Counting occurrences catches a second,
    // ungated copy being added later beside the guarded one.
    const casts = (agg.sql.match(/\(properties->>'revenueUsd'\)::numeric/g) || []).length;
    assert.equal(casts, 1,
        'exactly ONE cast of revenueUsd may exist, and it is the guarded one — a second copy is the bug');

    // ...and the guard is not merely PRESENT, it is the only route to the cast:
    // stripping the guarded CASE must leave no cast behind anywhere in the file's
    // issued SQL. This is the assertion that survives a careless later edit.
    for (const c of r.captured) {
        const withoutGuardedCast = c.sql.replace(
            /CASE WHEN properties->>'revenueUsd' ~ '[^']*'\s*THEN \(properties->>'revenueUsd'\)::numeric/g, 'GUARDED');
        assert.ok(!withoutGuardedCast.includes("(properties->>'revenueUsd')::numeric"),
            'an UNGUARDED revenueUsd cast would 500 the whole view on one malformed row');
    }
});

// -- 3. A MISSING REVENUE IS NEVER SUMMED AS ZERO ---------------------------

test('impressions with no reported revenue are counted separately, not summed as zero', async () => {
    const r = await call({ view: 'ads' }, null, responderFor({
        per_day: [{ day: '2026-09-16', impressions: 4, impressions_without_revenue: 1, revenue_usd: 0.012 }],
        per_network: [{ network: 'ironSource', impressions: 3, impressions_without_revenue: 0, revenue_usd: 0.012 },
                      { network: '(not reported)', impressions: 1, impressions_without_revenue: 1, revenue_usd: 0 }],
        per_placement: [],
        impressions: 4, impressions_without_revenue: 1, revenue_usd: 0.012,
        newest_impression_at: '2026-09-16T10:00:00.000Z',
    }));
    assert.equal(r.out.statusCode, 200);
    const b = r.out.body;
    assert.equal(b.impressions, 4);
    assert.equal(b.impressions_without_revenue, 1,
        'the unreported-revenue impression must be visible as its own number');
    assert.equal(b.revenue_usd, 0.012,
        'revenue is the sum over impressions that DID report a figure — the null row adds nothing and hides nothing');

    // The SQL must count them, not merely COALESCE them away.
    const agg = r.captured.find(c => isAggQuery(c.sql));
    assert.match(agg.sql, /COUNT\(\*\) FILTER \(WHERE revenue IS NULL\)/,
        'the view must COUNT the null-revenue impressions');

    // And the response has to SAY it, because a number whose meaning is not
    // printed gets read as the meaning the reader already assumed.
    assert.match(b.notes.null_revenue, /NOT summed as zero/i);
});

// -- 4. eCPM BELOW THE FLOOR IS NULL + low_n -------------------------------

test('eCPM is withheld below the impression floor and reported above it', async () => {
    const low = await call({ view: 'ads' }, null, responderFor({
        per_day: [], per_placement: [],
        per_network: [{ network: 'ironSource', impressions: 17, impressions_without_revenue: 0, revenue_usd: 0.19 }],
        impressions: 17, impressions_without_revenue: 0, revenue_usd: 0.19,
    }));
    assert.equal(low.out.statusCode, 200);
    assert.equal(low.out.body.ecpm_usd, null,
        '17 impressions cannot produce a trustworthy eCPM and must not produce a confident number');
    assert.equal(low.out.body.low_n, true);
    assert.equal(low.out.body.per_network[0].ecpm_usd, null, 'the per-network floor applies too');
    assert.equal(low.out.body.per_network[0].low_n, true);
    assert.ok(Number(low.out.body.ecpm_min_impressions) > 0,
        'the floor must be reported so the surface can explain WHY there is no figure');

    const floor = Number(low.out.body.ecpm_min_impressions);
    const high = await call({ view: 'ads' }, null, responderFor({
        per_day: [], per_placement: [],
        per_network: [{ network: 'ironSource', impressions: 1000, impressions_without_revenue: 0, revenue_usd: 2.5 }],
        impressions: 1000, impressions_without_revenue: 0, revenue_usd: 2.5,
    }));
    assert.ok(1000 >= floor);
    assert.equal(high.out.body.low_n, false);
    assert.equal(high.out.body.ecpm_usd, 2.5,
        '$2.50 over 1000 impressions is a $2.50 eCPM — revenue / impressions * 1000');
    assert.equal(high.out.body.per_network[0].ecpm_usd, 2.5);
});

// -- 5. NO USER INPUT IS EVER SQL TEXT -------------------------------------

test('days is bound as a PARAMETER and never reaches the SQL as text', async () => {
    const hostile = "7; DROP TABLE player_data; --";
    const r = await call({ view: 'ads', days: hostile });
    assert.equal(r.out.statusCode, 200);
    assert.ok(r.captured.length >= 1);
    for (const c of r.captured) {
        assert.ok(!c.sql.includes('DROP TABLE'),
            'hostile input must never appear in the literal SQL');
        assert.ok(!c.sql.includes(hostile));
    }
    // Non-numeric clamps to the default rather than erroring or widening.
    assert.equal(r.out.body.window_days, 7);
    for (const c of r.captured) assert.ok(c.values.includes(7), 'the window must travel as a bound value');
});

test('days clamps to the documented ceiling', async () => {
    const r = await call({ view: 'ads', days: '9999' });
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.out.body.window_days, 90, 'the window ceiling must hold against an absurd request');
});

// -- 6. SELECT-ONLY, WITH A HARD LIMIT ------------------------------------

test('every query the ads view issues is a SELECT with a hard LIMIT', async () => {
    const r = await call({ view: 'ads', days: '30' });
    assert.equal(r.out.statusCode, 200);
    assert.ok(r.captured.length >= 2, 'the view reads the impression rail and the reward rail');
    for (const c of r.captured) {
        assert.match(c.sql, /\bLIMIT\b/, 'an unbounded read on analytics_events is a foot-gun');
        assert.ok(!/\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|CREATE)\b/i.test(c.sql),
            'this endpoint is read-only by construction: ' + c.sql.slice(0, 120));
    }
    const agg = r.captured.find(c => isAggQuery(c.sql));
    assert.match(agg.sql, /event_name = 'rewarded_ad_impression'/,
        'the revenue rail is the impression event, which is the event the client actually emits');
});

// -- 7. THE COMPLETION RAIL IS ATTRIBUTED, AND THE CROSS-RAIL TRAP PRINTED -

test('completions are split by provider, and the cross-rail warning is in the response', async () => {
    const r = await call({ view: 'ads' }, null, responderFor(
        { per_day: [], per_network: [], per_placement: [], impressions: 0, impressions_without_revenue: 0, revenue_usd: 0 },
        [{ provider: 'UnityLevelPlay', outcome: 'Rewarded', completions: 6 },
         { provider: '(not reported)', outcome: 'Rewarded', completions: 3 }]
    ));
    assert.equal(r.out.statusCode, 200);
    const comp = r.captured.find(c => isCompletionQuery(c.sql));
    assert.ok(comp, 'the reward rail must be read');
    assert.match(comp.sql, /properties->>'provider'/,
        'a completion that cannot name its provider cannot be attributed to a rail');
    assert.equal(r.out.body.completions_total, 9, 'the total must add the split up');
    assert.match(r.out.body.notes.cross_rail, /NOT a completion rate/i,
        'impressions are LevelPlay-only while completions are provider-agnostic — the response must say so');
});

test('a zero-impression window reports zero WITHOUT claiming a revenue figure it cannot have', async () => {
    const r = await call({ view: 'ads' }, null, responderFor(
        { per_day: [], per_network: [], per_placement: [], impressions: 0, impressions_without_revenue: 0, revenue_usd: 0 }, []
    ));
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.out.body.read_ok, true, 'read_ok distinguishes "nothing served" from "we could not ask"');
    assert.equal(r.out.body.impressions, 0);
    assert.equal(r.out.body.ecpm_usd, null, 'no impressions means no eCPM, not an eCPM of 0');
});

// -- 8. DISCOVERABILITY ---------------------------------------------------

test('ads is named in the unknown-view hint', async () => {
    const r = await call({ view: 'not-a-view' });
    assert.equal(r.out.statusCode, 400);
    assert.match(r.out.body.error, /\bads\b/,
        'a view missing from the hint is a view nobody finds — that is how purchases stayed hidden');
    assert.match(r.out.body.error, /\bpurchases\b/,
        'purchases has been live since WO-1169 and the hint omitted it; fixed in the same edit');
});
