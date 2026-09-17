// =============================================================================
// test/clan-roles.test.js — WO-1846 (clan step 3): roles, authorization, leader
// succession and the clan action budgets.
// -----------------------------------------------------------------------------
// Same recording tagged-template mock as test/clan-membership.test.js (WO-1845), which
// borrowed it from test/wallet-identity.test.js (WO-1844) — zero network, zero database,
// zero Unity. The harness is copied rather than exported because that is the established
// pattern in this repo's api tests and because a shared harness that one lane edits is a
// way for two lanes to break each other's files.
//
// WHAT THIS FILE GUARDS BEYOND THE ACCEPTANCE CRITERIA:
//
//   1. AUTHORIZATION IS IN THE STATEMENT, NOT ONLY IN THE READ. Every role write
//      repeats the caller's role (an EXISTS) and the target's role (an `AND role = ...`)
//      inside the UPDATE/DELETE, so a caller demoted between the read and the write
//      cannot still act. A "simplification" that trusts the read is caught here.
//   2. NO MEMBERSHIP ORACLE. The target read is scoped by clan_id, so "no such wallet"
//      and "in another clan" are one indistinguishable 404. Without that, /promote is a
//      service for learning which clan any address belongs to.
//   3. SUCCESSION IS ONE STATEMENT, AND ITS ORDER IS TOTAL. Officers before Members,
//      oldest first, wallet as the final tiebreak — an unordered pick makes succession
//      irreproducible, which is the class of bug nobody can repro on purpose.
//   4. THE SNAPSHOT RULE, AGAIN. The successor CTE must exclude the leaver BY WALLET
//      (the sibling DELETE is invisible to it under the shared snapshot) or a leaving
//      Leader can elect themself.
//   5. THE BUDGET FAILS OPEN AND IS SPENT BEFORE THE ACTION. A missing clan_rate_limit
//      table must never 500 a clan request, and a refused attempt must still cost.
//
//     node --test test/clan-roles.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const CLAN_LIB = path.join(REPO, 'api', '_lib', 'clan.js');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const WALLET_AUTH = path.join(REPO, 'api', '_lib', 'wallet-auth.js');
const ROUTES = {
    promote: path.join(REPO, 'api', 'clan', 'promote.js'),
    demote: path.join(REPO, 'api', 'clan', 'demote.js'),
    kick: path.join(REPO, 'api', 'clan', 'kick.js'),
    leave: path.join(REPO, 'api', 'clan', 'leave.js'),
};
const MIGRATIONS_DIR = path.join(REPO, 'api', 'migrations');
const MIGRATION = '20260917_0032_clan_rate_limit.sql';
const MIGRATION_PATH = path.join(MIGRATIONS_DIR, MIGRATION);
const CLAN_MIGRATION = '20260917_0030_clan_tables.sql';
const SCHEMA_SQL = path.join(REPO, 'api', 'schema.sql');

const clan = require(CLAN_LIB);
const auth = require(WALLET_AUTH);
const { AuthCode, touchClanRate, CLAN_RATE_LIMITS, CLAN_RATE_WINDOW_SECONDS } = auth;

// Base58 Solana-shaped addresses (no 0/O/I/l), so isWalletId accepts them.
const LEAD = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
const OFF = '9nQxTpRvE4mLcYbWz3kFhJ6dSgA8uXvNqMePkRtZwYcD';
const MEM = '4kRzWpYnQ7tLmXvBcHdFgJ2sAeUwNqVxPkMrTyZbCgLd';
const SESSION = 'a'.repeat(44);
const CLAN_ID = '3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e';

// ═══════════════════════════════════════════════════════════════════════════
// HARNESS
// ═══════════════════════════════════════════════════════════════════════════

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

// ⚠ MATCH ON A TOKEN UNIQUE TO EACH STATEMENT (the lesson recorded in
// clan-membership.test.js): several clan statements read `FROM clan_members`, so a
// matcher spelled that way swallows the wrong one and every case fails for a fixture
// reason rather than a real one.
const MEMBERSHIP_READ = /JOIN\s+clans\s+c\s+ON/i;
const TARGET_READ = /SELECT\s+wallet\s*,\s*role\s*,\s*joined_at/i;
const ROLE_UPDATE = /UPDATE\s+clan_members\s+SET\s+role/i;
const KICK_DELETE = /DELETE\s+FROM\s+clan_members\s+WHERE\s+clan_id/i;
const SUCCESSION = /WITH\s+mine\s+AS/i;
const RATE_UPSERT = /INSERT\s+INTO\s+clan_rate_limit/i;

function membershipRow(role, extra = {}) {
    return Object.assign({
        clan_id: CLAN_ID, code: 'ABC234', name: 'Ember Wardens', tag: 'EMBR',
        join_policy: 'invite', created_at: '2026-09-17T00:00:00Z',
        role: role, joined_at: '2026-09-17T00:00:00Z', member_count: 3,
    }, extra);
}

function targetRow(wallet, role) {
    return { wallet: wallet, role: role, joined_at: '2026-09-17T01:00:00Z' };
}

/** A recording client wired for "caller holds callerRole, target holds targetRole". */
function roleSql(callerRole, target, targetRole, extra = []) {
    return recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow(callerRole)] },
        { match: TARGET_READ, rows: [targetRow(target, targetRole)] },
        ...extra,
    ]);
}

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

const DRIVER_ID = require.resolve('@neondatabase/serverless');

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
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(routePath)];
    delete require.cache[require.resolve(CLAN_HTTP)];
    return handler;
}

async function callRoute(name, opts = {}) {
    const sqlFn = opts.sql || recordingSql();
    const headers = Object.assign({ 'x-session': SESSION }, opts.headers || {});
    const req = { method: opts.method || 'POST', headers: headers, query: opts.query || {} };
    if (req.method === 'POST' && opts.body !== undefined) {
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

/** Authenticates the WALLET rail through the session path, then the clan routes. */
function authedSql(route = []) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: LEAD, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i, rows: [{ first_seen_at: 'T0', last_seen_at: 'T0' }] },
        ...route,
    ]);
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. TARGET VALIDATION — before any read
// ═══════════════════════════════════════════════════════════════════════════

test('a target that is not wallet-shaped is a 400 and never reaches the database', async () => {
    for (const bad of [undefined, null, '', '   ', 'guest-local-' + 'b'.repeat(64), 'not-a-wallet', '0OIl']) {
        const sql = recordingSql();
        const r = await clan.promoteMember(sql, LEAD, bad);
        assert.equal(r.status, 400, 'must refuse: ' + JSON.stringify(bad));
        assert.equal(r.code, clan.ClanCode.BAD_TARGET);
        assert.equal(sql.calls.length, 0,
            '"that is not an address" and "they are not in your clan" are different facts, and the ' +
            'first one costs no round trip');
    }
});

test('the wallet shape rule is IMPORTED, never re-spelled in clan.js', () => {
    const src = fs.readFileSync(CLAN_LIB, 'utf8');
    assert.match(src, /require\('\.\/wallet-auth'\)/,
        'a second copy of WALLET_RE is the duplicated-state failure CLAUDE.md §5/§8 describe');
    const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
    assert.ok(!/\[1-9A-HJ-NP-Za-km-z\]/.test(code), 'the base58 regex must not be duplicated here');
});

test('⛔ a role operation aimed at yourself is a 400, and reads nothing', async () => {
    for (const op of ['promoteMember', 'demoteMember', 'kickMember']) {
        const sql = recordingSql();
        const r = await clan[op](sql, LEAD, LEAD);
        assert.equal(r.status, 400, op);
        assert.equal(r.code, clan.ClanCode.SELF_TARGET, op);
        assert.equal(sql.calls.length, 0, op);
    }
});

test('a self-kick is NOT quietly routed to leave — succession would be skipped', async () => {
    const sql = recordingSql();
    const r = await clan.kickMember(sql, LEAD, LEAD);
    assert.equal(r.code, clan.ClanCode.SELF_TARGET);
    assert.equal(sql.matching(SUCCESSION).length, 0,
        'a Leader removing themself through kick would leave the clan with no leader — the exact ' +
        'state this ticket closes. /leave owns that path because succession is attached to it.');
});

test('a caller in no clan is a 404 before the target is ever read', async () => {
    const sql = recordingSql([{ match: MEMBERSHIP_READ, rows: [] }]);
    const r = await clan.promoteMember(sql, LEAD, MEM);
    assert.equal(r.status, 404);
    assert.equal(r.code, clan.ClanCode.NOT_IN_CLAN);
    assert.equal(sql.matching(TARGET_READ).length, 0);
});

test('⛔ the target read is SCOPED BY clan_id — no membership oracle', async () => {
    const sql = roleSql('leader', MEM, 'member');
    await clan.promoteMember(sql, LEAD, MEM);
    const read = sql.matching(TARGET_READ)[0];
    assert.ok(read, 'the target must be read');
    assert.match(read.text, /WHERE\s+clan_id\s*=\s*\?\s+AND\s+wallet\s*=\s*\?/i,
        'asked as "WHERE wallet = $1" this read answers "yes, they are in clan X" for ANY address a ' +
        'caller types, and /promote becomes a clan-membership lookup service');
    assert.deepEqual(read.values, [CLAN_ID, MEM], 'both values PARAMETERISED');
});

test('a wallet in no clan and a wallet in ANOTHER clan are the same 404', async () => {
    // Scoped by clan_id, both cases return zero rows — indistinguishable by construction
    // rather than by remembering to collapse two codes into one.
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: TARGET_READ, rows: [] },
    ]);
    const r = await clan.kickMember(sql, LEAD, OFF);
    assert.equal(r.status, 404);
    assert.equal(r.code, clan.ClanCode.TARGET_NOT_IN_CLAN);
    assert.notEqual(r.code, clan.ClanCode.NOT_FOUND, 'and it is not the clan-not-found code either');
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. PROMOTE / DEMOTE
// ═══════════════════════════════════════════════════════════════════════════

test('a Leader promotes a Member to Officer, and the role is written', async () => {
    const sql = roleSql('leader', MEM, 'member', [
        { match: ROLE_UPDATE, rows: [{ wallet: MEM, role: 'officer' }] },
    ]);
    const r = await clan.promoteMember(sql, LEAD, MEM);
    assert.equal(r.ok, true);
    assert.equal(r.role, 'officer', 'acceptance criterion 1');
    assert.equal(r.wallet, MEM);

    const q = sql.matching(ROLE_UPDATE)[0];
    assert.ok(q.values.includes('officer'), 'the new role is PARAMETERISED, never interpolated');
    assert.ok(q.values.includes('member'), 'and the role it must still hold is bound too');
});

test('⛔ the promote statement re-asserts BOTH roles, so a stale read cannot act', async () => {
    const sql = roleSql('leader', MEM, 'member', [
        { match: ROLE_UPDATE, rows: [{ wallet: MEM, role: 'officer' }] },
    ]);
    await clan.promoteMember(sql, LEAD, MEM);
    const q = sql.matching(ROLE_UPDATE)[0].text;
    assert.match(q, /AND\s+role\s*=\s*\?/i,
        'the TARGET\'s role is in the predicate: a target promoted between the read and the write ' +
        'must not be promoted twice');
    assert.match(q, /EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+clan_members\s+c/i,
        '⛔ THE LOAD-BEARING CLAUSE. The read produced the 403, but the read is NOT the authority: ' +
        'a Leader demoted between the two calls must not still be able to promote. Delete this and ' +
        'every authorization decision in the file becomes advisory.');
    assert.match(q, /c\.role\s*=\s*\?/i, 'and the caller\'s required role is bound, not spelled in the text');
});

test('a Leader demotes an Officer to Member', async () => {
    const sql = roleSql('leader', OFF, 'officer', [
        { match: ROLE_UPDATE, rows: [{ wallet: OFF, role: 'member' }] },
    ]);
    const r = await clan.demoteMember(sql, LEAD, OFF);
    assert.equal(r.ok, true, 'acceptance criterion 2');
    assert.equal(r.role, 'member');
    assert.ok(sql.matching(ROLE_UPDATE)[0].values.includes('officer'),
        'demote requires the target to STILL be an officer, in the statement');
});

test('⛔ a non-Leader calling promote or demote is a 403, and writes nothing', async () => {
    for (const callerRole of ['officer', 'member']) {
        for (const op of ['promoteMember', 'demoteMember']) {
            const sql = roleSql(callerRole, MEM, op === 'promoteMember' ? 'member' : 'officer');
            const r = await clan[op](sql, LEAD, MEM);
            assert.equal(r.status, 403, callerRole + '/' + op + ' — acceptance criterion 3');
            assert.equal(r.code, clan.ClanCode.FORBIDDEN);
            assert.equal(r.detail.role, callerRole, 'the refusal names the role that was insufficient');
            assert.equal(sql.matching(ROLE_UPDATE).length, 0, callerRole + '/' + op);
        }
    }
});

test('promoting someone who is already an Officer is a 409, not a silent no-op', async () => {
    const sql = roleSql('leader', OFF, 'officer');
    const r = await clan.promoteMember(sql, LEAD, OFF);
    assert.equal(r.status, 409);
    assert.equal(r.code, clan.ClanCode.TARGET_ROLE);
    assert.equal(r.detail.role, 'officer');
    assert.equal(sql.matching(ROLE_UPDATE).length, 0);
});

test('demoting a plain Member is a 409, and demoting the Leader is refused', async () => {
    const asMember = roleSql('leader', MEM, 'member');
    const a = await clan.demoteMember(asMember, LEAD, MEM);
    assert.equal(a.status, 409);
    assert.equal(a.code, clan.ClanCode.TARGET_ROLE);

    // A second leader row cannot exist, but if one did, demoting it must not be the way
    // a clan loses its leader.
    const asLeader = roleSql('leader', OFF, 'leader');
    const b = await clan.demoteMember(asLeader, LEAD, OFF);
    assert.equal(b.status, 409);
    assert.equal(asLeader.matching(ROLE_UPDATE).length, 0);
});

test('a write that moved zero rows is a RACE, never a success', async () => {
    for (const [op, targetRole] of [['promoteMember', 'member'], ['demoteMember', 'officer']]) {
        const sql = roleSql('leader', MEM, targetRole, [{ match: ROLE_UPDATE, rows: [] }]);
        const r = await clan[op](sql, LEAD, MEM);
        assert.equal(r.ok, false, op);
        assert.equal(r.status, 409, op);
        assert.equal(r.code, clan.ClanCode.RACED, op +
            ': the read said yes and the statement changed nothing — something moved underneath us, ' +
            'and a cheerful 200 would report a role change that never happened');
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. KICK — including the Officer's narrower power (owner ruling)
// ═══════════════════════════════════════════════════════════════════════════

test('a Leader kicks a Member and an Officer', async () => {
    for (const role of ['member', 'officer']) {
        const sql = roleSql('leader', MEM, role, [
            { match: KICK_DELETE, rows: [{ wallet: MEM, role: role }] },
        ]);
        const r = await clan.kickMember(sql, LEAD, MEM);
        assert.equal(r.ok, true, 'acceptance criterion 4: ' + role);
        assert.equal(r.removedRole, role);
        assert.equal(r.by, 'leader');
    }
});

test('an Officer kicks a Member (owner ruling: Officers get kick power)', async () => {
    const sql = roleSql('officer', MEM, 'member', [
        { match: KICK_DELETE, rows: [{ wallet: MEM, role: 'member' }] },
    ]);
    const r = await clan.kickMember(sql, OFF, MEM);
    assert.equal(r.ok, true, 'acceptance criterion 5 / test plan item 4');
    assert.equal(r.by, 'officer');
});

test('⛔ an Officer cannot kick another Officer or the Leader — 403 both times', async () => {
    for (const targetRole of ['officer', 'leader']) {
        const sql = roleSql('officer', MEM, targetRole);
        const r = await clan.kickMember(sql, OFF, MEM);
        assert.equal(r.status, 403, targetRole + ' — acceptance criterion 5 / test plan item 4');
        assert.equal(r.code, clan.ClanCode.FORBIDDEN);
        assert.equal(sql.matching(KICK_DELETE).length, 0, targetRole);
    }
});

test('a Member cannot kick anybody', async () => {
    const sql = roleSql('member', OFF, 'member');
    const r = await clan.kickMember(sql, MEM, OFF);
    assert.equal(r.status, 403);
    assert.equal(r.detail.role, 'member');
    assert.equal(sql.matching(KICK_DELETE).length, 0);
});

test('nobody kicks a Leader', async () => {
    const sql = roleSql('leader', OFF, 'leader');
    const r = await clan.kickMember(sql, LEAD, OFF);
    assert.equal(r.status, 403, 'a Leader\'s exit is succession, not removal');
    assert.equal(sql.matching(KICK_DELETE).length, 0);
});

test('⛔ the Officer\'s narrower power is in the DELETE predicate too', async () => {
    const sql = roleSql('officer', MEM, 'member', [
        { match: KICK_DELETE, rows: [{ wallet: MEM, role: 'member' }] },
    ]);
    await clan.kickMember(sql, OFF, MEM);
    const q = sql.matching(KICK_DELETE)[0];
    assert.match(q.text, /role\s*<>\s*\?/i, 'the Leader can never be the row removed');
    assert.match(q.text, /\(\s*\?\s*::text\s*=\s*\?\s+OR\s+role\s*=\s*\?\s*\)/i,
        'an Officer promoted to Leader — or a Leader demoted to Officer — between the read and the ' +
        'write must not borrow the role it no longer holds. ⛔ AND THE ::text CAST STAYS: this is the ' +
        'one predicate comparing two PARAMETERS rather than a parameter to a column, the driver sends ' +
        'them untyped, and `$n = $m` with both sides unknown is a 42P18 that would 500 every Officer ' +
        'kick in production while this mock noticed nothing.');
    assert.match(q.text, /EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+clan_members\s+c/i,
        'and the caller must still hold the exact role the decision was made on');
    assert.ok(q.values.includes('officer'), 'the caller role is bound as a parameter');
});

test('a kick that removed nothing is a RACE, never { ok: true }', async () => {
    const sql = roleSql('leader', MEM, 'member', [{ match: KICK_DELETE, rows: [] }]);
    const r = await clan.kickMember(sql, LEAD, MEM);
    assert.equal(r.ok, false);
    assert.equal(r.status, 409);
    assert.equal(r.code, clan.ClanCode.RACED);
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. LEADER SUCCESSION
// ═══════════════════════════════════════════════════════════════════════════

test('a Leader leaving with an Officer present hands the clan to that Officer', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: SUCCESSION, rows: [{
            departed_rows: 1, disbanded_rows: 0, new_leader: OFF, successor_prior_role: 'officer',
        }] },
    ]);
    const r = await clan.leaveClan(sql, LEAD);
    assert.equal(r.ok, true, 'acceptance criterion 6 / test plan item 1');
    assert.equal(r.newLeader, OFF);
    assert.equal(r.successionFrom, 'officer');
    assert.equal(r.clanDeleted, false, 'the clan survives — it has a leader again');
});

test('with no Officer, the OLDEST MEMBER is promoted', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: SUCCESSION, rows: [{
            departed_rows: 1, disbanded_rows: 0, new_leader: MEM, successor_prior_role: 'member',
        }] },
    ]);
    const r = await clan.leaveClan(sql, LEAD);
    assert.equal(r.ok, true, 'acceptance criterion 7 / test plan item 2');
    assert.equal(r.newLeader, MEM);
    assert.equal(r.successionFrom, 'member');
});

test('⛔ the successor ORDER BY prefers Officers, then oldest, then a total tiebreak', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: SUCCESSION, rows: [{ departed_rows: 1, disbanded_rows: 0, new_leader: OFF }] },
    ]);
    await clan.leaveClan(sql, LEAD);
    const q = sql.matching(SUCCESSION)[0].text;
    assert.match(q, /ORDER\s+BY\s+\(\s*m\.role\s*=\s*\?\s*\)\s+DESC/i,
        'Officers ahead of Members is the work order\'s two-tier preference, and `= officer` DESC is ' +
        'the honest spelling of it (true sorts first)');
    assert.match(q, /m\.joined_at\s+ASC/i, '"the oldest" is joined_at ASC');
    assert.match(q, /m\.wallet\s+ASC/i,
        'the FINAL tiebreak. Two rows can share joined_at to the microsecond in a scripted or seeded ' +
        'clan, and an unordered pick makes succession irreproducible — the class of bug nobody can ' +
        'ever repro on purpose.');
    assert.match(q, /LIMIT\s+1/i, 'exactly one successor');
});

test('⛔ the successor CTE excludes the leaver BY WALLET (the snapshot rule)', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: SUCCESSION, rows: [{ departed_rows: 1, disbanded_rows: 0, new_leader: OFF }] },
    ]);
    await clan.leaveClan(sql, LEAD);
    const q = sql.matching(SUCCESSION)[0].text;
    assert.match(q, /WHERE\s+m\.wallet\s*<>\s*\?/i,
        '⛔ Sub-statements in a WITH clause share ONE snapshot, so the sibling DELETE of the leader\'s ' +
        'row is INVISIBLE here. Without this predicate a leaving Leader is a candidate to succeed ' +
        'themself, and nothing else in this repo would notice.');
    assert.match(q, /m\.clan_id\s*=\s*clans\.id\s+AND\s+m\.wallet\s*<>\s*\?/i,
        'and the disband test asks "no member OTHER THAN me" for the same reason');
});

test('⛔ succession is ONE statement: promote, remove and disband together', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: SUCCESSION, rows: [{ departed_rows: 1, disbanded_rows: 0, new_leader: OFF }] },
    ]);
    await clan.leaveClan(sql, LEAD);
    const stmts = sql.matching(SUCCESSION);
    assert.equal(stmts.length, 1, 'one call, not three');
    const q = stmts[0].text;
    assert.match(q, /UPDATE\s+clan_members\s+SET\s+role/i, 'the promotion is in it');
    assert.match(q, /DELETE\s+FROM\s+clan_members/i, 'so is the departure');
    assert.match(q, /DELETE\s+FROM\s+clans/i, 'and so is the disband');
    assert.equal(sql.matching(ROLE_UPDATE).length, 1,
        'the role change must NOT be a second round trip: two calls leave a window containing TWO ' +
        'leaders, and a crash between them leaves it there permanently. The HTTP driver has no ' +
        'transaction across calls.');
});

test('a Leader leaving as the sole member deletes the clan', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader', { member_count: 1 })] },
        { match: SUCCESSION, rows: [{
            departed_rows: 1, disbanded_rows: 1, new_leader: null, successor_prior_role: null,
        }] },
    ]);
    const r = await clan.leaveClan(sql, LEAD);
    assert.equal(r.ok, true, 'acceptance criterion 8 / test plan item 3');
    assert.equal(r.clanDeleted, true);
    assert.equal(r.clanRemoved, true, 'the WO-1845 field name is kept so leave.js telemetry still reads');
    assert.equal(r.newLeader, null);
});

test('⛔ a succession that neither promoted nor disbanded is a 500, not an ok', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: SUCCESSION, rows: [{ departed_rows: 1, disbanded_rows: 0, new_leader: null }] },
    ]);
    const r = await clan.leaveClan(sql, LEAD);
    assert.equal(r.ok, false);
    assert.equal(r.status, 500,
        'a clan that was neither handed on nor deleted is exactly the leaderless state this function ' +
        'exists to prevent. Returning ok would leave a player to discover it.');
});

test('a Leader whose row moved underneath the read gets the literal leader_must_transfer', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: SUCCESSION, rows: [{ departed_rows: 0, disbanded_rows: 0, new_leader: null }] },
    ]);
    const r = await clan.leaveClan(sql, LEAD);
    assert.equal(r.status, 409);
    assert.equal(r.code, 'leader_must_transfer',
        'WO-1845 shipped this literal and the client branches on it. WO-1846 retires the RULE (a ' +
        'Leader can leave now) but keeps the STRING as the race label, because it still means ' +
        '"your leave did not happen, look at your role again".');
});

test('a Member leaving is untouched by WO-1846', async () => {
    const sql = recordingSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('member')] },
        { match: /WITH\s+departed\s+AS/i, rows: [{ departed_rows: 1, emptied_rows: 0 }] },
    ]);
    const r = await clan.leaveClan(sql, MEM);
    assert.equal(r.ok, true);
    assert.equal(sql.matching(SUCCESSION).length, 0, 'no succession statement for a non-leader');
    assert.equal(r.newLeader, undefined, 'and no succession fields on the result');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. THE CLAN ACTION BUDGETS
// ═══════════════════════════════════════════════════════════════════════════

test('the budgets are the work order\'s numbers, per action', () => {
    assert.deepEqual(CLAN_RATE_LIMITS, {
        create: 3, join: 10, leave: 5, promote: 20, demote: 20, kick: 20,
    }, 'first pass, tunable — but the code is the authority and this pins what shipped');
    assert.equal(CLAN_RATE_WINDOW_SECONDS, 3600, 'per hour, per the work order');
});

test('touchClanRate counts in ONE atomic upsert, keyed by (wallet, action)', async () => {
    const sql = recordingSql([{ match: RATE_UPSERT, rows: [{ hits: 1, total_hits: 1, retry_after: 3600 }] }]);
    const r = await touchClanRate(sql, LEAD, 'promote');
    assert.equal(r.ok, true);
    assert.equal(r.hits, 1);
    assert.equal(sql.calls.length, 1, 'a read-then-write pair is two requests racing each other');

    const q = sql.calls[0];
    assert.match(q.text, /ON\s+CONFLICT\s*\(\s*wallet\s*,\s*action\s*\)\s+DO\s+UPDATE/i,
        'the COMPOSITE key is what makes the counter per-action: six budgets cannot share one row');
    assert.ok(q.values.includes(LEAD) && q.values.includes('promote'), 'both PARAMETERISED');
    assert.match(q.text, /RETURNING[\s\S]*retry_after/i,
        'the retry hint is computed IN SQL from the row just written — a serverless function\'s ' +
        'Date.now() against a Postgres timestamp is how a hint goes negative in one region');
});

test('⛔ exceeding a budget is a refusal carrying a retry_after hint', async () => {
    const sql = recordingSql([{ match: RATE_UPSERT, rows: [{ hits: 21, total_hits: 21, retry_after: 1234 }] }]);
    const r = await touchClanRate(sql, LEAD, 'promote');
    assert.equal(r.ok, false, 'acceptance criterion 9 / test plan item 5: the 21st promote is refused');
    assert.equal(r.code, AuthCode.CLAN_RATE_LIMITED);
    assert.equal(r.detail.max, 20);
    assert.equal(r.detail.hits, 21);
    assert.equal(r.detail.retryAfterSeconds, 1234);
    assert.equal(r.detail.windowSeconds, 3600);
});

test('the boundary is exact: the last allowed hit passes, the next does not', async () => {
    for (const [action, max] of Object.entries(CLAN_RATE_LIMITS)) {
        const atMax = recordingSql([{ match: RATE_UPSERT, rows: [{ hits: max, retry_after: 10 }] }]);
        assert.equal((await touchClanRate(atMax, LEAD, action)).ok, true, action + ' at ' + max);
        const over = recordingSql([{ match: RATE_UPSERT, rows: [{ hits: max + 1, retry_after: 10 }] }]);
        assert.equal((await touchClanRate(over, LEAD, action)).ok, false, action + ' at ' + (max + 1));
    }
});

test('⛔ a missing clan_rate_limit table FAILS OPEN, loudly', async () => {
    const missing = new Error('relation "clan_rate_limit" does not exist');
    missing.code = '42P01';
    const sql = recordingSql([{ match: RATE_UPSERT, throws: missing }]);
    const warns = [];
    const realWarn = console.warn;
    console.warn = (...a) => warns.push(a.join(' '));
    let r;
    try { r = await touchClanRate(sql, LEAD, 'kick'); } finally { console.warn = realWarn; }

    assert.equal(r.ok, true, 'acceptance criterion 10: rate limiting is abuse control, not ' +
        'authorization. A deploy that lands the code before migration 0032 must keep serving clans.');
    assert.equal(r.degraded, true, 'and it says it degraded rather than pretending it counted');
    assert.ok(warns.some(w => /clan rate table unavailable/i.test(w)),
        'a silent fail-open is indistinguishable from a working limiter — CLAUDE.md §12 forbids it');
});

test('an undeclared action gets the STRICTEST budget, never an unlimited one', async () => {
    const sql = recordingSql([{ match: RATE_UPSERT, rows: [{ hits: 4, retry_after: 10 }] }]);
    const realWarn = console.warn;
    const warns = [];
    console.warn = (...a) => warns.push(a.join(' '));
    let r;
    try { r = await touchClanRate(sql, LEAD, 'disband'); } finally { console.warn = realWarn; }
    assert.equal(r.ok, false, 'a typo in a route must not silently remove that route\'s budget');
    assert.equal(r.detail.max, 3, 'the strictest declared budget');
    assert.ok(warns.some(w => /undeclared action/i.test(w)));
});

test('touchClanRate never throws, whatever the driver does', async () => {
    const sql = () => Promise.reject(new Error('connection reset'));
    sql.calls = [];
    const r = await touchClanRate(sql, LEAD, 'join');
    assert.equal(r.ok, true, 'it is awaited on the success path of an authenticated request: a throw ' +
        'here turns every proven clan call into a 500');
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. THE ROUTES
// ═══════════════════════════════════════════════════════════════════════════

test('POST /api/clan/promote answers the new role', async () => {
    const sql = authedSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: TARGET_READ, rows: [targetRow(MEM, 'member')] },
        { match: ROLE_UPDATE, rows: [{ wallet: MEM, role: 'officer' }] },
    ]);
    const { out } = await callRoute('promote', { sql: sql, body: { playerId: LEAD, wallet: MEM } });
    assert.equal(out.statusCode, 200);
    assert.deepEqual(out.body, { ok: true, wallet: MEM, role: 'officer' },
        'promotion is a GAME ROLE: the body says the role and nothing that reads as an asset or a right');
});

test('POST /api/clan/kick answers the work order\'s literal { ok: true }', async () => {
    const sql = authedSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('officer')] },
        { match: TARGET_READ, rows: [targetRow(MEM, 'member')] },
        { match: KICK_DELETE, rows: [{ wallet: MEM, role: 'member' }] },
    ]);
    const { out } = await callRoute('kick', { sql: sql, body: { playerId: LEAD, wallet: MEM } });
    assert.equal(out.statusCode, 200);
    assert.deepEqual(out.body, { ok: true });
});

test('a non-Leader promote is a 403 through the route too', async () => {
    const sql = authedSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('member')] },
        { match: TARGET_READ, rows: [targetRow(MEM, 'member')] },
    ]);
    const { out } = await callRoute('promote', { sql: sql, body: { playerId: LEAD, wallet: MEM } });
    assert.equal(out.statusCode, 403);
    assert.equal(out.body.error, clan.ClanCode.FORBIDDEN);
    assert.equal(typeof out.body.ref, 'string', 'the correlation ref is on every refusal');
});

test('⛔ a 429 carries Retry-After AND a retry_after field', async () => {
    const sql = authedSql([
        { match: RATE_UPSERT, rows: [{ hits: 99, total_hits: 99, retry_after: 600 }] },
    ]);
    const { out } = await callRoute('promote', { sql: sql, body: { playerId: LEAD, wallet: MEM } });
    assert.equal(out.statusCode, 429, 'acceptance criterion 9');
    assert.equal(out.body.code, AuthCode.CLAN_RATE_LIMITED);
    assert.equal(out.body.retry_after, 600, 'the work order asks for this hint by name');
    assert.equal(out.headers['retry-after'], '600',
        'the header is what a proxy or a well-behaved client already honours; the body field is what ' +
        'the Unity client can read without touching response headers');
    assert.equal(sql.matching(MEMBERSHIP_READ).length, 0,
        '⛔ SPENT BEFORE THE WORK: a throttled request must not still do the reads it was throttled ' +
        'for, and a refused attempt must still cost budget');
});

test('⛔ the budget is keyed to the PROVEN wallet, not the claimed one', async () => {
    const sql = authedSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: TARGET_READ, rows: [targetRow(MEM, 'member')] },
        { match: ROLE_UPDATE, rows: [{ wallet: MEM, role: 'officer' }] },
    ]);
    await callRoute('promote', { sql: sql, body: { playerId: LEAD, wallet: MEM } });
    const spend = sql.matching(RATE_UPSERT);
    assert.equal(spend.length, 1, 'exactly one spend per request');
    assert.ok(spend[0].values.includes(LEAD), 'the CALLER pays');
    assert.ok(!spend[0].values.includes(MEM),
        'keyed to the target, a budget would be a way to burn somebody else\'s by naming them');
    assert.ok(spend[0].values.includes('promote'), 'and it spends THIS route\'s action');
});

test('GET /api/clan/me spends no budget, and the state-changing routes each spend their own', async () => {
    const actions = { promote: 'promote', demote: 'demote', kick: 'kick', leave: 'leave' };
    for (const [route, action] of Object.entries(actions)) {
        const sql = authedSql([{ match: MEMBERSHIP_READ, rows: [] }]);
        await callRoute(route, { sql: sql, body: { playerId: LEAD, wallet: MEM } });
        const spend = sql.matching(RATE_UPSERT);
        assert.equal(spend.length, 1, route + ' must spend exactly one budget');
        assert.ok(spend[0].values.includes(action), route + ' spends the "' + action + '" action');
    }
    // /me is a pure read and is deliberately outside the action list.
    const meSrc = fs.readFileSync(path.join(REPO, 'api', 'clan', 'me.js'), 'utf8');
    assert.match(meSrc, /beginClanRequest\(req,\s*res,\s*'GET'\)/,
        'me.js passes no action: a membership read is not a state change and must not be rationed');
});

test('⛔ `wallet` alone cannot self-kick — playerId is the caller and auth still decides', async () => {
    // body.wallet is the THIRD candidate in the preamble's claimed-identity chain, so a
    // body with ONLY { wallet } names the TARGET as the caller. The session proves LEAD,
    // so the request fails closed instead of doing anything.
    const sql = authedSql([
        { match: MEMBERSHIP_READ, rows: [membershipRow('leader')] },
        { match: TARGET_READ, rows: [targetRow(MEM, 'member')] },
    ]);
    const { out } = await callRoute('kick', { sql: sql, body: { wallet: MEM } });
    assert.equal(out.statusCode, 401,
        'confusing, but never a hole: the identity is whatever the PROOF covers, and this proof ' +
        'covers LEAD, not MEM');
    assert.equal(out.body.code, AuthCode.SESSION_WRONG_WALLET);
    assert.equal(sql.matching(KICK_DELETE).length, 0);
});

test('the unambiguous target aliases work, and the target is never the caller', () => {
    delete require.cache[require.resolve(CLAN_HTTP)];
    const { clanTargetWallet } = require(CLAN_HTTP);
    assert.equal(clanTargetWallet({ playerId: LEAD, wallet: MEM }), MEM, 'the work order\'s shape');
    assert.equal(clanTargetWallet({ playerId: LEAD, target: MEM }), MEM);
    assert.equal(clanTargetWallet({ playerId: LEAD, targetWallet: MEM }), MEM);
    assert.equal(clanTargetWallet({ playerId: LEAD, target: OFF, wallet: MEM }), OFF,
        'the explicit alias wins over the overloaded field');
    assert.equal(clanTargetWallet({}), '', 'and a missing target is empty, which normalises to a 400');
    delete require.cache[require.resolve(CLAN_HTTP)];
});

test('⛔ every new route exports the handler AND keeps bodyParser:false', () => {
    for (const name of ['promote', 'demote', 'kick']) {
        delete require.cache[require.resolve(ROUTES[name])];
        const mod = require(ROUTES[name]);
        assert.equal(typeof mod, 'function', name);
        assert.equal(mod.config && mod.config.api && mod.config.api.bodyParser, false,
            name + ': `module.exports = handler` REPLACES the exports object, so a config assigned ' +
            'BEFORE it is thrown away — exactly how save.js ran with its body parser still active');
        delete require.cache[require.resolve(ROUTES[name])];
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 7. THE MIGRATION — statically, because no lane fires DDL at live Neon
// ═══════════════════════════════════════════════════════════════════════════

test('the clan_rate_limit migration exists and is re-runnable', () => {
    assert.ok(fs.existsSync(MIGRATION_PATH),
        MIGRATION + ' must exist under api/migrations/ — a CREATE that lives only in api/schema.sql ' +
        'is a DESCRIPTION; only api/migrations/ is ever applied');
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');

    assert.match(body, /CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+clan_rate_limit\b/i);
    assert.match(body, /PRIMARY\s+KEY\s*\(\s*wallet\s*,\s*action\s*\)/i,
        'the COMPOSITE key is the one difference from guest_rate_limit, and it is what makes the ' +
        'counter per-action');
    for (const col of ['window_started_at', 'hits', 'total_hits', 'last_seen']) {
        assert.match(body, new RegExp('\\b' + col + '\\b'), col + ' mirrors guest_rate_limit');
    }
    assert.match(body, /CREATE\s+INDEX\s+IF\s+NOT\s+EXISTS\s+clan_rate_limit_last_seen_idx/i);

    assert.ok(!/REFERENCES\s+wallet_identity/i.test(body),
        '⛔ NO FOREIGN KEY, DELIBERATELY. touchWalletIdentity is fail-open, so an FK here would let a ' +
        'missing identity row refuse a clan request THROUGH THE RATE LIMITER — a budget counter must ' +
        'never deny what the auth rail already approved. guest_rate_limit has none either.');
    assert.ok(!/\b(DROP|TRUNCATE)\b/i.test(body), 'nothing destructive, including in the prose');
    assert.ok(!/INSERT\s+INTO/i.test(body), 'no seed rows — a bare INSERT is what makes a file un-re-runnable');
    assert.ok(!/ALTER\s+TABLE/i.test(body), 'a fresh table needs no ALTER, and an ALTER is not idempotent');
});

test('the columns touchClanRate writes are the columns the migration declares', () => {
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');
    const src = fs.readFileSync(WALLET_AUTH, 'utf8');
    const insert = /INSERT INTO clan_rate_limit \(([^)]+)\)/.exec(src);
    assert.ok(insert, 'fixture assumption: touchClanRate still INSERTs by explicit column list');
    for (const col of insert[1].split(',').map(s => s.trim())) {
        assert.match(body, new RegExp('^\\s*' + col + '\\s', 'mi'),
            'touchClanRate writes "' + col + '", which the migration must declare or every clan ' +
            'request degrades fail-open and the limiter silently never runs');
    }
});

test('the one runner accepts the migration as purely additive', async () => {
    const { auditAdditive } = await import('file://' + path.join(REPO, 'tools', 'run-migrations.mjs').replace(/\\/g, '/'));
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');
    assert.deepEqual(auditAdditive(body), [],
        'auditAdditive is the ACTUAL gate a deploy applies, not this file\'s opinion of it');
});

test('the migration sorts after the clan tables it accompanies', () => {
    const files = fs.readdirSync(MIGRATIONS_DIR).filter(f => f.toLowerCase().endsWith('.sql')).sort();
    assert.ok(files.includes(CLAN_MIGRATION), 'fixture assumption: WO-1845 landed its migration');
    assert.ok(files.indexOf(CLAN_MIGRATION) < files.indexOf(MIGRATION),
        'the one runner applies files in FILENAME ORDER. This table has no foreign keys, so the order ' +
        'is not load-bearing the way 0029 -> 0030 is — but a number that sorts backwards is how a ' +
        'ledger ends up applying a file twice.');
});

test('api/schema.sql describes clan_rate_limit and names the applyable migration', () => {
    const schema = fs.readFileSync(SCHEMA_SQL, 'utf8');
    assert.match(schema, /CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+clan_rate_limit\b/i,
        'schema.sql is the readable map of the database and may never disagree with api/migrations/');
    assert.ok(schema.includes(MIGRATION),
        'the block must name its applyable migration by filename, as 0029/0030/0031 do');
});

test('⛔ no lane code fires DDL for this table at runtime', () => {
    for (const f of [CLAN_LIB, CLAN_HTTP, WALLET_AUTH, ...Object.values(ROUTES)]) {
        const src = fs.readFileSync(f, 'utf8');
        const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
        assert.ok(!/CREATE\s+TABLE/i.test(code),
            path.basename(f) + ' contains a CREATE TABLE. Schema changes go through ' +
            'tools/run-migrations.mjs at deploy time, never from a request path.');
    }
});
