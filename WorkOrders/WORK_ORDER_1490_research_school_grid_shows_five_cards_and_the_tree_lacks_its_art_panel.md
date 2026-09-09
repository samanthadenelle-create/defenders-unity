# WO-1490: the Research school grid shows 5 cards instead of 4 and leaves a 45% dead band; the tree has no art panel or RESEARCH button

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:07:20, build 2026.09.08.361259). PRIOR STATUS: AWAITING OWNER MATCH - the three named defects were closed by the overnight Manage passes
(`c0c30f715`, `94808e2e2`); this seat re-measured them at HEAD and closed the one item that was still
open, which turned out to be the MEASUREMENT and not the layout. See §5.
**Silo:** Manage 2000-block (WO-2010, research schools).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1490 -> 1491 in the same edit).

## 1. EVIDENCE

```
Builds/ui-capture/ManageFlow_RESEARCH_gridtop_2670x1200.png
  five cards per row -> Lumber Mill ORPHANS to row 2; ~45% of the panel is dead band below
Builds/ui-capture/ManageFlow_RESEARCH_school
  two rows read "QUEUE FULL"; captions clipped ("Mana")
```

The mockup's panels 6 and 7 show a FOUR-card grid and, on the tree, a left art panel with a gold RESEARCH
button carrying the costs. Neither is present.

## 2. FIX SHAPE

- Four cards per row; card width derived from the plate, so the fifth cannot squeeze in and orphan the sixth.
- Reclaim the dead band: the grid grows to fill the plate rather than sitting in the top 55%.
- Add the left art panel and the gold RESEARCH button with costs, matching mockup panels 6 and 7.
- `FitSingleLine` on captions ("Mana" is a clipped word, not a short one).
- A MEASURED case: cards-per-row == 4, no orphan row, captions not truncated, dead band under a threshold.

## 3. WHAT NOT TO DO
- Do not shrink the cards to fit five. The mockup says four.

## 4. ACCEPTANCE
- [ ] Fresh `ManageFlow_RESEARCH_gridtop` and `_school` PNGs opened; four per row, no orphan, no clip.
- [ ] Art panel + RESEARCH button with costs present.
- [ ] Measured case, RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.

---

## 5. RE-MEASURED AT HEAD, 2026-09-07 (implementation lane)

Everything below was read or measured this session at HEAD `17e3c4f03`. Frames
`Builds/ui-capture/ManageFlow_RESEARCH_*` and `Builds/cap-manage-wave5c.log` are both 03:20 and are
FRESH for the renderer: `ManageWorkspacePanel.cs` was last committed in `94808e2e2` (03:31, file mtime
03:03) and nothing has touched it since. `ManageScreenVM.cs` moved once after, in `3eecd3b99`, whose
scope is the Manage HUB.

### 5.1 The three named defects are CLOSED, and the "four per row" prescription is SUPERSEDED

| §2 item | State at HEAD | Proof read this session |
|---|---|---|
| orphaned school / ragged row | closed | `cap-manage-wave5c.log`: *"research picker capacity derived from 5 live school(s) -> 5x1 (0 empty cell(s))"* |
| left art panel on the tree | present | `cap-manage-wave5c.log`: `MANAGE_LIST_PAINTING key=Portraits/Buildings/arcane-tower side=734px in a 1835x758px well - the rows take x 0.42..1.0`, and the painting is visible on `ManageFlow_RESEARCH_school_2670x1200.png` |
| gold RESEARCH button + costs | present in code, NOT provable from these frames | `ManageWorkspacePanel.cs:1001` paints the inline action; pinned by `[research-tree-inline-cost]`. ⚠ Every row in the captured state reads RESEARCHED / QUEUE FULL / RESEARCHING (queue badge 15), so **no researchable row was on screen** - the button is UNPROVEN from the frame |
| clipped captions ("Mana", "Arcane Bas...", "RESE...") | closed | the `MaxTileAspect` width clamp is exempted for `columns == 1` (`ManageWorkspacePanel.cs:614`); `ManageFlow_RESEARCH_school` now reads "Mana Attunement" and "Wellspring of Elarion" in full |

⛔ **§2's "four cards per row" and §3's "do not shrink the cards to fit five" are RETIRED, deliberately.**
`ApplyPickerCapacity` (`ManageScreenVM.cs:4845`) derives the row from the LIVE school count and clamps
at five; five schools exist, so the row is 5x1 with **zero** empty cells. A literal 4 is the authored
capacity this program has already removed twice - it is what orphaned the Lumber Mill in the first
place. The owner passed the picker on `WO-1564` (2026-09-07 14:15, build 359076,
`proof/owner-validations.json`). **Implementing §2 literally would re-open the defect the ticket was
written about.**

### 5.2 THE ONE THING STILL OPEN WAS THE INSTRUMENT, NOT THE LAYOUT

`MANAGE_GRID` divided `fillH` out of `rowsPx` - the rows that WOULD FIT - instead of the rows of
content that exist. Measured, `cap-manage-wave5c.log`:

```
MANAGE_GRID tiles=5 want=5x1 cell=359px band=1835x758 rowsFit=2 shown=10 hidden=0 gridW=1835 fillW=1 fillH=0.96
```

`ManageFlow_RESEARCH_gridtop_2670x1200.png` shows ONE 359px row centred in that 758px well with
roughly half of it black. **0.47 of the band is painted and the log said 0.96**, because two rows fit.
That is the exact number this ticket's "45% dead band" is judged by, so the ticket could have been
retired on a line that contradicted its own frame.

The surplus itself is **forced and is not a defect**: five square tiles share the band's WIDTH, so the
cell can never exceed `bandW/5` = 359px however tall the well grows, and the renderer already splits
the remainder above and below (mockup panel 6 centres it). Fixing it would mean portrait tiles the art
was not drawn for, or four across - which the mockup does not draw either.

### 5.3 What this lane changed (edit-only, no gate, no commit)

- `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs:714-747` - `fillH` is `seatedPx / bandH`;
  `rowsSeated` added to the line; a new `MANAGE_GRID_DEAD_BAND` step names the unused fraction in
  words on every grid. A `Step`, not a `Warn`, and the code says why.
- `Assets/Editor/Regression/ManageMockupConformanceRegression.cs:1140`, `:1212-1274` -
  `CheckPickerDeadBand`, one new case `[research-picker-dead-band]`. It pins the honest `fillH` term
  and the dead-band line, then re-runs the renderer's own arithmetic for one row of five at
  `RefWellWidthPx` and **fails if the resolved cell is shorter than it is wide** - i.e. if anything
  other than the square ceiling shrank it.
  **RED proof:** restoring an absolute `cellH = Mathf.Min(cellH, MaxTileHeightPx)` ceiling resolves
  190px against a 358px width and the case fails; putting `rowsPx` back into the `fillH` term fails
  the first half.

### 5.4 Not done here, and why

The acceptance boxes stay unticked. No Unity gate and no fresh capture were run in this lane (another
seat holds the lock), so `REGRESSION_OK n/n` is unproven, and under **ruling 29** no seat-read frame
can close a Manage ticket regardless. The gold RESEARCH button needs a capture whose queue is NOT full
so at least one row is researchable.
