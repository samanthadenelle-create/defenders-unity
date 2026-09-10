// =============================================================================
// tools/gen-vfx-pick-options.mjs - WO-1348. DERIVE the VFX pick OPTION POOL that
// the Command Center offers and the build resolves, from the owner's own tag file.
// -----------------------------------------------------------------------------
// Owner ask 2026-09-03, verbatim:
//   "is it possible to tag those from the command center? and then change
//    pointer on next town load?"   "realm.vfx(set)"   "that idea"
//
// -----------------------------------------------------------------------------
// WHY AN OPTION POOL AT ALL, AND WHY IT IS THE OWNER'S OWN PICKS.
// -----------------------------------------------------------------------------
// CLAUDE.md section 16's lesson, applied one layer up: art with NO LOCAL FALLBACK
// produces a build that installs, launches and plays with tinted capsules and NO
// ERROR ON SCREEN. A VFX picker that could offer a prefab which was never shipped
// reproduces exactly that failure in a new place - she picks, nothing appears, and
// nothing says why.
//
// So the pool is not "every prefab in the packs". It is the set of keys the owner has
// ALREADY TAGGED in Assets/Editor/VfxManualPicks.json **INTERSECTED with the keys this
// build's Assets/Resources/VFX/HovlVfxCatalog.asset can actually resolve to a prefab.**
// An option therefore names a prefab that is SHIPPED BY CONSTRUCTION - choosing option N
// for key K means "render K with the prefab that key <option N's key> already renders
// with". No new art can be summoned from the database, and the work order says so out
// loud: this changes WHICH SHIPPED EFFECT IS USED, never WHICH EFFECTS EXIST.
//
// ⚠ THE INTERSECTION IS NOT BELT-AND-BRACES - THE ORACLE CAUGHT A REAL ONE. The first cut
// of this generator emitted the tag file verbatim, and VfxPickOverrideRegression went red:
// option 58, 'KnightShieldBuff_Aura', is tagged by the owner but has NO ROW in the catalog,
// because HovlVfxCatalogGenerator SKIPS a manual row whose prefab does not load - which is
// what happens when the prefab lives in a GITIGNORED art pack that was not imported on the
// machine that baked the catalog. Offering it would have let her pick an effect that renders
// nothing, with no error on screen. See parseCatalogKeys.
//
// ⛔ AN UNRESOLVABLE PICK IS MARKED, NEVER DELETED - not from this file and never from her
// JSON. Her tag is her record. It keeps its reserved id and carries `unresolved: true`, so
// the day the pack is imported and the catalog rebaked, the SAME id starts being offered
// again rather than a different key inheriting it.
//
// -----------------------------------------------------------------------------
// (!) IDS ARE STABLE AND APPEND-ONLY. THIS IS THE LOAD-BEARING PROPERTY.
// -----------------------------------------------------------------------------
// The remote row stores an INTEGER (the tunables rail is Int-only - see
// DeNelle.Core.Ops.TunableKind). If that integer were a SORTED POSITION, the day
// somebody tags "Aegis2_Cast" every id after it would shift and a row she set last
// week would silently point at a DIFFERENT prefab, while the trace cheerfully said
// "override applied". That is a lying trace, which is the one thing the work
// order's instrumentation section forbids by name.
//
// So: an id, once assigned to a key, is NEVER reassigned. On regenerate this file
// reads its own previous output, keeps every existing key->id pair verbatim, and
// appends new keys at max(id)+1. A key REMOVED from VfxManualPicks.json keeps its
// id reserved (rendered with "retired": true) rather than freeing it for reuse -
// a freed id is the same silent re-point by another route.
//
// -----------------------------------------------------------------------------
// TWO OUTPUTS, BYTE-IDENTICAL, AND THAT DUPLICATION IS DELIBERATE.
// -----------------------------------------------------------------------------
//   api/_lib/vfx-pick-options.generated.json          <- the Command Center reads it
//   Assets/Resources/VFX/vfx-pick-options.generated.json  <- the BUILD reads it
//
// The server cannot read a Unity .asset and the build cannot reach api/_lib/, so
// the same bytes have to exist on both sides of the wire. That is duplicated state,
// which CLAUDE.md sections 2/5/16 each record a scar from - so it is generated from
// ONE source by ONE tool and pinned byte-for-byte by test/vfx-pick-options.test.js.
// The cure for duplicated state is a single generator plus an oracle, never a
// promise to keep two files in step by hand.
//
// -----------------------------------------------------------------------------
// USAGE
//     node tools/gen-vfx-pick-options.mjs            # rewrite both copies
//     node tools/gen-vfx-pick-options.mjs --check    # exit non-zero on drift
//
// Judge it by the MARKER on the output, never the exit code (CLAUDE.md section 8):
// VFX_PICK_OPTIONS_GEN_OK / VFX_PICK_OPTIONS_DRIFT / VFX_PICK_OPTIONS_GEN_FAIL.
//
// No dependencies. ASCII only. LF newlines, and the test proves the newline count.
// =============================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '..');

export const PICKS_JSON = 'Assets/Editor/VfxManualPicks.json';
export const CATALOG_ASSET = 'Assets/Resources/VFX/HovlVfxCatalog.asset';
export const API_OPTIONS_JSON = 'api/_lib/vfx-pick-options.generated.json';
export const RESOURCES_OPTIONS_JSON = 'Assets/Resources/VFX/vfx-pick-options.generated.json';

/**
 * Read the owner's tag file and return its rows in FILE ORDER.
 * Loud on an unreadable file: silently yielding an empty pool would produce a
 * picker with nothing in it, which reads as "the feature is broken" rather than
 * "the input is missing".
 *
 * @param {string} src contents of VfxManualPicks.json
 */
export function parsePicks(src) {
    if (typeof src !== 'string' || !src.length) {
        throw new Error(PICKS_JSON + ' was empty or unreadable');
    }
    let doc;
    try {
        doc = JSON.parse(src);
    } catch (err) {
        throw new Error(PICKS_JSON + ' is not valid JSON: ' + (err && err.message));
    }
    if (!doc || !Array.isArray(doc.rows)) {
        throw new Error(PICKS_JSON + ' has no rows array');
    }
    const out = [];
    for (const row of doc.rows) {
        if (!row || typeof row.key !== 'string' || !row.key.length) continue;
        if (typeof row.prefabPath !== 'string' || !row.prefabPath.length) continue;
        out.push({
            key: row.key,
            prefabPath: row.prefabPath,
            isLoop: row.isLoop === true,
        });
    }
    if (!out.length) throw new Error(PICKS_JSON + ' yielded no usable rows');
    return out;
}

/**
 * The catalog keys THIS BUILD can actually resolve to a prefab.
 *
 * ⛔ WHY THE TAG FILE IS NOT ENOUGH, AND THIS IS NOT A THEORY - THE ORACLE CAUGHT IT.
 * Assets/Editor/VfxManualPicks.json is the owner's INTENT. The catalog is what the build
 * can RENDER, and they are not the same set: HovlVfxCatalogGenerator SKIPS a manual row
 * whose prefab does not load, which happens whenever the prefab lives in a GITIGNORED art
 * pack that is not imported on the machine that baked the catalog. Read at source
 * 2026-09-10: the catalog carries 169 resolvable rows and exactly ONE tagged key -
 * 'KnightShieldBuff_Aura' - has no row at all.
 *
 * Offering that key as a pick would let her choose an effect that renders NOTHING, with no
 * error on screen. That is CLAUDE.md section 16's failure exactly, and it is why the pool is
 * now derived from tag file INTERSECT catalog rather than the tag file alone.
 *
 * ⛔ THE PICK IS NEVER DELETED FROM HER JSON. Her tag is her record; the day the pack is
 * imported and the catalog rebaked, the key becomes resolvable and the SAME id starts being
 * offered again. That is the whole reason it keeps its reserved id instead of vanishing.
 *
 * The parse is deliberately narrow: a `- Key: <name>` line followed by its `Prefab:` line,
 * counted resolvable only when the reference has a non-zero fileID AND a guid. A null prefab
 * serialises as {fileID: 0}, which is precisely the "row exists but points at nothing" case
 * the runtime treats as a miss.
 *
 * @param {string} src contents of HovlVfxCatalog.asset
 * @returns {Set<string>}
 */
export function parseCatalogKeys(src) {
    if (typeof src !== 'string' || !src.length) {
        throw new Error(CATALOG_ASSET + ' was empty or unreadable');
    }
    const out = new Set();
    const re = /^\s*-\s*Key:\s*(.+?)\s*$\r?\n\s*Prefab:\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-fA-F]+))?/gm;
    for (let m = re.exec(src); m; m = re.exec(src)) {
        if (m[2] !== '0' && m[3]) out.add(m[1]);
    }
    if (out.size === 0) {
        // Loud, never an empty pool: silently yielding nothing would mark EVERY option
        // unresolved and empty the picker, which reads as "the feature is broken" rather
        // than "the parse stopped matching the asset's format".
        throw new Error(CATALOG_ASSET + ' yielded no resolvable keys - the asset format changed ' +
                        'or the file is not a HovlVfxCatalog. Refusing to emit an empty pool.');
    }
    return out;
}

/** The last path segment without its extension - what a human recognises. */
export function prefabLeaf(prefabPath) {
    const slash = prefabPath.lastIndexOf('/');
    let leaf = slash >= 0 ? prefabPath.slice(slash + 1) : prefabPath;
    if (leaf.toLowerCase().endsWith('.prefab')) leaf = leaf.slice(0, -'.prefab'.length);
    return leaf;
}

/**
 * Join today's picks onto the ids already assigned by a previous run.
 *
 * @param {Array} picks    parsePicks() output, file order
 * @param {object|null} previous the previously generated document, or null
 * @param {Set<string>|null} resolvable catalog keys this build can render (parseCatalogKeys)
 * @returns {Array} options, ascending by id
 */
export function assignIds(picks, previous, resolvable) {
    const idByKey = new Map();
    const retired = new Map();
    let maxId = 0;

    const prevOptions = previous && Array.isArray(previous.options) ? previous.options : [];
    for (const opt of prevOptions) {
        if (!opt || typeof opt.key !== 'string') continue;
        const id = Number(opt.id);
        if (!Number.isInteger(id) || id < 1) continue;
        idByKey.set(opt.key, id);
        retired.set(opt.key, opt);
        if (id > maxId) maxId = id;
    }

    const live = new Set(picks.map((p) => p.key));
    const out = [];

    for (const pick of picks) {
        let id = idByKey.get(pick.key);
        if (id === undefined) { id = ++maxId; idByKey.set(pick.key, id); }
        out.push({
            id: id,
            key: pick.key,
            prefab: prefabLeaf(pick.prefabPath),
            isLoop: pick.isLoop,
            retired: false,
            // NOT offered when this build's catalog cannot resolve the key. The entry stays so
            // the id is never handed to a different key, and so the file itself records WHY the
            // pick is missing from the picker - a pick that silently disappeared would look like
            // a generator bug rather than an un-imported art pack.
            unresolved: resolvable ? !resolvable.has(pick.key) : false,
        });
    }

    // An id whose key has left the tag file keeps its id RESERVED. Freeing it would
    // let a future key inherit it and silently re-point a row she already set.
    for (const [key, opt] of retired) {
        if (live.has(key)) continue;
        out.push({
            id: Number(opt.id),
            key: key,
            prefab: typeof opt.prefab === 'string' ? opt.prefab : '',
            isLoop: opt.isLoop === true,
            retired: true,
            unresolved: opt.unresolved === true,
        });
    }

    out.sort((a, b) => a.id - b.id);
    return out;
}

/** The exact bytes both copies should hold. LF, trailing newline, 4-space indent. */
export function renderJson(options) {
    return JSON.stringify({
        _generated: 'DO NOT HAND-EDIT. Produced by tools/gen-vfx-pick-options.mjs from ' +
            PICKS_JSON + '. Two byte-identical copies exist on purpose - ' + API_OPTIONS_JSON +
            ' is read by the Command Center and ' + RESOURCES_OPTIONS_JSON + ' is read by the ' +
            'build; the server cannot read a Unity asset and the build cannot reach api/_lib/. ' +
            'IDS ARE STABLE AND APPEND-ONLY: an id, once assigned, is never reassigned, and a ' +
            'key that leaves the tag file keeps its id reserved (retired:true) rather than ' +
            'freeing it, because a freed id would silently re-point a row the owner already ' +
            'set. An entry with unresolved:true is TAGGED BY THE OWNER but has no resolvable row ' +
            'in ' + CATALOG_ASSET + ' (usually a gitignored art pack that was not imported when ' +
            'the catalog was baked); it is NOT offered as a pick and its tag is never deleted, so ' +
            'importing the pack and rebaking makes the SAME id offerable again. ' +
            'test/vfx-pick-options.test.js pins both copies byte-for-byte.',
        source: PICKS_JSON,
        catalog: CATALOG_ASSET,
        options: options,
    }, null, 4) + '\n';
}

export function deriveFromDisk(repoRoot) {
    const root = repoRoot || REPO;
    const picks = parsePicks(fs.readFileSync(path.join(root, PICKS_JSON), 'utf8'));
    const resolvable = parseCatalogKeys(fs.readFileSync(path.join(root, CATALOG_ASSET), 'utf8'));
    const target = path.join(root, API_OPTIONS_JSON);
    let previous = null;
    if (fs.existsSync(target)) {
        try { previous = JSON.parse(fs.readFileSync(target, 'utf8')); } catch { previous = null; }
    }
    return assignIds(picks, previous, resolvable);
}

function main() {
    const check = process.argv.includes('--check');
    let options;
    try {
        options = deriveFromDisk(REPO);
    } catch (err) {
        console.log('VFX_PICK_OPTIONS_GEN_FAIL ' + (err && err.message));
        process.exitCode = 1;
        return;
    }

    const next = renderJson(options);
    const targets = [API_OPTIONS_JSON, RESOURCES_OPTIONS_JSON];

    if (check) {
        const drifted = targets.filter((rel) => {
            const abs = path.join(REPO, rel);
            return !fs.existsSync(abs) || fs.readFileSync(abs, 'utf8') !== next;
        });
        if (!drifted.length) {
            console.log('VFX_PICK_OPTIONS_GEN_OK options=' + options.length +
                ' offered=' + options.filter((o) => !o.retired && !o.unresolved).length +
                ' (checked, no drift)');
        } else {
            console.log('VFX_PICK_OPTIONS_DRIFT ' + drifted.join(' + ') + ' does not match ' +
                PICKS_JSON + ' - run: node tools/gen-vfx-pick-options.mjs');
            process.exitCode = 1;
        }
        return;
    }

    for (const rel of targets) {
        const abs = path.join(REPO, rel);
        fs.mkdirSync(path.dirname(abs), { recursive: true });
        fs.writeFileSync(abs, next, 'utf8');
    }
    const unresolved = options.filter((o) => o.unresolved).map((o) => o.id + ':' + o.key);
    console.log('VFX_PICK_OPTIONS_GEN_OK options=' + options.length +
        ' offered=' + options.filter((o) => !o.retired && !o.unresolved).length +
        (unresolved.length ? ' unresolved=[' + unresolved.join(' ') + ']' : '') +
        ' -> ' + targets.join(' + '));
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))) {
    main();
}
