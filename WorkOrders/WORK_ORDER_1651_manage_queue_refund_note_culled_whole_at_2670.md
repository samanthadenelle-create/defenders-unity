# WO-1651 - The queue row's refund note is CULLED WHOLE at 2670x1200: the one player who gets nothing back is told nothing

**Status:** IMPLEMENTED - awaiting gate 2026-09-10 (fix + RED-first pin landed edit-only in a lane worktree off `dev` @ `736b6b4b9`; no Unity run, no gate, no commit - the lead gates and commits) *(was: minted 2026-09-10)*
**Minted:** 2026-09-10 (lane MANAGE-QUEUE-FIT; number **PRE-ASSIGNED by the lead** - this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`, whose banner already reads past this number)
**Silo / Lane:** Manage queue drawer - UI fit. Code only, one authoring line + one pin.
**Severity:** P2 player-visible, and it is the *consequence* line: the row that tells a player a cancel returns **nothing** is the row that draws nothing.
**Type:** EXISTING. The row ships this way; nothing here is a new feature.
**Raised by:** the first flow-map capture of the session, `Builds/wave5-manageflow1` (08:09, HEAD `2039e2c41`).
**Files owned:** `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` (the refund label's fit call only),
`Assets/Editor/Regression/ManageQueueDrawerRegression.cs` (one added case).
**Do NOT touch:** `ObsidianQueueVM.NoRefundLine` (the copy - it is model-owned and behind WO-1479's
ruling), `QRowRefund*` / `QueueTextX1` / `RowHeightPx` / `MinTouchPx`, the control cluster, the drawer
band table, `LayoutOracle.cs`, `UICaptureLaunch.cs`.

---

## 1. The measurement (read at source this session; nothing inferred)

`Builds/wave5-manageflow1` reports `MANAGE_FLOW_MAP_OK 20 frames` and `UI_GEOMETRY_OK 20`, and
**`UI_GLYPH_FAIL` x12 NEW** - the same label four times on each of
`ManageFlow_{BUILD,ARMY,RESEARCH}_queue_2670x1200`, **clean at 1920 and 2340**:

```
[glyph-oracle] TEXT CULLED WHOLE [ManageFlow_BUILD_queue_2670x1200 @2670x1200]
  'ObsidianPanel/PanelContent/Zone_Body/ManageQueueDrawer/Drawer_QueueList/ScrollZone/Viewport/Content/QueueRow/Label'
  ("No refund - nothing was paid for this job") draws ZERO of 33 printable glyphs.
  (x -584.4..-157.8, y -54.3..-22.4) at font 30 [autosize 30..32, enabled=True]
  overflow=Ellipsis wrap=NoWrap isTextTruncated=True
```

(the third row's lane is wider, `x -686..-157.8`, because it carries no position marker or thumbnail).

⛔ **ZERO glyphs is a VERTICAL cull, not an ellipsis.** An over-long `Ellipsis` label still draws what
fits plus `...`; drawing *nothing* means the line box did not fit the rect's HEIGHT and TMP culled the
whole line. That is the `0 visible glyphs` class `ElarionUiKitObsidian` §1.14 exists to catch.

## 2. The producer, identified by its autosize band and not by its name

`ManageScreenPanel.AddQueueRow` builds three same-named `Label` children, so the oracle's path is
ambiguous - the **autosize band disambiguates it**, at source:

| label | fit call | resolved band | matches the capture's `[30..32]`? |
|---|---|---|---|
| `name` | `FitSingleLine(name, 0f, QueueNameFontPx)` | 30..**36** | no |
| `state` (timer) | `FitSingleLine(state, QueueStateFontFloorPx, QueueLineFontPx)` | **24**..32 | no |
| **`refund`** | **`FitSingleLine(refund, 0f, QueueLineFontPx)`** | **30..32** | **YES** |

The string is `ObsidianQueueVM.NoRefundLine` (`:229`), reaching the row as `QueueRowVM.RefundText`
(`ManageScreenVM.cs:1123`, via `QuoteRefund`).

## 3. Root cause - WO-1488 fixed this exact bug on the sibling and stopped there

`0f` resolves to the kit's `FontFloor` (**30**) against a 32px max - two points of headroom. The
in-code comment two blocks above the defect already says so, about the *other* label:

> `// WO-1488: the TIMER line, fitted to its own floor. `0f` here resolved to FontFloor(30) against a 32px max - two points of headroom - and the capture ellipsised at "(0% do...".`

`state` was moved to `QueueStateFontFloorPx` (24). **`refund`, which shares the identical band height,
was left on `0f`.** The pair drifted, and the sibling with the tighter floor requirement kept the
looser floor.

### Why only 2670 - arithmetic that closes on the captured pixel

`_queueRowPx = Mathf.Clamp(ideal, ElarionUiKit.MinTouchPx, RowHeightPx)` (`:6725`). At this aspect the
row is pinned to the **touch floor, 112**. The refund band is
`QRowRefundY1 - QRowRefundY0 = 0.378 - 0.093 = 0.285` of the row:

**0.285 x 112 = 31.92 px** - against the captured **31.9 px** (`y -54.3..-22.4`). To a tenth of a pixel.

At 1920/2340 `ideal` clears 112, the row is taller, the same fraction seats the line, and the oracle is
clean - which is exactly the aspect split the run reports.

## 4. ⛔ THE ANSWER TO "IS THE BAND UNDER THE TWO-LINE NEED": IT IS UNDER THE **ONE**-LINE NEED

**31.9 px does not seat ONE line at the 30px floor.** So a two-line block is the WRONG SHAPE here - two
lines want roughly twice this band inside 31.9 px, and wrapping would trade a culled line for a culled
second line.

The line factor is bounded **by this same run's own two labels**, no font-asset guess required:

| label | band | floor | outcome in the run | bound |
|---|---|---|---|---|
| `name` | `(0.996-0.679) x 112` = **35.5 px** | 30 | renders (not flagged) | `30f <= 35.5` -> **f <= 1.183** |
| `refund` | `0.285 x 112` = **31.9 px** | 30 | **CULLED WHOLE** | `30f > 31.9` -> **f > 1.063** |

So one line at 30 needs between 31.9 and 35.5 px, and the band is 31.9 - short by a hair, which is
precisely why it culls here and nowhere else.

**At the row's own floor of 24** the line box is at most `24 x 1.183 = 28.4 px` against 31.9 px of band:
it clears by **>= 3.5 px (+11%)**, and 24 sits above `ElarionUiKit.FontHardFloor` (20).

## 5. The fix

One line, and it is the idiom the row already owns:

```
- ElarionUiKit.FitSingleLine(refund, 0f, QueueLineFontPx);
+ ElarionUiKit.FitSingleLine(refund, QueueStateFontFloorPx, QueueLineFontPx);
```

⛔ **No font goes below the kit floor** (24 > `FontHardFloor` 20, and it is the floor the sibling label
one block away already uses). ⛔ **No player copy is shortened** - `NoRefundLine` is untouched; changing
it would need a ruling.

## 6. ⚠ Two findings raised BEYOND the fix, both report-only

1. **The kit's whole-cull safety net produced NO line on a whole cull.** `FitSingleLine` calls
   `ArmFitGuard`, and `UiKitTextFitGuard.LateUpdate` is built to catch exactly this - it relaxes
   `fontSizeMin` toward `FontHardFloor` and `FlowTrace.Fail`s if the label still renders zero glyphs.
   **`grep TextFitGuard` over `wave5-*` and `wave3-capture*` returns 0 lines, total.**
   **-> RAISED AS `WorkOrders/WORK_ORDER_1652_uikit_textfitguard_never_fires_in_headless_captures.md`.**

   ⛔ **CORRECTION 2026-09-10, SAME DAY, AND IT IS MINE.** This bullet first read *"The capture does
   enter play mode (`UICaptureLaunch.cs:535-536`), so `ArmFitGuard`'s `!Application.isPlaying`
   early-out is not the explanation"*, and called the cause unproven. **That citation was the WRONG
   ENTRY POINT and the claim is disproven.** `:536` is the ONLY `EnterPlaymode` in the file and it sits
   in the *interactive* path, whose `[UICap] capture requested -> entering Play mode` line has **0
   matches** in this capture's log. The run was `RunManageFlowMapCaptureHeadless`
   (`UICaptureLaunch.cs:8728`), whose own docstring says it renders **"WITHOUT entering Play mode"**.
   So `Application.isPlaying` is FALSE, `ArmFitGuard` returns at `ElarionUiKitObsidian.cs:3096`, and
   the guard is never attached - with a second, independent lock behind it: the guard is frame-driven
   (`LateUpdate`, and `_frames++ < 1` means it needs a SECOND tick), while the harness has no
   `yield return null` anywhere and tears down with `DestroyImmediate`. The cause is now **PROVEN**.
   I read a fact off an adjacent line; the correction is recorded rather than edited away.
2. **Width is a separate axis and is NOT MEASURED - a note, deliberately not a ticket.** The fix above
   is a HEIGHT fix: it stops the whole-line cull, which is what the capture measured. It does not
   claim the sentence then fits horizontally. The oracle logs a rect only for labels it FAILS, so
   **"No refund - nothing was paid for this job" has never been measured at 24px**, and its lane is
   **426.6 px** on rows 1-2 (`x -584.4..-157.8`) and **528.2 px** on row 3 (`x -686..-157.8`, which
   carries no position marker or thumbnail). A 40-character sentence in that lane is not obviously
   comfortable, and saying so now is cheaper than discovering it later.
   **The next capture decides it, and there are only two outcomes:**
   * **33 of 33** - done, nothing further; the height fix was the whole defect.
   * **an ELLIPSIS** (`n of 33`, n > 0) - that is a NEW, HORIZONTAL finding, not this one returning,
     and it has its own untouched levers: `x0` and `QueueTextX1` (0.40 - the text column is 40% of the
     row, and the control cluster owns the rest, so widening it trades against `MinTouchPx` and needs
     its own measurement).
   ⛔ **It is NOT raised as a ticket now, on purpose:** raising a ticket for a defect no run has
   observed is the guess this repo bans. It is written here so the next reader of the capture knows
   which of the two outcomes is expected and which one is new.

3. ⚠ **WHETHER THIS CULLS FOR A PLAYER IS UNPROVEN, AND WO-1652 IS WHY.** The capture runs in EDIT
   mode, where `ArmFitGuard` never attaches (finding 1, now proven). A real player build DOES run
   `UiKitTextFitGuard`, which relaxes `fontSizeMin` toward `FontHardFloor` (20) and could rescue this
   label at ~26px before the player ever sees a gap. **So the SEVERITY line at the top of this ticket -
   "the row that tells a player a cancel returns nothing draws nothing" - is proven for the CAPTURE and
   NOT proven for the DEVICE.** The fix stands on its own merits either way: a label should fit its
   authored band without a rescue, and the sibling timer line has been fitted to this exact floor since
   WO-1488. But do not let this ticket be cited as evidence of a shipped player-facing defect until
   WO-1652's trace has counted the arms and relaxations in a real build.

## 7. Acceptance

1. A fresh capture shows `ManageFlow_{BUILD,ARMY,RESEARCH}_queue_2670x1200` with the refund note
   drawing **33 of 33** printable glyphs, and 1920/2340 still clean.
2. `UI_GLYPH_FAIL` on nothing new; the twelve findings are gone, not baselined.
3. The PNGs are opened and the sentence is READ - the owner is colourblind and reads words, and a
   glyph count is a number.
4. `ManageQueueDrawerRegression`'s new `[rows-inside-the-plate]` refund case passes, and FAILS if the
   `0f` form is restored (RED mutation stated in-file).
5. No font floor lowered; no player copy shortened.
