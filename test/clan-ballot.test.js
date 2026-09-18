// =============================================================================
// test/clan-ballot.test.js — WO-1853 (clan WO-10). The perk ballot.
// -----------------------------------------------------------------------------
//     node --test test/clan-ballot.test.js
//
// ⭐ WHAT THIS SUITE IS FOR. Two rules decide a ballot and they are INDEPENDENT (the
// owner's ruling of 2026-09-17): the WINNER is a plurality of tenure-weighted vote
// weight, and WHETHER IT PASSES is a size-scaled participation threshold layered on
// top. A ballot with a clear weighted winner that fails turnout is the case the whole
// ticket turns on, so it is tested in both directions — pass and fail — at the library
// AND at the endpoint.
//
// ⛔ THE VIGIL ARITHMETIC IS NOT RE-PROVEN HERE. percent_staked, tenure, the 60-second
// cache, the degraded paths and the two-token-account sum are proven against a real
// local HTTP JSON-RPC server in test/clan-vigil.test.js, which is where they belong. This
// suite injects the Vigil read — through `opts.readVigil` at the library seam, and by
// swapping skr-staking's exported getStakedPercentage for the route tests — so a ballot
// test that fails tells you something about the BALLOT. Re-proving a proven layer is how
// a suite gets slow and starts failing for reasons that are not its subject.
//
// ⛔ recordingSql IS THIS DIRECTORY'S ESTABLISHED HARNESS, carried per-suite exactly as
// clan-roles / clan-membership / clan-report-message / clan-leaderboard / clan-vigil each
// carry their own copy. It records the SQL TEXT, which is what lets a test assert an
// invariant that lives in a statement (an ON CONFLICT clause that names one column, a
// WHERE that closes a race) rather than only its result.
//
// Zero database, zero mainnet, zero Unity. The epoch SQL is the one statement that was
// ALSO run against the live PostgreSQL — that proof is recorded in the work order's
// implementation record, not re-run here (a test that needs the network fails on a plane).
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const CLAN_LIB = path.join(REPO, 'api', '_lib', 'clan.js');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const CLAN_VIGIL = path.join(REPO, 'api', '_lib', 'clan-vigil.js');
const CLAN_BALLOT = path.join(REPO, 'api', '_lib', 'clan-ballot.js');
const WALLET_AUTH = path.join(REPO, 'api', '_lib', 'wallet-auth.js');
const SKR_PATH = path.join(REPO, 'api', '_lib', 'skr-staking.js');
const MIGRATION = path.join(REPO, 'api', 'migrations', '20260917_0035_clan_ballots.sql');
const SCHEMA_SQL = path.join(REPO, 'api', 'schema.sql');
const ROUTES = {
    propose: path.join(REPO, 'api', 'clan', 'ballot', 'propose.js'),
    vote: path.join(REPO, 'api', 'clan', 'ballot', 'vote.js'),
    current: path.join(REPO, 'api', 'clan', 'ballot', 'current.js'),
};

const ballot = require(CLAN_BALLOT);
const { AuthCode } = require(WALLET_AUTH);
const { ClanCode } = require(CLAN_LIB);

// Two real, distinct base58 wallets (they only have to decode to 32 bytes).
const WALLET_A = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const WALLET_B = 'DLQtTaJKEU8yCxC2boPMdttJf3BWDNzFbJXGei8upiUZ';
const CLAN_ID = '3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e';
const BALLOT_ID = '9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d';
const OTHER_BALLOT_ID = '11112222-3333-4444-5555-666677778888';
const SESSION = 'a'.repeat(44);

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE TIER GATE — acceptance criteria 1 and 2
// ═══════════════════════════════════════════════════════════════════════════

test('ACCEPTANCE 1: a clan with vigil_weight = 0 cannot open a Tier 1 ballot', () => {
    // Tier 1's bar is "> 0", the one strict comparison in the ladder.
    assert.equal(ballot.meetsTier(0, 1), false);
    assert.equal(ballot.meetsTier(-0, 1), false);
    assert.equal(ballot.meetsTier(0.0000001, 1), true, 'any Vigil at all opens Tier 1');
    assert.equal(ballot.TIER_THRESHOLDS[1], 0, 'the stored bar is 0; meetsTier owns the strictness');
});

test('ACCEPTANCE 2: a clan at or above T2 can open a Tier 2 ballot, and below it cannot', () => {
    const T2 = ballot.TIER_THRESHOLDS[2];
    assert.equal(ballot.meetsTier(T2, 2), true, 'the bar is inclusive for every tier but 1');
    assert.equal(ballot.meetsTier(T2 - 1, 2), false);
    assert.equal(ballot.meetsTier(T2 + 1, 2), true);
});

test('the tier ladder is strictly increasing and every tier 1..5 has a bar', () => {
    for (let t = ballot.MIN_TIER; t <= ballot.MAX_TIER; t++) {
        assert.equal(typeof ballot.TIER_THRESHOLDS[t], 'number', 'tier ' + t + ' has no threshold');
    }
    for (let t = 2; t <= ballot.MAX_TIER; t++) {
        assert.ok(ballot.TIER_THRESHOLDS[t] > ballot.TIER_THRESHOLDS[t - 1],
            'tier ' + t + ' must be a higher bar than tier ' + (t - 1));
    }
    // ⚠ THE MAGNITUDES ARE PLACEHOLDERS (the work order says so). What is asserted is the
    // UNIT — fully-staked member-seconds — because that is what makes the numbers
    // retunable by reading them: T3 is one member-week.
    assert.equal(ballot.TIER_THRESHOLDS[2], 86400, 'one fully-staked member-day');
    assert.equal(ballot.TIER_THRESHOLDS[3], 604800, 'one fully-staked member-week');
});

test('a tier outside 1..5, or not an integer, is refused before anything else happens', () => {
    for (const bad of [undefined, null, '', 0, 6, -1, 2.5, 'two', {}, []]) {
        const r = ballot.normalizeTier(bad);
        assert.equal(r.ok, false, 'must refuse: ' + JSON.stringify(bad));
        assert.equal(r.code, ballot.BallotCode.BAD_TIER);
    }
    assert.deepEqual(ballot.normalizeTier(3), { ok: true, value: 3 });
    assert.deepEqual(ballot.normalizeTier('4'), { ok: true, value: 4 },
        'a JSON body that sent the tier as a string still works');
});

test('the CHECK constraint in the migration matches the tier range the code enforces', () => {
    const sql = fs.readFileSync(MIGRATION, 'utf8');
    assert.match(sql, /CONSTRAINT\s+clan_ballots_tier_range\s+CHECK\s*\(tier\s+BETWEEN\s+1\s+AND\s+5\)/i);
    assert.equal(ballot.MIN_TIER, 1);
    assert.equal(ballot.MAX_TIER, 5);
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE OPTIONS
// ═══════════════════════════════════════════════════════════════════════════

test('an option id from ANOTHER tier, a duplicate, or an empty list is refused', () => {
    const tier1 = ballot.tierOptions(1).map(o => o.optionId);
    const tier3 = ballot.tierOptions(3).map(o => o.optionId);

    assert.deepEqual(ballot.normalizeOptionIds(tier1, 1), { ok: true, value: tier1 });
    for (const bad of [undefined, null, [], 'build_speed_1', {}, [''], ['nope'],
                       [tier1[0], tier1[0]], [tier3[0]]]) {
        const r = ballot.normalizeOptionIds(bad, 1);
        assert.equal(r.ok, false, 'must refuse: ' + JSON.stringify(bad));
        assert.equal(r.code, ballot.BallotCode.BAD_OPTIONS);
    }
});

test('a one-option Tier 5 ballot is LEGAL, and that is deliberate', () => {
    // The work order's tier table names exactly one Tier 5 option. Inventing a second
    // would be writing game content this lane was not asked to write, so a single-option
    // ballot is allowed — and it still has to clear the participation threshold.
    const t5 = ballot.tierOptions(5).map(o => o.optionId);
    assert.equal(t5.length, 1);
    assert.deepEqual(ballot.normalizeOptionIds(t5, 5), { ok: true, value: t5 });
});

test('every option maps to exactly one perk id, and no perk id is shared across tiers', () => {
    const seen = new Map();
    for (let t = ballot.MIN_TIER; t <= ballot.MAX_TIER; t++) {
        for (const o of ballot.tierOptions(t)) {
            assert.ok(o.perkId, 'option ' + o.optionId + ' has no perk id');
            assert.equal(ballot.perkIdFor(t, o.optionId), o.perkId);
            assert.ok(!seen.has(o.perkId), 'perk id reused: ' + o.perkId);
            seen.set(o.perkId, t);
        }
    }
    assert.ok(seen.size >= 10, 'expected the whole five-tier ladder, got ' + seen.size);
});

test('an option id retired from the catalogue renders as itself, never a throw', () => {
    // A closed ballot from last month can name an option this build no longer offers.
    const o = ballot.describeOption(1, 'a_retired_option');
    assert.equal(o.optionId, 'a_retired_option');
    assert.equal(o.perkId, null);
    assert.equal(ballot.perkIdFor(1, 'a_retired_option'), null);
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. THE COPY RULES — stated by the work order, pinned here rather than trusted
// ═══════════════════════════════════════════════════════════════════════════

function everyCopyString() {
    const out = [ballot.BALLOT_HEADLINE];
    for (let t = ballot.MIN_TIER; t <= ballot.MAX_TIER; t++) {
        for (const o of ballot.tierOptions(t)) {
            out.push(o.title, o.effect, o.description);
        }
    }
    return out.filter(s => typeof s === 'string');
}

test('COPY RULE: no player-facing string states a duration in hours or days', () => {
    // "Never state duration in hours/days — use 'epoch' or 'vigil.'"
    for (const s of everyCopyString()) {
        assert.ok(!/\b(hours?|days?|weeks?|minutes?)\b/i.test(s),
            'a duration in real-world units reached player copy: ' + JSON.stringify(s));
    }
});

test('COPY RULE: no perk description uses investment language', () => {
    // "yield" is deliberately NOT in this list: it is this game's farming noun and the
    // work order's own tier table says "+3% harvest yield".
    const banned = /\b(invest|investment|investor|returns?|profit|apy|apr|dividend|interest|earnings?|portfolio)\b/i;
    for (const s of everyCopyString()) {
        assert.ok(!banned.test(s), 'investment language reached player copy: ' + JSON.stringify(s));
    }
});

test('COPY RULE: the headline and the special-troop line are the owner\'s, verbatim', () => {
    assert.equal(ballot.BALLOT_HEADLINE,
        'The ancestors listen to those who gather. Stake together, and they will consider your requests.');
    const troop = ballot.tierOptions(5)[0];
    assert.equal(troop.description, 'The ancestors stir. A new shape is possible.');
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE PARTICIPATION THRESHOLD — the owner's NEW rule (first-pass default)
// ═══════════════════════════════════════════════════════════════════════════

test('the bands are the owner\'s three illustrative fractions, in monotone order', () => {
    // ⚠ THESE NUMBERS ARE THIS LANE'S FIRST-PASS DEFAULT AND ARE FLAGGED FOR THE OWNER.
    // What this test protects is the SHAPE she ruled: a bar that scales with clan size,
    // never decreasing as the clan grows.
    const ratios = ballot.PARTICIPATION_BANDS.map(b => b.requiredRatio);
    for (let i = 1; i < ratios.length; i++) {
        assert.ok(ratios[i] > ratios[i - 1], 'the bar must rise with clan size');
    }
    assert.deepEqual(ballot.PARTICIPATION_BANDS.map(b => b.ownerExample), ['2/4', '2/3', '5/6']);
    assert.equal(ballot.participationBand(4).requiredRatio, 1 / 2);
    assert.equal(ballot.participationBand(5).requiredRatio, 2 / 3);
    assert.equal(ballot.participationBand(13).requiredRatio, 5 / 6);
    assert.equal(ballot.participationBand(9999).requiredRatio, 5 / 6, 'the top band is open-ended');
});

test('requiredVoters CEILS, never rounds, and never exceeds the clan', () => {
    // Rounding DOWN would let fewer members than the fraction names clear the bar, which
    // is the one direction a quorum must never err in. 3 members at 1/2 is ceil(1.5) = 2,
    // which is also the owner's "2/3" example.
    // ⛔ THE WHOLE 1..20 RUN IS PINNED, NOT A SAMPLE OF IT. The work order's FLAG 1 quotes
    // this sequence for the owner to rule on, and the first draft of that table was
    // hand-typed and WRONG at n=8 and n=11 (it read 5 and 7; the answers are 6 and 8)
    // precisely because only 5, 6, 9 and 12 were pinned here. A ruling table must have one
    // source, and this is it — the work order's copy is generated from this function.
    const table = {
        1: 1, 2: 1, 3: 2, 4: 2,                                  // 1/2 band
        5: 4, 6: 4, 7: 5, 8: 6, 9: 6, 10: 7, 11: 8, 12: 8,       // 2/3 band
        13: 11, 14: 12, 15: 13, 16: 14, 17: 15, 18: 15, 19: 16, 20: 17,   // 5/6 band
        100: 84,
    };
    for (const [members, expected] of Object.entries(table)) {
        assert.equal(ballot.requiredVoters(Number(members)), expected,
            'requiredVoters(' + members + ')');
    }
    for (let n = 0; n <= 60; n++) {
        const r = ballot.requiredVoters(n);
        assert.ok(r >= 1, 'a bar of zero voters would let an empty ballot pass (n=' + n + ')');
        if (n > 0) assert.ok(r <= n, 'the bar must be reachable (n=' + n + ')');
    }
});

test('⚠ FLAGGED: in a TWO-member clan one voter clears the shipped bar', () => {
    // This is the shipped first-pass consequence and it is recorded, not hidden. The
    // alternative — requiring 2 of 2 — lets one absent member block the clan forever.
    assert.equal(ballot.requiredVoters(2), 1);
    assert.equal(ballot.evaluateTurnout(1, 2).clears, true);
    // In a FOUR-member clan (the owner's own "2/4") one voter does NOT clear it.
    assert.equal(ballot.requiredVoters(4), 2);
    assert.equal(ballot.evaluateTurnout(1, 4).clears, false);
});

test('weighted turnout is ADVISORY and is null rather than wrong when unknowable', () => {
    // It is reported so the owner can compare both readings on a real ballot; it never
    // decides `clears`. A clan whose whole Vigil is 0 has no fraction to state.
    assert.equal(ballot.evaluateTurnout(2, 2, 10, 40).weightedTurnout, 0.25);
    assert.equal(ballot.evaluateTurnout(2, 2, 0, 0).weightedTurnout, null);
    assert.equal(ballot.evaluateTurnout(2, 2, 10, undefined).weightedTurnout, null);
    const strict = ballot.evaluateTurnout(1, 4, 1000, 1000);
    assert.equal(strict.clears, false,
        '⛔ a whale who is the clan\'s entire weight still cannot pass a ballot alone');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. THE TALLY — plurality BY WEIGHT
// ═══════════════════════════════════════════════════════════════════════════

test('TEST PLAN 1: two members with weights 0.5 and 1.0 voting differently — weight wins', () => {
    const votes = [
        { wallet: WALLET_A, optionId: 'build_speed_1', weight: 0.5 },
        { wallet: WALLET_B, optionId: 'harvest_yield_1', weight: 1.0 },
    ];
    const t = ballot.tallyVotes(votes);
    assert.equal(t.winner, 'harvest_yield_1', 'the heavier Vigil wins, not the first vote cast');
    assert.equal(t.totalWeight, 1.5);
    assert.deepEqual(t.tallies.map(x => x.optionId), ['harvest_yield_1', 'build_speed_1']);

    // ACCEPTANCE 3, stated as arithmetic: two different contributions are two different
    // weights, so one member is not one vote.
    assert.equal(t.tallies[0].weight, 1.0);
    assert.equal(t.tallies[1].weight, 0.5);
});

test('a HEAD-COUNT majority loses to a heavier minority — that is what tenure-weighted means', () => {
    const t = ballot.tallyVotes([
        { wallet: 'a', optionId: 'wall_hp_1', weight: 1 },
        { wallet: 'b', optionId: 'wall_hp_1', weight: 1 },
        { wallet: 'c', optionId: 'build_speed_2', weight: 5 },
    ]);
    assert.equal(t.winner, 'build_speed_2');
    assert.equal(t.tallies[0].voters, 1);
    assert.equal(t.tallies[1].voters, 2);
});

test('ties break deterministically: weight, then voters, then option id', () => {
    const even = ballot.tallyVotes([
        { wallet: 'a', optionId: 'zeta', weight: 3 },
        { wallet: 'b', optionId: 'alpha', weight: 3 },
    ]);
    assert.equal(even.winner, 'alpha', 'the id key is the final, reproducible tie-break');

    const byVoters = ballot.tallyVotes([
        { wallet: 'a', optionId: 'zeta', weight: 2 },
        { wallet: 'b', optionId: 'zeta', weight: 2 },
        { wallet: 'c', optionId: 'alpha', weight: 4 },
    ]);
    assert.equal(byVoters.winner, 'zeta', 'equal weight, more voters');
});

test('a ZERO-WEIGHT electorate still produces a winner', () => {
    // A clan where nobody has staked yet: every tally is 0, so the voter count decides
    // and the id decides after it. "No winner" would make the head-count turnout gate
    // unreachable for exactly the clans most likely to clear it.
    const t = ballot.tallyVotes([
        { wallet: 'a', optionId: 'harvest_yield_1', weight: 0 },
        { wallet: 'b', optionId: 'harvest_yield_1', weight: 0 },
        { wallet: 'c', optionId: 'build_speed_1', weight: 0 },
    ]);
    assert.equal(t.totalWeight, 0);
    assert.equal(t.winner, 'harvest_yield_1');
});

test('no votes at all is no winner and no tallies', () => {
    const t = ballot.tallyVotes([]);
    assert.equal(t.winner, null);
    assert.deepEqual(t.tallies, []);
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. THE VERDICT — the two rules, layered
// ═══════════════════════════════════════════════════════════════════════════

test('THE LOAD-BEARING CASE: a clear weighted winner STILL FAILS on turnout', () => {
    // One voter of four, carrying the whole clan's weight, choosing unambiguously.
    const d = ballot.decideBallot(
        [{ wallet: 'a', optionId: 'build_speed_1', weight: 9999 }], 4, 9999);
    assert.equal(d.winner, 'build_speed_1', 'the plurality winner is unambiguous');
    assert.equal(d.passed, false, '⛔ and the ballot does NOT pass');
    assert.equal(d.reason, 'insufficient_turnout');
    assert.equal(d.turnout.requiredVoters, 2);
    assert.equal(d.turnout.voters, 1);
});

test('TEST PLAN 1 (end to end, pure): both members of a two-member clan vote — it passes', () => {
    const d = ballot.decideBallot([
        { wallet: WALLET_A, optionId: 'build_speed_1', weight: 0.5 },
        { wallet: WALLET_B, optionId: 'harvest_yield_1', weight: 1.0 },
    ], 2, 1.5);
    assert.equal(d.passed, true);
    assert.equal(d.reason, 'passed');
    assert.equal(d.winner, 'harvest_yield_1');
    assert.equal(d.turnout.clears, true);
    assert.equal(d.turnout.weightedTurnout, 1, 'every member voted, so all the weight did');
});

test('TEST PLAN 2: one of two members votes — the shipped formula CLEARS, and says so', () => {
    // The work order asks this case to "correctly report whether that turnout clears or
    // fails... per whichever formula this lane ships". With the shipped 1/2 band it clears,
    // and the numbers that produced that verdict are on the response.
    const d = ballot.decideBallot([{ wallet: WALLET_A, optionId: 'build_speed_1', weight: 0.5 }], 2, 1.5);
    assert.equal(d.passed, true);
    assert.equal(d.turnout.voters, 1);
    assert.equal(d.turnout.requiredVoters, 1);
    assert.equal(d.turnout.requiredRatio, 0.5);
    assert.equal(d.turnout.ratio, 0.5);
});

test('a ballot nobody voted on fails with no_votes, not insufficient_turnout', () => {
    const d = ballot.decideBallot([], 2, 0);
    assert.equal(d.passed, false);
    assert.equal(d.reason, 'no_votes');
    assert.equal(d.winner, null);
});

// ═══════════════════════════════════════════════════════════════════════════
// 7. THE STATEMENTS — invariants that live in SQL
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

const EPOCH_RE = /AS\s+epoch_index/i;
const LATEST_RE = /FROM\s+clan_ballots[\s\S]*ORDER\s+BY\s+opened_at/i;
const BY_ID_RE = /FROM\s+clan_ballots[\s\S]*AND\s+id\s*=/i;
const INSERT_BALLOT_RE = /INSERT\s+INTO\s+clan_ballots/i;
const CLOSE_RE = /UPDATE\s+clan_ballots/i;
const VOTES_RE = /FROM\s+clan_ballot_votes/i;
const INSERT_VOTE_RE = /INSERT\s+INTO\s+clan_ballot_votes/i;
const MEMBER_COUNT_RE = /COUNT\(\*\)::int\s+AS\s+member_count\s+FROM\s+clan_members/i;
const PERKS_RE = /FROM\s+clan_perks/i;
const INSERT_PERK_RE = /INSERT\s+INTO\s+clan_perks/i;
const MEMBERSHIP_RE = /FROM\s+clan_members\s+m[\s\S]*JOIN\s+clans/i;
const ROSTER_RE = /FROM\s+clan_members\s+m\s+LEFT\s+JOIN\s+wallet_identity/i;

function ballotRow(over = {}) {
    return Object.assign({
        id: BALLOT_ID,
        clan_id: CLAN_ID,
        proposed_by_wallet: WALLET_A,
        tier: 1,
        options: ['build_speed_1', 'harvest_yield_1'],
        opened_at: 'T0',
        closes_at: 'T1',
        closed_at: null,
        winning_option: null,
        expired: false,
    }, over);
}

function voteRow(wallet, optionId, weight) {
    return { wallet: wallet, option_id: optionId, weight: weight, voted_at: 'TV' };
}

test('the epoch is computed by POSTGRES, from a BOUND anchor, never by Date.now()', async () => {
    const sql = recordingSql([{ match: EPOCH_RE, rows: [{
        epoch_index: 130, started_at: 'S', ends_at: 'E', now_at: 'N' }] }]);
    const e = await ballot.readEpoch(sql);
    assert.deepEqual(e, { epochIndex: 130, startedAt: 'S', endsAt: 'E', nowAt: 'N' });

    const q = sql.matching(EPOCH_RE)[0];
    assert.match(q.text, /NOW\(\)/, 'the clock is the database\'s, the rule clan-vigil.js:19-25 states');
    assert.match(q.text, /EXTRACT\(EPOCH\s+FROM/i);
    assert.match(q.text, /FLOOR/i, 'whole epochs since the anchor');
    assert.ok(q.values.includes(ballot.EPOCH_ANCHOR_ISO),
        'the anchor is a BOUND parameter, not string-concatenated into the statement');
    assert.ok(q.values.includes(ballot.EPOCH_SECONDS));
    assert.equal(ballot.EPOCH_SECONDS, 172800, 'the owner-ruled 48-hour cadence');
});

test('expiry is a SQL comparison against NOW(), never a JS date compare', async () => {
    const sql = recordingSql([{ match: LATEST_RE, rows: [ballotRow()] }]);
    await ballot.readLatestBallot(sql, CLAN_ID);
    const q = sql.matching(LATEST_RE)[0];
    assert.match(q.text, /\(closed_at\s+IS\s+NULL\s+AND\s+closes_at\s*<=\s*NOW\(\)\)\s+AS\s+expired/i,
        '⛔ the expiry verdict is computed by the same clock that wrote opened_at');
    assert.match(q.text, /WHERE\s+clan_id\s*=\s*\?/i, 'scoped to ONE clan, as a bound parameter');
    assert.match(q.text, /ORDER\s+BY/i, 'deterministic, never an unordered pick');
});

test('a ballot is only ever read inside the caller\'s OWN clan', async () => {
    const sql = recordingSql([{ match: BY_ID_RE, rows: [ballotRow()] }]);
    await ballot.readBallotInClan(sql, CLAN_ID, BALLOT_ID);
    const q = sql.matching(BY_ID_RE)[0];
    assert.match(q.text, /WHERE\s+clan_id\s*=\s*\?\s*AND\s+id\s*=\s*\?/i,
        'another clan\'s ballot id is indistinguishable from a wrong one — that is the privacy rule');
    assert.deepEqual(q.values, [CLAN_ID, BALLOT_ID]);
});

test('closes_at is written from the value POSTGRES returned, and options go in as jsonb', async () => {
    const sql = recordingSql([{ match: INSERT_BALLOT_RE, rows: [ballotRow()] }]);
    await ballot.insertBallot(sql, CLAN_ID, WALLET_A, 1, ['build_speed_1'], 'EPOCH-END');
    const q = sql.matching(INSERT_BALLOT_RE)[0];
    assert.ok(q.values.includes('EPOCH-END'),
        'the anchor arithmetic is spelled ONCE, in readEpoch — this INSERT binds its answer');
    assert.ok(q.values.includes(JSON.stringify(['build_speed_1'])));
    assert.match(q.text, /::jsonb/i);
    assert.match(q.text, /opened_at[\s\S]*NOW\(\)/i, 'opened_at is the database\'s clock too');
});

test('ACCEPTANCE 4: the vote UPSERT sets option_id and NOTHING else', async () => {
    const sql = recordingSql([{ match: INSERT_VOTE_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 7)] }]);
    await ballot.upsertVote(sql, BALLOT_ID, WALLET_A, 'build_speed_1', 7);
    const q = sql.matching(INSERT_VOTE_RE)[0].text;
    const conflict = q.slice(q.search(/ON\s+CONFLICT/i));
    assert.match(conflict, /ON\s+CONFLICT\s*\(\s*ballot_id\s*,\s*wallet\s*\)\s*DO\s+UPDATE\s+SET\s+option_id\s*=\s*EXCLUDED\.option_id/i);
    assert.ok(!/DO\s+UPDATE[\s\S]*\bweight\s*=/i.test(conflict),
        '⛔ re-voting must NEVER move the weight snapshot — acceptance criterion 4');
    assert.ok(!/DO\s+UPDATE[\s\S]*voted_at\s*=/i.test(conflict),
        'nor the moment it was first cast');
});

test('the weight column is cast in SQL, because neon returns NUMERIC as a string', async () => {
    const sql = recordingSql([{ match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', '0.5')] }]);
    const votes = await ballot.readVotes(sql, BALLOT_ID);
    assert.match(sql.matching(VOTES_RE)[0].text, /weight::float8\s+AS\s+weight/i);
    assert.equal(votes[0].weight, 0.5);
    assert.equal(typeof votes[0].weight, 'number', 'a string weight would make every sum a concatenation');
});

test('closing is a CONDITIONAL update, which is the whole concurrency design', async () => {
    const sql = recordingSql([{ match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC', winning_option: 'x' }] }]);
    const won = await ballot.closeBallot(sql, BALLOT_ID, 'x');
    assert.equal(won.closed, true);
    assert.match(sql.matching(CLOSE_RE)[0].text,
        /WHERE\s+id\s*=\s*\?\s*AND\s+closed_at\s+IS\s+NULL/i,
        '⛔ exactly one racing request may move the row');

    const lost = recordingSql([{ match: CLOSE_RE, rows: [] }]);
    assert.deepEqual(await ballot.closeBallot(lost, BALLOT_ID, 'x'), { closed: false });
});

test('a perk write replaces that tier\'s perk and always writes expires_at NULL', async () => {
    const sql = recordingSql([{ match: INSERT_PERK_RE, rows: [{
        clan_id: CLAN_ID, tier: 1, perk_id: 'clan_build_speed_1', activated_at: 'TA', expires_at: null }] }]);
    const perk = await ballot.writePerk(sql, CLAN_ID, 1, 'clan_build_speed_1');
    assert.equal(perk.perkId, 'clan_build_speed_1');
    assert.equal(perk.expiresAt, null);
    const q = sql.matching(INSERT_PERK_RE)[0].text;
    assert.match(q, /ON\s+CONFLICT\s*\(\s*clan_id\s*,\s*tier\s*\)\s*DO\s+UPDATE/i);
    assert.match(q, /expires_at\s*=\s*NULL/i,
        'no ruling on expiry exists, so it is always NULL and the migration says so');
});

// ═══════════════════════════════════════════════════════════════════════════
// 8. SETTLING — acceptance criteria 5, 6 and 8
// ═══════════════════════════════════════════════════════════════════════════

test('ACCEPTANCE 5+8: an expired ballot that clears turnout closes and WRITES a perk', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 0.5),
                                  voteRow(WALLET_B, 'harvest_yield_1', 1.0)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 2 }] },
        { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC', winning_option: 'harvest_yield_1' }] },
        { match: INSERT_PERK_RE, rows: [{ clan_id: CLAN_ID, tier: 1, perk_id: 'clan_harvest_yield_1',
                                          activated_at: 'TA', expires_at: null }] },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID, rowBallot({ expired: true }));

    assert.equal(s.closedNow, true);
    assert.equal(s.decision.passed, true);
    assert.equal(s.decision.winner, 'harvest_yield_1');
    assert.equal(s.perkWritten, true);
    assert.equal(s.perk.perkId, 'clan_harvest_yield_1');
    // The winning option — not the losing one, and not the first vote cast — is what the
    // close statement stored.
    assert.ok(sql.matching(CLOSE_RE)[0].values.includes('harvest_yield_1'));
    assert.ok(sql.matching(INSERT_PERK_RE)[0].values.includes('clan_harvest_yield_1'));
});

test('ACCEPTANCE 6: an expired ballot that FAILS turnout closes and writes NO perk', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 9999)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 4 }] },
        { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC', winning_option: null }] },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID, rowBallot({ expired: true }));

    assert.equal(s.closedNow, true, 'it still CLOSES — a failed ballot does not stay open');
    assert.equal(s.decision.passed, false);
    assert.equal(s.decision.reason, 'insufficient_turnout');
    assert.equal(s.perk, null);
    assert.equal(sql.matching(INSERT_PERK_RE).length, 0, '⛔ no perk row is written');
    assert.deepEqual(sql.matching(CLOSE_RE)[0].values, [null, BALLOT_ID],
        'winning_option is stored NULL, which is what makes "did not pass" recoverable later');
});

test('an expired ballot NOBODY voted on closes with reason no_votes', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 3 }] },
        { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC', winning_option: null }] },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID, rowBallot({ expired: true }));
    assert.equal(s.decision.reason, 'no_votes');
    assert.equal(s.decision.passed, false);
    assert.equal(sql.matching(INSERT_PERK_RE).length, 0);
});

test('CLOSING SPENDS NO CHAIN READ — an SKR outage can never strand a ballot', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 0.5)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 1 }] },
        { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC', winning_option: 'build_speed_1' }] },
        { match: INSERT_PERK_RE, rows: [{ clan_id: CLAN_ID, tier: 1, perk_id: 'clan_build_speed_1',
                                          activated_at: 'TA', expires_at: null }] },
    ]);
    await ballot.settleBallot(sql, CLAN_ID, rowBallot({ expired: true }));
    // The proof is structural: the ONLY statements a settle issues are these four, and the
    // roster read (the one that costs RPC) is not among them.
    assert.equal(sql.matching(ROSTER_RE).length, 0,
        '⛔ the weight snapshot in clan_ballot_votes is what buys this property');
});

test('losing the close race reports the WINNER\'S outcome and writes no second perk', async () => {
    let byIdCalls = 0;
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 5)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 1 }] },
        { match: CLOSE_RE, rows: [] },                    // somebody else closed it first
        { match: BY_ID_RE, answer: () => {
            byIdCalls++;
            return { rows: [ballotRowRaw({ closed_at: 'TC-THEIRS', winning_option: 'harvest_yield_1',
                                           expired: false })] };
        } },
        { match: PERKS_RE, rows: [{ tier: 1, perk_id: 'clan_harvest_yield_1',
                                    activated_at: 'TA', expires_at: null }] },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID, rowBallot({ expired: true }));

    assert.equal(byIdCalls, 1, 'it re-read the row rather than trusting its own verdict');
    assert.equal(s.closedNow, false);
    assert.equal(s.ballot.winningOption, 'harvest_yield_1',
        'their stored outcome is the truth, not the one we computed');
    assert.equal(s.decision.passed, true);
    assert.equal(s.perkWritten, false, '⛔ the perk they wrote is not written a second time');
});

test('a closed-and-PASSED ballot whose perk row is missing SELF-HEALS on the next read', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 5)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 1 }] },
        { match: PERKS_RE, answer: (hit) => hit === 1 ? { rows: [] } : { rows: [] } },
        { match: INSERT_PERK_RE, rows: [{ clan_id: CLAN_ID, tier: 1, perk_id: 'clan_build_speed_1',
                                          activated_at: 'TA', expires_at: null }] },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID,
        rowBallot({ closed_at: 'TC', winning_option: 'build_speed_1', expired: false }));

    assert.equal(s.closedNow, false, 'it was already closed');
    assert.equal(s.decision.passed, true);
    assert.equal(s.perkWritten, true, 'the perk the close could not write is written now');
    assert.equal(sql.matching(INSERT_PERK_RE).length, 1);
});

test('a closed-and-FAILED ballot never grows a perk on a later read', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 5)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 4 }] },
        { match: PERKS_RE, rows: [] },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID,
        rowBallot({ closed_at: 'TC', winning_option: null, expired: false }));
    assert.equal(s.decision.passed, false);
    assert.equal(s.decision.reason, 'insufficient_turnout',
        'the reason is re-derived from the votes, because the row stores only the outcome');
    assert.equal(sql.matching(INSERT_PERK_RE).length, 0);
});

test('a still-running ballot is not closed, and still reports its live tally', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 2)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 2 }] },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID, rowBallot({ expired: false }));
    assert.equal(s.closedNow, false);
    assert.equal(sql.matching(CLOSE_RE).length, 0, '⛔ an unexpired ballot is never closed early');
    assert.equal(s.decision.tally.tallies[0].weight, 2);
});

test('a perk write that throws does not lose the close, and is retried later', async () => {
    const sql = recordingSql([
        { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 5)] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: 1 }] },
        { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC', winning_option: 'build_speed_1' }] },
        { match: INSERT_PERK_RE, throws: new Error('deadlock detected') },
    ]);
    const s = await ballot.settleBallot(sql, CLAN_ID, rowBallot({ expired: true }));
    assert.equal(s.closedNow, true, 'the close already committed and must not be undone');
    assert.equal(s.decision.passed, true);
    assert.equal(s.perkWritten, false, 'reported honestly rather than claimed');
});

// Helpers used by the settle tests. rowBallot() produces the LIBRARY shape (what
// readLatestBallot returns); ballotRowRaw() produces the DATABASE row shape.
function ballotRowRaw(over = {}) { return ballotRow(over); }
function rowBallot(over = {}) {
    const raw = ballotRow(over);
    return {
        ballotId: String(raw.id), clanId: String(raw.clan_id), proposedByWallet: raw.proposed_by_wallet,
        tier: Number(raw.tier), options: raw.options.map(String), openedAt: raw.opened_at,
        closesAt: raw.closes_at, closedAt: raw.closed_at, winningOption: raw.winning_option,
        expired: raw.expired === true,
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. THE VIGIL SEAM
// ═══════════════════════════════════════════════════════════════════════════

test('readVigilFor picks the CALLER out of the clan read, and separates the two degradations',
    async () => {
        const fake = async () => ({
            members: [
                { wallet: WALLET_A, percentStaked: 0.5, tenureSeconds: 7200, vigilContribution: 3600,
                  degraded: false },
                { wallet: WALLET_B, percentStaked: 0, tenureSeconds: 0, vigilContribution: 0,
                  degraded: true },
            ],
            vigilWeight: 3600, degraded: true, memberCount: 2,
        });
        const mine = await ballot.readVigilFor(recordingSql(), CLAN_ID, WALLET_A, { readVigil: fake });
        assert.equal(mine.myContribution, 3600);
        assert.equal(mine.degraded, true, 'the CLAN read was degraded (B was unreadable)');
        assert.equal(mine.memberDegraded, false,
            '⛔ but MY OWN position read cleanly, so my vote is not blocked by B\'s outage');

        const theirs = await ballot.readVigilFor(recordingSql(), CLAN_ID, WALLET_B, { readVigil: fake });
        assert.equal(theirs.memberDegraded, true);
        assert.equal(theirs.myContribution, 0);
    });

// ═══════════════════════════════════════════════════════════════════════════
// 10. THE WIRE
// ═══════════════════════════════════════════════════════════════════════════

test('the response carries per-option TALLIES and my own vote — never who voted for what',
    async () => {
        const votes = [voteRow(WALLET_A, 'build_speed_1', 0.5), voteRow(WALLET_B, 'harvest_yield_1', 1)];
        const body = ballot.toWire({
            ballot: rowBallot({ closed_at: 'TC', winning_option: 'harvest_yield_1' }),
            decision: ballot.decideBallot(
                votes.map(v => ({ wallet: v.wallet, optionId: v.option_id, weight: v.weight })), 2, 1.5),
            epoch: { epochIndex: 130, startedAt: 'S', endsAt: 'E' },
            vigilWeight: 1.5, vigilDegraded: false, myVote: 'build_speed_1',
            perk: { tier: 1, perkId: 'clan_harvest_yield_1' },
            perks: [{ tier: 1, perkId: 'clan_harvest_yield_1', activatedAt: 'TA', expiresAt: null }],
        });

        const json = JSON.stringify(body);
        assert.ok(!json.includes(WALLET_A), 'no wallet is rendered on a ballot at any depth');
        assert.ok(!json.includes(WALLET_B));
        assert.equal(body.ballot.my_vote, 'build_speed_1');
        const opt = body.ballot.options.find(o => o.option_id === 'harvest_yield_1');
        assert.equal(opt.weight, 1);
        assert.equal(opt.voters, 1);
        // The work order's literal result shape.
        assert.deepEqual(body.result, {
            closed: true, passed: true, reason: 'passed',
            winning_option: 'harvest_yield_1', perk_id: 'clan_harvest_yield_1',
        });
    });

test('a failed ballot answers the work order\'s literal { closed, passed:false, reason } shape', () => {
    const body = ballot.toWire({
        ballot: rowBallot({ closed_at: 'TC', winning_option: null }),
        decision: ballot.decideBallot([{ wallet: 'a', optionId: 'build_speed_1', weight: 9 }], 4, 9),
        epoch: null, vigilWeight: 9, myVote: null, perks: [],
    });
    assert.equal(body.result.closed, true);
    assert.equal(body.result.passed, false);
    assert.equal(body.result.reason, 'insufficient_turnout');
    assert.equal(body.result.winning_option, null);
    assert.equal(body.result.perk_id, null);
    assert.equal(body.ballot.turnout.required_voters, 2);
    assert.equal(body.ballot.turnout.voters, 1);
    assert.equal(body.ballot.turnout.clears, false);
});

test('an open ballot carries no result at all, and the tier ladder reports what is unlocked', () => {
    const body = ballot.toWire({
        ballot: rowBallot({}), decision: ballot.decideBallot([], 2, 0),
        epoch: null, vigilWeight: ballot.TIER_THRESHOLDS[2], myVote: null, perks: [],
    });
    assert.equal(body.result, null, 'a running ballot has no outcome to state');
    assert.equal(body.ballot.closed, false);
    const unlocked = body.tiers.filter(t => t.unlocked).map(t => t.tier);
    assert.deepEqual(unlocked, [1, 2], 'exactly the tiers this weight has earned');
    assert.equal(body.tiers[4].options.length, 1, 'tier 5 offers its one option');
});

test('with no ballot at all the response is still a complete answer', () => {
    const body = ballot.toWire({ ballot: null, decision: null, epoch: {
        epochIndex: 130, startedAt: 'S', endsAt: 'E' }, vigilWeight: 0, perks: [] });
    assert.equal(body.ok, true);
    assert.equal(body.ballot, null);
    assert.equal(body.result, null);
    assert.equal(body.epoch.index, 130);
    assert.equal(body.epoch.anchor, ballot.EPOCH_ANCHOR_ISO);
    assert.equal(body.headline, ballot.BALLOT_HEADLINE);
    assert.deepEqual(body.tiers.filter(t => t.unlocked), [], 'a clan with no Vigil has earned nothing');
});

// ═══════════════════════════════════════════════════════════════════════════
// 11. THE ENDPOINTS
// ═══════════════════════════════════════════════════════════════════════════

const DRIVER_ID = require.resolve('@neondatabase/serverless');
const FRESH = [CLAN_HTTP, CLAN_LIB, CLAN_VIGIL, CLAN_BALLOT, ...Object.values(ROUTES)];

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

function loadRoute(name, sqlFn) {
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = { neon() { return sqlFn; } };
    for (const p of FRESH) delete require.cache[require.resolve(p)];
    const handler = require(ROUTES[name]);
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    for (const p of FRESH) delete require.cache[require.resolve(p)];
    return handler;
}

/**
 * ⛔ THE VIGIL IS SWAPPED AT skr-staking's EXPORT, NOT MOCKED WITH A FAKE RPC SERVER.
 * clan-vigil.js resolves `skr.getStakedPercentage` at CALL time on the module exports
 * object, so replacing that one function is enough — and skr-staking is deliberately NOT
 * in FRESH, so the patch survives the route's re-require. The real RPC path is proven in
 * test/clan-vigil.test.js against a real local HTTP server; re-proving it here would make
 * every ballot test depend on a layer that is not its subject.
 */
function withStake(percentByWallet, fn) {
    const skr = require(SKR_PATH);
    const real = skr.getStakedPercentage;
    skr.getStakedPercentage = async (wallet) => {
        const spec = percentByWallet[wallet];
        if (spec === 'degraded') return { degraded: true, percent: 0, totalRaw: null };
        return { degraded: false, percent: typeof spec === 'number' ? spec : 0, verificationStatus: 'VERIFIED' };
    };
    return (async () => {
        try { return await fn(); } finally { skr.getStakedPercentage = real; }
    })();
}

async function callRoute(name, opts = {}) {
    const sqlFn = opts.sql || recordingSql();
    const headers = Object.assign({ 'x-session': SESSION }, opts.headers || {});
    const method = opts.method || (name === 'current' ? 'GET' : 'POST');
    const req = { method: method, headers: headers, query: opts.query || {} };
    if (method === 'POST' && opts.body !== undefined) {
        req.body = typeof opts.body === 'string' ? opts.body : JSON.stringify(opts.body);
        req.readableEnded = true;
    }
    const prevUrl = process.env.DATABASE_URL;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const handler = loadRoute(name, sqlFn);
    const res = fakeRes();
    try {
        await handler(req, res);
    } finally {
        if (prevUrl === undefined) delete process.env.DATABASE_URL;
        else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, sql: sqlFn };
}

/** Authenticates the WALLET rail through the session path, then answers the clan reads. */
function authedSql(extra = [], memberCount = 2) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET_A, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i,
          rows: [{ first_seen_at: 'T0', last_seen_at: 'T0', first_seen_staked_at: 'T0' }] },
        { match: MEMBER_COUNT_RE, rows: [{ member_count: memberCount }] },
        { match: EPOCH_RE, rows: [{ epoch_index: 130,
            started_at: '2026-09-18T00:00:00.000Z', ends_at: '2026-09-20T00:00:00.000Z',
            now_at: '2026-09-18T03:22:04.220Z' }] },
        ...extra,
        { match: MEMBERSHIP_RE, rows: [{
            clan_id: CLAN_ID, code: 'ABCD12', name: 'Ember Wardens', tag: 'EMBR',
            join_policy: 'invite', created_at: 'T0', role: 'leader', joined_at: 'T0',
            member_count: memberCount,
        }] },
    ]);
}

/** A two-member roster whose tenures make the arithmetic legible. */
function rosterRows(rows) {
    return rows.map(r => ({
        wallet: r.wallet, role: r.role || 'member',
        first_seen_staked_at: r.stakedAt === undefined ? 'T0' : r.stakedAt,
        tenure_seconds: r.tenure || 0,
    }));
}

const TWO_MEMBER_ROSTER = { match: ROSTER_RE, rows: rosterRows([
    { wallet: WALLET_A, tenure: 7200 },     // x 0.5 staked => 3600
    { wallet: WALLET_B, tenure: 7200 },     // x 1.0 staked => 7200
]) };

// ── propose ──────────────────────────────────────────────────────────────────

test('POST /propose opens a Tier 1 ballot for a clan with Vigil, and answers the full body',
    async () => {
        await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
            const { out, sql } = await callRoute('propose', {
                body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1', 'harvest_yield_1'] },
                sql: authedSql([
                    TWO_MEMBER_ROSTER,
                    { match: LATEST_RE, rows: [] },
                    { match: INSERT_BALLOT_RE, rows: [ballotRow()] },
                    { match: PERKS_RE, rows: [] },
                ]),
            });
            assert.equal(out.statusCode, 200, JSON.stringify(out.body));
            assert.equal(out.body.ok, true);
            assert.equal(out.body.ballot.ballot_id, BALLOT_ID);
            assert.equal(out.body.ballot.tier, 1);
            assert.equal(out.body.ballot.closed, false);
            assert.equal(out.body.ballot.my_vote, null);
            assert.equal(out.body.vigil_weight, 0.5 * 7200 + 1 * 7200);
            assert.equal(out.body.ballot.turnout.required_voters, 1, 'a two-member clan');
            // closes_at came from the epoch read, not from Node.
            assert.ok(sql.matching(EPOCH_RE).length >= 1);
            assert.ok(sql.matching(INSERT_BALLOT_RE)[0].values.includes('2026-09-20T00:00:00.000Z'));
        });
    });

test('ACCEPTANCE 1 at the endpoint: a clan with NO Vigil is refused Tier 1 with 403', async () => {
    await withStake({ [WALLET_A]: 0, [WALLET_B]: 0 }, async () => {
        const { out } = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] },
            sql: authedSql([TWO_MEMBER_ROSTER, { match: LATEST_RE, rows: [] }]),
        });
        assert.equal(out.statusCode, 403);
        assert.equal(out.body.error, 'CLAN_BALLOT_TIER_LOCKED');
    });
});

test('TEST PLAN 3: a Tier 3 propose with a weight of 0.5 is a 403', async () => {
    await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 0 }, async () => {
        const { out, sql } = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 3, optionIds: ['build_speed_3'] },
            sql: authedSql([
                { match: ROSTER_RE, rows: rosterRows([
                    { wallet: WALLET_A, tenure: 1 },      // 0.5 x 1s = 0.5
                    { wallet: WALLET_B, tenure: 0 },
                ]) },
                { match: LATEST_RE, rows: [] },
            ]),
        });
        assert.equal(out.statusCode, 403);
        assert.equal(out.body.error, 'CLAN_BALLOT_TIER_LOCKED');
        assert.equal(sql.matching(INSERT_BALLOT_RE).length, 0, 'nothing was opened');
    });
});

test('a DEGRADED read below the bar is a 503, not a 403 — the answer is not knowable', async () => {
    await withStake({ [WALLET_A]: 'degraded', [WALLET_B]: 'degraded' }, async () => {
        const { out } = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 2, optionIds: ['wall_hp_1'] },
            sql: authedSql([TWO_MEMBER_ROSTER, { match: LATEST_RE, rows: [] }]),
        });
        assert.equal(out.statusCode, 503);
        assert.equal(out.body.error, 'CLAN_BALLOT_WEIGHT_UNAVAILABLE');
    });
});

test('a degraded read that STILL clears the bar proceeds — the true weight is at least that',
    async () => {
        await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 'degraded' }, async () => {
            const { out } = await callRoute('propose', {
                body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] },
                sql: authedSql([
                    TWO_MEMBER_ROSTER,
                    { match: LATEST_RE, rows: [] },
                    { match: INSERT_BALLOT_RE, rows: [ballotRow()] },
                    { match: PERKS_RE, rows: [] },
                ]),
            });
            assert.equal(out.statusCode, 200);
            assert.equal(out.body.vigil_degraded, true, 'and it says the read was degraded');
        });
    });

test('ACCEPTANCE 7: a second propose while a ballot is open is a 409', async () => {
    await withStake({ [WALLET_A]: 0.5 }, async () => {
        const { out, sql } = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] },
            sql: authedSql([
                TWO_MEMBER_ROSTER,
                { match: LATEST_RE, rows: [ballotRow({ expired: false })] },
                { match: VOTES_RE, rows: [] },
            ]),
        });
        assert.equal(out.statusCode, 409);
        assert.equal(out.body.error, 'CLAN_BALLOT_ALREADY_OPEN');
        assert.equal(sql.matching(INSERT_BALLOT_RE).length, 0);
        assert.equal(sql.matching(ROSTER_RE).length, 0,
            'the refusal is decided before the chain read, so a spammed propose costs no RPC');
    });
});

test('ACCEPTANCE 7 under a RACE: a 23505 from the partial unique index is the same 409', async () => {
    const dup = Object.assign(new Error('duplicate key value violates unique constraint ' +
        '"clan_ballots_one_open_per_clan"'), { code: '23505' });
    await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
        const { out } = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] },
            sql: authedSql([
                TWO_MEMBER_ROSTER,
                { match: LATEST_RE, rows: [] },
                { match: INSERT_BALLOT_RE, throws: dup },
            ]),
        });
        assert.equal(out.statusCode, 409);
        assert.equal(out.body.error, 'CLAN_BALLOT_ALREADY_OPEN');
    });
});

test('an EXPIRED open ballot is closed by the propose, which then succeeds', async () => {
    await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
        const { out, sql } = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] },
            sql: authedSql([
                TWO_MEMBER_ROSTER,
                { match: LATEST_RE, rows: [ballotRow({ id: OTHER_BALLOT_ID, expired: true })] },
                { match: VOTES_RE, rows: [] },
                { match: CLOSE_RE, rows: [{ id: OTHER_BALLOT_ID, closed_at: 'TC', winning_option: null }] },
                { match: INSERT_BALLOT_RE, rows: [ballotRow()] },
                { match: PERKS_RE, rows: [] },
            ]),
        });
        assert.equal(out.statusCode, 200, JSON.stringify(out.body));
        assert.equal(sql.matching(CLOSE_RE).length, 1, 'the stale ballot was closed first');
        assert.equal(out.body.ballot.ballot_id, BALLOT_ID, 'and the new one is what came back');
    });
});

test('a bad tier or a foreign option id is a 400 that costs no chain read and no ballot read',
    async () => {
        for (const body of [
            { playerId: WALLET_A, tier: 9, optionIds: ['build_speed_1'] },
            { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_3'] },
            { playerId: WALLET_A, tier: 1, optionIds: [] },
            { playerId: WALLET_A },
        ]) {
            const { out, sql } = await callRoute('propose', {
                body: body, sql: authedSql([TWO_MEMBER_ROSTER]),
            });
            assert.equal(out.statusCode, 400, JSON.stringify(body) + ' -> ' + JSON.stringify(out.body));
            assert.match(out.body.error, /^CLAN_BALLOT_BAD_(TIER|OPTIONS)$/);
            assert.equal(sql.matching(ROSTER_RE).length, 0);
            assert.equal(sql.matching(LATEST_RE).length, 0);
        }
    });

test('a caller in NO clan cannot propose, and spends no chain read', async () => {
    const sql = recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET_A, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i, rows: [{ first_seen_at: 'T0', last_seen_at: 'T0' }] },
        { match: MEMBERSHIP_RE, rows: [] },
    ]);
    const { out } = await callRoute('propose', {
        body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] }, sql,
    });
    assert.equal(out.statusCode, 404);
    assert.equal(out.body.error, ClanCode.NOT_IN_CLAN);
    assert.equal(sql.matching(ROSTER_RE).length, 0);
});

// ── vote ─────────────────────────────────────────────────────────────────────

test('ACCEPTANCE 3 at the endpoint: the weight WRITTEN is this member\'s own contribution',
    async () => {
        await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
            const { out, sql } = await callRoute('vote', {
                body: { playerId: WALLET_A, ballotId: BALLOT_ID, optionId: 'build_speed_1' },
                sql: authedSql([
                    TWO_MEMBER_ROSTER,
                    { match: BY_ID_RE, rows: [ballotRow()] },
                    { match: INSERT_VOTE_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 3600)] },
                    { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 3600)] },
                    { match: PERKS_RE, rows: [] },
                ]),
            });
            assert.equal(out.statusCode, 200, JSON.stringify(out.body));
            assert.equal(out.body.ballot.my_vote, 'build_speed_1');
            // 0.5 staked x 7200s tenure. B's would be 1.0 x 7200 = 7200: different members,
            // different weights, which is the criterion.
            const bound = sql.matching(INSERT_VOTE_RE)[0].values;
            assert.ok(bound.includes(3600), 'bound weight was ' + JSON.stringify(bound));
            assert.ok(!bound.includes(7200), 'it is MY contribution, not the clan\'s weight');
        });
    });

test('a member whose OWN chain read degraded gets a 503, never a zero-weight vote', async () => {
    await withStake({ [WALLET_A]: 'degraded', [WALLET_B]: 1 }, async () => {
        const { out, sql } = await callRoute('vote', {
            body: { playerId: WALLET_A, ballotId: BALLOT_ID, optionId: 'build_speed_1' },
            sql: authedSql([TWO_MEMBER_ROSTER, { match: BY_ID_RE, rows: [ballotRow()] }]),
        });
        assert.equal(out.statusCode, 503);
        assert.equal(out.body.error, 'CLAN_BALLOT_WEIGHT_UNAVAILABLE');
        assert.equal(sql.matching(INSERT_VOTE_RE).length, 0,
            '⛔ the snapshot rule means a 0 written now could never be corrected');
    });
});

test('a member who has genuinely staked NOTHING still votes, at weight 0', async () => {
    // A real zero is not a degradation. Their vote counts toward the head-count threshold
    // and adds nothing to the plurality — which is exactly what weighted voting means.
    await withStake({ [WALLET_A]: 0, [WALLET_B]: 1 }, async () => {
        const { out, sql } = await callRoute('vote', {
            body: { playerId: WALLET_A, ballotId: BALLOT_ID, optionId: 'build_speed_1' },
            sql: authedSql([
                TWO_MEMBER_ROSTER,
                { match: BY_ID_RE, rows: [ballotRow()] },
                { match: INSERT_VOTE_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 0)] },
                { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 0)] },
                { match: PERKS_RE, rows: [] },
            ]),
        });
        assert.equal(out.statusCode, 200, JSON.stringify(out.body));
        assert.ok(sql.matching(INSERT_VOTE_RE)[0].values.includes(0));
        assert.equal(out.body.ballot.turnout.voters, 1, 'and it DOES count as turnout');
    });
});

test('voting on an EXPIRED ballot closes it and refuses the vote with 409', async () => {
    await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
        const { out, sql } = await callRoute('vote', {
            body: { playerId: WALLET_A, ballotId: BALLOT_ID, optionId: 'build_speed_1' },
            sql: authedSql([
                { match: BY_ID_RE, rows: [ballotRow({ expired: true })] },
                { match: VOTES_RE, rows: [voteRow(WALLET_B, 'harvest_yield_1', 7200)] },
                { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC',
                                            winning_option: 'harvest_yield_1' }] },
                { match: INSERT_PERK_RE, rows: [{ clan_id: CLAN_ID, tier: 1,
                    perk_id: 'clan_harvest_yield_1', activated_at: 'TA', expires_at: null }] },
            ]),
        });
        assert.equal(out.statusCode, 409);
        assert.equal(out.body.error, 'CLAN_BALLOT_CLOSED');
        assert.equal(sql.matching(CLOSE_RE).length, 1,
            '"the next request to any ballot endpoint closes it" holds on this endpoint too');
        assert.equal(sql.matching(INSERT_VOTE_RE).length, 0, 'the late vote is not recorded');
    });
});

test('another clan\'s ballot id, and a malformed one, are different refusals', async () => {
    await withStake({ [WALLET_A]: 0.5 }, async () => {
        const missing = await callRoute('vote', {
            body: { playerId: WALLET_A, ballotId: OTHER_BALLOT_ID, optionId: 'build_speed_1' },
            sql: authedSql([{ match: BY_ID_RE, rows: [] }]),
        });
        assert.equal(missing.out.statusCode, 404);
        assert.equal(missing.out.body.error, 'CLAN_BALLOT_NOT_FOUND');

        for (const bad of [undefined, '', 'not-a-uuid', '12345']) {
            const r = await callRoute('vote', {
                body: { playerId: WALLET_A, ballotId: bad, optionId: 'build_speed_1' },
                sql: authedSql([]),
            });
            assert.equal(r.out.statusCode, 400, JSON.stringify(bad));
            assert.equal(r.out.body.error, 'CLAN_BALLOT_BAD_BALLOT_ID');
            assert.equal(r.sql.matching(BY_ID_RE).length, 0, 'a malformed id costs no read');
        }
    });
});

test('an option that is not ON this ballot is a 400, even when the catalogue knows it', async () => {
    await withStake({ [WALLET_A]: 0.5 }, async () => {
        const { out, sql } = await callRoute('vote', {
            body: { playerId: WALLET_A, ballotId: BALLOT_ID, optionId: 'wall_hp_1' },
            sql: authedSql([{ match: BY_ID_RE,
                rows: [ballotRow({ options: ['build_speed_1', 'harvest_yield_1'] })] }]),
        });
        assert.equal(out.statusCode, 400);
        assert.equal(out.body.error, 'CLAN_BALLOT_BAD_OPTIONS');
        assert.equal(sql.matching(ROSTER_RE).length, 0, 'refused before the chain read');
    });
});

// ── current ──────────────────────────────────────────────────────────────────

test('TEST PLAN 4: GET /current on an expired ballot closes it and returns the result', async () => {
    await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
        const { out, sql } = await callRoute('current', {
            query: { playerId: WALLET_A },
            sql: authedSql([
                TWO_MEMBER_ROSTER,
                { match: LATEST_RE, rows: [ballotRow({ expired: true })] },
                { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 3600),
                                          voteRow(WALLET_B, 'harvest_yield_1', 7200)] },
                { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC',
                                            winning_option: 'harvest_yield_1' }] },
                { match: INSERT_PERK_RE, rows: [{ clan_id: CLAN_ID, tier: 1,
                    perk_id: 'clan_harvest_yield_1', activated_at: 'TA', expires_at: null }] },
                { match: PERKS_RE, rows: [{ tier: 1, perk_id: 'clan_harvest_yield_1',
                    activated_at: 'TA', expires_at: null }] },
            ]),
        });
        assert.equal(out.statusCode, 200, JSON.stringify(out.body));
        assert.deepEqual(out.body.result, {
            closed: true, passed: true, reason: 'passed',
            winning_option: 'harvest_yield_1', perk_id: 'clan_harvest_yield_1',
        });
        assert.equal(out.body.ballot.closed, true);
        assert.equal(out.body.ballot.my_vote, 'build_speed_1', 'my losing vote is still mine');
        assert.deepEqual(out.body.perks, [{ tier: 1, perk_id: 'clan_harvest_yield_1',
            activated_at: 'TA', expires_at: null }]);
        assert.equal(sql.matching(INSERT_PERK_RE).length, 1, 'ACCEPTANCE 5: the perk row is written');
    });
});

test('ACCEPTANCE 6 at the endpoint: an expired ballot below the bar closes with no perk', async () => {
    await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
        const { out, sql } = await callRoute('current', {
            query: { playerId: WALLET_A },
            // FOUR members, ONE voter: required 2, so a clear weighted winner still fails.
            sql: authedSql([
                { match: ROSTER_RE, rows: rosterRows([
                    { wallet: WALLET_A, tenure: 7200 }, { wallet: WALLET_B, tenure: 7200 }]) },
                { match: LATEST_RE, rows: [ballotRow({ expired: true })] },
                { match: VOTES_RE, rows: [voteRow(WALLET_A, 'build_speed_1', 3600)] },
                { match: CLOSE_RE, rows: [{ id: BALLOT_ID, closed_at: 'TC', winning_option: null }] },
                { match: PERKS_RE, rows: [] },
            ], 4),
        });
        assert.equal(out.statusCode, 200);
        assert.equal(out.body.result.closed, true);
        assert.equal(out.body.result.passed, false);
        assert.equal(out.body.result.reason, 'insufficient_turnout');
        assert.equal(out.body.result.perk_id, null);
        assert.equal(out.body.ballot.turnout.required_voters, 2);
        assert.equal(sql.matching(INSERT_PERK_RE).length, 0, '⛔ no perk is written');
        assert.deepEqual(out.body.perks, []);
    });
});

test('GET /current with no ballot at all still answers the epoch, the tiers and the perks',
    async () => {
        await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
            const { out } = await callRoute('current', {
                query: { playerId: WALLET_A },
                sql: authedSql([
                    TWO_MEMBER_ROSTER,
                    { match: LATEST_RE, rows: [] },
                    { match: PERKS_RE, rows: [] },
                ]),
            });
            assert.equal(out.statusCode, 200);
            assert.equal(out.body.ballot, null);
            assert.equal(out.body.result, null);
            assert.equal(out.body.epoch.index, 130);
            assert.equal(out.body.epoch.seconds, 172800);
            assert.equal(out.body.tiers.length, 5);
            assert.equal(out.body.tiers[0].unlocked, true, 'this clan has Vigil, so Tier 1 is open');
        });
    });

test('an RPC failure is a 200 with vigil_degraded on a READ, never a 500', async () => {
    await withStake({ [WALLET_A]: 'degraded', [WALLET_B]: 'degraded' }, async () => {
        const { out } = await callRoute('current', {
            query: { playerId: WALLET_A },
            sql: authedSql([
                TWO_MEMBER_ROSTER,
                { match: LATEST_RE, rows: [] },
                { match: PERKS_RE, rows: [] },
            ]),
        });
        assert.equal(out.statusCode, 200, '⛔ the chain is a dependency we report ON, never fail WITH');
        assert.equal(out.body.vigil_degraded, true);
        assert.equal(out.body.vigil_weight, 0, 'under-stated, and flagged as such');
        assert.equal(out.body.tiers[0].unlocked, false);
    });
});

test('a DATABASE failure is the one 500, on every ballot endpoint', async () => {
    const boom = new Error('relation "clan_ballots" does not exist');
    await withStake({ [WALLET_A]: 0.5 }, async () => {
        const current = await callRoute('current', {
            query: { playerId: WALLET_A },
            sql: authedSql([TWO_MEMBER_ROSTER, { match: LATEST_RE, throws: boom }]),
        });
        assert.equal(current.out.statusCode, 500);
        assert.equal(current.out.body.code, AuthCode.SERVER_ERROR);

        const propose = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] },
            sql: authedSql([TWO_MEMBER_ROSTER, { match: LATEST_RE, throws: boom }]),
        });
        assert.equal(propose.out.statusCode, 500);
    });
});

// ── the shared preamble's properties, on these three routes ──────────────────

test('an unauthenticated request is refused in the SAME shape as every other clan route',
    async () => {
        for (const name of ['propose', 'vote', 'current']) {
            const r = await callRoute(name, {
                headers: { 'x-session': '' }, query: { playerId: WALLET_A },
                body: { playerId: WALLET_A, tier: 1, optionIds: ['build_speed_1'] },
            });
            assert.equal(r.out.statusCode, 401, name);
            assert.equal(r.out.body.ok, false);
            assert.ok(r.out.body.code, 'carries a stable machine code');
            assert.ok(r.out.body.ref, 'and a ref, exactly like quietFail everywhere else');
        }
    });

test('the wrong METHOD on each route is a 400, never a silent fallthrough', async () => {
    const wrong = { propose: 'GET', vote: 'GET', current: 'POST' };
    for (const [name, method] of Object.entries(wrong)) {
        const r = await callRoute(name, { method: method, query: { playerId: WALLET_A } });
        assert.equal(r.out.statusCode, 400, name);
        assert.equal(r.out.body.code, AuthCode.METHOD_NOT_ALLOWED);
    }
});

test('no wallet address appears in ANY refusal body, at any depth', async () => {
    const cases = [
        await callRoute('current', { headers: { 'x-session': '' }, query: { playerId: WALLET_A } }),
        await callRoute('propose', { method: 'GET', query: { playerId: WALLET_A } }),
        await callRoute('vote', { body: { playerId: WALLET_A, ballotId: 'nope' },
                                  sql: authedSql([]) }),
        await callRoute('current', { query: {}, headers: { 'x-session': SESSION } }),
    ];
    for (const c of cases) {
        assert.ok(c.out.statusCode >= 400, 'fixture assumption: these are all refusals');
        const json = JSON.stringify(c.out.body || {});
        assert.ok(!json.includes(WALLET_A), 'a wallet reached a refusal body: ' + json);
    }
});

test('the two WRITE routes spend a rate budget and the READ route spends none', async () => {
    await withStake({ [WALLET_A]: 0.5, [WALLET_B]: 1 }, async () => {
        const read = await callRoute('current', {
            query: { playerId: WALLET_A },
            sql: authedSql([TWO_MEMBER_ROSTER, { match: LATEST_RE, rows: [] }, { match: PERKS_RE, rows: [] }]),
        });
        assert.equal(read.out.statusCode, 200);
        assert.equal(read.sql.matching(/clan_rate_limit/i).length, 0,
            'a read must never cost a player the ability to leave their clan');

        const write = await callRoute('propose', {
            body: { playerId: WALLET_A, tier: 9, optionIds: [] }, sql: authedSql([]),
        });
        const spend = write.sql.matching(/clan_rate_limit/i);
        assert.equal(spend.length, 1, 'propose spends budget, and spends it on a REFUSED attempt too');
        assert.ok(spend[0].values.includes('ballot_propose'),
            '⚠ not a declared key of CLAN_RATE_LIMITS, so the STRICTEST budget applies — the ' +
            'documented undeclared-action fallback, flagged for the lead as a two-line follow-up');

        const vote = await callRoute('vote', {
            body: { playerId: WALLET_A, ballotId: 'nope' }, sql: authedSql([]),
        });
        assert.ok(vote.sql.matching(/clan_rate_limit/i)[0].values.includes('ballot_vote'));
    });
});

// ═══════════════════════════════════════════════════════════════════════════
// 12. THE STANDING INVARIANTS THIS TICKET MUST NOT HAVE BROKEN
// ═══════════════════════════════════════════════════════════════════════════

test('clan.js and clan-vigil.js are NOT touched by this lane', () => {
    // The ballot lives in its own module for the reason clan-vigil.js:11-17 records.
    const clan = require(CLAN_LIB);
    const vigil = require(CLAN_VIGIL);
    for (const key of ['settleBallot', 'decideBallot', 'BallotCode', 'readEpoch']) {
        assert.equal(clan[key], undefined, 'clan.js must export no ballot symbol: ' + key);
        assert.equal(vigil[key], undefined, 'clan-vigil.js must export no ballot symbol: ' + key);
    }
    assert.equal(typeof ballot.settleBallot, 'function', 'it lives in clan-ballot.js instead');
    // And it REUSES the Vigil rather than re-implementing it.
    const src = fs.readFileSync(CLAN_BALLOT, 'utf8');
    assert.match(src, /require\('\.\/clan-vigil'\)/,
        'the weight comes from WO-1852\'s read, never from a second tenure query');
    assert.ok(!/first_seen_staked_at/.test(src),
        '⛔ a second spelling of "how long has this wallet been staked" would be duplicated state');
});

test('the migration is additive, re-runnable, and declares the one-open-ballot index', async () => {
    const sql = fs.readFileSync(MIGRATION, 'utf8');
    for (const table of ['clan_ballots', 'clan_ballot_votes', 'clan_perks']) {
        assert.match(sql, new RegExp('CREATE TABLE IF NOT EXISTS ' + table, 'i'),
            table + ' must be created idempotently');
    }
    assert.match(sql,
        /CREATE\s+UNIQUE\s+INDEX\s+IF\s+NOT\s+EXISTS\s+clan_ballots_one_open_per_clan[\s\S]*WHERE\s+closed_at\s+IS\s+NULL/i,
        'the partial index is what holds acceptance criterion 7 under a race');
    assert.match(sql, /PRIMARY KEY \(ballot_id, wallet\)/i, 'one vote per wallet per ballot');
    assert.match(sql, /PRIMARY KEY \(clan_id, tier\)/i, 'one perk per tier per clan');

    // The runner's own additive audit, imported rather than re-spelled.
    const runner = await import('file://' + path.join(REPO, 'tools', 'run-migrations.mjs')
        .replace(/\\/g, '/'));
    assert.deepEqual(runner.auditAdditive(sql), [],
        'nothing here drops, deletes or truncates, so the runner needs no exemption for it');
    assert.ok(!/\bINSERT\s+INTO\b/i.test(sql.replace(/--.*$/gm, '')),
        'it seeds no rows, so a ledger re-apply is a no-op');
});

test('api/schema.sql carries the DESCRIPTIVE copy, and says the migration is the applyable one', () => {
    const schema = fs.readFileSync(SCHEMA_SQL, 'utf8');
    assert.match(schema, /clan_ballots/, 'the drift oracle reads schema.sql CREATE bodies');
    assert.match(schema, /THE APPLYABLE COPY IS api\/migrations\/20260917_0035_clan_ballots\.sql/);
    for (const table of ['clan_ballots', 'clan_ballot_votes', 'clan_perks']) {
        assert.match(schema, new RegExp('CREATE TABLE IF NOT EXISTS ' + table, 'i'));
    }
});

test('the three endpoints exist at the paths the work order names', () => {
    for (const [name, p] of Object.entries(ROUTES)) {
        assert.ok(fs.existsSync(p), name + ' is missing at ' + p);
        const src = fs.readFileSync(p, 'utf8');
        assert.match(src, /module\.exports\s*=\s*handler/, name + ' must export the handler');
        assert.match(src, /bodyParser:\s*false/, name + ' must keep the raw body for signature auth');
        assert.match(src, /beginClanRequest/, name + ' must use the ONE shared clan preamble');
    }
});
