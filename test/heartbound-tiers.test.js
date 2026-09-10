// =============================================================================
// test/heartbound-tiers.test.js - WO-1679 / HEART-006 + WO-1682 / HEART-009.
// -----------------------------------------------------------------------------
// THE METER IS THE POINT OF THIS FILE.
//
// WO-1682 §0c: "A ceiling with no meter is a comment, not a guardrail" - and it
// names THREE gates in this exact feature area (JewelPolishRegression,
// SkrStakingRegression, StakingComplianceRegression) that were cited in code as
// live protection and had never existed. So this suite does not assert that the
// ceiling is respected; it MEASURES the sum and prints it, and it proves the
// meter goes RED by feeding it a table that breaches - never by editing the
// shipped config, which would prove nothing about the shipped config.
//
//     node --test test/heartbound-tiers.test.js
//
// Zero network, zero database, zero Unity. Node built-ins only.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const tiers = require('../api/_lib/heartbound-tiers');
const CONFIG = require('../api/_lib/heartbound-tiers-config.json');
const resonance = require('../api/_lib/heartbound-resonance');
const RESONANCE_CONFIG = require('../api/_lib/heartbound-resonance-config.json');

const REPO = path.resolve(__dirname, '..');
const MODULE_PATH = path.join(REPO, 'api/_lib/heartbound-tiers.js');
const CONFIG_PATH = path.join(REPO, 'api/_lib/heartbound-tiers-config.json');

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE METER - Q-METER, MEASURED
// ═══════════════════════════════════════════════════════════════════════════

test('THE MEASUREMENT: the whole ladder cumulative production-rate sum is inside the ceiling', () => {
    const m = tiers.ladderMeter();
    // Printed, not just asserted: the RESULT quotes this line.
    console.log('    HEARTBOUND_CEILING_MEASURED sum=' + m.sum + ' ceiling=' + m.ceiling +
                ' counted=' + m.counted.length + ' excluded=' + m.excluded.length +
                ' rows=' + m.counted.map((c) => 'T' + c.tier + ':' + c.id + '=' + c.value).join(','));
    assert.equal(m.withinCeiling, true,
        'the cumulative production-rate sum ' + m.sum + ' breaches the ' + m.ceiling + ' ceiling');
    assert.equal(m.ceiling, 0.10, 'HEART-009 spec :903-910 sets the ceiling at 10%');
    assert.equal(m.sum, 0.06, 'the shipped table sums to 6% - three +2% rows at tiers I, II and VII');
});

test('Q-METER: ONLY production-rate kinds are counted, and the exclusions are REPORTED', () => {
    const m = tiers.ladderMeter();
    for (const c of m.counted) assert.equal(c.kind, 'productionRate');
    const excludedKinds = Array.from(new Set(m.excluded.map((e) => e.kind))).sort();
    assert.deepEqual(excludedKinds, ['eventUnlock', 'information', 'polishAttempts', 'presentation'],
        'every non-rate kind in the shipped table must appear in `excluded`, so the ' +
        'ruling is visible rather than silent');
    for (const e of m.excluded) assert.match(e.why, /Q-METER/);
    assert.match(m.definition, /timers and event drops are NOT counted/i);
    assert.match(m.definition, /design guardrail, NOT an\s+anti-cheat control/i);
});

test('RED BEFORE GREEN: a table that breaches the ceiling is REPORTED AS BREACHING', () => {
    // ⛔ The shipped config is NOT edited to prove this. An injected table is fed to
    // the same pure function - which is what makes the meter a function rather than
    // a fact about one file.
    const breaching = JSON.parse(JSON.stringify(CONFIG));
    breaching.tiers[1].benefits[0].value = 0.09;   // 0.09 + 0.02 + 0.02 = 0.13 > 0.10
    const m = tiers.ladderMeter(breaching);
    assert.equal(m.sum, 0.13);
    assert.equal(m.withinCeiling, false,
        'a 13% ladder must FAIL the 10% ceiling - if this passes, the meter measures nothing');

    // And the shipped table is untouched by that experiment.
    assert.equal(tiers.ladderMeter().sum, 0.06);
});

test('the meter refuses a benefit whose kind nobody classified', () => {
    assert.throws(
        () => tiers.economicAccelerationMeter([{ id: 'x', kind: 'mysteryMultiplier', value: 5 }]),
        /not declared in/,
        'an unclassified benefit must be REFUSED, never silently uncounted - that is ' +
        'exactly how a modifier would slip past the ceiling');
});

test('a counted kind with no numeric value is a defect, not a zero', () => {
    const bad = JSON.parse(JSON.stringify(CONFIG));
    delete bad.tiers[1].benefits[0].value;
    assert.throws(() => tiers.ladderMeter(bad), /must carry a non-negative\s+numeric value/);
});

test('a missing ceiling THROWS - the imaginary-gate refusal', () => {
    const noCeiling = JSON.parse(JSON.stringify(CONFIG));
    delete noCeiling.economicCeiling.productionRateCeiling;
    assert.throws(() => tiers.ladderMeter(noCeiling), /imaginary gate/);
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE BENEFIT TABLE
// ═══════════════════════════════════════════════════════════════════════════

test('benefits are CUMULATIVE - a Tier X player still holds Tier I\'s passive', () => {
    const t1 = tiers.benefitsForTier(1).map((b) => b.id);
    const t10 = tiers.benefitsForTier(10).map((b) => b.id);
    assert.ok(t1.indexOf('offline_gathering') >= 0);
    for (const id of t1) assert.ok(t10.indexOf(id) >= 0, 'tier 10 lost tier 1\'s ' + id);
    assert.ok(t10.length > t1.length);
    // Every row says which tier it came from, so the panel need not re-derive it.
    for (const b of t10 && tiers.benefitsForTier(10)) assert.equal(typeof b.tier, 'number');
});

test('tier 0, a negative tier and an unknown tier all grant NOTHING', () => {
    assert.deepEqual(tiers.benefitsForTier(0), []);
    assert.deepEqual(tiers.benefitsForTier(-1), []);
    assert.deepEqual(tiers.benefitsForTier(null), []);
    assert.deepEqual(tiers.benefitsForTier('nonsense'), []);
    assert.deepEqual(tiers.polishGrantsForTier(0), { extraWeeklyRerolls: 0, rollCapDelta: 0 });
});

test('⛔ TIER III IS EMPTY BY RULING, NOT BY OVERSIGHT (Q-STONE: drop the event)', () => {
    const row = tiers.tierRows().filter((r) => r.tier === 3)[0];
    assert.deepEqual(row.benefits, [],
        'Q-STONE (RULED 2026-09-10 13:12) dropped Rough Stone Discovery. Nothing was ' +
        'invented to replace it - a substitute would be this lane making a design decision.');
    // Tier III adds nothing over Tier II.
    assert.deepEqual(tiers.benefitsForTier(3).map((b) => b.id),
                     tiers.benefitsForTier(2).map((b) => b.id));
});

test('⛔ NO ROW GRANTS COMBAT POWER (Q-P2W: economic acceleration yes, combat never)', () => {
    const forbidden = /damage|attack|dps|armou?r|defen[cs]e|health|hp|crit|power|troop|army|weapon/i;
    for (const b of tiers.allBenefits()) {
        assert.doesNotMatch(b.id, forbidden,
            'benefit "' + b.id + '" names a combat concept. Spec rule 10 (:25) and the owner\'s ' +
            'standing ruling: whale tier = prestige, never a better army.');
    }
    // Asserted on the SHAPE of the vocabulary too, so a new kind cannot smuggle one in.
    assert.deepEqual(Object.keys(tiers.benefitKinds()).sort(),
        ['eventUnlock', 'information', 'polishAttempts', 'presentation', 'productionRate']);
});

test('every tier 0..10 has a row, and the ladder has exactly the resonance module\'s tiers', () => {
    const mine = tiers.tierRows().map((r) => Number(r.tier));
    const theirs = resonance.tierTable().map((r) => r.tier);
    assert.deepEqual(mine, theirs,
        'BENEFIT TABLE vs LADDER: heartbound-tiers-config.json and ' +
        'heartbound-resonance-config.json name different tiers. A benefit table with a ' +
        'tier the ladder cannot reach is a benefit nobody can earn.');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. ⛔ THE LADDER IS NOT COPIED HERE (Q-LADDER merged; one authority)
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ this module and its config hold NO tier thresholds', () => {
    const source = fs.readFileSync(MODULE_PATH, 'utf8');
    const code = source.split('\n')
        .filter((l) => !/^\s*(\/\/|\*|\/\*)/.test(l))
        .join('\n');
    for (const row of RESONANCE_CONFIG.tiers) {
        if (row.minScore === 0) continue;
        assert.equal(code.indexOf(String(row.minScore)), -1,
            'heartbound-tiers.js contains the threshold ' + row.minScore + '. The ladder ' +
            'lives in heartbound-resonance-config.json and is reached ONLY through ' +
            'heartbound-resonance.js. A second copy is the duplicated-state failure ' +
            'CLAUDE.md §2/§5/§8/§16 each record a scar from.');
    }
    const cfgText = fs.readFileSync(CONFIG_PATH, 'utf8');
    assert.equal(cfgText.indexOf('minScore'), -1,
        'heartbound-tiers-config.json names minScore. It is keyed by tier NUMBER only.');
});

test('the tier is resolved by CALLING the resonance module, not re-derived', () => {
    const source = fs.readFileSync(MODULE_PATH, 'utf8');
    assert.match(source, /require\('\.\/heartbound-resonance'\)/);
    assert.match(source, /resonance\.tierForScore\(/);
    assert.match(source, /resonance\.nextTierAt\(/);
});

test('⛔ NO I/O: no network, no database, no fs, no clock, no randomness', () => {
    const source = fs.readFileSync(MODULE_PATH, 'utf8');
    const code = source.split('\n').filter((l) => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
    for (const forbidden of ['require(\'fs', 'require("fs', 'neon(', 'fetch(', 'Date.now',
                             'new Date', 'Math.random', 'process.env', 'setTimeout']) {
        assert.equal(code.indexOf(forbidden), -1, 'heartbound-tiers.js performs I/O: ' + forbidden);
    }
    // Exactly two requires, and both are the ones the header names.
    const requires = code.match(/require\([^)]*\)/g) || [];
    assert.deepEqual(requires.sort(), [
        "require('./heartbound-resonance')",
        "require('./heartbound-tiers-config.json')",
    ]);
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. Q-LADDER: THE POLISH PERK IS A BENEFIT ROW - ATTEMPTS, NEVER ODDS
// ═══════════════════════════════════════════════════════════════════════════

test('the merged ladder grants the weekly re-roll at Tier I and the roll cap at Tier V', () => {
    assert.deepEqual(tiers.polishGrantsForTier(0), { extraWeeklyRerolls: 0, rollCapDelta: 0 });
    assert.deepEqual(tiers.polishGrantsForTier(1), { extraWeeklyRerolls: 1, rollCapDelta: 0 });
    assert.deepEqual(tiers.polishGrantsForTier(4), { extraWeeklyRerolls: 1, rollCapDelta: 0 });
    assert.deepEqual(tiers.polishGrantsForTier(5), { extraWeeklyRerolls: 1, rollCapDelta: 1 });
    assert.deepEqual(tiers.polishGrantsForTier(10), { extraWeeklyRerolls: 1, rollCapDelta: 1 });
});

test('the Tier V placement is the closest tier to the shipped 10,000 SKR roll-cap threshold', () => {
    // NativeSkrPolishBonus.ExpandedRollCapStake = 10_000 SKR. At zero tenure that
    // stake scores stakePower(10000), and the tier that score reaches is the tier the
    // merged ladder must put the roll cap on, or a shipped perk moves under players.
    const score = resonance.resonanceScore(10000, 0);
    const tier = resonance.tierForScore(score).tier;
    assert.equal(tier, 5, 'stakePower(10000) no longer lands on Tier V - the roll-cap row ' +
        'in heartbound-tiers-config.json must move with it, or a 10k staker silently loses ' +
        'a perk they already had.');
    assert.equal(tiers.polishGrantsForTier(tier).rollCapDelta, 1);
    // And the minimum Heartbound stake is Tier I, where the weekly re-roll sits.
    assert.equal(tiers.polishGrantsForTier(1).extraWeeklyRerolls, 1);
});

test('⛔ a polish row naming an ODDS-shaped grant is REFUSED', () => {
    const odds = JSON.parse(JSON.stringify(CONFIG));
    odds.tiers[1].benefits[1].grant = 'shatterChanceDelta';
    assert.throws(() => tiers.benefitsForTier(1, odds), /ATTEMPTS, NEVER OUTCOMES/,
        'IPolishBonusProvider has exactly two members and neither is odds-shaped ' +
        '(PolishBonusProvider.cs:20-23). A config row must not be able to add one.');
    assert.deepEqual(tiers.allowedPolishGrants(), ['extraWeeklyRerolls', 'rollCapDelta']);
});

test('a fractional or negative attempt count is refused', () => {
    const frac = JSON.parse(JSON.stringify(CONFIG));
    frac.tiers[1].benefits[1].value = 0.5;
    assert.throws(() => tiers.benefitsForTier(1, frac), /whole number of attempts/);
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. THE INJECTION SEAM heartbound-state.js ALREADY HAS
// ═══════════════════════════════════════════════════════════════════════════

test('resolveTier returns nextTierAt as a SCALAR - heartbound-state.js stringifies it', () => {
    // api/_lib/heartbound-state.js:405-408 does String(resolved.nextTierAt). Handing it
    // heartbound-resonance.nextTierAt()'s OBJECT would put "[object Object]" on the wire,
    // silently. This is the assertion that keeps the two contracts in step.
    const r = tiers.resolveTier({ resonanceScore: '1234.500000', resonanceTier: 3 });
    assert.equal(typeof r.nextTierAt, 'number');
    assert.equal(String(r.nextTierAt), '1500', 'the next threshold above 1234.5 is Tier V at 1500');
    assert.equal(r.tier, 4);
    assert.equal(r.tierName, 'Echoing');
    // The richer object is still available, just not on the field state.js reads.
    assert.equal(r.nextTier.tier, 5);
    assert.equal(r.nextTier.pointsAway, 1500 - 1234.5);
});

test('at the top tier nextTierAt is null, and it is NEVER inferred from the tier number', () => {
    const r = tiers.resolveTier({ resonanceScore: 999999, resonanceTier: 10 });
    assert.equal(r.tier, 10);
    assert.equal(r.nextTierAt, null);
    assert.equal(r.nextTier, null);
});

test('an unreadable score falls back to the STORED tier, never to a guessed one', () => {
    const r = tiers.resolveTier({ resonanceScore: null, resonanceTier: 7 });
    assert.equal(r.tier, 7);
    assert.equal(r.tierName, 'Deep Resonance');
    assert.equal(r.nextTierAt, null, 'with no score there is no distance to the next tier');
    assert.equal(r.benefits.length, tiers.benefitsForTier(7).length);

    const blind = tiers.resolveTier({});
    assert.equal(blind.tier, 0);
    assert.deepEqual(blind.benefits, []);
});

test('resolveTier carries the meter, the polish grants and the unlocked events', () => {
    const r = tiers.resolveTier({ resonanceScore: 3200, resonanceTier: 10 });
    assert.equal(r.tier, 10);
    assert.equal(r.meter.sum, 0.06);
    assert.equal(r.meter.withinCeiling, true);
    assert.deepEqual(r.polish, { extraWeeklyRerolls: 1, rollCapDelta: 1 });
    assert.deepEqual(r.unlockedEvents.sort(),
        ['ancient_echo', 'crafting_inspiration', 'event_choice', 'heartfire_spark']);
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. THE WIRE PAYLOAD - the client is TOLD, it never DERIVES
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the status payload carries NO threshold, NO score, NO stake and NO wallet', () => {
    const wire = JSON.stringify(tiers.statusBenefitsPayload(10));
    for (const row of RESONANCE_CONFIG.tiers) {
        if (row.minScore === 0) continue;
        assert.equal(wire.indexOf(String(row.minScore)), -1,
            'the wire payload leaks the threshold ' + row.minScore + '. A client holding it ' +
            'could reconstruct the ladder, which is the second authority Q-CONFIG forbids.');
    }
    assert.doesNotMatch(wire, /minScore|resonanceScore|stake|wallet|skr/i);
});

test('the status payload is flat, complete and applyable without any client arithmetic', () => {
    const p = tiers.statusBenefitsPayload(2);
    assert.equal(p.tier, 2);
    assert.equal(p.productionRateSum, 0.04, 'tiers I + II = 2% + 2%');
    assert.equal(p.productionRateCeiling, 0.10);
    for (const r of p.rows) {
        assert.equal(typeof r.id, 'string');
        assert.equal(typeof r.kind, 'string');
        assert.equal(typeof r.tier, 'number');
        assert.ok(r.value === null || typeof r.value === 'number');
    }
    assert.deepEqual(tiers.statusBenefitsPayload(0),
        { tier: 0, rows: [], productionRateSum: 0, productionRateCeiling: 0.10,
          polish: { extraWeeklyRerolls: 0, rollCapDelta: 0 }, unlockedEvents: [] },
        'an unverified / unstaked / unknown player gets the ZERO payload on every path');
});

// ═══════════════════════════════════════════════════════════════════════════
// 7. THE CONFIG SAYS WHERE ITS NUMBERS CAME FROM
// ═══════════════════════════════════════════════════════════════════════════

test('every chosen-not-ruled number is labelled as chosen-not-ruled', () => {
    const notes = CONFIG._authoringNotes.join('\n');
    assert.match(notes, /CHOSEN, NOT RULED/);
    assert.match(notes, /Q-STONE/);
    assert.match(notes, /Q-LADDER/);
    assert.match(notes, /Q-P2W/);
    // The one value the spec actually states is identified as the spec's own.
    assert.match(notes, /THE SPEC'S OWN NUMBER/);
});

test('the ceiling block records Q-METER and Q-CLIENTECON verbatim enough to act on', () => {
    const c = CONFIG.economicCeiling._comment.join('\n');
    assert.match(c, /TIMERS AND EVENT DROPS ARE NOT COUNTED/);
    assert.match(c, /DESIGN GUARDRAIL, NOT AN\s+ANTI-CHEAT CONTROL/);
    assert.match(c, /save\.js:70-72/);
});
