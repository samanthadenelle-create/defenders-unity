// =============================================================================
// test/wallet-identity.test.js — WO-1844 (clan step 1).
// -----------------------------------------------------------------------------
// touchWalletIdentity() records that a PROVEN wallet was seen. It decides nothing,
// gates nothing, and therefore must never be able to break a request. Three
// properties carry the ticket, and all three are asserted at RUNTIME against a
// recording tagged-template mock — zero network, zero database, zero Unity:
//
//   1. INSERT writes first_seen_at AND last_seen_at; the conflict path writes
//      ONLY last_seen_at. If a future edit lets the conflict path touch
//      first_seen_at, the one fact this table exists for is destroyed silently
//      and no other test in the repo would notice.
//   2. FAIL-OPEN. A throwing sql (the real case: migration 0029 not yet applied
//      to prod) resolves to ok:true + degraded:true and warns. It never rejects.
//   3. WIRING. A wallet-rail SUCCESS touches the row. An auth FAILURE does not,
//      and the guest rail does not — a self-asserted guest id must never land in
//      an identity table.
//
// Plus the deploy-order oracle: the columns this file INSERTs are created by a
// file under api/migrations/, which is the only thing a deploy applies.
//
//     node --test test/wallet-identity.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const REPO = path.resolve(__dirname, '..');
const WALLET_AUTH_PATH = path.join(REPO, 'api', '_lib', 'wallet-auth.js');
const MIGRATION = '20260917_0029_wallet_identity.sql';
const MIGRATION_PATH = path.join(REPO, 'api', 'migrations', MIGRATION);
const SCHEMA_SQL = path.join(REPO, 'api', 'schema.sql');

const wa = require(WALLET_AUTH_PATH);

// A real base58 Solana address shape — 44 chars, no 0/O/I/l — so WALLET_RE accepts it.
const WALLET = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';
// SESSION_RE is /^[A-Za-z0-9_-]{40,90}$/.
const SESSION = 'a'.repeat(44);
const GUEST = 'guest-local-' + 'b'.repeat(64);

// ── A recording tagged-template client ───────────────────────────────────────
// neon(...) is called as sql`text ${v} more`, i.e. sql(strings, ...values). The
// mock records the reassembled text and the interpolated values, and answers from
// `route`: the FIRST matching entry wins, so a test can make one query throw while
// the rest behave.
function recordingSql(route = []) {
    const calls = [];
    const fn = (strings, ...values) => {
        const text = Array.isArray(strings) ? strings.join(' ? ') : String(strings);
        calls.push({ text: text, values: values });
        for (const r of route) {
            if (r.match.test(text)) {
                if (r.throws) return Promise.reject(new Error(r.throws));
                return Promise.resolve(r.rows || []);
            }
        }
        return Promise.resolve([]);
    };
    fn.calls = calls;
    fn.matching = re => calls.filter(c => re.test(c.text));
    return fn;
}

const IDENTITY_WRITE = /INSERT\s+INTO\s+wallet_identity/i;

function captureWarnings(run) {
    const original = console.warn;
    const lines = [];
    console.warn = (...args) => lines.push(args.map(String).join(' '));
    return Promise.resolve()
        .then(run)
        .then(value => ({ value: value, lines: lines }), err => { console.warn = original; throw err; })
        .then(out => { console.warn = original; return out; });
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE UPSERT SHAPE — and the clause that must never grow
// ═══════════════════════════════════════════════════════════════════════════

test('touchWalletIdentity is exported and upserts on the wallet primary key', async () => {
    assert.equal(typeof wa.touchWalletIdentity, 'function',
        'the function must be exported — later steps in this chain read this table');

    const sql = recordingSql([{
        match: IDENTITY_WRITE,
        rows: [{ first_seen_at: '2026-09-17T00:00:00Z', last_seen_at: '2026-09-17T00:00:00Z' }],
    }]);
    const r = await wa.touchWalletIdentity(sql, WALLET);

    assert.equal(r.ok, true);
    assert.equal(r.degraded, undefined, 'a healthy write is not degraded');
    assert.equal(r.firstSeenAt, '2026-09-17T00:00:00Z');
    assert.equal(r.lastSeenAt, '2026-09-17T00:00:00Z');

    const writes = sql.matching(IDENTITY_WRITE);
    assert.equal(writes.length, 1, 'exactly one statement — an upsert is one round trip, not a read then a write');
    const q = writes[0].text;

    assert.match(q, /INSERT\s+INTO\s+wallet_identity\s*\(\s*wallet\s*,\s*first_seen_at\s*,\s*last_seen_at\s*\)/i,
        'the insert must name all three columns explicitly — the drift oracle in ' +
        'test/migrations.runner.test.js parses this exact shape');
    assert.match(q, /ON\s+CONFLICT\s*\(\s*wallet\s*\)\s*DO\s+UPDATE/i,
        'one wallet = one row: the conflict target is the wallet primary key');
    assert.deepEqual(writes[0].values, [WALLET],
        'the wallet is PARAMETERISED, never interpolated into the SQL text');
});

test('⛔ the conflict path updates last_seen_at and NOTHING ELSE', async () => {
    const sql = recordingSql([{ match: IDENTITY_WRITE, rows: [{}] }]);
    await wa.touchWalletIdentity(sql, WALLET);
    const q = sql.matching(IDENTITY_WRITE)[0].text;

    // Isolate the SET clause: everything between DO UPDATE SET and RETURNING.
    const m = /DO\s+UPDATE\s+SET([\s\S]*?)RETURNING/i.exec(q);
    assert.ok(m, 'the upsert must have a DO UPDATE SET … RETURNING shape');
    const setClause = m[1];

    assert.match(setClause, /last_seen_at\s*=\s*NOW\(\)/i, 'last_seen_at advances on every visit');
    assert.ok(!/first_seen_at/i.test(setClause),
        'THE LOAD-BEARING ASSERTION: first_seen_at is written ONCE, by the insert. A conflict ' +
        'path that refreshed it would destroy the only fact this table exists to hold, and it ' +
        'would do so silently — every row would read as first seen today, forever.');

    // ── NARROWED 2026-09-17 BY WO-1852, THE "LATER STEP" THIS PIN NAMED ──────────
    // As written for WO-1844 this loop forbade all three reserved columns anywhere in
    // the statement, with the reason: "belong to later steps in this chain (the Vigil
    // read, Genesis Token binding)". WO-1852 IS the Vigil read, and it is chartered by
    // its work order to populate first_seen_staked_at from this very function — so the
    // column now legitimately appears in the UPSERT's RETURNING clause (read in the same
    // round trip rather than costing a second SELECT).
    //
    // ⛔ WHAT IS STILL FORBIDDEN, AND IT IS THE PART THAT WAS LOAD-BEARING ALL ALONG:
    //    first_seen_staked_at may be READ here but NEVER SET here. It is written only by
    //    stampFirstSeenStaked's separate, idempotent UPDATE (… WHERE first_seen_staked_at
    //    IS NULL), for exactly the reason first_seen_at may not appear in this SET clause:
    //    a tenure that has already begun must never be movable by a later visit. Putting
    //    it in this SET clause would reset every staker's Vigil on every request.
    assert.ok(!/first_seen_staked_at/i.test(setClause),
        'first_seen_staked_at must never be SET by the upsert — see stampFirstSeenStaked, ' +
        'whose IS NULL guard is what makes a Vigil un-resettable');

    // The Genesis Token columns remain wholly untouched: WO-1852 does not bind an SGT.
    for (const reserved of ['sgt_mint', 'sgt_verified_at']) {
        assert.ok(!new RegExp(reserved, 'i').test(q),
            `${reserved} is reserved for Genesis Token binding and must not appear in any statement yet`);
    }
});

test('⛔ WO-1852: the Vigil stamp is a SEPARATE, idempotent, Postgres-clocked UPDATE', async () => {
    // The counterpart to the assertion above. The stamp must not be folded into the
    // upsert, and it must carry both guards that make it safe to run on every auth:
    // the IS NULL predicate (so a begun tenure cannot move) and NOW() (so the clock that
    // writes the column is the clock that later measures the tenure from it).
    // ⛔ THE RPC ENDPOINT IS PINNED AT A DEAD PORT, NOT LEFT TO THE ENVIRONMENT. An earlier
    //    draft of this test branched on whether SOLANA_MAINNET_RPC_URL happened to be set,
    //    which made it near-vacuous locally AND would have had a UNIT TEST dial mainnet for
    //    a real wallet address on any CI that injects that var. Same env-seam discipline as
    //    test/skr-staking.test.js's unset-url case.
    const saved = process.env.SOLANA_MAINNET_RPC_URL;
    process.env.SOLANA_MAINNET_RPC_URL = 'http://127.0.0.1:1/dead';
    let r;
    let writes;
    try {
        const sql = recordingSql([{ match: /UPDATE\s+wallet_identity/i,
            rows: [{ first_seen_staked_at: 'T-NOW' }] }]);
        r = await wa.stampFirstSeenStaked(sql, WALLET);
        writes = sql.matching(/UPDATE\s+wallet_identity/i);
    } finally {
        if (saved === undefined) delete process.env.SOLANA_MAINNET_RPC_URL;
        else process.env.SOLANA_MAINNET_RPC_URL = saved;
    }

    assert.equal(writes.length, 0,
        'an unreadable chain must never begin a tenure — RPC_UNAVAILABLE is not evidence of a stake');
    assert.equal(r.stakedStamped, false);
    assert.equal(r.vigilDegraded, true, 'and it must SAY the read was degraded, never swallow it');

    // And the SQL text itself, asserted from the source so the guards cannot be dropped.
    const src = fs.readFileSync(WALLET_AUTH_PATH, 'utf8');
    const fn = src.slice(src.indexOf('async function stampFirstSeenStaked('));
    assert.match(fn, /SET\s+first_seen_staked_at\s*=\s*NOW\(\)/i,
        'Postgres time, never Node time — the work order names this to avoid clock skew');
    assert.match(fn, /WHERE\s+wallet\s*=\s*\$\{wallet\}\s*AND\s+first_seen_staked_at\s+IS\s+NULL/i,
        '⛔ the IS NULL guard is the whole idempotency argument; without it a tenure resets');
});

test('⛔ WO-1852: the Vigil probe on the auth path has a kill switch, and it really kills', async () => {
    // touchWalletIdentity runs on EVERY proven wallet-rail request, so the probe it can
    // now trigger is a standing cost. The switch exists so that cost can be removed
    // without a code change; this pins that it is checked BEFORE any work, not after.
    const prev = process.env.VIGIL_STAMP_ON_AUTH;
    process.env.VIGIL_STAMP_ON_AUTH = 'false';
    try {
        assert.equal(wa.vigilStampOnAuthEnabled(), false);
        const sql = recordingSql([{ match: IDENTITY_WRITE,
            rows: [{ first_seen_at: 'T0', last_seen_at: 'T0', first_seen_staked_at: null }] }]);
        const r = await wa.touchWalletIdentity(sql, WALLET);
        assert.equal(r.ok, true);
        assert.equal(sql.matching(/UPDATE\s+wallet_identity/i).length, 0);
        assert.equal(sql.calls.length, 1, 'exactly the one upsert — the switch skipped everything else');
    } finally {
        if (prev === undefined) delete process.env.VIGIL_STAMP_ON_AUTH;
        else process.env.VIGIL_STAMP_ON_AUTH = prev;
    }
});

test('⛔ no logic anywhere in wallet-auth.js touches the reserved Genesis Token columns', () => {
    const src = fs.readFileSync(WALLET_AUTH_PATH, 'utf8');
    // Comments legitimately NAME these columns to explain why they are untouched, so the
    // sweep is over code only — the prose is the documentation this assertion protects.
    const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');

    // ── NARROWED 2026-09-17 BY WO-1852 ───────────────────────────────────────────
    // `first_seen_staked_at` was in this list because WO-1844's explicit non-scope was
    // "declares these columns and builds NO logic for them". WO-1852 is the ticket that
    // builds that logic, so the column is now expected in executable code and its
    // ABSENCE would be the defect. The two Genesis Token columns are unchanged: nothing
    // binds an SGT yet, and this sweep still catches the first stray reference to one.
    for (const reserved of ['sgt_mint', 'sgt_verified_at']) {
        assert.ok(!new RegExp(reserved).test(code),
            `${reserved} appears in executable code. No ticket has built Genesis Token binding ` +
            'yet — this is still explicit non-scope.');
    }

    // The positive half, so the narrowing cannot quietly become "nothing is checked":
    // the Vigil column must be reachable ONLY through the two shapes WO-1852 defines.
    assert.ok(/first_seen_staked_at/.test(code),
        'WO-1852 builds this logic — its absence now means the Vigil stamp was reverted');
    // ⛔ AND EVERY REFERENCE IS CONTAINED IN THE TWO FUNCTIONS THAT OWN IT. A raw count
    //    ceiling would be an arbitrary number that goes stale on the first honest edit
    //    (CLAUDE.md's whole duplicated-state lesson); containment is the real property.
    //    The guards that make the write safe — the IS NULL predicate and NOW() — live in
    //    stampFirstSeenStaked, so a reference anywhere ELSE is a write that escaped them.
    const OWNERS = ['async function touchWalletIdentity(', 'async function stampFirstSeenStaked('];
    const spans = OWNERS.map((sig) => {
        const start = code.indexOf(sig);
        assert.ok(start >= 0, 'fixture assumption: ' + sig + ' is present');
        // Each function ends at the next top-level declaration.
        const rest = code.slice(start + sig.length);
        const end = rest.search(/\n(?:async )?function |\nconst \w+ = \{/);
        return [start, start + sig.length + (end < 0 ? rest.length : end)];
    });
    const inAnOwner = (idx) => spans.some(([a, b]) => idx >= a && idx < b);

    for (let i = code.indexOf('first_seen_staked_at'); i >= 0;
         i = code.indexOf('first_seen_staked_at', i + 1)) {
        assert.ok(inAnOwner(i),
            'first_seen_staked_at is referenced OUTSIDE touchWalletIdentity / ' +
            'stampFirstSeenStaked, at: ' + JSON.stringify(
                code.slice(code.lastIndexOf('\n', i) + 1, code.indexOf('\n', i)).trim()));
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. FAIL-OPEN — the property that keeps a deploy-order mistake harmless
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ a missing table degrades to a warning and NEVER rejects', async () => {
    const sql = recordingSql([{ match: IDENTITY_WRITE, throws: 'relation "wallet_identity" does not exist' }]);

    const { value: r, lines } = await captureWarnings(() => wa.touchWalletIdentity(sql, WALLET));

    assert.equal(r.ok, true,
        'This is the REAL case, not a hypothetical: if the JS deploys before migration 0029 is ' +
        'applied, every wallet auth hits this path. It must degrade, exactly as touchGuestRate does.');
    assert.equal(r.degraded, true, 'and it must SAY it degraded — a silent swallow is forbidden (§12)');
    assert.ok(lines.some(l => /wallet_identity/.test(l) && /fail-open/.test(l)),
        'the failure must be logged and name the table: ' + JSON.stringify(lines));
});

test('⛔ it cannot throw even when the client itself is broken', async () => {
    for (const broken of [null, undefined, {}, () => { throw new Error('sync boom'); }]) {
        const { value: r } = await captureWarnings(() => wa.touchWalletIdentity(broken, WALLET));
        assert.equal(r.ok, true, 'a broken client must still resolve ok — this function is awaited on the ' +
            'wallet rail SUCCESS path, so a throw here turns every good auth into a 500');
        assert.equal(r.degraded, true);
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. WIRING — who touches the table, and who must not
// ═══════════════════════════════════════════════════════════════════════════

// Drives the REAL authenticate() through the session rail: verifySession reads
// auth_sessions, so answering that query with a live row is a genuine wallet-rail
// success without needing an ed25519 signature.
function sessionRailSql(extra = []) {
    return recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: WALLET, revoked: false, expired: false }] },
        { match: IDENTITY_WRITE, rows: [{ first_seen_at: 'T0', last_seen_at: 'T0' }] },
        ...extra,
    ]);
}

test('a proven WALLET-rail authenticate() touches wallet_identity exactly once', async () => {
    const sql = sessionRailSql();
    const req = { headers: { 'x-session': SESSION }, method: 'GET' };
    const r = await wa.authenticate(sql, req, null, WALLET);

    assert.equal(r.ok, true, 'fixture assumption: the session rail authenticates');
    assert.equal(r.mode, 'wallet');
    assert.equal(r.identity, WALLET);
    assert.equal(sql.matching(IDENTITY_WRITE).length, 1, 'one visit, one write');
    assert.deepEqual(sql.matching(IDENTITY_WRITE)[0].values, [WALLET],
        'the row is keyed by the PROVEN wallet the rail returned, never by the claimed id');

    // The result object must be byte-identical to before this ticket: routes downstream
    // branch on these keys, and identity tracking is not part of the auth contract.
    assert.deepEqual(Object.keys(r).sort(), ['identity', 'mode', 'ok'],
        'touchWalletIdentity must add NOTHING to authenticate()\'s return shape');
});

test('⛔ a FAILED authenticate() writes no identity row', async () => {
    // Unknown session and no signature headers → the wallet rail refuses.
    const sql = recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [] },
        { match: IDENTITY_WRITE, rows: [{}] },
    ]);
    const r = await wa.authenticate(sql, { headers: { 'x-session': SESSION } }, null, WALLET);

    assert.equal(r.ok, false, 'fixture assumption: an unknown session does not authenticate');
    assert.equal(sql.matching(IDENTITY_WRITE).length, 0,
        'an unproven caller must leave no trace in an IDENTITY table — otherwise anyone who can ' +
        'name a wallet can create its identity row');
});

test('⛔ the GUEST rail writes no identity row', async () => {
    const sql = recordingSql([
        { match: /INSERT\s+INTO\s+guest_rate_limit/i, rows: [{ hits: 1, total_hits: 1 }] },
        { match: IDENTITY_WRITE, rows: [{}] },
    ]);
    const r = await wa.authenticate(sql, { headers: { 'x-guest-id': GUEST } }, null, GUEST);

    assert.equal(r.ok, true, 'fixture assumption: a guest still authenticates for its own row');
    assert.equal(r.mode, 'guest');
    assert.equal(sql.matching(IDENTITY_WRITE).length, 0,
        'a guest id is MINTED BY THE CLIENT (see the honesty note on verifyGuest), so recording ' +
        'one as an identity fills this table with whatever an attacker cares to mint');
});

test('⛔ a play- id does not reach the wallet identity table either', async () => {
    // The Play rail is dormant unless GOOGLE_IDENTITY_ENABLED, and either way its id can
    // never carry the Solana-shaped columns this table reserves.
    const PLAY = 'play-' + 'c'.repeat(64);
    const sql = recordingSql([
        { match: /FROM\s+auth_sessions/i, rows: [{ wallet: PLAY, revoked: false, expired: false }] },
        { match: IDENTITY_WRITE, rows: [{}] },
    ]);
    const r = await wa.authenticate(sql, { headers: { 'x-session': SESSION } }, null, PLAY);
    assert.notEqual(r.mode, 'wallet', 'a play- id is never the wallet rail');
    assert.equal(sql.matching(IDENTITY_WRITE).length, 0);
});

test('there is exactly ONE call site, and it is inside authenticate()', () => {
    const src = fs.readFileSync(WALLET_AUTH_PATH, 'utf8');
    const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
    const calls = code.match(/await\s+touchWalletIdentity\s*\(/g) || [];
    assert.equal(calls.length, 1,
        'authenticateGranting() and authenticatePromoRedeem() both DELEGATE to authenticate(), so a ' +
        'second call in either would double-write the same row on one request. One call site, in the ' +
        'one function that actually proves the wallet.');

    const fnStart = code.indexOf('async function authenticate(');
    const nextFn = code.indexOf('async function authenticateGranting(');
    const site = code.indexOf('await touchWalletIdentity(');
    assert.ok(fnStart >= 0 && nextFn > fnStart, 'fixture assumption: both functions are present in order');
    assert.ok(site > fnStart && site < nextFn, 'the call site must live inside authenticate()');
});

// ⚠ DISCLOSED CONSEQUENCE, NOT A DESIGN GOAL. WO-1844 says "not from
// authenticatePromoRedeem()". That route has no verification point of its own — it
// delegates to authenticate() — so a WALLET-mode promo redeem does touch the row, while
// the guest-mode redeem the exception exists for does not. Pinned here so the behaviour
// is written down rather than discovered later; the lead rules whether it stays.
test('wallet-mode promo redeem inherits the touch via delegation (disclosed)', async () => {
    const sql = sessionRailSql();
    const r = await wa.authenticatePromoRedeem(sql, { headers: { 'x-session': SESSION } }, null, WALLET);
    assert.equal(r.ok, true);
    assert.equal(r.unproven, false, 'a wallet holder redeems as a proven identity');
    assert.equal(sql.matching(IDENTITY_WRITE).length, 1,
        'inherited from authenticate(); see this test\'s comment — not an independent call site');
});

test('a guest-mode promo redeem still writes no identity row', async () => {
    const sql = recordingSql([
        { match: /INSERT\s+INTO\s+guest_rate_limit/i, rows: [{ hits: 1, total_hits: 1 }] },
        { match: IDENTITY_WRITE, rows: [{}] },
    ]);
    const r = await wa.authenticatePromoRedeem(sql, { headers: { 'x-guest-id': GUEST } }, null, GUEST);
    assert.equal(r.ok, true);
    assert.equal(r.unproven, true, 'the scoped exception still marks a guest unproven');
    assert.equal(sql.matching(IDENTITY_WRITE).length, 0);
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE MIGRATION — because a column nothing creates is a 42703 per request
// ═══════════════════════════════════════════════════════════════════════════

test('the migration exists, is re-runnable, and declares the full table', () => {
    assert.ok(fs.existsSync(MIGRATION_PATH),
        `${MIGRATION} must exist under api/migrations/ — an ALTER or CREATE that lives only in ` +
        'api/schema.sql is a DESCRIPTION; only api/migrations/ is ever applied');
    const body = fs.readFileSync(MIGRATION_PATH, 'utf8');

    assert.match(body, /CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+wallet_identity/i,
        'IF NOT EXISTS keeps the file re-runnable under a ledger re-apply');
    for (const col of ['wallet', 'first_seen_at', 'last_seen_at', 'first_seen_staked_at', 'sgt_mint', 'sgt_verified_at']) {
        assert.match(body, new RegExp('\\b' + col + '\\b'),
            `${col} must be declared now, so the table shape needs no second migration later`);
    }
    assert.match(body, /wallet\s+TEXT\s+PRIMARY\s+KEY/i, 'one wallet = one row is enforced by the key');
    assert.match(body, /CREATE\s+INDEX\s+IF\s+NOT\s+EXISTS\s+wallet_identity_first_seen_staked_idx/i);
    assert.match(body, /WHERE\s+first_seen_staked_at\s+IS\s+NOT\s+NULL/i,
        'the partial index clause — verified accepted on the live Neon instance (PostgreSQL 17.11)');

    // The runner refuses to apply a destructive migration; keep this file clean of that too.
    assert.ok(!/\b(DROP|TRUNCATE)\b/i.test(body), 'nothing destructive, including in the prose');
});

test('api/schema.sql describes the same table, and points at the applyable copy', () => {
    const schema = fs.readFileSync(SCHEMA_SQL, 'utf8');
    assert.match(schema, /CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+wallet_identity/i,
        'schema.sql is the readable map of the database and may never disagree with api/migrations/');
    assert.ok(schema.includes(MIGRATION),
        'the schema.sql block must name its applyable migration by filename, as 0024/0027/0028 do');
});
