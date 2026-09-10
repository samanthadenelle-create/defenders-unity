# WO-1615 RESULT - Phase 1 only: the two discriminating traces are in, no fix is authorized yet

**Status:** INSTRUMENTED - awaiting the capture (2026-09-09 lane PLACE)
**Lane:** PLACE (edit-only). No Unity was run, nothing was committed, no scene was touched.
**Date:** 2026-09-09

---

## 1. The verdict, stated honestly

**I did NOT fix the defect, because the source does not prove which cause it is.** WO-1615 §3 makes
the capture the gate, and CLAUDE.md §12 forbids an edit that cannot cite captured data. Everything
below is either a line I opened at source this session or an explicitly-labelled candidate.

What I could rule out from source (each read this session, not copied):

*(Anchored on SYMBOLS, not line numbers: my own edits shifted every digit below them in both files,
and CLAUDE.md §11B calls a copied number hearsay. Line numbers quoted here were re-read AFTER the
edits, 2026-09-09.)*

| Theory | Read at source | Verdict |
|---|---|---|
| The HUD is not in `Placing` state during a move, so the intent bar (which owns `OkChip`) is hidden | `BuildModeController` `Update` sets `_hud.SetState(...)` to `BuildHudState.Placing` when `_armed != null \|\| _movingSelected` (`:796`); `BuildHudController.SetState` activates `_intentBar` exactly on `placing` (`:1031-1038`) | NOT the cause - the bar is active during a move |
| The chip is non-interactable | `BuildHudController.LayoutGhostControlsNow` sets `_okChip.interactable = _ghostValid` (`:1176`), fed by `PushGhostAnchorToHud` -> `_hud.TrackGhost(..., _ghost.IsValid, ...)` (`BuildModeController.cs:1490`), and `UpdateMoveLoop` calls `_ghost.SetValid(valid)` each frame | NOT the cause while the ghost is GREEN (the ledger's own observation) |
| `BuildSelectionUI` (canvas sortingOrder 910, above the HUD's 906) still covers the rail | `BeginMoveSelected` calls `_selectionUi?.Hide()` (`BuildModeController.cs:3023`); `BuildSelectionUI.Hide` (`:128-131`) deactivates the whole canvas GameObject, not just its verbs | Weak candidate only - the canvas is off |
| The latch is cleared by something else each frame | the only writes to `_uiPlaceLatch = false` are inside **Exit** (`:717`) and the consume site itself (`:1009`) | NOT the cause during a live move |
| A second loop consumes the latch before the move loop reads it | the `Update` dispatch is mutually exclusive - `if (_movingSelected) { UpdateMoveLoop(); return; }` | NOT the cause |

So both branches the ticket names remain live: **(a)** a higher raycast surface owns the click, or
**(b)** `OkChip` fires into a null / stale binding. **One capture separates them. I am not guessing
between them.**

## 2. What was added (both permanent - CLAUDE.md §12, never stripped)

### Trace 1 - the OkChip callback names itself
`Assets/_Modules/Village/BuildMode/BuildHudController.cs`, inside the `OkChip` `MakeWordVerb` lambda,
**before** `_onPlace?.Invoke()`. Tagged `"Build"` (not this file's usual `"BuildHud"`) deliberately,
so it lands in the same grep as `PlaceConfirm: UI PLACE button latch consumed`. It reports the bound
flag **and the delegate's target object name + instance id**, which is what separates "not bound at
all" from "bound to a different `BuildModeController` instance than the one running the move".

### Trace 2 - the over-UI suppression names the surface that owns the click
`Assets/_Modules/Village/BuildMode/BuildModeController.cs`, the `IsPointOverUi` branch only. The
probe already leaves `s_uiHits` populated (topmost-first) when it returns true, so the owner is read
with **no second raycast**: `hits[0]` name, its **full hierarchy path** (new private static helper
`HierarchyPathOf`, depth-capped at 16), the **raycaster module type** (`GraphicRaycaster` = a uGUI
canvas, `PanelRaycaster` = a UI Toolkit `UIDocument` sitting over the top), the `sortingOrder`, the
hit count, and `movingSelected` / `armed` so the reader knows the tap happened during MOVE.

**Neither the suppression nor the probe was weakened, no second commit path was added, `OkChip` was
not renamed** (WO §4, all four honoured).

## 3. The exact lines the next capture must show

Run: Build -> Manage Upgrades -> tower -> Move -> tap PLACE. Then read, in this order:

```
[Flow:BuildMove] MOVE BEGUN id='tower_ground_archer' origin cell=(...)
[Flow:Build]     OkChip TAPPED (onPlace bound=True, target='<controller host GameObject name>' id=-#####)
[Flow:Build]     PlaceConfirm: UI PLACE button latch consumed (zone suppression bypassed; touch latch drained.)
[Flow:BuildMove] MOVE COMMITTED id='tower_ground_archer' (x,y) -> (x,y) ...
```

*(`MOVE BEGUN` and `MOVE COMMITTED` are the literals at `BuildModeController.cs:3026` and `:3095`,
re-read at source after these edits; `target=` reports the delegate target's GameObject **name**,
which is `BuildModeController`'s host object - not verified against a live scene from here.)*

Routing table - **whichever of these the log shows decides the fix, and nothing else does**:

| What the capture shows | What it proves | Where the fix goes |
|---|---|---|
| `PlaceConfirm SUPPRESSED: ... hits[0]='<X>' path='...' module=GraphicRaycaster sortingOrder=N movingSelected=True` with **X != OkChip**, and **no** `OkChip TAPPED` | **(a)** that surface owns the PLACE rect during a move | fix THAT surface's raycast/ordering. Do not add a second commit path |
| `hits[0]='OkChip'` (path ends `.../GhostVerbRail/RailFill/OkChip`) but **no** `OkChip TAPPED` | the chip is topmost yet its click never delivers - its own `interactable` / `raycastTarget` / event delivery | `BuildHudController` chip construction |
| `OkChip TAPPED (onPlace bound=False, target='<none>' id=0)` | **(b)** the binding in `BuildModeController.EnsureHud` (`:4258`, the `RequestUiPlaceConfirm` argument at `:4264`) never ran for the instance that drew this HUD | the binding site |
| `OkChip TAPPED (onPlace bound=True, ...)` but **no** `PlaceConfirm: UI PLACE button latch consumed` | bound to a **stale** controller (compare the reported `id` with the live one) or the latch is cleared before the move loop reads it | the binding / latch lifetime |
| `module=PanelRaycaster` on `hits[0]` | a UI Toolkit `UIDocument` is over the rail - a different class of blocker entirely (the `PointerOverPickableUI` family) | that document's `PickingMode` / sorting |

**No `OkChip TAPPED` line at all AND no `PlaceConfirm SUPPRESSED` line** would mean the tap never
reached `BuildModeController` either - route to the input driver, not to this HUD.

## 4. Regression added (RED-first pin, no behaviour asserted)

`Assets/Editor/Regression/BuildPlaceLatchTraceRegression.cs` - a source oracle, because the thing
under test is a **log line**, which no headless play run can assert without the capture this ticket
is waiting for. It reads both files with **comments stripped and string literals kept** (the inverse
of the folder's usual `ReadStripped`, and deliberately: stripping strings would make every case pass
vacuously, keeping comments would let prose satisfy the pin).

- **C1** the `OkChip` lambda emits `OkChip TAPPED (onPlace bound=` **and** still calls
  `_onPlace?.Invoke()`, with the trace **before** the invoke (a throw inside the invoke must not
  erase the evidence).
- **C2** the suppression branch reports `PlaceConfirm SUPPRESSED: tap at`, `s_uiHits[0]`, `hits[0]='`,
  `path='`, `module=`, `movingSelected=`. The region is anchored on the **branch**
  (`if (IsPointOverUi(_input.ScreenPoint))`), not on the log string: `var top = s_uiHits[0];` sits
  ABOVE the `FlowTrace.Warn` literal, so anchoring on the message would have excluded the very read
  the case exists to pin - and the case would have been RED on a tree that is correct.
- **C3** the suppression is intact - `if (IsPointOverUi(_input.ScreenPoint))` still gates the world
  tap, and `internal static bool IsPointOverUi` still exists (the touch driver shares it).
- **C4** exactly ONE commit latch - `RequestUiPlaceConfirm` still sets `_uiPlaceLatch`, the
  latch-consumed trace is intact, `UpdateMoveLoop` still commits only on `confirm ==
  ConfirmKind.UiPlace`, **and fails if `UpdateMoveLoop` starts acting on `ConfirmKind.WorldTap`**
  (the banned second commit path, WO §4).

Markers: `PLACE_LATCH_TRACE_OK` / `PLACE_LATCH_TRACE_FAIL`.

**Registration line for the lead** (`DataRegression.cs` is lead-owned; this lane did not edit it):

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "place-latch-trace suite", () => { if (!DeNelle.Editor.BuildPlaceLatchTraceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[place-latch-trace] " + r); });
```

## 5. Files touched

- `Assets/_Modules/Village/BuildMode/BuildHudController.cs` (the `OkChip` lambda only)
- `Assets/_Modules/Village/BuildMode/BuildModeController.cs` (the `IsPointOverUi(_input.ScreenPoint)` suppression branch inside `ConfirmIntentThisFrame` only + a new
  private static `HierarchyPathOf` helper beside the probe)
- `Assets/Editor/Regression/BuildPlaceLatchTraceRegression.cs` (new)
- this WO + this RESULT

Untouched, as ordered: `ManageScreenVM.cs`, every View kit file, every scene, `DataRegression.cs`.

**Oracle logic proven without Unity:** a Python replica of the suite's own `ReadNoComments` /
`Between` / token + regex predicates was run against the two real files - **every C1..C4 predicate
holds on the tree (0 failures)**. That is not a compile, and it is not `REGRESSION_OK`; it proves the
oracle's matching logic is GREEN on a correct tree, which is the failure mode a source oracle is most
likely to ship with (the C2 anchor bug above was caught exactly this way).

**Gate:** brace balance + NUL scan run on all three `.cs` files - `54/54`, `558/558`, `20/20`, zero
NUL bytes. A depth walk of `BuildHudController.cs` ends at 0 and never goes negative. **No Unity was
run - the compile gate is the lead's, and `COMPILE_GATE_OK` has NOT been earned by this lane.**

## 6. Unproven, recorded honestly

- **Which surface owns the click.** Not guessed. That is the whole point of §3 above.
- **That the traces compile.** They are brace-balanced and NUL-clean, and every symbol used
  (`s_uiHits`, `RaycastResult.module` / `.sortingOrder`, `_movingSelected`, `_armed`, `FlowTrace`)
  was read at source in these files - but no compiler has seen them. Only the lead's gate proves it.
- **That `hits[0]` is the surface that consumed the *button* click.** The probe fires on the
  **world-tap** path; if a chip tap does not also reach that path on this input driver, trace 2 may
  simply not print for it. That is itself informative (it points straight at trace 1's outcome), but
  it is not a guarantee that trace 2 fires on every PLACE tap.
- **Whether a fresh PLACE (a new building) shares the defect.** WO §7's open question - unchanged,
  and one extra capture answers it.
- **Candidate, NOT a cause:** the build HUD canvas sits at `sortingOrder` 906 (`BuildHudController.cs:98`),
  between the palette dock (900) and the selection panel (910). Any surface that renders above 906
  while a move is running would out-rank the rail. `BuildSelectionUI` is ruled out (its whole canvas
  deactivates). **Nothing else was tested, and this must not be treated as a finding.**
