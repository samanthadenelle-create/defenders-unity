# WORK ORDER 1790 — RESULT

**Outcome: IMPLEMENTED, NOT YET GATED.** Edit-only lane (lane A · raid input, run together with
WO-1777 — same file, one lane): no Unity, no compile gate, no regression run, no commit, no `.unity`,
no bake. `python tools/gate_brace.py` exit 0 and zero NUL bytes on both touched `.cs`.

**Date:** 2026-09-16 · **Branch:** `dev` · **Committer:** the lead (this lane does not commit).

---

## 1. FILES TOUCHED (two, both `.cs` — shared with WO-1777)

| File | What changed for WO-1790 |
|---|---|
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | `LogBreachTapDiagnostics` (`:1225`) rewritten below the mask/raycast section; the `not_wall_segment` and `raycast_miss` OUT lines re-worded; a new `DistanceFromRay` helper (`:1353`). |
| `Assets/Editor/Regression/RaidDeployUiRegression.cs` | One new case, `[breach-diag-truthful]` (`CheckBreachDiagnosticIsTruthful`, `:727`). |

⛔ **The diagnostic was NOT deleted** and must never be — CLAUDE.md §12, owner ruling 2026-08-09.
It may be flagged off; the fix for a lying instrument is to make it truthful.

**Nothing else was opened.** `TroopController.cs`, `RaidAssaultAi.cs` and `TroopBreachOrder.cs` were
**not touched at all** — the diagnostic needed nothing from any of them. `RaidBaseGenerator.cs` /
`RaidBaseDresser.cs` (the collider sizing the audit lane wrongly accused) are untouched, as are
`DataRegression.cs`, every `.unity`, and `CLI_LANES_WO_NUMBERS.md`.

---

## 2. THE THREE DEFECTS, EACH CLOSED

**2a. `nearest` was the wall nearest the CAMERA.** The old pick was the squared magnitude of
`wall.transform.position` minus `ray.origin`. For a tap that never went near a wall that is an
arbitrary wall, so the two `…IntersectsRay=False` values it printed were noise about a wall nobody
aimed at. It now picks by **perpendicular distance from the ray**, `DistanceFromRay(ray, point)`
(`:1353`), with the ray parameter **clamped to t ≥ 0** so geometry behind the camera measures from the
ray origin instead of reporting a false near miss. The trace prints the number —
`nearestWallDistToRay=` — plus `wallsInScene=`, so the reader can see for themselves whether the
reported wall is relevant at all. The label changed too: `WallSegment nearest THE RAY=` (`:1327`).

**2b. `rendererBounds` was an includes-inactive union under a name meaning "what the player sees".**
The old call was `GetComponentsInChildren<Renderer>(true)`. On an IronBastion segment that union
swallows the inactive placeholder twin and the `Ruin_*` rubble tiles, which is how 2.00 m of collider
read as "half of an 8.24 m wall" — the exact reading **WO-1723 §6 had already retired nine days
earlier**. It now unions **active, enabled** renderers only (`GetComponentsInChildren<Renderer>(false)`
plus per-renderer `r.enabled && r.gameObject.activeInHierarchy`) and reports it as
`activeRendererBounds=` (`<none active>` when empty), beside `activeRenderersCounted=` and
`renderersSkippedInactiveOrDisabled=` so an empty union can never be mistaken for a full one. The
intersect flag is renamed to match: `activeRendererBoundsIntersectsRay=`.

**2c. The message asserted a cause it had not measured. The inference is DELETED.**
- The comment that read *"if only the RENDERER bounds intersect, it is a collider/render size
  mismatch … if NEITHER intersects, the ray itself is wrong"* is gone. In its place the block carries
  a stated record of all three defects, so the correction cannot be lost the way WO-1723 §6's was.
- The trace line ends `- MEASURED ONLY.` and explains that a large `nearestWallDistToRay` means the
  wall was never aimed at **and that its two IntersectsRay flags therefore say nothing about the tap**.
  No cause is asserted.
- The sibling OUT line no longer invites suspicion of the wall hierarchy. It was
  *"the raycast hit something, but GetComponentInParent<WallSegment>() found no wall on it"*; it now
  reads `- MEASURED: the ray resolved that collider and GetComponentInParent<WallSegment>() found no
  WallSegment on it or its parents. Nothing more is claimed: this line does NOT say the wall
  hierarchy, the collider or the ray is at fault.`
- The `outcome=raycast_miss` line got the same treatment (`- MEASURED: … no cause is asserted here.`).

**2d. WO-1790 §3.4 — the field that would have named the real cause on first read.** Every one of
these now carries `screenNorm=<nx,ny>` **and** `inReservedThumbBand=<bool>`: `HandleBreachTap IN`,
`HandleDeployTap IN`, `outcome=raycast_miss`, `outcome=not_wall_segment`, and the
`breach-tap-diag-mask` line. The `not_wall_segment` line also gained `hitLayer=`, `hitPoint=` and
`hitDistance=`. On the owner's capture these fields read `screenNorm=0.290,0.065 … inReservedThumbBand=True`
forty times over — the whole of WO-1777, on one line, without a theory.

⚠ **And the instrument is now its own detector:** `inReservedThumbBand=True` on either `IN` line means
the WO-1777 guard regressed, because the guard refuses that band before either method runs. Both lines
say so in their own text.

**Grep prefixes preserved deliberately:** `HandleBreachTap IN - screenPoint=(` and
`HandleBreachTap OUT: outcome=<token>` are unchanged, because the WO evidence greps key on them. The
new fields were appended, never inserted ahead of the prefix.

---

## 3. REGRESSION — `[breach-diag-truthful]`

`CheckBreachDiagnosticIsTruthful` (`Assets/Editor/Regression/RaidDeployUiRegression.cs:727`), in a
suite wired at `Assets/Editor/Regression/DataRegression.cs:630` (read there this session; that file
was not touched). It slices the source between `private void LogBreachTapDiagnostics(` and
`private static float DistanceFromRay(` and asserts:

- **Absent:** the camera-distance expression (`- ray.origin).sqrMagnitude`),
  `GetComponentsInChildren<Renderer>(true)`, and the retired `Splits the remaining causes` comment.
- **Present:** `DistanceFromRay(ray`, `nearestWallDistToRay=`, `activeRendererBounds=`,
  `activeRenderersCounted=`, `renderersSkippedInactiveOrDisabled=`, `screenNorm=`,
  `inReservedThumbBand=`, `MEASURED ONLY`.
- **On the sibling OUT line:** `screenNorm=` and `hitLayer=` present, the editorialising `invites`
  absent.
- **The block still exists at all** — if `LogBreachTapDiagnostics` is gone the case FAILS with the
  §12 never-strip ruling in the failure text. That is WO-1790 §3.5 made mechanical.

This is the mechanised form of WO-1790 §4's third bullet (*"a grep showing no line in the block
asserts a cause it has not measured"*). The same greps were also run by hand this session (§4).

⚠ **Two comment rewrites were required to keep the negative lints honest.** The new explanatory
comment originally quoted the retired code fragment and `GetComponentsInChildren<Renderer>(true)`
verbatim, which would have tripped its own lint. Both are now written in prose. Worth knowing before
editing that comment: the lint is text, and it does not know a quotation from a call.

---

## 4. PROOFS RUN THIS SESSION

| Proof | Result |
|---|---|
| `python tools/gate_brace.py` on both `.cs` | `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0 |
| NUL scan on both `.cs` | 0 and 0 |
| The oracle's block slice + every token, simulated in Python against the real file | 8 positives all True, 3 negatives all False |
| The OUT-line slice (1400 chars from `outcome=not_wall_segment`) | `screenNorm=` True, `hitLayer=` True, `invites` False |
| First occurrence of `outcome=not_wall_segment` is the OUT line, not a comment | `:1164`, verified (a comment that mentioned the token was reworded so the slice cannot start on prose) |
| `RaidDeployUiRegression` is wired into `DataRegression.RunAll` | `DataRegression.cs:630` |
| **No other file consumes a renamed/removed trace token.** Grepped `Assets/Editor`, `tools`, `.claude` for `breach tap missed every WallSegment`, `nearest WallSegment='`, `rendererBounds`, `the raycast hit something`, `Splits the remaining causes`, `breach-tap-diag-nearest`, `breach-tap-diag-mask` | **zero consumers.** `RaidBreachTapLiveProof.cs` and `WallBreachOrderRegression.cs` match none of them; the only `rendererBounds` hits are `CastleGateNavVerify.cs`'s own unrelated local, and the only `Splits the remaining causes` hit is this lane's new negative lint |
| `RaidRepeatClearRegression.cs:423-470` lints `HandleDeployTap`'s body by ORDER (`iSample < iSpawn`, `iSample < iLedger`, and the `return`/`SetStatus`/`FlowTrace` slice between them). The new `HandleDeployTap IN` trace sits ahead of all three indices | ordering relations unchanged; the audited slice untouched |
| `GetComponentInParent<T>(bool includeInactive)` exists on this editor | `ProjectSettings/ProjectVersion.txt` = `6000.4.8f1` (overload shipped in 2020.3) |

---

## 5. ⚠ NOT PROVEN BY THIS LANE

1. **WO-1790 §4's first two bullets are NOT met.** They need a fresh Seeker capture of one Bastion
   raid with deliberate off-wall and on-wall taps: for each off-wall tap the trace must state, without
   editorialising, that nothing was aimed at; for an on-wall tap the reported nearest wall must be the
   wall that was hit. This lane took no device capture and fired no Unity, so **the corrected
   nearest-wall pick has never been observed against a real ray** — only its arithmetic is verified.
2. **No compile gate, no regression run.** Every symbol was verified by grep at source instead;
   `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs are outstanding.
3. **`BOARD.html` was not regenerated and nothing was committed** — the WO `**Status:**` line is
   flipped; `python tools/board_build.py` and the commit are the lead's.
