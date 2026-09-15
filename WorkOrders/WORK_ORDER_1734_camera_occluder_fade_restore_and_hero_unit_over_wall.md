# WORK ORDER 1734 — Restore the camera occluder FADE, and give the hero the troops' unit-over-wall rule

**Status:** FIXED - both fixes on disk; awaiting the lead's gate and the owner's felt-test
**Date:** 2026-09-15
**Lane:** Hero feel (camera + targeting) — file-disjoint from the raid-geometry lanes
**Silo:** `Assets/_Modules/Village/Hero/` + one regression file
**Owner rulings:** two, quoted verbatim in §1 and §2

---

## Why these two are ONE ticket

The owner feels them as one thing: in a raid gate the camera spins and the reticle keeps grabbing
walls. They are two independent defects in two files, but they compound in exactly the same place —
a narrow ~4 m raid gate flanked by `Wall_Outer_*` panels. Fixing either alone leaves the moment
still wrong, so they were ruled together and are shipped together.

Neither was re-diagnosed by this lane. Both root causes were proven before the work order existed
and are recorded below with the evidence that proved them.

---

## 1. CAMERA — the DEF-151 hard pull-in came back under WO-385's own comment

### Owner ruling (2026-09-15), verbatim

> **"Restore the fade + guard as documented"**

### The proven regression

Commit `486cd7b17` (2026-09-01, *"feat: finalize mobile UI combat art and Windows handover"*)
replaced WO-385's "fade the occluder, hold the seat" with the DEF-151 hard pull-in that WO-385
existed to delete. Its diff:

```
-                FadeOccluder(col);
-            RestoreFadedNotHitThisFrame();
-            if (nearestOccluderDist < _occluderPullInDistance)
+            if (nearestOccluderDist < float.MaxValue)
-                float allowed = nearestOccluderDist - _collisionSkin;
+                float allowed = AllowedCameraDistance(fullDist, nearestOccluderDist, _collisionSkin);
```

Read at source before this lane touched anything:

* the guard was `if (nearestOccluderDist < float.MaxValue)` — **true for ANY occluder at ANY
  distance** — while the comment three lines above it still read *"normal corner walls (well beyond
  `_occluderPullInDistance`) are faded, not pulled in."* The comment described behaviour that no
  longer existed.
* `FadeOccluder` had **zero callers**.
* `_occluderPullInDistance` (0.6f), `_minCollisionDistance` (1.2f) and `_collisionApproachSpeed`
  (40f) were declared, serialized, and referenced **only from comments** — dead inspector knobs.
* `RestoreAllFaded()` ran at the top of every frame, clearing `_faded` before anything could be
  added to it, so even if a fade call had survived it could not have held across two frames.

**Consequence:** `AllowedCameraDistance` clamped to a bare `0.25f` floor and the seat snapped in
instantly. In a ~4 m raid gate the camera collapses to a quarter-metre from the hero's chest, where
a small yaw becomes an enormous screen rotation. That is the owner's "camera spin".

### What was restored

The `ApplyCollision` body is restored to its shape at `486cd7b17^`, which is what the file's own
headers (`:1237-1246`, `:469-482`) have described the whole time:

* `_fadedThisFrame.Clear()` replaces the per-frame `RestoreAllFaded()`, so the fade set has state to
  hold across frames.
* `FadeOccluder(col)` is called for every occluder again, and `RestoreFadedNotHitThisFrame()` runs
  each frame, so a wall goes ShadowsOnly and pops back the instant it stops occluding.
* The pull-in gate is `nearestOccluderDist < _occluderPullInDistance` again — point-blank only.
* `_collisionApproachSpeed` drives the pull-in and `_collisionReturnSpeed` the ease-out, as
  documented.

### What this lane had to DECIDE (not restore)

`AllowedCameraDistance` did not exist before `486cd7b17` — the floor was inline. Deleting the static
would delete the regression's only pure hook, so it was **kept and given a fourth parameter**:

```csharp
public static float AllowedCameraDistance(
    float fullDistance, float hitDistance, float skin, float minDistance)
```

The old 3-arg overload with its hardcoded `0.25f` was **removed, not kept**: it had no production
callers and would have existed only to keep a test green. `Mathf.Min(minDistance, fullDistance)` is
used for the floor because `Mathf.Clamp` returns `min` when `min > max` — a 1.2 m floor against a
0.9 m boom would otherwise push the camera **further out** than its own authored seat.

> ⚠ **A WO-385 QUIRK CAME BACK WITH THE RESTORE, DELIBERATELY UNCHANGED.** `_occluderPullInDistance`
> is 0.6 m and `_minCollisionDistance` is 1.2 m, so whenever the point-blank backstop fires the
> clamped result is always the 1.2 m floor — i.e. the camera seats *beyond* the occluder it just
> detected, and sees the hero through the wall it also faded. That is how WO-385 shipped and it is
> harmless (the fade is doing the work; the floor only stops the near clip burying itself). Named
> here so the next seat does not "discover" it as a bug: **this lane restored, it did not re-tune.**
> If the owner ever wants the backstop to actually seat on the wall's near side, that is a knob
> change (raise `_occluderPullInDistance` above `_minCollisionDistance`), not a code change.

### The suite that had to move — and the finding inside it

`Assets/Editor/Regression/CameraWallOcclusionRegression.cs` **pinned the defect**. Quoted verbatim
as it stood:

```csharp
/// <summary>WO-1289: walls stay visible and the camera always seats on their near side.</summary>
...
            float tight = SmartMobileCamera.AllowedCameraDistance(5f, 0.1f, 0.2f);
            if (Math.Abs(tight - 0.25f) > 0.001f)
                failures.Add("tight wall hit did not use the near-side emergency floor");
...
            if (method.Contains("FadeOccluder(col)"))
                failures.Add("ApplyCollision still hides whole wall renderers");
            if (!method.Contains("nearestOccluderDist < float.MaxValue"))
                failures.Add("camera does not collision-resolve every obstruction");
```

It required `FadeOccluder(col)` to be **absent** and `float.MaxValue` to be **present** — i.e. it
locked the regression in place.

Two facts make moving it safe rather than a weakened test:

1. **`git log --all -- <that file>` returns exactly one commit: `486cd7b17`.** The suite was born in
   the same commit as the defect it pins. It is not an independent later ruling being overridden.
2. **Its "WO-1289" citation points at the wrong work order.**
   `WorkOrders/WORK_ORDER_1289_ground_meadow_regrade_chroma_oracle.md` is the ground-meadow regrade
   — nothing to do with the camera. There is no camera ruling behind the pin.

Both source-text assertions are **inverted**, not deleted, and three more were added, so the same
lines are still pinned — now to the documented contract instead of against it:

* `FadeOccluder(col)` must be PRESENT
* `RestoreFadedNotHitThisFrame()` must be PRESENT
* `nearestOccluderDist < float.MaxValue` must be ABSENT
* `nearestOccluderDist < _occluderPullInDistance` must be PRESENT
* `_minCollisionDistance` must appear in the method
* `tight` now expects the authored **1.2 m** floor, not `0.25f`
* new `floorAboveBoom` case pins the `Mathf.Clamp` min>max trap

The `nearClipPlane 0.08f` and smoothed-position-ordering assertions are untouched, and the
`CAMERA_WALL_OCCLUSION_OK` marker shape is unchanged (only its trailing prose).

> ⚠ **THEN THIS SAME LINT MISFIRED A SECOND TIME, ON THE FIX, AND WENT RED AT THE LEAD'S GATE.** It
> fenced its scan between two method-name anchors; `TraceOcclusionOutcome` was added between them in
> this same session, and its null-sentinel `nearestOccluderDist < float.MaxValue` matched the
> forbidden-string assertion. The rule was right; the SPAN was wrong. Replaced with a brace-matched
> `ExtractMethodBody` that fails closed. **A source-text lint fenced on "the next method signature"
> is only correct until somebody adds a method.** Full incident, the arithmetic that proves the new
> span, and the re-check of every other assertion for the same fault: see the `.RESULT.md`.

### Dungeon safety — checked, not assumed

WO-958's header claims occlusion thrash is "switched OFF" in dungeons, so restoring the fade could
in principle re-introduce dungeon strobing. Verified in CODE, not comment: `SmartMobileCamera.cs:536`
sets `_collisionEnabled = false` on the dungeon profile, and
`Assets/Editor/Regression/DungeonFpvRegression.cs:181` pins that assignment. `ApplyCollision`
early-returns on `!_collisionEnabled`, so no dungeon path reaches the fade at all.

---

## 2. HERO TARGETING — mobs first, walls last

### Owner ruling, verbatim

From `WorkOrders/WORK_ORDER_1730_troops_must_prefer_nearby_hostiles_over_walls.md`, extended to the
hero by the owner on 2026-09-15:

> **"should never default to target wall, should always default to aggresive targets nearby first
> asnd then only then wall"**

### The proven root cause

* `HeroTargetIndicator.RebuildCandidates` sorts by squared distance **only** — no type term.
* `NearestCandidate()` returned the first in-arc entry — no unit-vs-structure test anywhere.
* `WallSegment` is `Hostile` when the scene is enemy-owned, and `Awake` ORs `Structure` onto
  `_enemyMask`, so enemy walls are admitted as legitimate candidates. Admitting them is the FEATURE
  (WO-1458) — the bug is that they are admitted at the same PRIORITY as a mob.

So a ~4 m wall panel 2 m away outranked a mob at 5 m. Captured on device (build 370139): five
`[Flow:Reticle] TARGET ACQUIRED (auto) -> 'Wall_Outer_*'` inside 626 ms, and a straight
SS_5 ⇄ SS_6 oscillation.

### What was implemented

**ONE gate in front of the existing selection**, not a priority-aware sort — the seam WO-1719
established for the troop side and explains at `RaidAssaultAi.cs:230-241`:

```
NearestCandidate()
  -> NearestCandidateOfClass(unitsOnly: true)    // a unit, if ANY unit is acquirable
  -> NearestCandidateOfClass(unitsOnly: false)   // else the existing nearest-wins rule
  -> ApplyAutoSwitchHysteresis(...)
```

The DEF-269 / WO-1105 R2 body is kept intact — `unitsOnly` adds exactly **one** `continue` line to
it, so with the flag false it is the rule it has always been and a regression can still pin
"nearest wins among equals" against an unchanged body.

**Classification mirrors the troop side verbatim:** `cand is IDamageableStructure`, the same test as
`TroopController.IsHostileStructure`. Verified at source: `WallSegment`, `Gate`, `DefenseTower` and
`RaidSpire` all declare `: MonoBehaviour, IDamageable, IDamageableStructure`; `EnemyDamageable` and
`DragonBoss` declare `IDamageable` without it, and `Enemy` itself is a plain `MonoBehaviour`. So the
classifier is exact on both sides of the line.

> ⚠ **FOR THE OWNER TO SEE, NOT A BUG:** because the raid spire is an `IDamageableStructure`, a
> garrison unit also outranks the **win-condition spire**, not only walls. That is her troop rule
> applied consistently — the spire is acquirable again the moment no unit stands — but it is a
> behaviour change she should be told about rather than discover.

Walls remain fully targetable: with no unit acquirable, pass 2 is the unmodified old rule, and a
deliberate manual tap-lock on a wall never consults auto-acquire at all (`_locked ?? NearestCandidate()`).

### The oscillation (second, separate defect)

`HoldsCurrentAutoTarget(...)` — a pure static, so it is regression-pinnable without a scene — holds
the current auto pick against a rival unless the rival is closer by more than
`_autoSwitchStickinessMeters` (**1.0 m**) or the hold is older than `_autoSwitchMinDwellSeconds`
(**0.35 s**). Both are serialized and tunable.

⛔ **Stickiness applies ONLY between two targets of the same kind.** A unit displacing a held wall
crosses classes and is exempt, so the §2 priority gate can never be delayed by the anti-oscillation
rule. `ClearLock()` drops the stickiness state so an explicit clear is never overridden.

⛔ **AND THE HELD TARGET IS RE-CHECKED AGAINST THE SELECTION'S OWN GATES, NOT JUST THE CANDIDATE
LIST** (`IsStillAutoAcquirable`). `_candidates` is built from `_acquireRange` + faction + LoS only;
the selection body applies two MORE gates on top — the WO-1105 R2 `AutoEngageRange()` ring and the
DEF-269 forward arc. A `_candidates.Contains(...)`-only validity test would have held wall A while
the hero turned 180° to face wall B, leaving the reticle on a target **behind the hero** — exactly
the spam-at-your-back that DEF-269's header says `NearestCandidate` returns null to prevent, and it
would have bitten hardest while turning inside a raid gate, the very moment this ticket exists to
fix. Caught in review before hand-back; the stickiness may only ever hold a target the selection
itself would still accept.

---

## 3. Instrumentation added (§12 — permanent, never stripped)

Greppable strings, both designed so the owner's next capture answers the question without a theory:

| String | Where | Kind |
|---|---|---|
| `OCCLUDER PULL-IN ENTERED` | `SmartMobileCamera.TraceOcclusionOutcome` | `Step`, edge-triggered |
| `OCCLUDER PULL-IN RELEASED` | same | `Step`, edge-triggered |
| `OCCLUDER FADED x<n>` | same | `Throttle("Camera","occluder-fade",2f)` |
| `AUTO PICK '<name>' WHY=` | `HeroTargetIndicator.NearestCandidate` | `Step`, on change only |
| `AUTO SWITCH HELD` | `HeroTargetIndicator.ApplyAutoSwitchHysteresis` | `Throttle("Reticle","auto-switch-held",2f)` |

The camera transition is **edge-triggered `Step`, not `Throttle`**, deliberately: a pull-in episode
can last two frames and a throttle window would miss it entirely — which is the exact thing this
ticket needed to be able to see. The steady state is rate-limited so the frame path never floods the
log (CLAUDE.md §12 / memory `logcat-ring-buffer-destroys-evidence`).

`AUTO PICK ... WHY=` names the winning reason as one of `unit-over-wall (WO-1734 priority gate...)`,
`nearest (...)`, or `no acquirable hostile in the engage arc`, so the two defects can be told apart
in a single capture.

The pre-existing unit-pass out-of-range path deliberately does **not** print the old
`auto-acquire HELD` line — with two passes, a null from pass 1 is normal, and a false "held" line
would send the next triage hunting a range bug that is not there.

---

## 4. Files changed

| File | Change |
|---|---|
| `Assets/_Modules/Village/Hero/SmartMobileCamera.cs` | fade restored; pull-in re-gated on `_occluderPullInDistance`; `_minCollisionDistance` now bounds the seat; `_collisionApproachSpeed` re-wired; `_wasPullingIn` field; `TraceOcclusionOutcome` |
| `Assets/_Modules/Village/Hero/HeroTargetIndicator.cs` | unit-over-wall gate; `NearestCandidateOfClass(bool)`; `HoldsCurrentAutoTarget`; `ApplyAutoSwitchHysteresis`; two serialized knobs + three fields; `ClearLock` reset |
| `Assets/Editor/Regression/CameraWallOcclusionRegression.cs` | assertions inverted to the documented contract; mis-citation recorded; two new cases |
| `Assets/Editor/Regression/HeroUnitOverWallTargetingRegression.cs` | **NEW** — pins §2 (five pure stickiness cases + six seam-shape assertions) |
| `CLI_LANES_WO_NUMBERS.md` | minted 1734, bumped to 1735 in the same edit |
| `MASTER_PIPELINES_BACKLOG_2026-06-06.md` | slotted into Lane 3 (Combat Feel) |

### ⚠ ONE REGISTRATION LINE IS OUTSTANDING — THE LEAD'S STEP, NOT THIS LANE'S

`HeroUnitOverWallTargetingRegression` is written but **NOT registered**, so it does not run yet. A
suite that is not registered is not coverage. The registration lives in `DataRegression.cs`, which
the orchestration cadence reserves for the lead. The line to add, matching the existing
camera-occlusion row at `DataRegression.cs:1116`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-unit-over-wall suite", () => { if (!DeNelle.Editor.Regression.HeroUnitOverWallTargetingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-unit-over-wall] " + r); });
```

It bumps the suite count in `REGRESSION_OK <n>/<n>` by one, which the lead should expect.

`PlayerAttackController.cs` was **not** touched — see §6.

## 5. NOT touched (other lanes hold these)

`RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `WallSegment.cs`, `BuildModeController.cs`.

## 6. Observation for the lead — not fixed here

`Assets/_Modules/Village/Enemies/PlayerAttackController.cs:575` states its target priority is the
reticle's `HeroTargetIndicator.CurrentTarget`, so the fix above does move what the hero faces and
aims at. But the melee swing's DAMAGE is a 360° `Physics.OverlapSphere` (`:595`, `:700`) that hits
everything in range regardless of the reticle. So the reticle will stop *pointing* at a wall, while
a sweep next to a wall still chips it. That is a sweep, not an acquisition, and changing it is a
separate ruling — flagged, deliberately not touched.

## 7. Acceptance

- [ ] Lead: `COMPILE_GATE_OK` on a fresh log
- [ ] Lead: `REGRESSION_OK <n>/<n>` with `CAMERA_WALL_OCCLUSION_OK` present
- [ ] Owner felt-test in a raid gate: the camera holds its seat and the wall fades — no spin
- [ ] Owner felt-test: the reticle takes the mob, not the panel; no SS_5 ⇄ SS_6 flicker
- [ ] Owner sees and accepts the spire note in §2

**This lane did NOT gate, build or commit — edit-only. Nothing here is verified.**
