# WORK ORDER 1720 — RESULT

**Verdict: same underlying defect WO-1719's parallel lane is already fixing — the WallSegment
BoxCollider/renderer height mismatch. No duplicate fix made here. Instrumentation added only.**

## What was read (source-cited, this session)

All three defense structures gate fire on an identical `BlockedByWall` pattern:

- `Assets/_Modules/Village/Buildings/TowerCombat.cs:194-208` (now `:194-217` after instrumentation)
- `Assets/_Modules/Village/Buildings/DefenseTower.cs:912-921` (now `:912-931`)
- `Assets/_Modules/Village/Buildings/ArcaneTower.cs:459-468` (now `:459-478`)

Each does exactly: `Physics.Linecast(firePoint, target.WorldPosition, structureMask, ...)` where
`structureMask = LayerMask.GetMask("Structure")`. This **is** a raycast/linecast LOS check, it
**does** run, and it is masked to the exact layer walls sit on.

`Assets/_Modules/Village/Walls/WallSegment.cs:585-586` confirms the wall side of the contract:
`gameObject.layer = structureLayer` ("Structure") specifically so these three towers' linecasts hit
it — the file's own header comment (`:35-39`) names all three call sites by name as the reason the
wall must never leave that layer. `WallSegment.cs:210-218` (`ApplyTierBlockerHeight`) is the method
that sizes the `BoxCollider` (`_blocker`) from `Walls.WallDefense.TargetHeight(...)` — this is the
exact collider WO-1719 measured at 3m tall against a 15m renderer on an intact `Wall_Outer_SS_3`.

**Conclusion:** the tower LOS gate is not missing, not a different mechanism, and not merely
"correlated" with WO-1719's evidence — it is a linecast against the SAME undersized `BoxCollider`
WO-1719 is already resizing. A tower firing from an elevated muzzle (`transform.position + up*2` /
`+up*2.5` / the model's `FirePoint`) at a low ground target draws a line whose Y crosses the wall's
XZ footprint well above the collider's real 3m top while still visually crossing the 15m wall the
player sees — the exact mechanism the WO's lead described. Once WO-1719 corrects the collider height
to match the renderer, this ticket's symptom is expected to resolve with no code change in these
three files.

## Why no fix was made here

`WallSegment.cs` and `RaidBaseGenerator.cs` are explicitly out of scope for this ticket (owned by the
in-flight WO-1719 lane). The fix — making the collider height match the renderer height — belongs
entirely in those two files. Editing `TowerCombat.cs` / `DefenseTower.cs` / `ArcaneTower.cs` to
"fix" this would mean duplicating or working around the same defect in a second place (e.g. widening
the linecast's Y tolerance), which is exactly the two-places-fixing-one-bug outcome CLAUDE.md and this
WO's own instructions forbid.

## What was changed

FlowTrace instrumentation only, at the LOS decision point in all three `BlockedByWall` methods
(`[Flow:TowerLoS]`, tag `"TowerLoS"`, throttled ~1/sec per tower instance — matches the existing
hot-loop throttling convention in these files). Each call now uses the `RaycastHit`-returning
`Physics.Linecast` overload (same boolean return, same gameplay behavior — this is observability
only) and logs fire point, target point, the blocked bool, and — when a hit occurs — the hit
collider's name and **actual Y bounds**, plus a note when nothing was hit at all. This makes the
exact symptom (`blocked=false` with fPos/tPos Y straddling a wall the player can see) provable from
one fresh log read the next time it's reported, without re-deriving the mechanism from scratch, and
it will also directly show the fix landing once WO-1719's collider-height correction ships (the
`hitColliderBoundsY` in a blocked case should read ~[0.00..15.00] instead of ~[0.00..3.00]).

Nothing was stripped; only additive instrumentation.

## Files touched

- `Assets/_Modules/Village/Buildings/TowerCombat.cs`
- `Assets/_Modules/Village/Buildings/DefenseTower.cs`
- `Assets/_Modules/Village/Buildings/ArcaneTower.cs`

`Assets/_Modules/Village/Walls/WallSegment.cs` and `Assets/Editor/WallTools/RaidBaseGenerator.cs`
were read for context only — **not edited**, per this WO's explicit scope boundary.

## Gate

`python tools/gate_brace.py Assets/_Modules/Village/Buildings/TowerCombat.cs
Assets/_Modules/Village/Buildings/DefenseTower.cs Assets/_Modules/Village/Buildings/ArcaneTower.cs`
→ `GATE_BRACE_SUMMARY bad=0 of 3`

No full Unity gate run (per WO instruction — lead batches with other in-flight lanes). Not committed
(per WO instruction — left for the lead).

## Recommendation to the lead

Re-verify this ticket once WO-1719's `WallSegment.cs` collider-height fix lands and is gated: pull a
fresh `[Flow:TowerLoS]` capture on an intact wall and confirm `hitColliderBoundsY` now matches the
renderer height and that towers stop firing through standing walls. If confirmed, this ticket can be
closed as resolved-by-WO-1719 with no further tower-side change.
