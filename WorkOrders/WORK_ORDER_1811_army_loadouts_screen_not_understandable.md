# WORK ORDER 1811 — The Armies screen is not understandable

**Status:** IMPLEMENTED
**Silo:** UI / Village.Troops (ArmyMusterPanel + ArmyMusterVM)
**Owner report (2026-09-16 20:15, verbatim, binding):**
> "the armies training screen is where im confused, i have no way to understand it"
> "can you catch this screen and make it simpler, if i cant understand it noone else will"

**Primary evidence:** `logs/device/seeker-army-2010.png` (Seeker, build 372984, landscape 2670x1200),
opened and read this session. Title "ARMIES - LOADOUTS"; preset tabs `RAID *ACTIVE*` / `HOLD` / `SIEGE`
/ `CLEAR`; Gold 2292; unit rows Footman "45s each" 4, Archer "1m each" 3, Spearman "2m each" 3 with
-/+ steppers; a right well reading `STAGED: Wall Hold (slot 1) / 3x Spearman / 3x Archer / 4x Footman /
Cost: Free / Time: 6m 15s (2 train slots) / Train queue: 0 of 5 used, 5 free. / Fits now: 5 of 10 (rest
stays staged).`; a chip `SHORT OF: Army room`; bottom `NAME: WALL HOLD`, `SAVE SLOT 1`, and
`Train Army / 5 start now - 5 stay staged`.

---

## 1. What every confusing line ACTUALLY means (read at source, this session)

| What the player reads | Where it comes from | What it really means |
|---|---|---|
| `STAGED: Wall Hold (slot 1)` | `ArmyMusterPanel.BuildDetail` (`ArmyMusterPanel.cs:701-702`) printing `ArmyMusterVM.ArmyName` + `ActiveSlot+1` | The name of the saved LOADOUT PRESET being edited. "Staged" is an internal word for "planned but not ordered"; the player has no model for it. |
| `Cost: Free` | `preview.Cost` (`ArmyMusterPanel.cs:719`), always a zero `ArmyCost` — `MusterPreview.Cost` doc at `ArmyMusterService.cs:96-102` says it is ALWAYS ZERO since WO-1387/WO-1586 | Training charges nothing. Nothing on this screen costs gold, yet a Gold chip sits at the top right (`ArmyMusterPanel.cs:343-351`). |
| `Time: 6m 15s (2 train slots)` | `ArmyMusterPlanner.BatchSeconds` + `preview.TrainSlots` = `BuildTimerService.SlotCount(ChannelId.Train)` (`ArmyMusterService.cs:170-175`) | "2 train slots" is queue CONCURRENCY — a different axis from queue DEPTH (the WO-911 §2d trap). It is engine vocabulary. |
| `Train queue: 0 of 5 used, 5 free.` | `preview.LineDepth` vs `ArmyMusterPlanner.TrainQueueDepthCap` (`ArmyMusterPanel.cs:725-727`) | Queue DEPTH cap = 5 items. True, but it is a third number system on one screen. |
| `Fits now: 5 of 10 (rest stays staged)` | `WouldFit = min(TotalUnits, LineRoom)` — `ArmyMusterService.cs:240` | ⚠ **THIS IS QUEUE ROOM, NOT ARMY ROOM.** The "5" is free queue places; the "10" is the staged plan total. It only LOOKS like the army cap because plan=10, cap=10 and queue-cap=5 coincided in this capture. It does not contradict "Army is full 10/10" — it is a different axis, unlabelled. |
| `SHORT OF: Army room` | `PlanSlots > ArmyRoom` where `ArmyRoom = CapSlots - RosterSlots - QueuedSlots` (`ArmyMusterService.cs:251-277`) and `RosterSlots` counts WOUNDED troops (`ArmyReadiness.cs:62`, `Army.SlotsUsed`) | The army has no free slots. The wounded troops occupying those slots are **shown nowhere on this screen**, so the message has no visible cause. |
| `Train Army / 5 start now - 5 stay staged` | `UpdateCta` (`ArmyMusterPanel.cs:889-893`) | See the live defect below. |
| the `-` / `+` steppers | `ArmyMusterVM.Step` -> `ArmyComposition.Add` | They set a TARGET COMPOSITION for the preset, not "train N more". `Muster()` then enqueues EVERY staged unit (`ArmyMusterService.cs:262-269`), so "4 Footman" while owning 4 means "train 4 MORE". Nothing on screen says which. |

### 1b. A LIVE DEFECT, not just confusing copy
`UpdateCta` (`ArmyMusterPanel.cs:889-893`) decides `interactable` and the "N start now" subline purely
from `WouldNotFit` / `LineRoom` — the QUEUE axis. It never reads `preview.Affordable` / the army-room
branch. With a full roster the button stays enabled and promises "5 start now"; pressing it runs
`Muster()` -> `BarracksService.EnqueueTraining`, which refuses every unit at
`rosterSlots + committed + unitSlots > cap` with `stopReason = "Army is full."`
(`BarracksService.cs:385-386`). The screen promises what the action then refuses.

### 1c. WO-1810 changed the model under this screen (same day)
`ArmyStorage.cs:250-263`: a raid no longer PRODUCES wounded troops — the fallen are REMOVED
(`RemoveOwned`). `MarkWounded` / `AdvanceRecovery` survive as the backstop, so wounded can still exist
(and the owner's 3 wounded came from build 372984, the pre-WO-1810 model). The new copy therefore
shows the recovering line ONLY when `wounded > 0`, and reads correctly at zero wounded.

---

## 2. The redesign (owner tie-breaker: do what Clash of Clans does)

One screen, three questions, top to bottom:

* **(a) WHAT YOU HAVE** — an army bar: `Army 7 of 10`, plus, only when non-zero,
  `3 recovering - ready in 19m`. Numbers come from `ArmyReadiness.Compute` + `ArmyStorage.Owned`
  through the VM; the view computes nothing.
* **(b) WHAT TO TRAIN** — one row per unlocked troop: name, `45s each - you have 4`, and a single
  **Train** button that queues ONE of that unit through `BarracksService.EnqueueTraining(id, 1, ...)`
  (the sanctioned single-troop path — no second enqueue invented). A right-hand **IN TRAINING** well
  shows what is training and one total time.
* **(c) GO** — the raid door, live only when the army is ready (`ArmyReadiness.Snapshot.Ready`);
  otherwise it states what is missing.

**Presets move OFF the primary surface** into a `Loadouts` drawer (the old tabs / steppers / Name /
Save slot / `Train Army` bulk order, unchanged). The preset DATA MODEL is untouched.

**Copy rules:** no "staged", no "SHORT OF", no "slot 1" on the primary surface; no Gold chip (nothing
here costs gold); every string through the localisation table; every touch target >= `MinTouchPx`;
greyscale-safe (words in boxes, never hue); landscape.

## 2b. OWNER RULING 2026-09-16 ~20:50 (verbatim, binding — arrived mid-implementation)
> "full army but we need to know how many troops are trained or how many are deployed. If you change
> the you want 3 archers and 2 healers and rest footman, you need to remove ones from active army,
> and either return them to gold or to a staged ready troop"

What it settles, and how it is implemented:

1. **The raid gate is UNCHANGED** — `ArmyReadiness` still asks for the full cap after the first raid.
   Nothing in this ticket touches that rule.
2. **TRAINED vs DEPLOYED are two numbers, said separately.** Every troop row now reads
   `45s each - 4 active, 1 reserve` (`+2 training` while the queue holds any), and the army bar
   carries `Reserve N` beside the recovering/room line. ACTIVE = holds a cap slot and goes on the
   raid; RESERVE = trained, kept, holds no slot, never deploys.
3. **Rebalancing a full army = removing, and every removal is a CHOICE** offered in a per-troop
   sheet: **Move to Reserve** (kept, swappable back, offered only while the army has room for the
   return) or **Dismiss** (gone, pays gold). ⚠ **There is no price to refund** — training has
   charged nothing since WO-1387 (`ArmyMusterService.cs:96-102`; `BarracksService.EnqueueTraining`
   spends nothing), so "return them to gold" cannot be "what you paid". The dismissal pays a
   **percent of the troop's catalog gold value** (`TroopDef.CostGold`) behind the new tunable
   `army.dismissRefundPercent` (ships **50**, PROPOSED — the owner ruled the verb, not the number).
4. **"Staged" stays banned in player copy.** The owner's "staged ready troop" is the **Reserve**,
   and that is the only word the screen uses for it.
5. **The wounded/recovering line stays.**
6. **Save schema v41 → v42**: `ArmyStorage.reserve` (a `List<PlayerTroop>`), additive on the nested
   Army JSON, `MigrateToV42` seeds it empty and says so. A SEPARATE LIST rather than a flag on
   `PlayerTroop`, so `SlotsUsed` / `GetDeployable` / `CountOfDef` — the arithmetic every train and
   raid gate reads — are untouched and "not counted against the raid cap" is true by construction.
   Both moves route through the existing seams (`RemoveOwned`, WO-1810, on the way out).

## 3. Files in scope
* `Assets/_Modules/Village/Troops/ArmyMusterVM.cs` — the new headline strings + per-row projection + `TrainOne`.
* `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs` — primary surface + loadouts drawer.
* `Assets/StreamingAssets/Data/Canonical/<locale>.json` + `Assets/Resources/Data/Canonical/<locale>.json` — new `armyScreen.*` keys (10 locales x 2 mirrors, `LocaleParityRegression`).
* `Assets/Editor/Regression/ArmyScreenCopyRegression.cs` (new) + its registration in `DataRegression.cs`.
* `Assets/Editor/UICaptureLaunch.cs` — a capture entry for this panel (none existed).

**Do NOT touch:** `TroopRecoveryService`, the raid-end reconcile, `ArmyStorage` mutation (WO-1810 lane),
`ArmyMusterService` (read-only here).

## 4. Acceptance
1. The primary screen never prints "staged", "SHORT OF", "slot", "Cost: Free", or a Gold balance.
2. Army bar numbers add up: `used + room == cap`, wounded shown separately when > 0.
3. Train is refused/dimmed when the army has no room — the button can no longer promise what
   `EnqueueTraining` refuses (§1b).
4. A headless capture at 2670x1200 with a 7/10 army and 3 wounded is readable in greyscale.
5. `ArmyScreenCopyRegression` red-first on the banned words; registered in `DataRegression`.
6. Brace gate + NUL check clean.
