'use strict';

// =============================================================================
// heartbound-state.test.js — WO-1675 (HEART-002).
// -----------------------------------------------------------------------------
// The `sql` tagged template is mocked the way test/patronage.test.js does it
// (`const sql = async (strings, ...values) => ...`, patronage.test.js:56-62): the
// statement text is reassembled with `strings.join('?')` and the bound values are
// captured separately, so a test can assert BOTH what SQL was sent and what was
// parameterised.
//
// ⛔ WHAT A MOCKED sql CAN AND CANNOT PROVE. It proves the STATEMENT SHAPE — that
//    GREATEST() is what makes highest_lifetime_tier monotonic, that the failure path
//    names no verified column, that activation re-asserts its own timestamp. It does
//    NOT execute Postgres, so it cannot prove the DATABASE behaviour those shapes
//    produce. DATABASE_URL is REDACTED for every agent seat (tools/run-migrations.mjs:6-7),
//    so acceptance criteria 3 (two writes and a read) and 4 (MIGRATIONS_OK + a shape
//    query) are OWNER-RUN and are recorded in the RESULT as UNPROVEN by this lane
//    rather than ticked. Naming an unproven thing as unproven is the rule (CLAUDE.md
//    §11B); asserting it from a mock would be the lie.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const {
    HEARTBOUND_STATUSES,
    MAX_RAW_AMOUNT,
    toRawAmountText,
    toSnapshot,
    readHeartboundState,
    readHeartboundStatus,
    activateHeartbound,
    recordVerifiedStake,
    recordVerificationFailure,
} = require('../api/_lib/heartbound-state');

const MODULE_PATH = path.join(__dirname, '..', 'api', '_lib', 'heartbound-state.js');
const MIGRATION_PATH = path.join(__dirname, '..', 'api', 'migrations', '20260910_0025_heartbound_state.sql');
const SCHEMA_PATH = path.join(__dirname, '..', 'api', 'schema.sql');

/** A mocked neon `sql` tag. Records the statement text + bound values, returns `rows`. */
function mockSql(rows = []) {
    const calls = [];
    const sql = async (strings, ...values) => {
        calls.push({ text: strings.join('?'), values });
        return rows;
    };
    sql.calls = calls;
    sql.last = () => calls[calls.length - 1];
    return sql;
}

const ROW = Object.freeze({
    player_id: 'WalletAAA',
    status: 'ACTIVE',
    activated_at_utc: '2026-09-01T00:00:00.000Z',
    last_verified_at_utc: '2026-09-10T12:00:00.000Z',
    last_actual_stake: '1000000000000',
    effective_resonating_stake: '750000000000',
    last_verification_attempt_at_utc: null,
    last_verification_code: null,
    stale_since_utc: null,
    continuous_pulse_count: 4,
    total_lifetime_pulses: 11,
    last_global_pulse_id: 'pulse-99',
    last_player_pulse_id: 'pulse-99',
    resonance_score: '1234.500000',
    resonance_tier: 3,
    highest_lifetime_tier: 5,
    current_tree_resonance_stage: 2,
    pending_echo_events: [],
    last_echo_event_id: null,
    version: 7,
});

// ═══════════════════════════════════════════════════════════════════════════
// 1. THE SIX STATUSES, AND THE TABLE THAT CONSTRAINS THEM
// ═══════════════════════════════════════════════════════════════════════════

test('the six Heartbound statuses are exactly the spec list, and the CHECK matches them', () => {
    assert.deepEqual([...HEARTBOUND_STATUSES],
        ['DORMANT', 'ACTIVE', 'STALE', 'UNSTAKING', 'DISCONNECTED', 'SUSPENDED']);
    assert.ok(Object.isFrozen(HEARTBOUND_STATUSES));

    // The JS list and the SQL CHECK are two copies of one fact, which is the failure
    // CLAUDE.md §2/§5/§8/§16 each carry a scar from. They cannot be merged (one is
    // DDL), so this test is the seam that makes them disagree loudly.
    const migration = fs.readFileSync(MIGRATION_PATH, 'utf8');
    const check = /CHECK \(status IN \(([^)]*)\)\)/.exec(migration);
    assert.ok(check, 'the status CHECK must be in the CREATE body — schema-parity.mjs is blind to ALTERs');
    const sqlValues = check[1].split(',').map(v => v.trim().replace(/^'|'$/g, ''));
    assert.deepEqual(sqlValues, [...HEARTBOUND_STATUSES]);
});

test('the migration and api/schema.sql carry the SAME CREATE TABLE body', () => {
    const grab = (text) => {
        const m = /CREATE TABLE IF NOT EXISTS heartbound_state \(([\s\S]*?)\r?\n\);/.exec(text);
        assert.ok(m, 'heartbound_state CREATE body not found');
        return m[1].replace(/\r/g, '');
    };
    // schema.sql is a DESCRIPTION and is never applied (schema.sql:105-107); only
    // api/migrations/*.sql runs. tools/schema-parity.mjs compares the LIVE database
    // against schema.sql, so if these two ever disagree the gate is measuring one
    // thing and production is running another.
    assert.equal(grab(fs.readFileSync(SCHEMA_PATH, 'utf8')), grab(fs.readFileSync(MIGRATION_PATH, 'utf8')));
});

test('every heartbound_state object is DECLARED EXACTLY ONCE in each file', () => {
    // ⚠ THIS TEST EXISTS BECAUSE THE BUG HAPPENED. Inserting the description block
    //   into api/schema.sql duplicated both CREATE INDEX statements (the slice taken from
    //   the migration already carried them, and they were appended again). `IF NOT EXISTS`
    //   made the copy HARMLESS TO RUN and therefore INVISIBLE — the same shape as memory
    //   `idempotent-ddl-hides-a-stale-table`: idempotent DDL does not report what it
    //   skipped. It was caught by eye on a line-number check, which is not a gate.
    for (const file of [MIGRATION_PATH, SCHEMA_PATH]) {
        const text = fs.readFileSync(file, 'utf8');
        for (const object of [
            'CREATE TABLE IF NOT EXISTS heartbound_state',
            'CREATE INDEX IF NOT EXISTS idx_heartbound_state_status_verified',
            'CREATE INDEX IF NOT EXISTS idx_heartbound_state_stale_since',
        ]) {
            const hits = text.split(object).length - 1;
            assert.equal(hits, 1, `${object} appears ${hits}x in ${path.basename(file)}`);
        }
    }
});

test('the migration is additive — no DROP / DELETE / TRUNCATE outside comments', () => {
    // tools/run-migrations.mjs:48 stops the WHOLE run before applying anything if a
    // destructive statement appears in any file. Catch it here rather than at 2am.
    const code = fs.readFileSync(MIGRATION_PATH, 'utf8')
        .split('\n').filter(l => !l.trim().startsWith('--')).join('\n');
    assert.doesNotMatch(code, /\b(DROP|DELETE|TRUNCATE)\b/i);
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. BIGINT-SAFE RAW SKR — a `number` is REFUSED, never coerced
// ═══════════════════════════════════════════════════════════════════════════

test('the raw-amount coercion IS heartbound-resonance.toBigInt, not a second copy of it', () => {
    // ⛔ ONE RULE FOR WHAT A CHAIN-SOURCED u128 MAY BE, and it lives in the module that
    //    also does the arithmetic (heartbound-resonance.js:95). This test asserts the
    //    IDENTITY of the imported function, so deleting the import and re-implementing
    //    the rule here — the thing that nearly happened — goes red.
    const resonance = require('../api/_lib/heartbound-resonance');
    const source = fs.readFileSync(MODULE_PATH, 'utf8');
    assert.match(source, /require\('\.\/heartbound-resonance'\)/);
    assert.equal(typeof resonance.toBigInt, 'function');

    assert.equal(toRawAmountText(0n), '0');
    assert.equal(toRawAmountText(1000000000000n), '1000000000000');
    assert.equal(toRawAmountText('0042'), '42', 'leading zeros are normalised');
    assert.equal(toRawAmountText(MAX_RAW_AMOUNT), '340282366920938463463374607431768211455');

    // ⛔ THE PRECISION LINE IS AT Number.MAX_SAFE_INTEGER, and it is resonance's line,
    //    not one invented here: a SAFE integer has lost nothing and is honoured; an
    //    UNSAFE one arrived as a lossy double and no function downstream can repair it,
    //    so it is refused rather than coerced. That rounding would otherwise land in a
    //    reward. (This module's first draft refused EVERY Number — stricter, but a
    //    second rule over one currency, which is the failure being avoided.)
    assert.equal(toRawAmountText(12345), '12345', 'a safe integer is honoured');
    assert.equal(toRawAmountText(Number.MAX_SAFE_INTEGER), '9007199254740991');
    assert.throws(() => toRawAmountText(1e21), /MAX_SAFE_INTEGER/);
    assert.throws(() => toRawAmountText(Number.MAX_SAFE_INTEGER + 2), /MAX_SAFE_INTEGER/);
    assert.throws(() => toRawAmountText(1.5), /expected an integer/);

    // …and the two constraints that are THIS file's, because they belong to the
    // NUMERIC(39,0) column rather than to the curve. Resonance has no equivalent.
    assert.throws(() => toRawAmountText(-1n), /negative/);
    assert.throws(() => toRawAmountText(MAX_RAW_AMOUNT + 1n), /u128/);

    assert.throws(() => toRawAmountText('12.5'), /non-negative integer string/);
    assert.throws(() => toRawAmountText('0x10'), /non-negative integer string/);
    assert.throws(() => toRawAmountText(null), /expected BigInt/);
});

test('a stake larger than Number.MAX_SAFE_INTEGER survives the snapshot exactly', () => {
    const huge = '9007199254740993000000';                     // > 2^53, deliberately
    const snap = toSnapshot({ ...ROW, last_actual_stake: huge });
    assert.equal(snap.lastActualStake, huge);
    assert.notEqual(String(Number(huge)), huge, 'the control: a JS number cannot hold this');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. THE READ — numerics leave Postgres as text, never as a float
// ═══════════════════════════════════════════════════════════════════════════

test('the read casts every numeric column ::text and binds only the player id', async () => {
    const sql = mockSql([ROW]);
    const snap = await readHeartboundState(sql, 'WalletAAA');

    const { text, values } = sql.last();
    assert.match(text, /FROM heartbound_state/);
    assert.match(text, /WHERE player_id = \?/);
    assert.deepEqual(values, ['WalletAAA']);
    for (const col of ['last_actual_stake', 'effective_resonating_stake', 'resonance_score'])
        assert.match(text, new RegExp(`${col}::text`), `${col} must leave Postgres as text`);

    assert.equal(snap.lastActualStake, '1000000000000');
    assert.equal(snap.resonanceScore, '1234.500000');
    assert.equal(snap.highestLifetimeTier, 5);
    assert.equal(snap.version, 7);
    assert.ok(Object.isFrozen(snap));
});

test('a missing row is DORMANT with no state — never an error and never a zeroed stake', async () => {
    const status = await readHeartboundStatus(mockSql([]), 'WalletNEW');
    assert.equal(status.exists, false);
    assert.equal(status.status, 'DORMANT');
    assert.equal(status.state, null);
    assert.equal(status.nextTierAt, null);
});

test('the read refuses a missing sql tag or an empty player id rather than guessing', async () => {
    await assert.rejects(() => readHeartboundState(null, 'W'), /sql tagged-template/);
    await assert.rejects(() => readHeartboundState(mockSql([]), ''), /playerId/);
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. ACTIVATION — idempotent, and the floor never moves
// ═══════════════════════════════════════════════════════════════════════════

test('activation is idempotent and can never re-stamp activated_at_utc', async () => {
    const sql = mockSql([ROW]);
    await activateHeartbound(sql, 'WalletAAA');
    const { text, values } = sql.last();

    assert.match(text, /INSERT INTO heartbound_state/);
    assert.match(text, /ON CONFLICT \(player_id\) DO UPDATE/);
    // ⛔ The self-assignment IS the guarantee. spec :242 — "Do NOT retroactively award
    //    historical Elarion pulses" — is enforced by every WO-1677 pulse query filtering
    //    on this timestamp. Forward-moving it loses the player pulses; backward-moving it
    //    would let them claim the chain's whole history.
    assert.match(text, /activated_at_utc = heartbound_state\.activated_at_utc/);
    assert.deepEqual(values, ['WalletAAA', 'ACTIVE']);
});

test('activation refuses a status that is not one of the six', async () => {
    await assert.rejects(() => activateHeartbound(mockSql([]), 'W', { status: 'ONLINE' }),
        /unknown Heartbound status/);
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. VERIFIED WRITE — the lifetime high-water mark is monotonic IN SQL
// ═══════════════════════════════════════════════════════════════════════════

test('highest_lifetime_tier is written with GREATEST while resonance_tier is written flat', async () => {
    const sql = mockSql([ROW]);
    await recordVerifiedStake(sql, 'WalletAAA', {
        lastActualStake: 1000000000000n,
        effectiveResonatingStake: '750000000000',
        resonanceScore: '1234.5',
        resonanceTier: 3,
    });
    const { text, values } = sql.last();

    // ⛔ MONOTONIC IN THE STATEMENT, NOT IN JS. A caller that reaches this SQL without
    //    reading the row first still cannot lower the high-water mark — the same
    //    two-defence shape GREATEST(player_data.reset_epoch, …) uses (migration 0023).
    assert.match(text, /highest_lifetime_tier\s*=\s*GREATEST\(heartbound_state\.highest_lifetime_tier,\s*EXCLUDED\.highest_lifetime_tier\)/);
    // …and the CURRENT tier is a plain assignment, because the bond is allowed to weaken.
    assert.match(text, /resonance_tier\s*=\s*EXCLUDED\.resonance_tier/);
    assert.match(text, /version\s*=\s*heartbound_state\.version \+ 1/);
    assert.match(text, /activated_at_utc\s*=\s*heartbound_state\.activated_at_utc/);
    // A success clears the outage bookkeeping — the outage is over.
    assert.match(text, /stale_since_utc\s*=\s*NULL/);
    assert.match(text, /last_verification_code\s*=\s*NULL/);

    assert.ok(values.includes('1000000000000'), 'the stake is bound as a digit STRING, not a JS number');
    assert.equal(values.some(v => typeof v === 'number' && v > Number.MAX_SAFE_INTEGER), false);
});

test('the verified write refuses a float-shaped stake and a negative tier', async () => {
    const sql = mockSql([ROW]);
    // 1e21 base units is ~1e15 SKR — well past a double's integer precision, so it
    // reached this call already rounded. Refused, per the shared resonance rule.
    await assert.rejects(() => recordVerifiedStake(sql, 'W', {
        lastActualStake: 1e21, effectiveResonatingStake: '1',
    }), /MAX_SAFE_INTEGER/);
    await assert.rejects(() => recordVerifiedStake(sql, 'W', {
        lastActualStake: 100.5, effectiveResonatingStake: '1',
    }), /expected an integer/);
    await assert.rejects(() => recordVerifiedStake(sql, 'W', {
        lastActualStake: '1', effectiveResonatingStake: '1', resonanceTier: -1,
    }), /resonanceTier/);
    await assert.rejects(() => recordVerifiedStake(sql, 'W', {
        lastActualStake: '1', effectiveResonatingStake: '1', resonanceScore: 'NaN',
    }), /resonanceScore/);
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. FAILED VERIFICATION — fail to LAST-KNOWN, never to zero  (Q2 ruling)
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the failure path names NOT ONE verified column, so it cannot zero a stake', async () => {
    const sql = mockSql([{ ...ROW, status: 'STALE', last_verification_code: 'RPC_UNAVAILABLE' }]);
    await recordVerificationFailure(sql, 'WalletAAA', { code: 'RPC_UNAVAILABLE' });
    const { text } = sql.last();

    const setClause = text.slice(text.indexOf('SET'), text.indexOf('WHERE'));
    for (const forbidden of [
        'last_actual_stake', 'effective_resonating_stake', 'last_verified_at_utc',
        'resonance_score', 'resonance_tier', 'highest_lifetime_tier',
        'current_tree_resonance_stage', 'activated_at_utc',
        'continuous_pulse_count', 'total_lifetime_pulses',
    ]) {
        assert.doesNotMatch(setClause, new RegExp(`\\b${forbidden}\\b`),
            `a failed verification must not write ${forbidden} — the client fails CLOSED for the ` +
            'perk (NativeSkrStakeQuery.cs:87-94) and that is WRONG for a streak. RULED 2026-09-10: ' +
            'fail to LAST-KNOWN VERIFIED STATE.');
    }
    assert.match(setClause, /status\s*=\s*\?/);
    assert.match(setClause, /last_verification_code\s*=\s*\?/);
});

test('the failure path UPDATEs and never INSERTs — an outage cannot create a row', async () => {
    const sql = mockSql([]);
    const result = await recordVerificationFailure(sql, 'WalletNEVER', { code: 'RPC_UNAVAILABLE' });
    const { text } = sql.last();
    assert.match(text, /^\s*UPDATE heartbound_state/);
    assert.doesNotMatch(text, /INSERT/);
    // Zero rows updated is the honest outcome for a player who has never verified: a
    // row born from an outage would claim an activation timestamp on no evidence,
    // which is migration 0022's `DEFAULT 10` mistake in a new costume.
    assert.equal(result, null);
});

test('stale_since_utc is stamped on the FIRST failure only, so the window measures the outage', async () => {
    const sql = mockSql([ROW]);
    await recordVerificationFailure(sql, 'WalletAAA', { code: 'RPC_UNAVAILABLE' });
    // COALESCE keeps an existing value: a client that retries forever would otherwise
    // keep pushing the window's start forward and never leave it.
    assert.match(sql.last().text, /stale_since_utc\s*=\s*COALESCE\(heartbound_state\.stale_since_utc/);
});

test('a failure code is required — "it failed somehow" is not a record', async () => {
    await assert.rejects(() => recordVerificationFailure(mockSql([]), 'W', {}), /failure code is required/);
});

// ═══════════════════════════════════════════════════════════════════════════
// 7. THE GRACE WINDOW — the LENGTH is a server-only knob, not a constant here
// ═══════════════════════════════════════════════════════════════════════════

test('the last-known verified snapshot survives an outage untouched', () => {
    const stale = toSnapshot({
        ...ROW,
        status: 'STALE',
        last_verification_attempt_at_utc: '2026-09-10T18:00:00.000Z',
        last_verification_code: 'RPC_UNAVAILABLE',
        stale_since_utc: '2026-09-10T17:00:00.000Z',
    });
    assert.equal(stale.status, 'STALE');
    assert.equal(stale.lastActualStake, '1000000000000', 'the verified stake is still the last VERIFIED one');
    assert.equal(stale.lastVerifiedAtMs, Date.parse('2026-09-10T12:00:00.000Z'));
    assert.equal(stale.lastVerificationCode, 'RPC_UNAVAILABLE');
    assert.equal(stale.staleSinceMs, Date.parse('2026-09-10T17:00:00.000Z'));
});

test('withinGraceWindow is null until somebody supplies the window length', () => {
    // ⛔ NO DEFAULT WINDOW IS INVENTED HERE. Heartbound's knobs live as Command Center
    //    SERVER-ONLY rows (Q-CONFIG ruling); a number hardcoded in this module would be
    //    a second copy of a tunable the owner cannot see. `null` is the honest answer to
    //    "has the window expired" when nobody has said how long it is.
    const blind = toSnapshot(ROW);
    assert.equal(blind.graceWindowMs, null);
    assert.equal(blind.graceExpiresAtMs, null);
    assert.equal(blind.withinGraceWindow, null);

    const verifiedAt = Date.parse('2026-09-10T12:00:00.000Z');
    const window = 48 * 3600 * 1000;
    const inside = toSnapshot(ROW, { graceWindowMs: window, nowMs: verifiedAt + window - 1 });
    assert.equal(inside.withinGraceWindow, true);
    assert.equal(inside.graceExpiresAtMs, verifiedAt + window);

    const outside = toSnapshot(ROW, { graceWindowMs: window, nowMs: verifiedAt + window + 1 });
    assert.equal(outside.withinGraceWindow, false);
});

// ═══════════════════════════════════════════════════════════════════════════
// 8. nextTierAt IS SERVED — the client never derives it, and neither does this file
// ═══════════════════════════════════════════════════════════════════════════

test('the status read serves nextTierAt from an INJECTED resolver', async () => {
    const seen = [];
    const status = await readHeartboundStatus(mockSql([ROW]), 'WalletAAA', {
        nowMs: 1757520000000,
        resolveTier: (input) => { seen.push(input); return { nextTierAt: 2000000000000n }; },
    });
    assert.equal(status.nextTierAt, '2000000000000', 'a threshold crosses the wire as a string');
    assert.equal(status.serverNowMs, 1757520000000, 'the server owns the clock (save.js:753-755)');
    assert.deepEqual(seen, [{
        effectiveResonatingStake: '750000000000',
        resonanceScore: '1234.500000',
        resonanceTier: 3,
    }]);
});

test('with no resolver, nextTierAt is null — it is never inferred from the tier number', async () => {
    const status = await readHeartboundStatus(mockSql([ROW]), 'WalletAAA');
    assert.equal(status.exists, true);
    assert.equal(status.nextTierAt, null);
    // A resolver that cannot answer (top tier, thresholds unloaded) says so the same way.
    const top = await readHeartboundStatus(mockSql([ROW]), 'WalletAAA', { resolveTier: () => ({}) });
    assert.equal(top.nextTierAt, null);
});

test('⛔ this module holds NO tier ladder, NO thresholds and NO resonance mathematics', () => {
    const source = fs.readFileSync(MODULE_PATH, 'utf8');
    const code = source.split('\n')
        .filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l))
        .join('\n');
    // WO-1676/1679 own the curve and the ladder. A threshold table here would be the
    // second copy of the authority — the exact failure Q-LADDER was raised about.
    assert.doesNotMatch(code, /TIERS\s*=|THRESHOLD|LADDER/i);
    assert.doesNotMatch(code, /Math\.(pow|log|sqrt)/);
});

// ═══════════════════════════════════════════════════════════════════════════
// 9. ONE WALLET = ONE REALM — there is no re-binding surface to abuse
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ no function in this module can move a Heartbound row between two wallets', () => {
    const api = require('../api/_lib/heartbound-state');
    // RULED 2026-09-10: one wallet, one realm; no re-binding; NO LINKAGE TABLE. The
    // spec's "Wallet Change" section (HEART-002 :235-247) is DROPPED, not implemented —
    // a different wallet is a different player with a different row. The absence is the
    // deliverable, so it is pinned rather than left to be re-added by a later reader.
    for (const name of Object.keys(api))
        assert.doesNotMatch(name, /rebind|relink|transfer|migrateWallet|changeWallet/i, name);

    const source = fs.readFileSync(MODULE_PATH, 'utf8');
    assert.doesNotMatch(source, /oldWallet|newWallet|previousPlayerId/);

    // And the table has no second identity column that a linkage layer could grow from.
    const migration = fs.readFileSync(MIGRATION_PATH, 'utf8');
    const body = /CREATE TABLE IF NOT EXISTS heartbound_state \(([\s\S]*?)\n\);/.exec(migration)[1];
    assert.doesNotMatch(body, /wallet_address|linked_wallet|previous_wallet/);
    assert.match(body, /player_id\s+TEXT PRIMARY KEY/);
});

test('⛔ nothing here touches the client-authored save blob or its schema version', () => {
    const source = fs.readFileSync(MODULE_PATH, 'utf8');
    const code = source.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
    // §0b of the WO: a SaveSchema bump would be the DEFECT, not the deliverable — it
    // would put authoritative reward state onto the client-owned wire, where
    // api/game/save.js:410-421 says it is "a RECORD, NOT A CONTROL".
    assert.doesNotMatch(code, /game_state|player_data|schema_version|SaveSchema/);
});
