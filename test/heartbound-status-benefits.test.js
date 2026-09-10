// =============================================================================
// test/heartbound-status-benefits.test.js — WO-1679 / HEART-006 + WO-1682.
// THE WIRE SHAPE OF GET /api/heartbound/status: `benefits` + `nextTierAt`.
// -----------------------------------------------------------------------------
// WHY THIS FILE EXISTS AND WHY IT DRIVES THE REAL HANDLER.
//
// The benefit table and the meter are unit-tested next door
// (test/heartbound-tiers.test.js, 26 cases). What THOSE cannot prove is that the
// ENDPOINT actually calls them — and an unwired seam is the failure mode this
// feature area has already produced once: WO-1682 §0c names three gates cited in
// code as live protection that had never existed. A composition test that
// re-implements what the route does would have been a fourth version of that
// mistake, so this file requires the ACTUAL handler and asserts on the ACTUAL
// response object.
//
// ⛔ AND IT PINS THE THING THAT WOULD HAVE BEEN SILENT. api/_lib/heartbound-state.js
// does `String(resolved.nextTierAt)`, while heartbound-resonance.js's own
// `nextTierAt()` returns an OBJECT. Injecting the resonance function directly puts
// the literal string "[object Object]" on the wire, with no error, in a field a
// panel renders as a number. Case 3 below asserts the served value parses as a
// number and is the real next threshold.
//
// HOW THE DEPENDENCIES ARE MOCKED: the module cache is primed BEFORE the handler is
// required, the same trick the repo's other endpoint suites use, and the SQL mock
// routes on the query TEXT (the way test/heartbound-state.test.js's mockSql records
// it) so one fake client can serve two different tables honestly.
//
//     node --test test/heartbound-status-benefits.test.js
//
// Zero network, zero database, zero Unity.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const tiers = require('../api/_lib/heartbound-tiers');

// ── the heartbound_state row (shape copied from test/heartbound-state.test.js) ──
// resonance_score 1234.5 -> Tier IV (Echoing); the next threshold is Tier V at 1500.
const STATE_ROW = Object.freeze({
    player_id: 'WalletAAA',
    status: 'ACTIVE',
    activated_at_utc: '2026-09-01T00:00:00.000Z',
    last_verified_at_utc: '2026-09-10T12:00:00.000Z',
    last_actual_stake: '1000000000000',
    effective_resonating_stake: '750000000000',
    last_verification_attempt_at_utc: null,
    last_verification_code: null,
    stale_since_utc: null,
    continuous_pulse_count: 4,
    total_lifetime_pulses: 11,
    last_global_pulse_id: 'pulse-99',
    last_player_pulse_id: 'pulse-99',
    resonance_score: '1234.500000',
    resonance_tier: 4,
    highest_lifetime_tier: 5,
    current_tree_resonance_stage: 2,
    pending_echo_events: [],
    last_echo_event_id: null,
    version: 7,
});

function stakeSnapshotRow(nowSeconds) {
    const verified = new Date((nowSeconds - 30) * 1000).toISOString();
    return {
        player_id: 'WalletAAA',
        wallet_address: 'WalletAAA',
        guardian_pool: 'PoolAAA',
        user_stake_address: 'StakeAAA',
        shares_raw: '1000000',
        share_price_raw: '1000000000',
        active_staked_raw: '1000000000000',
        unstaking_raw: '0',
        unstake_timestamp: null,
        cooldown_seconds: null,
        unstaking_ready: false,
        source_slot: 42,
        verification_status: 'VERIFIED',
        error_code: null,
        verified_at: verified,
        last_attempt_at: verified,
    };
}

/**
 * A fake neon client that answers by TABLE. Returning one row list for every query
 * would have let a test pass while the route read the wrong table.
 */
function mockSql(stateRows, snapshotRows) {
    const calls = [];
    const sql = async (strings) => {
        const text = strings.join('?');
        calls.push(text);
        if (text.indexOf('skr_stake_snapshots') >= 0) return snapshotRows;
        if (text.indexOf('heartbound_state') >= 0) return stateRows;
        return [];
    };
    sql.calls = calls;
    sql.sawTable = (name) => calls.some((t) => t.indexOf(name) >= 0);
    return sql;
}

/** Prime the module cache with a stub, addressed by the SAME resolved path the handler uses. */
function stub(request, exports) {
    const resolved = require.resolve(request);
    require.cache[resolved] = { id: resolved, filename: resolved, loaded: true, exports: exports };
}

/**
 * Load api/heartbound/status.js with its dependencies stubbed.
 * `auth` decides what authenticate() returns, which is how the guest rail is reached.
 */
function loadHandler({ auth, stateRows, snapshotRows }) {
    const HANDLER = path.join(__dirname, '..', 'api', 'heartbound', 'status.js');
    const sql = mockSql(stateRows, snapshotRows);

    stub('@neondatabase/serverless', { neon: () => sql });
    stub('../api/_lib/audit', { logAuthReject: async () => {}, logApiEvent: async () => {} });
    stub('../api/_lib/wallet-auth', {
        AuthCode: { METHOD_NOT_ALLOWED: 'METHOD_NOT_ALLOWED', PLAYER_ID_MISSING: 'PLAYER_ID_MISSING',
                    SERVER_ERROR: 'SERVER_ERROR', PLAYER_ID_BAD_SHAPE: 'PLAYER_ID_BAD_SHAPE',
                    WALLET_MALFORMED: 'WALLET_MALFORMED' },
        authenticate: async () => auth,
        isWalletId: () => auth.mode === 'wallet',
    });
    stub('../api/_lib/http', {
        applyCors: () => false,
        newRef: () => 'ref-test',
        quietFail: (res, status, code) => res.status(status).json({ ok: false, code: code }),
    });

    delete require.cache[require.resolve(HANDLER)];
    return { handler: require(HANDLER), sql: sql };
}

/** Minimal express-shaped response recorder. */
function recorder() {
    const out = { statusCode: 0, body: null };
    return {
        out: out,
        status(code) { out.statusCode = code; return this; },
        json(payload) { out.body = payload; return this; },
        setHeader() {}, end() {},
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE SEAM IS WIRED AT ALL
// ═══════════════════════════════════════════════════════════════════════════

test('the route requires the tier module and injects it as heartbound-state\'s resolver', () => {
    const fs = require('node:fs');
    const src = fs.readFileSync(path.join(__dirname, '..', 'api', 'heartbound', 'status.js'), 'utf8');
    assert.match(src, /require\('\.\.\/_lib\/heartbound-tiers'\)/);
    assert.match(src, /resolveTier:\s*tiers\.resolveTier/,
        'the ladder must be INJECTED into readHeartboundStatus - the state module holds none ' +
        'and must never be given one');
    assert.match(src, /tiers\.statusBenefitsPayload\(/);
    // ⛔ And the route holds no ladder of its own.
    assert.equal(src.indexOf('minScore'), -1, 'status.js names minScore - the thresholds live in ' +
        'api/_lib/heartbound-resonance-config.json and nowhere else');
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. A STAKED WALLET GETS BOTH FIELDS, WITH REAL VALUES
// ═══════════════════════════════════════════════════════════════════════════

test('a verified wallet response carries `benefits` AND `nextTierAt`', async () => {
    const nowSeconds = Math.floor(Date.now() / 1000);
    const { handler, sql } = loadHandler({
        auth: { ok: true, identity: 'WalletAAA', mode: 'wallet' },
        stateRows: [STATE_ROW],
        snapshotRows: [stakeSnapshotRow(nowSeconds)],
    });

    const res = recorder();
    await handler({ method: 'GET', query: { playerId: 'WalletAAA' }, headers: {} }, res);

    assert.equal(res.out.statusCode, 200);
    const body = res.out.body;

    // The route actually went to BOTH tables - not one standing in for the other.
    assert.ok(sql.sawTable('skr_stake_snapshots'), 'the stake snapshot was never read');
    assert.ok(sql.sawTable('heartbound_state'), 'the heartbound row was never read - the tier ' +
        'block would be absent and every passive would silently stay at zero');

    assert.ok(body.benefits, 'no benefits block on the wire');
    assert.equal(body.benefits.tier, 4);
    assert.equal(body.heartboundStatus, 'ACTIVE');

    // Tier IV cumulative: Tiers I + II production rows = 2% + 2%.
    assert.equal(body.benefits.productionRateSum, 0.04);
    assert.equal(body.benefits.productionRateCeiling, 0.10);
    assert.deepEqual(body.benefits.polish, { extraWeeklyRerolls: 1, rollCapDelta: 0 });
    assert.ok(body.benefits.rows.some((r) => r.id === 'scouts_whisper'), 'Tier IV row missing');

    // And the payload the client applies matches the module's own answer exactly.
    assert.deepEqual(body.benefits, tiers.statusBenefitsPayload(4));
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. ⛔ nextTierAt IS A NUMBER ON THE WIRE, NOT "[object Object]"
// ═══════════════════════════════════════════════════════════════════════════

test('nextTierAt is the next THRESHOLD, and it survives heartbound-state\'s String()', async () => {
    const nowSeconds = Math.floor(Date.now() / 1000);
    const { handler } = loadHandler({
        auth: { ok: true, identity: 'WalletAAA', mode: 'wallet' },
        stateRows: [STATE_ROW],
        snapshotRows: [stakeSnapshotRow(nowSeconds)],
    });

    const res = recorder();
    await handler({ method: 'GET', query: { playerId: 'WalletAAA' }, headers: {} }, res);
    const served = res.out.body.nextTierAt;

    assert.notEqual(served, '[object Object]',
        'heartbound-state.js does String(resolved.nextTierAt). Injecting ' +
        'heartbound-resonance.nextTierAt directly - which returns an OBJECT - serialises this ' +
        'and NOTHING ERRORS. That is the silent wrong answer this case exists to catch.');
    assert.ok(/^\d+$/.test(String(served)), 'nextTierAt must read as a plain number, got ' + served);

    // Score 1234.5 -> the next threshold is Tier V's, taken from the LADDER, not typed here.
    const resonance = require('../api/_lib/heartbound-resonance');
    const expected = resonance.nextTierAt(1234.5);
    assert.equal(Number(served), expected.minScore);
    assert.equal(expected.tier, 5);
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE GUEST RAIL — tier 0 as a FACT, not as ignorance
// ═══════════════════════════════════════════════════════════════════════════

test('a rail with no wallet is served the ZERO benefit block, explicitly', async () => {
    const { handler } = loadHandler({
        auth: { ok: true, identity: 'guest-local-1', mode: 'guest' },
        stateRows: [],
        snapshotRows: [],
    });

    const res = recorder();
    await handler({ method: 'GET', query: { playerId: 'guest-local-1' }, headers: {} }, res);

    assert.equal(res.out.statusCode, 200);
    assert.deepEqual(res.out.body.benefits, tiers.statusBenefitsPayload(0));
    assert.equal(res.out.body.benefits.tier, 0);
    assert.deepEqual(res.out.body.benefits.rows, []);
    assert.equal(res.out.body.nextTierAt, null);
    // Heartbound is Seeker-only (owner ruling 13:10 Q3): this rail has no wallet and no way to
    // attach one, so tier 0 is a FACT here and the client may safely stand everything down.
    assert.equal(res.out.body.heartboundStatus, 'DORMANT');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. ⛔ AN UNREADABLE HEARTBOUND ROW OMITS THE BLOCK — IT NEVER SENDS A ZERO
// ═══════════════════════════════════════════════════════════════════════════

test('a failing heartbound read DEGRADES the response, it does not deny or zero it', async () => {
    const nowSeconds = Math.floor(Date.now() / 1000);
    const snapshotRows = [stakeSnapshotRow(nowSeconds)];

    // A client whose heartbound_state query throws (missing table, timeout).
    const sql = async (strings) => {
        const text = strings.join('?');
        if (text.indexOf('skr_stake_snapshots') >= 0) return snapshotRows;
        throw new Error('relation "heartbound_state" does not exist');
    };
    stub('@neondatabase/serverless', { neon: () => sql });
    stub('../api/_lib/audit', { logAuthReject: async () => {}, logApiEvent: async () => {} });
    stub('../api/_lib/wallet-auth', {
        AuthCode: { METHOD_NOT_ALLOWED: 'x', PLAYER_ID_MISSING: 'x', SERVER_ERROR: 'x',
                    PLAYER_ID_BAD_SHAPE: 'x', WALLET_MALFORMED: 'x' },
        authenticate: async () => ({ ok: true, identity: 'WalletAAA', mode: 'wallet' }),
        isWalletId: () => true,
    });
    stub('../api/_lib/http', {
        applyCors: () => false, newRef: () => 'ref-test',
        quietFail: (res, s, c) => res.status(s).json({ ok: false, code: c }),
    });
    const HANDLER = path.join(__dirname, '..', 'api', 'heartbound', 'status.js');
    delete require.cache[require.resolve(HANDLER)];
    const handler = require(HANDLER);

    const res = recorder();
    await handler({ method: 'GET', query: { playerId: 'WalletAAA' }, headers: {} }, res);

    // The stake answer is still served...
    assert.equal(res.out.statusCode, 200, 'a Heartbound read failure must not 500 the whole route');
    assert.ok(res.out.body.snapshot, 'the stake answer is useful on its own and must survive');

    // ...and the tier block is ABSENT, not zeroed.
    assert.equal('benefits' in res.out.body, false,
        'a transient database failure must NOT emit a tier-0 benefits block. The client holds ' +
        'its previous answer on an absent block; a zero block would stand every passive down ' +
        'on a database blip, which is the fail-to-zero mistake the Q2 ruling forbids.');
    assert.equal('nextTierAt' in res.out.body, false);
});
