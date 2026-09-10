# WO-1637 RESULT - all three ruled axes moved, edit-only

**Status:** IMPLEMENTED - awaiting bake + device frame
**Lane:** RAID-ART-2, 2026-09-10
**Rebased onto:** `e4b5906a541c65fc12255a109b5dc3723e328488` (`git merge --ff-only refs/heads/dev`,
fast-forward from `f5d39acd1`; `8e170b555` confirmed an ancestor, so both WOs' OWNER RULING sections
were in the tree). Every line number and every measurement below was taken AFTER that rebase.
**No Unity was run, no bake, no gate, no commit** - per the brief, the lead bakes
(`BuildAllRaidScenes` + `BuildToNewScene` + `RaidNavBake`) and takes the device frame.

---

## 1. The ruling, and what landed for each half

Owner ruling 2026-09-10 12:07: **ALL axes move.**

| Axis | Landed in | Value |
|---|---|---|
| 1. Fog first - end past the ring, density down | `RaidBaseDresser.DressAtmosphere`, hexagon-green branch | start **22 -> 45 m**, end **95 -> 200 m**; colour and ambient UNCHANGED |
| 2. Base wall height / mass | `DefaultWall("hexagon-green")` **and** `scene-configs.json` `raider_camp_small.raidDress.wallModule` | `barrier` -> **`wall`** (iron_bastion) / **`wall_broken`** (raider_camp_small) |
| 3. Ring material, BOTH venues | `ArenaBoundaryRing.RockPaths` (+ the root it resolves against) | the pale-tan `Stones_M` trio -> a **darker ruined-masonry trio** |

Files touched (4). `RaidBaseGenerator.cs` was **NOT** touched - it needed no change.

```
 Assets/Editor/ArenaBoundaryRing.cs                        | 85 +++++++++-----
 Assets/Editor/WallTools/RaidBaseDresser.cs                | 54 ++++++++-
 Assets/Resources/Data/Canonical/scene-configs.json        |  2 +-
 Assets/StreamingAssets/Data/Canonical/scene-configs.json  |  2 +-
```
(`git diff --numstat`: 85/17, 54/3, 2/2, 2/2. The JSON rows are shared with WO-1638 - see sec.6.)

---

## 2. THE MEASUREMENT THAT EARNED THE EDIT (CLAUDE.md sec.12 / sec.11B)

Static prefab reading was already done by the Step 1 pass (WO-1637 sec.11) and the lead's bake trace
(sec.12). **This lane added the one measurement neither of those could make: what the PLAYER saw.**

`Builds/device-frames/2026-09-10_0614_arena_06_wide.png` - build 363529, Seeker, 2670x1200 - sampled
directly. Luminance is `0.2126R + 0.7152G + 0.0722B` over the **sRGB-encoded** pixel values, i.e.
exactly what a desaturated copy of that PNG shows. That is the owner's gate, because she is
colourblind (memory `owner-colorblind-delegate-visual-creative`).

| region sampled (px box) | n | mean RGB | **meanLum** | p10 | p50 | p90 |
|---|---|---|---|---|---|---|
| sky, high (1900,120)-(2400,260) | 7849 | (113,127,146) | 0.491 | 0.460 | 0.478 | 0.511 |
| **sky, just above the ring** (1750,520)-(2350,600) | 5400 | (155,176,195) | **0.677** | 0.583 | 0.692 | 0.772 |
| **BOUNDARY RING band** (1560,625)-(2560,680) | 6346 | (169,173,156) | **0.670** | 0.462 | 0.595 | 0.901 |
| base wall (barrier) (1640,735)-(2500,775) | 4018 | (92,100,105) | 0.386 | 0.222 | 0.426 | 0.468 |
| ground, mid field (1700,830)-(2400,900) | 5616 | (103,92,71) | 0.363 | 0.124 | 0.435 | 0.451 |
| flag_green cloth (1966,625)-(2000,700) | 300 | (74,110,54) | 0.384 | 0.295 | 0.401 | 0.439 |
| spire (800,340)-(940,560) | 3478 | (126,129,108) | 0.498 | 0.337 | 0.471 | 0.706 |

> ## ⛔ THE RING AND THE SKY DIRECTLY ABOVE IT DIFFER BY **0.007**.
> Not "hard to see" - in greyscale the ring has **no top edge at all**. That single number is the
> ticket, it is measured on the shipped build, and it is the number the post-bake frame has to beat.

**Not proven, and named as such:** the p90 of the ring band (0.901) is sky showing between pieces and
the p10 (0.462) is shadowed rock, so the band mean mixes rock and sky. The medians (0.595 ring vs
0.692 sky, delta 0.097) are the rock-dominated reading. Both are stated; neither is flattering.

---

## 3. GREYSCALE RATIONALE - why each axis moves, in luminance

Swatch luminances, each read from the `.mat` / `.png` at source this session (same sRGB formula):

| swatch | where | lum |
|---|---|---|
| `M_14_Brown_lightest_LPUP` | today's ring, all three prefabs, no `_BaseMap` at all | **0.763** |
| `M_20_Grey_LPUP` | the new ring's dominant swatch | **0.514** |
| `M_21_Grey_Light_LPUP` | the new ring's second swatch (pillar caps/bands) | **0.636** |
| fog colour (0.66, 0.58, 0.42) | `DressAtmosphere` hexagon-green | **0.585** |
| raid ground (0.18, 0.14, 0.09) | `RaidNavBake.GroundColorFor`, the raider-camp branch | **0.145** |
| `dungeon_texture` over `wall_broken` UVs | the new base wall | **0.496** |
| `dungeon_texture` over `barrier` UVs | today's rail | 0.475 |

> ### ⚠ MEASURE FROM THE CAMERA, NOT FROM THE ARENA CENTRE.
> Unity's linear fog is a function of distance to the **camera**. WO-1637 sec.1d priced the ring at
> "68.6 to 97 m" - those are distances from `(0,0,0)`. The hero deploys at `RaidStagingPoint`
> `(0, 0, -51.2)` facing north, so the arc he is looking at is much further away:
>
> | ring feature | from centre | **from the deploy seat** |
> |---|---|---|
> | north side midpoint | 68.6 m | **119.8 m** |
> | E/W side midpoints | 68.6 m | **85.6 m** |
> | north corners | 97.0 m | **138.0 m** |
> | south side (behind the hero) | 68.6 m | 17.4 m |
>
> This **strengthens** the ticket rather than weakening it, and the corrected numbers are now in the
> code comment: under 22/95 **every forward distance is past the 95 m end**, so the entire visible arc
> rendered at a flat **100% fog**. It did not merely blend toward the fog colour - it *was* the fog
> colour. (An earlier draft of this section, and the code comment, said "15% at its nearest and 34% at
> its corners" from centre-relative distances. Wrong as stated; corrected in both places.)

**Deriving the lit albedo from the frame instead of guessing at it.** Solving the ring's measured
median against 100% fog gives the fog colour itself, 0.585 - and the measured median is **0.595**, a
1.7% agreement that independently validates the "fully saturated" reading. For the *unfogged* albedo,
solving at the E/W arc (85.6 m, 87% fog under the old curve) gives a lit albedo **A ~ 0.635** for a
0.763 material - a light factor of **0.83**. That factor is the one assumption carried forward, and it
is named as such.

| scenario | the visible ring arc renders at | vs sky 0.692 |
|---|---|---|
| today (M_14, fog 22/95) | **0.585** - saturated | 0.107, and the frame measures 0.595 |
| **material moved ONLY** (M_20, fog 22/95) | **0.585 - IDENTICAL** | 0.107 - **zero effect** |
| **fog moved ONLY** (M_14, fog 45/200) | 0.611 | 0.081 - ***worse*** |
| **both, as shipped here** | **0.504 - 0.522** | **0.170 - 0.188** |

> ## THIS TABLE IS WHY "ALL AXES" WAS THE ONLY RULING THAT WORKS.
> Move the **material** alone and *nothing whatever* changes - at 100% fog the albedo contributes
> zero. Move the **fog** alone and it gets **worse**, because lifting the fog restores the pale swatch
> the fog was hiding. Only the pair separates the ring from the sky. The two axes are not additive;
> each one is worthless without the other.

**And the bottom edge, which is the half nobody had named.** Under the old fog the GROUND at those same
forward distances is also saturated at 0.585 - so the ring and the ground beneath it landed on
**exactly the same value, delta 0.000**. The ring had no bottom edge either. After the change the
ground reads ~0.28-0.33 against a ring at ~0.50-0.52: **delta 0.19-0.22**. The ring gains a silhouette
on both sides.

**And the internal variation.** "One flat untextured swatch" was half the original finding. The new
palette carries **two** values (0.514 and 0.636), so the ring has shading inside itself, not just an
outline.

---

## 4. THE RING PALETTE - measured before it was swapped, because it can silently tear the ring open

`ArenaBoundaryRing.RockPaths` is not a cosmetic list. `PlaceSquarePerimeter` derives its stride from
the **thinnest** piece in the palette and band-fits the **widest**, so a bad thin/wide ratio drives the
per-side count past `maxPerSide`, the clamp widens the stride, and `WorstGap` goes **positive** - open
ground in a playable boundary, which is a WO-1632 pin.

**Method + control.** A binary-FBX parser was written to read vertex extents, with the importer's unit
conversion applied (`UnitScaleFactor/100 * scaleFactor` when `useFileScale`), times every transform
scale in the prefab. **It reproduces the 2026-09-10 bake log exactly** - that log said
`min 2.24 / max 3.38 / applied 1.08 / piece 2.42 / stride 1.67 / 82 a side / 0.74 m overlap`, and so
does the script. The method is validated, not asserted.

**The pack was swept by PROPERTY, not by name** (memory `search-by-token-not-by-name`): every prefab
in `_M/Prefabs_M/**` whose materials ALL sit in luminance 0.15-0.56, chunky enough not to open gaps,
pivot at its base.

> ### THE FINDING THAT SHAPED THE ANSWER: **polyperfect has no dark NATURAL rock at boulder scale.**
> All 23 prefabs in `Nature_M/Stones_M` bind the SAME single swatch, `M_14_Brown_lightest_LPUP`, with
> **no `_BaseMap` at all**. There was no darker boulder to pick. Every darker stone in the pack is a
> dungeon / ruin piece at roughly a third the size, and the chunky dark pieces that do exist are
> statues, Egyptian pillars and haybales. So the palette is **ruined masonry** - which also happens to
> suit this camp's own fiction (`raider_camp_small.description`: *"Scavengers strip an abandoned
> settlement the Heart can no longer reach."*).

| | old palette | **new palette** |
|---|---|---|
| entries | `Stones_M/Stone_Large`, `Rock_Pillar`, `Stone_Medium_Flat` | `Fantasy_M/Rubble_Stone`, `Dungeon_Pillar_Stone_Round`, `Dungeon_Pillar_Stone_Square` |
| materials | `M_14_Brown_lightest` (0.763) x3 | `M_20_Grey` (0.514) + `M_21_Grey_Light` (0.636) |
| measured XZ (m) | 3.01x3.38, 2.24x3.05, 2.58x2.99 | 1.56x1.61, 0.78x0.78, 0.80x0.80 |
| measured height (m) | 3.71, 6.77, 1.33 | 0.53, 3.02, 3.12 |
| pivot minY (m) | -0.87, -0.21, -0.58 | -0.14, **+0.09**, 0.00 |
| palette min / max | 2.24 / 3.38 | 0.78 / 1.61 |
| band-fitted scale | 1.08 | **2.26** |
| widest piece / inward reach | 3.64 m / **1.82 m** | 3.64 m / **1.82 m** (identical) |
| piece footprint | 2.42 m | 1.72 m |
| stride | 1.67 m | 1.39 m (per-side count clamps at 100) |
| **WorstGap** | **-0.74 m (overlap)** | **-0.33 m (overlap)** |
| heights at applied scale | 4.0 / 7.3 / 1.4 m | 6.8 / 7.1 / 1.2 m |
| pieces placed | 328 | ~396 (4 x 99) |

**Continuity holds** - the clamp binds, but the piece (1.72 m) is still WIDER than the clamped stride
(1.39 m), so `WorstGap` stays negative and the ring is continuous by construction. **Containment is
byte-identical**: the band fit clamps the widest piece to the same 3.64 m either way, so `InwardReach`
is still 1.82 m of the 2.60 m band and `AssertBoundaryContainsStaging` sees exactly what it saw before.

**Two things NOT proven, stated as such:**
- `Dungeon_Pillar_Stone_Round`'s pivot is **+0.09 m** (+0.20 m at the applied scale), so a third of the
  ring floats ~20 cm. `PlaceSquarePerimeter` sets `y = 0` and does **not** `SeatOnGround`. At 68-97 m
  this is sub-pixel; **at the corners it has not been seen**. The bake frame settles it.
- The ring will read as **ruins**, not as boulders. That is a deliberate consequence of the pack having
  no dark boulder, and it is the owner's to reject on the frame.

**The re-root, and why it was unavoidable.** `NatureRoot` (`.../Prefabs_M/Nature_M/`) made everything
outside `Nature_M` unreachable - and `Nature_M` is exactly the folder with only the one pale swatch.
It is now `PrefabRoot` (`.../Prefabs_M/`) and every entry carries its own theme folder; `TreePaths`
gained the `Nature_M/` prefix and is otherwise untouched. The const is referenced **only inside
`ArenaBoundaryRing.cs`** (grepped across `Assets/`, 2026-09-10) and appears in **no** regression lint,
so the rename reaches nothing else. All seven resulting paths were listed on disk before the edit.

---

## 5. REACH - which camps and which battle-arena venue each change touches

| change | raider_camp_small | iron_bastion | fortified_garrison | mage_enclave | battle arena (siege venue) |
|---|---|---|---|---|---|
| **fog 22/95 -> 45/200** | **YES** | **YES** | no (`synty-castle` branch, 28/115) | no (`dungeon-stone` branch, 18/88) | no - `ProceduralSiegeArenaBuilder` never touches `RenderSettings` |
| **wall `wall_broken`** (JSON) | **YES** | no | no | no | no - the siege venue has no clad ring |
| **wall `wall` (DefaultWall)** | no (it authors its own) | **YES** | no (`SM_Bld_Castle_Wall_01`) | no (`wall_cracked`) | no |
| **ring palette `RockPaths`** | **YES** | **YES** | **YES** | **YES** | **YES, in TWO places** |

**The battle arena's siege venue, named precisely** (`ProceduralSiegeArenaBuilder.cs:79-80` delegates
`RockPaths` to this array): the **56 m Rock cover ring** (16 pieces, scale 0.9-1.6, `:150`) and the
**72 m `OuterBoundary_Ring`** (40 pieces, scale 1.4-2.2, `:157`). The owner ticked "both venues".

> ### ⚠ A CONSEQUENCE FOR THE SIEGE VENUE THAT IS REAL, QUANTIFIED, AND DELIBERATELY NOT FIXED HERE.
> `PlacePolarRing` has **no band fit** - it applies the raw scale to the raw mesh. So the siege venue
> does not get the raid ring's 2.26x compensation:
> - **72 m boundary**: 40 pieces around a 452 m circumference = **11.3 m apart**. Old piece width at
>   scale 1.8 was ~6.1 m (a 5.2 m gap); the new pillar is ~1.4 m (a **9.9 m gap**). Materially sparser.
> - **56 m cover ring**: old ~4.2 m wide x 4.6 m tall cover; new ~1.0 m wide x 3.8 m tall.
>
> This is **not speculation** - it follows from the measured meshes and the two call sites. It is
> reported and not fixed because the ruling was "both venues, and if the siege venue then reads wrong
> it is its own ticket". **Recommend minting that ticket now**: the fix is a per-call `count`/`scale`
> bump at `ProceduralSiegeArenaBuilder.cs:150` and `:157`, or a band fit for `PlacePolarRing`.

> ### ⚠ "BOTH VENUES" IS HALF-PENDING - AND THE REASON IS BETTER THAN EXPECTED. THERE IS NO SIEGE SCENE.
> **`Assets/Scenes/SiegeArena.unity` DOES NOT EXIST**, and has never existed. Checked four ways
> 2026-09-10: absent from `git ls-files Assets/Scenes/*.unity`; absent from disk in **both** this
> worktree and the main tree; **not** in `.gitignore`; and `git log -- Assets/Scenes/SiegeArena.unity`
> returns **nothing at all**. It is also **not registered in `ProjectSettings/EditorBuildSettings.asset`**,
> and **no runtime `.cs` references it** - every `SiegeArena` hit under `Assets/` outside the builder
> itself is a comment or a regression's source-path string.
>
> **So the `RockPaths` change reaches the siege venue the first time anyone bakes it, and there is no
> stale tan-ring artifact to be inconsistent with.** The quantified sparseness below is a warning
> about that FUTURE bake, not a defect shipping today. That is the accurate statement of "half-pending".
>
> ⚠ **A canon-in-code inaccuracy this exposes, flagged not fixed** (I was scoped to no further code):
> `ArenaBoundaryRing.cs:21` and `:181` both assert *"SiegeArena.unity's saved layout is reproduced
> exactly by the same seed, so moving this code re-bakes nothing"* - written as though that scene
> exists. It does not. The RNG-order claim is still true and still worth keeping; the sentence should
> stop naming a saved scene that was never saved.

**THE BATCH ENTRY POINT THE LEAD ASKED FOR - it exists.**

```
-executeMethod DeNelle.Editor.ProceduralSiegeArenaBuilder.BatchBuildAndBakeSiegeArena
```

`Assets/Editor/ProceduralSiegeArenaBuilder.cs:232`, and that exact `run-unity-method.ps1` invocation
is written into its own header at `:229-230`. It is a **full** headless entry, not a venue-only stub:
it creates a fresh empty scene, calls `BuildVenue()`, adds and bakes a `NavMeshSurface` by reflection,
saves to `Assets/Scenes/SiegeArena.unity` (`:57`), registers it in Build Settings and saves assets.
There is also an interactive `[MenuItem]` at `:91-92`, `Defenders/Scenes/Build Siege Arena`
(`BuildSiegeArena`, `:49`), which builds the venue **without** the navmesh bake or the save - **use the
Batch one.**

⚠ **It is destructive by design:** `EditorSceneManager.NewScene(EmptyScene, Single)` means it
**replaces** that scene wholesale rather than updating it. Harmless here precisely because the scene
does not exist yet and nothing else lives in it.

---

## 5b. THE SIEGE BAKE RAN, AND IT RED THE POSTURE SEAM - the red was RIGHT, and the fix is NOT to classify

**Chain 41, `Builds/wave9-reg1`, read at source 2026-09-10:**

```
posture seam FAILED (1): 'SiegeArena' is in the build list but NO HubScenes predicate names it
(SceneKind.Unknown, resolves calm(explore)). Classify it in HubScenes.Classify and give it an
expectation here - do not delete this check to go green.
```

The obvious move is to classify it. **That would have been the wrong fix, and the seam's own last
clause is the clue: it says "do not delete this check", not "the scene must be classified".** What the
red actually caught is a scene entering the shipped build list **with no loader at all**.

**Evidence, all gathered this session:**

| question | answer | how |
|---|---|---|
| Is it loaded by NAME? | **No** | `grep -rn "SiegeArena" --include=*.cs Assets/` - outside `ProceduralSiegeArenaBuilder.cs`, every hit is a comment or a regression's source-PATH string |
| Is it loaded by BUILD INDEX? | **No, and nothing in the game is** | no `LoadScene(<int>)` / `buildIndex` / `GetSceneByBuildIndex` anywhere under `Assets/_Modules/` - every load is by name, so build-list ORDER carries nothing |
| Does `ArenaMode` load it? | **No** | `ArenaMode.cs` contains **no** `SceneManager` call at all. Its own header: spawn the opponent's base *"at a raid anchor near the hero"* by reusing `EnemyOutpost` + `OutpostFoundationGenerator.Realize` - **in the CURRENT scene** |
| Then why did the builder say otherwise? | it is an **aspiration**, never wired | `ProceduralSiegeArenaBuilder.cs:97`, *"ArenaMode ports the castle onto..."* |
| Who put it in the list? | **the bake itself** | `EnsureInBuildSettings()`, called from `BatchBuildAndBakeSiegeArena` |

> ## ⛔ SO THE BAKE'S REGISTRATION IS THE DEFECT, AND THAT IS WHAT WAS FIXED.
> Registering it shipped an **unloadable scene in every APK** and forced every seam oracle to invent a
> posture for ground no player can stand on. Classifying it would have made the red go away while
> leaving both of those in place - and would have written a guess ("what kind of scene is this?")
> into `HubScenes` for a scene the game never enters.
>
> **`HubScenes.Classify` was NOT touched. `ScenePostureSeamRegression` was NOT touched.** No oracle was
> weakened; the check stays exactly as strict as it was.

**The change** (`Assets/Editor/ProceduralSiegeArenaBuilder.cs`): the `EnsureInBuildSettings()` call in
`BatchBuildAndBakeSiegeArena` is commented out, with the full reasoning and the exact restore
condition written at the call site; the method itself is **kept, not deleted**, carrying a doc comment
that says it is uncalled on purpose and **must not be called to make a red go away**; and the batch
log line now states that the venue was deliberately not registered. **Building and baking the venue is
still correct** - the scene file is what the bake produces and WO-1637's ring palette lands in it
either way. Only the build-list entry was wrong.

> ### ⚠ ONE THING THE LEAD MUST DO IN THE MAIN TREE - I CANNOT REACH IT FROM THIS WORKTREE.
> The bake ALREADY ran there, so the registration is on disk **now**:
> `D:/EoA/ProjectSettings/EditorBuildSettings.asset:108` -> `path: Assets/Scenes/SiegeArena.unity`
> (read at source). My code change only stops FUTURE bakes from re-adding it. **That line has to be
> removed from `EditorBuildSettings.asset` as well, or the seam stays red.** Removing it is safe:
> nothing loads the scene, and no load anywhere uses a build index, so no other scene's index matters.
> `D:/EoA/Assets/Scenes/SiegeArena.unity` itself should **stay** - it is the baked venue.

> ### ⚠ A SECOND REACH FINDING, RECORDED FOR THE LEAD AND DELIBERATELY NOT FIXED: `fortified_garrison`.
> Its baked fog is **(0.58, 0.55, 0.50)**, whose greyscale luminance is **0.578** - and the ring's new
> dominant swatch `M_20_Grey_LPUP` is **0.514**. Those are **0.064 apart**. Its fog runs **28-115 m**,
> and from a deploy seat its far ring arc sits well past 115 m, so that arc renders at or near **100%
> fog** - i.e. **the same saturation trap this ticket just fixed on raider_camp_small, except that the
> new grey palette is now ALSO nearly the same value as that camp's fog colour.** The old tan swatch
> (0.763) at least differed from it by 0.185.
>
> **Net: the ring change may make `fortified_garrison` slightly WORSE, not better.** Its fog is on the
> `synty-castle` branch, which this ruling did not touch, so I did not touch it. **The fix is the same
> shape as the one that worked here** - push that branch's `fogEndDistance` past its own ring arc and
> lift the start - and it is a one-line change once the owner rules. **Look at that camp's frame on the
> same bake; do not assume the raider-camp win generalises.**

**A third, on the reach table's `iron_bastion` column:** those two YES cells are **conditional on the
bake proving that scene's entry point calls `Dress` at all.** WO-1637 sec.11.2 established that
`RaidBase_IronBastion.unity` carries **Unity default lighting** on disk (fog OFF, ExpSquared, 0.5 grey)
while `DressAtmosphere` would set `fog = true` unconditionally for its kit - so that scene did not come
from a `Dress`-running bake of the current code, and nothing static says why. The new `ATMOSPHERE` line
settles it: if it prints for `iron_bastion` the scene was simply stale; if it never prints, that entry
point never calls `Dress` and **neither the fog nor the `DefaultWall` change reaches that camp at all.**

> ### ⚠ A CONSEQUENCE AT THE GATEHOUSE THAT THIS LANE CREATES BUT MAY NOT FIX.
> The base wall goes from 1.10 m to ~3.8 m. The things standing beside it do **not**:
> `wall_straight_gate` is **2.00 x 1.41 m** and `PlaceGatehouse` applies **no** scale, dropping it into
> an **8.55 m** opening (`outer.GateWidth`); the `GateFlank` towers are `building_watchtower_green`
> from the same miniature hex kit. Beside a 3.8 m wall that cluster will read as a doll's gate in a
> giant's wall, and the flanking towers will now be SHORTER than the wall they flank.
> **Pre-existing** (the gate never filled its opening - that is why the frame shows a wide gap), and
> `PlaceGatehouse` is pinned **read-only** by WO-1638 sec.6, so it is deliberately not touched here.
> **The lead must look for it on the post-bake frame, and it deserves its own ticket:** either scale
> the gate to its opening or pick a gate module at wall scale.

---

## 6. Pins - opened, checked, kept

- **`RaidArenaShapeRegression.CaseArenaBoundary`** is a SOURCE-TEXT lint. Every token it requires was
  counted in the post-edit tree: in `RaidBaseGenerator.cs` - `ArenaBoundaryHalfExtent` (10),
  `ArenaBoundaryRing.PlaceSquarePerimeter` (1), `ArenaBoundaryRing.RockPaths` (1),
  `AssertBoundaryContainsStaging` (3), `boundary.InwardReach` (3), `ArenaBoundaryBandHalf` (4),
  `ArenaBoundaryContainmentSlack` (12), `ArenaBoundaryContainmentHeadroom` (5),
  `ArenaBoundaryContainmentEpsilon` (4), `gates=[none]` (1); in `ArenaBoundaryRing.cs` -
  `PlaceSquarePerimeter` (4), `PlacePolarRing` (2), `MeasureMinFootprint` (1); in
  `ProceduralSiegeArenaBuilder.cs` - `ArenaBoundaryRing.PlacePolarRing` (2). **Nothing re-pointed,
  nothing deleted** - the edits are value-only plus comments.
- **`RaidBaseLayoutRegression`** source-lints the dresser: `Zone_Gatehouse` (1), `Zone_Courtyard` (1),
  `GarrisonSlot_` (5), `def.raidDress.props` (3), `AssetRoots.StructureContent` (1),
  `MinGateWidth = 3.5f` (1) all present; the two FORBIDDEN tokens `DefaultProps` and `def.props` are
  both at **0** (WO-1635's one-prop-authority pin holds).
- **WO-1632's ring geometry**: every constant in WO-1637 sec.7 is untouched. `InwardReach` is
  arithmetically identical (sec.4).
- **WO-1619's spire height**: not touched. No spire code, scale or prefab was changed.
- **`MagentaGuard` / `ProtectPrimitiveArt`**: read-only, untouched.
- **The `ArenaBoundaryRing` FALLBACK path and its explicit tint** (`:342-366` pre-edit): untouched and
  still reachable - it is the `LogWarning`-never-fail branch the ruling said to keep. **No constructed
  material was introduced anywhere**; every ring piece is still `PrefabUtility.InstantiatePrefab` of a
  vendor prefab carrying the vendor's own material.
- **Canonical JSON**: edited in **BINARY**. LF count **361 unchanged** in both mirrors, CRLF 361, NUL 0,
  length 20451 -> 20453 (+4 for `barrier`->`wall_broken`, -2 for `flag_green`->`flag_red`), both mirrors
  **byte-identical** (sha256 `c28a82303c836c60...`), both re-parsed as JSON.
  ⚠ **`entranceCount`, the gate token and `PlaceGatehouse` were NOT touched.**

---

## 7. Open ownership conflict the lead must sequence

`scene-configs.json`'s `raidDress.props` array **belongs to WO-1634** (WO-1638 sec.8). WO-1634's own
file still reads `READY TO IMPLEMENT` while `WORK_ORDER_1634_...RESULT.md` reads `IMPLEMENTED -
awaiting gate + bake` - i.e. **its Status flip was left behind**, which is the exact pattern
CLAUDE.md sec.11 was hardened against. This lane's touch on that array is **one token on one row**
(`flag_green` -> `flag_red`, WO-1638's ruling), so a merge is trivial - **but if a WO-1634 lane is
still live in another worktree, the lead must sequence the two rather than let them race.**

---

## 8. What is NOT done

- **No bake, so ZERO of this has been seen.** Acceptance items 1-6 of WO-1637 are all OPEN. The lead
  runs `BuildAllRaidScenes` + `BuildToNewScene` + `RaidNavBake` and takes the device frame.
- **No compile gate** (single Unity seat). `python tools/gate_brace.py` reports
  `GATE_BRACE_SUMMARY bad=0 of 2`; raw braces balance 30/30 and 144/144; NUL bytes 0; no BOM; the
  per-file newline shape is preserved. **That is a brace proof, not a compile proof.**
- **The coverage-gap rule WO-1637 sec.7 proposes** - red when `fogEndDistance < ringRadius` - was
  **NOT written**. `LayoutOracle.cs:17-20` requires a new rule be SEEN RED first, which needs the Unity
  seat this lane does not hold. **Note that this change would make that rule GREEN (200 > 97), so it
  must be authored against the PRE-change values or it can never be seen red.** Raised, not done.
- **Not proven: what the ring reads as after the bake.** Every number in sec.3 past the measured frame
  is a model with its assumption named. **The device frame is the gate, in colour and in greyscale.**

---

## 9. What the lead should look for on the post-bake log

```
[Flow:RaidBase] ATMOSPHERE 'raider_camp_small' kit=hexagon-green fog=ON mode=Linear
                colour=(0.660, 0.580, 0.420) start=45.0m end=200.0m ambient=(0.420, 0.360, 0.260)
[Flow:RaidBase] MAT ArenaBoundary (boundary ring) prefab='...Fantasy_M/Rubble_Stone.prefab'
                ... mat='M_20_Grey_LPUP' ... _BaseMap=NULL _BaseColor=(0.533, 0.533, 0.533)
[Flow:RaidBase] MAT base wall token='wall_broken' kit=hexagon-green radius=31.0m
                prefab='...KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/wall_broken.fbx'
[RaidBaseGenerator] ring 'Arena': ... piece 1.72m, 0.33m overlap CLAMPED at 100/side, ...
                reach 1.82m of 2.60m band ...
```
**`CLAMPED at 100/side` is EXPECTED here and is not a failure** - the overlap stays negative. The line
that WOULD be a failure is `*** N.NNm GAP ***` plus the builder's non-continuous `LogWarning`. And an
`ATMOSPHERE 'iron_bastion'` line either appears (that scene was simply stale) or does not (its entry
point never calls `Dress`) - WO-1637 sec.11.2 left that open and this bake settles it.
