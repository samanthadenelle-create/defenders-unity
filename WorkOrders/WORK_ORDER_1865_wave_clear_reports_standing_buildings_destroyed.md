# WORK ORDER 1865 — Wave-clear damage report names STANDING buildings "destroyed"

**Status:** BLOCKED — AWAITING OWNER RULING (see §6)
*The code half is written, red/green-proven and ready for the lead to gate + commit (see the
RESULT file). The ticket is NOT Done and deliberately NOT Fixed: `Fixed` means "on her device,
awaiting the felt test", and **the owner's felt symptom is unchanged by this change on purpose** —
she would replay, see the same "Forge destroyed" row, and re-open an RCA against working code. One
owner word on §6 unblocks it.*
**Silo:** Village / Buildings-Progression + Waves (code only, no scene files)
**Opened:** 2026-09-18 — live owner bug report during a felt-test
**Oracle:** `Assets/Editor/Regression/DestroyedStructureRegression.cs` probe E
**Evidence:** `Logs/device/raid-trace-20260918-080200.txt` (owner's Seeker, this morning)
**Result:** `WorkOrders/WORK_ORDER_1865_wave_clear_reports_standing_buildings_destroyed.RESULT.md`

---

## 1. The symptom, in the owner's words

After a raid wave cleared, the wave-clear damage report said the **Forge** and **Lumber Mill**
were destroyed. Both are standing, built and max/near-max level in the town right now.

## 2. ⛔ THE ORIGINAL HYPOTHESIS WAS WRONG — RECORD THIS BEFORE READING ON

The ticket was opened on the theory that the report reads a **pre-repair snapshot** or a **sticky
"was hit for lethal damage" flag that repair never clears**, because the trace shows a "repair
scan" running immediately before the report is built.

**Both halves of that theory are disproven at source:**

- `WaveDamageReport.Collect()` (`Assets/_Modules/Village/Waves/WaveDamageReport.cs`) holds **no
  snapshot and no per-wave flag.** Every row is classified from LIVE state read in that call:
  `frac = c.IsBroken ? 1f : 1f - c.HpFraction`. There is nothing stale for it to read.
- The "repair scan" line (`WaveFeedbackDirector.cs:174`) precedes
  `_repair.SurfaceWorstRepair()` — a **UI nudge that surfaces the worst repairable structure.**
  It repairs nothing. Its ordering relative to the report is irrelevant.

**The report was the only honest voice in the system.** It must not be "fixed".

## 3. Proven root cause — an IMPOSSIBLE state: full HP *and* flagged destroyed

Read off the device log, in order:

| Time | Line | What it proves |
|---|---|---|
| 07:27:13.960 | `[Flow:Harvest] collector-destroyed building=forge` | Forge genuinely broke |
| 07:28:26.003 | `[Flow:Harvest] collector-destroyed building=lumbermill` | Lumber Mill genuinely broke |
| 07:28:26.114 | `[Flow:RepairProbe] BURNING 'lumbermill' hp=0.00 broken=True` | honest break state |
| 07:28:44.078 | `[Flow:WaveClear] damage report: 6 damaged, 2 destroyed` | **correct at this point** |
| 07:28:51.446 | `[Flow:Manage] building choice id=forge level=4/4 state=Max` | the town says BUILT and MAXED |
| 07:42:03.019 | `[Flow:BuildTimer] upgrade 'lumbermill' applied ... = 4` | the player successfully UPGRADED a "destroyed" building |
| **07:51:47.855** | **`[Flow:RepairProbe] BURNING 'forge' hp=1.00 broken=True`** | **THE DEFECT: full health AND flagged destroyed** |
| 07:53:50.897 | `[Flow:Vendor] Forge anchored to 'Weaponsmith' for 'forge'` | its vendor was re-spawned — nothing treated it as dead |
| 07:59:48.637 | `[Flow:Repair] hub repair affordance: HIDDEN because RepairAllCost() priced NOTHING` | the repair backend sees NO damaged structure |
| 07:59:48.865 | `[Flow:WaveClear] damage report: 0 damaged, 2 destroyed` | **the same two shells, 31 minutes and many waves later** |

### The mechanism

`ResourceCollector.Awake` and `ResourceCollector.Configure` both ran:

```csharp
LoadState();                    // _hp = pref (0 for a destroyed collector)
                                // _broken = _hp <= 0f           -> TRUE
if (_hp <= 0f) _hp = _maxHp;    // ...and immediately erased the only evidence for it
```

Two mutually contradictory statements, one line apart, **both born in the same commit
`b08293c93` (2026-07-09)** — confirmed with `git log -L196,197` vs `-L993,993` on the file.

`LoadState` defaults an **absent** pref to `_maxHp`, so `_hp <= 0` after a load means exactly one
thing: **this collector is persisted-destroyed.** The guard therefore never protected an
uninitialised collector — there is no such state — it only ever half-revived a dead one.

### Why it became permanent, and player-visible

In the resulting `hp=1.00 / broken=True` state:

- `IsAlive` = `_hp > 0f && !_broken` → **false**; `IsActive` → false → **no accrual, ever**
- `Repair()` returns at its `if (_broken) return;` guard (**WO-753**) → **nothing can clear it**
- `WallRepairController` excludes it from Repair-All as DESTROYED → the hub affordance prices
  nothing and hides, so the player is never even offered a fix
- `WaveDamageReport` reads `IsBroken` → **a "destroyed" row at every wave clear, forever**
- Meanwhile the mesh stands, `ResourceBuildingState` says level 4, and Manage offers upgrades

## 4. What was changed

| File | Change |
|---|---|
| `Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs` | The two copies of the post-load HP seed are replaced by **one** `SeedHpAfterLoad(via)`. It seeds HP **only when the collector is not flagged destroyed**, so `_hp` and `_broken` can no longer disagree. When a collector loads destroyed it emits a permanent `FlowTrace.Warn` naming id, hp, the persisted pref, the progression level and the unreachable-rebuild-door consequence. |
| `Assets/_Modules/Village/Waves/WaveDamageReport.cs` | Each `Entry` now carries diagnostics-only `Source` / `LiveHpFraction` / `LiveBroken`, and `Collect()` emits **one `FlowTrace.Step` per row** — name, surface, verdict, damage fraction, live HP, broken flag — plus an explicit `** CONTRADICTION **` clause when a row is DESTROYED at full health. **No classification logic was touched.** |
| `Assets/Editor/Regression/DestroyedStructureRegression.cs` | New probe **E** in the existing WO-753 oracle + header and verdict text. |

**Behaviour is otherwise unchanged by construction:** every consumer of a broken collector
(`IsActive`, Repair-All exclusion, `RepairAvailabilityProbe`, `StructureDamageVisuals`, the report
predicate) branches on `IsBroken`, not on HP, so making HP honest changes no outcome — it only
stops the state from lying. The 07:51:47 log shows the ruin/scuff visuals already fired off
`IsBroken` at `hp=1.00`, so there is no visual delta either.

## 5. Acceptance criteria — THE CODE HALF ONLY

⛔ **Deliberately contains NO "owner felt-verifies" box.** Her symptom is unchanged by this change
(§6), so a felt-verify box here would be unsatisfiable, and a WO with an unsatisfiable box is not a
WO. The felt-verify belongs to whichever ruling §6 produces, and is listed there.

- [x] Mechanism proven from captured data before any edit (CLAUDE.md §12)
- [x] Report classification logic left alone — it was correct
- [x] FlowTrace instrumentation added and **permanent** (never stripped)
- [x] `python tools/gate_brace.py` clean on all three files
- [x] Red proof captured headless: `Builds/wo1861-red.log` — `case1 ... IsBroken=True hpFrac=1.000`
- [x] Green proof captured headless: `Builds/wo1865-green.log` — `case1 ... IsBroken=True hpFrac=0.000`
- [x] Green half of the pair pinned: an uninitialised collector still loads at full health
      (note: after `LoadState`, `_hp <= 0` implies `_broken`, so the seed branch itself never
      fires — case 2 pins the OUTCOME, "no persisted pref ⇒ full HP, not broken", which is the
      thing that must hold, not the branch)
- [ ] Lead gates (`COMPILE_GATE_OK` + `REGRESSION_OK` on a fresh log) and commits by explicit path

## 6. ⚠ THE SECOND HALF IS AN OWNER RULING — DO NOT GUESS AT IT

The fix above removes an impossible state. **It does not change what the player sees**, and that
must be said plainly: a destroyed collector still reports DESTROYED, and its building still
stands, built and upgradable. That is because **WO-753 was never completed for baked structures**:

- WO-753 rules that a destroyed structure "returns ONLY via a full-cost build-mode placement".
- The only caller of `ResourceCollector.ResetToFullHp` — the one thing that clears `_broken` —
  is the **fresh-placement** path, `BuildModeController.cs:2200`.
- Both of these collectors are **`bake-owned`** (`[Flow:BuildMode] census ... bake-owned=[forge,
  collector_lumbermill, collector_farm, ..., arcane-tower, ...]`, 07:29:22). A baked, already-built
  id never reaches a fresh placement, so **WO-753's only recovery door is structurally unreachable
  for it.** Nothing cleared the grid cell, dropped a persisted record or removed the vendor either
  — the vendor was re-spawned 25 minutes later.
- This generalises: `arcane-tower` is bake-owned too, and `WallRepairController.cs:1283` excludes
  broken towers from Repair-All by the same rule.

**Candidate rulings (the owner picks; none is implied by this WO):**
1. **A baked structure's destruction is REVERSIBLE** — a destroyed baked collector recovers (on
   reload, or via a full-cost Repair-All line). Reverses WO-753 for baked structures only.
2. **Repair-All prices a full-cost REBUILD line for destroyed baked structures**, honouring
   WO-753's "full cost" while giving the door a location that exists.
3. **Destruction converts a baked structure into a placeable one** (drop its progression level +
   remove the twin) so WO-753's existing door becomes reachable. Closest to WO-753 as written,
   largest blast radius.

**Acceptance for whichever ruling lands (this is where the felt test lives):** the owner clears a
wave in which the Forge and Lumber Mill were untouched, and the wave-clear report lists neither —
while a genuinely destroyed, never-recovered structure is still listed.

## 7. Observed, NOT investigated (no scope creep)

- Every standalone run of this suite (red and green alike) also reports `WO-843: catalog row
  'collector_lumbermill' missing or not repo.singleton - the rebuild-card probe lost its subject`.
  ⚠ **This is a `RunStandalone` CONTEXT ARTIFACT, not a red on HEAD, and an earlier draft of this
  WO wrongly called it pre-existing.** Under the real gate the suite is GREEN: the last eight
  `DataRegression.RunAll` logs in `Builds/` all print `DESTROYED_STRUCTURE_OK`, the most recent
  being `Builds/data-regression-village-batch3.log` at 03:56 today. `-executeMethod
  DestroyedStructureRegression.RunStandalone` does not populate `CatalogRegistry` the way
  `RunAll` does, so probe D loses its catalog subject. **The lead's gate should stay green**;
  probe E reads no catalog and is unaffected by this.
- `[Flow:WaveClear] damage report: 6 damaged, 2 destroyed` fires **twice, 0.27 s apart** at
  07:28:44 (log lines 17808 / 17848) — i.e. `Collect()` appears to run twice per wave clear. Not
  investigated.
- The `6 damaged` rows at 07:28:44 had become `0 damaged` by 07:59:48, i.e. non-destroyed damage
  *does* clear itself over time. **I did not prove which system mended it** (Echo passive repair is
  a candidate, `EchoRepairService`); stated here as unproven rather than asserted.

## 8. What NOT to touch

- `WaveDamageReport`'s classification (`Destroyed`, `DamageFraction`, the pristine threshold) —
  proven correct; the new fields are diagnostics only and presentation must never read them.
- `Repair()`'s `if (_broken) return;` guard — that is WO-753 and it is pinned by probe A.
- `SiegeSession.cs:367` also consumes `Collect()` into the persisted defense ledger; any future
  change to row classification changes the raid defense report too.
