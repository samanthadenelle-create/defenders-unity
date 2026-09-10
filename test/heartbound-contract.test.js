'use strict';

// =============================================================================
// test/heartbound-contract.test.js — WO-1693: the STAKE UNIT CONTRACT between
// heartbound-pulse.js (whole SKR, fractional) and heartbound-state.js (raw u128
// base units, NUMERIC(39,0)).
//
// ⛔ WHY THIS SUITE EXISTS AT ALL. Every other Heartbound suite drives ONE module.
//    The defect WO-1684 §5.1 measured lived in NEITHER module — it lived in the
//    seam between them, where `last_actual_stake` carried two units 1e6 apart, so
//    no single-module suite could see it. This one WIRES THE REAL MODULES
//    TOGETHER (no stand-ins for either) over an in-memory stateful sql mock, and
//    runs THREE consecutive pulses, because the ramp only produces a fraction from
//    the SECOND pulse on:
//
//        activate 1000 effective / 4000 actual, then applyPulse x3
//          -> 1750        (integer  — a one-pulse test passes and proves nothing)
//          -> 2312.5      (fraction — HEAD throws here)
//          -> 2734.375    (fraction)
//
//    RED ON HEAD, verbatim from `node --test` with the fix stashed:
//        TypeError: effectiveResonatingStake: expected a non-negative integer
//        string, got "2312.5"
//    i.e. the daily cron 500s on pulse two, for every staked player, the moment
//    anyone wires the pulse's persist path to the state writer.
//
// The mock is STATEFUL, not a queued script: pulse N+1 must READ BACK what pulse N
// WROTE, which is the only way a unit error on the write side can be caught by the
// read side. A queued script would happily hand back whatever the test expected.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');

const resonance = require('../api/_lib/heartbound-resonance.js');
const state = require('../api/_lib/heartbound-state.js');
const pulse = require('../api/_lib/heartbound-pulse.js');

const WALLET = 'WalletHEARTBOUND1693';
const T0 = Date.parse('2026-09-10T05:00:00.000Z');
const DAY = 24 * 60 * 60 * 1000;
const SKR = 1000000n; // chain.skrBaseUnits, read off the config below rather than assumed
assert.equal(BigInt(resonance.DEFAULT_CONFIG.chain.skrBaseUnits), SKR);

// The four SELECT/RETURNING columns this wire actually reads back. Kept as one
// builder so the stored row and the returned row cannot disagree — a mock that
// returns a shape it does not store is a mock that proves nothing.
function rowOf(store) {
    return {
        player_id: WALLET,
        status: store.status,
        activated_at_utc: new Date(store.activatedAtMs).toISOString(),
        last_verified_at_utc: store.lastVerifiedAtUtc,
        last_actual_stake: store.lastActualStake,
        effective_resonating_stake: store.effectiveResonatingStake,
        last_verification_attempt_at_utc: null,
        last_verification_code: null,
        stale_since_utc: null,
        continuous_pulse_count: store.continuousPulseCount,
        total_lifetime_pulses: store.totalLifetimePulses,
        last_global_pulse_id: store.lastGlobalPulseId,
        last_player_pulse_id: null,
        resonance_score: store.resonanceScore,
        resonance_tier: store.resonanceTier,
        highest_lifetime_tier: store.highestLifetimeTier,
        current_tree_resonance_stage: store.currentTreeResonanceStage,
        pending_echo_events: [],
        last_echo_event_id: null,
        version: store.version,
    };
}

/**
 * A stateful tagged-template sql(). It answers from — and MUTATES — one
 * heartbound_state row, and records every player_pulse_grant write.
 *
 * The bind positions are read off the statements themselves, not guessed:
 * recordVerifiedStake's INSERT (heartbound-state.js) binds
 *   [playerId, status, verifiedAtUtc, actual, effective, score, tier, tier, stage]
 * so the two stake columns are values[3] and values[4].
 */
function wiredSql(seed) {
    const store = Object.assign({
        status: 'ACTIVE',
        activatedAtMs: T0 - 30 * DAY,
        lastVerifiedAtUtc: new Date(T0 - DAY).toISOString(),
        lastActualStake: '0',
        effectiveResonatingStake: '0',
        continuousPulseCount: 0,
        totalLifetimePulses: 0,
        lastGlobalPulseId: null,
        resonanceScore: '0',
        resonanceTier: 0,
        highestLifetimeTier: 0,
        currentTreeResonanceStage: 0,
        version: 1,
    }, seed || {});

    const grants = [];
    const stateWrites = [];

    const sql = async (strings, ...values) => {
        const text = strings.join('?');

        if (/INSERT INTO heartbound_state/.test(text)) {
            store.status = values[1];
            store.lastVerifiedAtUtc = values[2] == null ? new Date(T0).toISOString() : values[2];
            store.lastActualStake = values[3];
            store.effectiveResonatingStake = values[4];
            store.resonanceScore = values[5];
            store.resonanceTier = values[6];
            store.highestLifetimeTier = Math.max(store.highestLifetimeTier, values[7]);
            store.currentTreeResonanceStage = values[8];
            store.version += 1;
            stateWrites.push({ lastActualStake: values[3], effectiveResonatingStake: values[4] });
            return [rowOf(store)];
        }
        if (/SELECT[\s\S]*FROM heartbound_state/.test(text)) {
            return [rowOf(store)];
        }
        if (/INSERT INTO player_pulse_grant/.test(text)) {
            // values: [playerId, pulseId, status, effective_stake, score, tier, grantedAt]
            grants.push({ pulseId: values[1], status: values[2], effectiveStake: values[3] });
            return [{ player_id: values[0], global_pulse_id: values[1], status: values[2] }];
        }
        return [];
    };
    sql.store = store;
    sql.grants = grants;
    sql.stateWrites = stateWrites;
    return sql;
}

// ⭐ THE WIRE ITSELF — the two lines the cron shell (api/cron/heart-pulse.js, not
// this lane's file) has to write, and the whole point of the exported adapters:
// nothing in this test converts a unit by hand.
async function runOnePulse(sql, pulseId, atMs) {
    const snapshot = await state.readHeartboundState(sql, WALLET);
    const row = pulse.stateSnapshotToPulseState(snapshot, resonance);
    return pulse.processPlayerForPulse({
        sql,
        pulse: { pulse_id: pulseId, sequence_number: pulseId, detected_at_utc: new Date(atMs).toISOString() },
        state: row,
        readStake: async () => ({ rawU128: 4000n * SKR }), // a steady 4,000 SKR position
        resonance,
        persistPlayerState: async (s, payload) =>
            state.recordVerifiedStake(s, payload.playerId, pulse.verifiedStakeArgsFromPersistPayload(payload)),
        now: atMs,
    });
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE REGRESSION — three pulses, real modules, no throw, exact stored strings
// ═══════════════════════════════════════════════════════════════════════════

test('WO-1693: three consecutive pulses wire pulse -> state without throwing, and store RAW BASE UNITS', async () => {
    const sql = wiredSql({
        lastActualStake: String(4000n * SKR),      // 4,000 SKR, already verified once
        effectiveResonatingStake: String(1000n * SKR), // 1,000 SKR of trust earned so far
    });

    const outs = [];
    for (let i = 1; i <= 3; i++) outs.push(await runOnePulse(sql, i, T0 + i * DAY));

    // The ramp, in WHOLE SKR, straight off heartbound-resonance — the exact three
    // figures WO-1684 §5.1 measured, and the reason a two-pulse test is not enough.
    assert.deepEqual(outs.map((o) => o.granted), [true, true, true]);
    assert.deepEqual(outs.map((o) => o.effectiveStake), [1750, 2312.5, 2734.375]);

    // ⛔ heartbound_state — NUMERIC(39,0), RAW BASE UNITS. 1750 SKR is 1.75e9 base
    //    units; storing the SKR figure would be 1e6 too small, and storing the
    //    FRACTION would (and on HEAD did) throw.
    assert.deepEqual(sql.stateWrites.map((w) => w.effectiveResonatingStake),
        ['1750000000', '2312500000', '2734375000']);
    assert.deepEqual(sql.stateWrites.map((w) => w.lastActualStake),
        ['4000000000', '4000000000', '4000000000']);

    // ⛔ player_pulse_grant.effective_stake — NUMERIC(39,6), WHOLE SKR. A DIFFERENT
    //    column with a DIFFERENT unit AND a different scale, on the same statement
    //    path. Fixed-point text, never String(Number) and never a raw u128.
    assert.deepEqual(sql.grants.map((g) => g.effectiveStake),
        ['1750.000000', '2312.500000', '2734.375000']);

    // And the row that survives is the raw one, read back through the real snapshot.
    const finalSnapshot = await state.readHeartboundState(sql, WALLET);
    assert.equal(finalSnapshot.effectiveResonatingStake, '2734375000');
    assert.equal(resonance.rawTokensToSkr(finalSnapshot.effectiveResonatingStake), 2734.375);
});

test('the READ side crosses too: a raw base-unit row is not read as a billion-SKR position', async () => {
    // Without stateSnapshotToPulseState, Number('4000000000') makes a 4,000 SKR
    // holder look like a 4-billion-SKR whale, and every downstream figure is 1e6
    // too large. Silent, unlike the write side — which is why it is pinned.
    const sql = wiredSql({
        lastActualStake: String(4000n * SKR),
        effectiveResonatingStake: String(1000n * SKR),
    });
    const row = pulse.stateSnapshotToPulseState(await state.readHeartboundState(sql, WALLET), resonance);
    assert.equal(row.lastActualStake, 4000);
    assert.equal(row.effectiveResonatingStake, 1000);
    assert.equal(row.walletAddress, WALLET, 'ONE WALLET = ONE REALM: the player id IS the wallet');
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE GUARD — the state writer refuses whole SKR, by name
// ═══════════════════════════════════════════════════════════════════════════

test('recordVerifiedStake REFUSES a fractional (whole-SKR) stake with a message naming the unit', async () => {
    const sql = wiredSql({});
    await assert.rejects(
        () => state.recordVerifiedStake(sql, WALLET, {
            lastActualStake: '4000000000',
            effectiveResonatingStake: 2312.5, // the HEAD defect, as a Number
        }),
        (err) => {
            assert.ok(err instanceof TypeError);
            assert.match(err.message, /effectiveResonatingStake/);
            assert.match(err.message, /RAW SKR BASE UNITS/);
            assert.match(err.message, /skrToRawTokens/, 'the message names the ONE function that fixes it');
            return true;
        });
    // The same value as a STRING is refused identically — a JSON round-trip must
    // not launder it past the guard.
    await assert.rejects(() => state.recordVerifiedStake(sql, WALLET, {
        lastActualStake: '4000000000', effectiveResonatingStake: '2734.375',
    }), /RAW SKR BASE UNITS/);
    assert.equal(sql.stateWrites.length, 0, 'and nothing was written');
});

test('⚠ UNPROVABLE BY THIS GUARD, STATED RATHER THAN IMPLIED: whole SKR that is an INTEGER passes', async () => {
    // 1750 SKR and 1750 base units are the same digits. No guard on the writer's
    // side can tell them apart; only the CALLER's conversion can. This test pins
    // that the limitation is known, so nobody later reads the guard as complete.
    const sql = wiredSql({});
    await state.recordVerifiedStake(sql, WALLET, {
        lastActualStake: '1750', effectiveResonatingStake: '1750',
    });
    assert.equal(sql.stateWrites[0].effectiveResonatingStake, '1750',
        'accepted — 1e6 too small if the caller meant SKR, and the writer cannot know');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. THE BOUNDARY HELPERS — one home, and they round-trip
// ═══════════════════════════════════════════════════════════════════════════

test('skrToRawTokens is the exact inverse of rawTokensToSkr over the ramp figures', () => {
    for (const skr of [0, 100, 1750, 2312.5, 2734.375, 4000]) {
        const raw = resonance.skrToRawTokens(skr);
        assert.equal(resonance.rawTokensToSkr(raw), skr, `${skr} SKR must round-trip`);
    }
    assert.equal(resonance.skrToRawTokens(2312.5).toString(), '2312500000');
    // A position far past Number.MAX_SAFE_INTEGER base units is still exact,
    // because the whole part never rides through the fractional multiply.
    assert.equal(resonance.skrToRawTokens(90000000000).toString(), '90000000000000000');
});

test('skrToNumericText derives its scale from chain.skrBaseUnits, never a literal 6', () => {
    assert.equal(resonance.skrToNumericText(1750), '1750.000000');
    assert.equal(resonance.skrToNumericText(2734.375), '2734.375000');
    assert.equal(resonance.skrToNumericText(0), '0.000000');
    // Change the base unit and the scale follows it — the property a `toFixed(6)`
    // at the call site would not have.
    const cfg = JSON.parse(JSON.stringify(resonance.DEFAULT_CONFIG));
    cfg.chain.skrBaseUnits = 1000;
    assert.equal(resonance.skrToNumericText(2734.375, cfg), '2734.375');
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. FINDING §2 — pinned as a TODO, not buried
// ═══════════════════════════════════════════════════════════════════════════

test('the streak reaches heartbound_state', { todo: 'WO-1693 finding 2 — recordVerifiedStake names no streak column' }, async () => {
    // heartbound-state.js's verified INSERT names last_actual_stake,
    // effective_resonating_stake, resonance_score, resonance_tier,
    // highest_lifetime_tier and current_tree_resonance_stage — and NOT
    // continuous_pulse_count / total_lifetime_pulses / last_global_pulse_id,
    // though the pulse job computes all three and its persist payload carries
    // them. So the streak the tenure curve reads never lands. Recorded here
    // rather than asserted green: it is a contract gap outside this WO's fix,
    // and a suite that quietly omitted it would hide it.
    const sql = wiredSql({
        lastActualStake: String(4000n * SKR), effectiveResonatingStake: String(1000n * SKR),
    });
    await runOnePulse(sql, 1, T0 + DAY);
    await runOnePulse(sql, 2, T0 + 2 * DAY);
    const snapshot = await state.readHeartboundState(sql, WALLET);
    assert.equal(snapshot.continuousPulseCount, 2);
});
