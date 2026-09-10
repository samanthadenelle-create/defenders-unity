'use strict';

// =============================================================================
// test/heartbound-telemetry.test.js — the oracle for WO-1684 / HEART-011.
//
// It drives the REAL telemetry module, the REAL resonance arithmetic and the REAL
// pulse/state seams with an in-memory recording sql mock (the same shape
// test/heartbound-pulse.test.js:44-56 uses). Nothing here re-implements a bucket,
// a hash or a curve — a fake would agree with a contract the shipped module does
// not have, which is the failure this suite exists to catch.
//
// The cases are WO-1684's acceptance list plus the two things a telemetry lane can
// break that nothing else would notice:
//   A1  the fourteen names are exactly the spec's fourteen
//   A2  no event is emitted from both sides (the owner table is disjoint+complete)
//   A3  no wallet address ever reaches `properties`
//   A4  amounts are bucketed and the edges come from config
//   A5  no Heartbound name is caught by the 7-day web_trace retention sweep
//   +   AN EMIT THAT THROWS CHANGES NOTHING (the instrument must not move what it
//       measures — CLAUDE.md §12 requires the trace, not a side effect)
//   +   the anti-whale curve is measurable, pinned against spec :313
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');

const telemetry = require('../api/_lib/heartbound-telemetry.js');
const resonance = require('../api/_lib/heartbound-resonance.js');
const pulse = require('../api/_lib/heartbound-pulse.js');
const state = require('../api/_lib/heartbound-state.js');

const {
    HEARTBOUND_EVENTS, EVENT_NAMES, EVENT_OWNERS, SERVER_EVENTS, CLIENT_EVENTS,
    DEFAULT_CONFIG, hashPlayerId, stakeBucket, streakBucket, latencyBucket,
    buildEvent, safeEmit, createAnalyticsEmitter,
    whaleSampleFromTransition, aggregateWhaleRatio,
} = telemetry;

// The fourteen, retyped from docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:1036-1064.
// Deliberately a literal list and not derived from the module: a test that reads
// its expectation out of the thing under test proves only that the file is
// self-consistent.
const SPEC_EVENTS = [
    'heartbound_detected',
    'heartbound_activated',
    'skr_verification_success',
    'skr_verification_failed',
    'heart_pulse_global_detected',
    'heart_pulse_player_processed',
    'heart_pulse_player_deferred',
    'resonance_score_changed',
    'resonance_tier_up',
    'resonance_tier_down',
    'echo_event_generated',
    'echo_event_claimed',
    'echo_event_expired',
    'heartbound_screen_opened',
];

const WALLET = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const T0 = Date.parse('2026-09-10T05:00:00.000Z');

function mockSql(script) {
    const calls = [];
    const queue = (script || []).slice();
    const sql = async (strings, ...values) => {
        calls.push({ text: strings.join('?'), values });
        const next = queue.shift();
        if (next && next.throws) throw next.throws;
        return next ? (next.rows || []) : [];
    };
    sql.calls = calls;
    return sql;
}
const rows = (r) => ({ rows: r });

/** A recording emit. Returns the array it appends to, so a case can assert order. */
function recorder() {
    const seen = [];
    const emit = async (event) => { seen.push(event); };
    emit.seen = seen;
    return emit;
}

// -- A1. THE FOURTEEN NAMES ---------------------------------------------------

test('acceptance 1: the event list is exactly the spec\'s fourteen names', () => {
    assert.deepEqual([...EVENT_NAMES].sort(), [...SPEC_EVENTS].sort());
    assert.equal(EVENT_NAMES.length, 14);
    // Every constant resolves to a name in the list (no typo'd alias).
    for (const key of Object.keys(HEARTBOUND_EVENTS)) {
        assert.ok(SPEC_EVENTS.includes(HEARTBOUND_EVENTS[key]), `${key} is not a spec name`);
    }
});

test('an unknown event name is refused rather than shaped', () => {
    assert.throws(() => buildEvent('heartbound_vibes', { playerId: WALLET }), /unknown Heartbound telemetry event/);
    // The near-miss matters more than the nonsense one: a plural, a tense, a dash.
    assert.throws(() => buildEvent('resonance_tier_upgraded', {}), /unknown Heartbound telemetry event/);
});

// -- A2. ONE EMITTER PER EVENT ------------------------------------------------

test('acceptance 2: the server and client sets are DISJOINT and together COMPLETE', () => {
    const overlap = SERVER_EVENTS.filter((n) => CLIENT_EVENTS.includes(n));
    assert.deepEqual(overlap, [], 'an event on both sides is the double-counted funnel D1 forbids');
    assert.deepEqual([...SERVER_EVENTS, ...CLIENT_EVENTS].sort(), [...SPEC_EVENTS].sort());
});

test('acceptance 2: every one of the fourteen names a single owning seam', () => {
    for (const name of SPEC_EVENTS) {
        const owner = EVENT_OWNERS[name];
        assert.ok(owner, `${name} has no owner row`);
        assert.ok(owner.seam && owner.seam.length > 0, `${name} names no seam`);
        assert.ok(owner.side === 'server' || owner.side === 'client', `${name} has no side`);
        assert.equal(typeof owner.emittedByThisLane, 'boolean');
    }
});

test('the two names api/heartbound/status.js already emits are NOT claimed by this lane', () => {
    // status.js:258 and :350 are the live emitters. If a later lane wires the
    // heartbound-state.js seams into that same request path, this is the row that
    // says why the funnel then doubles.
    for (const name of ['skr_verification_success', 'skr_verification_failed']) {
        assert.equal(EVENT_OWNERS[name].emittedByThisLane, false);
        assert.match(EVENT_OWNERS[name].seam, /status\.js/);
    }
});

// -- A3. NO WALLET IN PROPERTIES ----------------------------------------------

test('acceptance 3: properties carry a HASH and never the wallet address', () => {
    const ev = buildEvent(HEARTBOUND_EVENTS.HEART_PULSE_PLAYER_PROCESSED, {
        playerId: WALLET, atMs: T0, tier: 4, effectiveSkr: 1200, actualSkr: 4000,
        continuousPulseCount: 12, pulseId: 43, resonanceScore: 1250,
    });
    // The identity column keeps the raw wallet — ANALYTICS_EXCLUDED_PLAYER_IDS
    // filters on it, and hashing it would hide the owner's own outlier rows.
    assert.equal(ev.identity, WALLET);
    assert.equal(ev.properties.playerHash, hashPlayerId(WALLET));
    assert.notEqual(ev.properties.playerHash, WALLET);
    assert.equal(JSON.stringify(ev.properties).includes(WALLET), false);
});

test('acceptance 3: a wallet smuggled through `extra` REFUSES the whole event', () => {
    assert.throws(() => buildEvent(HEARTBOUND_EVENTS.HEARTBOUND_ACTIVATED, {
        playerId: WALLET, atMs: T0, extra: { walletAddress: WALLET },
    }), /carries the player identity/);
    // Nested, because the guard walking only the top level would be a guard in name.
    assert.throws(() => buildEvent(HEARTBOUND_EVENTS.HEARTBOUND_ACTIVATED, {
        playerId: WALLET, atMs: T0, extra: { chain: { accounts: ['x', WALLET] } },
    }), /carries the player identity/);
});

test('the hash is stable, short and salted apart from the audit IP hash', () => {
    assert.equal(hashPlayerId(WALLET), hashPlayerId(WALLET));
    assert.equal(hashPlayerId(WALLET).length, DEFAULT_CONFIG.playerHashLength);
    assert.notEqual(hashPlayerId(WALLET), hashPlayerId(WALLET + 'x'));
    assert.equal(hashPlayerId(null), null);
    assert.equal(hashPlayerId('   '), null);
    assert.notEqual(DEFAULT_CONFIG.playerHashSalt, 'dotr-audit-ip:v1:5b21e0');
});

// -- A4. BUCKETS, EDGES IN CONFIG ---------------------------------------------

test('acceptance 4: stake buckets come from config and the boundaries are exact', () => {
    assert.equal(stakeBucket(null), 'unknown');
    assert.equal(stakeBucket(0), '0');
    assert.equal(stakeBucket(1), '1-99');
    assert.equal(stakeBucket(99), '1-99');
    assert.equal(stakeBucket(100), '100-999');   // belowMax is EXCLUSIVE
    assert.equal(stakeBucket(999), '100-999');
    assert.equal(stakeBucket(1000), '1k-9k');
    assert.equal(stakeBucket(10000), '10k-99k');
    assert.equal(stakeBucket(100000), '100k-999k');
    assert.equal(stakeBucket(1000000), '1m+');
    assert.equal(stakeBucket(Number.POSITIVE_INFINITY), 'unknown');
    // The labels are the ladder api/heartbound/status.js:400-408 already ships.
    // Two vocabularies in one column would make every chart seam-dependent.
    const labels = DEFAULT_CONFIG.stakeBuckets.rows.map((r) => r.label);
    assert.deepEqual(labels, ['1-99', '100-999', '1k-9k', '10k-99k', '100k-999k']);
});

test('acceptance 4: streak and latency buckets are config-driven too', () => {
    assert.equal(streakBucket(0), '0');
    assert.equal(streakBucket(6), '1-6');
    assert.equal(streakBucket(7), '7-29');
    assert.equal(streakBucket(365), '365+');
    assert.equal(latencyBucket(99), '<100ms');
    assert.equal(latencyBucket(100), '100-499ms');
    assert.equal(latencyBucket(60000), '10s+');
    // An injected config overrides — the module holds no literal edge.
    const cfg = JSON.parse(JSON.stringify(DEFAULT_CONFIG));
    cfg.stakeBuckets.rows = [{ belowMax: 5, label: 'tiny' }];
    cfg.stakeBuckets.overflow = 'huge';
    assert.equal(stakeBucket(4, cfg), 'tiny');
    assert.equal(stakeBucket(5, cfg), 'huge');
});

test('acceptance 4: buildEvent emits BUCKETS, never a raw amount', () => {
    const ev = buildEvent(HEARTBOUND_EVENTS.RESONANCE_TIER_UP, {
        playerId: WALLET, atMs: T0, effectiveSkr: 512345, actualSkr: 999999,
        continuousPulseCount: 40, tier: 7, previousTier: 6,
    });
    assert.equal(ev.properties.effectiveStakeBucket, '100k-999k');
    assert.equal(ev.properties.actualStakeBucket, '100k-999k');
    assert.equal(ev.properties.streakBucket, '30-89');
    const values = JSON.stringify(ev.properties);
    assert.equal(values.includes('512345'), false, 'an exact effective stake must not reach analytics_events');
    assert.equal(values.includes('999999'), false, 'an exact actual stake must not reach analytics_events');
    // The tier and the score are NOT amounts and stay exact — they are the axes
    // questions 3 and 9 are asked on.
    assert.equal(ev.properties.tier, 7);
    assert.equal(ev.properties.previousTier, 6);
});

// -- A5. THE RETENTION SWEEP --------------------------------------------------

test('acceptance 5: no Heartbound event is named into the 7-day web_trace sweep', () => {
    // api/admin/cleanup.js:79-85 prunes analytics_events WHERE event_name =
    // 'web_trace' at RETENTION_DAYS = 7. A retention question (#2, #7) asked over
    // a swept table answers confidently and wrongly.
    // There are TWO swept names, not one: cleanup.js:83 ('web_trace') and
    // cleanup.js:121 ('api_auth_reject'), both at RETENTION_DAYS = 7 (:35).
    // Read at source 2026-09-10; a test that knew only about the first would pass
    // while a Heartbound row was being deleted weekly by the second.
    const SWEPT = ['web_trace', 'api_auth_reject'];
    for (const name of EVENT_NAMES) {
        for (const swept of SWEPT) {
            assert.notEqual(name, swept);
            assert.equal(name.includes(swept), false);
        }
    }
});

// -- THE SINK -----------------------------------------------------------------

test('the emitter writes through the sink the repo already has (logApiEvent), not a new table', async () => {
    const seen = [];
    const emit = createAnalyticsEmitter({
        sql: 'SQL',
        logApiEvent: async (sql, identity, name, props) => { seen.push({ sql, identity, name, props }); },
    });
    await emit(buildEvent(HEARTBOUND_EVENTS.HEART_PULSE_GLOBAL_DETECTED, { playerId: null, atMs: T0, pulseId: 9 }));
    assert.equal(seen.length, 1);
    assert.equal(seen[0].sql, 'SQL');
    assert.equal(seen[0].identity, null);       // audit.js:120 maps null to 'anonymous'
    assert.equal(seen[0].name, 'heart_pulse_global_detected');
    assert.equal(seen[0].props.pulseId, '9');
});

test('safeEmit swallows a throwing sink and returns undefined', async () => {
    const boom = () => { throw new Error('sink down'); };
    assert.equal(await safeEmit(boom, { name: 'x' }), undefined);
    assert.equal(await safeEmit(async () => { throw new Error('async sink down'); }, { name: 'x' }), undefined);
    assert.equal(await safeEmit(null, { name: 'x' }), undefined);
});

// -- THE PULSE SEAMS ----------------------------------------------------------

const lastPulseRow = (price, atMs, seq = 7) => rows([{
    pulse_id: 42, sequence_number: seq, new_share_price: String(price),
    detected_at_utc: new Date(atMs).toISOString(), status: pulse.PULSE_STATUS.COMPLETE,
}]);
const mintedRow = () => rows([{
    pulse_id: 43, sequence_number: 8, previous_share_price: '1000', new_share_price: '1100',
    detected_at_utc: new Date(T0).toISOString(), status: pulse.PULSE_STATUS.MINTED,
}]);
const DAY = 24 * 60 * 60 * 1000;
const SKR_UNIT = 1000000n;

test('detectPulse emits heart_pulse_global_detected ONLY on a won mint', async () => {
    const emit = recorder();
    const out = await pulse.detectPulse({
        sql: mockSql([lastPulseRow(1000n, T0 - 2 * DAY), mintedRow()]),
        readSharePrice: async () => ({ rawU128: 1100n, sourceSlot: 900 }),
        now: T0, emit,
    });
    assert.equal(out.minted, true);
    assert.deepEqual(emit.seen.map((e) => e.name), ['heart_pulse_global_detected']);
    assert.equal(emit.seen[0].identity, null, 'a global pulse belongs to no player');
    assert.equal(emit.seen[0].properties.pulseId, '43');
    assert.equal(emit.seen[0].properties.sequenceNumber, 8);
});

test('detectPulse emits NOTHING for a baseline, a non-advancement or a lost race', async () => {
    const baseline = recorder();
    await pulse.detectPulse({
        sql: mockSql([rows([]), rows([{ pulse_id: 1, sequence_number: 0, status: 'BASELINE' }])]),
        readSharePrice: async () => ({ rawU128: 1000n }), now: T0, emit: baseline,
    });
    assert.deepEqual(baseline.seen, [], 'a BASELINE row is bookkeeping, not a pulse');

    const flat = recorder();
    await pulse.detectPulse({
        sql: mockSql([lastPulseRow(1000n, T0 - 2 * DAY)]),
        readSharePrice: async () => ({ rawU128: 1000n }), now: T0, emit: flat,
    });
    assert.deepEqual(flat.seen, []);

    const raced = recorder();
    await pulse.detectPulse({
        sql: mockSql([lastPulseRow(1000n, T0 - 2 * DAY), rows([])]),  // insert absorbed
        readSharePrice: async () => ({ rawU128: 1100n }), now: T0, emit: raced,
    });
    assert.deepEqual(raced.seen, [], 'the losing side of the ON CONFLICT race emits nothing');
});

function playerDeps(overrides) {
    return Object.assign({
        pulse: { pulse_id: 43, sequence_number: 8, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: WALLET, walletAddress: WALLET,
            activatedAtUtc: new Date(T0 - 30 * DAY).toISOString(),
            lastVerifiedAtUtc: new Date(T0 - DAY).toISOString(),
            lastActualStake: 0, effectiveResonatingStake: 0,
            continuousPulseCount: 0, totalLifetimePulses: 0, highestLifetimeTier: 0,
        },
        readStake: async () => ({ rawU128: 4000n * SKR_UNIT }),
        persistPlayerState: async () => {},
        now: T0,
    }, overrides);
}

test('a granted pulse emits processed + score_changed + tier_up, in that order, once', async () => {
    const emit = recorder();
    const out = await pulse.processPlayerForPulse(playerDeps({
        sql: mockSql([rows([{ player_id: WALLET, global_pulse_id: 43, status: 'GRANTED' }])]),
        emit,
    }));
    assert.equal(out.granted, true);
    assert.deepEqual(emit.seen.map((e) => e.name),
        ['heart_pulse_player_processed', 'resonance_score_changed', 'resonance_tier_up']);
    const up = emit.seen[2];
    assert.equal(up.properties.previousTier, 0);
    assert.equal(up.properties.tier, out.resonanceTier);
    assert.equal(up.properties.previousScore, 0);
    assert.ok(up.properties.effectiveStakeBucket, 'the transition carries a bucketed stake');
    assert.equal(JSON.stringify(up.properties).includes(WALLET), false);
});

test('a LOST grant race emits nothing at all', async () => {
    const emit = recorder();
    const out = await pulse.processPlayerForPulse(playerDeps({
        // insert absorbed, and the existing row is already GRANTED
        sql: mockSql([rows([]), rows([{ status: 'GRANTED' }])]),
        emit,
    }));
    assert.equal(out.reason, pulse.SKIP.ALREADY_GRANTED);
    assert.deepEqual(emit.seen, [], 'a duplicate run must not double-count the funnel');
});

test('an unreadable stake emits heart_pulse_player_deferred with the reason, and no grant events', async () => {
    const emit = recorder();
    const out = await pulse.processPlayerForPulse(playerDeps({
        sql: mockSql([rows([])]),
        readStake: async () => { throw new Error('rpc timeout for ' + WALLET); },
        emit,
    }));
    assert.equal(out.granted, false);
    assert.deepEqual(emit.seen.map((e) => e.name), ['heart_pulse_player_deferred']);
    assert.equal(emit.seen[0].properties.reason, pulse.SKIP.PENDING_RPC);
    // ⭐ The provider's error text echoed the wallet back. It must not ride into
    // the row — this is exactly why the seam carries `errorKind`, not `error`.
    assert.equal(JSON.stringify(emit.seen[0].properties).includes(WALLET), false);
    assert.equal(emit.seen[0].properties.errorKind, 'read_failed');
});

test('a pulse below the eligibility floor or before activation emits nothing', async () => {
    const low = recorder();
    await pulse.processPlayerForPulse(playerDeps({
        sql: mockSql([]), readStake: async () => ({ rawU128: 10n * SKR_UNIT }), emit: low,
    }));
    assert.deepEqual(low.seen, [], 'a sub-floor holder is not a Heartbound player');

    const early = recorder();
    const deps = playerDeps({ sql: mockSql([]), emit: early });
    deps.state = { ...deps.state, activatedAtUtc: new Date(T0 + DAY).toISOString() };
    await pulse.processPlayerForPulse(deps);
    assert.deepEqual(early.seen, []);
});

// -- THE INSTRUMENT MUST NOT MOVE WHAT IT MEASURES ---------------------------

test('a THROWING emit leaves every pulse return value byte-identical to no emit', async () => {
    const script = () => [rows([{ player_id: WALLET, global_pulse_id: 43, status: 'GRANTED' }])];
    const quiet = await pulse.processPlayerForPulse(playerDeps({ sql: mockSql(script()) }));
    const noisy = await pulse.processPlayerForPulse(playerDeps({
        sql: mockSql(script()),
        emit: () => { throw new Error('sink exploded'); },
    }));
    assert.deepEqual(noisy, quiet);
});

test('a THROWING emit leaves every state.js return value byte-identical to no emit', async () => {
    const row = () => rows([{
        player_id: WALLET, status: 'ACTIVE', activated_at_utc: new Date(T0).toISOString(),
        last_verified_at_utc: new Date(T0).toISOString(), last_actual_stake: '4000',
        effective_resonating_stake: '1000', continuous_pulse_count: 3, total_lifetime_pulses: 3,
        resonance_score: '800', resonance_tier: 2, highest_lifetime_tier: 2,
        current_tree_resonance_stage: 2, pending_echo_events: [], version: 4,
    }]);
    const boom = () => { throw new Error('sink exploded'); };

    assert.deepEqual(
        await state.activateHeartbound(mockSql([row()]), WALLET, { emit: boom }),
        await state.activateHeartbound(mockSql([row()]), WALLET));

    assert.deepEqual(
        await state.recordVerificationFailure(mockSql([row()]), WALLET, { code: 'RPC_UNAVAILABLE', emit: boom }),
        await state.recordVerificationFailure(mockSql([row()]), WALLET, { code: 'RPC_UNAVAILABLE' }));

    const stake = { lastActualStake: '4000000000', effectiveResonatingStake: '1000000000' };
    assert.deepEqual(
        await state.recordVerifiedStake(mockSql([row()]), WALLET, { ...stake, emit: boom }),
        await state.recordVerifiedStake(mockSql([row()]), WALLET, stake));
});

// -- THE STATE SEAMS ----------------------------------------------------------

test('activateHeartbound emits heartbound_activated; a failure emits skr_verification_failed even with no row', async () => {
    const activated = recorder();
    await state.activateHeartbound(mockSql([rows([{
        player_id: WALLET, status: 'ACTIVE', activated_at_utc: new Date(T0).toISOString(),
        resonance_tier: 0, continuous_pulse_count: 0, version: 0, pending_echo_events: [],
    }])]), WALLET, { emit: activated, nowMs: T0 });
    assert.deepEqual(activated.seen.map((e) => e.name), ['heartbound_activated']);
    assert.equal(activated.seen[0].identity, WALLET);
    assert.equal(JSON.stringify(activated.seen[0].properties).includes(WALLET), false);

    // ⛔ Zero rows updated STILL emits: "verification is failing for players we
    // have never verified" is a real finding and must not be invisible.
    const failed = recorder();
    const out = await state.recordVerificationFailure(mockSql([rows([])]), WALLET, {
        code: 'RPC_UNAVAILABLE', emit: failed, nowMs: T0,
    });
    assert.equal(out, null);
    assert.deepEqual(failed.seen.map((e) => e.name), ['skr_verification_failed']);
    assert.equal(failed.seen[0].properties.matched, false);
    assert.equal(failed.seen[0].properties.reason, 'RPC_UNAVAILABLE');
});

test('the state.js verification events carry NO stake bucket while the unit is ambiguous', async () => {
    // heartbound-state stores u128 base units; heartbound-pulse persists whole SKR
    // through the same column. Bucketing across both would file every base-unit
    // row as "1m+" — an instrument manufacturing the whale signal the ceiling is
    // meant to test. Pinned so nobody "completes" the event by adding one.
    const emit = recorder();
    await state.recordVerifiedStake(mockSql([rows([{
        player_id: WALLET, status: 'ACTIVE', last_actual_stake: '4000000000',
        effective_resonating_stake: '1000000000', resonance_score: '900', resonance_tier: 3,
        continuous_pulse_count: 5, total_lifetime_pulses: 5, highest_lifetime_tier: 3,
        current_tree_resonance_stage: 3, pending_echo_events: [], version: 2,
    }])]), WALLET, { lastActualStake: '4000000000', effectiveResonatingStake: '1000000000', emit, nowMs: T0 });
    assert.deepEqual(emit.seen.map((e) => e.name), ['skr_verification_success']);
    assert.equal(emit.seen[0].properties.effectiveStakeBucket, undefined);
    assert.equal(emit.seen[0].properties.actualStakeBucket, undefined);
    assert.equal(emit.seen[0].properties.tier, 3);
});

// -- QUESTION 9: THE ANTI-WHALE CURVE, MEASURED ------------------------------

/** Drive the REAL resonance module: activate, then pulse `n` times at `skr`. */
function realTransition(skr, pulses) {
    let s = resonance.activate(skr);
    for (let i = 0; i < pulses; i++) s = resonance.applyPulse(s, skr);
    return whaleSampleFromTransition(s, resonance.evaluate(s));
}

test('question 9: the curve is measurably SUBLINEAR — spec :313, through the real arithmetic', () => {
    // "500,000 SKR does NOT provide 500 times the power of 1,000 SKR."
    const small = realTransition(1000, 12);
    const whale = realTransition(500000, 12);
    const agg = aggregateWhaleRatio([small, whale]);

    assert.ok(agg.stakeRatio > 100, 'the whale really does hold hundreds of times the stake');
    assert.ok(agg.scoreRatio < 5, `a 500x stake must not buy 500x the score (got ${agg.scoreRatio})`);
    assert.ok(agg.advantageConcentration < 0.05,
        `advantage per SKR must collapse as stake rises (got ${agg.advantageConcentration})`);
    assert.equal(agg.sublinear, true);
    assert.equal(agg.n, 2);
    // Advantage per unit staked FALLS as stake rises — the anti-whale property,
    // stated as an inequality rather than a hope.
    assert.ok(whale.scorePerSkr < small.scorePerSkr);
});

test('question 9: a population spanning no stake range reports null, not a verdict', () => {
    const one = aggregateWhaleRatio([realTransition(1000, 5)]);
    assert.equal(one.sublinear, null, 'one bucket demonstrates nothing about the curve');
    assert.equal(aggregateWhaleRatio([]).n, 0);
    assert.equal(aggregateWhaleRatio([]).advantageConcentration, null);
});

test('question 9: the YIELD half stays null until a tierBenefit is injected (Q-METER)', () => {
    const samples = [realTransition(1000, 12), realTransition(500000, 12)];
    assert.equal(aggregateWhaleRatio(samples).benefitConcentration, null,
        'no benefit number is hardcoded here — heartbound-tiers.js owns the ladder');
    const withBenefit = aggregateWhaleRatio(samples, { tierBenefit: (tier) => tier * 2 });
    assert.ok(withBenefit.benefitRatio > 1);
    assert.ok(withBenefit.benefitConcentration < 1,
        'yield advantage must also grow more slowly than stake');
});

test('the whale sample derives from the transition and re-implements no arithmetic', () => {
    const s = resonance.applyPulse(resonance.activate(4000), 4000);
    const view = resonance.evaluate(s);
    const sample = whaleSampleFromTransition(s, view);
    assert.equal(sample.resonanceScore, view.resonanceScore);
    assert.equal(sample.tier, view.tier);
    assert.equal(sample.effectiveSkr, s.effectiveSkr);
    assert.equal(sample.stakeBucket, stakeBucket(s.effectiveSkr));
    assert.equal(sample.scorePerSkr, view.resonanceScore / s.effectiveSkr);
});
