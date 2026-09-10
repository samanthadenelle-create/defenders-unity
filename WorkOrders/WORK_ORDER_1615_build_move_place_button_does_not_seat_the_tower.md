# WO-1615 - Build -> Move -> PLACE does not seat the tower: the chip is drawn, the latch is never set

**Status:** BLOCKED - awaiting the device capture; INSTRUMENTED 2026-09-09 (lane PLACE), no behavioural edit until the trace names the surface
*(Both sec.3 traces are in the tree and pinned by a new suite. NO behavioural edit was made: source
alone does not discriminate (a) from (b), so the sec.12 hard gate still stands. See
`WORK_ORDER_1615_build_move_place_button_does_not_seat_the_tower.RESULT.md` for the exact lines the
next capture must show and how each outcome routes.)*
Prior status: READY TO IMPLEMENT - INSTRUMENT FIRST. No fix is authorized before the captured data
names the surface that is eating the click (CLAUDE.md sec.12 hard gate).
**Minted:** 2026-09-09 (CLI, main-line banner; bumped 1615 -> 1619 in the SAME edit)
**Silo / Lane:** Build mode (town) - `Assets/_Modules/Village/BuildMode/`
**Severity:** P1 felt - the player can begin a move and cannot finish it. The town's Move verb is
dead end-to-end; the only exit is CANCEL, which silently re-occupies the origin cell so nothing on
screen says the operation failed.
**Type:** EXISTING system, existing surfaces. Nothing new is being built here.
**Provenance:** proven RCA with no ticket, `docs/READY_RCA_2026-09-09.md` "Build -> Manage Upgrades
-> tower -> Move -> PLACE does not seat"; carried as footnote 1 of
`docs/reference/READY_SILOS_2026-09-09.md`.

---

## 1. The captured chain, verbatim from the ledger

The live log proves the route through every earlier link, and proves exactly where it stops:

- `ManagePlaced ENTERED` reports **10** selectable `PlacedStructure` bodies.
- Archer tower selection reaches `BuildSelectionUI` **twice**.
- `MOVE BEGUN id='tower_ground_archer'` frees the origin cell and arms a legal ghost.
- World taps are received, but after `MOVE BEGUN` there is **no**
  `PlaceConfirm: UI PLACE button latch consumed` and **no** `MOVE COMMITTED`.
- The eventual path is `MOVE CANCELLED`, which safely re-occupies the origin.

The ghost is GREEN, so placement validity is not the failure. The failure is strictly **before**
validation and commit.

## 2. The source chain, read at source 2026-09-09

Every line below was opened this session, not copied from a doc:

| Step | Where | Reading |
|---|---|---|
| The PLACE chip is created | `BuildHudController.cs:707-709` | `MakeWordVerb(railRt, "OkChip", PlaceVerbWord, ..., () => _onPlace?.Invoke(), out _okChipLabel)` |
| The callback field | `BuildHudController.cs:233`, assigned `:292` | `private Action _onPlace;` / `hud._onPlace = onPlace;` |
| The controller binds it | `BuildModeController.EnsureHud` `:4212`, arg at `:4218` | `RequestUiPlaceConfirm,   // PLACE (the only commit latch)` |
| The latch | `BuildModeController.cs:208` | `public void RequestUiPlaceConfirm() => _uiPlaceLatch = true;` |
| The latch is consumed | `BuildModeController.cs:995-996` | `FlowTrace.Step("Build", "PlaceConfirm: UI PLACE button latch consumed ...")` then `return ConfirmKind.UiPlace;` |
| The move loop commits ONLY on that kind | `BuildModeController.UpdateMoveLoop` `:1595`, commit at `:1662` | `if (confirm == ConfirmKind.UiPlace)` |
| The raw world tap is deliberately suppressed | `BuildModeController.cs:1017` | `PlaceConfirm SUPPRESSED: tap at {..} is over UI (button tap, not a world placement)` |
| The uGUI probe that decides "over UI" | `BuildModeController.cs:945-965` | `EventSystem.current` raycast into `s_uiHits` via `s_uiProbe` |

So the intended chain is whole in source, and the raw-input suppression at `:1017` is **correct
behaviour** - a tap on the chip must not also be read as a world placement. That correctness is
precisely what hides the defect: the raw path is suppressed as expected, and the button path
produces no trace at all, so the log cannot currently distinguish

- **(a)** a higher uGUI surface owns the click and `OkChip` never receives it, from
- **(b)** `OkChip` receives it, invokes `_onPlace`, and `_onPlace` is null or bound to a different
  controller instance in this state.

**No fix may be written until the capture says which.**

## 3. The required discriminating proof (this IS the first deliverable)

Add exactly two `FlowTrace` lines, both permanent (CLAUDE.md sec.12: instrumentation is never
stripped), then run the exact route: Build -> Manage Upgrades -> tower -> Move -> tap PLACE.

1. **The top uGUI raycast target on the tap.** The probe at `BuildModeController.cs:945-965`
   already fills `s_uiHits`; log `s_uiHits[0].gameObject` name **and its full transform path** at
   the `:1017` suppression site (`FlowTrace.Warn`, or `Throttle` if it proves hot). Today that
   branch says only "is over UI" and never names the owner - that omission is the reason this
   ticket cannot already be fixed.
2. **A first line INSIDE the OkChip callback.** At `BuildHudController.cs:709`, before
   `_onPlace?.Invoke()`, emit `FlowTrace.Step("Build", "OkChip TAPPED (onPlace bound=" +
   (_onPlace != null) + ")")`. The bound flag is what separates (b) from a dead binding.

**Read the resulting lines before touching anything else.** The two outcomes route differently:
- Line 1 names a surface that is not `OkChip` -> the defect is a raycast/ordering problem on that
  surface in move state. Fix THAT surface; do not add a second commit path.
- Line 1 names `OkChip` and line 2 never prints -> the chip's own raycast/interactable state is the
  defect.
- Both print and `bound=False` -> the binding at `EnsureHud` `:4212-4218` did not run for the
  instance that drew this HUD.

## 4. What NOT to touch

- **Do not add a second commit path.** `ConfirmKind.UiPlace` is the ONE latch that commits a move
  (`:1662`); a world-tap or long-press fallback would hide this bug forever and duplicate the
  decider.
- **Do not weaken or delete the "over UI" suppression** at `:1017` / the probe at `:945-965`. It is
  working as designed and removing it would make a chip tap also drop a building.
- **Do not rename `OkChip`.** `BuildHudController.cs:833-835` records that the GameObject NAME is
  load-bearing - `UICaptureLaunch.AssertConfirmChipInvalid` finds it by that literal.
- Do not touch `BuildSelectionUI`, the ghost validity maths, `ManagePlaced`, or the latch semantics
  at `:208`.

## 5. Acceptance criteria

- [ ] **RED-first evidence:** the RESULT quotes both NEW trace lines, captured on this exact route,
      BEFORE any behavioural edit. A RESULT without those two lines fails this ticket.
- [ ] The RCA names which of (a) / (b) the data proved, in one sentence, with the log line.
- [ ] After the fix, one capture of the same route shows, in order: `MOVE BEGUN
      id='tower_ground_archer'` -> `OkChip TAPPED` -> `PlaceConfirm: UI PLACE button latch
      consumed` -> the move commits. `MOVE CANCELLED` does not appear.
- [ ] The two instrumentation lines STAY in the code after the fix (sec.12 - never strip).
- [ ] A regression case pins whatever the data named. If the cause is a raycast surface, pin the
      surface's raycast state, not a screenshot.
- [ ] Owner felt-verifies a tower move on device and closes (PO closes, not CLI - sec.13).

## 6. Files

**Instrumentation (both, first):**
- `Assets/_Modules/Village/BuildMode/BuildModeController.cs` (the `:1017` suppression site only)
- `Assets/_Modules/Village/BuildMode/BuildHudController.cs` (`:707-709`)

**Likely fix site:** named by the capture. Do not pre-commit to one.

**Suite:** a new or extended case under `Assets/Editor/Regression/`; hand the registration line back
to the lead - `DataRegression.cs` is lead-owned and no lane edits it.

## 7. Unproven, recorded honestly

- Which surface owns the click. That is the entire point of section 3 and is NOT guessed here.
- Whether the same failure occurs on a fresh PLACE (a new building) rather than a MOVE. The capture
  only covers the move route; `:1390-1394` shows a two-step COMMIT path that may or may not share
  the defect. Do not assume either way - one extra capture answers it.
