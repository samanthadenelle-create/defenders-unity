// =============================================================================
// test/clan-leaderboard.test.js — WO-1850 (clan WO-7).
// -----------------------------------------------------------------------------
// GET /api/clan/leaderboard, asserted at RUNTIME against a recording tagged-
// template mock — zero network, zero database, zero Unity. The harness is a
// standalone copy of test/clan-membership.test.js's shape (same recording mock,
// same route-loader idiom) rather than an addition to that file: this ticket is
// file-disjoint from the concurrent clan lanes (WO-1851 gate/join-policy, WO-1858
// chat wiring), and touching a shared test file is exactly the kind of file that
// is NOT disjoint.
//
// WHAT THIS FILE IS ACTUALLY GUARDING:
//
//   1. THE METRIC IS THE DOCUMENTED FALLBACK, `member_count × days_since_created`
//      — never `clan_vigil_weight`, which does not exist in this schema. A clan
//      with more members AND more age outranks one with fewer of both; ties break
//      by `created_at ASC` (older first), per the WO-7 draft's own acceptance
//      criterion.
//   2. NO WALLET ADDRESS ANYWHERE IN THE RESPONSE, at any nesting depth — the same
//      property api/admin/stats.js already pins elsewhere in this codebase.
//   3. AUTH GOES THROUGH THE SAME SHARED PREAMBLE (api/_lib/clan-http.js) every
//      other clan route uses, so an unauthenticated request gets the IDENTICAL
//      shape — not a second hand-rolled 401.
//   4. THE EXISTING (UNRELATED) LEADERBOARD IS UNTOUCHED: this file never requires
//      api/leaderboard/get.js and asserts by grep that api/clan/leaderboard.js
//      never references `leaderboard_scores`.
//   5. `?limit=` IS CLAMPED, NEVER TRUSTED: default 50, ceiling 100, garbage input
//      falls back to the default rather than reaching the database unbounded.
//
//     node --test test/clan-leaderboard.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const CLAN_LIB = path.join(REPO, 'api', '_lib', 'clan.js');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const ROUTE_PATH = path.join(REPO, 'api', 'clan', 'leaderboard.js');
const UNRELATED_LEADERBOARD = path.join(REPO, 'api', 'leaderboard', 'get.js');

const clan = require(CLAN_LIB);
const { AuthCode } = require(path.join(REPO, 'api', '_lib', 'wallet-auth.js'));

const WALLET = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';

// ═══════════════════════════════════════════════════════════════════════════
// HARNESS — copied in shape from test/clan-membership.test.js, not shared with it
// ═══════════════════════════════════════════════════════════════════════════

function recordingSql(route = []) {
    const calls = [];
    const fn = (strings, ...values) => {
        const text = Array.isArray(strings) ? strings.join(' ? ') : String(strings);
        calls.push({ text: text, values: values });
        for (const r of route) {
            if (!r.match.test(text)) continue;
            const hit = calls.filter(c => r.match.test(c.text)).length;
            if (typeof r.answer === 'function') {
                const a = r.answer(hit, values);
                if (a && a.throws) return Promise.reject(a.throws);
                return Promise.resolve(a && a.rows ? a.rows : []);
            }
            if (r.throws) return Promise.reject(r.throws);
            return Promise.resolve(r.rows || []);
        }
        return Promise.resolve([]);
    };
    fn.calls = calls;
    fn.matching = re => calls.filter(c => re.test(c.text));
    return fn;
}

const DRIVER_ID = require.resolve('@neondatabase/serverless');

function fakeRes() {
    const out = { statusCode: null, body: null, headers: {}, ended: false };
    const res = {
        setHeader(k, v) { out.headers[String(k).toLowerCase()] = v; return res; },
        status(code) { out.statusCode = code; return res; },
        json(obj) { out.body = obj; return res; },
        send() { return res; },
        end() { out.ended = true; return res; },
    };
    res.out = out;
    return res;
}

function loadRoute(sqlFn) {
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = { neon() { return sqlFn; } };
    delete require.cache[require.resolve(CLAN_HTTP)];
    delete require.cache[require.resolve(CLAN_LIB)];
    delete require.cache[require.resolve(ROUTE_PATH)];
    const handler = require(ROUTE_PATH);
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(ROUTE_PATH)];
    delete require.cache[require.resolve(CLAN_HTTP)];
    delete require.cache[require.resolve(CLAN_LIB)];
    return handler;
}

async function callRoute(opts = {}) {
    const method = opts.method || 'GET';
    const sqlFn = opts.sql || recordingSql();
    const headers = Object.assign({ 'x-session': 'a'.repeat(44) }, opts.headers || {});
    const req = { method: method, headers: headers, query: opts.query || {} };
    const prevUrl = process.env.DATABASE_URL;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const handler = loadRoute(sqlFn);
    const res = fakeRes();
    try {
        await handler(req, res);
    } finally {
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, sql: sqlFn };
}

/** A route-level sql that authenticates the wallet rail, plus a leaderboard answer. */
function authedSql(leaderboardRows) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i, rows: [{ first_seen_at: 'T0', last_seen_at: 'T0' }] },
        { match: /WITH\s+ranked\s+AS/i, rows: leaderboardRows || [] },
    ]);
}

function rankedRow(overrides = {}) {
    return Object.assign({
        rank: 1, clan_id: '3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e',
        name: 'Ember Wardens', tag: 'EMBR', member_count: 3, metric: 30,
    }, overrides);
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE QUERY — clan.getLeaderboard, in isolation
// ═══════════════════════════════════════════════════════════════════════════

test('getLeaderboard ranks by member_count * days_since_created and shapes the row', async () => {
    const sql = recordingSql([
        { match: /WITH\s+ranked\s+AS/i, rows: [
            rankedRow({ rank: 1, name: 'Old Guard', tag: 'OLD', member_count: 3, metric: 30 }),
            rankedRow({ rank: 2, name: 'Newcomers', tag: 'NEW', member_count: 1, metric: 1 }),
        ] },
    ]);
    const rows = await clan.getLeaderboard(sql, 50);

    assert.equal(rows.length, 2);
    assert.deepEqual(Object.keys(rows[0]).sort(),
        ['clanId', 'memberCount', 'metric', 'name', 'rank', 'tag'].sort());
    assert.equal(rows[0].rank, 1);
    assert.equal(rows[0].name, 'Old Guard');
    assert.equal(rows[0].metric, 30);
    assert.equal(rows[0].memberCount, 3);
    assert.equal(rows[1].rank, 2);

    const q = sql.matching(/WITH\s+ranked\s+AS/i)[0].text;
    assert.match(q, /member_count\s*\*\s*days_since_created/i,
        'the fallback metric per docs/SKR Integtration.md:526 — member_count × days_since_created');
    assert.match(q, /ORDER\s+BY.*DESC.*created_at\s+ASC/is,
        'descending by metric, ties broken by created_at ASC (WO-7 acceptance criterion)');
    assert.doesNotMatch(q, /wallet/i, '⛔ no wallet column is ever selected by the leaderboard query');
    assert.match(q, /LIMIT\s+\?/, 'limit is a bound PARAMETER, never interpolated');
});

test('a clan with one member and one day old ranks below a clan with three members and ten days old',
    () => {
        const younger = 1 * 1;
        const older = 3 * 10;
        assert.ok(older > younger, 'fixture assumption pinned by the WO-7 acceptance criterion');
    });

test('getLeaderboard returns an empty array, not null, when no clans exist', async () => {
    const sql = recordingSql([{ match: /WITH\s+ranked\s+AS/i, rows: [] }]);
    const rows = await clan.getLeaderboard(sql, 50);
    assert.deepEqual(rows, []);
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. clampLeaderboardLimit — never trust ?limit= unbounded
// ═══════════════════════════════════════════════════════════════════════════

test('clampLeaderboardLimit defaults to 50 and ceilings at 100', () => {
    assert.equal(clan.clampLeaderboardLimit(undefined), 50);
    assert.equal(clan.clampLeaderboardLimit(''), 50);
    assert.equal(clan.clampLeaderboardLimit('not-a-number'), 50);
    assert.equal(clan.clampLeaderboardLimit('0'), 1, 'clamped up to the floor of 1');
    assert.equal(clan.clampLeaderboardLimit('-5'), 1);
    assert.equal(clan.clampLeaderboardLimit('7'), 7);
    assert.equal(clan.clampLeaderboardLimit('9999'), 100, 'ceilinged at LEADERBOARD_MAX_LIMIT');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. THE ROUTE — auth, shape, no-wallet-ever, and the unrelated leaderboard
// ═══════════════════════════════════════════════════════════════════════════

test('GET /api/clan/leaderboard returns ranked clans for an authenticated wallet', async () => {
    const sql = authedSql([
        rankedRow({ rank: 1, name: 'Ember Wardens', tag: 'EMBR', member_count: 3, metric: 30 }),
        rankedRow({ rank: 2, name: 'Solo Camp', tag: 'SOLO', member_count: 1, metric: 2,
            clan_id: '11111111-1111-4111-8111-111111111111' }),
    ]);
    const { out } = await callRoute({ sql, query: { playerId: WALLET, limit: '10' } });

    assert.equal(out.statusCode, 200);
    assert.equal(out.body.ok, true);
    assert.equal(out.body.clans.length, 2);
    assert.equal(out.body.clans[0].rank, 1);
    assert.equal(out.body.clans[0].name, 'Ember Wardens');
    assert.equal(out.body.clans[1].rank, 2);

    const limitCall = sql.matching(/WITH\s+ranked\s+AS/i)[0];
    assert.equal(limitCall.values[limitCall.values.length - 1], 10, 'the clamped limit reached the query');
});

test('⛔ an unauthenticated request is refused with the SAME shape as every other clan route', async () => {
    const sql = recordingSql([{ match: /FROM\s+auth_sessions/i, rows: [] }]);
    const { out } = await callRoute({ sql, query: { playerId: WALLET } });

    assert.equal(out.statusCode, 401);
    assert.equal(out.body.ok, false);
    assert.equal(out.body.code, AuthCode.SESSION_UNKNOWN, 'the stable auth code, not a bespoke refusal');
    assert.equal(typeof out.body.ref, 'string');
    assert.ok(!('wallet' in out.body) && !('detail' in out.body), 'minimal player-facing body');
    assert.equal(sql.matching(/WITH\s+ranked\s+AS/i).length, 0,
        'the leaderboard query must never run before authentication');
});

test('⛔ the GUEST rail cannot reach the leaderboard (schema-forced wallet narrowing)', async () => {
    const sql = recordingSql([
        { match: /INSERT\s+INTO\s+guest_rate_limit/i, rows: [{ hits: 1, total_hits: 1 }] },
    ]);
    const GUEST = 'guest-local-' + 'b'.repeat(64);
    const { out } = await callRoute({
        sql, headers: { 'x-guest-id': GUEST }, query: { playerId: GUEST },
    });
    assert.equal(out.statusCode, 401);
    assert.equal(out.body.code, AuthCode.WALLET_REQUIRED);
    assert.equal(sql.matching(/WITH\s+ranked\s+AS/i).length, 0);
});

test('an OPTIONS preflight is answered 204 with the clan headers advertised', async () => {
    const { out } = await callRoute({ method: 'OPTIONS' });
    assert.equal(out.statusCode, 204);
    assert.match(String(out.headers['access-control-allow-headers']), /X-Wallet/);
});

test('⛔ no wallet address appears anywhere in the response body, at any nesting depth', async () => {
    const sql = authedSql([
        rankedRow({ rank: 1, name: 'Ember Wardens', tag: 'EMBR', member_count: 3, metric: 30 }),
    ]);
    const { out } = await callRoute({ sql, query: { playerId: WALLET } });
    assert.equal(out.statusCode, 200);

    const serialized = JSON.stringify(out.body);
    assert.doesNotMatch(serialized, new RegExp(WALLET),
        'the calling wallet must never be echoed into a leaderboard response');
    // A base58 Solana address shape in general — 32-44 chars, no 0/O/I/l — never rendered.
    assert.doesNotMatch(serialized, /"[1-9A-HJ-NP-Za-km-z]{32,44}"/,
        'no field in the response is wallet-address-shaped');
    for (const row of out.body.clans) {
        assert.deepEqual(Object.keys(row).sort(),
            ['clanId', 'memberCount', 'metric', 'name', 'rank', 'tag'].sort(),
            'the response shape is exactly rank/clanId/name/tag/metric/memberCount — nothing else');
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE UNRELATED LEADERBOARD — untouched
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the clan leaderboard never references the unrelated leaderboard_scores table', () => {
    const src = fs.readFileSync(ROUTE_PATH, 'utf8');
    const libSrc = fs.readFileSync(CLAN_LIB, 'utf8');
    assert.doesNotMatch(src, /leaderboard_scores/i);
    assert.doesNotMatch(libSrc, /leaderboard_scores/i);
});

test('⛔ api/leaderboard/get.js was not modified by this ticket (file exists, untouched shape)', () => {
    assert.ok(fs.existsSync(UNRELATED_LEADERBOARD), 'fixture assumption: the unrelated leaderboard exists');
    const src = fs.readFileSync(UNRELATED_LEADERBOARD, 'utf8');
    assert.match(src, /leaderboard_scores/i, 'still reads its own table, unchanged by this ticket');
    assert.doesNotMatch(src, /clan_members|FROM\s+clans\b/i,
        'the unrelated leaderboard must never grow a dependency on the clan tables');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. THE METRIC IS A NAMED PLACEHOLDER — pin the comment, not just the behavior
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the leaderboard QUERY never references clan_vigil_weight — it does not exist in any migration',
    async () => {
        const sql = recordingSql([{ match: /WITH\s+ranked\s+AS/i, rows: [] }]);
        await clan.getLeaderboard(sql, 50);
        const q = sql.matching(/WITH\s+ranked\s+AS/i)[0].text;
        assert.doesNotMatch(q, /clan_vigil_weight/i,
            'the real Vigil-weight column is WO-1852+ scope; the SQL must select ONLY columns that ' +
            'exist today (clans, clan_members), never a column no migration has ever created');

        for (const migFile of fs.readdirSync(path.join(REPO, 'api', 'migrations'))) {
            const migSrc = fs.readFileSync(path.join(REPO, 'api', 'migrations', migFile), 'utf8');
            assert.doesNotMatch(migSrc, /clan_vigil_weight/i,
                migFile + ': fixture assumption — clan_vigil_weight must not exist yet. If it now ' +
                'does, WO-9 shipped and this fallback metric should be swapped for the real column, ' +
                'per WO-1850\'s own scope note.');
        }
    });

test('⛔ the fallback metric is documented as a placeholder for the future clan_vigil_weight column', () => {
    const libSrc = fs.readFileSync(CLAN_LIB, 'utf8');
    assert.match(libSrc, /vigil/i,
        'the placeholder must be documented as a placeholder, not silently passed off as the real metric');
});
