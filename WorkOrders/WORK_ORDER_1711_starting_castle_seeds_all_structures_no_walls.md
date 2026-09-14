# WORK ORDER 1711 - Starting castle pre-seeds every structure but towers; walls seed only on raid-arena flip

**Status:** READY TO IMPLEMENT - owner design ruling, depends on WO-1710's RCA verdict for the
existence-check half (see section 4)
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from the owner's live design ruling while
felt-testing the 2026-09-14 tester build (release 2026.09.14.369302)

## 1. Owner ruling, verbatim

> "In the premade building, I should already have one of everything that you need outside of a
> defensive tower already placed. If that's the case let's make sure that the only thing that's
> available to upgrade would be the towers. Also on a sidenote, let's remove adding the walls let's
> only add walls when they get to a player flipped base. I think it makes more sense."

Two rulings, related but separable:

**Ruling A - the starting castle is pre-complete except towers.** The premade/starting castle (built
by `CastleHubBuilder.cs`) should already have one of every non-tower structure placed and marked built.
The ONLY thing the build menu should offer as buildable/upgradeable from the start is defensive towers.
This is the target state WO-1710's fix must produce - if WO-1710's RCA finds the existence-check gap,
the fix criteria there and this ruling's acceptance criteria are the same list of "must already read as
built": barracks (`CastleHubBuilder.cs:417` seeds `CastleBarracks`), iron mine, crystal mine, and every
other non-tower structure the castle builder seeds.

**Ruling B - walls are a raid-arena concept, not a home-castle one.** Remove wall seeding from the
starting/home castle build (`CastleHubBuilder.cs` lines ~175-230, `OuterWalls_Towers_Battlements` root,
`CastleWallsFromRecipe.cs` per its own T-007 comment as "the SHIPPING walls come from
`CastleWallsFromRecipe.Recreate()`"). Walls should be added only when a town becomes a raid target -
i.e. the WO-1705 flow ("final raid victory to owned town") where a captured/inherited town is converted
into a playable raid arena for AI or future player opponents. That is the existing raid-base pipeline
(`RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` from
WO-1703/1704) which already builds walls for `RaidBase_*` scenes and `OwnedTown_IronBastion.unity`.
"Player flipped base" = a base that has flipped from home-town state into raid-target state via that
pipeline, not a new mechanic to invent.

## 2. Why this matters now

Removing home-castle walls is a real gameplay/visual change (the castle currently ships with a full
perimeter) - confirm with the owner only if the RCA/implementation lane finds a reason walls are load-
bearing for something else (e.g. a collider boundary the player relies on, or a nav-mesh fence). If no
such dependency exists, execute the ruling as stated; this is a decision already made, not one to
re-litigate.

## 3. Scope

- `Assets/Editor/CastleHubBuilder.cs` - remove the wall-seeding block for the starting castle
  (`wallsRoot`/`OuterWalls_Towers_Battlements` and its wall-segment loops); confirm whether
  `CastleWallsFromRecipe.cs` is also called from this path and, if so, gate it out of the home-castle
  build while leaving it available to whatever raid-arena conversion path calls it (or confirm it is
  raid-only already).
- Whatever registry WO-1710 finds is the "structure exists" source of truth - ensure the castle builder
  writes every non-tower structure it seeds into that registry at seed time, so the build menu never
  offers them and troop training (and any other structure-gated feature) reads them as present.
- Identify "tower" precisely via the existing `StructureRole` enum (`Assets/_Modules/Core/Catalog/
  StructureRole.cs`, `StructureRoles.cs`) rather than a hand-picked name list - confirm the enum already
  distinguishes defensive towers from other structures, and if not, say so before inventing a new field.

## 4. Dependency on WO-1710

Do not start implementation until WO-1710's RCA lane lands its verdict on the existence-check root
cause - both tickets touch the same seeding code and the same "what counts as built" registry. If
WO-1710's fix already makes castle-builder-seeded structures read as built, ruling A may already be
satisfied by that fix alone; verify against this ticket's acceptance criteria rather than assuming.

## 5. Acceptance criteria

- [ ] A fresh starting castle (via `CastleHubBuilder`) has one of every non-tower structure placed AND
      registered as built - verified against whatever check WO-1710 identifies, not just visually
      present in the scene.
- [ ] The build menu on a fresh castle offers ONLY defensive towers as buildable/upgradeable; no other
      structure type appears as an offer.
- [ ] Troop training works immediately on a fresh castle (barracks reads as built) with no remove/re-add
      workaround needed.
- [ ] The starting castle no longer seeds any wall segments; a fresh castle scene has zero `Wall_*`
      GameObjects from the home-castle build path.
- [ ] The raid-arena conversion path (WO-1705's flow, or the existing `RaidBase_*`/`OwnedTown_*`
      generation) still seeds walls correctly - do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`,
      `ArenaBoundaryRing.cs`, or `RaidNavBake.cs` (WO-1703/1704 already gated and shipped 2026-09-14) -
      confirm by re-running `tools/regression/raid_suites_gate.ps1` after this lane's edits and reading
      all four markers on a fresh log.
- [ ] Headless regression added/extended: build a fresh castle, assert zero walls, assert every non-tower
      structure reads as built, assert the build menu's offer list contains only towers.

## 6. What NOT to touch

- Do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs`,
  or any `RaidBase_*.unity` / `OwnedTown_IronBastion.unity` scene - those already ship walls correctly
  for raid arenas and are out of scope here.
- Do not hand-edit `Village.unity` or any curated scene.
- Do not remove wall PREFABS or the wall-seeding CODE PATH used by the raid-arena flow - only the call
  that seeds walls onto the starting home castle.
