# WORK ORDER 1711 — RESULT (implementation lane, 2026-09-14)

**Status:** IMPLEMENTED - awaiting lead gate 2026-09-14
**Lane:** implementation (edit-only). No Unity run, no gate, no build, no commit — per the brief.
**Base:** `dev @ cfee3dac9`.
**Companion:** `WORK_ORDER_1710_structure_existence_check_broken_for_castle_builder_structures.RESULT.md`
— same lane, shared files. **Read it first**; ruling A is delivered by the registry fix recorded there,
and it also carries a near-miss/recovery note about `structures-catalog.json` the lead must see.

**⛔ RULING B IS HALF DONE ON PURPOSE, AND THE OTHER HALF NEEDS AN OWNER RULING. Section 2 below.**
Do not read a green gate as "the castle has no walls".

---

## RULING A — the starting castle is pre-complete except towers

### Delivered

Every non-tower structure the castle seeds now reads as **built** through the **same** registry
WO-1710 fixed — `StructureSingleton.IsPlayerBuilt` -> `HasPlacedInstance` clause 1, a persisted
`BaseLayout` record. **No second "already built" mechanism was created**, which the brief explicitly
forbade and which would have been the easy wrong answer here.

The mechanism, end to end:

- **Barracks** — opens via Part A (`BarracksUnlock.FoundingComplete` now accepts the premade founding),
  which un-gates `AdoptBakedBarracksIfNeeded` (`StrategicPlacementMigration.cs:408`); adoption writes
  the authored record and the card stops being offered.
- **The eight ring storefronts** — already covered by `BakedRows`.
- **`collector_forge` (Iron Mine) and `workshop` (Crafting Station)** — the two that were in **no**
  registry at all; two `BakedRows` rows + their catalog `bakedTwins`
  (`StrategicPlacementMigration.cs:100-124`; both catalog copies). Full reasoning in the 1710 RESULT.
- **Existing Default-Town saves, including the owner's tester save**, are repaired on their **next
  home-hub load** by `StrategicPlacementMigration.BackfillNewCensusRows` (`:659-706`). **No START NEW
  and no save wipe is needed** — the one-shot writer alone would have served fresh foundings only.
  See the 1710 RESULT's "the half that almost shipped broken" section and its per-save table.

### ⚠ THE RULING IS NOT FULLY SATISFIABLE AS WRITTEN, AND HERE IS THE EXACT GAP

Owner, verbatim: *"In the premade building, I should already have one of everything that you need
outside of a defensive tower already placed. If that's the case let's make sure that the only thing
that's available to upgrade would be the towers."*

**"If that's the case" is doing real work, and it is NOT the case.** The castle authors **ten**
`canonicalId`s; the catalog has **29** rows. Counted at source 2026-09-14, these **non-tower** rows are
**not seeded by the castle at all**, so after this lane's fix they legitimately remain offered:

| id | displayName | why it is still offered |
|---|---|---|
| `mine_crystal` | Crystal Mine | no seeded instance anywhere (WO-1710 confirmed the owner's hedge) |
| `market` | Marketplace | IS a `BakedRow`, but `Marketplace_Monetization` is **absent from the shipped hub scene** |
| `healing_caravan` | — | Support row, never seeded |
| `lumberyard` / `foundry` / `silo` | storage containers | never seeded (the owner's hand-authored **pallets** sit beside the collectors instead — a different, render-only concern) |
| `mill` / `lumbermill` | retired | Town **lockedIds**; the palette hides them, so not actually offered |

Plus walls (`wall_wood`, `wall_stone`), `gate_stone` and decorations, which are their own palette verbs
(Walls / Defense) and were never in scope for a "one of everything" seed.

**Two possible readings, and it is the owner's call, not mine:**
**(i)** the ruling means "nothing the castle seeds may be re-offered" — **that is what shipped here, in
full**; or **(ii)** the ruling means the castle must additionally seed a Crystal Mine, a Marketplace and
the three storage containers so that literally only towers remain — **that is a SCENE change**
(authored roots in `Main_Castle_Overworld` via `OwnerCastleLayoutRepair`), which this edit-only lane
cannot do and which CLAUDE.md §3 forbids doing by hand.

**I did not guess between them.** Reading (i) is implemented; reading (ii) is surfaced here as a
one-word owner decision.

### Tower classification — WO-1711 §3, answered

**`StructureRole` does NOT distinguish defensive towers, and structurally it never can.** Read at
source 2026-09-14: all five `type: "Tower"` rows (`tower_ground_archer`, `tower_ballista`,
`tower_siege_tower`, `tower_catapult`, `tower_arcane_spire`) author **no `role` at all**. And
`StructureRoles.Index` **refuses two rows claiming one role** (`FlowTrace.Fail` on collision,
`StructureRoles.cs` header), so a role identifies exactly **ONE** building by construction — it cannot
name a *class* of buildings. Using it as a tower filter is not merely unauthored, it is a category
error.

**The clean discriminator is `CatalogEntry.type == CatalogType.Tower`.** It selects exactly those five
and correctly **excludes `arcane-tower`**, which despite its id is `type: Resource` (the Cathedral of
Learning). Precedent already in the tree: `DailyQuestTowerBridge.cs:66-78` says the same thing and
notes no runtime code reads the `type` field, using the `tower_` id prefix as its runtime stand-in with
`QuestCompletabilityRegression` pinning the invariant.

**Per the brief I am flagging rather than inventing:** I added **no new field**. Gate 6 of the new
suite pins the finding so the next lane does not re-litigate it.

---

## RULING B — walls

### What was removed

**`Assets/Editor/CastleHubBuilder.cs`** (`BuildCastleHub`, the perimeter block at ~`:194-217` post-edit):
the two poly-stone runs (`Wall_South_-3..3` skipping the gate cell, `Wall_North_-3..3`) and the two
Quaternius plaster lines (`QWall_West_0..6`, `QWall_East_0..6`) — **26 wall segments** — are deleted and
replaced with the ruling, its reasoning and its residual.

**Kept, deliberately:** the 4 corner towers (ruling A leaves towers), the south gate and the drawbridge
(the OuterWorld connection seam, not perimeter defence), and the `wallStone` / `qWallStraight` prefab
loads (other entry points in the file still take `wallStone`; `LoadQuat` is the pack-presence probe).
File header (`:32`) and the completion `Debug.Log` (`:376`) updated to match, each naming what they used
to say and why it changed.

**The CoC inner ring needed no removal:** `BuildInnerWallRing` (`:716`) was already made **destroy-only**
by the owner's own F8s (*"you added walls inside, not at the gates"* 2026-06-27; *"Wall in middle?"*
2026-07-02). The call stays precisely because it is what keeps the rejected ring from coming back.

### ⛔ AND NOW THE HONEST PART: THIS CHANGES NOTHING THE PLAYER SEES

**PROVEN at source, 2026-09-14:**

- `Assets/Scenes/Main_Castle_Overworld.unity` contains `CastleSide_North/East/South/West` and **zero**
  objects named `OuterWalls_Towers_Battlements`, `Wall_South_*`, `QWall_*` or `InnerWallRing_CoC`.
  The only `Wall_*` names in it are 4x `Wall_DoorJamb_L` + 4x `Wall_DoorJamb_R`.
- `BuildCastleHub()` **throws on any non-empty scene** (`:130-135`) — it has never authored the shipped
  hub. The ticket's own §3 quoted the T-007 comment that said this: *"the SHIPPING walls come from
  `CastleWallsFromRecipe.Recreate()` ... outside this method's legacy path."*
- `CastleWallsFromRecipe.Recreate()`'s only in-repo caller is
  `CastleHubBuilder.BatchRebuildCastleFromRecipeAndBake` (`:1703`), which targets the **legacy**
  `Assets/Scenes/MainCastle_Hall.unity` (`:1697`), **not** the hub. So there was nothing to "gate out of
  the home-castle build" — the ticket's conditional ("if so") resolves to **no**. The class and every
  raid-arena caller are untouched.

**So the walls the owner is looking at are baked `CastleSide_*` scene objects.** Removing them requires
an editor tool plus a re-bake of `Main_Castle_Overworld.unity` — which this edit-only lane cannot run,
and which no one may do by hand (CLAUDE.md §3).

### ⛔ AND IT NEEDS AN OWNER RULING BEFORE ANYONE DOES IT — this is exactly WO-1711 §2's case

§2 says to confirm with the owner *"if the implementation lane finds a reason walls are load-bearing
for something else (e.g. a collider boundary the player relies on, or a nav-mesh fence)"*. **I found
three, all on the same roots:**

1. **The gates.** `CastleGateNavVerify.cs:243-249` finds the `Gate` child **under** each `CastleSide_*`.
   Deleting the side roots deletes the gates.
2. **The OuterWorld exit seam.** `CastleHubBuilder.EnsureExitSeamAtRecipeGate` places the exit trigger
   **onto the recipe south gate**; a committed trigger already sat 43m off-mesh once and produced a
   proven *"can't exit"* (per that method's own comment).
3. **Nav.** `CastleHubBuilder.cs:1204-1213` applies `ignoreFromBuild=true` per `CastleSide_*`/`Gate_*`,
   and `CastleWallNavObstacleInstaller` exists for this wall geometry.

**Recommendation to the PO:** ruling B's visible half is a separate, scene-touching ticket. It needs
the owner to say what replaces the gates and the exit seam once the perimeter is gone — a castle with
no walls still needs a door the hero can walk through, and that door is currently a child of the walls.
I did not invent an answer.

---

## Files changed by this lane (WO-1711 half)

| File | What |
|---|---|
| `Assets/Editor/CastleHubBuilder.cs` | `:32` header corrected; `~:194-217` 26 wall segments removed + ruling/residual recorded; `:376` completion log corrected |
| `Assets/Editor/Regression/PremadeCastleCompleteRegression.cs` | NEW (shared with 1710) — gates 5 + 6 are this ticket's |
| `Assets/Editor/Regression/DataRegression.cs` | `:540` — the one registration line |

Ruling A's registry files are listed in the 1710 RESULT. **Out of scope and untouched, as instructed:**
`RaidBaseDresser.cs`, `RaidBaseGenerator.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs`, every
`RaidBase_*.unity`, `OwnedTown_IronBastion.unity`, and `CastleWallsFromRecipe.cs`.

---

## Regression (this ticket's gates)

- **Gate 5 — no home-castle walls.** Asserts the **shipped hub scene asset** carries none of the
  home-build wall objects, and that `CastleHubBuilder` no longer emits `Wall_South_*` / `Wall_North_*` /
  `QWall_*`. It then **logs the residual**: how many `CastleSide_*` roots remain, and that ruling B is
  half done. A green gate therefore cannot be misread as "the castle has no walls" — the residual is in
  the log every run.
- **Gate 6 — tower is typed, not roled.** Pins `CatalogType.Tower` as the discriminator and records why
  `StructureRole` cannot be one.

**NOT RUN** — edit-only lane, no Unity fired, no marker claimed. The lead's gate is the first execution.

```
python tools/gate_brace.py <5 .cs files>   ->  GATE_BRACE_SUMMARY bad=0 of 5   (exit 0)
NUL scan, same 5 .cs + both catalog copies ->  NUL=0 on all 7                  (exit 0)
```

## Acceptance criteria

- [x] Non-tower seeded structures registered as built via the WO-1710 registry, not a parallel one —
      **with the "one of everything" gap enumerated above for an owner decision.**
- [~] Build menu offers only towers — **true for everything the castle seeds**; six unseeded non-tower
      rows remain offered. Reading (i) vs (ii) above is the owner's call.
- [x] Troop training works immediately on a fresh premade castle (WO-1710 Part A).
- [x] `CastleHubBuilder` seeds zero wall segments — **but the shipped castle's walls are baked
      `CastleSide_*` objects and are STILL THERE.** Half done, flagged, reasons given.
- [x] Raid-arena wall path untouched and confirmed never called from the home-castle build.
      `tools/regression/raid_suites_gate.ps1` is the lead's to run — this lane fires no Unity.
- [x] Headless regression added + registered (`DataRegression.cs:540`).
