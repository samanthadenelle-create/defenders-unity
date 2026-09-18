// =============================================================================
// test/clan-two-wallet-integration.test.js — WO-1848 (clan WO-5), THE WO-1265
// ACCEPTANCE GATE for the whole clan chain (WO-1844 identity, WO-1845 membership,
// WO-1846 roles/succession/rate-limits). WO-1850/1851 are blocked on this file by
// the CLI_LANES_WO_NUMBERS.md banner's own dependency chain.
// -----------------------------------------------------------------------------
// test/clan-membership.test.js and test/clan-roles.test.js already prove every
// individual endpoint in isolation, each re-seeding a canned recordingSql per case.
// That is the right tool for a unit test and the wrong one for an ACCEPTANCE gate:
// it can never catch a bug that only shows up when the SAME clan is created,
// joined, promoted, kicked-from, succeeded and disbanded by the SAME two rows,
// in order, the way an actual pair of players would hit it.
//
// So this file runs ONE stateful in-process "database" — a plain JS map of
// clans / clan_members / clan_rate_limit / wallet_identity / auth_sessions that
// implements the REAL semantics of every SQL statement api/_lib/clan.js and
// api/_lib/wallet-auth.js issue (one-clan-per-wallet, the code-collision retry,
// the succession ORDER BY, the WITH-clause snapshot rule, the per-(wallet,action)
// rate budget) — and drives the REAL route handlers (api/clan/*.js) against it,
// in ONE continuous story, inside ONE test(). The mock is bespoke rather than a
// re-use of clan-roles.test.js's canned `roleSql` precisely because a canned
// answer cannot carry state from one call to the next; an integration test that
// re-seeds is a unit test wearing a longer file name.
//
// ⚠ THIS IS STILL NOT A NETWORK/DATABASE TEST. It is the SQL DRIVER that is
// mocked (the tagged-template `sql` function neon() would otherwise return), the
// same seam every other test in this suite mocks — Neon's own HTTP driver, the
// crypto (tweetnacl/bs58) and Vercel's request/response objects are the only
// three things nothing in this repo's test suite exercises for real, and that
// has been true since test/wallet-identity.test.js. What is new here is that the
// STATE behind that seam persists across every call in the story, which is the
// property a unit test's recordingSql deliberately does not have.
//
//     node --test test/clan-two-wallet-integration.test.js
// =============================================================================

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const ROUTES = {
    create: path.join(REPO, 'api', 'clan', 'create.js'),
    join: path.join(REPO, 'api', 'clan', 'join.js'),
    promote: path.join(REPO, 'api', 'clan', 'promote.js'),
    kick: path.join(REPO, 'api', 'clan', 'kick.js'),
    leave: path.join(REPO, 'api', 'clan', 'leave.js'),
};

// Base58 Solana-shaped addresses (no 0/O/I/l), 44 chars — satisfies WALLET_RE.
const WALLET_A = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU'; // creates the clan
const WALLET_B = '9nQxTpRvE4mLcYbWz3kFhJ6dSgA8uXvNqMePkRtZwYcD'; // joins, becomes Officer, then Leader
const WALLET_C = '4kRzWpYnQ7tLmXvBcHdFgJ2sAeUwNqVxPkMrTyZbCgLd'; // joins, becomes a SECOND Officer, then is kicked

// SESSION_RE is /^[A-Za-z0-9_-]{40,90}$/ — 44-char repeats satisfy it, one per wallet
// so the story can authenticate three different callers through the same rail the
// existing clan suites use (a pre-issued bearer session; see wallet-auth.js §WO-1157).
const SESSION_A = 'a'.repeat(44);
const SESSION_B = 'b'.repeat(44);
const SESSION_C = 'c'.repeat(44);

const LEADER = 'leader';
const MEMBER = 'member';
const OFFICER = 'officer';

// ═══════════════════════════════════════════════════════════════════════════
// THE STATEFUL WORLD — a from-scratch model of the tables this chain touches,
// implementing the ACTUAL predicates api/_lib/clan.js and wallet-auth.js send,
// not a paraphrase of them. Every branch below is read from the source quoted
// in this file's header comment for that statement (re-verified against
// api/_lib/clan.js and api/_lib/wallet-auth.js at HEAD before this file was
// written — CLAUDE.md §11B: nothing here is inferred from the work order prose).
// ═══════════════════════════════════════════════════════════════════════════

function makeWorld() {
    const clans = new Map();          // clanId -> {id, code, name, tag, join_policy, created_at}
    const members = new Map();        // `${clanId}|${wallet}` -> {clan_id, wallet, role, joined_at}
    const walletIdentity = new Map(); // wallet -> {first_seen_at, last_seen_at}
    const sessions = new Map([        // token -> wallet, pre-issued (bypasses nonce/signature,
        [SESSION_A, WALLET_A],        // exactly as clan-membership.test.js's authedSql does)
        [SESSION_B, WALLET_B],
        [SESSION_C, WALLET_C],
    ]);
    const rate = new Map();           // `${wallet}|${action}` -> hits this "hour"
    let clanSeq = 0;
    let clock = 0;
    const now = () => 'T' + (++clock);

    const membersOfClan = (clanId) => [...members.values()].filter((m) => m.clan_id === clanId);
    const memberOf = (wallet) => [...members.values()].find((m) => m.wallet === wallet) || null;

    function uniqueViolation(constraint) {
        const e = new Error(`duplicate key value violates unique constraint "${constraint}"`);
        e.code = '23505';
        e.constraint = constraint;
        return e;
    }

    function handle(text, values) {
        // ── wallet-auth.js: verifySession ────────────────────────────────────
        if (/FROM\s+auth_sessions/i.test(text)) {
            const wallet = sessions.get(values[0]);
            return wallet ? [{ wallet, revoked: false, expired: false }] : [];
        }
        // ── wallet-auth.js: touchWalletIdentity (WO-1844, criterion 8 of this WO) ─
        if (/INSERT\s+INTO\s+wallet_identity/i.test(text)) {
            const wallet = values[0];
            const ts = now();
            const existing = walletIdentity.get(wallet);
            if (existing) {
                existing.last_seen_at = ts;
                return [{ first_seen_at: existing.first_seen_at, last_seen_at: ts }];
            }
            const row = { first_seen_at: ts, last_seen_at: ts };
            walletIdentity.set(wallet, row);
            return [row];
        }
        // ── wallet-auth.js: touchClanRate (WO-1846) ──────────────────────────
        if (/INSERT\s+INTO\s+clan_rate_limit/i.test(text)) {
            const [wallet, action] = values;
            const key = wallet + '|' + action;
            const hits = (rate.get(key) || 0) + 1;
            rate.set(key, hits);
            return [{ hits, total_hits: hits, retry_after: 3600 }];
        }
        // ── clan.js createClan: WITH new_clan AS (...) ───────────────────────
        if (/WITH\s+new_clan\s+AS/i.test(text)) {
            const [code, name, tag, walletForClan, walletForMember, role] = values;
            if (memberOf(walletForClan)) throw uniqueViolation('clan_members_one_clan_per_wallet');
            if ([...clans.values()].some((c) => c.code === code)) throw uniqueViolation('clans_code_key');
            const id = 'clan-' + (++clanSeq);
            const created_at = now();
            clans.set(id, { id, code, name, tag, join_policy: 'invite', created_at });
            const joined_at = now();
            members.set(id + '|' + walletForMember, { clan_id: id, wallet: walletForMember, role, joined_at });
            return [{ id, code, name, tag, join_policy: 'invite', created_at, role, joined_at }];
        }
        // ── clan.js joinClan: WITH target AS (...) ───────────────────────────
        if (/WITH\s+target\s+AS/i.test(text)) {
            const [code, wallet, role] = values;
            const clan = [...clans.values()].find((c) => c.code === code);
            if (!clan) return [];
            if (memberOf(wallet)) throw uniqueViolation('clan_members_one_clan_per_wallet');
            const joined_at = now();
            members.set(clan.id + '|' + wallet, { clan_id: clan.id, wallet, role, joined_at });
            return [{
                id: clan.id, code: clan.code, name: clan.name, tag: clan.tag,
                join_policy: clan.join_policy, created_at: clan.created_at, role, joined_at,
            }];
        }
        // ── clan.js leaveAsLeader: WITH mine AS (...) — succession, WO-1846 ──
        if (/WITH\s+mine\s+AS/i.test(text)) {
            const [wallet, leaderRole, , officerRole] = values;
            const mine = memberOf(wallet);
            if (!mine || mine.role !== leaderRole) {
                return [{ departed_rows: 0, disbanded_rows: 0, new_leader: null, successor_prior_role: null }];
            }
            const clanId = mine.clan_id;
            // ⛔ THE SNAPSHOT RULE: `successor` and the disband NOT EXISTS both read the
            // members table as it stood BEFORE this statement's own DELETE, exactly as
            // Postgres evaluates every sub-statement of one WITH clause against one
            // snapshot (clan.js's header comment, property 3 / WO-1846 note 4).
            const snapshotOthers = membersOfClan(clanId).filter((m) => m.wallet !== wallet);
            const sorted = [...snapshotOthers].sort((a, b) => {
                const aOff = a.role === officerRole ? 0 : 1;
                const bOff = b.role === officerRole ? 0 : 1;
                if (aOff !== bOff) return aOff - bOff;
                if (a.joined_at !== b.joined_at) return a.joined_at < b.joined_at ? -1 : 1;
                return a.wallet < b.wallet ? -1 : 1;
            });
            const successor = sorted[0] || null;
            let newLeader = null;
            let priorRole = null;
            if (successor) {
                members.get(clanId + '|' + successor.wallet).role = leaderRole;
                newLeader = successor.wallet;
                priorRole = successor.role;
            }
            members.delete(clanId + '|' + wallet);
            let disbandedRows = 0;
            if (snapshotOthers.length === 0) {
                clans.delete(clanId);
                disbandedRows = 1;
                newLeader = null;
                priorRole = null;
            }
            return [{ departed_rows: 1, disbanded_rows: disbandedRows, new_leader: newLeader, successor_prior_role: priorRole }];
        }
        // ── clan.js leaveClan (non-leader): WITH departed AS (...) ───────────
        if (/WITH\s+departed\s+AS/i.test(text)) {
            const [wallet, leaderRole] = values;
            const mine = memberOf(wallet);
            if (!mine || mine.role === leaderRole) return [{ departed_rows: 0, emptied_rows: 0 }];
            const clanId = mine.clan_id;
            members.delete(clanId + '|' + wallet);
            const others = membersOfClan(clanId).filter((m) => m.wallet !== wallet);
            let emptiedRows = 0;
            if (others.length === 0) { clans.delete(clanId); emptiedRows = 1; }
            return [{ departed_rows: 1, emptied_rows: emptiedRows }];
        }
        // ── clan.js setMemberRole (promote/demote): plain UPDATE ─────────────
        if (/UPDATE\s+clan_members\s+SET\s+role/i.test(text)) {
            const [toRole, clanId, targetWallet, fromRole, , callerWallet, leaderRole] = values;
            const target = members.get(clanId + '|' + targetWallet);
            const caller = members.get(clanId + '|' + callerWallet);
            if (!target || target.role !== fromRole) return [];
            if (!caller || caller.role !== leaderRole) return [];
            target.role = toRole;
            return [{ wallet: targetWallet, role: toRole }];
        }
        // ── clan.js kickMember: DELETE ... WHERE clan_id = ... ───────────────
        if (/DELETE\s+FROM\s+clan_members\s+WHERE\s+clan_id/i.test(text)) {
            const [clanId, target, leaderRole, callerRole, , memberRole, , callerWallet] = values;
            const targetRec = members.get(clanId + '|' + target);
            const callerRec = members.get(clanId + '|' + callerWallet);
            if (!targetRec || targetRec.role === leaderRole) return [];
            if (!(callerRole === leaderRole || targetRec.role === memberRole)) return [];
            if (!callerRec || callerRec.role !== callerRole) return [];
            const removedRole = targetRec.role;
            members.delete(clanId + '|' + target);
            return [{ wallet: target, role: removedRole }];
        }
        // ── clan.js readMembership ────────────────────────────────────────────
        if (/JOIN\s+clans\s+c\s+ON/i.test(text)) {
            const mine = memberOf(values[0]);
            if (!mine) return [];
            const clan = clans.get(mine.clan_id);
            return [{
                clan_id: clan.id, code: clan.code, name: clan.name, tag: clan.tag,
                join_policy: clan.join_policy, created_at: clan.created_at,
                role: mine.role, joined_at: mine.joined_at, member_count: membersOfClan(clan.id).length,
            }];
        }
        // ── clan.js readMemberInClan ──────────────────────────────────────────
        if (/SELECT\s+wallet\s*,\s*role\s*,\s*joined_at/i.test(text)) {
            const [clanId, wallet] = values;
            const rec = members.get(clanId + '|' + wallet);
            return rec ? [{ wallet: rec.wallet, role: rec.role, joined_at: rec.joined_at }] : [];
        }
        // Anything else (logApiEvent's telemetry insert, etc.) — every route wraps its
        // telemetry call in try/catch, so an empty answer is silently ignored, exactly
        // as a real, unmodelled table would behave under a query this file never sends.
        return [];
    }

    const sql = (strings, ...values) => {
        const text = Array.isArray(strings) ? strings.join(' ? ') : String(strings);
        try {
            return Promise.resolve(handle(text, values));
        } catch (err) {
            return Promise.reject(err);
        }
    };

    return {
        sql,
        clans, members, walletIdentity, rate,
        clanRowCount: () => clans.size,
        memberRowCount: () => members.size,
        memberRole: (wallet) => (memberOf(wallet) ? memberOf(wallet).role : null),
        clanOf: (wallet) => (memberOf(wallet) ? clans.get(memberOf(wallet).clan_id) : null),
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// ROUTE HARNESS — identical shape to clan-membership.test.js / clan-roles.test.js
// (the DRIVER_ID module-cache stub, loadRoute, callRoute), reused rather than
// reinvented so this file exercises the seam ONE way. The one difference: `sql`
// is the SAME world object across every call in the story, never re-created —
// that persistence is the entire point of an integration test.
// ═══════════════════════════════════════════════════════════════════════════

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
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(routePath)];
    delete require.cache[require.resolve(CLAN_HTTP)];
    return handler;
}

async function call(world, name, session, body) {
    const req = { method: 'POST', headers: { 'x-session': session }, query: {} };
    req.body = JSON.stringify(body);
    req.readableEnded = true;
    const prevUrl = process.env.DATABASE_URL;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const handler = loadRoute(name, world.sql);
    const res = fakeRes();
    try {
        await handler(req, res);
    } finally {
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return res.out;
}

// ═══════════════════════════════════════════════════════════════════════════
// THE STORY — one continuous narrative, one running world, three wallets.
// Each assertion cites the WO-1845/1846 acceptance criterion it re-proves END TO
// END (through the real route handler, the real clan.js logic, and a database
// model that actually remembers what happened one line above it) — not merely
// in isolation the way clan-membership.test.js / clan-roles.test.js already do.
// ═══════════════════════════════════════════════════════════════════════════

test('WO-1265 gate: create -> join -> promote -> refused kick -> succession -> disband -> rate-limit, end to end', async () => {
    const world = makeWorld();

    // ── STEP 1 — Wallet A creates a clan (WO-1845 acceptance criterion 1) ───
    const created = await call(world, 'create', SESSION_A, { playerId: WALLET_A, name: 'Ember Wardens', tag: 'EMBR' });
    assert.equal(created.statusCode, 200, 'create must succeed for a wallet in no clan');
    assert.equal(created.body.role, LEADER, 'the creator is seated as Leader');
    const CODE = created.body.code;
    assert.match(CODE, /^[A-HJ-NP-Z2-9]{6}$/, 'the invite code is minted server-side, in the spec alphabet');

    assert.equal(world.clanRowCount(), 1, 'exactly one clans row exists after create');
    assert.equal(world.memberRowCount(), 1, 'exactly one clan_members row exists after create');
    assert.equal(world.memberRole(WALLET_A), LEADER, 'A is Leader in the actual table, not just the response');

    // ── STEP 1a — wallet_identity is touched on the FIRST proven call (WO-1844,
    //     re-proved end to end as WO-1848 criterion 8: "every step's wallet_identity
    //     row updates correctly across the story") ─────────────────────────────
    const aIdentityAfterCreate = world.walletIdentity.get(WALLET_A);
    assert.ok(aIdentityAfterCreate, 'the wallet rail touches wallet_identity on every proven call');
    const aFirstSeen = aIdentityAfterCreate.first_seen_at;
    assert.equal(aIdentityAfterCreate.last_seen_at, aFirstSeen, 'first call: first_seen_at == last_seen_at');

    // ── STEP 1b — exceed the `create` rate limit (3/hour) on wallet A, using the
    //     SAME clan-bound wallet the rest of the story needs (WO-1846 acceptance
    //     criterion 9 / WO-1848 step 7: "exceed the create limit ... verify the
    //     429 + retry_after shape"). A is already in a clan, so calls 2 and 3
    //     answer 409 ALREADY_IN_CLAN — and STILL spend budget, per clan-http.js's
    //     "spent BEFORE the action" rule — before the 4th call is refused. ───────
    const retry1 = await call(world, 'create', SESSION_A, { playerId: WALLET_A, name: 'x', tag: 'X' });
    assert.equal(retry1.statusCode, 409, 'hit 2 of 3: already in a clan, but the budget is still spent');
    const retry2 = await call(world, 'create', SESSION_A, { playerId: WALLET_A, name: 'x', tag: 'X' });
    assert.equal(retry2.statusCode, 409, 'hit 3 of 3: at the boundary, still allowed to attempt');
    const overLimit = await call(world, 'create', SESSION_A, { playerId: WALLET_A, name: 'x', tag: 'X' });
    assert.equal(overLimit.statusCode, 429, 'the 4th `create` this hour is refused by the budget itself');
    assert.equal(overLimit.body.code, 'CLAN_RATE_LIMITED');
    assert.equal(overLimit.body.retry_after, 3600, 'the work order asks for this hint by name');
    assert.equal(overLimit.headers['retry-after'], '3600', 'and the HTTP header carries the same hint');
    assert.equal(world.clanRowCount(), 1, 'the throttled/refused attempts created no second clan');

    // ── STEP 2 — Wallet B joins via A's code (WO-1845 acceptance criterion 2) ─
    const joinedB = await call(world, 'join', SESSION_B, { playerId: WALLET_B, code: CODE });
    assert.equal(joinedB.statusCode, 200);
    assert.equal(joinedB.body.role, MEMBER, 'a joiner is seated as Member');
    assert.equal(world.memberRole(WALLET_B), MEMBER);
    assert.equal(world.clanOf(WALLET_B).id, world.clanOf(WALLET_A).id, 'B landed in A\'s actual clan row');

    // A third wallet, C, also joins — needed for STEP 4 below (an Officer refusing to
    // kick ANOTHER Officer needs a second officer to exist; using a real member rather
    // than a canned fixture is what makes this an end-to-end proof of the rule).
    const joinedC = await call(world, 'join', SESSION_C, { playerId: WALLET_C, code: CODE });
    assert.equal(joinedC.statusCode, 200);
    assert.equal(world.memberRowCount(), 3, 'A, B and C are all now rows in the SAME clan');

    // ── STEP 3 — A promotes B to Officer (WO-1846 acceptance criterion 1) ────
    const promotedB = await call(world, 'promote', SESSION_A, { playerId: WALLET_A, wallet: WALLET_B });
    assert.equal(promotedB.statusCode, 200);
    assert.equal(promotedB.body.role, OFFICER);
    assert.equal(world.memberRole(WALLET_B), OFFICER, 'the role change actually landed in the table');

    // A also promotes C to Officer, purely to stage step 4's refusal with a REAL second
    // officer rather than a hypothetical one.
    const promotedC = await call(world, 'promote', SESSION_A, { playerId: WALLET_A, wallet: WALLET_C });
    assert.equal(promotedC.statusCode, 200);
    assert.equal(world.memberRole(WALLET_C), OFFICER);

    // ── STEP 4 — B (Officer) attempts to kick C (also an Officer) — refused,
    //     per WO-1846's rule that an Officer may remove a MEMBER ONLY
    //     (WO-1846 acceptance criterion 5 / WO-1848 step 4) ───────────────────
    const refusedKick = await call(world, 'kick', SESSION_B, { playerId: WALLET_B, wallet: WALLET_C });
    assert.equal(refusedKick.statusCode, 403, 'an Officer cannot kick another Officer');
    assert.equal(refusedKick.body.error, 'CLAN_FORBIDDEN');
    assert.equal(world.memberRole(WALLET_C), OFFICER, 'C is UNCHANGED — the refusal wrote nothing');
    assert.equal(world.memberRowCount(), 3, 'no row was removed by the refused attempt');

    // Clean-up so the succession step below matches the work order's literal story
    // ("B, the SOLE Officer, becomes Leader"): the Leader removes C, which IS within
    // a Leader's power (Leader may remove any Member or Officer) and is itself an
    // end-to-end re-proof of WO-1846 acceptance criteria 1 and 4.
    const leaderKicksC = await call(world, 'kick', SESSION_A, { playerId: WALLET_A, wallet: WALLET_C });
    assert.equal(leaderKicksC.statusCode, 200, 'a Leader may remove an Officer');
    assert.equal(world.memberRole(WALLET_C), null, 'C is fully gone from clan_members');
    assert.equal(world.memberRowCount(), 2, 'only A (Leader) and B (Officer) remain');

    // ── wallet_identity sanity across steps so far (WO-1848 step 8) ──────────
    const aIdentityMid = world.walletIdentity.get(WALLET_A);
    assert.equal(aIdentityMid.first_seen_at, aFirstSeen, 'first_seen_at NEVER changes after the first touch');
    assert.notEqual(aIdentityMid.last_seen_at, aFirstSeen, 'last_seen_at DOES advance on every later proven call');
    assert.ok(world.walletIdentity.get(WALLET_B), 'B was touched on its own first proven call (join)');
    assert.ok(world.walletIdentity.get(WALLET_C), 'C was touched on its own first proven call too');

    // ── STEP 5 — A (Leader) leaves: succession hands the clan to B, the SOLE
    //     remaining Officer, and A's row is gone (WO-1846 acceptance criterion 6) ─
    const leftA = await call(world, 'leave', SESSION_A, { playerId: WALLET_A });
    assert.equal(leftA.statusCode, 200);
    assert.equal(leftA.body.newLeader, WALLET_B, 'the response names the successor');
    assert.equal(world.memberRole(WALLET_A), null, 'A\'s clan_members row is gone');
    assert.equal(world.memberRole(WALLET_B), LEADER, 'B is now Leader IN THE TABLE, not only in the response');
    assert.equal(world.clanRowCount(), 1, 'the clan survives — it was handed a new Leader, not disbanded');
    assert.equal(world.memberRowCount(), 1, 'only B remains');

    // ── STEP 6 — B, now sole member and Leader, leaves too: the clan is deleted
    //     ENTIRELY, no orphan rows (WO-1846 acceptance criterion 8) ───────────
    const leftB = await call(world, 'leave', SESSION_B, { playerId: WALLET_B });
    assert.equal(leftB.statusCode, 200);
    assert.equal(leftB.body.clanDeleted, true, 'the response says the clan was disbanded');
    assert.equal(world.clanRowCount(), 0, 'no orphan row in clans');
    assert.equal(world.memberRowCount(), 0, 'no orphan row in clan_members — for ANY wallet, not only B\'s');
    assert.equal(world.clanOf(WALLET_A), null);
    assert.equal(world.clanOf(WALLET_B), null);
    assert.equal(world.clanOf(WALLET_C), null);
    // clan_messages / clan_reports / clan_rate_limit never had a row keyed to this
    // clan in the first place (this story never touched chat or reporting, and the
    // rate limiter is keyed by WALLET+ACTION, never by clan id) — so "no orphan rows
    // in any of clans/clan_members/clan_messages/clan_reports/clan_rate_limit" reduces
    // to the two checked above for those two tables, plus the structural fact that
    // neither of the other three tables is ever addressed by a clan id at all.

    // ── wallet_identity, final check across the WHOLE story (WO-1848 step 8) ──
    for (const w of [WALLET_A, WALLET_B, WALLET_C]) {
        const row = world.walletIdentity.get(w);
        assert.ok(row, w + ' has a wallet_identity row by the end of the story');
        assert.ok(row.first_seen_at, w + ' has a first_seen_at');
        assert.ok(row.last_seen_at, w + ' has a last_seen_at');
    }
    assert.equal(world.walletIdentity.get(WALLET_A).first_seen_at, aFirstSeen,
        'A\'s first_seen_at survived every later call in the story unchanged, exactly as ' +
        'touchWalletIdentity\'s ON CONFLICT clause promises (it names ONLY last_seen_at)');

    // ── A clean END STATE: a wallet that just left a now-deleted clan can create a
    //     NEW one — the one-clan-per-wallet index must not have wedged on the old row.
    const secondLife = await call(world, 'create', SESSION_B, { playerId: WALLET_B, name: 'Second Chances', tag: 'AGN2' });
    assert.equal(secondLife.statusCode, 200, 'B is free to found a new clan once the old one is gone');
    assert.equal(world.clanRowCount(), 1);
});
