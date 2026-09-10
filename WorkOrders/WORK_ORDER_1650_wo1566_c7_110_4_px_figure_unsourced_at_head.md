# WO-1650: WO-1566's C7 "110.4 px" touch-floor figure is unsourced at HEAD — re-source it or retire it

**Status:** DONE 2026-09-10 - C7 re-pointed at `UI_TOUCH_OK`, the 110.4 px figure RETIRED and DISPROVEN. Acceptance 2 MEASURED on `Builds/wave5-manageflow1` (08:09): `UI_TOUCH_OK 20/20 panels -- no control authored under MinTouchPx(112)`. All 5 acceptance rows met. See `WORK_ORDER_1650_wo1566_c7_110_4_px_figure_unsourced_at_head.RESULT.md`
**Silo:** documentation + the touch oracle. **No gameplay code.**
**Number:** PRE-ASSIGNED by the lead. ⛔ **Do NOT edit `CLI_LANES_WO_NUMBERS.md`.**
**Source:** WO-1566 audit, `WORK_ORDER_1566_..._definition_of_done.RESULT.md` chrome row **C7**
(audit committed `2039e2c41`).

---

## 1. THE CLAIM, AND WHAT IT DOES

`WORK_ORDER_1566_manage_conformance_spec_end_of_session_definition_of_done.md`, the Chrome table, row C7:

> | C7 | Every tappable ≥ `ElarionUiKit.MinTouchPx` (**112**) | ⚠ last measured **110.4 px** on every
> `ManageTabs/ObsBtn_*`, `ManageQueueDoor`, `ManageFilters/ObsBtn_*` — **still red** |

C7 is one of the eight chrome rows every Manage panel is measured against, and **"still red" is the only
row in the yardstick that asserts a live failing measurement.** It is therefore load-bearing: a seat
reading the spec to decide whether the Manage wave can close reads that as an open defect.

## 2. THE FINDING — the figure produces zero hits at HEAD

Measured 2026-09-10 against `Builds/wave5-reg1` (07:25), a **complete** run:
`REGRESSION_OK 494/494 suites -- 494 green, 0 red, 0 skipped`, NUL-stripped before grepping.

```
grep -niE 'ManageTabs|ObsBtn|ManageQueueDoor|ManageFilters'   ->  0 hits
grep -niE 'MinTouchPx|touch floor|110\.4'                     ->  hits, but NONE naming a Manage widget
```

- **No widget named `ManageTabs/ObsBtn_*`, `ManageQueueDoor` or `ManageFilters/ObsBtn_*` is measured
  anywhere in a 494/494 log.** Not red, not green — **absent**.
- `UI_TOUCH_ORACLE_OK 12/12 cases` measures **synthetic fixtures** (`SyntheticSubFloor`,
  `SubFloorHost/slot-chip-0`), not the Manage tree — it proves the oracle can go red, not that it was
  ever pointed at these controls.
- The only Manage touch facts on the log are **derivations, not rects**: `ManageExitPx =
  ElarionUiKit.MinTouchPx` (`Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:138`), so the
  constant exit sits at the floor by construction, plus `[queue-controls-clear-the-touch-floor]` in
  `ManageMockupConformanceRegression`.

**So the 110.4 figure cannot be reproduced, confirmed, or refuted from HEAD.** Per CLAUDE.md §11B a
number copied into a doc is **hearsay until re-read at source**, and this one has no source left. It is
exactly the duplicated-state shape §2/§5/§8/§16 each describe: a measurement frozen into prose, tracking
a live tree, with nothing to keep it honest.

⚠ **Both outcomes are bad and that is why this is a ticket.** If the controls really are 110.4 px, a
real defect has been sitting unmeasured behind a green 494/494. If they are not, the yardstick has
carried a false red that could block a wave from closing.

## 3. FILES TO EDIT

| File | Change |
|---|---|
| `Assets/Editor/Regression/*` (the touch oracle, wherever `UI_TOUCH_ORACLE_OK` is emitted) | Point a **measured** case at the real Manage chrome controls, at both landscape capture aspects. |
| `Assets/Editor/Regression/DataRegression.cs` | Registration line only, if a new case/suite is added. |
| `WorkOrders/WORK_ORDER_1566_..._definition_of_done.md` | Row C7 ONLY: replace the frozen figure per §4.3. Do not touch any other row — the file is under a `⚠ SUPERSEDED` banner regime, banner-only edits elsewhere. |

## 4. ACCEPTANCE

1. The three widget identities are resolved at HEAD: state whether `ManageTabs/ObsBtn_*`,
   `ManageQueueDoor` and `ManageFilters/ObsBtn_*` **still exist under those names**, and if they were
   renamed, name what they are called now — with the `file:line` that builds each. (The Manage surface
   moved substantially: `ManageScreenPanel.cs` is 7295 lines and `ManageWorkspacePanel.cs` 2199 at HEAD.)
2. Each surviving control's shortest side is **MEASURED off a laid-out rect** at 1920x1080 and
   2670x1200 and the numbers are recorded in the RESULT — never a source-parsed anchor, never a
   fraction-of-host arithmetic lint.
3. The oracle is proven **RED-first**: it fails on an authored sub-112 band and stays silent on the same
   controls laid at the floor.
4. **Row C7 in WO-1566 stops carrying a frozen number.** It points at the oracle marker instead — the
   §5/§7/§8 cure the repo already applies to face counts and suite counts (*"the cure is not a better
   copy — it is deleting the copy"*). If a genuine sub-floor control is found, it gets **its own
   ticket**, not a number re-frozen into the spec.
5. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.

## 5. WHAT NOT TO TOUCH

- ⛔ **`ElarionUiKit.MinTouchPx` (112).** It is the floor, referenced by ~a dozen suites. This ticket
  measures against it; it never moves it.
- ⛔ `ManageExitPx` (`ManageScreenPanel.cs:138`) and `ManageExitGapPx` (`:141`). Both are derived, both
  green, and `[manage-exit-clears-queue]` fails if the derivation is broken.
- ⛔ Do not "fix" a control's size on this ticket. **Measure first.** If something is genuinely under
  the floor, that is a new ticket with its own RCA — CLAUDE.md §12 forbids the edit before the data.
- ⛔ Do not rewrite any WO-1566 body row other than C7, and do not delete its `⚠ SUPERSEDED 2026-09-10`
  banners (§15: dated ledgers are banner-fixed, never rewritten).
- ⛔ No gameplay code, no scene files, no Unity bakes.
