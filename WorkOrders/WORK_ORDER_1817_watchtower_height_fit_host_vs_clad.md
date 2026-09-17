# WO-1817 — watchtower height fit: the HOST is fitted, the CLAD is what renders

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T13:50:54) - "owner: "close, as tested" (2026-09-17 raid-art lane: watchtower fit, spire, siege clad, corner posts)". PRIOR STATUS: FIXED - reached the owner and was felt-tested 2026-09-17; PRIOR STATUS: IMPLEMENTED (headless gates green, awaiting PO close)
**Date opened:** 2026-09-16
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*` + `Assets/Editor/Regression/RaidPostOrientationRegression.cs`
**Parent:** WO-1807 "NOT PROVEN" #4 (and #3, the `GroundShot` framing limitation)
**Owner bar (verbatim):** polish only; *"we want spells to launch and land well"* — here: a watchtower
that reads as ONE coherent tower at the right height for its wall.
**Not this lane (by rule):** gate, commit, push. PO felt-verifies + closes (CLAUDE.md §13).
**Do NOT touch:** `Assets/_Modules/Village/Buildings/DefenseTower.cs`,
`Assets/Editor/Regression/RaidWallColliderAuditRegression.cs`.

---

## 1. The defect, in one sentence

`RaidBaseDresser.ReplaceChildrenWith` hangs the clad as a CHILD of the host and never fits it, so the
clad's rendered height is the **accidental product of two unrelated models' native sizes**
(`hostLossyScale × cladNativeHeight`) — and since WO-1807 removed the host's own renderer, **nothing
controls the height of the thing the player actually sees.**

## 2. Measured, at source and in the baked scenes

Host fit, from the rebake log `Builds/raid-rebake-1807.log` (the `SPIRE FIT … turret cadence` lines):

```
rawHeight=1.002m prefabScaleBefore=0.010 target=4.80m wantedFactor=4.789 appliedFactor=4.789
  saturatedAt=none achieved=4.80m (100% of target)
```

so a fitted watchtower host carries `localScale ≈ 0.0479` and the HOST art stands 4.80 m — and the
clad child, parented with `SetParent(parent, false)`, renders at `0.0479 × cladNativeHeight`.

Clad, from the post-rebake audit `Builds/raid-post-audit-after2.log` (20:59, `RAID_POST_AUDIT_OK 59`),
`VISUAL … size=` and host `pos=`; wall tops derived from the WO-1812 suite's own summary line
(`[raid-wall-audit] scene=… minMuzzleMargin=`) as `margin + hostY + 2`:

| scene | kit | wall collider top | CornerPost clad H | Watchtower clad H | host y | minMuzzleMargin |
|---|---|---|---|---|---|---|
| RaidBase_fortified_garrison | synty-castle | **5.00 m** | 7.52 | **7.52 ×6** (catapult hosts, unfitted) / **0.36** (Archer_3, fitted) | 2.5 / 0.12 | **0.50 m** |
| RaidBase_IronBastion | hexagon-green | 4.00 m | 1.11 | **0.05 ×10** | 0 | 2.00 m |
| RaidBase_mage_enclave | dungeon-stone | 4.00 m | **17.96** | **0.86 ×10** | 0 | 2.00 m |
| RaidBase_raider_camp_small | hexagon-green | 4.00 m | 1.11 | **1.11 ×4** (catapult hosts) | 0 | 2.00 m |

The arithmetic closes exactly, which is the proof it is this mechanism and not another:
`0.0479 × 1.11 = 0.053` (IronBastion), `0.0479 × 17.96 = 0.86` (mage_enclave),
`0.0479 × 7.52 = 0.36` (garrison Archer_3).

**⚠ THE TICKET'S PREMISE IS THE GARRISON-ONLY VIEW.** "4.80 host vs 7.52 clad" is one of four
behaviours. The real finding: **not one watchtower in any of the four scenes renders at the 4.80 m the
generator fitted, and three of four scenes render SUB-METRE towers** (0.05 m in Iron Bastion — the
final raid). WO-1807's audit passed them because it pins upright-ness and seating, never absolute height.

**Why the split is 13 fitted / 10 unfitted, proven not inferred:** the rebake log's type summaries read
`turrets for 'fortified_garrison': … types [6xtower_catapult, 1xtower_arcane_spire]` and
`'raider_camp_small': … [4xtower_catapult]` — 10 catapults, and `RaidBaseGenerator.IsAuthoredSiegeMachine`
exempts `tower_catapult` from BOTH `EnsureUpright` and `ScaleToHeight`. Those are exactly the 10 posts
the before-audit reports as `rootRenderer=no` (their art was on children, which the dresser destroyed).

**The muzzle is `transform.position + Vector3.up * 2f`** (`DefenseTower.BlockedByWallAt`, `:977`, matched
by `Fire`/`FireAtParty`). It is therefore decided by the HOST TRANSFORM, not by the art — *except* that
`ReplaceChildrenWith` calls `SeatOnGround(host)`, which MOVES THE HOST to seat the clad. That is how the
garrison's host reached y=2.5 (the Synty clad's pivot sits 2.5 m above its base) and why its muzzle is at
4.5 m against a 5.00 m wall: **margin 0.50 m, the tightest in the game, set by an art pivot.**

## 3. The rule (ONE rule)

> **The clad's rendered height IS the host's authored turret cadence height, and the clad — not the host
> — is what moves to seat it.**

1. Hoist PlaceTowerProp's existing expression into `RaidBaseGenerator.TurretCadenceHeight(catalogId)`
   (`internal static`, same pattern as `IsAuthoredSiegeMachine` / `CatalogArtPath`) and call it from
   BOTH `PlaceTowerProp` and the dresser. No second `1.2`, no second `YHeightVariable`.
2. In `ReplaceChildrenWith`, for a `Watchtower_*`, recover the catalog id from the host's own
   `DefenseTower.CatalogId` (stamped by `ArmTower`, `:1697`) and scale the clad's `localScale`
   uniformly so its rendered Y equals that target. **Computed directly, NOT through `ScaleToHeight`** —
   its factor clamp is 0.125–8× and Iron Bastion needs ≈ ×90.
3. Seat the CLAD in the host's local space instead of calling `SeatOnGround(host)`, so the host
   transform keeps the position the generator gave it. Presentation stops moving the object
   (ARCHITECTURE_PRINCIPLES), and the muzzle becomes a pure function of the generator.

**Trade-off, stated:** tower art proportion vs the wall/muzzle relationship. The clad is uniformly
rescaled away from its native size — proportion preserved, absolute size not. The alternative (fit the
host to the clad's measured height) makes tower height a property of whichever art pack a kit names —
**measured range 0.05 m to 17.96 m** — while the muzzle is nailed at 2 m, so a 1.11 m tower fires from
above its own roof and an 18 m one dwarfs a 4 m wall. The authored cadence wins.

**Consequences to report for ruling, not to pre-empt:**
- Garrison towers land at **3.00 m** (6× catapult, heightMul 0.75) and **4.80 m** (1× arcane spire,
  heightMul 1.2) — both BELOW their own 5.00 m wall. If the owner wants a watchtower to overtop its
  wall, the one-line alternative is to tie the cadence to wall top + a margin. **Not implemented here.**
- The garrison muzzle drops 4.5 → its generator y + 2, so `minMuzzleMargin` rises from 0.50 m. That is
  a felt change to the tightest turret in the game; report the measured delta.

## 4. Scope — named, NOT fixed (each needs its own ticket / ruling)

1. **The dresser clads catapult-hosted `Watchtower_*` with the kit's tower model**, destroying the
   authored siege art on 10 posts. Same class as WO-1617. Owner ruling, not a lane's call.
2. **`CornerPost_*` has no authored cadence** and renders at clad-native 1.11 / 7.52 / **17.96** m.
   Out of scope (no `DefenseTower`, no target); the 18 m mage-enclave corner posts deserve their own look.
3. `TurretCadenceHeight` defaults heightMul to **1.2** while `StructureFactory.OptsFor` defaults **1.0**
   — already named at `StructureCadenceRegression.cs:114-121`. Every live turret row authors the key, so
   they agree today; the new pin REDS if a future row does not, which forces the convergence ticket.
4. `PlaceCornerTower` still never calls `EnsureUpright` (WO-1807 #5). Untouched.

## 5. Acceptance

- [ ] `RaidBaseGenerator.TurretCadenceHeight` is the ONE expression; `PlaceTowerProp` calls it.
- [ ] `ReportCladPose` prints `fitH` / `cladH` / `wallH` / `hostY` + wanted-vs-applied factor.
- [ ] `RaidPostAudit.Evaluate` reds when a `Watchtower_*` clad height disagrees with its cadence by >10%.
- [ ] `RaidPostOrientationRegression` gains the same pin via an INDEPENDENT path (catalog +
      `StructureFactory.OptsFor`), never by importing the audit's helper.
- [ ] `RaidPostAudit.GroundShot` picks its standoff side by linecast so a wall-line watchtower is not
      photographed through the boundary wall (WO-1807 #3).
- [ ] Rebake through the sanctioned chain ONLY: `RaidBaseGenerator.BuildAllRaidScenes` then
      `OwnedTownChain.RebuildFromRaid` (`RaidBaseGenerator.cs:610` forbids the generator alone).
- [ ] Fresh-log markers: `RAID_POST_AUDIT_OK`, `OWNED_TOWN_CHAIN_OK`, `RAID_POST_ORIENTATION_OK`, and
      the WO-1812 suite's `minMuzzleMargin` > 0 on all four scenes — judged by the marker, never exit code.
- [ ] Before/after `GroundShot` frames of a garrison watchtower and a camp watchtower, OPENED.
- [ ] `python tools/gate_brace.py` clean + zero NUL bytes on every `.cs` touched.
