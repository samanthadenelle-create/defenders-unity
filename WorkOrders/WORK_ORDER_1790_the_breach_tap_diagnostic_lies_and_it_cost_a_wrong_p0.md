# WORK ORDER 1790 — The breach-tap diagnostic **draws an invalid inference in its own message**, and it produced a wrong P0 the same day

**Status:** IMPLEMENTED, NOT YET GATED

*(2026-09-16, lane A · raid input — same file as WO-1777, one lane. Edit-only: no Unity, no compile
gate, no regression run, no commit. `gate_brace` exit 0 + zero NUL bytes on both touched `.cs`.
Detail: `WorkOrders/WORK_ORDER_1790_the_breach_tap_diagnostic_lies_and_it_cost_a_wrong_p0.RESULT.md`.
⚠ §4's first two bullets need a fresh Seeker capture with deliberate on-wall and off-wall taps, which
this lane could not take; the third (a grep showing no line asserts an unmeasured cause) is pinned by
a new source-lint case and was also run by hand.)*

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** raid instrumentation — the diagnostic block in `Assets/_Modules/Village/Troops/RaidDeployController.cs:1034-1051`. **No gameplay behaviour, no `.unity`, no bake. The only behaviour that may change is what the trace SAYS.**

**Build under test:** `2026.09.16.371701`, built 2026-09-15 22:06 from `f6653501f`.

**Why this is a ticket and not a nicety:** CLAUDE.md §12 makes instrumentation the thing every fix is built on, and §11B forbids stating an unproven thing as fact. **An instrument that states a false conclusion is worse than no instrument** — it converts "we don't know" into "we know, wrongly", and the reader cannot tell. P1 on the lane's own terms.

---

## 1. WHAT HAPPENED

On 2026-09-16 this lane read the owner's Bastion capture, found `HandleBreachTap OUT: outcome=not_wall_segment` ×40 against `outcome=success` ×1, and beside it:

```
[Flow:Raid] nearest WallSegment='Wall_Outer_SS_3' colliderPresent=True colliderEnabled=True
  colliderIsTrigger=False colliderLayer=Structure
  colliderBounds=Center: (-39.21, 2.00, -54.00), Extents: (1.96, 2.00, 0.75)
  rendererBounds=Center: (-39.21, 2.00, -53.99), Extents: (4.12, 2.00, 1.73)
  colliderBoundsIntersectsRay=False rendererBoundsIntersectsRay=False
```

It concluded the wall's collider was **half the width of the visible wall** and wrote a P0 ticket saying so. **That was false on both halves**, and the ticket had to be rewritten (see WO-1777 §2, where the correction is kept on the record). The real cause was in the **screen coordinates the same lines already carried** — 40 taps inside one ~100x50 px box at the bottom edge of the screen.

## 2. THE THREE DEFECTS IN THE INSTRUMENT

**2a. `rendererBounds` is not the visible wall.** `RaidDeployController.cs:1051` aggregates `GetComponentsInChildren<Renderer>(true)` — **includes-inactive** — so it encapsulates the hidden placeholder twin and the inactive `Ruin_*` rubble tiles now parented under every segment (the IronBastion scene carries 158 `WallSegment`, 174 `Clad_*`, 158 `Ruin_`, 158 `RuinStep`). The number it prints is the union of visible and invisible geometry, and it is reported under a name that means "what the player sees".

⛔ **WO-1723 §6 had already retired exactly this reading**: *"The renderer bounds it measured are `GetComponentsInChildren<Renderer>` on the segment — the invisible baked twins, not the clad."* The instrument outlived the correction and re-seeded the same wrong conclusion nine days later.

**2b. `nearest` is chosen by distance to the CAMERA, not to the ray.** `:1043-1047`:

```csharp
float d = (walls[i].transform.position - ray.origin).sqrMagnitude;
```

So the "nearest WallSegment" is simply the wall closest to the camera — which, for a tap that never went near a wall, is an arbitrary wall.

**2c. And the message then asserts a conclusion that does not follow.** `:1034-1039` reads the two `…IntersectsRay=False` values as evidence that *the ray itself is wrong*. For a wall selected by camera distance rather than by the ray, both Falses are **expected noise about a wall that was never tapped**. The comment turns that noise into a diagnosis. The sibling line does the same: `HandleBreachTap OUT: outcome=not_wall_segment hitCollider='RaidGround' - the raycast hit something, but GetComponentInParent<WallSegment>() found no wall on it` invites the reader to suspect the wall hierarchy, when the honest statement is *"nothing was aimed at"*.

## 3. THE FIX

1. `:1043-1047` — choose the nearest wall by **distance from the ray** (`Vector3.Cross` / closest-point-on-ray), not from `ray.origin`.
2. `:1051` — exclude inactive renderers and `Clad_*` / `Ruin_*` / `RuinStep` children; or, better, report the **collider** bounds and the **clad panel** bounds as two separately-named values and stop printing a meaningless union under the name `rendererBounds`.
3. `:1034-1039` — **delete the inference.** The line may state what was measured and nothing more. Where a conclusion is genuinely warranted, gate it on the condition that warrants it.
4. **Print the screen point's normalised position and whether it fell in the bottom/edge band.** That single field would have made WO-1777's real cause obvious on first read, and it is the field the reader needs when a tap resolves to nothing.
5. ⛔ **Do not delete the diagnostic.** Instrumentation is permanent (CLAUDE.md §12, owner ruling 2026-08-09); it may be flagged off, never stripped. The fix is to make it truthful.

## 4. ACCEPTANCE

- A fresh Seeker capture of one Bastion raid with deliberate off-wall taps and deliberate on-wall taps. For each off-wall tap the trace must state, without editorialising, that nothing was aimed at, and report the normalised screen point.
- For an on-wall tap, the reported nearest wall must be the wall that was hit.
- A grep showing no line in the block asserts a cause it has not measured.
- `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 5. DO NOT TOUCH

The resolver itself (`RaycastGround` `:909`, `:924`) — correct. The UI guard at `:739` — **WO-1777 owns that.** Collider sizing in `RaidBaseGenerator.cs` / `RaidBaseDresser.cs`. Any `.unity` file.
