# WORK ORDER 1856 — An always-reachable Settings door, plus a client-side "repair my session" action

**Status:** READY TO IMPLEMENT

**Minted:** 2026-09-17, by the CLI lead, live during the WO-1855 investigation, from the owner's own
words: *"i think this does show that we need a disection tool, if a plyer is stuck. THey will not
want to wait for a build to be fixed."*

**Silo:** HUD gating / Settings entry (`Assets/_Modules/HUD/Kit/HudKitController.cs`,
`Assets/_Modules/Village/HUD/HudContextEvaluator.cs`, `Assets/_Modules/Settings/SettingsController.cs`,
`SettingsGate`). Do not touch `WaveManager.cs` or anything in the WO-1855 silo (a different root
cause, a different fix, being landed separately).

## The concrete incident this generalizes from

Owner playtest, 2026-09-17, Seeker `SM02G4061955851`. She reported "stuck in battle model" and
separately "i have no way to get to [a] screen that i can access settings [from]". Live device
logcat pulled during triage:

```
[Flow:HUD] countdown IMMINENT (0.0s <= 5s) -> counts as Battle
[Flow:HUD] context inputs: sceneCombat=False wave=True battleLock=False pursuit=False inVillage=True
modal=False buildMode=False scene='Main_Castle_Overworld' -> Battle
```

`battleLock`, `pursuit`, and `modal` were all `False` — nothing was actually held. The HUD's
*context classification* alone (`HudContextEvaluator.IsWaveActive`, a parked endless-mode countdown
misread as imminent) was enough to route the HUD to the combat dock instead of the peaceful dock,
which is the ONLY door to the Settings slide-tab (`HudKitController.cs:5421`,
`AddDockTab(_slideDock.panel, dockRow++, "Settings", OpenSettings)` lives on the peaceful dock only).
That specific misread is WO-1736/WO-1855's territory to fix. **This ticket is the generalization the
owner asked for**: whatever the next context-classification bug turns out to be, it must never again
be able to take Settings away entirely, and a stuck player must never have to wait for a new APK.

## Scope

### Part A — Settings becomes reachable regardless of HUD context

Add ONE always-present, always-tappable affordance that opens `SettingsGate.RequestOpen` directly,
independent of `HudContextEvaluator`'s Battle/Town classification, independent of which dock
(`combatDock`/`peacefulDock`) is currently built, and independent of `PanelRouter`'s normal
context-gated panel rules. Requirements:
- Must render in EVERY HUD context this game has (town, battle, raid, dungeon, build mode) except
  where a full-screen modal already legitimately owns input (do not fight an active dialogue/cutscene
  gate — read `DialogueGateState` and yield to it, same as `PauseGate`/`SettingsGate` already do).
- Must not depend on `HudKitController.BuildAdaptivePeacefulDock` having run correctly — this is
  precisely the thing that failed in the captured incident.
- Visual treatment is a small persistent corner affordance (a plain gear glyph is fine; this is a
  safety-net control, not a design centerpiece — do not spend art budget here). Confirm the choice
  with a screenshot in the hand-back; the owner is colorblind, so state matters in words/shape, not
  color alone, same rule as everywhere else in this HUD.
- Existing `SettingsController`/`SettingsGate` plumbing is reused as-is — this ticket adds a second
  DOOR, not a second Settings screen.

### Part B — "Repair my session" action inside Settings

Add one row/button inside the Settings screen (reachable via the Part A door even when every other
door is broken) that does the following, and ONLY the following — this is a client-side, in-memory
reset, never a save mutation:
- Force-clears `BattleLock`'s held state (log every holder it force-released, by name, same shape as
  `BattleQuiescenceGate`'s existing self-heal logging).
- Force-clears `PursuitBattleProbe`'s registered probes.
- Force-closes any panel handle `PanelRouter` believes is still open.
- Re-evaluates `HudContextEvaluator` from scratch and rebuilds whichever dock that evaluation now
  calls for.
- Does **NOT** touch `GameStateService`/`PersistenceBridge`/anything that reaches the save file or the
  backend. This is explicitly NOT a save-repair tool — the save was never the thing at risk in the
  captured incident (`OnApplicationPause` already syncs before any of this could matter). Naming it
  "repair my session" rather than "repair my save" in all player-facing copy is deliberate; do not
  drift the copy toward implying save-file surgery.
- Shows the player a plain-language confirmation before running ("This clears a stuck screen state.
  Your progress is not affected.") and a one-line result after (what got cleared, in words: "Cleared:
  1 stuck battle lock, 1 stuck panel" or "Nothing was stuck — if you're still seeing a problem, this
  isn't it").

### Non-scope

- No actual save-file repair/validation tool. The owner's phrase "repair the save file" named the
  right instinct (a self-service fix that doesn't wait for a build) but the captured incident and
  WO-1855's RCA both show the save was never corrupted — only in-memory HUD/combat state was wrong.
  A true save-integrity tool is a different, larger ticket; do not fold it in here. Flag it in your
  hand-back as a follow-up if you think one is warranted, but do not build it under this number.
- No changes to when/why HudContextEvaluator picks Battle vs Town — that classification logic is
  WO-1736/WO-1855's territory. This ticket assumes that logic will occasionally be wrong again
  someday and builds the net under it.
- No server-side / backend changes.

## Acceptance criteria

- [ ] With the HUD deliberately forced into combat-dock/Battle context (a debug/test hook is fine),
      the Settings-opening affordance is still visible and functional.
- [ ] Tapping it opens the real `SettingsController` screen via `SettingsGate`, not a duplicate/stub
      screen.
- [ ] The "repair my session" action, run while `BattleLock` is artificially held by a test holder,
      releases it and the HUD returns to the context `HudContextEvaluator` actually computes for the
      current real state.
- [ ] The action never calls into `GameStateService.Save`/`SyncToBackend`/anything under
      `PersistenceBridge` — pin this with a regression the same way `PublicNavigationRetirementRegression`
      pins an absence (CLAUDE.md §7's Map-tab precedent).
- [ ] A regression test proves the Settings door survives at least the WO-1855/WO-1736 class of bug
      specifically: force `HudContextEvaluator` to report Battle while `battleLock`/`pursuit`/`modal`
      are all false, and assert the Settings door is still reachable.
- [ ] `node`/C# gate: brace balance + NUL scan on every file touched; Unity compile gate +
      DataRegression on the full tree.

## Test plan

1. Fresh boot, town, HUD in normal peaceful context. Confirm the new door is present and opens
   Settings identically to the existing dock route (no regression to the normal path).
2. Force `WaveManager` into the exact parked-Countdown-with-IsAwaitingPlayerStart-true state from the
   WO-1855/WO-1736 incident (or, if that fix has already landed by the time this lane runs, force
   `HudContextEvaluator`'s Battle branch directly via whatever seam is cleanest). Confirm the new door
   is still present.
3. Artificially register a `BattleLock` holder and a `PursuitBattleProbe` entry that never release.
   Run "repair my session." Confirm both are released and logged, and confirm no save/backend call
   fired (mock/spy on `GameStateService`).
4. Run the full regression suite; confirm no existing HUD/dock/dialogue-gate regression breaks.

## Rollback

Remove the new affordance and the repair action; both are additive UI, no schema/save changes exist
to roll back.

## Copy rules

Plain language, no jargon ("stuck screen state," not "context evaluator desync"). Never say "repair
save" — say "repair session" / "clears a stuck screen." No color-only state signaling (owner is
colorblind, CLAUDE.md standing rule).
