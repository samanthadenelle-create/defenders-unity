# WO-1515: the defense report is an unreadable tan slab with overlapping rows, and its only door is buried under Settings

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (panel half + the sec.2B/2D HUD chip; prior: READY TO IMPLEMENT - P1, owner-ask, ruling received 2026-09-06 20:05)
**Silo:** `Assets/_Modules/Village/UI/Defense/DefenseReportPanel.cs` + a new HUD chip.
**LANDS AFTER** the WO-1465 / 1466 / 1468 lane commits - that lane is editing `HudKitController.cs` tonight.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1515 -> 1516 in the same edit).

## 1. EVIDENCE

Owner device frame `Logs/device/screens/owner-defense-report-20260906-200350.png`, build 2026.09.07.358574,
20:03. Her words tonight: *"screenshot of defense report"*.

Right pane is a flat BEIGE rectangle with grey text, all near-invisible:

```
"They never reached your inner ring."   "Defence score 100/100 - Clean hold"
"ATTACKER"                              "Strength 147 - wave 13 - lasted 44s"
```

Only `Hollow Host (raiders)` is legible; the heading `HELD` is gold-on-tan.

Left list: `HOLLOW HOST - 6H AGO` paints OVER the BREACHED row's gold frame - the two rows share one band.

## 2. FIX SHAPE

- Build the detail pane with the kit obsidian plate + gold bezel and `ElarionUi.Ink` on dark, the way
  `ManageScreenPanel` does. No bespoke tan surface.
- Rows laid out by the kit list with a DERIVED pitch (not a fixed offset), `FitSingleLine` on every row.
- A measured layout case at 2670x1200: no row overlaps another, and the detail text meets a contrast ratio.

## 2B. THE DOOR (owner ruling, 2026-09-06 20:05, verbatim)

> "the only way to get to the defense report is buried under settings then realm. should be on screen as a
> button if there is a report that is incoming"

Today's only routes, at source:

```
SettingsController.cs:748        PanelRouter.Open(PanelId.DefenseReport)   -- Settings -> Realm
PlayerDeckWorkspace.cs:736       the same panel as a deck route
```

Required:

- When an UNREAD report exists (`DefenseReportLedger.All()` has an entry newer than the last-read mark), a HUD
  chip appears in the RIGHT COLUMN, same family and same kit as the Builders status chip: derived band,
  `ElarionUiKit.MinTouchPx`.
- It reads `ATTACK REPORT` with the outcome word (`HELD` / `BREACHED`), opens `PanelId.DefenseReport` through
  `PanelRouter`, and DISAPPEARS once read.
- Settings -> Realm stays as the ARCHIVE door.
- The WO-1408 lane already routes the welcome-back ATTACKED row to this panel; this chip is the in-town
  equivalent of that door.

## 3. WHAT NOT TO DO
- Do not leave the chip on screen when there is nothing unread. A permanent chip is a fifth status glance
  competing with the four that earn their place.

## 2C. RCA OF THE TAN SLAB (proven at source 2026-09-06, PANEL lane)

Not a "bespoke tan surface" — the panel never authored one. `DefenseReportPanel.StyleObsidianWell`
built **ONE** `Image`, seeded it `ElarionUiKit.ObsidianFill`, then **overwrote that fill**:

```
img.sprite = frame;      // Resources "UI/ElarionMedieval/frames/card-frame-empty"
img.color  = Color.white;
```

`card-frame-empty` is a **hollow bezel with a transparent centre**, so no dark surface remained.
`FrameQuest` is a `twoToneBody` frame, so `ElarionUiKit.BuildObsidianPanel` paints
`ZoneBacking(layout.bodyRight, TwoToneParchmentFill)` = RGB(0.827, 0.760, 0.576) behind the detail
zone — that tan read straight through the hole, under ink the panel had already picked for a DARK
surface (`_onParchment == false` -> Gilt / Parchment / ParchmentDim). Measured: ParchmentDim on that
tan is **1.05:1**; on the obsidian plate it is **10.96:1**.

**The left well proves it.** It takes the *identical* call and looked correct, because its backing is
the kit's dark `TwoToneWellFill`. One code path, two surfaces, one broken — the fill was never doing
the work it was credited with.

The row overlap is the second, separate cause: the row label carried a hard `\n`, and `FitSingleLine`
is a **WIDTH** fit (NoWrap + Ellipsis + autosize). A hard break survives NoWrap, so autosizing never
shrank the label to make its second line fit the fixed 132px band.

**Note on §2's wording:** it asks for the obsidian plate *and* `ElarionUi.Ink`. Those are opposite —
`Ink` is dark brown (0.137, 0.098, 0.055), for parchment. Surface and ink are one decision; the plate
is dark, so the panel keeps its light inks (Gilt / Parchment / ParchmentDim) and the oracle asserts
each clears 4.5:1 against the plate. Flagging rather than silently picking (CLAUDE.md §11B.B).

### Shipped in this lane
- `Assets/_Modules/Village/UI/Defense/DefenseReportPanel.cs` — plate and bezel are now **two** images
  (opaque `WellFill` plate, bezel as a later sibling); the row band is **derived**
  (`Mathf.Max(ElarionUiKit.MinTouchPx, RowFontMax * RowLineBoxMul + RowPadPx)` = 112px, gap 10px,
  pitch 122px); the row label is **one line** with `FitSingleLine(caption, 30f, 44f)` armed
  explicitly; scroll padding clears the bezel (22 list / 28 detail).
- `Assets/Editor/Regression/DefenseReportLayoutRegression.cs` — NEW suite, markers
  `DEFENSE_REPORT_LAYOUT_OK` / `_FAIL`, registered in `DataRegression.RunAll` as
  `defense-report-layout suite`. Three cases: `[derived-pitch]` (band fits its own line box, pitch
  clears the band, >= 2 whole rows in the list well at 1920x1080 / 2340x1080 / **2670x1200**),
  `[dark-plate]` (WCAG contrast of every detail ink against the plate >= 4.5:1, with the shipped tan
  as the negative fixture that must stay under the floor), `[source-laws]` (plate/bezel stay split,
  no `img.sprite = frame` on the fill, no `\n` in the row label, `FitSingleLine` armed,
  `_onParchment` never true, NUL-free, braces balanced).
- `HudKitController.cs` deliberately **untouched** — see 2D.

## 2D. SPEC FOR THE CHIP LANE (§2B), handed over, not implemented here

Add one right-column status chip to `HudKitController`, built with the same `BuildRailChip` call the
Builders chip uses so it inherits the derived band and `ElarionUiKit.MinTouchPx` rather than authoring
a fifth geometry. Its visibility is driven by a single predicate — `DefenseReportLedger.UnreadCount() > 0`
— evaluated on the same rail refresh tick the Builders chip already runs on, so it appears when a
report lands and disappears the moment one is read (`DefenseReportPanel.Select` calls
`DefenseReportLedger.MarkRead`, which is the only place the count drops). Its caption is
`"ATTACK REPORT - " + outcome word of the newest unread record` (`HELD` / `BREACHED` / `OVERRUN`) so it
survives greyscale, fitted with `FitSingleLine`; tapping it calls
`PanelRouter.Open(PanelId.DefenseReport)`. Nothing else changes: Settings -> Realm stays the archive
door, the bar's four faces are untouched (`HudActionBarModel.MaxVisibleFaces` is not a knob here), and
the chip must not render at all — not greyed, not empty-stated — when the unread count is zero, since a
permanent chip is the fifth glance §3 forbids. Pin it with one measured case that the chip exists only
while an unread report exists.

## 4. ACCEPTANCE
- [ ] Detail pane on the kit obsidian plate; no row overlap; measured case at 2670x1200 with a contrast assert.
- [ ] One measured case that the chip exists ONLY while an unread report exists.
- [ ] A headless PNG with the chip on screen, opened and looked at.
- [ ] `REGRESSION_OK n/n` on a fresh log.
