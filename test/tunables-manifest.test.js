// =============================================================================
// test/tunables-manifest.test.js - WO-1328. THE ORACLE.
// -----------------------------------------------------------------------------
// The Command Center balance editor is driven by a manifest. A manifest is a
// LIST OF FACTS ABOUT KNOBS, and this repo's single most expensive recurring bug
// is a fact written twice and then left to rot - CLAUDE.md records four separate
// scars from it (the stale WO number block, the hardcoded repo root, the retired
// assembly dependency table, the "six faces" action bar). A fifth copy of the
// knob list would have been a straight repeat of that mistake.
//
// So the manifest's spine is DERIVED, and this file is the thing that proves it
// still is. It re-parses DeNelle.Core.Ops.RemoteTunables.Registry from source on
// every run and compares it against:
//
//     1. api/_lib/tunable-manifest.generated.json  (the checked-in derivation)
//     2. TUNABLE_KEYS in api/_lib/tunables.js      (the server write allowlist)
//     3. PRESENTATION in api/_lib/tunable-manifest.js (owner-facing prose)
//     4. docs/PROD022_TUNABLE_FLAGS.md             (the human contract)
//     5. the served Command Center page itself
//
// EVERY failure message NAMES THE TWO SOURCES THAT DISAGREE, because "the
// manifest is wrong" is not something anyone can act on at 2am and "the build
// registry has combat.foo, the console manifest does not" is.
//
// WHY IT MATTERS MORE THAN IT LOOKS: a knob missing from the manifest is a lever
// the owner cannot find and does not know to look for. She would conclude the
// number cannot move and go back to the thirty-minute rebuild - which is the
// exact cost this whole ticket exists to delete.
//
//     node --test test/tunables-manifest.test.js
//
// Zero network, zero database, zero Unity. Node built-ins only.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const REPO = path.resolve(__dirname, '..');

const manifestLib = require('../api/_lib/tunable-manifest');
const generated = require('../api/_lib/tunable-manifest.generated.json');
const { TUNABLE_KEYS, normalizeValue } = require('../api/_lib/tunables');
const consoleEndpoint = require('../api/admin/console.js');

const REGISTRY_CS = 'Assets/_Modules/Core/Ops/RemoteTunables.cs';
const DOC = 'docs/PROD022_TUNABLE_FLAGS.md';

function read(rel) { return fs.readFileSync(path.join(REPO, rel), 'utf8'); }

// The generator is an ESM module and this suite is CommonJS, so the parse is
// reached through a dynamic import. It is the SAME function the generator uses -
// re-implementing the parse here would have created a sixth copy of the very
// thing under test.
let gen = null;
async function generator() {
    if (!gen) gen = await import('../tools/gen-tunable-manifest.mjs');
    return gen;
}

// -- 1. THE SPINE IS ACTUALLY DERIVED ----------------------------------------

test('the checked-in spine is byte-identical to a fresh derivation from the build registry', async () => {
    const g = await generator();
    const fresh = g.deriveFromDisk(REPO);
    const onDisk = read('api/_lib/tunable-manifest.generated.json');
    assert.equal(g.renderJson(fresh), onDisk,
        'BUILD REGISTRY vs GENERATED MANIFEST: ' + REGISTRY_CS + ' has moved and ' +
        'api/_lib/tunable-manifest.generated.json has not. Run: node tools/gen-tunable-manifest.mjs');
});

test('the derivation resolves every knob, including the ones whose default is a named const', async () => {
    const g = await generator();
    const fresh = g.deriveFromDisk(REPO);
    const byKey = new Map(fresh.map((k) => [k.key, k]));

    // trace.assetVerbosity's default is written as VerbosityVerbose and
    // combat.drainReturnPct's as DrainReturnPctDefault. A parse that only handled
    // integer literals would silently drop or mis-read them, so both are named.
    assert.equal(byKey.get('trace.assetVerbosity').default, 2);
    // 60, not 100: the owner ruled the drain down on 2026-09-02 ("keep drain at 60%
    // for now"). This literal is checking that the parser RESOLVES a named const, so it
    // has to name a value - and it is the one place in this file where retyping the
    // number is the point rather than the bug.
    assert.equal(byKey.get('combat.drainReturnPct').default, 60);
    assert.equal(byKey.get('pi.requestTimeoutSeconds').default, 20);
    assert.equal(byKey.get('assets.maxRequestAttempts').default, 3);
    for (const k of fresh) {
        assert.ok(k.kind === 'bool' || k.kind === 'int', k.key + ' has no usable kind');
        assert.equal(typeof k.default, 'number', k.key + ' has no numeric default');
    }
});

test('a registry entry the parse cannot resolve THROWS rather than being dropped', async () => {
    const g = await generator();
    const broken = read(REGISTRY_CS).replace(
        /new TunableSpec\(KeyCombatDrainReturnPct, TunableKind\.Int, DrainReturnPctDefault,/,
        'new TunableSpec(KeyCombatDrainReturnPct, TunableKind.Int, NoSuchConstant,');
    assert.throws(() => g.parseRegistry(broken), /unresolvable default const/,
        'a knob whose default cannot be resolved must be loud, never silently missing');
});

// -- 2. THE THREE SOURCES AGREE ----------------------------------------------

test('THE ORACLE: build registry, server allowlist and console manifest all agree', () => {
    const defects = manifestLib.mismatches();
    assert.deepEqual(defects, [],
        'the balance manifest disagrees with the build:\n  - ' + defects.join('\n  - '));
});

test('the key domain is identical in the build registry and the server allowlist', () => {
    const spine = generated.knobs.map((k) => k.key).slice().sort();
    // serverOnly keys are writable but not registered in any build (WO-1682 D3) -
    // see the note on the area-placement test above. Every other key must match.
    const allow = TUNABLE_KEYS.map((k) => k.key)
        .filter((k) => !manifestLib.isServerOnly(manifestLib.PRESENTATION[k]))
        .slice().sort();
    assert.deepEqual(spine, allow,
        'BUILD REGISTRY vs SERVER ALLOWLIST: RemoteTunables.Registry and TUNABLE_KEYS ' +
        'in api/_lib/tunables.js name different knobs. The server refuses to write a key ' +
        'it does not allowlist, and no build reads a key it does not register - either ' +
        'gap is a knob that looks settable and is not.');
});

test('every knob the build registers is reachable in exactly one owner-facing area', () => {
    const m = manifestLib.build();
    const placed = [];
    // ⚠ serverOnly rows are EXCLUDED from this comparison on purpose (WO-1682 D3,
    // Q-CONFIG). They have no entry in the build registry BY DESIGN - a backend-read
    // knob written into RemoteTunables.cs would hand the client a readable copy of a
    // number the server is authoritative for. Everything the build DOES register
    // still has to be here, which is what this test protects.
    for (const a of m.areas) for (const k of a.knobs) if (!k.serverOnly) placed.push(k.key);
    assert.deepEqual(placed.slice().sort(), generated.knobs.map((k) => k.key).slice().sort(),
        'BUILD REGISTRY vs CONSOLE MANIFEST: a knob the build has is not on the page, or ' +
        'the page has one the build does not. A lever the owner cannot see is a lever she ' +
        'will believe does not exist.');
    assert.equal(new Set(placed).size, placed.length, 'a knob appears in two areas');
});

test('the four areas are the ones the owner named, in the order she named them', () => {
    assert.deepEqual(manifestLib.AREAS.map((a) => a.id), ['skills', 'tiers', 'spells', 'misc']);
    const m = manifestLib.build();
    assert.deepEqual(m.areas.map((a) => a.title), ['Skills', 'Tiers', 'Spells', 'Misc']);
    // An area with no lever yet still renders and says so. Silence would read as
    // "this cannot be tuned", which is a different and wrong answer.
    const empty = m.areas.filter((a) => !a.knobs.length).map((a) => a.id);
    assert.deepEqual(empty, ['skills', 'tiers']);
});

test('the shipped default is always inside the range the page will offer', () => {
    const m = manifestLib.build();
    for (const a of m.areas) for (const k of a.knobs) {
        assert.ok(k.def >= k.min && k.def <= k.max,
            k.key + ' ships at ' + k.def + ' but the page offers ' + k.min + '..' + k.max +
            ' - the owner could not put it back by typing.');
        // And the value the page would submit must survive the server's own
        // validator, or the page is offering a write that will be refused.
        assert.equal(normalizeValue(k.key, String(k.def)), String(k.def),
            k.key + ': the server would refuse the value the build ships with');
        assert.equal(normalizeValue(k.key, String(k.min)), String(k.min));
        assert.equal(normalizeValue(k.key, String(k.max)), String(k.max));
    }
});

test('the human contract document names every knob the build registers', () => {
    const doc = read(DOC);
    for (const k of generated.knobs) {
        assert.ok(doc.indexOf('`' + k.key + '`') > 0,
            'BUILD REGISTRY vs ' + DOC + ': the doc does not mention "' + k.key + '". ' +
            'CLAUDE.md section 15 - canon moves in the same commit as the fact.');
    }
});

// -- 2b. SERVER-ONLY ROWS (WO-1682 D3; Q-CONFIG, owner 2026-09-10 13:36) ------
//
// "Command Center, server-only rows - teach tunable-manifest a serverOnly marker
// honoured by build(); the client registry never carries the ladder."
//
// The behaviour is proven BY INJECTION rather than by a live row, because there
// are deliberately none yet (a Heartbound row also needs a TUNABLE_KEYS entry and
// a card on api/admin/console.js, neither of which WO-1682 owns). Injection is
// also the stronger proof: it exercises the mechanism without making any claim
// about today's contents, so these cases keep working when the rows do land.

const SERVER_ONLY_ROW = {
    'heartbound.productionRateCeiling': {
        area: 'misc',
        label: 'Heartbound economic ceiling',
        what: 'The most combined passive resource-rate boost the Heartbound tiers may ' +
              'add up to, as a fraction. Read by the backend only.',
        min: 0,
        max: 1,
        // A REFERENCE into the module that reads it, never a retyped number.
        def: require('../api/_lib/heartbound-tiers-config.json').economicCeiling.productionRateCeiling,
        serverOnly: true,
    },
};

test('the marker exempts a backend-only row from the BUILD REGISTRY defect, and from that alone', () => {
    const KEY = 'heartbound.productionRateCeiling';

    // WITHOUT the marker: the row is judged as a client knob and reported as a lever
    // that moves nothing, which is the correct answer for a client knob.
    const withoutMarker = Object.assign({}, manifestLib.PRESENTATION, {
        [KEY]: Object.assign({}, SERVER_ONLY_ROW[KEY], { serverOnly: false }),
    });
    const before = manifestLib.mismatches(withoutMarker).filter((d) => d.indexOf(KEY) >= 0);
    assert.equal(before.length, 1);
    assert.match(before[0], /CONSOLE MANIFEST vs BUILD REGISTRY/);
    assert.match(before[0], /a lever that moves nothing/);

    // WITH the marker: the BUILD REGISTRY defect is gone. What remains is the
    // allowlist defect, because this key is genuinely not writable yet - and that
    // is the point: serverOnly buys an exemption from needing a BUILD, never from
    // needing to be WRITABLE.
    const withMarker = Object.assign({}, manifestLib.PRESENTATION, SERVER_ONLY_ROW);
    const after = manifestLib.mismatches(withMarker).filter((d) => d.indexOf(KEY) >= 0);
    assert.equal(after.length, 1);
    assert.match(after[0], /SERVER-ONLY ROW vs SERVER ALLOWLIST/);
    assert.doesNotMatch(after[0], /BUILD REGISTRY/);

    // And it perturbs nothing else in the manifest.
    assert.deepEqual(manifestLib.mismatches(withMarker).filter((d) => d.indexOf(KEY) < 0), []);
});

test('the serverOnly exemption is EXACTLY ONE DEFECT WIDE - every other check still bites', () => {
    // ⛔ THE CASE THAT CAUGHT A REAL GAP. Every row check used to live inside the loop
    // over the BUILD REGISTRY, so a row with no registry entry was reached by NONE of
    // them - and marking it serverOnly would have exempted it from every defect
    // rather than from one. checkPresentationRow exists because this went red.
    const bad = {
        'heartbound.broken': {
            area: 'nowhere', label: '', what: '', min: 5, max: 1, def: 0, serverOnly: true,
        },
    };
    const defects = manifestLib.mismatches(Object.assign({}, manifestLib.PRESENTATION, bad));
    const mine = defects.filter((d) => d.indexOf('heartbound.broken') >= 0);
    assert.ok(mine.some((d) => /is not one of/.test(d)), 'a bad area must still be caught');
    assert.ok(mine.some((d) => /no usable safe range/.test(d)), 'an inverted range must still be caught');
    assert.ok(mine.some((d) => /no label or no plain-English description/.test(d)),
        'missing prose must still be caught');
    assert.ok(mine.some((d) => /SERVER-ONLY ROW vs SERVER ALLOWLIST/.test(d)),
        'an unwritable server-only row must still be caught');
    // And it did NOT get the registry-agreement defect, which is the one exemption.
    assert.ok(!mine.some((d) => /BUILD REGISTRY/.test(d)));
});

test('build() offers a serverOnly row only when the server would actually accept a write', () => {
    const pres = Object.assign({}, manifestLib.PRESENTATION, SERVER_ONLY_ROW);

    // Not in TUNABLE_KEYS today => the page must NOT offer it. "A lever that moves
    // nothing" is the defect this join exists to catch, and serverOnly does not
    // buy an exemption from it.
    const m = manifestLib.build(pres);
    const placed = [];
    for (const a of m.areas) for (const k of a.knobs) placed.push(k.key);
    assert.equal(placed.indexOf('heartbound.productionRateCeiling'), -1,
        'a serverOnly row the server allowlist does not carry must not be offered');
});

test('the shipped manifest carries ZERO serverOnly rows, and that is deliberate', () => {
    for (const key of Object.keys(manifestLib.PRESENTATION)) {
        assert.equal(manifestLib.isServerOnly(manifestLib.PRESENTATION[key]), false,
            key + ' is marked serverOnly. WO-1682 D3 landed the MECHANISM only; a live ' +
            'row also needs a TUNABLE_KEYS entry and a card on api/admin/console.js. ' +
            'If you are adding the first one, update this test in the same commit.');
    }
    // ...so today's manifest is byte-for-byte what it was before the marker existed.
    assert.deepEqual(manifestLib.mismatches(), []);
});

// -- 3. THE BOUNDARY THAT CAN NEVER MOVE -------------------------------------

test('no server-authoritative money value can ever reach this manifest', () => {
    // The game takes real money on mainnet. A price the phone could override is
    // an exploit, not a feature. This is asserted on the SHAPE of the manifest,
    // not on its current contents, so a future seat adding "just one more knob"
    // trips it rather than shipping it.
    const forbidden = /price|sku|entitle|grant|usd|payout|refund|cost|purchase|wallet/i;
    for (const key of Object.keys(manifestLib.PRESENTATION)) {
        assert.doesNotMatch(key, forbidden,
            'knob "' + key + '" names a money concept. Prices, entitlements, grants and ' +
            'purchase amounts are decided server-side in api/_lib/purchase-catalog.js and ' +
            'are permanently out of scope for this page.');
    }
    const src = fs.readFileSync(path.join(REPO, 'api/_lib/tunable-manifest.js'), 'utf8');
    // NAMING the money catalog is required (the boundary has to be stated in
    // words); IMPORTING it is the thing that must never happen.
    assert.doesNotMatch(src, /require\([^)]*purchase-catalog[^)]*\)/,
        'the balance manifest must not import the money catalog');
    // The boundary is STATED where a person will read it, not only where a test will.
    assert.match(manifestLib.OUT_OF_SCOPE_NOTICE, /purchase-catalog\.js/);
    assert.match(consoleEndpoint.PAGE, /NEVER editable here and never will be/);
});

test('the write path is the ops endpoint and nothing else', () => {
    const src = fs.readFileSync(path.join(REPO, 'api/_lib/tunable-manifest.js'), 'utf8');
    // The manifest is prose and shape. It touches no database and issues no query.
    assert.doesNotMatch(src, /\b(INSERT|UPDATE|DELETE|neon\()\b/);
    // api/admin/db.js and api/admin/stats.js stay SELECT-only BY CONSTRUCTION;
    // test/command-center.test.js owns that assertion, and this one only proves
    // WO-1328 did not quietly add a second door.
    for (const rel of ['api/admin/db.js', 'api/admin/stats.js']) {
        const s = fs.readFileSync(path.join(REPO, rel), 'utf8');
        assert.doesNotMatch(s, /tunable/i, rel + ' must know nothing about knob writes');
    }
});

// -- 4. THE PAGE THE OWNER ACTUALLY TOUCHES ----------------------------------

test('the served page carries the manifest and a card for every knob', () => {
    const page = consoleEndpoint.PAGE;
    assert.doesNotMatch(page, /__TUNABLE_MANIFEST__/, 'the manifest placeholder was not substituted');
    for (const a of manifestLib.build().areas) {
        assert.ok(page.indexOf('"' + a.title + '"') > 0 || page.indexOf(a.title) > 0);
        for (const k of a.knobs) {
            assert.ok(page.indexOf('"' + k.key + '"') > 0, 'the page has no card for ' + k.key);
            assert.ok(page.indexOf(k.label) > 0, 'the page never prints the label for ' + k.key);
        }
    }
});

test('every knob states its state in WORDS - the owner is red/green colourblind', () => {
    const page = consoleEndpoint.PAGE;
    assert.match(page, /OVERRIDDEN \(the installed game ships with /);
    assert.match(page, /Shipped default - nothing is overriding it/);
    // A failed read is never rendered as "at its default".
    assert.match(page, /COULD NOT READ the override table/);
    assert.match(page, /this is NOT proof the knob is at its default/);
});

test('RESET IS NOT ZERO, and the page says so where the finger is', () => {
    const page = consoleEndpoint.PAGE;
    assert.match(manifestLib.CLEAR_IS_NOT_ZERO_NOTICE, /REMOVES the override/);
    assert.match(manifestLib.CLEAR_IS_NOT_ZERO_NOTICE, /20 seconds/);
    // Once at the top of the tab...
    assert.match(page, /Reset is not zero/);
    // ...once on the button that does it, in the confirm the finger triggers.
    assert.match(page, /It is NOT the ' \+\n {10}'same as saving 0\.'|same as saving 0/);
    assert.match(page, /Reset to shipped \(/);
    // And the two verbs are different actions, not one action with a value of 0.
    assert.match(page, /action:'tunable\.set'/);
    assert.match(page, /action:'tunable\.clear'/);
});

test('the balance surface is one tap from the landing page and sized for a thumb', () => {
    const page = consoleEndpoint.PAGE;
    // In the PRIMARY nav, not behind the "More tools" disclosure: this is the tab
    // she opens to do the job the ticket exists for.
    const nav = page.slice(page.indexOf('<nav id="tabs">'), page.indexOf('<nav id="tools"'));
    assert.match(nav, /data-tab="balance"/);
    // WO-1328: touch targets >= 112px.
    assert.match(page, /--bigtap:112px/);
    assert.match(page, /\.knob-controls button\{min-height:var\(--bigtap\)/);
    assert.match(page, /\.bool-row button\{min-height:var\(--bigtap\)/);
});

test('the page reads the SAME table the game reads, and defeats the edge cache', () => {
    const page = consoleEndpoint.PAGE;
    assert.match(page, /\/api\/client-tunables\?fresh=' \+ Date\.now\(\)/);
    // No admin key is spent on a public endpoint, and the key never enters a URL.
    const i = page.indexOf('/api/client-tunables');
    const block = page.slice(i - 200, i + 300);
    assert.doesNotMatch(block, /X-Admin-Key/);
    assert.doesNotMatch(page, /[?&]key=/);
});

test('the manifest is 7-bit ASCII, because the served page must be', () => {
    const json = JSON.stringify(manifestLib.build());
    for (let i = 0; i < json.length; i++) {
        const c = json.charCodeAt(i);
        assert.ok(c >= 32 && c <= 126, 'non-ASCII in the manifest at ' + i + ': ' + json.slice(i - 40, i + 10));
    }
});

// -- 5. THE HTML THE OWNER ACTUALLY SEES -------------------------------------
// Everything above this line reads SOURCE. These read OUTPUT.
//
// The distinction is the one CLAUDE.md section 12 keeps making: a regex over the
// page proves the code CONTAINS a sentence; only rendering proves the owner is
// SHOWN it. The page script is an IIFE, so the harness re-opens it just far
// enough to reach renderBalance - it changes not one byte of what is served, and
// it throws if the page's shape moves under it rather than silently testing
// nothing.

const vm = require('node:vm');

function renderBalanceHtml(mutateState) {
    const page = consoleEndpoint.PAGE;
    const i = page.indexOf('<script>');
    const j = page.lastIndexOf('</script>');
    let js = page.slice(i + 8, j).trim();
    const tail = '})();';
    assert.ok(js.endsWith(tail), 'the page script is no longer a plain IIFE - fix this harness');
    js = js.slice(0, -tail.length) +
        '\n  globalThis.__probe = { state: state, render: renderBalance };\n})();';

    const el = () => ({
        textContent: '', value: '', innerHTML: '', hidden: false,
        addEventListener() {}, setAttribute() {}, getAttribute() { return null; },
        querySelectorAll() { return []; }, querySelector() { return null; },
    });
    const ctx = {
        document: { getElementById: el, addEventListener() {} },
        window: { prompt() { return null; }, confirm() { return false; } },
        fetch: () => new Promise(() => {}),
        Date, Math, JSON, Number, String, Array, Object, isFinite, parseInt, Promise, console,
    };
    ctx.globalThis = ctx;
    vm.createContext(ctx);
    vm.runInContext(js, ctx);
    mutateState(ctx.__probe.state);
    return ctx.__probe.render();
}

test('RENDERED: every knob gets a card, and an override says so in words next to the default', () => {
    const html = renderBalanceHtml((s) => {
        s.tunReadOk = true;
        s.tun = { 'combat.drainReturnPct': '50' };
    });
    const total = generated.knobs.length;
    assert.equal((html.match(/class="knob"/g) || []).length, total, 'one card per knob');
    // The overridden one names BOTH numbers: what it is now, and what the build ships.
    // The shipped number is DERIVED from the generated spine, never retyped here: it was
    // hardcoded as 100 until the owner ruled the drain down to 60 (WO-1330), at which point
    // this assertion - a fifth hand-written copy of a default - went red on correct code.
    // That is exactly the duplicated-state disease this whole manifest exists to cure, so
    // the test now reads the default from the same place the page does.
    const shipped = generated.knobs.find((k) => k.key === 'combat.drainReturnPct').default;
    assert.match(html, /<span class="num">50<\/span>/);
    assert.ok(html.includes('OVERRIDDEN (the installed game ships with ' + shipped + ')'),
        'the card must name the shipped default in words; it said neither ' + shipped + ' nor anything like it');
    // Everything else says, in words, that nothing is overriding it.
    assert.equal((html.match(/Shipped default - nothing is overriding it/g) || []).length, total - 1);
    // And every card offers the one-tap way back.
    assert.equal((html.match(/class="knob-clear"/g) || []).length, total);
});

test('RENDERED: an unreadable table is NEVER drawn as "everything is at its default"', () => {
    const html = renderBalanceHtml((s) => {
        s.tunReadOk = false;
        s.tun = null;
        s.tunErr = 'QUERY_FAILED';
    });
    assert.match(html, /COULD NOT READ the override table/);
    // The load-bearing one. A failed read rendered as the default is a confident
    // lie, and it would send the owner off felt-testing a configuration she is not
    // actually running.
    assert.doesNotMatch(html, /Shipped default - nothing is overriding it/);
    assert.equal((html.match(/>unknown</g) || []).length, generated.knobs.length);
});

test('RENDERED: a junk value in the table is called junk, not silently shown as a number', () => {
    const html = renderBalanceHtml((s) => {
        s.tunReadOk = true;
        s.tun = { 'pi.requestTimeoutSeconds': 'twenty' };
    });
    assert.match(html, /OVERRIDDEN with a value the game cannot read, so the game is using 20/);
});

test('RENDERED: a bool knob offers ON, OFF and Reset - three distinct verbs', () => {
    const html = renderBalanceHtml((s) => { s.tunReadOk = true; s.tun = {}; });
    const bools = generated.knobs.filter((k) => k.kind === 'bool').length;
    assert.equal((html.match(/class="knob-on"/g) || []).length, bools);
    assert.equal((html.match(/class="knob-off"/g) || []).length, bools);
    // Reset is not "turn it off": it is a separate verb with its own button and
    // its own sentence, on every card.
    assert.match(html, /Reset REMOVES the override so the knob answers the installed game\./);
});

test('RENDERED: every area the owner named appears, empty ones included', () => {
    const html = renderBalanceHtml((s) => { s.tunReadOk = true; s.tun = {}; });
    for (const a of manifestLib.AREAS) {
        assert.ok(html.indexOf('<h2>' + a.title + '</h2>') > 0, 'no section for ' + a.title);
    }
    // An area with no lever says so instead of rendering as a blank box.
    assert.match(html, /No levers here yet\./);
});

test('RENDERED: the money boundary is printed on the page, not only in a comment', () => {
    const html = renderBalanceHtml((s) => { s.tunReadOk = true; s.tun = {}; });
    assert.match(html, /NEVER editable here and never will be/);
    assert.match(html, /purchase-catalog\.js/);
    assert.match(html, /Reset is not zero/);
});
