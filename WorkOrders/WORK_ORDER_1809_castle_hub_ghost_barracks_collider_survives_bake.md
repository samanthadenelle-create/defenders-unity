# WORK ORDER 1809 — the baked castle hub STILL carries the invisible `CastleBarracks` collider

**Status:** IMPLEMENTED
**RESULT:** `WorkOrders/WORK_ORDER_1809_castle_hub_ghost_barracks_collider_survives_bake.RESULT.md`

### ⚠ CORRECTION — the earlier "file was DELETED" reading in this ticket was WRONG
The block below was written while `Assets/Editor/Regression/EnemyTowerWallLosRegression.cs` did not
compile. My poll loop's `Test-Path` returned false at one instant and I reported the file DELETED and
the lane abandoned. **It was never deleted** — it is on disk, 13,044 bytes, and both dummies now
declare `CombatFaction IDamageableStructure.Faction => CombatFaction.Friendly;` explicitly (`:73`,
`:81`). The lane was mid-write; a single negative `Test-Path` sample against a file being rewritten is
not evidence of a deletion, and inferring an abandoned lane from it was a guess (§11B-A). The two
CS-error readings themselves were real and captured, and the registration at `DataRegression.cs:351`
now resolves. The history below is kept as written rather than rewritten (§15 frozen-record rule).

**BLOCKED 2026-09-16 20:13 (SINCE CLEARED) — a THIRD-PARTY compile error, not this ticket's code.**
`Assets/Editor/Regression/EnemyTowerWallLosRegression.cs` (UNTRACKED, written 20:13:10 by the WO-1808
lane, which has already filed `WORK_ORDER_1808_...RESULT.md` with `**Status:** IMPLEMENTED`) does not
compile:
```
EnemyTowerWallLosRegression.cs(62,64): error CS0535: 'EnemyTowerWallLosRegression.DummyPartyMember'
  does not implement interface member 'IDamageableStructure.Faction'
EnemyTowerWallLosRegression.cs(69,70): error CS0535: ... 'DummyFlyingPartyMember' ... same
```
`IDamageableStructure.Faction` is declared at `Assets/_Modules/Core/Combat/IDamageableStructure.cs:81`
(`CombatFaction Faction { get; }`; enum at `IDamageable.cs:28-34`, `Friendly = 0 / Hostile = 1`). Both
dummy fixtures need one line — a party member is village-side, so `public CombatFaction Faction =>
CombatFaction.Friendly;`. That is the WO-1808 lane's file and its semantic call, not this lane's.
Until it compiles, **no** batchmode entry point runs at all (`Builds/wo1809-oracle-before.log`:
`Scripts have compiler errors.`), so steps 2-4 of §3 below are queued, not skipped.

**20:47 — the file was DELETED by its lane and the tree is now broken in THREE places, all
third-party. Re-run of `Builds/wo1809-oracle-before.log`:**
```
DataRegression.cs(351,105): error CS0234: The type or namespace name 'EnemyTowerWallLosRegression'
  does not exist in the namespace 'DeNelle.Editor.Regression'   <- WO-1808's registration line
  survived the deletion of the file it calls; the lane removed the suite and left the call
RaidCasualtyRegression.cs(317,47): error CS1503: cannot convert from
  'DeNelle.Core.Catalog.ResourceCost' to 'DeNelle.Village.ResourceCost'   (and :334,47)
```
`DataRegression.cs:351` is the WO-1808 lane's registration, not this ticket's (this ticket's line is
the `hub-husk-free` entry above the `blank-start-census` fence and it compiles); `RaidCasualtyRegression.cs`
is a third lane's file. Both are outside this silo. **This lane cannot run a single Unity step until
the lead reconciles those two files.** Polled 20 minutes across three windows.

**Silo:** World / Hub scene (CastleHubBuilder + the hub scene bake) — file-disjoint from the raid lanes
**Owner report (2026-09-16):** *"can you check the bake castle scene. I think it still has the invisible barracks object"*
**Predecessor:** WO-1716 (the code defect — CLOSED). This ticket is the SCENE half that WO-1716 deliberately left open.

---

## 1. The owner is right, and it is proven three ways, not inferred

### (a) The scene YAML — `Assets/Scenes/Main_Castle_Overworld.unity`

`PrefabInstance &1439394418`, source
`Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Military_M/Military_Barracks.prefab`
(guid `de4d7b59ea6f7ac44855adb8d037ff98`), `m_Name: CastleBarracks`, root-level
(`m_TransformParent: {fileID: 0}`), localPos **(16, 0, -4)**, localScale (0.6, 0.90000004, 0.6):

```
m_RemovedComponents:
- {fileID: 5621597281765024167, ...}   # !u!23 MeshRenderer
- {fileID: 5621597281765991271, ...}   # !u!33 MeshFilter
m_RemovedGameObjects: []
m_AddedGameObjects: []
m_AddedComponents: []
```

The source prefab is a **single GameObject** — five YAML blocks only: root `&5621597281762770791`,
Transform, MeshFilter `5621597281765991271`, MeshRenderer `5621597281765024167`, **MeshCollider
`5621597281762736578` (`m_Convex: 0`, mesh guid `6d02b0b9e15f258428637c701e390f67`)**. So with the
renderer and the filter removed and nothing added, the instance has **zero renderers anywhere under
it and exactly one live, non-trigger, concave MeshCollider** shaped by the mesh that no longer
renders. That is `StructureVisualStrip.IsInvisibleNavBlockingHusk` == TRUE, by construction.

### (b) The visible barracks is a DIFFERENT object ~25 m away — so this is a husk, not "the host owns its collider"

`PrefabInstance &1727894720`, source `Assets/StructureContent/barracks.fbx`
(guid `5a258590045b55041aa22a4362144c9d`), name `Barracks`, **`m_TransformParent: {fileID: 1654537157}`
= `The8Structures_Storefronts_NPCPoints` → `CentralCourtyard_Plaza` → `CastleHubRoot`**, localPos
(3.5258868, -0.1438309, -19.217476), uniform scale 6.5048504. It carries
`Building{_buildingId: barracks}` (`&1727894725`), `AuthoredCastleStorefront{canonicalId: barracks,
legacyName: CastleBarracks, preserveAuthoredVisual: 1}` (`&1727894727`), `BuildingInteractable`,
`TripoMaterialFixer`, and a **BoxCollider** `m_Size (1.0000404, 0.9453529, 0.5234778)` (`&1727894723`).

It is **not** a child of `CastleBarracks`. The polyperfect instance is therefore not a skinned host
whose collider is legitimately its body (the `SkinOptions.Structure` / `StripColliders = true`
contract at `VisualFactory.cs:53,112,368-370`) — it is the orphan the WO-1716 header describes.

### (c) The DEVICE proves the player is hitting it — `logs/f8-inbox/device/SM02G4061955851/flags-20260917/logcat-after-372984.txt`

```
2524283: 09-16 13:08:59.614 [Flow:Camera] CAMERA SEAT EMBEDDED IN UNSEEN GEOMETRY - the occlusion
         spherecast returned NO occluder, yet the camera body overlaps collider 'CastleBarracks' on layer 0:Default
3076490: 09-16 19:43:23.001 [Flow:Camera] ... overlaps collider 'CastleBarracks' ...
3087271: 09-16 19:44:34.553 [Flow:Camera] ... overlaps collider 'CastleBarracks' ...
2524468: 09-16 13:09:02.928 [Flow:Repair] tap hit 'CastleBarracks' but RepairTarget could not wrap it
         - no repair prompt. HIT CHAIN: [0] CastleBarracks tag=Untagged {MeshCollider}
         (also 2532704, 2574052, 2574522, 2574728, 3058309 — six taps in one session)
2555831: 09-16 13:15:35.129 [Flow:Hub] suppressed physics on baked twin 'CastleBarracks' at (16.0,0.0,-4.0)
         - disabled 1 solid + 0 trigger collider(s), 0 nav obstacle(s) (a PLACED 'barracks' owns the singleton)
2555833: [Flow:Singleton] blank-town 'barracks': ... twins=[CastleBarracks] -> StoodDown
3116631: 09-16 19:46:55.330 [Flow:Singleton] blank-town 'barracks': ... twins=[CastleBarracks] -> LatchSkipped
```

**The exact runtime rule:** `HubStructureVisualInjector.SuppressBakedTwinPhysics`
(`Assets/_Modules/Village/HubStructureVisualInjector.cs:534-560`, the WO-950 discipline) *does*
disable that one solid collider — **but only on the `StoodDown` branch**. On the `LatchSkipped`
branch (the majority of the loads above) nothing suppresses it, and the camera lines + the six
`tap hit 'CastleBarracks'` lines are the player walking and tapping into an invisible wall.
WO-950 closed one path; the object is still there to be found by the others.

---

## 2. The CODE is already fixed. This is a SCENE artifact. No builder edit.

Read at source, not from a doc:

* `CastleHubBuilder.SkinHostUpright` (`Assets/Editor/CastleHubBuilder.cs:590-660`) now calls
  `StructureVisualStrip.StripHostVisual` and then `EnsureNoHusk` on **both** the `Skin FAILED`
  branch (`:628-635`) and as a post-skin assert.
* `NavMeshBakeFinal.PrepareBakedTwinsForDynamicCarving` (`Assets/Editor/NavMeshBakeFinal.cs:332-365`)
  refuses to size a carve from a twin that `RENDERS NOTHING`, drops any obstacle it held, and holds
  its colliders out of the bake — which is why the *navmesh hole* is already gone while the
  *collider* remains.
* `StructureRemovalHuskRegression` pins the pair, and says at `:52-56` that a scene-content
  assertion was **deliberately** left out because the scene still held the ghost "until the lead
  runs Defenders/Castle/Remove invisible structure husks".

So the one-time scene repair tool was written under WO-1716 and **never run**. That is the whole
defect. `Assets/Editor/StructureHuskCleanup.cs` is the sanctioned path (menu + `RemoveBatch`, with a
name/twin/gameplay-identity triple guard and a scene backup) and CLAUDE.md §3 forbids doing it by hand.

---

## 3. Work

1. ✅ `StructureHuskCleanup.PreviewBatch` — **DONE**, `Builds/wo1809-husk-preview.log`,
   `STRUCTURE_HUSK_PREVIEW_OK 1 husk(s) would be removed: CastleBarracks`:
   `CANDIDATE 'CastleBarracks' renders nothing but keeps 1 enabled solid collider(s) and 0 enabled
   carving NavMeshObstacle(s); localPos=(16.00, 0.00, -4.00) worldPos=(16.00, 0.00, -4.00)
   localScale=(0.60, 0.90, 0.60) prefab='...Military_M/Military_Barracks.prefab'`.
   The same run SKIPPED twelve other husk-shaped objects by name/identity, correctly:
   `MainKeep_CastleWithTwoLevels_Home`, `PlayerHeroHall_PersonalQuarters_HomeSpace`, `Hero (Blaise)`,
   `ExteriorTerrain`, and four `Wall_DoorJamb_L`/`Wall_DoorJamb_R` pairs. **That measurement is why the
   new oracle's husk/bounds cases are scoped to `*Barracks*` and only the CARVE case is scene-wide** —
   a scene-wide husk assertion would be red on twelve legitimate objects on its first run.
2. `StructureHuskCleanup.RemoveBatch` — destroys the husk, backs the scene up, saves.
3. `NavMeshBakeFinal.Run` — re-bake (the cleanup's own instruction; **not**
   `BakeOwnerUprightThenNavMesh`, which would also re-skin four storefronts and re-run
   `RealmStorePlacer`: a far wider blast radius for a deletion).
4. ✅ New oracle `Assets/Editor/Regression/HubHuskFreeSceneRegression.cs`
   (markers `HUB_HUSK_FREE_OK` / `_FAIL`) + one registration line in
   `Assets/Editor/Regression/DataRegression.cs` — **WRITTEN**, brace/NUL clean, registered
   immediately ABOVE the `blank-start-census` fence line (the other scene-opener), never below it.
   It resolves the hub from `SceneRouter.Castle` (no hardcoded literal — `HubSceneLiteralRegression`)
   and refuses the legacy `CastleCandidates[1]` branch rather than printing a green on the wrong
   scene. Five cases: scene opens with roots / no `CastleBarracks` that renders nothing / no
   `*Barracks*` husk by the SHARED predicate / no `*Barracks*` solid collider more than 1 m past what
   it renders / scene-wide no enabled carving `NavMeshObstacle` over 12 m on any world axis whose
   subtree renders nothing (the shipped ghost carve was 16.9 x 5.08 x 14.5 — the ceiling sits below
   it on purpose). Plus hollow-pass guards: <100 transforms or zero `*Barracks*` hosts is a FAIL.
   **Not yet run** — see the BLOCKED banner.

### Canon debt this creates (for whoever lands it, §15)
`Assets/Editor/Regression/StructureRemovalHuskRegression.cs:52-56` says the hub scene still holds the
ghost "until the lead runs Defenders/Castle/Remove invisible structure husks". That paragraph goes
stale the moment `RemoveBatch` saves, and it must be re-pointed at this new oracle in the SAME commit.
That file is outside this lane's silo, so it is surfaced here rather than edited.

**Not touched:** the `("CastleBarracks", "Structures/barracks", 180, 90)` row at
`CastleHubBuilder.cs:419` stays — `ApplyOwnerUprightCorrectionsBeforeBake` logs a benign
`upright skip - host '<name>' not in scene (ok if player-placeable only)` and continues
(`:452-457`). The catalog `barracks.repo.bakedTwins` entry `CastleBarracks` also stays — it is
pinned by `DataRegression.cs:3355`, and both `NavMeshBakeFinal` (`notFound` is logged, never failed)
and `SuppressBakedTwinPhysics` (null-safe) tolerate an absent twin.

## 4. Acceptance criteria

* [ ] `STRUCTURE_HUSK_CLEANUP_OK` on a fresh log, naming the removed object and the backup path.
* [ ] `NAVMESH_BAKE_OK` on a fresh log, with the scene bytes before/after and **no**
      `RENDERS NOTHING` line for `CastleBarracks` (it is gone, so the benign
      `bakedTwin 'CastleBarracks' ... NOT in this scene` warning is the expected line instead).
* [ ] `StructureHuskCleanup.PreviewBatch` re-run reports **no** husks.
* [ ] The new oracle passes, and FAILED before the cleanup (an oracle that never went red proves nothing).
* [ ] `python tools/gate_brace.py` exit 0 + NUL-free on every `.cs` touched.

## 5. What this cannot prove

Headless proves the object and the collider are gone from the saved scene. It cannot prove the
owner's *felt* "invisible wall" is gone — that is a PO felt-verify on the device (§13).
