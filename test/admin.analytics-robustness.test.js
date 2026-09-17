'use strict';

// =============================================================================
// test/admin.analytics-robustness.test.js — WO-1843.
//   the oracle for the four analytics additions on api/admin/stats.js:
//     ?view=active        DAU / WAU / MAU + stickiness
//     ?view=monetization  payer rate / ARPU / ARPDAU / ARPPU
//     ?view=stability     error / exception / softlock RATES, not counts
//     &app_version= / &platform=  slicing on retention, funnel and purchases
//
// THE GAP THESE CLOSE, measured read-only against the live database 2026-09-17
// (the numbers every case below is built from, so the oracle and the proof are
// the same figures):
//   DAU 57 / WAU 74 / MAU 137 — no view computed any of them; `active_players`
//     existed only as a column inside the overview per-day table.
//   4 settled entitlements, 2 payer wallets, $10.97 usd_anchor — revenue was
//     summed per day with NO denominator anywhere, so ARPU/ARPPU did not exist.
//   33,198 kind=error rows vs 32 kind=exception rows over 211 player-days — the
//     events view reported the raw counts and never divided by traffic, so
//     "is stability improving" was unanswerable.
//   0 rows carrying a `locale` key — which is why locale is REFUSED, not filtered.
//
// WHAT IS PROVEN HERE, and why each case exists.
// -----------------------------------------------------------------------------
//  1 THE GATE. All three new views sit behind ADMIN_DASH_KEY like every other
//    read on this endpoint, refuse with 400, and issue ZERO queries when refused.
//
//  2 ⭐ NO USER INPUT IS EVER SQL TEXT. The driver is STUBBED, so every tagged
//    template is captured — literal SQL separately from bound values. A hostile
//    app_version/platform must appear in the VALUES of every sliced view and
//    NEVER in the SQL text. A source lint could only see that the code "looks
//    parameterized"; this executes the query builder and reads what it produced.
//
//  3 THE ARITHMETIC IS PINNED ON THE REAL NUMBERS. The stub answers with the
//    live rows above, so each ratio is asserted against the value the live
//    database actually produces (41.6% stickiness, $0.0801 ARPU, 108.1% error
//    player-day rate). A metric view whose arithmetic is untested is a
//    plausible-looking number generator.
//
//  4 ⛔ EVERY MISLEADING-SAMPLE NUMBER CARRIES ITS FLAG. 2 payers cannot support
//    an ARPU, so `reportable` is false and the caveat says "INSUFFICIENT PAYER
//    VOLUME" in those words. Proven BOTH ways: the same view flips to reportable
//    once the payer count clears LOW_N_THRESHOLD, so the flag is a measurement
//    and not a constant.
//
//  5 ⛔ ARPDAU DIVIDES BY USER-DAYS, NOT BY DAU. Dividing a 30-day revenue sum by
//    one day's players overstates it by roughly the length of the window. The
//    denominator is asserted to be the user-days figure and NOT the DAU figure.
//
//  6 ⛔ DEVNET IS NOT REVENUE. usd_real excludes network=devnet and every ratio
//    is computed on usd_real, so test money can never be reported as income.
//
//  7 ⛔ THE BREAK KINDS ARE NEVER SUMMED INTO ONE "crash" NUMBER, and a rate over
//    an incomplete denominator SAYS SO instead of being clamped. On live data
//    kind=error affects MORE player-days (228) than carry a session_start (211);
//    the response returns 108.1% with denominator_incomplete=true rather than a
//    tidy 100%. A clamped number would have hidden a real data gap.
//
//  8 ⛔ CRASH-FREE IS NOT CLAIMED. There is no crash reporter in this game, so the
//    headline is named a PROXY and the caveat says the true rate is unmeasurable.
//
//  9 ⛔ LOCALE IS REFUSED, NOT ANSWERED WITH ZEROS. Nothing emits a locale, so a
//    locale filter would match nothing and read as "no players in that locale" —
//    a fabricated finding. 400, zero queries, and the reason named.
//
// 10 A SLICE THAT MATCHES NOBODY IS NOT A SLICE THAT MATCHES EVERYBODY. The
//    resolved cohort binds an EMPTY array, which filters everything out, rather
//    than degrading to no filter.
//
// 11 THE NEW VIEWS ARE IN THE UNKNOWN-VIEW HINT, and every statement is a SELECT
//    with a hard LIMIT — the read-only-by-construction contract of the file.
//
//   node --test test/admin.analytics-robustness.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.join(__dirname, '..');
const STATS_REL = 'api/admin/stats.js';
const STATS_ABS = path.join(REPO, STATS_REL);
const KEY = 'analytics-robustness-test-key';

// The live figures, in one place so every assertion below traces to the same read.
const LIVE = {
    dau: 57, wau: 74, mau: 137,
    user_days: 211, session_starts: 785, players: 137,
    payers: 2, settled: 4, usd_all: 10.97, usd_real: 10.97,
    error_player_days: 228, exception_player_days: 9,
};

// -- harness (res/req shape borrowed verbatim from test/admin.events.view.test.js
//    so the admin endpoints are exercised ONE way, not two) ---------------------

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

// `respond` receives the literal SQL and the bound values and returns the rows for
// that query, so the arithmetic can be driven with the live figures WITHOUT a
// database. Default: every query answers empty, which also proves the views
// survive a window with no data at all.
function loadHandlerWithCapture(respond) {
    const captured = [];
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = {
        neon() {
            return (strings, ...values) => {
                const text = Array.from(strings).join(' ? ');
                captured.push({ sql: text, values: values });
                let rows = [];
                try { rows = (respond ? respond(text, values) : null) || []; }
                catch (err) { return Promise.reject(err); }
                return Promise.resolve(rows);
            };
        },
    };
    delete require.cache[require.resolve(STATS_ABS)];
    const handler = require(STATS_ABS);
    // Restore immediately — a leaked stub would poison every later test file in
    // the same process.
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(STATS_ABS)];
    return { handler: handler, captured: captured };
}

async function call(query, headers, respond) {
    const prevKey = process.env.ADMIN_DASH_KEY;
    const prevUrl = process.env.DATABASE_URL;
    process.env.ADMIN_DASH_KEY = KEY;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const { handler, captured } = loadHandlerWithCapture(respond);
    const res = fakeRes();
    try {
        await handler(fakeReq(query, headers), res);
    } finally {
        if (prevKey === undefined) delete process.env.ADMIN_DASH_KEY; else process.env.ADMIN_DASH_KEY = prevKey;
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, captured: captured };
}

const readSrc = () => fs.readFileSync(STATS_ABS, 'utf8');

// =============================================================================
// 1. THE GATE
// =============================================================================

test('the three new views refuse without the admin key, and issue no query', async () => {
    for (const view of ['active', 'monetization', 'stability']) {
        const r = await call({ view: view }, { 'x-admin-key': 'wrong' });
        assert.equal(r.out.statusCode, 400, view);
        assert.equal(r.out.body.error, 'Unauthorized', view);
        assert.deepEqual(r.captured, [], view + ': a refused request must not touch the database');
    }
});

// =============================================================================
// 2. ?view=active — DAU / WAU / MAU + STICKINESS
// =============================================================================

// Answers the trailing-window query with the live row; everything else empty.
const activeRespond = (text) => {
    if (text.includes('AS dau,') && text.includes('AS mau')) {
        return [{
            dau: String(LIVE.dau), wau: String(LIVE.wau), mau: String(LIVE.mau),
            sessions_today: '76', sessions_7d: '254', sessions_30d: '785',
            latest_session_start: '2026-09-17T20:22:19.809Z',
        }];
    }
    if (text.includes('AS wau_now')) {
        return [{ wau_now: '74', wau_prior: '9', dau_now: '57', dau_prior: '17' }];
    }
    if (text.includes("date_trunc('day', received_at)::date::text AS day") && text.includes('AS sessions')) {
        return [{ day: '2026-09-17', dau: '53', sessions: '70' },
                { day: '2026-09-16', dau: '20', sessions: '34' }];
    }
    return [];
};

test('view=active returns DAU/WAU/MAU and the stickiness ratios, on the live numbers', async () => {
    const r = await call({ view: 'active', days: '30' }, null, activeRespond);
    assert.equal(r.out.statusCode, 200);
    const b = r.out.body;
    assert.equal(b.dau, 57);
    assert.equal(b.wau, 74);
    assert.equal(b.mau, 137);
    // 57/137 = 41.6%. The headline stickiness ratio, arithmetic pinned.
    assert.equal(b.stickiness.dau_over_mau_pct, 41.6);
    assert.equal(b.stickiness.wau_over_mau_pct, 54);
    assert.equal(b.stickiness.dau_over_wau_pct, 77);
    // MAU 137 clears the threshold, so these ARE reportable — the flag must be a
    // measurement, not a decoration.
    assert.equal(b.stickiness.low_n, false);
    assert.equal(b.stickiness.low_n_reason, null);
    // user-days is the SUM of per-day DAU (53 + 20), not a distinct-player count.
    assert.equal(b.user_days.value, 73);
    // Direction in WORDS, never a colour or an arrow.
    assert.equal(b.wau === 74 && b.trend.wau_word, 'GROWING');
    assert.ok(/DISTINCT player_id/.test(b.definition), 'the measure must be defined on the response');
});

test('view=active states that it is NOT the WO-1281 playing definition', async () => {
    const r = await call({ view: 'active' }, null, activeRespond);
    // Two different active-player definitions exist in this file. A view that does
    // not say which one it is will be compared against the other one.
    assert.match(r.out.body.not_this, /command/);
    assert.match(r.out.body.not_this, /never blended/);
});

test('view=active low_n fires when MAU is below the threshold — proven the other way', async () => {
    // Empty database: MAU 0. Ratios are null (never 0%), and the flag is set with a
    // reason naming the threshold.
    const r = await call({ view: 'active' });
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.out.body.mau, 0);
    assert.equal(r.out.body.stickiness.dau_over_mau_pct, null,
        'no data must render as null, never as 0% — 0% is a finding and null is an absence');
    assert.equal(r.out.body.stickiness.low_n, true);
    assert.match(r.out.body.stickiness.low_n_reason, /threshold/);
});

test('the 1/7/30 tiles are FIXED windows and are never clipped by ?days', async () => {
    // A 7-day selection must not silently render MAU as a 7-day number.
    const r = await call({ view: 'active', days: '7' }, null, activeRespond);
    const tiles = r.captured.find(c => c.sql.includes('AS mau'));
    assert.ok(tiles, 'the trailing-window query must exist');
    assert.ok(tiles.sql.includes("INTERVAL '30 days'"),
        'MAU must be a literal 30-day interval, not the ?days parameter');
    assert.ok(tiles.sql.includes("INTERVAL '7 days'"));
    assert.ok(!tiles.values.includes(7), 'the ?days value must not reach the fixed tiles');
    assert.match(r.out.body.window_note, /ignore \?days/);
});

// =============================================================================
// 3. ?view=monetization — PAYER RATE / ARPU / ARPDAU / ARPPU
// =============================================================================

function monetizationRespond(opts) {
    const o = opts || {};
    const payers = o.payers == null ? LIVE.payers : o.payers;
    const usdReal = o.usd_real == null ? LIVE.usd_real : o.usd_real;
    return (text) => {
        if (text.includes('AS payers_real')) {
            return [{
                settled: String(LIVE.settled), payers_all: String(payers), usd_all: LIVE.usd_all,
                settled_real: '3', payers_real: String(payers), usd_real: usdReal,
                rows_without_usd_anchor: '1',
                first_settled_at: '2026-08-23T00:09:02.330Z',
                last_settled_at: '2026-09-10T23:42:54.194Z',
            }];
        }
        if (text.includes('AS active_players')) {
            return [{ active_players: String(LIVE.players), user_days: String(LIVE.user_days), days_with_activity: '30' }];
        }
        if (text.includes('AS payers_seen_in_telemetry')) {
            return [{ payers: String(payers), payers_seen_in_telemetry: String(payers) }];
        }
        return [];
    };
}

test('view=monetization computes payer rate / ARPU / ARPDAU / ARPPU on the live numbers', async () => {
    const r = await call({ view: 'monetization', days: '30' }, null, monetizationRespond());
    assert.equal(r.out.statusCode, 200);
    const b = r.out.body;
    // 2 payers / 137 active = 1.5%
    assert.equal(b.payer_rate.pct, 1.5);
    assert.equal(b.payer_rate.payers, 2);
    assert.equal(b.payer_rate.active_players, 137);
    // 10.97 / 137 active players
    assert.equal(b.arpu.value, 0.0801);
    assert.equal(b.arpu.denominator, 137);
    // 10.97 / 211 user-days
    assert.equal(b.arpdau.value, 0.052);
    // 10.97 / 2 paying wallets
    assert.equal(b.arppu.value, 5.485);
    assert.equal(b.arppu.denominator, 2);
    assert.deepEqual(b.errors, []);
});

test('⛔ ARPDAU divides by USER-DAYS, not by DAU', async () => {
    const r = await call({ view: 'monetization', days: '30' }, null, monetizationRespond());
    const b = r.out.body;
    // The whole point: a 30-day revenue sum over one day's players overstates
    // ARPDAU by roughly the length of the window.
    assert.equal(b.arpdau.denominator, LIVE.user_days);
    assert.notEqual(b.arpdau.denominator, LIVE.dau);
    assert.equal(b.denominators.user_days, LIVE.user_days);
    assert.match(b.arpdau.formula, /user-days/);
    assert.match(b.denominators.definition, /NOT the same thing as DAU/);
});

test('⛔ every pre-revenue ratio is flagged NOT reportable and says why, in those words', async () => {
    const r = await call({ view: 'monetization' }, null, monetizationRespond());
    const b = r.out.body;
    assert.equal(b.headline, 'PRE-REVENUE — RATIOS NOT REPORTABLE');
    for (const key of ['arpu', 'arpdau', 'arppu']) {
        assert.equal(b[key].reportable, false, key + ' must not be reportable over 2 payers');
        assert.equal(b[key].low_n, true, key);
        assert.match(b[key].caveat, /INSUFFICIENT PAYER VOLUME/, key + ' must name the reason');
    }
    assert.equal(b.payer_rate.reportable, false);
    assert.match(b.payer_rate.caveat, /INSUFFICIENT PAYER VOLUME/);
});

test('the reportable flag is a MEASUREMENT: it flips once payer volume clears the threshold', async () => {
    // Proven the other way, so case 4 above cannot be passing on a hardcoded false.
    const r = await call({ view: 'monetization' }, null, monetizationRespond({ payers: 40, usd_real: 400 }));
    const b = r.out.body;
    assert.equal(b.headline, 'REVENUE MEASURABLE');
    assert.equal(b.arpu.reportable, true);
    assert.equal(b.arpu.caveat, null);
    assert.equal(b.arppu.value, 10);          // 400 / 40
    assert.equal(b.payer_rate.reportable, true);
});

test('a zero denominator returns null, never 0, and names the reason', async () => {
    // Empty database: no active players, no payers. A ratio over nothing is
    // undefined, and 0.0 ARPU would read as "they are here and not spending".
    const r = await call({ view: 'monetization' });
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.out.body.headline, 'NO REVENUE IN WINDOW');
    assert.equal(r.out.body.arpu.value, null);
    assert.match(r.out.body.arpu.caveat, /NO DENOMINATOR/);
    assert.equal(r.out.body.arppu.value, null);
});

test('⛔ devnet is excluded from revenue and every ratio is computed on usd_real', async () => {
    // usd_all 10.97 includes a devnet row; usd_real is 5.98. Reporting test money
    // as income is a fabricated revenue figure.
    const r = await call({ view: 'monetization' }, null, monetizationRespond({ usd_real: 5.98 }));
    const b = r.out.body;
    assert.equal(b.revenue.usd_all_networks, 10.97);
    assert.equal(b.revenue.usd_real, 5.98);
    assert.equal(b.arpu.numerator_usd, 5.98, 'ARPU must be computed on usd_real, not usd_all');
    assert.equal(b.arppu.numerator_usd, 5.98);
    assert.match(b.revenue.devnet_note, /devnet/);
    // And the SQL must actually carry the exclusion, not just the prose.
    const rev = r.captured.find(c => c.sql.includes('AS payers_real'));
    assert.ok(rev.sql.includes("<> 'devnet'"), 'the devnet exclusion must be in the query');
});

test('the payer/player identity overlap is MEASURED and returned, not assumed', async () => {
    const r = await call({ view: 'monetization' }, null, monetizationRespond());
    const b = r.out.body;
    // wallet (purchase_entitlements) and player_id (analytics_events) coincide only
    // for wallet-bound players; if they diverge the payer rate understates and the
    // response has to be able to say so.
    assert.equal(b.identity.payers_in_window, 2);
    assert.equal(b.identity.payers_seen_in_telemetry, 2);
    assert.equal(b.identity.fully_attributable, true);
    assert.match(b.identity.note, /WALLET-BOUND/);
});

test('LTV and churn are explicitly OUT OF SCOPE on the response', async () => {
    const r = await call({ view: 'monetization' }, null, monetizationRespond());
    assert.match(r.out.body.out_of_scope, /LTV/);
    assert.match(r.out.body.out_of_scope, /churn/);
});

// =============================================================================
// 4. ?view=stability — RATES, NOT COUNTS
// =============================================================================

const stabilityRespond = (text) => {
    if (text.includes('AS session_starts')) {
        return [{
            session_starts: String(LIVE.session_starts),
            players: String(LIVE.players),
            player_days: String(LIVE.user_days),
        }];
    }
    if (text.includes('AS kind') && text.includes('AS player_days')) {
        return [
            { kind: 'error', events: '33214', players: '166', player_days: String(LIVE.error_player_days), latest: 'x' },
            { kind: 'exception', events: '32', players: '3', player_days: String(LIVE.exception_player_days), latest: 'y' },
            { kind: 'possible_softlock', events: '110', players: '13', player_days: '33', latest: 'z' },
        ];
    }
    return [];
};

test('view=stability turns raw break counts into RATES against traffic', async () => {
    const r = await call({ view: 'stability', days: '30' }, null, stabilityRespond);
    assert.equal(r.out.statusCode, 200);
    const b = r.out.body;
    assert.equal(b.traffic.player_days, 211);
    assert.equal(b.traffic.session_starts, 785);
    const byKind = {};
    for (const k of b.by_kind) byKind[k.kind] = k;
    // 9 / 211 player-days = 4.3% affected, so 95.7% exception-free.
    assert.equal(byKind.exception.affected_pct, 4.3);
    assert.equal(byKind.exception.free_pct, 95.7);
    assert.equal(byKind.exception.events_per_session_start, 0.04);
    // 33 / 211 = 15.6%
    assert.equal(byKind.possible_softlock.affected_pct, 15.6);
    assert.equal(b.headline.exception_free_pct, 95.7);
    assert.equal(b.headline.reportable, true);
});

test('⛔ an incomplete denominator is reported as such, never clamped to look sane', async () => {
    const r = await call({ view: 'stability' }, null, stabilityRespond);
    const err = r.out.body.by_kind.find(k => k.kind === 'error');
    // 228 error player-days over 211 session player-days: the denominator is
    // genuinely incomplete and the honest answer is >100% plus the reason.
    assert.equal(err.affected_pct, 108.1);
    assert.equal(err.denominator_incomplete, true);
    assert.match(err.denominator_note, /UPPER bound/);
    assert.equal(err.free_pct, null,
        'above 100% there is no meaningful "free" share — null, not a negative percentage');
    // And the honest case must NOT carry the flag, or the flag proves nothing.
    const exc = r.out.body.by_kind.find(k => k.kind === 'exception');
    assert.equal(exc.denominator_incomplete, false);
    assert.equal(exc.denominator_note, null);
});

test('⛔ the break kinds are never summed into one "crash" number', async () => {
    const r = await call({ view: 'stability' }, null, stabilityRespond);
    const b = r.out.body;
    // kind=error is the Debug.LogError firehose (33k rows); kind=exception is the
    // 32 rows that matter. A single blended figure lets the firehose bury them.
    assert.equal(b.by_kind.length, 3, 'each kind keeps its own row');
    assert.equal(b.headline.metric.includes('exception-free'), true);
    assert.match(b.kinds_note, /never summed/);
    // No total-breaks field may exist at the top level.
    for (const banned of ['crash_rate', 'crashes', 'total_breaks', 'break_rate']) {
        assert.equal(banned in b, false, banned + ' must not exist — it would be a blended number');
    }
});

test('⛔ crash-free is NOT claimed: the headline is named a proxy and says why', async () => {
    const r = await call({ view: 'stability' }, null, stabilityRespond);
    const b = r.out.body;
    assert.match(b.crash_free_caveat, /NOT MEASURABLE/);
    assert.match(b.crash_free_caveat, /no crash reporter/);
    assert.match(b.headline.metric, /PROXY/);
    // And it points at the evidence rather than replacing it.
    assert.match(b.raw_rows_pointer, /view=events/);
});

test('view=stability flags low_n when there is not enough traffic to carry a rate', async () => {
    const r = await call({ view: 'stability' });
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.out.body.traffic.player_days, 0);
    assert.equal(r.out.body.traffic.low_n, true);
    assert.equal(r.out.body.headline.reportable, false);
    assert.match(r.out.body.headline.caveat, /not reportable/);
});

// =============================================================================
// 5. SLICING — app_version / platform, and the refused locale axis
// =============================================================================

const HOSTILE_VERSION = "2026.09.16'; DROP TABLE player_data; --";
const HOSTILE_PLATFORM = "Android' OR '1'='1";

test('⭐ a sliced view binds app_version/platform as PARAMETERS, never as SQL text', async () => {
    for (const view of ['retention', 'funnel', 'purchases', 'active', 'stability']) {
        const r = await call({
            view: view, days: '30',
            app_version: HOSTILE_VERSION, platform: HOSTILE_PLATFORM,
        }, null, () => []);
        assert.equal(r.out.statusCode, 200, view);
        assert.ok(r.captured.length >= 1, view + ' must issue at least one query');
        let seenInValues = false;
        for (const c of r.captured) {
            assert.ok(!c.sql.includes('DROP TABLE'), view + ': app_version reached the SQL text');
            assert.ok(!c.sql.includes("OR '1'='1"), view + ': platform reached the SQL text');
            if (c.values.includes(HOSTILE_VERSION) || c.values.includes(HOSTILE_PLATFORM)) seenInValues = true;
        }
        assert.ok(seenInValues, view + ': the slice value must appear as a BOUND VALUE somewhere');
    }
});

test('an unsliced request binds NULL, so one query text serves both cases', async () => {
    const r = await call({ view: 'retention', days: '30' }, null, () => []);
    const q = r.captured.find(c => c.sql.includes('cohort_day'));
    assert.ok(q, 'the retention cohort query must exist');
    assert.ok(q.sql.includes('::text IS NULL OR'),
        'the null-means-no-filter idiom must be in the query text');
    assert.ok(q.values.includes(null), 'an unsliced request binds null');
    assert.equal(r.out.body.slice.active, false);
});

test('each sliced view SAYS what was filtered and what was not', async () => {
    const r = await call({ view: 'purchases', platform: 'Android' }, null, () => []);
    const s = r.out.body.slice;
    assert.equal(s.active, true);
    assert.equal(s.platform, 'Android');
    // ⛔ The operational lists must never be hidden by a build filter: an
    // unfulfilled sale still needs a human whatever build the buyer was on.
    assert.ok(s.not_filtered.some(x => /needs_attention/.test(x)),
        'the unfulfilled list must be declared UNFILTERED');
    assert.ok(s.filters.length > 0);
    assert.match(s.ambiguity, /session_start/);
    assert.match(r.out.body.slice_identity_caveat, /WALLET-BOUND/);
});

test('the multi-build ambiguity is PRINTED, not implied', async () => {
    for (const view of ['retention', 'funnel', 'purchases', 'stability']) {
        const r = await call({ view: view, app_version: '2026.09.16.371701' }, null, () => []);
        assert.match(r.out.body.slice.ambiguity, /matches BOTH/, view);
    }
});

test('⛔ a slice that matches NOBODY filters everything out, it does not fall back to no filter', async () => {
    // The resolved cohort is empty, which binds [] — `= ANY('{}')` is false for
    // every row. Falling back to "no filter" would report the whole playerbase
    // under a build label that has no players.
    const r = await call({ view: 'funnel', app_version: 'never-shipped' }, null, (text) => {
        if (text.includes('SELECT DISTINCT player_id')) return [];   // nobody booted it
        return [];
    });
    assert.equal(r.out.statusCode, 200);
    assert.equal(r.out.body.slice.matched_players, 0);
    const stepQ = r.captured.find(c => c.sql.includes('tutorial_step_enter'));
    assert.ok(stepQ, 'the step query must exist');
    const arrays = stepQ.values.filter(v => Array.isArray(v));
    assert.ok(arrays.some(a => a.length === 0),
        'an empty cohort must be bound as an empty array, not as null');
});

test('the resolved slice cohort is CAPPED and says when the cap bit', async () => {
    const r = await call({ view: 'funnel', platform: 'Android' }, null, (text) => {
        if (text.includes('SELECT DISTINCT player_id')) return [{ player_id: 'W1' }, { player_id: 'W2' }];
        return [];
    });
    const resolve = r.captured.find(c => c.sql.includes('SELECT DISTINCT player_id'));
    assert.ok(resolve.sql.includes('LIMIT'), 'the cohort resolution must be bounded');
    assert.equal(r.out.body.slice.matched_players, 2);
    assert.equal(r.out.body.slice.cohort_truncated, false);
    assert.equal(r.out.body.slice.cohort_cap, 5000);
});

test('⛔ ?locale= is REFUSED with 400 and costs no query, because no event carries a locale', async () => {
    for (const view of ['retention', 'funnel', 'purchases', 'active', 'monetization', 'stability']) {
        const r = await call({ view: view, locale: 'en-US' });
        assert.equal(r.out.statusCode, 400, view);
        assert.equal(r.captured.length, 0, view + ': a refused axis must not run a query');
        assert.equal(r.out.body.axis, 'locale', view);
        assert.match(r.out.body.detail, /NOT COLLECTED/, view);
        assert.deepEqual(r.out.body.available_axes, ['app_version', 'platform'], view);
    }
});

test('the locale gap is declared on the unfiltered responses too', async () => {
    // A gap nobody is told about is a gap that gets re-discovered as a bug.
    for (const view of ['retention', 'funnel', 'purchases', 'active', 'stability']) {
        const r = await call({ view: view }, null, () => []);
        const gaps = r.out.body.slice.data_gaps;
        assert.ok(Array.isArray(gaps) && gaps.some(g => /locale/.test(g)), view);
    }
});

// =============================================================================
// 6. THE CONTRACT — discoverable, SELECT-only, bounded
// =============================================================================

test('⛔ operator/test wallets are excluded from BOTH SIDES of every monetization ratio', async () => {
    // WO-1281 acceptance 9, applied structurally. The denominator already excluded
    // operator ids; leaving them in the numerator would count the owner's own test
    // purchase against a playerbase that does not contain them — at 2 payers that
    // roughly doubles the payer rate. Asserted on the QUERIES, because the local
    // env sets no excluded ids, so a response-only check would pass vacuously.
    const r = await call({ view: 'monetization' }, null, monetizationRespond());
    const revenueQueries = r.captured.filter(c => c.sql.includes('purchase_entitlements'));
    assert.ok(revenueQueries.length >= 3, 'the revenue side issues several queries');
    for (const c of revenueQueries) {
        assert.ok(/NOT \(\s*p?\.?wallet = ANY/.test(c.sql.replace(/\s+/g, ' ')),
            'a revenue query with no operator exclusion mixes two populations: ' + c.sql.slice(0, 80));
    }
    assert.match(r.out.body.revenue.operator_exclusion, /BOTH SIDES/);
    assert.match(r.out.body.revenue.operator_exclusion, /view=purchases/);
});

test('⛔ a slice a view cannot honour is DECLARED IGNORED, never swallowed', async () => {
    // The worst failure mode this ticket exists to prevent: an unfiltered number
    // returned under a build label the caller believes was applied.
    for (const view of ['monetization', 'overview', 'economy']) {
        const r = await call({ view: view, platform: 'Android' }, null, monetizationRespond());
        assert.equal(r.out.statusCode, 200, view);
        assert.ok(r.out.body.slice_ignored, view + ': an unhonoured slice must be declared');
        assert.equal(r.out.body.slice_ignored.platform, 'Android', view);
        assert.match(r.out.body.slice_ignored.note, /IGNORED/, view);
    }
    // And a view that DOES slice must never carry the notice, or it proves nothing.
    for (const view of ['retention', 'funnel', 'purchases', 'active', 'stability']) {
        const r = await call({ view: view, platform: 'Android' }, null, () => []);
        assert.equal(r.out.body.slice_ignored, undefined, view + ' honours the slice');
    }
});

test('the per-day exception rate is NOT clamped, and flags its own incomplete denominator', async () => {
    // Same rule as by_kind, applied per row: a clamp would print a plausible 0%
    // and contradict the rule this very view states.
    const r = await call({ view: 'stability' }, null, (text) => {
        if (text.includes('AS session_starts')) {
            return [{ session_starts: '10', players: '3', player_days: '5' }];
        }
        if (text.includes('AS players_with_exception') && text.includes('LEFT JOIN')) {
            // 4 players hit an exception on a day only 2 fired a session_start.
            return [{ day: '2026-09-17', players: '2', error_events: '9', exception_events: '5',
                      players_with_exception: '4', players_with_softlock: '0' }];
        }
        return [];
    });
    const row = r.out.body.per_day[0];
    assert.equal(row.players, 2);
    assert.equal(row.players_with_exception, 4);
    assert.equal(row.exception_free_pct, -100, 'the honest output is negative, not a clamped 0');
    assert.equal(row.denominator_incomplete, true);
});

test('the three new views are named in the unknown-view hint', async () => {
    const r = await call({ view: 'nonsense' });
    assert.equal(r.out.statusCode, 400);
    for (const view of ['active', 'monetization', 'stability']) {
        assert.ok(r.out.body.error.includes(view),
            view + ' must be discoverable — a view nobody can find is a view nobody uses');
    }
});

test('every query the new views issue is a SELECT with a hard LIMIT', async () => {
    for (const view of ['active', 'monetization', 'stability']) {
        const r = await call({ view: view, days: '30' }, null, () => []);
        assert.ok(r.captured.length > 0, view);
        for (const c of r.captured) {
            const head = c.sql.replace(/\s+/g, ' ').trim().toUpperCase();
            assert.ok(head.startsWith('SELECT') || head.startsWith('WITH'),
                view + ': every statement must be a read — got ' + head.slice(0, 40));
            assert.ok(/\bLIMIT\b/.test(c.sql),
                view + ': every query needs a hard LIMIT on a table that only grows');
            for (const verb of ['INSERT INTO', 'UPDATE ', 'DELETE FROM', 'TRUNCATE', 'DROP ']) {
                assert.ok(!c.sql.toUpperCase().includes(verb), view + ': ' + verb + ' in a read endpoint');
            }
        }
    }
});

test('no query in this file carries a backtick inside a SQL comment', () => {
    // A real defect found in this lane: a backtick inside a `--` comment inside a
    // tagged template TERMINATES the template literal and breaks the whole module.
    // It parses as valid JS right up until it does not, so it is pinned here.
    const lines = readSrc().split(/\r?\n/);
    const offenders = [];
    for (let i = 0; i < lines.length; i++) {
        const m = /^\s*--\s.*/.exec(lines[i]);
        if (m && lines[i].includes('`')) offenders.push((i + 1) + ': ' + lines[i].trim());
    }
    assert.deepEqual(offenders, [], 'backticks in SQL comments break the template literal');
});

test('no schema migration was added for WO-1843 — these are pure reads', () => {
    // The WO says so explicitly, and the acceptance criterion is checkable: nothing
    // here needs new persisted state, so no migration may appear for it.
    const dir = path.join(REPO, 'api/migrations');
    const named = fs.existsSync(dir)
        ? fs.readdirSync(dir).filter(f => /1843/.test(f))
        : [];
    assert.deepEqual(named, [], 'WO-1843 must add no migration: every addition is a query');
});
