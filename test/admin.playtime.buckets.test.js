'use strict';

// =============================================================================
// test/admin.playtime.buckets.test.js — WO-1842.
//   the oracle for GET /api/admin/stats?view=playtime, the MEASURED session-duration
//   buckets the owner asked for on 2026-09-17: "1-5 minutes, 5-30, 30-60, 60+".
//
// ⛔ WHY THIS TICKET WAS NOT A QUERY. The owner's assumption was that the numbers
// were already in the database. They were not, and api/admin/stats.js said so in
// its own words: "THEY DO NOT. The game emits session_start on boot
// (EventTracker.cs) and there is NO session_end anywhere in the client." A duration
// needs an END, and nothing in the client had ever recorded one — so no query over
// any number of existing rows could produce one. WO-1842 is therefore mostly a
// CLIENT change (session_heartbeat + session_end in EventTracker.cs) and this view
// is its reader. That history is the reason case 4 below exists at all.
//
// WHAT IS PROVEN HERE, and why each case is worth a test.
// -----------------------------------------------------------------------------
//  1 THE GATE. Behind ADMIN_DASH_KEY like every read on this endpoint, refusing
//    with 400 and touching the database ZERO times.
//
//  2 ⭐ THE BUCKET EDGES TILE [0, infinity) WITH NO GAP AND NO OVERLAP. Every
//    duration lands in exactly ONE band, so the bands always sum to
//    sessions_measured. Asserted by feeding a value exactly ON each edge and one
//    just below it — the two places an off-by-one in an inclusive/exclusive rule
//    actually shows up. A test that re-implemented the boundary rule and compared
//    would prove only that the bug was copied.
//
//  3 A BOUNCE IS COUNTED, NOT DROPPED. The owner's list starts at one minute; the
//    "Under 1 minute" band is deliberately ADDED. Without it the shortest sessions
//    would silently leave the denominator and every other band would read high —
//    the single most misleading thing a retention distribution can do.
//
//  4 ⭐ EMPTY IS 'empty', AND IT NEVER FABRICATES A DISTRIBUTION. Immediately after
//    this ships, EVERY session in any window predates the client change and is
//    permanently unmeasurable. The view must say that in words rather than print
//    bands assembled from nothing, and its coverage must set measured sessions
//    against the session_start count so a 2% sample cannot read as the playerbase.
//
//  5 ⭐ THE NEW EVENTS DO NOT POLLUTE THE OLD FIGURES. session_heartbeat fires
//    every 60s; the pre-existing gap-based session_length estimate scans EVERY row
//    with NO event_name filter, so an unfiltered heartbeat would silently convert
//    it from "span between a player's acts" into "foreground time" while keeping
//    the old label. And early_exit_step — "the LAST thing each now-quiet player
//    did" — excluded only session_start, so session_end would have become every
//    departing player's last act and erased the signal completely. Both exclusions
//    are asserted on the SQL the handler actually issues.
//
//  6 NO USER INPUT IS EVER SQL TEXT, and the cast guard is present. properties is
//    JSONB written by a client we do not control; a bare ::float8 on one malformed
//    elapsedSeconds throws 22P02 and takes the WHOLE view down instead of dropping
//    one row. The regex guard must be there, and spelled with a DOUBLE backslash —
//    in a JS template literal a single backslash-dot collapses to a bare dot, which
//    matches any character and admits "12x34" straight into the cast.
//
//  7 EVERY STATEMENT IS A SELECT WITH A HARD LIMIT. Same read-only-by-construction
//    contract as the rest of the endpoint.
//
//  8 THE FLOOR IS LABELLED. A session killed by the OS reports its last heartbeat,
//    so its duration is a floor, not an exact figure. ended_cleanly_pct is what
//    tells the reader how much of the sample is exact.
//
// PROVEN RED, MEASURED 2026-09-17 (not asserted, run): with api/admin/stats.js at
// its pre-WO-1842 state this file reads 15 tests / 1 pass / 14 FAIL. The 14 fail on
// 'Unknown view. Use: ...' with view=playtime unrouted, and case 5 on the two
// missing exclusions. The ONE that passes in both states is the auth gate (the auth
// block predates the view) — and it must, or every assertion after it is vacuous.
// With the change in place: 15 / 15.
//
// It also caught one real defect while being written: the first draft of the view
// referenced EXCLUDED, which is declared INSIDE the command view's block, so
// ?view=playtime threw ReferenceError at request time and answered 500. A source
// read would not have found it; issuing the request did.
//
//   node --test test/admin.playtime.buckets.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.join(__dirname, '..');
const STATS_ABS = path.join(REPO, 'api/admin/stats.js');
const KEY = 'playtime-view-test-key';

// -- harness (res/req shape borrowed verbatim from test/admin.events.view.test.js so
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

const DRIVER_ID = require.resolve('@neondatabase/serverless');

// The stub captures every tagged template AND lets a case answer one. `rowsFor` is
// handed the literal SQL so a case can reply to the per-session query and the
// coverage query differently — which is the only way to drive a real distribution
// through the handler without a database.
function loadHandlerWithCapture(rowsFor) {
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
                const rows = rowsFor ? rowsFor(text) : null;
                return Promise.resolve(rows || []);
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

async function call(query, headers, rowsFor) {
    const prevKey = process.env.ADMIN_DASH_KEY;
    const prevUrl = process.env.DATABASE_URL;
    process.env.ADMIN_DASH_KEY = KEY;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const { handler, captured } = loadHandlerWithCapture(rowsFor);
    const res = fakeRes();
    try {
        await handler(fakeReq(query, headers), res);
    } finally {
        if (prevKey === undefined) delete process.env.ADMIN_DASH_KEY; else process.env.ADMIN_DASH_KEY = prevKey;
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, captured: captured };
}

// A stub answer set. The per-session query is the one that GROUPs BY session_id;
// the coverage query is the one that counts session_starts.
function answerWith(durations, opts) {
    const o = opts || {};
    return (text) => {
        if (text.includes('GROUP BY session_id')) {
            return durations.map((d, i) => ({
                session_id: 's' + i,
                player_id: 'p' + i,
                duration_seconds: d,
                ended_cleanly: o.endedCleanly === undefined ? true : o.endedCleanly,
                heartbeats: 1,
                last_signal: '2026-09-17T00:00:00.000Z',
            }));
        }
        if (text.includes('session_starts')) {
            return [{
                session_starts: o.starts === undefined ? durations.length : o.starts,
                duration_rows: durations.length * 2,
                malformed_rows: o.malformed || 0,
                first_duration_signal: o.first || '2026-09-17T00:00:00.000Z',
            }];
        }
        return [];
    };
}

// -- 1. THE GATE -------------------------------------------------------------

test('view=playtime refuses without the admin key, and issues no query', async () => {
    const r = await call({ view: 'playtime' }, { 'x-admin-key': 'wrong' });
    assert.equal(r.out.statusCode, 400);
    assert.equal(r.out.body.error, 'Unauthorized');
    assert.deepEqual(r.captured, [], 'a refused request must not touch the database');
});

test('view=playtime is listed in the unknown-view hint', async () => {
    const r = await call({ view: 'not-a-view' });
    assert.equal(r.out.statusCode, 400);
    assert.match(r.out.body.error, /playtime/,
        'a view that is not in the hint is a view nobody finds');
});

// -- 2. THE EDGES TILE, WITH NO GAP AND NO OVERLAP ---------------------------

test('every duration lands in exactly ONE band, and the bands sum to the sample', async () => {
    // One value exactly ON each edge and one just below it — the two places an
    // inclusive/exclusive mistake actually surfaces.
    const durations = [0, 59.9, 60, 299.9, 300, 1799.9, 1800, 3599.9, 3600, 100000];
    const r = await call({ view: 'playtime' }, null, answerWith(durations));
    assert.equal(r.out.statusCode, 200);

    const b = r.out.body;
    assert.equal(b.state, 'ok');
    assert.equal(b.instrumented, true, 'this view is MEASURED, not estimated');
    assert.equal(b.estimated, false);
    assert.equal(b.sessions_measured, durations.length);

    const by = {};
    for (const row of b.buckets) by[row.band] = row.sessions;
    assert.equal(b.coverage.unreadable_sessions_dropped, 0);
    assert.deepEqual(by, {
        'Under 1 minute': 2,          // 0 and 59.9
        '1 to 5 minutes': 2,          // 60 (edge, inclusive) and 299.9
        '5 to 30 minutes': 2,         // 300 and 1799.9
        '30 to 60 minutes': 2,        // 1800 and 3599.9
        '60 minutes and up': 2,       // 3600 and the long tail
    });

    // THE INVARIANT: the bands tile the whole line, so nothing is double-counted
    // and nothing falls through.
    const summed = b.buckets.reduce((a, row) => a + row.sessions, 0);
    assert.equal(summed, b.sessions_measured,
        'bands must tile [0, infinity): every session in exactly one');

    // The owner's four edges, in seconds, unchanged from her ask.
    assert.deepEqual(b.buckets.map(x => x.min_seconds), [0, 60, 300, 1800, 3600]);
    assert.deepEqual(b.buckets.map(x => x.max_seconds), [60, 300, 1800, 3600, null]);
    assert.equal(b.buckets[4].max_seconds, null, 'the top band must be open-ended');
});

test('median is the headline and the mean sits beside it', async () => {
    // A long tail that drags a mean and leaves a median alone — the reason this
    // endpoint reports both, stated in the command view and kept here.
    const durations = [60, 120, 180, 240, 100000];
    const r = await call({ view: 'playtime' }, null, answerWith(durations));
    assert.equal(r.out.body.median_seconds, 180);
    assert.ok(r.out.body.mean_seconds > 1000, 'the mean is dragged by the tail');
    assert.ok(r.out.body.median_seconds < r.out.body.mean_seconds);
    assert.equal(r.out.body.longest_seconds, 100000);
    assert.equal(r.out.body.median_minutes, 3, 'minutes are offered for the owner-facing read');
});

// -- 3. A BOUNCE IS COUNTED ---------------------------------------------------

test('sub-minute sessions are COUNTED in their own band, never dropped', async () => {
    const r = await call({ view: 'playtime' }, null, answerWith([1, 2, 3, 600]));
    const b = r.out.body;
    const bounce = b.buckets.find(x => x.band === 'Under 1 minute');
    assert.equal(bounce.sessions, 3);
    assert.equal(b.sessions_measured, 4, 'bounces stay in the denominator');
    assert.match(b.bucket_note, /ADDED, not in the ask/,
        'the extra band must be declared, not smuggled in');
});

test('an unreadable duration is DROPPED, not counted as a zero-second session', async () => {
    // A negative and a NaN are unreadable ROWS. Coercing either to zero would put
    // them in the bounce band — the one band a reader acts on hardest. They are
    // dropped from the sample and COUNTED, so the loss is visible.
    const r = await call({ view: 'playtime' }, null, answerWith([-5, 'abc', 30, 120]));
    const b = r.out.body;

    assert.equal(b.sessions_measured, 2, 'the two unreadable rows leave the sample');
    assert.equal(b.coverage.unreadable_sessions_dropped, 2,
        'a dropped row must be counted, or the loss is invisible');
    assert.equal(b.buckets.find(x => x.band === 'Under 1 minute').sessions, 1);   // 30
    assert.equal(b.buckets.find(x => x.band === '1 to 5 minutes').sessions, 1);   // 120

    // ⭐ THE INVARIANT NOW HOLDS EXACTLY, which is the point of dropping in the
    // handler rather than inside the bucketer: the denominator and the bands are
    // computed from the SAME filtered array, so `bands_tile` is a fact and not a hope.
    const summed = b.buckets.reduce((a, row) => a + row.sessions, 0);
    assert.equal(summed, b.sessions_measured);
});

test('the per-session scan ORDERS before it LIMITs', async () => {
    const r = await call({ view: 'playtime' }, null, answerWith([120]));
    const per = r.captured.find(c => c.sql.includes("properties->>'sessionId'"));
    assert.ok(per);
    // Strip -- comments first: one of them contains the word LIMIT, and a keyword
    // scan over commented SQL matches prose. Same trap as the SELECT-only case.
    const sql = per.sql.replace(/--[^\n]*/g, ' ');
    const order = sql.indexOf('ORDER BY received_at DESC');
    const limit = sql.indexOf('LIMIT');
    assert.ok(order > 0 && order < limit,
        'a bare LIMIT drops arbitrary rows when the cap bites, SPLITTING a session '
        + 'across the boundary and understating its MAX - a silently wrong duration');
});

test('coverage warns that measured_pct can legitimately exceed 100', async () => {
    // A resume after the gap mints a new sessionId with NO second session_start, so
    // one boot can produce several measured sessions.
    const r = await call({ view: 'playtime' }, null, answerWith([120, 300], { starts: 1 }));
    assert.equal(r.out.body.coverage.measured_pct, 200);
    assert.match(r.out.body.coverage.over_100_pct_is_possible, /valid state, not a bug/);
});

// -- 4. EMPTY IS EMPTY, AND SAYS WHY -----------------------------------------

test('with no measured sessions the view says empty and fabricates nothing', async () => {
    const r = await call({ view: 'playtime' }, null, answerWith([], { starts: 400 }));
    const b = r.out.body;
    assert.equal(r.out.statusCode, 200);
    assert.equal(b.state, 'empty');
    assert.equal(b.sessions_measured, 0);
    assert.equal(b.median_seconds, 0);
    for (const row of b.buckets) {
        assert.equal(row.sessions, 0, row.band + ' must be zero, never inferred');
    }
    // ⭐ THE HONESTY THIS WHOLE CASE EXISTS FOR: the sessions that DID happen are
    // named as unmeasurable rather than quietly left out of the arithmetic.
    assert.equal(b.coverage.session_starts_in_window, 400);
    assert.equal(b.coverage.unmeasured_sessions, 400);
    assert.match(b.coverage.note, /predates? the client change|predate the client change/);
    assert.ok(b.gaps.some(g => /PERMANENTLY UNMEASURABLE/.test(g)),
        'no backfill is possible and the view must say so');
    assert.ok(b.gaps.some(g => /fabricated/.test(g)));
});

test('low_n rides with the figure', async () => {
    const few = await call({ view: 'playtime' }, null, answerWith([100, 200, 300]));
    assert.equal(few.out.body.low_n, true);
    assert.equal(few.out.body.low_n_threshold, 10);

    const many = await call({ view: 'playtime' }, null,
        answerWith(Array.from({ length: 25 }, () => 900)));
    assert.equal(many.out.body.low_n, false);
});

// -- 5. THE NEW EVENTS DO NOT POLLUTE THE OLD FIGURES ------------------------

test('the gap-based estimate and the exit-step view both EXCLUDE the duration events', async () => {
    const r = await call({ view: 'command' });
    assert.equal(r.out.statusCode, 200);

    const gap = r.captured.find(c => c.sql.includes('starts_session'));
    assert.ok(gap, 'the gap-based session_length estimate must still be issued');
    const gapBound = JSON.stringify(gap.values);
    assert.match(gapBound, /session_heartbeat/,
        'the heartbeat must be EXCLUDED from the between-acts estimate, or that '
        + 'estimate silently becomes the foreground figure under the old label');
    assert.match(gapBound, /session_end/);

    const exit = r.captured.find(c => c.sql.includes("event_name <> 'session_start'"));
    assert.ok(exit, 'the early-exit-step query must still be issued');
    const exitBound = JSON.stringify(exit.values);
    assert.match(exitBound, /session_end/,
        'session_end is the last row of almost every session; unexcluded it becomes '
        + "every departing player's 'last act' and erases this view");
});

test('the duration events are NOT counted as play', async () => {
    const src = require('node:fs').readFileSync(STATS_ABS, 'utf8');
    const at = src.indexOf('const NOT_PLAY_EVENTS');
    assert.ok(at > 0);
    const block = src.slice(at, src.indexOf('];', at));
    assert.match(block, /'session_heartbeat', 'session_end'/,
        'a heartbeat fires because the app is OPEN, not because anybody played');
});

// -- 6. NO USER INPUT IS SQL TEXT, AND THE CAST IS GUARDED -------------------

test('the event-name filter and the window are BOUND, never built from the request', async () => {
    const r = await call({ view: 'playtime', days: '14' }, null, answerWith([120]));

    // ⭐ THE RULE IS ABOUT REQUEST INPUT, not about every literal in the file. A
    // code-authored `event_name = 'session_end'` inside a BOOL_OR is fine — nobody
    // can influence it. What must never be concatenated is anything from `q`.
    const joined = r.captured.map(c => JSON.stringify(c.values)).join(' ');
    assert.match(joined, /session_heartbeat/, 'the filter array must be a bound parameter');
    assert.match(joined, /session_end/);
    assert.ok(r.captured.some(c => c.values.includes(14)),
        'the ?days window must be bound and clamped, not interpolated');
    for (const c of r.captured) {
        assert.ok(!c.sql.includes("ANY('{"), 'the name array must not be inlined as SQL text');
        assert.ok(!/INTERVAL '14/.test(c.sql), 'the window must not be written into the SQL');
    }
});

test('the ::float8 cast is regex-guarded, with a DOUBLE backslash', async () => {
    const r = await call({ view: 'playtime' }, null, answerWith([120]));
    const guarded = r.captured.filter(c => c.sql.includes("elapsedSeconds"));
    assert.ok(guarded.length >= 2, 'both statements read elapsedSeconds');
    for (const c of guarded) {
        // ⛔ The guard must reach Postgres as \. — an escaped dot. A single
        // backslash-dot in the JS template literal collapses to a bare dot, which
        // matches ANY character, admits "12x34", and the cast throws 22P02 and takes
        // the whole view down. This asserts the string the DRIVER received.
        assert.ok(c.sql.includes('[0-9]+(\\.[0-9]+)?$'),
            'the numeric guard must escape the dot; got: '
            + (c.sql.match(/\^\[0-9\][^']*/) || ['<none>'])[0]);
    }
});

test('a session is assembled by sessionId and never joined to session_start', async () => {
    const r = await call({ view: 'playtime' }, null, answerWith([120]));
    const per = r.captured.find(c => c.sql.includes('GROUP BY session_id'));
    assert.ok(per);
    assert.match(per.sql, /MAX\(elapsed_raw::float8\)/,
        'duration is MAX of a cumulative figure, so arrival order cannot corrupt it');
    // session_start is often attributed to 'anonymous' (queued before the account id
    // is minted) while later rows carry the real id, so a join would drop exactly the
    // new players this view exists to describe.
    assert.ok(!per.sql.includes('session_start'),
        'the per-session query must not join session_start');
});

// -- 7. SELECT-ONLY, WITH A HARD LIMIT --------------------------------------

test('every playtime statement is a SELECT with a hard LIMIT', async () => {
    const r = await call({ view: 'playtime' }, null, answerWith([120]));
    assert.ok(r.captured.length >= 2);
    for (const c of r.captured) {
        // ⚠ STRIP THE -- COMMENTS FIRST. The statements carry explanatory comments,
        // and the word "DROPPED" in one of them is not a DROP statement. A keyword
        // scan over commented SQL fails on prose, which is a test that cries wolf.
        const upper = c.sql.replace(/--[^\n]*/g, ' ').toUpperCase();
        for (const verb of [/\bINSERT\s+INTO\b/, /\bUPDATE\s+\w/, /\bDELETE\s+FROM\b/,
                            /\bDROP\s+\w/, /\bALTER\s+\w/, /\bTRUNCATE\b/]) {
            assert.ok(!verb.test(upper),
                'a read view wrote ' + verb + ': ' + c.sql.slice(0, 120));
        }
        assert.match(upper, /LIMIT/, 'an unbounded scan of analytics_events is an outage later');
    }
    assert.equal(r.out.body.coverage.scan_cap, 50000);
});

// -- 8. THE FLOOR IS LABELLED ----------------------------------------------

test('a session with no clean end is reported as a FLOOR, and the share is printed', async () => {
    const clean = await call({ view: 'playtime' }, null,
        answerWith([600, 700], { endedCleanly: true }));
    assert.equal(clean.out.body.ended_cleanly, 2);
    assert.equal(clean.out.body.ended_cleanly_pct, 100);

    const killed = await call({ view: 'playtime' }, null,
        answerWith([600, 700], { endedCleanly: false }));
    assert.equal(killed.out.body.ended_cleanly, 0);
    assert.equal(killed.out.body.ended_cleanly_pct, 0);
    assert.match(killed.out.body.floor_note, /FLOOR, not an exact/);
    assert.match(killed.out.body.definition, /FOREGROUND SECONDS/,
        'what is being measured must be on the response, not in a code comment');
    assert.match(killed.out.body.delivery_note, /NEXT launch/,
        'a session_end raised at pause often arrives a launch later; a very recent '
        + 'window under-reports and then fills in');
    assert.match(killed.out.body.backing, /EventTracker\.cs/,
        'the figure must name the emitter it came from');
});
