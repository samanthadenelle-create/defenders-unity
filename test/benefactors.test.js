'use strict';

// =============================================================================
// test/benefactors.test.js -- the oracle for the Benefactors of the Realm wall
// (WO-1073, owner ruling 2026-08-27).
//
// It pins the four things the ruling actually turns on, each of which was proven
// to fail before it passed:
//   1. $500 FOUNDERS ONLY on the wall, by a PINNED id/threshold pair that is
//      cross-checked against the authored tier table (never derived from it).
//   2. The public wall leaks NO wallet, NO email and NO dollar figure.
//   3. The wall cannot become a spendable grant -- a source lint that scans CODE
//      with COMMENTS STRIPPED, and proves on synthetic input that it still can
//      see a real violation.
//   4. The patron name is player-chosen, capped, filtered, and editable a
//      bounded number of times.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const { PATRONAGE_TIERS } = require('../api/_lib/patronage');
const {
    BenefactorError,
    FOUNDER_THRESHOLD_USD_CENTS,
    FOUNDER_TIER_ID,
    MONUMENT_ASSET_ID_MAX_LEN,
    PLACEHOLDER_MONUMENT_ASSET_ID,
    WALL_DEFAULT_ROWS,
    WALL_MAX_ROWS,
    assertFounderTierPinned,
    assignPatronMonument,
    clampWallLimit,
    monumentsNeedingRepush,
    readBenefactorWall,
    readOwnPatronage,
    resolveMonumentAssetId,
    setPatronName,
} = require('../api/_lib/benefactors');
const {
    MAX_PATRON_NAME_EDITS,
    PATRON_NAME_MAX_LEN,
    PATRON_NAME_MIN_LEN,
    PatronNameError,
    validatePatronName,
} = require('../api/_lib/patron-name');

const REPO = path.join(__dirname, '..');
const readSrc = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');

// A recording tagged-template sql() that answers from a queued script, so every
// query the code issues is both DRIVEN and INSPECTED.
function mockSql(script) {
    const calls = [];
    const queue = script.slice();
    const sql = async (strings, ...values) => {
        const text = strings.join('?');
        calls.push({ text, values });
        const next = queue.shift();
        if (next && next.throws) throw next.throws;
        return next ? next.rows : [];
    };
    sql.calls = calls;
    return sql;
}

const lifetime = (usd) => ({ rows: [{ lifetime_usd: usd }] });

// -- 1. WHO IS ON THE WALL ---------------------------------------------------

test('the wall tier is a PINNED id and threshold, cross-checked against the authored table', () => {
    // Pinned as literals on purpose. Expressing this as "the last tier" or
    // "PATRONAGE_TIERS.length - 1" would silently re-point the whole wall the
    // day a fourth tier is authored.
    assert.equal(FOUNDER_TIER_ID, 'founder_benefactor');
    assert.equal(FOUNDER_THRESHOLD_USD_CENTS, 50000);

    // VALUE EQUALITY IS NOT ENOUGH HERE, and that gap was measured: a mutant
    // that rewrote both constants as PATRONAGE_TIERS[length - 1] passed every
    // other assertion in this file, because today the last tier IS the founder
    // tier. It stops being true the day a fourth tier is authored -- which the
    // owner's evidence gate explicitly contemplates. So the SHAPE is pinned too.
    const src = stripComments(readSrc('api/_lib/benefactors.js'));
    assert.match(src, /const FOUNDER_TIER_ID = 'founder_benefactor';/);
    assert.match(src, /const FOUNDER_THRESHOLD_USD_CENTS = 50000;/);
    assert.deepEqual(allMatches(src, /PATRONAGE_TIERS\s*\[|PATRONAGE_TIERS\.length/g), [],
        'the wall must never be addressed by POSITION in the tier table');

    const authored = assertFounderTierPinned();
    assert.equal(authored.id, 'founder_benefactor');
    assert.equal(authored.thresholdUsdCents, 50000);

    // And the $50 / $150 rungs still exist and are still BELOW the wall.
    const byId = Object.fromEntries(PATRONAGE_TIERS.map(t => [t.id, t.thresholdUsdCents]));
    assert.equal(byId.patron, 5000);
    assert.equal(byId.high_patron, 15000);
    assert.ok(byId.patron < FOUNDER_THRESHOLD_USD_CENTS);
    assert.ok(byId.high_patron < FOUNDER_THRESHOLD_USD_CENTS);

    // The evidence gate: no tier above $500 exists yet.
    assert.equal(PATRONAGE_TIERS.some(t => t.thresholdUsdCents > 50000), false);
});

test('the wall query lists ONLY the founder tier, in founding order', async () => {
    const sql = mockSql([{ rows: [] }]);
    await readBenefactorWall(sql);
    const q = sql.calls[0];

    assert.match(q.text, /FROM patronage_benefactors/);
    assert.match(q.text, /WHERE tier_id = \?/);
    assert.deepEqual(q.values[0], FOUNDER_TIER_ID);
    assert.match(q.text, /ORDER BY granted_at ASC/);
    // $50 and $150 are ruled OFF the wall; no other tier id may be referenced.
    assert.doesNotMatch(q.text, /high_patron|'patron'/);
});

test('$50 Patron and $150 High Patron can never be listed by this module', () => {
    const code = stripComments(readSrc('api/_lib/benefactors.js'));
    const sites = allMatches(code, /\bhigh_patron\b|['"]patron['"]/g);
    assert.deepEqual(sites, [], 'a non-founder tier id appears in wall code');

    // And the database refuses them independently of the code.
    const schema = readSrc('api/schema.sql');
    assert.match(schema, /CHECK \(tier_id IN \('founder_benefactor'\)\)/);
});

// -- 2. WHAT THE PUBLIC READ MAY CONTAIN -------------------------------------

test('the public wall carries a name and a place in line, and nothing else', async () => {
    const sql = mockSql([{ rows: [
        { patron_name: 'Wardens of Ashen Vale', granted_at: '2026-08-27T10:00:00.000Z' },
        { patron_name: 'Maren the Steadfast', granted_at: '2026-08-28T11:30:00.000Z' },
    ] }]);
    const wall = await readBenefactorWall(sql);

    assert.equal(wall.tierId, FOUNDER_TIER_ID);
    assert.equal(wall.count, 2);
    assert.deepEqual(wall.benefactors.map(b => b.ordinal), [1, 2]);
    assert.deepEqual(Object.keys(wall.benefactors[0]).sort(),
        ['foundedOn', 'monumentAssetId', 'monumentIsBespoke', 'ordinal', 'patronName']);
    // A founding DATE is honour; a timestamp is a fingerprint.
    assert.equal(wall.benefactors[0].foundedOn, '2026-08-27');

    // The GOOD path, asserted whole.
    assert.deepEqual(wall.benefactors[1], {
        ordinal: 2, patronName: 'Maren the Steadfast', foundedOn: '2026-08-28',
        monumentAssetId: PLACEHOLDER_MONUMENT_ASSET_ID, monumentIsBespoke: false,
    });
});

test('the public wall never emits a wallet, an email or a dollar figure', async () => {
    const sql = mockSql([{ rows: [
        { patron_name: 'Ashfall', granted_at: '2026-08-27T10:00:00.000Z',
          // A driver that one day returns extra columns must not be able to
          // widen the response by accident.
          wallet: '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU',
          usd_anchor: '500.0000' },
    ] }]);
    const wall = await readBenefactorWall(sql);
    const json = JSON.stringify(wall);

    assert.doesNotMatch(json, /wallet|usd|cent|lifetime|@|price|amount/i);
    assert.equal('wallet' in wall.benefactors[0], false);

    // The SELECT list itself never asks for the address.
    assert.match(sql.calls[0].text, /SELECT patron_name, granted_at/);
});

test('the wall limit is clamped at both ends, on pinned values', () => {
    assert.equal(WALL_DEFAULT_ROWS, 50);
    assert.equal(WALL_MAX_ROWS, 200);
    assert.equal(clampWallLimit(undefined), 50);
    assert.equal(clampWallLimit('not a number'), 50);
    assert.equal(clampWallLimit('10'), 10);
    assert.equal(clampWallLimit(0), 1);
    assert.equal(clampWallLimit(-5), 1);
    assert.equal(clampWallLimit(9999), 200);
});

// -- 3. GRANTED, NEVER PURCHASED ---------------------------------------------

test('a wallet one cent short of $500 is refused the wall and NOTHING is written', async () => {
    const sql = mockSql([lifetime('499.99')]);
    const result = await setPatronName(sql, 'wallet-A', 'Almost There');

    assert.deepEqual(result, { ok: false, error: BenefactorError.NOT_ELIGIBLE });
    assert.equal(sql.calls.length, 1, 'a refused caller must cost exactly one read');
    assert.equal(sql.calls.some(c => /INSERT|UPDATE/i.test(c.text)), false);
});

test('exactly $500.00 earns the wall, and the row is written as tier founder', async () => {
    const sql = mockSql([lifetime('500.00'), { rows: [] }, { rows: [] }]);
    const result = await setPatronName(sql, 'wallet-A', 'Wardens of Ashen Vale');

    assert.deepEqual(result, {
        ok: true,
        patronName: 'Wardens of Ashen Vale',
        onWall: true,
        wasEdit: false,
        nameEditsRemaining: MAX_PATRON_NAME_EDITS,
    });

    const write = sql.calls[2];
    assert.match(write.text, /INSERT INTO\s+patronage_benefactors/);
    assert.deepEqual(write.values, ['wallet-A', FOUNDER_TIER_ID, 'Wardens of Ashen Vale']);
    // The tier is the SERVER's, derived from settled purchases on this very call.
    assert.match(sql.calls[0].text, /SUM\(usd_anchor\)[\s\S]*FROM purchase_entitlements/);
});

test('the client cannot hand in a tier, an amount or an entitlement', () => {
    // The only inputs are a connection, an address and a string.
    assert.equal(setPatronName.length, 3);

    const code = stripComments(readSrc('api/patronage/name.js'));
    // The handler forwards exactly two fields off the wire.
    const wireReads = allMatches(code, /body\.[A-Za-z_]+/g);
    assert.deepEqual([...new Set(wireReads)].sort(), ['body.patronName', 'body.wallet']);
    // and it never writes to the database itself.
    assert.deepEqual(allMatches(code, /INSERT\s+INTO|UPDATE\s+[a-z_]+\s+SET|DELETE\s+FROM/gi), []);
});

test('the wall module writes to exactly one table and never to the money table', () => {
    const code = flatten(stripComments(readSrc('api/_lib/benefactors.js')));

    // EVERY write, each with its target named -- not "the first one".
    assert.deepEqual(allMatches(code, /INSERT INTO ([a-z_]+)/gi),
        ['INSERT INTO patronage_benefactors']);
    // The monument assignment is a real UPDATE now, so the assertion is that
    // every UPDATE names the WALL table and nothing else. ('ON CONFLICT (wallet)
    // DO UPDATE SET' is the tail of the insert and names no table, by design.)
    assert.deepEqual(allMatches(code, /UPDATE [a-z_]+ SET/gi),
        ['UPDATE patronage_benefactors SET']);
    assert.deepEqual(allMatches(code, /DELETE FROM ([a-z_]+)/gi), []);
    // purchase_entitlements is READ and never written: it is the money.
    assert.deepEqual(allMatches(code, /(INSERT INTO|UPDATE|DELETE FROM) purchase_entitlements/gi), []);
});

// -- 4. COSMETIC ONLY, PROVEN BY A LINT THAT DECIDES ABOUT COMMENTS -----------

// THE DELIBERATE DECISION: THIS LINT READS CODE, NOT COMMENTS.
//
// Both directions are wrong by default, so it is chosen rather than defaulted:
//   * Counting comments would fail on the very sentence that documents the
//     invariant ("there is deliberately no currency here"), which punishes the
//     documentation and gets the lint neutered by deleting the explanation.
//   * Ignoring comments could hide a violation behind a commented-out line --
//     but a commented-out grant grants nothing, and the moment it is uncommented
//     it becomes code and this lint sees it.
// The stripper is proven on synthetic input below, in BOTH directions, so this
// is not a hollow pass.
function stripComments(src) {
    let out = '';
    let i = 0;
    let quote = null;
    while (i < src.length) {
        const c = src[i];
        const d = src[i + 1];
        if (quote) {
            if (c === '\\') { out += '  '; i += 2; continue; }
            if (c === quote) quote = null;
            out += c; i += 1; continue;
        }
        if (c === '"' || c === "'" || c === '`') { quote = c; out += c; i += 1; continue; }
        if (c === '/' && d === '/') {
            while (i < src.length && src[i] !== '\n') { out += ' '; i += 1; }
            continue;
        }
        if (c === '/' && d === '*') {
            while (i < src.length && !(src[i] === '*' && src[i + 1] === '/')) {
                out += src[i] === '\n' ? '\n' : ' '; i += 1;
            }
            out += '  '; i += 2; continue;
        }
        out += c; i += 1;
    }
    return out;
}

/** Collapse whitespace so a multi-line SQL template reads as one statement. */
function flatten(src) {
    return src.replace(/\s+/g, ' ');
}

/** Blank out string BODIES, keeping the quotes -- for lints about IDENTIFIERS. */
function stripStrings(src) {
    return src.replace(/'[^'\n]*'/g, "''")
              .replace(/"[^"\n]*"/g, '""')
              .replace(/`[^`]*`/g, '``');
}

/** SQL line comments, stripped by the same chosen policy as the JS stripper. */
function stripSqlComments(src) {
    return src.replace(/--[^\r\n]*/g, '');
}

/** Every offending site, never just the first -- a one-site report hides the rest. */
function allMatches(text, re) {
    const found = [];
    let m;
    const rx = new RegExp(re.source, re.flags.includes('g') ? re.flags : re.flags + 'g');
    while ((m = rx.exec(text)) !== null) {
        found.push(m[0]);
        if (m.index === rx.lastIndex) rx.lastIndex += 1;
    }
    return found;
}

test('the comment stripper is proven in BOTH directions before it is trusted', () => {
    const commentOnly = '// this module never grants a crystal\nconst a = 1;\n';
    const inCode = 'const reward = { crystal: 5 };\n';
    const inString = 'const k = "crystal";\n';

    assert.doesNotMatch(stripComments(commentOnly), /crystal/,
        'comments must be invisible to the lint -- that is the chosen policy');
    assert.match(stripComments(inCode), /crystal/,
        'a real violation must remain visible, or the lint is hollow');
    assert.match(stripComments(inString), /crystal/,
        'a string literal is code, not commentary');
    // Line count is preserved so a future failure can name a line.
    assert.equal(stripComments(commentOnly).split('\n').length, commentOnly.split('\n').length);
});

test('nothing in the patronage wall surface can grant a spendable, a timer or a slot', () => {
    const SPENDABLE = new RegExp([
        'crystal', 'currency', 'coin', 'gem', 'lamport',
        'resource', 'wood', 'stone', 'iron', 'food', 'magic',
        'timer', 'cooldown', 'tempo', 'slot', 'inventory',
        'balance', 'reward', 'refund', 'stockpile', 'capacity',
    ].join('|'), 'gi');

    const surface = [
        'api/_lib/benefactors.js',
        'api/_lib/patron-name.js',
        'api/patronage/benefactors.js',
        'api/patronage/name.js',
    ];
    const offences = [];
    for (const rel of surface) {
        for (const hit of allMatches(stripComments(readSrc(rel)), SPENDABLE)) {
            offences.push(rel + ': ' + hit);           // EVERY site, from every file
        }
    }
    assert.deepEqual(offences, []);
});

// -- 5. THE PLAYER-CHOSEN NAME -----------------------------------------------

test('the patron name is capped, and the cap is a pinned number', () => {
    assert.equal(PATRON_NAME_MIN_LEN, 3);
    assert.equal(PATRON_NAME_MAX_LEN, 24);

    assert.deepEqual(validatePatronName('W'.repeat(24), {}),
        { ok: true, patronName: 'W'.repeat(24) });
    assert.equal(validatePatronName('x'.repeat(25), {}).error, PatronNameError.TOO_LONG);
    assert.equal(validatePatronName('ab', {}).error, PatronNameError.TOO_SHORT);
    assert.equal(validatePatronName(null, {}).error, PatronNameError.TOO_SHORT);
});

test('the name can never be an address, an email, or a unicode lookalike', () => {
    const wallet = '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU';

    // The obvious move: publish your own address.
    assert.equal(validatePatronName('7xKXtg2CW87d', { wallet }).error,
        PatronNameError.RESEMBLES_WALLET);
    // An email shape cannot even be typed -- '@' is not in the charset.
    assert.equal(validatePatronName('me@example.com', { wallet }).error,
        PatronNameError.INVALID_CHARS);
    // Homoglyph / zero-width / RTL-override impersonation is unexpressible.
    // DELIBERATE NON-ASCII, the only two bytes in this file: the 'e' in the
    // first string is CYRILLIC U+0435 and the second string carries a
    // ZERO-WIDTH SPACE U+200B. They are the ATTACK, not a typo -- an
    // ascii-cleanup pass that "fixes" them silently deletes this test's teeth.
    assert.equal(validatePatronName('Marеn', { wallet }).error,
        PatronNameError.INVALID_CHARS);
    assert.equal(validatePatronName('Maren​the', { wallet }).error,
        PatronNameError.INVALID_CHARS);
    // Padding to sort to the top of the wall, and punctuation runs.
    assert.equal(validatePatronName('___Maren', { wallet }).error, PatronNameError.INVALID_CHARS);
    assert.equal(validatePatronName('Maren--the', { wallet }).error, PatronNameError.INVALID_CHARS);
    // A short coincidence with the address is NOT a rejection (the good path).
    assert.deepEqual(validatePatronName('Kheta', { wallet }), { ok: true, patronName: 'Kheta' });
});

test('profanity and impersonation are both refused, on the normalised form', () => {
    assert.equal(validatePatronName('b1tch Lord', {}).error, PatronNameError.REJECTED);
    assert.equal(validatePatronName('Official Support', {}).error, PatronNameError.REJECTED);
    assert.equal(validatePatronName('Elarion Staff', {}).error, PatronNameError.REJECTED);
    assert.equal(validatePatronName('The Developer', {}).error, PatronNameError.REJECTED);
    // And an ordinary name still gets through.
    assert.deepEqual(validatePatronName('House of the Silver Ash', {}),
        { ok: true, patronName: 'House of the Silver Ash' });
});

// -- 6. THE EDIT PATH, DECIDED ON PURPOSE ------------------------------------

test('the name is editable a bounded number of times, then it is a support decision', async () => {
    assert.equal(MAX_PATRON_NAME_EDITS, 3);

    // Third edit: allowed, and it is the last one.
    const third = mockSql([lifetime('500.00'),
        { rows: [{ patron_name: 'Old Name', name_edits_used: 2 }] }, { rows: [] }]);
    const ok = await setPatronName(third, 'wallet-A', 'New Name');
    assert.equal(ok.ok, true);
    assert.equal(ok.wasEdit, true);
    assert.equal(ok.nameEditsRemaining, 0);
    assert.match(third.calls[2].text, /name_edits_used = patronage_benefactors\.name_edits_used \+ 1/);

    // Fourth: refused, and nothing is written.
    const fourth = mockSql([lifetime('500.00'),
        { rows: [{ patron_name: 'Old Name', name_edits_used: 3 }] }]);
    const denied = await setPatronName(fourth, 'wallet-A', 'Another Name');
    assert.deepEqual(denied, { ok: false, error: PatronNameError.EDITS_EXHAUSTED });
    assert.equal(fourth.calls.length, 2);
});

test('re-submitting the identical name is a no-op and never burns an edit', async () => {
    const sql = mockSql([lifetime('500.00'),
        { rows: [{ patron_name: 'Maren the Steadfast', name_edits_used: 1 }] }]);
    const result = await setPatronName(sql, 'wallet-A', 'Maren the Steadfast');

    assert.equal(result.ok, true);
    assert.equal(result.wasEdit, false);
    assert.equal(result.nameEditsRemaining, MAX_PATRON_NAME_EDITS - 1);
    assert.equal(sql.calls.length, 2, 'a retried request must not write');
});

test('two founders cannot share a name -- the unique index decides, not the code', async () => {
    const collision = Object.assign(new Error('duplicate key'), { code: '23505' });
    const sql = mockSql([lifetime('750.00'), { rows: [] }, { throws: collision }]);
    const result = await setPatronName(sql, 'wallet-B', 'Maren the Steadfast');
    assert.deepEqual(result, { ok: false, error: PatronNameError.TAKEN });

    const schema = readSrc('api/schema.sql');
    assert.match(schema, /CREATE UNIQUE INDEX IF NOT EXISTS uq_patronage_benefactors_name_ci/);
});

// -- 7. THE OWN-STATUS READ --------------------------------------------------

test('a founder who has not chosen a name is eligible but is not published', async () => {
    const sql = mockSql([lifetime('500.00'), { rows: [] }]);
    const status = await readOwnPatronage(sql, 'wallet-A');

    assert.deepEqual(status, {
        tierId: 'founder_benefactor',
        tierLabel: 'Founder / Benefactor',
        wallEligible: true,
        onWall: false,
        patronName: null,
        nameEditsRemaining: MAX_PATRON_NAME_EDITS,
        monumentAssetId: PLACEHOLDER_MONUMENT_ASSET_ID,
        monumentIsBespoke: false,
    });
    // Choosing the name IS the consent to be published.
    assert.doesNotMatch(JSON.stringify(status), /cent|usd|lifetime/i);
});

test('a High Patron sees their tier and is told the wall is not theirs', async () => {
    const sql = mockSql([lifetime('150.00'), { rows: [] }]);
    const status = await readOwnPatronage(sql, 'wallet-C');
    assert.equal(status.tierId, 'high_patron');
    assert.equal(status.wallEligible, false);
    assert.equal(status.onWall, false);
});

// -- 8. THE ENDPOINTS --------------------------------------------------------

test('the wall read is public and the name write is wallet-signed', () => {
    const wall = readSrc('api/patronage/benefactors.js');
    const name = readSrc('api/patronage/name.js');

    // Public, GET, CORS-answered -- every kingdom reads the same list.
    assert.match(wall, /applyCors\(req, res, 'GET, OPTIONS'\)/);
    assert.doesNotMatch(stripComments(wall), /wallet-auth|verifyAndConsume|verifyWallet/);

    // Signed, POST, exact bytes.
    assert.match(name, /require\('\.\.\/_lib\/wallet-auth'\)/);
    assert.match(name, /verifyAndConsume\(sql, req\.headers, rawBody, wallet\)/);
    // config must be assigned AFTER module.exports = handler, or the runtime
    // parser is never actually disabled (the save.js hang, _lib/http.js).
    const exportsAt = name.indexOf('module.exports = handler;');
    const configAt = name.indexOf('module.exports.config');
    assert.ok(exportsAt > 0 && configAt > exportsAt);
});

test('no endpoint can print a wallet, a name or a body into a log line', () => {
    for (const rel of ['api/patronage/benefactors.js', 'api/patronage/name.js']) {
        const code = stripComments(readSrc(rel));
        // Strings are stripped HERE and only here: '[patronage/name] body read
        // error' is a fixed TAG, not a value, and a lint that cannot tell a tag
        // from an identifier reports noise until somebody switches it off.
        const logs = allMatches(stripStrings(code), /console\.(log|error|warn)\([^)]*\)/g);
        assert.ok(logs.length > 0, rel + ' has no logging at all');
        const leaks = logs.filter(l => /\b(wallet|patronName|rawBody|body|auth)\b/.test(l));
        assert.deepEqual(leaks, [], rel + ' logs production identity');
    }
});

// -- 9. SCHEMA AND MIGRATION -------------------------------------------------

test('the table is declared, and the migration that provisions it is additive only', () => {
    const schema = readSrc('api/schema.sql');
    assert.match(schema, /CREATE TABLE IF NOT EXISTS patronage_benefactors/);
    for (const col of ['wallet', 'tier_id', 'patron_name', 'patron_name_ci',
                       'name_edits_used', 'granted_at', 'name_updated_at', 'updated_at']) {
        assert.match(schema, new RegExp('\\n\\s+' + col + '\\s+[A-Z]'), 'missing column ' + col);
    }
    // No money column on the wall table -- it is status, not a ledger.
    const table = /CREATE TABLE IF NOT EXISTS patronage_benefactors \(([\s\S]*?)\n\);/.exec(schema)[1];
    assert.doesNotMatch(table, /usd|amount|lamport|currency|price/i);

    // SQL comments fall to the same deliberate policy as JS comments: the
    // migration's own header PROMISES 'zero DROP, DELETE or TRUNCATE', and a
    // lint that fails on the promise would force the promise to be deleted.
    const migration = stripSqlComments(
        readSrc('api/migrations/20260827_0003_patronage_benefactors.sql'));
    assert.deepEqual(allMatches(migration, /\bDROP\b|\bDELETE\b|\bTRUNCATE\b/gi), []);
    assert.match(migration, /CREATE TABLE IF NOT EXISTS patronage_benefactors/);
    // The verify instruction lives in the header, so it is read UNSTRIPPED --
    // the same file, deliberately read two ways for two different questions.
    assert.match(readSrc('api/migrations/20260827_0003_patronage_benefactors.sql'),
        /SCHEMA_PARITY_OK/);
});

test('the landed architecture is built on, not re-implemented', () => {
    const code = stripComments(readSrc('api/_lib/benefactors.js'));
    assert.match(code, /require\('\.\/patronage'\)/);
    // The aggregate is NOT recomputed here -- there is one lifetime query in the
    // codebase and it lives in the module that landed at cb57b1a41.
    assert.deepEqual(allMatches(code, /SUM\(usd_anchor\)/gi), []);
});

// -- 10. THE BESPOKE MONUMENT, PER PATRON ------------------------------------

test('the stand-in id is a PINNED literal, never derived from a list', () => {
    assert.equal(PLACEHOLDER_MONUMENT_ASSET_ID, 'monument_founder_standin');
    assert.equal(MONUMENT_ASSET_ID_MAX_LEN, 64);

    // The same shape pin as the founder tier, for the same reason and after the
    // same near-miss: a placeholder read out of "the first entry" of any list
    // silently re-points every un-collaborated founder the day that list moves.
    const src = stripComments(readSrc('api/_lib/benefactors.js'));
    assert.match(src, /const PLACEHOLDER_MONUMENT_ASSET_ID = 'monument_founder_standin';/);
    assert.deepEqual(allMatches(src, /PLACEHOLDER_MONUMENT_ASSET_ID = [^']/g), []);
    assert.deepEqual(allMatches(src, /MONUMENT[A-Z_]* = [A-Za-z_]+\s*\[|\.length - 1/g), []);
});

test('an empty monument column means the stand-in, and nothing else does', () => {
    assert.equal(resolveMonumentAssetId(null), PLACEHOLDER_MONUMENT_ASSET_ID);
    assert.equal(resolveMonumentAssetId(undefined), PLACEHOLDER_MONUMENT_ASSET_ID);
    assert.equal(resolveMonumentAssetId('   '), PLACEHOLDER_MONUMENT_ASSET_ID);
    // The GOOD path: a bespoke key is returned untouched.
    assert.equal(resolveMonumentAssetId('monument_wardens_ashen_vale'),
        'monument_wardens_ashen_vale');

    // And the database refuses to store the stand-in id, so "placeholder" can
    // never be spelled two ways and drift apart.
    assert.match(readSrc('api/schema.sql'),
        /CHECK \(monument_asset_id <> 'monument_founder_standin'\)/);
});

test('the wall is PER PATRON, not one global phase', async () => {
    const sql = mockSql([{ rows: [
        { patron_name: 'Founder A', granted_at: '2026-08-27T10:00:00.000Z',
          monument_asset_id: 'monument_founder_a_oath' },
        { patron_name: 'Founder B', granted_at: '2026-08-28T10:00:00.000Z',
          monument_asset_id: null },
    ] }]);
    const wall = await readBenefactorWall(sql);

    // A carries their own bespoke FBX; B is still on the stand-in. One list, two
    // states, at the same instant -- that is the ruling.
    assert.equal(wall.benefactors[0].monumentAssetId, 'monument_founder_a_oath');
    assert.equal(wall.benefactors[0].monumentIsBespoke, true);
    assert.equal(wall.benefactors[1].monumentAssetId, PLACEHOLDER_MONUMENT_ASSET_ID);
    assert.equal(wall.benefactors[1].monumentIsBespoke, false);

    // Still no identity and still no money on the public read.
    assert.doesNotMatch(JSON.stringify(wall), /wallet|usd|cent|@/i);
    assert.match(sql.calls[0].text, /SELECT patron_name, granted_at, monument_asset_id/);
});

// -- 11. THE PUSH THAT MUST NOT BE FORGOTTEN (CLAUDE.md section 16) -----------

test('a monument CANNOT be assigned without a presence proof -- omitting it throws', async () => {
    const sql = mockSql([]);
    await assert.rejects(
        () => assignPatronMonument(sql, 'wallet-A', 'monument_founder_a_oath', {}),
        /verifyAssetPresent is REQUIRED/);
    await assert.rejects(
        () => assignPatronMonument(sql, 'wallet-A', 'monument_founder_a_oath'),
        /verifyAssetPresent is REQUIRED/);
    // Nothing was read and nothing was written on the way to that refusal.
    assert.equal(sql.calls.length, 0);
});

test('an unpublished monument is REFUSED and never written', async () => {
    const sql = mockSql([{ rows: [{ patron_name: 'Founder A' }] }]);
    const probed = [];
    const result = await assignPatronMonument(sql, 'wallet-A', 'monument_never_pushed', {
        verifyAssetPresent: async (id) => {
            probed.push(id);
            return { present: false, source: 'r2:ServerData/Android' };
        },
    });

    assert.deepEqual(result, {
        ok: false,
        error: BenefactorError.MONUMENT_NOT_PUBLISHED,
        source: 'r2:ServerData/Android',
    });
    assert.deepEqual(probed, ['monument_never_pushed']);
    assert.equal(sql.calls.some(c => /UPDATE/i.test(c.text)), false,
        'a monument nobody can see must never be recorded as assigned');
});

test('a probe that answers anything other than present:true is a refusal', async () => {
    for (const answer of [null, undefined, {}, { present: 'yes' }, { present: 1 }]) {
        const sql = mockSql([{ rows: [{ patron_name: 'Founder A' }] }]);
        const result = await assignPatronMonument(sql, 'wallet-A', 'monument_a', {
            verifyAssetPresent: async () => answer,
        });
        assert.equal(result.ok, false, JSON.stringify(answer));
        assert.equal(result.error, BenefactorError.MONUMENT_NOT_PUBLISHED);
        assert.equal(sql.calls.some(c => /UPDATE/i.test(c.text)), false);
    }
});

test('THE SUCCESS PATH: a published monument is assigned and its proof is dated', async () => {
    const sql = mockSql([{ rows: [{ patron_name: 'Founder A' }] }, { rows: [] }]);
    const result = await assignPatronMonument(sql, 'wallet-A', 'monument_wardens_ashen_vale', {
        verifyAssetPresent: async () => ({ present: true, source: 'r2:ServerData/Android' }),
    });

    assert.deepEqual(result, {
        ok: true,
        monumentAssetId: 'monument_wardens_ashen_vale',
        source: 'r2:ServerData/Android',
    });
    const write = sql.calls[1];
    assert.match(write.text, /UPDATE\s+patronage_benefactors/);
    assert.match(write.text, /monument_verified_at = NOW\(\)/);
    assert.deepEqual(write.values, ['monument_wardens_ashen_vale', 'wallet-A']);
});

test('a patron not on the wall cannot be given a monument', async () => {
    const sql = mockSql([{ rows: [] }]);
    let probed = false;
    const result = await assignPatronMonument(sql, 'wallet-Z', 'monument_a_oath', {
        verifyAssetPresent: async () => { probed = true; return { present: true }; },
    });
    assert.deepEqual(result, { ok: false, error: BenefactorError.NOT_ON_WALL });
    assert.equal(probed, false, 'do not probe the bucket for a patron who is not there');
});

test('the stand-in can never be assigned, and a malformed key is refused', async () => {
    const verifyAssetPresent = async () => ({ present: true });
    const sql = () => { throw new Error('must not reach the database'); };

    const placeholder = await assignPatronMonument(
        sql, 'wallet-A', PLACEHOLDER_MONUMENT_ASSET_ID, { verifyAssetPresent });
    assert.deepEqual(placeholder, { ok: false, error: BenefactorError.MONUMENT_IS_PLACEHOLDER });

    for (const bad of ['Monument_A', 'monument a', 'monument/a', 'monument_a.fbx',
                       'Assets/Art/monument.fbx', 'ab', 'm'.repeat(65)]) {
        const r = await assignPatronMonument(sql, 'wallet-A', bad, { verifyAssetPresent });
        assert.equal(r.error, BenefactorError.MONUMENT_ID_INVALID, bad);
    }
});

test('every proof older than the newest content build is reported as needing a push', async () => {
    // Bundle names are CONTENT-HASHED, so a content build invalidates every
    // earlier proof at once. A one-time check at assignment is necessary and NOT
    // sufficient, and this is the query that says so out loud.
    const sql = mockSql([{ rows: [
        { monument_asset_id: 'monument_a_oath', monument_verified_at: '2026-08-20T00:00:00.000Z' },
        { monument_asset_id: 'monument_b_vigil', monument_verified_at: null },
    ] }]);
    const stale = await monumentsNeedingRepush(sql, '2026-08-27T00:00:00.000Z');

    assert.deepEqual(stale, ['monument_a_oath', 'monument_b_vigil']);
    const q = sql.calls[0];
    assert.match(q.text, /monument_verified_at IS NULL OR monument_verified_at < \?/);
    assert.deepEqual(q.values, ['2026-08-27T00:00:00.000Z']);
    // Asset ids only -- the ship chain prints this, and it must print no identity.
    assert.doesNotMatch(q.text, /patron_name|wallet/);

    // A missing build stamp must THROW, never quietly report "all current".
    await assert.rejects(() => monumentsNeedingRepush(sql), /contentBuildIso is required/);
    await assert.rejects(() => monumentsNeedingRepush(sql, ''), /contentBuildIso is required/);
});


test('SQL comment lint handles CRLF without hiding destructive statements', () => {
    for (const eol of ['\n', '\r\n']) {
        const sql = '-- promise: no DROP DELETE TRUNCATE' + eol
            + 'CREATE TABLE example (id TEXT);' + eol
            + 'DROP TABLE example; -- actual destructive statement' + eol;
        assert.deepEqual(allMatches(stripSqlComments(sql), /\bDROP\b|\bDELETE\b|\bTRUNCATE\b/gi), ['DROP']);
    }
});
