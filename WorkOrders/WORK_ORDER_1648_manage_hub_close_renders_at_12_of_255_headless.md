# WO-1648: the Manage hub `CLOSE` renders at 12/255 while a green lint calls it "live and legible"

**Status:** IMPLEMENTED - awaiting gate
**Silo:** Manage chrome (`Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs`) + the capture oracle.
**Number:** PRE-ASSIGNED by the lead. ⛔ **Do NOT edit `CLI_LANES_WO_NUMBERS.md`** — the lead owns the
banner bump for this mint.
**Source:** WO-1566 audit, `WORK_ORDER_1566_..._definition_of_done.RESULT.md` §5 finding **F1**
(audit committed `2039e2c41`).

---

## 1. THE MEASUREMENT — this is the whole ticket, and it was taken, not inferred

Measured on the **fresh headless captures written 2026-09-10 07:57** (`Builds/ui-capture/`), luminance
read off the PNG with PIL, threshold-free (raw `max`/`mean` of the greyscale channel):

| Frame | brightest pixel in the bottom 22% | inside the `CLOSE` plate | `BUILD` card label, SAME frame |
|---|---|---|---|
| `ManageWorkspace_1920x1080.png` | **30/255** | max **12/255**, mean **0.8/255** | **172/255** |
| `ManageWorkspace_2340x1080.png` | **30/255** | — | — |
| `ManageWorkspace_2670x1200.png` | **30/255** | max **12/255** | **172/255** |

The 30/255 is the bezel corner, not the control. **The control itself is within 12/255 of black on
every landscape aspect the capture matrix shoots.** Panel 1 of the mockup draws a legible `CLOSE`
beneath the three cards (WO-1566 §2 row 1.2).

## 1A. ✅ ACCEPTANCE 1 IS FILLED — THE DEVICE FRAME EXISTS, AND IT IS **BRANCH A**

Captured by the DEVICE-FRAMES lane, APK **2026.09.10.363660**, landscape, ONE device session.
Re-measured independently by the MANAGE-CHROME lane on 2026-09-10 with PIL (raw Rec.709 luma,
threshold-free), so the numbers below are read off the pixels, not copied from a hand-back:

| Frame | region | max luma | mean | brightest RGB |
|---|---|---|---|---|
| `Builds/device-frames/2026-09-10_0814_363660_manage_hub.png` | CLOSE box `1119,975,1553,1079` | **13.0/255** | 0.74 | `(13,13,13)` |
| same frame | CLOSE interior `1140,995,1530,1060` | **13.0/255** | 0.90 | `(13,13,13)` |
| same frame | BUILD card label | **172/255** | — | `(212,175,56)` — ⚠ **per the DEVICE-FRAMES hand-back; NOT re-measured by this lane** |
| same frame | card band `300,250,2370,950` (this lane's own reference read) | **253.7/255** | — | `(255,255,237)` |
| `Builds/device-frames/2026-09-10_0815_363660_manage_queue_drawer.png` | **the same box** `1119,975,1553,1079` | **254.4/255** | 41.2 | `(255,255,246)` |

Device log, `Builds/device-frames/2026-09-10_0824_363660_logcat.txt` (PID 1040) at **08:14:35.210**:
`[Flow:Manage] MANAGE_HUB_CLOSE the shared CLOSE is live and legible ... it was never disabled,
only unreadable` — i.e. the setter echo fired green on the frame that measures 13/255.

**VERDICT: §3 branch one — a real render defect. The device is dark too.** This is NOT a
capture-environment defect, and the headless 12/255 and the device 13/255 are the same fault.

### ⛔ AND THE TICKET'S OWN FRAMING NEEDS ONE CORRECTION BEFORE ANYONE DIFFS TWO CONSTRUCTORS

The bright 254/255 plate in the drawer frame **IS `_chromeClose` — the same GameObject**, not a
second CLOSE with its own construction path. Proof, all from the two frames plus source:

- The crops are the same control: same plate art, same gold perimeter rect, same word `CLOSE`, same
  bottom-centre seat. A **x12 brightness boost of the hub crop reproduces the drawer crop intact** —
  nothing is missing, mis-coloured, mis-seated or clipped; the whole subtree is attenuated.
- The per-pixel ratio hub/drawer over that box **caps at 0.0512** (= 13/254) — a *multiplicative*
  attenuation of roughly 0.05, not a lost label and not a flattening overlay.
- `ApplyScreenVisibility` gates it on `_hubShowing` **alone** (`ManageScreenPanel.cs:1836`), and the
  drawer opens FROM the hub — so it is on screen in both frames.
- The drawer's own close is a **different control**: the `"X"` built into `_drawerHeader` at
  `ManageScreenPanel.cs:3108`, top-right, `ClosePx = 112`.

So the differential is **hub-state vs drawer-open-state of ONE control**, not two constructors.
A lane that diffs the hub CLOSE against the drawer CLOSE will diff two identical paths.

## 2. WHY NOTHING CAUGHT IT — the part worth reading twice

`ManageScreenPanel.cs:1362-1375` does all of this, unconditionally, on the hub:

```
_chromeClose.interactable = true;
closeLabel.text  = "CLOSE";
closeLabel.color = ElarionUi.Parchment;
ElarionUiKit.GoldPerimeter((RectTransform)_chromeClose.transform);
FlowTrace.Step("Manage", "MANAGE_HUB_CLOSE the shared CLOSE is live and legible - ... it was never
    disabled, only unreadable");
```

`ManageMockupConformanceRegression`'s `[chrome-close-is-live]` case pins **the presence of that source
text** and is green inside `MANAGE_MOCKUP_OK 10 cases` on `Builds/wave5-reg1` (07:25,
`REGRESSION_OK 494/494 suites`).

> ⛔ **A SOURCE LINT CANNOT SEE LUMINANCE.** The suite says so about itself in its own header
> (`ManageMockupConformanceRegression.cs:11-17`): *"an EditMode suite cannot stand one up headless. The
> PICTURE is judged by the capture loop."* So the code claims the control is legible, the lint confirms
> the code claims it, and **nothing in the chain ever looks at a pixel.** That is the defect this ticket
> closes as much as the dark button is.

## 3. ⚠ WHAT IS **NOT** PROVEN — do not open this as a device regression

**WO-1597** (`WORK_ORDER_1597_manage_hub_heart_chip_ghost_close_and_cards_that_do_not_fill_the_screen.md`)
covers this exact surface and reads `**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated
2026-09-09T02:10:48, build 2026.09.08.361259)`. **That close was on a DEVICE felt-test; the 12/255 above
is from a HEADLESS batchmode capture.** Both can be true — batchmode can render that control
differently. **No device frame of the Manage hub exists at or after build 363660**; the only Manage
device frame in the repo is `Builds/device-frames/2026-09-10_0033_manage_363195.png`, which is
**PORTRAIT** (out of scope under the owner's 2026-09-10 LANDSCAPE-ONLY ruling) and from build 363195.

**THE DISCRIMINATOR, and it is one command:** a **landscape** `adb screencap` of the Manage hub on a
build **>= 363660**, measured the same way (brightest pixel inside the `CLOSE` plate vs the `BUILD` card
label in the same frame). **Take it FIRST.** It splits the ticket cleanly:
- device also dark → a real render defect, fix the paint;
- device fine, headless dark → a **capture-environment** defect, and the fix is the oracle plus whatever
  the capture path does differently (font atlas, canvas material, a `CanvasGroup.alpha`, a sprite that
  resolves on device and misses headless).

⛔ **Do not "fix" the colour before that frame exists.** Under CLAUDE.md §12 the edit has not been earned
until captured data names the dead step, and there are two live candidate causes here, not one.

## 4. FILES TO EDIT

| File | Change |
|---|---|
| `Assets/Editor/Regression/ManageMockupConformanceRegression.cs` | ADD a **measured** case beside `[chrome-close-is-live]` — see §5. Do not weaken or delete the existing source case; the two defend different things. |
| `Assets/Editor/UICaptureLaunch.cs` | If the oracle must read the captured PNG, the read belongs beside the existing capture reporters (`ReportFidelity` / `ReportGeometry` / `ReportTouchOracle`, `:8735-8742`), emitting its own distinct marker. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` | ONLY once §3's device frame has named the cause. Touch `:1362-1375` and nothing else on this ticket. |
| `Assets/Editor/Regression/DataRegression.cs` | Registration line only, if a new suite is added. |

## 5. ACCEPTANCE

1. ✅ **DONE — see §1A.** Device landscape frame at build 363660 captured and measured; CLOSE plate
   13/255 vs BUILD label 172/255 in the same frame; **branch A, a real render defect.**
2. A **MEASURED** oracle exists that fails when the hub `CLOSE` is unreadable: it reads the actual
   rendered luminance (captured PNG, or a laid-out tree with a real font), compares the CLOSE plate
   against a reference glyph in the same frame, and **names both numbers in its failure text**.
   ⛔ It must be judged in **Rec.709 luma, not hue** — the owner is red/green colourblind and
   `ManageScreenPanel.cs:1360-1361` already states luminance is the channel here.
3. That oracle is proven **RED-first**: it fails on today's 07:57 frames (or on a fixture reproducing
   12/255) and goes green only after the fix. A case that has never failed proves nothing.
4. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.
5. A fresh capture is taken and **the PNGs are opened**; the CLOSE plate reads legibly at all three
   landscape aspects (1920x1080, 2340x1080, 2670x1200).
6. Per WO-1566 §2.0 this row cannot reach DONE on headless evidence: `**Status:**` goes to
   `AWAITING OWNER MATCH` until the owner judges a device frame.

## 6. WHAT NOT TO TOUCH

- ⛔ **The top-right constant `X`.** `CheckConstantExit` and `[chrome-close-on-hub-only]` defend **two
  different controls with two different fields** and the suite says in `:59-67` that anyone who
  "unifies" them re-opens one of two defects. `_chromeClose` is hub-only; the `X` is on every screen.
- ⛔ The `[chrome-close-is-live]` source case — add beside it, never replace it.
- ⛔ Anything in `ManageWorkspacePanel.cs`. `ManageDumbViewRegression` pins that the renderer holds none
  of canon 9's 16 forbidden shapes and is not a MonoBehaviour.
- ⛔ The hub card layout, the HEART chip, and the card art wells — all green
  (`[hub-cards-fill-the-well]`, `[hub-heart-chip-verb]`, `[hub-art-standins-exist]`).
- ⛔ Do not reopen or edit WO-1597. It closed on its own evidence; this is a new frame.

---

## 7. LANE RECORD — MANAGE-CHROME, 2026-09-10. INSTRUMENTED, **NOT FIXED**.

Per CLAUDE.md §12 the edit that changes a pixel has **not** been earned yet: two live candidates
remain and the read-back that separates them has not been RUN (this lane holds no Unity).

### 7.1 What landed

| File | Change |
|---|---|
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` | **INSTRUMENT ONLY.** `TraceHubCloseReadback(string when)` + helpers (`DescribeCloseGraphic`, `DescribeCloseOccluders`, `CollectGraphicsPreOrder`, `WorldAabb`, `PathFromCanvas`) and two call sites: end of `ApplyScreenVisibility` (state `hub`) and end of `ApplyDrawerPlacement` (state `drawer-open`). **No paint, colour, layout or visibility line was touched.** |
| `Assets/Editor/UICaptureLaunch.cs` | **SEPARATE, DELIMITED EDIT** (`===== WO-1648 LUMINANCE ORACLE - BEGIN/END =====`, two blocks): a pixel probe in `RenderCanvasToPng` right after `tex.Apply(false)`, plus `ProbeCloseLuminance` / `TryMeasureLuma` / `ReportLumaOracle` / `ResetLumaOracle`, wired at the Manage-flow entry point (§4's site) only. |

Marker: **`UI_LUMA_ORACLE_OK`** / **`UI_LUMA_FAIL`**. It names BOTH numbers in every finding
(plate peak luma vs the same frame's `BUILD` reference glyph, Rec.709 luma, never hue) and
**deliberately does not gate `MANAGE_FLOW_MAP_OK`** until it has been proven red once — the
suppression trap `UICaptureLaunch.cs` §5 records in its own words.

### 7.2 The read-back the lead must run, and what each answer means

Run the Manage capture / a hub+queue AutoPilot pass and grep the fresh log for
**`MANAGE_HUB_CLOSE_READBACK`**. Up to three lines per state appear, `[hub]` and `[drawer-open]`.

- ⚠ **READ THE LAST LINE OF EACH STATE.** The earliest one can still be a pre-settle frame.
- The budget is only spent on a frame where the plate's rect has real area **and** (on the hub)
  `_launcherHost` exists, so a line saying `OVERLAPPING: NONE` can never mean "candidate A's
  graphic had not been built yet".
- ⚠ **The luma oracle's FIRST run on the 07:57 frames is EXPECTED to print `UI_LUMA_FAIL` and
  `[luma-oracle] CLOSE UNREADABLE ...` as `LogError`. That is acceptance 3's RED-first proof, not
  a harness defect** — the glyph oracle was seeded red the same way and the run survives it.

| Field on the `[hub]` line | Reading | Cause |
|---|---|---|
| `inhAlpha` (plate and label) ~**1.0** on both lines | candidate **B is DEAD** | read `OVERLAPPING:` — the graphic it names is the occluder |
| `inhAlpha` **below ~0.1** on `[hub]` and ~1.0 on `[drawer-open]` | candidate **A is noise** | an inherited CanvasGroup/tint is the cause; find its owner |
| `OVERLAPPING: NONE` **and** `inhAlpha` ~1.0 | neither candidate | report it — the fault is outside both models (material/shader/atlas) |

**Candidate A, named at source** (static reading only — this LOCATES, it does not CONCLUDE):
`ManageCategoryLauncher` (`BuildLauncher`, `ManageScreenPanel.cs:2002-2008`) carries an `Image` at
`(0.012, 0.014, 0.018, alpha 0.995)`, is parented to `operationalWell.parent` with the **well's own**
anchors/offsets — and the geometry pass (`:1417-1436`) drops the well's floor onto the CLOSE band on
every screen, the hub re-reserving that band *inside* this host as empty space. It is built **after**
the kit chrome's close, so it draws over it, and it flips exactly the right way:
`SetActive(_hubShowing && !_queueDrawerOpen)` at `:1798` — present on the dark frame, gone on the
bright one. ⚠ **Consistent with, not proof of**: one 0.995 layer transmits 0.005 and the measured
ratio is ~0.05; blend space (linear vs gamma) and 8-bit rounding put both inside the plausible band.
The `OVERLAPPING:` list names the graphic or it does not exist.

**Candidate B:** an inherited `CanvasGroup`/tint near 0.05 on the hub, 1.0 with the drawer open.
`grep -rn "CanvasGroup|SetAlpha|inheritedAlpha"` over `_Modules/Village/UI/Manage/` and
`_Modules/Core/UI/` returned **no writer inside either file**, which lowers B but does not kill it —
`GetInheritedAlpha()` on the read-back line is what kills it.

### 7.2b ✅ THE READ-BACK RAN, AND IT NAMED THE CAUSE — `Builds/wave5-manageflow3` (08:44)

Read with `tr -d '\000' < Builds/wave5-manageflow3 | grep -a MANAGE_HUB_CLOSE_READBACK` (the log is
NUL-padded).

**`[hub]`:**
```
plate='.../CloseButton' color=1,1,1,a1 crColor=1,1,1,a1 inhAlpha=1 drawn active enabled
      sibling=6 mat=Default UI Material
label  color=0.953,0.918,0.827,a1 crColor=1,1,1,a1 inhAlpha=1
OVERLAPPING: '.../CloseButton/GoldTop|GoldBottom|GoldLeft|GoldRight' color=0.831,0.686,0.216,a0.95
             inhAlpha=1
           ; 'ManageScreenUI/ObsidianPanel/PanelContent/ManageCategoryLauncher'
             color=0.012,0.014,0.018,a0.995 inhAlpha=1
```
**`[drawer-open]`:** the same plate reads **`drawn INACTIVE`**.

**Verdict — candidate A, and it is now proven, not inferred:**
- **B is DEAD.** `inhAlpha=1` on the plate, the label and every overlapping graphic. Nothing faded
  the control; every alpha/tint/CanvasGroup theory dies on this one field.
- **A is NAMED.** `ManageCategoryLauncher`'s own backing `Image` — `(0.012, 0.014, 0.018, α 0.995)` —
  is drawn **after** the kit chrome's close and **overlaps its rect**. A 0.995-alpha near-black plate
  over a button is a button the player cannot see. It copied the operational well's rect verbatim,
  and the geometry pass deliberately drops that well's floor **onto** the close band.
- The oracle went **RED on its first run, as designed** — acceptance 3 is met:
  `UI_LUMA_FAIL x2 over 2 measured CLOSE plate(s)`;
  `[luma-oracle] ManageFlow_BUILD_hub_2670x1200: close max=14/255 mean=0.67 rect 1115..1555 x 86..242;
  reference 'BUILD' max=174.3/255 ... floor=61` (plus the hubheart frame).
- Same run: `COMPILE_GATE_OK` (`Builds/wave5-compile6`), `MANAGE_FLOW_MAP_OK 20`, `UI_GLYPH_OK 20/20`.

#### ⛔ AND §1A's REFRAME IS **FALSIFIED BY THIS SAME READ-BACK** — correcting it here, on the record

§1A argued the bright 254/255 plate in the drawer frame **is** `_chromeClose`. The `[drawer-open]`
line says that control is **`INACTIVE`** in that state, so it cannot be what those pixels are: the
bright plate at the same screen position is a **different control**. The visual-identity argument
(same art, same seat, x12 boost matches) was strong and still wrong — the kit builds every close the
same way, so identical art proves a shared *factory*, never a shared *instance*. **The read-back
outranks the crop**, which is exactly what §12 exists to enforce, and this lane's own reframe is the
worked example. What survives from §1A is the part that was measured: the hub plate is dark, the
device agrees with headless, and it is branch A.

### 7.2c THE FIX — the rect, never the paint

`ManageScreenPanel.cs` only, two paired lines in `BuildLauncher`:

1. **`_launcherHost`'s floor is raised** by `Mathf.Max(HubCloseBandPx, _hubCloseReservePx) +
   HubBandGapPx`, so the host's backing plate physically stops above the CLOSE band.
2. **`bottomF` becomes `0f`** — the card band is now the whole host. The reservation *moved* from the
   grid's fractions into the host's rect; it is not removed and it is not applied twice.
   ⛔ Restoring `(closeReserve + HubBandGapPx) / hostH` while the host inset stands reserves the band
   **twice** and hands back the half-height card row WO-1597 measured.

⛔ Deliberately **not**: no alpha lowered (the hub would go translucent over the town — the thing
`ManageBodyFill` exists to prevent), and **no sibling re-ordering** (draw order hides the symptom; a
near-black plate still drawn through a band it does not own would swallow the next control seated
there in exactly the same way).

**Green = `UI_LUMA_ORACLE_OK` on the next flow-map run**, where the RED above is today.

⛔ **AND IF IT DOES NOT GO GREEN, DO NOT TUNE `LumaCloseFloorFraction`.** The floor is 0.35 of the
frame's own reference glyph (174.3 → 61) and the oracle takes the PEAK inside the rect, so even if
the Gray plate tier renders dark by design the Parchment label alone clears it comfortably — the
uncovered control measures 254/255 on the device drawer frame. A persistent `UI_LUMA_FAIL` at, say,
40/255 means **a second occluder or a different fault**, not a threshold that is too strict. Re-run
`MANAGE_HUB_CLOSE_READBACK` and read the `OVERLAPPING:` list again; loosening the constant would
delete the only detector this ticket built.

### 7.3 Still open on this ticket

- Acceptance **3 (RED-first proof)** — ✅ **MET**, see §7.2b: `UI_LUMA_FAIL x2` on
  `Builds/wave5-manageflow3`, close 14/255 vs reference 174.3/255, floor 61. It has failed on
  purpose; it may go green only after the fix.
- Acceptance **4 / 5** are the lead's: `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on FRESH logs,
  then a fresh capture with the PNGs OPENED at all three landscape aspects. The luma marker flipping
  to `UI_LUMA_ORACLE_OK` is necessary but **not sufficient** — §5.5 wants human eyes on the frames.
- Acceptance **6** — this row cannot reach DONE on headless evidence; it goes to
  `AWAITING OWNER MATCH` once the gate is green.
- **§4 row 1 was deliberately NOT taken.** No measured case was added to
  `ManageMockupConformanceRegression.cs`; the measurement went to §4 row 2 (the capture path)
  instead, because that suite's own header (`:11-17`) says an EditMode suite cannot stand a
  rendered panel up and that the PICTURE is judged by the capture loop. A "measured" case that
  cannot see a pixel would be a second source lint beside the one this ticket exists to indict.
  `[chrome-close-is-live]` is untouched, as §6 requires.
- `Assets/Editor/Regression/DataRegression.cs` **untouched** — §4 row 4 is registration-only and no
  new suite was added.
- ⛔ No Unity run, no gate, no commit from this lane, and the main tree's uncommitted WO-1651
  one-liner in `FitSingleLine(refund, QueueStateFontFloorPx, ...)` was not touched (this lane
  worked in an isolated worktree at `736b6b4b9`).
- Acceptance **4, 5, 6** untouched — no gate, no capture, no commit from this lane.
- The fix itself is deliberately **not written**. One read-back line decides which of §7.2's rows it
  is, and the two rows have different fixes in different places.
