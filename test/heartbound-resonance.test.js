'use strict';

// =============================================================================
// test/heartbound-resonance.test.js - WO-1676 / HEART-003. THE ORACLE.
// -----------------------------------------------------------------------------
// Pins api/_lib/heartbound-resonance.js against the spec at
// docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:260-375.
//
// ⭐ THE SPEC SUPPLIES ITS OWN VECTOR TABLE (:299-311) and says at :297 the numbers
// are "not contractually fixed and should be covered by unit tests". So they are
// asserted WITH A STATED TOLERANCE, and the tolerance is IN THE TEST NAME - a future
// tuning pass then fails loudly with a number in the message instead of drifting
// quietly past a loose assertion nobody reads.
//
// ⛔ THE WHALE PROPERTY IS ASSERTED, NOT INSPECTED. Spec :313: "500,000 SKR does NOT
// provide 500 times the power of 1,000 SKR." A "simplification" that linearises the
// curve would still pass a vector table read loosely; it cannot pass an explicit
// power(500000) < 5 x power(1000).
//
// ⛔ THE ASYMMETRY IS TWO SEPARATE CASES, ON PURPOSE. Ramp-up gradual, ramp-down
// immediate (spec :288-298, ":306 This kills flash-staking exploits"). One combined
// case would let a symmetric implementation - the most likely wrong one - pass.
//
// ⛔ THRESHOLDS ARE PROVEN CONFIG-DRIVEN BY INJECTION, not by reading the source.
// [config] feeds the module a deliberately different tier ladder and asserts the
// answers move. A literal ladder in the arithmetic would survive any assertion made
// against the real config; it cannot survive this one.
//
//     node --test test/heartbound-resonance.test.js
//
// Zero network, zero database, zero Unity, zero fs. Node built-ins only.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');

const R = require('../api/_lib/heartbound-resonance.js');
const CONFIG = require('../api/_lib/heartbound-resonance-config.json');

// ── THE DETERMINISTIC VECTOR TABLE ───────────────────────────────────────────
// effectiveSkr + continuousPulseCount -> stakePower / tenurePower / score / tier.
// `specStakePower` is the spec's own approximate figure at :299-311 where it gives
// one; the assertion is |computed - spec| <= TOLERANCE.
const TOLERANCE = 1; // raw StakePower points. Stated here and in the test name.

const VECTORS = [
    // effectiveSkr, pulses, specStakePower, expectedScore, expectedTier, tierName
    { skr: 100, pulses: 0, spec: 146, score: 146, tier: 0, name: 'Silent' },
    { skr: 500, pulses: 0, spec: 477, score: 477, tier: 1, name: 'Emberbound' },
    { skr: 1000, pulses: 0, spec: 699, score: 698, tier: 2, name: 'Rootbound' },
    { skr: 5000, pulses: 0, spec: 1322, score: 1322, tier: 4, name: 'Echoing' },
    { skr: 10000, pulses: 0, spec: 1613, score: 1612, tier: 5, name: 'Awakened' },
    { skr: 25000, pulses: 0, spec: 2004, score: 2004, tier: 6, name: 'Hearttouched' },
    { skr: 50000, pulses: 0, spec: 2303, score: 2303, tier: 7, name: 'Deep Resonance' },
    { skr: 100000, pulses: 0, spec: 2603, score: 2603, tier: 8, name: 'Heartforged' },
    { skr: 250000, pulses: 0, spec: 3000, score: 3000, tier: 9, name: 'Eternal Echo' },
    { skr: 500000, pulses: 0, spec: 3301, score: 3301, tier: 10, name: 'Heartbound' },
    // Tenure ramps carrying the SAME stake across the knees - the second axis.
    { skr: 1000, pulses: 5, spec: 699, score: 802, tier: 2, name: 'Rootbound' },
    { skr: 1000, pulses: 20, spec: 699, score: 940, tier: 3, name: 'Stonebound' },
    { skr: 1000, pulses: 100, spec: 699, score: 1155, tier: 3, name: 'Stonebound' },
    { skr: 1000, pulses: 527, spec: 699, score: 1398, tier: 4, name: 'Echoing' },
    { skr: 1000, pulses: 100000, spec: 699, score: 1398, tier: 4, name: 'Echoing' },
];

test('[vectors] every spec knee at :299-311 matches StakePower within +/-1 point, and lands on its tier', () => {
    for (const v of VECTORS) {
        const sp = R.stakePower(v.skr);
        assert.ok(Math.abs(sp - v.spec) <= TOLERANCE,
            `StakePower(${v.skr}) = ${sp.toFixed(3)}, spec says ~${v.spec}, tolerance ${TOLERANCE}`);
        const score = R.resonanceScore(v.skr, v.pulses, v.skr);
        assert.equal(score, v.score, `ResonanceScore(${v.skr} SKR, ${v.pulses} pulses)`);
        const tier = R.tierForScore(score);
        assert.equal(tier.tier, v.tier, `tier for score ${score}`);
        assert.equal(tier.name, v.name, `tier name for score ${score}`);
    }
});

test('[whale] 500,000 SKR does NOT give 500x the power of 1,000 - it gives under 5x (spec :313)', () => {
    const whale = R.stakePower(500000);
    const minnow = R.stakePower(1000);
    assert.ok(whale < 5 * minnow, `500k gave ${whale.toFixed(1)}, 5x of 1k is ${(5 * minnow).toFixed(1)}`);
    assert.ok(whale > minnow, 'more stake must still be more power - the curve flattens, it never inverts');
    // 500x the stake buys under 5x the power. The anti-whale property, as a ratio.
    assert.ok(whale / minnow < 5);
});

test('[curve] StakePower is strictly increasing and concave across the knees', () => {
    const xs = VECTORS.filter((v) => v.pulses === 0).map((v) => v.skr);
    for (let i = 1; i < xs.length; i++) {
        assert.ok(R.stakePower(xs[i]) > R.stakePower(xs[i - 1]), `not increasing at ${xs[i]}`);
    }
    // Concavity, measured the way a player feels it: the SAME extra 1,000 SKR must
    // buy strictly less power the higher up the curve it is added.
    //
    // ⚠ NOT "each 10x step adds less than the last" - that is FALSE for log10 and the
    // first draft of this test asserted it and red-flagged correctly. Successive 10x
    // steps CLIMB toward 1,000 points (552.8 -> 913.8 -> 990.4) because a log curve is
    // linear in log space. Diminishing returns is a statement about absolute stake, and
    // this is the assertion that says it.
    const step = (a, b) => R.stakePower(b) - R.stakePower(a);
    assert.ok(step(100000, 101000) < step(10000, 11000));
    assert.ok(step(10000, 11000) < step(1000, 2000));
    assert.ok(step(1000, 2000) < step(100, 1100));
    // Doubling the stake never doubles the power.
    for (const x of [100, 1000, 10000, 100000]) {
        assert.ok(R.stakePower(2 * x) < 2 * R.stakePower(x), `doubling at ${x} doubled the power`);
    }
    assert.equal(R.stakePower(0), 0);
});

// ── the eligibility floor ────────────────────────────────────────────────────

test('[floor] MIN_HEARTBOUND_STAKE is inclusive, comes from config, and a sub-floor holder scores 0', () => {
    assert.equal(R.minimumStake(), 100);
    assert.equal(R.minimumStake(), CONFIG.minHeartboundStake);
    assert.equal(R.isEligible(100), true, 'exactly the floor qualifies');
    assert.equal(R.isEligible(99.999999), false);
    assert.equal(R.resonanceScore(99.999999, 50, 99.999999), 0, 'below the floor there is no position to score');
    assert.ok(R.resonanceScore(100, 0, 100) > 0);
    // The raw-base-unit edge, one unit either side of the floor.
    assert.equal(R.isEligible(R.rawTokensToSkr(99999999n)), false);
    assert.equal(R.isEligible(R.rawTokensToSkr(100000000n)), true);
});

test('[floor] the minimum stake is NOT tier I - 100 SKR is Silent until the curve earns it', () => {
    const view = R.evaluate(R.activate(100));
    assert.equal(view.eligible, true);
    assert.equal(view.tier, 0);
    assert.equal(view.tierName, 'Silent');
});

// ── boundary precision ───────────────────────────────────────────────────────

test('[precision] raw base units convert without the client whole-SKR truncation (NativeSkrStakeQuery.cs:76)', () => {
    assert.equal(R.rawTokensToSkr(100999999n), 100.999999);
    assert.notEqual(R.rawTokensToSkr(100999999n), 100, 'this is exactly the digit the client throws away');
    assert.ok(R.stakePower(100.5) > R.stakePower(100), 'fractional SKR must still move the score');
});

test('[precision] the u128 boundary stays in BigInt - a whale stake does not lose low digits to a double', () => {
    // 1e10 SKR = 1e16 base units, past Number.MAX_SAFE_INTEGER (~9.007e15).
    const raw = 10_000_000_000_000_000n;
    assert.equal(R.rawTokensToSkr(raw), 10_000_000_000);
    // shares x sharePrice / 1e9, the same expression as NativeSkrStakeQuery.cs:75.
    assert.equal(R.sharesToRawTokens('2000000000000', '1500000000'), 3_000_000_000_000n);
    assert.equal(R.skrFromShares('2000000000000', '1500000000'), 3_000_000);
    // A u128 handed in as a lossy double is REFUSED rather than silently rounded.
    assert.throws(() => R.toBigInt(1e17, 'sharesRaw'), /MAX_SAFE_INTEGER/);
});

// ── the asymmetry: two separate cases, deliberately ──────────────────────────

test('[ramp-up] increases are GRADUAL - activation grants 25%, each pulse closes 25% of the gap', () => {
    let s = R.activate(1000);
    assert.equal(s.effectiveSkr, 250, 'activation = actual x 0.25 (spec :288-290)');
    assert.equal(s.continuousPulseCount, 0);

    s = R.applyPulse(s);
    assert.equal(s.effectiveSkr, 437.5, '250 + (1000-250) x 0.25');
    s = R.applyPulse(s);
    assert.equal(s.effectiveSkr, 578.125);
    s = R.applyPulse(s);
    assert.equal(s.effectiveSkr, 683.59375);
    assert.equal(s.continuousPulseCount, 3);

    // It converges toward actual and never overshoots it.
    for (let i = 0; i < 200; i++) s = R.applyPulse(s);
    assert.ok(s.effectiveSkr <= 1000);
    assert.ok(s.effectiveSkr > 999.99, 'ramps toward actual');
});

test('[ramp-down] decreases are IMMEDIATE - no pulse, no delay (spec :296-298)', () => {
    let s = R.activate(100000);
    for (let i = 0; i < 20; i++) s = R.applyPulse(s);
    const before = s.effectiveSkr;
    assert.ok(before > 90000, `expected a well-ramped position, got ${before}`);

    s = R.observeStake(s, 1000);
    assert.equal(s.effectiveSkr, 1000, 'effectiveStake = MIN(actual, effective), applied on the spot');
    assert.equal(s.actualSkr, 1000);
});

test('[ramp] a top-up does NOT move effective stake until the next pulse - the flash-stake is dead', () => {
    let s = R.activate(1000);
    for (let i = 0; i < 60; i++) s = R.applyPulse(s);
    s = { ...s, effectiveSkr: 1000 }; // fully ramped, exactly

    s = R.observeStake(s, 100000);
    assert.equal(s.effectiveSkr, 1000, 'a 100x top-up buys nothing until a pulse is served');
    assert.equal(s.actualSkr, 100000);

    s = R.applyPulse(s);
    assert.equal(s.effectiveSkr, 25750, '1000 + (100000-1000) x 0.25');

    s = R.observeStake(s, 1000);
    assert.equal(s.effectiveSkr, 1000, 'and pulling it back out drops the trust instantly');
});

test('[ramp] a decrease seen AT pulse time clamps before the ramp - the pulse is not a laundering route', () => {
    let s = R.activate(100000);
    for (let i = 0; i < 20; i++) s = R.applyPulse(s);
    const s2 = R.applyPulse(s, 1000);
    assert.ok(s2.effectiveSkr <= 1000, `clamped first, then ramped: got ${s2.effectiveSkr}`);
    assert.equal(s2.effectiveSkr, 1000, 'min(1000, big) = 1000, then ramp toward 1000 stays 1000');
});

test('[ramp] dropping below the floor ends the position and breaks the streak', () => {
    let s = R.activate(1000);
    for (let i = 0; i < 10; i++) s = R.applyPulse(s);
    assert.equal(s.continuousPulseCount, 10);

    const dropped = R.observeStake(s, 50);
    assert.equal(dropped.effectiveSkr, 0);
    assert.equal(dropped.continuousPulseCount, 0);
    assert.equal(R.evaluate(dropped).resonanceScore, 0);
});

// ── tenure ───────────────────────────────────────────────────────────────────

test('[tenure] TenurePower ramps from 0 and caps at exactly 700, at and beyond the cap', () => {
    assert.equal(R.tenurePower(0), 0);
    assert.ok(Math.abs(R.tenurePower(5) - 103.972) < 0.01);
    assert.ok(Math.abs(R.tenurePower(20) - 241.416) < 0.01);
    assert.ok(Math.abs(R.tenurePower(100) - 456.678) < 0.01);
    // The cap crosses at 527 pulses. Below it the curve is still climbing.
    assert.ok(R.tenurePower(526) < 700);
    assert.equal(R.tenurePower(527), 700, 'at the cap');
    assert.equal(R.tenurePower(5000), 700, 'beyond the cap');
    assert.equal(R.tenurePower(1e9), 700, 'far beyond the cap - MIN(), not a runaway');
});

test('[tenure] tenure alone can raise a tier, and a fully-tenured 1,000 SKR position sits at IV', () => {
    assert.equal(R.tierForScore(R.resonanceScore(1000, 0, 1000)).tier, 2);
    assert.equal(R.tierForScore(R.resonanceScore(1000, 527, 1000)).tier, 4);
});

// ── tiers and downgrades ─────────────────────────────────────────────────────

test('[tiers] every threshold in the config ladder is the exact boundary - one point under is the tier below', () => {
    for (const row of CONFIG.tiers) {
        assert.equal(R.tierForScore(row.minScore).tier, row.tier, `score ${row.minScore}`);
        assert.equal(R.tierForScore(row.minScore).name, row.name);
        if (row.tier > 0) {
            assert.equal(R.tierForScore(row.minScore - 1).tier, row.tier - 1, `score ${row.minScore - 1}`);
        }
    }
    assert.equal(R.tierForScore(0).tier, 0);
    assert.equal(R.tierForScore(299).tier, 0);
    assert.equal(R.tierForScore(300).tier, 1);
    assert.equal(R.tierForScore(3199).tier, 9);
    assert.equal(R.tierForScore(3200).tier, 10);
    assert.equal(R.tierForScore(999999).tier, 10, 'the ladder has a ceiling');
});

test('[tiers] nextTierAt reports the gap so the client never re-derives a threshold table', () => {
    const next = R.nextTierAt(298);
    assert.equal(next.tier, 1);
    assert.equal(next.minScore, 300);
    assert.equal(next.pointsAway, 2);
    assert.equal(R.nextTierAt(3200), null, 'no next tier at the ceiling');
});

test('[downgrade] a stake decrease lowers resonanceTier and leaves highestLifetimeTier untouched (spec :371-375)', () => {
    let s = R.activate(500000);
    for (let i = 0; i < 30; i++) s = R.applyPulse(s);
    const peak = R.evaluate(s);
    assert.equal(peak.tier, 10);
    assert.equal(s.highestLifetimeTier, 10);

    s = R.observeStake(s, 500);
    const after = R.evaluate(s);
    assert.ok(after.tier < peak.tier, `tier must fall: ${peak.tier} -> ${after.tier}`);
    assert.equal(s.highestLifetimeTier, 10, 'highestLifetimeTier NEVER decreases');
    assert.equal(after.highestLifetimeTier, 10);

    // And it survives leaving entirely.
    const gone = R.observeStake(s, 0);
    assert.equal(R.evaluate(gone).resonanceScore, 0);
    assert.equal(gone.highestLifetimeTier, 10);
});

test('[downgrade] highestLifetimeTier is monotone across an arbitrary stake walk', () => {
    let s = R.activate(1000);
    let high = 0;
    for (const stake of [1000, 50000, 200, 500000, 100, 250000, 0, 300000]) {
        s = R.observeStake(s, stake);
        s = R.applyPulse(s);
        assert.ok(s.highestLifetimeTier >= high, `dropped from ${high} to ${s.highestLifetimeTier} at stake ${stake}`);
        high = s.highestLifetimeTier;
    }
    assert.ok(high > 0);
});

// ── config-driven, and pure ──────────────────────────────────────────────────

test('[config] the ladder and the curve are read from the injected table, not from literals', () => {
    const cfg = JSON.parse(JSON.stringify(CONFIG));
    cfg.minHeartboundStake = 5000;
    cfg.stakePower.multiplier = 500;
    cfg.tenurePower.cap = 100;
    cfg.effectiveStake.activationFraction = 0.5;
    cfg.tiers = [
        { tier: 0, name: 'Test Zero', minScore: 0 },
        { tier: 1, name: 'Test One', minScore: 10 },
    ];

    assert.equal(R.minimumStake(cfg), 5000);
    assert.equal(R.isEligible(1000, cfg), false, 'the real config would have said eligible');
    assert.ok(Math.abs(R.stakePower(1000, cfg) - R.stakePower(1000) / 2) < 1e-9);
    assert.equal(R.tenurePower(1e6, cfg), 100);
    assert.equal(R.activationEffectiveStake(1000, cfg), 500);
    assert.equal(R.tierForScore(50, cfg).name, 'Test One');
    assert.equal(R.tierForScore(50, cfg).tier, 1, 'the real ladder would have said tier 0');
    assert.equal(R.nextTierAt(5, cfg).minScore, 10);

    // ⚠ AND THROUGH THE NON-UNIFORM POSITIONS. `resonanceScore` takes cfg 4th, `activate`
    // 3rd, `applyPulse`/`observeStake`/`evaluate` 3rd - the easiest places for a future
    // consumer to drop the argument and silently get the DEFAULT table back. Each is
    // exercised here with a config whose answers differ from the real one.
    assert.equal(R.resonanceScore(1000, 0, 1000, cfg), 0, 'ineligible under the injected 5000 floor');
    assert.equal(R.activate(10000, null, cfg).effectiveSkr, 5000, 'injected activationFraction 0.5');
    assert.equal(R.evaluate(R.activate(10000, null, cfg), cfg).tierName, 'Test One');
    assert.equal(R.observeStake(R.activate(10000, null, cfg), 1000, cfg).effectiveSkr, 0, 'under the injected floor');
    assert.equal(R.applyPulse(R.activate(10000, null, cfg), 10000, cfg).continuousPulseCount, 1);
});

test('[purity] the transitions never mutate their input state', () => {
    const s = R.activate(1000);
    const snapshot = JSON.stringify(s);
    R.applyPulse(s);
    R.observeStake(s, 10);
    R.evaluate(s);
    assert.equal(JSON.stringify(s), snapshot);
});

test('[purity] the module opens nothing - no fs, no net, no db, no clock, no random', () => {
    const src = require('node:fs').readFileSync(
        require('node:path').join(__dirname, '..', 'api', '_lib', 'heartbound-resonance.js'), 'utf8');
    for (const banned of ['node:fs', 'node:https', 'node:http', 'fetch(', 'neon(', 'Date.now', 'Math.random', 'process.env']) {
        assert.ok(!src.includes(banned), `heartbound-resonance.js must not reference ${banned}`);
    }
    // Exactly one require, and it is the config table.
    const requires = src.match(/require\(/g) || [];
    assert.equal(requires.length, 1);
    assert.ok(src.includes("require('./heartbound-resonance-config.json')"));
});
