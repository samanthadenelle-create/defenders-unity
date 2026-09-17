# WORK ORDER 1788 — The FTUE "skip all" **silently deletes the owned-town repair/design/reentry teaching** before the player has a town — and practice is taught by nothing at all

**Status:** READY FOR LEAD REVIEW

**Implemented 2026-09-17** by the tutorial lane. Files: `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs`
(SkipAll scoped by `ctx.Scene`; the `:706` "Scene is DEAD DATA" comment amended, since this is its first
live read), `Assets/_Modules/Core/Tutorial/TutorialSignals.cs` (new `OwnedTownPracticeReady`),
`Assets/_Modules/Village/Tutorial/V2/TutorialSignalAdapters.cs` (`TickOwnedTownPracticeReady`, the 1 Hz
save-flag poll), and both `tutorial-steps.json` copies (v10 -> v11, row `owned_town_practice` order 840).
`python tools/gate_brace.py` exit 0 / `bad=0 of 3`; no NUL bytes; both JSON copies parse, are md5-identical
and uniformly CRLF. **No Unity process was run** — the lead gates the combined tree.

**Blast radius, measured 2026-09-17:** the three owned-town rows are the ONLY contextual rows in
`tutorial-steps.json` carrying a `scene`, so the skip-scoping change is a no-op for every other beat.

**Pre-existing finding, NOT fixed here (out of silo):** `ownedTown.reentered` is raised from
`OwnedTownController.Start` under `[DefaultExecutionOrder(-500)]`, i.e. BEFORE `TutorialFlow.Start`
subscribes `TutorialSignals.Raised`, and the flow never replays a latch on arm — so that raise can be
missed on the very scene entry that satisfies it. This is exactly why the new practice beat is triggered
by a poll rather than by `ownedTown.reentered`.

**⚠ Acceptance 2 (Run B "screenshot it") cannot show anything yet:** per §3.2 the hint/objective copy is
the owner's and is authored BLANK. `TutorialFlow` skips an empty hint rather than painting an empty toast,
so the beat is silent by construction until her words land — the `CTX-ENTER :: owned_town_practice`
FlowTrace line is the only proof it fired.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** tutorial — `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs`, `Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json`. **No `.unity`, no bake, no combat, no route logic (WO-1778 lane), no capture copy (WO-1783 lane).**

**Build under test:** `2026.09.16.371701`, built 2026-09-15 22:06 from `f6653501f`.

**Video path:** act 2 shots 13-14 (repair, then practice). If the owner's filming save ever pressed skip, **neither beat will prompt on camera.** P1.

---

## 1. SYMPTOM

Two separate holes in the teaching for the captured town:

1. A player who pressed **skip** during the opening FTUE — hours earlier, in a different scene, before a town existed to inherit — will **never** be shown how to repair the inherited town, how to place her defense tower, or that she must re-enter to confirm the save.
2. **Nothing anywhere teaches practice**, yet completing practice is a required milestone.

## 2. EVIDENCE — read at source 2026-09-16

**2a. The three owned-town steps exist and are correctly authored.** `Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json:6-29`, scene `OwnedTown_IronBastion`, all three `"oneShot": true, "skippable": false`:

| id | order | hint | objective |
|---|---|---|---|
| `owned_town_repair` | 810 | *"Restore a damaged structure with your capture supplies."* | *"Restore your town"* |
| `owned_town_design` | 820 | *"Choose a defense tower and give it a new position."* | *"Choose your defense layout"* |
| `owned_town_reentry` | 830 | *"Reenter your town to check that your design was saved."* | *"Return to your saved town"* |

The once-only gating works as designed: `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs:2778-2779` marks `CtxSeenPrefix + ctx.Id` on completion, and `:2794-2798` `CtxSeen` refuses a step already seen.

**2b. ⛔ THE DEFECT: the global skip marks EVERY oneShot contextual step seen — including these three.** `TutorialFlow.cs:1690-1693`. Note `"skippable": false` on all three rows **does not protect them** — that flag governs whether the step itself offers a skip, not whether the global skip consumes it. So a tap in the opening FTUE, before the player has any town, permanently erases the teaching for a beat that arrives hours later.

**2c. Practice has no step at all.** `grep "practice" Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json` returns **nothing** — while `Assets/_Modules/Core/State/OwnedBaseProgression.cs:12-17` (`AllMilestones` / `IsAiArenaReady`) **requires `PracticeCompleted`**. The panel's own prerequisite refusal exists (`Assets/_Modules/Core/State/OwnedTownPracticeSession.cs:23-24` — *"Repair, design, save and reenter the town before practice."*), so the game can refuse practice but never invites it.

⚠ **What is NOT broken:** the practice screen's honesty copy is present and correct — `Assets/Resources/Data/Canonical/en.json:8` *"Defeat the three attackers. Your saved town is safe; practice has no rewards or wagers."* (rendered at `OwnedTownPracticeController.cs:164-166`), plus `:6` *"Consumables are not spent in practice."* Do not touch those.

## 3. THE FIX

1. **`TutorialFlow.cs:1690-1693` — scope the global skip.** It must not consume a contextual step whose scene the player has never entered. The cheapest correct rule: skip only marks steps whose `scene` matches the current one (or that carry no scene), leaving `OwnedTown_IronBastion` steps unseen until she is in that scene. ⚠ **Check whether other late-game contextual steps are caught by the same bug while you are in there** — the fix is general, and a `grep` of `tutorial-steps.json` for other scene-scoped oneShot rows names the blast radius.
2. **Add a practice step**, order ~840, scene `OwnedTown_IronBastion` (the panel is where the button lives), `oneShot: true`. ⛔ **Copy is the owner's** — leave the hint/objective strings blank for her, per memory `vfx-map-owner-tags-no-creative-pick`. The row, the ordering and the wiring are this ticket; the words are not.

## 4. ACCEPTANCE

Two device runs on the filming build:

1. **Run A** — a save that pressed "skip all" in the opening FTUE, taken to a 3-star Bastion capture: all three owned-town steps fire, screenshot each hint.
2. **Run B** — the same save after repair/design/reentry: the practice step fires, screenshot it.
3. A capture line per step showing `CtxSeen` was false at the time it fired.
4. `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.
5. ⚠ `tutorial-steps.json` is a **canonical JSON** file — patch from HEAD bytes, prove the LF count, update any `Assets/StreamingAssets` twin in the same change (memory `canonical-json-edits-binary-only-verify-newlines`).

## 5. DO NOT TOUCH

The opening FTUE's own steps and ordering. The practice honesty strings (`en.json:6`, `:8`). The capture moment (WO-1783). The route-home strands (WO-1778). Any `.unity` file.
