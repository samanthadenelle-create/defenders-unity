// =============================================================================
// test/vfx-pick-options.test.js - WO-1348. THE ORACLE for the VFX pick option pool.
// -----------------------------------------------------------------------------
// The Command Center can now re-point a VFX key without a rebuild. The value it
// writes is an INTEGER, because the tunables rail is int-only, and that integer has
// to mean the SAME EFFECT next month as it means today. Everything below exists to
// keep that true, because the failure it prevents is SILENT: a shifted id re-points
// an effect while the trace still says "override applied", and nothing on screen or
// in the log says otherwise.
//
// What is pinned:
//   1. the two checked-in copies are byte-identical AND freshly derivable
//   2. ids are stable across a regenerate, and a removed key's id is RESERVED
//   3. the pool is joined into all three halves of the manifest join
//   4. every realm.vfx.* knob ships at 0 = the build-time pick (the invariant)
//   5. the served page can never offer an id the pool does not carry
//
//     node --test test/vfx-pick-options.test.js
//
// Zero network, zero database, zero Unity. Node built-ins only.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const REPO = path.resolve(__dirname, '..');

const API_JSON = 'api/_lib/vfx-pick-options.generated.json';
const RES_JSON = 'Assets/Resources/VFX/vfx-pick-options.generated.json';
const PICKS_JSON = 'Assets/Editor/VfxManualPicks.json';
const REGISTRY_CS = 'Assets/_Modules/Core/Ops/RemoteTunables.cs';

const manifestLib = require('../api/_lib/tunable-manifest');
const { TUNABLE_KEYS } = require('../api/_lib/tunables');

function read(rel) { return fs.readFileSync(path.join(REPO, rel), 'utf8'); }
function readBytes(rel) { return fs.readFileSync(path.join(REPO, rel)); }

let gen = null;
async function generator() {
    if (!gen) gen = await import('../tools/gen-vfx-pick-options.mjs');
    return gen;
}

// -- 1. THE TWO COPIES ARE ONE FILE -------------------------------------------

test('both checked-in copies are byte-identical to each other', () => {
    const a = readBytes(API_JSON);
    const b = readBytes(RES_JSON);
    assert.equal(a.length, b.length,
        'CONSOLE COPY vs BUILD COPY: ' + API_JSON + ' is ' + a.length + ' bytes and ' + RES_JSON +
        ' is ' + b.length + '. The server reads one and the game reads the other; they must be ' +
        'the same file. Run: node tools/gen-vfx-pick-options.mjs');
    assert.ok(a.equals(b),
        'CONSOLE COPY vs BUILD COPY: same length, different bytes. The Command Center would offer ' +
        'a pick the build resolves differently. Run: node tools/gen-vfx-pick-options.mjs');
});

test('the checked-in pool is byte-identical to a fresh derivation from the tag file', async () => {
    const g = await generator();
    const fresh = g.renderJson(g.deriveFromDisk(REPO));
    assert.equal(read(API_JSON), fresh,
        'TAG FILE vs GENERATED POOL: ' + PICKS_JSON + ' has moved and ' + API_JSON +
        ' has not. Run: node tools/gen-vfx-pick-options.mjs');
    assert.equal(read(RES_JSON), fresh,
        'TAG FILE vs BUILD POOL: ' + PICKS_JSON + ' has moved and ' + RES_JSON +
        ' has not. Run: node tools/gen-vfx-pick-options.mjs');
});

test('both copies are LF-only with a trailing newline, and carry no NUL byte', () => {
    for (const rel of [API_JSON, RES_JSON]) {
        const bytes = readBytes(rel);
        assert.equal(bytes.indexOf(0x0d), -1,
            rel + ' contains a CR. A CRLF rewrite would make the two copies differ on one ' +
            'machine and not the other - CLAUDE.md canonical-JSON rule.');
        assert.equal(bytes.indexOf(0x00), -1, rel + ' contains a NUL byte.');
        assert.equal(bytes[bytes.length - 1], 0x0a, rel + ' does not end with a newline.');
        let count = 0;
        for (const b of bytes) if (b === 0x0a) count++;
        assert.ok(count > 100,
            rel + ' has only ' + count + ' newlines - a flattened one-line rewrite is exactly ' +
            'the text-mode damage the canonical-JSON rule exists to catch.');
    }
});

// -- 2. IDS ARE STABLE AND APPEND-ONLY ----------------------------------------

test('regenerating over the existing pool NEVER moves an id', async () => {
    const g = await generator();
    const picks = g.parsePicks(read(PICKS_JSON));
    const previous = JSON.parse(read(API_JSON));

    const again = g.assignIds(picks, previous);
    const before = new Map(previous.options.map((o) => [o.key, o.id]));
    for (const opt of again) {
        if (!before.has(opt.key)) continue;
        assert.equal(opt.id, before.get(opt.key),
            'ID STABILITY: regenerating moved "' + opt.key + '" from id ' + before.get(opt.key) +
            ' to ' + opt.id + '. A row the owner set last week would now point at a DIFFERENT ' +
            'effect while the trace still said "override applied".');
    }
});

test('a NEW key appends at max+1 and disturbs nothing', async () => {
    const g = await generator();
    const picks = g.parsePicks(read(PICKS_JSON));
    const previous = JSON.parse(read(API_JSON));
    const maxBefore = previous.options.reduce((m, o) => (o.id > m ? o.id : m), 0);

    const withNew = picks.concat([{
        key: 'AAA_ZzTestKeyThatSortsFirst_Impact',
        prefabPath: 'Assets/Nowhere/Fake.prefab',
        isLoop: false,
    }]);
    const after = g.assignIds(withNew, previous);
    const added = after.find((o) => o.key === 'AAA_ZzTestKeyThatSortsFirst_Impact');
    assert.ok(added, 'the new key was dropped instead of appended');
    assert.equal(added.id, maxBefore + 1,
        'a new key must APPEND at max+1, not take a sorted position - it took ' + added.id);

    const before = new Map(previous.options.map((o) => [o.key, o.id]));
    for (const opt of after) {
        if (!before.has(opt.key)) continue;
        assert.equal(opt.id, before.get(opt.key),
            'adding one key moved "' + opt.key + '" from ' + before.get(opt.key) + ' to ' + opt.id);
    }
});

test('a REMOVED key keeps its id reserved rather than freeing it for reuse', async () => {
    const g = await generator();
    const picks = g.parsePicks(read(PICKS_JSON));
    const previous = JSON.parse(read(API_JSON));

    const dropped = picks[0];
    const after = g.assignIds(picks.slice(1), previous);
    const ghost = after.find((o) => o.key === dropped.key);
    assert.ok(ghost, 'the removed key vanished from the pool - its id is now free to be reused, ' +
        'which would silently re-point any row that already names it');
    assert.equal(ghost.retired, true, 'the removed key is present but not marked retired');

    const stillUsed = after.filter((o) => o.id === ghost.id);
    assert.equal(stillUsed.length, 1,
        'id ' + ghost.id + ' is claimed by ' + stillUsed.length + ' options at once');
});

// -- 2b. THE POOL IS TAG FILE **INTERSECT** CATALOG ---------------------------
//
// The first cut of the generator emitted the tag file verbatim and the editor oracle
// went red on option 58, 'KnightShieldBuff_Aura' - tagged by the owner, but with NO row
// in HovlVfxCatalog.asset, because HovlVfxCatalogGenerator skips a manual row whose
// prefab does not load (a gitignored pack that was not imported when the catalog was
// baked). Offering it would have let her pick an effect that renders nothing with no
// error on screen. These two cases keep that closed.

test('an option is OFFERED only when this build\'s catalog can resolve its key', async () => {
    const g = await generator();
    const resolvable = g.parseCatalogKeys(read('Assets/Resources/VFX/HovlVfxCatalog.asset'));
    assert.ok(resolvable.size > 0, 'the catalog parse found no resolvable keys at all');

    const doc = JSON.parse(read(API_JSON));
    for (const opt of doc.options) {
        if (opt.retired) continue;
        if (resolvable.has(opt.key)) {
            assert.notEqual(opt.unresolved, true,
                'TAG FILE vs CATALOG: option ' + opt.id + " ('" + opt.key + "') is marked unresolved " +
                'but the catalog resolves it - the generator is withholding a pick she is entitled to.');
        } else {
            assert.equal(opt.unresolved, true,
                'TAG FILE vs CATALOG: option ' + opt.id + " ('" + opt.key + "') has NO resolvable row in " +
                'HovlVfxCatalog.asset but is still OFFERED. Picking it would render nothing, with no error ' +
                'on screen - CLAUDE.md section 16, three incidents. Run: node tools/gen-vfx-pick-options.mjs');
        }
    }
});

test('an unresolvable key is MARKED, never deleted, and keeps its id reserved', () => {
    const doc = JSON.parse(read(API_JSON));
    const picks = JSON.parse(read(PICKS_JSON)).rows.map((r) => r.key);
    for (const opt of doc.options) {
        if (!opt.unresolved) continue;
        assert.ok(picks.includes(opt.key) || opt.retired,
            "option " + opt.id + " ('" + opt.key + "') is marked unresolved but is not in the owner's tag " +
            'file and is not retired - the two flags mean different things and must not be conflated.');
        const sharing = doc.options.filter((o) => o.id === opt.id);
        assert.equal(sharing.length, 1,
            'id ' + opt.id + ' is claimed by ' + sharing.length + ' options - an unresolvable key must ' +
            'keep its id RESERVED, so importing the pack later makes the SAME id offerable again rather ' +
            'than a different key inheriting it.');
    }
    // Her tag file is her record and this generator must never edit it.
    assert.ok(picks.length > 0, PICKS_JSON + ' is empty - it must never be pruned by this pipeline');
});

// -- 3. THE POOL IS JOINED INTO THE MANIFEST ----------------------------------

test('THE JOIN: every realm.vfx.* knob is in the build registry, the allowlist AND the manifest', () => {
    const src = read(REGISTRY_CS);
    const manifest = manifestLib.build();

    const registryKeys = [];
    const re = /const\s+string\s+[A-Za-z_]\w*\s*=\s*"(realm\.vfx\.[^"]+)"\s*;/g;
    for (let m = re.exec(src); m; m = re.exec(src)) registryKeys.push(m[1]);
    assert.ok(registryKeys.length >= 4,
        'BUILD REGISTRY: expected the four WO-1348 VFX pick keys, found ' + registryKeys.length);

    const allow = new Set(TUNABLE_KEYS.map((s) => s.key));
    const carded = new Map();
    for (const area of manifest.areas) for (const k of area.knobs) carded.set(k.key, k);

    for (const key of registryKeys) {
        assert.ok(allow.has(key),
            'BUILD REGISTRY vs SERVER ALLOWLIST: RemoteTunables.cs has "' + key + '" but ' +
            'TUNABLE_KEYS in api/_lib/tunables.js does not - the server would REFUSE every write.');
        assert.ok(manifestLib.PRESENTATION[key],
            'BUILD REGISTRY vs CONSOLE MANIFEST: PRESENTATION in api/_lib/tunable-manifest.js has ' +
            'no "' + key + '" - the knob would be INVISIBLE in the Command Center.');
        assert.ok(carded.has(key),
            'THE JOIN DROPPED "' + key + '": it is in the registry but build() did not card it.');
    }

    assert.deepEqual(manifest.defects, [],
        'the three-way join reports defects: ' + manifest.defects.join(' | '));
});

test('every VFX pick card offers the option pool as NAMES, not as a number field', () => {
    const manifest = manifestLib.build();
    let cards = 0;
    for (const area of manifest.areas) {
        for (const k of area.knobs) {
            if (!k.key.startsWith('realm.vfx.')) continue;
            cards++;
            assert.ok(Array.isArray(k.options) && k.options.length,
                '"' + k.key + '" carries no options - the owner would be typing a raw id, which ' +
                'is the "rocket scientist" surface this whole page exists to replace.');
            assert.ok(k.optionsNote && /rebuild/i.test(k.optionsNote),
                '"' + k.key + '" does not say IN WORDS that adding a new effect is still a ' +
                'rebuild. A picker that implies otherwise is CLAUDE.md section 16 all over again.');
            assert.ok(/next town load/i.test(k.what),
                '"' + k.key + '" does not state WHEN it applies. "I changed it and nothing ' +
                'happened" is the failure mode this feature must not have.');
            for (const o of k.options) {
                assert.ok(Number.isInteger(o.id) && o.id > 0, 'option id must be a positive int');
                assert.ok(o.key && o.prefab, 'option ' + o.id + ' has no key or no prefab name');
                assert.ok(o.retired !== true, 'option ' + o.id + ' is retired and must not be offered');
                assert.ok(o.unresolved !== true,
                    'option ' + o.id + " ('" + o.key + "') is UNRESOLVABLE in this build's catalog and " +
                    'must not be offered - picking it would render nothing with no error on screen.');
            }
        }
    }
    assert.equal(cards, 4, 'expected the four WO-1348 VFX pick cards, saw ' + cards);
});

test('THE INVARIANT: every realm.vfx.* knob ships at 0 = the build-time pick', () => {
    const generated = require('../api/_lib/tunable-manifest.generated.json');
    const vfx = generated.knobs.filter((k) => k.key.startsWith('realm.vfx.'));
    assert.ok(vfx.length >= 4, 'the generated spine carries only ' + vfx.length + ' VFX pick knobs');
    for (const k of vfx) {
        assert.equal(k.kind, 'int', '"' + k.key + '" must be int - the rail carries an option id');
        assert.equal(k.default, 0,
            'THE INVARIANT IS BROKEN: "' + k.key + '" ships at ' + k.default + ', not 0. 0 is the ' +
            'ONLY value that means "use the build-time pick", so any other default makes an EMPTY ' +
            'client_tunables table change what the game renders - which is the one thing ' +
            'RemoteTunables.cs says in capitals it must never do.');
    }
});

test('the safe range can never offer an id the shipped pool does not carry', () => {
    const manifest = manifestLib.build();
    const maxId = manifestLib.VFX_PICK_MAX_ID;
    assert.ok(maxId > 0, 'the option pool is empty');
    for (const area of manifest.areas) {
        for (const k of area.knobs) {
            if (!k.key.startsWith('realm.vfx.')) continue;
            assert.equal(k.min, 0, '"' + k.key + '" must allow 0 - it is how a pick is undone');
            assert.equal(k.max, maxId,
                '"' + k.key + '" offers up to ' + k.max + ' but the pool tops out at ' + maxId +
                '. A page that can submit an id the build cannot resolve renders NOTHING NEW and ' +
                'says nothing about why - the exact silence CLAUDE.md section 16 records three ' +
                'incidents from.');
        }
    }
});
