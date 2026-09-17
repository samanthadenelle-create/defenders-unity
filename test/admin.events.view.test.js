'use strict';

// =============================================================================
// test/admin.events.view.test.js — WO-1793.
//   the oracle for GET /api/admin/db?view=events and ?view=funnel.
//
// THE GAP THESE VIEWS CLOSE, measured 2026-09-16: 424 `playtest_break` rows and
// 7 `save_reset_accepted` rows landed in analytics_events and NO view in the
// product could name one of them. `metrics` returns counts only; `traces` is
// hardcoded to event_name = 'web_trace', which only POSTs under UNITY_WEBGL and
// is therefore EMPTY for the Android platform the game ships on; `authrejects`
// reads exactly three event names. The triage answered it by connecting to Neon
// with DATABASE_URL by hand — a deviation, and one the owner cannot perform from
// a phone at all. Same class as WO-1742/1745 one table over: a view that does
// not contain the event name reports ZERO and reads as a measurement.
//
// WHAT IS PROVEN HERE, and why each case exists.
// -----------------------------------------------------------------------------
//  1 THE GATE. Both views sit behind ADMIN_DASH_KEY like every other read on
//    this endpoint, and the refusal status is 400 (this project's API answers
//    200 | 400 | 500 only).
//
//  2 `name` IS REQUIRED AND ITS REFUSAL COSTS NO QUERY. A view that scans the
//    whole events table when a parameter is forgotten is a foot-gun on a table
//    with six figures of rows.
//
//  3 ⭐ NO USER INPUT IS EVER SQL TEXT. The driver is STUBBED so every tagged
//    template this endpoint issues is captured — the literal strings separately
//    from the bound values. The assertion is that the hostile strings appear in
//    the VALUES and never in the literal SQL, for every group mode. A source-text
//    lint could only see that the code "looks parameterized"; this executes the
//    query builder and reads what it actually produced.
//
//  4 THE GROUP WHITELIST FALLS BACK, IT DOES NOT ERROR OR INTERPOLATE. An
//    unknown group is answered as `rows` and the response SAYS which group was
//    requested, so a typo is visible rather than silently answering a different
//    question.
//
//  5 EVERY QUERY IS A SELECT WITH A HARD LIMIT, and the bounds clamp — the
//    read-only-by-construction contract stated at the top of api/admin/db.js.
//
//  6 THE AMBIGUITY IS PRINTED, NOT IMPLIED. BreakCaptureHarness.Record builds
//    {kind,message,stack,scene,t,utc} with NO build string (only session_start
//    carries appVersion), so build attribution is a per-player JOIN and is
//    ambiguous for a player who booted two builds in the window. The response
//    legend must say so — a number whose ambiguity is not printed is read as
//    certain, which is the whole reason the purchases view spells out verified
//    vs fulfilled.
//
//  7 THE NEW VIEWS ARE IN THE UNKNOWN-VIEW HINT. A view nobody can discover is
//    a view nobody uses, which is how this gap survived eight views.
//
// PROVEN RED: with api/admin/db.js at its pre-WO-1793 state, 11 of these 13 cases
// FAIL on 'Unknown view. Use: overview | ...' with zero captured queries. The two
// that still pass are the auth gate (which predates the views) and the harness
// self-test (which asserts about itself, and must pass in both states or every
// injection assertion above it is vacuous).
//
//   node --test test/admin.events.view.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.join(__dirname, '..');
const DB_REL = 'api/admin/db.js';
const DB_ABS = path.join(REPO, DB_REL);
const KEY = 'events-view-test-key';

// -- harness (res/req shape borrowed verbatim from test/admin.skus.view.test.js so
//    the admin endpoints are exercised ONE way, not two) ------------------------

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
// The endpoint calls neon(DATABASE_URL) and uses the result as a tagged template.
// Replacing the module in require.cache captures every query WITHOUT a database:
// `strings` is the literal SQL the code wrote, `values` the bound parameters.
// Nothing can connect, so a query that slipped past the guards would be visible
// here rather than silently succeeding against a real table.
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
    delete require.cache[require.resolve(DB_ABS)];
    const handler = require(DB_ABS);
    // Restore immediately — the handler holds the stub through its closure, and a
    // leaked stub would poison every later test file in the same process.
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(DB_ABS)];
    return { handler: handler, captured: captured };
}

async function call(query, headers) {
    const prevKey = process.env.ADMIN_DASH_KEY;
    const prevUrl = process.env.DATABASE_URL;
    process.env.ADMIN_DASH_KEY = KEY;
    // A syntactically valid URL the stub never dials. Real enough that neon()
    // constructs; unreachable enough that a real query could not succeed.
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

// -- 1. THE GATE -------------------------------------------------------------

test('events + funnel refuse without the admin key, and issue no query', async () => {
    for (const query of [{ view: 'events', name: 'playtest_break' }, { view: 'funnel' }]) {
        const r = await call(query, { 'x-admin-key': 'wrong' });
        assert.equal(r.out.statusCode, 400, JSON.stringify(query));
        assert.equal(r.out.body.error, 'Unauthorized');
        assert.deepEqual(r.captured, [], 'a refused request must not touch the database');
    }
});

// -- 2. name IS REQUIRED, AND THE REFUSAL COSTS NO SCAN ----------------------

test('view=events without ?name refuses with 400 and runs ZERO queries', async () => {
    const r = await call({ view: 'events' });
    assert.equal(r.out.statusCode, 400);
    assert.match(r.out.body.error, /requires \?name=/);
    assert.deepEqual(r.captured, [],
        'a forgotten name must not become an unfiltered scan of analytics_events');
});

// -- 3. NO USER INPUT IS EVER SQL TEXT --------------------------------------

const HOSTILE_NAME = "playtest_break'; DROP TABLE player_data; --";
const HOSTILE_PLAYER = "abc' OR '1'='1";

test('every group mode binds name/player as PARAMETERS, never as SQL text', async () => {
    for (const group of ['rows', 'message', 'kind', 'player', 'not-a-group']) {
        const r = await call({
            view: 'events', name: HOSTILE_NAME, player: HOSTILE_PLAYER,
            group: group, since_hours: '48', limit: '10',
        });
        assert.equal(r.out.statusCode, 200, 'group=' + group);
        assert.ok(r.captured.length >= 2,
            'group=' + group + ' should issue the window total plus the group query');

        for (const c of r.captured) {
            // The literal SQL the code wrote must contain NEITHER hostile string...
            assert.ok(!c.sql.includes('DROP TABLE'),
                'group=' + group + ': event name reached the SQL text');
            assert.ok(!c.sql.includes("OR '1'='1"),
                'group=' + group + ': player id reached the SQL text');
            // ...nor the group word itself, which selects between literal queries
            // rather than being built into one.
            assert.ok(!c.sql.includes('not-a-group'),
                'group=' + group + ': the group word reached the SQL text');
        }
        // ...and both hostile strings must be present as BOUND VALUES, so the
        // filter is genuinely applied rather than dropped.
        const allValues = r.captured.reduce((a, c) => a.concat(c.values), []);
        assert.ok(allValues.includes(HOSTILE_NAME), 'group=' + group + ': name was not bound');
        assert.ok(allValues.includes(HOSTILE_PLAYER), 'group=' + group + ': player was not bound');
    }
});

test('the capture harness can actually SEE an interpolation -- proven both ways', () => {
    // If this ever stops holding, every assertion above is vacuous: it proves the
    // captured `sql` string WOULD contain an interpolated value.
    const captured = [];
    const sql = (strings, ...values) => { captured.push({ sql: Array.from(strings).join(' ? '), values }); };
    const evil = "x'; DROP TABLE player_data; --";
    sql`SELECT 1 FROM t WHERE a = ${evil}`;                  // parameterized
    // eslint-disable-next-line no-useless-concat
    sql([`SELECT 1 FROM t WHERE a = '` + evil + `'`]);        // interpolated
    assert.ok(!captured[0].sql.includes('DROP TABLE'));
    assert.deepEqual(captured[0].values, [evil]);
    assert.ok(captured[1].sql.includes('DROP TABLE'));
});

test('funnel binds its window and takes no name/player input at all', async () => {
    const r = await call({ view: 'funnel', since_hours: '72' });
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.captured.length, 1, 'the funnel is ONE query by design');
    assert.equal(r.out.body.window_hours, 72);
    assert.ok(r.captured[0].values.includes(72) || r.captured[0].values.includes('72'),
        'the window must be bound, not interpolated');
});

// -- 4. THE GROUP WHITELIST FALLS BACK, VISIBLY ------------------------------

test('an unknown group answers as rows AND says which group was asked for', async () => {
    const r = await call({ view: 'events', name: 'playtest_break', group: 'DROP TABLE' });
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.out.body.group, 'rows');
    assert.equal(r.out.body.requested_group, 'drop table',
        'a typo must be visible in the response, not silently answered as something else');
});

test('a recognised group is reported as itself, with no requested_group noise', async () => {
    for (const group of ['rows', 'message', 'kind', 'player']) {
        const r = await call({ view: 'events', name: 'playtest_break', group: group.toUpperCase() });
        assert.equal(r.out.body.group, group, 'group is case-insensitive');
        assert.equal(r.out.body.requested_group, null);
    }
});

// -- 5. SELECT-ONLY, HARD LIMIT, CLAMPED BOUNDS -----------------------------

test('every query these views issue is a SELECT with a hard LIMIT', async () => {
    const queries = [];
    for (const group of ['rows', 'message', 'kind', 'player']) {
        const r = await call({ view: 'events', name: 'playtest_break', group: group });
        queries.push(...r.captured);
    }
    queries.push(...(await call({ view: 'funnel' })).captured);
    assert.ok(queries.length >= 9);
    for (const c of queries) {
        const text = c.sql.replace(/--[^\n]*/g, '');
        assert.match(text, /^\s*SELECT\b/, 'not a SELECT: ' + text.slice(0, 80));
        assert.ok(!/\b(INSERT\s+INTO|UPDATE\s+[a-z_]+\s+SET|DELETE\s+FROM|TRUNCATE)\b/.test(text),
            'a write verb appeared: ' + text.slice(0, 80));
        // The window-total aggregate is the one query that needs no LIMIT (it
        // returns exactly one row by construction); every row-returning query
        // must carry one.
        if (!/^\s*SELECT\s+COUNT\(\*\)::bigint AS hits/.test(text)) {
            assert.match(text, /LIMIT/, 'no LIMIT: ' + text.slice(0, 80));
        }
    }
});

test('since_hours and limit clamp at both ends', async () => {
    const huge = await call({ view: 'events', name: 'x', since_hours: '99999', limit: '99999' });
    assert.equal(huge.out.body.window_hours, 168, 'since_hours ceiling is 7 days');
    assert.equal(huge.out.body.limit, 200, 'limit ceiling is 200 rows');

    const junk = await call({ view: 'events', name: 'x', since_hours: 'abc', limit: '-5' });
    assert.equal(junk.out.body.window_hours, 24, 'garbage since_hours falls back to the default');
    assert.equal(junk.out.body.limit, 50, 'garbage limit falls back to the default');

    const funnel = await call({ view: 'funnel', limit: '99999' });
    assert.equal(funnel.out.body.limit, 200, 'the funnel is capped at 200 ids');
});

test('no player filter means the bound value is NULL, not an empty string', async () => {
    const r = await call({ view: 'events', name: 'playtest_break' });
    assert.equal(r.out.body.player, null);
    const allValues = r.captured.reduce((a, c) => a.concat(c.values), []);
    assert.ok(allValues.includes(null),
        'the no-filter case must bind NULL — an empty string would match nothing and read as zero rows');
    assert.ok(!allValues.includes(''), 'an empty string must never be bound as a player id');
});

// -- 6. THE AMBIGUITY IS PRINTED -------------------------------------------

test('the events legend STATES that no break payload carries a build', async () => {
    const r = await call({ view: 'events', name: 'playtest_break', group: 'player' });
    const legend = r.out.body.legend || {};
    assert.ok(legend.app_version, 'the response must carry an app_version legend');
    assert.match(legend.app_version, /session_start/,
        'the legend must name where appVersion DOES live');
    assert.match(legend.app_version, /AMBIGUOUS/,
        'the legend must say the per-player JOIN is ambiguous for a two-build window');
    assert.ok(legend.message && legend.props && legend.group);
});

test('the events rows view truncates message and stack, and reports the real stack length', () => {
    const src = fs.readFileSync(DB_ABS, 'utf8');
    // A stack is unbounded and a poll must never return one in full.
    assert.match(src, /left\(properties->>'message', 400\)/);
    assert.match(src, /left\(properties->>'stack', 400\)/);
    assert.match(src, /length\(properties->>'stack'\)\s+AS stack_len/);
    // ...and `props` is the payload MINUS those two, which is what makes the view
    // generic: save_reset_accepted's from/to/ref/mode answer through the same query.
    assert.match(src, /\(properties - 'message' - 'stack'\) AS props/);
});

// -- 7. DISCOVERABILITY ----------------------------------------------------

test('the unknown-view hint lists every view the file actually serves', async () => {
    const r = await call({ view: 'no-such-view' });
    assert.equal(r.out.statusCode, 400);
    const hint = r.out.body.error;
    for (const v of ['overview', 'players', 'metrics', 'traces', 'bugreports', 'bugreport',
                     'authrejects', 'purchases', 'events', 'funnel']) {
        assert.ok(hint.includes(v), 'the hint omits view=' + v + ': ' + hint);
    }
    // And the reverse direction: a view served but unlisted is undiscoverable.
    const src = fs.readFileSync(DB_ABS, 'utf8');
    const served = Array.from(src.matchAll(/if \(view === '([a-z]+)'\) \{/g)).map((m) => m[1]);
    for (const v of served) {
        assert.ok(hint.includes(v), 'view=' + v + ' is served but missing from the hint');
    }
});
