# WO-1703 - raid ground reaches the walls and carries texture

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:18:57, build 2026.09.17.373943). PRIOR STATUS: FIXED - implemented 2026-09-13 (Codex); lead re-ran on the current tree 2026-09-14 04:08-04:09 via tools/regression/raid_suites_gate.ps1: RAID_GROUND_COVERAGE_OK, RAID_GROUND_SAVED_OK, RAID_POLISH_SAVED_PROOF_OK on fresh Builds/ logs + REGRESSION_OK 04:17; Assets/Generated/RaidGround now tracked (.gitignore exception); PO felt-verifies and closes
**Minted:** 2026-09-10, direct owner request during READY clearance; banner 1702 -> 1704 includes WO-1702.

## Owner direction

"raids should have full floors to the walls some texture ground not just a solid color"

## Required outcome

All raid arenas have a continuous floor reaching their enclosing wall footprint,
with a visible appropriate existing ground texture instead of a flat color.
Keep collision, movement and placement consistent with the rendered ground.
Derive extent from the actual arena/wall authority, not a second hand-authored size.

## Method and acceptance

Inspect current raid capture/log evidence and builder/material paths first.
Reuse existing owned ground materials; do not replace approved walls or props.
Modify builders/runtime authority, never scene YAML. Verify representative raid
types with rendered views, floor-to-wall bounds and material/texture dependency
checks. Fresh compile/regression and relevant capture gates before check-in.
Record any test-build-only proof as Fixed under this session's owner ruling.

WO-1632/1633/1634/1635 received owner Pass in this session and stay closed;
this new ground requirement is a separate change.

## RCA 2026-09-14 (read-only lane)

Read-only lane. No `.cs`/`.unity`/asset edited, no Unity/gate/build/git-write run. Every claim below
cites a file:line opened in the WORKING TREE this session (or `git show HEAD:` where marked). The tree
carries ~580 uncommitted files from the 2026-09-13 Codex session, and **that session already did most
of this ticket's work** — see §4. Where a claim could not be proven from the repo it is marked
UNPROVEN with the measurement that would close it.

### 1. Symptom, one line

The raid arena's *textured* ground covers only a small centre disk; from the edge of that disk out to
the enclosing boundary ring the player sees a flat untextured colour, so the arena reads as "a solid
colour floor that does not reach the walls".

### 2. The two authorities — floor extent vs. wall positions

**Floor (the continuous surface) — `Assets/Editor/RaidNavBake.cs`.**
- `GroundName = "RaidGround"`, `GroundScale = 14f` (`RaidNavBake.cs:42-43`).
- Extent authored in `EnsureGround` (`RaidNavBake.cs:188`). At **HEAD** (`git show
  HEAD:Assets/Editor/RaidNavBake.cs`, `EnsureGround`) it was a fixed `localScale = (14,1,14)` Unity
  Plane at the origin -> **140 m square, ±70 m**, with `MagentaGuard.BuildUrpLitMaterial(c)` and a flat
  colour from `GroundColorFor` (three hard-coded `Color`s, e.g. `(0.18,0.14,0.09)` default) — **no
  texture at all**. That is the "solid color" half of the owner's sentence, at source.
- In the **working tree** `EnsureGround` now measures the ring: `footprint` starts at `GroundScale*10`
  and is `Encapsulate`d with every `Renderer`/`Collider` under `ArenaBoundary_Ring`
  (`RaidNavBake.cs:192-208`), then the plane is rescaled to that footprint
  (`RaidNavBake.cs:231-235`) and textured from an owned `TerrainLayer` with a metre-correct repeat
  (`RaidNavBake.cs:249-250`, layer chosen in `GroundLayerFor`, `RaidNavBake.cs:304-318`).

**Decorative floor tiles — `Assets/Editor/WallTools/RaidBaseDresser.cs`.**
- `TileApproachRoad` (`RaidBaseDresser.cs:669`) and `TileCourtyardRing` (`RaidBaseDresser.cs:690`),
  both called at `RaidBaseDresser.cs:180-181`.
- The courtyard tile disk extent is **`outer = ctx.Radius - 1.1f`** (`RaidBaseDresser.cs:697`) with a
  circular cull `if (x*x + z*z > outerSq) continue;` (`RaidBaseDresser.cs:709`). **This is the textured
  area, and it is the number that stops short.**

**Wall / boundary-ring positions — `Assets/Editor/WallTools/RaidBaseGenerator.cs`.**
- `MapHalfExtent = 70f` (`RaidBaseGenerator.cs:92`), documented as the mirror of
  `RaidNavBake.GroundScale` (`RaidBaseGenerator.cs:88-90`).
- `ArenaBoundaryEdgeTolerance = 1.2f` (`:105`), `StagingPlaneEdgeMargin = 4f` (`:313`),
  `ArenaBoundaryBandHalf = (4 + 1.2) * 0.5` (`:127-128`),
  **`ArenaBoundaryHalfExtent = MapHalfExtent + (1.2 - 4) * 0.5` = 68.6 m** (`:131-132`).
- The ring is placed by `ArenaBoundaryRing.PlaceSquarePerimeter(..., ArenaBoundaryHalfExtent,
  ArenaBoundaryBandHalf, ...)` (`RaidBaseGenerator.cs:1450-1451`) — a SQUARE ring, not a circle
  (`ArenaBoundaryRing.cs:33-36`).
- Inner arena wall radius: `radius = def.baseRadius` when > 1, else `MapHalfExtent * sqrt(tier.Footprint)`
  (`RaidBaseGenerator.cs:490-491`), clamped to `MapHalfExtent * 0.9` = 63 m (`:492-499`). Tier
  footprints 0.20 / 0.50 / 0.60 (`RaidBaseGenerator.cs:356-359`).

### 3. Why the floor stops short — proven from the numbers

Authored radii, read at source: `Assets/Resources/Data/Canonical/scene-configs.json:49` = 30,
`:74` = 31, `:150` = 49, `:238` = 54, `:312` = 54.

So the **textured** surface (the dresser's tile disk, `radius - 1.1`) ends at **28.9 m / 29.9 m /
47.9 m / 52.9 m** from centre, while the enclosing ring line sits at **±68.6 m**
(`RaidBaseGenerator.cs:131`; `ctx.Radius` IS the clamped `def.baseRadius` — `RaidBaseGenerator.cs:563-565`
passes `Radius = radius` into `RaidBaseDresser.Dress`). That leaves an **untextured annulus of roughly 16 m (Iron Bastion / mage
enclave) to 40 m (raider camp) radially**, out to the walls. Inside that annulus the only surface is
the `RaidGround` plane, which at HEAD is a flat `GroundColorFor` colour with no `_BaseMap`
(`git show HEAD:Assets/Editor/RaidNavBake.cs`, `EnsureGround` + `GroundColorFor`). **That is the
ticket, exactly: it is not that the mesh is missing, it is that the textured region is the dresser's
small disk and everything out to the walls is flat colour.**

One secondary contributor, also proven:
- **The plane edge is not a hard cover of the ring.** HEAD's plane is ±70 m while a ring piece may hang
  `ArenaBoundaryEdgeTolerance = 1.2 m` PAST the plane edge by design (`RaidBaseGenerator.cs:102-106`),
  so the ring's outer half can sit over nothing — a genuine "floor does not reach the wall" at the
  metre scale, on top of the texture problem above.

⚠ **NOT a cause — a tree-side design decision, recorded so the next seat does not mis-read it.** Both
tile functions now early-return on the dirt token — `if (token == "floor_dirt_large") return;`
(`RaidBaseDresser.cs:673` and `:692`), whose own comment (`RaidBaseDresser.cs:670-672`) says the
atlas-flat dirt tile would HIDE the new continuous ground texture. At HEAD there is no such guard in
either function: `git show HEAD:Assets/Editor/WallTools/RaidBaseDresser.cs | grep -n floor_dirt_large`
returns exactly ONE line, `:336`, inside `DefaultFloor`. So the raider camp
(`"floor": "floor_dirt_large"`, `scene-configs.json:91`) HAD tiles at HEAD and now draws its whole
surface from the ground plane; the other two configs author `floor_tile_large` (`:171`, `:255`). This
is part of the fix, not part of the defect.

### 4. State of the fix already in the tree (this is the important finding)

`git diff --stat HEAD` on the four raid files: `ArenaBoundaryRing.cs +84`, `RaidNavBake.cs +269`,
`RaidBaseDresser.cs +262`, `RaidBaseGenerator.cs +70`. `RaidNavBake.cs:184-187` carries an explicit
`// WO-1703:` comment. The scenes have already been re-baked in this tree: `git status --short
Assets/Scenes/` shows all four `RaidBase_*.unity` plus their `NavMesh.asset` modified, and
`Assets/Generated/RaidGround/` holds per-scene `.mat` + `_KeepPlatform` / `_KeepRamp` materials.

Measured proof the refit took effect, from the saved scene YAML:
`Assets/Scenes/RaidBase_raider_camp_small.unity:5783-5784` -> `m_LocalScale {x: 14.328936, z: 14.301155}`
at position `{0.043, 0, 0.066}` = a **143.3 x 143.0 m plane (±71.6 m)**, versus HEAD's
`m_LocalScale {x: 14, y: 1, z: 14}` at the origin (`git show
HEAD:Assets/Scenes/RaidBase_raider_camp_small.unity`, RaidGround transform). ±71.6 m now encloses the
±68.6 m ring line plus its outward reach.

**UNPROVEN — what is NOT shown by the repo:**
- That the textured result looks right to a player. No fresh raid capture PNG was read this session.
  Closing measurement: a rendered raid view per arena (the ticket's own "rendered views" clause), plus
  the `[RaidNavBake] refitted RaidGround scene=... bounds=... boundaryParts=N texture=... repeats=...`
  line emitted at `RaidNavBake.cs:268-269` on a FRESH bake log — `boundaryParts=0` there means the ring
  was not found and the legacy floor was retained (warning at `RaidNavBake.cs:267`).
- That the tree compiles / the suites pass. A Unity gate was running on this tree from another process
  during this lane; no marker was read. Closing measurement: `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>`
  on a fresh log.
- Whether the other 576 uncommitted files are lane-disjoint from these four. Not audited here.

### 5. Minimal file list for an implementation lane

If the tree's work is kept (recommended — it is the ticket, already built):
1. `Assets/Editor/RaidNavBake.cs` — the floor extent + texture authority (`EnsureGround`, `:188`).
2. `Assets/Editor/WallTools/RaidBaseDresser.cs` — the tile-disk early-returns (`:673`, `:692`).
3. `Assets/Editor/WallTools/RaidBaseGenerator.cs` + `Assets/Editor/ArenaBoundaryRing.cs` — only if the
   band constants move; `ArenaBoundaryHalfExtent` (`RaidBaseGenerator.cs:131`) stays the wall authority
   the floor is derived FROM, per the ticket's "derive extent from the actual arena/wall authority".
   ⚠ **Flag for the lane, no fix proposed here:** `RaidNavBake.GroundScale = 14f` (`RaidNavBake.cs:43`)
   and `RaidBaseGenerator.MapHalfExtent = 70f` (`:92`) are HAND-MIRRORED constants —
   `RaidBaseGenerator.cs:88-90` literally says "If RaidNavBake.GroundScale changes, change this" — and
   the tree still seeds the floor footprint MINIMUM from `GroundScale` (`RaidNavBake.cs:192`). That is
   the duplicated-state pattern CLAUDE.md §2/§5/§8 forbids, and this ticket's own "not a second
   hand-authored size" clause points straight at it.
4. Re-bake outputs (generated, not hand-edited): `Assets/Scenes/RaidBase_*.unity` + `NavMesh.asset` via
   `DeNelle.Editor.RaidNavBake.BakeAll` (`RaidNavBake.cs:19`, `:47-48`), and `Assets/Generated/RaidGround/*.mat`.
   ⛔ Never hand-edit the scene YAML (CLAUDE.md §3).

**Regression coverage for raid base geometry today:**
- **`Assets/Editor/Regression/RaidArenaShapeRegression.cs`** — marker `RAID_ARENA_SHAPE_OK` (`:2`,
  `:113`); it pins the authored `baseRadius` as a fraction of `MapHalfExtent` (`:232-238`), pins that
  the plane is big enough for the biggest arena plus the entry apron (`:465-477`), and pins the symbol
  `ArenaBoundaryHalfExtent` as "the ring sits at the PLANE edge" (`:598`). **This is the suite that will
  notice if the ring/plane relationship moves.**
- Also present: `Assets/Editor/Regression/RaidBaseLayoutRegression.cs`,
  `Assets/Editor/Regression/RaidStagingMarkerRegression.cs`,
  `Assets/Editor/WallTools/RaidWallContinuityRegression.cs`,
  `Assets/Editor/WallTools/RaidKeepRampRegression.cs`.
- **New and UNTRACKED in this tree:** `Assets/Editor/Regression/RaidGroundCoverageRegression.cs`
  (marker `RAID_GROUND_COVERAGE_OK`, `:19`) — it reflects into the real `RaidNavBake.EnsureGround`
  (`:33-34`) on a fixture scene and asserts floor-covers-rotated-wall-bounds (`:83`), unrelated props do
  NOT inflate the floor (`:84`), collider matches renderer (`:86-87`), the texture is the owned terrain
  layer repeating in metres rather than stretched (`:88-98`), and a re-bake is stable (`:100-103`).
  `Assets/Editor/RaidGroundSavedSceneRegression.cs` (marker `RAID_GROUND_SAVED_OK`, `:75`) is its
  saved-scene twin, asserting exactly one `RaidGround` per scene (`:82-83`).
  ⚠ **Neither is registered in `DataRegression.cs`:**
  `grep -n "RaidGroundSaved\|RaidGroundCoverage\|regression-registry" Assets/Editor/Regression/DataRegression.cs`
  returned NOTHING this session; a repo-wide search for `RaidGroundCoverage` returns only those two
  files, a comment at `RaidWallContinuityRegression.cs:36`, and the 2026-09-13 handoff path lists. Both
  new files are marked `// regression-registry: standalone` (`RaidGroundCoverageRegression.cs:1`) and
  expose `RunStandalone()` (`:17`) — i.e. they are authored as separate batchmode entry points with
  their own markers, not as rows inside `REGRESSION_OK <n>/<n>`.
  ✅ **CORRECTED — both questions are now answered, see the Verification section §(c)/§(d1).** A runner
  DOES invoke them (`Builds/night-remaining-focused.ps1:6-7`) and `RAID_GROUND_COVERAGE_OK` IS captured
  (`Builds/night-raid-ground.log`). The `regression-registry: standalone` token is an explicit opt-out
  recognised by the marker suite (`Assets/Editor/Regression/RegressionMarkerRegression.cs:182`), so
  leaving them out of `DataRegression.cs` is sanctioned, not an omission.
  (The 2026-09-13 handoff already scoped exactly these two files for this WO:
  `docs/handoffs/checkpoint-inventory-2026-09-13/ready-raid-floors-1703.paths.txt`.)

### 6. Owner rulings that constrain the fix

- **Owner ruling 2026-09-10 (morning), memory `owner-rulings-2026-09-10-morning`:** *"exterior walls
  around entire arena"* + *"similar strategy as we used in battle arena"* — the raid arena's boundary
  ring uses the battle arena's vocabulary (ProceduralSiegeArenaBuilder / ArenaPrefabBuilder EdgeProps)
  **plus courtyard cover rings**, because the arena *"feels incomplete and not polished"*. The ring is
  therefore approved art: the floor must be derived from it, and **the ring must not be moved or
  replaced to make the floor fit** (the ticket says the same: "do not replace approved walls or props").
- **This ticket's own constraints:** derive extent from the arena/wall authority, not a second
  hand-authored size; reuse existing owned ground materials; modify builders, never scene YAML.
- **CLAUDE.md §3:** never hand-edit `.unity`; bakes go in a work order and are not fired by UI.
- **CLAUDE.md §12 / §11B:** the acceptance evidence is a fresh bake log line + rendered views, not a
  reading of the builder source.
- UNPROVEN: no owner ruling was found this session that fixes WHICH texture each arena gets. The tree
  chooses it in `RaidNavBake.GroundLayerFor` (`:301-318`): `floor_tile_large` -> `Stoneback_Rock`,
  everything else -> `Path_Dirt`. Closing action: an owner Pass on a rendered view per arena (she is
  colourblind — judge by texture legibility, not hue).

## Verification of Codex RESULT 2026-09-14 (read-only lane)

Verifying `WorkOrders/WORK_ORDER_1703_raid_textured_floor_reaches_walls.RESULT.md` (untracked, mtime
2026-09-13 23:20) against the tree, the `Builds/` logs and the proof PNGs. Same read-only rules; no
edit outside this markdown, no Unity, no git state change. **Verdict: the RESULT's marker and image
claims hold. Two things it asserts cannot be proven from here, and one shipping hazard it does not
name.**

### (a) Markers on the named logs — ALL PRESENT, read this session

Read with `Select-String` (logs are Unity-written; judged by the marker LINE, not an exit code).

| Log (under `Builds/`) | mtime | Marker line read this session |
|---|---|---|
| `night-raid-height-regenerate.log` | 2026-09-13 22:09:29 | no marker of its own; `[RaidBaseGenerator] baked 3 raid scene(s) from scene-configs.json` + per-scene `BUILT:` lines (mage_enclave `radius 54.0m`, `ARENA BOUNDARY +/-68.6m: 396 landscape piece(s)`) |
| `night-raid-height-nav.log` | 2026-09-13 22:10:21 | `RAID_NAV_BAKE_OK scenes=5; wall and tower footprints use runtime carving` |
| `night-raid-height-ground-saved.log` | 2026-09-13 22:10:52 | `RAID_GROUND_SAVED_OK 4/4 scenes - persisted textured ground, wall/collision coverage, loaded navigation; IronBastion local-nav limitation logged` |
| `night-raid-height-polish.log` | 2026-09-13 22:11:29 | `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=33/33 output=D:\EoA\Builds\raid-polish-saved-proof\20260914-031117-134` |
| `night-raid-clear-ground.log` | 2026-09-13 23:04:35 | 3x `[RaidPolishSavedProof] CLEAR_GROUND_RENDER_PRISM ... testedRendererBounds=661 / 1064 / 1158 overlaps=0 physicsSamples=9`, then `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=33/33 output=...\20260914-040422-645` |

**Plus an EARLIER focused run the RESULT does not cite, found via `Builds/night-remaining-focused.ps1`:**
`Builds/night-raid-ground.log` (21:54:17) `RAID_GROUND_COVERAGE_OK - rotated/offcenter walls covered;
decoration excluded; matching collider; terrain texture repeats in metres; rebake stable`;
`Builds/night-raid-ground-saved.log` (21:54:51) `RAID_GROUND_SAVED_OK 4/4`;
`Builds/night-raid-polish-saved.log` (21:55:29) `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=30/30`.
That first marker is the **strongest single piece of evidence for this ticket** — it is the oracle that
drives `EnsureGround` itself — and it is missing from the RESULT's evidence list.

The `20260914-*` folder names are UTC; the log mtimes are local 2026-09-13 evening. That matches the
RESULT's own "evidence timestamps cross into 2026-09-14 UTC" note — not a discrepancy.
⚠ `night-raid-height-regenerate.log` also contains Unity `Lifecycle ERROR ... NullReferenceException`
noise around domain reload; the generator lines after it are complete, so the run finished, but this
lane did not establish that the exception is benign. **UNPROVEN:** whether that NRE matters.

### (b) Proof PNGs — folder exists, 33 images + `manifest.txt`, four opened

`Builds/raid-polish-saved-proof/20260914-040422-645/` holds **34 entries = 33 `*_color.png` + one
`manifest.txt`**, 11 per arena (raider_camp_small / fortified_garrison / mage_enclave). Opened:

- `raider_camp_small_overview_color.png` — top-down. **The textured orange dirt fills the entire square
  right out to and under the grey boundary rock ring on all four sides; no untextured band, no colour
  plate, no hole.** This is the ticket's requirement, rendered. The inner wall rectangle and centre
  spire sit on the same continuous surface.
- `mage_enclave_overview_color.png` — same story with the authored variation: the outer field is
  textured dirt out to the ring, the keep interior is the grey hex paving, the spire court returns to
  dirt. Paved-to-soil transitions are clean; no flat-colour region anywhere.
- `fortified_garrison_boundary_north_oblique_color.png` — ground-level oblique at the wall. **The dirt
  texture runs right up to the base of the stone pillars with no gap and no seam of untextured
  plane** — the "reaches the walls" half, at eye level. Continuous backing between pillars is visible.
- `raider_camp_small_textured_ground_color.png` — the flat ground camera. Repeating dirt with faint
  tile seams visible under close inspection, exactly as the RESULT admits; not a stretched swatch.

`manifest.txt` is the NAV manifest (`AGENT actualSceneBake ... navData=... sha256=`, `SAMPLE`, `PATH`,
`APERTURE_CROSS` lines), not an image manifest — worth knowing before someone greps it for hashes.

### (c) Entry points exist; NONE is registered in `DataRegression.cs`

- `Assets/Editor/WallTools/RaidPolishSavedProof.cs:46` `public static void Run()`; emits
  `RAID_POLISH_SAVED_PROOF_OK` at `:166`. **Untracked (`??`).**
- `Assets/Editor/RaidGroundSavedSceneRegression.cs:25` `public static void RunStandalone()`; emits
  `RAID_GROUND_SAVED_OK` at `:75`. **Untracked.**
- `Assets/Editor/Regression/RaidGroundCoverageRegression.cs:17` `public static void RunStandalone()`;
  emits `RAID_GROUND_COVERAGE_OK` at `:19`. **Untracked.**
- `Assets/Editor/RaidNavBake.cs:48` `BakeAll()`; emits `RAID_NAV_BAKE_OK` at `:96`.

**Registration: a grep of `Assets/Editor/Regression/DataRegression.cs` for
`RaidGroundSaved|RaidPolishSavedProof|RaidGroundCoverage|RAID_GROUND|RAID_POLISH` returned NO MATCHES
this session.** All three new oracles are `// regression-registry: standalone` batchmode entry points
with their own markers; none contributes a row to `REGRESSION_OK <n>/<n>`.

**That is SANCTIONED, not an omission — proven twice:**
1. `Assets/Editor/Regression/RegressionMarkerRegression.cs:182` lists the literal string
   `"regression-registry: standalone"` as the **explicit opt-out token for new files**. So the
   "an oracle written and never registered is a FAIL by design" rule (stated in-code at
   `DataRegression.cs:1097-1099`) does NOT fire on these three. The check-in gate will not fail on them.
2. A runner exists and is not hypothetical: **`Builds/night-remaining-focused.ps1:5-10`** drives six
   gates through `run-unity-method.ps1`, each with its own `-ExpectMarker`, including
   `DeNelle.Editor.Regression.RaidGroundCoverageRegression.RunStandalone` -> `RAID_GROUND_COVERAGE_OK`
   (`:6`), `DeNelle.Editor.RaidGroundSavedSceneRegression.RunStandalone` -> `RAID_GROUND_SAVED_OK`
   (`:7`) and `DeNelle.Editor.RaidPolishSavedProof.Run` -> `RAID_POLISH_SAVED_PROOF_OK` (`:8`).

⚠ **But that runner is a throwaway.** `Builds/` is gitignored (`git check-ignore -v` ->
`.gitignore:8:/[Bb]uilds/`) and `git ls-files` does not know
`Builds/night-remaining-focused.ps1`. **So the only thing that invokes these three oracles will not
survive this machine.** If they are meant to keep protecting raid ground geometry, the invocation
belongs somewhere tracked (`tools/regression/`), or the oracles belong in `DataRegression.cs`. **Lead
decision — this is the one durable-coverage call on the ticket.**

### (d) RESULT claims this lane could NOT prove

1. ~~`RAID_GROUND_COVERAGE_OK` has no captured run.~~ **WITHDRAWN — I was wrong, and the error is worth
   recording.** I searched `logs/` only and concluded "nowhere in this repo"; the raid evidence all
   lives under `Builds/`. The marker IS captured: **`Builds/night-raid-ground.log`** (mtime 2026-09-13
   21:54:17) carries `RAID_GROUND_COVERAGE_OK - rotated/offcenter walls covered; decoration excluded;
   matching collider; terrain texture repeats in metres; rebake stable`. The earlier sweep of that same
   focused run also logged `RAID_GROUND_SAVED_OK 4/4` (`Builds/night-raid-ground-saved.log`, 21:54:51)
   and `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=30/30` (`Builds/night-raid-polish-saved.log`,
   21:55:29 — 30 images, before the WO-1704 height rebuild took it to 33). *Lesson for the next lane:
   search BOTH `logs/` and `Builds/`; a "nowhere" claim from one directory is a guess wearing a grep.*
   The one thing that remains true: the RESULT never mentions `RAID_GROUND_COVERAGE_OK`, so the
   strongest oracle for this exact ticket is absent from its own evidence list.
2. **"all 26 generated RaidGround files match current source" / delta manifest SHA256
   `4af6a08f…6587d` / "124160-file ignored-input source-drift scan".** No such manifest file was
   located from the RESULT's own text; the claim names no path. UNPROVEN. Closing measurement: the
   RESULT should cite the manifest path, or the scan should be re-run.
3. **"Root inspected all 33 images" / "the seventeen changed non-ground images were also opened".**
   Unverifiable by nature. `Builds/night-raid-clear-visual-review.md` (mtime 2026-09-13 23:08) does
   record the review, with measured deltas (max changed-pixel fraction 0.322%, max mean |ΔRGB|
   0.02602/255) and an honest admission that the cause of the raster differences "is not established".
   This lane independently opened 4 of the 33 and agrees with its conclusion.
4. **Compile + full regression.** No `COMPILE_GATE_OK` / `REGRESSION_OK <n>/<n>` was read this session;
   the RESULT itself says these remain pending. A Unity gate was running on this tree from another
   process during this lane.

### ⛔ Shipping hazard the RESULT does not name

**`Assets/Generated/RaidGround/` is GITIGNORED.** `git check-ignore -v` returns
`.gitignore:398:Assets/Generated/*`. The folder holds **26 files** and `git ls-files` returns **0**. So
the four re-baked `RaidBase_*.unity` scenes will be committed referencing ground/keep materials that
**do not travel with the repo** — a fresh clone or a CI build bakes them from `RaidNavBake` or renders
them missing. The RESULT's talk of "detached build inputs" and "ignored `Assets/Generated/RaidGround/
RaidBase_<id>.mat`" implies Codex knew and routed around it, but this lane could not find or verify
that provisioning path. **UNPROVEN and lead-blocking:** either the bake is re-run on every machine
before a build, or the `.gitignore` rule needs an exception for this folder. Decide before the commit.

### File list to commit for this ticket

Modified (tracked):
- `Assets/Editor/RaidNavBake.cs`
- `Assets/Editor/WallTools/RaidBaseDresser.cs`
- `Assets/Editor/WallTools/RaidBaseGenerator.cs`
- `Assets/Editor/ArenaBoundaryRing.cs`
- `Assets/Scenes/RaidBase_IronBastion.unity` + `Assets/Scenes/RaidBase_IronBastion/NavMesh.asset`
- `Assets/Scenes/RaidBase_fortified_garrison.unity` + `.../RaidBase_fortified_garrison/NavMesh.asset`
- `Assets/Scenes/RaidBase_mage_enclave.unity` + `.../RaidBase_mage_enclave/NavMesh.asset`
- `Assets/Scenes/RaidBase_raider_camp_small.unity` + `.../RaidBase_raider_camp_small/NavMesh.asset`

New (untracked — each with its `.cs.meta`, all four `.meta` confirmed present on disk):
- `Assets/Editor/RaidGroundSavedSceneRegression.cs` (+ `.meta`)
- `Assets/Editor/Regression/RaidGroundCoverageRegression.cs` (+ `.meta`)
- `Assets/Editor/WallTools/RaidPolishSavedProof.cs` (+ `.meta`)
- `Assets/Editor/WallTools/RaidWallContinuityRegression.cs` (+ `.meta`)
- `Assets/Editor/WallTools/RaidKeepRampRegression.cs` (+ `.meta`)
- `WorkOrders/WORK_ORDER_1703_raid_textured_floor_reaches_walls.RESULT.md`
- this file (`WORK_ORDER_1703_...md`, RCA + verification + Status flip)

⚠ Lead calls, NOT this lane's to make:
- `Assets/Scenes/OwnedTown_IronBastion.unity` is **untracked and new**, and `RaidNavBake.cs:38` lists it
  among the baked scenes — it is in the blast radius but may belong to a different lane. Decide whether
  it rides this commit.
- `Assets/Generated/RaidGround/*` (26 files) is ignored — see the hazard above.
- `RaidPolishSavedProof` / `RaidKeepRampRegression` / `RaidWallContinuityRegression` were the
  WO-1704 wall-height lane's tooling; if that ticket commits separately, split them out.
- **The only invoker of the three new raid oracles is `Builds/night-remaining-focused.ps1`, and
  `Builds/` is gitignored (`.gitignore:8`).** Nothing tracked runs them. Either port that gate list
  into `tools/regression/` or register the oracles in `DataRegression.cs` — otherwise this ticket's
  coverage dies with this working tree.

### Recommended Status line (NOT flipped by this lane)

`**Status:** FIXED - implemented 2026-09-13 (Codex), markers RAID_GROUND_COVERAGE_OK / RAID_GROUND_SAVED_OK 4/4 / RAID_NAV_BAKE_OK scenes=5 / RAID_POLISH_SAVED_PROOF_OK 3/3 images=33/33 / CLEAR_GROUND_RENDER_PRISM overlaps=0 verified on fresh Builds/ logs + 4 proof PNGs opened by the lead lane 2026-09-14; PENDING lead COMPILE_GATE_OK + REGRESSION_OK, a ruling on the gitignored Assets/Generated/RaidGround materials and on the gitignored-only oracle invoker, and owner test-build acceptance.`
