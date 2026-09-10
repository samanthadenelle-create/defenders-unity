// =============================================================================
// test/heartbound-suite.test.js — WO-1685 / HEART-012.
//
// Run the whole Heartbound backend package in ONE line:
//     node --test test/heartbound-*.test.js test/skr-staking.test.js
//
// THIS FILE HOLDS NO COVERAGE OF ITS OWN. It is the *oracle over the map*:
//
//   1. It parses the "Coverage map" table out of
//      WorkOrders/WORK_ORDER_1685_heart_012_regression_and_automated_test_package.md
//      and proves every row it names is real — the file exists, the file is
//      inside the package run line, and the case name is literally present in
//      that file. Rename a case and this suite reds, so the map cannot rot the
//      way three CLAIMED-BUT-NEVER-EXISTING suites in this same feature area did
//      (WO-1685 §0d: StakingComplianceRegression, JewelPolishRegression,
//      SkrStakingRegression). ⛔ The map is NOT copied here. One authority, in
//      the WO; a second table in this file would be the duplicated-state scar
//      CLAUDE.md §2/§5/§8/§16 each record.
//
//   2. It asserts the four cross-module invariants no single Heartbound lane
//      can own, because each spans files owned by different lanes:
//        B — the tier ladder is in NO client-readable file under Assets/
//            (Q-CONFIG ruling: "Command Center, server-only rows"; the client is
//             told its tier and never computes one).
//        C — no backend .js under api/ carries a NUL byte. CompileGate's NUL
//            guard scans .cs ONLY (CLAUDE.md §1, WO-434), and HEART-005 shipped
//            a draft with NULs — nothing else in the repo checks the .js side.
//        D — every api/_lib/heartbound-*.js (plus the two routes) parses under
//            `node --check`. A module only a not-yet-written test imports is a
//            file, not code.
//        E — no pulse-injection route exists under api/ (Q-INJECT ruling: "test
//            builds only" — the route does not exist in the app real players
//            get). Complements test/heartbound-pulse.test.js:592, which checks
//            the cron route's *inputs*; this checks the whole api/ SURFACE.
//
// Every iterating assertion below carries a NON-VACUOUS guard, because a loop
// over zero files passes and proves nothing (memory
// `gates-report-success-without-proving-it`).
//
// ⛔ This file edits nothing and imports no Heartbound module. It reads text.
// =============================================================================

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const REPO = path.resolve(__dirname, '..');
const WO_REL = 'WorkOrders/WORK_ORDER_1685_heart_012_regression_and_automated_test_package.md';

function readRel(rel) {
    return fs.readFileSync(path.join(REPO, rel), 'utf8');
}

function existsRel(rel) {
    return fs.existsSync(path.join(REPO, rel));
}

function gitList(pathspecs) {
    const out = execFileSync('git', ['ls-files', '-z', '--'].concat(pathspecs), {
        cwd: REPO, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024,
    });
    return out.split('\0').filter(Boolean);
}

// ── The package run line, asserted as a SET, not as prose ────────────────────
// A test file that exists but is not matched by the run line is a file, not
// coverage — the §0d failure in a new costume.
function inPackageRunLine(rel) {
    return /^test\/heartbound-[^/]+\.test\.js$/.test(rel) || rel === 'test/skr-staking.test.js';
}

// ── Case-name extraction ─────────────────────────────────────────────────────
// Titles are written as JS string literals and several carry an escaped
// apostrophe (`a duplicate job\'s grant insert ...`). A raw substring search of
// the source would miss those, so the literal is parsed and unescaped.
function titlesOf(rel) {
    const src = readRel(rel);
    const re = /^[ \t]*(?:test|it)\((['"])((?:\\.|(?!\1).)*)\1/gm;
    const out = new Set();
    let m;
    while ((m = re.exec(src)) !== null) {
        out.add(m[2].replace(/\\(['"\\])/g, '$1'));
    }
    return out;
}

// ── The map, parsed OUT OF THE WORK ORDER ────────────────────────────────────
function parseCoverageMap() {
    const src = readRel(WO_REL).split(/\r?\n/);
    const start = src.findIndex((l) => /^##\s.*Coverage map/i.test(l));
    assert.notStrictEqual(start, -1,
        WO_REL + ' must carry a "## ... Coverage map" heading — the map is the deliverable');
    const rows = [];
    for (let i = start + 1; i < src.length; i++) {
        const line = src[i];
        if (/^##\s/.test(line)) break;
        if (!/^\s*\|/.test(line)) continue;
        if (/^\s*\|[\s:|-]+\|\s*$/.test(line)) continue;              // separator
        const cells = line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|')
            .map((c) => c.trim());
        if (cells.length < 5) continue;
        if (/^ID$/i.test(cells[0])) continue;                          // header
        rows.push({
            id: cells[0], criterion: cells[1], file: cells[2],
            caseName: cells[3].replace(/^`|`$/g, ''), owner: cells[4],
            note: cells[5] || '', line,
        });
    }
    return rows;
}

// =============================================================================
// A. THE MAP IS REAL
// =============================================================================

// ── The spec's own criteria, derived from the spec ───────────────────────────
// ⛔ NO COUNT IS WRITTEN HERE. HEART-012's criteria are the `- ` bullets and the
// `N. ` numbered steps between its heading and HEART-013's, and each is cited in
// the map by its SPEC LINE NUMBER. Deriving the set means an edit to the spec
// reds the map instead of silently leaving a criterion uncovered — the whole
// failure mode §0d records.
const SPEC_REL = 'docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md';

function specCriterionLines() {
    const lines = readRel(SPEC_REL).split(/\r?\n/);
    const start = lines.findIndex((l) => /^#\s+HEART-012\s*$/.test(l));
    assert.notStrictEqual(start, -1, SPEC_REL + ' has no "# HEART-012" heading');
    const end = lines.findIndex((l, i) => i > start && /^#\s+HEART-013\s*$/.test(l));
    assert.notStrictEqual(end, -1, SPEC_REL + ' has no "# HEART-013" heading to bound the section');
    const out = [];
    for (let i = start; i < end; i++) {
        if (/^- \S/.test(lines[i]) || /^\d+\. \S/.test(lines[i])) out.push(i + 1); // 1-based
    }
    return out;
}

test('the coverage map covers EVERY criterion HEART-012 lists, derived from the spec itself', () => {
    const rows = parseCoverageMap();
    const wanted = specCriterionLines();
    assert.ok(wanted.length >= 30,
        'only ' + wanted.length + ' criteria parsed out of the spec section — the parser is broken');

    const cited = new Set();
    for (const r of rows) {
        const m = /\(:(\d+)\)/.exec(r.criterion);
        assert.ok(m, r.id + ': every map row must cite its spec line as "(:N)", got: ' + r.criterion);
        cited.add(Number(m[1]));
    }
    const missing = wanted.filter((n) => !cited.has(n));
    assert.deepStrictEqual(missing, [],
        'spec criteria at these lines of ' + SPEC_REL + ' appear in NO map row: ' + missing.join(', '));
    for (const n of cited) {
        assert.ok(wanted.includes(n),
            'the map cites spec line ' + n + ', which is not a HEART-012 criterion');
    }

    // Ids are U<n> / I<n>[a-z] / E<n>, each unique.
    const ids = rows.map((r) => r.id);
    assert.strictEqual(new Set(ids).size, ids.length, 'duplicate row id in the map');
    for (const id of ids) {
        assert.match(id, /^(U|I|E)\d+[a-z]?$/, 'unrecognised map id: ' + id);
    }
    for (const prefix of ['U', 'I', 'E']) {
        const n = ids.filter((i) => i.startsWith(prefix)).length;
        assert.ok(n > 0, 'the map covers no ' + prefix + ' criteria at all');
    }
});

test('every case the map cites is PRESENT, by exact name, in the file it names', () => {
    const rows = parseCoverageMap().filter((r) => !/^UNCOVERED$/i.test(r.file));
    assert.ok(rows.length >= 20, 'only ' + rows.length + ' covered rows — the map cannot be this thin');

    const cache = new Map();
    for (const row of rows) {
        assert.ok(existsRel(row.file), row.id + ': the map names a file that does not exist: ' + row.file);
        assert.ok(inPackageRunLine(row.file),
            row.id + ': ' + row.file + ' exists but is NOT matched by the package run line — '
            + 'a file outside the run is not coverage');
        if (!cache.has(row.file)) cache.set(row.file, titlesOf(row.file));
        const titles = cache.get(row.file);
        assert.ok(titles.size > 0, row.file + ' yielded no test titles — the extractor is broken');
        assert.ok(titles.has(row.caseName),
            row.id + ': ' + row.file + ' has no case named exactly:\n  ' + row.caseName
            + '\n(a renamed case must red this map, not slip through)');
    }
});

test('every UNCOVERED row names an owning work order that exists on disk', () => {
    // ⛔ NO FLOOR ON THE UNCOVERED COUNT. An all-green map is the GOAL once
    // HEART-005..009 land; a `rows.length > 0` guard here would red on the good
    // state and push the next seat into keeping a fake UNCOVERED row. Test 1's
    // derived criterion set is what keeps this suite non-vacuous.
    const rows = parseCoverageMap().filter((r) => /^UNCOVERED$/i.test(r.file));
    const wos = gitList(['WorkOrders/*.md']);
    for (const row of rows) {
        const m = /WO-(\d{3,4})/.exec(row.owner);
        assert.ok(m, row.id + ': an UNCOVERED row must name its owning WO, got: ' + row.owner);
        const hit = wos.some((w) => w.includes('WORK_ORDER_' + m[1] + '_'));
        assert.ok(hit, row.id + ': WO-' + m[1] + ' is named as the owner and has no file under WorkOrders/');
    }
});

test('the package run line in the work order is the one that actually runs these files', () => {
    const src = readRel(WO_REL);
    assert.match(src, /node --test test\/heartbound-\*\.test\.js test\/skr-staking\.test\.js/,
        'the WO must carry the ONE-LINE run command for the whole Heartbound package');
    const files = gitList(['test/heartbound-*.test.js', 'test/skr-staking.test.js']);
    assert.ok(files.length >= 5, 'the package glob matched only ' + files.length + ' files');
    for (const f of files) {
        assert.ok(inPackageRunLine(f), f + ' is tracked but the run line would not pick it up');
    }
});

// =============================================================================
// B. THE TIER LADDER IS NOWHERE THE CLIENT CAN READ IT
//    Q-CONFIG (triage §3, RULED 2026-09-10 13:36): "Command Center, server-only
//    rows — the client registry never carries the ladder". A threshold table
//    under Assets/ would be a SECOND AUTHORITY on a score product rule 6 makes
//    the backend authoritative for.
//    ⚠ Only TEXT files git tracks are scanned: .cs .json .txt .csv .asset .md.
//    Binary .unity/.prefab/.fbx are NOT scanned and that is stated in the RESULT.
// =============================================================================

const CLIENT_TEXT_PATHSPECS = [
    'Assets/*.cs', 'Assets/*.json', 'Assets/*.txt', 'Assets/*.csv', 'Assets/*.asset', 'Assets/*.md',
];

function ladderTokens() {
    const cfg = JSON.parse(readRel('api/_lib/heartbound-resonance-config.json'));
    // ⚠ Four of the eleven tier names are ORDINARY PRODUCT VOCABULARY and carry
    // no ladder information on their own: "Silent", "Awakened", "Echoing" (the
    // game is full of Echoes) and "Heartbound" (the feature's own name — it is
    // in every one of these files legitimately). Measured 2026-09-10: including
    // them lit 18 innocent files. The other seven are coined words that exist
    // nowhere else in the repo, and they are the fingerprint.
    const GENERIC = new Set(['Silent', 'Awakened', 'Echoing', 'Heartbound']);
    const names = cfg.tiers.map((t) => t.name).filter((n) => n && !GENERIC.has(n));
    // Only the TOP FOUR thresholds are used as numeric evidence, derived by
    // sorting the config's own ladder. The low end (300/600/900/1200…) are
    // ordinary round numbers that appear all over a game's balance data and
    // would make this a flaky detector; the high end is this ladder's
    // fingerprint. Measured 2026-09-10: no tracked text file under Assets/
    // carries even TWO of them, so the >=3 rule below has real headroom.
    const scores = cfg.tiers.map((t) => t.minScore).filter((s) => s > 0)
        .sort((a, b) => a - b).slice(-4);
    return { names, scores: Array.from(new Set(scores)) };
}

test('⛔ no client-readable file under Assets/ carries a Heartbound tier NAME', () => {
    const { names } = ladderTokens();
    assert.ok(names.length >= 7, 'only ' + names.length + ' tier names read from the config');
    const args = ['grep', '-l', '-i', '-F'];
    for (const n of names) args.push('-e', n);
    args.push('--', ...CLIENT_TEXT_PATHSPECS);
    let hits = '';
    try {
        hits = execFileSync('git', args, { cwd: REPO, encoding: 'utf8' });
    } catch (e) {
        if (e.status !== 1) throw e;   // git grep exits 1 on "no match" — the pass
        hits = '';
    }
    assert.strictEqual(hits.trim(), '',
        'a Heartbound tier name is readable by the client:\n' + hits);
});

test('⛔ no client-readable file under Assets/ carries the resonance THRESHOLDS or the curve knobs', () => {
    const { scores } = ladderTokens();
    assert.ok(scores.length >= 3, 'only ' + scores.length + ' distinctive thresholds derived from the config');

    const args = ['grep', '-o', '-w', '-E', scores.join('|')];
    args.push('--', ...CLIENT_TEXT_PATHSPECS);
    let raw = '';
    try {
        raw = execFileSync('git', args, { cwd: REPO, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
    } catch (e) {
        if (e.status !== 1) throw e;
        raw = '';
    }
    const perFile = new Map();
    for (const line of raw.split(/\r?\n/).filter(Boolean)) {
        const idx = line.indexOf(':');
        const file = line.slice(0, idx);
        const val = line.slice(line.lastIndexOf(':') + 1);
        if (!perFile.has(file)) perFile.set(file, new Set());
        perFile.get(file).add(val);
    }
    const guilty = Array.from(perFile.entries()).filter(([, s]) => s.size >= 3).map(([f]) => f);
    assert.deepStrictEqual(guilty, [],
        'these files co-occur three or more resonance thresholds — that is a ladder:\n' + guilty.join('\n'));

    // The config KEY names are a second fingerprint: a ported table brings them.
    const keyArgs = ['grep', '-l', '-F',
        '-e', 'minHeartboundStake', '-e', 'activationFraction', '-e', 'pulseRampFraction',
        '-e', 'tenurePower', '-e', 'stakePower', '-e', 'highestLifetimeTier',
        '--', ...CLIENT_TEXT_PATHSPECS];
    let keyHits = '';
    try {
        keyHits = execFileSync('git', keyArgs, { cwd: REPO, encoding: 'utf8' });
    } catch (e) {
        if (e.status !== 1) throw e;
        keyHits = '';
    }
    assert.strictEqual(keyHits.trim(), '',
        'a resonance config key is present client-side:\n' + keyHits);
});

// =============================================================================
// C. NO NUL BYTES IN THE BACKEND .js
//    CompileGate's NUL guard (CLAUDE.md §1, WO-434) scans Assets/**/*.cs ONLY.
//    Nothing in this repo checks the api/ side, and HEART-005 shipped a draft
//    with embedded NULs.
// =============================================================================

test('⛔ no backend .js under api/ contains a NUL byte (the CompileGate guard is .cs-only)', () => {
    const files = gitList(['api/*.js']);
    assert.ok(files.length >= 30, 'only ' + files.length + ' api .js files scanned — the glob is wrong');
    const bad = [];
    for (const rel of files) {
        const buf = fs.readFileSync(path.join(REPO, rel));
        if (buf.includes(0)) bad.push(rel);
    }
    assert.deepStrictEqual(bad, [], 'NUL bytes found in: ' + bad.join(', '));
});

// =============================================================================
// D. EVERY HEARTBOUND BACKEND MODULE PARSES
// =============================================================================

test('every api/_lib/heartbound-*.js and both Heartbound routes pass node --check', () => {
    const files = gitList(['api/_lib/heartbound-*.js', 'api/heartbound/*.js', 'api/cron/heart-pulse.js'])
        .concat(['api/_lib/skr-staking.js', 'api/_lib/solana-pda.js'].filter(existsRel));
    const libs = files.filter((f) => /^api\/_lib\/heartbound-.*\.js$/.test(f));
    assert.ok(libs.length >= 4,
        'expected at least the four heartbound _lib modules, found ' + libs.length);
    for (const rel of Array.from(new Set(files))) {
        execFileSync(process.execPath, ['--check', path.join(REPO, rel)], { cwd: REPO });
    }
});

// =============================================================================
// E. NO PULSE-INJECTION ROUTE IN api/
//    Q-INJECT / Q-DEMO-FLAG (triage §3, RULED 2026-09-10 13:20): "test builds
//    only — the pulse-injection route does not exist in the app real players
//    get". heartbound-pulse.test.js:592 proves the CRON route reads no body or
//    query; this proves no such route was added ANYWHERE under api/.
// =============================================================================

const INJECT_RE = /(inject|force|simulate|debug|demo|seed|fake)[-_]?pulse|pulse[-_]?(inject|force|simulate|debug|demo|seed|fake)/i;

function stripComments(src) {
    return src
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .split(/\r?\n/)
        .filter((l) => !/^\s*(\/\/|\*)/.test(l))
        .join('\n');
}

test('⛔ no api/ file is NAMED as a pulse-injection route (Q-INJECT: test builds only)', () => {
    const files = gitList(['api/*']);
    assert.ok(files.length >= 40, 'only ' + files.length + ' api files listed — the glob is wrong');
    const bad = files.filter((f) => INJECT_RE.test(path.basename(f)));
    assert.deepStrictEqual(bad, [],
        'a pulse-injection route exists in production code: ' + bad.join(', '));
});

test('⛔ no live line of api/ .js references a pulse-injection route name', () => {
    const files = gitList(['api/*.js']);
    assert.ok(files.length >= 30, 'only ' + files.length + ' api .js files scanned');
    const bad = [];
    for (const rel of files) {
        const src = stripComments(readRel(rel));
        for (const line of src.split(/\r?\n/)) {
            if (INJECT_RE.test(line)) bad.push(rel + ': ' + line.trim());
        }
    }
    // The ONLY permitted mentions are prose — heartbound-pulse.js:44 states the
    // ruling in a comment, and a comment is stripped above. A live line is a route.
    assert.deepStrictEqual(bad, [],
        'pulse-injection referenced in live code:\n' + bad.join('\n'));
});
