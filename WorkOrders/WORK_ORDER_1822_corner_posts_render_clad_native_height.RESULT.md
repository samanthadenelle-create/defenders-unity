# WO-1822 RESULT — CornerPost_* rendered at clad-native height

**Status:** IMPLEMENTED
**Date:** 2026-09-17
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*` + `Assets/Editor/Regression/RaidPostOrientationRegression.cs`
**Not done by this lane (by rule):** gate, commit, push. PO felt-verify + close (CLAUDE.md §13).

---

## 1. Proven before / measured after

`AuthoredHeightFor` returned 0 for a `CornerPost_*` (no `DefenseTower`, no `RaidSpire`), so the clad
rendered at `hostLossyScale x cladNativeHeight`:

| scene | wall top | BEFORE | AFTER | rise |
|---|---|---|---|---|
| raider_camp_small | 4.00 | **1.11 m** | **6.50 m** | +2.50 |
| RaidBase_IronBastion | 4.00 | **1.11 m** | **6.50 m** | +2.50 |
| fortified_garrison | 5.00 | 7.52 m | **7.50 m** | +2.50 |
| mage_enclave | 4.00 | **17.96 m** | **6.50 m** | +2.50 |

All 28 corner posts read `fitH=… cladH=… delta=+0 (0.0 % off)` on `Builds/wo1821-rebake.log`.

## 2. ⛔ THE TICKET'S PREMISE NEEDED CORRECTING, AND SO DID MY FIRST PROPOSAL

**The generator intends NO corner height, by design.** `BuildRing:1823` states it: *"Four visual corner
posts … They deliberately do NOT carry the 'Watchtower' token: authored combat towers above are budgeted
to the tier's worst-case DPS, while runtime-arming these extra posts bypassed that budget."*
`PlaceCornerTower` seats and faces them and never scales them; the only thing the ring measures off the
prefab is its XZ half-footprint. **So the rise is an INVENTION, not a recovered intent** — said plainly
in the code, because dressing an invented number as an authority is the copied-state failure wearing a
disguise.

**And my first candidate authority was WRONG.** I proposed `WallTierData.TargetHeight`. Its authored
ladder is `3.0 / 3.8 / 4.5 / 5.2` — **town** wall levels — while the raid kits' wall art measures
**4.00 m** (hexagon-green, dungeon-stone) and **5.00 m** (synty-castle). Keying corner posts to the town
ladder would have stood every one of them against a wall of a different height than the one beside it.
The dresser **already measures the kit's own wall art** (`MeasureTallest`) and already passes it into
`ReplaceChildrenWith` as `wallH` — that is the number the player sees, and it is what the rule uses.

## 3. Rule implemented

> **A corner post renders at `wallH + CornerRise`**, `CornerRise = 2.5 m`.

`RaidBaseGenerator.CornerCadenceHeight(wallH)` + `internal const float CornerRise = 2.5f`, hoisted beside
`TurretCadenceHeight`. 2.5 m is the least-invented value available: `fortified_garrison` ships 7.52 m
over a 5.00 m wall = **+2.52 m**, and it is the one kit whose corner frame was opened and read correctly.

⚠ **OWNER-CHANGEABLE AND NOT YET RULED.** One constant, one place, so the ruling is a one-line edit. The
default exists so three of four scenes stop shipping knee-high (1.11 m) and 18 m corners while she
decides — the WO-1617 pattern (ship the codebase's own default, loudly labelled), not a settled answer.

**No muzzle consequence:** corner posts carry no `DefenseTower`. Margins unchanged.

## 4. The pin has NO copied constant

`CornerRise` is `internal` to `DeNelle.EditorWallTools`, which the regression assembly cannot reference —
and the value is expected to change on a ruling, so copying it would break silently. The gate therefore
pins the **invariant the rule produces**: a corner post's top must stand **1–4 m above the scene's
tallest `WallSegment` collider**, measured independently via `TallestWallTop`. That band catches both
measured failures without naming a rise (1.11 m sits **-2.89 m** relative to a 4 m wall; 17.96 m
overshoots by **+13.96 m**) and survives any sane ruling.

**RED-proved:** with `MinCornerRiseM = 99f` the gate flagged **`RAID_POST_ORIENTATION_FAIL 28 post(s)
bad of 63`** (`Builds/wo1822-redproof.log`) — exactly the 28 corner posts, watchtowers and spires still
green, proving the branch executes. Reverted byte-identical (`sha256 0731ffcc…1e76`).

## 5. Markers (fresh logs, shared with WO-1821)

`Builds/wo1821-compile.log` **COMPILE_GATE_OK** · `Builds/wo1821-rebake.log` `baked 4 raid scene(s)` ·
`Builds/wo1821-chain.log` **OWNED_TOWN_CHAIN_OK** · `Builds/wo1821-audit-after.log`
**RAID_POST_AUDIT_OK 63** · `Builds/wo1822-redproof.log` **FAIL 28/63** then reverted ·
`Builds/wo1822-compile.log` + `Builds/wo1822-regression.log` — final gates, see hand-back.

## 6. Frames (opened)

`Builds/raid-post-audit/RaidBase_mage_enclave_CornerPost_Keep1_E.png` — a corner tower terminating its
wall at 6.5 m, overtopping it by 2.5 m (was 17.96 m, over four times its own wall).
`RaidBase_raider_camp_small_CornerPost_Outer_E.png` — was 1.11 m knee-high.

## 7. Not proven / open

1. **PO felt-verify on device** — all headless (§13).
2. **`CornerRise = 2.5 m` is unruled.** It resizes visible architecture in three of four scenes.
3. The camp/Iron Bastion corner clad is wide (`8.63 x 6.5 x 8.63`, ratio 0.75) — squat but above the
   0.65 pancake floor, and the same shipped art WO-1807 already judged as legitimately squat.
4. `RaidSpireSiegeRegression` / `RaidPolishSavedProof` still not read by this lane.
