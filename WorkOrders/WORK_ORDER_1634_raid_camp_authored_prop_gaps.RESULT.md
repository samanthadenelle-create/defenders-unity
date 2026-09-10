# WO-1634 RESULT - the authored prop sets now carry what their own child WOs specified

**Status:** IMPLEMENTED - awaiting gate + bake (lane PROP-GAPS 2026-09-10)
**Lane:** PROP-GAPS, isolated worktree `D:\EoA\.claude\worktrees\agent-ac52944e70b8d2421`
**Base sha:** `5a65a7831` (ff-only from `refs/heads/dev`; tree clean at start)
**Scope kept:** DATA only, plus one new regression case. **No edit to `RaidBaseDresser.cs` or
`RaidBaseGenerator.cs`** (lane ARENA-WALL / the dresser's fallback retirement own those).

## 0. Files changed

| Path | Change |
|---|---|
| `Assets/Resources/Data/Canonical/scene-configs.json` | 9 rows added, 2 rows re-zoned (byte patch) |
| `Assets/StreamingAssets/Data/Canonical/scene-configs.json` | byte-identical twin copy |
| `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` | new `CaseAuthoredPropGaps` + `Run()` registration + header pin bullet |

## 1. ⚠ THE PACKS ARE NOT IN THIS WORKTREE - every token was verified in the main clone

`Assets/Models/KayKit/**` and `Assets/Synty/**` are **gitignored**, so a linked worktree gets an empty
tree for them (`ls` of all nine `RaidBaseDresser.KayFolders` returned 0 assets here). Every path below
was therefore verified in **`D:\EoA`**, which is where the bake runs. Proof taken this session: for all
11 assets, both the file **and its `.meta`** exist (an unimported asset would miss the `.meta` and
`AssetDatabase.LoadAssetAtPath` would return null even though `ls` finds the file).

**Shadowing also ruled out.** `LoadVisual` returns the FIRST hit and searches `SyntyFolders` in order -
`Castle`, `Props/BattleGround`, `Buildings`, `SiegeEngines`, `Props/Banners` - so a same-named prefab in
`Castle` or `Buildings` would silently win. Grepped both for `Camp_Firepit` / `Spit_Roaster` / `SM_Wep_`:
**no matches**, so the BattleGround / SiegeEngines paths cited below are the ones that resolve. Same
check across the nine `KayFolders` for the bare-name tokens: each lands in the folder named.

## 2. Per tier - tokens added, and the path each resolves to

Resolution order re-read at source: `RaidBaseDresser.LoadVisual` (`Assets/Editor/WallTools/RaidBaseDresser.cs:177-234`)
- explicit `Assets/` path -> the 9 `KayFolders` x `{.fbx,.gltf,.prefab}` -> the 5 `SyntyFolders` x `.prefab`
-> `StructureContent` -> `Resources.Load`. Every token below is a **bare filename** that lands in the
KayKit/Synty stage, so nothing depends on `Resources/Structures` (deleted - the cylinder-turret miss).

### Easy - `raider_camp_small` - the fire/cook cluster (WO-1609:105, was MISSING)

| Token added | count | zone | cover | Resolves to |
|---|---|---|---|---|
| `SM_Prop_Camp_Firepit_01` | 1 | Courtyard | false | `Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround/SM_Prop_Camp_Firepit_01.prefab` |
| `SM_Prop_Spit_Roaster_01` | 1 | Courtyard | false | `Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround/SM_Prop_Spit_Roaster_01.prefab` |
| `haybale` | 2 | Courtyard | **true** | `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props/haybale.fbx` |
| `torch_lit` | 2 | Courtyard | false | `Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/torch_lit.fbx` |

`cover:false` on the fire pieces and the torches is deliberate - WO-1609:105 says *"Visual only"*, and
`RaidBaseDresser` keeps colliders **only** on `cover:true` (`:816`, `:854-856`). The haybales are the
cover half of the cluster (something to stand behind), matching WO-1607 §6 "Cover stacks".

### Hard - `fortified_garrison` (WO-1610:110 / :112 / :113)

| Token added | count | zone | cover | Resolves to |
|---|---|---|---|---|
| `bucket_arrows` | 2 | Courtyard | true | `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props/bucket_arrows.fbx` |
| `cannonball_pallet` | 1 | Courtyard | true | `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props/cannonball_pallet.fbx` |
| `SM_Wep_Catapult_01` | 1 | Courtyard | true | `Assets/Synty/PolygonFantasyKingdom/Prefabs/SiegeEngines/SM_Wep_Catapult_01.prefab` |
| `SM_Wep_Ballista_Mobile_01` | 1 | Courtyard | true | `Assets/Synty/PolygonFantasyKingdom/Prefabs/SiegeEngines/SM_Wep_Ballista_Mobile_01.prefab` |
| `wall_broken` | 1 | Courtyard | true | `Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/wall_broken.fbx` |

- **Racks / ammo: 3 -> 6** (`weaponrack` 3 + `bucket_arrows` 2 + `cannonball_pallet` 1), the top of
  WO-1610:110's 4-6 band and all three of its token families. The pallet also pairs visually with the
  camp's three authored `tower_catapult` turrets.
- **Damage tell: 3 -> 4** (`rubble_large` 3 + `wall_broken` 1), exactly the count WO-1610:112 asks for,
  and the camp named *The Broken Garrison* finally carries a "broken" token. `sword_shield_broken` was
  **deliberately left out**: adding it would put the cluster at 5, over the authored ceiling.
- **Siege: 0 -> 2, authored as PROPS.** `SM_Wep_Catapult_01` and `SM_Wep_Ballista_Mobile_01` are rows in
  `raidDress.props`; **neither carries a `DefenseTower`**, and `towers[]` is untouched, so the tower DPS
  budget WO-1634 forbids retuning is unchanged. WO-1610:113's caveat ("count against the 7 turrets if
  they shoot") is satisfied by them not shooting. `Mobile_01` (not `Mounted_01`) is the free-standing
  wheeled ballista - `Mounted_01` expects a wall to sit on; the non-`Rigged` variants are used because
  the rigged twins carry animation rigs the bake has no driver for.
- **Kit register held at Synty/neutral** per the WO-1607 §4 per-camp table (*"Synty, not hexagon-red"*),
  which is the default WO-1634 §2 took. The two existing yard buildings (`barracks`,
  `House_Medieval_Medium`) were **not** swapped to the hexagon-red files WO-1610:108-109 names.

### Extreme - `mage_enclave` - zone move only, still spare (WO-1611:96-102)

| Token | count | zone before | zone after |
|---|---|---|---|
| `banner_white` | 4 | `Keep` | `Courtyard` |
| `torch_mounted` | 6 | `Keep` | `Courtyard` |

No token added, no count changed - WO-1611:96-98 keeps the enclave *"spare on purpose"*. The courtyard
census rises from 9 to 19 instances **purely by relocation**, so the colonnade WO-1611:100-102 describes
stops being 6 bare pillars while the camp's total stays 27.

**Residual, named not papered over:** `torch_mounted.fbx` is a **wall sconce** and the dresser has no
wall-mount path - it will seat on the ground inside the courtyard band (>= `CourtyardWallPad` 5.5 m
inside the wall line, `RaidBaseDresser.cs:41`). That is the same seating it already had in `Zone_Keep`,
so this is not a regression, but the owner will see free-standing torches rather than sconces on the
inner face. Fixing it needs a wall-mount placer in the dresser - **another lane's file**.

## 3. Expected bake lines (WO-1633 §2.4 per-config props line)

Baseline read from `WorkOrders/WORK_ORDER_1633_*.RESULT.md:107-109`. The authored sums at that commit
were **27 / 25 / 27**, identical to the placed counts logged - i.e. in that bake **no slot was rejected
by `AcceptPropSlot`**. So the expected new counts are the new authored sums, `<=` on any bake where the
lane / spire / turret / staging keep-out rejects a slot:

| Config | Was | Expect | Delta |
|---|---|---|---|
| `raider_camp_small` | `props 'raider_camp_small': 27 placed` | `33 placed` | +6 (firepit 1, spit roaster 1, haybale 2, torch_lit 2) |
| `fortified_garrison` | `props 'fortified_garrison': 25 placed` | `31 placed` | +6 (bucket_arrows 2, cannonball_pallet 1, catapult 1, ballista 1, wall_broken 1) |
| `mage_enclave` | `props 'mage_enclave': 27 placed` | `27 placed` | 0 - but the `[Flow:RaidBase] props 'mage_enclave' courtyard=` figure should rise 9 -> 19 |

`missing=0` must hold on all three `dressed '<id>' kit=... placed=... missing=0` lines - every one of the
11 tokens was proven present with its `.meta` in `D:\EoA` (§1). A non-zero `missing` on a fresh bake log
means a pack was not imported on the bake machine, not that a token was guessed.

## 4. Regression - `CaseAuthoredPropGaps`, RED-first

New case in `Assets/Editor/Regression/RaidBaseLayoutRegression.cs`, registered in `Run()` and pinned in
the file header. It is a **separate case**, not a bolt-on to `CaseCourtyardCoverRings`, so the WO-1633
cover-ring pins and the WO-1634 gap pins fail independently. Pins, all by token:

- **Easy** has >= 1 row whose token is in the fire set `{SM_Prop_Camp_Firepit_01, SM_Prop_Spit_Roaster_01, torch_lit, haybale, trough}`.
- **Hard** sums `{weaponrack, bucket_arrows, cannonball_pallet}` >= 4; carries `wall_broken` **or**
  `sword_shield_broken`; has >= 2 `SM_Wep_*` rows **in `raidDress.props`** and **zero** `SM_Wep_*` entries
  in `towers[]` (that second half is what stops a later seat promoting the emplacements into shooters).
- **Extreme**: no `banner_white` / `torch_mounted` row may sit in `zone: "Keep"`; and a **ceiling by token
  absence** - no `barracks` / `tent` / `building_tent_green` / `weaponrack` (WO-1611:107 verbatim). It is
  deliberately **not** a density floor, for the same reason `CaseCourtyardCoverRings` refuses one.

**RED-first proof without Unity** (this lane may not run a gate). Each assertion was replayed by a python
mirror against `git show HEAD:Assets/Resources/Data/Canonical/scene-configs.json` and against the edited
bytes:

```
--- HEAD scene-configs.json
notes: easy fire rows=0 hard racks=3 hard siege rows=0 hard broken tell=no
RESULT: RED - 6 failure(s)
   * Easy authors no fire/cook piece
   * Hard racks/ammo total 3 < 4
   * Hard authors neither wall_broken nor sword_shield_broken
   * Hard authors 0 siege prop row(s) - asks 2
   * Extreme leaves 'banner_white' in zone Keep
   * Extreme leaves 'torch_mounted' in zone Keep

--- Assets/Resources/Data/Canonical/scene-configs.json (edited)
notes: easy fire rows=4 hard racks=6 hard siege rows=2 hard broken tell=yes
RESULT: GREEN - 0 failures
```

The mirror is a scratchpad artifact, not repo content. **The C# case has NOT been executed** - no Unity
run happened in this lane; the suite's OK marker is the lead's to earn on a fresh log.

## 4b. Owner question - ONE, and a veto is a data swap

Easy's cook fire uses Synty `SM_Prop_Camp_Firepit_01` + `SM_Prop_Spit_Roaster_01` on the hexagon-green
camp. WO-1607 §4's Easy row permits Synty battleground **tents / spikes** as extras and a firepit is
literally neither, while WO-1609:105 named only KayKit fallbacks (`torch_lit`, `haybale`, `trough`)
because it assumed no fire prop existed. Both readings are supported. **Default taken:** a real cook
fire, with the spec's `haybale` + `torch_lit` authored alongside it. Say the word and the cluster falls
back to the KayKit pieces only - `CaseAuthoredPropGaps` pins the fire set as an **OR**, so a veto is a
two-row data edit and the test still passes untouched.

## 4c. Carried for the PROPS-RETIRE lane - WO-1635 acceptance #1, JSON half

The PROPS-RETIRE lane deleted the dresser's legacy `def.props.set` fallback and added
`RaidBaseLayoutRegression.CaseSinglePropAuthority`, which REDs while any raid row still authors a
non-empty legacy block. `fortified_garrison` was the last one - it carried
`"props": { "set": ["barracks"], "count": 1 }` **beside** a `"barracks"` row already in its
`raidDress.props`, i.e. the same building declared through two authorities. Set to
`{ "set": [], "count": 0 }`, the shape `raider_camp_small` and `mage_enclave` already use.

**This token is the WO-1635 acceptance #1 JSON half, landed with WO-1634's rows** because both edits
touch the same two twin files and the lanes are not file-disjoint. It is inert for the bake either way -
`ScatterProps` only consults `def.props.set` when `raidDress.props` is empty
(`RaidBaseDresser.cs:621`), and `fortified_garrison` authors 14 raidDress rows - so the expected
`31 placed` in §3 is unchanged. Post-edit scan: **no raid config carries a non-empty legacy
`props.set`.** Byte-safe: 361 LF / 361 CRLF before and after (a pure in-place swap, no line delta),
`json.load` parses, twins md5 `0bdd28526eaf844700462de32ead042c`.

## 5. JSON byte safety

- Patched as **bytes**, sliced by the byte offset of each `"id": "<config>"` so the row text
  `{ "token": "rubble_large", "count": 3, "zone": "Courtyard", "cover": true }` - which appears in
  more than one camp - could not hit the wrong config.
- Line endings: **352 LF / 352 CRLF before, 361 LF / 361 CRLF after** (+9 lines, all CRLF). No bare LF.
- `json.load` parses the result.
- Twins byte-identical at every step: `b869af60cb1eb73b8efaa0eadc3e3e4f` at base,
  `fd698cb46611fe8fcbacc1f417a275b1` after the prop rows, `0bdd28526eaf844700462de32ead042c` after the
  §4c legacy-block edit. Both files carry the same hash at each stage.
- No BOM (file starts `{\r\n`), unchanged.

## 6. `.cs` quality gate (lane half)

- `python tools/gate_brace.py Assets/Editor/Regression/RaidBaseLayoutRegression.cs` -> `GATE_BRACE_SUMMARY bad=0 of 1`, exit 0.
- NUL scan: 0 bytes of `\x00`. Raw brace count 61/61.
- No `$"..."` interpolation added (the file's existing convention, and the gate's blind spot).

## 7. NOT done in this lane, by instruction

No Unity, no bake, no compile gate, no regression run, no commit. `BOARD.html` not regenerated - the
lead regenerates and commits the Status flip in the same commit as the work.
