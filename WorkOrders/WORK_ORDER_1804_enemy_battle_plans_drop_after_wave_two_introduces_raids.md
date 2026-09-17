# WORK ORDER 1804 - Enemy Battle Plans (wave 2) + Bastion Plans (dungeon boss): two plans drops that introduce raiding

**Status:** IMPLEMENTED
**Silo:** Progression / Raids (Village) - file-disjoint from WO-1802 (TutorialFlow / PlayerDeckWorkspace) and WO-1803 (StarterArmy grant)
**Opened:** 2026-09-16
**Number:** PRE-ASSIGNED by the lead. `CLI_LANES_WO_NUMBERS.md` was NOT touched by this lane.

---

## 1. The rulings this implements

**Owner ruling 2026-09-16 (the wave-2 drop), verbatim:**

> "the way how we handle after the third wave we have that very special moment where they get the new
> plans why don't we after the second wave introduce raids the same similar way look you dropped
> detailed battle plans or something like that click here to you build your barracks and let's step
> into that"

**Owner ruling 2026-09-16 (the same evening - the hackathon video's act 2):** the video STARTS at the
end of a dungeon. The player kills the boss, the boss drops BASTION PLANS, the plans open the Iron
Bastion, and the player goes to raid it. The Iron Bastion is therefore LOCKED until the Bastion Plans
are held; the other three flagship camps are unchanged.

**Owner ruling 2026-09-16 (which dungeon), verbatim:**

> "Make it the one that's the ember deep"

So the authored default is `dg_ember_deep`, carried as DATA on the `iron_bastion` scene-config row.

**Owner ruling 2026-09-16 (where the reveal plays), verbatim:**

> "keep the reveal in the boss room"

This SUPERSEDES the first build's hub-deferral. The Bastion reveal now plays at the boss. The run's
banking is protected by the CTA's route instead of by the screen's location - see §2.

---

## 2. What was built - TWO KINDS ON ONE SEAM

Both kinds ride the WO-1013 castle-plans seam: a persistent self-bootstrapping service, a 1 Hz
spawn-from-persisted-state scan, a walk-over prop, and a full-screen beat in the
`SpirePlansCelebration` / `StoryIntroController` cold-open idiom.

| | KIND 1 - **Enemy Battle Plans** | KIND 2 - **Bastion Plans** |
|---|---|---|
| Dropped by | the wave-clear beat after `BattlePlans.RequiredWavesSurvived` waves (2) | the boss of the authored dungeon (`plansDungeonId`, default `dg_ember_deep`) |
| Resolved by | `HeroHealth` / `WaveSpawnPoint` / `WaveManager` - **all by COMPONENT** | `OutpostEnemyGroupSpawner.IsBossGroup` + its `BossCleared` event - **by COMPONENT** |
| Exposes | `raider_camp_small` ("The Forsaken Camp") | `iron_bastion` ("The Iron Bastion") |
| HELD flag | `ProgressionUnlocks` id `enemy_battle_plans` | `ProgressionUnlocks` id `bastion_plans` - **this flag IS the Bastion's key** |
| Reveal once-ever key | `enemy_battle_plans_reveal` | `bastion_plans_reveal` |
| Hand-off signal | `plans.revealed:battle` | `plans.revealed:bastion` |
| CTA | `BUILD YOUR BARRACKS` (no Barracks) / `RAID THE FORSAKEN CAMP` | `BUILD YOUR BARRACKS` / `RAID THE IRON BASTION` |

### The reveal says three things, none of them a literal
* **WHERE it is** - the camp's `displayName`, read from `scene-configs.json` via `SceneConfigCatalog.Find`.
* **WHAT it pays** - `RaidSelectionVM.FormatSpoils(RaidSelectionVM.EstimateSpoils(def))`, i.e. the
  settle payout's own chain (`RaidScoring.EstimateSpoils` -> `ProjectLoot` -> `ComputeLoot`). No second
  loot table, no digit.
* **THE ARMY IS READY** - live `GameState.Army.GetDeployable()` body count, in the identical shape
  `RaidSelectionScreen.CountDeployableTroops` uses. **It BRANCHES**: 0 bodies says "raise a Barracks
  and the first squad is free", UNKNOWN (-1) says nothing at all. "Your army is ready" is never printed
  over an empty roster.

### ONE CTA, and it is a real door
`BattlePlans.ResolveCta(barracksBuilt)` is a pure branch. No Barracks ->
`BuildModeController.EnterBuildModeForStructure("barracks")` (the same door
`ManageScreenPanel.OpenPlacementFor` uses). Barracks -> `RaidSelectionScreen.Open()`.
⚠ `RaidSelectionScreen.Open()` **takes no argument - there is no preselect API and none was added**
(the file fence forbade it): the grid opens and the camp is its own row, which is stated in the CTA's
own FlowTrace line.

### The Bastion lock
`iron_bastion` now authors `unlockedByPlans: "bastion_plans"`. `RaidSelectionVM.ResolveLock` checks
that gate **FIRST** - and it is the REAL lock, the one `OnCardTapped` reads, not a display word (the
WO-1542 scar: a card reading LOCKED opening anyway under a lit BEGIN ASSAULT). Sentence:
**"Find the Bastion Plans in The Ember Deep."**

⛔ **NEVER RE-LOCKS A BASTION THE PLAYER HAS BEEN TO.** `BattlePlans.ShouldGateBastion(plansHeld,
everClaimed, onCooldown)` refuses the gate on any prior contact, witnessed by `RaidClaimService.IsClaimed`
(PERMANENT, set at every clear/capture) plus the routed `IsRepeatClearInCycle`. Rows that author no
`unlockedByPlans` - which is every other row - are never asked and behave exactly as before.

### The reveal is in the boss room, and the route is the banked exit
The Bastion plans **drop, are picked up AND are revealed at the boss** (owner ruling: *"keep the reveal
in the boss room"*). What crosses the scene load is the CTA, as **two latches - never a route**:

1. **"RAID THE IRON BASTION"** tapped in the boss room -> `BattlePlans.RequestRaidGridOnHubArrival(campId)`
   (latch 2) + `BattlePlans.RequestDungeonExit(why)` (latch 1). It loads nothing.
2. `DungeonExitInteractable` **claims latch 1 on its own existing proximity tick** and raises its
   **ordinary Continue/Cancel confirm** -> `ExecuteLeave` -> `_onLeave` = `DungeonController.ExitToVillage`,
   which **banks the run's crafting scatter**. The request cannot skip `CanLeave`, cannot skip the
   player's own "Continue to exit", and cannot skip the bank.
3. The first hub frame after that, `BattlePlansService.TryConsumeRaidGridRequest` opens the grid.

⛔ **No `SceneRouter.GoRaid` and no scene load is reachable from the reveal or the pickup** - pinned by
a source lint in case 8 (matched on the call shape `GoRaid(`, not the bare word, because the word
legitimately appears in the comments AND in one runtime trace string that explain the rule).

⚠ **NO METHOD IN `DungeonExitInteractable` WAS MADE PUBLIC, and the named fix could not have worked.**
The WO asked for `RequestExitConfirm` to be widened and the CTA pointed at it. That **cannot compile**:
`DeNelle.Dungeons` references `DeNelle.Village` and **not the reverse** (both asmdefs read 2026-09-16),
so the reveal - a Village type - can never call a Dungeons method however visible it is, and widening
one would have added public surface nothing in the tree could reach. The latch respects the existing
dependency direction; **every member of that class stays private**. This is strictly narrower than the
named fix.

**A third CTA face was required to avoid a WO-1542 mismatch.** "BUILD YOUR BARRACKS" in a boss room is
a label whose door cannot open (build mode is a town verb), so outside a hub with no Barracks the face
becomes **"RETURN TO THE CASTLE"** (latch 1 only) and the WO-1802 helper chain's `NoBarracks` rung picks
the player up in town. `ResolveCta` therefore takes `(barracksBuilt, inHub)`.

**The toast is now a FALLBACK ONLY.** It fires once per kind after `FallbackToastAfterScans` (5) scans
in which the reveal still has not been delivered - a frame that genuinely cannot render the screen
(CustomDialogue off, an overlay that will not build, an arbiter that keeps refusing). It never fires
alongside a reveal that worked, which would be a second voice over the moment.

### Sequenced VFX
No new spawner, no new pool (`docs/ARCHITECTURE_PRINCIPLES.md` §2b.1). The prop reuses the castle
plans' exact visual grammar (primitive satchel + point-light glint + `PoiBeacon.Landmark` pillar,
neutral pale gold, meaning never carried by hue) **plus ONE owner-tagged key**:
`Treasure_Aura` -> `Lana Studio/Casual RPG VFX/Prefabs/Loot/Loot_iddle.prefab`, mapped verbatim
through the existing `VFXManager.PlayKey`, nothing rescaled, no tint imposed.
⚠ **The castle plans drop carries NO VFX key of its own** (read at source: `BuildVisual` never calls
`VFXManager`), and the owner's tag file authors **no** Plans / Reveal / Drop / Unlock / Reward key.
`Treasure_Aura` is the nearest tagged key whose authored meaning is exactly this - unclaimed loot on
the ground - and it is already bound to `DungeonTreasureCache`, the very grammar the castle plans
header names. If the owner tags a plans-specific key, one const changes.

---

## 3. Files

**New (all `Assets/_Modules/Village/Progression/`, assembly `DeNelle.Village`):**
| File | What it owns |
|---|---|
| `BattlePlans.cs` | the domain: keys, the threshold const, the pure spawn / gate / CTA rules, the pure copy composition |
| `BattlePlansPickup.cs` | the walk-over prop for both kinds, the HELD flag write, the hand-off signal, the tagged aura |
| `BattlePlansService.cs` | bootstrap + the 1 Hz scan: town drop, dungeon boss hook, and each reveal opened where its CTA works |
| `BattlePlansReveal.cs` | the full-screen three-beat reveal with ONE CTA - a DUMB SKIN: it reads no game state |
| `BattlePlansRevealVM.cs` | the reveal's pure `IPanelViewModel`: every state read, the composed beats, the CTA decision and the CTA command. Added after `UiMvvmConformanceRegression` correctly failed the first cut as a NEW state-reading View. |

**New (regression):** `Assets/Editor/Regression/BattlePlansRegression.cs` - marker
`BATTLE_PLANS_OK` / `BATTLE_PLANS_FAIL`.

**Edited (named hunks):**
| File | Hunk |
|---|---|
| `Assets/_Modules/Core/Tutorial/TutorialSignals.cs` | one block after `RaidAttempted`: `PlansRevealedPrefix` + `BattlePlansRevealed` + `BastionPlansRevealed` - **the named meet point with WO-1802** |
| `Assets/_Modules/Village/World/SceneConfigCatalog.cs` | `SceneConfigDef` gains `unlockedByPlans`, `plansDungeonId`, `plansDungeonName` (additive, absent-means-off) |
| `Assets/_Modules/Village/Hero/RaidSelectionVM.cs` | **HUNK 1** `PlansGateProvider` + `PlansDungeonNameProvider` beside `ClaimedProvider`; **HUNK 2** the plans branch at the top of `ResolveLock`. Nothing else. |
| `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs` | **ONE hunk** `(e2)` in `OpenInternal`: the two provider wirings. Nothing else. |
| `Assets/_Modules/Dungeons/DungeonExitInteractable.cs` | **ONE hunk** at the top of `Update()`: claim the pending-exit latch and raise the existing confirm. **No visibility changed, nothing made public.** |
| `Assets/Resources/Data/Canonical/scene-configs.json` + `Assets/StreamingAssets/Data/Canonical/scene-configs.json` | three fields on the `iron_bastion` row. Byte-identical twins, verified. |

**NOT touched** (fenced): `CastleDefensePlansService.cs`, `CastleDefensePlansPickup.cs`,
`SpirePlansCelebration.cs`, `TutorialFlow.cs`, `PlayerDeckWorkspace.cs`, `PackStore.cs`,
`StorePackCard.cs`, `BuildStructureInfoPanel.cs`, `RaidDeployController.cs`, `TroopController.cs`,
any StarterArmy grant file, any `.unity`, `DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`.

---

## 4. Acceptance criteria

1. The Enemy Battle Plans spawn at the wave-clear beat once `WavesCompleted >=
   BattlePlans.RequiredWavesSurvived`, in the defended town only, **never during a live wave**, once
   per save, and **never at all** on a save whose `EverCompletedRaid` is true (skipped with a
   `FlowTrace.Step` naming the reason).
2. The Bastion Plans spawn at the boss of the dungeon named by `iron_bastion.plansDungeonId`
   (`dg_ember_deep`), resolved by component, once per save, and re-derive from state on dungeon
   re-entry if the boss is already down.
3. Walk-over collection persists the kind's HELD flag in the SeenTutorials store (**no schema bump** -
   the castle plans did not bump either) and raises `plans.revealed:<kind>` exactly once.
4. The reveal shows once ever per kind, with ONE CTA that branches on Barracks presence AND on
   whether this is a hub, and hands off without leaving a modal over build mode. The **camp** reveal is
   town-only; the **Bastion** reveal plays **in the boss room** (and still in a hub, for a save that
   walked out before seeing it).
4b. The Bastion CTA's route is the **dungeon's own banked exit** - latch, confirm, `ExecuteLeave`,
   then the grid on the first hub frame. **Never `GoRaid` and never a scene load from the reveal.**
5. `iron_bastion` is locked with "Find the Bastion Plans in The Ember Deep." until the plans are held -
   **except** on any save with prior contact, which is never re-locked. The other three camps are
   untouched at `unlockVictories` 0.
6. Every step is a `FlowTrace.Step`: drop spawned (with the wave number / boss scene), picked up,
   reveal shown (with the CTA branch), CTA tapped, handed off.
7. `BATTLE_PLANS_OK` on a fresh log; `gate_brace` + NUL clean on every `.cs`; JSON twins byte-identical.

---

## 5. Not in scope

* No preselect of the camp row in `RaidSelectionScreen` (no API exists; adding one is outside the fence).
* No visibility change in `DungeonExitInteractable` - the assembly direction makes a direct call
  impossible, so the latch is the narrower fix (see §2).
* No change to the raid-door helper chain (WO-1802 owns it; the two lanes meet on the signal id).
* No starter-army change (WO-1803).
* No `DataRegression.cs` registration - the line is in the RESULT for the lead to apply.

---

## RULED IN 2026-09-16 - the Bastion half is in scope for the video

*(Header re-pointed by the owner's later ruling the same evening. The section body below is kept
VERBATIM as first recorded, including its "Not built now" line - that line described the position
before the ruling. What changed: the **Iron Bastion** half IS built, in §2 of this WO. What did NOT
change: Camp II and Camp III keep `unlockVictories` 0 and are NOT gated by dungeon plans.)*

> "if we want to really tighten better, we could give the first raid as plans, and the other raid grades are unlocked by plans that are in each of the dungeons"

Shape: the wave-2 Enemy Battle Plans drop (WO-1804) unlocks Camp I; Camp II / Camp III / the Iron Bastion are unlocked by plans found in specific dungeons, replacing the win-count ladder (today every flagship raid is `unlockVictories` 0 per the WO-1705 ruling of 2026-09-11 - this direction supersedes that ruling if adopted). Not built now: it would lock the Bastion capture shot (video act 2) behind dungeon runs and it is new-feature scope under the 09-16 stop-loss rule. Revisit after 2026-09-30 with a WO of its own.
