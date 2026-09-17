# WO-1817 RESULT — watchtower height fit: host vs clad

**Status:** IMPLEMENTED
**Date:** 2026-09-17
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*` + `Assets/Editor/Regression/RaidPostOrientationRegression.cs`
**Not done by this lane (by rule):** gate, commit, push. PO felt-verify + close (CLAUDE.md §13).

---

## The cause, in one sentence

The dresser hangs the clad as a CHILD of the host and never fits it, so the clad renders at
`hostLossyScale × cladNativeHeight` — and WO-1807 removed the host's own renderer, so **nothing at all
controlled the height of the thing the player sees.**

## Measured BEFORE (the ticket's premise was the garrison-only view)

Host fit `Builds/raid-rebake-1807.log`: `prefabScaleBefore=0.010 … appliedFactor=4.789 achieved=4.80m`
→ host localScale ≈ 0.0479. Clad `Builds/raid-post-audit-after2.log` (WO-1807's green run):

| scene | wall top | Watchtower clad | = 0.0479 × native | muzzle margin |
|---|---|---|---|---|
| fortified_garrison | 5.00 | **7.52 ×6** (catapult hosts, unfitted) / **0.36** | 0.0479 × 7.52 = 0.36 ✓ | **0.50** |
| IronBastion | 4.00 | **0.05 ×10** | 0.0479 × 1.11 = 0.053 ✓ | 2.00 |
| mage_enclave | 4.00 | **0.86 ×10** | 0.0479 × 17.96 = 0.86 ✓ | 2.00 |
| raider_camp_small | 4.00 | **1.11 ×4** (catapult hosts) | native, host scale 1 ✓ | 2.00 |

The arithmetic closes to the centimetre — that is the proof of mechanism. **Not one watchtower in any
scene rendered at the fitted 4.80 m, and three of four scenes shipped SUB-METRE towers** (0.05 m in Iron
Bastion, the final raid). WO-1807's audit passed them because it pinned upright-ness, never height.

The 13/10 split is proven, not inferred: `Builds/wo1817-rebake.log` type summaries read
`[6xtower_catapult, 1xtower_arcane_spire]` (garrison) and `[4xtower_catapult]` (camp), and
`IsAuthoredSiegeMachine` exempts `tower_catapult` from `ScaleToHeight`.

## The rule chosen, and why

> **The clad's rendered height IS the host's authored turret cadence, and the CLAD — not the host —
> moves to seat it.**

Target comes from `RaidBaseGenerator.TurretCadenceHeight(catalogId)` — PlaceTowerProp's own expression
hoisted, not copied — keyed by the id `ArmTower` already stamps on the host's `DefenseTower`.

**Trade-off:** tower art proportion vs the wall/muzzle relationship. The clad is uniformly rescaled away
from native (proportion preserved, absolute size not). The alternative — fit the host to the clad —
makes tower height a property of whichever art pack a kit names (**measured 0.05 m to 17.96 m**) while
the muzzle is nailed at `position + up*2`, so a 1.11 m tower fires from above its own roof.

**Seating the clad instead of the host is the second half, and it is the muzzle.** `SeatOnGround(host)`
let an art pivot set a combat value: the Synty clad's pivot sits 2.5 m up, which lifted the garrison
hosts to y=2.5 and left `minMuzzleMargin=0.50`, the tightest in the game, decided by nobody.

## Markers — all on FRESH logs, judged by the word

| Run | Log | Marker |
|---|---|---|
| Compile | `Builds/wo1817-compile.log` | **`COMPILE_GATE_OK :: scripts compiled clean`** |
| BEFORE audit (new pin on old scenes) | `Builds/wo1817-audit-before.log` | `RAID_POST_AUDIT_FAIL: 31 defect(s)` — every one a height defect |
| Rebake | `Builds/wo1817-rebake.log` | `[RaidBaseGenerator] baked 4 raid scene(s)`; all 31 watchtowers `delta=+0 (0.0 % off)` |
| Owned-town chain | `Builds/wo1817-chain.log` | **`OWNED_TOWN_CHAIN_OK`** |
| AFTER audit | `Builds/wo1817-audit-after.log` | **`RAID_POST_AUDIT_OK 59`** |
| Gate RED proof | `Builds/wo1817-orientation-redproof.log` | `RAID_POST_ORIENTATION_FAIL 31 post(s) bad of 59` |
| Gate GREEN | `Builds/wo1817-orientation.log` | **`RAID_POST_ORIENTATION_OK … 59`** |
| WO-1812 muzzle | `Builds/wo1817-wallaudit.log` | **`RAID_WALL_AUDIT_OK`**, margins below |

Scene mtimes 00:06:56–00:07:11 on all four `RaidBase_*`, `OwnedTown_IronBastion`,
`ArenaPractice_IronBastion` and `IronBastionTemplate.json` — the rebake landed.

**Muzzle margins, before → after:** garrison **0.50 → 2.99**; IronBastion 2.00 → 1.99;
mage_enclave 2.00 → 1.99; camp 2.00 → 2.00. All > 0. The 0.01 m dips are the host keeping the
generator's own seat instead of being re-zeroed by the dresser.

**The RED proof matters and is why it was run.** A height pin that never executes is green theatre. With
`CladHeightTolerance` temporarily at `-1f` the gate flagged **exactly the 31 watchtowers**, each with a
real target (`'tower_arcane_spire' authors a fit height of 4.8m`), proving the regression's INDEPENDENT
path (catalog + `StructureFactory.OptsFor`) reaches the comparison — and that it agrees with
`TurretCadenceHeight` to the decimal. The file was reverted and re-hashed **byte-identical**
(`sha256 24693d16…ee98e9`) to the one that produced the green.

## Frames (opened, all four)

BEFORE `Builds/wo1817-before/` (WO-1807 framing) and `Builds/wo1817-before-newframe/` (new framing on
old geometry). AFTER `Builds/raid-post-audit/`.

- `RaidBase_fortified_garrison_Watchtower_Archer_0.png` — before: a wall filling the frame with a roof
  peeking over. After: one coherent stone tower, seated, correct material — **but read it honestly: at
  the catapult cadence of 3.00 m the Synty tower is a slender column standing well below the parapet of
  its own 5.00 m wall.** Correct by the rule, and the visual evidence for open item #3 below.
- `RaidBase_raider_camp_small_Watchtower_Archer_0.png` — before: hidden behind a rock; the new framing
  revealed a knee-high nub. After: a 3.00 m wooden watchtower with fighting platform and stakes.
- `RaidBase_IronBastion_Watchtower_Archer_0.png` — 0.05 m speck → 4.80 m archer tower clearing the wall.
- `RaidBase_mage_enclave_Watchtower_Archer_0.png` — 0.86 m → 4.80 m crenellated tower over the wall.

**WO-1807 "NOT PROVEN" #3 is closed.** `GroundShot` now builds both candidate stations, Linecasts each
to the subject, takes the first clear one, derives standoff from the subject's own size, and writes
`side=inward dist=10.6m` or `OBSTRUCTED (outward blocked by 'Wall_Outer_SW_12'; …)` into the log. The
before-run proves the old instrument was reporting NON-BLANK on frames that showed nothing.

## Files changed

| File | State |
|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | **M** — `TurretCadenceHeight` hoisted out of `PlaceTowerProp` |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | **M** — `FitCladToCadence`, `SeatCladLocally`, `CladHeight`, wallH plumbing, `ReportCladPose` extended |
| `Assets/Editor/WallTools/RaidPostAudit.cs` | **M** — pin 5 (height), `CladHeightTolerance`, cadence/muzzle in the log line, `GroundShot` side-by-linecast |
| `Assets/Editor/Regression/RaidPostOrientationRegression.cs` | **M** — pin 5 via catalog + `StructureFactory.OptsFor` |
| `Assets/Scenes/RaidBase_{raider_camp_small,fortified_garrison,mage_enclave,IronBastion}.unity` | **rebaked** (00:06:59–00:07:07) |
| `Assets/Scenes/RaidBase_*/NavMesh.asset` (all four, VERIFIED by mtime 00:06:58–00:07:07) | **rebaked** |
| `Assets/Generated/RaidGround/*.mat` | **regenerated** (00:07:00–00:07:58) |
| `Assets/Scenes/OwnedTown_IronBastion.unity`, `ArenaPractice_IronBastion.unity`, `Assets/Resources/OwnedTown/IronBastionTemplate.json` | **re-derived by the chain** |

`python tools/gate_brace.py` → `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0. NUL bytes: 0 in all four.
`DefenseTower.cs` and `RaidWallColliderAuditRegression.cs` untouched, as instructed.

## Not proven / open — read these before closing

1. ⛔ **THE RAID SPIRE RENDERS AT 0.14 m IN THREE OF FOUR SCENES — the raid's WIN CONDITION.**
   `[wo1807] pose 'RaidSpire': … size=0.07x0.14x0.06` in garrison, mage_enclave and IronBastion; only
   the camp is correct at 14.4 m. **PRE-EXISTING, not caused here** — the identical four lines are in
   `Builds/raid-rebake-1807.log`. Same mechanism as this ticket (clad inherits host scale) but the
   spire carries no `DefenseTower`, so it has no authored cadence to fit to and the fix does not reach
   it. **This deserves its own ticket ahead of most things on the board.**
2. **PO felt-verify on device is still required.** Everything above is headless (§13).
   **This lane did NOT run `DataRegression.RunAll`** — only `RaidPostOrientationRegression` and
   `RaidWallColliderAuditRegression` standalone. Other suites that open these rebaked scenes and must be
   watched in the lead's combined gate: `EnemyTowerWallLosRegression` (the garrison muzzle moved
   4.5 → 2.0 m, which is that suite's exact subject), `RaidKeepReachRegression`,
   `RaidWallContinuityRegression`, and `RaidPolishSavedProof` (in this silo, 529 lines, NOT read by this
   lane — it may pin baked state).
3. **Garrison towers now stand BELOW their own 5.00 m wall** (3.00 m on the six catapult slots, 4.80 m
   on the arcane one). That is the authored cadence, faithfully applied. If the owner wants a watchtower
   to overtop its wall, the one-line alternative is to tie the cadence to wall top + a margin — **not
   implemented, needs her ruling.** Mixed 3.00/4.80 on the same wall line is the visible symptom.
4. **The dresser clads catapult-hosted `Watchtower_*` with the kit's tower model**, destroying authored
   siege art on 10 posts. Same class as WO-1617. Owner ruling, not a lane's call.
5. **`CornerPost_*` still renders at clad-native size** — 1.11 / 7.52 / **17.96** m across the kits. No
   `DefenseTower`, no authored target. The 18 m mage-enclave corner posts need their own look.
6. **`TurretCadenceHeight` defaults heightMul to 1.2, `OptsFor` to 1.0** (already named at
   `StructureCadenceRegression.cs:114-121`). Every live turret row authors the key so they agree today,
   and the red proof showed them agreeing to the decimal. A future row omitting it REDS pin 5 — correct,
   and the forcing function for the convergence ticket. Do not widen the tolerance to silence it.
7. **I did not read the baked `.unity` YAML for the clad scale.** The audit re-OPENS each baked scene
   and measures the rendered bounds there, which is a strictly stronger reading of the same artifact —
   but the literal "measure the YAML" instruction was met this way, not by parsing text.
8. **`PlaceCornerTower` still never calls `EnsureUpright`** (WO-1807 #5). Untouched.
