# WO-1670 RESULT - the Echoes chip and the QueueStatus band now read one seam

**Status of the WO:** IMPLEMENTED - awaiting gate + capture (lane ECHO-CHIP 2026-09-10)
**Lane:** ECHO-CHIP, isolated worktree `.claude/worktrees/agent-a1f6b172258ffe963`
**Base:** fast-forwarded to `refs/heads/dev` = **`e4b5906a541c65fc12255a109b5dc3723e328488`** (e4b5906a5)
**Not done here, by instruction:** no Unity run, no gate, no commit, no banner edit.

---

**Second pass (owner ruling 12:55):** re-synced `git merge --ff-only refs/heads/dev` →
**`de91a22ee871c6c89577e895b1ab91c1c78febf0`** (docs-only fast-forward; the four WO-1670 files were
NOT in that merge and are carried unchanged in this worktree). Two more files touched — see §9.

---

## 1. The files

| file | lines | what |
|---|---|---|
| `Assets/_Modules/Core/UI/HudLayoutBands.cs` | +104 | **THE SEAM.** New right-column section: `QueueStatusMount`, `EchoChipWidthPx` / `EchoChipHeightPx` / `EchoChipEdgeInsetPx`, `EchoChipTopY`, `ResolveEchoChip(w,h)` |
| `Assets/_Modules/HUD/Kit/HudAreasHost.cs` | +25 -4 | `Add(HudArea.QueueStatus, HudLayoutBands.QueueStatusMount)`; the two stale `0.420` / `.42` ActionRail comments retired |
| `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs` | +45 -55 | `EchoChipBandCentreY` + `EchoChipWidthPx` **DELETED**; the chip hangs from `HudLayoutBands.EchoChipTopY` with pivot (1,1) |
| `Assets/Editor/Regression/HudUiRegression.cs` | +266 -9 | check 6e re-pointed; `ReadRightColumn`'s QueueStatus read re-pointed; new check 11 `[right-column-bands]` + its registration |

`git diff --stat`: **459 insertions, 71 deletions, 4 files.**
`python tools/gate_brace.py` on all four: **`GATE_BRACE_SUMMARY bad=0 of 4`, exit 0.** NUL scan: all four clean.

⛔ **`HudKitController.cs` is untouched — zero lines.** See §3.

---

## 2. The change, in one line each

- **The chip is authored by its TOP EDGE, not its centre.** `EchoChipTopY = QueueStatusMount.yMin -
  ThumbBandClearanceGap` = **0.500, a pure fraction, identical at every resolution**; the fixed
  112 x 220 px box hangs down from it (pivot 1,1). ⭐ **This is the load-bearing half.** A FIXED-pixel
  box on a CENTRE fraction resolves a *different* top edge at every aspect (0.5330 at 2670x1200,
  0.5269 at 1920x1080), so no single centre value can be disjoint from a fractional band everywhere.
  Anchoring the top removes the aspect from the invariant entirely.
- **`ThumbBandClearanceGap` (0.010) is REUSED**, not duplicated. One clearance number.
- **The width, height and inset moved to the seam too**, so the chip's `MinTouchPx` height (the
  ClampMinTouch no-op) and its `ElarionUi.PadPanel * 3f` inset — the same expression as
  `HudKitController.RailGutterPx`, which keeps all four right-column faces on one right edge — are
  now stated once and checked (11d), rather than being two files agreeing by hand.

---

## 3. ⚠ THE BRIEF NAMED THE WRONG PRODUCER, AND THE CORRECTION IS GOOD NEWS

The brief said *"HudKitController's Echo chip mount"*. **It is not there.** The one authoring site is
`Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs:77` and `:474-475`
(`grep -rn "EchoChipBandCentreY|0\.475f" --include=*.cs Assets/` returns only that, plus
`HudObsidianShowcaseSceneBuilder.cs:214` — an unrelated editor-showcase rect — and the regression pin).
`HudKitController` builds the three RAIL chips, not the Echoes chip.

**So this lane touches ZERO lines of `HudKitController.cs`.** It is file-disjoint from WO-1660/1662/1667
and from the DOCK-CAPTIONS lane's `BuildAdaptivePeacefulDock` / `HudDockSlotLayout` work **by
construction, not by care.** There is no 3-way merge exposure in that file from here. The four files
above are the whole footprint.

---

## 4. The RED proof — arithmetic, and stated honestly

⛔ **I did NOT run the regression. No Unity was fired (per the brief), so "the pin was seen red" is a
sentence I have not earned** (CLAUDE.md §11B). What follows is the check's own arithmetic, computed
this session from values read at source, and the exact one-line revert that reproduces it.

**To observe RED:** revert `EchoUnlockFeedback.cs` to HEAD (centre `0.475f`) and run `HudUiRegression`.
Check 11c fires at both aspects with these numbers:

| aspect | canvas ref size | Echo band at centre 0.475 | QueueStatusMount | overlap |
|---|---|---|---|---|
| 2670x1200 | 2148.0 x 965.4 | 0.4170 .. **0.5330** | 0.510 .. 0.750 | **0.0230** ← the owner's "0.022" |
| 1920x1080 | 1920.0 x 1080.0 | 0.4231 .. **0.5269** | 0.510 .. 0.750 | **0.0169** |

**GREEN after the fix:** band = 0.3840..0.5000 (2670x1200) and 0.3963..0.5000 (1920x1080). Top edge
**0.500 at both**, clear of the mount by 0.010 — 20x `HudLayoutBands.Epsilon` (0.0005).

### 4a. ⭐ It was not merely a mount encroachment. Two LIVE bands already shared screen.

The mount's resting occupants are the **Collectors chip** and the **ATTACK REPORT chip** — two
112 ref px `BuildRailChip` bands stacked `RailGapPx = 6` apart from the mount's top, on the **same
220 px right gutter as the Echoes chip** (`HudKitController.cs:5631-5633` names this chip as the
reason `RailGutterPx` exists):

| aspect | Collectors (ref px from canvas top) | ATTACK REPORT | its bottom as y | Echo top (before) | shared |
|---|---|---|---|---|---|
| 2670x1200 | 241.3 .. 353.3 | 359.3 .. 471.3 | **0.5118** | **0.5330** | **0.0212** |
| 1920x1080 | 270.0 .. 382.0 | 388.0 .. 500.0 | **0.5370** | **0.5269** | **0.0101** |

⚠ **WO-1642 was right and this is also right.** It measured the PLATES on
`Builds/device-frames/2026-09-10_0602_town.png` and correctly reported no pixel collision (ATTACK
REPORT ink ends at device row 545, Echo ink starts at 585). That gap exists only because
`MedievalUiSkin`'s sprite ink occupies 0.145..0.736 of its band. **The INK missed; the BANDS did not.**

*Model validation, so this is not a theory:* the same arithmetic predicts the ink at device rows
580.6..662.9 (Echo) and 467.0..549.3 (ATTACK REPORT) against WO-1642's measured **585..658** and
**471..545** — within ~4 device px on all four edges.

### 4b. Check 11 also proves the step the old oracle assumed

11b derives the resting rail stack's depth from `HudKitController` source (`RailGapPx`, `MinTouchPx`,
and the COUNT of un-commented `Build*Chip(pool);` calls — so an un-retirement of the Builders chip
moves it with no second edit) and asserts it **fits inside the mount**: it ends at y **0.5118** /
**0.5370** against the mount floor 0.510. That is what makes 11c's mount-level disjointness actually
*imply* chip-level disjointness instead of assuming it — WO-1642 §6.4(ii)'s named gap.

### 4c. Why this had to be band arithmetic and not a capture rule

The capture harness's *"no overlapping sibling buttons"* passes on this defect and always would: the
two controls are on **different canvases** (HudAreasHost's at sortingOrder 4000; EchoUnlockFeedback
builds its own) and are therefore never siblings.

---

## 5. ⚠ The pin that would have gone SILENT — caught and re-pointed in the same change

`HudUiRegression.ReadRightColumn` did **not** read the QueueStatus band from the seam; it
**regex-parsed the literal `Add(HudArea.QueueStatus, new Vector2(...), new Vector2(...))` out of
`HudAreasHost.cs` source text**, and on a miss it NOTES and returns false — which by its own
documented contract **skips the entire layout half of checks 7 and 8**. Moving the band to the seam
without touching that read would have **silently retired WO-1435's Harvest-clearance geometry with
every marker still green.** Its own comment predicted this ticket: *"it may have moved to a band table
— the layout half of this check is SKIPPED rather than run on a guess."*

Re-pointed to `HudLayoutBands.QueueStatusMount` directly (the regression asmdef already references
`DeNelle.Core`). The ActionRail parse is left alone — still a literal. `ReadAreaBandY`'s doc example
was updated to ActionRail, because it was QueueStatus.

**Also re-pointed — check 6e** (`:938-966`): its two asserts named `ElarionUiKit.MinTouchPx` and
`EchoChipBandCentreY` *as strings in `EchoUnlockFeedback.cs`*, and both symbols are deleted there.
They now assert `HudLayoutBands.EchoChipHeightPx` / `HudLayoutBands.EchoChipTopY` in that file **plus**
a live `EchoChipHeightPx == ElarionUiKit.MinTouchPx` value check — the invariants survive, the copies
die. ⚠ Worth noting: a literal `EchoChipBandCentreY` still exists in that file's WO-1670 tombstone
**comment**, so the old `IndexOf` would now pass **on prose**. Re-pointing was not optional.

### 5a. The full sweep for other readers of the deleted symbols

`grep -rn "EchoUnlockFeedback\|PadPanel \* 3" Assets/Editor/` this session. Every hit accounted for:

| hit | verdict |
|---|---|
| `HudUiRegression.cs:759` (`ReadAsset` of that file) | feeds 6e, **re-pointed** |
| `HudUiRegression.cs:1793` (check 8d's failure TEXT named `EchoUnlockFeedback.EchoChipWidthPx`) | **fixed** — now names `HudLayoutBands.EchoChipWidthPx`, and "three rail chips" corrected to FOUR faces |
| `HudLabelFitRegression.cs:148` | a doc COMMENT with the same stale symbol. That file is out of scope here — raised in §7.6, not edited |
| `UiMvvmConformanceRegression.cs:125` | an allowlist keyed on the FILENAME, not on any deleted symbol — unaffected |
| `UICaptureLaunch.cs:3302-3378` | instantiates the component for the pip/Pets capture; behaviour-only, the chip simply seats lower |
| `CheckSafeAreaCorner` | checked: pins no string from this file |

**Nothing else read the deleted consts as source text.** The three `HudLayoutBands` value pins in 11d
read through **locals** on purpose: all five operands are `const float`, so an inline compare folds to
a constant condition and the failure body compiles as unreachable (CS0162). `warnaserror` /
`TreatWarningsAsErrors` appears nowhere in `ProjectSettings/` and there is no `csc.rsp`/`mcs.rsp`, so
it would not have blocked — but a pin the compiler has already answered is console noise, and through
a local it is real code that still fires the day either side is re-authored.

**The suite's verdict line was updated too** (`HudUiRegression.cs:~304`, the WO-1494 classification):
check 11 is added to the **MEASURED** clause, since it resolves a constructed `HudAreasHost` at two
aspects. A check absent from that roster would have made a green log mis-describe itself.

---

## 6. What the gate and the capture must show

1. **`COMPILE_GATE_OK`** — four files, one new public surface in `DeNelle.Core.UI`.
2. **`REGRESSION_OK`** with `HudUiRegression` green, and its notes carrying the new
   `RIGHT COLUMN (WO-1670) —` lines: two `Echoes chip [...] , QueueStatus [...]` rows, two
   `resting rail stack ends at y ...` rows, and the one `NOT MODELLED, and it is open` line.
3. **A town frame (headless `UI_CAPTURE_OK` or a device frame).** The Echoes chip sits **~32 device px
   lower** at the Seeker than in `Builds/device-frames/2026-09-10_0602_town.png` — its plate ink should
   measure roughly device rows **619..701** (was 585..658), with the ATTACK REPORT chip unmoved at
   471..545 and a visibly larger gutter between them. Nothing else on the right column moves.
4. **Suites to watch:** `HudUiRegression` checks 6e, 7, 8 and the new 11; `HudLabelFitRegression`
   (`RailChipWidthPx == EchoChipWidthPx` pin at `:148-149`); `DefenseReportLayoutRegression` case 4
   `[chip-gate]`; `RaidHudThumbBandRegression` (it reads this file's other bands).

---

## 7. Raised, not fixed

1. ⭐ **THE EXPANDED RESOURCE PANEL STILL PUSHES A RAIL CHIP INTO THE ECHO BAND — open, and it is an
   owner call.** `HudRailClearance` derives each rail chip's y from the laid-out panel and is
   *explicitly permitted* to leave the mount (`HudKitController.cs:5860-5863`: *"a mount point, not a
   clip rect"*). At 2670x1200 the ATTACK REPORT chip derives to y **0.4667..0.3507** at four resource
   rows (0.4139 bottom at three, 0.2244 at six) and crosses the new 0.384..0.500 band. **It crossed
   the old one too — pre-existing, not introduced here.** No authored fraction can close it: the depth
   is a runtime function of `kinds.Length`, and the Echoes chip is on a different canvas in an
   assembly that cannot see `HudRailClearance` (`internal` to `DeNelle.HUD`). Options, all owner calls:
   (i) reserve the worst-case depth in the seam — at six rows that drops the chip's top to ~0.218 and
   makes the DEFAULT screen worse for a transient state; (ii) hide the Echoes chip while the resource
   panel is open; (iii) move it off the right gutter. **Recorded in check 11 as a NOTE with its
   numbers, deliberately not failed.**
2. **`UICaptureLaunch.cs:4714` holds a THIRD stale copy** of this band — `(0.780, 0.530)..(0.995,
   0.865)`, its comment calling it *"the exact `HudArea.QueueStatus` band geometry"*. It is neither
   today's 0.510..0.750 nor the retired 0.420: a fixture rendering a band the game does not have.
   Out of scope by the WO's §7; it should read `HudLayoutBands.QueueStatusMount`.
3. **`HudKitController.cs:5633` cites `EchoUnlockFeedback.cs:381`** for the 54 ref px inset. That const
   is now in the seam, so the citation is stale. Left alone deliberately — that file is forbidden here
   and the seam documents the equality from its own side.
4. **`HudLayoutBands.RaidReadoutBand.yMin` is also 0.510** and is a tempting derive from
   `QueueStatusMount.yMin`. **Deliberately not taken** — it is a hostile-posture band whose own doc
   says it *takes* the ActionRail + QueueStatus seat; coupling them would let a raid-side change move
   a town band.
5. **`ReadAreaBandY` is now a one-caller helper.** Left in place; collapsing it is merge noise.
6. **`HudLabelFitRegression.cs:148` carries the same now-dead symbol in a doc comment** —
   *"RailChipWidthPx == EchoUnlockFeedback.EchoChipWidthPx"*. It is a comment, not an assert, so
   nothing reds; the const now lives at `HudLayoutBands.EchoChipWidthPx`. That file is outside this
   ticket's footprint and is deliberately not edited.

---

## 9. SECOND PASS — owner ruling 2026-09-10 12:55: hide the ATTACK REPORT chip while the resource panel is expanded

The ruling picks option **(ii)** of the three §7.1 offered. §7.1 stays above, unedited, as the
measurement it was made on.

### 9a. Two more files

| file | lines | what |
|---|---|---|
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | +57 −6 | the hide, in the ONE visibility writer + the edge ring |
| `Assets/Editor/UICaptureLaunch.cs` | +12 −2 | the third stale band copy retired onto the seam |

Running total across both passes: **616 insertions, 80 deletions, 6 files.**
`python tools/gate_brace.py` on all six: **`GATE_BRACE_SUMMARY bad=0 of 6`, exit 0.** NUL: all clean.

### 9b. The ONE visibility owner, cited

**`HudKitController.TickDefenseReportChip` — `Assets/_Modules/HUD/Kit/HudKitController.cs:2198`
(signature; the `SetActive` is at `:2244`).** It holds the **only** `_defenseChipBand.gameObject
.SetActive(` in the codebase — `grep -c` returns **1**, and check 11e-2 now pins that count.
WO-1515's own header (`:294-295`) states the rule this is built on: *"a second object is how a widget
ends up permanently off in exactly one posture."*

The change, in four parts:

1. **The panel is an INPUT to the one writer.** `bool panelOpen = _resChipsExpanded;` →
   `bool wantVisible = snap.Visible && !panelOpen;` → `SetActive(wantVisible)`. ⛔ A hide written
   from `SetResourcePanelOpen` would be a **second writer** racing this method's 0.5 s throttle —
   the chip would flicker back on over the open panel. That is why the ruling is implemented as an
   input, not as a call.
2. ⚠ **The change detector had to learn about the panel, and this is the subtle half.**
   `DefenseReportChipModel.Current.Key` does **not** move when the player toggles the resource
   panel, so the existing `if (snap.Key == _defenseChipKey) return;` early-out would never
   re-evaluate and the chip would sit on the expanded panel forever. New field
   `_defenseChipPanelWasOpen` (`:298-304`) folds the panel into the SAME detector — not a second one.
3. **The edge is RUNG, not polled.** `SetResourcePanelOpen` (`:4309`) clears the throttle
   (`_defenseChipPollTimer = DefenseChipPollSeconds;`) and calls `TickDefenseReportChip()`, so the
   hide lands on the **expand frame**. This is the identical idiom two lines above it, where the
   same method already rings `HudRailClearance.MarkDirty()` — and for the identical reason: a
   `SetActive` on a descendant raises no layout event, and leaving it to a poll turns a rule into a
   race.
4. **`FlowTrace.Step` on each hide/show edge**, gated on `panelEdge` so it fires once per toggle and
   not on every model repaint: `WO-1670b attack report chip HIDDEN/SHOWN on the resource panel
   EXPAND/COLLAPSE edge (owner ruling 12:55) - model says visible=…, so on screen=…`. The always-on
   `WO-1515` line gained `onScreen=` and `panelOpen=` fields.

**The report is not consumed.** `snap.Visible` is never overwritten — the panel only *suppresses* an
otherwise-visible chip, so a report landing while the panel is open is still unread and appears on
collapse. That is the "it returns on collapse" half of the ruling, by construction rather than by a
restore path.

### 9c. The pin — RED verified against HEAD source, this session

Check 11 gains **11e**, four asserts, all sliced to the single method so a `_resChipsExpanded` read
elsewhere in this 5,900-line file cannot satisfy them:

| assert | what it holds |
|---|---|
| 11e-1a | `TickDefenseReportChip`'s body reads `_resChipsExpanded` |
| 11e-1b | its `SetActive` argument is `wantVisible`, not the model's raw `snap.Visible` |
| 11e-1c | the change detector folds in `_defenseChipPanelWasOpen` — the "would never re-evaluate" trap |
| 11e-2 | **exactly ONE** `_defenseChipBand.gameObject.SetActive(` in the file (the structural half — this one cannot be faked) |
| 11e-3 | `SetResourcePanelOpen`'s body rings `TickDefenseReportChip();` |

⭐ **RED, executed rather than asserted.** I dumped `git show HEAD:…HudKitController.cs`, sliced
`TickDefenseReportChip` and `SetResourcePanelOpen` with the regression's own bounds, and measured:

```
HEAD tick: _resChipsExpanded= False | SetActive(wantVisible)= False | panelWasOpen= False
HEAD ring present: False
```

All four asserts fire on the pre-ruling tree. The slice bounds were also verified against the
post-fix tree (`True/True/True`, ring `True`, writers `1`), so the check is measuring the right text
in both directions. ⛔ Still **not run inside Unity** — no gate was fired here.

**Geometry, closed by the ruling:** with the panel **OPEN** there is no chip to overlap; with it
**CLOSED**, 11b proves the resting rail stack sits inside its mount (y 0.5118 / 0.5370 vs the 0.510
floor) and 11c proves the Echoes band is clear of it. The 11c "NOT MODELLED" note is replaced by
11e's statement of that argument.

### 9d. The third stale band copy, retired

`Assets/Editor/UICaptureLaunch.cs:4714` hardcoded `(0.780, 0.530)..(0.995, 0.865)` while its own
comment called it *"the exact `HudArea.QueueStatus` band geometry"*. It was **wrong twice over** —
neither today's 0.510..0.750 nor the 0.420 it was drifting from — so the queue-rail capture frame
judged a band the game does not ship. It now resolves `HudLayoutBands.QueueStatusMount.min/.max`
(the file already carries `using DeNelle.Core.UI;` at `:151`). Minimal, delimited: the one
`MakeAreaMount` call, nothing else in that fixture.

### 9e. What the capture must now show, in addition to §6

- A town frame with an **unread report AND the resource panel COLLAPSED**: the ATTACK REPORT chip is
  present at device rows ~471..545, the Echoes chip is at ~619..701, visibly clear.
- The same state with the **panel EXPANDED**: the ATTACK REPORT chip is **gone**, the expanded rows
  are unobstructed, and the trace carries the
  `WO-1670b attack report chip HIDDEN on the resource panel EXPAND edge` line.
- Collapse again: the chip **returns** with the same caption and unread count, and the trace carries
  the matching `SHOWN … COLLAPSE` line.

---

## 8. Hand-back

- **Board:** `WorkOrders/WORK_ORDER_1670_echo_chip_encroaches_the_queue_status_band.md` carries
  `**Status:** IMPLEMENTED - awaiting gate + capture (lane ECHO-CHIP 2026-09-10)`.
  The WO number was **pre-assigned by the lead**; this lane did **not** edit
  `CLI_LANES_WO_NUMBERS.md`, and `BOARD.html` is the lead's regenerate.
- **Paths:**
  - `WorkOrders/WORK_ORDER_1670_echo_chip_encroaches_the_queue_status_band.md`
  - `WorkOrders/WORK_ORDER_1670_echo_chip_encroaches_the_queue_status_band.RESULT.md`
  - `Assets/_Modules/Core/UI/HudLayoutBands.cs`
  - `Assets/_Modules/HUD/Kit/HudAreasHost.cs`
  - `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs`
  - `Assets/Editor/Regression/HudUiRegression.cs`
  - `Assets/_Modules/HUD/Kit/HudKitController.cs` *(second pass, ruling 12:55 — §9)*
  - `Assets/Editor/UICaptureLaunch.cs` *(second pass, §9d)*
