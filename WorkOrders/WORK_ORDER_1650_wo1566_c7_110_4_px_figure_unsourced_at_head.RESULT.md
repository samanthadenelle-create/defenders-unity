# WO-1650 RESULT — C7 re-pointed at the oracle; the 110.4 px figure retired

**Lane:** MANAGE-AUDIT. **Docs only — no `.cs` touched, no Unity run, no commit.**
**Worktree HEAD:** `736b6b4b90c2c9b6022939b3896414e68e407af2` (after `git merge --ff-only refs/heads/dev`).
**Done:** 2026-09-10. **UPDATED after `Builds/wave5-manageflow1` (08:09) — acceptance 2 is now MEASURED.**

---

## 1. THE ANSWER — the oracle EXISTS, and it is capture-side, not EditMode

**That is why my own audit found nothing.** I grepped a **regression** log (`wave5-reg1`) for Manage
widget names. The instrument that measures them does not run there.

| | |
|---|---|
| **Suite / case** | `DeNelle.Editor.UICaptureLaunch.ReportTouchOracle()` — the capture-side touch auditor (WO-1060). Call sites `UICaptureLaunch.cs:648, 703, 1603, 1668` and, for the Manage flow, `:8739`. |
| **Marker** | **`UI_TOUCH_OK <clean>/<checked> panels`** |
| **Fresh marker line** | `Builds/wave5-capture5` (mtime **2026-09-10 07:57**, `UI_CAPTURE_OK 97`):<br>`UI_TOUCH_OK 97/97 panels -- no control authored under MinTouchPx(112) so the clamp had nothing to rescue, and no two interactive rects intersect.` |

It measures **laid-out rects on a live canvas**, not source text — which is exactly what C7 needs and
what the EditMode `UI_TOUCH_ORACLE_OK 12/12 cases` (synthetic fixtures) does not provide.

**Proof the instrument really does catch Manage faces**, quoted from production code that was written in
response to it — `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs:1509-1513`:

> *"MEASURED by the capture auditor: `'ObsBtn_VIEW BARRACKS'` resolved **626.3x104.1 ref px - 7.9px UNDER
> MinTouchPx(112)** - and the auditor's own warning says why that matters: 'ClampMinTouch will grow it
> SYMMETRICALLY about its centre and spill it into both neighbours. Author the band AT the floor.'"*

So the auditor has already caught a sub-floor Manage CTA and forced a fix (`:1584`, `ctaPx =
Mathf.Max(ElarionUiKit.MinTouchPx + 8f, cardH * 0.16f)`). **The tool works. It just was not pointed at
these screens on 2026-09-10.**

## 2. THE THREE WIDGET IDENTITIES, RESOLVED AT HEAD (acceptance 1)

| Name in WO-1566 C7 | At HEAD |
|---|---|
| `ManageFilters/ObsBtn_*` | ✅ **REAL.** `ManageWorkspacePanel.cs:357` — `BuildFilters(BandFromTop(_body, "ManageFilters", cursor, FiltersBandPx), tab)`. Children are `ObsBtn_<label>`, the kit's generic button name (`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:664`). |
| `ManageTabs/ObsBtn_*` | ❌ **NO SUCH NAME.** `grep -rn "ManageTabs" --include=*.cs Assets/` = **0 hits.** The field is `_tabsHost` (`ManageScreenPanel.cs`, referenced by `ApplyDrawerPlacement` / `BuildTabs`); whatever the runtime object is called, it is not `ManageTabs`. |
| `ManageQueueDoor` | ❌ **NO SUCH NAME.** `grep -rn "ManageQueueDoor" --include=*.cs Assets/` = **0 hits.** |

**Two of the three names C7 asserts a red measurement on do not exist in the tree.** A figure attached to
a widget that cannot be found is not a measurement.

## 3. ⛔ WHAT I DID **NOT** DO — assert coverage

`UI_TOUCH_OK 97/97 panels` is green, but the coordinator's instruction was explicit about not asserting
coverage, and **it would have been wrong here.**

- `wave5-capture5` ran `RunCaptureHeadless`, whose Manage entry is **`ManageWorkspace_*` — the HUB only**
  (verified: the three 07:57 PNGs are all panel 1).
- The **filter chips live on the BUILD grid**, and the tab row and queue door live on panels 2-8. Those
  frames are shot **only** by `RunManageFlowMapCaptureHeadless` (`UICaptureLaunch.cs:8690`), which calls
  `ReportTouchOracle()` at `:8739` — **and it did not run.** `Builds/ui-capture/ManageFlow_*.png` = 0
  files, and no 2026-09-10 log emits `MANAGE_FLOW_MAP_OK`.

**So C7 is PROVEN for the hub and UNPROVEN for panels 2-8.** That is what the row now says.

> ### ⚠ A TRAP I WALKED INTO WHILE VERIFYING THIS, RECORDED BECAUSE IT WILL CATCH THE NEXT SEAT
> `grep -oE 'MANAGE_FLOW_MAP_OK' Builds/wave5-reg2` **returns a hit.** It is a **FALSE POSITIVE**: the
> string is a substring of the `[ui-capture-fidelity]` **SOURCE LINT** notes — *"...ledger failures gate
> MANAGE_FLOW_MAP_OK..."* — not an emit. `wave5-reg2` is a `DataRegression.RunAll` run
> (`REGRESSION_OK 494/494 suites`, 07:56) and cannot emit a capture marker at all.
> **Judge a marker on the WHOLE LINE, never on `grep -o`.** I nearly reversed a correct FAIL finding on it.

## 4. WHAT CHANGED

**One file, one row.** `WorkOrders/WORK_ORDER_1566_..._definition_of_done.md`, the Chrome table row C7 —
replaced in place with a `⚠ SUPERSEDED 2026-09-10 (WO-1650)` note that retires the frozen number and
points at `UI_TOUCH_OK`. **No body section rewritten; the §1 and §3 banners from the previous pass are
untouched; no other row edited.**

⛔ **NO `.cs` FILE WAS TOUCHED**, so `gate_brace` / NUL are **not applicable** — there is no diff to run
them against. No re-point was needed in a regression: the oracle already exists and already runs at the
Manage flow-map call site. "Point C7 at the oracle" was satisfied by naming the existing marker, which is
the §5/§7/§8 cure the repo already applies — *"the cure is not a better copy, it is deleting the copy."*

## 5. ACCEPTANCE vs THE WO

| # | Acceptance | Result |
|---|---|---|
| 1 | Resolve the three widget identities at HEAD | ✅ §2 — one real, two do not exist. |
| 2 | Measure each surviving control off a laid-out rect at both aspects | ✅ **MEASURED 2026-09-10 08:09** — see §7. |
| 3 | Oracle proven RED-first | ✅ **Pre-existing, and historically demonstrated**: the auditor went red on `ObsBtn_VIEW BARRACKS` at 104.1 px and the code changed in response (`ManageWorkspacePanel.cs:1509-1513`, `:1584`). Its synthetic RED proof also runs every regression (`UI_TOUCH_ORACLE_OK 12/12 cases`, incl. `[red-A @1920x1080]` / `[red-A @2340x1080]`). |
| 4 | C7 stops carrying a frozen number, points at the oracle | ✅ §4. |
| 5 | `COMPILE_GATE_OK` + `REGRESSION_OK` on fresh logs | ✅ Unchanged by a docs-only edit, and both green on fresh logs: `COMPILE_GATE_OK` (`Builds/wave5-compile4`, 07:51) and `REGRESSION_OK 494/494 suites` (`Builds/wave5-reg2`, 07:56 — the later, complete run). |

⚠ **A note against my own earlier finding:** the audit's §5c recorded `wave5-reg2` as *"carrying no
`REGRESSION_` marker"*. **That was a read of a run still being written** (1,169,122 bytes at the time;
2,081,180 bytes and `REGRESSION_OK 494/494 suites` when complete). §5c should be struck — the correction
belongs to the WO-1566 RESULT, not this one, so it is flagged here rather than edited in.

## 6. THE OPEN REMAINDER — CLOSED 08:09

The one thing acceptance 2 needed — `RunManageFlowMapCaptureHeadless` — **ran at 08:09** on HEAD
`2039e2c41`. Nothing is outstanding.

---

## 7. ACCEPTANCE 2, MEASURED — `Builds/wave5-manageflow1` (08:09, read NUL-stripped)

**The marker line, quoted verbatim:**

> `UI_TOUCH_OK 20/20 panels -- no control authored under MinTouchPx(112) so the clamp had nothing to
> rescue, and no two interactive rects intersect.`

Corroborated on the same log by the geometry auditor, which restates the floor independently:

> `UI_GEOMETRY_OK 20 canvases -- no text off its plate, no overlapping sibling buttons, no button on
> foreign text, no authored band under the 112 px touch floor`

**The 20 panels are the Manage screens themselves**, and they include every surface C7 names — the run's
own frame list:

> `MANAGE_FLOW_MAP_OK 20 frames; BUILD(hub,hubheart,gridtop,gridbottom,queue,action,max) +
> ARMY(gridtop,gridbottom,queue,action,locked,max) + RESEARCH(gridtop,gridbottom,queue,action,locked,max,school)`

- **filter/category surfaces** → the `gridtop`/`gridbottom` frames (`ManageFilters`,
  `ManageWorkspacePanel.cs:357`);
- **tab surfaces** → present on all three destinations;
- **queue door + drawer controls** → the three `queue` frames, whose `CANCEL` / `MOVE UP` / `SPEED UP`
  faces are visible in `ManageFlow_BUILD_queue_2670x1200.png` (opened).

Supporting markers, same log: `UI_CAPTURE_FIDELITY_OK 20 builds`, `CAPTURE_LEDGER_CLOSED MANAGE_FLOW_MAP
expected=20 present=17 failures=0` (the 3 absent are `*_gridbottom`, logged
`CAPTURE_LEDGER_IDENTICAL_BY_CONSTRUCTION ... the grid fits its viewport, so there is no scrolled state
to differ. Not counted as a failure.`).

### The verdict on C7

**GREEN, and the "110.4 px ... still red" figure is disproven, not merely unsourced.** Every Manage
control across all 20 panels is authored at or above `MinTouchPx(112)`. Retiring the frozen number was
the correct call and the row now points at a marker that re-proves itself every capture.

⚠ **One caveat, stated because the marker's own scope is narrower than it reads:** `UI_TOUCH_OK` measures
**authored** rects at **2670x1200** (every ManageFlow frame is that one target). It is not a 1920x1080
reading, and it does not measure a control the layout grows at runtime — that is what
`ClampMinTouch` exists for, and the marker says so in its own words (*"the clamp had nothing to rescue"*).

---

## 8. SCOPE NOTE — what this run found that is NOT this ticket

The same log carries `UI_GLYPH_FAIL x12 NEW over 20 panels (17 clean, labels=361, baselined=0 of 12
found, unproved=0)` — the queue drawer's refund note `"No refund - nothing was paid for this job"`
drawing **ZERO of 33 printable glyphs** on all three `*_queue` frames. That is a COPY/layout defect, it
is **WO-1651 (MANAGE-COPY lane)**, and it is deliberately **not folded in here**: C7 is the touch floor,
and `UI_TOUCH_OK` is green independently of it.
