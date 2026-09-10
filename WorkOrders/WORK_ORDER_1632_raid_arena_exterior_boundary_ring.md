# WO-1632 - Raid arenas have no exterior boundary: the base has wall rings, the 140 m arena has nothing at its edge

**Status:** FIXED 2026-09-10 - owner directive: a square rock boundary ring at +/-68.6 m on every raid base (82 pieces/side, band-fitted scale, 0.35 m containment slack both faces, no gates), shared ArenaBoundaryRing helper with the siege venue; baked wave3-bake7 + Iron Bastion; owner felt-test on the next APK closes (was: IMPLEMENTED - awaiting gate + bake (lane ARENA-WALL 2026-09-10))
**Minted:** 2026-09-10 (lane ARENA-WALL, main-line banner; bumped 1632 -> 1633 in the SAME edit)
**Silo / Lane:** World / Raid scenes - `Assets/Editor/WallTools/RaidBaseGenerator.cs` +
`Assets/Editor/ArenaBoundaryRing.cs` (new) + `Assets/Editor/ProceduralSiegeArenaBuilder.cs`.
Raid builders are a serialization bottleneck (CLAUDE.md sec.9): one agent at a time.
**Severity:** P1 felt. The owner asked for this the same morning she confirmed the base wall rings
were landing, i.e. she looked at a raid and the arena still did not read as a place.
**Type:** NEW behaviour on an EXISTING system. The ring primitive, the plane, the navmesh baker and
the battle arena's boundary vocabulary all already exist. Nothing here is greenfield.

**Owner words (verbatim, 2026-09-10 morning, both recorded because the second one changes the
material):**

> "exterior walls around entire arena"

> "similar strategy as we used in battle arena"

---

## 1. What was measured (read at source 2026-09-10, this session)

### 1a. An arena-perimeter ring was NEVER specified and NEVER built

The 2026-09-10 raid bake, read with `tr -d '\000' < Builds/wave2-bake2 | grep -a "RaidBaseGenerator]"`,
prints exactly two ring kinds across all three configs:

```
ring 'Outer' : target +/-31.0m ... gates=[S,N]      (raider_camp_small)
ring 'Outer' : target +/-49.0m ... gates=[S]        (fortified_garrison)
ring 'Keep1' : target +/-22.1m ... gates=[N]
ring 'Outer' : target +/-54.0m ... gates=[S]        (mage_enclave)
ring 'Keep1' : target +/-24.3m ... gates=[N]
```

Every one of those is authored at the **base's own** `baseRadius` (31 / 49 / 54 m). The ground the
base sits on is `MapHalfExtent = 70f` (`Assets/Editor/WallTools/RaidBaseGenerator.cs:84`), a 140 m
square dropped by `RaidNavBake` (`Assets/Editor/RaidNavBake.cs:41`, `GroundScale = 14f`). So between
the outer wall and the edge of the world there is bare, walkable, unframed plane on every side, with
nothing at the end of it:

| config | outer ring | plane edge | bare ground per side |
|---|---|---|---|
| `raider_camp_small` (easy) | 31.0 m | 70 m | **39.0 m** |
| `fortified_garrison` (hard) | 49.0 m | 70 m | **21.0 m** |
| `mage_enclave` (extreme) | 54.0 m | 70 m | **16.0 m** |

The EASY camp has the most bare ground, which is worth stating plainly because it is the first raid a
new player ever enters.

**The three tickets that own the raid look were read end to end** - WO-1593 + RESULT, WO-1607 +
RESULT, WO-1608. The string "perimeter" occurs exactly twice across them and both are the **hub**:
`WORK_ORDER_1607_raid_bases_as_places.md:87` and `WORK_ORDER_1608_raid_base_layered_defense_engine.md:31`,
both naming `SyntyCastlePerimeterBuilder`, which dresses the player's castle hub and has never run on
a raid. **No arena-perimeter wall appears in any of their specs, acceptance lists or RESULTs.** This
is a genuine gap, not a regression and not a re-bake of already-shipped work.

### 1b. What "similar strategy as we used in battle arena" points at

| File:line | What it does |
|---|---|
| `Assets/Editor/ProceduralSiegeArenaBuilder.cs:61` | `BoundaryRadius = 72f` - "outer boundary ring just past the plate edge" |
| `:26-27` (header) | "Outer boundary ring (a low wall of large rocks) to frame the venue." |
| `:152-157` | `OuterBoundary_Ring` - 40 pieces, `RockPaths`, jitter 1.5, scale 1.4-2.2, **colliders ON** |
| `:197-217` | `PlaceCoverRing` - polar placement: even angle, XZ jitter, random palette pick, random yaw, lerped scale |
| `:223-247` | `InstantiateCover` - `LogWarning` + collidered primitive fallback when the gitignored pack is absent |
| `Assets/Editor/ArenaPrefabBuilder.cs:17`, `:125-168` | `EdgeProps` - the same ring vocabulary on the forest-clearing arena, but **colliders STRIPPED** ("pure silhouette") |

So the battle arena's boundary is **landscape pieces, not wall panels**, and the siege-venue variant
(colliders ON, "so it reads as a wall") is the one that matches the owner's ask - the arena prefab
variant is silhouette-only and would not stop anything.

All seven prefab paths those builders name were listed on disk this session under
`<repo>/Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/`:
`Trees_M/Tree_Oak|Tree_Conifer|Tree_Beech`, `Trees_M/Trees_Dead_M/Tree_Dead_Broken`,
`Stones_M/Stone_Large|Rock_Pillar|Stone_Medium_Flat` - **all present, none missing.**
(The pack is gitignored, CLAUDE.md sec.4; it is absent from this worktree and present in the repo
root, which is where the bake runs.)

### 1c. Why the WO-1593 KayKit landscape set is NOT the vocabulary here

WO-1593 was delivered through WO-1607/1608-1611, and what it shipped is a **per-camp interior kit**
(`WORK_ORDER_1593_...RESULT.md` sec.2: Easy `hexagon-green`, Hard `synty-castle`, Extreme
`dungeon-stone`), consumed by `RaidBaseDresser` for zones and props inside the base. Two reasons it
is the wrong material for the arena edge:

1. It would make the **edge of the world** look like an extension of the **base**, which is the
   opposite of framing a venue - and it would differ per camp, so the boundary would read as three
   different things.
2. `WallTier` has exactly three values - `Wood`, `Iron`, `ReinforcedSteel`
   (`Assets/_Modules/Village/Walls/WallTierData.cs:30-35`). **There is no "landscape" tier**, so a
   panel ring at the arena edge would necessarily re-use one of the base's own tiers and read as a
   second, bigger, gate-less castle wall.

The owner's own follow-up settles it: rocks, in the battle arena's shape.

### 1d. THE CONSTRAINT THAT DECIDES THE RING'S SHAPE - square, not circle

`PlaceStagingMarker` (`RaidBaseGenerator.cs:493-540`) places the player's deploy pocket due south
when it fits, and otherwise **on the south-west diagonal**, clamping at

```
axisBudget     = MapHalfExtent - StagingPlaneEdgeMargin        (:503)   = 70 - 4 = 66 m per axis
diagonalBudget = axisBudget * sqrt(2)                          (:504)   = 93.3 m from centre
```

On the 2026-09-10 bake this fallback was NOT hypothetical - it fired for two of the three configs:

| config | staging marker | distance from centre |
|---|---|---|
| `raider_camp_small` | `(0.00, 0.00, -51.20)` | 51.2 m |
| `fortified_garrison` | `(-55.89, 0.00, -55.89)` | **79.0 m** |
| `mage_enclave` | `(-51.72, 0.00, -51.72)` | **73.1 m** |

**A circular ring cannot be used.** Any circle that fits inside the 140 m square has r <= 70, and
79.0 m > 70. A circular boundary copied from the siege venue would have fenced the player's own
staging marker - and every troop deployed from it - OUTSIDE the arena. The ring must be a **square
at the plane edge**, which contains all three points comfortably (max |axis| = 55.89 m).

### 1e. The navmesh already carves - no new wiring

`RaidNavBake.BakeAll` marks every renderer `NavigationStatic` and runs the legacy bake
(`Assets/Editor/RaidNavBake.cs:55-64`); its own comment at `:56-57` says "ground bakes walkable;
vertical walls/towers carve out as obstacles". So collidered, rendered rocks at the plane edge end
the walkable area at themselves with zero extra plumbing, and the ground plane is untouched, so
`:83`'s `<n>/<n> raid scenes now have a walkable navmesh` must still read 4/4.

---

## 2. What is NOT claimed

- **Not claimed:** that the hero can currently walk off the plane. The hero and every troop/enemy is
  a `NavMeshAgent`, which cannot leave baked mesh, so the plane edge is already a hard stop. What is
  claimed is that the arena **does not read as a place** and the walkable area ends in nothing - the
  felt defect the owner is describing.
- **Not claimed:** any measured piece count, stride or footprint. The polyperfect pack is not in this
  worktree, so the rock meshes were never measured here. The builder **derives** the stride from a
  runtime measurement and logs it; no count is written into this ticket or into canon.
- **Not claimed:** that polyperfect materials render correctly on this clone. The pack is gitignored
  and re-imported via `Defenders/Art/Fix Polyperfect URP Materials` (CLAUDE.md sec.4); whether that has
  been run here was not checked. ~300 new polyperfect instances per raid scene is the first time this
  pack has appeared in a raid base, so the bake PNG is the only proof. (`RaidWallMaterialRegression`
  was read this session: it is an asset-lint on the three WALL FBXes and their `.mat`, it does not
  walk scene text, so the new instances are outside its scope.)
- **Not claimed:** that `RaidBaseDresser`'s `Zone_Approach` props all sit inside the ring. Its zone
  extents were not read. A prop past ~66 m would sit in the boundary - cosmetic, settled by the PNG.
- **Not claimed:** that the corner pieces stay perfectly inside the plane's own edge. Radial jitter
  pushes outward by up to 1.2 m from a ring at +/-68.5 m, so a piece can straddle the 70 m edge. That
  is cosmetic (static geometry, not walkable) and is settled by the bake PNG, not by this document.
- **Not touched, deliberately:** the staging math. The three `STAGING @` values above are the
  acceptance oracle precisely because nothing in this ticket may move them.

---

## 3. Target - what "fixed" means

Every raid arena is enclosed at the plane edge by a continuous ring of landscape boundary pieces, in
the battle arena's own vocabulary, with no gate; the walkable area ends at that ring; the player's
staging marker and hero entry are provably inside it; and the ring's existence is pinned per config
so a future bake cannot quietly drop it.

---

## 4. The fix (IMPLEMENTED - see sec.9)

### 4a. NEW `Assets/Editor/ArenaBoundaryRing.cs` - the shared vocabulary

Assembly `DeNelle.Editor`. `DeNelle.EditorWallTools.asmdef:4-9` already references `DeNelle.Editor`,
so `RaidBaseGenerator` can call it with **no asmdef change**.

It owns, once, what was previously private to `ProceduralSiegeArenaBuilder`:

- `NatureRoot`, `TreePaths`, `RockPaths` (the palettes, moved verbatim).
- `InstantiatePiece` - prefab load, collider guaranteed, `LogWarning` + collidered primitive fallback.
  The fallback now gets an explicit URP/Lit tint: an **editor bake SAVES the scene**, so unlike the
  runtime `MagentaGuard` registry an unassigned material would persist as magenta in the shipped raid.
- `PlacePolarRing` - the siege venue's `PlaceCoverRing` body, moved. **The RNG draw order is frozen**
  (jitter-x, jitter-z, prefab index, yaw, scale), so `SiegeArena.unity`'s saved layout is reproduced
  exactly and that venue needs **no re-bake**.
- `MeasureMinFootprint` - instantiate each palette prefab once, take the min XZ renderer bound,
  destroy. Same measure-then-discard shape as `RaidBaseGenerator.MeasureTowerHalf`.
- `PlaceSquarePerimeter` - the new shape, with the derived stride below.

`ProceduralSiegeArenaBuilder.PlaceCoverRing` is kept as a one-line wrapper so its four cover-ring
call sites read exactly as they did, and it now routes into the shared helper.

### 4b. The derived stride - why there is no magic count

```
pieceFootprint = MeasureMinFootprint(palette) * scaleMin     // smallest a placed piece can be
wantedStride   = pieceFootprint * ArenaBoundaryOverlap       // <1 => pieces overlap
perSide        = ceil(2*halfExtent / wantedStride) + 1       // clamped by ArenaBoundaryMaxPerSide
stride         = 2*halfExtent / (perSide - 1)
worstGap       = stride - pieceFootprint                     // NEGATIVE = closed ring
```

The ring is therefore continuous **by construction** and the builder can prove it: `worstGap` is
reported in the ring log line and a positive value raises a `LogWarning` naming the knob to move.
A hardcoded "40 pieces" (the siege venue's number) would have been a guess about art nobody
measured - CLAUDE.md sec.11B - and would silently open metre-wide holes if the palette changed.
Tangential jitter is bounded by the surplus overlap, so jitter can never open a gap the stride
closed. Radial jitter is **outward only**, so the ring can never eat into the arena.

### 4c. `RaidBaseGenerator` - constants, one call, one assert, one log line

New constants (`:86-124`):

| Const | Role |
|---|---|
| `ArenaBoundaryInset = 1.5f` | how far inside the plane edge the ring sits; **constrained, not chosen** - see 4d |
| `ArenaBoundaryHalfExtent = MapHalfExtent - ArenaBoundaryInset` | the ring's square half-extent |
| `ArenaBoundaryOverlap = 0.7f` | stride as a fraction of the smallest piece footprint |
| `ArenaBoundaryRadialJitter = 1.2f` | outward-only scatter |
| `ArenaBoundaryScaleMin/Max = 2.2f / 3.4f` | boulder scale; larger than the siege venue's 1.4-2.2 because this ring frames a 137 m run |
| `ArenaBoundaryMaxPerSide = 80` | cost ceiling; when it binds the stride widens and the log says `CLAMPED` |

`BuildArenaBoundary(root, seed)` builds the ring under `ArenaBoundary_Ring`, on its **own RNG stream**
(`seed ^ 0x5A17`) so adding it never shifts the turret or prop seeds of an existing base. It is called
**last** in `BuildConfigLayout` - after the markers exist - and also from the legacy `BuildIronBastion`
path, because "entire arena" is every raid base. Palette = `ArenaBoundaryRing.RockPaths`; gates: none.

### 4d. `ArenaBoundaryInset` is derived from the staging clamp, and re-asserted every build

The ring's inner face must sit further out than the furthest point a staging marker can ever occupy:

```
innerFace   = ArenaBoundaryHalfExtent - inwardReach       inwardReach = MEASURED widest piece / 2
clampCorner = MapHalfExtent - StagingPlaneEdgeMargin      = 70 - 4 = 66.0 m   (the clamp branch)
invariant:  innerFace > clampCorner
```

`AssertBoundaryContainsStaging(id, staging, heroStart, boundary)` re-derives that at **every build**
and `Debug.LogError`s if it is violated, plus errors if this config's actual staging marker or hero
entry lands outside the inner face. It logs the measured slack when it passes. It never adjusts
anything - a violation is a LAYOUT finding, the same discipline `PlaceStagingMarker:526-538` uses.

⚠ **`inwardReach` is MEASURED, not derived from the scale.** `BoundaryReport.MaxPieceFootprint` =
widest XZ renderer bound across the palette x `ArenaBoundaryScaleMax`. Pricing it as
`ArenaBoundaryScaleMax / 2` would silently assume a 1 m mesh: at scale 3.4 on a 2 m rock the true
inward reach is 3.4 m, not 1.7 m, and the assert would have reported slack it never measured. **No
number for `innerFace` is written into this ticket** - the polyperfect pack is absent from the lane's
worktree, so the bake log is the authority. What IS known without the mesh: the live markers sit at
55.89 m max on their widest axis, i.e. ~12 m inside the 68.5 m ring line, so the live headroom is
large and only the clamp-branch worst case is tight.

The regression's version of this check is the **FLOOR bound only** (it prices a piece at
`scaleMax x 1 m`, because an asset-lint cannot open a mesh) and its failure text says so.

### 4e. The log line, in the shape the other rings log

One `[RaidBaseGenerator] ring 'Arena': ...` per config, so one grep reads every ring in a bake, plus
an `ARENA BOUNDARY` clause appended to that config's existing `BUILT:` summary.

---

## 5. Acceptance

1. `python tools/gate_brace.py` clean and zero NUL bytes on all four `.cs`. **[done, sec.9]**
2. Compile gate green on the combined tree. *(lead)*
3. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes` re-bakes the three scene-config raids and the
   log shows, per config, a `ring 'Arena':` line reporting `gates=[none]` and a **negative** gap
   (i.e. "Nm overlap"). *(lead)*

   ⚠ **`CLAMPED` or `*** GAP ***` on the FIRST bake is a PALETTE/TUNING FINDING, NOT A TICKET FAIL,
   and the lead does not bounce the ticket for it.** The stride derives from the THINNEST piece in
   the palette, and `Rock_Pillar` is a pillar - if its minor axis is small the derived stride is tiny,
   `ArenaBoundaryMaxPerSide` binds, the stride widens and a gap opens. Neither number is provable
   before the bake (that is precisely why the stride is derived rather than authored). The move, in
   order: (a) drop the thin piece - give `ArenaBoundaryRing` a `BoundaryRockPaths` subset and point
   `BuildArenaBoundary` at it; (b) if it still binds, raise `ArenaBoundaryMaxPerSide`. Re-bake and
   record the measured line. Only a gap that survives both is a defect.
4. **The three `STAGING @` values are BYTE-IDENTICAL to the 2026-09-10 bake** - `(0.00, 0.00, -51.20)`,
   `(-55.89, 0.00, -55.89)`, `(-51.72, 0.00, -51.72)`. The staging math never reads the boundary, so
   any drift there is a defect in this change. *(lead)*
5. No `ARENA BOUNDARY ASSERT` error line in the bake log. *(lead)*
6. `DeNelle.Editor.RaidNavBake.BakeAll` still reports **4/4** raid scenes with a walkable navmesh. *(lead)*
7. The regression suite is **RED before the bake and GREEN after it** - `[arena-boundary]` fails today
   with `has NO 'ArenaBoundary_Ring'` for all three configs, because the check reads the BAKED scene.
   That red is the proof the case measures something. *(lead)*
8. **The commit carries the BAKED ARTIFACTS with the code** - the three `Assets/Scenes/RaidBase_*.unity`,
   their `Assets/Scenes/RaidBase_*/NavMesh.asset`, and `Assets/Editor/ArenaBoundaryRing.cs.meta`
   (Unity writes the .meta on first import; it does not exist yet in the lane's worktree). Committing
   the `.cs` alone leaves Case 6 RED on every subsequent gate, for every other lane. *(lead)*
9. Owner felt-test closes: enter each raid and confirm the arena reads as an enclosed place and the
   walkable ground ends at the rocks.

**Run order** (the regression is red before the bake by design, so it goes LAST):
compile gate -> `RaidBaseGenerator.BuildAllRaidScenes` -> `RaidNavBake.BakeAll` -> regression suite.

---

## 6. Pins - what must not move

| Pin | Where | What it catches |
|---|---|---|
| `RaidArenaShapeRegression.CaseArenaBoundary` (new, Case 6) | `Assets/Editor/Regression/RaidArenaShapeRegression.cs` | four things, below |
| - source lint | | the builder stops wiring the shared ring, or grows a gate on it |
| - shared-not-copied | | `ProceduralSiegeArenaBuilder` stops routing through `ArenaBoundaryRing`, i.e. the two arenas drift back into two copies |
| - constant inequality | | anyone moves `ArenaBoundaryInset`, `ArenaBoundaryScaleMax` or `StagingPlaneEdgeMargin` **alone**, re-opening the fence-the-player-out failure |
| - **per-config scene check** | | **any raid config whose baked scene lacks `ArenaBoundary_Ring`, or has it on fewer than 4 sides** - this is the "reds if any raid config lacks the perimeter ring" case, and it is red-first by construction |
| `RaidStagingMarkerRegression` | existing | the staging seam this ticket must not disturb |
| `RaidBaseLayoutRegression` | existing | per-camp kits + dresser wiring; read this session, it counts no rings, so the new ring cannot break it |

Registered already: `RaidArenaShapeRegression.Run` is wired at
`Assets/Editor/Regression/DataRegression.cs:737`, so **no `DataRegression.cs` edit is needed** and
that lane-fenced file is untouched.

---

## 7. What NOT to touch

- `EnsureUpright` - not read, not called, not modified by this ticket.
- The spire fit (`SPIRE FIT` / WO-1617 / WO-1619 tunables) - untouched.
- Garrison HP, composition, `eliteCount`, `RaidGarrisonSpawner` - untouched.
- **The base's own rings** - `Outer`, `Keep1..n`, their tiers, their gates, `BuildRing`'s body,
  `MaxSegmentWidth`, `wallSegmentsPerSide`. The new ring is an ADDITIONAL, OUTERMOST ring and changes
  none of them.
- `PlaceStagingMarker`'s arithmetic, `StagingMargin`, `StagingPlaneEdgeMargin`,
  `DefenderPerceptionRadius` - read only.
- `RaidNavBake` - not edited; the ring carves through the existing NavigationStatic pass.
- `RaidBaseDresser` - not edited. The ring vocabulary does NOT live there; it lives in the new shared
  `ArenaBoundaryRing`, which is why the dresser needed no change.
- `DataRegression.cs` - not edited (already registered, sec.6).
- Scene `.unity` files - never hand-edited; they change only through the sanctioned bake.

---

## 8. Board

Status flipped in this same change; `python tools/board_build.py` regenerated `BOARD.html`.
RESULT: `WorkOrders/WORK_ORDER_1632_raid_arena_exterior_boundary_ring.RESULT.md`.

---

## 9. IMPLEMENTED 2026-09-10 - lane ARENA-WALL (edit-only)

Base `c10e4f5d1`. Files changed:

| File | Change |
|---|---|
| `Assets/Editor/ArenaBoundaryRing.cs` | **NEW** - shared palette + `PlacePolarRing` + `PlaceSquarePerimeter` + `MeasureMinFootprint` + `InstantiatePiece` |
| `Assets/Editor/ProceduralSiegeArenaBuilder.cs` | palettes + polar placement + instantiate routed into the shared helper; RNG order frozen, venue layout unchanged |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | boundary constants, `BuildArenaBoundary`, `AssertBoundaryContainsStaging`, one call in `BuildConfigLayout`, one in `BuildIronBastion`, ring log line + `BUILT:` clause, header note |
| `Assets/Editor/Regression/RaidArenaShapeRegression.cs` | Case 6 `[arena-boundary]` + `ReadConstF` helper + two source paths |

Quality gate run this session: `python tools/gate_brace.py <the four files>` -> `GATE_BRACE_SUMMARY
bad=0 of 4`; raw brace counts 23/23, 26/26, 293/293, 151/151; NUL bytes 0 in all four.

**Not run here, by instruction:** Unity, the compile gate, any bake, any commit.

## 9b. STEP 2 - 2026-09-10, forged by the first bake (`Builds/wave3-bake3`)

The ring baked on all three configs **and its own containment assert fired on all three**:

```
ring 'Arena': target +/-68.5m -> +/-68.5m, 40 piece(s)/side @ 3.43m (piece 4.93m, 1.51m overlap),
  kit landscape-rock (0 watchtowers, 160 boundary pieces), gates=[none].
ARENA BOUNDARY ASSERT: the staging clamp reaches +/-66.0m per axis but the boundary ring's inner
  face is at +/-62.8m (ring +/-68.5m minus a MEASURED 5.75m inward reach).
```

**The instrument worked and sec.4d's assumption was the defect.** Back-solving the printed numbers:
the palette's thinnest piece is `4.93 / 2.2 = 2.24 m` and its widest is `(5.75 x 2) / 3.4 = 3.38 m`
at scale 1. So the requested 3.4x scale made an **11.5 m boulder reaching 5.75 m inward through a
4 m band**. `ArenaBoundaryInset = 1.5f` was a CHOSEN number, and no inset can fix it: containing a
5.75 m reach outside the 66.0 m clamp needs a ring line past 71.75 m, which is off the 70 m plane.

**The fix inverts the dependency: the SCALE now follows the BAND, not the other way round.**

- `ArenaBoundaryInset` is **deleted**. The ring line is derived: `ArenaBoundaryHalfExtent =
  MapHalfExtent + (ArenaBoundaryEdgeTolerance - StagingPlaneEdgeMargin)/2` = the **midpoint of the
  band** between the staging clamp (66.0 m) and the edge tolerance (71.2 m) -> **68.6 m**, with
  `ArenaBoundaryBandHalf = 2.6 m`.
- `ArenaBoundaryScaleMin/Max` become **ceilings**. `PlaceSquarePerimeter` shrinks them until the
  measured widest piece occupies at most `ArenaBoundaryBandFill` (0.85) of the band, and logs the
  fit. `StagingPlaneEdgeMargin` is **not touched** - moving it would move the three authored staging
  positions, which acceptance 4 forbids.
- Radial jitter was **outward-only at 1.2 m**, which alone would have spent the whole outward
  half-band. It is now **symmetric and bounded by the band slack the fitted piece leaves**.
- The assert now also checks the OUTER face against the edge tolerance, and returns a bool.
- **On failure the ring root is renamed `ArenaBoundary_Ring_CONTAINMENT_FAIL`**, so the finding is
  carried by the SAVED SCENE, not only by a log nobody may re-read.
- `ArenaBoundaryMaxPerSide` 80 -> 100 (the smaller fitted pieces need a shorter stride; 100 keeps the
  clamp from binding silently).

Case 6 gains two hard reds for this, per the coordinator's instruction:
**step 4** fails on `ArenaBoundary_Ring_CONTAINMENT_FAIL` in any raid scene, and **step 5**
(`CaseArenaBoundaryBakeLogs`) fails when a `Builds/*bake*` log carrying a `ring 'Arena'` line ALSO
carries an `ARENA BOUNDARY ASSERT` line (bytes read, NULs stripped - what `tr -d '\000'` does;
absence of `Builds/` is never a failure, that directory is gitignored). Step 3's old scale-priced
"floor bound" is replaced by a **band-arithmetic** check, because the bake proved a lint that prices
a piece at `scaleMax x 1 m` sits green while the real mesh fails.

**Iron Bastion (coordinator option (b), taken).** Case 6 stays **strict** - no exemption. The
parameterless batchmode entry exists and is safe headless:
**`DeNelle.Editor.RaidBaseGenerator.BuildToNewScene`** (`RaidBaseGenerator.cs:323-331`, public static,
no args, `NewScene` -> `Build()` -> `SaveScene` to `Assets/Scenes/RaidBase_IronBastion.unity`). It
recreates the scene from scratch - which is how **every** raid scene is authored (`BuildSceneFor` at
`:355-364` does the same, and the 09-10 log says "into NEW scene" for all three), and
`RaidNavBake.BakeAll` already covers `RaidBase_IronBastion` in its 4/4. The case's own failure text
now names this entry point, so the red carries its own remedy.

## 9c. STEP 3 - 2026-09-10, the second bake (`Builds/wave3-bake4`): the ring was right, the TEST was a knife edge

The re-bake produced the predicted ring **exactly** - `+/-68.6m, 67 piece(s)/side @ 2.05m (piece
2.93m, 0.89m overlap, scale 3.40->1.31 band-fitted, reach 2.21m of 2.60m band, jitter +/-0.39m), 268
boundary pieces` - and the assert **still fired on all three**:

```
the staging clamp reaches +/-66.0m per axis but the boundary ring's inner face is at +/-66.0m
  (ring +/-68.6m minus a MEASURED 2.60m inward reach)
```

**Reach 2.21 + jitter 0.39 = 2.60 = the half-band, exactly.** That was not bad luck, it was built in:
the jitter bound was `bandHalf - maxFootprint/2`, i.e. *whatever room is left*, so the faces always
land **exactly on** the band edges, and the ring line being the band midpoint then puts the inner face
**exactly on** the staging clamp. A `>=` comparison against a value the code guarantees to equal can
only fail. **Zero clearance is not a pass** - the marker would sit on a boulder - so the fix is a real
margin, not a loosened comparison.

- New `ArenaBoundaryContainmentSlack = 0.3f` - the margin required at **both** faces, and **named in
  the assert's own message** so a failure says how much room it wanted.
- The jitter bound now **reserves it**: `jitterRoom = bandHalf - containmentSlack - maxFootprint/2`.
- `ArenaBoundaryBandFill` 0.85 -> **0.70**, because the fill, the jitter and the slack must all fit
  the half-band. The live invariant is `bandHalf*fill + jitter + slack <= bandHalf`; with slack 0.3
  over a 2.60 m half-band the fill ceiling is **0.88** before the jitter gets anything, and 0.70
  leaves the jitter 0.48 m and both faces a full 0.30 m.
- The assert is **strict**: it fails at `innerSlack < slack` and at `outerSlack < slack`, and the
  marker checks became `sMax + slack > innerFace` (same for the hero entry).
- Case 6 step 3 gains the matching lint: `slack > 0` and `fill <= 1 - slack/bandHalf`, so this exact
  knife edge cannot be re-authored.

The ring line is **NOT** moved off the band midpoint (the coordinator's other option). The band is
only 5.2 m wide, so pushing the midpoint outward buys inner slack by spending outer slack; reserving
the margin inside the fit gives **0.30 m at both faces** instead of trading one for the other.

### Expected numbers on the next bake (derived from the two bakes' printed values)

```
applied scale 3.40 -> 1.08      piece 2.41m   maxPiece 3.64m   reach 1.82m   jitter +/-0.48m
inner face 66.30m  vs staging clamp 66.00m  = 0.30m slack   (required 0.30m)
outer face 70.90m  vs edge limit    71.20m  = 0.30m slack   (required 0.30m)
82 piece(s)/side @ 1.67m stride, 0.74m overlap, 328 boundary pieces
```

Ring line: `ArenaBoundaryHalfExtent = 70 + (1.2 - 4)/2 = 68.60 m`; half-band `(4 + 1.2)/2 = 2.60 m`;
band `[66.00, 71.20]`. **These are a prediction from the previous bake's printed mesh sizes, not a
measurement** - the new bake's own line is the authority.

⚠ **`Builds/wave3-bake3` and `wave3-bake4` must be deleted or moved after the clean re-bake**, and
the three scenes re-baked: they currently carry `ArenaBoundary_Ring_CONTAINMENT_FAIL`, so Case 6
steps 4 and 5 are both red until then. That is the pins working, not a new defect.

## 10. Owner question (a default is already applied - this is not a blocker)

**Boundary palette: rocks only, or rocks with occasional trees?**
Applied default: **rocks only**, matching `ProceduralSiegeArenaBuilder:157`'s `OuterBoundary_Ring`
verbatim ("a low wall of large rocks"), at scale 2.2-3.4. `ArenaBoundaryRing.TreePaths` is already
exported, so switching to a mixed palette is a one-line change at the call site if she wants the
edge to read as a treeline instead of a boulder field.

---

## DEVICE FRAMES 2026-09-10 (build 363529)

Read-only device lane. Seeker `SM02G4061955851`, package `com.denellestudios.echoesofelarion`,
`versionName=2026.09.10.363529` / `versionCode=363529` (read from
`adb shell dumpsys package`, this session). Screencap framebuffer is **2670x1200 landscape** on every
frame - the WO-1631 landscape-only lock holds; no portrait frame was produced. One full raid was
played end to end: The Forsaken Camp (Regular), `raider_camp_small` ->
scene `RaidBase_raider_camp_small`, ended `TIME!` at 0:00 with 10% razed.

All paths relative to the repo root, under `Builds/device-frames/`.

### Approach frames

| PNG | What it shows |
|---|---|
| `2026-09-10_0558_raid_title_363529.png` | Title, LANDSCAPE, CONTINUE / START NEW / PLAY INTRO. |
| `2026-09-10_0559_after_continue.png` | WELCOME BACK, KEEPER idle-yield modal (3h 53m, storage full). |
| `2026-09-10_0600_after_collect.png` | HARVEST RESULT modal - wood/iron/stone all 3,000/3,000 FULL. |
| `2026-09-10_0601_after_close.png` | DAILY CHEST modal. |
| `2026-09-10_0602_town.png` | Town, `Main_Castle_Overworld`, bottom dock BUILD/TALK/HERO/JOURNEY/MANAGE. |
| `2026-09-10_0603_journey_deck.png` | Journey deck - QUESTS and RAIDS cards only. |
| `2026-09-10_0604_raid_selection.png` | **Raid selection** - 4 camps; Forsaken Camp (Regular) CLEARED, "Outmatched - Army 9 advised". |
| `2026-09-10_0605_raid_staging.png` | **Staging screen** - Grom + Sylas, ARMY 8/10, Footman x8, Orc Necromancer 96 power, Scout Report, BEGIN ASSAULT. |
| `2026-09-10_0607_arena_01_entry.png` | First BEGIN ASSAULT tap only highlighted the face; the screen did not advance (see issue 1). |

### Arena frames - the three things the owner asked to see

| PNG | What it shows |
|---|---|
| `2026-09-10_0608_arena_01_entry.png` | **Arena entry, staging camera.** The **exterior boundary ring is visible and continuous across the entire horizon** - a band of tan rock pillars behind the base wall on every side that the camera reaches. Spire, courtyard props and the low grey wall rail all in frame. Clock static 3:00, Troops 0/0. |
| `2026-09-10_0609_arena_02_pan_left.png` | Same view, camera pitched - **best single frame of the ring reading as an enclosing edge** left-to-right across the full 2670 px. |
| `2026-09-10_0610_arena_03_pan_right.png` | Ring again from a slightly lower pitch; ground plane edge visible bottom-left/bottom-right. |
| `2026-09-10_0612_arena_04_hero_forward.png` | **Courtyard cover props, close.** Weapon rack, red crates, barrels, rock cluster, red-roofed tent, low wall segments - props are at hero scale and sit between the hero and the spire, i.e. they read as cover, not as a decor border. |
| `2026-09-10_0613_arena_05_hero_left_edge.png` | Props from inside the base ring (crates, racks, tent) with the boundary ring behind them. Clock ticking 2:54; "HERO DOW…" text visible behind the deploy bar. |
| `2026-09-10_0614_arena_06_wide.png` | **Widest arena frame.** Ring across the full horizon, spire left of centre, props foreground. Clock 2:16. |
| `2026-09-10_0615_arena_07_deploy.png` | **Cleanest composite frame** - 8/8 troops deployed and fighting, ring on the horizon, crate + weapon rack + tent as cover, spire behind. |
| `2026-09-10_0616_arena_08_boundary.png` | Combat under way, Razed 4%, ring still fully enclosing at this camera yaw. |
| `2026-09-10_0617_arena_09_toward_ring.png` | Clock 1:34, Razed 6%, Troops 5/8. Hero is on the ground beside its shield and the camera did not advance - the walk input had no effect from here on (capture limit, section below). Ring, spire, crate, weapon rack and tent all in frame. |
| `2026-09-10_0618_arena_10_ring_edge.png` | Same locked camera, clock 1:26, Razed 6%, Troops 4/8. |
| `2026-09-10_0620_arena_11_result.png` | **TIME!** - clock ran out, 10% razed, 4 troops wounded, +189 wood / +198 iron / +396 gold / +2 crystals, 0/3 stars. |
| `2026-09-10_0625_back_in_town.png` | Back in town after RETURN TO CASTLE - Grom Lv 4, gold 1402. Device left here. |

**The `HHMM` in each filename is an approximate wall-clock label, not the device log timestamp.** The
authoritative times are in the logcat: scene load `06:02:17.884`, clock start `06:04:03.601`,
finalize `06:07:13`. **Exactly one raid was played**, despite `0607` and `0608` both carrying
"arena_01_entry" in their names.

### Camera coverage - what these frames DO and DO NOT cover

Camera diversity here is **pitch changes plus hero walking only**. Single-finger `adb shell input
swipe` did not yaw the camera (frames `_02` and `_03` are near-identical viewpoints), and there is no
pinch, so no zoom-out was attempted. The hero went down roughly 10 s after the clock engaged (Army 8
vs a 9-defender garrison - the selection screen said "Outmatched"), after which movement locked and
no further camera position was reachable. Read the compass strip: frames `_01`-`_03` show NW/N/NE,
frames `_04`-`_10` show NW/N/NE/E. **The camera never faced south or west.** The hero seat is at
z=-51.2 and the ring sits at +/-68.6 m, so the southern and western arc of the ring was never in
frame and is NOT evidenced here.

### Verdict against this ticket's acceptance

- **The exterior boundary ring SHIPPED and is visible.** Every arena frame shows a continuous band of
  tan rock pillars running the full width of the frame, unbroken, with no gap or open edge in any
  frame. ⚠ **Scope of that claim: the north-facing arc only** (compass NW through E). The southern
  and western arc was never in frame - see "Camera coverage" above - so "on every side" is NOT
  proven by this capture.
- **Courtyard cover props are PRESENT and at hero scale** (WO-1633): crates, barrels, weapon racks,
  a red-roofed tent and rock clusters, standing between the hero seat and the spire, with walkable
  ground between them. ⛔ The frames CANNOT show whether they carry colliders, nor whether the
  layout is "2-3 clustered rings with a clear lane" - that is WO-1633's own claim and is not
  evidenced here either way.
- **The Forsaken Camp spire SHIPPED** (WO-1619): the pale stone tower at centre. Confirmed in the log:
  `RaidSpire 'RaidSpire': built a solid capsule hitbox (h=14.4 r=5.3)` and
  `RaidSpire 'RaidSpire' online: 1200 HP, config='raider_camp_small', art='tower_ruined_watchtower'`
  - h=14.4 m matches WO-1619's `achieved=14.40m`.

### Viewable issues named plainly (NOT acted on - read-only lane)

1. **BEGIN ASSAULT needed two taps.** The first tap only lit the face; the screen stayed on staging
   for 15 s. The second tap loaded the raid. Reproduced once; not proven to be systematic.
2. **A thin RED LINE is drawn across the arena - UNIDENTIFIED, not proven to be a defect.** Visible
   in `..._0612`, `..._0615`, `..._0616`, `..._0617` and `..._0618`, running from a distant figure
   near the left edge (x~130-200, y~480) to the hero. Two candidates, neither proven: (a) an
   intentional aim/threat telegraph - the log carries 130 `[Flow:CastTelegraph]` and 64
   `[Flow:ThreatTell]` lines in this window, including
   `target-marker START unit=Wall_Outer_SS_8 path=VFX/UI/TalentNodePointer caster=Hero (Blaise)
   ability='target lock (auto)' windup=6.00s`; or (b) leftover debug geometry. **The check:**
   `grep -aE "\[Flow:(ThreatTell|CastTelegraph)\]" Builds/device-frames/2026-09-10_raid_logcat.txt`
   and correlate the START/END timestamps against the frames the line appears in, then read the
   `VFX/UI/TalentNodePointer` prefab. Do not open an RCA against working code before that read.
3. **Two flat untextured BRIGHT GREEN BOXES stand on the base-wall line - UNIDENTIFIED.** A
   symmetric pair, one right of centre in nearly every arena frame and one left of centre in
   `..._0609`. They carry no texture and read as placeholder next to the finished crates and racks.
   The scout report for this camp says "Wood walls, **2 gates**", so a plausible - but **unproven** -
   reading is that these ARE the two gate markers rendering untextured. **The check:** open the
   `RaidBase_raider_camp_small` scene / bake log for the gate objects and read their material. No
   gate-prop line exists in this logcat either way.
4. **The boundary ring and the spire are the same washed-out pale tan as the sky**, low contrast and
   flat-shaded. The ring is geometrically present but does not READ as a wall; this is the most
   likely remaining source of the owner's "feels incomplete and not polished".
5. **The raid readout panel (top right) is low contrast** - "Razed 0%" and "Troops 0/0" are grey on a
   translucent grey plate and are close to illegible over the pale ground.
6. **The yellow objective chevron is enormous** and overlaps the compass bar and the centre of the
   screen in `..._0612`, `..._0614`, `..._0618`.
7. **"HERO DOWN" is rendered BEHIND the deploy bar** and is clipped to "HERO DOW…"
   (`..._0613_arena_05_hero_left_edge.png`).
8. **`DEPLOY …` bar face is truncated with an ellipsis** in every arena frame.

Outside this ticket, seen on the way in and not acted on: `SPOILS` wraps to `SPOIL / S` on the
staging screen (`..._0605`); the RAIDS deck card has untinted white patches at its corners
(`..._0603`); `ATTACK REPORT HELD` overlaps the `Echoes 2/6` chip in town (`..._0602` and
`..._0625`); and in `..._0625`, taken immediately AFTER a raid was played with 8 troops, the Heart
of Elarion panel still reads **"Train 2 troops to unlock Raids"**.

Logcat for this session: `Builds/device-frames/2026-09-10_raid_logcat.txt` (`-d` dump) and
`Builds/device-frames/2026-09-10_raid_logcat_stream.txt` (live stream held open across the raid so
the load window could not be evicted from the ring). **No `ArenaBoundary` / `ring 'Arena'` / `props '`
lines exist in either** - the ring and the props are BAKED scene content (commit `2e66a552e`), not
runtime-built, so there is nothing for them to log. Their proof is the frames above, not a marker.

### Tickets minted from these frames (CLI minting lane, 2026-09-10; block 1637-1642)

| WO | What it owns | Premise correction carried in the ticket |
|---|---|---|
| [1637](WORK_ORDER_1637_raid_arena_reads_flat_ring_spire_and_sky_are_one_pale_tan.md) | issue 4 - ring, spire and sky are one pale tan; the base wall is a low grey railing | The ring did NOT fall back and no material failed on device. The tan is the palette pick (`M_14_Brown_lightest_LPUP`, no albedo texture) compounding with baked fog `(0.66,0.58,0.42)` ending at 95 m while the ring sits at 68.6-97 m. Also: the siege venue already SHARES this palette, and "WO-1607 section 4" is the wrong citation - the wall row is section 6 `:139`. |
| [1638](WORK_ORDER_1638_raid_gatehouse_banners_read_as_untextured_green_placeholder_boxes.md) | issue 3 - the two flat green slabs | **They are NOT the gates.** The gates are `Gatehouse_south` / `_north` at `(0,0.05,-/+31)`, real KayKit `wall_straight_gate`. The slabs are a symmetric gatehouse prop pair; the 5x zoom shows a hanging banner with two finials, matching `flag_green.fbx`. |
| [1639](WORK_ORDER_1639_raid_hud_readout_is_illegible_and_three_labels_are_cut_or_buried.md) | issues 5, 6, 7, 8 - readout contrast, objective marker, HERO DOWN, DEPLOY | Measured contrast: `Troops 0/0` **1.12:1**, `Razed 0%` 1.72:1, nothing on the panel reaches 3:1 (plate alpha 0.42 vs the kit's canon 0.98). The "chevron" is not a UI element - no raid objective chevron is authored anywhere; the only matching candidate is world-space and unclamped. |
| [1640](WORK_ORDER_1640_raid_staging_spoils_label_breaks_mid_word_and_the_outmatch_confirm_is_invisible.md) | issue 1 + the `SPOIL / S` line | **Issue 1 is not a defect and IS systematic.** The first tap fires the owner-ruled WO-1542 outmatch confirm (logcat `:25544-25547`, 9 defenders vs 8). The real defect: its toast draws at sortingOrder 720 under a 31050 panel with a 0.94-alpha backdrop, so the player never sees the question. |
| [1641](WORK_ORDER_1641_heart_objective_still_says_unlock_raids_after_the_first_raid.md) | the stale Heart gate copy | **Not stale - it FLIPPED.** `..._0602` reads "Prepare the realm for the next wave."; `..._0625` reads the gate line. `FIRST RAID COMPLETED ... everCompletedRaid false->true` (logcat `:60381`) moves the bar from 3 to the cap of 10, and the same emitter prints `required=3` then `required=10` 108 ms apart. |
| [1642](WORK_ORDER_1642_town_chrome_raids_card_white_corners_and_the_attack_report_chip_off_plate.md) | the RAIDS card corners + the ATTACK REPORT chip | `raids.png` has an opaque checkerboard border; the existing `OpaqueMargins` crop is RECTANGULAR, so residue survives in the rounded corners. And the chip does not "overlap" the Echoes chip - it is a 3-line caption escaping its own plate at BOTH edges, landing in the gutter above it. |

**Every one of the eight issues above is now owned.** Issue 2 (the thin red line) is deliberately NOT
minted - it is still unidentified, and this section's own check for it has not been run.
