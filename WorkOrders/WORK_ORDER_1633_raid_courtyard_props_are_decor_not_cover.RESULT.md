# WO-1633 RESULT - raid courtyard cover rings, colliders, and a props count in the log

**Lane:** RAID-POLISH, 2026-09-10
**Status handed back:** IMPLEMENTED - awaiting gate + bake. **EDIT-ONLY: no Unity run, no bake, no
gate, no commit was performed by this lane.** Every claim below is a source/data read or a replayed
computation, never a gate marker.
**Base:** worktree `.claude/worktrees/agent-a587cc193e1950ad3`, ff-merged to `dev` at **c10e4f5d1**.

---

## 1. The finding that re-scoped the ticket

The assignment (and `WORK_ORDER_1607_raid_bases_as_places.md:47`, and the generator header
`RaidBaseGenerator.cs:35`) said the `props` seam was authored empty and unread, and asked for a prop
dresser to be built. **It already exists and works.** Proven, not inferred:

- `RaidBaseDresser.ScatterProps` reads `def.raidDress.props` first (`:534-539` pre-change).
- All three raid rows in `Assets/Resources/Data/Canonical/scene-configs.json` author it - **27 / 25 / 27**
  prop instances for Easy / Hard / Extreme.
- All 27 distinct tokens resolve, verified by replaying `LoadVisual`'s exact search order
  (`KayFolders` -> `SyntyFolders` -> `StructureContent`) against the on-disk packs. Zero misses.
- `Builds/wave2-bake2` agrees: `[Flow:RaidBase] dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0`,
  `... 'fortified_garrison' kit=synty-castle placed=489 missing=0`,
  `... 'mage_enclave' kit=dungeon-stone placed=590 missing=0`.

Building the assignment as written would have stacked a second dresser on a working one. The ticket
was re-scoped to the three things that ARE unbuilt (section 2), and the stale sentences were routed
to **WO-1635** rather than silently patched.

## 2. What changed

| # | Defect | Fix |
|---|---|---|
| 1 | `RaidBaseDresser.cs:567` passed `stripColliders: true` for **every** prop - all cover was walk-through scenery, against WO-1609:104 / WO-1610:108 / WO-1610:112 / WO-1607 section 6 | `stripColliders: !p.cover`, driven by a NEW authored `bool cover` on `RaidDressPropDef`. Cover props also get `EnsureCoverCollider` - a bounds-fitted `BoxCollider` when the source FBX ships with none |
| 2 | `PropSlot :613-628` put every courtyard prop of every camp on ONE annulus at evenly spaced angles, no jitter, no scale variance - against WO-1609:99 "clusters, not a ring of singles" | `PlaceCourtyardCluster` - each authored entry becomes a CLUSTER anchored on one of 1-3 concentric cover rings across the whole courtyard band, with seeded jitter and a scale roll, via the new shared `CoverRingPlacer` |
| 3 | No bake log ever stated a props count; the only dresser line is an aggregate that also counts clad panels, floor tiles and the gatehouse | `Debug.Log($"[RaidBaseDresser] props '{def.id}': {placedProps} placed (set=...)")` plus a `FlowTrace.Step` carrying rings / band / lane / turrets-avoided |

Plus the exclusions acceptance 3 + 4 asked for, all **read out of the built tree**, never hardcoded:
`BuildKeepout` collects every `DefenseTower` position and the `RaidStagingPoint` marker
(`RaidBaseGenerator.cs:189`); `AcceptPropSlot` rejects the gate->spire corridor, the spire footprint,
a 3.5 m pad round each turret and a 10 m pad round staging. Staging is on the **south-west diagonal**
for Hard and Extreme per `Builds/wave2-bake2`, which is exactly why it is read rather than assumed.

`PushOutOfLane` pushes a cluster that lands in the corridor to the corridor EDGE instead of dropping
it - WO-1610:111 wants cover "along the sides of the march", WO-1610:46 wants the yard to be "a fight,
not a runway", and dropping would satisfy the first while breaking the second.

**Owner's named precedent honoured as reuse, not a copy:** `CoverRingPlacer` is the reusable half of
`ProceduralSiegeArenaBuilder.PlaceCoverRing` (`:207-224`) extracted into its own file. The arena file
is **not touched** - it can adopt the helper later without a third copy of the math.

## 3. Files changed

| Path | Change |
|---|---|
| `Assets/Editor/WallTools/CoverRingPlacer.cs` | **NEW** - pure polar ring / cluster math (`RingCount`, `BandRadius`, `PolarPoint`, `ClusterAnchorAngle`, `Jittered`) |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | courtyard knobs + `PropKeepout` + `BuildKeepout` / `AcceptPropSlot` / `PushOutOfLane` / `PlaceCourtyardCluster` / `PlaceZoneProps` / `EnsureCoverCollider`; `ScatterProps` rewritten; `DefaultProps` marked TGVRU-only and given `cover` |
| `Assets/_Modules/Village/World/SceneConfigCatalog.cs` | `RaidDressPropDef.cover` (one field, documented) |
| `Assets/Resources/Data/Canonical/scene-configs.json` | `"cover": true` authored on **22** prop rows |
| `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` | new `CaseCourtyardCoverRings` + header line |
| `docs/MASTER_CATALOG/scenes.md` | RaidBase_* section now names `RaidBaseDresser` + the new `CoverRingPlacer`, flags the stale `RaidBaseGenerator.cs:35` comment, and records that `RaidNavBake` carves nav from RENDER MESHES (CLAUDE.md navigation + section 15) |
| `CLI_LANES_WO_NUMBERS.md` | banner block; next free 1633 -> **1636** |

`git diff --stat` in the worktree is **line-level, not whole-file** (dresser 349 changed lines of
1102, regression +82, catalog +10, JSON 44) - so the lead's explicit-path staging is a normal diff,
not a whole-file replace, despite this worktree being CRLF where the shared checkout is LF on disk.

**`Assets/Editor/WallTools/CoverRingPlacer.cs.meta` does not exist yet** - Unity generates it on the
next import. The committer must include the generated `.meta` with the file.

`Assets/Editor/WallTools/RaidBaseGenerator.cs` was **NOT opened** - lane ARENA-WALL holds it.

## 4. Verification actually performed (edit-only lane)

- **`tools/gate_brace.py` on all four `.cs`: `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0.** Raw counts also
  balanced: 161/161, 10/10, 29/29, 43/43. **NUL bytes: 0** in every file.
- **Canonical JSON byte-safety:** patched in place at byte level, one prop per line, appending to
  existing lines only. **LF 352 -> 352, CRLF 352 -> 352, LF == CRLF.** Re-parsed after the write:
  6 configs, all 22 intended rows carry `cover: true` and the 5 soft tokens (`banner_green`,
  `flag_green`, `banner_white`, `torch_mounted`, `floor_tile_big_spikes`) correctly carry none.
- **Banner:** `CLI_LANES_WO_NUMBERS.md` LF 4835 -> 4875, CRLF 4835 -> 4875, LF == CRLF; next free
  now reads **1634**.
- **RED-FIRST PROVEN, not asserted.** The new case's logic was replayed in Python against the
  **HEAD c10e4f5d1 bytes** (`git show HEAD:...`) and against the working tree:

```
BEFORE (HEAD c10e4f5d1): FAIL x7
     - raider_camp_small: 10 prop rows, NONE cover:true
     - fortified_garrison: 9 prop rows, NONE cover:true
     - mage_enclave: 8 prop rows, NONE cover:true
     - no CoverRingPlacer call - props still on one annulus, no jitter
     - stripColliders not gated on the authored cover flag
     - no per-config props count line in any bake log
     - staging marker not read out of the built tree

AFTER  (working tree) : PASS
```

- **NOT verified, and named as such:** no Unity compile, no `COMPILE_GATE_OK`, no `REGRESSION_OK`, no
  bake, no screenshot. The lane is edit-only by instruction. The C# has not been compiled by anything.

## 5. Expected bake line

After `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`, one new line per config:

```
[RaidBaseDresser] props 'raider_camp_small': 27 placed (set=building_tent_greenx4*, barrel_largex4*, ...)
[RaidBaseDresser] props 'fortified_garrison': 25 placed (set=barracksx1*, House_Medieval_Mediumx1*, ...)
[RaidBaseDresser] props 'mage_enclave': 27 placed (set=pillar_decoratedx6*, banner_whitex4, ...)
```

`*` marks a cover-class prop. The counts are the **authored ceilings** - a slot the keepout rejects
after re-rolls is skipped, so a slightly lower number is correct behaviour, not a failure. The
accompanying `[Flow:RaidBase] props '<id>' total=... courtyard=... rings=... band=...m laneHalf=...m`
line names the band so a thin courtyard is diagnosable without a screenshot.

Derived courtyard bands (from the authored `baseRadius` / `innermost` in `Builds/wave2-bake2` and the
constants added this session): Easy `r 9.0-25.5 m`, **2 rings**, laneHalf 5.28 m; Hard `r 25.6-43.5 m`,
**3 rings**, laneHalf 5.36 m; Extreme `r 27.8-48.5 m`, **3 rings**, laneHalf 5.29 m. Compare the single
annulus each camp had before: Easy 15.6-20.8, Hard 33.7-38.8, Extreme 37.2-43.1.

`missing=0` must hold on all three - no new art token was introduced by this ticket.

## 6. Two things found late, and what happened to them

**(a) A real defect in this lane's own first draft, caught and fixed before hand-back.**
`ClusterAnchorAngle` was first indexed by the entry's position in the FULL props list, not by its
position among the COURTYARD entries. Replayed against the authored rows that put every cluster
anchor in a lopsided arc: Easy would have had nothing between 145 and 323 degrees, Hard's north
quadrant would have been bare, and **both** Extreme clusters would have sat on the east side - i.e.
the fix would have shipped half a courtyard of the exact empty dirt it exists to remove. Now counted
and indexed courtyard-locally (`courtyardCount` / `courtyardIndex`), with the cursor still advancing
on a missing-art skip so a missing pack cannot bunch the survivors into one arc.

**(b) The keep interior is BUILT but has never been EVIDENCED.** `RaidBaseDresser.cs:1022-1031`
does instantiate `ceiling_tile` as `KeepCeiling`, so WO-1611:131 is implemented. But grepping
`Builds/wave2-bake2` for `ceiling`, `threshold`, `Zone_`, `clad`, `torch` and `colonnade` returns
**0 hits for every one of them**. WO-1611's own acceptance asks for "`ceiling_tile` count > 0 under
the keep; courtyard ceiling count = 0" - **no bake log has ever been able to answer that**, which is
the same observability hole as the props count. Recorded as a finding, not fixed here: this ticket's
scope is the props seam. It belongs with WO-1635's canon pass or a follow-up.

## 7. Numbering

Minted 1631/1632/1633 off the banner (which read next free **1631** in both this worktree and the
shared checkout when read). The coordinator then reported that two other lanes had taken **1631**
(landscape lock) and **1632** (arena boundary ring) in parallel on dev. **All three tickets were
renumbered to 1633 / 1634 / 1635**, files renamed and all 33 internal references rewritten across the
four `.cs` files and four `.md` files (numeric-boundary regex, so unrelated WOs containing the digits
1631-1633 were left untouched). Banner rewritten: previous next free 1633 -> **new next free 1636**.

## 8. Follow-ups minted from this work

- **WO-1634** (READY) - per-camp authored gaps: Easy has no fire/cook cluster, Hard has no siege pair
  and is under on racks + damage tell, Extreme's banners/torches are authored into `Keep` instead of
  the courtyard colonnade. Carries one owner question with a default taken.
- **WO-1635** (READY) - the duplicated `props.set` authority (`fortified_garrison` authors `"barracks"`
  in both schemas) and the four stale doc lines, including WO-1607:47.
