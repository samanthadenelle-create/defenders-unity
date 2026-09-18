# WO-1865 RESULT — wave-clear report named standing buildings destroyed

**Date:** 2026-09-18
**Outcome:** Root cause PROVEN from captured device data; invariant defect FIXED with a headless
red/green proof. The remaining player-felt half is an **owner ruling** and was deliberately not
guessed at (WO §6).
**Seat:** SME agent (no gate, no commit — handed to the lead).

---

## Headline

The report was **not** the defect. `WaveDamageReport.Collect()` reads `IsBroken`/`HpFraction` LIVE
and read them correctly. The defect is that `ResourceCollector` could hold **full HP and the
destroyed flag at the same time**, a state nothing could clear.

The opening hypothesis (stale pre-repair snapshot / sticky per-wave flag / the "repair scan" not
clearing something) is **disproven at source** — there is no snapshot and no per-wave flag, and the
"repair scan" is `SurfaceWorstRepair()`, a UI nudge that repairs nothing.

## Proving lines (owner's Seeker, `Logs/device/raid-trace-20260918-080200.txt`)

```
07:27:13.960  [Flow:Harvest]     collector-destroyed building=forge
07:28:26.003  [Flow:Harvest]     collector-destroyed building=lumbermill
07:28:26.114  [Flow:RepairProbe] BURNING 'lumbermill' hp=0.00 broken=True     <- honest
07:28:44.078  [Flow:WaveClear]   damage report: 6 damaged, 2 destroyed        <- correct here
07:28:51.446  [Flow:Manage]      building choice id=forge level=4/4 state=Max <- town says BUILT
07:42:03.019  [Flow:BuildTimer]  upgrade 'lumbermill' applied ... = 4         <- upgraded a "dead" building
07:51:47.855  [Flow:RepairProbe] BURNING 'forge' hp=1.00 broken=True          <- ** THE DEFECT **
07:59:48.637  [Flow:Repair]      hub repair affordance: HIDDEN because RepairAllCost() priced NOTHING
07:59:48.865  [Flow:WaveClear]   damage report: 0 damaged, 2 destroyed        <- same 2, 31 min later
```

## Mechanism

`ResourceCollector.Awake` (`:196`) and `Configure` (`:370`) both ran:

```csharp
LoadState();                    // _broken = _hp <= 0f   -> TRUE for a persisted-destroyed collector
if (_hp <= 0f) _hp = _maxHp;    // ...then erased the only evidence for it
```

Both lines were born in the **same commit `b08293c93` (2026-07-09)** — dated with
`git log -L196,197:...` and `-L993,993:...`. Since `LoadState` defaults an *absent* pref to
`_maxHp`, `_hp <= 0` after a load can only mean "persisted-destroyed", so the guard never protected
an uninitialised collector; it only ever half-revived a dead one. WO-753 (2026-07-19) then added
`if (_broken) return;` to `Repair()`, which made the half-revived state **permanent and
unfixable**.

## Changes (3 files, code only, no scene files)

- `Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs`
  — the duplicated post-load seed collapses into one `SeedHpAfterLoad(via)`; it seeds HP only when
  the collector is **not** flagged destroyed, and emits a permanent `FlowTrace.Warn` naming
  id / hp / pref / progression level / the unreachable rebuild door when one loads destroyed.
- `Assets/_Modules/Village/Waves/WaveDamageReport.cs`
  — diagnostics-only `Source` / `LiveHpFraction` / `LiveBroken` on `Entry`, and **one
  `FlowTrace.Step` per row** in `Collect()` with an explicit `** CONTRADICTION **` clause for a
  DESTROYED row at full health. Classification logic untouched.
- `Assets/Editor/Regression/DestroyedStructureRegression.cs`
  — probe **E** (3 cases) + header and verdict text.

## Verification

**Brace gate** — real output:

```
$ python tools/gate_brace.py Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs \
    Assets/_Modules/Village/Waves/WaveDamageReport.cs \
    Assets/Editor/Regression/DestroyedStructureRegression.cs
GATE_BRACE_SUMMARY bad=0 of 3
EXIT=0
```

**RED** (`Builds/wo1861-red.log`, the guard temporarily restored to its old one-line form — marker
`DESTROYED_STRUCTURE_FAIL`, and it reproduces the device state exactly):

```
E. reload does not half-revive a destroyed collector (WO-1861)
  case1 persisted-destroyed reload: IsBroken=True hpFrac=1.000
 - WO-1861 (1): THE CAPTURED BUG - a destroyed collector reloaded at hpFrac=1.000 while
   IsBroken stayed true. FULL HP + broken is an impossible state...
```

*(Quoted verbatim. That log says **WO-1861** because it was captured before the number was minted;
`CLI_LANES_WO_NUMBERS.md` showed 1861 already taken by the pseudolocalization harness, so the
probe's strings were corrected to **WO-1865** and the green run was re-captured under the new
number. Same probe, same assertion.)*

**GREEN** (`Builds/wo1865-green-final.log`, fix in place, `errorCS=0`, run 08:23:13 — deliberately
re-run AFTER the final byte-level CRLF normalisation of all three files, so the proof postdates the
bytes it claims to prove; the identical result was first captured in `Builds/wo1865-green.log`):

```
  case1 persisted-destroyed reload: IsBroken=True hpFrac=0.000      <- was 1.000
  case2 no persisted pref:          IsBroken=False hpFrac=1.000     <- seed still works
  case3 WaveDamageReport predicate: destroyed-row frac=1.000 pristine-row frac=0.000
```

The temporary red-proof edit was reverted and verified gone
(`grep -c WO1865_RED_TEMP ResourceCollector.cs` = 0) before the green run.

The one remaining failure in every standalone run — `WO-843: catalog row 'collector_lumbermill'
missing or not repo.singleton` — is a **`RunStandalone` context artifact, NOT a red on HEAD.**
Proven, not assumed: the last eight `DataRegression.RunAll` logs in `Builds/` all print
`DESTROYED_STRUCTURE_OK` (most recent `Builds/data-regression-village-batch3.log`, 03:56 today), so
`RunAll` populates `CatalogRegistry` and `RunStandalone` does not. **The lead's gate should stay
green** — probe E reads no catalog. Not touched.

## ⚠ What this does NOT fix

The player-visible report is **unchanged**: a destroyed collector still reads DESTROYED, and its
building still stands, built and upgradable. Closing that needs an owner ruling, because both
structures are `bake-owned` and `ResetToFullHp`'s only caller is BuildModeController's
**fresh-placement** path — so WO-753's "rebuild at full cost" door cannot be reached for them at
all. Three candidate rulings are laid out in WO §6; none is implied by this change.

## Left for the lead

- Gate (`COMPILE_GATE_OK` + `REGRESSION_OK` on a fresh log) and commit by explicit path.
- `python tools/board_build.py` and commit the Status flip with the work.
- Route WO §6 to the owner for a ruling.
