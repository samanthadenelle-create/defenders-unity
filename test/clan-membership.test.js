// =============================================================================
// test/clan-membership.test.js — WO-1845 (clan step 2).
// -----------------------------------------------------------------------------
// The clan data model plus create / join / leave / me, asserted at RUNTIME against
// a recording tagged-template mock — zero network, zero database, zero Unity. The
// mock shape is borrowed verbatim from test/wallet-identity.test.js (WO-1844) and
// the route loader from test/admin.ads.view.test.js, so the clan lane exercises
// these seams ONE way rather than inventing a second.
//
// WHAT THIS FILE IS ACTUALLY GUARDING, beyond the acceptance criteria:
//
//   1. ATOMICITY. Creating a clan writes TWO rows and leaving writes up to two. The
//      Neon HTTP driver sends one statement per call, so a two-call version leaves a
//      window where a clan exists with no leader. Both are asserted to be ONE
//      statement carrying both writes.
//   2. THE SNAPSHOT RULE in the leave statement. Sub-statements in a WITH clause all
//      see the same snapshot, so "is this clan empty" CANNOT be asked as "no rows in
//      clan_members" — the row the sibling CTE is removing is still visible, and the
//      clan would never be cleaned up. The predicate must exclude the leaver by
//      wallet, and this file fails if someone "simplifies" it back.
//   3. THE CODE IS DRAWN WITH crypto.randomInt. An invite code is the entire access
//      control on joining a clan; Math.random makes it guessable.
//   4. THE COLLISION RETRY IS DRIVEN BY 23505, not by a pre-SELECT. A "is this code
//      free" read before the INSERT is a race dressed as a check.
//   5. ONE AUTH SHAPE ACROSS FOUR ROUTES (acceptance criterion 8), which is a
//      property only the shared preamble in api/_lib/clan-http.js can hold.
//
// Plus the deploy-order oracles: the tables this code writes are created by a file
// under api/migrations/ (the only thing a deploy applies), that file passes the one
// runner's additive audit, and api/schema.sql describes it and names it.
//
//     node --test test/clan-membership.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const CLAN_LIB = path.join(REPO, 'api', '_lib', 'clan.js');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const ROUTES = {
    create: path.join(REPO, 'api', 'clan', 'create.js'),
    join: path.join(REPO, 'api', 'clan', 'join.js'),
    leave: path.join(REPO, 'api', 'clan', 'leave.js'),
    me: path.join(REPO, 'api', 'clan', 'me.js'),
    // WO-1846. Added to THIS map, not to a second one, so the shared-preamble oracles
    // below (identical 401 shape, the guest rail cannot reach a clan table, OPTIONS is
    // answered, bodyParser:false survives the exports assignment) cover the three new
    // routes for free. That is the whole point of acceptance criterion 8 of WO-1845.
    promote: path.join(REPO, 'api', 'clan', 'promote.js'),
    demote: path.join(REPO, 'api', 'clan', 'demote.js'),
    kick: path.join(REPO, 'api', 'clan', 'kick.js'),
};
const MIGRATIONS_DIR = path.join(REPO, 'api', 'migrations');
const MIGRATION = '20260917_0030_clan_tables.sql';
const MIGRATION_PATH = path.join(MIGRATIONS_DIR, MIGRATION);
const IDENTITY_MIGRATION = '20260917_0029_wallet_identity.sql';
const SCHEMA_SQL = path.join(REPO, 'api', 'schema.sql');

const clan = require(CLAN_LIB);
const { AuthCode } = require(path.join(REPO, 'api', '_lib', 'wallet-auth.js'));

// A real base58 Solana address shape — 44 chars, no 0/O/I/l — so WALLET_RE accepts it.
const WALLET = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const WALLET_B = '9nQxTpRvE4mLcYbWz3kFhJ6dSgA8uXvNqMePkRtZwYcD';
const GUEST = 'guest-local-' + 'b'.repeat(64);
// SESSION_RE is /^[A-Za-z0-9_-]{40,90}$/.
const SESSION = 'a'.repeat(44);
const CLAN_ID = '3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e';

// ═══════════════════════════════════════════════════════════════════════════
// HARNESS
// ═══════════════════════════════════════════════════════════════════════════

// A recording tagged-template client. neon(...) is called as sql`text ${v}`, i.e.
// sql(strings, ...values). The FIRST matching route entry wins, so one query can be
// made to throw while the rest behave; a `rowsFor` function sees the call index,
// which is what lets the code-collision retry be driven realistically.
function recordingSql(route = []) {
    const calls = [];
    const fn = (strings, ...values) => {
        const text = Array.isArray(strings) ? strings.join(' ? ') : String(strings);
        calls.push({ text: text, values: values });
        for (const r of route) {
            if (!r.match.test(text)) continue;
            const hit = calls.filter(c => r.match.test(c.text)).length; // 1-based
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

/** A Postgres unique-violation as the driver surfaces it. */
function uniqueErr(constraint) {
    const e = new Error(`duplicate key value violates unique constraint "${constraint}"`);
    e.code = '23505';
    e.constraint = constraint;
    return e;
}

/** A Postgres foreign-key violation onto wallet_identity. */
function fkErr() {
    const e = new Error('insert or update on table "clans" violates foreign key constraint "clans_created_by_wallet_fkey"');
    e.code = '23503';
    e.constraint = 'clans_created_by_wallet_fkey';
    e.detail = 'Key (created_by_wallet) is not present in table "wallet_identity".';
    return e;
}

// ⚠ MATCH ON A TOKEN UNIQUE TO EACH STATEMENT, not on the table name. The leave
// statement's emptiness test also reads `FROM clan_members m`, so a membership-read
// matcher spelled that way swallows the removal and every leave case fails for a
// fixture reason — which is exactly what happened on the first run of this file.
const MEMBERSHIP_READ = /JOIN\s+clans\s+c\s+ON/i;
const CLAN_INSERT = /INSERT\s+INTO\s+clans/i;
const MEMBER_INSERT = /INSERT\s+INTO\s+clan_members/i;
const MEMBER_REMOVE = /WITH\s+departed\s+AS/i;

/** The row readMembership expects back. */
function membershipRow(role, extra = {}) {
    return Object.assign({
        clan_id: CLAN_ID, code: 'ABC234', name: 'Ember Wardens', tag: 'EMBR',
        join_policy: 'invite', created_at: '2026-09-17T00:00:00Z',
        role: role, joined_at: '2026-09-17T00:00:00Z', member_count: 2,
    }, extra);
}

/** The row the create CTE returns. */
function createdRow(code) {
    return {
        id: CLAN_ID, code: code, name: 'Ember Wardens', tag: 'EMBR',
        join_policy: 'invite', created_at: '2026-09-17T00:00:00Z',
        role: 'leader', joined_at: '2026-09-17T00:00:00Z',
    };
}

// ── The route harness ────────────────────────────────────────────────────────
// api/_lib/clan-http.js is the ONE place that calls neon(), so the driver stub must
// be in place while THAT module is (re)loaded, not just the route.
const DRIVER_ID = require.resolve('@neondatabase/serverless');

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
    const routePath = ROUTES[name];
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = { neon() { return sqlFn; } };
    delete require.cache[require.resolve(CLAN_HTTP)];
    delete require.cache[require.resolve(routePath)];
    const handler = require(routePath);
    // Restore immediately — the loaded module holds the stub through its closure, and
    // a leaked stub would poison every later test file in the same process.
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(routePath)];
    delete require.cache[require.resolve(CLAN_HTTP)];
    return handler;
}

async function callRoute(name, opts = {}) {
    const method = opts.method || (name === 'me' ? 'GET' : 'POST');
    const sqlFn = opts.sql || recordingSql();
    const headers = Object.assign({ 'x-session': SESSION }, opts.headers || {});
    const req = { method: method, headers: headers, query: opts.query || {} };
    if (method === 'POST' && opts.body !== undefined) {
        // A STRING body is the readBodyExact "already parsed, bytes are exact" path —
        // no stream needed, and it is the shape Vercel's runtime actually hands over.
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
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, sql: sqlFn };
}

/** A route-level sql that authenticates WALLET via the session rail, plus clan routes. */
function authedSql(route = []) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i, rows: [{ first_seen_at: 'T0', last_seen_at: 'T0' }] },
        ...route,
    ]);
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE INVITE CODE — minted here, and unguessable
// ═══════════════════════════════════════════════════════════════════════════

test('generateClanCode always satisfies the migration CHECK, over many draws', () => {
    const seen = new Set();
    for (let i = 0; i < 2000; i++) {
        const code = clan.generateClanCode();
        assert.equal(code.length, 6);
        assert.match(code, clan.CODE_RE,
            'the code must satisfy clans_code_format or the INSERT is a 23514, not a clan: ' + code);
        assert.ok(!/[O0I1]/.test(code),
            'the excluded characters exist so a code read aloud cannot be mistyped into ANOTHER clan');
        for (const ch of code) assert.ok(clan.CODE_ALPHABET.includes(ch), 'off-alphabet character: ' + ch);
        seen.add(code);
    }
    // 2000 draws from 32^6 collide with vanishing probability; a generator stuck on one
    // value (a fixed seed, a mis-scoped loop variable) is caught here and nowhere else.
    assert.ok(seen.size > 1900, `the generator is barely varying: ${seen.size} distinct of 2000`);
});

test('⛔ the code is drawn with crypto.randomInt, never Math.random', () => {
    const src = fs.readFileSync(CLAN_LIB, 'utf8');
    const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
    assert.ok(!/Math\.random/.test(code),
        'an invite code IS the access control on joining a clan. Math.random is seeded from ' +
        'observable state and is modulo-biased over a 32-character alphabet.');
    assert.match(code, /crypto\.randomInt\(/, 'randomInt is both unpredictable and bias-free');
});

test('the client and server draw from the SAME alphabet', () => {
    const cs = fs.readFileSync(path.join(REPO, 'Assets', '_Modules', 'Core', 'Services', 'ClanService.cs'), 'utf8');
    const m = /CodeAlphabet\s*=\s*"([^"]+)"/.exec(cs);
    assert.ok(m, 'fixture assumption: ClanService.cs still declares CodeAlphabet');
    assert.equal(clan.CODE_ALPHABET, m[1],
        'the server now mints the code, but the client still RENDERS and accepts one — a divergent ' +
        'alphabet means a legitimate code the client refuses to let the player type');
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. VALIDATION — a bound violation is a 400, never a 23514 dressed as a 500
// ═══════════════════════════════════════════════════════════════════════════

test('name is bounded 1..32 and trimmed', () => {
    assert.equal(clan.normalizeName('  Ember Wardens  ').value, 'Ember Wardens');
    assert.equal(clan.normalizeName('x'.repeat(32)).ok, true, '32 is inside clans_name_len');
    assert.equal(clan.normalizeName('x'.repeat(33)).code, clan.ClanCode.BAD_NAME,
        'test plan item 5: a name longer than 32 characters must be a 400, not a CHECK violation');
    assert.equal(clan.normalizeName('   ').code, clan.ClanCode.BAD_NAME);
    assert.equal(clan.normalizeName(null).code, clan.ClanCode.BAD_NAME);
});

test('tag is uppercased and bounded 1..5', () => {
    assert.equal(clan.normalizeTag(' embr ').value, 'EMBR');
    assert.equal(clan.normalizeTag('ABCDE').ok, true);
    assert.equal(clan.normalizeTag('ABCDEF').code, clan.ClanCode.BAD_TAG);
    assert.equal(clan.normalizeTag('').code, clan.ClanCode.BAD_TAG);
});

test('an invite code carrying an excluded character is a 400, not a 404', () => {
    assert.equal(clan.normalizeCode('abc234').value, 'ABC234', 'lowercase is normalised, never refused');
    for (const bad of ['ABC23O', 'ABC230', 'ABC23I', 'ABC231', 'ABC23', 'ABC2345', 'ABC-23', '']) {
        assert.equal(clan.normalizeCode(bad).ok, false, 'must refuse: ' + JSON.stringify(bad));
        assert.equal(clan.normalizeCode(bad).code, clan.ClanCode.BAD_CODE,
            'test plan item 4: the format refusal is its own answer — "that is not a code" and ' +
            '"no clan has that code" are different facts and a retyping player needs the first');
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. CREATE
// ═══════════════════════════════════════════════════════════════════════════

test('create writes the clan AND the leader row in ONE statement', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: CLAN_INSERT, answer: () => ({ rows: [createdRow('ABC234')] }) },
    ]);
    const r = await clan.createClan(sql, WALLET, 'Ember Wardens', 'embr');

    assert.equal(r.ok, true);
    assert.equal(r.role, 'leader', 'the creator is seated as Leader');
    assert.match(r.code, clan.CODE_RE);
    assert.equal(r.clanId, CLAN_ID);
    assert.equal(r.tag, 'EMBR');

    const writes = sql.matching(CLAN_INSERT);
    assert.equal(writes.length, 1, 'one attempt, one statement');
    const q = writes[0].text;
    assert.match(q, MEMBER_INSERT,
        '⛔ BOTH writes live in the SAME statement. Two calls leave a window in which a clan ' +
        'exists with no leader, and the HTTP driver has no transaction across calls.');
    assert.match(q, /WITH\s+new_clan\s+AS/i, 'the shape that makes it atomic is a data-modifying CTE');
    assert.match(q, /SELECT\s+id\s*,\s*\?\s*,\s*\?\s+FROM\s+new_clan/i,
        'the member row takes the clan id from the insert\'s RETURNING, never from a second read');

    // Everything player-supplied is a bound parameter. A clan name is free text.
    assert.ok(writes[0].values.includes('Ember Wardens'), 'the name is PARAMETERISED');
    assert.ok(writes[0].values.includes('EMBR'));
    assert.ok(writes[0].values.filter(v => v === WALLET).length >= 2,
        'the wallet is bound for both the clans row and the members row');
    assert.ok(writes[0].values.includes('leader'));
});

test('a code collision retries with a DIFFERENT code and succeeds', async () => {
    const tried = [];
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },
        {
            match: CLAN_INSERT,
            answer: (hit, values) => {
                tried.push(values[0]);
                if (hit === 1) return { throws: uniqueErr('clans_code_key') };
                return { rows: [createdRow(values[0])] };
            },
        },
    ]);
    const r = await clan.createClan(sql, WALLET, 'Ember Wardens', 'EMBR');

    assert.equal(r.ok, true, 'a collision is a retry, never a refusal the player sees');
    assert.equal(tried.length, 2, 'exactly one redraw');
    assert.notEqual(tried[0], tried[1], 'the retry must DRAW AGAIN, not resubmit the same code');
    assert.equal(sql.matching(/SELECT[\s\S]*FROM\s+clans\s+WHERE\s+code/i).length, 0,
        '⛔ there is deliberately NO pre-SELECT of the code: "is this code free" answered before ' +
        'the INSERT is a race, not a check. The UNIQUE constraint is the authority.');
});

test('ten collisions in a row is a SERVER failure, not a bad request', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: CLAN_INSERT, throws: uniqueErr('clans_code_key') },
    ]);
    const r = await clan.createClan(sql, WALLET, 'Ember Wardens', 'EMBR');
    assert.equal(r.ok, false);
    assert.equal(r.status, 500, 'ten collisions from 32^6 codes is a broken database, not the player');
    assert.equal(r.code, clan.ClanCode.CODE_UNAVAILABLE);
    assert.equal(sql.matching(CLAN_INSERT).length, clan.MAX_CODE_ATTEMPTS,
        'the loop is bounded — an unbounded retry against a real fault is an outage');
});

test('create refuses a wallet that is already in a clan, and writes nothing', async () => {
    const sql = recordingSql([{ match: MEMBERSHIP_READ, rows: [membershipRow('member')] }]);
    const r = await clan.createClan(sql, WALLET, 'Ember Wardens', 'EMBR');
    assert.equal(r.status, 409);
    assert.equal(r.code, clan.ClanCode.ALREADY_IN_CLAN);
    assert.equal(sql.matching(CLAN_INSERT).length, 0, 'no clan is created for a wallet that cannot join it');
});

test('⛔ a RACED membership collision returns the same 409 the read would have', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },                       // the read says free…
        { match: CLAN_INSERT, throws: uniqueErr('clan_members_one_clan_per_wallet') }, // …the index says no
    ]);
    const r = await clan.createClan(sql, WALLET, 'Ember Wardens', 'EMBR');
    assert.equal(r.status, 409, 'two simultaneous requests both pass the read; the INDEX refuses the second');
    assert.equal(r.code, clan.ClanCode.ALREADY_IN_CLAN);
    assert.equal(r.detail.raced, true, 'and it says it was a race, so the log can tell them apart');
});

test('a bad name never reaches the database at all', async () => {
    const sql = recordingSql();
    const r = await clan.createClan(sql, WALLET, 'x'.repeat(33), 'EMBR');
    assert.equal(r.status, 400);
    assert.equal(r.code, clan.ClanCode.BAD_NAME);
    assert.equal(sql.calls.length, 0, 'validation is cheaper than a round trip and must happen first');
});

test('a missing wallet_identity row is reported as a DEPLOY fault, not a player error', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: CLAN_INSERT, throws: fkErr() },
    ]);
    const r = await clan.createClan(sql, WALLET, 'Ember Wardens', 'EMBR');
    assert.equal(r.status, 500, 'the FK can only fail if migration 0029 is absent or the identity ' +
        'touch degraded fail-open — neither is the caller\'s fault');
    assert.equal(r.code, clan.ClanCode.IDENTITY_MISSING);
    assert.equal(r.detail.migration, '0029', 'and it names the migration, so the fix is one read away');
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. JOIN
// ═══════════════════════════════════════════════════════════════════════════

test('join resolves the code and seats the member in ONE statement', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: /FROM\s+clans/i, rows: [{
            id: CLAN_ID, code: 'ABC234', name: 'Ember Wardens', tag: 'EMBR',
            join_policy: 'invite', created_at: 'T0', role: 'member', joined_at: 'T1',
        }] },
    ]);
    const r = await clan.joinClan(sql, WALLET_B, 'abc234');

    assert.equal(r.ok, true);
    assert.equal(r.role, 'member', 'a joiner is a Member — no role assignment exists in this ticket');
    assert.equal(r.clanId, CLAN_ID);

    const q = sql.matching(MEMBER_INSERT)[0];
    assert.ok(q, 'the insert must be part of the lookup statement');
    assert.match(q.text, /WITH\s+target\s+AS/i,
        'resolving the code in one call and inserting in another lets the clan be removed in between');
    assert.ok(q.values.includes('ABC234'),
        'the code is UPPERCASE-NORMALISED before the lookup — a player typing lowercase joins');
    assert.ok(q.values.includes(WALLET_B));
});

test('an unknown code is a 404', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: /WITH\s+target\s+AS/i, rows: [] },   // the target CTE matched nothing
    ]);
    const r = await clan.joinClan(sql, WALLET_B, 'ZZZ999');
    assert.equal(r.status, 404);
    assert.equal(r.code, clan.ClanCode.NOT_FOUND);
});

test('a malformed code is a 400 and never reaches the database', async () => {
    const sql = recordingSql();
    const r = await clan.joinClan(sql, WALLET_B, 'ABC2O0');
    assert.equal(r.status, 400);
    assert.equal(r.code, clan.ClanCode.BAD_CODE);
    assert.equal(sql.calls.length, 0);
});

test('joining while already in a clan is a 409, by the read and by the index', async () => {
    const byRead = recordingSql([{ match: MEMBERSHIP_READ, rows: [membershipRow('leader')] }]);
    const a = await clan.joinClan(byRead, WALLET, 'ABC234');
    assert.equal(a.status, 409);
    assert.equal(a.code, clan.ClanCode.ALREADY_IN_CLAN);
    assert.equal(byRead.matching(MEMBER_INSERT).length, 0);

    const byIndex = recordingSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: /WITH\s+target\s+AS/i, throws: uniqueErr('clan_members_one_clan_per_wallet') },
    ]);
    const b = await clan.joinClan(byIndex, WALLET, 'ABC234');
    assert.equal(b.status, 409, 'the raced second join gets the identical answer');
    assert.equal(b.code, clan.ClanCode.ALREADY_IN_CLAN);
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. LEAVE — including the snapshot rule
// ═══════════════════════════════════════════════════════════════════════════

test('a Member leaves, and the clan is cleaned up when nobody else is left', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('member', { member_count: 1 })] },
        { match: MEMBER_REMOVE, rows: [{ departed_rows: 1, emptied_rows: 1 }] },
    ]);
    const r = await clan.leaveClan(sql, WALLET_B);

    assert.equal(r.ok, true);
    assert.equal(r.clanRemoved, true, 'last member out removes the clan');

    const q = sql.matching(MEMBER_REMOVE)[0].text;
    assert.match(q, /role\s*<>\s*\?/i,
        'the leader guard is in the STATEMENT as well as the read, so a role change landing ' +
        'between the two cannot slip a leader out');
    assert.match(q, /m\.wallet\s*<>\s*\?/i,
        '⛔ THE LOAD-BEARING PREDICATE. Sub-statements in a WITH clause share ONE snapshot, so ' +
        'the row being removed by the sibling CTE is STILL VISIBLE to an emptiness test written ' +
        'as "no rows in clan_members". The question has to be "no member OTHER THAN me" or the ' +
        'clan is never cleaned up — and nothing else in this repo would notice.');
});

test('a Member leaves a clan that still has others, and the clan survives', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('member', { member_count: 2 })] },
        { match: MEMBER_REMOVE, rows: [{ departed_rows: 1, emptied_rows: 0 }] },
    ]);
    const r = await clan.leaveClan(sql, WALLET_B);
    assert.equal(r.ok, true);
    assert.equal(r.clanRemoved, false, 'test plan item 2: Wallet A is still Leader and the clan remains');
});

// ⚠ SUPERSEDED BY WO-1846 — the ORIGINAL of this case asserted that a Leader CANNOT
// leave (a flat 409 `leader_must_transfer`, nothing written). That was WO-1845's
// deliberate hole, pending succession, and succession now exists: leaveClan hands a
// Leader to leaveAsLeader. The rewritten case below pins what is left of the old rule —
// the member-removal statement is NOT the path a leader takes — and the succession
// behaviour itself is asserted in test/clan-roles.test.js.
test('a Leader no longer takes the member-removal path (succession is WO-1846)', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: /WITH\s+mine\s+AS/i, rows: [{ departed_rows: 1, disbanded_rows: 0, new_leader: WALLET_B }] },
    ]);
    const r = await clan.leaveClan(sql, WALLET);
    assert.equal(r.ok, true, 'WO-1846 gave the Leader an exit; the 409 that used to be here is retired');
    assert.equal(r.newLeader, WALLET_B);
    assert.equal(sql.matching(MEMBER_REMOVE).length, 0,
        'the non-leader statement must not be reused for a leader — its predicate is `role <> leader`, ' +
        'so it would remove nothing and report a race');
});

test('leaving when in no clan is a 404', async () => {
    const sql = recordingSql([{ match: MEMBERSHIP_READ, rows: [] }]);
    const r = await clan.leaveClan(sql, WALLET);
    assert.equal(r.status, 404);
    assert.equal(r.code, clan.ClanCode.NOT_IN_CLAN);
});

test('a removal that removed nothing is reported, never claimed as success', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('member')] },
        { match: MEMBER_REMOVE, rows: [{ departed_rows: 0, emptied_rows: 0 }] },
    ]);
    const r = await clan.leaveClan(sql, WALLET_B);
    assert.equal(r.ok, false, 'the read said member and the write removed nothing — the row changed ' +
        'underneath us, and a cheerful 200 would be a lie');
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. READ
// ═══════════════════════════════════════════════════════════════════════════

test('readMembership answers null for a wallet in no clan', async () => {
    const sql = recordingSql([{ match: MEMBERSHIP_READ, rows: [] }]);
    assert.equal(await clan.readMembership(sql, WALLET), null);
});

test('readMembership returns the clan, the role and the member count', async () => {
    const sql = recordingSql([{ match: MEMBERSHIP_READ, rows: [membershipRow('leader')] }]);
    const m = await clan.readMembership(sql, WALLET);
    assert.equal(m.clanId, CLAN_ID);
    assert.equal(m.role, 'leader');
    assert.equal(m.code, 'ABC234');
    assert.equal(m.memberCount, 2);
    assert.deepEqual(sql.matching(MEMBERSHIP_READ)[0].values, [WALLET],
        'the wallet is PARAMETERISED, never interpolated into the SQL text');
});

// ═══════════════════════════════════════════════════════════════════════════
// 7. THE ROUTES — one auth shape across four files (acceptance criterion 8)
// ═══════════════════════════════════════════════════════════════════════════

const ROUTE_NAMES = ['create', 'join', 'leave', 'me', 'promote', 'demote', 'kick'];

test('⛔ all four routes refuse an unauthenticated request with the IDENTICAL shape', async () => {
    const shapes = [];
    for (const name of ROUTE_NAMES) {
        // An unknown session and no signature headers → the wallet rail refuses.
        const sql = recordingSql([{ match: /FROM\s+auth_sessions/i, rows: [] }]);
        const { out } = await callRoute(name, {
            sql: sql,
            body: { playerId: WALLET, name: 'Ember Wardens', tag: 'EMBR', code: 'ABC234' },
            query: { playerId: WALLET },
        });
        assert.equal(out.statusCode, 401, name + ' must be 401 for an unproven caller');
        assert.equal(out.body.ok, false, name);
        assert.equal(out.body.code, AuthCode.SESSION_UNKNOWN, name + ' reports the stable auth code');
        assert.equal(typeof out.body.ref, 'string', name + ' carries a correlation ref');
        assert.ok(!('wallet' in out.body) && !('detail' in out.body),
            name + ': the player-facing body stays minimal — loud in the db, quiet to the player');
        shapes.push(Object.keys(out.body).sort().join(','));
        assert.equal(sql.matching(/clan/i).length, 0, name + ' touched a clan table before authenticating');
    }
    assert.equal(new Set(shapes).size, 1,
        'four copies of a preamble drift; ONE shared preamble cannot. This is the criterion.');
});

test('⛔ the GUEST rail cannot reach a clan table', async () => {
    for (const name of ROUTE_NAMES) {
        const sql = recordingSql([
            { match: /INSERT\s+INTO\s+guest_rate_limit/i, rows: [{ hits: 1, total_hits: 1 }] },
        ]);
        const { out } = await callRoute(name, {
            sql: sql,
            headers: { 'x-guest-id': GUEST },
            body: { playerId: GUEST, name: 'Ember Wardens', tag: 'EMBR', code: 'ABC234' },
            query: { playerId: GUEST },
        });
        assert.equal(out.statusCode, 401, name);
        assert.equal(out.body.code, AuthCode.WALLET_REQUIRED, name +
            ': every clan wallet column is a foreign key onto wallet_identity, which ONLY the wallet ' +
            'rail populates — a guest reaching the INSERT is a 23503 surfacing as a 500');
        assert.equal(sql.matching(/clan_members|INSERT\s+INTO\s+clans/i).length, 0, name);
    }
});

test('an OPTIONS preflight is answered 204 by every route, with the headers advertised', async () => {
    for (const name of ROUTE_NAMES) {
        const { out } = await callRoute(name, { method: 'OPTIONS' });
        assert.equal(out.statusCode, 204, name);
        assert.match(String(out.headers['access-control-allow-headers']), /X-Wallet/,
            name + ': a custom header absent from the preflight is a silently blocked web request');
    }
});

test('the wrong method is refused, and a missing identity is a 400', async () => {
    const { out: wrong } = await callRoute('create', { method: 'GET' });
    assert.equal(wrong.statusCode, 400);
    assert.equal(wrong.body.code, AuthCode.METHOD_NOT_ALLOWED);

    const { out: noId } = await callRoute('create', { body: { name: 'Ember Wardens', tag: 'EMBR' } });
    assert.equal(noId.statusCode, 400);
    assert.equal(noId.body.code, AuthCode.PLAYER_ID_MISSING,
        'authenticate() routes by the SHAPE of the id being acted on and cannot be called without one');

    const { out: badJson } = await callRoute('create', { body: '{not json' });
    assert.equal(badJson.statusCode, 400);
    assert.equal(badJson.body.code, AuthCode.BAD_PAYLOAD);
});

test('POST /api/clan/create answers the work order body', async () => {
    const sql = authedSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: CLAN_INSERT, answer: (hit, values) => ({ rows: [createdRow(values[0])] }) },
    ]);
    const { out } = await callRoute('create', {
        sql: sql,
        body: { playerId: WALLET, name: 'Ember Wardens', tag: 'embr' },
    });
    assert.equal(out.statusCode, 200);
    assert.equal(out.body.ok, true);
    assert.equal(out.body.clanId, CLAN_ID);
    assert.match(out.body.code, clan.CODE_RE, 'the returned code is a VALID code, minted server-side');
    assert.equal(out.body.name, 'Ember Wardens');
    assert.equal(out.body.tag, 'EMBR');
    assert.equal(out.body.role, 'leader');
});

test('POST /api/clan/create refuses an over-long name with a 400', async () => {
    const sql = authedSql([{ match: MEMBERSHIP_READ, rows: [] }]);
    const { out } = await callRoute('create', {
        sql: sql,
        body: { playerId: WALLET, name: 'x'.repeat(33), tag: 'EMBR' },
    });
    assert.equal(out.statusCode, 400, 'test plan item 5');
    assert.equal(out.body.error, clan.ClanCode.BAD_NAME);
    assert.equal(sql.matching(CLAN_INSERT).length, 0);
});

test('POST /api/clan/join answers 200 on a valid code and 404 on an unknown one', async () => {
    const ok = authedSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: /WITH\s+target\s+AS/i, rows: [{
            id: CLAN_ID, code: 'ABC234', name: 'Ember Wardens', tag: 'EMBR',
            join_policy: 'invite', created_at: 'T0', role: 'member', joined_at: 'T1',
        }] },
    ]);
    const good = await callRoute('join', { sql: ok, body: { playerId: WALLET, code: 'abc234' } });
    assert.equal(good.out.statusCode, 200);
    assert.equal(good.out.body.role, 'member');
    assert.equal(good.out.body.clanId, CLAN_ID);

    const missing = authedSql([
        { match: MEMBERSHIP_READ, rows: [] },
        { match: /WITH\s+target\s+AS/i, rows: [] },
    ]);
    const bad = await callRoute('join', { sql: missing, body: { playerId: WALLET, code: 'ZZZ999' } });
    assert.equal(bad.out.statusCode, 404);
    assert.equal(bad.out.body.error, clan.ClanCode.NOT_FOUND);

    const dupe = authedSql([{ match: MEMBERSHIP_READ, rows: [membershipRow('member')] }]);
    const already = await callRoute('join', { sql: dupe, body: { playerId: WALLET, code: 'ABC234' } });
    assert.equal(already.out.statusCode, 409);
    assert.equal(already.out.body.error, clan.ClanCode.ALREADY_IN_CLAN);

    const malformed = authedSql([{ match: MEMBERSHIP_READ, rows: [] }]);
    const bad400 = await callRoute('join', { sql: malformed, body: { playerId: WALLET, code: 'ABC2O0' } });
    assert.equal(bad400.out.statusCode, 400, 'test plan item 4: an excluded character is a 400');
    assert.equal(bad400.out.body.error, clan.ClanCode.BAD_CODE);
});

test('POST /api/clan/leave: a Member succeeds, a Leader gets the specified 409 body', async () => {
    const asMember = authedSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('member')] },
        { match: MEMBER_REMOVE, rows: [{ departed_rows: 1, emptied_rows: 0 }] },
    ]);
    const okOut = await callRoute('leave', { sql: asMember, body: { playerId: WALLET } });
    assert.equal(okOut.out.statusCode, 200);
    assert.deepEqual(okOut.out.body, { ok: true }, 'the work order specifies exactly { ok: true }');

    // ⚠ SUPERSEDED BY WO-1846: this branch used to assert the Leader's 409. A Leader
    // leaving now succeeds through succession, and `leader_must_transfer` survives only
    // as the race label. The 200 body stays ADDITIVE — a member still gets exactly
    // { ok: true }, asserted with deepEqual above.
    const asLeader = authedSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: /WITH\s+mine\s+AS/i, rows: [{ departed_rows: 1, disbanded_rows: 0, new_leader: WALLET_B }] },
    ]);
    const succeeded = await callRoute('leave', { sql: asLeader, body: { playerId: WALLET } });
    assert.equal(succeeded.out.statusCode, 200);
    assert.equal(succeeded.out.body.newLeader, WALLET_B, 'the successor is named in the response');
    assert.equal(succeeded.out.body.clanDeleted, undefined,
        'a field that is not true is ABSENT, not false — the member case must stay byte-identical');
    assert.equal(asLeader.matching(MEMBER_REMOVE).length, 0);
});

test('GET /api/clan/me answers clan:null for a wallet with no clan, and the clan for one with', async () => {
    const none = authedSql([{ match: MEMBERSHIP_READ, rows: [] }]);
    const empty = await callRoute('me', { sql: none, query: { playerId: WALLET } });
    assert.equal(empty.out.statusCode, 200,
        '⛔ NOT a 404: "you are in no clan" is a successful answer, and the ordinary state of every ' +
        'new player. A 404 would make the client treat it as an error.');
    assert.deepEqual(empty.out.body, { ok: true, clan: null });

    const some = authedSql([{ match: MEMBERSHIP_READ, rows: [membershipRow('leader')] }]);
    const full = await callRoute('me', { sql: some, query: { playerId: WALLET } });
    assert.equal(full.out.statusCode, 200);
    assert.equal(full.out.body.role, 'leader');
    assert.equal(full.out.body.clan.clanId, CLAN_ID);
    assert.equal(full.out.body.clan.tag, 'EMBR');
    assert.equal(full.out.body.clan.memberCount, 2);
});

test('⛔ every route exports the handler AND keeps its bodyParser:false config', () => {
    for (const name of ROUTE_NAMES) {
        delete require.cache[require.resolve(ROUTES[name])];
        const mod = require(ROUTES[name]);
        assert.equal(typeof mod, 'function', name + ' must export the handler');
        assert.equal(mod.config && mod.config.api && mod.config.api.bodyParser, false,
            name + ': `module.exports = handler` REPLACES the exports object, so a config assigned ' +
            'BEFORE it is thrown away — exactly how save.js ran with its body parser still active');
        delete require.cache[require.resolve(ROUTES[name])];
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 8. THE MIGRATION — because a table nothing creates is a 42P01 per request
// ═══════════════════════════════════════════════════════════════════════════

test('the migration exists, is re-runnable, and declares all three tables', () => {
    assert.ok(fs.existsSync(MIGRATION_PATH),
        `${MIGRATION} must exist under api/migrations/ — a CREATE that lives only in api/schema.sql ` +
        'is a DESCRIPTION; only api/migrations/ is ever applied');
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');

    for (const t of ['clans', 'clan_members', 'clan_messages']) {
        assert.match(body, new RegExp('CREATE\\s+TABLE\\s+IF\\s+NOT\\s+EXISTS\\s+' + t + '\\b', 'i'),
            t + ' must be created, with IF NOT EXISTS so the file survives a ledger re-apply');
    }
    assert.match(body, /CREATE\s+UNIQUE\s+INDEX\s+IF\s+NOT\s+EXISTS\s+clan_members_one_clan_per_wallet/i);
    assert.match(body, /ON\s+clan_members\s*\(\s*wallet\s*\)/i,
        'the one-clan-per-wallet index is on wallet ALONE — the composite primary key cannot express it');
    assert.match(body, /CREATE\s+INDEX\s+IF\s+NOT\s+EXISTS\s+clan_messages_clan_sent_idx/i);

    for (const c of ['clans_code_format', 'clans_name_len', 'clans_tag_len',
        'clan_members_role_valid', 'clan_messages_has_body']) {
        assert.match(body, new RegExp('CONSTRAINT\\s+' + c, 'i'), c + ' must be declared');
    }
    assert.match(body, /code\s*~\s*'\^\[A-HJ-NP-Z2-9\]\{6\}\$'/,
        'the CHECK and api/_lib/clan.js CODE_RE express the SAME alphabet and may never disagree');
    assert.equal(clan.CODE_RE.source, '^[A-HJ-NP-Z2-9]{6}$',
        'the JS mirror of that CHECK, pinned so a widened regex cannot start producing 23514s');

    // Three foreign keys onto the table WO-1844 created. This is the deploy-order fact.
    assert.equal((body.match(/REFERENCES\s+wallet_identity\s*\(\s*wallet\s*\)/gi) || []).length, 3,
        'created_by_wallet, clan_members.wallet and sender_wallet all key onto wallet_identity');
    assert.match(body, /gen_random_uuid\(\)/,
        'a PostgreSQL 13+ builtin, already the UUID primary-key default of account_deletion_requests ' +
        '(20260830_0014) on this same instance');

    assert.ok(!/\b(DROP|TRUNCATE)\b/i.test(body),
        'nothing destructive, including in the prose — the one runner refuses the whole run over it');
    assert.ok(!/INSERT\s+INTO/i.test(body),
        'no seed rows: a bare INSERT is the 23505 that makes a file un-re-runnable (see 0004)');
    assert.ok(!/ALTER\s+TABLE/i.test(body),
        'the CHECKs are INLINE in the CREATE bodies, which is what keeps them idempotent without ' +
        'the DROP-and-recreate guard 0010/0011/0012/0017 needed');
});

test('⛔ the migration sorts AFTER the wallet_identity migration it depends on', () => {
    const files = fs.readdirSync(MIGRATIONS_DIR).filter(f => f.toLowerCase().endsWith('.sql')).sort();
    assert.ok(files.includes(IDENTITY_MIGRATION), 'fixture assumption: WO-1844 landed its migration');
    assert.ok(files.indexOf(IDENTITY_MIGRATION) < files.indexOf(MIGRATION),
        'the one runner derives its list in FILENAME ORDER. Applied before 0029, every foreign key ' +
        'in 0030 fails at migration time with 42P01 — and the runner stops the whole run there.');
});

test('the one runner accepts this migration as purely additive', async () => {
    const { auditAdditive } = await import('file://' + path.join(REPO, 'tools', 'run-migrations.mjs').replace(/\\/g, '/'));
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');
    assert.deepEqual(auditAdditive(body), [],
        'auditAdditive is the ACTUAL gate a deploy applies (not this file\'s opinion of it). ' +
        'ON DELETE CASCADE inside a CREATE TABLE is fine — the destructive check anchors ' +
        'DELETE/TRUNCATE at statement start.');
});

test('api/schema.sql describes the same three tables and names the applyable migration', () => {
    const schema = fs.readFileSync(SCHEMA_SQL, 'utf8');
    for (const t of ['clans', 'clan_members', 'clan_messages']) {
        assert.match(schema, new RegExp('CREATE\\s+TABLE\\s+IF\\s+NOT\\s+EXISTS\\s+' + t + '\\b', 'i'),
            'schema.sql is the readable map of the database and may never disagree with api/migrations/');
    }
    assert.ok(schema.includes(MIGRATION),
        'the block must name its applyable migration by filename, as 0024/0027/0028/0029 do');
});

// ═══════════════════════════════════════════════════════════════════════════
// 9. NON-SCOPE — the things this ticket deliberately did NOT build
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ nothing in this lane writes clan_messages, and no chat endpoint exists', () => {
    const files = [CLAN_LIB, CLAN_HTTP, ...Object.values(ROUTES)];
    for (const f of files) {
        const src = fs.readFileSync(f, 'utf8');
        const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
        assert.ok(!/clan_messages/.test(code),
            path.basename(f) + ' touches clan_messages. The table is declared so its shape needs no ' +
            'second migration; chat is a later ticket and is this ticket\'s explicit non-scope.');
    }
    assert.ok(!fs.existsSync(path.join(REPO, 'api', 'clan', 'chat.js')), 'no chat endpoint in this ticket');
    assert.ok(!fs.existsSync(path.join(REPO, 'api', 'clan', 'messages.js')), 'no chat endpoint in this ticket');
});

// ⚠ RETIRED BY WO-1846, AND THE RETIREMENT IS THE POINT. The original asserted that
// NOTHING in api/_lib/clan.js assigns 'officer' and that there is no `UPDATE
// clan_members` anywhere — a correct pin on WO-1845's non-scope, and now false by
// design: WO-1846 is precisely the ticket that assigns the role. Rather than delete the
// case (and lose the fact that the role vocabulary is FIXED), it is inverted: the three
// roles the migration's CHECK permits are the three the code may write, and a FOURTH
// would be a 23514 on every write that used it.
test('⛔ the role vocabulary is exactly the three the CHECK permits', () => {
    const src = fs.readFileSync(CLAN_LIB, 'utf8');
    const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
    assert.equal(clan.LEADER, 'leader');
    assert.equal(clan.MEMBER, 'member');
    assert.equal(clan.OFFICER, 'officer', 'WO-1846 assigns the third role the CHECK always permitted');

    const migration = fs.readFileSync(MIGRATION_PATH, 'utf8');
    assert.match(migration, /role\s+IN\s*\(\s*'leader'\s*,\s*'officer'\s*,\s*'member'\s*\)/i,
        'clan_members_role_valid is the authority on what may be written');

    // Any OTHER quoted role-looking literal in the code is the bug this now guards: a
    // 'coleader' or 'recruit' would pass every JS test and fail the CHECK at runtime.
    const literals = new Set((code.match(/role\s*[:=]\s*'([a-z]+)'/gi) || []).map(s => /'([a-z]+)'/i.exec(s)[1]));
    for (const role of literals) {
        assert.ok(['leader', 'officer', 'member'].includes(role),
            'undeclared role literal "' + role + '" — the CHECK would refuse it with a 23514');
    }
});
