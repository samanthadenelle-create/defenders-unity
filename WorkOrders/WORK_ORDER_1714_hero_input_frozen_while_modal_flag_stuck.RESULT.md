# WORK ORDER 1714 — RESULT (implementation lane, 2026-09-14)

**Status:** IMPLEMENTED - awaiting lead gate 2026-09-14
**Branch:** `dev` @ `9058b66b2`. Edit-only lane: no Unity run, no gate, no build, no commit, no `.unity` touched.

## Fix shape chosen — BOTH (b) and (a), layered, and why

The WO offered (a) a timeout, (b) a `DialogueHiddenForBuilder`-shaped bypass seam, and asked for (a)
as defence in depth even if (b) was done. Both shipped, because each covers a class the other cannot:

- **(b) is the right primary fix** because it matches the architecture that already exists and works.
  The builder truce's seam (`BuildModeState.DialogueHiddenForBuilder`, `BuildModeState.cs:38` ->
  `BuildModeController.cs:772`) is the shipped precedent for exactly this problem, and the asmdef law
  (CLAUDE.md §5, HUD never references Village) forces the same Core-static shape. (b) releases the gate
  **immediately** — no freeze at all, not a shorter freeze.
- **(a) alone would have been wrong as the only fix**: a timeout still leaves the hero frozen and
  defenceless for the whole bound in the *known* case the capture proved, which is the case that matters.
- **(a) is still required**, and is written to catch what (b) structurally cannot: an alive-but-headless
  VM (the P0 re-entrancy shape, `DialogueView.cs:137-145` post-edit), an `Ended` that never fires with nothing
  hidden, or a FOURTH truce added later with no seam of its own.

**The bound is NOT "suppression held too long" — it is "suppression held with NO VISIBLE PANEL and NO
KNOWN TRUCE".** That distinction is load-bearing: a player may legitimately leave a dialogue line on
screen for minutes, and force-clearing that would resurrect the WO-377 click-through defect the gate was
built for. The builder truce is exempt for the same reason (a build session routinely outlasts any bound)
— without that exemption the loud `Fail` would false-fire every session.

The recovery **latches** rather than destroying the raw flag, so a re-shown panel re-engages suppression
correctly, and it emits `FlowTrace.Fail` (never a silent recovery — CLAUDE.md §12), once, naming all four
gate flags so the next capture routes to the real upstream dialogue-lifecycle defect instead of stopping
at the recovery.

## Files changed (file:line of every change)

**NEW — `Assets/_Modules/Core/Dialogue/DialogueGateState.cs`** (whole file, 97 lines). The Core seam:
`HiddenForCombat` (`:49`), `HiddenForModal` (`:54`), `PanelVisible` (`:61`), `HiddenByTruce` (`:70`),
`DescribeGate` (`:77`), `Clear` (`:86`), `ResetStatics` under `SubsystemRegistration` (`:97`, copied from
`BuildModeState.cs:44-50` — domain reload is off, so a stale truce must not survive a Play session).
HUD writes; Village reads; neither writes the other's, exactly like `BuildModeState`.

**`Assets/_Modules/Village/Hero/HeroLocomotion.cs`**
- `:304-440` — the WO-377 raw `InputSuppressed` auto-property is replaced by a raw latch
  `_inputSuppressRaw` (`:340`) + `_inputSuppressStuckLatched` (`:346`) + `_inputSuppressInvisibleHeld`
  (`:350`) + `InputSuppressionInvisibleMaxSeconds = 5f` (`:355`), a **computed** `InputSuppressed`
  (`:363`), the pure `EvaluateInputSuppressed` (`:372`) and `InputSuppressionStuck` (`:382`), and
  `TickInputSuppressionWatchdog` (`:392`). Every existing consumer (`HeroAbilityInput.cs:51`,
  `PlayerAttackController.cs:297`/`:456`, `Tower.cs:1327`, `BuildModeController.cs:772`,
  `VillageBridgeService.cs:78`) is fixed from this one place, unchanged — the WO-377 header's own intent.
- `:1047` — `OnDestroy` now clears through the single writer.
- `:1055` — new `SetRawSuppression(bool)`: the ONLY writer of the raw latch; resets the stuck latch
  and the bound timer on every raise/clear, which is what makes the watchdog recoverable rather than
  one-shot.
- `:1077-1088` — `HookDialogueGate` reconciles against `_inputSuppressRaw`, not the composed property
  (the composed value can read false purely because a truce is masking it).
- `:1095-1097` / `:1102-1104` — `OnDialogueStarted` / `OnDialogueEnded` route through `SetRawSuppression`.
- `:1242` — `TickInputSuppressionWatchdog()` is the FIRST call in `Update`, before the
  `if (InputSuppressed && !probeDriving)` early-return at `:1264` (a tick after it could never run on the
  frames the hero is stuck). It sits under the existing WO-1483 `FlowTrace.Measure` scope, so it stays
  frame-budget accounted.

**`Assets/_Modules/HUD/DialogueView.cs`**
- `:110-116` — `OnDisable` (`:110`) now also calls `DialogueGateState.Clear()` (a view torn down mid-truce must not
  leave a stale TRUE), and new `PublishGateState()` (`:129`) writes all three flags.
- `:548` — `PublishGateState()` is called from `Update` after the three truce ticks, **every frame, not on
  transition**. That is deliberate and copies `TickBuilderTruce`'s own stated reason ("self-healing: a
  dialogue superseded/closed while hidden clears it"). No truce bookkeeping was changed — the three
  truces still hide-not-close and still log "Ended NOT fired"; they simply now say so out loud to Village.

**`Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs`**
- `:58` — `HeroSuppressionProgressMaxSeconds = 30f`, public const, **deliberately 6x longer than the
  game's own 5 s recovery** so the game gets first refusal and the harness only reports a suppression that
  outlived the fix.
- `:415-439` — the unconditional `IsHeroInputSuppressed() -> MarkProgress()` whitelist is now bounded:
  it accumulates `_heroSuppressedHeld` per 2 s tick, whitelists only while
  `SuppressionCountsAsProgress(...)`, and otherwise falls through to the existing stall/softlock
  detection. Over-bound is traced ONCE on the rising edge (`FlowTrace.Warn`), copying the
  `_buildSuppressTraced` shape that already sits at `:458-467` (field `:539`) in this same method for exactly this
  reason — a per-tick line would drown the capture the harness exists to produce (memory:
  `logcat-ring-buffer-destroys-evidence`). `PanelManager.AnyOpen` at `:444` was **left alone** — a modal
  the player opened is still the player's choice, and the captured defect's load-bearing 11.3 s window has
  `modal=False`, so the suppression bound is the one that reaches it.
- `:544-552` — new fields `_heroSuppressedHeld` / `_heroSuppressedOverBoundTraced` and the pure public
  `SuppressionCountsAsProgress(float)`.

## Regression

**NEW — `Assets/Editor/Regression/DialogueInputGateRegression.cs`**, marker `DIALOGUE_INPUT_GATE_OK`,
6 cases. Registered at **`Assets/Editor/Regression/DataRegression.cs:1236`** (one line, tag
`[dialogue-input-gate]`; `DataRegression.cs` had NO uncommitted hunks when this lane touched it —
`git diff` on it was empty — so there was nothing to route around).

| case | what it proves | kind |
|---|---|---|
| 1 `[gate]` | `EvaluateInputSuppressed` truth table: raw-only still suppresses (WO-377 intact); **raw + truce RELEASES** — that single row IS the captured mid-wave freeze; stuck latch releases | real assertion |
| 2 `[bound]` | `InputSuppressionStuck` fires past the bound with nothing accounting; does NOT fire on a VISIBLE panel, on any of the three truces (the builder row is the one that would false-fire every session), under the bound, or with no raw latch; bound > 0 | real assertion |
| 3 `[loud]` | the recovery still emits `FlowTrace.Fail`, still names WO-1714, still latches, still decides through `InputSuppressionStuck` | source lint |
| 4 `[seam]` | `DialogueView` still declares and calls `PublishGateState` from `Update`, publishes all three flags, clears on disable; `HiddenByTruce` honours both truces; `Clear()` clears everything | lint + real assertion |
| 5 `[harness]` | harness bound is strictly longer than the game's recovery; under-bound counts as progress, over-bound does NOT (the 2026-09-14 blind spot verbatim); the shipped branch still gates on the tested function | real assertion + lint |
| 6 `[wiring]` | `TickInputSuppressionWatchdog()` is called in `Update` and **before** the `InputSuppressed` early-return; `InputSuppressed` still composes through `EvaluateInputSuppressed` | source lint |

**Honest scope (CLAUDE.md §11B).** Cases 1, 2 and 5 compute the real shipped decision functions with no
PlayMode session — the precedent is `HeroLocomotion.TeleportGuardHeld` (`HeroLocomotion.cs:474`), which exists in this same
file for the same reason. Cases 3, 4, 6 are **source lints and say so in every reason string**: they stop
the wiring being deleted, which no pure test can, and they cannot prove runtime behaviour. **Neither half
proves the freeze is gone on a device** — that remains the WO's own device repro (its §7): start Wave 1
and trigger `tut_ctx_talents` while `BattleLock.IsInBattle()`. Today's build freezes; after this change
the expected log is the truce `Step` line with `inputSuppressed=False` on the same frames.

Case 6 deliberately does NOT brace-match `HeroLocomotion.Update` (hundreds of lines of interpolated trace
strings); it anchors on the signature and compares offsets, because this repo has already been bitten by a
brace walk with no interpolated-string model (CLAUDE.md §1, WO-1096).

## Grep results the lead asked to see recorded

- `grep -rn "DialogueService.Opened" Assets --include=*.cs` -> **only** `DialogueView.cs:108/109`.
  `DialogueView` is the SOLE dialogue presenter, so `PanelVisible` cannot be silently un-published by a
  second view. This is the precondition the whole backstop rests on.
- `grep -rn "DialogueService.Started" Assets --include=*.cs` -> `HeroBodySwapper.cs:1143/1161` (idle pose
  only, no gate) and `HeroLocomotion.cs`. No third writer of the gate.
- `AutoPilotDriver.cs:4066-4070` asserts `InputSuppressed` RELEASES within 2 s of a chained close — this
  change can only help it, never break it.
- `DungeonMoverOwnershipRegression.cs:275-305` and `ShippedSurfaceGateRegression.cs:108` are source-text
  checks over `HeroLocomotion.cs`; every token they require (`ForeignMoverOwnsTransform()`,
  `foreignOwnsTransform`, `ResolveAnimatorFeed(`, `ResolveSuppressedAnimatorFeed(`, and the *absence* of
  an unconditional `_actor?.SetLocomotion(0f)`) is untouched by this lane.

## Out of scope, deliberately untouched

`TutorialFlow.TickContextual`'s missing battle-lock gate (`TutorialFlow.cs:2724`/`:2757`) — the RCA's
secondary finding and the *trigger*; separate ticket. `TutorialFlow.cs:2470`'s narrow `WelcomeBackPopup`
modal counter — recorded by the RCA, separate. WO-1713 and WO-1705 — unrelated, untouched.

## Gate evidence from this lane (edit-only; the lead runs the real gate)

```
$ python tools/gate_brace.py Assets/_Modules/Core/Dialogue/DialogueGateState.cs \
    Assets/_Modules/Village/Hero/HeroLocomotion.cs Assets/_Modules/HUD/DialogueView.cs \
    Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs \
    Assets/Editor/Regression/DialogueInputGateRegression.cs Assets/Editor/Regression/DataRegression.cs
GATE_BRACE_SUMMARY bad=0 of 6
EXIT=0
```

NUL scan (embedded/trailing `\x00`, the §0 mount-garble detector the compile gate also runs):

```
Assets/_Modules/Core/Dialogue/DialogueGateState.cs: NUL=0 bytes=5844
Assets/_Modules/Village/Hero/HeroLocomotion.cs: NUL=0 bytes=156020
Assets/_Modules/HUD/DialogueView.cs: NUL=0 bytes=84101
Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs: NUL=0 bytes=67172
Assets/Editor/Regression/DialogueInputGateRegression.cs: NUL=0 bytes=22009
Assets/Editor/Regression/DataRegression.cs: NUL=0 bytes=439470
NUL_SCAN CLEAN (6 files)
```

**NOT PROVEN by this lane** (CLAUDE.md §11B): no Unity compile, no `COMPILE_GATE_OK`, no
`REGRESSION_OK`, no device run. All six files are edit-only claims until the lead gates the combined tree.

## Side-effect reads done before hand-back (all clean, all opened this session)

- **`HeroBodySwapper.OnDialogueIdle` (`HeroBodySwapper.cs:1149-1154`, subscribed to BOTH `Started` and
  `Ended` at `:1143-1144`) is an EDGE-TRIGGERED ONE-SHOT re-pin, not a latch** — it calls
  `SetCombatStance(false)` / `SetLocomotion(0f)` / `DriveIdlePose` once on the event and never again.
  So releasing the input gate mid-truce does NOT leave the hero walking in a forced idle pose; the
  animator is re-driven every frame by `ResolveAnimatorFeed`. No sliding-hero risk introduced.
- **The suppression branch (`HeroLocomotion.cs:1264-1296`) writes `Velocity = Vector3.zero` on EVERY
  frame it runs** (`:1277`), not only on the `Started` edge — so a truce that lifts and later re-engages
  cannot leak carried velocity into the re-suppressed frames.
- **`DialogueView.Repaint` really does hide the panel during a truce**: `_ui.SetActive(open &&
  !_vm.HiddenForBuilder && !_hiddenForCombat && !_hiddenForModal)` (`DialogueView.cs:702`). That is what
  makes `PanelVisible = live && IsShowing` truthful — `IsShowing` (`:106`) reads `_ui.activeSelf`, so it
  is genuinely false through every truce and the 5 s backstop is not blinded by a hidden-but-active panel.
- **No CS0104 risk from the regression's usings**: `ls Assets/_Modules/Core/Diagnostics/` declares no type
  named `Debug` (ArcaneTowerDiag, BreakCaptureHarness, DeathTrace, DevClock, FloorDeepDiag, FlowTrace,
  Guard, PerfReporter, PrivacySensitiveUi, ScreenOpenWatchdog, UICaptureMode, UiSurfaceProbe, WebTrace,
  WebTraceSink), so `Debug.Log` resolves to `UnityEngine.Debug`.

**Known, deliberate, low-risk:** `_inputSuppressInvisibleHeld` is STATIC (matching the gate it bounds) but
accumulated from an INSTANCE `Update`. Two simultaneously-live `HeroLocomotion` components would
double-count it and halve the effective bound to ~2.5 s. Not proven to happen — `HeroControlEnsurer`
maintains one tagged hero — and even at 2.5 s the bound only fires with no panel and no truce, so the
failure mode is a slightly earlier recovery, never a false freeze. Recorded rather than defended against.

## For the lead staging this lane

TWO NEW `.cs` files need their Unity-generated `.meta` staged alongside them once the gate imports them:
`Assets/_Modules/Core/Dialogue/DialogueGateState.cs.meta` and
`Assets/Editor/Regression/DialogueInputGateRegression.cs.meta`. A commit carrying the `.cs` without the
`.meta` gives the next clone a missing-script.
