#!/usr/bin/env python
"""
Reconciliation group generator - READ ONLY against git.

Re-derives the dirty set with `git status --porcelain -uall -z` and writes, into this
directory, one `<nn>-<group>.paths.txt` per commit group plus `00-do-not-commit.paths.txt`
and `MANIFEST.json`.

WHY THIS SCRIPT EXISTS AND THE .paths.txt FILES ARE NOT THE AUTHORITY
--------------------------------------------------------------------
The working tree is LIVE. Measured during the 2026-09-16 planning pass, with no commit in
between: 732 dirty paths at 11:0x, 746 a few minutes later, 748 at the end - another lane was
landing WO-1763 (raid difficulty remote tunables) the whole time. A hand-frozen path list is
duplicated state and rots exactly like the stale WO-number block in CLAUDE.md section 2.
The RULES are the durable artifact; the lists are a dated view of them.

    python docs/handoffs/reconcile-2026-09-16/regenerate.py

Run it from the repo root immediately before executing PLAN.md, then diff the regenerated
lists against the committed ones. It stages nothing, commits nothing and edits no tracked file.
"""

import os
import re
import json
import hashlib
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
INV = 'docs/handoffs/checkpoint-inventory-2026-09-13'
SELF_DIR = 'docs/handoffs/reconcile-2026-09-16'


def run(args):
    p = subprocess.run(args, capture_output=True, text=True, encoding='utf-8',
                       errors='replace', cwd=REPO)
    return p.stdout


def git_status():
    out = run(['git', 'status', '--porcelain', '-uall', '-z'])
    entries = []
    for e in out.split('\0'):
        if not e.strip():
            continue
        entries.append((e[:2], e[3:]))
    return entries


def seed(name):
    fn = os.path.join(REPO, INV, name)
    if not os.path.exists(fn):
        return set()
    return set(l.strip().replace('\\', '/') for l in open(fn, encoding='utf-8') if l.strip())


GROUPS = [
    ('01', 'food-to-stone'),
    ('02', 'castle-storage-1291'),
    ('03', 'owned-town-1705'),
    ('04', 'ready-tickets-2016-1701-1703-1704'),
    ('05', 'localization-canonical-mirrors'),
    ('06', 'serialized-scenes-art-addressables'),
    ('07', 'evidence-docs-handoffs'),
    ('08', 'local-orchestration'),
    ('09', 'residual-product-source'),
]

# Path-shape rules, evaluated in group order. First claim wins.
TOK = {
    '01': re.compile(r'food[-_ ]?to[-_ ]?stone|migrate-stone|migrate-economy|migrate-wallet'
                     r'|inventory-food-migration|food-migration|FOOD_TO_STONE|1163', re.I),
    '02': re.compile(r'StoragePallet|StorageStack|OwnerStorage|Pallet|Footprint|CollectorStack'
                     r'|1291|SyntyStructure|storage', re.I),
    '03': re.compile(r'OwnedTown|OwnedBase|OwnedTemplate|OwnedStructure|CollectorOwnership'
                     r'|Authored(Barracks|Ghost)|FreshTown|1705', re.I),
    '04': re.compile(r'\b(2016|1701|1703|1704)\b|Manage|webgl|RaidWallContinuity'
                     r'|RaidGroundCoverage|RaidFloor', re.I),
    '05': re.compile(r'Localization|/Data/Canonical/|LocaleFallback|GameStrings', re.I),
    '06': re.compile(r'\.unity$|\.prefab$|\.mat$|AddressableAssetsData|/Scenes/|/Prefabs/'
                     r'|StructureContent|NavMesh', re.I),
    '07': re.compile(r'^docs/|^WorkOrders/|^BOARD|^CLI_LANES|^MASTER_PIPELINES|^KEY_FACTS'
                     r'|^PIPELINE_STATE|^CANON_|^OVERNIGHT|\.RESULT\.md$', re.I),
    '08': re.compile(r'^\.ai/|^\.claude/|^tools/', re.I),
    '09': re.compile(r'(?!)'),
}

# Diff-CONTENT rules, for paths whose name carries no lane token.
CONTENT = {
    '01': re.compile(r'\bFood\b|\bStone\b|food-to-stone|LegacyFood|PaidFood', re.I),
    '02': re.compile(r'Pallet|StorageStack|Footprint|OwnerStorage|CollectorStack', re.I),
    '03': re.compile(r'OwnedTown|OwnedBase|OwnedTemplate|Authored', re.I),
    '04': re.compile(r'ManageWorkspace|WebGl|WebGL|RaidWallContinuity|RaidGround|RaidFloor', re.I),
}

# A WO markdown travels with its lane: CLAUDE.md section 11 puts the Status flip in the same
# commit as the work, so the ticket file may never drift into the docs group.
WO_LANE = [
    (re.compile(r'FOOD_TO_STONE', re.I), '01'),
    (re.compile(r'_1291_', re.I), '02'),
    (re.compile(r'_1705_', re.I), '03'),
    (re.compile(r'_(2016|1701|1703|1704)_', re.I), '04'),
]

DOCSHAPE = re.compile(r'^docs/|^WorkOrders/|\.md$|^BOARD|^OVERNIGHT', re.I)


def build():
    entries = git_status()
    status = dict((p, xy) for xy, p in entries)
    paths = [p for _, p in entries]

    excl = {}

    def ex(p, reason):
        excl.setdefault(p, reason)

    for p in paths:
        if p == '.gitattributes':
            ex(p, 'COMMIT FIRST AND ALONE - see PLAN.md step 0. One added LF rule for '
                  'api/_lib/sku-catalog.generated.json; a text attribute must land before '
                  'anything it governs, so it never rides a lane commit.')
        elif p == '.claude/settings.json':
            ex(p, 'LEAD RULING REQUIRED - the diff DELETES 19 lines: the UserPromptSubmit '
                  'f8-prompt-check.ps1 hook and the whole Stop/f8-poll-rewake.ps1 block. That '
                  'disarms the CLAUDE.md section 14 hook-enforced F8 listener for every seat.')
        elif p == 'docs/WARDROBE_ARCHITECTURE.zip':
            ex(p, 'DELETION, LEAD RULING REQUIRED - the only deleted path in the tree. The 09-13 '
                  'inventory calls it unrelated to the Unity release and warns against an '
                  'accidental deletion commit.')
        elif p.startswith('Logs/'):
            ex(p, 'LOG OUTPUT AND SECRET-SHAPED - Logs/device/**/logcat_full.txt carries live '
                  'firebaseAuthenticationToken JWTs and firebaseInstallationId values '
                  '(measured at post-lane-a-370139/logcat_full.txt:123135 and '
                  'pull-20260914-172704-breach-success-playthrough/logcat_full.txt:131783). '
                  'Largest member 32.8 MB.')
        elif p.startswith('docs/pitch/'):
            ex(p, 'GENERATED DECK BUILD OUTPUT - every member is under docs/pitch/.pi-build/ '
                  '(candidate.pptx, final-*.png, *.inspect.ndjson). 7.3 MB of build product.')
        elif p.startswith('docs/.pi-deck-validation/'):
            ex(p, 'GENERATED pitch-deck validation output; excluded with docs/pitch.')
        elif p == INV + '/ignored-assets-observed.paths.txt':
            ex(p, 'GENERATED 11.4 MB manifest, 124,494 lines of gitignored art-pack paths. Not '
                  'source, not readable evidence; excluded by size.')
        elif p == INV + '/candidate-current-unity-tree.paths.nul':
            ex(p, 'NUL-DELIMITED manifest, and stale - it describes the 09-13 tree at HEAD '
                  '70e8cc8e. Its .txt sibling carries the same record in a readable form.')
        elif p.startswith('proof/'):
            ex(p, 'OPTIONAL, NOT IN THE DEFAULT SEQUENCE - raw device screenshots, about 25 PNGs '
                  'near 4.9 MB each (~120 MB into LFS). PNG is LFS-tracked (.gitattributes:4) so '
                  'it is legal, but the 09-13 inventory excludes raw proof/ by default. '
                  'Re-include via 09b-proof-binaries-optional.paths.txt.')

    seeds = {
        '01': seed('food-to-stone-migration.paths.txt'),
        '02': seed('prior-castle-storage-footprint-review.paths.txt'),
        '03': seed('prior-owned-town-1705.paths.txt'),
        '04': (seed('ready-manage-2016.paths.txt') | seed('ready-webgl-1701-review-fence.paths.txt')
               | seed('ready-raid-floors-1703.paths.txt')
               | seed('ready-raid-continuity-1704.paths.txt')),
        '05': (seed('localization-cross-lane-review.paths.txt')
               | seed('canonical-data-review-mirror-pairs.paths.txt')),
        '06': seed('serialized-scene-art-addressables-review.paths.txt'),
        '07': (seed('generated-evidence-not-runtime.paths.txt')
               | seed('ticket-board-courier-docs.paths.txt')),
        '08': seed('local-orchestration-review-exclude-default.paths.txt'),
        '09': set(),
    }

    assign, why = {}, {}

    def claim(p, g, reason):
        if p not in assign:
            assign[p] = g
            why[p] = reason

    order = [g for g, _ in GROUPS]
    metas = [p for p in paths if p.endswith('.meta') and p not in excl]
    nonmeta = [p for p in paths if p not in excl and not p.endswith('.meta')]

    # pass 0: this directory's own artifacts go to 07 BEFORE any token rule sees them.
    # Without this, "01-food-to-stone.paths.txt" matches the group-01 path token and every list
    # file classifies itself into the lane it describes.
    for p in nonmeta:
        if p.startswith(SELF_DIR + '/'):
            claim(p, '07', 'this reconciliation directory travels with the docs commit')
        elif p.startswith(INV + '/'):
            claim(p, '07', 'the 09-13 checkpoint inventory is a record, not a lane; its list '
                           'filenames would otherwise match the lane they describe')

    for p in nonmeta:
        if p.startswith('WorkOrders/'):
            for rx, g in WO_LANE:
                if rx.search(p):
                    claim(p, g, 'WO markdown rides its lane (CLAUDE.md section 11)')
                    break

    for g in order:
        for p in nonmeta:
            if p in seeds[g]:
                claim(p, g, 'seeded from the 09-13 inventory list for ' + g)

    for g in order:
        for p in nonmeta:
            if TOK[g].search(p):
                claim(p, g, 'path token for ' + g)

    diffs = {}

    def diff_of(p):
        if p not in diffs:
            diffs[p] = run(['git', 'diff', '-U0', '--', p])
        return diffs[p]

    for g in ['01', '02', '03', '04']:
        for p in nonmeta:
            if p in assign or status.get(p, '').strip() != 'M':
                continue
            if CONTENT[g].search(diff_of(p)):
                claim(p, g, 'diff-content lane token for ' + g)

    defaulted, residual = [], []
    for p in nonmeta:
        if p in assign:
            continue
        if DOCSHAPE.search(p):
            claim(p, '07', 'DEFAULTED (docs-shaped, no lane rule matched)')
            defaulted.append(p)
        else:
            claim(p, '09', 'RESIDUAL product source - no path rule and no diff token claimed it')
            residual.append(p)

    clean_partner_metas = []
    for m in metas:
        partner = m[:-5]
        if partner in assign:
            claim(m, assign[partner], 'meta twin of ' + partner)
        elif partner in excl:
            ex(m, 'meta twin of an excluded path')
        else:
            placed = False
            for g in order:
                if TOK[g].search(m):
                    claim(m, g, 'meta whose partner is tracked and clean; placed by path token')
                    placed = True
                    break
            if not placed:
                claim(m, '06', 'meta whose partner is tracked and clean; parked with serialized assets')
            clean_partner_metas.append([m, partner, os.path.exists(os.path.join(REPO, partner))])

    mixed = []
    for p in paths:
        if p in excl or status.get(p, '').strip() != 'M':
            continue
        if not re.search(r'\.(cs|json|js|mjs|md|ps1|py)$', p):
            continue
        hits = sorted(g for g, rx in CONTENT.items() if rx.search(diff_of(p)))
        if len(hits) >= 2:
            mixed.append([p, assign.get(p), hits])

    return dict(status=status, paths=paths, excl=excl, assign=assign, why=why,
                mixed=mixed, defaulted=defaulted, residual=residual,
                clean_partner_metas=clean_partner_metas)



# The gate line is a PLACEHOLDER on purpose. Part of this tree postdates the 2026-09-15 22:00 gate
# (WO-1763 tunables landed 2026-09-16 during the planning pass), so asserting that gate over every
# group would be exactly the hearsay CLAUDE.md section 11B forbids. The lead fills it from the
# FRESH log of the gate run in PLAN.md step 1. A message still carrying <FILL> must not be committed.
GATE_LINE = ('\nGated: <FILL - COMPILE_GATE_OK + REGRESSION_OK n/n, log path, HH:MM, run on this '
             'exact tree before any commit in this sequence>\n')

MSG = {
    '01': (
        'refactor(economy): WO-1163 Food-to-Stone - one Stone identity over the frozen internal '
        'Food save slot\n\n'
        'The internal Food resource becomes player-facing Stone across the whole economy surface: '
        'wallet, collectors, offline harvest, raid and wave payouts, build and upgrade baskets, '
        'quests, packs, troop and gear catalogs, plus every regression that asserts on those '
        'shapes. The SAVE WIRE DOES NOT MOVE - the persisted key stays "Food" behind '
        '[JsonProperty("Food")] / [FormerlySerializedAs("Food")] / [EnumMember(Value = "Food")] '
        'and the api/ whitelists keep the food column, so existing saves and the Neon backend '
        'load unchanged; only the C# member and the displayed word are Stone. Carries the '
        'migration scripts (tools/migrate-*.py, tools/inventory-food-migration.py) and '
        'docs/food-migration/ for the record.\n'),
    '02': (
        'feat(town): castle, storage pallets and footprint reconciliation (WO-1291 lane)\n\n'
        'The owner-authored castle layout, the storage pallet props beside the collectors, the '
        'collector stack prop catalog and the footprint cache. Storage containers stay regular '
        'structures on the wood+iron basket (WO-947) and the pallet sizes match the owner '
        'hand-authored models rather than a generated ladder. WO-1291 does NOT reapply the Synty '
        'storefront replacement the owner rejected - the preserved Tripo castle is the authority.\n'),
    '03': (
        'feat(town): WO-1705 owned town - authored base, reconstruction, move and pose proofs\n\n'
        'The captured-player-town flow: OwnedTown scene builders and manifest bakes, the owned '
        'template identity bake, the practice scene, and the Editor proofs and regressions that '
        'pin construction, ghost preview, barracks provenance, fresh-town visuals and the pose or '
        'move round trip. Untracked manifests and scenes here are PRODUCT INPUTS, not disposable '
        'output, and travel with their .meta twins so Unity keeps the GUIDs.\n'),
    '04': (
        'fix(ui,raid): WO-2016 manage screen, WO-1701 webgl hero art, WO-1703 raid floors, '
        'WO-1704 raid continuity\n\n'
        'Four READY tickets whose files are disjoint from each other: the unified Manage/Queues '
        'workspace panel, the WebGL hero-art Addressables load path, raid floor coverage and raid '
        'wall continuity. Each ticket markdown rides this commit with its Status line flipped, '
        'per CLAUDE.md section 11 - the board is derived from those lines '
        '(python tools/board_build.py).\n'),
    '05': (
        'chore(i18n): localization tables and the canonical Resources/StreamingAssets mirrors\n\n'
        'All ten locale files plus the canonical data twins. THE MIRROR PAIRS TRAVEL TOGETHER AND '
        'ARE BYTE-IDENTICAL: 18 of 18 dirty files under Assets/Resources/Data/Canonical were '
        'sha256-compared against their Assets/StreamingAssets/Data/Canonical partner on '
        '2026-09-16 and every pair matched, so no half-mirror lands. Resources wins at load on '
        'every platform; the StreamingAssets copy alone changes nothing. Zero occurrences of '
        '"food" survive in canon-strings.json or in any of the ten locale files - see '
        'FOOD_REMOVAL_PROOF.md. Zero occurrences in the eight Unity Localization string tables '
        'under Assets/Localization/Tables/ either.\n'),
    '06': (
        'chore(art): serialized scenes, prefabs, materials and Addressables settings\n\n'
        'The serialized side of the validated tree: scene and prefab .unity/.prefab bytes, '
        'materials, navmesh assets and the AddressableAssetSettings. Every asset travels with its '
        '.meta so no GUID is regenerated. These files were saved by the editor, never hand-edited '
        '(CLAUDE.md section 3).\n'),
    '07': (
        'docs(board): reconcile the 09-09..09-16 evidence, handoffs and ticket record\n\n'
        'Work orders, RESULT files, handoff notes, MASTER_CATALOG updates, the 09-13 checkpoint '
        'inventory and this 09-16 reconciliation plan. No runtime code. Regenerate BOARD.html '
        '(python tools/board_build.py) in this same commit so the derived board matches the '
        'Status lines the lane commits flipped.\n'),
    '08': (
        'chore(tools): local orchestration and runner scripts\n\n'
        'The .ai/ context notes, tool and runner scripts (run-unity-method.ps1, '
        'tools/run-unity-playmode.ps1, tools/regression/checkin_gate.ps1, tools/art/ verifiers, '
        'tools/localization/ helpers, tools/status-post.mjs). .claude/settings.json is '
        'DELIBERATELY EXCLUDED and awaits a lead ruling - see 00-do-not-commit.paths.txt.\n'),
    '09': (
        'feat(game): residual 09-11..09-16 product source outside the named lanes\n\n'
        'Everything in the validated tree that no lane rule claimed by path or by diff token: '
        'raid and troop tunables, HUD and panel work, hero and crafting surfaces, api tunable '
        'manifests, and their tests. This is an INTEGRATION commit, deliberately named as such '
        'rather than split on guesses about hunk ownership. It is the residue of committing a '
        'validated whole, not a lane. NOTE: some members landed on 2026-09-16 (WO-1763 remote '
        'tunables) and therefore postdate the 2026-09-15 22:00 gate - which is why the gate line '
        'below is filled from a FRESH run, not copied.\n'),
}


def main():
    d = build()
    written = []
    for g, name in GROUPS:
        members = sorted(p for p in d['paths'] if d['assign'].get(p) == g)
        fn = os.path.join(HERE, '%s-%s.paths.txt' % (g, name))
        with open(fn, 'w', encoding='utf-8', newline='\n') as f:
            for p in members:
                f.write(p + '\n')
        with open(os.path.join(HERE, '%s-%s.msg' % (g, name)), 'w',
                  encoding='utf-8', newline='\n') as f:
            f.write(MSG[g].rstrip('\n') + '\n' + GATE_LINE)
        # sha256 drift manifest so the lead can prove the bytes did not move before `git add`
        with open(os.path.join(HERE, '%s-%s.sha256' % (g, name)), 'w',
                  encoding='utf-8', newline='\n') as f:
            for p in members:
                # SELF_DIR files are what the drift check is FOR, not what it guards: they are
                # rewritten by this very run, so hashing them guarantees a false "drifted" throw.
                if p.startswith(SELF_DIR + '/'):
                    continue
                full = os.path.join(REPO, p)
                if os.path.isfile(full):
                    h = hashlib.sha256(open(full, 'rb').read()).hexdigest()
                    f.write('%s  %s\n' % (h, p))
        written.append((g, name, len(members)))

    with open(os.path.join(HERE, '00-do-not-commit.paths.txt'), 'w',
              encoding='utf-8', newline='\n') as f:
        f.write('# EXCLUDE list - one path per line, then two spaces and the reason.\n')
        f.write('# NOT a pathspec file: strip the reason before feeding any path to git.\n')
        f.write('# Regenerate: python docs/handoffs/reconcile-2026-09-16/regenerate.py\n')
        for p in sorted(d['excl']):
            f.write('%s  # %s\n' % (p, d['excl'][p]))

    with open(os.path.join(HERE, '00-gitattributes.msg'), 'w',
              encoding='utf-8', newline='\n') as f:
        f.write('chore(git): normalise api/_lib/sku-catalog.generated.json to LF\n\n'
                'One added text attribute, committed alone and before every lane commit so the '
                'normalisation is in effect for every file it governs.\n')

    opt = sorted(p for p in d['excl'] if p.startswith('proof/'))
    with open(os.path.join(HERE, '09b-proof-binaries-optional.paths.txt'), 'w',
              encoding='utf-8', newline='\n') as f:
        for p in opt:
            f.write(p + '\n')

    json.dump({
        'generated_by': 'docs/handoffs/reconcile-2026-09-16/regenerate.py',
        'head': run(['git', 'rev-parse', 'HEAD']).strip(),
        'total_dirty_paths': len(d['paths']),
        'excluded': len(d['excl']),
        'assigned': sum(n for _, _, n in written),
        'groups': [{'id': g, 'name': n, 'count': c} for g, n, c in written],
        'mixed': d['mixed'],
        'defaulted_to_07': d['defaulted'],
        'residual_09': d['residual'],
        'metas_with_clean_partner': d['clean_partner_metas'],
        'assignment_reason': d['why'],
    }, open(os.path.join(HERE, 'MANIFEST.json'), 'w', encoding='utf-8'), indent=1, sort_keys=True)

    print('HEAD              ', run(['git', 'rev-parse', 'HEAD']).strip())
    print('dirty paths       ', len(d['paths']))
    print('excluded          ', len(d['excl']))
    print('assigned          ', sum(n for _, _, n in written))
    for g, n, c in written:
        print('  %s-%-42s %4d' % (g, n, c))
    print('mixed             ', len(d['mixed']))
    for m in d['mixed']:
        print('   %-70s -> %s  lanes %s' % (m[0], m[1], ','.join(m[2])))
    print('residual 09       ', len(d['residual']))
    print('defaulted to 07   ', len(d['defaulted']))


if __name__ == '__main__':
    main()
