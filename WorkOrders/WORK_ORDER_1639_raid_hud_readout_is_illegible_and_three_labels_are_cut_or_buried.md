# WO-1639 - The in-raid HUD: the readout panel is illegible at 1.1:1 contrast, DEPLOY is ellipsised in every frame, HERO DOWN is buried, and the objective marker has no size limit

**Status:** IMPLEMENTED - awaiting gate + device frame (lane RAID-HUD 2026-09-10)
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; number block 1637-1642 pre-assigned by the
lead, banner bumped 1637 -> 1643 in the SAME edit)
**Silo / Lane:** UI - `Assets/_Modules/Village/Troops/RaidHudController.cs` and
`Assets/_Modules/Village/Troops/RaidDeployController.cs`. Kit files are READ-ONLY (sec.7).
**Severity:** P1 felt. The readout is the only place the player learns whether she is winning, and two
of its five rows cannot be read at all. This is the HUD the owner was looking at when she said the
raid feels unpolished.
**Type:** EXISTING system, four defects on one surface.
**Owner words:** *"i have mentioned it in testing that it feels incomplete and not polished"*
(2026-09-10).

---

## 1. What was measured

Build 363529 on the Seeker. Frames under `Builds/device-frames/`, all 2670x1200 landscape, all opened
this session: `..._0608_arena_01_entry.png`, `..._0609_arena_02_pan_left.png`,
`..._0613_arena_05_hero_left_edge.png`, `..._0614_arena_06_wide.png`.

### 1a. DEFECT A - the readout panel, with measured contrast ratios

The top-right column. Five rows, all present, all correct in content:
`3:00` / `SPIRE 100%` / `Razed 0%` / a three-diamond star row with `0/3` / `Troops 0/0`.

**Contrast, sampled off the original `..._0608` PNG and computed with the standard WCAG relative-
luminance formula.** Plate sampled at (2200, 200) and (2100-2300, 395) -> RGB approximately
(136, 145, 154). Peak glyph luminance sampled across each row's text band:

| Row | peak glyph RGB | contrast vs plate |
|---|---|---|
| `3:00` (the brightest thing on the panel) | (243, 234, 211) | **2.67 : 1** |
| `SPIRE 100%` | (238, 200, 74) | **1.98 : 1** |
| `Razed 0%` | (199, 189, 168) | **1.72 : 1** |
| star diamonds (unlit) | (174, 182, 184) | **1.55 : 1** |
| `Troops 0/0` | (199, 189, 168) | **1.12 : 1** |

**Nothing on this panel reaches 3:1.** The common floor for large text is 3:1 and for body text
4.5:1. `Troops 0/0` at 1.12:1 is, to a reader, not there.

**And the plate is see-through.** In `..._0608` the boundary-ring pillars and one of WO-1638's green
slabs are plainly visible THROUGH the panel behind the star row and the `Troops` row. Sampling the
band directly behind `Troops 0/0` returns (139, 120, 77) - that is the tan ring, not the plate. So the
worst row is not even sitting on a constant background; its contrast varies with what is behind it,
and against the ring it measures 2.3:1.

**Where it comes from, at source.** `Assets/_Modules/Village/Troops/RaidHudController.cs`, `BuildHud`
at `:167`:

- **`:191`** `barImg.color = new Color(0.04f, 0.035f, 0.03f, 0.42f);` - **alpha 0.42.** The kit's canon
  panel fill is `ElarionUiKit.ObsidianFill` at **alpha 0.98** (`ElarionUiKit.cs:189`, mirrored at
  `ElarionUi.cs:47,51`). This plate ships at roughly 43% of canon opacity. The comment at `:189-190`
  states the intent - *"Quiet glass, not a gilt slab ... only the chrome gets out of the way"* - so
  this was a deliberate choice, and it is the choice that produced the numbers above.
- Seat: `:176-178`, `var band = ReadoutBand;` -> `:162-165` -> `HudLayoutBands.RaidReadoutBand`,
  `Assets/_Modules/Core/UI/HudLayoutBands.cs:333` =
  `Rect.MinMaxRect(0.780f, 0.510f, 0.995f, 0.870f)`.
- Row colours, via the local `MakeLabel` (`:272-292`):

  | Row | line | colour | font |
  |---|---|---|---|
  | Timer | `:202-203` | `ElarionUi.Parchment` | `FontBody` 50 bold |
  | `SPIRE 100%` | `:218-219` | `ElarionUi.Gilt` | `FontLabel` 40 bold |
  | `Razed 0%` | `:230-231` | **`ElarionUi.ParchmentDim`** | `FontLabel` 40, NOT bold |
  | `0/3` | `:257-258` | `Gilt` | `FontLabel` bold |
  | `Troops 0/0` | `:266-267` | **`ElarionUi.ParchmentDim`** | `FontLabel` 40, NOT bold |

  `ElarionUi.cs:67` - `ParchmentDim = (0.78, 0.74, 0.66)`, against `Parchment` at `:65`
  `(0.953, 0.918, 0.827)`.

**Stated plainly: nothing here is authored grey and nothing is broken.** The greyness is
`ParchmentDim`, not bold, on a 42%-alpha plate, over a bright daylit tan arena. The two rows that use
`ParchmentDim` are exactly the two rows that cannot be read.

### 1b. DEFECT B - `DEPLOY ...` is ellipsised in every arena frame

Every one of the four frames shows the bar face as `DEPLOY ...`.

- The literal is **`"Deploy All"`** - `RaidDeployController.cs:1703-1704`. Title case; the all-caps
  look is the Obsidian button skin.
- The face's band is **0.130 of the bar's width** (`0.565` to `0.695`), the narrowest of the three
  right-hand buttons (`Rally` 0.70-0.83 = 0.130, `Retreat` 0.845-0.985 = 0.140, `:1707-1710`), and the
  bar itself spans x `0.280`-`0.980` (`DeployBarBand`, `:1623-1630`).
- The fit: `ElarionUiKit.Button` routes to `BuildObsidianButton` (`ElarionUiKit.cs:1601-1612`), whose
  label is `ElarionUi.FontBody` (50) bold, then `FitSingleLine(tt)` at
  `ElarionUiKitObsidian.cs:686`. `FitSingleLine` (`:3054-3070`) sets `NoWrap`, **`Ellipsis`**,
  autosize `[FontFloor 30, 50]`, hard floor 20 (`:3033`, `:3044`).

So: a ten-character title-case label at 50 in a face that fits far less, shrunk to 30, then cut with
an ellipsis. **Rally (5 chars) and Retreat (7 chars) fit in the same width; Deploy All does not.**

### 1c. DEFECT C - `HERO DOWN` renders behind the deploy bar

`..._0613_arena_05_hero_left_edge.png`, cropped and enlarged this session from box
`(950, 820)-(1750, 960)` of the original: the words `HERO DO...` are faintly visible **through the
tan deploy-bar plate**, at roughly y 855-885 in original pixels. They are underneath it, not above it,
and the plate's own translucency is the only reason they can be seen at all.

- The literal is not `"HERO DOWN"`. It is
  `EndStateVM.HeroDownArmyFightsOn = "HERO DOWN - your army fights on"`
  (`Assets/_Modules/Village/UI/EndState/EndStateVM.cs:510`), printed by
  `RaidDeployController.NotifyHeroDown()` (`:1082-1095`, `SetStatus` at `:1095`, `SetStatus` itself at
  `:1919`), called from `Assets/_Modules/Village/Hero/HeroHealth.cs:969`.
- The widget is the deploy HUD's single `_status` label, `RaidDeployController.cs:1679-1699`:
  band `DeployStatusBand` (`:1634-1642`) resolving to **x 0.280-0.980, y 0.320-0.360**, `FontLabel` 40,
  `Parchment`, centred, `FitSingleLine(_status, FontHardFloor, FontLabel)` at `:1699` - the one call
  site in the raid HUD that passes an explicit 20 px floor, because the 0.040 band measures 38.62 px
  against a 30-px need of 38.58 (rationale at `:1691-1698`). **That is a 0.04 px margin.**

STOP: **THE SOURCE DOES NOT EXPLAIN THE BURIAL, AND THAT IS THE TICKET'S FIRST QUESTION.** In `BuildHud`
the bar panel is created FIRST (`:1664`) and `_status` SECOND (`:1680-1681`), both direct children of
the same canvas (`RaidDeployHud`, sortingOrder 30000, `:1658`). In uGUI the later sibling draws on
top. There is no `SetAsFirstSibling`, no reparent, no separate canvas for the status line. And the two
bands do not overlap in y on paper: bar `0.160`-`0.310`, status `0.320`-`0.360`.

But the frame measurement disagrees. On a 1200-px-tall frame, status y `0.320`-`0.360` maps to roughly
y 768-816 from the top; the observed glyphs sit at roughly **855-885**, i.e. inside the BAR's band
(828-1008). So on the device the status line seated lower than its authored band. **Do not fix this
from the source reading. Measure it first** - sec.4 Step 1.

### 1d. DEFECT D - the objective marker has no size limit, and it is NOT a UI chevron

The large yellow double-chevron shape. **The brief called it "an enormous objective chevron over the
compass" and the size half is right, but the kind is wrong, and the wrong kind sends the fix to the
wrong file.**

Measured across the frames: in `..._0608` and `..._0609` (camera far from the spire) the shape is
modest and sits above the rocks. In `..._0613` and `..._0614` (camera near) it spans from the top edge
of the frame down past the horizon - **hundreds of pixels tall.** It grows with proximity. A
screen-space UI rect does not do that.

**No file in `Assets/_Modules` authors a screen-space objective arrow or chevron for raids.** Every
gold direction indicator in the tree is small and authored in fixed reference pixels:

- `HudCompassWidget.cs:261-271` - the objective diamond, `sizeDelta = (20, 20)`, rotated 45,
  `ElarionUi.Gilt`, parented to `_tickLayer` which is a `RectMask2D` (`:215-219`), so it is CLIPPED to
  the tape.
- `HudCompassWidget.cs:285-294` - the static centre caret, `sizeDelta = (26, 16)`, Gilt. WARNING: It is
  parented to `_strip`, **not** to `_tickLayer`, so unlike the diamond it is not clipped - the only
  gold chevron-shaped UI thing structurally able to paint outside the tape, but at 26x16 it cannot be
  what these frames show.
- `HudMinimapWidget.cs:243-246` / `:446` - the objective dot, 14 px.
- `GuidePointer.cs:35` - `ChevronSizePx = 44f`, tutorial-only, driven from `TutorialFlow.cs:1196-1208`.

**The one large gold wedge that is actually in a raid scene is WORLD-SPACE:** the rally-flag banner
quad, `RaidDeployController.cs:904-939` - `banner.transform.localScale = new Vector3(0.9f, 0.6f, 1f)`
at `localPosition (0.45, 2.0, 0)` (`:924-925`), `BannerGilt` material. Being world-space at the raid's
monument scale it projects at whatever size the camera gives it, with no clamp.

**And the frames settle the kind, which the source reading alone could not.** All twelve arena frames
were opened this session. Two independent pixel facts:

1. **It draws BEHIND the compass strip.** Cropped and enlarged 3x from `..._0612` at box
   `(1150,40)-(1600,140)` and from `..._0613` at `(850,40)-(1300,140)`: the compass bar's black plate
   is **continuous and unbroken** where the yellow crosses it, and the bar reads dark OLIVE there and
   pure BLACK where sky is behind it - i.e. the yellow is showing THROUGH the bar's own translucency.
   **A ScreenSpaceOverlay canvas always draws over world geometry, so an element that the compass
   occludes cannot be a UI element on a higher canvas.** This also corrects the brief a second time:
   the shape is not "over the compass", it is behind it and merely large enough to surround it.
2. **It is semi-transparent and it is a heraldic shield, not a chevron.** `..._0618` shows the tower
   and the rock cluster visible THROUGH the yellow, and at that camera the silhouette resolves as a
   shield outline above a downward wedge, hovering over the hero's position.

Both are consistent with the world-space `BannerGilt` quad and inconsistent with every UI candidate
above.

WARNING: **STILL NOT PROVEN that it is the rally banner specifically, and NOT PROVEN that it is "the
objective marker" at all** - no source line calls it one, and the frames name a rendering layer, not a
GameObject. Step 1 names the object. **What IS now proven is the kind: it is world-space.** Do not
spec a `FitSingleLine`-shaped or band-shaped fix for it.

### 1f. Two more things the full frame sweep showed - recorded, NOT part of this ticket's scope

- **The illegibility is not a zero-state artefact.** `..._0615` reads `Troops 8/8`, `..._0617` reads
  `Troops 5/8` and `Razed 6%`, `..._0618` reads `Troops 4/8` - all in the same `ParchmentDim` at the
  same ratios, and in `..._0612`, `..._0610` and `..._0615` the boundary-ring pillars are plainly
  visible THROUGH the plate directly behind those rows. Sec.1a's measurements hold for live values,
  not just for `0/0`.
- **A sixth raid surface exists and no ticket covers it.** `..._0620_arena_11_result.png` is the
  `TIME!` end panel - and it reads WELL: gold frame, opaque plate, high contrast, whole words. It is
  the counter-example that shows the in-raid HUD's plate alpha is the outlier, not the house style.
  WARNING: one thing on it is worth a second pair of eyes and is deliberately NOT minted here: the panel
  awards **three empty stars** while the readout still visible behind it reads **`1/3`**, and the
  device log records `FIRST RAID COMPLETED (stars 0)`. `RaidWatchdogHonorRegression.cs:550-552` pins
  the readout row to `PresentationStars` rather than `ProjectedStars`, so the two may legitimately be
  different quantities. **Unproven either way - raise it with the lead, do not act on it.**

### 1e. Why no gate caught any of this - and it is one answer, not four

**Nothing renders the in-raid HUD.** `Assets/Editor/UICaptureLaunch.cs` captures only the PRE-raid
screens: `CaptureRaidSelection()` (`:621`, `:2478`, `:6597-6603`) and `CaptureRaidDeploy()` (`:622`,
the pre-raid `RaidDeployScreen`). **There is no capture of any `RaidBase_*` scene, no
`RaidHudController` capture, no `RaidDeployController` capture and no compass capture.**

And `Assets/_Modules/Core/UI/LayoutOracle.cs` has **zero** matches for `Raid`, `Deploy`, `Readout` or
`Compass`. The readout, the bar, the status line, the compass and the marker are not in the oracle at
all.

So the only automated coverage of all four defects is source-text and band arithmetic, via
`RaidHudThumbBandRegression`, `RaidArenaShapeRegression`, `RaidScoringRegression` and
`RaidDeployUiRegression` (sec.7). **Nothing renders these elements and measures pixels.** That gap is
why four visible defects shipped green, and it is worth its own ticket - raise it, do not build it
here (sec.8).

Note also that this is why WO-1636's glyph oracle did not catch `DEPLOY ...` or `HERO DOW...`: that
oracle runs over the 21 captured panel builds, and the in-raid HUD is not one of them. Checked - none
of `DEPLOY`, `SPOILS`, `HERO DOWN`, `Razed` or `Troops` appears in WO-1636's list. Same family,
different surface, no scope collision.

---

## 2. What is NOT claimed

- **NOT claimed the readout's CONTENT is wrong.** Every row carries the right words and the right
  numbers, and `Refresh()` (`RaidHudController.cs:352`, `:361`, `:366`, `:372`, `:380`, `:384`) writes
  them correctly. This is legibility only.
- **NOT claimed why the status line seated below its authored band** (sec.1c). The source says it
  should not. Measure before editing.
- **NOT claimed the yellow shape is the rally banner** (sec.1d).
- **NOT claimed the contrast numbers are the device's colour pipeline exactly.** They are computed off
  the captured PNG, which is what the player saw. Good enough to prove illegibility, not offered as a
  colorimetric audit.
- **NOT proven whether these frames post-date WO-1436 / WO-1464.** The newest raid capture in the repo
  (`Logs/device/screens/owner-screen-20260907-004502.png`, cited in the source itself at
  `RaidHudController.cs:146` and `HudLayoutBands.cs:262`) shows the PRE-fix full-width status layout,
  so some reported geometry may already be superseded in this tree. **Check the build's own commit
  before treating sec.1c as live.**

---

## 3. Target - what "fixed" means

Every row of the readout is readable at a glance on a bright daylit arena, at arm's length, without
looking twice - including `Razed` and `Troops`. Every bar face shows its whole word. `HERO DOWN` is
the most legible thing on the screen at the moment the hero falls. The objective marker reads as a
marker at every camera distance.

---

## 4. The fix

### Step 1 - INSTRUMENT and MEASURE (do this before any layout or colour edit)

Three unknowns, three measurements. Per CLAUDE.md sec.12 the instrumentation stays in afterwards.

1. **The status line's real seat.** Print, after layout, `_status`'s resolved world/screen rect, its
   sibling index, its canvas and sortingOrder, and the same for the bar panel. That settles sec.1c in
   one read and tells the lane whether it is a seat problem or a draw-order problem.
2. **The yellow shape's identity.** Print, for the rally banner and for the compass caret, the object
   name, whether it is world- or screen-space, and its projected screen height in pixels this frame.
   Whichever one reports hundreds of pixels is the shape in the frames.
3. **The readout rows' fit state.** For each of the five rows: resolved band px, `fontSize` /
   `fontSizeMin` / `fontSizeMax`, `textInfo.lineCount`, `characterCount` vs `text.Length`,
   `isTextTruncated`, and the overflow/wrapping modes as they stand at render. Same shape as WO-1628
   sec.4 Step 1, which is the established pattern here.

Compute interpolated parts into locals before building any of these strings - the gate's brace scanner
has no interpolated-string model (CLAUDE.md sec.1).

### Step 2 - the changes

**Defect A - the readout.** Two levers, and the lane should expect to use both:

- **The plate.** `RaidHudController.cs:191` alpha `0.42`. Raising it toward the kit's canon `0.98`
  (`ElarionUiKit.cs:189`) fixes the see-through problem AND lifts every row's ratio at once. But
  `:189-190`'s "quiet glass, not a gilt slab" is a deliberate authored intent - **changing it is a
  look change, so it goes to the owner with the rest of sec.3's ruling below.**
- **The two dim rows.** `Razed` (`:230-231`) and `Troops` (`:266-267`) are `ParchmentDim` and not bold
  while every legible row is either `Parchment`, `Gilt`, or bold. Promoting those two to `Parchment`
  and/or bold is the smallest change that clears the floor, and it touches no kit file.

**Target the numbers, not the adjective:** every row at **4.5:1 or better** against the plate, and the
plate opaque enough that nothing behind it reaches the glyphs. Re-measure with the same method as
sec.1a and paste the table.

**Defect B - `Deploy All`.** Either widen the face or shorten the copy. Widening it means re-balancing
three faces on one bar (`:1703-1710`) - say what moved. STOP: **Shortening the copy is PINNED:**
`RaidDeployUiRegression.cs:411-415` requires the exact literal `"Deploy All"` in this file. A copy
change reds that oracle and needs the owner's word on the new word; do not quietly rename it.

**Defect C - `HERO DOWN`.** From Step 1's measurement. If it is a seat problem, fix the band; if it is
draw order, fix the order. WARNING: The band has a **0.04 px** margin at the 30-px floor (`:1691-1698`) - it
is one rounding away from failing anyway, so whichever cause is found, give this line real room. The
hero falling is the single most important message this HUD ever shows.

**Defect D - the objective marker.** From Step 1's identification. If it is the world-space banner,
the fix is a screen-size clamp or a distance-scaled cap on a world-space marker - **not** a UI band
and **not** a fitter.

---

## 5. THE OWNER RULING

Two items in sec.4 change how the game looks, and the owner makes all final creative decisions
(CLAUDE.md sec.2). Ask both in one `AskUserQuestion`, with `..._0608` and `..._0614` attached:

1. **The readout plate.** Its 0.42 alpha is authored intent ("quiet glass"). Legibility needs it more
   opaque. Which wins?
2. **`Deploy All`.** Wider face, or a shorter word? The word is pinned, so a shorter one is her call.

STOP: **The owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) - do NOT ask her to
pick a colour.** Ask about opacity and about words. The palette values are the lane's, and the gate on
them is the greyscale check in sec.6.

---

## 6. Acceptance

1. **The frames.** Fresh device frames of the in-raid HUD from the same seats as `..._0608` and
   `..._0613`, OPENED and pasted. The owner's standing rule is *"I want images to verify anything that
   is a viewable issue"*.
2. **The contrast table, re-measured** by the sec.1a method, every row at 4.5:1 or better, pasted next
   to the original table.
3. **The greyscale check** of the same frames - the owner is colourblind, and greyscale is the gate.
4. **Every bar face shows its whole word** - `Deploy All` (or its ruled replacement), `Rally`,
   `Retreat`. Paste what each reads.
5. **`HERO DOWN - your army fights on` renders in full, above everything**, in a frame taken at the
   moment the hero falls. Paste it.
6. **The objective marker at two camera distances** - near the spire and far from it - in one frame
   each, with its projected screen height from Step 1's trace.
7. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 7. Pins - what must not move

- `Assets/Editor/Regression/RaidDeployUiRegression.cs:411-415` - the exact literal `"Deploy All"` in
  `RaidDeployController.cs`. Renaming or shortening the face without touching this oracle reds the
  build.
- `Assets/Editor/Regression/RaidScoringRegression.cs:411-413` - the hero-down line must come from
  `EndStateVM.HeroDownArmyFightsOn`, never be re-typed inline; `:294-309` - `RaidHudController.cs`
  exists and is code-built uGUI, no UXML.
- `Assets/Editor/Regression/RaidArenaShapeRegression.cs:59`, `:541-547` - the literal `SPIRE`,
  `ObjectiveHpFraction` and `DestructionPct` must stay in `RaidHudController.cs` (its own rationale at
  `:529-530`: *"'Razed N%' fed by a corpse count while the win condition is a spire is exactly the lie
  this pins"*).
- `Assets/Editor/Regression/RaidHudThumbBandRegression.cs` - `:207-209` the readout seat must keep
  reading `HudLayoutBands.RaidReadoutBand` and not be re-literalised; `:399-401` the status line must
  seat `NeedPx(FontHardFloor)`; `:402-405` the readout column's thinnest authored row is 0.120 of the
  panel (that is the `Razed` row, `:230-231`; `Troops` and the star row are 0.130, so 0.120 is still
  the minimum). **Any band edit updates this.** `:406-407` the tray count badge.
- `Assets/Editor/Regression/RaidWatchdogHonorRegression.cs:80`, `:550-552` - `PresentationStars`, not
  `ProjectedStars`, for the star row.
- STOP: **`ElarionUiKit.cs`, `ElarionUiKitObsidian.cs` and `ElarionUi.cs` are READ-ONLY.** `FontFloor`
  (`:3033`), `FontHardFloor` (`:3044`), `MinTouchPx = 112` (`ElarionUiKit.cs:347`), `ObsidianFill`
  (`:189`), `Parchment` / `ParchmentDim` / `Gilt` (`ElarionUi.cs:65`, `:67`, `:60`), `FitSingleLine`,
  `FitBlock`. Cite them; change nothing. **Lowering a kit constant or re-defining a palette colour so
  one screen reads is the inverse of this fix** - it silently repaints the whole game.
- `HudLayoutBands.RaidReadoutBand` (`:333`) unless Step 1 proves the band is implicated.
- The rally banner's world placement (`RaidDeployController.cs:904-939`) - its primitive shape is
  pinned by `RaidSelectionLayoutRegression` case `S8:no-bare-primitive` (per the comment at `:893`).

---

## 8. What NOT to touch

- **The staging screen** - `SPOILS`, `BEGIN ASSAULT`, the outmatch confirm. **That is WO-1640**, a
  different file (`RaidDeployScreen.cs`) and a different lane.
- **The arena's materials, fog, ring and spire** - **that is WO-1637**. Do not adjust the world to
  make the HUD read.
- **The town chrome** - **that is WO-1642**.
- **No new capture fixture and no new oracle rule in this ticket.** A `RaidBase_*` capture and a
  raid-HUD oracle are the right long-term answer to sec.1e, but `LayoutOracle.cs:17-20` requires any
  new rule be SEEN RED first against a synthetic authored-defect canvas. That is its own ticket with
  its own red-first proof. **Scoped out deliberately, not forgotten - raise it in the hand-back so the
  lead can mint it.**
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 9. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1639_raid_hud_readout_is_illegible_and_three_labels_are_cut_or_buried.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.
