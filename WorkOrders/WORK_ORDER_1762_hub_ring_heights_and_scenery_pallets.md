# WO-1762: size the hub authored ring to the 4.0 m family and remove the three scenery pallets

**Status:** FIXED - gated 2026-09-15 22:00 (COMPILE_GATE_OK, OWNER_CASTLE_RUNTIME_OK, REGRESSION_OK 547/547, HUB_RING_HEIGHT_OK); commit 3369a3f11; awaiting the owner felt test on the next APK
**Silo:** `Main_Castle_Overworld` authored ring — `Assets/Editor/OwnerCastleLayoutRepair.cs`,
`Assets/Editor/OwnerCastleRuntimeProof.cs`, one new height oracle. **Disjoint from the catalog**
(`Assets/Resources/Data/Canonical/structures-catalog.json` is NOT edited by this WO).
**Number:** PRE-ASSIGNED by the lead. The `CLI_LANES_WO_NUMBERS.md` banner was not touched by the
surveying agent; the lead owns the banner bump in the mint commit (CLAUDE.md §2).
**Source:** two READ-ONLY surveys, 2026-09-15 (no Unity run — a build held the project lock). Every
claim below was read at source this session and carries its `file:line` (CLAUDE.md §11B).

---

## 0. OWNER'S VERBATIM INTENT (2026-09-15, evening)

> (a) *"the storage pieces that I added don't seem to have any value, they don't connect — it flat out
> tells you that you have to add storage and it ignores the ones I put in there, so they're only
> scenery."*

> (b) *"You could remove the three storage that I added; that gives more room when you properly size
> the other components on the Y."*

> (c) *"make sure the invisible barracks doesn't populate — that's something we had to correct last
> time."*

Plus, on sizing: the lumber mill, iron mine, quarry and the Cathedral scale up to match the Crystal
Mine. The surveyed **4.00 m** Crystal Mine reading stands as her ruling.

⚠ **THIS REVERSES A PRIOR RULING, DELIBERATELY AND BY HER OWN WORDS.** On 2026-08-06 she said *"why is
the cathedral of magic so large? Normalize"* and the landmark tier (heightMul 1.25) was retired
(recorded at `Assets/_Modules/Village/HubStructureVisualInjector.cs:82-90`). Tonight she asks for the
Cathedral to come **up** to the family height. This is a ruling flip, not a defect, and the lane must
not "correct" it back.

---

## 1. THE LOAD-BEARING FINDING — the catalog cannot move these buildings

All four buildings in her list are **authored hub roots** in `Assets/Scenes/Main_Castle_Overworld.unity`
carrying `AuthoredCastleStorefront` with `preserveAuthoredVisual: 1`. The injector **returns early** on
that flag:

```
Assets/_Modules/Village/HubStructureVisualInjector.cs:804-808
  var authored = target.GetComponent<AuthoredCastleStorefront>();
  if (authored != null && authored.PreserveAuthoredVisual) { PrepareAuthoredStorefront(authored); return; }
```

`PrepareAuthoredStorefront` (`HubStructureVisualInjector.cs:779-796`) only repairs materials and
attaches capabilities — **it never fits and never scales**. So `repo.heightMul` reaches these objects
**not at all**, and every one of them authors `bakedTwins`, meaning the authored root is the ONLY
instance the player ever sees:

- `collector_forge` → `["IronMine"]`
- `collector_lumbermill` → `["Lumbermill_Wood_Storefront"]`
- `collector_farm` → `["Windmill_Food_Storefront"]`
- `arcane-tower` → `["ArcaneTower_MagicUpgrades"]`

(read from `Assets/Resources/Data/Canonical/structures-catalog.json`, v42, 29 entries)

⛔ **Editing `heightMul` here would be a silent no-op** — the exact failure the 2026-08-06 note already
records at `HubStructureVisualInjector.cs:82-90` ("the hub scene injects its own swap, so the catalog
row alone would not have moved it and the fix would have read as ineffective").

### The two authorities

| Path | Authority | Code |
|---|---|---|
| Catalog / player-placed | `targetHeight = StructureFactory.YHeightVariable (4 m) × repo.heightMul`, uniform fit; `repo.maxFootprint` is a downward-only width ceiling | `Assets/_Modules/Village/Catalog/StructureFactory.cs:59`, `:72-77`, `:122-133`; `Assets/_Modules/Core/Catalog/RepoProps.cs:400` |
| **Authored hub ring (these four)** | the PrefabInstance `m_LocalScale` modification in `Main_Castle_Overworld.unity` | `HubStructureVisualInjector.cs:804-808`; marker `Assets/_Modules/Village/AuthoredCastleStorefront.cs:11` |

### Current authored scales (parsed from the scene; markers at lines 7707 / 11521 / 17079 / 27358)

| canonicalId | scene root (legacyName) | source | authored uniform scale |
|---|---|---|---|
| `collector_forge` | IronMine | `Assets/StructureContent/IronMine.fbx` | **1.60927** |
| `collector_lumbermill` | Lumbermill_Wood_Storefront | `Assets/StructureContent/lumbermill.fbx` | **3.0888** |
| `collector_farm` (Quarry) | Windmill_Food_Storefront | `Assets/Prefabs/Village/Generated/Building_crystal-mine.prefab` ⚠ | **1.7869** |
| `arcane-tower` | ArcaneTower_MagicUpgrades | plain scene object at scale 1; child **`arcane tower(Clone)` baked into the scene** | **3.9978** |
| — | forge / armorer / jeweler / barracks / pet-house / workshop | .fbx each | 4.216 / 5.017 / 4.878 / 6.505 / 3.816 / 0.495 |

### DERIVED heights — indicative only, step 1 replaces them with measurements

`native_Y = 4.0 m fit target ÷ measured fit scale`, from `[Flow:Xform] … after Fit+SeatOnGround` in
`logs/device/2026-08-20-portal.log` and `logs/device/*.log` (09-04); then × authored scale. **The FBX
root import scale is unverified, so these are NOT measurements.**

| building | fit scale (log) | native_Y | × authored scale | derived height | vs 4.0 m |
|---|---|---|---|---|---|
| lumbermill | 4.00 (08-18) | 1.000 | ×3.0888 | ~3.09 m | −23% |
| IronMine | 3.52 (09-04) | 1.136 | ×1.60927 | ~1.83 m | −54% |
| arcane tower | 10.23 (09-04); was 4.00 (08-20) — the art re-pointed between those dates | 0.391 | ×3.9978 | ~1.56 m | −61% |
| Quarry | — | unproven (prefab guid `8a44e7b2…` unresolved in tree) | ×1.7869 | **unproven** | — |
| **CrystalMine (catalog path)** | 5.61 (09-04) | 0.713 | fit → **4.00 m** | **4.00 m** | reference |

Also read at source: the catalog's `_heightCadence` note (`structures-catalog.json:12`) is **stale** —
it still names LANDMARK 1.25 for `arcane-tower` while the row authors `heightMul: 1`. Fix or flag it
per CLAUDE.md §15 when this WO lands.

---

## 2. WHY THE PALLETS DO NOT COUNT — proven in one function

**Addresses** (`Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset`, file is
**uncommitted-modified**; `git diff --stat` = 21 insertions / 138 deletions):

| line | address | guid | asset |
|---|---|---|---|
| `:64` | `Structures/OwnerStorage/Iron_Pallet` | `33277f73…` | `Assets/Prefabs/Village/OwnerStorage/Iron_Pallet.prefab` |
| `:90` | `Structures/OwnerStorage/Wood_Pallet` | `4da844ac…` | `Assets/Prefabs/Village/OwnerStorage/Wood_Pallet.prefab` |
| `:181` | `Structures/OwnerStorage/Stone_Pallet` | `be5c38b9…` | `Assets/Prefabs/Village/OwnerStorage/Stone_Pallet.prefab` |

**Scene roots** (`Main_Castle_Overworld.unity`; name-override lines 2595 / 14799 / 23592), all children
of `The8Structures_Storefronts_NPCPoints < CentralCourtyard_Plaza < CastleHubRoot`:

| name | instance id | position | scale mod | scene source guid |
|---|---|---|---|---|
| `Wood_Pallet` | 160634900 | (17.06, 0.058, 3.72) | (0.954, 0.619, 1.192) | `f4a92ef5…` |
| `Iron_Pallet` | 1736337542 | (17.19, 0.0006, 11.79) | (unmodified = 1) | `3cf11469…` |
| `Stone_Pallet` | 1138258281 | (16.93, 0.0006, 19.26) | (unmodified = 1) | `12ea34ea…` |

⛔ **None carries `AuthoredCastleStorefront`. No `canonicalId`. No `bakedTwins` names them. Their scene
source guids are NOT the three OwnerStorage prefab guids** — the `Assets/Prefabs/Village/OwnerStorage/*.prefab`
are the derived catalog copies from the 2026-09-13 sizing pass, not these instances.

**The mechanism:**

```
Assets/_Modules/Core/Economy/TownBankCapacity.cs:977-1010  BuildSlots(...)
  var layout = state.BaseLayout;                                        // :996
  foreach row: CatalogRegistry.Get(d.itemId) -> repo.IsStorageContainer // :1005-1007
               repo.storageResource == want                             // :1008
               state.HasEverBuilt(d.itemId)   // existence co-gate       // :1009
```

Capacity is the base store **plus every `GameState.BaseLayout` row whose catalog entry is
`IsStorageContainer` and is in the ever-built ledger** (doc at `TownBankCapacity.cs:965-976`; the ONE
commit seam it names is `BuildModeController.cs:1888-1892`). A bare scene prefab instance writes no
BaseLayout row and no ever-built id, so it is **structurally invisible to the bank**. Her (a) is
correct in full.

Supporting seams: container predicate `TownBankCapacity.cs:313-314` → `RepoProps.IsStorageContainer`
(`Assets/_Modules/Core/Catalog/RepoProps.cs:282`); the "no catalog row authors IsStorageContainer"
failure text at `TownBankCapacity.cs:627-638`.

**The prompt string she is quoting:**

```
Assets/_Modules/Core/UI/BankOverflowToastPresenter.cs:312
  return $"{s.ResourceName} storage FULL - {s.Lost} lost. Build or upgrade a {s.ContainerName}, or spend {s.ResourceName.ToLowerInvariant()}.";
```

(intent recorded at `BankOverflowToastPresenter.cs:291`.)

### The alternative we are NOT taking, stated so nobody re-derives it

To make an authored root count, three things must agree (the WO-1710 `collector_forge` precedent):

1. an `AuthoredCastleStorefront` with `canonicalId` = `lumberyard` / `foundry` / `silo`;
2. that root's name in the catalog row's `repo.bakedTwins`, with `repo.singleton` set — otherwise
   `Assets/Editor/Regression/DataRegression.cs:3247-3249` reds;
3. a matching `StrategicPlacementMigration.BakedRows` entry
   (`Assets/_Modules/Village/BuildMode/StrategicPlacementMigration.cs:90`) so the founding load writes
   the BaseLayout row + ever-built id — *"the census and this list must agree or
   `DataRegression.cs:3252-3260` goes RED"* (catalog note, `structures-catalog.json:1286`).

That is roughly 3× the work of removing them, and she ruled removal.

---

## 3. THE "INVISIBLE BARRACKS" PRECEDENT — two real ones, run both

The literal string "invisible barracks" is not in the tree. Two prior corrections match her words:

**(A) The husk — an invisible building with live collision.** Closest match to "invisible", and
**precisely the failure mode of removing three objects from this ring.**
`WorkOrders/WORK_ORDER_1716_castle_hub_navmesh_no_holes_under_authored_buildings.md` — **FIXED**, root
cause proven: `CastleHubBuilder.SkinHostUpright` stripped a host's visual without clearing its collider
/ NavMeshObstacle. Fix: `DeNelle.Village.World.StructureVisualStrip.StripHostVisual` + `EnsureNoHusk`
(`Assets/Editor/CastleHubBuilder.cs:607`, `:612`, `:628`, `:646`). Pinned by
`Assets/Editor/Regression/StructureRemovalHuskRegression.cs` (idempotence + nothing-left-behind,
`:37-46`).

**(B) The barracks that populates when it should not.**
`WorkOrders/WORK_ORDER_1540_blank_start_census_sees_a_barracks_because_a_flag_bleeds_between_suites.md`
— **IMPLEMENTED** (`c0c30f715`, 2026-09-07; gated by the 2026-09-09 wave, 492/492): the blank-start
census saw a baked `CastleBarracks` because `ff.barracks` was ON in batchmode. Pinned by
`Assets/Editor/Regression/BlankStartCensusRegression.cs`.

The live standdown gate that keeps it hidden: `HubStructureVisualInjector.cs:261-262`
(`StructureSingleton.IsPlayerBuilt("barracks")` + `AuthoredCastleStorefront.IsBoundAuthoredRoot`) and
`:267` (`StructureSingleton.MayBakedTwinSurface("barracks")`). Ownership rules pinned by
`Assets/Editor/Regression/AuthoredBarracksProvenanceRegression.cs` (marker `AUTHORED_BARRACKS_OK`).
`CastleBarracks` is a marker-bearing child of the SAME ring (scale 6.5049) — touching the ring's child
set is exactly what could re-open this.

---

## 4. STEPS — in order

1. **MEASURE FIRST (CLAUDE.md §12).** Add a read-only editor method (home: beside
   `Assets/Editor/OwnerCastleRuntimeProof.cs`, which already walks the ring at `:71-157`): open the hub
   scene, iterate every `AuthoredCastleStorefront`, print encapsulated `Renderer.bounds.size` per root
   against the 4.0 m reference, with its own marker. **No existing tool does this** —
   `Assets/Editor/StructureHeightAudit.cs` and `Assets/Editor/StructureSizeAudit.cs` are catalog-only
   (they go through prefab load / `MeasureUprightFootprintMetres`). This is the before/after proof and
   it converts the DERIVED heights in §1 into measured ones.
2. **REMOVE the three pallets** through the `OwnerCastleLayoutRepair` seam, using
   `StructureVisualStrip.StripHostVisual` + `EnsureNoHusk` (the WO-1716 rule) — **never a raw destroy,
   never a YAML edit.**
3. **RESCALE the four roots to a measured 4.00 m** (uniform), in the same seam:
   `Lumbermill_Wood_Storefront` (3.0888), `IronMine` (1.60927), `Windmill_Food_Storefront`/Quarry
   (1.7869), and the baked child `arcane tower(Clone)` (3.9978) under the scale-1
   `ArcaneTower_MagicUpgrades` root. **Factors come from step 1, not from the derived table.**
   *Open question for the owner, one line:* the Quarry root wears
   `Assets/Prefabs/Village/Generated/Building_crystal-mine.prefab` — scale it, or re-point the art?
4. **NAVMESH re-bake** (pallets removed + four footprints grew; these roots carry `NavMeshObstacle`,
   e.g. scene `&559091513` under the IronMine root). Judge the bake by CONTENT, not the marker
   (memory `bake-marker-can-be-green-on-the-wrong-operation`).
5. **UPDATE the recipe + its asserts in the SAME commit.** Re-save
   `Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab` via `OwnerCastleLayoutRepair.Apply`
   (`Assets/Editor/OwnerCastleLayoutRepair.cs:108`, path const at `:22`); change
   `Assets/Editor/OwnerCastleRuntimeProof.cs:44` `ring.childCount == 14` → **11** and `:157`
   `Require(… no-marker children == 3, "Expected three props")` → **0**. The three props ARE the
   pallets (11 markers + 3 props = 14). Leaving either is a guaranteed red.
6. **BARRACKS CHECK, both halves.** Re-run `AuthoredBarracksProvenanceRegression`
   (`AUTHORED_BARRACKS_OK`), `BlankStartCensusRegression`, and `StructureRemovalHuskRegression`.
   Confirm `CastleBarracks` still stands down under `HubStructureVisualInjector.cs:261-267`.
7. **NEW ORACLE STAYS.** Register step 1's method as a suite so the ring's heights are pinned going
   forward — `Assets/Editor/Regression/StructureCadenceRegression.cs` explicitly does not cover this
   path (`:107-114` upper-bound-only, so "too small" is invisible to it; `:122-127` "THE HUB INJECTOR
   PATH IS NOT COVERED").
8. **BOARD.** Flip this file's `**Status:**` line and write
   `WorkOrders/WORK_ORDER_1762_hub_ring_heights_and_scenery_pallets.RESULT.md` in the same commit as
   the work; the lead regenerates `BOARD.html` (`python tools/board_build.py`).

### Do NOT touch

- `Assets/Resources/Data/Canonical/structures-catalog.json` — **no `heightMul` edit.** It cannot reach
  these roots (`HubStructureVisualInjector.cs:804-808`); editing it is the silent no-op this WO exists
  to avoid.
- The three `Structures/OwnerStorage/*` Addressables entries in
  `Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset` (`:64`, `:90`, `:181`) — they are
  still the art for the **catalog rows** `lumberyard` / `foundry` / `silo`
  (`visualPrefabPath: Structures/OwnerStorage/*_Pallet`). Removing the scene copies does not retire the
  addresses. CLAUDE.md §16 still applies: any build that reaches a device or a store runs
  `tools\r2-ship.ps1`.
- The catalog pallets' `maxFootprint` values — `lumberyard 1.78790474`, `foundry 1.72428513`,
  `silo 1.55203629`, all at `heightMul 0.5`. These are the frozen 2026-09-13 measurement (see §5).
- `Assets/Scenes/Main_Castle_Overworld.unity` by hand. Owner pref + CLAUDE.md §3: curated scenes are
  changed through a scripted seam, never edited as YAML.

### Unproven / needs her word

- **Which "invisible barracks" she means** — (A) the husk or (B) the census populate. The brief runs
  both checks, so the ambiguity costs nothing, but the answer should be recorded.
- **The Quarry's art.** The root wears a crystal-mine prefab rather than `farm.fbx`. Scale it, or
  re-point it?
- **Whether the catalog-built pallets keep their 2026-09-13 sizing** once the scene copies are gone
  (see §5 — the reconciliation says yes, but it is her ruling to confirm, not ours to assume).
- **The Quarry root's native height** — the scene source prefab guid `8a44e7b2…` did not resolve in the
  tree, so no derived height could be computed for it. Step 1 measures it.
- **Whether the FBX prefab roots carry a non-1 import scale**, which would shift every DERIVED number
  in §1. Step 1 settles it.
- **Whether the arcane-tower art re-point between 2026-08-20 and 2026-09-04 is the whole cause** of the
  Cathedral reading small (its fit scale moved 4.00 → 10.23 across those dates, i.e. the mesh changed).

---

## 5. COMPANION NOTE — the two storage rulings, and how they reconcile

**Ruling 1 — 2026-09-13 (memory `owner_storage_pallet_size.md`), PRESERVE AS REFERENCE.**
For the catalog-built `lumberyard` / `foundry` / `silo`, the owner explicitly chose *"Match my saved
castle pallets"*, adding *"I hand authored them and placed them next to each collector"* and *"storage
and collector are easy to isolate"*. The instruction: measure the corresponding saved pallet by
semantic name and position beside its collector; preserve art, flat orientation and saved scene
layout; do not use a generic regression ceiling as a creative sizing target.

**Ruling 2 — 2026-09-15 (tonight), REMOVE THE SCENE COPIES.**
*"You could remove the three storage that I added; that gives more room when you properly size the
other components on the Y."*

**The apparent conflict:** ruling 2 deletes the very scene objects ruling 1 told us to preserve as the
sizing reference.

**How they reconcile — the measurement was already frozen into the tree, so the reference outlives the
objects:**

1. The sizes are **captured in the catalog**, not in the scene. The `lumberyard` / `foundry` / `silo`
   rows carry `heightMul 0.5` with `maxFootprint 1.78790474 / 1.72428513 / 1.55203629` — those numbers
   ARE the 09-13 measurement of her pallets, and they live in
   `Assets/Resources/Data/Canonical/structures-catalog.json`, which this WO does not touch.
2. The **art survives independently**. The catalog rows point at
   `Structures/OwnerStorage/{Wood,Iron,Stone}_Pallet`, registered in `Structure_Art.asset` at `:64`,
   `:90`, `:181` to `Assets/Prefabs/Village/OwnerStorage/*.prefab`. Those prefabs carry **different
   guids** from the three scene instances (`f4a92ef5…` / `3cf11469…` / `12ea34ea…`), i.e. they are
   already independent derived copies. Deleting the scene instances does not touch them.
3. What is actually lost is the **layout** — "beside each collector" — not the sizing. Ruling 1's
   layout clause was in service of the measurement, and the measurement is done.
4. Ruling 2 is therefore a **narrowing of ruling 1, not a contradiction of it**: ruling 1 governs how
   the *catalog-built* containers look; ruling 2 removes the *scene scenery* that was never a container
   in the first place (§2 proves it could never have counted).

**The one thing to confirm with her, in one line, before step 2:** that the catalog-built pallets keep
those `maxFootprint` / `heightMul 0.5` values after the scene copies go. **Do not silently re-tune them
inside this WO** — that would be a creative decision smuggled into a structural one (CLAUDE.md
Architecture law).

**A note for the next seat, in this file's own spirit:** the reason this section exists is that the
09-13 ruling lives in an agent memory file and the 09-15 ruling arrived in chat. Neither is in the
repo. Whoever lands this WO should record the reconciliation in the RESULT file so the next session
reads it from the tree rather than re-deriving it (CLAUDE.md §15).

---

## 6. EVIDENCE INDEX (everything above, read at source 2026-09-15)

| Claim | Authority |
|---|---|
| Authored roots ignore the catalog fit | `Assets/_Modules/Village/HubStructureVisualInjector.cs:804-808`, `:779-796` |
| The 2026-08-06 "normalize" ruling being reversed | `Assets/_Modules/Village/HubStructureVisualInjector.cs:82-90` |
| Fit-to-height math + 4 m base | `Assets/_Modules/Village/Catalog/StructureFactory.cs:59`, `:72-77`, `:122-133` |
| `heightMul` semantics | `Assets/_Modules/Core/Catalog/RepoProps.cs:400` |
| Cadence rationale (stale on `arcane-tower`) | `Assets/Resources/Data/Canonical/structures-catalog.json:12` |
| Storage capacity = BaseLayout + ever-built | `Assets/_Modules/Core/Economy/TownBankCapacity.cs:965-1010` |
| Container predicate | `TownBankCapacity.cs:313-314`; `Assets/_Modules/Core/Catalog/RepoProps.cs:282` |
| The "add storage" prompt | `Assets/_Modules/Core/UI/BankOverflowToastPresenter.cs:312` (intent `:291`) |
| Baked-twin registration precedent | `Assets/_Modules/Village/BuildMode/StrategicPlacementMigration.cs:90`; `Assets/Editor/Regression/DataRegression.cs:3247-3260`; `structures-catalog.json:1286` |
| Husk removal fix | `Assets/Editor/CastleHubBuilder.cs:607-646`; `Assets/Editor/Regression/StructureRemovalHuskRegression.cs:37-46` |
| Barracks standdown gate | `Assets/_Modules/Village/HubStructureVisualInjector.cs:261-267` |
| Barracks ownership pin | `Assets/Editor/Regression/AuthoredBarracksProvenanceRegression.cs` |
| Blank-start census pin | `Assets/Editor/Regression/BlankStartCensusRegression.cs` |
| Ring asserts to update | `Assets/Editor/OwnerCastleRuntimeProof.cs:44`, `:157`, compare at `:141-148` |
| Scripted repair seam + recipe | `Assets/Editor/OwnerCastleLayoutRepair.cs:22`, `:67-70`, `:108`, `:230-238` |
| Cadence suite's stated blind spots | `Assets/Editor/Regression/StructureCadenceRegression.cs:107-114`, `:122-127` |
| Catalog-only audits (why a new oracle is needed) | `Assets/Editor/StructureHeightAudit.cs`; `Assets/Editor/StructureSizeAudit.cs` |
| Fitted-bounds measurements | `logs/device/2026-08-20-portal.log`; `logs/device/*.log` (2026-09-04) |

---

## 7. PHASE 1 — CODE LANDED, THEN RUN 2026-09-15

> ### ✅ RESULTS — the lead ran the chain. §1's DERIVED heights are now MEASURED.
>
> | Run | Log | Outcome |
> |---|---|---|
> | apply | `Builds/wo1762-apply.log` 21:19 | `HUB_RING_APPLY_OK` — three pallets removed, four roots rescaled |
> | re-point | `Builds/wo1762-repoint.log` 21:25 | `Stone_Quarry ← Quarry.fbx`, scale **0.7861**, **above=5.09 total=6.35 belowGround=1.27** (native metres, pre-scale) |
> | navmesh | bake2 | 3422 verts / 1644 tris |
> | audit (post) | `Builds/hub-ring-scale-factors.suggested.json` 02:38Z | see the table below |
>
> **Measured ring heights, 2026-09-15 post-apply** (live `Renderer.bounds`):
>
> | Root | Measured | Ruled? |
> |---|---|---|
> | LumberMill | **4.00** | ✅ owner ruling — family height |
> | IronMine | **4.00** | ✅ owner ruling — family height |
> | ArcaneTower | **5.00** | ✅ owner ruling — Cathedral *"a little bit larger"* |
> | Stone_Quarry | **5.00** total (**4.0 visible + 1.0 pit**) | ✅ owner ruling |
> | Jeweler 4.89 · RealmStore 4.90 · Barracks 4.04 · Weaponsmith 4.23 · Armorer 5.03 · Echo_Hollow 3.54 · Crafting 2.33 | — | ❌ **never in the owner's ask** — baseline only |
>
> ⚠ **The 3.20 m trap in §8.3 was REAL and was avoided**: the re-point log's
> `above=5.09 total=6.35` is exactly the split that predicted it, and the `aboveGround` fit mode is
> what put 4.0 m of building above the courtyard instead of 3.2 m.
>
> **Regression 21:44 → `REGRESSION_FAIL 539/541`.** Both reds were this lane's and both are now
> closed — see §9.

The commands below are the lead's, in order, and remain the re-run recipe.

### What landed

| Path (repo-relative) | What it is |
|---|---|
| `Assets/Editor/HubRingHeightAudit.cs` | NEW. `DeNelle.Editor.HubRingHeightAudit.Run` — step 1's measurement. Read-only; proves it by SHA-256'ing the scene before/after. Markers `HUB_RING_HEIGHT_AUDIT_OK` / `_FAIL`. Also declares `HubRingScaleRow` / `HubRingScaleFile`, the one type the audit writes and the apply reads. |
| `Assets/Editor/HubRingHeightApply.cs` | NEW. `DeNelle.Editor.HubRingHeightApply.Run` — steps 2-3. Markers `HUB_RING_APPLY_OK` / `_FAIL`. |
| `Assets/Editor/Regression/HubRingHeightRegression.cs` | NEW oracle `[hub-ring-height]`, markers `HUB_RING_HEIGHT_OK` / `_FAIL`. **Stands down via `RegressionOutcome.Skip`** while `ApplyLanded` is `false`. |
| `Assets/Editor/Regression/DataRegression.cs` | ONE registration line (+ a 4-line comment), placed beside `gear-addressable-group-oracle` — deliberately ~140 lines away from WO-1761's hunk. |
| `Assets/Editor/OwnerCastleLayoutRepair.cs` | The three pallet `Row`s made OPTIONAL, and the ring child-count check derived from what was found. Without this the repair seam would **throw** the first time it opened a ring the apply had already fixed. |
| `Assets/Editor/OwnerCastleRuntimeProof.cs` | `:44`'s `childCount == 14` and `CheckCapabilities`' `props == 3` now **derived from the recipe prefab** (step 5's oracle update, done the non-staling way). The eleven-marker floor is unchanged. |

### The lead's command list, in order

```
# 1. MEASURE (read-only; safe to run at any time, changes nothing)
.\run-unity-method.ps1 -Method DeNelle.Editor.HubRingHeightAudit.Run -LogName wo1762-audit.log `
    -ExpectMarker HUB_RING_HEIGHT_AUDIT_OK
#    -ExpectMarker (run-unity-method.ps1:44-46, WO-984) makes the RUNNER judge the marker.
#    Judge by HUB_RING_HEIGHT_AUDIT_OK on a FRESH log, never the exit code.
#    Reads: Builds/castle-validation-20260913/WO1762_hub_ring_height_audit.txt
#           Builds/hub-ring-scale-factors.suggested.json

# 2. REVIEW + PROMOTE the plan. Prune to the four roots the owner named, then:
#    copy Builds\hub-ring-scale-factors.suggested.json -> Builds\hub-ring-scale-factors.json
#    (targetUniformScale is ABSOLUTE, not a multiplier — that is what makes step 3 idempotent.)

# 3. APPLY (writes the scene + re-saves the recipe; backs both up first)
.\run-unity-method.ps1 -Method DeNelle.Editor.HubRingHeightApply.Run -LogName wo1762-apply.log `
    -ExpectMarker HUB_RING_APPLY_OK
#    Judge by HUB_RING_APPLY_OK. Run it TWICE: the second run must print "noop" and save nothing.
#    Open the before/after PNGs in Builds/castle-validation-20260913/.

# 4. NAVMESH — footprints grew and three NavMeshObstacle carvers are gone
.\run-unity-method.ps1 -Method DeNelle.Editor.NavMeshBakeFinal.Run -LogName wo1762-bake.log
#    NavMeshBakeFinal.cs:63 is the batchmode entry; it defaults to the hub scene (HubScene) and
#    accepts -bakeScene <path>. JUDGE BY CONTENT — surfaces baked, the scene byte delta it prints
#    at :110, and its own "BAKE NOT PERSISTED" branch at :115 — never by a marker
#    (memory bake-marker-can-be-green-on-the-wrong-operation).

# 5. FLIP THE ORACLE, in the SAME commit as the apply output
#    HubRingHeightRegression.cs -> ApplyLanded = true

# 6. RE-PROVE
.\run-unity-method.ps1 -Method DeNelle.Editor.OwnerCastleRuntimeProof.Run -LogName wo1762-proof.log `
    -ExpectMarker OWNER_CASTLE_RUNTIME_OK
#    OWNER_CASTLE_RUNTIME_OK. This is ALSO the only thing that pins scene == recipe, which the new
#    oracle depends on (it measures the recipe so it need not open a scene inside the gate).
#    Then the full suite + the §6 barracks halves:
.\run-unity-method.ps1 -Method DeNelle.Editor.Regression.DataRegression.RunAll -LogName wo1762-reg.log
#    REGRESSION_OK <n>/<n> suites, and [hub-ring-height] must no longer carry [SKIPPED].
```

### ⛔ TWO THINGS THE LEAD MUST KNOW BEFORE COMMITTING

1. **`Assets/Editor/OwnerCastleLayoutRepair.cs`, `OwnerCastleRuntimeProof.cs`, `OwnerCastleLayoutAudit.cs`,
   `OwnerCastleValidation.cs`, their `.meta`s, `Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab(.meta)`
   and `Assets/StructureContent/OwnerCastleMaterials/` are ALL UNTRACKED** (`??` in `git status`) as of
   2026-09-15. The working tree compiles; **HEAD does not**, if the three new files are committed by
   explicit path without them — `HubRingHeightApply` references `OwnerCastleLayoutRepair.LayoutPrefab`
   and `OwnerCastleLayoutAudit.*`, and the tracked `CastleHubBuilder.cs:141` already loads that
   untracked prefab. The two files listed above as "modified" are edits to UNTRACKED files.
2. **The three new `.cs` files have no `.meta` yet** — Unity mints them on the first import. They belong
   in the same commit as the code, or the next clone gets fresh GUIDs.

### Decisions this lane made, for the lead to ratify

1. **Pallet removal is `StripHostVisual` + `EnsureNoHusk` FOLLOWED BY `DestroyImmediate` of the whole
   child.** §4 step 2 said "never a raw destroy", and `StructureVisualStrip`'s own header says the
   opposite for this case, verbatim at `Assets/_Modules/Village/World/StructureVisualStrip.cs:62-63`:
   *"Child GameObjects — callers that want a child's visual gone destroy the whole child object,
   which leaves no husk by construction."* That pair is the **in-place re-skin** seam; a pallet's
   renderers are on its children, so the strip alone would remove nothing and `EnsureNoHusk` would
   correctly no-op while the pallet still stood there. The lane read "never a raw destroy" as
   **"never leave a husk"** and asserts the husk invariant directly: no `*_Pallet` transform under
   the ring, and every solid `Collider` / carving `NavMeshObstacle` still intersecting each pallet's
   recorded pre-destroy world bounds is NAMED in the evidence.
2. **Re-seat after rescale.** A uniform scale multiplies about the pivot, and the Cathedral's visual
   child was seated by shifting its position down (`OwnerCastleLayoutRepair.cs:175`), so its pivot is
   not at its base. The apply preserves `bounds.min.y` across the rescale; the audit prints
   `pivotOffsetY` per root so the correction is auditable. **Not doing this sinks or floats every
   rescaled building.**
3. **The oracle stands down on a CONSTANT, not on a sniff.** "Pallets present ⇒ skip" would make
   RULE 2 unable to ever go red (present ⇒ skip, absent ⇒ trivially true) — a hollow pass.

### Still unproven after this lane

- **Every height in §1.** Nothing was measured; step 1 has not run.
- **The Quarry's art question** (§4 step 3) is untouched — the apply scales whatever the row names.
- **The seven roots NOT being rescaled** (forge / armorer / jeweler / barracks / pet-house / workshop
  / RealmStore) have unknown heights. The new oracle's RULE 1 covers **every** authored root, so once
  `ApplyLanded` flips, one of them may red. The audit output is what decides whether the lead widens
  the apply or narrows the suite; do not guess.
- **`CastleBarracks` may render nothing** (runtime standdown, `HubStructureVisualInjector.cs:261-267`,
  plus the WO-1716 husk history). Both new tools report a zero-renderer root as a named
  partial-skip, never a throw or a divide-by-zero — but whether that is the RIGHT answer for the
  barracks is the owner's call.
- **Three standalone proofs hard-code the pallet names and will break after the apply**, by design of
  this lane's scope (none is registered in `DataRegression`, so the gate is unaffected):
  `Assets/Editor/StoragePalletSizingAudit.cs:37,:121`, `Assets/Editor/StoragePalletPlacementProof.cs:52`,
  `Assets/Editor/StoragePalletFillPlayProof.cs:159-160`. `StoragePalletSizingAudit` in particular does
  `all.Single(t => t.name == name)` against the hub scene and will throw. They need their own ticket.
- **The §5 question for the owner** — that the catalog-built `lumberyard` / `foundry` / `silo` keep
  their 2026-09-13 `maxFootprint` / `heightMul 0.5` after the scene copies go — is still unasked.
- **Which "invisible barracks" she meant** (§4's open question) is still unrecorded.

---

## 8. SCOPE ADDITION — the owner's Quarry model (2026-09-15, evening)

This closes §4 step 3's open question (*"the Quarry root wears a crystal-mine prefab — scale it, or
re-point it?"*): she supplied the art. Everything below was read at source 2026-09-15; no Unity ran.

**On disk, confirmed:** `Assets/StructureContent/Quarry.fbx` (484,460 bytes) and
`Assets/StructureContent/Quarry.fbm/` with seven textures (`DarkWood.jpg`, `Dirt.png`,
`GraniteTexture.png`, `manonarywall.png`, `RoofShingles.png`, `TudorStucco.png`, `WoodWindow.png`).
`git check-ignore -v` on the FBX and a texture returns **rc=1** — they are **NOT gitignored**, so they
are ordinary new untracked files and belong in the commit. No `.meta` yet (Unity mints them on the
lead's next batchmode run — they go in the same commit or the next clone gets fresh GUIDs).

### 8.1 The registration chain — it is a NO-OP for this model, and that is correct

| Step | What it actually does | Effect on `Quarry.fbx` |
|---|---|---|
| `DeNelle.Editor.CatalogPrefabImporter.CopyKitToResources` (`CatalogPrefabImporter.cs:126`) | A **PACK MIRROR**. Copies from `SrcRoot` (polyperfect `_M/Prefabs_M/`), `SrcRootT`, `SrcRootKayHex` into `DstDir = AssetRoots.StructureContent + "/"`, idempotent via `if (File.Exists(dst)) skipped++` (`:140-144`) | **Nothing.** The file is already AT the destination and has no pack source. Owner-sourced art is *deliberately* absent from that table — its own comment at `:71-77` says exactly this about the Tripo watchtower ladder |
| `DeNelle.Editor.StructureAddressablesMigrator.MarkCatalogArt` (`:518`) → `MarkInto` (`:537`) | Iterates **`ReadCatalogArtKeys()`** (`:583`), which regex-scrapes `"Structures/..."` **out of `structures-catalog.json`**, then probes `.prefab/.fbx/.png/...` under `ArtRoot` (`:552`) | **Nothing.** ⛔ It can only register an address **the catalog already names**. `Structures/Quarry` is not in the catalog, so it is not even in the input set — it will not be marked and will not appear in the `missing` warning either |

⛔ **So with instruction (3) honoured (no catalog edit), the documented chain CANNOT register
`Structures/Quarry` — and it does not need to.** The authored hub root is a **direct scene GUID
reference**, exactly like every other ring root (`IronMine` ← `Assets/StructureContent/IronMine.fbx`,
§1 table). A scene-referenced asset ships as a **scene dependency** of the player build; it is not
Addressable content and needs no `tools\r2-ship.ps1` push for this path.
⚠ **UNPROVEN:** the known hazard of an asset being *both* scene-referenced and Addressable is
**duplication, not absence** — which is what Addressables' "Check Duplicate Bundle Dependencies"
reports. That analyze has **not** been run here. If the lead later adds a `Structures/Quarry` address,
re-read this paragraph first.

### 8.2 Does a multi-material FBX with a `.fbm` folder survive? — precedent, and one live hazard

**The `.fbm` precedent is real but the cited one is retired.** `Assets/Resources/Structures/` **no
longer exists** (`ls` → *No such file or directory*, and CLAUDE.md §16 says so), so the
`.gitignore:170-192` `jeweler.fbm` / `RealmStore.fbm` whitelists are historical, for the legacy root.
The **live** precedent is in the current root: `Assets/StructureContent/ArcaneSpire_1.fbm/` and its
siblings, each texture carrying a `.meta` — a `.fbm` sidecar is already a shipped, tracked shape here.
Unity's FBX importer treats `<stem>.fbm` as the standard embedded-media sibling, so the relative
texture references resolve natively.

⚠ **THE HAZARD, AND IT FIRES WITHOUT BEING ASKED: `TripoAssetPostprocessor` WATCHES THIS FOLDER.**
Its `TargetFolders` (`Assets/Editor/TripoAssetPostprocessor.cs:62-76`) contains
`DeNelle.Core.AssetRoots.StructureContent + "/"`. On the lead's next import of `Quarry.fbx` it will
set the importer to **External materials**, call `ModelImporter.ExtractTextures(...)` into a sibling
folder, `SearchAndRemapMaterials`, and drop a `.tripo-extracted` marker (one-shot, idempotent after
that). It was written for Tripo FBXs that embed textures as **binary chunks** — the opposite of a
Blender export whose textures are already unpacked into `.fbm`. The likely result is a **second copy
of ~2 MB of textures** plus a remap onto it. **This is UNPROVEN either way**: read the `[Tripo]` lines
and the resulting folder after the first import. If it is unwanted, the seam to change is that
`TargetFolders` list (or a per-file exclusion), **never** the FBX.

**URP materials.** `SkinOptions.Structure` sets `FixTripoMaterials = true` (`VisualFactory.cs:147-148`),
which attaches `TripoMaterialFixer` — the **single-albedo** path, and the wrong tool for a 7-material
model (it forces one base map). **The code forces `FixTripoMaterials = false` for the re-point**, and the
URP conversion is a separate pass. **Which pass — read at source, this is settled, not a guess:**

- ⛔ **NOT `PolyperfectUrpFix.Fix`.** `PolyperfectUrpFix.cs:39` declares `private const string Root =
  "Assets/polyperfect"` and `:52` scans `AssetDatabase.FindAssets("t:Material", new[] { Root })`.
  It would **never see** this model's materials. (The menu item is the one CLAUDE.md §4 names for
  re-importing the pack — a different job entirely.)
- ✅ **`DeNelle.Editor.MagentaMaterialFixer.Run`** (`MagentaMaterialFixer.cs:56`). It runs
  `PolyperfectUrpFix.Fix` first as a no-op-if-absent courtesy (`:64`), then
  **`SweepAllMaterials(lit)` — "Sweep ALL material assets in the project for built-in/error shaders"**
  (`:66-67`) — then repairs null `sharedMaterial` slots in prefabs **and scenes** (`:74-77`). That is
  the project-wide multi-material seam, and it covers `Assets/StructureContent`.

### 8.3 THE PIT — two separate traps, and the second one is silent

The model is Y-up metric, 21.5 m × 19.7 m × **6.35 m total**, with the pit reaching **1.27 m BELOW
y = 0**, i.e. **5.08 m above ground**. Its own origin is the ground plane.

1. **SEATING.** `SkinOptions.SeatOnGround` is *"shift so the bounds base sits at the host's y"*
   (`VisualFactory.cs:52`), and `SkinOptions.Structure` sets it **TRUE** (`:148`). For this model that
   is wrong: the **pit floor** is the bounds base, so seating on bounds-min **lifts the whole model
   1.27 m** and floats the courtyard-level plateau. The correct seat is the **model origin — no
   offset at all**. The same applies to the hand-rolled version of this at
   `OwnerCastleLayoutRepair.cs:175` (`position += Vector3.down * BoundsOf(visual).min.y`).
2. ⛔ **FITTING — THE SILENT ONE.** `FitHeight` scales so **total** world-bounds height equals the
   target. Fitting 6.35 m → 4.00 m makes the **visible** building
   `4.00 × (5.08 / 6.35) = ` **3.20 m — 20% under the family height** — while the audit *and* the new
   `hub-ring-height` oracle (both of which measure total `Renderer.bounds`) read a satisfied 4.00 m
   and go green. Fit on the **above-ground extent** instead: `4.00 / 5.08 = 0.78740`, which lands
   5.00 m total, 4.00 m visible, 1.00 m of pit below ground.

Both are handled by explicit per-row modes rather than by a default, because a default that is right
for ten roots and wrong for this one is how the silent case ships.

### 8.4 Catalog recommendation — RECORDED, NOT DONE (instruction 3)

`collector_farm`'s `visualPrefabPath` is `Structures/farm`, and that is a **separate axis** from the
authored hub root: the injector **returns early** on `PreserveAuthoredVisual`
(`HubStructureVisualInjector.cs:804-808`), so the catalog value never reaches this object. **It is NOT
changed by this WO.** The recommendation, for a future ticket and the owner's ruling:
if she wants the Quarry art on *player-placed* farms too, that is a catalog `visualPrefabPath` edit to
`Structures/Quarry` **plus** `MarkCatalogArt` (which will then see the key) **plus** a content build
**plus** `tools\r2-ship.ps1` — the full §16 chain, because `Structure_Art` is a REMOTE group and a
missing push fails silently. Doing it inside this WO would mix a remote-content ship into a scene
lane.

### 8.5 What the code does (optional, flag-gated, OFF by default)

`Builds/hub-ring-scale-factors.json` gains a file-level `repointEnabled` flag and three per-row
fields, so the lead can **run scale-only first and the re-point second**:

```json
{
  "repointEnabled": true,
  "rows": [
    { "path": "Windmill_Food_Storefront",
      "targetUniformScale": 1.0,
      "repointModelPath": "Assets/StructureContent/Quarry.fbx",
      "seatMode": "modelOrigin",
      "fitMode": "aboveGround",
      "fitTargetHeightMetres": 4.0 }
  ]
}
```

With `repointEnabled` absent or false, every `repointModelPath` is **ignored and logged as ignored**
(`REPOINT IGNORED ...`, never silently dropped) — the apply behaves exactly as Phase 1 shipped it.
`seatMode` defaults to `boundsMin` (the legacy behaviour) and `fitMode` to `totalBounds`, so no
existing row changes meaning.

The re-point itself follows `CastleHubBuilder.SkinHostUpright`'s WO-1716 order exactly — strip the
host's own visual, destroy the prior visual **children** (keeping `NPC_*` interact points), skin,
and call `EnsureNoHusk` on **both** the failure branch (where the host is now invisible and must not
keep blocking) and the success branch. It forces `SeatOnGround = false`, `FitHeight = 0` and
`FixTripoMaterials = false` and does the fit/seat itself, for the three reasons in §8.2 and §8.3.

**Lead's second run, after the scale-only run is judged:**

```
#  edit Builds\hub-ring-scale-factors.json: repointEnabled -> true, add the Quarry row
.\run-unity-method.ps1 -Method DeNelle.Editor.HubRingHeightApply.Run -LogName wo1762-repoint.log `
    -ExpectMarker HUB_RING_APPLY_OK
.\run-unity-method.ps1 -Method DeNelle.Editor.MagentaMaterialFixer.Run -LogName wo1762-urp.log
.\run-unity-method.ps1 -Method DeNelle.Editor.NavMeshBakeFinal.Run -LogName wo1762-bake2.log
#  the Quarry's footprint is 21.5 x 19.7 m BEFORE fitting - re-bake is not optional here
```

⚠ **Read the `[Tripo]` lines in the first log**: `TripoAssetPostprocessor` fires on this file
unasked (§8.2) and its one-shot extraction lands before anything else runs.

---

## 9. THE TWO REGRESSION REDS (21:44, `REGRESSION_FAIL 539/541`) — BOTH CLOSED

Both were introduced by this lane. Neither was a defect in the ring.

### 9.1 `HUB_RING_HEIGHT_FAIL` — **the suite was wrong, not the scene**

The first oracle asserted a blanket **4.00 m ±15% over every authored root**. It went red on six,
and every red was the rule being too wide:

- **Jeweler 4.89, RealmStore 4.90, Armorer 5.03, Crafting 2.33** — these were **never in the owner's
  ask**. §0 names four buildings. A gate that fails a building nobody was asked to change is not
  protecting anything; it is a lane blocker that gets switched off, and a switched-off suite protects
  nothing at all.
- **Stone_Quarry 5.00 and ArcaneTower 5.00** — both **are 5.00 m BY RULING**. The suite was measuring
  total bounds and calling the owner's own decision a defect.

**`Assets/Editor/Regression/HubRingHeightRegression.cs` is re-pointed to the RULING, per root, by
name** — never to "whatever the scene currently is", which would assert nothing because it is copied
from the thing it claims to check:

| RULE | What it now pins |
|---|---|
| **1** | The **four ruled roots only**, each at **its own** ruled height, **±5%** (tighter than 15% *because* the targets are exact rulings, not a family average): `LumberMill` 4.00 · `IronMine` 4.00 · `ArcaneTower_MagicUpgrades` 5.00 · `Stone_Quarry` 5.00 total **and 4.00 above-ground** |
| **2** | No `*_Pallet` transform anywhere under the ring (unchanged) |
| **3** | The ring carries exactly **11** children (the authored fourteen minus the three pallets) |

**Yes, the measurement splits at the ground — asked and answered.** The recipe root *is* the ring, so
a direct child's own `localPosition.y` **is** its ground plane in that space — the same value
`HubRingHeightApply` calls `groundY`. So the Quarry is pinned **twice**: 5.00 m total **and** 4.00 m
above its own ground. That second pin is the one that can catch the §8.3 silent case; a total-only
pin reads green over a 3.20 m building. The other three have no sub-ground geometry, so their two
numbers are equal and only the total is pinned.

The seven un-ruled roots' measured values are recorded **in a comment, as the 2026-09-15 baseline,
explicitly NOT as an assertion** (`UnruledBaselineNote`), with a note telling the next seat to add a
`Ruled` row if the owner later rules on one — **not** to resurrect a blanket band.

Unchanged: `ApplyLanded` stays **true** (the lead's line 94 edit is preserved verbatim, comment and
all), the `RegressionMarkerRegression` shape (unique `*_OK`/`*_FAIL` pair, one registration, never
throws, `RegressionOutcome.Skip` for a genuine harness stand-down) and the `AssetRootsRegression`
RULE 1 shape (the recipe path is not an `AssetRoots` value — read at `AssetRoots.cs:55-81`).
One behaviour change worth naming: a ruled root that is **missing or renders nothing** is now a
**HARD FAIL, not a partial-skip** — it is one of the four objects the suite exists for, and "I could
not find the thing I am guarding" is a defect, not a limitation.

### 9.2 `[android-override]` FAIL — override set, **ledger untouched**

Three textures ≥ the 256 KB floor (`TextureImportBudgetRegression.SizeFloorBytes = 262144`) carried
no Android override. `Assets/StructureContent/` is **not** in `ToleratedRoots` (`:140-161`), and the
ledger is **frozen and may only shrink** (`:163-170`) — so the fix is the override, never a new row.

**The convention was read off the nearest precedent in the same root and the same `.fbm` shape**, not
guessed: `Assets/StructureContent/ArcaneSpire_1.fbm/Color_*.png.meta` (552 KB, the same size class)
carries `maxTextureSize: 1024`, `textureFormat: 50`, `crunchedCompression: 0`, `overridden: 1`.
Applied verbatim to all three:

| File | Size | Android block now |
|---|---|---|
| `Assets/StructureContent/Quarry.fbm/Dirt.png.meta` | 534 KB | `maxTextureSize: 1024`, `textureFormat: 50`, `crunchedCompression: 0`, `overridden: 1` |
| `Assets/StructureContent/Quarry.fbm/GraniteTexture.png.meta` | 598 KB | same |
| `Assets/StructureContent/Quarry.fbm/manonarywall.png.meta` | 632 KB | same |

Only those three fields changed per file; every other block (`DefaultTexturePlatform`, `Standalone`,
`WebGL`, `iOS`) is untouched.

**Why `textureFormat: 50` and not `48`:** the suite's own measurement (`:36-37`) puts ASTC format 50
at ~0.46 B/px and its failure text calls it *"~3x smaller again"* than 48. 48 is the **UI** tier
("keeps UI crisp"); these are a building's dirt / granite / masonry, which is what the spire
precedent in the same folder already uses. **And it side-steps RULE 1 entirely**: the RGBA32 fallback
only fires on format **Automatic + crunched** with post-clamp dimensions not both multiples of 4
(`:24-26`) — an explicit ASTC format is immune, which is exactly why WO-1485's fix was "name the
format".

The other four textures in the folder are below the floor and were correctly left alone
(`RoofShingles.png` 172 KB, `TudorStucco.png` 96 KB, `WoodWindow.png` 26 KB, `DarkWood.jpg` 15 KB).

⛔ **Nothing was added to `AndroidOverrideLedger` or `DuplicateLedger`.**

**Unproven:** the override values are asserted only by the `.meta` YAML as written. Unity has not
re-imported them in this session, so `GetPlatformTextureSettings("Android").overridden == true` — the
predicate the suite actually calls (`:497-499`) — is **proven by the file, not by a run**. The gate
re-run settles it.
