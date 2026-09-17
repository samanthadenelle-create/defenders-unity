# WO-1807 RESULT — raid corner posts / watchtowers read as inverted

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*`
**Not done by this lane (by rule):** gate, commit, push. PO felt-verify + close (CLAUDE.md §13).

---

## The cause, in one sentence

`RaidBaseDresser.ReplaceChildrenWith` destroyed only **children**, but the prefabs it clads carry
their mesh on the **root** — so every corner post rendered **two towers at once**, and the
polyperfect one was left **hanging 2.48 m in the air** inside the Synty one.

## The proving lines

Device, owner's 19:55 raid, build 2026.09.17.372984 (`logs/device/raid-window-1955.txt`):

```
[Flow:Raid] [wo1639-marker] t+5s OVERSIZED world renderer:
  path=RaidBase_fortified_garrison/CornerPost_Outer_E kind=MeshRenderer
  screenH=1608px (1.34 of screen) camDist=9.1m mat=M_10_Brown_Dark_LPUP (URP/Lit)
[Flow:RaidArt] path='RaidBase_fortified_garrison/CornerPost_Outer_E/Visual'
  mesh='SM_Bld_Castle_Wall_Tower_M_01' bounds=4.3x7.5x4.3m
```

Headless measurement, this session (`Builds/raid-post-audit-before.log`, 20:41):

```
CornerPost_Outer_E     hostRot=(0,225,0) pos=(-49,2.5,-49) rootRenderer=YES
  ROOT   mesh='tower-medieval_wood' mat='M_10_Brown_Dark_LPUP' size=(2.99,6.5,2.99) minY=2.48 ratio=2.17
  VISUAL localRot=(0,0,0) up=(0,1,0) upDot=1  size=(4.3,7.52,4.3) minY=0    ratio=1.75
```

- `VISUAL upDot=1`, `minY=0` → the clad was never pitched. Candidate (a) dead.
- `ROOT ratio=2.17` → the host mesh was standing up, never flat. Candidate (b) dead.
- `ROOT minY=2.48` with `rootRenderer=YES` → a 6.5 m wooden tower **floating**, reaching 8.98 m out
  of a 7.52 m stone tower. Candidate (c) proven — and the float, not the stack alone, is why the
  owner's words were "upside down" and "inverted". There was never a flipped transform to find.

## Markers (all on fresh logs, judged by the word, never the exit code)

| Run | Log | Marker |
|---|---|---|
| BEFORE audit | `Builds/raid-post-audit-before.log` | `RAID_POST_AUDIT_FAIL: 49 defect(s)` / `audited 59 post(s)` |
| Rebake (4 scenes) | `Builds/raid-rebake-1807.log` | `[RaidBaseGenerator] baked 4 raid scene(s) … (raider_camp_small, fortified_garrison, mage_enclave, iron_bastion)` |
| Owned-town chain | `Builds/owned-town-chain-1807.log` | **`OWNED_TOWN_CHAIN_OK`** (169 census structures, all four id sets equal) |
| AFTER audit run 1 | `Builds/raid-post-audit-after.log` | `RAID_POST_AUDIT_FAIL: 14 defect(s)` — all my own ratio-pin false positives |
| AFTER audit run 2 | `Builds/raid-post-audit-after2.log` | **`RAID_POST_AUDIT_OK 59`** |

Scene mtimes moved to 20:44:32–20:44:40 on all four `RaidBase_*.unity`, proving the rebake landed.

## Files changed

| File | State |
|---|---|
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | **M** — the fix: `StripRootArt`, `CladLocalBox`, `ReportCladPose`, rewritten `ReplaceChildrenWith` docblock |
| `Assets/Editor/WallTools/RaidPostAudit.cs` (+ `.meta`) | **new** — headless per-renderer audit + ground-level PNGs, marker `RAID_POST_AUDIT_OK <n>` |
| `Assets/Editor/Regression/RaidPostOrientationRegression.cs` (+ `.meta`) | **new** — the gate, markers `RAID_POST_ORIENTATION_OK/_FAIL` |
| `Assets/Editor/Regression/DataRegression.cs` | **M** — one registration line after the `raid-keep-reach` suite |
| `Assets/Scenes/RaidBase_{fortified_garrison,raider_camp_small,mage_enclave,IronBastion}.unity` | **rebaked** |
| `Assets/Scenes/RaidBase_*/NavMesh.asset` (4) | **rebaked** |
| `Assets/Scenes/OwnedTown_IronBastion.unity`, `ArenaPractice_IronBastion.unity`, `Assets/Resources/OwnedTown/IronBastionTemplate.json` | **re-derived** by `OwnedTownChain` (they are derived FROM the raid scene, so they carried the same stack) |
| `WorkOrders/WORK_ORDER_1807_*.md` / `.RESULT.md` | ticket + this file |

`python tools/gate_brace.py` → `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0. NUL bytes: 0 in all four.

## The one judgement call, stated plainly

AFTER run 1 red'd 14 posts at `ratio 0.75 < 0.8`. **That was my pin being wrong, not the art.** The
frame was opened (`Builds/raid-post-audit/RaidBase_raider_camp_small_CornerPost_Outer_E.png`) and
shows a correct upright stone watchtower; `building_watchtower_green` is simply squat — the exact
false positive `RaidBaseGenerator.EnsureUpright`'s own warning text predicts. The threshold was
re-derived from three measured populations (pitched clad **0.57**, squat shipped art **0.75–0.76**,
correct clads **1.75–2.16**) and set to **0.65**. The derivation is written into the code at
`RaidPostOrientationRegression.UprightRatio`, not just here, so the next seat can audit it. The
definitive inversion test remains `upDot > 0.9`, which is exact.

## Not proven / open

1. **PO felt-verify on device is still required.** Everything above is headless. Only the owner can
   close it (§13).
2. **The Synty clad renders SOLID YELLOW in the editor frames** for the `synty-castle` kit — the
   `Synty/Generic_Basic` non-URP shader the device log already flags nine times. **Separate defect,
   separate ticket**; it made the fortified_garrison AFTER frame unreadable, so this ticket is judged
   on the measurements plus the `raider_camp_small` frame where art renders properly.
3. **`RaidPostAudit.GroundShot` frames wall-line watchtowers badly** — it stands off along the post's
   radial and the arena boundary wall gets in the way. Corner posts (inside the ring) are fine.
   Instrument limitation, recorded, not fixed.
4. **Watchtower height fit is wrong and untouched.** `PlaceTowerProp` scales the host to 4.80 m
   (`achieved=4.80m` in the bake log) but the clad that actually renders is 7.52 m — the fit measures
   one model and the dresser instantiates another. Separate ticket.
5. **`PlaceCornerTower` still never calls `EnsureUpright`** while `PlaceTowerProp` does. Harmless now
   (the host no longer renders), but it is an asymmetry a future art token could fall into. Named,
   not fixed.
6. **Why the 19:45 camp drew no complaint** is explained in the ticket §4 from sourced config values
   (`hexagon-green` clads with `building_watchtower_green`, a near-match to the host; `synty-castle`
   clads with the 7.5 m stone `SM_Bld_Castle_Wall_Tower_M_01`, a violent mismatch). The *visibility*
   argument that follows from the 9.1 m vs 78 m camera distances is **inference**, and only the owner
   can confirm it.
7. **The lane was blocked 20:16–20:40** by other lanes' non-compiling files
   (`EnemyTowerWallLosRegression.cs`, then `ArmyMusterPanel.cs`). This lane did not touch them; the
   owning lane fixed the first at 20:40. Ticket §11 keeps the record.
