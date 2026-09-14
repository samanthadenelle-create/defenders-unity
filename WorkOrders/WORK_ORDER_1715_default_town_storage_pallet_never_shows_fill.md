# WORK ORDER 1715 - Default-town storage pallet never shows fill items; a player-built second one does

**Status:** RCA COMPLETE - proven cause is a DESIGN QUESTION, not a registration bug; owner ruling needed before any fix
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from a live device pull while the owner was
felt-testing (release track continuing after 2026.09.14.369413)

## 1. Owner report, verbatim

> "first the storage i have deployed by default in the default town layout never register as anything,
> but when i added a second one i now have the items appearing on the pallets (see screen) that very
> happy about"

## 2. What is visible in the screenshot (primary evidence)

`docs/handoffs/storage_pallet_fill_default_vs_built.png`, pulled live off the device: two storage
pallets are visible in frame. The near one (bottom-left, wood logs stacked on it) shows resources
filled onto it. The other (mid-right, flat and bare) shows no items despite the town having 514 gold
and presumably wood in bank. Owner's framing: the DEFAULT-town-seeded pallet is the one that never
shows fill; the pallet she PLACED HERSELF (interactively, through the build menu) is the one now
correctly displaying items.

## 3. Why this is likely the SAME root-cause family as WO-1710, different consumer

WO-1710 (committed `593823f7d`, same day) proved that castle-builder/default-town-seeded structures
were missing registration in the "does this exist / is this tracked" registry that gates troop
training and the build-menu singleton offer list, while interactively-built structures were
automatically correct. This report has the identical shape: DEFAULT-seeded storage container works
visually (placed, presumably functional for collection) but its FILL-LEVEL DISPLAY never updates,
while a player-built one of the same type displays correctly. This STRONGLY SUGGESTS the pallet-fill
visual system reads from the same or a sibling registry/state source that WO-1710 found gaps in for
default-seeded structures - but this is a hypothesis to prove, not assume. The storage-pallet visual
system itself (Wood_Pallet/Iron_Pallet/Stone_Pallet beside each collector, per the 2026-09-13 owner
ruling recorded in `structures-catalog.json`'s `_containerScaleNote2026_08_26`) may have its OWN
separate state source that also has a default-seeding gap, independently of WO-1710's fix.

## 4. What to instrument before fixing (CLAUDE.md section 12)

- Find the code that drives a storage container's pallet-fill visual (how full the stacked resource
  model reads) - likely reads a resource-bank quantity or a per-structure fill counter. Find the exact
  call site and what identifies "which structure instance" it's filling for.
- Determine whether that identification depends on the same registration state WO-1710 fixed
  (`bakedTwins`/`StructureSingleton`/census) or an entirely separate mechanism.
- Confirm via a headless check or careful reading whether a DEFAULT-town-seeded storage container is
  missing from whatever state the fill-display reads, matching the WO-1710 shape, or whether this is a
  distinct defect.
- Check timing: WO-1710's fix (committed today) added `BackfillNewCensusRows` so existing Default-Town
  saves repair on next hub load. Confirm whether the owner's device pull happened BEFORE or AFTER that
  fix reached her build - if her installed build predates `593823f7d`, this may already be fixed and
  just needs a fresh APK to confirm, rather than new code.

## 5. Acceptance criteria

- [ ] RCA lane identifies the exact fill-display code path and states plainly whether it shares
      WO-1710's registry or has its own separate gap.
- [ ] RCA lane confirms whether the owner's currently-installed build already contains the WO-1710
      fix (`593823f7d`) or predates it - if it predates it, recommend a fresh tester push to re-test
      before any new code changes.
- [ ] If a genuinely separate gap is found, fix it using the same registration mechanism WO-1710
      established rather than inventing a parallel one (CLAUDE.md's replace-the-legacy-path discipline).
- [ ] Headless regression proving a default-town-seeded storage container's pallet displays fill
      correctly, alongside a player-built one.

## 6. What NOT to touch

- Do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` -
  unrelated.
- Do not re-litigate the storage pallet SIZE/art work from 2026-09-13 (`_containerScaleNote2026_08_26`)
  - this ticket is about fill-display registration, not visual sizing.

## RCA 2026-09-14 (read-only lane)

**Lane constraints honoured:** no `.cs`/`.unity`/`Assets` file edited (this ticket only); no Unity gate,
build or commit run. Every citation opened at source this session on `dev`.

### VERDICT: **PROVEN. NOT the WO-1710 registry gap, and NOT fixed by `593823f7d`.**
The default-town "storage" the owner sees **is not a storage container at all** - it is a bare KayKit
art mesh with no catalog identity, no components and no save record. Nothing was ever registered to
un-register, so there is no registration gap here of the WO-1710 shape.

### 1. What drives the fill visual, and what identifies the instance - PROVEN
- The pallet stack is built by `StorageStackView`
  (`Assets/_Modules/Village/Buildings/Progression/StorageStackView.cs:12-59`); it instantiates the
  five-tier props from `CollectorStackPropCatalog` (`:96-130`).
- Its ONE entry point is `StorageStackView.Attach(PlacedStructure placed)` (`StorageStackView.cs:33`),
  and its ONE caller in the game is `PlacedStructure.Start()`
  (`Assets/_Modules/Village/BuildMode/PlacedStructure.cs:25-31`) - verified by grep, the only other
  hits are editor proofs/regressions.
- **The identifying mechanism is therefore: "does this GameObject carry a `PlacedStructure` whose
  `itemId` resolves to a catalog row with `IsStorageContainer`"** (`StorageStackView.cs:36-40`). It is
  NOT `StructureSingleton`, NOT `bakedTwins`, NOT a census - a **separate mechanism** from WO-1710's.
- In the hub `BaseLayout` path, `PlacedStructure` is added by two seams, both record/placement driven:
  `BaseLayoutLoader.cs:582` (catalog replay of a `BaseLayout` record) and `:431-432` (the
  authored-BARRACKS-only adoption, gated by `IsAuthoredBarracksRecord`, `BaseLayoutLoader.cs:390-405`).
  Two further runtime adders exist OUTSIDE the hub path and are not in scope here:
  `Assets/_Modules/Village/World/Camps/OwnedTownSnapshotImporter.cs:91` and
  `OwnedTownUpgradeCompletion.cs:65`.

### 2. Why the default-town pallet never fills - PROVEN
- The three castle pallets are rows `Iron_Pallet` / `Wood_Pallet` / `Stone_Pallet` in
  `Assets/Editor/OwnerCastleLayoutRepair.cs:43-45`, each with **`Id = null, Legacy = null`**, and the
  repair loop **skips them outright**: `if (pair.Key.Legacy == null) continue;`
  (`OwnerCastleLayoutRepair.cs:162`). So they never receive `Configure` (no `AuthoredCastleStorefront`,
  no `canonicalId`) and never receive `ConfigureCapabilities` (no `Building`, no `ResourceCollector`).
- In the shipped hub scene they are **raw model instances**, one each: the scene's `Wood_Pallet`
  prefab-instance points at guid `f4a92ef53f0848b4bae8624bd8beb3cf` =
  `Assets/Models/KayKit/KayKit Resource Bits 1.0/Assets/obj/Pallet_Plastic_Grey.obj`, with
  `m_AddedComponents: []` (read out of `Assets/Scenes/Main_Castle_Overworld.unity`; Iron_Pallet =
  `...fbx(unity)/Pallet_Wood_Covered_A.fbx`, Stone_Pallet = `...obj/Pallet_Wood.obj`). A raw
  `.obj`/`.fbx` instance carries only MeshFilter/MeshRenderer. **No `PlacedStructure` -> `Start` never
  runs -> `Attach` is never called -> no fill props. Ever.**
- The catalog storage rows are `lumberyard` / `foundry` / `silo` (`structures-catalog.json`, the only
  three rows with `storageResource`/`storageCapacity`, all `singleton:true`). Their
  `visualPrefabPath` is `Structures/OwnerStorage/Wood_Pallet` etc. (`structures-catalog.json:1388`,
  `:1472`, `:1556`) - a **render-only copy of the same art**. That shared look is exactly why the two
  objects read as "the same storage" to the player, and `docs/handoffs/DEEPSEEK_STORAGE_SIZING_AUDIT.md:18`
  already warned it in writing: *"Saved layout prop names Iron_Pallet/Wood_Pallet/Stone_Pallet are not
  automatically the three catalog storage records."*
- The owner's "never register as ANYTHING" is literally true beyond the visual: town capacity is summed
  off `GameState.BaseLayout` records (`Assets/_Modules/Core/Economy/TownBankCapacity.cs:996-1007`), so
  the decorative pallets add **zero** capacity. Corroborating: `lumberyard` is `singleton:true`, so the
  build menu could only have offered her one **because no registry holds a `lumberyard`** - consistent
  with the decoy reading, not with a display-only bug.
- No seeder exists: `grep lumberyard|foundry|silo` returns **nothing** in `CastleHubBuilder.cs`,
  `RealmStorePlacer.cs` or `BaseLayoutLoader.cs`.

### 3. Is it already fixed by `593823f7d`? **NO - determinable from source alone.**
`git show --stat 593823f7d` touches `CastleHubBuilder.cs`, `StrategicPlacementMigration.cs`,
`BarracksUnlock.cs`, `DataRegression.cs`, the catalog + a new regression. It touches **none** of
`StorageStackView.cs`, `PlacedStructure.cs`, `BaseLayoutLoader.cs`, `TownBankCapacity.cs` or
`OwnerCastleLayoutRepair.cs`. Its `BakedRows` additions are `collector_forge` and `workshop` only
(`StrategicPlacementMigration.cs:104-105` region) - no storage row.

⚠ **It DID touch the three storage rows in the catalog** - checked, because the stat alone would not
have shown it: `git show 593823f7d -- Assets/Resources/Data/Canonical/structures-catalog.json` adds
`"visualPrefabPath": "Structures/OwnerStorage/Wood_Pallet"` / `Iron_Pallet` / `Stone_Pallet` with the
matching `maxFootprint` + identity-orientation notes. That is the WO-1710 lane's recovery of the
2026-09-13 pallet-art ruling it briefly reverted (`WORK_ORDER_1710...RESULT.md:14-26`, "It is FULLY
RECOVERED"), not a registry change: it only changes what a **built** container's body LOOKS like. It
adds no `bakedTwins` and no `BakedRow` for a storage row, and no catalog edit can put a
`PlacedStructure` on a decorative mesh. **Verdict holds: a fresh push will not change this symptom; no
re-test is required to rule it out.** (Side effect worth knowing: from this commit on, a player-built
lumberyard and the decorative castle pallet render the *same* art, which is why the two objects in the
screenshot read as the same thing to the owner.)

### 4. Shape a fix must take (do NOT implement from this lane)
The gap is **content identity**, not a parallel registry - so reuse WO-1710's registry exactly:
1. Give the three pallet rows real identity in `OwnerCastleLayoutRepair.Rows` (`:43-45`) -
   `Id = lumberyard/foundry/silo`, a `Legacy` name so the `:162` skip stops discarding them - so
   `Configure` stamps an `AuthoredCastleStorefront.CanonicalId`.
2. Add the three to `StrategicPlacementMigration.BakedRows` (`:90-105`) + author `repo.bakedTwins` in
   `structures-catalog.json` - the WO-1710 note above those rows states both are required together.
   `BackfillNewCensusRows` then repairs existing saves with no schema bump, and the singleton offer
   disappears (which is a behaviour change the PO should be told about: she loses the ability to build
   a second one).
3. ⚠ **Registration alone is NOT sufficient for the fill visual** - verified at source, not from the
   lane's comment: `ShouldReplayRecord` returns **false** for any `IsBakedStorefrontId` id
   (`StrategicPlacementMigration.cs:261-274`), and `BaseLayoutLoader.Rebuild` then WITHHOLDS that
   record and spawns no body (`BaseLayoutLoader.cs:244-250`). So a new `BakedRows` member gets a record
   and no `PlacedStructure`, hence still no `StorageStackView`. (Same reason the eight existing baked
   storefronts carry none - the barracks is the single exception, via the adoption seam at `:431-432`.) The authored root must
   also be adopted the way the barracks is (`BaseLayoutLoader.cs:431-432`), i.e. the
   `IsAuthoredBarracksRecord` adoption (`:390-405`) generalised to any authored root with a matching
   `canonicalId`. That generalisation is the substantive part of the lane and needs its own review.
4. Alternative the PO may prefer and which is far cheaper: **delete the three decorative pallets from
   the hub scene** so the only pallet a player sees is a real, buildable, filling container. This is a
   creative call, not a defect fix - route to the owner.

**Not proven / not claimed:** no headless repro was run (lane is read-only, and the tree currently has
another lane's compile error in `HeroLocomotion.cs`). The oracle the implementation lane should add:
load `Main_Castle_Overworld`, assert every object whose catalog row `IsStorageContainer` carries a
`PlacedStructure`, and that no bare pallet mesh sits in the town without one.

## History, owner-flagged 2026-09-14: this is NOT a general fill-mechanic bug

Owner, verbatim: "and thats never been really working well so thats huge win" (re: seeing the
player-built pallet fill correctly). `WORK_ORDER_903_storage_pallet_fill_stacks.md` shipped and closed
2026-08-27 (owner felt-tested PASS, APK 2026.08.27.343739) with acceptance text: "Works for freshly
placed AND save-replayed structures - `PlacedStructure.Start` is the one seam both paths share." So the
fill mechanic itself has been solid for a month across two paths (interactive placement, save reload).
The likely missing THIRD path, sharpening this ticket's hypothesis: castle-builder/default-town seeding
bakes structures directly into the scene and may never route through `PlacedStructure.Start` at all -
the exact same blind spot WO-1710 found for the troop-training and build-menu-offer registries. The RCA
lane should check `PlacedStructure.Start`'s callers first and confirm whether the default castle-hub
build path reaches it, before looking anywhere else.
