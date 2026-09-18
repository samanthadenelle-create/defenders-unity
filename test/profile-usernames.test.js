// =============================================================================
// test/profile-usernames.test.js — WO-1870 Lane A (the Circle screen's name rail).
// -----------------------------------------------------------------------------
// The SOURCE-CHECKABLE half of the lead's 2026-09-18 ruling: the Circle screen's
// "Remnant name" is the EXISTING player_profiles.username, written by the EXISTING
// POST /api/profile/username. This lane therefore adds exactly ONE server file — the
// batch READ the roster needs — and nothing else.
//
// WHAT THIS FILE GUARDS:
//   1. THE REUSE HELD. No display_name column, no migration 0038, no
//      /api/profile/display-name route anywhere in the tree. "We reused it" is only
//      durable if something fails when someone quietly adds the second field.
//   2. THE LIVE GATES SURVIVED. api/profile/username.js still validates through
//      username-policy (profanity) and still surfaces the DB's case-insensitive
//      uniqueness as USERNAME_TAKEN. A demo does not remove a live safety gate.
//   3. THE NEW READ IS AUTHED, CAPPED AND ARRAY-SHAPED. The array is load-bearing:
//      UnityEngine.JsonUtility cannot deserialise a dictionary, so a map body would
//      parse to nothing silently and every member row would fall back to a short id.
//   4. THE CLIENT AND THE SERVER AGREE ON THE RULE. The VM's own length constants are
//      the policy file's, not the work order's retired "1..24".
//
//     node --test test/profile-usernames.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const REPO = path.resolve(__dirname, '..');
const read = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');

const ROUTE = 'api/profile/usernames.js';
const WRITE_ROUTE = 'api/profile/username.js';
const POLICY = 'api/_lib/username-policy.js';
const VM = 'Assets/_Modules/HUD/Circle/CircleScreenVM.cs';
const SOURCE = 'Assets/_Modules/HUD/Circle/CircleSource.cs';
const WIRE = 'Assets/_Modules/HUD/Circle/CircleWire.cs';

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE REUSE HELD — proven by absence
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ no display_name column, route or migration was added — the username is the name', () => {
    // The work order's own plan proposed migration 0038 + a display_name column + a
    // POST /api/profile/display-name. The lead ruled REUSE instead. A second name field
    // would mean two identity strings with two rules, which is the duplicated state this
    // repo keeps paying for — so its absence is asserted, not assumed.
    const migrations = fs.readdirSync(path.join(REPO, 'api/migrations'));
    for (const file of migrations) {
        assert.ok(!/display_name/i.test(file),
            'a display_name migration exists (' + file + '); the lead ruling is to reuse username');
    }
    const profileDir = fs.readdirSync(path.join(REPO, 'api/profile'));
    for (const file of profileDir) {
        assert.ok(!/display-name/i.test(file),
            'api/profile/' + file + ' exists; row 15 of the plan was REPLACED by the existing username route');
    }
    // Comments are stripped first: these files EXPLAIN the ruling in prose, and the
    // explanation must not read as the violation it exists to prevent.
    const bare = (rel) => read(rel).split('\n')
        .filter((l) => !/^\s*(\/\/|\*|\/\*)/.test(l) && !/^\s*--/.test(l)).join('\n');
    for (const rel of [ROUTE, WRITE_ROUTE, VM, SOURCE, WIRE]) {
        assert.ok(!bare(rel).includes('display_name'),
            rel + ' names a display_name column that this ticket deliberately did not create');
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE LIVE GATES SURVIVED
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the username write still runs the profanity gate and still surfaces uniqueness', () => {
    const src = read(WRITE_ROUTE);
    assert.match(src, /validateUsername/,
        'the profanity + format gate must still run — the demo does not remove a live gate');
    assert.match(src, /USERNAME_TAKEN/,
        'the case-insensitive unique index must still surface as a player-facing refusal');
    assert.match(src, /verifyAndConsume/, 'and the write is still auth-gated');
});

test('the policy file is still the ONE source of the name rule', () => {
    const policy = read(POLICY);
    assert.match(policy, /const MIN_LEN = 3;/, 'read at source, not copied from a work order');
    assert.match(policy, /const MAX_LEN = 16;/);
    assert.match(policy, /\/\^\[A-Za-z0-9_\]\+\$\//, 'letters, digits, underscore');
});

test('the VM mirrors the SERVER rule, not the work order plan that was overridden', () => {
    const vm = read(VM);
    const policy = read(POLICY);
    const min = policy.match(/const MIN_LEN = (\d+);/)[1];
    const max = policy.match(/const MAX_LEN = (\d+);/)[1];
    // DERIVED from the policy file, never typed here: a test that hardcodes the same
    // literal the code uses proves only that someone typed it twice.
    assert.match(vm, new RegExp('NameMinLength = ' + min + ';'),
        'CanSubmitName must not promise a length the server refuses');
    assert.match(vm, new RegExp('NameMaxLength = ' + max + ';'));
    assert.ok(!/1\.\.24/.test(vm.split('\n').filter((l) => !l.trim().startsWith('//')).join('\n')),
        'the retired "1..24" rule must not survive in code');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. THE NEW BATCH READ
// ═══════════════════════════════════════════════════════════════════════════

test('the batch read is authenticated, GET-only, and reads the real column', () => {
    const src = read(ROUTE);
    assert.match(src, /authenticate\(sql, req, null, claimed\)/,
        'authed, not public — a roster of names must not be enumerable by anyone who can type an address');
    assert.match(src, /req\.method !== 'GET'/, 'GET only');
    assert.match(src, /FROM player_profiles/, 'the one player-scoped profile row');
    assert.match(src, /SELECT wallet, username/, 'and the column that already holds the name');
    assert.ok(!src.includes('INSERT') && !src.includes('UPDATE'),
        'this route is a READ; the write stays on the existing username endpoint');
});

test('⛔ the response is an ARRAY of rows — JsonUtility cannot parse a map', () => {
    const src = read(ROUTE);
    assert.match(src, /names: \[\]/, 'the empty answer is an empty ARRAY');
    assert.match(src, /rows\.map\(\(r\) => \(\{ playerId: r\.wallet, username: r\.username \}\)\)/,
        'and a populated answer is an array of {playerId, username} rows');
    // The client side must agree, or the whole Members tab silently shows short ids.
    const wire = read(WIRE);
    assert.match(wire, /public UsernameRowDto\[\] names;/,
        'the DTO must expect an array; a Dictionary field would bind nothing at all');
});

test('the id list is capped and de-duplicated before it reaches the database', () => {
    const src = read(ROUTE);
    assert.match(src, /const MAX_IDS = 64;/, 'a hostile query must not become an unbounded IN-list');
    assert.match(src, /ids\.indexOf\(id\) !== -1/, 'duplicates are dropped rather than queried twice');
    assert.match(src, /WHERE wallet = ANY\(\$\{ids\}\)/, 'one query, not one per member');
});

test('the caller identity is required, because authenticate() cannot be called without one', () => {
    const src = read(ROUTE);
    assert.match(src, /AuthCode\.PLAYER_ID_MISSING/,
        'a query carrying only playerIds is a 400, named — not a silent empty list');
    assert.match(src, /quietFail\(res, 401, auth\.code/,
        'and an auth refusal uses the same quiet envelope every other route answers with');
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE CLIENT CALLS THE ROUTES THAT EXIST
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ every /api/ path the Circle client names resolves to a real file', () => {
    const src = read(SOURCE);
    const paths = src.match(/"\/api\/[a-z0-9/_-]+"/g) || [];
    assert.ok(paths.length >= 15, 'the screen makes five reads and ten writes; found ' + paths.length);
    for (const quoted of paths) {
        const rel = quoted.slice(2, -1); // strip the quotes and the leading slash
        const file = path.join(REPO, rel + '.js');
        assert.ok(fs.existsSync(file),
            quoted + ' does not resolve to a file — ' + rel + '.js is missing');
    }
});

test('the client signs every request through the one seam and fails closed', () => {
    const src = read(SOURCE);
    assert.match(src, /BackendRequestSigner\.TryAttachAsync/, 'never hand-rolled headers');
    assert.match(src, /could not attach auth \(fail-closed\)/,
        'a false return means ABORT — the request is never sent');
    // Reads pass false, writes pass the literal true: a non-purchase POST with no live
    // session is never sent otherwise, which on a phone is a button that does nothing.
    assert.match(src, /SendGet\("clan\/me", MeUrl \+ "\?playerId=" \+ Escape\(WalletAddress\), false/);
    assert.match(src, /SendPost\("clan\/create", CreateUrl, payload, true/);
    assert.match(src, /SendPost\("clan\/join", JoinUrl, payload, true/);
});

test('⛔ ruling 4 — nothing on the join path mentions a stake', () => {
    const code = (rel) => read(rel).split('\n').filter((l) => !l.trim().startsWith('//')).join('\n');
    for (const rel of [VM, SOURCE]) {
        for (const banned of ['MinimumStake', 'RequiresStake', 'percent_staked', 'vigil_contribution']) {
            assert.ok(!code(rel).includes(banned),
                rel + ' names ' + banned + '; joining a Circle is NEVER gated on staking');
        }
    }
});

test('⛔ ruling 3 — the stat magnitude is not even on the wire DTO', () => {
    const wire = read(WIRE);
    const code = wire.split('\n').filter((l) => !l.trim().startsWith('//')).join('\n');
    assert.ok(!code.includes('public string effect;'),
        'options[].effect must not be declared — the client physically cannot render a stat edge');
    assert.ok(!code.includes('public string perk_id;'),
        'a perk is shown as its narrative tier line, never as a stat identifier');
    const vm = read(VM);
    assert.match(vm, /EffectText = string\.Empty,/,
        'the VM assigns EffectText exactly one value, and that value is empty');
});
