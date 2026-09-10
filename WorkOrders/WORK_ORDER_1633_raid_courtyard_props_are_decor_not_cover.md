# WO-1633 - Raid courtyards: props are decor, not cover, and they sit on one mechanical ring

**Status:** FIXED 2026-09-10 - courtyard props keep colliders (cover), sit in 2-3 clustered rings with a clear lane, 27/25/27 placed on wave3-bake7 with per-config log lines; CoverRingPlacer extracted from the siege arena; owner felt-test on the next APK closes (was: IMPLEMENTED - awaiting gate + bake (lane RAID-POLISH 2026-09-10))
**Minted:** 2026-09-10 (lane RAID-POLISH, main-line banner; bumped 1633 -> 1634 in the SAME edit)
**Silo / Lane:** World / Raid scenes - the PROPS / DRESSING seam only
**Files owned:** `Assets/Editor/WallTools/RaidBaseDresser.cs`, NEW `Assets/Editor/WallTools/CoverRingPlacer.cs`,
the `raidDress.props` rows of `Assets/Resources/Data/Canonical/scene-configs.json`,
`Assets/_Modules/Village/World/SceneConfigCatalog.cs` (one field), `Assets/Editor/Regression/RaidBaseLayoutRegression.cs`
**Do NOT touch:** `Assets/Editor/WallTools/RaidBaseGenerator.cs` - lane **ARENA-WALL** is adding the
perimeter ring there right now. This ticket never opens that file. Also off-limits: `RaidNavBake`,
`RaidHudController`, `TroopController`, `RaidScoring`, garrison composition, spire HP, tower DPS.
**Type:** EXISTING system. The dresser exists and works. This is a placement + physics + observability
defect inside it, not a missing feature.

---

## 0. Owner voice (binding intent)

> **"i have mentioned it in testing that it feels incomplete and not polished"**
> - owner, 2026-09-10 morning, about the raid arena / bases.

> **"similar strategy as we used in battle arena"**
> - owner, 2026-09-10, same morning, on how to dress the courtyards.

And the program north star this hangs off, `WORK_ORDER_1607_raid_bases_as_places.md:65`, verbatim:

> `COURTYARD (camp life + cover + a fight, not empty dirt)`

---

## 1. FIRST: the premise most seats will arrive with is STALE

`WORK_ORDER_1607_raid_bases_as_places.md:47` says every raid row's `props` is *"authored empty and
unread"*, and `Assets/Editor/WallTools/RaidBaseGenerator.cs:35` says prop dressing is *"still dead
and DELIBERATELY not faked"*. **Both were true when written and are FALSE on HEAD.** Proven at
source 2026-09-10:

| Claim | Evidence read this session |
|---|---|
| The seam reads authored props | `RaidBaseDresser.ScatterProps` `:534-539` reads `def.raidDress.props` FIRST; `:540-552` is the legacy `props.set` fallback; `:553` the hardcoded default |
| All three raid rows author it | `scene-configs.json` rows `raider_camp_small` / `fortified_garrison` / `mage_enclave` each carry a `raidDress.props` array - **27 / 25 / 27** prop instances |
| Every token resolves on disk | all 27 distinct tokens resolved by replaying `LoadVisual`'s search order over `KayFolders` / `SyntyFolders` / `StructureContent` - zero misses |
| The bake agrees | `Builds/wave2-bake2`: `[Flow:RaidBase] dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0`, `... 'fortified_garrison' ... placed=489 missing=0`, `... 'mage_enclave' ... placed=590 missing=0` |

**A seat that implemented "author the props set and read it through the seam" would have built a
second dresser on top of a working one.** The stale sentences are retired by **WO-1635**, not here.

---

## 2. What is ACTUALLY wrong (this is the ticket)

### 2.1 The props are DECOR. They are not COVER.

`RaidBaseDresser.cs:567`:

```csharp
InstantiateVisual(model, zone, "Prop_" + p.token, pos, Quaternion.Euler(0f, (seed + k * 40) % 360, 0f), true);
```

That last argument is `stripColliders`. **Every prop in every raid base has its colliders removed.**
Against the child WOs' own tables, each re-read at source:

- WO-1609:104 - Easy loot pile, 8-10 pieces: *"Against the inner face of the palisade, not in the march line from south gate to spire. **Colliders on.**"*
- WO-1610:108 - Hard barracks: *"West yard, 16-22 m from centre. **Collider**; leave >= 4 m aisle to the choke."*
- WO-1610:111 - Hard crates, 8: *"**Cover** along the **sides** of the south->north march, not on it."*
- WO-1610:112 - Hard damage tell, 4: *"Visual, **collider stays**."*
- WO-1607 section 6, "Cover stacks" row: *"Troops can stand behind something ... `crate_*` / `barrel_*` / `box_stacked` **with colliders**."*

A crate you shoot through and walk through is scenery. That is a direct, cited cause of "not polished".

> **Nav note, proven, so nobody blocks on it:** `RaidNavBake.cs:55-64` marks **every Renderer**
> `NavigationStatic` and bakes the LEGACY navmesh (`UnityEditor.AI.NavMeshBuilder.BuildNavMesh()`,
> `:69-70`). Nav carve therefore comes from RENDER MESHES, and props already carve it today.
> Restoring colliders changes **physics and line-of-sight**, not the navmesh - so it cannot by itself
> introduce a nav softlock (WO-1607 acceptance 5). Added DENSITY can, which is what the lane
> exclusions in section 3 are for.

### 2.2 One mechanical ring, no jitter, no variance - the exact thing 1609 forbade

`PropSlot` `:613-628`. The courtyard branch is one line, `:627`:

```csharp
r = Mathf.Lerp(ctx.InnerLayers > 0 ? ctx.Innermost + 4f : 8f, ctx.Radius - 6f, 0.45f + (k % 3) * 0.15f);
```

and the angle, `:615`, is `((seed % 360) + k * (360f / n))` - **evenly spaced around one circle**.
No positional jitter. No scale variance. WO-1609:99, verbatim:

> *"Place as **clusters**, not a ring of singles"*

The shipped code is, precisely, a ring of singles. And because the authored `count` is a fixed number
while the courtyard grows with `baseRadius`, the ring thins out as the camp gets bigger. Courtyard-zone
instances against radius, computed from the authored rows plus the band formula at `:627`:

| Camp | baseRadius | Courtyard prop instances | Courtyard band from `:627` | Roughly |
|---|---|---|---|---|
| `raider_camp_small` | 31 m | 19 | r 15.6-20.8 m | one prop per ~6 m of ring |
| `fortified_garrison` | 49 m | 15 | r 33.7-38.8 m | one prop per ~15 m of ring |
| `mage_enclave` | 54 m | 9 | r 37.2-43.1 m | one prop per ~28 m of ring |

Everything inside the band is bare, everything outside it to the wall is bare, and the pieces are
spaced like fenceposts. **That is "empty dirt".**

> Extreme's low count is deliberate and is NOT a defect - WO-1611:96, verbatim: *"Courtyard - spare
> on purpose ... This is not a settlement. If it is as busy as Easy, Extreme loses identity."*
> This ticket therefore raises Extreme's **arrangement quality**, not its census.

### 2.3 The owner's named precedent already exists in this repo

`Assets/Editor/ProceduralSiegeArenaBuilder.cs` - the battle arena she pointed at:

- `:24-27` - *"Natural cover in polar rings (`PlaceCoverRing` math kept from the owner concept: radius / count / jitter) from polyperfect Nature_M prefabs ... **colliders ON so they actually block movement + line-of-sight**, giving attackers a multi-directional approach with cover."*
- `:147-150` - four concentric `PlaceCoverRing` calls, each with its own `radius` / `count` / `jitter` / `scaleMin` / `scaleMax`.
- `:207-224` - the placement body: even polar angle, then `jitter` on x and z from a **seeded** `System.Random`, a random yaw, and a `Lerp(scaleMin, scaleMax)` size roll. Deterministic via `RandomSeed = 9388` (`:89`) so a rebuild reproduces the venue.

The raid dresser has none of jitter, scale variance, or colliders. That gap **is** the owner's remark.

### 2.4 No bake log has ever stated a props count

The only dresser line is the aggregate at `:109-112`, `dressed '<id>' kit=... placed=314 missing=0` -
and `_placed` (`:189`) is incremented by **every** `InstantiateVisual`, so 314 is clad panels + floor
tiles + gatehouse + gate-mouth + props together. There is no way to tell from any log whether the
courtyard got 19 props or zero. That is why 2.1 and 2.2 survived three "IMPLEMENTED" bakes.

---

## 3. Acceptance

1. Cover-class props keep their colliders; soft decor (banners, torches, flags) still strips them. The choice is **authored data**, not a token allowlist in code.
2. Courtyard props are placed as **clusters on concentric cover rings** between the inner boundary and the wall line, with seeded jitter and scale variance - the `PlaceCoverRing` vocabulary, reached through a **shared helper**, not a copy of the arena file.
3. The south gate -> spire assault lane stays clear (WO-1609:110 >= 4 m; WO-1610:114 >= 5 m), as does the north gate lane when `entranceCount: 2` and the inner keep gate when `interiorWallLayers >= 1`.
4. No prop lands inside a turret footprint or on the staging bearing. Both are **read out of the built tree** (`DefenseTower` components; the `RaidStagingPoint` marker, `RaidBaseGenerator.cs:189`), never from hardcoded radii - staging is on the SOUTH-WEST diagonal for Hard and Extreme (`Builds/wave2-bake2`), not the south axis.
5. The bake prints, once per config: `[RaidBaseDresser] props '<config>': N placed (set=...)`.
6. `RaidBaseLayoutRegression` gains a case that is **RED on the tree as it stands today** and green after.
7. A missing gitignored pack still `LogWarning`s and never fails the bake (TGVRU).
8. `RaidBaseGenerator.cs` is not opened by this ticket.

## 4. Not in scope

Per-camp authored content gaps (Easy fire/cook, Hard siege + racks + damage tell, Extreme banner/torch
zones) -> **WO-1634**. Legacy `props.set` retirement plus the stale canon in section 1 -> **WO-1635**.
