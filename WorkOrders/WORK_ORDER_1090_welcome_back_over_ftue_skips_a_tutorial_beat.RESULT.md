# WO-1090 RESULT - welcome-back over the FTUE: IMPLEMENTED on HEAD

**Verified:** 2026-09-09 (SILO 0B verify-and-flip pass, read-only; no `.cs` touched)
**Landed in:** `6a5c7a36d`
**Ancestry proof:** `git merge-base --is-ancestor 6a5c7a36d HEAD` -> exit 0 (HEAD = `184c8ff06`, `dev`)
**Compile proof:** `Builds/compile-gate-recurring-dragon-spells.log` (2026-09-09 13:49) ->
`COMPILE_GATE_OK :: scripts compiled clean`.

## THE TRAP, ANSWERED

The working-tree note records all three of this ticket's files as **killed mid-edit** and
"logic half-written - do not trust it". **All three diffs (`git diff f4e4630e3 6a5c7a36d`, 218 lines
across the three files) were read in full and judged COMPLETE and COHERENT.** No dangling reference,
no unclosed branch, no TODO stub; every new symbol has a live caller and every caller has a live
symbol (cross-grepped at HEAD below).

## FILE:LINE PROOF AT HEAD

**The new predicate (the whole point of the ticket - the narrow key could not close the Settle window)**
- `TutorialFlow.IsMandatoryChainLive` - `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs`, the
  getter body previously belonging to `WaveLoopSuppressedForTutorial`, which now delegates to it
  (`public static bool WaveLoopSuppressedForTutorial => IsMandatoryChainLive;`). One predicate, no
  second copy of the three checks.
- `TutorialFlow.LiveChainStateLine` - static, null-safe, returns `"<phase>/<stepId>"` or `"<no flow>"`.

**Half A - park AND re-park (the missing direction)**
- `OfflineHarvestService.cs:1360` `if (WelcomeBackPopup.IsOpen && TutorialFlow.IsMandatoryChainLive)`
  -> reads `WelcomeBackPopup.ActiveResult` (`:1362`), `DismissIfOpen(...)`, re-parks into the EXISTING
  `_deferredReveal` / `_tutorialDeferred` pair. Both outcomes trace: a `FlowTrace.Warn` for the
  re-park and a distinct `FlowTrace.Warn` for the "open report carried NO result" branch (`:1384`),
  so no reveal can be swallowed silently (CLAUDE.md S12).
- `OfflineHarvestService.cs:1394` the RELEASE now uses the SAME key (`IsMandatoryChainLive`), which is
  what prevents the park/release flap the diff comment calls out.
- `OfflineHarvestService.cs:1451` `TryShowPopup` refuses to OPEN on the same broad key; the narrow
  `IsAwaitingDialogue` is still read, but only to name WHICH beat in the trace text.

**Supporting statics (one owner, sourced from `s_active`, never a mirrored bool)**
- `WelcomeBackPopup.cs:46` `public static bool IsOpen => s_active != null;`
- `WelcomeBackPopup.cs:55` `public static OfflineHarvestResult ActiveResult => s_active != null ? s_active._result : null;`
- `s_active` is cleared in BOTH `Dismiss` (`:805`) and `OnDestroy` (`:816`), so `IsOpen` cannot latch
  true after teardown.
- `DismissIfOpen`'s trace no longer asserts "on the previous save" - it had two callers and only one
  of them is a New Game (S11B: a trace line must not state a fact it cannot know).

**Half B - the beat is no longer charged for modal-held seconds**
- `TutorialFlow.TickStepClock`: `bool modal = DeNelle.Village.UI.WelcomeBackPopup.IsOpen;` feeds a
  third exclusion, `_stepClock.Tick(..., excluded: builder || frozen || modal)`. `StepClock.Tick`
  returns `float` (`TutorialFlow.cs:518` `public float Tick(float rawUnscaledDelta, bool excluded)`),
  so the `float accepted = ...` assignment is valid - StepClock itself is untouched, keeping
  `TutorialWatchdogBoundRegression` intact.
- `_modalExcludedSeconds` is declared, reset per step alongside `_stepClock.Reset()`, accumulated only
  when the modal is the actual reason (`if (modal && !builder && !frozen)`), and surfaced twice: in the
  STEP-STUCK breakdown text (`excluded (builder/frozen/modal) {excluded}s of which modal {modalOut}s`)
  and as `secondsExcludedModal` on the `tutorial_step_drop` analytics event.
- The watchdog itself now `return`s early while the modal owns the screen, with a `FlowTrace.Once`
  that says seeing the line at all means half A did not hold.

**Rejected approaches stayed rejected:** no `TutorialSkipUi.SetSuppressed`; the watchdog rescue is
paused, not removed; the exclusion is deliberately narrow (this modal, not "any PanelManager modal").

## WHAT THE OWNER FELT-TESTS

Start a NEW GAME with an away haul pending (background the app during HeroSelect for >30 min, or use a
save with an away window). Expect: no welcome-back report on screen while the FTUE runs; the Skip
control is tappable throughout; the report appears once the mandatory chain finishes; the founding beat
plays rather than being rescue-skipped.

## WHAT IS **NOT** PROVEN

- **No runtime proof.** No FTUE run has been driven since the change; the acceptance items
  ("`SKIP_TOP_HIT_BLOCKED` no longer fires", "a beat held under a modal shows non-zero excluded time")
  are unverified.
- **No regression pin was added** for the deferral or the modal exclusion.
- The WO's own Unproven list still stands: no tap trace exists (the probe is synthetic), and the
  probe-vs-settle frame timing is arithmetic from constants.
- Note the interaction with `f4e4630e3`'s `MinAwaySummarySeconds = 30 min` gate in the same file: an
  away window under 30 minutes now never reveals at all, so a short-background repro will NOT exercise
  this path.

---

## 2026-09-09 pins (edit-only PINS lane) - the "no regression pin was added" gap, closed

**New suite:** `Assets/Editor/Regression/FtueModalDeferralRegression.cs`
**Tag / markers:** `[ftue-modal-deferral]` -> `FTUE_MODAL_DEFERRAL_OK` / `FTUE_MODAL_DEFERRAL_FAIL`
**Registration line (hand-back to the lead - MUST land INSIDE the START/END fence of
`Assets/Editor/Regression/DataRegression.cs`, in the same gate as the file):**

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "ftue-modal-deferral suite", () => { if (!DeNelle.Editor.Regression.FtueModalDeferralRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ftue-modal-deferral] " + r); });
```

**Acceptance items pinned:** item 1 (the report does not sit over a fresh FTUE; it appears after the
chain) -> cases `[re-park]`, `[same-key]`, `[refuse-open]`. Item 3 (a beat held under a modal shows
non-zero excluded time and is not rescue-skipped) -> case `[step-clock]`.
**NOT pinned, and not claimed:** item 2 (`SKIP_TOP_HIT_BLOCKED` no longer fires) and item 5 (owner
felt-test) are runtime facts about a driven FTUE.

**RED-first mutations (the exact change that reds each case):**

| Case | Mutation that FAILS it |
|---|---|
| `[seam]` | `WelcomeBackPopup.IsOpen` re-implemented as a mirrored bool that latches, or `=> true` |
| `[chain-key]` | any guard in `IsMandatoryChainLive` flipped to `return true` (fail-CLOSED) - headless the getter exits at the `svc == null` guard, so **that** is the guard this case actually covers; the deeper `flow == null` fail-open is only reachable in a run where a `GameStateService` is installed, and this case does **not** cover it (stated, not glossed) |
| `[chain-key-shape]` | weakening the getter to `_phase != Phase.Idle && _phase != Phase.Finished` - the producer's own "do NOT weaken this" note, and the Settle window it re-opens. Scoped to the getter slice: `_phase != Phase.Idle` is a legitimate unrelated comparison at `TutorialFlow.cs:717`, so a whole-file rule would red a healthy tree |
| `[one-predicate]` | `WaveLoopSuppressedForTutorial` given its own copy of the three checks instead of delegating |
| `[re-park]` | deleting the `WelcomeBackPopup.IsOpen && IsMandatoryChainLive` branch from `Update()`, or dismissing without `_deferredReveal = onScreen` (a swallowed reveal), or dropping the "carried NO result" trace |
| `[same-key]` | releasing on `!IsAwaitingDialogue` while parking on the broad key (the once-per-frame flap) |
| `[refuse-open]` | `TryShowPopup` gating on `IsAwaitingDialogue` again - it reads FALSE in the Settle window, which is where seq 4682 happened |
| `[step-clock]` | dropping `modal` from `excluded: builder \|\| frozen \|\| modal`; dropping the `!builder && !frozen` terms from the attribution (the slice then overstates); removing `secondsExcludedModal`; removing `_modalExcludedSeconds = 0f` (accumulates across steps); removing the `watchdog-modal-pause` stand-down |

**Skip / PartialSkip handling.** Missing producer files are the FIXTURE and **FAIL** naming the path
(INSTRUMENTATION_STANDARD S8.5) - never a skip. The two LIVE reads (`[seam]`, `[chain-key]`) are
process-wide statics, so they carry an explicit ORDER-DEPENDENCE declaration: if an earlier suite in the
same `RunAll` left a `WelcomeBackPopup` or a `TutorialFlow` alive, the closed-state read cannot be made,
and the case emits `RegressionOutcome.PartialSkip` naming that rather than FAILing (which would red a
healthy tree for another lane's fixture) or passing silently (which would assert nothing). Checked
2026-09-09: no suite under `Assets/Editor/Regression` calls `WelcomeBackPopup.Show` (the only callers are
in `UICaptureLaunch`, a different entry point), and `TutorialCoachEscalationRegression:263` builds a
`TutorialFlow` on a throwaway GameObject that it `DestroyImmediate`s in a `finally`. The declaration is
there because "no caller today" is not an invariant. There is exactly one `return` in `Run`, and it is
the failure count: no guard-and-return-true exists to be a hollow pass.

**Unproven (CLAUDE.md S11B):** the RED-first mutations above are established **by construction** - each
case is a containment/equality test over a token that was verified present in the producer this session
(51/51 token checks re-run against the working tree after the getter-scope and PartialSkip edits).
**This lane has no Unity and executed nothing**: there is no `COMPILE_GATE_OK` and no `REGRESSION_OK` for these files, and
until the registration line lands, `RegressionMarkerRegression` RULE 2 will red the whole run on an
unregistered `Run(out string)` file. Brace-balanced (15/15) and NUL-free; parens balance outside strings
and comments (112/112).
