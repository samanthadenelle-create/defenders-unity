# WO-1608 — RaidBaseGenerator: named zones, gatehouse, and a KayKit dresser

**Status:** IMPLEMENTED — baked 2026-09-09; PO felt-test closes  
**Minted:** 2026-09-09 (CLI) — program WO-1607; banner bumped 1607 → 1612 in the same edit  
**Priority:** P0 — unblocks WO-1609 / 1610 / 1611  
**Lane:** World / Raid scenes. **One writer** on `Assets/Editor/WallTools/RaidBaseGenerator.cs` and any new dresser it calls.  
**Depends on:** nothing in this program (this IS the engine). WO-1520 staging must remain.  
**Do not touch:** `RaidHudController`, `TroopController`, `RaidScoring` star rules, `FeatureFlags.cs`, `.asmdef`, Village2, Iron Bastion.

---

## 1. Problem (proven)

`RaidBaseGenerator.BuildConfigLayout` (`Assets/Editor/WallTools/RaidBaseGenerator.cs:312-393`) only knows:

- an outer `BuildRing`
- optional inner `BuildRing`s (`interiorWallLayers`)
- `PlaceSpire` / `PlaceTowers` / BossSpawn / hero marker / staging

A gate is a skipped panel (`:333-338`). `props` is dead (`:35`). There is no courtyard, no gatehouse volume, no floor dress, no named spawn groups. Camps cannot become places until this baker speaks **zones**.

---

## 2. Reuse, do not reinvent

| Already built | Reuse how |
|---|---|
| `KayKitChallengeOutpostBuilder` token load (`:141-151`, `:1061+`) | KayKit dungeon FBX by **token** (`wall`, `wall_gated`, `barrier`, `floor_tile_large`, …). Deterministic folder order. Miss → warning + skip. |
| `DungeonSceneBuilder` `PackRoot` (`:88-89`) + `LoadModel` / `WallPiece` / `FloorPiece` | Dungeon Remastered 1.1 `fbx(unity)` path. Fortress palette = `wall_cracked.fbx` + `floor_tile_large.fbx` (`:1944-1968`). |
| `EnemyStrongholdBuilder` layered recipe | Courtyard → choke → raised keep + stairs + NavMeshLink. Port the **layout idea** into RaidBaseGenerator; do not start baking Village2. |
| `SyntyCastlePerimeterBuilder` | Hub already places Synty 5 m castle wall + battlements + gate + Tower_M. **Copy the load/seat pattern for Hard.** Do not retheme the player's wooden watchtowers (catalog ruling 2026-09-02). |
| `MagentaGuard.BuildUrpLitMaterial` | Any primitive fallback (already used at `:604`). |
| `GarrisonTurretArmer` + `"Watchtower"` in the turret name | Keep the token so arming still finds raid turrets (`PlaceTowerProp` `:866-869`). |
| `RaidNavBake` | Still the **one** nav baker. Run after this. This file still must not bake nav (`header :50-52`). |

Extract a shared KayKit loader **only if** copy-paste would create a third resolver. Prefer calling the existing static helpers; a small `KayKitPropLoader` editor helper is allowed if both dungeon builders can keep compiling unchanged. Do not move those builders onto a new abstraction in this ticket.

---

## 3. Named zones (the contract)

Author these as child transforms under the raid root (`RaidBase_<configId>`). Names are **load-bearing** — garrison seating and regressions read them.

| Zone | Name | Required | What lives here |
|---|---|---|---|
| Staging | `RaidStagingPoint` (already) | all | Unchanged WO-1520 math. |
| Approach | `Zone_Approach` | all | Road / dirt tiles + 1–2 banners, **outside** the outer wall, still inside the 140 m plane. Must not enter turret range. |
| Gatehouse | `Zone_Gatehouse` | all | A real opening with thickness: two gate-flank towers or dungeon `wall_gated` / `wall_doorway` pieces, collider on the structure layer, walkable gap ≥ 3.5 m (ClearanceReportBelow in the Outpost builder is 3 m — stay above that). |
| Courtyard | `Zone_Courtyard` | all | Inside the outer ring, outside the keep. Props + cover + most of the garrison. |
| Choke | `Zone_Choke` | Hard + Extreme | Inner gate + optional stairs / spike-grate décor. Single path to the keep. |
| Keep | `Zone_Keep` | Extreme required; Hard if Q3 = raised | Inner ring and/or raised platform. Spire sits in it. |
| Spire | `RaidSpire` object name (already) | all | Win condition. Do not rename. |
| Boss | `BossSpawn` (already) | all | Keep, not inside the spire mesh (`:360-365`). |

Easy: Staging, Approach, Gatehouse, Courtyard, Spire, Boss. No Keep / Choke required.

Hard: add Choke. Keep is the inner ring or raised platform (WO-1607 Q3; default = raised + stairs).

Extreme: all zones. Keep is a **dungeon room** (floor tiles, columns, torches), not a second fence.

---

## 4. Consume `props` (currently dead)

`scene-configs.json` `_schema.props` is `{ set: [structures-catalog ids], count }`. Raid rows all ship `set: []`, `count: 0`.

**Do both:**

1. **Read the existing shape** so a structures-catalog id in `set` still places (catalog `visualPrefabPath` via `Resources.Load`, same miss rule as towers).
2. **Add a raid-only dresser table** (JSON next to scene-configs, or a block on each raid row) that names **KayKit tokens**, not catalog ids. Catalog ids cannot see gitignored KayKit paths (`visualPrefabPath` is Resources-relative). KayKit must load via `AssetDatabase.LoadAssetAtPath` at **bake** time, the way dungeon builders already do.

Recommended row shape (dual-copy if it lives under Canonical JSON):

```json
"raidDress": {
  "kit": "hexagon-green | hexagon-red | dungeon-stone",
  "gate": "wall_gated",
  "wallModule": "barrier | wall | wall_cracked",
  "floor": "floor_dirt_large | floor_tile_large",
  "towersVisual": "building_watchtower_green",
  "props": [
    { "token": "tent", "count": 4, "zone": "Courtyard" },
    { "token": "barrel_large", "count": 6, "zone": "Courtyard" },
    { "token": "banner_red", "count": 4, "zone": "Gatehouse" }
  ]
}
```

Exact schema is implementer-owned as long as:

- Easy / Hard / Extreme rows are **not identical**.
- Tokens resolve through the existing KayKit folders listed in WO-1607 §4.
- A missing token logs once and skips that instance.
- Dual-copy rule if the file is Canonical JSON (`Resources` + `StreamingAssets`, byte-identical).

Do not invent a runtime Addressable load of KayKit for raid dress. Bake it into the scene.

---

## 5. Walls and the gatehouse (stop skipping a panel)

Today: `BuildRing` + `outerGates = { S, false, N-if-two, false }`.

Change:

1. **Keep** `BuildRing` for the curtain (nav + `WallSegment` colliders + destructibility already work). Do not delete the combat wall.
2. **Clad or replace the visual** on Easy/Hard/Extreme per `raidDress.wallModule`:
   - Easy: dungeon `barrier` / `barrier_half` / `barrier_corner` at authored scale (Outpost builder measured wall ~4 m long × 4 m tall × 1 m thick — **do not stretch the atlas over a cube**, WO-1000 §2.2).
   - Hard/Extreme: dungeon `wall` / `wall_cracked` / `wall_corner`.
3. **Gatehouse:** at each true gate, instantiate a gate **assembly** (flank pieces + `wall_gated` or `wall_doorway`) instead of leaving a hole. The walkable gap must be a measured opening, reported in the bake log (`[RaidBaseGenerator] GATE south width=Xm`).
4. `entranceCount` stays live: 2 = south + north (Easy), 1 = south only (Hard/Extreme).

If cladding KayKit onto `WallSegment` double-draws, hide the WallSegment **renderers** and keep the collider. Do not drop the Structure-layer blocker — that is how troops and turrets respect the wall (`header :17-20`).

---

## 6. Towers that are not pillars

`PlaceTowerProp` already prefers catalog `visualPrefabPath`, then `Structures/Tower_Medieval_Wood`, then a cylinder (`:872-932`).

**The cylinder is the live path, proven in the baked `.unity` files this session** (not inferred from the loader):

- Easy `RaidBase_raider_camp_small.unity`: Watchtowers have `Body`/`Cap` children (the fallback cylinder). `RaidSpire` has `Shaft`/`Crown`/`Plinth` (the fallback obelisk). Catalog id `tower_siege_tower` is stored; the Ballista mesh was never instanced.
- Same miss on Hard/Extreme spires (`tower_arcane_spire` → obelisk).
- **`RaidStagingPoint` is absent from all four baked scenes.** WO-1520 code exists; the bake has not been re-run. Hero still seats at `HeroStartPoint_PlayerSpawn` = `-(radius+8)` (easy z=−39, hard −57, extreme −62), which is the under-fire seat. First rebuild after this WO **must** emit `RaidStagingPoint` and `HeroControlEnsurer` will prefer it (`:685-692`).
- Easy `towers[]` is `[catapult, catapult]`. `ResolveTowerTypes` feeds **both** archer and mage slots from that palette, so all **4** turrets carry **catapult combat stats** on cylinder art. Do **not** silently rewrite `towers[]` in 1608 (that is a DPS change). Visual override only. Flag the palette for the owner on 1609.
- `EnemyStrongholdBuilder` already loads `StructureContent` by **asset path**, which is why Village2 can find `Tower_Medieval_Wood` while raids cannot. Copy that load, not `Resources.Load("Structures/...")`.

WO-1608 must load raid visuals with `AssetDatabase.LoadAssetAtPath` from:

- KayKit FBX (dungeon / hexagon) — bake-time instantiate, same as `DungeonSceneBuilder`
- Synty castle prefabs under `Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/`
- Tracked `Assets/StructureContent/` (owner watchtower / ballista / catapult) if a KayKit/Synty piece is missing

Do **not** point raid bake at Addressable R2 keys. A bake machine without the gitignored packs falls back to `WallTierData` slabs + MagentaGuard primitives and **warns**.

For raid bases only, allow a **visual override** from `raidDress.towersVisual` (KayKit hexagon watchtower / tower_A / tower_B). Behavior stays `DefenseTower` + `"Watchtower"` in the GameObject name. Catalog combat stats stay.

`EnsureUpright` already skips catapult / siege tower (`IsAuthoredSiegeMachine :909-913`). Keep that. Hexagon `building_tower_catapult_*` is a siege silhouette — add it to that skip list if used.

Siege machines belong in the **courtyard or on the wall** as emplacements, not as the only “tower” the player sees. Easy currently authors `towers[] = tower_catapult x2` while also placing 3+1 watchtowers from counts. After this WO, Easy’s **visible** skyline must include at least two watchtowers with a fighting top. Catapults may remain as extra emplacements (WO-1609 places them).

---

## 7. Ground

`RaidNavBake` still owns the 140 m walkable plane. On top of it, dress:

- Approach + courtyard: KayKit floor tiles at authored scale (`floor_dirt_large` Easy, `floor_tile_large` Hard/Extreme). Same rule as `KayKitChallengeOutpostBuilder.BuildFloorTiles`: tiles sit on the slab, slab does not bleed through.
- Extreme keep: dungeon stone + optional grate/spike tiles in the choke only.
- Do not stretch one quad with `dungeon_texture.png` (WO-1000: rainbows).

Fog / sky: readable **day** fight for Easy/Hard (no black crush). Extreme may go moodier (Outpost `ConfigureAmbient` is a dungeon night — use a **lighter** cousin, not a copy, or the raid HUD and troops vanish). No new lighting system.

---

## 8. Garrison seating markers (do not rewrite AI)

`RaidGarrisonSpawner` seats the composition on a ring (`:168`). After zones exist, add **named empty markers** the spawner can prefer:

- `GarrisonSlot_Gate_*` — Hold
- `GarrisonSlot_Yard_*` — default / Hunter
- `GarrisonSlot_Keep_*` — Hold on Extreme/Hard

If the spawner finds ≥ N slots, use them (navmesh snap, same as BossSpawn). If it finds none, **keep the ring** (today’s behaviour). Do not wait on WO-1595. Do not change targeting.

Cap still `liveCombatantCap`. Do not add enemies.

---

## 9. Staging and nav (do not regress)

- Staging distance is still computed from placed turret reach + defender perception (`:121-168`, `:437-503`). After gatehouse / outer dress, **re-measure** turret positions. If a new gatehouse tower sits further out, staging must move out with it. The unsafe assert stays a `LogError`, never softened.
- Approach dress must not place a shootable turret or a garrison slot in staging.
- After bake: `RaidNavBake`. Editor closed. Worktree only if Unity is running on `D:\eoa`.
- Report pinch points < 3 m in the bake log (copy the Outpost clearance idea, `:205-213`).

---

## 10. Files to edit

| File | Why |
|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | Zones, gatehouse, dress call, tower visual override, bake log. |
| New `Assets/Editor/WallTools/RaidBaseDresser.cs` (or equivalent) | KayKit instantiate + seat. Keep RaidBaseGenerator from becoming a second 1.5k-line dungeon builder. |
| `Assets/Resources/Data/Canonical/scene-configs.json` **and** StreamingAssets twin | `raidDress` (or equivalent) on the three raid rows. Dual-copy, LF, binary-safe. |
| `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs` | Prefer named `GarrisonSlot_*` if present; else ring. FlowTrace the choice. |
| `Assets/Editor/Regression/<new>RaidBaseLayoutRegression.cs` | See §12. Register inside DataRegression START/END fences. |

**Do not edit:** `RaidScoring` star formula, `RaidHudController`, `TroopController`, `EnemyStrongholdBuilder`, `Village2.unity`, `FeatureFlags.cs`.

---

## 11. Instrumentation

Permanent `FlowTrace` (never strip):

- `Step("RaidBase", "zone X built: pieces=N missing=M")`
- `Step("RaidBase", "GATE side=S width=Xm")`
- `Step("Garrison", "seating=slots|ring count=N")` at spawn
- `Warn` on every missing KayKit token / catalog prefab
- `Fail` if staging unsafe (already a LogError — also FlowTrace.Fail)

No per-frame traces.

---

## 12. Regression (RED first)

New suite, marker e.g. `RAID_BASE_LAYOUT_OK`. Cases that must go **red on current HEAD** before the fix:

1. `props` / `raidDress` empty on `raider_camp_small` → fail “Easy has no courtyard dress.”
2. No transform named `Zone_Gatehouse` under a built Easy root → fail.
3. Gate is only a skipped panel (no gatehouse assembly) → fail.
4. Easy turret visuals are the primitive cylinder fallback when KayKit hexagon watchtower exists on disk → fail.
5. Staging marker closer than turret max-reach + `StagingMargin` → fail (already covered by `RaidStagingMarkerRegression` — **do not duplicate**; call or sibling it).
6. `GarrisonSlot_*` count is 0 after a 1609 bake → fail (this case goes red until 1609; 1608 may author a helper that 1609 fills). For 1608 alone: assert the **spawner still rings** when slots are absent (no spawn-zero regression).

Prove RED on the old generator, then GREEN on the new. State the mutation in the RESULT.

---

## 13. Acceptance

1. A headless Easy bake log contains `Zone_Gatehouse`, `Zone_Courtyard`, `Zone_Approach`, and a `GATE south width=` line ≥ 3.5 m.  
2. Eye-level capture of the baked Easy root (even without 1609’s full prop list) shows a gate assembly, not a hole in a stick fence.  
3. Missing KayKit: bake completes, warnings only.  
4. Staging assert still loud.  
5. `COMPILE_GATE_OK`. Layout regression green on the cases in §12 that 1608 owns.  
6. Brace-balance + NUL check on every `.cs` touched.

## 14. Not in scope

Camp-specific prop counts and hexagon building lists (1609–1611). Raised-keep numbers for Hard (1607 Q3 / 1610). Dungeon ceiling over the Extreme keep (1611). Star HUD. Troop AI. HP retune.
