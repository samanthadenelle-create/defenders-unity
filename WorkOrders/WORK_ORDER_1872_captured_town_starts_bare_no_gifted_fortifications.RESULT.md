# WO-1872 RESULT — the captured town converts as the DESTROYED camp

**Status:** IMPLEMENTED PENDING LEAD GATE. Edit-only lane: no Unity run, no gate, no commit, no scene
edit, no locale edit. Everything below is a CLAIM for the lead to verify.

---

## 1. The evidence: STATE, not baked — and nothing "repaired" anything

The WO asked the lane to prove state-vs-baked first. The answer is **both, in a specific way**, and
the owner's correction is exactly right about the felt result while the mechanism is not a repair call.

**The bodies ARE baked** into `Assets/Scenes/OwnedTown_IronBastion.unity`: the owned town is a twin of
the raid base carrying `OwnedTemplateIdentity` components, and the save addresses them by
`templateStructureId` rather than instantiating them —
`OwnedTownScenePose.TryResolve` (`Assets/_Modules/Village/World/Camps/OwnedTownScenePose.cs:62-77`)
matches a saved pose to a baked `OwnedTemplateIdentity`, and
`OwnedTownLayoutSnapshot.Validate` (`Assets/_Modules/Village/World/Camps/OwnedTownLayoutSnapshot.cs:43-45`
and `:102-103`) **refuses a snapshot that is missing any manifest identity**. That refusal is why the
WO's first option — "filter defensive categories OUT of the captured template" — is not implementable
as written: dropping the 168 defensive records makes `TryReconstruct` fail outright and the town
refuses to load. Recorded because the next seat will reach for it too.

**Their CONDITION is state**, and that is the whole defect:

| Step | File:line | What it does |
|---|---|---|
| 1 | `Assets/_Modules/Village/World/Camps/RaidCaptureCensus.cs:47-49` | freezes a `Func<float>` per body returning its live `HpFraction` |
| 2 | `RaidCaptureCensus.cs:71` (pre-fix) | samples it at `Settle()` — the **victory-time** health — straight onto `record.condition01` |
| 3 | `Assets/_Modules/Village/World/Camps/OwnedTownSnapshotImporter.cs:97-102` | replays that fraction onto the baked twin via `RestoreOwnedTownCondition` |
| 4 | `Assets/_Modules/Village/Walls/WallSegment.cs:612` / `Assets/_Modules/Village/Buildings/DefenseTower.cs:325-330` / `Assets/_Modules/Village/World/Camps/RaidSpire.cs:293` | at condition `0` those calls `Collapse()` / mark broken + `Destructible.NotifyBroken` / `Raze()` |

A three-star clear does **not** require breaking the perimeter — razing the spire alone wins
(`Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:271`), and a garrison wipe wins too
(`:208-212`). So every wall and tower the player never attacked carried condition `1.0` across and
arrived **standing**. The owner's *"they arrive that way cause you repair the current camp"* describes
the felt result precisely; mechanically nothing repaired anything, the untouched sections simply kept
their health. **There is no repair call to delete** — a lane hunting for one would have found nothing
and bounced the ticket, which is why this paragraph is here.

The manifest (`Assets/Resources/OwnedTown/IronBastionTemplate.json`, 169 entries, read 2026-09-18)
carries **158 `wall_stone` + 10 Watchtowers + 1 `RaidSpire`**.

> ### ⛔ THE LOAD-BEARING TRAP: the Heart and the ten Watchtowers share a catalog id.
> Both are `tower_arcane_spire`. A filter keyed on `placement.itemId` — **or** on
> `CatalogEntry.repo.behaviorId` — therefore **cannot tell the town's Heart from a defense tower**, and
> would have razed the Heart along with the garrison. The manifest's `movableTower` flag is `false` for
> the walls **and** the Heart, so it cannot carry the distinction either. The only thing that knows is
> the **live component type at census time**. That is why `CapturedStructureKind` exists and is recorded
> in `RaidCaptureCensus`, and why every rule is a pure function of the kind.

---

## 2. Changes, with file:line

Thirteen files (10 in round 1, 3 oracle re-points in round 2). No scene, no locale JSON, no `DataRegression.cs`, no `RaidVictoryController` capture gate,
nothing owned by WO-1870/WO-1871.

| # | File | What changed |
|---|---|---|
| 1 | **NEW** `Assets/_Modules/Core/State/CapturedTownStanddown.cs` | The whole contract, Unity-free so the suite can drive it with no scene: `CapturedStructureKind` (`:19-27`), `ConditionOnCapture` — **the filter** (`:88`), `Salvage` floored per resource (`:100`), `IsRepairableDamage` (`:137`), `IsClearableRubble` (`:147`) |
| 2 | `Assets/_Modules/Village/World/Camps/RaidCaptureCensus.cs` | `Kind` recorded on `Entry` (`:22`, set `:54-55`); `Settle()` routes every entry through the filter and traces razed/kept **by name** (`:74-105`); the capture-supplies guard narrowed to **standing** damage (`:107-114`) |
| 3 | `Assets/_Modules/Core/State/OwnedBaseProgression.cs` | `TryInspectPristineTown` no longer counts rubble as damage (`:85-94`) |
| 4 | `Assets/_Modules/Village/World/Camps/OwnedTownLayoutSnapshot.cs` | the `retired && !movableTower` refusal carved out for walls, so the perimeter can be **cleared** while staying **unsellable** and the Heart stays refused (`:86-102`) |
| 5 | `Assets/_Modules/Village/World/Camps/OwnedTownConstructionService.cs` | `TryQuoteSale` gated to standing records (`:77-83`); **new** `SalvagePct` (`:148`) / `TryQuoteClear` (`:161`) / `TryClearRubble` (`:188`) |
| 6 | `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs` | `_rubbleSelected` (`:21`); `pristine` re-pointed at the shared predicate (`:55`); the ruin row — select / cycle / clear with a live quote (`:125-158`); `ClearRubble()` (`:324`) |
| 7 | `Assets/_Modules/Core/Ops/RemoteTunables.cs` | `KeyTownCaptureSalvagePct` (`:645`) + `TownCaptureSalvagePctDefault = 50` (`:656`) + the `Registry` row (`:1708`) |
| 8 | `api/_lib/tunables.js` (`:222-228`) + `docs/PROD022_TUNABLE_FLAGS.md` (row 79, `:87`) | the same key in the other two of the three sources `RemoteTunablesDefaultsRegression` case 4 `[key-domain]` compares |
| 9 | `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` (`:302-308`) | the **fourth** statement of the same default, in that suite's own `ExpectedDefaults` literal map. ⛔ **Adding a knob to Registry/js/docs WITHOUT this reds that suite**: `:1099` compares the found key set against `ExpectedDefaults.Length` for equality — *"never a subset"* (`:619`) — and `:556` says in so many words to update the oracle and the doc in the SAME commit. Found by re-reading the suite, not by running it |
| 10 | `Assets/_Modules/Village/World/Camps/OwnedTownRepairService.cs` (`:24-38`, `:42-51`) | rubble skipped in `TryChooseFirstRepair` (§5) + a `FlowTrace.Warn` on the no-candidate return, which was a silent `false` |

### Round 2 — three oracle pins re-pointed after the lead's RED regression (`Builds/reg1870`, 17:38)

| # | File | What changed |
|---|---|---|
| 11 | `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` (`:156`) | `ExpectedKnobCount` **78 → 79**. This is a **second, separate pin** from `ExpectedDefaults` (row 9) — the suite carries both, and the red (`Registry holds 79 knob(s), pinned at 78`) came from this one. Comment follows the file's dated convention |
| 12 | `Assets/Editor/Regression/OwnedTownConstructionRulesProof.cs` (`:25-40`, `:66-82`) | **the oracle moved WITH the ruling.** Its fixture seeded every movable tower at `0f` — **as rubble** — and then required `TryChooseFirstRepair` to return one (`No eligible damaged structure is available` was that `Require` throwing, not the service). Towers now seed at `.5f`: *standing and damaged*, which is what the case was always about (a usable defense tower, not the fixed objective, is the first repair) and is also the shape of a pre-WO-1872 save. **The deleted state is not lost** — an all-razed town is asserted directly at the end, with the ruling's answer: no repair offered, and a reason present |
| 13 | `Assets/Editor/OwnedTownScenePoseProof.cs` (`:60-82`, `:96-110`, `:137-158`) | **Not in the lead's red list — found by grepping every `TryChooseFirstRepair` caller, and it would have red next round.** Three re-points: (a) the `devastated` block forced all conditions to `0f` then demanded *"Total destruction must restore a tower for the design lesson"* — the force is now redundant (Settle razes) and the demand is inverted; (b) `"Captured wall condition differs from actual damage"` required the measured damage to **pass through**, which **is the defect** — inverted, and kept sharp by first asserting the fixture wall is genuinely `0 < c < 1`, so a pass-through filter cannot satisfy it; (c) the total-loss block's `TrueForAll(condition01 == 0f)` now fails **because the Heart is forced standing** — re-pointed, and it also pins that a total loss quotes **no** repair supplies |

⚠ **The design lesson does not soft-lock** when every inherited tower is rubble: `OwnedBaseConstruction.Finish` (`:106-113`) sets `LayoutChoiceCompleted` on **any** construction edit, and **clearing a ruin is one**. That is why the old "restore a tower so the design lesson is possible" rule (`OwnedTownRepairService.cs:10-11`) could be retired rather than worked around. Verified at source, not assumed.

⚠ **`clearableWall` cannot leak to the objective** — checked at source rather than reasoned: `tower_arcane_spire` is `behaviorId = ArcaneTower` (`Assets/Resources/Data/Canonical/structures-catalog.json`), not `WallSegment`, so the Heart stays unsellable **and** unclearable at the validator, and `OwnedTownConstructionRulesProof:39` ("The fixed objective must not be sellable") still holds. The ten Watchtowers remain retirable via `movableTower`, which is what makes them clearable rubble.

**NEW suite:** `Assets/Editor/Regression/CapturedTownStartsBareRegression.cs`, tag
`[captured-town-bare]`, markers `CAPTURED_TOWN_BARE_OK` / `CAPTURED_TOWN_BARE_FAIL`,
`public static bool Run(out string reason)`, never throws, no reflection.
**The lead registers it in `DataRegression.RunAll` — this lane did not touch that file.**

**New player-facing strings** are in the sidecar, not in locale JSON, per the brief:
`Logs/debug/scratch/sweep-sidecar-wo1872.json` — `ownedTown.clearRubble`, `ownedTown.nextRubble`,
`ownedTown.rubbleCleared`.

### How it plays now
The Bastion converts as the wrecked camp: 158 wall sections and 10 watchtowers arrive as rubble where
they fell, the Heart stands. The player clears each ruin from the town panel; each clear pays
`floor(build cost x town.captureSalvagePct)` through the normal wallet seam (standard cap/overflow —
the trace reports **credited**, not quoted, so a capped payout reads as a full bank rather than a bug),
retires the record and takes the body out of the scene. She then builds her own layout on the cleared
ground. **Build mode itself is untouched** (ruling point 4).

---

## 3. Why case A is RED on HEAD

`CaseA_DefensiveStructuresConvertRazed` has **two halves and needs both**:

1. `ConditionOnCapture(Wall|DefenseTower, m)` must return `0` for **every** measured `m` in
   `{0, .17, .5, .99, 1}`. On HEAD the conversion is the identity — a wall measured `1.0` converts at
   `1.0` — so the `m = 1f` iteration fails first, and its message names the defect.
2. `RaidCaptureCensus.cs` (**comment-stripped** source) must contain
   `CapturedTownStanddown.ConditionOnCapture`, must no longer assign the raw measured value, and its
   supplies guard must read `s.condition01 > 0f && s.condition01 < 1f`. On HEAD none of the three hold.

Half 2 is not decoration. Without it the pure rule could be perfectly correct and **completely
unused** — which is exactly what HEAD looks like. The source match is comment-stripped because this
repo documents its own history directly above the code it explains; a raw-text match matches the
prose, the false FAIL WO-1778 burned a combined-tree gate run on.

Cases B–E and their individual revert recipes are written into the suite header
(`CapturedTownStartsBareRegression.cs:53-61`).

### Revert recipe (whole ticket)
Restore `record.condition01 = Mathf.Clamp01(entry.Condition());` in `RaidCaptureCensus.Settle` and
delete `Assets/_Modules/Core/State/CapturedTownStanddown.cs` → case A red on both halves, B/D red,
and the captured town converts fortified again. The other seven edits are independently revertible:
#3/#4 only widen what is permitted, #5/#6 only add a verb, #7/#8 only add a knob.

---

## 4. ⛔ OPEN OWNER QUESTIONS — the lane did not decide these

1. **Save migration — NOT DONE, and deliberately so.** Per the WO, no existing save is mutated.
   **Only a NEW capture converts destroyed.** An owned base captured before this lands keeps its
   standing walls and towers for good; the standdown runs at capture time and never again.
   *Two consequences the owner should rule on together:*
   - Does she want an existing owned base re-razed into a ruin field (a one-shot migration on load), or
     left as it is? A migration would delete structures a player already owns, which is why it is not
     a lane call.
   - Because #4 relaxes the retire rule by category rather than by history, an **old save's standing
     walls become sellable** through the ordinary sale path (`TryQuoteSale` refuses only rubble). New
     captures are unaffected — their walls are rubble and go down the clear path. If that is unwanted,
     the cleanest fix is a `revision`-stamped gate, and it needs her word.
2. **The salvage share is `50`** (`town.captureSalvagePct`, WO ruling point 3 read literally: "default
   0.5"). Percent rather than a fraction because `RemoteTunables` carries **Int and Bool knobs only —
   there is no Float accessor**. It is live-tunable without a build, so her number can land any time.
3. **The Heart is FORCED to standing, not passed through** (`CapturedTownStanddown.HeartCondition`).
   Stated plainly because it is a decision, not a derivation: razing the spire is itself a win
   condition (`RaidVictoryController.cs:271`), so on the commonest three-star route the Heart's
   measured condition is exactly `0`, and passing that through would convert a town with **nothing
   standing in it at all**. Ruling point 1 says "The Heart and non-defensive dressing still convert",
   and forcing it is the only reading under which it does. If she wants a battered Heart she takes
   over and repairs, that is a one-line change here plus a repair-lesson re-think.

## 5. The reachable half of the repair hazard — CLOSED; the unreachable half recorded

A late re-trace found the hazard was **not** unreachable, and it is now closed at the chooser.

- **CLOSED — the "Repair more" screen could have offered a full-cost repair of rubble.** The panel's
  `repairMore` button (`OwnedTownPanel.cs:159-167`) is gated on `!pristine`, so once anything standing
  takes damage the repair screen opens; `OwnedTownRepairService.TryChooseFirstRepair` skipped only
  `retired || condition01 >= 1f`, and a razed watchtower is neither — it even still satisfies
  `manifest.IsEditableTower`. The player would have been quoted a full-cost rebuild of a ruin: the
  exact *"you repair the current camp"* the ruling removes, and one the body would then refuse to
  honour (`WallSegment.Repair:620` / `DefenseTower.Repair:338`, WO-753). **Fixed** by
  `OwnedTownRepairService.cs:24-38` (`|| record.condition01 <= 0f`) plus the matching list in
  `OwnedTownPanel.cs:194-198`. It cannot strand the screen: that screen is reachable only when a
  STANDING damaged record exists, and by then the repair milestone is done so `needsTower` is false
  and that record is eligible.
- **RECORDED, not fixed — `OwnedBaseProgression.TryRepair` still accepts a zero-condition record.**
  It refuses a retired one (`:113`) and a full one (`:115`) but not a razed one, so a ruin can be
  "repaired" in the save while the body stays rubble — a state/body desync. **This pre-dates WO-1872**
  (any genuinely destroyed structure already hit it); the standdown widens the surface. Deliberately
  not fixed: `Assets/Editor/Regression/OwnedTownRepairPaymentProof.cs:40` repairs a fixture named
  `"ruin-1"` at condition `0` **on purpose**, so refusing it is a separate ruling with its own oracle.
  With the chooser fix above no player route reaches it; a hand-built call still can. Suggest a
  follow-up WO.
- **The world-tap route is unchanged.** Clearing is driven from the town panel's select/cycle/act
  shape — the one `ShowRepairControls` and the tower row already use — rather than a new interaction
  system, per "reuse the town interactable pattern, never a new system". `BuildModeController.cs:2620`
  → `OwnedTownPanel.SelectStructure` still works for any ruin carrying a `PlacedStructure`. If the
  owner wants a literal world tap on each ruin, that is a follow-up and it touches build-mode input,
  which this WO holds out of scope.

## 6. Verification this lane ran (and did not run)

```
python tools/gate_brace.py <8 files>   ->  GATE_BRACE_SUMMARY bad=0 of 8   (exit 0)
CLAUDE.md section 1 brace + NUL one-liner -> all 8 balanced, no NUL bytes   (exit 0)
```

⛔ **`COMPILE_GATE_OK` and `REGRESSION_OK n/n` have NOT been run** — this is an edit-only lane and does
not fire Unity. Acceptance item 2 (a headless or device capture of the owned town after a three-star
Bastion clear, showing a ruin field rather than standing walls) and the owner felt-verify are both
still open. Nothing here is proven until the lead gates it.
