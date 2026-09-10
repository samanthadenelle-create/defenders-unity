# WO-1651 RESULT - the queue refund note, culled whole at 2670

**Base:** `dev` @ **`736b6b4b9`** (worktree `D:\EoA\.claude\worktrees\agent-a1c87f271103479d0`,
ff-merged from `a96bfe332`; the six files this worktree still showed as modified were byte-identical to
dev's committed copies - proven by their absence from `git diff --numstat refs/heads/dev` - and were
stashed rather than discarded before the merge).
**EDIT ONLY** - no Unity run, no gate, no commit, no capture. The lead gates and commits.

## 1. What was measured, before anything was edited

Instrumentation first (§12): the log was read before the code. `Builds/wave5-manageflow1`,
1,307,056 bytes, UTF-8, opened this session.

* `MANAGE_FLOW_MAP_OK 20 frames`, `UI_GEOMETRY_OK 20`, and **`UI_GLYPH_FAIL` x12 NEW**.
* The finding, four times on each of `ManageFlow_{BUILD,ARMY,RESEARCH}_queue_2670x1200`, **clean at
  1920 and 2340**: `TEXT CULLED WHOLE ... 'QueueRow/Label' ("No refund - nothing was paid for this
  job") draws ZERO of 33 printable glyphs. (x -584.4..-157.8, y -54.3..-22.4) at font 30
  [autosize 30..32] overflow=Ellipsis wrap=NoWrap isTextTruncated=True`.
* ⛔ **ZERO glyphs = a VERTICAL cull.** An over-long `Ellipsis` label still draws what fits plus `...`.

## 2. The producer, identified by its autosize band

`AddQueueRow` builds three same-named `Label` children, so the hierarchy path alone is ambiguous. The
band `[30..32]` disambiguates it at source: `name` resolves 30..36, `state` 24..32, and **`refund`
30..32** - the only match. String = `ObsidianQueueVM.NoRefundLine` (`:229`) via
`QueueRowVM.RefundText` (`ManageScreenVM.cs:1123`).

## 3. Root cause

`0f` -> the kit's `FontFloor` (30) against a 32px max. **WO-1488 already fixed this exact bug on the
sibling `state` label and left `refund`, which shares the identical band, on `0f`** - the file's own
comment two blocks up documents the fix it did not propagate.

**Why only 2670, closing on the captured pixel:** `_queueRowPx = Clamp(ideal, MinTouchPx, RowHeightPx)`
(`:6725`) pins the row to the touch floor **112** at this aspect; the band is
`QRowRefundY1 - QRowRefundY0 = 0.285`, and **0.285 x 112 = 31.92 px** against the captured **31.9 px**.

## 4. THE SHAPE QUESTION, ANSWERED: the band is under the ONE-line need

**31.9 px does not seat one line at the 30px floor**, so a two-line block is the wrong shape - it would
want ~2x this band inside 31.9 px. Bounded by the run's own two labels, no font-asset assumption:

| label | band | floor | outcome | bound |
|---|---|---|---|---|
| `name` | 0.317 x 112 = **35.5 px** | 30 | renders | `f <= 1.183` |
| `refund` | 0.285 x 112 = **31.9 px** | 30 | **culled** | `f > 1.063` |

At floor **24**: line box `<= 24 x 1.183 = 28.4 px` vs 31.9 px band -> clears by **>= 3.5 px (+11%)**.

## 5. Files changed

| file | change |
|---|---|
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` | the refund label's fit call `0f` -> `QueueStateFontFloorPx`, with the measurement, the 0.285x112 arithmetic and the one-line-vs-two-line finding recorded at the site |
| `Assets/Editor/Regression/ManageQueueDrawerRegression.cs` | one added `[rows-inside-the-plate]` case, seated beside its `state` sibling so the pair cannot drift apart again |

```diff
- ElarionUiKit.FitSingleLine(refund, 0f, QueueLineFontPx);
+ ElarionUiKit.FitSingleLine(refund, QueueStateFontFloorPx, QueueLineFontPx);
```

## 6. RED-first, PROVEN against HEAD

The new pin requires `FitSingleLine(refund, QueueStateFontFloorPx, QueueLineFontPx)`. Read out of the
`736b6b4b9` blob this session:

```
HEAD new-pin form : 0   -> the case FAILS on the pre-fix tree = RED-first proven
HEAD old form     : 1   -> FitSingleLine(refund, 0f, QueueLineFontPx)
HEAD state sibling: 1   -> the WO-1488 pin it is modelled on
```

RED mutation is stated in-file: restore `FitSingleLine(refund, 0f, QueueLineFontPx)`.

## 7. Gate hygiene

`python tools/gate_brace.py` on both files: **`GATE_BRACE_SUMMARY bad=0 of 2`, exit 0**.
NUL bytes 0 / 0. Raw braces 426/426 and 26/26. Both files uniform CRLF (7341/7341 and 915/915),
**bare-LF 0**. No gate-marker strings introduced in `Assets/`.

## 8. Two findings raised BEYOND the fix - report only

1. ⚠ **The kit's whole-cull safety net produced NO line on a whole cull.** `FitSingleLine` arms
   `UiKitTextFitGuard`, which is built to relax toward `FontHardFloor` and `FlowTrace.Fail` on a
   zero-glyph render. **`grep TextFitGuard` over every `wave5-*` and `wave3-capture*` log returns 0
   lines, total.** The capture *does* enter play mode (`UICaptureLaunch.cs:535-536`), so the guard's
   `!Application.isPlaying` early-out does not explain it. **Why it never runs in a capture is NOT
   PROVEN and is deliberately not guessed at.** It is bigger than this ticket: captures are measuring
   the un-guarded layout, and a real whole-cull produced no `Fail` line for a seat to triage. Worth its
   own ticket.
2. ⚠ **Width is unproven.** The oracle logs a rect only for labels it FAILS, so this sentence's width
   at 24px in its 426.6 px lane has never been measured. If the next capture ellipsises it, that is a
   NEW finding with its own levers (`x0` / `QueueTextX1`), not this one returning. Said here rather
   than discovered later.

## 9. What the lead does next

1. Gate the combined tree once (`COMPILE_GATE_OK` on a fresh log, marker not exit code).
2. Re-run the flow-map capture; expect the refund note at **33 of 33** on all three
   `*_queue_2670x1200` frames, 1920/2340 still clean, `UI_GLYPH_FAIL` on nothing new.
3. **Open the PNGs and READ the sentence** - acceptance item 3; a glyph count is a number.
4. Flip WO-1651 to FIXED in the same commit as the work, regenerate `BOARD.html`.
