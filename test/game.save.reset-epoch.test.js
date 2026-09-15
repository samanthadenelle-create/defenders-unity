'use strict';

// =============================================================================
// WO-1598 — a legitimate NEW GAME is rejected by the save sanity guard.
// -----------------------------------------------------------------------------
// ⛔ THE DEFECT, EXACTLY. The guard in api/game/save.js compares every incoming
// balance against the STORED row and strips anything that falls by more than
// MAX_BALANCE_DROP_FRACTION (`implausible_drop`), and refuses any bestWave below
// the stored high-water mark (`rollback`). Those rules are correct for a tampered
// or replayed save and WRONG for a reset: a new town legitimately holds 13-36
// crystals and wave 0 while the row still carries the old town's 901 and a high
// bestWave. Measured in analytics_events on 2026-09-07: 177 `save_sanity_reject`
// rows in 14 days, the owner's own wallet row eleven times today
// (`implausible_drop crystals 901 -> 36`), a guest row 166 times
// (`wood 15 -> 0`, `iron 5 -> 0`, `rollback:bestWave`).
//
// The cost is not the rejection. The rejected FIELDS are stripped and the rest of
// the save lands, so the cloud row keeps the OLD balances beside the new town's
// everything-else — and api/game/load.js hands those old balances back on the next
// load. The new game never exists in the cloud.
//
// THE FIX UNDER TEST: the body may declare a monotonic `resetEpoch`. A save whose
// epoch is NEWER than the stored one is a declared reset — the COMPARATIVE guards
// (implausible_drop / rollback) do not apply to it, exactly once, and the epoch
// advances. Equal epochs are an ordinary save and the guard applies as today. An
// OLDER epoch is an old device replaying a dead town and is refused
// SAVE_RESET_STALE. An ABSENT epoch (every client shipped before this) behaves
// EXACTLY as today — no bypass, no new audit row, nothing.
//
// ⚠ A RESET IS NOT A BLANK CHEQUE. Only the comparative rules stand down. The
// BOUNDS (negative / non-finite / > MAX_RESOURCE / > MAX_BEST_WAVE) still run, so
// `resetEpoch` can never be used to land an impossible balance. Case (5) proves it.
//
// RED PROOF — this file was written and run BEFORE api/ was touched
// (2026-09-07, `node --test test/game.save.reset-epoch.test.js` against the
// untouched handler): tests 9, pass 3, FAIL 6. The six, with the verbatim message:
//   ✖ a NEWER resetEpoch lands the new town's balances instead of stripping them
//        "the new town's crystals were STRIPPED by implausible_drop — the cloud row
//         keeps the old town (wrote undefined)"   undefined !== 36
//   ✖ a NEWER resetEpoch writes save_reset_accepted and NOT save_sanity_reject
//        "the sanity guard still fired on a declared reset"
//   ✖ an OLDER resetEpoch is REFUSED, and nothing is written
//        "a stale replay was ACCEPTED - an old device can overwrite a newer new game"
//        200 !== 409 — the old code has no notion of an epoch at all
//   ✖ an ABSENT resetEpoch never lowers the stored epoch
//        "the upsert does not clamp reset_epoch monotonically"
//   ✖ /api/game/load returns resetEpoch, null when the column is NULL
//        "resetEpoch is undefined - JSON.stringify DROPS it, so a client cannot
//         tell it is behind"
//   ✖ /api/game/load returns the stored resetEpoch when the row has one
// The three that PASSED on the old code are the EQUAL case, the ABSENT case and the
// out-of-bounds case, and that is the point of including the first two: they pin that the
// fix changed nothing for an ordinary save and nothing for every client in the field.
// ⚠ THE OUT-OF-BOUNDS CASE'S RED-RUN PASS WAS A FALSE ONE, and it is recorded rather than
// quietly corrected: the fixture poisoned only the FLAT `crystals`, buildState's normalise
// pass replaced it from the nested `resources` block, and the case then passed because
// `crystals` appeared in the rejects as an implausible_drop — the very rule a reset is
// meant to stand down. Caught after the fix, when the bypass removed that rule and the
// case went red for the right reason. The fixture now poisons BOTH spellings and the
// assertion names the RULE, not just the field.
//
// ZERO NETWORK, ZERO DATABASE. `@neondatabase/serverless` is replaced in
// require.cache BEFORE save.js/load.js are required; `authenticate`,
// `logAuthReject`, `logApiEvent` and the maintenance kill switches are swapped on
// their (plain-object) module exports. No api/ source file is modified.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const Module = require('node:module');

// The SQL half of the monotonic clamp is a source-shape assertion: there is no Postgres
// in this runner, and GREATEST()'s semantics are Postgres's job to keep, not ours to
// re-test. Same split as test/game.save.schema-version.test.js.
const SAVE_SRC = fs.readFileSync(
    path.join(__dirname, '..', 'api', 'game', 'save.js'), 'utf8');

process.env.DATABASE_URL = process.env.DATABASE_URL || 'postgres://fake/fake';

// ── The database stub, installed before save.js can capture the real neon ────

let priorRow = null;      // what the prior-state SELECT answers with
let queries = [];         // { text, values } for every statement the handler ran

function sqlTag(strings, ...values) {
    const text = Array.isArray(strings) ? strings.raw.join(' ? ') : String(strings);
    queries.push({ text, values });

    if (/FROM player_data\s+WHERE player_id/.test(text)) {
        return Promise.resolve(priorRow ? [priorRow] : []);
    }
    return Promise.resolve([]);
}

const neonId = require.resolve('@neondatabase/serverless');
require.cache[neonId] = new Module(neonId, null);
require.cache[neonId].filename = neonId;
require.cache[neonId].loaded = true;
require.cache[neonId].exports = { neon: () => sqlTag };

// ⭐ ORDER IS LOAD-BEARING: save.js DESTRUCTURES these at require time, so every
// swap must land BEFORE the require or the real, database-backed paths run.
const walletAuth = require('../api/_lib/wallet-auth.js');
const audit = require('../api/_lib/audit.js');
const maintenance = require('../api/_lib/maintenance.js');

let events = [];          // every logApiEvent row, in order
let rejectRows = [];       // every logAuthReject row
audit.logApiEvent = async (sql, playerId, name, props) => { events.push({ name, props }); };
audit.logAuthReject = async (sql, req, row) => { rejectRows.push(row); };

walletAuth.authenticate = async () => ({ ok: true, identity: 'p1', mode: 'wallet' });

maintenance.enforce = async () => false;                 // never sealed in these cases
maintenance.isClosed = async () => ({ closed: false });
maintenance.noteSealedActivity = async () => {};

const save = require(path.join(__dirname, '..', 'api', 'game', 'save.js'));
const load = require(path.join(__dirname, '..', 'api', 'game', 'load.js'));

// ── Request / response doubles the real applyCors + quietFail can drive ──────

function makeRes() {
    return {
        statusCode: null, body: null, headers: {}, ended: false,
        setHeader(k, v) { this.headers[k] = v; },
        status(c) { this.statusCode = c; return this; },
        json(b) { this.body = b; return this; },
        end() { this.ended = true; return this; },
    };
}

/**
 * A body string plus `readableEnded` hits _lib/http.readBodyExact's parsed-body
 * arm, which is the shape Vercel's Node runtime actually delivers (WO-1453) — the
 * stream arm would hang forever on a plain object.
 */
async function post(body) {
    const raw = JSON.stringify(body);
    const req = {
        method: 'POST', url: '/api/game/save',
        headers: { 'content-type': 'application/json' },
        body: raw, readableEnded: true, complete: true,
    };
    const res = makeRes();
    await save(req, res);
    return res;
}

async function get(query) {
    const req = { method: 'GET', url: '/api/game/load', query: query, headers: {} };
    const res = makeRes();
    await load(req, res);
    return res;
}

function reset() {
    priorRow = null;
    queries = [];
    events = [];
    rejectRows = [];
}

// ── Readers over what the handler actually sent to Postgres ──────────────────

function insertCall() {
    return queries.find(q => /INSERT INTO player_data/.test(q.text)) || null;
}

/** The game_state JSONB parameter, parsed — what would really land in the row. */
function writtenState() {
    const ins = insertCall();
    if (!ins) return null;
    for (const v of ins.values) {
        if (typeof v !== 'string') continue;
        try {
            const parsed = JSON.parse(v);
            if (parsed && typeof parsed === 'object') return parsed;
        } catch (_) { /* not the blob */ }
    }
    return null;
}

function eventNames() { return events.map(e => e.name); }
function eventNamed(name) { return events.find(e => e.name === name) || null; }

// WO-1745 — the 409 refusal moved from logApiEvent('save_reset_refused') to logAuthReject,
// because api/admin/db.js's `view=authrejects` reads only the logAuthReject event names and
// therefore could not see a single one of the old rows.
function rejectNamed(code) { return rejectRows.find(r => r.code === code) || null; }

/**
 * WHAT THE ROW WOULD ACTUALLY HOLD AFTER THE UPSERT.
 *
 * ⛔ `writtenState()` above is what the handler SENT; this is what Postgres would be left
 * WITH, and on the reset arm those are two different objects. The ON CONFLICT clause
 * chooses between `player_data.game_state || EXCLUDED.game_state` (a SHALLOW MERGE onto
 * the stored row) and `EXCLUDED.game_state` (a WHOLESALE REPLACE), on a bound boolean.
 * A test that only read the sent blob could not tell the two apart at all — every
 * assertion would pass under either, which is precisely the near-miss this models around.
 *
 * The choice is read from the bound parameter rather than by emulating SQL, and the
 * CASE expression itself is pinned as a source shape in the case below: the same split
 * every SQL-side property in this repo uses, because there is no Postgres in this runner.
 */
function resultingState() {
    const ins = insertCall();
    if (!ins) return null;
    const incoming = writtenState();
    const replace = ins.values.some(v => v === true);
    const stored = (priorRow && priorRow.game_state) || {};
    return replace ? incoming : Object.assign({}, stored, incoming);
}

// The old town on the server, and the new town the client is trying to save.
// Both shapes the client really posts: the flat legacy key AND the nested
// `resources` block, because applyGuards polices both and a one-shape fixture
// would prove only half the fix.
const OLD_TOWN = {
    crystals: 901, wood: 15, iron: 5, bestWave: 12,
    resources: { crystals: 901, food: 40, coins: 200 },
};
const NEW_TOWN = () => ({
    playerId: 'p1', schemaVersion: 38,
    crystals: 36, wood: 0, iron: 0, bestWave: 0,
    resources: { crystals: 36, food: 0, coins: 0 },
});

function seedOldTown(storedEpoch) {
    priorRow = {
        game_state: OLD_TOWN,
        updated_at: new Date(Date.now() - 60 * 60 * 1000),
        schema_version: 38,
        reset_epoch: storedEpoch === undefined ? null : storedEpoch,
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. A NEWER EPOCH — the declared reset lands
// ═══════════════════════════════════════════════════════════════════════════

test('a NEWER resetEpoch lands the new town\'s balances instead of stripping them', async () => {
    reset();
    seedOldTown(null);                     // never reset before: stored epoch is NULL

    const res = await post(Object.assign(NEW_TOWN(), { resetEpoch: 1 }));

    assert.equal(res.statusCode, 200, 'a declared reset must be accepted');
    const state = writtenState();
    assert.ok(state, 'no INSERT ran — the reset save never reached the row');
    assert.equal(state.crystals, 36,
        'the new town\'s crystals were STRIPPED by implausible_drop — the cloud row keeps ' +
        `the old town (wrote ${JSON.stringify(state.crystals)})`);
    assert.equal(state.resources.crystals, 36,
        'the NESTED crystals were restored to the old value — load would hand back 901');
    assert.equal(state.wood, 0, 'wood 15 -> 0 was refused on a declared reset');
    assert.equal(state.iron, 0, 'iron 5 -> 0 was refused on a declared reset');
    assert.equal(state.bestWave, 0,
        'bestWave was held at the old high-water mark — a reset town starts at wave 0');
    assert.ok(!res.body.rejects, `fields were rejected on a reset: ${JSON.stringify(res.body.rejects)}`);
});

test('a NEWER resetEpoch writes save_reset_accepted and NOT save_sanity_reject', async () => {
    reset();
    seedOldTown(3);

    const res = await post(Object.assign(NEW_TOWN(), { resetEpoch: 4 }));

    assert.equal(res.statusCode, 200);
    assert.ok(!eventNames().includes('save_sanity_reject'),
        'the sanity guard still fired on a declared reset');
    const accepted = eventNamed('save_reset_accepted');
    assert.ok(accepted, `save_reset_accepted was never written (events: ${JSON.stringify(eventNames())})`);
    assert.equal(accepted.props.from, 3, 'the audit row must record the epoch it came FROM');
    assert.equal(accepted.props.to, 4, 'the audit row must record the epoch it moved TO');
    assert.ok(accepted.props.ref, 'the audit row must carry the request ref');
    assert.equal(accepted.props.mode, 'wallet', 'the audit row must record the auth rail');

    // And the epoch ADVANCES — a second save at the same epoch must not bypass again.
    const ins = insertCall();
    assert.ok(ins.values.includes(4),
        `the new epoch was not bound into the write (values: ${JSON.stringify(ins.values)})`);
});

test('a declared reset REPLACES the row — an old-town key absent from the new save does not survive', async () => {
    // ⛔ OWNER'S STANDING RULE: A NEW GAME INHERITS NOTHING (ruling relayed 2026-09-07).
    // Standing the balance guards down is only half the fix. The upsert merges SHALLOWLY
    // (`player_data.game_state || EXCLUDED.game_state`), and the client posts a snapshot
    // with nulls and empties STRIPPED — so every old-town key the new save simply does not
    // carry survives the "new game" and comes back on the next load: the obsidian queue,
    // the army, the base layout, `everBuiltStructureIds` (which gates the blank-town
    // standdown). The row would be a chimera of two towns and would still fail the
    // ticket's acceptance with every balance correct.
    //
    // On a reset-accepted write the incoming blob is AUTHORITATIVE: game_state =
    // EXCLUDED.game_state, wholesale. The merge stays for every ordinary save, where it
    // is what protects a partial/older client from blanking fields it never sent.
    reset();
    priorRow = {
        game_state: Object.assign({}, OLD_TOWN, {
            everBuiltStructureIds: ['barracks', 'foundry'],   // the old town's history
            obsidianQueue: [{ line: 'builder', jobId: 'j-1' }],
            army: { knights: 40 },
        }),
        updated_at: new Date(Date.now() - 60 * 60 * 1000),
        schema_version: 38,
        reset_epoch: 1,
    };

    const res = await post(Object.assign(NEW_TOWN(), { resetEpoch: 2 }));

    assert.equal(res.statusCode, 200);
    const row = resultingState();
    assert.ok(row, 'no INSERT ran');
    assert.equal(row.everBuiltStructureIds, undefined,
        'the old town\'s build history survived the reset — the new game inherited it, and ' +
        'the blank-town standdown will never fire again');
    assert.equal(row.obsidianQueue, undefined, 'the old town\'s queue survived the reset');
    assert.equal(row.army, undefined, 'the old town\'s army survived the reset');
    assert.equal(row.crystals, 36, 'the new town\'s own state must still land');

    // The SQL half: there is no Postgres here, so the expression that makes the choice is
    // pinned as a shape. Both branches named, so a future "simplification" to one or the
    // other is caught rather than silently shipping.
    assert.ok(/CASE\s+WHEN[\s\S]{0,120}THEN\s+EXCLUDED\.game_state[\s\S]{0,120}ELSE\s+player_data\.game_state\s*\|\|\s*EXCLUDED\.game_state/
        .test(SAVE_SRC),
        'the upsert does not choose between REPLACE and MERGE — a reset either inherits ' +
        'the old town or every ordinary save blanks the fields it did not send');
});

test('an ORDINARY save still MERGES — a field the client did not send is not blanked', async () => {
    // The other half of the same ruling, and the reason the merge cannot simply be
    // deleted: a partial or older client posts fewer keys, and on an ordinary save those
    // stored keys must survive. Proven at the same seam so the two arms cannot drift.
    reset();
    priorRow = {
        game_state: Object.assign({}, OLD_TOWN, { army: { knights: 40 } }),
        updated_at: new Date(Date.now() - 60 * 60 * 1000),
        schema_version: 38,
        reset_epoch: 2,
    };

    await post(Object.assign(NEW_TOWN(), { resetEpoch: 2 }));   // EQUAL epoch = ordinary

    const row = resultingState();
    assert.ok(row, 'no INSERT ran');
    assert.deepEqual(row.army, { knights: 40 },
        'an ordinary save wiped a field the client never sent — the merge must stay for ' +
        'every non-reset write');
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. AN EQUAL EPOCH — an ordinary save, guarded exactly as today
// ═══════════════════════════════════════════════════════════════════════════

test('an EQUAL resetEpoch is an ordinary save — the guard applies as today', async () => {
    reset();
    seedOldTown(4);

    const res = await post(Object.assign(NEW_TOWN(), { resetEpoch: 4 }));

    assert.equal(res.statusCode, 200, 'a guarded save still lands its surviving fields');
    assert.ok(res.body.rejects && res.body.rejects.length > 0,
        'the guard stood down for a save that declared NO reset — one epoch bypasses forever');
    const fields = res.body.rejects.map(r => r.field);
    assert.ok(fields.includes('crystals'), `crystals 901 -> 36 was accepted (rejects: ${JSON.stringify(fields)})`);
    assert.ok(fields.includes('bestWave'), 'the bestWave rollback was accepted');
    assert.ok(eventNames().includes('save_sanity_reject'), 'the rejection was not audited');
    assert.ok(!eventNames().includes('save_reset_accepted'),
        'an equal epoch was recorded as a reset');
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. AN OLDER EPOCH — a stale device replaying a dead town
// ═══════════════════════════════════════════════════════════════════════════

test('an OLDER resetEpoch is REFUSED, and nothing is written', async () => {
    reset();
    seedOldTown(5);

    const res = await post(Object.assign(NEW_TOWN(), { resetEpoch: 2 }));

    assert.equal(res.statusCode, 409,
        'a stale replay was ACCEPTED - an old device can overwrite a newer new game');
    assert.equal(res.body.ok, false);
    assert.equal(res.body.code, 'SAVE_RESET_STALE',
        'the refusal must name a stable code the client can branch on');
    assert.equal(insertCall(), null, 'a refused save still wrote to the row');
    // ⛔ WO-1745 — THE ROW MUST LAND WHERE THE ADMIN VIEW CAN READ IT, not merely exist.
    // It used to be written by logApiEvent under the name 'save_reset_refused'; that row was
    // durable and UNREADABLE, because api/admin/db.js filters on the logAuthReject event
    // names. WO-1742 §3 reported "zero refusals on /api/game/save in seven days" off that
    // blind view. Asserting the logApiEvent name here would re-pin the invisible shape.
    const refused = rejectNamed('SAVE_RESET_STALE');
    assert.ok(refused,
        `the refusal was not audited through logAuthReject (rejects: ${JSON.stringify(rejectRows)}, ` +
        `events: ${JSON.stringify(eventNames())})`);
    assert.equal(refused.ref, res.body.ref,
        'the audited ref must be the one the player was handed, or a reported ref resolves to nothing');
    assert.equal(refused.identity, 'p1', 'the row must name WHO is frozen, not just that someone is');
    assert.equal(refused.detail.incoming, 2);
    assert.equal(refused.detail.stored, 5);
    assert.equal(refused.detail.behindBy, 3);
    assert.ok(!eventNames().includes('save_reset_refused'),
        'the refusal was written TWICE — one refusal is one row, or the counts double');
});

// The read half. A row nobody can query is not detection, so the view's filter is pinned by
// source shape the same way the GREATEST() clamp is: there is no Postgres in this runner.
test('view=authrejects can actually SEE a SAVE_RESET_STALE row — both eras', async () => {
    const dbSrc = fs.readFileSync(
        path.join(__dirname, '..', 'api', 'admin', 'db.js'), 'utf8');

    // ⛔ PARSE IT, DO NOT ONLY READ IT. Caught live on 2026-09-15: the first draft of this
    // change put a backtick inside an SQL `--` comment that sits INSIDE a JS template
    // literal, which terminated the query string and made the whole module a SyntaxError.
    // Every source-shape assertion below still passed, because they read the file as text.
    // A view that cannot be loaded is even less readable than one with the wrong filter.
    assert.doesNotThrow(() => require(path.join(__dirname, '..', 'api', 'admin', 'db.js')),
        'api/admin/db.js does not parse — the authrejects view is dead on arrival');

    const inLists = dbSrc.match(/event_name IN \([^)]*\)/g) || [];
    assert.ok(inLists.length >= 4,
        `expected the authrejects view's four event_name filters, found ${inLists.length}`);
    for (const list of inLists) {
        assert.ok(list.includes("'api_auth_reject'"),
            `an authrejects filter lost the live event name: ${list}`);
        assert.ok(list.includes("'save_reset_refused'"),
            `an authrejects filter cannot see the pre-WO-1745 409 rows — ` +
            `the ref lookup and the summary would disagree: ${list}`);
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. NO EPOCH — every client shipped before this. EXACTLY today's behaviour.
// ═══════════════════════════════════════════════════════════════════════════

test('an ABSENT resetEpoch behaves exactly as today — guarded, and no new audit row', async () => {
    reset();
    seedOldTown(2);

    const res = await post(NEW_TOWN());     // no resetEpoch member at all

    assert.equal(res.statusCode, 200);
    assert.ok(res.body.rejects && res.body.rejects.length > 0,
        'an old client without an epoch got the bypass — the guard is off for everyone');
    assert.ok(eventNames().includes('save_sanity_reject'));
    assert.ok(!eventNames().includes('save_reset_accepted'),
        'a version-less client wrote a reset audit row — that is a behaviour change for the field');
    // ⚠ WO-1745 — ASSERT ON THE ROW THAT IS NOW WRITTEN, not the retired name. Left as
    // `!eventNames().includes('save_reset_refused')` this would pass VACUOUSLY forever,
    // since nothing writes that name any more, and the "an absent epoch writes no new audit
    // row" guarantee for every client in the field would quietly stop being pinned.
    assert.ok(!eventNames().includes('save_reset_refused'));
    assert.equal(rejectNamed('SAVE_RESET_STALE'), null,
        'a version-less client was refused and audited — that is a behaviour change for the field');
    const state = writtenState();
    assert.equal(state.crystals, undefined,
        'the stripped flat key was written anyway');
});

test('an ABSENT resetEpoch never lowers the stored epoch', async () => {
    reset();
    seedOldTown(7);

    await post(NEW_TOWN());

    const ins = insertCall();
    assert.ok(ins, 'no INSERT ran');
    // GREATEST() on the upsert is the structural half: whatever this statement binds,
    // the stored epoch cannot go down. Proven as a source shape because there is no
    // Postgres in this runner (the same split test/game.save.schema-version.test.js uses).
    assert.ok(/GREATEST\(\s*player_data\.reset_epoch\s*,\s*EXCLUDED\.reset_epoch\s*\)/.test(SAVE_SRC),
        'the upsert does not clamp reset_epoch monotonically — a stale write could lower it');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. A RESET IS NOT A BLANK CHEQUE — the BOUNDS still run
// ═══════════════════════════════════════════════════════════════════════════

test('a declared reset still cannot land an out-of-bounds balance', async () => {
    reset();
    seedOldTown(null);

    // ⚠ THE IMPOSSIBLE VALUE HAS TO BE IN THE NESTED BLOCK TOO, and finding that out is
    // half the value of this case. buildState's normalise pass (normalizeDeltaFields)
    // promotes `resources.crystals` ONTO the flat `crystals` key, so a fixture that
    // poisoned only the flat one had its 999_999_999_999 quietly replaced by the nested
    // 36 — and then passed for the wrong reason (pre-fix, `crystals` appeared in the
    // rejects as an implausible_drop, not as out_of_bounds). Measured 2026-09-07 by
    // printing the handler's own response.
    const body = Object.assign(NEW_TOWN(), {
        resetEpoch: 1,
        crystals: 999_999_999_999,          // far past MAX_RESOURCE
        resources: { crystals: 999_999_999_999, food: 0, coins: 0 },
        wood: -5,                           // negative
    });
    const res = await post(body);

    assert.equal(res.statusCode, 200);
    const fields = (res.body.rejects || []).map(r => r.field);
    assert.ok(fields.includes('crystals'),
        'resetEpoch was used to land an impossible balance — the BOUNDS must survive a reset');
    assert.ok(fields.includes('wood'), 'a negative balance survived a declared reset');
    const rule = res.body.rejects.find(r => r.field === 'crystals').rule;
    assert.equal(rule, 'out_of_bounds',
        `crystals was rejected as '${rule}', not by the BOUNDS — the case is passing for ` +
        'the wrong reason and proves nothing about a reset');
    const state = writtenState();
    assert.equal(state.crystals, undefined, 'the out-of-bounds crystals were written');
});

test('a reset whose every field is out of bounds does NOT claim the epoch was spent', async () => {
    // ⛔ THE AUDIT ROW MUST NOT LIE. When the guards strip everything, the handler returns
    // 'all fields rejected by guards' WITHOUT writing — so the epoch never advances,
    // because GREATEST() only runs on the upsert. A save_reset_accepted row written above
    // that return would assert a bypass that was never spent, and the next incident would
    // read it as fact.
    reset();
    seedOldTown(null);

    // ⚠ NO `resources` BLOCK. A stripped NESTED field leaves the (now empty) `resources`
    // object behind in the delta, so the payload would not be empty and the early return
    // would never be reached — the case would pass for a reason that has nothing to do
    // with the ordering it exists to pin. One flat guarded key, and nothing else.
    const res = await post({
        playerId: 'p1', schemaVersion: 38, resetEpoch: 1, crystals: -1,
    });

    assert.equal(res.statusCode, 200);
    assert.equal(insertCall(), null, 'a save with nothing left to write still wrote');
    assert.ok(!eventNames().includes('save_reset_accepted'),
        'the epoch was audited as spent on a write that never happened ' +
        `(events: ${JSON.stringify(eventNames())})`);
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. load.js hands the epoch back, so a client can tell it is behind
// ═══════════════════════════════════════════════════════════════════════════

test('/api/game/load returns resetEpoch, null when the column is NULL', async () => {
    reset();
    priorRow = {
        game_state: OLD_TOWN, schema_version: 38,
        updated_at: new Date(), reset_epoch: null,
    };
    const res = await get({ playerId: 'p1' });

    assert.equal(res.statusCode, 200);
    assert.ok('resetEpoch' in res.body,
        'resetEpoch is undefined - JSON.stringify DROPS it, so a client cannot tell it is behind');
    assert.equal(res.body.resetEpoch, null, 'a NULL column must read as an explicit null');
});

test('/api/game/load returns the stored resetEpoch when the row has one', async () => {
    reset();
    priorRow = {
        game_state: OLD_TOWN, schema_version: 38,
        updated_at: new Date(), reset_epoch: 6,
    };
    const res = await get({ playerId: 'p1' });

    assert.equal(res.statusCode, 200);
    assert.equal(res.body.resetEpoch, 6, 'the stored epoch was not returned to the client');
});

test('/api/game/load preserves owned town but never exports local capture recovery intent', async () => {
    reset();
    const ownedBase = {
        baseId: 'captured-iron-bastion', revision: 7,
        captureReceiptId: 'capture-one', milestoneFlags: 7,
        structures: [{ instanceId: 'sold-tower', retired: true, condition01: 0 }],
    };
    priorRow = {
        game_state: {
            ...OLD_TOWN, ownedBase,
            pendingTownCapture: { receiptId: 'device-a-pending' },
            PendingTownCapture: { receiptId: 'legacy-device-a-pending' },
        },
        schema_version: 41, reset_epoch: 6, updated_at: new Date(),
    };
    const before = JSON.stringify(priorRow);
    const res = await get({ playerId: 'p1' });
    assert.equal(res.statusCode, 200);
    assert.deepEqual(res.body.data.ownedBase, ownedBase);
    assert.deepEqual(res.body.data.baseLayout, OLD_TOWN.baseLayout);
    assert.equal(Object.hasOwn(res.body.data, 'pendingTownCapture'), false);
    assert.equal(Object.hasOwn(res.body.data, 'PendingTownCapture'), false);
    assert.equal(res.body.resetEpoch, 6);
    assert.equal(res.body.schemaVersion, 41);
    assert.equal(JSON.stringify(priorRow), before, 'load filtering must not modify the stored record');
});
