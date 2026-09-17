# WORK ORDER 1804 - RESULT

**Status:** IMPLEMENTED
**Lane:** implementation (Progression / Raids). No Unity gate run, no commit - as briefed.
**Date:** 2026-09-16

---

## 1. Registration line for the lead (DataRegression.cs is fenced from this lane)

Add inside `DataRegression.RunAll`, beside the other progression suites:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "battle-plans suite", () => { if (!BattlePlansRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[battle-plans] " + r); });
```

Marker on success: `BATTLE_PLANS_OK`. On failure: `BATTLE_PLANS_FAIL: <reasons>`.

---

## 2. Files

### New
| Path | Purpose |
|---|---|
| `Assets/_Modules/Village/Progression/BattlePlans.cs` | domain: keys, threshold const, pure spawn/gate/CTA rules, pure copy composition |
| `Assets/_Modules/Village/Progression/BattlePlansPickup.cs` | walk-over prop for both kinds; HELD flag write; hand-off signal; owner-tagged aura |
| `Assets/_Modules/Village/Progression/BattlePlansService.cs` | bootstrap + 1 Hz scan (town drop, dungeon boss hook, reveal scheduling) |
| `Assets/_Modules/Village/Progression/BattlePlansReveal.cs` | full-screen three-beat reveal, ONE CTA |
| `Assets/_Modules/Village/Progression/BattlePlansRevealVM.cs` | the reveal's pure ViewModel - every state read, the composed beats, the CTA decision AND the CTA command |
| `Assets/Editor/Regression/BattlePlansRegression.cs` | 8-case suite, marker `BATTLE_PLANS_OK` |

### Edited
| Path | Hunk |
|---|---|
| `Assets/_Modules/Core/Tutorial/TutorialSignals.cs` | one block after `RaidAttempted` (line ~190): `PlansRevealedPrefix` / `BattlePlansRevealed` / `BastionPlansRevealed` |
| `Assets/_Modules/Village/World/SceneConfigCatalog.cs` | `SceneConfigDef` + `unlockedByPlans`, `plansDungeonId`, `plansDungeonName` (after `unlockVictories`) |
| `Assets/_Modules/Village/Hero/RaidSelectionVM.cs` | HUNK 1: `PlansGateProvider` + `PlansDungeonNameProvider` after `ClaimedProvider`. HUNK 2: the plans branch at the top of `ResolveLock`. Both marked `WO-1804 HUNK n of 2 IN THIS FILE`. |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs` | ONE hunk, marked `(e2) WO-1804 - THE ONLY HUNK THIS TICKET ADDS TO THIS FILE`, in `OpenInternal` after the `ClaimedProvider` wiring |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs` | ONE hunk, marked `WO-1804 -- THE ONE HUNK THIS TICKET ADDS TO THIS FILE`, at the top of `Update()`. **No member's visibility was changed.** |
| `Assets/Resources/Data/Canonical/scene-configs.json` | 3 fields on the `iron_bastion` row |
| `Assets/StreamingAssets/Data/Canonical/scene-configs.json` | the byte-identical twin |

⚠ **TWO OF THESE FILES ARE BEING CO-EDITED RIGHT NOW.** The harness reported
`Assets/_Modules/Village/Hero/RaidSelectionVM.cs` and `Assets/_Modules/Core/Tutorial/TutorialSignals.cs`
changed on disk between this lane's read and its edit (the WO-1802 lane). Both hunks landed cleanly on
top of those changes and nothing was reverted - **but the lead must diff both files at gate time against
both lanes' claims** before staging by path.

### Fenced and untouched (verified)
`CastleDefensePlansService.cs`, `CastleDefensePlansPickup.cs`, `SpirePlansCelebration.cs`,
`TutorialFlow.cs`, `PlayerDeckWorkspace.cs`, `PackStore.cs`, `StorePackCard.cs`,
`BuildStructureInfoPanel.cs`, `RaidDeployController.cs`, `TroopController.cs`, every StarterArmy grant
file, every `.unity`, `DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`.

---

## 3. Proofs measured this session

| Check | Result |
|---|---|
| `python tools/gate_brace.py` over all 11 `.cs` | `GATE_BRACE_SUMMARY bad=0 of 11`, exit 0 |
| Raw brace parity per file | 21/21, 22/22, 40/40, 41/41, 27/27, 43/43, 158/158, 12/12, 29/29, 82/82, 53/53 |
| NUL bytes (`\x00`) per file | 0 in all 11 (`BAD=0 of 11`) |
| Case 8's own source lint, simulated against the tree | `CASE8_LINT_SIM: PASS` |
| **`hub-scene-literal` rule, simulated** (comments stripped, over its real scan roots) | `HUB_LITERAL_SIM: PASS - no typed hub in scanned roots` |
| **`UI-MVVM CONFORMANCE` rule, simulated** (candidate detection + banned symbols + VM routing, exactly as the oracle's regexes read) | `BattlePlansReveal.cs  view=True  banned=0  routed=True -> conformant`; the VM / service / pickup are not View candidates |
| JSON twins byte-identical | `twins byte-identical: True`, 20565 bytes each |
| JSON line endings preserved | CRLF 361 -> 364, LF 361 -> 364 (exactly the 3 added lines; no reflow) |
| JSON parses, row reads back | `{'id':'iron_bastion','unlockedByPlans':'bastion_plans','plansDungeonId':'dg_ember_deep','plansDungeonName':'The Ember Deep'}` |

### Facts read at source 2026-09-16 (not from any doc)
* `CastleDefensePlansService.RequiredWavesSurvived = 3` (`:81`); the new camp threshold is 2 and the
  suite asserts `camp < spire` off both consts.
* `raider_camp_small.displayName = "The Forsaken Camp"`; `iron_bastion.displayName = "The Iron Bastion"`,
  `unlockVictories 0`, `rewardMultiplier 2.2`, `sceneName RaidBase_IronBastion`.
* **`RaidBase_IronBastion.unity` is registered `enabled: 1`** in `ProjectSettings/EditorBuildSettings.asset`
  (line 54). The VM's 09-04 note about it being disabled is now stale - the "Raid the Iron Bastion" CTA
  lands on a loadable scene.
* `BuildModeController.EnterBuildModeForStructure(string)` is public at `:500`; `EnsureExists()` at `:420`.
  `ManageScreenPanel.OpenPlacementFor` (`:6786-6791`) is the existing caller and it `Close()`s first -
  which is why the reveal tears down before invoking the CTA.
* `OutpostEnemyGroupSpawner.IsBossGroup` (`:117`), `IsBossCleared` (`:118`), `BossCleared` (`:116`),
  `RoomId` (`:125`) are all public; `ComposedDungeonHost.InstallBossContract` (`:271-290`) uses the
  identical `IsBossGroup` predicate and the identical `IsBossCleared` re-entry check.
* `dg_ember_deep` exists as an authored composed dungeon (`Assets/Resources/Data/Canonical/dungeon-graphs/dg_ember_deep.json`,
  six levels, boss = an Ogre warlord) and is one of the six ids `DungeonComposedPillarsRegression` lists.
  `DungeonWorldPortalSpawner.cs:1580` builds its def as `MakeDef(EmberDeep, EmberDeep, "The Ember Deep", ...)`
  - id and sceneName are the SAME string, which is what makes the scene-name identity check legitimate.
* `DungeonExitInteractable.Leave` (`:1012`), `RequestExitConfirm` (`:895`) and `ExecuteLeave` (`:1015`)
  are all **private**, and `ExecuteLeave` routes a rich dungeon through `_onLeave` =
  `DungeonController.ExitToVillage`, which banks the run's crafting scatter (`:1047-1052`).
* `RaidClaimService.IsClaimed` (`:496`) is permanent; `IsRepeatClearInCycle` (`:151`) is the routed
  cooldown read. `RaidSelectionScreen` must NOT reference `RaidCooldownService` directly -
  HeartfireRegression PIN F reds the file for it, which its own `(f)` comment states. The new wiring
  respects that.
* No existing suite CONSTRUCTS `RaidSelectionVM` from the real catalog - `RaidEscalationRegression`,
  `RaidSelectionSpoilsRegression` and `RaidSelectionVMTests` all inject fabricated
  `SceneConfigDef`s that author no `unlockedByPlans`, so the new plans gate cannot red them.
* Reading the REAL rows from an Editor regression IS established practice, so cases 6 and 7 are not
  inventing a loader dependency: `RaidEscalationRegression` (`:128`, `:228-229`),
  `RaidSceneCoverageRegression` (`:158-159`), `RaidCooldownRegression` (`:218`),
  `RaidDifficultyTunablesRegression` (`:176`) and `RaidSelectionSpoilsRegression` (`:435`) all call
  `SceneConfigCatalog.Find` / `.All`, and the first two call `SceneConfigCatalog.Invalidate()` first.
  The new suite follows that precedent and invalidates before each real read.
* `DeNelle.Dungeons.asmdef` references `DeNelle.Village`; `DeNelle.Village.asmdef` has **no**
  Dungeons entry - so the Village -> Dungeons call the WO asked for is a cycle and cannot exist. Both
  files read 2026-09-16.
* `DungeonExitInteractable.RequestExitConfirm` re-runs `CanLeave` and raises the Continue/Cancel modal;
  `ExecuteLeave` is reachable only through that Continue face. A latch-claimed request therefore cannot
  skip the gate, the confirm, or the banking.
* The source lint in case 8 matches **`GoRaid(`**, not `GoRaid`: the bare token appears 3x in
  `BattlePlansReveal.cs` - twice in comments and **once inside a runtime FlowTrace string** - so a
  word-match lint failed on the very file it was asserting. Simulated the suite's lint against the tree
  after the fix: **PASS**.
* The owner's VFX tag file authors **no** Plans / Reveal / Drop / Unlock / Reward key (grepped the 152
  keys in `Assets/Resources/VFX/vfx-pick-options.generated.json`), and
  `CastleDefensePlansService.BuildVisual` never calls `VFXManager` - the castle plans drop has no key
  of its own to reuse. `Treasure_Aura` was chosen and is documented in-file as the substitution.

---

## 3b. The two full-tree regression failures (`data-regression.log` 19:08), and what they cost

### (1) `hub-scene-literal FAIL` - a typed hub name in my own suite
Case 8 asserted the Bastion reveal plays in a hub by **typing the hub's name**. Fixed the way WO-1765
fixed `CameraRaidFramingRegression`: **iterate `SceneRouter.CastleCandidates`** rather than read
`SceneRouter.Castle`, because `Castle` resolves only the branch `ff.MergedWorld` happens to be flagged
into and the claim must hold for **both** hubs - the legacy `MainCastle_Hall` (still on disk per
CLAUDE.md §7) included. Iterating is strictly stronger and the assertion now names the hub it checked.

**Swept every file this lane touched** (the lint reports only the first literal per file, so one green
line proves nothing about the rest): the only remaining occurrence anywhere is inside a **comment**,
which that oracle strips - the identical shape `CameraRaidFramingRegression` passes with today.

### (2) `UI-MVVM CONFORMANCE VIOLATION` - the reveal read state directly
`BattlePlansReveal.cs` reached for the save-state singleton at 4 sites. **The oracle was right** and
the fix is a real ViewModel, not a dodge. Two dodges were available and both were declined, out loud:

* **Gaming the routing token.** That oracle's routing check is **file-level by its own admission** - a
  View that merely *names* `IPanelViewModel` drops out of the offender set even with the reads still in
  place. Adding the token would have turned the gate green while the defect stayed. So **every state
  read moved**, and the View now passes the **banned-symbol** half too, not just the routing half
  (simulated: `banned=0`).
* **Copying the allow-list exemption.** `SpirePlansCelebration.cs` is allow-listed as a one-shot
  cinematic controller that *"reads only its own seen-flag"*, and this screen is built in that exact
  idiom - so the entry was there for the taking. **It would have been a lie:** this reveal also reads
  the **live army roster** to compose its middle beat, which is precisely the live game data that
  exemption promises is absent. An allow-list entry whose stated reason is false is worse than a
  violation, because the next reader believes it.

**The split:** `BattlePlansRevealVM` (pure C#, `IPanelViewModel`, `CreateDefault`) owns the once-ever
flag (read + mark), the live army count, the Barracks check, where the player stands, the camp row, the
spoils projection, the Echo roster, the composed beats, the CTA decision **and** the CTA command. The
View binds it, renders it, and routes the tap (`vm.InvokeCta()`). The portrait crosses as a **name**,
never a `Sprite`, because `IPanelViewModel` forbids UnityEngine types on that seam.

**Knock-on the coordinator could not have known about:** case 8's lint asserted that *the View* sets the
two latches. With the CTA correctly moved to the VM, that lint would have gone **red on the MVVM fix** -
one gate punishing the fix for the gate beside it. Re-pointed at the VM, and it now also asserts the
View still only *routes* the command, so the doors cannot be re-inlined later.

### The same mistake, three times, and it is worth recording
My prose about a lint kept tripping that lint - these oracles are plain source scans with **no comment
or string stripping**. `GoRaid` in an explanatory comment and in a runtime `FlowTrace` string; then
`GoRaid(` inside the docstring that explained the rule; then the banned symbol's name in the header
vouching for the file. Each one would have pressured the next author to **delete the explanation to
make the gate green**. The rule now written into both files: *describe a banned symbol, never type it* -
and match a **call shape** (`(` included), because prose never carries the parenthesis.

---

## 4. Unproven / owner's call

1. **THE MOMENT'S FEEL IS THE OWNER'S, ON DEVICE.** Beat pacing (4.4 / 6.4 / 6.0 s), the CTA's
   position and whether the reveal lands as a marquee moment cannot be judged from here. No screenshot
   was captured: this lane ran no Unity.
2. **NOTHING WAS COMPILED OR RUN.** No `COMPILE_GATE_OK`, no `BATTLE_PLANS_OK`, no headless play. Brace
   and NUL parity are not compilation. Every namespace and signature above was read at source, but the
   only proof that the tree builds is a gate run.
3. **ONE HONEST GAP IN THE NEVER-RE-LOCK RULE, recorded rather than closed.** There is **no per-camp
   ATTEMPTED record anywhere in the tree** - `RaidFunnel`'s first-raid latch is install-wide
   PlayerPrefs, not per camp (its own note at `RaidFunnel.cs:332` and `RaidDoorReadiness.cs:60-76`
   both draw that distinction). So the witnesses are CLEARED/CAPTURED (`IsClaimed`, permanent) and
   in-cooldown-cycle. A save that **attempted the Iron Bastion, lost, and whose cooldown has since
   lapsed** derives "no contact" and is gated once until the plans are found. That costs that player
   one dungeon run and never a conquest; closing it would mean minting a new PlayerPrefs key, which
   this lane declined to do unilaterally. **Owner's call if she wants it closed.**
4. **RESOLVED by the owner's ruling "keep the reveal in the boss room" (2026-09-16).** The reveal now
   plays at the boss. ⚠ **The cheap fix named in the previous revision of this item was WRONG and could
   not have compiled:** making `DungeonExitInteractable.RequestExitConfirm` public does not help,
   because `DeNelle.Dungeons` references `DeNelle.Village` and not the reverse (both asmdefs read at
   source), so a Village type can never call a Dungeons method however visible it is. Implemented
   instead as two latches on `BattlePlans` (the one assembly both sides can see), consumed by the
   dungeon's own exit on its existing tick - **no member's visibility was changed and the banked
   `ExecuteLeave` path plus the player's own Continue/Cancel confirm are both still in the loop.**
5. **`plansDungeonName` is a second copy of the string "The Ember Deep."** The only other copy is a
   code literal inside a private builder (`DungeonWorldPortalSpawner.cs:1580`) that nothing outside
   that method can read. The duplication is pre-existing and is named in the field's own docstring
   rather than silently re-created; if a dungeon display-name catalog is ever added, this field points
   at it instead.
6. **⚠ THE `OpenRaidGrid` CTA CAN LAND ON A REFUSAL, AND IT IS PER SPEC.**
   `RaidSelectionScreen.Open()` recomputes `ArmyReadiness` and, when the save is short of
   `RequiredSlots` (3 while `everCompletedRaid` is false, the WO-823 soft gate), **toasts and redirects
   to the drillmaster instead of opening the grid**. The CTA branches on Barracks presence ONLY, as
   briefed - so a player with a Barracks but under 3 deployable bodies taps "RAID THE FORSAKEN CAMP"
   and gets the drillmaster. `RaidDoorReadiness`' own header says why that shape is dangerous ("teaches
   that the HUD lies"). It is expected to be RARE because WO-1803's starter-army grant fires with the
   Barracks - **but that is the sibling lane's claim, not something this lane measured.** If the owner
   wants it closed, the fix is a third CTA branch on `ArmyReadiness`, which needs her word because it
   changes the one-CTA ruling.

7. **The hand-off is NOT one-sided - the sibling lane already consumed it (verified).**
   `Assets/_Modules/Village/Tutorial/V2/TutorialSignalAdapters.cs:411-440` references
   `TutorialSignals.PlansRevealedPrefix` and `TutorialSignals.BattlePlansRevealed` by name and matches
   the whole PREFIX family in its `OnBusSignal`, clearing the raid-door chain's re-raise cooldown so
   its current rung raises on the next tick. The two lanes meet on the id with no edit to each other's
   files. Read at source 2026-09-16; not a claim.

8. **Same-session boss re-arm only.** `OutpostEnemyGroupSpawner.IsBossCleared` is runtime, and
   `DungeonRuntimeState.BossDefeated` is explicitly per-RUN ("defeated this run", `_runActive`-gated,
   `DungeonRuntimeState.cs:460-469`) and lives in the Dungeons assembly that `DeNelle.Village` cannot
   reference (the reverse edge already exists). So a player who leaves the Ember Deep without walking
   over the plans meets a fresh boss next run and drops them again - nothing is lost, and only the HELD
   flag ends the offer. The in-file comment was narrowed to say exactly this.

9. **The boss-room route spends TWO scene-scoped hops and nothing proves the hand-off end to end.**
   The latches, the scheduling predicate and the absence of a raid route are pinned headless (case 8),
   but "tap in the boss room -> confirm -> banked exit -> hub -> grid opens" has **not been run**. It
   is the one path in this feature whose failure mode is the player stranded in a dungeon, so it is
   the first thing to drive in a headless or device pass.

10. **The MVVM split is proven by SIMULATION of the oracle's regexes, not by the oracle.** I ran the
    candidate/banned/routing rules against the tree and the View reads `banned=0 routed=True`, but the
    real `UiMvvmConformanceRegression` has not executed here - and it is the authority. Same for
    `hub-scene-literal`. Both need the lead's gate run to be facts rather than well-founded claims.

11. **`BattlePlansRevealVM.Changed` never fires today.** The VM is composed once and not mutated, so the
    `IPanelViewModel` contract is honoured structurally (the View subscribes and re-renders the CTA face)
    without inventing a refresh loop this one-shot moment does not have. If a future beat needs live
    data, the seam is already there - stated so the next author does not re-invent it or mistake the
    silence for a missing wire.

12. **The `plansDungeonId` / `plansDungeonName` pair is read by BattlePlansService only.** If the
   owner moves the drop to a different dungeon by editing `plansDungeonId`, the lock sentence follows
   only if `plansDungeonName` is edited too. Two strings, one intent - called out here rather than
   derived, because no dungeon display-name catalog exists to derive from (see item 5). `plans.revealed:battle` /
   `plans.revealed:bastion` are raised and pinned by name in the suite, but nothing consumes them yet -
   by design, the sibling lane owns `TutorialFlow.cs`. If that lane chose a different id, the suite
   reds and names the mismatch, which is the intended failure mode.

---

## 5. Board

This WO's own `**Status:**` line is flipped to `IMPLEMENTED, NOT YET GATED` in the file itself.
`BOARD.html` regeneration (`python tools/board_build.py`) and the commit are the lead's, per the brief.
