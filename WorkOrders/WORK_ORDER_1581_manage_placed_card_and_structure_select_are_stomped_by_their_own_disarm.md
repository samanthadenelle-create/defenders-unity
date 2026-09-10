# WO-1581 - "Manage Placed" card and tap-a-structure select are both stomped by their own disarm, so MOVE is unreachable

**Status:** IMPLEMENTED - 55d3a7c56 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Build / BuildMode (palette seam + placed-structure edit verbs)
**Minted from:** `CLI_LANES_WO_NUMBERS.md` banner, hundred-and-sixteenth pass (1581 -> 1582, same edit)
**Source:** owner device report 2026-09-07 08:3x on build 2026.09.07.359076 - *"manage buildings
doesnt do anything"*; coordinator priority - *"prioritize the move for the build as the tester has
asked a few times now"*. Frame `Logs/device/screens/owner-screen-20260907-005742.png`.

## 1. THE DEFECT, FROM CAPTURED DATA (adb logcat -d -s Unity, device, this session)

The door FIRED. It was overwritten one frame later.

```
08:28:00.191  [Flow:BuildCollections] Manage Placed card TAPPED - closing the browser ...
08:28:00.193  [Flow:Navigation]       closed workspace 'Build Collections' to world
08:28:00.194  [Flow:Pause]            WorldHold ACQUIRE 'obsidian-navigation-workspace:Build Collections' -> timeScale 0.00
08:28:00.194  [Flow:UI]               PanelManager: 'Build Collections' opened and verified visible (IsOpen=true)
08:28:00.208  [Flow:BuildPalette]     expand: restored BuildCollectionBrowser categories after place/cancel
08:28:00.210  [Flow:UI]               kit toast -> 'Tap a building or wall to move, upgrade or sell it.'
08:28:00.210  [Flow:Build]            ManagePlaced ENTERED ... 28 live PlacedStructure bodies are selectable
```

The owner tapped it twice (`08:28:00.191`, `08:28:00.972`), saw the catalog re-appear both times,
and reported the card as dead. It was not dead - the toast announcing "tap a building" was published
*underneath* a re-opened full-screen modal holding a `timeScale 0` WorldHold.

**The same stomp is why the tester cannot find MOVE.** Selecting a placed structure the ordinary way
works, and is then buried:

```
08:28:02.424  [Flow:Build] tap-select: hit - SELECTS 'lumberyard' (web pointer path)
08:28:02.429  [Flow:UI]    PanelManager: 'Build Collections' opened and verified visible (IsOpen=true)
```

That is the coordinator's own observation - *"MOVE ... CANCEL seen behind the Build Collections
frame"* - reproduced in the log. The verb row renders, then the catalog covers it.

## 2. ROOT CAUSE (one seam, two victims)

`BuildPaletteUI.Expand()` (`BuildPaletteUI.cs:875-886`) was redefined by **WO-1273**: it no longer
restores a dock carousel, it calls `Show()`, which **opens the modal `BuildCollectionBrowser`** and
takes a player-owned WorldHold. Its inverse, `Collapse()` (`BuildPaletteUI.cs:631`), was **not**
updated - it went on hiding legacy `_canvas` chrome and never touched `_collectionBrowser`. The pair
stopped being symmetric, and `CancelArmed()` (`BuildModeController.cs:2396`) calls `Expand()`
unconditionally on every disarm. So:

- `BeginManagePlaced` (`BuildModeController.cs:2436`) called `CancelArmed()` and had **no** Collapse
  at all -> the card's own `Close()` was undone in the same frame.
- `SelectStructure` (`BuildModeController.cs:2504`) *does* call `Collapse(selLabel)` deliberately
  "so the two do not fight" - but after WO-1273 that Collapse no longer wins.

**The MOVE chain itself is CORRECT and is not the bug.** `BeginMoveSelected`
(`BuildModeController.cs:2898`) frees the origin cells, seeds the ghost at the current spot and the
persisted yaw; `CommitMove` (`:2938`) occupies the new cells, repositions, syncs the
`PlacedStructure` marker, calls `UpdateLayoutEntry` (old cell -> new) and re-keys any in-flight
build/upgrade job; `CancelMove` (`:2990`) re-occupies the origin. MOVE is **free and preserves
level** (WO-1445 / OWNER_RULINGS_LOCKED ss25, commit `32659c0f6`) - no wallet call exists on the
path. What it had was **zero instrumentation**: begin/commit/cancel spoke only through
`Debug.Log`/`LogWarning`, so no device log could separate "MOVE was never reachable" from "MOVE ran
and refused".

## 3. THE FIX

1. **`BuildPaletteUI.Collapse`** - close the collection browser, **before** the `_canvas == null`
   early return (the legacy dock canvas is lazy and `Show()` deactivates it, so that guard would
   skip the only line that matters). Guarded on `IsOpen`, so the Arm path - where `Done()` already
   closed the browser - emits no second close and no second WorldHold release. This one edit fixes
   the select path, and therefore MOVE, for every caller.
2. **`BuildModeController.BeginManagePlaced`** - `_palette?.Collapse(null)` *after*
   `CancelArmed(); ClearSelection();`. Order is load-bearing; before `CancelArmed` it is overwritten.
   Both required calls are kept, so `PlacedStructureDoorRegression` C5c stays green.
3. **Instrument the MOVE chain (ss12)** - `FlowTrace` on `SELECTED` (naming that the browser was
   collapsed and MOVE is one tap away), `MOVE BEGUN` (origin cell, footprint, yaw, level, "free"),
   `MOVE COMMITTED` (old -> new cell, level preserved, ledger re-keyed, cost zero), `MOVE CANCELLED`
   (origin re-occupied), plus `FlowTrace.Fail` on the no-catalog-entry refusal that previously only
   wrote a `Debug.LogWarning`.

**Not changed, deliberately:** the card is **not** re-pointed at the Manage workspace. Ruling ss25
lands it on the existing tap-to-select loop on purpose - "move, upgrade or sell" *is* that loop, and
a second placed-structure screen would mint a second answer to "what is selected". The footer link
`ALREADY BUILT? MANAGE DEFENSES >` is **proven working** and untouched: `08:28:01.569 footer link
TAPPED` -> `PanelRouter: 'Manage' opened and verified visible` -> `08:28:02.421 Manage -> Defense`.
No route on the collections root resolves to nothing, so no `FlowTrace.Fail` is warranted there.

## 4. THE PIN

`Assets/Editor/Regression/PlacedStructureDoorRegression.cs` gains **C6**, named for what it guards:
*the door cannot be stomped by its own disarm*.

- **C6a** - `BuildPaletteUI.Collapse` references `_collectionBrowser`, at an index **before**
  `_canvas == null`.
- **C6b** - `BeginManagePlaced` contains `Collapse` at an index **after** `CancelArmed` (index
  comparison, not a bare grep - ordering is the whole assertion).

The suite header records why C6 exists: **C1..C5 were all green and all true** while the feature was
dead to the player. A chain oracle proves the chain; it cannot prove the panel at the end of it is
what is on screen.

The footer link is already pinned elsewhere and needs nothing new -
`BuildCollectionPlayerRegression.cs:153-156` asserts the link name, its copy, and the literal
`PanelRouter.Open(PanelId.Manage, "Defense")`.

## 5. FILES

- `Assets/_Modules/Village/BuildMode/BuildPaletteUI.cs` - `Collapse` closes the browser (the seam)
- `Assets/_Modules/Village/BuildMode/BuildModeController.cs` - `BeginManagePlaced` collapse;
  `SelectStructure` / `BeginMoveSelected` / `CommitMove` / `CancelMove` FlowTrace
- `Assets/Editor/Regression/PlacedStructureDoorRegression.cs` - C6a + C6b
- `CLI_LANES_WO_NUMBERS.md` - banner bumped 1581 -> 1582 in the mint edit

## 6. NOT DONE HERE (surfaced, not fixed)

- The double `placed-structure glow ON/off/ON` per tap - two select paths (`tap-select` and
  `SelectLoop`) both fire on one tap. Cosmetic today; log noise.
- `[touch-oracle] CLAMP FIRED ... ManageDefensesFooterLink: authored 746.2x52.4 -> grown to 112`.
  The link is authored under `MinTouchPx` and is only reachable because the clamp rescues it.
- No Unity run was made from this lane (no gate, no commit) - see the RESULT for what remains unproven.
