'use strict';

// =============================================================================
// test/heartbound-events.test.js - WO-1678 / HEART-005. THE ORACLE.
// -----------------------------------------------------------------------------
// Pins api/_lib/heartbound-events.js against the spec at
// docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:469-616 and the
// owner rulings of 2026-09-10.
//
// ⛔ DETERMINISM IS ASSERTED BY RE-ROLLING, NOT BY READING THE SOURCE. Spec :503 -
// "Same inputs must always return the same event." A module that called Math.random
// would still look deterministic to a reader; it cannot survive a thousand repeated
// rolls compared against the first.
//
// ⛔ AND THE INVERSE IS ASSERTED TOO. A "deterministic" implementation that ignored
// its inputs and always returned row 0 would pass every equality check above. So the
// distribution over many players is asserted to hit EVERY eligible row, and the
// table version is asserted to move the answer.
//
// ⛔ CONFIG-DRIVEN IS PROVEN BY INJECTION. [config] feeds a deliberately different
// table and asserts the answers move. A weight or duration baked into the arithmetic
// would survive any assertion made against the real config; it cannot survive this.
//
// ⛔ THE RULINGS ARE CASES, NOT COMMENTS. The dropped crafting-ingredient event, the
// deferred vendor event, the four banned words and the no-combat-power allow-list are
// each asserted against the shipped table, so a future edit that re-adds one fails
// here before it reaches a human.
//
//     node --test test/heartbound-events.test.js
//
// Zero network, zero database, zero Unity, zero fs. Node built-ins only.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');

const E = require('../api/_lib/heartbound-events.js');
const CONFIG = require('../api/_lib/heartbound-events-config.json');

const PULSE = 'pulse-2026-09-10T00:00:00Z-000123';
const PLAYER = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const TOP_TIER = 10;

/**
 * The AUTHORED DATA of the table - rows, kinds and version - with every `_`-prefixed
 * prose key dropped.
 *
 * ⚠ THE DISTINCTION IS NOT COSMETIC AND THIS TEST FILE PROVED IT. The first draft
 * swept the whole serialised config for substrings, and the config's own English
 * explanation of the rulings failed it: "resolving" contains "sol", "consolation"
 * contains "sol", "an actor" fails an "actor" sweep. A lint that fires on the prose
 * written to PREVENT a defect is a lint somebody deletes - the same trap CLAUDE.md
 * records for HeartfireRegression, which strips comments for exactly this reason.
 * So the DATA is swept here, and the prose is swept only for tokens that have no
 * innocent English form (an item id, a feature name).
 */
function authoredData() {
    return JSON.stringify({
        tableVersion: CONFIG.tableVersion,
        kinds: CONFIG.kinds,
        rows: CONFIG.rows,
    }).toLowerCase();
}

/** A module's source with comments stripped - the header states prohibitions using
 *  the very tokens a purity sweep looks for, so the sweep must read CODE only. */
function codeOf(relative) {
    const src = require('node:fs').readFileSync(require.resolve(relative), 'utf8');
    return src.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/^\s*\/\/.*$/gm, ' ');
}

// ── determinism ──────────────────────────────────────────────────────────────

test('[determinism] the same (pulseId, playerId, tableVersion) returns the same event, 1000 times', () => {
    const first = E.rollEvent({ globalPulseId: PULSE, playerId: PLAYER, tier: TOP_TIER });
    assert.ok(first, 'a top-tier player must resolve an event');
    for (let i = 0; i < 1000; i++) {
        const again = E.rollEvent({ globalPulseId: PULSE, playerId: PLAYER, tier: TOP_TIER });
        assert.deepEqual(again, first, `roll ${i} diverged from the first`);
    }
});

test('[determinism] the seed is SHA-256 of the three inputs and nothing else', () => {
    const hex = E.seedHex(PULSE, PLAYER, 1);
    assert.match(hex, /^[0-9a-f]{64}$/);
    assert.equal(hex, E.seedHex(PULSE, PLAYER, 1), 'the digest is not stable');
    assert.notEqual(hex, E.seedHex(PULSE, PLAYER, 2), 'the table version must be inside the seed');
    assert.notEqual(hex, E.seedHex(PULSE, PLAYER + 'x', 1), 'the player must be inside the seed');
    assert.notEqual(hex, E.seedHex(PULSE + 'x', PLAYER, 1), 'the pulse must be inside the seed');
});

test('[determinism] the seed separator prevents a boundary collision between pulse and player', () => {
    // Without a separator, ("ab","c") and ("a","bc") would concatenate identically.
    assert.notEqual(E.seedHex('ab', 'c', 1), E.seedHex('a', 'bc', 1));
});

test('[determinism] a table-version bump re-rolls, which is exactly why a row edit must bump it', () => {
    const v1 = { ...CONFIG, tableVersion: 1 };
    const v2 = { ...CONFIG, tableVersion: 2 };
    // Over a population, at least one player's event must move; a version that changed
    // nothing for anybody would mean the version is not in the seed.
    let moved = 0;
    for (let i = 0; i < 200; i++) {
        const p = `player-${i}`;
        const a = E.rollEvent({ globalPulseId: PULSE, playerId: p, tier: TOP_TIER }, v1);
        const b = E.rollEvent({ globalPulseId: PULSE, playerId: p, tier: TOP_TIER }, v2);
        if (a.eventId !== b.eventId) moved++;
        assert.equal(a.tableVersion, 1);
        assert.equal(b.tableVersion, 2);
    }
    assert.ok(moved > 0, 'a version bump changed nobody\'s event - the version is not in the seed');
});

// ── the roll is not a constant ───────────────────────────────────────────────

test('[spread] over a population every eligible row is reachable, so "deterministic" is not "constant"', () => {
    const seen = new Set();
    for (let i = 0; i < 5000; i++) {
        seen.add(E.rollEvent({ globalPulseId: PULSE, playerId: `player-${i}`, tier: TOP_TIER }).eventId);
    }
    const eligible = E.eligibleRows(TOP_TIER).map((r) => r.id);
    for (const id of eligible) assert.ok(seen.has(id), `row "${id}" was never reachable in 5000 rolls`);
    assert.equal(seen.size, eligible.length, 'a row outside the eligible set was returned');
});

test('[weight] the heaviest row is drawn more often than the lightest, at the authored ratio order', () => {
    const counts = new Map();
    for (let i = 0; i < 20000; i++) {
        const id = E.rollEvent({ globalPulseId: PULSE, playerId: `p${i}`, tier: TOP_TIER }).eventId;
        counts.set(id, (counts.get(id) || 0) + 1);
    }
    const authored = E.eligibleRows(TOP_TIER).slice().sort((a, b) => b.weight - a.weight);
    const heaviest = authored[0];
    const lightest = authored[authored.length - 1];
    assert.ok(counts.get(heaviest.id) > counts.get(lightest.id),
        `${heaviest.id} (weight ${heaviest.weight}) drew ${counts.get(heaviest.id)}, ` +
        `${lightest.id} (weight ${lightest.weight}) drew ${counts.get(lightest.id)}`);
});

// ── tier gating ──────────────────────────────────────────────────────────────

test('[tier] a row is unreachable below its minTier, and reachable at it', () => {
    for (const row of E.rows()) {
        const below = E.eligibleRows(row.minTier - 1).map((r) => r.id);
        const at = E.eligibleRows(row.minTier).map((r) => r.id);
        if (row.minTier > 0) assert.ok(!below.includes(row.id), `${row.id} leaked below minTier`);
        assert.ok(at.includes(row.id), `${row.id} is not eligible at its own minTier`);
    }
});

test('[tier] tier 0 unlocks nothing and returns null - never a consolation row', () => {
    assert.equal(E.eligibleRows(0).length, 0, 'the table must gate every row above tier 0');
    assert.equal(E.rollEvent({ globalPulseId: PULSE, playerId: PLAYER, tier: 0 }), null);
    assert.deepEqual(E.toClientPayload(null), { hasEvent: false });
});

test('[tier] higher tiers unlock MORE VARIETIES, never bigger numbers (spec :604-615)', () => {
    let previous = 0;
    for (let t = 0; t <= TOP_TIER; t++) {
        const n = E.eligibleRows(t).length;
        assert.ok(n >= previous, `tier ${t} unlocked fewer rows than tier ${t - 1}`);
        previous = n;
    }
    // And the reward amounts are identical at every tier: the table is the same table.
    const low = E.rows().find((r) => r.id === 'resource_surge');
    assert.ok(low, 'resource_surge is the low-tier control row for this assertion');
    for (const t of [1, 5, 10]) {
        const row = E.eligibleRows(t).find((r) => r.id === 'resource_surge');
        assert.equal(row.magnitude, low.magnitude, `tier ${t} scaled a reward magnitude`);
        assert.equal(row.durationSeconds, low.durationSeconds, `tier ${t} scaled a duration`);
    }
});

// ── the owner rulings, as cases ──────────────────────────────────────────────

test('[ruled] the dropped crafting-ingredient event is absent from the table and its payloads', () => {
    // Owner ruling 2026-09-10 13:12. Asserted against the SERIALISED table so a row,
    // a note, a comment or a reward field naming it all fail the same way.
    const text = JSON.stringify(CONFIG).toLowerCase();
    for (const token of ['rough_stone', 'roughstone', 'rough stone', 'ing_rough']) {
        assert.ok(!text.includes(token), `the event table names "${token}" - the event was DROPPED, not deferred`);
    }
});

test('[ruled] the visiting-vendor event left this table for its own work order', () => {
    const text = JSON.stringify(CONFIG).toLowerCase();
    for (const token of ['merchant', 'wandering']) {
        assert.ok(!text.includes(token), `the event table names "${token}" - it belongs to the visiting-NPC ticket`);
    }
});

test('[ruled] Heartfire has exactly ONE row, capped at the authored charge count, and never purchasable', () => {
    // Owner ruling 2026-09-10 13:12: a second source is allowed, and it is THIS one.
    const heartfire = E.rows().filter((r) => r.kind === 'heartfire');
    assert.equal(heartfire.length, 1, 'exactly one Heartfire source may exist in the table');
    assert.equal(heartfire[0].id, 'heartfire_spark');
    assert.ok(heartfire[0].charges >= 1);
    const text = JSON.stringify(CONFIG).toLowerCase();
    for (const token of ['price', 'cost', 'purchase', 'skr', 'usd']) {
        assert.ok(!text.includes(token), `the event table names "${token}" - no event may be bought`);
    }
});

test('[ruled] Echo Labor is a MODIFIER, so no world actor is implied by the table', () => {
    const labor = E.rows().find((r) => r.id === 'echo_labor');
    assert.ok(labor, 'echo_labor is the Tier II identity row');
    assert.equal(labor.kind, 'modifier');
    assert.equal(labor.minTier, 2, 'Tier II identity (HEART-006)');
    assert.ok(typeof labor.magnitude === 'number');
    const text = authoredData();
    for (const token of ['spawn', 'prefab', 'actor', 'figure']) {
        assert.ok(!text.includes(token), `the event table names "${token}" - Echo Labor moves numbers only`);
    }
});

test('[ruled] Scout carries NO new intel fields - only the moment moves', () => {
    const scout = E.rows().find((r) => r.kind === 'scout');
    assert.ok(scout, 'the scout row must exist');
    for (const forbidden of ['composition', 'resistance', 'recommendedTroop', 'intel', 'preview']) {
        assert.equal(scout[forbidden], undefined, `the scout row authored "${forbidden}" - that is NEW information`);
    }
    // Its only payload is how long the early reveal lasts.
    const reward = E.rollEvent({ globalPulseId: PULSE, playerId: 'scout-seeker', tier: TOP_TIER });
    assert.ok(reward);
    const rewardOfScout = E.rewardOf(scout);
    assert.deepEqual(Object.keys(rewardOfScout).sort(), ['durationSeconds', 'kind']);
});

// ── the covenant: no combat power, no withdrawable reward, no banned copy ────

test('[covenant] every row kind is on the allow-list, and combat power is not on it', () => {
    for (const row of E.rows()) {
        assert.ok(E.ALLOWED_KINDS.includes(row.kind), `row "${row.id}" has kind "${row.kind}"`);
    }
    assert.deepEqual(E.ALLOWED_KINDS.slice().sort(), ['heartfire', 'item', 'modifier', 'scout']);
    // ⛔ AND THE TWO COPIES OF THE ALLOW-LIST ARE ASSERTED EQUAL. The config declares
    // `kinds` for the reader and the code enforces ALLOWED_KINDS; that is duplicated
    // state, which is this repo's most expensive recurring defect (CLAUDE.md sections 2,
    // 5, 8, 16 are each one instance of it). Two copies are tolerable ONLY while
    // something fails when they disagree - so this is that something.
    assert.deepEqual(CONFIG.kinds.slice().sort(), E.ALLOWED_KINDS.slice().sort(),
        'the config\'s kinds list and the module\'s ALLOWED_KINDS have drifted apart');
    const text = authoredData();
    for (const stat of ['damage', 'attack', 'armor', 'crit', 'lifesteal', 'dps', 'penetration']) {
        assert.ok(!text.includes(stat), `the event table names the combat stat "${stat}" (owner ruling Q-P2W)`);
    }
});

test('[covenant] no row pays a withdrawable asset and no probability field exists', () => {
    const text = authoredData();
    for (const token of ['sol', 'usdc', 'token', 'withdraw', 'airdrop']) {
        assert.ok(!text.includes(token), `the event table names "${token}"`);
    }
    for (const token of ['probability', 'odds', 'chance', 'random', '"roll"', 'droprate']) {
        assert.ok(!text.includes(token), `the event table carries the probability field "${token}"`);
    }
});

test('[covenant] the four banned words appear nowhere in the table, and the lint is whole-word', () => {
    const text = JSON.stringify(CONFIG).toLowerCase();
    for (const banned of E.BANNED_WORDS) {
        assert.ok(!new RegExp(`\\b${banned}\\b`).test(text), `the event table says "${banned}" (spec :483-491)`);
    }
    assert.throws(() => E.assertCopyIsClean('a jackpot awaits'), /jackpot/);
    assert.throws(() => E.assertCopyIsClean('place your bet'), /bet/);
    // Whole-word, so honest copy survives: a lint that fires on "better" gets deleted.
    assert.equal(E.assertCopyIsClean('the harvest is better today'), true);
    assert.equal(E.assertCopyIsClean('an alphabet of echoes'), true);
});

// ── config injection ─────────────────────────────────────────────────────────

test('[config] weights, durations and gates come from config - a different table gives different answers', () => {
    const injected = {
        tableVersion: 1,
        rows: [
            { id: 'aaa_only_row', kind: 'modifier', weight: 1, minTier: 1, modifier: 'gather_rate', magnitude: 0.99, durationSeconds: 11 },
        ],
    };
    const rolled = E.rollEvent({ globalPulseId: PULSE, playerId: PLAYER, tier: TOP_TIER }, injected);
    assert.equal(rolled.eventId, 'aaa_only_row');
    assert.equal(rolled.reward.magnitude, 0.99, 'the magnitude was not read from the injected config');
    assert.equal(rolled.reward.durationSeconds, 11, 'the duration was not read from the injected config');
});

test('[config] the row order is id-sorted, so re-ordering the JSON cannot re-roll history', () => {
    const forward = { tableVersion: 7, rows: [
        { id: 'alpha', kind: 'modifier', weight: 1, minTier: 1 },
        { id: 'beta', kind: 'modifier', weight: 1, minTier: 1 },
    ] };
    const reversed = { tableVersion: 7, rows: forward.rows.slice().reverse() };
    for (let i = 0; i < 50; i++) {
        const a = E.rollEvent({ globalPulseId: PULSE, playerId: `p${i}`, tier: 1 }, forward);
        const b = E.rollEvent({ globalPulseId: PULSE, playerId: `p${i}`, tier: 1 }, reversed);
        assert.equal(a.eventId, b.eventId, 're-ordering the JSON changed a player\'s event');
    }
});

test('[config] a malformed table is REFUSED loudly, never silently defaulted', () => {
    assert.throws(() => E.rows({ tableVersion: 1, rows: [] }), /empty/);
    assert.throws(() => E.rows({ tableVersion: 1, rows: [{ id: 'x', kind: 'damage_boost', weight: 1 }] }), /kind/);
    assert.throws(() => E.rows({ tableVersion: 1, rows: [{ id: 'x', kind: 'modifier', weight: 0 }] }), /weight/);
    assert.throws(() => E.rows({ tableVersion: 1, rows: [
        { id: 'x', kind: 'modifier', weight: 1 }, { id: 'x', kind: 'modifier', weight: 1 }] }), /duplicate/);
    assert.throws(() => E.tableVersion({ tableVersion: 0 }), /tableVersion/);
    assert.throws(() => E.seedHex('', PLAYER, 1), /globalPulseId/);
    assert.throws(() => E.seedHex(PULSE, '', 1), /playerId/);
});

// ── the client contract ──────────────────────────────────────────────────────

test('[contract] toClientPayload emits exactly the keys the Unity parser reads', () => {
    const rolled = E.rollEvent({ globalPulseId: PULSE, playerId: PLAYER, tier: TOP_TIER });
    const payload = E.toClientPayload(rolled);
    assert.deepEqual(Object.keys(payload).sort(),
        ['claimId', 'eventId', 'hasEvent', 'kind', 'reward', 'tableVersion']);
    assert.equal(payload.hasEvent, true);
    assert.equal(typeof payload.claimId, 'string');
    // ⛔ THE SEED DOES NOT CROSS THE WIRE. A client holding the seed could enumerate
    // the table offline and know its next event; the payload carries the ANSWER only.
    assert.equal(payload.seedHex, undefined);
    assert.ok(rolled.seedHex, 'the server keeps the seed for its own audit trail');
});

test('[contract] the claimId is stable per pulse+version, which is what once-only keys on', () => {
    const a = E.rollEvent({ globalPulseId: PULSE, playerId: PLAYER, tier: TOP_TIER });
    const b = E.rollEvent({ globalPulseId: PULSE, playerId: PLAYER, tier: TOP_TIER });
    assert.equal(a.claimId, b.claimId);
    const other = E.rollEvent({ globalPulseId: PULSE + '-next', playerId: PLAYER, tier: TOP_TIER });
    assert.notEqual(a.claimId, other.claimId, 'two pulses must not share a claim key');
});

test('[purity] rolling never mutates the config and never touches the clock', () => {
    const before = JSON.stringify(CONFIG);
    for (let i = 0; i < 100; i++) E.rollEvent({ globalPulseId: `pulse-${i}`, playerId: PLAYER, tier: TOP_TIER });
    assert.equal(JSON.stringify(CONFIG), before, 'the authored table was mutated by a roll');
    const src = codeOf('../api/_lib/heartbound-events.js');
    for (const forbidden of ['Math.random', 'Date.now', 'randomBytes', 'node:fs']) {
        assert.ok(!src.includes(forbidden), `heartbound-events.js calls ${forbidden} - it must be pure`);
    }
});
