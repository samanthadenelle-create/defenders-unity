# WO-1704 - continuous raid walls and connected inner gate assemblies

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:18:58, build 2026.09.17.373943). PRIOR STATUS: FIXED PENDING OWNER TEST BUILD - implemented + measured RED->GREEN (Codex 2026-09-13); lead re-ran RAID_WALL_CONTINUITY_OK on the current tree 2026-09-14 04:08 via the new tracked runner tools/regression/raid_suites_gate.ps1 + REGRESSION_OK 04:17; PO felt-verifies and closes
**Minted:** 2026-09-10, direct owner phone playtest; main-line banner 1704 -> 1705.
**Silo:** Raid geometry; separate from WO-1703 floor/material work.

## Owner finding and purpose

"holes in the exterior wall, the inner keep isnt connected to the gate"

"i want the raids to feel really polished as i feel its the way to get to arena competitions"

Primary evidence: `Builds/device-frames/owner-20260910/Screenshot_20260910-202900.png`,
pulled from the connected phone and opened by root and RCA. Visible exterior sections
have gaps. The keep connection is owner-observed; this frame does not expose its full plan.

## RCA handoff (read-only agent -> root CLI)

`Assets/Editor/WallTools/RaidBaseDresser.cs:518` fits wall pieces to `step * 0.98f`,
deliberately leaving seams. At :153-159 it clads an inner north-gated ring but only
places gatehouses on the outer ring. `PlaceGatehouse` places gate/flanks without a
measured adjacency postcondition. Root re-read these methods. Exact gate side-gap
width remains unmeasured; do not report a predicted width as captured fact.

## Bounded correction and proof

Use actual ring and gate geometry to join wall segments and gate assemblies with
existing approved modules. Place the missing inner north gate assembly. Preserve
the intended crossing between opposite outer/inner gates; do not invent connecting
walls through the kill zone. Maintain the measured usable gate opening, navigation,
colliders, destructible behavior and dressing idempotence. Broken decorative modules
must not substitute for a continuous enclosing wall where the owner expects closure.

First capture a regression failure against current placement. Verify measured
segment/gate adjacency and inner assembly presence, bake representative raid types,
check navigable gate-to-keep paths, and inspect four-side and gate screenshots.
Record delivered work as Fixed pending owner's test build, never self-close it.

Fence: RaidBaseDresser and meaningful geometry regression; RaidBaseGenerator layout
report only if needed to share the actual ring authority. Root alone runs Unity,
regenerates assets and commits. Preserve owner-closed WO-1632/1633/1634/1635.

## RCA 2026-09-14 (read-only lane)

Read-only lane. No `.cs`/`.unity`/asset edited, no Unity/gate/build/git-write run. Every
claim below is a file:line opened this session or a log line read this session.

### 1. Symptom, one line

A raid base does not read as an enclosed place: the arena's exterior ring shows sky between
its pieces at body height and above, the clad castle ring carries per-panel seams, and the
inner keep ring is clad with a north opening that no gate assembly ever stood in — while the
opening has to stay wide enough for NavMeshAgents to use.

**Which ring the owner's frame actually shows.** I opened
`Builds/device-frames/owner-20260910/Screenshot_20260910-202900.png`. It is a raid HUD frame
(SPIRE 100%, 3:00, DEPLOY ALL) looking W/NW along a line of free-standing grey rock **pillars
with open sky between them**, receding to the horizon — that is the **`ArenaBoundary_Ring`
landscape ring (WO-1632)**, not the clad castle wall and not the structural `Wall_` ring. No
castle wall or keep is in frame, which matches the ticket's own line 15. So "holes in the
exterior wall" is an **ArenaBoundary_Ring** finding; the clad-ring seams and the inner gate are
separate defects proven from source and logs below, not from this frame.

### 2. Where the geometry is authored, and what the bake consumes

| Thing | Authority (working tree unless noted) |
|---|---|
| **Structural wall ring** (colliders, `WallSegment`, destructible) | `RaidBaseGenerator.BuildRing` `Assets/Editor/WallTools/RaidBaseGenerator.cs:1353-1416`. Segments named `{ringName}_S{side}_{i}`; the gate is a **skipped run of panels** (`:1397`), width `gateSpan * segW` widened to `RaidBaseDresser.MinGateWidth` by the `while` at `:1383-1387`. |
| Outer ring call | `RaidBaseGenerator.cs:508` |
| Inner keep ring call | `RaidBaseGenerator.cs:520-528`; `innerGates = {false,false,true,false}` at `:520` fixes the inner gate to **side 2 (N)**; its own `keep.GateWidth` is now handed to the dresser via `RingLayout` at `:523-528` / `:569`. |
| **Cosmetic clad ring** | `RaidBaseDresser.CladRing` `Assets/Editor/WallTools/RaidBaseDresser.cs:499-559`. Pieces `Clad_*`, instantiated with **`stripColliders: true`** (`:540-541`) — cladding is **never** navigation, only what the player sees. |
| **Gate assembly** | `RaidBaseDresser.PlaceGatehouse` `:561-620`. The gate keeps colliders (`stripColliders: false`, `:571`); `OpenGateAssembly` (`:575`, body at `:1434`) cuts the aperture; flanks are stripped (`:606-607`) and offset from the flank's **measured** span (`:601`). |
| Call sites | outer `:161`, `:176`, `:178`; **inner ring + inner gatehouse `:161-171`** (new). |
| Exterior arena boundary | `RaidBaseGenerator.BuildArenaBoundary` `:1451`, half-extent const `RaidBaseGenerator.cs:131`; placement in `Assets/Editor/ArenaBoundaryRing.cs` (stride/overlap `:289-300`), body-height backing `PlaceSquareBacking` `:373-420` (new). |
| **Nav consumer** | `Assets/Editor/RaidNavBake.cs`. `EnsureGround` (`:188`) drops the walkable plane; `BakeAll:61-80` marks every non-destructible renderer **NavigationStatic** and bakes (`NavMeshBuilder.ClearAllNavMeshes/BuildNavMesh`, `:79-80`); `PrepareDestructibleWalls:157-185` **strips** NavigationStatic from `WallSegment` children and gives each one a **carving `NavMeshObstacle` sized from its BoxCollider** (`:171-180`). |

**Nav consequence a lane must not miss:** because `WallSegment`s carve at *runtime* rather than
bake, the **baked** NavMesh contains no wall blocking at all. An editor-batchmode
`NavMesh.CalculatePath` from outside the gate to the spire would pass trivially and prove
nothing about the aperture. The **gatehouse**, by contrast, is not a `WallSegment`, keeps its
colliders and **is** NavigationStatic, so it does bake in — which is why `PlaceGatehouse`'s own
comment at `:596-599` treats a mis-sized flank as capable of sealing the base.

### 3. Proven cause

Four independent defects, all read at `git show HEAD:Assets/Editor/WallTools/RaidBaseDresser.cs`
this session, and all three that the regression can see are confirmed by a captured RED log:

1. **Clad seams by construction.** HEAD `:518` `FitPieceAlong(go, step * 0.98f, piece)` — every
   panel is fitted 2% narrower than its slot. Captured: `Builds/ready-iter3-1704-red.log:839`
   `raider_camp_small clad x=-31.00 panels=16 actualBoundsGap=0.0775` (7.75 cm of daylight per
   join, 16 panels per side).
2. **Cladding skipped by cell CENTRE, not by the real gate edge.** HEAD `:514`
   `if (gated && Mathf.Abs(t) < gateWidth * 0.5f) continue;` — a panel whose centre clears the
   half-width still reaches up to `step/2` into the mouth. Captured
   (`ready-iter3-1704-red.log:848-850`): an `8.553 m` structural `colliderCut` presenting only
   `visibleClearRun=1.90 m` — **below `MinGateWidth = 3.5f`** (`RaidBaseDresser.cs:28`). After the
   tree's fix the same site reads `visibleClearRun=4.30 m`
   (`Builds/night-raid-wall-height-after.log:1361-1366`).
3. **No inner gate assembly existed at all.** HEAD `:155` clads the inner ring with
   `northGate: true`, but the only two `PlaceGatehouse` calls (HEAD `:157`, `:159`) are both at
   `±ctx.Radius`, i.e. the **outer** ring. The inner north opening was a hole with no gate in it.
   Captured RED: `ready-iter3-1704-red.log` counts `inner gate assemblies=0`; GREEN
   `night-raid-wall-height-after.log:1408`/`:1456` read
   `fortified_garrison inner gate assemblies=1 actual authored inner layers=1` and the same for
   `mage_enclave`.
4. **The inner gate width was a guess, not the ring's authority.** HEAD `:155` passed
   `Mathf.Max(MinGateWidth, ctx.GateWidth * 0.85f)` — a fraction of the **outer** ring's gate —
   while the inner ring's true `gateWidth` was computed by `BuildRing` at HEAD
   `RaidBaseGenerator.cs:504` from its own `segW` and thrown away (HEAD `LayoutContext` carried
   only `Innermost`, no `InnerRings`). So the clad opening and the structural opening were
   authored from two different numbers.
5. **Exterior ring (the owner's frame).** `ArenaBoundaryRing` closed the ring by **footprint
   overlap in XZ** (`WorstGap` negative, `:289-300`) which says nothing about solidity through
   the wall **body**. Captured: `Builds/night-raid-wall-height-baseline.log`
   `RAID_WALL_CONTINUITY_FAIL 36` — probes at 50/75/90% of the measured skyline exposed upper
   openings on all three configs, while ankle/head probes passed.

**A candidate fix for all five already sits UNCOMMITTED in the working tree** (Codex, 09-10 →
09-13; `RaidBaseDresser.cs` mtime 09-11 09:04, `ArenaBoundaryRing.cs` 09-13 22:07,
`RaidWallContinuityRegression.cs` 09-13 22:04, all `??`/` M` in `git status`). Partition at real
gate edges + 2 cm internal lap (`RaidBaseDresser.cs:530-545`), `wall_broken → wall` for enclosing
rings (`:502-504`), inner gatehouse per `RingLayout` (`:161-171`), and a 6.692 m body-height
`BoundaryBacking` at 95% of the measured 7.044 m skyline (`ArenaBoundaryRing.cs:383`).

⚠ **The ticket's `**Status:**` line is stale.** `WorkOrders/WORK_ORDER_1704_raid_wall_and_inner_gate_continuity.RESULT.md`
exists, dated 2026-09-13, and the markers it cites are real on fresh logs I read:
`RAID_WALL_CONTINUITY_OK` (`night-raid-wall-height-after.log:1351`),
`RAID_NAV_BAKE_OK scenes=5`, `RAID_GROUND_SAVED_OK 4/4`,
`RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=33/33` (`night-raid-clear-ground.log`). The
ticket's "first capture a regression failure" criterion is **met**: RED
`RAID_WALL_CONTINUITY_FAIL 68` (`ready-iter3-1704-red.log:838`) → partial
`FAIL 6` (`ready-iter3-1704-green.log:1262`) → `OK` (`ready-iter3-1704-green2.log:1247`), then the
height-probe pass RED 36 → OK. The lane owed the board a flip to Fixed; per §11 hand-back rules
that belongs to the owning lane, and I am read-only, so it is surfaced here instead.

**UNPROVEN, and exactly what closes each:**
- That the GREEN logs describe the **current** tree. `night-raid-wall-height-after.log` is
  2026-09-13 22:08 and `ArenaBoundaryRing.cs` is 22:07, so it is plausible but not certain that
  nothing changed after. **Proof:** one re-run of
  `DeNelle.Editor.RaidWallContinuityRegression.RunHeadless` on the current tree, judged by
  `RAID_WALL_CONTINUITY_OK` on a fresh log.
- That an agent can actually **walk** gate→keep→spire. Nothing measured a path with carving
  active (see §2). **Proof:** a play-mode capture, or the geometric assertion in §4.
- The in-code claims that `wall_broken` has body-level holes (`RaidBaseDresser.cs:502-504`) and
  that footprint overlap never proved body solidity (`ArenaBoundaryRing.cs:351`) are **comments**;
  the measurements that back them are the `actualTriangleUncoveredSamples` /
  `uncoveredSamples` lines above, which I read — the comments themselves are not evidence.

### 4. What pins this today, and what a measured regression must assert

**Existing suites under `Assets/Editor/Regression/` do NOT pin raid wall/gate/nav geometry:**
- `RaidBaseLayoutRegression.cs:269-279` checks gate width by **source-text search**
  (`dress.IndexOf("MinGateWidth = 3.5f")`) — it would pass over every defect in §3.
- `RaidArenaShapeRegression.cs:421-450` only regex-scans the saved scene for a non-empty
  `m_NavMeshData` — presence, never connectivity or aperture.
- `RaidWallMaterialRegression.cs` is materials (WO-1703 lane).
- `Assets/Editor/RaidGroundSavedSceneRegression.cs:182` proves a complete path **hero → staging**,
  and both markers are authored **outside** the walls (`RaidBaseGenerator.cs:549`, `:554`) — it
  never crosses a gate.

**The measured suite for this ticket is `Assets/Editor/WallTools/RaidWallContinuityRegression.cs`
(untracked).** It builds real geometry by reflecting `RaidBaseGenerator.BuildConfigLayout`
(private static, confirmed at `RaidBaseGenerator.cs:483`) on throwaway roots for
`raider_camp_small` / `fortified_garrison` / `mage_enclave` (`:62`) — Easy/Hard/Extreme, correctly
excluding IronBastion per the 09-09 scope — and asserts, from mesh-probe raycasts:
clad bounds gap ≤ 1 cm plus zero uncovered triangle samples (`:158-196`); inner gatehouse count ==
authored `interiorWallLayers` (`:199-210`); gate collider cut ≥ `MinGateWidth` with a measured
body-height clear run (`:212-266`); exterior boundary solid at ankle/torso/head **and** 50/75/90%
of the independently measured skyline (`:268-315`). It emits its own marker,
`RAID_WALL_CONTINUITY_OK` / `_FAIL <n>`.

Still owed by that suite, and what any "measured regression" here must add:
- **A geometric nav assertion** (not `CalculatePath`, which is vacuous pre-carve): no
  `NavMeshObstacle` box as `RaidNavBake.cs:171-180` would size it from a `WallSegment` BoxCollider
  intersects either ring's gate cut in XZ, and no NavigationStatic **gatehouse/flank** collider
  intrudes into the opening. Note the current `CheckGatePassages` measures only `Wall_*`
  BoxColliders plus *renderer* probes, so a colliding flank inside the mouth would pass it today.
- **Registration.** `Assets/Editor/WallTools/DeNelle.EditorWallTools.asmdef:4-9` references
  `DeNelle.Editor` one-way, so `DataRegression` **cannot** call this suite and the
  `REGRESSION_OK <n>/<n>` count will never include it. It needs its own batchmode entry point and
  its own marker in the gate chain, judged separately (§8's distinct-marker rule).
- **Re-bake idempotence.** Clad names changed from HEAD `Clad_{s}_{i}` to
  `Clad_{s}_{run}_{i}_R{radius}` (`RaidBaseDresser.cs:541`) and `EnsureZone` (`:368-377`) **reuses**
  an existing `Zone_Clad` without clearing children. Re-dressing is safe only because
  `BuildFromConfig` destroys the prior root first (`RaidBaseGenerator.cs:461`, `:464`); a lane that
  ever calls `Dress` without that destroy would stack two clad generations, and the fixture (fresh
  roots) cannot catch it.

### 5. Minimal file list for an implementation lane

Already carrying the candidate fix — verify, do not re-author:
- `Assets/Editor/WallTools/RaidBaseDresser.cs` (`CladRing` `:499-559`; inner-ring call `:161-171`)
- `Assets/Editor/WallTools/RaidBaseGenerator.cs` (`RingLayout` emit `:523-528`, `:569`)
- `Assets/Editor/ArenaBoundaryRing.cs` (`PlaceSquareBacking` `:373-420`)
- `Assets/Editor/WallTools/RaidWallContinuityRegression.cs` (+ `.meta`) — untracked, needs staging
- `Assets/Editor/RaidNavBake.cs` (bake consumer; only if the nav assertion needs a seam)

Not code, but part of the change: the ticket's own `**Status:**` line, `BOARD.html` via
`python tools/board_build.py`, and the existing `.RESULT.md`. Out of scope: floors/materials
(WO-1703), `RaidBaseLayoutRegression`'s source-token cases (leave; they are a different axis).

### 6. Owner rulings constraining the fix

- `docs/HANDOVER.md:17-20` — WO-1607–1611 raid base design is **implemented in tree and baked
  once; PO felt-test closes**. **Do not retune garrison HP. Village2 and Iron Bastion are out of
  scope** (the suite's three fixture ids at `RaidWallContinuityRegression.cs:62` already honour it).
- This ticket `:41` — preserve owner-closed **WO-1632/1633/1634/1635** (arena boundary, keepout,
  spire, staging). `ArenaBoundaryRing`'s change is vertical fit only; palette, RNG cadence,
  XZ placement, band depth and the staging envelope must stay authored (`:352-354`, `:374-376`).
- This ticket `:29-31` — keep the crossing between opposite outer/inner gates; **do not invent
  connecting walls through the kill zone**; keep the measured usable opening, colliders,
  destructible behaviour and dressing idempotence.
- This ticket `:37` — record as **Fixed pending the owner's test build**; CLI never self-closes
  (§13: PO felt-verifies and closes).
- `CLAUDE.md` §16 — the raid art is served from R2 and a missing push fails silently, so any build
  that reaches the phone for the felt-test goes through `tools\r2-ship.ps1`, judged by
  `R2_PARITY_OK` on a fresh log.

## Verification of Codex RESULT 2026-09-14

Read-only verification of `WORK_ORDER_1704_...RESULT.md` (untracked, 2026-09-13) against the
working tree, the `Builds/` logs and the saved proof images. No edit outside this markdown, no
Unity, no git state change.

### (a) Every cited marker is present, on a log whose mtime I read

| Log (`Builds/`) | mtime | Marker read this session |
|---|---|---|
| `night-raid-wall-height-baseline.log` | 2026-09-13 22:05 | `RAID_WALL_CONTINUITY_FAIL 36` |
| `night-raid-wall-height-after.log` | 2026-09-13 22:08 | `RAID_WALL_CONTINUITY_OK` (line 1351) |
| `night-raid-height-regenerate.log` | 2026-09-13 22:09 | no `RAID_*` marker — **3 × `BUILT`** only (see (e)) |
| `night-raid-height-nav.log` | 2026-09-13 22:10 | `RAID_NAV_BAKE_OK scenes=5; wall and tower footprints use runtime carving` |
| `night-raid-height-ground-saved.log` | 2026-09-13 22:10 | `RAID_GROUND_SAVED_OK 4/4 scenes` |
| `night-raid-height-polish.log` | 2026-09-13 22:11 | `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=33/33` |
| `night-raid-clear-ground.log` | 2026-09-13 23:04 | `RAID_POLISH_SAVED_PROOF_OK scenes=3/3 images=33/33 output=…20260914-040422-645` |

The earlier iteration ladder is also real and in the right order:
`Builds/ready-iter3-1704-red.log:838` `RAID_WALL_CONTINUITY_FAIL 68` (20:54) →
`ready-iter3-1704-green.log:1262` `FAIL 6` (21:00) → `ready-iter3-1704-green2.log:1247` `OK`
(21:07), all 2026-09-10. **The ticket's "first capture a regression failure" criterion is met
twice** (68 → OK on the cladding/gate axis, 36 → OK on the wall-body-height axis).

Measured content, not just the marker: `night-raid-wall-height-after.log:1360/:1408/:1456` read
`inner gate assemblies=0/1/1` against `actual authored inner layers=0/1/1`;
`:1367/:1415/:1463` read `boundary originalSkylineTop=7.044` on all three configs;
`:1361-1366` read `colliderCut=8.553 visibleClearRun=4.30` at y=1.50 (vs `1.90` in the RED log),
clearing `MinGateWidth = 3.5f`.

### (b) The suite exists, probes what it claims — and is HEADLESS-ONLY

`Assets/Editor/WallTools/RaidWallContinuityRegression.cs` (untracked, mtime 2026-09-13 22:04).
The 50/75/90% probe claim is literal — `:292`:
`foreach (float height in new[] { 0.30f, 1.50f, 2.00f, skylineTop * 0.5f, skylineTop * 0.75f, skylineTop * 0.9f })`,
with `skylineTop` measured at `:283-286` from the boundary's own pieces **excluding**
`BoundaryBacking`, so the backing under test cannot inflate the bar it is judged against.
Entry point `RunHeadless` at `:22`; markers `RAID_WALL_CONTINUITY_OK` / `_FAIL <n>` at `:103-104`.

⛔ **It is NOT registered in `Assets/Editor/Regression/DataRegression.cs`** — grep for
`RaidWallContinuity` and `EditorWallTools` in that file returns nothing. It **cannot** be:
`Assets/Editor/WallTools/DeNelle.EditorWallTools.asmdef:4-9` references `DeNelle.Editor`
one-way, so `DeNelle.Editor` cannot see back. It is a **standalone batchmode entry point with its
own marker**, and `REGRESSION_OK <n>/<n>` will never include it. Whoever runs the pre-ship gate
must run and judge it separately.

**⚠ SELF-CORRECTION — I asserted "nothing anywhere calls it" and that was WRONG.** My first grep
was scoped to `Assets/Editor/`, `tools/` and `.claude/` and returned zero hits; I reported that as
"nothing invokes it". A wider grep (whole repo) then found the caller I had excluded by my own
scoping. The rule I broke is my own §11B: a negative result is only as wide as its search. Recording
both the error and the corrected fact, because the corrected fact is worse news, not better.

**What actually calls it:** `Builds/night-remaining-focused.ps1:5` —
`@('DeNelle.Editor.RaidWallContinuityRegression.RunHeadless','RAID_WALL_CONTINUITY_OK','night-raid-wall')`,
first entry in a six-gate array, marker-judged. Sibling ad-hoc runners
`Builds/night-raid-height-rebuild.ps1:7` and `Builds/release-raid-recovery.ps1:7` drive
`RaidPolishSavedProof` the same way.

**Why this is still the gap, and a sharper one than I first described:** all three runners are
**GITIGNORED** — `git check-ignore` resolves each to `.gitignore:8:/[Bb]uilds/`. They are scratch
scripts on this machine only. `grep -l RaidWallContinuity` across the **tracked** gate scripts
(`*.ps1` at repo root, `tools/*.ps1`, `tools/regression/*.ps1`) returns **nothing**, and
`DataRegression` cannot reference it at all (asmdef, above). The runner also sets
`$env:EOA_LOCAL_STATUS_ONLY='1'` (`:3`), i.e. it is explicitly a local-status harness, not a gate.

So: the suite's only invocation **does not survive a clone, a `Builds/` wipe, or another seat**.
This ticket's proof is one `rm -rf Builds/` away from being unreproducible. An implementation lane
must promote the invocation into a **tracked** entry point — repo-root or `tools/regression/`, per
the "gate scripts live at repo root" convention — with `RAID_WALL_CONTINUITY_OK` judged on a fresh
log alongside the other distinct markers (§8). Until then, treat the green as a one-machine
artifact.

### (c) Four of the 33 proof images opened (`Builds/raid-polish-saved-proof/20260914-040422-645/`)

- `fortified_garrison_boundary_north_oblique_color.png` — the exterior ring reads as a **solid
  continuous wall**: the original pillars and their caps are still there, with unbroken stone
  backing filling the full body behind them. **No sky between pieces at any height.** This is the
  direct answer to the owner's frame.
- `fortified_garrison_Gatehouse_keep1_north_approach_color.png` — **the inner keep gate assembly
  exists and is seated in the wall**: arch + two flanking towers + raised portcullis + open
  leaf doors, cladding continuous into both shoulders with no seam, and the **spire is visible
  straight through the opening**. This is the direct answer to "the inner keep isn't connected to
  the gate".
- `raider_camp_small_boundary_west_color.png` — full west elevation: one unbroken wall band from
  corner to corner, no light through it.
- `mage_enclave_overview_color.png` — top-down: the arena boundary reads closed on all four
  sides; both castle rings read closed with corner posts and **one centred opening each**; the
  spire sits at the centre.

⚠ These images are dated `20260914-040422-645` — the **later clear-floor reproof** set, which the
RESULT says is the authoritative one; the earlier `20260914-031117-134` ledger must not be cited
as proof of an unobstructed mage floor (the RESULT says so itself, and that is WO-1703's axis).

### (d) The backing change — correct, but NOT where the RESULT implies

The `Fantasy_M/Dungeon_Wall_Stone.prefab` backing is **not** in `RaidBaseDresser.cs`. `git diff`
shows it added in **`Assets/Editor/WallTools/RaidBaseGenerator.cs:1461`** — the boundary call now
passes the module (`- RaidBaseDresser.Sys)` → `+ RaidBaseDresser.Sys, "Fantasy_M/Dungeon_Wall_Stone.prefab");`) —
and consumed by the new `PlaceSquareBacking` in **`Assets/Editor/ArenaBoundaryRing.cs:373-420`**,
where `closureHeight = (skylineTop - parent.position.y) * 0.95f` (`:383`) produces the RESULT's
6.692 m against the measured 7.044 m skyline. `ArenaBoundaryRing.cs:138` separately warns not to
substitute a thin piece. The RESULT's numbers are right; its file attribution is loose.

### (e) What I could NOT prove

- **`Builds/night-raid-height-regenerate.log` carries no `RAID_*` marker** — three `BUILT` lines
  only. The RESULT describes it as "three representative saved raid scenes regenerated", which
  matches, but there is no marker to judge it by. **UNPROVEN:** that the regenerate step is
  marker-gated at all. *Closes by:* the lane naming which line is the pass token, or the
  regenerate path emitting one.
- **That the GREEN logs describe the CURRENT tree.** `night-raid-wall-height-after.log` is
  22:08 and `ArenaBoundaryRing.cs` is 22:07 — one minute apart, so plausible, not proven, and
  `RaidWallContinuityRegression.cs` is stamped 22:04. **Closes by:** one re-run of
  `DeNelle.Editor.RaidWallContinuityRegression.RunHeadless` on the tree as it stands, judged by
  `RAID_WALL_CONTINUITY_OK` on a fresh log. This is the single cheapest thing the CLI can do
  before committing.
- **That an agent can walk gate → keep → spire.** Nothing measured it. `RaidNavBake.cs:157-185`
  strips `NavigationStatic` from every `WallSegment` and carves at runtime, so the **baked**
  navmesh has no wall blocking and an editor `NavMesh.CalculatePath` would pass vacuously;
  `RaidGroundSavedSceneRegression.cs:182` only proves hero → staging, both outside the walls.
  **Closes by:** the geometric assertion in §4 above, or a play-mode capture.
- **That the 4 images I opened represent the other 29.** I opened four. The RESULT claims root
  opened all 33 plus a 17-image delta; I did not re-verify that.
- **Owner acceptance.** Not mine to give (§13: PO felt-verifies and closes).

### File list to commit for WO-1704

**1704-only (no overlap with WO-1703):**
- `Assets/Editor/WallTools/RaidBaseDresser.cs` — `CladRing` `:499-559`, inner-ring gate `:161-171`
- `Assets/Editor/ArenaBoundaryRing.cs` — `PlaceSquareBacking` `:373-420`
- `Assets/Editor/WallTools/RaidWallContinuityRegression.cs` **+ `.cs.meta`** (untracked — both)
- **NEW FILE STILL OWED:** a **tracked** runner invoking
  `DeNelle.Editor.RaidWallContinuityRegression.RunHeadless` and judging `RAID_WALL_CONTINUITY_OK`.
  The only existing caller, `Builds/night-remaining-focused.ps1:5`, is gitignored
  (`.gitignore:8:/[Bb]uilds/`) and sets `EOA_LOCAL_STATUS_ONLY=1`, so it cannot be committed and
  does not survive a clone. Without this the suite ships uncallable.
- this ticket `.md` + its `.RESULT.md` (untracked) + regenerated `BOARD.html`

**Shared with WO-1703 — the two lanes must land these together, not separately:**
- `Assets/Editor/WallTools/RaidBaseGenerator.cs` — carries the 1704 `RingLayout`/backing-module
  change (`:523-528`, `:569`, `:1461`) in the same file as floor/layout work
- `Assets/Editor/RaidNavBake.cs` — `PrepareDestructibleWalls:157-185` is 1704's nav seam;
  `EnsureGround:188` and `TextureKeepSurfaces:275` are explicitly WO-1703 (`:180-182` comment)
- `Assets/Editor/WallTools/RaidPolishSavedProof.cs` + `.meta` (untracked) — the capture harness
  both lanes' proofs ran through; the RESULT records a 1703-side hardening inside it
- `Assets/Editor/RaidGroundSavedSceneRegression.cs` + `.meta` (untracked) — 1703's suite, but the
  only thing asserting loaded-scene wall colliders survive the bake
- The **regenerated artifacts**: `Assets/Scenes/RaidBase_{raider_camp_small,fortified_garrison,mage_enclave,IronBastion}.unity`
  and their `RaidBase_*/NavMesh.asset` (all ` M`) — one bake produced both lanes' geometry, so
  they cannot be split by path. `IronBastion` is touched by the bake although both tickets scope
  it out; the committer should say so rather than let it read as scope creep.

### Recommended Status line (I did not flip it)

`**Status:** FIXED PENDING OWNER TEST BUILD - implemented + measured RED->GREEN (RAID_WALL_CONTINUITY_FAIL 68/36 -> _OK, Builds/night-raid-wall-height-after.log 2026-09-13 22:08); suite is headless-only (not in DataRegression); re-run RunHeadless on the tree before commit; PO felt-verifies and closes`
