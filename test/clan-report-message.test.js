// =============================================================================
// test/clan-report-message.test.js — WO-1847 (clan step 4).
// -----------------------------------------------------------------------------
// POST /api/clan/report-message plus reportMessage(), asserted at RUNTIME against a
// recording tagged-template mock — zero network, zero database, zero Unity. The mock
// and the route harness are borrowed VERBATIM from test/clan-membership.test.js
// (WO-1845), which borrowed them from test/wallet-identity.test.js (WO-1844), so the
// clan lane exercises these seams ONE way rather than inventing a third.
//
// WHAT THIS FILE IS ACTUALLY GUARDING, beyond the acceptance criterion:
//
//   1. THE MEMBERSHIP CHECK AND THE WRITE ARE ONE STATEMENT. The INSERT's rows come
//      from a SELECT over clan_members, so a non-member inserts zero rows. A separate
//      "am I a member" SELECT followed by an INSERT is two answers to one question,
//      with a leave landing in the gap. This file fails if someone splits them.
//   2. ZERO ROWS IS A 403, NOT A 200. An insert that wrote nothing must never be
//      reported as a recorded report — the whole point of the table is that a report
//      landed somewhere.
//   3. THE REFUSAL IS AMBIGUOUS ON PURPOSE. "Not your clan" and "no such clan" answer
//      the same code, so the endpoint is not a clan-existence oracle for any
//      authenticated wallet.
//   4. A MALFORMED clanId IS A 400, NOT A 500. Postgres raises 22P02 on a bad UUID
//      cast; unvalidated, that surfaces as an opaque server error.
//   5. THE TABLE IS WRITE-ONLY. Nothing in the API reads clan_reports in this ticket —
//      the admin review surface is deferred, and a lane "finishing" it would ship an
//      unreviewed moderation read.
//
// Plus the deploy-order oracles the 0030 lane established: the table this code writes
// is created by a file under api/migrations/ (the only thing a deploy applies), that
// file passes the one runner's additive audit, and api/schema.sql describes it and
// names it.
//
//     node --test test/clan-report-message.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

const REPO = path.resolve(__dirname, '..');
const CLAN_LIB = path.join(REPO, 'api', '_lib', 'clan.js');
const CLAN_HTTP = path.join(REPO, 'api', '_lib', 'clan-http.js');
const ROUTE = path.join(REPO, 'api', 'clan', 'report-message.js');
const MIGRATIONS_DIR = path.join(REPO, 'api', 'migrations');
const MIGRATION = '20260917_0031_clan_reports.sql';
const MIGRATION_PATH = path.join(MIGRATIONS_DIR, MIGRATION);
const CLAN_MIGRATION = '20260917_0030_clan_tables.sql';
const IDENTITY_MIGRATION = '20260917_0029_wallet_identity.sql';
const SCHEMA_SQL = path.join(REPO, 'api', 'schema.sql');

const clan = require(CLAN_LIB);
const { AuthCode } = require(path.join(REPO, 'api', '_lib', 'wallet-auth.js'));

// A real base58 Solana address shape — 44 chars, no 0/O/I/l — so WALLET_RE accepts it.
const WALLET = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
// SESSION_RE is /^[A-Za-z0-9_-]{40,90}$/.
const SESSION = 'a'.repeat(44);
const CLAN_ID = '3f6b1c2e-4a5d-4f7b-9c8e-0d1a2b3c4d5e';
const REPORT_ID = '8c7d6e5f-1a2b-4c3d-9e8f-7a6b5c4d3e2f';
// A plausible Cherry message id: opaque, external, and not this schema's business.
const MESSAGE_ID = 'cherry_msg_01J9ZXQ4T7B3K5N8M2P6R4V1W0';

// ═══════════════════════════════════════════════════════════════════════════
// HARNESS (mock + route loader: verbatim from test/clan-membership.test.js)
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

/** A Postgres foreign-key violation onto wallet_identity. */
function fkErr() {
    const e = new Error('insert or update on table "clan_reports" violates foreign key constraint "clan_reports_reporter_wallet_fkey"');
    e.code = '23503';
    e.constraint = 'clan_reports_reporter_wallet_fkey';
    e.detail = 'Key (reporter_wallet) is not present in table "wallet_identity".';
    return e;
}

// ⚠ MATCH ON A TOKEN UNIQUE TO THE STATEMENT. The report statement reads clan_members
// as the INSERT's source, so a matcher spelled `clan_members` would also catch any
// membership read a future preamble adds and answer it with a report row.
const REPORT_INSERT = /INSERT\s+INTO\s+clan_reports/i;

/** The row the report CTE returns. */
function reportRow() {
    return { id: REPORT_ID, clan_id: CLAN_ID, reported_at: '2026-09-17T00:00:00Z' };
}

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

function loadRoute(sqlFn) {
    const realDriver = require.cache[DRIVER_ID];
    require.cache[DRIVER_ID] = new Module(DRIVER_ID, null);
    require.cache[DRIVER_ID].filename = DRIVER_ID;
    require.cache[DRIVER_ID].loaded = true;
    require.cache[DRIVER_ID].exports = { neon() { return sqlFn; } };
    delete require.cache[require.resolve(CLAN_HTTP)];
    delete require.cache[require.resolve(ROUTE)];
    const handler = require(ROUTE);
    // Restore immediately — the loaded module holds the stub through its closure, and a
    // leaked stub would poison every later test file in the same process.
    if (realDriver) require.cache[DRIVER_ID] = realDriver;
    else delete require.cache[DRIVER_ID];
    delete require.cache[require.resolve(ROUTE)];
    delete require.cache[require.resolve(CLAN_HTTP)];
    return handler;
}

async function callRoute(opts = {}) {
    const method = opts.method || 'POST';
    const sqlFn = opts.sql || recordingSql();
    const headers = Object.assign({ 'x-session': SESSION }, opts.headers || {});
    const req = { method: method, headers: headers, query: opts.query || {} };
    if (method === 'POST' && opts.body !== undefined) {
        req.body = typeof opts.body === 'string' ? opts.body : JSON.stringify(opts.body);
        req.readableEnded = true;
    }
    const prevUrl = process.env.DATABASE_URL;
    process.env.DATABASE_URL = 'postgres://u:p@127.0.0.1:1/never';
    const handler = loadRoute(sqlFn);
    const res = fakeRes();
    try {
        await handler(req, res);
    } finally {
        if (prevUrl === undefined) delete process.env.DATABASE_URL; else process.env.DATABASE_URL = prevUrl;
    }
    return { out: res.out, sql: sqlFn };
}

/** A route-level sql that authenticates WALLET via the session rail, plus report routes. */
function authedSql(route = []) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET, revoked: false, expired: false }] },
        { match: /INSERT\s+INTO\s+wallet_identity/i, rows: [{ first_seen_at: 'T0', last_seen_at: 'T0' }] },
        ...route,
    ]);
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. VALIDATION — a bad argument is a 400 with a stable code, never a 500
// ═══════════════════════════════════════════════════════════════════════════

test('normalizeClanId accepts a UUID and refuses everything Postgres would 22P02 on', () => {
    assert.deepEqual(clan.normalizeClanId(CLAN_ID), { ok: true, value: CLAN_ID });
    assert.deepEqual(clan.normalizeClanId('  ' + CLAN_ID + '  '), { ok: true, value: CLAN_ID },
        'trimmed, because a client that pads an id should not be told the clan does not exist');
    assert.deepEqual(clan.normalizeClanId(CLAN_ID.toUpperCase()), { ok: true, value: CLAN_ID.toUpperCase() },
        'UUIDs are case-insensitive to Postgres, so the regex must be too');
    for (const bad of [null, undefined, '', '   ', 'not-a-uuid', CLAN_ID + 'x', CLAN_ID.slice(0, -1),
        '3f6b1c2e4a5d4f7b9c8e0d1a2b3c4d5e', 'zzzzzzzz-4a5d-4f7b-9c8e-0d1a2b3c4d5e',
        "' OR 1=1 --"]) {
        const r = clan.normalizeClanId(bad);
        assert.equal(r.ok, false, 'must refuse: ' + String(bad));
        assert.equal(r.code, clan.ClanCode.BAD_CLAN_ID);
    }
});

test('normalizeMessageId bounds an OPAQUE external id without guessing its format', () => {
    assert.deepEqual(clan.normalizeMessageId(MESSAGE_ID), { ok: true, value: MESSAGE_ID });
    assert.deepEqual(clan.normalizeMessageId('  ' + MESSAGE_ID + ' '), { ok: true, value: MESSAGE_ID });
    for (const bad of [null, undefined, '', '   ', '\t\n ']) {
        const r = clan.normalizeMessageId(bad);
        assert.equal(r.ok, false, 'must refuse blank: ' + JSON.stringify(bad));
        assert.equal(r.code, clan.ClanCode.BAD_MESSAGE_ID);
    }
    const over = 'x'.repeat(clan.MESSAGE_ID_MAX + 1);
    assert.equal(clan.normalizeMessageId(over).code, clan.ClanCode.BAD_MESSAGE_ID,
        'an unbounded TEXT column reachable from an authenticated endpoint is a cheap way to ' +
        'write megabytes per request');
    assert.equal(clan.normalizeMessageId('x'.repeat(clan.MESSAGE_ID_MAX)).ok, true,
        'the ceiling itself is accepted — an off-by-one here refuses a legitimate id');
    // Deliberately NOT pattern-matched: the id belongs to Cherry's schema, not this one.
    assert.equal(clan.normalizeMessageId('a b/c:d.e-f_g#1').ok, true,
        'a regex guessing at Cherry id format would refuse valid reports the day Cherry changed it');
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE WRITE — one statement that checks membership AND inserts
// ═══════════════════════════════════════════════════════════════════════════

test('a member reporting a message writes ONE statement carrying the membership check', async () => {
    const sql = recordingSql([{ match: REPORT_INSERT, rows: [reportRow()] }]);
    const r = await clan.reportMessage(sql, WALLET, CLAN_ID, MESSAGE_ID);

    assert.equal(r.ok, true);
    assert.equal(r.reportId, REPORT_ID);
    assert.equal(r.clanId, CLAN_ID);

    assert.equal(sql.calls.length, 1,
        'ONE call. A membership SELECT followed by an INSERT is two answers to one question, ' +
        'with a leave landing in the gap.');
    const text = sql.calls[0].text;
    assert.match(text, /INSERT\s+INTO\s+clan_reports/i);
    assert.match(text, /FROM\s+clan_members\s+m/i,
        'the INSERT\'s rows come FROM clan_members — that is what makes a non-member insert zero rows');
    assert.match(text, /m\.wallet\s*=/i, 'and it is scoped to the caller\'s own membership row');
    assert.match(text, /RETURNING/i, 'without RETURNING there is no way to tell a write from a no-op');
    // The caller's wallet is bound twice (reporter and membership scope), the id once each.
    assert.deepEqual(sql.calls[0].values, [WALLET, MESSAGE_ID, CLAN_ID, WALLET]);
});

test('⛔ a non-member gets 403 and the SAME code as a clan that does not exist', async () => {
    // Zero rows is what BOTH facts look like: the SELECT over clan_members matched nothing.
    const sql = recordingSql([{ match: REPORT_INSERT, rows: [] }]);
    const r = await clan.reportMessage(sql, WALLET, CLAN_ID, MESSAGE_ID);

    assert.equal(r.ok, false, 'an insert that wrote nothing must NEVER be reported as a recorded report');
    assert.equal(r.status, 403);
    assert.equal(r.code, clan.ClanCode.REPORT_NOT_MEMBER);
    assert.equal(r.code, 'CLAN_REPORT_NOT_MEMBER');
    // The property, stated as a property: nothing in the refusal distinguishes the two
    // cases, so the endpoint cannot be used to enumerate clans.
    assert.equal(r.detail, undefined,
        'a detail naming which fact it was would turn /report-message into a clan-existence oracle');
});

test('a missing wallet_identity row is a DEPLOY fault: 500 IDENTITY_MISSING, not a 400', async () => {
    const sql = recordingSql([{ match: REPORT_INSERT, throws: fkErr() }]);
    const r = await clan.reportMessage(sql, WALLET, CLAN_ID, MESSAGE_ID);
    assert.equal(r.ok, false);
    assert.equal(r.status, 500, 'a migration that is not applied is never the player\'s fault');
    assert.equal(r.code, clan.ClanCode.IDENTITY_MISSING);
});

test('validation refuses BEFORE any query — a bad argument never touches the database', async () => {
    for (const [clanId, messageId, code] of [
        ['not-a-uuid', MESSAGE_ID, clan.ClanCode.BAD_CLAN_ID],
        [CLAN_ID, '', clan.ClanCode.BAD_MESSAGE_ID],
        [CLAN_ID, 'x'.repeat(clan.MESSAGE_ID_MAX + 1), clan.ClanCode.BAD_MESSAGE_ID],
    ]) {
        const sql = recordingSql([{ match: REPORT_INSERT, rows: [reportRow()] }]);
        const r = await clan.reportMessage(sql, WALLET, clanId, messageId);
        assert.equal(r.ok, false);
        assert.equal(r.status, 400);
        assert.equal(r.code, code);
        assert.equal(sql.calls.length, 0, 'refused before the query, so a 22P02 can never be reached');
    }
});

test('an unexpected driver error PROPAGATES — it is never swallowed into a cheerful refusal', async () => {
    const boom = new Error('connection reset');
    const sql = recordingSql([{ match: REPORT_INSERT, throws: boom }]);
    await assert.rejects(() => clan.reportMessage(sql, WALLET, CLAN_ID, MESSAGE_ID), /connection reset/,
        'the route turns this into a 500 with a ref; classifying it here would hide a real outage');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. THE ROUTE — same auth shape as every other clan route
// ═══════════════════════════════════════════════════════════════════════════

test('POST /api/clan/report-message returns { ok:true } for a member', async () => {
    const sql = authedSql([{ match: REPORT_INSERT, rows: [reportRow()] }]);
    const { out } = await callRoute({ body: { playerId: WALLET, clanId: CLAN_ID, messageId: MESSAGE_ID }, sql });
    assert.equal(out.statusCode, 200, 'body was: ' + JSON.stringify(out.body));
    assert.deepEqual(out.body, { ok: true });
});

test('the route answers the library\'s refusals with the clanFail shape', async () => {
    const notMember = authedSql([{ match: REPORT_INSERT, rows: [] }]);
    const a = await callRoute({ body: { playerId: WALLET, clanId: CLAN_ID, messageId: MESSAGE_ID }, sql: notMember });
    assert.equal(a.out.statusCode, 403);
    assert.equal(a.out.body.ok, false);
    assert.equal(a.out.body.error, 'CLAN_REPORT_NOT_MEMBER');
    assert.equal(a.out.body.code, 'CLAN_REPORT_NOT_MEMBER');
    assert.ok(a.out.body.ref, 'every refusal carries a ref, as every other route does');

    const bad = authedSql([{ match: REPORT_INSERT, rows: [reportRow()] }]);
    const b = await callRoute({ body: { playerId: WALLET, clanId: 'nope', messageId: MESSAGE_ID }, sql: bad });
    assert.equal(b.out.statusCode, 400);
    assert.equal(b.out.body.code, 'CLAN_BAD_CLAN_ID');

    const blank = authedSql([{ match: REPORT_INSERT, rows: [reportRow()] }]);
    const c = await callRoute({ body: { playerId: WALLET, clanId: CLAN_ID, messageId: '  ' }, sql: blank });
    assert.equal(c.out.statusCode, 400);
    assert.equal(c.out.body.code, 'CLAN_BAD_MESSAGE_ID');
});

test('⛔ the route rejects an unauthenticated request, with the shared preamble\'s shape', async () => {
    // No session row: authenticate() refuses, and the refusal comes from the ONE preamble
    // every clan route runs — which is the property a copied preamble could not hold.
    const sql = recordingSql([{ match: /FROM\s+auth_sessions/i, rows: [] }]);
    const { out } = await callRoute({ body: { playerId: WALLET, clanId: CLAN_ID, messageId: MESSAGE_ID }, sql });
    assert.equal(out.statusCode, 401);
    assert.equal(out.body.ok, false);
    assert.ok(out.body.code, 'the machine code is always present');
    assert.ok(out.body.ref);
    assert.equal(sql.matching(REPORT_INSERT).length, 0, 'nothing was written for an unauthenticated caller');
});

test('⛔ a caller with no identity at all is refused before the database is even reached', async () => {
    const sql = recordingSql();
    const { out } = await callRoute({ body: { clanId: CLAN_ID, messageId: MESSAGE_ID }, headers: {} });
    assert.equal(out.statusCode, 400);
    assert.equal(out.body.code, AuthCode.PLAYER_ID_MISSING);
    assert.equal(sql.calls.length, 0);
});

test('⛔ only POST is accepted', async () => {
    for (const method of ['GET', 'PUT', 'DELETE', 'PATCH']) {
        const { out } = await callRoute({ method, body: undefined });
        assert.equal(out.statusCode, 400, method + ' must be refused');
        assert.equal(out.body.code, AuthCode.METHOD_NOT_ALLOWED);
    }
});

test('⛔ the route exports the handler AND keeps its bodyParser:false config', () => {
    delete require.cache[require.resolve(ROUTE)];
    const mod = require(ROUTE);
    assert.equal(typeof mod, 'function', 'the handler must be the export');
    assert.equal(mod.config && mod.config.api && mod.config.api.bodyParser, false,
        '`module.exports = handler` REPLACES the exports object, so a config assigned BEFORE it is ' +
        'thrown away — exactly how save.js ran with its body parser still active');
    delete require.cache[require.resolve(ROUTE)];
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE MIGRATION — because a table nothing creates is a 42P01 per request
// ═══════════════════════════════════════════════════════════════════════════

test('the migration exists, is re-runnable, and declares the table and its index', () => {
    assert.ok(fs.existsSync(MIGRATION_PATH),
        `${MIGRATION} must exist under api/migrations/ — a CREATE that lives only in api/schema.sql ` +
        'is a DESCRIPTION; only api/migrations/ is ever applied');
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');

    assert.match(body, /CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+clan_reports\b/i,
        'IF NOT EXISTS so the file survives a ledger re-apply');
    assert.match(body, /CREATE\s+INDEX\s+IF\s+NOT\s+EXISTS\s+clan_reports_clan_idx/i);
    assert.match(body, /ON\s+clan_reports\s*\(\s*clan_id\s*,\s*reported_at\s+DESC\s*\)/i,
        'the index a review tool will read by: newest reports for one clan');

    for (const col of ['reporter_wallet', 'message_id', 'clan_id', 'reported_at']) {
        assert.match(body, new RegExp('\\b' + col + '\\b'), col + ' must be declared');
    }
    assert.match(body, /reporter_wallet\s+TEXT\s+NOT\s+NULL\s+REFERENCES\s+wallet_identity\s*\(\s*wallet\s*\)/i,
        'the reporter keys onto the table WO-1844 created — this is the deploy-order fact');
    assert.match(body, /clan_id\s+UUID\s+NOT\s+NULL\s+REFERENCES\s+clans\s*\(\s*id\s*\)\s+ON\s+DELETE\s+CASCADE/i,
        'reports go when their clan goes, matching clan_members and clan_messages in 0030');
    assert.match(body, /gen_random_uuid\(\)/,
        'a PostgreSQL 13+ builtin, already the UUID primary-key default of clans (0030) on this instance');

    // ⛔ THE LOAD-BEARING NEGATIVE. message_id is TEXT and must never become a foreign key:
    // Cherry owns message persistence, so no clan_messages row exists for an embedded
    // message and a key here would point at a table that cannot hold the id.
    assert.match(body, /message_id\s+TEXT\s+NOT\s+NULL\s*,/i,
        'message_id is TEXT and NOT NULL — the id is an opaque external string');
    assert.ok(!/message_id[^,]*REFERENCES/i.test(body),
        'message_id must NOT be a foreign key: the game backend does not own Cherry\'s message ids ' +
        'and cannot validate them against a table it does not have');

    assert.ok(!/\b(DROP|TRUNCATE)\b/i.test(body),
        'nothing destructive, including in the prose — the one runner refuses the whole run over it');
    assert.ok(!/INSERT\s+INTO/i.test(body),
        'no seed rows: a bare INSERT is the 23505 that makes a file un-re-runnable (see 0004)');
    assert.ok(!/ALTER\s+TABLE/i.test(body),
        'declared inline in the CREATE body, which is what keeps it idempotent');
});

test('⛔ the migration sorts AFTER both migrations it depends on', () => {
    const files = fs.readdirSync(MIGRATIONS_DIR).filter(f => f.toLowerCase().endsWith('.sql')).sort();
    assert.ok(files.includes(IDENTITY_MIGRATION), 'fixture assumption: WO-1844 landed its migration');
    assert.ok(files.includes(CLAN_MIGRATION), 'fixture assumption: WO-1845 landed its migration');
    assert.ok(files.indexOf(IDENTITY_MIGRATION) < files.indexOf(MIGRATION),
        'the one runner derives its list in FILENAME ORDER. Applied before 0029, reporter_wallet ' +
        'fails at migration time with 42P01 — and the runner stops the whole run there.');
    assert.ok(files.indexOf(CLAN_MIGRATION) < files.indexOf(MIGRATION),
        'and before 0030, clan_id has no clans table to reference');
});

test('the one runner accepts this migration as purely additive', async () => {
    const { auditAdditive } = await import('file://' + path.join(REPO, 'tools', 'run-migrations.mjs').replace(/\\/g, '/'));
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');
    assert.deepEqual(auditAdditive(body), [],
        'auditAdditive is the ACTUAL gate a deploy applies (not this file\'s opinion of it). ' +
        'ON DELETE CASCADE inside a CREATE TABLE is fine — the destructive check anchors ' +
        'DELETE/TRUNCATE at statement start.');
});

test('api/schema.sql describes clan_reports and names the applyable migration', () => {
    const schema = fs.readFileSync(SCHEMA_SQL, 'utf8');
    assert.match(schema, /CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+clan_reports\b/i,
        'schema.sql is the readable map of the database and may never disagree with api/migrations/');
    assert.ok(schema.includes(MIGRATION),
        'the block must name its applyable migration by filename, as 0024/0027/0028/0029/0030 do');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. NON-SCOPE — the things this ticket deliberately did NOT build
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ clan_reports is WRITE-ONLY: nothing in the API reads it', () => {
    // The admin review surface is deferred with the admin clan-health ticket. A lane
    // "finishing" this table by adding a read would ship an unreviewed moderation read.
    const apiDir = path.join(REPO, 'api');
    const offenders = [];
    const walk = (dir) => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) { walk(full); continue; }
            if (!entry.name.endsWith('.js')) continue;
            const src = fs.readFileSync(full, 'utf8');
            const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
            if (!/clan_reports/.test(code)) continue;
            if (/(SELECT[\s\S]{0,200}FROM\s+clan_reports|FROM\s+clan_reports)/i.test(code)) {
                offenders.push(path.relative(REPO, full));
            }
        }
    };
    walk(apiDir);
    assert.deepEqual(offenders, [],
        'clan_reports is written by api/clan/report-message.js and read by NOTHING in this ticket');
});

test('⛔ still nothing writes clan_messages — Cherry owns message persistence', () => {
    const src = fs.readFileSync(ROUTE, 'utf8');
    const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
    assert.ok(!/clan_messages/.test(code),
        'the game backend stores no chat messages. clan_messages (declared by 0030) stays unwritten: ' +
        'Cherry owns delivery and persistence, and this ticket adds no message endpoints.');
    assert.ok(!fs.existsSync(path.join(REPO, 'api', 'clan', 'chat.js')), 'no chat endpoint in this ticket');
    assert.ok(!fs.existsSync(path.join(REPO, 'api', 'clan', 'messages.js')), 'no chat endpoint in this ticket');
});

test('⛔ no app-trusted embed-token path exists on the server', () => {
    // WO-1847 ships Cherry's WALLET-ONLY auth mode: the embedded iframe handles wallet
    // connection and signing entirely itself, so the game's backend never mints an embed
    // token and the game's wallet adapter is never asked to sign for the embed. This test
    // is the acceptance criterion, asserted rather than eyeballed.
    const files = [ROUTE, CLAN_LIB, CLAN_HTTP];
    for (const f of files) {
        const src = fs.readFileSync(f, 'utf8');
        for (const banned of ['signChallengeHandler', 'embedToken', 'onSignChallenge', 'cherryToken']) {
            assert.ok(!src.includes(banned),
                path.basename(f) + ' names ' + banned + '. Wallet-only mode is this ticket\'s default ' +
                'and the app-trusted signature bridge is its explicit non-scope — do not build it ' +
                'unless a later ticket asks for it.');
        }
    }
});
