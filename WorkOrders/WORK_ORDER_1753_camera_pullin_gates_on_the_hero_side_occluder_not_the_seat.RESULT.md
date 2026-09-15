# WORK ORDER 1753 — RESULT (a CLAIM, not a fact: nothing here has been compiled)

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** edit-only, 2026-09-15. No Unity, no compile gate, no regression run, no build, no bake, no git.
**Judge this by:** `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a FRESH log, and the owner's felt test
of a raid corridor (acceptance item 3). Until then every line below is unverified by execution.

---

## 1. What changed

### `Assets/_Modules/Village/Hero/SmartMobileCamera.cs`

| Line(s) | Change |
|---|---|
| `:277-283` | New `private readonly List<float> _occluderDistances` — this frame's accepted hit distances. Cleared, never reallocated (capacity 16 = `_occluderHits`), so the frame path stays allocation-light like every buffer beside it. |
| `:1355` | `_occluderDistances.Clear();` at the top of the sweep. |
| `:1372-1374` | Every accepted hit distance is recorded. `nearestOccluderDist` is KEPT — the fade trace names it. |
| `:1397-1412` | **THE FIX** (the ⛔ comment block plus the two lines). `occluderGateDist = SelectOccluderGateDistance(_occluderDistances)` (`:1411`, the FARTHEST hit) and `pullingIn = ShouldPullIn(fullDist, occluderGateDist, _occluderPullInDistance)` (`:1412`) — the gate is now the SEAT-SIDE GAP. `_occluderPullInDistance` stays **0.6** (`:252`). |
| `:1415-1421` | `AllowedCameraDistance(fullDist, **occluderGateDist**, …)` at `:1419-1420` — see §2, a deliberate consequence, not scope creep. |
| `:1429` · `:1499-1580` | The trace call now carries the gate distance; `TraceOcclusionOutcome` (`:1504`) prints the seat-side gap in both lines. The pinned substrings `OCCLUDER PULL-IN ENTERED` / `OCCLUDER PULL-IN RELEASED` / `OCCLUDER FADED` are untouched (§12 forbids stripping, not correcting). |
| `:1535-1580` | New public pure statics `NoOccluderGateDistance` (-1f sentinel, `:1541`), `SelectOccluderGateDistance(IList<float>)` (`:1562`), `ShouldPullIn(...)` (`:1577`). They sit ABOVE `AllowedCameraDistance`'s own WO-1734 doc block (`:1583-1600`), which stays attached to that method — a first placement had orphaned it onto the new const. |

### `Assets/Editor/Regression/CameraWallOcclusionRegression.cs`

| Line(s) | Change |
|---|---|
| `:51` | Calls the new `CheckSeatSidePullInGate(failures)`. |
| `:73-97` | The source-text pin `nearestOccluderDist < _occluderPullInDistance` is **INVERTED from required to FORBIDDEN**, plus a new forbid on `fullDist - nearestOccluderDist` (the same defect wearing the new gate's clothes), plus three new REQUIRED pins that the shipped `ApplyCollision` actually routes through `SelectOccluderGateDistance` / `ShouldPullIn` and seats against the same occluder the gate judged. The WO-1734 `float.MaxValue` forbid and the `_minCollisionDistance` pin are unchanged and still hold. |
| `:225-296` | `CheckSeatSidePullInGate` — **five BEHAVIOURAL cases** (it CALLS the statics; a `Contains()` cannot tell a max from a min). |

## 2. The one decision beyond the ticket's letter — stated, not smuggled

The ticket specifies the **gate**. It does not say what `AllowedCameraDistance` is fed. I changed that
too, to `occluderGateDist`, and it is **necessary rather than optional**: leaving `nearestOccluderDist`
there re-creates the collapse from the other side. Worked example with the shipped numbers
(boom 4.5 m, skin 0.2, floor 1.2): hits `{0.5, 4.45}` → gap `4.5 - 4.45 = 0.05 < 0.6` → pull in;
seat = `clamp(4.45 - 0.2, 1.2, 4.5)` = **4.25 m**, a 0.25 m nudge off the wall, and the 0.5 m occluder
is FADED. Feeding the nearest hit instead gives `clamp(0.5 - 0.2, 1.2, 4.5)` = **1.2 m** — the 3.75x
zoom this ticket exists to delete. Case 5 of the regression pins the 4.25.

## 3. The regression cases, and why a `nearestOccluderDist` implementation FAILS them

| Case | Input | Farthest (shipped) | Nearest (the defect) |
|---|---|---|---|
| 1 — the discriminating two-occluder arrangement the ticket names | hits `{0.5, 4.45}`, boom 4.5 | gate 4.45 → gap 0.05 → **pull in** | gate 0.5 → gap 4.0 → **no pull-in; camera body inside the seat-side wall → FAILS** |
| 2 — the ticket's regression case | hits `{0.5}`, boom 4.5 | gap 4.0 → **no pull-in** | `0.5 < 0.6` → **fires, seat collapses to the 1.2 m floor** — the defect |
| 3 — the backstop is not disarmed | hits `{4.2}` | gap 0.3 → **pull in** | — |
| 4 — empty / null sweep | `{}`, `null` | `-1` sentinel, no pull-in | — |
| 5 — the seat | gate 4.45 | 4.25 m | 1.2 m → **FAILS** |

Case 1 is the one the ticket demanded: a min-based implementation fails it by arithmetic, not by a lint.

## 4. Brace + NUL (the gate's OWN rule, CLAUDE.md §1)

`python tools/gate_brace.py` on all three files this lane touched:
`GATE_BRACE_SUMMARY bad=0 of 3`, **exit 0**.
Raw balance: `SmartMobileCamera.cs` 149/149 · `CameraWallOcclusionRegression.cs` 30/30 ·
`RaidBaseGenerator.cs` 388/388. Embedded NUL bytes: **0** in all three.

## 5. Proven vs unproven

**Proven this session (read at source):** the pre-fix gate was `nearestOccluderDist < _occluderPullInDistance`
(`SmartMobileCamera.cs:1386` before the edit); the sweep starts at `pivot` (`:1352`); `_occluderPullInDistance`
is `0.6f` (`:252`); `AllowedCameraDistance` clamps to the `_minCollisionDistance` floor (`:1509-1513`);
the suite required the very string now forbidden. Brace/NUL results above are measured.

**NOT proven (no Unity in this lane):** that the file compiles; that `CameraWallOcclusionRegression.Run`
passes; that the felt symptom is gone. The 4.5 m boom is read off the ticket's arithmetic, not
re-measured from the prefab's serialized `_followOffset` — the regression's `boom` const is a test
fixture, not a claim about the scene.

## 6. Acceptance

| Item | State |
|---|---|
| 1. A wall 3.9 m in front of the seat no longer triggers the backstop; a wall AT the seat still does | **DONE in code**, pinned by cases 2 + 3 — unproven until the suite runs |
| 2. A case pinning farthest-vs-nearest, incl. the two-occluder arrangement; nearest must FAIL | **DONE** — case 1 (§3) |
| 3. Owner felt-tests a raid corridor | **OPEN** — needs a tester build |
| 4. Status flipped + `.RESULT.md` written | **DONE** — this file |
