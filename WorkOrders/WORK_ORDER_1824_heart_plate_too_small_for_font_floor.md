# WO-1824: The Heart plate is too small to seat the 30 px font floor

**Status:** READY

**Ticket:** WO-1824
**Route:** `Main_Castle_Overworld` (town HUD, left column)
**Date logged:** 2026-09-17
**Silo:** `Assets/_Modules/Core/UI/HudLayoutBands.cs` (layout), `Assets/Editor/Regression/HudLabelFitRegression.cs` Case 10
**Parent:** WO-1823 (player report, build `2026.09.16.371701`: *"Text is too small to read."*)

---

## ⛔ THE INSTRUCTED FIX WAS NOT MADE, AND THIS TICKET IS WHY

The assignment was: grow `HudLayoutBands.HeartMount` enough that all four Heart rows fit at the
30 px floor, then re-point `HudLabelFitRegression` Case 10c to assert the new sizes.

**Investigation result: the required growth does not exist inside the left column.** `HeartMount` is
boxed between two neighbours whose own occupants are already over-subscribed, so the only ways to find
the height are an owner visual ruling on the column, or a content ruling on the plate's row count.
`HudLayoutBands.cs` and Case 10c are therefore **UNTOUCHED** — shipping the growth by taking the height
from the minimap would re-create a defect the owner has already captured once (see §3). Every number
below was read at source this session.

---

## 1. How much plate height the floor needs

Canvas is authored 1080x1920 with `CanvasMatch = 0.5f` (`HudLayoutBands.cs:55-59`), so the reference
height is aspect-dependent and **2670x1200 (the owner's Seeker, `:62-66`) is the binding aspect**:

| Aspect | scale = (W/1080)^0.5 x (H/1920)^0.5 | reference height | plate = 0.135 x refH x 0.96 |
|---|---|---|---|
| 2670x1200 | 1.2431 | **965.3** | **125.1 ref px** |
| 1920x1080 | 1.0000 | 1080.0 | 140.0 ref px |

(`HeartPlateOfMount` 0.96 and the 125/140 pair are `HudLabelFitRegression.cs` Case 10's own numbers —
this table reproduces them from the constants rather than trusting them.)

Case 10c's rule: every row band must seat `floor x LineHeightFactor` where `LineHeightFactor = 1.2f`
(`HudLabelFitRegression.cs:174`). At the 30 px floor **each of the four rows needs 36.0 ref px**, so the
plate must carry **144 ref px of text**.

Case 10a confines every band to the plate's visible frame **0.06..0.97** = 0.91 of the plate
(`HudLabelFitRegression.cs:1656-1658`).

| Packing | Required plate | Required `HeartMount.height` (÷ 965.3 x 0.96) | Growth over 0.135 |
|---|---|---|---|
| **Hard minimum** — 4 rows at 36 px, zero inter-row gaps, full 0.91 frame | 144 / 0.91 = **158.2 px** | **0.1707** | **+0.0357** |
| **Proportionate** — today's 0.82 band / 0.18 gap ratio preserved | 144 / 0.82 = **175.6 px** | **0.1896** | +0.0546 |

The hard minimum is a zero-gap plate, which is legal by Case 10a (it forbids overlap, not adjacency) but
is **not** "visual proportions sane". A sensible landing point is between them — ~0.180 (plate 166.8 px,
151.8 px usable vs 144 needed, a 7.8 px margin). **All three numbers need ≥ 0.0357 of screen height.**

## 2. The slack that actually exists (read, not inferred)

`HeartMount = Rect.MinMaxRect(0.011f, 0.655f, 0.240f, 0.790f)` (`HudLayoutBands.cs:84`), between:

| Neighbour | Band | Its occupants | Verdict |
|---|---|---|---|
| `VitalsMount` `:73` | 0.800..0.983 (0.183 = **176.6 ref px**) | hero plate `HeroPlateInVitals` 0.180..1.0 of mount = 145 px, + the SKILL chip on the remaining 31.8 px | **no slack** — the chip is already under a 36 px line |
| gap above | 0.790..0.800 = **0.010** | deliberate | ~0.008 recoverable, at the cost of breathing room |
| gap below | 0.645..0.655 = **0.010** | deliberate: the Night Market card's soft ring (`HudKitController.NightMarketRingPx`, WO-1384b, cited at `HudLayoutBands.cs:78-83`) | **do not take** |
| `MinimapMount` `:93` | 0.420..0.645 (0.225 = **217.2 ref px**) | `MinimapPlatePx` 200 + `StatusLineGapPx` 4 + `StatusLinePx` 30 = **234 px** (`HudLayoutBands.cs:107-113`, consumed as `PlateSize`/`ChipHeight` at `HudMinimapWidget.cs:88/91`) | **already over-subscribed by ~17 px — it has negative slack** |
| `DockMount` `:97` | 0.360..0.470 (0.110 = **106.2 ref px**) | gear + Store row at `DockControlPx = ElarionUiKit.MinTouchPx` = **112 px** (`HudLayoutBands.cs:115-120`); the drawer hangs to the RIGHT of the mount (`HudKitController.DockPanelSeatAnchorX` ≈ 1.174, `:5279-5291`) so it costs no left-column height | **already over by ~6 px** |

**Total recoverable inside the column: ~0.008 of screen. The deficit is ~0.0357. Short by a factor of
four.**

## 3. Why the obvious move is forbidden

Taking the height by shrinking or lowering `MinimapMount` pushes the region status line ("Elarion -
Safe - N threats") down into `DockMount`'s gear/Store band. `HudLayoutBands.cs:87-92` records that exact
frame as already captured by the owner: *"the owner captured 'Elarion - Safe - N threats' competing with
both the map and the gear."* Trading one captured legibility defect for a re-run of another is not a fix.

## 4. Candidate remedies — each needs a ruling, none is a code-only call

1. **Restack the left column** (owner **visual** ruling). Find ~0.036 across hero plate / SKILL chip /
   minimap / dock, accepting that two of those bands are already over their occupants' needs. Cheapest
   honest version is shrinking the 200 px minimap plate, which is the owner's map.
2. **Drop the Heart plate to THREE rows** (owner **content** ruling). Merge the rekindle line into the
   objective line. Three rows at 36 px = 108 px, needing 108 / 0.91 = 118.7 px — **satisfied by today's
   125.1 px plate with no layout change at all.** This is the only option that needs zero height.
3. **Raise only name + Heartfire to 30, leave objective + rekindle at `ElarionUiKit.FontHardFloor` (20).**
   The two small rows then need 24 px each and the two large rows 36 px = 120 px of text. But Case 10c
   checks **per band**, and the name band is 0.22 of the plate (`HudKitController.cs:2505/2506`), so the
   binding constraint is 36 / 0.22 = **163.6 px → height 0.177 → still +0.042**. ⛔ **This option does NOT
   escape the deficit** unless the row fractions are re-cut at the same time (e.g. name and Heartfire to
   0.26 each, objective and rekindle to 0.17: then 36 / 0.26 = 138.5 px, **satisfied by today's plate**).
   That re-cut is a `HudKitController` row-band change, not a `HudLayoutBands` one.

**Recommendation: option 2 or the option-3 re-cut**, because both land inside the plate the owner already
approved and neither touches a neighbour's band. Option 1 is the only one that grows the plate, and it is
the one that needs her eyes.

## 5. What this ticket will do once ruled

- [ ] Apply the ruled remedy (row-band re-cut, row merge, or `HeartMount` growth + neighbour restack)
- [ ] Raise the Heart font consts to `30f` **as float LITERALS, never aliased** to
      `ElarionUi.FontFloorMobile` — Case 10 parses them as numbers out of the source
      (`HudKitController.cs:2537/2538`)
- [ ] Re-point Case 10c to assert the floor-compliant sizes and **delete the WO-1823 EXCEPTION block**
      above `HeartNameFontMin`, plus Case 16d's pin on it (`HudLabelFitRegression.cs`)
- [ ] Screenshot-verify: no headless capture for the Heart plate alone was found this session
      (`RunCaptureHeadless` is the town-HUD capture); judge the plate in a fresh device frame at
      2670x1200 and 1920x1080

## 6. Not determined

- Whether the top margin `VitalsMount.yMax` 0.983 carries any free height: `SafeAreaInset`
  (`Assets/_Modules/Core/UI/SafeAreaInset.cs`) exists and insets screen-anchored HUD corners from
  `Screen.safeArea`, but whether this mount is already inside that inset was **not** traced. Do not
  budget the 0.017 above 0.983 until it is.
- `LineHeightFactor` 1.2 is described in its own summary as *conservative* — TMP's real line height for
  these faces is below it. A measured line height could shave the requirement, but that is a change to
  the oracle's rule and needs its own ruling; it is NOT a licence to lower the number to make this fit.

---

## 7. Done under this ticket number (unblocked, landed)

- `HudKitController.cs:2346-2354` — `BuildRailChip`'s stale header corrected. It argued for `FitBlock`
  ("the label wraps instead") while the method has always called `FitSingleLine`, and closed with
  "nothing is clipped or dropped", which is false for a width fit. It now states what the code does
  (bounded auto-size at the floor, no wrap, **ellipsis** on an over-wide caption) and names the wrap as
  an open behaviour ruling. **Comment only, no behaviour change**; `gate_brace` clean (`bad=0 of 1`,
  434/434 braces, 0 NUL bytes). Carried here from WO-1823's RESULT open finding 3.

---

**Assigned to:** HUD/Core layout lane. **Blocked on:** owner ruling, §4.
