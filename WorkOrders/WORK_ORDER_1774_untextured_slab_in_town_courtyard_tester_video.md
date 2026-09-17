# WORK ORDER 1774 — The beige untextured "slab" in the tester's town is a player-built WALL rendering with no albedo

**Status:** NEEDS DATA
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** Structure art / material degrade — `DeNelle.Core` (TripoMaterialFixer, StructureAssetLoader) + `DeNelle.Village/Catalog` (StructureFactory) + the R2 content chain (CLAUDE.md §16)
**Lane disjointness:** touches NO `.unity`, NO wave/balance code. File-disjoint from WO-1773 (balance lane).
**Evidence:** `logs/device/owner-video/DefenderDemoRun.mp4`; frames at `logs/device/owner-video/frames_1s/` and `frames/`; crops at `logs/device/owner-video/crops/`.

> ⚠ **BUILD UNDER TEST IS UNKNOWN — this is line one of the ticket, not a footnote.** The tester's
> `Application.version` has never been captured; it is still open owner item 7 in
> `docs/handoffs/SESSION_HANDOVER_2026-09-15_play_track_and_wall_fork.md:61`.
> **WO-1758 (arena boundary pillars) is `IMPLEMENTED, NOT YET GATED` and WO-1747 is `READY` — if
> either postdates his binary, this may already be fixed and awaiting his update.** That possibility
> must be eliminated before any code is written. **Ask for the version string first.**

---

## 1. WHAT THE OBJECT IS — named, with a chain of exact matches

The lead's brief described "a large flat beige/untextured rectangular slab standing in the town
courtyard". **It is not a slab and it is not a mystery. It is the tester's own player-built perimeter
wall, rendering with no texture.**

### 1a. It is a wall — five independent confirmations

1. **Crenellations.** `crops/slab_22.jpg` (t = 22 s) shows the beige surface carrying **battlements along its top edge** and a **gap where a section is missing**. That is wall geometry, not a plane, a billboard or a placeholder cube. The adjacent wooden buildings and the tower in the same frame are **correctly textured** — the failure is specific to this object.
2. **The game calls it a Wall Section.** `crops/wallinspect.jpg` (t ≈ 131.5 s): *"Wall Sect… / Health: 76%… Damage: 24% / Repair cost… wood 20 / Ready to repair"*. That string is `Assets/_Modules/Village/Walls/WallRepairStrings.cs:27` — `public const string WallSegmentName = "Wall Section";` (`grep -rn "Wall Section" Assets --include=*.cs` returns **that one line only**).
3. **Two of them died in the wave.** `frames_1s/s_134.jpg` (t ≈ 133 s), the WAVE 176 CLEARED panel: **"Wall Section - DESTROYED   Rebuild Wood 80"** ×2, *"Showing 4 of 8 results"*.
4. **Damage numbers land on it** (`frames/f_002.jpg`, a "40" over the surface) — so it has a **live collider** and is an `IDamageableStructure`. `WallSegment.cs:58` implements it.
5. **The repair/rebuild prices reconcile to the digit** — see §1b.

### 1b. The walls the wave destroyed are the WOOD tier — the arithmetic proves that much

⚠ **Scope this claim carefully.** The cost arithmetic below identifies the **damaged and destroyed**
walls in the wave report. It does **NOT** pin which catalog row the *visible beige crenellated surface*
belongs to — the report lists "4 of 8 results", so at least three distinct wall objects are in play, and
`Structure_Art.asset` carries **six** wall addressables (`Structures/Wall_Medieval_Wood` `:136`,
`Wall_Medieval_Stone` `:161`, `Torche_Wall` `:75`, and `Synty_Tower_Castle_Wall_S/M/L` `:141`/`:85`/`:171`).

✅ **The "wooden palisade vs crenellated masonry" worry is RESOLVED — the wood-tier wall IS a castle
wall.** `Structures/Wall_Medieval_Wood` (guid `8e38bac6a566a9c4ab37974ab7713c38`) is a 92-line **prefab
VARIANT** carrying only transform modifications, whose base is
**`Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/SM_Bld_Castle_Hoarding_Wood_Wall_01.prefab`**
(guid `fab6d140a2b06bc499ea3277ae476342`) — a Synty *Castle Hoarding Wood Wall*. That silhouette is
consistent with the battlements in `crops/slab_22.jpg`. **It remains unproven that the visible surface
is this row rather than one of the other five**, but the tier no longer argues against it.

`Assets/Resources/Data/Canonical/structures-catalog.json:311-322`:
```
"type": "Wall", "kind": "Cell",
"visualPrefabPath": "Structures/Wall_Medieval_Wood",
"repo": { "behaviorId": "WallSegment", "buildCost": 160,
          "cost": { "wood": 80, "food": 0, "iron": 0, "crystals": 0 }, "maxLevel": 2, … }
```

| Observed on the tester's screen | Catalog value | Match |
|---|---|---|
| "Rebuild **Wood 80**" (`s_134.jpg`) | `cost.wood = 80` — and `WaveDamageReport`'s rule is *"destroyed = full build cost = the REBUILD price"* (`WaveDamageReport.cs:17-24`) | **exact** |
| "Damage: **24%** … Repair cost: **wood 20**" (`wallinspect.jpg`) | damage fraction × build cost = 0.24 × 80 = **19.2** (`WaveDamageReport.cs:110,:176` → `repair.CostForStructure(structure, frac)`) | **consistent** — ⚠ `round(19.2) = 19`, so the display implies a `ceil` or a slightly different fraction; the displayed "24%" is itself rounded and **`CostForStructure`'s rounding rule was not read.** Do not call this exact |

The stone tier (`:368-382`, `Structures/Wall_Medieval_Stone`) costs wood 120 + iron 240 and would
produce neither number. **The Wood 80 rebuild figure is an exact match and carries the identification on
its own; the repair figure corroborates it.**

### 1c. Why it is not in the scene, and why that matters

`grep` over `Assets/Scenes/Main_Castle_Overworld.unity` returns **zero** occurrences of `WallSegment`,
**zero** of the three wall-FBX guids and **zero** of the three wall-material guids. That is not a
defect — `Assets/Editor/CastleHubBuilder.cs:200-208` records the owner ruling (WO-1711 B) that **the
home castle has no builder-placed perimeter walls**, and `BuildInnerWallRing` is destroy-only (`:750`).

**And the authored perimeter builder is DEAD in the hub.** `WallLayout`'s only instantiating consumer is
`VillageController` (`:91 segment.Configure(data, WallHeight)`, `:135`), and that component's guid
`dec308f9c8fa10943a15fdd995af76fb` appears in **zero scenes and zero prefabs** — grepped 2026-09-16
across `Assets/Scenes/` and every `*.prefab`. (`StructureFactory.cs:1220-1224` says the same thing in
passing: *"The opener was attached only by VillageController, whose guid is in no scene or prefab"*.)

**The tester's walls are therefore PLAYER-BUILT, restored from his save and spawned at runtime** — the
only remaining producer of a `WallSegment`:
`Assets/_Modules/Village/BuildMode/BaseLayoutLoader.cs:571` → `Assets/_Modules/Village/Catalog/StructureFactory.cs:1211`
```
// WallSegment is authored with id/index/length by the builder, not
// from RepoProps stats — attach it bare; the caller configures it.
case "WallSegment":
    root.AddComponent<WallSegment>();
```
**This puts the object squarely on the remote-art path (CLAUDE.md §16), not the scene-bake path** — and
that is the single most important structural fact in this ticket.

### 1d. ⛔ The failure is ALBEDO, not a missing bundle — and this is a load-bearing distinction

`Assets/Resources/Structures` **does not exist** (`ls` → "No such file or directory"), exactly as
CLAUDE.md §16 states. `Structures/Wall_Medieval_Wood` is an Addressable —
`Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset:136 m_Address: Structures/Wall_Medieval_Wood`
— served from R2 with **no local fallback**. `StructureAssetLoader.cs:56-57` says so verbatim: *"There
is NO Resources fallback tier for structures any more: the CDN migration deleted
`Assets/Resources/Structures`."*

**But a missing bundle does NOT look like this.** The placeholder is
`StructureFactory.BuildPendingArtProxy` (`:277-293`):
- a `PrimitiveType.Cube` at scale `(h×0.45, h, h×0.45)` — **a tall thin box, not a crenellated wall**,
- colour `new Color(0.72f, 0.28f, 0.08f)` — **orange-brown, not beige**,
- and `Object.Destroy(primitiveCollider)` — **the collider is deliberately destroyed** (`:271-274`: *"It intentionally owns no gameplay collider"*).

**The tester's object has crenellations, takes damage numbers, is inspectable, is repairable and was
destroyed by the wave. The real mesh loaded. Only its texture did not.**

The beige is an **exact source match** for the designed no-texture degrade:
`Assets/_Modules/Core/TripoMaterialFixer.cs:131` —
`[SerializeField] private Color _missTint = new Color(0.60f, 0.58f, 0.54f, 1f);` — applied when
`tex == null` at `:385`, and set on every structure path (`StructureFactory.cs:209,:331,:614`;
`VisualFactory.cs:675`). **(0.60, 0.58, 0.54) is beige/stone.**

### 1e. Correction to the brief, on the record

- ⛔ **"Persists across the run" is not right, and "an Ogre flying over it" is a misread.** The wall is out of frame at t ≈ 93 s (`frames_1s/s_094.jpg`) simply because the hero turned — it is a perimeter wall, not a courtyard object. In `frames/f_004.jpg` (t ≈ 60 s) the winged shape is **in front of / above** the wall as seen from the camera, not flying over it. ⚠ **No enemy in `enemies.json` is declared flying** (`grep -n "flying"` → zero matches) and `Enemy._isFlying` is a **targeting-layer flag with no movement effect** (its only usages are `Enemy.cs:148,:610,:614,:737`). Treat "flying over" as unproven camera perspective.
- ⛔ **The small dark chip carrying a white "2" / "3"** (`f_002.jpg`, `s_113.jpg`, `s_094.jpg`) sits *on* the wall and is **UNIDENTIFIED**. No structure level-badge emitter was located. It is plausibly a wall-tier badge (`WallTierData` authors levels 0..3), but that is **NOT PROVEN** and nothing in this ticket depends on it.

---

## 2. Why nothing reported it — the instrumentation gap is the real finding

**This defect is structurally invisible to every detector the project has.** That, not the beige wall,
is what should be fixed first.

1. **`MagentaGuard` cannot see it, BY DESIGN.** `Assets/Editor/Regression/RaidWallMaterialRegression.cs:17-22`: *"a textureless-but-valid URP/Lit material passes `MagentaGuard.IsBrokenShader` … White-but-valid is structurally invisible to the runtime guard. Nothing on screen says 'broken'. The owner's eyes were the detector, which is exactly what CLAUDE.md sec.14 exists to never rely on."*
2. **⛔ The one diagnostic line that WOULD name it is suppressed to `Warn`, and Warns never reach F8.** `TripoMaterialFixer.cs:546-552` emits `NO ALBEDO on '<obj>' renderer '<r>' slot <i>: material='…' shader='…' tint=(r,g,b) — the URP rebuild took but bound NO base map, so this mesh renders as flat tint.` It is promoted to **`FlowTrace.Fail` only when the tint is pure white (`> 0.95`)** — a **non-white tint stays a `Warn`** (`:553`), and per the WO-1707 lesson recorded at `:534-539`, **a `Warn` never reaches `break-log.jsonl` or the F8 device bridge.** **A wall degrading to the beige `_missTint` therefore produces no capture, on any device, ever.**
3. **`[Flow:StructureAssets]` also filters it out.** `Assets/_Modules/Core/Addressables/DependencyClosureTrace.cs:121` deliberately does **not** report a material whose min channel is `< 0.92` — i.e. **a beige-tinted offender is invisible to that tag too.**
4. **`RaidUntexturedCensus` never runs in town.** `Assets/_Modules/Core/Diagnostics/RaidUntexturedCensus.cs:123` returns immediately unless `HubScenes.IsRaid(scene.name)`, and `:154` re-checks the same gate every deferred pass. `HubScenes.IsHub` (`Assets/_Modules/Core/HubScenes.cs:38`) and `IsRaid` (`:61`) are disjoint, and `Main_Castle_Overworld` is in `HubScenes.Names` (`:25`). **The one instrument that solved this exact class of bug (WO-1757 → WO-1758) is switched off in the scene where the tester hit it.**
5. **The asset-lint oracle that covers this class is RAID-ONLY.** `RaidWallMaterialRegression` (markers `RAID_WALL_MATERIAL_OK` / `_FAIL`) pins four properties of the wall art — but PIN 5 resolves *"through the REAL `Resources.Load` call `RaidBaseGenerator.PlaceSegment` uses"*. **It lints `Assets/Resources/Walls/*`, which is the RAID/editor-builder path. The town's walls come from the Addressable `Structures/Wall_Medieval_Wood` (§1c/§1d) and are not covered by any pin.**

**Verified at source on 2026-09-16, so the raid-side fix is NOT the gap:** all three `Assets/Resources/Walls/Materials/{wood,iron,steel}_wall.mat` exist and carry a bound `_BaseMap`; all three `.fbx.meta` carry a correct `externalObjects` Material remap onto the matching `.mat` guid (`wood → 73141e58…`, `iron → 79158448…`, `steel → 89ec89c8…`). **WO-838's four pins are green. This is a different, uncovered path.**

### 1f. The wall's art chain, followed to the texture — and one theory REFUTED on the way

Traced from disk 2026-09-16, guid by guid:

```
structures-catalog.json:313   visualPrefabPath "Structures/Wall_Medieval_Wood"
  -> Structure_Art.asset:136  guid 8e38bac6a566a9c4ab37974ab7713c38
  -> Assets/StructureContent/Synty/Wall_Medieval_Wood.prefab        (VARIANT, 92 lines, transform mods only)
  -> Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/SM_Bld_Castle_Hoarding_Wood_Wall_01.prefab
  -> Assets/Synty/PolygonFantasyKingdom/Materials/Alts/PolygonFantasyKingdom_Mat_01_A.mat
       shader  = Assets/Synty/PolygonGeneric/Shaders/Generic_Basic.shadergraph
       albedo  = _Albedo_Map  -> guid c83598997a5b4f1797e66e82b1a8fb7a   (BOUND on disk)
       also    = _Emission_Map, _Normal_Map bound;  NO _BaseMap, NO _MainTex
```

**Two things follow, and they point in opposite directions — both are recorded honestly.**

⛔ **REFUTED: "the fixer looks for `_BaseMap` and this material only declares `_Albedo_Map`, so it
repaints a good wall beige."** That is a tempting theory and it is **wrong**. `TripoMaterialFixer.cs:345`
reads `tex = DependencyClosureTrace.GetAlbedo(src)`, and `DependencyClosureTrace.cs:204-208` lists
`AlbedoTokens` beginning with **`"albedo"`**, so `_Albedo_Map` is recognised. The fixer's own comment at
`:342-345` says this handling exists *precisely* because asking only `_MainTex`/`_BaseMap` produced the
flat-white Default Town LightSkin (Seeker 365875, 2026-09-11). **Do not file this as the cause.**

⚠ **STRENGTHENED: the FORCED-albedo path is a different code path and it does only know the two
names.** `HubStructureVisualInjector.cs:743-751` fails with *"bound onto ZERO of N material slot(s) — no
material declares `_BaseMap` or `_MainTex` (first shader '<x>') … The structure will render colorless.
This is a SHADER PROPERTY mismatch OR the baked-twin placeholder was still standing in"* — and that
sentence is **literally true of this material**, whose shader is the very `Synty/Generic_Basic` the
injector names. **This promotes §3 row 2 above row 1.**

⚠ **AND THE WHOLE CHAIN BELOW THE VARIANT IS UNTRACKED.** `.gitignore:732` ignores `/Assets/Synty/`, and
`git ls-files --error-unmatch` reports the base prefab **NOT TRACKED**. So the base prefab, the material,
the shader graph and the albedo texture **all live outside version control**, exactly like the
polyperfect pack (CLAUDE.md §4). That is the project's normal arrangement and is **not by itself a
defect** — but it means this wall's art is **build-machine-dependent**, and a content build made where
the Synty pack was absent or stale would produce a bundle whose albedo resolves to null at runtime,
which lands straight on the beige `_missTint`. **This gives §3 row 3 a concrete mechanism it did not
have before.**

---

## 3. Ranked causes — the object is named, the CAUSE is what is unproven

> **⚠ RE-RANKED after §1f.** Row 2 now outranks row 1, and row 3 has a real mechanism.

| # | Cause | Strength | Evidence |
|---|---|---|---|
| **1** | The wall's remote material **resolved but bound no albedo**, degrading to `_missTint` (0.60, 0.58, 0.54) | **STRONG** — colour is an exact source match, and it is the only mechanism that yields *the real mesh, correctly shaped, with a live collider, untextured* | `TripoMaterialFixer.cs:131,:385`; `StructureFactory.cs:209,:331,:614` |
| **2** | The **baked-twin placeholder** was still standing in for the real model when the albedo was forced | MODERATE — device-proven for the "white-town" boots seq 5018/5022 on 2026-09-12 | `Assets/_Modules/Village/HubStructureVisualInjector.cs:726-751` — *"bound onto ZERO of N material slot(s) … The structure will render colorless."* Emits `[Flow:Hub]`. ⚠ Would read **white**, not beige, unless lighting warms it — **unproven** |
| **3** | The wall's **texture dependency never reached R2** (CLAUDE.md §16: bundle names are content-hashed, every content build needs its own push) | MODERATE | §16. ⚠ **A wholesale bundle miss is REFUTED by §1d** — the mesh rendered. This would have to be a *texture-only* dependency miss inside a resident bundle |
| **4** | An FBX-embedded material with `externalObjects: {}` and no `.mat` to edit, i.e. the WO-1747 pattern | **REFUTED for this object** | §1f followed the chain to a **tracked, standalone `.mat`** (`PolygonFantasyKingdom_Mat_01_A.mat`) with its albedo bound on `_Albedo_Map`. There is no embedded-material problem here. The `Resources/Walls` FBX remaps are separately verified correct (§2) |
| — | "The fixer only knows `_BaseMap`/`_MainTex`, so it repaints this Synty material beige" | **REFUTED — do not file it** | §1f. `TripoMaterialFixer.cs:345` delegates to `DependencyClosureTrace.GetAlbedo`, whose `AlbedoTokens` (`DependencyClosureTrace.cs:204-208`) lead with `"albedo"` and therefore match `_Albedo_Map` |
| — | Missing-bundle placeholder cube | **REFUTED** | §1d — orange-brown, collider destroyed, wrong shape |
| — | A courtyard primitive from `CastleHubBuilder` | **REFUTED** | The only visible primitives are `CastleBasePlinth` (`:102-125`, horizontal, grey 0.55/0.55/0.57, under the castle) and `Bridge_Deck_Visual` (`:2161`, horizontal, outside the south gate). `GateExit_*_Nav` (`:1081`) and `CourtyardFloor_Nav` (`:1195`) have `r.enabled = false`. The owner's stray `"Plane".."Plane (3)"` are destroyed every build (`:1598-1620`) |
| — | A billboard / signboard / banner / plaque / quest-board | **REFUTED — none exists** | Grepped `CastleHubBuilder.cs`, `ExteriorTerrainBuilder.cs`, `OwnerCastleLayoutRepair.cs`, `HubStructureVisualInjector.cs`; the only hits are Unity **terrain tree** billboarding (`ExteriorTerrainBuilder.cs:243,:349`) |
| — | `ArenaBoundary_Ring` pillars (the WO-1758 object) | **REFUTED for the hub** | Visually the closest known match (2.6 × 7.0 × 2.6 m untextured pale slab, `M_21_Grey_Light_LPUP`, `_BaseMap=EMPTY`) but `ArenaBoundaryRing` is called only from `RaidBaseGenerator` / `ProceduralSiegeArenaBuilder` — **never from the hub** |
| — | A storefront / `RealmStore` placeholder | **REFUTED** | `Assets/Editor/RealmStorePlacer.cs:22-25` — the storefront is deliberately not in the catalog and **not** an `IDamageableStructure`, so damage numbers could not land on it. And it has no crenellations |

---

## 4. WHAT TO CAPTURE — this is why the status is NEEDS DATA

### 4a. From the tester, before anything else
1. **His `Application.version` line.** Already an open owner item (`SESSION_HANDOVER_2026-09-15…:61`). Without it, no fix can be shown to apply to his build.
2. **Whether the walls still render beige after he updates**, once WO-1758 and WO-1747 have shipped.

### 4b. From a logcat of his session — grep FOUR tags, not one

⚠ **Grep the entry lines, never just a header phrase.** WO-1757 was closed INVALID precisely because a
grep matched only the census header and the lead concluded the output was truncated when it was not.

| Tag | Emitter | The line that names the object |
|---|---|---|
| `[Flow:TripoMatFix]` | `Assets/_Modules/Core/TripoMaterialFixer.cs:546-552` | `NO ALBEDO on '<obj>' renderer '<r>' slot <i>: material='…' shader='…' tint=(r,g,b)` — **⚠ at `Warn` level for a beige tint, so it is in Player.log but NOT in break-log.jsonl** |
| `[Flow:Hub]` | `Assets/_Modules/Village/HubStructureVisualInjector.cs:743-751` | `'<bakedName>': forced albedo '<texPath>' RESOLVED but bound onto ZERO of N material slot(s) … The structure will render colorless.` |
| `[Flow:StructureAssets]` | tag const `StructureAssetLoader.cs:111`; printed by `DependencyClosureTrace.cs:129` | `dep MISS on '<address>': material '<m>' has NO albedo and NO tint …` — **⚠ suppressed for tinted materials (`:121`)** |
| `[Flow:Structure]` | `StructureFactory.cs:244,:296,:305,:330` | `'<id>': skinned visual '<path>' FAILED RENDER VERIFICATION … retaining a visible pending-art proxy` |

**Expected discriminator:** if `[Flow:TripoMatFix] NO ALBEDO on 'Wall_Medieval_Wood…'` appears while
`[Flow:Structure] … pending-art proxy` does **not**, cause #1 is confirmed — the mesh loaded, the
texture did not.

### 4c. The `playtest_break` analytics question

The lead's admin query returned **396 `playtest_break` events across 20 players today**.
**Those events are very unlikely to carry this defect, and §2 says why:** the beige degrade never
raises a `Fail`, and per `TripoMaterialFixer.cs:534-539` a `Warn` does not reach `break-log.jsonl` — the
file the F8 bridge and the analytics pipeline draw from. **A query that comes back empty for this object
proves nothing except that the instrument is muted.** ⚠ **UNPROVEN either way by this lane: the
`playtest_break` payload schema was not inspected.** The cheap check is to query for any event whose
text contains `NO ALBEDO`, `colorless` or `RENDER VERIFICATION` — if there are zero across 396 events
while the tester's own screen shows an untextured wall, that is the §2 gap confirmed from the server
side.

---

## 5. Recommended actions — cheapest and highest-leverage first. **Design-neutral; the owner rules.**

### (A) Arm the census in the hub — one gate, and it names the object on the next run
`RaidUntexturedCensus.RunPass(string sceneName, string why)` (`:163`) is **already public and contains
no scene gate**; only the two call sites gate it. Relaxing `:123` and `:154` from
`HubScenes.IsRaid(...)` to `IsRaid(...) || IsHub(...)` makes it report **biggest-first, with bounds and
world centre** — which is exactly how WO-1758 was solved on its first device run.
⚠ Note its own `FlowTintLuminanceFloor = 0.6f` (`:79`): the beige `_missTint` has channels
0.60/0.58/0.54, so **it may sit right on that floor and be filtered out.** Check the floor in the same
change, or the census will be armed and still silent.

### (B) Promote the non-white `NO ALBEDO` case to `Fail`
`TripoMaterialFixer.cs:553`. Today only pure white is a `Fail`; the **designed** beige degrade is the
one that ships silently. Without this, every future occurrence is again invisible to F8 and analytics.
⚠ **Weigh the noise cost:** the miss-tint is a *deliberate* graceful degrade, so promoting it will
surface every intentional one too. **The owner should rule** between (i) promote unconditionally,
(ii) promote only for renderers above a bounds threshold (a 2-storey wall, not a pebble),
(iii) leave it a `Warn` and rely on (A).

### (C) Extend the wall asset-lint oracle to the Addressable structure path
`RaidWallMaterialRegression`'s five pins cover `Resources/Walls/*` (the raid/editor-builder path) and
**not** `Structures/Wall_Medieval_*` (the Addressable path the player's own town uses). The same four
properties — tracked material, URP/Lit, bound albedo, correct remap — should be pinned for every
`visualPrefabPath` in `structures-catalog.json`.
⛔ **Do NOT write a second broken-shader predicate.** `MagentaGuard.IsBrokenShader` stays the one
authority for the MAGENTA class (`RaidWallMaterialRegression.cs:25-28`). This is a different property
("is the art wired to tracked assets") at a different time (import, not runtime).

### (D) Re-verify the R2 push for the structure content
Per CLAUDE.md §16, bundle names are content-hashed and **every content build needs its own push**.
⛔ Run the one sanctioned path, `tools\r2-ship.ps1` — **never re-inline the push or the verify**, and
**judge by the `R2_PUSH_OK` / `R2_PARITY_OK` markers on a fresh log, never the exit code.**
⚠ This is listed last on purpose: **§1d substantially refutes a wholesale bundle miss**, because the
wall mesh rendered. Do this to close the possibility, not as the leading theory.

### (E) ⚠ Flag to the owner, unresolved and NOT part of this ticket
`Assets/Editor/RealmStorePlacer.cs:10-14` quotes `CastleHubBuilder`'s own comment — *"Jeweler (Gems)
REMOVED from the fixed ring — it was blocking the south door. Do NOT re-add a fixed Jeweler here."* —
yet a `Jeweler` prefab instance **is** saved in `Main_Castle_Overworld.unity` at (−8.00, −0.32, −2.41).
Separately, a `break-log.jsonl` entry at **2026-09-15T18:55:30Z, scene `Main_Castle_Overworld`** reads
`[Flow:TripoMatFix] NO ALBEDO on 'Jeweler' renderer 'Rim' slot 0 … tint=(1.00,1.00,1.00) … this is the
flat-white-structure symptom` (quoted inside `RaidUntexturedCensus.cs:30-33`). **That is a second,
already-captured untextured object in the same scene, and it is WHITE, not beige — so it is a different
instance of the same class, not this ticket's wall.** It needs an owner ruling and probably its own WO.

---

## 6. Files that would change (none yet — status is NEEDS DATA)

| File | Change | Gated on |
|---|---|---|
| `Assets/_Modules/Core/Diagnostics/RaidUntexturedCensus.cs` | `:123`, `:154` scene gate; review `FlowTintLuminanceFloor` `:79` | (A) — safe to do now |
| `Assets/_Modules/Core/TripoMaterialFixer.cs` | `:553` Warn→Fail promotion | (B) — **owner ruling required** |
| `Assets/Editor/Regression/…` (new or extended) | pin albedo for Addressable structure art | (C) |
| — | no art or scene change | until the cause is proven |

## 7. Acceptance criteria

1. The tester's `Application.version` is recorded in this WO, and it is established whether WO-1758 / WO-1747 pre- or post-date it.
2. A capture (device logcat or an armed hub census) contains a line that **names the offending renderer and material by path**, per §4b.
3. The named cause maps to exactly one row of §3, and the WO is re-statused **READY TO IMPLEMENT** with that row as the fix.
4. Whatever is fixed, a **regression pins it** so the next occurrence is caught at gate time, not by a tester's eyes (CLAUDE.md §14).
5. **Screenshot proof** — memory rule `screenshots-are-primary-evidence-for-visual-defects`: FlowTrace shows belief, the screenshot shows what the player sees. A green marker is not sufficient; open the PNG.
6. Gates: `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` + `UI_CAPTURE_OK` on **fresh** logs, judged by the marker.

## 8. What NOT to touch

- ⛔ **Do not hand-edit `Main_Castle_Overworld.unity`** (CLAUDE.md §3). The walls are not in it anyway (§1c).
- ⛔ **Do not "fix" `Assets/Resources/Walls/*`.** Verified green at source 2026-09-16 (§2). That is the raid path and it is not the defect.
- ⛔ **Do not add a second broken-shader predicate** (§5C).
- ⛔ **Do not re-inline the R2 push or verify into any script or doc** — call `tools\r2-ship.ps1` (CLAUDE.md §16).
- ⛔ **Do not strip any FlowTrace call** (CLAUDE.md §12). §2 is a case study in why: the missing evidence here is *suppressed*, not absent, and suppressing further would make the next occurrence unfindable.
- ⛔ **Do not touch the wave/balance code.** That is WO-1773.
- ⛔ **Do not chase the "2"/"3" chip** (§1e). Unidentified and immaterial.

---

## 9. Unverified claims in this document — stated plainly

1. **The build under test is unknown.** Nothing here is proven against the tester's binary.
2. **The tester is presumed to be "Sminer"** on circumstantial grounds only (see WO-1773 §1c). His save is not in Neon.
3. **The CAUSE is unproven.** §3 row 1 is strong (exact colour match + the mesh demonstrably loaded) but no log line from his device has been read. **That is precisely why this is NEEDS DATA.**
4. **The `playtest_break` payload schema was not inspected** (§4c) — whether those 396 events *could* carry this is unproven in both directions.
5. ~~The Addressable wall prefab's material state~~ — **RESOLVED, see §1f.** The chain is followed to the texture; the material's `_Albedo_Map` is **bound on disk**. What is still unproven is whether that texture **resolves at runtime on the tester's device** — which is the whole remaining question and is exactly what §4b's capture answers.
6. **Which of the six wall addressables the visible beige surface is** was not pinned (§1b) — though the wood tier is now known to be a *castle* wall, so it no longer argues against itself.
7. **Whether the Synty pack was present and current on the machine that produced the tester's content build** is unproven, and unprovable from here (§1f). It is the concrete mechanism behind §3 row 3.
8. **The "2"/"3" world-space chip is unidentified.**
9. **"An Ogre flying over it" is refuted as a reading** (§1e), but what the winged shape at t ≈ 60 s actually is has not been established.
10. ⚠ **The census may be armed and still silent** — the beige tint sits at the `FlowTintLuminanceFloor = 0.6f` boundary (§5A). Verify the floor in the same change or (A) will produce a false clean.

---

## 10. Hand-back

- This WO: `WorkOrders/WORK_ORDER_1774_untextured_slab_in_town_courtyard_tester_video.md`
- Companion balance ticket: `WorkOrders/WORK_ORDER_1773_high_level_hero_takes_no_damage_in_town_waves.md`
- WO number **pre-assigned by the lead**; `CLI_LANES_WO_NUMBERS.md` was **not** touched by this lane.
- Board: regenerate with `python tools/board_build.py`.
