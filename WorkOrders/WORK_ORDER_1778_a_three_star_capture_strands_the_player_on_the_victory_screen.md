# WORK ORDER 1778 — A 3-star capture **strands the player on the victory screen**: the retry that was supposed to save it has zero callers

**Status:** READY TO IMPLEMENT

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** raid exit + owned-town entry — `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs`, `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs`, `Assets/_Modules/Core/SceneRouter.cs`. **No `.unity`, no bake, no AI, no camera.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`. WO-1767 (`bca258130`) and WO-1768 (`0da7af499`) landed 2026-09-16 15:33 and are **NOT** in it.

**Video path:** the marquee beat — act 2 shot 12 of `docs/HACKATHON_CLOCK_IN_SUBMISSION_PLAN_2026-09-16.md`. ⛔ **If this fires on camera the video has no ending.** P0.

---

## 1. SYMPTOM

The player 3-stars Iron Bastion. The victory screen says the base is hers. **Every route off that screen is refused, and the screen cannot be dismissed.** The only feedback is a toast telling her to try again — at a button that will refuse again, forever.

## 2. EVIDENCE

**The precondition fired in the owner's own 2026-09-16 run.** From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped):

```
[Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable identity: Wall_Outer_SS_3
```

She settled at 2 stars (`veterancy: 2 star(s) - no ranks granted (3 stars required).`), so capture was never attempted and the strand was never reached. **A 3-star clear on that same build would have hit it.**

**The strand, traced through source (all read 2026-09-16):**

1. `RaidVictoryController.cs:163-164` swallows the census throw, leaving `_captureCensus == null`.
2. `:1070-1071` then refuses capture forever: `"Final victory cannot capture: precombat census is missing."`
3. `CanEnterCapturedTown` `:1048-1053` toasts `ownedTown.captureRetry` (`Assets/Resources/Data/Canonical/en.json:16` — *"Your town could not be saved. Please try entering again."*) and returns **false**.
4. `ReturnHome` `:1037` returns early on that false.
5. `EndStateView.FirePrimary:2698` returns **before** `Destroy(gameObject)` when the gate refuses, so the auto-dismiss guard (`EndStateView.cs:2769-2777`) fires once and cannot clear the screen.

⛔ **The retry the source promises does not exist.** `RaidVictoryController.RetryCaptureAfterDismissal` (`:1055-1061`) has **zero callers**, and `_waitingForCapture` is never set true (declared `:115`, set false `:1059`, read `:261`). The doc comment at `:104-109` asserting *"a route home is guaranteed from three independent places"* plus a caller watchdog is **false for the capture branch**: `grep VictoryOwnsTheReturn` finds only `HeroHealth.cs:1485` (the death path).

**Two sibling strands on the same lane, same root shape (no guaranteed exit):**

- `OwnedTownPanel.Show:59` — `if (OwnedTownDesignService.IsBusy || OwnedTownConstructionService.IsBusy) return;` bails **before any button is built**, and the panel is built `withClose: false` (`:42`). While a move/build save is in flight the player sees `en.json:14` and has **no "Return to castle" and no X**.
- `SceneRouter.GoOwnedTown:607-608` **refuses silently** when `OwnedBaseProgression.Validate` fails — a `FlowTrace.Warn` only, no toast — so the practice panel's single button (`OwnedTownPracticeController.ReturnToTown:145`) can do nothing with no feedback at all.

## 3. WHAT THIS IS NOT

**WO-1767 (`bca258130`) fixes the CENSUS**, so the precondition should stop firing. ⛔ **That is not this ticket.** This is the unconditional promise: *whatever* refuses, the player always has one route off the victory screen. A refusal that can only be retried through a button that refuses is not a route.

## 4. THE FIX

1. **Wire the dead retry, or delete it and force the exit.** Either `RetryCaptureAfterDismissal` gets its caller and `_waitingForCapture` gets set, or the capture gate falls back to `ReturnHome` → `GoCastle` after a bounded number of refusals, with the capture receipt kept for a later claim. Trace whichever path is taken.
2. `EndStateView.FirePrimary:2698` — a refused gate must still allow the screen to be dismissed after the guard window; never leave a screen whose only CTA is a no-op.
3. `OwnedTownPanel.Show:59` — build the close/return affordance **before** the busy early-out, or build the panel `withClose: true`.
4. `SceneRouter.GoOwnedTown:607-608` — a refusal the player triggered must surface a toast, never only a `Warn`.
5. Add `FlowTrace` to `OwnedBaseProgression.cs` and `RaidCaptureCensus.cs` — both carry **zero** calls today (`grep -c FlowTrace` = 0 on each), so the contract's own refusal reasons are invisible except where the caller happens to echo them.

## 5. ACCEPTANCE

- A headless/editor proof that forces `_captureCensus == null` on a 3-star final victory and asserts the player reaches `Main_Castle_Overworld` or the owned town — never neither.
- A device capture of a real 3-star Bastion clear showing `OWNED_TOWN_CAPTURED:` (`RaidVictoryController.cs:1075`) **or**, on a refusal, a trace line naming the forced route home.
- `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 6. DO NOT TOUCH

WO-1767's census identity code, WO-1768's death-latch (`RaidVictoryController.cs:974`), the tutorial (WO-1788 lane), the victory screen's copy (WO-1783 lane).
