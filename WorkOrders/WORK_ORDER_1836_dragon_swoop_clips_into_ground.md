# WO-1836 — Dragon clips into the ground during its dive-swoop

**Status: READY FOR LEAD REVIEW**

Implemented 2026-09-17 (lane). Code written + brace/NUL clean; Unity gate deliberately NOT run
(concurrent lanes open — the lead gates the combined tree). See the implementation note at the
bottom of this file.

## Owner report

Screenshot, town defense wave 21 (endless mode), Thrain Lv11: the apex dragon's tail and body are
visibly clipping through the ground/rooftop line near the player's houses, mid dive-swoop. Owner:
"see how dragon clips into the ground."

## Root cause (confirmed at source — do not re-derive)

`Assets/_Modules/Village/Enemies/DragonBoss.cs`:

- The dive-swoop's low point is computed at two call sites as a flat offset above the CURRENT
  TARGET's transform, never the ground under the dragon's own body:
  - `:820-821` — `float cruiseY = AnchorPosition().y + _orbitHeight; float lowY = tp.y + _swoopLowHeight;`
  - `:1346-1349` — `Vector3 centre = AnchorPosition(); ... float height = Mathf.Lerp(_orbitHeight, _swoopLowHeight, arc);`
  - `_swoopLowHeight` is a flat `4.5f` (`:199`) — a small, constant clearance above wherever the
    target (hero/structure) happens to stand.
- A real ground-sampling helper already exists in this same file — `SampleGroundY` (`:1210-1225`,
  a guarded `Physics.Raycast` straight down, falls back to y=0, deliberately no NavMesh dependency
  per WO-760) — but it is used ONLY by `LandSpotNear` (`:1201-1209`) for the dragon's landing spot.
  **It is never consulted during the swoop arc itself.**
- The dragon model has a large wingspan/tail length (visible in the screenshot — wings and tail
  extend many meters beyond the body's own transform position). A flat 4.5m clearance measured from
  the TARGET's feet does not account for: (a) the dragon's own body/wing/tail extent, (b) terrain or
  rooftop height directly under the dragon's body at each point along the arc (which can differ from
  the target's own ground height), or (c) uneven ground between the swoop's start and end points.

## Fix

In the swoop-dive height calculation (both call sites, `:820-821` and `:1346-1349`), replace the
flat `tp.y + _swoopLowHeight` low point with a ground-aware clearance:

1. Sample the ground height under the dragon's actual XZ position at the low point of the arc using
   the existing `SampleGroundY(xz)` helper (not the target's transform Y).
2. Set the swoop's low Y to `max(groundY + _swoopLowHeight, tp.y + _swoopLowHeight)` (or similar —
   whichever floor is higher wins) so the dragon never dips below actual ground/rooftop geometry
   regardless of what the target's transform reports.
3. Account for the model's own vertical extent (wing/tail reach) when computing clearance — either
   add a fixed body-radius buffer on top of `_swoopLowHeight`, or read it from the model's actual
   bounds if a convenient accessor exists; do not guess a number without checking the prefab's
   renderer bounds or an existing radius field in this file.
4. Add FlowTrace instrumentation (e.g. `FlowTrace.Warn`) if the computed low point still falls below
   ground after clamping, so a genuine future terrain edge case is visible in a capture instead of
   silently clipping again.

## What NOT to touch

- Do not change `_orbitHeight`, the orbit/cruise logic, or the landing-spot logic (`LandSpotNear`) —
  only the swoop-dive's low-point calculation.
- Do not touch WaveManager.cs, EnemyBrain.cs, or any of the new Enemy*.cs files from the concurrent
  WO-1835 lane (post-20 endless escalation) — this ticket is scoped to `DragonBoss.cs` only.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched `.cs`.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] Add or extend an Editor/Regression check asserting the swoop-dive's computed low Y is never
  below `SampleGroundY` at the dragon's XZ position (pure/testable without a live scene if
  `SampleGroundY`'s raycast can be substituted or the check targets the clamp math directly).
- [ ] Owner felt-verifies on a device/headless capture that the dragon no longer visibly clips
  through ground or rooftops during a dive-swoop.

## Implementation note (lane, 2026-09-17)

Files: `Assets/_Modules/Village/Enemies/DragonBoss.cs`,
`Assets/Editor/Regression/ApexDragonSpawnRegression.cs` (extended; already registered at
`DataRegression.cs:758`, so NO edit to `DataRegression.cs` — that file is dirty from another lane).

Clamp (pure + public static, `DragonBoss.ResolveSwoopLowY`):

```
lowY = min( max(groundY, targetY) + _swoopLowHeight + max(0, bodyUnderExtent), cruiseY )
```

- `groundY = max(SampleGroundYExcludingSelf(dragonXZ), SampleGroundYExcludingSelf(arcLowXZ))` —
  both sampled because `nextY` chases `wantY` at a finite descend speed, so a rooftop discovered
  only once the dragon is over it arrives too late.
- **`SampleGroundY` was NOT reused and NOT modified.** Its cast starts 50 m directly above the
  dragon's pivot with mask `~0`, and `EnsureHitCollider` guarantees a non-trigger SphereCollider on
  the rig — casting under the dragon's own XZ with it would hit the DRAGON and feed a runaway climb.
  `LandSpotNear` only escapes that because its XZ is offset by the landing standoff. The new
  `SampleGroundYExcludingSelf` uses `RaycastNonAlloc` into a cached buffer and skips any hit whose
  collider is on/under this transform, taking the highest surviving surface.
- `bodyUnderExtent` = pivot Y minus the lowest `bounds.min.y` of the rig's **mesh** renderers
  (`MeshRenderer`/`SkinnedMeshRenderer` only — particle/trail renderers are excluded; a fire-breath
  plume reaches the ground and would balloon the extent). Renderers cached once, bounds read live
  each frame because `FaceTravel` pitches the body during the dive. Fallback 2.5 m with a
  `FlowTrace.Once` if the rig has no mesh renderers (mirrors `EnsureHitCollider`'s fallback radius).
- `min(..., cruiseY)` guards an inverted arc (a floor above cruise height would make the dive climb).
- Instrumentation: `ground=`/`low=`/`extent=` appended to the existing `airgeo` throttle, a new
  `swoopgeo` throttle on the finale path, and `WarnIfBelowGround` — a `FlowTrace.Warn` throttled to
  ~1/sec per instance that fires if the MESH (pivot minus extent) is still below the sampled ground
  after clamping.

Untouched: `_orbitHeight`, orbit/cruise logic, `LandSpotNear`, `SampleGroundY`, WaveManager.cs,
EnemyBrain.cs, Enemy*.cs.

Verification done in-lane: `python tools/gate_brace.py` → `GATE_BRACE_SUMMARY bad=0 of 2`; raw brace
counts 248/248 and 22/22; no NUL bytes in either file. The regression proves the clamp MATH only —
the self-hit exclusion and the live mesh extent need the lead's headless `[Flow:DragonBoss]` capture
(`ground=`/`low=`/`extent=` on the `airgeo`/`swoopgeo` lines).
