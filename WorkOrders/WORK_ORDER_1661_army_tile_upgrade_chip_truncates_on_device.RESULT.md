# WO-1661 RESULT — ARMY tile state chip: instrumented, short face wired, fixture un-blinded

**⚠ THIS IS NOT A "DONE" RESULT. Read this line before the board does.**
The code is written and brace/NUL-clean. It is **UNGATED** (no Unity was run by this lane) and the
**owner's word for §4B has not been given**. Status is `IMPLEMENTED - awaiting gate + owner word`,
not DONE. Three acceptance boxes in §7 of the WO are still open and are named as unproven below.

**Lane:** ARMY-BADGE
**Branch/base:** `dev`, ff-merged at **`c3e767666d502b603dbb83fd62a0455a87b7ed57`**
**Worktree:** `.claude/worktrees/agent-af0250ab2e956be05` (no commit, per brief)

---

## 1. THE FINDING THE TICKET DID NOT HAVE — and it changes §4C

**No troop-seeding edit in `BuildManageFlowFixture` could ever have produced the missing state.**

`ManageScreenVM.ComposeTroopItem`'s badge precedence puts its `trainLineFull` arm **above** its
`UPGRADE AVAILABLE` arm (read at `c3e7676`, lines 4914 and 4915 respectively — **line numbers given
only because this file is dated; the in-code comments cite by SYMBOL**, since a line number in a
permanent comment is the duplicated state CLAUDE.md §2/§5/§16 each describe):

- `else if (trainLineFull) { … BadgeText = "QUEUE FULL"; }`
- `else if (c.UpgradeWord == "UPGRADE AVAILABLE") { … }`

and the flow-map capture (`CaptureManageFlowFrame`) saturates the Train channel to its authored depth cap for **every frame
except `ActionDetail`** (`UICaptureLaunch.cs`, the `SeedManageFlowExtraQueue` call). While that line
is full, **every** unlocked troop reads `QUEUE FULL` and `ManageTileBadge.UpgradeAffordable` is
**unreachable on that grid no matter what the fixture stores in `TroopLevels` or `BaseLayout`.**

That is exactly the grid the ticket measured: MAX x1, QUEUE FULL x4, LOCKED x4 — longest word 10
chars, while the device painted the 17-char string and ellipsised it.

So the fixture edit is **the Train-line exemption**, not troop seeding. The brief's "touch only the
ARMY fixture seeding" was written without this precedence fact; the edit stays inside the ARMY flow
frame and does **not** touch the `RunCaptureHeadless` list the HUD-CHIP lane is editing.

**Why the exemption is safe** (read at source, not assumed):
- `BarracksService.CanUpgradeTroop` (opened at source) is **unlocked && HasNextTroopLevel
  && !IsUpgradingTroop** — WO-1387 removed every affordability and prerequisite test ("just time").
  So draining the Train line lands the four unlocked non-max troops on `UPGRADE AVAILABLE`.
- Footman stays `MAX`: `ComposeTroopItem` hoists its `atMax` arm **above** `trainLineFull`, so the
  MaxDetail-style state does not depend on the saturation the old comment at the seeding site warns
  about. Only `Army` + `GridTop` is exempted; `GridBottom`, `QueueDrawer` and all three detail
  frames keep their saturation untouched.

---

## 2. WHAT LANDED

### A. INSTRUMENT FIRST — `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs`

`ResolveStateWordFont` had **four silent early returns** and logged only on its two loud paths, which
is why `grep -i "state word"` over the device log returns zero lines while 30+ `[Flow:Manage]` lines
print in the same window. Added **five** `FlowTrace.Step("Manage", …)` calls, all string-concat (no
interpolation, so the gate's brace scanner has no hole to fall into):

| Marker in the message | Return it reports | Carries |
|---|---|---|
| `state word font: resolving for widest=…` | *entry* | `widest`, its length, `cellW` |
| `[no-word-or-no-cell]` | `:1116` | `widest`, length, `cellW`, returned px |
| `[no-plate-width]` | `:1121` | `widest`, `cellW`, `availablePx`, the band, returned px |
| `[probe-measured-nothing]` | `wantPx <= 1f` | `widest`, `wantPx`, resolved face name, `availablePx` |
| `[already-fits]` | `wantPx <= availablePx` | `widest`, `wantPx`, `availablePx`, `cellW`, **slack px** |

The **entry** line is deliberate and not redundant: no return-report can distinguish "ran and took a
quiet branch" from "was never called on that screen", and that is precisely the question the device
log could not answer.

⛔ **No return value changed.** Every branch returns byte-for-byte what it returned before. The
constants `TileStateX0/X1`, `MinTileHeightPx`, `MaxTileAspect`, the hard floor and the layer stack are
untouched (WO-1661 §6).

### B. THE SHORT FACE — `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs`

The `UpgradeAffordable` branch now authors `item.BadgeWord` alongside the existing `BadgeText`,
exactly as the READY/SHORT composer already does. The 17-char long face **stays** on `BadgeText` for
the detail card and the research row.

⚠ **THE WORD IS PROVISIONAL AND SAYS SO IN CODE.** It is a named private const,
`UpgradeAffordableGridWordProvisional = "UPGRADE"`, carrying a doc comment that states the open
question (it collides at a glance with `UPGRADING`, composed one branch above on the same grid) and
names WO-1661 §4B. **The owner's ruling is a one-token change to that one const** — nothing else in
the tree, the fixture or the test reads the value.

### C. THE FIXTURE — `Assets/Editor/UICaptureLaunch.cs` (flow-map body only)

Two edits, both inside `CaptureManageFlowFrame`'s body:

1. **The exemption.** `armyGridNeedsUpgradableState` (`tab == Army && frame == GridTop`) now joins
   `ActionDetail` in skipping `SeedManageFlowExtraQueue`, with the precedence finding of §1 written
   at the site so the next reader does not re-derive it.
2. **The assertion — and this is the load-bearing half.** After `EnterTab`, the ARMY grid frame
   **throws** unless some tile carries a `StateWord` that is non-empty **and distinct from** its
   `StateText`. Without it, a `CanUpgradeTroop` refusal would leave the capture exactly as blind as
   the one that missed the device defect **while the ticket read as covered** — strictly worse than
   today. The throw names the observed states and the two causes that can produce it. On success it
   appends a `_flowStateNotes` line naming the troop and both faces with their lengths.

The assertion tests **the rule, not the word**, so the §4B ruling does not touch it.

### D. RED-FIRST PIN — `Assets/Editor/Regression/ManageTroopsTrainDoorRegression.cs`

Extended **case 10 half A**, reusing the suite's own seam (`TileFor(vm, c.Id)`) rather than inventing
one — that seam already runs with the Train line un-saturated, which is required for the state to be
reachable at all (same precedence fact as §1). For each troop composing `UPGRADE AVAILABLE`, the tile
must carry a grid face that is present, **not equal to** the row face, and **shorter** than it. Three
distinct failure sentences, each naming the device evidence.

A `gridFacesMeasured` counter is logged either way, so a fixture with no upgradable troop reports a
**NOTE naming the covering evidence** instead of passing silently.

---

## 3. UNPROVEN — do not read any of these as done

1. ⛔ **The RED pin was NOT seen failing on HEAD.** No Unity was run by this lane (brief: "No Unity").
   The proof is one runner invocation for the lead:
   `powershell -File run-unity-method.ps1 -Method DeNelle.Editor.Regression.DataRegression.RunAll`
   — judged by `REGRESSION_OK <n>/<n> suites` on a **fresh** log, never the exit code. To see it RED
   first, stash the `item.BadgeWord = …` line in `ManageScreenVM.cs` and re-run; the
   `[case 10 / WO-1661]` "SAME string in the grid cell as in the detail row" failure is the one that
   must appear.
2. ⛔ **No fresh device logcat has been taken, so WO-1661 acceptance 1 is OPEN.** Which of the five
   new markers fires on the ARMY grid, and with what numbers, is exactly what the instrumentation
   exists to answer and is **not** answered yet. The `[already-fits]` slack figure is the one to read
   against §1's 41 px of unused plate. **Nothing in this lane's change was chosen on a theory of that
   branch** — the composer fix is independently proven by the projection fallback at
   `ManageVmProjection.cs:223`, which needs no font measurement at all.
3. ⛔ **The owner has not ruled on the word** (§4B). `"UPGRADE"` is a placeholder.
4. **The un-saturated ARMY grid's exact composed states were derived, not observed.** No
   `cap-manage-wave*.log` with `MANAGE_FLOW_MAP` state notes exists on disk to read, so §1's
   conclusion rests on `CanUpgradeTroop`'s body plus the queue-seeding call sites, opened at source.
   The assertion in §2C is the deliberate consequence: if that derivation is wrong, the capture
   **fails loudly** rather than shipping blind.
5. **The capture assertion calls `ActiveManageTabVm(vm)` on a frame that is then photographed.** That
   re-composes the workspace. The three detail frames already do the same through
   `TryNavigateManageFlowDetail`, so the seam is exercised in this body — but not previously on a grid
   frame that goes on to be shot. Judged low risk and **not proven**; if the ARMY gridtop png changes
   shape unexpectedly, this is the first thing to look at.
6. Not checked (out of scope, WO-1661 §6): whether BUILD or RESEARCH carry the same missing-
   `BadgeWord` shape. Worth a sibling ticket.


---

## 3B. HOW THE LEAD ACTUALLY RUNS THE RED PROOF — read this before running it

⚠ **Pre-fix, the thing that goes RED is the CAPTURE ASSERTION, not the glyph oracle.** The brief asked
for a fixture that reds `UI_GLYPH`. What the seeding change produces is stricter and fires **earlier**:
with the Train line drained and no `BadgeWord` authored, `CaptureManageFlowFrame` **throws**
(`InvalidOperationException`, the "NO tile … carries a grid face distinct from its row face" message)
**before the frame is photographed**, so `UI_GLYPH` never gets handed the 17-char string at all.

That is the better guard — a frame that cannot photograph the state it claims must fail loudly rather
than shoot a blind grid — but it means **the lead must not read that exception as "something else
broke".** To exercise the glyph oracle itself on the long string, it takes two steps, in this order:
1. comment out the `if (armyGridNeedsUpgradableState) { … }` assertion block in `UICaptureLaunch.cs`;
2. remove `item.BadgeWord = UpgradeAffordableGridWordProvisional;` from `ManageScreenVM.cs`.
Then the ARMY gridtop frame shoots `UPGRADE AVAILABLE` and `LayoutOracle` ASSERT C reds on the
ellipsis. Restore both afterwards.

⚠ **AND THE REGRESSION PIN CAN REPORT A NOTE INSTEAD OF A PASS — check which.** Case 10 half A only
reaches the new assertions for a troop that is `UPGRADE AVAILABLE` **and** `!c.ArmyFull`, and case 9
in the same suite drives the army to its cap. On the fresh log the line to find is:

```
case 10 OK (WO-1661 grid face) - N UPGRADE AVAILABLE tile(s) carry a grid face shorter than their row face
```

**with N > 0.** If `case 10 NOTE (WO-1661 grid face)` prints instead, the pin did **not** exercise and
RED-first stays unproven on this seam — the covering evidence is then the capture assertion alone.

---

## 4. GATE EVIDENCE FROM THIS LANE

```
python tools/gate_brace.py <4 files>  ->  GATE_BRACE_SUMMARY bad=0 of 4   (exit 0)
NUL scan (0x00 bytes)                 ->  0 in all four files            (NUL_SCAN_OK)
raw brace balance                     ->  91/91, 460/460, 252/252, 971/971
```

No commit, no push, no Unity — per brief. Left in the worktree for the lead to batch-gate.

---

## 5. FILES TOUCHED

| Path | Change |
|---|---|
| `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs` | 5 `FlowTrace.Step` on `ResolveStateWordFont`'s entry + 4 silent returns; no behaviour change |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | `UpgradeAffordable` branch authors `BadgeWord`; provisional named const + ruling comment |
| `Assets/Editor/UICaptureLaunch.cs` | ARMY/GridTop Train-line exemption + the throw that asserts the state is present |
| `Assets/Editor/Regression/ManageTroopsTrainDoorRegression.cs` | case 10 half A: grid-face rule pin + measured counter |
| `WorkOrders/WORK_ORDER_1661_army_tile_upgrade_chip_truncates_on_device.md` | Status flipped |
