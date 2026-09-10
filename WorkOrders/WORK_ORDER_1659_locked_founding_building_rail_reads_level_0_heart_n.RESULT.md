# WO-1659 RESULT — STOPPED. The ticket targets DEAD CODE; there is no live defect.

**Status:** ⛔ STOPPED BEFORE IMPLEMENTATION — NO LIVE DEFECT. **No code was written.**
**Lane:** MANAGE-VM (isolated worktree `.claude/worktrees/agent-a6afae9850478e00d`, branch `dev` @ `aba49bd4c` + the uncommitted WO-1657 item B diff)
**Date:** 2026-09-10
**No Unity run, no commit.**

> ## ⛔ I WROTE WO-1659, AND WO-1659 IS WRONG. THE MISS IS MINE.
> I minted it from a grep of `LockText` + the two `railState` sites and read those expressions at
> source — but I **never checked whether the method containing them still runs.** It does not. My own
> WO §8 listed *"whether any OTHER surface reads `BuildingChoiceVM.Level` raw — that is a grep, not
> an exhaustive audit"* as unproven; the thing that actually needed proving was one level up, and I
> did not name it. Recorded, not smoothed over (CLAUDE.md §11B).

---

## 1. WHY I STOPPED INSTEAD OF IMPLEMENTING

The brief was explicit: widen the seam, route both rail branches, pin it. **The first instruction was
to MEASURE the rail band.** Measuring is what killed the ticket — before the band width mattered, the
frame showed there is no rail on screen at all, and the source then said why.

⛔ **§12's hard gate cuts both ways.** It forbids editing without data proving the cause; the data
here **disproves the premise**. Implementing anyway would have:
- changed **no pixel** a player can see,
- added a regression case asserting on an **unreachable path**, making the removal the code itself
  asks for *harder*, and
- produced a RESULT claiming a live defect was fixed — the exact lie §11B is written against.

§11B(B) says deviating from a written instruction needs permission **asked in advance**; it also says
*"If the procedure is wrong or stale, SAY SO and get a ruling."* This is that case, so the work stops
here with the proof attached rather than being quietly reinterpreted.

---

## 2. THE PROOF — one chain, every link read at source this session

| # | Site | What it establishes |
|---|---|---|
| 1 | `ManageScreenPanel.cs:1595` | `BuildWorkspaceHost(well);` — a **plain statement in the build sequence**, between `BuildNotice(...)` (`:1587`) and `BuildQueueDrawer(well)` (`:1597`). ⚠ Enclosing block **opened and read** (`:1578-1600`): **no `if`, no feature flag, no branch of any kind.** |
| 2 | `ManageScreenPanel.cs:1763-1773` | `BuildWorkspaceHost` always assigns **both** `_workspaceHost` and `_workspace` |
| 3 | `ManageScreenPanel.cs:1761` | `WorkspaceActive => _workspace != null && _workspaceHost != null` ⇒ **always true**. ⚠ Checked for a later reset: the ONLY assignments to either field are the two in `BuildWorkspaceHost` (`:1767`, `:1772`) and the null-out in **`Close()`** (`:1231-1232`) — which is TEARDOWN, and nulls `_vm` and `_listContent` in the same breath, so it cannot yield a live rail (`RenderList` returns at `if (_listContent == null)` and `Render` is already unsubscribed). |
| 4 | `ManageScreenPanel.cs:4688` | `RenderList()` opens: **`if (WorkspaceActive) { RenderWorkspace(); return; }`** |
| 5 | `ManageScreenPanel.cs:4710` | `RenderBuildingsDestination(channel);` — **after** that return ⇒ never reached |
| 6 | `:4906` → `:4938` → `:4989` | ⇒ `AddBuildingWorkspaceRow` → `BuildBuildingRailRow` → **`:5024` and `:5023` never execute** |

### The code had already written this down, and I had not read it
`ManageScreenPanel.cs:4681-4684`, on `RenderList` itself:

> *"⚠ It is nevertheless **DEAD CODE UNDER GREEN PINS** - the exact shape
> `ManageQueueDrawerRegression:103-113` exists to catch - so its removal, and the pin moves that must
> precede it, are itemised in this work order's hand-back. **Do not leave it here indefinitely.**"*

### Confirmed on a FRAME and on a RUN, not only at source
- **The frame.** `Builds/device-frames/2026-09-10_0915b_363722_build_detail_quarry_placed.png`,
  **opened this session**: a full-bleed detail card — portrait left, stats right, one `UPGRADE` face.
  **There is no rail anywhere on it.** That is `ManageWorkspacePanel`'s shape, not the rail+card split
  `AddBuildingWorkspaceRow` builds. (It also shows `Level 0 of 4` top-left — WO-1657 item B's target,
  on the surface that *is* live.)
- **The run.** `Builds/wave6-manageflow1`: `grep -ac "building rail selected="` → **0**. Every BUILD
  screen logs `[Flow:Manage] workspace screen=Detail/BUILD item='…'`, i.e. the workspace renderer.

---

## 3. AND BOTH LIVE SURFACES ARE ALREADY CORRECT

| Live surface | Level line | Status |
|---|---|---|
| Detail card | `ManageVmProjection.cs:319-322` | ✅ **fixed by WO-1657 item B** (the founding seam) |
| Grid tile | `ManageVmProjection.cs:210` | ✅ **never had the bug** |

The tile already reads:

```
Subtitle = item.MaxLevel > 0 && item.Level > 0
    ? "LEVEL " + item.Level
    : (string.IsNullOrEmpty(item.NextRungLine) ? null : item.NextRungLine),
```

— it guards `Level > 0` and falls back to the effect sentence, and its own comment cites **ruling
3.7, *"never paint LEVEL 0"***. So the tile was correct before either ticket.

> ## ⇒ AFTER WO-1657 ITEM B, NO LIVE MANAGE SURFACE PAINTS THE TIER-0 SENTINEL AS A LEVEL.
> WO-1659 would fix nothing a player can reach.

---

## 4. WHAT I DID MEASURE BEFORE STOPPING (the brief's first instruction)

Recorded because it is real, and because it is what the rail would need **if it is ever revived**:

- **Rail face height: `TroopRailRowPx = 112f`** (`ManageScreenPanel.cs:307`, `== MinTouchPx`).
- **Sub-line band: `(0.48f - 0.06f) x 112 = 47.04 px`** (`ManageScreenPanel.cs:5028-5030`).
- **Fit: `FitSingleLine(sub, 22f, 30f)`** → `ElarionUiKitObsidian.cs:3054-3070`: `NoWrap` +
  **`Ellipsis`**, auto-size clamped to `[22..30]`. `22 > FontHardFloor(20)` so the 22 stands; the
  authored role is `ElarionUi.FontMicro = 32` (`ElarionUi.cs:115`). **So an over-long rail line
  ellipsizes rather than shrinking** — which is why the brief was right to ask for a measurement.
- **Label width: `0.30f..0.84f` = 0.54 of the face**, and the face is the full width of
  `BuildingSelectorRail`, a **0.26** slice of the workspace (`:4941`).

⚠ **I did NOT resolve that 0.26 slice to reference px, and I am not going to guess it.** It needs the
drawer→well→workspace fraction chain plus the canvas reference resolution. Since the surface is dead,
finishing that measurement would price a band nothing draws. **So the question the brief asked —
"does `Not yet upgraded . 4 levels` seat in the rail?" — is UNANSWERED, and it is moot unless the rail
is revived.** The band HEIGHT above is measured; the WIDTH verdict is not.

---

## 5. THE REAL FINDING — worth more than the ticket

`ManageScreenPanel`'s retired per-destination chrome (Buildings / Troops / Defense / Research) is
dead code, **and its own comment asks for it to be removed**, with the required pin moves itemised.
**That removal is a genuine ticket**, and it would delete `:5024` and `LockText` outright — the
correct end state for both halves of WO-1659, reached by deletion rather than by teaching a dead
branch a new string.

⚠ **It is a REMOVAL under GREEN PINS, and I opened them rather than repeating the comment's
citation:**
- `ManageQueueDrawerRegression.cs:96-108` **`[rows-not-inline]`** extracts
  `Body(panel, "private void RenderList()", "private string FindSummary")` and asserts on its
  contents — so it **reads the very method that is dead**, and FAILS outright if `RenderList`'s body
  cannot be located.
- `ManageQueueDrawerRegression.cs:110-113` **`[rows-have-a-home]`** counts definitions vs callers of
  `AddQueueRow(` — it is precisely the *"private method with zero callers is dead code that LOOKS
  like a shipped feature"* detector the `RenderList` comment names.
- `ManageBuildingsCardRegression.CheckPanelSource` scans `BuildBuildingCard`'s body for
  `selected.StateWord` — also inside the retired chrome.

Pins must move **before** the delete, or the suites go red on correct work. That needs the lead's
scoping — **not something this lane starts unasked**, and deliberately not started.

---

## 6. DECISION NEEDED FROM THE LEAD / OWNER

1. **Close WO-1659 as NO-OP** — nothing live to fix; §3 shows both live surfaces are already correct. *(My recommendation.)*
2. **Or re-scope WO-1659** to the dead-chrome removal in §5, pin moves first.

⛔ **What must NOT happen:** implementing WO-1659 as written. It would add a pin to an unreachable
path and stand directly in the way of the removal the code is asking for.

---

## 7. STATE OF THE TREE

**Nothing was written for WO-1659.** No `.cs` file was touched this turn.
`git status` carries only the **WO-1657 item B** work (already taken into the main tree by the lead)
plus this WO's Status flip and this RESULT. `gate_brace` and the NUL scan were re-run over the three
`.cs` files still modified in this worktree and are clean — unchanged from the item B hand-back.

⚠ **BOARD NOTE for the lead:** this WO's `**Status:**` now begins
`⛔ STOPPED BEFORE IMPLEMENTATION - NO LIVE DEFECT`, which is **not one of the strings
`tools/board_build.py` normally recognises**. It may need mapping (or a known status word chosen)
when the board is regenerated — flagged rather than silently left to render as unknown.

**Also corrected in the same breath (§15):** WO-1659's own §2A heading, which labelled the rail
expression **LIVE**. It now carries a `⛔ NOT LIVE — SUPERSEDED` banner pointing at the stop box, with
the original reasoning left unrewritten as the record of the miss.
