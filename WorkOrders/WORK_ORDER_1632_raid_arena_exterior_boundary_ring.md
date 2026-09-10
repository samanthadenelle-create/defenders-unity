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
