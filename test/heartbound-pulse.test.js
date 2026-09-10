'use strict';

// =============================================================================
// test/heartbound-pulse.test.js — the oracle for WO-1677 / HEART-004.
//
// It drives the REAL detector functions with an in-memory recording sql mock
// (the shape test/benefactors.test.js:52-64 already uses), so every query the
// job issues is both driven and inspected. Nothing here re-implements the job.
//
// The cases are WO-1677's acceptance list, one test per line, plus the owner
// rulings of 2026-09-10 that the WO predates:
//   1. exactly one pulse per unique observed advancement (run twice → one mint)
//   2. a duplicate job cannot double-pay (PK absorbed → "already granted")
//   3. an RPC timeout leaves the streak intact and produces no reward
//   4. a player offline for the grace window is capped at 5 catch-up pulses
//   5. no pulse dated before activated_at_utc is processed
//   Q-CADENCE: at most one pulse per day, even on a real advancement
//   Q1:        the stake seam is UNWIRED by default and says so
//   Q2:        RPC failure never zeroes and never resets — no state write at all
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const {
    MAX_PULSES_PER_UTC_DAY,
    utcDayKey,
    MAX_CATCHUP_PULSES,
    STALE_GRACE_MS,
    GRANT_STATUS,
    PULSE_STATUS,
    SKIP,
    detectPulse,
    processPlayerForPulse,
    selectCatchupPulses,
    runHeartPulseDetector,
} = require('../api/_lib/heartbound-pulse.js');

const REPO = path.join(__dirname, '..');
const readSrc = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');

// A recording tagged-template sql() answering from a queued script.
function mockSql(script) {
    const calls = [];
    const queue = (script || []).slice();
    const sql = async (strings, ...values) => {
        const text = strings.join('?');
        calls.push({ text, values });
        const next = queue.shift();
        if (next && next.throws) throw next.throws;
        return next ? (next.rows || []) : [];
    };
    sql.calls = calls;
    return sql;
}

const rows = (r) => ({ rows: r });
const T0 = Date.parse('2026-09-10T05:00:00.000Z');
const DAY = 24 * 60 * 60 * 1000;

// ⭐ THE REAL HEART-003 MODULE, not a stand-in. It landed on dev at cde1c1f63 and
// it is PURE (no DB, no network), so the pulse job is exercised against the
// arithmetic that will actually run in production — a fake here could agree with
// a contract the real module does not have, which is the whole failure mode this
// reconciliation existed to close. The curve itself is pinned by that lane's own
// suite (test/heartbound-resonance.test.js); this suite pins the SEQUENCING.
const resonance = require('../api/_lib/heartbound-resonance.js');

// Stakes are expressed in CHAIN BASE UNITS (1 SKR = 1e6, config chain.skrBaseUnits),
// because that is what a HEART-001 reading carries. 4,000 SKR clears the 100 SKR floor.
const SKR = 1000000n;
const stakeReading = (skr) => ({ rawU128: BigInt(skr) * SKR });

const lastPulseRow = (price, atMs, seq = 7) => rows([{
    pulse_id: 42,
    sequence_number: seq,
    new_share_price: String(price),
    detected_at_utc: new Date(atMs).toISOString(),
    status: PULSE_STATUS.COMPLETE,
}]);

// runHeartPulseDetector always asks these two after detection: the unfinished
// pulses to resume, then the PENDING debts to sweep. An empty pair means
// "nothing to retry", which is the quiet-day script.
const NO_RETRY_WORK = [rows([]), rows([])];

const mintedRow = (seq = 8) => rows([{
    pulse_id: 43,
    sequence_number: seq,
    previous_share_price: '1000',
    new_share_price: '1100',
    detected_at_utc: new Date(T0).toISOString(),
    status: PULSE_STATUS.MINTED,
}]);

// -- 1. EXACTLY ONE PULSE PER UNIQUE OBSERVED ADVANCEMENT ---------------------

test('acceptance 1: a real advancement past the cadence floor mints exactly one pulse', async () => {
    const sql = mockSql([lastPulseRow(1000n, T0 - 2 * DAY), mintedRow()]);
    const out = await detectPulse({
        sql,
        readSharePrice: async () => ({ rawU128: 1100n, sourceSlot: 900, chainReference: 'StakeConfig' }),
        now: T0,
    });
    assert.equal(out.minted, true);
    assert.equal(out.pulse.pulse_id, 43);

    // The INSERT carries the PREVIOUS price and the NEW one as strings — a u128
    // must never ride through a JS number. NUMERIC(39,0) is the column.
    const insert = sql.calls.find((c) => c.text.includes('INSERT INTO global_heart_pulse'));
    assert.ok(insert, 'the detector must INSERT the pulse');
    assert.ok(insert.values.includes('1000'), 'previous_share_price is bound as a string');
    assert.ok(insert.values.includes('1100'), 'new_share_price is bound as a string');
    assert.ok(insert.text.includes('ON CONFLICT'), 'the mint is guarded by the unique index');
});

test('acceptance 1: running the detector twice over the SAME chain state mints nothing the second time', async () => {
    // Second run: the recorded price already equals what the chain reports.
    const sql = mockSql([lastPulseRow(1100n, T0 - 2 * DAY)]);
    const out = await detectPulse({ sql, readSharePrice: async () => ({ rawU128: 1100n }), now: T0 });
    assert.equal(out.minted, false);
    assert.equal(out.reason, SKIP.NO_ADVANCEMENT);
    assert.equal(sql.calls.filter((c) => c.text.includes('INSERT')).length, 0,
        'no INSERT may be issued when the price did not advance');
});

test('acceptance 1: a lost mint race is absorbed by the unique index, not by a second row', async () => {
    // The INSERT returns zero rows => another run won. This one mints nothing.
    const sql = mockSql([lastPulseRow(1000n, T0 - 2 * DAY), rows([])]);
    const out = await detectPulse({ sql, readSharePrice: async () => ({ rawU128: 1100n }), now: T0 });
    assert.equal(out.minted, false);
    assert.equal(out.raced, true);
});

test('first run records a BASELINE that grants nothing (there is no previous price to advance from)', async () => {
    const sql = mockSql([rows([]), rows([{ pulse_id: 1, sequence_number: 0, status: PULSE_STATUS.BASELINE }])]);
    const out = await detectPulse({ sql, readSharePrice: async () => ({ rawU128: 5000n }), now: T0 });
    assert.equal(out.minted, false);
    assert.equal(out.reason, SKIP.BASELINE_RECORDED);
    assert.equal(out.baseline.status, PULSE_STATUS.BASELINE);
});

// -- Q-CADENCE: at most one per day ------------------------------------------

test('Q-CADENCE: a REAL advancement on a day that already pulsed is refused — rare and ceremonial', async () => {
    assert.equal(MAX_PULSES_PER_UTC_DAY, 1, 'the owner ruling: at most one pulse per day');
    const sql = mockSql([lastPulseRow(1000n, T0 - 3 * 60 * 60 * 1000)]); // 02:00 the same UTC day
    const out = await detectPulse({ sql, readSharePrice: async () => ({ rawU128: 9999n }), now: T0 });
    assert.equal(out.minted, false);
    assert.equal(out.reason, SKIP.INTERVAL_FLOOR);
    assert.equal(out.pulsedOn, utcDayKey(T0));
    assert.equal(sql.calls.filter((c) => c.text.includes('INSERT')).length, 0);
});

test('Q-CADENCE: CRON JITTER MUST NOT SKIP A DAY — 23h59m25s apart across midnight still MINTS', async () => {
    // ⛔ THE CASE THAT KILLED THE ELAPSED-MS FLOOR. Vercel does not fire a cron
    // to the second: yesterday 05:00:45, today 05:00:10 is 86,399,000 ms — inside
    // a 24h floor — and a `now - last < 86_400_000` test would have refused a
    // real advancement and skipped the pulse for a whole day on scheduler drift.
    // A UTC calendar-day key has no such edge.
    const yesterday = Date.parse('2026-09-09T05:00:45.000Z');
    const today = Date.parse('2026-09-10T05:00:10.000Z');
    assert.ok(today - yesterday < DAY, 'the two runs really are less than 24h apart');
    assert.notEqual(utcDayKey(yesterday), utcDayKey(today), 'but they are different UTC days');

    const sql = mockSql([lastPulseRow(1000n, yesterday), mintedRow()]);
    const out = await detectPulse({ sql, readSharePrice: async () => ({ rawU128: 1100n }), now: today });
    assert.equal(out.minted, true, 'scheduler jitter must never cost the player a pulse');
});

test('Q-CADENCE: the BASELINE row does not make the first real pulse wait a day', async () => {
    const baseline = rows([{
        pulse_id: 1, sequence_number: 0, new_share_price: '1000',
        detected_at_utc: new Date(T0 - 60 * 1000).toISOString(), status: PULSE_STATUS.BASELINE,
    }]);
    const sql = mockSql([baseline, mintedRow(1)]);
    const out = await detectPulse({ sql, readSharePrice: async () => ({ rawU128: 1100n }), now: T0 });
    assert.equal(out.minted, true, 'bookkeeping is not a pulse');
});

// -- 3 + Q2: RPC failure ------------------------------------------------------

test('acceptance 3 / Q2: an RPC failure in the SHARE-PRICE read mints nothing and writes nothing', async () => {
    const sql = mockSql([lastPulseRow(1000n, T0 - 2 * DAY)]);
    const out = await detectPulse({
        sql,
        readSharePrice: async () => { throw new Error('RPC timeout'); },
        now: T0,
    });
    assert.equal(out.minted, false);
    assert.equal(out.reason, SKIP.RPC_UNAVAILABLE);
    assert.equal(sql.calls.length, 1, 'only the "newest pulse" SELECT ran; nothing was written');
});

test('acceptance 3 / Q2: a per-player RPC failure records PENDING, grants nothing, and NEVER writes state', async () => {
    const sql = mockSql([rows([])]); // the PENDING insert
    let statePersisted = false;
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: 'wallet-a',
            walletAddress: 'wallet-a',
            activatedAtUtc: new Date(T0 - 30 * DAY).toISOString(),
            lastVerifiedAtUtc: new Date(T0 - 2 * 60 * 60 * 1000).toISOString(),
            lastActualStake: '5000',
            effectiveResonatingStake: '1250',
            continuousPulseCount: 9,
            totalLifetimePulses: 9,
        },
        readStake: async () => { throw new Error('RPC timeout'); },
        resonance,
        persistPlayerState: async () => { statePersisted = true; },
        now: T0,
    });

    assert.equal(out.granted, false);
    assert.equal(out.reason, SKIP.PENDING_RPC);
    assert.equal(statePersisted, false,
        'Q2 fails to LAST-KNOWN state: the streak/stake row is not touched at all, never zeroed');

    const ins = sql.calls[0];
    assert.ok(ins.text.includes('INSERT INTO player_pulse_grant'));
    assert.ok(ins.values.includes(GRANT_STATUS.PENDING), 'the row is PENDING, not GRANTED');
    assert.ok(ins.values.includes('RPC_UNAVAILABLE'));
    // No reward field is bound on this path — an unverified pulse pays nothing.
    assert.ok(!ins.text.includes('resonance_score'), 'no score is written for an unverified pulse');
});

test('Q2: a player stale BEYOND the 72h grace is still PENDING (marked STALE), never reset', async () => {
    assert.equal(STALE_GRACE_MS, 72 * 60 * 60 * 1000, 'grace is the spec-suggested 72 hours');
    const sql = mockSql([rows([])]);
    let statePersisted = false;
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: 'wallet-b',
            walletAddress: 'wallet-b',
            activatedAtUtc: new Date(T0 - 90 * DAY).toISOString(),
            lastVerifiedAtUtc: new Date(T0 - 5 * DAY).toISOString(),
            continuousPulseCount: 40,
            totalLifetimePulses: 40,
        },
        readStake: async () => { throw new Error('RPC down'); },
        resonance,
        persistPlayerState: async () => { statePersisted = true; },
        now: T0,
    });
    assert.equal(out.reason, SKIP.PENDING_STALE);
    assert.equal(statePersisted, false);
    assert.ok(sql.calls[0].values.includes('STALE'));
});

// -- 2. A DUPLICATE JOB CANNOT DOUBLE-PAY ------------------------------------

test('acceptance 2: a duplicate job\'s grant insert returns zero rows and is mapped to "already granted"', async () => {
    // INSERT … ON CONFLICT DO NOTHING RETURNING -> [] , then the re-read shows
    // the row is already GRANTED. This is api/referral/install-brag.js:118-140's
    // mapping, reused rather than reinvented.
    const sql = mockSql([rows([]), rows([{ status: GRANT_STATUS.GRANTED }])]);
    let statePersisted = false;
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: 'wallet-c', walletAddress: 'wallet-c',
            activatedAtUtc: new Date(T0 - 10 * DAY).toISOString(),
            lastVerifiedAtUtc: new Date(T0).toISOString(),
            effectiveResonatingStake: '1000', continuousPulseCount: 3, totalLifetimePulses: 3,
        },
        readStake: async () => stakeReading(4000),
        resonance,
        persistPlayerState: async () => { statePersisted = true; },
        now: T0,
    });
    assert.equal(out.granted, false);
    assert.equal(out.reason, SKIP.ALREADY_GRANTED);
    assert.equal(statePersisted, false, 'the second run must not advance the streak either');

    const ins = sql.calls[0];
    assert.ok(ins.text.includes('ON CONFLICT (player_id, global_pulse_id) DO NOTHING'),
        'the PK is the structural gate — "a pulse can never pay the same player twice"');
});

test('a PENDING row from an earlier failed run is PROMOTED in place, never re-inserted', async () => {
    const sql = mockSql([rows([]), rows([{ status: GRANT_STATUS.PENDING }]), rows([])]);
    let persisted = null;
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: 'wallet-d', walletAddress: 'wallet-d',
            activatedAtUtc: new Date(T0 - 10 * DAY).toISOString(),
            lastVerifiedAtUtc: new Date(T0 - DAY).toISOString(),
            effectiveResonatingStake: '1000', continuousPulseCount: 3, totalLifetimePulses: 3,
        },
        readStake: async () => stakeReading(4000),
        resonance,
        persistPlayerState: async (_sql, s) => { persisted = s; },
        now: T0,
    });
    assert.equal(out.granted, true);
    const upd = sql.calls.find((c) => c.text.includes('UPDATE player_pulse_grant'));
    assert.ok(upd, 'the pending row is promoted with an UPDATE');
    assert.ok(upd.values.includes(GRANT_STATUS.GRANTED));
    assert.equal(sql.calls.filter((c) => c.text.includes('INSERT INTO player_pulse_grant')).length, 1,
        'exactly one INSERT — one row per (player, pulse), forever');
    assert.equal(persisted.continuousPulseCount, 4, 'the streak advances only on a verified grant');
});

// -- 5. NO PULSE BEFORE ACTIVATION -------------------------------------------

test('acceptance 5: a pulse dated BEFORE activated_at_utc is not processed (no retroactive awards)', async () => {
    const sql = mockSql([]);
    let read = false;
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0 - 10 * DAY).toISOString() },
        state: {
            playerId: 'wallet-e', walletAddress: 'wallet-e',
            activatedAtUtc: new Date(T0).toISOString(),
            lastVerifiedAtUtc: new Date(T0).toISOString(),
        },
        readStake: async () => { read = true; return stakeReading(9999); },
        resonance,
        persistPlayerState: async () => {},
        now: T0,
    });
    assert.equal(out.granted, false);
    assert.equal(out.reason, SKIP.BEFORE_ACTIVATION);
    assert.equal(read, false, 'the chain is not even read for a pulse that predates activation');
    assert.equal(sql.calls.length, 0, 'and no row is written');
});

test('a stake below the HEART-003 eligibility floor grants nothing', async () => {
    const sql = mockSql([]);
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: 'wallet-f', walletAddress: 'wallet-f',
            activatedAtUtc: new Date(T0 - DAY).toISOString(), lastVerifiedAtUtc: new Date(T0).toISOString(),
        },
        readStake: async () => stakeReading(5),
        resonance,
        persistPlayerState: async () => {},
        now: T0,
    });
    assert.equal(out.reason, SKIP.BELOW_MINIMUM);
    assert.equal(sql.calls.length, 0);
});

// -- 4. CATCH-UP CAP ----------------------------------------------------------

test('acceptance 4: catch-up is capped at 5 pending pulses, oldest first, remainder REPORTED', () => {
    assert.equal(MAX_CATCHUP_PULSES, 5);
    const pending = [9, 5, 7, 6, 8, 10, 11].map((n) => ({ sequence_number: n }));
    const { toProcess, deferred } = selectCatchupPulses(pending);
    assert.equal(toProcess.length, 5);
    assert.deepEqual(toProcess.map((p) => p.sequence_number), [5, 6, 7, 8, 9], 'oldest first');
    assert.equal(deferred, 2, 'the remainder is surfaced for explicit reconciliation, not silently dropped');
});

// -- THE WHOLE JOB ------------------------------------------------------------

const twoStates = () => ([
    { playerId: 'wallet-a', walletAddress: 'wallet-a', activatedAtUtc: new Date(T0 - 10 * DAY).toISOString(), lastVerifiedAtUtc: new Date(T0).toISOString(), effectiveResonatingStake: '1000', continuousPulseCount: 2, totalLifetimePulses: 2 },
    { playerId: 'wallet-b', walletAddress: 'wallet-b', activatedAtUtc: new Date(T0 - 10 * DAY).toISOString(), lastVerifiedAtUtc: new Date(T0).toISOString(), continuousPulseCount: 1, totalLifetimePulses: 1 },
]);

const unfinishedPulse = (id = 43, seq = 8) => ({
    pulse_id: id, sequence_number: seq,
    detected_at_utc: new Date(T0).toISOString(), status: PULSE_STATUS.MINTED,
});

test('a full run mints once, processes every eligible player, and closes the pulse COMPLETE', async () => {
    const sql = mockSql([
        lastPulseRow(1000n, T0 - 2 * DAY),      // detect: newest pulse
        mintedRow(),                             // detect: the mint
        rows([unfinishedPulse()]),               // resume: the pulse just minted
        rows([]),                                // sweep: no pending debts
        rows([{ player_id: 'wallet-a', global_pulse_id: 43, status: GRANT_STATUS.GRANTED }]),
        rows([]),                                // pending insert for b (RPC down)
        rows([]),                                // the COMPLETE update
    ]);
    const summary = await runHeartPulseDetector({
        sql,
        now: T0,
        readSharePrice: async () => ({ rawU128: 1100n }),
        readStake: async (w) => {
            if (w === 'wallet-b') throw new Error('RPC timeout');
            return stakeReading(4000);
        },
        listActiveStates: async () => twoStates(),
        persistPlayerState: async () => {},
        resonance,
    });
    assert.equal(summary.minted, true);
    assert.equal(summary.eligible, 2);
    assert.equal(summary.processed, 1);
    assert.equal(summary.pending, 1);
    const done = sql.calls.find((c) => c.text.includes('UPDATE global_heart_pulse'));
    assert.ok(done && done.values.includes(PULSE_STATUS.COMPLETE));
});

test('a run that mints nothing and owes nothing never touches the players or the HEART-003 seam', async () => {
    const sql = mockSql([lastPulseRow(1100n, T0 - 2 * DAY), ...NO_RETRY_WORK]);
    let listed = false;
    const summary = await runHeartPulseDetector({
        sql,
        now: T0,
        readSharePrice: async () => ({ rawU128: 1100n }),
        listActiveStates: async () => { listed = true; return []; },
    });
    assert.equal(summary.minted, false);
    assert.equal(summary.reason, SKIP.NO_ADVANCEMENT);
    assert.equal(listed, false);
});

// -- HEART-003 IS REALLY DRIVEN, AND THE UNIT BOUNDARY IS REAL ---------------

test('the grant carries HEART-003\'s own numbers — computed independently, not restated', async () => {
    const prior = {
        actualSkr: 4000, effectiveSkr: 1000, continuousPulseCount: 4, highestLifetimeTier: 2,
    };
    // The expectation is derived by calling the module directly, so the test
    // agrees with the CURVE rather than with a number typed here that could
    // silently diverge the day the config is retuned.
    const expected = resonance.evaluate(resonance.applyPulse(prior, 4000));

    const sql = mockSql([rows([{ player_id: 'wallet-x', global_pulse_id: 43, status: GRANT_STATUS.GRANTED }])]);
    let persisted = null;
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: 'wallet-x', walletAddress: 'wallet-x',
            activatedAtUtc: new Date(T0 - 10 * DAY).toISOString(),
            lastVerifiedAtUtc: new Date(T0).toISOString(),
            lastActualStake: prior.actualSkr,
            effectiveResonatingStake: prior.effectiveSkr,
            continuousPulseCount: prior.continuousPulseCount,
            totalLifetimePulses: 9,
            highestLifetimeTier: prior.highestLifetimeTier,
        },
        readStake: async () => stakeReading(4000),
        resonance,
        persistPlayerState: async (_sql, s) => { persisted = s; },
        now: T0,
    });

    assert.equal(out.granted, true);
    assert.equal(out.resonanceScore, expected.resonanceScore);
    assert.equal(out.resonanceTier, expected.tier);
    assert.equal(out.tierName, expected.tierName);
    // The ramp closed 25% of the 3,000 SKR gap: 1,000 -> 1,750. That number comes
    // from applyPulse, not from this file.
    assert.equal(out.effectiveStake, 1750);
    assert.equal(persisted.continuousPulseCount, 5, 'the streak advanced by exactly one');
    assert.ok(persisted.highestLifetimeTier >= prior.highestLifetimeTier,
        'highestLifetimeTier never decreases (spec :371-375)');
});

test('the unit boundary is REAL: eligibility is judged in SKR, not in raw base units', async () => {
    // 99 SKR = 99,000,000 base units. A naive raw-vs-floor comparison would call
    // that eligible (99e6 >= 100) — the exact bug the boundary exists to prevent.
    const run = async (skr) => {
        const sql = mockSql([rows([{ player_id: 'w', global_pulse_id: 43, status: GRANT_STATUS.GRANTED }])]);
        return processPlayerForPulse({
            sql,
            pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
            state: {
                playerId: 'w', walletAddress: 'w',
                activatedAtUtc: new Date(T0 - DAY).toISOString(), lastVerifiedAtUtc: new Date(T0).toISOString(),
            },
            readStake: async () => stakeReading(skr),
            resonance,
            persistPlayerState: async () => {},
            now: T0,
        });
    };
    assert.equal((await run(99)).reason, SKIP.BELOW_MINIMUM, '99 SKR is below the 100 SKR floor');
    assert.equal((await run(100)).granted, true, 'exactly the floor qualifies (isEligible is inclusive)');
});

test('a malformed stake snapshot is treated as UNVERIFIED — pending, never a zero-stake payout', async () => {
    const sql = mockSql([rows([])]);
    let persisted = false;
    const out = await processPlayerForPulse({
        sql,
        pulse: { pulse_id: 43, detected_at_utc: new Date(T0).toISOString() },
        state: {
            playerId: 'w', walletAddress: 'w',
            activatedAtUtc: new Date(T0 - DAY).toISOString(), lastVerifiedAtUtc: new Date(T0).toISOString(),
            lastActualStake: 4000, effectiveResonatingStake: 1000, continuousPulseCount: 3,
        },
        readStake: async () => ({ somethingElse: 1 }), // none of the accepted shapes
        resonance,
        persistPlayerState: async () => { persisted = true; },
        now: T0,
    });
    assert.equal(out.reason, SKIP.PENDING_RPC);
    assert.match(out.error, /readStake snapshot carries none of/);
    assert.equal(persisted, false, 'and the last-known state is untouched (Q2)');
});

// -- D4 RETRY: the next run finishes what the last one could not --------------

test('D4 retry: a run that CRASHED mid-pulse is resumed by the next run, with no new mint', async () => {
    // Nothing advanced (so nothing mints), but pulse 43 is still MINTED — the
    // previous run died before closing it. The resume phase finishes it.
    const sql = mockSql([
        lastPulseRow(1100n, T0 - 2 * DAY),       // detect: no advancement
        rows([unfinishedPulse()]),               // resume: 43 left open
        rows([]),                                // sweep: none
        rows([{ player_id: 'wallet-a', global_pulse_id: 43, status: GRANT_STATUS.GRANTED }]),
        rows([{ player_id: 'wallet-b', global_pulse_id: 43, status: GRANT_STATUS.GRANTED }]),
        rows([]),                                // COMPLETE
    ]);
    const summary = await runHeartPulseDetector({
        sql,
        now: T0,
        readSharePrice: async () => ({ rawU128: 1100n }),
        readStake: async () => stakeReading(4000),
        listActiveStates: async () => twoStates(),
        persistPlayerState: async () => {},
        resonance,
    });
    assert.equal(summary.minted, false, 'a resume never mints');
    assert.equal(summary.pulsesProcessed, 1);
    assert.equal(summary.processed, 2, 'both players were paid for the abandoned pulse');
    const done = sql.calls.find((c) => c.text.includes('UPDATE global_heart_pulse'));
    assert.ok(done && done.values.includes(PULSE_STATUS.COMPLETE));
});

test('acceptance 4: a returning player\'s PENDING pulses are promoted, capped at 5, remainder deferred', async () => {
    // wallet-a came back after an outage holding SEVEN pending pulses on
    // already-CLOSED pulses. Five are paid; two are reported for reconciliation.
    const pending = [];
    for (let i = 1; i <= 7; i++) {
        pending.push({ player_id: 'wallet-a', global_pulse_id: 100 + i, sequence_number: i, detected_at_utc: new Date(T0 - (10 - i) * DAY).toISOString() });
    }
    const script = [
        lastPulseRow(1100n, T0 - 2 * DAY),  // detect: no advancement
        rows([]),                            // resume: nothing open
        rows(pending),                       // sweep: seven debts
    ];
    // Each promotion: INSERT (conflict -> []), SELECT (PENDING), UPDATE.
    for (let i = 0; i < 5; i++) {
        script.push(rows([]), rows([{ status: GRANT_STATUS.PENDING }]), rows([]));
    }
    const sql = mockSql(script);
    const summary = await runHeartPulseDetector({
        sql,
        now: T0,
        readSharePrice: async () => ({ rawU128: 1100n }),
        readStake: async () => stakeReading(4000),
        listActiveStates: async () => ([twoStates()[0]]),
        persistPlayerState: async () => {},
        resonance,
    });
    assert.equal(summary.processed, 5, 'exactly the cap is paid');
    assert.equal(summary.deferred, 2, 'the overflow is REPORTED, never silently paid');
    const promotions = sql.calls.filter((c) => c.text.includes('UPDATE player_pulse_grant'));
    assert.equal(promotions.length, 5, 'each promotion is an in-place UPDATE of the existing row');
    assert.equal(sql.calls.filter((c) => c.text.includes('INSERT INTO player_pulse_grant')).length, 5,
        'and never a second row for a (player, pulse) that already has one');
});

// -- Q1 / Q-INJECT: the seams and what may NOT exist --------------------------

test('Q1: the stake seams are UNWIRED by default and say exactly which lane owns them', async () => {
    const sql = mockSql([lastPulseRow(1000n, T0 - 2 * DAY)]);
    const out = await detectPulse({ sql, now: T0 }); // no readSharePrice injected
    assert.equal(out.reason, SKIP.RPC_UNAVAILABLE);
    assert.match(out.error, /HEART-001 seam not wired/,
        'the default read is a named placeholder, never a chain read written by this lane');
});

test('Q1: this lane ships NO chain read — no RPC/web3 call exists in either new file', () => {
    for (const rel of ['api/_lib/heartbound-pulse.js', 'api/cron/heart-pulse.js']) {
        const src = readSrc(rel).split('\n')
            .filter((l) => !l.trim().startsWith('//')).join('\n'); // comments stripped: they discuss RPC
        assert.ok(!/getAccountInfo|@solana|web3\.js|solana-rpc|fetch\(/.test(src),
            rel + ' must contain no chain read — HEART-001 owns it (Q1)');
    }
});

test('Q-INJECT: the cron route accepts no injectable pulse input', () => {
    const src = readSrc('api/cron/heart-pulse.js').split('\n')
        .filter((l) => !l.trim().startsWith('//') && !l.trim().startsWith('*')).join('\n');
    assert.ok(!/req\.body/.test(src), 'the route must never read a request body — Q-INJECT');
    assert.ok(!/req\.query/.test(src), 'the route must never read a query parameter — Q-INJECT');
    assert.ok(/timingSafeEqual/.test(src), 'auth is constant-time, never ===');
    assert.ok(!/Access-Control-Allow-Origin/.test(src), 'no CORS surface on a server-to-server route');
});

// -- Q-CRON: the third daily cron is declared --------------------------------

test('Q-CRON: vercel.json declares a DAILY cron pointing at this route', () => {
    // ⚠ Deliberately NOT asserting cfg.crons.length. A total is hand-maintained
    // state tracking a live file (CLAUDE.md §5's own lesson); the next cron
    // anyone adds would fail this test for no reason. Assert THIS cron.
    const cfg = JSON.parse(readSrc('vercel.json'));
    const pulse = cfg.crons.find((c) => c.path === '/api/cron/heart-pulse');
    assert.ok(pulse, 'the third cron the ruling accepts must be declared');
    assert.match(pulse.schedule, /^\d+ \d+ \* \* \*$/, 'DAILY — a fixed hour every day (Q-CADENCE)');
});

// -- The DDL this lane could not fold into schema.sql -------------------------

test('the pulse DDL carries the achievement_grants idempotency shape and u128-safe columns', () => {
    const ddl = readSrc('api/_lib/heartbound-pulse-schema.sql');
    assert.ok(/PRIMARY KEY \(player_id, global_pulse_id\)/.test(ddl),
        'spec :445-449 — the pair is the PK, so a double-pay is structurally impossible');
    assert.ok(/UNIQUE/.test(ddl) && /new_share_price\s+NUMERIC\(39,0\)\s+NOT NULL UNIQUE/.test(ddl),
        'one pulse per unique observed advancement, enforced by the index');
    assert.ok(!/share_price\s+BIGINT/.test(ddl),
        'a u128 share price must never be a BIGINT (int64 overflows)');
    // ⚠ Deliberately NOT asserting that api/schema.sql lacks these tables. That
    // assertion would go RED the moment the lead folds this DDL in — i.e. it
    // would pin the very state the RESULT asks them to change. That this lane
    // did not edit schema.sql is proven by git status, not by a test.
});
