# WO-1645 - Nothing renders the IN-RAID HUD in any gate, which is why four visible defects shipped green

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (lane RAID-HUD, number **PRE-ASSIGNED by the lead** - `CLI_LANES_WO_NUMBERS.md`
was **not** edited by this lane, per the lead's instruction. The banner bump is the lead's.)
**Silo / Lane:** EDITOR - `Assets/Editor/UICaptureLaunch.cs` only. No runtime file changes.
**Severity:** P2 structural. Not a player-facing defect; it is the **absence of the gate** that let four
player-facing defects reach the owner's hands.
**Type:** EXISTING system, a coverage hole. **Raised out of WO-1639's hand-back** (WO-1639 sec.1e
scoped it OUT deliberately and asked the lead to mint it - this is that ticket).
**Depends on:** nothing. **Blocks:** nothing. It can run in parallel with any runtime lane.

---

## 1. The hole, proved at source (every path opened 2026-09-10 in this worktree, at `a96bfe332`)

### 1a. The capture harness shoots the PRE-raid screens and stops there

`Assets/Editor/UICaptureLaunch.cs`, inside `RunCaptureHeadless` (`:548`), the raid block is exactly
two lines:

```
:621            count += CaptureRaidSelection();     // the grid that was hard-refusing to open
:622            count += CaptureRaidDeploy();        // the pre-raid deploy screen (never shot before)
```

- `CaptureRaidSelection` (`:6718`) -> `CaptureRaidSelectionOnce` (`:6724`) opens `RaidSelectionScreen`.
- `CaptureRaidDeploy` (`:6810`) -> `CaptureRaidDeployOnce` (`:6816`) opens **`RaidDeployScreen`** - the
  **STAGING** screen (`Assets/_Modules/Village/Hero/RaidDeployScreen.cs`), i.e. the thing you tap
  BEFORE the raid starts. Its own log line at `:6869-6871` says so.

**Both of those are pre-raid.** The surfaces the player looks at DURING a raid are
`RaidHudController` (the right-hand readout column) and `RaidDeployController` (the bottom command
bar + the status line), both in `Assets/_Modules/Village/Troops/`.

> ⛔ **THE MEASUREMENT: `grep -c "RaidHudController\|RaidDeployController" Assets/Editor/UICaptureLaunch.cs`
> returns `0`.** Neither type is named anywhere in the harness. There is **no `RaidBase_*` scene
> capture, no `RaidHudController` capture, no `RaidDeployController` capture and no compass capture.**

### 1b. And the layout oracle has never heard of them either

> ⛔ **`grep -c -i "raid\|deploy\|readout\|compass" Assets/_Modules/Core/UI/LayoutOracle.cs` returns `0`.**

So the readout, the command bar, the status line, the compass and the in-world objective marker are in
**neither** automated surface. The only coverage they have ever had is **source-text and band
arithmetic** - `RaidHudThumbBandRegression`, `RaidArenaShapeRegression`, `RaidScoringRegression`,
`RaidDeployUiRegression`. Those suites are good and they stay; what none of them does is **build the
thing and measure pixels.**

### 1c. The four defects that shipped green because of it (WO-1639 sec.1, all measured on build 363529)

Every one of these was visible in a device frame and invisible to every gate:

| | defect | how it presented | why source-text coverage could not see it |
|---|---|---|---|
| A | the readout plate at alpha 0.42 | **nothing on the panel reached 3:1.** Timer 2.67:1, SPIRE 1.98:1, Razed 1.72:1, unlit stars 1.55:1, **Troops 1.12:1** - and the arena's boundary pillars were visible THROUGH the plate | a colour literal is legal source text; contrast is a rendered-pixel property |
| B | `DEPLOY ...` | ellipsised in **all four** arena frames - a 10-char label in a 0.130-of-bar face | the literal `"Deploy All"` is present and pinned, so `RaidDeployUiRegression:411-415` passed. The pin proves the STRING exists, never that it FITS |
| C | `HERO DOWN - your army fights on` buried | rendered UNDER the deploy bar (kit toast canvas at sortingOrder 720 vs the HUD's 30000) | two canvases' sorting orders live in two different files; no source-text rule compares them |
| D | the oversized objective marker | spans from the top edge past the horizon at close camera range | world-space, size driven by projection - it does not exist in any authored rect |

**One hole, four symptoms.** WO-1639 fixed A, B and C and instrumented D. **This ticket closes the
hole**, so the next one is caught by a marker instead of by the owner's eyes - which is the whole
point of CLAUDE.md sec.14 (*the owner is NEVER the bug detector*).

### 1d. Same family, different surface - and this is the second time

WO-1636's glyph oracle did not catch `DEPLOY ...` or `HERO DOW...` for exactly this reason: the glyph
oracle runs over the captured panel builds, and the in-raid HUD is not one of them. A perfectly good
oracle, pointed at a set that does not include the defective screen.

---

## 2. What already exists, so this is small

**The harness does all the hard work the moment a canvas is handed to it.**
`RenderCanvasToPng` (`:5712`) settles the layout and then calls **`AuditGeometry`** (`:5775` ->
`:5893`), which increments `_geoCanvasesChecked`, runs the numeric layout assertions, the WO-1060
touch half, **and the WO-1630 glyph oracle Assert C** (`_glyphPanelsChecked++` at `:6024`). It also
offers `_settledProbe` (`:5784-5788`) - the ONE point in the run where a rect read is in kit reference
px - for any panel-specific measurement.

**So a new capture inherits geometry + touch + glyph coverage for free.** It needs to do exactly one
thing: **construct the real canvas headlessly and hand the `GameObject` to `RenderCanvasToPng`.**

`ForEachTarget(panelName, body)` (`:253-256`) already runs a body once per size against
`LandscapeTargets` (`:215-220`), which is:

```
1920x1080   // desktop / reference landscape
2340x1080   // common tall-phone landscape (NOT the Seeker)
2670x1200   // THE SEEKER'S REAL SURFACE  <- the size every WO-1639 frame was measured at
```

---

## 3. The fix - one new capture, two panels

### 3a. Shape

Add, next to the two existing raid captures (`:621-622`):

```
count += CaptureRaidHud();        // the LIVE in-raid readout column
count += CaptureRaidDeployHud();  // the LIVE in-raid command bar + status line
```

Each follows the **established pattern of `CaptureRaidDeployOnce`** (`:6816-6891`) exactly - temp
`EventSystem`, build, reach the private canvas field, `RenderCanvasToPng`, tear down in `finally`:

1. `ForEachTarget("RaidHud", ...)` / `ForEachTarget("RaidDeployHud", ...)` so all **three**
   `LandscapeTargets` are built, not rescaled.
2. Stand up the component on a temp `GameObject` and invoke its private `BuildHud()`. Both are
   `MonoBehaviour`s that build in `Start()`, which edit mode does not run - so drive `BuildHud`
   directly. **Reflection into a private member is the harness's own established idiom here**, not a
   new pattern: `CaptureRaidDeployOnce` already does `GetPrivateGameObject(screen, "_ui")` (`:6862`,
   helper at `:9522`).
3. Reach the built canvas through the same `GetPrivateGameObject(controller, "_ui")` - the field is
   `private GameObject _ui` on both (`RaidHudController.cs:53`, `RaidDeployController.cs:78`).
4. `RenderCanvasToPng(canvasGo, OutDir + "RaidHud_" + target.Tag + ".png", target.W, target.H)` and
   the same for `RaidDeployHud_`.
5. Tear down in `finally` - destroy the canvas, the controller GameObject and the temp EventSystem.

### 3b. What each panel will contain headlessly, and say so in the log

**Do not fabricate a fixture. Log the state instead**, the way `CaptureRaidDeployOnce:6869-6871`
already logs its zero-army caveat:

- **`RaidHudController`**: `BuildHud` ends with `Refresh()`, which returns immediately when
  `RaidScoring.Instance` is null (`RaidHudController.cs`, top of `Refresh`). So the panel shoots its
  AUTHORED placeholder state: `3:00` / `SPIRE 100%` / `Razed 0%` / three diamonds + `0/3` /
  `Troops 0/0`. ⭐ **That is EXACTLY the state WO-1639 sec.1a measured on the device**, so this
  capture is directly comparable to the frames the defect was found in - a genuine advantage, not a
  compromise. Log it as the no-scorer state.
- **`RaidDeployController`**: `BuildTrayTiles` -> `Army()` -> `GameStateService.Instance` is null in
  edit mode, so `defIds` is empty and the tray renders its `"No troops to deploy - train at the
  Barracks first."` label. Log that this is the EMPTY-tray layout and that a populated tray's tile
  widths are **not** proven by this shot. (Sec.5 records the follow-up.)

### 3c. The one panel-specific probe worth adding, via `_settledProbe`

The three bar faces (`Deploy All` / `Rally` / `Retreat`) are the WO-1639 Defect B class, and the glyph
oracle already answers "did every printable character draw?". Set `_settledProbe` on the
`RaidDeployHud` capture to additionally record, per face, the resolved face width in reference px, the
seated `fontSize`, and `isTextTruncated` - the same numbers `RaidDeployController`'s own runtime
`[wo1639-face]` trace prints, so the headless read and the device read are directly comparable.
Clear it in `finally`. **This is optional to the acceptance below; the glyph oracle alone reds a
`DEPLOY ...`.**

---

## 4. Acceptance

1. **`Builds/ui-capture/RaidHud_1920x1080.png`, `_2340x1080.png`, `_2670x1200.png`** and the same
   three for **`RaidDeployHud_`** exist, are non-blank, and are **OPENED and pasted** in the RESULT
   (owner's standing rule: images verify anything viewable).
2. **The run's `UI_CAPTURE_OK <count>` rises by exactly 6**, and its `UI_CAPTURE_STAMP` line
   (`:766-813`) shows `canvases`, `glyphPanels` and `touchPanels` each rising by 6. A count that does
   not move by 6 means a body returned 0 through one of its `LogWarning` skip paths - that is a FAIL,
   not a pass with a note.
3. **`UI_GLYPH_OK <clean>/<checked>` covers the two new panels**, with `labels=<n>` non-zero for them.
   ⛔ The header at `:44-50` is explicit that `labels=0` over any number of panels is a **FAIL** -
   zero findings from zero measurements reads exactly like a clean panel and must not.
4. **`UI_GEOMETRY_OK <n> canvases`** stays green with the six added, or names its failures.
5. **Judge by the MARKERS on a FRESH log, never the exit code** (CLAUDE.md sec.8 / memory
   `gates-report-success-without-proving-it`).
6. **The RED-FIRST proof, and it is not optional.** `LayoutOracle.cs:15-20` states the rule in its own
   words: *"an oracle never seen red is not evidence"* - `UiTouchClampRegression` exists precisely so
   the oracle is watched going red against **synthetic canvases whose defects are authored on
   purpose**. So: temporarily reinstate WO-1639's pre-fix values (the readout plate back to
   `new Color(0.04f, 0.035f, 0.03f, 0.42f)`, the `Deploy All` face back to `0.565..0.695`), run the
   capture, and **paste the FAILING glyph line for `DEPLOY ...`.** Then revert. **If the pre-fix
   geometry does NOT red this oracle, the capture is decoration and the ticket is not done** - say so
   and hand back rather than shipping a green that proves nothing.
7. **Brace + NUL on the touched file**, including `python tools/gate_brace.py` (CLAUDE.md sec.1 - the
   gate counts differently from the raw one-liner).

---

## 5. What NOT to touch

- ⛔ **No runtime file.** This ticket is `Assets/Editor/UICaptureLaunch.cs`. If a capture cannot reach
  a controller without a runtime change, **say so and hand back** - do not widen a private member's
  accessibility to suit a test. Reflection through the harness's existing `GetPrivateGameObject`
  (`:9522`) is the sanctioned route and needs nothing from the runtime.
- ⛔ **No new `LayoutOracle` RULE.** Adding a rule to `LayoutOracle.cs` requires the red-first proof
  against a synthetic authored-defect canvas (`:15-20`), which is its own ticket. This ticket only
  points the EXISTING audits at two more canvases - it adds no assertion of its own beyond the
  optional `_settledProbe` read in sec.3c.
- ⛔ **Do not "fix" what the capture reveals.** If the new PNGs surface a fifth defect, that is a new
  ticket. This lane delivers the capture.
- ⛔ **Do not touch `RaidBaseDresser` / `RaidBaseGenerator` or any `RaidBase_*` scene.** The capture
  builds the two HUD canvases in isolation; it does not need a raid scene and must not load one.
- The **staging** screen (`RaidDeployScreen.cs`) is **WO-1640's** and is already captured at `:622`.
  Leave both alone.
- **Do not commit. Do not push.** Hand the diff back (CLAUDE.md sec.11).

---

## 6. Residual this ticket does NOT close - a populated tray

Sec.3b's caveat is real: headless, both panels shoot an empty-state. The readout's empty state is the
one that was defective and is therefore the right target; the deploy bar's is not. **A populated-tray
shot needs a `GameStateService` fixture and that is a different problem** (the same one
`CaptureRaidDeployOnce:6869-6871` already carries for the staging screen). **Record it in the RESULT;
do not build it here.** WO-1639's runtime `[wo1639-face]` trace covers the populated case on a real
device in the meantime.

---

## 7. The kit-toast item - WHY IT NEEDS ITS OWN TICKET

The lead asked whether *"the kit toast's label is 24 px, below `ElarionUiKit.FontFloor` (30)"* belongs
here as a second item. **It does not, and the reason is the lane, not the size of the change.**

- **This ticket is EDITOR-ONLY** (`Assets/Editor/UICaptureLaunch.cs`) and ships no runtime bytes.
- **The toast is a KIT change** (`Assets/_Modules/Core/UI/ElarionUiKitConformance.cs`,
  `ShowToast`/`ToastCard` around `:393-430`) that **repaints every toast in the game** - shop, save,
  equip, bank, raid. It is the widest blast radius a UI change has in this repo, and CLAUDE.md sec.7
  is emphatic that *"lowering a kit constant or re-defining a palette colour so one screen reads is
  the inverse of this fix"* - the same logic applies to raising one.
- Merged, they would be **one ticket that cannot be committed as one lane** (CLAUDE.md sec.11:
  one lane per commit, explicit paths). The lead would have to split it at commit time anyway.

**Ready to mint, so the lead can copy it straight out (number is the lead's to assign):**

> **The kit's transient toast label is below the mobile font floor.**
> `ElarionUiKit.ShowToast` builds a `ToastCard` whose label is the legacy 24 px Text, while
> `ElarionUiKit.FontFloor` is **30** and `FontHardFloor` is **20** (`ElarionUiKitObsidian.cs:3033`,
> `:3044`). Every `FitSingleLine`/`FitBlock` caller in the game is held to 30; the toast - the one
> widget that carries urgent transient copy, including
> `EndStateVM.HeroDownArmyFightsOn` - is not held to anything. **Evidence:** WO-1639 Defect C, where
> that toast is the delivery path for the single most important sentence the raid HUD shows.
> **Scope:** the kit only; every caller inherits it. **Watch:** the card is `480x76` reference px by
> default and its label wraps at `VerticalWrapMode.Overflow`, so raising the font without raising the
> card paints a third line OUTSIDE the plate (the kit's own `cardWidth` doc-comment says so).
> **Acceptance:** a capture of at least three existing toast callers at all three `LandscapeTargets`
> before and after, plus the glyph oracle. **Related:** the same kit method also hardcodes the card's
> seat at `anchoredPosition (0, 220)`, which WO-1639 sec.4 records as a residual - worth folding into
> the same kit lane.

---

## 8. Board

This ticket is minted READY and **unassigned**. Its number was pre-assigned by the lead and
`CLI_LANES_WO_NUMBERS.md` was deliberately **not** edited by this lane. Whoever takes it owns flipping
this file's `**Status:**` line and writing
`WorkOrders/WORK_ORDER_1645_no_gate_renders_the_in_raid_hud.RESULT.md`; the lead regenerates
`BOARD.html`.
