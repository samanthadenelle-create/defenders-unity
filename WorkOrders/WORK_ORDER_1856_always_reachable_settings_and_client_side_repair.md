# WORK ORDER 1856 — An always-reachable Settings door, plus a client-side "repair my session" action

**Status:** READY FOR LEAD REVIEW

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

---

## IMPLEMENTATION RECORD (SME agent, 2026-09-17)

Silo respected exactly as dispatched: `WaveManager.cs`, `BattleLock.cs`'s release logic,
`PursuitBattleProbe.cs` and `BattleSessionEnd.cs` were **read but not edited** (`git diff` against
this lane touches none of them — verified by the file list below).

### Files changed

- `Assets/_Modules/HUD/Kit/HudKitController.cs` — Part A: the safety-net Settings door.
- `Assets/_Modules/Core/UI/SessionRepairService.cs` (**new**) — Part B: the repair action.
- `Assets/_Modules/Settings/SettingsController.cs` — the "Repair Session" row + confirm/result UI.
- `Assets/Editor/Regression/SessionRepairRegression.cs` (**new**) — the pinning suite.
- `Assets/Editor/Regression/DataRegression.cs` — one registration line (`"session-repair suite"`),
  inserted above the `>>> REGISTERED ORACLE SUITES — END FENCE <<<` marker per that file's own rule.

### Part A — the door, and why it is built the way it is

`HudKitController.BuildSafetyNetSettingsDoor()` is the **literal first statement** of
`BuildWidgets()` (`HudKitController.cs`, called before `Transform pool = transform;` and before
every dock builder), wrapped in its **own** `Guard.Try`. Evidence this matters:
`VillageHudController.cs:109` calls `Guard.Try("HudKit", "build HUD kit", () =>
HudKitController.Create(this), null)` — the ENTIRE `BuildWidgets()` call is already inside one
outer Guard.Try. `Guard.Try` (`Assets/_Modules/Core/Diagnostics/Guard.cs:29`) catches and logs but
does **not** roll back GameObjects already constructed before a later throw — so building the door
first is what makes it survive a broken dock builder later in the same method, which is precisely
the WO's "must not depend on BuildAdaptivePeacefulDock having run correctly" requirement, read
literally against the actual failure mechanics.

The door lives on its **own root canvas** (`ElarionUiKit.BuildModalCanvas`, sortingOrder 4700),
never parented under `HudAreasHost`/`_host.transform`. Reason, read at source:
`BuildModeHudBridge.cs:65-68` calls `SetHudVisible(!building)`, and
`HudKitController.SetHudVisible` (`:401-407`) sets `_host.Group.alpha/interactable/blocksRaycasts`
to 0/false/false — i.e. Build Mode fades the WHOLE HudAreasHost CanvasGroup. Build Mode is one of
the contexts the WO explicitly lists (`town, battle, raid, dungeon, build mode`), so a door parented
under that group would go dark exactly there. It is also **not** one of the `_widgets` occupancy
rows `ApplyPosture` drives from `hud-areas.json`, because occupancy is keyed to the very posture
classification this door must survive being wrong.

The door reads **no** context/battle/panel signal at all — not `HudContextEvaluator`, not
`BattleLock`, not `PanelManager.AnyOpen`. It is unconditional by construction, which is the
strongest form of "independent of HudContextEvaluator's Battle/Town classification" the WO asks for:
there is nothing to misclassify. Pinned by `SessionRepairRegression.DoorBuiltUnconditionallyFirst`
(source-lint: the build-order and the absence of those four tokens in the builder's body) and
`DoorSurvivesForcedBattleLock` (live build with `BattleLock.RegisterProbe(() => true)` held — the
door still constructs, its Canvas exists, and its Button stays `interactable`/active).

Tap handler: `SettingsGate.RequestOpen("safety-gear")` — the existing Core seam, unmodified, with a
new `source` string so `[Flow:Settings]` traces can tell this door apart from the dock's `"dock"`.
Same `SwallowedByCloseGrace` guard every other HUD tap already goes through (WO-1393).

**Yield to dialogue — a corrected claim, not a copied pattern.** The WO's own text says to yield
"same as PauseGate/SettingsGate already do." Read both files in full: neither `PauseGate.cs` nor
`SettingsGate.cs` references `DialogueGateState` anywhere. That sentence in the WO was **not
accurate against the current tree** — this is a new behaviour, not an existing one being copied.
`HudKitController.TickSafetyGearDialogueYield()` (ticked from the existing per-frame `Update()`)
hides the door only while `DialogueGateState.PanelVisible` is true — a genuinely on-screen dialogue
— and deliberately does **not** consult `PanelManager.AnyOpen`, because hiding behind "any panel is
open" would recreate the exact lockout Part B exists to clear. Pinned by
`DoorYieldsOnlyToLiveDialogue`.

Visual: a plain gear glyph + the word "Settings" (`"⚙ Settings"`), built through the existing
`ElarionUiKit.BuildObsidianButton` factory — no new art, no color-only signaling (the WO's own
colorblind rule): the label is always text, and the yield above is a full show/hide, never a color
change.

### Part B — the repair action, and the deviation this hand-back surfaces

`SessionRepairService.Repair()` (new file, `DeNelle.Core.UI`):
1. Calls `BattleSessionEnd.Release("session repair")` — the **exact same call**
   `BattleQuiescenceGate`'s own self-heal makes (see that file's header and
   `BattleSessionEnd.cs:189-218`). This clears the pursuit-pulse ring
   (`PostureSignals.ClearPursuits()`) and runs every registered session-end unwind. It does **not**
   force `BattleLock` false and does **not** unregister any probe:
   `BattleSessionEnd.cs:61-63` states in its own header it "does NOT force BattleLock false", and
   `BattleQuiescenceRegression.LiveChaseIsNotSuppressed` (`:441-459`) already pins that a
   still-chasing pursuer re-raises the lock on its very next tick. `SessionRepairRegression.
   RepairDoesNotSuppressLiveChase` re-proves this specifically through the new service.
2. Force-closes whatever `PanelManager.OpenPanelName` reports open, through the panel's **own**
   `Close()` action (`PanelManager.CloseOpen()`), never by zeroing the arbiter's record blind — the
   same discipline the WO-1337 ghost-panel heal already established.
3. Does **not** call `HudContextEvaluator` or force a dock rebuild — it **cannot**:
   `HudContextEvaluator` is `internal sealed` inside `DeNelle.Village`
   (`HudContextEvaluator.cs:69`), and `DeNelle.Core` (where this file lives) cannot reference
   `DeNelle.Village` at all (CLAUDE.md §5 — the dependency runs the other way). It doesn't need to:
   `HudProducer.Tick` polls every producer at its own interval (`HudModelHost.cs:88-99`,
   `HudContextEvaluator`'s interval is `0.20f`, `HudContextEvaluator.cs:85`), and
   `PostureEvaluator.Update` polls every `0.15f` (`PostureEvaluator.cs:52,63-71`) reading the exact
   Core statics this service clears. So the next natural poll pair (≤0.35s combined) re-derives
   Town/Overworld and `HudKitController.ApplyPosture` rebuilds the dock with zero HUD-specific code
   in this file — which also means Part B never touches `DeNelle.HUD` or `DeNelle.Village` at all,
   the strongest possible form of "reused as-is."

**Never touches persistence — proof, not assertion.** `GameStateService` and `PersistenceBridge`
both live INSIDE `DeNelle.Core` itself (`Assets/_Modules/Core/State/`), so the asmdef boundary alone
cannot prove this (unlike, say, HUD→Village). The proof is a source-lint,
`SessionRepairRegression.RepairNeverTouchesPersistence`, which strips `//` comment lines from
`SessionRepairService.cs` (the file's own header names `GameStateService`/`PersistenceBridge` in
prose, deliberately, as documentation of what it must never call — exactly the
`PublicNavigationRetirementRegression.AssertAbsentInCode` shape) and asserts the remaining CODE
contains none of `GameStateService`, `PersistenceBridge`, `.Save(`, `SyncToBackend`. It is also true
by direct reading: the file's only imports are `DeNelle.Core.Combat` and `DeNelle.Core.Diagnostics`,
and its only calls are `BattleLock.DescribeHolders`, `BattleSessionEnd.Release`,
`PanelManager.OpenPanelName`/`CloseOpen`, `Guard.Try`, `FlowTrace.Step`.

**⚠ DEVIATION FROM ACCEPTANCE CRITERION #3 — SURFACED FOR LEAD RULING (CLAUDE.md §11B.B: never
silently reinterpret a written line).** The criterion reads: *"run while BattleLock is artificially
held by a test holder, releases it."* `BattleLock` exposes no force-unregister/override API, and
building one would contradict the "never force BattleLock false" law this codebase already
regression-enforces (see above) — an arbitrary `() => true` test probe cannot be "released" by
anything except unregistering it, which is destructive in production (real owners register once at
boot). `SessionRepairRegression.RepairClearsPursuitStuckLock` instead reproduces the **real captured
shape** (a stale pursuit pulse the battle opened and nothing closed — the WO-1855/1233/1603
incident) and proves `Repair()` clears exactly that, safely. `DoorSurvivesForcedBattleLock`
separately proves Part A's door does not care what `BattleLock` reports at all. If the lead wants
the literal "any test holder" case honored, that requires either a new force-clear API on
`BattleLock.cs` (outside this ticket's silo — that file is explicitly off-limits per the dispatch)
or a ruling that the acceptance line is superseded by `BattleSessionEnd`'s own "never force false"
law. This is written into the regression file's own header comment as well, not only here.

### Confirmation / result copy (Settings screen, new "Repair Session" row)

- Confirm: "Repair Session" / "This clears a stuck screen state. Your progress is not affected." /
  buttons "Repair" · "Cancel" — verbatim WO copy.
- Result: `SessionRepairResult.Summarize()` — "Cleared: 1 stuck battle lock, 1 stuck panel." or
  "Nothing was stuck - if you're still seeing a problem, this isn't it." — verbatim WO copy, built
  from what actually happened (never a static string).
- The row label itself ("Repair Session") is a **plain literal string**, not routed through
  `SettingsText`/canon-strings — a deliberate, flagged scope call (same shape as `HelpMenu`'s own
  plain toast strings, e.g. "Reset - heading back to Hero Select..."), so as not to fold a
  localization-data change (two canon-strings.json copies) into this ticket. Flagged as a possible
  follow-up, not built here.

### Non-scope honored

No save-integrity tool was built. No change to `HudContextEvaluator`'s Battle-vs-Town logic. No
backend/server change. `WaveManager.cs`, `BattleLock.cs`, `PursuitBattleProbe.cs`,
`BattleSessionEnd.cs` are unedited.

### Verification run by this agent

```
$ python tools/gate_brace.py Assets/_Modules/HUD/Kit/HudKitController.cs \
    Assets/_Modules/Settings/SettingsController.cs \
    Assets/_Modules/Core/UI/SessionRepairService.cs \
    Assets/Editor/Regression/SessionRepairRegression.cs \
    Assets/Editor/Regression/DataRegression.cs
GATE_BRACE_SUMMARY bad=0 of 5
```
NUL-byte scan: `0` bytes in all five files (Python `bytes.count(b'\x00')`).

**Not run by this agent, per explicit instruction:** the Unity `COMPILE_GATE_OK` /
`REGRESSION_OK <n>/<n>` batchmode gate, any build, or any commit — those are the lead's to run over
the combined tree (this lane's files plus WO-1855's, which touch a disjoint set:
`OverworldEncounterSpawner.cs` + `BattleQuiescenceRegression.cs`).

### Follow-ups flagged, not built here

- A true save-integrity/repair tool (explicitly out of scope per the WO's own non-scope section).
- Localizing the "Repair Session" row label through `SettingsText`/canon-strings.
- The acceptance-criterion-#3 deviation above needs an explicit lead ruling.
