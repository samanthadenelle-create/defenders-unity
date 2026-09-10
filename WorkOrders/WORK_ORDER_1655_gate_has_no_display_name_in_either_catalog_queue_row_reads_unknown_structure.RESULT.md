# WO-1655 RESULT — `gate` had no display name because `gate` IS NOT A STRUCTURE ID

**Date:** 2026-09-10 · **Lane:** CATALOG-GATE (isolated worktree `agent-a54531f37537f7517`)
**Base:** `dev` @ `efc56f67c` (ff-only merge; `git merge-base --is-ancestor 7bf8f532e HEAD` → **ANCESTOR_OK**)
**Status:** IMPLEMENTED - awaiting gate. Edit-only. **No Unity run, no gate, no commit.**

---

## 0. ⛔ THE TICKET'S STATED CAUSE WAS WRONG, AND THAT IS THE HEADLINE

WO-1655 §2 says *"The structure id `gate` is absent from both name sources"* and §3 asks for a catalog
row. **Read at source this session, that premise does not hold, and following it would have shipped a
duplicate structure.**

| Read this session | Finding |
|---|---|
| `Assets/Resources/Data/Canonical/structures-catalog.json` (29 rows dumped) | row **`gate_stone`** / displayName **"Stone Gate"** / type **Gate** / manageArtKey `building-stone-gate`. **The gate IS authored and IS named.** |
| `Assets/Resources/Data/Canonical/building-tiers.json` | ladders are exactly `arcane-tower, armorer, barracks, forge, lumbermill, farm`. No tower, wall or gate — **by design**; `ManageScreenVM.cs:986-993` says so in its own words. |
| `Assets/Editor/UICaptureLaunch.cs:8170` (HEAD) | `queue.Enqueue(JobKind.Repair, ChannelId.Builder, "gate:4:1", 180d);` — **the ONLY producer of `gate:4:1` in the tree.** |
| `grep -rn 'JobKind\.Repair'` over all of `Assets/` | **7 hits, and not one is a live enqueue.** Two are capture fixtures, one is a `ChannelId` assert, the rest are `switch` arms in `BuildTimerService.cs:2167` / `ObsidianQueueHud.cs:541,558`. **`_Modules/` enqueues no Repair job at all.** |
| `Assets/_Modules/Village/Walls/WallRepairController.cs` (`FallbackGateCatalogId`) | `= "gate_stone"` — **the repo already treats `gate_stone` as the canonical gate row.** |

**So the queue row read `Unknown structure` because a TEST FIXTURE asked the catalog a question in
the wrong language.** The VM behaved perfectly and said so out loud.

### Acceptance #4, answered explicitly: `gate_stone` is canonical. `gate` is not a structure id at all.

`gate` is the **damage-states TELL key** — a different vocabulary that happens to spell the same word:
- `Assets/Resources/Data/Canonical/damage-states.json` (per-type thresholds),
- `WallRepairController.DamageTellKeyFor(RepairTargetKind.Gate) → "gate"` (`:552`),
- `RepairAvailabilityProbe.AddIfBurning<Gate>(into, "gate", …)` (`:300`, `:404`),
- `StructureDamageVisuals.RegisterRepairables<Gate>("gate")` (`:753`).

**No save key was renamed and none needed to be.** Every other `"gate"` in canonical JSON was opened
this session and none is a structure catalog id:

| File | Field carrying `gate` | What it actually is |
|---|---|---|
| `scene-configs.json` | `raidDress.gate` | a **prefab name** (`"wall_straight_gate"`, `"SM_Bld_Castle_Wall_Gate_01"`) — art dress |
| `garrison-recipes.json` | `layout.courtyard.gate` = `"main"`; `props[]` includes `"gate"` | enemy-stronghold **layout/prop role** names (`EnemyStrongholdBuilder.cs:1167` resolves them to prefabs) |
| `realm-map.json` | `gate` | a **RegionGate unlock condition** (`{"kind":"bestWave","value":3}`) — `RealmMapCatalog.cs:97` |

**⛔ Adding a `gate` row was therefore rejected on purpose.** It would have minted a phantom palette
tile, made `CatalogRegistry.OfType(Gate)` return two structures for one gate, and been exactly the
alias-by-another-name `ManageArt.cs:306` forbids — the rule the ticket itself cites.

### And it is the FOURTH fake id in that one seeder

`SeedManageCaptureQueue`'s own comments record three predecessors: the colon-shape tower key
`"tower_ground_archer:7:0"` (WO-1422), the invented perk `warding`, the invented troop `militia`
(WO-1564). Three prose warnings in a row did not stop the fourth. **That is why this ticket's
deliverable is an oracle, not a data row.**

---

## 1. WHAT CHANGED

### `Assets/Editor/UICaptureLaunch.cs` — the one-token fix (+ its RCA in the file's own voice)
```
-            queue.Enqueue(JobKind.Repair, ChannelId.Builder, "gate:4:1", 180d);
+            [27 lines of comment: the four-fake-ids history, the two vocabularies,
+             the resolution walk, and why no catalog row was added]
+            queue.Enqueue(JobKind.Repair, ChannelId.Builder, "gate_stone:4:1", 180d);
```
The sibling seeder `SeedManageFlowExtraQueue` already had the grammar right (`"wall_wood:13:9"`,
`:9002` at HEAD) — the two Repair fixtures now speak the same language.

**The resolution this restores, traced through `ManageScreenVM.cs:940-1007`:**
`NormalizeBuildingJobId` → `"gate-stone"` misses `BuildingTierCatalog` (**correct** — gates have no
civic ladder) → the structures branch strips at `':'` → `"gate_stone"` →
`CatalogRegistry.Get("gate_stone").displayName` = **"Stone Gate"** → row label reads **"Stone Gate"**;
`portraitId = "gate_stone"` → `ManageArt.BuildingPortraitKey("gate_stone", 0)` =
`"Portraits/Buildings/gate_stone"` → **`Assets/Resources/Portraits/Buildings/gate_stone.png` is on
disk** (listed this session). **The portrait resolves VERBATIM with no alias layer.**

### `Assets/Editor/Regression/QueueJobCatalogCoverageRegression.cs` — NEW (+ `.meta`, guid `41e73a6e6e7941bd8e6d262e80f73610`)
Marker `QUEUE_JOB_CATALOG_COVERAGE_OK` / `..._FAIL`. Three cases, all failing **BY NAME**:

| Case | Asserts | RED recipe |
|---|---|---|
| `[fixture-builder-job-id]` | every Builder-channel job target the capture fixtures seed resolves a **non-empty** display name through the **same two-catalog walk the VM performs**. Quotes the id, the `file:line` that seeds it, and both catalogs it missed. | restore `"gate:4:1"` |
| `[catalog-display-name]` | neither authority ships a row with a **blank** displayName — the VM tests `!IsNullOrEmpty`, so blank reads as absent and is the same defect from the other direction | blank any `displayName` |
| `[miss-branch-still-loud]` | the VM still carries the `queue row catalog MISS` **FlowTrace.Fail** and the `"Unknown structure"` placeholder — WO-1655 §6 + CLAUDE.md §12 | delete either from the VM |

The regex matches **both** seeding shapes and the `PlacedUpgradeKey.Compose("id", …)` form, and the
case **fails if it matches ZERO seeds** — so a changed call shape can never turn it into a silent pass.

### `Assets/Editor/Regression/DataRegression.cs` — one registration line (+ its `// --- WO-1655 …` header), beside `manage-portrait-coverage`.

---

## 2. RED PROOF — MEASURED, NOT INFERRED

This lane runs no Unity, so the oracle's **exact rule** (`BuilderSeedRx` + `StripSuffixLikeVm` +
`NormalizeLikeVm` + the two JSON authorities) was ported line-for-line to python and run **twice over
the same tree** — once against `git show HEAD:Assets/Editor/UICaptureLaunch.cs`, once against the
working copy:

```
HEAD (efc56f67c)            working copy
  ok  8168 tower_ground_archer -> Archer Tower        ok  8168 tower_ground_archer -> Archer Tower
  ok  8169 barracks:2:0        -> Barracks            ok  8169 barracks:2:0        -> Barracks
FAIL  8170 gate:4:1            -> None      <-- RED   ok  8198 gate_stone:4:1      -> Stone Gate  <-- GREEN
  ok  9001 armorer:12:3        -> Armorer            ok  9029 armorer:12:3         -> Armorer
  ok  9002 wall_wood:13:9      -> Wooden Palisade    ok  9030 wall_wood:13:9       -> Wooden Palisade
seeds matched: 5                                     seeds matched: 5
```

The RED names `gate` at `UICaptureLaunch.cs:8170`, which is exactly the id in the captured trace.

⚠ **HONEST LIMIT (CLAUDE.md §11B.A):** what ran was the **PORT** of the rule, not this C#.
`QUEUE_JOB_CATALOG_COVERAGE_OK` has **never appeared on a log** and must not be claimed until the
gate produces one. Acceptance **#1** (a fresh `RunManageFlowMapCaptureHeadless` frame), **#2**
(`queue row catalog MISS` appearing zero times on that run) and **#5** (`COMPILE_GATE_OK` +
`REGRESSION_OK <n>/<n>`) are **NOT PROVEN BY THIS LANE** — they need the lead's Unity run. Acceptance
**#6** (`AWAITING OWNER MATCH`) is post-capture and is deliberately not set; the Status reads
`IMPLEMENTED - awaiting gate` per the lane brief.

---

## 3. HYGIENE

- **`python tools/gate_brace.py` (the gate's own rule, not the raw one-liner): `GATE_BRACE_SUMMARY bad=0 of 3`.**
- **NUL scan:** `NUL_SCAN bad=0 of 4` — `UICaptureLaunch.cs` (0 NUL, 954/954 raw braces, 558715 B),
  `QueueJobCatalogCoverageRegression.cs` (0, 26/26, 21160 B), `DataRegression.cs` (0, 1193/1193,
  432399 B), the new `.meta` (0, 59 B).
- **ZERO JSON edits — no newline proof is owed because no canonical file was touched.** `git status --short`:
  `M Assets/Editor/Regression/DataRegression.cs`, `M Assets/Editor/UICaptureLaunch.cs`,
  `?? Assets/Editor/Regression/QueueJobCatalogCoverageRegression.cs(+.meta)`, plus this RESULT and the
  WO Status flip. **Both `Resources/` and `StreamingAssets/` canonical twins are untouched and identical to HEAD.**
- **Line endings matched to the repo:** the new `.cs` is CRLF (365/365) like its neighbours; the new
  `.meta` is LF with no trailing newline, byte-shaped exactly like `ManagePortraitCoverageRegression.cs.meta`.
- **Nothing in §6's do-not-touch list was touched:** `ManageScreenVM.cs`, `ManageScreenPanel.cs`,
  `ObsidianQueueHud.FormatJobTarget`, `ManageArt.cs` and `ObsidianQueueVM.cs` are **unmodified** —
  the miss branch and its `FlowTrace.Fail` are now *pinned* by `[miss-branch-still-loud]` rather than edited.

---

## 4. FOR THE LEAD

1. Gate the combined tree once — `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a **fresh** log, judged by the marker.
2. Run `RunManageFlowMapCaptureHeadless`; **grep the log for `queue row catalog MISS` (expect zero)** and open
   `ManageFlow_BUILD_queue_2670x1200.png` — row 3 should read **"Stone Gate"** with the gate thumbnail.
3. Then the WO can move to `AWAITING OWNER MATCH` (acceptance #6).

**Two merge facts, so neither reads as a defect of this lane:**
- **`DataRegression.cs` is a shared-merge file.** The WO-1651/1653/1654 lanes are likely adding their own
  registration lines to it. Reconcile **by hand, by explicit path** — `git apply --3way` over a dirty tree
  drops hunks while reporting per-file success (memory `git-apply-3way-drops-hunks-silently`).
- **`[miss-branch-still-loud]` reads `ManageScreenVM.cs` as SOURCE, and other lanes own that file.** If one
  of them reworks the `queue row catalog MISS` message or the `"Unknown structure` literal, this suite goes
  **red on their merge**. That is the pin doing exactly its job (WO-1655 §6 + CLAUDE.md §12 — the trace is
  PERMANENT), not a fault here: the remedy is to keep the trace, or to move the pin **with** an owner ruling.

**Files:**
- `Assets/Editor/UICaptureLaunch.cs`
- `Assets/Editor/Regression/QueueJobCatalogCoverageRegression.cs` (+ `.cs.meta`)
- `Assets/Editor/Regression/DataRegression.cs`
- `WorkOrders/WORK_ORDER_1655_gate_has_no_display_name_in_either_catalog_queue_row_reads_unknown_structure.md` (Status flipped)
- `WorkOrders/WORK_ORDER_1655_gate_has_no_display_name_in_either_catalog_queue_row_reads_unknown_structure.RESULT.md` (this file)
