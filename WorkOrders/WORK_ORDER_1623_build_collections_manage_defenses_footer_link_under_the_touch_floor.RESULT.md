# WO-1623 RESULT - the Build Collections footer link is authored AT the kit touch floor, in pixels

**Status:** IMPLEMENTED - awaiting gate (lane FOOTER 2026-09-10)
**Lane:** FOOTER (edit-only worktree `.claude/worktrees/agent-a945f93ff422c338b`, base `bb12c728e`)
**Gate / commit:** NOT RUN BY THIS LANE. Edit-only per the lane brief; the lead gates and commits.

---

## 1. What was wrong, and what the evidence said

Read this session out of `Builds/wave1-capture` (`tr -d '\000' < Builds/wave1-capture | grep -a
UI_GEOMETRY_FAIL` and the `[UICap-GEO]` / `[touch-oracle]` lines):

```
[UICap-GEO] SUB-TOUCH-FLOOR BAND [BuildCollections_1920x1080 @1920x1080] 'ObsidianPanel/PanelFill/Zone_Body/ManageDefensesFooterLink' resolves 666.8x60.3 ref px -- shortest side 60.3 is 51.7 px UNDER ElarionUiKit.MinTouchPx (112). ...
[UICap-GEO] SUB-TOUCH-FLOOR BAND [BuildCollections_2340x1080 @2340x1080] ... resolves 736.3x53.3 ref px -- shortest side 53.3 is 58.7 px UNDER ...
[UICap-GEO] SUB-TOUCH-FLOOR BAND [BuildCollections_2670x1200 @2670x1200] ... resolves 746.2x52.4 ref px -- shortest side 52.4 is 59.6 px UNDER ...
UI_GEOMETRY_FAIL x3 over 91 canvases -- see the [UICap-GEO] lines above; each names the panel, the element and the numbers.
```

The same three lines print again under `[touch-oracle]`, which is the WO's own proof that
`BuildCollections` is **not** on the baseline allow-list.

**The band was a pure fraction** (`BuildCollectionBrowser.cs`, pre-edit `:281-283`:
`anchorMin.y = .05f`, `anchorMax.y = .155f`) - `.105` of `Zone_Body`, whose height changes with the
aspect. `ElarionUiKit.MinTouchPx = 112f` (`Assets/_Modules/Core/UI/ElarionUiKit.cs:347`, read at
source this session) is a number of pixels on a finger, so no single fraction can satisfy it across
three aspects. The width was never short (666-746 ref px on every frame).

## 2. Per-file changes

### `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`

1. **New px constants** (after `MissingImageCopy`, `:40-73`; the constants themselves at `:63`, `:65`, `:69`, `:71-72`), each with the reasoning above them:
   - `FooterLinkBandPx = ElarionUiKit.MinTouchPx` - the floor is READ from its one owner, never
     retyped as `112` (WO section 3).
   - `FooterLinkBottomInsetPx = 12f` - band bottom edge to the body zone's bottom edge.
   - `FooterLinkGridGapPx = 16f` - band top edge to the category grid. Chosen comfortably above
     `LayoutOracle.OverlapPadPx` (`Assets/_Modules/Core/UI/LayoutOracle.cs:52`, `= 2f`, read this
     session) so the fix cannot trade one `SUB-TOUCH-FLOOR BAND` finding for a set of
     `BUTTONS OVERLAP` ones against the seven category cards.
   - `FooterLinkReservePx = 12 + 112 + 16 = 140f` - what the footer costs the grid, bottom-up.
2. **The category grid's bottom edge stops being a fraction** (`RenderCategories`, `:161-175`; the `Region` call at `:169`, the px offset at `:170`).
   Was `Region("CategoryGrid", new Vector2(.02f, .18f), new Vector2(.98f, .84f))`; now anchored at
   `0f` on Y with `grid.offsetMin = new Vector2(0f, FooterLinkReservePx)`. The top edge (`.84f`),
   the side insets (`.02f/.98f`) and the layout group are untouched. A `FlowTrace.Step` prints the
   reserve and its three terms.
3. **The footer band is authored in px** (`BuildManageDefensesFooterLink`, `:284-298`; anchors `:294-295`, pivot `:296`, offsets `:297-298`): both Y
   anchors collapse to `0f`, `pivot` is set to `(.5f, 0f)` **before** the offsets (moving a pivot
   afterwards preserves `sizeDelta`/`anchoredPosition` and would slide the rect back off the
   floor), then `offsetMin = (0, 12)` and `offsetMax = (0, 12 + MinTouchPx)`. X stays `.28f-.72f`,
   because the width was never the defect. Shape mirrors the codebase's existing pixel-band idiom,
   `UICaptureLaunch.MakePixelBand` (`Assets/Editor/UICaptureLaunch.cs:4736`) - read, not called
   (editor file).
4. **A new `FlowTrace.Step`** naming the authored band and the retired fraction (`:316-321`). The
   pre-existing WO-1411 trace, the `Guard.Try` wrapper and the tap trace are all untouched
   (CLAUDE.md section 12 - instrumentation is permanent).
5. **Two comments corrected rather than left lying** (CLAUDE.md section 15): the WO-1411 rationale
   block said *"(the grid ends at y .18)"*, which this change makes false - the paragraph now
   records what replaced it and why, and the ruling itself is preserved verbatim.

### `Assets/_Modules/Core/UI/LayoutOracle.cs` (WO section 4 Step 1 - MEASURE, do not derive)

Assert A's `SubTouchFloorBand` message now also names the offender's **host rect, measured**, and
the fraction of that host the floor would need (`LayoutOracle.cs:177-202`; the host block `:185-194`, the finding `:195-202`). Rationale in-code: WO-1623 section 1d
had to publish a table labelled *"derived, not measured"* because the log printed the resolved band
but never its parent, so anyone authoring a fix off a capture log was inferring the one number the
layout depends on. This is additive to the message only - `FindingKind`, the trigger condition
(`shortest >= ElarionUiKit.MinTouchPx - 0.5f`) and every other assert are unchanged.

`UiTouchClampRegression.CaseSubFloor` (`Assets/Editor/Regression/UiTouchClampRegression.cs:134-149`)
asserts the message contains `slot-chip-0` and `UNDER`, and does **not** contain `healthy-cta`. The
appended text adds the offender's PARENT path (`SubFloorHost` in that synthetic canvas), so all
three assertions still hold.

## 3. Suite

**No new suite was written, deliberately.** WO section 6 states the RED-first proof already exists and
is a MEASURED one: `LayoutOracle.Audit` Assert A (`LayoutOracle.cs:168-203`), driven by the capture
harness, verdict `UI_GEOMETRY_FAIL x3` on `Builds/wave1-capture`. Adding a source-text lint beside a
measured oracle would be the weaker duplicate of a check that cannot go stale.

## 4. What is NOT proven, and what would prove it

- ⛔ **NOTHING HERE IS MEASURED POST-EDIT.** This lane did not run Unity, the compile gate, the
  regression suites or a UI capture. Every claim above is source read at the paths given; the
  geometry claim is arithmetic on the constants, not a capture.
- **The proof is a FRESH UI capture run by the lead:** `UI_GEOMETRY_OK` with `BuildCollections`
  measured at 1920x1080 / 2340x1080 / 2670x1200, and **zero** `[touch-oracle]` lines naming
  `ManageDefensesFooterLink`. Judge by the marker on a fresh log, never an exit code.
- **Open the 2670x1200 frame** (`Builds/ui-capture/BuildCollections_2670x1200.png`) - owner standing
  rule. It is the aspect with the shortest body zone and therefore the one where the grid gives up
  the most room.
- ⚠ **A VISIBLE CHANGE THE FRAME MUST BE JUDGED ON: the seven category cards get SHORTER.** The grid's
  bottom edge moves from `.18` of the body zone to a flat 140 px above it. Using the host heights
  the capture implies (band px / `.105`: ~574 / ~508 / ~499 ref px), the reserve grows from `.18 x 574 = ~103` / `.18 x 508 = ~91` / `.18 x 499 = ~90` px to a flat
  140 px, so the grid band (top edge `.84`) loses roughly **8% / 13% / 14%** of its height and
  `childForceExpandHeight` passes that to the cards. That is the price of a real touch floor in the
  same panel; if the owner judges the cards too small, the follow-up is the panel's height or the
  `.84f` top edge, not the floor.
- **`BUILD_COLLECTION_PLAYER_OK` must stay green.** Verified by string search this session that all
  three pinned literals survive byte-identical (`link.name = "ManageDefensesFooterLink"`, the caption
  `"Already built? Manage defenses >"`, `PanelRouter.Open(PanelId.Manage, "Defense")`), that the
  retired 8th-card literal was **not** reintroduced, and that no `TextOverflowModes.Ellipsis` /
  `Truncate` literal entered this file. That is a source-text check, not a suite run.
- **The mutation that reproves RED** (for the RESULT record, per WO section 6): restore
  `rt.anchorMax = new Vector2(.72f, .155f)` with `anchorMin.y = .05f` and zeroed offsets, and the
  three `SUB-TOUCH-FLOOR BAND` lines return.

## 4b. Blast-radius sweeps run this session (source reads, not suite runs)

- **Nothing else in the tree matches on the SubTouchFloorBand message tail.** The capture harness
  classifies the finding by `f.StartsWith("SUB-TOUCH-FLOOR BAND", ...)`
  (`Assets/Editor/UICaptureLaunch.cs:5918-5924`), i.e. by PREFIX, so appending to the end of the
  message cannot move a finding between the geometry and touch buckets, nor into or out of the
  baseline branch (`:5915`, `:5932-5934`). No file outside `LayoutOracle.cs` matches the phrase in a
  way that reads it as source text.
- **No local shadowing in `Audit`.** The new locals (`host`, `parentRt`, `pr`, `needFrac`) are the
  only occurrences of those names in the file; `Audit`'s own locals are `found`, `root`, `at`,
  `texts`, `buttons`, `canvasRect`, `canvasArea` (`LayoutOracle.cs:86-99`).
- **The other suites that read `BuildCollectionBrowser.cs` pin behaviour, not geometry.**
  `BuildAffordabilityWordsRegression` (`:122-127`) asserts the Manage-defense door is a FOOTER LINK
  and not a card - unchanged; `BuildFirstUseGuideRegression:49`, `CardCollectionFoundationRegression:73`
  and `PlacedStructureDoorRegression:124` read the file for guide/catalog/door seams. None reads
  `.18f`, `.155f` or `CategoryGrid`.
- **Zone_Body has no other low-Y occupant to collide with.** `ObsidianNavigationWorkspace` parents
  the subtitle and BACK to `chrome.content`, not to the body zone (`:133-146`), and
  `RenderCurrent` destroys every child of the body before `RenderPage` (`:180-205`) - so on the root
  page the zone holds exactly the grid and this footer link.

## 5. Follow-up, NOT taken here (out of WO-1623 scope, section 7)

`BuildCollectionBrowser.cs:312` sets `label.fontSizeMin = 16f` on the footer caption. The kit's own
floor is `ElarionUiKit.FontHardFloor = 20f` (`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3044`,
read this session), and `FitSingleLine`/`FitBlock` clamp a sub-floor `minSize` UP to it for exactly
this reason (`:3061-3063`). The literal bypasses that clamp because it is written straight onto the
TMP component. It is left in place with an in-code note: WO-1623's target is the BAND, and a caption
legibility change is a separate ruling. **It is now dormant rather than load-bearing** - a 112 px band
gives the autosizer room to sit at `fontSizeMax`, so the sub-floor minimum should not be reached.
Routing it through `FitSingleLine` would also switch the label to `Ellipsis` overflow, which is a
behaviour change this ticket has no mandate for. Worth its own small ticket.

## 6. Paths

- WO: `WorkOrders/WORK_ORDER_1623_build_collections_manage_defenses_footer_link_under_the_touch_floor.md`
  (Status flipped in this lane's worktree copy)
  ⚠ **RECONCILE, DO NOT BLIND-REPLACE.** At base `bb12c728e` this WO existed only as an UNCOMMITTED
  file in the shared checkout; the worktree copy is a snapshot taken from it this session, and the
  shared copy may have been revised since (its own section 5 records one mid-flight correction
  already). Take the `**Status:**` line from this copy, or diff the two before replacing.
- RESULT: `WorkOrders/WORK_ORDER_1623_build_collections_manage_defenses_footer_link_under_the_touch_floor.RESULT.md`
- Code: `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`,
  `Assets/_Modules/Core/UI/LayoutOracle.cs`
