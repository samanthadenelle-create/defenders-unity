# WORK ORDER 1720 — Defense towers have line-of-sight through standing walls

**Status: DONE — SAME ROOT CAUSE AS WO-1719, INSTRUMENTED, NOT DUPLICATE-FIXED**
**Filed:** 2026-09-14, owner live playtest report: "also towers can shoot be through walls should not happen."
**Silo:** Combat/AI (code only, no scene files) — file-disjoint from the in-flight WO-1719 follow-up
fixing the wall collider/renderer height mismatch (`WallSegment.cs`, `RaidBaseGenerator.cs`). Do NOT
touch those two files in this ticket — if the investigation proves the cause lives there, STOP, report
the overlap, and hand back rather than editing a file another lane owns mid-flight.

## Context — a strong, PROVEN lead from the same session, not a guess

In the same live capture that diagnosed WO-1719's breach-tap-miss bug, `Wall_Outer_SS_3` (a fully
intact, undamaged wall) showed:
```
colliderBounds=Center: (-37.77, 1.50, -49.00), Extents: (1.45, 1.50, 0.75)   <- 3m tall
rendererBounds=Center: (-37.77, 7.50, -49.00), Extents: (1.45, 7.50, 0.75)   <- 15m tall
```
The wall LOOKS 15m tall but its physics collider is only 3m tall. If any defense tower's
line-of-sight check is a physics raycast/linecast against wall colliders (rather than, say, a NavMesh
or bounds check), a raycast from an elevated tower position to a low target could easily pass ABOVE the
real 3m collider while still visually appearing to cross "through" the 15m wall — i.e. this may be the
EXACT SAME underlying defect, just observed via tower targeting instead of via the tap handler.

**Do not assume this is the cause.** It is a lead worth checking first, cheaply, before any other
theory — but it must be confirmed by reading the tower's actual LOS code, not assumed from the
coincidence.

## Instrument-first (CLAUDE.md §12 — binding)

1. Find the defense tower's line-of-sight / target-visibility check. Start with
   `Assets/_Modules/Village/Buildings/DefenseTower.cs` and `Assets/_Modules/Village/Buildings/ArcaneTower.cs`
   — search for `Physics.Linecast`, `Physics.Raycast`, `LineOfSight`, or any occlusion check gating
   whether a tower may fire at a target.
2. Read exactly what mask/layer that check uses, and whether it targets `WallSegment` colliders at all
   (some LOS checks might use a completely different mechanism — a NavMesh sample, a bounds test, or
   nothing at all, i.e. towers currently have NO wall-occlusion check, which is a different bug with a
   different fix).
3. If a raycast/linecast IS used: correlate its height/origin logic against the WO-1719 evidence above.
   Does it originate from the tower's muzzle height, aim at the target's collider center, and does the
   ray's Y ever pass below the wall's actual (undersized) 3m collider top while still appearing, from
   camera view, to pass through the 15m visual? If WO-1719's fix (landing separately, watch for its
   commit) already corrects the collider height to match the renderer, re-verify after that fix lands
   whether this ticket is now moot — do not duplicate that fix here.
4. If NO occlusion check exists at all (towers simply don't test for wall-blocking), that is the root
   cause and the fix is adding one — confirm this by reading the full fire-decision code path (target
   acquisition -> can-fire gate) before concluding it's absent.
5. Add FlowTrace (tagged `[Flow:DefenseTower]` or matching the file you fix) at the LOS decision point
   so a future case is provable from one log read.
6. Fix the actual cause. If it turns out to be the exact same collider/renderer mismatch WO-1719's
   follow-up is already fixing, say so plainly, make no further code change here, and note this ticket
   closes once that fix is verified — do not fix the same bug twice in two files.

## Gate

Brace/NUL gate on every file you touch (`python tools/gate_brace.py <paths>`). Do not run a full Unity
gate yourself — the lead batches this with other in-flight lanes. Do not commit.

## Acceptance criteria

- A standing, undamaged wall segment blocks tower fire at a target with no direct line of sight over/
  around it.
- A collapsed/destroyed wall segment does NOT block tower fire (existing, working behavior per
  `WallSegment.Collapse`'s own log line "it no longer blocks tower line-of-sight" — do not regress this).
- FlowTrace instrumentation added, nothing stripped.
- WO's own Status line flipped and `.RESULT.md` written on hand-back, per CLAUDE.md §11.
