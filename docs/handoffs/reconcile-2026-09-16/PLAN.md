# COMMIT PLAN — reconcile the 09-09..09-16 working tree — 2026-09-16

Produced by a **read-only** planning lane. Nothing was staged, committed, stashed, reset, checked
out, applied or edited; the only writes are the files in this directory.

- Branch `dev`, HEAD `c9327a210` (= `origin/dev`).
- Measured dirty set at the last generation run: **785 paths — 365 modified / 419 untracked
  (expanded) / 1 deleted**, of which **159 EXCLUDED** and **626 ASSIGNED** to nine groups. Verified
  **complete and disjoint**: 626 + 159 = 785, zero overlap, zero unassigned, and consecutive
  `regenerate.py` runs produce identical group counts.
- ⚠ **Every count in this document is a dated reading.** `regenerate.py` prints the live numbers and
  those are the authority — see the drift table immediately below.
- Whole set was inside the tree the **2026-09-15 22:00 gate** read (`COMPILE_GATE_OK`,
  `REGRESSION_OK 547/547`) and **APK 371627** was built over and felt-passed.

---

## ⛔ READ THIS FIRST: THE TREE IS MOVING UNDER YOU

The path lists in this directory are a **dated view, not the authority.** Measured during this
planning pass, with **no commit in between**:

| time | `git status --porcelain -uall` count |
|---|---:|
| start of pass | **732** |
| ~15 min later | **746** |
| ~20 min later | **748** |
| at generation | **749** |
| after writing this directory | **783** (about 32 of the +34 are this directory's own files; the rest are other lanes) |
| last generation run | **785** |

Another lane was landing WO-1763 (raid difficulty remote tunables) throughout — the added paths
include `Assets/_Modules/Core/Ops/RemoteTunables.cs`,
`Assets/_Modules/Village/Troops/RaidDifficultyTunables.cs`, `api/_lib/tunables.js`,
`WorkOrders/WORK_ORDER_1764_*.md`, `WORK_ORDER_1765_*.md` (two of them),
`docs/RAID_DIFFICULTY_LEVERS_2026-09-16.md`.

**A frozen path list is duplicated state and rots exactly like CLAUDE.md §2's stale WO-number block.
So the durable artifact here is the RULE SET, shipped as `regenerate.py`.** Before you execute a
single `git add`:

```
python docs/handoffs/reconcile-2026-09-16/regenerate.py
```

It rewrites every `*.paths.txt`, every `*.sha256`, `00-do-not-commit.paths.txt` and `MANIFEST.json`
from the live tree using the same rules, and prints the counts. Diff the regenerated lists against
the committed ones and eyeball the delta. **If the counts moved, the new lists win.**

Second consequence: **freeze the other lanes before you start.** Reconciling a tree that is still
being written produces a commit nobody can reproduce.

---

## Step 0 — `.gitattributes` FIRST, AND ALONE

`.gitattributes` is the one path that must land in its own commit before every group. Its diff adds
exactly one rule:

```
+# Generated SKU contract is LF-only, including a fresh Windows checkout.
+api/_lib/sku-catalog.generated.json text eol=lf
```

A text-attribute change decides how git normalises the files it governs, so it must be in effect
*before* those files are staged — never riding a lane commit. **Also note: `core.quotePath` is
unset, so `git status` quotes four untracked paths that contain spaces or mixed case** (`"docs/Codex
last work.md"`, `"docs/SCROLLS AND THE COMMAND LIBRARY.md"`, `"docs/SKR Vison.md"`,
`"docs/eVERYTHING dEEPsEEK.MD"`, plus three tracked ones). `regenerate.py` reads `-z` porcelain and
writes every list **unquoted**, which is why every `git add` below is spelled
`git --literal-pathspecs add --pathspec-from-file=` (the flag is a GIT-level option and is rejected
after the subcommand -- verified with `git add -n` today).

```powershell
git reset
git --literal-pathspecs add -- .gitattributes
git status --porcelain -- .gitattributes     # expect exactly:  M .gitattributes  (staged)
git commit -F "docs/handoffs/reconcile-2026-09-16/00-gitattributes.msg"
```

Mandatory pre-commit hygiene, before EVERY commit in this plan (memory
`git-apply-3way-stages-reset-before-commit`):

```powershell
git reset                                    # clear any index left by another lane
git diff --cached --quiet                    # MUST be silent; a non-empty index means stop
```

---

## Group order, counts and files

First claim wins, evaluated in this order. Each group has `<nn>-<name>.paths.txt` (one repo-relative
path per line, unquoted, untracked directories already expanded to files), `<nn>-<name>.msg` (the
commit message draft) and `<nn>-<name>.sha256` (the drift manifest).

| # | Group | Paths | List file |
|---|---|---:|---|
| 01 | food-to-stone (WO-1163) | **195** | `01-food-to-stone.paths.txt` |
| 02 | castle / storage / WO-1291 | **43** | `02-castle-storage-1291.paths.txt` |
| 03 | owned-town WO-1705 (+ Editor/OwnedTown* + proofs) | **116** | `03-owned-town-1705.paths.txt` |
| 04 | manage 2016 / webgl 1701 / raid floors 1703 / raid continuity 1704 | **10** | `04-ready-tickets-2016-1701-1703-1704.paths.txt` |
| 05 | localization + canonical JSON mirrors | **47** | `05-localization-canonical-mirrors.paths.txt` |
| 06 | serialized scenes / art / Addressables | **22** | `06-serialized-scenes-art-addressables.paths.txt` |
| 07 | generated evidence + docs + handoffs (incl. this directory + the 09-13 inventory) | **123** | `07-evidence-docs-handoffs.paths.txt` |
| 08 | local orchestration (`.ai/`, `.claude/`, `tools/`) | **11** | `08-local-orchestration.paths.txt` |
| 09 | **residual product source** — see the deviation note below | **59** | `09-residual-product-source.paths.txt` |
| — | EXCLUDED | **159** | `00-do-not-commit.paths.txt` |
| — | optional, off-sequence | 29 | `09b-proof-binaries-optional.paths.txt` |

Groups 01–06 and 08 have been stable across every regeneration; only 07 and 09 move, because 07
absorbs this directory's own growing file set and 09 absorbs whatever another lane just added.

### ⚠ DEVIATION FROM THE BRIEF, DECLARED (CLAUDE.md §11B.B)

The brief named eight groups (a)–(h). **A ninth was required and is named rather than hidden.** 58
dirty paths are runtime product source — `Assets/_Modules/Village/Troops/RaidLootTunables.cs`,
`Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs`,
`Assets/_Modules/Village/Items/CraftingPanelMvvm.cs`, `api/_lib/tunables.js`,
`run-unity-method.ps1`, `test/benefactors.test.js`, … — that no lane rule claims, by path **or** by
diff-token. Filing runtime `.cs` into group (g) *"generated evidence + docs"* would have been a
false label on the commit that carries it, so group **09 is an explicitly-named INTEGRATION
commit**. Its message says so. Two genuinely docs-shaped leftovers (`OVERNIGHT_EXEC_2026-09-12.md`,
`OVERNIGHT_ORDERS_2026-09-12.md`) did default into 07 and are listed in
`MANIFEST.json.defaulted_to_07`. Every path's rule is recorded in `MANIFEST.json.assignment_reason`.

**One label caveat, so the commit is not read as a claim it does not make:** the group-01
diff-content rule matches `\bStone\b` as well as `\bFood\b`, so a file whose only relevant change is
the *rough stone* raid-loot work (owner ruling 2026-09-09) can land in the "food-to-stone" commit.
First-claim-wins makes that harmless for *landing* the bytes — nothing is lost or duplicated — but
the commit subject is then broader than the file. Where it matters, `MANIFEST.json.assignment_reason`
names `diff-content lane token for 01` rather than a path match, which is the tell.

### Rules applied beyond the brief (state them so the next seat can check them)

- **`.meta` twins never classify alone** — a `.meta` inherits its partner's group, so no GUID is
  orphaned and no new asset lands meta-less (Unity would regenerate a GUID and break references).
  Verified: **no orphan metas.** All six untracked `.meta` files whose partner is tracked-and-clean
  (`Assets/_Modules/Core/Backend.meta`, `…/Troops/BreachOrderMarker.cs.meta`,
  `…/Walls/WallRuinPresenter.cs.meta`, `Assets/Editor/Regression/NavMeshReferenceRegression.cs.meta`,
  `Assets/Prefabs/Village/OwnerStorage.meta`, `Assets/Resources/OwnedTown.meta`) have an existing
  partner; they are folder metas or metas for files already in HEAD.
- **WO markdown rides its lane**, never group 07 — CLAUDE.md §11 puts the `**Status:**` flip in the
  same commit as the work, and the board is derived from it.
- **Mirror pairs travel together and were hash-verified** (below).
- **A records directory is not a lane.** `docs/handoffs/checkpoint-inventory-2026-09-13/` and this
  directory are pinned to 07 BEFORE any token rule runs. Without that pin, `ready-manage-2016.paths.txt`
  matched the group-04 token and `01-food-to-stone.paths.txt` matched the group-01 token, so each list
  file classified itself into the lane it merely describes. Caught and fixed today.

---

## Mirror-pair verification — PASSED

`Assets/Resources/Data/Canonical/<f>` ⇄ `Assets/StreamingAssets/Data/Canonical/<f>` must be
byte-identical. All **18** dirty canonical files were sha256-compared on both sides, 2026-09-16:
**18 of 18 MATCH, 0 differ, 0 missing twins** (36 paths, all in group 05).

`ar, building-tiers, canon-strings, de, en, es, fr, guide-content, ja, ko, pt-BR, quests, ru,
scene-configs, starter-settlement-layout, troops, tutorial/tutorial-steps, zh-Hans`

Re-prove it after `regenerate.py` and before staging group 05:

```powershell
Get-Content docs/handoffs/reconcile-2026-09-16/05-localization-canonical-mirrors.paths.txt |
  Where-Object { $_ -like 'Assets/Resources/Data/Canonical/*' } | ForEach-Object {
    $rel = $_ -replace '^Assets/Resources/Data/Canonical/',''
    $a = (Get-FileHash "Assets/Resources/Data/Canonical/$rel" -Algorithm SHA256).Hash
    $b = (Get-FileHash "Assets/StreamingAssets/Data/Canonical/$rel" -Algorithm SHA256).Hash
    if ($a -ne $b) { "MIRROR MISMATCH: $rel" }
  }
```

Silence = pass. Any line printed = **stop**; a half-mirror in history is a WebGL-vs-desktop data
split (Resources wins at load on every platform).

---

## MIXED FILES — hunks belong to more than one group. NOT SPLIT.

Detected by counting distinct lane tokens in each tracked file's `git diff -U0`. Per the brief, no
hunk was split: **each file is assigned to its EARLIEST group** and listed here so the lead knows
the commit boundary is approximate for these 18 files. The same list is in `MANIFEST.json.mixed`.

| File | Assigned | Lanes present in its diff |
|---|---|---|
| `Assets/_Modules/Core/State/SaveSchema.cs` | 01 | 01, 03 |
| `Assets/_Modules/Core/State/GameState.cs` | 01 | 01, 03 |
| `Assets/_Modules/Commerce/PackCatalog.cs` | 01 | 01, 03 |
| `Assets/_Modules/Village/BuildMode/BuildModeController.cs` | 01 | 01, 02, 03 |
| `Assets/_Modules/Village/Buildings/Progression/StorageStackView.cs` | 01 | 01, 02, 03 |
| `Assets/_Modules/Village/Buildings/Progression/BuildingUpgradePanelMvvm.cs` | 01 | 01, 03 |
| `Assets/_Modules/Village/Buildings/BuildTimerService.cs` | 01 | 01, 03 |
| `Assets/_Modules/Village/HUD/HudModelProducers.cs` | 01 | 01, 03 |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs` | 01 | 01, 03 |
| `Assets/Editor/CollectorStackPropCatalogBuilder.cs` | 01 | 01, 02 |
| `Assets/Editor/Regression/CollectorStackPropCatalogRegression.cs` | 01 | 01, 02 |
| `Assets/Editor/Regression/RetiredVocabularyRegression.cs` | 01 | 01, 03 |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | 01 | 01, 03 |
| `Assets/Editor/UICaptureLaunch.cs` | 01 | 01, 03, 04 |
| `Assets/_Modules/Village/Catalog/StructureFactory.cs` | 02 | 02, 03 |
| `Assets/_Modules/Village/BuildMode/BaseLayoutLoader.cs` | 02 | 02, 03 |
| `Assets/_Modules/Village/BuildMode/StructureTierVisual.cs` | 02 | 02, 03 |
| `docs/MASTER_CATALOG/village-systems.md` | 07 | 01, 02, 03 |

Two notes against the 09-13 inventory, which named `DataRegression.cs` and `StructureFactory.cs` as
the mixed pair:

- **`Assets/Editor/Regression/DataRegression.cs` is NO LONGER DIRTY** — it was committed between
  09-13 (`70e8cc8e`) and today (`c9327a210`). That hazard is closed.
- **`Assets/_Modules/Village/Catalog/StructureFactory.cs` is still dirty** and still mixed (lanes
  02 + 03). Assigned to 02.

**The index is clean today.** The 09-13 inventory recorded 37 already-staged paths and a
staged+unstaged split on those two files; `git status --porcelain` now shows no staged entry at all
(`git diff --cached --quiet` is silent), so that complication is gone.

---

## ⚠ STEP 1 — GATE THE WORKING TREE **BEFORE** THE COMMITS, NOT AFTER

Group 01 is the Food→Stone rename of the whole economy surface. A tree with 01 landed and 09 not
landed **will not compile**, so there is no point gating between groups — and gating only at the end
means a failure costs nine local reverts.

**The right order is gate-first, and the drift table above is why.** Every path these commits carry
already exists in the working tree; the excluded set (`Logs/`, `proof/`, `docs/pitch/`,
`.claude/settings.json`, `.gitattributes`) touches nothing that compiles. So **the working tree's
compiled bytes ARE the final tree's compiled bytes** — gate the tree once, up front, and the
`.sha256` check in each group then *proves* the committed bytes are the gated bytes. The single
Unity seat is also the natural lane-freeze this plan needs.

1. Freeze the other lanes.
2. Brace port first — the gate's scanner has no interpolated-string model:

```powershell
# every dirty .cs in the tree, from the group lists (exit 0 = clean; CLAUDE.md §1)
python tools/gate_brace.py (Get-Content "$D/0?-*.paths.txt" | Where-Object { $_ -like '*.cs' })
```

3. Run the Unity gate on the tree and **judge the MARKER on a FRESH log, never the exit code**
   (CLAUDE.md §8; memory `gates-report-success-without-proving-it`, `unity-logs-are-utf16-read-with-powershell`).
   Expect `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n> suites`. Add `UI_CAPTURE_OK` and open the PNGs
   if you care about groups 04 / 09, which move UI.
4. **Write the marker line, the log path and the time into every `<nn>-*.msg`**, replacing the
   `Gated: <FILL …>` placeholder each one ships with. Those placeholders exist because part of this
   tree — `RaidDifficultyTunables.cs`, `RemoteTunables.cs`, `RaidLootTunables.cs`,
   `RaidGarrisonSpawner.cs`, `api/_lib/tunables.js`, `RemoteTunablesDefaultsRegression.cs`,
   `WORK_ORDER_1764/1765` — **landed on 2026-09-16 and therefore postdates the 2026-09-15 22:00
   gate.** Asserting that older gate over these commits would be the exact hearsay §11B forbids.
   ⛔ **A `.msg` still containing `<FILL` must not be committed.** Check before each commit:
   `Select-String -Path "$D/0?-*.msg" -Pattern '<FILL' -Quiet` must be `False`.
5. Then run the sequence below. Do **not** re-gate between groups.

Only if a mid-sequence gate is demanded anyway: the one safe cut is 01 + 09 together, then the rest.

---

## The sequence

For each group in order 01 → 09. Paths are **read from the list file, never retyped.**

```powershell
$D = 'docs/handoffs/reconcile-2026-09-16'
$G = '01-food-to-stone'        # then 02-castle-storage-1291, 03-owned-town-1705,
                               # 04-ready-tickets-2016-1701-1703-1704,
                               # 05-localization-canonical-mirrors,
                               # 06-serialized-scenes-art-addressables,
                               # 07-evidence-docs-handoffs, 08-local-orchestration,
                               # 09-residual-product-source

# 1. index must be empty before staging (never git add -A)
git reset
git diff --cached --quiet; if (-not $?) { throw "index not empty - stop" }

# 2. drift check: the bytes must be the ones this plan classified.
#    Pure PowerShell on purpose - `sha256sum` is not a PS command, and when it IS on PATH via
#    Git's usr/bin an unset $LASTEXITCODE is $null, and `$null -ne 0` is TRUE, so an exit-code
#    test throws on success (memory `prove-the-success-path-not-just-the-refusal`).
#    `regenerate.py` deliberately omits this directory's own files from every .sha256: they are
#    rewritten by the run that produces the check, so hashing them would always report drift.
$bad = @()
foreach ($line in Get-Content "$D/$G.sha256") {
  $want, $rel = $line -split '  ', 2
  if (-not (Test-Path -LiteralPath $rel)) { $bad += "MISSING $rel"; continue }
  $have = (Get-FileHash -LiteralPath $rel -Algorithm SHA256).Hash
  if ($have -ne $want.ToUpper()) { $bad += "CHANGED $rel" }
}
if ($bad.Count) { $bad; throw "$G drifted since the plan was written - rerun regenerate.py" }

# 3. stage BY EXPLICIT PATH, from the file
git --literal-pathspecs add --pathspec-from-file="$D/$G.paths.txt"

# 4. prove what is staged equals the list, before committing
git diff --cached --name-only | Sort-Object > "$env:TEMP\staged.txt"
Get-Content "$D/$G.paths.txt" | Sort-Object > "$env:TEMP\wanted.txt"
Compare-Object (Get-Content "$env:TEMP\staged.txt") (Get-Content "$env:TEMP\wanted.txt")
# any output = STOP. (Expected benign case: a path git considers unchanged simply will not stage.)

# 5. commit from the drafted message file
git commit -F "$D/$G.msg"
```

Append the attribution line required by this session to each `.msg` before committing:

```
Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
```

### Board, in the same commits

Group 04 carries the four READY tickets' markdown; groups 01–03 carry theirs. Flip each WO's own
`**Status:**` line to the exact single-word label (memory
`status-label-is-a-fixed-word-rulings-go-in-prose`) **in the same commit as its work**, then
regenerate and commit the derived board with group 07:

```powershell
python tools/board_build.py      # then add BOARD.html to the 07 staging set
```

---

## Verification AFTER the whole sequence

```powershell
git status --porcelain -uall | Measure-Object -Line     # expect 159
git status --porcelain -uall | ForEach-Object { $_.Substring(3) } | Sort-Object > $env:TEMP\left.txt
# every remaining line must appear in 00-do-not-commit.paths.txt and nowhere else
```

**PASS = `git status` shows ONLY the excluded set** (159 paths: `.gitattributes` already committed
at step 0 so it is gone from the count — expect **158** after step 0 lands, plus anything other
lanes added while you worked). Then:

1. One fresh `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on the final tree, marker-judged.
2. `python tools/gate_brace.py` over every `.cs` in the nine commits.
3. `UI_CAPTURE_OK` and open the PNGs if any UI path moved (group 04, 09).
4. **Do NOT push yet.** `.githooks/pre-push` refuses a push whenever anything under `ServerData/`
   postdates `Builds/r2-parity.log` (CLAUDE.md §16). Nothing under `ServerData/` is in this
   reconciliation, so a docs+source push should pass untouched — but if it blocks, the remedy is
   `tools\r2-ship.ps1`, never a force. Push only on the owner's word.

---

## EXCLUSIONS — `00-do-not-commit.paths.txt` (159 paths, reason per line)

| Bucket | Paths | Reason |
|---|---:|---|
| `Logs/**` | 38 | **Log output AND secret-shaped.** `Logs/device/**/logcat_full.txt` carries live `firebaseAuthenticationToken` JWTs and `firebaseInstallationId` values — measured at `post-lane-a-370139/logcat_full.txt:123135` and `pull-20260914-172704-breach-success-playthrough/logcat_full.txt:131783`. Largest member 32.8 MB. |
| `docs/pitch/**` | 82 | **Generated deck build output** — every one of the 82 entries is under `docs/pitch/.pi-build/` (`candidate.pptx`, `final-*.png`, `*.inspect.ndjson`), 7.3 MB of build product. |
| `docs/.pi-deck-validation/**` | 5 | Generated pitch-deck validation JSON. |
| `proof/**` | 29 | **Optional, off-sequence.** 29 files, the PNGs near 4.9 MB each, ≈120 MB into LFS. PNG *is* LFS-tracked (`.gitattributes:4`) so it is legal, but the 09-13 inventory excludes raw `proof/` by default. `09b-proof-binaries-optional.paths.txt` re-includes them in one command if the lead wants the felt-test evidence in history. |
| `checkpoint-inventory-2026-09-13/ignored-assets-observed.paths.txt` | 1 | Generated 11.4 MB manifest, 124,494 lines of gitignored art-pack paths. |
| `checkpoint-inventory-2026-09-13/candidate-current-unity-tree.paths.nul` | 1 | NUL-delimited, and stale — describes the 09-13 tree at HEAD `70e8cc8e`. Its `.txt` sibling carries the same record readably. |
| `.gitattributes` | 1 | **Commit FIRST and ALONE** — step 0 above, not an exclusion in spirit. |
| `.claude/settings.json` | 1 | **LEAD RULING REQUIRED** — see below. |
| `docs/WARDROBE_ARCHITECTURE.zip` | 1 | **LEAD RULING REQUIRED** — see below. |

Counted from `00-do-not-commit.paths.txt` on 2026-09-16: 38 + 82 + 5 + 29 + 1 + 1 + 1 + 1 + 1 =
**159**, which matches the generator's own total.

*(`00-do-not-commit.paths.txt` is the exact line-by-line
authority and is regenerated with the rest.)*

### `.claude/settings.json` — the diff, for the lead's decision

It **deletes 19 lines** and adds none. Two removals:

1. the `UserPromptSubmit` hook
   `powershell … -File .claude/hooks/f8-prompt-check.ps1` (timeout 30);
2. the **entire `Stop` block**, i.e. the `.claude/hooks/f8-poll-rewake.ps1` async-rewake hook
   (`asyncRewake: true`, timeout 3600, `rewakeSummary: "New F8 capture in inbox"`,
   `statusMessage: "F8 passive listener armed"`).

That is the CLAUDE.md **§14 hook-enforced F8 passive listener**, for every seat in the repo, since
`.claude/settings.json` is committed and shared. §14 exists precisely because the per-turn-poll
discipline stopped being followed within a month. **Committing this quietly disarms live triage.**
Not committing it leaves the working tree permanently dirty on this one path. Lead call.

### `docs/WARDROBE_ARCHITECTURE.zip` — flagged for a lead ruling

The **only deleted path** in the tree (` D `). The 09-13 inventory states it is unrelated to the
Unity release and warns against turning it into an accidental deletion commit. Reminder from memory
`a-deletion-in-git-status-will-ship-if-you-build`: an unresolved ` D ` ships if you build over it —
so rule on it, do not leave it as "not mine". Options: commit the deletion in its own
`chore(docs): drop the superseded wardrobe zip`, or `git checkout -- docs/WARDROBE_ARCHITECTURE.zip`
to restore it. Either is one command; the plan does not choose.

### Large-file findings (`.gitattributes` LFS patterns re-read at source)

LFS covers `*.mp3 *.wav *.fbx *.png *.jpg *.jpeg *.JPEG *.tga *.mp4 *.psd` plus
`Assets/Firebase/**` and `Assets/GoogleSignIn/**` binaries. Files over 5 MB in the dirty set:

| Path | Size | Ruling |
|---|---:|---|
| `Logs/device/post-lane-a-370139/logcat_full.txt` | 32.8 MB | excluded (logs) |
| `Logs/device/pull-20260914-172704-…/logcat_full.txt` | 27.2 MB | excluded (logs) |
| `Logs/device/signin-test/signin-seeker.mp4` | 24.9 MB | excluded (logs; LFS-eligible anyway) |
| `Logs/device/watch-breachflow-369984/logcat_live.txt` | 12.7 MB | excluded (logs) |
| `…/checkpoint-inventory-2026-09-13/ignored-assets-observed.paths.txt` | 11.4 MB | excluded (generated) |
| `Logs/device/watch-20260914-breach-diag/logcat_live.txt` | 8.9 MB | excluded (logs) |
| **`Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset`** | **8.5 MB** | **INCLUDED in group 05.** It is **already tracked at HEAD** (`git ls-files` confirms), so this introduces no new large binary; `git diff --numstat` reports a **2-line** change. `.asset` is not an LFS pattern, but that decision was made in an earlier commit — excluding it now would ship a broken locale fallback. |
| `Logs/device/watch-wall-tower-fix-confirm/logcat_live.txt` | 8.3 MB | excluded (logs) |
| `Logs/device/pull-20260914-085211/footprint_issue.png` | 6.8 MB | excluded (logs) |
| `Logs/device/pull-20260914-092756-pallets/pallets.png` | 5.5 MB | excluded (logs) |

`tools/localization/__pycache__/` is correctly gitignored — proved with
`git check-ignore -v` → `.gitignore:718: __pycache__/`.

### One orphan flagged, not resolved

`Assets/Scenes/Main_Castle_Overworld/NavMesh-OuterWorld_NavMeshSurface.asset` (1.5 MB, untracked,
GUID `6d1a44b74e916ce41b25db55656f1459`) is **not referenced by the scene at HEAD** —
`git grep <guid> HEAD -- Assets/Scenes/Main_Castle_Overworld.unity` returns nothing, and that scene
was committed between 09-13 and today. So it is most likely a leftover bake artifact. It is parked
in group 06 with its `.meta`; committing it is harmless, dropping it is also safe. Lead's call —
flagged because memory `bake-marker-can-be-green-on-the-wrong-operation` says to judge bake content,
not markers.

---

## Delta vs. the 09-13 inventory (never trust the old lists blindly)

Against the union of the 09-13 review lists (712 dirty paths at HEAD `70e8cc8e`):

- **+142 paths are NEW since 09-13** (and more since; see the drift table). Headlines: the four
  `Assets/Prefabs/Village/OwnerStorage/*_Pallet.prefab` + metas, the storage-pallet proofs
  (`StoragePalletFillPlayProof.cs`, `StoragePalletPlacementProof.cs`),
  `Assets/Editor/CollectorOwnershipPlayProof.cs`, `RaidBreachTapLiveProof.cs`, the Village2/Village3
  builders, `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs`, `PolishBonusProvider.cs`,
  `CurrencySkinResolver.cs`, `WeaponOrientHelper.cs`, `.gitattributes`, `.claude/settings.json`,
  the whole 09-13 inventory directory itself, and ~33 `Logs/device/**` captures.
- **−122 paths are GONE since 09-13** — committed in the meantime. Headlines:
  `Assets/Editor/Regression/DataRegression.cs` (closing the 09-13 mixed-file hazard),
  `Assets/Scenes/Main_Castle_Overworld.unity`, every `Assets/Scenes/RaidBase_*.unity`,
  `Assets/Scenes/OwnedTown_IronBastion.unity`, `ProjectSettings/ProjectSettings.asset`,
  `ProjectSettings/EditorBuildSettings.asset`, `Assets/_Modules/HUD/AdminOverlay.cs`, the whole
  `tools/hybrid-orchestrator/**` tree (30 paths), `api/game/save.js`,
  `Assets/StructureContent/OwnerCastleMaterials/**`.

Do **not** import from the three local-only snapshot branches
`local/night-build-20260913-23*` (`dfe0c7403`, *"Snapshot integrated castle and night release
candidate"*). They were used as evidence of intent only; nothing was taken from them, and nothing
should be.

---

## Files in this directory

| File | What it is |
|---|---|
| `PLAN.md` | this document |
| `FOOD_REMOVAL_PROOF.md` | the Food/Stone audit and verdict |
| `regenerate.py` | **the authority** — re-derives every list below from the live tree |
| `00-do-not-commit.paths.txt` | exclusions, one path + reason per line (strip the reason before feeding git) |
| `01..09-*.paths.txt` | per-group pathspec files, unquoted, expanded |
| `01..09-*.msg` | per-group commit message drafts |
| `01..09-*.sha256` | per-group drift manifests for `sha256sum -c` |
| `09b-proof-binaries-optional.paths.txt` | the `proof/` PNGs, off-sequence |
| `MANIFEST.json` | machine-readable: counts, per-path assignment reason, mixed list, meta findings |

This directory is itself untracked and is assigned to **group 07**, so it lands with the work.
