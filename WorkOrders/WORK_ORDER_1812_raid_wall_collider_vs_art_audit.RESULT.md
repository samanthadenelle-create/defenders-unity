# RESULT — WO-1812 raid wall collider vs. art audit

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Lane:** Editor regression, file-disjoint from WO-1807 (`Assets/Editor/WallTools/*` untouched)

## Delivered

| File | Lines | What |
|---|---|---|
| `Assets/Editor/Regression/RaidWallColliderAuditRegression.cs` | 388 (new) | `[raid-wall-audit]`, markers `RAID_WALL_AUDIT_OK` / `_FAIL` |
| `Assets/Editor/Regression/DataRegression.cs` | +9 at `:784-792` | one `Guard.Try` registration + its comment block |
| `WorkOrders/WORK_ORDER_1812_raid_wall_collider_vs_art_audit.md` | ticket | the WHY, the YAML pre-read table, the un-ruled invariant question |

## Proven

* Brace gate: `python tools/gate_brace.py` on both `.cs` → `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0.
* NUL scan: both files clean (`b'\x00' not in open(p,'rb').read()`).
* Registered exactly once; `grep -n raid-wall-audit DataRegression.cs` → the comment at `:784`/`:789`
  and the single call at `:792`.
* The WHY re-read at source, not from any doc: `WallSegment.cs:511-512` (raid walls never `Configure()`),
  `:626-629` (`Awake` sets no layer), `:632-648` (`RebuildCollider` is the sole author of both the box
  size and the `Structure` layer), `DefenseTower.cs:965-970` (`Structure`-masked linecast from
  `position + up*2`), `DefenseTower.cs:50-56` (`EnemyOwned = 1`), `TagManager.asset:11-20`
  (`Structure` = layer 8).
* YAML pre-read of all four baked scenes with full TRS composition, numbers in the ticket §2. Walls:
  158/118/158/58, **every one on layer 8**, every blocker enabled and non-trigger, collider world span
  0→4.00 m in three scenes and 0→5.00 m in `fortified_garrison`. Enemy turret muzzle margins:
  **+1.99 / +0.50 / +1.99 / +2.00 m**. Six garrison towers at pivot y 2.50 (five with a wall in 12 m,
  all five at +0.50), one at 0.12 (+2.88).

## Two findings the lead should carry forward

1. **The WO-1808 hand-back's wording is inverted.** The muzzle at 4.50 m sits 0.50 m *below* the collider
   top at 5.00 m — it does not "clear the wall top by 0.50 m". That is why the WO-1808 fix works. A seat
   acting on the original sentence would raise the wall and break a correct arrangement.
2. **The 2.50 m tower pivot is art-dependent, and that is the real fragility.**
   `RaidBaseGenerator.PlaceTowerProp` → `SeatOnGround` → `SeatOnSurface(go, 0f, …)`
   (`RaidBaseGenerator.cs:2121-2143`) lifts each tower until its lowest *rendered* point is y = 0, so a
   baked `position.y` of 2.50 is **consistent with** that FBX's pivot sitting 2.50 m above its own mesh
   floor — the other three scenes seated at y ≈ 0.01, and neither `RaiseKeep` (1.5 m) nor any mound
   object explains it. ⚠ **The mechanism is a static read, NOT measured** (a later height-fit or the
   `/Visual` clad could produce the same number); the ticket names two cheap proofs. The conclusion
   survives all of them: `position + up*2` is measured from an art-owned pivot, so swapping one
   watchtower model would silently put every garrison muzzle over its own wall. The ticket proposes
   (does not implement) making the muzzle art-derived behind ONE `MuzzleOffset` seam — the `up * 2f`
   literal is duplicated at five sites in `DefenseTower.cs` and nothing keeps them equal.

## Open / not claimed

* **No `COMPILE_GATE_OK`, no regression run, no commit** — this lane may not run Unity or touch git.
  The new `.cs` has **no `.meta`**; Unity mints it on first import and it must land in the same commit.
* Pins 3+4 (collider vs. art bounds) are **undecidable from YAML** — FBX mesh extents are not in the
  scene file. Expected first run is `shortCollider=0` (the generator already lands two different
  scale/size pairs on exactly 5.00 m, which implies it derives the box from the art), but that is an
  inference, not a measurement, and only the real run settles it.
* The margin threshold stays un-asserted beyond `> 0` until the owner rules on ticket §2. Under a
  `margin ≥ 1.0 m` rule, `fortified_garrison` alone would fail today.
