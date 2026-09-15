'use strict';

// =============================================================================
// WO-1506 — /api/events/track accepted a CLIENT-ASSERTED playerId with no auth
// and no rate limit.
// -----------------------------------------------------------------------------
// The route wrote `analytics_events.player_id` straight from the request body, so
// anyone could write unbounded rows attributed to any wallet — and those rows feed
// the retention/funnel numbers the owner makes business decisions from.
//
// The fix, and what this file pins:
//   1. The row is bound to the CALLER, never to a body-asserted WALLET. A verified
//      session (X-Session) names the wallet; an X-Guest-Id binds to that guest id;
//      with neither, the row lands under the literal id `unverified` so one entry in
//      ANALYTICS_EXCLUDED_PLAYER_IDS removes the whole unproven bucket.
//
// ⚠ AMENDED BY WO-1733 (2026-09-15). The client sends NEITHER header, so from
//   2026-09-07T10:45Z every row in the live DB landed as `unverified` and
//   COUNT(DISTINCT player_id) read 1 forever. The route now ALSO accepts a
//   GUEST-SHAPED playerId out of the body when no header was offered (_auth:
//   'guest-body') — a guest id is a 256-bit bearer credential the server already
//   trusts in the header, so the same value through the body forges nothing. A
//   WALLET-shaped body id is STILL refused: a wallet address is public, not a
//   credential. Section 5 below pins that asymmetry.
//   2. The shared IP budget (api/_lib/ip-budget.js, WO-1456) rate-limits the
//      route — FAIL-OPEN, because analytics must never take the game down.
//   3. The success path still works: ordinary events still land (memory
//      `prove-the-success-path-not-just-the-refusal`).
//
// ⚠ WO §4 acceptance 2 asked for a "server-minted guest id" for anonymous events.
//   No such minting helper exists in this project (guest ids are minted on the
//   DEVICE), and a per-request server id is either unbounded cardinality or
//   forgeable. Per the lane instruction, anonymous traffic is instead TAGGED
//   `unverified`. Recorded here rather than quietly reinterpreted (§11B).
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const trackSrc = fs.readFileSync(path.join(root, 'api/events/track.js'), 'utf8');
const track = require('../api/events/track.js');

const WALLET_A = 'B1oNqzGevRmYh6Ntcx2p9Y1oPd3TzZ7vGkR4sQwJc2aE';
const WALLET_B = 'C7uKpMtA4wLdNq8Rr2fY6HbVzXe1JmS9TgW3vPc5DkQn';
const GUEST = 'guest-local-' + 'a'.repeat(64);   // wallet-auth.GUEST_RE, read at source

/**
 * One fake Neon client serving BOTH call shapes the route uses:
 *   - tagged template  → wallet-auth.verifySession, ip-budget's UPSERT
 *   - sql(text, params) → the multi-row analytics insert
 * Every statement is recorded so a test can assert what did NOT happen.
 */
function fakeSql(opts = {}) {
    const calls = [];
    const fn = (a, ...rest) => {
        const tagged = Array.isArray(a) && Array.isArray(a.raw);
        const text = tagged ? a.join('?') : String(a);
        const values = tagged ? rest : (rest[0] || []);
        calls.push({ text, values, tagged });

        if (/auth_sessions/.test(text)) {
            return Promise.resolve(opts.sessionRows || []);
        }
        if (/promo_ip_budget/.test(text)) {
            if (opts.budgetError) return Promise.reject(opts.budgetError);
            const g = opts.grants == null ? 1 : opts.grants;
            return Promise.resolve([{ grants: g, total_grants: g }]);
        }
        return Promise.resolve([]);
    };
    fn.calls = calls;
    fn.inserts = () => calls.filter((c) => /INSERT INTO analytics_events/.test(c.text));
    return fn;
}

function makeReq(events, headers = {}) {
    return { method: 'POST', headers: Object.assign({ 'x-forwarded-for': '203.0.113.7' }, headers), body: { events } };
}

function makeRes() {
    const res = {
        statusCode: null, body: null, headers: {},
        setHeader(k, v) { this.headers[k.toLowerCase()] = v; },
        status(c) { this.statusCode = c; return this; },
        json(b) { this.body = b; return this; },
        end() { return this; },
    };
    return res;
}

async function run(sql, req) {
    const handler = track._test.makeHandler({ getSql: () => sql });
    const res = makeRes();
    await handler(req, res);
    return res;
}

/** player_id is parameter 1 of each 4-parameter row tuple. */
function insertedPlayerIds(sql) {
    const ins = sql.inserts();
    if (ins.length === 0) return [];
    const p = ins[0].values;
    const out = [];
    for (let i = 0; i < p.length; i += 4) out.push(p[i]);
    return out;
}

function insertedProps(sql) {
    const ins = sql.inserts();
    if (ins.length === 0) return [];
    const p = ins[0].values;
    const out = [];
    for (let i = 2; i < p.length; i += 4) out.push(JSON.parse(p[i]));
    return out;
}

// ── 1. THE HOLE: a client-asserted wallet id ─────────────────────────────────

test('an asserted wallet id with NO auth headers is overridden, never written', async () => {
    const sql = fakeSql();
    const res = await run(sql, makeReq([{ playerId: WALLET_A, eventName: 'session_start', clientTs: 1 }]));

    assert.equal(res.statusCode, 200);
    const ids = insertedPlayerIds(sql);
    assert.deepEqual(ids, ['unverified'],
        'the body-asserted wallet reached analytics_events — anyone can still attribute rows to any wallet');
    assert.equal(insertedProps(sql)[0]._auth, 'unverified',
        'the row is not self-describing; a reader cannot tell proven traffic from asserted traffic');
});

test('a valid session OVERRIDES the asserted id — the token names the player, the body never does', async () => {
    const sql = fakeSql({ sessionRows: [{ wallet: WALLET_B, revoked: false, expired: false }] });
    const res = await run(sql, makeReq(
        [{ playerId: WALLET_A, eventName: 'wave_completed', clientTs: 2 }],
        { 'x-session': 'a'.repeat(48) },
    ));

    assert.equal(res.statusCode, 200);
    assert.deepEqual(insertedPlayerIds(sql), [WALLET_B],
        'a session for wallet B wrote a row for the wallet the BODY named — the token is not binding');
    assert.equal(insertedProps(sql)[0]._auth, 'session');
});

test('an unknown/expired session does not grant a wallet id — it degrades to unverified', async () => {
    const sql = fakeSql({ sessionRows: [] });
    await run(sql, makeReq(
        [{ playerId: WALLET_A, eventName: 'session_start', clientTs: 3 }],
        { 'x-session': 'b'.repeat(48) },
    ));
    assert.deepEqual(insertedPlayerIds(sql), ['unverified'],
        'an unknown session token still bought the asserted wallet id');
});

test('X-Guest-Id binds the row to that guest id', async () => {
    const sql = fakeSql();
    await run(sql, makeReq(
        [{ playerId: WALLET_A, eventName: 'session_start', clientTs: 4 }],
        { 'x-guest-id': GUEST },
    ));
    assert.deepEqual(insertedPlayerIds(sql), [GUEST]);
    assert.equal(insertedProps(sql)[0]._auth, 'guest');
});

test('a MALFORMED guest header buys nothing', async () => {
    const sql = fakeSql();
    await run(sql, makeReq(
        [{ playerId: WALLET_A, eventName: 'session_start', clientTs: 5 }],
        { 'x-guest-id': WALLET_A },
    ));
    assert.deepEqual(insertedPlayerIds(sql), ['unverified'],
        'a wallet-shaped string in X-Guest-Id was accepted as an identity');
});

test('the guest rail never spends the SAVE budget (guest_rate_limit is keyed on guest id and shared with save/load)', async () => {
    const sql = fakeSql();
    await run(sql, makeReq([{ playerId: GUEST, eventName: 'session_start', clientTs: 6 }], { 'x-guest-id': GUEST }));
    assert.equal(sql.calls.filter((c) => /guest_rate_limit/.test(c.text)).length, 0,
        'analytics is spending the guest save budget — a busy funnel would 429 the player\'s own saves');
});

// ── 2. The success path (memory: prove-the-success-path-not-just-the-refusal) ─

test('an ordinary batch still lands, one row per event', async () => {
    const sql = fakeSql({ sessionRows: [{ wallet: WALLET_A, revoked: false, expired: false }] });
    const res = await run(sql, makeReq([
        { playerId: WALLET_A, eventName: 'session_start', properties: '{"k":1}', clientTs: 10 },
        { playerId: WALLET_A, eventName: 'wave_completed', properties: '{"k":2}', clientTs: 11 },
    ], { 'x-session': 'c'.repeat(48) }));

    assert.equal(res.statusCode, 200);
    assert.equal(res.body.success, true);
    assert.equal(res.body.inserted, 2, 'the success path stopped inserting');
    assert.deepEqual(insertedPlayerIds(sql), [WALLET_A, WALLET_A]);
    const props = insertedProps(sql);
    assert.equal(props[0].k, 1, 'client properties were dropped by the auth stamp');
    assert.equal(props[1]._auth, 'session');
});

// ── 3. The IP budget (shared helper, fail-open) ──────────────────────────────

test('a caller past its IP budget is refused and writes NOTHING', async () => {
    const sql = fakeSql({ grants: 999 });
    const res = await run(sql, makeReq([{ playerId: 'x', eventName: 'session_start', clientTs: 20 }]));

    assert.equal(sql.inserts().length, 0, 'a rate-limited caller still wrote analytics rows');
    assert.equal(res.statusCode, 200,
        'a non-2xx makes EventTracker.FlushWithRetry retry the batch 4x — a refusal must not become a storm');
    assert.equal(res.body.success, false);
    assert.equal(res.body.error, 'RATE_LIMITED');
});

test('an UNREADABLE budget table must not take analytics down (fail-open)', async () => {
    const sql = fakeSql({ budgetError: new Error('relation "promo_ip_budget" does not exist') });
    const res = await run(sql, makeReq([{ playerId: 'x', eventName: 'session_start', clientTs: 21 }]));
    assert.equal(res.statusCode, 200);
    assert.equal(res.body.success, true);
    assert.equal(sql.inserts().length, 1, 'a missing budget table stopped every analytics write');
});

test('a malformed request never costs a household a unit of budget', async () => {
    const sql = fakeSql();
    const res = await run(sql, { method: 'POST', headers: {}, body: { nope: true } });
    assert.equal(res.statusCode, 400);
    assert.equal(sql.calls.filter((c) => /promo_ip_budget/.test(c.text)).length, 0,
        'the budget is spent before the free shape checks');
});

// ── 4. One limiter, one implementation ───────────────────────────────────────

test('the route uses the SHARED budget helper, keyed on the one signal a client cannot choose', () => {
    assert.match(trackSrc, /require\(['"]\.\.\/_lib\/ip-budget['"]\)/,
        'events/track.js does not import the shared budget helper');
    assert.match(trackSrc, /hashIp\(req\)/);
    assert.match(trackSrc, /reserveIpBudget\(/);
    const executable = trackSrc.replace(/^\s*\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '');
    assert.doesNotMatch(executable, /INSERT INTO promo_ip_budget/,
        'a second limiter was inlined into the route — duplicated state');
    // ⚠ A `doesNotMatch(executable, /ev\.playerId/)` lint stood here until WO-1733.
    // It pinned "the body never names the player"; the invariant is now the narrower
    // "the body never names a WALLET", which a source-text lint cannot express. The
    // behavioural pins in section 5 replace it, and they check the property that
    // actually matters rather than the spelling of the code that implements it.
    assert.match(trackSrc, /isGuestId\(bodyGuestCandidate\)/,
        'the body rail no longer runs its candidate through the guest-shape gate');
});

test('the CORS preflight admits the identity headers it now reads', async () => {
    const handler = track._test.makeHandler({ getSql: () => fakeSql() });
    const res = makeRes();
    await handler({ method: 'OPTIONS', headers: {} }, res);
    const allow = String(res.headers['access-control-allow-headers'] || '');
    assert.match(allow, /X-Session/i, 'the browser preflight would strip X-Session');
    assert.match(allow, /X-Guest-Id/i, 'the browser preflight would strip X-Guest-Id');
});

// ── 5. WO-1733 — the body-guest fallback, and the wallet asymmetry it must keep ─
//
// THE BUG IT FIXES: the client sets only Content-Type (EventTracker.cs:290-294), so
// with header-only identity EVERY event from EVERY build in players' hands landed
// under `unverified` and the dashboard's COUNT(DISTINCT player_id) read 1.

const GUEST_B = 'guest-local-' + 'b'.repeat(64);

test('a GUEST-shaped body playerId with no headers names the row (the shipped-fleet fix)', async () => {
    const sql = fakeSql();
    const res = await run(sql, makeReq([{ playerId: GUEST, eventName: 'session_start', clientTs: 30 }]));

    assert.equal(res.statusCode, 200);
    assert.deepEqual(insertedPlayerIds(sql), [GUEST],
        'every shipped build still lands in the one `unverified` bucket — the dashboard stays at 1 player');
    assert.equal(insertedProps(sql)[0]._auth, 'guest-body',
        'the body rail is not distinguishable from the header rail — the client fix has no landing signal');
});

test('⛔ a WALLET-shaped body id is STILL refused — a wallet address is public, not a credential', async () => {
    const sql = fakeSql();
    await run(sql, makeReq([{ playerId: WALLET_A, eventName: 'purchase_completed', clientTs: 31 }]));
    assert.deepEqual(insertedPlayerIds(sql), ['unverified'],
        'anyone can now write analytics rows under any player\'s wallet — WO-1506\'s hole is back open');
});

test('a play- shaped body id is refused too (only the guest shape may come from the body)', async () => {
    const sql = fakeSql();
    await run(sql, makeReq([{ playerId: 'play-' + 'c'.repeat(64), eventName: 'session_start', clientTs: 32 }]));
    assert.deepEqual(insertedPlayerIds(sql), ['unverified']);
});

test('the literal "anonymous" (pre-EnsureAccount events) buys nothing', async () => {
    const sql = fakeSql();
    await run(sql, makeReq([{ playerId: 'anonymous', eventName: 'session_start', clientTs: 33 }]));
    assert.deepEqual(insertedPlayerIds(sql), ['unverified']);
});

test('the HEADER wins over the body when both name a guest', async () => {
    const sql = fakeSql();
    await run(sql, makeReq(
        [{ playerId: GUEST_B, eventName: 'session_start', clientTs: 34 }],
        { 'x-guest-id': GUEST },
    ));
    assert.deepEqual(insertedPlayerIds(sql), [GUEST], 'the body overrode a proven header');
    assert.equal(insertedProps(sql)[0]._auth, 'guest');
});

test('a valid SESSION wins over a guest-shaped body id', async () => {
    const sql = fakeSql({ sessionRows: [{ wallet: WALLET_B, revoked: false, expired: false }] });
    await run(sql, makeReq(
        [{ playerId: GUEST, eventName: 'session_start', clientTs: 35 }],
        { 'x-session': 'd'.repeat(48) },
    ));
    assert.deepEqual(insertedPlayerIds(sql), [WALLET_B]);
    assert.equal(insertedProps(sql)[0]._auth, 'session');
});

test('a mixed batch takes the FIRST guest-shaped id, and every row lands under it', async () => {
    const sql = fakeSql();
    await run(sql, makeReq([
        { playerId: 'anonymous', eventName: 'session_start', clientTs: 36 },
        { playerId: GUEST, eventName: 'wave_completed', clientTs: 37 },
    ]));
    assert.deepEqual(insertedPlayerIds(sql), [GUEST, GUEST],
        'events queued before EnsureAccount dragged the whole batch back to unverified');
});

test('GUEST_SAVE_ENABLED=false switches off the BODY rail too, not just the header', async () => {
    const prev = process.env.GUEST_SAVE_ENABLED;
    process.env.GUEST_SAVE_ENABLED = 'false';
    try {
        const sql = fakeSql();
        await run(sql, makeReq([{ playerId: GUEST, eventName: 'session_start', clientTs: 38 }]));
        assert.deepEqual(insertedPlayerIds(sql), ['unverified'],
            'the guest kill switch leaves a second door open through the body');
    } finally {
        if (prev === undefined) delete process.env.GUEST_SAVE_ENABLED;
        else process.env.GUEST_SAVE_ENABLED = prev;
    }
});

test('the BODY guest rail never spends the SAVE budget either', async () => {
    const sql = fakeSql();
    await run(sql, makeReq([{ playerId: GUEST, eventName: 'session_start', clientTs: 39 }]));
    assert.equal(sql.calls.filter((c) => /guest_rate_limit/.test(c.text)).length, 0,
        'analytics is spending the guest save budget — a busy funnel would 429 the player\'s own saves');
});

test('a surplus event past the batch cap can NOT steer the identity of the rows that land', async () => {
    const events = [];
    for (let i = 0; i < 100; i++) events.push({ playerId: 'anonymous', eventName: 'session_start', clientTs: i });
    events.push({ playerId: GUEST, eventName: 'session_start', clientTs: 999 });   // dropped by the cap

    const sql = fakeSql();
    const res = await run(sql, makeReq(events));
    assert.equal(res.body.dropped, 1, 'the cap stopped dropping surplus events');
    assert.equal(insertedPlayerIds(sql)[0], 'unverified',
        'a DROPPED event named the batch — identity was read past the cap');
});
