# WO-1762: size the hub authored ring to the 4.0 m family and remove the three scenery pallets

**Status:** READY TO IMPLEMENT
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
