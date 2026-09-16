# DeepSeek work order: storage pallet sizing audit

## Request, scope and evidence status

Perform a **read-only engineering audit** of three current catalog storage rows: `lumberyard`, `foundry`, `silo`. Return a concise evidence-grounded diagnosis and ranked options. If sufficient evidence supports a fix, provide a **minimal conditional patch** for CLI review, clearly naming the preconditions; otherwise give the exact missing measurement and stop short of guessing a value. Nothing in this packet authorizes implementation.

You have NO filesystem/repository access. All text needed for this audit is embedded below. Paths label sources for the local CLI and hashes identify the snapshot; do not ask to open paths as if you can access them. The local CLI has Unity 6000.4.8f1. Do not claim you compiled, ran Unity, saw the binary geometry, or verified the appearance. Output only your final deliverable, not exploratory drafts or internal reasoning.

The existing full regression log reports these three storage models at **11.49m widest horizontal extent and 2.00m height**, exceeding the 10.40m population-independent structure cadence ceiling. These are captured editor measurements, not a new run, not Play Mode/device appearance verification. One suite contains all three outlier rows; they are not three separate failed suites.

## Owner constraints (binding)

- Preserve the original thirteen saved castle objects/layout and added original Arcane Tower / Cathedral of Learning, with original Tripo art/colors. Do not edit any saved castle scene, recipe prefab, materials, or authored geometry.
- Preserve the working capture-to-player-owned-town flow. No save/migration, occupancy, gameplay/economy or routing redesign.
- The current orientation notes explicitly record the owner's September 12 inspector setting for the KayKit pallet: Euler **0,0,0**, Y position zero, slats flush on grass. The old Tripo -90 correction stood this pallet on its edge. **Do not rotate it -90 to make its footprint pass.**
- Do not restore an old model, invent replacement art, change global YHeightVariable, weaken all cadence thresholds, exclude storage from all tests, or shrink the entire town. A proposed family-specific sizing policy must preserve proportions, original orientation and unrelated rows; no nonuniform squashing.
- Do not choose a new creative size from a gate number alone. 10.40m is a regression ceiling, not the owner's chosen pallet footprint. Historic heightMul=0.5 was authored for an older holder body; its continued suitability for the new flat pallet is the question, not proof.
- Saved layout prop names Iron_Pallet/Wood_Pallet/Stone_Pallet are not automatically the three catalog storage records. This audit concerns catalog-created lumberyard/foundry/silo, which share one address. Do not relabel or resize the saved props based on opaque catalog IDs.

## Questions to answer

1. Separate measured facts from inference: is this a wrongly rotated building, height fitting applied to intentionally flat geometry, a valid flat-art size that needs an explicit class policy, or not decidable from the evidence? Explain why the old blanket 'usually rotation' failure text is not sufficient diagnosis here.
2. Trace `Structures/GenericContainer` through address GUID -> wrapper prefab -> imported source GUID. The wrapper's folder name includes Synty; the source is KayKit Pallet_Wood_Covered_A. Old catalog notes say Tripo holder: distinguish stale history from current source identity and the newer owner orientation note.
3. Explain the current numerical path: 4m base * heightMul0.5 = 2m fit height; uniform scale from the actual fit-time Y bounds; maxFootprint0 disables the post-fit cap; manual Euler0/scale1 leaves the identity pose. Compare 11.49/2 ~=5.745 aspect with the wrapper collider's 1.7242849/0.30005282 ~=5.747. The latter corroborates proportions but is **not** a renderer-bounds measurement or proof of imported scale.
4. Assess options separately: preserve current geometry/pose and obtain an owner-grounded flat-family size, an existing per-row maxFootprint cap if justified, or a narrowly specified flat-prop fit policy requiring further design. Quantify consequences for BOTH height and width. For example, capping measured 11.49m to 10.40m would scale height to about 1.81m; this arithmetic does not establish that a 10.40m pallet is appropriate. Do not submit an unconditional 10.4 cap simply to green the gate.
5. Identify what the existing regression really covers: its `TryMeasure` mirrors relevant fit logic with Editor AssetDatabase-loaded geometry, while C5 still pins the old heightMul0.5. Account for actual VisualFactory Fit/Cap/Seat and StructureFactory options. Do not claim the oracle directly invokes VisualFactory.Skin. Distinguish current stored geometry from the live runtime path.
6. Identify narrow regression coverage needed for any proposed policy: original orientation and source identity, expected finite positive dimensions/proportions, all three rows consistent, known 14.34m misfit still detected, unrelated building band unchanged, address resolution failures still fail. Describe required create/preview/reload/upgrade parity checks without asserting they passed.
7. If discussing placement cells, do not turn rounded widest-axis log output into an exact square occupancy claim. Need full X/Z bounds, grid CellSize and the actual consumer's cell conversion/cache lifecycle. The excerpted footprint measurement cache key omits heightMul/maxFootprint; flag relevant cache invalidation as a source-review concern for a proposed live tuning change, not a reproduced current bug. No changes to it in this audit.

## Deliverable

Return one concise audit with:

- verdict and confidence, distinguishing facts/inferences/missing evidence;
- evidence table citing embedded source path + method/field or log line;
- at most three ranked options with scope and dimensional/save implications;
- a minimal conditional patch only if the sizing target is justified, otherwise an exact measurement work order for the local CLI;
- explicit assumptions and a compact validation table, **UNRUN** throughout.

Keep it bounded. No wholesale factory rewrite, new art, gameplay changes or unexplained threshold changes. If more context is essential, name the exact missing item and why it changes the decision. No dangling 'inspect this file' as though you can do so yourself.

## Available and missing geometry evidence

Available here: complete wrapper YAML and importer metadata, both current catalog rows, the measured full-suite lines, source fit/options/oracle, source identity hashes. Not included: the binary FBX mesh/vertex data, Unity imported mesh bounds/hierarchy export at fit stages, a current rendered image with a scale reference, or the owner's desired numerical size for these catalog-created pallets. The local file exists and its hash is given below solely for reproducibility; you cannot derive mesh bounds from a hash. Collider dimensions are not a replacement for render geometry.

Current source snapshot may differ in unrelated areas from the older captured full run. Do not claim source hashes retrospectively prove every file matched that run; they identify what the CLI currently has. The current rows and address chain are embedded for comparing the recorded behavior.

## Captured baseline log
`Builds/castle-validation-20260913-data-regression.log` SHA-256 `e128d7ed55a2dfecf172be14be12b6edfeb2cf15f182cf04904ecc7f1e929ce9`

```text
3711: [Flow:Structure] OptsFor('lumberyard'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
3712: [Flow:Structure] OptsFor('foundry'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
3713: [Flow:Structure] OptsFor('silo'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
13569: [Flow:Structure] OptsFor('lumberyard'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
13570: [Flow:Structure] OptsFor('foundry'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
13571: [Flow:Structure] OptsFor('silo'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
13608: [Flow:Structure] OptsFor('lumberyard'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
13609: [Flow:Structure] OptsFor('foundry'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
13610: [Flow:Structure] OptsFor('silo'): rotation channel = IDENTITY RESET (DEF-232 default); catalogEuler=(0.00, 0.00, 0.00) manual=True preserve=False applyManualEuler=True fitHeight=2 maxFootprint=0.
17738: REGRESSION_FAIL: 5 failure(s) (509/514 registered suites green, 0 skipped):
17740:   - 'lumberyard' FOOTPRINT OUTLIER: the fitted model is 11.49 m across — 2.9x the 4.0 m base fit height, over the 10.40 m band (2.6x base). THE CAUSE IS ALMOST NEVER heightMul. Fit-to-height is a single-axis promise run as a UNIFORM scale (VisualFactory.Fit: localScale *= target / bounds.size.y), so a model whose FIT-TIME pose is FLAT divides by a tiny number and drags its footprint up with it. FITTED ASPECT (widest : height) = 5.75 : 1, at a measured height of 2.00 m. ⚠ THE HEIGHT ALONE IS NOT DIAGNOSTIC and never was: Fit divides by whatever axis is up, so a heightMul-1.0 row measures EXACTLY the base height whether it was posed upright or flat. The ASPECT is the number that separates them — the honest town runs 0.6 : 1 to 1.9 : 1 (wall_stone 1.86, barracks 1.91) and the measured collector_farm pancake was 2.56 : 1. CHECK, IN THIS ORDER: (1) is the model upright at FIT time? its catalog orientation.euler is applied PRE-fit via SkinOptions.LocalRotation, so a wrong euler chooses which axis Fit divides by; (2) if the art really is flat-and-wide and correctly posed, author repo.maxFootprint on the row (a ceiling in metres, default 0 = disarmed) — that is what collector_farm does. ⚠ (2) IS FOR A FLAT MODEL ONLY. The cap is a UNIFORM scale-down, so on a correctly-posed wide building it just makes the building smaller — the same objection as heightMul, on a different key (WO-1239). DO NOT lower heightMul to fix this either: it shrinks the BUILDING as well as the footprint, which is the 'shrunk farm' the owner already rejected in commit 31b41d19.
17741:   - 'foundry' FOOTPRINT OUTLIER: the fitted model is 11.49 m across — 2.9x the 4.0 m base fit height, over the 10.40 m band (2.6x base). THE CAUSE IS ALMOST NEVER heightMul. Fit-to-height is a single-axis promise run as a UNIFORM scale (VisualFactory.Fit: localScale *= target / bounds.size.y), so a model whose FIT-TIME pose is FLAT divides by a tiny number and drags its footprint up with it. FITTED ASPECT (widest : height) = 5.75 : 1, at a measured height of 2.00 m. ⚠ THE HEIGHT ALONE IS NOT DIAGNOSTIC and never was: Fit divides by whatever axis is up, so a heightMul-1.0 row measures EXACTLY the base height whether it was posed upright or flat. The ASPECT is the number that separates them — the honest town runs 0.6 : 1 to 1.9 : 1 (wall_stone 1.86, barracks 1.91) and the measured collector_farm pancake was 2.56 : 1. CHECK, IN THIS ORDER: (1) is the model upright at FIT time? its catalog orientation.euler is applied PRE-fit via SkinOptions.LocalRotation, so a wrong euler chooses which axis Fit divides by; (2) if the art really is flat-and-wide and correctly posed, author repo.maxFootprint on the row (a ceiling in metres, default 0 = disarmed) — that is what collector_farm does. ⚠ (2) IS FOR A FLAT MODEL ONLY. The cap is a UNIFORM scale-down, so on a correctly-posed wide building it just makes the building smaller — the same objection as heightMul, on a different key (WO-1239). DO NOT lower heightMul to fix this either: it shrinks the BUILDING as well as the footprint, which is the 'shrunk farm' the owner already rejected in commit 31b41d19.
17742:   - 'silo' FOOTPRINT OUTLIER: the fitted model is 11.49 m across — 2.9x the 4.0 m base fit height, over the 10.40 m band (2.6x base). THE CAUSE IS ALMOST NEVER heightMul. Fit-to-height is a single-axis promise run as a UNIFORM scale (VisualFactory.Fit: localScale *= target / bounds.size.y), so a model whose FIT-TIME pose is FLAT divides by a tiny number and drags its footprint up with it. FITTED ASPECT (widest : height) = 5.75 : 1, at a measured height of 2.00 m. ⚠ THE HEIGHT ALONE IS NOT DIAGNOSTIC and never was: Fit divides by whatever axis is up, so a heightMul-1.0 row measures EXACTLY the base height whether it was posed upright or flat. The ASPECT is the number that separates them — the honest town runs 0.6 : 1 to 1.9 : 1 (wall_stone 1.86, barracks 1.91) and the measured collector_farm pancake was 2.56 : 1. CHECK, IN THIS ORDER: (1) is the model upright at FIT time? its catalog orientation.euler is applied PRE-fit via SkinOptions.LocalRotation, so a wrong euler chooses which axis Fit divides by; (2) if the art really is flat-and-wide and correctly posed, author repo.maxFootprint on the row (a ceiling in metres, default 0 = disarmed) — that is what collector_farm does. ⚠ (2) IS FOR A FLAT MODEL ONLY. The cap is a UNIFORM scale-down, so on a correctly-posed wide building it just makes the building smaller — the same objection as heightMul, on a different key (WO-1239). DO NOT lower heightMul to fix this either: it shrinks the BUILDING as well as the footprint, which is the 'shrunk farm' the owner already rejected in commit 31b41d19.
17743: C0 self-test: the clean family (widest honest row 7.64 m) passes the 10.40 m band, and the measured 14.34 m pancake is caught (1 report, correctly named) — the rule can go red, and it does not go red on the town as shipped.
17746: C5 [storage-container-scale]: lumberyard/foundry/silo all author heightMul 0.5.
17747: band = 10.40 m (2.6x the 4.0 m base fit height, population-independent). POPULATION (reported, not used as a threshold): 28 measured base visual(s), median widest-horizontal-extent 3.92 m.
17773:   lumberyard               widest 11.49 m, height 2.00 m
17774:   foundry                  widest 11.49 m, height 2.00 m
17775:   silo                     widest 11.49 m, height 2.00 m
```

## Current catalog rows and author notes
`Assets/Resources/Data/Canonical/structures-catalog.json` SHA-256 `d3c48d6f2da60e6d34a6eae729a3a0cc3ca41e6766791cb300273a2e564b9088`

`Assets/StreamingAssets/Data/Canonical/structures-catalog.json` SHA-256 `d3c48d6f2da60e6d34a6eae729a3a0cc3ca41e6766791cb300273a2e564b9088`

Byte-identical copies at creation: **True**. The following complete three entries and relevant height/container author notes are extracted once for efficiency; unrelated entries are omitted. Read-only context, not a replacement JSON file.

```json
{
  "version": 42,
  "_containerScaleNote2026_08_26": "WO-1224 Slice A: lumberyard, foundry and silo share the wide GenericContainer art, which the owner measured on device as reading about twice town scale. All three author heightMul 0.5 as one family. Lowering the uniform fit shrinks both height and footprint and is save-overlap-safe; device felt-test owns the final visual dial. Slice B placeholder-art replacement remains blocked and is not part of this version.",
  "_heightCadence": "OWNER RULING 2026-08-05 (verbatim): 'take whatever the y height that we use across the board, we're gonna take that as y and make this maybe one point five or one point two higher. It's only gonna be half as wide as the diameter of any of the houses. I want all of the other structures to stay within that cadence. I want all of them to be relatively the same size. Wide heights, all of that should be similar. Doesn't have to be exact, but it must be similar, all scaled to the same point.' ONE FAMILY, NOT ONE NUMBER. The dial is repo.heightMul against StructureFactory.YHeightVariable (4 m base). The fit is a UNIFORM scale, so heightMul moves HEIGHT AND FOOTPRINT TOGETHER: BuildModeController claims grid cells from StructureFactory.MeasureUprightFootprintMetres (the height-fitted model's XZ bounds), never from placement.footprint, which is only the prefab-missing fallback. THE OLD SENTENCE HERE - 'there is no width dial and none is needed' - WAS TRUE ONLY WHILE EVERY MODEL'S FITTED POSE WAS ROUGHLY BUILDING-SHAPED, and it is retired as of 2026-08-20. Fit-to-height is a single-axis promise: the scale is target / boundsY, so a model whose fitted pose is FLAT divides by a tiny number and takes its footprint along for the ride. There is now exactly ONE width dial, repo.maxFootprint, and it is a CEILING in metres on the fitted model's widest horizontal extent - it only ever scales DOWN, only ever uniformly, and it DEFAULTS TO 0 = DISARMED, so it changes nothing on any row that does not author it. Exactly ONE row authors it today (collector_farm 5.6); it is not a second cadence and never a place to hand-tune a building that height already handles. GROUPS: (1) BUILDING BASE 1.0 = 4.0 m: houses, production, vendors, storage containers, collectors, civic. This is the WIDTH REFERENCE the ruling measures against; House_Medieval_Medium fits to 5.562 m across = a 2x2 claim at the 3 m cell. (2) TOWER 1.2 = 4.8 m: reads 20 percent taller than a house and 2.778 m across = 49.9 percent of a house diameter, exactly the ruling's 'half as wide', and a 1x1 claim. 1.2 is the OWNER-RULED anchor from commit 0ac59581 (archer tower); on 2026-08-05 the rest of the tower class came off the old WO-764 1.25 onto it, so no tower is an outlier against the anchor any more. (3) SIEGE ENGINE 0.75 = 3.0 m: tower_catapult and tower_siege_tower. Machines, not architecture, so they sit deliberately UNDER the house line and read as equipment; it also keeps the wall-walk Sky Ballista from out-topping the towers once stacked on a wall. (4) LANDMARK 1.25 = 5.0 m: arcane-tower (Cathedral of Learning) ONLY. The single apex of the town, which the ruling's 'relatively the same size, doesn't have to be exact' allows for one building. (5) DECORATION, well under the family: deco_torch 0.35 = 1.4 m. EXCEPTION, NOT A CADENCE VALUE: collector_farm 1.4, AND IT IS NOW CAPPED ON THE OTHER AXIS (repo.maxFootprint 5.6). The 'do NOT normalize it to 1.0' half stands - that re-ships the shrunk farm the owner reported in commit 31b41d19. The half this key MISSED, and the owner felt it on 2026-08-20 ('farm seems to be much larger than anything else'), is that heightMul is a uniform scale and therefore a FOOTPRINT dial as well: Structures/farm measures 0.977 x 1.000 x 0.391 m natively and its (-90,0,0) euler stands the 0.391 axis up, so the 5.6 m height target came out as scale 14.34 and a 14.00 x 14.34 m footprint against a 2.8-5.8 m family (measured, logs/device/2026-08-20-portal.log). BOTH directions on heightMul are wrong for that - down re-shrinks the building, up widens it further - so the fix is the footprint ceiling, which lands the row back on the 5.47 x 2.19 x 5.60 m it rendered before the PRE-fit-euler change of 2026-08-19. The row's own _heightNote carries the full derivation. NOTE THE MECHANISM CORRECTION: the 'spindly silhouette / windmill blades inflate its Y bounds' explanation that used to sit here is not what the geometry says - the art is a flat square plot - but the number it defended was right for the pipeline of its day. DELIBERATELY LEFT UNAUTHORED: wall_wood, wall_stone and gate_stone stay on the 1.0 base. See their per-row _heightNote - lowering a wall narrows it by the same factor and opens pathable GAPS in already-saved wall runs, which is a save-compat break rather than a visual tweak. TIERS 2 AND 3 NEED NO KEYS: ReskinForLevel re-fits every tier model through the same OptsFor/EffectiveVisualHeight call, so an upgrade inherits its row's heightMul automatically; there are no per-level height fields anywhere in RepoProps.",
  "entries": [
    {
      "id": "lumberyard",
      "manageFilters": [
        "STORAGE"
      ],
      "manageArtKey": "building-lumberyard",
      "displayName": "Lumberyard",
      "description": "Raises how much Wood your town can hold.",
      "role": "wood_store",
      "type": "Resource",
      "kind": "Cell",
      "_note": "WO-707 storage container (owner taxonomy 2026-07-13): stores Wood. | WO-966 (owner ruling 2026-08-15): SIX levels -- storageCapacity 1000 x storage-caps levelCapacityMultipliers [1,2,4,8,16,32] = 1000/2000/4000/8000/16000/32000 held. The per-step upgradeCost table below is wood+iron ONLY (WO-947 regular-structure basket; containers are NOT magical). Upgrade DURATION is not authored here and must not be: BuildTimerService.StartUpgrade derives the tier as (targetLevel - 2) off BuildTimerConfig, giving 40s / 2m / 6m / 18m / 55m for L2..L6. Trade buildings are pure vendor/upgrade shops; the three containers (lumberyard/foundry/silo) hold the stock and are the ONLY enemy-raid targets. storageCapacity is stubbed data for the WO-672 damage-to-stores loop; CoC-style visible fill is a later pass. Placeholder art: Stables_Medieval reads as an open wood yard/shed. | WO-707: shared GenericContainer holder body (owner 2026-07-13, Tripo import); resource identity = tile word + fill stacks (pass 2)",
      "visualPrefabPath": "Structures/GenericContainer",
      "repo": {
        "behaviorId": "GameplayBuilding",
        "heightMul": 0.5,
        "storageCapacity": 1000,
        "storageResource": "wood",
        "singleton": true,
        "_singletonNote": "WO-2005 / OWNER RULING 23 (2026-09-06), verbatim: \"also cap only one of each storage type, the idea is they should level them\" / \"if we decide one day we need more space we add another level easy.\" Capacity has ONE axis of growth: LEVEL. Before this flag TownBankCapacity.MaxOf summed storageCapacity over EVERY built container of the resource (TownBankCapacity.cs:435-442), so a second Lumberyard bought another full container's worth of wood ceiling and was CHEAPER than the L5->L6 rung (14400 wood vs 800 wood + 320 iron) - which made the ladder pointless and the WO-1425 \"which container do I need\" copy unanswerable. Raising the ceiling later is a DATA edit (add a rung, or raise levelCapacityMultipliers in storage-caps.json), never a second building. MIGRATION: GRANDFATHERED - an existing save holding two containers keeps both and keeps their summed ceiling; nothing is destroyed and no capacity is clamped (a clamp would delete resources the player paid for). StructureSingleton only refuses the NEXT placement. SWEEP VERIFIED NON-DESTRUCTIVE: StructureSingleton.EnforceInternal (:279-325) has three branches and EVERY one acts only on repo.bakedTwins - StandDownBakedTwins (:401-410) early-returns 'nothing to hide' when a row authors none, and ResurfaceBakedTwins walks the same empty list. These three rows author bakedTwins null, so the hub-load sweep never touches a placed body, a PlacedStructure or a BaseLayout record: an over-cap town is left exactly as it is. DEATH PATH VERIFIED: these rows are behaviorId GameplayBuilding -> Building.cs:230 Destructible.NotifyBroken -> RemovePersistedLayoutRecord + StructureSingletonBootstrap.NotifyRemovedDeferred (Destructible.cs:150-195), so the BaseLayout record StructureSingleton.HasPlacedInstance reads first (StructureSingleton.cs:543-549) is dropped on death and the container can always be rebuilt - the healing_caravan sharp edge does NOT apply here.",
        "buildCost": 1280,
        "cost": {
          "wood": 800,
          "food": 0,
          "iron": 320,
          "crystals": 0
        },
        "maxLevel": 6,
        "upgradeCost": [
          {
            "wood": 1200,
            "food": 0,
            "iron": 480,
            "crystals": 0
          },
          {
            "wood": 3000,
            "food": 0,
            "iron": 1200,
            "crystals": 0
          },
          {
            "wood": 4800,
            "food": 0,
            "iron": 1920,
            "crystals": 0
          },
          {
            "wood": 7800,
            "food": 0,
            "iron": 3120,
            "crystals": 0
          },
          {
            "wood": 14400,
            "food": 0,
            "iron": 5760,
            "crystals": 0
          }
        ],
        "navSurface": "Blocker",
        "placement": {
          "mustSitOn": "Ground",
          "footprint": 2.38,
          "noOverlap": true,
          "checkAffordable": true
        }
      },
      "orientation": {
        "corrected": false,
        "manual": true,
        "euler": [
          0,
          0,
          0
        ],
        "offset": [
          0,
          0,
          0
        ],
        "scale": 1,
        "note": "Owner 2026-09-12 inspector on Pallet_Wood_Covered_A (KayKit GenericContainer): rotation X=0 Y=0 Z=0, position Y=0, slats flush on grass. The Tripo -90 was for the old holder body; identity + -90 stood the pallet on its edge."
      }
    },
    {
      "id": "foundry",
      "manageFilters": [
        "STORAGE"
      ],
      "manageArtKey": "building-foundry",
      "displayName": "Foundry",
      "description": "Raises how much Iron your town can hold.",
      "role": "iron_store",
      "type": "Resource",
      "kind": "Cell",
      "_note": "WO-707 storage container: stores Iron. | WO-966 (owner ruling 2026-08-15): SIX levels -- 1000/2000/4000/8000/16000/32000 iron held; upgradeCost below is wood+iron only (WO-947); duration derived from (targetLevel - 2), never authored. Placeholder art: House_Medieval_Small (not any surviving tile's visual; the Forge/Armorer tiles use House_Medieval_Medium). | WO-707: shared GenericContainer holder body (owner 2026-07-13, Tripo import); resource identity = tile word + fill stacks (pass 2)",
      "visualPrefabPath": "Structures/GenericContainer",
      "repo": {
        "behaviorId": "GameplayBuilding",
        "heightMul": 0.5,
        "storageCapacity": 1000,
        "storageResource": "iron",
        "singleton": true,
        "_singletonNote": "WO-2005 / OWNER RULING 23 (2026-09-06), verbatim: \"also cap only one of each storage type, the idea is they should level them\" / \"if we decide one day we need more space we add another level easy.\" Capacity has ONE axis of growth: LEVEL. Before this flag TownBankCapacity.MaxOf summed storageCapacity over EVERY built container of the resource (TownBankCapacity.cs:435-442), so a second Lumberyard bought another full container's worth of wood ceiling and was CHEAPER than the L5->L6 rung (14400 wood vs 800 wood + 320 iron) - which made the ladder pointless and the WO-1425 \"which container do I need\" copy unanswerable. Raising the ceiling later is a DATA edit (add a rung, or raise levelCapacityMultipliers in storage-caps.json), never a second building. MIGRATION: GRANDFATHERED - an existing save holding two containers keeps both and keeps their summed ceiling; nothing is destroyed and no capacity is clamped (a clamp would delete resources the player paid for). StructureSingleton only refuses the NEXT placement. SWEEP VERIFIED NON-DESTRUCTIVE: StructureSingleton.EnforceInternal (:279-325) has three branches and EVERY one acts only on repo.bakedTwins - StandDownBakedTwins (:401-410) early-returns 'nothing to hide' when a row authors none, and ResurfaceBakedTwins walks the same empty list. These three rows author bakedTwins null, so the hub-load sweep never touches a placed body, a PlacedStructure or a BaseLayout record: an over-cap town is left exactly as it is. DEATH PATH VERIFIED: these rows are behaviorId GameplayBuilding -> Building.cs:230 Destructible.NotifyBroken -> RemovePersistedLayoutRecord + StructureSingletonBootstrap.NotifyRemovedDeferred (Destructible.cs:150-195), so the BaseLayout record StructureSingleton.HasPlacedInstance reads first (StructureSingleton.cs:543-549) is dropped on death and the container can always be rebuilt - the healing_caravan sharp edge does NOT apply here.",
        "buildCost": 1600,
        "cost": {
          "wood": 960,
          "food": 0,
          "iron": 480,
          "crystals": 0
        },
        "maxLevel": 6,
        "upgradeCost": [
          {
            "wood": 1440,
            "food": 0,
            "iron": 720,
            "crystals": 0
          },
          {
            "wood": 2700,
            "food": 0,
            "iron": 1800,
            "crystals": 0
          },
          {
            "wood": 4000,
            "food": 0,
            "iron": 2880,
            "crystals": 0
          },
          {
            "wood": 6800,
            "food": 0,
            "iron": 4680,
            "crystals": 0
          },
          {
            "wood": 12000,
            "food": 0,
            "iron": 8640,
            "crystals": 0
          }
        ],
        "navSurface": "Blocker",
        "placement": {
          "mustSitOn": "Ground",
          "footprint": 2.38,
          "noOverlap": true,
          "checkAffordable": true
        }
      },
      "orientation": {
        "corrected": false,
        "manual": true,
        "euler": [
          0,
          0,
          0
        ],
        "offset": [
          0,
          0,
          0
        ],
        "scale": 1,
        "note": "Owner 2026-09-12 inspector on Pallet_Wood_Covered_A (KayKit GenericContainer): rotation X=0 Y=0 Z=0, position Y=0, slats flush on grass. The Tripo -90 was for the old holder body; identity + -90 stood the pallet on its edge."
      }
    },
    {
      "id": "silo",
      "manageFilters": [
        "STORAGE"
      ],
      "manageArtKey": "building-stoneyard",
      "displayName": "Stoneyard",
      "description": "Raises how much Stone your town can hold.",
      "role": "stone_store",
      "type": "Resource",
      "kind": "Cell",
      "_note": "WO-707 storage container: stores Grain/food. | WO-966 (owner ruling 2026-08-15): SIX levels -- 1000/2000/4000/8000/16000/32000 food held; upgradeCost below is wood+iron only (WO-947); duration derived from (targetLevel - 2), never authored. Placeholder art: Windmill_Medieval reads agrarian (the mill tile that used it retires from the palette). | WO-707: shared GenericContainer holder body (owner 2026-07-13, Tripo import); resource identity = tile word + fill stacks (pass 2)",
      "visualPrefabPath": "Structures/GenericContainer",
      "repo": {
        "behaviorId": "GameplayBuilding",
        "heightMul": 0.5,
        "storageCapacity": 1000,
        "storageResource": "stone",
        "singleton": true,
        "_singletonNote": "WO-2005 / OWNER RULING 23 (2026-09-06), verbatim: \"also cap only one of each storage type, the idea is they should level them\" / \"if we decide one day we need more space we add another level easy.\" Capacity has ONE axis of growth: LEVEL. Before this flag TownBankCapacity.MaxOf summed storageCapacity over EVERY built container of the resource (TownBankCapacity.cs:435-442), so a second Lumberyard bought another full container's worth of wood ceiling and was CHEAPER than the L5->L6 rung (14400 wood vs 800 wood + 320 iron) - which made the ladder pointless and the WO-1425 \"which container do I need\" copy unanswerable. Raising the ceiling later is a DATA edit (add a rung, or raise levelCapacityMultipliers in storage-caps.json), never a second building. MIGRATION: GRANDFATHERED - an existing save holding two containers keeps both and keeps their summed ceiling; nothing is destroyed and no capacity is clamped (a clamp would delete resources the player paid for). StructureSingleton only refuses the NEXT placement. SWEEP VERIFIED NON-DESTRUCTIVE: StructureSingleton.EnforceInternal (:279-325) has three branches and EVERY one acts only on repo.bakedTwins - StandDownBakedTwins (:401-410) early-returns 'nothing to hide' when a row authors none, and ResurfaceBakedTwins walks the same empty list. These three rows author bakedTwins null, so the hub-load sweep never touches a placed body, a PlacedStructure or a BaseLayout record: an over-cap town is left exactly as it is. DEATH PATH VERIFIED: these rows are behaviorId GameplayBuilding -> Building.cs:230 Destructible.NotifyBroken -> RemovePersistedLayoutRecord + StructureSingletonBootstrap.NotifyRemovedDeferred (Destructible.cs:150-195), so the BaseLayout record StructureSingleton.HasPlacedInstance reads first (StructureSingleton.cs:543-549) is dropped on death and the container can always be rebuilt - the healing_caravan sharp edge does NOT apply here.",
        "buildCost": 1280,
        "cost": {
          "wood": 960,
          "food": 0,
          "iron": 240,
          "crystals": 0
        },
        "maxLevel": 6,
        "upgradeCost": [
          {
            "wood": 1440,
            "food": 0,
            "iron": 360,
            "crystals": 0
          },
          {
            "wood": 2700,
            "food": 0,
            "iron": 900,
            "crystals": 0
          },
          {
            "wood": 4000,
            "food": 0,
            "iron": 1440,
            "crystals": 0
          },
          {
            "wood": 6800,
            "food": 0,
            "iron": 2340,
            "crystals": 0
          },
          {
            "wood": 12000,
            "food": 0,
            "iron": 4320,
            "crystals": 0
          }
        ],
        "navSurface": "Blocker",
        "placement": {
          "mustSitOn": "Ground",
          "footprint": 2.38,
          "noOverlap": true,
          "checkAffordable": true
        }
      },
      "orientation": {
        "corrected": false,
        "manual": true,
        "euler": [
          0,
          0,
          0
        ],
        "offset": [
          0,
          0,
          0
        ],
        "scale": 1,
        "note": "Owner 2026-09-12 inspector on Pallet_Wood_Covered_A (KayKit GenericContainer): rotation X=0 Y=0 Z=0, position Y=0, slats flush on grass. The Tripo -90 was for the old holder body; identity + -90 stood the pallet on its edge."
      }
    }
  ]
}
```

## Exact current art identity chain

### `Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset` (exact lines 103-107, inclusive)
Full file SHA-256 `3a71d44c77177d0ff5ded35a1c1bb01984142c6090892bc8b16b2a68e482238e`. Surrounding unrelated source is omitted.

```yaml
  - m_GUID: 6d6e7bf602d57574784cd00938f83419
    m_Address: Structures/GenericContainer
    m_ReadOnly: 0
    m_SerializedLabels: []
    FlaggedDuringContentUpdateRestriction: 0
```

### `Assets/StructureContent/Synty/GenericContainer.prefab.meta` (complete file)
SHA-256 `cc26f7b917f2168157f0a4a6e9853c95d019d26412b738c639638621174a5a8e`

```yaml
fileFormatVersion: 2
guid: 6d6e7bf602d57574784cd00938f83419
PrefabImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant:
```

### `Assets/StructureContent/Synty/GenericContainer.prefab` (complete file)
SHA-256 `3a1854e43391b43d5774724b288d808943008a4d99510b4de6793adbb2ee8875`

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1001 &2912511481543103391
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalPosition.x
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalPosition.y
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalPosition.z
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalRotation.w
      value: 1
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalRotation.x
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalRotation.y
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalRotation.z
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalEulerAnglesHint.x
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalEulerAnglesHint.y
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: -8679921383154817045, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_LocalEulerAnglesHint.z
      value: 0
      objectReference: {fileID: 0}
    - target: {fileID: 919132149155446097, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_Name
      value: GenericContainer
      objectReference: {fileID: 0}
    - target: {fileID: 919132149155446097, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      propertyPath: m_Layer
      value: 8
      objectReference: {fileID: 0}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents:
    - targetCorrespondingSourceObject: {fileID: 919132149155446097, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
      insertIndex: -1
      addedObject: {fileID: 7437314781433592094}
  m_SourcePrefab: {fileID: 100100000, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
--- !u!1 &2641985785688141518 stripped
GameObject:
  m_CorrespondingSourceObject: {fileID: 919132149155446097, guid: 3cf11469951e4da4b9ee72c0a264aa40, type: 3}
  m_PrefabInstance: {fileID: 2912511481543103391}
  m_PrefabAsset: {fileID: 0}
--- !u!65 &7437314781433592094
BoxCollider:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 2641985785688141518}
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_IsTrigger: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Size: {x: 1.7242849, y: 0.30005282, z: 1.7242849}
  m_Center: {x: 0, y: 0.14999998, z: -0.000000059604645}
```

### `Assets/Models/KayKit/KayKit Resource Bits 1.0/Assets/fbx(unity)/Pallet_Wood_Covered_A.fbx.meta` (complete file)
SHA-256 `c498eda674c46f8b07ed5657052bbc73ccf0c50aa11ea11bbc73804d12c167f4`

```yaml
fileFormatVersion: 2
guid: 3cf11469951e4da4b9ee72c0a264aa40
ModelImporter:
  serializedVersion: 24200
  internalIDToNameTable: []
  externalObjects:
  - first:
      type: UnityEngine:Material
      assembly: UnityEngine.CoreModule
      name: resource
    second: {fileID: 2100000, guid: 470ab58bd64e9b648881cc549c6b0a79, type: 2}
  materials:
    materialImportMode: 2
    materialName: 0
    materialSearch: 1
    materialLocation: 0
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    removeConstantScaleCurves: 0
    motionNodeName: 
    animationImportErrors: 
    animationImportWarnings: 
    animationRetargetingWarnings: 
    animationDoRetargetingWarnings: 0
    importAnimatedCustomProperties: 0
    importConstraints: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 2
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importPhysicalCameras: 1
    importVisibility: 0
    importBlendShapes: 0
    importCameras: 0
    importLights: 0
    nodeNameCollisionStrategy: 1
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
    generateMeshLods: 0
    meshLodGenerationFlags: 0
    maximumMeshLod: -1
    meshOptimizationFlags: -1
    indexFormat: 0
    secondaryUVAngleDistortion: 8
    secondaryUVAreaDistortion: 15.000001
    secondaryUVHardAngle: 88
    secondaryUVMarginMethod: 1
    secondaryUVMinLightmapResolution: 40
    secondaryUVMinObjectScale: 1
    secondaryUVPackMargin: 4
    useFileScale: 1
    strictVertexDataChecks: 0
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
    normalSmoothingSource: 0
  referencedClips: []
  importAnimation: 1
  humanDescription:
    serializedVersion: 3
    human: []
    skeleton: []
    armTwist: 0.5
    foreArmTwist: 0.5
    upperLegTwist: 0.5
    legTwist: 0.5
    armStretch: 0.05
    legStretch: 0.05
    feetSpacing: 0
    globalScale: 1
    rootMotionBoneName: 
    hasTranslationDoF: 0
    hasExtraRoot: 0
    skeletonHasParents: 1
  lastHumanDescriptionAvatarSource: {instanceID: 0}
  autoGenerateAvatarMappingIfUnspecified: 1
  animationType: 2
  humanoidOversampling: 1
  avatarSetup: 0
  addHumanoidExtraRootOnlyWhenUsingAvatar: 1
  importBlendShapeDeformPercent: 1
  remapMaterialsIfMaterialImportModeIsNone: 0
  additionalBone: 0
  userData: 
  assetBundleName: 
  assetBundleVariant:
```

Binary mesh source (NOT embedded): `Assets/Models/KayKit/KayKit Resource Bits 1.0/Assets/fbx(unity)/Pallet_Wood_Covered_A.fbx`; bytes 29884; SHA-256 `763532cd60acf3e50c6e2418613574e114589122b53074478846f5711363e6c1`. Its .meta above carries GUID3cf11469951e4da4b9ee72c0a264aa40. No fresh geometry measurement or image was generated for this packet.

## Complete geometry fit/options implementation

### `Assets/_Modules/Village/VisualFactory.cs` (complete file)
SHA-256 `4d12fb5c418ee90bacc3fef03303103a58a14c862bf12545fc02331801bb47e6`

```csharp
// =============================================================================
// VisualFactory — one runtime "skinner" for any gameplay object: enemy, animal,
// tower, structure, prop.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The same five steps were copy-pasted across the codebase whenever something
// needed a real mesh at runtime — load a model, instantiate it under a host,
// scale it to fit, seat it on the ground, fix its materials for URP, strip its
// stray colliders. This consolidates them behind ONE call:
//
//     VisualFactory.Skin(host, "Enemies/Skeleton_Warrior", SkinOptions.Enemy(1.9f));
//     VisualFactory.Skin(host, "Structures/Tower",          SkinOptions.Structure(17f));
//
// It is deliberately VISUAL-ONLY: it does not touch gameplay. Living things layer
// their animator on top afterwards (EnemyAnimatorFactory) — a static wall simply
// never asks for one. That keeps the skinner universal without pretending a tower
// is animated.
//
// Runtime/Resources-based (the editor scene baker has its own AssetDatabase path +
// the same fit/seat helpers; the two asmdefs can't share without a reference, so
// this mirrors that logic for the runtime side rather than forcing a dependency).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>How <see cref="VisualFactory"/> should dress a model. Use the
    /// <see cref="Enemy"/> / <see cref="Structure"/> / <see cref="Prop"/> presets,
    /// or set fields directly.</summary>
    public struct SkinOptions
    {
        public float FitHeight;          // >0 → scale so world-bounds HEIGHT = this
        public float FitLargest;         // >0 → scale so LARGEST world-bounds dim = this (wins over FitHeight)

        // FOOTPRINT CAP (owner F8 2026-08-20 "farm seems to be much larger than anything else").
        // >0 => after the height fit, if the widest HORIZONTAL world-bounds extent (max of x,z)
        // exceeds this many metres, scale DOWN uniformly so it equals it. A CAP, never an
        // enlargement, never non-uniform.
        //
        // WHY IT IS NEEDED: fit-to-HEIGHT is a SINGLE-AXIS promise executed as a UNIFORM scale, so
        // it silently drags the other two axes with it. A model whose FIT-TIME pose is flat blows
        // up: Structures/farm measures 0.977 x 0.391 x 1.000 m once its (-90,0,0) euler is applied
        // PRE-fit, so a 5.6 m height target divides by 0.391 and drags the 1.000 m plan axis to
        // 14.34 m — measured, against a 2.8–5.8 m family. Neither direction on heightMul fixes
        // that, because heightMul IS the thing being multiplied.
        // DEFAULT 0 = disabled = byte-identical behaviour for every existing caller.
        public float MaxFootprint;
        public bool  SeatOnGround;       // shift so the bounds base sits at the host's y
        public bool  StripColliders;     // remove the model's own colliders (the host owns its collider)
        public bool  FixTripoMaterials;  // attach DeNelle.Core.TripoMaterialFixer (Tripo→URP) via reflection

        // DEF-232: a fixed local orientation correction APPLIED BEFORE Fit / SeatOnGround.
        // Hero/companion bodies import facing +X and need a -90° yaw to face the root's +Z.
        // Callers used to apply that yaw AFTER Skin returned — but SeatOnGround had already
        // centred the (off-pivot) bounds over the root while the body was still at identity,
        // so the post-Skin rotation swung the off-centre mesh sideways (camera "stays to her
        // right", body "pivots in place" instead of translating). Set the yaw HERE instead:
        // the body is rotated FIRST, then fit + seated/centred in its FINAL orientation, so
        // the visible mesh sits dead-centre over the hero root regardless of import pivot.
        public Quaternion? LocalRotation;

        // FLAT-SEAT (owner F8 2026-07-04 "iron mine not sitting with flat side down"): derive a
        // natural resting orientation from the model's own geometry — rotate so its NARROWEST
        // world-bounds axis points to world +Y (CLAUDE.md §4: narrowest axis → up/down, so the
        // largest/flattest face rests on the ground). Applied AFTER LocalRotation and BEFORE
        // Fit/SeatOnGround, so the model is measured + seated in its final upright pose. Replaces
        // the guessed magic-euler tips (e.g. z:-129 / z:-90) that laid the harvest props on their
        // side. Robust to import pivot — measured live per instance, no per-prop hand-tuning.
        public bool SeatFlat;

        // WO-928: KEEP the prefab's own authored rotation instead of flattening it to identity.
        // The default below (identity when no LocalRotation is supplied) is DEF-232's and stays —
        // but it silently DESTROYS an orientation correction that was deliberately baked into a
        // prefab, which is how the L3 Archer Tower shipped lying on its side.
        //
        // Captured 2026-08-08, owner felt-test:
        //   after instantiate (prefab-native pose): euler=(270.00, 0.00, 0.00)   <- the bake is THERE
        //   after LocalRotation identity:           euler=(0.00, 0.00, 0.00)     <- and gone
        //   after Fit+SeatOnGround:                 scale=(8.34, 8.34, 8.34)
        //   skinned ... boundsSize=(4.91, 4.80, 8.34)
        // The second-order damage is worse than the wrong pose: once the model is flat, Fit measures
        // the WRONG AXIS to reach its height target, so the tower scales 8.34x instead of L1's 4.74x
        // and sprawls 8.34 m across a 3x3 m nav/physics blocker. Orientation and "the footprint is
        // huge" are ONE defect, not two.
        //
        // This is opt-in, not a default flip, precisely because DEF-232's identity is load-bearing for
        // hero/companion bodies (they import facing +X and are corrected via LocalRotation). It is also
        // NOT a per-FACTORY opt-in — see the ⛔ block on SkinOptions.Structure for the 2026-08-08 revert
        // that proved "structures" is far too wide a set. The ONE correct granularity is PER CATALOG
        // ENTRY: StructureFactory.OptsFor reads it off `entry.repo.preservePrefabRotation`, so a single
        // row whose art carries its correction in the asset opts in and every other row is byte-unchanged.
        // ARCHITECTURE_PRINCIPLES law 4: an authored/manual correction is canon and is NEVER
        // overwritten by an automatic pass. Flattening it to identity was exactly that overwrite.
        public bool PreservePrefabRotation;

        // WO-928 instrumentation: an OPTIONAL caller-supplied identity (the catalog entry id) stamped
        // into every Xform trace line this skin emits. `prefab.name` alone cannot answer "which ROW put
        // a sideways model on my screen" — several rows share one model (GenericContainer serves
        // lumberyard/foundry/silo; House_Medieval_Medium serves armorer AND collector_forge), and the
        // rotation POLICY is now per-row, so the row is the only thing that identifies the decision.
        // The owner should never have to felt-test a second sideways structure: with this, the
        // preserve-vs-identity branch is `grep "entry='<id>'"` away. Null/empty = unstamped (unchanged
        // line shape for every non-catalog caller: enemies, troops, props, hero bodies).
        public string TraceId;

        /// <summary>An enemy/creature: fit to height, strip its colliders (the root carries the trigger capsule).</summary>
        public static SkinOptions Enemy(float height) =>
            new SkinOptions { FitHeight = height, StripColliders = true };

        /// <summary>A tower/building: fit to largest dimension, seat on ground, URP-fix Tripo materials.
        /// DELIBERATELY leaves <see cref="PreservePrefabRotation"/> FALSE — the identity reset is the
        /// known-good default for the structure class as a whole. The rotation policy is a PER-ROW
        /// decision now and is applied by StructureFactory.OptsFor from
        /// <c>entry.repo.preservePrefabRotation</c>, never from this factory. Read the ⛔ block.</summary>
        // ⛔ PreservePrefabRotation was set TRUE **HERE** on 2026-08-08 and REVERTED the same day.
        //    DO NOT SET IT HERE AGAIN. It is set per catalog row, in StructureFactory.OptsFor.
        //
        // It fixed the L3 Archer Tower, whose baked -90 is the only thing standing it up. It also
        // reached EVERY structure, because StructureFactory.OptsFor builds from this factory - and
        // most Tripo building prefabs also instantiate at euler (270, 0, 0), where that same 270 is
        // precisely what DEF-232's identity reset exists to CANCEL. Preserving it laid the whole
        // town on its side.
        //
        // Captured (owner felt-test, returning to town from a dungeon):
        //     after instantiate (prefab-native pose): euler=(270.00, 0.00, 0.00)
        //     prefab rotation PRESERVED (WO-928):     euler=(270.00, 0.00, 0.00)   x10 structures
        //
        // It only surfaced on RE-ENTRY because the first town load seats buildings from the
        // bake/injector path; coming back re-creates them through BaseLayoutLoader ->
        // StructureFactory.Create -> here. A first-load-only test would have missed it entirely.
        //
        // WHY A BLANKET FLIP CANNOT WORK, stated as mechanism rather than as a caution: THIRTEEN
        // catalog rows already carry a manual orientation of exactly (-90, 0, 0) — tower_wall_wizard,
        // pet-house, workshop, market, forge, jeweler, arcane-tower, collector_farm,
        // collector_lumbermill, lumberyard, foundry, silo, barracks. Those rows are stood up by
        // StructureFactory.Create APPLYING that -90 on top of an identity-reset root. Preserve the
        // native 270 as well and the two COMPOSE to 180 — upside down, not merely sideways. Any row
        // with a non-zero manual orientation is therefore permanently ineligible for this flag.
        //
        // THE LESSON, and it is the reason this comment is long: an opt-in is only as narrow as the
        // thing you opt in. "Structures" is not a narrow set. The correct scope is PER CATALOG
        // ENTRY - the wooden watchtower ladder specifically - not per factory. See WO-928 defect A.
        public static SkinOptions Structure(float largest) =>
            new SkinOptions { FitLargest = largest, SeatOnGround = true, FixTripoMaterials = true };

        /// <summary>A small prop: fit to largest dimension, seat on ground.</summary>
        public static SkinOptions Prop(float largest) =>
            new SkinOptions { FitLargest = largest, SeatOnGround = true };
    }

    public static class VisualFactory
    {
        // PROD-022 Lane B — BOUND THE MISS-LOG STORM, WITHOUT EVER GOING SILENT.
        // The Fail below fires on EVERY Skin attempt, and a hub re-apply plus save replay can drive
        // several attempts per address per second. A Pi Browser session's final seconds were nothing
        // but the same four addresses cycling -> Skin / not found / <- Skin, which buries every other
        // line in the capture and costs bandwidth on a device that is already the suspect.
        // The shape here is deliberately ESCALATE-THEN-THROTTLE, never suppress:
        //   attempts 1..MissLogCap   -> full Fail, with the underlying network cause
        //   attempt  MissLogCap + 1  -> one Fail saying the cap is reached and what happens next
        //   thereafter               -> a throttled Fail-equivalent, ~1 per 10s per address
        // CLAUDE.md §12 is binding: instrumentation is permanent and a failure never becomes silent.
        //
        // PROD-022 (owner ruling 2026-09-02) — the cap is now REMOTELY TUNABLE, defaulting to the
        // 3 it has always been. A build with no `visuals.missLogCap` row behaves exactly as before;
        // the value can be moved from the database without a 30-minute WebGL rebuild. The registry
        // and the owner-facing list live in DeNelle.Core.Ops.RemoteTunables /
        // docs/PROD022_TUNABLE_FLAGS.md. Clamped to at least 1: a cap of zero would skip straight
        // to the throttle and lose the first, most informative Fail of every address.
        private const int DefaultMissLogCap = 3;
        private static int MissLogCap => Mathf.Max(1,
            DeNelle.Core.Ops.RemoteTunables.Int(DeNelle.Core.Ops.RemoteTunables.KeyVisualsMissLogCap));

        private static readonly Dictionary<string, int> s_missLogCounts = new Dictionary<string, int>();

        /// <summary>
        /// The one place a resolve-miss is reported. Escalates for the first few attempts, announces its
        /// own cap, then throttles — and always carries the UNDERLYING fetch cause when the warmer has
        /// one, so the reader is told WHY the bytes never arrived rather than merely that they did not.
        /// </summary>
        private static void ReportResolveMiss(string resourcesPath)
        {
            s_missLogCounts.TryGetValue(resourcesPath, out int n);
            n++;
            s_missLogCounts[resourcesPath] = n;

            // Cross-module read, null-conditional per CLAUDE.md §10. Non-structure addresses (enemies,
            // props, hero bodies) simply have no warmer record and report "none".
            string cause = DeNelle.Core.StructureContentWarmer.LastFailureCause(resourcesPath);
            int attempts = DeNelle.Core.StructureContentWarmer.AttemptsFor(resourcesPath);
            string detail =
                $"model not found via Addressables OR Resources: '{resourcesPath}' — returning null " +
                "(caller falls back). UNDERLYING FETCH CAUSE: " +
                (cause ?? "none recorded — no async fetch has FAILED for this address, so the bytes were " +
                          "either never requested or are still in flight") +
                $" [fetchAttempts={attempts}/{DeNelle.Core.StructureContentWarmer.MaxRequestAttempts}, " +
                $"resolveAttempts={n}, warmerState={DeNelle.Core.StructureContentWarmer.State}, " +
                $"resident={DeNelle.Core.StructureContentWarmer.ResidentCount}, " +
                $"pending={DeNelle.Core.StructureContentWarmer.PendingRequests}, " +
                $"lastTransportUrl={DeNelle.Core.StructureContentWarmer.LastRequestUrl ?? "(none)"}]";

            if (n <= MissLogCap)
            {
                FlowTrace.Fail("VisualFactory", detail);
                return;
            }

            if (n == MissLogCap + 1)
            {
                FlowTrace.Fail("VisualFactory",
                    $"RESOLVE-LOG CAP: '{resourcesPath}' has now missed {n} times. Further misses for this " +
                    "address are THROTTLED to roughly one line every 10s for the rest of the launch — they " +
                    "are NOT suppressed and the address is NOT abandoned here (the fetch retry budget in " +
                    "StructureContentWarmer owns that decision). " + detail);
                return;
            }

            FlowTrace.Throttle("VisualFactory", "miss-" + resourcesPath, 10f,
                $"(throttled, miss #{n}) " + detail);
        }

        /// <summary>Loads <paramref name="resourcesPath"/> from Resources and skins it
        /// under <paramref name="host"/>. Returns null (caller falls back) if absent.</summary>
        public static GameObject Skin(Transform host, string resourcesPath, SkinOptions opts)
        {
            // PROD-022 — the `-> Skin(...)` / `<- Skin(...)` pair is the single loudest thing in a
            // Pi Browser capture: a hub re-apply drives several attempts per address per second and
            // the observed final seconds were nothing but this pair cycling. It is NARRATION, so it
            // is dimmable by `trace.assetVerbosity` — default 2, which is today's behaviour, every
            // scope printed. At a lower level the scope is `default(FlowScope)`, whose Dispose is a
            // documented no-op (FlowTrace.cs:322 — `_active` is false), so nothing changes but the
            // volume. ⛔ Warn and Fail below are NEVER gated: CLAUDE.md §12 is binding and a failure
            // that stops being logged is the exact bug this instrumentation exists to prevent.
            using var _ = DeNelle.Core.Ops.RemoteTunables.Int(
                              DeNelle.Core.Ops.RemoteTunables.KeyTraceAssetVerbosity)
                          >= DeNelle.Core.Ops.RemoteTunables.VerbosityVerbose
                ? FlowTrace.Enter("VisualFactory", $"Skin('{resourcesPath}')")
                : default;

            // ⛔ RESOLVES THROUGH StructureAssetLoader, NOT Resources.Load.
            // This is the SINGLE point every structure visual flows through — StructureFactory.Create,
            // the tier-upgrade reskin and the build-preview probe all land here. Structure art moved
            // OUT of Resources into a remote Addressable group (2026-08-18), so a bare Resources.Load
            // here returns null for EVERY building: the town renders empty while every gate that does
            // not instantiate still passes. The seam is Addressables-first with a Resources fallback,
            // so this line is correct both before and after the migration.
            GameObject prefab = null;
            FlowTrace.Try("VisualFactory", $"resolve '{resourcesPath}'",
                () => prefab = DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(resourcesPath));

            if (prefab == null)
            {
                // §12: a missing model is a hard miss the caller falls back on — promote from a
                // swallowed Debug.LogWarning to FlowTrace.Fail so it rolls up to the break-log
                // (error severity) and a headless capture pinpoints the unresolved address.
                // ⚠ Post-CDN this line NAMES THE ADDRESS that failed, which is the whole benefit of
                // resolving by address rather than by path: a remote miss says exactly which asset
                // and which key, instead of leaving a silently empty spot in the world.
                // PROD-022 Lane B: routed through ReportResolveMiss so the line NAMES THE CAUSE
                // (UnityWebRequest result / HTTP status / timeout-vs-protocol, from the warmer) and so
                // the same address repeating cannot flood a session's final seconds. The wording
                // "model not found via Addressables OR Resources: '<addr>'" is preserved verbatim
                // because existing triage docs, greps and the WO's acceptance criterion match on it.
                ReportResolveMiss(resourcesPath);
                return null;
            }
            FlowTrace.Step("VisualFactory", $"resolved Resources model '{resourcesPath}' -> '{prefab.name}'.");
            return Skin(host, prefab, opts);
        }

        /// <summary>Instantiates <paramref name="prefab"/> under <paramref name="host"/>
        /// and applies the skin options.</summary>
        public static GameObject Skin(Transform host, GameObject prefab, SkinOptions opts)
        {
            if (prefab == null)
            {
                FlowTrace.Fail("VisualFactory", "Skin called with a null prefab — returning null (caller falls back).");
                return null;
            }

            using var _ = FlowTrace.Enter("VisualFactory", $"Skin(prefab='{prefab.name}')");

            // Guard the Instantiate: a broken/aborted prefab clone returns null rather than NRE'ing
            // every caller. Treated as a miss (Fail + null) so callers fall back, never get half-built.
            GameObject go = null;
            FlowTrace.Try("VisualFactory", $"Instantiate '{prefab.name}'",
                () => go = Object.Instantiate(prefab, host));
            if (go == null)
            {
                FlowTrace.Fail("VisualFactory",
                    $"Instantiate returned null for prefab '{prefab.name}' — returning null (caller falls back).");
                return null;
            }

            go.transform.localPosition = Vector3.zero;

            // XFORM VALUE-TRACE (owner 2026-07-08: "i want to see everything that happens to it
            // from selecting fbx to placement" — the euler ping-pong RCA): one line per mutation
            // stage with the ACTUAL local euler/pos/scale, so a single placement prints the whole
            // transform journey. Companion census: docs/STRUCTURE_TRANSFORM_CENSUS (agent).
            //
            // WO-928: the line is now stamped with the CALLER'S ENTRY ID as well as the model name.
            // The model name alone is ambiguous by construction — GenericContainer is the model for
            // lumberyard AND foundry AND silo, House_Medieval_Medium for armorer AND collector_forge —
            // and since the preserve-vs-identity branch below is decided PER ROW, the row is the only
            // thing that identifies which decision ran. A future sideways structure is now one grep
            // ("entry='<id>'" or "prefab rotation PRESERVED") away instead of one felt-test away.
            string who = string.IsNullOrEmpty(opts.TraceId)
                ? $"'{prefab.name}'"
                : $"'{prefab.name}' (entry='{opts.TraceId}')";
            // WO-1157 (owner F8 2026-08-24, "the ballista builds on its side"): the line now also
            // carries the MEASURED WORLD BOUNDS and the upright aspect at that stage. euler alone
            // cannot answer "is it standing" — a euler of (0,0,0) is upright for one model and flat
            // for the next, which is exactly how two orientation theories were argued from the same
            // trace and both were wrong. size + aspect are the numbers that decide it, so they are
            // printed beside the pose at EVERY mutation stage rather than derived afterwards.
            // aspect = height / max(width, depth): >1 tall-and-narrow, <1 flat-and-wide.
            void TraceXform(string stage)
            {
                var t = go.transform;
                string measured = "bounds=<none>";
                if (TryBounds(go, out Bounds tb))
                {
                    float widest = Mathf.Max(tb.size.x, tb.size.z);
                    float aspect = widest > 0.0001f ? tb.size.y / widest : 0f;
                    measured = $"bounds size=({tb.size.x:0.###}w x {tb.size.y:0.###}h x {tb.size.z:0.###}d) " +
                               $"aspect={aspect:0.###} minY={tb.min.y:0.###}";
                }
                FlowTrace.Step("Xform", $"{who} after {stage}: " +
                    $"euler={t.localEulerAngles} pos={t.localPosition} scale={t.localScale} {measured}");
            }
            TraceXform("instantiate (prefab-native pose)");

            // DEF-232: apply the caller's orientation BEFORE Fit/SeatOnGround so the body is
            // measured + centred in its FINAL facing. A post-Skin rotation (the old pattern)
            // swung the off-pivot bounds sideways. Default is identity (unchanged for callers
            // that don't pass LocalRotation, e.g. enemies/structures).
            // WO-928: an explicit LocalRotation always wins. Otherwise we only FORCE identity when the
            // caller has not asked us to preserve the prefab's authored pose — flattening a baked
            // correction is what laid the L3 Archer Tower on its side and then let Fit measure the
            // wrong axis (see SkinOptions.PreservePrefabRotation for the captured trace).
            if (opts.LocalRotation.HasValue)
                go.transform.localRotation = opts.LocalRotation.Value;
            else if (!opts.PreservePrefabRotation)
                go.transform.localRotation = Quaternion.identity;

            // The stage string IS the decision record: three mutually exclusive branches, each naming
            // WHY the pose is what it is, on a line that now also carries the entry id (see `who`).
            // "prefab rotation PRESERVED (WO-928, opt-in row)" is the only branch that can leave a
            // native Tripo 270 standing, so grepping it lists exactly the rows running the opt-in.
            TraceXform(opts.LocalRotation.HasValue ? "opts.LocalRotation"
                     : opts.PreservePrefabRotation ? "prefab rotation PRESERVED (WO-928, opt-in row)"
                     : "LocalRotation identity (DEF-232 default)");

            // FLAT-SEAT: derive the natural resting orientation from geometry (narrowest world-bounds
            // axis → +Y, §4) so the model sits flat side down. Runs BEFORE Fit/SeatOnGround so the
            // upright bounds are what gets fit + seated. Replaces guessed magic-euler tips.
            if (opts.SeatFlat)
            {
                FlowTrace.Try("VisualFactory", "seat flat (bounds-derived)", () => SeatFlat(go));
                TraceXform("SeatFlat");
            }

            if (opts.StripColliders)
                FlowTrace.Try("VisualFactory", "strip colliders",
                    () => { foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c); });

            if (opts.FixTripoMaterials)
                FlowTrace.Try("VisualFactory", "add Tripo material fixer", () => TryAddTripoFixer(go));

            FlowTrace.Try("VisualFactory", "fit + seat", () =>
            {
                if (opts.FitLargest > 0f)     Fit(go, opts.FitLargest, largest: true);
                else if (opts.FitHeight > 0f) Fit(go, opts.FitHeight,  largest: false);

                // AFTER the fit and BEFORE the seat: the cap is a ceiling on the fit's result, not
                // a second competing fit, and it changes height as well as width (uniform), so it
                // must land before the bounds base is dropped to the host.
                if (opts.MaxFootprint > 0f) CapFootprint(go, opts.MaxFootprint);

                // SeatOnGround centres the (now correctly-oriented) bounds over the host's x/z and
                // drops the bounds-base to the host's y — so the visible mesh sits dead-centre over
                // the hero ROOT, the transform the camera follows and HeroLocomotion drives.
                if (opts.SeatOnGround)
                    SeatOnGround(go, host != null ? host.position : go.transform.position);
            });
            TraceXform("Fit+SeatOnGround");

            // RENDER-VERIFY (owner directive 2026-06-19: "anything that renders can be broken — check
            // render==true and roll back the error"). This is the #1 shared choke point — every
            // enemy/troop/structure/prop/animal/companion body skins through here. A prefab that loads
            // but renders nothing (no enabled renderer, missing mesh, degenerate bounds) reads as a
            // grey/empty body to the player. PROVE it can render before handing it back; a broken build
            // logs Fail (rolls up to break-log) and is destroyed + treated as a MISS (return null) so
            // the caller falls back — we never hand back a render-broken-but-non-null body silently.
            if (!VerifyRenders(go, prefab.name))
            {
                Object.Destroy(go);
                return null;
            }

            // RIG-LEVEL DRESSABLE capability (BlinkWardrobe, owner architecture 2026-06-20): a body that
            // ships outfit-set renderers self-dresses to its default outfit HERE — beside the rig, the one
            // shared path every character skins through — so EVERY dressable humanoid (hero / companion /
            // arena fighter / future human-skinned enemy) starts CLOTHED, never in underwear. Non-dressable
            // bodies (skeletons / animals / structures) ship no outfit renderers → IsDressable=false → skip.
            // The data-driven per-character wardrobe + cosmetic-store feed land on this seam (WO-456).
            FlowTrace.Try("VisualFactory", "wardrobe default-dress",
                () => { if (BlinkWardrobe.IsDressable(go)) BlinkWardrobe.DressInStarter(go); });

            // WO-436 Step 1 (§12): surface the ACTUAL material name on the skinned body so a headless
            // capture PROVES Failure A (URP material not applied → the raw FBX surface renders as Unity's
            // solid unlit-green fallback) instead of guessing. Null-guarded: no renderer/material → Warn
            // (never a silent blank). sharedMaterial (not .material) — no per-instance material leak.
            FlowTrace.Try("VisualFactory", "material trace", () =>
            {
                var renderer = go.GetComponentInChildren<Renderer>();
                if (renderer == null || renderer.sharedMaterial == null)
                    FlowTrace.Warn("EnemyVisual", $"Material on {prefab.name}: NO renderer/material (would render blank/fallback)");
                else
                    FlowTrace.Step("EnemyVisual", $"Material on {prefab.name}: {renderer.sharedMaterial.name}");
            });

            // MAGENTA RECOVERY AT THE SPAWN SEAM (owner defect 2026-08-02: "raid troops are magenta").
            // PROVEN CAUSE (not a theory): MagentaGuard.Init is [RuntimeInitializeOnLoadMethod(
            // AfterSceneLoad)] + SceneManager.sceneLoaded, and its Sweep takes a ONE-TIME
            // Object.FindObjectsByType<Renderer>() SNAPSHOT. It has no Update and had no per-object
            // entry point. A raid troop is built MID-RAID (TroopDeployer.SpawnFromArmy ->
            // TroopFactory.Build -> here), i.e. after every sceneLoaded has already fired, so the
            // guard was structurally BLIND to it and the body stayed magenta forever.
            //
            // THIS overload is the choke point: the (Transform, string, SkinOptions) overload above
            // resolves the prefab then calls straight into this one (:95), and every runtime factory
            // — TroopFactory, EnemyFactory, StructureFactory, GhostPreview, HubStructureVisualInjector,
            // MineNodeVisual, HarvestSite, the station injectors, BuildPreviewModal,
            // StoryCompanionInjector — enters through one of those two. One hook covers them all.
            //
            // Placed AFTER VerifyRenders + the wardrobe dress so it sees the FINAL renderer set
            // (BlinkWardrobe toggles outfit renderers), and it is the last thing before the body is
            // handed back. SweepGameObject never throws and warns-not-errors on a missing art pack.
            FlowTrace.Try("VisualFactory", "magenta sweep (runtime spawn seam)",
                () => DeNelle.Core.MagentaGuard.SweepGameObject(go, "VisualFactory.Skin"));

            return go;
        }

        // RENDER-VERIFY: the instantiated body MUST carry >=1 ENABLED Renderer (SkinnedMeshRenderer or
        // MeshRenderer) with a non-null shared mesh AND non-degenerate world bounds. Traces the exact
        // counts so a headless capture splits "no enabled renderer" vs "missing mesh" vs "degenerate
        // bounds" with zero guessing. Returns false => caller (Skin) treats the build as a miss.
        private static bool VerifyRenders(GameObject go, string what)
        {
            if (go == null)
            {
                FlowTrace.Fail("VisualFactory", $"VerifyRenders: skinned '{what}' instance is null.");
                return false;
            }

            var rends = go.GetComponentsInChildren<Renderer>(true);
            int total = 0, enabled = 0, withMesh = 0;
            foreach (var r in rends)
            {
                if (r == null) continue;
                total++;
                bool on = r.enabled && r.gameObject.activeInHierarchy;
                bool hasMesh = false;
                if (r is SkinnedMeshRenderer smr) hasMesh = smr.sharedMesh != null;
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    hasMesh = mf != null && mf.sharedMesh != null;
                }
                if (on) enabled++;
                if (on && hasMesh) withMesh++;
            }

            bool boundsOk = TryBounds(go, out Bounds b) && b.size.sqrMagnitude > 1e-8f;
            bool renders = enabled > 0 && withMesh > 0 && boundsOk;

            FlowTrace.Step("VisualFactory",
                $"skinned '{what}' on '{go.name}': renderers={total} enabled={enabled} withMesh={withMesh} " +
                $"boundsSize={(boundsOk ? b.size.ToString("F2") : "<degenerate>")} => renders={renders}");

            if (!renders)
            {
                FlowTrace.Fail("VisualFactory",
                    $"VerifyRenders FAILED for skinned '{what}' on '{go.name}': renderers={total} enabled={enabled} " +
                    $"withMesh={withMesh} boundsOk={boundsOk} — treating as a MISS (destroy + return null; caller falls back).");
                return false;
            }
            return true;
        }

        // ── Geometry helpers ─────────────────────────────────────────────────
        /// <summary>Uniformly scales so the world-bounds HEIGHT (or largest dimension)
        /// equals <paramref name="target"/> — robust to arbitrary import scale.</summary>
        private static void Fit(GameObject go, float target, bool largest)
        {
            if (!TryBounds(go, out Bounds b))
            {
                // W (WO-1157): a silent return here left the model at its authored scale with
                // nothing saying the fit never ran. Never silent.
                FlowTrace.Warn("VisualFactory",
                    $"Fit('{go?.name}'): NO measurable renderer bounds — NOT fitted, scale left at " +
                    $"{(go != null ? go.transform.localScale.ToString("F3") : "<null>")}.");
                return;
            }
            float measure = largest ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : b.size.y;
            if (measure < 0.0001f)
            {
                FlowTrace.Warn("VisualFactory",
                    $"Fit('{go?.name}'): measured axis is degenerate ({measure:0.#####} m) — NOT fitted.");
                return;
            }

            // WO-1157 (§12): the fit is where a mis-ORIENTED model becomes a mis-SCALED one as well,
            // because fit-to-height divides by whatever axis happens to be vertical AT THIS MOMENT.
            // Printing the measured axis, the whole bounds and the resulting factor makes that
            // coupling readable in one line: a lying-down model shows a small `measure` and a wildly
            // large factor, which is the signature the archer-tower thread had to re-derive by hand.
            float k = target / measure;
            FlowTrace.Step("VisualFactory",
                $"Fit('{go.name}'): mode={(largest ? "largest" : "height")} measured={measure:0.###}m " +
                $"of bounds ({b.size.x:0.###} x {b.size.y:0.###} x {b.size.z:0.###}) " +
                $"target={target:0.###}m -> scale x{k:0.####} (from {go.transform.localScale.x:0.####}).");

            go.transform.localScale *= k;
        }

        // ── Seat verification (§12: the next float NAMES ITSELF) ─────────────
        //
        // ⚠ READ THE TRACE IN WORLD SPACE, NOT IN THE Xform LINE'S localPosition.
        // The "[Flow:Xform] ... after Fit+SeatOnGround: pos=(0.00, 2.00, 3.13)" line prints
        // transform.localPosition — the position of the model's PIVOT relative to its host, NOT
        // the height of its bottom above the ground. A model whose pivot sits at the centre of a
        // 4 m body seats CORRECTLY at local y = +2.00: that is exactly the lift needed to put the
        // bounds BOTTOM on the host's y. Reading that 2.00 as "floating 2 m" is a misdiagnosis
        // that has cost a session already (2026-08-20 portal triage). The only number that can
        // decide the question is bounds.min.y vs the ground plane — which is what this pair of
        // helpers measures and prints, so nobody has to re-derive the pivot maths from a log.
        //
        // Epsilon: a seat is "on the ground" when its bounds bottom is within this of the ground
        // plane. Renderer bounds are a loose world AABB and a fitted 4 m building carries a few
        // mm of float error, so a hard == would cry wolf on every correct seat. 5 cm is well under
        // anything a player can perceive as a gap and well over the numeric noise.
        public const float SeatEpsilonMetres = 0.05f;

        /// <summary>
        /// TRUE when <paramref name="go"/>'s world-bounds BOTTOM rests within
        /// <see cref="SeatEpsilonMetres"/> of <paramref name="groundY"/>. <paramref name="bottomY"/>
        /// receives the measured bottom (NaN when the object has no measurable bounds — which is
        /// itself a fail, since an unmeasurable object cannot have been seated).
        /// <para>PUBLIC on purpose: this is the one definition of "seated", shared by the runtime
        /// seat below and by <c>StructureSeatRegression</c>, so the gate cannot drift from the game.</para>
        /// </summary>
        public static bool IsSeatedOnGround(GameObject go, float groundY, out float bottomY,
                                            float epsilon = SeatEpsilonMetres)
        {
            bottomY = float.NaN;
            if (go == null || !TryBounds(go, out Bounds b)) return false;
            bottomY = b.min.y;
            return Mathf.Abs(bottomY - groundY) <= epsilon;
        }

        /// <summary>Shifts the object so its bounds base sits at <paramref name="basePos"/>.y
        /// (centred on basePos.x/z).</summary>
        /// <summary>
        /// Uniformly scales DOWN (never up) so the widest horizontal world-bounds extent (max of
        /// x,z) is at most <paramref name="maxMetres"/>. Runs AFTER <see cref="Fit"/>, so it is a
        /// ceiling on that fit rather than a second competing fit; proportions are preserved.
        /// </summary>
        private static void CapFootprint(GameObject go, float maxMetres)
        {
            if (maxMetres <= 0f || !TryBounds(go, out Bounds b)) return;
            float widest = Mathf.Max(b.size.x, b.size.z);
            if (widest < 0.0001f || widest <= maxMetres) return;
            float k = maxMetres / widest;
            go.transform.localScale *= k;
            FlowTrace.Step("VisualFactory",
                $"footprint cap: widest {widest:0.##}m > {maxMetres:0.##}m — scaled x{k:0.###} uniformly " +
                "(height follows; this row's fit-time pose is flat, so fit-to-height alone over-scales it).");
        }

        private static void SeatOnGround(GameObject go, Vector3 basePos)
        {
            // W: an unmeasurable body used to return here in SILENCE, leaving the model wherever
            // Fit left it — the exact "it floats and nothing said so" class §12 exists to kill.
            if (!TryBounds(go, out Bounds b))
            {
                FlowTrace.Warn("VisualFactory",
                    $"SeatOnGround('{go?.name}'): NO measurable renderer bounds — NOT seated, left at " +
                    $"{(go != null ? go.transform.position.ToString("F2") : "<null>")} (ground y={basePos.y:F2}). " +
                    "The body may float or sink; check the model has an enabled renderer with a mesh.");
                return;
            }

            Vector3 delta = new Vector3(basePos.x - b.center.x,
                                        basePos.y - b.min.y,
                                        basePos.z - b.center.z);
            go.transform.position += delta;

            // VERIFY THE SEAT ACTUALLY LANDED (§12). The shift above is correct by construction
            // *given the bounds it measured*; the failure mode is that the measurement was stale or
            // degenerate (skinned-mesh bounds before the first pose, a renderer that reports an
            // empty AABB). Re-measuring after the move is the only thing that proves the bottom is
            // on the plane — and it makes the offending object print its OWN name and its OWN
            // offending Y, so the next occurrence is one grep away instead of one felt-test away.
            if (!IsSeatedOnGround(go, basePos.y, out float bottomY))
            {
                FlowTrace.Warn("VisualFactory",
                    $"SeatOnGround('{go.name}') LEFT IT OFF THE GROUND: bounds bottom y={bottomY:F2} vs " +
                    $"ground y={basePos.y:F2} (off by {bottomY - basePos.y:F2} m, tolerance " +
                    $"{SeatEpsilonMetres:F2} m). NOTE: the Xform line's localPosition is the PIVOT, " +
                    "not the bottom — this line is the one that decides whether it floats.");
            }
        }

        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            bounds = default;
            if (rends.Length == 0) return false;
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
            return true;
        }

        /// <summary>Levels a prop to rest on its flattest face: rotates (world space) so the
        /// NARROWEST world-bounds axis points to world +Y (CLAUDE.md §4). This is the runtime
        /// twin of CatalogOrientationBaker's bake-time keep-vertical heuristic — a geometry
        /// derivation, not a hand-authored euler. A model already flat (Y narrowest) is left
        /// untouched. Measured from live world bounds so it is robust to any import pivot.</summary>
        private static void SeatFlat(GameObject go)
        {
            if (go == null || !TryBounds(go, out Bounds b)) return;
            Vector3 sz = b.size;

            // Index of the SHORTEST world-bounds axis (0=X, 1=Y, 2=Z). That axis should be vertical
            // so the two longer axes span the ground-resting face.
            int shortest = (sz.x <= sz.y && sz.x <= sz.z) ? 0 : (sz.y <= sz.z ? 1 : 2);
            if (shortest == 1)
            {
                FlowTrace.Step("VisualFactory",
                    $"SeatFlat('{go.name}'): already flat (Y narrowest, size {sz:F2}) — no rotation.");
                return;
            }

            // Bring the current shortest WORLD axis onto world +Y with the shortest-arc rotation,
            // pre-multiplied so it applies in world space (parent may be rotated).
            Vector3 shortAxis = shortest == 0 ? Vector3.right : Vector3.forward;
            Quaternion delta = Quaternion.FromToRotation(shortAxis, Vector3.up);
            go.transform.rotation = delta * go.transform.rotation;

            FlowTrace.Step("VisualFactory",
                $"SeatFlat('{go.name}'): narrowest axis was {(shortest == 0 ? "X" : "Z")} " +
                $"(size {sz:F2}) → stood it to +Y so the flat face rests down.");
        }

        // ── Tripo material fix ──────────────────────────────────────────────
        // WO-1511: TripoMaterialFixer lives in DeNelle.Core, which DeNelle.Village.asmdef
        // already references — so the type is directly nameable and the cached
        // Type.GetType lookup (plus its "type missing" null path) is gone. Behaviour is
        // identical: add the component once, never twice.
        private static void TryAddTripoFixer(GameObject go)
        {
            var fixer = go.GetComponent<DeNelle.Core.TripoMaterialFixer>();
            if (fixer == null) fixer = go.AddComponent<DeNelle.Core.TripoMaterialFixer>();
            // Same stone miss-tint StructureFactory sets. Hub LightSkins go through THIS
            // path, not StructureFactory, so a remaining texture miss degrades to stone
            // instead of bright white (Default Town 365875).
            fixer.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
        }
    }
}
```

### `Assets/_Modules/Village/Catalog/StructureFactory.cs` (exact lines 42-77, inclusive)
Full file SHA-256 `fd7f5d1944bf04db5a2e0310c9d39dec9c1012da99c3a2210e3579cdd2cb0ac4`. Surrounding unrelated source is omitted.

```csharp
    {
        /// <summary>
        /// WO-764 (Y-height normalization, centralized) - the ONE global base ceiling (metres)
        /// every structure is fit-to-height against:
        /// <c>EffectiveHeight = YHeightVariable * repo.heightMul</c>. Every building uses the
        /// default 1.0 multiplier so the whole script-built town reads at ONE uniform height.
        /// THE CADENCE (owner ruling 2026-08-05, "all of the other structures stay within that
        /// cadence... relatively the same size... all scaled to the same point") is ONE FAMILY,
        /// NOT ONE NUMBER: 1.0 building base (4.0 m) / 1.2 TOWER ANCHOR (4.8 m, measured at 49.9%
        /// of a house diameter) / 0.75 siege engines (3.0 m) / 1.25 for the ONE landmark, the
        /// Cathedral of Magic / 0.35 decoration. The authority for the per-group rationale is the
        /// catalog's top-level <c>_heightCadence</c> key; RepoProps.heightMul carries the summary
        /// plus the two standing caveats (collector_farm's 1.4 is a BOUNDS compensation, not a
        /// cadence value; walls are deliberately unauthored for save compat). Change THIS ONE
        /// number and the entire town re-scales together (the owner-locked model). Was the WO-751
        /// per-item-absolute DefaultVisualHeight (also 4 m); the old absolute overrides became
        /// per-item <c>heightMul</c> multipliers.
        /// </summary>
        public const float YHeightVariable = 4f;

        /// <summary>
        /// WO-764 - the fit-to-HEIGHT target (metres) for <paramref name="entry"/>:
        /// <c>YHeightVariable * repo.heightMul</c> (multiplier default 1.0, guarded &gt; 0). Single
        /// source of truth so Create / ReskinForLevel / footprint-measure all fit to the SAME height
        /// (no size jump between placement, upgrade, and ghost/footprint). Only the multiplier changes
        /// WHICH height feeds the fit - the bounds+height scale math in VisualFactory.Fit is untouched.
        /// <paramref name="isOverride"/> is true when the item's multiplier != 1.0 (a deliberate class
        /// exception like a tower), false when it inherits the uniform base.
        /// </summary>
        private static float EffectiveVisualHeight(CatalogEntry entry, out bool isOverride)
        {
            float mult = entry != null && entry.repo != null ? entry.repo.heightMul : 1f;
            if (mult <= 0f) mult = 1f;   // guard a zero/unset/negative authored multiplier -> uniform base
            isOverride = !Mathf.Approximately(mult, 1f);
            return YHeightVariable * mult;
        }
```

### `Assets/_Modules/Village/Catalog/StructureFactory.cs` (exact lines 112-178, inclusive)
Full file SHA-256 `fd7f5d1944bf04db5a2e0310c9d39dec9c1012da99c3a2210e3579cdd2cb0ac4`. This is a call-site excerpt; unrelated construction behavior is omitted.

```csharp
            // DEF-208 + WO-751 (Y-height normalization): EVERY structure fits to HEIGHT now.
            // A tall structure (tower) must fit to HEIGHT, not to its largest bounds dim
            // (fit-to-largest scaled a tower so its tallest axis = footprint ~2.5 m -> a
            // squashed/wrong-scaled tower). WO-764: every structure fits to YHeightVariable *
            // repo.heightMul - buildings inherit the uniform 1.0 base while a class that must read
            // taller or shorter authors its multiplier off the 2026-08-05 cadence (tower 1.2 /
            // siege 0.75 / landmark 1.25 / decoration 0.35), so a tall structure stands tall while
            // the town stays one family.
            if (!string.IsNullOrEmpty(entry.visualPrefabPath))
            {
                float targetHeight = EffectiveVisualHeight(entry, out bool heightOverride);
                // WO-1142: true once the pending-art proxy + its ONE WhenSettled retry are armed,
                // so the render-verify degrade below never arms a rival second subscription.
                bool pendingArtArmed = false;

                // WO-928: build the opts through the ONE shared helper instead of assembling them
                // inline. The inline copy here had drifted from ReskinForLevel's OptsFor - it carried
                // the height and nothing else - so any per-row skin policy added to OptsFor would have
                // reached tier models and MISSED the base visual, i.e. an L1 tower and its L3 tower
                // would obey different rules. That is precisely the class of split this defect is.
                var opts = OptsFor(entry);   // fit-to-height + per-row rotation policy + trace id
                FlowTrace.Step("Structure", $"'{entry.id}' fit-to-height target={targetHeight:0.##}m " +
                    $"(source={(heightOverride ? "override" : "default")}), " +
                    $"preservePrefabRotation={opts.PreservePrefabRotation}.");

                // G: Guard the Skin — a throwing VisualFactory (bad prefab path / Addressables
                // hiccup) logs + rolls up instead of aborting the whole create. null => meshless.
                GameObject visual = Guard.Try("Structure",
                    $"skin '{entry.id}' visual '{entry.visualPrefabPath}'",
                    () => VisualFactory.Skin(root.transform, entry.visualPrefabPath, opts),
                    fallback: null);

                if (visual == null)
                {
                    // WO-1142: an Addressables miss here usually means "not resident THIS FRAME".
                    // Returning null made BaseLayoutLoader drop a paid building, its footprint and
                    // its behaviour for the entire session. Keep a loud, renderer-bearing proxy so
                    // the gameplay root survives, then replace only that proxy after the request
                    // VisualFactory/StructureAssetLoader just issued has settled.
                    FlowTrace.Fail("Structure", $"'{entry.id}': visual '{entry.visualPrefabPath}' " +
                        "is not resident yet — retaining a visible pending-art proxy and arming one " +
                        "WhenSettled retry (the building, footprint and behaviour remain present).");
                    visual = BuildPendingArtProxy(root, entry);
                    pendingArtArmed = true;
                    GameObject capturedProxy = visual;
                    DeNelle.Core.StructureContentWarmer.WhenSettled(() =>
                        TryReplacePendingArt(root, entry, capturedProxy));
                }

                if (entry.orientation != null && entry.orientation.manual)
                {
                    // Euler is applied PRE-fit via OptsFor → LocalRotation (GROK_BRIEF 2026-08-19).
                    // Re-multiplying it here would tip twice. Only offset + non-uniform scale remain
                    // post-Skin; reseat when those move the bounds base off the root y.
                    Guard.Try("Structure", $"apply orientation offset/scale '{entry.id}'", () =>
                    {
                        bool moved = false;
                        Vector3 off = entry.orientation.Offset;
                        if (off.sqrMagnitude > 0.0001f)
                        {
                            visual.transform.localPosition += off;
                            moved = true;
                        }
                        if (entry.orientation.HasScale)
                        {
                            visual.transform.localScale = Vector3.Scale(
                                visual.transform.localScale, entry.orientation.EffectiveScale);
```

### `Assets/_Modules/Village/Catalog/StructureFactory.cs` (exact lines 508-656, inclusive)
Full file SHA-256 `fd7f5d1944bf04db5a2e0310c9d39dec9c1012da99c3a2210e3579cdd2cb0ac4`. Complete visual path/texture/reskin/upgrade options methods; unrelated factory behavior is omitted.

```csharp
        public static string VisualPathForLevel(CatalogEntry entry, int level)
        {
            if (entry == null) return null;
            var ladder = entry.repo != null ? entry.repo.upgradeVisualPath : null;
            if (level >= 2 && ladder != null && ladder.Length >= level - 1
                && !string.IsNullOrEmpty(ladder[level - 2]))
                return ladder[level - 2];
            return entry.visualPrefabPath;
        }

        /// <summary>The FORCED albedo (Resources path) a structure wears at <paramref name="level"/>:
        /// repo.upgradeTexturePath[level-2] when authored (L2 = [0], L3 = [1] - same contract as
        /// upgradeVisualPath), else the base visualTexturePath (which itself may be null). WO-719
        /// upgrade-tier fix: an upgraded spire (ArcaneSpire_2/_3) is a Tripo FBX whose only Color map
        /// is buried in its .fbm folder — it does NOT survive a player build (renders WHITE, exactly
        /// like L1 did before its fix). ReskinForLevel routes this flat Resources albedo through the
        /// FRESH TripoMaterialFixer the tier reskin adds, so the upgraded model keeps its colour.</summary>
        public static string TexturePathForLevel(CatalogEntry entry, int level)
        {
            if (entry == null) return null;
            var ladder = entry.repo != null ? entry.repo.upgradeTexturePath : null;
            if (level >= 2 && ladder != null && ladder.Length >= level - 1
                && !string.IsNullOrEmpty(ladder[level - 2]))
                return ladder[level - 2];
            return entry.visualTexturePath;
        }

        /// <summary>
        /// Swap the skinned visual to the per-tier model for <paramref name="level"/>.
        /// Returns TRUE only when a real per-tier model (different from the base path) is
        /// now worn — the caller then SKIPS the legacy StructureTierVisual scale-step (the
        /// model IS the progression). New visual is skinned BEFORE the old is destroyed, so
        /// a bad path keeps the old look (never a blank structure). No-op-true when the
        /// tier model is already worn (idempotent across re-loads).
        /// </summary>
        public static bool ReskinForLevel(GameObject root, CatalogEntry entry, int level)
        {
            if (root == null || entry == null) return false;

            // TOWER-VFX TIER ESCALATION (owner felt-test 2026-07-17: "more/better VFX at higher tower
            // levels"). Drive the idle aura + firing bursts off the NEW level so an upgraded tower's
            // VFX visibly escalate. Done FIRST — before the tier-model early-returns below — so the
            // escalation fires on EVERY upgrade, even when a structure has no authored tier model
            // (the level still changed). Runs on the live BuildMode upgrade AND on save/reload
            // (BaseLayoutLoader), which both funnel through this one path. Null-safe / colorblind-safe
            // (§7: size/motion, not hue). We only READ the level here; the build-mode upgrade path
            // (BuildModeController) is untouched.
            bool towerLike = entry.id != null &&
                (entry.id.IndexOf("arcane", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.id.IndexOf("wizard", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.id.IndexOf("spire",  System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 entry.id.IndexOf("mage",   System.StringComparison.OrdinalIgnoreCase) >= 0);
            ArcaneAura.EscalateTo(root, level, ensure: towerLike);   // idle aura grows with the tier
            var arcaneSpire = root.GetComponent<ArcaneTower>();
            if (arcaneSpire != null) arcaneSpire.SetVfxLevel(level);  // firing bursts grow with the tier

            string path = VisualPathForLevel(entry, level);
            if (string.IsNullOrEmpty(path) || path == entry.visualPrefabPath)
                return false;   // no authored tier model — legacy scale/tint applies

            // Already wearing it? (Skin instantiates '<prefab>(Clone)' under the root.)
            string stem = path.Substring(path.LastIndexOf('/') + 1);
            for (int i = 0; i < root.transform.childCount; i++)
                if (root.transform.GetChild(i).name.StartsWith(stem)) return true;

            // Collect the current visual children BEFORE adding the new one (renderer-bearing
            // direct children; leaves non-visual children like the WO-612 BuildCountdown alone).
            var old = new List<GameObject>();
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var c = root.transform.GetChild(i);
                if (c.GetComponentInChildren<Renderer>(true) != null) old.Add(c.gameObject);
            }

            GameObject visual = Guard.Try("Structure",
                $"reskin '{entry.id}' L{level} visual '{path}'",
                // Base Euler remains excluded; only an explicitly-authored tier Euler may apply.
                () => VisualFactory.Skin(root.transform, path, OptsForUpgradeLevel(entry, level)),
                fallback: null);
            if (visual == null)
            {
                FlowTrace.Fail("Structure", $"'{entry.id}': tier-{level} visual '{path}' failed to " +
                    "skin — keeping the previous visual (structure never blanks).");
                return false;
            }

            // WO-719 (upgrade-tier albedo): the tier reskin's VisualFactory.Skin just added a FRESH
            // TripoMaterialFixer to this new model with NO forced texture — so an upgraded Tripo spire
            // (ArcaneSpire_2/_3), whose only Color map is buried in its .fbm folder (does NOT survive a
            // player build), would render WHITE exactly like L1 did before its fix. Route the per-tier
            // flat Resources albedo THROUGH that fixer BEFORE its next-frame Start (identical to the L1
            // path in Create) so the forced map is baked into the fixer's single-pass URP/Lit rebuild
            // and STICKS in the build. Covers BOTH the live upgrade (BuildModeController) AND save/
            // reload (BaseLayoutLoader) — both funnel through this one reskin path.
            string texPath = TexturePathForLevel(entry, level);
            if (!string.IsNullOrEmpty(texPath))
            {
                var fixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                fixer?.SetForcedTexture(texPath);
            }

            // WHITE-STRUCTURE FALLBACK (ballista fix 2026-07-19): mirror Create — a tier model whose
            // albedo didn't survive the build would otherwise reskin to SOLID WHITE. Register the same
            // neutral stone MISS-tint (texture-miss-only, so textured tiers are byte-unchanged).
            {
                var missFixer = visual?.GetComponentInChildren<DeNelle.Core.TripoMaterialFixer>(true);
                missFixer?.SetMissTint(new Color(0.60f, 0.58f, 0.54f, 1f));
            }

            // Base orientation remains isolated from upgrade art. A measured tier correction is
            // authored independently in repo.upgradeOrientationEuler and has already been applied
            // by OptsForUpgradeLevel before the model is fitted.

            foreach (var g in old) Object.Destroy(g);

            // COSMETICS re-drive: the renderer set the root's CosmeticApplier decorated a moment ago
            // was just DESTROYED and replaced by the tier model. Without this the upgraded building
            // silently reverts to its bare tier look while the player still has a cosmetic equipped.
            // RefreshOn is a no-op on a root with no applier, and the applier re-collects renderers
            // itself — no second visual path, no re-skin from here.
            CosmeticApplier.RefreshOn(root);

            FlowTrace.Step("Structure", $"'{entry.id}' reskinned to tier-{level} model '{stem}' " +
                $"(replaced {old.Count} old visual(s)); village cosmetic seam re-driven.");
            return true;
        }

        /// <summary>Production skin options for an authored upgrade model.</summary>
        public static SkinOptions OptsForUpgradeLevel(CatalogEntry entry, int level)
        {
            var opts = OptsFor(entry, applyManualEuler: false);
            int index = level - 2;
            var ladder = entry != null && entry.repo != null
                ? entry.repo.upgradeOrientationEuler
                : null;
            if (index >= 0 && ladder != null && index < ladder.Length)
            {
                var euler = ladder[index];
                if (euler != null && euler.Length >= 3)
                {
                    var degrees = new Vector3(euler[0], euler[1], euler[2]);
                    if (degrees.sqrMagnitude > 0.0001f)
                        opts.LocalRotation = Quaternion.Euler(degrees);
                    FlowTrace.Step("Structure",
                        $"OptsForUpgradeLevel('{entry.id}', L{level}): tier Euler={degrees} (index {index}).");
                }
            }
            return opts;
        }
```

### `Assets/_Modules/Village/Catalog/StructureFactory.cs` (exact lines 696-744, inclusive)
Full file SHA-256 `fd7f5d1944bf04db5a2e0310c9d39dec9c1012da99c3a2210e3579cdd2cb0ac4`. Surrounding unrelated source is omitted.

```csharp
        public static SkinOptions OptsFor(CatalogEntry entry, bool applyManualEuler = true)
        {
            var o = SkinOptions.Structure(0f);   // clear FitLargest
            o.FitHeight = EffectiveVisualHeight(entry, out _);

            // Per-row rotation policy (WO-928). Null-guarded the same way EffectiveVisualHeight
            // guards repo: a sparse / missing repo means "no opt-in", i.e. the known-good default.
            o.PreservePrefabRotation = entry != null && entry.repo != null && entry.repo.preservePrefabRotation;

            // FOOTPRINT CAP (owner F8 2026-08-20). One line, and it is deliberately HERE rather
            // than at each call site: Create, ReskinForLevel, the placement GHOST and
            // MeasureUprightFootprintXZ all route through OptsFor, so the grid claim shrinks with
            // the visual and the ghost can never disagree with the placed structure.
            o.MaxFootprint = entry != null && entry.repo != null ? entry.repo.maxFootprint : 0f;

            // GROK_BRIEF 2026-08-19 / owner upright: a manual catalog euler MUST feed
            // SkinOptions.LocalRotation so VisualFactory applies it BEFORE Fit. Post-fit
            // euler measured the lying-down short axis (~6.3 m storefronts). Pre-fit yields 4.00 m.
            if (applyManualEuler && entry != null && entry.orientation != null && entry.orientation.manual)
            {
                Vector3 e = entry.orientation.Euler;
                if (e.sqrMagnitude > 0.0001f)
                    o.LocalRotation = Quaternion.Euler(e);
            }

            // Instrumentation (§12): stamp the ROW id into every Xform line VisualFactory emits, so
            // the preserve-vs-identity branch is attributable to a row by grep. The model name alone
            // cannot do it - GenericContainer is the model for lumberyard AND foundry AND silo.
            o.TraceId = entry != null ? entry.id : null;

            // WO-1157 (§12, owner F8 2026-08-24 "the ballista builds on its side"): the DECISION is
            // logged here, at the one place it is made, rather than being reconstructed downstream
            // from the Xform lines. The three orientation channels (manual euler / preserve / the
            // identity reset) are mutually exclusive and only ONE of them runs for a given row —
            // so the row must SAY which, or every future orientation triage starts by re-deriving
            // it from the catalog by hand. That re-derivation is what produced two wrong theories
            // on this same defect. PERMANENT instrumentation; flag it off, never strip it.
            string channel = o.LocalRotation.HasValue ? "MANUAL EULER (opts.LocalRotation, pre-fit)"
                           : o.PreservePrefabRotation ? "PRESERVE PREFAB ROTATION (repo opt-in row)"
                           : "IDENTITY RESET (DEF-232 default)";
            FlowTrace.Step("Structure",
                $"OptsFor('{o.TraceId ?? "<null>"}'): rotation channel = {channel}; " +
                $"catalogEuler={(entry?.orientation != null ? entry.orientation.Euler.ToString() : "<none>")} " +
                $"manual={(entry?.orientation != null && entry.orientation.manual)} " +
                $"preserve={o.PreservePrefabRotation} applyManualEuler={applyManualEuler} " +
                $"fitHeight={o.FitHeight:0.###} maxFootprint={o.MaxFootprint:0.###}.");

            return o;
        }
```

### `Assets/_Modules/Village/Catalog/StructureFactory.cs` (exact lines 898-1025, inclusive)
Full file SHA-256 `fd7f5d1944bf04db5a2e0310c9d39dec9c1012da99c3a2210e3579cdd2cb0ac4`. Surrounding unrelated source is omitted.

```csharp
        // OrientationFix-applied bounds, not the raw lying-down prefab. These helpers
        // make that the single source of truth used by Create (seat) and the loader/
        // validity (footprint) so all three agree on the same corrected geometry.

        /// <summary>
        /// Re-seat a skinned visual so its CURRENT (post-correction) world-bounds base
        /// sits at <paramref name="groundY"/>. Called AFTER the OrientationFix re-rotates
        /// the mesh, undoing the float/sink VisualFactory.SeatOnGround introduced when it
        /// seated the raw (un-corrected) bounds. XZ is left as VisualFactory centred it.
        /// </summary>
        private static void ReseatCorrectedBottom(GameObject visual, float groundY)
        {
            if (visual == null) return;
            // G: guard the bounds op — a degenerate/NaN mesh bound logs + leaves the seat unchanged
            // (the float self-reports via the bounds-miss path) rather than throwing mid-create.
            Guard.Try("Structure", "reseat corrected bottom", () =>
            {
                if (!TryWorldBounds(visual, out Bounds b))
                {
                    FlowTrace.Warn("Structure", $"ReseatCorrectedBottom: no measurable bounds on '{visual.name}' — left at seat (may float/sink).");
                    return;
                }
                float dy = groundY - b.min.y;
                if (!Mathf.Approximately(dy, 0f))
                    visual.transform.position += new Vector3(0f, dy, 0f);

                // VERIFY THE RESEAT LANDED (§12, 2026-08-20 portal triage). This helper runs AFTER
                // the per-row orientation offset/scale has moved the mesh, i.e. it is the LAST thing
                // that decides whether a placed structure's bottom touches the plaza. It used to
                // shift and return with no proof, so a stale/degenerate bounds measurement produced
                // a silent floater and the only evidence left was the [Flow:Xform] line — whose
                // pos= is the PIVOT, not the bottom, and is therefore routinely misread as a float
                // when a centre-pivoted 4 m building correctly reports local y = +2.00.
                // VisualFactory.IsSeatedOnGround is the SHARED definition of "seated" (same epsilon
                // as the runtime seat and as StructureSeatRegression) — never re-derive it here.
                if (!VisualFactory.IsSeatedOnGround(visual, groundY, out float bottomY))
                    FlowTrace.Warn("Structure",
                        $"ReseatCorrectedBottom('{visual.name}') LEFT IT OFF THE GROUND: bounds bottom " +
                        $"y={bottomY:F2} vs ground y={groundY:F2} (off by {bottomY - groundY:F2} m, " +
                        $"tolerance {VisualFactory.SeatEpsilonMetres:F2} m) — this structure floats/sinks.");
            });
        }

        /// <summary>
        /// Build the entry's visual OFF-SCREEN, apply its OrientationFix, and measure the
        /// resulting UPRIGHT XZ footprint (the larger of width/depth, in metres). Used by
        /// the placement/loader path so the footprint matches the corrected mesh the ghost
        /// shows — a lying-down prefab would otherwise report a long, wrong footprint and
        /// the tower couldn't sit tight to a wall. Returns the entry's authored
        /// repo.placement.footprint as a fallback when the visual can't be measured.
        /// The temp object is destroyed before return (no scene side-effects).
        /// </summary>
        // Cache upright XZ per entry id — ghost loop calls every frame while arming.
        // Key folds orientation + scale so a live re-orient invalidates.
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> s_footprintXzCache =
            new System.Collections.Generic.Dictionary<string, Vector2>();

        /// <summary>
        /// Scalar max(width,depth) — legacy callers / regressions. Prefer
        /// <see cref="MeasureUprightFootprintXZ"/> for CoC non-square claims (WO-986).
        /// </summary>
        public static float MeasureUprightFootprintMetres(CatalogEntry entry)
        {
            Vector2 xz = MeasureUprightFootprintXZ(entry);
            return Mathf.Max(xz.x, xz.y);
        }

        /// <summary>
        /// WO-986: upright mesh claim in metres as (size.x, size.z) — NOT collapsed to max
        /// and squared. Thin structures keep a thin axis so they pack CoC-style.
        /// </summary>
        public static Vector2 MeasureUprightFootprintXZ(CatalogEntry entry)
        {
            float authored = entry != null && entry.repo != null && entry.repo.placement != null
                ? Mathf.Max(1f, entry.repo.placement.footprint) : 3f;
            Vector2 authoredV = new Vector2(authored, authored);
            if (entry == null || string.IsNullOrEmpty(entry.visualPrefabPath)) return authoredV;

            var o = entry.orientation;
            Vector3 es = o != null ? o.EffectiveScale : Vector3.one;
            string key = o != null && o.manual
                ? $"{entry.id}|{o.Euler.x:0.#},{o.Euler.y:0.#},{o.Euler.z:0.#}|{es.x:0.##},{es.y:0.##},{es.z:0.##}|xz"
                : entry.id + "|xz";
            if (s_footprintXzCache.TryGetValue(key, out Vector2 cached)) return cached;

            var probe = new GameObject("FootprintProbe");
            probe.hideFlags = HideFlags.HideAndDontSave;
            Vector2 result = authoredV;
            try
            {
                Guard.Try("Structure", $"measure upright footprint XZ '{entry.id}'", () =>
                {
                    var opts = OptsFor(entry);
                    var visual = VisualFactory.Skin(probe.transform, entry.visualPrefabPath, opts);
                    if (visual == null)
                    {
                        FlowTrace.Warn("Structure",
                            $"MeasureUprightFootprintXZ '{entry.id}': visual '{entry.visualPrefabPath}' failed to skin — using authored {authored:0.##}m square.");
                        return;
                    }
                    if (entry.orientation != null && entry.orientation.manual)
                    {
                        visual.transform.localRotation = Quaternion.Euler(entry.orientation.Euler) * visual.transform.localRotation;
                        visual.transform.localPosition += entry.orientation.Offset;
                        if (entry.orientation.HasScale)
                            visual.transform.localScale = Vector3.Scale(visual.transform.localScale, entry.orientation.EffectiveScale);
                    }
                    if (TryWorldBounds(visual, out Bounds b))
                    {
                        // World AABB after orientation — CoC claim axes (WO-986).
                        result = new Vector2(
                            Mathf.Max(0.1f, b.size.x),
                            Mathf.Max(0.1f, b.size.z));
                    }
                    else
                        FlowTrace.Warn("Structure",
                            $"MeasureUprightFootprintXZ '{entry.id}': no measurable bounds — using authored square.");
                });
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(probe);
                else                       Object.DestroyImmediate(probe);
            }
            s_footprintXzCache[key] = result;
            return result;
        }

```

### `Assets/_Modules/Core/Catalog/CatalogEntry.cs` (complete file)
SHA-256 `7afdd449576a08360b0ca75cb7f4fdada3c3199bcd1558015448eae0bc17e14b`

```csharp
// =============================================================================
// CatalogEntry — one def in the catalog. visualPrefabPath = LOOK (a real
// polyperfect prefab path), repo = BEHAVIOR. A Composite is a pre-snapped set
// of cell placements. This is the unit the build palette lists and the
// dispatcher builds. Pure data (DeNelle.Core).
// =============================================================================
using UnityEngine;

namespace DeNelle.Core.Catalog
{
    /// <summary>A cell inside a Composite: which cell-entry, at what relative offset + rotation.</summary>
    [System.Serializable]
    public sealed class CellPlacement
    {
        public string  cellEntryId;
        public Vector3 offset;
        public float   yRotation;   // 90° steps

        public CellPlacement() { }
        public CellPlacement(string cellEntryId, Vector3 offset, float yRotation)
        {
            this.cellEntryId = cellEntryId;
            this.offset = offset;
            this.yRotation = yRotation;
        }
    }

    [System.Serializable]
    public sealed class CatalogEntry
    {
        public string      id;
        public string      displayName;
        /// <summary>
        /// WO-1081 - the one player-facing sentence saying what this building does.
        ///
        /// <para>⛔ THERE IS NO FALLBACK SENTENCE ANY MORE, AND AN UNAUTHORED ROW IS A GATE
        /// FAILURE, NOT A DEFAULT. This comment read <i>"absent/blank falls back to the per-type
        /// sentence"</i> until 2026-09-07 (WO-1534 §B5) — WO-1565 had already DELETED that prose
        /// from <c>StructureCardVM.DescriptionFor</c>, which now projects authored copy only.
        /// The per-type sentence is exactly what made all four Tower rows read <i>"A defensive
        /// tower ... auto-fires on enemies in range"</i>, so the Catapult described itself as a
        /// tower and the anti-air Sky Ballista read the same as the ground ones — a stub that
        /// looked authored, which is why it survived for months.</para>
        ///
        /// <para>Two oracles hold it. <c>BuildEconomyRegression.CheckStructureDescriptions</c>
        /// FAILS the build on any row with a <see cref="visualPrefabPath"/> and no description,
        /// and pins the deletion so the prose cannot come back; case 7 of
        /// <c>BuildInventoryFilterRegression</c> pins that every row the Manage grid renders
        /// (i.e. carrying <see cref="manageFilters"/>) is inside that gate's reach.</para>
        /// </summary>
        public string      description;
        public CatalogType type;
        public EntryKind   kind = EntryKind.Cell;

        /// <summary>
        /// WHAT THIS BUILDING IS — an open, data-authored role string (WO-1161).
        /// Resolve it through <see cref="StructureRoles"/>:
        /// <c>StructureRoles.By[StructureRole.Armorer].DisplayName</c>.
        ///
        /// <para>⛔ THE `id` IS AN OPAQUE SAVE KEY AND THIS FIELD EXISTS SO IT CAN STAY ONE.
        /// `everBuiltStructureIds`, BaseLayout records, baked scenes, vendors.json and
        /// dialogues.json all join on `id`, and the game is LIVE on the Solana dApp Store —
        /// renaming `forge` orphans every existing player's building. The id never moves;
        /// the ROLE carries the meaning, and the DISPLAY NAME is free to change.</para>
        ///
        /// <para>⛔ OPEN VOCABULARY, ON PURPOSE (owner 2026-08-23: "the idea is staying
        /// fluid" / "if we add a building we do not want to have to manually code it").
        /// Any string is legal. <see cref="StructureRole"/> only names the few roles CODE
        /// branches on; a brand-new role resolves with no code change at all.</para>
        ///
        /// <para>⛔ FUNCTION IS THE AUTHORITY (owner: "which sells weapons, that is the
        /// weaponsmith use the JSON data") — assign the role from what the row DOES in
        /// vendors.json, NEVER from the word currently printed on its tile. Ignoring that
        /// is how `forge` came to sell weapons while displaying "Armorer".</para>
        ///
        /// Absent/empty = unroled, which is exactly how every row behaved before this
        /// field existed. Nothing regresses by leaving it unset.
        /// </summary>
        public string      role;

        /// <summary>
        /// WO-2005 — the Manage &gt; BUILD filter chips this row belongs to. **DATA IS THE
        /// AUTHORITY** (Manage redesign canon §3 / owner ruling 5): the UI may NOT infer a
        /// category from an id prefix, a class name, an asset name or the tab a row used to
        /// live on.
        ///
        /// <para>⛔ THIS IS NOT <see cref="role"/>, AND IT MUST NEVER BE FOLDED INTO IT. A role
        /// identifies exactly ONE row — <c>StructureRoles.Index</c> emits a
        /// <c>FlowTrace.Fail</c> collision the moment two rows claim the same one — so a role
        /// can never be a category. A filter is many-to-many by construction, which is why it
        /// is a separate array.</para>
        ///
        /// <para>Legal tokens are named (and validated) by <see cref="BuildFilter"/>;
        /// comparison is ordinal case-insensitive. ALL is NOT authored here — it is the
        /// unfiltered list, and authoring it would be duplicated state.</para>
        ///
        /// <para>Null/empty = the row is not Manage content at all (today: <c>deco_torch</c>,
        /// <c>repair_default</c>). Every row the build browser OFFERS must carry at least one
        /// token; <c>BuildInventoryFilterRegression</c> fails the build otherwise.</para>
        /// </summary>
        public string[]    manageFilters;

        /// <summary>
        /// WO-2005 — the art-sheet tile name for this row (e.g. <c>building-sky-ballista</c> for
        /// id <c>tower_siege_tower</c>), from the Sheet A delivery
        /// (<c>docs/ART_DELIVERY_2026-09-06_manage_assets.md</c> appendix).
        ///
        /// <para>⛔ THE ART NAME AND THE CATALOG ID ARE DIFFERENT LANGUAGES ON PURPOSE, and the
        /// join lives HERE, in data. <c>building-sky-ballista</c> is <c>tower_siege_tower</c>;
        /// <c>building-wooden-palisade</c> is <c>wall_wood</c>. A resolver that parsed names
        /// would have to encode both of those as special cases and would break on the next
        /// re-skin — the id is a live save key and can never move to match the art.</para>
        ///
        /// <para>Null = no tile is drawn for this row on any delivered sheet (today:
        /// <c>mill</c>, <c>deco_torch</c>, <c>repair_default</c>). Not an error — a missing tile
        /// is a presentation fallback, never a gate.</para>
        /// </summary>
        public string      manageArtKey;

        /// <summary>
        /// PRESENTATION ONLY (WO-963) — the build palette carousel's authored display order.
        /// LOWER SORTS FIRST; 0 (the default, i.e. the JSON key absent) means UNAUTHORED and
        /// sorts AFTER every authored row, keeping its current relative position — the sort is
        /// STABLE on catalog row order, so an unauthored row can never jump.
        ///
        /// Seeded to the TUTORIAL'S TEACHING ORDER so the shelf and the script agree. The
        /// palette NEVER reads tutorial-steps.json at runtime (ARCHITECTURE_PRINCIPLES §1/§2 —
        /// presentation must not take a teaching script as an input); the catalog carries the
        /// order and BuildCarouselTutorialOrderRegression is what keeps the two agreeing.
        ///
        /// Consumed by <c>BuildPaletteVM.SortForDisplay</c> and nothing else: it gates nothing,
        /// prices nothing and changes no group split.
        /// </summary>
        public int         displayOrder;

        /// <summary>LOOK — Resources/polyperfect-style prefab path. Resolved to a model at build time.</summary>
        public string      visualPrefabPath;

        /// <summary>
        /// LOOK (WO-707, ported from HubStructureVisualInjector.Swap.texPath) — OPTIONAL
        /// Resources texture FORCED onto the skinned visual's materials when the model's
        /// embedded material lost its texture link and would render colorless (e.g. the
        /// 'Structures/arcane tower' Tripo FBX — its owner-dialed swap row carried
        /// texPath "Structures/ArcaneTower_Albedo" -- the atlas was moved OUT of the nested
        /// "arcane tower/" folder whose name collided with the sibling "arcane tower.fbx"
        /// (that collision made Resources.Load return null and left the spire pure white)). Applied by
        /// StructureFactory.Create after the skin; null (default) = untouched.
        /// JSON deserializes "visualTexturePath" straight in.
        /// </summary>
        public string      visualTexturePath;

        /// <summary>BEHAVIOR — stats, nav, placement, behaviour id.</summary>
        public RepoProps   repo = new RepoProps();

        /// <summary>Composites only: the cell set to drop as a bundle. Null for cells.</summary>
        public CellPlacement[] composite = null;

        /// <summary>
        /// Orientation correction (CatalogOrientationBaker / the future Orientation
        /// Inspector). Auto-baked entries are ADVISORY (manual=false) and are NOT
        /// applied — a bounds heuristic can't tell an authored-flat-but-skinned-upright
        /// model (e.g. tower2: Y=0.37, Z=1.00) from a genuinely tipped one, so auto-
        /// rotating would tip good assets. Only HUMAN-verified (manual=true) corrections
        /// are applied by StructureFactory.
        /// </summary>
        public OrientationFix orientation = null;
    }

    /// <summary>A stored keep-vertical correction for a catalog entry's visual.</summary>
    [System.Serializable]
    public sealed class OrientationFix
    {
        public bool    corrected;
        public bool    manual;        // true = human-verified (Inspector) → applied; false = advisory
        public float[] euler;         // [x,y,z] degrees
        public float[] offset;        // [x,y,z] metres
        public float   scale = 1f;    // UNIFORM scale multiplier (legacy / back-compat).
        // NON-UNIFORM per-axis scale (WO: stretch X or Z, keep Y height) — multiplies on
        // TOP of the uniform `scale` per component. OPTIONAL + backward-compatible: when
        // absent/null (every existing entry) EffectiveScale falls back to (1,1,1), so the
        // effective scale equals the legacy uniform `scale` exactly. Serialized as [x,y,z].
        public float[] scaleAxis;     // [x,y,z] per-axis multipliers; null/short → (1,1,1)
        public string  note;

        public Vector3 Euler  => euler  != null && euler.Length  == 3 ? new Vector3(euler[0],  euler[1],  euler[2])  : Vector3.zero;
        public Vector3 Offset => offset != null && offset.Length == 3 ? new Vector3(offset[0], offset[1], offset[2]) : Vector3.zero;

        /// <summary>Per-axis multipliers as a Vector3, defaulting to (1,1,1) when unset/short.</summary>
        public Vector3 ScaleAxis => scaleAxis != null && scaleAxis.Length == 3
            ? new Vector3(scaleAxis[0], scaleAxis[1], scaleAxis[2])
            : Vector3.one;

        /// <summary>
        /// The full per-axis scale to APPLY: uniform <see cref="scale"/> (clamped to a sane
        /// positive, legacy 1) times the per-axis <see cref="ScaleAxis"/> per component. For
        /// every existing entry (scale only, no scaleAxis) this returns (s,s,s) — identical
        /// to the old uniform behaviour. Returned components are forced positive (≥ a tiny
        /// epsilon) so a 0/garbage value never collapses the mesh.
        /// </summary>
        public Vector3 EffectiveScale
        {
            get
            {
                float s = scale > 0f ? scale : 1f;
                Vector3 a = ScaleAxis;
                return new Vector3(
                    Mathf.Max(0.0001f, s * a.x),
                    Mathf.Max(0.0001f, s * a.y),
                    Mathf.Max(0.0001f, s * a.z));
            }
        }

        /// <summary>True when the effective scale differs from identity on any axis.</summary>
        public bool HasScale
        {
            get
            {
                Vector3 e = EffectiveScale;
                return !Mathf.Approximately(e.x, 1f)
                    || !Mathf.Approximately(e.y, 1f)
                    || !Mathf.Approximately(e.z, 1f);
            }
        }
    }
}
```

### `Assets/_Modules/Core/Catalog/RepoProps.cs` (exact lines 355-477, inclusive)
Full file SHA-256 `c75ccdce0bfa06ca44698b82af5ffdeea67ec771f0ee8be19697b0c0b1b5cd21`. Surrounding unrelated source is omitted.

```csharp
        /// <summary>Placement conditions, evaluated at the free cursor.</summary>
        public PlacementRules placement = new PlacementRules();

        /// <summary>
        /// WO-764 — the per-item Y-height MULTIPLIER against the ONE global base ceiling
        /// (StructureFactory.YHeightVariable = 4 m). The skinned model is fit-to-HEIGHT so its
        /// world-bounds Y == <c>YHeightVariable * heightMul</c>. DEFAULT 1.0 = every building
        /// normalizes to the base ceiling (uniform, script-built town — the owner-locked model).
        /// Change the base in ONE place and the whole town re-scales together.
        /// <para>THE CADENCE (owner ruling 2026-08-05, "I want all of the other structures to stay
        /// within that cadence... relatively the same size... all scaled to the same point"). ONE
        /// FAMILY, NOT ONE NUMBER - the full rationale per group lives in the catalog's top-level
        /// <c>_heightCadence</c> key, which is the authority; this is the summary:
        /// <list type="bullet">
        /// <item>1.00 (4.0 m) - BUILDING BASE. Houses, production, vendors, storage, collectors,
        /// civic. The width reference: House_Medieval_Medium fits to 5.562 m across.</item>
        /// <item>1.20 (4.8 m) - TOWER, and the ANCHOR the rest of the family is expressed against.
        /// Owner-ruled and MEASURED: 2.778 m across = 49.9% of a house diameter, i.e. the ruling's
        /// "half as wide as the diameter of any of the houses". The WHOLE tower class sits here as
        /// of 2026-08-05 (tower_wall_wizard and tower_arcane_spire came off the old 1.25).</item>
        /// <item>0.75 (3.0 m) - SIEGE ENGINE (catapult, wall-walk sky ballista). Machines, not
        /// architecture; deliberately under the house line.</item>
        /// <item>1.25 (5.0 m) - LANDMARK, exactly one row (<c>arcane-tower</c>, the Cathedral of
        /// Magic). The town's single apex.</item>
        /// <item>0.35 (1.4 m) - DECORATION (<c>deco_torch</c>). Unauthored it inherited the 1.0
        /// building base, i.e. a wall torch as tall as a house.</item>
        /// </list></para>
        /// <para>NOT A CADENCE VALUE - <c>collector_farm</c> = 1.4. This multiplier fits BOUNDS, so
        /// a spindly silhouette reads SMALLER than a boxy one at the same number; the farm's windmill
        /// blades inflate its Y bounds and 1.4 is the owner felt-report compensation that puts its
        /// BODY back on the 4 m line. Never "normalize" it to 1.0. The same caveat applies to any
        /// cross-row comparison: equal heightMul does NOT mean equal apparent size.</para>
        /// <para>HEIGHT AND FOOTPRINT ARE ONE NUMBER, by design. The fit is a UNIFORM scale, so this
        /// multiplier moves the base footprint by the same factor, and
        /// StructureFactory.MeasureUprightFootprintMetres measures the real estate off the
        /// height-fitted model, never off the authored placement.footprint (that is only the
        /// prefab-missing fallback). There is no width dial and none is needed. Corollary for
        /// SAVE COMPAT: the grid claim is ceil(measured / 3 m), so RAISING a multiplier can grow a
        /// claim and make an existing saved town reload with OVERLAPPING claims - always state the
        /// before/after cell claim when you change one. (Lowering only shrinks a claim, which is
        /// overlap-safe, but for WALLS a narrower segment opens pathable GAPS in already-placed
        /// runs, which is why wall_wood/wall_stone/gate_stone were deliberately left at 1.0.)</para>
        /// JSON deserializes "heightMul" straight in. SUPERSEDES the
        /// deprecated absolute <see cref="visualHeight"/> below.
        /// </summary>
        public float heightMul = 1.0f;

        /// <summary>
        /// FOOTPRINT CEILING in metres for the height-fitted model — the widest HORIZONTAL
        /// world-bounds extent (max of X and Z) this row is allowed to occupy. &gt;0 arms it;
        /// <b>DEFAULT 0 = DISARMED = exactly today's behaviour</b>, so a row that does not author
        /// it is byte-identical to before this field existed. It only ever scales DOWN, never up,
        /// and it scales UNIFORMLY — the model keeps its proportions, it just stops eating the
        /// plaza. Applied AFTER the height fit (VisualFactory.Fit), so it is a CAP on that fit and
        /// not a second competing fit.
        /// <para>WHY A SECOND AXIS EXISTS AT ALL — and why <see cref="heightMul"/> could not do
        /// this job. Fit-to-height is a SINGLE-AXIS promise over a UNIFORM scale: the model is
        /// scaled by <c>target / boundsY</c>, so whatever the footprint happens to be, it comes
        /// along at the same factor. That is fine while every model's fitted pose is roughly
        /// building-shaped. It breaks the moment a model's fitted pose is FLAT, because a small
        /// Y divisor makes the scale explode and the footprint explodes with it. MEASURED, on
        /// device, 2026-08-20 (logs/device/2026-08-20-portal.log, [Flow:Xform] + [Flow:VisualFactory]
        /// "skinned"): <c>Structures/farm</c> is natively 0.977 x 1.000 x 0.391 m, and the row's
        /// authored orientation euler (-90,0,0) — applied PRE-fit since the GROK_BRIEF 2026-08-19
        /// change — stands the 0.391 axis up. Fit therefore divided a 5.6 m target by 0.391 and
        /// produced <c>scale=(14.34)</c> and a fitted footprint of <b>14.00 x 14.34 m</b>, against
        /// a family that measures 2.8-5.8 m across. The owner's report was "farm seems to be much
        /// larger than anything else"; the number behind that felt-report is 3.5x.</para>
        /// <para>NO OTHER ROW CAN BE FIXED BY DIALING HEIGHT INSTEAD. Both directions on
        /// <see cref="heightMul"/> are wrong here: lowering it shrinks the BUILDING as well as the
        /// footprint (that is literally the "shrunk farm" the owner already rejected, commit
        /// 31b41d19), and raising it makes the footprint worse. Height and footprint were ONE
        /// number by design; this is the deliberate, opt-in second number for the case where that
        /// design has no answer.</para>
        /// <para>SAVE COMPAT, same rule as <see cref="heightMul"/>: BuildModeController claims
        /// <c>ceil(measured / 3 m)</c> cells from StructureFactory.MeasureUprightFootprintXZ, which
        /// measures the fitted model, so arming this key SHRINKS a claim. Shrinking is overlap-safe
        /// (a saved town reloads with a smaller claim, never an overlapping one) — collector_farm
        /// goes 5x5 cells -&gt; 2x2. RAISING an already-armed cap can grow a claim; state the
        /// before/after cell claim when you change one, exactly as for heightMul. Never arm this on
        /// a WALL row: a narrower segment opens pathable GAPS in already-placed runs.</para>
        /// <para>Read by <c>StructureFactory.OptsFor</c> into <c>SkinOptions.MaxFootprint</c>, so it
        /// reaches Create, ReskinForLevel, the placement GHOST and MeasureUprightFootprintXZ through
        /// the one shared options builder — the ghost cannot disagree with the placed structure.
        /// JSON deserializes "maxFootprint" straight in.</para>
        /// </summary>
        public float maxFootprint = 0f;

        /// <summary>
        /// DEPRECATED (WO-764) — the legacy ABSOLUTE visual height (metres) the model was fit to.
        /// Superseded by <see cref="heightMul"/> (base × multiplier) and NO LONGER READ by
        /// StructureFactory.EffectiveVisualHeight. Retained only so any older serialized JSON that
        /// still carries a "visualHeight" key deserializes without error. Do NOT author new rows
        /// against it — use <see cref="heightMul"/>.
        /// </summary>
        public float visualHeight = 0f;

        /// <summary>
        /// WO-928 (2026-08-08, owner felt-test "the L3 Archer Tower is lying on its side") — KEEP the
        /// skinned model's OWN authored root rotation instead of flattening it to identity.
        /// <para>THE DEFAULT (false) IS THE KNOWN-GOOD BEHAVIOUR AND MUST STAY THE DEFAULT.
        /// VisualFactory.Skin resets an instantiated model's root to identity (DEF-232). That is
        /// RIGHT for almost every structure here: most Tripo building FBXs instantiate at euler
        /// (270,0,0) and the reset is exactly what CANCELS that. A handful of assets are the
        /// opposite case — their native 270 IS the upright correction — and for those the reset both
        /// lays the model down AND makes VisualFactory.Fit measure the SHORT axis to reach the height
        /// target, so it ships sideways *and* oversized (one defect, not two).</para>
        /// <para>ELIGIBILITY, read off the data rather than guessed: a row that authors a NON-ZERO
        /// manual <see cref="CatalogEntry.orientation"/> is PERMANENTLY INELIGIBLE. Thirteen rows
        /// carry a manual (-90,0,0) which StructureFactory.Create applies on top of the identity-reset
        /// root; preserve the native 270 as well and the two COMPOSE to 180 — upside down. Only a row
        /// whose correction lives in the ASSET, with nothing to apply from the row, may opt in.</para>
        /// <para>SCOPE IS THE ROW, DELIBERATELY. The same flag was set for the whole structure CLASS
        /// (on SkinOptions.Structure) earlier the same day and laid the entire town down; see the ⛔
        /// block there for the captured trace. The single reader is StructureFactory.OptsFor.</para>
        /// <para>VERIFYING A CHANGE HERE REQUIRES A RETURN-TO-TOWN PASS, NOT A FIRST LOAD: the first
        /// town load seats buildings from the bake/injector path, and only re-entry (exit to a dungeon
        /// and come back) rebuilds them through BaseLayoutLoader → StructureFactory.Create →
        /// VisualFactory.Skin, the ONLY route that reads this field. That is why the class-wide flip
        /// shipped unnoticed.</para>
        /// JSON deserializes "preservePrefabRotation" straight in.
        /// </summary>
        public bool preservePrefabRotation = false;
```

### `Assets/_Modules/Village/Catalog/StructureFactory.cs` (exact lines 1109-1123, inclusive; complete TryWorldBounds method)
Full source SHA-256 `fd7f5d1944bf04db5a2e0310c9d39dec9c1012da99c3a2210e3579cdd2cb0ac4`. Unlike the fit/oracle bounds helper, this footprint helper includes inactive renderers and has a collider fallback; compare actual call paths rather than assuming identical coverage.

```csharp
        private static bool TryWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null) return false;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                bounds = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
                return true;
            }
            var col = go.GetComponentInChildren<Collider>(true);
            if (col != null) { bounds = col.bounds; return true; }
            return false;
        }
```

## Complete existing sizing regression

### `Assets/Editor/Regression/StructureCadenceRegression.cs` (complete file)
SHA-256 `693a20f774a6437d520853f5330aa6ac764a5d873aab286cc12dd0441117a780`

```csharp
// =============================================================================
// StructureCadenceRegression [structure-cadence] — the gate that can SEE a
// building that dwarfs the town, because fit-to-HEIGHT structurally cannot.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Markers: STRUCTURE_CADENCE_OK /
// STRUCTURE_CADENCE_FAIL.  Editor-only asset reads. No scene, no PlayMode.
//
// that was forbidden to touch DataRegression.cs (another lane owns it this hour).
// The committer wires the registration line; this token exists ONLY so
// RegressionMarkerRegression RULE 2 does not red the tree in the window between
// this file landing and that line landing. DELETE THIS TOKEN in the same commit
// that registers the suite — a suite that stays "standalone" is a suite that
// never runs, which is the failure this file was written about.
//
// Standalone: run-unity-method
//   -Method DeNelle.Editor.StructureCadenceRegression.RunAll
//
// =============================================================================
// WHY THIS EXISTS — THE CAPTURED DEFECT, NOT A THEORY
// =============================================================================
// Owner, on device, 2026-08-20: "farm seems to be much larger than anything else".
// The numbers behind that felt-report, read off logs/device/2026-08-20-portal.log
// ([Flow:Xform] "after Fit+SeatOnGround" + [Flow:VisualFactory] "skinned ...
// boundsSize"), NOT inferred from source:
//
//   'farm'   (entry='collector_farm')  scale=(14.34)  fitted bounds 14.00 x 5.60 x 14.34 m
//   'Forge'  (entry='forge')           scale=(3.99)   fitted bounds  2.91 x 4.00 x  2.55 m
//   'store'  (entry='market')          scale=(4.01)   fitted bounds  4.02 x 4.00 x  3.78 m
//   'lumbermill' (collector_lumbermill) scale=(5.09)  fitted bounds  5.09 x 4.00 x  4.84 m
//
// Dividing the fitted bounds by the fitted scale gives the model's NATIVE size —
// Structures/farm is 0.977 x 1.000 x 0.391 m, and the two independent captures
// (identity-reset pose and euler-applied pose) agree to three decimals, which is
// how we know the number rather than believing it.
//
// =============================================================================
// THE MECHANISM (read at source; file:line so the next seat can re-derive it)
// =============================================================================
// VisualFactory.Fit, the `largest:false` arm (Assets/_Modules/Village/VisualFactory.cs):
//     measure = bounds.size.y;  localScale *= target / measure;
// StructureFactory.OptsFor clears FitLargest and sets FitHeight, so every structure
// fits to HEIGHT. That is a SINGLE-AXIS promise executed as a UNIFORM scale: the
// footprint is never asked about, it just rides along at the same factor.
//
// That is harmless while every model's fit-time pose is roughly building-shaped.
// It detonates when a model's fit-time pose is FLAT, because the divisor is tiny:
// collector_farm authors orientation.euler (-90,0,0), which since the GROK_BRIEF
// change of 2026-08-19 is applied PRE-fit via SkinOptions.LocalRotation, and that
// stands the model's 0.391 m axis UP. 5.6 / 0.391 = 14.34, and the 1.000 m plan
// axis is multiplied by that same 14.34 — a 14 m building in a 3-5 m town.
//
// NEITHER DIRECTION ON heightMul FIXES IT, which is why a new axis had to exist:
// lowering it re-ships "the shrunk farm" the owner already rejected (commit
// 31b41d19); raising it makes the footprint worse. The fix is repo.maxFootprint —
// a CEILING on the fitted model's widest horizontal extent, default 0 = disarmed.
//
// =============================================================================
// WHAT THIS SUITE ASSERTS
// =============================================================================
// C0  SELF-TEST (both directions, runs FIRST). The outlier rule is a pure function
//     over (label, widest-metres). It is fed a synthetic CLEAN family and must pass
//     it, then the same family plus one 14.34 m pancake and must fail exactly that
//     row. A gate that has never been shown to go red is not evidence of anything.
//
// C1  CATALOG COPIES ARE BYTE-IDENTICAL. Resources wins at load and StreamingAssets
//     ships to the device; a divergence means the thing measured here is not the
//     thing the player gets. Byte compare, not JSON compare — a re-serialization
//     that reorders keys is still a divergence worth knowing about.
//
// C2  FOOTPRINT OUTLIER BAND (measured, BASE-HEIGHT-relative, upper bound only).
//     Every row's base visual is replayed through the shipped pipeline and its
//     widest horizontal extent max(x,z) is compared to an ABSOLUTE ceiling:
//     StructureFactory.YHeightVariable * CadenceWidthRatio = 4.0 * 2.6 = 10.4 m.
//
//     ⚠ UNTIL 2026-08-26 THIS COMPARED AGAINST THE FAMILY MEDIAN, AND THAT
//     REFERENCE WAS ITSELF A DEFECT (WO-1239). The intent was right — "it holds if
//     the owner re-scales the whole town" — but a median over the measured
//     population silently RE-THRESHOLDS whenever ANY member changes size. WO-1224
//     halved three GenericContainer rows; the median fell 4.32 -> 3.78 m, the band
//     fell 8.64 -> 7.56 m, and 'barracks' (7.64 m in both runs, untouched) was
//     reported as an outlier. The gate went red because three OTHER buildings got
//     SMALLER. Measured, not theorised: Builds/wo1211-reg.log (green) vs
//     Builds/gate-r3 (red), same 27 rows, same ids, three rows different.
//
//     YHeightVariable keeps the whole-town-re-scale property (it IS the one number
//     the town scales from) with none of the population coupling, and it still
//     needs no list of ids and no per-row thresholds. The family median is still
//     COMPUTED AND PRINTED, as observability — never again as a threshold.
//
// C3  AN ARMED CAP IS OBEYED. A row authoring repo.maxFootprint must measure at or
//     under it. This is the direct assert on the fix; C2 is the assert on the next
//     model nobody has imported yet. NOTE the cap is a UNIFORM scale-down, so it
//     shortens the building as well as narrowing it — it is the right tool for a
//     model that is FLAT AT FIT TIME (where the height was inflated by a tiny
//     divisor anyway) and the WRONG tool for a correctly-posed wide building, which
//     it would simply shrink. See the WO-1239 note under C2.
//
// C4  THE PRODUCTION PATH CARRIES THE CAP. SkinOptions must expose a public float
//     MaxFootprint and StructureFactory.OptsFor must populate it from the row.
//     Checked by REFLECTION deliberately (see the note on the method): this file is
//     authored on a lane that may not edit those two files, and without C4 the
//     suite would go green over data that nothing reads — a catalog key with no
//     consumer is exactly the silent no-op this project keeps re-learning.
//
// =============================================================================
// ⚠ WHAT THIS SUITE DOES **NOT** COVER — stated, never special-cased
// =============================================================================
// (1) UPPER BOUND ONLY. A row that measures far SMALLER than the median is not
//     flagged: deco_torch (heightMul 0.35) is deliberately tiny and the siege group
//     deliberately sits under the house line, so a symmetric band would red honest
//     rows. "Too small" already has an owner-visible channel (the felt-report that
//     produced the farm's 1.4 in the first place); "too large" did not, until now.
// (2) TIER MODELS (repo.upgradeVisualPath) are not measured here. They are fit
//     through the same OptsFor call and StructureOrientationOracle already measures
//     them for height; adding them to a FAMILY MEDIAN would mix L1 and L3 silhouettes
//     into one statistic and blunt it. A tier whose art is a pancake is a real gap.
// (3) THE HUB INJECTOR PATH IS NOT COVERED. HubStructureVisualInjector hand-rolls
//     its own SkinOptions (it sets FitHeight from YHeightVariable * a local mult
//     rather than calling OptsFor), so a cap authored in the catalog does NOT reach
//     it. The same device log shows that path producing 'farm' at 17.93 x 4.00 x
//     14.40 m. Routing it through OptsFor is a separate ticket; naming it here is
//     the honest alternative to a special case.
// (4) RealmStore is not a catalog row (StructureOrientationOracle coverage note 1),
//     so no catalog-driven suite can see it.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DeNelle.Core;            // CanonicalJson
using DeNelle.Core.Catalog;    // CatalogEntry / RepoProps
using DeNelle.Village;         // SkinOptions / StructureFactory
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    /// <summary>
    /// Footprint-cadence oracle over structures-catalog.json: no structure may render
    /// wildly wider than its family. Returns true (summary) / false (detail); never throws.
    /// </summary>
    public static class StructureCadenceRegression
    {
        public const string MarkerOk   = "STRUCTURE_CADENCE_OK";
        public const string MarkerFail = "STRUCTURE_CADENCE_FAIL";

        private const string CatalogRelPath = "Data/Canonical/structures-catalog.json";
        private const string ResourcesCopy   = "Resources/Data/Canonical/structures-catalog.json";
        private const string StreamingCopy   = "StreamingAssets/Data/Canonical/structures-catalog.json";

        /// <summary>
        /// THE BAND, expressed as a multiple of <see cref="StructureFactory.YHeightVariable"/> —
        /// the ONE global base fit height (4.0 m) that the whole town is scaled from. A row is an
        /// outlier when its widest horizontal extent exceeds <c>YHeightVariable * this</c>.
        /// <para>⚠ THIS USED TO BE "2.0x THE FAMILY MEDIAN" AND THAT REFERENCE WAS THE DEFECT
        /// (WO-1239, 2026-08-26). A median over the measured population silently RE-THRESHOLDS
        /// every time any member changes size, in either direction. The proof, measured not
        /// theorised — Builds/wo1211-reg.log (green, 08-25 21:15) vs Builds/gate-r3 (red, 08-26
        /// 17:23), same 27 rows, same ids:
        /// WO-1224 halved three GenericContainer rows (lumberyard/foundry/silo, heightMul 0.5),
        /// so their widest went 5.83 -> 2.91 m. That moved the MEDIAN 4.32 -> 3.78 m and the band
        /// 8.64 -> 7.56 m, and 'barracks' — which measured 7.64 m in BOTH runs and was not edited
        /// — became an "outlier" without changing by one millimetre. i.e. the gate went red
        /// because three OTHER buildings got SMALLER, which is the town getting BETTER. A
        /// threshold that inverts like that is not measuring the thing it names.</para>
        /// <para>The base height is the right reference and keeps the original design intent:
        /// the reason a family-relative band was chosen was "it holds if the owner re-scales the
        /// whole town", and YHeightVariable IS that one number ("change THIS ONE number and the
        /// entire town re-scales together" — StructureFactory). Only now it cannot be moved by an
        /// unrelated row. Deliberately the FLAT base, NOT the row's own fit height
        /// (YHeightVariable * heightMul): collector_farm authors heightMul 1.4, so a row-relative
        /// ceiling would have been 5.6*2.6 = 14.56 m and the measured 14.34 m defect this suite
        /// exists for would have walked straight through it.</para>
        /// <para>2.6 IS NOT A TASTE VALUE — it is bracketed by the same two measurements the old
        /// 2.0 was, re-expressed against the base: the widest HONEST structure in the town is
        /// 'barracks' at 7.64 m = 1.91x base (with 'wall_stone' right behind at 7.42 = 1.86x),
        /// and the collector_farm defect measured 14.34 m = 3.58x base. 2.6 is the GEOMETRIC
        /// midpoint of 1.91 and 3.58 (sqrt(1.91*3.58) = 2.615), i.e. equal multiplicative margin
        /// on both sides: 1.36x of headroom over the widest honest row, 1.38x of bite before the
        /// known defect. Ceiling = 10.4 m. Raising this to make something pass defeats the file;
        /// the row's repo.maxFootprint cap is the per-row dial.</para>
        /// </summary>
        private const float CadenceWidthRatio = 2.6f;

        /// <summary>The absolute ceiling in metres. One place, derived from the shipped base
        /// height so a whole-town re-scale carries it — never a second hardcoded number.</summary>
        private static float WidthBandM => StructureFactory.YHeightVariable * CadenceWidthRatio;

        /// <summary>
        /// Slack in metres when checking an ARMED cap (C3). The cap is one float multiply, so a
        /// correctly-capped model reproduces it to float precision; this absorbs bounds
        /// round-tripping only, same rationale as StructureOrientationOracle.HeightToleranceM.
        /// </summary>
        private const float CapToleranceM = 0.05f;

        /// <summary>Below this many measured rows the catalog is not being read. The band no
        /// longer depends on the population (see CadenceWidthRatio), so this is no longer a
        /// statistical minimum — it is an ART-OUTAGE detector, and it still FAILS rather than
        /// skips so "nothing resolved" can never read as "nothing wrong".</summary>
        private const int MinMeasuredFamily = 6;

        [Serializable]
        private sealed class StructuresFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        /// <summary>One measured row: what it is called and how wide it actually came out.</summary>
        private struct Sample
        {
            public string Label;
            public float  WidestM;      // max(size.x, size.z) of the FITTED model
            public float  HeightM;
            public float  CapM;         // repo.maxFootprint, 0 = disarmed
        }

        [MenuItem("Defenders/Build/Audit Structure Cadence (footprint)")]
        public static void RunMenu()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason); else Debug.LogError(reason);
        }

        /// <summary>Standalone entry point (run-unity-method). Exits 1 so a batch can judge it.</summary>
        public static void RunAll()
        {
            bool ok = Run(out string reason);
            Debug.Log(reason);
            if (!ok) EditorApplication.Exit(1);
        }

        // =====================================================================
        //  THE SUITE
        // =====================================================================
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();

            // ---- C0: the rule must work in BOTH directions before it judges anything ----
            SelfTest(failures, log);

            // ---- C1: the two shipped copies of the catalog -------------------
            CheckCopiesIdentical(failures, log);

            // ---- parse ------------------------------------------------------
            var entries = ParseCatalog(failures);
            if (entries == null) return Verdict(failures, log, 0, out reason);

            var addressToPath = BuildAddressMap();
            if (addressToPath == null)
            {
                failures.Add("no AddressableAssetSettings object — structure art lives in the remote " +
                             "Structure_Art group (Assets/StructureContent), so nothing resolves and no " +
                             "footprint can be measured. This suite cannot pass without geometry.");
                return Verdict(failures, log, 0, out reason);
            }

            // ---- measure every base visual through the shipped pipeline ------
            var samples = new List<Sample>();
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (string.IsNullOrEmpty(e.visualPrefabPath)) continue;   // meshless row: nothing to measure

                if (!addressToPath.TryGetValue(e.visualPrefabPath, out string assetPath) || string.IsNullOrEmpty(assetPath))
                {
                    failures.Add("'" + e.id + "': address '" + e.visualPrefabPath + "' is NOT registered in any " +
                                 "Addressable group — StructureAssetLoader resolves it via no path, so this " +
                                 "structure renders NOTHING and its size is undefined.");
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    failures.Add("'" + e.id + "': address '" + e.visualPrefabPath + "' points at '" + assetPath +
                                 "', which loads no GameObject (dangling entry — the group keeps the GUID " +
                                 "after the asset moves).");
                    continue;
                }

                if (!TryMeasure(prefab, e, out Vector3 size, out string note))
                {
                    failures.Add("'" + e.id + "' (" + assetPath + "): " + note);
                    continue;
                }

                float cap = e.repo != null ? e.repo.maxFootprint : 0f;
                samples.Add(new Sample
                {
                    Label   = e.id,
                    WidestM = Mathf.Max(size.x, size.z),
                    HeightM = size.y,
                    CapM    = cap,
                });

                // ---- C3: an ARMED cap is obeyed -----------------------------
                if (cap > 0f && Mathf.Max(size.x, size.z) > cap + CapToleranceM)
                {
                    failures.Add("'" + e.id + "' CAP NOT APPLIED: repo.maxFootprint authors " +
                                 cap.ToString("0.##") + " m but the fitted model measures " +
                                 Mathf.Max(size.x, size.z).ToString("0.##") + " m across (" +
                                 size.x.ToString("0.##") + " x " + size.y.ToString("0.##") + " x " +
                                 size.z.ToString("0.##") + "). Either the cap is not being applied in " +
                                 "VisualFactory after the fit, or something re-scales the model afterwards. " +
                                 "The row authored a ceiling and the pipeline ignored it — that is worse " +
                                 "than not having the key, because the catalog now lies.");
                }
            }

            // ---- C4: the production path can actually carry a cap ------------
            CheckProductionPathCarriesCap(entries, failures, log);

            // ---- C5: WO-1224's three shared storage-container rows -----------
            CheckStorageContainerScale(entries, failures, log);

            // ---- C2: the outlier band ---------------------------------------
            if (samples.Count < MinMeasuredFamily)
            {
                failures.Add("only " + samples.Count + " structure model(s) measured (expected at least " +
                             MinMeasuredFamily + "). A 28-row catalog that yields this few measurable " +
                             "buildings is an art outage, not a quiet day — failing rather than skipping " +
                             "so it cannot read as a pass.");
            }
            else
            {
                // The band is FIXED against the town's base height and does NOT depend on this
                // population (WO-1239). The median is still computed and PRINTED — as observability,
                // never as a threshold — so a reader can see the family shift without the shift being
                // able to move the line under anybody's feet.
                float median = Median(samples);
                log.AppendLine("band = " + WidthBandM.ToString("0.00") + " m (" +
                               CadenceWidthRatio.ToString("0.0") + "x the " +
                               StructureFactory.YHeightVariable.ToString("0.0") +
                               " m base fit height, population-independent). POPULATION (reported, not " +
                               "used as a threshold): " + samples.Count + " measured base visual(s), " +
                               "median widest-horizontal-extent " + median.ToString("0.00") + " m.");
                EvaluateOutliers(samples, WidthBandM, failures);

                foreach (var s in samples)
                    log.AppendLine("  " + s.Label.PadRight(24) + " widest " + s.WidestM.ToString("0.00") +
                                   " m, height " + s.HeightM.ToString("0.00") + " m" +
                                   (s.CapM > 0f ? ", cap " + s.CapM.ToString("0.##") + " m" : string.Empty));
            }

            return Verdict(failures, log, samples.Count, out reason);
        }

        // =====================================================================
        //  THE RULE — pure, so it can be shown to go red (C0)
        // =====================================================================
        /// <summary>
        /// Flags every sample wider than <paramref name="bandM"/> metres — an ABSOLUTE ceiling
        /// derived once from the town's base fit height (see <see cref="CadenceWidthRatio"/>),
        /// deliberately NOT from anything about this population.
        /// Pure over its inputs and free of Unity state on purpose: that is what lets
        /// <see cref="SelfTest"/> prove it fails on a known-bad family and passes a clean one.
        /// UPPER BOUND ONLY — see coverage note (1) in the header.
        /// </summary>
        private static void EvaluateOutliers(List<Sample> samples, float bandM, List<string> failures)
        {
            if (samples == null || samples.Count == 0 || bandM <= 0.0001f) return;
            float band = bandM;

            foreach (var s in samples)
            {
                if (s.WidestM <= band) continue;
                failures.Add("'" + s.Label + "' FOOTPRINT OUTLIER: the fitted model is " +
                             s.WidestM.ToString("0.00") + " m across — " +
                             (s.WidestM / StructureFactory.YHeightVariable).ToString("0.0") +
                             "x the " + StructureFactory.YHeightVariable.ToString("0.0") +
                             " m base fit height, over the " + band.ToString("0.00") + " m band (" +
                             CadenceWidthRatio.ToString("0.0") +
                             "x base). THE CAUSE IS ALMOST NEVER heightMul. Fit-to-height is a single-axis " +
                             "promise run as a UNIFORM scale (VisualFactory.Fit: localScale *= target / " +
                             "bounds.size.y), so a model whose FIT-TIME pose is FLAT divides by a tiny " +
                             "number and drags its footprint up with it. FITTED ASPECT (widest : height) = " +
                             (s.WidestM / Mathf.Max(s.HeightM, 0.0001f)).ToString("0.00") + " : 1, at a " +
                             "measured height of " + s.HeightM.ToString("0.00") + " m. \u26a0 THE HEIGHT " +
                             "ALONE IS NOT DIAGNOSTIC and never was: Fit divides by whatever axis is up, " +
                             "so a heightMul-1.0 row measures EXACTLY the base height whether it was " +
                             "posed upright or flat. The ASPECT is the number that separates them — the " +
                             "honest town runs 0.6 : 1 to 1.9 : 1 (wall_stone 1.86, barracks 1.91) and " +
                             "the measured collector_farm pancake was 2.56 : 1. CHECK, IN THIS ORDER: " +
                             "(1) is the model upright at FIT time? its catalog orientation.euler is " +
                             "applied PRE-fit via SkinOptions.LocalRotation, so a wrong euler chooses " +
                             "which axis Fit divides by; (2) if the art really is flat-and-wide and " +
                             "correctly posed, author repo.maxFootprint on the row (a ceiling in metres, " +
                             "default 0 = disarmed) — that is what collector_farm does. \u26a0 (2) IS FOR " +
                             "A FLAT MODEL ONLY. The cap is a UNIFORM scale-down, so on a correctly-posed " +
                             "wide building it just makes the building smaller — the same objection as " +
                             "heightMul, on a different key (WO-1239). DO NOT lower heightMul to fix this " +
                             "either: it shrinks the BUILDING as well as the footprint, which is the " +
                             "'shrunk farm' the owner already rejected in commit 31b41d19.");
            }
        }

        /// <summary>
        /// C0 — the rule is exercised in BOTH directions against synthetic families before it is
        /// trusted on the real one. The clean numbers are the real measured town (forge 2.91,
        /// workshop 2.84, store 4.02, lumbermill 5.09, pet-house 4.32, container 5.83) PLUS the
        /// two widest honest rows in the shipped catalog — wall_stone 7.42 and barracks 7.64
        /// (both measured green in Builds/wo1211-reg.log). Those two are in here deliberately:
        /// without them the clean family's widest row was 5.83 m, so the self-test could not have
        /// noticed a band that reds a CORRECT building — which is exactly what WO-1239 was.
        /// The bad one is the actual defect (collector_farm 14.34). If either direction
        /// misbehaves this suite fails LOUD rather than judging the catalog with a broken rule.
        /// </summary>
        private static void SelfTest(List<string> failures, StringBuilder log)
        {
            var clean = new List<Sample>
            {
                new Sample { Label = "st_forge",      WidestM = 2.91f, HeightM = 4.00f },
                new Sample { Label = "st_workshop",   WidestM = 2.84f, HeightM = 4.00f },
                new Sample { Label = "st_store",      WidestM = 4.02f, HeightM = 4.00f },
                new Sample { Label = "st_lumbermill", WidestM = 5.09f, HeightM = 4.00f },
                new Sample { Label = "st_pethouse",   WidestM = 4.32f, HeightM = 4.00f },
                new Sample { Label = "st_container",  WidestM = 5.83f, HeightM = 4.00f },
                new Sample { Label = "st_wall_stone",  WidestM = 7.42f, HeightM = 4.00f },
                new Sample { Label = "st_barracks",    WidestM = 7.64f, HeightM = 4.00f },
            };

            var cleanFailures = new List<string>();
            EvaluateOutliers(clean, WidthBandM, cleanFailures);
            if (cleanFailures.Count != 0)
            {
                failures.Add("SELF-TEST (clean family) FAILED: the honest measured town — forge 2.91, " +
                             "workshop 2.84, store 4.02, lumbermill 5.09, pet-house 4.32, container 5.83, " +
                             "wall_stone 7.42, barracks 7.64 m — produced " + cleanFailures.Count +
                             " outlier report(s) against the " + WidthBandM.ToString("0.00") + " m band. " +
                             "The band is too tight and would red correct buildings; a gate that reds " +
                             "correct buildings gets itself disabled (WO-1239 — it already did once). " +
                             "First report: " + cleanFailures[0]);
            }

            var dirty = new List<Sample>(clean)
            {
                // The measured defect, verbatim from logs/device/2026-08-20-portal.log.
                new Sample { Label = "st_pancake", WidestM = 14.34f, HeightM = 5.60f },
            };
            var dirtyFailures = new List<string>();
            EvaluateOutliers(dirty, WidthBandM, dirtyFailures);
            bool caught = dirtyFailures.Count == 1 && dirtyFailures[0].Contains("st_pancake");
            if (!caught)
            {
                failures.Add("SELF-TEST (known-bad family) FAILED: the same clean family plus the measured " +
                             "14.34 m pancake produced " + dirtyFailures.Count + " report(s) and " +
                             (dirtyFailures.Count == 0 ? "did NOT name st_pancake" : "named the wrong row") +
                             ". The rule cannot go red on the exact defect it was written for, so nothing " +
                             "it says about the real catalog is evidence of anything.");
            }

            if (failures.Count == 0)
                log.AppendLine("C0 self-test: the clean family (widest honest row 7.64 m) passes the " +
                               WidthBandM.ToString("0.00") + " m band, and the measured 14.34 m pancake " +
                               "is caught (1 report, correctly named) — the rule can go red, and it " +
                               "does not go red on the town as shipped.");
        }

        // =====================================================================
        //  C1 — the two shipped copies
        // =====================================================================
        private static void CheckCopiesIdentical(List<string> failures, StringBuilder log)
        {
            string res = null, str = null;
            try
            {
                res = Path.Combine(Application.dataPath, ResourcesCopy);
                str = Path.Combine(Application.dataPath, StreamingCopy);
            }
            catch (Exception ex)
            {
                failures.Add("could not build the catalog copy paths: " + ex.Message);
                return;
            }

            if (!File.Exists(res)) { failures.Add("missing " + ResourcesCopy + " — Resources wins at load, so this copy IS the game's catalog."); return; }
            if (!File.Exists(str)) { failures.Add("missing " + StreamingCopy + " — this copy is what ships to the device."); return; }

            byte[] a, b;
            try { a = File.ReadAllBytes(res); b = File.ReadAllBytes(str); }
            catch (Exception ex) { failures.Add("could not read the catalog copies: " + ex.Message); return; }

            if (a.Length != b.Length)
            {
                failures.Add("the two catalog copies DIVERGE in length (" + a.Length + " vs " + b.Length +
                             " bytes). Resources wins at load and StreamingAssets ships to the device, so " +
                             "the town measured in the editor is not the town the player gets. Edit BOTH.");
                return;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == b[i]) continue;
                failures.Add("the two catalog copies DIVERGE at byte " + i + " (same length, different " +
                             "content). Resources wins at load and StreamingAssets ships to the device. " +
                             "Edit BOTH copies with the same bytes.");
                return;
            }
            log.AppendLine("C1 catalog copies byte-identical (" + a.Length + " bytes).");
        }

        // =====================================================================
        //  C4 — the catalog key has a consumer
        // =====================================================================
        /// <summary>
        /// Asserts SkinOptions exposes a public float MaxFootprint and that
        /// StructureFactory.OptsFor populates it from repo.maxFootprint for an armed row.
        /// <para>REFLECTION IS DELIBERATE HERE and is not the §10 "bridge script" pattern: this
        /// file may not edit VisualFactory/StructureFactory on its lane, and a hard compile
        /// reference would make the suite un-compilable in the window before the consumer lands
        /// — i.e. it would be silently absent exactly when it is needed. Reflection lets the gate
        /// go RED (visible) instead of missing (invisible).</para>
        /// </summary>
        private static void CheckProductionPathCarriesCap(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            FieldInfo f = typeof(SkinOptions).GetField("MaxFootprint", BindingFlags.Public | BindingFlags.Instance);
            if (f == null || f.FieldType != typeof(float))
            {
                failures.Add("SkinOptions has no public float MaxFootprint — the catalog can author " +
                             "repo.maxFootprint but NOTHING READS IT, so every armed row is a silent no-op " +
                             "and this suite's C3 would pass on rows that render at full size. Apply the " +
                             "VisualFactory/StructureFactory patch that carries the cap (cap AFTER the " +
                             "height fit, uniform, scale-down-only) before landing catalog data that " +
                             "depends on it.");
                return;
            }

            CatalogEntry armed = null;
            foreach (var e in entries)
            {
                if (e != null && e.repo != null && e.repo.maxFootprint > 0f) { armed = e; break; }
            }
            if (armed == null)
            {
                // hollow-pass-ok: this is ONE of four checks, not the suite's verdict — C0/C1/C2 have
                // already asserted by the time we get here, and this branch arms itself automatically
                // the moment any row authors a cap.
                log.AppendLine("C4: SkinOptions.MaxFootprint exists; no row authors a cap today, so the " +
                               "wiring assert has nothing to exercise (this is a real gap the moment a row " +
                               "arms one, and it arms itself automatically when that happens).");
                return;
            }

            object opts;
            try { opts = StructureFactory.OptsFor(armed); }
            catch (Exception ex)
            {
                failures.Add("StructureFactory.OptsFor threw on the armed row '" + armed.id + "': " + ex.Message);
                return;
            }

            float carried = (float)f.GetValue(opts);
            if (Mathf.Abs(carried - armed.repo.maxFootprint) > 0.0001f)
            {
                failures.Add("StructureFactory.OptsFor does NOT carry the cap: row '" + armed.id +
                             "' authors repo.maxFootprint=" + armed.repo.maxFootprint.ToString("0.###") +
                             " but SkinOptions.MaxFootprint came back " + carried.ToString("0.###") + ". " +
                             "OptsFor is the ONE shared options builder — Create, ReskinForLevel, the " +
                             "placement GHOST and MeasureUprightFootprintXZ all go through it — so a cap " +
                             "that does not land there reaches none of them, and the ghost would disagree " +
                             "with the placed structure again (the WO-928 defect, on a new axis).");
                return;
            }
            log.AppendLine("C4: SkinOptions.MaxFootprint exists and OptsFor carries " +
                           carried.ToString("0.##") + " m for '" + armed.id + "'.");
        }

        // =====================================================================
        //  MEASUREMENT — replays the CURRENT VisualFactory.Skin pipeline
        // =====================================================================
        /// <summary>
        /// Instantiates <paramref name="prefab"/> and reproduces, step for step, what the shipped
        /// pipeline does to it, then returns the final WORLD bounds size.
        /// <para>Order matters and is read at source (VisualFactory.Skin): LocalRotation FIRST
        /// (opts.LocalRotation wins, else identity unless PreservePrefabRotation), THEN Fit, THEN
        /// the cap, THEN SeatOnGround (translation only, never affects size). Note this differs
        /// from StructureOrientationOracle.TryMeasure, which applies the catalog euler AFTER the
        /// fit under a comment saying "LocalRotation is never set by OptsFor (structures)" — that
        /// stopped being true with the GROK_BRIEF change of 2026-08-19, and the difference is
        /// precisely what decides which axis the fit divides by. Do not copy that order here.</para>
        /// <para>Like that oracle, this deliberately does NOT call VisualFactory.Skin: that path
        /// goes through StructureAssetLoader -> Addressables, whose editor behaviour depends on the
        /// play-mode script, and a gate that can silently resolve nothing is a hollow pass. The
        /// fit target and rotation policy still come from the real StructureFactory.OptsFor so no
        /// formula is re-typed here.</para>
        /// </summary>
        private static void CheckStorageContainerScale(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            string[] ids = { "lumberyard", "foundry", "silo" };
            const float expected = 0.5f;
            int matched = 0;

            foreach (string id in ids)
            {
                CatalogEntry entry = entries.Find(e => e != null && e.id == id);
                if (entry == null)
                {
                    failures.Add("[storage-container-scale] structures-catalog.json is missing required row '" +
                                 id + "'. WO-1224 applies to the complete three-container family.");
                    continue;
                }
                if (entry.repo == null)
                {
                    failures.Add("[storage-container-scale] '" + id + "' has no repo block, so it cannot " +
                                 "author the WO-1224 height dial.");
                    continue;
                }
                if (Mathf.Abs(entry.repo.heightMul - expected) > 0.0001f)
                {
                    failures.Add("[storage-container-scale] '" + id + "' heightMul=" +
                                 entry.repo.heightMul.ToString("0.###") + "; expected 0.5. The three " +
                                 "GenericContainer rows move together so their apparent scale cannot drift.");
                    continue;
                }
                matched++;
            }

            if (matched == ids.Length)
                log.AppendLine("C5 [storage-container-scale]: lumberyard/foundry/silo all author heightMul 0.5.");
        }

        private static bool TryMeasure(GameObject prefab, CatalogEntry entry, out Vector3 size, out string note)
        {
            size = Vector3.zero;
            note = null;

            SkinOptions opts;
            try { opts = StructureFactory.OptsFor(entry); }
            catch (Exception ex) { note = "StructureFactory.OptsFor threw: " + ex.Message; return false; }

            float target = opts.FitHeight;
            if (target <= 0f)
            {
                note = "OptsFor produced a non-positive fit height (" + target.ToString("0.###") +
                       ") — nothing to measure against.";
                return false;
            }

            var host = new GameObject("StructureCadenceProbe");
            host.hideFlags = HideFlags.HideAndDontSave;
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject go = null;
            try
            {
                go = Object.Instantiate(prefab, host.transform);
                go.transform.localPosition = Vector3.zero;

                // VisualFactory.Skin — clone-root policy, BEFORE the fit.
                if (opts.LocalRotation.HasValue) go.transform.localRotation = opts.LocalRotation.Value;
                else if (!opts.PreservePrefabRotation) go.transform.localRotation = Quaternion.identity;

                // VisualFactory.Fit, `largest:false` arm: localScale *= target / bounds.size.y
                if (!TryActiveWorldBounds(go, out Bounds pre))
                {
                    note = "no ACTIVE renderers with measurable bounds — VerifyRenders would destroy this " +
                           "instance and the structure would fall back to nothing.";
                    return false;
                }
                if (pre.size.y < 0.0001f)
                {
                    note = "degenerate pre-fit Y extent (" + pre.size.y.ToString("0.#####") + " m) — Fit " +
                           "would early-return and leave the model at import scale.";
                    return false;
                }
                go.transform.localScale *= target / pre.size.y;

                // The FOOTPRINT CAP, applied AFTER the fit, scale-down only, uniform.
                float cap = entry.repo != null ? entry.repo.maxFootprint : 0f;
                if (cap > 0f && TryActiveWorldBounds(go, out Bounds fitted))
                {
                    float widest = Mathf.Max(fitted.size.x, fitted.size.z);
                    if (widest > cap && widest > 0.0001f)
                        go.transform.localScale *= cap / widest;
                }

                // StructureFactory.Create — post-skin orientation SCALE only. The euler is already
                // in LocalRotation above (pre-fit); re-applying it here would tip the model twice.
                if (entry.orientation != null && entry.orientation.manual && entry.orientation.HasScale)
                    go.transform.localScale = Vector3.Scale(go.transform.localScale, entry.orientation.EffectiveScale);

                if (!TryActiveWorldBounds(go, out Bounds post))
                {
                    note = "bounds became unmeasurable after the fit.";
                    return false;
                }
                size = post.size;
                return true;
            }
            catch (Exception ex)
            {
                note = "measurement threw: " + ex.Message;
                return false;
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>Mirror of VisualFactory.TryBounds: ACTIVE renderers only, world AABB.</summary>
        private static bool TryActiveWorldBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return false;
            bounds = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
            return true;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        /// <summary>Median widest-horizontal-extent. Median, not mean, so one 14 m outlier
        /// cannot drag the very statistic that is supposed to catch it.</summary>
        private static float Median(List<Sample> samples)
        {
            if (samples == null || samples.Count == 0) return 0f;
            var widths = new List<float>(samples.Count);
            foreach (var s in samples) widths.Add(s.WidestM);
            widths.Sort();
            int n = widths.Count;
            return (n % 2 == 1) ? widths[n / 2] : 0.5f * (widths[n / 2 - 1] + widths[n / 2]);
        }

        private static List<CatalogEntry> ParseCatalog(List<string> failures)
        {
            string json = CanonicalJson.Read(CatalogRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add(CatalogRelPath + " unreadable (CanonicalJson.Read returned empty) — no rows to " +
                             "measure, so nothing can be asserted.");
                return null;
            }

            StructuresFile file;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = { new StringEnumConverter() },
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                file = JsonConvert.DeserializeObject<StructuresFile>(json, settings);
            }
            catch (Exception ex)
            {
                failures.Add("structures-catalog.json failed to parse: " + ex.Message);
                return null;
            }

            if (file == null || file.Entries == null || file.Entries.Count == 0)
            {
                failures.Add("structures-catalog.json deserialized to 0 CatalogEntry objects (mapping break " +
                             "or empty 'entries').");
                return null;
            }
            return file.Entries;
        }

        /// <summary>Every authored Addressable address -> the asset path its GUID resolves to.</summary>
        private static Dictionary<string, string> BuildAddressMap()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return null;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.address)) continue;
                    map[entry.address] = AssetDatabase.GUIDToAssetPath(entry.guid);
                }
            }
            return map;
        }

        private static bool Verdict(List<string> failures, StringBuilder log, int measured, out string reason)
        {
            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(MarkerFail).Append(": ").Append(failures.Count).Append(" issue(s):");
                foreach (var f in failures) sb.Append("\n  - ").Append(f);
                if (log.Length > 0) sb.Append("\n").Append(log);
                reason = sb.ToString();
                return false;
            }

            var ok = new StringBuilder(MarkerOk);
            ok.Append(" — ").Append(measured)
              .Append(" structure base visual(s) measured through the shipped fit pipeline (the ")
              .Append("population size is stated on purpose: this band no longer depends on it, and ")
              .Append("the WO-1239 defect was a threshold that silently did); none is wider ")
              .Append("than ").Append(WidthBandM.ToString("0.00")).Append(" m (")
              .Append(CadenceWidthRatio.ToString("0.0"))
              .Append("x the base fit height), every armed repo.maxFootprint is obeyed to within ")
              .Append(CapToleranceM.ToString("0.00"))
              .Append(" m, the rule was shown to catch the measured 14.34 m defect, and the two catalog ")
              .Append("copies are byte-identical. NOT COVERED: tier models, the HubStructureVisualInjector ")
              .Append("path, RealmStore, and the small side of the band — see the header coverage notes.\n");
            ok.Append(log);
            reason = ok.ToString();
            return true;
        }
    }
}
```

## Catalog loading context

### `Assets/_Modules/Core/Data/CanonicalJson.cs` (complete file)
SHA-256 `b7772fd06b1445f5d45fd0eade0a38738c310bd91e3c06bf124c62398e5a32e1`

```csharp
// =============================================================================
// CanonicalJson — single WebGL-safe loader for the canonical JSON catalogs.
// -----------------------------------------------------------------------------
// WebGL has NO filesystem, so File.ReadAllText(Application.streamingAssetsPath)
// THROWS in a browser build — which is why the abilities/gear/etc. catalogs came
// up empty in WebGL ("the build loads but combat won't play": no abilities ->
// can't cast, no gear -> can't equip).
//
// Every catalog routes its read through here. It loads Resources.Load<TextAsset>
// FIRST (synchronous on EVERY platform INCLUDING WebGL) and falls back to a
// desktop StreamingAssets File.ReadAllText only when a Resources copy is absent.
//
// The canonical JSON therefore lives in BOTH:
//   - Assets/Resources/Data/Canonical/*.json    (WebGL-safe copy, Resources.Load)
//   - Assets/StreamingAssets/Data/Canonical/*.json (desktop fallback + source)
// Keep them in sync; Resources wins at load time. Small text files are exactly
// what Resources is good for — the old "no Resources.Load" rule targeted large
// assets (models/textures), not a few KB of catalog JSON.
// =============================================================================

namespace DeNelle.Core
{
    /// <summary>WebGL-safe reader for canonical catalog JSON (Resources first, StreamingAssets fallback).
    ///
    /// Source-agnostic seam (Tier-0 of docs/DATA_ARCHITECTURE_DECISION_2026-06-27.md): the actual
    /// read is delegated to a swappable <see cref="ICatalogSource"/> (<see cref="Source"/>), which
    /// DEFAULTS to <see cref="LocalJsonCatalogSource"/> — exactly the original local-JSON behavior
    /// (Resources first, StreamingAssets fallback). A future remote/DB source is a one-line swap:
    ///   <c>CanonicalJson.Source = new MyRemoteCatalogSource();</c>
    /// No call site changes — every caller still calls <see cref="Read"/> with the same signature.</summary>
    public static class CanonicalJson
    {
        /// <summary>The active catalog source. Defaults to local JSON (Resources first,
        /// StreamingAssets fallback). Assign a different <see cref="ICatalogSource"/> to back the
        /// same catalogs with a remote/DB source without touching any call site.</summary>
        public static ICatalogSource Source { get; set; } = new LocalJsonCatalogSource();

        /// <summary>Reads canonical JSON text. <paramref name="relativePath"/> is the
        /// StreamingAssets-relative path, e.g. "Data/Canonical/abilities.json".
        /// Returns null if the active <see cref="Source"/> cannot resolve it.</summary>
        public static string Read(string relativePath)
        {
            // Defensive: a caller could null out Source; fall back to a fresh local source
            // so catalog loads never silently break (no silent failure, §12).
            var src = Source;
            if (src == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("CanonJson",
                    $"Source was null — rebuilt LocalJsonCatalogSource for '{relativePath}'.");
                src = Source = new LocalJsonCatalogSource();
            }
            var text = src.Read(relativePath);
            if (string.IsNullOrEmpty(text))
                DeNelle.Core.Diagnostics.FlowTrace.Warn("CanonJson",
                    $"Read('{relativePath}') via {src.GetType().Name} returned EMPTY (no Resources dual-copy + no StreamingAssets file).");
            else
                DeNelle.Core.Diagnostics.FlowTrace.Step("CanonJson",
                    $"Read('{relativePath}') via {src.GetType().Name} -> {text.Length} chars.");
            return text;
        }
    }
}
```

### `Assets/_Modules/Core/Data/ICatalogSource.cs` (complete file)
SHA-256 `b3dea17331a75baeea67b2f7e8a552f25200c90d02f677b0bda9926e375efb53`

```csharp
// =============================================================================
// ICatalogSource — source-agnostic catalog seam (Tier-0 of
// docs/DATA_ARCHITECTURE_DECISION_2026-06-27.md).
// -----------------------------------------------------------------------------
// Every canonical data file (gear/weapons/armor/accessories/talents/abilities/
// quests/...) is loaded through CanonicalJson. This interface abstracts WHERE
// that raw JSON text comes from so a future remote/DB source is a one-line swap
// (CanonicalJson.Source = new MyRemoteCatalogSource();) with NO call-site churn.
//
// The contract MATCHES CanonicalJson.Read exactly: given a StreamingAssets-
// relative logical path (e.g. "Data/Canonical/abilities.json") return the raw
// JSON text, or null when the catalog cannot be resolved. Implementations must
// be synchronous (callers expect a string back immediately) and must never
// throw — resolution failures return null (and self-report via FlowTrace/Guard).
// =============================================================================

namespace DeNelle.Core
{
    /// <summary>Source-agnostic provider of canonical catalog JSON text.
    /// Default implementation is <see cref="LocalJsonCatalogSource"/> (local
    /// JSON from Resources first, StreamingAssets fallback). Swap the active
    /// source via <see cref="CanonicalJson.Source"/> to back the same catalogs
    /// with a remote/DB source without touching any call site.</summary>
    public interface ICatalogSource
    {
        /// <summary>Returns the raw JSON text for the logical catalog at
        /// <paramref name="relativePath"/> (StreamingAssets-relative, e.g.
        /// "Data/Canonical/abilities.json"), or null if it cannot be resolved.</summary>
        string Read(string relativePath);
    }
}
```

### `Assets/_Modules/Core/Data/LocalJsonCatalogSource.cs` (complete file)
SHA-256 `c103d5fe216baccacf7801dc66eb7f31458fc0b487b9f3f20ec0e71bbf0ba6af`

```csharp
// =============================================================================
// LocalJsonCatalogSource — the default ICatalogSource (local on-disk JSON).
// -----------------------------------------------------------------------------
// This is BYTE-IDENTICAL in behavior to the original CanonicalJson.Read: it
// loads Resources.Load<TextAsset> FIRST (synchronous on EVERY platform INCLUDING
// WebGL — WebGL has no filesystem) and falls back to a desktop StreamingAssets
// File.ReadAllText only when a Resources copy is absent. Resources wins.
//
// The canonical JSON therefore lives in BOTH:
//   - Assets/Resources/Data/Canonical/*.json    (WebGL-safe copy, Resources.Load)
//   - Assets/StreamingAssets/Data/Canonical/*.json (desktop fallback + source)
// Keep them in sync; Resources wins at load time.
// =============================================================================

using System.IO;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Core
{
    /// <summary>Default <see cref="ICatalogSource"/>: local JSON, Resources first,
    /// StreamingAssets fallback. Identical precedence/behavior to the original
    /// CanonicalJson loader.</summary>
    public sealed class LocalJsonCatalogSource : ICatalogSource
    {
        /// <inheritdoc/>
        public string Read(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

            // 1) Resources.Load<TextAsset> — works on ALL platforms incl. WebGL.
            //    Resources paths omit the file extension.
            string resPath = relativePath.EndsWith(".json")
                ? relativePath.Substring(0, relativePath.Length - 5)
                : relativePath;
            var ta = Resources.Load<TextAsset>(resPath);
            if (ta != null && !string.IsNullOrEmpty(ta.text))
            {
                FlowTrace.Step("Catalog", $"resolve '{relativePath}' <- Resources ({ta.text.Length} chars)");
                return ta.text;
            }

            // 2) Desktop / Editor fallback — real filesystem under StreamingAssets.
            //    On WebGL this is never needed (the Resources copy is the source of
            //    truth there); Guard keeps it safe even if it is reached and reports
            //    any real desktop read failure (locked/permission/corrupt file)
            //    instead of silently producing an empty catalog (§12 no-silent-failure).
            string text = Guard.Try("Catalog", $"StreamingAssets read of '{relativePath}'", () =>
            {
                string full = Path.Combine(Application.streamingAssetsPath, relativePath);
                return File.Exists(full) ? File.ReadAllText(full) : null;
            }, fallback: null);

            if (!string.IsNullOrEmpty(text))
            {
                FlowTrace.Step("Catalog", $"resolve '{relativePath}' <- StreamingAssets ({text.Length} chars)");
                return text;
            }

            FlowTrace.Warn("Catalog", $"resolve '{relativePath}' FAILED (no Resources copy, no StreamingAssets file)");
            return null;
        }
    }
}
```

## Local execution seam (for CLI, not execution by DeepSeek)

The embedded oracle offers `DeNelle.Editor.StructureCadenceRegression.Run(out string reason)` and zero-argument batch `RunAll()`. Its own MarkerOk/MarkerFail constants identify success/failure. Local CLI must schedule Unity serially with other work and collect actual markers. No Unity invocation occurred while building this audit packet.

Deliver only the final audit, conditional patch or exact missing-measurement request, and an UNRUN validation table.
