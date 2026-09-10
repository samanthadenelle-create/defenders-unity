# WO-1607 — Raid bases as places: layered defenses, not a fence and a tower

**Status:** IMPLEMENTED - 6a5c7a36d on HEAD 2026-09-09 (was READY); owner felt-test closes. See `WORK_ORDER_1607_raid_bases_as_places.RESULT.md`. PRIOR STATUS: READY TO IMPLEMENT — program spine; implement via WO-1608 / 1609 / 1610 / 1611  
**Minted:** 2026-09-09 (CLI) — banner bumped 1607 → 1612 in the same edit  
**Priority:** P0 felt — owner: review the raid bases, design them better, use prefabs (KayKit dungeon is a good fit), better defenses than fence + tower, “needs to have something to it”  
**Lane:** World / Raid scenes (serialization bottleneck: one agent on raid builders at a time)  
**Extends:** WO-1592 (felt north star) + WO-1593 (KayKit art pass). This program **answers 1593 Q1 from disk** and supplies the layout 1593 did not spec.  
**Respects:** `docs/RAID_BALANCE_AUDIT_2026-09-06.md` (do not rebalance HP/DPS/caps); WO-1520 staging; WO-1594 HUD; WO-1595 AI (file-disjoint)  
**Law:** never hand-edit `.unity` — rebuild via `RaidBaseGenerator` + `RaidNavBake`. KayKit packs are **gitignored** (`.gitignore:115-119`); missing pack → `LogWarning` + labelled fallback, never an error.

---

## 0. Owner voice (binding intent)

> Can you review how raids are and look at the bases we are raiding and help design them better.  
> Use any prefabs we have, KayKat dungeon could be good.  
> Better defenses and not just the fence and tower.  
> Needs to have something to it.

Creative authority stays hers on camp names, kit vetoes, and which camp ships first. These WOs propose a concrete menu she can accept, trim, or redirect.

---

## 1. How raids actually work (proven this session, not restated from a doc)

Live loop (WO-771 lock): **RaidSelectionScreen → RaidDeployScreen → `SceneRouter.GoRaid(def.sceneName)` → `RaidBase_<id>`**. Walk-to overworld raids are dormant (`ff.raidwalk` default OFF).

| Piece | Authority (opened 2026-09-09) | What it does |
|---|---|---|
| Flagship camps | `Assets/Resources/Data/Canonical/scene-configs.json` rows `raider_camp_small` / `fortified_garrison` / `mage_enclave` | Easy / Hard / Extreme. Display names: The Forsaken Camp, The Broken Garrison, The Veiled Enclave. |
| Scene baker | `Assets/Editor/WallTools/RaidBaseGenerator.cs` `BuildConfigLayout` `:312-393` | Outer wall **ring**, optional inner keep **ring**, central **spire**, two-band **turrets**, BossSpawn, hero marker, WO-1520 staging. |
| Win | `RaidSpire` at origin; `RaidVictoryController` | Destroy the spire. Clock 180 s (`RaidScoring`). |
| Garrison | `RaidGarrisonSpawner.cs:168` | Ring at `max(2, baseRadius * 0.5)`. Composition from the config. |
| HUD / AI | WO-1594 / WO-1595 | Out of this program. Do not retune them here. |

**Village2** (`EnemyStrongholdBuilder`) is a **separate** layered-fortress baker (courtyard → choke → raised keep → boss chamber, `garrison-recipes.json` `village2_stronghold`). It is **not** one of the three RaidSelection cards. Do not rebuild Village2 in this program.

**Iron Bastion** (`RaidBase_IronBastion`) is the generator **template**. Catalog `scenes.md` FLAG-5: on disk, **not** in build settings. Park it. Do not ship a fourth camp in this pass.

---

## 2. What is wrong with the bases (the “fence and a tower” read)

Measured from the baker + the three live configs, not from a screenshot this session:

1. **One primitive: a square ring of wall panels.** `BuildRing` tiles `WallTierData.SegmentPrefabPath` panels (`RaidBaseGenerator.cs:1150`) at `SegSize = (1.5, 3.0, 1.5)` (`:92`). A gate is a **missing panel** (`outerGates` bools, `:333-338`), not a gatehouse. Easy + Hard have **zero** inner keep (`interiorWallLayers: 0`). Extreme has one concentric keep ring at 45% radius (`:340-352`) — still a square, just smaller.
2. **`props` is authored empty and unread.** Every raid row has `"props": { "set": [], "count": 0 }`. Generator header `:35` says so: *“Still dead and DELIBERATELY not faked: `props` (no prop dresser for raid bases yet).”* No tents, barrels, banners, barracks, rubble, or camp life.

   > ⚠ **CORRECTED 2026-09-10 (WO-1635, lane PROPS-CANON) — item 2 above was TRUE AT MINT (2026-09-09) AND IS NOW FALSE. Body left intact per CLAUDE.md §15 (frozen dated WO gets a banner, not a rewrite).**
   > Props are **authored** and **read**, proven at base sha `3da5e5360`:
   > - **Authored:** `Assets/Resources/Data/Canonical/scene-configs.json` `raidDress.props` — `raider_camp_small` 10 entries / **27** instances, `fortified_garrison` 9 / **25**, `mage_enclave` 8 / **27** (counted from the file this session).
   > - **Read:** `RaidBaseDresser.ScatterProps` consumes `def.raidDress.props` (`Assets/Editor/WallTools/RaidBaseDresser.cs:535-539` at `3da5e5360`; the **method name is the durable anchor** — the main tree has already moved it to `:616-619`).
   > - **Called:** `RaidBaseDresser.Dress(...)` at `Assets/Editor/WallTools/RaidBaseGenerator.cs:424`.
   > - The quoted generator header at `:35` is **also already corrected** and now reads *“WO-1608: `props` + `raidDress` are LIVE — RaidBaseDresser consumes them at bake time.”* (`RaidBaseGenerator.cs:35`, read at `3da5e5360`).
   > - Baked proof: `Builds/wave3-bake7:503` `dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0`.
   >
   > ⚠ Still true, and the reason WO-1635 exists: the **legacy `props.set` fallback survives** at `ScatterProps` (`:540-552` at `3da5e5360`) as a second authority, and `fortified_garrison` still authors `"barracks"` in BOTH schemas. That is WO-1635 acceptance #1 and is **not** closed by this banner.

3. **Towers are catalog turrets on a circle.** Easy: 3 archer + 1 mage, Cardinal (all on the wall line). Hard: 5+2 OverlappingFire. Extreme: 7+3 OverlappingFire. Fallback art is `Structures/Tower_Medieval_Wood` (`:112`); if that Resources path misses, `BuildFallbackTurret` is a **cylinder + cube cap** (`:916-932`) — the pillar the owner named. Easy’s `towers[]` palette is `tower_catapult` x2, which is a siege machine, not a watchtower with a fighting top.
4. **Ground is the RaidNavBake 140 m plane.** No floor tiles, no dirt, no cobble, no dungeon stone.
5. **Garrison is a ring, not a defense.** Guards stand at 0.5 × radius regardless of gate, courtyard, or keep. There is no “this is the gate watch” vs “this is the inner guard.”
6. **Nothing to read as a place.** Easy is an orc scavenger camp in copy (`displayName` / `description`) and a wood square in geometry. Hard is a garrison in copy and a bigger iron square. Extreme is a hollow enclave in copy and a steel square with one inner ring.

The fight can be fair and still feel cheap. That is a **layout** defect, not a balance defect. Do not retune spire HP (1200 / 2200 / 3500) or tower DPS budgets (12 / 16 / 20) in this program.

---

## 3. What “something to it” means (player-felt beats)

A raid is an **assault on a place**. Every camp must be readable in one eye-level screenshot as a **named kind of site**, and the walk from staging to the spire must have **beats**, not a flat yard:

```
STAGING (WO-1520, outside all reach)
  → APPROACH (road / dirt / banners — you can see the place before it shoots you)
    → GATEHOUSE (a building you breach, not a hole in a fence)
      → COURTYARD (camp life + cover + a fight, not empty dirt)
        → CHOKE (the funnel: inner gate / stairs / spike run)
          → KEEP / SPIRE (the objective, contested)
```

Easy may skip the raised keep (single ring + courtyard). Hard adds the choke. Extreme **is** a dungeon keep (KayKit stone interior around the spire).

This is the same layered idea `EnemyStrongholdBuilder` already ships for Village2 (`garrison-recipes.json:78-83`: courtyard → chokepoint → raised keep → boss chamber). **Reconcile onto RaidBaseGenerator.** Do not greenfield a second baker.

---

## 4. Kit pick — ANSWERED from disk (WO-1593 Q1)

**Why the towers are pillars (proven, not inferred):** `PlaceTowerProp` does `Resources.Load(plan.PrefabPath)` (`RaidBaseGenerator.cs:873`). Catalog paths are `Structures/Tower_Medieval_Wood` etc. **`Assets/Resources/Structures` was deleted** (CDN/R2 move). Those loads miss, then `BuildFallbackTurret` builds a cylinder + cube cap (`:916-932`). Town art is Addressable/`StructureContent/`; raid bake does not use that channel. The dresser in WO-1608 must load via `AssetDatabase.LoadAssetAtPath` (dungeon-builder pattern), never `Resources.Load("Structures/...")`.

Verified present this session (all gitignored except owner wall FBXs):

| Pack | Path used by existing builders | Raid use |
|---|---|---|
| **KayKit Dungeon Remastered 1.1** | `Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/` — same `PackRoot` as `DungeonSceneBuilder.cs:88-89` | Walls, doorways, gated arches, barriers, columns, stairs, floors, spike/grate tiles, barrels, crates, chests, banners, torches, rubble. **This is the pack the owner named for the enclave.** |
| **KayKit dungeon (legacy twin)** | `Assets/Models/KayKit/dungeon/` — `KayKitChallengeOutpostBuilder.cs:141` | Same meshes; Outpost builder already loads them. Reuse its token resolver, do not fork. |
| **KayKit Medieval Hexagon Pack 1.0.1** | `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/<color>/` | Outdoor **place** pieces for Easy: watchtower, tent, barracks, catapult, shrine. |
| **Synty POLYGON Fantasy Kingdom** | `Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/` — already used by `SyntyCastlePerimeterBuilder` on the **hub**, never on raids | **Hard fortress:** `SM_Bld_Castle_Wall_01` (+ corners, arrowslit, destroyed), `SM_Bld_Castle_Wall_Gate_01` (doors + **portcullis**), `SM_Bld_Castle_Wall_Tower_{S,M,L}_01`, battlements, hoarding. Battleground: `SM_Prop_Spike_Fortification_01..03`, tents, banners, weapon racks. Siege: `SM_Wep_*`. Gitignored; this clone has it. |
| Owner wall slabs | `Assets/Resources/Walls/{wood,iron,steel}_wall.fbx` via `WallTierData` | **Keep as the collider / destructible curtain.** Visual clad sits on top. Tracked; the only raid wall art that `Resources.Load` still finds. |

**Banned:** KayKit **characters / enemies / hexagon units**. Owner 2026-08-19: *“I hate the KayKat enemies”* (`EnemyResolver.cs:190`). Environment and buildings only.

**Dungeon Remastered has no tower and no castle gate** (`KayKitChallengeOutpostBuilder.cs:790-794` already records `"tower"` does not exist in that pack; there is no `gate.fbx` / `door.fbx`). `wall_gated.fbx` is a **barred dungeon opening** — correct for Extreme’s keep door, wrong as Easy/Hard’s outer gate. Outdoor gates/towers: Hexagon `wall_straight_gate.fbx` / `building_watchtower_*` (Village already loads the hexagon wall family in `VillageSceneBuilder.Walls.cs`) or Synty castle gate (Hard).

**Fallback when a pack is missing (another machine / CI):** `LogWarning` + current WallSegment / catalog turret / MagentaGuard primitive. Never fail the bake. Same TGVRU rule as `EnemyStrongholdBuilder` and `KayKitChallengeOutpostBuilder`.

### Per-camp kit (owner may veto; recommended)

| Camp | Vibe | Primary kit | Accent |
|---|---|---|---|
| Easy — The Forsaken Camp | Abandoned scavenger settlement | KayKit dungeon `barrier*` palisade + dirt floors; Hexagon **green** tents / watchtowers. Synty battleground tents/spikes allowed as extras | hexagon `green` |
| Hard — The Broken Garrison | Occupied stone fortress | **Synty castle** (same modules as the hub perimeter): 5 m walls, gate+portcullis, S/M/L wall towers, arrowslits, battlements. KayKit dungeon `wall_gated` only if Synty gate pinches nav | Synty, not hexagon-red |
| Extreme — The Veiled Enclave | Ritual dungeon keep | **KayKit Dungeon Remastered** stone room around the spire. Synty castle stays off this camp so it does not read as Hard-but-purple | dungeon stone |

---

## 5. Child tickets (do not implement this file alone)

| WO | Owns | Sequence |
|---|---|---|
| **1608** | Engine: named zones, gatehouse, prop dresser, KayKit load, consume `props` | First. Unlocks all three camps. |
| **1609** | Easy camp layout (Forsaken Camp) | Second. Owner-felt first. |
| **1610** | Hard camp layout (Broken Garrison) | After Easy is felt-green, or in parallel on a **worktree** once 1608 is in. |
| **1611** | Extreme camp layout (Veiled Enclave dungeon keep) | After Hard, or stretch. |

**1593:** keep as the original art-pass ticket. Its Q1 is answered here. Implement 1593 **through 1608–1611**, do not bake the same scenes twice.

**Not in this program:** 1594 star HUD, 1595 AI roles, army caps, spire HP, Village2, Iron Bastion, KayKit enemy bodies, hand-edited scenes, R2 push (CLI after merge if new addressables appear — KayKit instantiated at bake time is usually scene-embedded).

---

## 6. Defense vocabulary (what “better than fence + tower” is)

Every camp must use **at least four** of these, not just wall panels + DefenseTower:

| Defense | What the player reads | Proven piece (this clone) |
|---|---|---|
| **Gatehouse** | A building you fight through | Dungeon `wall_gated.fbx` / `wall_doorway.fbx` / `wall_archedwindow_gated.fbx`; catalog `Structures/Gate_Medieval_Medium` |
| **Palisade / barrier** | Thickness, not a stick | Dungeon `barrier.fbx` / `barrier_half.fbx` / `barrier_corner.fbx` / `barrier_column.fbx` |
| **Watchtower with a top** | Platform + roof / crenel, not a cylinder | Hexagon `building_watchtower_<color>.fbx`, `building_tower_A/B_<color>.fbx` |
| **Siege emplacement** | A machine in a pit / yard | Hexagon `building_tower_catapult_*` / `building_tower_cannon_*`; catalog `Structures/Catapult`, `Structures/Ballista_L1` |
| **Courtyard life** | People lived here | Hexagon tent / barracks / weaponrack / crates; dungeon barrels, banners, rubble |
| **Choke** | Funnel under fire | Inner gate + stairs (`stairs_long.fbx`) + optional `floor_tile_big_spikes.fbx` as **nav-blocked décor** (not a new damage type in V1) |
| **Raised keep** | Vertical objective | `EnemyStrongholdBuilder` pattern: platform + stairs + NavMeshLink. Extreme required; Hard recommended; Easy off. |
| **Cover stacks** | Troops can stand behind something | Dungeon `crate_*` / `barrel_*` / `box_stacked` with colliders. Optional `BreakableContainer` — **must not** be required to win (win stays the spire). |

V1 traps = **geometry and décor**. Do not invent a new trap-damage system. Spike tiles are a choke **read**, not a DPS budget change.

---

## 7. Owner rulings needed (one answer each)

**Q1.** Approve the three kit assignments in §4, or name replacements (still KayKit-first, still no KayKit enemies).  
**Q2.** Ship Easy first (recommended) vs all three in one bake?  
**Q3.** Hard inner keep: raised platform (Village2 pattern) vs a flat inner ring (today’s Extreme)? **Recommend raised + stairs** so Hard teaches verticality before Extreme goes dungeon.

Until she answers, implementers use the recommended column. Do not stall 1608 on Q2 — the engine does not pick a camp.

---

## 8. Acceptance for the PROGRAM

1. Owner can name Easy camp as “a scavenger camp” from one eye-level screenshot (tents / palisade / gate / watchtower top), not “a fence.”  
2. Hard reads as a fortress: gatehouse, courtyard, inner choke, spire.  
3. Extreme reads as a dungeon keep: stone, torches, columns, the spire inside a place.  
4. Staging pocket still outside every turret and defender (WO-1520 assert stays loud).  
5. Troops can path staging → gate → courtyard → (choke) → spire. No new nav softlock.  
6. Win condition unchanged: raze `RaidSpire`. Extra destructibles are optional.  
   > ⚠ **CORRECTED 2026-09-09 by owner ruling - the sentence above is INCOMPLETE, and it is kept
   > verbatim rather than deleted because section 10 is a record of what it used to say.** Owner,
   > verbatim: ***"Keep both: spire raze OR full garrison wipe"***. There are **TWO independent
   > win paths**, either of which ends the raid in a victory: razing `RaidSpire`, **or** wiping the
   > garrison. Read acceptance 6 as *"win condition unchanged: raze `RaidSpire` **or** wipe the
   > garrison; extra destructibles are optional."* See section 10.
7. Missing KayKit pack does not fail the bake.  
8. `COMPILE_GATE_OK` + a raid-base layout regression that **fails** if Easy still has `props.count == 0` and `interiorWallLayers` semantics ignore named zones.

## 9. Paste for CLI

```text
Implement WO-1608 first (RaidBaseGenerator named zones + KayKit dresser), then WO-1609 Easy camp.
Do not hand-edit RaidBase_*.unity. Do not retune garrison HP. Do not touch RaidHudController or TroopController.
Reuse KayKitChallengeOutpostBuilder / DungeonSceneBuilder loaders; do not fork a third KayKit resolver.
```

---

## 10. RULED 2026-09-09 - the win condition, and which one the oracle pins

> ## ✅ RULED 2026-09-09. OWNER, VERBATIM: ***"Keep both: spire raze OR full garrison wipe"***
>
> **This is ruling (A) below, stated as canon rather than as "the shipped behaviour standing until
> she rules".** BOTH win paths are canon: razing `RaidSpire` ends the raid in a victory, and wiping
> the garrison ends the raid in a victory. Either signal is enough; `RaidScoring.Finalize` latches
> the result, and `RaidVictoryController.HandleVictory` de-duplicates the two signals through its
> `_handled` latch, so a camp whose last defender dies on the same frame the spire falls settles
> once.
>
> **Nothing in code changes.** `RaidBaseLayoutRegression.CaseGarrisonWipeWins` was already pinning
> exactly this, and it is now pinning a RULED behaviour instead of an undecided one - **it stays
> exactly as it is, and no seat may re-point it.** Acceptance 6 above carried the stale half of
> this and now carries a dated correction; the old sentence is deliberately left in place, because
> the record of what it used to say is what makes the correction checkable.
>
> **The two live win seams, read at source 2026-09-09:**
> - `RaidScoring.RaidWon` = `(_spire != null && _spire.IsDestroyed) || (_spawner != null && _spawner.Cleared)`
> - `RaidVictoryController.HandleCleared` -> `HandleVictory("garrison wiped")`, and
>   `HandleSpireRazed` -> `HandleVictory(...)` - two callers, one latched settle.

**HISTORY - the question as it stood before the ruling, kept unrewritten (CLAUDE.md section 15):**

**Acceptance 6 above says:** *"Win condition unchanged: raze `RaidSpire`. Extra destructibles are
optional."*

**The shipped oracle says something else.** Read at source 2026-09-09,
`Assets/Editor/Regression/RaidBaseLayoutRegression.cs:201-216`, case `CaseGarrisonWipeWins` FAILS
the suite unless all three of these hold:

| Assertion | The line it requires |
|---|---|
| `RaidVictoryController.cs` must NOT contain `"the raid is not over"` | failing text: *"still treats garrison wipe as a milestone - the player would be left hitting an empty camp."* |
| `RaidVictoryController.cs` MUST contain `HandleVictory("garrison wiped")` | failing text: *"HandleCleared no longer calls HandleVictory on garrison wipe."* |
| `RaidScoring.cs` MUST contain `_spawner != null && _spawner.Cleared` | failing text: *"RaidScoring.RaidWon no longer treats a wiped garrison as a win."* |

So **garrison wipe is pinned as an INDEPENDENT win path**, not as an optional extra on top of
razing the spire. That is a real deviation from acceptance 6, and it is deliberate work, not drift -
`WorkOrders/WORK_ORDER_1095_raid_stranding_watchdog_fired_again.md` records the same ruling in the
owner's own voice, dated the same day: *"a dead camp must settle, not wait for the empty spire to be
farmed."*

**The two possible rulings, and what each costs:**

- **(A) Garrison wipe IS a win.** Then acceptance 6 is the stale text and this section replaces it;
  the oracle already pins the shipped behaviour and nothing in code changes.
- **(B) Raze the spire is the only win.** Then `CaseGarrisonWipeWins` is pinning a behaviour the
  program does not want, and BOTH the case and `RaidVictoryController` / `RaidScoring` must change.
  That is a felt change to every camp, so it is not a doc edit.

WARNING: **No seat may resolve this by picking the reading it prefers.** A lane never re-points an oracle
on its own; a sanctioned re-point carries an owner ruling and a comment naming the WO. Until she
rules, the shipped behaviour (A) stands because it is what is on HEAD and what the suite proves -
**recorded as the current state, not as the decision.**

*(Raised by the Silo 0 pass, `docs/reference/READY_SILOS_2026-09-09.md` SILO 0 row 1607 and the
BLOCKED-ON-OWNER table. Unrelated to WO-1617, which touches `PlaceSpire` / `MapCatalogArt` in the
same suite's files but not this case.)*
