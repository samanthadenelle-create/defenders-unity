# WORK ORDER 1664 — RESULT

**Lane:** TOUCH-FLOOR, 2026-09-10. Isolated worktree `.claude/worktrees/agent-a35ba6c65858e79ea`,
fast-forwarded to `dev` at **`95eb7ea73`** (`git merge --ff-only refs/heads/dev`).
**No Unity was run and nothing was committed** — by instruction. Everything below that is a
measurement says where it was measured; everything that is not yet proven says so by name (§11B).

---

## 0. TL;DR

Three DRIVER bands re-authored at `ElarionUiKit.MinTouchPx`, plus the two capture entry points that
were computing the touch verdict and throwing it away. **Two of the ticket's own conclusions were
wrong** and are corrected at source, in the code, not just here.

| Control | Driver (the real one) | Authored before | After | Δ |
|---|---|---|---|---|
| `ObsBtn_Continue` / `Start New` / `Play Intro` | `TitleController` row anchors | 69.5 ref px | **115.8** | +46.3 |
| `ObsBtn_REPAIR ALL` | **`HudLayoutBands.ToastZone`**, not the call site | 101.3 | **117.8** | +16.5 |
| `ObsBtn_COLLECT` (+ the WO-1408 raid door) | `WelcomeBackPopup.ActionBandY0/Y1` | 89.2 | **113.5** | +24.3 |
| *(sixth, never on any log)* WO-1408 door row | `WelcomeBackPopup.DoorRowH` | 102.2 | **115.2** | +13.0 |

`ElarionUiKit.MinTouchPx` = **112f** — untouched. `ClampMinTouch` / `UiKitMinTouchGuard` /
`RecordClampGrowth` — untouched. `TouchBaseline` — untouched, and the new suite now FAILS if
`Title`, `WelcomeBack` or `HubRepair` is ever added to it.

---

## 1. ⛔ TWO CORRECTIONS TO THE TICKET. Read these before anything else.

### 1a. §2B / §4B are WRONG: `0.72` is a WIDTH slice. Following them would have shipped a wider card and left the clamp firing.

The ticket says *"The `0.72` height fraction of that zone resolves to 101.4 px"* and prescribes
`0.72 x 112/101.4 = 0.795, so ~0.80`. `HudLayoutBands.ToastZoneSlice`
(`Assets/_Modules/Core/UI/HudLayoutBands.cs`, the method directly under `ToastZone`) lerps **X only** and its own doc says
`from`/`to` are *"0..1 across the zone's WIDTH"*:

```
float x0 = Mathf.Lerp(ToastZone.xMin, ToastZone.xMax, Mathf.Clamp01(from));
float x1 = Mathf.Lerp(ToastZone.xMin, ToastZone.xMax, Mathf.Clamp01(to));
return Rect.MinMaxRect(x0, ToastZone.yMin, x1, ToastZone.yMax);   // <- yMin/yMax passed straight through
```

The device log proves which axis `0.72` drives, in its own numbers:
`authored 386.6x101.4`, and `0.72 x 0.25 x 2148.0 = 386.6` — the **width**, to one decimal.
The height `101.4` is `(0.308 - 0.203) x 965.4 = 101.3`, i.e. the zone's whole height, with no call-site
term in it at all.

So the ticket's own **§4B escape hatch** is the branch that is true: *"If `HudLayoutBands.ToastZoneSlice`'s
zone is itself too short to seat 112 px at any fraction, the change belongs in `HudLayoutBands` and it
moves for every toast."* **That is what was done**, and §6's required cross-module line is §3b below.
`0.72` was left exactly as it is, and `HubRepairAffordance` still seats through `ToastZoneMin`/`ToastZoneMax`
— its `:528-534` instruction (*"Do NOT author a rect here again"*) is honoured and now pinned.

### 1b. §3C is WRONG for the welcome-back modal: it IS captured. The verdict was thrown away — the SAME defect as §3B.

§3C states *"No `Capture*` entry point ever builds either canvas, so `LayoutOracle.Audit` has no rect to
measure and Assert A cannot fire at gate time."* For `WelcomeBackPopup` that is false:

- `UICaptureLaunch.RunWelcomeBackCaptureHeadless` exists, and builds **two** fixtures —
  `ForEachTarget("WelcomeBack", CaptureWelcomeBackOnce)` + `ForEachTarget("WelcomeBackDoors", CaptureWelcomeBackDoorsOnce)`.
- `CaptureWelcomeBackDoorsOnce` renders `popup._modal.canvas` through `RenderCanvasToPng`, which calls
  `AuditGeometry` on the settled layout (`UICaptureLaunch.cs:5814` — the file's only audit call site).
- `AuditGeometry` has been incrementing `_touchPanelsChecked` and filling `_touchFailures` from the
  `SUB-TOUCH-FLOOR BAND` prefix on every one of those builds. **Nothing ever called `ReportTouchOracle()`
  on that path.**

This matters because it changes the fix. §5.6 asked for a NEW capture entry point as "the only thing that
closes 3C properly"; the honest close is **read the verdict already being computed**. So §3B and §3C are
**one cause with two faces**, not the two independent explanations the ticket warned against — and the
correction is written into `RunWelcomeBackCaptureHeadless` itself, not left in this file.

**`HubRepairAffordance` genuinely has no capture fixture** (`grep -rn "HubRepairAffordance" Assets/Editor/`
returns only `RepairProbeSeverityRegression.cs`, three source-text lines). I did **not** force one: the card
needs a live `HudAreasHost`-bearing town canvas plus a `WallRepairController` in a damaged-and-affordable
state, which is a play-mode fixture, not a `CaptureTitleOnce`-shaped builder. Its proof is therefore the
arithmetic pin on `HudLayoutBands.ToastZone` (§5 case `[repair-all-seats-the-touch-floor]`) plus the fresh
device logcat in §7 — stated plainly rather than allowed to become "no capture exists, so no proof needed".

---

## 2. The unit that caused all of this: REFERENCE px, not device px

`MinTouchPx` is a **reference-pixel** floor. A band authored as a fraction resolves against the
CanvasScaler's post-scale height. The kit scaler is `referenceResolution (1080,1920)`,
`ScreenMatchMode.MatchWidthOrHeight`, `matchWidthOrHeight = 0.5f`
(`Assets/_Modules/Core/UI/ElarionUiKit.cs:109-111`, read at source this session), so

```
scale     = (W/1080)^(1-0.5) x (H/1920)^0.5
refHeight = H / scale
```

| Capture aspect | scale | **refHeight** | refWidth |
|---|---|---|---|
| 1920x1080 | 1.0000 | 1080.0 | 1920.0 |
| 2340x1080 | 1.1039 | 978.4 | 2119.9 |
| **2670x1200 (the Seeker)** | **1.2430** | **965.4** | **2148.0** |

**965.4 is the smallest, so every band below is authored against it.** Two independent corroborations that
the model is right, both from the device log rather than from this arithmetic:

- `[Flow:Manage] bands(px): canvas=965` (11:36:27.793) — the runtime printing its own reference height.
- `COLLECT authored ...x89.2` for a `0.110` band ⇒ content height `89.2 / 0.110 = 810.9`, and
  `0.84 x 965.4 = 810.9` where `0.84` is the modal shell's own `0.08..0.92` anchors.

⚠ **This is the whole bug in `DoorRowH`.** Its retired doc block computed *"at 0.21 the plate is ~127px"* —
that is `0.21 x 0.60 x 0.84 x **1200**`, i.e. DEVICE pixels. In reference pixels the same plate was
`0.21 x 0.60 x 0.84 x 965.4 = **102.2**`, 9.8 under the floor it claimed to clear. **A band can pass its own
comment and fail the oracle, and this one did for a month.** The new suite derives `refHeight` by parsing the
scaler out of `ElarionUiKit.cs` rather than hardcoding 965.4, so re-tuning the scaler re-tunes the pin.

---

## 3. The changes, with line numbers (post-edit)

### 3a. `Assets/_Modules/Onboarding/TitleController.cs` — the title row (WO §4A)

`:311` `rt.anchorMax = new Vector2(0.80f, 0.135f);` → **`0.195f`**. `anchorMin.y` stays `0.045f`, so the
row grows **upward off a fixed bottom margin** and the thumb-reach edge does not move.
`:279-309` — the comment block replaced. The old one read *"Kept a healthy ~7% screen-height so the touch
target stays tappable on mobile"*, a claim the device refuted three times; the new one carries the formula.

```
faceH = (anchorMax.y - anchorMin.y) x refHeight x 0.80      // faces fill 0.10..0.90 of the row
before: 0.090 x 965.4 x 0.80 =  69.48 ref px   (logged: 69.5)   ->  1.61x clamp growth
after:  0.150 x 965.4 x 0.80 = 115.85 ref px                    ->  clamp has nothing to do
```

⛔ **The ticket's suggested `~0.190` DOES NOT CLEAR THE FLOOR.** `(0.190-0.045) x 965.4 x 0.80 = **111.94**`
— 0.06 px under 112, and `ClampMinTouch` would still fire. `0.195` is the value used, and this is exactly
the kind of near-miss the arithmetic pin exists to catch.

**Collision proof (WO §4A's required check, measured not inherited).** `BuildTitleTextBlock` and
`BuildButtonColumn` both parent to `_canvas.transform` (`:204`, `:206`), i.e. the SAME full-canvas space,
so the comparison is fraction-vs-fraction at every aspect with no scaling term:

| element | band | vs row top 0.195 |
|---|---|---|
| tagline (`:265`) | 0.60 .. 0.65 | **0.405 of screen clear** |
| series | 0.655 .. 0.70 | clear |
| title | 0.70 .. 0.84 | clear |

Also unchanged, as §4A requires: `TitleActionWell` inset `0.01..0.99 x 0.08..0.92`, the tray sprite, and
the `slotGap`/`slotW` width distribution. Both row shapes (2 entries with no save, 3 with) share the row,
so the height fix covers both.

### 3b. `Assets/_Modules/Core/UI/HudLayoutBands.cs` — the shared toast zone (WO §4B, §6 cross-module line)

`:386` `ToastZone = Rect.MinMaxRect(0.375f, 0.203f, 0.625f, **0.308f**)` → **`0.325f`**.
`:338-385` — the doc block gained the §1a correction, the arithmetic and the clearance measurement.

```
before: (0.308 - 0.203) x 965.4 = 101.35 ref px   (logged: 101.4)   ->  1.1x clamp growth
floor:   112 / 965.4              = 0.1160 of screen height
after:  (0.325 - 0.203) x 965.4 = 117.78 ref px
```

**It grows UPWARD and `yMin` is untouched**, so no toast's bottom edge moves. Headroom, from the zone's own
doc block and re-checked against the band table above it: within the zone's x range (0.375-0.625) the only
neighbour is `TargetInfo` (x 0.280-0.720), whose floor is **y 0.660** — 0.335 of screen above the new top.
Below, `ActionBar` tops out at 0.150, 0.053 under the unchanged `yMin`.

⛔ **§6'S REQUIRED CROSS-MODULE LINE — this moves for every toast, by design.** Consumers that grow with it:
`HudKitController` toast (`ApplyToastZone`, `:561-562`), `BreakCaptureHarness` FLAGGED acknowledgement
(`:749`), WO-1236's dungeon-flag ack, and **both** faces of the Repair All card — the button and its
acknowledge close, which takes `ToastZoneSlice(0.76f, 1f)` and inherits the same height. All of them get
**taller**, none get repositioned, and every one of them is a touch or display surface for which a taller
band is a strict improvement. `HudUiRegression` check 9b does **not** pin ToastZone (its own comment
`:1861-1863` records that exclusion deliberately), so nothing pinned the old height.

### 3c. `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs` — COLLECT, the raid door, the door row (WO §4C)

Content height = `0.84 x 965.4 = 810.9` ref px (device-corroborated, §2).

| line | before | after | resolves |
|---|---|---|---|
| `:121` `DoorRowH` | `0.21f` | **`0.245f`** | 102.2 → **115.2** ref px |
| `:130` `BodyY0` *(new const)* | inline `0.22f` | **`0.24f`** | body 0.60 → 0.58 of content |
| `:140-141` `ActionBandY0/Y1` *(new consts)* | inline `0.045f`/`0.155f` | **`0.040f`/`0.180f`** | 89.2 → **113.5** ref px |
| `:205` body floor | `0.22f` literal | `BodyY0` | — |
| `:285` COLLECT | `(0.37,0.045)..(0.63,0.155)` | `(0.37,ActionBandY0)..(0.63,ActionBandY1)` | 113.5 |
| `:607` ready line | `0.168..0.215` | **`0.185..0.235`** | 38.1 → **40.5** ref px |
| `:618` raid door | `(0.68,0.045)..(0.90,0.155)` | `(0.68,ActionBandY0)..(0.90,ActionBandY1)` | 113.5 |

**Why the body floor moved, and why that was the cheapest edit.** The bottom of the content is a closed
budget: the shell's footer inset below, the body's floor above. COLLECT needs `112/810.9 = 0.1381` of
content and had `0.110`. Spending that 0.028 downward would have cut the bottom margin from 36.5 px to
~23 px; spending it upward would have crushed the readiness line to ~28 px, under a `FontMicro` line's own
height. Moving the body floor 0.22 → 0.24 pays for it out of the one band with 0.60 of content to spare:
**the buttons gain 24.3 px, the ready line gains 2.4 px, the bottom margin does not move, and body rows lose
3.3% of their height and none of their count** (`MinRowY`, `MaxJobRows`, `MaxCollectorRows` unchanged).

⛔ **§4C's `AddReadyBand` warning is now structural, not a comment.** The ticket says *"move both together,
or the two faces desynchronise"*. Both call sites now read the SAME two constants, so they cannot drift —
and the new suite fails if either goes back to a hand-typed band.

**The sixth control, which was on no log and no gate.** Wiring the touch reporter on the welcome-back path
(§4 below) would have turned the `WelcomeBackDoors` fixture red on day one, because the WO-1408 door row
resolved 102.2 ref px. It was invisible in three independent ways at once: the gate did not report, the
device session carried no door row, and its own doc block computed in the wrong units. Rather than land an
oracle that reds immediately and gets suppressed within the week — the failure `UICaptureLaunch.cs:6014-6017`
names in so many words — **`DoorRowH` was fixed in the same change**. Cost: a taller door row means three fit
in an empty body where 0.21 allowed four; `AddDoorRows` already `FlowTrace.Warn`s and skips on overflow, and
a fourth door the player cannot hit is worth less than three they can.

### 3d. `Assets/Editor/UICaptureLaunch.cs` — the thrown-away verdicts (WO §3B, §5 pin 1)

**Both folds were verified against the fixtures, not assumed.** `_touchPanelsChecked` increments once
per `RenderCanvasToPng` → `AuditGeometry`, so `== 6` is only right if each fixture renders exactly one
canvas per call. Counted this session: `CaptureTitleOnce`, `CaptureLoginOnce`, `CaptureWelcomeBackOnce`
and `CaptureWelcomeBackDoorsOnce` each contain **exactly one** `RenderCanvasToPng` and each returns
`? 1 : 0` from it. So front-door = 3 Title + 3 Login = 6, welcome-back = 3 + 3 = 6, and `count` and
`_touchPanelsChecked` fall together on an early-out rather than diverging.

**The two marker strings gained a `; touch=clean` suffix, and nothing parses them.** A grep for
`FRONT_DOOR_CAPTURE_OK` and `WELCOME_BACK_CAPTURE_OK` across `*.ps1 *.py *.sh *.cs *.md` returns
**no script consumer at all** — only the two emit sites, frozen dated ledgers
(`docs/HANDOVER_2026-09-05_overnight.md`, `docs/ui-reskin/UI_RESKIN_EXECUTION_LEDGER_2026-08-31.md`),
`docs/MASTER_CATALOG/village-systems.md` and historical WOs. `RegressionMarkerRegression` names
neither. Neither appears in `UICaptureLaunch`'s own header marker table (that table documents
`RunCaptureHeadless`'s markers), so there was no "named ONCE" entry to move. `TOUCH_FLOOR_AUTHORING_OK`
needs no registration — like `INVENTORY_ARMORY_RAIL_OK` it lives only in its own file, and the suite's
real reporting path is its `DataRegression` registration.

**`RunFrontDoorCaptureHeadless` (`:2275-2316`)** — added the reset triple beside `ResetGlyphOracle()`
(`_touchFailures.Clear()`, `_touchPanelsChecked = 0`, `_touchPanelsClean = 0`), `ReportTouchOracle()` beside
`ReportGlyphOracle()`, and folded the touch tally into the verdict:

```
bool touchClean = _touchPanelsChecked == 6 && _touchPanelsClean == _touchPanelsChecked
                  && _touchFailures.Count == 0;
if (count == 6 && touchClean) Debug.Log("FRONT_DOOR_CAPTURE_OK 6/6; touch=clean");
```

**`RunWelcomeBackCaptureHeadless` (`:2005-2043`)** — the identical shape, per §1b.

Both are copied **verbatim** from `RunGooglePlayLoginCaptureHeadless` (its reset triple, report call and folded verdict at `:2336-2339`, `:2367`, `:2376-2380` post-edit)
rather than invented in a third form; a third reporting shape in this file is what let these two drift.

**Why the fold, and not just the marker.** Acceptance §7.3 asks that `FRONT_DOOR_CAPTURE_OK 6/6` still emit.
It does — with `; touch=clean` appended — but only when the touch tally is clean. A capture that renders six
clean PNGs of a panel whose faces are under the touch floor is not a pass, and `UI_TOUCH_FAIL` printed beside
a green `FRONT_DOOR_CAPTURE_OK` is precisely the split verdict CLAUDE.md §8 describes (*"a 22-case suite's
pass read as the full suite's pass"*). Weakening the assert to avoid a red I expect is the thing §5 forbids.

⚠ **RISK, NAMED IN ADVANCE.** Neither path has EVER had its touch tally read, so the first run measures
`Login`, `WelcomeBack` and `WelcomeBackDoors` for the first time. If a control on one of those panels is also
sub-floor, these markers red. **That is a finding, not a reason to unfold the assert** — it is the same class
this ticket exists to close, one panel further on. I could not run Unity, so I have **not proven** either path
goes green; §7 is the proof.

### 3e. `Assets/Editor/Regression/AwaySummaryReportRegression.cs:139` — the moved pin

`"new Vector2(0.63f, 0.155f), CollectAndDismiss"` → `"ActionBandY1), CollectAndDismiss"`, with a comment
recording why. The case is named `[collect-button-performs-its-verb]` and its failure text is about the
**verb**; the rect was incidental context that happened to share the line. Pinning the constant keeps the
verb assertion and stops the case failing every legitimate re-seat. The band's *arithmetic* is not that
suite's job — the new suite owns it.

---

## 4. The new suite — `Assets/Editor/Regression/TouchFloorAuthoringRegression.cs` (WO §5)

Registered in `Assets/Editor/Regression/DataRegression.cs:1200-1207` beside the inventory-armory-rail suite,
inside the same `Guard.Try` shape, so it rides `REGRESSION_OK <n>/<n> suites`. Standalone marker for a
focused run: `TOUCH_FLOOR_AUTHORING_OK` / `_FAIL`.

| WO §5 pin | Case | Independent authority |
|---|---|---|
| 2 | `[title-row-seats-the-touch-floor]` | `ElarionUiKit.MinTouchPx` + the parsed scaler model **vs** the row anchors and inner face fraction parsed out of `TitleController` |
| 3 | `[title-row-clears-the-tagline]` | the lowest `BuildTitleTextBlock` label band **vs** the row's `anchorMax.y` |
| 4a | `[repair-all-seats-the-touch-floor]` | floor + scaler **vs** `HudLayoutBands.ToastZone`, **plus** that `HubRepairAffordance` still seats through `ToastZoneMin/Max` (`:528-534`) |
| 4b | `[collect-seats-the-touch-floor]` | floor + scaler **vs** `ActionBandY0/Y1`, `BodyY0`, `DoorRowH` and the parsed modal shell anchors — **the door row is asserted in the same case on purpose** |
| 5 | `[welcomeback-collect-and-raid-door-share-one-band]` | the two call sites **vs** each other |
| 1 | `[oracles-are-read]` | the captured `CLAMP FIRED` lines **vs** both entry points still resetting, reporting and folding in the tally; **plus** `TouchBaseline` must not contain `Title`/`WelcomeBack`/`HubRepair` (§6, shrink-only) |

Design notes that matter to the next seat:

- **It never recomputes geometry from the layout's own constants.** `InventoryArmoryRailRegression:42-44`
  records that this repo found three structurally-unfailable suites in twenty-four hours; every case here
  pairs parsed anchors against a separate authority.
- **`MinTouchPx` is referenced as a live symbol, never parsed.** Lowering it to "fix" a red (§6's exact
  inversion) moves the assertion with it and trips the six other regressions that name it.
- **`refHeight` is derived from the parsed scaler**, and the suite FAILS loudly if `ScreenMatchMode` is no
  longer `MatchWidthOrHeight` — the geometric-mean formula is wrong for Expand/Shrink, and a silently-wrong
  reference height would make every case here a false green.
- **`MethodBody` is brace-matched**, so a call found in the wrong method cannot satisfy a pin.
- Honest label: **arithmetic on source-parsed constants, plus two source lints.** It proves an authored band
  resolves under the floor; it does not prove the band lays out as its anchors say. `LayoutOracle.Audit` on
  the capture path is that authority, which is why `[oracles-are-read]` exists.

---

## 5. RED-first — the exact revert (WO §5, acceptance §7.2)

No Unity was run, so **the RED and GREEN runs are owed, not claimed.** The recipe is exact.

**RED (title, front-door path).** Keep the `UICaptureLaunch.cs` wiring, revert only the driver:

```powershell
git checkout HEAD -- Assets/_Modules/Onboarding/TitleController.cs
# ... run RunFrontDoorCaptureHeadless ...
```

Expected on the log — judge by the MARKER, not the exit code:

```
[touch-oracle] SUB-TOUCH-FLOOR BAND [Title @2670x1200] '.../ObsBtn_Start New' resolves 399.5x69.5 ref px
               -- shortest side 69.5 is 42.5 px UNDER ElarionUiKit.MinTouchPx (112). ...
UI_TOUCH_FAIL x<n> over 6 panels (<m> clean) -- ...
FRONT_DOOR_CAPTURE_FAIL 6/6; touchPanels=6; touchClean=<m>; touchFailures=<n>
```

⚠ **The example line is the SEEKER aspect and the numbers are aspect-specific.** `399.5x69.5` is
`@2670x1200` (refHeight 965.4); the same face at `@1920x1080` reads `0.090 x 1080 x 0.80 = 77.8` and at
`@2340x1080` `70.4`. All three are under 112, so all three red — with different numbers.

**`<n>` and `<m>` are deliberately not predicted.** `<n>` is 6 if `HasExistingSave()` is false in the
fixture (2 faces x 3 aspects) and 9 if it is true, and `<m>` depends on **Login, which has never been
touch-measured** — so "3 clean" would be an assertion I have not earned. The assertion is the MARKER's
presence, not its counts. `Title` is **not** in the `TouchBaseline` array (it holds only `ArmyMuster` +
`EquipDrawer`, verified this session), so nothing suppresses it.

**GREEN.** `git checkout <this branch> -- Assets/_Modules/Onboarding/TitleController.cs`, rerun, expect
`UI_TOUCH_OK 6/6 panels` and `FRONT_DOOR_CAPTURE_OK 6/6; touch=clean`.

**RED for the suite (all six cases, one revert each):**

| Case | One-line RED |
|---|---|
| `[title-row-seats-the-touch-floor]` | `anchorMax` y back to `0.135f` (or `0.190f` — it reds at 111.94, which is the point) |
| `[title-row-clears-the-tagline]` | `anchorMax` y to `0.70f` |
| `[repair-all-seats-the-touch-floor]` | `ToastZone` yMax back to `0.308f` |
| `[collect-seats-the-touch-floor]` | `ActionBandY1` back to `0.155f`; separately `DoorRowH` back to `0.21f` |
| `[...share-one-band]` | put `0.155f` back at one of the two call sites only |
| `[oracles-are-read]` | delete `ReportTouchOracle()` from `RunFrontDoorCaptureHeadless` |

---

## 6. Gate hygiene (CLAUDE.md §1)

`python tools/gate_brace.py` — the port of `CompileGate.BraceBalanced`'s exact rule — over all seven
touched `.cs`: **`GATE_BRACE_SUMMARY bad=0 of 7`, exit 0.** NUL scan (WO-434) over the same seven: **0
NUL bytes** in every file.

⚠ **The naive one-liner disagrees on `TouchFloorAuthoringRegression.cs` (63 open / 62 close) and the gate
is right.** The difference is `{` inside string and char literals — `@"\s*\{"` in the method-body regex and
`'{'` / `'}'` in the brace matcher. This is the documented divergence CLAUDE.md §1 warns about, and it is
**not novel**: measured this session, **18 other files in `Assets/Editor/Regression/` alone** are raw-imbalanced
for the same reason (`SheathePoseRegression.cs` 177/175, `PanelDoorRegression.cs` 60/58, …). `gate_brace.py`
is the authority and it is clean.

---

## 7. What is NOT proven, and exactly what closes it

⛔ Per §11B these are findings, not ticks. **Acceptance §7.1 and §7.4 are OPEN.**

1. **No Unity run.** `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n> suites` are owed on a fresh log. The new
   suite has never executed; its `Guard.Try` registration means a throw is a failure line, not a crash, but
   *"it should pass"* is a guess and is not claimed.
2. **No capture run.** `FRONT_DOOR_CAPTURE_OK` / `WELCOME_BACK_CAPTURE_OK` with `touch=clean` are owed, and
   §3d's named risk (Login / WelcomeBack measured for the first time) resolves only there.
3. **No device logcat.** Acceptance §7.1 — `grep -c "CLAMP FIRED"` = 0 on a fresh `logcat -c` + force-stop +
   relaunch, walked to the title, through the welcome-back modal, with the repair affordance shown — is
   **the** proof for `HubRepairAffordance`, which has no headless fixture (§1b). Until that log exists, the
   repair card's fix is arithmetic only.
4. **No frames opened.** Acceptance §7.4 needs eyes on the title row, the welcome-back modal and the repair
   affordance. One specific thing to look at: the title screen normally renders **cover art with the title
   baked in** (`BuildTitleTextBlock` runs only when the art is missing), so the row's top rising 0.06 of screen
   height is a collision risk against *painted* art that no fraction arithmetic can see.
   **A second, specific thing to look at on that frame:** the row's tray and all three faces use the
   `UI/ElarionMedieval/frames/content-panel` sprite with `Image.Type.Simple` — **not** `Sliced` — and
   the row is now 1.67x taller, so the border art stretches vertically rather than tiling. Named, not
   fixed: if it reads badly the remedy is `Sliced` on a sprite with real 9-slice borders, which is a
   change to the frame asset and belongs in its own ticket, not smuggled into a touch-floor fix.
5. **Nothing committed, nothing pushed** — by instruction.

## 8. Files touched

```
Assets/_Modules/Onboarding/TitleController.cs                        (row band + comment)
Assets/_Modules/Core/UI/HudLayoutBands.cs                            (ToastZone height + doc)
Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs               (4 bands, 3 new consts)
Assets/Editor/UICaptureLaunch.cs                                     (2 entry points wired)
Assets/Editor/Regression/AwaySummaryReportRegression.cs              (pin moved to the constant)
Assets/Editor/Regression/TouchFloorAuthoringRegression.cs            (NEW - 6 cases)
Assets/Editor/Regression/DataRegression.cs                           (registration)
WorkOrders/WORK_ORDER_1664_...clamp.md                               (Status flipped)
WorkOrders/WORK_ORDER_1664_...clamp.RESULT.md                        (this file)
```

Not touched, per §6: `ElarionUiKit.MinTouchPx`, `ClampMinTouch` / `UiKitMinTouchGuard` / `RecordClampGrowth`,
`TouchBaseline`, the WO-1660 `CurrencyChip_Gold` seating, `HudLabelFitRegression.cs` (WO-1663), any `.unity`
scene, and `HubRepairAffordance`'s call-site rect.
