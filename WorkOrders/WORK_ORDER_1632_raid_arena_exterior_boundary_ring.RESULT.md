# WO-1632 RESULT - Raid arena exterior boundary ring

**Status:** IMPLEMENTED - awaiting gate + bake (lane ARENA-WALL 2026-09-10)
**Base:** `c10e4f5d1` (worktree `.claude/worktrees/agent-a5c4068f74dbd22cd`, branch `dev`, clean before
the lane started). Edit-only: no Unity, no bake, no gate, no commit.

---

## 1. The question the lane was asked first, and the answer

**"Was an arena-perimeter wall ever specified or built?" - NO, neither.**

- **Never built.** `tr -d '\000' < Builds/wave2-bake2 | grep -a "RaidBaseGenerator]"` (the 2026-09-10
  bake) prints only `ring 'Outer'` and `ring 'Keep1'`, all at the base's authored `baseRadius`
  (31 / 49 / 54 m). Nothing at `MapHalfExtent = 70f` (`RaidBaseGenerator.cs:84`).
- **Never specified.** WO-1593 (+ RESULT), WO-1607 (+ RESULT) and WO-1608 were read end to end. The
  word "perimeter" appears exactly twice, both naming `SyntyCastlePerimeterBuilder` on the **hub**
  (`1607:87`, `1608:31`). No arena-edge ring in any spec, acceptance list or RESULT.

## 2. The one design ruling worth carrying forward

The raid boundary is a **SQUARE**, unlike the battle arena's circle. `PlaceStagingMarker`'s diagonal
fallback (`RaidBaseGenerator.cs:503-528`) parks the player's deploy pocket in the plane's CORNERS -
`fortified_garrison` staged at `(-55.89, 0, -55.89)`, **79.0 m** from centre on the 09-10 bake. No
circle that fits the plane (r <= 70) contains that point, so a copied circular ring would have fenced
the player's own staging marker out of the arena. Square at +/-68.5 m contains it with 11 m to spare.

## 3. Files changed

| File | Change |
|---|---|
| `Assets/Editor/ArenaBoundaryRing.cs` | **NEW** (DeNelle.Editor). Shared palette + `PlacePolarRing` (the siege venue's body, RNG order frozen) + `PlaceSquarePerimeter` + `MeasureMinFootprint` + graceful `InstantiatePiece` |
| `Assets/Editor/ProceduralSiegeArenaBuilder.cs` | palettes / placement / instantiate routed into the shared helper; `PlaceCoverRing` kept as a wrapper. Venue layout byte-identical -> **no SiegeArena re-bake** |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | boundary constants `:86-124`, `BuildArenaBoundary`, `AssertBoundaryContainsStaging`, call in `BuildConfigLayout` (after staging) and in `BuildIronBastion`, ring log line + `BUILT:` clause, header note |
| `Assets/Editor/Regression/RaidArenaShapeRegression.cs` | Case 6 `[arena-boundary]` + `ReadConstF` + two source paths. **Already registered** at `DataRegression.cs:737` - that lane-fenced file is untouched |
| `CLI_LANES_WO_NUMBERS.md` | minted 1632, bumped 1632 -> 1633 in the same edit |
| `WorkOrders/WORK_ORDER_1632_*.md` + this RESULT | the ticket |

No `.unity` file, no `.asmdef`, no `RaidBaseDresser.cs` (the ring vocabulary does not live there),
no `DataRegression.cs`.

## 4. Quality gate run here

```
python tools/gate_brace.py Assets/Editor/ArenaBoundaryRing.cs \
  Assets/Editor/ProceduralSiegeArenaBuilder.cs \
  Assets/Editor/WallTools/RaidBaseGenerator.cs \
  Assets/Editor/Regression/RaidArenaShapeRegression.cs
GATE_BRACE_SUMMARY bad=0 of 4     (exit 0)
```

Raw brace counts 23/23, 26/26, 293/293, 151/151. NUL bytes: 0 in all four.

## 5. The regression is RED right now, and that is the deliverable

`CaseArenaBoundary` step 4 reads each **baked** `RaidBase_*.unity` for `ArenaBoundary_Ring` and for
pieces on all four sides. On the current tree those scenes predate the ring, so the suite fails with
`has NO 'ArenaBoundary_Ring' - the arena is not enclosed` for all three configs. It goes green only
after `BuildAllRaidScenes`. A source-only lint would have passed the moment the code compiled and
proved nothing about the scenes the player loads.

## 6. What the lead runs, and what to read in the output

**Run order - the regression is red before the bake BY DESIGN, so it goes last:**

1. compile gate
2. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`
3. `DeNelle.Editor.RaidNavBake.BakeAll`
4. the regression suite

**The commit must carry the baked artifacts with the code:** the three `RaidBase_*.unity`, their
`RaidBase_*/NavMesh.asset`, and `Assets/Editor/ArenaBoundaryRing.cs.meta` (Unity writes that on first
import; it does not exist in this worktree). Committing the `.cs` alone leaves Case 6 RED on every
later gate, for every other lane.

**If the first bake prints `CLAMPED` or `*** GAP ***`:** that is a palette/tuning finding, not a
ticket fail - see WO sec.5 acceptance 3 for the ordered remedy (drop the thin `Rock_Pillar` from the
boundary palette first, raise `ArenaBoundaryMaxPerSide` second, re-bake, record the measured line).

Expected new line, once per config, in the shape the other rings log:

```
[RaidBaseGenerator] ring 'Arena': target +/-68.5m -> +/-68.5m, <n> piece(s)/side @ <s>m
  (piece <p>m, <g>m overlap), kit landscape-rock (0 watchtowers, <N> boundary pieces), gates=[none].
```

`<n>`, `<s>`, `<p>`, `<N>` are **derived from a runtime measurement of the rock meshes and are
deliberately not predicted here** - the polyperfect pack is gitignored and absent from this worktree,
so any number written here would be a guess (CLAUDE.md sec.11B). What IS asserted about the line:
`gates=[none]`, the gap clause reads `overlap` (not `*** GAP ***`), and no `CLAMPED` suffix.

Each config's `BUILT:` summary also gains `ARENA BOUNDARY +/-68.5m: <N> landscape piece(s), <n>/side
@ <s>m`.

**Must be unchanged** (the staging math never reads the boundary):

```
raider_camp_small   STAGING @ (0.00, 0.00, -51.20)
fortified_garrison  STAGING @ (-55.89, 0.00, -55.89)
mage_enclave        STAGING @ (-51.72, 0.00, -51.72)
```

**Must not appear:** any `ARENA BOUNDARY ASSERT` line. **Must still read 4/4:** `RaidNavBake` DONE line.

## 6b. STEP 2 - the first bake fired my own assert, and that is the finding

`Builds/wave3-bake3`: the ring built on all three configs (160 pieces, 40/side @ 3.43 m) **and**
`ARENA BOUNDARY ASSERT` fired on all three - inner face 62.8 m vs a 66.0 m staging clamp, from a
**measured 5.75 m inward reach**. Back-solved from the printed numbers, the palette is 2.24 m thin /
3.38 m wide at scale 1, so the requested 3.4x scale made an 11.5 m boulder. **No inset value could
have fixed it** (containing a 5.75 m reach outside 66.0 m needs a ring line at 71.75 m, off the 70 m
plane), so the chosen-inset design was wrong, not mistuned.

Fix: the ring line is now the **midpoint of the band** between the staging clamp and the edge
tolerance (`68.6 m`, half-band `2.6 m`), and the SCALE is fitted to the band rather than the band to
the scale. `StagingPlaneEdgeMargin` untouched, so the three authored staging positions cannot move.
Radial jitter is symmetric and band-bounded (it was outward-only at 1.2 m, which alone would have
blown the outward half-band). Full detail: WO sec.9b.

Two new hard reds in Case 6, per instruction: the builder renames its ring root to
`ArenaBoundary_Ring_CONTAINMENT_FAIL` on failure (step 4 reds on the SCENE), and
`CaseArenaBoundaryBakeLogs` (step 5) reds when a `Builds/*bake*` log carrying `ring 'Arena'` also
carries `ARENA BOUNDARY ASSERT`.

**Iron Bastion, coordinator option (b) taken - the case stays strict.** Entry point:
**`DeNelle.Editor.RaidBaseGenerator.BuildToNewScene`** (`RaidBaseGenerator.cs:323-331`) - public
static, parameterless, `NewScene` -> `Build()` -> `SaveScene` to
`Assets/Scenes/RaidBase_IronBastion.unity`. Safe headless; it recreates the scene from scratch, which
is how every raid scene is authored (`BuildSceneFor:355-364` does the same and the 09-10 log says
"into NEW scene" for all three). `RaidNavBake.BakeAll` already covers it in its 4/4. Case 6's failure
text names this entry point so the red carries its own remedy.

### Predicted numbers for the next bake (derived, so they are checkable)

From the measured palette (thin 2.24 m, wide 3.38 m): band half 2.60 m, fill 0.85 -> allowed
footprint 4.42 m -> applied scale **1.31** (from a 3.40 ceiling) -> piece 2.93 m, inward reach
2.21 m, jitter +/-0.39 m, wanted stride 2.05 m, 67 pieces/side, **268 pieces**, overlap 0.88 m.
Containment: inner face **66.39 m** > clamp 66.00 m (0.39 m slack); outer face **70.81 m** < edge
limit 71.20 m (0.39 m slack). These are a PREDICTION from the previous bake's printed values, not a
measurement - the new bake's own line is the authority.

## 6c. STEP 3 - the second bake built the ring exactly as predicted, and the assert was still wrong

`Builds/wave3-bake4` reproduced the prediction to the decimal (`reach 2.21m of 2.60m band, jitter
+/-0.39m`) and asserted anyway: **reach + jitter = 2.60 = the half-band exactly**, so the inner face
landed **exactly on** the 66.0 m staging clamp. Built in, not bad luck - the jitter bound was
"whatever room is left", so the faces always sit ON the band edges, and a midpoint ring line then puts
the inner face ON the clamp. Zero clearance is not a pass.

Fix (WO sec.9c): `ArenaBoundaryContainmentSlack = 0.3f`, **reserved by the jitter bound** and **named
in the assert's message**; `ArenaBoundaryBandFill` 0.85 -> 0.70 so fill + jitter + slack fit the
half-band; assert strict at both faces; Case 6 step 3 lints `slack > 0` and
`fill <= 1 - slack/bandHalf`. The ring line stays the band midpoint - the band is only 5.2 m wide, so
moving it would buy inner slack by spending outer slack, where reserving the margin gives **0.30 m at
both faces**.

### New expected containment numbers

```
applied scale 3.40 -> 1.08    piece 2.41m   maxPiece 3.64m   reach 1.82m   jitter +/-0.48m
inner face 66.30m  vs staging clamp 66.00m  = 0.30m slack  (required 0.30m)
outer face 70.90m  vs edge limit    71.20m  = 0.30m slack  (required 0.30m)
82 piece(s)/side @ 1.67m stride, 0.74m overlap, 328 boundary pieces
```

A PREDICTION from the previous bakes' printed mesh sizes (thin 2.24 m, wide 3.38 m at scale 1), not a
measurement. The containment line to look for is
`arena boundary containment for '<id>': ... both clear the required 0.30m`.

⚠ **Delete or move `Builds/wave3-bake3` AND `wave3-bake4`, and re-bake the three scenes** - they
currently carry `ArenaBoundary_Ring_CONTAINMENT_FAIL`, so Case 6 steps 4 and 5 are both red until a
clean bake replaces them. Pins working as designed.

## 7. Open, with a default already applied

Palette = **rocks only** (the siege venue's `OuterBoundary_Ring` verbatim) at scale 2.2-3.4.
`ArenaBoundaryRing.TreePaths` is exported, so a mixed rock+tree edge is a one-line call-site change if
the owner prefers a treeline. Not a blocker.

## 8. Unproven from here

- The rock meshes were never measured (pack absent from this worktree), so piece footprint, stride,
  count AND the containment inner face are derived at bake time and reported by the log, not claimed
  here. The regression's containment check is deliberately only the FLOOR bound (it prices a piece at
  `scaleMax x 1 m`, because an asset-lint cannot open a mesh) and its failure text says so; the
  authority is `AssertBoundaryContainsStaging`, which uses the measured widest piece.
- Whether `Defenders/Art/Fix Polyperfect URP Materials` has been run on the bake machine was not
  checked. This is the first time the polyperfect pack appears in a raid base (~300 instances per
  scene); the bake PNG is the only proof there is no magenta.
  `RaidWallMaterialRegression` was read: it lints the three WALL FBXes and their `.mat` only, does not
  walk scene text, so the new instances are outside its scope and it cannot catch this.
- `RaidBaseDresser` `Zone_Approach` prop extents were not read - a prop past ~66 m would sit inside
  the ring. Cosmetic; PNG settles it.
- `RaidBase_IronBastion.unity` will NOT gain a ring from this run: `BuildAllRaidScenes` only bakes
  scene-configs and Iron Bastion is not one (it is parked by WO-1607 sec.1). The code path is wired in
  `BuildIronBastion` for whenever that menu item is next run. Do not look for it in the bake log.
- Corner pieces may straddle the 70 m plane edge by up to `radialJitter` (1.2 m). Static geometry, not
  walkable; settled by the bake screenshot, not by this document.
- Nothing here has been compiled. The brace/NUL gate is a syntax floor, not a compile.
