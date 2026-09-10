# WORK ORDER 1090 — the welcome-back modal opens over the FTUE, blocks Skip, and gets a tutorial beat rescue-skipped

**Status:** IMPLEMENTED - 6a5c7a36d on HEAD 2026-09-09 (was READY); owner felt-test closes
PRIOR STATUS: READY TO IMPLEMENT
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1089 → 1095 in the same edit)
**Silo:** Onboarding / UI layering
**Severity:** P1 — first-run experience, silent
**Source:** F8 capture seq=4705, with the 09-05 device capture seq=4682 as the harm proof

> ⚠ **UNTRUSTED WORKING-TREE EDITS EXIST.** A UI-seat edit agent was stopped mid-work in
> `OfflineHarvestService.cs`, `WelcomeBackPopup.cs` and `TutorialFlow.cs`. All three are dirty,
> braces balanced, 0 NUL bytes, **logic half-written — do not trust it.** See
> `WO_1089_1094_WORKING_TREE_NOTE.md`.
>
> **REVIEWED 2026-09-09 (SILO 0B):** `git diff f4e4630e3 6a5c7a36d` read in full across all three
> files and judged COMPLETE and COHERENT - the "half-written" call above is SUPERSEDED. See
> `WORK_ORDER_1090_welcome_back_over_ftue_skips_a_tutorial_beat.RESULT.md`.

---

## Symptom

`[Flow:Tutorial] SKIP_TOP_HIT_BLOCKED top=ObsidianPanel path=WelcomeBackUI/ObsidianPanel`

The tutorial's Skip control is unreachable because the welcome-back modal is on top of it.

## Why it is guaranteed, not racy

- Skip's canvas: `CanvasSortOrder = 6000` — `Assets\_Modules\Core\UI\TutorialSkipUi.cs:54`, applied
  with `overrideSorting = true` at `:151-152`.
- Welcome-back modal: `sortingOrder: 32020` — `Assets\_Modules\Village\Harvest\UI\WelcomeBackPopup.cs:120-122`.

Whenever both are on screen the modal wins by **26,020**. The emitter is
`TutorialSkipUi.VerifyTopPointerHit()` (`:342-344`), a synthetic `EventSystem.RaycastAll` probe from
`Update()` (`:296-297`), latched after 8 stable frames (`:339`) — a probe, not an observed tap.

## The ordering (Player.log, that session, in order)

```
L1446  [Flow:Tutorial] Bootstrap(sceneLoaded): TutorialFlow armed in hub 'Main_Castle_Overworld'.
L1489  [Flow:Offline] welcome-back deferred reveal RELEASED: hub scene loaded. AwaySeconds=12.
L1497  [Flow:UI]      PanelManager: 'Welcome Back' opened and verified visible (IsOpen=true).
L1938  [Flow:Tutorial] flow 'ftue_v2' started (8 steps).
L1940  [Flow:Tutorial] SkipControl SHOW (the ONE skip, top-middle, WO-1033)
L2725  [Flow:Tutorial] SKIP_TOP_HIT_BLOCKED top=ObsidianPanel path=WelcomeBackUI/ObsidianPanel
```

## Why the existing WO-1414 guard did not catch it — it is one-directional

`OfflineHarvestService.TryShowPopup` already refuses to open over a beat awaiting a dialogue
(`Assets\_Modules\Village\Harvest\OfflineHarvestService.cs:1377-1385`), reading
`TutorialFlow.IsAwaitingDialogue` → `AwaitedDialogueSignal`
(`Assets\_Modules\Village\Tutorial\V2\TutorialFlow.cs:233-252`), which returns null while
`flow._step == null` (`:242`).

At hub load the flow is **armed but not started** — `_step` is null — so the guard correctly
evaluated false, and the tutorial then started **underneath an already-open modal**. Nothing
re-checks after that: the only release path is `OfflineHarvestService.Update` (`:1325-1336`), which
releases a *parked* reveal and never re-parks a *shown* one.

This is a **second uncovered ordering, not a regression** — the build post-dates the WO-1414 fix
(commit `57d3437a2`, 2026-09-05) and carries its sibling strings.

## The real harm — not a freeze, a silently skipped first beat

`TutorialFlow.TickWatchdog` (`:2432-2463`) pauses only for `BuildModeState.IsActive` (`:2443`) and
`WorldClockFrozen` (`:2454`). **A modal is neither, so modal-held time is charged in full.** Proven
on 09-05 in `logs\f8-inbox\capture-device-20260905-100245-seq4682.md`:

> `STEP-STUCK :: founding_greet — no 'dialogue.ended:tut_founding_greet' after 120s in-step ...
> [WO-1036 clock: played-and-charged 120s, wall 120s, excluded (builder/frozen) 0s ...]; RESCUED via
> watchdog and recorded as SKIPPED`

So: a first-run tutorial beat is silently skipped, and the one escape hatch is under the modal
causing it. On 09-08 the player tapped COLLECT inside the bound, which is the only reason it read as
noise. The WO-795 modal truce (`DialogueView.cs:169`, `:592-598`) held the dialogue and fired it on
modal close, so that session did not softlock.

## Owner ruling (2026-09-09)

**Defer the welcome-back for the whole mandatory tutorial chain**, not just for a dialogue-awaiting
beat. A brand-new player's away haul is trivial (12 seconds here) and is already banked, so deferring
costs nothing; interrupting the FTUE costs a beat. *Reversible on her word — she asked for the broad
key knowing the away report then waits for the chain to finish.*

## Fix spec — both halves, neither alone is sufficient

**(A) Make the deferral symmetric, and broaden its key.** In `OfflineHarvestService.Update`
(`:1325-1336`), when the mandatory chain is live and a welcome-back is already on screen, dismiss via
the **existing** `WelcomeBackPopup.DismissIfOpen(...)` (`WelcomeBackPopup.cs:750-763`, already built
for WO-1414 A) and re-park into the existing `_deferredReveal` / `_tutorialDeferred` fields, letting
the existing release path re-show it. Reuse the mechanism — the recorded design intent at `:1374-1376`
is "DEFERRING, NOT RE-LAYERING".

Key on **the chain being live** (`TutorialFlow` instance present and not finished — `:207-212`), NOT
on `IsAwaitingDialogue`: `_step` is null during the 1.25 s Settle window (`TutorialFlow.cs:60`) while
the Skip probe has already latched, so the narrow key provably cannot close the window.

**(B) Exclude modal-open time from the tutorial watchdog**, alongside the existing builder/frozen
exclusions (`TutorialFlow.cs:2443-2460`). A beat must never be rescue-skipped for time the player
could not act in. Keep the exclusions **reported separately** in the WO-1036 clock breakdown, so the
next log still says how much time was excluded and why.

## Do NOT

- Do not add a `TutorialSkipUi.SetSuppressed` path for modals. It silences the probe and repairs
  nothing — explicitly rejected by the RCA.
- Do not weaken or remove the watchdog rescue itself.

## Acceptance criteria

- [ ] With an away haul pending, a fresh FTUE start does **not** leave a welcome-back on screen; the
      report appears after the chain finishes.
- [ ] `SKIP_TOP_HIT_BLOCKED` no longer fires during the FTUE.
- [ ] A beat held under a modal shows non-zero excluded time in the clock breakdown and is **not**
      rescue-skipped.
- [ ] Brace balance; gate markers on a fresh log.
- [ ] Owner felt-verifies a new-game run and closes.

## Unproven, recorded honestly

- Whether the player ever actually tried to tap Skip — the probe is synthetic; no tap trace exists.
- The exact probe-vs-settle frame timing is arithmetic from constants, not a measurement.
- Whether this reproduces on a clean cold boot with no away window. The reveal existed here only
  because of a 12-second app background during HeroSelect.
- `Player.log` is overwritten on next launch; the quoted lines were captured 2026-09-09 and should be
  re-obtained under headless capture if re-verification is needed.
