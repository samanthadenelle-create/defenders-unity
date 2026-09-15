# WO-1734 RESULT — camera occluder fade restored; hero takes mobs over walls

**Date:** 2026-09-15
**Lane:** Hero feel (camera + targeting) — EDIT-ONLY
**Status of this result:** code on disk, brace-gated. **NOT compile-gated, NOT built, NOT committed,
NOT felt-tested.** The lead owns all of those. Nothing below is a verification claim.

---

## What landed

### Camera (`Assets/_Modules/Village/Hero/SmartMobileCamera.cs`)

`ApplyCollision` is restored to its shape at `486cd7b17^` — the behaviour the file's own headers at
`:1237-1246` and `:469-482` have described continuously while the code did the opposite:

* `_fadedThisFrame.Clear()` replaces the per-frame `RestoreAllFaded()` that made the fade set unable
  to hold state across frames
* `FadeOccluder(col)` is called again for every occluder; `RestoreFadedNotHitThisFrame()` runs each
  frame so a wall pops back the instant it stops occluding
* the pull-in gate is `nearestOccluderDist < _occluderPullInDistance` (0.6 m, point-blank) instead
  of `nearestOccluderDist < float.MaxValue` (any occluder, any distance)
* `_collisionApproachSpeed` drives pull-in, `_collisionReturnSpeed` the ease-out
* `AllowedCameraDistance` gained a `minDistance` parameter so the floor is the authored
  `_minCollisionDistance` (1.2 m) rather than a bare `0.25f` literal. The 3-arg overload was removed
  — it had no production callers.

### Targeting (`Assets/_Modules/Village/Hero/HeroTargetIndicator.cs`)

* ONE gate in front of the existing selection: `NearestCandidateOfClass(unitsOnly: true)` first, the
  unchanged nearest-wins rule as fallback. The DEF-269 / WO-1105 R2 body took exactly **one** added
  `continue` line.
* Classification is `cand is IDamageableStructure` — the same test as
  `TroopController.IsHostileStructure`, confirmed at source against `WallSegment` / `Gate` /
  `DefenseTower` / `RaidSpire` (structures) and `EnemyDamageable` / `DragonBoss` (units).
* `HoldsCurrentAutoTarget(...)` — pure static, regression-pinnable — plus
  `ApplyAutoSwitchHysteresis`: 1.0 m distance margin, 0.35 s minimum dwell, **same-class only**, so
  a unit displacing a held wall is never delayed by the stickiness.
* `IsStillAutoAcquirable(...)` — the held target is re-checked against the SELECTION's gates
  (`AutoEngageRange()` ring + DEF-269 forward arc), not merely `_candidates.Contains`. Caught in
  review: a Contains-only test would have kept the reticle on a wall **behind the hero** after a
  180° turn, reintroducing the spam-at-your-back DEF-269 exists to prevent, in the raid gate this
  ticket is about.
* `ClearLock()` drops the stickiness state.

### New suite (written, NOT registered — the lead's step)

`Assets/Editor/Regression/HeroUnitOverWallTargetingRegression.cs` — five pure
`HoldsCurrentAutoTarget` cases (including the cross-class exemption that keeps the priority gate
undelayable) and six seam-shape assertions. **It does not run until one line is added to
`DataRegression.cs`; that line is quoted in the WO §4.** Until then §2 has no coverage.

### Regression moved (declared, not silent)

`Assets/Editor/Regression/CameraWallOcclusionRegression.cs` **pinned the defect** and had to move
with the restoration. Full quote of what it asserted is in the WO §1. Two facts justified moving it:

1. `git log --all -- <file>` returns exactly one commit — `486cd7b17`, the same commit that
   introduced the pull-in. The suite was born with the defect, not as an independent later ruling.
2. Its header cited "WO-1289", which is
   `WorkOrders/WORK_ORDER_1289_ground_meadow_regrade_chroma_oracle.md` — the ground-meadow regrade.
   There was no camera ruling behind the pin.

Both source-text assertions were **inverted, not removed**, and three further assertions were added,
so the file pins strictly more than before. `tight` now expects 1.2 m (the authored floor) instead
of 0.25 f. The `CAMERA_WALL_OCCLUSION_OK` marker shape is unchanged.

---

---

## ⚠ GATE RED AFTER HAND-BACK — a FALSE POSITIVE I caused, and the lesson from it

The lead's gate came back `CAMERA_WALL_OCCLUSION_FAIL: the DEF-151 hard pull-in is back: ANY
occluder at ANY distance pulls in` (`Builds/regPre`, `REGRESSION_FAIL: 2 failure(s)
(530/532 registered suites green)` — the other failure is a different lane's intentional red).

**The camera fix was correct. My own new lint was wrong.**

### What happened

The suite sliced the source with `IndexOf("private Vector3 ApplyCollision")` ..
`IndexOf("private void FadeOccluder")` and ran `Contains(...)` over that whole span. In the same
session I inserted `TraceOcclusionOutcome` **between those two anchors**. Its trace-formatting line

```csharp
string occluder = nearestOccluderDist < float.MaxValue
    ? nearestOccluderDist.ToString("0.##") + "m" : "none";
```

is a **null sentinel** — "is there an occluder distance worth printing?" — not a pull-in guard. But
it sat inside the scanned span, so my `Contains("nearestOccluderDist < float.MaxValue")` assertion
matched it and reported the DEF-151 pull-in as "back" while the real guard 40 lines above it
correctly read `< _occluderPullInDistance`. Measured after the fact: that string occurs **exactly
once in the whole file**, in `TraceOcclusionOutcome`.

### The lesson — worth keeping

> **A source-text lint that fences on "the next method signature" is only correct until somebody
> adds a method.** That is not a hypothetical: it happened within one session, by the same hand that
> wrote the lint, in the very commit the lint was meant to protect.

This is now the **second** misfire of this one lint in a single day — it had already pinned the
DEFECT in place (see the WO §1), and then it misfired on the fix. Both failures share one root:
**the span was derived from a neighbouring symbol's NAME instead of from the code's own
structure.** Braces are structure. The name of whatever method happens to sit below is not.

### What was actually fixed (the span, not the rule)

The assertion is **unchanged and not weakened** — `nearestOccluderDist < float.MaxValue` inside
`ApplyCollision` is still a hard failure, because it is still the thing that catches the real
regression returning. What changed is the span it is evaluated over:

* new `ExtractMethodBody(source, signature)` — **brace-matched** from the method's own opening
  brace, skipping `//` and `/* */` comments and string/char literals (verbatim and interpolated
  included) so a brace in a comment or a string cannot close the body early.
* it returns empty on "signature absent" or "unbalanced", and the caller **FAILS on empty** rather
  than silently passing every `Contains()` — a lint that cannot find its subject must go red, not
  green.
* the reason it must be brace-matched is written into the method's own `<remarks>`, naming this
  incident, so the next person to edit it does not re-introduce the anchor fence.

### Proven, not assumed

A Python port of the exact same extractor, run against the real file:

```
SPAN lines 1249..1334  chars=4665
contains TraceOcclusionOutcome DEFINITION: False   (must be False)
contains AllowedCameraDistance  DEFINITION: False   (must be False)

FadeOccluder(col)                              present=True  want=True  OK
RestoreFadedNotHitThisFrame()                  present=True  want=True  OK
nearestOccluderDist < float.MaxValue           present=False want=False OK
nearestOccluderDist < _occluderPullInDistance  present=True  want=True  OK
_minCollisionDistance                          present=True  want=True  OK
```

The span now ends at 1334; `TraceOcclusionOutcome` starts at 1343 and is correctly outside it. All
five assertions return the intended verdict.

### The other assertions, re-checked for the same fault (instruction 3)

| Assertion | Scope | Over-broad-span risk |
|---|---|---|
| `smoothAt` / `collisionAt` ordering | whole file, `IndexOf` ordering | **None.** Both needles occur exactly **once** each (measured), so the ordering is unambiguous. |
| `nearClipPlane 0.08f` cap | whole file, existence | **None.** Existence pin; occurs once. |
| the five `method.Contains` pins | `ApplyCollision` body | **Fixed** — now brace-matched. |
| `HeroUnitOverWallTargetingRegression` pins | whole file, existence | **None** — that suite uses no spans at all. Its failure mode would be a weaker pin, never a false RED. |

### Turned the trap into coverage

Three assertions were **added** pinning the trace strings themselves (`OCCLUDER PULL-IN ENTERED` /
`RELEASED` / `OCCLUDER FADED`), deliberately at whole-FILE scope with a comment saying why: they
live in `TraceOcclusionOutcome`, outside the `ApplyCollision` span on purpose. So the string that
caused the false positive is now the subject of a real §12 instrumentation-is-permanent pin.

### One housekeeping note on the brace counts

`ExtractMethodBody` needs to compare against brace characters. Written inline as `'{'` / `'}'` that
gave the file a raw one-liner count of **16 open / 15 close** — *correct code reading as mismatched*,
because the CLAUDE.md §1 raw check counts every brace in the file including those inside literals
(the gate's own scanner, `tools/gate_brace.py`, said `bad=0` throughout). Rather than hand the lead a
number that looks broken, the two characters are declared as a balanced pair of consts
(`OpenBrace` / `CloseBrace`) and one stray brace in a prose comment was reworded. **Both counters now
read 15/15.** Nothing about the logic changed.

---

## Brace gate

```
python tools/gate_brace.py Assets/_Modules/Village/Hero/SmartMobileCamera.cs \
                           Assets/_Modules/Village/Hero/HeroTargetIndicator.cs \
                           Assets/Editor/Regression/CameraWallOcclusionRegression.cs
GATE_BRACE_SUMMARY bad=0 of 3      (exit 0)
```

Raw counts (whole-file `{` vs `}`):

| File | open | close |
|---|---|---|
| `SmartMobileCamera.cs` | 138 | 138 |
| `HeroTargetIndicator.cs` | 133 | 133 |
| `CameraWallOcclusionRegression.cs` | 15 | 15 |
| `HeroUnitOverWallTargetingRegression.cs` | 5 | 5 |

All four also scanned for embedded/trailing NUL bytes (CLAUDE.md §1 WO-434 guard): clean.
Counts above are the FINAL run, after the WO-1733 -> WO-1734 citation correction and after the
`IsStillAutoAcquirable` fix.

### Checked, so it is not a guess

* `grep -n "_collisionApproachSpeed\|_collisionReturnSpeed\|MoveTowards" Assets/Editor/Regression/Dungeon*.cs`
  → **no matches.** No dungeon suite pins the speed asymmetry inside `ApplyCollision`, so restoring
  `_collisionApproachSpeed` moves nothing else. (The dungeon path early-returns on
  `_collisionEnabled = false`, `SmartMobileCamera.cs:536`, pinned by `DungeonFpvRegression.cs:181`.)
* `grep -rn "AllowedCameraDistance"` → only the four regression call sites and the two in
  `SmartMobileCamera`. No stale 3-arg caller survived its removal.
* `grep -rn "HeroTargetIndicator.cs" Assets/Editor/Regression/` → the other four suites that read
  this file pin `[hostile-admit]`, `CombatFactionRules`, `FlowTrace.Measure` in Update/LateUpdate,
  and the ranged-facing drive. None of them touch `NearestCandidate`'s body.

## Greppable trace strings

`OCCLUDER PULL-IN ENTERED` · `OCCLUDER PULL-IN RELEASED` · `OCCLUDER FADED x` ·
`AUTO PICK '<name>' WHY=` · `AUTO SWITCH HELD`

## Open for the lead / owner

* A garrison unit now outranks the raid **spire** as well as walls (the spire is an
  `IDamageableStructure`). Consistent with the troop rule; the owner should see it stated.
* `PlayerAttackController`'s melee swing damage is a 360° OverlapSphere independent of the reticle,
  so a sweep beside a wall still chips it. Flagged, deliberately not touched — separate ruling.
