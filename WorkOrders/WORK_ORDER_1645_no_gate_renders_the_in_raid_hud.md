# WO-1645 - Nothing renders the IN-RAID HUD in any gate, which is why four visible defects shipped green

**Status:** IMPLEMENTED - awaiting WO-1646 for UI_GEOMETRY_OK
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

---

## 9. HAND-BACK — RAID-CAPTURE lane, 2026-09-10 (worktree `agent-a64cca841b3bd61c1`, HEAD `0a7edc6b1`)

**Status stays `READY TO IMPLEMENT` deliberately.** The code is written; what makes it DONE is a Unity
run this lane **cannot** perform (no Unity on this seat). Acceptance §4.1-4.6 are all run-produced
artefacts. Flip to DONE only when §9.3 has produced its markers on a FRESH log.

### 9.1 The edit — one file, additive only

`Assets/Editor/UICaptureLaunch.cs`, **+233 lines, 0 deletions** (`git diff --stat`, this worktree).

| lines | what |
|---|---|
| `:623-627` | the registration. Comment at `:623-625`, then `count += CaptureRaidHud();` (`:626`) and `count += CaptureRaidDeployHud();` (`:627`), immediately after the existing `CaptureRaidDeploy()` at `:622` |
| `:6794-6822` | the doc block: why the hole existed, why `RaidTestFlagScope` is NOT used, why no raid scene is loaded |
| `:6823-6826` | `CaptureRaidHud()` -> `ForEachTarget("RaidHud", ...)` — all three `LandscapeTargets`, each a real build |
| `:6828-6886` | `CaptureRaidHudOnce` — temp `EventSystem`, `AddComponent<RaidHudController>()`, `InvokePrivate(hud, "BuildHud")`, `GetPrivateGameObject(hud, "_ui")`, no-scorer log, `RenderCanvasToPng(... "RaidHud_" + target.Tag + ".png")`, canvas-first teardown in `finally` |
| `:6888-6891` | `CaptureRaidDeployHud()` -> `ForEachTarget("RaidDeployHud", ...)` |
| `:6893-6960` | `CaptureRaidDeployHudOnce` — same shape, plus `_settledProbe = RaidFaceProbe` before the render and `_settledProbe = null` in `finally` |
| `:6961-7020` | `RaidFaceProbe` — §3c's per-face read. **LOG ONLY**, feeds no marker (§5) |

**No runtime file was touched.** `git diff --stat` names exactly one path. The private members are
reached through the harness's existing `InvokePrivate` (`:9641`) / `GetPrivateGameObject` (`:9634`) —
nothing's accessibility was widened.

Patch for the lead: **`WorkOrders/WORK_ORDER_1645_uicapturelaunch.patch`** (251 lines,
`git diff HEAD -- Assets/Editor/UICaptureLaunch.cs`), plus the edited file in this worktree.

### 9.2 ⛔ HOW THE LEAD MERGES THIS — DO **NOT** COPY THE FILE WHOLE

The main tree carries an **uncommitted WO-1644 hunk** in `RunFrontDoorCaptureHeadless`
(`ResetGlyphOracle` / `ReportGlyphOracle`, ~`:2218`/`:2222` pre-merge). Copying this lane's whole
`UICaptureLaunch.cs` over the main tree **silently deletes it** — the exact failure memory
`git-apply-3way-drops-hunks-silently-copy-lane-files` records. Apply the **patch**, then, before
gating, prove BOTH survived in the main-tree file:

```
grep -c "CaptureRaidHud"   Assets/Editor/UICaptureLaunch.cs   # expect >= 3
grep -c "ResetGlyphOracle" Assets/Editor/UICaptureLaunch.cs   # expect unchanged from pre-merge
```

⚠ **The 1644 hunk's line numbers MOVE.** This lane inserts 5 lines at `:623` and 228 at `:6793`, so
every later line shifts. Diff by CONTENT, never by line number.

### 9.3 THE RED-FIRST PROCEDURE — the exact commands, and what each proves

⛔ **Do NOT hand-retype the pre-fix literals.** This worktree's HEAD (`0a7edc6b1` = `dev`) **IS** the
pre-1639 state — proved at source in this worktree, 2026-09-10:
`RaidHudController.cs:191` = `barImg.color = new Color(0.04f, 0.035f, 0.03f, 0.42f);` and
`RaidDeployController.cs:1704` = `new Vector2(0.565f, 0.18f), new Vector2(0.695f, 0.82f), DeployAll);`.
The WO-1639 edits exist only as **uncommitted changes in the lead's main tree**, so the revert is a
stash, not an edit:

```
# 1. RED: park the WO-1639 fixes, restoring the exact pre-fix values from HEAD
git stash push -- Assets/_Modules/Village/Troops/RaidHudController.cs \
                  Assets/_Modules/Village/Troops/RaidDeployController.cs
<run RunCaptureHeadless>            # expect UI_GLYPH_FAIL — see 9.4
# 2. GREEN: restore them
git stash pop
<run RunCaptureHeadless>            # expect UI_GLYPH_OK and the six PNGs
```

### 9.4 ⛔ WHAT THE RED-FIRST ACTUALLY PROVES — and the half of §4.6 that it CANNOT

§4.6 bundles Defect A (the 0.42 plate) with Defect B (the face band) as one red-first. **Only Defect B
can red anything, and the lead must know that before running it.** Read at source this session:

- `AuditGeometry` (`Assets/Editor/UICaptureLaunch.cs:5893-6060`) and
  `LayoutOracle.Audit` (`Assets/_Modules/Core/UI/LayoutOracle.cs:244-400`) contain **no contrast rule
  of any kind**. Every rule measures where a rect is, or whether glyphs drew. **Reverting the plate to
  alpha 0.42 will red NOTHING**, and that is not a defect in this capture — it is the honest limit.
  Defect A's coverage from this ticket is **the PNG, for eyes**, and nothing more.
- **Defect C (the buried `HERO DOWN` toast) is likewise NOT covered.** The kit toast is a separate
  canvas that `BuildHud` never creates, so it is not in either shot. Its ticket is §7's kit lane.
- **Defect D (the objective marker) is world-space** and not on either canvas.

**Defect B is the one that reds**, via Assert C. The expected line, as a template — the parts marked
`<...>` are **NOT PROVEN** from this seat (no Unity run):

```
[glyph-oracle] TEXT TRUNCATED [RaidDeployHud_2670x1200 @2670x1200] '<path/to/Btn>' ("Deploy All")
draws <n> of 9 printable glyphs. (x <..> .. y <..>) at font <f> [autosize 30..<FontBody>,
enabled=True] overflow=Ellipsis wrap=NoWrap isTextTruncated=<bool> (corroborating only -- the glyph
count is the assertion). ...
```
followed by the run marker `UI_GLYPH_FAIL x<n> NEW over <p> panels ...`
(`UICaptureLaunch.cs:6366` — that is the marker string; there is no other).

- **`9` IS proven**: `LayoutOracle.PrintableCount("Deploy All", richText)` counts non-whitespace =
  `Deploy`(6) + `All`(3) = 9 (`LayoutOracle.cs:378-397`).
- **Proven the label is even measurable by Assert C**: `ElarionUiKit.Button` (`ElarionUiKit.cs:1601`)
  routes to `BuildObsidianButton` (`ElarionUiKitObsidian.cs:617`), which calls `FitSingleLine`
  (`:644`), which sets `overflowMode = Ellipsis` (`ElarionUiKitObsidian.cs:3065`). Assert C skips
  only `Overflow` (`LayoutOracle.cs:288`), so this label is inside the rule.
- **NOT proven**: `<n>`, the widget path (it depends on which `BuildObsidianButton` mode resolves
  headlessly — sprite-catalog presence was not measured from here), whether the kit uppercases the
  caption, and **whether 1920x1080 / 2340x1080 also red**. Only 2670x1200 matches the device the
  WO-1639 measurement came from. **One red at 2670x1200 satisfies §4.6.**
- ⛔ **If the pre-1639 face band reds NOTHING at any of the three targets, the capture is decoration
  and this ticket is NOT done** (§4.6's own words). Hand it back rather than shipping a green.

### 9.5 What the GREEN run may also red — these are NEW TICKETS, not scope (§5)

At the pre-fix state, and possibly after, two more labels on these canvases may trip Assert C for
reasons unrelated to WO-1639:
- `Troops 0/0` (`RaidHudController.cs:263-264`) — its own comment records it was one rounding error
  from TMP culling the line at 0.130 of the panel.
- the empty-tray label `"No troops to deploy - train at the Barracks first."`
  (`RaidDeployController.cs:1736-1739`) — a 46-character sentence in 0.65 of the bar.

⛔ **Do NOT fix either from this lane** (§5: *"Do not 'fix' what the capture reveals"*). Mint them.

### 9.6 Zero-return traps that were checked at source (so the lead need not re-derive them)

Acceptance §4.2 makes a body returning 0 a FAIL, so each null/throw path was read, not assumed:

| trap | read at | verdict |
|---|---|---|
| canvas on a CHILD, so `RenderCanvasToPng:5716` skips | `ElarionUiKit.BuildModalCanvas` (`ElarionUiKit.cs:99-115`) | `Canvas` is on the **returned root** GO. Safe |
| `Start()` fires and double-builds | `[DisallowMultipleComponent]`, no `ExecuteAlways` on either class (`RaidHudController.cs:45-46`, `RaidDeployController.cs:53-54`) | `Start` never runs in edit mode. `BuildHud` is the only build |
| `BuildHud` NREs with no `GameStateService` | `Army()` (`RaidDeployController.cs:1935-1939`) nulls out into the empty-tray branch (`:1734-1740`); `RefreshTiles` (`:1864`) loops an empty `_tiles`; `RefreshRallyButton` (`:1912`) null-guards; `DeployBarBand`/`DeployStatusBand` (`:1623-1642`) are pure `HudLayoutBands` math | no NRE path found |
| edit-illegal runtime `Destroy` in `OnDestroy` | `RaidHudController.cs:126-129`, `RaidDeployController.cs:257-263` | canvas is destroyed FIRST so `_ui` reads dead; `_rallyFlag` is null (only built by a rally tap, `:898`); `TroopRally.Clear()` is `Point = null` (`TroopRally.cs:35`) |
| `ff.raidtest` needed | `grep -n raidtest` over both controllers | **no hits** — the scope is deliberately not used, and the code says so |

### 9.7 Brace + NUL gate (§4.7 / CLAUDE.md §1) — run in this worktree, 2026-09-10

```
$ python tools/gate_brace.py Assets/Editor/UICaptureLaunch.cs
GATE_BRACE_SUMMARY bad=0 of 1          (exit 0)

$ NUL bytes: 0
$ raw braces: 954 open / 954 close
$ lines: 9721
```
Both counts agree, so the CLAUDE.md §1 interpolated-string divergence does not bite here.

### 9.8 ⛔ NOT PROVEN by this lane, and it is the whole remainder

⚠ **§9.8 IS SUPERSEDED BY §10 — the runs happened. Kept for the record, not as current state.**

**No Unity ran.** Therefore: the six PNGs do not exist; `UI_CAPTURE_OK` was not observed to rise by 6;
`UI_CAPTURE_STAMP`'s `canvases`/`glyphPanels`/`touchPanels` were not observed; `UI_GLYPH_OK`'s
`labels=<n>` for the two new panels is unknown; and **the red-first has not been seen going red**. Every
one of acceptance §4.1-§4.6 is open. The code compiles by inspection only — **that is not a compile
proof**; `COMPILE_GATE_OK` is the lead's.

---

## 10. ACCEPTANCE, SETTLED AGAINST THE RUNS (RAID-CAPTURE lane, 2026-09-10)

Logs read by this lane under `Builds/` (NUL-padded; read with `tr -d '\000' < <log> | grep -a`).
Judged by the MARKER on a fresh log, never an exit code (CLAUDE.md §8).

| run | log | head | what it is |
|---|---|---|---|
| baseline | `Builds/wave5-capture1` | `0a7edc6b1` | pre-WO-1645 |
| **GREEN** | `Builds/wave5-capture2` | `ff42319de` | HEAD runtime files |
| **RED** | `Builds/wave5-capture3red` | `ff42319de` | pre-1639 controllers restored |
| compile | `Builds/wave5-compile2` | `ff42319de` | `COMPILE_GATE_OK :: scripts compiled clean` |

### 4.1 The six PNGs — **MET**, with one caveat that matters

All six exist in `Builds/ui-capture/` and all six were **OPENED by this lane**: `RaidHud_1920x1080.png`
(70795 B), `RaidHud_2340x1080.png` (82148 B), `RaidHud_2670x1200.png` (97461 B),
`RaidDeployHud_1920x1080.png` (117487 B), `RaidDeployHud_2340x1080.png` (139177 B),
`RaidDeployHud_2670x1200.png` (169933 B). None blank — each carries the readout column or the command
bar against black.

⚠ **THE PNGs ON DISK ARE THE RED RUN, NOT THE GREEN ONE.** All six are timestamped **07:35**, which is
`wave5-capture3red`; the 07:33 GREEN run wrote the same filenames and was overwritten. So the frames a
reader opens today show the **pre-1639** state — which is why `DEPLOY ...` is visibly ellipsised in all
three `RaidDeployHud_*.png`. That is excellent red-first evidence and **poor** post-fix evidence.
**To photograph the shipped state, re-run the capture at HEAD** (no code change needed); the GREEN
run's numbers below stand on the log regardless.

### 4.2 +6 panels — **MET, exactly 6**

`UI_CAPTURE_OK 91` (baseline) -> `UI_CAPTURE_OK 97` (green). The stamp moves on every axis by six:

```
baseline  UI_CAPTURE_STAMP head=0a7edc6b1 ... pngs=91 panelBuilds=76 canvases=91 ...
green     UI_CAPTURE_STAMP head=ff42319de ... pngs=97 panelBuilds=82 canvases=97
          touchPanels=97 touchClean=94 glyphPanels=97 glyphLabels=902
```
`canvases` 91->97, `glyphPanels` 91->97, `touchPanels` 91->97, `panelBuilds` 76->82. Both bodies logged
their honest state **3x each** (`grep -c` on the two log sentences returns `3` and `3`), so no target
fell through a `LogWarning` skip path.

### 4.3 Glyph coverage with non-zero labels — **MET**

Green: `UI_GLYPH_FAIL x1 NEW over 97 panels (96 clean, labels=902, baselined=2 of 3 found, unproved=0)`.
`labels=902` over 97 panels, `unproved=0` — the §4.3 `labels=0` failure mode is not in play. The one NEW
finding is **not on these panels**: it is
`TEXT TRUNCATED [ManageWorkspace_2670x1200] '.../ManageCard_ARMY/Label' ("BUILD BARRACKS") draws 12 of 13`
— an unrelated Manage surface, out of this lane's scope (§5).

### 4.4 `UI_GEOMETRY_OK` — ⛔ **THE ONE OPEN ITEM. BLOCKED ON WO-1646.**

Green emits `UI_GEOMETRY_FAIL x15 over 97 canvases` and `UI_TOUCH_FAIL x15 over 97 panels (94 clean)`.
**All 15 are on `RaidDeployHud`, 5 per aspect**, and they are **real defects the new capture FOUND** —
the first thing it did was catch two classes the source-text suites could not:

- `SUB-TOUCH-FLOOR BAND` x3 per aspect — the three faces resolve **103.7 / 93.9 / 92.7 ref px** tall
  against `ElarionUiKit.MinTouchPx (112)`, e.g. at 2670x1200:
  `'Panel/ObsBtn_Deploy All' resolves 360.9x92.7 ref px -- shortest side 92.7 is 19.3 px UNDER ... (112)`.
  The bar band is authored too short at every aspect and `ClampMinTouch` was silently papering over it.
- `BUTTON OVER TEXT` x2 per aspect — `ObsBtn_Deploy All` and `ObsBtn_Rally` cover
  `'Panel/Label' ("No troops to deploy - train at the Barracks f...")`, by **360.9x92.7** and
  **22.6x92.7** ref px at 2670x1200. Visible in all three `RaidDeployHud_*.png`: the sentence runs
  under the buttons and reads `...at the Barracks firs` before a face swallows it.

Per §5 (*"Do not 'fix' what the capture reveals"*) this lane did **not** touch them. They are
**WO-1646**, owned by the RAID-HUD lane. **`UI_GEOMETRY_OK` / `UI_TOUCH_OK` cannot go green until
WO-1646 lands — and that is the capture working, not the capture failing.**

### 4.5 Judged by markers on fresh logs — **MET.** Every line above is quoted off a log, never a runner
exit code.

### 4.6 THE RED-FIRST — **MET, and unambiguous**

`Builds/wave5-capture3red`: `UI_GLYPH_FAIL x4 NEW over 97 panels (93 clean, labels=902, unproved=0)`.
Three of the four are the reinstated Defect B, at **all three aspects**:

```
[glyph-oracle] TEXT TRUNCATED [RaidDeployHud_2670x1200 @2670x1200] 'Panel/ObsBtn_Deploy All/Label'
("DEPLOY ALL") draws 7 of 9 printable glyphs. (x 384.8..564.6, y -302.2..-209.5) at font 30
[autosize 30..44, enabled=True] overflow=Ellipsis wrap=NoWrap isTextTruncated=True
```
plus `[RaidDeployHud_1920x1080] ... draws 6 of 9` and `[RaidDeployHud_2340x1080] ... draws 7 of 9`.
The fourth is the unrelated Manage ARMY card. **The `9` predicted in §9.4 is confirmed**, so is
`isTextTruncated=True`, and the path resolved to `Panel/ObsBtn_Deploy All/Label` (the obsidian sprite
mode). GREEN, same label: `9 of 9` at all three aspects
(`[wo1645-face] ... ("DEPLOY ALL") face 360.9x92.7 ref px, font 44 ... isTextTruncated=False, 9 of 9`).
**The oracle has now been watched going red AND green on the same canvas.** §3c's probe corroborated it
independently: face width **195.5 -> 360.9** ref px, font **30 -> 44**, at 2670x1200.

⚠ **§9.4's finding held: the plate-alpha half of §4.6 red NOTHING**, because no rule on this path
measures contrast. Defect B carried the whole red-first, exactly as predicted.

### 4.7 Brace + NUL — **MET** (§9.7), and `COMPILE_GATE_OK` on `Builds/wave5-compile2`.

---

## 11. NEW FINDING — the readout plate reads OLIVE, and `barImg.color` is not what paints it

**The lead's eyes are right.** Measured off `Builds/ui-capture/RaidHud_2670x1200.png` by decoding the
PNG and sampling five interior points clear of text — (2150,200), (2200,470), (2300,300), (2500,540),
(2620,180) — **every one reads RGB (89, 72, 20)**, a dark olive/ochre, against (0,0,0) off-plate. Not
near-black. The command bar's plate in `RaidDeployHud_*.png` is the same olive.

**`ElarionUiKit.ObsidianFill = new Color(0.02f, 0.02f, 0.025f, 0.98f)`**
(`Assets/_Modules/Core/UI/ElarionUiKit.cs:189`), and WO-1639 points the plate at it — at HEAD,
`RaidHudController.cs:508` reads `barImg.color = ElarionUiKit.ObsidianFill;`.

⛔ **BUT THE PLATE'S COLOUR DOES NOT COME FROM THAT IMAGE.** `ElarionUiKit.Panel` (`:145-152`) builds
the fill and then calls **`AddInnerRim(p, AccentSoft)`** (`:150`), and `AddInnerRim` (`:2670-2683`) is
**not a rim**: it creates a FULL-RECT child `Image` at anchors 0..1 with a 1 px inset and
`color.a * 0.5f`. `AccentSoft` is `Gold` at alpha **0.30** (`UiStyle.cs:116`), so the child is
**Gold at 0.15 across the whole plate**, drawn ON TOP of the host's face. `ElarionUi.Gold` is
`(0.831, 0.686, 0.216)` (`ElarionUi.cs:58`). Composited in **linear** space and encoded back to sRGB
that predicts **(92, 76, 18)** against the measured **(89, 72, 20)** — the veil IS the plate's colour,
to within rounding. The kit's own doc block at `ElarionUiKit.cs:2657-2668` says so in its own words:
*"IT IS NOT A RIM ... a FULL-RECT filled rounded quad ... on an ornate plate it VEILS the art rather
than framing it."*

**Consequence for WO-1639:** re-tinting `barImg` from `(0.04,0.035,0.03,0.42)` to `ObsidianFill`
changes what sits **underneath** a 0.15-alpha gold veil. It cannot make the plate near-black on its
own, and the contrast ratios WO-1639 was fixing are measured against **this olive**, not against
ObsidianFill.

⚠ **TWO THINGS NOT PROVEN, and they decide whether this ships:**
1. **`AddInnerRim` returns early when `BlinkChromeActive`** (`:2672`). Headless with the Blink art
   absent that flag is presumably false, so the veil draws. **Whether it draws on the owner's DEVICE is
   NOT PROVEN from here** — if the shipping build has the art present, the olive may be a headless-only
   artifact. One device frame settles it.
2. The green run's PNG was overwritten (§4.1), so **HEAD's plate has not been seen rendered**. The
   argument above is from construction, not from a HEAD pixel.

**Not this lane's to fix** (§5). Recommended as its own ticket, with those two unknowns as its first
two measurements.
