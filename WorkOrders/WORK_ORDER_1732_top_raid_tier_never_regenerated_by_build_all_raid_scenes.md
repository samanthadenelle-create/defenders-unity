# WO-1732 — The top raid tier is never regenerated: `BuildAllRaidScenes` omits `iron_bastion`

**Status:** DONE
**Silo:** World/Environment (raid scene generation) — edit-only lane, no Unity run
**Opened:** 2026-09-15
**Source:** Owner felt-test, device build `2026.09.15.370203`, F8 flag seq 5245, scene `RaidBase_IronBastion`

> Owner, verbatim: *"it works on the first raid tier but on the last raid tier does destroy doesnt
> show as destroyed can walk through."*

---

## 1. The defect

`RaidBaseGenerator.RaidConfigIds` was a hardcoded three-id array:

```csharp
private static readonly string[] RaidConfigIds =
    { "raider_camp_small", "fortified_garrison", "mage_enclave" };
```

`iron_bastion` was **missing**, although `Assets/Resources/Data/Canonical/scene-configs.json`
authors it (difficulty `Extreme`, sceneName `RaidBase_IronBastion`). So
`BuildAllRaidScenes` regenerated three of the four raid levels and the top tier stayed frozen
on an older builder.

Measured on disk 2026-09-15 (line counts off the serialized `.unity` files, this session):

| scene | `Wall_*` | `Ruin_Wall_*` | `RuinStep` |
|---|---|---|---|
| `RaidBase_raider_camp_small` | 58 | 58 | 58 |
| `RaidBase_fortified_garrison` | 118 | 118 | 118 |
| `RaidBase_mage_enclave` | 158 | 158 | 158 |
| **`RaidBase_IronBastion`** | **210** | **0** | **0** |

IronBastion still carries the pre-WO-1723 3.0 m wall partition and has **zero** rubble objects, so
WO-1723 Lane B's "one clad panel per WallSegment, rubble on collapse" visual never applies there.
The WO-1723 Lane A nav-bake exclusion **is** name-based and `RaidNavBake` **does** list
`RaidBase_IronBastion.unity`, so a destroyed wall there stops blocking while never looking
destroyed — exactly the owner's two-part symptom.

### The trap: the config id and the scene name differ

The config id is **`iron_bastion`**; the scene the game loads is **`RaidBase_IronBastion`** —
different casing *and* separator, because WO-1705 kept the scene path that already existed in
`EditorBuildSettings` and in saves. `BuildSceneFor` composed its destination as
`$"Assets/Scenes/RaidBase_{configId}.unity"`, so simply adding `"iron_bastion"` to the array would
have written a **new, never-loaded** `RaidBase_iron_bastion.unity` and left the live scene stale.

### It was known and still shipped

`WorkOrders/WORK_ORDER_1632_raid_arena_exterior_boundary_ring.RESULT.md:194` already records
*"RaidBase_IronBastion.unity will NOT gain a ring from this run"*. A note in a RESULT file is not a
gate. That is why this WO ships a regression, not only a fix.

---

## 2. Which scene the game actually loads — proven, not assumed

| step | file:line | evidence |
|---|---|---|
| The raid launch hands GoRaid the config's authored field | `Assets/_Modules/Village/Hero/RaidDeployVM.cs:443` | `DeNelle.Core.SceneRouter.GoRaid(_def.sceneName);` |
| …and so does the deploy screen's documented contract | `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:32` | `SceneRouter.GoRaid(def.sceneName)` |
| `sceneName` is a real authored field on the def | `Assets/_Modules/Village/World/SceneConfigCatalog.cs:155` | `public string sceneName;` |
| the authored value for the top tier | `Assets/Resources/Data/Canonical/scene-configs.json` | `iron_bastion` → `RaidBase_IronBastion` |
| the runtime constant agrees | `Assets/_Modules/Core/SceneRouter.cs:195` | `RaidBaseIronBastion = "RaidBase_IronBastion"` |
| the scene is registered **and enabled** | `ProjectSettings/EditorBuildSettings.asset:53-54` | `- enabled: 1` / `path: Assets/Scenes/RaidBase_IronBastion.unity` |

**Conclusion: `def.sceneName` is the single authority for the destination path.** The comment at
`RaidSelectionScreen.cs:452` calling IronBastion "registered DISABLED" is **stale** — the asset reads
`enabled: 1`, which is consistent with the owner having played it.

---

## 3. The fix

`Assets/Editor/WallTools/RaidBaseGenerator.cs`

1. **`RaidConfigIds` (hardcoded array) → `RaidConfigIdsFromCatalog()` (derived).** The raid set is
   read off `SceneConfigCatalog.All`, selecting every config whose authored `sceneName` starts with
   `RaidBase`. This is the **twin** of the runtime predicate at
   `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:501`, which already defines "is a
   raid tier" the same way.
   *A hand-kept list is the defect; an explicit id→path map would have been a second copy of
   `sceneName` and the same duplicated-state failure CLAUDE.md §2/§5/§8 describes. Derivation makes
   the mismatch impossible by construction.*
2. **New `ScenePathFor(def, configId)`** — the one resolver, returning
   `Assets/Scenes/{def.sceneName}.unity`, with a comment stating why the names differ and citing
   `RaidDeployVM.cs:443`.
3. **`BuildSceneFor` uses it**, resolves the config *before* building, and throws on an unknown
   config or a failed save instead of silently writing an empty scene.
4. **`BuildAllRaidScenes` throws on an empty derived set** rather than reporting a silent no-op bake.
5. **`BuildFinalRaidScene`'s hardcoded `"Assets/Scenes/RaidBase_IronBastion.unity"` literal removed**
   — it now calls the same resolver. That literal was the last remaining copy of the path.

---

## 4. The guard

`Assets/Editor/Regression/RaidSceneCoverageRegression.cs` — marker
`RAID_SCENE_COVERAGE_OK` / `RAID_SCENE_COVERAGE_FAIL`, tag `[raid-scene-coverage]`.
Registered in `Assets/Editor/Regression/DataRegression.cs:748-752`.

For every catalog-derived raid config:

| case | assertion |
|---|---|
| `[exists]` | the `.unity` the config names is on disk |
| `[registered]` | that exact path is an **enabled** entry in `EditorBuildSettings.asset` |
| `[partition]` | the scene carries one `Ruin_Wall_*` per `Wall_*`, and `> 0` — i.e. the CURRENT clad partition |
| `[bake-list]` | `RaidNavBake.cs` names exactly the catalog's raid set (both directions) |
| `[orphan]` | no `RaidBase_*.unity` sits on disk unclaimed by a config — catches "a generator wrote a second scene from the ID", which every case above would pass |

Pure file I/O — no PlayMode, no scene load, no bake. An empty raid set is a **failure**, never a
vacuous pass.

---

## 5. Other configs — deliberate, do NOT add

`village2_enemy_outpost` → sceneName `Village2`, and `player_outpost` → sceneName `PlayerOutpost`.
Neither is a `RaidBase_*` scene, so the derived predicate correctly excludes both. `Village2` is a
hand-built scene with its own controller (`Village2RaidController.cs:97` matches it by name). This
generator only ever writes `RaidBase_*` levels. **Their absence is correct, not a latent bug.**

---

## 6. Follow-up (NOT done here)

`Assets/Editor/WallTools/RaidWallContinuityRegression.cs:59` hardcodes the **same three ids**, so
`RAID_WALL_CONTINUITY_OK` has never exercised the top tier's geometry. Same shape of defect, third
copy of the list. Not changed in this lane: adding `iron_bastion` could fail the lead's gate for
geometry reasons this edit-only lane cannot test. Fix = derive from the catalog, as done here.

---

## 7. What the lead must run

1. `python tools/gate_brace.py` — already clean (see RESULT).
2. Compile gate → `COMPILE_GATE_OK`.
3. `DeNelle.Editor.DataRegression.RunAll` **BEFORE regenerating** — expect **`REGRESSION_FAIL`**
   naming `[raid-scene-coverage] ... [partition] 'iron_bastion' ... 210 Wall_* segment(s) but 0
   Ruin_Wall_*`. That is the guard proving it can fail, on the real tree.
4. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes` — now bakes **four** scenes; expect
   `iron_bastion` in the log line and `RaidBase_IronBastion.unity` rewritten (**not** a new
   `RaidBase_iron_bastion.unity`).
5. `DeNelle.Editor.RaidNavBake.BakeAll`.
6. `DataRegression.RunAll` again — expect `REGRESSION_OK <n>/<n>` with `[raid-scene-coverage]`
   passing and IronBastion reading walls == ruins > 0.
7. Owner felt-test on the top tier: a destroyed wall must now **look** destroyed.

## 8. What NOT to touch

`RaidBaseDresser.cs`, `WallSegment.cs`, `RaidAssaultAi.cs`, `TroopController.cs`, camera files,
`RaidWallContinuityRegression.cs` — other lanes / follow-up.
